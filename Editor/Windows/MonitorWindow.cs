using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GBible.EditorTools.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GBible.EditorTools
{
    // 런타임 모니터: 통합 기록(인과 추적), 데이터, 도전과제, 수치, 행동(Undo), 디버그 명령.
    public sealed class MonitorWindow : EditorWindow
    {
        enum Tab
        {
            Timeline,
            Data,
            Achievements,
            Stats,
            Events,
            Actions,
            Commands,
        }

        // 데이터(현재 세션·플레이어의 모든 값)는 탐색기로, 나머지는 기능별 화면.
        static readonly string[] TabNames = { "타임라인", "데이터", "도전과제", "로그", "이벤트", "행동", "명령" };

        [SerializeField] Tab _tab;
        [SerializeField] string _search = "";
        // 타임라인: 이 값(플레이어 데이터)과 관련된 기록만.
        [SerializeField] string _keyFilter = "";
        [SerializeField] string _eventTypeName;
        [SerializeField] string _selectedAchievement;
        [SerializeField] string _selectedStat;

        readonly Dictionary<string, double> _statAmounts = new Dictionary<string, double>();
        readonly Dictionary<string, object> _eventInstances = new Dictionary<string, object>();
        VisualElement _feedback;
        VisualElement _dock;

        // 테스트에서 런타임 없이 세션을 주입하기 위한 통로. 기본: 멀티 시험 모드·방장이면 머리에서 고른 세션(서버·클라이언트), 아니면 이 화면의 세션.
        internal static Func<GameSession> SessionSource = PickedSession;

        // 머리에서 고른 멀티 세션 이름(null = 이 화면의 세션).
        static string _picked;

        static GameSession PickedSession()
        {
            if (_picked != null)
                foreach (var (name, session) in GameNet.Sessions)
                    if (name == _picked) return session;
            return GBibleRuntime.Session;
        }
        internal static int TabCount => TabNames.Length;
        internal static string TabKey(int index) => ((Tab)index).ToString();

        internal void ShowTab(int index)
        {
            _tab = (Tab)index;
            Build(rootVisualElement);
        }

        VisualElement _root;
        VisualElement _sidebar;
        VisualElement _body;
        Label _sessionInfo;
        GameSession _session;
        long _lastJournalId = -1;
        string _lastChoices = "";
        int _lastUndoCount = -1;
        bool _paused;
        long _selectedEntry;
        readonly HashSet<long> _chain = new HashSet<long>();
        readonly HashSet<JournalKind> _hiddenKinds = new HashSet<JournalKind>();
        readonly List<LogRow> _rows = new List<LogRow>();
        // 펼친 기록 id(묶어 보기). 창을 다시 그려도 유지된다.
        readonly HashSet<long> _expanded = new HashSet<long>();
        readonly Dictionary<long, List<JournalEntry>> _children = new Dictionary<long, List<JournalEntry>>();
        [SerializeField] bool _grouped = true;
        ListView _timeline;
        VisualElement _chainPanel;

        [MenuItem("MomoiProject/모니터", priority = 1)]
        public static void Open()
        {
            var window = GsWindows.Get<MonitorWindow>();
            window.titleContent = new GUIContent("모니터");
            window.minSize = new Vector2(900, 480);
        }

        void OnEnable() => EditorApplication.playModeStateChanged += OnPlayModeChanged;
        void OnDisable() => EditorApplication.playModeStateChanged -= OnPlayModeChanged;

        void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode || change == PlayModeStateChange.EnteredEditMode)
                rootVisualElement.schedule.Execute(() => Build(rootVisualElement)).StartingIn(50);
        }

        void CreateGUI() => Build(rootVisualElement);

        internal void Build(VisualElement host)
        {
            host.Clear();
            _session = SessionSource();
            _lastJournalId = -1;
            _root = Gs.Row();
            Gs.Apply(_root);
            _root.style.alignItems = Align.Stretch;
            host.Add(_root);

            _sidebar = Gs.Box("gs-sidebar");
            _sidebar.style.width = 196;
            _root.Add(_sidebar);

            var main = Gs.Col("gs-grow");
            main.style.paddingLeft = 28;
            main.style.paddingRight = 28;
            main.style.paddingTop = 24;
            main.style.minWidth = 0;
            _root.Add(main);

            var header = Gs.Row();
            header.style.marginBottom = 18;
            var titles = Gs.Col();
            titles.Add(Gs.Label(TabNames[(int)_tab], "gs-display"));
            _sessionInfo = Gs.Label("", "gs-caption");
            _sessionInfo.style.marginTop = 4;
            titles.Add(_sessionInfo);
            header.Add(titles);
            header.Add(Gs.Spacer());
            // 멀티(시험 모드·방장): 서버 쪽·클라이언트 쪽 세션을 골라 본다.
            var sessions = GameNet.Sessions.ToList();
            if (sessions.Count > 1)
            {
                var names = sessions.Select(s => s.name).ToArray();
                var picker = new GsSegmented(names);
                picker.Select(Math.Max(0, Array.FindIndex(sessions.ToArray(), s => s.session == _session)));
                picker.Changed += index =>
                {
                    _picked = names[index];
                    Build(rootVisualElement);
                };
                header.Add(picker);
                header.Add(Gs.HSpace(16));
            }
            if (_session != null)
            {
                if (Application.isPlaying)
                {
                    header.Add(TimeControls());
                    header.Add(Gs.HSpace(16));
                }
                var undo = new GsButton("Undo", UndoLast);
                undo.tooltip = _session.Options.History == HistoryMode.Snapshots
                    ? "마지막 스냅샷으로 되감습니다. 행동이 바꾼 값·시간이 돌아가고, 달성한 도전과제와 보상은 유지됩니다."
                    : "마지막 행동을 되돌립니다(몇 번이든 뒤에서부터). 행동이 바꾼 값이 돌아가고, 달성한 도전과제와 보상은 유지됩니다.";
                undo.SetEnabled(_session.Options.History != HistoryMode.None);
                header.Add(undo);
            }
            main.Add(header);

            _feedback = Gs.Col();
            main.Add(_feedback);

            _body = Gs.Col("gs-grow");
            main.Add(_body);

            // 선택한 항목을 조작하는 줄(본문 아래, 오른쪽 정렬). 본문은 이 줄 위에서 끝난다.
            _dock = Gs.Row("gs-dock");
            _dock.style.display = DisplayStyle.None;
            main.Add(_dock);

            BuildSidebar();

            if (_session == null)
            {
                var empty = Gs.Box("gs-empty");
                empty.Add(Gs.Label(EditorApplication.isPlaying ? "세션이 없습니다" : "플레이 모드에서 실시간 값이 표시됩니다", "gs-title"));
                empty.Add(Gs.Space(8));
                empty.Add(Gs.Label(EditorApplication.isPlaying
                    ? "GBibleRuntime이 시작되지 않았습니다."
                    : "플레이를 시작하면 모든 시스템의 값, 기록, 행동을 여기서 확인하고 조작할 수 있습니다.", "gs-caption"));
                _body.Add(empty);
                UpdateInfo();
                return;
            }

            BuildTab();
            _root.schedule.Execute(Tick).Every(250);
        }

        void BuildSidebar()
        {
            var brand = Gs.Row();
            brand.style.marginLeft = 12;
            brand.style.marginBottom = 10;
            var dot = new VisualElement();
            dot.style.width = 8;
            dot.style.height = 8;
            dot.style.borderTopLeftRadius = dot.style.borderTopRightRadius = dot.style.borderBottomLeftRadius = dot.style.borderBottomRightRadius = 4;
            dot.style.backgroundColor = _session != null ? Gs.Success : new Color(1, 1, 1, 0.2f);
            dot.style.marginRight = 10;
            brand.Add(dot);
            brand.Add(Gs.Label(_session != null ? "실행 중" : "대기", "gs-heading"));
            _sidebar.Add(brand);
            _sidebar.Add(Gs.Label("모니터", "gs-sidebar__group"));

            for (int i = 0; i < TabNames.Length; i++)
            {
                var tab = (Tab)i;
                var item = new GsNavItem(TabNames[i], Gs.Palette[(i * 3 + 6) % Gs.Palette.Length], () =>
                {
                    _tab = tab;
                    Build(rootVisualElement);
                });
                item.SetActive(tab == _tab);
                _sidebar.Add(item);
            }
        }

        void Tick()
        {
            if (_session == null || _session != SessionSource())
            {
                if (SessionSource() != _session) Build(rootVisualElement);
                return;
            }
            UpdateInfo();
            if (_paused) return;
            // 입력 중인 칸이 있으면 화면을 다시 그리지 않는다(입력값이 날아가지 않게).
            var focused = _root.focusController?.focusedElement as VisualElement;
            if (focused != null && (_body.Contains(focused) || _dock.Contains(focused))) return;
            var last = _session.Journal.LastId;
            if (last == _lastJournalId && _session.UndoCount == _lastUndoCount) return;
            _lastJournalId = last;
            _lastUndoCount = _session.UndoCount;
            if (_tab == Tab.Timeline) RefreshTimeline();
            // 데이터 탭은 들어가 있는 폴더를 유지한 채 값만 다시 그린다.
            else if (_tab == Tab.Data) _dataExplorer?.Refresh();
            else if (_tab != Tab.Commands) BuildTab();
            // 명령 탭은 기다리는 선택이 바뀔 때만 다시 그린다(누르는 중인 버튼이 사라지지 않게).
            else if (ChoiceKey() != _lastChoices) BuildTab();
        }

        void UpdateInfo()
        {
            if (_sessionInfo == null) return;
            if (_session == null)
            {
                _sessionInfo.text = "세션 없음";
                return;
            }
            _sessionInfo.text = $"시드 {_session.Random.MasterSeed}  ·  틱 {_session.Clock.Tick}  ·  {_session.Clock.Time:0.0}초  ·  " +
                                $"{HistoryText()}  ·  기록 {_session.Journal.Count}/{_session.Journal.Capacity}" +
                                (_session.Data.MissingRequired.Any() ? $"  ·  필수 값 비어 있음 {_session.Data.MissingRequired.Count()}" : "");
        }

        void BuildTab()
        {
            _body.Clear();
            ShowDock(null);
            switch (_tab)
            {
                case Tab.Timeline: BuildTimeline(); break;
                case Tab.Data: BuildData(); break;
                case Tab.Achievements: BuildAchievements(); break;
                case Tab.Stats: BuildStats(); break;
                case Tab.Events: BuildEvents(); break;
                case Tab.Actions: BuildActions(); break;
                case Tab.Commands: BuildCommands(); break;
            }
        }

        // ── 타임라인 ───────────────────────────────────────────
        // 묶어 보기: 원인이 없는 기록(행동·Undo·되감기·시스템)을 한 줄로 두고, 그 때문에 일어난 기록을 아래에 접어 둔다.
        // 행동 → 이벤트 → 수치 변화 → 달성 → 보상처럼 여러 단계로 이어지며, 단계마다 접고 펼칠 수 있다.
        // 시간순: 모든 기록을 최신순으로 평평하게 본다.

        sealed class LogRow
        {
            public JournalEntry Entry;
            public int Depth;
            public int Descendants;
            public bool Expanded;
            public string Summary;
        }

        VisualElement MakeLogRow()
        {
            var row = Gs.Box("gs-log-row");
            var indent = new VisualElement();
            indent.style.flexShrink = 0;
            row.Add(indent);
            var chevron = Gs.Label("", "gs-log-row__chevron");
            chevron.RegisterCallback<ClickEvent>(e =>
            {
                if (!(chevron.userData is LogRow data) || data.Descendants == 0) return;
                e.StopPropagation();
                // Shift+클릭: 아래 단계까지 모두 펼치기/접기.
                SetExpanded(data.Entry.Id, !data.Expanded, e.shiftKey);
                RefreshTimeline();
            });
            row.Add(chevron);
            var badge = new GsBadge("", Color.white);
            badge.style.width = 64;
            badge.style.flexShrink = 0;
            row.Add(badge);
            row.Add(Gs.Label("", "gs-grow", "gs-nowrap"));
            row.Add(Gs.Label("", "gs-caption", "gs-nowrap", "gs-log-row__count"));
            row.Add(Gs.Label("", "gs-caption", "gs-nowrap"));
            // 내용 객체(이벤트·행동·클래스 값)가 있으면 탐색기로 연다.
            var open = new GsButton("열기", null, GsButtonKind.Ghost);
            open.AddToClassList("gs-log-row__open");
            open.AddManipulator(new Clickable(() =>
            {
                if (open.userData is JournalEntry entry && entry.Payload != null)
                    GsDataExplorer.BrowseModal(_root, $"{KindName(entry.Kind)} · {entry.Message}", entry.Payload, null);
            }));
            open.tooltip = "담긴 내용을 탐색기로 봅니다(읽기 전용)";
            row.Add(open);
            return row;
        }

        void BindLogRow(VisualElement row, int index)
        {
            var data = _rows[index];
            var entry = data.Entry;
            row[0].style.width = data.Depth * 20;
            var chevron = (Label)row[1];
            chevron.userData = data;
            chevron.text = data.Descendants == 0 ? "" : data.Expanded ? "▾" : "▸";
            chevron.tooltip = data.Descendants == 0 ? "" : "클릭: 펼치기/접기 · Shift+클릭: 아래 단계까지 모두";
            var badge = (GsBadge)row[2];
            badge.Text = KindName(entry.Kind);
            badge.SetColor(KindColor(entry.Kind));
            Gs.SetText((Label)row[3], Gs.Dual(entry.Message, entry.Code));
            ((Label)row[4]).text = data.Descendants > 0 && !data.Expanded ? data.Summary : "";
            ((Label)row[5]).text = $"#{entry.Id} · 틱 {entry.Tick}";
            row[6].userData = entry;
            row[6].style.visibility = entry.Payload != null ? Visibility.Visible : Visibility.Hidden;
            row.EnableInClassList("gs-log-row--root", _grouped && data.Depth == 0);
            row.EnableInClassList("gs-log-row--chain", _selectedEntry != 0 && _chain.Contains(entry.Id));
            row.EnableInClassList("gs-log-row--dim", _selectedEntry != 0 && !_chain.Contains(entry.Id));
        }

        void BuildTimeline()
        {
            var tools = Gs.Row();
            tools.style.marginBottom = 12;
            var search = new GsSearchField("기록 검색");
            search.style.width = 220;
            search.Value = _search;
            search.Changed += v =>
            {
                _search = v;
                RefreshTimeline();
            };
            tools.Add(search);
            tools.Add(Gs.HSpace(8));

            GsButton keyFilter = null;
            keyFilter = new GsButton(KeyFilterText(), () =>
            {
                var state = _session.Data;
                var used = new HashSet<string>(_session.Journal.Entries.Where(e => !string.IsNullOrEmpty(e.Key)).Select(e => e.Key));
                var items = new List<GsDropdownItem>
                {
                    new GsDropdownItem("모든 값", () =>
                    {
                        _keyFilter = "";
                        keyFilter.Text = KeyFilterText();
                        RefreshTimeline();
                    }) { Selected = string.IsNullOrEmpty(_keyFilter) },
                };
                items.AddRange(state.Keys.Where(used.Contains).OrderBy(k => k, StringComparer.Ordinal).Select(key => new GsDropdownItem(state.LabelOf(key), () =>
                {
                    _keyFilter = key;
                    keyFilter.Text = KeyFilterText();
                    RefreshTimeline();
                }) { Caption = key, Group = IdRules.GroupOf(key), Selected = _keyFilter == key }));
                GsDropdown.Show(keyFilter, items, placeholder: "값 검색");
            }, GsButtonKind.Ghost);
            keyFilter.tooltip = "이 값이 바뀐 기록만 봅니다(묶어 보기에서는 그 원인 줄도 함께).";
            tools.Add(keyFilter);
            tools.Add(Gs.HSpace(12));

            foreach (JournalKind kind in Enum.GetValues(typeof(JournalKind)))
            {
                var k = kind;
                var chip = new GsBadge(KindName(kind), KindColor(kind));
                chip.style.opacity = _hiddenKinds.Contains(kind) ? 0.3f : 1f;
                chip.RegisterCallback<ClickEvent>(_ =>
                {
                    if (!_hiddenKinds.Remove(k)) _hiddenKinds.Add(k);
                    chip.style.opacity = _hiddenKinds.Contains(k) ? 0.3f : 1f;
                    RefreshTimeline();
                });
                chip.tooltip = "클릭해서 보이기/숨기기";
                tools.Add(chip);
            }
            tools.Add(Gs.Spacer());
            _body.Add(tools);

            var viewTools = Gs.Row();
            viewTools.style.marginBottom = 10;
            var mode = new GsSegmented("묶어 보기", "시간순");
            mode.Select(_grouped ? 0 : 1);
            mode.Changed += index =>
            {
                _grouped = index == 0;
                RefreshTimeline();
            };
            mode.tooltip = "묶어 보기: 행동(또는 Undo·되감기) 한 줄 아래에 그로 인해 일어난 기록을 접어 둡니다.";
            viewTools.Add(mode);
            viewTools.Add(Gs.HSpace(12));
            var expandAll = new GsButton("모두 펼치기", () =>
            {
                foreach (var row in _rows) SetExpanded(row.Entry.Id, true, true);
                RefreshTimeline();
            }, GsButtonKind.Ghost);
            var collapseAll = new GsButton("모두 접기", () =>
            {
                _expanded.Clear();
                RefreshTimeline();
            }, GsButtonKind.Ghost);
            viewTools.Add(expandAll);
            viewTools.Add(collapseAll);
            viewTools.Add(Gs.Spacer());
            var pause = new GsButton(_paused ? "재개" : "일시정지", null, GsButtonKind.Ghost);
            pause.AddManipulator(new Clickable(() =>
            {
                _paused = !_paused;
                pause.Text = _paused ? "재개" : "일시정지";
            }));
            viewTools.Add(pause);
            _body.Add(viewTools);

            var split = Gs.Row("gs-grow");
            split.style.alignItems = Align.Stretch;
            _body.Add(split);

            _timeline = new ListView
            {
                fixedItemHeight = 34,
                selectionType = SelectionType.Single,
                makeItem = MakeLogRow,
                bindItem = BindLogRow,
                itemsSource = _rows,
            };
            _timeline.style.flexGrow = 1;
            _timeline.selectionChanged += selection =>
            {
                var row = selection.OfType<LogRow>().FirstOrDefault();
                _selectedEntry = row?.Entry.Id ?? 0;
                UpdateChain();
            };
            // 두 번 클릭(또는 Enter): 펼치기/접기.
            _timeline.itemsChosen += items =>
            {
                var row = items.OfType<LogRow>().FirstOrDefault();
                if (row == null || row.Descendants == 0) return;
                SetExpanded(row.Entry.Id, !row.Expanded, false);
                RefreshTimeline();
            };
            split.Add(_timeline);

            _chainPanel = Gs.Box("gs-panel");
            _chainPanel.style.width = 340;
            _chainPanel.style.marginLeft = 16;
            _chainPanel.style.marginBottom = 16;
            split.Add(_chainPanel);

            RefreshTimeline();
        }

        string KeyFilterText() =>
            string.IsNullOrEmpty(_keyFilter) || _session == null ? "모든 값 ▾" : (_session.Data.IsDefined(_keyFilter) ? _session.Data.LabelOf(_keyFilter) : _keyFilter) + " ▾";

        // deep: 이 기록 아래의 모든 단계에도 적용한다.
        void SetExpanded(long id, bool expanded, bool deep)
        {
            if (expanded) _expanded.Add(id);
            else _expanded.Remove(id);
            if (!deep || !_children.TryGetValue(id, out var children)) return;
            foreach (var child in children) SetExpanded(child.Id, expanded, true);
        }

        void RefreshTimeline()
        {
            if (_timeline == null || _session == null) return;
            var journal = _session.Journal;
            var all = journal.Entries.ToList(); // 오래된 것부터
            var byId = new Dictionary<long, JournalEntry>();
            foreach (var entry in all) byId[entry.Id] = entry;

            bool Matches(JournalEntry e) =>
                (string.IsNullOrEmpty(_keyFilter) || e.Key == _keyFilter) &&
                (string.IsNullOrEmpty(_search) ||
                 (e.Message ?? "").IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                 (e.Code ?? "").IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                 (e.Key ?? "").IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0);
            var filtering = !string.IsNullOrEmpty(_search) || !string.IsNullOrEmpty(_keyFilter);

            _rows.Clear();
            _children.Clear();
            if (!_grouped)
            {
                for (int i = all.Count - 1; i >= 0; i--)
                    if (!_hiddenKinds.Contains(all[i].Kind) && Matches(all[i])) _rows.Add(new LogRow { Entry = all[i] });
                _timeline.RefreshItems();
                UpdateChain();
                return;
            }

            // 보이는 기록: 종류 필터 통과 + (검색어가 있으면) 검색에 맞는 기록과 그 원인들(맥락이 보이도록).
            var visible = new HashSet<long>();
            var searchOpen = new HashSet<long>();
            foreach (var entry in all)
            {
                if (_hiddenKinds.Contains(entry.Kind) || !Matches(entry)) continue;
                visible.Add(entry.Id);
                if (!filtering) continue;
                for (var cause = entry.CauseId; cause != 0 && byId.TryGetValue(cause, out var parent); cause = parent.CauseId)
                {
                    if (_hiddenKinds.Contains(parent.Kind)) continue;
                    visible.Add(parent.Id);
                    searchOpen.Add(parent.Id);
                }
            }

            // 부모 = 가장 가까운 보이는 원인. 숨긴 종류는 건너뛰고 그 위에 붙인다.
            var roots = new List<JournalEntry>();
            foreach (var entry in all)
            {
                if (!visible.Contains(entry.Id)) continue;
                long parentId = 0;
                for (var cause = entry.CauseId; cause != 0 && byId.TryGetValue(cause, out var parent); cause = parent.CauseId)
                    if (visible.Contains(cause))
                    {
                        parentId = cause;
                        break;
                    }
                if (parentId == 0) roots.Add(entry);
                else
                {
                    if (!_children.TryGetValue(parentId, out var list)) _children[parentId] = list = new List<JournalEntry>();
                    list.Add(entry);
                }
            }

            var counts = new Dictionary<long, int>();
            int CountOf(long id)
            {
                if (counts.TryGetValue(id, out var cached)) return cached;
                int total = 0;
                if (_children.TryGetValue(id, out var list))
                    foreach (var child in list) total += 1 + CountOf(child.Id);
                counts[id] = total;
                return total;
            }

            void Add(JournalEntry entry, int depth)
            {
                var expanded = _expanded.Contains(entry.Id) || searchOpen.Contains(entry.Id);
                var row = new LogRow { Entry = entry, Depth = depth, Descendants = CountOf(entry.Id), Expanded = expanded };
                if (row.Descendants > 0 && !expanded) row.Summary = SummaryOf(entry.Id, row.Descendants);
                _rows.Add(row);
                if (!expanded || !_children.TryGetValue(entry.Id, out var list)) return;
                foreach (var child in list) Add(child, depth + 1);
            }

            for (int i = roots.Count - 1; i >= 0; i--) Add(roots[i], 0);
            _timeline.RefreshItems();
            UpdateChain();
        }

        // 접힌 줄 옆 요약: "하위 12 · 데이터 8 · 이벤트 2 · 보상 1"
        string SummaryOf(long id, int total)
        {
            var kinds = new Dictionary<JournalKind, int>();
            void Walk(long parent)
            {
                if (!_children.TryGetValue(parent, out var list)) return;
                foreach (var child in list)
                {
                    kinds.TryGetValue(child.Kind, out var n);
                    kinds[child.Kind] = n + 1;
                    Walk(child.Id);
                }
            }
            Walk(id);
            var parts = kinds.OrderByDescending(k => k.Value).Take(3).Select(k => $"{KindName(k.Key)} {k.Value}");
            return $"하위 {total}  ·  " + string.Join(" · ", parts);
        }

        void UpdateChain()
        {
            _chain.Clear();
            _chainPanel.Clear();
            _chainPanel.Add(Gs.Label("인과 관계", "gs-title"));
            _chainPanel.Add(Gs.Label("기록을 선택하면 원인과 결과를 따라갑니다.", "gs-caption"));
            _chainPanel.Add(Gs.Space(14));

            if (_selectedEntry != 0 && _session != null)
            {
                var causes = _session.Journal.CauseChain(_selectedEntry);
                var effects = _session.Journal.Consequences(_selectedEntry);
                foreach (var e in causes) _chain.Add(e.Id);
                foreach (var e in effects) _chain.Add(e.Id);

                int depth = 0;
                foreach (var entry in causes)
                    _chainPanel.Add(ChainRow(entry, depth++, entry.Id == _selectedEntry));
                foreach (var entry in effects)
                    _chainPanel.Add(ChainRow(entry, depth + Depth(entry, _selectedEntry), false));
            }
            _timeline?.RefreshItems();
        }

        int Depth(JournalEntry entry, long rootId)
        {
            int depth = 0;
            for (var e = entry; e != null && e.Id != rootId && depth < 32; e = _session.Journal.Find(e.CauseId)) depth++;
            return depth - 1;
        }

        static VisualElement ChainRow(JournalEntry entry, int depth, bool selected)
        {
            var row = Gs.Row();
            row.style.paddingLeft = Math.Min(depth, 8) * 14;
            row.style.minHeight = 26;
            var marker = new VisualElement();
            marker.style.width = 6;
            marker.style.height = 6;
            marker.style.borderTopLeftRadius = marker.style.borderTopRightRadius = marker.style.borderBottomLeftRadius = marker.style.borderBottomRightRadius = 3;
            marker.style.backgroundColor = selected ? Gs.Accent : KindColor(entry.Kind);
            marker.style.marginRight = 10;
            row.Add(marker);
            var label = Gs.Label(Gs.Dual(entry.Message, entry.Code), "gs-grow");
            if (!selected) label.AddToClassList("gs-text-2");
            row.Add(label);
            return row;
        }

        static string KindName(JournalKind kind)
        {
            switch (kind)
            {
                case JournalKind.Action: return "행동";
                case JournalKind.Event: return "이벤트";
                case JournalKind.Data: return "데이터";
                case JournalKind.Snapshot: return "스냅샷";
                case JournalKind.Undo: return "Undo";
                case JournalKind.Achievement: return "도전과제";
                case JournalKind.Reward: return "보상";
                case JournalKind.Warning: return "경고";
                case JournalKind.Error: return "오류";
                default: return "시스템";
            }
        }

        static Color KindColor(JournalKind kind)
        {
            switch (kind)
            {
                case JournalKind.Action: return Gs.Accent;
                case JournalKind.Event: return Gs.Palette[6];
                case JournalKind.Data: return Gs.Palette[7];
                case JournalKind.Snapshot: return Gs.Palette[4];
                case JournalKind.Undo: return Gs.Palette[1];
                case JournalKind.Achievement: return Gs.Palette[2];
                case JournalKind.Reward: return Gs.Palette[3];
                case JournalKind.Warning: return Gs.Warning;
                case JournalKind.Error: return Gs.Danger;
                default: return Gs.Palette[8];
            }
        }

        // ── 데이터: 현재 세션·플레이어의 모든 값(분류별 폴더) ──────────
        // 판 데이터(세션 구역) · 프로필 데이터(메타 구역) · 수치 · 도전과제 달성. 클래스·컬렉션은 폴더로 들어가 배치대로 본다.
        // 고치면 행동 하나(DeveloperEditAction)로 Set·Modify를 거쳐 기록되고 Undo 버튼으로 되돌릴 수 있다.

        GsDataExplorer _dataExplorer;

        void BuildData()
        {
            var session = _session;
            var state = session.Data;

            ExplorerItem KeyItem(string key)
            {
                var type = state.TypeOf(key);
                var item = new ExplorerItem
                {
                    Label = state.LabelOf(key),
                    Caption = key,
                    Value = state.Get(key),
                    Type = type ?? state.Get(key)?.GetType(),
                };
                // 고치는 일은 행동 하나로(Undo 버튼으로 되돌린다). 행동 중이면(드묾) 그 자리에서.
                void AsAction(Action edit)
                {
                    if (state.InTransaction) edit();
                    else session.Do(new DeveloperEditAction { label = $"모니터에서 {state.LabelOf(key)} 고침", Edit = edit });
                }
                if (state.IsData(key)) item.Apply = change => AsAction(() => state.Modify<object>(key, _ => change()));
                else
                {
                    item.Set = value => AsAction(() => state.Set(key, value));
                    item.Apply = change => AsAction(change);
                }
                return item;
            }

            ExplorerItem Folder(string label, string caption, Func<string, bool> include, Color color) => new ExplorerItem
            {
                Label = label,
                Caption = caption,
                Color = color,
                Children = () => state.Keys.Where(include).OrderBy(k => k, StringComparer.Ordinal).Select(KeyItem).ToList(),
            };

            bool IsSystem(string key) => key.StartsWith(PlayerDataKeys.StatPrefix, StringComparison.Ordinal) || key.StartsWith(PlayerDataKeys.AchievementPrefix, StringComparison.Ordinal);

            var root = new ExplorerItem
            {
                Label = "데이터",
                Children = () => MissingFolder(state).Concat(new List<ExplorerItem>
                {
                    Folder("판 데이터", "이번 판 내용(세션 구역) · 판 저장에 들어감", k => !IsSystem(k) && state.ZoneOf(k) == DataZone.Session, Gs.Palette[6]),
                    Folder("프로필 데이터", "판이 바뀌어도 유지(메타 구역) · 프로필 저장에 들어감", k => !IsSystem(k) && state.ZoneOf(k) == DataZone.Meta, Gs.Palette[4]),
                    Folder("로그", "로그 탭에서 규칙·이벤트로 다루는 값", k => k.StartsWith(PlayerDataKeys.StatPrefix, StringComparison.Ordinal), Gs.Palette[2]),
                    new ExplorerItem
                    {
                        Label = "도전과제 달성",
                        Caption = "달성 여부(참/거짓을 바꾸면 달성·초기화)",
                        Color = Gs.Palette[3],
                        Children = () => session.Achievements.Definitions.Select(d => new ExplorerItem
                        {
                            Label = d.DisplayName,
                            Caption = d.Id,
                            Value = session.Achievements.IsUnlocked(d.Id),
                            Type = typeof(bool),
                            Set = value =>
                            {
                                if (value is bool on && on) session.Achievements.Unlock(d.Id, "모니터에서 직접 달성");
                                else session.Achievements.ResetProgress(d.Id);
                            },
                            Apply = change => change(),
                        }).ToList(),
                    },
                }),
            };
            _dataExplorer = GsDataExplorer.Browse(root);
            _dataExplorer.style.flexGrow = 1;
            _body.Add(_dataExplorer);
        }
        // 필드 규칙의 '필수' 값이 빈 곳(채워질 때까지 맨 위에 보인다).
        static IEnumerable<ExplorerItem> MissingFolder(PlayerDataStore state)
        {
            var missing = state.MissingRequired.ToList();
            if (missing.Count == 0) yield break;
            yield return new ExplorerItem
            {
                Label = $"필수 값 비어 있음 ({missing.Count})",
                Caption = "필드 규칙에서 필수로 정한 값",
                Color = Gs.Danger,
                Children = () => state.MissingRequired.Select(m => new ExplorerItem
                {
                    Label = state.LabelOf(m.key),
                    Caption = DataModel.NaturalPath(state.TypeOf(m.key), m.where),
                    Value = "비어 있음",
                    Type = typeof(string),
                }).ToList(),
            };
        }

        // ── 데이터·로그 (관찰 계약 트리) ─────────────────────────

        void BuildInspect(IInspectable inspectable)
        {
            var root = new InspectNode(inspectable.InspectName);
            inspectable.Inspect(root);
            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            _body.Add(scroll);

            var loose = root.Children.Where(c => c.Children.Count == 0).ToList();
            if (loose.Count > 0) scroll.Add(InspectPanel(inspectable.InspectName, loose));
            foreach (var group in root.Children.Where(c => c.Children.Count > 0))
                scroll.Add(InspectPanel(group.Label, group.Children));
        }

        VisualElement InspectPanel(string title, List<InspectNode> nodes)
        {
            var panel = Gs.Panel(title, $"{nodes.Count}개", out _, out var body);
            panel.style.marginBottom = 16;
            foreach (var node in nodes)
            {
                var row = Gs.Box("gs-sheet__row");
                var label = Gs.Label(node.Label, "gs-nowrap");
                label.style.width = 240;
                row.Add(label);
                var tag = Gs.Label(node.Tag ?? "", "gs-caption");
                tag.style.width = 130;
                row.Add(tag);
                row.Add(node.PlayerDataKey != null ? StateValue(node) : node.DataKey != null ? DataValue(node) : Gs.Label(node.Value ?? "", "gs-grow"));
                if (!string.IsNullOrEmpty(node.Note)) row.Add(Gs.Label(node.Note, "gs-caption", "gs-nowrap"));
                body.Add(row);
            }
            return panel;
        }

        // 클래스·컬렉션 값: 요약을 누르면 폴더형 탐색기로 열어 보고 고친다(고치면 Modify로 기록되어 Undo 가능).
        VisualElement DataValue(InspectNode node)
        {
            var box = Gs.Box("gs-inline", "gs-inline--editable", "gs-grow");
            box.Add(Gs.Label((node.Value ?? "") + "  ›", "gs-inline__value"));
            box.tooltip = "클릭해서 열기";
            var key = node.DataKey;
            box.RegisterCallback<ClickEvent>(_ =>
            {
                if (_session == null || !_session.Data.IsDefined(key)) return;
                GsDataExplorer.BrowseModal(_root, node.Label, _session.Data.Get(key),
                    change => _session.Data.Modify<object>(key, _ => change()));
            });
            return box;
        }
        // 모니터에서 플레이어 데이터 값을 직접 바꾼다(디버그 조작, 트랜잭션 밖에서 바로 적용되며 기록에 남는다).
        VisualElement StateValue(InspectNode node)
        {
            var box = Gs.Box("gs-inline", "gs-inline--editable", "gs-grow");
            var label = Gs.Label(node.Value ?? "", "gs-inline__value");
            box.Add(label);
            box.tooltip = "클릭해서 값 변경";
            box.RegisterCallback<ClickEvent>(_ =>
            {
                if (box.childCount > 1) return;
                var current = _session.Data.Get(node.PlayerDataKey);
                if (current is bool b)
                {
                    _session.Data.Set(node.PlayerDataKey, !b);
                    return;
                }
                var field = new TextField { value = current is double d ? d.ToString(CultureInfo.InvariantCulture) : current?.ToString() ?? "" };
                label.style.display = DisplayStyle.None;
                box.Add(field);
                box.AddToClassList("gs-inline--editing");
                _paused = true;

                void Finish(bool commit)
                {
                    if (field.parent == null) return;
                    if (commit)
                    {
                        object value = field.value;
                        if (current is double || current == null)
                            if (double.TryParse(field.value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) value = number;
                        try { _session.Data.Set(node.PlayerDataKey, value); }
                        catch (Exception e) { Debug.LogWarning($"[MomoiProject] 값을 바꿀 수 없습니다: {e.Message}"); }
                    }
                    field.RemoveFromHierarchy();
                    label.style.display = DisplayStyle.Flex;
                    box.RemoveFromClassList("gs-inline--editing");
                    _paused = false;
                    _lastJournalId = -1;
                }

                field.RegisterCallback<KeyDownEvent>(e =>
                {
                    if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Finish(true);
                    else if (e.keyCode == KeyCode.Escape) Finish(false);
                }, TrickleDown.TrickleDown);
                field.RegisterCallback<FocusOutEvent>(_ => field.schedule.Execute(() => Finish(true)).StartingIn(30));
                field.schedule.Execute(() =>
                {
                    field.Focus();
                    field.SelectAll();
                });
            });
            return box;
        }

        // ── 도전과제: 카드 선택 → 작업 패널에서 이벤트로 달성 / 강제 달성 ──

        void BuildAchievements()
        {
            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            _body.Add(scroll);
            var definitions = _session.Achievements.Definitions.ToList();
            if (definitions.Count == 0)
            {
                var empty = Gs.Box("gs-empty");
                empty.Add(Gs.Label("정의된 도전과제가 없습니다", "gs-heading"));
                scroll.Add(empty);
                return;
            }

            foreach (var definition in definitions)
            {
                var id = definition.Id;
                var unlocked = _session.Achievements.IsUnlocked(id);
                var card = new GsCard(CardLayout.Bar);
                card.SetImage(null, unlocked ? Gs.Success : Gs.PaletteFor(id));
                card.Title = definition.DisplayName;
                card.Subtitle = definition.When != null ? Gs.Dual(definition.When.Natural(_session), definition.When.Describe(_session)) : "(트리거 없음)";
                card.AddBadge(unlocked ? "달성" : "진행 중", unlocked ? Gs.Success : Gs.Palette[7]);

                if (!unlocked && _session.Achievements.TryGetProgress(id, out var current, out var target))
                {
                    var progress = new GsProgress();
                    progress.style.width = 140;
                    progress.SetValue(current, target);
                    card.AddToStats(progress);
                    card.AddToStats(Gs.Label($"{current:0.##} / {target:0.##}", "gs-caption"));
                }

                card.SetSelected(id == _selectedAchievement);
                card.tooltip = "클릭해서 선택 → 오른쪽 아래에서 달성 처리";
                card.RegisterCallback<ClickEvent>(_ =>
                {
                    _selectedAchievement = id;
                    BuildTab();
                });
                scroll.Add(card);
            }

            var selected = _session.Achievements.Find(_selectedAchievement);
            if (selected == null)
            {
                ShowDock(Gs.Row());
                return;
            }

            var isUnlocked = _session.Achievements.IsUnlocked(selected.Id);
            var dock = Gs.Row();
            var buttons = Gs.Row();
            if (isUnlocked)
            {
                var reset = new GsButton("초기화", () =>
                {
                    _session.Achievements.ResetProgress(selected.Id);
                    ShowFeedback($"'{selected.DisplayName}' 달성 표시를 지웠습니다 (수치는 그대로)", GsNoticeKind.Info);
                });
                reset.tooltip = "직접 수정: 달성 표시만 지웁니다(수치는 그대로).";
                buttons.Add(reset);
            }
            else
            {
                var force = new GsButton("강제 달성", () =>
                {
                    _session.Achievements.Unlock(selected.Id, "모니터에서 강제 달성");
                    ShowFeedback($"'{selected.DisplayName}' 강제 달성", GsNoticeKind.Ok);
                });
                force.tooltip = "직접 수정: 조건·이벤트 없이 달성 처리만 합니다.";
                buttons.Add(force);
                buttons.Add(Gs.HSpace(8));
                var fill = new GsButton("트리거로 달성", () =>
                {
                    if (_session.TrySimulateAchievement(selected.Id, out var error))
                        ShowFeedback($"'{selected.DisplayName}' 조건을 이벤트로 채웠습니다", GsNoticeKind.Ok);
                    else
                        ShowFeedback(error, GsNoticeKind.Warning);
                }, GsButtonKind.Primary);
                fill.tooltip = "게임과 같은 경로: 부족한 수치만큼 실제 이벤트를 쏩니다.\nUndo하면 수치는 돌아가고 달성은 유지됩니다.";
                GsMotion.AddPressSpring(fill);
                buttons.Add(fill);
            }
            dock.Add(buttons);
            ShowDock(dock);
        }

        // ── 수치: 행 선택 → 작업 패널에서 직접 수정 / 이벤트로 기록 ──

        void BuildStats()
        {
            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            _body.Add(scroll);
            var definitions = _session.Stats.Definitions.ToList();
            if (definitions.Count == 0)
            {
                var empty = Gs.Box("gs-empty");
                empty.Add(Gs.Label("정의된 수치가 없습니다", "gs-heading"));
                scroll.Add(empty);
                return;
            }

            var panel = Gs.Panel("로그", "행을 클릭하면 오른쪽 아래에서 값을 바꾸거나 이벤트로 기록합니다", out _, out var body);
            var header = Gs.Box("gs-sheet__header");
            foreach (var (title, width) in new[] { ("로그", 240f), ("범위 · 집계", 150f), ("현재 값", 150f), ("", 200f) })
            {
                var cell = Gs.Label(title);
                cell.style.width = width;
                header.Add(cell);
            }
            body.Add(header);

            foreach (var definition in definitions)
            {
                var id = definition.Id;
                var key = PlayerDataKeys.Stat(id);
                var row = Gs.Box("gs-sheet__row");
                row.EnableInClassList("gs-sheet__row--selected", id == _selectedStat);
                row.RegisterCallback<ClickEvent>(_ =>
                {
                    _selectedStat = id;
                    BuildTab();
                });

                var name = Gs.Label(definition.DisplayName ?? id, "gs-nowrap");
                name.style.width = 240;
                row.Add(name);
                var tag = Gs.Label(ScopeText(definition), "gs-caption");
                tag.style.width = 150;
                row.Add(tag);
                var value = Gs.Label(PlayerDataStore.Format(_session.Data.Get(key)));
                value.style.width = 150;
                row.Add(value);
                body.Add(row);
            }
            scroll.Add(panel);

            var selected = _session.Stats.Find(_selectedStat);
            if (selected == null)
            {
                ShowDock(Gs.Row());
                return;
            }

            var statKey = PlayerDataKeys.Stat(selected.Id);
            var dock = Gs.Row();
            var isSum = selected.Aggregate == StatAggregate.Sum;
            var now = _session.Stats.Get(selected.Id);
            if (double.IsNaN(now)) now = 0;

            // 직접 수정: 값만 덮어쓴다(이벤트·다른 수치는 반응하지 않고 기록에만 남는다).
            var direct = new DoubleField { value = now };
            direct.AddToClassList("gs-inline");
            direct.style.width = 90;
            dock.Add(direct);
            dock.Add(Gs.HSpace(8));
            var apply = new GsButton("직접 수정", () =>
            {
                _session.Data.Set(statKey, direct.value);
                ShowFeedback($"{selected.DisplayName} = {PlayerDataStore.Format(direct.value)} (직접 수정)", GsNoticeKind.Info);
            });
            apply.tooltip = "값만 덮어씁니다. 이벤트·다른 수치·반영 규칙은 움직이지 않습니다.";
            dock.Add(apply);
            dock.Add(Gs.HSpace(28));

            // 이벤트로: 반영 규칙을 거꾸로 따라가 실제 이벤트를 쏜다(게임과 같은 경로).
            if (!_statAmounts.TryGetValue(selected.Id, out var amount)) amount = isSum ? 1 : Math.Max(0, now);
            var amountField = new DoubleField { value = amount };
            amountField.AddToClassList("gs-inline");
            amountField.style.width = 90;
            dock.Add(amountField);
            dock.Add(Gs.HSpace(8));
            var fire = new GsButton(isSum ? "이벤트로 더하기" : "이벤트로 기록", () =>
            {
                if (_session.TrySimulateStat(selected.Id, amountField.value, out var description, out var error))
                    ShowFeedback($"{description} 받기", GsNoticeKind.Ok);
                else
                    ShowFeedback(error, GsNoticeKind.Warning);
            }, GsButtonKind.Primary);
            GsMotion.AddPressSpring(fire);
            dock.Add(fire);

            // 쏠 이벤트는 버튼 설명으로 보여 준다.
            void UpdatePlan()
            {
                fire.tooltip = EventSimulator.TryPlanForStat(_session.Stats, selected.Id, amountField.value, out var p, out var error)
                    ? "쏠 이벤트: " + p.Description + "\n같은 이벤트를 받는 다른 수치와 도전과제도 함께 반응하고, Undo할 수 있습니다."
                    : error;
            }
            amountField.RegisterValueChangedCallback(e =>
            {
                _statAmounts[selected.Id] = e.newValue;
                UpdatePlan();
            });
            UpdatePlan();
            ShowDock(dock);
        }

        static string ScopeText(StatDefinition definition) =>
            (definition.Scope == StatScope.Run ? "이번 판" : "누적") + " · " + StatSystem.AggregateText(definition.Aggregate);

        // ── 이벤트 호출 ───────────────────────────────────────

        void BuildEvents()
        {
            var split = Gs.Row("gs-grow");
            split.style.alignItems = Align.Stretch;
            _body.Add(split);

            var types = ProjectCode.Events().OrderBy(EventBus.DisplayNameOf).ToList();
            var list = new ScrollView();
            list.style.width = 240;
            list.style.flexShrink = 0;
            list.style.marginRight = 16;
            split.Add(list);

            if (types.Count == 0)
            {
                list.Add(Gs.Label("[GameEvent]가 붙은 이벤트 타입이 없습니다.", "gs-caption"));
                return;
            }

            var selected = types.FirstOrDefault(t => t.FullName == _eventTypeName) ?? types[0];
            foreach (var type in types)
            {
                var t = type;
                var item = new GsNavItem(EventBus.DisplayNameOf(type), Gs.PaletteFor(type.FullName), () =>
                {
                    _eventTypeName = t.FullName;
                    BuildTab();
                });
                item.SetCount(_session.Events.SubscriberCount(type) > 0 ? $"받는 곳 {_session.Events.SubscriberCount(type)}" : "");
                item.SetActive(type == selected);
                item.tooltip = type.FullName;
                list.Add(item);
            }

            var form = Gs.Panel(EventBus.DisplayNameOf(selected), selected.FullName, out _, out var body);
            form.style.flexGrow = 1;
            form.style.marginBottom = 4;
            split.Add(form);

            // 이벤트 값은 실제 이벤트 인스턴스 하나를 탐색기로 채운다(중첩 클래스·목록 필드도 같은 화면에서).
            if (!_eventInstances.TryGetValue(selected.FullName, out var instance))
            {
                try
                {
                    instance = EventSimulator.CreateDefault(selected);
                }
                catch (Exception e)
                {
                    body.Add(new GsNotice(e.Message.Replace("[MomoiProject] ", ""), GsNoticeKind.Error));
                    return;
                }
                _eventInstances[selected.FullName] = instance;
            }
            var editor = GsDataExplorer.Browse(EventBus.DisplayNameOf(selected), instance, change => change());
            editor.style.minHeight = 160;
            body.Add(editor);
            // 이 이벤트가 움직이는 수치(반영 규칙)를 보여줘 결과를 예상할 수 있게 한다.
            var affected = _session.Stats.Definitions
                .SelectMany(d => d.SimpleRules().Where(r => r.eventType == selected.FullName).Select(r => $"{d.DisplayName} ← {(string.IsNullOrEmpty(r.field) ? "횟수" : r.field)}"))
                .ToList();
            body.Add(Gs.Space(14));
            body.Add(Gs.Label(affected.Count > 0 ? "반응하는 로그:  " + string.Join("   ·   ", affected) : "이 이벤트를 반영하는 로그가 없습니다.", "gs-caption"));

            var dock = Gs.Row();
            var actions = Gs.Row();
            var repeat = new DoubleField { value = 1 };
            repeat.style.width = 70;
            repeat.AddToClassList("gs-inline");
            actions.Add(repeat);
            actions.Add(DockUnit("회"));
            var fire = new GsButton("이벤트 호출", () =>
            {
                try
                {
                    var count = Math.Max(1, Math.Min(1000, (int)Math.Round(repeat.value)));
                    var template = _eventInstances[selected.FullName];
                    // 쏠 때마다 사본을 만들어, 이후 편집이 이미 쏜 이벤트(기록)를 바꾸지 않게 한다.
                    var events = Enumerable.Range(0, count).Select(_ => CopyEvent(template)).ToList();
                    _session.SimulateEvents(events, "모니터 이벤트 호출");
                    ShowFeedback($"{EventBus.DisplayNameOf(selected)} ×{count} 쐈습니다", GsNoticeKind.Ok);
                }
                catch (Exception e)
                {
                    ShowFeedback(e.Message.Replace("[MomoiProject] ", ""), GsNoticeKind.Error);
                }
            }, GsButtonKind.Primary);
            GsMotion.AddPressSpring(fire);
            actions.Add(fire);
            dock.Add(actions);
            ShowDock(dock);
        }

        static object CopyEvent(object template)
        {
            try
            {
                return DataModel.Clone(template, null);
            }
            catch (Exception)
            {
                return template;
            }
        }
        // 필드 이름 뒤에 붙이는 친숙한 타입 이름.
        internal static string TypeName(Type type)
        {
            if (type.IsEnum) return type.Name;
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Int32: return "int";
                case TypeCode.Int64: return "long";
                case TypeCode.Int16: return "short";
                case TypeCode.Byte: return "byte";
                case TypeCode.UInt32: return "uint";
                case TypeCode.UInt64: return "ulong";
                case TypeCode.Single: return "float";
                case TypeCode.Double: return "double";
                case TypeCode.Decimal: return "decimal";
                case TypeCode.Boolean: return "bool";
                case TypeCode.String: return "string";
                default: return type.Name;
            }
        }

        // ── 작업 줄(본문 아래, 오른쪽 정렬) ─────────────────────
        // 선택한 항목을 조작하는 버튼은 항상 같은 자리에 뜬다.

        static Label DockUnit(string text)
        {
            var label = Gs.Label(text, "gs-caption");
            label.style.marginLeft = 6;
            label.style.marginRight = 12;
            return label;
        }

        void ShowDock(VisualElement content)
        {
            if (_dock == null) return;
            _dock.Clear();
            _dock.style.display = content == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (content != null) _dock.Add(content);
        }

        // 게임 시간: 일시정지·1프레임 진행(개발용)·배속. 게임 안의 모든 요소(물리·애니메이션·파티클·게임 시계)가 따라간다.
        static readonly float[] Speeds = { 0.25f, 0.5f, 1f, 2f, 4f };

        VisualElement TimeControls()
        {
            var row = Gs.Row();
            var pause = new GsButton("", () => GameTime.TogglePause("개발 도구"), GsButtonKind.Ghost);
            var step = new GsButton("1프레임", () => GameTime.StepFrame(), GsButtonKind.Ghost);
            step.tooltip = "일시정지 중에 한 프레임만 진행합니다(개발용).";
            var speed = new GsSegmented(Speeds.Select(s => s.ToString("0.##", CultureInfo.InvariantCulture) + "×").ToArray());
            speed.tooltip = "배속·슬로우. 게임 안의 모든 요소가 함께 빨라지거나 느려집니다.";
            speed.Changed += index => GameTime.SetSpeed(Speeds[index]);
            row.Add(pause);
            row.Add(step);
            row.Add(Gs.HSpace(8));
            row.Add(speed);

            void Refresh()
            {
                var paused = GameTime.IsPaused;
                pause.Text = paused ? "재개" : "일시정지";
                pause.tooltip = paused ? "멈춘 이유: " + string.Join(", ", GameTime.PausedBy) : "게임 시간을 멈춥니다.";
                step.SetEnabled(paused);
                var index = Array.FindIndex(Speeds, s => Mathf.Approximately(s, GameTime.Speed));
                if (index >= 0) speed.Select(index);
            }
            Action onChanged = Refresh;
            row.RegisterCallback<AttachToPanelEvent>(_ => GameTime.Changed += onChanged);
            row.RegisterCallback<DetachFromPanelEvent>(_ => GameTime.Changed -= onChanged);
            Refresh();
            return row;
        }
        string HistoryText()
        {
            switch (_session.Options.History)
            {
                case HistoryMode.Actions: return $"Undo할 수 있는 행동 {_session.UndoCount}";
                case HistoryMode.Snapshots: return $"되감기 스냅샷 {_session.UndoCount} ({_session.Options.SnapshotInterval:0.##}초 간격)";
                default: return "되돌리기 없음";
            }
        }

        void UndoLast()
        {
            var history = _session.Data.History;
            var label = _session.Options.History == HistoryMode.Actions && history.Count > 0 ? history[history.Count - 1].Label : null;
            if (_session.Undo())
                ShowFeedback((label != null ? $"Undo됨: {label}" : "되감기") + "  ·  행동이 바꾼 값은 돌아가고 달성한 도전과제·보상은 유지", GsNoticeKind.Info);
            else
                ShowFeedback(_session.Options.History == HistoryMode.None ? "이 세션은 되돌리기를 쓰지 않습니다(SessionOptions.History)." : "되돌릴 행동이 없습니다.", GsNoticeKind.Warning);
        }
        void ShowFeedback(string text, GsNoticeKind kind)
        {
            if (_feedback == null) return;
            _feedback.Clear();
            var notice = new GsNotice(text, kind);
            notice.AddToClassList("gs-appear");
            _feedback.Add(notice);
            notice.schedule.Execute(() => notice.AddToClassList("gs-appear--hidden")).StartingIn(4000);
            notice.schedule.Execute(() => notice.RemoveFromHierarchy()).StartingIn(4400);
        }

        // ── 행동 ───────────────────────────────────────────────

        void BuildActions()
        {
            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            _body.Add(scroll);
            var log = _session.Actions.Log;
            if (log.Count == 0)
            {
                var empty = Gs.Box("gs-empty");
                empty.Add(Gs.Label("아직 행동이 없습니다", "gs-heading"));
                empty.Add(Gs.Space(6));
                empty.Add(Gs.Label("session.Do(행동)으로 실행한 플레이어 행동이 여기 쌓입니다.", "gs-caption"));
                scroll.Add(empty);
                return;
            }

            var panel = Gs.Panel("행동 로그", $"{log.Count}개  ·  {HistoryText()}", out _, out var body);
            for (int i = log.Count - 1; i >= 0; i--)
            {
                var record = log[i];
                var row = Gs.Box("gs-sheet__row");
                var status = record.Status;
                var color = status == ActionStatus.Done ? Gs.Success : status == ActionStatus.Undone ? Gs.Palette[1] : Gs.Danger;
                row.Add(new GsBadge(ActionDispatcher.StatusText(status), color) { style = { width = 70 } });
                var text = Gs.Label(record.Action.Describe(), "gs-grow");
                var action = record.Action;
                row.RegisterCallback<ClickEvent>(_ => GsDataExplorer.BrowseModal(_root, "행동 · " + action.Describe(), action, null));
                row.tooltip = "클릭해서 행동 내용 보기";
                if (status == ActionStatus.Undone) text.AddToClassList("gs-text-3");
                row.Add(text);
                row.Add(Gs.Label(record.Action.Visibility == ActionVisibility.Public ? "공개" : record.Action.Visibility == ActionVisibility.Private ? "본인만" : "개발자", "gs-caption"));
                row.Add(Gs.HSpace(16));
                row.Add(Gs.Label($"틱 {record.Action.tick}", "gs-caption"));
                if (!string.IsNullOrEmpty(record.Error)) row.tooltip = record.Error;
                body.Add(row);
            }
            scroll.Add(panel);
        }

        // ── 명령 ───────────────────────────────────────────────

        void BuildCommands()
        {
            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            _body.Add(scroll);
            BuildChoices(scroll);
            BuildScenarios(scroll);
            foreach (var group in _session.Debug.All.GroupBy(c => c.Group))
            {
                var panel = Gs.Panel(group.Key, null, out _, out var body);
                panel.style.marginBottom = 16;
                var row = Gs.Row("gs-wrap");
                foreach (var command in group)
                {
                    var c = command;
                    var button = new GsButton(c.Name, () =>
                    {
                        try { c.Execute(); }
                        catch (Exception e) { Debug.LogException(e); }
                    });
                    button.tooltip = c.Description ?? "";
                    button.style.marginRight = 8;
                    button.style.marginBottom = 8;
                    GsMotion.AddPressSpring(button);
                    row.Add(button);
                }
                body.Add(row);
                scroll.Add(panel);
            }
        }

        // 테스트 상황을 저장·불러올 세션: 권한이 있는 쪽만. 이 프로그램이 서버(방장·시험 모드)면 서버 세션,
        // 아니면 클라이언트가 아닐 때의 이 화면 세션. 멀티 클라이언트·리플레이면 null(서버 값을 클라이언트가 덮지 않게).
        GameSession ScenarioSession()
        {
            if (GameNet.IsReplaying) return null;
            if (GameNet.Server != null) return GameNet.Server.Session;
            if (GameNet.Client != null) return null;
            return GameNet.IsOnline ? GBibleRuntime.Session : _session;
        }

        // 테스트 상황: 지금 판·프로필·난수·시각을 이름 붙여 저장하고 불러온다(Assets/MomoiProject/Data/Tests, TestScenarios).
        void BuildScenarios(VisualElement scroll)
        {
            var panel = Gs.Panel("테스트 상황", "지금 상황을 저장해 두고 다시 연다(테스트 씬의 시작 상황으로도 고를 수 있다)", out _, out var body);
            panel.style.marginBottom = 16;
            var target = ScenarioSession();
            if (target == null)
            {
                var note = Gs.Label("멀티 클라이언트(또는 리플레이)에서는 테스트 상황을 저장·불러올 수 없습니다. 서버(방장)나 혼자 할 때 씁니다.", "gs-caption");
                note.style.whiteSpace = WhiteSpace.Normal;
                note.style.marginBottom = 8;
                body.Add(note);
            }
            else if (target != _session)
            {
                var note = Gs.Label("서버 세션에 저장하고 불러옵니다(위에서 고른 세션과 다름).", "gs-caption");
                note.style.marginBottom = 8;
                body.Add(note);
            }
            var saveRow = Gs.Row();
            var nameField = new TextField { value = "" };
            nameField.style.flexGrow = 1;
            nameField.style.marginRight = 8;
            saveRow.Add(nameField);
            var save = new GsButton("지금 저장", () =>
            {
                try
                {
                    TestScenarios.Save(target, nameField.value);
                    ShowFeedback($"테스트 상황 '{nameField.value}'를 저장했습니다.", GsNoticeKind.Ok);
                    BuildTab();
                }
                catch (Exception e) { ShowFeedback(e.Message, GsNoticeKind.Warning); }
            }, GsButtonKind.Primary);
            save.SetEnabled(target != null);
            nameField.SetEnabled(target != null);
            saveRow.Add(save);
            body.Add(saveRow);
            var row = Gs.Row("gs-wrap");
            foreach (var name in TestScenarios.Names())
            {
                var n = name;
                var button = new GsButton(n, () =>
                {
                    try
                    {
                        if (TestScenarios.Load(target, n)) ShowFeedback($"테스트 상황 '{n}'를 불러왔습니다.", GsNoticeKind.Ok);
                        else ShowFeedback($"테스트 상황 '{n}'를 불러오지 못했습니다(저널).", GsNoticeKind.Warning);
                    }
                    catch (Exception e) { ShowFeedback(e.Message, GsNoticeKind.Warning); }
                });
                button.tooltip = "불러오기(지금 판이 이 상황으로 바뀐다, Undo 이력은 비워진다)";
                button.style.marginRight = 8;
                button.style.marginBottom = 8;
                button.SetEnabled(target != null);
                row.Add(button);
            }
            if (row.childCount > 0)
            {
                row.style.marginTop = 8;
                body.Add(row);
            }
            scroll.Add(panel);
        }

        string ChoiceKey() => string.Join(",", _session.Choices.All.Select(c => c.id));

        // 선택 대기(Game.Ask): 게임 화면이 아직 없어도 여기서 고르고 답할 수 있다. 답하기도 게임 행동이라 타임라인에 남고 Undo된다.
        void BuildChoices(VisualElement scroll)
        {
            _lastChoices = ChoiceKey();
            foreach (var choice in _session.Choices.All)
            {
                var pending = choice;
                var count = pending.min == pending.max ? $"{pending.min}개 고르기" : $"{pending.min}~{pending.max}개 고르기";
                var panel = Gs.Panel("선택 대기 · " + (string.IsNullOrEmpty(pending.prompt) ? pending.id : pending.prompt), count, out _, out var body);
                panel.style.marginBottom = 16;
                var picked = new List<string>();
                var row = Gs.Row("gs-wrap");
                foreach (var candidate in pending.options)
                {
                    var id = candidate.id;
                    var label = string.IsNullOrEmpty(candidate.label) ? id : candidate.label;
                    GsButton button = null;
                    button = new GsButton(label, () =>
                    {
                        if (!picked.Remove(id))
                        {
                            // 하나만 고르는 선택은 누르면 바꿔 고른다.
                            if (pending.max == 1) picked.Clear();
                            picked.Add(id);
                        }
                        foreach (var child in row.Children().OfType<GsButton>())
                            child.Text = picked.Contains((string)child.userData) ? "✓ " + child.tooltip : child.tooltip;
                    });
                    button.userData = id;
                    button.tooltip = label;
                    button.style.marginRight = 8;
                    button.style.marginBottom = 8;
                    row.Add(button);
                }
                body.Add(row);
                var answer = new GsButton("답하기", () =>
                {
                    try { _session.Game.Answer(pending.id, picked.Cast<object>().ToList()); }
                    catch (Exception e) { ShowFeedback(e.Message, GsNoticeKind.Warning); }
                }, GsButtonKind.Primary);
                answer.style.alignSelf = Align.FlexStart;
                body.Add(answer);
                scroll.Add(panel);
            }
        }
    }
}
