using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GBible.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GBible.EditorTools.UI
{
    // 상세 패널용 필드 편집기. 단순 값은 인라인, 조건·보상은 블록 편집기, 목록은 카드 목록으로 그린다.
    // onValue: 값만 바뀜(미리보기 갱신) / onStructure: 블록 추가·삭제(패널 재구성).
    public static class GsPropertyEditor
    {
        public static VisualElement BuildProperty(SerializedProperty property, Action onValue, Action onStructure)
        {
            var field = PropertyUtil.GetField(property, out var valueType);
            var label = LabelOf(property, field);

            // 조건식(조건·값 조건): 블럭 · 논리 연산자 · 괄호로 한 줄 식을 만든다.
            if (property.propertyType == SerializedPropertyType.ManagedReference && GsExpressionEditor.Handles(field?.FieldType))
                return Section(label, new GsExpressionEditor(property, field.FieldType, onValue, onStructure));

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
            if (property.propertyType == SerializedPropertyType.ManagedReference && GsExpressionEditor.Handles(field?.FieldType))
                return new GsExpressionEditor(property, field.FieldType, onValue, onStructure);
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

        // 칸 이름표 = 코드의 변수 이름(항목·필드는 코드 이름으로 식별한다). 목록 요소는 [번호].
        public static string LabelOf(SerializedProperty property, FieldInfo field)
        {
            if (property.propertyPath.EndsWith("]", StringComparison.Ordinal))
            {
                var open = property.propertyPath.LastIndexOf('[');
                return open >= 0 ? property.propertyPath.Substring(open) : property.displayName;
            }
            return field?.Name ?? property.name;
        }

        // 마우스를 올리면 보일 설명: [FieldLabel]·[Tooltip] 글자(있으면).
        public static string HintOf(MemberInfo member)
        {
            if (member == null) return null;
            var label = member.GetCustomAttribute<FieldLabelAttribute>()?.Text;
            var tip = member.GetCustomAttribute<TooltipAttribute>()?.tooltip;
            if (label == null) return tip;
            return tip == null ? label : label + "\n" + tip;
        }

        public static VisualElement FieldRow(string label, SerializedProperty property, Action onValue)
        {
            var inline = new GsInlineProperty(property) { Multiline = property.propertyType == SerializedPropertyType.String };
            inline.Changed += () => onValue?.Invoke();
            var box = GsField.For(label, inline);
            box.tooltip = HintOf(PropertyUtil.GetField(property, out _));
            return box;
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
            // 보상 목록: '+ 보상 추가' → 플레이어 데이터 필드 고르기 → 필드 형식에 맞는 값 칸(주는 구문 공통 규칙).
            if (elementType == typeof(Reward) || elementType == typeof(Outcome)) return RewardListEditor.Build(list, elementType, onValue, onStructure);
            var container = Gs.Col();
            for (int i = 0; i < list.arraySize; i++)
            {
                var index = i;
                var element = list.GetArrayElementAtIndex(i);
                var count = list.arraySize;
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
                },
                onMove: delta =>
                {
                    var target = index + delta;
                    if (target < 0 || target >= count) return;
                    list.serializedObject.Update();
                    list.MoveArrayElement(index, target);
                    list.serializedObject.ApplyModifiedProperties();
                    onStructure?.Invoke();
                },
                canMoveUp: index > 0,
                canMoveDown: index < count - 1,
                onDuplicate: () =>
                {
                    list.serializedObject.Update();
                    var source = list.GetArrayElementAtIndex(index).managedReferenceValue;
                    if (source == null) return;
                    // 같은 객체를 두 번 가리키지 않게 깊은 복사본을 넣는다(하위 조건까지).
                    var copy = DataModel.Clone(source, null);
                    list.InsertArrayElementAtIndex(index + 1);
                    list.GetArrayElementAtIndex(index + 1).managedReferenceValue = copy;
                    list.serializedObject.ApplyModifiedProperties();
                    onStructure?.Invoke();
                }));
            }

            GsButton add = null;
            add = new GsButton($"+ {KindName(elementType)} 추가", () => ShowTypeMenu(add, elementType, type =>
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

        // ── 키워드 목록: 칩으로 보여 주고 여러 개 고른다 ─────────────
        // 칩을 누르면 빠지고, '+ 추가'로 연결된 그룹의 키워드를 더한다(그룹이 없으면 모든 키워드).
        static VisualElement KeywordChips(SerializedProperty list, FieldInfo field, Action onStructure)
        {
            var db = EditorDatabase.Instance;
            var row = Gs.Row("gs-wrap");
            var current = new List<string>();
            for (int i = 0; i < list.arraySize; i++) current.Add(list.GetArrayElementAtIndex(i).stringValue);
            for (int i = 0; i < current.Count; i++)
            {
                var index = i;
                var target = db.FindById(current[i]);
                var chip = new GsBadge((target != null ? target.DisplayName : $"없는 키워드: {current[i]}") + "  ✕", target != null ? Gs.PaletteFor("kw:" + (target as KeywordData)?.group) : Gs.Danger);
                chip.tooltip = $"{current[i]}\n눌러서 빼기";
                chip.style.marginRight = 6;
                chip.style.marginBottom = 6;
                chip.RegisterCallback<ClickEvent>(_ => Remove(list, index, onStructure));
                row.Add(chip);
            }

            void Add(string id)
            {
                list.serializedObject.Update();
                var at = list.arraySize;
                list.InsertArrayElementAtIndex(at);
                list.GetArrayElementAtIndex(at).stringValue = id;
                list.serializedObject.ApplyModifiedProperties();
                onStructure?.Invoke();
            }

            GsButton add = null;
            add = new GsButton("+ 추가", () =>
            {
                var items = db.RefCandidates(field).Where(k => !current.Contains(k.Id))
                    .OrderBy(k => db.GroupLabel(k) ?? "").ThenBy(k => CodexGroups.OrderOf(k)).ThenBy(k => k.Id)
                    .Select(k =>
                    {
                        var id = k.Id;
                        return new GsDropdownItem(id, () => Add(id)) { Group = db.GroupLabel(k) };
                    }).ToList();
                if (items.Count == 0) items.Add(new GsDropdownItem("더 고를 키워드가 없습니다") { Disabled = true });
                items.AddRange(KeywordMenuExtras(add, field, ids => { foreach (var id in ids) Add(id); }));
                GsDropdown.Show(add, items, placeholder: "키워드 검색");
            }, GsButtonKind.Ghost);
            add.style.marginBottom = 6;
            row.Add(add);
            return row;
        }

        // 키워드 드롭다운 끝에 붙는 항목: 새 키워드 만들기, 클래스 상세에서 연결 바꾸기.
        public static IEnumerable<GsDropdownItem> KeywordMenuExtras(VisualElement anchor, FieldInfo field, Action<List<string>> created)
        {
            var group = EditorDatabase.RuleFor(field)?.keywordGroup;
            var host = anchor.panel?.visualTree.Q(className: "gs-root") ?? anchor;
            if (!string.IsNullOrEmpty(group))
                yield return new GsDropdownItem("+ 새 키워드", () => KeywordDialogs.AddKeywords(host, group, created))
                    { Group = "관리", Detail = $"'{EditorDatabase.Instance.FindById(group)?.DisplayName ?? group}' 그룹에 추가하고 바로 고릅니다" };
            if (field != null)
                yield return new GsDropdownItem("연결 바꾸기(클래스 상세)", () => ClassDetail.Open(field.DeclaringType, field.Name))
                    { Group = "관리", Detail = "이 필드가 쓸 키워드 그룹을 바꾸거나 새 그룹을 만듭니다" };
        }

        // ── 일반 목록(수치 규칙 등) ─────────────────────────────
        static VisualElement PlainList(SerializedProperty list, Type elementType, Action onValue, Action onStructure)
        {
            var listField = PropertyUtil.GetField(list, out _);
            if (EditorDatabase.RefTypeOf(listField) == typeof(KeywordData)) return KeywordChips(list, listField, onStructure);
            var container = Gs.Col();
            for (int i = 0; i < list.arraySize; i++)
            {
                var index = i;
                var element = list.GetArrayElementAtIndex(i);
                var card = Gs.Box("gs-block");
                var header = Gs.Box("gs-block__header");

                var sentence = elementType != null ? BlockTypes.Sentence(elementType) : null;
                if (element.propertyType == SerializedPropertyType.Generic && !string.IsNullOrEmpty(sentence) && sentence.Contains("{"))
                {
                    // 문장형 항목(필드 검사 등): 겉패널 없이 한 줄.
                    card.RemoveFromClassList("gs-block");
                    card.AddToClassList("gs-block-line");
                    var line = Gs.Box("gs-block-line__row");
                    var words = Sentence(element, sentence, new HashSet<string>(), onValue);
                    words.AddToClassList("gs-grow");
                    line.Add(words);
                    var remove = RemoveButton(() => Remove(list, index, onStructure));
                    remove.AddToClassList("gs-block__action");
                    line.Add(remove);
                    card.Add(line);
                }
                else if (element.propertyType == SerializedPropertyType.Generic && element.hasVisibleChildren)
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
                case StatRule rule: return rule.Describe("이 로그");
                case TriggerRule rule: return rule.Natural(EditorDatabase.Instance);
                case null: return "";
            }
            return string.Join("  ·  ", PropertyUtil.Children(element).Select(PropertyUtil.Summary).Where(s => !string.IsNullOrEmpty(s)));
        }

        // "{stat} {value} {op}" → [처치 수] [100] [이상]. 필드 단어는 클릭하면 바꾸고, 나머지 글자는 옅은 고정 글자.
        public static VisualElement Sentence(SerializedProperty block, string template, HashSet<string> used, Action onValue)
        {
            var row = Gs.Box("gs-sentence");
            var tokens = new List<GsInlineProperty>();
            int index = 0;
            while (index < template.Length)
            {
                var open = template.IndexOf('{', index);
                if (open < 0)
                {
                    AddWords(row, template.Substring(index));
                    break;
                }
                if (open > index) AddWords(row, template.Substring(index, open - index));
                var close = template.IndexOf('}', open);
                if (close < 0) break;
                var name = template.Substring(open + 1, close - open - 1);
                var child = block.FindPropertyRelative(name);
                if (child != null && !child.hasVisibleChildren)
                {
                    used.Add(name);
                    var token = new GsInlineProperty(child);
                    token.AddToClassList("gs-token");
                    // 첫 단어는 문장의 주어: 뒤 내용과 간격을 더 둔다.
                    if (used.Count == 1) token.AddToClassList("gs-token--subject");
                    // 한 단어가 바뀌면 같은 문장의 다른 단어도 다시 그린다(필드를 바꾸면 비교·값 표시가 달라지므로).
                    token.Changed += () =>
                    {
                        foreach (var other in tokens) if (other != token) other.Refresh();
                        onValue?.Invoke();
                    };
                    tokens.Add(token);
                    var field = PropertyUtil.GetField(child, out _);
                    token.tooltip = LabelOf(child, field);
                    row.Add(token);
                }
                index = close + 1;
            }
            return row;
        }

        static void AddWords(VisualElement row, string text)
        {
            var trimmed = text.Trim();
            if (trimmed.Length > 0) row.Add(Gs.Label(trimmed, "gs-sentence__word"));
        }

        public static string KindName(Type baseType)
        {
            if (baseType == typeof(Condition)) return "조건";
            if (baseType == typeof(Reward)) return "보상";
            if (baseType == typeof(Outcome)) return "결과";
            if (baseType == typeof(Trigger)) return "트리거";
            if (baseType == typeof(ValueFilter)) return "조건";
            if (baseType == typeof(Effect)) return "효과";
            if (baseType == typeof(TargetSelector)) return "대상";
            return "항목";
        }

        // property: 고르는 칸. 결과 칸이면 그 칸의 잠금([OutcomeSlot])에 없는 종류는 빼고 보인다.
        public static void ShowTypeMenu(VisualElement anchor, Type baseType, Action<Type> onPick, SerializedProperty property = null)
        {
            var allowed = property != null && typeof(Outcome).IsAssignableFrom(baseType) ? TriggerSlots.OutcomeSlotOf(property)?.Allowed : null;
            var items = BlockTypes.ConcreteTypes(baseType)
                .Where(type => allowed == null || (((Outcome)Activator.CreateInstance(type)).Kind & allowed.Value) != 0)
                .Select(type =>
            {
                var picked = type;
                return new GsDropdownItem(BlockTypes.DisplayName(type), () => onPick(picked))
                {
                    Group = BlockTypes.Category(type),
                    Detail = BlockTypes.Description(type),
                };
            }).ToList();
            if (items.Count == 0) items.Add(new GsDropdownItem("사용할 수 있는 블록이 없습니다") { Disabled = true });
            GsDropdown.Show(anchor, items, placeholder: $"{KindName(baseType)} 검색");
        }
    }

    // 조건·보상 블록 하나. 비어 있으면 "선택" 버튼, 차 있으면 카드 + 내부 필드.
    public sealed class BlockEditor : VisualElement
    {
        // 접힌 그룹 블록(에셋·경로별). 창을 다시 그려도 유지된다.
        static readonly HashSet<string> Collapsed = new HashSet<string>();

        public BlockEditor(SerializedProperty property, Type baseType, Action onValue, Action onStructure, Action onRemove,
                           Action<int> onMove = null, bool canMoveUp = false, bool canMoveDown = false, Action onDuplicate = null)
        {
            var value = property.managedReferenceValue;
            if (value == null)
            {
                GsButton pick = null;
                pick = new GsButton($"+ {GsPropertyEditor.KindName(baseType)} 선택", () => GsPropertyEditor.ShowTypeMenu(pick, baseType, type =>
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
            tooltip = BlockTypes.Description(type) ?? "";

            GsButton swap = null;
            swap = new GsButton("바꾸기", () => GsPropertyEditor.ShowTypeMenu(swap, baseType, newType =>
            {
                property.serializedObject.Update();
                property.managedReferenceValue = Activator.CreateInstance(newType);
                property.serializedObject.ApplyModifiedProperties();
                onStructure?.Invoke();
            }, property), GsButtonKind.Ghost);
            swap.AddToClassList("gs-block__action");

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
            remove.AddToClassList("gs-block__action");
            remove.tooltip = "삭제";

            // 줄 도구(마우스를 올리면 보임): 위로·아래로·복제·바꾸기·삭제
            var tools = new List<VisualElement>();
            if (onMove != null && canMoveUp) tools.Add(Action("↑", "위로", () => onMove(-1)));
            if (onMove != null && canMoveDown) tools.Add(Action("↓", "아래로", () => onMove(1)));
            if (onDuplicate != null) tools.Add(Action("복제", "바로 아래에 복사", onDuplicate));
            tools.Add(swap);
            tools.Add(remove);

            // 플레이어 데이터에 주기: 보상 목록과 같은 한 줄(왼쪽 필드, 오른쪽 형식에 맞는 값 칸).
            if (value is PlayerDataReward)
            {
                AddToClassList("gs-block-line");
                Add(RewardListEditor.Row(property, onValue, onStructure, tools));
                return;
            }

            var used = new HashSet<string>();
            var sentence = BlockTypes.Sentence(type);
            if (!string.IsNullOrEmpty(sentence) && sentence.Contains("{"))
            {
                // 문장 블록: 겉패널 없이 한 줄. [단어] 패널마다 클릭해 바꾸고, 바꾸기·삭제는 마우스를 올리면 오른쪽 끝에.
                AddToClassList("gs-block-line");
                var line = Gs.Box("gs-block-line__row");
                // 식은 코드처럼, 뜻은 마우스를 올리면 문장으로.
                line.tooltip = Explain(value);
                var words = GsPropertyEditor.Sentence(property, sentence, used, onValue);
                words.AddToClassList("gs-grow");
                line.Add(words);
                foreach (var tool in tools) line.Add(tool);
                Add(line);
            }
            else
            {
                // 그룹 블록(모두 만족 등): 이름 한 줄 + 들여쓴 하위 문장들. 머리줄을 누르면 접고 펼친다.
                AddToClassList("gs-block-group");
                var key = property.serializedObject.targetObject.GetHashCode() + ":" + property.propertyPath;
                var collapsed = Collapsed.Contains(key);
                var header = Gs.Box("gs-block__header");
                var chevron = Gs.Label(collapsed ? "▸" : "▾", "gs-block__chevron");
                header.Add(chevron);
                var keyword = BlockTypes.Keyword(type);
                if (!string.IsNullOrEmpty(keyword))
                {
                    var token = Gs.Label(keyword, "gs-token", "gs-token--keyword");
                    token.tooltip = Explain(value);
                    header.Add(token);
                }
                var children = (value as Condition)?.Children.Count() ?? (value as ValueFilter)?.Children.Count() ?? 0;
                header.Add(Gs.Label((string.IsNullOrEmpty(keyword) ? BlockTypes.DisplayName(type) : "") + (children > 0 ? $"  {children}개" : ""), "gs-caption"));
                var summary = Gs.Label(collapsed ? Summary(value) : "", "gs-caption", "gs-nowrap", "gs-block__summary");
                summary.style.display = collapsed ? DisplayStyle.Flex : DisplayStyle.None;
                header.Add(summary);
                header.Add(Gs.Spacer());
                foreach (var tool in tools) header.Add(tool);
                header.RegisterCallback<ClickEvent>(e =>
                {
                    if (e.target is VisualElement target && (target is GsButton || target.GetFirstAncestorOfType<GsButton>() != null)) return;
                    var now = !Collapsed.Contains(key);
                    if (now) Collapsed.Add(key);
                    else Collapsed.Remove(key);
                    chevron.text = now ? "▸" : "▾";
                    summary.text = now ? Summary(property.managedReferenceValue) : "";
                    summary.style.display = now ? DisplayStyle.Flex : DisplayStyle.None;
                    foreach (var child in Children().Where(c => c.ClassListContains("gs-block__children"))) child.style.display = now ? DisplayStyle.None : DisplayStyle.Flex;
                });
                header.tooltip = "클릭해서 접기·펼치기";
                Add(header);
            }

            foreach (var child in PropertyUtil.Children(property))
            {
                if (used.Contains(child.name)) continue;
                // 하위 조건 목록은 제목 없이 내용만, 들여쓴 줄로 묶는다.
                if (child.isArray || child.propertyType == SerializedPropertyType.ManagedReference)
                {
                    var nested = Gs.Box("gs-block__children");
                    nested.Add(GsPropertyEditor.BuildContent(child, onValue, onStructure));
                    if (ClassListContains("gs-block-group") && Collapsed.Contains(property.serializedObject.targetObject.GetHashCode() + ":" + property.propertyPath))
                        nested.style.display = DisplayStyle.None;
                    Add(nested);
                }
                else
                {
                    Add(GsPropertyEditor.BuildProperty(child, onValue, onStructure));
                }
            }
        }

        static GsButton Action(string text, string tip, Action onClick)
        {
            var button = new GsButton(text, onClick, GsButtonKind.Ghost);
            button.AddToClassList("gs-block__action");
            button.tooltip = tip;
            return button;
        }

        static string Explain(object value) =>
            value is Condition condition ? condition.Explain(EditorDatabase.Instance) : value is ValueFilter filter ? filter.Explain()
            : value is Outcome outcome ? outcome.Explain(EditorDatabase.Instance) : value is Trigger trigger ? trigger.Explain(EditorDatabase.Instance) : "";

        static string Summary(object value) =>
            value is Condition condition ? condition.Describe(EditorDatabase.Instance) : value is ValueFilter filter ? filter.Describe()
            : value is Outcome outcome ? outcome.Describe(EditorDatabase.Instance) : value is Trigger trigger ? trigger.Describe(EditorDatabase.Instance) : "";
    }
}
