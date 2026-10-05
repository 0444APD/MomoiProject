using UnityEngine;

namespace GBible.Data
{
    // 스프라이트 규격 그룹: 한 용도의 규격들(예: 캐릭터 일러 = 스탠딩A·스탠딩B·아이콘).
    // 필드 규칙으로 스프라이트 필드에 그룹을 연결하면, 그 필드에 원본을 넣을 때 그룹의 규격이 모두 베이크된다.
    [Database("스프라이트 규격 그룹", Group = "이미지", Order = 0, IdPrefix = "spriteGroup", Hidden = true, Page = "sprites", DeveloperAuthored = true)]
    [CreateAssetMenu(menuName = "MomoiProject/스프라이트 규격 그룹", fileName = "NewSpritePresetGroup")]
    public sealed class SpritePresetGroupData : GameData, IOrderedData, INestedData
    {
        // 상위 그룹(DB 규격 탭에서 그룹을 다른 그룹 위로 끌어 넣는다). 비어 있으면 맨 위.
        [HideInInspector, DataRef(typeof(SpritePresetGroupData))] public string parent = "";
        // 규격 탭에서의 순서(끌어서 바꾼다).
        [HideInInspector] public int order;

        int IOrderedData.Order { get => order; set => order = value; }
        string INestedData.Parent { get => parent; set => parent = value ?? ""; }
    }
}
