using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GBible.Tests
{
    // 밸런스 값·곡선, 가중치 표, 플레이어용 실패(다국어).
    public class BalanceTableTests
    {
        [Serializable] public class BtRun { public int floor = 1; public int energy = 3; }

        // 조건: 3층 이상.
        public sealed class DeepFloor : SimpleCondition
        {
            protected override bool Check() => Game.Get<BtRun>("run").floor >= 3;
        }

        sealed class Edit : GameAction
        {
            public Action<GameApi> run;
            protected override void Run() => run(Game);
        }

        GameSession _session;
        GameApi Game => _session.Game;

        [SetUp]
        public void SetUp()
        {
            _session = new GameSession(new SessionOptions { Seed = 11 });
            _session.Data.Define("run", DataZone.Session, new BtRun(), "판");
        }

        List<string> RollIn(string table, int count, bool unique = false)
        {
            List<string> result = null;
            Game.Do(new Edit { run = g => result = g.Roll(table, count, unique) });
            return result;
        }

        [Test]
        public void 곡선은_시작_단계의_기준값에서_변한다()
        {
            Assert.AreEqual(130, new Curve { kind = CurveKind.Linear, step = 15 }.Evaluate(100, 3), 1e-9);
            Assert.AreEqual(121, new Curve { kind = CurveKind.Exponential, rate = 1.1 }.Evaluate(100, 3), 1e-9);
            Assert.AreEqual(800, new Curve { kind = CurveKind.Power, power = 1.5 }.Evaluate(100, 4), 1e-9);
            var table = new Curve { kind = CurveKind.Table, points = { new CurvePoint { x = 1, y = 10 }, new CurvePoint { x = 5, y = 50 } } };
            Assert.AreEqual(30, table.Evaluate(0, 3), 1e-9, "점 사이는 직선");
            Assert.AreEqual(50, table.Evaluate(0, 9), 1e-9, "마지막 점 뒤는 끝 값");
            Assert.AreEqual(102, new Curve { kind = CurveKind.Linear, step = 0.5, rounding = CurveRounding.Round }.Evaluate(100, 4), 1e-9, "101.5 → 반올림");
            Assert.AreEqual(125, new Curve { kind = CurveKind.Linear, step = 15, useMax = true, max = 125 }.Evaluate(100, 3), 1e-9, "최댓값으로 자른다");
        }

        [Test]
        public void 곡선은_한쪽만_자를_수_있다()
        {
            var floor = new Curve { kind = CurveKind.Linear, step = -30, useMin = true, min = 0 };
            Assert.AreEqual(0, floor.Evaluate(100, 9), 1e-9, "0 아래로 내려가지 않는다");
            Assert.AreEqual(160, new Curve { kind = CurveKind.Linear, step = 30, useMin = true, min = 0 }.Evaluate(100, 3), 1e-9, "위로는 자르지 않는다");
        }

        [Test]
        public void 밸런스_값을_이름으로_읽는다()
        {
            _session.Balance.Define("startEnergy", 3);
            _session.Balance.Define("enemyHp", 40, new Curve { kind = CurveKind.Linear, step = 8 });
            Assert.AreEqual(3, Game.Balance("startEnergy"));
            Assert.AreEqual(40, Game.Balance("enemyHp"), "단계를 안 주면 기준값");
            Assert.AreEqual(56, Game.Balance("enemyHp", 3));
            Assert.AreEqual(3, Game.Balance("startEnergy", 7), "곡선이 없으면 단계와 상관없이 값");
            Assert.Throws<InvalidOperationException>(() => Game.Balance("nope"));
        }

        static WeightTableDefinition Table(string id, double empty, params (string item, double weight)[] entries)
        {
            var table = new WeightTableDefinition { Id = id, EmptyWeight = empty };
            foreach (var (item, weight) in entries) table.Entries.Add(new WeightEntry { item = item, weight = weight });
            return table;
        }

        [Test]
        public void 무게만큼_나오고_Undo하면_같은_결과()
        {
            _session.Tables.Define(Table("drop", 0, ("common", 8), ("rare", 2)));
            var counts = new Dictionary<string, int> { ["common"] = 0, ["rare"] = 0 };
            for (int i = 0; i < 2000; i++)
                foreach (var item in RollIn("drop", 1)) counts[item]++;
            Assert.IsTrue(counts["rare"] >= 320 && counts["rare"] <= 480, "2/10 근처");

            var first = RollIn("drop", 5);
            Assert.IsTrue(_session.Undo());
            CollectionAssert.AreEqual(first, RollIn("drop", 5), "Undo하면 난수도 돌아간다");
        }

        [Test]
        public void 꽝_무게만큼_아무것도_나오지_않는다()
        {
            _session.Tables.Define(Table("maybe", 3, ("gem", 1)));
            var got = Enumerable.Range(0, 400).Sum(_ => RollIn("maybe", 1).Count);
            Assert.IsTrue(got >= 70 && got <= 130, "1/4 근처");
            var chances = _session.Tables.Chances("maybe");
            Assert.AreEqual(0.25, chances.Single().chance, 1e-9);
        }

        [Test]
        public void 안쪽_표에서_다시_뽑고_서로_다른_것만_고를_수_있다()
        {
            _session.Tables.Define(Table("reward", 0, ("rareCards", 1), ("commonCards", 3)));
            _session.Tables.Define(Table("rareCards", 0, ("r1", 1), ("r2", 1)));
            _session.Tables.Define(Table("commonCards", 0, ("c1", 1), ("c2", 1), ("c3", 1)));
            for (int i = 0; i < 50; i++)
            {
                var picks = RollIn("reward", 3, unique: true);
                Assert.AreEqual(3, picks.Count);
                Assert.AreEqual(3, picks.Distinct().Count(), "서로 다른 카드");
                Assert.IsTrue(picks.All(p => p.StartsWith("r") || p.StartsWith("c")), "안쪽 표의 항목만 나온다");
            }
        }

        [Test]
        public void 조건이_맞을_때만_후보가_된다()
        {
            var table = Table("floorDrop", 0, ("potion", 1));
            table.Entries.Add(new WeightEntry { item = "relic", weight = 1000, condition = new DeepFloor() });
            _session.Tables.Define(table);
            Assert.IsTrue(Enumerable.Range(0, 30).All(_ => RollIn("floorDrop", 1).Single() == "potion"));
            Game.Do(new Edit { run = g => g.Get<BtRun>("run").floor = 3 });
            Assert.IsTrue(Enumerable.Range(0, 30).Count(_ => RollIn("floorDrop", 1).Single() == "relic") > 25);
        }

        [Test]
        public void 개수_범위만큼_같은_항목이_들어간다()
        {
            var table = new WeightTableDefinition { Id = "gold" };
            table.Entries.Add(new WeightEntry { item = "coin", weight = 1, minCount = 2, maxCount = 4 });
            _session.Tables.Define(table);
            for (int i = 0; i < 30; i++) { var n = RollIn("gold", 1).Count; Assert.IsTrue(n >= 2 && n <= 4); }
        }

        [Test]
        public void 하나만_뽑기는_개수가_많은_항목이면_표마다_한_번_알린다()
        {
            var table = new WeightTableDefinition { Id = "coins" };
            table.Entries.Add(new WeightEntry { item = "coin", weight = 1, minCount = 3, maxCount = 3 });
            _session.Tables.Define(table);
            string first = null;
            Game.Do(new Edit { run = g => { first = g.Roll("coins"); g.Roll("coins"); } });
            Assert.AreEqual("coin", first);
            Assert.AreEqual(1, _session.Journal.Entries.Count(e => e.Message.Contains("첫 ID만 돌려줍니다")));
        }

        public sealed class Never : SimpleCondition
        {
            protected override bool Check() => false;
        }

        [Test]
        public void 쓸_수_없을_때_글은_항목마다_정한다()
        {
            _session.Abilities.Define(new AbilityDefinition
            {
                Id = "heal",
                When = new AllTrigger { triggers = { new UseTrigger(), new ConditionTrigger { condition = new Never() } } },
                FailText = "체력이 가득 차서 {ability}을(를) 쓸 수 없습니다.",
            });
            Assert.IsFalse(Game.CanUse("heal", null, out var reason));
            Assert.AreEqual("체력이 가득 차서 heal을(를) 쓸 수 없습니다.", reason);
            var e = Assert.Throws<GameFailure>(() => Game.Use("heal", null));
            Assert.AreEqual(reason, e.Message);
        }

        [Test]
        public void 표끼리_돌면_알린다()
        {
            _session.Tables.Define(Table("a", 0, ("b", 1)));
            _session.Tables.Define(Table("b", 0, ("a", 1)));
            Assert.Throws<InvalidOperationException>(() => RollIn("a", 1));
        }

        [Test]
        public void 플레이어용_실패는_텍스트_표의_글로_보인다()
        {
            var previous = Texts.Catalog;
            try
            {
                Texts.Catalog = new TextCatalog { SourceLanguage = "ko", Language = "en" };
                var failure = Game.Fail("fail.frozen", "얼어 있어 {turns}턴 동안 움직일 수 없습니다.", ("turns", 2));
                Texts.Catalog.Language = "ko";
                Assert.AreEqual("얼어 있어 2턴 동안 움직일 수 없습니다.", failure.Message, "표에 없으면 기본 문장");
                Texts.Catalog.Set("en", "fail.frozen", "Frozen for {turns} turns.");
                Texts.Catalog.Language = "en";
                Assert.AreEqual("Frozen for 2 turns.", failure.Message, "언어를 바꾸면 다시 만든다");

                Texts.Catalog.Set("en", FailureKeys.NotEnough, "Not enough {what} ({have}/{need}).");
                var e = Assert.Throws<GameFailure>(() => Game.Do(new Edit { run = g => g.Pay("run", "energy", 5) }));
                StringAssert.Contains("Not enough", e.Message);
                Assert.AreEqual(3, _session.Data.Get<BtRun>("run").energy);
            }
            finally
            {
                Texts.Catalog = previous;
            }
        }
    }
}
