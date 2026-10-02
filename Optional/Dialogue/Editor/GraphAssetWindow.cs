using System;
using System.Collections.Generic;
using System.Linq;
using GBible.Data;
using GBible.Dialogue;
using GBible.EditorTools.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GBible.EditorTools.Graph
{
    // 그래프 에셋(다이얼로그·연출 프리셋)을 왼쪽 목록에서 고르고 오른쪽 그래프에서 편집하는 창의 공통 부분.
    // 편집은 에셋에 바로 저장한다(도감의 모아 두기 적용과 별개, Undo 가능).
    public abstract class GraphAssetWindow : EditorWindow
    {
        [SerializeField] string _selected;

        VisualElement _root;
        VisualElement _list;
        VisualElement _main;
        GsGraphCanvas _canvas;
        Label _title;
        Label _issues;

        protected abstract Type AssetType { get; }
        protected abstract GraphKinds Kind { get; }
        protected abstract string Noun { get; }
        protected abstract NodeGraph GraphOf(GameData data);
        protected abstract void Seed(NodeGraph graph);

        void OnEnable()
        {
            EditorDatabase.Changed += QueueList;
            PresetOutputs.Install();
        }

        void OnDisable() => EditorDatabase.Changed -= QueueList;

        void OnFocus() => _canvas?.Rebuild();

        void CreateGUI() => Build();

        IEnumerable<GameData> Assets => EditorDatabase.Instance.Committed.Where(d => d != null && d.GetType() == AssetType).OrderBy(d => d.Id, StringComparer.Ordinal);

        GameData Current => Assets.FirstOrDefault(d => d.Id == _selected);

        public void ShowAsset(string id)
        {
            _selected = id;
            Build();
        }

        void Build()
        {
            var host = rootVisualElement;
            host.Clear();
            _root = Gs.Row();
            Gs.Apply(_root);
            var sheet = Gs.LoadSheet("GsGraph");
            if (sheet != null) _root.styleSheets.Add(sheet);
            _root.style.flexGrow = 1;
            _root.style.alignItems = Align.Stretch;
            host.Add(_root);

            // 왼쪽 목록: 도감의 모든 탭과 같은 모양(CodexTree). 폴더는 정리용이고, 끌어서 다른 폴더로 옮긴다.
            var side = Gs.Col("gs-graph-window__side");
            _list = Gs.Col();
            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            scroll.Add(_list);
            side.Add(scroll);
            _root.Add(side);

            _main = Gs.Col();
            _main.style.flexGrow = 1;
            _root.Add(_main);

            RefreshList();
            OpenCanvas();
        }

        bool _listQueued;

        void QueueList()
        {
            if (_listQueued || _root == null) return;
            _listQueued = true;
            _root.schedule.Execute(() =>
            {
                _listQueued = false;
                RefreshList();
                if (Current == null && _canvas != null) OpenCanvas();
            });
        }

        // 이 창의 편집은 에셋에 바로 저장하므로, 폴더 옮기기·만들기도 바로 적용한다(도감의 Ctrl+S를 기다리지 않는다).
        void RefreshList()
        {
            if (_list == null) return;
            _list.Clear();
            var items = Assets.ToList();
            var tab = AssetType.FullName;
            var tree = new CodexTree { Key = "graph:" + tab, ItemNoun = Noun };
            tree.Folders.Add(new CodexTree.Folder
            {
                Path = "", Parent = null, Name = Noun, Draggable = false,
                Hint = $"아직 {Noun}가 없습니다. '+ 새로'로 만드세요.",
                Tools = tools =>
                {
                    tools.Add(new GsButton("+ 새로", () => NewAsset(""), GsButtonKind.Ghost));
                    tools.Add(new GsButton("+ 폴더", () => NewFolder(""), GsButtonKind.Ghost));
                },
            });
            foreach (var folder in CodexFolders.All(tab, items))
            {
                var path = folder;
                tree.Folders.Add(new CodexTree.Folder
                {
                    Path = path, Parent = CodexFolders.Parent(path), Name = CodexFolders.Name(path),
                    Tools = tools =>
                    {
                        tools.Add(new GsButton("+ 새로", () => NewAsset(path), GsButtonKind.Ghost));
                        tools.Add(new GsButton("+ 폴더", () => NewFolder(path), GsButtonKind.Ghost));
                    },
                });
            }
            foreach (var data in items)
            {
                var d = data;
                tree.Items.Add(new CodexTree.Item
                {
                    Key = d.Id, Folder = d.Folder, Name = d.Id, Payload = d,
                    Build = () =>
                    {
                        var row = GsGroupSection.Row(d.Id);
                        row.EnableInClassList("gs-group__row--active", d.Id == _selected);
                        return row;
                    },
                    Open = () =>
                    {
                        _selected = d.Id;
                        RefreshList();
                        OpenCanvas();
                    },
                });
            }
            tree.MoveItem = (item, folder) =>
            {
                SetFolder((GameData)item.Payload, folder.Path);
                RefreshList();
            };
            tree.MoveFolder = (folder, target) =>
            {
                var to = CodexFolders.Join(target.Path, CodexFolders.Name(folder.Path));
                if (CodexFolders.All(tab, items).Contains(to)) return;
                CodexFolders.Move(tab, folder.Path, to);
                CodexFolders.CommitTab(tab);
                foreach (var data in items.Where(i => CodexFolders.IsUnder(i.Folder, folder.Path)))
                    SetFolder(data, CodexFolders.Rebase(data.Folder, folder.Path, to));
                RefreshList();
            };
            tree.Build(_list, _root);
        }

        static void SetFolder(GameData data, string folder)
        {
            Undo.RecordObject(data, "폴더 옮기기");
            data.EditorAssignFolder(folder);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);
            EditorDatabase.MarkDirty();
        }

        void NewFolder(string parent)
        {
            var tab = AssetType.FullName;
            GsDialog.Form(_root, parent == "" ? "새 폴더" : $"'{parent}' 안에 폴더", "정리용 폴더입니다(게임 동작과 상관없음).",
                new List<DialogField> { new DialogField { Key = "name", Label = "폴더 이름", Value = "" } }, "만들기",
                values => CodexFolders.CanCreate(tab, Assets, parent, values["name"], out var error) ? null : error,
                values =>
                {
                    CodexFolders.Create(tab, CodexFolders.Join(parent, values["name"].Trim()));
                    CodexFolders.CommitTab(tab);
                    RefreshList();
                });
        }

        void OpenCanvas()
        {
            _main.Clear();
            _canvas = null;
            var data = Current;
            if (data == null)
            {
                var empty = Gs.Col();
                empty.style.flexGrow = 1;
                empty.style.alignItems = Align.Center;
                empty.style.justifyContent = Justify.Center;
                empty.Add(Gs.Label($"왼쪽에서 {Noun}를 고르거나 새로 만드세요.", "gs-text-2"));
                _main.Add(empty);
                return;
            }

            var bar = Gs.Row();
            bar.style.paddingLeft = 16;
            bar.style.paddingRight = 16;
            bar.style.paddingTop = 10;
            bar.style.paddingBottom = 10;
            _title = Gs.Label(data.Id, "gs-title");
            bar.Add(_title);
            _issues = Gs.Label("", "gs-caption");
            _issues.style.marginLeft = 14;
            _issues.style.color = Gs.Warning;
            bar.Add(_issues);
            bar.Add(Gs.Spacer());
            bar.Add(new GsButton("프로젝트에서 보기", () => EditorGUIUtility.PingObject(data), GsButtonKind.Ghost));
            _main.Add(bar);

            _main.Add(PreviewBar());

            _canvas = new GsGraphCanvas(data, () => GraphOf(Current), "graph", Kind);
            _canvas.Changed += RefreshIssues;
            _main.Add(_canvas);
            _canvas.schedule.Execute(() => _canvas?.FrameAll()).StartingIn(50);
            RefreshIssues();
        }

        void RefreshIssues()
        {
            var data = Current;
            if (data == null || _issues == null) return;
            var context = new CollectingContext();
            data.ValidateData(context);
            var issues = context.Messages.Concat(GraphIssues(GraphOf(data))).ToList();
            _issues.text = issues.Count == 0 ? "" : "⚠ " + string.Join(" · ", issues.Take(3)) + (issues.Count > 3 ? $" 외 {issues.Count - 3}건" : "");
            _issues.tooltip = string.Join("\n", issues);
        }

        // ── 미리보기 ─────────────────────────────────────────

        // 미리보기 시작(플레이 모드에서). time: 그 시점으로 감아서 시작.
        protected abstract void StartPreview(GameData data, GsGraphCanvas canvas, float time);
        protected abstract string PreviewLabel { get; }

        Slider _timeSlider;
        Label _previewStatus;
        GsButton _pauseButton;
        GsButton _autoButton;

        VisualElement PreviewBar()
        {
            var bar = Gs.Row();
            bar.style.paddingLeft = 16;
            bar.style.paddingRight = 16;
            bar.style.paddingBottom = 8;
            bar.Add(new GsButton(PreviewLabel, () => StartPreview(Current, _canvas, 0f), GsButtonKind.Primary));
            _pauseButton = new GsButton("⏸", () =>
            {
                DialoguePreview.Paused = !DialoguePreview.Paused;
                RefreshPreview();
            }, GsButtonKind.Ghost) { tooltip = "일시정지 / 계속" };
            bar.Add(_pauseButton);
            _timeSlider = new Slider(0f, 10f) { tooltip = "끌면 그 시점으로 이동(일시정지)" };
            _timeSlider.style.width = 220;
            _timeSlider.style.marginLeft = 8;
            _timeSlider.RegisterValueChangedCallback(e =>
            {
                if (!Application.isPlaying)
                {
                    StartPreview(Current, _canvas, e.newValue);
                    return;
                }
                DialoguePreview.Paused = true;
                DialoguePreview.Seek(e.newValue);
                RefreshPreview();
            });
            bar.Add(_timeSlider);
            _previewStatus = Gs.Label("", "gs-caption");
            _previewStatus.style.marginLeft = 10;
            _previewStatus.style.minWidth = 190;
            bar.Add(_previewStatus);
            bar.Add(new GsButton("다음 ▷", DialoguePreview.Next, GsButtonKind.Ghost) { tooltip = "클릭한 것처럼(막는 효과 중이면 무시)" });
            _autoButton = new GsButton("자동", () =>
            {
                DialoguePreview.Auto = !DialoguePreview.Auto;
                RefreshPreview();
            }, GsButtonKind.Ghost);
            bar.Add(_autoButton);
            bar.Add(new GsButton("선택지까지 ⏭", DialoguePreview.SkipToChoice, GsButtonKind.Ghost));
            bar.Add(new GsButton("■", DialoguePreview.Stop, GsButtonKind.Ghost) { tooltip = "정지" });
            bar.schedule.Execute(RefreshPreview).Every(100);
            return bar;
        }

        void RefreshPreview()
        {
            if (_timeSlider == null) return;
            var time = DialoguePreview.Time;
            if (time > _timeSlider.highValue - 0.5f) _timeSlider.highValue = Mathf.Ceil(time + 5f);
            _timeSlider.SetValueWithoutNotify(time);
            _previewStatus.text = $"{time:0.00}초 · {DialoguePreview.Status}";
            _pauseButton.Text = DialoguePreview.Paused ? "▶" : "⏸";
            _autoButton.Text = DialoguePreview.Auto ? "자동 ✓" : "자동";
        }

        // 그래프 공통 검사(끊긴 흐름 등).
        protected virtual IEnumerable<string> GraphIssues(NodeGraph graph) => Enumerable.Empty<string>();

        void NewAsset(string folder)
        {
            var info = DataTypes.Find(AssetType);
            var suggested = EditorDatabase.Instance.NextId(info);
            GsDialog.Form(_root, $"새 {Noun}", "ID는 lowerCamelCase로 짓습니다(예: shopIntro).",
                new List<DialogField> { new DialogField { Key = "id", Label = "id", Value = suggested } }, "만들기",
                values => EditorDatabase.Instance.CanUseId(values["id"], null, out var error) ? null : error,
                values =>
                {
                    var data = EditorDatabase.Instance.Create(AssetType, values["id"]);
                    data.EditorAssignFolder(folder ?? "");
                    Seed(GraphOf(data));
                    EditorUtility.SetDirty(data);
                    AssetDatabase.SaveAssetIfDirty(data);
                    _selected = data.Id;
                    RefreshList();
                    OpenCanvas();
                });
        }

        sealed class CollectingContext : IValidationContext
        {
            public readonly List<string> Messages = new List<string>();
            public bool StatExists(string id) => true;
            public bool AchievementExists(string id) => true;
            public StatDefinitionInfo? FindStat(string id) => null;
            public void Report(ValidationSeverity severity, string message) => Messages.Add(message);
        }
    }

    // 다이얼로그 그래프 창.
    public sealed class DialogueGraphWindow : GraphAssetWindow
    {
        [MenuItem("MomoiProject/다이얼로그 시스템/그래프", priority = 5)]
        public static void Open() => OpenWith(null);

        public static DialogueGraphWindow OpenWith(string id)
        {
            var window = GBible.EditorTools.GsWindows.Get<DialogueGraphWindow>();
            window.titleContent = new GUIContent("다이얼로그 그래프");
            window.minSize = new Vector2(900, 520);
            if (id != null) window.ShowAsset(id);
            return window;
        }

        protected override Type AssetType => typeof(DialogueData);
        protected override GraphKinds Kind => GraphKinds.Dialogue;
        protected override string Noun => "다이얼로그";
        protected override NodeGraph GraphOf(GameData data) => (data as DialogueData)?.graph;

        protected override void Seed(NodeGraph graph)
        {
            var start = new StartNode { position = new Vector2(0, 0) };
            var line = new LineNode { position = new Vector2(320, 0), text = "첫 대사" };
            graph.Add(start);
            graph.Add(line);
            graph.Connect(start.guid, StartNode.NextPort, line.guid, GraphNode.FlowInPort);
        }

        protected override string PreviewLabel => "▶ 선택한 노드부터";

        protected override void StartPreview(GameData data, GsGraphCanvas canvas, float time)
        {
            if (!(data is DialogueData dialogue)) return;
            var node = canvas?.FirstSelected ?? dialogue.graph.OfType<StartNode>().FirstOrDefault()?.guid;
            if (node == null) return;
            AssetDatabase.SaveAssetIfDirty(dialogue);
            DialoguePreview.PlayDialogue(dialogue, node, time);
        }

        protected override IEnumerable<string> GraphIssues(NodeGraph graph)
        {
            if (graph == null) yield break;
            var reached = new HashSet<string>(graph.edges.Where(e => graph.Find(e.fromNode)?.Port(e.fromPort)?.Kind == PortKind.Flow).Select(e => e.toNode));
            var lonely = graph.nodes.Count(n => n != null && n.Port(GraphNode.FlowInPort) != null && !reached.Contains(n.guid));
            if (lonely > 0) yield return $"흐름이 들어오지 않는 노드 {lonely}개";
            var emptyLines = graph.OfType<LineNode>().Count(l => string.IsNullOrWhiteSpace(l.text));
            if (emptyLines > 0) yield return $"빈 대사 {emptyLines}개";
            foreach (var line in graph.OfType<LineNode>())
            {
                var error = TextFormat.Parse(line.text).Error;
                if (error != null) yield return "대사 문법 오류: " + error;
            }
        }
    }

    // 연출 프리셋 창: 효과 노드로 프리셋을 만든다. 다이얼로그 그래프에서는 '연출 프리셋' 노드 하나로 쓴다.
    public sealed class EffectPresetWindow : GraphAssetWindow
    {
        [MenuItem("MomoiProject/다이얼로그 시스템/연출 프리셋", priority = 6)]
        public static void Open() => OpenWith(null);

        public static EffectPresetWindow OpenWith(string id)
        {
            var window = GBible.EditorTools.GsWindows.Get<EffectPresetWindow>();
            window.titleContent = new GUIContent("연출 프리셋");
            window.minSize = new Vector2(900, 520);
            if (id != null) window.ShowAsset(id);
            return window;
        }

        protected override Type AssetType => typeof(EffectPresetData);
        protected override GraphKinds Kind => GraphKinds.Preset;
        protected override string Noun => "프리셋";
        protected override NodeGraph GraphOf(GameData data) => (data as EffectPresetData)?.graph;

        protected override void Seed(NodeGraph graph) => graph.Add(new PresetEntryNode { position = Vector2.zero });

        protected override string PreviewLabel => "▶ 프리셋 재생";

        protected override void StartPreview(GameData data, GsGraphCanvas canvas, float time)
        {
            if (!(data is EffectPresetData preset)) return;
            AssetDatabase.SaveAssetIfDirty(preset);
            DialoguePreview.PlayPreset(preset, time);
        }

        protected override IEnumerable<string> GraphIssues(NodeGraph graph)
        {
            if (graph == null) yield break;
            var entries = graph.OfType<PresetEntryNode>().ToList();
            if (entries.Count > 0 && !entries.Any(e => graph.From(e.guid, PresetEntryNode.StartPort).Any())) yield return "'프리셋 시작'에 이어진 효과가 없습니다";
            foreach (var duplicate in graph.OfType<PresetOutputNode>().GroupBy(o => o.signal).Where(g => g.Count() > 1))
                yield return $"내보내는 신호 '{duplicate.Key}'가 {duplicate.Count()}곳에 있습니다(한 이름은 한 곳에서)";
        }
    }

    // 다이얼로그 그래프의 프리셋 노드에 프리셋이 내보내는 신호 점을 만든다.
    static class PresetOutputs
    {
        [InitializeOnLoadMethod]
        public static void Install()
        {
            PresetEffect.DefaultOutputsOf = id =>
                string.IsNullOrEmpty(id)
                    ? Enumerable.Empty<string>()
                    : EditorDatabase.Instance.Committed.OfType<EffectPresetData>().FirstOrDefault(p => p.Id == id)?.Outputs.ToList() ?? Enumerable.Empty<string>();
        }
    }
}
