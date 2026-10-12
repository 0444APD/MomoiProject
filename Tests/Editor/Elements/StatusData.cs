using System;
using System.Collections.Generic;
using System.Linq;
using GBible;
using GBible.Data;
using UnityEngine;

namespace GBible.Tests
{
    // 선택 요소 '상태 효과'의 DB 종류 — GBible 템플릿(Documentation~/AI/Templates/Elements/StatusData.cs.txt)에서 들였다.
    // 이 클래스는 프로젝트 것이다: 칸을 마음대로 더한다(예: 전투용·런용). 규격(IStatusData.ToDefinition)만 지키면 엔진(걸기·중첩·지속·주기·
    // 행동 직전 이벤트 '상태 효과 걸기'·기본 Block·Game.ApplyStatus …)이 그대로 돈다. 더한 칸은 프로젝트 규칙이 DB에서 읽는다.
    //
    // 독·화상·보호막·"3턴 뒤 폭발" 같은 버프·디버프. 걸기는 효과 Block '상태 효과 걸기' 또는 Game.ApplyStatus.
    //   중첩·다시 걸 때·지속(영구 / 이벤트 횟수 / 게임 시간)을 정하고,
    //   걸려 있는 동안: 스탯 보정(중첩당 값) + 트리거(이벤트 받기·조건·행동 직전 이벤트 받기)로 발동하는 결과.
    //   걸릴 때 결과 / 끝날 때 결과(지속이 다 돼서 끝날 때만. 풀어서 없앨 때는 돌지 않는다).
    // 결과의 컨텍스트: 사용자(User) = 가진 쪽, Owner = 이 항목 ID. 결과를 코드로 쓰려면 이 ID에 [BindTo](걸려 있는 동안의 결과를 대신한다).
    [Database("상태 효과", Group = "DB", Order = 35, IdPrefix = "status")]
    public sealed class StatusData : GameData, IStatusData
    {
        [FieldLabel("종류"), Tooltip("버프·디버프. 디버프만 풀기(정화)·디버프 개수·행동 직전 이벤트 '상태 효과 걸기'의 종류 조건에 쓰입니다")] public StatusKind kind;
        [FieldLabel("걸린 쪽의 패시브 효과를 끔"), Tooltip("켜면 이 상태 효과가 걸린 쪽(조커·유닛·조각)의 패시브 효과가 발동·가진 동안 보정·레벨 모두 꺼집니다(Ink·디버프된 조커·봉인)")] public bool silencesPassives;
        [FieldLabel("최대 중첩"), Tooltip("0이면 제한 없음")] public int maxStacks = 1;
        [FieldLabel("다시 걸 때")] public StatusReapply reapply = StatusReapply.AddStacksAndRefresh;
        [FieldLabel("지속")] public StatusDurationSettings duration = new StatusDurationSettings();
        [FieldLabel("스탯 보정")] public List<StatusModifier> modifiers = new List<StatusModifier>();
        [FieldLabel("걸려 있는 동안 트리거"), SerializeReference, TriggerSlot(TriggerKinds.Event | TriggerKinds.Condition | TriggerKinds.Intercept)]
        public Trigger trigger;
        [FieldLabel("가진 쪽 필드"), FieldPath(EmptyLabel = "가진 쪽 모두"),
         Tooltip("이벤트·행동 직전 이벤트의 이 필드가 가진 쪽 id일 때만 발동합니다(예: 피해 행동 직전 이벤트의 대상 id → 내가 맞을 때만).")]
        public string holderField = "";
        [FieldLabel("주기(초)"), Tooltip("0보다 크면 걸려 있는 동안 이 초(게임 시간)마다 결과를 냅니다(실시간 지속 피해·재생). 트리거가 없어도 됩니다")] public double pulse;
        [FieldLabel("결과"), SerializeReference, OutcomeSlot(OutcomeKinds.All)] public List<Outcome> outcomes = new List<Outcome>();
        [FieldLabel("걸릴 때 결과"), SerializeReference, OutcomeSlot(OutcomeKinds.Targeted | OutcomeKinds.PlayerData | OutcomeKinds.Coded | OutcomeKinds.Event, BindTo = false)]
        public List<Outcome> onApply = new List<Outcome>();
        [FieldLabel("끝날 때 결과"), SerializeReference, OutcomeSlot(OutcomeKinds.Targeted | OutcomeKinds.PlayerData | OutcomeKinds.Coded | OutcomeKinds.Event, BindTo = false)]
        public List<Outcome> onExpire = new List<Outcome>();
        [FieldLabel("아이콘")] public Sprite icon;
        [FieldLabel("화면에 숨김"), Tooltip("쿨다운처럼 규칙에만 쓰고 상태 아이콘 줄에 보이지 않는 상태 효과")] public bool hidden;

        // 규격: 엔진이 쓰는 정의. 프로젝트가 더한 칸은 여기에 넣지 않는다(프로젝트 규칙이 이 항목에서 직접 읽는다).
        public StatusDefinition ToDefinition()
        {
            var definition = new StatusDefinition
            {
                Id = Id, DisplayName = DisplayName, MaxStacks = Math.Max(0, maxStacks), Reapply = reapply,
                Duration = duration.kind, Length = duration.length, TickEvent = duration.tickEvent, TickHolderField = duration.holderField,
                When = trigger, HolderField = holderField, Pulse = Math.Max(0, pulse), Hidden = hidden, Kind = kind, Silences = silencesPassives,
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
                if (modifier == null || string.IsNullOrWhiteSpace(modifier.attribute)) context.Error("스탯 보정: 스탯(숫자 필드 이름)을 적으세요.");
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
