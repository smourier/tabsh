namespace Tabsh;

// the colour of the tab we are hosted in, which Windows Terminal exposes as palette entry 264, FRAME_BACKGROUND.
// A host that has never heard of it swallows the sequence, conhost included, so nothing has to ask who is hosting.
internal static class TerminalTab
{
    private const int _frameBackground = 264;
    private const char _escape = (char)0x1b;
    private const char _bell = (char)0x07;

    public static void SetColor(D3DCOLORVALUE color) =>
        Write(string.Create(CultureInfo.InvariantCulture, $"{_escape}]4;{_frameBackground};rgb:{color.BR:x2}/{color.BG:x2}/{color.BB:x2}{_bell}"));

    // OSC 104 with an index puts that entry back, which is how the terminal's own colour is handed back to it.
    public static void ResetColor() =>
        Write(string.Create(CultureInfo.InvariantCulture, $"{_escape}]104;{_frameBackground}{_bell}"));

    private static void Write(string sequence)
    {
        // written where nothing understands one, an escape sequence is just characters on the screen.
        // A redirected output is not a terminal at all, and the sequence would land in whatever is reading it.
        if (!ConsoleSession.VirtualTerminal || Console.IsOutputRedirected)
            return;

        Console.Write(sequence);
    }
}
