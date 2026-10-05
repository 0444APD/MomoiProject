using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEditor;

namespace GBible.EditorTools
{
    // 인스턴스 편집 화면의 필드 규칙: [Hide](항상 숨김)·[ShowIf](조건부 보이기)와 기능 필드(비어 있을 수 있는 [SerializeReference]).
    // 어트리뷰트는 코드가 원본이다(AI가 코드에 쓴다).
    public static class InstanceFields
    {
        // [Hide] 여부.
        public static bool IsHidden(FieldInfo field) => field != null && field.IsDefined(typeof(HideAttribute), true);

        // 이 객체에서 이 필드를 보이는가.
        public static bool Visible(object owner, FieldInfo field)
        {
            if (field == null) return true;
            if (IsHidden(field)) return false;
            var condition = field.GetCustomAttribute<ShowIfAttribute>(true);
            if (condition == null || owner == null) return true;
            var other = FindField(owner.GetType(), condition.Field);
            if (other == null) return true;
            return condition.Matches(other.GetValue(owner));
        }

        static FieldInfo FindField(Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var found = t.GetField(name ?? "", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (found != null) return found;
            }
            return null;
        }

        // 기능 필드: [SerializeReference]가 붙은 클래스 필드(목록·조건·보상 같은 블록이 아닌 것). null이면 그 기능이 없다.
        public static bool IsFeature(FieldInfo field)
        {
            if (field == null || field.FieldType.IsValueType || field.FieldType == typeof(string)) return false;
            if (!field.IsDefined(typeof(UnityEngine.SerializeReference), true)) return false;
            if (FieldPaths.ElementTypeOf(field.FieldType) != null) return false;
            return !IsBlock(field.FieldType);
        }

        static bool IsBlock(Type type) =>
            typeof(Condition).IsAssignableFrom(type) || typeof(Reward).IsAssignableFrom(type) || typeof(ValueFilter).IsAssignableFrom(type) ||
            typeof(Effect).IsAssignableFrom(type) || typeof(TargetSelector).IsAssignableFrom(type) ||
            typeof(Trigger).IsAssignableFrom(type) || typeof(Outcome).IsAssignableFrom(type);

        public static IEnumerable<FieldInfo> Features(Type type) =>
            FieldPaths.MembersOf(type).Select(m => m.Info as FieldInfo).Where(IsFeature);

        // 기능 필드에 넣을 수 있는 형식: 선언 형식(만들 수 있으면)과 그 하위 클래스.
        public static IEnumerable<Type> Choices(Type declared)
        {
            if (CanCreate(declared)) yield return declared;
            foreach (var type in TypeCache.GetTypesDerivedFrom(declared).Where(CanCreate).OrderBy(t => t.Name, StringComparer.Ordinal))
                yield return type;
        }

        static bool CanCreate(Type type) => !type.IsAbstract && !type.IsInterface && !type.IsGenericTypeDefinition && type.GetConstructor(Type.EmptyTypes) != null;
    }
}
