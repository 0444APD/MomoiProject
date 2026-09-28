using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GameSystems.Tests
{
    // 모니터의 "이벤트로 반영"이 값을 덮어쓰지 않고 게임과 같은 경로를 타는지.
    public class SimulationTests
    {
        GameSession _session;

        [SetUp]
        public void SetUp()
        {
            _session = new GameSession(new SessionOptions { AutoCommit = false, Seed = 5 });
            _session.Stats.Define(Stat("kills", StatScope.Total, StatAggregate.Sum, nameof(TestKill.Count)));
            _session.Stats.Define(Stat("run_kills", StatScope.Run, StatAggregate.Sum, null));
            _session.Stats.Define(Stat("combo", StatScope.Total, StatAggregate.Max, nameof(TestKill.Combo)));
            _session.Stats.Define(new StatDefinition { Id = "gold", DisplayName = "골드", Scope = StatScope.Total, Aggregate = StatAggregate.Sum });
        }

        static StatDefinition Stat(string id, StatScope scope, StatAggregate aggregate, string field)
        {
            var definition = new StatDefinition { Id = id, DisplayName = id, Scope = scope, Aggregate = aggregate };
            definition.Rules.Add(new StatRule { eventType = typeof(TestKill).FullName, field = field });
            return definition;
        }

        [Test]
        public void 이벤트로_반영하면_같은_이벤트를_받는_다른_통계도_움직인다()
        {
            Assert.IsTrue(_session.TrySimulateStat("kills", 3, commit: true, out var description, out var error), error);
            Assert.AreEqual(3, _session.Stats.Get("kills"));
            Assert.AreEqual(1, _session.Stats.Get("run_kills"), "처치 이벤트 1회가 발생했으므로 횟수 규칙도 반응한다");
            StringAssert.Contains("Count", description);
            Assert.IsTrue(_session.Journal.Entries.Any(e => e.Kind == JournalKind.Event), "실제 이벤트가 기록된다");
        }

        [Test]
        public void 횟수_규칙은_이벤트를_그_수만큼_발생시킨다()
        {
            Assert.IsTrue(_session.TrySimulateStat("run_kills", 4, commit: false, out _, out var error), error);
            Assert.AreEqual(4, _session.Stats.Get("run_kills"));
            Assert.AreEqual(4, _session.Journal.Entries.Count(e => e.Kind == JournalKind.Event));
        }

        [Test]
        public void 최고치_통계는_값을_한_번_기록한다()
        {
            Assert.IsTrue(_session.TrySimulateStat("combo", 7, commit: true, out _, out var error), error);
            Assert.AreEqual(7, _session.Stats.Get("combo"));
            _session.TrySimulateStat("combo", 3, commit: true, out _, out _);
            Assert.AreEqual(7, _session.Stats.Get("combo"), "최고치 규칙은 게임과 똑같이 적용된다");
        }

        [Test]
        public void 테스트_이벤트도_무를_수_있다()
        {
            _session.TrySimulateStat("kills", 5, commit: false, out _, out _);
            Assert.AreEqual(5, _session.Stats.Get("kills"));
            Assert.IsTrue(_session.Undo());
            Assert.AreEqual(0, _session.Stats.Get("kills"));
            Assert.AreEqual(ActionStatus.Undone, _session.Actions.Log.Last().Status);
        }

        [Test]
        public void 규칙이_없는_통계는_이유와_함께_거절한다()
        {
            Assert.IsFalse(_session.TrySimulateStat("gold", 10, commit: true, out _, out var error));
            StringAssert.Contains("반영 규칙이 없어", error);
            Assert.AreEqual(0, _session.Actions.Log.Count, "아무 행동도 실행되지 않는다");
        }

        [Test]
        public void 정수_필드에_소수는_거절한다()
        {
            Assert.IsFalse(_session.TrySimulateStat("kills", 1.5, commit: true, out _, out var error));
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
            definition.Rewards.Add(new AddToStatReward { stat = "gold", amount = 100 });
            _session.Achievements.Define(definition);
            _session.TrySimulateStat("kills", 4, commit: true, out _, out _);

            Assert.IsTrue(_session.TrySimulateAchievement("legend", commit: true, out var error), error);
            Assert.IsTrue(_session.Achievements.IsUnlocked("legend"));
            Assert.AreEqual(100, _session.Stats.Get("gold"));
            Assert.GreaterOrEqual(_session.Stats.Get("kills"), 10);

            var reward = _session.Journal.Entries.Last(e => e.Kind == JournalKind.Reward);
            Assert.AreEqual(JournalKind.Action, _session.Journal.CauseChain(reward.Id)[0].Kind, "보상의 뿌리는 테스트 행동이다");
        }

        [Test]
        public void 이미_만족한_조건은_채우지_않는다()
        {
            _session.Achievements.Define(new AchievementDefinition { Id = "a", DisplayName = "a", Condition = new StatCompareCondition { stat = "kills", value = 0 } });
            Assert.IsFalse(_session.TrySimulateAchievement("a", commit: false, out var error));
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
    }
}
