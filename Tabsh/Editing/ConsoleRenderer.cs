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
        var painted = _promptWidth + VisibleLength(text);
        var offset = _anchorLeft + _promptWidth + cursor;
        Console.CursorVisible = false;
        try
        {
            // the row after the last cell written, since a line filling its last row exactly leaves the cursor below it,
            // and without terminal processing the console scrolls there on its own.
            MakeRoom(_anchorTop + (_anchorLeft + Math.Max(painted, _paintedLength)) / width);

            Console.SetCursorPosition(_anchorLeft, Math.Max(0, _anchorTop));
            Console.Write(line);

            // whatever the previous, longer line left behind has to be wiped, or a deleted character stays on screen.
            if (painted < _paintedLength)
            {
                Console.Write(new string(' ', _paintedLength - painted));
            }

            _paintedLength = painted;
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

    // the rows a line is about to need are made first, by scrolling at the bottom of the buffer.
    // Reading the cursor back afterwards fails at the last column, where a console holds it on the same row until the next character.
    private void MakeRoom(int lastRow)
    {
        var bottom = Console.BufferHeight - 1;
        if (lastRow <= bottom)
            return;

        var overflow = lastRow - bottom;
        Console.SetCursorPosition(0, bottom);
        Console.Write(new string(_lineFeed, overflow));
        _anchorTop -= overflow;
    }

    private const char _lineFeed = '\n';

    // leaves the cursor past the end of the finished line, ready for whatever the command prints.
    public void Finish(string text)
    {
        var width = Console.BufferWidth;
        if (width <= 0)
        {
            Console.WriteLine();
            return;
        }

        var end = _anchorLeft + _promptWidth + VisibleLength(text);
        try
        {
            // a line that fills its last row exactly leaves the cursor on that row's last cell,
            // so the newline below moves to the next row instead of leaving an empty one behind it.
            if (end > 0 && end % width == 0)
            {
                Console.SetCursorPosition(width - 1, Math.Max(0, _anchorTop + end / width - 1));
            }
            else
            {
                Console.SetCursorPosition(end % width, Math.Max(0, _anchorTop + end / width));
            }
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or IOException)
        {
            // continue, the newline below still puts us on a fresh row.
        }

        Console.WriteLine();
    }
}
