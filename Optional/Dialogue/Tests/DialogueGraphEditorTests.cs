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
                Assert.AreEqual(data.graph.nodes.Count, canvas.Query(className: "gs-graph-node").ToList().Count);
                Assert.AreEqual(data.graph.nodes.Sum(n => n.Ports.Count()), canvas.Query(className: "gs-graph-port__dot").ToList().Count, "점마다 하나씩");
                Assert.DoesNotThrow(() => canvas.FrameAll());
            }
            finally
            {
                Object.DestroyImmediate(data);
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
