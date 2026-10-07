using System;
using System.Collections.Generic;
using System.Text;

namespace GBible
{
    // 한국어 조사: 앞 단어의 받침(읽는 소리 기준)에 맞춰 을/를, 이/가, (으)로 … 를 고른다.
    //  · 한글: 마지막 글자의 받침. (으)로는 ㄹ 받침이면 '로'.
    //  · 숫자: 읽는 소리(3=삼 → 을, 2=이 → 를, 10=십 → 을, 1000=천 → 을).
    //  · 영문 약어·한 글자: 알파벳 이름(L=엘 → ㄹ, M=엠, N=엔, R=알, 나머지는 받침 없음).
    //  · 영문 단어: 끝소리가 분명한 경우만(…ng, …m, …n, …l → 받침 / 모음·s·x·z·f·v → 없음). 애매하면 '을(를)'처럼 둘 다 적는다.
    //  · 판단이 틀리는 단어는 Overrides에 직접 등록한다(예: "HP" → 받침 없음).
    public static class KoreanParticles
    {
        // (받침 있을 때, 받침 없을 때, ㄹ 받침이면 '없을 때' 형태를 쓰는지)
        static readonly (string with, string without, bool rieulUsesWithout)[] Pairs =
        {
            ("을", "를", false), ("이", "가", false), ("은", "는", false), ("과", "와", false),
            ("으로", "로", true), ("으로서", "로서", true), ("으로써", "로써", true),
            ("아", "야", false), ("이여", "여", false), ("이시여", "시여", false),
            ("이에요", "예요", false), ("이었다", "였다", false), ("이라", "라", false), ("이라고", "라고", false),
            ("이라는", "라는", false), ("이라면", "라면", false), ("이나", "나", false), ("이랑", "랑", false),
            ("이며", "며", false), ("이다", "다", false), ("이고", "고", false), ("이란", "란", false),
            ("이든", "든", false), ("이야", "야", false), ("이면", "면", false), ("이지만", "지만", false),
        };

        static Dictionary<string, (string with, string without, bool rieul)> _byForm;

        // 받침 판단 예외(대소문자 무시). true = 받침 있음. 예: Overrides["HP"] = false
        public static readonly Dictionary<string, bool> Overrides = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        static Dictionary<string, (string with, string without, bool rieul)> ByForm
        {
            get
            {
                if (_byForm != null) return _byForm;
                var map = new Dictionary<string, (string with, string without, bool rieul)>(StringComparer.Ordinal);
                foreach (var (with, without, rieul) in Pairs)
                {
                    map[with] = (with, without, rieul);
                    if (!map.ContainsKey(without)) map[without] = (with, without, rieul);
                }
                return _byForm = map;
            }
        }

        // 조사로 알아보는 글자인지(을·를·이·가 … 중 하나).
        public static bool IsParticle(string text) => !string.IsNullOrEmpty(text) && ByForm.ContainsKey(text);

        // 단어 뒤에 올 알맞은 조사. 모르는 조사면 그대로 돌려준다.
        public static string Pick(string word, string particle)
        {
            if (!ByForm.TryGetValue(particle ?? "", out var pair)) return particle;
            var (known, hasFinal, rieul) = Final(word);
            if (!known) return pair.rieul ? $"({pair.with.Substring(0, 1)}){pair.without}" : $"{pair.with}({pair.without})";
            if (!hasFinal) return pair.without;
            if (rieul && pair.rieul) return pair.without;
            return pair.with;
        }

        public static string Attach(string word, string particle) => word + Pick(word, particle);

        // 받침 판단: (판단 가능, 받침 있음, ㄹ 받침)
        public static (bool known, bool hasFinal, bool rieul) Final(string word)
        {
            var text = Clean(word);
            if (text.Length == 0) return (false, false, false);
            if (Overrides.TryGetValue(text, out var forced)) return (true, forced, false);
            // 예외 등록은 마지막 단어로도 찾는다("New Game" → "Game").
            var space = text.LastIndexOf(' ');
            if (space >= 0 && space < text.Length - 1 && Overrides.TryGetValue(text.Substring(space + 1), out var forcedLast)) return (true, forcedLast, false);
            var c = text[text.Length - 1];

            if (c >= 0xAC00 && c <= 0xD7A3)
            {
                var jong = (c - 0xAC00) % 28;
                return (true, jong != 0, jong == 8);
            }
            if (c >= 0x3131 && c <= 0x314E) return (true, true, c == 0x3139);
            if (char.IsDigit(c)) return NumberFinal(text);
            if (IsLatin(c)) return LatinFinal(text);
            return (false, false, false);
        }

