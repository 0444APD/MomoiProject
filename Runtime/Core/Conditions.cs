using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GameSystems
{
    [Serializable]
    [BlockInfo("통계 비교", Category = "통계", Description = "통계 값을 기준값과 비교합니다.")]
    public sealed class StatCompareCondition : Condition
    {
        [StatRef, FieldLabel("통계")] public string stat;
        [FieldLabel("비교")] public CompareOp op = CompareOp.AtLeast;
        [FieldLabel("기준값")] public double value;

        public override bool Evaluate(GameSession session) =>
            session.Stats.IsDefined(stat) && CompareOps.Apply(op, session.Stats.Get(stat), value);

        public override void CollectDependencies(ICollection<string> stateKeys)
        {
            if (!string.IsNullOrEmpty(stat)) stateKeys.Add(StateKeys.Stat(stat));
        }

        public override string Describe(INameResolver names) =>
            $"{names.StatName(stat)} {CompareOps.Symbol(op)} {value.ToString("0.##", CultureInfo.InvariantCulture)}";

        public override void Validate(IValidationContext context)
        {
            if (string.IsNullOrEmpty(stat)) context.Error("비교할 통계가 선택되지 않았습니다.");
            else if (!context.StatExists(stat)) context.Error($"존재하지 않는 통계 '{stat}'을 참조합니다.");
        }

        public override bool TryGetProgress(GameSession session, out double current, out double target)
        {
            current = 0;
            target = value;
            if (!session.Stats.IsDefined(stat) || (op != CompareOp.AtLeast && op != CompareOp.GreaterThan) || value <= 0) return false;
            var raw = session.Stats.Get(stat);
            current = double.IsNaN(raw) ? 0 : Math.Min(raw, value);
            return true;
        }
    }

    [Serializable]
    [BlockInfo("모두 만족", Category = "논리", Description = "안의 조건을 모두 만족해야 합니다.")]
    public sealed class AllOfCondition : Condition
    {
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.SerializeReference]
#endif
        [FieldLabel("조건")] public List<Condition> conditions = new List<Condition>();

        public override IEnumerable<Condition> Children => conditions.Where(c => c != null);

        public override bool Evaluate(GameSession session) => Children.All(c => c.Evaluate(session));

        public override void CollectDependencies(ICollection<string> stateKeys)
        {
            foreach (var child in Children) child.CollectDependencies(stateKeys);
        }

        public override double PollInterval => MinPoll(Children);

        public override string Describe(INameResolver names) => Join(Children, names, " 그리고 ");

        public override void Validate(IValidationContext context)
        {
            if (!Children.Any()) context.Error("'모두 만족' 안에 조건이 없습니다.");
            foreach (var child in Children) child.Validate(context);
        }

        public override bool TryGetProgress(GameSession session, out double current, out double target) =>
            AverageProgress(Children, session, out current, out target);

        internal static double MinPoll(IEnumerable<Condition> children)
        {
            var polls = children.Select(c => c.PollInterval).Where(p => p > 0).ToList();
            return polls.Count == 0 ? 0 : polls.Min();
        }

        internal static string Join(IEnumerable<Condition> children, INameResolver names, string separator)
        {
            var parts = children.Select(c => c.Children.Any() ? $"({c.Describe(names)})" : c.Describe(names)).ToList();
            return parts.Count == 0 ? "(조건 없음)" : string.Join(separator, parts);
        }

        internal static bool AverageProgress(IEnumerable<Condition> children, GameSession session, out double current, out double target)
        {
            current = 0;
            target = 0;
            int count = 0;
            double sum = 0;
            foreach (var child in children)
            {
                count++;
                if (child.TryGetProgress(session, out var c, out var t) && t > 0) sum += Math.Min(1, c / t);
                else sum += child.Evaluate(session) ? 1 : 0;
            }
            if (count == 0) return false;
            current = sum;
            target = count;
            return true;
        }
    }

    [Serializable]
    [BlockInfo("하나라도 만족", Category = "논리", Description = "안의 조건 중 하나만 만족하면 됩니다.")]
    public sealed class AnyOfCondition : Condition
    {
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.SerializeReference]
#endif
        [FieldLabel("조건")] public List<Condition> conditions = new List<Condition>();

        public override IEnumerable<Condition> Children => conditions.Where(c => c != null);

        public override bool Evaluate(GameSession session) => Children.Any(c => c.Evaluate(session));

        public override void CollectDependencies(ICollection<string> stateKeys)
        {
            foreach (var child in Children) child.CollectDependencies(stateKeys);
        }

        public override double PollInterval => AllOfCondition.MinPoll(Children);

        public override string Describe(INameResolver names) => AllOfCondition.Join(Children, names, " 또는 ");

        public override void Validate(IValidationContext context)
        {
            if (!Children.Any()) context.Error("'하나라도 만족' 안에 조건이 없습니다.");
            foreach (var child in Children) child.Validate(context);
        }
    }

    [Serializable]
    [BlockInfo("아님", Category = "논리", Description = "안의 조건을 만족하지 않아야 합니다.")]
    public sealed class NotCondition : Condition
    {
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.SerializeReference]
#endif
        [FieldLabel("조건")] public Condition condition;

        public override IEnumerable<Condition> Children => condition == null ? Array.Empty<Condition>() : new[] { condition };

        public override bool Evaluate(GameSession session) => condition != null && !condition.Evaluate(session);

        public override void CollectDependencies(ICollection<string> stateKeys) => condition?.CollectDependencies(stateKeys);

        public override double PollInterval => condition?.PollInterval ?? 0;

        public override string Describe(INameResolver names) =>
            condition == null ? "아님 (조건 없음)" : $"아님 ({condition.Describe(names)})";

        public override void Validate(IValidationContext context)
        {
            if (condition == null) context.Error("'아님' 안에 조건이 없습니다.");
            else condition.Validate(context);
        }
    }

    [Serializable]
    [BlockInfo("도전과제 달성", Category = "도전과제", Description = "다른 도전과제를 달성했는지 확인합니다.")]
    public sealed class AchievementUnlockedCondition : Condition
    {
        [AchievementRef, FieldLabel("도전과제")] public string achievement;

        public override bool Evaluate(GameSession session) =>
            session.Achievements.IsDefined(achievement) && session.Achievements.IsUnlocked(achievement);

        public override void CollectDependencies(ICollection<string> stateKeys)
        {
            if (!string.IsNullOrEmpty(achievement)) stateKeys.Add(StateKeys.Achievement(achievement));
        }

        public override string Describe(INameResolver names) => $"'{names.AchievementName(achievement)}' 달성";

        public override void Validate(IValidationContext context)
        {
            if (string.IsNullOrEmpty(achievement)) context.Error("확인할 도전과제가 선택되지 않았습니다.");
            else if (!context.AchievementExists(achievement)) context.Error($"존재하지 않는 도전과제 '{achievement}'를 참조합니다.");
        }
    }

    [Serializable]
    [BlockInfo("통계 더하기", Category = "통계", Description = "합계형 통계에 값을 더합니다. 재화 지급에도 씁니다.")]
    public sealed class AddToStatReward : Reward
    {
        [StatRef, FieldLabel("통계")] public string stat;
        [FieldLabel("수량")] public double amount = 1;

        public override RewardEffect Effect => RewardEffect.Reversible;

        public override void Grant(GameSession session) => session.Stats.Report(stat, amount);

        public override string Describe(INameResolver names) =>
            $"{names.StatName(stat)} {(amount >= 0 ? "+" : "")}{amount.ToString("0.##", CultureInfo.InvariantCulture)}";

        public override void Validate(IValidationContext context)
        {
            if (string.IsNullOrEmpty(stat))
            {
                context.Error("보상 대상 통계가 선택되지 않았습니다.");
                return;
            }
            var info = context.FindStat(stat);
            if (info == null) context.Error($"존재하지 않는 통계 '{stat}'을 참조합니다.");
            else if (info.Value.Aggregate != StatAggregate.Sum) context.Error($"'통계 더하기'는 합계형 통계에만 쓸 수 있습니다 ('{stat}'은 합계형이 아님).");
        }
    }
}
