namespace Tabsh.Parsing;

// sequence  : pipeline (('&&' | '||' | '&') pipeline)*
// pipeline  : command ('|' command)*
// command   : word* redirection* | '(' sequence ')' redirection*
internal sealed class CommandParser
{
    private readonly List<Token> _tokens;
    private int _index;

    private CommandParser(List<Token> tokens)
    {
        _tokens = tokens;
    }

    public static SequenceNode Parse(string line, Func<string, string?> variableResolver)
    {
        ArgumentNullException.ThrowIfNull(line);

        var parser = new CommandParser(new CommandLexer(line, variableResolver).Tokenize());
        var sequence = parser.ParseSequence();
        if (parser.Current.Kind != TokenKind.EndOfInput)
            throw new CommandSyntaxException(string.Format(CultureInfo.CurrentCulture, Res.UnexpectedToken, parser.Current));

        return sequence;
    }

    private Token Current => _tokens[_index];

    private SequenceNode ParseSequence()
    {
        var sequence = new SequenceNode();
        var sequenceOperator = SequenceOperator.Always;
        while (true)
        {
            var pipeline = ParsePipeline();
            if (pipeline == null)
            {
                if (sequence.Items.Count == 0 && Current.Kind is TokenKind.EndOfInput or TokenKind.CloseParenthesis)
                    return sequence;

                throw new CommandSyntaxException(string.Format(CultureInfo.CurrentCulture, Res.CommandExpectedBefore, Current));
            }

            sequence.Items.Add(new SequenceItem(sequenceOperator, pipeline));

            switch (Current.Kind)
            {
                case TokenKind.AndIf:
                    sequenceOperator = SequenceOperator.OnSuccess;
                    break;

                case TokenKind.OrIf:
                    sequenceOperator = SequenceOperator.OnFailure;
                    break;

                case TokenKind.Separator:
                    sequenceOperator = SequenceOperator.Always;
                    break;

                default:
                    return sequence;
            }

            _index++;

            // a line ending on its separator is not an error, it just ends there.
            if (sequenceOperator == SequenceOperator.Always && Current.Kind is TokenKind.EndOfInput or TokenKind.CloseParenthesis)
                return sequence;
        }
    }

    private PipelineNode? ParsePipeline()
    {
        var command = ParseCommand();
        if (command == null)
            return null;

        var pipeline = new PipelineNode();
        pipeline.Commands.Add(command);

        while (Current.Kind == TokenKind.Pipe)
        {
            _index++;
            pipeline.Commands.Add(ParseCommand() ?? throw new CommandSyntaxException(Res.CommandExpectedAfterPipe));
        }

        return pipeline;
    }

    private CommandNode? ParseCommand()
    {
        if (Current.Kind == TokenKind.OpenParenthesis)
        {
            _index++;
            var body = ParseSequence();
            if (Current.Kind != TokenKind.CloseParenthesis)
                throw new CommandSyntaxException(Res.ClosingParenthesisExpected);

            _index++;
            var group = new CommandGroup(body);
            while (Current.Kind == TokenKind.Redirection)
            {
                ReadRedirection(group);
            }

            return group;
        }

        var command = new SimpleCommand();
        while (true)
        {
            if (Current.Kind == TokenKind.Word)
            {
                command.Words.Add(Current.Text);
                command.RawWords.Add(Current.Raw);
                _index++;
                continue;
            }

            if (Current.Kind == TokenKind.Redirection)
            {
                ReadRedirection(command);
                continue;
            }

            break;
        }

        // a line that is nothing but a redirection is cmd's way of creating or emptying a file, so it is kept.
        if (command.Words.Count == 0 && command.Redirections.Count == 0)
            return null;

        return command;
    }

    private void ReadRedirection(CommandNode command)
    {
        var token = Current;
        _index++;
        if (Current.Kind != TokenKind.Word)
            throw new CommandSyntaxException(string.Format(CultureInfo.CurrentCulture, Res.FileNameExpectedAfter, token.Text));

        command.Redirections.Add(new Redirection(token.FileDescriptor, token.RedirectionMode, Current.Text));
        _index++;
    }
}
