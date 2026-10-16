using System;
using System.Collections;
using System.Globalization;
using System.Linq;

namespace GBible
{
    // 화면 칸의 연결 글(UXML의 binding-path) — 코드 없이 플레이어 데이터의 값이나 번역된 글을 보인다(GameScreens가 화면을 열 때 읽는다).
    //   "run:gold"             플레이어 데이터 run의 gold(목록이면 개수)
    //   "run:hero.hp@hud.hp"   값을 텍스트 키 hud.hp의 틀({value})에 넣어서
    //   "@ui.endTurn"          텍스트 키 하나(언어가 바뀌면 다시)
    //   "run"                  값 전체
    //   "stat:kills"           키에 ':'이 든 플레이어 데이터(로그·도전과제·플레이어별 키) — 정의된 가장 긴 앞부분을 키로 본다
    // 틀이 없으면 그 칸의 원래 글자에 {value}가 있으면 그것을 틀로, 없으면 값 그대로 보인다.
    public sealed class ScreenBinding
    {
        // 첫 ':' 앞(키에 ':'이 없을 때의 키)과 그 뒤. 실제로 읽는 키는 Read가 정의된 키에 맞춰 고른다(Keys).
        public string Key { get; private set; } = "";
        public string Path { get; private set; } = "";
        public string TextKey { get; private set; } = "";
        // ' 앞 글 전체(키와 경로).
        string _data = "";
        string[] _keys = Array.Empty<string>();

        // 이 칸이 읽을 수 있는 키 후보(긴 것부터): "stat:kills" → "stat:kills", "stat". 화면은 이 키들이 바뀔 때 다시 그린다.
        public System.Collections.Generic.IReadOnlyList<string> Keys => _keys;

        public bool HasData => Key.Length > 0;

        // 비었거나 형식이 틀리면 null.
        public static ScreenBinding Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            text = text.Trim();
            var binding = new ScreenBinding();
            var at = text.IndexOf('@');
            if (at >= 0)
            {
                binding.TextKey = text.Substring(at + 1).Trim();
                text = text.Substring(0, at).Trim();
                if (binding.TextKey.Length == 0) return null;
            }
            if (text.Length > 0)
            {
                var colon = text.IndexOf(':');
                binding.Key = (colon < 0 ? text : text.Substring(0, colon)).Trim();
                binding.Path = colon < 0 ? "" : text.Substring(colon + 1).Trim();
                if (binding.Key.Length == 0) return null;
                binding._data = text;
                var keys = new System.Collections.Generic.List<string> { text };
                for (var at2 = text.LastIndexOf(':'); at2 > 0; at2 = text.LastIndexOf(':', at2 - 1))
                    keys.Add(text.Substring(0, at2).Trim());
                binding._keys = keys.Distinct().ToArray();
            }
            return binding.HasData || binding.TextKey.Length > 0 ? binding : null;
        }

        // 지금 값(없는 키·경로면 null). 목록은 그대로 돌려준다(글자로 바꿀 때 개수).
        public object Read(GameSession session)
        {
            if (!HasData || session == null) return null;
            // 정의된 가장 긴 앞부분이 키, 나머지가 경로("stat:kills"는 키 그대로, "run:gold"는 run의 gold).
            foreach (var key in _keys)
            {
                if (!session.Data.IsDefined(key)) continue;
                var root = session.Data.Get(key);
                var path = key.Length >= _data.Length ? "" : _data.Substring(key.Length).TrimStart(':').Trim();
                return path.Length == 0 ? root : FieldPaths.Resolve(root, path).FirstOrDefault();
            }
            return null;
        }

        // 보일 글. template: 그 칸의 원래 글자({value}가 있으면 틀로 쓴다).
        public string Text(GameSession session, string template)
        {
            if (!HasData) return Texts.Get(TextKey);
            var value = Shown(Read(session));
            var args = TextArgs.From(("value", value));
            if (TextKey.Length > 0) return Texts.Get(TextKey, args);
            if (!string.IsNullOrEmpty(template) && template.Contains("{value}")) return Texts.Catalog.FormatTemplate(template, args);
            return Format(value);
        }

        // 글자로 보일 값: 목록이면 개수.
        public static object Shown(object value) => value is ICollection collection && !(value is string) ? collection.Count : value;

        public static string Format(object value)
        {
            switch (value)
            {
                case null: return "";
                case double number: return number.ToString("0.##", CultureInfo.InvariantCulture);
                case float number: return number.ToString("0.##", CultureInfo.InvariantCulture);
                // 시각은 이 기기의 지역 시각·지역 형식으로(UTC를 그대로 보이면 9시간 이르게 보인다).
                case DateTime time: return (time.Kind == DateTimeKind.Utc ? time.ToLocalTime() : time).ToString("g", CultureInfo.CurrentCulture);
                // enum: 텍스트 표에 '<형식 이름>.<값>'(TimeOfDay.Morning)이 있으면 그 글(지금 언어), 없으면 코드 이름.
                case Enum choice:
                {
                    var key = choice.GetType().Name + "." + choice;
                    return Texts.Has(key) ? Texts.Get(key) : choice.ToString();
                }
                case IFormattable formattable: return formattable.ToString(null, CultureInfo.InvariantCulture);
                default: return value.ToString();
            }
        }

        public static double Number(object value)
        {
            value = Shown(value);
            switch (value)
            {
                case null: return 0;
                case bool flag: return flag ? 1 : 0;
                case BigNumber big: return big.ToDouble();
                case string text: return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
                case IConvertible _:
                    try { return Convert.ToDouble(value, CultureInfo.InvariantCulture); }
                    catch (Exception) { return 0; }
                default: return 0;
            }
        }

        public static bool Flag(object value)
        {
            value = Shown(value);
            return value is bool flag ? flag : value is string text ? text.Length > 0 : Number(value) != 0;
        }

        public override string ToString() => (HasData ? Key + (Path.Length > 0 ? ":" + Path : "") : "") + (TextKey.Length > 0 ? "@" + TextKey : "");
    }
}
