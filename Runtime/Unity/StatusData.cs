using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GBible.Data
{
    // DB › 상태 효과: 독·화상·보호막·"3턴 뒤 폭발" 같은 버프·디버프. 걸기는 효과 Block '상태 효과 걸기' 또는 Game.ApplyStatus.
    //   중첩·다시 걸 때·지속(영구 / 이벤트 횟수 / 게임 시간)을 정하고,
    //   걸려 있는 동안: 능력치 보정(중첩당 값) + 트리거(이벤트 받기·조건·가로채기)로 발동하는 결과.
    //   걸릴 때 결과 / 끝날 때 결과(지속이 다 돼서 끝날 때만. 풀어서 없앨 때는 돌지 않는다).
    // 결과의 컨텍스트: 사용자(User) = 가진 쪽, Owner = 이 항목 ID. 결과를 코드로 쓰려면 이 ID에 [BindTo](걸려 있는 동안의 결과를 대신한다).
    [Database("상태 효과", Group = "DB", Order = 35, IdPrefix = "status")]
    [CreateAssetMenu(menuName = "MomoiProject/상태 효과", fileName = "NewStatus")]
    public sealed class StatusData : GameData
    {
        [FieldLabel("최대 중첩"), Tooltip("0이면 제한 없음")] public int maxStacks = 1;
        [FieldLabel("다시 걸 때")] public StatusReapply reapply = StatusReapply.AddStacksAndRefresh;
        [FieldLabel("지속")] public StatusDurationSettings duration = new StatusDurationSettings();
        [FieldLabel("능력치 보정")] public List<StatusModifier> modifiers = new List<StatusModifier>();
        [FieldLabel("걸려 있는 동안 트리거"), SerializeReference, TriggerSlot(TriggerKinds.Event | TriggerKinds.Condition | TriggerKinds.Intercept)]
        public Trigger trigger;
        [FieldLabel("가진 쪽 필드"), FieldPath(EmptyLabel = "가진 쪽 모두"),
         Tooltip("이벤트·의도의 이 필드가 가진 쪽 id일 때만 발동합니다(예: 피해 의도의 대상 id → 내가 맞을 때만).")]
        public string holderField = "";
        [FieldLabel("주기(초)"), Tooltip("0보다 크면 걸려 있는 동안 이 초(게임 시간)마다 결과를 냅니다(실시간 지속 피해·재생). 트리거가 없어도 됩니다")] public double pulse;
        [FieldLabel("결과"), SerializeReference, OutcomeSlot(OutcomeKinds.All)] public List<Outcome> outcomes = new List<Outcome>();
        [FieldLabel("걸릴 때 결과"), SerializeReference, OutcomeSlot(OutcomeKinds.Targeted | OutcomeKinds.PlayerData | OutcomeKinds.Coded | OutcomeKinds.Event, BindTo = false)]
        public List<Outcome> onApply = new List<Outcome>();
        [FieldLabel("끝날 때 결과"), SerializeReference, OutcomeSlot(OutcomeKinds.Targeted | OutcomeKinds.PlayerData | OutcomeKinds.Coded | OutcomeKinds.Event, BindTo = false)]
        public List<Outcome> onExpire = new List<Outcome>();
        [FieldLabel("아이콘")] public Sprite icon;
        [FieldLabel("화면에 숨김"), Tooltip("쿨다운처럼 규칙에만 쓰고 상태 아이콘 줄에 보이지 않는 상태 효과")] public bool hidden;

        public StatusDefinition ToDefinition()
        {
            var definition = new StatusDefinition
            {
                Id = Id, DisplayName = DisplayName, MaxStacks = Math.Max(0, maxStacks), Reapply = reapply,
                Duration = duration.kind, Length = duration.length, TickEvent = duration.tickEvent, TickHolderField = duration.holderField,
                When = trigger, HolderField = holderField, Pulse = Math.Max(0, pulse), Hidden = hidden,
            };
            definition.Modifiers.AddRange(modifiers.Where(m => m != null && !string.IsNullOrEmpty(m.attribute)));
            definition.Outcomes.AddRange(BoundOutcomes.Has(Id) ? BoundOutcomes.Create(Id) : outcomes.Where(o => o != null));
            definition.OnApply.AddRange(onApply.Where(o => o != null));
            definition.OnExpire.AddRange(onExpire.Where(o => o != null));
            return definition;
        }

        public override void ValidateData(IValidationContext context)
        {
            if (duration.kind == StatusDuration.Events)
            {
                if (string.IsNullOrEmpty(duration.tickEvent)) context.Error("지속: 횟수를 셀 이벤트를 고르세요.");
                if (duration.length < 1) context.Error("지속: 횟수는 1 이상이어야 합니다.");
            }
            if (duration.kind == StatusDuration.Seconds && duration.length <= 0) context.Error("지속: 시간은 0보다 커야 합니다.");
            if (duration.kind == StatusDuration.Permanent && onExpire.Count > 0)
                context.Warning("지속이 영구라 끝날 때 결과가 돌지 않습니다(풀어서 없앨 때는 돌지 않습니다).");
            foreach (var modifier in modifiers)
                if (modifier == null || string.IsNullOrWhiteSpace(modifier.attribute)) context.Error("능력치 보정: 능력치(숫자 필드 이름)를 적으세요.");
            var bound = BoundOutcomes.Has(Id);
            if (trigger == null && pulse <= 0 && (outcomes.Count > 0 || bound)) context.Warning("걸려 있는 동안 트리거도 주기도 없어 결과가 돌지 않습니다.");
            if (pulse < 0) context.Error("주기는 0 이상이어야 합니다.");
            if (pulse > 0 && outcomes.Count == 0 && !bound) context.Warning("주기가 있는데 결과가 없습니다.");
            if (trigger != null)
            {
                trigger.Validate(context);
                if (outcomes.Count == 0 && !bound) context.Warning("트리거가 있는데 결과가 없습니다.");
            }
            foreach (var list in new[] { outcomes, onApply, onExpire })
                foreach (var outcome in list)
                {
                    if (outcome == null) context.Error("비어 있는 결과 칸이 있습니다(스크립트가 지워졌을 수 있음).");
                    else outcome.Validate(context);
                }
        }
    }

    [Serializable]
    public sealed class StatusDurationSettings
    {
        [FieldLabel("방식")] public StatusDuration kind = StatusDuration.Permanent;
        [FieldLabel("길이"), Tooltip("이벤트 횟수 또는 초"), ShowIf(nameof(kind), StatusDuration.Events, StatusDuration.Seconds)] public double length = 3;
        [FieldLabel("셀 이벤트"), EventTypeRef, FieldPathRoot(FieldPathRootKind.EventType), ShowIf(nameof(kind), StatusDuration.Events)] public string tickEvent = "";
        [FieldLabel("가진 쪽 필드"), FieldPath(EmptyLabel = "가진 쪽 모두"), ShowIf(nameof(kind), StatusDuration.Events),
         Tooltip("이 필드가 가진 쪽 id인 이벤트만 셉니다(예: 턴 끝의 플레이어 id → 내 턴이 끝날 때만).")]
        public string holderField = "";
    }
}

