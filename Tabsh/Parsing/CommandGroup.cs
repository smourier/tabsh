namespace Tabsh.Parsing;

// a parenthesised group, so that a redirection can be attached to several commands at once, as in "(ver & set) > log.txt".
internal sealed class CommandGroup(SequenceNode body) : CommandNode
{
    public SequenceNode Body { get; } = body;
}
