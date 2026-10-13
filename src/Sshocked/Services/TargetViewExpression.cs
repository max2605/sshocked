namespace Sshocked.Services;

public sealed class TargetViewExpression
{
    private readonly List<(string Value, bool Quoted)> _tokens;
    private int _position;

    private TargetViewExpression(string expression) => _tokens = Tokenize(expression);

    public static Func<IEnumerable<string>, bool> Compile(string expression)
    {
        var parser = new TargetViewExpression(expression);
        var predicate = parser.ParseOr(0);
        if (parser._position != parser._tokens.Count) throw new ArgumentException("Unexpected token in tag expression.");
        return tags => predicate(tags.Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    private Func<HashSet<string>, bool> ParseOr(int depth)
    {
        var result = ParseAnd(depth);
        while (Take("OR"))
        {
            var left = result;
            var right = ParseAnd(depth);
            result = tags => left(tags) || right(tags);
        }
        return result;
    }

    private Func<HashSet<string>, bool> ParseAnd(int depth)
    {
        var result = ParseUnary(depth);
        while (Take("AND"))
        {
            var left = result;
            var right = ParseUnary(depth);
            result = tags => left(tags) && right(tags);
        }
        return result;
    }

    private Func<HashSet<string>, bool> ParseUnary(int depth)
    {
        if (depth > 32) throw new ArgumentException("Tag expression nesting exceeds 32 levels.");
        if (Take("NOT"))
        {
            var value = ParseUnary(depth + 1);
            return tags => !value(tags);
        }
        if (Take("("))
        {
            var value = ParseOr(depth + 1);
            if (!Take(")")) throw new ArgumentException("Tag expression has an unmatched parenthesis.");
            return value;
        }
        if (_position == _tokens.Count) throw new ArgumentException("Expected a tag after the operator.");
        var token = _tokens[_position++];
        if (!token.Quoted && (token.Value is "(" or ")" ||
            token.Value.Equals("AND", StringComparison.OrdinalIgnoreCase) ||
            token.Value.Equals("OR", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Expected a tag. Quote tags named AND, OR or NOT.");
        return tags => tags.Contains(token.Value);
    }

    private bool Take(string value)
    {
        if (_position == _tokens.Count || _tokens[_position].Quoted ||
            !_tokens[_position].Value.Equals(value, StringComparison.OrdinalIgnoreCase)) return false;
        _position++;
        return true;
    }

    private static List<(string Value, bool Quoted)> Tokenize(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression) || expression.Length > 1024 || expression.Any(char.IsControl))
            throw new ArgumentException("Use a tag expression of 1 to 1024 characters without control characters.");
        var result = new List<(string, bool)>();
        for (var index = 0; index < expression.Length;)
        {
            if (char.IsWhiteSpace(expression[index])) { index++; continue; }
            if (expression[index] is '(' or ')') { result.Add((expression[index++].ToString(), false)); continue; }
            if (expression[index] is '"' or '\'')
            {
                var quote = expression[index++];
                var value = new System.Text.StringBuilder();
                var closed = false;
                while (index < expression.Length)
                {
                    var character = expression[index++];
                    if (character == quote) { closed = true; break; }
                    if (character == '\\' && index < expression.Length && expression[index] is '\\' or '\'' or '"')
                        character = expression[index++];
                    value.Append(character);
                }
                if (!closed || value.ToString().Trim().Length == 0) throw new ArgumentException("Tag expression has an empty or unclosed quoted tag.");
                result.Add((value.ToString().Trim(), true));
            }
            else
            {
                var start = index;
                while (index < expression.Length && !char.IsWhiteSpace(expression[index]) && expression[index] is not '(' and not ')') index++;
                var value = expression[start..index];
                if (value.Contains('"') || value.Contains('\'')) throw new ArgumentException("Quote the complete tag containing quote characters.");
                result.Add((value, false));
            }
            if (result.Count > 256) throw new ArgumentException("Tag expression exceeds 256 tokens.");
        }
        return result;
    }
}
