using System;
using System.Linq;
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
            Do(g => g.Get<BcRun>("run").cookies = BigNumber.Parse("1.5e308"));
            Do(g => new PlayerDataReward { key = "run", path = "cookies", amount = 1.5e308 }.Grant(g.Session));
            Assert.AreEqual(BigNumber.Parse("3e308"), Run.cookies, "double을 넘는 합도 큰 수로 담긴다");
            Do(g => g.Get<BcRun>("run").cookies = BigNumber.Parse("5e400"));
            Assert.IsTrue(FieldOps.Test(Run, "cookies", FieldOp.AtLeast, "1e400"));
            Assert.IsFalse(FieldOps.Test(Run, "cookies", FieldOp.Less, "1e400"));
            Assert.IsTrue(FieldOps.Test(Run, "cookies", FieldOp.Greater, "1.5K"), "글자 단위(1.5K)도 읽는다");
            CollectionAssert.Contains(FieldOps.For(typeof(BigNumber)), FieldOp.AtLeast, "DB의 비교 고르기에도 숫자 비교가 나온다");
            Assert.IsTrue(FieldOps.TryValidateValue(typeof(BigNumber), FieldOp.AtLeast, "1e400", out _));
            Assert.IsFalse(BigNumber.TryParse("NaN", out _), "담을 수 없는 글자는 던지지 않고 거짓");
        }

        [Serializable] public struct BcWallet { public BigNumber coins; }
        [Serializable] public class BcBank { public BcWallet wallet; public double gold = double.PositiveInfinity; }

        [Test]
        public void 무한대와_구조체_부모와_고쳐진_양을_안전하게_다룬다()
        {
            _session.Data.Define("bank", DataZone.Session, new BcBank(), "은행");
            Do(g => g.Pay("bank", "gold", 5));
            Assert.IsTrue(double.IsPositiveInfinity(Game.Get<BcBank>("bank").gold), "무한대 칸도 예전처럼 낸다");
            Assert.Throws<InvalidOperationException>(() => Do(g => g.Pay("bank", "wallet.coins", 1)), "구조체 부모의 큰 수 칸은 공짜가 되지 않게 막는다");
            var triedInfinite = true;
            Do(g => triedInfinite = g.TryPay("run", "cookies", double.PositiveInfinity));
            Assert.IsFalse(triedInfinite, "무한대 값은 낼 수 없다(던지지 않고 거짓)");

            // 큰 비용을 '공짜'로(amount × 0 → 무한대 × 0 = NaN): 던지지 않고 공짜.
            var free = new ModifyIntentOutcome { path = "amount", op = IntentOp.Multiply, value = "0" };
            _session.Triggers.Register("free", new InterceptTrigger { intentType = typeof(CostIntent).FullName }, repeat: true,
                fire: moment => _session.Triggers.RunOutcomes(new Outcome[] { free }, moment, "Test"));
            Do(g => g.Pay("run", "cookies", BigNumber.Parse("1e400")));
            Assert.AreEqual(BigNumber.Parse("5e400"), Run.cookies);
        }

        [Test]
        public void 양과_큰_수를_함께_고치면_둘_다_맞는다()
        {
            _session.Triggers.Register("half", new InterceptTrigger { intentType = typeof(CostIntent).FullName }, repeat: true,
                fire: moment => { if (moment.Event is CostIntent c) c.amount *= 0.5; });
            _session.Triggers.Register("more", new InterceptTrigger { intentType = typeof(CostIntent).FullName }, repeat: true,
                fire: moment => { if (moment.Event is CostIntent c) c.big += new BigNumber(2); });
            Do(g => g.Pay("run", "gems", new BigNumber(4)));
            Assert.AreEqual(7, Run.gems, 1e-9, "(4 + 2) × (2 / 4) = 3을 냈다 — 한쪽 고침이 버려지지 않는다");
        }

        [Serializable] public class BcShop { public System.Collections.Generic.List<BcBuilding> buildings = new System.Collections.Generic.List<BcBuilding>(); [UnityEngine.SerializeField] public System.Collections.Generic.Dictionary<string, int> flags = new System.Collections.Generic.Dictionary<string, int>(); }
        [Serializable] public class BcBuilding { public string id; public string template; public bool active; public int count = 3; }

        [Test]
        public void 거르기_경로는_비용에_못_쓰고_참거짓을_알아듣고_등호_키도_읽는다()
        {
            var shop = new BcShop();
            shop.buildings.Add(new BcBuilding { id = "b1", template = "lens", active = true });
            shop.buildings.Add(new BcBuilding { id = "b2", template = "lens" });
            shop.flags["a=b"] = 7;
            _session.Data.Define("shop", DataZone.Session, shop, "상점");
            Assert.Throws<InvalidOperationException>(() => Do(g => g.Pay("shop", "buildings[template=lens].count", 1)), "여럿을 고르는 경로");
            Assert.AreEqual(1, FieldPaths.Resolve(Game.Get<BcShop>("shop"), "buildings[active=true]").Count(), "참/거짓은 true · 참으로");
            Assert.AreEqual(1, FieldPaths.Resolve(Game.Get<BcShop>("shop"), "buildings[active=참]").Count());
            Assert.AreEqual(7, FieldPaths.Resolve(Game.Get<BcShop>("shop"), "flags[a=b]").Single(), "딕셔너리에 그 글자 그대로의 키가 있으면 그 키");
        }
    }
}
