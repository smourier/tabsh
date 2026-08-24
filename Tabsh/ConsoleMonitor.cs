namespace Tabsh;

// repaints a command from the top of the screen until Ctrl+C, for the things worth watching change.
internal static class ConsoleMonitor
{
    public const int DefaultSeconds = 1;
    private const int _minimumWidth = 20;
    private const int _pollMilliseconds = 100;

    public static int Run(BuiltinContext context, int seconds, Func<TextWriter, int> render, Func<ConsoleKeyInfo, bool>? onKey = null, string? keys = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(render);

        // WritesToConsole only says the command kept the shell's own handle,
        // which is a pipe when the shell itself was redirected, so a real console is asked about separately.
        if (Console.IsOutputRedirected || !context.WritesToConsole)
            return context.Fail(Res.MonitorNeedsConsole);

        // the shell cancels Ctrl+C and only acts on it when a child is running,
        // so a built in that wants to be stopped by it has to listen for itself.
        var stop = new ManualResetEventSlim(false);
        void cancel(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            stop.Set();
        }

        Console.CancelKeyPress += cancel;
        var code = 0;
        try
        {
            Console.Clear();
            var painted = 0;
            do
            {
                var writer = new StringWriter(CultureInfo.CurrentCulture);
                var now = DateTime.Now.ToString("T", CultureInfo.CurrentCulture);
                writer.WriteLine(keys == null
                    ? string.Format(CultureInfo.CurrentCulture, Res.MonitorHeader, now)
                    : string.Format(CultureInfo.CurrentCulture, Res.MonitorHeaderWithKey, now, keys));
                code = render(writer);
                painted = Paint(writer.ToString(), painted);
            }
            while (!Idle(stop, seconds, onKey, ref painted));
        }
        catch (Exception exception) when (exception is IOException or ArgumentOutOfRangeException)
        {
            // the window went away or was resized out from under the cursor maths, which is not worth a message.
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
            Console.CursorVisible = true;
        }

        return code;
    }

    // the wait between repaints, cut short by a key so that what is on screen can be acted on while it is still true.
    private static bool Idle(ManualResetEventSlim stop, int seconds, Func<ConsoleKeyInfo, bool>? onKey, ref int painted)
    {
        if (onKey == null)
            return stop.Wait(TimeSpan.FromSeconds(seconds));

        var until = TimeSpan.FromSeconds(seconds);
        while (until > TimeSpan.Zero)
        {
            if (stop.Wait(_pollMilliseconds))
                return true;

            until -= TimeSpan.FromMilliseconds(_pollMilliseconds);
            if (!Console.KeyAvailable)
                continue;

            var key = Console.ReadKey(intercept: true);
            Console.CursorVisible = true;

            // whatever it writes belongs under the listing it is about, and none of it belongs on the next pass,
            // so the screen is taken back afterwards rather than before.
            Below(painted);
            var carryOn = onKey(key);
            Console.Clear();
            painted = 0;
            return !carryOn;
        }

        return false;
    }

    private static void Below(int painted)
    {
        try
        {
            Console.SetCursorPosition(0, Math.Min(painted, Math.Max(Console.WindowHeight - 1, 0)));
        }
        catch (ArgumentOutOfRangeException)
        {
            // the window shrank under the maths, and where the cursor lands matters less than not throwing here.
        }
    }

    // written over what was there rather than cleared first, which is what stops a monitor flickering.
    // Whatever the last pass left below the new text is wiped, or a shorter listing keeps the tail of a longer one.
    private static int Paint(string text, int painted)
    {
        var width = Math.Max(Console.WindowWidth - 1, _minimumWidth);
        var lines = text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

        Console.CursorVisible = false;
        Console.SetCursorPosition(0, 0);
        foreach (var line in lines)
        {
            Console.WriteLine(Trim(line, width).PadRight(width));
        }

        for (var i = lines.Length; i < painted; i++)
        {
            Console.WriteLine(new string(' ', width));
        }

        return lines.Length;
    }

    private static string Trim(string line, int width) => line.Length <= width ? line : line[..width];

    // "/m" on its own, or "/m:5" for a slower one. The slash and the letter are the caller's, not part of the value.
    public static bool TryParseInterval(string option, out int seconds)
    {
        ArgumentNullException.ThrowIfNull(option);

        var text = option.Length > 2 ? option[2..].TrimStart(':') : string.Empty;
        if (text.Length == 0)
        {
            seconds = DefaultSeconds;
            return true;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.CurrentCulture, out seconds) && seconds > 0;
    }
}
