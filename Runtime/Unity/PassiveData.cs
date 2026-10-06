using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GBible.Data
{
    // DB › 패시브: "가지고 있으면 발동"(유물·특성·패시브 스킬·장비 효과·축복). 가진 목록(플레이어 데이터)에 이 ID가 있으면 발동 → 결과.
    //   예: 닻 — 가진 목록 run › relics, 발동 '이벤트 받기 → 턴 시작(turn 일치 1)', 결과 '대상에게 효과 → 방어도'.
    //   예: 가시 갑옷 — 발동 '가로채기 → 피해 의도', 가진 쪽 필드 targetId(가진 쪽이 hero일 때), 결과 '의도 고치기 amount −5'.
    //   예: 조커 — 가진 목록 run › jokers(객체 목록), 일치 필드 joker, 놓인 것마다 켬 → 조커 칸 왼쪽부터 차례로 발동(사용자 = 그 조커).
    //   예: 힘의 부적 — 가진 동안 능력치 보정 attack +2(레벨마다), 보정 받을 대상 hero. 트리거 없이 보정만 있어도 된다.
    // 얻기·잃기는 그 목록에 ID를 넣고 빼는 것(결과 '플레이어 데이터에 주기'·'가중치 표에서 고르기'). 몇 개 가졌나(레벨)는 Game.Owned(ID).
    // 결과를 코드로 쓰려면 이 ID에 [BindTo].
    [Database("패시브", Group = "DB", Order = 37, IdPrefix = "passive")]
    [CreateAssetMenu(menuName = "MomoiProject/패시브", fileName = "NewPassive")]
    public sealed class PassiveData : GameData
    {
        [PlayerDataRef, FieldPathRoot(FieldPathRootKind.PlayerDataKey), FieldLabel("가진 목록(플레이어 데이터)"), Tooltip("이 ID가 들어 있으면 가진 것")] public string ownerKey = "run";
        [FieldPath(EmptyLabel = "값 전체"), FieldLabel("필드(목록·딕셔너리)"), Tooltip("ID 목록·id가 있는 객체 목록·ID → 레벨 딕셔너리. 앞마디에 []를 쓰면 가진 쪽이 여럿(party[].traits)")] public string ownerPath = "relics";
        [FieldLabel("일치 필드(객체 목록)"), Tooltip("객체 목록에서 요소의 이 필드(글자·글자 목록)에 이 패시브 ID가 있으면 가진 것입니다(예: run.jokers의 joker, 유닛의 traits). 비우면 요소 자신(ID 글자)·요소의 id")] public string matchField = "";
        [FieldLabel("원본에서 읽기"), Tooltip("일치 필드가 원본(DB › 인스턴스) ID를 담을 때, 그 원본의 이 필드(글자·글자 목록)에 패시브 ID가 있으면 가진 것입니다(예: 원본 Card의 passives). 비우면 일치 필드를 그대로 비교")] public string matchVia = "";
        [FieldLabel("놓인 것마다"), Tooltip("켜면 가진 쪽이 목록의 요소 하나하나입니다(결과의 사용자 = 그 요소). 같은 목록을 보는 '놓인 것마다' 패시브는 목록 순서(자리)대로 발동합니다 — 조커 칸 왼쪽부터, 보드 앞 칸부터")] public bool perElement;
        [FieldLabel("레벨 필드"), Tooltip("요소의 이 숫자 필드가 레벨입니다({level}·Game.Owned·'레벨마다' 보정). 비우면 요소 하나 = 1")] public string levelField = "";
        [FieldLabel("발동"), SerializeReference, TriggerSlot(TriggerKinds.Event | TriggerKinds.Condition | TriggerKinds.Intercept)] public Trigger trigger;
        [FieldLabel("가진 쪽 필드"), FieldPath(EmptyLabel = "가진 쪽 모두", Root = nameof(trigger)), Tooltip("이벤트·의도의 이 필드가 가진 쪽 id일 때만 발동합니다(예: 피해 의도의 targetId → 가진 쪽이 맞을 때만)")] public string holderField = "";
        [FieldLabel("결과"), SerializeReference, OutcomeSlot(OutcomeKinds.All)] public List<Outcome> outcomes = new List<Outcome>();
        // [BindTo] 코드는 '결과'만 대신한다(ToDefinition) — 줄마다의 결과는 그대로 돌므로 BindTo = false(편집 화면에서 잠그지 않는다).
        [FieldLabel("발동 더(다른 순간)"), TriggerSlot(TriggerKinds.Event | TriggerKinds.Condition | TriggerKinds.Intercept), OutcomeSlot(OutcomeKinds.All, BindTo = false), Tooltip("같은 패시브가 다른 순간에 다른 결과를 낼 때 줄마다 트리거 + 결과(손을 낼 때 쌓기 · 버릴 때 빼기 · 점수 때 내기). 가진 쪽 필드·놓인 것마다 순서는 위와 같다")] public List<TriggerRule> rules = new List<TriggerRule>();
        [FieldLabel("주기(초)"), Tooltip("0보다 크면 가진 동안 이 초(게임 시간)마다 가진 쪽마다 결과를 냅니다(트리거 없이도). 레벨별 주기가 있으면 그것")] public double pulse;
        [FieldLabel("레벨별 주기(초, 1레벨부터)")] public List<double> pulseByLevel = new List<double>();
        [FieldLabel("가진 동안 능력치 보정"), Tooltip("가진 동안만 붙는 보정입니다(잃으면 저절로 빠짐). 받는 쪽 = 가진 쪽(놓인 것마다면 그 요소) 또는 아래 '보정 받을 대상'")] public List<PassiveModifier> modifiers = new List<PassiveModifier>();
        [FieldPath(EmptyLabel = "가진 쪽"), FieldLabel("보정 받을 대상"), Tooltip("가진 목록 플레이어 데이터 안의 객체 경로(예: hero, 모든 동료면 party[]). 비우면 가진 쪽. 보정 줄의 받을 대상이 있으면 그 줄은 그것")] public string modifierTarget = "";
        [FieldLabel("보정 조건"), SerializeReference, TriggerSlot(TriggerKinds.Condition), Tooltip("참일 때만 보정이 붙습니다(읽을 때 가진 쪽마다 검사, 사용자 = 가진 쪽). 예: 자리 검사 '같은 줄에 혼자'(Fury), 체력 절반 이하. 비우면 늘")] public Trigger modifierWhen;
        [FieldLabel("여럿일 때"), Tooltip("받는 쪽 하나에 가진 쪽이 여럿일 때: 레벨을 더해 한 번 / 가진 쪽마다 값을 더함(같은 아티팩트 둘) / 가장 높은 레벨 하나만([고유])")] public PassiveStacking stacking = PassiveStacking.SumLevels;
        [FieldLabel("아이콘")] public Sprite icon;

        public PassiveDefinition ToDefinition()
        {
            var definition = new PassiveDefinition
            {
                Id = Id, DisplayName = DisplayName, OwnerKey = ownerKey ?? "", OwnerPath = ownerPath ?? "", When = trigger, HolderField = holderField ?? "",
                MatchField = matchField ?? "", MatchVia = matchVia ?? "", PerElement = perElement, LevelField = levelField ?? "", ModifierTarget = modifierTarget ?? "",
                ModifierWhen = modifierWhen, Stacking = stacking, Pulse = Math.Max(0, pulse),
            };
            // 0인 칸 = 그 레벨에는 주기 없음(칸을 지우면 레벨이 밀린다).
            definition.PulseByLevel.AddRange(pulseByLevel.Select(p => Math.Max(0, p)));
            foreach (var rule in rules.Where(r => r?.when != null))
            {
                var line = new PassiveRule { When = rule.when };
                line.Outcomes.AddRange(rule.then.Where(o => o != null));
                definition.Rules.Add(line);
            }
            definition.Outcomes.AddRange(BoundOutcomes.Has(Id) ? BoundOutcomes.Create(Id) : outcomes.Where(o => o != null));
            definition.Modifiers.AddRange(modifiers.Where(m => m != null && !string.IsNullOrEmpty(m.attribute)));
            return definition;
        }

        public override void ValidateData(IValidationContext context)
        {
            if (string.IsNullOrEmpty(ownerKey)) context.Error("가진 목록의 플레이어 데이터를 고르세요.");
            // 발동·보정·주기가 모두 없어도 된다: 가지고 있는지만 보는 스위치(Game.Owned — Four Fingers 같은 판정 스위치).
            trigger?.Validate(context);
            if (trigger != null && outcomes.Count == 0 && !BoundOutcomes.Has(Id)) context.Warning("결과가 없습니다.");
            // 레벨별 주기가 있으면 '주기(초)' 대신 그것을 쓴다(0 = 그 레벨에는 주기 없음).
            var pulses = pulseByLevel.Count > 0 ? pulseByLevel.Any(p => p > 0) : pulse > 0;
            if (trigger == null && !pulses && (outcomes.Count > 0 || BoundOutcomes.Has(Id))) context.Warning("발동(트리거)도 주기도 없어 결과가 돌지 않습니다. 가진 동안 능력치 보정만 쓰려면 결과를 비우세요.");
            else if (pulseByLevel.Count > 0 && !pulses) context.Warning("레벨별 주기(초)가 모두 0이라 어느 레벨에서도 주기 결과가 돌지 않습니다(레벨별 주기가 있으면 '주기(초)'는 쓰지 않습니다).");
            for (int i = 0; i < rules.Count; i++)
            {
                if (rules[i]?.when == null) context.Error($"발동 더 {i + 1}번째 줄에 트리거가 없습니다.");
                else rules[i].when.Validate(context);
                foreach (var outcome in rules[i]?.then ?? new List<Outcome>()) outcome?.Validate(context);
            }
            modifierWhen?.Validate(context);
            if (pulse < 0 || pulseByLevel.Any(p => p < 0)) context.Error("주기(초)는 0 이상이어야 합니다(0 = 주기 없음).");
            for (int i = 0; i < modifiers.Count; i++)
                if (modifiers[i] == null || string.IsNullOrEmpty(modifiers[i].attribute)) context.Error($"가진 동안 능력치 보정 {i + 1}번째 줄의 능력치(숫자 필드 이름)가 비어 있습니다.");
            foreach (var outcome in outcomes)
            {
                if (outcome == null) context.Error("비어 있는 결과 칸이 있습니다(스크립트가 지워졌을 수 있음).");
                else outcome.Validate(context);
            }
        }
    }
}
