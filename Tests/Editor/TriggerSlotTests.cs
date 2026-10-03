using System.Collections.Generic;
using System.Linq;
using GBible.Data;
using GBible.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GBible.Tests
{
    // 트리거·결과 칸: 모든 기능이 같은 칸을 쓰고 잠금만 다르다(검증·예전 형식 옮기기·결과 칸의 이벤트 필드 뿌리).
    public class TriggerSlotTests
    {
        readonly List<Object> _made = new List<Object>();

        T Make<T>(string id) where T : GameData
        {
            var data = ScriptableObject.CreateInstance<T>();
            data.EditorAssignId(id);
            _made.Add(data);
            return data;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _made) Object.DestroyImmediate(o);
            _made.Clear();
        }

        static EventTrigger Kill() => new EventTrigger { eventType = typeof(TestKill).FullName };

        [Test]
        public void 칸의_잠금에_없는_트리거와_결과를_잡는다()
        {
            var achievement = Make<AchievementData>("achievement1");
            achievement.trigger = new AnyTrigger { triggers = { Kill(), new UseTrigger() } };
            var ability = Make<AbilityData>("ability1");
            ability.outcomes.Add(new RecordOutcome());
            var issues = DataValidator.Validate(new List<GameData> { achievement, ability }).Select(i => i.Message).ToList();
            Assert.IsTrue(issues.Any(m => m.Contains("'사용' 트리거를 쓸 수 없습니다")), string.Join("\n", issues));
            Assert.IsTrue(issues.Any(m => m.Contains("'로그에 기록' 결과를 쓸 수 없습니다")), string.Join("\n", issues));
        }

        [Test]
        public void 뜻이_흐린_트리거와_읽을_이벤트가_없는_기록을_잡는다()
        {
            var achievement = Make<AchievementData>("achievement1");
            achievement.trigger = new AllTrigger
            {
                triggers = { new ConditionTrigger { condition = new StatCompareCondition { stat = "stat1", value = 1 } }, new NotTrigger { trigger = Kill() } },
            };
            achievement.outcomes.Add(new RecordOutcome { stat = "stat1", field = nameof(TestKill.Count) });
            var stat = Make<StatData>("stat1");
            var issues = DataValidator.Validate(new List<GameData> { achievement, stat }).Where(i => i.Asset == achievement).Select(i => i.Message).ToList();
            Assert.IsTrue(issues.Any(m => m.Contains("NOT") && m.Contains("이벤트")), string.Join("\n", issues));
            Assert.IsTrue(issues.Any(m => m.Contains("읽을 이벤트가 트리거에 없습니다")), string.Join("\n", issues));
        }

        [Test]
        public void 로그의_결과는_로그에_기록_하나로_고정이다()
        {
            var log = Make<StatData>("stat1");
            log.triggers.Add(new TriggerRule { when = Kill(), then = { new RecordOutcome(), new RecordOutcome() } });
            var issues = DataValidator.Validate(new List<GameData> { log }).Select(i => i.Message).ToList();
            Assert.IsTrue(issues.Any(m => m.Contains("하나로 고정된 칸")), string.Join("\n", issues));

            log.triggers[0].then.RemoveAt(1);
            Assert.IsFalse(DataValidator.Validate(new List<GameData> { log }).Any(i => i.Severity == ValidationSeverity.Error));
        }

        [Test]
        public void 예전_형식은_트리거_결과로_옮겨진다()
        {
            var log = Make<StatData>("stat1");
            log.rules.Add(new StatRule { eventType = typeof(TestKill).FullName, field = nameof(TestKill.Count), multiplier = 2 });
            Assert.IsTrue(log.MigrateLegacy());
            Assert.AreEqual(0, log.rules.Count);
            var rule = log.triggers.Single();
            Assert.AreEqual(typeof(TestKill).FullName, ((EventTrigger)rule.when).eventType);
            Assert.AreEqual(2, ((RecordOutcome)rule.then.Single()).multiplier);

            var achievement = Make<AchievementData>("achievement1");
            achievement.condition = new StatCompareCondition { stat = "stat1", value = 3 };
            achievement.rewards.Add(new PlayerDataReward { key = "gold", amount = 1 });
            Assert.IsTrue(achievement.MigrateLegacy());
            Assert.IsInstanceOf<ConditionTrigger>(achievement.trigger);
            Assert.IsInstanceOf<PlayerDataReward>(achievement.outcomes.Single());
            Assert.IsNull(achievement.condition);

            var ability = Make<AbilityData>("ability1");
            ability.effects.Add(new TbDamage());
            Assert.IsTrue(ability.MigrateLegacy());
            Assert.IsInstanceOf<TargetedOutcome>(ability.outcomes.Single());
            Assert.IsFalse(ability.MigrateLegacy(), "두 번째는 할 일이 없다");
        }

        [Test]
        public void 결과의_반영할_값은_같은_규칙의_트리거_이벤트에서_고른다()
        {
            var log = Make<StatData>("stat1");
            log.triggers.Add(new TriggerRule { when = Kill(), then = { new RecordOutcome() } });
            var so = new SerializedObject(log);
            var field = so.FindProperty("triggers.Array.data[0].then.Array.data[0].field");
            Assert.IsNotNull(field);
            Assert.AreEqual(typeof(TestKill), FieldPathContext.RootType(field, out _));
            Assert.AreEqual(OutcomeKinds.Record, TriggerSlots.OutcomeSlotOf(so.FindProperty("triggers.Array.data[0].then")).Allowed);
            Assert.AreEqual(TriggerKinds.Event, TriggerSlots.TriggerSlotOf(so.FindProperty("triggers.Array.data[0].when")).Allowed);
        }

        [Test]
        public void 같은_칸_찾기가_DB_종류와_기능_목록_안쪽까지_본다()
        {
            var achievement = Make<AchievementData>("achievement1");
            achievement.trigger = Kill();
            achievement.outcomes.Add(new PlayerDataReward { key = "gold", amount = 1 });
            var slots = TriggerSlots.Of(achievement).ToList();
            Assert.IsTrue(slots.Any(s => s.Trigger != null && s.TriggerAllowed == (TriggerKinds.Event | TriggerKinds.Condition)));
            Assert.IsTrue(slots.Any(s => s.Outcomes != null && s.Outcomes.Count == 1));
        }
    }
}
