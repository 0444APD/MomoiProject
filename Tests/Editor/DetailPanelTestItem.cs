using UnityEngine;

namespace GBible.Tests
{
    // 상세 패널 테스트용 데이터(메모리에만 존재, 에셋으로 저장하지 않음).
    public sealed class DetailPanelTestItem : ScriptableObject
    {
        public enum Rarity
        {
            [InspectorName("일반")] Common,
            [InspectorName("희귀")] Rare,
            [InspectorName("영웅")] Epic,
            [InspectorName("전설")] Legendary,
        }

        [GBible.FieldLabel("이름")] public string displayName = "아이템";
        [GBible.FieldLabel("등급")] public Rarity rarity;
        [GBible.FieldLabel("가격")] public int price = 100;
        [GBible.FieldLabel("무게")] public float weight = 1.5f;
        [GBible.FieldLabel("보유")] public bool owned;
        [GBible.FieldLabel("설명")] public string description = "";
    }
}
