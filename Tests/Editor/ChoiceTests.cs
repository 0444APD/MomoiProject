using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GBible.Tests
{
    // 선택 대기.
    public class ChoiceTests
    {
        [Serializable] public class CtCard { public string id; public int cost; }
        [Serializable] public class CtUnit { public string id; public int hp = 30; }
        [Serializable] public class CtRun { public List<CtCard> hand = new List<CtCard>(); public List<CtCard> discard = new List<CtCard>(); public CtUnit hero = new CtUnit { id = "hero" }; public string stance = ""; public List<string> relics = new List<string>(); }

        // 손패에서 고르게 하고, 고른 카드를 버린다.
        public sealed class AskDiscard : GameAction
        {
            public int count = 1;
            protected override void Run()
            {
                var run = Game.Get<CtRun>("run");
                Game.Ask("버릴 카드", run.hand, new DiscardChosen(), min: count, max: count);
            }
        }

        [Serializable]
        public sealed class DiscardChosen : GameAction
        {
            [Chosen] public List<CtCard> cards = new List<CtCard>();
            protected override void Run()
            {
                var run = Game.Get<CtRun>("run");
                foreach (var card in cards)
                {
                    run.hand.Remove(card);
                    run.discard.Add(card);
                }
            }
        }

        public sealed class AskStance : GameAction
        {
            protected override void Run() =>
                Game.Ask("자세", new[] { new ChoiceCandidate("attack", "공격"), new ChoiceCandidate("guard", "방어") }, new SetStance());
        }

        [Serializable]
        public sealed class SetStance : GameAction
        {
            [Chosen] public string stance;
            protected override void Run() => Game.Get<CtRun>("run").stance = stance;
        }

        public sealed class NoChosen : GameAction
        {
            protected override void Run() { }
        }

        sealed class Edit : GameAction
        {
            public Action<GameApi> run;
            protected override void Run() => run(Game);
        }

        // 효과: 고른 카드의 비용 0.
        [Serializable, BlockInfo("비용 0")]
        public sealed class FreeCard : Effect<CtCard>
        {
            protected override void Apply(CtCard target) => target.cost = 0;
        }

        // 대상 Block: 손패 전부.
        [Serializable, BlockInfo("손패")]
        public sealed class HandCards : TargetSelector
        {
            protected override IEnumerable<object> Select(IEnumerable<object> targets) => Game.Get<CtRun>("run").hand;
        }

        // 결과: 영웅 체력 +5.
        [Serializable]
        public sealed class Heal : Outcome
        {
            public override OutcomeKinds Kind => OutcomeKinds.Coded;
            public override void Run(TriggerContext context) => context.Game.Get<CtRun>("run").hero.hp += 5;
            public override string Describe(INameResolver names) => "회복";
        }

        // 결과: 고른 것(Picked)을 stance에 적는다(고른 원본 id가 넘어오는지 보기).
        [Serializable]
        public sealed class RecordPicked : Outcome
        {
            public override OutcomeKinds Kind => OutcomeKinds.Coded;
            public override void Run(TriggerContext context) => context.Game.Get<CtRun>("run").stance = string.Join(",", context.Picked.Cast<string>());
            public override string Describe(INameResolver names) => "고른 것 적기";
        }

        // 결과 '가중치 표에서 고르기'(Unity 쪽)가 하는 일: 표에서 뽑아 원본 id를 후보로 요구한다.
        static void AskFromTable(GameApi g, int min = 1) =>
            g.Session.Choices.Ask("유물", g.Roll("relicPool", 3, unique: true).Select(id => new ChoiceCandidate(id)),
                new ChoiceEffectsRun { ids = true, key = "run", path = "relics", then = { new RecordPicked() } }, min, 1, source: "event1");

        void DefineRelicPool()
        {
            var table = new WeightTableDefinition { Id = "relicPool" };
            foreach (var id in new[] { "anchor", "blood", "lantern", "bell" }) table.Entries.Add(new WeightEntry { item = id, weight = 1 });
            _session.Tables.Define(table);
        }

        GameSession _session;
        GameApi Game => _session.Game;
        CtRun Run => _session.Data.Get<CtRun>("run");

        static GameSession NewSession()
        {
            var session = new GameSession(new SessionOptions { Seed = 9 });
            var run = new CtRun();
            for (int i = 1; i <= 3; i++) run.hand.Add(new CtCard { id = "c" + i, cost = i });
            session.Data.Define("run", DataZone.Session, run, "판");
            return session;
        }

        [SetUp]
        public void SetUp() => _session = NewSession();

        [Test]
        public void 고르면_이어서_할_행동이_고른_객체로_실행된다()
        {
            Game.Do(new AskDiscard());
            Assert.IsTrue(Game.IsWaiting);
            Assert.AreEqual(3, Game.Choice.options.Count);
            Assert.AreEqual(3, Run.hand.Count, "고르기 전에는 아무것도 하지 않는다");

            Game.Answer(Game.Choice.id, Run.hand[1]);
            Assert.IsFalse(Game.IsWaiting);
            CollectionAssert.AreEqual(new[] { "c1", "c3" }, Run.hand.Select(c => c.id).ToList());
            CollectionAssert.AreEqual(new[] { "c2" }, Run.discard.Select(c => c.id).ToList());
        }

        [Test]
        public void 글_선택지는_id를_받는다()
        {
            Game.Do(new AskStance());
            Assert.AreEqual("방어", Game.Choice.options[1].label);
            Game.Answer(Game.Choice.id, "guard");
            Assert.AreEqual("guard", Run.stance);
        }

        [Test]
        public void 개수나_후보가_틀리면_아무것도_바뀌지_않는다()
        {
            Game.Do(new AskDiscard { count = 2 });
            var id = Game.Choice.id;
            Assert.Throws<GameFailure>(() => Game.Answer(id, "c1"));
            Assert.Throws<GameFailure>(() => Game.Answer(id, "c1", "zz"));
            Assert.Throws<GameFailure>(() => Game.Answer(id, "c1", "c1"));
            Assert.IsTrue(Game.IsWaiting, "실패한 답은 선택을 꺼내지 않는다");
            Game.Answer(id, "c1", "c3");
            Assert.AreEqual(2, Run.discard.Count);
        }

        [Test]
        public void 답하기를_Undo하면_다시_기다린다()
        {
            Game.Do(new AskDiscard());
            Game.Answer(Game.Choice.id, "c1");
            Assert.IsTrue(_session.Undo());
            Assert.IsTrue(Game.IsWaiting);
            Assert.AreEqual(3, Run.hand.Count);
            Assert.IsTrue(_session.Undo());
            Assert.IsFalse(Game.IsWaiting, "요구한 행동까지 Undo하면 선택도 사라진다");
        }

        [Test]
        public void 후보가_없으면_기다리지_않는다()
        {
            Game.Do(new Edit { run = g => g.Get<CtRun>("run").hand.Clear() });
            Game.Do(new AskDiscard());
            Assert.IsFalse(Game.IsWaiting);
        }

        [Test]
        public void Chosen_필드가_없으면_요구할_때_알린다()
        {
            var e = Assert.Throws<InvalidOperationException>(() => Game.Do(new Edit { run = g => g.Ask("x", new[] { new ChoiceCandidate("a") }, new NoChosen()) }));
            StringAssert.Contains("[Chosen]", e.Message);
        }

        [Test]
        public void 여러_선택은_먼저_요구한_것부터()
        {
            Game.Do(new AskStance());
            Game.Do(new AskDiscard());
            Assert.AreEqual("자세", Game.Choice.prompt);
            Game.Answer(Game.Choice.id, "attack");
            Assert.AreEqual("버릴 카드", Game.Choice.prompt);
        }

        [Test]
        public void 선택_요구_Block은_고른_것에_효과를_주고_결과를_잇는다()
        {
            var block = new ChoiceOutcome { prompt = "공짜로 만들 카드", candidates = { new HandCards() }, effects = { new FreeCard() }, then = { new Heal() } };
            Game.Do(new Edit { run = g => g.Session.Triggers.RunOutcomes(new Outcome[] { block }, new TriggerContext { Session = g.Session, Owner = "relic1" }, "Test") });
            Assert.IsTrue(Game.IsWaiting);
            Assert.AreEqual("relic1", Game.Choice.source);
            Game.Answer(Game.Choice.id, "c3");
            Assert.AreEqual(0, Run.hand[2].cost);
            Assert.AreEqual(2, Run.hand[1].cost, "고르지 않은 카드는 그대로");
            Assert.AreEqual(35, Run.hero.hp);
        }

        [Test]
        public void 원본_id에서_고르면_목록에_넣고_고른_뒤_결과가_id를_읽는다()
        {
            DefineRelicPool();
            Game.Do(new Edit { run = g => AskFromTable(g) });
            var options = Game.Choice.options.Select(o => o.id).ToList();
            Assert.AreEqual(3, options.Distinct().Count());
            Assert.AreEqual(0, Run.relics.Count, "고르기 전에는 넣지 않는다");

            var id = Game.Choice.id;
            Game.Answer(id, options[1]);
            CollectionAssert.AreEqual(new[] { options[1] }, Run.relics);
            Assert.AreEqual(options[1], Run.stance);
            Assert.Throws<GameFailure>(() => Game.Answer(id, options[0]), "답한 선택은 다시 답할 수 없다");
        }

        [Test]
        public void 원본_id_고르기를_Undo하면_같은_후보로_다시_기다린다()
        {
            DefineRelicPool();
            Game.Do(new Edit { run = g => AskFromTable(g) });
            var options = Game.Choice.options.Select(o => o.id).ToList();
            Game.Answer(Game.Choice.id, options[0]);
            Assert.IsTrue(_session.Undo());
            Assert.AreEqual(0, Run.relics.Count);
            CollectionAssert.AreEqual(options, Game.Choice.options.Select(o => o.id).ToList());

            var json = _session.ExportRun();
            var loaded = NewSession();
            loaded.ImportRun(json);
            CollectionAssert.AreEqual(options, loaded.Game.Choice.options.Select(o => o.id).ToList(), "후보는 저장에 남아 다시 뽑지 않는다");
            loaded.Game.Answer(loaded.Game.Choice.id, options[2]);
            CollectionAssert.AreEqual(new[] { options[2] }, loaded.Data.Get<CtRun>("run").relics);
        }

        [Test]
        public void 원본_id_고르기에서_최소_0이면_건너뛸_수_있다()
        {
            DefineRelicPool();
            Game.Do(new Edit { run = g => AskFromTable(g, min: 0) });
            Game.Answer(Game.Choice.id);
            Assert.IsFalse(Game.IsWaiting);
            Assert.AreEqual(0, Run.relics.Count);
            Assert.AreEqual("", Run.stance, "고른 뒤 결과는 고른 것 없이 돈다");
        }

        [Test]
        public void 기다리는_선택은_판_저장으로_이어진다()
        {
            Game.Do(new AskDiscard());
            var json = _session.ExportRun();
            var loaded = NewSession();
            loaded.ImportRun(json);
            Assert.IsTrue(loaded.Game.IsWaiting);
            loaded.Game.Answer(loaded.Game.Choice.id, "c2");
            Assert.AreEqual("c2", loaded.Data.Get<CtRun>("run").discard.Single().id);
        }
    }
}
