using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GBible.Data;
using UnityEditor;

namespace GBible.EditorTools
{
    // 키워드 그룹·키워드 만들기와 필드 연결(필드 규칙)을 한곳에서. 키워드 연결 창·도감 블록·드롭다운이 함께 쓴다.
    public static class KeywordTools
    {
        static EditorDatabase Db => EditorDatabase.Instance;

        public static IEnumerable<KeywordGroupData> Groups() => CodexGroups.Groups(typeof(KeywordGroupData)).Cast<KeywordGroupData>();

        public static IEnumerable<KeywordData> KeywordsOf(string group) =>
            CodexGroups.Items(typeof(KeywordData), group).Cast<KeywordData>();

        // string 배열·리스트 필드면 여러 개, string 필드면 하나.
        public static bool IsMulti(FieldInfo field) => field != null && field.FieldType != typeof(string) && FieldPaths.ElementTypeOf(field.FieldType) == typeof(string);

        // 키워드를 연결할 수 있는 필드: string 또는 string 배열·리스트.
        public static bool IsKeywordField(FieldInfo field) =>
            field != null && (field.FieldType == typeof(string) || FieldPaths.ElementTypeOf(field.FieldType) == typeof(string));

        // 키워드를 담을 수 있는 필드 전체(데이터 클래스·도감 종류·이벤트). 도감 종류의 id·이름·[DataRef] 등 이미 용도가 정해진 칸은 뺀다.
        // 상속받은 필드는 선언한 클래스에 한 번만 나온다(규칙도 선언한 클래스 기준이라 모든 하위 클래스에 적용된다).
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

        // 키워드 연결 창에 나오는(연결할 수 있는) 필드인지.
        public static bool CanConnect(FieldInfo field) => IsKeywordField(field) && !HasFixedPurpose(field);

        static bool HasFixedPurpose(FieldInfo field)
        {
            if (field.DeclaringType == typeof(GameData)) return true;
            // 코드에서 이미 용도를 정한 칸(다른 도감 항목·이벤트·클래스·필드 경로 등)은 연결 대상이 아니다.
            return field.GetCustomAttributes(false).Any(a =>
                !(a is FieldLabelAttribute) && !(a is UnityEngine.SerializeField) && !(a is UnityEngine.TooltipAttribute) &&
                !(a is UnityEngine.HeaderAttribute) && !(a is UnityEngine.TextAreaAttribute) && !(a is UnityEngine.MultilineAttribute) &&
                !(a is DataRefAttribute d && d.DataType == typeof(KeywordData)));
        }

        // 필드의 현재 연결: 규칙이 있으면 그 규칙, 없으면 null.
        public static FieldRuleData RuleOf(Type type, FieldInfo field)
        {
            var name = type.FullName + "|" + field.Name;
            return Db.OfType<FieldRuleData>().FirstOrDefault(r => r.targetType + "|" + r.field == name)
                   ?? EditorDatabase.RuleFor(field);
        }

        // 필드에 연결된 키워드 그룹 ID. 키워드가 아니면 null, 그룹 없이(모든 키워드) 연결됐으면 "".
        public static string GroupOf(Type type, FieldInfo field)
        {
            var rule = RuleOf(type, field);
            if (rule != null) return rule.source == FieldRuleSource.Keyword ? rule.keywordGroup ?? "" : null;
            return EditorDatabase.RefTypeOf(field) == typeof(KeywordData) ? "" : null;
        }

        public static IEnumerable<(Type type, FieldInfo field)> FieldsLinkedTo(string group) =>
            Fields().Where(f => GroupOf(f.type, f.field) == group);

        public static KeywordGroupData CreateGroup(string id, bool? staged = null) =>
            (KeywordGroupData)CodexGroups.Create(typeof(KeywordGroupData), id, null, staged);

        public static KeywordData CreateKeyword(string group, string id, bool? staged = null) =>
            (KeywordData)CodexGroups.Create(typeof(KeywordData), id, group, staged);

        // 필드의 규칙을 고친다(없으면 만든다). 고친 뒤 아무 뜻도 남지 않은 규칙은 지운다.
        // staged(기본): 도감의 다른 변경과 함께 모아 두었다가 저장할 때 에셋에 쓴다. 도감의 모든 화면은 고친 즉시 새 연결대로 보인다.
        // staged가 아니면 바로 에셋에 쓴다(샘플 데이터 만들기·배치 작업용).
        static FieldRuleData EditRule(Type type, FieldInfo field, string undoName, Action<FieldRuleData> edit, bool staged)
        {
            // 규칙은 필드를 선언한 클래스에 건다(편집 칸은 하위 클래스에서도 위로 올라가며 규칙을 찾는다).
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

        // 필드를 키워드 그룹에 연결한다(규칙이 있으면 고치고, 없으면 만든다). group이 ""이면 모든 키워드.
        public static FieldRuleData Connect(Type type, FieldInfo field, string group, bool staged = true) =>
            EditRule(type, field, "키워드 연결", rule =>
            {
                rule.source = FieldRuleSource.Keyword;
                rule.keywordGroup = group ?? "";
            }, staged);

        // 연결 해제(필수·스프라이트 설정이 함께 있으면 규칙은 남기고 출처만 자유 입력으로 바꾼다).
        public static void Disconnect(Type type, FieldInfo field, bool staged = true)
        {
            if (RuleOf(field.DeclaringType ?? type, field) == null) return;
            EditRule(type, field, "키워드 연결 해제", rule =>
            {
                rule.source = FieldRuleSource.Free;
                rule.keywordGroup = "";
            }, staged);
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
