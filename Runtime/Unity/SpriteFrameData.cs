using System;
using System.Collections.Generic;
using UnityEngine;

namespace GBible.Data
{
    // 스프라이트 틀: 한 용도(캐릭터 일러·배경·카드 그림 …)의 원본 하나를 잘라 둘 크기 목록. 파일 하나 = 틀 하나.
    // 개발자가 Project 창(Assets/Dev 아래 아무 폴더)에서 우클릭 › Create › MomoiProject › 스프라이트 틀로 만들고 인스펙터(SpriteFrameInspector)의 표에 크기를 적는다.
    // 원본 일러를 틀에 넣으면(틀 인스펙터에 끌어 놓기 · 틀을 쓰는 스프라이트 필드에 넣기) 맞춤 창이 열리고, 표의 모든 크기로 한꺼번에 잘라 구운 이미지를 만든다.
    // 크기마다 따로 잡기(예외 촬영)도 맞춤 창에서 한다. 게임 코드: image.sprite = SpriteSpecs.Get(item.portrait, GameIds.SpriteSize.Character.Icon);
    [Database("스프라이트 틀", Group = "이미지", Order = 0, IdPrefix = "spriteFrame", Hidden = true, DeveloperAuthored = true)]
    [CreateAssetMenu(menuName = "MomoiProject/스프라이트 틀", fileName = "NewSpriteFrame")]
    public sealed class SpriteFrameData : GameData
    {
        public const int MinSize = 8;
        public const int MaxSize = 8192;

        [FieldLabel("크기")] public List<SpriteFrameSize> sizes = new List<SpriteFrameSize>();

        public SpriteFrameSize Find(string name) => string.IsNullOrEmpty(name) ? null : sizes.Find(s => s != null && s.name == name);

        public override void ValidateData(IValidationContext context)
        {
            if (sizes.Count == 0) context.Warning("크기가 없습니다. 인스펙터의 표에 줄을 더하세요.");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var size in sizes)
            {
                if (size == null) continue;
                if (!IsValidName(size.name, out var error)) context.Error($"크기 '{size.name}': {error}");
                else if (!seen.Add(size.name)) context.Error($"크기 이름 '{size.name}'이(가) 겹칩니다(대소문자만 다른 것도 같은 이름).");
                if (!IsValidSize(size.width) || !IsValidSize(size.height))
                    context.Error($"크기 '{size.name}': {MinSize}~{MaxSize}px 사이여야 합니다(지금 {size.width}×{size.height}).");
            }
        }

        public static bool IsValidSize(int pixels) => pixels >= MinSize && pixels <= MaxSize;

        // 크기 이름: lowerCamelCase(영문 소문자로 시작, 영문·숫자, 점 없음). 예: standing, icon, cardFront
        public static bool IsValidName(string name, out string error)
        {
            if (!IdRules.IsValid(name, out error)) return false;
            if (name.IndexOf('.') >= 0)
            {
                error = "크기 이름에는 점(.)을 쓰지 않습니다. 예: standing, icon, cardFront";
                return false;
            }
            return true;
        }
    }

    // 틀의 표 한 줄: 이름과 잘라 낼 크기(px).
    [Serializable]
    public sealed class SpriteFrameSize
    {
        [FieldLabel("이름")] public string name = "";
        [FieldLabel("가로(px)")] public int width = 256;
        [FieldLabel("세로(px)")] public int height = 256;

        // 크기가 바뀌었는지 알아내는 값(구운 기록과 비교).
        public int Revision => width * 73856093 ^ height * 19349663;
    }
}
