using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GBible
{
    // 상태 효과(Status): 대상에게 걸리는 버프·디버프. 정의(StatusDefinition, DB › 상태 효과)와 걸린 것(ActiveStatus, 플레이어 데이터)으로 나뉜다.
    //   중첩: 최대 중첩 수, 다시 걸 때의 규칙(중첩 더하기 / 지속 갱신 / 둘 다 / 무시).
    //   지속: 영구 / 이벤트 N회(예: 턴 끝 3번) / 게임 시간 N초. 끝나면 '끝날 때 결과'(지연 효과 "3턴 뒤 폭발"도 이것으로).
    //   걸려 있는 동안: 능력치 Modifier(중첩당 값, 출처 = 걸린 것 하나의 id), 그리고 트리거(이벤트 받기·가로채기 …)로 발동하는 결과.
    //     가진 쪽 필드: 이벤트·의도의 그 필드가 가진 쪽 id일 때만 발동한다(예: DamageIntent.targetId → "내가 맞을 때만").
    //     트리거 안의 조건도 가진 쪽마다 따로 검사한다(조건 Block의 Context.User = 가진 쪽 → "가진 쪽 체력이 절반 이하일 때").
    //   결과의 컨텍스트: User = 가진 쪽 객체, Owner = 상태 효과 ID. 중첩 수는 Game.StatusStacks(c.User, c.Owner).
    // 같은 이벤트가 결과 발동과 횟수 줄이기를 둘 다 하면 결과가 먼저다("턴 끝마다 피해 1, 3턴" = 피해 3번 뒤 끝).
    // 그 이벤트를 받는 도중에 새로 걸린 상태 효과는 그 이벤트를 세지 않는다(턴 끝에 건 "2턴"은 다음 턴 끝부터 센다).
    // 주기(Pulse, 초): 걸려 있는 동안 N초(게임 시간)마다 결과를 낸다(실시간 지속 피해·재생). 이벤트 트리거와 같은 결과 칸을 쓴다.
    // 걸릴 때·끝날 때 이벤트 StatusApplied·StatusEnded를 쏜다(화면 아이콘·다른 규칙이 받는다). 시간으로 끝날 때는 보정을 떼고 → 끝날 때 결과 → StatusEnded 순.
    // 모두 플레이어 데이터라 Undo·저장·멀티 그대로다. 가진 쪽이 데이터에서 사라지면 그 상태 효과도 정리된다.
    public enum StatusReapply
    {
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("중첩 더하기")]
#endif
        AddStacks,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("지속 갱신")]
#endif
        Refresh,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("중첩 더하고 지속 갱신")]
#endif
        AddStacksAndRefresh,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("무시")]
#endif
        Ignore,
    }

    public enum StatusDuration
    {
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("영구(풀 때까지)")]
#endif
        Permanent,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("이벤트 횟수")]
#endif
        Events,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("게임 시간(초)")]
