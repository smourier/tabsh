namespace Tabsh.Parsing;

internal readonly struct Token
{
    public Token(TokenKind kind, string text)
        : this(kind, text, text)
    {
    }

    public Token(TokenKind kind, string text, string raw)
    {
        Kind = kind;
        Text = text;
        Raw = raw;
        FileDescriptor = -1;
        RedirectionMode = RedirectionKind.Output;
    }

    public Token(int fileDescriptor, RedirectionKind mode, string text)
    {
        Kind = TokenKind.Redirection;
        Text = text;
        Raw = text;
        FileDescriptor = fileDescriptor;
        RedirectionMode = mode;
    }

    public TokenKind Kind { get; }

    // the word with its quotes taken off, which is what the shell itself works with.
    public string Text { get; }

    // the word as it was written, quotes included, which is what an external program is given.
    // Programs parse their own command line, and find and findstr among others need those quotes back.
    public string Raw { get; }
    public int FileDescriptor { get; }
    public RedirectionKind RedirectionMode { get; }

    public override string ToString() => Kind == TokenKind.EndOfInput ? Res.EndOfLine : Text;
}
