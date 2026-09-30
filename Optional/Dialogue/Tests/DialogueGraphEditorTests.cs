using System.Linq;
using GameSystems.Data;
using GameSystems.Dialogue;
using GameSystems.EditorTools.Graph;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameSystems.Tests
{
    public sealed class DialogueGraphEditorTests
    {
        [Test]
        public void 그래프마다_쓸_수_있는_노드만_메뉴에_나온다()
        {
            var dialogue = GsGraphCanvas.NodeTypes(GraphKinds.Dialogue).Select(x => x.type).ToList();
            var preset = GsGraphCanvas.NodeTypes(GraphKinds.Preset).Select(x => x.type).ToList();
            CollectionAssert.Contains(dialogue, typeof(LineNode));
            CollectionAssert.Contains(dialogue, typeof(ShakeEffect));
            CollectionAssert.Contains(dialogue, typeof(PresetEffect));
            CollectionAssert.DoesNotContain(dialogue, typeof(PresetEntryNode));
            CollectionAssert.Contains(preset, typeof(PresetEntryNode));
            CollectionAssert.Contains(preset, typeof(BackgroundEffect));
            CollectionAssert.DoesNotContain(preset, typeof(LineNode));
            Assert.AreEqual(typeof(StartNode), dialogue[0], "흐름 분류가 맨 앞, 시작이 첫 항목");
        }

        [Test]
        public void 모든_노드_종류를_그려도_예외가_없다()
        {
            var data = ScriptableObject.CreateInstance<DialogueData>();
            try
            {
                var x = 0f;
                foreach (var (type, _) in GsGraphCanvas.NodeTypes(GraphKinds.Both))
                {
                    var node = (GraphNode)System.Activator.CreateInstance(type);
                    node.position = new Vector2(x += 320, 0);
                    data.graph.Add(node);
                }
                var line = data.graph.OfType<LineNode>().First();
                line.options.Add(new ChoiceOption { text = "예" });
                var start = data.graph.OfType<StartNode>().First();
                data.graph.Connect(start.guid, StartNode.NextPort, line.guid, GraphNode.FlowInPort);

                GsGraphCanvas canvas = null;
                Assert.DoesNotThrow(() => canvas = new GsGraphCanvas(data, () => data.graph, "graph", GraphKinds.Both));
                Assert.AreEqual(data.graph.nodes.Count(n => !(n is GroupNode)), canvas.Query(className: "gs-graph-node").ToList().Count);
                Assert.AreEqual(1, canvas.Query(className: "gs-graph-group").ToList().Count, "묶음 상자는 따로 그린다");
                Assert.AreEqual(data.graph.nodes.Sum(n => n.Ports.Count()), canvas.Query(className: "gs-graph-port__dot").ToList().Count, "점마다 하나씩");
                Assert.DoesNotThrow(() => canvas.FrameAll());
            }
            finally
            {
                Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void 복사_붙여넣기는_선과_함께_새_ID로_들어가고_못_쓰는_노드는_건너뛴다()
        {
            var dialogue = ScriptableObject.CreateInstance<DialogueData>();
            var preset = ScriptableObject.CreateInstance<EffectPresetData>();
            try
            {
                var start = new StartNode();
                var line = new LineNode { text = "안녕" };
                var shake = new ShakeEffect();
                foreach (var node in new GraphNode[] { start, line, shake }) dialogue.graph.Add(node);
                dialogue.graph.Connect(start.guid, StartNode.NextPort, line.guid, GraphNode.FlowInPort);
                dialogue.graph.Connect(line.guid, LineNode.EnterPort, shake.guid, EffectNode.PlayPort);

                var canvas = new GsGraphCanvas(dialogue, () => dialogue.graph, "graph", GraphKinds.Dialogue);
                canvas.SelectOnly(new[] { start.guid, line.guid, shake.guid });
                canvas.CopySelection();
                canvas.PasteAt(new Vector2(1000, 0));
                Assert.AreEqual(6, dialogue.graph.nodes.Count);
                Assert.AreEqual(4, dialogue.graph.edges.Count, "복사한 노드 사이의 선도 함께");
                var copy = dialogue.graph.OfType<LineNode>().Single(l => l.guid != line.guid);
                Assert.AreNotEqual(line.lineId, copy.lineId, "번역 키는 새로");
                Assert.GreaterOrEqual(copy.position.x, 1000f);
                CollectionAssert.AreEquivalent(dialogue.graph.nodes.Skip(3).Select(n => n.guid), canvas.Selection, "붙여 넣은 것을 고른다");

                canvas.DuplicateSelection();
                Assert.AreEqual(9, dialogue.graph.nodes.Count);

                var presetCanvas = new GsGraphCanvas(preset, () => preset.graph, "graph", GraphKinds.Preset);
                presetCanvas.PasteAt(Vector2.zero);
                Assert.AreEqual(1, preset.graph.nodes.Count, "프리셋에는 효과만 들어간다(시작·대사는 건너뜀)");
                Assert.IsInstanceOf<ShakeEffect>(preset.graph.nodes[0]);
            }
            finally
            {
                Object.DestroyImmediate(dialogue);
                Object.DestroyImmediate(preset);
            }
        }

        [Test]
        public void 노드_복사는_안쪽_블록까지_새_인스턴스다()
        {
            var branch = new BranchNode { condition = new StatCompareCondition { stat = "kills", value = 3 } };
            var copy = (BranchNode)NodeClipboard.Clone(branch);
            Assert.AreNotSame(branch, copy);
            Assert.IsNotNull(copy.condition);
            Assert.AreNotSame(branch.condition, copy.condition);
            Assert.AreEqual(3, ((StatCompareCondition)copy.condition).value);
        }
    }
}
