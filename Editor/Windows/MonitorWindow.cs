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
    // 런타임 모니터: 통합 기록(인과 추적), 상태, 도전과제, 통계, 행동(확정·무르기), 디버그 명령.
    public sealed class MonitorWindow : EditorWindow
    {
        enum Tab
        {
            Timeline,
            State,
            Achievements,
            Stats,
            Actions,
            Commands,
        }

        static readonly string[] TabNames = { "타임라인", "상태", "도전과제", "통계", "행동", "명령" };

        [SerializeField] Tab _tab;
        [SerializeField] string _search = "";

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
            _session = GameSystemsRuntime.Session;
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
                var commit = new GsButton("확정", () => _session.Commit());
                var undo = new GsButton("무르기", () => _session.Undo());
                header.Add(undo);
                header.Add(Gs.HSpace(8));
                header.Add(commit);
            }
            main.Add(header);

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
            if (_session == null || _session != GameSystemsRuntime.Session)
            {
                if (GameSystemsRuntime.Session != _session) Build(rootVisualElement);
                return;
            }
            UpdateInfo();
            if (_paused) return;
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
                case Tab.Stats: BuildInspect(_session.Stats); break;
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

        // ── 상태·통계 (관찰 계약 트리) ─────────────────────────

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
                var action = unlocked
                    ? new GsButton("초기화", () => _session.Achievements.ResetProgress(id), GsButtonKind.Ghost)
                    : new GsButton("강제 달성", () => _session.Achievements.Unlock(id, "모니터에서 강제 달성"), GsButtonKind.Ghost);
                card.AddToStats(action);
                scroll.Add(card);
            }
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
