using System;
using System.Collections.Generic;
using System.Linq;
using GBible.Data;
using UnityEngine;

namespace GBible
{
    // 게임의 현재 언어. 시작할 때 저장된 언어 → 기기 언어 → 원문 언어 순으로 정하고, 바꾸면 저장한다.
    // 글자는 어디서든 Texts.Get(...)으로 만든다. 이 클래스는 설정·표·용어 모양을 Texts에 연결한다.
    public static class GameLanguage
    {
        const string PrefsKey = "GBible.Language";

        static LocalizationSettings _settings;
        static Dictionary<string, LocalizationSettings.TermStyle> _terms;
        static bool _ready;

        // 언어가 바뀌었거나 표를 다시 읽은 뒤(화면 글자 새로 고침 신호).
        public static event Action Changed;

        public static LocalizationSettings Settings
        {
            get
            {
                EnsureReady();
                return _settings;
            }
        }

        public static string Current
        {
            get
            {
                EnsureReady();
                return Texts.Language;
            }
        }

        // 지원 언어(코드, 그 언어로 쓴 이름).
        public static IEnumerable<(string code, string nativeName)> Available
        {
            get
            {
                EnsureReady();
                if (_settings == null) return new[] { (Texts.Catalog.SourceLanguage, Texts.Catalog.SourceLanguage) };
                return _settings.languages.Where(l => l != null && !string.IsNullOrEmpty(l.code)).Select(l => (l.code, string.IsNullOrEmpty(l.nativeName) ? l.code : l.nativeName));
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _ready = false;
            _settings = null;
            _terms = null;
            Changed = null;
            Texts.Catalog = new TextCatalog();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void AutoStart() => EnsureReady();

        // 설정을 읽어 Texts를 준비한다(여러 번 불러도 한 번만).
        public static void EnsureReady()
        {
            if (_ready) return;
            _ready = true;
            _settings = LocalizationSettings.Load();
            var catalog = Texts.Catalog;
            if (_settings != null) _settings.Apply(catalog);
            catalog.DecorateTerm = DecorateTerm;
            catalog.LanguageChanged += () => Changed?.Invoke();
            catalog.Missing += (language, key) =>
            {
                if (Debug.isDebugBuild) Debug.LogWarning($"[다국어] '{language}'에 텍스트 '{key}'가 없습니다.");
            };
            catalog.Language = StartLanguage();
        }

        // 표·설정을 다시 읽는다(에디터에서 시트를 가져온 뒤 등).
        public static void Reload()
        {
            var language = _ready ? Texts.Language : null;
            _ready = false;
            _terms = null;
            Texts.Catalog = new TextCatalog();
            EnsureReady();
            if (language != null) Texts.Language = language;
            Changed?.Invoke();
        }

        public static void Set(string code, bool remember = true)
        {
            EnsureReady();
            var chosen = _settings?.Closest(code) ?? code;
            if (remember)
            {
                PlayerPrefs.SetString(PrefsKey, chosen);
                PlayerPrefs.Save();
            }
            Texts.Language = chosen;
        }

        static string StartLanguage()
        {
            var source = Texts.Catalog.SourceLanguage;
            if (_settings == null) return source;
            var saved = PlayerPrefs.GetString(PrefsKey, "");
            return _settings.Closest(saved) ?? _settings.Closest(SystemCode(Application.systemLanguage)) ?? source;
        }

        // 기기 언어 → BCP-47
        public static string SystemCode(SystemLanguage language)
        {
            switch (language)
            {
                case SystemLanguage.Korean: return "ko";
                case SystemLanguage.English: return "en";
                case SystemLanguage.Japanese: return "ja";
                case SystemLanguage.Chinese:
                case SystemLanguage.ChineseSimplified: return "zh-Hans";
                case SystemLanguage.ChineseTraditional: return "zh-Hant";
                case SystemLanguage.French: return "fr";
                case SystemLanguage.German: return "de";
                case SystemLanguage.Spanish: return "es";
                case SystemLanguage.Portuguese: return "pt";
                case SystemLanguage.Italian: return "it";
                case SystemLanguage.Russian: return "ru";
                case SystemLanguage.Ukrainian: return "uk";
                case SystemLanguage.Belarusian: return "be";
                case SystemLanguage.Polish: return "pl";
                case SystemLanguage.Czech: return "cs";
                case SystemLanguage.Slovak: return "sk";
                case SystemLanguage.Turkish: return "tr";
                case SystemLanguage.Thai: return "th";
                case SystemLanguage.Vietnamese: return "vi";
                case SystemLanguage.Indonesian: return "id";
                case SystemLanguage.Arabic: return "ar";
                case SystemLanguage.Hebrew: return "he";
                case SystemLanguage.Dutch: return "nl";
                case SystemLanguage.Swedish: return "sv";
                case SystemLanguage.Norwegian: return "nb";
                case SystemLanguage.Danish: return "da";
                case SystemLanguage.Finnish: return "fi";
                case SystemLanguage.Greek: return "el";
                case SystemLanguage.Hungarian: return "hu";
                case SystemLanguage.Romanian: return "ro";
                case SystemLanguage.Bulgarian: return "bg";
                default: return null;
            }
        }

        // [[id]] 용어 모양: 키워드의 스타일·아이콘을 TextMeshPro 태그로 씌운다.
        // <link="term:id">는 툴팁을 띄울 때 TMP_TextUtilities.FindIntersectingLink로 찾는다.
        static string DecorateTerm(string id, string text)
        {
            var term = Term(id);
            if (term == null || (string.IsNullOrEmpty(term.style) && string.IsNullOrEmpty(term.icon))) return text;
            var body = string.IsNullOrEmpty(term.style) ? text : $"<style=\"{term.style}\">{text}</style>";
            if (!string.IsNullOrEmpty(term.icon)) body = $"<sprite name=\"{term.icon}\">{body}";
            return $"<link=\"term:{id}\">{body}</link>";
        }

        static LocalizationSettings.TermStyle Term(string id)
        {
            if (_terms == null)
            {
                _terms = new Dictionary<string, LocalizationSettings.TermStyle>(StringComparer.Ordinal);
                var settings = LocalizationSettings.Load();
                if (settings != null)
                    foreach (var term in settings.termStyles)
                        if (term != null && !string.IsNullOrEmpty(term.id)) _terms[term.id] = term;
            }
            return _terms.TryGetValue(id, out var found) ? found : null;
        }

        // 링크 ID("term:burn")에서 용어 ID를 꺼낸다. 용어 링크가 아니면 null.
        public static string TermOfLink(string linkId) =>
            linkId != null && linkId.StartsWith("term:", StringComparison.Ordinal) ? linkId.Substring(5) : null;
    }
}
