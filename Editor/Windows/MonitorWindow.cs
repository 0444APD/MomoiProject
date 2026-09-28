using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GameSystems.EditorTools.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameSystems.EditorTools
{
    // 런타임 모니터: 통합 기록(인과 추적), 상태, 도전과제, 수치, 행동(확정·무르기), 디버그 명령.
    public sealed class MonitorWindow : EditorWindow
    {
        enum Tab
        {
            Timeline,
            State,
            Achievements,
            Stats,
            Events,
            Actions,
            Commands,
        }

        static readonly string[] TabNames = { "타임라인", "상태", "도전과제", "수치", "이벤트", "행동", "명령" };

        [SerializeField] Tab _tab;
        [SerializeField] string _search = "";
        // 테스트 이벤트 직후 확정까지 할지(수동 확정 세션에서만 의미가 있다).
        [SerializeField] bool _commitAfterTest = true;
        [SerializeField] string _eventTypeName;

        readonly Dictionary<string, double> _statAmounts = new Dictionary<string, double>();
        readonly Dictionary<string, Dictionary<string, object>> _eventDrafts = new Dictionary<string, Dictionary<string, object>>();
        VisualElement _feedback;

        // 테스트에서 런타임 없이 세션을 주입하기 위한 통로.
        internal static Func<GameSession> SessionSource = () => GameSystemsRuntime.Session;
        internal static int TabCount => TabNames.Length;

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
        int _lastUncommitted = -1;
        bool _paused;
        long _selectedEntry;
        readonly HashSet<long> _chain = new HashSet<long>();
        readonly HashSet<JournalKind> _hiddenKinds = new HashSet<JournalKind>();
        List<JournalEntry> _entries = new List<JournalEntry>();
        ListView _timeline;
        VisualElement _chainPanel;

        [MenuItem("Game Systems/모니터", priority = 1)]
        public static void Open()
        {
            var window = GetWindow<MonitorWindow>();
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
            if (_session != null)
            {
                if (!_session.Options.AutoCommit)
                {
                    var toggle = new GsButton(CommitToggleText(), null, GsButtonKind.Ghost);
                    toggle.tooltip = "켜면 테스트 이벤트를 발생시킨 뒤 곧바로 확정합니다(도전과제·누적 수치가 즉시 반영).\n끄면 확정 대기로 남아 무르기를 시험할 수 있습니다.";
                    toggle.AddManipulator(new Clickable(() =>
                    {
                        _commitAfterTest = !_commitAfterTest;
                        toggle.Text = CommitToggleText();
                    }));
                    header.Add(toggle);
                    header.Add(Gs.HSpace(12));
                }
                var commit = new GsButton("확정", () => _session.Commit());
                var undo = new GsButton("무르기", () => _session.Undo());
                header.Add(undo);
                header.Add(Gs.HSpace(8));
                header.Add(commit);
            }
            main.Add(header);

            _feedback = Gs.Col();
            main.Add(_feedback);

            _body = Gs.Col("gs-grow");
            main.Add(_body);

            BuildSidebar();

            if (_session == null)
            {
                var empty = Gs.Box("gs-empty");
                empty.Add(Gs.Label(EditorApplication.isPlaying ? "세션이 없습니다" : "플레이 모드에서 실시간 상태가 표시됩니다", "gs-title"));
                empty.Add(Gs.Space(8));
                empty.Add(Gs.Label(EditorApplication.isPlaying
                    ? "GameSystemsRuntime이 시작되지 않았습니다."
                    : "플레이를 시작하면 모든 시스템의 상태, 기록, 행동을 여기서 확인하고 조작할 수 있습니다.", "gs-caption"));
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
            if (focused != null && _body.Contains(focused)) return;
            var last = _session.Journal.LastId;
            if (last == _lastJournalId && _session.State.UncommittedCount == _lastUncommitted) return;
            _lastJournalId = last;
            _lastUncommitted = _session.State.UncommittedCount;
            if (_tab == Tab.Timeline) RefreshTimeline();
            else if (_tab != Tab.Commands) BuildTab();
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
                                $"확정 대기 {_session.State.UncommittedCount}  ·  기록 {_session.Journal.Count}/{_session.Journal.Capacity}" +
                                (_session.Options.AutoCommit ? "  ·  자동 확정" : "  ·  수동 확정");
        }

        void BuildTab()
        {
            _body.Clear();
            switch (_tab)
            {
                case Tab.Timeline: BuildTimeline(); break;
                case Tab.State: BuildInspect(_session.State); break;
                case Tab.Achievements: BuildAchievements(); break;
                case Tab.Stats: BuildStats(); break;
                case Tab.Events: BuildEvents(); break;
                case Tab.Actions: BuildActions(); break;
                case Tab.Commands: BuildCommands(); break;
            }
        }

        // ── 타임라인 ───────────────────────────────────────────

        void BuildTimeline()
        {
            var tools = Gs.Row();
            tools.style.marginBottom = 12;
            var search = new GsSearchField("기록 검색");
            search.style.width = 240;
            search.Value = _search;
            search.Changed += v =>
            {
                _search = v;
                RefreshTimeline();
            };
            tools.Add(search);
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
            var pause = new GsButton(_paused ? "재개" : "일시정지", null, GsButtonKind.Ghost);
            pause.AddManipulator(new Clickable(() =>
            {
                _paused = !_paused;
                pause.Text = _paused ? "재개" : "일시정지";
            }));
            tools.Add(pause);
            _body.Add(tools);

            var split = Gs.Row("gs-grow");
            split.style.alignItems = Align.Stretch;
            _body.Add(split);

            _timeline = new ListView
            {
                fixedItemHeight = 34,
                selectionType = SelectionType.Single,
                makeItem = MakeLogRow,
                bindItem = BindLogRow,
                itemsSource = _entries,
            };
            _timeline.style.flexGrow = 1;
            _timeline.selectionChanged += selection =>
            {
                var entry = selection.OfType<JournalEntry>().FirstOrDefault();
                _selectedEntry = entry?.Id ?? 0;
                UpdateChain();
            };
            split.Add(_timeline);

            _chainPanel = Gs.Box("gs-panel");
            _chainPanel.style.width = 340;
            _chainPanel.style.marginLeft = 16;
            _chainPanel.style.marginBottom = 16;
            split.Add(_chainPanel);

            RefreshTimeline();
        }

        VisualElement MakeLogRow()
        {
            var row = Gs.Box("gs-log-row");
            var badge = new GsBadge("", Color.white);
            badge.style.width = 64;
            row.Add(badge);
            row.Add(Gs.Label("", "gs-grow", "gs-nowrap"));
            row.Add(Gs.Label("", "gs-caption"));
            return row;
        }

        void BindLogRow(VisualElement row, int index)
        {
            var entry = _entries[index];
            var badge = (GsBadge)row[0];
            badge.Text = KindName(entry.Kind);
            badge.SetColor(KindColor(entry.Kind));
            ((Label)row[1]).text = entry.Message;
            ((Label)row[2]).text = $"#{entry.Id} · 틱 {entry.Tick}";
            row.EnableInClassList("gs-log-row--chain", _selectedEntry != 0 && _chain.Contains(entry.Id));
            row.EnableInClassList("gs-log-row--dim", _selectedEntry != 0 && !_chain.Contains(entry.Id));
        }

        void RefreshTimeline()
        {
            if (_timeline == null || _session == null) return;
            _entries.Clear();
            _entries.AddRange(_session.Journal.Entries.Reverse().Where(e =>
                !_hiddenKinds.Contains(e.Kind) &&
                (string.IsNullOrEmpty(_search) || (e.Message ?? "").IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                 (e.Key ?? "").IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0)));
            _timeline.RefreshItems();
            UpdateChain();
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
            var label = Gs.Label(entry.Message, "gs-grow");
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
                case JournalKind.State: return "상태";
                case JournalKind.Commit: return "확정";
                case JournalKind.Undo: return "무르기";
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
                case JournalKind.State: return Gs.Palette[7];
                case JournalKind.Commit: return Gs.Palette[4];
                case JournalKind.Undo: return Gs.Palette[1];
                case JournalKind.Achievement: return Gs.Palette[2];
                case JournalKind.Reward: return Gs.Palette[3];
                case JournalKind.Warning: return Gs.Warning;
                case JournalKind.Error: return Gs.Danger;
                default: return Gs.Palette[8];
            }
        }

        // ── 상태·수치 (관찰 계약 트리) ─────────────────────────

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
                row.Add(node.StateKey != null ? StateValue(node) : Gs.Label(node.Value ?? "", "gs-grow"));
                if (!string.IsNullOrEmpty(node.Note)) row.Add(Gs.Label(node.Note, "gs-caption", "gs-nowrap"));
                body.Add(row);
            }
            return panel;
        }

        // 모니터에서 상태 값을 직접 바꾼다(디버그 조작, 트랜잭션 밖에서 바로 적용되며 기록에 남는다).
        VisualElement StateValue(InspectNode node)
        {
            var box = Gs.Box("gs-inline", "gs-inline--editable", "gs-grow");
            var label = Gs.Label(node.Value ?? "", "gs-inline__value");
            box.Add(label);
            box.tooltip = "클릭해서 값 변경";
            box.RegisterCallback<ClickEvent>(_ =>
            {
                if (box.childCount > 1) return;
                var current = _session.State.Get(node.StateKey);
                if (current is bool b)
                {
                    _session.State.Set(node.StateKey, !b);
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
                        try { _session.State.Set(node.StateKey, value); }
                        catch (Exception e) { Debug.LogWarning($"[GameSystems] 값을 바꿀 수 없습니다: {e.Message}"); }
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

        // ── 도전과제 ───────────────────────────────────────────

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
                var unlocked = _session.Achievements.IsUnlocked(definition.Id);
                var card = new GsCard(CardLayout.Bar);
                card.SetImage(null, unlocked ? Gs.Success : Gs.PaletteFor(definition.Id));
                card.Title = definition.DisplayName;
                card.Subtitle = definition.Condition?.Describe(_session) ?? "(조건 없음)";
                card.AddBadge(unlocked ? "달성" : "진행 중", unlocked ? Gs.Success : Gs.Palette[7]);

                if (!unlocked && _session.Achievements.TryGetProgress(definition.Id, out var current, out var target))
                {
                    var progress = new GsProgress();
                    progress.style.width = 140;
                    progress.SetValue(current, target);
                    card.AddToStats(progress);
                    card.AddToStats(Gs.Label($"{current:0.##} / {target:0.##}", "gs-caption") );
                }

                var id = definition.Id;
                if (unlocked)
                {
                    var reset = new GsButton("초기화", () => _session.Achievements.ResetProgress(id), GsButtonKind.Ghost);
                    reset.tooltip = "직접 수정: 달성 표시만 지웁니다(수치는 그대로).";
                    card.AddToStats(reset);
                }
                else
                {
                    var fill = new GsButton("이벤트로 채우기", () =>
                    {
                        if (_session.TrySimulateAchievement(id, _commitAfterTest, out var error))
                            ShowFeedback($"'{definition.DisplayName}' 조건을 이벤트로 채웠습니다" + (PendingNote()), GsNoticeKind.Ok);
                        else
                            ShowFeedback(error, GsNoticeKind.Warning);
                    });
                    fill.tooltip = "게임과 같은 경로: 부족한 수치만큼 실제 이벤트를 발생시킵니다(무르기 가능, 다른 수치·도전과제도 함께 반응).";
                    card.AddToStats(fill);
                    card.AddToStats(Gs.HSpace(6));
                    var force = new GsButton("강제 달성", () => _session.Achievements.Unlock(id, "모니터에서 강제 달성"), GsButtonKind.Ghost);
                    force.tooltip = "직접 수정: 조건·이벤트 없이 달성 처리만 합니다(보상은 지급).";
                    card.AddToStats(force);
                }
                scroll.Add(card);
            }
        }

        // ── 수치: 직접 수정 / 이벤트로 반영 ─────────────────────

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

            var panel = Gs.Panel("수치", "값 칸 클릭: 직접 수정(값만 덮어씀, 규칙·도전과제 연쇄 없음)  ·  이벤트로: 게임과 같은 경로로 반영", out _, out var body);
            var header = Gs.Box("gs-sheet__header");
            foreach (var (title, width) in new[] { ("수치", 220f), ("범위 · 집계", 130f), ("현재 값 (직접 수정)", 150f), ("이벤트로 반영", 300f) })
            {
                var cell = Gs.Label(title);
                cell.style.width = width;
                header.Add(cell);
            }
            body.Add(header);

            foreach (var definition in definitions)
            {
                var id = definition.Id;
                var key = StateKeys.Stat(id);
                var row = Gs.Box("gs-sheet__row");

                var name = Gs.Label(definition.DisplayName ?? id, "gs-nowrap");
                name.style.width = 220;
                row.Add(name);
                var tag = Gs.Label((definition.Scope == StatScope.Run ? "이번 판" : "누적") + " · " + StatSystem.AggregateText(definition.Aggregate), "gs-caption");
                tag.style.width = 130;
                row.Add(tag);

                var value = StateValue(new InspectNode(definition.DisplayName, StateStore.Format(_session.State.Get(key)), key));
                value.style.width = 150;
                value.style.flexGrow = 0;
                row.Add(value);

                var isSum = definition.Aggregate == StatAggregate.Sum;
                if (!_statAmounts.TryGetValue(id, out var amount)) amount = isSum ? 1 : Math.Max(0, _session.Stats.Get(id) is var v && !double.IsNaN(v) ? v : 0);
                var amountField = new DoubleField { value = amount };
                amountField.style.width = 70;
                amountField.AddToClassList("gs-inline");
                var plan = Gs.Label("", "gs-caption", "gs-nowrap");
                plan.style.flexShrink = 1;

                void UpdatePlan()
                {
                    if (EventSimulator.TryPlanForStat(_session.Stats, id, amountField.value, out var p, out var error))
                    {
                        plan.text = "→ " + p.Description;
                        plan.tooltip = "";
                        plan.style.color = StyleKeyword.Null;
                    }
                    else
                    {
                        plan.text = "이벤트로 만들 수 없음";
                        plan.tooltip = error;
                        plan.style.color = Gs.Warning;
                    }
                }

                amountField.RegisterValueChangedCallback(e =>
                {
                    _statAmounts[id] = e.newValue;
                    UpdatePlan();
                });

                var fire = new GsButton(isSum ? "이벤트로 +" : "이벤트로 기록", () =>
                {
                    if (_session.TrySimulateStat(id, amountField.value, _commitAfterTest, out var description, out var error))
                        ShowFeedback($"{description} 발생" + PendingNote(), GsNoticeKind.Ok);
                    else
                        ShowFeedback(error, GsNoticeKind.Warning);
                }, GsButtonKind.Ghost);
                fire.tooltip = isSum
                    ? "이 수치의 반영 규칙을 거꾸로 따라가 실제 이벤트를 발생시킵니다. 같은 이벤트를 받는 다른 수치와 도전과제도 함께 반응합니다."
                    : "입력한 값을 이벤트로 한 번 기록합니다(최고치·최저치 규칙은 게임과 똑같이 적용).";

                var eventCell = Gs.Row();
                eventCell.style.width = 300;
                eventCell.Add(fire);
                eventCell.Add(amountField);
                eventCell.Add(Gs.HSpace(8));
                eventCell.Add(plan);
                row.Add(eventCell);
                UpdatePlan();
                body.Add(row);
            }
            scroll.Add(panel);
        }

        // ── 이벤트 발생기 ───────────────────────────────────────

        void BuildEvents()
        {
            var split = Gs.Row("gs-grow");
            split.style.alignItems = Align.Stretch;
            _body.Add(split);

            var types = TypeResolver.GameEventTypes().OrderBy(EventBus.DisplayNameOf).ToList();
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
                item.SetCount(_session.Events.SubscriberCount(type) > 0 ? $"구독 {_session.Events.SubscriberCount(type)}" : "");
                item.SetActive(type == selected);
                item.tooltip = type.FullName;
                list.Add(item);
            }

            var form = Gs.Panel(EventBus.DisplayNameOf(selected), selected.FullName, out _, out var body);
            form.style.flexGrow = 1;
            form.style.marginBottom = 16;
            split.Add(form);

            if (!_eventDrafts.TryGetValue(selected.FullName, out var draft))
            {
                draft = new Dictionary<string, object>();
                try
                {
                    var sample = EventSimulator.CreateDefault(selected);
                    foreach (var member in EventSimulator.EditableMembers(selected)) draft[member.Name] = EventSimulator.GetValue(member, sample);
                }
                catch (Exception e)
                {
                    body.Add(new GsNotice(e.Message.Replace("[GameSystems] ", ""), GsNoticeKind.Error));
                    return;
                }
                _eventDrafts[selected.FullName] = draft;
            }

            var members = EventSimulator.EditableMembers(selected).ToList();
            if (members.Count == 0) body.Add(Gs.Label("채울 값이 없는 이벤트입니다.", "gs-caption"));
            foreach (var member in members)
            {
                var row = Gs.Box("gs-field-row");
                var label = Gs.Label(member.Name, "gs-field-row__label");
                label.style.width = 140;
                row.Add(label);
                row.Add(EventFieldEditor(member, draft));
                row.Add(Gs.Label(EventSimulator.MemberType(member).Name, "gs-caption"));
                body.Add(row);
            }

            // 이 이벤트가 움직이는 수치(반영 규칙)를 보여줘 결과를 예상할 수 있게 한다.
            var affected = _session.Stats.Definitions
                .SelectMany(d => d.Rules.Where(r => r.eventType == selected.FullName).Select(r => $"{d.DisplayName} ← {(string.IsNullOrEmpty(r.field) ? "횟수" : r.field)}"))
                .ToList();
            body.Add(Gs.Space(14));
            body.Add(Gs.Label(affected.Count > 0 ? "반응하는 수치:  " + string.Join("   ·   ", affected) : "이 이벤트를 반영하는 수치가 없습니다.", "gs-caption"));
            body.Add(Gs.Space(16));

            var actions = Gs.Row();
            var repeat = new DoubleField { value = 1 };
            repeat.style.width = 60;
            repeat.AddToClassList("gs-inline");
            var fire = new GsButton("이벤트 발생", () =>
            {
                try
                {
                    var count = Math.Max(1, Math.Min(1000, (int)Math.Round(repeat.value)));
                    var events = Enumerable.Range(0, count).Select(_ => EventSimulator.Create(selected, draft)).ToList();
                    _session.SimulateEvents(events, "모니터 이벤트 발생기", _commitAfterTest);
                    ShowFeedback($"{EventBus.DisplayNameOf(selected)} ×{count} 발생" + PendingNote(), GsNoticeKind.Ok);
                }
                catch (Exception e)
                {
                    ShowFeedback(e.Message.Replace("[GameSystems] ", ""), GsNoticeKind.Error);
                }
            }, GsButtonKind.Primary);
            GsMotion.AddPressSpring(fire);
            actions.Add(fire);
            actions.Add(Gs.HSpace(10));
            actions.Add(repeat);
            actions.Add(Gs.Label("회", "gs-caption"));
            body.Add(actions);
            body.Add(Gs.Space(8));
            body.Add(Gs.Label("행동으로 기록되어 무르기·확정·인과 추적이 게임과 똑같이 적용됩니다.", "gs-caption"));
        }

        static VisualElement EventFieldEditor(System.Reflection.MemberInfo member, Dictionary<string, object> draft)
        {
            var type = EventSimulator.MemberType(member);
            draft.TryGetValue(member.Name, out var current);
            VisualElement editor;
            if (type == typeof(string))
            {
                var field = new TextField { value = current as string ?? "" };
                field.RegisterValueChangedCallback(e => draft[member.Name] = e.newValue);
                editor = field;
            }
            else if (type == typeof(bool))
            {
                var field = new Toggle { value = current is bool b && b };
                field.RegisterValueChangedCallback(e => draft[member.Name] = e.newValue);
                editor = field;
            }
            else if (type.IsEnum)
            {
                var names = Enum.GetNames(type).ToList();
                var field = new DropdownField(names, Math.Max(0, names.IndexOf(current?.ToString())));
                field.RegisterValueChangedCallback(e => draft[member.Name] = e.newValue);
                editor = field;
            }
            else
            {
                var field = new DoubleField { value = current == null ? 0 : Convert.ToDouble(current, CultureInfo.InvariantCulture) };
                field.RegisterValueChangedCallback(e => draft[member.Name] = e.newValue);
                editor = field;
            }
            editor.AddToClassList("gs-inline");
            editor.style.width = 200;
            return editor;
        }

        string CommitToggleText() => _commitAfterTest ? "테스트 후 즉시 확정: 켜짐" : "테스트 후 즉시 확정: 꺼짐";

        string PendingNote() =>
            _session != null && _session.State.UncommittedCount > 0 ? $" (확정 대기 {_session.State.UncommittedCount} — 확정해야 누적 수치·도전과제에 반영)" : "";

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

            var panel = Gs.Panel("행동 로그", $"{log.Count}개  ·  확정 대기 {_session.State.UncommittedCount}개", out _, out var body);
            for (int i = log.Count - 1; i >= 0; i--)
            {
                var record = log[i];
                var row = Gs.Box("gs-sheet__row");
                var status = record.Status;
                var color = status == ActionStatus.Committed ? Gs.Success : status == ActionStatus.Pending ? Gs.Palette[2] : status == ActionStatus.Undone ? Gs.Palette[1] : Gs.Danger;
                row.Add(new GsBadge(ActionDispatcher.StatusText(status), color) { style = { width = 70 } });
                var text = Gs.Label(record.Action.Describe(), "gs-grow");
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
    }
}
