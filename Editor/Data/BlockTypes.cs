using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;

namespace GameSystems.EditorTools
{
    // 조건·보상 블록 타입 목록. 블록 클래스를 추가하면 여기서 자동으로 찾는다(TypeCache).
    public static class BlockTypes
    {
        public static IEnumerable<Type> ConcreteTypes(Type baseType) =>
            TypeCache.GetTypesDerivedFrom(baseType)
                     .Where(t => !t.IsAbstract && !t.IsGenericTypeDefinition && t.GetConstructor(Type.EmptyTypes) != null)
                     .OrderBy(t => Category(t))
                     .ThenBy(t => DisplayName(t));

        public static string DisplayName(Type type)
        {
            var info = type.GetCustomAttribute<BlockInfoAttribute>();
            return info?.DisplayName ?? ObjectNames.NicifyVariableName(type.Name);
        }

        public static string Category(Type type) => type.GetCustomAttribute<BlockInfoAttribute>()?.Category ?? "기타";

        public static string Description(Type type) => type.GetCustomAttribute<BlockInfoAttribute>()?.Description;

        public static string Sentence(Type type) => type.GetCustomAttribute<BlockInfoAttribute>()?.Sentence;

        [UnityEditor.Callbacks.DidReloadScripts]
        static void ClearTypeCache() => TypeResolver.ClearCache();
    }
}
