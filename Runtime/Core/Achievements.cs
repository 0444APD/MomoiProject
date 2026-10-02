using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GBible
{
    // 도전과제 = 트리거(조건 충족·이벤트 발생, AND·OR) + 결과. 다른 기능과 같은 두 칸(Triggers.cs).
    public sealed class AchievementDefinition
    {
        public string Id;
        public string DisplayName;
        public Trigger When;
        public readonly List<Outcome> Outcomes = new List<Outcome>();
        // 짧은 형태: 조건만으로 된 트리거(When = 조건 충족).
        public Condition Condition
        {
            get => When is ConditionTrigger single ? single.condition : When?.Conditions().FirstOrDefault();
            set => When = value == null ? null : new ConditionTrigger { condition = value };
        }
        // 짧은 형태: 코딩한 결과(보상)만. Outcomes 뒤에 차례로 실행된다.
        public readonly List<Reward> Rewards = new List<Reward>();

        public IEnumerable<Outcome> AllOutcomes() => Outcomes.Concat(Rewards).Where(o => o != null);
    }

    [GameEvent("도전과제 달성")]
    public sealed class AchievementUnlocked : IDescribable
    {
        public string Id;
        public string DisplayName;

        public string Describe() => $"도전과제 달성: {DisplayName}";
    }

    // 얇은 층: 정의 목록 + 달성 상태(메타 구역) + 달성 알림. 트리거 판정은 공용 트리거 감시(TriggerSystem)가 한다.
    // 달성은 조건을 만족하는 순간 일어나고 보상도 그때 지급한다. 달성·보상은 Undo 이력에 넣지 않으므로(Permanent)
    // Undo·되감기로 취소되지 않는다. 행동이 바꾼 값(소지 골드·누적 수치)만 돌아가므로 Undo하고 다시 해도 쌓이지 않는다.
    public sealed class AchievementSystem : IInspectable
    {
        readonly Dictionary<string, AchievementDefinition> _definitions = new Dictionary<string, AchievementDefinition>(StringComparer.Ordinal);
        readonly GameSession _session;
        // 달성 기록(달성 기록 id, 도전과제 ID). 스냅샷 되감기 후 그 구간의 달성을 유지하는 데 쓴다.
        readonly List<(long entryId, string id)> _unlocks = new List<(long, string)>();

        public event Action<AchievementDefinition> Unlocked;

        public AchievementSystem(GameSession session)
        {
            _session = session;
        }

        public string InspectName => "도전과제";
        public IEnumerable<AchievementDefinition> Definitions => _definitions.Values;
        public ConditionWatcher Watcher => _session.Triggers.Watcher;

        public void Define(AchievementDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (string.IsNullOrEmpty(definition.Id)) throw new ArgumentException("[MomoiProject] 도전과제 ID가 비어 있습니다.");

            _definitions[definition.Id] = definition;
            _session.Data.Define(PlayerDataKeys.Achievement(definition.Id), DataZone.Meta, false, definition.DisplayName ?? definition.Id);
            if (!IsUnlocked(definition.Id)) WatchCondition(definition);
        }

        void WatchCondition(AchievementDefinition definition)
        {
            if (definition.When == null)
            {
                _session.Journal.Append(JournalKind.Warning, "Achievements", $"도전과제 '{definition.DisplayName}'에 트리거가 없어 달성될 수 없습니다.");
                return;
            }
            var id = definition.Id;
            _session.Triggers.Unregister(id);
            _session.Triggers.Register(id, definition.When, repeat: false, fire: moment => Unlock(id, moment.Event != null ? EventBus.DisplayNameOf(moment.Event.GetType()) + " 발생" : "조건 충족", moment));
        }

        public bool IsDefined(string id) => id != null && _definitions.ContainsKey(id);

        public AchievementDefinition Find(string id) => id != null && _definitions.TryGetValue(id, out var d) ? d : null;

        public bool IsUnlocked(string id) => _session.Data.IsDefined(PlayerDataKeys.Achievement(id)) && _session.Data.GetBool(PlayerDataKeys.Achievement(id));

        public bool TryGetProgress(string id, out double current, out double target)
        {
            current = 0;
            target = 0;
            var definition = Find(id);
            return definition?.When != null && definition.When.TryGetProgress(_session, out current, out target);
        }

        public void Unlock(string id, string reason = null) => Unlock(id, reason, null);

        void Unlock(string id, string reason, TriggerContext moment)
        {
            var definition = Find(id) ?? throw new InvalidOperationException($"[MomoiProject] 정의되지 않은 도전과제 '{id}'입니다.");
            if (IsUnlocked(id)) return;

            _session.Triggers.Unregister(id);
            var entry = _session.Journal.Append(JournalKind.Achievement, "Achievements",
                $"달성: {definition.DisplayName}" + (reason != null ? $" ({reason})" : ""), PlayerDataKeys.Achievement(id));
            _unlocks.Add((entry.Id, id));

            using (_session.Journal.Cause(entry.Id))
            using (_session.Data.Permanent())
            {
                _session.Data.Set(PlayerDataKeys.Achievement(id), true);
                GrantRewards(definition, moment);
                _session.Events.Publish(new AchievementUnlocked { Id = id, DisplayName = definition.DisplayName });
            }
            Unlocked?.Invoke(definition);
        }

        // 결과 실행(Permanent 안이라 Undo 이력에는 들어가지 않는다). moment: 발동한 순간(이벤트 등), 직접 달성이면 없음.
        void GrantRewards(AchievementDefinition definition, TriggerContext moment)
        {
            moment ??= new TriggerContext { Session = _session, Owner = definition.Id };
            _session.Triggers.RunOutcomes(definition.AllOutcomes(), moment, "Achievements");
        }

        // 스냅샷으로 되감은 뒤: 그 기록 id 이후에 달성한 도전과제의 달성 표시와 보상을 다시 적용한다(달성은 되감기로 취소되지 않는다).
        public void ReapplyUnlocksAfter(long journalId)
        {
            foreach (var (entryId, id) in _unlocks.ToArray())
            {
                if (entryId <= journalId) continue;
                var definition = Find(id);
                if (definition == null || IsUnlocked(id)) continue;
                var entry = _session.Journal.Append(JournalKind.Achievement, "Achievements", $"달성 유지(되감기): {definition.DisplayName}", PlayerDataKeys.Achievement(id));
                using (_session.Journal.Cause(entry.Id))
                using (_session.Data.Permanent())
                {
                    _session.Data.Set(PlayerDataKeys.Achievement(id), true);
                    GrantRewards(definition, null);
                }
            }
        }

        // 테스트용: 달성 상태를 되돌리고 다시 감시한다.
        public void ResetProgress(string id)
        {
            var definition = Find(id);
            if (definition == null) return;
            _unlocks.RemoveAll(u => u.id == id);
            using (_session.Data.Permanent()) _session.Data.Set(PlayerDataKeys.Achievement(id), false);
            WatchCondition(definition);
        }

        public void Flush() => Watcher.Flush();
        public void Poll(double time) => Watcher.Poll(time);

        // 불러오기 직후: 이미 조건을 넘은 상태에서 추가된 도전과제도 소급 달성시킨다.
        public void EvaluateAll() => Watcher.EvaluateAll();

        public void Inspect(InspectNode root)
        {
            foreach (var definition in _definitions.Values)
            {
                var unlocked = IsUnlocked(definition.Id);
                string value = unlocked ? "달성" : "미달성";
                if (!unlocked && TryGetProgress(definition.Id, out var current, out var target))
                    value = $"{current.ToString("0.##", CultureInfo.InvariantCulture)} / {target.ToString("0.##", CultureInfo.InvariantCulture)}";
                var node = root.Add(definition.DisplayName ?? definition.Id, value, PlayerDataKeys.Achievement(definition.Id));
                node.Note = definition.When?.Describe(_session);
            }
        }
    }
}
