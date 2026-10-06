using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine.UIElements;

namespace GBible.EditorTools.UI
{
    // [ShowIf] 판정 한 곳. 칸을 그리는 세 길(상세 패널의 맨 위 칸·묶음 필드·일반 목록의 줄·블록 안쪽 칸 / 조건식 블럭 / 탐색기)과 검증이 모두 이것을 쓴다.
    //   칸(SerializedProperty): 같은 객체의 조건 칸 값으로. 객체 필드(탐색기): 같은 객체의 조건 필드 값으로.
    //   조건 칸이 숨어 있으면 이 칸도 숨는다(예: 곡선 '방식'이 없음이면 '최솟값으로 자르기'가 숨고, 그에 딸린 '최솟값'도 숨는다).
    public static class ShowIfRules
    {
        const int MaxDepth = 8;

        public static ShowIfAttribute Of(FieldInfo field) => field?.GetCustomAttribute<ShowIfAttribute>(true);

        // ── 칸(SerializedProperty) ───────────────────────────────

        // 이 칸을 보이나([ShowIf]가 없거나 조건 칸을 못 찾으면 보인다). 목록의 요소는 목록 칸의 [ShowIf]를 따른다.
        public static bool Visible(SerializedProperty property)
        {
            var path = property.propertyPath;
            if (path.EndsWith("]", StringComparison.Ordinal))
            {
                var at = path.LastIndexOf(".Array.data[", StringComparison.Ordinal);
                var list = at > 0 ? property.serializedObject.FindProperty(path.Substring(0, at)) : null;
                if (list != null) property = list;
            }
            return Holds(property, PropertyUtil.GetField(property, out _), 0);
        }

        static bool Holds(SerializedProperty property, FieldInfo field, int depth)
        {
            var condition = Of(field);
            if (condition == null || string.IsNullOrEmpty(condition.Field)) return true;
            var sibling = property.serializedObject.FindProperty(FieldPathContext.Sibling(property.propertyPath, condition.Field));
            if (sibling == null) return true;
            if (!condition.Matches(ValueOf(sibling))) return false;
            return depth >= MaxDepth || Holds(sibling, PropertyUtil.GetField(sibling, out _), depth + 1);
        }

        // 조건 칸의 지금 값(ShowIfAttribute.Matches가 읽는 모양: enum은 코드 이름).
        public static object ValueOf(SerializedProperty sibling)
        {
            if (sibling == null) return null;
            switch (sibling.propertyType)
            {
                case SerializedPropertyType.Boolean: return sibling.boolValue;
                case SerializedPropertyType.Enum:
                    return sibling.enumValueIndex >= 0 && sibling.enumValueIndex < sibling.enumNames.Length ? sibling.enumNames[sibling.enumValueIndex] : null;
                case SerializedPropertyType.String: return sibling.stringValue;
                case SerializedPropertyType.Integer: return sibling.longValue;
                case SerializedPropertyType.Float: return sibling.doubleValue;
                case SerializedPropertyType.ManagedReference: return sibling.managedReferenceValue;
                // 사라진 오브젝트(Unity의 가짜 null)는 진짜 null로.
                case SerializedPropertyType.ObjectReference: return sibling.objectReferenceValue == null ? null : sibling.objectReferenceValue;
                default: return null;
            }
        }

        // ── 객체 필드(탐색기) ───────────────────────────────────

        // 이 객체에서 이 필드를 보이나: [Hide]면 숨기고, [ShowIf]는 조건대로(조건 필드가 숨어 있으면 함께 숨는다).
        public static bool Visible(object owner, FieldInfo field)
        {
            if (field == null) return true;
            if (InstanceFields.IsHidden(field)) return false;
            return Holds(owner, field, 0);
        }

        static bool Holds(object owner, FieldInfo field, int depth)
        {
            var condition = Of(field);
            if (condition == null || owner == null || string.IsNullOrEmpty(condition.Field)) return true;
            var other = PropertyUtil.FindFieldInHierarchy(owner.GetType(), condition.Field);
            if (other == null) return true;
            if (!condition.Matches(other.GetValue(owner))) return false;
            return depth >= MaxDepth || Holds(owner, other, depth + 1);
        }

        // 이 필드가 같은 클래스의 다른 필드의 [ShowIf] 스위치인가(비어 있으면 '끔'인 선택 칸 — 예: 무게를 밸런스 값에서).
        public static bool IsSwitch(FieldInfo field) =>
            field?.DeclaringType != null &&
            field.DeclaringType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Any(other => Of(other)?.Field == field.Name);
    }

    // 조건부 칸 묶음: 칸(보기 + 그 칸의 속성)을 등록해 두고, 값이 바뀌면 Refresh로 다시 판정한다.
    // 조건 칸을 바꾸는 편집기에 Wrap(onValue)를 넘기면 바꿀 때마다 다시 판정된다.
    public sealed class ShowIfViews
    {
        readonly List<(VisualElement view, SerializedProperty property)> _views = new List<(VisualElement, SerializedProperty)>();

        public bool Any => _views.Count > 0;

        // [ShowIf]가 붙은 칸만 기억한다. 지금 상태로 바로 보이거나 숨긴다.
        public void Track(VisualElement view, SerializedProperty property)
        {
            if (view == null || property == null || ShowIfRules.Of(PropertyUtil.GetField(property, out _)) == null) return;
            var copy = property.Copy();
            _views.Add((view, copy));
            Apply(view, copy);
        }

        public void Clear() => _views.Clear();

        public void Refresh()
        {
            var updated = new HashSet<SerializedObject>();
            foreach (var (view, property) in _views.ToList())
            {
                try
                {
                    var so = property.serializedObject;
                    if (so.targetObject == null) continue;
                    // 적용하지 않은 값이 있으면 다시 읽지 않는다(Update가 그 값을 버린다).
                    if (updated.Add(so) && !so.hasModifiedProperties) so.Update();
                    Apply(view, property);
                }
                catch (Exception)
                {
                    // 목록 줄이 빠지는 등 칸이 없어졌다(구조가 바뀌면 화면을 다시 그린다).
                    _views.Remove((view, property));
                }
            }
        }

        // 값이 바뀔 때마다 다시 판정하고 원래 할 일을 한다.
        public Action Wrap(Action onValue) => () =>
        {
            if (Any) Refresh();
            onValue?.Invoke();
        };

        static void Apply(VisualElement view, SerializedProperty property) =>
            view.style.display = ShowIfRules.Visible(property) ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
