using UnityEngine;

namespace GBible.Data
{
    // 스프라이트 규격 하나: 출력 크기. 바꾸면 이 규격으로 베이크한 이미지가 모두 다시 베이크된다.
    // 개발자가 Project 창(Assets/Dev 아래 아무 폴더)에서 우클릭 › Create › MomoiProject › 스프라이트 규격으로 만들고 인스펙터(SpritePresetInspector)에서 채운다. DB 탭은 없다.
    [Database("스프라이트 규격", Group = "이미지", Order = 0, IdPrefix = "spritePreset", Page = "sprites", DeveloperAuthored = true, Hidden = true)]
    [CreateAssetMenu(menuName = "MomoiProject/스프라이트 규격", fileName = "NewSpritePreset")]
    public sealed class SpritePresetData : GameData, IGroupedData
    {
        [FieldLabel("그룹"), DataRef(typeof(SpritePresetGroupData))] public string group = "";
        [FieldLabel("가로(px)")] public int width = 256;
        [FieldLabel("세로(px)")] public int height = 256;
        // 그룹 안에서의 순서(끌어서 바꾼다).
        [HideInInspector] public int order;

        int IOrderedData.Order { get => order; set => order = value; }
        string IGroupedData.Group { get => group; set => group = value; }

        // 규격이 바뀌었는지 알아내는 값(베이크 기록과 비교).
        public int Revision => width * 73856093 ^ height * 19349663;

        public override void ValidateData(IValidationContext context)
        {
            if (string.IsNullOrEmpty(group)) context.Error("그룹이 정해지지 않았습니다.");
            if (width < 8 || height < 8 || width > 8192 || height > 8192) context.Error("크기는 8~8192px 사이여야 합니다.");
        }
    }
}
