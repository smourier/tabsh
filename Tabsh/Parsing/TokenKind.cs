namespace Tabsh.Parsing;

internal enum TokenKind
{
    EndOfInput,
    Word,
    Pipe,
    AndIf,
    OrIf,
    Separator,
    Redirection,
    OpenParenthesis,
    CloseParenthesis,
}
