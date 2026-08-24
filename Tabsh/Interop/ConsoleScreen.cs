namespace Tabsh.Interop;

// a second screen buffer, so that a program still running keeps writing to the buffer it started with,
// and cannot scroll ours away. This is how the alternate screen is done on Windows, with no virtual terminal needed.
internal sealed unsafe partial class ConsoleScreen : IDisposable
{
    private nint _screen;
    private nint _previous;

    private ConsoleScreen(nint screen, nint previous)
    {
        _screen = screen;
        _previous = previous;
    }

    // null when there is no console to take over, which is every case where the output is not a console anyway.
    public static ConsoleScreen? Open()
    {
        nint previous;
        fixed (char* name = "CONOUT$")
        {
            previous = Functions.CreateFileW(
                new PWSTR { Value = (nint)name },
                (uint)(GENERIC_ACCESS_RIGHTS.GENERIC_READ | GENERIC_ACCESS_RIGHTS.GENERIC_WRITE),
                FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE,
                0,
                FILE_CREATION_DISPOSITION.OPEN_EXISTING,
                0,
                HANDLE.Null);
        }

        if (previous == _invalidHandle)
            return null;

        var screen = CreateConsoleScreenBuffer(
            (uint)(GENERIC_ACCESS_RIGHTS.GENERIC_READ | GENERIC_ACCESS_RIGHTS.GENERIC_WRITE),
            (uint)(FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE),
            0,
            CONSOLE_TEXTMODE_BUFFER,
            0);
        if (screen == _invalidHandle)
        {
            Functions.CloseHandle(previous);
            return null;
        }

        // a new buffer starts on the console's default colours, not the ones in use.
        if (GetConsoleScreenBufferInfo(previous, out var information))
        {
            SetConsoleTextAttribute(screen, information.wAttributes);
        }

        if (!SetConsoleActiveScreenBuffer(screen))
        {
            Functions.CloseHandle(screen);
            Functions.CloseHandle(previous);
            return null;
        }

        return new ConsoleScreen(screen, previous);
    }

    public void WriteLine(string text = "")
    {
        Write(text);
        Write(Environment.NewLine);
    }

    public void Write(string text)
    {
        if (_screen != _invalidHandle && text.Length > 0)
        {
            WriteConsoleW(_screen, text, (uint)text.Length, out _, 0);
        }
    }

    public void Dispose()
    {
        var screen = Interlocked.Exchange(ref _screen, _invalidHandle);
        var previous = Interlocked.Exchange(ref _previous, _invalidHandle);

        // the screen goes back to what was on it, which is where everything that kept running has been writing.
        if (previous != _invalidHandle)
        {
            SetConsoleActiveScreenBuffer(previous);
            Functions.CloseHandle(previous);
        }

        if (screen != _invalidHandle)
        {
            Functions.CloseHandle(screen);
        }
    }

    private const nint _invalidHandle = -1;

#pragma warning disable IDE1006 // Naming Styles
    private const uint CONSOLE_TEXTMODE_BUFFER = 1;

    // written by Windows, never by us.
#pragma warning disable CS0649 // Field is never assigned to
    private struct SMALL_RECT
    {
        public short Left;
        public short Top;
        public short Right;
        public short Bottom;
    }

    private struct CONSOLE_SCREEN_BUFFER_INFO
    {
        public COORD dwSize;
        public COORD dwCursorPosition;
        public ushort wAttributes;
        public SMALL_RECT srWindow;
        public COORD dwMaximumWindowSize;
    }
#pragma warning restore CS0649 // Field is never assigned to
#pragma warning restore IDE1006 // Naming Styles

    [LibraryImport("kernel32", SetLastError = true)]
    private static partial nint CreateConsoleScreenBuffer(uint dwDesiredAccess, uint dwShareMode, nint lpSecurityAttributes, uint dwFlags, nint lpScreenBufferData);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleActiveScreenBuffer(nint hConsoleOutput);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetConsoleScreenBufferInfo(nint hConsoleOutput, out CONSOLE_SCREEN_BUFFER_INFO lpConsoleScreenBufferInfo);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleTextAttribute(nint hConsoleOutput, ushort wAttributes);

    [LibraryImport("kernel32", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WriteConsoleW(nint hConsoleOutput, string lpBuffer, uint nNumberOfCharsToWrite, out uint lpNumberOfCharsWritten, nint lpReserved);
}
