namespace Tabsh;

// the raw command line rather than the argument array,
// everything after /c or /k belongs to the command being run and splitting it then joining it back loses the quoting.
internal sealed class ShellOptions
{
    private ShellOptions(string? command, bool keepRunning, bool quiet, StringComparison comparison, string? badComparison, uint seed)
    {
        Command = command;
        KeepRunning = keepRunning;
        Quiet = quiet;
        Comparison = comparison;
        BadComparison = badComparison;
        Seed = seed;
    }

    public string? Command { get; }
    public bool KeepRunning { get; }
    public bool Quiet { get; }

    // how the tests in a title or a tab colour compare, whole and not just a question of case.
    // Names here are Unicode and the answer for them is the one Windows itself uses on a file name.
    public StringComparison Comparison { get; }

    // what was asked for and could not be read, kept so that whoever can write to the screen says so.
    public string? BadComparison { get; }

    // the number a computed colour starts from, so that a set of colours nobody likes can be traded for another.
    public uint Seed { get; }

    public static ShellOptions Parse(string commandLine)
    {
        ArgumentNullException.ThrowIfNull(commandLine);

        var index = SkipToken(commandLine, 0);
        var quiet = false;
        var comparison = _defaultComparison;
        string? badComparison = null;
        uint seed = 0;

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
                return new ShellOptions(rest.Length > 0 ? rest : null, IsSwitch(token, 'k'), quiet, comparison, badComparison, seed);
            }

            if (IsSwitch(token, 'q'))
            {
                quiet = true;
            }
            else if (IsComparisonSwitch(token, out var asked, out var unreadable))
            {
                comparison = asked;
                badComparison ??= unreadable;
            }
            else if (IsSeedSwitch(token, out var asked2))
            {
                seed = asked2;
            }

            index = end;
        }

        return new ShellOptions(null, keepRunning: true, quiet, comparison, badComparison, seed);
    }

    // the quotes that kept the whole command in one argument are not part of it. cmd /s /c does the same,
    // wart included, a command that both starts and ends with a quote of its own loses them.
    private static string StripOuterQuotes(string text)
    {
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
            return text[1..^1];

        return text;
    }

    private const string _caseSwitch = "case";
    private const string _seedSwitch = "seed";

    // what Windows compares a file name with, so the shell agrees with the file system it is sitting on.
    private const StringComparison _defaultComparison = StringComparison.OrdinalIgnoreCase;

    // "/case" on its own is the plain "tell them apart" answer, and "/case:<name>" is any StringComparison there is,
    // which is what puts the culture aware ones within reach for text no ordinal comparison reads the way a reader would.
    private static bool IsComparisonSwitch(string token, out StringComparison comparison, out string? unreadable)
    {
        comparison = _defaultComparison;
        unreadable = null;
        if (token.Length < 2 || (token[0] != '/' && token[0] != '-'))
            return false;

        var text = token[1..];
        var colon = text.IndexOf(':');
        var name = colon < 0 ? text : text[..colon];
        if (!name.Equals(_caseSwitch, StringComparison.OrdinalIgnoreCase))
            return false;

        if (colon < 0)
        {
            comparison = StringComparison.Ordinal;
            return true;
        }

        // a name nobody recognises is not a reason to refuse to start, it is a reason to say so and carry on.
        var asked = text[(colon + 1)..];
        if (!Enum.TryParse<StringComparison>(asked, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
        {
            unreadable = asked;
            return true;
        }

        comparison = parsed;
        return true;
    }

    // "/seed:<number>", the same shape as "/case:", and a number nobody can read leaves the default alone.
    private static bool IsSeedSwitch(string token, out uint seed)
    {
        seed = 0;
        var colon = token.IndexOf(':');
        if (colon < 2 || (token[0] != '/' && token[0] != '-'))
            return false;

        if (!token[1..colon].Equals(_seedSwitch, StringComparison.OrdinalIgnoreCase))
            return false;

        return uint.TryParse(token[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out seed);
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
