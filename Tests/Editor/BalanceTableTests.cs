using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GBible.Tests
{
    // 밸런스 값·곡선, 가중치 표, 플레이어용 실패(다국어).
    public class BalanceTableTests
    {
        [Serializable] public class BtCard { public string id; }
        [Serializable] public class BtRun { public int floor = 1; public int energy = 3; public List<BtCard> hand = new List<BtCard>(); public List<string> unlocked = new List<string>(); public List<string> relics = new List<string>(); public Dictionary<string, int> perks = new Dictionary<string, int>(); }

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
        public void 목록_줄은_뽑는_순간의_플레이어_데이터_목록에서_뽑는다()
        {
            var table = new WeightTableDefinition { Id = "handPick" };
            table.Entries.Add(new WeightEntry { listKey = "run", listPath = "hand" });
            table.Entries.Add(new WeightEntry { listKey = "run", listPath = "unlocked", weight = 2 });
            _session.Tables.Define(table);
            Assert.AreEqual(0, RollIn("handPick", 1).Count, "목록이 비어 있으면 아무것도 나오지 않는다");

            Game.Do(new Edit { run = g => { var run = g.Get<BtRun>("run"); run.hand.Add(new BtCard { id = "card1" }); run.hand.Add(new BtCard { id = "card2" }); run.unlocked.Add("bolt"); } });
            var seen = Enumerable.Range(0, 200).SelectMany(_ => RollIn("handPick", 1)).ToList();
            CollectionAssert.AreEquivalent(new[] { "bolt", "card1", "card2" }, seen.Distinct().ToList(), "객체는 id, 글자는 그대로");
            var chances = _session.Tables.Chances("handPick").ToDictionary(c => c.item, c => c.chance);
            Assert.AreEqual(0.5, chances["bolt"], 1e-9, "무게는 요소마다(1 + 1 + 2)");
        }

        [Test]
        public void 뺄_목록에_있는_것은_안쪽_표에서도_나오지_않는다()
        {
            var relics = Table("relicPool", 0, ("anchor", 1), ("blood", 1), ("rareRelics", 1));
            relics.Exclude.Add(new PlayerDataListRef { key = "run", path = "relics" });
            _session.Tables.Define(relics);
            _session.Tables.Define(Table("rareRelics", 0, ("crown", 1), ("bell", 1)));
            Game.Do(new Edit { run = g => g.Get<BtRun>("run").relics.AddRange(new[] { "anchor", "crown" }) });

            for (int i = 0; i < 30; i++)
                CollectionAssert.AreEquivalent(new[] { "blood", "bell" }, RollIn("relicPool", 4, unique: true), "가진 것을 빼면 둘만 남는다");
            Game.Do(new Edit { run = g => g.Get<BtRun>("run").relics.Add("bell") });
            Assert.IsTrue(Enumerable.Range(0, 30).All(_ => RollIn("relicPool", 1).Single() == "blood"), "안쪽 표가 다 빠지면 그 줄도 후보가 아니다");

            var cards = Table("cardPool", 0, ("card1", 1), ("card2", 1), ("card3", 1));
            cards.Exclude.Add(new PlayerDataListRef { key = "run", path = "hand[].id" });
            _session.Tables.Define(cards);
            Game.Do(new Edit { run = g => g.Get<BtRun>("run").hand.AddRange(new[] { new BtCard { id = "card1" }, new BtCard { id = "card3" } }) });
            Assert.IsTrue(Enumerable.Range(0, 20).All(_ => RollIn("cardPool", 1).Single() == "card2"), "요소 안의 필드 경로(hand[].id)로도 뺀다");
        }

        [Test]
        public void 뺄_목록과_목록_줄이_딕셔너리면_키를_ID로_본다()
        {
            // 가진 특성(ID → 레벨)을 빼고, 가진 특성에서 뽑는다 — 값(레벨)이 아니라 키가 ID.
            var pool = Table("perkPool", 0, ("swift", 1), ("tough", 1), ("lucky", 1));
            pool.Exclude.Add(new PlayerDataListRef { key = "run", path = "perks" });
            _session.Tables.Define(pool);
            Game.Do(new Edit { run = g => { var run = g.Get<BtRun>("run"); run.perks["swift"] = 2; run.perks["lucky"] = 1; } });
            Assert.IsTrue(Enumerable.Range(0, 20).All(_ => RollIn("perkPool", 1).Single() == "tough"), "가진 특성(키)은 빠진다");

            var owned = new WeightTableDefinition { Id = "ownedPerk" };
            owned.Entries.Add(new WeightEntry { listKey = "run", listPath = "perks" });
            _session.Tables.Define(owned);
            var seen = Enumerable.Range(0, 100).SelectMany(_ => RollIn("ownedPerk", 1)).Distinct().ToList();
            CollectionAssert.AreEquivalent(new[] { "swift", "lucky" }, seen, "목록 줄도 키");
        }

        [Test]
        public void 코드로_만든_풀에서_뽑고_Undo하면_같은_결과()
        {
            List<string> first = null;
            Game.Do(new Edit { run = g => first = g.Roll(new[] { ("x", 1.0), ("y", 0.0), ("z", 1.0) }, 6, stream: "test") });
            Assert.IsTrue(first.All(i => i == "x" || i == "z"), "무게 0은 나오지 않는다");
            Assert.IsTrue(_session.Undo());
            List<string> again = null;
            Game.Do(new Edit { run = g => again = g.Roll(new[] { ("x", 1.0), ("y", 0.0), ("z", 1.0) }, 6, stream: "test") });
            CollectionAssert.AreEqual(first, again);

            List<string> picks = null;
            Game.Do(new Edit { run = g => picks = g.Roll(new List<string> { "a", "b", "c", "d" }, 3, unique: true, stream: "shop") });
            Assert.AreEqual(3, picks.Distinct().Count());
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

        [Test]
        public void 하나만_뽑기의_알림은_미리보기에서_써_버리지_않는다()
        {
            var table = new WeightTableDefinition { Id = "coins" };
            table.Entries.Add(new WeightEntry { item = "coin", weight = 1, minCount = 3, maxCount = 3 });
            _session.Tables.Define(table);
            string seen = null;
            Game.Preview(new Edit { run = g => seen = g.Roll("coins") }, g => 0);   // 버튼을 흐리게 할지 보는 Game.CanDo처럼
            Assert.AreEqual("coin", seen);
            Assert.AreEqual(0, _session.Journal.Entries.Count(e => e.Message.Contains("첫 ID만 돌려줍니다")), "미리보기의 줄은 남지 않는다");
            Game.Do(new Edit { run = g => g.Roll("coins") });
            Assert.AreEqual(1, _session.Journal.Entries.Count(e => e.Message.Contains("첫 ID만 돌려줍니다")), "실제로 뽑을 때 한 번은 알린다");
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

        [Test]
        public void 표_곡선은_같은_단계의_점이_둘이면_목록에서_뒤의_점을_쓴다()
        {
            var middle = new Curve { kind = CurveKind.Table, points = { new CurvePoint { x = 1, y = 10 }, new CurvePoint { x = 5, y = 50 }, new CurvePoint { x = 5, y = 70 }, new CurvePoint { x = 10, y = 100 } } };
            Assert.AreEqual(70, middle.Evaluate(0, 5), 1e-9, "그 단계에서는 뒤의 점");
            Assert.AreEqual(55, middle.Evaluate(0, 4), 1e-9, "앞의 점은 쓰지 않는다(1 → 5 사이도 뒤의 점으로 잇는다)");
            Assert.AreEqual(76, middle.Evaluate(0, 6), 1e-9);
            var first = new Curve { kind = CurveKind.Table, points = { new CurvePoint { x = 1, y = 10 }, new CurvePoint { x = 1, y = 20 }, new CurvePoint { x = 5, y = 60 } } };
            Assert.AreEqual(20, first.Evaluate(0, 1), 1e-9, "첫 단계에 둘");
            Assert.AreEqual(20, first.Evaluate(0, 0), 1e-9, "첫 점 앞도 뒤의 점");
            var unordered = new Curve { kind = CurveKind.Table, points = { new CurvePoint { x = 5, y = 70 }, new CurvePoint { x = 1, y = 10 }, new CurvePoint { x = 5, y = 50 } } };
            Assert.AreEqual(50, unordered.Evaluate(0, 5), 1e-9, "적은 순서와 상관없이 같은 단계면 목록에서 뒤의 점");
            Assert.AreEqual(30, unordered.Evaluate(0, 3), 1e-9);
        }

        [Test]
        public void 거듭제곱은_단계가_0_이하면_지수가_음수여도_0이다()
        {
            var decay = new Curve { kind = CurveKind.Power, power = -1 };
            Assert.AreEqual(50, decay.Evaluate(100, 2), 1e-9, "100 × 2^−1");
            Assert.AreEqual(0, decay.Evaluate(100, 0), 1e-9, "단계 0 — ∞가 아니다");
            Assert.AreEqual(0, decay.Evaluate(100, -3), 1e-9);
            var zero = decay.Evaluate(0, 0);
            Assert.IsTrue(zero == 0, $"기준 값 0이어도 0(숫자가 아님이 아니다): {zero}");
            Assert.AreEqual(0, new Curve { kind = CurveKind.Power, power = 1.5 }.Evaluate(100, 0), 1e-9, "지수가 양수면 전과 같다");
        }

        [Test]
        public void 숫자가_아닌_무게의_줄은_후보가_아니고_다른_줄을_막지_않는다()
        {
            // 코드 풀: 무게 = 남은 체력 비율(0 / 0)
            List<string> picks = null;
            Game.Do(new Edit { run = g => picks = g.Roll(new[] { ("ghost", double.NaN), ("slime", 1.0) }, 5, stream: "nan") });
            CollectionAssert.AreEqual(new[] { "slime", "slime", "slime", "slime", "slime" }, picks, "숫자가 아닌 무게는 0 이하처럼 후보가 아니다");

            // 표: 무게를 밸런스 값에서 읽었는데 숫자가 아니다
            _session.Balance.Define("brokenWeight", double.NaN);
            var table = Table("loot", 1, ("gem", 3));
            table.Entries.Add(new WeightEntry { item = "relic", weightBalance = "brokenWeight" });
            _session.Tables.Define(table);
            var got = Enumerable.Range(0, 200).SelectMany(_ => RollIn("loot", 1)).ToList();
            Assert.IsTrue(got.Count > 100 && got.All(i => i == "gem"), $"꽝 1 · gem 3만 남는다({got.Count}개)");
            var chances = _session.Tables.Chances("loot");
            Assert.AreEqual("gem", chances.Single().item);
            Assert.AreEqual(0.75, chances.Single().chance, 1e-9);
        }

        [Test]
        public void 끝없이_큰_무게의_줄은_늘_나온다()
        {
            List<string> picks = null;
            Game.Do(new Edit { run = g => picks = g.Roll(new[] { ("rat", 1.0), ("boss", double.PositiveInfinity), ("bat", 1.0) }, 5, stream: "inf") });
            CollectionAssert.AreEqual(new[] { "boss", "boss", "boss", "boss", "boss" }, picks, "무게 ÷ 합 = 1(다른 줄·꽝은 0)");
            Game.Do(new Edit { run = g => picks = g.Roll(new[] { ("a", double.PositiveInfinity), ("b", double.PositiveInfinity), ("c", 1.0) }, 40, stream: "inf2") });
            CollectionAssert.AreEquivalent(new[] { "a", "b" }, picks.Distinct().ToList(), "끝없이 큰 줄이 여럿이면 그 줄끼리 같은 몫");

            _session.Tables.Define(Table("jackpot", 5, ("coin", 1), ("crown", double.PositiveInfinity)));
            Assert.IsTrue(Enumerable.Range(0, 20).All(_ => RollIn("jackpot", 1).SingleOrDefault() == "crown"), "꽝 무게가 있어도");
            var chances = _session.Tables.Chances("jackpot").ToDictionary(c => c.item, c => c.chance);
            Assert.AreEqual(1, chances["crown"], 1e-9);
            Assert.AreEqual(0, chances["coin"], 1e-9);
        }

        // 확률 공개는 항목마다 한 줄: 같은 항목이 여러 줄에 있으면 몫을 합친다(한 번 뽑을 때 그 항목이 나올 확률, 처음 나온 줄의 차례).
        [Test]
        public void 확률_공개는_같은_항목의_여러_줄을_합친다()
        {
            _session.Tables.Define(Table("mixed", 0, ("gem", 1), ("coin", 1), ("gem", 2)));
            var chances = _session.Tables.Chances("mixed");
            CollectionAssert.AreEqual(new[] { "gem", "coin" }, chances.Select(c => c.item).ToList());
            Assert.AreEqual(0.75, chances[0].chance, 1e-9);
            Assert.AreEqual(0.25, chances[1].chance, 1e-9);
            Assert.DoesNotThrow(() => chances.ToDictionary(c => c.item, c => c.chance), "사전으로 바꿔도 겹치지 않는다");
        }
        // 고정 %: 적힌 % 그대로, 무게 줄은 남은 몫을 무게 비율로(D-166).
        [Test]
        public void 고정_퍼센트_줄은_전체_중_그_퍼센트다()
        {
            var table = new WeightTableDefinition { Id = "loot" };
            table.Entries.Add(new WeightEntry { item = "rare", weight = 20, percent = true });
            table.Entries.Add(new WeightEntry { item = "sword", weight = 1 });
            table.Entries.Add(new WeightEntry { item = "shield", weight = 3 });
            _session.Tables.Define(table);
            var chances = _session.Tables.Chances("loot").ToDictionary(c => c.item, c => c.chance);
            Assert.AreEqual(0.20, chances["rare"], 1e-9);
            Assert.AreEqual(0.20, chances["sword"], 1e-9, "남은 80%를 1:3으로");
            Assert.AreEqual(0.60, chances["shield"], 1e-9);
            var got = Enumerable.Range(0, 500).Count(_ => RollIn("loot", 1).Single() == "rare");
            Assert.IsTrue(got >= 70 && got <= 130, $"20% 근처({got}/500)");
        }

        // 조건으로 빠진 고정 % 줄의 몫은 무게 줄이 나눠 가진다. 무게 줄이 없으면 고정 % 줄끼리 비율대로(D-167).
        [Test]
        public void 빠진_고정_퍼센트는_무게_줄이_나눠_가진다()
        {
            var table = new WeightTableDefinition { Id = "drop" };
            table.Entries.Add(new WeightEntry { item = "relic", weight = 10, percent = true, condition = new DeepFloor() });
            table.Entries.Add(new WeightEntry { item = "gem", weight = 30, percent = true });
            table.Entries.Add(new WeightEntry { item = "coin", weight = 1 });
            _session.Tables.Define(table);
            var shallow = _session.Tables.Chances("drop").ToDictionary(c => c.item, c => c.chance);
            Assert.AreEqual(0.30, shallow["gem"], 1e-9, "고정 %는 적힌 대로");
            Assert.AreEqual(0.70, shallow["coin"], 1e-9, "빠진 relic 10%까지 무게 줄이");
            Game.Do(new Edit { run = g => g.Get<BtRun>("run").floor = 3 });
            var deep = _session.Tables.Chances("drop").ToDictionary(c => c.item, c => c.chance);
            Assert.AreEqual(0.10, deep["relic"], 1e-9);
            Assert.AreEqual(0.60, deep["coin"], 1e-9);

            var only = new WeightTableDefinition { Id = "only" };
            only.Entries.Add(new WeightEntry { item = "a", weight = 30, percent = true });
            only.Entries.Add(new WeightEntry { item = "b", weight = 30, percent = true });
            _session.Tables.Define(only);
            var half = _session.Tables.Chances("only").ToDictionary(c => c.item, c => c.chance);
            Assert.AreEqual(0.5, half["a"], 1e-9, "무게 줄이 없으면 고정 % 줄끼리 비율대로");
        }

        // 목록 줄의 고정 %는 요소들이 나눠 가지고("이 묶음 = 전체의 n%"), 꽝도 고정 %가 된다.
        [Test]
        public void 목록_줄과_꽝도_고정_퍼센트가_된다()
        {
            Game.Do(new Edit { run = g => { var run = g.Get<BtRun>("run"); run.unlocked.Add("x"); run.unlocked.Add("y"); } });
            var table = new WeightTableDefinition { Id = "mix", EmptyWeight = 50, EmptyPercent = true };
            table.Entries.Add(new WeightEntry { listKey = "run", listPath = "unlocked", weight = 20, percent = true });
            table.Entries.Add(new WeightEntry { item = "z", weight = 1 });
            _session.Tables.Define(table);
            var chances = _session.Tables.Chances("mix").ToDictionary(c => c.item, c => c.chance);
            Assert.AreEqual(0.10, chances["x"], 1e-9, "목록 20%를 둘이 나눔");
            Assert.AreEqual(0.10, chances["y"], 1e-9);
            Assert.AreEqual(0.30, chances["z"], 1e-9, "꽝 50% · 목록 20% · 나머지 30%");
        }

        // 고정 % 줄이 없으면 예전과 같은 몫(무게 그대로).
        [Test]
        public void 고정_퍼센트가_없으면_무게_그대로다()
        {
            var shares = WeightTableSystem.Shares(new List<(double, bool)> { (1, false), (3, false), (0, false) }, out var total);
            Assert.AreEqual(4, total, 1e-9);
            CollectionAssert.AreEqual(new[] { 1.0, 3.0, 0.0 }, shares);
        }
        // 편집 화면 요약: 줄마다 최종 %(안쪽 표까지 곱한 전체 기준).
        [Test]
        public void 편집_요약은_안쪽_표까지_곱한_퍼센트를_보인다()
        {
            var inner = UnityEngine.ScriptableObject.CreateInstance<GBible.Data.WeightTableData>();
            inner.EditorAssignId("rareTable");
            inner.entries.Add(new GBible.Data.WeightTableEntry { item = "bow", weight = 1 });
            inner.entries.Add(new GBible.Data.WeightTableEntry { item = "staff", weight = 1 });
            var outer = UnityEngine.ScriptableObject.CreateInstance<GBible.Data.WeightTableData>();
            outer.EditorAssignId("loot");
            outer.entries.Add(new GBible.Data.WeightTableEntry { item = "rareTable", mode = GBible.Data.WeightMode.Percent, percent = 10 });
            outer.entries.Add(new GBible.Data.WeightTableEntry { item = "sword", weight = 1 });
            var lines = outer.Preview(id => id == "rareTable" ? inner : null, _ => null).ToDictionary(l => l.label, l => l.percent);
            Assert.AreEqual(10, lines["rareTable"], 1e-9);
            Assert.AreEqual(5, lines["bow"], 1e-9, "안쪽 표의 50% × 10%");
            Assert.AreEqual(90, lines["sword"], 1e-9);
            Assert.AreEqual(outer.ToDefinition().Entries[0].percent, true);
            UnityEngine.Object.DestroyImmediate(inner);
            UnityEngine.Object.DestroyImmediate(outer);
        }
    }
}
