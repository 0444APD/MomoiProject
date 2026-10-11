using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using GBible.EditorTools.UI;
using UnityEditor;

namespace GBible.EditorTools
{
    public sealed class FieldChange
    {
        public string Path;
        public string Label;
        public string Old;
        public string New;
        // 오브젝트 참조(스프라이트 등)면 확인 창에서 전후 이미지를 보여준다.
        public UnityEngine.Object OldObject;
        public UnityEngine.Object NewObject;
    }

    // 원본과 편집 중 사본을 최상위 필드 단위로 비교한다(조건 블록처럼 안쪽이 바뀐 것도 잡는다).
    // hidden: 화면에 보이지 않는 칸([HideInInspector] — 프로젝트 DB 종류의 숨은 칸 등)까지 본다. 적용할 변경을 고를 때 쓰고,
    // 화면(상세 패널)에 보일 변경 목록은 보이는 칸만 본다.
    public static class DraftDiff
    {
        public static List<FieldChange> Compute(SerializedObject original, SerializedObject draft, ICollection<string> exclude = null, bool hidden = false)
        {
            original.Update();
            draft.Update();
            var changes = new List<FieldChange>();
            foreach (var property in hidden ? AllTopLevel(draft) : PropertyUtil.TopLevel(draft))
            {
                if (exclude != null && exclude.Contains(property.propertyPath)) continue;
                var before = original.FindProperty(property.propertyPath);
                if (before != null && Fingerprint(before) == Fingerprint(property)) continue;
                var field = PropertyUtil.GetField(property, out _);
                changes.Add(new FieldChange
                {
                    Path = property.propertyPath,
                    Label = GsPropertyEditor.LabelOf(property, field),
                    Old = before == null ? "(없음)" : Text(before),
                    New = Text(property),
                    OldObject = property.propertyType == SerializedPropertyType.ObjectReference ? before?.objectReferenceValue : null,
                    NewObject = property.propertyType == SerializedPropertyType.ObjectReference ? property.objectReferenceValue : null,
                });
            }
            return changes;
        }

        // 값이 다른 최상위 칸의 경로만(사람이 읽는 글 없이 — 상세 패널이 옮길 칸·맞출 칸을 고를 때). 같은 형식끼리 비교한다.
        public static List<string> ChangedPaths(SerializedObject original, SerializedObject draft, bool hidden)
        {
            original.Update();
            draft.Update();
            var paths = new List<string>();
            foreach (var property in hidden ? AllTopLevel(draft) : PropertyUtil.TopLevel(draft))
            {
                var before = original.FindProperty(property.propertyPath);
                if (before == null || Fingerprint(before) != Fingerprint(property)) paths.Add(property.propertyPath);
            }
            return paths;
        }

        // 숨은 칸까지 모든 최상위 직렬화 칸. Unity 내부 칸(m_ObjectHideFlags·m_Name·m_Script …)은 뺀다(사본은 숨김 플래그가 달라 늘 다르다).
        static IEnumerable<SerializedProperty> AllTopLevel(SerializedObject so)
        {
            var iterator = so.GetIterator();
            if (!iterator.Next(true)) yield break;
            do
            {
                if (iterator.name.StartsWith("m_", System.StringComparison.Ordinal)) continue;
                yield return iterator.Copy();
            }
            while (iterator.Next(false));
        }

        // 사람이 읽는 값: ID는 이름으로, 조건·보상은 문장으로, 목록은 요소를 이어서.
        public static string Text(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.ManagedReference:
                    switch (property.managedReferenceValue)
                    {
                        case null: return "(없음)";
                        case Condition condition: return condition.Describe(EditorDatabase.Instance);
                        case Outcome outcome: return outcome.Natural(EditorDatabase.Instance);
                        case Trigger trigger: return trigger.Natural(EditorDatabase.Instance);
                        default: return BlockTypes.DisplayName(property.managedReferenceValue.GetType());
                    }
            }

            if (property.isArray && property.propertyType == SerializedPropertyType.Generic)
            {
                if (property.arraySize == 0) return "(비어 있음)";
                var items = new List<string>();
                for (int i = 0; i < property.arraySize && i < 6; i++) items.Add(Text(property.GetArrayElementAtIndex(i)));
                var text = string.Join(", ", items);
                return property.arraySize > 6 ? $"{text} 외 {property.arraySize - 6}개" : text;
            }

            if (property.propertyType == SerializedPropertyType.Generic && property.hasVisibleChildren)
            {
                if (PropertyUtil.GetBoxed(property) is StatRule rule) return rule.Describe("이 로그");
                if (PropertyUtil.GetBoxed(property) is TriggerRule triggerRule) return triggerRule.Natural(EditorDatabase.Instance);
                return string.Join(" · ", PropertyUtil.Children(property).Select(Text).Where(t => !string.IsNullOrEmpty(t)));
            }

            var (value, empty, _) = GsInlineProperty.Describe(property);
            return empty ? $"({value})" : value;
        }

        // 속성과 그 안쪽 전체(관리 참조 포함)의 값을 한 줄로 요약해 비교에 쓴다.
        static string Fingerprint(SerializedProperty property)
        {
            var builder = new StringBuilder();
            var iterator = property.Copy();
            var end = property.GetEndProperty();
            var prefix = property.propertyPath.Length;
            var inside = property.propertyPath + ".";
            var first = true;
            do
            {
                // 이 칸 안쪽만(빈 목록 다음의 숨은 칸 등 다음 칸의 내용이 섞이지 않게). 깊이로 가리지 않는다 —
                // Unity 6.6은 목록 칸의 안쪽(`칸.Array`)을 바깥 칸과 같은 깊이로 내서, 깊이로 끊으면 목록의 바뀐 내용을 하나도 보지 못한다.
                if (!first && !iterator.propertyPath.StartsWith(inside, System.StringComparison.Ordinal)) break;
                first = false;
                builder.Append(iterator.propertyPath.Substring(System.Math.Min(prefix, iterator.propertyPath.Length))).Append('=');
                switch (iterator.propertyType)
                {
                    case SerializedPropertyType.String: builder.Append(iterator.stringValue); break;
                    case SerializedPropertyType.Integer: builder.Append(iterator.longValue); break;
                    case SerializedPropertyType.Boolean: builder.Append(iterator.boolValue); break;
                    case SerializedPropertyType.Float: builder.Append(iterator.doubleValue.ToString("R", CultureInfo.InvariantCulture)); break;
                    case SerializedPropertyType.Enum: builder.Append(iterator.enumValueIndex); break;
                    // 인스턴스 ID API는 6.6에서 폐기됨 → 참조 대상의 해시(같은 오브젝트면 같다)로 비교한다.
                    case SerializedPropertyType.ObjectReference: builder.Append(iterator.objectReferenceValue != null ? iterator.objectReferenceValue.GetHashCode() : 0); break;
                    case SerializedPropertyType.Color: builder.Append(iterator.colorValue); break;
                    case SerializedPropertyType.ArraySize: builder.Append(iterator.intValue); break;
                    case SerializedPropertyType.ManagedReference: builder.Append(iterator.managedReferenceFullTypename); break;
                }
                builder.Append(';');
            }
            while (iterator.Next(true) && !SerializedProperty.EqualContents(iterator, end));
            return builder.ToString();
        }
    }
}
