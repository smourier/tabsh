namespace Tabsh.Interop;

// what a command cost the graphics card, which no counter accumulates for you the way a job accumulates CPU time.
// The GPU counters live only as long as the process does, so they are sampled while it runs and totalled afterwards.
internal sealed unsafe partial class GpuSampler : IDisposable
{
    private const string _runningTimePath = @"\GPU Engine(*)\Running Time";
    private const string _dedicatedMemoryPath = @"\GPU Process Memory(*)\Dedicated Usage";
    private const string _instancePrefix = "pid_";
    private const int _instanceBufferBytes = 256 * 1024;

    // everything below is written by the sampling thread and read by the one that started it,
    // and disposing frees the buffer that a sample is reading, so the two are never allowed to overlap.
    private readonly Lock _gate = new();
    private readonly Dictionary<string, long> _first = [];
    private readonly Dictionary<string, long> _last = [];
    private readonly HashSet<uint> _seen = [];
    private nint _query;
    private nint _engine;
    private nint _memory;
    private nint _buffer;
    private long _peakMemory;

    public static GpuSampler? Open()
    {
        var sampler = new GpuSampler();
        if (sampler.Start())
            return sampler;

        sampler.Dispose();
        return null;
    }

    private bool Start()
    {
        if (PdhOpenQueryW(0, 0, out _query) != _success)
            return false;

        // the English path whatever the machine's language, since a counter name is translated and this one is not.
        fixed (char* engine = _runningTimePath)
        fixed (char* memory = _dedicatedMemoryPath)
        {
            if (PdhAddEnglishCounterW(_query, engine, 0, out _engine) != _success)
                return false;

            PdhAddEnglishCounterW(_query, memory, 0, out _memory);
        }

        _buffer = Marshal.AllocHGlobal(_instanceBufferBytes);
        return PdhCollectQueryData(_query) == _success;
    }

    // the processes worth counting, which changes as a command starts and loses children.
    public void Sample(IEnumerable<uint> processIds)
    {
        ArgumentNullException.ThrowIfNull(processIds);

        lock (_gate)
        {
            if (_query == 0)
                return;

            foreach (var id in processIds)
            {
                _seen.Add(id);
            }

            if (PdhCollectQueryData(_query) != _success)
                return;

            Accumulate();
            Peak();
        }
    }

    // running time is cumulative from the moment the process started,
    // so what this command cost is its growth between the first sample and the last, over every engine of the tree.
    private void Accumulate()
    {
        foreach (var item in Read(_engine))
        {
            if (!Belongs(item.Key))
                continue;

            _first.TryAdd(item.Key, item.Value);
            _last[item.Key] = item.Value;
        }
    }

    private void Peak()
    {
        if (_memory == 0)
            return;

        long total = 0;
        foreach (var item in Read(_memory))
        {
            if (Belongs(item.Key))
            {
                total += item.Value;
            }
        }

        if (total > _peakMemory)
        {
            _peakMemory = total;
        }
    }

    public TimeSpan Total()
    {
        long ticks = 0;
        lock (_gate)
        {
            foreach (var pair in _last)
            {
                ticks += pair.Value - _first[pair.Key];
            }
        }

        return TimeSpan.FromTicks(ticks);
    }

    public ulong PeakMemory
    {
        get
        {
            lock (_gate)
            {
                return (ulong)_peakMemory;
            }
        }
    }

    // an instance is named pid_1234_luid_..., so the process it belongs to is written on the front of it.
    private bool Belongs(string instance)
    {
        if (!instance.StartsWith(_instancePrefix, StringComparison.Ordinal))
            return false;

        var end = instance.IndexOf('_', _instancePrefix.Length);
        if (end < 0)
            return false;

        return uint.TryParse(instance.AsSpan(_instancePrefix.Length, end - _instancePrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var id) && _seen.Contains(id);
    }

    // the raw value rather than a formatted one, because Running Time is a total and formatting it would make a rate.
    private List<KeyValuePair<string, long>> Read(nint counter)
    {
        var values = new List<KeyValuePair<string, long>>();
        if (counter == 0 || _buffer == 0)
            return values;

        var size = (uint)_instanceBufferBytes;
        if (PdhGetRawCounterArrayW(counter, ref size, out var count, _buffer) != _success)
            return values;

        var items = (PDH_RAW_COUNTER_ITEM_W*)_buffer;
        for (var i = 0; i < count; i++)
        {
            var name = Marshal.PtrToStringUni(items[i].szName);
            if (name != null && items[i].RawValue.CStatus == _success)
            {
                values.Add(new KeyValuePair<string, long>(name, items[i].RawValue.FirstValue));
            }
        }

        return values;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_query != 0)
            {
                PdhCloseQuery(_query);
                _query = 0;
            }

            if (_buffer != 0)
            {
                Marshal.FreeHGlobal(_buffer);
                _buffer = 0;
            }
        }
    }

#pragma warning disable IDE1006 // Naming Styles
#pragma warning disable CA1707 // Identifiers should not contain underscores
    private const int _success = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct PDH_RAW_COUNTER
    {
        public uint CStatus;
        public long TimeStamp;
        public long FirstValue;
        public long SecondValue;
        public uint MultiCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PDH_RAW_COUNTER_ITEM_W
    {
        public nint szName;
        public PDH_RAW_COUNTER RawValue;
    }

    [LibraryImport("pdh")]
    private static partial int PdhOpenQueryW(nint szDataSource, nuint dwUserData, out nint phQuery);

    [LibraryImport("pdh")]
    private static partial int PdhAddEnglishCounterW(nint hQuery, char* szFullCounterPath, nuint dwUserData, out nint phCounter);

    [LibraryImport("pdh")]
    private static partial int PdhCollectQueryData(nint hQuery);

    [LibraryImport("pdh")]
    private static partial int PdhGetRawCounterArrayW(nint hCounter, ref uint lpdwBufferSize, out uint lpdwItemCount, nint ItemBuffer);

    [LibraryImport("pdh")]
    private static partial int PdhCloseQuery(nint hQuery);
#pragma warning restore CA1707
#pragma warning restore IDE1006
}
