using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GBible.Dialogue;
using GBible.EditorTools.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace GBible.EditorTools.Graph
{
    // 노드 그래프 편집 화면: 격자 배경, 끌어서 이동(빈 곳), 휠 확대·축소, 우클릭으로 노드 만들기,
    // 점을 끌어 잇기(빈 곳에 놓으면 노드를 만들어 바로 잇기), 선 클릭 선택, Delete 지우기, Ctrl+D 복제, F 전체 보기.
    // 값은 에셋에 바로 저장한다(Undo 가능).
    public sealed class GsGraphCanvas : VisualElement
    {
        const float GridStep = 20f;
        const float MinZoom = 0.2f;
        const float MaxZoom = 2f;

        enum DragMode
        {
            None,
            Pan,
            Move,
            Connect,
            Box,
            Resize,
        }

        readonly Object _owner;
        readonly Func<NodeGraph> _graph;
        readonly string _path;
        readonly GraphKinds _kind;
        readonly VisualElement _content;
        readonly VisualElement _edges;
        readonly VisualElement _nodes;
        readonly VisualElement _groups;
        readonly VisualElement _marquee;
        readonly Label _zoomLabel;
        readonly Dictionary<string, ItemView> _views = new Dictionary<string, ItemView>();
        readonly Dictionary<(string node, string port), VisualElement> _dots = new Dictionary<(string, string), VisualElement>();
        readonly HashSet<string> _selected = new HashSet<string>();
        SerializedObject _so;
        GraphEdge _selectedEdge;
        Vector2 _pan = new Vector2(80, 80);
        float _zoom = 1f;
        bool _saveQueued;

        DragMode _drag;
        int _pointer = -1;
        bool _moveRecorded;
        (string node, string port, PortSide side, PortKind kind) _from;
        Vector2 _connectEnd;
        Vector2 _boxStart;
        Vector2 _lastLocal;
        bool _spaceHeld;
        string _resizing;
        readonly HashSet<string> _moving = new HashSet<string>();

        // 접힌 노드(편집 세션 동안만).
        public readonly HashSet<string> Collapsed = new HashSet<string>();

        public event Action Changed;

        public GsGraphCanvas(Object owner, Func<NodeGraph> graph, string path, GraphKinds kind)
        {
            _owner = owner;
            _graph = graph;
            _path = path;
            _kind = kind;
            AddToClassList("gs-graph");
            var sheet = Gs.LoadSheet("GsGraph");
            if (sheet != null) styleSheets.Add(sheet);
            focusable = true;
            generateVisualContent += DrawGrid;

            _content = new VisualElement { pickingMode = PickingMode.Ignore };
            _content.AddToClassList("gs-graph__content");
            Add(_content);
            _edges = new VisualElement { pickingMode = PickingMode.Ignore };
            _edges.AddToClassList("gs-graph__edges");
            _edges.generateVisualContent += DrawEdges;
            _groups = new VisualElement { pickingMode = PickingMode.Ignore };
            _groups.AddToClassList("gs-graph__nodes");
            _content.Add(_groups);
            _content.Add(_edges);
            _nodes = new VisualElement { pickingMode = PickingMode.Ignore };
            _nodes.AddToClassList("gs-graph__nodes");
            _content.Add(_nodes);

            var tools = Gs.Row("gs-graph__toolbar");
            tools.Add(new GsButton("－", () => ZoomBy(1 / 1.2f, Center), GsButtonKind.Ghost));
            _zoomLabel = Gs.Label("100%", "gs-caption");
            _zoomLabel.style.minWidth = 40;
            _zoomLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _zoomLabel.AddManipulator(new Clickable(() => ZoomTo(1f, Center)));
            tools.Add(_zoomLabel);
            tools.Add(new GsButton("＋", () => ZoomBy(1.2f, Center), GsButtonKind.Ghost));
            tools.Add(new GsButton("전체 보기", FrameAll, GsButtonKind.Ghost));
            var hint = Gs.Label("우클릭: 노드 만들기 · 점 끌기: 잇기 · 빈 곳 끌기: 범위 선택 · 가운데 버튼·Alt·Space 끌기: 화면 이동 · 휠: 확대 · Ctrl+C/V/D · Ctrl+F: 노드 찾기", "gs-caption");
            hint.style.marginLeft = 12;
            tools.Add(hint);
            Add(tools);

            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerCaptureOutEvent>(_ => EndDrag());
            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<KeyDownEvent>(OnKey);
            RegisterCallback<KeyUpEvent>(e =>
            {
                if (e.keyCode == KeyCode.Space) _spaceHeld = false;
            });
            RegisterCallback<PointerMoveEvent>(e => _lastLocal = this.WorldToLocal(e.position), TrickleDown.TrickleDown);

            _marquee = new VisualElement { pickingMode = PickingMode.Ignore };
            _marquee.AddToClassList("gs-graph__marquee");
            _marquee.style.display = DisplayStyle.None;
            Add(_marquee);
            RegisterCallback<GeometryChangedEvent>(_ => MarkDirtyRepaint());
            RegisterCallback<AttachToPanelEvent>(_ => Undo.undoRedoPerformed += OnUndoRedo);
            RegisterCallback<DetachFromPanelEvent>(_ => Undo.undoRedoPerformed -= OnUndoRedo);

            ApplyTransform();
            Rebuild();
        }

        NodeGraph Graph => _graph();
        Vector2 Center => contentRect.center;
        public float Zoom => _zoom;
        // 선택한 노드(처음 것). 없으면 null.
        public string FirstSelected => _selected.FirstOrDefault();
        public IReadOnlyCollection<string> Selection => _selected;

        public void SelectOnly(IEnumerable<string> guids)
        {
            ClearNodeSelection();
            foreach (var guid in guids) SelectNode(guid, true);
        }

        public void CopySelection() => GraphClipboard.Copy(Graph, _selected);
        public void PasteAt(Vector2 graphPoint) => Paste(GraphClipboard.Nodes, GraphClipboard.Edges, graphPoint - GraphClipboard.Origin);
        public void DuplicateSelection() => Duplicate(_selected.ToList());
        public Vector2 Pan => _pan;

        public void SetView(Vector2 pan, float zoom)
        {
            _pan = pan;
            _zoom = Mathf.Clamp(zoom, MinZoom, MaxZoom);
            ApplyTransform();
        }

        void OnUndoRedo()
        {
            if (_owner == null) return;
            Rebuild();
        }

        // ── 다시 그리기 ─────────────────────────────────────

        public void Rebuild()
        {
            _nodes.Clear();
            _groups.Clear();
            _views.Clear();
            _dots.Clear();
            if (_owner == null || Graph == null) return;
            _so = new SerializedObject(_owner);
            var list = _so.FindProperty(_path + ".nodes");
            for (var i = 0; i < Graph.nodes.Count; i++)
            {
                var node = Graph.nodes[i];
                if (node == null) continue;
                if (node is GroupNode)
                {
                    var group = new GroupView(this, node.guid);
                    _views[node.guid] = group;
                    _groups.Add(group);
                    continue;
                }
                var view = new NodeView(this, node.guid, list.GetArrayElementAtIndex(i));
                _views[node.guid] = view;
                _nodes.Add(view);
            }
            _selected.RemoveWhere(id => !_views.ContainsKey(id));
            if (_selectedEdge != null && !Graph.edges.Any(e => e.Same(_selectedEdge))) _selectedEdge = null;
            foreach (var view in _views.Values) view.SetSelected(_selected.Contains(view.Guid));
            RepaintEdges();
        }

        void RepaintEdges()
        {
            _edges.MarkDirtyRepaint();
            schedule.Execute(() => _edges.MarkDirtyRepaint());
        }

        // 값이 바뀌었다(에셋은 이미 바뀐 상태). structure: 노드가 늘거나 줄어 전체를 다시 만든다.
        void Touched(bool structure)
        {
            if (_owner == null) return;
            EditorUtility.SetDirty(_owner);
            if (structure) Rebuild();
            else
            {
                _so?.Update();
                RepaintEdges();
            }
            QueueSave();
            Changed?.Invoke();
        }

        void QueueSave()
        {
            if (_saveQueued) return;
            _saveQueued = true;
            schedule.Execute(() =>
            {
                _saveQueued = false;
                if (_owner != null) AssetDatabase.SaveAssetIfDirty(_owner);
            }).StartingIn(400);
        }

        void Record(string what) => Undo.RecordObject(_owner, what);

        // ── 좌표 ─────────────────────────────────────────────

        void ApplyTransform()
        {
            _content.style.translate = new Translate(_pan.x, _pan.y);
            _content.style.scale = new Scale(new Vector2(_zoom, _zoom));
            _zoomLabel.text = Mathf.RoundToInt(_zoom * 100) + "%";
            MarkDirtyRepaint();
            RepaintEdges();
        }

        Vector2 ToGraph(Vector2 local) => (local - _pan) / _zoom;

        void ZoomBy(float factor, Vector2 around) => ZoomTo(_zoom * factor, around);

        void ZoomTo(float zoom, Vector2 around)
        {
            var point = ToGraph(around);
            _zoom = Mathf.Clamp(zoom, MinZoom, MaxZoom);
            _pan = around - point * _zoom;
            ApplyTransform();
        }

        public void FrameAll()
        {
            if (_views.Count == 0)
            {
                SetView(new Vector2(80, 80), 1f);
                return;
            }
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (var view in _views.Values)
            {
                var node = Graph.Find(view.Guid);
                if (node == null) continue;
                var size = new Vector2(Mathf.Max(view.layout.width, 260), Mathf.Max(view.layout.height, 80));
                min = Vector2.Min(min, node.position);
                max = Vector2.Max(max, node.position + size);
            }
            var area = contentRect.size - new Vector2(80, 120);
            var zoom = Mathf.Clamp(Mathf.Min(area.x / Mathf.Max(1, max.x - min.x), area.y / Mathf.Max(1, max.y - min.y)), MinZoom, 1f);
            var center = (min + max) / 2;
            SetView(contentRect.center + new Vector2(0, 20) - center * zoom, zoom);
        }

        // ── 그리기 ───────────────────────────────────────────

        void DrawGrid(MeshGenerationContext context)
        {
            var rect = contentRect;
            var painter = context.painter2D;
            var step = GridStep * _zoom;
            if (step >= 6) Lines(painter, rect, step, new Color(1, 1, 1, 0.025f));
            Lines(painter, rect, step * 5, new Color(1, 1, 1, 0.055f));
        }

        void Lines(Painter2D painter, Rect rect, float step, Color color)
        {
            painter.strokeColor = color;
            painter.lineWidth = 1f;
            painter.BeginPath();
            var x0 = Mathf.Repeat(_pan.x, step);
            for (var x = x0; x < rect.width; x += step)
            {
                painter.MoveTo(new Vector2(x, 0));
                painter.LineTo(new Vector2(x, rect.height));
            }
            var y0 = Mathf.Repeat(_pan.y, step);
            for (var y = y0; y < rect.height; y += step)
            {
                painter.MoveTo(new Vector2(0, y));
                painter.LineTo(new Vector2(rect.width, y));
            }
            painter.Stroke();
        }

        static readonly Color FlowColor = new Color(0.95f, 0.95f, 0.95f, 0.75f);
        static readonly Color SignalColor = Gs.WithAlpha(Gs.Accent, 0.85f);

        void DrawEdges(MeshGenerationContext context)
        {
            var graph = Graph;
            if (graph == null) return;
            var painter = context.painter2D;
            foreach (var edge in graph.edges)
            {
                if (!TryDot(edge.fromNode, edge.fromPort, out var a) || !TryDot(edge.toNode, edge.toPort, out var b)) continue;
                var kind = graph.Find(edge.fromNode)?.Port(edge.fromPort)?.Kind ?? PortKind.Flow;
                var selected = _selectedEdge != null && edge.Same(_selectedEdge);
                var color = kind == PortKind.Flow ? FlowColor : SignalColor;
                Curve(painter, a, b, selected ? Color.white : color, selected ? 3.5f : kind == PortKind.Flow ? 2.2f : 1.8f);
            }
            if (_drag == DragMode.Connect && TryDot(_from.node, _from.port, out var start))
            {
                var end = _connectEnd;
                var color = _from.kind == PortKind.Flow ? FlowColor : SignalColor;
                if (_from.side == PortSide.Out) Curve(painter, start, end, color, 2f);
                else Curve(painter, end, start, color, 2f);
            }
        }

        static void Curve(Painter2D painter, Vector2 a, Vector2 b, Color color, float width)
        {
            var bend = Mathf.Max(40f, Mathf.Abs(b.x - a.x) * 0.5f);
            painter.strokeColor = color;
            painter.lineWidth = width;
            painter.lineCap = LineCap.Round;
            painter.BeginPath();
            painter.MoveTo(a);
            painter.BezierCurveTo(a + new Vector2(bend, 0), b - new Vector2(bend, 0), b);
            painter.Stroke();
        }

        // 점의 중심(그래프 좌표 = _content 안 좌표).
        bool TryDot(string node, string port, out Vector2 point)
        {
            point = default;
            if (!_dots.TryGetValue((node, port), out var dot) || dot.panel == null) return false;
            var bound = dot.worldBound;
            if (float.IsNaN(bound.width) || bound.width <= 0) return false;
            point = _content.WorldToLocal(bound.center);
            return true;
        }

        GraphEdge EdgeAt(Vector2 graphPoint)
        {
            var graph = Graph;
            var tolerance = 7f / _zoom;
            foreach (var edge in graph.edges)
            {
                if (!TryDot(edge.fromNode, edge.fromPort, out var a) || !TryDot(edge.toNode, edge.toPort, out var b)) continue;
                var bend = Mathf.Max(40f, Mathf.Abs(b.x - a.x) * 0.5f);
                var c1 = a + new Vector2(bend, 0);
                var c2 = b - new Vector2(bend, 0);
                var previous = a;
                for (var i = 1; i <= 32; i++)
                {
                    var t = i / 32f;
                    var u = 1 - t;
                    var p = u * u * u * a + 3 * u * u * t * c1 + 3 * u * t * t * c2 + t * t * t * b;
                    if (DistanceToSegment(graphPoint, previous, p) < tolerance) return edge;
                    previous = p;
                }
            }
            return null;
        }

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var t = ab.sqrMagnitude < 1e-6f ? 0 : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        // ── 입력 ─────────────────────────────────────────────

        void OnPointerDown(PointerDownEvent e)
        {
            Focus();
            var local = (Vector2)e.localPosition;
            if (e.target != this && e.target != _content && e.target != _edges && e.target != _nodes) return;

            if (e.button == 1)
            {
                var edge = EdgeAt(ToGraph(local));
                if (edge != null) EdgeMenu(edge, local);
                else CreateMenu(local, null);
                e.StopPropagation();
                return;
            }

            if (e.button == 0)
            {
                var edge = EdgeAt(ToGraph(local));
                if (edge != null)
                {
                    _selectedEdge = edge;
                    ClearNodeSelection();
                    RepaintEdges();
                    e.StopPropagation();
                    return;
                }
                if (!e.shiftKey) ClearNodeSelection();
                _selectedEdge = null;
                RepaintEdges();
                if (e.altKey || _spaceHeld) BeginDrag(DragMode.Pan, e.pointerId);
                else
                {
                    _boxStart = local;
                    UpdateMarquee(local);
                    _marquee.style.display = DisplayStyle.Flex;
                    BeginDrag(DragMode.Box, e.pointerId);
                }
            }
            if (e.button == 2) BeginDrag(DragMode.Pan, e.pointerId);
            e.StopPropagation();
        }

        void UpdateMarquee(Vector2 local)
        {
            var min = Vector2.Min(_boxStart, local);
            var max = Vector2.Max(_boxStart, local);
            _marquee.style.left = min.x;
            _marquee.style.top = min.y;
            _marquee.style.width = max.x - min.x;
            _marquee.style.height = max.y - min.y;
        }

        // 옮길 노드: 고른 노드 + 고른 그룹 상자 안에 든 노드(끌기 시작할 때 정한다).
        void CollectMoving()
        {
            _moving.Clear();
            foreach (var id in _selected)
            {
                _moving.Add(id);
                if (Graph.Find(id) is GroupNode group)
                    foreach (var inside in Inside(group))
                        _moving.Add(inside);
            }
        }

        IEnumerable<string> Inside(GroupNode group)
        {
            var rect = new Rect(group.position, group.size);
            foreach (var pair in _views)
            {
                if (pair.Key == group.guid) continue;
                var node = Graph.Find(pair.Key);
                if (node == null) continue;
                var size = new Vector2(Mathf.Max(pair.Value.layout.width, 1), Mathf.Max(pair.Value.layout.height, 1));
                if (rect.Contains(node.position + size * 0.5f)) yield return pair.Key;
            }
        }

        void BeginDrag(DragMode mode, int pointerId)
        {
            _drag = mode;
            _pointer = pointerId;
            _moveRecorded = false;
            if (mode == DragMode.Move) CollectMoving();
            this.CapturePointer(pointerId);
        }

        void OnPointerMove(PointerMoveEvent e)
        {
            if (_drag == DragMode.None || e.pointerId != _pointer) return;
            var delta = (Vector2)e.deltaPosition;
            switch (_drag)
            {
                case DragMode.Pan:
                    _pan += delta;
                    ApplyTransform();
                    break;
                case DragMode.Move:
                    if (!_moveRecorded)
                    {
                        Record("노드 옮기기");
                        _moveRecorded = true;
                    }
                    foreach (var id in _moving)
                    {
                        var node = Graph.Find(id);
                        if (node == null || !_views.TryGetValue(id, out var view)) continue;
                        node.position += delta / _zoom;
                        view.Place(node.position);
                    }
                    RepaintEdges();
                    break;
                case DragMode.Box:
                    UpdateMarquee(this.WorldToLocal(e.position));
                    break;
                case DragMode.Resize:
                    if (!(Graph.Find(_resizing) is GroupNode resized) || !_views.TryGetValue(_resizing, out var resizedView)) break;
                    if (!_moveRecorded)
                    {
                        Record("그룹 상자 크기");
                        _moveRecorded = true;
                    }
                    resized.size = Vector2.Max(new Vector2(200, 120), resized.size + delta / _zoom);
                    ((GroupView)resizedView).Resize(resized.size);
                    break;
                case DragMode.Connect:
                    _connectEnd = ToGraph(this.WorldToLocal(e.position));
                    RepaintEdges();
                    break;
            }
        }

        void OnPointerUp(PointerUpEvent e)
        {
            if (_drag == DragMode.None || e.pointerId != _pointer) return;
            var mode = _drag;
            var from = _from;
            EndDrag();
            if (mode == DragMode.Move && _moveRecorded)
            {
                // 격자에 맞춘다.
                foreach (var id in _moving)
                {
                    var node = Graph.Find(id);
                    if (node == null) continue;
                    node.position = new Vector2(Mathf.Round(node.position.x / 10) * 10, Mathf.Round(node.position.y / 10) * 10);
                    if (_views.TryGetValue(id, out var view)) view.Place(node.position);
                }
                Touched(false);
            }
            if (mode == DragMode.Resize && _moveRecorded) Touched(false);
            if (mode == DragMode.Box)
            {
                _marquee.style.display = DisplayStyle.None;
                var box = _marquee.worldBound;
                foreach (var pair in _views)
                {
                    var bound = pair.Value.worldBound;
                    // 노드는 겹치면, 그룹 상자는 다 들어오면 고른다.
                    var hit = pair.Value is GroupView ? box.Contains(bound.min) && box.Contains(bound.max) : box.Overlaps(bound);
                    if (!hit) continue;
                    _selected.Add(pair.Key);
                    pair.Value.SetSelected(true);
                }
            }
            if (mode == DragMode.Connect)
            {
                var target = PortUnder(e.position);
                if (target != null) TryConnect(from, target.Value);
                else CreateMenu(this.WorldToLocal(e.position), from);
            }
        }

        void EndDrag()
        {
            if (_pointer >= 0 && this.HasPointerCapture(_pointer)) this.ReleasePointer(_pointer);
            var wasConnect = _drag == DragMode.Connect;
            _drag = DragMode.None;
            _pointer = -1;
            if (wasConnect) RepaintEdges();
        }

        (string node, string port, PortSide side, PortKind kind)? PortUnder(Vector2 worldPosition)
        {
            for (var element = panel?.Pick(worldPosition); element != null; element = element.parent)
                if (element.userData is PortTag tag)
                    return (tag.Node, tag.Port, tag.Side, tag.Kind);
            // 점 근처(작은 점을 정확히 맞추기 어렵다).
            foreach (var pair in _dots)
            {
                var bound = pair.Value.worldBound;
                if (Vector2.Distance(bound.center, worldPosition) < 14f && pair.Value.userData is PortTag tag)
                    return (tag.Node, tag.Port, tag.Side, tag.Kind);
            }
            return null;
        }

        void TryConnect((string node, string port, PortSide side, PortKind kind) a, (string node, string port, PortSide side, PortKind kind) b)
        {
            var (from, to) = a.side == PortSide.Out ? (a, b) : (b, a);
            if (!Graph.CanConnect(from.node, from.port, to.node, to.port, out var reason))
            {
                if (a.node != b.node || a.port != b.port) Debug.Log("[다이얼로그 그래프] " + reason);
                return;
            }
            Record("선 잇기");
            Graph.Connect(from.node, from.port, to.node, to.port);
            Touched(false);
            RefreshDots();
        }

        void OnWheel(WheelEvent e)
        {
            ZoomBy(Mathf.Pow(1.1f, -e.delta.y / 3f), this.WorldToLocal(e.mousePosition));
            e.StopPropagation();
        }

        void OnKey(KeyDownEvent e)
        {
            // 칸에 글을 쓰는 중이면 단축키를 쓰지 않는다.
            if (panel?.focusController?.focusedElement is VisualElement focused && (focused is TextField || focused.GetFirstAncestorOfType<TextField>() != null)) return;
            switch (e.keyCode)
            {
                case KeyCode.Delete:
                case KeyCode.Backspace:
                    DeleteSelection();
                    e.StopPropagation();
                    break;
                case KeyCode.D when e.actionKey:
                    Duplicate(_selected.ToList());
                    e.StopPropagation();
                    break;
                case KeyCode.C when e.actionKey:
                    GraphClipboard.Copy(Graph, _selected);
                    e.StopPropagation();
                    break;
                case KeyCode.V when e.actionKey:
                    Paste(GraphClipboard.Nodes, GraphClipboard.Edges, ToGraph(_lastLocal) - GraphClipboard.Origin);
                    e.StopPropagation();
                    break;
                case KeyCode.F when e.actionKey:
                    SearchMenu();
                    e.StopPropagation();
                    break;
                case KeyCode.Space:
                    _spaceHeld = true;
                    break;
                case KeyCode.F:
                    FrameAll();
                    e.StopPropagation();
                    break;
            }
        }

        // ── 선택 ─────────────────────────────────────────────

        void ClearNodeSelection()
        {
            foreach (var id in _selected)
                if (_views.TryGetValue(id, out var view))
                    view.SetSelected(false);
            _selected.Clear();
        }

        void SelectNode(string guid, bool additive)
        {
            if (!additive && !_selected.Contains(guid)) ClearNodeSelection();
            _selected.Add(guid);
            if (_views.TryGetValue(guid, out var view))
            {
                view.SetSelected(true);
                view.BringToFront();
            }
            _selectedEdge = null;
            RepaintEdges();
        }

        public void Select(string guid)
        {
            SelectNode(guid, false);
            var node = Graph.Find(guid);
            if (node != null) SetView(contentRect.center - (node.position + new Vector2(130, 60)) * _zoom, _zoom);
        }

        // ── 편집 ─────────────────────────────────────────────

        void DeleteSelection()
        {
            if (_selected.Count == 0 && _selectedEdge == null) return;
            Record("지우기");
            if (_selectedEdge != null) Graph.edges.RemoveAll(e => e.Same(_selectedEdge));
            foreach (var id in _selected.ToList())
            {
                var node = Graph.Find(id);
                if (node != null) Graph.Remove(node);
            }
            _selected.Clear();
            _selectedEdge = null;
            Touched(true);
        }

        void DeleteNode(string guid)
        {
            var node = Graph.Find(guid);
            if (node == null) return;
            Record("노드 지우기");
            Graph.Remove(node);
            _selected.Remove(guid);
            Touched(true);
        }

        // 복제: 고른 노드와 그 사이의 선을 함께(클립보드는 건드리지 않는다).
        void Duplicate(List<string> guids)
        {
            var nodes = guids.Select(g => Graph.Find(g)).Where(n => n != null).ToList();
            var ids = new HashSet<string>(nodes.Select(n => n.guid));
            Paste(nodes, Graph.edges.Where(e => ids.Contains(e.fromNode) && ids.Contains(e.toNode)).ToList(), new Vector2(30, 30));
        }

        // 노드들을 새 ID로 복사해 넣는다(선은 복사한 노드끼리만 다시 잇는다). 이 그래프에 못 쓰는 노드는 건너뛴다.
        void Paste(IReadOnlyList<GraphNode> nodes, IReadOnlyList<GraphEdge> edges, Vector2 offset)
        {
            if (nodes == null || nodes.Count == 0) return;
            Record("노드 붙여넣기");
            var map = new Dictionary<string, string>();
            var skipped = 0;
            foreach (var node in nodes)
            {
                var info = node.GetType().GetCustomAttribute<GraphNodeAttribute>(false);
                if (info == null || (info.Graphs & _kind) == 0)
                {
                    skipped++;
                    continue;
                }
                var copy = NodeClipboard.Clone(node);
                copy.guid = GraphNode.NewId();
                copy.position = new Vector2(Mathf.Round((node.position.x + offset.x) / 10) * 10, Mathf.Round((node.position.y + offset.y) / 10) * 10);
                FreshKeys(copy);
                Graph.Add(copy);
                map[node.guid] = copy.guid;
            }
            foreach (var edge in edges ?? Array.Empty<GraphEdge>())
                if (map.TryGetValue(edge.fromNode, out var from) && map.TryGetValue(edge.toNode, out var to))
                    Graph.Connect(from, edge.fromPort, to, edge.toPort);
            if (skipped > 0) Debug.Log($"[다이얼로그 그래프] 이 그래프에서 쓸 수 없는 노드 {skipped}개는 붙여 넣지 않았습니다.");
            _selected.Clear();
            foreach (var id in map.Values) _selected.Add(id);
            Touched(true);
        }

        // 노드 찾기: 종류·요약·대사 글로 검색해 그 노드로 간다.
        void SearchMenu()
        {
            var items = new List<GsDropdownItem>();
            foreach (var node in Graph.nodes.Where(n => n != null))
            {
                var info = node.GetType().GetCustomAttribute<GraphNodeAttribute>(false);
                var detail = node is LineNode line ? line.text : node is NoteNode note ? note.text : null;
                if (detail != null && detail.Length > 40) detail = detail.Substring(0, 40) + "…";
                var guid = node.guid;
                items.Add(new GsDropdownItem(info?.Name ?? node.GetType().Name, () => Select(guid)) { Caption = node.Summary, Detail = detail, Group = info?.Category });
            }
            ShowMenu(new Vector2(contentRect.width * 0.5f - 150, 60), items, true);
        }

        // 복제한 노드는 번역 키를 새로 받는다(같은 키가 둘이면 한 줄의 번역이 두 곳에 쓰인다).
        static void FreshKeys(GraphNode node)
        {
            if (node is LineNode line)
            {
                line.lineId = DialogueIds.NewLineId();
                foreach (var option in line.options) option.lineId = DialogueIds.NewLineId();
            }
        }

        GraphNode CreateNode(Type type, Vector2 graphPosition, (string node, string port, PortSide side, PortKind kind)? connectFrom)
        {
            Record("노드 추가");
            var node = (GraphNode)Activator.CreateInstance(type);
            node.position = new Vector2(Mathf.Round(graphPosition.x / 10) * 10, Mathf.Round(graphPosition.y / 10) * 10);
            Graph.Add(node);
            if (connectFrom != null)
            {
                var from = connectFrom.Value;
                var wanted = from.side == PortSide.Out ? PortSide.In : PortSide.Out;
                var port = node.Ports.Where(p => p.Kind == from.kind && p.Side == wanted).Select(p => (PortInfo?)p).FirstOrDefault();
                if (port != null)
                {
                    if (from.side == PortSide.Out) Graph.Connect(from.node, from.port, node.guid, port.Value.Id);
                    else Graph.Connect(node.guid, port.Value.Id, from.node, from.port);
                }
            }
            _selected.Clear();
            _selected.Add(node.guid);
            Touched(true);
            return node;
        }

        // 노드 종류(이 그래프에서 쓸 수 있는 것). 분류 순서 → Order → 이름.
        static readonly string[] CategoryOrder = { "흐름", "프리셋", "화면", "스탠딩", "이미지", "움직임", "소리", "흐름 제어", "정리" };

        public static IEnumerable<(Type type, GraphNodeAttribute info)> NodeTypes(GraphKinds kind) =>
            TypeCache.GetTypesDerivedFrom<GraphNode>()
                .Where(t => !t.IsAbstract && !t.IsGenericType)
                .Select(t => (type: t, info: t.GetCustomAttribute<GraphNodeAttribute>(false)))
                .Where(x => x.info != null && (x.info.Graphs & kind) != 0)
                .OrderBy(x =>
                {
                    var i = Array.IndexOf(CategoryOrder, x.info.Category);
                    return i < 0 ? CategoryOrder.Length : i;
                })
                .ThenBy(x => x.info.Order)
                .ThenBy(x => x.info.Name, StringComparer.Ordinal);

        void CreateMenu(Vector2 local, (string node, string port, PortSide side, PortKind kind)? connectFrom)
        {
            var graphPoint = ToGraph(local);
            var items = new List<GsDropdownItem>();
            foreach (var (type, info) in NodeTypes(_kind))
            {
                if (connectFrom != null)
                {
                    // 끌어 온 점과 이을 수 있는 노드만.
                    var probe = (GraphNode)Activator.CreateInstance(type);
                    var wanted = connectFrom.Value.side == PortSide.Out ? PortSide.In : PortSide.Out;
                    if (!probe.Ports.Any(p => p.Kind == connectFrom.Value.kind && p.Side == wanted)) continue;
                }
                items.Add(new GsDropdownItem(info.Name, () => CreateNode(type, graphPoint, connectFrom)) { Group = info.Category });
            }
            ShowMenu(local, items, true);
        }

        void EdgeMenu(GraphEdge edge, Vector2 local)
        {
            _selectedEdge = edge;
            RepaintEdges();
            ShowMenu(local, new List<GsDropdownItem>
            {
                new GsDropdownItem("선 지우기", () =>
                {
                    Record("선 지우기");
                    Graph.edges.RemoveAll(e => e.Same(edge));
                    _selectedEdge = null;
                    Touched(false);
                    RefreshDots();
                }),
            }, false);
        }

        void NodeMenu(string guid, Vector2 local)
        {
            var targets = _selected.Contains(guid) ? _selected.ToList() : new List<string> { guid };
            ShowMenu(local, new List<GsDropdownItem>
            {
                new GsDropdownItem(Collapsed.Contains(guid) ? "펼치기" : "접기", () => ToggleCollapsed(guid)),
                new GsDropdownItem(targets.Count > 1 ? $"복제 ({targets.Count}개)" : "복제", () => Duplicate(targets)) { Caption = "Ctrl+D" },
                new GsDropdownItem("노드에 이어진 선 모두 지우기", () =>
                {
                    Record("선 지우기");
                    Graph.edges.RemoveAll(e => e.fromNode == guid || e.toNode == guid);
                    Touched(false);
                    RefreshDots();
                }),
                new GsDropdownItem(targets.Count > 1 ? $"지우기 ({targets.Count}개)" : "지우기", () =>
                {
                    _selected.Clear();
                    foreach (var id in targets) _selected.Add(id);
                    _selectedEdge = null;
                    DeleteSelection();
                }) { Caption = "Delete" },
            }, false);
        }

        void ShowMenu(Vector2 local, List<GsDropdownItem> items, bool searchable)
        {
            var anchor = new VisualElement { pickingMode = PickingMode.Ignore };
            anchor.style.position = Position.Absolute;
            anchor.style.left = local.x;
            anchor.style.top = local.y;
            anchor.style.width = 1;
            anchor.style.height = 1;
            Add(anchor);
            var dropdown = GsDropdown.Show(anchor, items, searchable, "노드 검색");
            if (dropdown == null)
            {
                anchor.RemoveFromHierarchy();
                return;
            }
            dropdown.Closed += () => anchor.RemoveFromHierarchy();
        }

        void ToggleCollapsed(string guid)
        {
            if (!Collapsed.Remove(guid)) Collapsed.Add(guid);
            if (_views.TryGetValue(guid, out var view) && view is NodeView nodeView) nodeView.BuildBody();
            RepaintEdges();
        }

        void RefreshDots()
        {
            var linked = new HashSet<(string, string)>();
            foreach (var edge in Graph.edges)
            {
                linked.Add((edge.fromNode, edge.fromPort));
                linked.Add((edge.toNode, edge.toPort));
            }
            foreach (var pair in _dots) pair.Value.EnableInClassList("gs-graph-port__dot--linked", linked.Contains(pair.Key));
        }

        // ── 노드 화면 ─────────────────────────────────────────

        sealed class PortTag
        {
            public string Node;
            public string Port;
            public PortSide Side;
            public PortKind Kind;
        }

        // 캔버스 위의 항목(노드·그룹 상자) 공통.
        abstract class ItemView : VisualElement
        {
            public readonly string Guid;

            protected ItemView(string guid)
            {
                Guid = guid;
            }

            public void Place(Vector2 position)
            {
                style.left = position.x;
                style.top = position.y;
            }

            public virtual void SetSelected(bool selected) => EnableInClassList("gs-graph-node--selected", selected);
        }

        // 그룹 상자: 노드 뒤에 깔리는 제목 달린 영역. 머리를 끌면 안에 든 노드도 함께 움직이고, 오른쪽 아래를 끌면 크기가 바뀐다.
        sealed class GroupView : ItemView
        {
            readonly GsGraphCanvas _canvas;
            readonly Label _title;

            public GroupView(GsGraphCanvas canvas, string guid) : base(guid)
            {
                _canvas = canvas;
                AddToClassList("gs-graph-group");
                var group = (GroupNode)canvas.Graph.Find(guid);
                var head = Gs.Row("gs-graph-group__head");
                _title = Gs.Label(group.title, "gs-graph-group__title");
                head.Add(_title);
                head.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.button == 1)
                    {
                        Menu(canvas.WorldToLocal(e.position));
                        e.StopPropagation();
                        return;
                    }
                    if (e.button != 0) return;
                    canvas.SelectNode(Guid, e.shiftKey);
                    canvas.BeginDrag(DragMode.Move, e.pointerId);
                    e.StopPropagation();
                });
                Add(head);
                var handle = new VisualElement();
                handle.AddToClassList("gs-graph-group__resize");
                handle.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.button != 0) return;
                    canvas._resizing = Guid;
                    canvas.BeginDrag(DragMode.Resize, e.pointerId);
                    e.StopPropagation();
                });
                Add(handle);
                Place(group.position);
                Resize(group.size);
                Paint(group.color);
            }

            public void Resize(Vector2 size)
            {
                style.width = size.x;
                style.height = size.y;
            }

            void Paint(Color color)
            {
                style.backgroundColor = Gs.WithAlpha(color, 0.07f);
                style.borderTopColor = style.borderBottomColor = style.borderLeftColor = style.borderRightColor = Gs.WithAlpha(color, 0.45f);
                _title.style.color = color;
            }

            void Menu(Vector2 local)
            {
                var items = new List<GsDropdownItem>
                {
                    new GsDropdownItem("제목 바꾸기", () =>
                    {
                        var group = (GroupNode)_canvas.Graph.Find(Guid);
                        GsDialog.Form(_canvas, "그룹 상자 제목", null, new List<DialogField> { new DialogField { Key = "title", Label = "title", Value = group.title } }, "바꾸기",
                            values => string.IsNullOrWhiteSpace(values["title"]) ? "제목을 적으세요." : null,
                            values =>
                            {
                                _canvas.Record("그룹 상자 제목");
                                ((GroupNode)_canvas.Graph.Find(Guid)).title = values["title"].Trim();
                                _canvas.Touched(true);
                            });
                    }),
                };
                for (var i = 0; i < Gs.Palette.Length; i++)
                {
                    var color = Gs.Palette[i];
                    items.Add(new GsDropdownItem(Gs.PaletteNames[i], () =>
                    {
                        _canvas.Record("그룹 상자 색");
                        ((GroupNode)_canvas.Graph.Find(Guid)).color = color;
                        Paint(color);
                        _canvas.Touched(false);
                    }) { Group = "색" });
                }
                items.Add(new GsDropdownItem("상자만 지우기(안의 노드는 남긴다)", () => _canvas.DeleteNode(Guid)));
                _canvas.ShowMenu(local, items, false);
            }
        }

        sealed class NodeView : ItemView
        {
            readonly GsGraphCanvas _canvas;
            readonly SerializedProperty _property;
            readonly Label _summary;
            readonly VisualElement _ports;
            readonly VisualElement _body;
            string _portSignature;

            public NodeView(GsGraphCanvas canvas, string guid, SerializedProperty property) : base(guid)
            {
                _canvas = canvas;
                _property = property;
                AddToClassList("gs-graph-node");
                EnableInClassList("gs-graph-node--note", canvas.Graph.Find(guid) is NoteNode);
                var node = Node;
                var info = node.GetType().GetCustomAttribute<GraphNodeAttribute>(false);
                var category = info?.Category ?? "";
                var accent = category == "흐름" ? new Color(0.95f, 0.95f, 0.95f) : Gs.PaletteFor(category);

                var header = Gs.Row("gs-graph-node__header");
                header.style.borderTopColor = accent;
                var dot = new VisualElement();
                dot.AddToClassList("gs-graph-node__mark");
                dot.style.backgroundColor = accent;
                header.Add(dot);
                header.Add(Gs.Label(info?.Name ?? node.GetType().Name, "gs-graph-node__title"));
                _summary = Gs.Label("", "gs-graph-node__summary", "gs-nowrap");
                header.Add(_summary);
                header.Add(Gs.Spacer());
                var close = Gs.Label("✕", "gs-graph-node__close");
                close.AddManipulator(new Clickable(() => canvas.DeleteNode(Guid)));
                header.Add(close);
                header.RegisterCallback<PointerDownEvent>(OnHeaderDown);
                Add(header);

                _ports = Gs.Col("gs-graph-node__ports");
                Add(_ports);
                _body = Gs.Col("gs-graph-node__body");
                Add(_body);
                // 노드 안을 누르면 선택(칸 편집은 그대로).
                RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.button == 0) _canvas.SelectNode(Guid, e.shiftKey);
                    if (e.button == 1)
                    {
                        _canvas.NodeMenu(Guid, _canvas.WorldToLocal(e.position));
                        e.StopPropagation();
                    }
                });
                RegisterCallback<GeometryChangedEvent>(_ => _canvas.RepaintEdges());

                Place(node.position);
                BuildPorts();
                BuildBody();
                RefreshSummary();
            }

            GraphNode Node => _canvas.Graph.Find(Guid);

            void OnHeaderDown(PointerDownEvent e)
            {
                if (e.button != 0 || (e.target as VisualElement)?.ClassListContains("gs-graph-node__close") == true) return;
                _canvas.SelectNode(Guid, e.shiftKey);
                _canvas.BeginDrag(DragMode.Move, e.pointerId);
                e.StopPropagation();
            }

            string Signature() => string.Join("|", Node.Ports.Select(p => $"{p.Id}:{p.Label}"));

            public void BuildPorts()
            {
                var node = Node;
                _portSignature = Signature();
                foreach (var key in _canvas._dots.Keys.Where(k => k.node == Guid).ToList()) _canvas._dots.Remove(key);
                _ports.Clear();
                var ins = node.Ports.Where(p => p.Side == PortSide.In).ToList();
                var outs = node.Ports.Where(p => p.Side == PortSide.Out).ToList();
                for (var i = 0; i < Mathf.Max(ins.Count, outs.Count); i++)
                {
                    var row = Gs.Row("gs-graph-port-row");
                    row.Add(i < ins.Count ? Port(ins[i]) : new VisualElement());
                    row.Add(Gs.Spacer());
                    row.Add(i < outs.Count ? Port(outs[i]) : new VisualElement());
                    _ports.Add(row);
                }
                _canvas.RefreshDots();
            }

            VisualElement Port(PortInfo port)
            {
                var element = Gs.Row("gs-graph-port", port.Side == PortSide.In ? "gs-graph-port--in" : "gs-graph-port--out");
                var dot = new VisualElement();
                dot.AddToClassList("gs-graph-port__dot");
                dot.AddToClassList(port.Kind == PortKind.Flow ? "gs-graph-port__dot--flow" : "gs-graph-port__dot--signal");
                var tag = new PortTag { Node = Guid, Port = port.Id, Side = port.Side, Kind = port.Kind };
                dot.userData = tag;
                element.userData = tag;
                dot.tooltip = port.Kind == PortKind.Flow ? "흐름" : "신호";
                var label = Gs.Label(port.Label, "gs-nowrap");
                if (port.Side == PortSide.In)
                {
                    element.Add(dot);
                    element.Add(label);
                }
                else
                {
                    element.Add(label);
                    element.Add(dot);
                }
                element.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.button != 0) return;
                    _canvas._from = (tag.Node, tag.Port, tag.Side, tag.Kind);
                    _canvas._connectEnd = _canvas.ToGraph(_canvas.WorldToLocal(e.position));
                    _canvas.BeginDrag(DragMode.Connect, e.pointerId);
                    e.StopPropagation();
                });
                _canvas._dots[(Guid, port.Id)] = dot;
                return element;
            }

            public void BuildBody()
            {
                _body.Clear();
                var collapsed = _canvas.Collapsed.Contains(Guid);
                _body.style.display = collapsed ? DisplayStyle.None : DisplayStyle.Flex;
                if (collapsed) return;
                foreach (var child in PropertyUtil.Children(_property))
                    _body.Add(GsPropertyEditor.BuildProperty(child, OnValue, OnStructure));
            }

            void OnValue()
            {
                _canvas._so.ApplyModifiedProperties();
                RefreshSummary();
                if (Signature() != _portSignature)
                {
                    if (_canvas.Graph.Prune() > 0) EditorUtility.SetDirty(_canvas._owner);
                    BuildPorts();
                }
                _canvas.Touched(false);
            }

            void OnStructure()
            {
                _canvas._so.ApplyModifiedProperties();
                _canvas._so.Update();
                RefreshSummary();
                if (Signature() != _portSignature)
                {
                    _canvas.Graph.Prune();
                    BuildPorts();
                }
                BuildBody();
                _canvas.Touched(false);
            }

            void RefreshSummary()
            {
                var summary = Node?.Summary;
                _summary.text = string.IsNullOrEmpty(summary) ? "" : summary;
            }
        }
    }
}
