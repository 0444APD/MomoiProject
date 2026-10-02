using System;
using System.Collections.Generic;
using System.Linq;
using GBible.Data;
using UnityEngine;

namespace GBible.InventorySystem
{
    // 아이템(도감 종류). 모든 아이템에 공통인 칸만 여기에 두고, 종류마다 다른 부분은 기능 리스트로 붙인다(기능 조립).
    // 프로젝트는 ItemFeature를 상속한 기능 클래스(장착·사용 …)를 만들어 붙인다: item.features.GetFeature<Equippable>()
    // 이름·설명처럼 플레이어에게 보일 글자는 텍스트 시트의 '<id>.name' · '<id>.desc'.
    [Database("아이템", Group = "인벤토리 시스템", Order = 0, IdPrefix = "item")]
    public sealed class ItemData : GameData, IUsableData
    {
        [FieldLabel("아이콘")] public Sprite icon;
        [FieldLabel("한 칸에 겹치는 최대 개수"), Min(1)] public int maxStack = 1;
        [FieldLabel("기능"), SerializeReference] public List<ItemFeature> features = new List<ItemFeature>();

        public override void ValidateData(IValidationContext context)
        {
            if (maxStack < 1) context.Error("한 칸에 겹치는 최대 개수는 1 이상이어야 합니다.");
            Features.Validate(features, context);
            foreach (var usable in features.OfType<UsableFeature>())
            {
                var scoped = new PrefixedContext(context, "사용: ");
                usable.when?.Validate(scoped);
                foreach (var outcome in usable.then)
                    if (outcome == null) scoped.Error("비어 있는 결과 칸이 있습니다.");
                    else outcome.Validate(scoped);
            }
        }

        // 사용 기능이 있으면 Game.Use(아이템 ID, 사용자, 대상들)로 쓴다(능력과 같은 트리거·결과).
        AbilityDefinition IUsableData.ToUsable()
        {
            var usable = features.OfType<UsableFeature>().FirstOrDefault();
            if (usable == null) return null;
            var definition = new AbilityDefinition { Id = Id, DisplayName = DisplayName, When = usable.when };
            definition.Outcomes.AddRange(usable.then.Where(o => o != null));
            return definition;
        }
    }

    // 아이템 기능의 바탕. 프로젝트가 상속해 기능을 만든다([Serializable]과 [BlockInfo("이름", Category = "아이템 기능")]을 붙인다).
    [Serializable]
    public abstract class ItemFeature
    {
    }

    // 사용: 쓰는 순간의 결과. 능력·로그·도전과제와 같은 트리거·결과 칸(트리거 = 사용 + 원하면 AND로 사용 조건).
    // 게임 코드: Game.Use(아이템 ID, 사용자, 대상들). 개수 줄이기는 인벤토리 쪽에서(inventory.TryRemove).
    [Serializable]
    [BlockInfo("사용", Category = "아이템 기능", Description = "아이템을 쓰는 순간의 결과(대상에게 효과·플레이어 데이터에 주기 …). 게임 코드는 Game.Use(아이템 ID, 사용자, 대상들).")]
    public sealed class UsableFeature : ItemFeature
    {
        [FieldLabel("트리거"), SerializeReference, TriggerSlot(TriggerKinds.Use | TriggerKinds.Condition)] public Trigger when = new UseTrigger();
        [FieldLabel("결과"), SerializeReference, OutcomeSlot(OutcomeKinds.Targeted | OutcomeKinds.PlayerData | OutcomeKinds.Coded)]
        public List<Outcome> then = new List<Outcome>();
    }
}
