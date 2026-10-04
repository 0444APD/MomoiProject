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
    // 틀이 없으면 그 칸의 원래 글자에 {value}가 있으면 그것을 틀로, 없으면 값 그대로 보인다.
    public sealed class ScreenBinding
    {
        public string Key { get; private set; } = "";
        public string Path { get; private set; } = "";
        public string TextKey { get; private set; } = "";

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
            }
            return binding.HasData || binding.TextKey.Length > 0 ? binding : null;
        }

        // 지금 값(없는 키·경로면 null). 목록은 그대로 돌려준다(글자로 바꿀 때 개수).
        public object Read(GameSession session)
        {
            if (!HasData || session == null || !session.Data.IsDefined(Key)) return null;
            var root = session.Data.Get(Key);
            return Path.Length == 0 ? root : FieldPaths.Resolve(root, Path).FirstOrDefault();
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
