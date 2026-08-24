namespace Tabsh;

// one entry of a shell folder, read once and kept, because the item it came from is disposed straight after.
internal sealed class ShellChild(string name, string parsingName, bool isFolder, long size, DateTime? modified)
{
    // what Explorer calls it, which is what you type to go there.
    public string Name { get; } = name;

    // the absolute name it can be reopened by, "::{...}" and the rest.
    public string ParsingName { get; } = parsingName;

    public bool IsFolder { get; } = isFolder;

    // negative for a folder and for anything virtual, which has no size to give.
    public long Size { get; } = size;

    public DateTime? Modified { get; } = modified;
}
