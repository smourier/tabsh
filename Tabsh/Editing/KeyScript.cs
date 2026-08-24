namespace Tabsh.Editing;

// a single character token is that character, an all upper case token is a named key, anything else is typed out,
// which is why "Tab" is three letters and "TAB" is the key.
internal static class KeyScript
{
    public static IEnumerable<ConsoleKeyInfo> Parse(IEnumerable<string> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        var keys = new List<ConsoleKeyInfo>();
        foreach (var token in tokens)
        {
            if (token.Length == 0)
                continue;

            if (token.Length == 1)
            {
                keys.Add(Character(token[0]));
                continue;
            }

            var named = Named(token);
            if (named != null)
            {
                keys.Add(named.Value);
                continue;
            }

            foreach (var c in token)
            {
                keys.Add(Character(c));
            }
        }

        return keys;
    }

    private static ConsoleKeyInfo? Named(string token) => token switch
    {
        "SP" => Character(' '),
        "TAB" => Key(ConsoleKey.Tab),
        "STAB" => Key(ConsoleKey.Tab, ConsoleModifiers.Shift),
        "ESC" => Key(ConsoleKey.Escape),
        "BS" => Key(ConsoleKey.Backspace),
        "DEL" => Key(ConsoleKey.Delete),
        "LEFT" => Key(ConsoleKey.LeftArrow),
        "RIGHT" => Key(ConsoleKey.RightArrow),
        "CLEFT" => Key(ConsoleKey.LeftArrow, ConsoleModifiers.Control),
        "CRIGHT" => Key(ConsoleKey.RightArrow, ConsoleModifiers.Control),
        "HOME" => Key(ConsoleKey.Home),
        "END" => Key(ConsoleKey.End),
        "UP" => Key(ConsoleKey.UpArrow),
        "DOWN" => Key(ConsoleKey.DownArrow),
        "F8" => Key(ConsoleKey.F8),
        "CU" => Key(ConsoleKey.U, ConsoleModifiers.Control),
        "CK" => Key(ConsoleKey.K, ConsoleModifiers.Control),
        "CW" => Key(ConsoleKey.W, ConsoleModifiers.Control),
        _ => null,
    };

    private static ConsoleKeyInfo Key(ConsoleKey key, ConsoleModifiers modifiers = 0) => new(
        '\0',
        key,
        modifiers.HasFlag(ConsoleModifiers.Shift),
        modifiers.HasFlag(ConsoleModifiers.Alt),
        modifiers.HasFlag(ConsoleModifiers.Control));

    private static ConsoleKeyInfo Character(char c) => new(c, ConsoleKey.None, shift: false, alt: false, control: false);
}
