namespace Tabsh.Editing;

internal sealed class LineBuffer
{
    private readonly StringBuilder _text = new();
    private int _cursor;

    public int Length => _text.Length;

    public int Cursor
    {
        get => _cursor;
        set => _cursor = Math.Clamp(value, 0, _text.Length);
    }

    public string Text => _text.ToString();

    public void Insert(char c)
    {
        _text.Insert(_cursor, c);
        _cursor++;
    }

    public void Insert(string value)
    {
        _text.Insert(_cursor, value);
        _cursor += value.Length;
    }

    public void Replace(int start, int length, string replacement)
    {
        _text.Remove(start, length);
        _text.Insert(start, replacement);
        _cursor = start + replacement.Length;
    }

    public void Backspace()
    {
        if (_cursor == 0)
            return;

        _text.Remove(_cursor - 1, 1);
        _cursor--;
    }

    public void Delete()
    {
        if (_cursor >= _text.Length)
            return;

        _text.Remove(_cursor, 1);
    }

    public void DeleteToStart()
    {
        _text.Remove(0, _cursor);
        _cursor = 0;
    }

    public void DeleteToEnd() => _text.Remove(_cursor, _text.Length - _cursor);

    public void DeletePreviousWord()
    {
        var start = PreviousWord();
        if (start == _cursor)
            return;

        _text.Remove(start, _cursor - start);
        _cursor = start;
    }

    public void Clear()
    {
        _text.Clear();
        _cursor = 0;
    }

    public void SetText(string value)
    {
        _text.Clear();
        _text.Append(value);
        _cursor = _text.Length;
    }

    // a word ends at a space or at a path separator, so Ctrl+Left walks a path one element at a time.
    public int PreviousWord()
    {
        var index = _cursor;
        while (index > 0 && IsBoundary(_text[index - 1]))
        {
            index--;
        }

        while (index > 0 && !IsBoundary(_text[index - 1]))
        {
            index--;
        }

        return index;
    }

    public int NextWord()
    {
        var index = _cursor;
        while (index < _text.Length && !IsBoundary(_text[index]))
        {
            index++;
        }

        while (index < _text.Length && IsBoundary(_text[index]))
        {
            index++;
        }

        return index;
    }

    private static bool IsBoundary(char c) => c is ' ' or '\t' or '\\' or '/';
}
