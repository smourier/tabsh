namespace Tabsh.Parsing;

internal enum RedirectionKind
{
    Input,
    Output,
    Append,

    // ">&" and its "2>&1" spelling, where the target is another file descriptor rather than a file.
    Duplicate,
}
