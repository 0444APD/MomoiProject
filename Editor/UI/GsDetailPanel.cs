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
        // 제목 아래 요약: (머리말, 내용) 줄 목록. 예: ("조건", "처치 수 1 이상 달성 시"), ("보상", "골드 50 획득").
        // 편집 중 사본으로 계산해 바로 갱신된다.
        public Func<UnityEngine.Object, IList<(string label, string text)>> Summary;
        public List<(string label, Action<GsDetailPanel> action)> Actions = new List<(string, Action<GsDetailPanel>)>();
        public Action<UnityEngine.Object> Saved;
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
        readonly Dictionary<string, (VisualElement section, VisualElement change, Action<FieldChange> update)> _sections =
            new Dictionary<string, (VisualElement, VisualElement, Action<FieldChange>)>();

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
            var width = Mathf.Min(960f, Mathf.Max(700f, _host.layout.width - 64f));
            var height = Mathf.Clamp(width * 0.8f, 520f, Mathf.Max(520f, _host.layout.height - 48f));
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

            var close = new GsButton("✕", RequestClose, GsButtonKind.Ghost);
            close.tooltip = "닫기 (Esc)";
            top.Add(close);

            _body = new ScrollView(ScrollViewMode.Vertical);
            _body.AddToClassList("gs-detail__body");
            _body.contentContainer.style.paddingLeft = 24;
            _body.contentContainer.style.paddingRight = 24;
            _body.contentContainer.style.paddingTop = 20;
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
            _save = new GsButton("저장", Save, GsButtonKind.Primary);
            footer.Add(_save);
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

            var titleProperty = _draftObject.FindProperty(_options.TitlePath);
            if (titleProperty != null)
            {
                var title = new GsInlineProperty(titleProperty);
                title.Q<Label>().AddToClassList("gs-display");
                title.Changed += OnValueChanged;
                _titleSlot.Add(ValueWithHistory(titleProperty.propertyPath, title, _titleSlot, "gs-display"));
            }

            _notices = Gs.Col();
            _body.Add(_notices);

            foreach (var property in PropertyUtil.TopLevel(_draftObject))
            {
                var path = property.propertyPath;
                if (path == _options.TitlePath || _options.Hidden.Contains(path)) continue;

                var field = PropertyUtil.GetField(property, out _);
                var section = Gs.Box("gs-detail__section");
                section.Add(Gs.Label(GsPropertyEditor.LabelOf(property, field).ToUpperInvariant(), "gs-detail__label"));
                var content = GsPropertyEditor.BuildContent(property, OnValueChanged, OnStructureChanged);
                var isLeaf = content is GsInlineProperty;

                if (isLeaf)
                {
                    section.Add(ValueWithHistory(path, (GsInlineProperty)content, section, null));
                }
                else
                {
                    // 조건·목록: 위에 문장 단위 이전 / V / 현재, 아래 편집기
                    var change = new GsChangeView(null, "", "");
                    change.style.marginBottom = 10;
                    section.Add(change);
                    section.Add(content);
                    _sections[path] = (section, change, c => change.Set(c.Old, c.New));
                }
                _body.Add(section);
            }

            RefreshState();

            if (!string.IsNullOrEmpty(_options.FocusPath))
            {
                var path = _options.FocusPath;
                _options.FocusPath = null;
                schedule.Execute(() => FocusField(path)).StartingIn(60);
            }
        }

        // 필드를 강조하고, 스크롤 영역 안에 있으면 그 위치로 스크롤한다.
        // 제목처럼 헤더에 있는 필드는 항상 보이므로 스크롤하지 않는다(ScrollView 밖 요소로 ScrollTo하면 예외).
        internal void FocusField(string path)
        {
            if (path == null || !_sections.TryGetValue(path, out var focus)) return;
            var target = focus.section;
            target.AddToClassList("gs-detail__section--changed");
            if (_body.contentContainer.Contains(target)) _body.ScrollTo(target);
            schedule.Execute(() => RefreshState()).StartingIn(1600);
        }

        // 단순 값: 바뀌지 않았으면 값 칸만. 바뀌면 원래 자리에 같은 크기·같은 글자 위치의 [이전] 칸이 남고,
        // 그 아래 V 화살표와 현재 값(편집 칸)이 붙는다 → 원래 글자는 움직이지 않는다.
        VisualElement ValueWithHistory(string path, GsInlineProperty inline, VisualElement owner, string labelClass)
        {
            inline.Multiline = true;
            var column = Gs.Col("gs-detail__value");
            var oldBox = Gs.Box("gs-change__box", "gs-change__old");
            var oldLabel = labelClass == null ? Gs.Label("") : Gs.Label("", labelClass);
            oldBox.Add(oldLabel);
            var chevron = Gs.Box("gs-change__chevron");
            chevron.style.display = DisplayStyle.None;
            column.Add(oldBox);
            column.Add(chevron);
            column.Add(inline);
            _sections[path] = (owner, oldBox, _ =>
            {
                // 이전 값은 원본을 값 칸과 같은 규칙으로 표시한다(ID는 이름으로 등).
                var original = _originalObject.FindProperty(path);
                oldLabel.text = original != null ? GsInlineProperty.Describe(original).text : "없음";
                chevron.style.display = oldBox.style.display;
            });
            return column;
        }

        void OnValueChanged() => RefreshState();

        void OnStructureChanged()
        {
            var scroll = _body.scrollOffset;
            BuildBody();
            schedule.Execute(() => _body.scrollOffset = scroll);
        }

        internal void RefreshState()
        {
            _changes = DraftDiff.Compute(_originalObject, _draftObject);
            var changed = new HashSet<string>(_changes.Select(c => c.Path));

            foreach (var pair in _sections)
            {
                var isChanged = changed.Contains(pair.Key);
                pair.Value.section.EnableInClassList("gs-detail__section--changed", isChanged);
                pair.Value.change.style.display = isChanged ? DisplayStyle.Flex : DisplayStyle.None;
                if (isChanged) pair.Value.update(_changes.First(c => c.Path == pair.Key));
                else pair.Value.update(new FieldChange());
            }

            var count = _changes.Count;
            _status.text = count == 0 ? "변경 없음" : $"변경 {count}개 · 저장 전";
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
            var title = _draftObject.FindProperty(_options.TitlePath);
            var titleText = title != null && title.propertyType == SerializedPropertyType.String ? title.stringValue : null;
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
            if (_options.Validate != null)
                foreach (var (error, message) in _options.Validate(_draft))
                    _notices.Add(new GsNotice(message, error ? GsNoticeKind.Error : GsNoticeKind.Warning));
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
            GsDialog.Confirm(_host, "변경 사항 저장", $"아래 {_changes.Count}개 항목을 저장합니다.", "저장", () => SaveNow(), _changes);
        }

        void RequestClose()
        {
            if (_changes.Count == 0)
            {
                _modal.Close();
                return;
            }
            GsDialog.Show(_host, "저장하지 않은 변경", $"변경 {_changes.Count}개가 저장되지 않았습니다.", _changes,
                new DialogButton("계속 편집", GsButtonKind.Ghost, null),
                new DialogButton("버리고 닫기", GsButtonKind.Default, () => _modal.Close()),
                new DialogButton("저장하고 닫기", GsButtonKind.Primary, () =>
                {
                    SaveNow();
                    _modal.Close();
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

        public void Close() => _modal?.Close();

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