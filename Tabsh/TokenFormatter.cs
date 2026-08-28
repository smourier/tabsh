namespace Tabsh;

// "{Name}" stands for the public property of that name, matched without regard to case.
// The properties ARE the vocabulary, so adding one adds a token and nothing else has to know.
internal static class TokenFormatter
{
    // a token name can never hold a space, so a group with one in it is a test rather than a lookup.
    // That alone is what tells "{Name}" from "{Name contains x ? a : b}", with no marker of any kind needed.
    private const string _thenSeparator = " ? ";
    private const string _elseSeparator = " : ";
    private const string _thenMark = "?";
    private const string _fromWord = "from";

    // what a token class has to keep, so that the reflection below still finds it after the trimmer has been through.
    private const DynamicallyAccessedMemberTypes _tokens = DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicMethods;
    private const string _elseMark = ":";
    private const int _maximumDepth = 8;

    public static string Format<[DynamicallyAccessedMembers(_tokens)] T>(string format, T container, StringComparison comparison = StringComparison.OrdinalIgnoreCase, uint seed = 0) => Format(format, container, comparison, seed, 0);

    private static string Format<[DynamicallyAccessedMembers(_tokens)] T>(string format, T container, StringComparison comparison, uint seed, int depth)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(container);

        if (depth > _maximumDepth)
            return format;

        var built = new StringBuilder(format.Length);
        for (var i = 0; i < format.Length; i++)
        {
            var c = format[i];
            if ((c == '{' || c == '}') && i + 1 < format.Length && format[i + 1] == c)
            {
                built.Append(c);
                i++;
                continue;
            }

            if (c != '{')
            {
                built.Append(c);
                continue;
            }

            var end = Closing(format, i);
            if (end < 0)
            {
                // an opening brace with nothing to close it is text somebody meant, not a token they got wrong.
                built.Append(format.AsSpan(i));
                break;
            }

            built.Append(Render(format[(i + 1)..end], container, comparison, seed, depth));
            i = end;
        }

