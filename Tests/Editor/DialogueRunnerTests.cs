using System.Collections.Generic;
using System.Linq;
using GameSystems.Data;
using GameSystems.Dialogue;
using NUnit.Framework;
using UnityEngine;

namespace GameSystems.Tests
{
    public sealed class DialogueRunnerTests
    {
        sealed class RecordingStage : IDialogueStage
        {
            public readonly List<string> Log = new List<string>();
            public string Line;
            public int Visible;
            public IReadOnlyList<DialogueChoice> Choices = new List<DialogueChoice>();

            public void ApplyState(StageState state) => Log.Add("apply");

            public IEffectPlayer Begin(EffectNode effect, EffectContext context, StageState before, StageState after)
            {
                Log.Add("begin " + effect.GetType().Name + (effect is StandingEnterEffect ? " " + context.Character(((StandingEnterEffect)effect).character) : ""));
                return null;
            }

            public void ShowLine(string speaker, string speakerName, string text) => Line = text;
            public void SetVisibleCharacters(int count) => Visible = count;
            public void ShowChoices(IReadOnlyList<DialogueChoice> choices) => Choices = choices.ToList();
            public void Hide() => Log.Add("hide");
        }

        readonly List<Object> _made = new List<Object>();
        readonly Dictionary<string, DialogueData> _dialogues = new Dictionary<string, DialogueData>();
        readonly Dictionary<string, EffectPresetData> _presets = new Dictionary<string, EffectPresetData>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _made) Object.DestroyImmediate(o);
            _made.Clear();
            _dialogues.Clear();
            _presets.Clear();
        }

        DialogueData Dialogue(string id)
        {
            var data = ScriptableObject.CreateInstance<DialogueData>();
            data.EditorAssignId(id);
            _made.Add(data);
            _dialogues[id] = data;
            return data;
        }

        EffectPresetData Preset(string id)
        {
            var data = ScriptableObject.CreateInstance<EffectPresetData>();
            data.EditorAssignId(id);
            _made.Add(data);
            _presets[id] = data;
            return data;
        }

        static T Add<T>(NodeGraph graph, T node) where T : GraphNode
        {
            graph.Add(node);
            return node;
        }

        static void Link(NodeGraph graph, GraphNode from, string fromPort, GraphNode to, string toPort) =>
            Assert.IsNotNull(graph.Connect(from.guid, fromPort, to.guid, toPort), $"{from.GetType().Name}.{fromPort} → {to.GetType().Name}.{toPort}");

        (DialogueRunner runner, RecordingStage stage) Runner()
        {
            var stage = new RecordingStage();
            var runner = new DialogueRunner(id => id != null && _dialogues.TryGetValue(id, out var d) ? d : null,
                id => id != null && _presets.TryGetValue(id, out var p) ? p : null) { Stage = stage, DefaultTypingSpeed = 10 };
            return (runner, stage);
        }

        [Test]
        public void 대사를_차례로_보이고_끝난다()
        {
            var d = Dialogue("intro");
            var g = d.graph;
            var start = Add(g, new StartNode());
            var a = Add(g, new LineNode { text = "안녕" });
            var b = Add(g, new LineNode { text = "잘 가" });
            Link(g, start, StartNode.NextPort, a, GraphNode.FlowInPort);
            Link(g, a, LineNode.NextPort, b, GraphNode.FlowInPort);

            var (runner, stage) = Runner();
            var ended = false;
            runner.Ended += () => ended = true;
            Assert.IsTrue(runner.Start(d));
            Assert.AreEqual(DialoguePhase.Typing, runner.Phase);
            Assert.AreEqual("안녕", stage.Line);
            runner.Tick(0.1f);
            Assert.AreEqual(1, stage.Visible, "초당 10글자 × 0.1초");
            runner.Advance();
            Assert.AreEqual(DialoguePhase.Shown, runner.Phase, "타이핑 중 클릭 = 다 보이기");
            runner.Advance();
            Assert.AreEqual(b, runner.Line);
            runner.FastForward();
            runner.Advance();
            Assert.IsTrue(ended);
            Assert.AreEqual(DialoguePhase.Ended, runner.Phase);
            Assert.AreEqual(2, runner.History.Count);
        }

        [Test]
        public void 효과의_진행_지점_신호가_대사를_시작시킨다()
        {
            var d = Dialogue("gate");
            var g = d.graph;
            var start = Add(g, new StartNode());
            var line = Add(g, new LineNode { text = "왔구나, 드디어 왔어" });
            var bg = Add(g, new BackgroundEffect { duration = 1f, marks = { new ProgressMark { percent = 90 } } });
            Link(g, start, StartNode.NextPort, line, GraphNode.FlowInPort);
            Link(g, line, LineNode.EnterPort, bg, EffectNode.PlayPort);
            Link(g, bg, EffectNode.MarkPrefix + bg.marks[0].id, line, LineNode.TextStartPort);

            var (runner, _) = Runner();
            runner.Start(d);
            Assert.AreEqual(DialoguePhase.WaitingForText, runner.Phase, "연출 중에는 대사를 막는다");
            runner.Tick(0.5f);
            Assert.AreEqual(DialoguePhase.WaitingForText, runner.Phase);
            runner.Tick(0.45f);
            Assert.AreEqual(DialoguePhase.Typing, runner.Phase, "90% 지점에서 풀린다");
            Assert.AreEqual(1, runner.RunningEffects, "배경은 계속 진행 중");
        }

        [Test]
        public void 완료_신호로_이은_효과가_기다림_뒤_시작한다()
        {
            var d = Dialogue("chain");
            var g = d.graph;
            var start = Add(g, new StartNode());
            var line = Add(g, new LineNode { text = "…" });
            var first = Add(g, new DimEffect { duration = 0.5f, amount = 0.7f });
            var second = Add(g, new ShakeEffect { duration = 0.2f, delay = 0.3f });
            Link(g, start, StartNode.NextPort, line, GraphNode.FlowInPort);
            Link(g, line, LineNode.EnterPort, first, EffectNode.PlayPort);
            Link(g, first, EffectNode.DonePort, second, EffectNode.PlayPort);

            var (runner, stage) = Runner();
            runner.Start(d);
            Assert.AreEqual(0.7f, runner.State.dim, 1e-4, "상태는 시작할 때 목표 값으로");
            runner.Tick(0.5f);
            Assert.IsFalse(stage.Log.Contains("begin ShakeEffect"), "완료 직후엔 기다림 중");
            runner.Tick(0.2f);
            Assert.IsFalse(stage.Log.Contains("begin ShakeEffect"));
            runner.Tick(0.15f);
            Assert.IsTrue(stage.Log.Contains("begin ShakeEffect"), "0.3초 기다린 뒤 시작");
            runner.Tick(0.2f);
            Assert.AreEqual(0, runner.RunningEffects);
        }

        [Test]
        public void 클릭하면_연출을_끝내고_대사를_시작한다()
        {
            var d = Dialogue("skip");
            var g = d.graph;
            var start = Add(g, new StartNode());
            var line = Add(g, new LineNode { text = "짠" });
            var bg = Add(g, new BackgroundEffect { duration = 3f });
            var dim = Add(g, new DimEffect { duration = 3f, amount = 0.4f });
            Link(g, start, StartNode.NextPort, line, GraphNode.FlowInPort);
            Link(g, line, LineNode.EnterPort, bg, EffectNode.PlayPort);
            Link(g, bg, EffectNode.DonePort, dim, EffectNode.PlayPort);
            Link(g, dim, EffectNode.DonePort, line, LineNode.TextStartPort);

            var (runner, _) = Runner();
            runner.Start(d);
            runner.Advance();
            Assert.AreEqual(DialoguePhase.Typing, runner.Phase, "체인 끝까지 마무리되어 대사 시작 신호가 온다");
            Assert.AreEqual(0.4f, runner.State.dim, 1e-4);
            Assert.AreEqual(0, runner.RunningEffects);
        }

        [Test]
        public void 다음_줄로_넘기면_이_줄의_효과를_마무리한다()
        {
            var d = Dialogue("leave");
            var g = d.graph;
            var start = Add(g, new StartNode());
            var a = Add(g, new LineNode { text = "a" });
            var b = Add(g, new LineNode { text = "b" });
            var shake = Add(g, new ShakeEffect { duration = 5f });
            var music = Add(g, new MusicEffect { duration = 5f, keepAcrossLines = true });
            Link(g, start, StartNode.NextPort, a, GraphNode.FlowInPort);
            Link(g, a, LineNode.NextPort, b, GraphNode.FlowInPort);
            Link(g, a, LineNode.EnterPort, shake, EffectNode.PlayPort);
            Link(g, a, LineNode.EnterPort, music, EffectNode.PlayPort);

            var (runner, _) = Runner();
            runner.Start(d);
            runner.FastForward();
            Assert.AreEqual(0, runner.RunningEffects, "빨리 감기는 모두 끝낸다");

            runner.Start(d);
            runner.Advance();
            runner.Advance();
            Assert.AreEqual(b, runner.Line);
            Assert.AreEqual(1, runner.RunningEffects, "계속 설정한 음악만 남는다");
        }

        [Test]
        public void 선택지와_조건_분기()
        {
            var d = Dialogue("shop");
            var g = d.graph;
            var start = Add(g, new StartNode());
            var ask = Add(g, new LineNode { text = "무엇을 살래?", options = { new ChoiceOption { text = "바나나" }, new ChoiceOption { text = "새우" } } });
            var banana = Add(g, new LineNode { text = "바나나다" });
            var signal = Add(g, new SignalNode { signal = "buyShrimp" });
            var branch = Add(g, new BranchNode());
            var shrimp = Add(g, new LineNode { text = "새우다" });
            Link(g, start, StartNode.NextPort, ask, GraphNode.FlowInPort);
            Link(g, ask, LineNode.OptionPrefix + ask.options[0].id, banana, GraphNode.FlowInPort);
            Link(g, ask, LineNode.OptionPrefix + ask.options[1].id, signal, GraphNode.FlowInPort);
            Link(g, signal, SignalNode.NextPort, branch, GraphNode.FlowInPort);
            Link(g, branch, BranchNode.TruePort, shrimp, GraphNode.FlowInPort);

            var (runner, stage) = Runner();
            string signaled = null;
            runner.Signaled += s => signaled = s;
            runner.Start(d);
            runner.Advance();
            Assert.AreEqual(DialoguePhase.Choosing, runner.Phase);
            Assert.AreEqual(2, stage.Choices.Count);
            Assert.IsFalse(runner.Choose(5));
            Assert.IsTrue(runner.Choose(1));
            Assert.AreEqual("buyShrimp", signaled);
            Assert.AreEqual(shrimp, runner.Line, "조건이 비면 참");
        }

        [Test]
        public void 프리셋은_노드_하나로_재생되고_신호를_내보낸다()
        {
            var preset = Preset("surprise");
            var pg = preset.graph;
            var entry = Add(pg, new PresetEntryNode());
            var enter = Add(pg, new StandingEnterEffect { duration = 0.2f });
            var shake = Add(pg, new ShakeEffect { duration = 0.3f, shakeCharacter = true });
            var output = Add(pg, new PresetOutputNode { signal = "hit" });
            Link(pg, entry, PresetEntryNode.StartPort, enter, EffectNode.PlayPort);
            Link(pg, enter, EffectNode.DonePort, shake, EffectNode.PlayPort);
            Link(pg, shake, EffectNode.StartedPort, output, PresetOutputNode.FirePort);
            PresetEffect.DefaultOutputsOf = id => _presets.TryGetValue(id ?? "", out var p) ? p.Outputs : Enumerable.Empty<string>();

            try
            {
                var d = Dialogue("usePreset");
                var g = d.graph;
                var start = Add(g, new StartNode());
                var line = Add(g, new LineNode { speaker = "arin", text = "헉! 깜짝이야" });
                var use = Add(g, new PresetEffect { preset = "surprise" });
                var after = Add(g, new DimEffect { duration = 0.1f, amount = 0.2f });
                Link(g, start, StartNode.NextPort, line, GraphNode.FlowInPort);
                Link(g, line, LineNode.EnterPort, use, EffectNode.PlayPort);
                Link(g, use, PresetEffect.OutputPrefix + "hit", line, LineNode.TextStartPort);
                Link(g, use, EffectNode.DonePort, after, EffectNode.PlayPort);

                var (runner, stage) = Runner();
                runner.Start(d);
                Assert.IsTrue(stage.Log.Contains("begin StandingEnterEffect arin"), "프리셋 안의 빈 캐릭터 = 현재 화자");
                Assert.AreEqual(DialoguePhase.WaitingForText, runner.Phase);
                runner.Tick(0.2f);
                Assert.AreEqual(DialoguePhase.Typing, runner.Phase, "프리셋이 내보낸 신호로 대사 시작");
                Assert.IsFalse(stage.Log.Contains("begin DimEffect"), "프리셋이 아직 안 끝남");
                runner.Tick(0.3f);
                Assert.IsTrue(stage.Log.Contains("begin DimEffect"), "안의 효과가 모두 끝나면 프리셋 완료");
                Assert.IsNotNull(runner.State.Standing("arin"));
            }
            finally
            {
                PresetEffect.DefaultOutputsOf = null;
            }
        }

        [Test]
        public void 저장한_줄에서_화면_상태와_함께_다시_시작한다()
        {
            var d = Dialogue("resume");
            var g = d.graph;
            var start = Add(g, new StartNode());
            var a = Add(g, new LineNode { text = "a" });
            var b = Add(g, new LineNode { text = "b" });
            var dim = Add(g, new DimEffect { amount = 0.8f, duration = 0 });
            Link(g, start, StartNode.NextPort, a, GraphNode.FlowInPort);
            Link(g, start, StartNode.StartedPort, dim, EffectNode.PlayPort);
            Link(g, a, LineNode.NextPort, b, GraphNode.FlowInPort);

            var (runner, _) = Runner();
            runner.Start(d);
            runner.FastForward();
            runner.Advance();
            var snapshot = runner.Snapshot();
            Assert.AreEqual(b.guid, snapshot.node);

            var (other, stage) = Runner();
            Assert.IsTrue(other.Resume(snapshot));
            Assert.AreEqual(b, other.Line);
            Assert.AreEqual(0.8f, other.State.dim, 1e-4);
            Assert.AreEqual("apply", stage.Log[0]);
        }

        [Test]
        public void 시간_0인_효과_고리는_멈추지_않고_끊긴다()
        {
            var d = Dialogue("loop");
            var g = d.graph;
            var start = Add(g, new StartNode());
            var line = Add(g, new LineNode { text = "x" });
            var a = Add(g, new ShakeEffect { duration = 0 });
            var b = Add(g, new ShakeEffect { duration = 0 });
            Link(g, start, StartNode.NextPort, line, GraphNode.FlowInPort);
            Link(g, line, LineNode.EnterPort, a, EffectNode.PlayPort);
            Link(g, a, EffectNode.DonePort, b, EffectNode.PlayPort);
            Link(g, b, EffectNode.DonePort, a, EffectNode.PlayPort);

            var (runner, _) = Runner();
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            try
            {
                Assert.DoesNotThrow(() => runner.Start(d));
            }
            finally
            {
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
            }
        }

        [Test]
        public void 흐름_점은_하나만_신호_점은_여러_개_잇는다()
        {
            var g = new NodeGraph();
            var line = Add(g, new LineNode());
            var x = Add(g, new LineNode());
            var y = Add(g, new LineNode());
            var fx = Add(g, new ShakeEffect());
            var fy = Add(g, new DimEffect());
            g.Connect(line.guid, LineNode.NextPort, x.guid, GraphNode.FlowInPort);
            g.Connect(line.guid, LineNode.NextPort, y.guid, GraphNode.FlowInPort);
            Assert.AreEqual(1, g.edges.Count);
            Assert.AreEqual(y.guid, g.edges[0].toNode);
            g.Connect(line.guid, LineNode.EnterPort, fx.guid, EffectNode.PlayPort);
            g.Connect(line.guid, LineNode.EnterPort, fy.guid, EffectNode.PlayPort);
            Assert.AreEqual(3, g.edges.Count);
            Assert.IsFalse(g.CanConnect(line.guid, LineNode.EnterPort, x.guid, GraphNode.FlowInPort, out _), "신호 → 흐름 금지");
            Assert.IsFalse(g.CanConnect(fx.guid, EffectNode.DonePort, fx.guid, EffectNode.PlayPort, out _), "자기 자신 금지");

            line.options.Add(new ChoiceOption());
            Assert.AreEqual(1, g.Prune(), "선택지가 생겨 '다음' 점이 사라지면 그 선이 정리된다");
        }
    }
}
