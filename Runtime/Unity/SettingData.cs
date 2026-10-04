using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GBible.Data
{
    // DB › 설정: 설정 화면의 칸 하나(음량·언어·전체 화면·자막 크기·전투 속도 …).
    // 값은 기기마다 하나다(settings.json): 슬롯·Undo·판 저장·멀티플레이와 상관없다.
    //   코드: GameSettings.Number(GameIds.Setting.X) / Toggle / Choice / Text, 바꾸기 GameSettings.Set(id, 값), 받기 GameSettings.Changed.
    //   적용 대상(언어·음량·화면)을 고르면 라이브러리가 켤 때와 바꿀 때 바로 적용한다.
    // 화면 글자: 칸 이름은 텍스트 표의 'id.name'(GameSettings.Label), 선택지는 선택지마다 적은 글(텍스트 키면 번역된 글).
    [Database("설정", Group = "선언", Order = 6, IdPrefix = "setting")]
    [CreateAssetMenu(menuName = "MomoiProject/설정", fileName = "NewSetting")]
    public sealed class SettingData : GameData
    {
        [FieldLabel("분류"), Tooltip("설정 화면에서 묶이는 이름(소리·화면·조작·게임 …)")] public string category = "게임";
        [FieldLabel("값")] public SettingSpec spec = new SettingSpec();
        [FieldLabel("적용 대상"), Tooltip("라이브러리가 바로 적용하는 것. 없음이면 게임 코드가 GameSettings.Changed로 받아 적용한다")]
        public SettingTarget target = SettingTarget.None;

        public override void ValidateData(IValidationContext context)
        {
            var kind = spec.kind;
            if (kind == SettingKind.Slider && spec.min >= spec.max) context.Error("최솟값이 최댓값보다 작아야 합니다.");
            if (kind == SettingKind.Slider && spec.step < 0) context.Error("단위는 0 이상이어야 합니다(0이면 연속).");
            var autoChoices = target == SettingTarget.Language || target == SettingTarget.Resolution;
            if (kind == SettingKind.Choice && spec.choices.Count == 0 && !autoChoices) context.Error("선택지가 없습니다.");
            if (kind == SettingKind.Choice && spec.choices.Count > 0 && !string.IsNullOrEmpty(spec.defaultChoice) && spec.choices.All(c => c.value != spec.defaultChoice))
                context.Error($"기본값 '{spec.defaultChoice}'이(가) 선택지에 없습니다.");
            if (spec.choices.GroupBy(c => c.value).Any(g => g.Count() > 1)) context.Error("선택지 값이 겹칩니다.");
            var expected = ExpectedKind(target);
            if (expected != null && !expected.Contains(kind))
                context.Error($"적용 대상 '{Label(target)}'에는 종류 {string.Join(" 또는 ", expected.Select(k => $"'{Label(k)}'"))}만 맞습니다.");
            if (IsVolume(target) && (spec.min < 0 || spec.max > 1)) context.Warning("음량은 0~1 사이 값입니다.");
        }

        // DB에 보이는 enum 이름(InspectorName). 검증은 에디터에서만 돌므로 형식 정보를 읽어도 된다.
        static string Label(Enum value) =>
            value.GetType().GetField(value.ToString())?.GetCustomAttributes(typeof(InspectorNameAttribute), false).OfType<InspectorNameAttribute>().FirstOrDefault()?.displayName ?? value.ToString();

        public static bool IsVolume(SettingTarget target) =>
            target == SettingTarget.MasterVolume || target == SettingTarget.BgmVolume || target == SettingTarget.VoiceVolume || target == SettingTarget.SfxVolume || target == SettingTarget.UiVolume;

        static SettingKind[] ExpectedKind(SettingTarget target)
        {
            if (IsVolume(target)) return new[] { SettingKind.Slider };
            switch (target)
            {
                case SettingTarget.Language:
                case SettingTarget.Resolution: return new[] { SettingKind.Choice };
                case SettingTarget.Fullscreen:
                case SettingTarget.VSync: return new[] { SettingKind.Toggle };
                case SettingTarget.FrameRate: return new[] { SettingKind.Slider, SettingKind.Choice };
                default: return null;
            }
        }
    }

    public enum SettingKind
    {
        [InspectorName("켜기/끄기")] Toggle,
        [InspectorName("숫자(슬라이더)")] Slider,
        [InspectorName("선택지")] Choice,
        [InspectorName("글")] Text,
    }

    public enum SettingTarget
    {
        [InspectorName("없음(게임 코드)")] None,
        [InspectorName("언어")] Language,
        [InspectorName("전체 음량")] MasterVolume,
        [InspectorName("브금 음량")] BgmVolume,
        [InspectorName("목소리 음량")] VoiceVolume,
        [InspectorName("효과음 음량")] SfxVolume,
        [InspectorName("UI 음량")] UiVolume,
        [InspectorName("전체 화면")] Fullscreen,
        [InspectorName("해상도")] Resolution,
        [InspectorName("수직 동기화")] VSync,
        [InspectorName("최대 프레임(0 = 제한 없음)")] FrameRate,
    }

    [Serializable]
    public sealed class SettingSpec
    {
        [FieldLabel("종류")] public SettingKind kind = SettingKind.Toggle;
        [FieldLabel("기본값"), ShowIf(nameof(kind), SettingKind.Toggle)] public bool defaultToggle;
        [FieldLabel("기본값"), ShowIf(nameof(kind), SettingKind.Slider)] public double defaultNumber;
        [FieldLabel("최솟값"), ShowIf(nameof(kind), SettingKind.Slider)] public double min;
        [FieldLabel("최댓값"), ShowIf(nameof(kind), SettingKind.Slider)] public double max = 1;
        [FieldLabel("단위(0이면 연속)"), ShowIf(nameof(kind), SettingKind.Slider)] public double step;
        [FieldLabel("기본값(선택지의 값, 비우면 첫 선택지)"), ShowIf(nameof(kind), SettingKind.Choice)] public string defaultChoice = "";
        [FieldLabel("선택지(언어·해상도는 비우면 자동)"), ShowIf(nameof(kind), SettingKind.Choice)] public List<SettingChoice> choices = new List<SettingChoice>();
        [FieldLabel("기본값"), ShowIf(nameof(kind), SettingKind.Text)] public string defaultText = "";
    }

    [Serializable]
    public sealed class SettingChoice
    {
        [FieldLabel("값")] public string value = "";
        [FieldLabel("보일 글(텍스트 키 또는 글)")] public string label = "";
    }
}
