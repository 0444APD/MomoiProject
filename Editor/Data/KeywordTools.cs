using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GameSystems.Data;
using UnityEditor;

namespace GameSystems.EditorTools
{
    // 키워드 그룹·키워드 만들기와 필드 연결(필드 규칙)을 한곳에서. 키워드 연결 창·도감 블록·드롭다운이 함께 쓴다.
    public static class KeywordTools
    {
        static EditorDatabase Db => EditorDatabase.Instance;

        public static IEnumerable<KeywordGroupData> Groups() => CodexGroups.Groups(typeof(KeywordGroupData)).Cast<KeywordGroupData>();

        public static IEnumerable<KeywordData> KeywordsOf(string group) =>
            CodexGroups.Items(typeof(KeywordData), group).Cast<KeywordData>();

        // string 목록 필드면 여러 개, string 필드면 하나.
        public static bool IsMulti(FieldInfo field) => field != null && field.FieldType != typeof(string) && FieldPaths.ElementTypeOf(field.FieldType) == typeof(string);

        // 키워드를 연결할 수 있는 필드: string 또는 string 목록.
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

        public static bool StageByDefault => CodexGroups.StageByDefault;

        public static KeywordGroupData CreateGroup(string id, bool? staged = null) =>
            (KeywordGroupData)CodexGroups.Create(typeof(KeywordGroupData), id, null, staged);

        public static KeywordData CreateKeyword(string group, string id, bool? staged = null) =>
            (KeywordData)CodexGroups.Create(typeof(KeywordData), id, group, staged);

        // 필드를 키워드 그룹에 연결한다(규칙이 있으면 고치고, 없으면 만든다). group이 ""이면 모든 키워드.
        public static FieldRuleData Connect(Type type, FieldInfo field, string group)
        {
            // 규칙은 필드를 선언한 클래스에 건다(편집 칸은 하위 클래스에서도 위로 올라가며 규칙을 찾는다).
            type = field.DeclaringType ?? type;
            var rule = RuleOf(type, field);
            if (rule == null)
            {
                var id = UniqueRuleId(type, field);
                rule = (FieldRuleData)Db.Create(typeof(FieldRuleData), id);
                rule.targetType = type.FullName;
                rule.field = field.Name;
            }
            Undo.RecordObject(rule, "키워드 연결");
            rule.source = FieldRuleSource.Keyword;
            rule.keywordGroup = group ?? "";
            EditorUtility.SetDirty(rule);
            AssetDatabase.SaveAssetIfDirty(rule);
            EditorDatabase.MarkDirty();
            return rule;
        }

        // 연결 해제: 규칙 에셋을 휴지통으로(필수·스프라이트 설정이 함께 있으면 출처만 자유 입력으로 바꾼다).
        public static void Disconnect(Type type, FieldInfo field)
        {
            var rule = RuleOf(type, field);
            if (rule == null) return;
            if (rule.required || !string.IsNullOrEmpty(rule.spriteGroup))
            {
                Undo.RecordObject(rule, "키워드 연결 해제");
                rule.source = FieldRuleSource.Free;
                rule.keywordGroup = "";
                EditorUtility.SetDirty(rule);
                AssetDatabase.SaveAssetIfDirty(rule);
            }
            else AssetDatabase.MoveAssetToTrash(AssetDatabase.GetAssetPath(rule));
            Db.Sync();
            EditorDatabase.MarkDirty();
        }

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
