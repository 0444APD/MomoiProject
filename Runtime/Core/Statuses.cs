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
    // 걸기 직전에 의도 '상태 효과 걸기'(StatusIntent)를 거친다: 가로채기가 상태 효과를 바꾸고(화상 → 푸른 화상)·중첩·길이를 고치고(독 ×2·디버프 지속 +)·막는다(면역).
    // 종류(버프·디버프): 디버프만 풀기·디버프 개수·디버프 지속 늘리기처럼 묶어서 다룰 때.
    // 모두 플레이어 데이터라 Undo·저장·멀티 그대로다. 가진 쪽이 데이터에서 사라지면 그 상태 효과도 정리된다.
    // 시간 규칙(주기·시간 만료)의 결과가 실패하면(비용이 모자람 등) 그 결과가 바꾼 것만 되돌리고 기록에 경고를 남긴다: 주기는 그 회차를 건너뛰고
    // 다음 시각으로 넘어가며(밀린 회차도 버린다), 만료는 끝날 때 결과 없이 그대로 끝난다. 그래서 같은 실패가 매 프레임 되풀이되지 않는다.
    // (실패가 없으면 같은 프레임의 주기·만료는 행동 하나에 묶인다. 실패한 프레임에만 상태 효과마다 저장점으로 감싸 다시 낸다.)
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

    public enum StatusKind
    {
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("없음")]
#endif
        None,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("버프")]
#endif
        Buff,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("디버프")]
