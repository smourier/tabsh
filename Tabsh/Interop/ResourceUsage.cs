namespace Tabsh.Interop;

// what one command cost, read from its job where there is one and from the process itself either way.
internal sealed class ResourceUsage
{
    // a job accounts for every process that ever joined it, the ones that have already exited included,
    // which is the whole reason the tree numbers are worth anything.
    public bool HasTree { get; init; }
    public TimeSpan TreeUserTime { get; init; }
    public TimeSpan TreeKernelTime { get; init; }
    public uint TreeProcesses { get; init; }
    public uint TreePageFaults { get; init; }
    public ulong TreePeakMemory { get; init; }
    public ulong TreePeakProcessMemory { get; init; }
    public ulong ReadBytes { get; init; }
    public ulong WriteBytes { get; init; }
    public ulong OtherBytes { get; init; }
    public ulong ReadOperations { get; init; }
    public ulong WriteOperations { get; init; }
    public ulong OtherOperations { get; init; }

    public TimeSpan OwnUserTime { get; init; }
    public TimeSpan OwnKernelTime { get; init; }
    public ulong OwnPeakWorkingSet { get; init; }
    public ulong OwnPeakPagefile { get; init; }
    public uint OwnPageFaults { get; init; }

    // GPU time and memory are sampled while the command runs, so they are filled in by whoever was watching.
    public TimeSpan GpuTime { get; set; }
    public ulong GpuPeakMemory { get; set; }
    public bool HasGpu { get; set; }
}