        return built.ToString();
    }

    private static string Render<[DynamicallyAccessedMembers(_tokens)] T>(string body, T container, StringComparison comparison, uint seed, int depth)
    {
        // the spaced form is looked for first, and finding it is what says the terse one was not meant.
        // That is what keeps a branch like "E:\work" whole, its colon is only a separator where none was spaced.
        var question = Separator(body, _thenSeparator);
        var spaced = question >= 0;
        if (!spaced)
        {
            question = Separator(body, _thenMark);
        }

        if (question < 0)
        {
            var name = body.Trim();
            if (Computed(name, container, comparison, seed, out var computed))
                return computed;

            // a name that stands for nothing is left exactly as it was written, so a brace is never a trap.
            return Resolve(name, container, out var value) ? value : Group(body);
        }

        var answer = Test(body[..question], container, comparison);
        if (answer == null)
            return Group(body);

        var then = spaced ? _thenSeparator : _thenMark;
        var otherwise = spaced ? _elseSeparator : _elseMark;
        var rest = body[(question + then.Length)..];
        var colon = Separator(rest, otherwise);

        // no else at all is an empty one, which is how a decoration that only shows up sometimes is written.
        var chosen = answer.Value
            ? (colon < 0 ? rest : rest[..colon])
            : (colon < 0 ? string.Empty : rest[(colon + otherwise.Length)..]);

        return Format(chosen, container, comparison, seed, depth + 1);
    }

    // null rather than false when the test cannot be read at all, so the group is left alone rather than decided.
    // A misspelt token or operator then shows up on the screen instead of quietly turning into the wrong answer.
    private static bool? Test<[DynamicallyAccessedMembers(_tokens)] T>(string condition, T container, StringComparison comparison)
    {
        var text = condition.Trim();

        // a word operator needs a space in front of it to be a word at all, a symbol one does not.
        var afterName = text.IndexOf(' ');
        var symbol = text.IndexOfAny(['=', '!']);
        if (afterName < 0 || (symbol > 0 && symbol < afterName))
        {
            afterName = symbol;
        }

        if (afterName <= 0)
            return null;

        if (!Resolve(text[..afterName].TrimEnd(), container, out var value))
            return null;

        var rest = text[afterName..].TrimStart();
        var afterOperator = rest.StartsWith("!=", StringComparison.Ordinal) ? 2 : rest.StartsWith('=') ? 1 : rest.IndexOf(' ');
        var name = afterOperator < 0 ? rest : rest[..afterOperator];

        // the right hand side keeps its spaces, a folder called Program Files is one operand and not two.
        var operand = afterOperator < 0 ? string.Empty : rest[afterOperator..].Trim();

        // a quoted one is unwrapped, since anybody writing quotes means the text inside them and not the quotes.
        // It is also the only way to write an operand that ends in a space, which trimming would otherwise take.
        if (operand.Length >= 2 && operand[0] == operand[^1] && (operand[0] == '\'' || operand[0] == '"'))
        {
            operand = operand[1..^1];
        }

        return name.ToUpperInvariant() switch
        {
            "IFEXISTS" => value.Length > 0,
            "IFEMPTY" => value.Length == 0,
            "=" => value.Equals(operand, comparison),
            "!=" => !value.Equals(operand, comparison),
            "CONTAINS" => operand.Length > 0 && value.Contains(operand, comparison),
            "STARTSWITH" => operand.Length > 0 && value.StartsWith(operand, comparison),
            "ENDSWITH" => operand.Length > 0 && value.EndsWith(operand, comparison),
            _ => null,
        };
    }

    // "{from Path}" is a colour worked out from what Path says, the same one every time for the same words.
    // The seed that shifts the whole set belongs to the command line, not in here where it sits beside a token name.
    private static bool Computed<[DynamicallyAccessedMembers(_tokens)] T>(string body, T container, StringComparison comparison, uint seed, out string computed)
    {
        computed = string.Empty;
        var words = body.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length != 2 || !words[0].Equals(_fromWord, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!Resolve(words[1], container, out var value))
            return false;

        computed = StableColor.Of(value, seed, comparison);
        return true;
    }

    // a plain name is a property, and a name carrying a number in brackets is a method that takes the number.
    // Both are written down once, in the token class, which is still the only place the vocabulary lives.
    private static bool Resolve<[DynamicallyAccessedMembers(_tokens)] T>(string name, T container, out string value)
    {
        value = string.Empty;
        if (Cache<T>.Map.TryGetValue(name, out var property))
        {
            value = Text(property.GetValue(container));
            return true;
        }

        var open = name.IndexOf('[');
        if (open <= 0 || name[^1] != ']')
            return false;

        if (!Cache<T>.Indexed.TryGetValue(name[..open], out var method))
            return false;

        if (!int.TryParse(name[(open + 1)..^1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var index))
            return false;

        value = Text(method.Invoke(container, [index]));
        return true;
    }

    public static IEnumerable<string> Names<[DynamicallyAccessedMembers(_tokens)] T>() =>
        Cache<T>.Map.Keys.Concat(Cache<T>.Indexed.Keys.Select(k => k + "[n]")).Order(StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<string> Operators => ["=", "!=", "contains", "startswith", "endswith", "ifexists", "ifempty"];

    private static string Group(string body) => "{" + body + "}";

    private static string Text(object? value) => value is IFormattable formattable ? formattable.ToString(null, CultureInfo.CurrentCulture) : value?.ToString() ?? string.Empty;

    // the brace that closes the one at open, counting the groups in between so a nested choice stays one piece.
    // A doubled brace is text, but only outside a group, which the scan above has already dealt with by now.
    // Reading one in here would take the "}}" that ends a nested choice for an escape and never find the end.
    private static int Closing(string format, int open)
    {
        var depth = 0;
        for (var i = open; i < format.Length; i++)
        {
            var c = format[i];
            if (c != '{' && c != '}')
                continue;

            depth += c == '{' ? 1 : -1;
            if (depth == 0)
                return i;
        }

        return -1;
    }

    private static int Separator(string text, string separator)
    {
        var depth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
            }
            else if (depth == 0 && i + separator.Length <= text.Length && string.CompareOrdinal(text, i, separator, 0, separator.Length) == 0)
                return i;
        }

        return -1;
    }

    // a static field of a generic type is one per type argument, which is the whole cache without a dictionary of them.
    private static class Cache<[DynamicallyAccessedMembers(_tokens)] T>
    {
        public static readonly Dictionary<string, PropertyInfo> Map = Build();
        public static readonly Dictionary<string, MethodInfo> Indexed = BuildIndexed();

        // one public method that takes a number and answers with text is one token that takes a number.
        private static Dictionary<string, MethodInfo> BuildIndexed()
        {
            var map = new Dictionary<string, MethodInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var method in typeof(T).GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                var parameters = method.GetParameters();
                if (method.ReturnType == typeof(string) && parameters.Length == 1 && parameters[0].ParameterType == typeof(int))
                {
                    map.TryAdd(method.Name, method);
                }
            }

            return map;
        }

        private static Dictionary<string, PropertyInfo> Build()
        {
            var map = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.CanRead && property.GetIndexParameters().Length == 0)
                {
                    map.TryAdd(property.Name, property);
                }
            }

            return map;
        }
    }
}
