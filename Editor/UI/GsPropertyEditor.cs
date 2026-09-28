using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameSystems.EditorTools.UI
{
    // 상세 패널용 필드 편집기. 단순 값은 인라인, 조건·보상은 블록 편집기, 목록은 카드 목록으로 그린다.
    // onValue: 값만 바뀜(미리보기 갱신) / onStructure: 블록 추가·삭제(패널 재구성).
    public static class GsPropertyEditor
    {
        public static VisualElement BuildProperty(SerializedProperty property, Action onValue, Action onStructure)
        {
            var field = PropertyUtil.GetField(property, out var valueType);
            var label = LabelOf(property, field);

            if (property.propertyType == SerializedPropertyType.ManagedReference)
                return Section(label, new BlockEditor(property, field?.FieldType ?? typeof(object), onValue, onStructure, null));

            if (property.isArray && property.propertyType == SerializedPropertyType.Generic)
            {
                var elementType = PropertyUtil.ElementType(field?.FieldType);
                if (PropertyUtil.IsManagedReferenceList(field))
                    return Section(label, BlockList(property, elementType, onValue, onStructure));
                return Section(label, PlainList(property, elementType, onValue, onStructure));
            }

            if (property.propertyType == SerializedPropertyType.Generic && property.hasVisibleChildren)
            {
                var group = Gs.Col();
                foreach (var child in PropertyUtil.Children(property))
                    group.Add(BuildProperty(child, onValue, onStructure));
                return Section(label, group);
            }

            return FieldRow(label, property, onValue);
        }

        // 제목 없이 내용만(상세 패널의 "헤더 · 내용" 배치에서 헤더는 패널이 그린다).
        public static VisualElement BuildContent(SerializedProperty property, Action onValue, Action onStructure)
        {
            var field = PropertyUtil.GetField(property, out _);
            if (property.propertyType == SerializedPropertyType.ManagedReference)
                return new BlockEditor(property, field?.FieldType ?? typeof(object), onValue, onStructure, null);

            if (property.isArray && property.propertyType == SerializedPropertyType.Generic)
            {
                var elementType = PropertyUtil.ElementType(field?.FieldType);
                return PropertyUtil.IsManagedReferenceList(field)
                    ? BlockList(property, elementType, onValue, onStructure)
                    : PlainList(property, elementType, onValue, onStructure);
            }

            if (property.propertyType == SerializedPropertyType.Generic && property.hasVisibleChildren)
            {
                var group = Gs.Col();
                foreach (var child in PropertyUtil.Children(property))
                    group.Add(BuildProperty(child, onValue, onStructure));
                return group;
            }

            var inline = new GsInlineProperty(property);
            inline.Changed += () => onValue?.Invoke();
            return inline;
        }

        public static string LabelOf(SerializedProperty property, FieldInfo field)
        {
            var custom = field?.GetCustomAttribute<FieldLabelAttribute>();
            return custom != null ? custom.Text : property.displayName;
        }

        public static VisualElement FieldRow(string label, SerializedProperty property, Action onValue)
        {
            var row = Gs.Box("gs-field-row");
            row.Add(Gs.Label(label, "gs-field-row__label"));
            var inline = new GsInlineProperty(property);
            inline.AddToClassList("gs-grow");
            inline.Changed += () => onValue?.Invoke();
            row.Add(inline);
            return row;
        }

        static VisualElement Section(string label, VisualElement content)
        {
            var section = Gs.Col();
            section.style.marginTop = 10;
            section.style.marginBottom = 4;
            section.Add(Gs.Label(label, "gs-caption"));
            section.Add(Gs.Space(6));
            section.Add(content);
            return section;
        }

        // ── 관리 참조 목록(보상, 복합 조건의 하위 조건) ─────────────
        public static VisualElement BlockList(SerializedProperty list, Type elementType, Action onValue, Action onStructure)
        {
            var container = Gs.Col();
            for (int i = 0; i < list.arraySize; i++)
            {
                var index = i;
                var element = list.GetArrayElementAtIndex(i);
                container.Add(new BlockEditor(element, elementType, onValue, onStructure, () =>
                {
                    list.serializedObject.Update();
                    var el = list.GetArrayElementAtIndex(index);
                    el.managedReferenceValue = null;
                    var size = list.arraySize;
                    list.DeleteArrayElementAtIndex(index);
                    if (list.arraySize == size) list.DeleteArrayElementAtIndex(index);
                    list.serializedObject.ApplyModifiedProperties();
                    onStructure?.Invoke();
                }));
            }

            var add = new GsButton($"+ {KindName(elementType)} 추가", () => ShowTypeMenu(elementType, type =>
            {
                list.serializedObject.Update();
                var index = list.arraySize;
                list.InsertArrayElementAtIndex(index);
                list.GetArrayElementAtIndex(index).managedReferenceValue = Activator.CreateInstance(type);
                list.serializedObject.ApplyModifiedProperties();
                onStructure?.Invoke();
            }), GsButtonKind.Ghost);
            add.style.alignSelf = Align.FlexStart;
            container.Add(add);
            return container;
        }

        // ── 일반 목록(통계 규칙, 태그 등) ─────────────────────────
        static VisualElement PlainList(SerializedProperty list, Type elementType, Action onValue, Action onStructure)
        {
            var container = Gs.Col();
            for (int i = 0; i < list.arraySize; i++)
            {
                var index = i;
                var element = list.GetArrayElementAtIndex(i);
                var card = Gs.Box("gs-block");
                var header = Gs.Box("gs-block__header");

                if (element.propertyType == SerializedPropertyType.Generic && element.hasVisibleChildren)
                {
                    var preview = Gs.Label(Preview(element), "gs-text-2", "gs-grow");
                    header.Add(preview);
                    header.Add(RemoveButton(() => Remove(list, index, onStructure)));
                    card.Add(header);
                    foreach (var child in PropertyUtil.Children(element))
                        card.Add(BuildProperty(child, () =>
                        {
                            preview.text = Preview(element);
                            onValue?.Invoke();
                        }, onStructure));
                }
                else
                {
                    var inline = new GsInlineProperty(element);
                    inline.AddToClassList("gs-grow");
                    inline.Changed += () => onValue?.Invoke();
                    header.Add(inline);
                    header.Add(RemoveButton(() => Remove(list, index, onStructure)));
                    header.style.marginBottom = 0;
                    card.Add(header);
                    card.style.paddingTop = 6;
                    card.style.paddingBottom = 6;
                }
                container.Add(card);
            }

            var add = new GsButton("+ 추가", () =>
            {
                list.serializedObject.Update();
                var index = list.arraySize;
                list.InsertArrayElementAtIndex(index);
                var element = list.GetArrayElementAtIndex(index);
                // 이전 항목 값이 복사되지 않게 기본값으로 채운다.
                if (element.propertyType == SerializedPropertyType.Generic && elementType != null && elementType.GetConstructor(Type.EmptyTypes) != null)
                    element.boxedValue = Activator.CreateInstance(elementType);
                else if (element.propertyType == SerializedPropertyType.String)
                    element.stringValue = "";
                else if (element.propertyType == SerializedPropertyType.ObjectReference)
                    element.objectReferenceValue = null;
                list.serializedObject.ApplyModifiedProperties();
                onStructure?.Invoke();
            }, GsButtonKind.Ghost);
            add.style.alignSelf = Align.FlexStart;
            container.Add(add);
            return container;
        }

        static void Remove(SerializedProperty list, int index, Action onStructure)
        {
            list.serializedObject.Update();
            list.DeleteArrayElementAtIndex(index);
            list.serializedObject.ApplyModifiedProperties();
            onStructure?.Invoke();
        }

        static VisualElement RemoveButton(Action onClick)
        {
            var button = new GsButton("✕", onClick, GsButtonKind.Ghost);
            button.AddToClassList("gs-block__remove");
            button.tooltip = "삭제";
            return button;
        }

        // 요소가 스스로 문장을 만들 수 있으면(StatRule 등) 그 문장을 쓴다.
        static string Preview(SerializedProperty element)
        {
            element.serializedObject.Update();
            var value = PropertyUtil.GetBoxed(element);
            switch (value)
            {
                case StatRule rule: return rule.Describe("이 통계");
                case null: return "";
            }
            return string.Join("  ·  ", PropertyUtil.Children(element).Select(PropertyUtil.Summary).Where(s => !string.IsNullOrEmpty(s)));
        }

        public static string KindName(Type baseType)
        {
            if (baseType == typeof(Condition)) return "조건";
            if (baseType == typeof(Reward)) return "보상";
            return "항목";
        }

        public static void ShowTypeMenu(Type baseType, Action<Type> onPick)
        {
            var menu = new GenericMenu();
            foreach (var type in BlockTypes.ConcreteTypes(baseType))
            {
                var picked = type;
                menu.AddItem(new GUIContent($"{BlockTypes.Category(type)}/{BlockTypes.DisplayName(type)}"), false, () => onPick(picked));
            }
            if (menu.GetItemCount() == 0) menu.AddDisabledItem(new GUIContent("사용할 수 있는 블록이 없습니다"));
            menu.ShowAsContext();
        }
    }

    // 조건·보상 블록 하나. 비어 있으면 "선택" 버튼, 차 있으면 카드 + 내부 필드.
    public sealed class BlockEditor : VisualElement
    {
        public BlockEditor(SerializedProperty property, Type baseType, Action onValue, Action onStructure, Action onRemove)
        {
            var value = property.managedReferenceValue;
            if (value == null)
            {
                var pick = new GsButton($"+ {GsPropertyEditor.KindName(baseType)} 선택", () => GsPropertyEditor.ShowTypeMenu(baseType, type =>
                {
                    property.serializedObject.Update();
                    property.managedReferenceValue = Activator.CreateInstance(type);
                    property.serializedObject.ApplyModifiedProperties();
                    onStructure?.Invoke();
                }), GsButtonKind.Ghost);
                pick.style.alignSelf = Align.FlexStart;
                Add(pick);
                return;
            }

            var type = value.GetType();
            AddToClassList("gs-block");
            tooltip = BlockTypes.Description(type) ?? "";

            var header = Gs.Box("gs-block__header");
            header.Add(Gs.Label(BlockTypes.Category(type), "gs-caption"));
            header.Add(Gs.HSpace(8));
            header.Add(Gs.Label(BlockTypes.DisplayName(type), "gs-heading"));
            header.Add(Gs.Spacer());

            var swap = new GsButton("바꾸기", () => GsPropertyEditor.ShowTypeMenu(baseType, newType =>
            {
                property.serializedObject.Update();
                property.managedReferenceValue = Activator.CreateInstance(newType);
                property.serializedObject.ApplyModifiedProperties();
                onStructure?.Invoke();
            }), GsButtonKind.Ghost);
            swap.AddToClassList("gs-block__remove");
            header.Add(swap);

            var remove = new GsButton("✕", () =>
            {
                if (onRemove != null)
                {
                    onRemove();
                    return;
                }
                property.serializedObject.Update();
                property.managedReferenceValue = null;
                property.serializedObject.ApplyModifiedProperties();
                onStructure?.Invoke();
            }, GsButtonKind.Ghost);
            remove.AddToClassList("gs-block__remove");
            remove.tooltip = "삭제";
            header.Add(remove);
            Add(header);

            foreach (var child in PropertyUtil.Children(property))
            {
                var element = GsPropertyEditor.BuildProperty(child, onValue, onStructure);
                // 복합 조건의 하위 조건은 들여쓴 줄로 묶는다.
                if (child.isArray || child.propertyType == SerializedPropertyType.ManagedReference)
                {
                    var nested = Gs.Box("gs-block__children");
                    nested.Add(element);
                    Add(nested);
                }
                else
                {
                    Add(element);
                }
            }
        }
    }
}
