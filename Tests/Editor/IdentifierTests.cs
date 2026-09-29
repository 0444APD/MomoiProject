using System.Collections.Generic;
using System.Linq;
using GameSystems.Data;
using GameSystems.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GameSystems.Tests
{
    // 식별자: 한 곳(데이터베이스)에서 짓고, 나머지는 목록 선택·생성 코드로만 쓴다.
    public class IdentifierTests
    {
        readonly List<Object> _created = new List<Object>();

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
            foreach (var o in _created) Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void 형식에_맞지_않는_ID를_잡는다()
        {
            var bad = Make<StatData>("Kills Total", "처치");
            var issues = DataValidator.Validate(new List<GameData> { bad });
            Assert.IsTrue(issues.Any(i => i.Asset == bad && i.Message.Contains("ID 'Kills Total'")));
        }

        [Test]
        public void 없는_그룹을_가리키면_오류_그룹을_만들면_통과()
        {
            var boss = Make<KeywordData>("boss", "보스");
            boss.group = "monsterTag";
            var issues = DataValidator.Validate(new List<GameData> { boss });
            Assert.IsTrue(issues.Any(i => i.Asset == boss && i.Severity == ValidationSeverity.Error && i.Message.Contains("'monsterTag'")));

            var group = Make<KeywordGroupData>("monsterTag", "몬스터 태그");
            issues = DataValidator.Validate(new List<GameData> { boss, group });
            Assert.IsFalse(issues.Any(i => i.Asset == boss && i.Severity == ValidationSeverity.Error));
        }

        [Test]
        public void 참조_필드를_모두_찾는다()
        {
            var boss = Make<KeywordData>("boss", "보스");
            boss.group = "monsterTag";
            var found = new List<(string value, System.Type type)>();
            EditorDatabase.ForEachReference(boss, (property, type) => found.Add((property.stringValue, type)));
            CollectionAssert.AreEqual(new[] { "monsterTag" }, found.Select(f => f.value));
            Assert.IsTrue(found.All(f => f.type == typeof(KeywordGroupData)));

            var achievement = Make<AchievementData>("hunter", "사냥꾼");
            achievement.condition = new StatCompareCondition { stat = "kills", value = 3 };
            found.Clear();
            EditorDatabase.ForEachReference(achievement, (property, type) => found.Add((property.stringValue, type)));
            Assert.IsTrue(found.Any(f => f.value == "kills" && f.type == typeof(StatData)), "조건 블록 안쪽 참조까지 찾는다");
        }

        [Test]
        public void 상태_키_데이터가_저장소에_등록된다()
        {
            var gold = Make<StateKeyData>("player.gold", "골드");
            gold.zone = StateZone.Meta;
            gold.valueType = typeof(int).FullName;
            gold.defaultValue = "5";
            var bag = Make<StateKeyData>("player.bag", "가방");
            bag.valueType = typeof(TestInventory).FullName;

            var session = new GameSession();
            gold.DefineIn(session.State);
            bag.DefineIn(session.State);

            Assert.AreEqual(5, session.State.Get<int>("player.gold"));
            Assert.AreEqual(StateZone.Meta, session.State.ZoneOf("player.gold"));
            Assert.IsNotNull(session.State.Get<TestInventory>("player.bag"));
            Assert.AreEqual("골드", session.State.LabelOf("player.gold"));
        }

        [Test]
        public void 상태_키의_잘못된_형식과_기본값을_잡는다()
        {
            var missing = Make<StateKeyData>("a.missing", "없는 형식");
            missing.valueType = "Nope.Type";
            var badDefault = Make<StateKeyData>("a.bad", "잘못된 기본값");
            badDefault.valueType = typeof(int).FullName;
            badDefault.defaultValue = "많이";
            var issues = DataValidator.Validate(new List<GameData> { missing, badDefault });
            Assert.IsTrue(issues.Any(i => i.Asset == missing && i.Message.Contains("찾을 수 없습니다")));
            Assert.IsTrue(issues.Any(i => i.Asset == badDefault && i.Message.Contains("읽을 수 없습니다")));
        }

        [Test]
        public void 식별자_코드를_타입까지_붙여_만든다()
        {
            var gold = Make<StateKeyData>("player.gold", "골드");
            gold.valueType = typeof(int).FullName;
            var bag = Make<StateKeyData>("player.bag", "가방");
            bag.valueType = typeof(TestInventory).FullName;
            var kills = Make<StatData>("killsTotal", "처치 수");

            var code = GameIdsGenerator.Build(new List<GameData> { gold, bag, kills }, "GameSystems.Tests.Editor");

            StringAssert.Contains("public static class PlayerData", code);
            StringAssert.Contains("global::GameSystems.StateKey<int> PlayerGold = new global::GameSystems.StateKey<int>(\"player.gold\");", code);
            StringAssert.Contains("global::GameSystems.StateKey<global::GameSystems.Tests.TestInventory> PlayerBag", code);
            StringAssert.Contains("public static class Stat", code);
            StringAssert.Contains("public const string KillsTotal = \"killsTotal\";", code);
            StringAssert.Contains("/// <summary>killsTotal</summary>", code);
        }

        [Test]
        public void 참조할_수_없는_위치면_ID_문자열만_만든다()
        {
            var bag = Make<StateKeyData>("player.bag", "가방");
            bag.valueType = typeof(TestInventory).FullName;
            var code = GameIdsGenerator.Build(new List<GameData> { bag }, "없는어셈블리");
            StringAssert.Contains("public const string PlayerBag = \"player.bag\";", code);
        }
    }
}
