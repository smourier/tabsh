namespace Tabsh.Interop;

// console modes belong to the console and any program sharing it can leave them anywhere,
// so the shell forces what it needs and puts back what it found. CONIN$ and CONOUT$, not the standard handles.
internal static unsafe partial class ConsoleSession
{
    private static nint _input = _invalidHandle;
    private static nint _output = _invalidHandle;
    private static uint _originalInput;
    private static uint _originalOutput;
    private static uint _originalInputCodePage;
    private static uint _originalOutputCodePage;

    public static void Capture()
    {
        _input = OpenConsole("CONIN$");
        _output = OpenConsole("CONOUT$");

        if (!GetConsoleMode(_input, out _originalInput))
        {
            Close(ref _input);
        }

        if (!GetConsoleMode(_output, out _originalOutput))
        {
            Close(ref _output);
        }

        _originalInputCodePage = GetConsoleCP();
        _originalOutputCodePage = GetConsoleOutputCP();
    }

    // an OEM code page cannot write most of Unicode, so a file named in Chinese lists as "???".
    // The Console properties as well as the code pages, .NET builds its writers from the page in force at first use.
    public static void UseUnicode()
    {
        // the console's own pages, which is what the children inherit.
        SetConsoleCP(_unicodeCodePage);
        SetConsoleOutputCP(_unicodeCodePage);

        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (Exception exception) when (exception is IOException or System.Security.SecurityException)
        {
            // nothing to set it on, which is every case where the output is not a console.
        }

        try
        {
            // asking for this on a redirected input throws, and the console's own page is set above regardless.
            if (!Console.IsInputRedirected)
            {
                Console.InputEncoding = Encoding.UTF8;
            }
        }
        catch (Exception exception) when (exception is IOException or System.Security.SecurityException)
        {
            // continue, the output side is the half that shows.
        }
    }

    // ICU gives a good many cultures a no break space to group digits with, which a console cannot draw.
    // What matters in a size is the number, not which kind of space separates the groups.
    public static void UseWritableNumbers()
    {
        var culture = (CultureInfo)CultureInfo.CurrentCulture.Clone();
        culture.NumberFormat.NumberGroupSeparator = Plain(culture.NumberFormat.NumberGroupSeparator);
        culture.NumberFormat.CurrencyGroupSeparator = Plain(culture.NumberFormat.CurrencyGroupSeparator);
        culture.NumberFormat.PercentGroupSeparator = Plain(culture.NumberFormat.PercentGroupSeparator);

        CultureInfo.CurrentCulture = culture;

        // the pipeline runs a stage per thread, and each of them formats its own output.
        CultureInfo.DefaultThreadCurrentCulture = culture;
    }

    private static string Plain(string separator) => separator.Replace(_noBreakSpace, ' ').Replace(_narrowNoBreakSpace, ' ');

    // only what has to be certain is forced, quick edit and the rest are kept,
    // so this never argues with how the user configured their terminal.
    public static void Normalize()
    {
        if (_input != _invalidHandle && GetConsoleMode(_input, out var inputMode))
        {
            SetConsoleMode(_input, inputMode | ENABLE_PROCESSED_INPUT | ENABLE_LINE_INPUT | ENABLE_ECHO_INPUT);
        }

        if (_output != _invalidHandle && GetConsoleMode(_output, out var outputMode))
        {
            outputMode |= ENABLE_PROCESSED_OUTPUT | ENABLE_WRAP_AT_EOL_OUTPUT;

            // virtual terminal arrived in Windows 10,
            // and an unsupported flag fails the WHOLE call rather than being ignored, so the flags above go with it.
            VirtualTerminal = SetConsoleMode(_output, outputMode | ENABLE_VIRTUAL_TERMINAL_PROCESSING);
            if (!VirtualTerminal)
            {
                SetConsoleMode(_output, outputMode);
            }
        }
    }

    // false means escape sequences are printed rather than obeyed, whatever runs here.
    public static bool VirtualTerminal { get; private set; }

