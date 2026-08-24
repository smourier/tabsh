namespace Tabsh.Parsing;

// one stage of a pipeline, either a command to run or a parenthesised group, plus whatever redirections were attached to it.
internal abstract class CommandNode
{
    public List<Redirection> Redirections { get; } = [];
}
