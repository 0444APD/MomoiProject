using System;
using System.Collections.Generic;

namespace GameSystems
{
    // 블록 타입의 표시 정보. 에디터의 "+ 추가" 메뉴에 이 이름·분류로 나타난다. (계약: 모든 블록 타입 필수)
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class BlockInfoAttribute : Attribute
    {
        public string DisplayName { get; }
        public string Category { get; set; }
        public string Description { get; set; }
        // 편집 화면의 한 줄 문장. {필드이름}은 클릭해 바꾸는 단어, 나머지는 고정 글자. 예: "{stat} {value} {op}"
        public string Sentence { get; set; }

        public BlockInfoAttribute(string displayName)
        {
            DisplayName = displayName;
        }
    }

    // 편집 화면에 보일 필드 이름. 없으면 필드 이름을 사람이 읽기 쉽게 바꿔 쓴다.
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class FieldLabelAttribute : Attribute
    {
        public string Text { get; }

        public FieldLabelAttribute(string text)
        {
            Text = text;
        }
    }
    // 문자열 필드가 무엇을 가리키는 ID인지 표시한다. 에디터는 직접 입력 대신 선택 목록을 띄우고, 검증기가 존재 여부를 확인한다.
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class StatRefAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class AchievementRefAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class EventTypeRefAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class EventFieldRefAttribute : Attribute
    {
        public string EventTypeField { get; }

        public EventFieldRefAttribute(string eventTypeField)
        {
            EventTypeField = eventTypeField;
        }
    }

    // ── 조건 블록 계약 ───────────────────────────────────────────
    // Evaluate·Describe·CollectDependencies는 필수. 의존 대상이 없다면 PollInterval을 반드시 선언해야 한다
    // (둘 다 없으면 "조용히 동작하지 않는 조건"이므로 계약 테스트가 실패시킨다).
    [Serializable]
    public abstract class Condition
    {
        public abstract bool Evaluate(GameSession session);
        public abstract void CollectDependencies(ICollection<string> stateKeys);
        public abstract string Describe(INameResolver names);

        // 시간 기반 조건만 0보다 크게 선언한다(초 단위 확인 주기).
        public virtual double PollInterval => 0;

        public virtual void Validate(IValidationContext context)
        {
        }

        // 진행도를 표시할 수 있는 조건이면 현재값·목표값을 돌려준다.
        public virtual bool TryGetProgress(GameSession session, out double current, out double target)
        {
            current = 0;
            target = 0;
            return false;
        }

        public virtual IEnumerable<Condition> Children => Array.Empty<Condition>();
    }

    public enum RewardEffect
    {
        // 게임 상태 안에서만 일어나 되돌릴 수 있다.
        Reversible,
        // 확정 시점까지 미뤄서 실행한다(외부 전송 등).
        AfterCommit,
        // 되돌릴 수 없다(결제, 플랫폼 전송).
        Irreversible,
    }

    // ── 보상 블록 계약 ───────────────────────────────────────────
    [Serializable]
    public abstract class Reward
    {
        public abstract void Grant(GameSession session);
        public abstract string Describe(INameResolver names);
        public abstract RewardEffect Effect { get; }

        public virtual void Validate(IValidationContext context)
        {
        }
    }

    public enum CompareOp
    {
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("이상")]
#endif
        AtLeast,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("이하")]
#endif
        AtMost,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("초과")]
#endif
        GreaterThan,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("미만")]
#endif
        LessThan,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("일치")]
#endif
        Equal,
    }

    public static class CompareOps
    {
        public static bool Apply(CompareOp op, double left, double right)
        {
            if (double.IsNaN(left)) return false;
            switch (op)
            {
                case CompareOp.AtLeast: return left >= right;
                case CompareOp.AtMost: return left <= right;
                case CompareOp.GreaterThan: return left > right;
                case CompareOp.LessThan: return left < right;
                default: return Math.Abs(left - right) < 1e-9;
            }
        }

        // 문장에 쓰는 말: "처치 수 100 이상"
        public static string Word(CompareOp op)
        {
            switch (op)
            {
                case CompareOp.AtLeast: return "이상";
                case CompareOp.AtMost: return "이하";
                case CompareOp.GreaterThan: return "초과";
                case CompareOp.LessThan: return "미만";
                default: return "일치";
            }
        }
    }
}
