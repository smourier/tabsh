using DirectN.Extensions.Utilities;
using ShellN;

namespace Tabsh.Interop;

// what is actually on the clipboard, which is almost never just text.
// A copy from Explorer carries the shell's own item list, the drop effect it wants and the image it drew.
internal static unsafe class ClipboardDump
{
    private const int _textLimit = 120;
    private const string _ansiFileNameFormat = "FileName";
    private const string _dragContextFormat = "DragContext";
    private const string _dragImageFormat = "DragImageBits";

    public static DataObject? Open() => Clipboard.GetDataObject(throwOnError: false);

    // GetClipboardFormatNameW answers for a registered format and says nothing about the standard ones,
    // which are numbers and would print as numbers.
    public static string Name(uint format) => format switch
    {
        Clipboard.CF_TEXT => "CF_TEXT",
        Clipboard.CF_BITMAP => "CF_BITMAP",
        Clipboard.CF_METAFILEPICT => "CF_METAFILEPICT",
        Clipboard.CF_SYLK => "CF_SYLK",
        Clipboard.CF_DIF => "CF_DIF",
        Clipboard.CF_TIFF => "CF_TIFF",
        Clipboard.CF_OEMTEXT => "CF_OEMTEXT",
        Clipboard.CF_DIB => "CF_DIB",
        Clipboard.CF_PALETTE => "CF_PALETTE",
        Clipboard.CF_RIFF => "CF_RIFF",
        Clipboard.CF_PENDATA => "CF_PENDATA",
        Clipboard.CF_WAVE => "CF_WAVE",
        Clipboard.CF_UNICODETEXT => "CF_UNICODETEXT",
        Clipboard.CF_ENHMETAFILE => "CF_ENHMETAFILE",
        Clipboard.CF_HDROP => "CF_HDROP",
        Clipboard.CF_LOCALE => "CF_LOCALE",
        Clipboard.CF_DIBV5 => "CF_DIBV5",
        Clipboard.CF_OWNERDISPLAY => "CF_OWNERDISPLAY",
        Clipboard.CF_DSPTEXT => "CF_DSPTEXT",
        Clipboard.CF_DSPBITMAP => "CF_DSPBITMAP",
        Clipboard.CF_DSPMETAFILEPICT => "CF_DSPMETAFILEPICT",
        Clipboard.CF_DSPENHMETAFILE => "CF_DSPENHMETAFILE",
        >= Clipboard.CF_GDIOBJFIRST and <= Clipboard.CF_GDIOBJLAST => string.Format(CultureInfo.InvariantCulture, "CF_GDIOBJFIRST+{0}", format - Clipboard.CF_GDIOBJFIRST),
        >= Clipboard.CF_PRIVATEFIRST and <= Clipboard.CF_PRIVATELAST => string.Format(CultureInfo.InvariantCulture, "CF_PRIVATEFIRST+{0}", format - Clipboard.CF_PRIVATEFIRST),
        _ => Clipboard.GetFormatName(format),
    };

    // the shell's own item list first, which is the only one that can name a thing with no path at all.
    // It is asked of the shell rather than read out of the CIDA by hand.
    public static IReadOnlyList<ShellItem> Items(DataObject dataObject)
    {
        ArgumentNullException.ThrowIfNull(dataObject);

        try
        {
            var items = ShellItem.ArrayFromDataObject(dataObject, throwOnError: false);
            if (items.Count > 0)
                return items;
        }
        catch (Exception exception) when (exception is COMException or ArgumentException)
        {
            // nothing the shell recognised, which is what the file drop below is for.
        }

        // and the classic file drop otherwise, which is all an application that never heard of the shell writes.
        var dropped = new List<ShellItem>();
        foreach (var path in dataObject.GetFilesPath(throwOnError: false))
        {
            var item = ShellItem.FromParsingName(path, throwOnError: false);
            if (item != null)
            {
                dropped.Add(item);
            }
        }

        return dropped;
    }

