using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GameSystems.Data;
using GameSystems.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GameSystems.Tests
{
    // 계약 테스트: 규칙을 어긴 블록·시스템은 추가되는 순간 여기서 걸린다.
    public class BlockContractTests
    {
        static IEnumerable<Type> ConditionTypes() => BlockTypes.ConcreteTypes(typeof(Condition));
        static IEnumerable<Type> RewardTypes() => BlockTypes.ConcreteTypes(typeof(Reward));

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
            Assert.IsTrue(keys.Count > 0 || instance.PollInterval > 0,
                $"{type.Name}: 의존 대상도 확인 주기도 없습니다 — 조용히 동작하지 않는 조건입니다");
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
                data.rewards.Add(new AddToStatReward { stat = "gold", amount = 3 });
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
                Assert.IsNotEmpty(root.Children, $"{inspectable.InspectName}: 상태 트리가 비어 있습니다");
            }
        }

        [Test]
        public void 세션은_기본_디버그_명령을_제공한다()
        {
            var session = new GameSession();
            Assert.IsTrue(session.Debug.All.Any(c => c.Name.Contains("Commit")));
            Assert.IsTrue(session.Debug.All.Any(c => c.Name.Contains("Undo")));
        }
    }
}
