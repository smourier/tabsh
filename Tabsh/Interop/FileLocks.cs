namespace Tabsh.Interop;

// which processes are holding a file open, which Windows knows and has never had a command for.
// Restart Manager is the documented way to ask, and it needs no privilege to ask about a file.
internal static unsafe partial class FileLocks
{
    private const int _maximumAttempts = 4;

    public static List<JobProcess> Holding(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var found = new List<JobProcess>();

        // the key buffer is written by RmStartSession and has to be there even though nothing here reads it back.
        var key = stackalloc char[CCH_RM_SESSION_KEY + 1];
        if (RmStartSession(out var session, 0, key) != (int)WIN32_ERROR.ERROR_SUCCESS)
            return found;

        try
        {
            fixed (char* name = path)
            {
                var names = stackalloc nint[1];
                names[0] = (nint)name;
                if (RmRegisterResources(session, 1, (nint)names, 0, 0, 0, 0) != (int)WIN32_ERROR.ERROR_SUCCESS)
                    return found;
            }

            Collect(session, found);
        }
        finally
        {
            RmEndSession(session);
        }

        return found;
    }

    // closes, restarts or terminates what is holding the file, and says which error stopped it when one did.
    // Restart Manager does the first two, and an empty list of processes means everything holding the file.
    public static bool Act(string path, IReadOnlyList<uint> processIds, FileLockAction action, out int error)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(processIds);

        if (action == FileLockAction.Terminate)
            return Terminate(processIds, out error);

        error = (int)WIN32_ERROR.ERROR_SUCCESS;
        var key = stackalloc char[CCH_RM_SESSION_KEY + 1];
        if (!Start(out var session, key, out error))
            return false;

