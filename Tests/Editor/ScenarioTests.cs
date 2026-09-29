using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GameSystems.Tests
{
    // ── 예제 게임 데이터(개발자가 짜는 부분: 클래스만) ───────────────
    public enum ScenarioElement
    {
        None,
        Fire,
        Ice,
    }

    [Serializable]
    public sealed class ScenarioEnhancement
    {
        [FieldLabel("효과")] public string effect = "";
        [FieldLabel("수치")] public int power;
    }

    [Serializable]
    public sealed class ScenarioEquipment
    {
        [FieldLabel("이름")] public string name = "";
        [FieldLabel("등급")] public int grade;
        [FieldLabel("속성")] public ScenarioElement element;
        [FieldLabel("태그")] public List<string> tags = new List<string>();
        [FieldLabel("강화")] public List<ScenarioEnhancement> enhancements = new List<ScenarioEnhancement>();
    }

    [Serializable]
    public sealed class ScenarioInventory
    {
        [FieldLabel("장비")] public List<ScenarioEquipment> items = new List<ScenarioEquipment>();
    }

    [GameEvent("예제: 몬스터 처치")]
    public sealed class ScenarioKilled
    {
        public string monster = "";
        public List<string> tags = new List<string>();
    }

    [Serializable] public sealed class ScenarioAttack : GameAction { public string monster; public string[] tags = Array.Empty<string>(); public override string Describe() => $"{monster} 공격"; }
    [Serializable] public sealed class ScenarioLoot : GameAction { public string template; public override string Describe() => $"{template} 획득"; }
    [Serializable] public sealed class ScenarioEnhance : GameAction { public string effect; public override string Describe() => $"첫 장비 {effect} 강화"; }

    // 끝까지 검증: 도감에서 정의할 것(수치 규칙·도전과제 조건식·상태 키·템플릿)을 데이터로 만들고,
    // 행동 → 이벤트 → 수치 → 조건식 → 달성·보상 → Undo → 저장·이어하기가 코드 추가 없이 맞물리는지 확인한다.
    public class ScenarioTests
    {
        const string Inventory = "player.inventory";
        const string Floor = "run.floor";

        // 도감의 템플릿에 해당(개발자가 미리 채워 둔 인스턴스).
        static readonly Dictionary<string, ScenarioEquipment> Templates = new Dictionary<string, ScenarioEquipment>
        {
            ["training_sword"] = new ScenarioEquipment { name = "훈련용 검", grade = 1, tags = { "melee" } },
            ["dragon_armor"] = new ScenarioEquipment { name = "용의 비늘 갑옷", grade = 5, element = ScenarioElement.Fire, tags = { "legendary" } },
        };

        static GameSession NewSession()
        {
            var session = new GameSession(new SessionOptions { Seed = 77 });

            // 상태 키(도감: 상태 키)
            session.State.Define(Inventory, StateZone.Meta, new ScenarioInventory(), "인벤토리");
            session.State.Define(Floor, StateZone.Session, 1, "현재 층");

            // 수치(도감: 수치) — 보스 처치 수는 이벤트 조건식으로 거른다.
            var kills = new StatDefinition { Id = "kills", DisplayName = "처치 수", Scope = StatScope.Total, Aggregate = StatAggregate.Sum };
            kills.Rules.Add(new StatRule { eventType = typeof(ScenarioKilled).FullName });
            var bossKills = new StatDefinition { Id = "boss_kills", DisplayName = "보스 처치 수", Scope = StatScope.Total, Aggregate = StatAggregate.Sum };
            bossKills.Rules.Add(new StatRule { eventType = typeof(ScenarioKilled).FullName, filter = new FieldFilter { path = "tags", op = FieldOp.Contains, value = "boss" } });
            var gold = new StatDefinition { Id = "gold", DisplayName = "골드", Scope = StatScope.Total, Aggregate = StatAggregate.Sum };
            session.Stats.Define(kills);
            session.Stats.Define(bossKills);
            session.Stats.Define(gold);

            // 도전과제(도감: 도전과제) — 조건식 트리
            var dragonSlayer = new AchievementDefinition
            {
                Id = "dragon_slayer",
                DisplayName = "용 사냥꾼",
                Condition = new StatCompareCondition { stat = "boss_kills", op = CompareOp.AtLeast, value = 1 },
            };
            dragonSlayer.Rewards.Add(new AddToStatReward { stat = "gold", amount = 100 });
            session.Achievements.Define(dragonSlayer);
            session.Achievements.Define(new AchievementDefinition
            {
                Id = "legend_collector",
                DisplayName = "전설 수집가",
                Condition = new StateFieldCondition
                {
                    key = Inventory,
                    filter = new ElementFilter { path = "items", mode = ElementMode.Any, filter = new FieldFilter { path = "tags", op = FieldOp.Contains, value = "legendary" } },
                },
            });
            // 중첩: 장비 중 [강화가 3개 이상이고 불 속성이 아닌] 것이 하나라도
            session.Achievements.Define(new AchievementDefinition
            {
                Id = "enhance_master",
                DisplayName = "강화 장인",
                Condition = new StateFieldCondition
                {
                    key = Inventory,
                    filter = new ElementFilter
                    {
                        path = "items",
                        mode = ElementMode.Any,
                        filter = new AllFilter
                        {
                            filters =
                            {
                                new FieldFilter { path = "enhancements", op = FieldOp.CountAtLeast, value = "3" },
                                new NotFilter { filter = new FieldFilter { path = "element", op = FieldOp.Equal, value = "Fire" } },
                            },
                        },
                    },
                },
            });

            // 행동(프로그래머가 짜는 부분: 게임 로직 한 줄씩)
            session.Actions.Register<ScenarioAttack>(a => session.Events.Publish(new ScenarioKilled { monster = a.monster, tags = a.tags.ToList() }));
            session.Actions.Register<ScenarioLoot>(a => session.State.Modify<ScenarioInventory>(Inventory, inv => inv.items.Add((ScenarioEquipment)DataModel.Clone(Templates[a.template], null))));
            session.Actions.Register<ScenarioEnhance>(a => session.State.Modify<ScenarioInventory>(Inventory, inv =>
                inv.items[0].enhancements.Add(new ScenarioEnhancement { effect = a.effect, power = session.Random.Get("enhance").Range(1, 10) })));
            return session;
        }

        [Test]
        public void 처음부터_끝까지_코드_추가_없이_맞물린다()
        {
            var session = NewSession();

            // 1) 이벤트 조건식: 슬라임은 보스가 아니고, 드래곤만 보스 처치 수에 들어간다 → 달성·보상
            session.Do(new ScenarioAttack { monster = "슬라임" });
            session.Do(new ScenarioAttack { monster = "드래곤", tags = new[] { "boss" } });
            Assert.AreEqual(2, session.Stats.Get("kills"));
            Assert.AreEqual(1, session.Stats.Get("boss_kills"));
            Assert.IsTrue(session.Achievements.IsUnlocked("dragon_slayer"));
            Assert.AreEqual(100, session.Stats.Get("gold"));

            // 2) 템플릿 사본 획득 → 저장소 조건식(목록 항목 검사) 달성 → Undo해도 달성은 유지, 아이템만 빠진다
            session.Do(new ScenarioLoot { template = "dragon_armor" });
            Assert.IsTrue(session.Achievements.IsUnlocked("legend_collector"));
            session.Undo();
            Assert.AreEqual(0, session.State.Get<ScenarioInventory>(Inventory).items.Count);
            Assert.IsTrue(session.Achievements.IsUnlocked("legend_collector"));
            Assert.AreEqual(0, Templates["dragon_armor"].enhancements.Count, "템플릿 원본은 그대로");

            // 3) 중첩 조건식: 불 속성 갑옷은 강화 3번이어도 안 되고, 검은 된다
            session.Do(new ScenarioLoot { template = "dragon_armor" });
            for (int i = 0; i < 3; i++) session.Do(new ScenarioEnhance { effect = "lifesteal" });
            Assert.IsFalse(session.Achievements.IsUnlocked("enhance_master"), "불 속성은 제외");
            session.State.Modify<ScenarioInventory>(Inventory, inv => inv.items.Insert(0, (ScenarioEquipment)DataModel.Clone(Templates["training_sword"], null)));
            for (int i = 0; i < 3; i++) session.Do(new ScenarioEnhance { effect = "fire_resist" });
            Assert.IsTrue(session.Achievements.IsUnlocked("enhance_master"));

            // 4) 기록: 강화 한 번이 사람이 읽는 경로와 코드 경로로 남는다
            var change = session.Journal.Entries.Last(e => e.Kind == JournalKind.State && e.Key == Inventory);
            StringAssert.Contains("인벤토리 › 장비 #", change.Message);
            StringAssert.Contains("› 강화", change.Message);
            StringAssert.Contains("items[#", change.Code);

            // 5) 저장·이어하기: 프로필(인벤토리·수치·달성)과 판(층·난수)이 따로 저장되고 그대로 이어진다
            session.State.Set(Floor, 4);
            var profile = session.ExportMeta();
            var run = session.ExportRun();
            var nextRoll = session.Random.Get("enhance").Range(1, 10);

            var resumed = NewSession();
            resumed.ImportMeta(profile);
            resumed.ImportRun(run);
            var items = resumed.State.Get<ScenarioInventory>(Inventory).items;
            CollectionAssert.AreEqual(new[] { "훈련용 검", "용의 비늘 갑옷" }, items.Select(i => i.name).ToArray());
            Assert.AreEqual(3, items[0].enhancements.Count);
            Assert.AreEqual(ScenarioElement.Fire, items[1].element);
            Assert.AreEqual(4, resumed.State.Get<int>(Floor));
            Assert.AreEqual(100, resumed.Stats.Get("gold"), "보상 골드 유지");
            resumed.Achievements.EvaluateAll();
            Assert.IsTrue(resumed.Achievements.IsUnlocked("dragon_slayer"));
            Assert.IsTrue(resumed.Achievements.IsUnlocked("enhance_master"));
            Assert.AreEqual(nextRoll, resumed.Random.Get("enhance").Range(1, 10), "난수도 저장 시점부터 그대로");
        }
    }
}
