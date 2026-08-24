namespace Tabsh.Interop;

// the job is what makes a tree killable, terminating a batch file alone leaves the program it started behind.
// Deliberately no JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE, a command is allowed to leave a server running after it.
internal sealed partial class ChildProcess : IDisposable
{
    private readonly Lock _gate = new();
    private nint _process;
    private nint _thread;
    private nint _job;

    internal ChildProcess(nint process, nint thread, uint processId, nint job = 0)
    {
        _process = process;
        _thread = thread;
        _job = job;
        ProcessId = processId;
    }

    public uint ProcessId { get; }

    // read at the end of Wait, because Dispose closes the job and takes the accounting for its whole tree with it.
    public ResourceUsage? Usage { get; private set; }

    // Ctrl+C does not interrupt this, Windows delivers the console event on a thread of its own.
    public int Wait()
    {
        if (_process == 0)
            return -1;

        Functions.WaitForSingleObject(_process, INFINITE);

        lock (_gate)
        {
            Usage = UsageInspector.Snapshot(_process, _job);
        }

        if (!GetExitCodeProcess(_process, out var code))
            return -1;

        return unchecked((int)code);
    }

    // false on Windows 7 when this shell is itself inside a job, jobs did not nest before Windows 8.
    public bool TracksTree
    {
        get
        {
            lock (_gate)
            {
                return _job != 0;
            }
        }
    }

    // taken at the moment it is asked for, a process can always end between then and the answer.
    public IReadOnlyList<JobProcess> Running()
    {
        lock (_gate)
        {
            if (_job != 0)
                return JobInspector.ProcessesIn(_job);

            if (_process != 0)
                return [JobInspector.Describe(ProcessId)];

            return [];
        }
    }

    // answers whether there was a job to do it with. Without one only the child itself goes.
    public bool Terminate()
    {
        lock (_gate)
        {
            if (_job != 0)
                return TerminateJobObject(_job, STATUS_CONTROL_C_EXIT);

            if (_process != 0)
            {
                TerminateProcess(_process, STATUS_CONTROL_C_EXIT);
            }

            return false;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            Close(ref _thread);
            Close(ref _process);
            Close(ref _job);
        }
    }

    private static void Close(ref nint handle)
    {
        if (handle != 0)
        {
            Functions.CloseHandle(handle);
            handle = 0;
        }
    }

#pragma warning disable IDE1006 // Naming Styles
    private const uint INFINITE = 0xFFFFFFFF;

    // what Windows reports for a process ended by Ctrl+C, so one killed here looks like one that took the hint.
    private const uint STATUS_CONTROL_C_EXIT = 0xC000013A;
#pragma warning restore IDE1006 // Naming Styles

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetExitCodeProcess(nint hProcess, out uint lpExitCode);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateJobObject(nint hJob, uint uExitCode);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateProcess(nint hProcess, uint uExitCode);
}
