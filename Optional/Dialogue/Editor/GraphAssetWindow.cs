using System;
using System.Collections.Generic;
using System.Linq;
using GameSystems.Data;
using GameSystems.Dialogue;
using GameSystems.EditorTools.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameSystems.EditorTools.Graph
{
    // 그래프 에셋(대화·연출 프리셋)을 왼쪽 목록에서 고르고 오른쪽 그래프에서 편집하는 창의 공통 부분.
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

            var side = Gs.Col("gs-graph-window__side");
            var head = Gs.Row();
            head.Add(Gs.Label(Noun, "gs-heading"));
            head.Add(Gs.Spacer());
            head.Add(new GsButton("+ 새로", NewAsset, GsButtonKind.Ghost));
            side.Add(head);
            side.Add(Gs.Space(8));
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

        void RefreshList()
        {
            if (_list == null) return;
            _list.Clear();
            var any = false;
            foreach (var data in Assets)
            {
                any = true;
                var item = Gs.Row("gs-graph-window__item");
                item.EnableInClassList("gs-graph-window__item--active", data.Id == _selected);
                item.Add(Gs.Label(data.Id, "gs-nowrap"));
                var id = data.Id;
                item.AddManipulator(new Clickable(() =>
                {
                    _selected = id;
                    RefreshList();
                    OpenCanvas();
                }));
                _list.Add(item);
            }
            if (!any) _list.Add(Gs.Label($"아직 {Noun}가 없습니다.\n'+ 새로'로 만드세요.", "gs-caption"));
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

        // 그래프 공통 검사(끊긴 흐름 등).
        protected virtual IEnumerable<string> GraphIssues(NodeGraph graph) => Enumerable.Empty<string>();

        void NewAsset()
        {
            var info = DataTypes.Find(AssetType);
            var suggested = EditorDatabase.Instance.NextId(info);
            GsDialog.Form(_root, $"새 {Noun}", "ID는 lowerCamelCase로 짓습니다(예: shopIntro).",
                new List<DialogField> { new DialogField { Key = "id", Label = "id", Value = suggested } }, "만들기",
                values => EditorDatabase.Instance.CanUseId(values["id"], null, out var error) ? null : error,
                values =>
                {
                    var data = EditorDatabase.Instance.Create(AssetType, values["id"]);
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

    // 대화 그래프 창.
    public sealed class DialogueGraphWindow : GraphAssetWindow
    {
        [MenuItem("Game Systems/대화 그래프", priority = 5)]
        public static void Open() => OpenWith(null);

        public static DialogueGraphWindow OpenWith(string id)
        {
            var window = GetWindow<DialogueGraphWindow>();
            window.titleContent = new GUIContent("대화 그래프");
            window.minSize = new Vector2(900, 520);
            if (id != null) window.ShowAsset(id);
            return window;
        }

        protected override Type AssetType => typeof(DialogueData);
        protected override GraphKinds Kind => GraphKinds.Dialogue;
        protected override string Noun => "대화";
        protected override NodeGraph GraphOf(GameData data) => (data as DialogueData)?.graph;

        protected override void Seed(NodeGraph graph)
        {
            var start = new StartNode { position = new Vector2(0, 0) };
            var line = new LineNode { position = new Vector2(320, 0), text = "첫 대사" };
            graph.Add(start);
            graph.Add(line);
            graph.Connect(start.guid, StartNode.NextPort, line.guid, GraphNode.FlowInPort);
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

    // 연출 프리셋 창: 효과 노드로 프리셋을 만든다. 대화 그래프에서는 '연출 프리셋' 노드 하나로 쓴다.
    public sealed class EffectPresetWindow : GraphAssetWindow
    {
        [MenuItem("Game Systems/연출 프리셋", priority = 6)]
        public static void Open() => OpenWith(null);

        public static EffectPresetWindow OpenWith(string id)
        {
            var window = GetWindow<EffectPresetWindow>();
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

        protected override IEnumerable<string> GraphIssues(NodeGraph graph)
        {
            if (graph == null) yield break;
            var entries = graph.OfType<PresetEntryNode>().ToList();
            if (entries.Count > 0 && !entries.Any(e => graph.From(e.guid, PresetEntryNode.StartPort).Any())) yield return "'프리셋 시작'에 이어진 효과가 없습니다";
            foreach (var duplicate in graph.OfType<PresetOutputNode>().GroupBy(o => o.signal).Where(g => g.Count() > 1))
                yield return $"내보내는 신호 '{duplicate.Key}'가 {duplicate.Count()}곳에 있습니다(한 이름은 한 곳에서)";
        }
    }

    // 대화 그래프의 프리셋 노드에 프리셋이 내보내는 신호 점을 만든다.
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
