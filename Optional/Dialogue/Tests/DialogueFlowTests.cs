using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using GBible.Data;
using GBible.Dialogue;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace GBible.Tests
{
    // 시험용 조건: 골드가 value 이상인가.
    [Serializable]
    public sealed class FlowTestGold : Condition
    {
        public double value;

        public override bool Evaluate(GameSession session) => session.Data.GetNumber("gold") >= value;
        public override void CollectDependencies(ICollection<string> stateKeys) => stateKeys.Add("gold");
        public override string Describe(INameResolver names) => $"gold >= {value}";
    }

    // 시험용 보상: 골드를 낸다(모자라면 GameFailure). failOnServer: 서버에서만 실패(클라이언트의 미리 해 보기는 통과).
    // failForReal: 미리보기에서는 되고 실제로는 실패(이동 뒤 안전망 시험).
    [Serializable]
    public sealed class FlowTestPay : Reward
    {
        public double amount;
        public bool failOnServer;
        public bool failForReal;

        public override RewardEffect Effect => RewardEffect.Reversible;

        public override void Grant(GameSession session)
        {
            if (failOnServer && session.Role == SessionRole.Server) throw new GameFailure("test.serverRefused", null, "서버가 거절");
            if (failForReal && !session.Actions.IsPreviewing) throw new GameFailure("test.realFailure", null, "실제로는 실패");
            session.Game.Pay("gold", "", amount, what: "골드");
        }

        public override string Describe(INameResolver names) => $"골드 {amount} 내기";
    }

    // 시험용 프로젝트 흐름 노드(RichCheckNode 같은 것): 골드가 threshold 이상이면 다음, 아니면 끝.
    [Serializable]
    public sealed class FlowTestGate : CustomFlowNode
    {
        public static int Calls;
        public double threshold;

        public override string Run(DialogueRunner runner)
        {
            Calls++;
            return runner.Session.Data.GetNumber("gold") >= threshold ? NextPort : null;
        }
    }

    // 시험용 행동: 보상 하나를 준다(실패를 고를 수 있다). permanent: 도전과제 보상처럼 되돌리지 않는 구역(Data.Permanent) 안에서 준다.
    [Serializable]
    public sealed class FlowTestGrant : GameAction
    {
        [NonSerialized] public Reward Reward;
        [NonSerialized] public Action AfterGrant;
        public bool fail;
        public bool permanent;

        protected override void Run()
        {
            if (permanent)
                using (Game.Session.Data.Permanent())
                    Reward?.Grant(Game.Session);
            else Reward?.Grant(Game.Session);
            AfterGrant?.Invoke();
            if (fail) throw Game.Fail("test.grantFail", "실패");
        }
    }

    // 실행기의 흐름 보강: 고를 것이 없는 선택지, 불러오기, 값 바꾸기 실패, 조용히 다시 밟기, 신호 고리, 멀티.
    public sealed class DialogueFlowTests
    {
        sealed class RecordingStage : IDialogueStage
        {
            public readonly List<string> Log = new List<string>();
            public string Line;
            public IReadOnlyList<DialogueChoice> Choices = new List<DialogueChoice>();

            public void ApplyState(StageState state) => Log.Add("apply");

            public IEffectPlayer Begin(EffectNode effect, EffectContext context, StageState before, StageState after)
            {
                Log.Add("begin " + effect.GetType().Name);
                return null;
            }

            public void ShowLine(LineNode line, string speakerName, string text)
            {
                Line = text;
                Log.Add("line " + text);
            }

            public void SetVisibleCharacters(int count)
            {
            }

            public void ShowChoices(IReadOnlyList<DialogueChoice> choices) => Choices = choices.ToList();
            public void Hide() => Log.Add("hide");
        }

        readonly List<Object> _made = new List<Object>();
        readonly Dictionary<string, DialogueData> _dialogues = new Dictionary<string, DialogueData>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _made) Object.DestroyImmediate(o);
            _made.Clear();
            _dialogues.Clear();
            DialogueLookup.Source = null;
        }

        DialogueData Dialogue(string id)
        {
            var data = ScriptableObject.CreateInstance<DialogueData>();
            data.EditorAssignId(id);
            _made.Add(data);
            _dialogues[id] = data;
            return data;
        }

        static T Add<T>(NodeGraph graph, T node) where T : GraphNode
        {
            graph.Add(node);
            return node;
        }

        static void Link(NodeGraph graph, GraphNode from, string fromPort, GraphNode to, string toPort) =>
            Assert.IsNotNull(graph.Connect(from.guid, fromPort, to.guid, toPort), $"{from.GetType().Name}.{fromPort} → {to.GetType().Name}.{toPort}");

        (DialogueRunner runner, RecordingStage stage) Runner(GameSession session = null)
        {
            var stage = new RecordingStage();
            var runner = new DialogueRunner(id => id != null && _dialogues.TryGetValue(id, out var d) ? d : null, _ => null)
                { Stage = stage, DefaultTypingSpeed = 10, ClickCooldown = 0, Session = session };
            return (runner, stage);
        }

        static GameSession Session(double gold)
        {
            var session = new GameSession(new SessionOptions { Seed = 1 });
            session.Data.Define("gold", DataZone.Session, gold);
            return session;
        }

        [Test]
        public void 고를_선택지가_없으면_클릭으로_고를_선택지_없음_점으로_가고_잇지_않았으면_끝난다()
        {
            var d = Dialogue("locked");
            var g = d.graph;
            var start = Add(g, new StartNode());
            var ask = Add(g, new LineNode
            {
                text = "무엇을 살래?",
                options =
                {
                    new ChoiceOption { text = "검", condition = new FlowTestGold { value = 100 } },
                    new ChoiceOption { text = "방패", condition = new FlowTestGold { value = 100 }, showWhenLocked = true },
                },
            });
            var none = Add(g, new LineNode { text = "살 것이 없네" });
            Link(g, start, StartNode.NextPort, ask, GraphNode.FlowInPort);
            Link(g, ask, LineNode.NoChoicePort, none, GraphNode.FlowInPort);

            var (runner, stage) = Runner(Session(0));
            runner.Start(d);
            runner.FastForward();
            Assert.AreEqual(DialoguePhase.Shown, runner.Phase, "고를 것이 없으면 선택지 상태로 멈추지 않는다");
            Assert.AreEqual(1, stage.Choices.Count, "흐리게 보여 주는 선택지만");
            Assert.IsFalse(stage.Choices[0].Enabled);
            Assert.IsFalse(runner.Choose(0));
            Assert.IsTrue(runner.Advance(), "클릭으로 넘어간다");
            Assert.AreEqual(none, runner.Line);
            CollectionAssert.AreEqual(new[] { DialogueRunner.NoChoiceDecision }, runner.Progress.decisions);
            CollectionAssert.IsEmpty(DialogueChecks.ChoicesWithoutFallback(g), "'고를 선택지 없음'을 이었으면 경고하지 않는다");

            var d2 = Dialogue("locked2");
            var start2 = Add(d2.graph, new StartNode());
            var ask2 = Add(d2.graph, new LineNode { text = "…", options = { new ChoiceOption { text = "검", condition = new FlowTestGold { value = 100 } } } });
            Link(d2.graph, start2, StartNode.NextPort, ask2, GraphNode.FlowInPort);
            CollectionAssert.AreEqual(new[] { ask2 }, DialogueChecks.ChoicesWithoutFallback(d2.graph), "모두 조건부이고 점을 잇지 않은 대사는 경고");
            var ended = false;
            runner.Ended += () => ended = true;
            runner.Start(d2);
            runner.FastForward();
            Assert.IsTrue(runner.Advance());
            Assert.IsTrue(ended, "점을 잇지 않았으면 다이얼로그 끝(멈춰 있지 않는다)");
        }

        [Test]
        public void 불러올_때는_조건을_다시_보지_않고_고른_길과_프로젝트_노드의_점을_따른다()
        {
            var d = Dialogue("buy");
            var g = d.graph;
            var start = Add(g, new StartNode());
            var gate = Add(g, new FlowTestGate { threshold = 100 });
            var ask = Add(g, new LineNode { text = "살래?", options = { new ChoiceOption { text = "산다", condition = new FlowTestGold { value = 100 } }, new ChoiceOption { text = "안 산다" } } });
            var pay = Add(g, new SetNode { rewards = { new FlowTestPay { amount = 100 } } });
            var bought = Add(g, new LineNode { text = "샀다" });
            Link(g, start, StartNode.NextPort, gate, GraphNode.FlowInPort);
            Link(g, gate, CustomFlowNode.NextPort, ask, GraphNode.FlowInPort);
            Link(g, ask, LineNode.OptionPrefix + ask.options[0].id, pay, GraphNode.FlowInPort);
            Link(g, pay, SetNode.NextPort, bought, GraphNode.FlowInPort);

            var session = Session(100);
            var (runner, _) = Runner(session);
            FlowTestGate.Calls = 0;
            runner.Start(d);
            runner.FastForward();
            Assert.IsTrue(runner.Choose(0));
            Assert.AreEqual(bought, runner.Line);
            Assert.AreEqual(0, session.Data.GetNumber("gold"), 1e-9, "산 뒤 골드 0");
            var progress = runner.Progress;
            CollectionAssert.AreEqual(new[] { DialogueRunner.FlowDecisionPrefix + CustomFlowNode.NextPort, ask.options[0].id }, progress.decisions, "프로젝트 노드가 고른 점도 남긴다");

            var json = JsonUtility.ToJson(progress);
            FlowTestGate.Calls = 0;
            var (other, stage) = Runner(session);
            Assert.IsTrue(other.Restore(JsonUtility.FromJson<DialogueProgress>(json)));
            Assert.AreEqual(bought, other.Line, "지금 골드로는 문이 닫히고 선택지도 숨지만 저장한 길을 그대로 밟는다");
            Assert.AreEqual(0, FlowTestGate.Calls, "프로젝트 노드의 Run을 다시 부르지 않는다");
            Assert.AreEqual(0, session.Data.GetNumber("gold"), 1e-9, "값 바꾸기를 다시 하지 않는다");
            Assert.IsFalse(stage.Log.Contains("hide"), "불러오는 중에 끝나지 않는다");
        }

        [Test]
        public void 값_바꾸기가_실패하면_대사에_머물고_결정과_로그를_남기지_않는다()
        {
            var d = Dialogue("fail");
            var g = d.graph;
            var start = Add(g, new StartNode());
            var ask = Add(g, new LineNode { text = "어느 것?", options = { new ChoiceOption { text = "비싼 것" }, new ChoiceOption { text = "싼 것" }, new ChoiceOption { text = "속임수" } } });
            var expensive = Add(g, new SetNode { rewards = { new FlowTestPay { amount = 1000 } } });
            var trick = Add(g, new SetNode { rewards = { new FlowTestPay { failForReal = true } } });
            var a = Add(g, new LineNode { text = "비싼 걸 샀다" });
            var b = Add(g, new LineNode { text = "싼 걸 샀다" });
            Link(g, start, StartNode.NextPort, ask, GraphNode.FlowInPort);
            Link(g, ask, LineNode.OptionPrefix + ask.options[0].id, expensive, GraphNode.FlowInPort);
            Link(g, expensive, SetNode.NextPort, a, GraphNode.FlowInPort);
            Link(g, ask, LineNode.OptionPrefix + ask.options[1].id, b, GraphNode.FlowInPort);
            Link(g, ask, LineNode.OptionPrefix + ask.options[2].id, trick, GraphNode.FlowInPort);
            Link(g, trick, SetNode.NextPort, a, GraphNode.FlowInPort);

            var session = Session(10);
            var (runner, stage) = Runner(session);
            var failures = new List<string>();
            runner.Failed += failures.Add;
            runner.Start(d);
            runner.FastForward();

            Assert.IsFalse(runner.Choose(0), "골드가 모자라 갈 수 없다");
            Assert.IsFalse(runner.Choose(0), "다시 골라도 같다");
            Assert.AreEqual(2, failures.Count, "고를 때마다 실패 글을 알린다");
            Assert.AreEqual(ask, runner.Line);
            Assert.AreEqual(DialoguePhase.Choosing, runner.Phase, "그 대사의 선택지에 머문다");
            Assert.AreEqual(3, runner.Choices.Count);
            CollectionAssert.IsEmpty(runner.Progress.decisions, "실패한 이동의 결정은 남지 않는다");
            Assert.AreEqual(1, runner.Log.Count, "고른 선택지도 로그에 남지 않는다");
            Assert.AreEqual(10, session.Data.GetNumber("gold"), 1e-9);

            // 미리 해 보기는 통과했는데 실제로 실패: 떠났다가 그 대사로 돌아온다.
            Assert.IsFalse(runner.Choose(2));
            Assert.AreEqual("실제로는 실패", failures[failures.Count - 1]);
            Assert.AreEqual(ask, runner.Line);
            Assert.AreEqual(DialoguePhase.Choosing, runner.Phase);
            Assert.AreEqual(3, stage.Choices.Count, "떠나며 지운 선택지를 다시 띄운다");
            CollectionAssert.IsEmpty(runner.Progress.decisions);
            Assert.AreEqual(1, runner.Log.Count);

            Assert.IsTrue(runner.Choose(1));
            Assert.AreEqual(b, runner.Line);
            CollectionAssert.AreEqual(new[] { ask.options[1].id }, runner.Progress.decisions);
        }

        [Test]
        public void 다이얼로그_재생_보상은_행동이_성공한_뒤에만_요청한다()
        {
            var session = Session(0);
            var requests = new List<DialoguePlayRequested>();
            session.Events.SubscribeAfterAction<DialoguePlayRequested>(requests.Add);
            var reward = new PlayDialogueReward { dialogue = "after", entryId = "start" };

            session.Game.Preview(new FlowTestGrant { Reward = reward }, _ => 0);
            Assert.IsTrue(session.Game.CanDo(new FlowTestGrant { Reward = reward }, out _));
            Assert.AreEqual(0, requests.Count, "미리보기·CanDo에서는 열지 않는다");

            Assert.Throws<GameFailure>(() => session.Do(new FlowTestGrant { Reward = reward, fail = true }));
            Assert.AreEqual(0, requests.Count, "실패한 행동에서는 열지 않는다");

            var seenDuring = -1;
            session.Do(new FlowTestGrant { Reward = reward, actorId = 3, AfterGrant = () => seenDuring = requests.Count });
            Assert.AreEqual(0, seenDuring, "행동 안에서는 아직 열지 않는다");
            Assert.AreEqual(1, requests.Count, "행동이 끝난 뒤 한 번");
            Assert.AreEqual("after", requests[0].dialogue);
            Assert.AreEqual(3, requests[0].player, "그 행동을 한 플레이어");
            Assert.AreEqual(3, requests[0].Owner, "멀티 서버는 그 사람에게만 보낸다");

            // 도전과제 보상처럼 되돌리지 않는 구역(Permanent)에서 주어도: 행동이 끝난 뒤에, 실패한 행동이면 열지 않는다.
            requests.Clear();
            Assert.Throws<GameFailure>(() => session.Do(new FlowTestGrant { Reward = reward, permanent = true, fail = true }));
            Assert.AreEqual(0, requests.Count, "실패한 행동 안의 보상으로는 열지 않는다");
            seenDuring = -1;
            session.Do(new FlowTestGrant { Reward = reward, permanent = true, actorId = 2, AfterGrant = () => seenDuring = requests.Count });
            Assert.AreEqual(0, seenDuring, "되돌리지 않는 구역에서도 행동 안에서는 열지 않는다");
            Assert.AreEqual(1, requests.Count);
            Assert.AreEqual(2, requests[0].player);
        }

        [Test]
        public void 스킵과_불러오기는_지나간_한_번짜리_연출을_내지_않고_마지막_모습만_그린다()
        {
            var d = Dialogue("skip");
            var g = d.graph;
            var start = Add(g, new StartNode());
            var a = Add(g, new LineNode { text = "하나" });
            var b = Add(g, new LineNode { text = "둘" });
            var c = Add(g, new LineNode { text = "셋", options = { new ChoiceOption { text = "예" } } });
            var sound = Add(g, new SoundEffect());
            var shake = Add(g, new ShakeEffect { duration = 0.5f, blockInput = false });
            var dim = Add(g, new DimEffect { amount = 0.5f, duration = 0.5f, blockInput = false });
            Link(g, start, StartNode.NextPort, a, GraphNode.FlowInPort);
            Link(g, a, LineNode.NextPort, b, GraphNode.FlowInPort);
            Link(g, b, LineNode.NextPort, c, GraphNode.FlowInPort);
            Link(g, b, LineNode.EnterPort, sound, EffectNode.PlayPort);
            Link(g, b, LineNode.EnterPort, shake, EffectNode.PlayPort);
            Link(g, b, LineNode.EnterPort, dim, EffectNode.PlayPort);

            var (runner, stage) = Runner();
            runner.Start(d);
            stage.Log.Clear();
            runner.SkipToChoice();
            Assert.AreEqual(c, runner.Line);
            Assert.IsFalse(stage.Log.Any(l => l.StartsWith("begin", StringComparison.Ordinal)), "지나간 줄의 효과음·흔들기·어둡게를 화면에 내지 않는다");
            Assert.AreEqual(1, stage.Log.Count(l => l == "apply"), "끝 모습을 한 번 적용");
            Assert.AreEqual(0.5f, runner.State.dim, 1e-4);
            CollectionAssert.AreEqual(new[] { "line 셋" }, stage.Log.Where(l => l.StartsWith("line", StringComparison.Ordinal)).ToArray(), "멈춘 줄만 띄운다(지나간 줄의 목소리 없음)");
            Assert.AreEqual(1, stage.Choices.Count);

            // 불러오기: 지나온 줄(둘)의 연출은 내지 않고, 들어갈 줄(셋)부터 보통 재생.
            var progress = runner.Progress;
            var (other, otherStage) = Runner();
            Assert.IsTrue(other.Restore(progress));
            Assert.AreEqual(c, other.Line);
            Assert.IsFalse(otherStage.Log.Any(l => l.StartsWith("begin", StringComparison.Ordinal)), "지나간 줄의 효과는 내지 않는다");
            Assert.AreEqual(0.5f, other.State.dim, 1e-4, "지나온 연출의 끝 모습");
            Assert.AreEqual(1, otherStage.Log.Count(l => l == "apply"));
        }

        [Test]
        public void 시간_이동은_지나간_효과음을_내지_않고_진행_중인_효과만_그_시점으로_그린다()
        {
            var d = Dialogue("seekFx");
            var g = d.graph;
            var a = Add(g, new LineNode { text = "하나", advance = LineAdvance.Timed, holdSeconds = 0.5f });
            var b = Add(g, new LineNode { text = "둘" });
            var sound = Add(g, new SoundEffect());
            var dim = Add(g, new DimEffect { amount = 1f, duration = 2f, ease = Ease.Linear, blockInput = false });
            Link(g, a, LineNode.NextPort, b, GraphNode.FlowInPort);
            Link(g, a, LineNode.EnterPort, sound, EffectNode.PlayPort);
            Link(g, b, LineNode.EnterPort, dim, EffectNode.PlayPort);

            var (runner, stage) = Runner();
            using (runner.Quietly())
            {
                runner.StartAt(d, a.guid);
                runner.Simulate(2f);
            }
            Assert.AreEqual(b, runner.Line);
            Assert.IsFalse(stage.Log.Contains("begin SoundEffect"), "지나간 효과음은 내지 않는다");
            Assert.IsTrue(stage.Log.Contains("begin DimEffect"), "진행 중인 효과는 그 시점의 모습으로 그린다");
        }

        [Test]
        public void 신호_고리는_첫_바퀴만_클릭을_막고_넘기면_멈춘다()
        {
            var d = Dialogue("loop");
            var g = d.graph;
            var start = Add(g, new StartNode());
            var line = Add(g, new LineNode { text = "번쩍번쩍" });
            var next = Add(g, new LineNode { text = "끝" });
            var a = Add(g, new ShakeEffect { duration = 0.5f });
            var b = Add(g, new FlashEffect { duration = 0.5f });
            Link(g, start, StartNode.NextPort, line, GraphNode.FlowInPort);
            Link(g, line, LineNode.NextPort, next, GraphNode.FlowInPort);
            Link(g, line, LineNode.EnterPort, a, EffectNode.PlayPort);
            Link(g, a, EffectNode.DonePort, b, EffectNode.PlayPort);
            Link(g, b, EffectNode.DonePort, a, EffectNode.PlayPort);
            CollectionAssert.AreEquivalent(new[] { a.guid, b.guid }, DialogueChecks.LoopingEffects(g), "그래프 검사가 고리를 찾는다");

            var (runner, _) = Runner();
            runner.Start(d);
            Assert.IsTrue(runner.InputBlocked, "첫 바퀴는 효과 시간을 지킨다");
            runner.Tick(0.5f);
            Assert.IsTrue(runner.InputBlocked);
            runner.Tick(0.5f);
            Assert.IsFalse(runner.InputBlocked, "두 번째 바퀴부터는 클릭을 막지 않는다");
            Assert.AreEqual(1, runner.RunningEffects);

            runner.FastForward();
            Assert.AreEqual(1, runner.RunningEffects, "빨리 감기는 한 번씩만 끝내고(끝없이 돌지 않는다) 다시 시작한 것은 그대로 둔다");
            Assert.IsTrue(runner.Advance());
            Assert.AreEqual(next, runner.Line);
            Assert.AreEqual(0, runner.RunningEffects, "줄을 넘기면 고리도 멈춘다");
        }

        [Test]
        public void 미리보기_시간_이동은_값_바꾸기와_신호를_다시_하지_않고_진행_기록을_건드리지_않는다()
        {
            var d = Dialogue("preview");
            var g = d.graph;
            var start = Add(g, new StartNode());
            var pay = Add(g, new SetNode { rewards = { new FlowTestPay { amount = 10 } } });
            var signal = Add(g, new SignalNode { signal = "shop" });
            var line = Add(g, new LineNode { text = "어서 와" });
            Link(g, start, StartNode.NextPort, pay, GraphNode.FlowInPort);
            Link(g, pay, SetNode.NextPort, signal, GraphNode.FlowInPort);
            Link(g, signal, SignalNode.NextPort, line, GraphNode.FlowInPort);

            var session = Session(100);
            var (runner, _) = Runner(session);
            var signals = 0;
            var progress = 0;
            runner.Signaled += _ => signals++;
            runner.ProgressChanged += () => progress++;
            for (var i = 0; i < 3; i++)
                using (runner.Quietly())
                {
                    runner.StartAt(d, start.guid);
                    runner.Simulate(1f);
                }
            Assert.AreEqual(line, runner.Line);
            Assert.AreEqual(100, session.Data.GetNumber("gold"), 1e-9, "시간 이동마다 값 바꾸기가 쌓이지 않는다");
            Assert.AreEqual(0, signals);
            Assert.AreEqual(0, progress, "미리보기는 판 저장의 진행 칸을 건드리지 않는다");

            runner.StartAt(d, start.guid);
            Assert.AreEqual(90, session.Data.GetNumber("gold"), 1e-9, "처음부터 재생하는 미리보기는 그대로 한다");
            Assert.AreEqual(1, signals);
            Assert.AreEqual(0, progress);
        }

        [Test]
        public void 태그가_아닌_꺾쇠_글은_글자로_센다()
        {
            Assert.AreEqual(6, DialogueRunner.VisibleLength("<주인공>님"));
            Assert.AreEqual(1, DialogueRunner.VisibleLength("<b>가</b>"));
            Assert.AreEqual(1, DialogueRunner.VisibleLength("<color=#ff0000>가</color>"));
            Assert.AreEqual(1, DialogueRunner.VisibleLength("<#f00>가"));
            Assert.AreEqual(2, DialogueRunner.VisibleLength("<link=\"term:burn\">화상</link>"));
            Assert.AreEqual(3, DialogueRunner.VisibleLength("<noparse><b></noparse>"), "noparse 안은 글자");
            Assert.AreEqual(3, DialogueRunner.VisibleLength("a<b"), "닫히지 않은 꺾쇠도 글자");
            Assert.AreEqual("<주<alpha=#00>인공>님", TextReveal.Reveal("<주인공>님", 2), "한 글자씩 나온다");
        }

        [Test]
        public void 먼저_온_대사_시작_신호는_바로_다음에_들어오는_줄에만_쓰인다()
        {
            var d = Dialogue("release");
            var g = d.graph;
            var start = Add(g, new StartNode());
            var k = Add(g, new LineNode { text = "케이" });
            var x = Add(g, new LineNode { text = "엑스" });
            var l = Add(g, new LineNode { text = "엘" });
            var fx = Add(g, new WaitEffect { duration = 0.2f, blockInput = false, keepAcrossLines = true, marks = { new ProgressMark { percent = 50 } } });
            Link(g, start, StartNode.NextPort, k, GraphNode.FlowInPort);
            Link(g, k, LineNode.NextPort, x, GraphNode.FlowInPort);
            Link(g, x, LineNode.NextPort, l, GraphNode.FlowInPort);
            Link(g, k, LineNode.EnterPort, fx, EffectNode.PlayPort);
            Link(g, fx, EffectNode.MarkPrefix + fx.marks[0].id, l, LineNode.TextStartPort);

            var (runner, _) = Runner();
            runner.Start(d);
            runner.Tick(0.15f);
            runner.FastForward();
            runner.Advance();
            Assert.AreEqual(x, runner.Line);
            runner.FastForward();
            runner.Advance();
            Assert.AreEqual(l, runner.Line);
            Assert.AreEqual(DialoguePhase.WaitingForText, runner.Phase, "다른 줄을 거쳐 왔으면 앞서 온 신호로 기다림을 건너뛰지 않는다");
        }

        [Test]
        public void 언어가_바뀌면_지금_대사와_선택지_로그를_새_언어로_다시_그린다()
        {
            var previous = Texts.Catalog;
            var catalog = new TextCatalog();
            catalog.SetTable("ko", new Dictionary<string, string> { ["line.flowTestA"] = "안녕", ["line.flowTestO"] = "응" });
            catalog.SetTable("en", new Dictionary<string, string> { ["line.flowTestA"] = "Hello", ["line.flowTestO"] = "Yes" });
            Texts.Catalog = catalog;
            try
            {
                var d = Dialogue("lang");
                var start = Add(d.graph, new StartNode());
                var line = Add(d.graph, new LineNode { text = "원문", lineId = "line.flowTestA", options = { new ChoiceOption { text = "원문", lineId = "line.flowTestO" } } });
                Link(d.graph, start, StartNode.NextPort, line, GraphNode.FlowInPort);
                var (runner, stage) = Runner();
                runner.Start(d);
                runner.FastForward();
                Assert.AreEqual("안녕", runner.Text);
                catalog.Language = "en";
                runner.RefreshTexts();
                Assert.AreEqual("Hello", runner.Text);
                Assert.AreEqual("Hello", stage.Line, "화면의 줄도 새 글로");
                Assert.AreEqual("Yes", stage.Choices[0].Text);
                Assert.AreEqual("Hello", runner.Log[0].Text, "로그도");
            }
            finally
            {
                Texts.Catalog = previous;
            }
        }

        [Test]
        public void 서버는_보낸_사람의_자리에서_닿는_값_바꾸기만_한다()
        {
            var d = Dialogue("guard");
            var g = d.graph;
            var start = Add(g, new StartNode());
            var ask = Add(g, new LineNode { text = "낼래?", options = { new ChoiceOption { text = "낸다" } } });
            var first = Add(g, new SetNode { rewards = { new FlowTestPay { amount = 10 } } });
            var second = Add(g, new SetNode { rewards = { new FlowTestPay { amount = 5 } } });
            var done = Add(g, new LineNode { text = "냈다" });
            Link(g, start, StartNode.NextPort, ask, GraphNode.FlowInPort);
            Link(g, ask, LineNode.OptionPrefix + ask.options[0].id, first, GraphNode.FlowInPort);
            Link(g, first, SetNode.NextPort, second, GraphNode.FlowInPort);
            Link(g, second, SetNode.NextPort, done, GraphNode.FlowInPort);
            DialogueLookup.Source = id => id != null && _dialogues.TryGetValue(id, out var found) ? found : null;

            var session = Session(100);
            var server = new NetServer(session, new NullServerTransport());
            try
            {
                Assert.Throws<GameFailure>(() => session.Do(new DialogueSetAction { dialogue = "guard", node = first.guid, actorId = 2 }), "진행 기록이 없으면 거절");
                session.Do(new DialogueProgressAction
                {
                    actorId = 2,
                    progress = JsonUtility.ToJson(new DialogueProgress { dialogue = "guard", entry = "start", at = "guard", node = ask.guid, lines = 1 }),
                });
                Assert.AreEqual(DataVisibility.Owner, session.Data.VisibilityOf("dialogue.progress:2", out var owner), "플레이어마다 주인만 보는 진행 칸");
                Assert.AreEqual(2, owner);
                Assert.Throws<GameFailure>(() => session.Do(new DialogueSetAction { dialogue = "guard", node = second.guid, actorId = 2 }), "앞의 값 바꾸기를 건너뛸 수 없다");
                session.Do(new DialogueSetAction { dialogue = "guard", node = first.guid, actorId = 2 });
                Assert.AreEqual(90, session.Data.GetNumber("gold"), 1e-9);
                Assert.Throws<GameFailure>(() => session.Do(new DialogueSetAction { dialogue = "guard", node = first.guid, actorId = 2 }), "같은 것을 다시 보낼 수 없다");
                session.Do(new DialogueSetAction { dialogue = "guard", node = second.guid, actorId = 2 });
                Assert.AreEqual(85, session.Data.GetNumber("gold"), 1e-9);
                session.Do(new DialogueSetAction { dialogue = "guard", node = first.guid });
                Assert.AreEqual(75, session.Data.GetNumber("gold"), 1e-9, "방장(이 세션의 플레이어) 자신의 행동은 보지 않는다");
            }
            finally
            {
                server.Dispose();
            }
        }

        [Test]
        public void 멀티_클라이언트는_서버_결과를_기다린_뒤_이어_가고_거절되면_대사로_돌아온다()
        {
            var d = Dialogue("netShop");
            var g = d.graph;
            var start = Add(g, new StartNode());
            var ask = Add(g, new LineNode
            {
                text = "무엇을?",
                options =
                {
                    new ChoiceOption { text = "산다", condition = new FlowTestGold { value = 50 } },
                    new ChoiceOption { text = "마법" },
                },
            });
            var pay = Add(g, new SetNode { rewards = { new FlowTestPay { amount = 50 } } });
            var magic = Add(g, new SetNode { rewards = { new FlowTestPay { failOnServer = true } } });
            var check = Add(g, new BranchNode { condition = new FlowTestGold { value = 100 } });
            var rich = Add(g, new LineNode { text = "아직 부자" });
            var poor = Add(g, new LineNode { text = "이제 가난" });
            var magicLine = Add(g, new LineNode { text = "마법이다" });
            Link(g, start, StartNode.NextPort, ask, GraphNode.FlowInPort);
            Link(g, ask, LineNode.OptionPrefix + ask.options[0].id, pay, GraphNode.FlowInPort);
            Link(g, pay, SetNode.NextPort, check, GraphNode.FlowInPort);
            Link(g, check, BranchNode.TruePort, rich, GraphNode.FlowInPort);
            Link(g, check, BranchNode.FalsePort, poor, GraphNode.FlowInPort);
            Link(g, ask, LineNode.OptionPrefix + ask.options[1].id, magic, GraphNode.FlowInPort);
            Link(g, magic, SetNode.NextPort, magicLine, GraphNode.FlowInPort);
            DialogueLookup.Source = id => id != null && _dialogues.TryGetValue(id, out var found) ? found : null;

            var net = new MemoryNetwork { Manual = true, Latency = 0.02 };
            var serverSession = new GameSession();
            serverSession.Data.Define("gold", DataZone.Session, 100.0);
            var server = new NetServer(serverSession, net.Server());
            server.Start();
            var client = new NetClient(new GameSession(), net.Client(), "철수");

            void Pump()
            {
                for (var i = 0; i < 20; i++)
                {
                    net.Advance(0.01);
                    server.Update(0.01);
                    client.Update(0.01);
                }
            }

            try
            {
                client.Connect();
                Pump();
                Assert.IsTrue(client.IsJoined);
                var rejected = new HashSet<GameAction>();
                var failures = new List<NetActionFailure>();
                client.ActionFailed += failure =>
                {
                    failures.Add(failure);
                    rejected.Add(failure.Action);
                };
                var (runner, stage) = Runner(client.Session);
                runner.ServerResult = action => rejected.Remove(action) ? false : client.PendingActions > 0 ? (bool?)null : true;
                runner.ProgressChanged += () => client.Session.Do(new DialogueProgressAction { progress = runner.Progress == null ? "" : JsonUtility.ToJson(runner.Progress) });

                runner.Start(d);
                Pump();
                runner.FastForward();
                Assert.AreEqual(DialoguePhase.Choosing, runner.Phase);

                // 서버가 거절: 결과를 기다렸다가 그 대사로 돌아온다.
                Assert.IsTrue(runner.Choose(1));
                Assert.IsTrue(runner.WaitingForServer, "서버 결과를 기다린다");
                Assert.IsFalse(runner.Choose(0), "기다리는 동안은 받지 않는다");
                Pump();
                runner.Tick(0.01f);
                Assert.IsFalse(runner.WaitingForServer);
                Assert.AreEqual(ask, runner.Line, "거절되면 그 대사에 머문다");
                Assert.AreEqual(DialoguePhase.Choosing, runner.Phase);
                Assert.AreEqual(2, stage.Choices.Count);
                CollectionAssert.IsEmpty(runner.Progress.decisions);

                // 서버가 값을 바꾼 뒤에 조건 분기를 본다(보낸 직후의 옛 값 100이 아니라 바뀐 50).
                Assert.IsTrue(runner.Choose(0));
                Pump();
                runner.Tick(0.01f);
                Assert.AreEqual(poor, runner.Line);
                Assert.AreEqual(50.0, serverSession.Data.GetNumber("gold"), 1e-9);
                Assert.AreEqual(50.0, client.Session.Data.GetNumber("gold"), 1e-9);
                Pump();
                Assert.IsTrue(client.Session.Data.IsDefined("dialogue.progress:1"), "내 진행 칸은 서버가 남겨 내게 보낸다");
                Assert.IsFalse(client.Session.Data.IsDefined("dialogue.guard:1"), "서버의 확인 칸은 보내지 않는다");

                // 같은 값 바꾸기를 다시 보내면 서버가 거절한다(지금 자리에서 닿지 않음).
                failures.Clear();
                client.Session.Do(new DialogueSetAction { dialogue = "netShop", node = pay.guid });
                Pump();
                Assert.AreEqual(1, failures.Count);
                Assert.AreEqual("momoi.dialogue.outOfStep", failures[0].Key);
                Assert.AreEqual(50.0, serverSession.Data.GetNumber("gold"), 1e-9);
            }
            finally
            {
                client.Dispose();
                server.Dispose();
            }
        }

        [Test]
        public void 끝에서_화면을_남겨도_카메라는_놓는다()
        {
            var made = new List<Object>();
            try
            {
                var cameraObject = new GameObject("Camera");
                made.Add(cameraObject);
                var camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 5f;
                var stageObject = new GameObject("Stage");
                made.Add(stageObject);
                var stage = stageObject.AddComponent<DialogueStage>();
                stage.stageCamera = camera;
                stage.clearOnEnd = false;
                stage.standingRoot = new GameObject("Standings").transform;
                stage.standingRoot.SetParent(stageObject.transform);

                var d = Dialogue("keep");
                var line = Add(d.graph, new LineNode { text = "…" });
                var zoom = Add(d.graph, new CameraEffect { zoomPercent = 200f, duration = 0f });
                var enter = Add(d.graph, new StandingEnterEffect { character = "arin", duration = 0f });
                Link(d.graph, line, LineNode.EnterPort, zoom, EffectNode.PlayPort);
                Link(d.graph, line, LineNode.EnterPort, enter, EffectNode.PlayPort);
                var runner = new DialogueRunner(_ => d, _ => null) { Stage = stage, ClickCooldown = 0 };
                runner.StartAt(d, line.guid);
                stage.Sync();
                Assert.AreEqual(2.5f, camera.orthographicSize, 1e-3);
                runner.Stop();
                stage.Sync();
                Assert.AreEqual(5f, camera.orthographicSize, 1e-3, "clearOnEnd를 꺼도 카메라는 되돌리고 더 덮어쓰지 않는다");
                Assert.AreEqual(1, stage.standingRoot.childCount, "스탠딩은 남긴다");
            }
            finally
            {
                foreach (var o in made) Object.DestroyImmediate(o);
            }
        }

        [Test]
        public void 시작하지_못하면_시작_알림을_내지_않는다()
        {
            var go = new GameObject("Player");
            try
            {
                var player = go.AddComponent<DialoguePlayer>();
                var started = 0;
                player.onStarted.AddListener(() => started++);
                LogAssert.Expect(LogType.Warning, new Regex("noSuchDialogueForFlowTest"));
                Assert.IsFalse(player.Play("noSuchDialogueForFlowTest"));
                Assert.AreEqual(0, started, "입력이 막힌 채로 남지 않게");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
