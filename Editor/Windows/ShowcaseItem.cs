using UnityEngine;

namespace GameSystems.EditorTools
{
    // UI 샘플 창의 시트·카드 시연용 데이터(메모리에만 존재, 에셋으로 저장하지 않음).
    public sealed class ShowcaseItem : ScriptableObject
    {
        public enum Rarity
        {
            [InspectorName("일반")] Common,
            [InspectorName("희귀")] Rare,
            [InspectorName("영웅")] Epic,
            [InspectorName("전설")] Legendary,
        }

        [GameSystems.FieldLabel("이름")] public string displayName = "아이템";
        [GameSystems.FieldLabel("등급")] public Rarity rarity;
        [GameSystems.FieldLabel("가격")] public int price = 100;
        [GameSystems.FieldLabel("무게")] public float weight = 1.5f;
        [GameSystems.FieldLabel("보유")] public bool owned;
        [GameSystems.FieldLabel("설명")] public string description = "";
    }
}
