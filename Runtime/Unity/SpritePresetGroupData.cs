using UnityEngine;

namespace GameSystems.Data
{
    // 스프라이트 규격 묶음: 한 용도의 규격들(예: 캐릭터 일러 = 스탠딩A·스탠딩B·아이콘).
    // 필드 규칙으로 스프라이트 필드에 묶음을 연결하면, 그 필드에 원본을 넣을 때 묶음의 규격이 모두 베이크된다.
    [Database("스프라이트 규격 묶음", Group = "이미지", Order = 0, IdPrefix = "spriteGroup", Hidden = true, Page = "sprites")]
    [CreateAssetMenu(menuName = "Game Systems/스프라이트 규격 묶음", fileName = "NewSpritePresetGroup")]
    public sealed class SpritePresetGroupData : GameData, IOrderedData
    {
        [FieldLabel("설명")] public string description = "";
        // 규격 탭에서의 순서(끌어서 바꾼다).
        [HideInInspector] public int order;

        int IOrderedData.Order { get => order; set => order = value; }
    }
}
