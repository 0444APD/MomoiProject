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
        VisualElement _viewSettingsPanel;
        Label _title;
        Label _caption;
        GsSegmented _views;
        GsSearchField _searchField;
        List<ValidationIssue> _issues = new List<ValidationIssue>();
        bool _refreshQueued;
        // 등장 애니메이션은 타입을 바꿀 때만(값 수정 후 재구성 때는 생략).
        bool _animate = true;

        // 사이드바 맨 위의 "시작하기" 탭(타입 대신 이 값이 선택된다).
        const string SetupKey = "$setup";
        [SerializeField] string _expandedStep;
        VisualElement _typeTools;
        VisualElement _helpSlot;

        [MenuItem("Game Systems/도감", priority = 0)]
        public static void Open() => OpenWindow();

        static DatabaseWindow OpenWindow()
        {
            var window = GetWindow<DatabaseWindow>();
            window.titleContent = new GUIContent("도감");
            window.minSize = new Vector2(980, 560);
            return window;
        }

        // 다른 창·시작하기 단계에서 특정 종류로 이동한다. create면 새 항목 만들기 창을 바로 연다.
        public static void ShowType(Type type, bool create = false)
        {
            var window = OpenWindow();
            window._selectedType = type.FullName;
            window._selectedId = null;
            window._animate = true;
            window.RefreshAll();
            if (create) window.CreateItem();
        }

        // 항목 하나를 상세 화면으로 연다.
        public static void ShowItem(GameData item)
        {
            if (item == null) return;
            var window = OpenWindow();
            window._selectedType = item.GetType().FullName;
            window._selectedId = item.Id;
            window.RefreshAll();
            window.OpenDetail(item);
        }

        public static void ShowSetup()
        {
            var window = OpenWindow();
            window._selectedType = SetupKey;
            window.RefreshAll();
        }

        // 검증 오류가 있는 첫 종류를 열고 오류 항목만 거른다.
        public static void ShowInvalid()
        {
            var window = OpenWindow();
            var issue = DataValidator.ValidateAll().FirstOrDefault(i => i.Severity == ValidationSeverity.Error && i.Asset != null);
            if (issue != null) window._selectedType = issue.Asset.GetType().FullName;
            window._search = "invalid";
            if (window._searchField != null) window._searchField.Value = "invalid";
            window.RefreshAll();
        }

        bool ShowingSetup
        {
            get
            {
                // 처음 열 때: 필수 단계가 남아 있으면 시작하기부터.
                if (_selectedType == null)
                    _selectedType = SetupRegistry.All.Any(s => !s.Optional && SetupRegistry.StateOf(s).state == SetupState.Todo) || DataTypes.All.Count == 0
                        ? SetupKey
                        : DataTypes.All[0].Type.FullName;
                return _selectedType == SetupKey;
            }
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
            var titleRow = Gs.Row();
            titleRow.Add(_title);
            _helpSlot = Gs.Row();
            _helpSlot.style.marginLeft = 10;
            titleRow.Add(_helpSlot);
            titles.Add(titleRow);
            titles.Add(_caption);
            header.Add(titles);
            header.Add(Gs.Spacer());
            _typeTools = Gs.Row();
            header.Add(_typeTools);

            _searchField = new GsSearchField("검색   tag:  invalid");
            _searchField.style.width = 230;
            _searchField.Value = _search;
            _searchField.Changed += value =>
            {
                _search = value;
                RebuildContent();
            };
            _typeTools.Add(_searchField);
            _typeTools.Add(Gs.HSpace(12));

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
            _typeTools.Add(_views);
            _typeTools.Add(Gs.HSpace(8));
            _typeTools.Add(new GsButton("보기 설정", ToggleViewSettings, GsButtonKind.Ghost));
            _typeTools.Add(Gs.HSpace(8));
            _typeTools.Add(new GsButton("+ 새로 만들기", CreateItem, GsButtonKind.Primary));
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

            RefreshAll();
        }

        Type SelectedType
        {
            get
            {
                if (ShowingSetup) return null;
                var types = DataTypes.All;
                var info = types.FirstOrDefault(t => t.Type.FullName == _selectedType) ?? types.FirstOrDefault();
                return info?.Type;
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

            var steps = SetupRegistry.All;
            var finished = steps.Count(s => SetupRegistry.StateOf(s).state != SetupState.Todo);
            var setup = new GsNavItem("시작하기", Gs.Accent, () =>
            {
                _selectedType = SetupKey;
                _viewSettingsPanel.style.display = DisplayStyle.None;
                _animate = true;
                RefreshAll();
            });
            setup.SetCount($"{finished}/{steps.Count}");
            setup.SetActive(ShowingSetup);
            _sidebar.Add(setup);
            var help = new GsNavItem("도움말", Gs.PaletteFor("help"), () => HelpWindow.Show(null));
            _sidebar.Add(help);

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
            _helpSlot.Clear();
            _typeTools.style.display = ShowingSetup ? DisplayStyle.None : DisplayStyle.Flex;
            if (ShowingSetup)
            {
                BuildSetup();
                return;
            }
            var type = SelectedType;
            if (type != null) _helpSlot.Add(HelpUI.Button(HelpIds.Data(type)));
            else _helpSlot.Add(HelpUI.Button("guide:codex"));
            if (type == null)
            {
                _title.text = "도감";
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

        // ── 시작하기 ───────────────────────────────────────────
        // 새 프로젝트의 설정 단계를 순서대로. 단계마다 설명·현재 상태·바로 하기 버튼·완료 표시.

        void BuildSetup()
        {
            var steps = SetupRegistry.All;
            var states = steps.Select(s => (step: s, status: SetupRegistry.StateOf(s))).ToList();
            var required = states.Where(s => !s.step.Optional).ToList();
            _title.text = "시작하기";
            _caption.text = $"필수 {required.Count(s => s.status.state != SetupState.Todo)}/{required.Count}  ·  전체 {states.Count(s => s.status.state != SetupState.Todo)}/{states.Count} 단계 완료";
            _helpSlot.Add(HelpUI.Button(HelpIds.Setup));

            var intro = Gs.Label("위에서부터 차례로 진행하세요. 뒤 단계는 앞 단계에서 만든 항목을 목록에서 고릅니다. 각 단계의 '?'로 자세한 도움말을, 왼쪽 '도움말'로 전체 설명을 볼 수 있습니다.", "gs-caption", "gs-setup__intro");
            _content.Add(intro);

            // 펼친 단계: 사용자가 고른 것, 없으면 첫 할 일.
            var expanded = states.Any(s => s.step.Id == _expandedStep) ? _expandedStep : states.FirstOrDefault(s => s.status.state == SetupState.Todo).step?.Id;
            int number = 0;
            foreach (var (step, status) in states)
            {
                number++;
                var card = Gs.Col("gs-setup__step");
                card.EnableInClassList("gs-setup__step--done", status.state == SetupState.Done || status.state == SetupState.Skipped);
                var open = step.Id == expanded;

                var head = Gs.Row("gs-setup__head");
                var mark = Gs.Label(status.state == SetupState.Done ? "✓" : status.state == SetupState.Skipped ? "–" : number.ToString(), "gs-setup__number");
                mark.EnableInClassList("gs-setup__number--done", status.state == SetupState.Done);
                head.Add(mark);
                var titles = Gs.Col("gs-grow");
                var titleRow = Gs.Row();
                titleRow.Add(Gs.Label(step.Title, "gs-heading"));
                if (step.Optional)
                {
                    var optional = new GsBadge("선택", Gs.PaletteFor("optional"));
                    optional.style.marginLeft = 8;
                    titleRow.Add(optional);
                }
                titles.Add(titleRow);
                var summary = Gs.Label(step.Summary, "gs-caption");
                summary.style.marginTop = 2;
                summary.style.whiteSpace = WhiteSpace.Normal;
                titles.Add(summary);
                head.Add(titles);
                head.Add(StatusBadge(status.state, status.text));
                if (!string.IsNullOrEmpty(step.HelpTopic)) head.Add(HelpUI.Button(step.HelpTopic));
                var id = step.Id;
                head.RegisterCallback<ClickEvent>(e =>
                {
                    if (e.target is VisualElement v && v.GetFirstAncestorOfType<GsButton>() == null && !(v is GsButton))
                    {
                        _expandedStep = open ? "" : id;
                        RebuildContent();
                    }
                });
                card.Add(head);

                if (open)
                {
                    var detail = Gs.Col("gs-setup__detail");
                    detail.Add(HelpUI.Body(step.Body));
                    var actions = Gs.Row("gs-wrap");
                    foreach (var action in step.Actions)
                    {
                        var a = action;
                        var button = new GsButton(a.Label, () =>
                        {
                            a.Run?.Invoke();
                            QueueRefresh();
                        }, a.Primary ? GsButtonKind.Primary : GsButtonKind.Default);
                        button.style.marginRight = 8;
                        button.style.marginBottom = 6;
                        actions.Add(button);
                    }
                    actions.Add(Gs.Spacer());
                    // 자동 판정이 없는 단계만 직접 완료 표시. 선택 단계는 건너뛰기 가능.
                    if (step.Check == null)
                    {
                        var marked = SetupRegistry.IsMarkedDone(id);
                        actions.Add(new GsButton(marked ? "완료 해제" : "완료로 표시", () =>
                        {
                            SetupRegistry.Mark(id, marked ? SetupState.Todo : SetupState.Done);
                            RefreshAll();
                        }, marked ? GsButtonKind.Ghost : GsButtonKind.Primary));
                    }
                    if (step.Optional)
                    {
                        var skipped = SetupRegistry.IsSkipped(id);
                        actions.Add(new GsButton(skipped ? "건너뛰기 취소" : "건너뛰기", () =>
                        {
                            SetupRegistry.Mark(id, skipped ? SetupState.Todo : SetupState.Skipped);
                            RefreshAll();
                        }, GsButtonKind.Ghost));
                    }
                    detail.Add(actions);
                    card.Add(detail);
                }
                _content.Add(card);
            }
            if (_animate) GsMotion.Appear(_content.Children().Skip(1));
            _animate = false;
        }

        static VisualElement StatusBadge(SetupState state, string text)
        {
            var color = state == SetupState.Done ? Gs.Success : state == SetupState.Warning ? Gs.Warning : state == SetupState.Skipped ? Gs.PaletteFor("skipped") : Gs.Accent;
            var label = state == SetupState.Done ? "완료" : state == SetupState.Skipped ? "건너뜀" : state == SetupState.Warning ? "확인 필요" : "할 일";
            var row = Gs.Col("gs-setup__status");
            row.Add(new GsBadge(label, color));
            var detail = Gs.Label(text ?? "", "gs-caption");
            detail.style.marginTop = 4;
            row.Add(detail);
            return row;
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
            // 시트는 읽기 전용. 행을 더블클릭하면 그 셀의 필드로 상세 패널을 연다.
            // 열 폭은 탭(타입)마다 저장된다: 머리줄 경계를 끌면 ProjectSettings의 보기 설정에 기록.
            var typeView = ViewSettings.Get(SelectedType);
            var sheet = new GsSheet(Gs.Background)
            {
                Thumbnail = view.Image == null ? null : (Func<UnityEngine.Object, UnityEngine.Object>)(o => new SerializedObject(o).FindProperty(view.Image.Path)?.objectReferenceValue),
                Trailing = o => ((GameData)o).Id,
                TrailingWidth = typeView.WidthOf(GsSheet.TrailingKey, 110),
                HasError = o => _issues.Any(i => i.Asset == o && i.Severity == ValidationSeverity.Error),
            };
            sheet.ColumnResized += (key, width) =>
            {
                typeView.SetWidth(key, Mathf.Round(width));
                ViewSettings.Save();
            };
            sheet.Build(view.Columns.Select(c => new SheetColumn { Title = c.Label, Width = typeView.WidthOf(c.Path, ColumnWidth(c)), Path = c.Path, Bold = c.Path == "displayName" }).ToList(),
                        items.Cast<UnityEngine.Object>());
            var selected = items.FirstOrDefault(i => i.Id == _selectedId);
            if (selected != null) sheet.Select(selected, false);
            sheet.Selected += o => _selectedId = ((GameData)o).Id;
            sheet.OpenRequested += (o, path) => OpenDetail((GameData)o, path);
            _content.Add(sheet);
            if (_animate) GsMotion.Appear(sheet.Children().Skip(1));
            _animate = false;
        }
        static float ColumnWidth(FieldEntry column)
        {
            if (column.Path == "displayName") return 200;
            // 설명처럼 긴 글은 넓게(ID보다 설명이 길어야 맞다).
            if (column.Kind == FieldKind.Text && column.Path.IndexOf("desc", StringComparison.OrdinalIgnoreCase) >= 0) return 320;
            switch (column.Kind)
            {
                case FieldKind.Number:
                case FieldKind.Bool: return 100;
                case FieldKind.Enum: return 120;
                case FieldKind.Other: return 150;
                default: return 170;
            }
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
                card.RegisterCallback<ClickEvent>(_ => OpenDetail(item));

                if (view.Image != null)
                {
                    var imageProperty = so.FindProperty(view.Image.Path);
                    card.SetImage(imageProperty == null ? null : SpriteSpecEditor.Preview(imageProperty.objectReferenceValue, PropertyUtil.GetField(imageProperty, out _)), placeholder);
                }
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
                    card.AddStat(stat.Label, GsInlineProperty.Describe(property).text);
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

        void CreateItem()
        {
            var type = SelectedType;
            if (type == null) return;
            var info = DataTypes.Find(type);
            var db = EditorDatabase.Instance;
            // 이름(ID)은 여기서 한 번 짓는다. 이후 모든 곳에서는 목록에서 고른다.
            GsDialog.Form(_root, $"새 {info.DisplayName}", "ID는 저장 파일·코드(GameIds)·조건이 함께 쓰는 식별자입니다. 나중에 바꿔도 참조가 모두 따라갑니다.",
                new[]
                {
                    new DialogField { Key = "id", Label = "ID", Value = db.NextId(info), Caption = "영문 소문자·숫자·밑줄, 분류는 점(.)으로. 예: player.inventory, kills_total" },
                    new DialogField { Key = "name", Label = "표시 이름", Value = "", Caption = "화면과 목록에 보이는 이름(한글 가능)" },
                },
                "만들기",
                values => db.CanUseId(values["id"], null, out var error) ? null : error,
                values =>
                {
                    var name = string.IsNullOrWhiteSpace(values["name"]) ? null : values["name"].Trim();
                    var created = db.Create(type, name, values["id"]);
                    _selectedId = created.Id;
                    _search = "";
                    _searchField.Value = "";
                    RefreshAll();
                    OpenDetail(created);
                });
        }

        void RenameId(GameData item, GsDetailPanel panel)
        {
            var db = EditorDatabase.Instance;
            var usages = db.FindUsages(item);
            var message = usages.Count == 0
                ? "이 ID를 가리키는 곳이 없습니다."
                : $"이 ID를 가리키는 {usages.Count}곳이 함께 바뀝니다: " + string.Join(", ", usages.Take(5).Select(u => u.Label)) + (usages.Count > 5 ? " …" : "");
            var saveNote = EditorDatabase.IsReleased
                ? "\n출시 상태: 저장 파일의 예전 ID 값은 새 ID로 이어집니다."
                : "\n개발 중: 이 ID로 저장된 데이터는 다음 플레이 때 초기화됩니다(변환하지 않음).";
            GsDialog.Form(_root, "ID 바꾸기", message + saveNote + " GameIds 코드도 다시 만들어집니다.",
                new[] { new DialogField { Key = "id", Label = "새 ID", Value = item.Id } },
                "바꾸기",
                values => values["id"] == item.Id ? "지금과 같은 ID입니다." : db.CanUseId(values["id"], item, out var error) ? null : error,
                values =>
                {
                    panel.Close();
                    var count = db.Rename(item, values["id"]);
                    _selectedId = item.Id;
                    QueueRefresh();
                    Debug.Log($"[GameSystems] ID를 바꿨습니다: {item.DisplayName} → {item.Id} (참조 {count}곳 갱신)");
                });
        }

        // ── 상세 패널(모든 카드·시트 공용) ──────────────────────

        void OpenDetail(GameData item, string focusPath = null)
        {
            _selectedId = item.Id;
            var info = DataTypes.Find(item.GetType());
            var view = ViewSettings.Resolve(item.GetType());
            var options = new DetailOptions
            {
                Subtitle = $"{info?.DisplayName}  ·  {item.Id}",
                ImagePath = view.Image?.Path,
                Placeholder = Gs.PaletteFor(item.GetType().Name),
                FocusPath = focusPath,
                HelpTopic = HelpIds.Data(item.GetType()),
                Summary = draft => DescribeItem((GameData)draft),
                Validate = draft => ValidateDraft(item, (GameData)draft),
                Saved = _ =>
                {
                    EditorDatabase.MarkDirty();
                    QueueRefresh();
                },
            };
            options.Actions.Add(("에셋 위치", _ => EditorGUIUtility.PingObject(item)));
            options.Actions.Add(("ID 바꾸기", panel => RenameId(item, panel)));
            if (item is PrefabData prefabEntry)
            {
                // 프리팹은 도감에서 지우지 않는다. 프로젝트 창의 프리팹으로 이동해 거기서 지운다(항목은 자동 정리).
                options.Actions.Add(("프로젝트에서 보기", _ => ShowInProject(prefabEntry.prefab)));
            }
            else
            {
                options.Actions.Add(("삭제", panel => Delete(item, panel)));
            }
            GsDetailPanel.Open(_root, item, options);
        }

        // 편집 중 사본을 원본 자리에 넣고 검증한다(저장 전에 오류를 미리 보여준다).
        static List<(bool error, string message)> ValidateDraft(GameData original, GameData draft)
        {
            var list = EditorDatabase.Instance.All.Select(d => d == original ? draft : d).ToList();
            return DataValidator.Validate(list)
                .Where(i => i.Asset == draft)
                .Select(i => (i.Severity == ValidationSeverity.Error, i.Message))
                .ToList();
        }

        // 상세 패널 제목 아래 요약: 머리말과 내용을 나눈 줄들.
        static IList<(string label, string text)> DescribeItem(GameData item)
        {
            var names = EditorDatabase.Instance;
            switch (item)
            {
                case AchievementData achievement:
                    var rewards = achievement.rewards.Where(r => r != null).Select(r => r.Describe(names)).ToList();
                    return new List<(string, string)>
                    {
                        ("조건", achievement.condition != null ? Gs.Dual(achievement.condition.Natural(names), achievement.condition.Describe(names)) : "조건 없음"),
                        ("보상", rewards.Count > 0 ? string.Join(",  ", rewards) : "없음"),
                    };
                case StatData stat:
                    var rules = stat.rules.Where(r => r != null).Select(r => r.Describe(stat.DisplayName)).ToList();
                    var rulesCode = stat.rules.Where(r => r != null).Select(r => r.DescribeCode(stat.DisplayName)).ToList();
                    return new List<(string, string)>
                    {
                        ("방식", $"{(stat.scope == StatScope.Run ? "이번 판" : "누적")} · {StatSystem.AggregateText(stat.aggregate)}"),
                        ("반영", rules.Count > 0 ? Gs.Dual(string.Join("\n", rules), string.Join("\n", rulesCode)) : "반영 규칙 없음"),
                    };
            }
            return null;
        }
        static void ShowInProject(UnityEngine.Object asset)
        {
            if (asset == null)
            {
                Debug.LogWarning("[GameSystems] 연결된 프리팹이 없습니다.");
                return;
            }
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        void Delete(GameData item, GsDetailPanel panel)
        {
            if (item is PrefabData)
            {
                GsDialog.Show(_root, "도감에서 지울 수 없습니다", "프리팹은 프로젝트 창에서 직접 지우세요. '프로젝트에서 보기'로 이동할 수 있고, 프리팹을 지우면 이 항목도 자동으로 정리됩니다.", null,
                    new DialogButton("확인", GsButtonKind.Primary, null));
                return;
            }
            // 어딘가에서 가리키는 항목은 지우지 못하게 막고 사용처를 보여 준다(지우면 끊긴 참조가 생긴다).
            var usages = EditorDatabase.Instance.FindUsages(item);
            if (usages.Count > 0)
            {
                GsDialog.Show(_root, "삭제할 수 없습니다",
                    $"'{item.DisplayName}'을(를) {usages.Count}곳에서 가리키고 있습니다. 먼저 그곳들의 선택을 바꾸세요.\n" +
                    string.Join("\n", usages.Take(10).Select(u => "· " + u.Label)) + (usages.Count > 10 ? "\n…" : ""),
                    null, new DialogButton("확인", GsButtonKind.Primary, null));
                return;
            }
            GsDialog.Confirm(_root, "항목 삭제", $"'{item.DisplayName}'을(를) 삭제합니다. 에셋은 휴지통으로 이동합니다.", "삭제", () =>
            {
                panel.Close();
                AssetDatabase.MoveAssetToTrash(AssetDatabase.GetAssetPath(item));
                _selectedId = null;
                EditorDatabase.Instance.Sync();
            });
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
                    var items = new List<GsDropdownItem>
                    {
                        new GsDropdownItem("자동", () => Apply(null)) { Detail = "필드 이름과 종류로 알아서 고릅니다", Selected = string.IsNullOrEmpty(current) },
                        new GsDropdownItem("없음", () => Apply("-")) { Selected = current == "-" },
                    };
                    foreach (var field in fields.Where(f => FieldCatalog.Accepts(slot, f.Kind)))
                    {
                        var path = field.Path;
                        items.Add(new GsDropdownItem(field.Label, () => Apply(path)) { Group = "필드", Caption = path, Selected = current == path });
                    }
                    GsDropdown.Show(value, items, placeholder: "필드 검색");
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
                GsButton add = null;
                add = new GsButton("+", () =>
                {
                    var items = new List<GsDropdownItem>();
                    foreach (var field in fields.Where(f => FieldCatalog.Accepts(slot, f.Kind)))
                    {
                        var path = field.Path;
                        if (isCustom() && paths.Contains(path)) continue;
                        items.Add(new GsDropdownItem(field.Label, () =>
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
                        }) { Caption = path });
                    }
                    if (items.Count == 0) items.Add(new GsDropdownItem("더 추가할 필드가 없습니다") { Disabled = true });
                    GsDropdown.Show(add, items, placeholder: "필드 검색");
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
