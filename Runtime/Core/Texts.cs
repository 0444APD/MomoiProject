using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace GBible
{
    // 언어별 텍스트 표(키 → 문장 틀)와 현재 언어. 틀을 찾아 TextFormat으로 채워 최종 글자를 만든다.
    //
    //  · 찾는 순서: 현재 언어 → Fallbacks에 등록한 언어들 → 기본 언어(pt-BR → pt) → 원문 언어(SourceLanguage).
    //  · 값이 도감 ID 글자면('dragonArmor') 그 언어의 'dragonArmor.name' 텍스트로 바꿔 넣는다.
    //  · [[id]] 용어는 'id.name' 텍스트를 용어에 넘긴 값(count 등)으로 채운 뒤 DecorateTerm으로 꾸민다.
    //  · 키가 어디에도 없으면 '[키]'를 돌려주고 Missing을 알린다.
    //
    // 세션과 달리 언어는 앱 전체에 하나라서, 어디서든 부르는 정적 입구 Texts가 기본 카탈로그 하나를 쓴다.
    // 테스트·도구는 TextCatalog를 따로 만들어 쓸 수 있다.
    public sealed class TextCatalog
    {
        const int MaxDepth = 8;

        readonly Dictionary<string, Dictionary<string, string>> _tables = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<(string lang, string key), TextFormat.Message> _cache = new Dictionary<(string, string), TextFormat.Message>();
        readonly Dictionary<string, CultureInfo> _cultures = new Dictionary<string, CultureInfo>(StringComparer.OrdinalIgnoreCase);
        string _language = "ko";
        string[] _chain;
        int _depth;

        // 원문(작가가 쓰는) 언어. 마지막 대체 언어가 된다.
        public string SourceLanguage = "ko";

        // 언어별 대체 순서. 예: Fallbacks["zh-Hant"] = new[] { "zh-Hans" }
        public readonly Dictionary<string, string[]> Fallbacks = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        // 아직 없는 언어 표를 처음 찾을 때 불러오는 함수(없으면 null을 돌려준다). Unity 쪽에서 Resources 등으로 채운다.
        public Func<string, IReadOnlyDictionary<string, string>> Loader;

        // 용어 꾸밈: (용어 ID, 이름 글자) → 서식 씌운 글자. null이면 이름 그대로.
        public Func<string, string, string> DecorateTerm;

        // 키를 못 찾았을 때(언어, 키).
        public event Action<string, string> Missing;

        // 현재 언어가 바뀌었을 때.
        public event Action LanguageChanged;

        // 가짜 번역: 틀 글자를 악센트 글자로 바꾸고 [ ~~~ ]로 감싼다(번역 안 된 글자·잘림 확인용).
        public bool Pseudo;

        // 현재 언어(BCP-47: ko, en, ja, zh-Hans, pt-BR …).
        public string Language
        {
            get => _language;
            set
            {
                var next = string.IsNullOrWhiteSpace(value) ? SourceLanguage : value.Trim();
                if (string.Equals(next, _language, StringComparison.OrdinalIgnoreCase)) return;
                _language = next;
                _chain = null;
                LanguageChanged?.Invoke();
            }
        }

        public IEnumerable<string> Languages => _tables.Keys;

        // ── 표 ───────────────────────────────────────────────

        // 한 언어의 표를 통째로 바꾼다.
        public void SetTable(string language, IReadOnlyDictionary<string, string> entries)
        {
            var table = new Dictionary<string, string>(StringComparer.Ordinal);
            if (entries != null)
                foreach (var pair in entries)
                    if (!string.IsNullOrEmpty(pair.Key)) table[pair.Key] = pair.Value ?? "";
            _tables[language] = table;
            Invalidate(language);
        }

        // 한 언어의 표에 항목을 더한다(같은 키는 덮어쓴다).
        public void Merge(string language, IReadOnlyDictionary<string, string> entries)
        {
            if (!_tables.TryGetValue(language, out var table)) _tables[language] = table = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in entries)
                if (!string.IsNullOrEmpty(pair.Key)) table[pair.Key] = pair.Value ?? "";
            Invalidate(language);
        }

        public void Set(string language, string key, string template) => Merge(language, new Dictionary<string, string> { [key] = template });

        public void Clear()
        {
            _tables.Clear();
            _cache.Clear();
            _chain = null;
        }

        void Invalidate(string language)
        {
            foreach (var key in _cache.Keys.Where(k => string.Equals(k.lang, language, StringComparison.OrdinalIgnoreCase)).ToList()) _cache.Remove(key);
            _chain = null;
        }

        Dictionary<string, string> Table(string language)
        {
            if (_tables.TryGetValue(language, out var table)) return table;
            var loaded = Loader?.Invoke(language);
            if (loaded == null) return null;
            SetTable(language, loaded);
            return _tables[language];
        }

        // 찾는 언어 순서.
        public IReadOnlyList<string> Chain(string language)
        {
            var list = new List<string>();

            void Add(string lang)
            {
                if (string.IsNullOrEmpty(lang) || list.Contains(lang, StringComparer.OrdinalIgnoreCase)) return;
                list.Add(lang);
                if (Fallbacks.TryGetValue(lang, out var more))
                    foreach (var next in more) Add(next);
                var baseLang = PluralRules.BaseLanguage(lang);
                if (!string.Equals(baseLang, lang, StringComparison.OrdinalIgnoreCase)) Add(baseLang);
            }

            Add(language);
            Add(SourceLanguage);
            return list;
        }

        IReadOnlyList<string> CurrentChain => _chain ??= Chain(_language).ToArray();

        // 틀 글자 그대로(해석 전). 찾은 언어도 돌려준다.
        public bool TryGetTemplate(string key, out string template, out string foundIn)
        {
            if (!string.IsNullOrEmpty(key))
                foreach (var lang in CurrentChain)
                {
                    var table = Table(lang);
                    if (table != null && table.TryGetValue(key, out template))
                    {
                        foundIn = lang;
                        return true;
                    }
                }
            template = null;
            foundIn = null;
            return false;
        }

        public bool Has(string key) => TryGetTemplate(key, out _, out _);

        TextFormat.Message Message(string key, out string foundIn)
        {
            if (!TryGetTemplate(key, out var template, out foundIn)) return null;
            if (_cache.TryGetValue((foundIn, key), out var message)) return message;
            return _cache[(foundIn, key)] = TextFormat.Parse(template);
        }

        // ── 글자 만들기 ───────────────────────────────────────

        public string Get(string key) => Get(key, (IReadOnlyDictionary<string, object>)null);

        public string Get(string key, IReadOnlyDictionary<string, object> args)
        {
            var message = Message(key, out var foundIn);
            if (message == null)
            {
                Missing?.Invoke(_language, key);
                return "[" + key + "]";
            }
            return Run(message, foundIn, args);
        }

        public string Get(string key, params (string name, object value)[] args) => Get(key, TextArgs.From(args));

        // 익명 객체로 값 넘기기: Get("reward.gain", new { item = "dragonArmor", count = 3 })
        public string Get(string key, object args) => Get(key, TextArgs.From(args));

        // 표에 없는 틀을 바로 채운다(도구·미리보기용).
        public string FormatTemplate(string template, IReadOnlyDictionary<string, object> args = null) =>
            Run(TextFormat.Parse(template), _language, args);

        // 도감 ID의 현재 언어 이름('id.name'). 없으면 null.
        public string NameOf(string id, IReadOnlyDictionary<string, object> args = null)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var message = Message(id + ".name", out var foundIn);
            return message == null ? null : Run(message, foundIn, args, pseudo: false);
        }

        string Run(TextFormat.Message message, string foundIn, IReadOnlyDictionary<string, object> args, bool pseudo = true)
        {
            if (_depth >= MaxDepth) return message.Template;
            _depth++;
            try
            {
                // 복수 규칙·숫자 표기는 실제로 틀을 찾은 언어를 따른다(대체 언어로 보여 줄 때 문장과 맞도록).
                var context = new TextFormat.Context
                {
                    Language = foundIn,
                    Culture = CultureOf(foundIn),
                    Args = args,
                    ResolveName = value => IdRules.IsValid(value, out _) ? NameOf(value, args) : null,
                    ResolveKey = key => Get(key),
                    TermText = (id, termArgs) => NameOf(id, termArgs) ?? id,
                    DecorateTerm = DecorateTerm,
                    Pseudo = Pseudo && pseudo,
                };
                var text = TextFormat.Format(message, context);
                return Pseudo && pseudo && _depth == 1 ? TextFormat.PseudoFrame(text) : text;
            }
            finally
            {
                _depth--;
            }
        }

        CultureInfo CultureOf(string language)
        {
            if (string.IsNullOrEmpty(language)) return CultureInfo.InvariantCulture;
            if (_cultures.TryGetValue(language, out var culture)) return culture;
            try
            {
                culture = CultureInfo.GetCultureInfo(language);
            }
            catch (Exception)
            {
                culture = CultureInfo.InvariantCulture;
            }
            return _cultures[language] = culture;
        }
    }

    // 값 인자 만들기 도우미.
    public static class TextArgs
    {
        public static IReadOnlyDictionary<string, object> From(params (string name, object value)[] args)
        {
            var map = new Dictionary<string, object>(StringComparer.Ordinal);
            if (args != null)
                foreach (var (name, value) in args)
                    if (!string.IsNullOrEmpty(name)) map[name] = value;
            return map;
        }

        public static IReadOnlyDictionary<string, object> From(object args)
        {
            switch (args)
            {
                case null: return null;
                case IReadOnlyDictionary<string, object> ready: return ready;
                case IDictionary<string, object> dict: return new Dictionary<string, object>(dict, StringComparer.Ordinal);
            }
            var map = new Dictionary<string, object>(StringComparer.Ordinal);
            var type = args.GetType();
            // 생성 코드가 있는 형식([Serializable]·[GameEvent] 객체)은 생성 코드로 읽는다.
            if (FieldAccess.IsGenerated(type))
            {
                foreach (var name in FieldAccess.NamesOf(type)) map[name] = FieldAccess.Find(type, name)?.Get(args);
                return map;
            }
            // 익명 객체(new { item = …, count = 3 })는 리플렉션으로 읽는다(형식마다 멤버 목록은 한 번만 찾는다).
            // 리플렉션 없이 쓰려면 ("item", …), ("count", 3) 형태를 쓴다.
            foreach (var property in AnonymousMembers(type)) map[property.Name] = property.GetValue(args);
            return map;
        }

        static readonly Dictionary<Type, PropertyInfo[]> Anonymous = new Dictionary<Type, PropertyInfo[]>();

        static PropertyInfo[] AnonymousMembers(Type type)
        {
            lock (Anonymous)
            {
                if (Anonymous.TryGetValue(type, out var members)) return members;
                members = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0).ToArray();
                Anonymous[type] = members;
                return members;
            }
        }
    }

    // 어디서든 부르는 입구. 예:
    //   Texts.Get("ui.start")
    //   Texts.Get("reward.gain", ("item", "dragonArmor"), ("count", 3))
    //   Texts.Get("reward.gain", new { item = "dragonArmor", count = 3 })
    //   Texts.Language = "en";
    public static class Texts
    {
        public static TextCatalog Catalog { get; set; } = new TextCatalog();

        public static string Language
        {
            get => Catalog.Language;
            set => Catalog.Language = value;
        }

        public static string Get(string key) => Catalog.Get(key);
        public static string Get(string key, IReadOnlyDictionary<string, object> args) => Catalog.Get(key, args);
        public static string Get(string key, params (string name, object value)[] args) => Catalog.Get(key, args);
        public static string Get(string key, object args) => Catalog.Get(key, args);
        public static string NameOf(string id) => Catalog.NameOf(id) ?? id;
        public static bool Has(string key) => Catalog.Has(key);
    }
}
