using System;
using System.Globalization;

namespace GameSystems
{
    // 언어별 복수 구분(유니코드 CLDR 규칙의 주요 언어). 결과: zero · one · two · few · many · other
    // 규칙이 없는 언어는 영어처럼(1 = one, 나머지 = other) 다룬다.
    public static class PluralRules
    {
        public static string Category(string language, double n)
        {
            var lang = BaseLanguage(language);
            var abs = Math.Abs(n);
            var text = abs.ToString("0.#########", CultureInfo.InvariantCulture);
            var dot = text.IndexOf('.');
            var v = dot < 0 ? 0 : text.Length - dot - 1;          // 소수 자릿수
            var i = (long)Math.Floor(abs);                          // 정수 부분
            var i10 = i % 10;
            var i100 = i % 100;

            switch (lang)
            {
                // 복수 구분이 없는 언어
                case "ko":
                case "ja":
                case "zh":
                case "th":
                case "vi":
                case "id":
                case "ms":
                case "lo":
                case "my":
                case "km":
                    return "other";

                case "fr":
                case "hy":
                case "kab":
                    return i == 0 || i == 1 ? "one" : "other";

                case "pt":
                    return i == 0 || i == 1 ? "one" : "other";

                case "ru":
                case "uk":
                case "be":
                    if (v != 0) return "other";
                    if (i10 == 1 && i100 != 11) return "one";
                    if (i10 >= 2 && i10 <= 4 && (i100 < 12 || i100 > 14)) return "few";
                    return "many";

                case "pl":
                    if (v != 0) return "other";
                    if (i == 1) return "one";
                    if (i10 >= 2 && i10 <= 4 && (i100 < 12 || i100 > 14)) return "few";
                    return "many";

                case "cs":
                case "sk":
                    if (v != 0) return "many";
                    if (i == 1) return "one";
                    if (i >= 2 && i <= 4) return "few";
                    return "other";

                case "he":
                    if (v == 0 && i == 1) return "one";
                    if (v == 0 && i == 2) return "two";
                    return "other";

                case "ar":
                    if (v != 0) return "other";
                    if (i == 0) return "zero";
                    if (i == 1) return "one";
                    if (i == 2) return "two";
                    if (i100 >= 3 && i100 <= 10) return "few";
                    if (i100 >= 11 && i100 <= 99) return "many";
                    return "other";

                default:
                    // en, de, es, it, nl, sv, da, nb, fi, el, tr, hu … : 정수 1만 one
                    return i == 1 && v == 0 ? "one" : "other";
            }
        }

        // "zh-Hant-TW" → "zh", "pt-BR" → "pt"
        public static string BaseLanguage(string language)
        {
            if (string.IsNullOrEmpty(language)) return "";
            var dash = language.IndexOfAny(new[] { '-', '_' });
            return (dash < 0 ? language : language.Substring(0, dash)).ToLowerInvariant();
        }
    }
}
