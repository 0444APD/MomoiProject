using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GameSystems.Tests
{
    [GameEvent("테스트 처치")]
    public sealed class TestKill
    {
        public int Count = 1;
        public int Combo;
    }

    [Serializable]
    public sealed class TestKillAction : GameAction
    {
        public override string Describe() => "테스트 처치 행동";
    }

    [Serializable]
    public sealed class FailingAction : GameAction
    {
        public override string Describe() => "실패하는 행동";
    }

    public class StateStoreTests
    {
        GameSession _session;

        [SetUp]
        public void SetUp()
        {
            _session = new GameSession(new SessionOptions { AutoCommit = false, Seed = 42 });
            _session.State.Define("s:hp", StateZone.Session, 10, "체력");
            _session.State.Define("m:gold", StateZone.Meta, 0, "골드");
            _session.Actions.Register<TestKillAction>(_ =>
            {
                _session.State.Set("s:hp", _session.State.GetNumber("s:hp") - 3);
                _session.State.Set("m:gold", _session.State.GetNumber("m:gold") + 5);
            });
            _session.Actions.Register<FailingAction>(_ =>
            {
                _session.State.Set("s:hp", 1);
                throw new InvalidOperationException("의도된 실패");
            });
        }

        [Test]
        public void 정의되지_않은_키는_조용히_넘어가지_않는다()
        {
            Assert.Throws<InvalidOperationException>(() => _session.State.Set("없는:키", 1));
            Assert.Throws<InvalidOperationException>(() => _session.State.Get("없는:키"));
        }

        [Test]
        public void 저장할_수_없는_값_형식은_거부한다()
        {
            Assert.Throws<ArgumentException>(() => _session.State.Set("s:hp", new List<int>()));
        }

        [Test]
        public void 숫자는_double로_정규화된다()
        {
            _session.State.Set("s:hp", 7);
            Assert.AreEqual(7.0, _session.State.Get("s:hp"));
        }

        [Test]
        public void 세션_변화는_즉시_메타_변화는_확정_시_적용된다()
        {
            _session.Do(new TestKillAction());
            Assert.AreEqual(7, _session.State.GetNumber("s:hp"));
            Assert.AreEqual(5, _session.State.GetNumber("m:gold"), "행동 중 읽기는 대기 중인 값을 본다");
            Assert.AreEqual(0.0, _session.State.GetCommitted("m:gold"), "확정 전에는 메타에 반영되지 않는다");
            _session.Commit();
            Assert.AreEqual(5.0, _session.State.GetCommitted("m:gold"));
        }

        [Test]
        public void 대기_중인_메타_변화는_누적되어_보인다()
        {
            _session.Do(new TestKillAction());
            _session.Do(new TestKillAction());
            Assert.AreEqual(10, _session.State.GetNumber("m:gold"));
            _session.Commit();
            Assert.AreEqual(10.0, _session.State.GetCommitted("m:gold"));
        }

        [Test]
        public void 무르기는_세션을_되돌리고_메타_대기분을_버린다()
        {
            _session.Do(new TestKillAction());
            _session.Do(new TestKillAction());
            Assert.IsTrue(_session.Undo());
            Assert.AreEqual(7, _session.State.GetNumber("s:hp"));
            Assert.AreEqual(5, _session.State.GetNumber("m:gold"));
            Assert.IsTrue(_session.Undo());
            Assert.AreEqual(10, _session.State.GetNumber("s:hp"));
            Assert.AreEqual(0, _session.State.GetNumber("m:gold"));
            Assert.IsFalse(_session.Undo(), "더 무를 행동이 없다");
            _session.Commit();
            Assert.AreEqual(0.0, _session.State.GetCommitted("m:gold"), "무른 행동은 확정되지 않는다");
        }

        [Test]
        public void 확정된_행동은_무를_수_없다()
        {
            _session.Do(new TestKillAction());
            _session.Commit();
            Assert.IsFalse(_session.Undo());
            Assert.AreEqual(7, _session.State.GetNumber("s:hp"));
        }

        [Test]
        public void 행동_실패_시_변경이_모두_취소된다()
        {
            Assert.Throws<InvalidOperationException>(() => _session.Do(new FailingAction()));
            Assert.AreEqual(10, _session.State.GetNumber("s:hp"));
            Assert.AreEqual(ActionStatus.Failed, _session.Actions.Log.Last().Status);
            Assert.AreEqual(0, _session.State.UncommittedCount);
        }

        [Test]
        public void 처리기가_없는_행동은_명확한_오류를_낸다()
        {
            var session = new GameSession();
            var e = Assert.Throws<InvalidOperationException>(() => session.Do(new TestKillAction()));
            StringAssert.Contains("처리기가 없습니다", e.Message);
        }

        [Test]
        public void 행동_상태는_확정과_무르기를_따라간다()
        {
            var first = _session.Do(new TestKillAction());
            var second = _session.Do(new TestKillAction());
            Assert.AreEqual(ActionStatus.Pending, first.Status);
            _session.Undo();
            Assert.AreEqual(ActionStatus.Undone, second.Status);
            _session.Commit();
            Assert.AreEqual(ActionStatus.Committed, first.Status);
        }

        [Test]
        public void 내보내기와_불러오기는_메타_구역만_왕복한다()
        {
            _session.Do(new TestKillAction());
            var json = _session.ExportMeta();

            var loaded = new GameSession();
            loaded.ImportMeta(json);
            loaded.State.Define("m:gold", StateZone.Meta, 0);
            Assert.AreEqual(5, loaded.State.GetNumber("m:gold"), "정의보다 먼저 불러온 값도 정의 시점에 적용된다");
        }
    }

    public class JournalTests
    {
        [Test]
        public void 원인_범위_안의_기록은_원인을_잇는다()
        {
            var journal = new Journal();
            var root = journal.Append(JournalKind.Action, "t", "행동");
            JournalEntry child;
            JournalEntry grandChild;
            using (journal.Cause(root.Id))
            {
                child = journal.Append(JournalKind.Event, "t", "이벤트");
                using (journal.Cause(child.Id)) grandChild = journal.Append(JournalKind.State, "t", "상태");
            }
            var after = journal.Append(JournalKind.System, "t", "무관");

            Assert.AreEqual(root.Id, child.CauseId);
            Assert.AreEqual(child.Id, grandChild.CauseId);
            Assert.AreEqual(0, after.CauseId);
            CollectionAssert.AreEqual(new[] { root.Id, child.Id, grandChild.Id }, journal.CauseChain(grandChild.Id).Select(e => e.Id).ToArray());
            CollectionAssert.AreEqual(new[] { child.Id, grandChild.Id }, journal.Consequences(root.Id).Select(e => e.Id).ToArray());
        }

        [Test]
        public void 링_버퍼는_용량을_넘으면_오래된_것부터_버린다()
        {
            var journal = new Journal(16);
            for (int i = 0; i < 40; i++) journal.Append(JournalKind.System, "t", i.ToString());
            Assert.AreEqual(16, journal.Count);
            Assert.AreEqual("24", journal.Entries.First().Message);
            Assert.IsNull(journal.Find(1));
            Assert.AreEqual("39", journal.Find(40).Message);
        }
    }

    public class RandomTests
    {
        [Test]
        public void SplitMix64_공개_기준값과_일치한다()
        {
            ulong state = 1234567;
            var expected = new ulong[] { 6457827717110365317, 3203168211198807973, 9817491932198370423, 4593380528125082431, 16408922859458223821 };
            foreach (var value in expected) Assert.AreEqual(value, SplitMix64.Next(ref state));
        }

        [Test]
        public void 같은_시드는_같은_순서를_낸다()
        {
            var a = new RandomStreams(99).Get("combat");
            var b = new RandomStreams(99).Get("combat");
            for (int i = 0; i < 100; i++) Assert.AreEqual(a.NextULong(), b.NextULong());
        }

        [Test]
        public void 스트림은_서로_독립이다()
        {
            var plain = new RandomStreams(7);
            var busy = new RandomStreams(7);
            for (int i = 0; i < 50; i++) busy.Get("combat").NextULong();
            Assert.AreEqual(plain.Get("map").NextULong(), busy.Get("map").NextULong(), "전투 난수를 더 써도 맵 난수는 같아야 한다");
        }

        [Test]
        public void 범위와_실수는_경계를_지킨다()
        {
            var stream = new RandomStreams(1).Get("t");
            for (int i = 0; i < 2000; i++)
            {
                var r = stream.Range(-3, 4);
                Assert.That(r, Is.InRange(-3, 3));
                var d = stream.NextDouble();
                Assert.That(d, Is.GreaterThanOrEqualTo(0.0).And.LessThan(1.0));
            }
            Assert.Throws<ArgumentException>(() => stream.Range(5, 5));
        }

        [Test]
        public void 상태_저장과_복원()
        {
            var stream = new RandomStreams(5).Get("t");
            stream.NextULong();
            var saved = stream.GetState();
            var next = stream.NextULong();
            stream.SetState(saved);
            Assert.AreEqual(next, stream.NextULong());
        }

        [Test]
        public void 무르면_난수도_되돌아가_같은_행동이_같은_결과를_낸다()
        {
            var session = new GameSession(new SessionOptions { AutoCommit = false, Seed = 3 });
            var rolls = new List<ulong>();
            session.Actions.Register<TestKillAction>(_ => rolls.Add(session.Random.Get("combat").NextULong()));
            session.Do(new TestKillAction());
            session.Undo();
            session.Do(new TestKillAction());
            Assert.AreEqual(rolls[0], rolls[1]);
        }
    }

    public class StatsAndAchievementTests
    {
        GameSession _session;

        [SetUp]
        public void SetUp()
        {
            _session = new GameSession(new SessionOptions { AutoCommit = false, Seed = 1 });
            var kills = new StatDefinition { Id = "kills", DisplayName = "처치 수", Scope = StatScope.Total, Aggregate = StatAggregate.Sum };
            kills.Rules.Add(new StatRule { eventType = typeof(TestKill).FullName, field = nameof(TestKill.Count) });
            var runKills = new StatDefinition { Id = "run_kills", DisplayName = "이번 판 처치", Scope = StatScope.Run, Aggregate = StatAggregate.Sum };
            runKills.Rules.Add(new StatRule { eventType = typeof(TestKill).FullName });
            var combo = new StatDefinition { Id = "combo", DisplayName = "최고 콤보", Scope = StatScope.Total, Aggregate = StatAggregate.Max };
            combo.Rules.Add(new StatRule { eventType = typeof(TestKill).FullName, field = nameof(TestKill.Combo) });
            var gold = new StatDefinition { Id = "gold", DisplayName = "골드", Scope = StatScope.Total, Aggregate = StatAggregate.Sum };
            _session.Stats.Define(kills);
            _session.Stats.Define(runKills);
            _session.Stats.Define(combo);
            _session.Stats.Define(gold);
            _session.Actions.Register<TestKillAction>(_ => _session.Events.Publish(new TestKill { Count = 1, Combo = 3 }));
        }

        AchievementDefinition Achievement(string id, Condition condition, params Reward[] rewards)
        {
            var definition = new AchievementDefinition { Id = id, DisplayName = id, Condition = condition };
            definition.Rewards.AddRange(rewards);
            _session.Achievements.Define(definition);
            return definition;
        }

        static StatCompareCondition AtLeast(string stat, double value) => new StatCompareCondition { stat = stat, op = CompareOp.AtLeast, value = value };

        [Test]
        public void 이벤트_규칙이_통계를_집계한다()
        {
            _session.Events.Publish(new TestKill { Count = 2, Combo = 5 });
            _session.Events.Publish(new TestKill { Count = 1, Combo = 2 });
            Assert.AreEqual(3, _session.Stats.Get("kills"));
            Assert.AreEqual(2, _session.Stats.Get("run_kills"), "필드가 없으면 횟수를 센다");
            Assert.AreEqual(5, _session.Stats.Get("combo"), "최고치는 유지된다");
        }

        [Test]
        public void 이번_판_통계만_초기화된다()
        {
            _session.Events.Publish(new TestKill());
            _session.Stats.ResetRun();
            Assert.AreEqual(0, _session.Stats.Get("run_kills"));
            Assert.AreEqual(1, _session.Stats.Get("kills"));
        }

        [Test]
        public void 확정_시점에_달성되고_보상이_지급된다()
        {
            Achievement("first", AtLeast("kills", 1), new AddToStatReward { stat = "gold", amount = 50 });
            _session.Do(new TestKillAction());
            _session.EndFrame(0.016);
            Assert.IsFalse(_session.Achievements.IsUnlocked("first"), "확정 전에는 평가하지 않는다");
            _session.Commit();
            Assert.IsTrue(_session.Achievements.IsUnlocked("first"));
            Assert.AreEqual(50, _session.Stats.Get("gold"));
        }

        [Test]
        public void 무른_행동으로는_달성되지_않는다_되감기_파밍_차단()
        {
            Achievement("first", AtLeast("kills", 1));
            _session.Do(new TestKillAction());
            _session.Undo();
            _session.Commit();
            Assert.IsFalse(_session.Achievements.IsUnlocked("first"));
            Assert.AreEqual(0, _session.Stats.Get("kills"));
        }

        [Test]
        public void 관련_조건만_다시_평가한다()
        {
            Achievement("kills10", AtLeast("kills", 10));
            Achievement("gold999", AtLeast("gold", 999));
            var before = _session.Achievements.Watcher.EvaluationCount;
            _session.Do(new TestKillAction());
            _session.Commit();
            Assert.AreEqual(1, _session.Achievements.Watcher.EvaluationCount - before, "처치 수를 보는 조건 하나만 평가된다");
        }

        [Test]
        public void 연쇄_달성()
        {
            Achievement("a", AtLeast("kills", 1), new AddToStatReward { stat = "gold", amount = 100 });
            Achievement("b", AtLeast("gold", 100));
            Achievement("c", new AllOfCondition { conditions = { new AchievementUnlockedCondition { achievement = "a" }, new AchievementUnlockedCondition { achievement = "b" } } });
            _session.Do(new TestKillAction());
            _session.Commit();
            Assert.IsTrue(_session.Achievements.IsUnlocked("a"));
            Assert.IsTrue(_session.Achievements.IsUnlocked("b"));
            Assert.IsTrue(_session.Achievements.IsUnlocked("c"));
        }

        [Test]
        public void 불러온_뒤_이미_넘은_조건은_소급_달성된다()
        {
            _session.Stats.Set("kills", 120);
            Achievement("hunter", AtLeast("kills", 100));
            Assert.IsFalse(_session.Achievements.IsUnlocked("hunter"), "정의만으로는 평가하지 않는다");
            _session.Achievements.EvaluateAll();
            Assert.IsTrue(_session.Achievements.IsUnlocked("hunter"));
        }

        [Test]
        public void 달성_상태는_저장과_불러오기를_거쳐도_유지된다()
        {
            Achievement("first", AtLeast("kills", 1));
            _session.Do(new TestKillAction());
            _session.Commit();
            var json = _session.ExportMeta();

            var next = new GameSession();
            next.ImportMeta(json);
            next.Stats.Define(new StatDefinition { Id = "kills", Scope = StatScope.Total, Aggregate = StatAggregate.Sum });
            next.Achievements.Define(new AchievementDefinition { Id = "first", DisplayName = "first", Condition = AtLeast("kills", 1) });
            Assert.IsTrue(next.Achievements.IsUnlocked("first"));
            Assert.IsFalse(next.Achievements.Watcher.IsWatching("first"), "달성한 항목은 감시하지 않는다");
        }

        [Test]
        public void 진행도()
        {
            var definition = Achievement("k10", AtLeast("kills", 10));
            _session.Stats.Set("kills", 4);
            Assert.IsTrue(_session.Achievements.TryGetProgress(definition.Id, out var current, out var target));
            Assert.AreEqual(4, current);
            Assert.AreEqual(10, target);
        }

        [Test]
        public void 조건_문장은_이름을_채운다()
        {
            var condition = new AllOfCondition { conditions = { AtLeast("kills", 100), new AchievementUnlockedCondition { achievement = "x" } } };
            Assert.AreEqual("처치 수 ≥ 100 그리고 'x' 달성", condition.Describe(_session));
        }
    }

    public class MiniJsonTests
    {
        [Test]
        public void 왕복()
        {
            var value = new Dictionary<string, object>
            {
                ["n"] = 1.5,
                ["b"] = true,
                ["s"] = "따옴표\"와 줄바꿈\n",
                ["z"] = null,
                ["list"] = new List<object> { 1.0, "a" },
                ["map"] = new Dictionary<string, object> { ["k"] = -2.0 },
            };
            var parsed = (Dictionary<string, object>)MiniJson.Deserialize(MiniJson.Serialize(value));
            Assert.AreEqual(1.5, parsed["n"]);
            Assert.AreEqual(true, parsed["b"]);
            Assert.AreEqual("따옴표\"와 줄바꿈\n", parsed["s"]);
            Assert.IsNull(parsed["z"]);
            Assert.AreEqual("a", ((List<object>)parsed["list"])[1]);
            Assert.AreEqual(-2.0, ((Dictionary<string, object>)parsed["map"])["k"]);
        }

        [Test]
        public void 잘못된_JSON은_위치와_함께_실패한다()
        {
            var e = Assert.Throws<FormatException>(() => MiniJson.Deserialize("{\"a\": }"));
            StringAssert.Contains("JSON 오류", e.Message);
        }
    }
}