#endif
        Seconds,
    }

    [Serializable]
    public sealed class StatusModifier
    {
        [FieldLabel("능력치(숫자 필드 이름)")] public string attribute = "";
        [FieldLabel("칸")] public ModifierKind kind = ModifierKind.Flat;
        [FieldLabel("중첩당 값")] public double valuePerStack;
    }

    public sealed class StatusDefinition
    {
        public string Id;
        public string DisplayName;
        public int MaxStacks;                       // 0 = 제한 없음
        public StatusReapply Reapply = StatusReapply.AddStacks;
        public StatusDuration Duration = StatusDuration.Permanent;
        public double Length;                       // 이벤트 횟수 또는 초
        public string TickEvent;                    // 이벤트 지속: 줄이는 이벤트 형식
        public string TickHolderField;              // 그 이벤트의 이 필드가 가진 쪽 id일 때만 줄인다(비우면 늘)
        public readonly List<StatusModifier> Modifiers = new List<StatusModifier>();
        public Trigger When;                        // 걸려 있는 동안 발동(없으면 null)
        public string HolderField;                  // 이벤트·의도의 이 필드가 가진 쪽 id일 때만 발동(비우면 가진 쪽 모두)
        public readonly List<Outcome> Outcomes = new List<Outcome>();
        public readonly List<Outcome> OnApply = new List<Outcome>();
        public readonly List<Outcome> OnExpire = new List<Outcome>();
        public double Pulse;                        // 0보다 크면 걸려 있는 동안 이 초(게임 시간)마다 Outcomes를 낸다
        public bool Hidden;                         // 화면에 보이지 않는 상태 효과(쿨다운·표시용 아닌 규칙)
    }

    // 상태 효과가 걸렸을 때(처음·다시 걸 때 모두, stacks = 걸린 뒤의 중첩).
    [Serializable, GameEvent("상태 효과 걸림")]
    public sealed class StatusApplied
    {
        public string holder;
        public string status;
        public int stacks;
        public string source;
    }

    // 상태 효과가 끝났을 때(expired: 지속이 다 됨 / 거짓이면 풀림).
    [Serializable, GameEvent("상태 효과 끝남")]
    public sealed class StatusEnded
    {
        public string holder;
        public string status;
        public bool expired;
    }

    [Serializable]
    public sealed class ActiveStatus
    {
        public string id;
        public string status;     // 정의 ID
        public string target;     // 가진 쪽 id
        public int stacks;
        public double remaining;  // 이벤트 지속: 남은 횟수 / 시간 지속: 끝나는 게임 시각
        public string source;
        public double nextPulse;  // 주기가 있으면 다음 결과를 낼 게임 시각
    }

    [Serializable]
    public sealed class StatusList
    {
        public List<ActiveStatus> items = new List<ActiveStatus>();
    }

    public sealed class StatusSystem
    {
        public const string Key = "$statuses";

        readonly GameSession _session;
        readonly Dictionary<string, StatusDefinition> _definitions = new Dictionary<string, StatusDefinition>(StringComparer.Ordinal);
        // 이번 행동에서 새로 걸린 것 → 걸 때의 기록 원인(그 이벤트를 받는 도중에 걸렸는지 가린다).
        readonly Dictionary<string, long> _appliedUnder = new Dictionary<string, long>(StringComparer.Ordinal);

        internal StatusSystem(GameSession session)
        {
            _session = session;
            session.Events.SubscribeAll(OnEvent);
            session.Actions.TransactionStarted += _ => _appliedUnder.Clear();
        }

        public IEnumerable<StatusDefinition> Definitions => _definitions.Values;
        public StatusDefinition Find(string id) => id != null && _definitions.TryGetValue(id, out var d) ? d : null;

        public void Define(StatusDefinition definition)
        {
            if (definition == null || string.IsNullOrEmpty(definition.Id)) return;
            _definitions[definition.Id] = definition;
            var owner = "status:" + definition.Id;
            _session.Triggers.Unregister(owner);
            if (definition.When != null)
                _session.Triggers.Register(owner, definition.When, repeat: true, fire: moment => FireFor(definition, moment), holders: moment => HoldersFor(definition, moment));
        }

        // ── 걸기·풀기 ─────────────────────────────────────────

        // length: 이번에만 쓸 지속 길이(이벤트 횟수 또는 초). 비우면 정의의 길이.
        public ActiveStatus Apply(object target, string statusId, int stacks = 1, string source = null, double? length = null)
        {
            var definition = Find(statusId) ?? throw new InvalidOperationException($"[MomoiProject] 없는 상태 효과 '{statusId}'입니다.");
            var holder = Resolve(target, out var targetId);
            if (string.IsNullOrEmpty(targetId)) throw new InvalidOperationException("[MomoiProject] 상태 효과를 걸 대상의 id가 없습니다.");
            stacks = Math.Max(1, stacks);
            var list = Ensure();
            var existing = list.items.FirstOrDefault(s => s.target == targetId && s.status == statusId);
            if (existing != null)
            {
                if (definition.Reapply == StatusReapply.Ignore) return existing;
                _session.Data.Modify<StatusList>(Key, _ =>
                {
                    if (definition.Reapply != StatusReapply.Refresh) existing.stacks = Cap(definition, existing.stacks + stacks);
                    if (definition.Reapply != StatusReapply.AddStacks) existing.remaining = Initial(definition, length);
                });
                RefreshModifiers(definition, existing);
                _session.Events.Publish(new StatusApplied { holder = targetId, status = statusId, stacks = existing.stacks, source = source ?? "" });
                return existing;
            }
            var active = new ActiveStatus
            {
                id = _session.Game.NewId("$status"), status = statusId, target = targetId,
                stacks = Cap(definition, stacks), remaining = Initial(definition, length), source = source ?? "",
                nextPulse = definition.Pulse > 0 ? _session.Clock.Time + definition.Pulse : 0,
            };
            _session.Data.Modify<StatusList>(Key, l => l.items.Add(active));
            if (_session.Data.InTransaction) _appliedUnder[active.id] = _session.Journal.CurrentCause;
            RefreshModifiers(definition, active);
            Run(definition, active, definition.OnApply, holder, null, false, "Status");
            _session.Events.Publish(new StatusApplied { holder = targetId, status = statusId, stacks = active.stacks, source = source ?? "" });
            return active;
        }

        // 그 대상의 그 상태 효과를 푼다(중첩을 주면 그만큼 줄이고, 0이 되면 푼다). 끝날 때 결과는 돌지 않는다(만료로 끝날 때만 돈다).
        public bool Remove(object target, string statusId, int stacks = 0)
        {
            Resolve(target, out var targetId);
            var active = Current().FirstOrDefault(s => s.target == targetId && s.status == statusId);
            if (active == null) return false;
            if (stacks > 0 && active.stacks > stacks)
            {
                _session.Data.Modify<StatusList>(Key, _ => active.stacks -= stacks);
                if (Find(statusId) is StatusDefinition definition) RefreshModifiers(definition, active);
                return true;
            }
            Drop(active);
            _session.Events.Publish(new StatusEnded { holder = active.target, status = statusId, expired = false });
            return true;
        }

        // 남은 지속을 줄인다(쿨다운 −1초·남은 턴 −1). 이벤트 횟수면 그 수만큼, 초면 그 초만큼. 다 되면 지속이 끝난 것으로(끝날 때 결과) 끝낸다.
        // 영구 상태 효과나 걸려 있지 않으면 거짓.
        public bool Shorten(object target, string statusId, double amount)
        {
            Resolve(target, out var targetId);
            var active = Current().FirstOrDefault(s => s.target == targetId && s.status == statusId);
            var definition = Find(statusId);
            if (active == null || definition == null || definition.Duration == StatusDuration.Permanent || amount <= 0) return false;
            _session.Data.Modify<StatusList>(Key, _ => active.remaining -= definition.Duration == StatusDuration.Events ? Math.Round(amount) : amount);
            if (definition.Duration == StatusDuration.Events ? active.remaining <= 0 : active.remaining <= _session.Clock.Time) Expire(active);
            return true;
        }

        public int Stacks(object target, string statusId)
        {
            Resolve(target, out var targetId);
            return Current().FirstOrDefault(s => s.target == targetId && s.status == statusId)?.stacks ?? 0;
        }

        public IReadOnlyList<ActiveStatus> Of(object target)
        {
            Resolve(target, out var targetId);
            return Current().Where(s => s.target == targetId).ToList();
        }

        internal bool HasAny => _session.Data.IsDefined(Key) && (_session.Data.Get<StatusList>(Key)?.items.Count ?? 0) > 0;

        // 가진 쪽이 데이터에서 사라진 상태 효과를 지운다(끝날 때 결과 없이). 바깥 행동이 끝나기 직전에 부른다.
        internal void PruneMissing()
        {
            var list = _session.Data.IsDefined(Key) ? _session.Data.Get<StatusList>(Key)?.items : null;
            if (list == null || list.Count == 0) return;
            var gone = new HashSet<string>(list.Select(s => s.target).Distinct().Where(id => _session.Game.Find(id) == null), StringComparer.Ordinal);
            if (gone.Count == 0) return;
            foreach (var active in list.Where(s => gone.Contains(s.target)).ToList()) Drop(active);
        }

        // ── 지속 ───────────────────────────────────────────────

        void OnEvent(object evt)
        {
            if (_definitions.Count == 0 || !Exists()) return;
            var type = evt.GetType().FullName;
            var ticking = Current().Where(s => Find(s.status) is StatusDefinition d && d.Duration == StatusDuration.Events && d.TickEvent == type).ToList();
            if (ticking.Count == 0) return;
            // 이 이벤트를 받는 도중(그 원인 아래에서) 새로 걸린 것은 세지 않는다.
            var eventEntry = _session.Journal.CurrentCause;
            ticking.RemoveAll(s => _appliedUnder.TryGetValue(s.id, out var cause) && IsUnder(cause, eventEntry));
            if (ticking.Count == 0) return;
            var expired = new List<ActiveStatus>();
            _session.Data.Modify<StatusList>(Key, _ =>
            {
                foreach (var active in ticking)
                {
                    var definition = Find(active.status);
                    if (!string.IsNullOrEmpty(definition.TickHolderField) && FieldText(evt, definition.TickHolderField) != active.target) continue;
                    active.remaining -= 1;
                    if (active.remaining <= 0) expired.Add(active);
                }
            });
            foreach (var active in expired) Expire(active);
        }

        bool IsUnder(long cause, long eventEntry)
        {
            if (eventEntry == 0 || cause == 0) return false;
            if (cause == eventEntry) return true;
            return _session.Journal.CauseChain(cause).Any(e => e.Id == eventEntry);
        }

        // 프레임 끝(행동 밖): 주기가 된 상태 효과의 결과를 내고, 시간이 다 된 것을 끝낸다(각각 행동 하나로).
        internal void Tick()
        {
            if (_session.Data.InTransaction || !Exists()) return;
            var now = _session.Clock.Time;
            var pulsing = Current().Where(s => s.nextPulse > 0 && s.nextPulse <= now && (Find(s.status)?.Pulse ?? 0) > 0).Select(s => s.id).ToList();
            if (pulsing.Count > 0) _session.Do(new StatusPulseRun { statuses = pulsing });
            var due = Current().Where(s => Find(s.status)?.Duration == StatusDuration.Seconds && s.remaining <= now).Select(s => s.id).ToList();
            if (due.Count > 0) _session.Do(new StatusExpireRun { statuses = due });
        }

        // 주기 결과: 밀린 만큼(최대 8번) 낸다. 다음 시각은 주기만큼 뒤로.
        internal void PulseById(string id)
        {
            var active = Current().FirstOrDefault(s => s.id == id);
            var definition = active == null ? null : Find(active.status);
            if (definition == null || definition.Pulse <= 0) return;
            var holder = _session.Game.Find(active.target);
            var now = _session.Clock.Time;
            // 시간 지속이면 끝나는 시각 뒤의 주기는 내지 않는다(같은 프레임에 끝남과 겹칠 때).
            var until = definition.Duration == StatusDuration.Seconds ? Math.Min(now, active.remaining) : now;
            for (var i = 0; i < 8 && active.nextPulse > 0 && active.nextPulse <= until; i++)
            {
                _session.Data.Modify<StatusList>(Key, _ => active.nextPulse += definition.Pulse);
                if (holder != null) Run(definition, active, definition.Outcomes, holder, null, false, "Status");
                if (!Current().Contains(active)) return;   // 결과가 그 상태 효과를 풀었다
            }
            if (active.nextPulse <= now) _session.Data.Modify<StatusList>(Key, _ => active.nextPulse = now + definition.Pulse);
        }

        internal void ExpireById(string id)
        {
            var active = Current().FirstOrDefault(s => s.id == id);
            if (active != null) Expire(active);
        }

        void Expire(ActiveStatus active)
        {
            var definition = Find(active.status);
            var holder = _session.Game.Find(active.target);
            Drop(active);
            if (definition != null && holder != null) Run(definition, active, definition.OnExpire, holder, null, false, "Status");
            _session.Events.Publish(new StatusEnded { holder = active.target, status = active.status, expired = true });
        }

        void Drop(ActiveStatus active)
        {
            _session.Data.Modify<StatusList>(Key, l => l.items.RemoveAll(s => s.id == active.id));
            _session.Attributes.RemoveBySource(active.id);
        }

        // ── 걸려 있는 동안의 발동 ───────────────────────────────

        // 트리거를 가진 쪽마다 검사한다(TriggerSystem이 User = 가진 쪽으로 트리거 전체를 검사): 걸려 있고, 가진 쪽 필드가 맞는 대상만.
        IEnumerable<object> HoldersFor(StatusDefinition definition, TriggerContext moment)
        {
            foreach (var active in Current().Where(s => s.status == definition.Id).ToList())
            {
                if (!string.IsNullOrEmpty(definition.HolderField) && FieldText(moment.Event, definition.HolderField) != active.target) continue;
                var holder = _session.Game.Find(active.target);
                if (holder != null) yield return holder;
            }
        }

        void FireFor(StatusDefinition definition, TriggerContext moment)
        {
            var holderId = moment.User == null ? null : GameApi.IdOf(moment.User);
            var active = Current().FirstOrDefault(s => s.status == definition.Id && s.target == holderId);
            if (active != null) Run(definition, active, definition.Outcomes, moment.User, moment.Event, moment.IsIntercept, "Status");
        }

        void Run(StatusDefinition definition, ActiveStatus active, List<Outcome> outcomes, object holder, object evt, bool intercept, string source)
        {
            if (outcomes == null || outcomes.Count == 0) return;
            var context = new TriggerContext
            {
                Session = _session, Owner = definition.Id, Event = evt, IsIntercept = intercept,
                User = holder, Picked = holder == null ? Array.Empty<object>() : new[] { holder },
            };
            _session.Triggers.RunOutcomes(outcomes, context, source);
        }

        void RefreshModifiers(StatusDefinition definition, ActiveStatus active)
        {
            if (definition.Modifiers.Count == 0) return;
            _session.Attributes.RemoveBySource(active.id);
            foreach (var modifier in definition.Modifiers.Where(m => m != null && !string.IsNullOrEmpty(m.attribute)))
                _session.Attributes.Add(active.target, modifier.attribute, modifier.kind, modifier.valuePerStack * active.stacks, active.id);
        }

        // ── 도우미 ─────────────────────────────────────────────

        double Initial(StatusDefinition definition, double? length = null)
        {
            var span = length ?? definition.Length;
            switch (definition.Duration)
            {
                case StatusDuration.Events: return Math.Max(1, Math.Round(span));
                case StatusDuration.Seconds: return _session.Clock.Time + Math.Max(0, span);
                default: return 0;
            }
        }

        static int Cap(StatusDefinition definition, int stacks) => definition.MaxStacks > 0 ? Math.Min(definition.MaxStacks, stacks) : stacks;

        StatusList Ensure()
        {
            if (!_session.Data.IsDefined(Key)) _session.Data.Define(Key, DataZone.Session, new StatusList(), "상태 효과");
            return _session.Data.Get<StatusList>(Key);
        }

        // 등록돼 있거나, 불러온 저장의 값이 기다리고 있으면(지금 등록한다) 참.
        bool Exists()
        {
            if (_session.Data.IsDefined(Key)) return true;
            if (!_session.Data.IsPending(Key)) return false;
            Ensure();
            return true;
        }

        IEnumerable<ActiveStatus> Current() =>
            Exists() ? _session.Data.Get<StatusList>(Key)?.items ?? new List<ActiveStatus>() : Enumerable.Empty<ActiveStatus>();

        object Resolve(object target, out string id)
        {
            if (target is string text)
            {
                id = text;
                return _session.Game.Find(text);
            }
            id = target == null ? null : GameApi.IdOf(target);
            return target;
        }

        static string FieldText(object evt, string field)
        {
            if (evt == null || string.IsNullOrEmpty(field)) return null;
            var value = FieldAccess.Read(evt, field);
            return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }

    // 주기가 된 상태 효과의 결과를 내는 행동.
    [Serializable]
    public sealed class StatusPulseRun : GameAction
    {
        public List<string> statuses = new List<string>();

        public override ActionVisibility Visibility => ActionVisibility.Developer;
        public override string Describe() => "상태 효과 주기";

        protected override void Run()
        {
            foreach (var id in statuses) Game.Session.Statuses.PulseById(id);
        }
    }

    // 시간이 다 된 상태 효과를 끝내는 행동(끝날 때 결과까지 한 묶음).
    [Serializable]
    public sealed class StatusExpireRun : GameAction
    {
        public List<string> statuses = new List<string>();

        public override ActionVisibility Visibility => ActionVisibility.Developer;
        public override string Describe() => "상태 효과 만료";

        protected override void Run()
        {
            foreach (var id in statuses) Game.Session.Statuses.ExpireById(id);
        }
    }
}
