using System;
using System.Collections.Generic;
using System.Linq;

namespace GameSystems
{
    // 값 조건식: 어떤 객체(이벤트, 저장소 값, 컬렉션의 한 항목)를 뿌리로 두고 참·거짓을 판정하는 트리.
    // 필드 검사 · 모두 만족 · 하나라도 만족 · 아님 · 목록 항목 검사를 자유롭게 중첩해 어떤 식이든 만든다.
    //   예) 모두 만족
    //         ├ 등급 3 이상
    //         └ 하나라도 만족
    //              ├ 태그 boss 포함
    //              └ 목록 항목 검사: 드롭 중 [희귀도 5 이상]인 항목이 2개 이상
    // 쓰는 곳: 수치 반영 규칙의 조건(뿌리 = 이벤트), 조건 블록 "상태 값 검사"(뿌리 = 상태 값).
    [Serializable]
    public abstract class ValueFilter
    {
        public abstract bool Test(object root);
        public abstract string Describe();
        public virtual IEnumerable<ValueFilter> Children => Array.Empty<ValueFilter>();

        // root 타입 기준 검증. 문제는 errors에 문장으로.
        public abstract void Validate(Type root, List<string> errors);

        public static string DescribeOrAll(ValueFilter filter) => filter == null ? "항상" : filter.Describe();
    }

    [Serializable]
    [BlockInfo("필드 검사", Category = "필드", Description = "필드 하나를 비교합니다.", Sentence = "{path} {value} {op}")]
    public sealed class FieldFilter : ValueFilter
    {
        [FieldPath, FieldLabel("필드")] public string path = "";
        [FieldOpRef(nameof(path)), FieldLabel("비교")] public FieldOp op = FieldOp.Equal;
        [FieldValueRef(nameof(path), nameof(op)), FieldLabel("값")] public string value = "";

        public override bool Test(object root) => FieldOps.Test(root, path, op, value);

        public override string Describe() => FieldOps.Sentence(PathLabel(path), op, value);

        public static string PathLabel(string path) =>
            string.IsNullOrEmpty(path) ? "(필드 없음)" : path.Replace("[]", "의 각 항목").Replace(".", " › ");

        public override void Validate(Type root, List<string> errors)
        {
            if (string.IsNullOrEmpty(path))
            {
                errors.Add("검사할 필드가 선택되지 않았습니다.");
                return;
            }
            var type = FieldPaths.TypeAt(root, path, out var error);
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
    [BlockInfo("모두 만족", Category = "논리", Description = "안의 조건을 모두 만족해야 합니다(그리고).")]
    public sealed class AllFilter : ValueFilter
    {
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.SerializeReference]
#endif
        [FieldLabel("조건")] public List<ValueFilter> filters = new List<ValueFilter>();

        public override IEnumerable<ValueFilter> Children => filters.Where(f => f != null);
        public override bool Test(object root) => Children.All(f => f.Test(root));
        public override string Describe() => Children.Any() ? "(" + string.Join(" 그리고 ", Children.Select(f => f.Describe())) + ")" : "(비어 있음: 항상 참)";

        public override void Validate(Type root, List<string> errors)
        {
            foreach (var child in Children) child.Validate(root, errors);
        }
    }

    [Serializable]
    [BlockInfo("하나라도 만족", Category = "논리", Description = "안의 조건 중 하나만 만족해도 됩니다(또는).")]
    public sealed class AnyFilter : ValueFilter
    {
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.SerializeReference]
#endif
        [FieldLabel("조건")] public List<ValueFilter> filters = new List<ValueFilter>();

        public override IEnumerable<ValueFilter> Children => filters.Where(f => f != null);
        public override bool Test(object root) => Children.Any(f => f.Test(root));
        public override string Describe() => Children.Any() ? "(" + string.Join(" 또는 ", Children.Select(f => f.Describe())) + ")" : "(비어 있음: 항상 거짓)";

        public override void Validate(Type root, List<string> errors)
        {
            if (!Children.Any()) errors.Add("'하나라도 만족'이 비어 있어 항상 거짓입니다.");
            foreach (var child in Children) child.Validate(root, errors);
        }
    }

    [Serializable]
    [BlockInfo("아님", Category = "논리", Description = "안의 조건이 거짓일 때 참입니다.")]
    public sealed class NotFilter : ValueFilter
    {
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.SerializeReference]
#endif
        [FieldLabel("조건")] public ValueFilter filter;

        public override IEnumerable<ValueFilter> Children => filter == null ? Array.Empty<ValueFilter>() : new[] { filter };
        public override bool Test(object root) => filter == null || !filter.Test(root);
        public override string Describe() => $"아님({DescribeOrAll(filter)})";

        public override void Validate(Type root, List<string> errors)
        {
            if (filter == null) errors.Add("'아님' 안에 조건이 없습니다.");
            filter?.Validate(root, errors);
        }
    }

    public enum ElementMode
    {
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("하나라도")]
#endif
        Any,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("모두")]
#endif
        All,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("하나도 없음")]
#endif
        None,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("개 이상")]
#endif
        CountAtLeast,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("개 이하")]
#endif
        CountAtMost,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("개")]
#endif
        CountEqual,
    }

    // 목록의 각 항목에 안쪽 조건을 적용하고 몇 개가 만족하는지로 판정한다.
    // 예: 아이템 중 [태그 boss 포함 그리고 등급 3 이상]인 것이 2개 이상
    [Serializable]
    [BlockInfo("목록 항목 검사", Category = "목록", Description = "목록의 각 항목에 조건을 적용해, 만족하는 항목이 하나라도·모두·없음·N개인지 봅니다.",
               Sentence = "{path} 중 조건을 만족하는 항목이 {count} {mode}")]
    public sealed class ElementFilter : ValueFilter
    {
        [FieldPath, FieldPathScope, FieldLabel("목록")] public string path = "";
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
            var what = $"{FieldFilter.PathLabel(path)} 중 [{DescribeOrAll(filter)}]인 항목이";
            switch (mode)
            {
                case ElementMode.Any: return $"{what} 하나라도 있음";
                case ElementMode.All: return $"{FieldFilter.PathLabel(path)}의 모든 항목이 [{DescribeOrAll(filter)}]";
                case ElementMode.None: return $"{what} 없음";
                case ElementMode.CountAtLeast: return $"{what} {count}개 이상";
                case ElementMode.CountAtMost: return $"{what} {count}개 이하";
                default: return $"{what} {count}개";
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
            if (string.IsNullOrEmpty(path))
            {
                errors.Add("검사할 목록이 선택되지 않았습니다.");
                return;
            }
            var type = FieldPaths.TypeAt(root, path, out var error);
            if (type == null)
            {
                errors.Add(error);
                return;
            }
            if (FieldPaths.ElementTypeOf(type) == null && !path.Contains(FieldPaths.Each))
                errors.Add($"'{FieldFilter.PathLabel(path)}'는 목록이 아닙니다.");
            filter?.Validate(ElementRoot(root, path), errors);
        }
    }
}
