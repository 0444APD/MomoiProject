using System;
using System.Collections.Generic;
using System.Linq;
using GBible.Data;
using NUnit.Framework;
using UnityEngine;

namespace GBible.Tests
{
    [Serializable]
    public sealed class DataFlowKill : GameAction
    {
        public override string Describe() => "처치";
    }

    // 에디터에서 만든 데이터 에셋(StatData·AchievementData)이 런타임 세션에서 그대로 동작하는지.
    public class UnityDataFlowTests
    {
        readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();

        T Make<T>(string id, string name) where T : GameData
        {
            var data = ScriptableObject.CreateInstance<T>();
            data.EditorAssignId(id);
            _created.Add(data);
            return data;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) UnityEngine.Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void 에셋에서_만든_정의로_행동부터_보상까지_이어진다()
        {
            var kills = Make<StatData>("stat1", "처치 수");
            kills.scope = StatScope.Total;
            kills.aggregate = StatAggregate.Sum;
            kills.rules.Add(new StatRule { eventType = typeof(TestKill).FullName, field = nameof(TestKill.Count) });
            var gold = Make<PlayerDataEntry>("playerData1", "골드");
            gold.zone = DataZone.Meta;

            var hunter = Make<AchievementData>("achievement1", "사냥꾼");
            hunter.condition = new StatCompareCondition { stat = kills.Id, op = CompareOp.AtLeast, value = 3 };
            hunter.rewards.Add(new PlayerDataReward { key = gold.Id, amount = 100 });

            var session = new GameSession(new SessionOptions { Seed = 9 });
            session.Stats.Define(kills.ToDefinition());
            gold.DefineIn(session.Data);
            session.Achievements.Define(hunter.ToDefinition());
            session.Actions.Register<DataFlowKill>(_ => session.Events.Publish(new TestKill { Count = 1 }));

            for (int i = 0; i < 3; i++) session.Do(new DataFlowKill());
            Assert.AreEqual(3, session.Stats.Get(kills.Id));
            Assert.IsTrue(session.Achievements.IsUnlocked(hunter.Id));
            Assert.AreEqual(100, session.Data.GetNumber(gold.Id));

            // 인과 사슬: 보상 ← 달성 ← 처치 수 변화 ← 적 처치 이벤트 ← 세 번째 처치 행동.
            var reward = session.Journal.Entries.Last(e => e.Kind == JournalKind.Reward);
            var chain = session.Journal.CauseChain(reward.Id);
            CollectionAssert.AreEqual(
                new[] { JournalKind.Action, JournalKind.Event, JournalKind.Data, JournalKind.Achievement, JournalKind.Reward },
                chain.Select(e => e.Kind).ToArray(),
                string.Join(" → ", chain.Select(e => e.Message)));
        }
    }
}
