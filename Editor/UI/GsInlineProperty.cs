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
        Type DataRefType => _field?.GetCustomAttribute<DataRefAttribute>()?.DataType;
        bool IsDataTypeRef => _field != null && _field.IsDefined(typeof(DataTypeRefAttribute), false);
        bool Has<T>() where T : Attribute => _field != null && _field.IsDefined(typeof(T), false);

        public void Refresh()
        {
            if (_editor != null) return;
            _property.serializedObject.Update();
            var (text, empty, broken) = Display();
            Gs.SetText(_value, text);
            _value.EnableInClassList("gs-inline__empty", empty);
            EnableInClassList("gs-inline--broken", broken);
        }

        (string text, bool empty, bool broken) Display() => Describe(_property, _field);

        // 값의 표시 문장. 시트 셀·변경 표시 등에서도 같은 규칙으로 쓴다(ID는 이름으로, 끊긴 참조는 표시).
        public static (string text, bool empty, bool broken) Describe(SerializedProperty property, FieldInfo field = null)
        {
            if (field == null) field = PropertyUtil.GetField(property, out _);
            var db = EditorDatabase.Instance;
            if (field != null && field.IsDefined(typeof(FieldOpRefAttribute), false) && property.propertyType == SerializedPropertyType.Enum)
                return (FieldOps.Symbol((FieldOp)property.intValue), false, false);
            if (field != null && field.IsDefined(typeof(FieldPathAttribute), false) && property.propertyType == SerializedPropertyType.String)
            {
                if (string.IsNullOrEmpty(property.stringValue)) return ("필드 선택", true, false);
                var root = FieldPathContext.RootType(property, out _);
                var broken = root != null && FieldPaths.TypeAt(root, property.stringValue, out _) == null;
                return (FieldPathContext.DescribePath(property), false, broken);
            }
            var valueRef = field?.GetCustomAttribute<FieldValueRefAttribute>();
            if (valueRef != null && property.propertyType == SerializedPropertyType.String)
            {
                var op = property.serializedObject.FindProperty(FieldPathContext.Sibling(property.propertyPath, valueRef.OpField));
                if (op != null && !FieldOps.NeedsValue((FieldOp)op.intValue)) return ("—", true, false);
                if (string.IsNullOrEmpty(property.stringValue)) return ("값 입력", true, false);
                var leafRef = LeafDataRef(property, valueRef);
                if (leafRef != null)
                {
                    var target = db.FindById(property.stringValue);
                    return target != null ? (target.DisplayName, false, false) : ($"없는 항목: {property.stringValue}", false, true);
                }
                return (property.stringValue, false, false);
            }
            if (field != null && field.IsDefined(typeof(StateKeyRefAttribute), false) && property.propertyType == SerializedPropertyType.String)
            {
                if (string.IsNullOrEmpty(property.stringValue)) return ("상태 선택", true, false);
                var key = db.FindById(property.stringValue) as StateKeyData;
                return key != null ? (key.DisplayName, false, false) : ($"없는 상태 키: {property.stringValue}", false, true);
            }
            if (property.propertyType == SerializedPropertyType.String)
            {
                var id = property.stringValue;
                if (field != null && (field.IsDefined(typeof(StatRefAttribute), false) || field.IsDefined(typeof(AchievementRefAttribute), false)))
                {
                    if (string.IsNullOrEmpty(id)) return ("선택 안 됨", true, false);
                    var target = db.FindById(id);
                    return target != null ? (target.DisplayName, false, false) : ($"없는 항목: {id}", false, true);
                }
                var dataRef = field?.GetCustomAttribute<DataRefAttribute>();
                if (dataRef != null)
                {
                    var kind = DataTypes.Find(dataRef.DataType)?.DisplayName ?? dataRef.DataType.Name;
                    if (string.IsNullOrEmpty(id)) return ($"{kind} 선택", true, false);
                    var target = db.FindById(id);
                    return target != null && dataRef.DataType.IsInstanceOfType(target) ? (target.DisplayName, false, false) : ($"없는 {kind}: {id}", false, true);
                }
                if (field != null && field.IsDefined(typeof(DataTypeRefAttribute), false))
                {
                    if (string.IsNullOrEmpty(id)) return ("형식 선택", true, false);
                    var valueType = TypeResolver.Find(id);
                    return valueType != null ? (StorableTypes.Label(valueType), false, false) : ($"없는 형식: {id}", false, true);
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
                case SerializedPropertyType.Enum when Has<FieldOpRefAttribute>():
                    ShowFieldOpMenu();
                    return;
                case SerializedPropertyType.Enum:
                    ShowEnumMenu();
                    return;
                case SerializedPropertyType.String when Has<FieldPathAttribute>():
                    PickFieldPath();
                    return;
                case SerializedPropertyType.String when Has<StateKeyRefAttribute>():
                    ShowIdMenu(EditorDatabase.Instance.OfType<StateKeyData>().Cast<GameData>(), "상태 키");
                    return;
                case SerializedPropertyType.String when Has<FieldValueRefAttribute>():
                    if (EditFieldValue()) return;
                    break;
                case SerializedPropertyType.String when IsStatRef:
                    ShowIdMenu(EditorDatabase.Instance.OfType<StatData>().Cast<GameData>(), "수치");
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
                case SerializedPropertyType.String when DataRefType != null:
                    ShowIdMenu(EditorDatabase.Instance.OfType(DataRefType), DataTypes.Find(DataRefType)?.DisplayName ?? DataRefType.Name);
                    return;
                case SerializedPropertyType.String when IsDataTypeRef:
                    ShowStorableTypeMenu();
                    return;
            }
            BeginEdit();
        }

        // ── 목록 선택 ─────────────────────────────────────────

        void ShowEnumMenu()
        {
            var names = _property.enumDisplayNames;
            var items = new List<GsDropdownItem>();
            for (int i = 0; i < names.Length; i++)
            {
                var index = i;
                items.Add(new GsDropdownItem(names[i], () =>
                {
                    _property.serializedObject.Update();
                    _property.enumValueIndex = index;
                    Apply();
                }) { Selected = _property.enumValueIndex == i });
            }
            GsDropdown.Show(this, items);
        }

        void ShowIdMenu(IEnumerable<GameData> data, string kind)
        {
            var list = data.OrderBy(d => d.DisplayName).ToList();
            var items = list.Select(d =>
            {
                var id = d.Id;
                return new GsDropdownItem(d.DisplayName, () => SetString(id)) { Caption = id, Selected = _property.stringValue == id };
            }).ToList();
            if (items.Count == 0) items.Add(new GsDropdownItem($"{kind}이(가) 아직 없습니다 — 데이터베이스에서 먼저 만드세요") { Disabled = true });
            GsDropdown.Show(this, items, placeholder: $"{kind} 검색");
        }

        void ShowEventTypeMenu()
        {
            var items = TypeResolver.GameEventTypes().OrderBy(EventBus.DisplayNameOf).Select(type =>
            {
                var name = type.FullName;
                return new GsDropdownItem(EventBus.DisplayNameOf(type), () => SetString(name)) { Caption = type.Name, Selected = _property.stringValue == name };
            }).ToList();
            if (items.Count == 0) items.Add(new GsDropdownItem("[GameEvent]가 붙은 이벤트 타입이 없습니다") { Disabled = true });
            GsDropdown.Show(this, items, placeholder: "이벤트 검색");
        }

        void ShowEventFieldMenu()
        {
            var items = new List<GsDropdownItem>
            {
                new GsDropdownItem("횟수 (필드 없음)", () => SetString("")) { Detail = "이벤트가 한 번 발생할 때마다 1", Selected = string.IsNullOrEmpty(_property.stringValue) },
            };
            var eventType = TypeResolver.Find(Sibling(EventFieldRef.EventTypeField)?.stringValue);
            if (eventType == null)
            {
                items.Add(new GsDropdownItem("이벤트를 먼저 선택하세요") { Disabled = true });
            }
            else
            {
                foreach (var member in TypeResolver.NumericMembers(eventType))
                {
                    var name = member.Name;
                    var type = member is FieldInfo f ? f.FieldType : ((PropertyInfo)member).PropertyType;
                    items.Add(new GsDropdownItem(name, () => SetString(name)) { Caption = type.Name, Selected = _property.stringValue == name });
                }
            }
            GsDropdown.Show(this, items);
        }
        // ── 필드 검사(경로·비교·값) ───────────────────────────

        void PickFieldPath()
        {
            var root = FieldPathContext.RootType(_property, out var rootLabel);
            var host = GetFirstAncestorOfType<GsModal>()?.parent ?? panel?.visualTree.Q(className: "gs-root") ?? this;
            if (root == null)
            {
                GsDropdown.Show(this, new List<GsDropdownItem> { new GsDropdownItem("이벤트나 상태 키를 먼저 고르세요") { Disabled = true } });
                return;
            }
            GsDataExplorer.Pick(host, root, rootLabel ?? DataModel.FriendlyName(root), _property.stringValue, picked =>
            {
                _property.serializedObject.Update();
                _property.stringValue = picked;
                // 새 필드 형식에 맞지 않는 비교는 맞는 첫 비교로 바꾸고 값을 비운다.
                var leaf = FieldPaths.TypeAt(root, picked, out _);
                foreach (var sibling in new[] { "op" })
                {
                    var op = PathSibling(sibling);
                    if (op == null || op.propertyType != SerializedPropertyType.Enum || leaf == null) continue;
                    var allowed = FieldOps.For(leaf);
                    if (allowed.Count == 0 || allowed.Contains((FieldOp)op.intValue)) continue;
                    op.intValue = (int)allowed[0];
                    var value = PathSibling("value");
                    if (value != null && value.propertyType == SerializedPropertyType.String) value.stringValue = "";
                }
                Apply();
            });
        }

        SerializedProperty PathSibling(string name) => _property.serializedObject.FindProperty(FieldPathContext.Sibling(_property.propertyPath, name));

        void ShowFieldOpMenu()
        {
            var attr = _field.GetCustomAttribute<FieldOpRefAttribute>();
            var path = PathSibling(attr.PathField);
            var type = path == null ? null : FieldPathContext.LeafType(path, out _);
            var ops = type == null ? (IReadOnlyList<FieldOp>)Enum.GetValues(typeof(FieldOp)).Cast<FieldOp>().ToList() : FieldOps.For(type);
            var items = ops.Select(op => new GsDropdownItem(FieldOps.Symbol(op), () =>
            {
                _property.serializedObject.Update();
                _property.intValue = (int)op;
                Apply();
            }) { Caption = FieldOps.Word(op), Selected = _property.intValue == (int)op }).ToList();
            if (type == null) items.Insert(0, new GsDropdownItem("필드를 먼저 고르면 맞는 비교만 보입니다") { Disabled = true });
            GsDropdown.Show(this, items);
        }

        // 값 칸: enum·ID 참조면 목록, 필요 없는 비교면 무시, 나머지는 글자 입력(false 반환).
        bool EditFieldValue()
        {
            var attr = _field.GetCustomAttribute<FieldValueRefAttribute>();
            var op = PathSibling(attr.OpField);
            var fieldOp = op == null ? FieldOp.Equal : (FieldOp)op.intValue;
            if (!FieldOps.NeedsValue(fieldOp)) return true;
            if (FieldOps.IsCount(fieldOp)) return false;
            var path = PathSibling(attr.PathField);
            var type = path == null ? null : FieldPathContext.LeafType(path, out _);
            if (type != null && (fieldOp == FieldOp.Contains || fieldOp == FieldOp.NotContains)) type = FieldPaths.ElementTypeOf(type) ?? type;
            type = type == null ? null : Nullable.GetUnderlyingType(type) ?? type;
            var dataRef = LeafDataRef(_property, attr);
            if (dataRef != null)
            {
                ShowIdMenu(EditorDatabase.Instance.OfType(dataRef), DataTypes.Find(dataRef)?.DisplayName ?? dataRef.Name);
                return true;
            }
            if (type != null && type.IsEnum)
            {
                var items = Enum.GetNames(type).Select(name => new GsDropdownItem(name, () => SetString(name)) { Selected = _property.stringValue == name }).ToList();
                GsDropdown.Show(this, items);
                return true;
            }
            return false;
        }

        // 경로 끝 필드에 [DataRef]가 붙어 있으면 그 데이터 타입(예: 태그 목록 → 태그).
        static Type LeafDataRef(SerializedProperty valueProperty, FieldValueRefAttribute attr)
        {
            var path = valueProperty.serializedObject.FindProperty(FieldPathContext.Sibling(valueProperty.propertyPath, attr.PathField));
            if (path == null) return null;
            FieldPathContext.LeafType(path, out var member);
            return member?.GetCustomAttribute<DataRefAttribute>()?.DataType;
        }

        void ShowStorableTypeMenu()
        {
            var items = StorableTypes.All().Select(type =>
            {
                var name = type.FullName;
                return new GsDropdownItem(StorableTypes.Label(type), () =>
                {
                    var instanceField = _field.GetCustomAttribute<DataTypeRefAttribute>()?.InstanceField;
                    if (string.IsNullOrEmpty(instanceField))
                    {
                        SetString(name);
                        return;
                    }
                    // 형식을 고르면 그 형식의 새 인스턴스를 옆 칸에 넣는다(템플릿). 같은 형식이면 기존 값을 둔다.
                    _property.serializedObject.Update();
                    var changed = _property.stringValue != name;
                    _property.stringValue = name;
                    var instance = _property.serializedObject.FindProperty(FieldPathContext.Sibling(_property.propertyPath, instanceField));
                    if (instance != null && instance.propertyType == SerializedPropertyType.ManagedReference && (changed || instance.managedReferenceValue == null))
                        instance.managedReferenceValue = DataModel.Create(type);
                    Apply();
                })
                {
                    Group = StorableTypes.GroupOf(type),
                    Caption = type.Namespace,
                    Selected = _property.stringValue == name,
                };
            }).ToList();
            GsDropdown.Show(this, items, placeholder: "형식 검색");
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
