using System.Collections.Generic;
using System.Linq;
using GBible.Data;
using GBible.EditorTools;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace GBible.Tests
{
    public class ValidatorTests
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
        public void 중복_ID를_잡는다()
        {
            var a = Make<StatData>("stat1", "A");
            var b = Make<StatData>("stat1", "B");
            var issues = DataValidator.Validate(new List<GameData> { a, b });
            Assert.AreEqual(2, issues.Count(i => i.Message.Contains("중복")));
        }

        [Test]
        public void 없는_수치_참조와_빈_조건을_잡는다()
        {
            var achievement = Make<AchievementData>("achievement1", "업적");
            achievement.condition = new StatCompareCondition { stat = "없는수치", value = 1 };
            var empty = Make<AchievementData>("achievement2", "빈 업적");
            var issues = DataValidator.Validate(new List<GameData> { achievement, empty });
            Assert.IsTrue(issues.Any(i => i.Asset == achievement && i.Message.Contains("존재하지 않는 로그")));
            Assert.IsTrue(issues.Any(i => i.Asset == empty && i.Message.Contains("트리거가 없습니다")));
        }

        [Test]
        public void 플레이어_데이터에_주기_보상을_값_형식과_인스턴스로_검사한다()
        {
            var bag = Make<PlayerDataEntry>("playerData1", "가방");
            bag.valueType = typeof(TestInventory).FullName;
            var achievement = Make<AchievementData>("achievement1", "업적");
            achievement.condition = new StatCompareCondition { stat = "stat1", value = 1 };
            achievement.rewards.Add(new PlayerDataReward { key = "playerData1", path = "items", amount = 1 });
            achievement.rewards.Add(new PlayerDataReward { key = "없는키", amount = 1 });
            achievement.rewards.Add(new PlayerDataReward { key = "playerData1", path = "gold", amount = 5 });
            achievement.rewards.Add(new PlayerDataReward { key = "playerData1", path = "items", items = { "instance.none" } });
            var issues = DataValidator.Validate(new List<GameData> { bag, achievement }).Where(i => i.Message.StartsWith("플레이어 데이터에 주기")).ToList();
            Assert.AreEqual(3, issues.Count, string.Join("\n", issues.Select(i => i.Message)));
            Assert.IsTrue(issues.Any(i => i.Message.Contains("넣을 요소가 없습니다")));
            Assert.IsTrue(issues.Any(i => i.Message.Contains("없는 플레이어 데이터")));
            Assert.IsTrue(issues.Any(i => i.Message.Contains("없는 인스턴스 'instance.none'")));
        }

        [Test]
        public void 순환_참조를_잡는다()
        {
            var a = Make<AchievementData>("achievement1", "A");
            var b = Make<AchievementData>("achievement2", "B");
            a.condition = new AchievementUnlockedCondition { achievement = "achievement2" };
            b.condition = new AchievementUnlockedCondition { achievement = "achievement1" };
            var issues = DataValidator.Validate(new List<GameData> { a, b });
            Assert.IsTrue(issues.Any(i => i.Message.Contains("순환")));
        }

        [Test]
        public void 잘못된_이벤트_규칙을_잡는다()
        {
            var stat = Make<StatData>("stat1", "처치");
            stat.rules.Add(new StatRule { eventType = "Nope.Missing" });
            stat.rules.Add(new StatRule { eventType = typeof(TestKill).FullName, field = "Missing" });
            var issues = DataValidator.Validate(new List<GameData> { stat });
            Assert.IsTrue(issues.Any(i => i.Message.Contains("찾을 수 없습니다")));
            Assert.IsTrue(issues.Any(i => i.Message.Contains("필드가 없습니다")));
        }

        [Test]
        public void 정상_데이터는_통과한다()
        {
            var stat = Make<StatData>("stat1", "처치");
            stat.rules.Add(new StatRule { eventType = typeof(TestKill).FullName, field = nameof(TestKill.Count) });
            var achievement = Make<AchievementData>("achievement1", "업적");
            achievement.condition = new StatCompareCondition { stat = "stat1", value = 5 };
            Assert.IsEmpty(DataValidator.Validate(new List<GameData> { stat, achievement }).Where(i => i.Severity == ValidationSeverity.Error));
        }
    }

    public class DataToolTests
    {
        [Test]
        public void 데이터베이스_타입은_GameData_상속으로_판별된다()
        {
            var types = DataTypes.All.Select(t => t.Type).ToList();
            CollectionAssert.Contains(types, typeof(StatData));
            CollectionAssert.Contains(types, typeof(AchievementData));
            CollectionAssert.DoesNotContain(types, typeof(GameDatabase), "[HideInDatabase]는 제외");
        }

        [Test]
        public void 필드_목록은_저장되는_필드만_종류와_함께()
        {
            var fields = FieldCatalog.For(typeof(AchievementData));
            Assert.AreEqual(FieldKind.Image, fields.First(f => f.Path == "icon").Kind);
            Assert.AreEqual(FieldKind.Text, FieldCatalog.For(typeof(FieldRuleData)).First(f => f.Path == "field").Kind);
            Assert.IsFalse(fields.Any(f => f.Path == "displayName"), "DB 항목은 ID로만 식별한다(표시 이름 없음)");
            Assert.IsFalse(fields.Any(f => f.Path == "id"), "숨긴 필드(ID)는 목록에 없다");
        }

        [Test]
        public void 자동_추론_보기()
        {
            var view = ViewSettings.Resolve(typeof(AchievementData));
            Assert.AreEqual("icon", view.Image?.Path);
            Assert.IsNull(view.Title, "제목을 고르지 않으면 카드 제목은 ID");
            Assert.IsNull(view.Subtitle, "부제목으로 쓸 글자 필드가 없다");
        }

        [Test]
        public void 끊긴_연결에_비슷한_필드를_제안한다()
        {
            var fields = FieldCatalog.For(typeof(AchievementData));
            var suggestion = ViewSettings.Suggest(fields, "icons", Slot.Image);
            Assert.AreEqual("icon", suggestion?.Path);
        }

        [Test]
        public void CSV_숫자_칸은_천_단위_쉼표와_소수_쉼표를_가려_읽는다()
        {
            Assert.AreEqual("1000", DataCsv.PlainNumber("1,000"));
            Assert.AreEqual("-1234567.5", DataCsv.PlainNumber("-1,234,567.5"));
            Assert.AreEqual("0.5", DataCsv.PlainNumber("0,5"));
            Assert.AreEqual("1e3", DataCsv.PlainNumber("1e3"));
            Assert.IsNull(DataCsv.PlainNumber("1.000,5"), "점과 쉼표가 함께면 알 수 없다");
            Assert.IsNull(DataCsv.PlainNumber("0,500"), "쉼표 뒤 세 자리: 천 단위인지 소수인지 알 수 없다");
            Assert.IsNull(DataCsv.PlainNumber("1,00,000"));

            var balance = ScriptableObject.CreateInstance<BalanceData>();
            try
            {
                var so = new UnityEditor.SerializedObject(balance);
                var value = so.FindProperty(nameof(BalanceData.value));
                Assert.IsTrue(DataCsv.Parse(value, "1,000", out _));
                so.ApplyModifiedPropertiesWithoutUndo();
                Assert.AreEqual(1000.0, balance.value, "1이 아니라 1000");
                Assert.IsFalse(DataCsv.Parse(value, "1.000,5", out var error));
                StringAssert.Contains("쉼표", error);
                so.ApplyModifiedPropertiesWithoutUndo();
                Assert.AreEqual(1000.0, balance.value, "읽지 못한 칸은 그대로");
            }
            finally
            {
                Object.DestroyImmediate(balance);
            }
        }

        [Test]
        public void CSV는_UTF_8이_아니면_읽지_않는다()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "momoi-csv-" + System.Guid.NewGuid().ToString("N") + ".csv");
            try
            {
                // "key,가" — 가 = CP949 B0 A1(한국어 Windows Excel의 기본 'CSV').
                System.IO.File.WriteAllBytes(path, new byte[] { 0x6B, 0x65, 0x79, 0x2C, 0xB0, 0xA1 });
                Assert.Throws<System.Text.DecoderFallbackException>(() => DataCsv.ReadUtf8(path));
                System.IO.File.WriteAllText(path, "key,가", new System.Text.UTF8Encoding(true));
                Assert.AreEqual("key,가", DataCsv.ReadUtf8(path), "BOM이 있는 UTF-8은 읽는다");
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }
    }

    public class WindowSmokeTests
    {
        [Test]
        public void 창들이_예외_없이_구성된다()
        {
            var host = new VisualElement();

            var database = ScriptableObject.CreateInstance<DatabaseWindow>();
            Assert.DoesNotThrow(() => database.Build(host));
            Assert.Greater(host.Query<VisualElement>().ToList().Count, 10);
            Object.DestroyImmediate(database);

            var monitor = ScriptableObject.CreateInstance<MonitorWindow>();
            Assert.DoesNotThrow(() => monitor.Build(new VisualElement()));
            Object.DestroyImmediate(monitor);
        }

        [Test]
        public void 모니터의_모든_탭이_실행_중인_세션으로_구성된다()
        {
            var session = new GameSession(new SessionOptions());
            var kills = new StatDefinition { Id = "kills", DisplayName = "처치", Scope = StatScope.Total, Aggregate = StatAggregate.Sum };
            kills.Rules.Add(new StatRule { eventType = typeof(TestKill).FullName, field = nameof(TestKill.Count) });
            session.Stats.Define(kills);
            session.Stats.Define(new StatDefinition { Id = "gold", DisplayName = "골드", Scope = StatScope.Total, Aggregate = StatAggregate.Sum });
            session.Achievements.Define(new AchievementDefinition { Id = "a", DisplayName = "업적", Condition = new StatCompareCondition { stat = "kills", value = 5 } });
            session.TrySimulateStat("kills", 2, out _, out _);

            var previous = MonitorWindow.SessionSource;
            MonitorWindow.SessionSource = () => session;
            var monitor = ScriptableObject.CreateInstance<MonitorWindow>();
            try
            {
                for (int tab = 0; tab < MonitorWindow.TabCount; tab++)
                {
                    var index = tab;
                    Assert.DoesNotThrow(() => monitor.ShowTab(index), $"탭 {index}");
                    Assert.Greater(monitor.rootVisualElement.Query<VisualElement>().ToList().Count, 10, $"탭 {index}이 비어 있다");
                }
            }
            finally
            {
                MonitorWindow.SessionSource = previous;
                Object.DestroyImmediate(monitor);
            }
        }
    }
}
