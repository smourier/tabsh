namespace Tabsh.Interop;

// what is actually running inside a job, so that killing it can be a decision rather than a surprise.
internal static unsafe partial class JobInspector
{
    // NumberOfAssignedProcesses and NumberOfProcessIdsInList, then the list itself,
    // which a pointer sized alignment puts at eight bytes in on both architectures.
    private const int _headerSize = 8;
    private const int _maximumProcesses = 4096;
    private const int _maximumCommandLine = 64 * 1024;

    public static List<JobProcess> ProcessesIn(nint job)
    {
        var found = new List<JobProcess>();
        var capacity = 16;

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var size = _headerSize + (capacity * nint.Size);
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (!QueryInformationJobObject(job, JobObjectBasicProcessIdList, buffer, (uint)size, 0))
                {
                    // the count is filled in even when the buffer was too small, which is how it says how much to ask for.
                    var assigned = Marshal.ReadInt32(buffer, 0);
                    if (Marshal.GetLastPInvokeError() == (int)WIN32_ERROR.ERROR_MORE_DATA && assigned > capacity && assigned <= _maximumProcesses)
                    {
                        capacity = assigned + 8;
                        continue;
                    }

                    return found;
                }

                var count = Marshal.ReadInt32(buffer, 4);
                var identifiers = (nint*)(buffer + _headerSize);
                for (var i = 0; i < count; i++)
                {
                    var identifier = (uint)identifiers[i];
                    if (identifier != 0)
                    {
                        found.Add(Describe(identifier));
                    }
                }

                return found;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        return found;
    }

    public static JobProcess Describe(uint processId)
    {
        // the limited right is enough for both questions asked here,
        // and is the one an ordinary process is given for something running as another user.
        var process = Functions.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == 0)
        {
            var error = Marshal.GetLastPInvokeError();
            return new JobProcess(processId, string.Empty, null, error switch
            {
                (int)WIN32_ERROR.ERROR_ACCESS_DENIED => Res.ProcessElevated,
                (int)WIN32_ERROR.ERROR_INVALID_PARAMETER => Res.ProcessEnded,
                _ => string.Format(CultureInfo.CurrentCulture, Res.ProcessOpenFailed, error),
            });
        }

        try
        {
            return new JobProcess(processId, ImageOf(process), CommandLineOf(process));
        }
        finally
        {
            Functions.CloseHandle(process);
        }
    }

    private static string ImageOf(nint process)
    {
        var buffer = stackalloc char[_maximumPath];
        var size = (uint)_maximumPath;
        return QueryFullProcessImageNameW(process, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : string.Empty;
    }

    // ProcessCommandLineInformation arrived in Windows 8.1.
    // Anything older answers that it has never heard of the question, and the image path is then all there is.
    private static string? CommandLineOf(nint process)
    {
        NtQueryInformationProcess(process, ProcessCommandLineInformation, 0, 0, out var needed);
        if (needed == 0 || needed > _maximumCommandLine)
            return null;

        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (NtQueryInformationProcess(process, ProcessCommandLineInformation, buffer, needed, out _) != 0)
                return null;

            var text = *(UNICODE_STRING*)buffer;
            if (text.Buffer == 0 || text.Length == 0)
                return null;

            return new string((char*)text.Buffer, 0, text.Length / sizeof(char));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private const int _maximumPath = 1024;

#pragma warning disable IDE1006 // Naming Styles
    private const int JobObjectBasicProcessIdList = 3;
    private const int ProcessCommandLineInformation = 60;

    // written by Windows into a buffer of ours and read back through a pointer, so nothing here ever assigns it.
#pragma warning disable CS0649 // Field is never assigned to
    private struct UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public nint Buffer;
    }
#pragma warning restore CS0649 // Field is never assigned to
#pragma warning restore IDE1006 // Naming Styles

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryInformationJobObject(nint hJob, int JobObjectInformationClass, nint lpJobObjectInformation, uint cbJobObjectInformationLength, nint lpReturnLength);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryFullProcessImageNameW(nint hProcess, uint dwFlags, char* lpExeName, ref uint lpdwSize);

    [LibraryImport("ntdll")]
    private static partial int NtQueryInformationProcess(nint ProcessHandle, int ProcessInformationClass, nint ProcessInformation, uint ProcessInformationLength, out uint ReturnLength);

}
