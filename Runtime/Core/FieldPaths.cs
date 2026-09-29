using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace GameSystems
{
    // 필드 경로: 클래스·컬렉션을 따라 내려가는 주소.
    //   "level"            필드 하나
    //   "weapon.power"     중첩 클래스의 필드
    //   "tags"             컬렉션 자체(포함·개수 검사용)
    //   "items[].level"    컬렉션의 각 요소의 필드(하나라도 만족하면 참)
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

        // 경로 끝의 타입과 멤버(경로가 틀리면 null과 오류 문장).
        public static Type TypeAt(Type root, string path, out MemberInfo member, out string error)
        {
            member = null;
            error = null;
            var type = root;
            if (string.IsNullOrEmpty(path)) return root;
            foreach (var raw in path.Split('.'))
            {
                var many = raw.EndsWith(Each, StringComparison.Ordinal);
                var name = many ? raw.Substring(0, raw.Length - Each.Length) : raw;
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
                        error = $"'{name}'는 컬렉션이 아니라 [] 를 쓸 수 없습니다.";
                        return null;
                    }
                    type = element;
                }
            }
            return type;
        }

        public static Type TypeAt(Type root, string path, out string error) => TypeAt(root, path, out _, out error);

        // 경로의 값들. [] 가 있으면 요소마다 펼친다. 중간에 null이 있으면 그 갈래는 비어 있다.
        public static IEnumerable<object> Resolve(object root, string path)
        {
            IEnumerable<object> current = new[] { root };
            if (string.IsNullOrEmpty(path)) return current;
            foreach (var raw in path.Split('.'))
            {
                var many = raw.EndsWith(Each, StringComparison.Ordinal);
                var name = many ? raw.Substring(0, raw.Length - Each.Length) : raw;
                current = current.SelectMany(node =>
                {
                    var value = name.Length == 0 ? node : Read(node, name);
                    if (value == null) return Enumerable.Empty<object>();
                    return many ? Items(value) : new[] { value };
                }).ToList();
            }
            return current;
        }

        public static object Read(object target, string name)
        {
            if (target == null) return null;
            var type = target.GetType();
            var field = DataModel.Of(type).Fields.FirstOrDefault(f => f.Name == name);
            if (field != null) return field.Field.GetValue(target);
            var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            return property != null && property.CanRead && property.GetIndexParameters().Length == 0 ? property.GetValue(target) : null;
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

    // 필드 형식별 비교. 숫자는 크기, 글자·선택지는 일치, 컬렉션은 포함·개수, 참/거짓은 참·거짓.
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
            if (!TryNumber(value, out var number) || !double.TryParse(target, NumberStyles.Float, CultureInfo.InvariantCulture, out var limit)) return false;
            switch (op)
            {
                case FieldOp.AtLeast: return number >= limit;
                case FieldOp.AtMost: return number <= limit;
                case FieldOp.Greater: return number > limit;
                case FieldOp.Less: return number < limit;
            }
            return false;
        }

        static bool Same(object value, string target)
        {
            if (value == null) return string.IsNullOrEmpty(target);
            if (value is bool b) return string.Equals(target, b ? "true" : "false", StringComparison.OrdinalIgnoreCase) || target == (b ? "참" : "거짓");
            if (value is Enum e) return string.Equals(e.ToString(), target, StringComparison.Ordinal);
            if (TryNumber(value, out var number))
                return double.TryParse(target, NumberStyles.Float, CultureInfo.InvariantCulture, out var other) && Math.Abs(number - other) < 1e-9;
            return string.Equals(Convert.ToString(value, CultureInfo.InvariantCulture), target, StringComparison.Ordinal);
        }

        static bool TryNumber(object value, out double number)
        {
            number = 0;
            if (value == null || value is bool || value is Enum || !TypeResolver.IsNumeric(value.GetType())) return false;
            number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
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

    // 필드 검사 한 줄(경로 + 비교 + 값). 수치 규칙의 걸러내기 등에 쓴다.
    [Serializable]
    public sealed class FieldCheck
    {
        [FieldPath, FieldLabel("필드")] public string path = "";
        [FieldOpRef(nameof(path)), FieldLabel("비교")] public FieldOp op = FieldOp.Equal;
        [FieldValueRef(nameof(path), nameof(op)), FieldLabel("값")] public string value = "";

        public bool Test(object root) => FieldOps.Test(root, path, op, value);

        public string Describe() => FieldOps.Sentence(string.IsNullOrEmpty(path) ? "(필드 없음)" : PathLabel(path), op, value);

        public static string PathLabel(string path) => string.IsNullOrEmpty(path) ? "(전체)" : path.Replace("[]", "의 각 항목").Replace(".", " › ");

        // root 타입 기준으로 경로·비교·값이 맞는지.
        public bool Validate(Type root, out string error)
        {
            if (string.IsNullOrEmpty(path))
            {
                error = "검사할 필드가 선택되지 않았습니다.";
                return false;
            }
            var type = FieldPaths.TypeAt(root, path, out error);
            if (type == null) return false;
            if (!FieldOps.For(type).Contains(op))
            {
                error = $"'{PathLabel(path)}'({DataModel.FriendlyName(type)})에는 '{FieldOps.Word(op)}' 비교를 쓸 수 없습니다.";
                return false;
            }
            return FieldOps.TryValidateValue(type, op, value, out error);
        }
    }

    // ── 편집기 표시용 어트리뷰트 ──────────────────────────────────

    // 필드 경로 문자열. 편집기는 폴더형 탐색기로 고르게 한다. 뿌리 타입은 같은 객체(또는 위쪽 객체)의 [FieldPathRoot] 필드로 정해진다.
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class FieldPathAttribute : Attribute
    {
    }

    // 필드 경로의 뿌리를 정하는 필드. Kind: 이벤트 타입 이름 / 상태 키.
    public enum FieldPathRootKind
    {
        EventType,
        StateKey,
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

    // 상태 키 ID. 편집기는 데이터베이스의 상태 키 목록을 띄운다.
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class StateKeyRefAttribute : Attribute
    {
    }
}
