using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GameSystems.Data;
using UnityEngine;

namespace GameSystems.EditorTools
{
    // 필드 규칙을 걸 수 있는 클래스: 데이터 클래스(저장소·템플릿에 담는 것), 도감 종류, 이벤트.
    public static class ClassTypes
    {
        public static IEnumerable<Type> All() =>
            StorableTypes.All().Where(t => !t.IsEnum && StorableTypes.GroupOf(t) == "데이터 클래스")
                .Concat(DataTypes.All.Select(i => i.Type))
                .Concat(TypeResolver.GameEventTypes())
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
            typeof(GameData).IsAssignableFrom(type) ? "도감 종류" : type.IsDefined(typeof(GameEventAttribute), false) ? "이벤트" : "데이터 클래스";

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
