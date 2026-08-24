namespace Tabsh.Interop;

// one named data stream inside a file, other than the unnamed one everybody means when they say the file.
internal sealed class NamedStream(string name, long size)
{
    // as Windows names it, ":Zone.Identifier:$DATA" and the like, which is also how it is typed to open one.
    public string Name { get; } = name;

    public long Size { get; } = size;
}