        try
        {
            // registering the chosen processes rather than the file, so shutting the session down reaches them alone.
            // Registering the file would take every holder with it.
            var chosen = processIds.Count == 0 ? [] : Unique(path, processIds);
            if (processIds.Count > 0 && chosen.Count == 0)
            {
                error = (int)WIN32_ERROR.ERROR_NOT_FOUND;
                return false;
            }

            if (!Register(session, path, chosen, out error))
                return false;

            error = RmShutdown(session, 0, 0);
            if (error != (int)WIN32_ERROR.ERROR_SUCCESS)
                return false;

            if (action == FileLockAction.Restart)
            {
                error = RmRestart(session, 0, 0);
            }

            return error == (int)WIN32_ERROR.ERROR_SUCCESS;
        }
        finally
        {
            RmEndSession(session);
        }
    }

    private static bool Start(out uint session, char* key, out int error)
    {
        error = RmStartSession(out session, 0, key);
        return error == (int)WIN32_ERROR.ERROR_SUCCESS;
    }

    private static bool Register(uint session, string path, List<RM_UNIQUE_PROCESS> chosen, out int error)
    {
        if (chosen.Count == 0)
        {
            fixed (char* name = path)
            {
                var names = stackalloc nint[1];
                names[0] = (nint)name;
                error = RmRegisterResources(session, 1, (nint)names, 0, 0, 0, 0);
                return error == (int)WIN32_ERROR.ERROR_SUCCESS;
            }
        }

        var applications = Marshal.AllocHGlobal(chosen.Count * sizeof(RM_UNIQUE_PROCESS));
        try
        {
            var entries = (RM_UNIQUE_PROCESS*)applications;
            for (var i = 0; i < chosen.Count; i++)
            {
                entries[i] = chosen[i];
            }

            error = RmRegisterResources(session, 0, 0, (uint)chosen.Count, applications, 0, 0);
            return error == (int)WIN32_ERROR.ERROR_SUCCESS;
        }
        finally
        {
            Marshal.FreeHGlobal(applications);
        }
    }

    // a process is named to Restart Manager by its id and the moment it started,
    // and only Restart Manager knows the start time it recorded, so it is asked again.
    private static List<RM_UNIQUE_PROCESS> Unique(string path, IReadOnlyList<uint> processIds)
    {
        var chosen = new List<RM_UNIQUE_PROCESS>();
        var key = stackalloc char[CCH_RM_SESSION_KEY + 1];
        if (RmStartSession(out var session, 0, key) != (int)WIN32_ERROR.ERROR_SUCCESS)
            return chosen;

        try
        {
            fixed (char* name = path)
            {
                var names = stackalloc nint[1];
                names[0] = (nint)name;
                if (RmRegisterResources(session, 1, (nint)names, 0, 0, 0, 0) != (int)WIN32_ERROR.ERROR_SUCCESS)
                    return chosen;
            }

            foreach (var entry in Entries(session))
            {
                if (processIds.Contains(entry.Process.dwProcessId))
                {
                    chosen.Add(entry.Process);
                }
            }
        }
        finally
        {
            RmEndSession(session);
        }

        return chosen;
    }

    private static bool Terminate(IReadOnlyList<uint> processIds, out int error)
    {
        error = (int)WIN32_ERROR.ERROR_SUCCESS;
        if (processIds.Count == 0)
        {
            // nothing named is nothing done, and saying otherwise would be a lie about a process still running.
            error = (int)WIN32_ERROR.ERROR_NOT_FOUND;
            return false;
        }

        var stopped = true;
        foreach (var processId in processIds)
        {
            var process = Functions.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_TERMINATE, false, processId);
            if (process == 0)
            {
                error = Marshal.GetLastPInvokeError();
                stopped = false;
                continue;
            }

            try
            {
                if (!TerminateProcess(process, _terminatedExitCode))
                {
                    error = Marshal.GetLastPInvokeError();
                    stopped = false;
                }
            }
            finally
            {
                Functions.CloseHandle(process);
            }
        }

        return stopped;
    }

    private static void Collect(uint session, List<JobProcess> found)
    {
        foreach (var entry in Entries(session))
        {
            var copy = entry;
            found.Add(Describe(&copy));
        }
    }

    // the count is a two call question, and the answer can grow between the two calls,
    // because the processes being asked about are running and free to start more.
    private static List<RM_PROCESS_INFO> Entries(uint session)
    {
        var list = new List<RM_PROCESS_INFO>();
        uint capacity = 0;
        for (var attempt = 0; attempt < _maximumAttempts; attempt++)
        {
            var count = capacity;
            var buffer = capacity == 0 ? 0 : Marshal.AllocHGlobal((int)(capacity * (uint)sizeof(RM_PROCESS_INFO)));
            try
            {
                var result = RmGetList(session, out var needed, ref count, buffer, out _);
                if (result == (int)WIN32_ERROR.ERROR_MORE_DATA)
                {
                    capacity = needed;
                    continue;
                }

                if (result != (int)WIN32_ERROR.ERROR_SUCCESS)
                    return list;

                var entries = (RM_PROCESS_INFO*)buffer;
                for (var i = 0; i < count; i++)
                {
                    list.Add(entries[i]);
                }

                return list;
            }
            finally
            {
                if (buffer != 0)
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }

        return list;
    }

    // Restart Manager gives a friendly name and nothing else,
    // so the process is asked directly and its own name only stands when it will not answer.
    private static JobProcess Describe(RM_PROCESS_INFO* entry)
    {
        var id = entry->Process.dwProcessId;
        var described = JobInspector.Describe(id);
        if (described.ImagePath.Length > 0)
            return described;

        var name = new string(entry->strAppName);
        if (name.Length == 0)
            return described;

        return new JobProcess(id, string.Empty, null, name);
    }

#pragma warning disable IDE1006 // Naming Styles
#pragma warning disable CA1707 // Identifiers should not contain underscores
    // what a process ended by this reports, the same as anything else stopped rather than finished.
    private const uint _terminatedExitCode = 1;

    private const int CCH_RM_SESSION_KEY = 32;
    private const int CCH_RM_MAX_APP_NAME = 255;
    private const int CCH_RM_MAX_SVC_NAME = 63;

    [StructLayout(LayoutKind.Sequential)]
    private struct RM_UNIQUE_PROCESS
    {
        public uint dwProcessId;
        public FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;
        public fixed char strAppName[CCH_RM_MAX_APP_NAME + 1];
        public fixed char strServiceShortName[CCH_RM_MAX_SVC_NAME + 1];
        public int ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        public int bRestartable;
    }

    [LibraryImport("rstrtmgr")]
    private static partial int RmStartSession(out uint pSessionHandle, uint dwSessionFlags, char* strSessionKey);

    [LibraryImport("rstrtmgr")]
    private static partial int RmRegisterResources(uint dwSessionHandle, uint nFiles, nint rgsFileNames, uint nApplications, nint rgApplications, uint nServices, nint rgsServiceNames);

    [LibraryImport("rstrtmgr")]
    private static partial int RmGetList(uint dwSessionHandle, out uint pnProcInfoNeeded, ref uint pnProcInfo, nint rgAffectedApps, out uint lpdwRebootReasons);

    [LibraryImport("rstrtmgr")]
    private static partial int RmEndSession(uint dwSessionHandle);

    [LibraryImport("rstrtmgr")]
    private static partial int RmShutdown(uint dwSessionHandle, uint lActionFlags, nint fnStatus);

    [LibraryImport("rstrtmgr")]
    private static partial int RmRestart(uint dwSessionHandle, uint dwRestartFlags, nint fnStatus);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateProcess(nint hProcess, uint uExitCode);
#pragma warning restore CA1707
#pragma warning restore IDE1006
}
