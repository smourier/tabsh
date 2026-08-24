using DirectN.Extensions.Utilities;

namespace Tabsh;

internal static class MeasureCommands
{
    private const int _sampleMilliseconds = 200;
    private const int _labelWidth = 20;

    public static int Measure(BuiltinContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var ownOnly = false;
        var words = new List<string>();
        foreach (var argument in context.Arguments)
        {
            // only the switches before the command are ours, everything after the first word belongs to it.
            if (words.Count == 0 && argument.StartsWith('/'))
            {
                switch (char.ToUpperInvariant(argument.Length > 1 ? argument[1] : ' '))
                {
                    case 'P':
                        ownOnly = true;
                        continue;

                    default:
                        return context.Fail(string.Format(CultureInfo.CurrentCulture, Res.InvalidSwitch, argument));
                }
            }

            words.Add(argument);
        }

        if (words.Count == 0)
            return context.Fail(Res.CommandExpected);

        var measured = new List<ResourceUsage>();
        var stopwatch = Stopwatch.StartNew();

        int code;
        using (var sampler = GpuSampler.Open())
        {
            using var watcher = Watch(context, sampler);
            using (context.Shell.Executor.Measure(measured))
            {
                code = context.Shell.ExecuteLine(CommandLineBuilder.Build(words));
            }

            stopwatch.Stop();
            watcher.Set();

            Attribute(measured, sampler);
        }

        Report(context, stopwatch.Elapsed, code, measured, ownOnly);
        return code;
    }

    // a GPU counter disappears with the process that owned it, so it is read while the command is still running.
    private static ManualResetEventSlim Watch(BuiltinContext context, GpuSampler? sampler)
    {
        var stop = new ManualResetEventSlim(false);
        if (sampler == null)
            return stop;

        var thread = new Thread(() =>
        {
            while (!stop.Wait(_sampleMilliseconds))
            {
                sampler.Sample(Alive(context));
            }

            sampler.Sample(Alive(context));
        })
        { IsBackground = true, Name = "tabsh gpu sampler" };

        thread.Start();
        return stop;
    }

    private static List<uint> Alive(BuiltinContext context)
    {
        var ids = new List<uint>();
        foreach (var child in context.Shell.Executor.RunningChildren())
        {
            foreach (var process in child.Running())
            {
                ids.Add(process.ProcessId);
            }
        }

        return ids;
    }

    private static void Attribute(List<ResourceUsage> measured, GpuSampler? sampler)
    {
        if (sampler == null || measured.Count == 0)
            return;

        // the sampler watched the whole line, so what it saw is put against the line rather than against one stage.
        measured[0].HasGpu = true;
        measured[0].GpuTime = sampler.Total();
        measured[0].GpuPeakMemory = sampler.PeakMemory;
    }

    private static void Report(BuiltinContext context, TimeSpan elapsed, int code, List<ResourceUsage> measured, bool ownOnly)
    {
        context.Output.WriteLine();
        Row(context, Res.LabelElapsed, Duration(elapsed));
        Row(context, Res.LabelExitCode, code.ToString(CultureInfo.CurrentCulture));

        if (measured.Count == 0)
        {
            // a built in, or something that never became a process, has no accounting of its own to show.
            context.Output.WriteLine(Res.NothingMeasured);
            return;
        }

        var tree = !ownOnly && measured.TrueForAll(m => m.HasTree);
        var user = TimeSpan.Zero;
        var kernel = TimeSpan.Zero;
        var gpu = TimeSpan.Zero;
        uint processes = 0;
        uint faults = 0;
        ulong peak = 0;
        ulong gpuMemory = 0;
        ulong read = 0;
        ulong written = 0;
        ulong other = 0;
        ulong operations = 0;

        foreach (var one in measured)
        {
            user += tree ? one.TreeUserTime : one.OwnUserTime;
            kernel += tree ? one.TreeKernelTime : one.OwnKernelTime;
            processes += tree ? one.TreeProcesses : 1;
            faults += tree ? one.TreePageFaults : one.OwnPageFaults;
            peak = Math.Max(peak, tree ? one.TreePeakMemory : one.OwnPeakWorkingSet);
            read += one.ReadBytes;
            written += one.WriteBytes;
            other += one.OtherBytes;
            operations += one.ReadOperations + one.WriteOperations + one.OtherOperations;

            if (one.HasGpu)
            {
                gpu += one.GpuTime;
                gpuMemory = Math.Max(gpuMemory, one.GpuPeakMemory);
            }
        }

        Row(context, Res.LabelProcesses, processes.ToString("N0", CultureInfo.CurrentCulture));
        Row(context, Res.LabelUserTime, Duration(user));
        Row(context, Res.LabelKernelTime, Duration(kernel));
        Row(context, Res.LabelCpuTime, Duration(user + kernel));

        if (elapsed > TimeSpan.Zero)
        {
            Row(context, Res.LabelCpuShare, string.Format(CultureInfo.CurrentCulture, Res.PercentValue, (user + kernel).TotalMilliseconds * 100 / elapsed.TotalMilliseconds));
        }

        Row(context, tree ? Res.LabelPeakJobMemory : Res.LabelPeakWorkingSet, Bytes(peak));
        Row(context, Res.LabelPageFaults, faults.ToString("N0", CultureInfo.CurrentCulture));

        if (gpu > TimeSpan.Zero || gpuMemory > 0)
        {
            Row(context, Res.LabelGpuTime, Duration(gpu));
            Row(context, Res.LabelGpuMemory, Bytes(gpuMemory));
        }

        if (tree)
        {
            Row(context, Res.LabelBytesRead, Bytes(read));
            Row(context, Res.LabelBytesWritten, Bytes(written));
            Row(context, Res.LabelBytesOther, Bytes(other));
            Row(context, Res.LabelOperations, operations.ToString("N0", CultureInfo.CurrentCulture));
        }

        // without a job there is only the process that was started, and whatever it went on to start is missing.
        if (!tree && !ownOnly)
        {
            context.Output.WriteLine();
            context.Output.WriteLine(Res.TreeNotMeasured);
        }
    }

    private static void Row(BuiltinContext context, string label, string value) =>
        context.Output.WriteLine(string.Format(CultureInfo.CurrentCulture, Res.SpecificationLine, label.PadRight(_labelWidth), value));

    private static string Duration(TimeSpan value) => value < TimeSpan.FromSeconds(1)
        ? string.Format(CultureInfo.CurrentCulture, Res.MillisecondValue, value.TotalMilliseconds)
        : string.Format(CultureInfo.CurrentCulture, Res.SecondValue, value.TotalSeconds);

    private static string Bytes(ulong size)
    {
        using var pwstr = new AllocPwstr(128 * 2);
        DirectN.Functions.StrFormatByteSizeW((long)size, pwstr, pwstr.SizeInChars);
        return pwstr.ToString() ?? size.ToString();
    }
}
