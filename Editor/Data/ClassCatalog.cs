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
    // 도감 › 클래스·이벤트 탭에 보이는 클래스 하나.
    public sealed class ClassEntry
    {
        public string FullName;
        public string Name;
        public ClassKind Kind;
        // 컴파일된 형식(적용 전인 새 클래스는 null).
        public Type Type;
        // 도감 종류·이벤트의 표시 이름.
        public string Label;
        public string Group;
        // 자동 그룹(기본·기능 이름)에 속한다: 그룹을 옮길 수 없다.
        public bool AutoGroup;
        // 고치거나 지울 수 없는 클래스(라이브러리의 기본 클래스·기능의 클래스)와 그 이유.
        public bool Locked;
        public string LockReason;
        public bool PendingNew;
        public bool PendingDelete;
    }

    public sealed class ClassFieldEntry
    {
        public string Name;
        public string TypeLabel;
        // 컴파일된 필드(적용 전에 더한 필드는 null).
        public FieldInfo Field;
        // 필드를 선언한 클래스가 이 클래스가 아니다(부모 클래스에서 물려받음).
        public string InheritedFrom;
        public bool PendingAdd;
        public bool PendingRemove;
        // 적용 전 이름 바꾸기: 코드의 지금 이름.
        public string RenamedFrom;
        // 도감에서 빼거나 이름을 바꿀 수 있다(파일의 필드 구역 안에 있고 잠기지 않았다).
        public bool Editable;
    }

    // 클래스 탭의 내용: 컴파일된 클래스(코드가 원본) + 적용 전 변경 + 그룹 설정 + 켜진 기능의 클래스.
    // 컴파일된 형식은 TypeCache에서 읽는다. TypeCache는 컴파일할 때 Unity가 만들어 두는 목록이라,
    // 프로그래머가 코드를 고쳐도 다시 컴파일된 뒤 도감을 그릴 때 한 번 읽는 것으로 끝난다(감시·주기 검사 없음).
    public static class ClassCatalog
    {
        public const string BuiltInGroup = "기본";

        const string BuiltInReason = "모든 프로젝트가 쓰는 기본 클래스라 고치거나 지울 수 없습니다.";

        static string FeatureReason(GameFeature feature) =>
            $"'{feature.Name}' 기능의 클래스라 고치거나 지울 수 없습니다. 쓰지 않으려면 도감 사이드바의 '{feature.Name}' 아래 '기능 활성화'를 눌러 기능을 끄세요.";

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
                // 각자의 도감 탭에서 관리하고, 필드의 키워드 연결은 그 탭의 '필드 연결'로 연다.
                foreach (var type in DataClasses()) result.Add(Describe(type, ClassKind.Data));
                foreach (var info in DataTypes.All)
                    if (IsDeveloperClass(info.Type))
                        result.Add(Describe(info.Type, ClassKind.Codex));
            }
            foreach (var op in ClassStaging.All.Where(o => o.op == ClassOp.Create && o.tab == tab))
            {
                if (result.Any(e => e.FullName == op.type)) continue;
                Enum.TryParse(op.kind, out ClassKind kind);
                result.Add(new ClassEntry { FullName = op.type, Name = op.ClassName, Kind = kind, Label = op.label, Group = ClassFolders.GroupOf(op.type) ?? "", PendingNew = true });
            }
            foreach (var entry in result) entry.PendingDelete = ClassStaging.IsDeleted(entry.FullName);
            return result.OrderBy(e => e.Name, StringComparer.Ordinal).ToList();
        }

        // 목록에 없는 클래스(시스템이 만든 도감 종류·기능 클래스)도 상세는 열 수 있다(도감 탭의 '필드 연결').
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

        // 프로젝트의 데이터 클래스: [Serializable]이 붙은 내 코드의 클래스(컴포넌트·행동·조건·보상 블록·이벤트·도감 종류 제외).
        static IEnumerable<Type> DataClasses() =>
            TypeCache.GetTypesWithAttribute<SerializableAttribute>()
                .Where(t => t.IsClass && !t.IsEnum && IsDeveloperClass(t) &&
                            !typeof(UnityEngine.Object).IsAssignableFrom(t) &&
                            !typeof(GameAction).IsAssignableFrom(t) && !typeof(Condition).IsAssignableFrom(t) && !typeof(Outcome).IsAssignableFrom(t) &&
                            !typeof(Trigger).IsAssignableFrom(t) && !typeof(Effect).IsAssignableFrom(t) && !typeof(TargetSelector).IsAssignableFrom(t) &&
                            !t.IsDefined(typeof(GameEventAttribute), false));

        // 라이브러리의 기본 클래스 가운데 클래스 탭에 보이는 것(너무 범용적이라 항상 있는 것): 스프라이트 규격.
        static bool IsBuiltIn(Type type) => type == typeof(SpritePresetData) || type == typeof(SpritePresetGroupData);

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
            var region = new HashSet<string>(StringComparer.Ordinal);
            if (entry.Type != null && !entry.Locked)
            {
                var file = FileOf(entry.FullName);
                if (!string.IsNullOrEmpty(file) && File.Exists(file))
                    foreach (var name in ClassCode.RegionFields(File.ReadAllText(file), entry.Name)) region.Add(name);
            }
            var ops = ClassStaging.For(entry.FullName).ToList();
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
                        Editable = !entry.Locked && field.DeclaringType == entry.Type && region.Contains(field.Name),
                    };
                    item.PendingRemove = ops.Any(o => o.op == ClassOp.RemoveField && o.field == field.Name);
                    var rename = ops.FirstOrDefault(o => o.op == ClassOp.RenameField && o.field == field.Name);
                    if (rename != null)
                    {
                        item.RenamedFrom = field.Name;
                        item.Name = rename.newName;
                    }
                    result.Add(item);
                }
            }
            foreach (var op in ops.Where(o => o.op == ClassOp.AddField))
                result.Add(new ClassFieldEntry { Name = op.field, TypeLabel = op.typeLabel, PendingAdd = true, Editable = true });
            return result;
        }

        // 화면에 보일 형식 이름. 연결(필드 규칙·[DataRef])이 있으면 그것을 덧붙인다.
        public static string TypeLabel(FieldInfo field)
        {
            var text = TypeName(field.FieldType);
            var reference = ((DataRefAttribute)Attribute.GetCustomAttribute(field, typeof(DataRefAttribute)))?.DataType;
            if (reference != null) text += $" · {DataTypes.Find(reference)?.DisplayName ?? reference.Name} 참조";
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

        // ── 새 클래스·삭제 검사 ────────────────────────────────

        public static bool CanCreate(string className, out string error)
        {
            if (!ClassCode.IsValidClassName(className, out error)) return false;
            var fullName = ClassRegistry.FullName(className);
            if (TypeResolver.Find(fullName) != null || ClassStaging.IsNew(fullName))
            {
                error = $"'{className}' 클래스가 이미 있습니다.";
                return false;
            }
            // Code 폴더 어디에든 같은 이름의 스크립트가 있으면 안 된다(그룹을 옮길 때 부딪힌다).
            var path = Directory.Exists(ClassFolders.Root)
                ? Directory.GetFiles(ClassFolders.Root, className + ".cs", SearchOption.AllDirectories).FirstOrDefault()
                : null;
            if (path != null)
            {
                error = $"'{path.Replace('\\', '/')}' 파일이 이미 있습니다.";
                return false;
            }
            return true;
        }

        // 지울 수 없는 이유(지울 수 있으면 null).
        public static string DeleteBlocker(ClassEntry entry)
        {
            if (entry.Locked) return entry.LockReason;
            if (entry.PendingNew) return null;
            var file = FileOf(entry.FullName);
            if (string.IsNullOrEmpty(file)) return "클래스 파일을 찾지 못했습니다.";
            if (!ClassCode.IsGenerated(File.ReadAllText(file)))
                return $"코드에서 직접 만든 클래스입니다(도감에서 만든 파일이 아님). 프로젝트 창에서 '{file}'을 직접 지우세요.";
            var usages = Usages(entry).ToList();
            if (usages.Count > 0)
                return $"'{entry.Name}'을(를) {usages.Count}곳에서 쓰고 있습니다. 먼저 그곳들을 바꾸세요.\n" + string.Join("\n", usages.Take(10).Select(u => "· " + u)) + (usages.Count > 10 ? "\n…" : "");
            return null;
        }

        // 이 클래스를 쓰는 곳: 다른 클래스의 필드, 플레이어 데이터의 값 형식, 템플릿, 필드 규칙, 이 종류의 도감 항목.
        public static IEnumerable<string> Usages(ClassEntry entry)
        {
            if (entry.Type == null) yield break;
            var type = entry.Type;
            foreach (var other in All(ClassRegistry.ClassTab).Concat(All(ClassRegistry.EventTab)))
            {
                if (other.Type == null || other.Type == type) continue;
                foreach (var field in ClassTypes.FieldsOf(other.Type))
                    if (Mentions(field.FieldType, type)) yield return $"클래스 {other.Name} › {field.Name}";
            }
            var db = EditorDatabase.Instance;
            foreach (var key in db.OfType<PlayerDataEntry>())
                if (key.valueType == type.FullName) yield return $"플레이어 데이터 {key.Id}";
            foreach (var template in db.OfType<TemplateData>())
                if (template.templateType == type.FullName) yield return $"템플릿 {template.Id}";
            if (typeof(GameData).IsAssignableFrom(type))
            {
                var count = db.OfType(type).Count();
                if (count > 0) yield return $"이 종류의 도감 항목 {count}개";
            }
        }

        static bool Mentions(Type fieldType, Type target)
        {
            if (fieldType == target) return true;
            if (fieldType.IsArray) return Mentions(fieldType.GetElementType(), target);
            return fieldType.IsGenericType && fieldType.GetGenericArguments().Any(a => Mentions(a, target));
        }

        // ── 필드 형식 고르기 ───────────────────────────────────

        public sealed class TypeChoice
        {
            public string Label;
            public string Group;
            public string Caption;
            // 코드에 쓸 형식 글자와 필요한 using·어트리뷰트.
            public string Code;
            public List<string> Usings = new List<string>();
            public string Attributes;
            // 리스트·배열로 만들 수 있는지.
            public bool AllowMany = true;
        }

        // '+ 필드'에서 고르는 형식. 도감 종류에는 Unity 에셋(스프라이트 등)도 나온다(저장소에 담는 데이터 클래스·이벤트에는 담을 수 없어 뺀다).
        public static List<TypeChoice> TypeChoices(ClassKind kind)
        {
            var list = new List<TypeChoice>
            {
                new TypeChoice { Label = "정수", Caption = "int", Group = "기본 형식", Code = "int" },
                new TypeChoice { Label = "소수", Caption = "float", Group = "기본 형식", Code = "float" },
                new TypeChoice { Label = "참/거짓", Caption = "bool", Group = "기본 형식", Code = "bool" },
                new TypeChoice { Label = "글자 · 키워드", Caption = "string — 만든 뒤 키워드 그룹을 연결할 수 있습니다", Group = "기본 형식", Code = "string" },
                new TypeChoice { Label = "큰 정수", Caption = "long", Group = "기본 형식", Code = "long" },
                new TypeChoice { Label = "정밀 소수", Caption = "double", Group = "기본 형식", Code = "double" },
            };
            if (kind == ClassKind.Codex)
            {
                foreach (var (label, code) in new[] { ("스프라이트", "Sprite"), ("프리팹·게임 오브젝트", "GameObject"), ("오디오 클립", "AudioClip"), ("색", "Color"), ("2D 좌표", "Vector2"), ("3D 좌표", "Vector3") })
                    list.Add(new TypeChoice { Label = label, Caption = code, Group = "Unity", Code = code, Usings = { "UnityEngine" } });
            }
            foreach (var info in DataTypes.All.Where(i => !i.Hidden || i.Type == typeof(KeywordGroupData)))
            {
                if (info.Type == typeof(KeywordData) || info.Type == typeof(KeywordGroupData) || info.Type == typeof(FieldRuleData)) continue;
                list.Add(new TypeChoice
                {
                    Label = info.DisplayName + " 항목",
                    Caption = "도감 항목을 ID로 가리킵니다",
                    Group = "도감 항목 참조",
                    Code = "string",
                    Attributes = $"[DataRef(typeof({CodeName(info.Type)}))]",
                    Usings = { "GBible.Data" },
                });
            }
            foreach (var type in TypeCache.GetTypesDerivedFrom<Enum>().Where(t => t.IsVisible && ProjectCode.IsProjectType(t)).OrderBy(t => t.Name))
                list.Add(new TypeChoice { Label = type.Name, Caption = "enum", Group = "enum", Code = CodeName(type) });
            foreach (var type in DataClasses().Concat(GameFeatures.DataClassTypes()).Where(t => !t.IsAbstract).OrderBy(t => t.Name))
                list.Add(new TypeChoice { Label = type.Name, Caption = type.Namespace, Group = "데이터 클래스", Code = CodeName(type) });
            return list;
        }

        public static string CodeName(Type type) => "global::" + type.FullName.Replace('+', '.');

        // 고른 형식과 개수(0 하나 · 1 리스트 · 2 배열)로 선언 줄과 화면용 이름을 만든다.
        // playerDataClass: 플레이어 데이터 클래스의 필드면 리스트·배열 대신 GameList(쓰는 순간 기록되는 리스트).
        public static (string declaration, string typeLabel, List<string> usings) Declare(TypeChoice choice, int many, string fieldName, bool playerDataClass = false)
        {
            var code = choice.Code;
            var label = choice.Attributes != null ? choice.Label : Regex.Replace(choice.Code, @"^global::(?:.*\.)?", "");
            var usings = new List<string>(choice.Usings);
            if (playerDataClass && many != 0)
            {
                code = $"GameList<{code}>";
                label = $"GameList<{label}>";
                usings.Add("GBible");
            }
            else if (many == 1)
            {
                code = $"List<{code}>";
                label = $"List<{label}>";
                usings.Add("System.Collections.Generic");
            }
            else if (many == 2)
            {
                code += "[]";
                label += "[]";
            }
            return (ClassCode.Declaration(code, fieldName, choice.Attributes), label, usings);
        }
    }
}
