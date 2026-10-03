using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GBible.Data
{
    // 도전과제 = 트리거(조건 충족·이벤트 받기, AND·OR) + 결과(무엇이든). 트리거가 처음 참이 되는 순간 달성되고 결과가 실행된다.
    // 다른 기능(로그·능력·아이템 …)과 같은 트리거·결과 편집 화면을 쓰고, 잠금만 다르다.
    [Database("도전과제", Group = "진행", Order = 20, IdPrefix = "achievement")]
    [CreateAssetMenu(menuName = "MomoiProject/도전과제", fileName = "NewAchievement")]
    public sealed class AchievementData : GameData, ILegacyData
    {
        [FieldLabel("아이콘")] public Sprite icon;
        [FieldLabel("트리거"), SerializeReference, TriggerSlot(TriggerKinds.Event | TriggerKinds.Condition)] public Trigger trigger;
        [FieldLabel("결과"), SerializeReference, OutcomeSlot(OutcomeKinds.All)] public List<Outcome> outcomes = new List<Outcome>();
        // 예전 저장 형식(달성 조건·보상). DB를 열면 위의 트리거·결과로 옮겨진다. 코드에서 넣어도 같은 길로 돈다.
        [HideInInspector, SerializeReference, TriggerSlot(TriggerKinds.Event | TriggerKinds.Condition)] public Condition condition;
        [HideInInspector, SerializeReference, OutcomeSlot(OutcomeKinds.All)] public List<Reward> rewards = new List<Reward>();

        // 실제로 쓰는 트리거: trigger, 없으면 예전 조건.
        public Trigger EffectiveTrigger => trigger ?? (condition != null ? new ConditionTrigger { condition = condition } : null);
        public IEnumerable<Outcome> EffectiveOutcomes => outcomes.Concat(rewards).Where(o => o != null);

        public AchievementDefinition ToDefinition()
        {
            var definition = new AchievementDefinition { Id = Id, DisplayName = DisplayName, When = EffectiveTrigger };
            definition.Outcomes.AddRange(EffectiveOutcomes);
            return definition;
        }

        public bool MigrateLegacy()
        {
            if (condition == null && rewards.Count == 0) return false;
            if (trigger == null && condition != null) trigger = new ConditionTrigger { condition = condition };
            condition = null;
            outcomes.AddRange(rewards.Where(r => r != null));
            rewards.Clear();
            return true;
        }

        public override void ValidateData(IValidationContext context)
        {
            var when = EffectiveTrigger;
            if (when == null) context.Error("트리거가 없습니다(언제 달성되는지).");
            else when.Validate(context);
            foreach (var outcome in outcomes.Concat(rewards))
            {
                if (outcome == null) context.Error("비어 있는 결과 칸이 있습니다.");
                else outcome.Validate(context);
            }
        }
    }
}