    // one line of detail for a format, or null when the bytes say nothing a reader would want.
    public static string? Describe(uint format, string name, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(bytes);

        if (format == Clipboard.CF_HDROP)
            return null;

        switch (name)
        {
            case Clipboard.CFSTR_PREFERREDDROPEFFECT:
            case Clipboard.CFSTR_PERFORMEDDROPEFFECT:
            case Clipboard.CFSTR_LOGICALPERFORMEDDROPEFFECT:
                return bytes.Length >= sizeof(uint) ? Effect((DROPEFFECT)BitConverter.ToUInt32(bytes)) : null;

            case Clipboard.CFSTR_DROPDESCRIPTION:
                return DropDescription(bytes);

            case _dragContextFormat:
                return DragContext(bytes);

            case _dragImageFormat:
                return DragImage(bytes);

            case Clipboard.CFSTR_FILEDESCRIPTORW:
                return bytes.Length >= sizeof(uint)
                    ? string.Format(CultureInfo.CurrentCulture, Res.DescriptorCount, BitConverter.ToUInt32(bytes))
                    : null;
        }

        return Text(format, name, bytes) ?? Number(bytes);
    }

    private static string? Text(uint format, string name, byte[] bytes)
    {
        string? text = null;
        if (format == Clipboard.CF_UNICODETEXT || name == Clipboard.CFSTR_INETURL || name == Clipboard.CFSTR_FILENAMEW)
        {
            text = Encoding.Unicode.GetString(bytes);
        }
        else if (format is Clipboard.CF_TEXT or Clipboard.CF_OEMTEXT || name == _ansiFileNameFormat)
        {
            text = Encoding.Default.GetString(bytes);
        }

        if (text == null)
            return null;

        return string.Format(CultureInfo.CurrentCulture, Res.QuotedValue, Shorten(text.TrimEnd('\0')));
    }

    private static string? Number(byte[] bytes) => bytes.Length == sizeof(uint)
        ? string.Format(CultureInfo.CurrentCulture, Res.NumberValue, BitConverter.ToUInt32(bytes), BitConverter.ToUInt32(bytes))
        : null;

    private static string DropDescription(byte[] bytes)
    {
        if (bytes.Length < sizeof(DROPDESCRIPTION))
            return string.Empty;

        fixed (byte* pointer = bytes)
        {
            var description = *(DROPDESCRIPTION*)pointer;
            var message = new string((char*)&description.szMessage);
            var insert = new string((char*)&description.szInsert);
            return string.Format(CultureInfo.CurrentCulture, Res.DropDescriptionValue, description.type, Shorten(message), Shorten(insert));
        }
    }

    // undocumented, and written by the shell whenever it drags something with a picture under the cursor.
    private static string DragContext(byte[] bytes)
    {
        if (bytes.Length < sizeof(DRAGCONTEXT))
            return string.Empty;

        fixed (byte* pointer = bytes)
        {
            var context = *(DRAGCONTEXT*)pointer;
            return string.Format(CultureInfo.CurrentCulture, Res.DragContextValue, context.IsImage != 0, context.IsLayered != 0, context.Offset.x, context.Offset.y);
        }
    }

    private static string DragImage(byte[] bytes)
    {
        if (bytes.Length < sizeof(SHDRAGIMAGE))
            return string.Empty;

        fixed (byte* pointer = bytes)
        {
            var image = *(SHDRAGIMAGE*)pointer;
            return string.Format(CultureInfo.CurrentCulture, Res.DragImageValue, image.sizeDragImage.cx, image.sizeDragImage.cy, image.ptOffset.x, image.ptOffset.y, (uint)image.crColorKey.Value);
        }
    }

    private static string Effect(DROPEFFECT effect)
    {
        var names = new List<string>();
        if (effect.HasFlag(DROPEFFECT.DROPEFFECT_COPY))
        {
            names.Add(Res.EffectCopy);
        }

        if (effect.HasFlag(DROPEFFECT.DROPEFFECT_MOVE))
        {
            names.Add(Res.EffectMove);
        }

        if (effect.HasFlag(DROPEFFECT.DROPEFFECT_LINK))
        {
            names.Add(Res.EffectLink);
        }

        return names.Count == 0 ? Res.EffectNone : string.Join(", ", names);
    }

    private static string Shorten(string value)
    {
        var single = value.Replace('\r', ' ').Replace('\n', ' ');
        return single.Length <= _textLimit ? single : single[.._textLimit] + Res.Ellipsis;
    }

#pragma warning disable IDE1006 // Naming Styles
#pragma warning disable CA1707 // Identifiers should not contain underscores
    // no header declares this one, it is what the shell puts on a data object it is dragging an image for.
    [StructLayout(LayoutKind.Sequential)]
    private struct DRAGCONTEXT
    {
        public int IsImage;
        public int IsLayered;
        public POINT Offset;
    }
#pragma warning restore CA1707
#pragma warning restore IDE1006
}
