using System.Globalization;

namespace AltPane.Core.Search;

/// <summary>
/// A small arithmetic parser. It never calls eval or DataTable.Compute.
/// An invalid expression, including division by zero, is not a result.
/// </summary>
public static class Expression
{
    public readonly record struct Value(string Plain, string Shown);

    public static bool TryEvaluate(string? text, out Value value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var source = Normalize(text.Trim());
        if (source.StartsWith('=')) source = source[1..].Trim();
        if (source.Length == 0) return false;
        try
        {
            var parser = new Parser(source);
            var number = parser.Parse();
            if (!parser.Finished || double.IsNaN(number) || double.IsInfinity(number)) return false;
            value = new Value(Format(number), "= " + Format(number));
            return true;
        }
        catch (ArithmeticException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static string Normalize(string text)
    {
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = chars[i] switch
            {
                '＋' or '﹢' => '+',
                '－' or '−' or '﹣' => '-',
                '×' or '✕' => '*',
                '÷' => '/',
                '（' => '(',
                '）' => ')',
                '＝' => '=',
                _ => chars[i]
            };
        }
        return new string(chars);
    }

    /// <summary>Plain invariant text, no grouping separators, trailing binary noise rounded away.</summary>
    public static string Format(double number)
    {
        if (double.IsNaN(number) || double.IsInfinity(number))
            throw new ArithmeticException();
        var rounded = Math.Abs(number) >= 1e12 || (number != 0 && Math.Abs(number) < 1e-8)
            ? number
            : Math.Round(number, 10, MidpointRounding.AwayFromZero);
        if (Math.Abs(rounded - Math.Round(rounded)) < 1e-9 && Math.Abs(rounded) < 1e15)
            return Math.Round(rounded).ToString("0", CultureInfo.InvariantCulture);
        if (Math.Abs(number) >= 1e12 || (number != 0 && Math.Abs(number) < 1e-8))
            return number.ToString("G10", CultureInfo.InvariantCulture);
        return rounded.ToString("0.##########", CultureInfo.InvariantCulture);
    }

    sealed class Parser
    {
        readonly string _text;
        int _at;

        public Parser(string text) => _text = text;
        public bool Finished
        {
            get
            {
                Skip();
                return _at >= _text.Length;
            }
        }

        public double Parse()
        {
            var value = Expression();
            Skip();
            if (_at < _text.Length) throw new FormatException();
            return value;
        }

        double Expression()
        {
            var value = Term();
            while (true)
            {
                Skip();
                if (Take('+')) value += Term();
                else if (Take('-')) value -= Term();
                else return value;
            }
        }

        double Term()
        {
            var value = Power();
            while (true)
            {
                Skip();
                if (Take('*')) value *= Power();
                else if (Take('/'))
                {
                    var divisor = Power();
                    if (divisor == 0) throw new ArithmeticException();
                    value /= divisor;
                }
                else if (Take('%'))
                {
                    var divisor = Power();
                    if (divisor == 0) throw new ArithmeticException();
                    value %= divisor;
                }
                else return value;
            }
        }

        double Power()
        {
            var value = Unary();
            Skip();
            if (!Take('^')) return value;
            return Math.Pow(value, Power());
        }

        double Unary()
        {
            Skip();
            if (Take('+')) return Unary();
            if (Take('-')) return -Unary();
            return Primary();
        }

        double Primary()
        {
            Skip();
            if (Take('('))
            {
                var value = Expression();
                Skip();
                if (!Take(')')) throw new FormatException();
                return value;
            }
            if (_at < _text.Length && char.IsLetter(_text[_at]))
                return Ident();
            return Number();
        }

        double Ident()
        {
            var start = _at;
            while (_at < _text.Length && char.IsLetter(_text[_at])) _at++;
            var name = _text[start.._at];
            Skip();
            if (Take('('))
            {
                var argument = Expression();
                Skip();
                if (!Take(')')) throw new FormatException();
                return Call(name, argument);
            }
            return name.ToLowerInvariant() switch
            {
                "pi" => Math.PI,
                "e" => Math.E,
                _ => throw new FormatException()
            };
        }

        static double Call(string name, double argument) => name.ToLowerInvariant() switch
        {
            "sqrt" => argument < 0 ? throw new ArithmeticException() : Math.Sqrt(argument),
            "abs" => Math.Abs(argument),
            "round" => Math.Round(argument, MidpointRounding.AwayFromZero),
            "sin" => Math.Sin(argument),
            "cos" => Math.Cos(argument),
            "tan" => Math.Tan(argument),
            "log" => argument <= 0 ? throw new ArithmeticException() : Math.Log10(argument),
            "ln" => argument <= 0 ? throw new ArithmeticException() : Math.Log(argument),
            _ => throw new FormatException()
        };

        double Number()
        {
            var start = _at;
            while (_at < _text.Length && char.IsDigit(_text[_at])) _at++;
            if (_at < _text.Length && _text[_at] == '.')
            {
                _at++;
                while (_at < _text.Length && char.IsDigit(_text[_at])) _at++;
            }
            if (_at < _text.Length && _text[_at] is 'e' or 'E' && _at + 1 < _text.Length)
            {
                var mark = _at;
                var next = _at + 1;
                if (_text[next] is '+' or '-') next++;
                var digits = next;
                while (digits < _text.Length && char.IsDigit(_text[digits])) digits++;
                if (digits > next)
                    _at = digits;
                else
                    _at = mark;
            }
            if (_at == start) throw new FormatException();
            if (!double.TryParse(_text[start.._at], NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                throw new FormatException();
            return number;
        }

        void Skip()
        {
            while (_at < _text.Length && char.IsWhiteSpace(_text[_at])) _at++;
        }

        bool Take(char token)
        {
            if (_at >= _text.Length || _text[_at] != token) return false;
            _at++;
            return true;
        }
    }
}
