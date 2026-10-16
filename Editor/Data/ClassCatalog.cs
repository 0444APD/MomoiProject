using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using GBible.Data;
using UnityEditor;
using UnityEngine;

namespace GBible.EditorTools
{
    // DB › 클래스·이벤트 탭에 보이는 클래스 하나.
    public sealed class ClassEntry
    {
        public string FullName;
        public string Name;
        public ClassKind Kind;
        // 컴파일된 형식(적용 전인 새 클래스는 null).
        public Type Type;
        // DB 종류·이벤트의 표시 이름.
        public string Label;
        public string Group;
        // 자동 그룹(기본·기능 이름)에 속한다: 그룹을 옮길 수 없다.
        public bool AutoGroup;
        // 고치거나 지울 수 없는 클래스(라이브러리의 기본 클래스·기능의 클래스)와 그 이유.
        public bool Locked;
        public string LockReason;
    }

    public sealed class ClassFieldEntry
    {
        public string Name;
        public string TypeLabel;
        // 컴파일된 필드.
        public FieldInfo Field;
        // 필드를 선언한 클래스가 이 클래스가 아니다(부모 클래스에서 물려받음).
        public string InheritedFrom;
    }

    // 클래스 탭의 내용: 컴파일된 클래스(코드가 원본 — 개발자가 선언한 것만, 시스템 · 기능의 클래스는 뺀다).
    // 컴파일된 형식은 TypeCache에서 읽는다. TypeCache는 컴파일할 때 Unity가 만들어 두는 목록이라,
    // 프로그래머가 코드를 고쳐도 다시 컴파일된 뒤 DB를 그릴 때 한 번 읽는 것으로 끝난다(감시·주기 검사 없음).
    public static class ClassCatalog
    {
        public const string BuiltInGroup = "기본";

        const string BuiltInReason = "모든 프로젝트가 쓰는 기본 클래스라 고치거나 지울 수 없습니다.";

        static string FeatureReason(GameFeature feature) =>
            $"'{feature.Name}' 기능의 클래스라 고치거나 지울 수 없습니다. 쓰지 않으려면 DB 사이드바의 '{feature.Name}' 아래 '기능 활성화'를 눌러 기능을 끄세요.";

        // ── 목록 ───────────────────────────────────────────────

        public static List<ClassEntry> All(string tab)
        {
            var result = new List<ClassEntry>();
            if (tab == ClassRegistry.EventTab)
            {
                foreach (var type in ProjectCode.Events().OrderBy(t => t.Name)) result.Add(Describe(type, ClassKind.Event));
            }
            else
            {
                // 개발자가 새로 선언한 클래스만. 시스템이 만든 클래스(스프라이트 규격·기능의 아이템·인벤토리 등)는
                // 각자의 DB 탭에서 관리하고, 필드의 키워드 연결은 그 탭의 '필드 연결'로 연다.
                foreach (var type in DataClasses()) result.Add(Describe(type, ClassKind.Data));
                foreach (var info in DataTypes.All)
                    if (IsDeveloperClass(info.Type))
                        result.Add(Describe(info.Type, ClassKind.Codex));
            }
            return result.OrderBy(e => e.Name, StringComparer.Ordinal).ToList();
        }

        // 목록에 없는 클래스(시스템이 만든 DB 종류·기능 클래스)도 상세는 열 수 있다(DB 탭의 '필드 연결').
        public static ClassEntry Find(string fullName)
        {
            var listed = All(ClassRegistry.ClassTab).Concat(All(ClassRegistry.EventTab)).FirstOrDefault(e => e.FullName == fullName);
            if (listed != null) return listed;
            var type = TypeResolver.Find(fullName);
            if (type == null) return null;
            return Describe(type, typeof(GameData).IsAssignableFrom(type) ? ClassKind.Codex : ClassKind.Data);
        }

        // 개발자가 선언한 클래스인지(시스템·선택 기능의 클래스가 아님).
        public static bool IsDeveloperClass(Type type) =>
            type != null && ProjectCode.IsDataClass(type) && GameFeatures.Of(type) == null && !IsBuiltIn(type) && !IsLibrary(type);

        static bool IsLibrary(Type type) => type.Assembly.GetName().Name.StartsWith("GBible", StringComparison.Ordinal);

        // 프로젝트의 데이터 클래스: [Serializable]이 붙은 내 코드의 클래스(컴포넌트·행동·조건·보상 블록·이벤트·DB 종류 제외).
        static IEnumerable<Type> DataClasses() =>
            TypeCache.GetTypesWithAttribute<SerializableAttribute>()
                .Where(t => t.IsClass && !t.IsEnum && IsDeveloperClass(t) &&
                            !typeof(UnityEngine.Object).IsAssignableFrom(t) &&
                            !typeof(GameAction).IsAssignableFrom(t) && !typeof(Condition).IsAssignableFrom(t) && !typeof(Outcome).IsAssignableFrom(t) &&
                            !typeof(Trigger).IsAssignableFrom(t) && !typeof(Effect).IsAssignableFrom(t) && !typeof(TargetSelector).IsAssignableFrom(t) &&
                            !t.IsDefined(typeof(GameEventAttribute), false));

