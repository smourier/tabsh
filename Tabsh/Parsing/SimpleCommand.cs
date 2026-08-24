namespace Tabsh.Parsing;

internal sealed class SimpleCommand : CommandNode
{
    public List<string> Words { get; } = [];

    // the same words as they were written. Only the command line handed to an external program is built from these.
    public List<string> RawWords { get; } = [];

    public string Name => Words.Count > 0 ? Words[0] : string.Empty;
}
