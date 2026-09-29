using UnityEngine;

namespace GameSystems.Data
{
    // 키워드 하나. 식별용 string 값(등급·부위·속성·태그 등)은 모두 키워드다.
    // 필드에는 이 항목의 ID가 들어간다(표시 이름을 바꿔도 값은 그대로, ID를 바꾸면 참조가 함께 바뀐다).
    [Database("키워드", Group = "식별자", Order = 1, IdPrefix = "keyword", Page = "keywords")]
    [CreateAssetMenu(menuName = "Game Systems/키워드", fileName = "NewKeyword")]
    public sealed class KeywordData : GameData, IGroupedData
    {
        [FieldLabel("그룹"), DataRef(typeof(KeywordGroupData))] public string group = "";
        [FieldLabel("설명")] public string description = "";
        // 문장 속 용어([[id]])로 쓸 때의 모양. 비워 두면 이름 글자만 나온다.
        // TextMeshPro 스타일 시트의 스타일 이름(색·굵기 등)과 스프라이트 애셋의 아이콘 이름.
        [FieldLabel("용어 스타일")] public string textStyle = "";
        [FieldLabel("용어 아이콘")] public string textIcon = "";
        // 그룹 안에서의 순서(끌어서 바꾼다).
        [HideInInspector] public int order;

        int IOrderedData.Order { get => order; set => order = value; }
        string IGroupedData.Group { get => group; set => group = value; }

        public override void ValidateData(IValidationContext context)
        {
            if (string.IsNullOrEmpty(group)) context.Error("어느 키워드 그룹에 속하는지 정해지지 않았습니다.");
        }
    }
}
