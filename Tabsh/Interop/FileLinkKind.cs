namespace Tabsh.Interop;

internal enum FileLinkKind
{
    None,

    // mklink /J, and what every "this folder is really over there" on Windows has been made of since Vista.
    Junction,

    // mklink and mklink /D, which need a privilege to create and behave more like the Unix idea.
    Symlink,

    // a reparse point that is neither, which is most of OneDrive, deduplication and the app execution aliases.
    Other,
}
