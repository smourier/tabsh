namespace Tabsh.Interop;

#pragma warning disable IDE1006 // Naming Styles
internal struct STARTUPINFOW
{
    public uint cb;
    public nint lpReserved;
    public nint lpDesktop;
    public nint lpTitle;
    public uint dwX;
    public uint dwY;
    public uint dwXSize;
    public uint dwYSize;
    public uint dwXCountChars;
    public uint dwYCountChars;
    public uint dwFillAttribute;
    public uint dwFlags;
    public ushort wShowWindow;
    public ushort cbReserved2;
    public nint lpReserved2;
    public nint hStdInput;
    public nint hStdOutput;
    public nint hStdError;
}
#pragma warning restore IDE1006 // Naming Styles
