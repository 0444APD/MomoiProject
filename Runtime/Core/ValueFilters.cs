using System;
using System.Collections.Generic;
using System.Linq;

namespace GBible
{
    // 값 조건식: 어떤 객체(이벤트, 저장소 값, 컬렉션의 한 항목)를 뿌리로 두고 참·거짓을 판정하는 트리.
    // 필드 검사 · 모두 만족 · 하나라도 만족 · 아님 · 목록 항목 검사를 자유롭게 중첩해 어떤 식이든 만든다.
    //   예) 모두 만족
    //         ├ 등급 3 이상
    //         └ 하나라도 만족
    //              ├ 태그 boss 포함
    //              └ 목록 항목 검사: 드롭 중 [희귀도 5 이상]인 항목이 2개 이상
    // 쓰는 곳: 수치 반영 규칙의 조건(뿌리 = 이벤트), 조건 블록 "플레이어 데이터 검사"(뿌리 = 플레이어 데이터 값).
    [Serializable]
    public abstract class ValueFilter
    {
        public abstract bool Test(object root);
        // 코드처럼 보이는 식(요약·기록용): 등급 >= 3 && (태그 contains boss || !(이름 == 활))
        public abstract string Describe();
        // 마우스를 올렸을 때 보이는 설명 문장.
        public virtual string Explain() => Describe();
        // 자연어 요약(DB·로그 기본 표시): 등급이 3 이상 그리고 (태그에 boss 포함 또는 …)
        public abstract string Natural();

        public static string NaturalOrAll(ValueFilter filter) => filter == null ? "항상" : filter.Natural();

        protected static string Wrap(ValueFilter filter) => filter.Children.Any() && !(filter is ElementFilter) ? $"({filter.Natural()})" : filter.Natural();
        public virtual IEnumerable<ValueFilter> Children => Array.Empty<ValueFilter>();

        // root 타입 기준 검증. 문제는 errors에 문장으로.
        public abstract void Validate(Type root, List<string> errors);

        public static string DescribeOrAll(ValueFilter filter) => filter == null ? "true" : filter.Describe();
    }

    [Serializable]
    [BlockInfo("필드 검사", Category = "필드", Description = "필드 하나를 비교합니다.", Sentence = "{path} {op} {value}")]
    public sealed class FieldFilter : ValueFilter
    {
        [FieldPath, FieldLabel("필드")] public string path = "";
        [FieldOpRef(nameof(path)), FieldLabel("비교")] public FieldOp op = FieldOp.Equal;
        [FieldValueRef(nameof(path), nameof(op)), FieldLabel("값")] public string value = "";

        public override bool Test(object root) => FieldOps.Test(root, path, op, value);

        public override string Describe() => FieldOps.Code(PathLabel(path), op, value);
        public override string Explain() => FieldOps.Natural(PathLabel(path), op, value);
        public override string Natural() => FieldOps.Clause(PathLabel(path), op, value);

        public static string PathLabel(string path) =>
            string.IsNullOrEmpty(path) ? "(필드 없음)" : path.Replace("[]", "의 각 항목").Replace(".", " › ");

        // 경로를 비우면 값 전체 — 숫자·글자·참거짓·목록 하나인 값(골드 · 이름 목록)은 실행할 때도 그렇게 본다. 클래스 값이면 칸을 골라야 한다.
        public static bool IsWholeValue(Type type)
        {
            if (type == null) return false;
            type = Nullable.GetUnderlyingType(type) ?? type;
            return type == typeof(bool) || type.IsEnum || TypeResolver.IsNumeric(type) || type == typeof(string) || FieldPaths.ElementTypeOf(type) != null;
        }

        public override void Validate(Type root, List<string> errors)
        {
            if (string.IsNullOrEmpty(path) && !IsWholeValue(root))
            {
                errors.Add("검사할 필드가 선택되지 않았습니다.");
                return;
            }
            string error = null;
            var type = string.IsNullOrEmpty(path) ? root : FieldPaths.TypeAt(root, path, out error);
            if (type == null)
            {
                errors.Add(error);
                return;
            }
            if (!FieldOps.For(type).Contains(op))
                errors.Add($"'{PathLabel(path)}'({DataModel.FriendlyName(type)})에는 '{FieldOps.Word(op)}' 비교를 쓸 수 없습니다.");
            else if (!FieldOps.TryValidateValue(type, op, value, out error))
                errors.Add(error);
        }
    }

