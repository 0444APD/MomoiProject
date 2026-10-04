using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GBible.Data
{
    // 능력: 스킬·버프 발동처럼 "쓰는 순간" 발동하는 것. 트리거 = 사용(+ 원하면 AND로 사용 조건), 결과 = 대상에게 효과·플레이어 데이터에 주기·코딩한 결과.
    // 다른 기능(로그·도전과제·아이템 …)과 같은 트리거·결과 편집 화면을 쓰고, 잠금만 다르다.
    // 게임에서는 Game.Use("능력 ID", 사용자, 고른 대상들).
    [Database("능력", Group = "DB", Order = 30, IdPrefix = "ability")]
    [CreateAssetMenu(menuName = "MomoiProject/능력", fileName = "NewAbility")]
    public sealed class AbilityData : GameData, ILegacyData, IUsableData
    {
        [FieldLabel("트리거"), SerializeReference, TriggerSlot(TriggerKinds.Use | TriggerKinds.Condition)] public Trigger trigger = new UseTrigger();
        [FieldLabel("결과"), SerializeReference, OutcomeSlot(OutcomeKinds.Targeted | OutcomeKinds.PlayerData | OutcomeKinds.Coded)]
        public List<Outcome> outcomes = new List<Outcome>();
        [FieldLabel("쓸 수 없을 때 글"), Tooltip("사용 조건이 맞지 않을 때 플레이어에게 보일 글. 텍스트 키(fail.needEnergy)면 번역된 글, 아니면 그 글 그대로({ability}·{condition} 틀). 비우면 기본 글")]
        public string failText = "";
        // 예전 저장 형식(대상·효과). DB를 열면 결과의 "대상에게 효과" 하나로 옮겨진다.
        [HideInInspector, SerializeReference] public List<TargetSelector> targets = new List<TargetSelector>();
        [HideInInspector, SerializeReference] public List<Effect> effects = new List<Effect>();

        public AbilityDefinition ToDefinition()
        {
            var definition = new AbilityDefinition { Id = Id, DisplayName = DisplayName, When = trigger, FailText = failText };
            definition.Outcomes.AddRange(outcomes.Where(o => o != null));
            foreach (var target in targets) if (target != null) definition.Targets.Add(target);
            foreach (var effect in effects) if (effect != null) definition.Effects.Add(effect);
            return definition;
        }

        AbilityDefinition IUsableData.ToUsable() => ToDefinition();

        public bool MigrateLegacy()
        {
            if (targets.Count == 0 && effects.Count == 0) return false;
            var moved = new TargetedOutcome();
            moved.targets.AddRange(targets.Where(t => t != null));
            moved.effects.AddRange(effects.Where(e => e != null));
            outcomes.Insert(0, moved);
            targets.Clear();
            effects.Clear();
            trigger ??= new UseTrigger();
            return true;
        }

        public override void ValidateData(IValidationContext context)
        {
            if (trigger != null)
            {
                trigger.Validate(context);
                if ((trigger.Kinds() & TriggerKinds.Use) == 0) context.Warning("트리거에 '사용'이 없습니다. 쓰는 순간과 상관없이 조건만으로 쓸 수 있는지가 정해집니다.");
            }
            if (outcomes.Count == 0 && effects.Count == 0) context.Warning("결과가 없습니다. '+ 결과 추가'로 무엇을 할지 더하세요.");
            foreach (var outcome in outcomes)
            {
                if (outcome == null) context.Error("비어 있는 결과 칸이 있습니다(스크립트가 지워졌을 수 있음).");
                else outcome.Validate(context);
            }
            if (effects.Contains(null) || targets.Contains(null)) context.Error("비어 있는 Block이 있습니다(스크립트가 지워졌을 수 있음).");
        }
    }
}
