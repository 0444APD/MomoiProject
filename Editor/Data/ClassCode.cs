using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace GBible.EditorTools
{
    public enum ClassKind
    {
        // 평범한 데이터 클래스([Serializable]): 플레이어 데이터의 값 형식·인스턴스·다른 클래스의 필드로 쓴다.
        Data,
        // DB 종류(GameData 상속): 자기 탭이 생기고 항목을 만든다.
        Codex,
        // 이벤트([GameEvent]).
        Event,
    }

    // C# 파일 글자 다루기(파일·Unity를 모르는 순수 함수라 그대로 테스트한다).
    // 클래스·키워드는 코드에서 만들고 고친다(AI). DB는 코드를 읽기만 한다(클래스 찾기 등).
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
    }
}
