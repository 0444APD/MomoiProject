using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GBible.Tests
{
    // 능력치·Modifier, 가로채기, 비용.
    public class AttributeInterceptTests
    {
        [Serializable] public class AiUnit { public string id; public double attack = 100; public int hp = 50; public int armor; }
        [Serializable] public class AiParty { public List<AiUnit> units = new List<AiUnit>(); public int gold = 10; public int energy = 3; }

        [Serializable, Intent("피해")] public class AiDamage { public string target; public int amount; public bool cancelled; }

        sealed class Hit : GameAction
        {
            public AiUnit unit;
            public int amount;
            protected override void Run()
            {
                var hit = Game.Intercept(new AiDamage { target = unit.id, amount = amount });
                if (hit.cancelled) return;
                unit.hp -= hit.amount;
            }
        }

        sealed class Buff : GameAction
        {
            public AiUnit unit;
            public double readBack;
            protected override void Run()
            {
                Game.AddModifier(unit, nameof(AiUnit.attack), ModifierKind.Percent, 0.5, "rage");
                readBack = Game.Attribute(unit, nameof(AiUnit.attack));   // 같은 행동 안에서 바로 보인다
            }
        }

        sealed class Spend : GameAction
        {
            public int energy;
            public int gold;
            protected override void Run()
            {
                Game.Pay("party", "gold", gold);
                Game.Pay("party", "energy", energy);
            }
        }

        GameSession _session;
        GameApi Game => _session.Game;
        AiUnit Hero => Game.Find<AiUnit>("hero");

        [SetUp]
        public void SetUp()
        {
            _session = new GameSession(new SessionOptions { Seed = 3 });
            _session.Data.Define("party", DataZone.Session, new AiParty { units = { new AiUnit { id = "hero" }, new AiUnit { id = "foe" } } }, "파티");
        }

        sealed class Edit : GameAction
        {
            public Action<GameApi> run;
            protected override void Run() => run(Game);
        }

        void Do(Action<GameApi> run) => Game.Do(new Edit { run = run });

        [Test]
        public void 같은_칸은_더하고_칸끼리는_곱한다()
        {
            Do(g =>
            {
                g.AddModifier(Hero, nameof(AiUnit.attack), ModifierKind.Flat, 20, "sword");
                g.AddModifier(Hero, nameof(AiUnit.attack), ModifierKind.Percent, 0.2, "ring");
                g.AddModifier(Hero, nameof(AiUnit.attack), ModifierKind.Percent, 0.3, "aura");
                g.AddModifier(Hero, nameof(AiUnit.attack), ModifierKind.Multiply, 0.5, "ultimate");
            });
            // (100 + 20) × (1 + 0.2 + 0.3) × (1 + 0.5) = 270
            Assert.AreEqual(270, Game.Attribute(Hero, nameof(AiUnit.attack)), 1e-9);
            Assert.AreEqual(100, Hero.attack, "기본값은 그대로");
            Assert.AreEqual(100, Game.Attribute("foe", nameof(AiUnit.attack)), 1e-9, "다른 대상에는 붙지 않는다");
        }

        [Test]
        public void 출처로_떼고_Undo로_돌아온다()
        {
            Do(g =>
            {
                g.AddModifier(Hero, nameof(AiUnit.attack), ModifierKind.Flat, 50, "sword");
                g.AddModifier(Hero, nameof(AiUnit.attack), ModifierKind.Flat, 10, "ring");
            });
            Do(g => g.RemoveModifiers("sword", Hero));
            Assert.AreEqual(110, Game.Attribute(Hero, nameof(AiUnit.attack)), 1e-9);
            Assert.IsTrue(_session.Undo());
            Assert.AreEqual(160, Game.Attribute(Hero, nameof(AiUnit.attack)), 1e-9, "Undo하면 뗀 것이 돌아온다");
        }

        [Test]
        public void 같은_행동_안에서_붙인_보정이_바로_보인다()
        {
            var buff = new Buff { unit = Hero };
            Game.Do(buff);
            Assert.AreEqual(150, buff.readBack, 1e-9);
        }

        [Test]
        public void 영구_보정은_메타_구역에_저장된다()
        {
            Do(g => g.AddModifier(Hero, nameof(AiUnit.attack), ModifierKind.Flat, 5, "unlock", permanent: true));
            Assert.AreEqual(DataZone.Meta, _session.Data.ZoneOf(AttributeSystem.MetaKey));
            Assert.AreEqual(105, Game.Attribute(Hero, nameof(AiUnit.attack)), 1e-9);
        }

        [Test]
        public void 없는_숫자_필드에는_붙일_수_없다()
        {
            Assert.Throws<InvalidOperationException>(() => Do(g => g.AddModifier(Hero, "speed", ModifierKind.Flat, 1, "x")));
        }

        static void RegisterIntercept(GameSession session, string owner, int order, IntentOp op, string value, string path = "amount")
        {
            var outcome = new ModifyIntentOutcome { path = path, op = op, value = value };
            session.Triggers.Register(owner, new InterceptTrigger { intentType = typeof(AiDamage).FullName, order = order }, repeat: true,
                fire: moment => session.Triggers.RunOutcomes(new Outcome[] { outcome }, moment, "Test"));
        }

        [Test]
        public void 가로채기는_순서대로_의도를_고친다()
        {
            RegisterIntercept(_session, "half", order: 2, IntentOp.Multiply, "0.5");
            RegisterIntercept(_session, "shield", order: 1, IntentOp.Add, "-4");
            Game.Do(new Hit { unit = Hero, amount = 20 });
            // (20 − 4) × 0.5 = 8
            Assert.AreEqual(42, Hero.hp);
        }

        [Test]
        public void 가로채기로_막으면_일이_일어나지_않는다()
        {
            RegisterIntercept(_session, "immune", order: 0, IntentOp.Cancel, "");
            RegisterIntercept(_session, "after", order: 5, IntentOp.Add, "100");
            Game.Do(new Hit { unit = Hero, amount = 20 });
            Assert.AreEqual(50, Hero.hp, "막힌 뒤의 가로채기는 건너뛴다");
        }

        [Test]
        public void 비용이_모자라면_행동_전체가_취소된다()
        {
            var party = _session.Data.Get<AiParty>("party");
            var e = Assert.Throws<GameFailure>(() => Game.Do(new Spend { gold = 5, energy = 9 }));
            StringAssert.Contains("모자랍니다", e.Message);
            Assert.AreEqual(10, party.gold, "먼저 낸 골드도 돌아온다");
            Game.Do(new Spend { gold = 5, energy = 2 });
            Assert.AreEqual(5, party.gold);
            Assert.AreEqual(1, party.energy);
        }

        [Test]
        public void 없는_필드로_내면_프로그래머_실수로_알린다()
        {
            var e = Assert.Throws<InvalidOperationException>(() => Do(g => g.Pay("party", "gems", 1)));
            StringAssert.Contains("gems", e.Message);
            Assert.IsFalse(e is GameFailure, "플레이어에게 보일 \"모자랍니다\"가 아니다");
        }

        sealed class Collect : IValidationContext
        {
            public readonly List<string> Errors = new List<string>();
            public bool StatExists(string id) => true;
            public bool AchievementExists(string id) => true;
            public StatDefinitionInfo? FindStat(string id) => null;
            public void Report(ValidationSeverity severity, string message)
            {
                if (severity == ValidationSeverity.Error) Errors.Add(message);
            }
        }

        [Test]
        public void 요소마다_펼치는_경로로는_비용을_낼_수_없다()
        {
            var e = Assert.Throws<InvalidOperationException>(() => Do(g => g.Pay("party", "units[].hp", 5)));
            Assert.IsFalse(e is GameFailure, "프로그래머 실수로 알린다");
            StringAssert.Contains("[]", e.Message);
            Assert.AreEqual(50, Game.Find<AiUnit>("hero").hp, "모든 요소를 첫 요소의 값으로 덮지 않는다");
            Assert.AreEqual(50, Game.Find<AiUnit>("foe").hp);
            Assert.Throws<InvalidOperationException>(() => Game.CanPay("party", "units[].hp", 1));
            Assert.Throws<InvalidOperationException>(() => Do(g => g.TryPay("party", "units[].hp", 0)), "양이 0이어도 경로는 본다");

            Do(g => g.Pay("party", "units[foe].hp", 5));
            Assert.AreEqual(45, Game.Find<AiUnit>("foe").hp, "한 요소는 [id]로 고른다");
            Assert.AreEqual(50, Game.Find<AiUnit>("hero").hp);

            var check = new Collect();
            new PayOutcome { key = "party", path = "units[].hp", amount = 1 }.Validate(check);
            Assert.IsTrue(check.Errors.Any(m => m.Contains("[]")), string.Join("\n", check.Errors));
        }

        [Serializable, Intent("막기")] public class AiGuard { public bool parry; public int armor; public bool cancelled; }
        [Serializable, GameEvent("막음")] public class AiParried { public bool parried; public int armor; }

        [Test]
        public void 참거짓은_대소문자와_틀을_가리지_않고_읽지_못하는_글자는_넣지_않는다()
        {
            var seen = new List<bool>();
            _session.Events.Subscribe<AiParried>(e => seen.Add(e.parried));
            foreach (var text in new[] { "True", "TRUE", "참", "{event:parry}" })
            {
                var publish = new PublishEventOutcome { eventType = typeof(AiParried).FullName };
                publish.values.Add(new EventFieldValue { path = "parried", value = text });
                publish.Run(new TriggerContext { Session = _session, Event = new AiGuard { parry = true } });
            }
            CollectionAssert.AreEqual(new[] { true, true, true, true }, seen, "틀이 펼친 True도 참");

            var garbled = new PublishEventOutcome { eventType = typeof(AiParried).FullName };
            garbled.values.Add(new EventFieldValue { path = "parried", value = "yes" });
            garbled.Run(new TriggerContext { Session = _session });
            Assert.IsFalse(seen.Last(), "읽지 못한 글자는 넣지 않는다(기본값)");
            Assert.IsTrue(_session.Journal.Entries.Any(e => e.Kind == JournalKind.Warning && e.Message.Contains("'yes'")), "알린다");
            var check = new Collect();
            garbled.Validate(check);
            Assert.IsTrue(check.Errors.Any(m => m.Contains("yes")), "DB 검증도 잡는다");

            var guard = new AiGuard();
            new ModifyIntentOutcome { path = "parry", op = IntentOp.Set, value = "TRUE" }.Run(new TriggerContext { Session = _session, Event = guard, IsIntercept = true });
            Assert.IsTrue(guard.parry, "의도 고치기도 같은 규칙");
            new ModifyIntentOutcome { path = "parry", op = IntentOp.Set, value = "maybe" }.Run(new TriggerContext { Session = _session, Event = guard, IsIntercept = true });
            Assert.IsTrue(guard.parry, "읽지 못한 글자로 거짓을 넣지 않는다");
            Assert.IsTrue(_session.Journal.Entries.Any(e => e.Kind == JournalKind.Warning && e.Message.Contains("'maybe'")));
            Assert.IsFalse(PlayerDataGrants.TryParse(typeof(bool), "maybe", out _, out _));
            Assert.IsTrue(PlayerDataGrants.TryParse(typeof(bool), " FALSE ", out var parsed, out _) && parsed is bool b && !b);
        }

        [Test]
        public void 정수_필드에_넣는_값은_0점5에서_0에서_먼_쪽으로_반올림한다()
        {
            var party = _session.Data.Get<AiParty>("party");
            Do(g => g.Pay("party", "gold", 3.5));
            Assert.AreEqual(7, party.gold, "10 − 3.5 = 6.5 → 7(짝수 쪽 6이 아니다)");

            RegisterIntercept(_session, "brittle", order: 0, IntentOp.Add, "0.5");
            Game.Do(new Hit { unit = Hero, amount = 4 });
            Assert.AreEqual(50 - 5, Hero.hp, "피해 4 + 0.5 = 4.5 → 5");

            new PlayerDataReward { key = "party", path = "energy", amount = 1.5 }.Grant(_session);
            Assert.AreEqual(5, party.energy, "3 + 1.5 = 4.5 → 5");

            var seen = new List<int>();
            _session.Events.Subscribe<AiParried>(e => seen.Add(e.armor));
            var publish = new PublishEventOutcome { eventType = typeof(AiParried).FullName };
            publish.values.Add(new EventFieldValue { path = "armor", value = "2.5" });
            publish.Run(new TriggerContext { Session = _session });
            CollectionAssert.AreEqual(new[] { 3 }, seen, "이벤트 쏘기의 정수 칸도");
            Assert.AreEqual(3, GameNumbers.Round(2.5));
            Assert.AreEqual(-3, GameNumbers.Round(-2.5));
        }

        [Test]
        public void 비용_지불도_가로챌_수_있다()
        {
            var free = new ModifyIntentOutcome { op = IntentOp.Cancel };
            _session.Triggers.Register("free", new InterceptTrigger { intentType = typeof(CostIntent).FullName }, repeat: true,
                fire: moment => _session.Triggers.RunOutcomes(new Outcome[] { free }, moment, "Test"));
            Game.Do(new Spend { gold = 100, energy = 100 });
            Assert.AreEqual(10, _session.Data.Get<AiParty>("party").gold, "공짜면 내지 않는다");
        }
    }
}
