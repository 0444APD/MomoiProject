using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GBible
{
    // 도전과제 = 트리거(조건 충족·이벤트 받기, AND·OR) + 결과. 다른 기능과 같은 두 칸(Triggers.cs).
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
    // 행동 안에서 발동하면 그 바깥 행동이 성공한 뒤에 달성한다(달성 표시·보상·AchievementUnlocked·Unlocked가 그때, 발동한 차례대로).
    // 행동이 실패하거나 미리보기(Preview·CanDo)면 달성하지 않고 트리거도 그대로 감시한다 — 없던 일이 된 실행이 되돌릴 수 없는 달성을 남기지 않게.
    public sealed class AchievementSystem : IInspectable
    {
        readonly Dictionary<string, AchievementDefinition> _definitions = new Dictionary<string, AchievementDefinition>(StringComparer.Ordinal);
        readonly GameSession _session;
        // 달성 기록(달성 기록 id, 도전과제 ID, 발동한 순간). 스냅샷 되감기 후 그 구간의 달성을 유지하는 데 쓴다(결과는 그 순간 — 받은 이벤트 — 으로 다시).
        readonly List<(long entryId, string id, TriggerContext moment)> _unlocks = new List<(long, string, TriggerContext)>();
        // 행동 안에서 발동해 바깥 행동이 끝나기를 기다리는 달성(발동한 순간과 그때의 기록 원인 — 달성 줄이 그 이벤트를 원인으로 잇게 — 와 그 행동의 번호).
        readonly List<(string id, string reason, TriggerContext moment, long cause, long transaction)> _pending = new List<(string, string, TriggerContext, long, long)>();

        public event Action<AchievementDefinition> Unlocked;

        public AchievementSystem(GameSession session)
        {
            _session = session;
            // 행동이 취소되면 그 행동에서 기다리던 달성을 버린다(트리거는 TriggerSystem이 되살린다). 행동 안에서 일부만 되돌린 이벤트(실패한 이벤트 예약)로 발동한 것도.
            // 그 행동 것만: 성공한 행동의 달성은 CommitPending 전에(행동 뒤에 받는 쪽 안에서) 다른 행동이 실패하거나 미리보기(CanDo)가 끝나도 남는다 — 트리거는 이미 빠졌다.
            session.Actions.TransactionAborted += transaction => _pending.RemoveAll(p => p.transaction == transaction);
            session.Events.Retracted += evt => _pending.RemoveAll(p => p.moment?.Event != null && ReferenceEquals(p.moment.Event, evt));
        }

        public string InspectName => "도전과제";
        public IEnumerable<AchievementDefinition> Definitions => _definitions.Values;
        public ConditionWatcher Watcher => _session.Triggers.Watcher;

        public void Define(AchievementDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (string.IsNullOrEmpty(definition.Id)) throw new ArgumentException("[MomoiProject] 도전과제 ID가 비어 있습니다.");

            // 코드로 쓴 결과가 연결되어 있으면([BindTo]) 그것이 이 도전과제의 결과다(에디터에서 고른 결과 대신).
            if (BoundOutcomes.Has(definition.Id))
            {
                definition.Outcomes.Clear();
                definition.Rewards.Clear();
                definition.Outcomes.AddRange(BoundOutcomes.Create(definition.Id));
            }
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
            _session.Triggers.Register(id, definition.When, repeat: false, fire: moment => Unlock(id, moment.Event != null ? EventBus.DisplayNameOf(moment.Event.GetType()) + " 받음" : "조건 충족", moment));
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
            // 미리보기(해 보기만 하는 실행)에서는 달성하지 않는다(달성은 되돌리지 않는 변경이라 미리보기 뒤에도 남는다).
            // 발동한 트리거는 미리보기가 끝나면 되살아난다(TriggerSystem).
            if (_session.Actions.IsPreviewing) return;
            // 행동 안: 바깥 행동이 성공한 뒤에 달성한다(CommitPending). 실패하면 버려진다.
            if (_session.Data.InTransaction)
            {
                if (!_pending.Any(p => p.id == id)) _pending.Add((id, reason, moment, _session.Journal.CurrentCause, _session.Data.ActiveTransactionId));
                return;
            }

            _session.Triggers.Unregister(id);
            var entry = _session.Journal.Append(JournalKind.Achievement, "Achievements",
                $"달성: {definition.DisplayName}" + (reason != null ? $" ({reason})" : ""), PlayerDataKeys.Achievement(id));
            _unlocks.Add((entry.Id, id, moment));

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

        // 바깥 행동이 성공한 뒤(세션이 부른다): 그 행동 안에서 발동한 달성을 발동한 차례대로 달성한다.
        // 행동은 이미 끝났으므로 보상 하나의 오류가 행동을 실패로 보이게 하거나 뒤의 달성을 막지 않게 기록에 남기고 넘어간다.
        internal void CommitPending()
        {
            if (_pending.Count == 0) return;
            var ready = _pending.ToArray();
            _pending.Clear();
            foreach (var (id, reason, moment, cause, _) in ready)
            {
                try
                {
                    using (_session.Journal.Cause(cause)) Unlock(id, reason, moment);
                }
                catch (Exception e)
                {
                    _session.Journal.Append(JournalKind.Error, "Achievements", $"도전과제 '{_session.AchievementName(id)}'을(를) 달성하다 오류가 났습니다: {e.Message}");
                }
            }
        }

        // 프로필을 불러온 뒤(ImportMeta): 감시를 불러온 달성 상태에 맞춘다. 달성한 것은 감시를 그만두고, 달성하지 않은 것은 다시 감시한다
        // (이 세션에서 달성했지만 불러온 프로필에서는 아직인 것). 다시 감시한 것은 다음 Flush에서 지금 값으로 평가된다(이미 넘었으면 소급 달성).
        // 여기서 바로 평가하지 않는다: 판도 이어서 불러오는 중이면(테스트 상황) 반쯤 불러온 상태로 달성하게 된다 — 판 불러오기 끝의 Flush가 평가한다.
        internal void SyncWithData()
        {
            _unlocks.RemoveAll(u => !IsUnlocked(u.id));
            foreach (var definition in _definitions.Values.ToList())
            {
                if (IsUnlocked(definition.Id))
                {
                    _session.Triggers.Unregister(definition.Id);
                    continue;
                }
                if (definition.When == null || _session.Triggers.IsRegistered(definition.Id)) continue;
                WatchCondition(definition);
                _session.Triggers.Touch(definition.Id);
            }
        }

        // 스냅샷으로 되감은 뒤: 그 기록 id 이후에 달성한 도전과제의 달성 표시와 보상을 다시 적용한다(달성은 되감기로 취소되지 않는다).
        // 보상은 달성할 때와 같은 순간으로 낸다(이벤트 값을 쓰는 결과·[BindTo](이벤트, …) 함수가 같은 값을 다시 준다).
        public void ReapplyUnlocksAfter(long journalId)
        {
            foreach (var (entryId, id, moment) in _unlocks.ToArray())
            {
                if (entryId <= journalId) continue;
                var definition = Find(id);
                if (definition == null || IsUnlocked(id)) continue;
                var entry = _session.Journal.Append(JournalKind.Achievement, "Achievements", $"달성 유지(되감기): {definition.DisplayName}", PlayerDataKeys.Achievement(id));
                using (_session.Journal.Cause(entry.Id))
                using (_session.Data.Permanent())
                {
                    _session.Data.Set(PlayerDataKeys.Achievement(id), true);
                    GrantRewards(definition, moment);
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
