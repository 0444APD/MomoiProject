using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameSystems.EditorTools.UI
{
    public sealed class DetailOptions
    {
        // 제목으로 쓸 필드(패널 위쪽에 크게, 편집 가능).
        public string TitlePath = "displayName";
        // TitlePath가 없을 때 보일 제목. TitleCommit이 있으면 제목을 눌러 고칠 수 있다(도감: ID 바꾸기).
        public string Title;
        // 새 제목 검사(문제가 있으면 문구, 없으면 null)와 반영.
        public Func<string, string> TitleValidate;
        public Action<GsDetailPanel, string> TitleCommit;
        // 위쪽 왼편 이미지 칸을 보일지(도감 항목은 쓰지 않는다. 이미지 필드는 본문에 나온다).
        public bool ShowMedia = true;
        // 클래스·배열·리스트 필드에 들어갈 때 따로 여는 방법(상세 창의 새 탭). true면 패널 안에서 들어가지 않는다.
        public Func<ExplorerItem, bool> OpenNested;
        public string Subtitle;
        // 이미지로 쓸 필드 경로. 지정하면 이미지를 클릭해 바꿀 수 있고, 그 필드는 정보 영역에 따로 나오지 않는다.
        public string ImagePath;
        // 필드가 아닌 곳에서 오는 이미지(읽기 전용).
        public Func<SerializedObject, UnityEngine.Object> Image;
        public Color Placeholder = Gs.Palette[6];
        public HashSet<string> Hidden = new HashSet<string>();
        // 이 필드로 스크롤하고 강조한다(시트에서 셀을 더블클릭해 연 경우).
        public string FocusPath;
        // 편집 중 사본을 검증해 (오류 여부, 문장) 목록을 돌려준다.
        public Func<UnityEngine.Object, List<(bool error, string message)>> Validate;
        // 제목 아래 요약: (머리말, 내용) 줄 목록. 예: ("조건", "처치 수 >= 1"), ("보상", "골드 50 획득").
        // 편집 중 사본으로 계산해 바로 갱신된다.
        public Func<UnityEngine.Object, IList<(string label, string text)>> Summary;
        public List<(string label, Action<GsDetailPanel> action)> Actions = new List<(string, Action<GsDetailPanel>)>();
        public Action<UnityEngine.Object> Saved;
        // 제목 옆 "?" 버튼이 여는 도움말 항목.
        public string HelpTopic;
        // 도감처럼 변경을 모아 두는 곳: 저장 확인 없이 작업본에 바로 반영하고, Unity 적용은 탭을 옮길 때 한다.
        public bool Staged;
    }

    // 모든 카드·시트가 공유하는 상세 보기·편집 패널.
    // 편집은 이 패널 안에서만, 원본이 아닌 사본에 한다. 되돌리기·저장은 확인을 거친다.
    public sealed class GsDetailPanel : VisualElement
    {
        readonly VisualElement _host;
        readonly UnityEngine.Object _target;
        readonly DetailOptions _options;
        readonly UnityEngine.Object _draft;
        readonly SerializedObject _draftObject;
        SerializedObject _originalObject;
        GsModal _modal;
        List<FieldChange> _changes = new List<FieldChange>();

        VisualElement _media;
        Label _initial;
        VisualElement _titleSlot;
        ScrollView _body;
        VisualElement _notices;
        VisualElement _summary;
        Label _status;
        GsButton _revert;
        GsButton _save;
        // 필드 경로 → 그 필드의 칸(바뀐 칸 표시·찾아가기).
        readonly Dictionary<string, VisualElement> _sections = new Dictionary<string, VisualElement>();

        public UnityEngine.Object Target => _target;
        internal SerializedObject Draft => _draftObject;
        internal IReadOnlyList<FieldChange> Changes => _changes;

        GsDetailPanel(VisualElement host, UnityEngine.Object target, DetailOptions options)
        {
            _host = host;
            _target = target;
            _options = options ?? new DetailOptions();
            _draft = UnityEngine.Object.Instantiate(target);
            _draft.name = target.name;
            _draft.hideFlags = HideFlags.HideAndDontSave;
            _draftObject = new SerializedObject(_draft);
            _originalObject = new SerializedObject(target);
            AddToClassList("gs-detail");
            Build();
        }

        // 창 안에 붙여 쓰는 패널(모달 아님). 닫기를 누르면 CloseRequested가 온다.
        public static GsDetailPanel Create(VisualElement host, UnityEngine.Object target, DetailOptions options = null)
        {
            var panel = new GsDetailPanel(host, target, options);
            panel.AddToClassList("gs-detail--docked");
            return panel;
        }

        public event Action CloseRequested;
        // 본문을 다시 그렸다(안쪽 값 객체가 바뀌었을 수 있다 → 따로 연 안쪽 탭은 닫는다).
        public event Action BodyRebuilt;

        public static GsDetailPanel Open(VisualElement host, UnityEngine.Object target, DetailOptions options = null)
        {
            var panel = new GsDetailPanel(host, target, options);
            panel._modal = GsModal.Show(host, panel);
            panel._modal.RequestClose = panel.RequestClose;
            panel._modal.Closed += panel.DestroyDraft;
            panel.Fit();
            EventCallback<GeometryChangedEvent> fit = _ => panel.Fit();
            host.RegisterCallback(fit);
            panel._modal.Closed += () => host.UnregisterCallback(fit);
            return panel;
        }

        // 창 크기에 맞춰 넉넉한 세로 크기.
        void Fit()
        {
            // 좁고 긴 패널: 폭은 좁게 고정, 높이는 창에 맞춰 길게(내용은 스크롤).
            var width = Mathf.Min(480f, Mathf.Max(360f, _host.layout.width - 32f));
            var height = Mathf.Max(420f, _host.layout.height - 40f);
            style.width = width;
            style.height = height;
        }

        // 위: [정사각형 이미지] [제목 · 정보 · 요약]   아래: 전체 폭 정보 영역   맨 아래: 되돌리기 · 저장
        void Build()
        {
            var top = Gs.Box("gs-detail__top");
            Add(top);

            _media = Gs.Box("gs-detail__media");
            _initial = Gs.Label("", "gs-detail__initial");
            _initial.pickingMode = PickingMode.Ignore;
            _media.Add(_initial);
            top.Add(_media);
            if (!_options.ShowMedia) _media.style.display = DisplayStyle.None;
            BuildImagePicker();

            var titles = Gs.Col("gs-grow");
            titles.style.minWidth = 0;
            _titleSlot = Gs.Col();
            titles.Add(_titleSlot);
            if (!string.IsNullOrEmpty(_options.Subtitle))
            {
                var subtitle = Gs.Label(_options.Subtitle, "gs-caption");
                subtitle.style.marginTop = 6;
                titles.Add(subtitle);
            }
            _summary = Gs.Box("gs-summary");
            titles.Add(_summary);
            top.Add(titles);

            // 오른쪽 위: 도움말 · 닫기를 나란히.
            var corner = Gs.Row("gs-detail__corner");
            if (!string.IsNullOrEmpty(_options.HelpTopic)) corner.Add(HelpUI.Button(_options.HelpTopic));
            var close = new GsButton("✕", RequestClose, GsButtonKind.Ghost);
            close.tooltip = "닫기 (Esc)";
            corner.Add(close);
            top.Add(corner);

            _body = new ScrollView(ScrollViewMode.Vertical);
            _body.AddToClassList("gs-detail__body");
            _body.contentContainer.style.paddingLeft = 16;
            _body.contentContainer.style.paddingRight = 16;
            _body.contentContainer.style.paddingTop = 16;
            _body.contentContainer.style.paddingBottom = 8;
            Add(_body);

            var footer = Gs.Box("gs-detail__footer");
            _status = Gs.Label("", "gs-caption");
            footer.Add(_status);
            footer.Add(Gs.HSpace(12));
            foreach (var (label, action) in _options.Actions)
            {
                var a = action;
                footer.Add(new GsButton(label, () => a(this), GsButtonKind.Ghost));
            }
            footer.Add(Gs.Spacer());
            _revert = new GsButton("되돌리기", Revert, GsButtonKind.Ghost);
            footer.Add(_revert);
            footer.Add(Gs.HSpace(8));
            _save = new GsButton(_options.Staged ? "반영" : "저장", Save, GsButtonKind.Primary);
            if (_options.Staged) _save.tooltip = "도감 목록에 바로 반영합니다. Unity 에셋에는 탭을 옮길 때 변경 목록을 확인하고 적용합니다.";
            footer.Add(_save);
            if (_options.Staged)
            {
                // 바꾼 값은 바로 반영되므로 되돌리기·반영 버튼이 필요 없다(되돌리기는 도감 탭을 옮길 때 한꺼번에).
                _revert.style.display = DisplayStyle.None;
                _save.style.display = DisplayStyle.None;
            }
            Add(footer);

            BuildBody();
        }

        ObjectFieldProxy _picker;

        // 이미지 필드가 있으면 이미지를 클릭해 바꾼다. Unity 오브젝트 선택 창을 그대로 쓰기 위해
        // 투명한 ObjectField의 선택 버튼을 이미지 전체에 덮는다(끌어다 놓기도 된다).
        void BuildImagePicker()
        {
            var path = _options.ImagePath;
            var property = string.IsNullOrEmpty(path) ? null : _draftObject.FindProperty(path);
            if (property == null || property.propertyType != SerializedPropertyType.ObjectReference) return;
            _options.Hidden.Add(path);
            PropertyUtil.GetField(property, out var type);

            _picker = new ObjectFieldProxy(type ?? typeof(Sprite));
            _picker.Changed += value =>
            {
                _draftObject.Update();
                _draftObject.FindProperty(path).objectReferenceValue = value;
                _draftObject.ApplyModifiedPropertiesWithoutUndo();
                RefreshState();
            };
            _media.Add(_picker.Element);
            _media.AddToClassList("gs-detail__media--editable");
            var hint = Gs.Box("gs-detail__media-hint");
            hint.pickingMode = PickingMode.Ignore;
            var hintLabel = Gs.Label("변경");
            hintLabel.pickingMode = PickingMode.Ignore;
            hint.Add(hintLabel);
            _media.Add(hint);
            _media.tooltip = "클릭해서 이미지 바꾸기 (끌어다 놓기도 됨)";
        }

        UnityEngine.Object CurrentImage()
        {
            if (!string.IsNullOrEmpty(_options.ImagePath))
                return _draftObject.FindProperty(_options.ImagePath)?.objectReferenceValue;
            return _options.Image?.Invoke(_draftObject);
        }
        void BuildBody()
        {
            _draftObject.Update();
            _sections.Clear();
            _titleSlot.Clear();
            _body.Clear();

            var titleProperty = string.IsNullOrEmpty(_options.TitlePath) ? null : _draftObject.FindProperty(_options.TitlePath);
            if (titleProperty != null)
            {
                var title = new GsInlineProperty(titleProperty);
                title.Q<Label>().AddToClassList("gs-display");
                title.Changed += OnValueChanged;
                _titleSlot.Add(Track(titleProperty.propertyPath, title, _titleSlot));
            }
            else if (!string.IsNullOrEmpty(_options.Title))
            {
                // 고칠 수 없는 제목(도감 항목의 ID. ID는 'ID 바꾸기'로만 바꾼다).
                _titleSlot.Add(_options.TitleCommit != null ? EditableTitle() : Gs.Label(_options.Title, "gs-display"));
            }

            _notices = Gs.Col();
            _body.Add(_notices);

            // 본문은 타입별 배치(그리드 + 블록)로 그린다. 기본 배치: 스프라이트 나열 + 데이터 나열.
            RememberReferences();
            _body.Add(new GsDataView(new DetailContext(this)));

            RefreshState();

            if (!string.IsNullOrEmpty(_options.FocusPath))
            {
                var path = _options.FocusPath;
                _options.FocusPath = null;
                schedule.Execute(() => FocusField(path)).StartingIn(60);
            }
            BodyRebuilt?.Invoke();
        }

        // 필드 한 칸(이름 + 값). 단순 값은 이전/현재 표시가 붙은 인라인 편집, 조건·목록은 문장 변경 표시 + 편집기.
        VisualElement BuildSection(SerializedProperty property)
        {
            var path = property.propertyPath;
            var field = PropertyUtil.GetField(property, out _);
            var section = Gs.Box("gs-detail__section");
            var label = GsPropertyEditor.LabelOf(property, field);
            var content = UsesExplorer(property, field) ? EmbeddedExplorer(property, field) : GsPropertyEditor.BuildContent(property, OnValueChanged, OnStructureChanged);
            if (content is GsInlineProperty inline)
            {
                // 값 하나: 입력 칸 모양(상자 안 위쪽 이름표, 아래 값). 바뀐 뒤 값만 보인다.
                var box = GsField.For(label, inline, Track(path, inline, section));
                box.tooltip = GsPropertyEditor.HintOf(field);
                section.Add(box);
            }
            else
            {
                // 클래스·배열·리스트: 변수 이름 + 형식(예: nestedList · SampleFieldNested 리스트).
                var head = Gs.Row("gs-detail__section-head");
                head.Add(Gs.Label(label, "gs-detail__label"));
                var caption = field != null ? DataModel.TypeCaption(field.FieldType) : "";
                if (!string.IsNullOrEmpty(caption)) head.Add(Gs.Label(caption, "gs-detail__type"));
                head.tooltip = GsPropertyEditor.HintOf(field);
                section.Add(head);
                section.Add(content);
                _sections[path] = section;
            }
            return section;
        }

        // 클래스·컬렉션 필드는 탐색기로 연다(도감·모니터와 같은 모양). 조건·보상 같은 블록 트리는 문장 편집기를 쓴다.
        static bool UsesExplorer(SerializedProperty property, System.Reflection.FieldInfo field)
        {
            if (field == null) return false;
            if (property.propertyType == SerializedPropertyType.ManagedReference) return !IsBlockType(field.FieldType);
            if (property.propertyType != SerializedPropertyType.Generic) return false;
            var type = field.FieldType;
            var element = FieldPaths.ElementTypeOf(type);
            if (element != null)
                return !PropertyUtil.IsManagedReferenceList(field) && !ContainsBlocks(element, 0) && !typeof(UnityEngine.Object).IsAssignableFrom(element);
            return property.hasVisibleChildren && !ContainsBlocks(type, 0);
        }

        static bool IsBlockType(Type type) =>
            typeof(Condition).IsAssignableFrom(type) || typeof(Reward).IsAssignableFrom(type) || typeof(ValueFilter).IsAssignableFrom(type);

        static bool ContainsBlocks(Type type, int depth)
        {
            if (type == null || depth > 4) return false;
            if (IsBlockType(type)) return true;
            var element = FieldPaths.ElementTypeOf(type);
            if (element != null) return ContainsBlocks(element, depth + 1);
            if (DataModel.Of(type).Kind != DataKind.Object) return false;
            return FieldPaths.MembersOf(type).Any(m => ContainsBlocks(m.Type, depth + 1));
        }

        // 사본 객체의 필드 값을 탐색기로 연다. 고치면 사본에 반영되고 변경 표시·저장 흐름을 그대로 탄다.
        VisualElement EmbeddedExplorer(SerializedProperty property, System.Reflection.FieldInfo field)
        {
            var value = field.GetValue(_draft);
            if (value == null)
            {
                if (property.propertyType == SerializedPropertyType.ManagedReference)
                    return Gs.Label("비어 있습니다 — 위에서 형식을 고르면 만들어집니다.", "gs-caption");
                try
                {
                    value = DataModel.Create(field.FieldType);
                    field.SetValue(_draft, value);
                }
                catch (Exception)
                {
                    return Gs.Label("비어 있습니다", "gs-caption");
                }
            }
            var explorer = GsDataExplorer.Browse(new ExplorerItem
            {
                Label = GsPropertyEditor.LabelOf(property, field),
                Value = value,
                Member = field,
                Apply = change =>
                {
                    change();
                    _draftObject.Update();
                    OnValueChanged();
                },
            });
            explorer.AddToClassList("gs-explorer--embedded");
            explorer.OpenInTab = _options.OpenNested;
            return explorer;
        }

        // 상세 패널의 데이터(편집 중 사본)를 배치 블록에 넘겨준다.
        sealed class DetailContext : DataViewContext
        {
            readonly GsDetailPanel _panel;

            public DetailContext(GsDetailPanel panel)
            {
                _panel = panel;
            }

            // 좁은 패널: 블록을 옆으로 나누지 않고 한 줄씩 쌓는다.
            public override bool SingleColumn => true;

            public override Type Type => _panel._draft.GetType();
            public override bool CanEdit => true;
            public override UnityEngine.Object Target => _panel._target;

            public override IEnumerable<string> Fields() =>
                PropertyUtil.TopLevel(_panel._draftObject)
                    .Select(p => p.propertyPath)
                    .Where(p => p != _panel._options.TitlePath && !_panel._options.Hidden.Contains(p))
                    .ToList();

            public override VisualElement FieldRow(string path)
            {
                var property = _panel._draftObject.FindProperty(path);
                return property == null ? new VisualElement() : _panel.BuildSection(property);
            }

            public override VisualElement FieldValue(string path)
            {
                var property = _panel._draftObject.FindProperty(path);
                if (property == null) return Gs.Label($"없는 필드: {path}", "gs-caption");
                var isLeaf = !(property.propertyType == SerializedPropertyType.ManagedReference ||
                               (property.propertyType == SerializedPropertyType.Generic && (property.isArray || property.hasVisibleChildren)));
                if (!isLeaf) return Gs.Label(PropertyUtil.Summary(property), "gs-nowrap");
                var inline = new GsInlineProperty(property);
                inline.Changed += _panel.OnValueChanged;
                return inline;
            }

            public override UnityEngine.Object ObjectAt(string path)
            {
                // 배치의 스프라이트 블록이 스프라이트가 아닌 필드를 가리킬 수 있다(배치를 바꾼 뒤 필드 형식이 바뀐 경우 등).
                var property = _panel._draftObject.FindProperty(path);
                return property != null && property.propertyType == SerializedPropertyType.ObjectReference ? property.objectReferenceValue : null;
            }

            public override void SetObject(string path, UnityEngine.Object value)
            {
                _panel._draftObject.Update();
                var property = _panel._draftObject.FindProperty(path);
                if (property == null) return;
                property.objectReferenceValue = value;
                _panel._draftObject.ApplyModifiedPropertiesWithoutUndo();
                _panel.OnStructureChanged();
            }
        }
        // 필드를 강조하고, 스크롤 영역 안에 있으면 그 위치로 스크롤한다.
        // 제목처럼 헤더에 있는 필드는 항상 보이므로 스크롤하지 않는다(ScrollView 밖 요소로 ScrollTo하면 예외).
        internal void FocusField(string path)
        {
            if (path == null || !_sections.TryGetValue(path, out var focus)) return;
            var target = focus;
            target.AddToClassList("gs-detail__section--changed");
            if (_body.panel != null && _body.contentContainer.Contains(target)) _body.ScrollTo(target);
            schedule.Execute(() => RefreshState()).StartingIn(1600);
        }

        // 단순 값 칸을 기록한다(바뀐 칸 표시·찾아가기). 화면에는 바뀐 뒤 값만 보인다.
        VisualElement Track(string path, GsInlineProperty inline, VisualElement owner)
        {
            inline.Multiline = true;
            _sections[path] = owner;
            return inline;
        }

        // 편집 중인 사본(도감이 ID를 바꿀 때 사본의 ID도 맞춘다).
        public UnityEngine.Object DraftTarget => _draft;

        // 고칠 수 있는 제목: 평소엔 큰 글자, 마우스를 올리면 상자, 누르면 입력 칸. Enter·바깥 클릭으로 확정, Esc로 취소.
        VisualElement EditableTitle()
        {
            var box = Gs.Col("gs-detail__title-edit");
            var label = Gs.Label(_options.Title, "gs-display");
            var error = Gs.Label("", "gs-detail__title-error");
            error.style.display = DisplayStyle.None;
            box.Add(label);
            box.tooltip = "눌러서 바꾸기";
            var wrap = Gs.Col();
            wrap.Add(box);
            wrap.Add(error);
            TextField input = null;

            void ShowError(string message)
            {
                error.text = message ?? "";
                error.style.display = string.IsNullOrEmpty(message) ? DisplayStyle.None : DisplayStyle.Flex;
                box.EnableInClassList("gs-detail__title-edit--error", !string.IsNullOrEmpty(message));
            }

            void Finish(bool commit)
            {
                if (input == null) return;
                var value = input.value?.Trim() ?? "";
                if (commit && value != _options.Title)
                {
                    var problem = _options.TitleValidate?.Invoke(value);
                    if (problem != null)
                    {
                        ShowError(problem);
                        input.schedule.Execute(() => input?.Focus());
                        return;
                    }
                    _options.TitleCommit(this, value);
                    _options.Title = value;
                    label.text = value;
                }
                ShowError(null);
                input.RemoveFromHierarchy();
                input = null;
                label.style.display = DisplayStyle.Flex;
                box.RemoveFromClassList("gs-detail__title-edit--editing");
            }

            box.RegisterCallback<ClickEvent>(_ =>
            {
                if (input != null) return;
                input = new TextField { value = _options.Title };
                input.AddToClassList("gs-detail__title-input");
                label.style.display = DisplayStyle.None;
                box.Insert(0, input);
                box.AddToClassList("gs-detail__title-edit--editing");
                input.RegisterCallback<KeyDownEvent>(e =>
                {
                    if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                    {
                        e.StopPropagation();
                        Finish(true);
                    }
                    else if (e.keyCode == KeyCode.Escape)
                    {
                        e.StopPropagation();
                        ShowError(null);
                        Finish(false);
                    }
                }, TrickleDown.TrickleDown);
                // 바깥을 누르면 확정. 문제가 있으면 원래 값으로 돌리고 문구만 남긴다.
                input.RegisterCallback<FocusOutEvent>(_ => input?.schedule.Execute(() =>
                {
                    if (input == null) return;
                    var focused = input.focusController?.focusedElement as VisualElement;
                    if (focused != null && (focused == input || input.Contains(focused))) return;
                    var value = input.value?.Trim() ?? "";
                    var problem = value == _options.Title ? null : _options.TitleValidate?.Invoke(value);
                    if (problem != null)
                    {
                        Finish(false);
                        ShowError(problem);
                    }
                    else Finish(true);
                }).StartingIn(30));
                input.schedule.Execute(() =>
                {
                    input?.Focus();
                    input?.SelectAll();
                });
            });
            return wrap;
        }

        // 관리 참조 필드(템플릿 값 등)가 새 인스턴스로 바뀌면 본문을 다시 그린다(새 값의 탐색기를 보이려고).
        readonly Dictionary<string, long> _referenceIds = new Dictionary<string, long>();

        void RememberReferences()
        {
            _referenceIds.Clear();
            foreach (var property in PropertyUtil.TopLevel(_draftObject))
                if (property.propertyType == SerializedPropertyType.ManagedReference) _referenceIds[property.propertyPath] = property.managedReferenceId;
        }

        void OnValueChanged()
        {
            _draftObject.Update();
            foreach (var property in PropertyUtil.TopLevel(_draftObject))
            {
                if (property.propertyType != SerializedPropertyType.ManagedReference) continue;
                if (_referenceIds.TryGetValue(property.propertyPath, out var id) && id == property.managedReferenceId) continue;
                OnStructureChanged();
                return;
            }
            RefreshState();
            ReflectIfStaged();
        }

        // 도감(변경 모아 두기): 바꾼 값은 바로 작업본에 반영한다(확인·저장 단계 없음). Unity 적용은 도감 탭을 옮길 때.
        void ReflectIfStaged()
        {
            if (!_options.Staged || _draft == null || _target == null) return;
            if (_changes == null || _changes.Count == 0) RefreshState();
            if (_changes != null && _changes.Count > 0) SaveNow();
        }

        void OnStructureChanged()
        {
            var scroll = _body.scrollOffset;
            BuildBody();
            schedule.Execute(() => _body.scrollOffset = scroll);
            ReflectIfStaged();
        }

        internal void RefreshState()
        {
            // 닫혔거나(사본 삭제) 대상이 사라졌으면(적용·되돌리기로 작업본이 없어짐) 아무것도 하지 않는다. 대상이 사라졌으면 패널도 닫는다.
            if (_draft == null) return;
            if (_target == null)
            {
                // 모달은 닫는다. 창에 붙은 패널은 창이 같은 ID로 다시 연다(상세 창의 Rebind).
                if (_modal != null) CloseNow();
                return;
            }
            _changes = DraftDiff.Compute(_originalObject, _draftObject);
            var changed = new HashSet<string>(_changes.Select(c => c.Path));

            foreach (var pair in _sections)
            {
                var isChanged = changed.Contains(pair.Key);
                // 바뀐 칸은 이름표 색으로만 알린다(이전 값은 보이지 않는다).
                pair.Value.EnableInClassList("gs-detail__section--changed", isChanged);
            }

            var count = _changes.Count;
            _status.text = _options.Staged ? "바꾼 값은 바로 반영 · Unity 적용은 도감 탭을 옮길 때"
                : count == 0 ? "변경 없음" : $"변경 {count}개 · 저장 전";
            _status.EnableInClassList("gs-accent-text", count > 0);
            _revert.SetEnabled(count > 0);
            _save.SetEnabled(count > 0);

            var image = CurrentImage();
            _picker?.SetValue(image);
            if (image is Sprite sprite) _media.style.backgroundImage = new StyleBackground(sprite);
            else if (image is Texture2D texture) _media.style.backgroundImage = new StyleBackground(texture);
            else
            {
                _media.style.backgroundImage = StyleKeyword.Null;
                _media.style.backgroundColor = Gs.Blend(Gs.Surface2, _options.Placeholder, 0.14f);
            }
            var title = string.IsNullOrEmpty(_options.TitlePath) ? null : _draftObject.FindProperty(_options.TitlePath);
            var titleText = title != null && title.propertyType == SerializedPropertyType.String ? title.stringValue : _options.Title;
            _initial.text = image == null && !string.IsNullOrEmpty(titleText) ? titleText.Substring(0, 1) : "";

            _summary.Clear();
            var lines = _options.Summary?.Invoke(_draft);
            if (lines != null)
            {
                foreach (var (label, text) in lines)
                {
                    var row = Gs.Box("gs-summary__row");
                    row.Add(Gs.Label(label, "gs-summary__label"));
                    row.Add(Gs.Label(text, "gs-summary__text"));
                    _summary.Add(row);
                }
            }
            _summary.style.display = _summary.childCount == 0 ? DisplayStyle.None : DisplayStyle.Flex;

            _notices.Clear();
            var issues = _options.Validate != null ? _options.Validate(_draft) : new List<(bool error, string message)>();
            foreach (var (error, message) in issues)
                _notices.Add(new GsNotice(message, error ? GsNoticeKind.Error : GsNoticeKind.Warning));
            // 오류 문구에 필드 이름이 들어 있으면 그 입력 칸에도 오류로 보인다(빨간 이름표·밑줄, 칸 아래 문구).
            foreach (var field in _body.Query<GsField>().ToList())
            {
                var match = issues.FirstOrDefault(i => i.error && !string.IsNullOrEmpty(field.Label) && i.message.Contains(field.Label));
                field.SetError(match.message);
            }
        }

        // ── 되돌리기 · 저장 · 닫기 ─────────────────────────────

        void Revert()
        {
            if (_changes.Count == 0) return;
            GsDialog.Show(_host, "변경 되돌리기", $"저장하지 않은 변경 {_changes.Count}개를 모두 되돌립니다.", _changes,
                new DialogButton("취소", GsButtonKind.Ghost, null),
                new DialogButton("되돌리기", GsButtonKind.Primary, RevertNow));
        }

        void Save()
        {
            if (_changes.Count == 0) return;
            if (_options.Staged)
            {
                SaveNow();
                return;
            }
            GsDialog.Confirm(_host, "변경 사항 저장", $"아래 {_changes.Count}개 항목을 저장합니다.", "저장", () => SaveNow(), _changes);
        }

        void RequestClose()
        {
            if (_changes.Count == 0)
            {
                CloseNow();
                return;
            }
            GsDialog.Show(_host, _options.Staged ? "반영하지 않은 변경" : "저장하지 않은 변경", $"변경 {_changes.Count}개가 {(_options.Staged ? "반영" : "저장")}되지 않았습니다.", _changes,
                new DialogButton("계속 편집", GsButtonKind.Ghost, null),
                new DialogButton("버리고 닫기", GsButtonKind.Default, () => CloseNow()),
                new DialogButton(_options.Staged ? "반영하고 닫기" : "저장하고 닫기", GsButtonKind.Primary, () =>
                {
                    SaveNow();
                    CloseNow();
                }));
        }

        internal void RevertNow()
        {
            var name = _target.name;
            EditorUtility.CopySerialized(_target, _draft);
            _draft.name = name;
            _draft.hideFlags = HideFlags.HideAndDontSave;
            BuildBody();
        }

        internal void SaveNow()
        {
            if (_changes.Count == 0) return;
            // CopySerialized는 이름·숨김 플래그까지 복사하므로 원본의 것을 지킨다(사본은 숨김 상태).
            var name = _target.name;
            var flags = _target.hideFlags;
            Undo.RecordObject(_target, "상세 편집 저장");
            EditorUtility.CopySerialized(_draft, _target);
            _target.name = name;
            _target.hideFlags = flags;
            EditorUtility.SetDirty(_target);
            if (AssetDatabase.Contains(_target)) AssetDatabase.SaveAssetIfDirty(_target);
            _originalObject = new SerializedObject(_target);
            _options.Saved?.Invoke(_target);
            RefreshState();
        }

        public void Close() => CloseNow();

        void CloseNow()
        {
            if (_modal != null)
            {
                _modal.Close();
                return;
            }
            DestroyDraft();
            CloseRequested?.Invoke();
        }

        void DestroyDraft()
        {
            if (_draft != null) UnityEngine.Object.DestroyImmediate(_draft);
        }
    }

    // 이미지 위에 투명하게 덮는 오브젝트 선택기(Unity 선택 창·끌어다 놓기를 그대로 쓴다).
    sealed class ObjectFieldProxy
    {
        readonly ObjectField _field;

        public event Action<UnityEngine.Object> Changed;

        public ObjectFieldProxy(Type type)
        {
            _field = new ObjectField { objectType = type, allowSceneObjects = false };
            _field.AddToClassList("gs-detail__picker");
            _field.RegisterValueChangedCallback(e => Changed?.Invoke(e.newValue));
        }

        public VisualElement Element => _field;

        public void SetValue(UnityEngine.Object value) => _field.SetValueWithoutNotify(value);
    }
}