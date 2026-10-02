using System.Collections.Generic;
using System.Linq;
using GBible.Dialogue;
using UnityEngine;

namespace GBible.EditorTools.Graph
{
    // 그래프 노드 복사·붙여넣기(창·다이얼로그·프리셋 사이에서도). 복사할 때 깊은 사본을 떠 두므로 원본을 고쳐도 영향이 없다.
    static class GraphClipboard
    {
        public static List<GraphNode> Nodes { get; private set; } = new List<GraphNode>();
        public static List<GraphEdge> Edges { get; private set; } = new List<GraphEdge>();
        // 복사한 노드들의 왼쪽 위(붙여넣을 때 이 점이 마우스 위치에 온다).
        public static Vector2 Origin { get; private set; }

        public static void Copy(NodeGraph graph, IEnumerable<string> guids)
        {
            var nodes = guids.Select(graph.Find).Where(n => n != null).ToList();
            if (nodes.Count == 0) return;
            var ids = new HashSet<string>(nodes.Select(n => n.guid));
            Nodes = nodes.Select(NodeClipboard.Clone).ToList();
            Edges = graph.edges.Where(e => ids.Contains(e.fromNode) && ids.Contains(e.toNode))
                .Select(e => new GraphEdge(e.fromNode, e.fromPort, e.toNode, e.toPort)).ToList();
            Origin = new Vector2(nodes.Min(n => n.position.x), nodes.Min(n => n.position.y));
        }
    }
}
