namespace Tabsh.Interop;

// a module some process has loaded, which is how a file with no path anybody remembers gets found.
internal sealed class LoadedModule(uint processId, string path)
{
    public uint ProcessId { get; } = processId;
    public string Path { get; } = path;
}
