using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;

namespace GBible.EditorTools
{
    // 조건·보상 블록 타입 목록. 블록 클래스를 추가하면 여기서 자동으로 찾는다(TypeCache).
    public static class BlockTypes
    {
        // 고르는 목록(+ 메뉴)에 보일 Block: 프로젝트에 없는 선택 요소(상태 효과 …)의 Block은 뺀다([Element]).
        public static IEnumerable<Type> ConcreteTypes(Type baseType) => AllConcreteTypes(baseType).Where(DataTypes.Visible);

        // 모든 Block(선택 요소의 것까지 — 계약 검사용).
        public static IEnumerable<Type> AllConcreteTypes(Type baseType) =>
            TypeCache.GetTypesDerivedFrom(baseType)
                     .Where(t => !t.IsAbstract && !t.IsGenericTypeDefinition && t.GetConstructor(Type.EmptyTypes) != null && !IsTestAssembly(t))
                     .OrderBy(t => Category(t))
                     .ThenBy(t => DisplayName(t));

        // 테스트가 만든 Block(예제용)은 에디터 메뉴에 내보이지 않는다.
        static bool IsTestAssembly(Type type)
        {
            var name = type.Assembly.GetName().Name;
            return name.EndsWith(".Tests", StringComparison.Ordinal) || name.Contains(".Tests.");
        }

        public static string DisplayName(Type type)
        {
            var info = type.GetCustomAttribute<BlockInfoAttribute>();
            return info?.DisplayName ?? ObjectNames.NicifyVariableName(type.Name);
        }

        public static string Category(Type type) => type.GetCustomAttribute<BlockInfoAttribute>()?.Category ?? "기타";

        public static string Description(Type type) => type.GetCustomAttribute<BlockInfoAttribute>()?.Description;

        public static string Sentence(Type type) => type.GetCustomAttribute<BlockInfoAttribute>()?.Sentence;
        public static string Keyword(Type type) => type.GetCustomAttribute<BlockInfoAttribute>()?.Keyword;

        [UnityEditor.Callbacks.DidReloadScripts]
        static void ClearTypeCache() => TypeResolver.ClearCache();
    }
}
