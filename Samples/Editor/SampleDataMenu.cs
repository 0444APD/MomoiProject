using System.Linq;
using GameSystems.Data;
using GameSystems.EditorTools;
using UnityEditor;

namespace GameSystems.Samples
{
    // 샘플 통계·도전과제 에셋을 만든다. 이미 있으면 건너뛴다.
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

            // 이번 판 통계는 되감기 대상임을 보여주는 용도(확정 전에도 즉시 바뀐다).
            _ = killsRun;

            AssetDatabase.SaveAssets();
            EditorDatabase.Instance.Sync();
            return true;
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
