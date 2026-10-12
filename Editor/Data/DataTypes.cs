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
        // 개발자가 에디터에서 만드는 종류([Database(DeveloperAuthored = true)]) — 추가·삭제·ID 바꾸기가 열린다(EditorPolicy).
        public bool DeveloperAuthored;
        // 하나만 두는 설정 종류([Database(Single = true)]) — 없으면 에디터가 기본값으로 만든다(EditorDatabase.Upkeep).
        public bool Single;
        // 항목이 없으면 탭을 숨긴다([Database(HideWhenEmpty = true)], 'DB' 그룹의 로직 종류는 늘).
        public bool HideWhenEmpty;
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
                DeveloperAuthored = attr?.DeveloperAuthored ?? false,
                Single = attr?.Single ?? false,
                HideWhenEmpty = (attr?.HideWhenEmpty ?? false) || ((attr?.Group ?? "데이터") == "DB" && !(attr?.DeveloperAuthored ?? false)),
            };
        }

        // 선택 요소(상태 효과·패시브 효과·액티브 효과 — [Element]): 프로젝트에 그 규격(IStatusData …)을 붙인 DB 종류가 있으면 켜진 것.
        // 고르는 목록(+ 메뉴의 Block·행동 직전 이벤트·이벤트)은 꺼진 요소에 딸린 형식을 뺀다(엔진 코드는 그대로 있다).
        static HashSet<string> _elements;

        public static bool ElementActive(string name)
        {
            if (_elements == null)
            {
                _elements = new HashSet<string>(StringComparer.Ordinal);
                foreach (var info in All)
                    foreach (var face in info.Type.GetInterfaces())
                        if (face.GetCustomAttribute<ElementAttribute>(false) is ElementAttribute element) _elements.Add(element.Name);
            }
            return _elements.Contains(name);
        }

        // 고르는 목록에 보일 형식인가: 선택 요소에 딸리지 않았거나, 그 요소가 켜져 있다.
        public static bool Visible(Type type) =>
            type == null || !(type.GetCustomAttribute<ElementAttribute>(false) is ElementAttribute element) || ElementActive(element.Name);

        // 화면에 보일 종류 이름. 선택 요소의 규격(IStatusData …)이면 그것을 붙인 DB 종류의 이름, 그런 종류가 없으면 형식 이름.
        public static string Label(Type type)
        {
            if (type == null) return "";
            var info = Find(type) ?? (type.IsInterface ? All.FirstOrDefault(i => type.IsAssignableFrom(i.Type)) : null);
            return info?.DisplayName ?? type.Name;
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
