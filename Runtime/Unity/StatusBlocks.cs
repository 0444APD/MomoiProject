using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GBible
{
    using GBible.Data;

    // 선택 요소 '상태 효과'의 기본 Block. 상태 효과의 DB 종류(데이터 칸 클래스)는 프로젝트가 템플릿에서 들인다(AI/Templates/Elements/StatusData.cs.txt, 규격 IStatusData).
    // 프로젝트에 그 종류가 없으면 이 Block들은 + 메뉴에 보이지 않는다([Element]).

    // 효과 '상태 효과 걸기': 고른 대상마다 상태 효과를 건다. 다시 걸면 그 상태 효과의 '다시 걸 때' 규칙을 따른다.
    [Serializable, Element(Elements.Status)]
    [BlockInfo("상태 효과 걸기", Category = "상태 효과", Description = "대상에게 상태 효과를 겁니다. 이미 걸려 있으면 그 상태 효과의 '다시 걸 때' 규칙(중첩·갱신)을 따릅니다.",
               Sentence = "{status} {stacks}중첩 걸기")]
    public sealed class ApplyStatusEffect : Effect
    {
        [DataRef(typeof(IStatusData)), FieldLabel("상태 효과")] public string status = "";
        [FieldLabel("중첩")] public int stacks = 1;
        [FieldLabel("길이(0이면 상태 효과의 길이)"), Tooltip("이번에만 쓸 지속(이벤트 횟수 또는 초)")] public double length;

        // 중첩은 효과 배율(Use.Context.Power)을 곱한다. 건 개체(by) = 사용자(처치 기여).
        protected override void Apply(object target) =>
            Game.ApplyStatus(target, status, Math.Max(1, GameNumbers.RoundToInt(stacks * (Use?.Context?.Power ?? 1))), Use?.Context?.Owner ?? Use?.Ability?.Id,
                length > 0 ? length : (double?)null, Use?.User);

        public override string Describe() => stacks == 1 ? $"{status} 걸기" : $"{status} {stacks}중첩 걸기";

        public override void Validate(IValidationContext context)
        {
            if (string.IsNullOrEmpty(status)) context.Error("상태 효과 걸기: 상태 효과를 고르세요.");
            if (stacks < 1) context.Error("상태 효과 걸기: 중첩은 1 이상이어야 합니다.");
        }
    }

    // 효과 '상태 효과 줄이기': 남은 지속을 줄인다(쿨다운 −1초·남은 턴 −1). 다 되면 지속이 끝난 것으로(끝날 때 결과) 끝난다.
    [Serializable, Element(Elements.Status)]
    [BlockInfo("상태 효과 줄이기", Category = "상태 효과", Description = "대상의 상태 효과의 남은 지속을 줄입니다(이벤트 횟수 또는 초). 다 되면 끝납니다(끝날 때 결과가 돈다). 쿨다운 줄이기에 씁니다.",
               Sentence = "{status} 남은 지속 −{amount}")]
    public sealed class ShortenStatusEffect : Effect
    {
        [DataRef(typeof(IStatusData)), FieldLabel("상태 효과")] public string status = "";
        [FieldLabel("줄일 양(횟수·초)")] public double amount = 1;

        protected override void Apply(object target) => Game.ShortenStatus(target, status, amount);

        public override string Describe() => $"{status} 남은 지속 −{amount}";

        public override void Validate(IValidationContext context)
        {
            if (string.IsNullOrEmpty(status)) context.Error("상태 효과 줄이기: 상태 효과를 고르세요.");
            if (amount <= 0) context.Error("상태 효과 줄이기: 줄일 양은 0보다 커야 합니다.");
        }
    }

    public enum StatusSubject
    {
        [InspectorName("사용자·가진 쪽")] User,
        [InspectorName("이벤트의 개체")] EventField,
    }

    // 조건 '상태 효과 검사': 사용자(가진 쪽)나 이벤트의 개체에 그 상태 효과가 몇 중첩 걸려 있나.
    //   상태 효과를 비우고 종류를 고르면 그 종류(디버프 …)가 몇 가지 걸려 있나.
    //   쿨다운: 액티브 효과의 사용 조건 = '사용자·가진 쪽에게 fireballCd <= 0'(없을 때만), 결과에 '상태 효과 걸기(fireballCd, 지속 3턴)'.
    [Serializable, Element(Elements.Status)]
    [BlockInfo("상태 효과 검사", Category = "상태 효과", Description = "사용자(가진 쪽) 또는 이벤트의 개체에 상태 효과가 몇 중첩 걸려 있는지 봅니다. 없을 때만 = '<= 0'. 쿨다운: 사용 조건에 '쿨다운 상태 효과 <= 0'.",
               Sentence = "{who}의 {status} {op} {stacks}")]
    public sealed class StatusCondition : SimpleCondition
    {
        [FieldLabel("누구")] public StatusSubject who = StatusSubject.User;
        [FieldLabel("이벤트 필드(id)"), ShowIf(nameof(who), StatusSubject.EventField)] public string eventField = "";
        [DataRef(typeof(IStatusData)), FieldLabel("상태 효과"), Tooltip("비우고 종류를 고르면 그 종류가 몇 가지 걸려 있나")] public string status = "";
        [FieldLabel("종류(상태 효과를 비웠을 때)")] public StatusKind kind;
        [FieldLabel("비교")] public CompareOp op = CompareOp.AtLeast;
        [FieldLabel("중첩(종류면 가짓수)")] public int stacks = 1;

        protected override bool Check()
        {
            var target = who == StatusSubject.User ? Context?.User : FromEvent();
            var have = target == null ? 0 : !string.IsNullOrEmpty(status) ? Game.StatusStacks(target, status) : kind != StatusKind.None ? Game.StatusCount(target, kind) : 0;
            switch (op)
            {
                case CompareOp.AtLeast: return have >= stacks;
                case CompareOp.AtMost: return have <= stacks;
                case CompareOp.GreaterThan: return have > stacks;
                case CompareOp.LessThan: return have < stacks;
                default: return have == stacks;
            }
        }

        object FromEvent()
        {
            var evt = Context?.Event;
            if (evt == null || string.IsNullOrEmpty(eventField)) return null;
            var value = FieldPaths.Resolve(evt, eventField).FirstOrDefault();
            return value is string id ? Game.Find(id) : value;
        }

        public override string Describe(INameResolver names)
        {
            var whom = who == StatusSubject.User ? "사용자" : $"이벤트의 {eventField}";
            var symbol = op == CompareOp.AtLeast ? ">=" : op == CompareOp.AtMost ? "<=" : op == CompareOp.GreaterThan ? ">" : op == CompareOp.LessThan ? "<" : "==";
            return $"{whom}의 {(string.IsNullOrEmpty(status) ? KindName(kind) : status)} {symbol} {stacks}";
        }

        public override void Validate(IValidationContext context)
        {
            if (string.IsNullOrEmpty(status) && kind == StatusKind.None) context.Error("상태 효과 검사: 상태 효과나 종류를 고르세요.");
            if (who == StatusSubject.EventField && string.IsNullOrEmpty(eventField)) context.Error("상태 효과 검사: 이벤트 필드를 적으세요.");
        }

        internal static string KindName(StatusKind kind) => kind == StatusKind.Buff ? "버프" : kind == StatusKind.Debuff ? "디버프" : "종류 없음";
    }

    // 효과 '상태 효과 풀기': 대상의 상태 효과를 푼다(중첩을 적으면 그만큼만 줄인다). 끝날 때 결과는 돌지 않는다.
    [Serializable, Element(Elements.Status)]
    [BlockInfo("상태 효과 풀기", Category = "상태 효과", Description = "대상의 상태 효과를 풉니다. 중첩을 적으면 그만큼만 줄이고, 0이면 통째로 풉니다. 끝날 때 결과는 돌지 않습니다. 상태 효과를 비우고 종류를 고르면 그 종류(디버프 …)를 모두 풉니다(정화).")]
    public sealed class RemoveStatusEffect : Effect
    {
        [DataRef(typeof(IStatusData)), FieldLabel("상태 효과")] public string status = "";
        [FieldLabel("종류 전부(상태 효과를 비웠을 때)")] public StatusKind kind;
        [FieldLabel("중첩(0이면 모두)")] public int stacks;

        protected override void Apply(object target)
        {
            if (!string.IsNullOrEmpty(status)) Game.RemoveStatus(target, status, stacks);
            else if (kind != StatusKind.None) Game.RemoveStatuses(target, kind);
        }

        public override string Describe() => string.IsNullOrEmpty(status) ? $"{StatusCondition.KindName(kind)} 모두 풀기" : stacks <= 0 ? $"{status} 풀기" : $"{status} {stacks}중첩 풀기";

        public override void Validate(IValidationContext context)
        {
            if (string.IsNullOrEmpty(status) && kind == StatusKind.None) context.Error("상태 효과 풀기: 상태 효과나 종류를 고르세요.");
        }
    }

    // 대상 '상태 효과로 거르기': 앞의 대상 중 상태 효과가 걸린(비교가 참인) 것만 — "독에 걸린 적 모두"·"디버프가 없는 아군".
    [Serializable, Element(Elements.Status)]
    [BlockInfo("상태 효과로 거르기", Category = "대상", Description = "앞의 대상 중 상태 효과 중첩 비교가 참인 것만 남깁니다. 없는 것만 = '<= 0'. 상태 효과를 비우고 종류를 고르면 그 종류의 가짓수로 비교합니다.")]
    public sealed class StatusFilterTarget : TargetSelector
    {
        [DataRef(typeof(IStatusData)), FieldLabel("상태 효과"), Tooltip("비우고 종류를 고르면 그 종류가 몇 가지 걸려 있나")] public string status = "";
        [FieldLabel("종류(상태 효과를 비웠을 때)")] public StatusKind kind;
        [FieldLabel("비교")] public CompareOp op = CompareOp.AtLeast;
        [FieldLabel("중첩(종류면 가짓수)")] public int stacks = 1;

        protected override IEnumerable<object> Select(IEnumerable<object> targets) => targets.Where(t => t != null && Holds(t)).ToList();

        bool Holds(object target)
        {
            var have = !string.IsNullOrEmpty(status) ? Game.StatusStacks(target, status) : kind != StatusKind.None ? Game.StatusCount(target, kind) : 0;
            switch (op)
            {
                case CompareOp.AtLeast: return have >= stacks;
                case CompareOp.AtMost: return have <= stacks;
                case CompareOp.GreaterThan: return have > stacks;
                case CompareOp.LessThan: return have < stacks;
                default: return have == stacks;
            }
        }

        public override string Describe() => $"{(string.IsNullOrEmpty(status) ? StatusCondition.KindName(kind) : status)} {(op == CompareOp.AtLeast ? ">=" : op == CompareOp.AtMost ? "<=" : op == CompareOp.GreaterThan ? ">" : op == CompareOp.LessThan ? "<" : "==")} {stacks}";

        public override void Validate(IValidationContext context)
        {
            if (string.IsNullOrEmpty(status) && kind == StatusKind.None) context.Error("상태 효과로 거르기: 상태 효과나 종류를 고르세요.");
        }
    }
}
