using System.Globalization;

namespace NodeEditor.Core.Expression;

/// <summary>
/// Tiny safe expression evaluator used by inline drag-float/int text-edit
/// mode. Whitelist: + - * / % ^, constants pi/tau/e, functions sin cos tan
/// asin acos atan sqrt abs floor ceil round min max clamp deg rad,
/// suffix `deg` and `rad`. Recursive-descent parser.
/// </summary>
public static class ExpressionEvaluator
{
    /// <summary>Result of an evaluation attempt.</summary>
    public readonly record struct Result(bool Success, double Value, string? Error)
    {
        public static Result Ok(double v) => new(true, v, null);
        public static Result Fail(string e) => new(false, double.NaN, e);
    }

    /// <summary>Evaluate an expression. Returns failure with message on parse error.</summary>
    public static Result Evaluate(string expr)
    {
        if (string.IsNullOrWhiteSpace(expr))
            return Result.Fail("Empty expression.");

        var parser = new Parser(expr);
        try
        {
            var v = parser.ParseFullExpression();
            return Result.Ok(v);
        }
        catch (FormatException ex)
        {
            return Result.Fail(ex.Message);
        }
    }

    private sealed class Parser
    {
        private readonly string _s;
        private int _i;

        public Parser(string s) { _s = s; _i = 0; }

        public double ParseFullExpression()
        {
            var v = ParseExpr();
            SkipWs();
            if (_i < _s.Length)
                throw new FormatException($"Unexpected character at position {_i}: '{_s[_i]}'");
            return v;
        }

        // expr := term (('+'|'-') term)*
        private double ParseExpr()
        {
            var v = ParseTerm();
            while (true)
            {
                SkipWs();
                if (Peek('+')) { _i++; v += ParseTerm(); }
                else if (Peek('-')) { _i++; v -= ParseTerm(); }
                else break;
            }
            return v;
        }

        // term := power (('*'|'/'|'%') power)*
        private double ParseTerm()
        {
            var v = ParsePower();
            while (true)
            {
                SkipWs();
                if (Peek('*')) { _i++; v *= ParsePower(); }
                else if (Peek('/')) { _i++; v /= ParsePower(); }
                else if (Peek('%')) { _i++; v %= ParsePower(); }
                else break;
            }
            return v;
        }

        // power := unary ('^' power)?     (right-associative)
        private double ParsePower()
        {
            var v = ParseUnary();
            SkipWs();
            if (Peek('^')) { _i++; v = Math.Pow(v, ParsePower()); }
            return v;
        }

        // unary := '-' unary | primary
        private double ParseUnary()
        {
            SkipWs();
            if (Peek('-')) { _i++; return -ParseUnary(); }
            if (Peek('+')) { _i++; return ParseUnary(); }
            return ParsePrimary();
        }

        // primary := number | identifier | '(' expr ')'  with optional suffix 'deg' or 'rad'
        private double ParsePrimary()
        {
            SkipWs();
            double v;

            if (Peek('('))
            {
                _i++;
                v = ParseExpr();
                SkipWs();
                if (!Peek(')')) throw new FormatException("Missing ')'");
                _i++;
            }
            else if (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.'))
            {
                v = ParseNumber();
            }
            else if (_i < _s.Length && char.IsLetter(_s[_i]))
            {
                v = ParseIdentifier();
            }
            else
            {
                throw new FormatException($"Unexpected character at position {_i}");
            }

            // Suffix
            SkipWs();
            if (MatchKeyword("deg")) v = v * Math.PI / 180.0;
            else if (MatchKeyword("rad")) { /* no-op */ }
            return v;
        }

        private double ParseNumber()
        {
            int start = _i;
            while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.'))
                _i++;
            // Scientific notation: e[+-]?digits
            if (_i < _s.Length && (_s[_i] == 'e' || _s[_i] == 'E'))
            {
                _i++;
                if (_i < _s.Length && (_s[_i] == '+' || _s[_i] == '-')) _i++;
                while (_i < _s.Length && char.IsDigit(_s[_i])) _i++;
            }
            var slice = _s.AsSpan(start, _i - start);
            if (!double.TryParse(slice, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                throw new FormatException($"Invalid number '{slice.ToString()}'");
            return v;
        }

        private double ParseIdentifier()
        {
            int start = _i;
            while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '_'))
                _i++;
            var name = _s.Substring(start, _i - start).ToLowerInvariant();

            // Constants
            if (name == "pi")  return Math.PI;
            if (name == "tau") return Math.PI * 2;
            if (name == "e")   return Math.E;

            // Suffix keywords: caller's ParsePrimary handles deg/rad. If we
            // see them here as primary, treat as 0 (shouldn't happen if
            // parser balanced).
            // Functions: name '(' args ')'
            SkipWs();
            if (Peek('('))
            {
                _i++;
                var args = new List<double>();
                SkipWs();
                if (!Peek(')'))
                {
                    args.Add(ParseExpr());
                    SkipWs();
                    while (Peek(','))
                    {
                        _i++;
                        args.Add(ParseExpr());
                        SkipWs();
                    }
                }

                if (!Peek(')')) throw new FormatException("Missing ')'");
                _i++;
                return CallFunction(name, args);
            }

            throw new FormatException($"Unknown identifier '{name}'");
        }

        private static double CallFunction(string name, List<double> args)
        {
            return (name, args.Count) switch
            {
                ("sin", 1)   => Math.Sin(args[0]),
                ("cos", 1)   => Math.Cos(args[0]),
                ("tan", 1)   => Math.Tan(args[0]),
                ("asin", 1)  => Math.Asin(args[0]),
                ("acos", 1)  => Math.Acos(args[0]),
                ("atan", 1)  => Math.Atan(args[0]),
                ("sqrt", 1)  => Math.Sqrt(args[0]),
                ("abs", 1)   => Math.Abs(args[0]),
                ("floor", 1) => Math.Floor(args[0]),
                ("ceil", 1)  => Math.Ceiling(args[0]),
                ("round", 1) => Math.Round(args[0]),
                ("min", 2)   => Math.Min(args[0], args[1]),
                ("max", 2)   => Math.Max(args[0], args[1]),
                ("clamp", 3) => Math.Clamp(args[0], args[1], args[2]),
                ("deg", 1)   => args[0] * 180.0 / Math.PI,
                ("rad", 1)   => args[0] * Math.PI / 180.0,
                _ => throw new FormatException($"Unknown function '{name}'/{args.Count}"),
            };
        }

        private bool MatchKeyword(string kw)
        {
            SkipWs();
            if (_i + kw.Length > _s.Length) return false;
            for (int k = 0; k < kw.Length; k++)
                if (char.ToLowerInvariant(_s[_i + k]) != kw[k]) return false;
            // Must not be followed by identifier char.
            int after = _i + kw.Length;
            if (after < _s.Length && (char.IsLetterOrDigit(_s[after]) || _s[after] == '_'))
                return false;
            _i = after;
            return true;
        }

        private bool Peek(char c) => _i < _s.Length && _s[_i] == c;

        private void SkipWs()
        {
            while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
        }
    }
}
