namespace Tabsh.Parsing;

internal enum SequenceOperator
{
    // the first pipeline of a line, and cmd's "&", which runs the next one whatever the previous one returned.
    Always,
    OnSuccess,
    OnFailure,
}
