namespace Tabsh.Parsing;

// cmd's rules for double quotes and the caret escape, but %VAR% expands during tokenizing rather than over the raw line,
// so a variable holding "a & del *" contributes text to one word instead of injecting an operator.
internal sealed class CommandLexer(string line, Func<string, string?> variableResolver)
{
    private int _position;

    public List<Token> Tokenize()
    {
        var tokens = new List<Token>();
        while (true)
        {
            SkipWhitespace();
            if (_position >= line.Length)
                break;

            var c = line[_position];

            // a bare digit stuck to a redirection operator names the file descriptor being redirected, as in "2>nul".
            if (char.IsAsciiDigit(c) && _position + 1 < line.Length && (line[_position + 1] == '>' || line[_position + 1] == '<'))
            {
                var descriptor = c - '0';
                _position++;
                tokens.Add(ReadRedirection(descriptor));
                continue;
            }

            switch (c)
            {
                case '|':
                    if (Peek(1) == '|')
                    {
                        _position += 2;
                        tokens.Add(new Token(TokenKind.OrIf, "||"));
                    }
                    else
                    {
                        _position++;
                        tokens.Add(new Token(TokenKind.Pipe, "|"));
                    }
                    break;

                case '&':
                    if (Peek(1) == '&')
                    {
                        _position += 2;
                        tokens.Add(new Token(TokenKind.AndIf, "&&"));
                    }
                    else
                    {
                        _position++;
                        tokens.Add(new Token(TokenKind.Separator, "&"));
                    }
                    break;

                case '(':
                    _position++;
                    tokens.Add(new Token(TokenKind.OpenParenthesis, "("));
                    break;

                case ')':
                    _position++;
                    tokens.Add(new Token(TokenKind.CloseParenthesis, ")"));
                    break;

                case '<':
                case '>':
                    tokens.Add(ReadRedirection(-1));
                    break;

                default:
                    tokens.Add(ReadWord());
                    break;
            }
        }

        tokens.Add(new Token(TokenKind.EndOfInput, string.Empty));
        return tokens;
    }

    // a descriptor of -1 means the operator did not name one, so the caller applies the default for its direction.
    private Token ReadRedirection(int descriptor)
    {
        var c = line[_position];
        _position++;

        if (c == '<')
            return new Token(descriptor < 0 ? 0 : descriptor, RedirectionKind.Input, "<");

        if (Peek(0) == '>')
        {
            _position++;
            return new Token(descriptor < 0 ? 1 : descriptor, RedirectionKind.Append, ">>");
        }

        if (Peek(0) == '&')
        {
            _position++;
            return new Token(descriptor < 0 ? 1 : descriptor, RedirectionKind.Duplicate, ">&");
        }

        return new Token(descriptor < 0 ? 1 : descriptor, RedirectionKind.Output, ">");
    }

    private Token ReadWord()
    {
        var text = new StringBuilder();
        var raw = new StringBuilder();
        var quoted = false;

        while (_position < line.Length)
        {
            var c = line[_position];

            if (c == '"')
            {
                quoted = !quoted;
                raw.Append(c);
                _position++;
                continue;
            }

            if (!quoted)
            {
                if (c is ' ' or '\t' or '|' or '&' or '<' or '>' or '(' or ')')
                    break;

                // the caret hands the next character through untouched, which is how an operator is typed as text.
                // It is consumed here, so what reaches a child is the character and not the escape.
                if (c == '^' && _position + 1 < line.Length)
                {
                    text.Append(line[_position + 1]);
                    raw.Append(line[_position + 1]);
                    _position += 2;
                    continue;
                }
            }

            if (c == '%')
            {
                AppendVariable(text, raw);
                continue;
            }

            text.Append(c);
            raw.Append(c);
            _position++;
        }

        return new Token(TokenKind.Word, text.ToString(), raw.ToString());
    }

    private void AppendVariable(StringBuilder text, StringBuilder raw)
    {
        if (Peek(1) == '%')
        {
            text.Append('%');
            raw.Append('%');
            _position += 2;
            return;
        }

        var end = line.IndexOf('%', _position + 1);
        if (end < 0)
        {
            text.Append('%');
            raw.Append('%');
            _position++;
            return;
        }

        var name = line[(_position + 1)..end];
        var value = variableResolver(name);
        if (value == null)
        {
            // cmd leaves an unknown name standing at the prompt, and so does this,
            // because a path that happens to contain a percent sign should survive being typed.
            text.Append(line, _position, end - _position + 1);
            raw.Append(line, _position, end - _position + 1);
        }
        else
        {
            text.Append(value);
            raw.Append(value);
        }

        _position = end + 1;
    }

    private char Peek(int offset)
    {
        var index = _position + offset;
        return index < line.Length ? line[index] : '\0';
    }

    private void SkipWhitespace()
    {
        while (_position < line.Length && (line[_position] == ' ' || line[_position] == '\t'))
        {
            _position++;
        }
    }
}