        // 라이브러리의 기본 클래스(스프라이트 틀): 개발자 클래스로 세지 않는다(상세는 열린다).
        static bool IsBuiltIn(Type type) => type == typeof(SpritePresetData) || type == typeof(SpriteFrameData) || type == typeof(SpriteFrameSize);

        static ClassEntry Describe(Type type, ClassKind kind)
        {
            var entry = new ClassEntry { FullName = type.FullName, Name = type.Name, Kind = kind, Type = type };
            if (kind == ClassKind.Codex) entry.Label = DataTypes.Find(type)?.DisplayName;
            else if (kind == ClassKind.Event) entry.Label = EventBus.DisplayNameOf(type);
            var feature = GameFeatures.Of(type);
            if (feature != null)
            {
                entry.Group = feature.Name;
                entry.AutoGroup = entry.Locked = true;
                entry.LockReason = FeatureReason(feature);
            }
            else if (!ProjectCode.IsProjectType(type) || IsLibrary(type) || IsBuiltIn(type))
            {
                entry.Group = BuiltInGroup;
                entry.AutoGroup = entry.Locked = true;
                entry.LockReason = BuiltInReason;
            }
            else entry.Group = ClassFolders.GroupOf(type.FullName) ?? ClassFolders.Outside;
            return entry;
        }

        // ── 필드 ───────────────────────────────────────────────

        public static List<ClassFieldEntry> Fields(ClassEntry entry)
        {
            var result = new List<ClassFieldEntry>();
            if (entry.Type != null)
            {
                foreach (var field in ClassTypes.FieldsOf(entry.Type))
                {
                    var item = new ClassFieldEntry
                    {
                        Name = field.Name,
                        TypeLabel = TypeLabel(field),
                        Field = field,
                        InheritedFrom = field.DeclaringType != entry.Type ? field.DeclaringType.Name : null,
                    };
                    result.Add(item);
                }
            }
            return result;
        }

        // 화면에 보일 형식 이름. 연결(필드 규칙·[DataRef])이 있으면 그것을 덧붙인다.
        public static string TypeLabel(FieldInfo field)
        {
            var text = TypeName(field.FieldType);
            var reference = ((DataRefAttribute)Attribute.GetCustomAttribute(field, typeof(DataRefAttribute)))?.DataType;
            if (reference != null) text += $" · {EditorDatabase.RefKindLabel(field)} 참조";
            return text;
        }

        public static string TypeName(Type type)
        {
            if (type == typeof(int)) return "int";
            if (type == typeof(float)) return "float";
            if (type == typeof(double)) return "double";
            if (type == typeof(long)) return "long";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(string)) return "string";
            if (type.IsArray) return TypeName(type.GetElementType()) + "[]";
            if (type.IsGenericType)
            {
                var name = type.Name;
                var tick = name.IndexOf('`');
                if (tick >= 0) name = name.Substring(0, tick);
                return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(TypeName))}>";
            }
            return type.Name;
        }

        // ── 파일 찾기 ──────────────────────────────────────────

        // 클래스가 선언된 스크립트 파일 경로(없으면 null). 기억해 둔 경로 → 같은 이름의 스크립트 → 클래스 선언이 든 스크립트 순서로 찾는다.
        public static string FileOf(string fullName)
        {
            var name = fullName.Substring(fullName.LastIndexOf('.') + 1).Replace('+', '.');
            name = name.Substring(name.LastIndexOf('.') + 1);
            var remembered = ClassRegistry.PathOf(fullName);
            if (!string.IsNullOrEmpty(remembered) && File.Exists(remembered) && Declares(remembered, name)) return remembered;
            string fallback = null;
            foreach (var guid in AssetDatabase.FindAssets("t:MonoScript " + name))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;
                if (Path.GetFileNameWithoutExtension(path) == name && Declares(path, name)) return path;
            }
            var type = TypeResolver.Find(fullName);
            if (type == null) return null;
            // 파일 이름이 클래스 이름과 다른 경우(한 파일에 여러 클래스): 그 어셈블리의 스크립트에서 선언을 찾는다.
            var assembly = UnityEditor.Compilation.CompilationPipeline.GetAssemblies().FirstOrDefault(a => a.name == type.Assembly.GetName().Name);
            if (assembly == null) return null;
            foreach (var source in assembly.sourceFiles)
                if (Declares(source, name))
                {
                    fallback = source.Replace('\\', '/');
                    break;
                }
            return fallback;
        }

        static bool Declares(string path, string className)
        {
            try
            {
                return ClassCode.TryFindBody(File.ReadAllText(path), className, out _, out _);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
