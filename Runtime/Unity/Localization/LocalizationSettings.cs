using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GBible
{
    // 지원 언어와 언어별 텍스트 표. Resources/MomoiProject/LocalizationSettings 에 둔다.
    [CreateAssetMenu(menuName = "MomoiProject/다국어/다국어 설정", fileName = "LocalizationSettings")]
    public sealed class LocalizationSettings : ScriptableObject
    {
        public const string ResourcesPath = "MomoiProject/LocalizationSettings";

        [Serializable]
        public sealed class Language
        {
            [Tooltip("BCP-47 언어 코드(ko, en, ja, zh-Hans, pt-BR …)")]
            public string code = "ko";
            [Tooltip("언어 선택 화면에 보일 이름(그 언어로: 한국어, English, 日本語 …)")]
            public string nativeName = "한국어";
            [Tooltip("이 언어에 없는 문장을 찾을 언어들(쉼표로 구분). 끝에는 항상 원문 언어를 찾는다.")]
            public string fallbacks = "";
            public TextTable table;
        }

        // 문장 속 용어([[키워드 ID]])의 모양: TextMeshPro 스타일 시트의 스타일 이름(색·굵기 등)과 스프라이트 애셋의 아이콘 이름.
        [Serializable]
        public sealed class TermStyle
        {
            public string id = "";
            public string style = "";
            public string icon = "";
        }

        [Tooltip("문장 속 용어([[키워드 ID]])의 스타일·아이콘")]
        public List<TermStyle> termStyles = new List<TermStyle>();

        [Tooltip("작가가 쓰는 원문 언어. 모든 언어의 마지막 대체 언어.")]
        public string sourceLanguage = "ko";
        public List<Language> languages = new List<Language> { new Language() };

        public Language Find(string code) =>
            languages.FirstOrDefault(l => l != null && string.Equals(l.code, code, StringComparison.OrdinalIgnoreCase));

        public IEnumerable<string> Codes => languages.Where(l => l != null && !string.IsNullOrEmpty(l.code)).Select(l => l.code);

        public static LocalizationSettings Load() => Resources.Load<LocalizationSettings>(ResourcesPath);

        // 카탈로그에 설정을 옮긴다(대체 순서, 표 불러오기).
        public void Apply(TextCatalog catalog)
        {
            catalog.Clear();
            catalog.SourceLanguage = string.IsNullOrEmpty(sourceLanguage) ? "ko" : sourceLanguage;
            catalog.Fallbacks.Clear();
            foreach (var language in languages)
            {
                if (language == null || string.IsNullOrEmpty(language.code)) continue;
                var list = (language.fallbacks ?? "").Split(',').Select(f => f.Trim()).Where(f => f.Length > 0).ToArray();
                if (list.Length > 0) catalog.Fallbacks[language.code] = list;
            }
            // 표는 처음 필요할 때 읽는다(안 쓰는 언어의 문장을 메모리에 올리지 않는다).
            catalog.Loader = code => Find(code)?.table?.ToDictionary();
        }

        // 지원 언어 중 요청과 가장 가까운 것(정확히 같은 코드 → 기본 언어가 같은 코드). 없으면 null.
        public string Closest(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            var exact = Find(code);
            if (exact != null) return exact.code;
            var baseLanguage = PluralRules.BaseLanguage(code);
            return Codes.FirstOrDefault(c => PluralRules.BaseLanguage(c) == baseLanguage);
        }
    }
}
