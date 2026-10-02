using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GBible
{
    // 외부 의존 없는 최소 JSON. 저장 데이터와 서버용 내보내기에 쓴다.
    // 지원: null, bool, 숫자(double), string, 배열(List<object>), 객체(Dictionary<string, object>).
    public static class MiniJson
    {
        public static string Serialize(object value)
        {
            var builder = new StringBuilder();
            Write(builder, value);
            return builder.ToString();
        }

        static void Write(StringBuilder b, object value)
        {
            switch (value)
            {
                case null:
                    b.Append("null");
                    return;
                case bool flag:
                    b.Append(flag ? "true" : "false");
                    return;
                case string text:
                    WriteString(b, text);
                    return;
                case double d:
                    if (double.IsNaN(d) || double.IsInfinity(d)) b.Append("null");
                    else b.Append(d.ToString("R", CultureInfo.InvariantCulture));
                    return;
                case float f:
                    Write(b, (double)f);
                    return;
                case int i:
                    b.Append(i.ToString(CultureInfo.InvariantCulture));
                    return;
                case long l:
                    b.Append(l.ToString(CultureInfo.InvariantCulture));
                    return;
                case IDictionary<string, object> map:
                    b.Append('{');
                    var first = true;
                    foreach (var pair in map)
                    {
                        if (!first) b.Append(',');
                        first = false;
                        WriteString(b, pair.Key);
                        b.Append(':');
                        Write(b, pair.Value);
                    }
                    b.Append('}');
                    return;
                case System.Collections.IEnumerable list:
                    b.Append('[');
                    var firstItem = true;
                    foreach (var item in list)
                    {
                        if (!firstItem) b.Append(',');
                        firstItem = false;
                        Write(b, item);
                    }
                    b.Append(']');
                    return;
            }
            throw new ArgumentException($"[MomoiProject] JSON으로 쓸 수 없는 값 형식입니다: {value.GetType().Name}");
        }

        static void WriteString(StringBuilder b, string text)
        {
            b.Append('"');
            foreach (var c in text)
            {
                switch (c)
                {
                    case '"': b.Append("\\\""); break;
                    case '\\': b.Append("\\\\"); break;
                    case '\n': b.Append("\\n"); break;
                    case '\r': b.Append("\\r"); break;
                    case '\t': b.Append("\\t"); break;
                    default:
                        if (c < 0x20) b.Append("\\u").Append(((int)c).ToString("x4"));
                        else b.Append(c);
                        break;
                }
            }
            b.Append('"');
        }

        public static object Deserialize(string json)
        {
            var parser = new Parser(json);
            var value = parser.ReadValue();
            parser.SkipWhitespace();
            if (!parser.AtEnd) throw parser.Fail("값 뒤에 알 수 없는 내용이 있습니다");
            return value;
        }

        sealed class Parser
        {
            readonly string _text;
            int _pos;

            public Parser(string text) => _text = text ?? throw new ArgumentNullException(nameof(text));

            public bool AtEnd => _pos >= _text.Length;

            public FormatException Fail(string message) => new FormatException($"[MomoiProject] JSON 오류 ({_pos}번째 문자): {message}");

            public void SkipWhitespace()
            {
                while (!AtEnd && char.IsWhiteSpace(_text[_pos])) _pos++;
            }

            public object ReadValue()
            {
                SkipWhitespace();
                if (AtEnd) throw Fail("값이 없습니다");
                var c = _text[_pos];
                switch (c)
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || char.IsDigit(c)) return ReadNumber();
                        throw Fail($"예상하지 못한 문자 '{c}'");
                }
            }

            void Expect(string word)
            {
                if (string.CompareOrdinal(_text, _pos, word, 0, word.Length) != 0) throw Fail($"'{word}'가 필요합니다");
                _pos += word.Length;
            }

            Dictionary<string, object> ReadObject()
            {
                var map = new Dictionary<string, object>(StringComparer.Ordinal);
                _pos++;
                SkipWhitespace();
                if (!AtEnd && _text[_pos] == '}') { _pos++; return map; }
                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || _text[_pos] != '"') throw Fail("객체 키가 필요합니다");
                    var key = ReadString();
                    SkipWhitespace();
                    if (AtEnd || _text[_pos] != ':') throw Fail("':'가 필요합니다");
                    _pos++;
                    map[key] = ReadValue();
                    SkipWhitespace();
                    if (AtEnd) throw Fail("객체가 닫히지 않았습니다");
                    if (_text[_pos] == ',') { _pos++; continue; }
                    if (_text[_pos] == '}') { _pos++; return map; }
                    throw Fail("',' 또는 '}'가 필요합니다");
                }
            }

            List<object> ReadArray()
            {
                var list = new List<object>();
                _pos++;
                SkipWhitespace();
                if (!AtEnd && _text[_pos] == ']') { _pos++; return list; }
                while (true)
                {
                    list.Add(ReadValue());
                    SkipWhitespace();
                    if (AtEnd) throw Fail("배열이 닫히지 않았습니다");
                    if (_text[_pos] == ',') { _pos++; continue; }
                    if (_text[_pos] == ']') { _pos++; return list; }
                    throw Fail("',' 또는 ']'가 필요합니다");
                }
            }

            string ReadString()
            {
                var b = new StringBuilder();
                _pos++;
                while (true)
                {
                    if (AtEnd) throw Fail("문자열이 닫히지 않았습니다");
                    var c = _text[_pos++];
                    if (c == '"') return b.ToString();
                    if (c != '\\') { b.Append(c); continue; }
                    if (AtEnd) throw Fail("잘못된 이스케이프");
                    var e = _text[_pos++];
                    switch (e)
                    {
                        case '"': b.Append('"'); break;
                        case '\\': b.Append('\\'); break;
                        case '/': b.Append('/'); break;
                        case 'b': b.Append('\b'); break;
                        case 'f': b.Append('\f'); break;
                        case 'n': b.Append('\n'); break;
                        case 'r': b.Append('\r'); break;
                        case 't': b.Append('\t'); break;
                        case 'u':
                            if (_pos + 4 > _text.Length) throw Fail("잘못된 유니코드 이스케이프");
                            b.Append((char)int.Parse(_text.Substring(_pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _pos += 4;
                            break;
                        default: throw Fail($"알 수 없는 이스케이프 '\\{e}'");
                    }
                }
            }

            double ReadNumber()
            {
                int start = _pos;
                while (!AtEnd && "+-0123456789.eE".IndexOf(_text[_pos]) >= 0) _pos++;
                if (!double.TryParse(_text.Substring(start, _pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    throw Fail("숫자 형식이 잘못되었습니다");
                return number;
            }
        }
    }
}
