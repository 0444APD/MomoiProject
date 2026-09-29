using UnityEngine;

namespace GameSystems.Data
{
    // 키워드 그룹: 키워드의 부모. 예: '등급'(전설·레어·일반), '장비 태그'(불속성·얼음·전기·도트).
    // 필드 규칙으로 string 필드에 그룹을 연결하면 그 필드는 이 그룹의 키워드에서 고른다
    // (string 필드는 하나, string 목록 필드는 여러 개).
    [Database("키워드 그룹", Group = "식별자", Order = 1, IdPrefix = "keywords")]
    [CreateAssetMenu(menuName = "Game Systems/키워드 그룹", fileName = "NewKeywordGroup")]
    public sealed class KeywordGroupData : GameData
    {
        [FieldLabel("설명")] public string description = "";
    }
}
