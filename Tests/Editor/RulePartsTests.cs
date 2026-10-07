using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GBible.Tests
{
    // 6장르 시험 뒤 보강한 규칙 부품: 경로의 [키], 상태 효과(이벤트·길이·줄이기·주기·그 이벤트에 건 것), 이벤트 구독자 실패,
    // NewIds, 의도 고치기 틀, 기본 대상 Block, 밸런스 값 무게, 패시브, 원본 ID 넣기(딕셔너리).
    public class RulePartsTests
    {
        [Serializable] public class RpWallet { public int gold = 10; }
        [Serializable] public struct RpPurse { public int gold; }
        [Serializable]
        public class RpBag
        {
            public RpPurse purse = new RpPurse { gold = 10 };
            public Dictionary<bool, int> flags = new Dictionary<bool, int> { [true] = 5 };
            public Dictionary<int, int> byLevel = new Dictionary<int, int> { [1] = 3 };
            public float ratio = 0.1f;
            public List<string> items = new List<string> { "a", "b" };
        }
        [Serializable, Intent("원소 피해")] public class RpElementHit { public Dictionary<string, int> byElement = new Dictionary<string, int> { ["fire"] = 1 }; public bool cancelled; }

        [Serializable] public class RpUnit { public string id; public int hp = 30; public string element = "none"; public List<string> perks = new List<string>(); }
        [Serializable]
        public class RpRun
        {
            public List<RpUnit> units = new List<RpUnit>();
            public Dictionary<string, int> stock = new Dictionary<string, int>();
            public List<string> relics = new List<string>();
            public int pity;
            public int turn;
        }
        [Serializable, GameEvent("턴 끝")] public class RpTurnEnded { public string unit; }
        [Serializable, GameEvent("맞음")] public class RpHit { public string sourceId; public string targetId; }
        [Serializable, Intent("피해")] public class RpDamage { public string target; public int amount; public bool cancelled; }

        public sealed class Mark : Outcome
        {
            public readonly List<string> log = new List<string>();
            public string text = "";
            public override OutcomeKinds Kind => OutcomeKinds.Coded;
            public override void Run(TriggerContext context) => log.Add($"{text}:{(context.User is RpUnit unit ? unit.id : context.User == null ? "-" : "run")}");
            public override string Describe(INameResolver names) => text;
        }

        [Serializable, BlockInfo("표시 피해")]
        public sealed class Poke : Effect<RpUnit>
        {
            public int amount = 1;
            protected override void Apply(RpUnit target) => target.hp -= amount;
        }

        public sealed class Hit : GameAction
        {
            public RpUnit unit;
            public int amount;
            protected override void Run()
            {
                var hit = Game.Intercept(new RpDamage { target = unit.id, amount = amount });
                if (!hit.cancelled) Game.Find<RpUnit>(hit.target).hp -= hit.amount;
            }
        }

        sealed class Edit : GameAction
        {
            public Action<GameApi> run;
            protected override void Run() => run(Game);
        }

        GameSession _session;
        GameApi Game => _session.Game;
        RpRun Run => _session.Data.Get<RpRun>("run");
        RpUnit Unit(string id) => Run.units.Single(u => u.id == id);

        [SetUp]
        public void SetUp()
        {
            _session = new GameSession(new SessionOptions { Seed = 21 });
            _session.Data.Define("run", DataZone.Session, new RpRun
            {
                units = { new RpUnit { id = "hero", hp = 40 }, new RpUnit { id = "foe", hp = 12, element = "fire" }, new RpUnit { id = "imp", hp = 7, element = "fire" } },
                stock = { ["gold"] = 50 },
            }, "판");
        }

        void Do(Action<GameApi> run) => Game.Do(new Edit { run = run });

        [Test]
        public void 값_하나인_데이터의_검사는_빈_경로가_값_전체라_검증도_통과한다()
        {
            // 숫자·글자·목록 하나인 플레이어 데이터(골드 · 이름 목록)는 실행할 때 빈 경로를 값 전체로 본다 — DB 검증도 같게(빌드가 막히지 않게).
            var problems = new List<string>();
            new FieldFilter { op = FieldOp.AtLeast, value = "100" }.Validate(typeof(double), problems);
            Assert.AreEqual(0, problems.Count, string.Join(" / ", problems));
            Assert.IsTrue(new FieldFilter { op = FieldOp.AtLeast, value = "100" }.Test(150.0), "실행도 값 전체");
            new FieldFilter { op = FieldOp.AtLeast, value = "많이" }.Validate(typeof(double), problems);
            Assert.AreEqual(1, problems.Count, "값의 형식은 그대로 본다");
            problems.Clear();
            new ElementFilter { mode = ElementMode.CountAtLeast, count = 2 }.Validate(typeof(List<string>), problems);
            Assert.AreEqual(0, problems.Count, string.Join(" / ", problems));
            Assert.IsTrue(new ElementFilter { mode = ElementMode.CountAtLeast, count = 2 }.Test(new List<string> { "a", "b" }));
            new FieldFilter { op = FieldOp.Equal, value = "1" }.Validate(typeof(RpWallet), problems);
            new ElementFilter { mode = ElementMode.Any }.Validate(typeof(RpWallet), problems);
            Assert.AreEqual(2, problems.Count, "클래스 값이면 칸을 골라야 한다");
        }

        [Test]
        public void 경로_도구는_키_형식_소수_점_든_키를_맞게_다룬다()
        {
            _session.Data.Define("player.bag", DataZone.Session, new RpBag(), "가방");
            var bag = _session.Data.Get<RpBag>("player.bag");
            Assert.AreEqual(5, FieldPaths.Resolve(bag, "flags[true]").Single(), "bool 키도 쓰기와 같은 칸을 읽는다");
            Assert.AreEqual(3, FieldPaths.Resolve(bag, "byLevel[01]").Single(), "int 키는 숫자로");
            Assert.IsTrue(FieldOps.Test(bag, "ratio", FieldOp.Equal, "0.1"), "float 0.1f는 0.1과 같다");
            Assert.IsTrue(FieldOps.Test(bag, "ratio", FieldOp.AtMost, "0.1"));
            var context = new TriggerContext { Session = _session };
            Assert.AreEqual("2", ModifyIntentOutcome.Expand("{count:player.bag.items}", context), "점이 든 키도 찾는다");
            Assert.Throws<InvalidOperationException>(() => Do(g => g.Pay("player.bag", "purse.gold", 3)), "구조체 안의 숫자로는 낼 수 없다(사본만 줄어든다)");
            Assert.AreEqual(10, bag.purse.gold);

            var intent = new RpElementHit();
            var modify = new ModifyIntentOutcome { path = "byElement[fire]", op = IntentOp.Add, value = "10" };
            modify.Run(new TriggerContext { Session = _session, Event = intent, IsIntercept = true });
            Assert.AreEqual(11, intent.byElement["fire"], "의도 고치기의 마지막 마디가 [키]여도 쓴다");

            Assert.IsFalse(IdRules.IsValid("gold\n", out _), "끝의 줄바꿈은 ID가 아니다");
            StringAssert.Contains("\\ud83d", MiniJson.Serialize("lone\uD83D"), "짝 없는 서로게이트는 이스케이프");
            Assert.AreEqual("😀", MiniJson.Deserialize(MiniJson.Serialize("😀")), "짝이 맞는 이모지는 그대로");
            Assert.Throws<FormatException>(() => MiniJson.Deserialize("\"\\u 41 \""), "\\u 뒤는 16진수 네 자리");
            Assert.AreEqual(0, Placement.Pattern(-1, 12, 6, new[] { new Cell(1, 0) }).Count(), "목록 밖 칸에서는 무늬도 없다");
        }

        [Test]
        public void 경로의_키로_딕셔너리_항목과_목록의_id_요소를_고른다()
        {
            Assert.AreEqual(12, FieldPaths.Resolve(Run, "units[foe].hp").Single());
            Assert.AreEqual(50, FieldPaths.Resolve(Run, "stock[gold]").Single());
            Assert.IsEmpty(FieldPaths.Resolve(Run, "stock[none]"), "없는 키는 비어 있다");
            Assert.AreEqual(typeof(int), FieldPaths.TypeAt(typeof(RpRun), "stock[gold]", out _));
            Assert.AreEqual(typeof(int), FieldPaths.TypeAt(typeof(RpRun), "units[foe].hp", out _));
            Assert.IsTrue(FieldOps.Test(Run, "units[imp].hp", FieldOp.Less, "10"));

            Do(g => g.Pay("run", "stock[gold]", 20, what: "골드"));
            Assert.AreEqual(30, Run.stock["gold"]);
            Assert.Throws<GameFailure>(() => Do(g => g.Pay("run", "stock[gem]", 1, what: "보석")), "없는 키는 0이라 모자란다");
            Do(g => new PlayerDataReward { key = "run", path = "stock[potion]", amount = 2 }.Grant(g.Session));
            Assert.AreEqual(2, Run.stock["potion"], "주기는 없던 키를 만든다");
            Do(g => new PlayerDataReward { key = "run", path = "units[hero].hp", amount = -5 }.Grant(g.Session));
            Assert.AreEqual(35, Unit("hero").hp);
            Assert.IsTrue(_session.Undo());
            Assert.AreEqual(40, Unit("hero").hp);
        }

        [Test]
        public void 원본_id를_딕셔너리에_넣으면_개수가_는다()
        {
            Do(g => GrantIds.Into(g.Session, "run", "stock", new[] { "potion", "gem", "potion" }));
            Assert.AreEqual(2, Run.stock["potion"]);
            Assert.AreEqual(1, Run.stock["gem"]);
            Do(g => GrantIds.Into(g.Session, "run", "relics", new[] { "anchor" }));
            CollectionAssert.AreEqual(new[] { "anchor" }, Run.relics);
            Do(g => GrantIds.Into(g.Session, "run", "pity", new[] { "a", "b" }));
            Assert.AreEqual(2, Run.pity, "숫자 하나면 개수만큼 더한다");
        }

        [Test]
        public void 상태_효과는_걸림_끝남을_쏘고_길이를_바꿔_걸고_줄일_수_있다()
        {
            var seen = new List<string>();
            _session.Events.Subscribe<StatusApplied>(e => seen.Add($"걸림:{e.status}:{e.stacks}"));
            _session.Events.Subscribe<StatusEnded>(e => seen.Add($"끝남:{e.status}:{e.expired}"));
            _session.Statuses.Define(new StatusDefinition { Id = "cd", Duration = StatusDuration.Events, Length = 3, TickEvent = typeof(RpTurnEnded).FullName });
            Do(g => g.ApplyStatus("hero", "cd", length: 1));
            Do(g => g.Session.Events.Publish(new RpTurnEnded()));
            CollectionAssert.AreEqual(new[] { "걸림:cd:1", "끝남:cd:True" }, seen, "길이 1로 걸면 한 번에 끝난다");

            Do(g => g.ApplyStatus("hero", "cd"));
            Do(g => g.ShortenStatus("hero", "cd", 2));
            Assert.AreEqual(1, Game.StatusStacks("hero", "cd"), "3 − 2 = 1 남음");
            Do(g => g.ShortenStatus("hero", "cd", 1));
            Assert.AreEqual(0, Game.StatusStacks("hero", "cd"), "다 줄이면 끝난다");
            Do(g => g.ApplyStatus("hero", "cd"));
            Do(g => g.RemoveStatus("hero", "cd"));
            Assert.AreEqual("끝남:cd:False", seen.Last(), "풀면 expired 거짓");
        }

        [Test]
        public void 이벤트를_받는_도중에_건_상태_효과는_그_이벤트를_세지_않는다()
        {
            _session.Statuses.Define(new StatusDefinition { Id = "burn", Duration = StatusDuration.Events, Length = 2, TickEvent = typeof(RpTurnEnded).FullName });
            _session.Triggers.Register("igniter", new EventTrigger { eventType = typeof(RpTurnEnded).FullName }, repeat: false,
                fire: c => c.Game.ApplyStatus("foe", "burn"));
            Do(g => g.Session.Events.Publish(new RpTurnEnded()));
            Assert.AreEqual(2, Game.Statuses("foe").Single().remaining, "건 그 턴 끝은 세지 않는다");
            Do(g => g.Session.Events.Publish(new RpTurnEnded()));
            Assert.AreEqual(1, Game.Statuses("foe").Single().remaining);
        }

        [Test]
        public void 주기가_있으면_걸려_있는_동안_초마다_결과를_낸다()
        {
            var tick = new Mark { text = "화상" };
            var burn = new StatusDefinition { Id = "burn", Duration = StatusDuration.Seconds, Length = 2.6, Pulse = 1 };
            burn.Outcomes.Add(tick);
            _session.Statuses.Define(burn);
            Do(g => g.ApplyStatus("foe", "burn"));
            for (var i = 0; i < 6; i++) _session.EndFrame(0.5);
            CollectionAssert.AreEqual(new[] { "화상:foe", "화상:foe" }, tick.log, "2.6초 동안 1초마다 두 번");
            Assert.AreEqual(0, Game.StatusStacks("foe", "burn"), "끝났다");
        }

        [Test]
        public void 행동_안에서_구독자가_실패하면_행동_전체가_취소된다()
        {
            _session.Events.Subscribe<RpTurnEnded>(_ => throw new InvalidOperationException("구독자 실수"));
            Assert.Throws<InvalidOperationException>(() => Do(g => { g.Get<RpRun>("run").turn = 5; g.Session.Events.Publish(new RpTurnEnded()); }));
            Assert.AreEqual(0, Run.turn, "반쯤 바뀐 값이 남지 않는다");
        }

        [Test]
        public void 새_id_여럿을_한_번에_만든다()
        {
            List<string> ids = null;
            Do(g => ids = g.NewIds("bat", 3));
            CollectionAssert.AreEqual(new[] { "bat1", "bat2", "bat3" }, ids);
            Do(g => ids = new List<string> { g.NewId("bat") });
            Assert.AreEqual("bat4", ids[0]);
        }

        [Test]
        public void 의도_고치기는_틀로_사용자와_중첩을_쓴다()
        {
            var taunt = new StatusDefinition { Id = "taunt", When = new InterceptTrigger { intentType = typeof(RpDamage).FullName } };
            taunt.Outcomes.Add(new ModifyIntentOutcome { path = "target", op = IntentOp.Set, value = "{user}" });
            taunt.Outcomes.Add(new ModifyIntentOutcome { path = "amount", op = IntentOp.Add, value = "-{stacks}" });
            _session.Statuses.Define(taunt);
            Do(g => g.ApplyStatus("hero", "taunt", stacks: 2));
            Game.Do(new Hit { unit = Unit("imp"), amount = 6 });
            Assert.AreEqual(7, Unit("imp").hp, "도발: 피해가 가진 쪽으로 간다");
            Assert.AreEqual(36, Unit("hero").hp, "6 − 중첩 2 = 4");
        }

        [Test]
        public void 기본_대상_Block을_이어_붙여_고른다()
        {
            void Hurt(params TargetSelector[] chain)
            {
                var outcome = new TargetedOutcome { effects = { new Poke { amount = 1 } } };
                outcome.targets.AddRange(chain);
                Do(g => g.Session.Triggers.RunOutcomes(new Outcome[] { outcome }, new TriggerContext { Session = g.Session, Event = new RpHit { sourceId = "imp", targetId = "hero" } }, "Test"));
            }
            Hurt(new DataListTarget { key = "run", path = "units" }, new SortTarget { path = "hp", count = 1 });
            Assert.AreEqual(6, Unit("imp").hp, "체력이 가장 낮은 하나");
            Hurt(new DataListTarget { key = "run", path = "units" }, new FilterTarget { path = "element", op = FieldOp.Equal, value = "fire" });
            Assert.AreEqual(11, Unit("foe").hp);
            Assert.AreEqual(5, Unit("imp").hp, "불 속성만");
            Assert.AreEqual(40, Unit("hero").hp);
            Hurt(new EventFieldTarget { field = "sourceId" });
            Assert.AreEqual(4, Unit("imp").hp, "이벤트의 때린 쪽");
            Hurt(new DataListTarget { key = "run", path = "units" }, new RandomTarget { count = 2 });
            Assert.AreEqual(40 + 11 + 4 - 2, Run.units.Sum(u => u.hp), "무작위 둘");
        }

        [Test]
        public void 밸런스_값_무게는_단계에_따라_바뀐다()
        {
            _session.Balance.Define("pityWeight", 0, new Curve { kind = CurveKind.Table, points = { new CurvePoint { x = 0, y = 0 }, new CurvePoint { x = 88, y = 0 }, new CurvePoint { x = 89, y = 1000000 } } });
            var table = new WeightTableDefinition { Id = "banner" };
            table.Entries.Add(new WeightEntry { item = "common", weight = 99 });
            table.Entries.Add(new WeightEntry { item = "legend", weightBalance = "pityWeight", weightStepKey = "run", weightStepPath = "pity" });
            _session.Tables.Define(table);
            List<string> pulls = null;
            Do(g => pulls = g.Roll("banner", 20));
            Assert.IsTrue(pulls.All(p => p == "common"), "천장 전에는 무게 0");
            Do(g => g.Get<RpRun>("run").pity = 89);
            Do(g => pulls = g.Roll("banner", 1));
            Assert.AreEqual("legend", pulls.Single(), "89번째에는 거의 확정");
        }

        // 단계가 id 있는 객체의 숫자 필드면 능력치(보정 포함)로 읽는다 — 후보 수에 더할 숫자와 같은 규칙(행운 유물 +N이 확률에도 든다).
        [Test]
        public void 밸런스_값_무게의_단계는_보정을_포함한_능력치로_읽는다()
        {
            _session.Balance.Define("luckWeight", 0, new Curve { kind = CurveKind.Table, points = { new CurvePoint { x = 0, y = 0 }, new CurvePoint { x = 49, y = 0 }, new CurvePoint { x = 50, y = 1000000 } } });
            var table = new WeightTableDefinition { Id = "lucky" };
            table.Entries.Add(new WeightEntry { item = "common", weight = 1 });
            table.Entries.Add(new WeightEntry { item = "legend", weightBalance = "luckWeight", weightStepKey = "run", weightStepPath = "units[hero].hp" });
            _session.Tables.Define(table);
            List<string> pulls = null;
            Do(g => pulls = g.Roll("lucky", 10));
            Assert.IsTrue(pulls.All(p => p == "common"), "hero 체력 40 — 50 전에는 무게 0");
            Do(g => g.AddModifier(Unit("hero"), "hp", ModifierKind.Flat, 10, "relic"));
            Assert.AreEqual(40, Unit("hero").hp, "필드 자체는 그대로");
            Do(g => pulls = g.Roll("lucky", 1));
            Assert.AreEqual("legend", pulls.Single(), "보정 +10으로 단계 50 — 거의 확정");
        }

        [Test]
        public void 패시브는_가진_쪽마다_발동하고_개수를_센다()
        {
            var mark = new Mark { text = "닻" };
            var anchor = new PassiveDefinition { Id = "anchor", OwnerKey = "run", OwnerPath = "relics", When = new EventTrigger { eventType = typeof(RpTurnEnded).FullName } };
            anchor.Outcomes.Add(mark);
            _session.Passives.Define(anchor);
            var spikes = new Mark { text = "가시" };
            var thorns = new PassiveDefinition { Id = "thorns", OwnerKey = "run", OwnerPath = "units[].perks", When = new EventTrigger { eventType = typeof(RpHit).FullName }, HolderField = "targetId" };
            thorns.Outcomes.Add(spikes);
            _session.Passives.Define(thorns);

            Do(g => g.Session.Events.Publish(new RpTurnEnded()));
            Assert.IsEmpty(mark.log, "가지기 전에는 발동하지 않는다");
            Do(g => { g.Get<RpRun>("run").relics.AddRange(new[] { "anchor", "anchor" }); Unit("foe").perks.Add("thorns"); });
            Do(g => g.Session.Events.Publish(new RpTurnEnded()));
            Assert.AreEqual(1, mark.log.Count, "가진 쪽(run) 하나라 한 번");
            Assert.AreEqual(2, Game.Owned("anchor"), "두 개 가짐");
            Do(g => g.Session.Events.Publish(new RpHit { sourceId = "hero", targetId = "imp" }));
            Assert.IsEmpty(spikes.log, "가진 쪽(foe)이 맞은 게 아니다");
            Do(g => g.Session.Events.Publish(new RpHit { sourceId = "hero", targetId = "foe" }));
            CollectionAssert.AreEqual(new[] { "가시:foe" }, spikes.log, "사용자 = 가진 쪽");
            Assert.AreEqual(1, Game.Owned("thorns", Unit("foe")));
            Assert.AreEqual(0, Game.Owned("thorns", Unit("hero")));
        }
    }
}
