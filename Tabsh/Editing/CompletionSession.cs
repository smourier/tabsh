namespace Tabsh.Editing;

// one run of TAB presses over the same token, ended by any other key so the next TAB starts from the new text.
// Walking down a tree works because the separator the last candidate left behind moves the search inside it.
internal sealed class CompletionSession(int tokenStart, string originalText, IReadOnlyList<CompletionCandidate> candidates)
{
    public int TokenStart { get; } = tokenStart;
    public string OriginalText { get; } = originalText;
    public IReadOnlyList<CompletionCandidate> Candidates { get; } = candidates;

    // the length currently occupying the line at TokenStart, which is the original token until the first candidate lands.
    public int CurrentLength { get; private set; } = originalText.Length;

    public int Index { get; private set; } = -1;

    public CompletionCandidate Advance(int direction)
    {
        if (Index < 0)
        {
            Index = direction > 0 ? 0 : Candidates.Count - 1;
        }
        else
        {
            Index = (Index + direction + Candidates.Count) % Candidates.Count;
        }

        var candidate = Candidates[Index];
        CurrentLength = candidate.Text.Length;
        return candidate;
    }

    public string Revert()
    {
        CurrentLength = OriginalText.Length;
        Index = -1;
        return OriginalText;
    }
}
