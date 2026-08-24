namespace Tabsh;

// set /a, cmd's own expression language, down to the operators it takes and the order it takes them in.
// A name that holds nothing counts as zero, which is what lets a counter be added to before it exists.
internal sealed class Arithmetic(ShellEnvironment environment)
{
    private string _text = string.Empty;
    private int _at;

    public bool TryEvaluate(string expression, out long value)
    {
        ArgumentNullException.ThrowIfNull(expression);

        _text = expression;
        _at = 0;
        value = 0;

        try
        {
            do
            {
                value = Assignment();
            }
            while (Take(','));

            Skip();
            return _at == _text.Length;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    // the assignment operators, which are the only ones that read a name rather than a value on the left.
    private long Assignment()
    {
        Skip();
        var mark = _at;
        var name = Name();
        if (name != null)
        {
            Skip();
            var op = Operator();
            if (op != null)
            {
                var right = Assignment();
                var left = ValueOf(name);
                var result = op.Length == 1 ? right : Apply(op[..^1], left, right);
                environment.Set(name, result.ToString(CultureInfo.InvariantCulture));
                return result;
            }
        }

        _at = mark;
        return Or();
    }

    private string? Operator()
    {
        foreach (var candidate in _assignments)
        {
            if (_at + candidate.Length <= _text.Length && _text.AsSpan(_at, candidate.Length).SequenceEqual(candidate))
            {
                // "==" is not an assignment, and neither is anything else that only starts like one.
                if (candidate == "=" && _at + 1 < _text.Length && _text[_at + 1] == '=')
                    return null;

                _at += candidate.Length;
                return candidate;
            }
        }

        return null;
    }

    private long Or()
    {
        var value = Xor();
        while (true)
        {
            Skip();
            if (_at < _text.Length && _text[_at] == '|' && (_at + 1 >= _text.Length || _text[_at + 1] != '|'))
            {
                _at++;
                value |= Xor();
                continue;
            }

            return value;
        }
    }

    private long Xor()
    {
        var value = And();
        while (Take('^'))
        {
            value ^= And();
        }

        return value;
    }

    private long And()
    {
        var value = Shift();
        while (true)
        {
            Skip();
            if (_at < _text.Length && _text[_at] == '&' && (_at + 1 >= _text.Length || _text[_at + 1] != '&'))
            {
                _at++;
                value &= Shift();
                continue;
            }

            return value;
        }
    }

    private long Shift()
    {
        var value = Additive();
        while (true)
        {
            Skip();
            if (Take("<<"))
            {
                value <<= (int)Additive();
                continue;
            }

            if (Take(">>"))
            {
                value >>= (int)Additive();
                continue;
            }

            return value;
        }
    }

    private long Additive()
    {
        var value = Multiplicative();
        while (true)
        {
            Skip();
            if (Take('+'))
            {
                value += Multiplicative();
                continue;
            }

            if (Take('-'))
            {
                value -= Multiplicative();
                continue;
            }

            return value;
        }
    }

    private long Multiplicative()
    {
        var value = Unary();
        while (true)
        {
            Skip();
            if (Take('*'))
            {
                value *= Unary();
                continue;
            }

            if (Take('/'))
            {
                var divisor = Unary();
                if (divisor == 0)
                    throw new FormatException();

                value /= divisor;
                continue;
            }

            if (Take('%'))
            {
                var divisor = Unary();
                if (divisor == 0)
                    throw new FormatException();

                value %= divisor;
                continue;
            }

            return value;
        }
    }

    private long Unary()
    {
        Skip();
        if (Take('-'))
            return -Unary();

        if (Take('+'))
            return Unary();

        if (Take('~'))
            return ~Unary();

        if (Take('!'))
            return Unary() == 0 ? 1 : 0;

        return Primary();
    }

    private long Primary()
    {
        Skip();
        if (Take('('))
        {
            var value = Assignment();
            Skip();
            if (!Take(')'))
                throw new FormatException();

            return value;
        }

        var name = Name();
        if (name != null)
            return ValueOf(name);

        return Number();
    }

    // decimal, or hexadecimal behind 0x, or octal behind a lone leading zero, which is what cmd reads.
    private long Number()
    {
        Skip();
        var start = _at;
        if (_at + 1 < _text.Length && _text[_at] == '0' && (_text[_at + 1] is 'x' or 'X'))
        {
            _at += 2;
            var from = _at;
            while (_at < _text.Length && Uri.IsHexDigit(_text[_at]))
            {
                _at++;
            }

            if (_at == from)
                throw new FormatException();

            return Convert.ToInt64(_text[from.._at], 16);
        }

        while (_at < _text.Length && char.IsAsciiDigit(_text[_at]))
        {
            _at++;
        }

        if (_at == start)
            throw new FormatException();

        var digits = _text[start.._at];
        if (digits.Length > 1 && digits[0] == '0')
            return Convert.ToInt64(digits, 8);

        return long.Parse(digits, CultureInfo.InvariantCulture);
    }

    private string? Name()
    {
        Skip();
        var start = _at;
        while (_at < _text.Length && (char.IsLetter(_text[_at]) || _text[_at] == '_' || (_at > start && char.IsAsciiDigit(_text[_at]))))
        {
            _at++;
        }

        return _at == start ? null : _text[start.._at];
    }

    private long ValueOf(string name) =>
        long.TryParse(environment.Get(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static long Apply(string op, long left, long right) => op switch
    {
        "+" => left + right,
        "-" => left - right,
        "*" => left * right,
        "/" => right == 0 ? throw new FormatException() : left / right,
        "%" => right == 0 ? throw new FormatException() : left % right,
        "&" => left & right,
        "|" => left | right,
        "^" => left ^ right,
        "<<" => left << (int)right,
        ">>" => left >> (int)right,
        _ => throw new FormatException(),
    };

    private void Skip()
    {
        while (_at < _text.Length && char.IsWhiteSpace(_text[_at]))
        {
            _at++;
        }
    }

    private bool Take(char c)
    {
        Skip();
        if (_at >= _text.Length || _text[_at] != c)
            return false;

        _at++;
        return true;
    }

    private bool Take(string s)
    {
        Skip();
        if (_at + s.Length > _text.Length || !_text.AsSpan(_at, s.Length).SequenceEqual(s))
            return false;

        _at += s.Length;
        return true;
    }

    // longest first, so that "<<=" is never read as "<<" and then something else.
    private static readonly string[] _assignments = ["<<=", ">>=", "*=", "/=", "%=", "+=", "-=", "&=", "^=", "|=", "="];
}
