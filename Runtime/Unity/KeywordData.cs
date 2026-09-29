using UnityEngine;

namespace GameSystems.Data
{
    // 키워드 하나. 식별용 string 값(등급·부위·속성·태그 등)은 모두 키워드다.
    // 필드에는 이 항목의 ID가 들어간다(표시 이름을 바꿔도 값은 그대로, ID를 바꾸면 참조가 함께 바뀐다).
    [Database("키워드", Group = "식별자", Order = 1, IdPrefix = "keyword", Page = "keywords")]
    [CreateAssetMenu(menuName = "Game Systems/키워드", fileName = "NewKeyword")]
    public sealed class KeywordData : GameData
    {
        [FieldLabel("그룹"), DataRef(typeof(KeywordGroupData))] public string group = "";
        [FieldLabel("설명")] public string description = "";
        // 그룹 안에서의 순서(끌어서 바꾼다).
        [HideInInspector] public int order;

        public override void ValidateData(IValidationContext context)
        {
            if (string.IsNullOrEmpty(group)) context.Error("어느 키워드 그룹에 속하는지 정해지지 않았습니다.");
        }
    }
}
