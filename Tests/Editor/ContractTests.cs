using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GBible.Data;
using GBible.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GBible.Tests
{
    // 계약 테스트: 규칙을 어긴 블록·시스템은 추가되는 순간 여기서 걸린다.
    public class BlockContractTests
    {
        static IEnumerable<Type> ConditionTypes() => BlockTypes.AllConcreteTypes(typeof(Condition));
        static IEnumerable<Type> RewardTypes() => BlockTypes.AllConcreteTypes(typeof(Reward));

        [Test]
        public void 블록_타입이_하나_이상_있다()
        {
            Assert.IsNotEmpty(ConditionTypes());
            Assert.IsNotEmpty(RewardTypes());
        }

        [TestCaseSource(nameof(ConditionTypes))]
        public void 조건_블록_계약(Type type)
        {
            Assert.IsTrue(type.IsDefined(typeof(SerializableAttribute), false), $"{type.Name}: [Serializable]이 필요합니다");
            Assert.IsNotNull(type.GetCustomAttribute<BlockInfoAttribute>(), $"{type.Name}: [BlockInfo]가 필요합니다(메뉴 이름)");
            var instance = (Condition)Activator.CreateInstance(type);
            Assert.IsFalse(string.IsNullOrEmpty(instance.Describe(new FakeNames())), $"{type.Name}: Describe가 비어 있습니다");
            FillSample(instance);
            var keys = new HashSet<string>();
            instance.CollectDependencies(keys);
            Assert.IsTrue(keys.Count > 0 || instance.PollInterval > 0 || instance.AutoDependencies,
                $"{type.Name}: 의존 대상도 확인 주기도 없습니다 — 조용히 동작하지 않는 조건입니다");
        }

        static IEnumerable<Type> AbilityBlockTypes() =>
            BlockTypes.AllConcreteTypes(typeof(Effect)).Concat(BlockTypes.AllConcreteTypes(typeof(TargetSelector)));

        // 액티브 효과 Block(효과·대상): 에디터 메뉴에 나오고, 세션마다 복사되므로 직렬화돼야 한다.
        [TestCaseSource(nameof(AbilityBlockTypes))]
        public void 액티브_효과_블록_계약(Type type)
        {
            Assert.IsTrue(type.IsDefined(typeof(SerializableAttribute), false), $"{type.Name}: [Serializable]이 필요합니다");
            Assert.IsNotNull(type.GetCustomAttribute<BlockInfoAttribute>(), $"{type.Name}: [BlockInfo]가 필요합니다(메뉴 이름)");
            var instance = Activator.CreateInstance(type);
            Assert.IsNotNull(DataModel.Clone(instance, null), $"{type.Name}: 복사할 수 없습니다");
        }

        static IEnumerable<Type> TriggerAndOutcomeTypes() =>
            BlockTypes.AllConcreteTypes(typeof(Trigger)).Concat(BlockTypes.AllConcreteTypes(typeof(Outcome)));

        // 트리거·결과 Block: 메뉴 이름·설명이 있고, 문장이 나오고, 종류를 선언하고, 세션마다 복사되므로 직렬화돼야 한다.
        [TestCaseSource(nameof(TriggerAndOutcomeTypes))]
        public void 트리거_결과_블록_계약(Type type)
        {
            Assert.IsTrue(type.IsDefined(typeof(SerializableAttribute), false), $"{type.Name}: [Serializable]이 필요합니다");
            Assert.IsNotNull(type.GetCustomAttribute<BlockInfoAttribute>(), $"{type.Name}: [BlockInfo]가 필요합니다(메뉴 이름)");
            var instance = Activator.CreateInstance(type);
            Assert.IsNotNull(DataModel.Clone(instance, null), $"{type.Name}: 복사할 수 없습니다");
            if (instance is Outcome outcome)
            {
                Assert.IsFalse(string.IsNullOrEmpty(outcome.Describe(new FakeNames())), $"{type.Name}: Describe가 비어 있습니다");
                Assert.AreNotEqual(OutcomeKinds.None, outcome.Kind, $"{type.Name}: 결과 종류(Kind)를 선언해야 합니다(칸 잠금에 쓰인다)");
            }
            else Assert.IsNotNull(((Trigger)instance).Describe(new FakeNames()), $"{type.Name}: Describe가 null입니다");
        }

        // 트리거·결과 칸이 있는 DB 종류는 모두 같은 표시([TriggerSlot]·[OutcomeSlot])로 잠금을 정한다.
        [Test]
        public void 트리거_결과_칸은_잠금_표시를_가진다()
        {
            foreach (var type in new[] { typeof(StatData), typeof(AchievementData), typeof(AbilityData) })
            {
                var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public).Where(f => !f.IsDefined(typeof(HideInInspector), false)).ToList();
                Assert.IsTrue(fields.Any(f => f.IsDefined(typeof(TriggerSlotAttribute), false)), $"{type.Name}: 트리거 칸");
                Assert.IsTrue(fields.Any(f => f.IsDefined(typeof(OutcomeSlotAttribute), false)), $"{type.Name}: 결과 칸");
            }
        }

        [TestCaseSource(nameof(RewardTypes))]
        public void 보상_블록_계약(Type type)
        {
            Assert.IsTrue(type.IsDefined(typeof(SerializableAttribute), false), $"{type.Name}: [Serializable]이 필요합니다");
            Assert.IsNotNull(type.GetCustomAttribute<BlockInfoAttribute>(), $"{type.Name}: [BlockInfo]가 필요합니다");
            var instance = (Reward)Activator.CreateInstance(type);
            Assert.IsFalse(string.IsNullOrEmpty(instance.Describe(new FakeNames())));
            Assert.IsTrue(Enum.IsDefined(typeof(RewardEffect), instance.Effect), $"{type.Name}: 효과 분류(Effect)를 선언해야 합니다");
        }

        [Test]
        public void 블록은_직렬화_왕복_후에도_같은_문장을_낸다()
        {
            var data = ScriptableObject.CreateInstance<AchievementData>();
            try
            {
                data.condition = new AllOfCondition
                {
                    conditions =
                    {
                        new StatCompareCondition { stat = "kills", op = CompareOp.AtLeast, value = 7 },
                        new NotCondition { condition = new AchievementUnlockedCondition { achievement = "x" } },
                    },
                };
                data.rewards.Add(new PlayerDataReward { key = "bag", path = "items", items = { "template.sword", "template.potion" } });
                var names = new FakeNames();
                var before = data.condition.Describe(names) + data.rewards[0].Describe(names);

                var json = EditorJsonUtility.ToJson(data);
                var copy = ScriptableObject.CreateInstance<AchievementData>();
                EditorJsonUtility.FromJsonOverwrite(json, copy);
                var after = copy.condition.Describe(names) + copy.rewards[0].Describe(names);
                UnityEngine.Object.DestroyImmediate(copy);

                Assert.AreEqual(before, after);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(data);
            }
        }

        // 계약 검사를 위해 참조 필드를 채운다(빈 조건은 의존 대상이 없는 게 정상).
        static void FillSample(Condition condition)
        {
            foreach (var field in condition.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.FieldType == typeof(string)) field.SetValue(condition, "sample");
                else if (typeof(Condition).IsAssignableFrom(field.FieldType))
                    field.SetValue(condition, new StatCompareCondition { stat = "sample" });
                else if (field.FieldType == typeof(List<Condition>))
                    field.SetValue(condition, new List<Condition> { new StatCompareCondition { stat = "sample" } });
            }
        }

        sealed class FakeNames : INameResolver
        {
            public string StatName(string id) => "수치:" + id;
            public string AchievementName(string id) => "도전과제:" + id;
        }
    }

    public class ObservabilityContractTests
    {
        [Test]
        public void 모든_시스템은_관찰_계약을_구현한다()
        {
            var session = new GameSession();
            session.Stats.Define(new StatDefinition { Id = "a", DisplayName = "A", Scope = StatScope.Total, Aggregate = StatAggregate.Sum });
            session.Achievements.Define(new AchievementDefinition { Id = "b", DisplayName = "B", Condition = new StatCompareCondition { stat = "a", value = 1 } });
            foreach (var inspectable in session.Inspectables)
            {
                var root = new InspectNode(inspectable.InspectName);
                inspectable.Inspect(root);
                Assert.IsFalse(string.IsNullOrEmpty(inspectable.InspectName));
                Assert.IsNotEmpty(root.Children, $"{inspectable.InspectName}: 값 트리가 비어 있습니다");
            }
        }

        [Test]
        public void 세션은_기본_디버그_명령을_제공한다()
        {
            var session = new GameSession();
            Assert.IsTrue(session.Debug.All.Any(c => c.Name.Contains("Undo")));
        }
    }
}
