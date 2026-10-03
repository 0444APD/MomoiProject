using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GBible.Data
{
    // 로그: 값의 기록. 반영 규칙 = 트리거(이벤트 받기만, AND·OR 가능) → 결과(로그에 기록으로 고정, 세부 칸만 고친다).
    // 다른 기능(도전과제·능력·아이템 …)과 같은 트리거·결과 편집 화면을 쓰고, 잠금만 다르다.
    [Database("로그", Group = "선언", Order = 2, IdPrefix = "stat")]
    [CreateAssetMenu(menuName = "MomoiProject/로그", fileName = "NewStat")]
    public sealed class StatData : GameData, ILegacyData
    {
        [FieldLabel("범위")] public StatScope scope = StatScope.Total;
        [FieldLabel("집계 방식")] public StatAggregate aggregate = StatAggregate.Sum;
        [FieldLabel("반영 규칙"), TriggerSlot(TriggerKinds.Event), OutcomeSlot(OutcomeKinds.Record, Fixed = true)]
        public List<TriggerRule> triggers = new List<TriggerRule>();
        // 예전 저장 형식(이벤트·값·배수·조건 한 줄). DB를 열면 위의 규칙으로 옮겨진다. 코드에서 넣어도 같은 길로 돈다.
        [HideInInspector, TriggerSlot(TriggerKinds.Event), OutcomeSlot(OutcomeKinds.Record)] public List<StatRule> rules = new List<StatRule>();

        public IEnumerable<TriggerRule> AllRules() => rules.Where(r => r != null).Select(r => r.ToTriggerRule()).Concat(triggers.Where(r => r != null));

        public StatDefinition ToDefinition()
        {
            var definition = new StatDefinition
            {
                Id = Id,
                DisplayName = DisplayName,
                Scope = scope,
                Aggregate = aggregate,
            };
            foreach (var rule in rules)
                if (rule != null) definition.Rules.Add(rule);
            foreach (var rule in triggers)
                if (rule != null) definition.Triggers.Add(rule);
            return definition;
        }

        public bool MigrateLegacy()
        {
            if (rules.Count == 0) return false;
            triggers.AddRange(rules.Where(r => r != null).Select(r => r.ToTriggerRule()));
            rules.Clear();
            return true;
        }

        public override void ValidateData(IValidationContext context)
        {
            var i = 0;
            foreach (var rule in AllRules())
            {
                i++;
                if (rule.when == null)
                {
                    context.Error($"규칙 {i}: 트리거가 없습니다.");
                    continue;
                }
                var scoped = new PrefixedContext(context, $"규칙 {i}: ");
                rule.when.Validate(scoped);
                foreach (var outcome in rule.then.Where(o => o != null)) outcome.Validate(scoped);
                // 반영할 값이 트리거의 이벤트에 있는지는 DB 검증기가 모든 기능에 같은 규칙으로 본다(DataValidator.CheckRecord).
                if (aggregate != StatAggregate.Sum && rule.then.OfType<RecordOutcome>().Any(r => string.IsNullOrEmpty(r.field)))
                    context.Warning($"규칙 {i}: 횟수 반영은 합계형 로그에서만 의미가 있습니다.");
            }
        }
    }
}
