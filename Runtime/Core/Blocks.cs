using System;
using System.Collections.Generic;
using System.Linq;

namespace GBible
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
        // 그룹 블록의 코드식 표시(AND·OR·NOT).
        public string Keyword { get; set; }

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

        // 마우스를 올렸을 때 보이는 설명 문장(없으면 Describe).
        public virtual string Explain(INameResolver names) => Describe(names);

        // 자연어 요약(DB·로그 기본 표시). Describe는 코드 형태(Alt를 누르면 보임).
        public virtual string Natural(INameResolver names) => Describe(names);

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

        // 무엇을 보는지 선언하지 않고, 평가할 때 읽은 값을 시스템이 기록해 감시하는 조건(SimpleCondition과 그것을 품은 조건).
        public virtual bool AutoDependencies => Children.Any(c => c != null && c.AutoDependencies);
    }

    // 보상이 어디에 영향을 주는지. 도전과제 보상은 어느 쪽이든 Undo·되감기로 취소되지 않는다(달성과 같다).
    public enum RewardEffect
    {
        // 플레이어 데이터(저장소) 안에서만 일어난다.
        Reversible,
        // 게임 밖에 영향을 준다(결제, 플랫폼 전송). 지금은 표시용(실행 방식은 같다) — 멀티플레이를 붙일 때 서버만 실행하게 할 표시.
        Irreversible,
    }

    // ── 코딩한 결과 Block 계약(보상) ─────────────────────────────
    // 결과(Outcome)의 한 종류: 대상 없이 게임에 무언가를 한다(플레이어 데이터에 주기, 다이얼로그 재생 …).
    // 프로그래머가 결과를 코딩할 때 이것을 상속한다. Grant 안에서 Context로 트리거의 이벤트·사용자·대상을 볼 수 있다.
    [Serializable]
    public abstract class Reward : Outcome
    {
        [NonSerialized] TriggerContext _context;

        // 지금 실행 중인 트리거 순간(이벤트·사용자·대상). 트리거 밖에서 직접 Grant하면 null.
        protected TriggerContext Context => _context;

        public abstract void Grant(GameSession session);

        public override OutcomeKinds Kind => OutcomeKinds.Coded;

        // 안에서 같은 결과가 다시 돌아도(결과가 쏜 이벤트가 같은 항목을 다시 발동) 끝나면 바깥 순간으로 돌아간다.
        public override void Run(TriggerContext context)
        {
            var previous = _context;
            _context = context;
            try { Grant(context.Session); }
            finally { _context = previous; }
        }

        public abstract RewardEffect Effect { get; }
    }

    public enum CompareOp
    {
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName(">=")]
#endif
        AtLeast,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("<=")]
#endif
        AtMost,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName(">")]
#endif
        GreaterThan,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("<")]
#endif
        LessThan,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("==")]
#endif
        Equal,
    }

    public static class CompareOps
    {
        public static bool Apply(CompareOp op, double left, double right)
        {
            if (double.IsNaN(left)) return false;
            // 같은 숫자 비교 규칙(FieldOps.Near — 아주 작은 차이는 같다): 0.1씩 쌓은 로그도 '같음'·'이상'이 서로 맞는다.
            switch (op)
            {
                case CompareOp.AtLeast: return FieldOps.AtLeast(left, right);
                case CompareOp.AtMost: return FieldOps.AtMost(left, right);
                case CompareOp.GreaterThan: return FieldOps.Greater(left, right);
                case CompareOp.LessThan: return FieldOps.Less(left, right);
                default: return FieldOps.Near(left, right);
            }
        }

        // 코드처럼 보이는 기호: 처치 수 >= 100
        public static string Symbol(CompareOp op)
        {
            switch (op)
            {
                case CompareOp.AtLeast: return ">=";
                case CompareOp.AtMost: return "<=";
                case CompareOp.GreaterThan: return ">";
                case CompareOp.LessThan: return "<";
                default: return "==";
            }
        }

        // 자연어 조각: "처치 수가 100 이상"
        public static string Clause(string left, CompareOp op, string right) => $"{Josa.IGa(left)} {right} {Word(op)}";

        // 설명 문장: "처치 수가 100 이상이라면"
        public static string Natural(string left, CompareOp op, string right)
        {
            switch (op)
            {
                case CompareOp.AtLeast: return $"{Josa.IGa(left)} {right} 이상이라면";
                case CompareOp.AtMost: return $"{Josa.IGa(left)} {right} 이하라면";
                case CompareOp.GreaterThan: return $"{Josa.IGa(left)} {right}보다 크다면";
                case CompareOp.LessThan: return $"{Josa.IGa(left)} {right}보다 작다면";
                default: return $"{Josa.IGa(left)} {right}{Josa.RaMyeon(right)}";
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

    // 한국어 조사: 앞 글자의 받침에 맞춰 이/가, 을/를, 와/과, (이)라면.
    public static class Josa
    {
        static bool? HasFinal(string word)
        {
            // 공백뿐인 이름(이름 칸에 띄어쓰기만)도 판단할 수 없는 것으로 — 끝 글자가 없어 던지면 그 문장을 쓰는 DB 화면·설명이 멈춘다.
            var trimmed = word?.TrimEnd();
            if (string.IsNullOrEmpty(trimmed)) return null;
            var c = trimmed[trimmed.Length - 1];
            if (c >= 0xAC00 && c <= 0xD7A3) return (c - 0xAC00) % 28 != 0;
            if (char.IsDigit(c)) return "013678".IndexOf(c) >= 0;
            return null;
        }

        public static string IGa(string word) => word + (HasFinal(word) switch { true => "이", false => "가", _ => "(이)가" });
        public static string EulReul(string word) => word + (HasFinal(word) switch { true => "을", false => "를", _ => "을(를)" });
        public static string WaGwa(string word) => word + (HasFinal(word) switch { true => "과", false => "와", _ => "와(과)" });
        public static string RaMyeon(string word) => HasFinal(word) switch { true => "이라면", false => "라면", _ => "(이)라면" };
    }
}