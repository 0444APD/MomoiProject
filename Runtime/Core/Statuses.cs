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
    //   결과의 컨텍스트: User = 가진 쪽 객체, Owner = 상태 효과 ID. 중첩 수는 Game.StatusStacks(c.User, c.Owner).
    // 같은 이벤트가 결과 발동과 횟수 줄이기를 둘 다 하면 결과가 먼저다("턴 끝마다 피해 1, 3턴" = 피해 3번 뒤 끝).
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

        internal StatusSystem(GameSession session)
        {
            _session = session;
            session.Events.SubscribeAll(OnEvent);
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
                _session.Triggers.Register(owner, definition.When, repeat: true, fire: moment => FireFor(definition, moment));
        }

        // ── 걸기·풀기 ─────────────────────────────────────────

        public ActiveStatus Apply(object target, string statusId, int stacks = 1, string source = null)
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
                    if (definition.Reapply != StatusReapply.AddStacks) existing.remaining = Initial(definition);
                });
                RefreshModifiers(definition, existing);
                return existing;
            }
            var active = new ActiveStatus
            {
                id = _session.Game.NewId("$status"), status = statusId, target = targetId,
                stacks = Cap(definition, stacks), remaining = Initial(definition), source = source ?? "",
            };
            _session.Data.Modify<StatusList>(Key, l => l.items.Add(active));
            RefreshModifiers(definition, active);
            Run(definition, active, definition.OnApply, holder, null, false, "Status");
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

        // 프레임 끝(행동 밖): 시간이 다 된 상태 효과를 끝낸다(행동 하나로).
        internal void Tick()
        {
            if (_session.Data.InTransaction || !Exists()) return;
            var now = _session.Clock.Time;
            var due = Current().Where(s => Find(s.status)?.Duration == StatusDuration.Seconds && s.remaining <= now).Select(s => s.id).ToList();
            if (due.Count > 0) _session.Do(new StatusExpireRun { statuses = due });
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
        }

        void Drop(ActiveStatus active)
        {
            _session.Data.Modify<StatusList>(Key, l => l.items.RemoveAll(s => s.id == active.id));
            _session.Attributes.RemoveBySource(active.id);
        }

        // ── 걸려 있는 동안의 발동 ───────────────────────────────

        void FireFor(StatusDefinition definition, TriggerContext moment)
        {
            foreach (var active in Current().Where(s => s.status == definition.Id).ToList())
            {
                var holder = _session.Game.Find(active.target);
                if (holder == null)
                {
                    Drop(active);
                    continue;
                }
                if (!string.IsNullOrEmpty(definition.HolderField) && FieldText(moment.Event, definition.HolderField) != active.target) continue;
                Run(definition, active, definition.Outcomes, holder, moment.Event, moment.IsIntercept, "Status");
                if (moment.IsIntercept && Intents.IsCancelled(moment.Event)) break;
            }
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

        double Initial(StatusDefinition definition)
        {
            switch (definition.Duration)
            {
                case StatusDuration.Events: return Math.Max(1, Math.Round(definition.Length));
                case StatusDuration.Seconds: return _session.Clock.Time + Math.Max(0, definition.Length);
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
