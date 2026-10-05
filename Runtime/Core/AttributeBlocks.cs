using System;
using System.Linq;

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
        [FieldLabel("보일 이름(모자랄 때 실패 글에, 텍스트 키 또는 글, 비우면 플레이어 데이터 › 필드)")] public string what = "";

        public override OutcomeKinds Kind => OutcomeKinds.PlayerData;

        public override void Run(TriggerContext context) => context.Game.Pay(key, path, amount, string.IsNullOrEmpty(what) ? null : what);

        public override string Describe(INameResolver names) => $"비용 {(string.IsNullOrEmpty(path) ? key : key + " › " + path)} {amount}";

        public override void Validate(IValidationContext context)
        {
            if (string.IsNullOrEmpty(key)) context.Error("비용 내기: 플레이어 데이터를 고르세요.");
            if (amount < 0) context.Error("비용 내기: 양은 0 이상이어야 합니다(얻는 것은 '플레이어 데이터에 주기').");
        }
    }

    public enum FieldChange
    {
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("더하기")]
#endif
        Add,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("곱하기")]
#endif
        Multiply,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("값으로 바꾸기")]
#endif
        Set,
    }

    // 효과 '필드 바꾸기': 대상의 숫자 필드를 직접 바꾼다(저장되는 값 자체 — 보정이 아니다).
    //   조커 "이 조커 배수 +1"(대상 = 사용자 자신) · 카운터 −1 · 처치 횟수 +1(진화) · 카드 칩 +5(영구).
    //   값에 틀: {level}(이 항목의 레벨) · {level:2/4/7} · {stacks} · {balance:ID}. 능력치를 잠깐 올리는 것은 '능력치 보정'.
    [Serializable]
    [BlockInfo("필드 바꾸기", Category = "필드", Description = "대상의 숫자 필드를 더하기·곱하기·값으로 바꿉니다(저장되는 값 자체). 예: 이 조커 배수 +1(대상 = 사용자 자신), 카운터 −1, 처치 횟수 +1. 잠깐 올리는 것은 '능력치 보정'.",
               Sentence = "{field} {change} {value}")]
    public sealed class ChangeFieldEffect : Effect
    {
        [FieldLabel("필드(대상의 숫자 필드·경로)")] public string field = "";
        [FieldLabel("방법")] public FieldChange change = FieldChange.Add;
        [FieldLabel("값(틀 {level} {level:2/4/7} {stacks} {balance:ID})")] public string value = "1";
        [FieldLabel("0 아래로 내리지 않음")] public bool clampZero;

        protected override void Apply(object target)
        {
            if (target == null || string.IsNullOrEmpty(field)) return;
            var text = ModifyIntentOutcome.Expand(value, Use?.Context);
            if (!double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var amount))
            {
                Game?.Session.Journal.Append(JournalKind.Warning, "Effect", $"필드 바꾸기: 값 '{text}'을(를) 숫자로 읽지 못했습니다.");
                return;
            }
            FieldPaths.SplitLast(field, out var parentPath, out var member);
            foreach (var parent in FieldPaths.Resolve(target, parentPath).ToList())
            {
                if (parent == null || parent.GetType().IsValueType) continue;
                var access = FieldAccess.Find(parent.GetType(), member);
                if (access == null || !access.CanWrite) continue;
                var current = access.Get(parent);
                if (!(current is IConvertible) || current is string || current is bool) continue;
                var now = Convert.ToDouble(current, System.Globalization.CultureInfo.InvariantCulture);
                var next = change == FieldChange.Add ? now + amount : change == FieldChange.Multiply ? now * amount : amount;
                if (clampZero && next < 0) next = 0;
                var underlying = current.GetType();
                access.Set(parent, IsInteger(underlying) ? Convert.ChangeType(Math.Round(next), underlying, System.Globalization.CultureInfo.InvariantCulture) : Convert.ChangeType(next, underlying, System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        static bool IsInteger(Type type) =>
            type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte) || type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) || type == typeof(sbyte);

        public override string Describe() => change == FieldChange.Add ? $"{field} {(value.StartsWith("-") ? "" : "+")}{value}" : change == FieldChange.Multiply ? $"{field} ×{value}" : $"{field} = {value}";

        public override void Validate(IValidationContext context)
        {
            if (string.IsNullOrWhiteSpace(field)) context.Error("필드 바꾸기: 필드를 적으세요.");
            if (value.IndexOf('{') < 0 && !double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
                context.Error("필드 바꾸기: 값에는 숫자(또는 {level}·{stacks}·{balance:ID} 틀)를 적으세요.");
        }
    }
}
