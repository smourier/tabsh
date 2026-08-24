namespace Tabsh.Interop;

// .NET hands back the target of a reparse point but not what sort it is, and a junction is not a symbolic link.
// The tag that tells them apart is in the find data.
internal static unsafe partial class FileLink
{
    public static FileLinkKind KindOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        WIN32_FIND_DATAW data;
        var find = FindFirstFileW(path, &data);
        if (find == _invalidHandle)
            return FileLinkKind.None;

        try
        {
            if ((data.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) == 0)
                return FileLinkKind.None;

            return data.dwReserved0 switch
            {
                IO_REPARSE_TAG_MOUNT_POINT => FileLinkKind.Junction,
                IO_REPARSE_TAG_SYMLINK => FileLinkKind.Symlink,
                _ => FileLinkKind.Other,
            };
        }
        finally
        {
            FindClose(find);
        }
    }

    private const nint _invalidHandle = -1;

#pragma warning disable IDE1006 // Naming Styles
    private const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x400;
    private const uint IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003;
    private const uint IO_REPARSE_TAG_SYMLINK = 0xA000000C;

    // the times are two unsigned values each rather than one signed one,
    // so that every field keeps the four byte alignment the structure is written with.
#pragma warning disable CS0649 // Field is never assigned to
    private struct WIN32_FIND_DATAW
    {
        public uint dwFileAttributes;
        public uint ftCreationTimeLow;
        public uint ftCreationTimeHigh;
        public uint ftLastAccessTimeLow;
        public uint ftLastAccessTimeHigh;
        public uint ftLastWriteTimeLow;
        public uint ftLastWriteTimeHigh;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint dwReserved0;
        public uint dwReserved1;
        public fixed char cFileName[260];
        public fixed char cAlternateFileName[14];
    }
#pragma warning restore CS0649 // Field is never assigned to
#pragma warning restore IDE1006 // Naming Styles

    [LibraryImport("kernel32", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindFirstFileW(string lpFileName, WIN32_FIND_DATAW* lpFindFileData);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FindClose(nint hFindFile);
}