    // what the console is actually set to, which is the only way to tell why colour is or is not working.
    public static void Describe(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (_output == _invalidHandle || !GetConsoleMode(_output, out var outputMode))
        {
            writer.WriteLine(Res.NoConsole);
            return;
        }

        GetConsoleMode(_input, out var inputMode);
        Row(writer, Res.LabelConsoleInputMode, string.Format(CultureInfo.CurrentCulture, Res.ConsoleMode, inputMode));
        Row(writer, Res.LabelConsoleOutputMode, string.Format(CultureInfo.CurrentCulture, Res.ConsoleMode, outputMode));
        Row(writer, Res.LabelVirtualTerminal, (outputMode & ENABLE_VIRTUAL_TERMINAL_PROCESSING) != 0 ? Res.VirtualTerminalOn : Res.VirtualTerminalOff);
        Row(writer, Res.LabelCodePage, string.Format(CultureInfo.CurrentCulture, Res.CodePageLine, GetConsoleCP(), GetConsoleOutputCP()));

        if ((outputMode & ENABLE_VIRTUAL_TERMINAL_PROCESSING) == 0)
        {
            writer.WriteLine();
            writer.WriteLine(Res.VirtualTerminalHint);
        }
    }

    private static void Row(TextWriter writer, string label, string value) =>
        writer.WriteLine(string.Format(CultureInfo.CurrentCulture, Res.SpecificationLine, label.PadRight(_labelWidth), value));

    private const int _labelWidth = 20;

    // the console belonged to whoever started us, and it is given back as it was found.
    public static void Restore()
    {
        if (_originalOutputCodePage != 0)
        {
            SetConsoleOutputCP(_originalOutputCodePage);
        }

        if (_originalInputCodePage != 0)
        {
            SetConsoleCP(_originalInputCodePage);
        }

        if (_input != _invalidHandle)
        {
            SetConsoleMode(_input, _originalInput);
            Close(ref _input);
        }

        if (_output != _invalidHandle)
        {
            SetConsoleMode(_output, _originalOutput);
            Close(ref _output);
        }
    }

    // PWSTR is a bare pointer, so the name is pinned for the length of the call.
    private static nint OpenConsole(string name)
    {
        fixed (char* pointer = name)
        {
            return Functions.CreateFileW(
                new PWSTR { Value = (nint)pointer },
                (uint)(GENERIC_ACCESS_RIGHTS.GENERIC_READ | GENERIC_ACCESS_RIGHTS.GENERIC_WRITE),
                FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE,
                0,
                FILE_CREATION_DISPOSITION.OPEN_EXISTING,
                0,
                HANDLE.Null);
        }
    }

    private static void Close(ref nint handle)
    {
        if (handle != _invalidHandle)
        {
            Functions.CloseHandle(handle);
            handle = _invalidHandle;
        }
    }

    private const nint _invalidHandle = -1;

    // UTF-8, the only code page that can name a file in any language at once.
    private const uint _unicodeCodePage = 65001;

    // written as code points because neither is distinguishable from an ordinary space in a source file.
    private const char _noBreakSpace = '\u00a0';
    private const char _narrowNoBreakSpace = '\u202f';

#pragma warning disable IDE1006 // Naming Styles
    private const uint ENABLE_PROCESSED_INPUT = 0x0001;
    private const uint ENABLE_LINE_INPUT = 0x0002;
    private const uint ENABLE_ECHO_INPUT = 0x0004;

    private const uint ENABLE_PROCESSED_OUTPUT = 0x0001;
    private const uint ENABLE_WRAP_AT_EOL_OUTPUT = 0x0002;
    private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;
#pragma warning restore IDE1006 // Naming Styles

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetConsoleMode(nint hConsoleHandle, out uint lpMode);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleMode(nint hConsoleHandle, uint dwMode);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleCP(uint wCodePageID);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleOutputCP(uint wCodePageID);

    [LibraryImport("kernel32", SetLastError = true)]
    private static partial uint GetConsoleCP();

    [LibraryImport("kernel32", SetLastError = true)]
    private static partial uint GetConsoleOutputCP();

}
