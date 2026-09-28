using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameSystems.EditorTools.UI
{
    public sealed class DetailOptions
    {
        // 제목으로 쓸 필드(패널 위쪽에 크게, 편집 가능).
        public string TitlePath = "displayName";
        public string Subtitle;
        // 왼쪽 정사각형 이미지. 편집 중 사본에서 읽으므로 이미지 필드를 바꾸면 바로 반영된다.
        public Func<SerializedObject, UnityEngine.Object> Image;
        public Color Placeholder = Gs.Palette[6];
        public HashSet<string> Hidden = new HashSet<string>();
        // 이 필드로 스크롤하고 강조한다(시트의 '…'에서 연 경우).
        public string FocusPath;
        // 편집 중 사본을 검증해 (오류 여부, 문장) 목록을 돌려준다.
        public Func<UnityEngine.Object, List<(bool error, string message)>> Validate;
        // 내용 맨 위 요약 문장(예: 조건 → 보상). 편집 중 사본으로 계산해 바로 갱신된다.
        public Func<UnityEngine.Object, string> Summary;
        public List<(string label, Action<GsDetailPanel> action)> Actions = new List<(string, Action<GsDetailPanel>)>();
        public Action<UnityEngine.Object> Saved;
    }

    // 모든 카드·시트가 공유하는 상세 보기·편집 패널(카드 B 구조: 정사각형 이미지 + 내용).
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
        Label _summary;
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

        // 창 크기에 맞춘 16:9 비율. 이미지는 패널 높이와 같은 정사각형.
        void Fit()
        {
            var width = Mathf.Min(940f, Mathf.Max(640f, _host.layout.width - 64f));
            var height = Mathf.Clamp(width * 9f / 16f, 380f, Mathf.Max(380f, _host.layout.height - 56f));
            style.width = width;
            style.height = height;
            var media = Mathf.Min(height, width - 400f);
            _media.style.width = media;
            _media.style.height = height;
        }

        void Build()
        {
            _media = Gs.Box("gs-detail__media");
            _media.style.alignItems = Align.Center;
            _media.style.justifyContent = Justify.Center;
            _initial = Gs.Label("", "gs-display");
            _initial.style.fontSize = 64;
            _initial.style.opacity = 0.35f;
            _media.Add(_initial);
            Add(_media);

            var main = Gs.Col("gs-detail__main");
            Add(main);

            var header = Gs.Box("gs-detail__header");
            var titles = Gs.Col("gs-grow");
            _titleSlot = Gs.Col();
            titles.Add(_titleSlot);
            if (!string.IsNullOrEmpty(_options.Subtitle))
            {
                var subtitle = Gs.Label(_options.Subtitle, "gs-caption");
                subtitle.style.marginTop = 6;
                titles.Add(subtitle);
            }
            header.Add(titles);
            var close = new GsButton("✕", RequestClose, GsButtonKind.Ghost);
            close.tooltip = "닫기 (Esc)";
            header.Add(close);
            main.Add(header);

            _body = new ScrollView(ScrollViewMode.Vertical);
            _body.AddToClassList("gs-detail__body");
            _body.contentContainer.style.paddingLeft = 26;
            _body.contentContainer.style.paddingRight = 22;
            _body.contentContainer.style.paddingTop = 18;
            _body.contentContainer.style.paddingBottom = 8;
            main.Add(_body);

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
            main.Add(footer);

            BuildBody();
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
                var titleChange = new GsChangeView(null, "", "");
                titleChange.style.marginBottom = 6;
                _titleSlot.Add(titleChange);
                var title = new GsInlineProperty(titleProperty);
                title.Q<Label>().AddToClassList("gs-display");
                title.Changed += OnValueChanged;
                _titleSlot.Add(title);
                _sections[titleProperty.propertyPath] = (_titleSlot, titleChange, c => titleChange.Set(c.Old, c.New));
            }

            _summary = Gs.Label("", "gs-preview");
            _body.Add(_summary);
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
                    // 단순 값: [이전] › 현재값(편집)
                    var row = Gs.Row();
                    var oldBox = Gs.Box("gs-change__box", "gs-change__old");
                    var oldLabel = Gs.Label("");
                    oldBox.Add(oldLabel);
                    var arrow = Gs.Label("›", "gs-change__arrow");
                    row.Add(oldBox);
                    row.Add(arrow);
                    content.AddToClassList("gs-grow");
                    row.Add(content);
                    section.Add(row);
                    _sections[path] = (section, oldBox, c =>
                    {
                        oldLabel.text = c.Old;
                        arrow.style.display = oldBox.style.display;
                    });
                    arrow.style.display = DisplayStyle.None;
                }
                else
                {
                    // 조건·목록: 위에 문장 단위 [이전] › [현재], 아래 편집기
                    var change = new GsChangeView(null, "", "");
                    change.style.marginBottom = 10;
                    section.Add(change);
                    section.Add(content);
                    _sections[path] = (section, change, c => change.Set(c.Old, c.New));
                }
                _body.Add(section);
            }

            RefreshState();

            if (!string.IsNullOrEmpty(_options.FocusPath) && _sections.TryGetValue(_options.FocusPath, out var focus))
            {
                var target = focus.section;
                _options.FocusPath = null;
                target.AddToClassList("gs-detail__section--changed");
                schedule.Execute(() => _body.ScrollTo(target)).StartingIn(60);
                schedule.Execute(() => RefreshState()).StartingIn(1600);
            }
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

            var image = _options.Image?.Invoke(_draftObject);
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

            var summary = _options.Summary?.Invoke(_draft);
            _summary.text = summary ?? "";
            _summary.style.display = string.IsNullOrEmpty(summary) ? DisplayStyle.None : DisplayStyle.Flex;

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
}
