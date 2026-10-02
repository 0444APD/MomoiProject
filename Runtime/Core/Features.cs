using System;
using System.Collections.Generic;
using System.Linq;

namespace GBible
{
    // 기능 조립: 종류별로 상속하지 않고, 한 클래스에 기능 리스트을 붙인다.
    //   [SerializeReference] public List<ItemFeature> features = new List<ItemFeature>();
    // 한 아이템에 여러 기능을 함께 붙일 수 있고(장착 + 사용), 동작은 각 기능 클래스가 가진다.
    // 코드는 종류를 묻지 않고 기능이 있는지만 묻는다: item.features.GetFeature<Consumable>()
    // 도감 상세 창에서는 조건·보상 블록처럼 목록에 기능을 더하고 뺀다(기능 클래스에 [BlockInfo]로 이름·분류를 붙인다).
    public static class Features
    {
        public static T GetFeature<T>(this IEnumerable<object> features) where T : class =>
            features?.OfType<T>().FirstOrDefault();

        public static bool HasFeature<T>(this IEnumerable<object> features) where T : class =>
            features != null && features.OfType<T>().Any();

        public static IEnumerable<T> FeaturesOf<T>(this IEnumerable<object> features) where T : class =>
            features?.OfType<T>() ?? Enumerable.Empty<T>();

        // 규칙 검사([FeatureRule]): 빈 칸, 한 번만 붙는 기능의 중복, 함께 못 붙는 기능, 먼저 있어야 하는 기능.
        public static IEnumerable<string> Check(IEnumerable<object> features)
        {
            var list = features?.ToList() ?? new List<object>();
            if (list.Any(f => f == null)) yield return "비어 있는 기능 칸이 있습니다.";
            var present = list.Where(f => f != null).ToList();
            foreach (var group in present.GroupBy(f => f.GetType()))
            {
                var rule = RuleOf(group.Key);
                var name = NameOf(group.Key);
                if (rule.Single && group.Count() > 1) yield return $"'{name}' 기능이 {group.Count()}개 붙어 있습니다(하나만 붙습니다).";
                foreach (var other in rule.ExclusiveWith ?? Array.Empty<Type>())
                    if (present.Any(f => other.IsInstanceOfType(f)))
                        yield return $"'{name}'와(과) '{NameOf(other)}'는 함께 붙일 수 없습니다.";
                foreach (var needed in rule.Requires ?? Array.Empty<Type>())
                    if (!present.Any(f => needed.IsInstanceOfType(f)))
                        yield return $"'{name}'에는 '{NameOf(needed)}' 기능이 함께 있어야 합니다.";
            }
        }

        // 도감 검증에 올린다(ValidateData에서 부른다).
        public static void Validate(IEnumerable<object> features, IValidationContext context)
        {
            foreach (var message in Check(features)) context.Error(message);
        }

        static FeatureRuleAttribute RuleOf(Type type) =>
            (FeatureRuleAttribute)Attribute.GetCustomAttribute(type, typeof(FeatureRuleAttribute)) ?? FeatureRuleAttribute.Default;

        static string NameOf(Type type) =>
            ((BlockInfoAttribute)Attribute.GetCustomAttribute(type, typeof(BlockInfoAttribute)))?.DisplayName ?? type.Name;
    }

    // 기능 클래스의 붙이기 규칙(선택). 없으면 '한 번만 붙는다'만 적용된다.
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class FeatureRuleAttribute : Attribute
    {
        internal static readonly FeatureRuleAttribute Default = new FeatureRuleAttribute();

        // 한 목록에 하나만(기본 참). 여러 개 붙어도 되면 false.
        public bool Single { get; set; } = true;
        // 함께 붙일 수 없는 기능들.
        public Type[] ExclusiveWith { get; set; }
        // 먼저(함께) 있어야 하는 기능들.
        public Type[] Requires { get; set; }
    }
}
