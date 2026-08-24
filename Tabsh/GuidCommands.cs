namespace Tabsh;

internal static class GuidCommands
{
    private const string _defaultFormat = "D";
    private const string _formats = "NDBPX";

    // a loop in a built in cannot be interrupted, Ctrl+C is only ever aimed at a child,
    // so a mistyped count has to be refused rather than run.
    private const int _maxCount = 10000;

    public static int Generate(BuiltinContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var count = 1;
        var format = _defaultFormat;
        var upper = false;

        foreach (var argument in context.Arguments)
        {
            if (!argument.StartsWith('/'))
            {
                if (!int.TryParse(argument, NumberStyles.None, CultureInfo.CurrentCulture, out count) || count < 1)
                    return context.Fail(string.Format(CultureInfo.CurrentCulture, Res.InvalidCount, argument));

                if (count > _maxCount)
                    return context.Fail(string.Format(CultureInfo.CurrentCulture, Res.CountTooLarge, _maxCount));

                continue;
            }

            var option = argument[1..];
            switch (char.ToUpperInvariant(option.Length > 0 ? option[0] : ' '))
            {
                case 'F':
                    format = option[1..].TrimStart(':');
                    if (format.Length != 1 || !_formats.Contains(char.ToUpperInvariant(format[0]), StringComparison.Ordinal))
                        return context.Fail(string.Format(CultureInfo.CurrentCulture, Res.InvalidFormat, format));

                    break;

                case 'U':
                    upper = true;
                    break;

                default:
                    return context.Fail(string.Format(CultureInfo.CurrentCulture, Res.InvalidSwitch, argument));
            }
        }

        for (var i = 0; i < count; i++)
        {
            var text = Guid.NewGuid().ToString(format, CultureInfo.InvariantCulture);
            context.Output.WriteLine(upper ? text.ToUpperInvariant() : text);
        }

        return 0;
    }
}
