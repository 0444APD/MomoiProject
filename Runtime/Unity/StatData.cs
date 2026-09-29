using System.Collections.Generic;
using UnityEngine;

namespace GameSystems.Data
{
    [Database("수치", Group = "진행", Order = 10, IdPrefix = "stat")]
    [CreateAssetMenu(menuName = "Game Systems/수치", fileName = "NewStat")]
    public sealed class StatData : GameData
    {
        [FieldLabel("범위")] public StatScope scope = StatScope.Total;
        [FieldLabel("집계 방식")] public StatAggregate aggregate = StatAggregate.Sum;
        [FieldLabel("반영 규칙")] public List<StatRule> rules = new List<StatRule>();

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
            return definition;
        }

        public override void ValidateData(IValidationContext context)
        {
            for (int i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                if (rule == null) continue;
                var type = TypeResolver.Find(rule.eventType);
                if (type == null)
                {
                    context.Error(string.IsNullOrEmpty(rule.eventType)
                        ? $"규칙 {i + 1}: 이벤트가 선택되지 않았습니다."
                        : $"규칙 {i + 1}: 이벤트 타입 '{rule.eventType}'을 찾을 수 없습니다(이름이 바뀌었는지 확인).");
                    continue;
                }
                if (!string.IsNullOrEmpty(rule.field) && TypeResolver.NumberGetter(type, rule.field, out var error) == null)
                    context.Error($"규칙 {i + 1}: {error}");
                if (aggregate != StatAggregate.Sum && string.IsNullOrEmpty(rule.field))
                    context.Warning($"규칙 {i + 1}: 횟수 반영은 합계형 수치에서만 의미가 있습니다.");
                if (rule.filter == null) continue;
                var problems = new List<string>();
                rule.filter.Validate(type, problems);
                foreach (var problem in problems) context.Error($"규칙 {i + 1} 조건: {problem}");
            }
        }
    }
}
