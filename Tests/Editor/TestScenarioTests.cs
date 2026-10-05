using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GBible.Tests
{
    // 테스트 상황(TestScenarios): 저장 → 판을 바꿈 → 불러오면 저장한 때로, 시드를 주면 난수만 다르게, 시뮬레이션은 시드마다 한 줄.
    public class TestScenarioTests
    {
        [Serializable] public class TsRun { public int hp = 10; public int turn; }
        [Serializable] public class TsProfile { public int wins; }

        sealed class Hit : GameAction
        {
            public int amount;
            protected override void Run()
            {
                var run = Game.Get<TsRun>("run");
                run.hp -= amount + (int)(Game.Random("hit").NextDouble() * 3);
                run.turn++;
            }
        }

        const string Name = "__gbible_test_scenario";
        GameSession _session;

        [SetUp]
        public void SetUp()
        {
            _session = new GameSession(new SessionOptions { Seed = 11 });
            _session.Data.Define("run", DataZone.Session, new TsRun(), "판");
            _session.Data.Define("profile", DataZone.Meta, new TsProfile(), "프로필");
        }

        [TearDown]
        public void TearDown() => TestScenarios.Delete(Name);

        [Test]
        public void 저장한_상황을_불러오면_판과_프로필이_그때로_돌아간다()
        {
            _session.Do(new Hit { amount = 2 });
            _session.Data.Modify<TsProfile>("profile", p => p.wins = 3);
            TestScenarios.Save(_session, Name);
            var hp = _session.Data.Get<TsRun>("run").hp;

            _session.Do(new Hit { amount = 5 });
            _session.Data.Modify<TsProfile>("profile", p => p.wins = 9);
            Assert.IsTrue(TestScenarios.Load(_session, Name));
            Assert.AreEqual(hp, _session.Data.Get<TsRun>("run").hp);
            Assert.AreEqual(1, _session.Data.Get<TsRun>("run").turn);
            Assert.AreEqual(3, _session.Data.Get<TsProfile>("profile").wins);
            Assert.AreEqual(0, _session.UndoCount, "불러오면 Undo 이력은 비워진다");
            CollectionAssert.Contains(TestScenarios.Names(), Name);
        }

        [Test]
        public void 시뮬레이션은_시드마다_한_번씩_돌고_끝나면_상황을_다시_연다()
        {
            TestScenarios.Save(_session, Name);
            var report = TestScenarios.Simulate(_session, Name, 5,
                s =>
                {
                    s.Do(new Hit { amount = 1 });
                    return s.Data.Get<TsRun>("run").hp > 0;
                },
                s => new Dictionary<string, double> { ["turns"] = s.Data.Get<TsRun>("run").turn });
            Assert.AreEqual(5, report.Runs.Count);
            Assert.IsTrue(report.Average("turns") >= 3 && report.Average("turns") <= 10);
            StringAssert.Contains("turns", report.ToCsv());
            Assert.AreEqual(0, _session.Data.Get<TsRun>("run").turn, "끝나면 상황을 처음 시드로 다시 연다");
        }
    }
}
