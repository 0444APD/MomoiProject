using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace GBible
{
    // 필드 경로: 클래스·컬렉션을 따라 내려가는 주소.
    //   "level"            필드 하나
    //   "weapon.power"     중첩 클래스의 필드
    //   "tags"             컬렉션 자체(포함·개수 검사용)
    //   "items[].level"    컬렉션의 각 요소의 필드(하나라도 만족하면 참)
    //   "materials[book]"  딕셔너리의 키 하나(키는 글자로, 없으면 비어 있음 — 쓰면 그 키가 생긴다)
    //   "units[hero].hp"   목록에서 id가 hero인 요소(글자 목록이면 그 글자) 하나
    // 딕셔너리는 값들을 요소로 본다. 필드 외에 읽을 수 있는 공개 속성도 따라간다(이벤트 클래스용).
    public static class FieldPaths
    {
        public const string Each = "[]";

        public sealed class Member
        {
            public string Name;
            public Type Type;
            public MemberInfo Info;
            // 컬렉션이면 요소 타입(아니면 null).
            public Type ElementType;
            public bool IsCollection => ElementType != null;
        }

        // 타입의 하위 항목(탐색기의 폴더 내용).
        public static IReadOnlyList<Member> MembersOf(Type type)
        {
            var result = new List<Member>();
            if (type == null) return result;
            var info = DataModel.Of(type);
            if (info.Kind == DataKind.Object)
            {
                foreach (var field in info.Fields) result.Add(Describe(field.Name, field.Type, field.Field));
                var names = new HashSet<string>(result.Select(m => m.Name), StringComparer.Ordinal);
                foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
                    if (property.CanRead && property.GetIndexParameters().Length == 0 && names.Add(property.Name) && !property.IsDefined(typeof(ObsoleteAttribute), true))
                        result.Add(Describe(property.Name, property.PropertyType, property));
            }
            return result;
        }

        static Member Describe(string name, Type type, MemberInfo info) =>
            new Member { Name = name, Type = type, Info = info, ElementType = ElementTypeOf(type) };

        public static Type ElementTypeOf(Type type)
        {
            if (type == null || type == typeof(string)) return null;
            var info = DataModel.Of(type);
            switch (info.Kind)
            {
                case DataKind.List:
                case DataKind.Array:
                case DataKind.Collection:
                case DataKind.Dictionary:
                    return info.ElementType;
            }
            return null;
        }

        static readonly Dictionary<(Type, string), (Type type, MemberInfo member, string error)> TypeCache =
            new Dictionary<(Type, string), (Type, MemberInfo, string)>();

        // 경로 끝의 타입과 멤버(경로가 틀리면 null과 오류 문장). 형식·경로마다 처음 한 번만 찾는다.
        public static Type TypeAt(Type root, string path, out MemberInfo member, out string error)
        {
            if (root == null)
            {
                member = null;
                error = null;
                return null;
            }
            var key = (root, path ?? "");
            lock (TypeCache)
                if (TypeCache.TryGetValue(key, out var cached))
                {
                    member = cached.member;
                    error = cached.error;
                    return cached.type;
                }
            var found = FindType(root, path, out member, out error);
            lock (TypeCache) TypeCache[key] = (found, member, error);
            return found;
        }

        static Type FindType(Type root, string path, out MemberInfo member, out string error)
        {
            member = null;
            error = null;
            var type = root;
            if (string.IsNullOrEmpty(path)) return root;
            foreach (var raw in Split(path))
            {
                var step = ToStep(raw);
                var many = step.Many || step.Select != null;
                var name = step.Name;
                if (name.Length > 0)
                {
                    var found = MembersOf(type).FirstOrDefault(m => m.Name == name);
                    if (found == null)
                    {
                        error = $"'{DataModel.FriendlyName(type)}'에 '{name}' 필드가 없습니다.";
                        return null;
                    }
                    type = found.Type;
                    member = found.Info;
                }
                if (many)
                {
                    var element = ElementTypeOf(type);
                    if (element == null)
                    {
                        error = step.Select != null ? $"'{name}'는 컬렉션이 아니라 [{step.Select}]를 쓸 수 없습니다." : $"'{name}'는 컬렉션이 아니라 [] 를 쓸 수 없습니다.";
                        return null;
                    }
                    type = element;
                }
            }
            return type;
        }

        public static Type TypeAt(Type root, string path, out string error) => TypeAt(root, path, out _, out error);

        // 경로 한 마디(이름 + 요소마다 펼치기). 경로 글자는 처음 쓸 때 한 번 나눠 둔다.
        struct Step
        {
            public string Name;
            public bool Many;
            // name[키]: 딕셔너리의 그 키 · 목록에서 id가 그 키인 요소(없으면 null).
            public string Select;
            // name[필드=값]: 목록 · 딕셔너리 값에서 그 필드가 그 값인 요소 모두(원본 ID로 고르기 — buildings[template=lens]).
            public string FilterField;
            public string FilterValue;
        }

        static readonly Dictionary<string, Step[]> Parsed = new Dictionary<string, Step[]>(StringComparer.Ordinal);

        static Step[] Parse(string path)
        {
            lock (Parsed)
            {
                if (Parsed.TryGetValue(path, out var steps)) return steps;
                steps = Split(path).Select(ToStep).ToArray();
                Parsed[path] = steps;
                return steps;
            }
        }

        static Step ToStep(string raw)
        {
            if (raw.EndsWith(Each, StringComparison.Ordinal)) return new Step { Name = raw.Substring(0, raw.Length - Each.Length), Many = true };
            var open = raw.IndexOf('[');
            if (open >= 0 && raw.EndsWith("]", StringComparison.Ordinal))
            {
                var select = raw.Substring(open + 1, raw.Length - open - 2);
                var step = new Step { Name = raw.Substring(0, open), Select = select };
                var equal = select.IndexOf('=');
                if (equal > 0)
                {
                    step.FilterField = select.Substring(0, equal).Trim();
                    step.FilterValue = select.Substring(equal + 1).Trim();
                }
                return step;
            }
            return new Step { Name = raw };
        }

        // 경로를 마디로 나눈다(점으로. [ ] 안의 점은 나누지 않는다 — 키 "forest.chest3").
        public static List<string> Split(string path)
        {
            var parts = new List<string>();
            if (string.IsNullOrEmpty(path)) return parts;
            var depth = 0;
            var start = 0;
            for (var i = 0; i < path.Length; i++)
            {
                if (path[i] == '[') depth++;
                else if (path[i] == ']') depth = Math.Max(0, depth - 1);
                else if (path[i] == '.' && depth == 0)
                {
                    parts.Add(path.Substring(start, i - start));
                    start = i + 1;
                }
            }
            parts.Add(path.Substring(start));
            return parts;
        }

        // 마지막 마디와 그 앞(부모 경로). "hero.stats[atk]" → "hero", "stats[atk]".
        public static void SplitLast(string path, out string parent, out string last)
        {
            var parts = Split(path ?? "");
            last = parts.Count == 0 ? "" : parts[parts.Count - 1];
            parent = parts.Count <= 1 ? "" : string.Join(".", parts.Take(parts.Count - 1));
        }

        public static bool IsSelect(string step) => ToStep(step ?? "").Select != null;

        // 마지막 마디 하나에 쓴다: 필드(생성 코드)나 딕셔너리의 키(name[키], 없던 키면 만든다). next(지금 값, 형식)이 null이면 쓰지 않는다. 썼으면 참.
        public static bool Assign(object parent, string last, Func<object, Type, object> next)
        {
            if (parent == null || string.IsNullOrEmpty(last)) return false;
            var step = ToStep(last);
            if (step.Many || step.FilterField != null) return false;   // 여럿을 고르는 마디에는 쓰지 않는다
            if (step.Select == null)
            {
                var access = FieldAccess.Find(parent.GetType(), step.Name);
                var type = TypeAt(parent.GetType(), step.Name, out _);
                if (access == null || type == null) return false;
                var value = next(access.Get(parent), type);
                if (value == null) return false;
                access.Set(parent, value);
                return true;
            }
            var collection = step.Name.Length == 0 ? parent : Read(parent, step.Name);
            if (!(collection is IDictionary map)) return false;
            var info = DataModel.Of(collection.GetType());
            if (!TryKey(info.KeyType, step.Select, out var key)) return false;
            var nextValue = next(map.Contains(key) ? map[key] : null, info.ElementType);
            if (nextValue == null) return false;
            map[key] = nextValue;
            return true;
        }

        static bool TryKey(Type type, string text, out object key)
        {
            key = null;
            if (type == null || type == typeof(string)) { key = text; return true; }
            try
            {
                if (type.IsEnum) { key = Enum.Parse(type, text); return true; }
                key = Convert.ChangeType(text, type, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception) { return false; }
        }

        // name[키]로 고르기: 딕셔너리면 그 키의 값, 목록이면 id(글자 목록이면 그 글자)가 키인 첫 요소.
        static object Pick(object collection, string key)
        {
            switch (collection)
            {
                case string _:
                    return null;
                case IDictionary map:
                {
                    // 쓰기(Assign)와 같게 키 형식으로 바꿔 찾는다(flags[true]·byLevel[01]도). 바꿀 수 없으면 글자로 견준다.
                    if (TryKey(DataModel.Of(map.GetType()).KeyType, key, out var typed) && typed != null && map.Contains(typed)) return map[typed];
                    foreach (DictionaryEntry entry in map)
                        if (string.Equals(Convert.ToString(entry.Key, CultureInfo.InvariantCulture), key, StringComparison.Ordinal)) return entry.Value;
                    return null;
                }
                case IEnumerable list:
                    foreach (var item in list)
                    {
                        if (item is string text) { if (text == key) return item; continue; }
                        if (item != null && GameApi.HasId(item.GetType()) && GameApi.IdOf(item) == key) return item;
                    }
                    return null;
                default:
                    return null;
            }
        }

        // 경로의 값들. [] 가 있으면 요소마다 펼친다. 중간에 null이 있으면 그 갈래는 비어 있다.
        // 멤버는 생성 코드(FieldAccess)로 읽는다(리플렉션 없음).
        public static IEnumerable<object> Resolve(object root, string path)
        {
            var current = new List<object> { root };
            if (string.IsNullOrEmpty(path)) return current;
            var next = new List<object>();
            foreach (var step in Parse(path))
            {
                next.Clear();
                foreach (var node in current)
                {
                    var value = step.Name.Length == 0 ? node : Read(node, step.Name);
                    if (value == null) continue;
                    if (step.FilterField != null)
                    {
                        foreach (var item in Items(value))
                            if (Matches(item, step.FilterField, step.FilterValue)) next.Add(item);
                    }
                    else if (step.Select != null)
                    {
                        var picked = Pick(value, step.Select);
                        if (picked != null) next.Add(picked);
                    }
                    else if (!step.Many) next.Add(value);
                    else AddItems(value, next);
                }
                (current, next) = (next, current);
            }
            return current;
        }

        public static object Read(object target, string name) => FieldAccess.Read(target, name);

        // [필드=값] 고르기: 요소의 그 필드 글자가 값과 같은가(enum은 이름, 숫자는 그 글자).
        static bool Matches(object item, string field, string value)
        {
            if (item == null || item is string) return false;
            var read = FieldAccess.Read(item, field);
            return read != null && string.Equals(Convert.ToString(read, CultureInfo.InvariantCulture), value, StringComparison.Ordinal);
        }

        static void AddItems(object collection, List<object> into)
        {
            switch (collection)
            {
                case string _:
                    into.Add(collection);
                    return;
                case IDictionary map:
                    foreach (var value in map.Values) into.Add(value);
                    return;
                case IEnumerable list:
                    foreach (var item in list) into.Add(item);
                    return;
            }
            into.Add(collection);
        }

        public static IEnumerable<object> Items(object collection)
        {
            switch (collection)
            {
                case string _: return new[] { collection };
                case IDictionary map: return map.Values.Cast<object>();
                case IEnumerable list: return list.Cast<object>();
            }
            return new[] { collection };
        }

        public static bool IsCollectionValue(object value) => value != null && !(value is string) && value is IEnumerable;
    }

    // 필드 형식별 비교. 숫자는 크기, 글자·enum은 일치, 컬렉션은 포함·개수, 참/거짓은 참·거짓.
    public enum FieldOp
    {
        Equal,
        NotEqual,
        AtLeast,
        AtMost,
        Greater,
        Less,
        IsTrue,
        IsFalse,
        Contains,
        NotContains,
        CountAtLeast,
        CountAtMost,
        CountEqual,
        IsEmpty,
        IsNotEmpty,
    }

    public static class FieldOps
    {
        // 게임 숫자 비교의 한 가지 규칙: 아주 작은 차이(1e-9, 1000을 넘는 수는 그 크기의 1e-12배)는 같다고 본다 — 0.1씩 열 번 찬 마나
        // (0.9999999999999999)도 1이다. '같음'뿐 아니라 이상·이하·초과·미만·비용(Game.Pay·CanPay)도 이 기준이라 '1과 같은데 1 이상은 아님'이 없다.
        public static bool Near(double a, double b) =>
            a == b || Math.Abs(a - b) <= Math.Max(1e-9, Math.Max(Math.Abs(a), Math.Abs(b)) * 1e-12);

        public static bool AtLeast(double value, double limit) => value >= limit || Near(value, limit);
        public static bool AtMost(double value, double limit) => value <= limit || Near(value, limit);
        public static bool Greater(double value, double limit) => value > limit && !Near(value, limit);
        public static bool Less(double value, double limit) => value < limit && !Near(value, limit);

        static readonly FieldOp[] Numeric = { FieldOp.AtLeast, FieldOp.AtMost, FieldOp.Greater, FieldOp.Less, FieldOp.Equal, FieldOp.NotEqual };
        static readonly FieldOp[] Text = { FieldOp.Equal, FieldOp.NotEqual, FieldOp.IsEmpty, FieldOp.IsNotEmpty };
        static readonly FieldOp[] Boolean = { FieldOp.IsTrue, FieldOp.IsFalse };
        static readonly FieldOp[] Choice = { FieldOp.Equal, FieldOp.NotEqual };
        static readonly FieldOp[] Collection = { FieldOp.Contains, FieldOp.NotContains, FieldOp.CountAtLeast, FieldOp.CountAtMost, FieldOp.CountEqual, FieldOp.IsEmpty, FieldOp.IsNotEmpty };
        static readonly FieldOp[] Presence = { FieldOp.IsEmpty, FieldOp.IsNotEmpty };

        // 경로 끝 타입에 맞는 비교 목록(편집 화면이 이것만 보여 준다).
        public static IReadOnlyList<FieldOp> For(Type type)
        {
            if (type == null) return Array.Empty<FieldOp>();
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (type == typeof(bool)) return Boolean;
            if (type.IsEnum) return Choice;
            if (TypeResolver.IsNumeric(type)) return Numeric;
            if (type == typeof(string)) return Text;
            if (FieldPaths.ElementTypeOf(type) != null) return Collection;
            return Presence;
        }

        public static bool NeedsValue(FieldOp op) =>
            op != FieldOp.IsTrue && op != FieldOp.IsFalse && op != FieldOp.IsEmpty && op != FieldOp.IsNotEmpty;

        public static bool IsCount(FieldOp op) => op == FieldOp.CountAtLeast || op == FieldOp.CountAtMost || op == FieldOp.CountEqual;

        public static string Word(FieldOp op)
        {
            switch (op)
            {
                case FieldOp.Equal: return "일치";
                case FieldOp.NotEqual: return "아님";
                case FieldOp.AtLeast: return "이상";
                case FieldOp.AtMost: return "이하";
                case FieldOp.Greater: return "초과";
                case FieldOp.Less: return "미만";
                case FieldOp.IsTrue: return "참";
                case FieldOp.IsFalse: return "거짓";
                case FieldOp.Contains: return "포함";
                case FieldOp.NotContains: return "미포함";
                case FieldOp.CountAtLeast: return "개수 이상";
                case FieldOp.CountAtMost: return "개수 이하";
                case FieldOp.CountEqual: return "개수 일치";
                case FieldOp.IsEmpty: return "비어 있음";
                default: return "비어 있지 않음";
            }
        }

        // 한 문장: "등급 3 이상", "태그 boss 포함", "아이템 개수 5 이상", "보스 참"
        public static string Sentence(string pathLabel, FieldOp op, string value) =>
            NeedsValue(op) ? $"{pathLabel} {value} {Word(op)}" : $"{pathLabel} {Word(op)}";

        // 코드처럼 보이는 기호: 등급 >= 3, 태그 contains boss
        public static string Symbol(FieldOp op)
        {
            switch (op)
            {
                case FieldOp.Equal: return "==";
                case FieldOp.NotEqual: return "!=";
                case FieldOp.AtLeast: return ">=";
                case FieldOp.AtMost: return "<=";
                case FieldOp.Greater: return ">";
                case FieldOp.Less: return "<";
                case FieldOp.IsTrue: return "== true";
                case FieldOp.IsFalse: return "== false";
                case FieldOp.Contains: return "contains";
                case FieldOp.NotContains: return "!contains";
                case FieldOp.CountAtLeast: return "count >=";
                case FieldOp.CountAtMost: return "count <=";
                case FieldOp.CountEqual: return "count ==";
                case FieldOp.IsEmpty: return "is empty";
                default: return "is not empty";
            }
        }

        public static string Code(string pathLabel, FieldOp op, string value) =>
            NeedsValue(op) ? $"{pathLabel} {Symbol(op)} {value}" : $"{pathLabel} {Symbol(op)}";

        // 자연어 조각(이어 붙이기용): "등급이 3 이상", "태그에 boss 포함"
        public static string Clause(string pathLabel, FieldOp op, string value)
        {
            switch (op)
            {
                case FieldOp.AtLeast: return $"{Josa.IGa(pathLabel)} {value} 이상";
                case FieldOp.AtMost: return $"{Josa.IGa(pathLabel)} {value} 이하";
                case FieldOp.Greater: return $"{Josa.IGa(pathLabel)} {value} 초과";
                case FieldOp.Less: return $"{Josa.IGa(pathLabel)} {value} 미만";
                case FieldOp.Equal: return $"{Josa.IGa(pathLabel)} {value}";
                case FieldOp.NotEqual: return $"{Josa.IGa(pathLabel)} {Josa.IGa(value)} 아님";
                case FieldOp.IsTrue: return $"{Josa.IGa(pathLabel)} 참";
                case FieldOp.IsFalse: return $"{Josa.IGa(pathLabel)} 거짓";
                case FieldOp.Contains: return $"{pathLabel}에 {value} 포함";
                case FieldOp.NotContains: return $"{pathLabel}에 {value} 없음";
                case FieldOp.CountAtLeast: return $"{pathLabel} 개수 {value} 이상";
                case FieldOp.CountAtMost: return $"{pathLabel} 개수 {value} 이하";
                case FieldOp.CountEqual: return $"{pathLabel} 개수 {value}개";
                case FieldOp.IsEmpty: return $"{Josa.IGa(pathLabel)} 비어 있음";
                default: return $"{Josa.IGa(pathLabel)} 비어 있지 않음";
            }
        }

        // 설명 문장: "등급이 3 이상이라면", "태그에 boss를 포함한다면"
        public static string Natural(string pathLabel, FieldOp op, string value)
        {
            switch (op)
            {
                case FieldOp.AtLeast: return $"{Josa.IGa(pathLabel)} {value} 이상이라면";
                case FieldOp.AtMost: return $"{Josa.IGa(pathLabel)} {value} 이하라면";
                case FieldOp.Greater: return $"{Josa.IGa(pathLabel)} {value}보다 크다면";
                case FieldOp.Less: return $"{Josa.IGa(pathLabel)} {value}보다 작다면";
                case FieldOp.Equal: return $"{Josa.IGa(pathLabel)} {value}{Josa.RaMyeon(value)}";
                case FieldOp.NotEqual: return $"{Josa.IGa(pathLabel)} {Josa.IGa(value)} 아니라면";
                case FieldOp.IsTrue: return $"{Josa.IGa(pathLabel)} 참이라면";
                case FieldOp.IsFalse: return $"{Josa.IGa(pathLabel)} 거짓이라면";
                case FieldOp.Contains: return $"{pathLabel}에 {Josa.IGa(value)} 들어 있다면";
                case FieldOp.NotContains: return $"{pathLabel}에 {Josa.IGa(value)} 없다면";
                case FieldOp.CountAtLeast: return $"{pathLabel}의 개수가 {value} 이상이라면";
                case FieldOp.CountAtMost: return $"{pathLabel}의 개수가 {value} 이하라면";
                case FieldOp.CountEqual: return $"{pathLabel}의 개수가 {value}개라면";
                case FieldOp.IsEmpty: return $"{Josa.IGa(pathLabel)} 비어 있다면";
                default: return $"{Josa.IGa(pathLabel)} 비어 있지 않다면";
            }
        }

        // 경로의 값들이 조건을 만족하는지. 값이 여러 개([] 경로)면 하나라도 만족하면 참(개수·비어 있음은 전체 기준).
        public static bool Test(object root, string path, FieldOp op, string value)
        {
            var values = FieldPaths.Resolve(root, path).ToList();
            switch (op)
            {
                case FieldOp.CountAtLeast:
                case FieldOp.CountAtMost:
                case FieldOp.CountEqual:
                {
                    var count = values.Count == 1 && FieldPaths.IsCollectionValue(values[0]) ? FieldPaths.Items(values[0]).Count() : values.Count;
                    if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var target)) return false;
                    return op == FieldOp.CountAtLeast ? count >= target : op == FieldOp.CountAtMost ? count <= target : Math.Abs(count - target) < 1e-9;
                }
                case FieldOp.IsEmpty:
                    return values.All(IsEmptyValue);
                case FieldOp.IsNotEmpty:
                    return !values.All(IsEmptyValue);
                case FieldOp.Contains:
                    return values.Any(v => ContainsValue(v, value));
                case FieldOp.NotContains:
                    return !values.Any(v => ContainsValue(v, value));
                case FieldOp.NotEqual:
                    return values.Count > 0 && values.All(v => !Same(v, value));
            }
            return values.Any(v => Compare(v, op, value));
        }

        static bool IsEmptyValue(object value) =>
            value == null || (value is string s && s.Length == 0) || (FieldPaths.IsCollectionValue(value) && !FieldPaths.Items(value).Any());

        static bool ContainsValue(object value, string target) =>
            FieldPaths.IsCollectionValue(value) ? FieldPaths.Items(value).Any(item => Same(item, target)) : Same(value, target);

        static bool Compare(object value, FieldOp op, string target)
        {
            switch (op)
            {
                case FieldOp.IsTrue: return value is bool b && b;
                case FieldOp.IsFalse: return value is bool f && !f;
                case FieldOp.Equal: return Same(value, target);
            }
            // 큰 수 칸(BigNumber): 값 글자도 큰 수로 읽는다(1e400 · 1.5K).
            if (value is BigNumber big)
            {
                if (!BigNumber.TryParse(target, out var bigLimit)) return false;
                switch (op)
                {
                    case FieldOp.AtLeast: return big >= bigLimit;
                    case FieldOp.AtMost: return big <= bigLimit;
                    case FieldOp.Greater: return big > bigLimit;
                    case FieldOp.Less: return big < bigLimit;
                }
                return false;
            }
            if (!TryNumber(value, out var number) || !double.TryParse(target, NumberStyles.Float, CultureInfo.InvariantCulture, out var limit)) return false;
            switch (op)
            {
                case FieldOp.AtLeast: return AtLeast(number, limit);
                case FieldOp.AtMost: return AtMost(number, limit);
                case FieldOp.Greater: return Greater(number, limit);
                case FieldOp.Less: return Less(number, limit);
            }
            return false;
        }

        static bool Same(object value, string target)
        {
            if (value == null) return string.IsNullOrEmpty(target);
            if (value is bool b) return string.Equals(target, b ? "true" : "false", StringComparison.OrdinalIgnoreCase) || target == (b ? "참" : "거짓");
            if (value is Enum e) return string.Equals(e.ToString(), target, StringComparison.Ordinal);
            if (value is BigNumber big) return BigNumber.TryParse(target, out var other) && big == other;
            if (TryNumber(value, out var number))
                return double.TryParse(target, NumberStyles.Float, CultureInfo.InvariantCulture, out var other) && Near(number, other);
            return string.Equals(Convert.ToString(value, CultureInfo.InvariantCulture), target, StringComparison.Ordinal);
        }

        static bool TryNumber(object value, out double number)
        {
            number = 0;
            if (value == null || value is bool || value is Enum || !TypeResolver.IsNumeric(value.GetType())) return false;
            // float는 적힌 소수 그대로(0.1f는 double로 0.10000000149… — 0.1과 같다고 보게).
            number = value is float f && !float.IsNaN(f) && !float.IsInfinity(f) && Math.Abs(f) < 7.9e27f ? (double)(decimal)f : Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return true;
        }

        // 편집·검증용: 값 글자가 이 타입으로 읽히는지.
        public static bool TryValidateValue(Type type, FieldOp op, string value, out string error)
        {
            error = null;
            if (!NeedsValue(op)) return true;
            if (IsCount(op) || (type != null && TypeResolver.IsNumeric(Nullable.GetUnderlyingType(type) ?? type) && type != typeof(bool)))
            {
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) return true;
                error = $"'{value}'는 숫자가 아닙니다.";
                return false;
            }
            var target = op == FieldOp.Contains || op == FieldOp.NotContains ? FieldPaths.ElementTypeOf(type) ?? type : type;
            target = target == null ? null : Nullable.GetUnderlyingType(target) ?? target;
            if (target != null && target.IsEnum && !Enum.IsDefined(target, value ?? ""))
            {
                error = $"'{value}'는 {target.Name}에 없는 값입니다.";
                return false;
            }
            return true;
        }
    }

    // ── 편집기 표시용 어트리뷰트 ──────────────────────────────────

    // 필드 경로 문자열. 편집기는 폴더형 탐색기로 고르게 한다. 뿌리 타입은 같은 객체(또는 위쪽 객체)의 [FieldPathRoot] 필드로 정해진다.
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class FieldPathAttribute : Attribute
    {
        // 비어 있을 때의 뜻(예: "값 전체"). 없으면 비어 있는 칸은 '필드 선택'으로 보인다.
        public string EmptyLabel { get; set; }
        // 뿌리로 삼을 같은 객체의 칸 이름. 비우면 위쪽에서 처음 만나는 뿌리 칸. 트리거 칸이면 그 트리거가 받는 이벤트(패시브 효과의 '가진 쪽 필드').
        public string Root { get; set; }
    }

    // 이 필드(컬렉션 경로)의 안쪽 조건은 컬렉션 요소를 뿌리로 삼는다(목록 항목 검사).
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class FieldPathScopeAttribute : Attribute
    {
    }

    // 필드 경로의 뿌리를 정하는 필드. Kind: 이벤트 타입 이름 / 플레이어 데이터.
    public enum FieldPathRootKind
    {
        EventType,
        PlayerDataKey,
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class FieldPathRootAttribute : Attribute
    {
        public FieldPathRootKind Kind { get; }

        public FieldPathRootAttribute(FieldPathRootKind kind)
        {
            Kind = kind;
        }
    }

    // 비교 선택: 경로 끝 타입에 맞는 비교만 보여 준다.
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class FieldOpRefAttribute : Attribute
    {
        public string PathField { get; }

        public FieldOpRefAttribute(string pathField)
        {
            PathField = pathField;
        }
    }

    // 비교 값: 경로 끝이 enum·참거짓·ID 참조면 목록으로 고르게 한다.
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class FieldValueRefAttribute : Attribute
    {
        public string PathField { get; }
        public string OpField { get; }

        public FieldValueRefAttribute(string pathField, string opField)
        {
            PathField = pathField;
            OpField = opField;
        }
    }

    // 플레이어 데이터 ID. 편집기는 데이터베이스의 플레이어 데이터 목록을 띄운다.
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class PlayerDataRefAttribute : Attribute
    {
    }
}
