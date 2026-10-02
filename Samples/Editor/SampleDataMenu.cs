using System.Linq;
using GBible.Data;
using GBible.EditorTools;
using UnityEditor;

namespace GBible.Samples
{
    // 샘플 수치·도전과제 에셋을 만든다. 이미 있으면 건너뛴다.
    public static class SampleDataMenu
    {
        [MenuItem("Tests/샘플/샘플 데이터 만들기", priority = 40)]
        public static void Create()
        {
            if (!CreateData())
            {
                EditorUtility.DisplayDialog("샘플 데이터", "샘플 데이터가 이미 있습니다.", "확인");
                return;
            }
            DatabaseWindow.Open();
        }

        // 다국어 예시: 한국어(원문)·영어 표와 문장 몇 개. 있는 키는 건드리지 않는다.
        [MenuItem("Tests/샘플/샘플 텍스트 만들기", priority = 42)]
        public static void CreateTexts()
        {
            var settings = TextSheetTools.EnsureSettings();
            if (settings.Find("en") == null)
            {
                settings.languages.Add(new LocalizationSettings.Language { code = "en", nativeName = "English" });
                EditorUtility.SetDirty(settings);
            }
            foreach (var language in settings.languages)
                if (language.table == null) language.table = TextSheetTools.EnsureTable(language.code);
            EditorUtility.SetDirty(settings);

            AddTexts(settings.Find("ko").table, new[]
            {
                ("ui.start", "시작하기"),
                ("gold.name", "골드"),
                ("sample.reward", "{item:을} {count}개 얻었습니다."),
                ("sample.goldEarned", "{amount, number} [[gold:을]] 얻었습니다."),
                ("sample.enemy", "{enemy:이} 나타났다!"),
                ("sample.inbox", "새 편지 {n}통"),
                ("sample.burn", "[[burn:이]] 번졌다."),
                ("burn.name", "화상"),
                ("template.dragonArmor.name", "용의 비늘 갑옷"),
            });
            AddTexts(settings.Find("en").table, new[]
            {
                ("ui.start", "Start"),
                ("gold.name", "gold"),
                ("sample.reward", "You got {count, plural, one {a} other {#}} {item}."),
                ("sample.goldEarned", "You earned {amount, number} [[gold]]."),
                ("sample.enemy", "{enemy} appeared!"),
                ("sample.inbox", "{n, plural, =0 {No new letters} one {# new letter} other {# new letters}}"),
                ("sample.burn", "[[burn:cap]] spreads."),
                ("burn.name", "burn"),
                ("template.dragonArmor.name", "{count, plural, one {Dragon Scale Armor} other {Dragon Scale Armors}}"),
            });
            AssetDatabase.SaveAssets();
            GameLanguage.Reload();
            Selection.activeObject = settings;
        }

        static void AddTexts(TextTable table, (string key, string text)[] lines)
        {
            var existing = new System.Collections.Generic.HashSet<string>(table.entries.Select(e => e.key));
            foreach (var (key, text) in lines)
                if (existing.Add(key)) table.entries.Add(new TextTable.Entry { key = key, text = text });
            EditorUtility.SetDirty(table);
        }

        // 배치모드용: Unity.exe -batchmode -quit -executeMethod GBible.Samples.SampleDataMenu.CreateDataBatch
        public static void CreateDataBatch()
        {
            var created = CreateData();
            UnityEngine.Debug.Log(created ? "[MomoiProject] 샘플 데이터를 만들었습니다." : "[MomoiProject] 샘플 데이터가 이미 있습니다.");
        }

        public static bool CreateData()
        {
            var db = EditorDatabase.Instance;
            db.Sync();
            var created = CreateBasics(db);
            created |= CreateEquipmentExample(db);
            if (!created) return false;
            AssetDatabase.SaveAssets();
            EditorDatabase.Instance.Sync();
            return true;
        }

