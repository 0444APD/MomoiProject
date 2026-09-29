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
            if (db.OfType<StatData>().Any(s => s.DisplayName == "처치 수 (누적)")) return false;

            var killsRun = Stat("처치 수 (이번 판)", StatScope.Run, StatAggregate.Sum, Rule<EnemyKilled>(nameof(EnemyKilled.Count)));
            var killsTotal = Stat("처치 수 (누적)", StatScope.Total, StatAggregate.Sum, Rule<EnemyKilled>(nameof(EnemyKilled.Count)));
            var bestCombo = Stat("최고 콤보", StatScope.Total, StatAggregate.Max, Rule<EnemyKilled>(nameof(EnemyKilled.Combo)));
            var gold = Stat("골드", StatScope.Total, StatAggregate.Sum, Rule<GoldEarned>(nameof(GoldEarned.Amount)));

            Achievement("첫 사냥", "처음으로 적을 처치했다.",
                new StatCompareCondition { stat = killsTotal.Id, op = CompareOp.AtLeast, value = 1 },
                new AddToStatReward { stat = gold.Id, amount = 50 });

            var hunter = Achievement("몬스터 사냥꾼", "적을 25마리 처치했다.",
                new StatCompareCondition { stat = killsTotal.Id, op = CompareOp.AtLeast, value = 25 },
                new AddToStatReward { stat = gold.Id, amount = 500 });

            var comboMaster = Achievement("연속 공격의 달인", "한 번에 8연속 처치했다.",
                new StatCompareCondition { stat = bestCombo.Id, op = CompareOp.AtLeast, value = 8 });

            Achievement("전설의 전사", "몬스터 사냥꾼과 연속 공격의 달인을 모두 달성했다.",
                new AllOfCondition
                {
                    conditions =
                    {
                        new AchievementUnlockedCondition { achievement = hunter.Id },
                        new AchievementUnlockedCondition { achievement = comboMaster.Id },
                    },
                },
                new AddToStatReward { stat = gold.Id, amount = 2000 });

            Achievement("부자", "골드를 1000 모았다.",
                new StatCompareCondition { stat = gold.Id, op = CompareOp.AtLeast, value = 1000 });

            // 이번 판 수치는 되감기 대상임을 보여주는 용도(Undo하면 되돌아간다).
            _ = killsRun;
            return true;
        }

        // 장비·강화 효과·몬스터 태그 예제: 태그, 상태 키, 템플릿, 조건식이 붙은 수치·도전과제.
        static bool CreateEquipmentExample(EditorDatabase db)
        {
            if (db.FindById("player.inventory") != null) return false;
            foreach (var (id, name) in new[] { ("boss", "보스"), ("legendary", "전설"), ("melee", "근접"), ("lifesteal", "흡혈"), ("fire_resist", "화염 저항") })
                if (db.FindById(id) == null) db.Create(typeof(TagData), name, id);

            // 선택지 목록 + 필드 규칙: SampleEquipment.slot은 코드에 어트리뷰트가 없어도 '장비 부위'에서만 고르고, 필수다.
            var slots = (ChoiceListData)db.Create(typeof(ChoiceListData), "장비 부위", "equipment_slot");
            foreach (var (id, name) in new[] { ("weapon", "무기"), ("armor", "방어구"), ("accessory", "장신구") })
            {
                var choice = (ChoiceData)db.Create(typeof(ChoiceData), name, id);
                choice.list = slots.Id;
                EditorUtility.SetDirty(choice);
            }
            var rule = (FieldRuleData)db.Create(typeof(FieldRuleData), "장비 부위 규칙", "rule.equipment_slot");
            rule.targetType = typeof(SampleEquipment).FullName;
            rule.field = nameof(SampleEquipment.slot);
            rule.source = FieldRuleSource.Choice;
            rule.choiceList = slots.Id;
            rule.required = true;
            EditorUtility.SetDirty(rule);

            // 스프라이트 규격: 장비 아이콘 필드에 원본을 넣으면 아이콘·카드 규격으로 베이크, 목록은 아이콘 규격으로 보인다.
            var itemImages = (SpritePresetGroupData)db.Create(typeof(SpritePresetGroupData), "아이템 이미지", "item_images");
            foreach (var (id, name, w, h) in new[] { ("item_icon", "아이콘", 128, 128), ("item_card", "카드", 256, 384) })
            {
                var preset = (SpritePresetData)db.Create(typeof(SpritePresetData), name, id);
                preset.group = itemImages.Id;
                preset.width = w;
                preset.height = h;
                EditorUtility.SetDirty(preset);
            }
            var iconRule = (FieldRuleData)db.Create(typeof(FieldRuleData), "장비 아이콘 규격", "rule.equipment_icon");
            iconRule.targetType = typeof(SampleEquipment).FullName;
            iconRule.field = nameof(SampleEquipment.icon);
            iconRule.source = FieldRuleSource.Free;
            iconRule.spriteGroup = itemImages.Id;
            iconRule.previewPreset = "item_icon";
            EditorUtility.SetDirty(iconRule);
            var inventory = (StateKeyData)db.Create(typeof(StateKeyData), "인벤토리", "player.inventory");
            inventory.zone = StateZone.Meta;
            inventory.valueType = typeof(SampleInventory).FullName;
            EditorUtility.SetDirty(inventory);

            Template("template.training_sword", "훈련용 검", new SampleEquipment { name = "훈련용 검", slot = "weapon", grade = 1, tags = { "melee" } });
            Template("template.dragon_armor", "용의 비늘 갑옷", new SampleEquipment { name = "용의 비늘 갑옷", slot = "armor", grade = 5, element = SampleElement.Fire, tags = { "legendary" } });

            var gold = db.OfType<StatData>().FirstOrDefault(s => s.DisplayName == "골드");
            var bossRule = Rule<EnemyKilled>("");
            bossRule.filter = new FieldFilter { path = nameof(EnemyKilled.Tags), op = FieldOp.Contains, value = "boss" };
            var bossKills = Stat("보스 처치 수", StatScope.Total, StatAggregate.Sum, bossRule);

            Achievement("용 사냥꾼", "보스를 처치했다.",
                new StatCompareCondition { stat = bossKills.Id, op = CompareOp.AtLeast, value = 1 },
                gold != null ? new[] { new AddToStatReward { stat = gold.Id, amount = 100 } } : new Reward[0]);
            Achievement("전설 수집가", "전설 태그 장비를 얻었다.",
                new StateFieldCondition
                {
                    key = inventory.Id,
                    filter = new ElementFilter { path = "items", mode = ElementMode.Any, filter = new FieldFilter { path = "tags", op = FieldOp.Contains, value = "legendary" } },
                });
            Achievement("강화 장인", "불 속성이 아닌 장비를 3번 이상 강화했다.",
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

        static void Template(string id, string name, object value)
        {
            var template = (TemplateData)EditorDatabase.Instance.Create(typeof(TemplateData), name, id);
            template.templateType = value.GetType().FullName;
            template.value = value;
            EditorUtility.SetDirty(template);
            AssetDatabase.SaveAssetIfDirty(template);
        }

        static StatRule Rule<T>(string field) => new StatRule { eventType = typeof(T).FullName, field = field, multiplier = 1 };

        static StatData Stat(string name, StatScope scope, StatAggregate aggregate, StatRule rule)
        {
            var stat = (StatData)EditorDatabase.Instance.Create(typeof(StatData), name);
            stat.scope = scope;
            stat.aggregate = aggregate;
            stat.rules.Add(rule);
            EditorUtility.SetDirty(stat);
            return stat;
        }

        static AchievementData Achievement(string name, string description, Condition condition, params Reward[] rewards)
        {
            var achievement = (AchievementData)EditorDatabase.Instance.Create(typeof(AchievementData), name);
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
