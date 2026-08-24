namespace Tabsh.Interop;

// what happens when the thing being run is not a program.
// Typing readme.md at the prompt should open it, and only the association database can answer that.
internal static unsafe partial class ShellExecutor
{
    // returns the child so the caller can wait on it, null when the verb was handled by an already running process,
    // which is what a second Explorer window or a document handed to an open editor looks like.
    public static ChildProcess? Execute(string file, string? arguments, string? workingDirectory, string? verb)
    {
        ArgumentNullException.ThrowIfNull(file);

        var information = new SHELLEXECUTEINFOW
        {
            cbSize = (uint)sizeof(SHELLEXECUTEINFOW),
            fMask = SEE_MASK_NOCLOSEPROCESS | SEE_MASK_FLAG_NO_UI | SEE_MASK_NOASYNC,
            nShow = SHOW_WINDOW_CMD.SW_SHOWNORMAL,
        };

        fixed (char* filePointer = file)
        fixed (char* argumentsPointer = arguments)
        fixed (char* directoryPointer = workingDirectory)
        fixed (char* verbPointer = verb)
        {
            information.lpFile = (nint)filePointer;
            information.lpParameters = (nint)argumentsPointer;
            information.lpDirectory = (nint)directoryPointer;
            information.lpVerb = (nint)verbPointer;

            if (!ShellExecuteExW(ref information))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        if (information.hProcess == 0)
            return null;

        return new ChildProcess(information.hProcess, 0, 0);
    }

#pragma warning disable IDE1006 // Naming Styles
    private const uint SEE_MASK_NOCLOSEPROCESS = 0x00000040;
    private const uint SEE_MASK_FLAG_NO_UI = 0x00000400;
    private const uint SEE_MASK_NOASYNC = 0x00000100;

    private struct SHELLEXECUTEINFOW
    {
        public uint cbSize;
        public uint fMask;
        public nint hwnd;
        public nint lpVerb;
        public nint lpFile;
        public nint lpParameters;
        public nint lpDirectory;
        public DirectN.SHOW_WINDOW_CMD nShow;
        public nint hInstApp;
        public nint lpIDList;
        public nint lpClass;
        public nint hkeyClass;
        public uint dwHotKey;
        public nint hIcon;
        public nint hProcess;
    }
#pragma warning restore IDE1006 // Naming Styles

    [LibraryImport("shell32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShellExecuteExW(ref SHELLEXECUTEINFOW lpExecInfo);
}
