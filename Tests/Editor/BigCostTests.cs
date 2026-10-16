using System;
using NUnit.Framework;

namespace GBible.Tests
{
    // 큰 수 비용(방치형): Game.Pay가 BigNumber 칸 · BigNumber 양을 숫자로 다룬다. 할인은 비용 행동 직전 이벤트의 multiplier.
    // 기본 Block '플레이어 데이터에 주기' · '플레이어 데이터 검사'도 BigNumber 칸을 숫자로 본다.
    public class BigCostTests
    {
        [Serializable] public class BcRun { public BigNumber cookies; public double gems = 10; }

        sealed class Edit : GameAction
        {
            public Action<GameApi> run;
            protected override void Run() => run(Game);
        }

        GameSession _session;
        GameApi Game => _session.Game;
        BcRun Run => Game.Get<BcRun>("run");

        [SetUp]
        public void SetUp()
        {
            _session = new GameSession(new SessionOptions { Seed = 1 });
            _session.Data.Define("run", DataZone.Session, new BcRun { cookies = BigNumber.Parse("5e400") }, "판");
        }

        void Do(Action<GameApi> run) => _session.Do(new Edit { run = run });

        [Test]
        public void 큰_수_칸에서_큰_수_비용을_낸다()
        {
            Do(g => g.Pay("run", "cookies", BigNumber.Parse("2e400")));
            Assert.AreEqual(BigNumber.Parse("3e400"), Run.cookies);
            var failure = Assert.Throws<GameFailure>(() => Do(g => g.Pay("run", "cookies", BigNumber.Parse("1e401"))));
            Assert.AreEqual(FailureKeys.NotEnough, failure.Key);
            Assert.AreEqual(BigNumber.Parse("3e400"), Run.cookies, "모자라면 바뀌지 않는다");
            Assert.IsTrue(Game.CanPay("run", "cookies", BigNumber.Parse("3e400")));
            Assert.IsFalse(Game.CanPay("run", "cookies", BigNumber.Parse("4e400")));
        }

        [Test]
        public void 할인_배율은_큰_수_비용에도_맞는다()
        {
            var half = new ModifyIntentOutcome { path = "multiplier", op = IntentOp.Multiply, value = "0.5" };
            _session.Triggers.Register("sale", new InterceptTrigger { intentType = typeof(CostIntent).FullName }, repeat: true,
                fire: moment => _session.Triggers.RunOutcomes(new Outcome[] { half }, moment, "Test"));
            Do(g => g.Pay("run", "cookies", BigNumber.Parse("4e400")));
            Assert.AreEqual(BigNumber.Parse("3e400"), Run.cookies, "4e400 × 0.5 = 2e400을 냈다");
            Do(g => g.Pay("run", "gems", 4));
            Assert.AreEqual(8, Run.gems, 1e-9, "일반 숫자 칸도 배율");
        }

        [Test]
        public void 주기와_검사가_큰_수_칸을_숫자로_본다()
        {
            Do(g => new PlayerDataReward { key = "run", path = "cookies", amount = 1e300 }.Grant(g.Session));
            Assert.IsTrue(Run.cookies > BigNumber.Parse("5e400"), "큰 수에 더했다");
            Assert.IsTrue(FieldOps.Test(Run, "cookies", FieldOp.AtLeast, "1e400"));
            Assert.IsFalse(FieldOps.Test(Run, "cookies", FieldOp.Less, "1e400"));
            Assert.IsTrue(FieldOps.Test(Run, "cookies", FieldOp.Greater, "1.5K"), "글자 단위(1.5K)도 읽는다");
        }
    }
}
