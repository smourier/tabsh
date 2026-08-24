namespace Tabsh.Editing;

// the history as something to choose from rather than something to read, which is what F7 has always been for.
internal static class HistoryPicker
{
    private const int _maximumRows = 12;
    private const int _reservedRows = 2;

    public static string? Choose(IReadOnlyList<string> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Count == 0 || Console.IsOutputRedirected || Console.IsInputRedirected)
            return null;

        var rows = Math.Max(1, Math.Min(Math.Min(_maximumRows, entries.Count), Console.WindowHeight - _reservedRows));

        // the newest is the one most likely wanted, so the list opens on it with the tail of the list showing.
        var selected = entries.Count - 1;
        var first = Math.Max(0, entries.Count - rows);
        var top = MakeRoom(rows);

        try
        {
            while (true)
            {
                Paint(entries, top, first, rows, selected);

                var key = Console.ReadKey(intercept: true);
                switch (key.Key)
                {
                    case ConsoleKey.UpArrow:
                        selected = Math.Max(0, selected - 1);
                        break;

                    case ConsoleKey.DownArrow:
                        selected = Math.Min(entries.Count - 1, selected + 1);
                        break;

                    case ConsoleKey.PageUp:
                        selected = Math.Max(0, selected - rows);
                        break;

                    case ConsoleKey.PageDown:
                        selected = Math.Min(entries.Count - 1, selected + rows);
                        break;

                    case ConsoleKey.Home:
                        selected = 0;
                        break;

                    case ConsoleKey.End:
                        selected = entries.Count - 1;
                        break;

                    case ConsoleKey.Enter:
                        return entries[selected];

                    case ConsoleKey.Escape:
                        return null;
                }

                first = Math.Clamp(first, selected - rows + 1, selected);
                first = Math.Clamp(first, 0, Math.Max(0, entries.Count - rows));
            }
        }
        finally
        {
            Erase(top, rows);
        }
    }

    // the room for the list has to exist before anything is painted on it,
    // and printing it is what scrolls the window when there is not enough left.
    private static int MakeRoom(int rows)
    {
        var top = Console.CursorTop;
        for (var i = 0; i < rows; i++)
        {
            Console.WriteLine();
        }

        return Math.Min(top, Console.CursorTop - rows);
    }

    private static void Paint(IReadOnlyList<string> entries, int top, int first, int rows, int selected)
    {
        var width = Math.Max(Console.WindowWidth - 1, 1);
        var foreground = Console.ForegroundColor;
        var background = Console.BackgroundColor;

        Console.CursorVisible = false;
        try
        {
            for (var i = 0; i < rows; i++)
            {
                var index = first + i;
                Console.SetCursorPosition(0, top + i);

                var text = index < entries.Count
                    ? string.Format(CultureInfo.CurrentCulture, Res.HistoryLine, index + 1, entries[index])
                    : string.Empty;

                if (text.Length > width)
                {
                    text = text[..width];
                }

                if (index == selected)
                {
                    Console.ForegroundColor = background;
                    Console.BackgroundColor = foreground;
                }

                Console.Write(text.PadRight(width));
                Console.ForegroundColor = foreground;
                Console.BackgroundColor = background;
            }
        }
        finally
        {
            Console.CursorVisible = true;
        }
    }

    private static void Erase(int top, int rows)
    {
        var width = Math.Max(Console.WindowWidth - 1, 1);
        for (var i = 0; i < rows; i++)
        {
            Console.SetCursorPosition(0, top + i);
            Console.Write(new string(' ', width));
        }

        Console.SetCursorPosition(0, top);
    }
}
