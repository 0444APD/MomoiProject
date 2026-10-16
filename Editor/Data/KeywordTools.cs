using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using GBible.Data;
using UnityEditor;

namespace GBible.EditorTools
{
    // 키워드(코드가 원본)와 필드 연결을 한곳에서. 키워드 탭·클래스 상세·드롭다운·검증이 함께 쓴다.
    // 키워드 그룹·키워드는 코드([KeywordGroup] static class + const string)이고 AI가 코드에서 만든다. DB는 읽기만 한다.
    public static class KeywordTools
    {
        static EditorDatabase Db => EditorDatabase.Instance;

        public sealed class Group
        {
            public string Id;
            public string Name;
            public string Label;
            public string Parent;
            public Type Type;
            public readonly List<Keyword> Keywords = new List<Keyword>();
            public string DisplayName => string.IsNullOrEmpty(Label) ? Name : Label;
        }

        public sealed class Keyword
        {
            public string Id;
            public string Name;
            public string Group;
        }

        // 컴파일 뒤에는 코드에서 다시 모은다. 같은 키워드 ID가 두 그룹에 있으면 알린다(막지는 않는다 — 그룹 ID 순으로 앞선 그룹의 것만 쓰인다).
        [InitializeOnLoadMethod]
        static void ResetOnReload()
        {
            Keywords.Reset();
            EditorApplication.delayCall += WarnDuplicateIds;
        }

        static void WarnDuplicateIds()
        {
            foreach (var same in Groups().SelectMany(g => g.Keywords).GroupBy(k => k.Id, StringComparer.Ordinal).Where(s => s.Select(k => k.Group).Distinct().Count() > 1))
                UnityEngine.Debug.LogWarning($"[MomoiProject] 키워드 ID '{same.Key}'가 여러 그룹에 있습니다({string.Join(", ", same.Select(k => k.Group).Distinct())}). " +
                                             $"키워드 ID는 모든 그룹을 통틀어 하나여야 합니다 — 앞선 그룹({same.First().Group})의 것만 쓰입니다. 하나의 값을 바꾸세요.");
        }

        public static List<Group> Groups()
        {
            var result = new List<Group>();
            foreach (var info in Keywords.Groups)
            {
                var group = new Group { Id = info.Id, Name = info.Name, Label = info.Label, Parent = info.Parent?.Id, Type = info.Type };
                foreach (var keyword in info.Keywords) group.Keywords.Add(new Keyword { Id = keyword.Id, Name = keyword.Name, Group = info.Id });
                result.Add(group);
            }
            return result.OrderBy(g => g.Id, StringComparer.Ordinal).ToList();
        }

        public static Group FindGroup(string id) => string.IsNullOrEmpty(id) ? null : Groups().FirstOrDefault(g => g.Id == id);

        public static IEnumerable<Keyword> KeywordsOf(string group) => FindGroup(group)?.Keywords ?? Enumerable.Empty<Keyword>();

        public static IEnumerable<Keyword> AllKeywords() => Groups().SelectMany(g => g.Keywords);

        public static Keyword FindKeyword(string id) => string.IsNullOrEmpty(id) ? null : AllKeywords().FirstOrDefault(k => k.Id == id);

        // string 배열·리스트 필드면 여러 개, string 필드면 하나.
        public static bool IsMulti(FieldInfo field) => field != null && field.FieldType != typeof(string) && FieldPaths.ElementTypeOf(field.FieldType) == typeof(string);

        // 키워드를 담을 수 있는 필드: string 또는 string 배열·리스트.
        public static bool IsKeywordField(FieldInfo field) =>
            field != null && (field.FieldType == typeof(string) || FieldPaths.ElementTypeOf(field.FieldType) == typeof(string));

        // 키워드 그룹을 연결할 수 있는 필드(이미 용도가 정해진 칸 — 다른 DB 항목·이벤트·필드 경로 등 — 은 뺀다).
        public static bool CanConnect(FieldInfo field) => IsKeywordField(field) && !HasFixedPurpose(field);

        static bool HasFixedPurpose(FieldInfo field)
        {
            if (field.DeclaringType == typeof(GameData)) return true;
            return field.GetCustomAttributes(false).Any(a =>
                !(a is FieldLabelAttribute) && !(a is KeywordAttribute) && !(a is UnityEngine.SerializeField) && !(a is UnityEngine.TooltipAttribute) &&
                !(a is UnityEngine.HeaderAttribute) && !(a is UnityEngine.TextAreaAttribute) && !(a is UnityEngine.MultilineAttribute) &&
                !(a is HideAttribute) && !(a is ShowIfAttribute));
        }

        // 필드에 연결된 키워드 그룹 ID([Keyword(typeof(그룹))]). 없으면 null.
        public static string GroupOf(FieldInfo field) => field == null ? null : Keywords.GroupOf(field)?.Id;

        public static string GroupOf(Type type, FieldInfo field) => GroupOf(field);

