namespace Tabsh.Interop;

// what every process currently has loaded, searched by name.
// A shell extension in explorer.exe lives somewhere no search of PATH will reach, and the processes know where.
internal static unsafe partial class LoadedModules
{
    public static List<LoadedModule> Find(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        var found = new List<LoadedModule>();
        var processes = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (processes == _invalidHandle)
            return found;

        try
        {
            var entry = default(PROCESSENTRY32W);
            entry.dwSize = (uint)sizeof(PROCESSENTRY32W);
            if (!Process32FirstW(processes, ref entry))
                return found;

            do
            {
                // a snapshot asked for process 0 is a snapshot of the calling process,
                // so the idle process would hand back our own modules under its own id.
                if (entry.th32ProcessID != 0)
                {
                    Search(entry.th32ProcessID, pattern, found);
                }
            }
            while (Process32NextW(processes, ref entry));
        }
        finally
        {
            Functions.CloseHandle(processes);
        }

        return found;
    }

    // both module lists, because a 32 bit process seen from here has its modules under the second flag,
    // and a process that will not be snapshotted at all is simply one this shell cannot see into.
    private static void Search(uint processId, string pattern, List<LoadedModule> found)
    {
        var modules = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, processId);
        if (modules == _invalidHandle)
            return;

        try
        {
            var entry = default(MODULEENTRY32W);
            entry.dwSize = (uint)sizeof(MODULEENTRY32W);
            if (!Module32FirstW(modules, ref entry))
                return;

            do
            {
                var name = new string(entry.szModule);
                if (name.Length > 0 && System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, name, ignoreCase: true))
                {
                    found.Add(new LoadedModule(processId, new string(entry.szExePath)));
                }
            }
            while (Module32NextW(modules, ref entry));
        }
        finally
        {
            Functions.CloseHandle(modules);
        }
    }

#pragma warning disable IDE1006 // Naming Styles
#pragma warning disable CA1707 // Identifiers should not contain underscores
    private const nint _invalidHandle = -1;
    private const uint TH32CS_SNAPPROCESS = 0x00000002;
    private const uint TH32CS_SNAPMODULE = 0x00000008;
    private const uint TH32CS_SNAPMODULE32 = 0x00000010;
    private const int MAX_PATH = 260;
    private const int MAX_MODULE_NAME32 = 255;

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESSENTRY32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public nuint th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        public fixed char szExeFile[MAX_PATH];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MODULEENTRY32W
    {
        public uint dwSize;
        public uint th32ModuleID;
        public uint th32ProcessID;
        public uint GlblcntUsage;
        public uint ProccntUsage;
        public nint modBaseAddr;
        public uint modBaseSize;
        public nint hModule;
        public fixed char szModule[MAX_MODULE_NAME32 + 1];
        public fixed char szExePath[MAX_PATH];
    }

    [LibraryImport("kernel32", SetLastError = true)]
    private static partial nint CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32FirstW(nint hSnapshot, ref PROCESSENTRY32W lppe);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32NextW(nint hSnapshot, ref PROCESSENTRY32W lppe);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Module32FirstW(nint hSnapshot, ref MODULEENTRY32W lpme);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Module32NextW(nint hSnapshot, ref MODULEENTRY32W lpme);
#pragma warning restore CA1707
#pragma warning restore IDE1006
}
