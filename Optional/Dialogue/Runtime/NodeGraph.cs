using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GBible.Dialogue
{
    // 선 종류. 흐름(다이얼로그가 어디로 가는가)과 신호(연출이 언제 시작하는가)는 서로 잇지 않는다.
    public enum PortKind
    {
        Flow,
        Signal,
    }

    public enum PortSide
    {
        In,
        Out,
    }

    public readonly struct PortInfo
    {
        public readonly string Id;
        public readonly string Label;
        public readonly PortKind Kind;
        public readonly PortSide Side;

        public PortInfo(string id, string label, PortKind kind, PortSide side)
        {
            Id = id;
            Label = label;
            Kind = kind;
            Side = side;
        }

        public static PortInfo FlowIn(string label = "") => new PortInfo(GraphNode.FlowInPort, label, PortKind.Flow, PortSide.In);
        public static PortInfo FlowOut(string id, string label) => new PortInfo(id, label, PortKind.Flow, PortSide.Out);
        public static PortInfo SignalIn(string id, string label) => new PortInfo(id, label, PortKind.Signal, PortSide.In);
        public static PortInfo SignalOut(string id, string label) => new PortInfo(id, label, PortKind.Signal, PortSide.Out);
    }

    // 어느 그래프에서 쓰는 노드인지(우클릭 메뉴에 나오는 곳).
    [Flags]
    public enum GraphKinds
    {
        Dialogue = 1,
        Preset = 2,
        Both = Dialogue | Preset,
    }

    // 노드 메뉴 정보. 우클릭 메뉴에 "분류 › 이름"으로 나온다.
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class GraphNodeAttribute : Attribute
    {
        public string Name { get; }
        public string Category { get; set; } = "";
        public GraphKinds Graphs { get; set; } = GraphKinds.Dialogue;
        public int Order { get; set; }

        public GraphNodeAttribute(string name)
        {
            Name = name;
        }
    }

    [Serializable]
    public abstract class GraphNode
    {
        public const string FlowInPort = "in";

        [HideInInspector] public string guid = NewId();
        [HideInInspector] public Vector2 position;

        public abstract IEnumerable<PortInfo> Ports { get; }

        // 노드 머리글에 덧붙는 짧은 요약(없으면 null).
        public virtual string Summary => null;

        public static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 12);

        public PortInfo? Port(string id)
        {
            foreach (var port in Ports)
                if (port.Id == id)
                    return port;
            return null;
        }
    }

    [Serializable]
    public sealed class GraphEdge
    {
        public string fromNode;
        public string fromPort;
        public string toNode;
        public string toPort;

        public GraphEdge()
        {
        }

        public GraphEdge(string fromNode, string fromPort, string toNode, string toPort)
        {
            this.fromNode = fromNode;
            this.fromPort = fromPort;
            this.toNode = toNode;
            this.toPort = toPort;
        }

        public bool Same(GraphEdge other) =>
            other != null && fromNode == other.fromNode && fromPort == other.fromPort && toNode == other.toNode && toPort == other.toPort;
    }

    // 노드와 선. 다이얼로그와 연출 프리셋이 같은 구조를 쓴다.
    [Serializable]
    public sealed class NodeGraph
    {
        [SerializeReference] public List<GraphNode> nodes = new List<GraphNode>();
        public List<GraphEdge> edges = new List<GraphEdge>();

        // guid → 목록 위치. 편집기가 목록을 다시 읽어 노드 인스턴스가 바뀌어도 항상 현재 목록에서 꺼낸다.
        [NonSerialized] Dictionary<string, int> _index;

        public GraphNode Find(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return null;
            if (_index != null && _index.TryGetValue(guid, out var at) && at < nodes.Count && nodes[at]?.guid == guid) return nodes[at];
            Reindex();
            return _index.TryGetValue(guid, out at) ? nodes[at] : null;
        }

        void Reindex()
        {
            _index = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < nodes.Count; i++)
                if (nodes[i] != null && !string.IsNullOrEmpty(nodes[i].guid))
                    _index[nodes[i].guid] = i;
        }

        public IEnumerable<T> OfType<T>() where T : GraphNode => nodes.OfType<T>();

        public IEnumerable<GraphEdge> From(string node, string port) => edges.Where(e => e.fromNode == node && e.fromPort == port);
        public IEnumerable<GraphEdge> To(string node, string port) => edges.Where(e => e.toNode == node && e.toPort == port);

        public GraphNode Next(GraphNode from, string port)
        {
            var edge = edges.FirstOrDefault(e => e.fromNode == from.guid && e.fromPort == port);
            return edge == null ? null : Find(edge.toNode);
        }

        // 두 포트를 이을 수 있는지(종류가 같고, 나가는 쪽 → 들어오는 쪽, 자기 자신 아님).
        public bool CanConnect(string fromNode, string fromPort, string toNode, string toPort, out string reason)
        {
            reason = null;
            var a = Find(fromNode)?.Port(fromPort);
            var b = Find(toNode)?.Port(toPort);
            if (a == null || b == null) reason = "포트를 찾을 수 없습니다.";
            else if (fromNode == toNode) reason = "같은 노드끼리는 이을 수 없습니다.";
            else if (a.Value.Side != PortSide.Out || b.Value.Side != PortSide.In) reason = "나가는 점에서 들어오는 점으로 잇습니다.";
            else if (a.Value.Kind != b.Value.Kind) reason = a.Value.Kind == PortKind.Flow ? "흐름 점은 흐름 점과만 잇습니다." : "신호 점은 신호 점과만 잇습니다.";
            else if (edges.Any(e => e.fromNode == fromNode && e.fromPort == fromPort && e.toNode == toNode && e.toPort == toPort)) reason = "이미 이어져 있습니다.";
            return reason == null;
        }

        // 흐름 출력은 한 곳으로만 간다(새로 이으면 이전 선을 뗀다). 신호 출력은 여러 곳으로 갈 수 있다.
        public GraphEdge Connect(string fromNode, string fromPort, string toNode, string toPort)
        {
            if (!CanConnect(fromNode, fromPort, toNode, toPort, out _)) return null;
            if (Find(fromNode).Port(fromPort)?.Kind == PortKind.Flow)
                edges.RemoveAll(e => e.fromNode == fromNode && e.fromPort == fromPort);
            var edge = new GraphEdge(fromNode, fromPort, toNode, toPort);
            edges.Add(edge);
            return edge;
        }

        public void Remove(GraphNode node)
        {
            nodes.Remove(node);
            edges.RemoveAll(e => e.fromNode == node.guid || e.toNode == node.guid);
            _index = null;
        }

        public void Add(GraphNode node)
        {
            if (string.IsNullOrEmpty(node.guid) || Find(node.guid) != null) node.guid = GraphNode.NewId();
            nodes.Add(node);
            _index = null;
        }

        // 없어진 노드·포트를 가리키는 선을 지운다(선택지·진행 지점을 지운 뒤 등). 지운 수를 돌려준다.
        public int Prune()
        {
            _index = null;
            return edges.RemoveAll(e =>
            {
                var from = Find(e.fromNode);
                var to = Find(e.toNode);
                return from == null || to == null || from.Port(e.fromPort) == null || to.Port(e.toPort) == null;
            });
        }
    }
}
