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
            _session = new GameSession(new SessionOptions { Seed = 42 });
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
        public void 행동은_실행되는_즉시_반영된다()
        {
            _session.Do(new TestKillAction());
            Assert.AreEqual(7, _session.State.GetNumber("s:hp"));
            Assert.AreEqual(5, _session.State.GetNumber("m:gold"), "메타도 즉시 반영된다");
            Assert.AreEqual(1, _session.UndoCount);
        }

        [Test]
        public void 무르기는_세션과_메타를_함께_되돌린다()
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
        }

        [Test]
        public void 이미_한_행동은_언제든_무를_수_있다()
        {
            for (int i = 0; i < 3; i++) _session.Do(new TestKillAction());
            _session.EndFrame(1);
            _session.ExportMeta();
            Assert.AreEqual(3, _session.UndoCount, "저장·시간 경과와 상관없이 이력이 남는다");
            while (_session.Undo()) { }
            Assert.AreEqual(10, _session.State.GetNumber("s:hp"));
            Assert.AreEqual(0, _session.State.GetNumber("m:gold"));
        }

        [Test]
        public void 이력_한도를_넘으면_오래된_행동부터_무를_수_없게_된다()
        {
            var session = new GameSession(new SessionOptions { HistoryLimit = 2 });
            session.State.Define("s:n", StateZone.Session, 0);
            session.Actions.Register<TestKillAction>(_ => session.State.Set("s:n", session.State.GetNumber("s:n") + 1));
            for (int i = 0; i < 5; i++) session.Do(new TestKillAction());
            Assert.AreEqual(2, session.UndoCount);
            while (session.Undo()) { }
            Assert.AreEqual(3, session.State.GetNumber("s:n"));
        }

        [Test]
        public void 되돌리기를_끄면_기록하지_않는다()
        {
            var session = new GameSession(new SessionOptions { History = HistoryMode.None });
            session.State.Define("s:n", StateZone.Session, 0);
            session.Actions.Register<TestKillAction>(_ => session.State.Set("s:n", 1));
            session.Do(new TestKillAction());
            Assert.AreEqual(0, session.UndoCount);
            Assert.IsFalse(session.Undo());
            Assert.AreEqual(1, session.State.GetNumber("s:n"));
        }
        [Test]
        public void 행동_실패_시_변경이_모두_취소된다()
        {
            Assert.Throws<InvalidOperationException>(() => _session.Do(new FailingAction()));
            Assert.AreEqual(10, _session.State.GetNumber("s:hp"));
            Assert.AreEqual(ActionStatus.Failed, _session.Actions.Log.Last().Status);
            Assert.AreEqual(0, _session.State.HistoryCount);
        }

        [Test]
        public void 처리기가_없는_행동은_명확한_오류를_낸다()
        {
            var session = new GameSession();
            var e = Assert.Throws<InvalidOperationException>(() => session.Do(new TestKillAction()));
            StringAssert.Contains("처리기가 없습니다", e.Message);
        }

        [Test]
        public void 행동_상태는_무르기를_따라간다()
        {
            var first = _session.Do(new TestKillAction());
            var second = _session.Do(new TestKillAction());
            Assert.AreEqual(ActionStatus.Done, first.Status);
            _session.Undo();
            Assert.AreEqual(ActionStatus.Undone, second.Status);
            Assert.AreEqual(ActionStatus.Done, first.Status);
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
            var session = new GameSession(new SessionOptions { Seed = 3 });
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
            _session = new GameSession(new SessionOptions { Seed = 1 });
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
        public void 이벤트_규칙이_수치를_집계한다()
        {
            _session.Events.Publish(new TestKill { Count = 2, Combo = 5 });
            _session.Events.Publish(new TestKill { Count = 1, Combo = 2 });
            Assert.AreEqual(3, _session.Stats.Get("kills"));
            Assert.AreEqual(2, _session.Stats.Get("run_kills"), "필드가 없으면 횟수를 센다");
            Assert.AreEqual(5, _session.Stats.Get("combo"), "최고치는 유지된다");
        }

        [Test]
        public void 이번_판_수치만_초기화된다()
        {
            _session.Events.Publish(new TestKill());
            _session.Stats.ResetRun();
            Assert.AreEqual(0, _session.Stats.Get("run_kills"));
            Assert.AreEqual(1, _session.Stats.Get("kills"));
        }

        [Test]
        public void 달성과_보상은_행동_직후_바로_일어난다()
        {
            Achievement("first", AtLeast("kills", 1), new AddToStatReward { stat = "gold", amount = 50 });
            _session.Do(new TestKillAction());
            Assert.IsTrue(_session.Achievements.IsUnlocked("first"));
            Assert.AreEqual(50, _session.Stats.Get("gold"));
        }

        [Test]
        public void 무르기는_행동이_바꾼_값만_되돌리고_달성과_보상은_유지된다()
        {
            Achievement("first", AtLeast("kills", 1), new AddToStatReward { stat = "gold", amount = 50 });
            _session.Do(new TestKillAction());
            _session.Undo();
            Assert.IsTrue(_session.Achievements.IsUnlocked("first"), "한 번 달성한 것은 무르기로 취소되지 않는다");
            Assert.AreEqual(0, _session.Stats.Get("kills"), "행동이 바꾼 값은 되돌아간다");
            Assert.AreEqual(50, _session.Stats.Get("gold"), "보상은 유지된다");
            _session.Do(new TestKillAction());
            Assert.AreEqual(50, _session.Stats.Get("gold"), "다시 해도 보상이 또 나오지 않는다");
        }

        [Test]
        public void 무르기를_반복해도_값은_쌓이지_않는다_파밍_차단()
        {
            Achievement("hunter", AtLeast("kills", 3));
            for (int i = 0; i < 10; i++)
            {
                _session.Do(new TestKillAction());
                _session.Undo();
            }
            Assert.AreEqual(0, _session.Stats.Get("kills"));
            Assert.IsFalse(_session.Achievements.IsUnlocked("hunter"), "한 번의 행동으로 닿지 않는 목표는 무르기 반복으로 달성할 수 없다");
        }

        [Test]
        public void 스냅샷_방식은_같은_Undo로_되감고_달성과_보상은_유지한다()
        {
            var session = new GameSession(new SessionOptions { History = HistoryMode.Snapshots, SnapshotInterval = 1, Seed = 1 });
            var kills = new StatDefinition { Id = "kills", Scope = StatScope.Total, Aggregate = StatAggregate.Sum };
            kills.Rules.Add(new StatRule { eventType = typeof(TestKill).FullName, field = nameof(TestKill.Count) });
            session.Stats.Define(kills);
            session.Stats.Define(new StatDefinition { Id = "gold", Scope = StatScope.Total, Aggregate = StatAggregate.Sum });
            var definition = new AchievementDefinition { Id = "first", DisplayName = "first", Condition = AtLeast("kills", 2) };
            definition.Rewards.Add(new AddToStatReward { stat = "gold", amount = 50 });
            session.Achievements.Define(definition);
            session.Actions.Register<TestKillAction>(_ => session.Events.Publish(new TestKill { Count = 1 }));

            session.EndFrame(0.5);                         // 첫 스냅샷(처치 0)
            session.Do(new TestKillAction());
            session.EndFrame(1.0);                         // 스냅샷(처치 1)
            session.Do(new TestKillAction());               // 처치 2 → 달성, 골드 +50
            session.Random.Get("loot").NextULong();
            session.EndFrame(0.2);
            Assert.AreEqual(0, session.State.HistoryCount, "스냅샷 방식은 행동별 기록을 남기지 않는다");
            Assert.AreEqual(2, session.UndoCount);

            Assert.IsTrue(session.Undo());
            Assert.AreEqual(1, session.Stats.Get("kills"), "행동이 바꾼 값은 스냅샷 시점으로 돌아간다");
            Assert.AreEqual(1.5, session.Clock.Time, 1e-9, "시간도 되감긴다");
            Assert.IsTrue(session.Achievements.IsUnlocked("first"), "되감은 구간의 달성은 유지된다");
            Assert.AreEqual(50, session.Stats.Get("gold"), "보상도 유지된다");
            Assert.AreEqual(ActionStatus.Undone, session.Actions.Log.Last().Status);

            Assert.IsTrue(session.Undo());
            Assert.AreEqual(0, session.Stats.Get("kills"));
            Assert.IsFalse(session.Undo());
        }
        [Test]
        public void 관련_조건만_다시_평가한다()
        {
            Achievement("kills10", AtLeast("kills", 10));
            Achievement("gold999", AtLeast("gold", 999));
            var before = _session.Achievements.Watcher.EvaluationCount;
            _session.Do(new TestKillAction());
            Assert.AreEqual(1, _session.Achievements.Watcher.EvaluationCount - before, "처치 수를 보는 조건 하나만 평가된다");
        }

        [Test]
        public void 연쇄_달성()
        {
            Achievement("a", AtLeast("kills", 1), new AddToStatReward { stat = "gold", amount = 100 });
            Achievement("b", AtLeast("gold", 100));
            Achievement("c", new AllOfCondition { conditions = { new AchievementUnlockedCondition { achievement = "a" }, new AchievementUnlockedCondition { achievement = "b" } } });
            _session.Do(new TestKillAction());
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
            Assert.AreEqual("처치 수 100 이상 그리고 x 달성", condition.Describe(_session));
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
