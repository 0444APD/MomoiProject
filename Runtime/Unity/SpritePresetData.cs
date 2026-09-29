using UnityEngine;

namespace GameSystems.Data
{
    // 스프라이트 규격 하나: 출력 크기. 바꾸면 이 규격으로 베이크한 이미지가 모두 다시 베이크된다.
    [Database("스프라이트 규격", Group = "이미지", Order = 1, IdPrefix = "preset")]
    [CreateAssetMenu(menuName = "Game Systems/스프라이트 규격", fileName = "NewSpritePreset")]
    public sealed class SpritePresetData : GameData
    {
        [FieldLabel("묶음"), DataRef(typeof(SpritePresetGroupData))] public string group = "";
        [FieldLabel("가로(px)")] public int width = 256;
        [FieldLabel("세로(px)")] public int height = 256;

        // 규격이 바뀌었는지 알아내는 값(베이크 기록과 비교).
        public int Revision => width * 73856093 ^ height * 19349663;

        public override void ValidateData(IValidationContext context)
        {
            if (string.IsNullOrEmpty(group)) context.Error("묶음이 정해지지 않았습니다.");
            if (width < 8 || height < 8 || width > 8192 || height > 8192) context.Error("크기는 8~8192px 사이여야 합니다.");
        }
    }
}
