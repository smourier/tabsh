namespace Tabsh.Interop;

// a command with no redirection inherits the console outright,
// which is what makes full screen programs, colours and Ctrl+C behave as they do under cmd.
internal static unsafe partial class ProcessLauncher
{
    // without the explicit list the write end of a pipe leaks into the next stage of the same pipeline,
    // the reader never sees end of file and "a | b" hangs forever.
    public static ChildProcess Start(string commandLine, string? workingDirectory, string? environmentBlock, in StandardHandles handles, bool redirected, bool newConsole)
    {
        ArgumentNullException.ThrowIfNull(commandLine);

        // CreateProcessW may write to the command line buffer,
        // and an AOT image can place a literal in read-only memory, so it gets a copy.
        var mutableCommandLine = new char[commandLine.Length + 1];
        commandLine.CopyTo(mutableCommandLine);

        var startupInfo = new STARTUPINFOEXW();
        startupInfo.StartupInfo.cb = (uint)sizeof(STARTUPINFOEXW);

        // suspended so that it is in the job before it runs an instruction,
        // assigning afterwards is a race the child can win.
        var creationFlags = CREATE_UNICODE_ENVIRONMENT | CREATE_SUSPENDED;
        if (newConsole)
        {
            creationFlags |= CREATE_NEW_CONSOLE;
        }

        nint attributeList = 0;
        nint handleValues = 0;
        try
        {
            if (redirected)
            {
                MakeInheritable(handles.Input);
                MakeInheritable(handles.Output);
                MakeInheritable(handles.Error);

                startupInfo.StartupInfo.dwFlags = STARTF_USESTDHANDLES;
                startupInfo.StartupInfo.hStdInput = handles.Input;
                startupInfo.StartupInfo.hStdOutput = handles.Output;
                startupInfo.StartupInfo.hStdError = handles.Error;

                // CreateProcess reads the list, not UpdateProcThreadAttribute, so it cannot live on our stack.
                handleValues = AllocateHandleValues(handles, out var handleCount);
                attributeList = CreateAttributeList(handleValues, handleCount);
                startupInfo.lpAttributeList = attributeList;
                creationFlags |= EXTENDED_STARTUPINFO_PRESENT;
            }

            PROCESS_INFORMATION information;
            bool created;
            fixed (char* commandLinePointer = mutableCommandLine)
            fixed (char* environmentPointer = environmentBlock)
            fixed (char* directoryPointer = workingDirectory)
            {
                created = CreateProcessW(
                    null,
                    commandLinePointer,
                    0,
                    0,
                    redirected,
                    creationFlags,
                    environmentPointer,
                    directoryPointer,
                    &startupInfo,
                    &information);
            }

            if (!created)
                throw Failed(Marshal.GetLastPInvokeError(), commandLine);

            var job = Capture(information.hProcess);

            // whatever happened to the job, the child has to be let go, or it hangs there suspended forever.
            ResumeThread(information.hThread);

            return new ChildProcess(information.hProcess, information.hThread, information.dwProcessId, job);
        }
        finally
        {
            if (attributeList != 0)
            {
                DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
            }

            if (handleValues != 0)
            {
                Marshal.FreeHGlobal(handleValues);
            }
        }
    }

    // several of these messages carry a "%1" where the name of the program belongs, and nothing fills it in.
    // cmd names the file it could not run, and a message reading "%1 is not a valid Win32 application" names nothing.
    private static Win32Exception Failed(int error, string commandLine)
    {
        var message = new Win32Exception(error).Message;
        if (!message.Contains(_messageInsert, StringComparison.Ordinal))
            return new Win32Exception(error);

        return new Win32Exception(error, message.Replace(_messageInsert, FirstWord(commandLine), StringComparison.Ordinal));
    }

    // the program out of the command line, which is quoted there whenever it holds a space.
    private static string FirstWord(string commandLine)
    {
        var text = commandLine.TrimStart();
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            return end > 0 ? text[1..end] : text;
        }

