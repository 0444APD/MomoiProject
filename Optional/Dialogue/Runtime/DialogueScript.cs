using System;
using System.Collections.Generic;
using System.Linq;
using GBible.Data;

namespace GBible.Dialogue
{
    // 다이얼로그 그래프를 '대본'으로 펼친다: 재생 순서대로(분기·선택지는 차례로 모두) 대사·선택지 한 줄씩, 그때의 장소·등장 캐릭터와 함께.
    // 시트 내보내기·검사에 쓴다. 시간은 보지 않고 효과의 끝 모습만 적용한다.
    public static class DialogueScript
    {
        public sealed class Row
        {
            public DialogueData Dialogue;
            public LineNode Line;
            // 선택지 줄이면 그 선택지(대사 줄이면 null).
            public ChoiceOption Option;
            public string Location;
            public IReadOnlyList<string> Present = Array.Empty<string>();

            public bool IsChoice => Option != null;
            public string Key => IsChoice ? Option.lineId : Line.lineId;
            public string Source => IsChoice ? Option.text : Line.text;
        }

        public static List<Row> Walk(DialogueData dialogue, Func<string, EffectPresetData> presets, Func<string, LocationData> locations)
        {
            var rows = new List<Row>();
            if (dialogue == null) return rows;
            var graph = dialogue.graph;
            var visited = new HashSet<string>();
            var context = new EffectContext { Locations = locations ?? (_ => null) };
            presets ??= _ => null;

            foreach (var start in graph.OfType<StartNode>().OrderBy(s => s.position.y).ThenBy(s => s.position.x))
            {
                var state = new StageState();
                Apply(graph, start, StartNode.StartedPort, state, context, presets, new HashSet<string>());
                Follow(graph.Next(start, StartNode.NextPort), state);
            }
            return rows;

            void Follow(GraphNode node, StageState state)
            {
                for (var guard = 0; node != null && guard < 10000; guard++)
                {
                    if (!visited.Add(node.guid)) return;
                    switch (node)
                    {
                        case LineNode line:
                            var lineContext = context.With(line.speaker);
                            if (!string.IsNullOrEmpty(line.expression) || line.expressionSprite != null)
                                new ExpressionEffect { character = line.speaker, expression = line.expression, sprite = line.expressionSprite }.ApplyTo(state, lineContext);
                            Apply(graph, line, LineNode.EnterPort, state, lineContext, presets, new HashSet<string>());
                            rows.Add(new Row { Dialogue = dialogue, Line = line, Location = state.location, Present = state.standings.Select(s => s.character).ToList() });
                            Apply(graph, line, LineNode.TypedPort, state, lineContext, presets, new HashSet<string>());
                            foreach (var option in line.options)
                                rows.Add(new Row { Dialogue = dialogue, Line = line, Option = option, Location = state.location, Present = state.standings.Select(s => s.character).ToList() });
                            Apply(graph, line, LineNode.LeavePort, state, lineContext, presets, new HashSet<string>());
                            if (line.options.Count == 0)
                            {
                                node = graph.Next(line, LineNode.NextPort);
                                continue;
                            }
                            foreach (var option in line.options)
                                Follow(graph.Next(line, LineNode.OptionPrefix + option.id), state.Clone());
                            return;
                        case BranchNode branch:
                            Follow(graph.Next(branch, BranchNode.TruePort), state.Clone());
                            node = graph.Next(branch, BranchNode.FalsePort);
                            continue;
                        case SetNode set:
                            node = graph.Next(set, SetNode.NextPort);
                            continue;
                        case SignalNode signal:
                            node = graph.Next(signal, SignalNode.NextPort);
                            continue;
                        case CustomFlowNode custom:
                            // 실행하지 않고 나가는 흐름 점을 모두 따라간다.
                            var outs = custom.Ports.Where(p => p.Kind == PortKind.Flow && p.Side == PortSide.Out).ToList();
                            foreach (var port in outs.Skip(1)) Follow(graph.Next(custom, port.Id), state.Clone());
                            node = outs.Count > 0 ? graph.Next(custom, outs[0].Id) : null;
                            continue;
                        default:
                            // 끝·다른 다이얼로그로 이동.
                            return;
                    }
                }
            }
        }

        // 신호 체인을 따라가며 상태 효과의 끝 모습을 적용한다(프리셋 안쪽까지).
        static void Apply(NodeGraph graph, GraphNode from, string port, StageState state, EffectContext context,
            Func<string, EffectPresetData> presets, HashSet<string> seen)
        {
            foreach (var edge in graph.From(from.guid, port).ToList())
            {
                if (!(graph.Find(edge.toNode) is EffectNode effect) || edge.toPort != EffectNode.PlayPort) continue;
                if (!seen.Add(effect.guid)) continue;
                if (effect is PresetEffect presetEffect)
                {
                    var preset = presets(presetEffect.preset);
                    if (preset != null)
                    {
                        var inner = context.With(context.Character(presetEffect.character));
                        foreach (var entry in preset.graph.OfType<PresetEntryNode>())
                            Apply(preset.graph, entry, PresetEntryNode.StartPort, state, inner, presets, new HashSet<string>());
                    }
                }
                else if (effect.IsState) effect.ApplyTo(state, context);
                foreach (var out_ in effect.Ports.Where(p => p.Kind == PortKind.Signal && p.Side == PortSide.Out))
                    Apply(graph, effect, out_.Id, state, context, presets, seen);
            }
        }
    }
}
