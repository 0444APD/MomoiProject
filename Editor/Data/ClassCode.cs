using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GBible.EditorTools
{
    public enum ClassKind
    {
        // 평범한 데이터 클래스([Serializable]): 플레이어 데이터의 값 형식·템플릿·다른 클래스의 필드로 쓴다.
        Data,
        // 도감 종류(GameData 상속): 자기 탭이 생기고 항목을 만든다.
        Codex,
        // 이벤트([GameEvent]).
        Event,
    }

    // 도감 › 클래스가 쓰는 C# 파일 글자 다루기(파일·Unity를 모르는 순수 함수라 그대로 테스트한다).
    // 원본은 항상 C# 파일이다. 도감은 컴파일된 클래스를 읽어 보여 주고, 고칠 때는 파일의 '필드 구역' 안 선언 줄만 더하고 빼고 고친다.
    // 구역 밖의 코드(메서드·직접 쓴 필드·주석)는 건드리지 않는다.
    public static class ClassCode
    {
        public const string GeneratedMark = "// <gbible-class>";
        public const string Begin = "// <fields>";
        public const string End = "// </fields>";

        static readonly Regex Identifier = new Regex("^[A-Za-z_][A-Za-z0-9_]*$");

        static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue", "decimal", "default",
            "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto",
            "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out",
            "override", "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc",
            "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using",
            "virtual", "void", "volatile", "while",
        };

        // 클래스 이름: 영문 대문자로 시작(PascalCase), 영문·숫자만.
        public static bool IsValidClassName(string name, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(name)) error = "이름을 입력하세요.";
            else if (!Regex.IsMatch(name, "^[A-Z][A-Za-z0-9]*$")) error = "영문 대문자로 시작하고 영문·숫자만 씁니다(예: Item, PlayerInventory).";
            else if (Keywords.Contains(name)) error = $"'{name}'은(는) C# 예약어입니다.";
            return error == null;
        }

        // 필드 이름: 영문 소문자로 시작(camelCase), 영문·숫자만.
        public static bool IsValidFieldName(string name, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(name)) error = "이름을 입력하세요.";
            else if (!Regex.IsMatch(name, "^[a-z][A-Za-z0-9]*$")) error = "영문 소문자로 시작하고 영문·숫자만 씁니다(예: level, maxCount).";
            else if (Keywords.Contains(name)) error = $"'{name}'은(는) C# 예약어입니다.";
            return error == null;
        }

        // ── 새 클래스 파일 ─────────────────────────────────────

        public static string NewClass(string ns, string name, ClassKind kind, string label)
        {
            var b = new StringBuilder();
            b.AppendLine(GeneratedMark + " 도감 › 클래스에서 만든 파일입니다.");
            b.AppendLine("// 필드 구역(<fields> … </fields>)의 선언은 도감이 더하고 뺍니다. 코드에서 직접 고쳐도 도감에 그대로 반영됩니다.");
            b.AppendLine("// 구역 밖에 쓴 코드는 도감이 건드리지 않습니다.");
            b.AppendLine("using System;");
            b.AppendLine("using System.Collections.Generic;");
            b.AppendLine("using GBible;");
            b.AppendLine("using GBible.Data;");
            b.AppendLine("using UnityEngine;");
            b.AppendLine("using UnityEngine.Serialization;");
            b.AppendLine();
            var indent = "";
            if (!string.IsNullOrEmpty(ns))
            {
                b.AppendLine($"namespace {ns}");
                b.AppendLine("{");
                indent = "    ";
            }
            var text = Escape(string.IsNullOrWhiteSpace(label) ? name : label.Trim());
            switch (kind)
            {
                case ClassKind.Codex:
                    b.AppendLine($"{indent}[Database(\"{text}\", Group = \"데이터\")]");
                    b.AppendLine($"{indent}public class {name} : GameData");
                    break;
                case ClassKind.Event:
                    b.AppendLine($"{indent}[GameEvent(\"{text}\")]");
                    b.AppendLine($"{indent}public sealed class {name}");
                    break;
                default:
                    // 데이터 클래스: [Serializable]이면 Mirror 비교 코드가 생긴다(Undo·저장·동기화).
                    b.AppendLine($"{indent}[Serializable, PlayerData]");
                    b.AppendLine($"{indent}public class {name}");
                    break;
            }
            b.AppendLine($"{indent}{{");
            b.AppendLine($"{indent}    {Begin}");
            b.AppendLine($"{indent}    {End}");
            b.AppendLine($"{indent}}}");
            if (!string.IsNullOrEmpty(ns)) b.AppendLine("}");
            return b.ToString();
        }

        public static bool IsGenerated(string text) => text != null && text.Contains(GeneratedMark);

        static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"");

        // ── 클래스 본문 찾기 ───────────────────────────────────

        // 'class 이름'의 여는 중괄호와 짝이 되는 닫는 중괄호 위치. 문자열·주석 안의 중괄호는 세지 않는다.
        public static bool TryFindBody(string text, string className, out int open, out int close)
        {
            open = close = -1;
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(className)) return false;
            foreach (Match match in Regex.Matches(text, @"\b(class|struct)\s+" + Regex.Escape(className) + @"\b"))
            {
                if (InCommentOrString(text, match.Index)) continue;
                var i = match.Index + match.Length;
                var depth = 0;
                while (i < text.Length)
                {
                    var skipped = SkipTrivia(text, i);
                    if (skipped != i)
                    {
                        i = skipped;
                        continue;
                    }
                    var c = text[i];
                    if (c == ';' && depth == 0) break; // 선언만 있는 줄(있을 수 없지만 안전하게)
                    if (c == '{')
                    {
                        if (depth == 0) open = i;
                        depth++;
                    }
                    else if (c == '}')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            close = i;
                            return true;
                        }
                    }
                    i++;
                }
                open = close = -1;
            }
            return false;
        }

        // i가 주석·문자열의 시작이면 그 끝 다음 위치를, 아니면 i를 돌려준다.
        static int SkipTrivia(string text, int i)
        {
            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';
            if (c == '/' && next == '/')
            {
                var end = text.IndexOf('\n', i);
                return end < 0 ? text.Length : end + 1;
            }
            if (c == '/' && next == '*')
            {
                var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                return end < 0 ? text.Length : end + 2;
            }
            if (c == '@' && next == '"')
            {
                var j = i + 2;
                while (j < text.Length)
                {
                    if (text[j] == '"')
                    {
                        if (j + 1 < text.Length && text[j + 1] == '"') j += 2;
                        else return j + 1;
                    }
                    else j++;
                }
                return text.Length;
            }
            if (c == '"' || c == '\'')
            {
                var j = i + 1;
                while (j < text.Length && text[j] != c && text[j] != '\n')
                    j += text[j] == '\\' ? 2 : 1;
                return Math.Min(text.Length, j + 1);
            }
            return i;
        }

        static bool InCommentOrString(string text, int index)
        {
            var i = 0;
            while (i < index)
            {
                var skipped = SkipTrivia(text, i);
                if (skipped == i)
                {
                    i++;
                    continue;
                }
                if (skipped > index) return true;
                i = skipped;
            }
            return false;
        }

        // ── 필드 구역 ──────────────────────────────────────────

        // 클래스 본문 안 필드 구역의 안쪽 범위(Begin 줄 다음 ~ End 줄 시작). 없으면 false.
        static bool TryFindRegion(string text, string className, out int start, out int end)
        {
            start = end = -1;
            if (!TryFindBody(text, className, out var open, out var close)) return false;
            var begin = text.IndexOf(Begin, open, close - open, StringComparison.Ordinal);
            if (begin < 0) return false;
            var finish = text.IndexOf(End, begin, close - begin, StringComparison.Ordinal);
            if (finish < 0) return false;
            var lineEnd = text.IndexOf('\n', begin);
            if (lineEnd < 0 || lineEnd > finish) return false;
            start = lineEnd + 1;
            end = LineStart(text, finish);
            return end >= start;
        }

        static int LineStart(string text, int index)
        {
            var i = text.LastIndexOf('\n', Math.Max(0, index - 1));
            return i < 0 ? 0 : i + 1;
        }

        static string NewLine(string text) => text.Contains("\r\n") ? "\r\n" : "\n";

        static string IndentOf(string text, int lineStart)
        {
            var i = lineStart;
            while (i < text.Length && (text[i] == ' ' || text[i] == '\t')) i++;
            return text.Substring(lineStart, i - lineStart);
        }

        // 선언 줄에서 필드 이름을 읽는다: [어트리뷰트] public 형식 이름 (= 초기값)?;
        static readonly Regex FieldLine = new Regex(@"^\s*(?:\[[^\n]*\]\s*)*public\s+[^;=(]+?\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:=[^;]*)?;", RegexOptions.Compiled);

        static string NameOfLine(string line)
        {
            var match = FieldLine.Match(line);
            return match.Success ? match.Groups["name"].Value : null;
        }

        // 필드 구역 안에 선언된(도감이 빼거나 이름을 바꿀 수 있는) 필드 이름.
        public static List<string> RegionFields(string text, string className)
        {
            var result = new List<string>();
            if (!TryFindRegion(text, className, out var start, out var end)) return result;
            foreach (var line in text.Substring(start, end - start).Split('\n'))
            {
                var name = NameOfLine(line.TrimEnd('\r'));
                if (name != null) result.Add(name);
            }
            return result;
        }

        public static bool HasRegion(string text, string className) => TryFindRegion(text, className, out _, out _);

        // 필드 선언 한 줄을 구역 끝에 더한다. 구역이 없는 클래스(직접 쓴 코드)에는 본문 끝에 구역을 만들어 넣는다.
        public static bool TryAddField(ref string text, string className, string declaration, out string error)
        {
            error = null;
            var name = NameOfLine(declaration);
            if (name == null)
            {
                error = "필드 선언을 읽을 수 없습니다: " + declaration;
                return false;
            }
            if (!TryFindBody(text, className, out _, out var close))
            {
                error = $"파일에서 클래스 '{className}'을(를) 찾지 못했습니다.";
                return false;
            }
            var newline = NewLine(text);
            if (!TryFindRegion(text, className, out _, out var end))
            {
                // 닫는 중괄호 줄 바로 앞에 구역을 만든다.
                var closeLine = LineStart(text, close);
                var indent = IndentOf(text, closeLine) + "    ";
                var onOwnLine = text.Substring(closeLine, close - closeLine).Trim().Length == 0;
                var region = indent + Begin + newline + indent + declaration + newline + indent + End + newline;
                text = onOwnLine
                    ? text.Insert(closeLine, region)
                    : text.Insert(close, newline + region + IndentOf(text, closeLine));
                return true;
            }
            if (RegionFields(text, className).Contains(name))
            {
                error = $"'{name}' 필드가 이미 있습니다.";
                return false;
            }
            var fieldIndent = IndentOf(text, end);
            text = text.Insert(end, fieldIndent + declaration + newline);
            return true;
        }

        // 구역 안의 필드 선언 줄을 지운다.
        public static bool TryRemoveField(ref string text, string className, string field, out string error)
        {
            error = null;
            if (!TryFindLine(text, className, field, out var lineStart, out var lineEnd))
            {
                error = $"'{className}'의 필드 구역에서 '{field}' 선언을 찾지 못했습니다(구역 밖에 직접 쓴 필드는 코드에서 지우세요).";
                return false;
            }
            text = text.Remove(lineStart, lineEnd - lineStart);
            return true;
        }

        // 구역 안의 필드 이름을 바꾼다. 저장된 값이 이어지도록 [FormerlySerializedAs("예전 이름")]를 붙인다.
        public static bool TryRenameField(ref string text, string className, string field, string newName, out string error)
        {
            error = null;
            if (!Identifier.IsMatch(newName ?? ""))
            {
                error = "새 이름이 올바르지 않습니다.";
                return false;
            }
            if (!TryFindLine(text, className, field, out var lineStart, out var lineEnd))
            {
                error = $"'{className}'의 필드 구역에서 '{field}' 선언을 찾지 못했습니다(구역 밖에 직접 쓴 필드는 코드에서 고치세요).";
                return false;
            }
            if (RegionFields(text, className).Contains(newName))
            {
                error = $"'{newName}' 필드가 이미 있습니다.";
                return false;
            }
            var line = text.Substring(lineStart, lineEnd - lineStart);
            var renamed = Regex.Replace(line, @"\b" + Regex.Escape(field) + @"(\s*(?:=[^;]*)?;)", newName + "$1", RegexOptions.None, TimeSpan.FromSeconds(1));
            var indent = IndentOf(line, 0);
            renamed = indent + $"[FormerlySerializedAs(\"{field}\")] " + renamed.Substring(indent.Length);
            text = text.Remove(lineStart, lineEnd - lineStart).Insert(lineStart, renamed);
            EnsureUsing(ref text, "UnityEngine.Serialization");
            return true;
        }

        static bool TryFindLine(string text, string className, string field, out int lineStart, out int lineEnd)
        {
            lineStart = lineEnd = -1;
            if (!TryFindRegion(text, className, out var start, out var end)) return false;
            var position = start;
            while (position < end)
            {
                var next = text.IndexOf('\n', position);
                var stop = next < 0 || next >= end ? end : next + 1;
                var line = text.Substring(position, stop - position);
                if (NameOfLine(line.TrimEnd('\r', '\n')) == field)
                {
                    lineStart = position;
                    lineEnd = stop;
                    return true;
                }
                position = stop;
            }
            return false;
        }

        // using 줄이 없으면 더한다(마지막 using 다음, 없으면 파일 머리 주석 다음).
        public static void EnsureUsing(ref string text, string ns)
        {
            if (Regex.IsMatch(text, @"^\s*using\s+" + Regex.Escape(ns) + @"\s*;", RegexOptions.Multiline)) return;
            var newline = NewLine(text);
            var usings = Regex.Matches(text, @"^using\s+[^;\n]+;[ \t]*\r?\n", RegexOptions.Multiline);
            var line = $"using {ns};{newline}";
            if (usings.Count > 0)
            {
                var last = usings[usings.Count - 1];
                text = text.Insert(last.Index + last.Length, line);
                return;
            }
            var i = 0;
            while (i < text.Length && text.Substring(i).TrimStart(' ', '\t').StartsWith("//", StringComparison.Ordinal))
            {
                var end = text.IndexOf('\n', i);
                if (end < 0) break;
                i = end + 1;
            }
            text = text.Insert(i, line);
        }

        // ── 선언 만들기 ────────────────────────────────────────

        // 형식 글자(예: int, List<string>, Game.Data.Item)와 이름으로 선언 한 줄을 만든다. 글자·컬렉션은 빈 값으로 시작한다.
        // 데이터 클래스에 [PlayerData] 표시를 붙인다(예전 기능. 지금은 [Serializable]만 있으면 같다).
        public static bool TryUpgradeToPlayerData(ref string text, string className, out string error)
        {
            error = null;
            if (!TryFindBody(text, className, out _, out _))
            {
                error = $"파일에서 클래스 '{className}'을(를) 찾지 못했습니다.";
                return false;
            }
            var source = text;
            var declaration = Regex.Matches(source, @"\bclass\s+" + Regex.Escape(className) + @"\b").Cast<Match>().FirstOrDefault(m => !InCommentOrString(source, m.Index));
            if (declaration == null)
            {
                error = $"클래스 '{className}' 선언을 찾지 못했습니다.";
                return false;
            }
            var newline = NewLine(text);
            var lineStart = LineStart(text, declaration.Index);
            // 바로 위 어트리뷰트 줄들에 이미 PlayerData가 있으면 그대로 둔다.
            var attributes = "";
            for (var start = lineStart; start > 0;)
            {
                var previous = LineStart(text, start - 1);
                var line = text.Substring(previous, start - previous).Trim();
                if (!line.StartsWith("[", StringComparison.Ordinal)) break;
                attributes += line;
                start = previous;
            }
            if (!Regex.IsMatch(attributes, @"\bPlayerData\b"))
                text = text.Insert(lineStart, IndentOf(text, lineStart) + "[PlayerData]" + newline);

            EnsureUsing(ref text, "GBible");
            return true;
        }

        public static string Declaration(string typeCode, string name, string attributes = null)
        {
            var initializer = "";
            if (typeCode == "string") initializer = " = \"\"";
            else if (typeCode.StartsWith("List<", StringComparison.Ordinal) || typeCode.StartsWith("Dictionary<", StringComparison.Ordinal))
                initializer = $" = new {typeCode}()";
            else if (typeCode.EndsWith("[]", StringComparison.Ordinal)) initializer = $" = new {typeCode.Substring(0, typeCode.Length - 2)}[0]";
            var prefix = string.IsNullOrEmpty(attributes) ? "" : attributes.Trim() + " ";
            return $"{prefix}public {typeCode} {name}{initializer};";
        }
    }
}