    [Serializable]
    [BlockInfo("&& AND", Category = "논리", Description = "안의 조건을 모두 만족해야 합니다(그리고).", Keyword = "AND")]
    public sealed class AllFilter : ValueFilter
    {
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.SerializeReference]
#endif
        [FieldLabel("조건")] public List<ValueFilter> filters = new List<ValueFilter>();

        public override IEnumerable<ValueFilter> Children => filters.Where(f => f != null);
        public override bool Test(object root) => Children.All(f => f.Test(root));
        public override string Describe() => Children.Any() ? "(" + string.Join(" && ", Children.Select(f => f.Describe())) + ")" : "true";
        public override string Explain() => "아래 조건을 모두 만족한다면";
        public override string Natural() => Children.Any() ? string.Join(" 그리고 ", Children.Select(Wrap)) : "항상";

        public override void Validate(Type root, List<string> errors)
        {
            foreach (var child in Children) child.Validate(root, errors);
        }
    }

    [Serializable]
    [BlockInfo("|| OR", Category = "논리", Description = "안의 조건 중 하나만 만족해도 됩니다(또는).", Keyword = "OR")]
    public sealed class AnyFilter : ValueFilter
    {
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.SerializeReference]
#endif
        [FieldLabel("조건")] public List<ValueFilter> filters = new List<ValueFilter>();

        public override IEnumerable<ValueFilter> Children => filters.Where(f => f != null);
        public override bool Test(object root) => Children.Any(f => f.Test(root));
        public override string Describe() => Children.Any() ? "(" + string.Join(" || ", Children.Select(f => f.Describe())) + ")" : "false";
        public override string Explain() => "아래 조건 중 하나라도 만족한다면";
        public override string Natural() => Children.Any() ? string.Join(" 또는 ", Children.Select(Wrap)) : "없음";

        public override void Validate(Type root, List<string> errors)
        {
            if (!Children.Any()) errors.Add("'|| OR'이 비어 있어 항상 거짓입니다.");
            foreach (var child in Children) child.Validate(root, errors);
        }
    }

    [Serializable]
    [BlockInfo("! NOT", Category = "논리", Description = "안의 조건이 거짓일 때 참입니다.", Keyword = "NOT")]
    public sealed class NotFilter : ValueFilter
    {
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.SerializeReference]
#endif
        [FieldLabel("조건")] public ValueFilter filter;

        public override IEnumerable<ValueFilter> Children => filter == null ? Array.Empty<ValueFilter>() : new[] { filter };
        public override bool Test(object root) => filter != null && !filter.Test(root);
        public override string Describe() => $"!({DescribeOrAll(filter)})";
        public override string Explain() => "아래 조건을 만족하지 않는다면";
        public override string Natural() => filter == null ? "아님" : $"({filter.Natural()}) 아님";

        public override void Validate(Type root, List<string> errors)
        {
            if (filter == null) errors.Add("'! NOT' 안에 조건이 없습니다.");
            filter?.Validate(root, errors);
        }
    }

    public enum ElementMode
    {
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("any")]
#endif
        Any,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("all")]
#endif
        All,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("none")]
#endif
        None,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("count >=")]
#endif
        CountAtLeast,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("count <=")]
#endif
        CountAtMost,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("count ==")]
