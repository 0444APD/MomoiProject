using System.Linq;
using GameSystems.Data;
using GameSystems.EditorTools;
using UnityEditor;

namespace GameSystems.Samples
{
    // 샘플 수치·도전과제 에셋을 만든다. 이미 있으면 건너뛴다.
    public static class SampleDataMenu
    {
        [MenuItem("Game Systems/샘플/샘플 데이터 만들기", priority = 40)]
        public static void Create()
        {
            if (!CreateData())
            {
                EditorUtility.DisplayDialog("샘플 데이터", "샘플 데이터가 이미 있습니다.", "확인");
                return;
            }
            DatabaseWindow.Open();
        }

        // 상세 패널 점검용 항목: 필드 종류·개수가 많고, 긴 글(10줄 넘음)과 빈 칸이 들어 있다. 만들고 바로 상세 패널을 연다.
        [MenuItem("Game Systems/샘플/필드 시험 항목 만들기", priority = 41)]
        public static void CreateFieldTest()
        {
            var db = EditorDatabase.Instance;
            db.Sync();
            var item = (SampleFieldTestData)db.Create(typeof(SampleFieldTestData), db.NextId(DataTypes.Find(typeof(SampleFieldTestData))));
            item.longText = string.Join("\n", Enumerable.Range(1, 16).Select(i => $"{i}번째 줄: 긴 글은 칸이 늘어나다가 10줄을 넘으면 칸 안에서 스크롤됩니다."));
            item.stat = db.OfType<StatData>().FirstOrDefault()?.Id ?? "";
            item.achievement = db.OfType<AchievementData>().FirstOrDefault()?.Id ?? "";
            item.condition = new AllOfCondition();
            EditorUtility.SetDirty(item);
            AssetDatabase.SaveAssetIfDirty(item);
            // 샘플 키워드가 있으면 키워드 칸(하나·여러 개)도 드롭다운으로 확인할 수 있게 연결한다.
            if (db.FindById("grade") != null) KeywordTools.Connect(typeof(SampleFieldTestData), typeof(SampleFieldTestData).GetField(nameof(SampleFieldTestData.keyword)), "grade");
            if (db.FindById("equipmentTag") != null) KeywordTools.Connect(typeof(SampleFieldTestData), typeof(SampleFieldTestData).GetField(nameof(SampleFieldTestData.keywords)), "equipmentTag");
            DatabaseWindow.ShowItem(item);
        }