namespace GBible
{
    using GBible.Data;

    // 효과 '상태 효과 걸기': 고른 대상마다 상태 효과를 건다. 다시 걸면 그 상태 효과의 '다시 걸 때' 규칙을 따른다.
    [Serializable]
    [BlockInfo("상태 효과 걸기", Category = "상태 효과", Description = "대상에게 상태 효과를 겁니다. 이미 걸려 있으면 그 상태 효과의 '다시 걸 때' 규칙(중첩·갱신)을 따릅니다.",
               Sentence = "{status} {stacks}중첩 걸기")]
    public sealed class ApplyStatusEffect : Effect
    {
        [DataRef(typeof(StatusData)), FieldLabel("상태 효과")] public string status = "";
        [FieldLabel("중첩")] public int stacks = 1;
        [FieldLabel("길이(0이면 상태 효과의 길이)"), Tooltip("이번에만 쓸 지속(이벤트 횟수 또는 초)")] public double length;

        protected override void Apply(object target) => Game.ApplyStatus(target, status, stacks, Use?.Context?.Owner ?? Use?.Ability?.Id, length > 0 ? length : (double?)null);

        public override string Describe() => stacks == 1 ? $"{status} 걸기" : $"{status} {stacks}중첩 걸기";

        public override void Validate(IValidationContext context)
        {
            if (string.IsNullOrEmpty(status)) context.Error("상태 효과 걸기: 상태 효과를 고르세요.");
            if (stacks < 1) context.Error("상태 효과 걸기: 중첩은 1 이상이어야 합니다.");
        }
    }

