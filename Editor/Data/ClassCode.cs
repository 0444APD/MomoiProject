using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace GBible.EditorTools
{
    public enum ClassKind
    {
        // 평범한 데이터 클래스([Serializable]): 플레이어 데이터의 값 형식·인스턴스·다른 클래스의 필드로 쓴다.
        Data,
        // 도감 종류(GameData 상속): 자기 탭이 생기고 항목을 만든다.
        Codex,
        // 이벤트([GameEvent]).
        Event,
    }

    // C# 파일 글자 다루기(파일·Unity를 모르는 순수 함수라 그대로 테스트한다).
    // 클래스는 코드에서 만들고 고친다. 도감은 에디터에서 정한 것(필드의 어트리뷰트, 키워드)만 코드에 써 넣는다.
    public static class ClassCode
    {
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

        internal static bool InCommentOrString(string text, int index)
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

        internal static int LineStart(string text, int index)
        {
            var i = text.LastIndexOf('\n', Math.Max(0, index - 1));
            return i < 0 ? 0 : i + 1;
        }

        internal static string NewLine(string text) => text.Contains("\r\n") ? "\r\n" : "\n";

        internal static string IndentOf(string text, int lineStart)
        {
            var i = lineStart;
            while (i < text.Length && (text[i] == ' ' || text[i] == '\t')) i++;
            return text.Substring(lineStart, i - lineStart);
        }

        // 필드 선언에 어트리뷰트를 붙이거나 뗀다. attribute = 대괄호 안 글자(예: "Hide", "ShowIf(nameof(equip))").
        // 떼기는 이름이 같은 어트리뷰트를 선언 줄과 그 위 어트리뷰트 줄에서 모두 지운다. 필드 구역 밖의 선언도 고친다(한 줄 선언).
        public static bool TrySetFieldAttribute(ref string text, string className, string field, string attribute, bool add, out string error)
        {
            if (!TryFindBody(text, className, out var open, out var close))
            {
                error = $"파일에서 클래스 '{className}'을(를) 찾지 못했습니다.";
                return false;
            }
            var source = text;
            var line = new Regex(@"^[^\n]*\b(?:public|private|protected|internal)\b[^\n;=(]*\b" + Regex.Escape(field) + @"\b\s*(?:=[^;\n]*)?;", RegexOptions.Multiline)
                .Matches(source, open).Cast<Match>().FirstOrDefault(m => m.Index < close && !InCommentOrString(source, m.Index));
            if (line == null)
            {
                error = $"'{className}.{field}' 필드 선언을 찾지 못했습니다(한 줄 선언만 고칩니다).";
                return false;
            }
            var name = attribute.Split('(')[0].Trim();
            var pattern = new Regex(@"\[\s*" + Regex.Escape(name) + @"(?:Attribute)?\s*(?:\([^\]]*\))?\s*\]\s*");
            // 선언 줄과 바로 위 어트리뷰트 줄들.
            var start = line.Index;
            while (start > 0)
            {
                var previous = LineStart(text, start - 1);
                var above = text.Substring(previous, start - previous).Trim();
                if (!above.StartsWith("[", StringComparison.Ordinal)) break;
                start = previous;
            }
            var block = text.Substring(start, line.Index + line.Length - start);
            var has = pattern.IsMatch(block);
            if (add && !has)
            {
                var indent = IndentOf(text, line.Index);
                text = text.Insert(line.Index + indent.Length, $"[{attribute}] ");
            }
            else if (!add && has)
            {
                var cleaned = pattern.Replace(block, "");
                // 어트리뷰트만 있던 줄이 비면 지운다.
                cleaned = Regex.Replace(cleaned, @"^[ \t]*\r?\n", "", RegexOptions.Multiline);
                text = text.Substring(0, start) + cleaned + text.Substring(line.Index + line.Length);
            }
            if (add) EnsureUsing(ref text, "GBible");
            error = null;
            return true;
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

    }
}
