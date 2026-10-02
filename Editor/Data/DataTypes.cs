using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using GBible.Data;
using UnityEditor;
using UnityEngine;

namespace GBible.EditorTools
{
    public sealed class DataTypeInfo
    {
        public Type Type;
        public string DisplayName;
        public string Group;
        public int Order;
        public string IdPrefix;
        // 사이드바에 따로 나오지 않는다(Page가 같은 다른 종류의 탭에서 함께 다룬다).
        public bool Hidden;
        // 시트·카드 대신 쓰는 전용 화면 키(없으면 시트·카드).
        public string Page;
    }

    // 데이터베이스 등록 판별: GameData를 상속한 구체 클래스만(명시적 참여).
    public static class DataTypes
    {
        static List<DataTypeInfo> _cache;

        public static IReadOnlyList<DataTypeInfo> All
        {
            get
            {
                if (_cache != null) return _cache;
                _cache = TypeCache.GetTypesDerivedFrom<GameData>()
                    .Where(IsRegistered)
                    .Select(Describe)
                    .OrderBy(i => i.Group ?? "")
                    .ThenBy(i => i.Order)
                    .ThenBy(i => i.DisplayName)
                    .ToList();
                return _cache;
            }
        }

        public static bool IsRegistered(Type type) =>
            !type.IsAbstract && !type.IsGenericTypeDefinition &&
            !type.IsDefined(typeof(HideInDatabaseAttribute), false) &&
            !type.Assembly.GetName().Name.EndsWith(".Tests", StringComparison.Ordinal) &&
            !type.Assembly.GetName().Name.Contains(".Tests.");

        public static DataTypeInfo Find(Type type) => All.FirstOrDefault(i => i.Type == type);

        static DataTypeInfo Describe(Type type)
        {
            var attr = type.GetCustomAttribute<DatabaseAttribute>();
            return new DataTypeInfo
            {
                Type = type,
                DisplayName = attr?.DisplayName ?? ObjectNames.NicifyVariableName(Regex.Replace(type.Name, "Data$", "")),
                Group = attr?.Group ?? "데이터",
                Order = attr?.Order ?? 100,
                IdPrefix = !string.IsNullOrEmpty(attr?.IdPrefix) ? attr.IdPrefix : DefaultPrefix(type),
                Hidden = attr?.Hidden ?? false,
                Page = attr?.Page,
            };
        }

        static string DefaultPrefix(Type type)
        {
            var name = Regex.Replace(type.Name, "Data$", "");
            return IdRules.Normalize(name);
        }

        static HashSet<string> _projectAssemblies;

        // 프로젝트(Assets 폴더)에서 컴파일되는 어셈블리. 패키지·Unity 내부 타입을 걸러내는 데 쓴다.
        static HashSet<string> ProjectAssemblies =>
            _projectAssemblies ??= new HashSet<string>(
                UnityEditor.Compilation.CompilationPipeline.GetAssemblies()
                    .Where(a => a.sourceFiles.Any(f => f.StartsWith("Assets/", StringComparison.Ordinal)))
                    .Select(a => a.name));

        // GameData를 상속하지 않은 데이터용 ScriptableObject(놓침 방지 알림용). 형식 목록은 다시 컴파일할 때만 바뀌므로 한 번만 찾는다.
        static List<Type> _unregistered;

        public static IReadOnlyList<Type> UnregisteredScriptableObjects() => _unregistered ??= FindUnregistered().ToList();

        static IEnumerable<Type> FindUnregistered()
        {
            foreach (var type in TypeCache.GetTypesDerivedFrom<ScriptableObject>())
            {
                if (type.IsAbstract || type.IsGenericTypeDefinition || typeof(GameData).IsAssignableFrom(type)) continue;
                if (typeof(EditorWindow).IsAssignableFrom(type) || typeof(UnityEditor.Editor).IsAssignableFrom(type)) continue;
                var assembly = type.Assembly.GetName().Name;
                if (assembly.StartsWith("GBible", StringComparison.Ordinal) || !ProjectAssemblies.Contains(assembly)) continue;
                if (type.IsDefined(typeof(HideInDatabaseAttribute), false)) continue;
                yield return type;
            }
        }

        [UnityEditor.Callbacks.DidReloadScripts]
        static void Invalidate()
        {
            _cache = null;
            _projectAssemblies = null;
            _unregistered = null;
        }
    }
}
