using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GameSystems
{
    [Serializable]
    [BlockInfo("수치 비교", Category = "수치", Description = "수치 값을 기준값과 비교합니다.", Sentence = "{stat} {value} {op}")]
    public sealed class StatCompareCondition : Condition
    {
        [StatRef, FieldLabel("수치")] public string stat;
        [FieldLabel("비교")] public CompareOp op = CompareOp.AtLeast;
        [FieldLabel("기준값")] public double value;

        public override bool Evaluate(GameSession session) =>
            session.Stats.IsDefined(stat) && CompareOps.Apply(op, session.Stats.Get(stat), value);

        public override void CollectDependencies(ICollection<string> stateKeys)
        {
            if (!string.IsNullOrEmpty(stat)) stateKeys.Add(StateKeys.Stat(stat));
        }

        public override string Describe(INameResolver names) =>
            $"{names.StatName(stat)} {value.ToString("0.##", CultureInfo.InvariantCulture)} {CompareOps.Word(op)}";

        public override void Validate(IValidationContext context)
        {
            if (string.IsNullOrEmpty(stat)) context.Error("비교할 수치가 선택되지 않았습니다.");
            else if (!context.StatExists(stat)) context.Error($"존재하지 않는 수치 '{stat}'을 참조합니다.");
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
    [BlockInfo("상태 값 검사", Category = "상태", Description = "저장소 값(클래스·컬렉션 포함)을 조건식으로 검사합니다. 예: 인벤토리 › 아이템 중 [태그 legendary 포함]인 항목이 하나라도 있음")]
    public sealed class StateFieldCondition : Condition
    {
        [StateKeyRef, FieldPathRoot(FieldPathRootKind.StateKey), FieldLabel("상태")] public string key;
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.SerializeReference]
#endif
        [FieldLabel("조건")] public ValueFilter filter;

        public override bool Evaluate(GameSession session) =>
            !string.IsNullOrEmpty(key) && session.State.IsDefined(key) && (filter == null || filter.Test(session.State.Get(key)));

        public override void CollectDependencies(ICollection<string> stateKeys)
        {
            if (!string.IsNullOrEmpty(key)) stateKeys.Add(key);
        }

        public override string Describe(INameResolver names) =>
            $"{(string.IsNullOrEmpty(key) ? "(상태 없음)" : key)}: {ValueFilter.DescribeOrAll(filter)}";

        public override void Validate(IValidationContext context)
        {
            if (string.IsNullOrEmpty(key)) context.Error("검사할 상태 키가 선택되지 않았습니다.");
            if (filter == null) context.Warning("조건식이 비어 있어 상태 키가 있기만 하면 참입니다.");
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
            condition == null ? "(조건 없음) 아님" : $"({condition.Describe(names)}) 아님";

        public override void Validate(IValidationContext context)
        {
            if (condition == null) context.Error("'아님' 안에 조건이 없습니다.");
            else condition.Validate(context);
        }
    }

    [Serializable]
    [BlockInfo("도전과제 달성", Category = "도전과제", Description = "다른 도전과제를 달성했는지 확인합니다.", Sentence = "{achievement} 달성")]
    public sealed class AchievementUnlockedCondition : Condition
    {
        [AchievementRef, FieldLabel("도전과제")] public string achievement;

        public override bool Evaluate(GameSession session) =>
            session.Achievements.IsDefined(achievement) && session.Achievements.IsUnlocked(achievement);

        public override void CollectDependencies(ICollection<string> stateKeys)
        {
            if (!string.IsNullOrEmpty(achievement)) stateKeys.Add(StateKeys.Achievement(achievement));
        }

        public override string Describe(INameResolver names) => $"{names.AchievementName(achievement)} 달성";

        public override void Validate(IValidationContext context)
        {
            if (string.IsNullOrEmpty(achievement)) context.Error("확인할 도전과제가 선택되지 않았습니다.");
            else if (!context.AchievementExists(achievement)) context.Error($"존재하지 않는 도전과제 '{achievement}'를 참조합니다.");
        }
    }

    [Serializable]
    [BlockInfo("획득", Category = "수치", Description = "골드·경험치 같은 합계형 수치를 얻습니다(음수면 차감).", Sentence = "{stat} {amount} 획득")]
    public sealed class AddToStatReward : Reward
    {
        [StatRef, FieldLabel("수치")] public string stat;
        [FieldLabel("수량")] public double amount = 1;

        public override RewardEffect Effect => RewardEffect.Reversible;

        public override void Grant(GameSession session) => session.Stats.Report(stat, amount);

        public override string Describe(INameResolver names) =>
            amount >= 0
                ? $"{names.StatName(stat)} {amount.ToString("0.##", CultureInfo.InvariantCulture)} 획득"
                : $"{names.StatName(stat)} {(-amount).ToString("0.##", CultureInfo.InvariantCulture)} 차감";

        public override void Validate(IValidationContext context)
        {
            if (string.IsNullOrEmpty(stat))
            {
                context.Error("보상 대상 수치가 선택되지 않았습니다.");
                return;
            }
            var info = context.FindStat(stat);
            if (info == null) context.Error($"존재하지 않는 수치 '{stat}'을 참조합니다.");
            else if (info.Value.Aggregate != StatAggregate.Sum) context.Error($"'획득'은 합계형 수치에만 쓸 수 있습니다 ('{stat}'은 합계형이 아님).");
        }
    }
}
