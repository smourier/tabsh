namespace Tabsh.Interop;

// reads what a command cost, while its handles are still open.
// A job handle closes with the command, and once it has, the accounting for everything it ran is gone with it.
internal static unsafe partial class UsageInspector
{
    public static ResourceUsage Snapshot(nint process, nint job)
    {
        var accounting = default(JOBOBJECT_BASIC_AND_IO_ACCOUNTING_INFORMATION);
        var limits = default(JOBOBJECT_EXTENDED_LIMIT_INFORMATION);
        var tree = job != 0
            && QueryInformationJobObject(job, JobObjectBasicAndIoAccountingInformation, (nint)(&accounting), (uint)sizeof(JOBOBJECT_BASIC_AND_IO_ACCOUNTING_INFORMATION), 0)
            && QueryInformationJobObject(job, JobObjectExtendedLimitInformation, (nint)(&limits), (uint)sizeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION), 0);

        var memory = default(PROCESS_MEMORY_COUNTERS_EX);
        memory.cb = (uint)sizeof(PROCESS_MEMORY_COUNTERS_EX);
        var own = process != 0 && K32GetProcessMemoryInfo(process, (nint)(&memory), memory.cb);

        long userTime = 0;
        long kernelTime = 0;
        if (process != 0 && GetProcessTimes(process, out _, out _, out var kernel, out var user))
        {
            kernelTime = kernel;
            userTime = user;
        }

        return new ResourceUsage
        {
            HasTree = tree,
            TreeUserTime = TimeSpan.FromTicks(accounting.BasicInfo.TotalUserTime),
            TreeKernelTime = TimeSpan.FromTicks(accounting.BasicInfo.TotalKernelTime),
            TreeProcesses = accounting.BasicInfo.TotalProcesses,
            TreePageFaults = accounting.BasicInfo.TotalPageFaultCount,
            TreePeakMemory = limits.PeakJobMemoryUsed,
            TreePeakProcessMemory = limits.PeakProcessMemoryUsed,
            ReadBytes = accounting.IoInfo.ReadTransferCount,
            WriteBytes = accounting.IoInfo.WriteTransferCount,
            OtherBytes = accounting.IoInfo.OtherTransferCount,
            ReadOperations = accounting.IoInfo.ReadOperationCount,
            WriteOperations = accounting.IoInfo.WriteOperationCount,
            OtherOperations = accounting.IoInfo.OtherOperationCount,

            OwnUserTime = TimeSpan.FromTicks(userTime),
            OwnKernelTime = TimeSpan.FromTicks(kernelTime),
            OwnPeakWorkingSet = own ? memory.PeakWorkingSetSize : 0,
            OwnPeakPagefile = own ? memory.PeakPagefileUsage : 0,
            OwnPageFaults = own ? memory.PageFaultCount : 0,
        };
    }

#pragma warning disable IDE1006 // Naming Styles
#pragma warning disable CA1707 // Identifiers should not contain underscores
    private const int JobObjectBasicAndIoAccountingInformation = 8;
    private const int JobObjectExtendedLimitInformation = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_ACCOUNTING_INFORMATION
    {
        public long TotalUserTime;
        public long TotalKernelTime;
        public long ThisPeriodTotalUserTime;
        public long ThisPeriodTotalKernelTime;
        public uint TotalPageFaultCount;
        public uint TotalProcesses;
        public uint ActiveProcesses;
        public uint TotalTerminatedProcesses;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_AND_IO_ACCOUNTING_INFORMATION
    {
        public JOBOBJECT_BASIC_ACCOUNTING_INFORMATION BasicInfo;
        public IO_COUNTERS IoInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_MEMORY_COUNTERS_EX
    {
        public uint cb;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
        public nuint PrivateUsage;
    }

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryInformationJobObject(nint hJob, int JobObjectInformationClass, nint lpJobObjectInformation, uint cbJobObjectInformationLength, nint lpReturnLength);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool K32GetProcessMemoryInfo(nint Process, nint ppsmemCounters, uint cb);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessTimes(nint hProcess, out long lpCreationTime, out long lpExitTime, out long lpKernelTime, out long lpUserTime);
#pragma warning restore CA1707
#pragma warning restore IDE1006
}
