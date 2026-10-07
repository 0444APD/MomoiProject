using System;
using System.Collections.Generic;
using System.Linq;

namespace GBible.Dialogue
{
    // 그래프 검사. 그래프 창의 경고와 실행기가 함께 쓴다.
    public static class DialogueChecks
    {
        // 신호가 고리로 이어진 효과 노드(guid). 예: A 완료 → B 재생, B 완료 → A 재생 — 끝없이 반복된다.
        // 실행기는 고리를 한 바퀴 돌아 다시 시작한 효과로는 클릭을 막지 않고, 빨리 감기·넘김에서 한 번만 끝낸다.
        public static HashSet<string> LoopingEffects(NodeGraph graph)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (graph == null) return result;
            var next = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var edge in graph.edges)
            {
                if (edge == null || edge.toPort != EffectNode.PlayPort) continue;
                if (!(graph.Find(edge.fromNode) is EffectNode) || !(graph.Find(edge.toNode) is EffectNode)) continue;
                if (!next.TryGetValue(edge.fromNode, out var list)) next[edge.fromNode] = list = new List<string>();
                list.Add(edge.toNode);
            }
            if (next.Count == 0) return result;

            // 강하게 이어진 묶음(Tarjan): 둘 이상이 서로 닿으면 고리.
            var order = 0;
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            var low = new Dictionary<string, int>(StringComparer.Ordinal);
            var stack = new Stack<string>();
            var onStack = new HashSet<string>(StringComparer.Ordinal);

            void Visit(string node)
            {
                index[node] = low[node] = order++;
                stack.Push(node);
                onStack.Add(node);
                if (next.TryGetValue(node, out var outs))
                    foreach (var to in outs)
                    {
                        if (!index.ContainsKey(to))
                        {
                            Visit(to);
                            low[node] = Math.Min(low[node], low[to]);
                        }
                        else if (onStack.Contains(to)) low[node] = Math.Min(low[node], index[to]);
                    }
                if (low[node] != index[node]) return;
                var component = new List<string>();
                string member;
                do
                {
                    member = stack.Pop();
                    onStack.Remove(member);
                    component.Add(member);
                } while (member != node);
                if (component.Count > 1 || (next.TryGetValue(node, out var self) && self.Contains(node))) result.UnionWith(component);
            }

            foreach (var node in next.Keys.ToList())
                if (!index.ContainsKey(node)) Visit(node);
            return result;
        }

        // 선택지가 모두 '보일 조건'을 가진 대사 중 '고를 선택지 없음' 점이 이어지지 않은 것: 조건이 모두 안 맞으면 다이얼로그가 끝난다.
        public static List<LineNode> ChoicesWithoutFallback(NodeGraph graph) =>
            graph == null
                ? new List<LineNode>()
                : graph.OfType<LineNode>().Where(l => l.options.Count > 0 && l.options.All(o => o.condition != null) && graph.Next(l, LineNode.NoChoicePort) == null).ToList();
    }
}
