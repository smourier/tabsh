namespace Tabsh.Editing;

// the whole line is rewritten from its anchor rather than patched,
// the only version that stays correct when the text wraps onto more rows than before. The buffer can scroll under us.
internal sealed class ConsoleRenderer
{
    private string _prompt = string.Empty;
    private int _anchorLeft;
    private int _anchorTop;
    private int _paintedLength;
    private int _promptWidth;

    public void Start(string prompt)
    {
        // escape sequences take up no columns, and counting them would put the cursor that many places out.
        // A console that cannot obey them has them stripped instead of printing bracket codes.
        _prompt = ConsoleSession.VirtualTerminal ? prompt : WithoutEscapes(prompt);
        _promptWidth = VisibleLength(_prompt);
        _anchorLeft = Console.CursorLeft;
        _anchorTop = Console.CursorTop;
        _paintedLength = 0;
    }

    public void Render(string text, int cursor)
    {
        var width = Console.BufferWidth;
        if (width <= 0)
            return;

        var line = _prompt + text;
        Console.CursorVisible = false;
        try
        {
            Console.SetCursorPosition(_anchorLeft, Math.Max(0, _anchorTop));
            Console.Write(line);

            // whatever the previous, longer line left behind has to be wiped, or a deleted character stays on screen.
            var painted = _promptWidth + VisibleLength(text);
            if (painted < _paintedLength)
            {
                Console.Write(new string(' ', _paintedLength - painted));
                painted = _paintedLength;
            }

            var expectedTop = _anchorTop + (_anchorLeft + painted) / width;
            var scrolled = expectedTop - Console.CursorTop;
            if (scrolled > 0)
            {
                _anchorTop -= scrolled;
            }

            _paintedLength = _promptWidth + VisibleLength(text);

            var offset = _anchorLeft + _promptWidth + cursor;
            Console.SetCursorPosition(offset % width, Math.Max(0, _anchorTop + offset / width));
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or IOException)
        {
            // the window was resized out from under the maths, the next keystroke redraws from wherever we are now.
            _anchorLeft = 0;
            _anchorTop = Console.CursorTop;
            _paintedLength = 0;
        }
        finally
        {
            Console.CursorVisible = true;
        }
    }

    // how many columns a string actually occupies, which is every character that is not part of an escape sequence.
    private static int VisibleLength(string text)
    {
        var width = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == _escape)
            {
                i = SkipEscape(text, i);
                continue;
            }

            width++;
        }

        return width;
    }

    private static string WithoutEscapes(string text)
    {
        if (!text.Contains(_escape))
            return text;

        var kept = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == _escape)
            {
                i = SkipEscape(text, i);
                continue;
            }

            kept.Append(text[i]);
        }

        return kept.ToString();
    }

    // the index of the last character of the sequence starting at the escape, so that the caller's loop steps past it.
    private static int SkipEscape(string text, int start)
    {
        var i = start + 1;
        if (i >= text.Length)
            return i;

        // the bracket form, which is all of colour and cursor movement, ends at the first byte from @ to ~.
        if (text[i] == '[')
        {
            for (i++; i < text.Length; i++)
            {
                if (text[i] >= '@' && text[i] <= '~')
                    return i;
            }

            return text.Length;
        }

        // an operating system command runs to a bell or to a string terminator, and anything else is two characters.
        if (text[i] == ']')
        {
            for (i++; i < text.Length; i++)
            {
                if (text[i] == _bell)
                    return i;

                if (text[i] == _escape && i + 1 < text.Length)
                    return i + 1;
            }

            return text.Length;
        }

        return i;
    }

    private const char _escape = (char)0x1b;

    // what ends an operating system command sequence, written as a code point because a raw one is invisible here.
    private const char _bell = (char)0x07;

    // leaves the cursor past the end of the finished line, ready for whatever the command prints.
    public void Finish(string text)
    {
        var width = Console.BufferWidth;
        if (width <= 0)
        {
            Console.WriteLine();
            return;
        }

        var offset = _anchorLeft + _promptWidth + VisibleLength(text);
        try
        {
            Console.SetCursorPosition(offset % width, Math.Max(0, _anchorTop + offset / width));
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or IOException)
        {
            // continue, the newline below still puts us on a fresh row.
        }

        Console.WriteLine();
    }
}