        // 태그(<...>)와 끝의 따옴표·괄호·문장부호를 걷어 낸 글자.
        static string Clean(string word)
        {
            if (string.IsNullOrEmpty(word)) return "";
            var b = new StringBuilder(word.Length);
            var inTag = false;
            foreach (var ch in word)
            {
                if (ch == '<') inTag = true;
                else if (ch == '>' && inTag) inTag = false;
                else if (!inTag) b.Append(ch);
            }
            var s = b.ToString();
            var end = s.Length;
            while (end > 0 && "\"'”’)]}」』》〉>.,!?~… \t\r\n".IndexOf(s[end - 1]) >= 0) end--;
            return s.Substring(0, end);
        }

        static bool IsLatin(char c) => c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z';

        // 숫자 읽기: 끝자리가 0이 아니면 그 숫자(영·일·이·삼…), 0으로 끝나면 단위(십·백·천·만·억·조).
        static (bool, bool, bool) NumberFinal(string text)
        {
            var end = text.Length;
            var start = end;
            while (start > 0 && (char.IsDigit(text[start - 1]) || text[start - 1] == ',')) start--;
            var digits = text.Substring(start, end - start).Replace(",", "");
            if (start > 0 && text[start - 1] == '.') return Digit(digits[digits.Length - 1]);
            var trimmed = digits.TrimStart('0');
            if (trimmed.Length == 0) return (true, true, false); // 영
            var zeros = 0;
            for (var i = trimmed.Length - 1; i >= 0 && trimmed[i] == '0'; i--) zeros++;
            if (zeros == 0) return Digit(trimmed[trimmed.Length - 1]);
            switch (zeros)
            {
                case 1: return (true, true, false);   // 십
                case 2: return (true, true, false);   // 백
                case 3: return (true, true, false);   // 천
            }
            if (zeros < 8) return (true, true, false);   // 만
            if (zeros < 12) return (true, true, false);  // 억
            if (zeros < 16) return (true, false, false); // 조
            return (true, true, false);                  // 경
        }

        static (bool, bool, bool) Digit(char d)
        {
            switch (d)
            {
                case '0': return (true, true, false);  // 영
                case '1': return (true, true, true);   // 일
                case '3': return (true, true, false);  // 삼
                case '6': return (true, true, false);  // 육
                case '7': return (true, true, true);   // 칠
                case '8': return (true, true, true);   // 팔
                default: return (true, false, false);  // 이·사·오·구
            }
        }

        static (bool, bool, bool) LatinFinal(string text)
        {
            var start = text.Length;
            while (start > 0 && IsLatin(text[start - 1])) start--;
            var run = text.Substring(start);
            var acronym = run.Length == 1 || run == run.ToUpperInvariant();
            var last = char.ToLowerInvariant(run[run.Length - 1]);
            if (acronym)
            {
                // 알파벳 이름: 엘·엠·엔·알만 받침.
                switch (last)
                {
                    case 'l': return (true, true, true);
                    case 'r': return (true, true, true);
                    case 'm':
                    case 'n': return (true, true, false);
                    default: return (true, false, false);
                }
            }
            var lower = run.ToLowerInvariant();
            if (lower.EndsWith("ng", StringComparison.Ordinal)) return (true, true, false);
            // 소리 나지 않는 e: game·frame·time(게임·프레임·타임) = ㅁ, rune·line(룬·라인) = ㄴ, table·castle(테이블·캐슬) = ㄹ, 그 밖(page·code·gate)은 받침 없음.
            if (lower.Length >= 3 && last == 'e')
            {
                switch (lower[lower.Length - 2])
                {
                    case 'm':
                    case 'n': return (true, true, false);
                    case 'l': return (true, true, true);
                    default: return (true, false, false);
                }
            }
            switch (last)
            {
                case 'm':
                case 'n': return (true, true, false);
                case 'l': return (true, true, true);
                case 'a':
                case 'e':
                case 'i':
                case 'o':
                case 'u':
                case 'y':
                case 'w':
                case 'r':
                case 's':
                case 'x':
                case 'z':
                case 'f':
                case 'v': return (true, false, false);
                default: return (false, false, false);
            }
        }
    }
}
