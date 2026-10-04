using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GBible.Tests
{
    // 트리거·결과: 로그·도전과제·능력이 같은 두 칸(트리거 AND·OR 중첩 + 결과 목록)으로 돈다.
    [GameEvent("보스 처치")]
    public sealed class TgBossKilled
    {
        public int Damage;
        public string Boss;
    }

    // 코딩한 결과: 트리거가 된 이벤트의 값을 본다.
    [Serializable, BlockInfo("처치 피해만큼 골드")]
    public sealed class TgGoldFromEvent : Reward
    {
        public override RewardEffect Effect => RewardEffect.Reversible;
        public override string Describe(INameResolver names) => "처치 피해만큼 골드";

        public override void Grant(GameSession session)
        {
            var damage = Context?.Event is TgBossKilled killed ? killed.Damage : 0;
            session.Data.Set("gold", session.Data.GetNumber("gold") + damage);
        }
    }

    // 사용 조건: 사용자가 살아 있을 때만(트리거 순간의 사용자를 본다).
    [Serializable, BlockInfo("사용자 체력 있음")]
    public sealed class TgUserAlive : SimpleCondition
    {
        protected override bool Check() => Context?.User is TbUnit unit && unit.hp > 0;
    }

    public class TriggerTests
    {
        GameSession _session;

        [SetUp]
        public void SetUp()
        {
            _session = new GameSession(new SessionOptions { Seed = 1 });
            _session.Data.Define("gold", DataZone.Meta, 0.0);
            _session.Data.Define("board", DataZone.Session, new TbBoard
            {
                units =
                {
                    new TbUnit { id = "hero", team = "ally" },
                    new TbUnit { id = "orc", team = "enemy", x = 1 },
                },
            });
        }

        static EventTrigger Boss(string name = null) => new EventTrigger
        {
            eventType = typeof(TgBossKilled).FullName,
            filter = name == null ? null : new FieldFilter { path = nameof(TgBossKilled.Boss), op = FieldOp.Equal, value = name },
        };

        static ConditionTrigger GoldAtLeast(double amount) => new ConditionTrigger
        {
            condition = new PlayerDataCondition { key = "gold", filter = new FieldFilter { path = "", op = FieldOp.AtLeast, value = amount.ToString() } },
        };

        void Kill(string boss, int damage) => _session.Events.Publish(new TgBossKilled { Boss = boss, Damage = damage });

        [Test]
        public void 로그는_이벤트_받기_트리거와_로그에_기록_결과로_돈다()
        {
            var log = new StatDefinition { Id = "bossDamage", Aggregate = StatAggregate.Sum };
            log.Triggers.Add(new TriggerRule { when = Boss("dragon"), then = { new RecordOutcome { field = nameof(TgBossKilled.Damage), multiplier = 2 } } });
            _session.Stats.Define(log);
            Kill("dragon", 10);
            Kill("slime", 99);
            Kill("dragon", 5);
            Assert.AreEqual(30, _session.Stats.Get("bossDamage"), "dragon만, 피해 × 2");
        }

        [Test]
        public void 예전_짧은_규칙도_같은_길로_돈다()
        {
            var log = new StatDefinition { Id = "kills", Aggregate = StatAggregate.Sum };
            log.Rules.Add(new StatRule { eventType = typeof(TgBossKilled).FullName });
            _session.Stats.Define(log);
            Kill("a", 1);
            Kill("b", 1);
            Assert.AreEqual(2, _session.Stats.Get("kills"));
        }

        [Test]
        public void AND는_이벤트를_받은_순간_조건도_참이어야_발동한다()
        {
            var achievement = new AchievementDefinition { Id = "richSlayer", When = new AllTrigger { triggers = { Boss("dragon"), GoldAtLeast(100) } } };
            achievement.Outcomes.Add(new TgGoldFromEvent());
            _session.Achievements.Define(achievement);

            Kill("dragon", 7);
            Assert.IsFalse(_session.Achievements.IsUnlocked("richSlayer"), "골드가 모자란 채 처치");
            _session.Data.Set("gold", 100.0);
            _session.Achievements.Flush();
            Assert.IsFalse(_session.Achievements.IsUnlocked("richSlayer"), "조건만 참이 된 순간은 이벤트가 없다");
            Kill("dragon", 7);
            Assert.IsTrue(_session.Achievements.IsUnlocked("richSlayer"));
            Assert.AreEqual(107, _session.Data.GetNumber("gold"), "결과가 트리거의 이벤트 값을 봤다");
        }

        [Test]
        public void OR는_이벤트나_조건_중_먼저_오는_쪽으로_발동한다()
        {
            _session.Achievements.Define(new AchievementDefinition { Id = "either", When = new AnyTrigger { triggers = { Boss("dragon"), GoldAtLeast(50) } } });
            _session.Data.Set("gold", 60.0);
            _session.Achievements.Flush();
            Assert.IsTrue(_session.Achievements.IsUnlocked("either"), "조건 쪽");
            Assert.IsFalse(_session.Triggers.IsRegistered("either"), "한 번 발동하면 감시하지 않는다");
        }

        [Test]
        public void 반복_트리거의_조건은_거짓이_된_뒤에_다시_발동한다()
        {
            var fired = 0;
            _session.Triggers.Register("watch", GoldAtLeast(10), repeat: true, fire: _ => fired++);
            _session.Data.Set("gold", 20.0);
            _session.Triggers.Flush();
            _session.Data.Set("gold", 30.0);
            _session.Triggers.Flush();
            Assert.AreEqual(1, fired, "참인 동안 다시 발동하지 않는다");
            _session.Data.Set("gold", 0.0);
            _session.Triggers.Flush();
            _session.Data.Set("gold", 15.0);
            _session.Triggers.Flush();
            Assert.AreEqual(2, fired, "거짓이 된 뒤 다시 참");
        }

        [Test]
        public void 능력은_사용_트리거와_사용_조건을_AND로_보고_결과를_실행한다()
        {
            var strike = new AbilityDefinition { Id = "strike", When = new AllTrigger { triggers = { new UseTrigger(), new ConditionTrigger { condition = new TgUserAlive() } } } };
            strike.Outcomes.Add(new TargetedOutcome { effects = { new TbDamage { amount = 4 } } });
            strike.Outcomes.Add(new PlayerDataReward { key = "gold", amount = 3 });
            _session.Abilities.Define(strike);
            var game = _session.Game;

            game.Use("strike", game.Find<TbUnit>("hero"), game.Find<TbUnit>("orc"));
            Assert.AreEqual(26, game.Find<TbUnit>("orc").hp);
            Assert.AreEqual(3, _session.Data.GetNumber("gold"));

            game.Find<TbUnit>("hero").hp = 0;
            Assert.Throws<GameFailure>(() => game.Use("strike", game.Find<TbUnit>("hero"), game.Find<TbUnit>("orc")), "사용 조건 거짓");
            Assert.AreEqual(26, game.Find<TbUnit>("orc").hp, "실패한 사용은 아무것도 바꾸지 않는다");
        }

        [Test]
        public void 같은_이벤트면_로그가_먼저_오르고_조건이_있는_트리거가_그_값을_본다()
        {
            // 도전과제를 먼저 정의해도(등록 순서와 상관없이) 로그 기록이 먼저.
            _session.Achievements.Define(new AchievementDefinition
            {
                Id = "third",
                When = new AllTrigger { triggers = { Boss(), new ConditionTrigger { condition = new StatCompareCondition { stat = "kills", value = 3 } } } },
            });
            var log = new StatDefinition { Id = "kills", Aggregate = StatAggregate.Sum };
            log.Triggers.Add(new TriggerRule { when = Boss(), then = { new RecordOutcome() } });
            _session.Stats.Define(log);
            Kill("a", 1);
            Kill("b", 1);
            Assert.IsFalse(_session.Achievements.IsUnlocked("third"));
            Kill("c", 1);
            Assert.IsTrue(_session.Achievements.IsUnlocked("third"), "세 번째 처치 순간에 처치 수가 이미 3");
        }

        // 결과가 자기 트리거의 이벤트를 다시 쏜다(잘못 만든 데이터).
        [Serializable, BlockInfo("보스 처치 다시 알리기")]
        public sealed class TgEcho : Reward
        {
            public override RewardEffect Effect => RewardEffect.Reversible;
            public override string Describe(INameResolver names) => "다시 알리기";
            public override void Grant(GameSession session) => session.Events.Publish(new TgBossKilled { Boss = "echo" });
        }

        [Test]
        public void 자기_이벤트를_다시_부르는_사슬은_한도에서_끊긴다()
        {
            var fired = 0;
            _session.Triggers.Register("loop", Boss(), repeat: true, fire: moment =>
            {
                fired++;
                _session.Triggers.RunOutcomes(new Outcome[] { new TgEcho() }, moment, "test");
            });
            Assert.DoesNotThrow(() => Kill("start", 1));
            Assert.AreEqual(TriggerSystem.MaxDepth, fired);
            Assert.IsTrue(_session.Journal.Entries.Any(e => e.Kind == JournalKind.Warning && e.Message.Contains("트리거 사슬")));
        }

        [Test]
        public void CanUse는_사용_조건만_보고_아무것도_바꾸지_않는다()
        {
            var strike = new AbilityDefinition { Id = "strike", When = new AllTrigger { triggers = { new UseTrigger(), new ConditionTrigger { condition = new TgUserAlive() } } } };
            strike.Outcomes.Add(new PlayerDataReward { key = "gold", amount = 3 });
            _session.Abilities.Define(strike);
            var game = _session.Game;
            Assert.IsTrue(game.CanUse("strike", game.Find<TbUnit>("hero")));
            game.Find<TbUnit>("hero").hp = 0;
            Assert.IsFalse(game.CanUse("strike", "hero", out var why));
            StringAssert.Contains("사용 조건", why);
            Assert.IsFalse(game.CanUse("none", "hero", out why));
            Assert.AreEqual(0, _session.Data.GetNumber("gold"));
        }

        [Test]
        public void 진행도는_AND면_가장_덜_된_조건이다()
        {
            var log = new StatDefinition { Id = "kills", Aggregate = StatAggregate.Sum };
            log.Triggers.Add(new TriggerRule { when = Boss(), then = { new RecordOutcome() } });
            _session.Stats.Define(log);
            var damage = new StatDefinition { Id = "damage", Aggregate = StatAggregate.Sum };
            damage.Triggers.Add(new TriggerRule { when = Boss(), then = { new RecordOutcome { field = nameof(TgBossKilled.Damage) } } });
            _session.Stats.Define(damage);
            _session.Achievements.Define(new AchievementDefinition
            {
                Id = "both",
                When = new AllTrigger
                {
                    triggers =
                    {
                        new ConditionTrigger { condition = new StatCompareCondition { stat = "kills", value = 10 } },
                        new ConditionTrigger { condition = new StatCompareCondition { stat = "damage", value = 100 } },
                    },
                },
            });
            Kill("a", 80);
            Assert.IsTrue(_session.Achievements.TryGetProgress("both", out var current, out var target));
            Assert.AreEqual(1, current, "처치 1/10이 피해 80/100보다 덜 됐다");
            Assert.AreEqual(10, target);
        }

        [Test]
        public void 트리거로_달성은_조건을_채우고_값을_맞춘_이벤트를_쏜다()
        {
            var log = new StatDefinition { Id = "kills", Aggregate = StatAggregate.Sum };
            log.Triggers.Add(new TriggerRule { when = Boss(), then = { new RecordOutcome() } });
            _session.Stats.Define(log);
            _session.Achievements.Define(new AchievementDefinition
            {
                Id = "dragonVeteran",
                When = new AllTrigger { triggers = { Boss("dragon"), new ConditionTrigger { condition = new StatCompareCondition { stat = "kills", value = 3 } } } },
            });
            Assert.IsTrue(_session.TrySimulateAchievement("dragonVeteran", out var error), error);
            Assert.IsTrue(_session.Achievements.IsUnlocked("dragonVeteran"));
            Assert.IsTrue(_session.Undo(), "한 행동");
            Assert.IsTrue(_session.Achievements.IsUnlocked("dragonVeteran"), "달성은 유지");

            _session.Achievements.Define(new AchievementDefinition { Id = "used", When = new UseTrigger() });
            Assert.IsFalse(_session.TrySimulateAchievement("used", out error));
            StringAssert.Contains("사용", error);
        }

        [Serializable, PlayerData]
        public class TgBag
        {
            public Dictionary<string, int> counts = new Dictionary<string, int>();
            public Dictionary<string, string> names = new Dictionary<string, string>();
        }

        [Test]
        public void 딕셔너리에는_키와_값을_넣는다_숫자는_증감_나머지는_덮어쓰기()
        {
            _session.Data.Define("bag", DataZone.Session, new TgBag { counts = { ["potion"] = 2 }, names = { ["a"] = "old" } });
            new PlayerDataReward { key = "bag", path = "counts", keys = { "potion", "elixir" }, items = { "3", "1" } }.Grant(_session);
            new PlayerDataReward { key = "bag", path = "names", keys = { "a", "b" }, items = { "new", "fresh" } }.Grant(_session);
            var bag = _session.Data.Get<TgBag>("bag");
            Assert.AreEqual(5, bag.counts["potion"], "있던 키는 더한다");
            Assert.AreEqual(1, bag.counts["elixir"], "없던 키는 0에서");
            Assert.AreEqual("new", bag.names["a"], "숫자가 아니면 덮어쓴다");
            Assert.AreEqual("fresh", bag.names["b"]);
            Assert.AreEqual(GrantKind.Dictionary, PlayerDataGrants.KindOf(typeof(Dictionary<string, int>)));

            var problems = new List<string>();
            new PlayerDataReward { key = "bag", path = "counts", keys = { "x", "x" }, items = { "1", "abc" } }.Validate(typeof(TgBag), problems);
            Assert.IsTrue(problems.Any(p => p.Contains("두 번")), string.Join("\n", problems));
            Assert.IsTrue(problems.Any(p => p.Contains("숫자가 아닙니다")), string.Join("\n", problems));
        }

        [Test]
        public void 트리거와_결과는_문장으로_읽힌다()
        {
            var rule = new TriggerRule { when = new AllTrigger { triggers = { Boss("dragon"), GoldAtLeast(5) } }, then = { new RecordOutcome { field = "Damage" } } };
            var text = rule.Natural(_session);
            StringAssert.Contains("보스 처치 받기", text);
            StringAssert.Contains("그리고", text);
            StringAssert.Contains("Damage", text);
            Assert.AreEqual(TriggerKinds.Event | TriggerKinds.Condition, rule.when.Kinds());
            CollectionAssert.AreEqual(new[] { typeof(TgBossKilled) }, rule.when.EventTypes().ToArray());
        }
    }
}
