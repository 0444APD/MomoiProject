using System;
using System.Collections.Generic;

namespace GBible.Dialogue
{
    // 리치 텍스트 태그 판별: TextMeshPro·UI Toolkit 글자가 태그로 먹는 것만 태그로 본다.
    // 알려진 이름이 아닌 <…>(예: "<주인공>")는 화면에 그대로 보이는 글자라서 타이핑에서도 한 글자씩 나온다. <noparse> 안은 모두 글자다.
    public static class RichText
    {
        static readonly HashSet<string> Names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "a", "align", "allcaps", "alpha", "b", "br", "color", "cspace", "font", "font-weight", "gradient", "i", "indent",
            "line-height", "line-indent", "link", "lowercase", "margin", "margin-left", "margin-right", "mark", "material", "mspace",
            "nobr", "noparse", "page", "pos", "rotate", "s", "size", "smallcaps", "space", "sprite", "strikethrough", "style",
            "sub", "sup", "u", "uppercase", "voffset", "width",
        };

        const string NoparseOpen = "<noparse>";
        const string NoparseClose = "</noparse>";

        // text[index]에서 시작하는 태그의 길이(태그가 아니면 0). noparse: <noparse> 안이면 </noparse>만 태그다.
        public static int TagLength(string text, int index, bool noparse = false)
        {
            if (string.IsNullOrEmpty(text) || index < 0 || index >= text.Length || text[index] != '<') return 0;
            if (noparse) return string.Compare(text, index, NoparseClose, 0, NoparseClose.Length, StringComparison.OrdinalIgnoreCase) == 0 ? NoparseClose.Length : 0;
            var i = index + 1;
            var closing = i < text.Length && text[i] == '/';
            if (closing) i++;
            var start = i;
            while (i < text.Length && text[i] != '>' && text[i] != '=' && text[i] != ' ' && text[i] != '<') i++;
            if (i >= text.Length || text[i] == '<' || !IsName(text.Substring(start, i - start))) return 0;
            // 값·속성(따옴표 안의 '>'는 건너뛴다).
            var quote = '\0';
            for (; i < text.Length; i++)
            {
                var c = text[i];
                if (quote != '\0')
                {
                    if (c == quote) quote = '\0';
                    continue;
                }
                if (c == '"' || c == '\'') quote = c;
                else if (c == '>') return i - index + 1;
                else if (c == '<') return 0;
            }
            return 0;
        }

        // 이 태그를 지나면 <noparse> 안인지(여는 태그면 참, 닫는 태그면 거짓, 다른 태그면 그대로).
        public static bool Noparse(string text, int index, int length, bool noparse)
        {
            if (length == NoparseOpen.Length && string.Compare(text, index, NoparseOpen, 0, length, StringComparison.OrdinalIgnoreCase) == 0) return true;
            if (length == NoparseClose.Length && string.Compare(text, index, NoparseClose, 0, length, StringComparison.OrdinalIgnoreCase) == 0) return false;
            return noparse;
        }

        // 태그를 뺀 글자 수(TextMeshPro의 maxVisibleCharacters와 같은 기준).
        public static int VisibleLength(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            var count = 0;
            var noparse = false;
            for (var i = 0; i < text.Length; i++)
            {
                var tag = text[i] == '<' ? TagLength(text, i, noparse) : 0;
                if (tag > 0)
                {
                    noparse = Noparse(text, i, tag, noparse);
                    i += tag - 1;
                    continue;
                }
                count++;
            }
            return count;
        }

        static bool IsName(string name)
        {
            if (name.Length == 0) return false;
            if (name[0] != '#') return Names.Contains(name);
            // 색 줄임: <#RGB> · <#RGBA> · <#RRGGBB> · <#RRGGBBAA>
            if (name.Length != 4 && name.Length != 5 && name.Length != 7 && name.Length != 9) return false;
            for (var i = 1; i < name.Length; i++)
                if (!Uri.IsHexDigit(name[i])) return false;
            return true;
        }
    }
}
