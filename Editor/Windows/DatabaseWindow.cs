using System;
using System.Collections.Generic;
using System.Linq;
using GBible.Data;
using GBible.EditorTools.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GBible.EditorTools
{
    // 공용 DB 창: GameData를 상속한 모든 타입을 시트·카드로 보고, 검색·추가·수정한다.
    public sealed class DatabaseWindow : EditorWindow
    {
        // 예전 목록(시트·카드 보기와 보기 설정). 지우지 않고 숨겨 두었다: true면 전용 화면이 없는 종류가 다시 시트·카드로 보인다.
        // 기본은 모든 탭이 클래스 탭과 같은 목록(DataListPage·CodexTree).
        public static bool LegacyViews = false;

        static readonly string[] ViewNames = { "시트", "세로 카드", "가로 카드", "바" };
        static readonly string[] ViewKeys = { "Sheet", "Vertical", "Horizontal", "Bar" };

        [SerializeField] string _selectedType;
        [SerializeField] string _selectedId;
        [SerializeField] string _search = "";

        VisualElement _root;
        VisualElement _sidebar;
        // 사이드바 = 탭 목록(넘치면 스크롤 — 항목 크기는 그대로) + 바닥(저장·검증, 늘 보임).
        ScrollView _sidebarNav;
        VisualElement _sidebarFooter;
        VisualElement _content;
        VisualElement _viewSettingsPanel;
        Label _title;
        Label _caption;
        GsSegmented _views;
        GsSearchField _searchField;
        List<ValidationIssue> _issues = new List<ValidationIssue>();
        bool _refreshQueued;
        // 사이드바의 탭 버튼(탭 키 → 버튼). 탭을 옮길 때는 사이드바를 다시 짓지 않고 선택 표시만 바꾼다.
        readonly Dictionary<string, GsNavItem> _navItems = new Dictionary<string, GsNavItem>();

        // DB 종류에 딸리지 않은 독립 탭(클래스·이벤트)의 키 앞머리.
        const string PagePrefix = "$page:";

        // 사이드바의 탭 하나: DB 종류(Type) 또는 독립 화면(Page). 꺼진 기능의 탭은 흐리게만 보인다(DisabledReason).
        sealed class TabEntry
        {
            public string Key;
            public string Label;
            public string Group;
            public int Order;
            public Type Type;
            public CodexPage Page;
            public string DisabledReason;
        }

        // 사이드바 그룹 순서: 선언(키워드·클래스·로그·이벤트·플레이어 데이터)이 맨 위, 선택 기능은 아래쪽.
        static int GroupRank(string group)
        {
            switch (group)
            {
                case BootstrapPage.Group: return -10;
                case ClassesPage.DeclareGroup: return 0;
                case "DB": return 20;
                case "이미지": return 30;
                case DevFolders.Group: return 50;
            }
            for (int i = 0; i < GameFeatures.All.Count; i++)
                if (GameFeatures.All[i].Name == group) return 60 + i;
            return 40;
        }

        // 'DB' 그룹의 로직 종류(능력·상태 효과·패시브·가중치 표 …)와 [Database(HideWhenEmpty)] 종류(도전과제·로그)는 항목이 없으면 탭을 숨긴다 —
        // 그 프로젝트가 쓰는 것만 보인다(AI가 항목을 만들면 나타난다). 개발자가 만드는 종류(DeveloperAuthored)는 비어 있어도 보인다(첫 항목을 만들 수 있게).
        // keep: 지금 연 탭은 비어도 남긴다.
        static int _hiddenEmpty;

        static bool ShowEmptyTabs
        {
            get => SessionState.GetBool("MomoiProject.DB.ShowEmptyTabs", false);
            set => SessionState.SetBool("MomoiProject.DB.ShowEmptyTabs", value);
        }

        static List<TabEntry> Tabs(string keep = null)
        {
            var tabs = new List<TabEntry>();
            _hiddenEmpty = 0;
            foreach (var info in DataTypes.All.Where(i => !i.Hidden))
            {
                if (info.HideWhenEmpty && !ShowEmptyTabs && info.Type.FullName != keep &&
                    !EditorDatabase.Instance.All.Any(d => d != null && d.GetType() == info.Type))
                {
                    _hiddenEmpty++;
                    continue;
                }
                // 막 끈 기능의 종류는 다시 컴파일되기 전이라도 바로 뺀다(아래에서 흐린 탭으로 보인다).
                if (GameFeatures.Of(info.Type) is GameFeature owner && !GameFeatures.IsEnabled(owner)) continue;
                tabs.Add(new TabEntry { Key = info.Type.FullName, Label = info.DisplayName, Group = info.Group, Order = info.Order, Type = info.Type });
            }
            foreach (var page in CodexPage.All.Where(p => p.TabLabel != null))
                tabs.Add(new TabEntry { Key = PagePrefix + page.Key, Label = page.TabLabel, Group = page.TabGroup, Order = page.TabOrder, Page = page });
            // 개발자 폴더(Assets/Dev 바로 아래 폴더마다 탭 하나).
            var devFolders = DevFolders.Tabs();
            for (int i = 0; i < devFolders.Count; i++)
            {
                var page = DevFolderPage.For(devFolders[i]);
                tabs.Add(new TabEntry { Key = PagePrefix + page.Key, Label = page.TabLabel, Group = DevFolders.Group, Order = i, Page = page });
            }
            foreach (var feature in GameFeatures.All.Where(f => !GameFeatures.IsActive(f)))
                for (int i = 0; i < feature.TabNames.Length; i++)
                    tabs.Add(new TabEntry
                    {
                        Key = $"$off:{feature.Id}:{i}", Label = feature.TabNames[i], Group = feature.Name, Order = i,
                        DisabledReason = $"'{feature.Name}' 기능이 꺼져 있습니다. 위의 '기능 활성화'를 눌러 켜면 열립니다.",
                    });
            return tabs.OrderBy(t => GroupRank(t.Group)).ThenBy(t => t.Group, StringComparer.Ordinal).ThenBy(t => t.Order).ThenBy(t => t.Label, StringComparer.Ordinal).ToList();
        }

        static string FirstTabKey() => Tabs().FirstOrDefault(t => t.DisabledReason == null)?.Key;

        // 지금 고른 탭이 독립 화면이면 그 화면.
        CodexPage CurrentPage =>
            _selectedType != null && _selectedType.StartsWith(PagePrefix, StringComparison.Ordinal) ? CodexPage.Find(_selectedType.Substring(PagePrefix.Length)) : null;
        VisualElement _typeTools;
        GsButton _viewSettingsButton;
        GsButton _createButton;
        GsButton _linkButton;

        [MenuItem("MomoiProject/DB", priority = 0)]
        public static void Open() => OpenWindow();

        // 탭마다 옮기는 데 걸린 시간(본문 그리기까지)을 콘솔에 남긴다. 느린 탭을 찾을 때 쓴다.
        [MenuItem("Tests/DB 탭 전환 시간 재기", priority = 60)]
        static void MeasureTabs()
        {
            var window = OpenWindow();
            var back = window._selectedType;
            var lines = new List<string>();
            var watch = new System.Diagnostics.Stopwatch();
            watch.Restart();
            window.RefreshAll();
            lines.Add($"전체 새로 그리기(검증·사이드바·본문): {watch.Elapsed.TotalMilliseconds:0.0}ms");
            foreach (var tab in Tabs().Where(t => t.DisabledReason == null))
            {
                watch.Restart();
                window.Navigate(tab.Key);
                lines.Add($"{tab.Group} › {tab.Label}: {watch.Elapsed.TotalMilliseconds:0.0}ms");
            }
            window.Navigate(back);
            Debug.Log("[MomoiProject] DB 탭 전환 시간\n" + string.Join("\n", lines));
        }

        static DatabaseWindow OpenWindow()
        {
            var window = GsWindows.Get<DatabaseWindow>();
            window.titleContent = new GUIContent("DB");
            window.minSize = new Vector2(980, 560);
            return window;
        }

        // 다른 창에서 특정 종류로 이동한다. create면 새 항목 만들기 창을 바로 연다.
        public static void ShowType(Type type, bool create = false)
        {
            var window = OpenWindow();
            window.Navigate(TabOf(type), () =>
            {
                if (create) window.CreateItem();
            });
        }

        // 항목 하나를 상세 화면으로 연다.
        public static void ShowItem(GameData item)
        {
            if (item == null) return;
            var window = OpenWindow();
            window.Navigate(TabOf(item.GetType()), () =>
            {
                window._selectedId = item.Id;
                window.OpenDetail(item);
            });
        }

        // 독립 탭(클래스·이벤트)으로 이동한다. key: CodexPage.Key
        public static void ShowPage(string key)
        {
            var window = OpenWindow();
            window.Navigate(PagePrefix + key);
        }

        // 검증 오류가 있는 첫 종류를 열고 오류 항목만 거른다.
        public static void ShowInvalid()
        {
            var window = OpenWindow();
            var issue = DataValidator.ValidateAll().FirstOrDefault(i => i.Severity == ValidationSeverity.Error && i.Asset != null);
            window.Navigate(issue != null ? TabOf(issue.Asset.GetType()) : window._selectedType, () =>
            {
                window._search = "invalid";
                if (window._searchField != null) window._searchField.Value = "invalid";
                window.RebuildContent();
            });
        }

        // 종류가 보이는 탭: 사이드바에 따로 없는 종류(예: 키워드 그룹)는 같은 전용 화면을 쓰는 종류의 탭.
        static string TabOfName(string typeName)
        {
            var info = DataTypes.All.FirstOrDefault(i => i.Type.FullName == typeName);
            return info != null ? TabOf(info.Type) : typeName;
        }

        static string TabOf(Type type)
        {
            var info = DataTypes.Find(type);
            if (info == null || !info.Hidden || info.Page == null) return type.FullName;
            return DataTypes.All.FirstOrDefault(i => !i.Hidden && i.Page == info.Page)?.Type.FullName ?? type.FullName;
        }

        // ── 탭 이동과 저장 ──────────────────────────────────────
        // DB의 변경은 Unity에 바로 쓰지 않고 모아 둔다. 탭을 옮겨도 그대로 남고,
        // Ctrl+S 또는 사이드바의 저장 버튼으로 한 번에 저장한다(CodexChanges). 창을 닫거나 플레이·종료하기 전에는 묻는다.

        static CodexStaging Staging => EditorDatabase.Staging;

        void Navigate(string tab, Action after = null)
        {
            if (tab != _selectedType)
            {
                _selectedType = tab;
                _selectedId = null;
                if (_viewSettingsPanel != null) _viewSettingsPanel.style.display = DisplayStyle.None;
            }
            // 탭만 옮길 때는 검증·사이드바를 다시 하지 않는다(데이터가 바뀌지 않았다). 선택 표시만 옮기고 본문만 새로 그린다.
            if (!MarkActiveTab()) RefreshAll();
            else RebuildContent();
            after?.Invoke();
        }

        // 사이드바의 선택 표시를 지금 탭으로 옮긴다. 사이드바가 아직 없거나 지금 탭의 버튼이 없으면 false.
        bool MarkActiveTab()
        {
            if (_root == null || _navItems.Count == 0) return false;
            EnsureTab();
            var active = CurrentPage != null ? _selectedType : SelectedType?.FullName;
            if (active == null || !_navItems.ContainsKey(active)) return false;
            foreach (var pair in _navItems) pair.Value.SetActive(pair.Key == active);
            return true;
        }

        // 저장 안 된 변경을 한 줄씩 보여 준다(사이드바의 '저장 안 된 변경'을 눌렀을 때). 여기서 저장하거나 되돌릴 수 있다.
        void ShowChanges()
        {
            var rows = CodexChanges.Rows();
            GsDialog.Show(_root, "저장 안 된 변경", $"변경 {rows.Count}건. 저장(Ctrl+S)하기 전까지 에셋은 바뀌지 않습니다.", rows,
                new DialogButton("닫기", GsButtonKind.Ghost, null),
                new DialogButton("되돌리기", GsButtonKind.Default, ConfirmDiscard),
                new DialogButton("저장", GsButtonKind.Primary, () => CodexChanges.Save()));
        }

        void ConfirmDiscard()
        {
            var count = CodexChanges.Count;
            GsDialog.Confirm(_root, "변경 되돌리기", $"저장 안 된 변경 {count}건을 모두 버리고 마지막으로 저장한 상태로 돌아갑니다.", "되돌리기", CodexChanges.Discard);
        }

        void OnDestroy()
        {
            // 창을 닫아도 변경은 사라지지 않지만, 잊지 않도록 묻는다('취소'면 다음에 열 때까지 그대로 둔다).
            CodexStaging.AskBeforeLeaving("DB 창을 닫기 전에");
        }

        // 처음 열 때, 또는 없어진 탭(지운 종류·꺼진 기능의 탭)을 열어 두었으면 첫 탭으로.
        void EnsureTab()
        {
            if (_selectedType == null || !Tabs(_selectedType).Any(t => t.Key == TabOfName(_selectedType) && t.DisabledReason == null)) _selectedType = FirstTabKey();
        }

        void OnEnable()
        {
            EditorDatabase.Changed += QueueRefresh;
            EditorPolicy.Changed += QueueRefresh;
            ClassRegistry.Changed += QueueRefresh;
            CodexFolders.Changed += QueueRefresh;
            GameFeatures.Changed += QueueRefresh;
            DevFolders.Changed += QueueRefresh;
            Undo.undoRedoPerformed += QueueRefresh;
        }

        void OnDisable()
        {
            EditorDatabase.Changed -= QueueRefresh;
            EditorPolicy.Changed -= QueueRefresh;
            ClassRegistry.Changed -= QueueRefresh;
            CodexFolders.Changed -= QueueRefresh;
            GameFeatures.Changed -= QueueRefresh;
            DevFolders.Changed -= QueueRefresh;
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
            // Ctrl+S: 모아 둔 변경을 저장한다(단축키 등록이 먹지 않는 경우를 대비해 창에서도 받는다. 두 번 불려도 두 번째는 할 일이 없다).
            _root.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.S || !e.actionKey || e.shiftKey || e.altKey) return;
                e.StopPropagation();
                CodexChanges.Save();
            }, TrickleDown.TrickleDown);
            _root.style.alignItems = Align.Stretch;
            host.Add(_root);

            _sidebar = Gs.Box("gs-sidebar");
            _sidebarNav = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            _sidebarNav.AddToClassList("gs-sidebar__scroll");
            _sidebar.Add(_sidebarNav);
            _sidebarFooter = Gs.Col("gs-sidebar__footer");
            _sidebar.Add(_sidebarFooter);
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
            _typeTools = Gs.Row();
            header.Add(_typeTools);

            _searchField = new GsSearchField("ID 검색   invalid");
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
                RebuildContent();
            };
            _typeTools.Add(_views);
            _typeTools.Add(Gs.HSpace(8));
            _viewSettingsButton = new GsButton("보기 설정", ToggleViewSettings, GsButtonKind.Ghost);
            _typeTools.Add(_viewSettingsButton);
            _typeTools.Add(Gs.HSpace(8));
            // 이 종류의 필드마다 키워드 그룹·스프라이트 규격 그룹·필수를 정한다(클래스 탭의 클래스 상세와 같은 화면).
            _linkButton = new GsButton("필드 연결", () =>
            {
                if (SelectedType != null) ClassDetail.Open(SelectedType);
            }, GsButtonKind.Ghost);
            _linkButton.tooltip = "이 종류의 필드에 키워드 그룹·스프라이트 규격 그룹을 연결하고 필수 여부를 정합니다(상세 창에 열림).";
            _typeTools.Add(_linkButton);
            _typeTools.Add(Gs.HSpace(8));
            _createButton = new GsButton("+ 새로 만들기", CreateItem, GsButtonKind.Primary);
            _typeTools.Add(_createButton);
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
                EnsureTab();
                if (CurrentPage != null) return null;
                var types = DataTypes.All;
                var info = types.FirstOrDefault(t => t.Type.FullName == TabOfName(_selectedType)) ?? types.FirstOrDefault(t => !t.Hidden);
                return info?.Type;
            }
        }

        void QueueRefresh()
        {
            if (_refreshQueued || _root == null) return;
            _refreshQueued = true;
            _root.schedule.Execute(Run);

            void Run()
            {
                // 에셋을 가져오는 중(저장 직후 스크립트 다시 가져오기 등)에는 다시 그리지 않고 끝날 때까지 미룬다.
                if (EditorApplication.isUpdating)
                {
                    _root.schedule.Execute(Run).StartingIn(100);
                    return;
                }
                _refreshQueued = false;
                RefreshAll();
            }
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
            // 스크롤 위치는 그대로 둔다(목록만 다시 채운다).
            _sidebarNav.Clear();
            _sidebarFooter.Clear();
            _navItems.Clear();
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
            logo.Add(Gs.Label("MomoiProject", "gs-heading"));
            brand.Add(logo);
            _sidebarNav.Add(brand);

            var selected = SelectedType;
            var currentPage = CurrentPage;
            var shownGroups = new HashSet<string>();
            foreach (var group in Tabs(_selectedType).GroupBy(t => t.Group))
            {
                _sidebarNav.Add(Gs.Label(group.Key.ToUpperInvariant(), "gs-sidebar__group"));
                shownGroups.Add(group.Key);
                // 선택 기능의 그룹: 맨 위에 켜고 끄는 버튼(오른쪽 끝에 켜짐 여부).
                if (GameFeatures.All.FirstOrDefault(f => f.Name == group.Key) is GameFeature groupFeature) _sidebarNav.Add(FeatureToggle(groupFeature));
                foreach (var tab in group)
                {
                    var key = tab.Key;
                    if (tab.DisabledReason != null)
                    {
                        // 꺼진 기능의 탭: 보이기만 하고 들어갈 수 없다.
                        var off = new GsNavItem(tab.Label, Gs.WithAlpha(Gs.Text, 0.25f), null);
                        off.SetEnabled(false);
                        off.tooltip = tab.DisabledReason;
                        _sidebarNav.Add(off);
                        continue;
                    }
                    var item = new GsNavItem(tab.Label, Gs.PaletteFor(tab.Type != null ? tab.Type.Name : key), () => Navigate(key));
                    _navItems[key] = item;
                    if (tab.Page != null)
                    {
                        item.SetCount(tab.Page.TabCount ?? "");
                        item.SetActive(tab.Page == currentPage);
                        _sidebarNav.Add(item);
                        continue;
                    }
                    var type = tab.Type;
                    var info = DataTypes.Find(type);
                    // 전용 화면을 함께 쓰는 종류(키워드 + 키워드 그룹)는 한 탭에서 센다.
                    var types = DataTypes.All.Where(i => i.Type == type || (info.Page != null && i.Page == info.Page)).Select(i => i.Type).ToList();
                    var count = EditorDatabase.Instance.All.Count(d => types.Contains(d.GetType()));
                    var errors = _issues.Count(i => i.Severity == ValidationSeverity.Error && i.Asset != null && types.Contains(i.Asset.GetType()));
                    item.SetCount(errors > 0 ? $"{count} · 오류 {errors}" : count.ToString());
                    item.SetActive(type == selected);
                    // 플레이어 데이터: 마우스를 올리면 오른쪽에 판·프로필 클래스 파일(PlayerData.cs)로 가는 버튼.
                    if (type == typeof(PlayerDataEntry)) AddDataFileTool(item);
                    _sidebarNav.Add(item);
                }
            }

            // 탭이 하나도 없는 선택 기능도 켜고 끌 수 있게 그룹을 보인다.
            foreach (var feature in GameFeatures.All.Where(f => !shownGroups.Contains(f.Name)))
            {
                _sidebarNav.Add(Gs.Label(feature.Name.ToUpperInvariant(), "gs-sidebar__group"));
                _sidebarNav.Add(FeatureToggle(feature));
            }

            // 개발자 폴더가 아직 없으면: 그룹 제목 + 안내(누르면 Assets/Dev를 만들어 Project 창에서 고른다).
            if (!shownGroups.Contains(DevFolders.Group))
            {
                _sidebarNav.Add(Gs.Label(DevFolders.Group.ToUpperInvariant(), "gs-sidebar__group"));
                var hint = Gs.Label($"{DevFolders.Root} 안에 폴더를 만들면 여기 탭이 생깁니다", "gs-caption");
                hint.style.marginLeft = 12;
                hint.style.whiteSpace = WhiteSpace.Normal;
                hint.tooltip = "눌러서 Project 창에서 Dev 폴더 보기(없으면 만든다). 폴더 하나 = 탭 하나, 그 안의 모든 에셋이 보입니다.";
                hint.RegisterCallback<ClickEvent>(_ =>
                {
                    EditorDatabase.EnsureFolder(DevFolders.Root);
                    var folder = AssetDatabase.LoadMainAssetAtPath(DevFolders.Root);
                    Selection.activeObject = folder;
                    EditorGUIUtility.PingObject(folder);
                });
                _sidebarNav.Add(hint);
            }

            if (DataTypes.All.Count == 0)
                _sidebarNav.Add(Gs.Label("GameData를 상속한 데이터 타입이 없습니다.", "gs-caption"));


            if (CodexChanges.HasChanges)
            {
                // 저장 안 된 변경: 누르면 목록, 아래 버튼으로 저장·되돌리기. Ctrl+S로도 저장한다.
                var pending = new GsNotice($"저장 안 된 변경 {CodexChanges.Count}건", GsNoticeKind.Warning);
                pending.tooltip = "눌러서 변경 목록 보기. 저장(Ctrl+S)하기 전까지 에셋도 스크립트 파일도 바뀌지 않습니다.";
                pending.style.marginBottom = 4;
                pending.RegisterCallback<ClickEvent>(_ => ShowChanges());
                _sidebarFooter.Add(pending);
                var buttons = Gs.Row();
                buttons.style.marginBottom = 8;
                var save = new GsButton("저장  Ctrl+S", () => CodexChanges.Save(), GsButtonKind.Primary);
                save.style.flexGrow = 1;
                save.style.marginRight = 6;
                buttons.Add(save);
                buttons.Add(new GsButton("되돌리기", ConfirmDiscard, GsButtonKind.Ghost));
                _sidebarFooter.Add(buttons);
            }

            if (_hiddenEmpty > 0 || ShowEmptyTabs)
            {
                // 숨긴 빈 탭: 눌러서 보이기·다시 숨기기(이 에디터를 쓰는 동안 기억).
                var toggle = Gs.Label(ShowEmptyTabs ? "빈 탭 숨기기" : $"빈 탭 {_hiddenEmpty}개 숨김 · 보기", "gs-caption");
                toggle.tooltip = "항목이 없는 로직 종류(능력·상태 효과·패시브 …)의 탭. 이 프로젝트가 쓰기 시작하면(항목이 생기면) 저절로 보입니다.";
                toggle.style.marginLeft = 12;
                toggle.style.marginBottom = 6;
                toggle.RegisterCallback<ClickEvent>(_ =>
                {
                    ShowEmptyTabs = !ShowEmptyTabs;
                    RebuildSidebar();
                });
                _sidebarFooter.Add(toggle);
            }

            var errorCount = _issues.Count(i => i.Severity == ValidationSeverity.Error);
            var warningCount = _issues.Count(i => i.Severity == ValidationSeverity.Warning);
            _sidebarFooter.Add(errorCount > 0
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
                _sidebarFooter.Add(hint);
            }
        }

        // 판·프로필 데이터 클래스 바로가기: 탭이 아니라 누르면 그 클래스의 cs 파일을 연다.
        static void AddDataFileTool(GsNavItem item)
        {
            var (label, id, className) = StartFiles.DataShortcuts[0];
            var (file, _) = StartFiles.DataFile(id, className);
            if (file == null) return;
            item.AddHoverTool("cs ↗", "판·프로필 클래스(RunState·Profile) 파일 열기", () =>
            {
                var (now, _) = StartFiles.DataFile(id, className);
                if (now != null) StartFiles.Open(now);
            });
        }

        // ── 선택 기능 켜고 끄기 ─────────────────────────────────
        // 사이드바의 '기능 활성화' 버튼: 탭이 아니라 누르면 바로 켜고 끈다(확인 뒤). 오른쪽 끝의 스위치가 지금 상태다.

        VisualElement FeatureToggle(GameFeature feature)
        {
            var enabled = GameFeatures.IsEnabled(feature);
            var active = GameFeatures.IsActive(feature);
            var item = new GsNavItem("기능 활성화", enabled ? Gs.Success : Gs.WithAlpha(Gs.Text, 0.25f), () => ConfirmFeature(feature));
            item.AddToClassList("gs-nav-item--toggle");
            item.Add(new GsSwitch(enabled));
            item.tooltip = !feature.Installed
                ? $"이 프로젝트에 '{feature.Name}'의 코드 폴더가 없어 켤 수 없습니다."
                : enabled
                    ? (active ? "켜짐" : "켜는 중(다시 컴파일이 끝나면 탭이 열립니다)") + $" · 눌러서 끄기\n{feature.Description}"
                    : $"꺼짐 · 눌러서 켜기\n{feature.Description}";
            if (!feature.Installed && !enabled) item.SetEnabled(false);
            return item;
        }

        void ConfirmFeature(GameFeature feature)
        {
            var enable = !GameFeatures.IsEnabled(feature);
            string message;
            if (enable)
                message = $"'{feature.Name}'을(를) 켭니다. Unity가 다시 컴파일한 뒤 이 기능의 탭·메뉴가 열립니다. 기능의 클래스는 그 탭에서 다루고(DB › 클래스에는 개발자가 만든 클래스만 보입니다), 기능이 쏘는 이벤트가 있으면 DB › 이벤트의 '{feature.Name}' 묶음에 들어옵니다.\n\n{feature.Description}";
            else
            {
                var types = GameFeatures.ClassTypes(feature).ToList();
                var items = EditorDatabase.Instance.All.Count(d => types.Contains(d.GetType()));
                message = $"'{feature.Name}'을(를) 끕니다. 이 기능의 탭·메뉴·클래스가 모두 빠지고 빌드에도 들어가지 않습니다.\n" +
                          $"만들어 둔 데이터 {items}개는 지워지지 않고 남으며, 다시 켜면 돌아옵니다. 이 기능을 쓰는 게임 코드가 있으면 컴파일 오류가 납니다.";
            }
            GsDialog.Confirm(_root, enable ? "기능 켜기" : "기능 끄기", message, enable ? "켜기" : "끄기", () =>
            {
                GameFeatures.SetEnabled(feature, enable);
                Debug.Log($"[MomoiProject] '{feature.Name}'을(를) {(enable ? "켰습니다" : "껐습니다")}. 다시 컴파일합니다.");
                QueueRefresh();
            });
        }

        // ── 목록(시트·카드) ─────────────────────────────────────

        void RebuildContent()
        {
            if (_content == null) return;
            _content.Clear();
            // 독립 화면(클래스·이벤트): 그 화면이 그린다.
            var standalone = CurrentPage;
            if (standalone != null)
            {
                _views.style.display = DisplayStyle.None;
                _viewSettingsButton.style.display = DisplayStyle.None;
                _linkButton.style.display = DisplayStyle.None;
                // 클래스·이벤트·키워드는 코드다 → 만들기는 구조 편집에서만(EditorPolicy).
                _createButton.style.display = standalone.CreateLabel != null && EditorPolicy.CanEditStructure ? DisplayStyle.Flex : DisplayStyle.None;
                _createButton.Text = standalone.CreateLabel ?? "";
                _title.text = standalone.TabGroup == ClassesPage.DeclareGroup ? standalone.TabLabel : $"{standalone.TabGroup} · {standalone.TabLabel}";
                var pageContext = PageContext();
                _caption.text = standalone.Caption(pageContext);
                standalone.Build(pageContext);
                return;
            }
            _createButton.style.display = DisplayStyle.Flex;
            var type = SelectedType;
            if (type == null)
            {
                _title.text = "DB";
                _caption.text = "GameData를 상속한 클래스를 만들면 여기에 자동으로 나타납니다.";
                return;
            }

            var info = DataTypes.Find(type);
            // 전용 화면(키워드 등)이 있으면 그 화면이, 없으면 공용 목록(클래스 탭 모양, DataListPage)이 그린다. 예전 시트·카드는 LegacyViews.
            var page = info.Page != null ? CodexPage.Find(info.Page) : LegacyViews ? null : new DataListPage(type);
            _views.style.display = page != null ? DisplayStyle.None : DisplayStyle.Flex;
            _viewSettingsButton.style.display = page != null ? DisplayStyle.None : DisplayStyle.Flex;
            _linkButton.style.display = HasLinkableFields(type) ? DisplayStyle.Flex : DisplayStyle.None;
            _createButton.Text = page != null ? page.CreateLabel : "+ 새로 만들기";
            // 로직 쪽 종류는 AI가 DB 반영 스크립트로 만든다 → 만들기 버튼은 개발자가 만드는 종류([Database(DeveloperAuthored = true)])에만(EditorPolicy).
            var canAdd = EditorPolicy.CanAddRemove(type);
            if (!canAdd) _createButton.style.display = DisplayStyle.None;
            if (page != null)
            {
                _title.text = info.DisplayName;
                var context = PageContext();
                _caption.text = page.Caption(context);
                page.Build(context);
                return;
            }
            var all = EditorDatabase.Instance.OfType(type).ToList();
            var items = all.Where(Matches).ToList();
            var typeErrors = _issues.Count(i => i.Severity == ValidationSeverity.Error && i.Asset != null && i.Asset.GetType() == type);
            _title.text = info.DisplayName;
            _caption.text = (items.Count == all.Count ? $"{all.Count}개 항목" : $"{items.Count} / {all.Count}개 항목") + (typeErrors > 0 ? $"  ·  오류 {typeErrors}" : "")
                + (canAdd ? "" : "  ·  값을 바꿉니다(추가·삭제·구조는 AI에게)");

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

        // 연결을 정할 필드가 있는 종류인지(GameData 공통 칸 id 등만 있으면 없음).
        static bool HasLinkableFields(Type type) =>
            ClassTypes.FieldsOf(type).Any(f => f.DeclaringType != typeof(GameData) && (KeywordTools.CanConnect(f) || KeywordTools.IsSpriteField(f)));

        CodexPageContext PageContext() => new CodexPageContext
        {
            Host = _root,
            Content = _content,
            Search = _search ?? "",
            Issues = _issues,
            OpenDetail = item => OpenDetail(item),
            Refresh = QueueRefresh,
        };

        bool Matches(GameData data)
        {
            if (string.IsNullOrWhiteSpace(_search)) return true;
            foreach (var token in _search.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.Equals("invalid", StringComparison.OrdinalIgnoreCase))
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
                HasError = o => _issues.Any(i => i.Asset == o && i.Severity == ValidationSeverity.Error),
            };
            sheet.ColumnResized += (key, width) =>
            {
                typeView.SetWidth(key, Mathf.Round(width));
                ViewSettings.Save();
            };
            // 첫 열은 항목 ID(항목은 ID로만 식별한다).
            var columns = new List<SheetColumn> { new SheetColumn { Title = "ID", Width = typeView.WidthOf("id", 200), Path = "id", Bold = true } };
            columns.AddRange(view.Columns.Select(c => new SheetColumn { Title = c.Label, Width = typeView.WidthOf(c.Path, ColumnWidth(c)), Path = c.Path }));
            sheet.Build(columns,
                        items.Cast<UnityEngine.Object>());
            var selected = items.FirstOrDefault(i => i.Id == _selectedId);
            if (selected != null) sheet.Select(selected, false);
            sheet.Selected += o => _selectedId = ((GameData)o).Id;
            sheet.OpenRequested += (o, path) => OpenDetail((GameData)o, path);
            _content.Add(sheet);
        }
        static float ColumnWidth(FieldEntry column)
        {
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
                card.Subtitle = view.Subtitle != null ? Value(so, view.Subtitle) : "";

                if (view.Badge != null)
                {
                    var badge = Value(so, view.Badge);
                    if (!string.IsNullOrEmpty(badge)) card.AddBadge(badge, Gs.PaletteFor(view.Badge.Path + ":" + badge));
                }
                if (_issues.Any(i => i.Asset == item && i.Severity == ValidationSeverity.Error))
                    card.AddBadge("오류", Gs.Danger);
                if (Staging.IsPending(item)) card.AddBadge(Staging.IsAdded(item) ? "새 항목 · 적용 전" : "변경 · 적용 전", Gs.Warning);

                foreach (var stat in view.Stats)
                {
                    var property = so.FindProperty(stat.Path);
                    if (property == null) continue;
                    card.AddStat(stat.Label, GsInlineProperty.Describe(property).text);
                }
                grid.Add(card);
            }
            _content.Add(grid);
        }

        static string Value(SerializedObject so, FieldEntry entry)
        {
            var property = so.FindProperty(entry.Path);
            return property == null ? "" : PropertyUtil.Summary(property);
        }

        void CreateItem()
        {
            if (CurrentPage is CodexPage standalone)
            {
                standalone.Create(PageContext());
                return;
            }
            var type = SelectedType;
            if (type == null) return;
            var info = DataTypes.Find(type);
            if (info.Page != null && CodexPage.Find(info.Page) is CodexPage page)
            {
                page.Create(PageContext());
                return;
            }
            if (!LegacyViews)
            {
                new DataListPage(type).Create(PageContext());
                return;
            }
            var db = EditorDatabase.Instance;
            // 이름(ID)은 여기서 한 번 짓는다. 이후 모든 곳에서는 목록에서 고른다.
            GsDialog.Form(_root, $"새 {info.DisplayName}", "ID는 저장 파일·코드(GameIds)·조건이 함께 쓰는 식별자입니다. 나중에 바꿔도 참조가 모두 따라갑니다.",
                new[]
                {
                    new DialogField { Key = "id", Label = "ID", Value = db.NextId(info), Caption = "영문 소문자로 시작, 단어 사이는 대문자(띄어쓰기·밑줄 없음), 분류는 점(.)으로. 예: playerGold, player.inventory" },
                },
                "만들기",
                values => db.CanUseId(values["id"], null, type, out var error) ? null : error,
                values =>
                {
                    var created = Staging.Add(type, values["id"]);
                    _selectedId = created.Id;
                    _search = "";
                    _searchField.Value = "";
                    RefreshAll();
                    OpenDetail(created);
                });
        }


        // ID 바꾸기도 모아 둔다(CodexStaging.Rename): 이 항목과, 이 ID를 가리키는 항목들의 작업본을 함께 고친다(DB가 바로 새 ID로 보인다).
        // 적용하면 내용을 원본에 옮기고 ID·파일 이름·변경 이력·이름 텍스트·GameIds가 바뀐다(참조는 다시 훑지 않는다 — 서로 바꾸기·비운 ID 다시 쓰기도 맞다).
        // 돌려주는 값: 함께 바꾼 참조 수(같은 ID를 쓰는 항목이 또 있어 참조를 옮기지 않았으면 -1).
        static int StageRename(GameData item, string newId) => Staging.Rename(item, newId);

        // ── 상세 패널(모든 카드·시트 공용) ──────────────────────
        // 패널은 작업본(메모리 사본)을 고친다. '반영'하면 DB 목록에 바로 보이고, Unity 에셋에는 저장(Ctrl+S)할 때 적용된다.

        // 상세 창(탭)으로 연다. 적용·되돌리기로 작업본이 바뀌면 상세 창이 같은 설정 함수로 다시 연다.
        void OpenDetail(GameData item, string focusPath = null)
        {
            _selectedId = item.Id;
            var first = true;
            DetailWindow.OpenItem(item, work =>
            {
                var options = DetailOptionsFor(work, first ? focusPath : null);
                first = false;
                return options;
            });
        }

        DetailOptions DetailOptionsFor(GameData item, string focusPath)
        {
            var info = DataTypes.Find(item.GetType());
            var target = item;
            var options = new DetailOptions
            {
                TitlePath = null,
                Title = item.Id,
                Subtitle = info?.DisplayName + (Staging.IsAdded(item) ? "  ·  새 항목(적용 전)" : ""),
                ShowMedia = false,
                // 제목(ID)을 눌러 바로 고친다. 이 ID를 가리키는 곳도 함께 바뀌고, 적용은 저장(Ctrl+S)할 때.
                // ID는 코드(GameIds)가 쓰므로 로직 쪽 종류는 AI가 바꾼다(EditorPolicy) — 그때는 제목을 고칠 수 없다.
                TitleValidate = !EditorPolicy.CanAddRemove(item) ? null : (Func<string, string>)(id => id == target.Id ? null : EditorDatabase.Instance.CanUseId(id, target, out var error) ? null : error),
                TitleCommit = !EditorPolicy.CanAddRemove(item) ? null : (Action<GsDetailPanel, string>)((panel, id) =>
                {
                    var oldId = target.Id;
                    var count = StageRename(target, id);
                    DetailWindow.Renamed(target, oldId);
                    if (panel.DraftTarget is GameData draft) draft.EditorAssignId(id);
                    _selectedId = id;
                    QueueRefresh();
                    Debug.Log(count < 0
                        ? $"[MomoiProject] ID를 {id}(으)로 바꿨습니다. 예전 ID '{oldId}'를 쓰는 항목이 또 있어(복제한 에셋) 참조는 옮기지 않았습니다 — 그 칸들은 남은 항목을 그대로 가리킵니다. 저장(Ctrl+S)할 때 적용."
                        : $"[MomoiProject] ID를 {id}(으)로 바꿨습니다(참조 {count}곳 함께, 저장(Ctrl+S)할 때 적용).");
                }),
                Placeholder = Gs.PaletteFor(item.GetType().Name),
                FocusPath = focusPath,
                Staged = true,
                Summary = draft => DescribeItem((GameData)draft),
                Validate = draft => ValidateDraft(target, (GameData)draft),
                Saved = _ => Staging.Touch(),
            };
            options.Actions.Add(("에셋 위치", _ =>
            {
                var original = Staging.OriginalOf(target);
                if (original != null) EditorGUIUtility.PingObject(original);
                else Debug.Log("[MomoiProject] 아직 적용하지 않은 새 항목이라 에셋이 없습니다.");
            }));
            if (item is PrefabData prefabEntry)
            {
                // 프리팹은 DB에서 지우지 않는다. 프로젝트 창의 프리팹으로 이동해 거기서 지운다(항목은 자동 정리).
                options.Actions.Add(("프로젝트에서 보기", _ => ShowInProject(prefabEntry.prefab)));
            }
            else if (EditorPolicy.CanAddRemove(item))
            {
                options.Actions.Add(("삭제", panel => Delete(item, panel)));
            }
            return options;
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
                    var when = achievement.EffectiveTrigger;
                    var outcomes = BoundOutcomes.Has(achievement.Id) ? BoundOutcomes.For(achievement.Id).Select(c => "코드: " + c.Name).ToList()
                                 : achievement.EffectiveOutcomes.Select(o => o.Natural(names)).ToList();
                    return new List<(string, string)>
                    {
                        ("트리거", when != null ? Gs.Dual(when.Natural(names), when.Describe(names)) : "트리거 없음"),
                        ("결과", outcomes.Count > 0 ? string.Join(",  ", outcomes) : "없음"),
                    };
                // 가중치 표: 줄마다 최종 %(안쪽 표까지 곱한 전체 기준 — 조건은 맞다고, 밸런스 값은 기준값으로).
                case WeightTableData table:
                    var chances = table.Preview(id => names.FindById(id) as WeightTableData, id => (names.FindById(id) as BalanceData)?.value);
                    return new List<(string, string)>
                    {
                        ("확률", chances.Count == 0 ? "없음" : string.Join("  ·  ", chances.Select(c => $"{new string('›', c.depth)}{(c.depth > 0 ? " " : "")}{c.label} {c.percent:0.##}%"))),
                    };
                // 선택 요소 '능력'(프로젝트의 DB 종류, 규격 IUsableData).
                case IUsableData usable when usable.ToUsable() is AbilityDefinition ability:
                    var abilityOutcomes = BoundOutcomes.Has(item.Id) ? BoundOutcomes.For(item.Id).Select(c => "코드: " + c.Name).ToList()
                                        : ability.Outcomes.Select(o => o.Natural(names)).ToList();
                    return new List<(string, string)>
                    {
                        ("트리거", ability.When != null ? Gs.Dual(ability.When.Natural(names), ability.When.Describe(names)) : "사용할 때"),
                        ("결과", abilityOutcomes.Count > 0 ? string.Join(",  ", abilityOutcomes) : "없음"),
                    };
                case StatData stat:
                    var rules = stat.AllRules().Select(r => r.Natural(names)).ToList();
                    var rulesCode = stat.AllRules().Select(r => r.Describe(names)).ToList();
                    var statLines = new List<(string, string)>
                    {
                        ("방식", $"{(stat.scope == StatScope.Run ? "이번 판" : "누적")} · {StatSystem.AggregateText(stat.aggregate)}"),
                        ("반영", rules.Count > 0 ? Gs.Dual(string.Join("\n", rules), string.Join("\n", rulesCode)) : "반영 규칙 없음"),
                    };
                    if (BoundOutcomes.Has(stat.Id)) statLines.Add(("결과", string.Join(",  ", BoundOutcomes.For(stat.Id).Select(c => "코드: " + c.Name)) + " (로그에 기록 대신)"));
                    return statLines;
            }
            return null;
        }
        static void ShowInProject(UnityEngine.Object asset)
        {
            if (asset == null)
            {
                Debug.LogWarning("[MomoiProject] 연결된 프리팹이 없습니다.");
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
                GsDialog.Show(_root, "DB에서 지울 수 없습니다", "프리팹은 프로젝트 창에서 직접 지우세요. '프로젝트에서 보기'로 이동할 수 있고, 프리팹을 지우면 이 항목도 자동으로 정리됩니다.", null,
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
            GsDialog.Confirm(_root, "항목 삭제", $"'{item.DisplayName}'을(를) 삭제합니다. 저장(Ctrl+S)하면 에셋이 휴지통으로 이동합니다.", "삭제", () =>
            {
                panel.Close();
                Staging.Remove(item);
                _selectedId = null;
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
