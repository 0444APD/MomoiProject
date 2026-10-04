using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GBible.Data
{
    // DB › 패시브: "가지고 있으면 발동"(유물·특성·패시브 스킬·장비 효과·축복). 가진 목록(플레이어 데이터)에 이 ID가 있으면 발동 → 결과.
    //   예: 닻 — 가진 목록 run › relics, 발동 '이벤트 받기 → 턴 시작(turn 일치 1)', 결과 '대상에게 효과 → 방어도'.
    //   예: 가시 갑옷 — 발동 '가로채기 → 피해 의도', 가진 쪽 필드 targetId(가진 쪽이 hero일 때), 결과 '의도 고치기 amount −5'.
    // 얻기·잃기는 그 목록에 ID를 넣고 빼는 것(결과 '플레이어 데이터에 주기'·'가중치 표에서 고르기'). 몇 개 가졌나(레벨)는 Game.Owned(ID).
    // 결과를 코드로 쓰려면 이 ID에 [BindTo].
    [Database("패시브", Group = "DB", Order = 37, IdPrefix = "passive")]
    [CreateAssetMenu(menuName = "MomoiProject/패시브", fileName = "NewPassive")]
    public sealed class PassiveData : GameData
    {
        [PlayerDataRef, FieldPathRoot(FieldPathRootKind.PlayerDataKey), FieldLabel("가진 목록(플레이어 데이터)"), Tooltip("이 ID가 들어 있으면 가진 것")] public string ownerKey = "run";
        [FieldPath(EmptyLabel = "값 전체"), FieldLabel("필드(목록·딕셔너리)"), Tooltip("ID 목록·id가 있는 객체 목록·ID → 레벨 딕셔너리. 앞마디에 []를 쓰면 가진 쪽이 여럿(party[].traits)")] public string ownerPath = "relics";
        [FieldLabel("발동"), SerializeReference, TriggerSlot(TriggerKinds.Event | TriggerKinds.Condition | TriggerKinds.Intercept)] public Trigger trigger;
        [FieldLabel("가진 쪽 필드"), FieldPath(EmptyLabel = "가진 쪽 모두"), Tooltip("이벤트·의도의 이 필드가 가진 쪽 id일 때만 발동합니다(예: 피해 의도의 targetId → 가진 쪽이 맞을 때만)")] public string holderField = "";
        [FieldLabel("결과"), SerializeReference, OutcomeSlot(OutcomeKinds.All)] public List<Outcome> outcomes = new List<Outcome>();
        [FieldLabel("아이콘")] public Sprite icon;

        public PassiveDefinition ToDefinition()
        {
            var definition = new PassiveDefinition
            {
                Id = Id, DisplayName = DisplayName, OwnerKey = ownerKey ?? "", OwnerPath = ownerPath ?? "", When = trigger, HolderField = holderField ?? "",
            };
            definition.Outcomes.AddRange(BoundOutcomes.Has(Id) ? BoundOutcomes.Create(Id) : outcomes.Where(o => o != null));
            return definition;
        }

        public override void ValidateData(IValidationContext context)
        {
            if (string.IsNullOrEmpty(ownerKey)) context.Error("가진 목록의 플레이어 데이터를 고르세요.");
            if (trigger == null) context.Error("발동(트리거)이 없습니다.");
            else trigger.Validate(context);
            if (outcomes.Count == 0 && !BoundOutcomes.Has(Id)) context.Warning("결과가 없습니다.");
            foreach (var outcome in outcomes)
            {
                if (outcome == null) context.Error("비어 있는 결과 칸이 있습니다(스크립트가 지워졌을 수 있음).");
                else outcome.Validate(context);
            }
        }
    }
}
