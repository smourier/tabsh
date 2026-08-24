namespace Tabsh.Interop;

// the clipboard as text, which clip.exe can only write to and nothing shipped can read back.
// It is a shared resource any process may hold, so opening it is retried rather than treated as a failure.
internal static unsafe partial class ClipboardText
{
    private const int _attempts = 10;
    private const int _waitMilliseconds = 20;

    public static string? Read()
    {
        if (!Open())
            return null;

        try
        {
            if (!Functions.IsClipboardFormatAvailable(CF_UNICODETEXT))
                return null;

            // GetClipboardData answers with a HANDLE and GlobalLock wants an HGLOBAL, and nint is what joins them.
            nint handle = Functions.GetClipboardData(CF_UNICODETEXT);
            if (handle == 0)
                return null;

            var pointer = Functions.GlobalLock(handle);
            if (pointer == 0)
                return null;

            try
            {
                return Marshal.PtrToStringUni(pointer);
            }
            finally
            {
                Functions.GlobalUnlock(handle);
            }
        }
        finally
        {
            Functions.CloseClipboard();
        }
    }

    public static bool Write(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (!Open())
            return false;

        try
        {
            Functions.EmptyClipboard();

            var bytes = (nuint)((text.Length + 1) * sizeof(char));
            var memory = GlobalAlloc(GMEM_MOVEABLE, bytes);
            if (memory == 0)
                return false;

            var pointer = Functions.GlobalLock(memory);
            if (pointer == 0)
            {
                GlobalFree(memory);
                return false;
            }

            try
            {
                fixed (char* source = text)
                {
                    Buffer.MemoryCopy(source, (void*)pointer, (long)bytes, (long)text.Length * sizeof(char));
                }

                ((char*)pointer)[text.Length] = '\0';
            }
            finally
            {
                Functions.GlobalUnlock(memory);
            }

            // the block belongs to the clipboard once it has taken it, and stays ours when it has not.
            if (Functions.SetClipboardData(CF_UNICODETEXT, memory) == 0)
            {
                GlobalFree(memory);
                return false;
            }

            return true;
        }
        finally
        {
            Functions.CloseClipboard();
        }
    }

    public static bool Clear()
    {
        if (!Open())
            return false;

        try
        {
            return Functions.EmptyClipboard();
        }
        finally
        {
            Functions.CloseClipboard();
        }
    }

    private static bool Open()
    {
        for (var attempt = 0; attempt < _attempts; attempt++)
        {
            if (Functions.OpenClipboard(HWND.Null))
                return true;

            Thread.Sleep(_waitMilliseconds);
        }

        return false;
    }

#pragma warning disable IDE1006 // Naming Styles
#pragma warning disable CA1707 // Identifiers should not contain underscores
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    [LibraryImport("kernel32", SetLastError = true)]
    private static partial nint GlobalAlloc(uint uFlags, nuint dwBytes);

    [LibraryImport("kernel32", SetLastError = true)]
    private static partial nint GlobalFree(nint hMem);
#pragma warning restore CA1707
#pragma warning restore IDE1006
}