    // 효과 '상태 효과 줄이기': 남은 지속을 줄인다(쿨다운 −1초·남은 턴 −1). 다 되면 지속이 끝난 것으로(끝날 때 결과) 끝난다.
    [Serializable]
    [BlockInfo("상태 효과 줄이기", Category = "상태 효과", Description = "대상의 상태 효과의 남은 지속을 줄입니다(이벤트 횟수 또는 초). 다 되면 끝납니다(끝날 때 결과가 돈다). 쿨다운 줄이기에 씁니다.",
               Sentence = "{status} 남은 지속 −{amount}")]
    public sealed class ShortenStatusEffect : Effect
    {
        [DataRef(typeof(StatusData)), FieldLabel("상태 효과")] public string status = "";
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
    //   쿨다운: 능력의 사용 조건 = '사용자·가진 쪽에게 fireballCd <= 0'(없을 때만), 결과에 '상태 효과 걸기(fireballCd, 지속 3턴)'.
    [Serializable]
    [BlockInfo("상태 효과 검사", Category = "상태 효과", Description = "사용자(가진 쪽) 또는 이벤트의 개체에 상태 효과가 몇 중첩 걸려 있는지 봅니다. 없을 때만 = '<= 0'. 쿨다운: 사용 조건에 '쿨다운 상태 효과 <= 0'.",
               Sentence = "{who}의 {status} {op} {stacks}")]
    public sealed class StatusCondition : SimpleCondition
    {
        [FieldLabel("누구")] public StatusSubject who = StatusSubject.User;
        [FieldLabel("이벤트 필드(id)"), ShowIf(nameof(who), StatusSubject.EventField)] public string eventField = "";
        [DataRef(typeof(StatusData)), FieldLabel("상태 효과")] public string status = "";
        [FieldLabel("비교")] public CompareOp op = CompareOp.AtLeast;
        [FieldLabel("중첩")] public int stacks = 1;

        protected override bool Check()
        {
            var target = who == StatusSubject.User ? Context?.User : FromEvent();
            var have = target == null || string.IsNullOrEmpty(status) ? 0 : Game.StatusStacks(target, status);
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
            return $"{whom}의 {status} {symbol} {stacks}";
        }

        public override void Validate(IValidationContext context)
        {
            if (string.IsNullOrEmpty(status)) context.Error("상태 효과 검사: 상태 효과를 고르세요.");
            if (who == StatusSubject.EventField && string.IsNullOrEmpty(eventField)) context.Error("상태 효과 검사: 이벤트 필드를 적으세요.");
        }
    }

    // 효과 '상태 효과 풀기': 대상의 상태 효과를 푼다(중첩을 적으면 그만큼만 줄인다). 끝날 때 결과는 돌지 않는다.
    [Serializable]
    [BlockInfo("상태 효과 풀기", Category = "상태 효과", Description = "대상의 상태 효과를 풉니다. 중첩을 적으면 그만큼만 줄이고, 0이면 통째로 풉니다. 끝날 때 결과는 돌지 않습니다.")]
    public sealed class RemoveStatusEffect : Effect
    {
        [DataRef(typeof(StatusData)), FieldLabel("상태 효과")] public string status = "";
        [FieldLabel("중첩(0이면 모두)")] public int stacks;

        protected override void Apply(object target) => Game.RemoveStatus(target, status, stacks);

        public override string Describe() => stacks <= 0 ? $"{status} 풀기" : $"{status} {stacks}중첩 풀기";

        public override void Validate(IValidationContext context)
        {
            if (string.IsNullOrEmpty(status)) context.Error("상태 효과 풀기: 상태 효과를 고르세요.");
        }
    }
}
