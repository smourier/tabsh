namespace Tabsh;

internal static class Base64Commands
{
    private const int _noWrap = 0;
    private const int _maxWidth = 4096;

    public static int Convert(BuiltinContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var decode = false;
        var text = false;
        var width = _noWrap;
        string? output = null;
        var inputs = new List<string>();

        foreach (var argument in context.Arguments)
        {
            if (!argument.StartsWith('/'))
            {
                inputs.Add(argument);
                continue;
            }

            var option = argument[1..];
            switch (char.ToUpperInvariant(option.Length > 0 ? option[0] : ' '))
            {
                case 'D':
                    decode = true;
                    break;

                case 'T':
                    text = true;
                    break;

                case 'O':
                    output = option[1..].TrimStart(':');
                    if (output.Length == 0)
                        return context.Fail(Res.NameExpected);

                    break;

                case 'W':
                    if (!int.TryParse(option[1..].TrimStart(':'), NumberStyles.None, CultureInfo.CurrentCulture, out width) || width < 1 || width > _maxWidth)
                        return context.Fail(string.Format(CultureInfo.CurrentCulture, Res.InvalidWidth, option[1..].TrimStart(':')));

                    break;

                default:
                    return context.Fail(string.Format(CultureInfo.CurrentCulture, Res.InvalidSwitch, argument));
            }
        }

        if (inputs.Count == 0)
            return context.Fail(Res.NameExpected);

        // a file is written once, from one input, or two inputs would silently leave only the second behind.
        if (output != null && inputs.Count != 1)
            return context.Fail(Res.OneInputExpected);

        var code = 0;
        foreach (var input in inputs)
        {
            code = Run(context, input, decode, text, width, output) != 0 ? 1 : code;
        }

        return code;
    }

    private static int Run(BuiltinContext context, string input, bool decode, bool text, int width, string? output)
    {
        try
        {
            var bytes = decode ? Decode(context, input, text) : Encode(context, input, text, width);
            if (bytes == null)
                return context.Fail(string.Format(CultureInfo.CurrentCulture, Res.InvalidBase64, input));

            if (output != null)
                return Save(context, output, bytes);

            // decoded bytes come back as text, which is all a writer can carry. /o: is how binary gets out intact.
            context.Output.WriteLine(decode ? Encoding.UTF8.GetString(bytes) : Encoding.ASCII.GetString(bytes));
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return context.Fail(exception.Message);
        }
    }

    // wrapped here rather than on the way out, so that a file written with /o: is wrapped the same as the screen is.
    private static byte[] Encode(BuiltinContext context, string input, bool text, int width)
    {
        var encoded = System.Convert.ToBase64String(Read(context, input, text));
        return Encoding.ASCII.GetBytes(width == _noWrap ? encoded : Wrap(encoded, width));
    }

    // whitespace is not part of the encoding, so a file wrapped at any width still decodes.
    private static byte[]? Decode(BuiltinContext context, string input, bool text)
    {
        var encoded = Encoding.UTF8.GetString(Read(context, input, text));
        var stripped = new StringBuilder(encoded.Length);
        foreach (var c in encoded)
        {
            if (!char.IsWhiteSpace(c))
            {
                stripped.Append(c);
            }
        }

        var buffer = new byte[stripped.Length];
        if (!System.Convert.TryFromBase64String(stripped.ToString(), buffer, out var count))
            return null;

        return buffer[..count];
    }

    // a name that is a file on disk is the file, anything else is the text itself, the same rule hash reads a name by.
    private static byte[] Read(BuiltinContext context, string input, bool text)
    {
        if (text)
            return Encoding.UTF8.GetBytes(input);

        string full;
        try
        {
            full = ShellPath.Resolve(input, context.Environment.CurrentDirectory);
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8.GetBytes(input);
        }

        if (!File.Exists(full))
            return Encoding.UTF8.GetBytes(input);

        using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static int Save(BuiltinContext context, string output, byte[] bytes)
    {
        var full = ShellPath.Resolve(output, context.Environment.CurrentDirectory);
        File.WriteAllBytes(full, bytes);
        return 0;
    }

    private static string Wrap(string value, int width)
    {
        var wrapped = new StringBuilder(value.Length + value.Length / width + 1);
        for (var i = 0; i < value.Length; i += width)
        {
            if (i > 0)
            {
                wrapped.AppendLine();
            }

            wrapped.Append(value.AsSpan(i, Math.Min(width, value.Length - i)));
        }

        return wrapped.ToString();
    }
}
