using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GBible
{
    [Serializable]
    [BlockInfo("비교", Category = "로그", Description = "로그 값을 기준값과 비교합니다.", Sentence = "{stat} {op} {value}")]
    public sealed class StatCompareCondition : Condition
    {
        [StatRef, FieldLabel("로그")] public string stat;
        [FieldLabel("비교")] public CompareOp op = CompareOp.AtLeast;
        [FieldLabel("기준값")] public double value;

        public override bool Evaluate(GameSession session) =>
            session.Stats.IsDefined(stat) && CompareOps.Apply(op, session.Stats.Get(stat), value);

        public override void CollectDependencies(ICollection<string> stateKeys)
        {
            if (!string.IsNullOrEmpty(stat)) stateKeys.Add(PlayerDataKeys.Stat(stat));
        }

        public override string Describe(INameResolver names) =>
            $"{names.StatName(stat)} {CompareOps.Symbol(op)} {value.ToString("0.##", CultureInfo.InvariantCulture)}";

        public override string Explain(INameResolver names) =>
            CompareOps.Natural(names.StatName(stat), op, value.ToString("0.##", CultureInfo.InvariantCulture));

        public override string Natural(INameResolver names) =>
            CompareOps.Clause(names.StatName(stat), op, value.ToString("0.##", CultureInfo.InvariantCulture));

        public override void Validate(IValidationContext context)
        {
            if (string.IsNullOrEmpty(stat)) context.Error("비교할 로그가 선택되지 않았습니다.");
            else if (!context.StatExists(stat)) context.Error($"존재하지 않는 로그 '{stat}'을 참조합니다.");
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
    [BlockInfo("플레이어 데이터 검사", Category = "플레이어 데이터", Description = "저장소 값(클래스·컬렉션 포함)을 조건식으로 검사합니다. 예: 인벤토리 › 아이템 중 [태그 legendary 포함]인 항목이 하나라도 있음")]
    public sealed class PlayerDataCondition : Condition
    {
        [PlayerDataRef, FieldPathRoot(FieldPathRootKind.PlayerDataKey), FieldLabel("플레이어 데이터")] public string key;
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.SerializeReference]
#endif
        [FieldLabel("조건")] public ValueFilter filter;

        public override bool Evaluate(GameSession session) =>
            !string.IsNullOrEmpty(key) && session.Data.IsDefined(key) && (filter == null || filter.Test(session.Data.Get(key)));

        public override void CollectDependencies(ICollection<string> stateKeys)
        {
            if (!string.IsNullOrEmpty(key)) stateKeys.Add(key);
        }

        public override string Describe(INameResolver names) =>
            $"{(string.IsNullOrEmpty(key) ? "(플레이어 데이터 없음)" : key)}: {ValueFilter.DescribeOrAll(filter)}";

        public override string Natural(INameResolver names) =>
            $"{(string.IsNullOrEmpty(key) ? "(플레이어 데이터 없음)" : key)}의 {ValueFilter.NaturalOrAll(filter)}";

        public override void Validate(IValidationContext context)
        {
            if (string.IsNullOrEmpty(key)) context.Error("검사할 플레이어 데이터가 선택되지 않았습니다.");
            if (filter == null) context.Warning("조건식이 비어 있어 플레이어 데이터가 있기만 하면 참입니다.");
        }
    }
    [Serializable]
    [BlockInfo("&& AND", Category = "논리", Description = "안의 조건을 모두 만족해야 합니다(그리고).", Keyword = "AND")]
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

        public override string Describe(INameResolver names) => Join(Children, names, " && ");
        public override string Natural(INameResolver names) => JoinNatural(Children, names, " 그리고 ");
        public override string Explain(INameResolver names) => "아래 조건을 모두 만족한다면";

        public override void Validate(IValidationContext context)
        {
            if (!Children.Any()) context.Error("'&& AND' 안에 조건이 없습니다.");
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

        internal static string JoinNatural(IEnumerable<Condition> children, INameResolver names, string separator)
        {
            var parts = children.Select(c => c.Children.Any() ? $"({c.Natural(names)})" : c.Natural(names)).ToList();
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
    [BlockInfo("|| OR", Category = "논리", Description = "안의 조건 중 하나만 만족하면 됩니다(또는).", Keyword = "OR")]
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

        public override string Describe(INameResolver names) => AllOfCondition.Join(Children, names, " || ");
        public override string Natural(INameResolver names) => AllOfCondition.JoinNatural(Children, names, " 또는 ");
        public override string Explain(INameResolver names) => "아래 조건 중 하나라도 만족한다면";

        public override void Validate(IValidationContext context)
        {
            if (!Children.Any()) context.Error("'|| OR' 안에 조건이 없습니다.");
            foreach (var child in Children) child.Validate(context);
        }
    }

    [Serializable]
    [BlockInfo("! NOT", Category = "논리", Description = "안의 조건을 만족하지 않아야 합니다.", Keyword = "NOT")]
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
            condition == null ? "!(true)" : $"!({condition.Describe(names)})";

        public override string Explain(INameResolver names) => "아래 조건을 만족하지 않는다면";
        public override string Natural(INameResolver names) => condition == null ? "아님" : $"({condition.Natural(names)}) 아님";

        public override void Validate(IValidationContext context)
        {
            if (condition == null) context.Error("'! NOT' 안에 조건이 없습니다.");
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
            if (!string.IsNullOrEmpty(achievement)) stateKeys.Add(PlayerDataKeys.Achievement(achievement));
        }

        public override string Describe(INameResolver names) => $"{names.AchievementName(achievement)} 달성";
        public override string Explain(INameResolver names) => $"{Josa.EulReul(names.AchievementName(achievement))} 달성했다면";

        public override void Validate(IValidationContext context)
        {
            if (string.IsNullOrEmpty(achievement)) context.Error("확인할 도전과제가 선택되지 않았습니다.");
            else if (!context.AchievementExists(achievement)) context.Error($"존재하지 않는 도전과제 '{achievement}'를 참조합니다.");
        }
    }
}
