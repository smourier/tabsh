namespace Tabsh;

// doskey style macros, substituted on the raw line before it is parsed,
// which is what lets the body of an alias carry operators and redirections of its own.
internal sealed class AliasTable
{
    private const int _maximumExpansions = 16;

    private readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<string> Names => _aliases.Keys;

    public IEnumerable<KeyValuePair<string, string>> All => _aliases.OrderBy(a => a.Key, StringComparer.OrdinalIgnoreCase);

    public string? Get(string name) => _aliases.TryGetValue(name, out var body) ? body : null;

    public void Set(string name, string? body)
    {
        if (string.IsNullOrEmpty(body))
        {
            _aliases.Remove(name);
        }
        else
        {
            _aliases[name] = body;
        }
    }

    public string Expand(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var round = 0; round < _maximumExpansions; round++)
        {
            var name = FirstWord(line, out var rest);
            if (name.Length == 0 || !_aliases.TryGetValue(name, out var body))
                break;

            // an alias whose body starts with its own name is the usual way of adding default arguments to a command,
            // so it is expanded once and then left alone rather than treated as an error.
            if (!seen.Add(name))
                break;

            line = Substitute(body, rest);
        }

        return line;
    }

    private static string Substitute(string body, string rest)
    {
        var arguments = SplitWords(rest);
        var builder = new StringBuilder();
        var usedArguments = false;

        for (var i = 0; i < body.Length; i++)
        {
            if (body[i] != '$' || i + 1 >= body.Length)
            {
                builder.Append(body[i]);
                continue;
            }

            i++;
            var code = body[i];
            if (code == '*')
            {
                builder.Append(rest);
                usedArguments = true;
                continue;
            }

            if (char.IsAsciiDigit(code) && code != '0')
            {
                var index = code - '1';
                if (index < arguments.Count)
                {
                    builder.Append(arguments[index]);
                }

                usedArguments = true;
                continue;
            }

            switch (char.ToUpperInvariant(code))
            {
                case 'T':
                    builder.Append('&');
                    break;

                case '$':
                    builder.Append('$');
                    break;

                default:
                    builder.Append('$').Append(code);
                    break;
            }
        }

        // a body that never mentions its arguments still gets them, which is what makes "alias ll=dir /b" behave.
        if (!usedArguments && rest.Length > 0)
        {
            builder.Append(' ').Append(rest);
        }

        return builder.ToString();
    }

    private static string FirstWord(string line, out string rest)
    {
        var index = 0;
        while (index < line.Length && (line[index] == ' ' || line[index] == '\t'))
        {
            index++;
        }

        var start = index;
        var quoted = false;
        while (index < line.Length)
        {
            var c = line[index];
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (!quoted && c is ' ' or '\t' or '|' or '&' or '<' or '>')
            {
                break;
            }

            index++;
        }

        rest = line[index..].TrimStart(' ', '\t');
        return line[start..index].Trim('"');
    }

    private static List<string> SplitWords(string text)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        var started = false;

        foreach (var c in text)
        {
            if (c == '"')
            {
                quoted = !quoted;
                started = true;
                continue;
            }

            if (!quoted && (c == ' ' || c == '\t'))
            {
                if (started)
                {
                    words.Add(current.ToString());
                    current.Clear();
                    started = false;
                }

                continue;
            }

            current.Append(c);
            started = true;
        }

        if (started)
        {
            words.Add(current.ToString());
        }

        return words;
    }
}
