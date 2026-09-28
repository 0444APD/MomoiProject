using System;
using System.Collections.Generic;
using System.Linq;
using GameSystems.Data;
using GameSystems.EditorTools.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameSystems.EditorTools
{
    // 공용 데이터베이스 창: GameData를 상속한 모든 타입을 시트·카드로 보고, 검색·추가·수정한다.
    public sealed class DatabaseWindow : EditorWindow
    {
        static readonly string[] ViewNames = { "시트", "세로 카드", "가로 카드", "바" };
        static readonly string[] ViewKeys = { "Sheet", "Vertical", "Horizontal", "Bar" };

        [SerializeField] string _selectedType;
        [SerializeField] string _selectedId;
        [SerializeField] string _search = "";

        VisualElement _root;
        VisualElement _sidebar;
        VisualElement _content;
        VisualElement _detail;
        VisualElement _viewSettingsPanel;
        Label _title;
        Label _caption;
        GsSegmented _views;
        GsSearchField _searchField;
        List<ValidationIssue> _issues = new List<ValidationIssue>();
        bool _refreshQueued;
        // 등장 애니메이션은 타입을 바꿀 때만(값 수정 후 재구성 때는 생략).
        bool _animate = true;

        [MenuItem("Game Systems/데이터베이스", priority = 0)]
        public static void Open()
        {
            var window = GetWindow<DatabaseWindow>();
            window.titleContent = new GUIContent("데이터베이스");
            window.minSize = new Vector2(980, 560);
        }

        void OnEnable()
        {
            EditorDatabase.Changed += QueueRefresh;
            Undo.undoRedoPerformed += QueueRefresh;
        }

        void OnDisable()
        {
            EditorDatabase.Changed -= QueueRefresh;
            Undo.undoRedoPerformed -= QueueRefresh;
        }

        void CreateGUI()
        {
            EditorDatabase.Instance.Sync();
            Build(rootVisualElement);
        }

        internal void Build(VisualElement host)
        {
            host.Clear();
            _root = Gs.Row();
            Gs.Apply(_root);
            _root.style.alignItems = Align.Stretch;
            host.Add(_root);

            _sidebar = Gs.Box("gs-sidebar");
            _root.Add(_sidebar);

            var main = Gs.Col("gs-grow");
            main.style.paddingLeft = 32;
            main.style.paddingRight = 32;
            main.style.paddingTop = 28;
            main.style.minWidth = 0;
            _root.Add(main);

            var header = Gs.Row();
            header.style.marginBottom = 22;
            var titles = Gs.Col();
            _title = Gs.Label("", "gs-display");
            _caption = Gs.Label("", "gs-caption");
            _caption.style.marginTop = 4;
            titles.Add(_title);
            titles.Add(_caption);
            header.Add(titles);
            header.Add(Gs.Spacer());

            _searchField = new GsSearchField("검색   tag:  invalid");
            _searchField.style.width = 230;
            _searchField.Value = _search;
            _searchField.Changed += value =>
            {
                _search = value;
                RebuildContent();
            };
            header.Add(_searchField);
            header.Add(Gs.HSpace(12));

            _views = new GsSegmented(ViewNames);
            _views.Changed += index =>
            {
                var type = SelectedType;
                if (type == null) return;
                ViewSettings.Get(type).layout = ViewKeys[index];
                ViewSettings.Save();
                _animate = true;
                RebuildContent();
            };
            header.Add(_views);
            header.Add(Gs.HSpace(8));
            header.Add(new GsButton("보기 설정", ToggleViewSettings, GsButtonKind.Ghost));
            header.Add(Gs.HSpace(8));
            header.Add(new GsButton("+ 새로 만들기", CreateItem, GsButtonKind.Primary));
            main.Add(header);

            var body = new VisualElement();
            body.style.flexGrow = 1;
            main.Add(body);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            _content = scroll.contentContainer;
            body.Add(scroll);

            _viewSettingsPanel = Gs.Box("gs-panel");
            _viewSettingsPanel.style.position = Position.Absolute;
            _viewSettingsPanel.style.right = 0;
            _viewSettingsPanel.style.top = 0;
            _viewSettingsPanel.style.width = 360;
            _viewSettingsPanel.style.display = DisplayStyle.None;
            body.Add(_viewSettingsPanel);

            var detailScroll = new ScrollView(ScrollViewMode.Vertical);
            detailScroll.style.width = 400;
            detailScroll.style.flexShrink = 0;
            detailScroll.style.backgroundColor = Gs.Surface;
            detailScroll.style.borderLeftWidth = 1;
            detailScroll.style.borderLeftColor = new Color(1, 1, 1, 0.06f);
            _detail = detailScroll.contentContainer;
            _detail.style.paddingLeft = 24;
            _detail.style.paddingRight = 24;
            _detail.style.paddingTop = 28;
            _detail.style.paddingBottom = 40;
            _root.Add(detailScroll);

            RefreshAll();
        }

        Type SelectedType
        {
            get
            {
                var types = DataTypes.All;
                var info = types.FirstOrDefault(t => t.Type.FullName == _selectedType) ?? types.FirstOrDefault();
                return info?.Type;
            }
        }

        GameData SelectedItem
        {
            get
            {
                var type = SelectedType;
                return type == null ? null : EditorDatabase.Instance.OfType(type).FirstOrDefault(d => d.Id == _selectedId);
            }
        }

        void QueueRefresh()
        {
            if (_refreshQueued || _root == null) return;
            _refreshQueued = true;
            _root.schedule.Execute(() =>
            {
                _refreshQueued = false;
                RefreshAll();
            });
        }

        void RefreshAll()
        {
            if (_root == null) return;
            _issues = DataValidator.ValidateAll();
            RebuildSidebar();
            RebuildContent();
            RebuildDetail();
        }

        // ── 사이드바 ───────────────────────────────────────────

        void RebuildSidebar()
        {
            _sidebar.Clear();
            var brand = Gs.Col();
            brand.style.marginLeft = 12;
            brand.style.marginBottom = 8;
            var logo = Gs.Row();
            var mark = new VisualElement();
            mark.style.width = 18;
            mark.style.height = 18;
            mark.style.borderTopLeftRadius = mark.style.borderTopRightRadius = mark.style.borderBottomLeftRadius = mark.style.borderBottomRightRadius = 5;
            mark.style.backgroundColor = Gs.Accent;
            mark.style.marginRight = 10;
            logo.Add(mark);
            logo.Add(Gs.Label("Game Systems", "gs-heading"));
            brand.Add(logo);
            _sidebar.Add(brand);

            var selected = SelectedType;
            foreach (var group in DataTypes.All.GroupBy(t => t.Group))
            {
                _sidebar.Add(Gs.Label(group.Key.ToUpperInvariant(), "gs-sidebar__group"));
                foreach (var info in group)
                {
                    var type = info.Type;
                    var item = new GsNavItem(info.DisplayName, Gs.PaletteFor(type.Name), () =>
                    {
                        _selectedType = type.FullName;
                        _selectedId = null;
                        _animate = true;
                        _viewSettingsPanel.style.display = DisplayStyle.None;
                        RefreshAll();
                    });
                    var count = EditorDatabase.Instance.OfType(type).Count();
                    var errors = _issues.Count(i => i.Severity == ValidationSeverity.Error && i.Asset != null && i.Asset.GetType() == type);
                    item.SetCount(errors > 0 ? $"{count} · 오류 {errors}" : count.ToString());
                    item.SetActive(type == selected);
                    _sidebar.Add(item);
                }
            }

            if (DataTypes.All.Count == 0)
                _sidebar.Add(Gs.Label("GameData를 상속한 데이터 타입이 없습니다.", "gs-caption"));

            _sidebar.Add(Gs.Spacer());

            var errorCount = _issues.Count(i => i.Severity == ValidationSeverity.Error);
            var warningCount = _issues.Count(i => i.Severity == ValidationSeverity.Warning);
            _sidebar.Add(errorCount > 0
                ? new GsNotice($"검증 오류 {errorCount}건 · 빌드가 막힙니다", GsNoticeKind.Error)
                : warningCount > 0 ? new GsNotice($"경고 {warningCount}건", GsNoticeKind.Warning)
                : new GsNotice("검증 통과", GsNoticeKind.Ok));

            var unregistered = DataTypes.UnregisteredScriptableObjects().ToList();
            if (unregistered.Count > 0)
            {
                var hint = Gs.Label($"등록되지 않은 데이터 타입 {unregistered.Count}개", "gs-caption");
                hint.tooltip = "GameData를 상속하지 않은 ScriptableObject:\n" + string.Join("\n", unregistered.Select(t => t.FullName)) +
                               "\n\n데이터베이스에서 다루려면 GameData를 상속하세요.";
                hint.style.marginLeft = 12;
                hint.style.marginTop = 4;
                _sidebar.Add(hint);
            }
        }

        // ── 목록(시트·카드) ─────────────────────────────────────

        void RebuildContent()
        {
            if (_content == null) return;
            _content.Clear();
            var type = SelectedType;
            if (type == null)
            {
                _title.text = "데이터베이스";
                _caption.text = "GameData를 상속한 클래스를 만들면 여기에 자동으로 나타납니다.";
                return;
            }

            var info = DataTypes.Find(type);
            var all = EditorDatabase.Instance.OfType(type).ToList();
            var items = all.Where(Matches).ToList();
            var typeErrors = _issues.Count(i => i.Severity == ValidationSeverity.Error && i.Asset != null && i.Asset.GetType() == type);
            _title.text = info.DisplayName;
            _caption.text = (items.Count == all.Count ? $"{all.Count}개 항목" : $"{items.Count} / {all.Count}개 항목") + (typeErrors > 0 ? $"  ·  오류 {typeErrors}" : "");

            var view = ViewSettings.Resolve(type);
            _views.Select(Math.Max(0, Array.IndexOf(ViewKeys, view.Layout)));

            foreach (var broken in view.Broken)
            {
                var notice = new GsNotice($"보기 설정의 '{broken.slot}' 연결이 끊겼습니다: {broken.path}", GsNoticeKind.Warning);
                if (broken.suggestion != null)
                {
                    var fix = broken;
                    notice.Add(new GsButton($"'{broken.suggestion.Label}'로 다시 연결", () =>
                    {
                        Rebind(type, fix.path, fix.suggestion.Path);
                        RebuildContent();
                    }, GsButtonKind.Ghost));
                }
                var clear = broken;
                notice.Add(new GsButton("연결 해제", () =>
                {
                    Rebind(type, clear.path, null);
                    RebuildContent();
                }, GsButtonKind.Ghost));
                _content.Add(notice);
            }

            if (all.Count == 0)
            {
                var empty = Gs.Box("gs-empty");
                empty.Add(Gs.Label($"아직 {info.DisplayName} 항목이 없습니다", "gs-title"));
                empty.Add(Gs.Space(8));
                empty.Add(Gs.Label("첫 항목을 만들면 이 화면에 시트나 카드로 표시됩니다.", "gs-caption"));
                empty.Add(Gs.Space(18));
                empty.Add(new GsButton($"+ 첫 {info.DisplayName} 만들기", CreateItem, GsButtonKind.Primary));
                _content.Add(empty);
                return;
            }
            if (items.Count == 0)
            {
                var empty = Gs.Box("gs-empty");
                empty.Add(Gs.Label("검색 결과가 없습니다", "gs-heading"));
                _content.Add(empty);
                return;
            }

            if (view.Layout == "Sheet" || ViewSettings.CardLayoutOf(ViewSettings.Get(type)) == null) BuildSheet(items, view);
            else BuildCards(items, view, ViewSettings.CardLayoutOf(ViewSettings.Get(type)).Value);
        }

        bool Matches(GameData data)
        {
            if (string.IsNullOrWhiteSpace(_search)) return true;
            foreach (var token in _search.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.StartsWith("tag:", StringComparison.OrdinalIgnoreCase))
                {
                    var tag = token.Substring(4);
                    if (!data.Tags.Any(t => t.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0)) return false;
                }
                else if (token.Equals("invalid", StringComparison.OrdinalIgnoreCase))
                {
                    if (!_issues.Any(i => i.Asset == data && i.Severity == ValidationSeverity.Error)) return false;
                }
                else if (data.DisplayName.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0 &&
                         (data.Id ?? "").IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return false;
                }
            }
            return true;
        }

        void BuildSheet(List<GameData> items, ResolvedView view)
        {
            var sheet = Gs.Col();
            var header = Gs.Box("gs-sheet__header");
            if (view.Image != null) header.Add(Cell(Gs.Label(""), 40));
            foreach (var column in view.Columns) header.Add(Cell(Gs.Label(column.Label.ToUpperInvariant()), ColumnWidth(column)));
            header.Add(Cell(Gs.Label("ID"), 130));
            sheet.Add(header);

            foreach (var item in items)
            {
                var so = new SerializedObject(item);
                var row = Gs.Box("gs-sheet__row");
                row.EnableInClassList("gs-sheet__row--selected", item.Id == _selectedId);
                row.RegisterCallback<ClickEvent>(_ => Select(item));

                if (view.Image != null)
                {
                    var thumb = Gs.Box("gs-sheet__thumb");
                    SetBackground(thumb, so.FindProperty(view.Image.Path)?.objectReferenceValue);
                    row.Add(Cell(thumb, 40));
                }

                foreach (var column in view.Columns)
                {
                    var property = so.FindProperty(column.Path);
                    VisualElement cell;
                    if (property == null) cell = Gs.Label("—", "gs-text-3");
                    else if (column.Kind == FieldKind.Other) cell = Gs.Label(PropertyUtil.Summary(property), "gs-text-2", "gs-nowrap");
                    else
                    {
                        var inline = new GsInlineProperty(property);
                        inline.Changed += QueueRefresh;
                        if (column.Path == "displayName") inline.Q<Label>().AddToClassList("gs-heading");
                        cell = inline;
                    }
                    row.Add(Cell(cell, ColumnWidth(column)));
                }

                var idLabel = Gs.Label(item.Id, "gs-caption", "gs-nowrap");
                var hasError = _issues.Any(i => i.Asset == item && i.Severity == ValidationSeverity.Error);
                var idCell = Gs.Row();
                idCell.Add(idLabel);
                if (hasError) idCell.Add(new GsBadge("오류", Gs.Danger) { style = { marginLeft = 8 } });
                row.Add(Cell(idCell, 130));
                sheet.Add(row);
            }
            _content.Add(sheet);
            if (_animate) GsMotion.Appear(sheet.Children().Skip(1));
            _animate = false;
        }

        static float ColumnWidth(FieldEntry column)
        {
            if (column.Path == "displayName") return 210;
            switch (column.Kind)
            {
                case FieldKind.Number:
                case FieldKind.Bool: return 100;
                case FieldKind.Enum: return 120;
                case FieldKind.Other: return 150;
                default: return 170;
            }
        }

        static VisualElement Cell(VisualElement content, float width)
        {
            var cell = Gs.Box("gs-sheet__cell");
            cell.style.width = width;
            cell.Add(content);
            return cell;
        }

        void BuildCards(List<GameData> items, ResolvedView view, CardLayout layout)
        {
            var grid = new VisualElement();
            grid.style.flexDirection = layout == CardLayout.Bar ? FlexDirection.Column : FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;
            var placeholder = Gs.PaletteFor(SelectedType.Name);

            foreach (var item in items)
            {
                var so = new SerializedObject(item);
                var card = new GsCard(layout);
                card.SetSelected(item.Id == _selectedId);
                card.RegisterCallback<ClickEvent>(_ => Select(item));

                if (view.Image != null) card.SetImage(so.FindProperty(view.Image.Path)?.objectReferenceValue, placeholder);
                else if (layout == CardLayout.Bar) card.SetImage(null, placeholder);
                else card.HideMedia();

                card.Title = view.Title != null ? Value(so, view.Title) : item.DisplayName;
                card.Subtitle = view.Subtitle != null ? Value(so, view.Subtitle) : item.Id;

                if (view.Badge != null)
                {
                    var badge = Value(so, view.Badge);
                    if (!string.IsNullOrEmpty(badge)) card.AddBadge(badge, Gs.PaletteFor(view.Badge.Path + ":" + badge));
                }
                foreach (var tag in item.Tags.Where(t => !string.IsNullOrEmpty(t)))
                    card.AddBadge(tag, Gs.PaletteFor("tag:" + tag));
                if (_issues.Any(i => i.Asset == item && i.Severity == ValidationSeverity.Error))
                    card.AddBadge("오류", Gs.Danger);

                foreach (var stat in view.Stats)
                {
                    var property = so.FindProperty(stat.Path);
                    if (property == null) continue;
                    var box = Gs.Box("gs-card__stat");
                    box.Add(Gs.Label(stat.Label, "gs-card__stat-label"));
                    var inline = new GsInlineProperty(property);
                    inline.Changed += QueueRefresh;
                    box.Add(inline);
                    card.AddToStats(box);
                }
                grid.Add(card);
            }
            _content.Add(grid);
            if (_animate) GsMotion.Appear(grid);
            _animate = false;
        }

        static string Value(SerializedObject so, FieldEntry entry)
        {
            var property = so.FindProperty(entry.Path);
            return property == null ? "" : PropertyUtil.Summary(property);
        }

        static void SetBackground(VisualElement element, UnityEngine.Object image)
        {
            if (image is Sprite sprite) element.style.backgroundImage = new StyleBackground(sprite);
            else if (image is Texture2D texture) element.style.backgroundImage = new StyleBackground(texture);
        }

        void Select(GameData item)
        {
            _selectedId = item.Id;
            RebuildContent();
            RebuildDetail();
        }

        void CreateItem()
        {
            var type = SelectedType;
            if (type == null) return;
            var created = EditorDatabase.Instance.Create(type);
            _selectedId = created.Id;
            _search = "";
            _searchField.Value = "";
            RefreshAll();
        }

        // ── 상세 패널 ───────────────────────────────────────────

        void RebuildDetail()
        {
            if (_detail == null) return;
            _detail.Clear();
            var item = SelectedItem;
            if (item == null)
            {
                var empty = Gs.Box("gs-empty");
                empty.Add(Gs.Label("항목을 선택하세요", "gs-heading"));
                empty.Add(Gs.Space(6));
                empty.Add(Gs.Label("시트의 행이나 카드를 클릭하면 여기서 자세히 편집합니다.", "gs-caption"));
                _detail.Add(empty);
                return;
            }

            var so = new SerializedObject(item);
            var view = ViewSettings.Resolve(item.GetType());

            if (view.Image != null)
            {
                var image = so.FindProperty(view.Image.Path)?.objectReferenceValue;
                if (image != null)
                {
                    var preview = new VisualElement();
                    preview.style.width = 72;
                    preview.style.height = 72;
                    preview.style.borderTopLeftRadius = preview.style.borderTopRightRadius = preview.style.borderBottomLeftRadius = preview.style.borderBottomRightRadius = 14;
                    preview.style.marginBottom = 16;
                    SetBackground(preview, image);
                    _detail.Add(preview);
                }
            }

            var nameProperty = so.FindProperty("displayName");
            if (nameProperty != null)
            {
                var name = new GsInlineProperty(nameProperty);
                name.Q<Label>().AddToClassList("gs-display");
                name.Changed += QueueRefresh;
                _detail.Add(name);
            }

            var info = DataTypes.Find(item.GetType());
            var meta = Gs.Row();
            meta.style.marginTop = 4;
            meta.Add(Gs.Label($"{info?.DisplayName}  ·  {item.Id}", "gs-caption"));
            _detail.Add(meta);
            _detail.Add(Gs.Space(18));

            var previewText = Gs.Label(DescribeItem(item), "gs-preview");
            if (!string.IsNullOrEmpty(previewText.text)) _detail.Add(previewText);

            foreach (var issue in _issues.Where(i => i.Asset == item))
                _detail.Add(new GsNotice(issue.Message, issue.Severity == ValidationSeverity.Error ? GsNoticeKind.Error : GsNoticeKind.Warning));

            void OnValue()
            {
                so.Update();
                previewText.text = DescribeItem(item);
                QueueRefresh();
            }

            foreach (var property in PropertyUtil.TopLevel(so))
            {
                if (property.propertyPath == "displayName") continue;
                _detail.Add(GsPropertyEditor.BuildProperty(property, OnValue, () => RebuildDetailSoon()));
            }

            _detail.Add(Gs.Space(24));
            var actions = Gs.Row();
            actions.Add(new GsButton("에셋 위치", () => EditorGUIUtility.PingObject(item), GsButtonKind.Ghost));
            if (_issues.Any(i => i.Asset == item && i.Message.Contains("중복")))
                actions.Add(new GsButton("ID 재발급", () =>
                {
                    EditorDatabase.Instance.AssignNewId(item);
                    _selectedId = item.Id;
                    RefreshAll();
                }, GsButtonKind.Ghost));
            actions.Add(Gs.Spacer());
            actions.Add(new GsButton("삭제", () => Delete(item), GsButtonKind.Ghost));
            _detail.Add(actions);
        }

        void RebuildDetailSoon() => _root.schedule.Execute(() =>
        {
            _issues = DataValidator.ValidateAll();
            RebuildDetail();
            RebuildContent();
            RebuildSidebar();
        });

        static string DescribeItem(GameData item)
        {
            var names = EditorDatabase.Instance;
            switch (item)
            {
                case AchievementData achievement:
                    var condition = achievement.condition != null ? achievement.condition.Describe(names) : "(조건 없음)";
                    var rewards = achievement.rewards.Where(r => r != null).Select(r => r.Describe(names)).ToList();
                    return rewards.Count > 0 ? $"{condition}  →  {string.Join(", ", rewards)}" : condition;
                case StatData stat:
                    var scope = stat.scope == StatScope.Run ? "이번 판" : "누적";
                    var rules = stat.rules.Where(r => r != null).Select(r => r.Describe(stat.DisplayName)).ToList();
                    return rules.Count > 0 ? $"{scope} · {StatSystem.AggregateText(stat.aggregate)}\n" + string.Join("\n", rules) : $"{scope} · {StatSystem.AggregateText(stat.aggregate)} · 반영 규칙 없음";
            }
            return null;
        }

        void Delete(GameData item)
        {
            if (!EditorUtility.DisplayDialog("항목 삭제", $"'{item.DisplayName}'을(를) 삭제할까요?\n에셋은 휴지통으로 이동합니다.", "삭제", "취소")) return;
            AssetDatabase.MoveAssetToTrash(AssetDatabase.GetAssetPath(item));
            _selectedId = null;
            EditorDatabase.Instance.Sync();
        }

        // ── 보기 설정 ──────────────────────────────────────────

        void ToggleViewSettings()
        {
            var visible = _viewSettingsPanel.style.display == DisplayStyle.Flex;
            _viewSettingsPanel.style.display = visible ? DisplayStyle.None : DisplayStyle.Flex;
            if (!visible) RebuildViewSettings();
        }

        void RebuildViewSettings()
        {
            _viewSettingsPanel.Clear();
            var type = SelectedType;
            if (type == null) return;
            var view = ViewSettings.Get(type);
            var fields = FieldCatalog.For(type);

            var header = Gs.Row();
            header.Add(Gs.Label("보기 설정", "gs-title"));
            header.Add(Gs.Spacer());
            header.Add(new GsButton("닫기", ToggleViewSettings, GsButtonKind.Ghost));
            _viewSettingsPanel.Add(header);
            _viewSettingsPanel.Add(Gs.Label("비워 두면 자동으로 고릅니다. 이 설정은 프로젝트 전체에 공유됩니다.", "gs-caption"));
            _viewSettingsPanel.Add(Gs.Space(14));

            void SlotRow(string label, Slot slot, Func<string> get, Action<string> set)
            {
                var row = Gs.Box("gs-field-row");
                row.Add(Gs.Label(label, "gs-field-row__label"));
                var current = get();
                var text = current == "-" ? "없음" : string.IsNullOrEmpty(current) ? "자동" : fields.FirstOrDefault(f => f.Path == current)?.Label ?? $"끊김: {current}";
                var value = Gs.Box("gs-inline", "gs-inline--editable", "gs-grow");
                var valueLabel = Gs.Label(text, "gs-inline__value");
                if (string.IsNullOrEmpty(current) || current == "-") valueLabel.AddToClassList("gs-inline__empty");
                value.Add(valueLabel);
                value.RegisterCallback<ClickEvent>(_ =>
                {
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent("자동"), string.IsNullOrEmpty(current), () => Apply(null));
                    menu.AddItem(new GUIContent("없음"), current == "-", () => Apply("-"));
                    menu.AddSeparator("");
                    foreach (var field in fields.Where(f => FieldCatalog.Accepts(slot, f.Kind)))
                    {
                        var path = field.Path;
                        menu.AddItem(new GUIContent(field.Label), current == path, () => Apply(path));
                    }
                    menu.ShowAsContext();
                });
                row.Add(value);
                _viewSettingsPanel.Add(row);

                void Apply(string path)
                {
                    set(path);
                    ViewSettings.Save();
                    RebuildViewSettings();
                    RebuildContent();
                }
            }

            SlotRow("이미지", Slot.Image, () => view.image, v => view.image = v);
            SlotRow("제목", Slot.Title, () => view.title, v => view.title = v);
            SlotRow("부제목", Slot.Subtitle, () => view.subtitle, v => view.subtitle = v);
            SlotRow("배지", Slot.Badge, () => view.badge, v => view.badge = v);

            ListRow("카드 수치", Slot.Stat, view.stats, () => view.customStats, v => view.customStats = v, ViewSettings.Resolve(type).Stats);
            ListRow("시트 열", Slot.Column, view.columns, () => view.customColumns, v => view.customColumns = v, ViewSettings.Resolve(type).Columns);

            void ListRow(string label, Slot slot, List<string> paths, Func<bool> isCustom, Action<bool> setCustom, List<FieldEntry> resolved)
            {
                _viewSettingsPanel.Add(Gs.Space(10));
                var head = Gs.Row();
                head.Add(Gs.Label(label, "gs-caption"));
                head.Add(Gs.Spacer());
                if (isCustom())
                    head.Add(new GsButton("자동으로", () =>
                    {
                        setCustom(false);
                        paths.Clear();
                        ViewSettings.Save();
                        RebuildViewSettings();
                        RebuildContent();
                    }, GsButtonKind.Ghost));
                _viewSettingsPanel.Add(head);

                var chips = Gs.Row("gs-wrap");
                chips.style.marginTop = 6;
                foreach (var entry in resolved)
                {
                    var chip = new GsBadge(entry.Label + (isCustom() ? "  ✕" : ""), Gs.PaletteFor(entry.Path));
                    if (isCustom())
                    {
                        var path = entry.Path;
                        chip.RegisterCallback<ClickEvent>(_ =>
                        {
                            paths.Remove(path);
                            ViewSettings.Save();
                            RebuildViewSettings();
                            RebuildContent();
                        });
                    }
                    chip.style.marginBottom = 6;
                    chips.Add(chip);
                }
                var add = new GsButton("+", () =>
                {
                    var menu = new GenericMenu();
                    foreach (var field in fields.Where(f => FieldCatalog.Accepts(slot, f.Kind)))
                    {
                        var path = field.Path;
                        if (isCustom() && paths.Contains(path)) continue;
                        menu.AddItem(new GUIContent(field.Label), false, () =>
                        {
                            if (!isCustom())
                            {
                                paths.Clear();
                                paths.AddRange(resolved.Select(r => r.Path));
                                setCustom(true);
                            }
                            paths.Add(path);
                            ViewSettings.Save();
                            RebuildViewSettings();
                            RebuildContent();
                        });
                    }
                    menu.ShowAsContext();
                }, GsButtonKind.Ghost);
                chips.Add(add);
                _viewSettingsPanel.Add(chips);
            }
        }

        static void Rebind(Type type, string oldPath, string newPath)
        {
            var view = ViewSettings.Get(type);
            var replacement = newPath ?? (string)null;
            if (view.image == oldPath) view.image = replacement;
            if (view.title == oldPath) view.title = replacement;
            if (view.subtitle == oldPath) view.subtitle = replacement;
            if (view.badge == oldPath) view.badge = replacement;
            ReplaceIn(view.stats, oldPath, newPath);
            ReplaceIn(view.columns, oldPath, newPath);
            ViewSettings.Save();
        }

        static void ReplaceIn(List<string> list, string oldPath, string newPath)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i] != oldPath) continue;
                if (newPath == null) list.RemoveAt(i);
                else list[i] = newPath;
            }
        }
    }
}