        static bool CreateBasics(EditorDatabase db)
        {
            if (db.OfType<StatData>().Any(s => s.Id == "killsTotal")) return false;

            var killsRun = Stat("killsRun", StatScope.Run, StatAggregate.Sum, Rule<EnemyKilled>(nameof(EnemyKilled.Count)));
            var killsTotal = Stat("killsTotal", StatScope.Total, StatAggregate.Sum, Rule<EnemyKilled>(nameof(EnemyKilled.Count)));
            var bestCombo = Stat("bestCombo", StatScope.Total, StatAggregate.Max, Rule<EnemyKilled>(nameof(EnemyKilled.Combo)));
            // 번 골드 총합은 로그(이벤트로 자동 누적), 지갑 골드는 플레이어 데이터(보상으로 지급·사용으로 차감).
            var goldEarned = Stat("goldEarned", StatScope.Total, StatAggregate.Sum, Rule<GoldEarned>(nameof(GoldEarned.Amount)));
            var gold = (PlayerDataEntry)db.Create(typeof(PlayerDataEntry), "player.gold");
            gold.zone = DataZone.Meta;
            gold.valueType = typeof(int).FullName;
            gold.defaultValue = "0";
            EditorUtility.SetDirty(gold);

            Achievement("firstHunt",
                new StatCompareCondition { stat = killsTotal.Id, op = CompareOp.AtLeast, value = 1 },
                Pay(gold.Id, 50));

            var hunter = Achievement("monsterHunter",
                new StatCompareCondition { stat = killsTotal.Id, op = CompareOp.AtLeast, value = 25 },
                Pay(gold.Id, 500));

            var comboMaster = Achievement("comboMaster",
                new StatCompareCondition { stat = bestCombo.Id, op = CompareOp.AtLeast, value = 8 });

            Achievement("legendaryWarrior",
                new AllOfCondition
                {
                    conditions =
                    {
                        new AchievementUnlockedCondition { achievement = hunter.Id },
                        new AchievementUnlockedCondition { achievement = comboMaster.Id },
                    },
                },
                Pay(gold.Id, 2000));

            Achievement("rich",
                new StatCompareCondition { stat = goldEarned.Id, op = CompareOp.AtLeast, value = 1000 });

            // 이번 판 수치는 되감기 대상임을 보여주는 용도(Undo하면 되돌아간다).
            _ = killsRun;
            return true;
        }

