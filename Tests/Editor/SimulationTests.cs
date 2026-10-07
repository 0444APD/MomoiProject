using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GBible.Tests
{
    [GameEvent("시뮬레이션: 적중")]
    public sealed class SimHit
    {
        public string target = "";
        public int amount;
        public bool critical;
        public List<string> tags = new List<string>();
        public string[] marks = Array.Empty<string>();
    }

    // 아군·적의 공통 부모가 추상 클래스인 데이터.
    [Serializable] public abstract class SimUnit { public string id; public int hp = 30; }
    [Serializable] public sealed class SimHero : SimUnit { }
    [Serializable] public sealed class SimFoe : SimUnit { }
    [Serializable] public sealed class SimParty { public List<SimUnit> units = new List<SimUnit>(); }

    // 참조 필드가 리스트·배열이 아닌 컬렉션(HashSet)인 행동.
    [Serializable]
    public sealed class SimHitAll : GameAction
    {
        public HashSet<SimUnit> targets = new HashSet<SimUnit>();
        protected override void Run()
        {
            foreach (var target in targets) target.hp -= 1;
        }
    }

    // [Chosen] 필드의 형식이 추상 클래스인 선택.
    public sealed class SimAskHeal : GameAction
    {
        protected override void Run() => Game.Ask("치료할 대상", Game.Get<SimParty>("party").units, new SimHeal());
    }

    [Serializable]
    public sealed class SimHeal : GameAction
    {
        [Chosen] public SimUnit target;
        protected override void Run() => target.hp += 5;
    }

    // 모니터의 "이벤트로 반영"이 값을 덮어쓰지 않고 게임과 같은 경로를 타는지.
    public class SimulationTests
    {
        GameSession _session;

        [SetUp]
        public void SetUp()
        {
            _session = new GameSession(new SessionOptions { Seed = 5 });
            _session.Stats.Define(Stat("kills", StatScope.Total, StatAggregate.Sum, nameof(TestKill.Count)));
            _session.Stats.Define(Stat("run_kills", StatScope.Run, StatAggregate.Sum, null));
            _session.Stats.Define(Stat("combo", StatScope.Total, StatAggregate.Max, nameof(TestKill.Combo)));
            _session.Stats.Define(new StatDefinition { Id = "gold", DisplayName = "골드", Scope = StatScope.Total, Aggregate = StatAggregate.Sum });
            _session.Data.Define("player.gold", DataZone.Meta, 0.0, "골드");
        }

        static StatDefinition Stat(string id, StatScope scope, StatAggregate aggregate, string field)
        {
            var definition = new StatDefinition { Id = id, DisplayName = id, Scope = scope, Aggregate = aggregate };
            definition.Rules.Add(new StatRule { eventType = typeof(TestKill).FullName, field = field });
            return definition;
        }

        [Test]
        public void 이벤트로_반영하면_같은_이벤트를_받는_다른_수치도_움직인다()
        {
            Assert.IsTrue(_session.TrySimulateStat("kills", 3, out var description, out var error), error);
            Assert.AreEqual(3, _session.Stats.Get("kills"));
            Assert.AreEqual(1, _session.Stats.Get("run_kills"), "처치 이벤트를 1회 받았으므로 횟수 규칙도 반응한다");
            StringAssert.Contains("Count", description);
            Assert.IsTrue(_session.Journal.Entries.Any(e => e.Kind == JournalKind.Event), "실제 이벤트가 기록된다");
        }

        [Test]
        public void 횟수_규칙은_이벤트를_그_수만큼_쏜다()
        {
            Assert.IsTrue(_session.TrySimulateStat("run_kills", 4, out _, out var error), error);
            Assert.AreEqual(4, _session.Stats.Get("run_kills"));
            Assert.AreEqual(4, _session.Journal.Entries.Count(e => e.Kind == JournalKind.Event));
        }

        [Test]
        public void 최고치_수치는_값을_한_번_기록한다()
        {
            Assert.IsTrue(_session.TrySimulateStat("combo", 7, out _, out var error), error);
            Assert.AreEqual(7, _session.Stats.Get("combo"));
            _session.TrySimulateStat("combo", 3, out _, out _);
            Assert.AreEqual(7, _session.Stats.Get("combo"), "최고치 규칙은 게임과 똑같이 적용된다");
        }

        [Test]
        public void 테스트_이벤트도_Undo할_수_있다()
        {
            _session.TrySimulateStat("kills", 5, out _, out _);
            Assert.AreEqual(5, _session.Stats.Get("kills"));
            Assert.IsTrue(_session.Undo());
            Assert.AreEqual(0, _session.Stats.Get("kills"));
            Assert.AreEqual(ActionStatus.Undone, _session.Actions.Log.Last().Status);
        }

        [Test]
        public void 규칙이_없는_수치는_이유와_함께_거절한다()
        {
            Assert.IsFalse(_session.TrySimulateStat("gold", 10, out _, out var error));
            StringAssert.Contains("반영 규칙이 없어", error);
            Assert.AreEqual(0, _session.Actions.Log.Count, "아무 행동도 실행되지 않는다");
        }

        [Test]
        public void 정수_필드에_소수는_거절한다()
        {
            Assert.IsFalse(_session.TrySimulateStat("kills", 1.5, out _, out var error));
            StringAssert.Contains("정수", error);
        }

        [Test]
        public void 도전과제_조건을_이벤트로_채우면_달성과_보상까지_이어진다()
        {
            var definition = new AchievementDefinition
            {
                Id = "legend",
                DisplayName = "전설",
                Condition = new AllOfCondition
                {
                    conditions =
                    {
                        new StatCompareCondition { stat = "kills", op = CompareOp.AtLeast, value = 10 },
                        new StatCompareCondition { stat = "combo", op = CompareOp.AtLeast, value = 6 },
                    },
                },
            };
            definition.Rewards.Add(new PlayerDataReward { key = "player.gold", amount = 100 });
            _session.Achievements.Define(definition);
            _session.TrySimulateStat("kills", 4, out _, out _);

            Assert.IsTrue(_session.TrySimulateAchievement("legend", out var error), error);
            Assert.IsTrue(_session.Achievements.IsUnlocked("legend"));
            Assert.AreEqual(100, _session.Data.GetNumber("player.gold"));
            Assert.GreaterOrEqual(_session.Stats.Get("kills"), 10);

            var reward = _session.Journal.Entries.Last(e => e.Kind == JournalKind.Reward);
            Assert.AreEqual(JournalKind.Action, _session.Journal.CauseChain(reward.Id)[0].Kind, "보상의 뿌리는 테스트 행동이다");
        }

        [Test]
        public void 이미_만족한_조건은_채우지_않는다()
        {
            _session.Achievements.Define(new AchievementDefinition { Id = "a", DisplayName = "a", Condition = new StatCompareCondition { stat = "kills", value = 0 } });
            Assert.IsFalse(_session.TrySimulateAchievement("a", out var error));
            Assert.IsNotNull(error);
        }

        [Test]
        public void 이벤트_생성기는_이름과_값으로_만든다()
        {
            var evt = (TestKill)EventSimulator.Create(typeof(TestKill), new Dictionary<string, object> { ["Count"] = 3.0, ["Combo"] = 2.0 });
            Assert.AreEqual(3, evt.Count);
            Assert.AreEqual(2, evt.Combo);
            Assert.Throws<ArgumentException>(() => EventSimulator.Create(typeof(TestKill), new Dictionary<string, object> { ["없음"] = 1.0 }));
            CollectionAssert.AreEquivalent(new[] { "Count", "Combo" }, EventSimulator.EditableMembers(typeof(TestKill)).Select(m => m.Name));
        }

        [Test]
        public void 타입을_몰라도_발행되고_구독자가_받는다()
        {
            TestKill received = null;
            _session.Events.Subscribe<TestKill>(e => received = e);
            _session.Events.PublishObject(new TestKill { Count = 9 });
            Assert.AreEqual(9, received?.Count);
        }

        static EventTrigger Hit(FieldFilter filter = null) => new EventTrigger { eventType = typeof(SimHit).FullName, filter = filter };

        void DefineBossHits()
        {
            var boss = new StatDefinition { Id = "boss_hits", DisplayName = "보스 적중", Scope = StatScope.Total, Aggregate = StatAggregate.Sum };
            boss.Rules.Add(new StatRule { eventType = typeof(SimHit).FullName, filter = new FieldFilter { path = nameof(SimHit.tags), op = FieldOp.Contains, value = "boss" } });
            _session.Stats.Define(boss);
        }

        [Test]
        public void 반영_조건이_있는_로그는_조건에_맞춘_이벤트로_움직인다()
        {
            DefineBossHits();
            var damage = new StatDefinition { Id = "dragon_damage", DisplayName = "용 피해", Scope = StatScope.Total, Aggregate = StatAggregate.Sum };
            damage.Triggers.Add(new TriggerRule
            {
                when = Hit(new FieldFilter { path = nameof(SimHit.target), op = FieldOp.Equal, value = "dragon" }),
                then = { new RecordOutcome { field = nameof(SimHit.amount) } },
            });
            _session.Stats.Define(damage);

            Assert.IsTrue(_session.TrySimulateStat("boss_hits", 2, out _, out var error), error);
            Assert.AreEqual(2, _session.Stats.Get("boss_hits"), "조건(태그 boss 포함)에 맞춘 이벤트라 반영된다");
            Assert.IsTrue(_session.TrySimulateStat("dragon_damage", 30, out _, out error), error);
            Assert.AreEqual(30, _session.Stats.Get("dragon_damage"));
        }

        [Test]
        public void 반영할_값이_조건에_걸러지면_이유와_함께_거절한다()
        {
            var big = new StatDefinition { Id = "big_hits", DisplayName = "큰 피해", Scope = StatScope.Total, Aggregate = StatAggregate.Sum };
            big.Rules.Add(new StatRule { eventType = typeof(SimHit).FullName, field = nameof(SimHit.amount), filter = new FieldFilter { path = nameof(SimHit.amount), op = FieldOp.AtLeast, value = "50" } });
            _session.Stats.Define(big);
            Assert.IsTrue(_session.TrySimulateStat("big_hits", 80, out _, out var error), error);
            Assert.AreEqual(80, _session.Stats.Get("big_hits"));

            var actions = _session.Actions.Log.Count;
            Assert.IsFalse(_session.TrySimulateStat("big_hits", 10, out _, out error), "피해 10은 조건(50 이상)에 걸러져 반영되지 않는다");
            StringAssert.Contains("조건", error);
            Assert.AreEqual(actions, _session.Actions.Log.Count, "아무 행동도 실행되지 않는다");
            Assert.AreEqual(80, _session.Stats.Get("big_hits"));
        }

        [Test]
        public void 참거짓_값을_반영하는_로그는_참인_이벤트를_그_수만큼_쏜다()
        {
            var crits = new StatDefinition { Id = "crits", DisplayName = "치명타", Scope = StatScope.Total, Aggregate = StatAggregate.Sum };
            crits.Rules.Add(new StatRule { eventType = typeof(SimHit).FullName, field = nameof(SimHit.critical) });
            _session.Stats.Define(crits);
            Assert.IsTrue(_session.TrySimulateStat("crits", 3, out var description, out var error), error);
            Assert.AreEqual(3, _session.Stats.Get("crits"), "참 하나는 1이므로 3을 더하려면 이벤트 3번");
            StringAssert.Contains("×3", description);

            var everCrit = new StatDefinition { Id = "ever_crit", DisplayName = "치명타 해 봄", Scope = StatScope.Total, Aggregate = StatAggregate.Max };
            everCrit.Rules.Add(new StatRule { eventType = typeof(SimHit).FullName, field = nameof(SimHit.critical) });
            _session.Stats.Define(everCrit);
            Assert.IsFalse(_session.TrySimulateStat("ever_crit", 5, out _, out error), "참/거짓은 5로 기록할 수 없다");
            StringAssert.Contains("0이나 1", error);
            Assert.IsTrue(_session.TrySimulateStat("ever_crit", 1, out _, out error), error);
            Assert.AreEqual(1, _session.Stats.Get("ever_crit"));
        }

        [Test]
        public void 조건이_있는_로그를_보는_도전과제도_트리거로_달성된다()
        {
            DefineBossHits();
            _session.Achievements.Define(new AchievementDefinition { Id = "bossHunter", DisplayName = "보스 사냥꾼", Condition = new StatCompareCondition { stat = "boss_hits", value = 3 } });
            Assert.IsTrue(_session.TrySimulateAchievement("bossHunter", out var error), error);
            Assert.AreEqual(3, _session.Stats.Get("boss_hits"));
            Assert.IsTrue(_session.Achievements.IsUnlocked("bossHunter"));
        }

        [Test]
        public void 다른_도전과제가_조건이면_그_트리거의_이벤트도_쏜다()
        {
            _session.Achievements.Define(new AchievementDefinition { Id = "dragonSlain", DisplayName = "용 처치", When = Hit(new FieldFilter { path = nameof(SimHit.target), op = FieldOp.Equal, value = "dragon" }) });
            _session.Achievements.Define(new AchievementDefinition { Id = "afterDragon", DisplayName = "용 다음", Condition = new AchievementUnlockedCondition { achievement = "dragonSlain" } });
            Assert.IsTrue(_session.TrySimulateAchievement("afterDragon", out var error), error);
            Assert.IsTrue(_session.Achievements.IsUnlocked("dragonSlain"));
            Assert.IsTrue(_session.Achievements.IsUnlocked("afterDragon"));
        }

        [Test]
        public void 같은_도전과제를_두_갈래가_봐도_순환이_아니다()
        {
            _session.Achievements.Define(new AchievementDefinition { Id = "first", DisplayName = "처음", Condition = new StatCompareCondition { stat = "kills", value = 5 } });
            _session.Achievements.Define(new AchievementDefinition { Id = "left", DisplayName = "왼쪽", Condition = new AchievementUnlockedCondition { achievement = "first" } });
            _session.Achievements.Define(new AchievementDefinition { Id = "right", DisplayName = "오른쪽", Condition = new AchievementUnlockedCondition { achievement = "first" } });
            _session.Achievements.Define(new AchievementDefinition
            {
                Id = "both",
                DisplayName = "둘 다",
                Condition = new AllOfCondition { conditions = { new AchievementUnlockedCondition { achievement = "left" }, new AchievementUnlockedCondition { achievement = "right" } } },
            });
            Assert.IsTrue(_session.TrySimulateAchievement("both", out var error), error);
            Assert.AreEqual(5, _session.Stats.Get("kills"), "같은 도전과제의 부족분은 한 번만 채운다");
            Assert.IsTrue(_session.Achievements.IsUnlocked("both"));

            _session.Achievements.Define(new AchievementDefinition { Id = "loopA", DisplayName = "고리 A", Condition = new AchievementUnlockedCondition { achievement = "loopB" } });
            _session.Achievements.Define(new AchievementDefinition { Id = "loopB", DisplayName = "고리 B", Condition = new AchievementUnlockedCondition { achievement = "loopA" } });
            Assert.IsFalse(_session.TrySimulateAchievement("loopA", out error));
            StringAssert.Contains("순환", error);
        }

        [Test]
        public void 조건_OR는_채울_수_있는_갈래로_채운다()
        {
            _session.Achievements.Define(new AchievementDefinition
            {
                Id = "either",
                DisplayName = "둘 중 하나",
                Condition = new AnyOfCondition
                {
                    conditions =
                    {
                        new NotCondition { condition = new StatCompareCondition { stat = "kills", value = 0 } },
                        new StatCompareCondition { stat = "kills", value = 4 },
                    },
                },
            });
            Assert.IsTrue(_session.TrySimulateAchievement("either", out var error), error);
            Assert.AreEqual(4, _session.Stats.Get("kills"));
            Assert.IsTrue(_session.Achievements.IsUnlocked("either"));
        }

        [Test]
        public void 최저치_로그는_기록이_있으면_더_크게_채울_수_없다고_알린다()
        {
            var lowest = new StatDefinition { Id = "lowest_hp", DisplayName = "최저 체력", Scope = StatScope.Run, Aggregate = StatAggregate.Min };
            lowest.Rules.Add(new StatRule { eventType = typeof(SimHit).FullName, field = nameof(SimHit.amount) });
            _session.Stats.Define(lowest);
            _session.Achievements.Define(new AchievementDefinition { Id = "steady", DisplayName = "꿋꿋함", Condition = new StatCompareCondition { stat = "lowest_hp", value = 50 } });
            Assert.IsTrue(_session.TrySimulateStat("lowest_hp", 30, out _, out var error), error);
            Assert.IsFalse(_session.TrySimulateAchievement("steady", out error), "최저 30이 기록된 뒤에는 50을 기록해도 최저치가 오르지 않는다");
            StringAssert.Contains("최저치", error);
            Assert.IsFalse(_session.Achievements.IsUnlocked("steady"));
        }

        [Test]
        public void 배열_필드의_포함_조건도_맞춰_쏜다()
        {
            _session.Achievements.Define(new AchievementDefinition { Id = "marked", DisplayName = "표식", When = Hit(new FieldFilter { path = nameof(SimHit.marks), op = FieldOp.Contains, value = "burn" }) });
            Assert.IsTrue(_session.TrySimulateAchievement("marked", out var error), error);
            Assert.IsTrue(_session.Achievements.IsUnlocked("marked"));
        }

        static GameSession UnitSession()
        {
            var session = new GameSession(new SessionOptions { Seed = 3 });
            session.Data.Define("party", DataZone.Session, new SimParty { units = { new SimHero { id = "a" }, new SimFoe { id = "b" } } });
            return session;
        }

        [Test]
        public void 행동의_HashSet_참조_필드도_실행_직전에_지금_객체로_바뀐다()
        {
            var session = UnitSession();
            var live = session.Game.Find<SimUnit>("a");
            var old = new SimHero { id = "a" };   // Undo·불러오기 뒤의 옛 객체처럼 데이터 밖의 객체
            session.Do(new SimHitAll { targets = { old } });
            Assert.AreEqual(29, live.hp, "id로 지금 데이터의 객체를 찾아 고친다");
            Assert.AreEqual(30, old.hp, "넣은 옛 객체는 건드리지 않는다");
            Assert.IsTrue(session.Undo());
            Assert.AreEqual(30, session.Game.Find<SimUnit>("a").hp);
        }

        [Test]
        public void 예약에_맡긴_행동의_HashSet_참조는_id만_남는다()
        {
            var session = UnitSession();
            var live = session.Game.Find<SimUnit>("a");
            session.Do(new DeveloperEditAction { Edit = () => session.Game.ScheduleAfter(1, new SimHitAll { targets = { live } }) });
            var stored = (SimHitAll)session.Schedules.All.Single().action;
            Assert.IsFalse(ReferenceEquals(stored.targets.Single(), live), "데이터 안의 객체를 예약 칸에 끌어들이지 않는다");
            Assert.AreEqual("a", stored.targets.Single().id);
            session.EndFrame(2);
            Assert.AreEqual(29, session.Game.Find<SimUnit>("a").hp, "실행할 때 지금 객체를 찾아 고친다");
        }

        [Test]
        public void 추상_형식의_Chosen_필드에도_고른_객체가_들어간다()
        {
            var session = new GameSession(new SessionOptions { Seed = 4 });
            session.Data.Define("party", DataZone.Session, new SimParty { units = { new SimHero { id = "hero" }, new SimFoe { id = "foe" } } });
            session.Do(new SimAskHeal());
            session.Game.Answer(session.Game.Choice.id, "foe");
            Assert.AreEqual(35, session.Game.Find<SimUnit>("foe").hp, "id로 지금 객체(SimFoe)를 찾아 넣는다");
            Assert.AreEqual(30, session.Game.Find<SimUnit>("hero").hp);
        }
    }
}
