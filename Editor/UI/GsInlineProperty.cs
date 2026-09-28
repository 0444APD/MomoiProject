using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using GameSystems.Data;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameSystems.EditorTools.UI
{
    // 정보 우선 원칙의 핵심 부품.
    // 평소: 값이 일반 글자로 보인다. 마우스를 올리면 옅게 강조. 클릭하면 편집 UI로 바뀐다.
    // 편집 종료(Enter·포커스 해제·바깥 클릭) 시 값을 적용하고, Esc는 취소한다.
    public sealed class GsInlineProperty : VisualElement
    {
        readonly SerializedProperty _property;
        readonly FieldInfo _field;
        readonly Type _valueType;
        readonly Label _value;
        VisualElement _editor;
        EventCallback<PointerDownEvent> _outsideHandler;

        public event Action Changed;

        // 긴 글을 여러 줄로 편집한다(상세 패널). Enter는 적용, Shift+Enter는 줄바꿈.
        public bool Multiline { get; set; }

        public GsInlineProperty(SerializedProperty property)
        {
            _property = property.Copy();
            _field = PropertyUtil.GetField(_property, out _valueType);
            AddToClassList("gs-inline");

            _value = Gs.Label("", "gs-inline__value", "gs-grow");
            Add(_value);

            if (IsEditable)
            {
                AddToClassList("gs-inline--editable");
                RegisterCallback<ClickEvent>(OnClick);
                tooltip = "클릭해서 편집";
            }
            Refresh();
        }

        bool IsEditable
        {
            get
            {
                switch (_property.propertyType)
                {
                    case SerializedPropertyType.String:
                    case SerializedPropertyType.Integer:
                    case SerializedPropertyType.Float:
                    case SerializedPropertyType.Boolean:
                    case SerializedPropertyType.Enum:
                    case SerializedPropertyType.ObjectReference:
                    case SerializedPropertyType.Color:
                        return true;
                }
                return false;
            }
        }

        bool IsStatRef => _field != null && _field.IsDefined(typeof(StatRefAttribute), false);
        bool IsAchievementRef => _field != null && _field.IsDefined(typeof(AchievementRefAttribute), false);
        bool IsEventTypeRef => _field != null && _field.IsDefined(typeof(EventTypeRefAttribute), false);
        EventFieldRefAttribute EventFieldRef => _field?.GetCustomAttribute<EventFieldRefAttribute>();

        public void Refresh()
        {
            if (_editor != null) return;
            _property.serializedObject.Update();
            var (text, empty, broken) = Display();
            _value.text = text;
            _value.EnableInClassList("gs-inline__empty", empty);
            EnableInClassList("gs-inline--broken", broken);
        }

        (string text, bool empty, bool broken) Display() => Describe(_property, _field);

        // 값의 표시 문장. 시트 셀·변경 표시 등에서도 같은 규칙으로 쓴다(ID는 이름으로, 끊긴 참조는 표시).
        public static (string text, bool empty, bool broken) Describe(SerializedProperty property, FieldInfo field = null)
        {
            if (field == null) field = PropertyUtil.GetField(property, out _);
            var db = EditorDatabase.Instance;
            if (property.propertyType == SerializedPropertyType.String)
            {
                var id = property.stringValue;
                if (field != null && (field.IsDefined(typeof(StatRefAttribute), false) || field.IsDefined(typeof(AchievementRefAttribute), false)))
                {
                    if (string.IsNullOrEmpty(id)) return ("선택 안 됨", true, false);
                    var target = db.FindById(id);
                    return target != null ? (target.DisplayName, false, false) : ($"없는 항목: {id}", false, true);
                }
                if (field != null && field.IsDefined(typeof(EventTypeRefAttribute), false))
                {
                    if (string.IsNullOrEmpty(id)) return ("선택 안 됨", true, false);
                    var type = TypeResolver.Find(id);
                    return type != null ? (EventBus.DisplayNameOf(type), false, false) : ($"없는 이벤트: {id}", false, true);
                }
                var fieldRef = field?.GetCustomAttribute<EventFieldRefAttribute>();
                if (fieldRef != null)
                {
                    if (string.IsNullOrEmpty(id)) return ("횟수 (필드 없음)", true, false);
                    var eventType = TypeResolver.Find(Sibling(property, fieldRef.EventTypeField)?.stringValue);
                    if (eventType != null && TypeResolver.NumberGetter(eventType, id, out _) == null) return ($"없는 필드: {id}", false, true);
                    return (id, false, false);
                }
                return string.IsNullOrEmpty(id) ? ("비어 있음", true, false) : (id, false, false);
            }

            if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue == null)
                return ("없음", true, false);

            var summary = PropertyUtil.Summary(property);
            return (summary, string.IsNullOrEmpty(summary), false);
        }

        SerializedProperty Sibling(string name) => Sibling(_property, name);

        static SerializedProperty Sibling(SerializedProperty property, string name)
        {
            var path = property.propertyPath;
            var index = path.LastIndexOf('.');
            var siblingPath = index < 0 ? name : path.Substring(0, index + 1) + name;
            return property.serializedObject.FindProperty(siblingPath);
        }

        void OnClick(ClickEvent evt)
        {
            if (_editor != null) return;
            evt.StopPropagation();
            _property.serializedObject.Update();

            switch (_property.propertyType)
            {
                case SerializedPropertyType.Boolean:
                    _property.boolValue = !_property.boolValue;
                    Apply();
                    return;
                case SerializedPropertyType.Enum:
                    ShowEnumMenu();
                    return;
                case SerializedPropertyType.String when IsStatRef:
                    ShowIdMenu(EditorDatabase.Instance.OfType<StatData>().Cast<GameData>(), "통계");
                    return;
                case SerializedPropertyType.String when IsAchievementRef:
                    ShowIdMenu(EditorDatabase.Instance.OfType<AchievementData>().Cast<GameData>(), "도전과제");
                    return;
                case SerializedPropertyType.String when IsEventTypeRef:
                    ShowEventTypeMenu();
                    return;
                case SerializedPropertyType.String when EventFieldRef != null:
                    ShowEventFieldMenu();
                    return;
            }
            BeginEdit();
        }

        // ── 목록 선택 ─────────────────────────────────────────

        void ShowEnumMenu()
        {
            var menu = new GenericMenu();
            var names = _property.enumDisplayNames;
            for (int i = 0; i < names.Length; i++)
            {
                var index = i;
                menu.AddItem(new GUIContent(names[i]), _property.enumValueIndex == i, () =>
                {
                    _property.serializedObject.Update();
                    _property.enumValueIndex = index;
                    Apply();
                });
            }
            menu.ShowAsContext();
        }

        void ShowIdMenu(IEnumerable<GameData> items, string kind)
        {
            var menu = new GenericMenu();
            var list = items.OrderBy(d => d.DisplayName).ToList();
            if (list.Count == 0) menu.AddDisabledItem(new GUIContent($"{kind}이(가) 아직 없습니다 — 데이터베이스에서 먼저 만드세요"));
            foreach (var item in list)
            {
                var id = item.Id;
                menu.AddItem(new GUIContent($"{item.DisplayName}   ({id})"), _property.stringValue == id, () => SetString(id));
            }
            menu.ShowAsContext();
        }

        void ShowEventTypeMenu()
        {
            var menu = new GenericMenu();
            var types = TypeResolver.GameEventTypes().OrderBy(EventBus.DisplayNameOf).ToList();
            if (types.Count == 0) menu.AddDisabledItem(new GUIContent("[GameEvent]가 붙은 이벤트 타입이 없습니다"));
            foreach (var type in types)
            {
                var name = type.FullName;
                menu.AddItem(new GUIContent($"{EventBus.DisplayNameOf(type)}   ({type.Name})"), _property.stringValue == name, () => SetString(name));
            }
            menu.ShowAsContext();
        }

        void ShowEventFieldMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("횟수 (필드 없음)"), string.IsNullOrEmpty(_property.stringValue), () => SetString(""));
            var eventType = TypeResolver.Find(Sibling(EventFieldRef.EventTypeField)?.stringValue);
            if (eventType == null)
            {
                menu.AddDisabledItem(new GUIContent("이벤트를 먼저 선택하세요"));
            }
            else
            {
                foreach (var member in TypeResolver.NumericMembers(eventType))
                {
                    var name = member.Name;
                    menu.AddItem(new GUIContent(name), _property.stringValue == name, () => SetString(name));
                }
            }
            menu.ShowAsContext();
        }

        void SetString(string value)
        {
            _property.serializedObject.Update();
            _property.stringValue = value;
            Apply();
        }

        // ── 입력 필드 편집 ────────────────────────────────────

        void BeginEdit()
        {
            _editor = CreateEditor();
            if (_editor == null) return;

            AddToClassList("gs-inline--editing");
            _value.style.display = DisplayStyle.None;
            Add(_editor);

            _editor.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Escape)
                {
                    e.StopPropagation();
                    EndEdit(false);
                }
                else if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && !(Multiline && e.shiftKey))
                {
                    e.StopPropagation();
                    EndEdit(true);
                }
            }, TrickleDown.TrickleDown);

            // 텍스트 필드는 내부 입력 요소로 포커스가 옮겨가며 FocusOut이 생길 수 있어, 한 틱 뒤 실제로 벗어났는지 확인한다.
            if (!(_editor is ObjectField) && !(_editor is ColorField))
                _editor.RegisterCallback<FocusOutEvent>(_ => schedule.Execute(() =>
                {
                    if (_editor != null && !IsFocusWithin(_editor)) EndEdit(true);
                }).StartingIn(30));

            // 바깥을 클릭하면 편집을 끝낸다(오브젝트·색 선택처럼 포커스가 창 밖으로 나가는 편집기 포함).
            var root = panel?.visualTree;
            if (root != null)
            {
                _outsideHandler = e =>
                {
                    if (_editor != null && e.target is VisualElement target && !_editor.Contains(target) && target != _editor)
                        EndEdit(true);
                };
                root.RegisterCallback(_outsideHandler, TrickleDown.TrickleDown);
            }

            _editor.schedule.Execute(() =>
            {
                _editor?.Focus();
                if (_editor is TextField text) text.SelectAll();
            });
        }

        static bool IsFocusWithin(VisualElement element)
        {
            var focused = element.focusController?.focusedElement as VisualElement;
            return focused != null && (focused == element || element.Contains(focused));
        }

        VisualElement CreateEditor()
        {
            switch (_property.propertyType)
            {
                case SerializedPropertyType.String:
                    var text = new TextField { value = _property.stringValue, multiline = Multiline };
                    if (Multiline) text.style.whiteSpace = WhiteSpace.Normal;
                    return text;
                case SerializedPropertyType.Integer:
                    return new LongField { value = _property.longValue };
                case SerializedPropertyType.Float:
                    return _property.type == "double"
                        ? (VisualElement)new DoubleField { value = _property.doubleValue }
                        : new FloatField { value = _property.floatValue };
                case SerializedPropertyType.ObjectReference:
                    var objectField = new ObjectField { objectType = _valueType ?? typeof(UnityEngine.Object), allowSceneObjects = false, value = _property.objectReferenceValue };
                    objectField.RegisterValueChangedCallback(e =>
                    {
                        _property.serializedObject.Update();
                        _property.objectReferenceValue = e.newValue;
                        Apply(keepEditing: true);
                    });
                    return objectField;
                case SerializedPropertyType.Color:
                    var colorField = new ColorField { value = _property.colorValue };
                    colorField.RegisterValueChangedCallback(e =>
                    {
                        _property.serializedObject.Update();
                        _property.colorValue = e.newValue;
                        Apply(keepEditing: true);
                    });
                    return colorField;
            }
            return null;
        }

        void EndEdit(bool commit)
        {
            if (_editor == null) return;
            var editor = _editor;
            _editor = null;

            if (_outsideHandler != null)
            {
                panel?.visualTree.UnregisterCallback(_outsideHandler, TrickleDown.TrickleDown);
                _outsideHandler = null;
            }

            if (commit)
            {
                _property.serializedObject.Update();
                switch (editor)
                {
                    case TextField t: _property.stringValue = t.value; break;
                    case LongField l: _property.longValue = l.value; break;
                    case DoubleField d: _property.doubleValue = d.value; break;
                    case FloatField f: _property.floatValue = f.value; break;
                }
            }

            editor.RemoveFromHierarchy();
            RemoveFromClassList("gs-inline--editing");
            _value.style.display = DisplayStyle.Flex;
            if (commit) Apply();
            else Refresh();
        }

        void Apply(bool keepEditing = false)
        {
            // ApplyModifiedProperties가 Undo 기록을 남긴다.
            _property.serializedObject.ApplyModifiedProperties();
            if (!keepEditing) Refresh();
            Changed?.Invoke();
        }

        public static string FormatDouble(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
