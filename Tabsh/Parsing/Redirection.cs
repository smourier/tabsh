namespace Tabsh.Parsing;

internal sealed class Redirection(int fileDescriptor, RedirectionKind kind, string target)
{
    public int FileDescriptor { get; } = fileDescriptor;
    public RedirectionKind Kind { get; } = kind;
    public string Target { get; } = target;
}
