namespace Tabsh.Interop;

#pragma warning disable IDE1006 // Naming Styles
internal struct PROCESS_INFORMATION
{
    public nint hProcess;
    public nint hThread;
    public uint dwProcessId;
    public uint dwThreadId;
}
#pragma warning restore IDE1006 // Naming Styles
