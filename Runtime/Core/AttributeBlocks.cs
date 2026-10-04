using System;

namespace GBible
{
    // 기획자가 DB에서 조립하는 능력치·비용 Block.

    // 효과 '능력치 보정': 대상의 숫자 필드에 Modifier를 붙인다. 출처는 이 효과를 가진 항목(아이템·유물·상태 효과 ID).
    [Serializable]
    [BlockInfo("능력치 보정", Category = "능력치", Description = "대상의 능력치(숫자 필드)에 보정을 붙입니다. 같은 칸은 더하고 칸끼리는 곱합니다(플랫 → 퍼센트 → 독립 곱).",
               Sentence = "{attribute} {kind} {value}")]
    public sealed class AttributeModifierEffect : Effect
    {
        [FieldLabel("능력치(숫자 필드 이름)")] public string attribute = "";
        [FieldLabel("칸")] public ModifierKind kind = ModifierKind.Flat;
        [FieldLabel("값(퍼센트는 0.2 = +20%)")] public double value;
        [FieldLabel("영구(프로필에 남김)")] public bool permanent;

        protected override void Apply(object target) =>
            Game.AddModifier(target, attribute, kind, value, Source(Use), permanent);

        internal static string Source(AbilityUse use) => use?.Context?.Owner ?? use?.Ability?.Id ?? "";

        public override string Describe()
        {
            switch (kind)
            {
                case ModifierKind.Flat: return $"{attribute} {(value < 0 ? "" : "+")}{value}";
                case ModifierKind.Percent: return $"{attribute} {(value < 0 ? "" : "+")}{value * 100:0.##}%";
                default: return $"{attribute} ×{1 + value:0.##}";
            }
        }

        public override void Validate(IValidationContext context)
        {
            if (string.IsNullOrWhiteSpace(attribute)) context.Error("능력치 보정: 능력치(숫자 필드 이름)를 적으세요.");
        }
    }

    // 효과 '능력치 보정 떼기': 대상에서 이 항목이 붙인 보정을 모두 뗀다(장비 벗기·효과 끝).
    [Serializable]
    [BlockInfo("능력치 보정 떼기", Category = "능력치", Description = "대상에서 이 항목이 붙인 보정을 모두 뗍니다. 출처를 적으면 그 출처의 것을 뗍니다.")]
    public sealed class RemoveModifiersEffect : Effect
    {
        [FieldLabel("출처(비우면 이 항목)")] public string source = "";

        protected override void Apply(object target) =>
            Game.RemoveModifiers(string.IsNullOrEmpty(source) ? AttributeModifierEffect.Source(Use) : source, target);

        public override string Describe() => string.IsNullOrEmpty(source) ? "보정 떼기" : $"보정 떼기({source})";
    }

    // 결과 '비용 내기': 플레이어 데이터의 숫자를 낸다. 가로채기(비용 지불)를 거치고, 모자라면 행동 전체가 실패한다.
    [Serializable]
    [BlockInfo("비용 내기", Category = "결과", Description = "플레이어 데이터의 숫자(골드·에너지 …)를 냅니다. 모자라면 실패하고 이 행동의 변경이 모두 취소됩니다. '비용 지불' 가로채기가 양을 바꿀 수 있습니다.")]
    public sealed class PayOutcome : Outcome
    {
        [PlayerDataRef, FieldPathRoot(FieldPathRootKind.PlayerDataKey), FieldLabel("플레이어 데이터")] public string key = "";
        [FieldPath(EmptyLabel = "값 전체"), FieldLabel("필드(비우면 값 전체)")] public string path = "";
        [FieldLabel("양")] public double amount = 1;

        public override OutcomeKinds Kind => OutcomeKinds.PlayerData;

        public override void Run(TriggerContext context) => context.Game.Pay(key, path, amount);

        public override string Describe(INameResolver names) => $"비용 {(string.IsNullOrEmpty(path) ? key : key + " › " + path)} {amount}";

        public override void Validate(IValidationContext context)
        {
            if (string.IsNullOrEmpty(key)) context.Error("비용 내기: 플레이어 데이터를 고르세요.");
            if (amount < 0) context.Error("비용 내기: 양은 0 이상이어야 합니다(얻는 것은 '플레이어 데이터에 주기').");
        }
    }
}
