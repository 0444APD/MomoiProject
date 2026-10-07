using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GBible.Tests
{
    // 4게임(발라트로·와일드프로스트·매지크래프트·세피리아) 기믹을 받치는 부품: 패시브 '놓인 것마다'(자리 순서)·가진 동안 보정·{level},
    // 상태 효과 걸기 의도·종류, 확률 의도·조건, 사용 의도·TryUse, 지불 하한, 자리(Placement)·자리 Block, 다시 뽑기·후보 수, 시드 새 판, Grid.Rotate.
    public class GimmickPartsTests
    {
        [Serializable] public class GpJoker { public string id; public string joker; public int level = 1; public int mult; }
        [Serializable] public class GpUnit { public string id; public int hp = 10; public int attack = 2; }
        [Serializable]
        public class GpRun
        {
            public List<GpJoker> jokers = new List<GpJoker>();
            public List<GpUnit> board = new List<GpUnit>();
            public List<GpUnit> enemies = new List<GpUnit>();
            public GpUnit hero = new GpUnit { id = "hero", hp = 30, attack = 3 };
            public Dictionary<string, int> relics = new Dictionary<string, int>();
            public List<string> log = new List<string>();
            public int gold = 5;
            public int bonus;
            public int mult = 1;
        }
        [Serializable, GameEvent("점수 단계")] public class GpScored { public string card; }
        [Serializable, Intent("피해")] public class GpDamage { public string target; public int amount; public bool cancelled; }

        // 결과: 플레이어 데이터의 log에 "글:사용자" 한 줄(능력 정의는 복사되므로 테스트 객체 대신 데이터에 남긴다).
        [Serializable]
        public sealed class Log : Outcome
        {
            public string text = "";
            public override OutcomeKinds Kind => OutcomeKinds.Coded;
            public override void Run(TriggerContext context)
            {
                var who = context.User == null ? "-" : GameApi.HasId(context.User.GetType()) ? GameApi.IdOf(context.User) : "run";
                context.Game.Get<GpRun>("run").log.Add($"{text}:{who}");
            }
            public override string Describe(INameResolver names) => text;
        }

        // 결과: 배수 × 값(순서가 결과를 바꾸는 계산).
        [Serializable]
        public sealed class TimesMult : Outcome
        {
            public int value = 2;
            public override OutcomeKinds Kind => OutcomeKinds.Coded;
            public override void Run(TriggerContext context) => context.Game.Get<GpRun>("run").mult *= value;
            public override string Describe(INameResolver names) => "×" + value;
        }

        [Serializable]
        public sealed class PlusMult : Outcome
        {
            public int value = 4;
            public override OutcomeKinds Kind => OutcomeKinds.Coded;
            public override void Run(TriggerContext context) => context.Game.Get<GpRun>("run").mult += value;
            public override string Describe(INameResolver names) => "+" + value;
        }

        // 결과: 오른쪽 조커를 없앤다(발동 중에 목록이 바뀌는 경우).
        [Serializable]
        public sealed class DestroyRight : Outcome
        {
            public override OutcomeKinds Kind => OutcomeKinds.Coded;
            public override void Run(TriggerContext context)
            {
                var run = context.Game.Get<GpRun>("run");
                var right = Placement.Around(run.jokers, (GpJoker)context.User, 0, PlaceDirection.Right).FirstOrDefault();
                if (right != null) run.jokers.Remove(right);
            }
            public override string Describe(INameResolver names) => "오른쪽 없애기";
        }

        sealed class Edit : GameAction
        {
            public Action<GameApi> run;
            protected override void Run() => run(Game);
        }

        sealed class Hit : GameAction
        {
            public string target;
            public int amount;
            protected override void Run()
            {
                var hit = Game.Intercept(new GpDamage { target = target, amount = amount });
                if (!hit.cancelled) Game.Find<GpUnit>(hit.target).hp -= hit.amount;
            }
        }

        GameSession _session;
        GameApi Game => _session.Game;
        GpRun Run => _session.Data.Get<GpRun>("run");

        [SetUp]
        public void SetUp()
        {
            _session = new GameSession(new SessionOptions { Seed = 77 });
            _session.Data.Define("run", DataZone.Session, new GpRun
            {
                board = { new GpUnit { id = "a" }, null, new GpUnit { id = "b" }, new GpUnit { id = "c" }, new GpUnit { id = "d" }, null },
                enemies = { null, new GpUnit { id = "e1" }, new GpUnit { id = "e2" }, new GpUnit { id = "e3" }, null, null },
            }, "판");
        }

        void Do(Action<GameApi> run) => Game.Do(new Edit { run = run });

        PassiveDefinition Joker(string id, params Outcome[] outcomes)
        {
            var definition = new PassiveDefinition
            {
                Id = id, OwnerKey = "run", OwnerPath = "jokers", MatchField = "joker", PerElement = true, LevelField = "level",
                When = new EventTrigger { eventType = typeof(GpScored).FullName },
            };
            definition.Outcomes.AddRange(outcomes);
            _session.Passives.Define(definition);
            return definition;
        }

        [Test]
        public void 놓인_것마다_패시브는_등록_순서가_아니라_목록_순서로_발동한다()
        {
            Joker("times", new TimesMult { value = 2 }, new Log { text = "곱" });
            Joker("plus", new PlusMult { value = 4 }, new Log { text = "더하기" });
            Do(g => g.Get<GpRun>("run").jokers.AddRange(new[] { new GpJoker { id = "j1", joker = "plus" }, new GpJoker { id = "j2", joker = "times" } }));
            Do(g => g.Session.Events.Publish(new GpScored()));
            Assert.AreEqual((1 + 4) * 2, Run.mult, "왼쪽(더하기)부터");
            CollectionAssert.AreEqual(new[] { "더하기:j1", "곱:j2" }, Run.log, "사용자 = 그 조커");

            Do(g => { var run = g.Get<GpRun>("run"); run.mult = 1; run.log.Clear(); run.jokers.Reverse(); });
            Do(g => g.Session.Events.Publish(new GpScored()));
            Assert.AreEqual(1 * 2 + 4, Run.mult, "자리를 바꾸면 결과가 바뀐다");
        }

        [Test]
        public void 같은_조커가_둘이면_각자_발동하고_앞_칸이_없앤_조커는_발동하지_않는다()
        {
            Joker("plus", new PlusMult { value = 4 });
            Joker("dagger", new DestroyRight(), new Log { text = "단검" });
            Do(g => g.Get<GpRun>("run").jokers.AddRange(new[]
            {
                new GpJoker { id = "j1", joker = "plus" }, new GpJoker { id = "j2", joker = "dagger" }, new GpJoker { id = "j3", joker = "plus" }, new GpJoker { id = "j4", joker = "plus" },
            }));
            Do(g => g.Session.Events.Publish(new GpScored()));
            Assert.AreEqual(1 + 4 + 4, Run.mult, "j3은 단검이 없애서 발동하지 않는다(j1·j4만)");
            Assert.AreEqual(3, Run.jokers.Count);
            Assert.AreEqual(2, Game.Owned("plus"));
            Assert.AreEqual(1, Game.Owned("plus", Run.jokers[0]), "요소 하나의 레벨");
        }

        [Test]
        public void 가진_동안_능력치_보정은_가질_때만_붙고_레벨마다_커진다()
        {
            var amulet = new PassiveDefinition { Id = "amulet", OwnerKey = "run", OwnerPath = "relics", ModifierTarget = "hero" };
            amulet.Modifiers.Add(new PassiveModifier { attribute = "attack", kind = ModifierKind.Flat, value = 2, perLevel = true });
            _session.Passives.Define(amulet);
            Assert.AreEqual(3, Game.Attribute(Run.hero, "attack"));
            Do(g => g.Get<GpRun>("run").relics["amulet"] = 3);
            Assert.AreEqual(3 + 6, Game.Attribute(Run.hero, "attack"), "레벨 3 × 2");
            Assert.AreEqual(1, Game.Modifiers(Run.hero, "attack").Count(m => m.source == "amulet"), "보정 목록에도 보인다");
            Do(g => g.Get<GpRun>("run").relics.Remove("amulet"));
            Assert.AreEqual(3, Game.Attribute(Run.hero, "attack"), "잃으면 저절로 빠진다");

            var charm = new PassiveDefinition { Id = "battleCharm", OwnerKey = "run", OwnerPath = "jokers", MatchField = "joker", PerElement = true };
            charm.Modifiers.Add(new PassiveModifier { attribute = "mult", kind = ModifierKind.Percent, value = 0.5 });
            _session.Passives.Define(charm);
            Do(g => g.Get<GpRun>("run").jokers.Add(new GpJoker { id = "j1", joker = "battleCharm", mult = 10 }));
            Assert.AreEqual(15, Game.Attribute("j1", "mult"), "놓인 것마다면 그 요소가 받는다");
            Assert.AreEqual(1, _session.Undo() ? 1 : 0);
            Assert.AreEqual(0, Run.jokers.Count, "Undo하면 보정도 같이 사라진다(저장된 보정이 없다)");
        }

        [Test]
        public void 의도_고치기의_level_틀은_가진_쪽의_패시브_레벨이다()
        {
            var thorns = new PassiveDefinition { Id = "shell", OwnerKey = "run", OwnerPath = "relics", When = new InterceptTrigger { intentType = typeof(GpDamage).FullName } };
            thorns.Outcomes.Add(new ModifyIntentOutcome { path = "amount", op = IntentOp.Add, value = "-{level}" });
            _session.Passives.Define(thorns);
            Do(g => g.Get<GpRun>("run").relics["shell"] = 2);
            Game.Do(new Hit { target = "hero", amount = 5 });
            Assert.AreEqual(30 - 3, Run.hero.hp, "레벨 2만큼 덜 맞는다");
        }

        [Test]
        public void 상태_효과_걸기_의도는_바꾸고_늘리고_막는다()
        {
            _session.Statuses.Define(new StatusDefinition { Id = "burn", Kind = StatusKind.Debuff, Duration = StatusDuration.Events, Length = 2, TickEvent = typeof(GpScored).FullName });
            _session.Statuses.Define(new StatusDefinition { Id = "blueBurn", Kind = StatusKind.Debuff, Duration = StatusDuration.Events, Length = 5, TickEvent = typeof(GpScored).FullName });
            _session.Statuses.Define(new StatusDefinition { Id = "poison", Kind = StatusKind.Debuff, MaxStacks = 0 });
            _session.Statuses.Define(new StatusDefinition { Id = "spice", Kind = StatusKind.Buff, MaxStacks = 0 });
            var tejas = new PassiveDefinition { Id = "tejas", OwnerKey = "run", OwnerPath = "relics", When = new InterceptTrigger { intentType = typeof(StatusIntent).FullName, filter = new FieldFilter { path = "statusId", op = FieldOp.Equal, value = "burn" } } };
            tejas.Outcomes.Add(new ModifyIntentOutcome { path = "statusId", op = IntentOp.Set, value = "blueBurn" });
            _session.Passives.Define(tejas);
            var reaper = new PassiveDefinition { Id = "reaper", OwnerKey = "run", OwnerPath = "relics", When = new InterceptTrigger { intentType = typeof(StatusIntent).FullName, filter = new FieldFilter { path = "statusId", op = FieldOp.Equal, value = "poison" } } };
            reaper.Outcomes.Add(new ModifyIntentOutcome { path = "stacks", op = IntentOp.Multiply, value = "2" });
            _session.Passives.Define(reaper);
            var boots = new PassiveDefinition { Id = "boots", OwnerKey = "run", OwnerPath = "relics", When = new InterceptTrigger { intentType = typeof(StatusIntent).FullName, filter = new FieldFilter { path = "targetId", op = FieldOp.Equal, value = "hero" } } };
            boots.Outcomes.Add(new ModifyIntentOutcome { op = IntentOp.Cancel });
            _session.Passives.Define(boots);

            Do(g => { var r = g.Get<GpRun>("run").relics; r["tejas"] = 1; r["reaper"] = 1; });
            Do(g => g.ApplyStatus("e1", "burn"));
            Assert.AreEqual(0, Game.StatusStacks("e1", "burn"));
            Assert.AreEqual(1, Game.StatusStacks("e1", "blueBurn"), "화상 → 푸른 화상");
            Assert.AreEqual(5, Game.Statuses("e1").Single().remaining, "길이를 정하지 않았으면 바뀐 상태 효과의 길이");
            Do(g => g.ApplyStatus("e1", "poison", 3));
            Assert.AreEqual(6, Game.StatusStacks("e1", "poison"), "중첩 2배");
            Do(g => g.ApplyStatus("e1", "spice", 2));
            Assert.AreEqual(2, Game.StatusCount("e1", StatusKind.Debuff));
            Assert.AreEqual(1, Game.StatusCount("e1", StatusKind.Buff));
            Do(g => g.RemoveStatuses("e1", StatusKind.Debuff));
            Assert.AreEqual(0, Game.StatusCount("e1", StatusKind.Debuff), "정화");
            Assert.AreEqual(2, Game.StatusStacks("e1", "spice"), "버프는 그대로");

            Do(g => g.Get<GpRun>("run").relics["boots"] = 1);
            ActiveStatus applied = null;
            Do(g => applied = g.ApplyStatus("hero", "poison", 1));
            Assert.IsNull(applied, "막히면 null");
            Assert.AreEqual(0, Game.StatusStacks("hero", "poison"), "면역");
        }

        [Test]
        public void 확률_의도는_확률을_바꾸고_난수는_한_번만_굴린다()
        {
            var results = new List<bool>();
            Do(g => { for (int i = 0; i < 200; i++) results.Add(g.Chance(0.25, "eight")); });
            var plain = results.Count(r => r);
            Assert.IsTrue(plain >= 25 && plain <= 75, "대략 25%");

            var oops = new PassiveDefinition { Id = "oops", OwnerKey = "run", OwnerPath = "relics", When = new InterceptTrigger { intentType = typeof(ChanceIntent).FullName } };
            oops.Outcomes.Add(new ModifyIntentOutcome { path = "probability", op = IntentOp.Multiply, value = "2" });
            _session.Passives.Define(oops);
            Do(g => g.Get<GpRun>("run").relics["oops"] = 1);
            var doubled = new List<bool>();
            Do(g => { for (int i = 0; i < 200; i++) doubled.Add(g.Chance(0.25, "eight2")); });
            Assert.IsTrue(doubled.Count(r => r) > plain, "2배");

            // 같은 시드·같은 흐름이면 확률을 바꿔도 다음 난수가 밀리지 않는다.
            var a = new GameSession(new SessionOptions { Seed = 5 });
            var b = new GameSession(new SessionOptions { Seed = 5 });
            double nextA = 0, nextB = 0;
            a.Do(new Edit { run = g => { g.Chance(0.1, "s"); nextA = g.Random("s").NextDouble(); } });
            b.Do(new Edit { run = g => { g.Chance(0.9, "s"); nextB = g.Random("s").NextDouble(); } });
            Assert.AreEqual(nextA, nextB);
        }

        [Test]
        public void 확률_조건은_쓸_때만_굴리고_미리_보기에서는_굴리지_않는다()
        {
            var ability = new AbilityDefinition { Id = "lucky", When = new AllTrigger { triggers = { new UseTrigger(), new ConditionTrigger { condition = new ChanceCondition { percent = 50, stream = "lucky" } } } } };
            ability.Outcomes.Add(new Log { text = "행운" });
            _session.Abilities.Define(ability);
            // 같은 시드의 세션 둘: 한쪽만 미리 보기를 해도 다음 난수가 같다(미리 보기는 굴리지 않는다).
            var checker = new GameSession(new SessionOptions { Seed = 3 });
            checker.Abilities.Define(ability);
            var plain = new GameSession(new SessionOptions { Seed = 3 });
            for (int i = 0; i < 5; i++) Assert.IsTrue(checker.Game.CanUse("lucky", "hero"), "미리 보기는 참");
            Assert.AreEqual(plain.Random.Get("lucky").NextDouble(), checker.Random.Get("lucky").NextDouble(), "미리 보기가 난수를 쓰지 않았다");
            var used = 0;
            for (int i = 0; i < 40; i++) if (Game.TryUse("lucky", Run.hero)) used++;
            Assert.IsTrue(used >= 8 && used <= 32, "반쯤 쓰인다");
            Assert.AreEqual(used, Run.log.Count, "쓴 만큼만 결과");
        }

        [Test]
        public void 사용_의도는_횟수를_바꾸고_막으면_조용히_끝난다()
        {
            var ability = new AbilityDefinition { Id = "strike" };
            ability.Outcomes.Add(new Log { text = "타격" });
            _session.Abilities.Define(ability);
            var frenzy = new PassiveDefinition { Id = "frenzy", OwnerKey = "run", OwnerPath = "relics", When = new InterceptTrigger { intentType = typeof(UseIntent).FullName, filter = new FieldFilter { path = "ability", op = FieldOp.Equal, value = "strike" } } };
            frenzy.Outcomes.Add(new ModifyIntentOutcome { path = "times", op = IntentOp.Add, value = "{level}" });
            _session.Passives.Define(frenzy);
            Game.Use("strike", Run.hero);
            Assert.AreEqual(1, Run.log.Count);
            Do(g => g.Get<GpRun>("run").relics["frenzy"] = 2);
            Game.Use("strike", Run.hero);
            Assert.AreEqual(1 + 3, Run.log.Count, "1 + 레벨 2 = 세 번");

            var gated = new AbilityDefinition { Id = "gated", When = new AllTrigger { triggers = { new UseTrigger(), new ConditionTrigger { condition = new PlayerDataCondition { key = "run", filter = new FieldFilter { path = "gold", op = FieldOp.AtLeast, value = "100" } } } } } };
            gated.Outcomes.Add(new Log { text = "열림" });
            _session.Abilities.Define(gated);
            Assert.IsFalse(Game.TryUse("gated", Run.hero), "조건이 안 맞으면 거짓");
            Assert.Throws<GameFailure>(() => Game.Use("gated", Run.hero), "Use는 그대로 실패를 던진다");
        }

        [Test]
        public void 지불_하한을_낮추면_빚을_낼_수_있다()
        {
            Assert.Throws<GameFailure>(() => Do(g => g.Pay("run", "gold", 8)));
            var credit = new PassiveDefinition { Id = "credit", OwnerKey = "run", OwnerPath = "relics", When = new InterceptTrigger { intentType = typeof(CostIntent).FullName } };
            credit.Outcomes.Add(new ModifyIntentOutcome { path = "floor", op = IntentOp.Set, value = "-20" });
            _session.Passives.Define(credit);
            Do(g => g.Get<GpRun>("run").relics["credit"] = 1);
            Do(g => g.Pay("run", "gold", 8));
            Assert.AreEqual(-3, Run.gold);
            Assert.Throws<GameFailure>(() => Do(g => g.Pay("run", "gold", 18)), "−20 아래로는 안 된다");
        }

        [Test]
        public void 자리는_한_줄과_격자의_방향과_자리를_계산한다()
        {
            var row = new List<string> { "j1", "j2", "j3" };
            CollectionAssert.AreEqual(new[] { "j3" }, Placement.Around(row, "j2", 0, PlaceDirection.Right));
            CollectionAssert.AreEqual(new[] { "j1" }, Placement.Around(row, "j3", 0, PlaceDirection.First));
            Assert.IsTrue(Placement.Is(row, "j1", 0, PlaceSpot.First));
            Assert.IsTrue(Placement.Is(row, "j2", 0, PlaceSpot.Edge), "한 줄은 위아래가 없어 모두 가장자리");

            // 34칸 가방(6열, 마지막 줄 4칸).
            var bag = Enumerable.Range(0, 34).Select(i => "a" + i).ToList();
            CollectionAssert.AreEquivalent(new[] { "a1", "a6", "a7" }, Placement.Around(bag, "a0", 6, PlaceDirection.Around8));
            Assert.IsTrue(Placement.Is(bag, "a31", 6, PlaceSpot.BottomRow));
            Assert.IsTrue(Placement.Is(bag, "a28", 6, PlaceSpot.Edge), "아래 칸(34번)이 없어 가장자리");
            Assert.IsTrue(Placement.Is(bag, "a27", 6, PlaceSpot.Inner), "아래 칸(33번)이 있다");
            Assert.IsTrue(Placement.Is(bag, "a8", 6, PlaceSpot.Inner));
            CollectionAssert.AreEqual(new[] { 2, 7 }, Placement.Pattern(1, 34, 6, new[] { new Cell(1, 0), new Cell(0, 1) }).ToArray());
            CollectionAssert.AreEqual(new[] { 7, 0 }, Placement.Pattern(1, 34, 6, new[] { new Cell(1, 0), new Cell(0, 1) }, turns: 1).ToArray(), "시계 방향 90°: 오른쪽 → 아래, 아래 → 왼쪽");
            Assert.AreEqual(new Cell(0, -1), Grid.Rotate(new Cell(1, 0), 1), "Grid는 y 위 기준: 오른쪽 → 아래");
        }

        [Test]
        public void 자리_Block은_맞은편_줄_맨_앞과_양옆을_본다()
        {
            // 보드: 2줄 × 3칸(0~2가 첫 줄, 3~5가 둘째 줄). 각 줄 첫 칸이 맨 앞.
            var front = new PlaceTarget
            {
                list = new PlayerDataListRef { key = "run", path = "board" }, columns = 3, direction = PlaceDirection.RowFirst,
                other = new PlayerDataListRef { key = "run", path = "enemies" },
            };
            var ability = new AbilityDefinition { Id = "attack" };
            ability.Outcomes.Add(new TargetedOutcome { targets = { front }, effects = { new PokeEffect { amount = 3 } } });
            _session.Abilities.Define(ability);
            Game.Use("attack", "a");
            Assert.AreEqual(7, Run.enemies[1].hp, "첫 줄의 맞은편 첫 줄 맨 앞(빈 칸 건너뜀) = e1");
            Game.Use("attack", "d");
            Assert.AreEqual(7, Run.enemies[3].hp, "둘째 줄의 맞은편 둘째 줄 맨 앞 = e3");

            // 맞은편 줄 전체(Barrage): 기준과 같은 번호의 칸(바로 맞은편)도 들어간다.
            var row = new PlaceTarget { list = new PlayerDataListRef { key = "run", path = "board" }, columns = 3, direction = PlaceDirection.SameRow, other = new PlayerDataListRef { key = "run", path = "enemies" } };
            var barrage = new AbilityDefinition { Id = "barrage" };
            barrage.Outcomes.Add(new TargetedOutcome { targets = { row }, effects = { new PokeEffect { amount = 1 } } });
            _session.Abilities.Define(barrage);
            Game.Use("barrage", "b");
            Assert.AreEqual(6, Run.enemies[1].hp, "e1(첫 줄)");
            Assert.AreEqual(9, Run.enemies[2].hp, "e2 = b의 바로 맞은편(같은 번호 2)도 맞는다");
            Assert.AreEqual(7, Run.enemies[3].hp, "둘째 줄은 그대로");
            Assert.AreEqual("맞은편 같은 줄", row.Describe());

            var alone = new PlaceCondition { list = new PlayerDataListRef { key = "run", path = "board" }, columns = 3, spot = PlaceSpot.SidesEmpty };
            var check = new AbilityDefinition { Id = "fury", When = new AllTrigger { triggers = { new UseTrigger(), new ConditionTrigger { condition = alone } } } };
            check.Outcomes.Add(new Log { text = "분노" });
            _session.Abilities.Define(check);
            Assert.IsTrue(Game.TryUse("fury", "a"), "a의 오른쪽(1번)이 빈 칸, 왼쪽은 밖");
            Assert.IsFalse(Game.TryUse("fury", "c"), "c의 오른쪽(4번)에 d가 있다");
        }

        [Serializable, BlockInfo("테스트 피해")]
        public sealed class PokeEffect : Effect<GpUnit>
        {
            public int amount = 1;
            protected override void Apply(GpUnit target) => target.hp -= amount;
        }

        [Serializable]
        public sealed class GpPick : GameAction
        {
            [Chosen] public List<string> picked = new List<string>();
            protected override void Run() => Game.Get<GpRun>("run").log.AddRange(picked);
        }

        [Test]
        public void 다시_뽑기는_같은_표에서_후보를_새로_뽑는다()
        {
            var table = new WeightTableDefinition { Id = "relicPool" };
            foreach (var id in new[] { "r1", "r2", "r3", "r4", "r5", "r6", "r7", "r8" }) table.Entries.Add(new WeightEntry { item = id, weight = 1 });
            _session.Tables.Define(table);
            string choice = null;
            Do(g =>
            {
                choice = g.Session.Choices.Ask("유물", g.Roll("relicPool", 3, true).Select(id => new ChoiceCandidate(id)), new GpPick());
                g.Session.Choices.MarkTable(choice, "relicPool", 3, true);
            });
            var first = Game.Choice.options.Select(o => o.id).ToList();
            var rerolled = false;
            Do(g => rerolled = g.RerollChoice(choice));
            Assert.IsTrue(rerolled);
            Assert.AreEqual(3, Game.Choice.options.Count);
            Assert.AreEqual(choice, Game.Choice.id, "같은 선택");
            _session.Undo();
            CollectionAssert.AreEqual(first, Game.Choice.options.Select(o => o.id).ToList(), "Undo하면 원래 후보");
            Game.Answer(choice, first[0]);
            CollectionAssert.AreEqual(new[] { first[0] }, Run.log);
        }

        [Test]
        public void 레벨별_값과_필드_바꾸기로_쌓이는_조커와_레벨_표를_만든다()
        {
            // 레벨별 보정(3/6/10/14%) — 레벨 4를 넘으면 마지막 값.
            var amulet = new PassiveDefinition { Id = "crit", OwnerKey = "run", OwnerPath = "relics", ModifierTarget = "hero" };
            amulet.Modifiers.Add(new PassiveModifier { attribute = "attack", kind = ModifierKind.Flat, byLevel = { 3, 6, 10, 14 } });
            _session.Passives.Define(amulet);
            Do(g => g.Get<GpRun>("run").relics["crit"] = 3);
            Assert.AreEqual(3 + 10, Game.Attribute(Run.hero, "attack"));
            Do(g => g.Get<GpRun>("run").relics["crit"] = 9);
            Assert.AreEqual(3 + 14, Game.Attribute(Run.hero, "attack"));

            // {level:2/4/7}: 레벨 2면 4만큼 덜 맞는다.
            var shell = new PassiveDefinition { Id = "shell", OwnerKey = "run", OwnerPath = "relics", When = new InterceptTrigger { intentType = typeof(GpDamage).FullName } };
            shell.Outcomes.Add(new ModifyIntentOutcome { path = "amount", op = IntentOp.Add, value = "-{level:2/4/7}" });
            _session.Passives.Define(shell);
            Do(g => g.Get<GpRun>("run").relics["shell"] = 2);
            Game.Do(new Hit { target = "hero", amount = 10 });
            Assert.AreEqual(30 - 6, Run.hero.hp);

            // 쌓이는 조커: 점수 단계마다 이 조커의 mult +1(레벨 필드 = level → +레벨).
            var bus = Joker("bus", new TargetedOutcome { targets = { new SelfTarget() }, effects = { new ChangeFieldEffect { field = "mult", change = FieldChange.Add, value = "{level}" } } });
            Do(g => g.Get<GpRun>("run").jokers.AddRange(new[] { new GpJoker { id = "j1", joker = "bus", level = 1 }, new GpJoker { id = "j2", joker = "bus", level = 3 } }));
            Do(g => g.Session.Events.Publish(new GpScored()));
            Do(g => g.Session.Events.Publish(new GpScored()));
            Assert.AreEqual(2, Run.jokers[0].mult, "조커마다 따로 쌓인다");
            Assert.AreEqual(6, Run.jokers[1].mult);
            Do(g => g.Get<GpRun>("run").jokers[1].mult = 1);
            var counter = new ChangeFieldEffect { field = "mult", change = FieldChange.Add, value = "-5", clampZero = true };
            var drain = new AbilityDefinition { Id = "drain" };
            drain.Outcomes.Add(new TargetedOutcome { targets = { new SelfTarget() }, effects = { counter } });
            _session.Abilities.Define(drain);
            Game.Use("drain", "j2");
            Assert.AreEqual(0, Run.jokers[1].mult, "0 아래로 내리지 않음");
        }

        // 결과: 사용자(조커)의 mult를 판 배수에 더한다(원본 값을 읽는지 보려고).
        [Serializable]
        public sealed class AddOwnMult : Outcome
        {
            public override OutcomeKinds Kind => OutcomeKinds.Coded;
            public override void Run(TriggerContext context) => context.Game.Get<GpRun>("run").mult += ((GpJoker)context.User).mult;
            public override string Describe(INameResolver names) => "자기 배수 더하기";
        }

        static PlaceTarget Neighbor(PlaceDirection direction) => new PlaceTarget { list = new PlayerDataListRef { key = "run", path = "jokers" }, direction = direction };

        [Test]
        public void 빌려_쓰기는_옆_조각의_패시브를_원본_값으로_내고_쌓이는_값은_원본만_바꾼다()
        {
            Joker("plus", new PlusMult { value = 4 });
            Joker("blueprint", new BorrowOutcome { targets = { Neighbor(PlaceDirection.Right) } });
            Do(g => g.Get<GpRun>("run").jokers.AddRange(new[] { new GpJoker { id = "j1", joker = "plus" }, new GpJoker { id = "j2", joker = "blueprint" }, new GpJoker { id = "j3", joker = "plus" } }));
            Do(g => g.Session.Events.Publish(new GpScored()));
            Assert.AreEqual(1 + 4 + 4 + 4, Run.mult, "Blueprint가 오른쪽 +4를 한 번 더");

            Do(g => { var run = g.Get<GpRun>("run"); run.mult = 1; run.jokers.Clear(); });
            Joker("bus", new TargetedOutcome { targets = { new SelfTarget() }, effects = { new ChangeFieldEffect { field = "mult", value = "1" } } }, new AddOwnMult());
            Do(g => g.Get<GpRun>("run").jokers.AddRange(new[] { new GpJoker { id = "j1", joker = "blueprint" }, new GpJoker { id = "j2", joker = "bus", mult = 2 } }));
            Do(g => g.Session.Events.Publish(new GpScored()));
            Assert.AreEqual(3, Run.jokers[1].mult, "버스는 자기 차례에만 한 번 쌓인다");
            Assert.AreEqual(1 + 2 + 3, Run.mult, "Blueprint는 쌓기 전 원본 값(2), 버스는 쌓은 뒤(3)");

            // 서로 빌리는 고리: Blueprint(오른쪽) ↔ Brainstorm(맨 왼쪽)은 끝난다.
            Joker("brainstorm", new BorrowOutcome { targets = { Neighbor(PlaceDirection.First) } }, new Log { text = "브레인" });
            Do(g => { var run = g.Get<GpRun>("run"); run.mult = 1; run.jokers.Clear(); run.jokers.AddRange(new[] { new GpJoker { id = "j1", joker = "blueprint" }, new GpJoker { id = "j2", joker = "brainstorm" } }); });
            Do(g => g.Session.Events.Publish(new GpScored()));
            Assert.IsTrue(Run.log.Count >= 1 && Run.log.Count <= 4, "고리가 끊겨 몇 번 안에 끝난다: " + Run.log.Count);
        }

        [Test]
        public void 레벨_0으로_꺼진_조각은_빌려_쓰지도_못한다()
        {
            Joker("plus", new PlusMult { value = 4 });
            Joker("blueprint", new BorrowOutcome { targets = { Neighbor(PlaceDirection.Right) } });
            Do(g => g.Get<GpRun>("run").jokers.AddRange(new[] { new GpJoker { id = "j1", joker = "blueprint" }, new GpJoker { id = "j2", joker = "plus", level = 0 } }));
            Do(g => g.Session.Events.Publish(new GpScored()));
            Assert.AreEqual(1, Run.mult, "꺼진 j2는 직접도, Blueprint가 빌려서도 발동하지 않는다");
            Do(g => g.Get<GpRun>("run").jokers[1].level = 1);
            Do(g => g.Session.Events.Publish(new GpScored()));
            Assert.AreEqual(1 + 4 + 4, Run.mult, "켜면 둘 다");
        }

        [Test]
        public void 같은_시드로_새_판을_시작하면_같은_난수가_나온다()
        {
            var seed = RandomStreams.SeedOf(" daily-2026-10-05 ");
            Assert.AreEqual(seed, RandomStreams.SeedOf("DAILY-2026-10-05"), "앞뒤 빈칸·대소문자 무시");
            _session.NewRun(seed);
            var first = _session.Random.Get("deck").NextDouble();
            _session.Random.Get("deck").NextDouble();
            _session.NewRun(seed);
            Assert.AreEqual(first, _session.Random.Get("deck").NextDouble());
        }
    }
}
