using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using GBible.Data;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace GBible.EditorTools.UI
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

            // 색: 왼쪽에 둥근 정사각형 미리보기, 오른쪽에 16진수.
            if (_property.propertyType == SerializedPropertyType.Color)
            {
                _swatch = Gs.Box("gs-inline__swatch");
                Insert(0, _swatch);
            }
            // 오브젝트·색은 칸을 누르면 Unity 선택 창(오브젝트 선택기·색 선택기)이 바로 열린다. 끌어다 놓기도 된다.
            if (_property.propertyType == SerializedPropertyType.ObjectReference) DirectPicker = ObjectPicker();
            else if (_property.propertyType == SerializedPropertyType.Color) DirectPicker = ColorPicker();
            if (DirectPicker != null) Add(DirectPicker);

            // 식별용 글자 칸: 오른쪽 클릭으로 클래스 상세를 연다(필드와 키워드 그룹을 잇거나 새로 만든다).
            if (_property.propertyType == SerializedPropertyType.String && KeywordTools.CanConnect(_field))
            {
                var field = _field;
                this.AddManipulator(new ContextualMenuManipulator(e =>
                    e.menu.AppendAction("키워드 연결…", _ => ClassDetail.Open(field.DeclaringType, field.Name))));
            }

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
        bool IsIntentTypeRef => _field != null && _field.IsDefined(typeof(IntentTypeRefAttribute), false);
        EventFieldRefAttribute EventFieldRef => _field?.GetCustomAttribute<EventFieldRefAttribute>();
        // [DataRef] 또는 DB의 필드 규칙으로 정해진 참조 종류.
        Type DataRefType => EditorDatabase.RefTypeOf(_field);
        bool IsDataTypeRef => _field != null && _field.IsDefined(typeof(DataTypeRefAttribute), false);
        bool Has<T>() where T : Attribute => _field != null && _field.IsDefined(typeof(T), false);

        VisualElement _swatch;

        // 칸 전체를 덮는 투명한 Unity 선택기(오브젝트·색). 입력 칸 안에 있으면 칸 상자 전체로 옮겨 덮는다.
        public VisualElement DirectPicker { get; private set; }

        VisualElement ObjectPicker()
        {
            var picker = new ObjectField { objectType = _valueType ?? typeof(UnityEngine.Object), allowSceneObjects = false, value = _property.objectReferenceValue };
            picker.AddToClassList("gs-detail__picker");
            picker.RegisterValueChangedCallback(e =>
            {
                _property.serializedObject.Update();
                _property.objectReferenceValue = e.newValue;
                Apply();
                OpenFramingIfRuled(e.newValue);
            });
            return picker;
        }

        VisualElement ColorPicker()
        {
            var picker = new ColorField { value = _property.colorValue, showEyeDropper = false };
            picker.AddToClassList("gs-inline__color-picker");
            picker.RegisterValueChangedCallback(e =>
            {
                _property.serializedObject.Update();
                _property.colorValue = e.newValue;
                Apply();
            });
            return picker;
        }

        public void Refresh()
        {
            if (_editor != null) return;
            _property.serializedObject.Update();
            var (text, empty, broken) = Display();
            Gs.SetText(_value, text);
            _value.EnableInClassList("gs-inline__empty", empty);
            EnableInClassList("gs-inline--broken", broken);
            if (_property.propertyType == SerializedPropertyType.Color)
            {
                var color = _property.colorValue;
                if (_swatch != null) _swatch.style.backgroundColor = color;
                Gs.SetText(_value, "#" + (color.a < 1f ? ColorUtility.ToHtmlStringRGBA(color) : ColorUtility.ToHtmlStringRGB(color)));
            }
            switch (DirectPicker)
            {
                case ObjectField objectPicker: objectPicker.SetValueWithoutNotify(_property.objectReferenceValue); break;
                case ColorField colorPicker: colorPicker.SetValueWithoutNotify(_property.colorValue); break;
            }
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
                var emptyLabel = field.GetCustomAttribute<FieldPathAttribute>().EmptyLabel;
                if (string.IsNullOrEmpty(property.stringValue)) return emptyLabel != null ? (emptyLabel, false, false) : ("필드 선택", true, false);
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
            if (field != null && field.IsDefined(typeof(PlayerDataRefAttribute), false) && property.propertyType == SerializedPropertyType.String)
            {
                if (string.IsNullOrEmpty(property.stringValue)) return ("플레이어 데이터 선택", true, false);
                var key = db.FindById(property.stringValue) as PlayerDataEntry;
                return key != null ? (key.DisplayName, false, false) : ($"없는 플레이어 데이터: {property.stringValue}", false, true);
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
                var dataRef = EditorDatabase.RefTypeOf(field);
                if (dataRef != null)
                {
                    var kind = EditorDatabase.RefKindLabel(field);
                    if (string.IsNullOrEmpty(id)) return EditorDatabase.IsRequired(field) ? ($"{kind} 선택 (필수)", true, true) : ($"{kind} 선택", true, false);
                    var target = db.FindById(id);
                    if (target != null && dataRef.IsInstanceOfType(target) && !EditorDatabase.MatchesRef(field, target)) return ($"{kind}이 아님: {id}", false, true);
                    return target != null && dataRef.IsInstanceOfType(target) ? (target.DisplayName, false, false) : ($"없는 {kind}: {id}", false, true);
                }
                if (field != null && field.IsDefined(typeof(ClassTypeRefAttribute), false))
                {
                    if (string.IsNullOrEmpty(id)) return ("클래스 선택", true, false);
                    var classType = TypeResolver.Find(id);
                    return classType != null ? (ClassTypes.Label(classType), false, false) : ($"없는 클래스: {id}", false, true);
                }
                if (field != null && field.IsDefined(typeof(GameDataTypeRefAttribute), false))
                {
                    if (string.IsNullOrEmpty(id)) return ("DB 종류 선택", true, false);
                    var dataType = TypeResolver.Find(id);
                    return dataType != null ? (DataTypes.Find(dataType)?.DisplayName ?? dataType.Name, false, false) : ($"없는 종류: {id}", false, true);
                }
                var memberRef = field?.GetCustomAttribute<MemberRefAttribute>();
                if (memberRef != null)
                {
                    if (string.IsNullOrEmpty(id)) return ("필드 선택", true, false);
                    var owner = TypeResolver.Find(Sibling(property, memberRef.TypeField)?.stringValue);
                    var member = owner?.GetField(id, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    return member != null ? ($"{GsDataExplorer.LabelOf(member, id)} ({id})", false, false) : ($"없는 필드: {id}", false, owner != null);
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
                if (field != null && field.IsDefined(typeof(IntentTypeRefAttribute), false))
                {
                    if (string.IsNullOrEmpty(id)) return ("의도 선택", true, false);
                    var type = TypeResolver.Find(id);
                    return type != null ? (Intents.DisplayNameOf(type), false, false) : ($"없는 의도: {id}", false, true);
                }
                var fieldRef = field?.GetCustomAttribute<EventFieldRefAttribute>();
                if (fieldRef != null)
                {
                    if (string.IsNullOrEmpty(id)) return ("횟수 (필드 없음)", true, false);
                    var eventType = EventTypeFor(property, fieldRef);
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

        // 이벤트 필드 칸이 보는 이벤트: 형제 칸(이벤트 이름)이 있으면 그것, 없으면 같은 규칙의 트리거가 보는 이벤트.
        static Type EventTypeFor(SerializedProperty property, EventFieldRefAttribute reference) =>
            string.IsNullOrEmpty(reference.EventTypeField)
                ? FieldPathContext.RootType(property, out _)
                : TypeResolver.Find(Sibling(property, reference.EventTypeField)?.stringValue);

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
            Open();
        }

        public bool Editable => IsEditable;

        // 값이 비었는지(입력 칸의 '비어 있음' 모양에 쓴다).
        public bool IsEmptyValue
        {
            get
            {
                // 적용하지 않은 값이 있으면 다시 읽지 않는다(Update가 그 값을 버린다).
                if (!_property.serializedObject.hasModifiedProperties) _property.serializedObject.Update();
                return Describe(_property, _field).empty;
            }
        }

        GsField Field => GetFirstAncestorOfType<GsField>();

        // 목록은 입력 칸 전체 아래에 펼치고, 열려 있는 동안 '편집 중'으로 보인다.
        GsDropdown OpenMenu(IEnumerable<GsDropdownItem> items, bool? searchable = null, string placeholder = "검색")
        {
            var field = Field;
            var menu = GsDropdown.Show(field?.Box ?? (VisualElement)this, items, searchable, placeholder);
            field?.TrackMenu(menu);
            return menu;
        }

        ScrollView _valueScroll;

        // 긴 값은 칸이 늘어나며 최대 높이까지 보이고, 그 이상은 칸 안에서 스크롤한다.
        public void CapHeight(float maxHeight)
        {
            if (_valueScroll != null) return;
            _valueScroll = new ScrollView(ScrollViewMode.Vertical);
            _valueScroll.AddToClassList("gs-inline__scroll");
            _valueScroll.style.maxHeight = maxHeight;
            _valueScroll.style.flexGrow = 1;
            var index = IndexOf(_value);
            _value.RemoveFromHierarchy();
            _value.style.whiteSpace = WhiteSpace.Normal;
            _valueScroll.Add(_value);
            Insert(Math.Max(0, index), _valueScroll);
        }

        // 편집 열기(클릭과 같다). 비어 있는 입력 칸을 눌렀을 때도 쓴다.
        public void Open()
        {
            if (_editor != null || !IsEditable || DirectPicker != null) return;
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
                case SerializedPropertyType.String when Has<PlayerDataRefAttribute>():
                    ShowIdMenu(EditorDatabase.Instance.OfType<PlayerDataEntry>().Cast<GameData>(), "플레이어 데이터");
                    return;
                case SerializedPropertyType.String when Has<FieldValueRefAttribute>():
                    if (EditFieldValue()) return;
                    break;
                case SerializedPropertyType.String when IsStatRef:
                    ShowIdMenu(EditorDatabase.Instance.OfType<StatData>().Cast<GameData>(), "로그");
                    return;
                case SerializedPropertyType.String when IsAchievementRef:
                    ShowIdMenu(EditorDatabase.Instance.OfType<AchievementData>().Cast<GameData>(), "도전과제");
                    return;
                case SerializedPropertyType.String when IsEventTypeRef:
                    ShowEventTypeMenu();
                    return;
                case SerializedPropertyType.String when IsIntentTypeRef:
                    ShowIntentTypeMenu();
                    return;
                case SerializedPropertyType.String when EventFieldRef != null:
                    ShowEventFieldMenu();
                    return;
                case SerializedPropertyType.String when InlineChoices.For(_field) != null:
                    ShowProviderMenu(InlineChoices.For(_field));
                    return;
                case SerializedPropertyType.String when KeywordTools.GroupOf(_field) != null:
                    ShowKeywordMenu();
                    return;
                case SerializedPropertyType.String when DataRefType != null:
                    ShowIdMenu(EditorDatabase.Instance.RefCandidates(_field), DataTypes.Find(DataRefType)?.DisplayName ?? DataRefType.Name);
                    return;
                case SerializedPropertyType.String when Has<ClassTypeRefAttribute>():
                    ShowTypeList(ClassTypes.All(), ClassTypes.Label, ClassTypes.GroupOf, "클래스 검색");
                    return;
                case SerializedPropertyType.String when Has<GameDataTypeRefAttribute>():
                    ShowTypeList(DataTypes.All.Select(i => i.Type), t => DataTypes.Find(t)?.DisplayName ?? t.Name, t => DataTypes.Find(t)?.Group, "DB 종류 검색");
                    return;
                case SerializedPropertyType.String when Has<MemberRefAttribute>():
                    ShowMemberMenu();
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
            OpenMenu(items);
        }

        // 선택 모듈(다이얼로그 시스템 등)이 등록한 목록.
        void ShowProviderMenu(IInlineChoiceProvider provider)
        {
            var items = provider.Items(_property, _field, SetString).ToList();
            if (items.Count == 0) items.Add(new GsDropdownItem("고를 항목이 없습니다") { Disabled = true });
            OpenMenu(items, placeholder: provider.Placeholder);
        }
        void ShowIdMenu(IEnumerable<GameData> data, string kind)
        {
            var db = EditorDatabase.Instance;
            var list = data.OrderBy(d => db.GroupLabel(d) ?? "").ThenBy(d => CodexGroups.OrderOf(d)).ThenBy(d => d.Id).ToList();
            var items = list.Select(d =>
            {
                var id = d.Id;
                return new GsDropdownItem(id, () => SetString(id)) { Group = db.GroupLabel(d), Selected = _property.stringValue == id };
            }).ToList();
            if (items.Count == 0) items.Add(new GsDropdownItem($"DB › {kind} 탭에 아직 항목이 없습니다 — 먼저 만드세요") { Disabled = true });
            OpenMenu(items, placeholder: $"{kind} 검색");
        }

        // 키워드 필드([Keyword(typeof(그룹))]): 그 그룹의 키워드에서 고른다.
        void ShowKeywordMenu()
        {
            var group = KeywordTools.GroupOf(_field);
            var items = KeywordTools.KeywordsOf(group).Select(k =>
            {
                var id = k.Id;
                return new GsDropdownItem(id, () => SetString(id)) { Caption = k.Name, Selected = _property.stringValue == id };
            }).ToList();
            items.Insert(0, new GsDropdownItem("(비움)", () => SetString("")) { Selected = string.IsNullOrEmpty(_property.stringValue) });
            if (items.Count == 1) items.Add(new GsDropdownItem("이 그룹에 아직 키워드가 없습니다") { Disabled = true });
            items.AddRange(GsPropertyEditor.KeywordMenuExtras(_field));
            OpenMenu(items, placeholder: "키워드 검색");
        }

        void ShowEventTypeMenu()
        {
            var items = ProjectCode.Events().OrderBy(EventBus.DisplayNameOf).Select(type =>
            {
                var name = type.FullName;
                return new GsDropdownItem(EventBus.DisplayNameOf(type), () => SetString(name)) { Caption = type.Name, Selected = _property.stringValue == name };
            }).ToList();
            if (items.Count == 0) items.Add(new GsDropdownItem("[GameEvent]가 붙은 이벤트 타입이 없습니다") { Disabled = true });
            OpenMenu(items, placeholder: "이벤트 검색");
        }

        void ShowIntentTypeMenu()
        {
            var items = ProjectCode.Intents().OrderBy(Intents.DisplayNameOf).Select(type =>
            {
                var name = type.FullName;
                return new GsDropdownItem(Intents.DisplayNameOf(type), () => SetString(name)) { Caption = type.Name, Selected = _property.stringValue == name };
            }).ToList();
            if (items.Count == 0) items.Add(new GsDropdownItem("[Intent]가 붙은 의도 형식이 없습니다") { Disabled = true });
            OpenMenu(items, placeholder: "의도 검색");
        }

        void ShowEventFieldMenu()
        {
            var items = new List<GsDropdownItem>
            {
                new GsDropdownItem("횟수 (필드 없음)", () => SetString("")) { Detail = "이벤트가 한 번 받을 때마다 1", Selected = string.IsNullOrEmpty(_property.stringValue) },
            };
            var eventType = EventTypeFor(_property, EventFieldRef);
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
            OpenMenu(items);
        }
        // ── 필드 검사(경로·비교·값) ───────────────────────────

        void PickFieldPath()
        {
            var root = FieldPathContext.RootType(_property, out var rootLabel);
            var host = GetFirstAncestorOfType<GsModal>()?.parent ?? panel?.visualTree.Q(className: "gs-root") ?? this;
            if (root == null)
            {
                OpenMenu(new List<GsDropdownItem> { new GsDropdownItem("이벤트나 플레이어 데이터를 먼저 고르세요") { Disabled = true } });
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
            OpenMenu(items);
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
            var dataRef = LeafDataRef(_property, attr, out var leafField);
            if (dataRef != null)
            {
                ShowIdMenu(EditorDatabase.Instance.RefCandidates(leafField), DataTypes.Find(dataRef)?.DisplayName ?? dataRef.Name);
                return true;
            }
            if (type != null && type.IsEnum)
            {
                var items = Enum.GetNames(type).Select(name => new GsDropdownItem(name, () => SetString(name)) { Selected = _property.stringValue == name }).ToList();
                OpenMenu(items);
                return true;
            }
            return false;
        }

        // 경로 끝 필드가 ID 참조([DataRef]·필드 규칙)면 그 데이터 타입(예: 태그 목록 → 태그).
        static Type LeafDataRef(SerializedProperty valueProperty, FieldValueRefAttribute attr) => LeafDataRef(valueProperty, attr, out _);

        static Type LeafDataRef(SerializedProperty valueProperty, FieldValueRefAttribute attr, out FieldInfo leaf) =>
            LeafDataRef(valueProperty, attr.PathField, out leaf);

        static Type LeafDataRef(SerializedProperty valueProperty, string pathField, out FieldInfo leaf)
        {
            leaf = null;
            var path = valueProperty.serializedObject.FindProperty(FieldPathContext.Sibling(valueProperty.propertyPath, pathField));
            if (path == null) return null;
            FieldPathContext.LeafType(path, out var member);
            leaf = member as FieldInfo;
            return EditorDatabase.RefTypeOf(leaf);
        }

        void ShowTypeList(IEnumerable<Type> types, Func<Type, string> label, Func<Type, string> group, string placeholder)
        {
            var items = types.Select(type =>
            {
                var name = type.FullName;
                return new GsDropdownItem(label(type), () => SetString(name)) { Group = group(type), Caption = type.Namespace, Selected = _property.stringValue == name };
            }).ToList();
            OpenMenu(items, placeholder: placeholder);
        }

        // 옆 칸의 클래스가 가진 필드 목록(필드 규칙의 '필드' 칸).
        void ShowMemberMenu()
        {
            var attr = _field.GetCustomAttribute<MemberRefAttribute>();
            var owner = TypeResolver.Find(Sibling(attr.TypeField)?.stringValue);
            var items = new List<GsDropdownItem>();
            if (owner == null) items.Add(new GsDropdownItem("클래스를 먼저 고르세요") { Disabled = true });
            else
                foreach (var member in ClassTypes.FieldsOf(owner))
                {
                    var name = member.Name;
                    items.Add(new GsDropdownItem(GsDataExplorer.LabelOf(member, name), () => SetString(name))
                    {
                        Caption = $"{name} · {DataModel.FriendlyName(member.FieldType)}",
                        Selected = _property.stringValue == name,
                    });
                }
            OpenMenu(items, placeholder: "필드 검색");
        }

        // 스프라이트 규격 그룹이 걸린 필드에 원본을 넣으면 맞춤 창을 띄운다.
        void OpenFramingIfRuled(UnityEngine.Object value)
        {
            var rule = SpriteSpecEditor.RuleFor(_field);
            if (rule == null || !(value is Sprite sprite)) return;
            var host = panel?.visualTree.Q(className: "gs-root");
            if (host != null) schedule.Execute(() => GsSpriteFraming.Open(host, sprite, rule.spriteGroup, Refresh)).StartingIn(50);
        }

        void ShowStorableTypeMenu()
        {
            var instanceField = _field.GetCustomAttribute<DataTypeRefAttribute>()?.InstanceField;
            // 형식을 고르면 옆 칸([SerializeReference])에 새 인스턴스를 넣는 칸이면 클래스만 보인다(기본 형식·enum은 넣을 수 없다).
            var types = string.IsNullOrEmpty(instanceField) ? StorableTypes.All() : StorableTypes.All().Where(t => !t.IsValueType && t != typeof(string));
            var items = types.Select(type =>
            {
                var name = type.FullName;
                return new GsDropdownItem(StorableTypes.Label(type), () =>
                {
                    if (string.IsNullOrEmpty(instanceField))
                    {
                        SetString(name);
                        return;
                    }
                    // 형식을 고르면 그 형식의 새 인스턴스를 옆 칸에 넣는다(인스턴스). 같은 형식이면 기존 값을 둔다.
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
            if (items.Count == 0) items.Add(new GsDropdownItem("고를 수 있는 데이터 클래스가 없습니다(AI가 코드로 만듭니다)") { Disabled = true });
            OpenMenu(items, placeholder: "형식 검색");
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
            Field?.SetEditing(true);
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
            _editor.RegisterCallback<FocusOutEvent>(_ => schedule.Execute(() =>
            {
                if (_editor != null && !IsFocusWithin(_editor)) EndEdit(true);
            }).StartingIn(30));

            // 바깥을 클릭하면 편집을 끝낸다.
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
                    if (Multiline)
                    {
                        // 긴 글은 칸이 늘어나며 10줄까지, 그 이상은 칸 안에서 스크롤.
                        text.style.whiteSpace = WhiteSpace.Normal;
                        text.style.maxHeight = GsField.MaxValueHeight;
                        text.verticalScrollerVisibility = ScrollerVisibility.Auto;
                    }
                    return text;
                case SerializedPropertyType.Integer:
                    return new LongField { value = _property.longValue };
                case SerializedPropertyType.Float:
                    return _property.type == "double"
                        ? (VisualElement)new DoubleField { value = _property.doubleValue }
                        : new FloatField { value = _property.floatValue };
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
            // 값을 먼저 적용한 뒤에 칸에 알린다: 칸이 빈 값인지 다시 읽을 때(serializedObject.Update) 아직 적용하지 않은 값이 버려지지 않게.
            if (commit) Apply();
            else Refresh();
            Field?.SetEditing(false);
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
