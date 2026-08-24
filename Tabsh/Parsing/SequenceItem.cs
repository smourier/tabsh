namespace Tabsh.Parsing;

internal sealed class SequenceItem(SequenceOperator sequenceOperator, PipelineNode pipeline)
{
    public SequenceOperator Operator { get; } = sequenceOperator;
    public PipelineNode Pipeline { get; } = pipeline;
}
