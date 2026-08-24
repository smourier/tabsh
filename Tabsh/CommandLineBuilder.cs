namespace Tabsh;

// the C runtime's own quoting rules, since that is what almost every program on Windows splits a command line with.
// Joining the words with spaces would break the first path containing one.
internal static class CommandLineBuilder
{
    public static string Build(IEnumerable<string> words)
    {
        var builder = new StringBuilder();
        foreach (var word in words)
        {
            Append(builder, word);
        }

        return builder.ToString();
    }

    public static void Append(StringBuilder builder, string argument)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(argument);

        if (builder.Length > 0)
        {
            builder.Append(' ');
        }

        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '"']) < 0)
        {
            builder.Append(argument);
            return;
        }

        builder.Append('"');
        for (var i = 0; i < argument.Length; i++)
        {
            var backslashes = 0;
            while (i < argument.Length && argument[i] == '\\')
            {
                backslashes++;
                i++;
            }

            if (i == argument.Length)
            {
                // trailing backslashes sit against the closing quote, where they would escape it, so they are doubled.
                builder.Append('\\', backslashes * 2);
                break;
            }

            if (argument[i] == '"')
            {
                builder.Append('\\', backslashes * 2 + 1).Append('"');
            }
            else
            {
                builder.Append('\\', backslashes).Append(argument[i]);
            }
        }

        builder.Append('"');
    }
}
