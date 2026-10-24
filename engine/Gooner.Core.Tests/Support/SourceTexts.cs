using System.Globalization;
using System.Text;

namespace Gooner.Core.Tests.Support;

// The texts the code hands to the catalogues, read from the source itself: every literal passed to
// L10n.T, L10n.N or L10n.F, and both forms passed to L10n.P. That is what each language has to
// translate - LocaleFilesTests fails on a text added to the code but not to a catalogue, and on a
// translation whose text has gone.
//
// It reads as much C# as that takes: comments, regular, verbatim and interpolated strings, char
// literals, and literals joined with +. A text handed over as an interpolated string can never be in
// a catalogue - its words differ every time - so that is reported rather than skipped.
internal static class SourceTexts
{
    public sealed record Text(string Value, string Where);

    public sealed record Scan(IReadOnlyList<Text> Texts, IReadOnlyList<string> Problems);

    private static readonly HashSet<string> Methods = ["T", "N", "F", "P"];

    public static Scan Read(params string[] dirs)
    {
        var texts = new List<Text>();
        var problems = new List<string>();
        foreach (var dir in dirs)
        {
            foreach (var file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(dir, file);
                if (relative.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj" or ".godot")) continue;
                var scan = ReadSource(File.ReadAllText(file), $"{Path.GetFileName(dir)}/{relative.Replace('\\', '/')}");
                texts.AddRange(scan.Texts);
                problems.AddRange(scan.Problems);
            }
        }
        return new Scan(texts, problems);
    }

    public static Scan ReadSource(string source, string where)
    {
        var tokens = new Lexer(source).Tokens();
        var texts = new List<Text>();
        var problems = new List<string>();
        for (var i = 0; i + 3 < tokens.Count; i++)
        {
            if (!(tokens[i].Is(Kind.Word, "L10n") && tokens[i + 1].Is(Kind.Symbol, ".")
                && tokens[i + 2].Kind == Kind.Word && Methods.Contains(tokens[i + 2].Value) && tokens[i + 3].Is(Kind.Symbol, "(")))
                continue;
            var method = tokens[i + 2].Value;
            var at = $"{where}:{tokens[i].Line}";
            var next = i + 4;
            if (method == "P")
            {
                next = SkipArgument(tokens, next);
                for (var form = 0; form < 2 && next < tokens.Count && tokens[next].Is(Kind.Symbol, ","); form++)
                    next = Literal(tokens, next + 1, method, at, texts, problems);
            }
            else
            {
                Literal(tokens, next, method, at, texts, problems);
            }
        }
        return new Scan(texts, problems);
    }

    // A literal, or literals joined with +, at tokens[start]; the index after it. Anything else is
    // a text that is translated here but written somewhere else, and not this scan's business.
    private static int Literal(List<Token> tokens, int start, string method, string at, List<Text> texts, List<string> problems)
    {
        if (start >= tokens.Count) return start;
        if (tokens[start].Kind == Kind.Interpolated)
        {
            problems.Add($"{at}: L10n.{method} is given an interpolated string, which no catalogue can hold - give it a format and its values (L10n.F) instead.");
            return start + 1;
        }
        if (tokens[start].Kind != Kind.Literal) return start;
        var value = new StringBuilder(tokens[start].Value);
        var i = start + 1;
        while (i + 1 < tokens.Count && tokens[i].Is(Kind.Symbol, "+"))
        {
            if (tokens[i + 1].Kind != Kind.Literal)
            {
                problems.Add($"{at}: L10n.{method} is given a literal joined with something that is not one - the catalogue would hold only part of the text.");
                return i + 1;
            }
            value.Append(tokens[i + 1].Value);
            i += 2;
        }
        texts.Add(new Text(value.ToString(), at));
        return i;
    }

    // Past one argument: up to the comma or closing parenthesis that ends it at its own depth.
    private static int SkipArgument(List<Token> tokens, int start)
    {
        var depth = 0;
        for (var i = start; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Kind != Kind.Symbol) continue;
            if (token.Value is "(" or "[" or "{") depth++;
            else if (token.Value is ")" or "]" or "}")
            {
                if (depth == 0) return i;
                depth--;
            }
            else if (token.Value == "," && depth == 0) return i;
        }
        return tokens.Count;
    }

    private enum Kind { Word, Symbol, Literal, Interpolated, Other }

    private readonly record struct Token(Kind Kind, string Value, int Line)
    {
        public bool Is(Kind kind, string value) => Kind == kind && Value == value;
    }

