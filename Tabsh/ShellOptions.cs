namespace Tabsh;

// the raw command line rather than the argument array,
// everything after /c or /k belongs to the command being run and splitting it then joining it back loses the quoting.
internal sealed class ShellOptions
{
    private ShellOptions(string? command, bool keepRunning, bool quiet)
    {
        Command = command;
        KeepRunning = keepRunning;
        Quiet = quiet;
    }

    public string? Command { get; }
    public bool KeepRunning { get; }
    public bool Quiet { get; }

    public static ShellOptions Parse(string commandLine)
    {
        ArgumentNullException.ThrowIfNull(commandLine);

        var index = SkipToken(commandLine, 0);
        var quiet = false;

        while (index < commandLine.Length)
        {
            while (index < commandLine.Length && (commandLine[index] == ' ' || commandLine[index] == '\t'))
            {
                index++;
            }

            if (index >= commandLine.Length)
                break;

            var end = SkipToken(commandLine, index);
            var token = commandLine[index..end];

            if (IsSwitch(token, 'c') || IsSwitch(token, 'k'))
            {
                var rest = StripOuterQuotes(commandLine[end..].TrimStart(' ', '\t'));
                return new ShellOptions(rest.Length > 0 ? rest : null, IsSwitch(token, 'k'), quiet);
            }

            if (IsSwitch(token, 'q'))
            {
                quiet = true;
            }

            index = end;
        }

        return new ShellOptions(null, keepRunning: true, quiet);
    }

    // the quotes that kept the whole command in one argument are not part of it. cmd /s /c does the same,
    // wart included, a command that both starts and ends with a quote of its own loses them.
    private static string StripOuterQuotes(string text)
    {
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
            return text[1..^1];

        return text;
    }

    private static bool IsSwitch(string token, char letter) =>
        token.Length == 2 && (token[0] == '/' || token[0] == '-') && char.ToUpperInvariant(token[1]) == char.ToUpperInvariant(letter);

    private static int SkipToken(string text, int index)
    {
        var quoted = false;
        while (index < text.Length)
        {
            var c = text[index];
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (!quoted && (c == ' ' || c == '\t'))
            {
                break;
            }

            index++;
        }

        return index;
    }
}
