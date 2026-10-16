using System.Collections.Generic;
using System.Linq;
using GBible.Data;
using GBible.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GBible.Tests
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
        public void 없는_항목을_가리키면_오류_그_항목을_만들면_통과()
        {
            var boss = Rule("boss", "monsterTag");
            var issues = DataValidator.Validate(new List<GameData> { boss });
            Assert.IsTrue(issues.Any(i => i.Asset == boss && i.Severity == ValidationSeverity.Error && i.Message.Contains("'monsterTag'")));

            var frame = Make<SpriteFrameData>("monsterTag", "몬스터 틀");
            issues = DataValidator.Validate(new List<GameData> { boss, frame });
            Assert.IsFalse(issues.Any(i => i.Asset == boss && i.Severity == ValidationSeverity.Error));
        }

        // 틀([DataRef(typeof(SpriteFrameData))])을 가리키는 필드 규칙(다른 칸은 올바르게 — 참조만 시험한다).
        FieldRuleData Rule(string id, string frame)
        {
            var rule = Make<FieldRuleData>(id, id);
            rule.targetType = typeof(FieldRuleData).FullName;
            rule.field = nameof(FieldRuleData.spriteFrame);
            rule.source = FieldRuleSource.Free;
            rule.spriteFrame = frame;
            return rule;
        }

        [Test]
        public void 참조_필드를_모두_찾는다()
        {
            var boss = Rule("boss", "monsterTag");
            var found = new List<(string value, System.Type type)>();
            EditorDatabase.ForEachReference(boss, (property, type) => found.Add((property.stringValue, type)));
            CollectionAssert.AreEqual(new[] { "monsterTag" }, found.Select(f => f.value));
            Assert.IsTrue(found.All(f => f.type == typeof(SpriteFrameData)));

            var achievement = Make<AchievementData>("hunter", "사냥꾼");
            achievement.condition = new StatCompareCondition { stat = "kills", value = 3 };
            found.Clear();
            EditorDatabase.ForEachReference(achievement, (property, type) => found.Add((property.stringValue, type)));
            Assert.IsTrue(found.Any(f => f.value == "kills" && f.type == typeof(StatData)), "조건 블록 안쪽 참조까지 찾는다");
        }

        [Test]
        public void 플레이어_데이터_항목이_저장소에_등록된다()
        {
            var gold = Make<PlayerDataEntry>("player.gold", "골드");
            gold.zone = DataZone.Meta;
            gold.valueType = typeof(int).FullName;
            gold.defaultValue = "5";
            var bag = Make<PlayerDataEntry>("player.bag", "가방");
            bag.valueType = typeof(TestInventory).FullName;

            var session = new GameSession();
            gold.DefineIn(session.Data);
            bag.DefineIn(session.Data);

            Assert.AreEqual(5, session.Data.Get<int>("player.gold"));
            Assert.AreEqual(DataZone.Meta, session.Data.ZoneOf("player.gold"));
            Assert.IsNotNull(session.Data.Get<TestInventory>("player.bag"));
            Assert.AreEqual("player.gold", session.Data.LabelOf("player.gold"), "DB 항목은 ID로만 식별한다");
        }

        [Test]
        public void 플레이어_데이터_항목의_잘못된_형식과_기본값을_잡는다()
        {
            var missing = Make<PlayerDataEntry>("a.missing", "없는 형식");
            missing.valueType = "Nope.Type";
            var badDefault = Make<PlayerDataEntry>("a.bad", "잘못된 기본값");
            badDefault.valueType = typeof(int).FullName;
            badDefault.defaultValue = "많이";
            var issues = DataValidator.Validate(new List<GameData> { missing, badDefault });
            Assert.IsTrue(issues.Any(i => i.Asset == missing && i.Message.Contains("찾을 수 없습니다")));
            Assert.IsTrue(issues.Any(i => i.Asset == badDefault && i.Message.Contains("읽을 수 없습니다")));
        }

        [Test]
        public void 식별자_코드를_타입까지_붙여_만든다()
        {
            var gold = Make<PlayerDataEntry>("player.gold", "골드");
            gold.valueType = typeof(int).FullName;
            var bag = Make<PlayerDataEntry>("player.bag", "가방");
            bag.valueType = typeof(TestInventory).FullName;
            var kills = Make<StatData>("killsTotal", "처치 수");

            var code = GameIdsGenerator.Build(new List<GameData> { gold, bag, kills }, "GBible.Tests.Editor");

            StringAssert.Contains("public static class PlayerData", code);
            StringAssert.Contains("global::GBible.PlayerDataKey<int> PlayerGold = new global::GBible.PlayerDataKey<int>(\"player.gold\");", code);
            StringAssert.Contains("global::GBible.PlayerDataKey<global::GBible.Tests.TestInventory> PlayerBag", code);
            StringAssert.Contains("public static class Stat", code);
            StringAssert.Contains("public const string KillsTotal = \"killsTotal\";", code);
            StringAssert.Contains("/// <summary>killsTotal</summary>", code);
            // 이름으로 읽고 쓰는 창구: 단순 값은 읽고 쓰기, 클래스 값은 읽기(안쪽을 고친다).
            StringAssert.Contains("public int PlayerGold { get => _game.Session.Data.Get<int>(\"player.gold\"); set => _game.Session.Data.Set(\"player.gold\", value); }", code);
            StringAssert.Contains("public global::GBible.Tests.TestInventory PlayerBag => _game.Session.Data.Get<global::GBible.Tests.TestInventory>(\"player.bag\");", code);
            StringAssert.Contains("public static PlayerDataView Data(this global::GBible.GameApi game)", code);
        }


        [Test]
        public void 스프라이트_틀의_크기_이름도_상수로_만든다()
        {
            var frame = Make<SpriteFrameData>("character", "캐릭터");
            frame.sizes.Add(new SpriteFrameSize { name = "standing", width = 600, height = 900 });
            frame.sizes.Add(new SpriteFrameSize { name = "icon", width = 128, height = 128 });
            frame.sizes.Add(new SpriteFrameSize { name = "Bad.Name", width = 128, height = 128 });
            var code = GameIdsGenerator.Build(new List<GameData> { frame }, "GBible.Tests.Editor");
            StringAssert.Contains("public const string Character = \"character\";", code);
            StringAssert.Contains("public static class SpriteSize", code);
            StringAssert.Contains("public const string Standing = \"standing\";", code);
            StringAssert.Contains("public const string Icon = \"icon\";", code);
            StringAssert.DoesNotContain("Bad.Name", code, "올바르지 않은 크기 이름은 빼고 만든다");
        }

        [Test]
        public void 참조할_수_없는_위치면_ID_문자열만_만든다()
        {
            var bag = Make<PlayerDataEntry>("player.bag", "가방");
            bag.valueType = typeof(TestInventory).FullName;
            var code = GameIdsGenerator.Build(new List<GameData> { bag }, "없는어셈블리");
            StringAssert.Contains("public const string PlayerBag = \"player.bag\";", code);
        }

        [Test]
        public void 상수_이름이_겹쳐도_이미_있던_상수는_같은_ID를_가리킨다()
        {
            var old = Make<StatData>("bossDragon", "");
            var first = GameIdsGenerator.Build(new List<GameData> { old }, "GBible.Tests.Editor");
            StringAssert.Contains("public const string BossDragon = \"bossDragon\";", first);

            // 'boss.dragon'은 정렬 순서로 앞이지만, 기존 코드의 BossDragon이 새 항목을 가리키게 되지 않는다.
            var added = Make<StatData>("boss.dragon", "");
            var next = GameIdsGenerator.Build(new List<GameData> { old, added }, "GBible.Tests.Editor", first);
            StringAssert.Contains("public const string BossDragon = \"bossDragon\";", next);
            StringAssert.Contains("public const string BossDragon_2 = \"boss.dragon\";", next);
            Assert.AreEqual(next, GameIdsGenerator.Build(new List<GameData> { old, added }, "GBible.Tests.Editor", next), "다시 만들어도 같다");

            var gold = Make<PlayerDataEntry>("player.gold", "골드");
            gold.valueType = typeof(int).FullName;
            var view = GameIdsGenerator.Build(new List<GameData> { gold }, "GBible.Tests.Editor");
            var names = GameIdsGenerator.PreviousNames(view);
            Assert.AreEqual("PlayerGold", names["PlayerData|player.gold"]);
            Assert.AreEqual("PlayerGold", names["PlayerDataView|player.gold"]);
        }

        [Test]
        public void 같은_종류에서_코드_이름이_같아지는_ID와_대소문자만_다른_ID를_잡는다()
        {
            var a = Make<StatData>("bossDragon", "");
            var b = Make<StatData>("boss.dragon", "");
            var otherKind = Make<SpriteFrameData>("boss.dragon2", "");
            var issues = DataValidator.Validate(new List<GameData> { a, b, otherKind });
            Assert.AreEqual(2, issues.Count(i => i.Severity == ValidationSeverity.Error && i.Message.Contains("GameIds의 BossDragon")));

            var upper = Make<StatData>("potionA", "");
            var lower = Make<StatData>("potiona", "");
            issues = DataValidator.Validate(new List<GameData> { upper, lower });
            Assert.AreEqual(2, issues.Count(i => i.Message.Contains("중복")), "대소문자만 다른 ID도 중복(ID 바꾸기·만들기와 같은 규칙)");
        }

        [Test]
        public void 플레이어_데이터_칸과_값_틀이_없는_항목을_가리키면_알린다()
        {
            var run = Make<PlayerDataEntry>("run", "판");
            run.valueType = typeof(TestInventory).FullName;
            var passive = Make<PassiveData>("passive1", "");
            passive.ownerKey = "nope";
            var found = new List<(string value, System.Type type)>();
            EditorDatabase.ForEachReference(passive, (property, type) => found.Add((property.stringValue, type)));
            Assert.IsTrue(found.Any(f => f.value == "nope" && f.type == typeof(PlayerDataEntry)), "ID 바꾸기·지우기 검사가 플레이어 데이터 칸도 본다");

            var issues = DataValidator.Validate(new List<GameData> { run, passive });
            Assert.IsTrue(issues.Any(i => i.Asset == passive && i.Severity == ValidationSeverity.Error && i.Message.Contains("'nope'")));

            var rule = Make<FieldRuleData>("rule.template", "");
            rule.previewSize = "{balance:missing} {data:run.gold} {count:nokey.items}";
            issues = DataValidator.Validate(new List<GameData> { run, rule }).Where(i => i.Asset == rule).ToList();
            var seen = string.Join("\n", issues.Select(i => i.Severity + " " + i.Message));
            // 없는 밸런싱 값은 오류(실행하면 그 행동이 실패한다), 없는 플레이어 데이터는 경고(빈 글로 넘어간다).
            Assert.IsTrue(issues.Any(i => i.Severity == ValidationSeverity.Error && i.Message.Contains("'missing'")), seen);
            Assert.IsTrue(issues.Any(i => i.Severity == ValidationSeverity.Warning && i.Message.Contains("'nokey'")), seen);
            Assert.IsFalse(issues.Any(i => i.Message.Contains("'run'")), "있는 플레이어 데이터는 괜찮다");
        }
    }
}
