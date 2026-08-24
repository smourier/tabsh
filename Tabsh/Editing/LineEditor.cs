namespace Tabsh.Editing;

// Ctrl+C is taken as input for exactly as long as the prompt is up, so that it abandons the line.
// It goes back to being an event before any child starts, interrupting that one is the console's job.
internal sealed class LineEditor(Shell shell)
{
    private readonly LineBuffer _buffer = new();
    private readonly ConsoleRenderer _renderer = new();
    private CompletionSession? _session;
    private string _prompt = string.Empty;

    public CommandHistory History { get; } = new();

    // null means the session should end, which is what Ctrl+D on an empty line asks for.
    public string? ReadLine(string prompt)
    {
        _prompt = prompt;
        _buffer.Clear();
        _session = null;
        History.ResetCursor();
        _renderer.Start(prompt);
        _renderer.Render(string.Empty, 0);

        Console.TreatControlCAsInput = true;
        try
        {
            while (true)
            {
                var key = Console.ReadKey(intercept: true);

                if (key.Key == ConsoleKey.Enter)
                {
                    _renderer.Finish(_buffer.Text);
                    return _buffer.Text;
                }

                if (!Dispatch(key))
                    return null;

                // a paste arrives as a burst of key events, and redrawing between each of them is what makes it crawl.
                if (!Console.KeyAvailable)
                {
                    _renderer.Render(_buffer.Text, _buffer.Cursor);
                }
            }
        }
        finally
        {
            // off rather than whatever was found,
            // or a console some earlier program left in raw mode would stay that way and nothing could be interrupted.
            Console.TreatControlCAsInput = false;
        }
    }

    // false asks for the shell to end. TAB is the only key that leaves a completion session standing,
    // which is what makes the next TAB start again from the new text.
    private bool Dispatch(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Tab)
        {
            Complete(key.Modifiers.HasFlag(ConsoleModifiers.Shift) ? -1 : 1);
            return true;
        }

        // ESC while cycling puts back what had been typed, so a wrong guess costs nothing.
        if (key.Key == ConsoleKey.Escape && _session != null)
        {
            var length = _session.CurrentLength;
            _buffer.Replace(_session.TokenStart, length, _session.Revert());
            _session = null;
            return true;
        }

        _session = null;

        // anything that is not walking the history starts the next walk over from whatever is on the line by then.
        if (key.Key is not (ConsoleKey.UpArrow or ConsoleKey.DownArrow or ConsoleKey.F8 or ConsoleKey.F7))
        {
            History.ResetCursor();
        }

        return Edit(key);
    }

    // drives the editor from a script rather than a keyboard, for the "keys" command. Nothing is drawn.
    public string Simulate(IEnumerable<ConsoleKeyInfo> keys, out int cursor)
    {
        ArgumentNullException.ThrowIfNull(keys);

        _buffer.Clear();
        _session = null;

        foreach (var key in keys)
        {
            if (key.Key == ConsoleKey.Enter)
                break;

            Dispatch(key);
        }

        cursor = _buffer.Cursor;
        return _buffer.Text;
    }

    // false asks for the shell to end.
    private bool Edit(ConsoleKeyInfo key)
    {
        var control = key.Modifiers.HasFlag(ConsoleModifiers.Control);
        switch (key.Key)
        {
            case ConsoleKey.C when control:
                _buffer.Cursor = _buffer.Length;
                _renderer.Render(_buffer.Text, _buffer.Cursor);
                Console.WriteLine("^C");
                _buffer.Clear();
                _renderer.Start(_prompt);
                return true;

            case ConsoleKey.D when control:
                if (_buffer.Length == 0)
                {
                    _renderer.Finish(_buffer.Text);
                    return false;
                }

                _buffer.Delete();
                return true;

            case ConsoleKey.Backspace:
                _buffer.Backspace();
                return true;

            case ConsoleKey.Delete:
                _buffer.Delete();
                return true;

            case ConsoleKey.LeftArrow:
                _buffer.Cursor = control ? _buffer.PreviousWord() : _buffer.Cursor - 1;
                return true;

            case ConsoleKey.RightArrow:
                _buffer.Cursor = control ? _buffer.NextWord() : _buffer.Cursor + 1;
                return true;

            case ConsoleKey.Home:
                _buffer.Cursor = 0;
                return true;

            case ConsoleKey.End:
                _buffer.Cursor = _buffer.Length;
                return true;

            // what is left of the cursor is what the entry must start with,
            // so "r" then Up walks the commands beginning with an r. An empty line is no restriction.
            case ConsoleKey.UpArrow:
            case ConsoleKey.F8:
                Browse(History.Previous(_buffer.Text, _buffer.Text[.._buffer.Cursor]));
                return true;

            case ConsoleKey.DownArrow:
                Browse(History.Next());
                return true;

            case ConsoleKey.F7:
                Pick();
                return true;

            case ConsoleKey.Escape:
                _buffer.Clear();
                return true;

            case ConsoleKey.U when control:
                _buffer.DeleteToStart();
                return true;

            case ConsoleKey.K when control:
                _buffer.DeleteToEnd();
                return true;

            case ConsoleKey.W when control:
                _buffer.DeletePreviousWord();
                return true;

            case ConsoleKey.L when control:
                try
                {
                    Console.Clear();
                }
                catch (IOException)
                {
                    // there is no screen to clear when the output is not a console, which is not an error worth raising.
                }

                _renderer.Start(_prompt);
                return true;
        }

        if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
        {
            _buffer.Insert(key.KeyChar);
        }

        return true;
    }

    private void Complete(int direction)
    {
        _session ??= shell.Completer.Create(_buffer.Text, _buffer.Cursor);
        if (_session == null)
            return;

        var length = _session.CurrentLength;
        var candidate = _session.Advance(direction);
        _buffer.Replace(_session.TokenStart, length, candidate.Text);
    }

    private void Browse(string? entry)
    {
        if (entry != null)
        {
            _buffer.SetText(entry);
        }
    }

    // the list is drawn on its own rows below the line, and a fresh prompt is anchored where it was.
    // With nothing to choose from there is nothing to draw, and the line has to be left exactly as it is.
    private void Pick()
    {
        if (History.Entries.Count == 0)
            return;

        _renderer.Finish(_buffer.Text);
        var chosen = HistoryPicker.Choose(History.Entries);
        _renderer.Start(_prompt);
        if (chosen != null)
        {
            _buffer.SetText(chosen);
        }
    }
}
