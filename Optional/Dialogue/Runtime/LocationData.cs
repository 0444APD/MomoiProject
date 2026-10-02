using System.Collections.Generic;
using GBible.Dialogue;
using UnityEngine;

namespace GBible.Data
{
    // 장소. 다이얼로그 그래프의 '배경'은 장소를 고른다(배경 이미지 = 장소). 이름은 텍스트 시트의 '<id>.name'.
    // 시트에는 대사마다 그때의 장소가 나간다.
    [Database("장소", Group = "다이얼로그 시스템", Order = 1, IdPrefix = "location")]
    public sealed class LocationData : GameData
    {
        [FieldLabel("배경 이미지")] public Sprite background;
        [FieldLabel("추가 글(UI의 dialogue-extra-<키> 요소에 보인다)")] public List<ExtraText> extras = new List<ExtraText>();
    }
}