        // 장비·강화 효과·몬스터 태그 예제: 키워드, 플레이어 데이터, 템플릿, 조건식이 붙은 수치·도전과제.
        static bool CreateEquipmentExample(EditorDatabase db)
        {
            if (db.FindById("player.inventory") != null) return false;
            // 키워드 그룹 + 필드 연결: 코드의 빈 string 필드가 에디터 설정만으로 드롭다운이 된다.
            // string 필드는 하나, string 배열·리스트 필드는 여러 개를 고른다.
            // 키워드 그룹과 필드 연결은 샘플 코드(SampleGame.cs의 [KeywordGroup]·[Keyword])에 있다. 여기서는 필수만.
            Required(typeof(SampleEquipment), nameof(SampleEquipment.slot));

            // 스프라이트 규격: 장비 아이콘 필드에 원본을 넣으면 아이콘·카드 규격으로 베이크, 목록은 아이콘 규격으로 보인다.
            var itemImages = CodexGroups.Create(typeof(SpritePresetGroupData), "itemImages", staged: false);
            foreach (var (id, w, h) in new[] { ("itemIcon", 128, 128), ("itemCard", 256, 384) })
                CodexGroups.Create(typeof(SpritePresetData), id, itemImages.Id, staged: false, init: data =>
                {
                    var preset = (SpritePresetData)data;
                    preset.width = w;
                    preset.height = h;
                });
            var iconRule = (FieldRuleData)db.Create(typeof(FieldRuleData), "rule.equipmentIcon");
            iconRule.targetType = typeof(SampleEquipment).FullName;
            iconRule.field = nameof(SampleEquipment.icon);
            iconRule.source = FieldRuleSource.Free;
            iconRule.spriteGroup = itemImages.Id;
            iconRule.previewPreset = "itemIcon";
            EditorUtility.SetDirty(iconRule);
            var inventory = (PlayerDataEntry)db.Create(typeof(PlayerDataEntry), "player.inventory");
            inventory.zone = DataZone.Meta;
            inventory.valueType = typeof(SampleInventory).FullName;
            EditorUtility.SetDirty(inventory);

            Template("template.trainingSword", new SampleEquipment { name = "훈련용 검", slot = "weapon", grade = "common", tags = { "melee" } });
            Template("template.dragonArmor", new SampleEquipment { name = "용의 비늘 갑옷", slot = "armor", grade = "legendary", element = SampleElement.Fire, tags = { "pierce" } });

            var gold = db.OfType<PlayerDataEntry>().FirstOrDefault(s => s.Id == "player.gold");
            var bossRule = Rule<EnemyKilled>("");
            bossRule.filter = new FieldFilter { path = nameof(EnemyKilled.Tags), op = FieldOp.Contains, value = "boss" };
            var bossKills = Stat("bossKills", StatScope.Total, StatAggregate.Sum, bossRule);

            Achievement("dragonSlayer",
                new StatCompareCondition { stat = bossKills.Id, op = CompareOp.AtLeast, value = 1 },
                // 보상: 골드 지급 + 아이템(용의 비늘 갑옷 템플릿 사본)을 인벤토리 items에 지급.
                gold != null
                    ? new Reward[] { Pay(gold.Id, 100), Give(inventory.Id, "items", "template.dragonArmor") }
                    : new Reward[] { Give(inventory.Id, "items", "template.dragonArmor") });
            Achievement("legendCollector",
                new PlayerDataCondition
                {
                    key = inventory.Id,
                    filter = new ElementFilter { path = "items", mode = ElementMode.Any, filter = new FieldFilter { path = "grade", op = FieldOp.Equal, value = "legendary" } },
                });
            Achievement("enhanceMaster",
                new PlayerDataCondition
                {
                    key = inventory.Id,
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
                });
            return true;
        }

        static void Required(System.Type type, string field) => KeywordTools.SetRequired(type, type.GetField(field), true, staged: false);

        static void Template(string id, object value)
        {
            var template = (TemplateData)EditorDatabase.Instance.Create(typeof(TemplateData), id);
            template.templateType = value.GetType().FullName;
            template.value = value;
            EditorUtility.SetDirty(template);
            AssetDatabase.SaveAssetIfDirty(template);
        }

        // 보상: 숫자 플레이어 데이터 증감 / 리스트에 템플릿(사본) 넣기.
        static PlayerDataReward Pay(string key, int amount) =>
            new PlayerDataReward { key = key, amount = amount };

        static PlayerDataReward Give(string key, string path, string template) =>
            new PlayerDataReward { key = key, path = path, items = { template } };

        static StatRule Rule<T>(string field) => new StatRule { eventType = typeof(T).FullName, field = field, multiplier = 1 };

        static StatData Stat(string id, StatScope scope, StatAggregate aggregate, StatRule rule)
        {
            var stat = (StatData)EditorDatabase.Instance.Create(typeof(StatData), id);
            stat.scope = scope;
            stat.aggregate = aggregate;
            stat.triggers.Add(rule.ToTriggerRule());
            EditorUtility.SetDirty(stat);
            return stat;
        }

        static AchievementData Achievement(string id, Condition condition, params Reward[] rewards)
        {
            var achievement = (AchievementData)EditorDatabase.Instance.Create(typeof(AchievementData), id);
            achievement.trigger = new ConditionTrigger { condition = condition };
            achievement.outcomes.AddRange(rewards);
            EditorUtility.SetDirty(achievement);
            AssetDatabase.SaveAssetIfDirty(achievement);
            EditorDatabase.MarkDirty();
            return achievement;
        }
    }
}