        // 배치모드용: Unity.exe -batchmode -quit -executeMethod GameSystems.Samples.SampleDataMenu.CreateDataBatch
        public static void CreateDataBatch()
        {
            var created = CreateData();
            UnityEngine.Debug.Log(created ? "[GameSystems] 샘플 데이터를 만들었습니다." : "[GameSystems] 샘플 데이터가 이미 있습니다.");
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
            var gold = (StateKeyData)db.Create(typeof(StateKeyData), "player.gold");
            gold.zone = StateZone.Meta;
            gold.valueType = typeof(int).FullName;
            gold.defaultValue = "0";
            EditorUtility.SetDirty(gold);

            Achievement("firstHunt", "처음으로 적을 처치했다.",
                new StatCompareCondition { stat = killsTotal.Id, op = CompareOp.AtLeast, value = 1 },
                new AddToStateReward { key = gold.Id, amount = 50 });

            var hunter = Achievement("monsterHunter", "적을 25마리 처치했다.",
                new StatCompareCondition { stat = killsTotal.Id, op = CompareOp.AtLeast, value = 25 },
                new AddToStateReward { key = gold.Id, amount = 500 });

            var comboMaster = Achievement("comboMaster", "한 번에 8연속 처치했다.",
                new StatCompareCondition { stat = bestCombo.Id, op = CompareOp.AtLeast, value = 8 });

            Achievement("legendaryWarrior", "몬스터 사냥꾼과 연속 공격의 달인을 모두 달성했다.",
                new AllOfCondition
                {
                    conditions =
                    {
                        new AchievementUnlockedCondition { achievement = hunter.Id },
                        new AchievementUnlockedCondition { achievement = comboMaster.Id },
                    },
                },
                new AddToStateReward { key = gold.Id, amount = 2000 });

            Achievement("rich", "골드를 총 1000 벌었다.",
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
            Keywords("equipmentSlot", "weapon", "armor", "accessory");
            Keywords("grade", "common", "rare", "legendary");
            Keywords("equipmentTag", "melee", "ranged", "dot", "pierce");
            Keywords("monsterTag", "boss", "elite");
            Keywords("enhanceEffect", "lifesteal", "fireResist");
            Link(typeof(SampleEquipment), nameof(SampleEquipment.slot), "equipmentSlot", required: true);
            Link(typeof(SampleEquipment), nameof(SampleEquipment.grade), "grade");
            Link(typeof(SampleEquipment), nameof(SampleEquipment.tags), "equipmentTag");
            Link(typeof(EnemyKilled), nameof(EnemyKilled.Tags), "monsterTag");
            Link(typeof(SampleEnhancement), nameof(SampleEnhancement.effect), "enhanceEffect");

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
            var inventory = (StateKeyData)db.Create(typeof(StateKeyData), "player.inventory");
            inventory.zone = StateZone.Meta;
            inventory.valueType = typeof(SampleInventory).FullName;
            EditorUtility.SetDirty(inventory);

            Template("template.trainingSword", new SampleEquipment { name = "훈련용 검", slot = "weapon", grade = "common", tags = { "melee" } });
            Template("template.dragonArmor", new SampleEquipment { name = "용의 비늘 갑옷", slot = "armor", grade = "legendary", element = SampleElement.Fire, tags = { "pierce" } });

            var gold = db.OfType<StateKeyData>().FirstOrDefault(s => s.Id == "player.gold");
            var bossRule = Rule<EnemyKilled>("");
            bossRule.filter = new FieldFilter { path = nameof(EnemyKilled.Tags), op = FieldOp.Contains, value = "boss" };
            var bossKills = Stat("bossKills", StatScope.Total, StatAggregate.Sum, bossRule);

            Achievement("dragonSlayer", "보스를 처치했다.",
                new StatCompareCondition { stat = bossKills.Id, op = CompareOp.AtLeast, value = 1 },
                // 보상: 골드 지급 + 아이템(용의 비늘 갑옷 템플릿 사본)을 인벤토리 items에 지급.
                gold != null
                    ? new Reward[] { new AddToStateReward { key = gold.Id, amount = 100 }, new GrantItemReward { key = inventory.Id, path = "items", template = "template.dragonArmor" } }
                    : new Reward[] { new GrantItemReward { key = inventory.Id, path = "items", template = "template.dragonArmor" } });
            Achievement("legendCollector", "전설 등급 장비를 얻었다.",
                new StateFieldCondition
                {
                    key = inventory.Id,
                    filter = new ElementFilter { path = "items", mode = ElementMode.Any, filter = new FieldFilter { path = "grade", op = FieldOp.Equal, value = "legendary" } },
                });
            Achievement("enhanceMaster", "불 속성이 아닌 장비를 3번 이상 강화했다.",
                new StateFieldCondition
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

        static void Keywords(string group, params string[] keywords)
        {
            if (EditorDatabase.Instance.FindById(group) == null) KeywordTools.CreateGroup(group, staged: false);
            foreach (var id in keywords)
                if (EditorDatabase.Instance.FindById(id) == null) KeywordTools.CreateKeyword(group, id, staged: false);
        }

        static void Link(System.Type type, string field, string group, bool required = false)
        {
            var rule = KeywordTools.Connect(type, type.GetField(field), group);
            if (!required) return;
            rule.required = true;
            EditorUtility.SetDirty(rule);
            AssetDatabase.SaveAssetIfDirty(rule);
        }

        static void Template(string id, object value)
        {
            var template = (TemplateData)EditorDatabase.Instance.Create(typeof(TemplateData), id);
            template.templateType = value.GetType().FullName;
            template.value = value;
            EditorUtility.SetDirty(template);
            AssetDatabase.SaveAssetIfDirty(template);
        }

        static StatRule Rule<T>(string field) => new StatRule { eventType = typeof(T).FullName, field = field, multiplier = 1 };

        static StatData Stat(string id, StatScope scope, StatAggregate aggregate, StatRule rule)
        {
            var stat = (StatData)EditorDatabase.Instance.Create(typeof(StatData), id);
            stat.scope = scope;
            stat.aggregate = aggregate;
            stat.rules.Add(rule);
            EditorUtility.SetDirty(stat);
            return stat;
        }

        static AchievementData Achievement(string id, string description, Condition condition, params Reward[] rewards)
        {
            var achievement = (AchievementData)EditorDatabase.Instance.Create(typeof(AchievementData), id);
            achievement.description = description;
            achievement.condition = condition;
            achievement.rewards.AddRange(rewards);
            EditorUtility.SetDirty(achievement);
            AssetDatabase.SaveAssetIfDirty(achievement);
            EditorDatabase.MarkDirty();
            return achievement;
        }
    }
}
