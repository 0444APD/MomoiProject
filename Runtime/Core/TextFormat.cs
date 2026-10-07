using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace GBible
{
    // 문장 틀 해석기: ICU MessageFormat의 주요 부분 + 우리 확장 두 가지.
    //
    //   {name}                          값 넣기(숫자는 언어별 천 단위 표기, DB ID면 현재 언어의 이름)
    //   {name, number} / {n, number, integer | percent | 0.0}
    //   {d, date[, short|long]} / {t, time[, short|long]}
    //   {n, plural, [offset:1] =0 {…} one {# item} other {# items}}   # = 그 숫자
    //   {v, select, male {…} female {…} other {…}}
    //   {name:을}                       확장: 한국어 조사(받침에 맞춰 을/를 …)
    //   [[id]] [[id:을]] [[id:cap]] [[id, count]] [[id, case=acc]]
    //                                   확장: 용어(이름은 '<id>.name' 텍스트, 서식은 꾸밈 함수가 씌운다)
    //
    // 따옴표 규칙(ICU): '' → '   '{…}' → 글자 그대로   \[[ → 글자 '[['
    // 태그(<b>, <color=…>, <pause=0.5> …)는 글자로 그대로 통과한다(번역 때 지우면 안 되는 부분).
    public static class TextFormat
    {
        // ── 해석 결과 ─────────────────────────────────────────

        public sealed class Message
        {
            internal Node[] Nodes;
            public string Template { get; internal set; }
            // 틀 문법 오류(없으면 null). 오류가 있으면 틀 글자를 그대로 보여 준다.
            public string Error { get; internal set; }

            // 틀에 쓰인 값 이름·용어 ID(검증: 번역이 원문과 같은 값·용어를 쓰는지).
            public IEnumerable<string> Arguments => Walk(Nodes).OfType<ArgNode>().Select(a => a.Name)
                .Concat(Walk(Nodes).OfType<ChoiceNode>().Select(c => c.Name)).Distinct();
            public IEnumerable<string> Terms => Walk(Nodes).OfType<TermNode>().Select(t => t.Id).Distinct();

            static IEnumerable<Node> Walk(IEnumerable<Node> nodes)
            {
                if (nodes == null) yield break;
                foreach (var node in nodes)
                {
                    yield return node;
                    if (node is ChoiceNode choice)
                        foreach (var (_, body) in choice.Cases)
                            foreach (var inner in Walk(body)) yield return inner;
                }
            }
        }

        internal abstract class Node
        {
        }

        sealed class LitNode : Node
        {
            public string Text;
        }

        sealed class ArgNode : Node
        {
            public string Name;
            public string Type;
            public string Style;
            public string Particle;
        }

        sealed class PoundNode : Node
        {
        }

        sealed class ChoiceNode : Node
        {
            public string Name;
            public bool Plural;
            // selectordinal: 순서 수(1st·2nd·3rd …) 규칙.
            public bool Ordinal;
            public double Offset;
            public readonly List<(string key, Node[] body)> Cases = new List<(string, Node[])>();
        }

        sealed class TermNode : Node
        {
            public string Id;
            public readonly List<(string name, string value)> Params = new List<(string, string)>();
            public readonly List<string> Modifiers = new List<string>();
        }

        // ── 해석 ──────────────────────────────────────────────

        public static Message Parse(string template)
        {
            var message = new Message { Template = template ?? "" };
            try
            {
                var i = 0;
                message.Nodes = ParseNodes(message.Template, ref i, false, false);
                if (i < message.Template.Length) throw Error(message.Template, i, "짝이 맞지 않는 '}'");
            }
            catch (FormatException e)
            {
                message.Error = e.Message;
                message.Nodes = new Node[] { new LitNode { Text = message.Template } };
            }
            return message;
        }

        static FormatException Error(string s, int at, string what) =>
            new FormatException($"{what} ({Math.Min(at, s.Length) + 1}번째 글자 근처)");

        static Node[] ParseNodes(string s, ref int i, bool nested, bool inPlural)
        {
            var nodes = new List<Node>();
            var text = new StringBuilder();

            void Flush()
            {
                if (text.Length == 0) return;
                nodes.Add(new LitNode { Text = text.ToString() });
                text.Clear();
            }

            while (i < s.Length)
            {
                var c = s[i];
                var next = i + 1 < s.Length ? s[i + 1] : '\0';
                if (c == '}')
                {
                    if (nested) break;
                    throw Error(s, i, "짝이 맞지 않는 '}'");
                }
                if (c == '\'')
                {
                    if (next == '\'')
                    {
                        text.Append('\'');
                        i += 2;
                        continue;
                    }
                    var special = next == '{' || next == '}' || (inPlural && next == '#') || (next == '[' && i + 2 < s.Length && s[i + 2] == '[');
                    if (special)
                    {
                        // 따옴표 안은 글자 그대로('' 는 따옴표 하나).
                        i++;
                        while (i < s.Length)
                        {
                            if (s[i] == '\'')
                            {
                                if (i + 1 < s.Length && s[i + 1] == '\'')
                                {
                                    text.Append('\'');
                                    i += 2;
                                    continue;
                                }
                                i++;
                                break;
                            }
                            text.Append(s[i++]);
                        }
                        continue;
                    }
                    text.Append('\'');
                    i++;
                    continue;
                }
                if (c == '\\' && next == '[')
                {
                    text.Append('[');
                    i += 2;
                    continue;
                }
                if (c == '[' && next == '[')
                {
                    Flush();
                    nodes.Add(ParseTerm(s, ref i));
                    continue;
                }
                if (c == '#' && inPlural)
                {
                    Flush();
                    nodes.Add(new PoundNode());
                    i++;
                    continue;
                }
                if (c == '{')
                {
                    Flush();
                    nodes.Add(ParseArgument(s, ref i));
                    continue;
                }
                text.Append(c);
                i++;
            }
            Flush();
            return nodes.ToArray();
        }

        static void SkipSpace(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        static string ReadWord(string s, ref int i)
        {
            var start = i;
            while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != ',' && s[i] != ':' && s[i] != '{' && s[i] != '}') i++;
            return s.Substring(start, i - start);
        }

        static Node ParseArgument(string s, ref int i)
        {
            var open = i;
            i++; // {
            SkipSpace(s, ref i);
            var name = ReadWord(s, ref i);
            if (name.Length == 0) throw Error(s, open, "'{' 뒤에 값 이름이 없습니다");
            SkipSpace(s, ref i);
            if (i >= s.Length) throw Error(s, open, "'}'로 닫히지 않았습니다");

            if (s[i] == '}')
            {
                i++;
                return new ArgNode { Name = name };
            }
            if (s[i] == ':')
            {
                i++;
                var close = s.IndexOf('}', i);
                if (close < 0) throw Error(s, open, "'}'로 닫히지 않았습니다");
                var particle = s.Substring(i, close - i).Trim();
                i = close + 1;
                return new ArgNode { Name = name, Particle = particle };
            }
            if (s[i] != ',') throw Error(s, i, $"'{name}' 뒤에는 '}}', ',', ':' 중 하나가 와야 합니다");
            i++;
            SkipSpace(s, ref i);
            var type = ReadWord(s, ref i).ToLowerInvariant();
            SkipSpace(s, ref i);

            if (type == "plural" || type == "select" || type == "selectordinal")
            {
                if (i >= s.Length || s[i] != ',') throw Error(s, i, $"'{type}' 뒤에 ','와 경우들이 와야 합니다");
                i++;
                var choice = new ChoiceNode { Name = name, Plural = type != "select", Ordinal = type == "selectordinal" };
                while (true)
                {
                    SkipSpace(s, ref i);
                    if (i >= s.Length) throw Error(s, open, "'}'로 닫히지 않았습니다");
                    if (s[i] == '}')
                    {
                        i++;
                        break;
                    }
                    var key = ReadWord(s, ref i);
                    if (choice.Plural && key == "offset" && i < s.Length && s[i] == ':')
                    {
                        i++;
                        SkipSpace(s, ref i);
                        var number = ReadWord(s, ref i);
                        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out choice.Offset))
                            throw Error(s, i, $"offset 값 '{number}'을(를) 읽을 수 없습니다");
                        continue;
                    }
                    if (key.Length == 0) throw Error(s, i, "경우 이름이 없습니다");
                    SkipSpace(s, ref i);
                    if (i >= s.Length || s[i] != '{') throw Error(s, i, $"경우 '{key}' 뒤에 '{{…}}'가 와야 합니다");
                    i++;
                    var body = ParseNodes(s, ref i, true, choice.Plural);
                    if (i >= s.Length || s[i] != '}') throw Error(s, i, $"경우 '{key}'가 '}}'로 닫히지 않았습니다");
                    i++;
                    choice.Cases.Add((key, body));
                }
                if (!choice.Cases.Any(c => c.key == "other")) throw Error(s, open, $"'{name}'에 other 경우가 없습니다");
                return choice;
            }

            string style = null;
            if (i < s.Length && s[i] == ',')
            {
                i++;
                var close = s.IndexOf('}', i);
                if (close < 0) throw Error(s, open, "'}'로 닫히지 않았습니다");
                style = s.Substring(i, close - i).Trim();
                i = close;
            }
            SkipSpace(s, ref i);
            string particleAfter = null;
            if (style != null)
            {
                // {n, number, integer:을} — 서식 뒤의 ':조사'
                var colon = style.LastIndexOf(':');
                if (colon >= 0 && KoreanParticles.IsParticle(style.Substring(colon + 1).Trim()))
                {
                    particleAfter = style.Substring(colon + 1).Trim();
                    style = style.Substring(0, colon).Trim();
                }
                if (style.Length == 0) style = null;
            }
            else if (i < s.Length && s[i] == ':')
            {
                i++;
                var close = s.IndexOf('}', i);
                if (close < 0) throw Error(s, open, "'}'로 닫히지 않았습니다");
                particleAfter = s.Substring(i, close - i).Trim();
                i = close;
            }
            if (i >= s.Length || s[i] != '}') throw Error(s, open, "'}'로 닫히지 않았습니다");
            i++;
            return new ArgNode { Name = name, Type = type, Style = style, Particle = particleAfter };
        }

        static Node ParseTerm(string s, ref int i)
        {
            var open = i;
            var close = s.IndexOf("]]", i + 2, StringComparison.Ordinal);
            if (close < 0) throw Error(s, open, "'[['가 ']]'로 닫히지 않았습니다");
            var body = s.Substring(i + 2, close - i - 2);
            i = close + 2;
            var parts = body.Split(':');
            var head = parts[0].Split(',');
            var term = new TermNode { Id = head[0].Trim() };
            if (term.Id.Length == 0) throw Error(s, open, "'[[ ]]' 안에 용어 ID가 없습니다");
            foreach (var raw in head.Skip(1))
            {
                var param = raw.Trim();
                if (param.Length == 0) continue;
                var eq = param.IndexOf('=');
                term.Params.Add(eq < 0 ? (param, null) : (param.Substring(0, eq).Trim(), param.Substring(eq + 1).Trim()));
            }
            foreach (var modifier in parts.Skip(1))
                if (modifier.Trim().Length > 0) term.Modifiers.Add(modifier.Trim());
            return term;
        }

        // ── 만들기 ─────────────────────────────────────────────

        // 틀을 채울 때 필요한 것들. 언어·문화권, 값, DB ID → 이름, 용어 → 글자, 용어 꾸밈.
        public sealed class Context
        {
            public string Language = "";
            public CultureInfo Culture = CultureInfo.InvariantCulture;
            public IReadOnlyDictionary<string, object> Args;
            // 값이 DB ID 같은 글자면 현재 언어의 이름으로(없으면 null → 글자 그대로).
            public Func<string, string> ResolveName;
            // 텍스트 키(TextKey 값) → 글자.
            public Func<string, string> ResolveKey;
            // 용어 ID + 용어에 넘길 값 → 용어 이름(없으면 null → ID 그대로).
            public Func<string, IReadOnlyDictionary<string, object>, string> TermText;
            // 용어 이름에 서식을 씌운다(색·아이콘·툴팁 링크). 없으면 이름 그대로.
            public Func<string, string, string> DecorateTerm;
            // 가짜 번역: 틀의 글자(값 제외)를 악센트 글자로.
            public bool Pseudo;
        }

        public static string Format(Message message, Context context)
        {
            if (message == null) return "";
            var b = new StringBuilder();
            Emit(message.Nodes, context ?? new Context(), b, null);
            return b.ToString();
        }

        public static string Format(string template, Context context) => Format(Parse(template), context);

        static void Emit(Node[] nodes, Context ctx, StringBuilder b, double? pound)
        {
            foreach (var node in nodes)
            {
                switch (node)
                {
                    case LitNode lit:
                        b.Append(ctx.Pseudo ? Pseudo(lit.Text) : lit.Text);
                        break;
                    case PoundNode _:
                        b.Append(pound.HasValue ? FormatNumber(pound.Value, null, ctx.Culture) : "#");
                        break;
                    case ArgNode arg:
                        EmitArgument(arg, ctx, b);
                        break;
                    case ChoiceNode choice:
                        EmitChoice(choice, ctx, b);
                        break;
                    case TermNode term:
                        EmitTerm(term, ctx, b);
                        break;
                }
            }
        }

        // 값 이름. 그대로 없으면 점 앞부분을 값 이름, 뒷부분을 그 값 안의 필드 경로로 본다: {run.gold} = 값 "run"의 gold 필드
        // (값 이름 자체에 점이 있어도 된다: {player.gold}가 값 이름이면 그대로). 필드는 생성 코드로 읽는다.
        static bool TryArg(Context ctx, string name, out object value)
        {
            value = null;
            if (ctx.Args == null) return false;
            if (ctx.Args.TryGetValue(name, out value)) return true;
            for (var dot = name.LastIndexOf('.'); dot > 0; dot = name.LastIndexOf('.', dot - 1))
            {
                if (!ctx.Args.TryGetValue(name.Substring(0, dot), out var root) || root == null) continue;
                foreach (var found in FieldPaths.Resolve(root, name.Substring(dot + 1)))
                {
                    value = found;
                    return true;
                }
                return false;
            }
            return false;
        }

        static void EmitArgument(ArgNode arg, Context ctx, StringBuilder b)
        {
            if (!TryArg(ctx, arg.Name, out var value))
            {
                // 값이 없으면 틀을 그대로 보여 준다(빠진 값을 알아보기 쉽게).
                b.Append('{').Append(arg.Name).Append(string.IsNullOrEmpty(arg.Particle) ? "" : ":" + arg.Particle).Append('}');
                return;
            }
            var text = Stringify(value, arg.Type, arg.Style, ctx);
            b.Append(text);
            if (!string.IsNullOrEmpty(arg.Particle)) b.Append(KoreanParticles.Pick(text, arg.Particle));
        }

        static void EmitChoice(ChoiceNode choice, Context ctx, StringBuilder b)
        {
            TryArg(ctx, choice.Name, out var value);
            Node[] body = null;
            double? pound = null;
            if (choice.Plural)
            {
                var n = ToDouble(value);
                pound = n - choice.Offset;
                foreach (var (key, nodes) in choice.Cases)
                    if (key.StartsWith("=", StringComparison.Ordinal) &&
                        double.TryParse(key.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var exact) && Math.Abs(exact - n) < 1e-9)
                    {
                        body = nodes;
                        break;
                    }
                if (body == null)
                {
                    var category = choice.Ordinal ? PluralRules.Ordinal(ctx.Language, pound.Value) : PluralRules.Category(ctx.Language, pound.Value);
                    body = choice.Cases.FirstOrDefault(c => c.key == category).body;
                }
            }
            else
            {
                // 참거짓은 보이는 글자와 같은 소문자로(true/false) — {flag, select, true {…} other {…}}.
                var key = value is bool flag ? (flag ? "true" : "false") : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
                body = choice.Cases.FirstOrDefault(c => c.key == key).body;
            }
            body ??= choice.Cases.First(c => c.key == "other").body;
            Emit(body, ctx, b, pound);
        }

        static void EmitTerm(TermNode term, Context ctx, StringBuilder b)
        {
            var args = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var (name, literal) in term.Params)
            {
                if (literal == null)
                {
                    if (TryArg(ctx, name, out var passed)) args[name] = passed;
                }
                else if (double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) args[name] = number;
                else args[name] = literal;
            }
            var text = ctx.TermText?.Invoke(term.Id, args) ?? term.Id;
            string particle = null;
            foreach (var modifier in term.Modifiers)
            {
                switch (modifier)
                {
                    case "cap":
                        text = text.Length == 0 ? text : ctx.Culture.TextInfo.ToUpper(text[0]) + text.Substring(1);
                        break;
                    case "upper":
                        text = ctx.Culture.TextInfo.ToUpper(text);
                        break;
                    case "lower":
                        text = ctx.Culture.TextInfo.ToLower(text);
                        break;
                    default:
                        if (KoreanParticles.IsParticle(modifier)) particle = modifier;
                        break;
                }
            }
            // 조사는 서식(태그) 밖, 글자 바로 뒤에 붙인다.
            b.Append(ctx.DecorateTerm != null ? ctx.DecorateTerm(term.Id, text) : text);
            if (particle != null) b.Append(KoreanParticles.Pick(text, particle));
        }

        // ── 값 → 글자 ─────────────────────────────────────────

        static string Stringify(object value, string type, string style, Context ctx)
        {
            var culture = ctx.Culture;
            switch (type)
            {
                case "number":
                    return IsNumber(value) ? FormatNumber(ToDouble(value), style, culture) : Convert.ToString(value, culture);
                case "date":
                    return value is DateTime date ? Shown(date).ToString(style == "long" || style == "full" ? "D" : "d", culture) : Convert.ToString(value, culture);
                case "time":
                    return value is DateTime time ? Shown(time).ToString(style == "long" || style == "medium" ? "T" : "t", culture) : Convert.ToString(value, culture);
            }
            switch (value)
            {
                case null: return "";
                case TextKey key: return ctx.ResolveKey?.Invoke(key.Key) ?? key.Key;
                case string s: return ctx.ResolveName?.Invoke(s) ?? s;
                case bool flag: return flag ? "true" : "false";
                case DateTime moment: return Shown(moment).ToString("d", culture);
            }
            if (IsNumber(value)) return FormatNumber(ToDouble(value), null, culture);
            return Convert.ToString(value, culture);
        }

        // UTC 시각(Game.Now 등)은 이 기기의 지역 시각으로 보인다(그대로 보이면 한국에서 9시간 이르고 자정 근처에는 날짜도 틀린다).
        static DateTime Shown(DateTime value) => value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value;

        static bool IsNumber(object value) =>
            value is int || value is long || value is float || value is double || value is decimal || value is short || value is byte || value is uint || value is ulong;

        static double ToDouble(object value)
        {
            if (value == null) return 0;
            try
            {
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        public static string FormatNumber(double value, string style, CultureInfo culture)
        {
            switch (style)
            {
                case null:
                case "":
                    return value.ToString("#,##0.###", culture);
                case "integer":
                    return Math.Round(value, MidpointRounding.AwayFromZero).ToString("#,##0", culture);
                case "percent":
                    return value.ToString("#,##0.##%", culture);
                default:
                    try
                    {
                        return value.ToString(style, culture);
                    }
                    catch (FormatException)
                    {
                        return value.ToString("#,##0.###", culture);
                    }
            }
        }

        // ── 가짜 번역 ─────────────────────────────────────────

        const string Plain = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
        const string Accented = "åƀçðéƒĝĥîĵķļɱñöþǫŕšţûṽŵẋýžÅƁÇÐÉƑĜĤÎĴĶĻṀÑÖÞǪŔŠŢÛṼŴẊÝŽ";

        // 영문 글자를 악센트 글자로(태그 안은 그대로).
        public static string Pseudo(string text)
        {
            var b = new StringBuilder(text.Length);
            var inTag = false;
            foreach (var c in text)
            {
                if (c == '<') inTag = true;
                if (inTag)
                {
                    b.Append(c);
                    if (c == '>') inTag = false;
                    continue;
                }
                var index = Plain.IndexOf(c);
                b.Append(index >= 0 ? Accented[index] : c);
            }
            return b.ToString();
        }

        // 가짜 번역 전체 모양: [본문~~~] (눈에 보이는 길이의 30%만큼 늘린다).
        public static string PseudoFrame(string text)
        {
            var visible = 0;
            var inTag = false;
            foreach (var c in text)
            {
                if (c == '<') inTag = true;
                else if (c == '>' && inTag) inTag = false;
                else if (!inTag) visible++;
            }
            return "[" + text + new string('~', Math.Max(1, (int)Math.Ceiling(visible * 0.3))) + "]";
        }
    }

    // 값으로 넘기는 텍스트 키: 현재 언어의 그 텍스트로 바뀐다. 예: Texts.Get("log.gain", ("what", new TextKey("ui.gold")))
    public readonly struct TextKey
    {
        public readonly string Key;

        public TextKey(string key)
        {
            Key = key;
        }

        public override string ToString() => Key;
    }
}