        // 키워드를 담을 수 있는 필드 전체(데이터 클래스·DB 종류·이벤트). 상속받은 필드는 선언한 클래스에 한 번만.
        public static IEnumerable<(Type type, FieldInfo field)> Fields()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var type in ClassTypes.All())
                foreach (var field in ClassTypes.FieldsOf(type))
                {
                    if (!CanConnect(field)) continue;
                    if (!seen.Add(field.DeclaringType.FullName + "|" + field.Name)) continue;
                    yield return (field.DeclaringType, field);
                }
        }

        public static IEnumerable<(Type type, FieldInfo field)> FieldsLinkedTo(string group) =>
            Fields().Where(f => GroupOf(f.field) == group);

        // 키워드 그룹 코드가 있는 곳(관례: Assets/MomoiProject/Code/Rules/Keywords/그룹.cs — 데이터 클래스와 같은 규칙 어셈블리여야 참조된다).
        public const string Folder = ProjectFolders.Code + "/Rules/Keywords";

        // 그룹 형식이 정의된 파일(바깥 클래스 기준). 못 찾으면 null.
        public static string FileOf(Type group)
        {
            if (group == null) return null;
            var outer = group;
            while (outer.DeclaringType != null) outer = outer.DeclaringType;
            var direct = Directory.Exists(Folder) ? Path.Combine(Folder, outer.Name + ".cs").Replace('\\', '/') : null;
            if (direct != null && File.Exists(direct) && Regex.IsMatch(File.ReadAllText(direct), @"\bclass\s+" + outer.Name + @"\b")) return direct;
            foreach (var guid in AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;
                string text;
                try { text = File.ReadAllText(path); }
                catch (IOException) { continue; }
                if (text.Contains("KeywordGroup") && Regex.IsMatch(text, @"\bclass\s+" + Regex.Escape(outer.Name) + @"\b")) return path;
            }
            return null;
        }

        // ── 필드 규칙(에디터 설정: 필수·스프라이트 틀) ─────────────

        // 필드의 규칙: 있으면 그 규칙, 없으면 null.
        public static FieldRuleData RuleOf(Type type, FieldInfo field)
        {
            var name = type.FullName + "|" + field.Name;
            return Db.OfType<FieldRuleData>().FirstOrDefault(r => r.targetType + "|" + r.field == name)
                   ?? EditorDatabase.RuleFor(field);
        }

        // 필드의 규칙을 고친다(없으면 만든다). 고친 뒤 아무 뜻도 남지 않은 규칙은 지운다.
        // staged(기본): DB의 다른 변경과 함께 모아 두었다가 저장할 때 에셋에 쓴다. 아니면 바로 쓴다(배치 작업용).
        static FieldRuleData EditRule(Type type, FieldInfo field, string undoName, Action<FieldRuleData> edit, bool staged)
        {
            type = field.DeclaringType ?? type;
            var rule = RuleOf(type, field);
            bool Empty(FieldRuleData r) => r.source == FieldRuleSource.Free && !r.required && string.IsNullOrEmpty(r.spriteFrame);

            if (staged)
            {
                var staging = EditorDatabase.Staging;
                if (rule == null)
                {
                    rule = (FieldRuleData)staging.Add(typeof(FieldRuleData), UniqueRuleId(type, field));
                    rule.targetType = type.FullName;
                    rule.field = field.Name;
                    rule.source = FieldRuleSource.Free;
                }
                else rule = (FieldRuleData)staging.Work(rule);
                edit(rule);
                if (Empty(rule))
                {
                    staging.Remove(rule);
                    rule = null;
                }
                staging.Touch();
                return rule;
            }

            if (rule == null)
            {
                rule = (FieldRuleData)Db.Create(typeof(FieldRuleData), UniqueRuleId(type, field));
                rule.targetType = type.FullName;
                rule.field = field.Name;
                rule.source = FieldRuleSource.Free;
            }
            Undo.RecordObject(rule, undoName);
            edit(rule);
            if (Empty(rule))
            {
                AssetDatabase.MoveAssetToTrash(AssetDatabase.GetAssetPath(rule));
                Db.Sync();
                rule = null;
            }
            else
            {
                EditorUtility.SetDirty(rule);
                AssetDatabase.SaveAssetIfDirty(rule);
            }
            EditorDatabase.MarkDirty();
            return rule;
        }

        public static void SetRequired(Type type, FieldInfo field, bool required, bool staged = true)
        {
            if ((RuleOf(field.DeclaringType ?? type, field)?.required ?? false) == required) return;
            EditRule(type, field, "필수 여부", rule => rule.required = required, staged);
        }

        // 스프라이트 필드에 틀을 연결한다(비우면 해제). 미리보기 크기는 그 틀의 크기 가운데 하나.
        public static void SetSpriteFrame(Type type, FieldInfo field, string frame, bool staged = true)
        {
            EditRule(type, field, "스프라이트 틀 연결", rule =>
            {
                if (rule.spriteFrame != (frame ?? "")) rule.previewSize = "";
                rule.spriteFrame = frame ?? "";
            }, staged);
        }

        public static void SetPreviewSize(Type type, FieldInfo field, string size, bool staged = true)
        {
            if (RuleOf(field.DeclaringType ?? type, field) == null) return;
            EditRule(type, field, "미리보기 크기", rule => rule.previewSize = size ?? "", staged);
        }

        public static bool IsSpriteField(FieldInfo field) => field != null && field.FieldType == typeof(UnityEngine.Sprite);

        // 스프라이트 틀에 연결된 필드.
        public static IEnumerable<(Type type, FieldInfo field)> SpriteFieldsLinkedTo(string frame) =>
            Db.OfType<FieldRuleData>().Where(r => r.spriteFrame == frame && r.TargetType != null && r.Field != null).Select(r => (r.TargetType, r.Field));

        public static string FieldLabel(FieldInfo field) => field.GetCustomAttribute<FieldLabelAttribute>()?.Text ?? field.Name;

        static string UniqueRuleId(Type type, FieldInfo field)
        {
            var baseId = "rule." + IdRules.Normalize(type.Name) + "." + IdRules.Normalize(field.Name);
            var id = baseId;
            for (int i = 2; !Db.CanUseId(id, null, typeof(FieldRuleData), out _); i++) id = baseId + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return id;
        }
    }
}
