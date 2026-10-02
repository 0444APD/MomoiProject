using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GBible.Data;
using UnityEngine;

namespace GBible.EditorTools
{
    // 필드 규칙을 걸 수 있는 클래스: 데이터 클래스(저장소·템플릿에 담는 것), 도감 종류, 이벤트.
    // 내 코드만 보인다(ProjectCode: 플러그인·패키지·이 라이브러리의 기본 도감 종류는 빠진다). 이벤트는 [GameEvent]를 붙인 것만.
    public static class ClassTypes
    {
        public static IEnumerable<Type> All() =>
            StorableTypes.All().Where(t => !t.IsEnum && StorableTypes.GroupOf(t) == "데이터 클래스")
                .Concat(DataTypes.All.Select(i => i.Type).Where(ProjectCode.IsDataClass))
                .Concat(GameFeatures.ClassTypes())
                .Concat(ProjectCode.Events())
                .Distinct()
                .OrderBy(GroupOf).ThenBy(t => t.Name);

        public static string Label(Type type)
        {
            var info = DataTypes.Find(type);
            if (info != null) return $"{info.DisplayName} ({type.Name})";
            if (type.IsDefined(typeof(GameEventAttribute), false)) return $"{EventBus.DisplayNameOf(type)} ({type.Name})";
            return type.Name;
        }

        public static string GroupOf(Type type) =>
            GameFeatures.Of(type) is GameFeature feature ? feature.Name : typeof(GameData).IsAssignableFrom(type) ? "도감 종류" : type.IsDefined(typeof(GameEventAttribute), false) ? "이벤트" : "데이터 클래스";

        // 규칙을 걸 수 있는 필드: 저장되는 필드(public 또는 [SerializeField], [NonSerialized] 제외).
        public static IEnumerable<FieldInfo> FieldsOf(Type type)
        {
            var result = new List<FieldInfo>();
            for (var t = type; t != null && t != typeof(object) && t != typeof(ScriptableObject) && t != typeof(MonoBehaviour); t = t.BaseType)
                result.InsertRange(0, t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(f => !f.IsInitOnly && !f.Name.Contains("<") && !f.IsDefined(typeof(NonSerializedAttribute), false) &&
                                (f.IsPublic || f.IsDefined(typeof(SerializeField), false))));
            return result;
        }
    }
}
