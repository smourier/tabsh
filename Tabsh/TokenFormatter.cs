namespace Tabsh;

// "{Name}" stands for the public property of that name, matched without regard to case.
// The properties ARE the vocabulary, so adding one adds a token and nothing else has to know.
internal static class TokenFormatter
{
    public static string Format<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(string format, T container)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(container);

        var map = Cache<T>.Map;
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

            var end = format.IndexOf('}', i + 1);
            if (end < 0)
            {
                // an opening brace with nothing to close it is text somebody meant, not a token they got wrong.
                built.Append(format.AsSpan(i));
                break;
            }

            // a name that stands for nothing is left exactly as it was written, so a brace is never a trap.
            var name = format[(i + 1)..end].Trim();
            built.Append(map.TryGetValue(name, out var property) ? Text(property.GetValue(container)) : format[i..(end + 1)]);
            i = end;
        }

        return built.ToString();
    }

    public static IEnumerable<string> Names<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>() => Cache<T>.Map.Keys.Order(StringComparer.OrdinalIgnoreCase);

    private static string Text(object? value) => value is IFormattable formattable ? formattable.ToString(null, CultureInfo.CurrentCulture) : value?.ToString() ?? string.Empty;

    // a static field of a generic type is one per type argument, which is the whole cache without a dictionary of them.
    private static class Cache<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>
    {
        public static readonly Dictionary<string, PropertyInfo> Map = Build();

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
