namespace Tabsh.Interop;

// alternate data streams, where a download records its zone and where something can hide.
// Nothing in Windows shows them by default.
internal static unsafe partial class FileStreams
{
    // the unnamed stream is the file itself and is already on the line above, so it is not listed again.
    private const string _defaultStream = "::$DATA";
    private const int _maximumStreamName = 296;

    public static List<NamedStream> Of(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var streams = new List<NamedStream>();

        WIN32_FIND_STREAM_DATA data;
        var find = FindFirstStreamW(path, FindStreamInfoStandard, &data, 0);
        if (find == _invalidHandle)
            return streams;

        try
        {
            do
            {
                var name = new string(data.cStreamName);
                if (name.Length > 0 && !string.Equals(name, _defaultStream, StringComparison.Ordinal))
                {
                    streams.Add(new NamedStream(name, data.StreamSize));
                }
            }
            while (FindNextStreamW(find, &data));
        }
        finally
        {
            FindClose(find);
        }

        return streams;
    }

    private const nint _invalidHandle = -1;

#pragma warning disable IDE1006 // Naming Styles
    private const int FindStreamInfoStandard = 0;

#pragma warning disable CS0649 // Field is never assigned to
    private struct WIN32_FIND_STREAM_DATA
    {
        public long StreamSize;
        public fixed char cStreamName[_maximumStreamName];
    }
#pragma warning restore CS0649 // Field is never assigned to
#pragma warning restore IDE1006 // Naming Styles

    [LibraryImport("kernel32", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindFirstStreamW(string lpFileName, int InfoLevel, WIN32_FIND_STREAM_DATA* lpFindStreamData, uint dwFlags);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FindNextStreamW(nint hFindStream, WIN32_FIND_STREAM_DATA* lpFindStreamData);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FindClose(nint hFindFile);
}