#endif
        Debuff,
    }

    // 기본 제공 의도: 상태 효과 걸기(Game.ApplyStatus·효과 '상태 효과 걸기'가 걸기 직전에 거친다).
    //   화상 → 푸른 화상: statusId 값으로 바꾸기 blueBurn · 독 중첩 2배: stacks 곱하기 2 · 디버프 지속 +20%: length 곱하기 1.2 · 면역: 막기.
    [Serializable, Intent("상태 효과 걸기"), Element(Elements.Status)]
    public sealed class StatusIntent
    {
        public string targetId;    // 걸릴 대상 id
        public string statusId;    // 상태 효과 ID(바꾸면 그 상태 효과가 걸린다)
        public int stacks;         // 걸 중첩
        public double length;      // 지속 길이(이벤트 횟수·초, 처음에는 그 상태 효과의 길이)
        public string sourceId;    // 건 항목(능력·패시브·상태 효과 ID)
        public string byId;        // 건 개체 id(사용자 — 처치 기여: "마지막에 건 쪽")
        public StatusKind kind;    // 그 상태 효과의 종류(버프·디버프)
        public int maxStacks;      // 최대 중첩(처음 = 정의의 최대 중첩, 0 = 제한 없음) — "화상 중첩 +1"
        public double pulse;       // 주기(초, 처음 = 정의의 주기) — "화상 틱 속도 +50%"는 곱하기 1/1.5
        public bool cancelled;     // 참이면 걸지 않는다
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
        public StatusKind Kind;                     // 버프·디버프(묶어서 풀기·세기·가로채기 조건)
        public bool Silences;                       // 걸린 쪽의 패시브를 끈다(발동·보정 없음 — Ink·디버프된 조커)
    }

    // 상태 효과가 걸렸을 때(처음·다시 걸 때 모두, stacks = 걸린 뒤의 중첩).
    [Serializable, GameEvent("상태 효과 걸림"), Element(Elements.Status)]
    public sealed class StatusApplied
    {
        public string holder;
        public string status;
        public int stacks;
        public string source;
        public string by;      // 건 개체 id(사용자)
    }

    // 상태 효과가 끝났을 때(expired: 지속이 다 됨 / 거짓이면 풀림).
    [Serializable, GameEvent("상태 효과 끝남"), Element(Elements.Status)]
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
        public double pulse;      // 이 걸린 것의 주기(초, 0이면 정의의 주기)
        public int maxStacks;     // 마지막에 건 의도의 최대 중첩(0 = 제한 없음, 보기용 — 상한은 걸 때마다 그때 의도의 값으로 자른다)
        public string by = "";   // 마지막에 건 개체 id(다시 걸면 바뀐다)
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
        bool _anySilences;   // 패시브를 끄는 상태 효과가 하나라도 정의돼 있나(없으면 침묵 검사를 건너뛴다)
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
            _anySilences = _definitions.Values.Any(d => d.Silences);
            var owner = "status:" + definition.Id;
            _session.Triggers.Unregister(owner);
            if (definition.When != null)
                _session.Triggers.Register(owner, definition.When, repeat: true, fire: moment => FireFor(definition, moment), holders: moment => HoldersFor(definition, moment), item: definition.Id);
        }

        // ── 걸기·풀기 ─────────────────────────────────────────

        // length: 이번에만 쓸 지속 길이(이벤트 횟수 또는 초). 비우면 정의의 길이.
        // by: 건 개체(사용자) — 처치 기여·"마지막에 건 쪽". 효과 '상태 효과 걸기'는 사용자를 넣는다.
        public ActiveStatus Apply(object target, string statusId, int stacks = 1, string source = null, double? length = null, object by = null)
        {
            var definition = Find(statusId) ?? throw new InvalidOperationException($"[MomoiProject] 없는 상태 효과 '{statusId}'입니다.");
            var holder = Resolve(target, out var targetId);
            if (string.IsNullOrEmpty(targetId)) throw new InvalidOperationException("[MomoiProject] 상태 효과를 걸 대상의 id가 없습니다.");
            // 의도 '상태 효과 걸기': 가로채기가 바꾸거나 막는다. 막히면 null.
            var intent = _session.Game.Intercept(new StatusIntent
            {
                targetId = targetId, statusId = statusId, stacks = Math.Max(1, stacks), length = length ?? definition.Length,
                sourceId = source ?? "", byId = ByIdOf(by), kind = definition.Kind, maxStacks = definition.MaxStacks, pulse = definition.Pulse,
            });
            if (intent.cancelled || intent.stacks <= 0) return null;
            if (intent.statusId != statusId)
            {
                var changed = Find(intent.statusId) ?? throw new InvalidOperationException($"[MomoiProject] 행동 직전 이벤트가 바꾼 상태 효과 '{intent.statusId}'가 없습니다.");
                // 길이를 따로 정하지 않았으면 바뀐 상태 효과의 길이로.
                if (!length.HasValue && intent.length == definition.Length) intent.length = changed.Length;
                if (intent.maxStacks == definition.MaxStacks) intent.maxStacks = changed.MaxStacks;
                if (intent.pulse == definition.Pulse) intent.pulse = changed.Pulse;
                definition = changed;
                statusId = changed.Id;
            }
            if (intent.targetId != targetId)
            {
                holder = _session.Game.Find(intent.targetId);
                if (holder == null)
                {
                    _session.Journal.Append(JournalKind.Warning, "Status", $"상태 효과 걸기: 행동 직전 이벤트가 바꾼 대상 '{intent.targetId}'를 찾지 못해 걸지 않았습니다.");
                    return null;
                }
                targetId = intent.targetId;
            }
            stacks = intent.stacks;
            length = intent.length;
            source = intent.sourceId;
            stacks = Math.Max(1, stacks);
            var cap = Math.Max(0, intent.maxStacks);
            var pulse = Math.Max(0, intent.pulse);
            var byId = intent.byId ?? "";
            var list = Ensure();
            var existing = list.items.FirstOrDefault(s => s.target == targetId && s.status == statusId);
            if (existing != null)
            {
                if (definition.Reapply == StatusReapply.Ignore) return existing;
                _session.Data.Modify<StatusList>(Key, _ =>
                {
                    existing.maxStacks = cap;
                    if (definition.Reapply != StatusReapply.Refresh) existing.stacks = Cap(cap, existing.stacks + stacks);
                    if (definition.Reapply != StatusReapply.AddStacks) existing.remaining = Initial(definition, length);
                    if (!string.IsNullOrEmpty(byId)) existing.by = byId;
                    // 주기는 이번에 건 의도의 값으로(가로채기가 빠지면 정의의 주기로 돌아온다). 주기가 없다가 생기면 지금부터 세고, 0이 되면 멈춘다(처음 걸 때와 같다).
                    existing.pulse = pulse != definition.Pulse ? pulse : 0;
                    if (pulse <= 0) existing.nextPulse = 0;
                    else if (existing.nextPulse <= 0) existing.nextPulse = _session.Clock.Time + pulse;
                });
                RefreshModifiers(definition, existing);
                _session.Events.Publish(new StatusApplied { holder = targetId, status = statusId, stacks = existing.stacks, source = source ?? "", by = byId });
                return existing;
            }
            var active = new ActiveStatus
            {
                id = _session.Game.NewId("$status"), status = statusId, target = targetId,
                stacks = Cap(cap, stacks), remaining = Initial(definition, length), source = source ?? "", by = byId, maxStacks = cap,
                pulse = pulse != definition.Pulse ? pulse : 0,
                nextPulse = pulse > 0 ? _session.Clock.Time + pulse : 0,
            };
            _session.Data.Modify<StatusList>(Key, l => l.items.Add(active));
            if (_session.Data.InTransaction) _appliedUnder[active.id] = _session.Journal.CurrentCause;
            RefreshModifiers(definition, active);
            Run(definition, active, definition.OnApply, holder, null, false, "Status");
            _session.Events.Publish(new StatusApplied { holder = targetId, status = statusId, stacks = active.stacks, source = source ?? "", by = byId });
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
            _session.Data.Modify<StatusList>(Key, _ => active.remaining -= definition.Duration == StatusDuration.Events ? GameNumbers.Round(amount) : amount);
            if (definition.Duration == StatusDuration.Events ? active.remaining <= 0 : active.remaining <= _session.Clock.Time) Expire(active);
            return true;
        }

        public int Stacks(object target, string statusId)
        {
            Resolve(target, out var targetId);
            return Current().FirstOrDefault(s => s.target == targetId && s.status == statusId)?.stacks ?? 0;
        }

        // 대상에 걸린 그 종류(버프·디버프)의 상태 효과 수(중첩이 아니라 가짓수).
        public int CountOfKind(object target, StatusKind kind)
        {
            Resolve(target, out var targetId);
            return Current().Count(s => s.target == targetId && Find(s.status)?.Kind == kind);
        }

        // 대상에 걸린 그 종류의 상태 효과를 모두 푼다(정화). 푼 가짓수.
        public int RemoveKind(object target, StatusKind kind)
        {
            Resolve(target, out var targetId);
            var removed = 0;
            foreach (var active in Current().Where(s => s.target == targetId && Find(s.status)?.Kind == kind).ToList())
                if (Remove(target, active.status)) removed++;
            return removed;
        }

        public IReadOnlyList<ActiveStatus> Of(object target)
        {
            Resolve(target, out var targetId);
            return Current().Where(s => s.target == targetId).ToList();
        }

        internal bool HasAny => _session.Data.IsDefined(Key) && (_session.Data.Get<StatusList>(Key)?.items.Count ?? 0) > 0;

        // 가진 쪽이 데이터에서 사라진 상태 효과를 지운다(끝날 때 결과 없이). 바깥 행동이 끝나기 직전에 부른다(모든 바깥 행동·미리보기·시간 규칙).
        // 가진 쪽은 데이터를 한 번 훑어 한꺼번에 찾는다(FindAll) — 가진 쪽마다 찾으면 데이터 전체를 가진 쪽 수만큼 훑는다.
        internal void PruneMissing()
        {
            var list = _session.Data.IsDefined(Key) ? _session.Data.Get<StatusList>(Key)?.items : null;
            if (list == null || list.Count == 0) return;
            var targets = new HashSet<string>(list.Select(s => s.target).Where(id => !string.IsNullOrEmpty(id)), StringComparer.Ordinal);
            var alive = _session.Game.FindAll(targets);
            var gone = list.Where(s => string.IsNullOrEmpty(s.target) || !alive.ContainsKey(s.target)).ToList();
            foreach (var active in gone) Drop(active);
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
            var pulsing = Current().Where(s => s.nextPulse > 0 && s.nextPulse <= now && Find(s.status) is StatusDefinition d && PulseOf(d, s) > 0).Select(s => s.id).ToList();
            if (pulsing.Count > 0) RunTimeRule(new StatusPulseRun { statuses = pulsing }, new StatusPulseRun { statuses = pulsing, isolate = true });
            var due = Current().Where(s => Find(s.status)?.Duration == StatusDuration.Seconds && s.remaining <= now).Select(s => s.id).ToList();
            if (due.Count > 0) RunTimeRule(new StatusExpireRun { statuses = due }, new StatusExpireRun { statuses = due, isolate = true });
        }

        // 시간 규칙 행동을 낸다. 실패하면(행동은 이미 취소됐고 기록에 "행동 실패"가 남는다) 상태 효과마다 결과를 저장점으로 감싼 같은 행동을
        // 한 번 더 낸다: 실패한 결과만 되돌리고 나머지는 그대로 돌며, 실패한 주기·만료도 시각이 넘어가 다음 프레임에 다시 실패하지 않는다.
        void RunTimeRule(GameAction action, GameAction isolated)
        {
            try
            {
                _session.Do(action);
                return;
            }
            catch (Exception)
            {
            }
            try { _session.Do(isolated); }
            catch (Exception e)
            {
                _session.Journal.Append(JournalKind.Error, "Status", $"{isolated.Describe()}: 결과를 하나씩 감싸 다시 냈는데도 실패했습니다 — {e.Message}");
            }
        }

        // 주기 결과: 밀린 만큼(최대 8번) 낸다. 다음 시각은 주기만큼 뒤로.
        // isolate: 회차마다 결과를 저장점으로 감싼다 — 실패한 회차는 그 결과만 되돌리고 남은 밀린 회차도 버린다(다음 시각은 지금 + 주기).
        internal void PulseById(string id, bool isolate = false)
        {
            var active = Current().FirstOrDefault(s => s.id == id);
            var definition = active == null ? null : Find(active.status);
            var interval = definition == null ? 0 : PulseOf(definition, active);
            if (interval <= 0) return;
            var holder = _session.Game.Find(active.target);
            var now = _session.Clock.Time;
            // 시간 지속이면 끝나는 시각 뒤의 주기는 내지 않는다(같은 프레임에 끝남과 겹칠 때).
            var until = definition.Duration == StatusDuration.Seconds ? Math.Min(now, active.remaining) : now;
            for (var i = 0; i < 8 && active.nextPulse > 0 && active.nextPulse <= until; i++)
            {
                _session.Data.Modify<StatusList>(Key, _ => active.nextPulse += interval);
                // 앞 회차의 결과가 가진 쪽을 데이터에서 뺐으면(쓰러져 빠짐) 남은 회차는 내지 않는다(그 상태 효과는 행동 끝에 정리된다).
                if (i > 0 && holder != null) holder = _session.Game.Find(active.target);
                if (holder != null)
                {
                    if (!isolate) Run(definition, active, definition.Outcomes, holder, null, false, "Status");
                    else if (!Guarded(active, "주기 결과", () => Run(definition, active, definition.Outcomes, holder, null, false, "Status"))) break;
                }
                if (!Current().Contains(active)) return;   // 결과가 그 상태 효과를 풀었다
            }
            if (active.nextPulse <= now) _session.Data.Modify<StatusList>(Key, _ => active.nextPulse = now + interval);
        }

        internal void ExpireById(string id, bool isolate = false)
        {
            var active = Current().FirstOrDefault(s => s.id == id);
            if (active != null) Expire(active, isolate);
        }

        // isolate: 끝날 때 결과와 StatusEnded를 받은 쪽의 반응을 저장점으로 감싼다 — 실패하면 그것만 되돌리고 상태 효과는 그대로 끝난다.
        void Expire(ActiveStatus active, bool isolate = false)
        {
            // 같은 순간에 끝나는 다른 상태 효과의 끝날 때 결과가 이미 풀었으면(풀린 것은 끝날 때 결과 없이 끝난다) 다시 끝내지 않는다.
            if (!Current().Any(s => s.id == active.id)) return;
            var definition = Find(active.status);
            // 다시 걸어 지속을 갱신했으면(앞의 끝날 때 결과가 다시 걸었다) 아직 끝나지 않았다.
            if (definition != null && !IsDue(definition, active)) return;
            var holder = _session.Game.Find(active.target);
            Drop(active);
            if (definition != null && holder != null)
            {
                if (!isolate) Run(definition, active, definition.OnExpire, holder, null, false, "Status");
                else Guarded(active, "끝날 때 결과", () => Run(definition, active, definition.OnExpire, holder, null, false, "Status"));
            }
            var ended = new StatusEnded { holder = active.target, status = active.status, expired = true };
            if (!isolate) _session.Events.Publish(ended);
            else Guarded(active, "끝남(StatusEnded)을 받은 반응", () => _session.Events.Publish(ended));
        }

        // 저장점 안에서 돌린다. 실패하면 그것이 바꾼 것과 쏜 이벤트(연출·전송)만 되돌리고 경고를 남긴다(실패한 이벤트 예약과 같은 방식). 돌았으면 참.
        bool Guarded(ActiveStatus active, string what, Action run)
        {
            var point = _session.Data.Mark();
            var events = _session.Events.MarkEvents();
            try
            {
                run();
                return true;
            }
            catch (Exception e)
            {
                _session.Data.RollbackTo(point);
                _session.Events.RollbackEvents(events);
                _session.Journal.Append(JournalKind.Warning, "Status",
                    $"상태 효과 '{Find(active.status)?.DisplayName ?? active.status}'({active.target})의 {KoreanParticles.Attach(what, "가")} 실패해 그것이 바꾼 것만 되돌렸습니다 — {e.Message}");
                return false;
            }
        }

        void Drop(ActiveStatus active)
        {
            _session.Data.Modify<StatusList>(Key, l => l.items.RemoveAll(s => s.id == active.id));
            _session.Attributes.RemoveBySource(active.id);
        }

        // ── 걸려 있는 동안의 발동 ───────────────────────────────

        // 트리거를 가진 쪽마다 검사한다(TriggerSystem이 User = 가진 쪽으로 트리거 전체를 검사): 걸려 있고, 가진 쪽 필드가 맞고, 데이터에 있는 대상만.
        // 가진 쪽이 여럿이면 데이터를 한 번 훑어 한꺼번에 찾는다(FindAll — 가진 쪽마다 Find하면 데이터 전체를 가진 쪽 수만큼 훑는다).
        IEnumerable<object> HoldersFor(StatusDefinition definition, TriggerContext moment)
        {
            var targets = new List<string>();
            foreach (var active in Current())
            {
                if (active.status != definition.Id) continue;
                if (!string.IsNullOrEmpty(definition.HolderField) && FieldText(moment.Event, definition.HolderField) != active.target) continue;
                targets.Add(active.target);
            }
            if (targets.Count == 0) return Array.Empty<object>();
            if (targets.Count == 1) return _session.Game.Find(targets[0]) is { } single ? new[] { single } : Array.Empty<object>();
            var found = _session.Game.FindAll(targets);
            return targets.Select(id => id != null && found.TryGetValue(id, out var holder) ? holder : null).Where(h => h != null).ToList();
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
                case StatusDuration.Events: return Math.Max(1, GameNumbers.Round(span));
                case StatusDuration.Seconds: return _session.Clock.Time + Math.Max(0, span);
                default: return 0;
            }
        }

        static int Cap(int max, int stacks) => max > 0 ? Math.Min(max, stacks) : stacks;

        // 지속이 다 됐나(이벤트 횟수: 남은 횟수 0 이하, 게임 시간: 끝나는 시각이 지남). 영구는 끝내는 쪽이 정한다.
        bool IsDue(StatusDefinition definition, ActiveStatus active) =>
            definition.Duration == StatusDuration.Events ? active.remaining <= 0 :
            definition.Duration != StatusDuration.Seconds || active.remaining <= _session.Clock.Time;

        static string ByIdOf(object by) => by == null ? "" : by as string ?? (GameApi.HasId(by.GetType()) ? GameApi.IdOf(by) ?? "" : "");

        // 걸린 것의 주기(초): 걸 때 의도가 바꾼 값, 없으면 정의의 주기.
        static double PulseOf(StatusDefinition definition, ActiveStatus active) => active.pulse > 0 ? active.pulse : definition.Pulse;

        // 그 가진 쪽에 '걸린 쪽의 패시브를 끔' 상태 효과가 걸려 있나(Ink·디버프).
        public bool Silences(object target)
        {
            if (!_anySilences || !Exists()) return false;
            Resolve(target, out var targetId);
            if (string.IsNullOrEmpty(targetId)) return false;
            return Current().Any(s => s.target == targetId && s.stacks > 0 && (Find(s.status)?.Silences ?? false));
        }

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
            // id가 없는 형식(가진 쪽 run 같은 것)에는 걸린 것이 없다 — 읽기는 0·없음, 걸기는 "대상의 id가 없습니다".
            id = target == null || !GameApi.HasId(target.GetType()) ? null : GameApi.IdOf(target);
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
    [Serializable, SystemAction]
    public sealed class StatusPulseRun : GameAction, ITimeRuleAction
    {
        public List<string> statuses = new List<string>();
        // 결과를 회차마다 저장점으로 감싼다(묶음이 한 번 실패한 뒤 다시 낼 때 — 실패한 결과만 되돌리고 그 회차를 건너뛴다).
        public bool isolate;

        public override ActionVisibility Visibility => ActionVisibility.Developer;
        public override string Describe() => isolate ? "상태 효과 주기(실패 감싸기)" : "상태 효과 주기";

        protected override void Run()
        {
            foreach (var id in statuses) Game.Session.Statuses.PulseById(id, isolate);
        }
    }

    // 시간이 다 된 상태 효과를 끝내는 행동(끝날 때 결과까지 한 묶음).
    [Serializable, SystemAction]
    public sealed class StatusExpireRun : GameAction, ITimeRuleAction
    {
        public List<string> statuses = new List<string>();
        // 끝날 때 결과를 저장점으로 감싼다(묶음이 한 번 실패한 뒤 다시 낼 때 — 실패한 결과만 되돌리고 상태 효과는 그대로 끝낸다).
        public bool isolate;

        public override ActionVisibility Visibility => ActionVisibility.Developer;
        public override string Describe() => isolate ? "상태 효과 만료(실패 감싸기)" : "상태 효과 만료";

        protected override void Run()
        {
            foreach (var id in statuses) Game.Session.Statuses.ExpireById(id, isolate);
        }
    }
}