    private sealed class Lexer(string source)
    {
        private readonly string _s = source;
        private int _i;
        private int _line = 1;

        private char At(int offset = 0) => _i + offset < _s.Length ? _s[_i + offset] : '\0';

        public List<Token> Tokens()
        {
            var tokens = new List<Token>();
            while (_i < _s.Length)
            {
                var c = At();
                if (c == '\n')
                {
                    _line++;
                    _i++;
                }
                else if (char.IsWhiteSpace(c))
                {
                    _i++;
                }
                else if (c == '/' && At(1) == '/')
                {
                    while (_i < _s.Length && At() != '\n') _i++;
                }
                else if (c == '/' && At(1) == '*')
                {
                    _i += 2;
                    while (_i < _s.Length && !(At() == '*' && At(1) == '/')) Step();
                    _i += 2;
                }
                else if (c == '\'')
                {
                    SkipChar();
                }
                else if (StartsString())
                {
                    var line = _line;
                    var (value, interpolated) = ReadString();
                    tokens.Add(new Token(interpolated ? Kind.Interpolated : Kind.Literal, value, line));
                }
                else if (char.IsLetter(c) || c == '_')
                {
                    var start = _i;
                    while (char.IsLetterOrDigit(At()) || At() == '_') _i++;
                    tokens.Add(new Token(Kind.Word, _s[start.._i], _line));
                }
                else if (char.IsDigit(c))
                {
                    while (char.IsLetterOrDigit(At()) || At() is '_' or '.') _i++;
                    tokens.Add(new Token(Kind.Other, "", _line));
                }
                else
                {
                    tokens.Add(new Token(Kind.Symbol, c.ToString(), _line));
                    _i++;
                }
            }
            return tokens;
        }

        private void Step()
        {
            if (At() == '\n') _line++;
            _i++;
        }

        private bool StartsString()
        {
            var j = 0;
            while (j < 2 && At(j) is '$' or '@') j++;
            return At(j) == '"';
        }

        private void SkipChar()
        {
            _i++;
            while (_i < _s.Length && At() != '\'')
            {
                if (At() == '\\') _i++;
                _i++;
            }
            _i++;
        }

        private (string Value, bool Interpolated) ReadString()
        {
            bool verbatim = false, interpolated = false;
            while (At() != '"')
            {
                verbatim |= At() == '@';
                interpolated |= At() == '$';
                _i++;
            }
            if (At(1) == '"' && At(2) == '"')
                throw new NotSupportedException($"line {_line}: raw string literals are not read by this scan");
            _i++;
            var value = new StringBuilder();
            while (_i < _s.Length)
            {
                var c = At();
                if (c == '"')
                {
                    if (verbatim && At(1) == '"')
                    {
                        value.Append('"');
                        _i += 2;
                        continue;
                    }
                    _i++;
                    return (value.ToString(), interpolated);
                }
                if (interpolated && c == '{')
                {
                    if (At(1) == '{')
                    {
                        value.Append('{');
                        _i += 2;
                        continue;
                    }
                    SkipHole();
                    continue;
                }
                if (interpolated && c == '}' && At(1) == '}')
                {
                    value.Append('}');
                    _i += 2;
                    continue;
                }
                if (!verbatim && c == '\\')
                {
                    value.Append(Escape());
                    continue;
                }
                value.Append(c);
                Step();
            }
            throw new FormatException($"line {_line}: a string runs to the end of the file");
        }

        // An interpolation hole, strings and all, up to the brace that closes it.
        private void SkipHole()
        {
            _i++;
            var depth = 0;
            while (_i < _s.Length)
            {
                var c = At();
                if (StartsString())
                {
                    ReadString();
                    continue;
                }
                if (c == '\'')
                {
                    SkipChar();
                    continue;
                }
                if (c == '{') depth++;
                else if (c == '}')
                {
                    if (depth == 0)
                    {
                        _i++;
                        return;
                    }
                    depth--;
                }
                Step();
            }
        }

        private string Escape()
        {
            var kind = At(1);
            _i += 2;
            switch (kind)
            {
                case 'n': return "\n";
                case 'r': return "\r";
                case 't': return "\t";
                case '0': return "\0";
                case '\\': return "\\";
                case '"': return "\"";
                case '\'': return "'";
                case 'u':
                case 'U':
                    var length = kind == 'u' ? 4 : 8;
                    var code = int.Parse(_s.AsSpan(_i, length), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    _i += length;
                    return char.ConvertFromUtf32(code);
                default:
                    throw new FormatException($"line {_line}: the escape \\{kind} is not read by this scan");
            }
        }
    }
}