        var space = text.IndexOf(' ');
        return space < 0 ? text : text[..space];
    }

    private const string _messageInsert = "%1";

    // 0 when no job could be arranged, which fails on Windows 7 inside a job since jobs did not nest until 8.
    // The child runs either way, it just cannot be killed as a tree.
    private static nint Capture(nint process)
    {
        var job = CreateJobObjectW(0, null);
        if (job == 0)
            return 0;

        if (AssignProcessToJobObject(job, process))
            return job;

        Functions.CloseHandle(job);
        return 0;
    }

    // duplicates are rejected by UpdateProcThreadAttribute, and the three standard handles are very often the same one.
    private static nint AllocateHandleValues(in StandardHandles handles, out int count)
    {
        var values = (nint*)Marshal.AllocHGlobal(3 * nint.Size);
        count = 0;
        foreach (var handle in (ReadOnlySpan<nint>)[handles.Input, handles.Output, handles.Error])
        {
            if (handle == 0 || handle == -1)
                continue;

            var seen = false;
            for (var i = 0; i < count; i++)
            {
                if (values[i] == handle)
                {
                    seen = true;
                    break;
                }
            }

            if (!seen)
            {
                values[count] = handle;
                count++;
            }
        }

        return (nint)values;
    }

    private static nint CreateAttributeList(nint handleValues, int handleCount)
    {
        nint size = 0;
        InitializeProcThreadAttributeList(0, 1, 0, ref size);
        var list = Marshal.AllocHGlobal(size);
        if (!InitializeProcThreadAttributeList(list, 1, 0, ref size))
        {
            var error = Marshal.GetLastPInvokeError();
            Marshal.FreeHGlobal(list);
            throw new Win32Exception(error);
        }

        if (!UpdateProcThreadAttribute(list, 0, PROC_THREAD_ATTRIBUTE_HANDLE_LIST, handleValues, handleCount * nint.Size, 0, 0))
        {
            var error = Marshal.GetLastPInvokeError();
            DeleteProcThreadAttributeList(list);
            Marshal.FreeHGlobal(list);
            throw new Win32Exception(error);
        }

        return list;
    }

    private static void MakeInheritable(nint handle)
    {
        if (handle == 0 || handle == -1)
            return;

        SetHandleInformation(handle, HANDLE_FLAG_INHERIT, HANDLE_FLAG_INHERIT);
    }

#pragma warning disable IDE1006 // Naming Styles
    private const uint CREATE_NEW_CONSOLE = 0x00000010;
    private const uint CREATE_SUSPENDED = 0x00000004;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    private const uint STARTF_USESTDHANDLES = 0x00000100;
    private const uint HANDLE_FLAG_INHERIT = 0x00000001;
    private const nint PROC_THREAD_ATTRIBUTE_HANDLE_LIST = 0x00020002;
#pragma warning restore IDE1006 // Naming Styles

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateProcessW(
        char* lpApplicationName,
        char* lpCommandLine,
        nint lpProcessAttributes,
        nint lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        uint dwCreationFlags,
        char* lpEnvironment,
        char* lpCurrentDirectory,
        STARTUPINFOEXW* lpStartupInfo,
        PROCESS_INFORMATION* lpProcessInformation);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InitializeProcThreadAttributeList(nint lpAttributeList, uint dwAttributeCount, uint dwFlags, ref nint lpSize);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateProcThreadAttribute(nint lpAttributeList, uint dwFlags, nint attribute, nint lpValue, nint cbSize, nint lpPreviousValue, nint lpReturnSize);

    [LibraryImport("kernel32", SetLastError = true)]
    private static partial void DeleteProcThreadAttributeList(nint lpAttributeList);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetHandleInformation(nint hObject, uint dwMask, uint dwFlags);

    [LibraryImport("kernel32", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateJobObjectW(nint lpJobAttributes, string? lpName);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(nint hJob, nint hProcess);

    [LibraryImport("kernel32", SetLastError = true)]
    private static partial uint ResumeThread(nint hThread);

}
