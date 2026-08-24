namespace Tabsh;

internal static class ClipCommands
{
    private const int _columnGap = 2;

    public static int Clip(BuiltinContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var paste = false;
        var files = false;
        var clear = false;
        var seconds = 0;
        var words = new List<string>();

        foreach (var argument in context.Arguments)
        {
            if (!argument.StartsWith('/'))
            {
                words.Add(argument);
                continue;
            }

            switch (char.ToUpperInvariant(argument.Length > 1 ? argument[1] : ' '))
            {
                case 'V':
                    paste = true;
                    break;

                case 'F':
                    files = true;
                    break;

                case 'C':
                    clear = true;
                    break;

                case 'M':
                    if (!ConsoleMonitor.TryParseInterval(argument, out seconds))
                        return context.Fail(string.Format(CultureInfo.CurrentCulture, Res.InvalidInterval, argument));

                    break;

                default:
                    return context.Fail(string.Format(CultureInfo.CurrentCulture, Res.InvalidSwitch, argument));
            }
        }

        if (clear)
            return ClipboardText.Clear() ? 0 : context.Fail(Res.ClipboardUnavailable);

        if (words.Count > 0)
            return Write(context, string.Join(' ', words));

        // piped into, this is clip.exe and takes what came down the pipe, newline and all.
        if (!paste && !files && seconds == 0 && context.HasOwnInput)
            return Write(context, context.Input.ReadToEnd());

        if (files)
            return Files(context, context.Output);

        if (paste)
            return Paste(context);

        if (seconds > 0)
            return ConsoleMonitor.Run(context, seconds, writer => Dump(writer));

        return Dump(context.Output);
    }

    private static int Write(BuiltinContext context, string text) =>
        ClipboardText.Write(text) ? 0 : context.Fail(Res.ClipboardUnavailable);

    private static int Paste(BuiltinContext context)
    {
        var text = ClipboardText.Read();
        if (text == null)
            return context.Fail(Res.ClipboardEmpty);

        context.Output.WriteLine(text);
        return 0;
    }

    // the paths on their own, for a line that goes on to do something with them.
    private static int Files(BuiltinContext context, TextWriter writer)
    {
        using var dataObject = ClipboardDump.Open();
        if (dataObject == null)
            return context.Fail(Res.ClipboardUnavailable);

        var items = ClipboardDump.Items(dataObject);
        if (items.Count == 0)
            return context.Fail(Res.ClipboardNoItems);

        foreach (var item in items)
        {
            using (item)
            {
                writer.WriteLine(item.SIGDN_DESKTOPABSOLUTEPARSING ?? item.SIGDN_NORMALDISPLAY ?? string.Empty);
            }
        }

        return 0;
    }

    private static int Dump(TextWriter writer)
    {
        using var dataObject = ClipboardDump.Open();
        if (dataObject == null)
        {
            writer.WriteLine(Res.ClipboardUnavailable);
            return 1;
        }

        // the numbers rather than the names,
        // because a standard format has no name and asking for one would register a brand new format called "13".
        var formats = dataObject.EnumerateFormats(throwOnError: false);
        if (formats.Count == 0)
        {
            writer.WriteLine(Res.ClipboardEmpty);
            return 1;
        }

        var rows = new List<ClipboardRow>();
        foreach (var format in formats)
        {
            var id = (uint)format.cfFormat;
            var name = ClipboardDump.Name(id);
            var bytes = dataObject.GetBytes(id, throwOnError: false);
            rows.Add(new ClipboardRow(
                name,
                bytes == null ? Res.NotRendered : string.Format(CultureInfo.CurrentCulture, Res.ByteValue, bytes.Length),
                bytes == null ? null : ClipboardDump.Describe(id, name, bytes)));
        }

        // measured rather than guessed, since a name can be as long as DataObjectAttributesRequiringElevation.
        var nameWidth = 0;
        var sizeWidth = 0;
        foreach (var row in rows)
        {
            nameWidth = Math.Max(nameWidth, row.Name.Length);
            sizeWidth = Math.Max(sizeWidth, row.Size.Length);
        }

        writer.WriteLine();
        writer.WriteLine(Res.ClipboardHeader);
        writer.WriteLine();

        foreach (var row in rows)
        {
            writer.WriteLine(string.Format(
                CultureInfo.CurrentCulture,
                Res.ClipboardFormatLine,
                row.Name.PadRight(nameWidth + _columnGap),
                row.Size.PadLeft(sizeWidth),
                row.Detail ?? string.Empty).TrimEnd());
        }

        // asked of the shell rather than read out of any one format,
        // so a CIDA, a file drop and whatever else it understands all answer the same way.
        var items = ClipboardDump.Items(dataObject);
        if (items.Count == 0)
            return 0;

        writer.WriteLine();
        writer.WriteLine(Res.ClipboardItems);
        foreach (var item in items)
        {
            using (item)
            {
                writer.WriteLine(string.Format(CultureInfo.CurrentCulture, Res.ClipboardItemLine, item.SIGDN_DESKTOPABSOLUTEPARSING ?? item.SIGDN_NORMALDISPLAY ?? string.Empty));
            }
        }

        return 0;
    }

    private sealed class ClipboardRow(string name, string size, string? detail)
    {
        public string Name { get; } = name;
        public string Size { get; } = size;
        public string? Detail { get; } = detail;
    }
}