#endif
        CountEqual,
    }

    // 목록의 각 항목에 안쪽 조건을 적용하고 몇 개가 만족하는지로 판정한다.
    // 예: 아이템 중 [태그 boss 포함 그리고 등급 3 이상]인 것이 2개 이상
    [Serializable]
    [BlockInfo("배열·리스트 항목 검사", Category = "배열·리스트", Description = "배열·리스트의 각 항목에 조건을 적용해, 만족하는 항목이 하나라도·모두·없음·N개인지 봅니다.",
               Sentence = "{path} {mode} {count}")]
    public sealed class ElementFilter : ValueFilter
    {
        [FieldPath, FieldPathScope, FieldLabel("배열·리스트")] public string path = "";
        [FieldLabel("판정")] public ElementMode mode = ElementMode.Any;
        [FieldLabel("개수")] public int count = 1;
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.SerializeReference]
#endif
        [FieldLabel("항목 조건")] public ValueFilter filter;

        public override IEnumerable<ValueFilter> Children => filter == null ? Array.Empty<ValueFilter>() : new[] { filter };

        public override bool Test(object root)
        {
            var items = FieldPaths.Resolve(root, path).SelectMany(v => FieldPaths.IsCollectionValue(v) ? FieldPaths.Items(v) : new[] { v }).ToList();
            var matched = items.Count(item => filter == null || filter.Test(item));
            switch (mode)
            {
                case ElementMode.Any: return matched > 0;
                case ElementMode.All: return matched == items.Count;
                case ElementMode.None: return matched == 0;
                case ElementMode.CountAtLeast: return matched >= count;
                case ElementMode.CountAtMost: return matched <= count;
                default: return matched == count;
            }
        }

        public override string Describe()
        {
            var inner = $"{FieldFilter.PathLabel(path)}: {DescribeOrAll(filter)}";
            switch (mode)
            {
                case ElementMode.Any: return $"any({inner})";
                case ElementMode.All: return $"all({inner})";
                case ElementMode.None: return $"none({inner})";
                case ElementMode.CountAtLeast: return $"count({inner}) >= {count}";
                case ElementMode.CountAtMost: return $"count({inner}) <= {count}";
                default: return $"count({inner}) == {count}";
            }
        }

        public override string Natural()
        {
            var what = $"{FieldFilter.PathLabel(path)} 중 [{NaturalOrAll(filter)}]인 항목이";
            switch (mode)
            {
                case ElementMode.Any: return $"{what} 하나 이상";
                case ElementMode.All: return $"{FieldFilter.PathLabel(path)}의 모든 항목이 [{NaturalOrAll(filter)}]";
                case ElementMode.None: return $"{what} 없음";
                case ElementMode.CountAtLeast: return $"{what} {count}개 이상";
                case ElementMode.CountAtMost: return $"{what} {count}개 이하";
                default: return $"{what} {count}개";
            }
        }

        public override string Explain()
        {
            var what = $"{FieldFilter.PathLabel(path)}의 항목 중 아래 조건을 만족하는 것이";
            switch (mode)
            {
                case ElementMode.Any: return $"{what} 하나라도 있다면";
                case ElementMode.All: return $"{FieldFilter.PathLabel(path)}의 모든 항목이 아래 조건을 만족한다면";
                case ElementMode.None: return $"{what} 하나도 없다면";
                case ElementMode.CountAtLeast: return $"{what} {count}개 이상이라면";
                case ElementMode.CountAtMost: return $"{what} {count}개 이하라면";
                default: return $"{what} {count}개라면";
            }
        }

        // 안쪽 조건의 뿌리 = 목록의 요소 타입.
        public static Type ElementRoot(Type root, string path)
        {
            var type = FieldPaths.TypeAt(root, path, out _);
            return FieldPaths.ElementTypeOf(type) ?? type;
        }

        public override void Validate(Type root, List<string> errors)
        {
            // 경로를 비우면 값 전체(목록 하나인 값 — 실행할 때도 그렇게 본다).
            if (string.IsNullOrEmpty(path) && FieldPaths.ElementTypeOf(root) == null)
            {
                errors.Add("검사할 배열·리스트가 선택되지 않았습니다.");
                return;
            }
            string error = null;
            var type = string.IsNullOrEmpty(path) ? root : FieldPaths.TypeAt(root, path, out error);
            if (type == null)
            {
                errors.Add(error);
                return;
            }
            if (FieldPaths.ElementTypeOf(type) == null && !path.Contains(FieldPaths.Each))
                errors.Add($"'{FieldFilter.PathLabel(path)}'는 배열·리스트가 아닙니다.");
            filter?.Validate(ElementRoot(root, path), errors);
        }
    }
}
