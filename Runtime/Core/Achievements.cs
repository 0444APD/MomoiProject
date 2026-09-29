using System;
using System.Collections.Generic;
using System.Globalization;

namespace GameSystems
{
    public sealed class AchievementDefinition
    {
        public string Id;
        public string DisplayName;
        public string Description;
        public Condition Condition;
        public readonly List<Reward> Rewards = new List<Reward>();
    }

    [GameEvent("도전과제 달성")]
    public sealed class AchievementUnlocked : IDescribable
    {
        public string Id;
        public string DisplayName;

        public string Describe() => $"도전과제 달성: {DisplayName}";
    }

    // 얇은 층: 정의 목록 + 달성 상태(메타 구역) + 달성 알림. 조건 판정은 ConditionWatcher가 한다.
    // 달성은 조건을 만족하는 순간(확정 전 값 포함) 일어나고, 무르기·되감기로 취소되지 않는다.
    // 값(소지 골드·누적 수치)은 무르면 돌아가므로 무르기를 반복해도 한 번의 행동으로 닿는 수준 이상은 쌓이지 않는다.
    // 보상은 확정 시점에 지급한다(확정 전 보상을 바로 주면 무를 때 값 계산이 어긋난다).
    public sealed class AchievementSystem : IInspectable
    {
        readonly Dictionary<string, AchievementDefinition> _definitions = new Dictionary<string, AchievementDefinition>(StringComparer.Ordinal);
        readonly GameSession _session;
        readonly ConditionWatcher _watcher;
        // 확정을 기다리는 보상: (도전과제, 달성 기록 id)
        readonly List<(AchievementDefinition definition, long entryId)> _deferred = new List<(AchievementDefinition, long)>();

        public event Action<AchievementDefinition> Unlocked;

        public AchievementSystem(GameSession session)
        {
            _session = session;
            _watcher = new ConditionWatcher(session);
        }

        public string InspectName => "도전과제";
        public IEnumerable<AchievementDefinition> Definitions => _definitions.Values;
        public ConditionWatcher Watcher => _watcher;
        public int DeferredRewardCount => _deferred.Count;

        public void Define(AchievementDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (string.IsNullOrEmpty(definition.Id)) throw new ArgumentException("[GameSystems] 도전과제 ID가 비어 있습니다.");

            _definitions[definition.Id] = definition;
            _session.State.Define(StateKeys.Achievement(definition.Id), StateZone.Meta, false, definition.DisplayName ?? definition.Id);
            if (!IsUnlocked(definition.Id)) WatchCondition(definition);
        }

        void WatchCondition(AchievementDefinition definition)
        {
            if (definition.Condition == null)
            {
                _session.Journal.Append(JournalKind.Warning, "Achievements", $"도전과제 '{definition.DisplayName}'에 조건이 없어 달성될 수 없습니다.");
                return;
            }
            var id = definition.Id;
            _watcher.Watch(id, definition.Condition, () => Unlock(id, "조건 충족"));
        }

        public bool IsDefined(string id) => id != null && _definitions.ContainsKey(id);

        public AchievementDefinition Find(string id) => id != null && _definitions.TryGetValue(id, out var d) ? d : null;

        public bool IsUnlocked(string id) => _session.State.IsDefined(StateKeys.Achievement(id)) && _session.State.GetBool(StateKeys.Achievement(id));

        public bool TryGetProgress(string id, out double current, out double target)
        {
            current = 0;
            target = 0;
            var definition = Find(id);
            return definition?.Condition != null && definition.Condition.TryGetProgress(_session, out current, out target);
        }

        public void Unlock(string id, string reason = null)
        {
            var definition = Find(id) ?? throw new InvalidOperationException($"[GameSystems] 정의되지 않은 도전과제 '{id}'입니다.");
            if (IsUnlocked(id)) return;

            _watcher.Unwatch(id);
            var entry = _session.Journal.Append(JournalKind.Achievement, "Achievements",
                $"달성: {definition.DisplayName}" + (reason != null ? $" ({reason})" : ""), StateKeys.Achievement(id));

            using (_session.Journal.Cause(entry.Id))
            {
                _session.State.Set(StateKeys.Achievement(id), true);
                if (_session.State.UncommittedCount > 0 || _session.State.InTransaction)
                {
                    if (definition.Rewards.Count > 0)
                    {
                        _deferred.Add((definition, entry.Id));
                        _session.Journal.Append(JournalKind.System, "Achievements", $"보상은 확정 시 지급: {definition.DisplayName}");
                    }
                }
                else
                {
                    GrantRewards(definition);
                }
                _session.Events.Publish(new AchievementUnlocked { Id = id, DisplayName = definition.DisplayName });
            }
            Unlocked?.Invoke(definition);
        }

        void GrantRewards(AchievementDefinition definition)
        {
            foreach (var reward in definition.Rewards)
            {
                if (reward == null) continue;
                _session.Journal.Append(JournalKind.Reward, "Achievements", "보상: " + reward.Describe(_session));
                reward.Grant(_session);
            }
        }

        // 확정 직후 호출: 기다리던 보상을 확정된 값 위에 지급한다(원인은 달성 기록).
        public void GrantDeferred()
        {
            if (_deferred.Count == 0) return;
            var pending = new List<(AchievementDefinition definition, long entryId)>(_deferred);
            _deferred.Clear();
            foreach (var (definition, entryId) in pending)
                using (_session.Journal.Cause(entryId)) GrantRewards(definition);
        }

        // 테스트용: 달성 상태를 되돌리고 다시 감시한다.
        public void ResetProgress(string id)
        {
            var definition = Find(id);
            if (definition == null) return;
            _session.State.Set(StateKeys.Achievement(id), false);
            WatchCondition(definition);
        }

        public void Flush() => _watcher.Flush();
        public void Poll(double time) => _watcher.Poll(time);

        // 불러오기 직후: 이미 조건을 넘은 상태에서 추가된 도전과제도 소급 달성시킨다.
        public void EvaluateAll() => _watcher.EvaluateAll();

        public void Inspect(InspectNode root)
        {
            foreach (var definition in _definitions.Values)
            {
                var unlocked = IsUnlocked(definition.Id);
                string value = unlocked ? "달성" : "미달성";
                if (!unlocked && TryGetProgress(definition.Id, out var current, out var target))
                    value = $"{current.ToString("0.##", CultureInfo.InvariantCulture)} / {target.ToString("0.##", CultureInfo.InvariantCulture)}";
                var node = root.Add(definition.DisplayName ?? definition.Id, value, StateKeys.Achievement(definition.Id));
                node.Note = definition.Condition?.Describe(_session);
            }
        }
    }
}
