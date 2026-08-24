namespace Tabsh.Editing;

internal sealed class CompletionCandidate(string text, bool isDirectory)
{
    // exactly what replaces the token in the line, quoting included.
    public string Text { get; } = text;

    public bool IsDirectory { get; } = isDirectory;
}
