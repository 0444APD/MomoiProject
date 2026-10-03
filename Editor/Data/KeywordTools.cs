using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GBible.Data;
using UnityEditor;

namespace GBible.EditorTools
{
    // 키워드(코드가 원본)와 필드 연결을 한곳에서. 키워드 탭·클래스 상세·드롭다운·검증이 함께 쓴다.
    // 키워드 그룹·키워드는 코드([KeywordGroup] static class + const string)이고, DB에서 한 변경은 저장(Ctrl+S) 때 코드에 쓰인다.
    // 이 목록은 컴파일된 코드에 저장 전 변경(ClassStaging)을 더한 모습이다.
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
            public bool Pending;
            public readonly List<Keyword> Keywords = new List<Keyword>();
            public string DisplayName => string.IsNullOrEmpty(Label) ? Name : Label;
        }

        public sealed class Keyword
        {
            public string Id;
            public string Name;
            public string Group;
            public bool Pending;
        }

        // 컴파일 뒤에는 코드에서 다시 모은다.
        [InitializeOnLoadMethod]
        static void ResetOnReload() => Keywords.Reset();

        public static List<Group> Groups()
        {
            var result = new List<Group>();
            foreach (var info in Keywords.Groups)
            {
                if (ClassStaging.IsGroupRemoved(info.Id) || (info.Parent != null && ClassStaging.IsGroupRemoved(info.Parent.Id))) continue;
                var group = new Group { Id = info.Id, Name = info.Name, Label = info.Label, Parent = info.Parent?.Id, Type = info.Type };
                foreach (var keyword in info.Keywords) group.Keywords.Add(new Keyword { Id = keyword.Id, Name = keyword.Name, Group = info.Id });
                result.Add(group);
            }
            foreach (var (id, parent, name, label) in ClassStaging.PendingGroups())
                if (result.All(g => g.Id != id))
                    result.Add(new Group { Id = id, Name = name, Label = label, Parent = string.IsNullOrEmpty(parent) ? null : parent, Pending = true });
            // 저장 전 키워드 변경.
            foreach (var op in ClassStaging.All)
            {
                var group = result.FirstOrDefault(g => g.Id == op.type);
                if (group == null) continue;
                switch (op.op)
                {
                    case ClassOp.KeywordAdd:
                        group.Keywords.Add(new Keyword { Id = op.declaration, Name = op.field, Group = group.Id, Pending = true });
                        break;
                    case ClassOp.KeywordRemove:
                        group.Keywords.RemoveAll(k => k.Name == op.field);
                        break;
                    case ClassOp.KeywordRename:
                        foreach (var keyword in group.Keywords.Where(k => k.Name == op.field)) keyword.Name = op.newName;
                        break;
                }
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

        // 필드에 연결된 키워드 그룹 ID([Keyword(typeof(그룹))], 저장 전 연결 포함). 없으면 null.
        public static string GroupOf(FieldInfo field)
        {
            if (field == null) return null;
            var owner = field.DeclaringType?.FullName;
            var pending = ClassStaging.All.LastOrDefault(o => (o.op == ClassOp.FieldKeyword || (o.op == ClassOp.FieldAttribute && o.declaration == "Keyword")) && o.type == owner && o.field == field.Name);
            if (pending != null) return pending.op == ClassOp.FieldKeyword ? pending.kind : null;
            return Keywords.GroupOf(field)?.Id;
        }

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

        // ── 코드 변경(저장 때 쓴다) ─────────────────────────────

        // 새 그룹. 돌려주는 값: 그룹 ID.
        public static string CreateGroup(string parent, string name, string label) => ClassStaging.AddKeywordGroup(parent, name, label);

        // 키워드 ID들을 그룹에 더한다(상수 이름은 ID에서 만든다). 더한 ID 목록.
        public static List<string> AddKeywords(string group, IEnumerable<string> ids)
        {
            var added = new List<string>();
            var names = new HashSet<string>(KeywordsOf(group).Select(k => k.Name), StringComparer.Ordinal);
            foreach (var id in ids)
            {
                var name = KeywordCode.ConstName(id);
                for (var i = 2; names.Contains(name); i++) name = KeywordCode.ConstName(id) + i;
                names.Add(name);
                ClassStaging.AddKeyword(group, name, id);
                added.Add(id);
            }
            return added;
        }

        // 새 키워드 ID 검사: 형식, 모든 그룹에서 겹치지 않음.
        public static string CheckNewIds(IEnumerable<string> ids)
        {
            var existing = new HashSet<string>(AllKeywords().Select(k => k.Id), StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in ids)
            {
                if (!IdRules.IsValid(id, out var error)) return $"'{id}': {error}";
                if (existing.Contains(id) || !seen.Add(id)) return $"'{id}' 키워드가 이미 있습니다(키워드 ID는 모든 그룹에서 하나뿐).";
            }
            return null;
        }

        public static void RemoveKeyword(string group, string name) => ClassStaging.RemoveKeyword(group, name);
        public static void RenameKeyword(string group, string name, string newName) => ClassStaging.RenameKeyword(group, name, newName);
        public static void RemoveGroup(string group) => ClassStaging.RemoveKeywordGroup(group);

        // 필드를 키워드 그룹에 연결한다(코드에 [Keyword(typeof(그룹))]). group이 null이면 연결을 뗀다.
        public static void Connect(Type type, FieldInfo field, string group)
        {
            var owner = field.DeclaringType ?? type;
            if (group == null)
            {
                ClassStaging.SetFieldAttribute(owner.FullName, field.Name, "Keyword", false);
                return;
            }
            var info = FindGroup(group);
            var ns = info?.Type?.Namespace ?? GroupNamespace(group);
            var shortName = ns == null ? group : group.Substring(ns.Length + 1);
            ClassStaging.SetFieldKeyword(owner.FullName, field.Name, shortName, ns, group);
        }

        static string GroupNamespace(string group)
        {
            var ns = ClassRegistry.Current.codeNamespace;
            return !string.IsNullOrEmpty(ns) && group.StartsWith(ns + ".", StringComparison.Ordinal) ? ns : null;
        }

        // ── 필드 규칙(에디터 설정: 필수·스프라이트 규격) ─────────────

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
            bool Empty(FieldRuleData r) => r.source == FieldRuleSource.Free && !r.required && string.IsNullOrEmpty(r.spriteGroup);

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

        // 스프라이트 필드에 규격 그룹을 연결한다(비우면 해제). 미리보기 규격은 그 그룹의 규격 가운데 하나.
        public static void SetSpriteGroup(Type type, FieldInfo field, string group, bool staged = true)
        {
            EditRule(type, field, "스프라이트 규격 그룹 연결", rule =>
            {
                if (rule.spriteGroup != (group ?? "")) rule.previewPreset = "";
                rule.spriteGroup = group ?? "";
            }, staged);
        }

        public static void SetPreviewPreset(Type type, FieldInfo field, string preset, bool staged = true)
        {
            if (RuleOf(field.DeclaringType ?? type, field) == null) return;
            EditRule(type, field, "미리보기 규격", rule => rule.previewPreset = preset ?? "", staged);
        }

        public static bool IsSpriteField(FieldInfo field) => field != null && field.FieldType == typeof(UnityEngine.Sprite);

        // 스프라이트 규격 그룹에 연결된 필드.
        public static IEnumerable<(Type type, FieldInfo field)> SpriteFieldsLinkedTo(string group) =>
            Db.OfType<FieldRuleData>().Where(r => r.spriteGroup == group && r.TargetType != null && r.Field != null).Select(r => (r.TargetType, r.Field));

        public static string FieldLabel(FieldInfo field) => field.GetCustomAttribute<FieldLabelAttribute>()?.Text ?? field.Name;

        static string UniqueRuleId(Type type, FieldInfo field)
        {
            var baseId = "rule." + IdRules.Normalize(type.Name) + "." + IdRules.Normalize(field.Name);
            var id = baseId;
            for (int i = 2; !Db.CanUseId(id, null, out _); i++) id = baseId + "_" + i;
            return id;
        }
    }
}
