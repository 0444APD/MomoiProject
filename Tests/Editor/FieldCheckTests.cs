using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GameSystems.Tests
{
    [GameEvent("테스트 몬스터 처치")]
    public sealed class TestMonsterKilled
    {
        public string monster = "";
        public int level;
        public bool elite;
        public TestElement element;
        public List<string> tags = new List<string>();
    }

    [Serializable]
    public sealed class TestLoot
    {
        public string name;
        public int grade;
        public List<string> tags = new List<string>();
    }

    [Serializable]
    public sealed class TestBag
    {
        public TestLoot equipped = new TestLoot();
        public List<TestLoot> items = new List<TestLoot>();
    }

    [Serializable]
    public sealed class LootAction : GameAction
    {
        public override string Describe() => "전리품 획득";
    }

    // 필드 경로와 형식별 비교: 코드 없이 "보스 처치", "전설 아이템 보유" 같은 조건을 조립하는 바탕.
    public class FieldCheckTests
    {
        static TestBag Bag() => new TestBag
        {
            equipped = new TestLoot { name = "검", grade = 3, tags = { "melee" } },
            items =
            {
                new TestLoot { name = "활", grade = 1, tags = { "ranged" } },
                new TestLoot { name = "왕관", grade = 5, tags = { "legendary", "boss" } },
            },
        };

        [Test]
        public void 경로를_따라_타입과_값을_찾는다()
        {
            Assert.AreEqual(typeof(int), FieldPaths.TypeAt(typeof(TestBag), "equipped.grade", out _));
            Assert.AreEqual(typeof(List<string>), FieldPaths.TypeAt(typeof(TestBag), "items[].tags", out _));
            Assert.AreEqual(typeof(string), FieldPaths.TypeAt(typeof(TestBag), "items[].tags[]", out _));
            Assert.IsNull(FieldPaths.TypeAt(typeof(TestBag), "equipped.nope", out var error));
            StringAssert.Contains("nope", error);
            CollectionAssert.AreEqual(new object[] { 1, 5 }, FieldPaths.Resolve(Bag(), "items[].grade").ToArray());
        }

        [Test]
        public void 형식에_맞는_비교만_제공한다()
        {
            CollectionAssert.Contains(FieldOps.For(typeof(int)).ToList(), FieldOp.AtLeast);
            CollectionAssert.DoesNotContain(FieldOps.For(typeof(string)).ToList(), FieldOp.AtLeast);
            CollectionAssert.AreEqual(new[] { FieldOp.IsTrue, FieldOp.IsFalse }, FieldOps.For(typeof(bool)).ToList());
            CollectionAssert.Contains(FieldOps.For(typeof(List<string>)).ToList(), FieldOp.Contains);
        }

        [Test]
        public void 숫자_글자_컬렉션_개수_비교()
        {
            var bag = Bag();
            Assert.IsTrue(FieldOps.Test(bag, "equipped.grade", FieldOp.AtLeast, "3"));
            Assert.IsFalse(FieldOps.Test(bag, "equipped.grade", FieldOp.Greater, "3"));
            Assert.IsTrue(FieldOps.Test(bag, "equipped.name", FieldOp.Equal, "검"));
            Assert.IsTrue(FieldOps.Test(bag, "items[].tags", FieldOp.Contains, "legendary"), "아이템 중 하나라도 legendary 태그");
            Assert.IsFalse(FieldOps.Test(bag, "items[].tags", FieldOp.Contains, "mythic"));
            Assert.IsTrue(FieldOps.Test(bag, "items[].grade", FieldOp.AtLeast, "5"), "하나라도 5등급 이상");
            Assert.IsTrue(FieldOps.Test(bag, "items", FieldOp.CountEqual, "2"));
            Assert.IsTrue(FieldOps.Test(bag, "items", FieldOp.IsNotEmpty, ""));
        }

        [Test]
        public void 검증은_경로_비교_값을_타입_기준으로_확인한다()
        {
            Assert.IsTrue(new FieldCheck { path = "items[].grade", op = FieldOp.AtLeast, value = "2" }.Validate(typeof(TestBag), out _));
            Assert.IsFalse(new FieldCheck { path = "items[].grade", op = FieldOp.Contains, value = "2" }.Validate(typeof(TestBag), out var wrongOp));
            StringAssert.Contains("쓸 수 없습니다", wrongOp);
            Assert.IsFalse(new FieldCheck { path = "equipped.grade", op = FieldOp.AtLeast, value = "많이" }.Validate(typeof(TestBag), out var notNumber));
            StringAssert.Contains("숫자가 아닙니다", notNumber);
            Assert.IsFalse(new FieldCheck { path = "element", op = FieldOp.Equal, value = "Water" }.Validate(typeof(TestMonsterKilled), out var noEnum));
            StringAssert.Contains("없는 값", noEnum);
        }

        [Test]
        public void 수치_규칙은_조건에_맞는_이벤트만_센다()
        {
            var session = new GameSession();
            var bossKills = new StatDefinition { Id = "boss_kills", Scope = StatScope.Total, Aggregate = StatAggregate.Sum };
            bossKills.Rules.Add(new StatRule
            {
                eventType = typeof(TestMonsterKilled).FullName,
                filters = { new FieldCheck { path = "tags", op = FieldOp.Contains, value = "boss" } },
            });
            var eliteFire = new StatDefinition { Id = "elite_fire", Scope = StatScope.Total, Aggregate = StatAggregate.Sum };
            eliteFire.Rules.Add(new StatRule
            {
                eventType = typeof(TestMonsterKilled).FullName,
                filters =
                {
                    new FieldCheck { path = "elite", op = FieldOp.IsTrue },
                    new FieldCheck { path = "element", op = FieldOp.Equal, value = "Fire" },
                },
            });
            session.Stats.Define(bossKills);
            session.Stats.Define(eliteFire);

            session.Events.Publish(new TestMonsterKilled { monster = "슬라임" });
            session.Events.Publish(new TestMonsterKilled { monster = "드래곤", tags = { "boss" }, elite = true, element = TestElement.Fire });
            session.Events.Publish(new TestMonsterKilled { monster = "불 정령", elite = true, element = TestElement.Fire });

            Assert.AreEqual(1, session.Stats.Get("boss_kills"));
            Assert.AreEqual(2, session.Stats.Get("elite_fire"), "조건은 모두 만족해야 한다");
            StringAssert.Contains("tags boss 포함일 때", bossKills.Rules[0].Describe("보스 처치 수"));
        }

        [Test]
        public void 상태_값_검사_조건은_컬렉션_안까지_보고_바뀔_때만_평가한다()
        {
            var session = new GameSession();
            session.State.Define("player.bag", StateZone.Meta, new TestBag());
            session.Actions.Register<LootAction>(_ => session.State.Modify<TestBag>("player.bag",
                bag => bag.items.Add(new TestLoot { name = "왕관", grade = 5, tags = { "legendary" } })));
            session.Achievements.Define(new AchievementDefinition
            {
                Id = "legend_owner",
                DisplayName = "전설 보유",
                Condition = new StateFieldCondition { key = "player.bag", path = "items[].tags", op = FieldOp.Contains, value = "legendary" },
            });

            Assert.IsFalse(session.Achievements.IsUnlocked("legend_owner"));
            session.Do(new LootAction());
            Assert.IsTrue(session.Achievements.IsUnlocked("legend_owner"));
            session.Undo();
            Assert.IsTrue(session.Achievements.IsUnlocked("legend_owner"), "Undo해도 달성은 유지된다");
        }
    }
}
