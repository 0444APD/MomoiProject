using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using GameSystems.Data;
using UnityEditor;
using UnityEngine;

namespace GameSystems.EditorTools
{
    public sealed class DataTypeInfo
    {
        public Type Type;
        public string DisplayName;
        public string Group;
        public int Order;
        public string IdPrefix;
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
            };
        }

        static string DefaultPrefix(Type type)
        {
            var name = Regex.Replace(type.Name, "Data$", "");
            return Regex.Replace(name, "(?<=[a-z0-9])([A-Z])", "_$1").ToLowerInvariant();
        }

        static HashSet<string> _projectAssemblies;

        // 프로젝트(Assets 폴더)에서 컴파일되는 어셈블리. 패키지·Unity 내부 타입을 걸러내는 데 쓴다.
        static HashSet<string> ProjectAssemblies =>
            _projectAssemblies ??= new HashSet<string>(
                UnityEditor.Compilation.CompilationPipeline.GetAssemblies()
                    .Where(a => a.sourceFiles.Any(f => f.StartsWith("Assets/", StringComparison.Ordinal)))
                    .Select(a => a.name));

        // GameData를 상속하지 않은 데이터용 ScriptableObject(놓침 방지 알림용).
        public static IEnumerable<Type> UnregisteredScriptableObjects()
        {
            foreach (var type in TypeCache.GetTypesDerivedFrom<ScriptableObject>())
            {
                if (type.IsAbstract || type.IsGenericTypeDefinition || typeof(GameData).IsAssignableFrom(type)) continue;
                if (typeof(EditorWindow).IsAssignableFrom(type) || typeof(UnityEditor.Editor).IsAssignableFrom(type)) continue;
                var assembly = type.Assembly.GetName().Name;
                if (assembly.StartsWith("GameSystems", StringComparison.Ordinal) || !ProjectAssemblies.Contains(assembly)) continue;
                if (type.IsDefined(typeof(HideInDatabaseAttribute), false)) continue;
                yield return type;
            }
        }

        [UnityEditor.Callbacks.DidReloadScripts]
        static void Invalidate()
        {
            _cache = null;
            _projectAssemblies = null;
        }
    }
}
