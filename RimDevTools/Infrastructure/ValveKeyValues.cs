namespace RimDevTools;

public sealed record ValveKeyValue(string Name, string? Value, IReadOnlyList<ValveKeyValue> Children)
{
    public bool IsObject => Value is null;

    public ValveKeyValue? Child(string name)
        => Children.FirstOrDefault(child => child.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public string? Scalar(string name) => Child(name)?.Value;
}

public static class ValveKeyValues
{
    public static IReadOnlyList<ValveKeyValue> Parse(string text)
    {
        var parser = new Parser(text);
        return parser.ParseDocument();
    }

    public static IReadOnlyList<ValveKeyValue> Load(string path) => Parse(File.ReadAllText(path));

    private sealed class Parser(string text)
    {
        private int _position;

        public IReadOnlyList<ValveKeyValue> ParseDocument()
        {
            var values = ParseValues(expectClosingBrace: false);
            SkipTrivia();
            if (_position != text.Length) throw Error("文件末尾存在无法解析的内容");
            return values;
        }

        private List<ValveKeyValue> ParseValues(bool expectClosingBrace)
        {
            var values = new List<ValveKeyValue>();
            while (true)
            {
                SkipTrivia();
                if (_position >= text.Length)
                {
                    if (expectClosingBrace) throw Error("对象缺少结束大括号");
                    return values;
                }
                if (text[_position] == '}')
                {
                    if (!expectClosingBrace) throw Error("出现多余的结束大括号");
                    _position++;
                    return values;
                }

                var name = ReadToken();
                SkipTrivia();
                if (_position >= text.Length) throw Error($"键 {name} 缺少值");
                if (text[_position] == '{')
                {
                    _position++;
                    values.Add(new ValveKeyValue(name, null, ParseValues(expectClosingBrace: true)));
                }
                else
                {
                    values.Add(new ValveKeyValue(name, ReadToken(), []));
                }
            }
        }

        private string ReadToken()
        {
            SkipTrivia();
            if (_position >= text.Length) throw Error("缺少标记");
            if (text[_position] != '"')
            {
                var start = _position;
                while (_position < text.Length && !char.IsWhiteSpace(text[_position]) && text[_position] is not '{' and not '}') _position++;
                if (start == _position) throw Error("标记为空");
                return text[start.._position];
            }

            _position++;
            var value = new System.Text.StringBuilder();
            while (_position < text.Length)
            {
                var current = text[_position++];
                if (current == '"') return value.ToString();
                if (current != '\\')
                {
                    value.Append(current);
                    continue;
                }
                if (_position >= text.Length) throw Error("字符串转义不完整");
                var escaped = text[_position++];
                value.Append(escaped switch { '"' => '"', '\\' => '\\', 'n' => '\n', 'r' => '\r', 't' => '\t', _ => escaped });
            }
            throw Error("字符串缺少结束引号");
        }

        private void SkipTrivia()
        {
            while (_position < text.Length)
            {
                if (char.IsWhiteSpace(text[_position])) { _position++; continue; }
                if (_position + 1 < text.Length && text[_position] == '/' && text[_position + 1] == '/')
                {
                    _position += 2;
                    while (_position < text.Length && text[_position] != '\n') _position++;
                    continue;
                }
                break;
            }
        }

        private InvalidDataException Error(string message) => new($"Valve KeyValues 解析失败（位置 {_position}）：{message}");
    }
}
