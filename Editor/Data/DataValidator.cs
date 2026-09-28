using System;
using System.Collections.Generic;
using System.Linq;
using GameSystems.Data;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace GameSystems.EditorTools
{
    public sealed class ValidationIssue
    {
        public GameData Asset;
        public ValidationSeverity Severity;
        public string Message;
    }

    // 저장·빌드 시점 검증. 오류가 있으면 빌드를 막는다.
    public static class DataValidator
    {
        sealed class Context : IValidationContext
        {
            readonly Dictionary<string, StatData> _stats;
            readonly HashSet<string> _achievements;
            public GameData Current;
            public readonly List<ValidationIssue> Issues = new List<ValidationIssue>();

            public Context(IEnumerable<GameData> all)
            {
                _stats = new Dictionary<string, StatData>(StringComparer.Ordinal);
                foreach (var stat in all.OfType<StatData>())
                    if (!string.IsNullOrEmpty(stat.Id)) _stats[stat.Id] = stat;
                _achievements = new HashSet<string>(all.OfType<AchievementData>().Select(a => a.Id).Where(id => !string.IsNullOrEmpty(id)), StringComparer.Ordinal);
            }

            public bool StatExists(string id) => id != null && _stats.ContainsKey(id);
            public bool AchievementExists(string id) => id != null && _achievements.Contains(id);

            public StatDefinitionInfo? FindStat(string id) =>
                id != null && _stats.TryGetValue(id, out var s) ? new StatDefinitionInfo(s.Id, s.scope, s.aggregate) : (StatDefinitionInfo?)null;

            public void Report(ValidationSeverity severity, string message) =>
                Issues.Add(new ValidationIssue { Asset = Current, Severity = severity, Message = message });
        }

        public static List<ValidationIssue> ValidateAll() => Validate(EditorDatabase.Instance.All);

        public static List<ValidationIssue> Validate(IReadOnlyList<GameData> all)
        {
            var context = new Context(all);

            foreach (var group in all.Where(d => !string.IsNullOrEmpty(d.Id)).GroupBy(d => d.Id).Where(g => g.Count() > 1))
                foreach (var data in group)
                {
                    context.Current = data;
                    context.Error($"ID '{data.Id}'가 {group.Count()}개 항목에서 중복됩니다(복제한 에셋일 수 있음). 'ID 재발급'으로 고치세요.");
                }

            foreach (var data in all)
            {
                context.Current = data;
                if (string.IsNullOrEmpty(data.Id)) context.Error("ID가 없습니다.");
                try
                {
                    data.ValidateData(context);
                }
                catch (Exception e)
                {
                    context.Error($"검증 중 오류: {e.Message}");
                }

                if (data is AchievementData achievement && achievement.condition != null)
                {
                    var keys = new HashSet<string>();
                    achievement.condition.CollectDependencies(keys);
                    if (keys.Count == 0 && achievement.condition.PollInterval <= 0)
                        context.Error("조건이 아무 값도 관찰하지 않아 달성될 수 없습니다(블록 계약 위반).");
                }
            }

            FindCycles(all.OfType<AchievementData>().ToList(), context);
            return context.Issues;
        }

        // A 달성 → B 조건 → A 조건 같은 순환 참조.
        static void FindCycles(List<AchievementData> achievements, Context context)
        {
            var edges = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var a in achievements)
            {
                if (string.IsNullOrEmpty(a.Id) || a.condition == null) continue;
                var keys = new HashSet<string>();
                a.condition.CollectDependencies(keys);
                edges[a.Id] = keys.Where(k => k.StartsWith(StateKeys.AchievementPrefix, StringComparison.Ordinal))
                                  .Select(k => k.Substring(StateKeys.AchievementPrefix.Length)).ToList();
            }

            var state = new Dictionary<string, int>(StringComparer.Ordinal);
            var reported = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in edges.Keys) Visit(id, new Stack<string>());

            void Visit(string id, Stack<string> path)
            {
                state.TryGetValue(id, out var s);
                if (s == 2) return;
                if (s == 1)
                {
                    var cycle = path.Reverse().SkipWhile(p => p != id).Concat(new[] { id }).ToList();
                    foreach (var member in cycle.Distinct())
                    {
                        if (!reported.Add(member)) continue;
                        context.Current = achievements.FirstOrDefault(a => a.Id == member);
                        context.Error("도전과제 순환 참조: " + string.Join(" → ", cycle.Select(c => EditorDatabase.Instance.AchievementName(c))));
                    }
                    return;
                }
                state[id] = 1;
                path.Push(id);
                if (edges.TryGetValue(id, out var next))
                    foreach (var n in next) Visit(n, path);
                path.Pop();
                state[id] = 2;
            }
        }
    }

    sealed class DataValidatorBuildGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var errors = DataValidator.ValidateAll().Where(i => i.Severity == ValidationSeverity.Error).ToList();
            if (errors.Count == 0) return;
            var lines = errors.Take(20).Select(e => $"- {(e.Asset != null ? e.Asset.DisplayName : "?")}: {e.Message}");
            throw new BuildFailedException($"[GameSystems] 데이터 검증 오류 {errors.Count}건으로 빌드를 중단합니다. 데이터베이스 창에서 확인하세요.\n" + string.Join("\n", lines));
        }
    }
}
