using UnityEngine;

namespace GBible.Data
{
    // 키워드 그룹: 키워드의 부모. 도감에서는 키워드 탭 한 화면에서 그룹과 키워드를 함께 다룬다.
    // 예: '등급'(전설·레어·일반), '장비 태그'(불속성·얼음·전기·도트).
    // 필드 규칙으로 string 필드에 그룹을 연결하면 그 필드는 이 그룹의 키워드에서 고른다
    // (string 필드는 하나, string 배열·리스트 필드는 여러 개).
    [Database("키워드 그룹", Group = "선언", Order = 0, IdPrefix = "keywordGroup", Hidden = true, Page = "keywords")]
    [CreateAssetMenu(menuName = "MomoiProject/키워드 그룹", fileName = "NewKeywordGroup")]
    public sealed class KeywordGroupData : GameData, IOrderedData, INestedData
    {
        // 상위 그룹(도감 키워드 탭에서 그룹을 다른 그룹 위로 끌어 넣는다). 비어 있으면 맨 위. 필드 연결은 그룹마다 따로다(하위 그룹의 키워드는 포함하지 않는다).
        [HideInInspector, DataRef(typeof(KeywordGroupData))] public string parent = "";
        // 키워드 탭에서의 순서(끌어서 바꾼다).
        [HideInInspector] public int order;

        int IOrderedData.Order { get => order; set => order = value; }
        string INestedData.Parent { get => parent; set => parent = value ?? ""; }
    }
}
