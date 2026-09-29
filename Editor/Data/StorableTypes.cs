using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;

namespace GameSystems.EditorTools
{
    // 플레이어 데이터의 "값 형식" 목록: 기본 형식 + 프로젝트의 enum + 저장소에 담을 수 있는 데이터 클래스.
    // 클래스는 추가 코드 없이 목록에 나타난다(공개 클래스이고 담을 수 없는 필드가 없으면).
    public static class StorableTypes
    {
        static readonly (Type type, string label)[] Basics =
        {
            (typeof(double), "숫자 (double)"),
            (typeof(int), "정수 (int)"),
            (typeof(long), "큰 정수 (long)"),
            (typeof(float), "소수 (float)"),
            (typeof(bool), "참/거짓 (bool)"),
            (typeof(string), "글자 (string)"),
            (typeof(DateTime), "날짜·시각 (DateTime)"),
            (typeof(TimeSpan), "시간 길이 (TimeSpan)"),
        };

        static List<Type> _cache;

        public static IReadOnlyList<Type> All()
        {
            if (_cache != null) return _cache;
            var assemblies = new HashSet<string>(CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies)
                .Select(a => a.name)
                .Where(name => !name.StartsWith("Unity", StringComparison.Ordinal) &&
                               !name.StartsWith("GameSystems.", StringComparison.Ordinal)));

            var enums = TypeCache.GetTypesDerivedFrom<Enum>()
                .Where(t => t.IsVisible && assemblies.Contains(t.Assembly.GetName().Name))
                .OrderBy(t => t.Name);
            var classes = TypeCache.GetTypesWithAttribute<SerializableAttribute>()
                .Where(t => t.IsVisible && !t.IsEnum && !t.IsAbstract && !t.IsGenericTypeDefinition && !t.IsInterface &&
                            assemblies.Contains(t.Assembly.GetName().Name) &&
                            !typeof(UnityEngine.Object).IsAssignableFrom(t) && !typeof(Attribute).IsAssignableFrom(t) &&
                            !typeof(GameAction).IsAssignableFrom(t) &&
                            DataModel.Of(t).Kind == DataKind.Object && DataModel.Validate(t).Count == 0)
                .OrderBy(t => t.Name);
            _cache = Basics.Select(b => b.type).Concat(enums).Concat(classes).ToList();
            return _cache;
        }

        public static string Label(Type type)
        {
            foreach (var basic in Basics)
                if (basic.type == type) return basic.label;
            if (type.IsEnum) return type.Name + " (enum)";
            return DataModel.FriendlyName(type);
        }

        public static string GroupOf(Type type) =>
            Basics.Any(b => b.type == type) ? "기본 형식" : type.IsEnum ? "enum" : "데이터 클래스";

        [InitializeOnLoadMethod]
        static void ResetOnReload() => _cache = null;
    }
}
