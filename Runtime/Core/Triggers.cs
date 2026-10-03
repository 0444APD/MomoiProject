using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GBible
{
    // 트리거·결과: 로그·도전과제·퀘스트·능력·사용 아이템이 모두 같은 두 칸으로 이루어진다.
    //   트리거(언제) — 이벤트 받기 · 조건 충족 · 사용, 그리고 AND·OR·NOT으로 중첩.
    //   결과(무엇을) — 로그에 기록 · 플레이어 데이터에 주기 · 코딩한 결과 Block · 대상에게 효과.
    // 두 칸의 화면과 동작은 모든 기능이 같이 쓴다. 기능마다 다른 것은 칸에서 고를 수 있는 범위(잠금)뿐이다:
    //   [TriggerSlot(TriggerKinds.Event)] Trigger when;   [OutcomeSlot(OutcomeKinds.Record, Fixed = true)] List<Outcome> then;
    // 새 기능(예: 퀘스트)도 필드 두 개에 이 표시만 붙이면 같은 편집 화면·검증이 붙는다.

    [Flags]
    public enum TriggerKinds
    {
        None = 0,
        // 이벤트를 받은 순간(이벤트의 값으로 거를 수 있다).
        Event = 1,
        // 플레이어 데이터 조건이 참인 동안(참이 되는 순간 발동).
        Condition = 2,
        // 아이템·능력을 쓰는 순간(Game.Use).
        Use = 4,
        All = Event | Condition | Use,
    }

    [Flags]
    public enum OutcomeKinds
    {
        None = 0,
        // 로그에 기록(이벤트의 값 × 배수).
        Record = 1,
        // 플레이어 데이터 필드에 주기(숫자 증감·값·인스턴스·요소 넣기).
        PlayerData = 2,
        // 프로그래머가 코딩한 결과 Block(Reward를 상속).
        Coded = 4,
        // 대상 Block이 고른 객체마다 효과 Block.
        Targeted = 8,
        // 이벤트 쏘기(PublishEventOutcome).
        Event = 16,
        All = Record | PlayerData | Coded | Targeted | Event,
    }

    // 트리거 칸의 잠금: 고를 수 있는 트리거 종류.
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class TriggerSlotAttribute : Attribute
    {
        public TriggerKinds Allowed { get; }

        public TriggerSlotAttribute(TriggerKinds allowed)
        {
            Allowed = allowed;
        }
    }

    // 결과 칸의 잠금: 고를 수 있는 결과 종류. Fixed면 그 종류 하나로 고정되고 세부 칸만 고친다(예: 로그 = 로그에 기록).
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class OutcomeSlotAttribute : Attribute
    {
        public OutcomeKinds Allowed { get; }
        public bool Fixed { get; set; }

        public OutcomeSlotAttribute(OutcomeKinds allowed)
        {
            Allowed = allowed;
        }
    }

    // 트리거를 검사하는 한 순간과, 발동했을 때 결과에 넘기는 컨텍스트.
    public sealed class TriggerContext
    {
        public GameSession Session;
        // 이 트리거를 가진 항목의 ID(로그·도전과제·능력 …).
        public string Owner;
        // 이벤트를 받은 순간이면 그 이벤트.
        public object Event;
        // 사용하는 순간이면 true, 사용자와 플레이어가 고른 대상.
        public bool IsUse;
        public object User;
        public IReadOnlyList<object> Picked = Array.Empty<object>();

        public GameApi Game => Session?.Game;
    }

    // ── 트리거 ───────────────────────────────────────────────────

    [Serializable]
    public abstract class Trigger
    {
        // 잎이면 그 종류, 논리(AND·OR·NOT)면 None.
        public abstract TriggerKinds Kind { get; }
        public abstract bool Test(TriggerContext moment);
        public abstract string Describe(INameResolver names);
        public virtual string Natural(INameResolver names) => Describe(names);
        public virtual string Explain(INameResolver names) => Natural(names);
        public virtual IEnumerable<Trigger> Children => Array.Empty<Trigger>();

        public virtual void Validate(IValidationContext context)
        {
        }

        // 잎 전체(논리 블록 안쪽까지).
        public IEnumerable<Trigger> Leaves()
        {
            if (Kind != TriggerKinds.None) yield return this;
            foreach (var child in Children)
                if (child != null)
                    foreach (var leaf in child.Leaves())
                        yield return leaf;
        }

        // 이 트리거 안에 있는 종류들.
        public TriggerKinds Kinds() => Leaves().Aggregate(TriggerKinds.None, (all, leaf) => all | leaf.Kind);

        // 이벤트 받기 잎들이 보는 이벤트 형식(결과의 "이벤트 값"이 어느 이벤트의 필드인지 정할 때).
        public IEnumerable<Type> EventTypes() =>
            Leaves().OfType<EventTrigger>().Select(e => TypeResolver.Find(e.eventType)).Where(t => t != null).Distinct();

        // 조건 충족 잎들의 조건.
        public IEnumerable<Condition> Conditions() =>
            Leaves().OfType<ConditionTrigger>().Select(c => c.condition).Where(c => c != null);

        public static string NaturalOrNone(Trigger trigger, INameResolver names) => trigger == null ? "(트리거 없음)" : trigger.Natural(names);

        // 진행도(예: 37 / 100). 조건 잎의 진행도를 모아: AND는 가장 덜 된 것, OR는 가장 많이 된 것. 이벤트·사용 잎은 진행도가 없다.
        public bool TryGetProgress(GameSession session, out double current, out double target)
        {
            current = 0;
            target = 0;
            switch (this)
            {
                case ConditionTrigger single:
                    return single.condition != null && single.condition.TryGetProgress(session, out current, out target);
                case AllTrigger all:
                    return Pick(all.Children, session, lowest: true, out current, out target);
                case AnyTrigger any:
                    return Pick(any.Children, session, lowest: false, out current, out target);
            }
            return false;
        }

        static bool Pick(IEnumerable<Trigger> children, GameSession session, bool lowest, out double current, out double target)
        {
            current = 0;
            target = 0;
            var found = false;
            var best = 0.0;
            foreach (var child in children)
            {
                if (!child.TryGetProgress(session, out var c, out var g) || g <= 0) continue;
                var ratio = c / g;
                if (found && (lowest ? ratio >= best : ratio <= best)) continue;
                found = true;
                best = ratio;
                current = c;
                target = g;
            }
            return found;
        }
    }

    [Serializable]
    [BlockInfo("이벤트 받기", Category = "트리거", Description = "이벤트를 받은 순간입니다. 이벤트의 값으로 거를 수 있습니다(예: 태그 boss 포함인 처치만).",
               Sentence = "{eventType} 받기")]
    public sealed class EventTrigger : Trigger
    {
        [EventTypeRef, FieldPathRoot(FieldPathRootKind.EventType), FieldLabel("이벤트")] public string eventType;
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.SerializeReference]
#endif
        [FieldLabel("이벤트 조건")] public ValueFilter filter;

        public override TriggerKinds Kind => TriggerKinds.Event;

        // 지금 형식 이름(옛 이름 GameSystems.… 으로 저장된 것도 지금 이름으로). 처음 한 번 찾아 둔다.
        [NonSerialized] string _resolvedFrom;
        [NonSerialized] string _resolved;

        public string ResolvedType
        {
            get
            {
                if (_resolved != null && ReferenceEquals(_resolvedFrom, eventType)) return _resolved;
                _resolvedFrom = eventType;
                _resolved = string.IsNullOrEmpty(eventType) ? "" : TypeResolver.Find(eventType)?.FullName ?? eventType;
                return _resolved;
            }
        }

        public override bool Test(TriggerContext moment)
        {
            var evt = moment?.Event;
            if (evt == null || string.IsNullOrEmpty(eventType) || evt.GetType().FullName != ResolvedType) return false;
            return filter == null || filter.Test(evt);
        }

        public override string Describe(INameResolver names) =>
            $"on {StatRule.EventDisplayName(eventType)}" + (filter == null ? "" : $" where {filter.Describe()}");

        public override string Natural(INameResolver names) =>
            $"{StatRule.EventDisplayName(eventType)} 받기" + (filter == null ? "" : $"({filter.Natural()}일 때)");

        public override void Validate(IValidationContext context)
        {
            var type = TypeResolver.Find(eventType);
            if (type == null)
            {
                context.Error(string.IsNullOrEmpty(eventType) ? "이벤트 받기: 이벤트가 선택되지 않았습니다." : $"이벤트 받기: 이벤트 '{eventType}'을 찾을 수 없습니다(이름이 바뀌었는지 확인).");
                return;
            }
            if (filter == null) return;
            var problems = new List<string>();
            filter.Validate(type, problems);
            foreach (var problem in problems) context.Error($"이벤트 받기({EventBus.DisplayNameOf(type)}) 조건: {problem}");
        }
    }

    [Serializable]
    [BlockInfo("조건 충족", Category = "트리거", Description = "조건이 참이 되는 순간입니다(로그·플레이어 데이터를 봅니다).")]
    public sealed class ConditionTrigger : Trigger
    {
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.SerializeReference]
#endif
        [FieldLabel("조건")] public Condition condition;

        public override TriggerKinds Kind => TriggerKinds.Condition;

        public override bool Test(TriggerContext moment) => condition != null && moment?.Session != null && condition.Evaluate(moment.Session);

        public override string Describe(INameResolver names) => condition?.Describe(names) ?? "(조건 없음)";
        public override string Natural(INameResolver names) => condition?.Natural(names) ?? "(조건 없음)";
        public override string Explain(INameResolver names) => condition?.Explain(names) ?? "조건 충족";

        public override void Validate(IValidationContext context)
        {
            if (condition == null) context.Error("조건 충족: 조건이 비어 있습니다.");
            else condition.Validate(context);
        }
    }

    [Serializable]
    [BlockInfo("사용", Category = "트리거", Description = "아이템·능력을 쓰는 순간입니다(게임 코드의 Game.Use).")]
    public sealed class UseTrigger : Trigger
    {
        public override TriggerKinds Kind => TriggerKinds.Use;
        public override bool Test(TriggerContext moment) => moment != null && moment.IsUse;
        public override string Describe(INameResolver names) => "on use";
        public override string Natural(INameResolver names) => "사용할 때";
    }

    [Serializable]
    [BlockInfo("&& AND", Category = "논리", Description = "안의 트리거를 모두 만족해야 합니다(그리고).", Keyword = "AND")]
    public sealed class AllTrigger : Trigger
    {
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.SerializeReference]
#endif
        [FieldLabel("트리거")] public List<Trigger> triggers = new List<Trigger>();

        public override TriggerKinds Kind => TriggerKinds.None;
        public override IEnumerable<Trigger> Children => triggers.Where(t => t != null);
        public override bool Test(TriggerContext moment) => Children.Any() && Children.All(t => t.Test(moment));
        public override string Describe(INameResolver names) => "(" + string.Join(" && ", Children.Select(t => t.Describe(names))) + ")";
        public override string Natural(INameResolver names) => string.Join(" 그리고 ", Children.Select(t => Wrap(t, names)));

        public override void Validate(IValidationContext context)
        {
            if (!Children.Any()) context.Error("'&& AND'이 비어 있습니다.");
            // 서로 다른 이벤트가 같은 순간에 함께 오지는 않는다.
            if (Children.Count(t => t is EventTrigger) > 1)
                context.Warning("이벤트 받기 둘을 AND로 묶었습니다. 이벤트는 한 순간에 하나씩 받으므로 같은 이벤트가 아니면 만족하지 않습니다(OR를 뜻했는지 확인).");
            foreach (var child in Children) child.Validate(context);
        }

        internal static string Wrap(Trigger trigger, INameResolver names) =>
            trigger is AllTrigger || trigger is AnyTrigger ? $"({trigger.Natural(names)})" : trigger.Natural(names);
    }

    [Serializable]
    [BlockInfo("|| OR", Category = "논리", Description = "안의 트리거 중 하나만 만족해도 됩니다(또는).", Keyword = "OR")]
    public sealed class AnyTrigger : Trigger
    {
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.SerializeReference]
#endif
        [FieldLabel("트리거")] public List<Trigger> triggers = new List<Trigger>();

        public override TriggerKinds Kind => TriggerKinds.None;
        public override IEnumerable<Trigger> Children => triggers.Where(t => t != null);
        public override bool Test(TriggerContext moment) => Children.Any(t => t.Test(moment));
        public override string Describe(INameResolver names) => "(" + string.Join(" || ", Children.Select(t => t.Describe(names))) + ")";
        public override string Natural(INameResolver names) => string.Join(" 또는 ", Children.Select(t => AllTrigger.Wrap(t, names)));

        public override void Validate(IValidationContext context)
        {
            if (!Children.Any()) context.Error("'|| OR'이 비어 있습니다.");
            foreach (var child in Children) child.Validate(context);
        }
    }

    [Serializable]
    [BlockInfo("! NOT", Category = "논리", Description = "안의 트리거가 아닐 때입니다(주로 조건에 씁니다).", Keyword = "NOT")]
    public sealed class NotTrigger : Trigger
    {
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.SerializeReference]
#endif
        [FieldLabel("트리거")] public Trigger trigger;

        public override TriggerKinds Kind => TriggerKinds.None;
        public override IEnumerable<Trigger> Children => trigger == null ? Array.Empty<Trigger>() : new[] { trigger };
        public override bool Test(TriggerContext moment) => trigger != null && !trigger.Test(moment);
        public override string Describe(INameResolver names) => $"!({trigger?.Describe(names)})";
        public override string Natural(INameResolver names) => $"({trigger?.Natural(names)}) 아님";

        public override void Validate(IValidationContext context)
        {
            if (trigger == null) context.Error("'! NOT' 안에 트리거가 없습니다.");
            else
            {
                // 이벤트·사용은 "그 순간"이라 뒤집으면 "그 순간이 아닌 모든 때"가 된다: 혼자면 발동하지 않고, 조건과 AND면 조건만으로 발동한다.
                if ((trigger.Kinds() & (TriggerKinds.Event | TriggerKinds.Use)) != 0)
                    context.Warning("'! NOT' 안에 이벤트 받기·사용이 있습니다. 순간을 뒤집으면 뜻이 흐려집니다(혼자면 발동하지 않음). 이벤트의 값을 거르려면 이벤트 받기의 조건 칸을 쓰세요.");
                trigger.Validate(context);
            }
        }
    }

    // ── 결과 ─────────────────────────────────────────────────────

    [Serializable]
    public abstract class Outcome
    {
        public abstract OutcomeKinds Kind { get; }
        public abstract void Run(TriggerContext context);
        public abstract string Describe(INameResolver names);
        public virtual string Natural(INameResolver names) => Describe(names);
        public virtual string Explain(INameResolver names) => Natural(names);

        public virtual void Validate(IValidationContext context)
        {
        }
    }

    [Serializable]
    [BlockInfo("로그에 기록", Category = "결과", Description = "트리거가 된 이벤트의 값(없으면 횟수 1)에 배수를 곱해 로그에 반영합니다. 로그의 집계 방식(합계·최고치 …)을 따릅니다.")]
    public sealed class RecordOutcome : Outcome
    {
        // 비어 있으면 이 결과를 가진 로그 자신(로그 탭에서는 고정).
        [StatRef, FieldLabel("로그")] public string stat = "";
        // 트리거의 이벤트에서 읽을 숫자 필드(비우면 횟수 1).
        [EventFieldRef(""), FieldLabel("반영할 값")] public string field = "";
        [FieldLabel("배수")] public double multiplier = 1;

        // 이벤트 형식별 값 읽기 함수(이벤트마다 리플렉션으로 다시 만들지 않는다).
        [NonSerialized] Type _getterType;
        [NonSerialized] string _getterField;
        [NonSerialized] Func<object, double> _getter;
        [NonSerialized] string _getterError;

        public override OutcomeKinds Kind => OutcomeKinds.Record;

        public override void Run(TriggerContext context)
        {
            var target = string.IsNullOrEmpty(stat) ? context.Owner : stat;
            if (!context.Session.Stats.IsDefined(target))
            {
                context.Session.Journal.Append(JournalKind.Warning, "Outcomes",
                    string.IsNullOrEmpty(stat) ? $"로그에 기록: 기록할 로그를 고르지 않았습니다({target}은(는) 로그가 아님)." : $"로그에 기록: 없는 로그 '{target}'입니다.");
                return;
            }
            var value = ValueOf(context.Event, out var error);
            if (error != null)
            {
                context.Session.Journal.Append(JournalKind.Warning, "Outcomes", $"로그에 기록({target}): {error}");
                return;
            }
            context.Session.Stats.Report(target, value * multiplier);
        }

        public double ValueOf(object evt, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(field)) return 1;
            if (evt == null)
            {
                error = $"'{field}' 값을 읽을 이벤트가 없습니다(트리거가 이벤트 받기가 아님).";
                return 0;
            }
            var type = evt.GetType();
            if (_getterType != type || _getterField != field)
            {
                _getterType = type;
                _getterField = field;
                _getter = TypeResolver.NumberGetter(type, field, out _getterError);
            }
            error = _getterError;
            return _getter == null ? 0 : _getter(evt);
        }

        string Target(INameResolver names) => string.IsNullOrEmpty(stat) ? "이 로그" : names.StatName(stat);
        string Factor => Math.Abs(multiplier - 1) < 1e-9 ? "" : $" × {multiplier.ToString("0.##", CultureInfo.InvariantCulture)}";

        public override string Describe(INameResolver names) => $"{Target(names)} += {(string.IsNullOrEmpty(field) ? "1" : field)}{Factor}";
        public override string Natural(INameResolver names) => $"{(string.IsNullOrEmpty(field) ? "횟수" : field)}{Factor}를 {Target(names)}에 반영";

        public override void Validate(IValidationContext context)
        {
            if (!string.IsNullOrEmpty(stat) && !context.StatExists(stat)) context.Error($"로그에 기록: 존재하지 않는 로그 '{stat}'를 가리킵니다.");
            if (Math.Abs(multiplier) < 1e-12) context.Warning("로그에 기록: 배수가 0이라 아무것도 반영되지 않습니다.");
        }
    }

    // 대상 Block이 고른 객체마다 효과 Block을 실행한다(능력·사용 아이템·도전과제 등 어디서나).
    // 대상 Block은 위에서부터 차례로(앞 결과가 다음 입력, 비어 있으면 사용할 때 고른 대상 그대로).
    [Serializable]
    [BlockInfo("대상에게 효과", Category = "결과", Description = "대상 Block으로 누구에게 줄지 고르고, 그 대상마다 효과 Block을 실행합니다.")]
    public sealed class TargetedOutcome : Outcome
    {
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.SerializeReference]
#endif
        [FieldLabel("대상")] public List<TargetSelector> targets = new List<TargetSelector>();
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.SerializeReference]
#endif
        [FieldLabel("효과")] public List<Effect> effects = new List<Effect>();

        public override OutcomeKinds Kind => OutcomeKinds.Targeted;

        public override void Run(TriggerContext context) => Apply(context, null);

        // 실제로 효과를 받은 대상들을 돌려준다.
        public List<object> Apply(TriggerContext context, AbilityDefinition ability)
        {
            var use = new AbilityUse { Game = context.Game, Ability = ability, User = context.User, Picked = context.Picked ?? Array.Empty<object>(), Context = context };
            IEnumerable<object> chosen = use.Picked;
            foreach (var selector in targets.Where(t => t != null)) chosen = selector.Run(use, chosen);
            var final = chosen.ToList();
            foreach (var target in final)
                foreach (var effect in effects.Where(e => e != null))
                    if (effect.TargetType.IsInstanceOfType(target)) effect.Run(use, target);
            return final;
        }

        public override string Describe(INameResolver names)
        {
            var who = targets.Count == 0 ? "고른 대상" : string.Join(" → ", targets.Where(t => t != null).Select(t => t.Describe()));
            return $"{who}: {string.Join(", ", effects.Where(e => e != null).Select(e => e.Describe()))}";
        }

        public override void Validate(IValidationContext context)
        {
            if (effects.Count == 0) context.Warning("대상에게 효과: 효과가 없습니다.");
            if (effects.Contains(null) || targets.Contains(null)) context.Error("대상에게 효과: 비어 있는 Block이 있습니다(스크립트가 지워졌을 수 있음).");
            foreach (var target in targets) target?.Validate(context);
            foreach (var effect in effects) effect?.Validate(context);
        }
    }

    // 트리거 하나 + 결과 목록(로그의 반영 규칙 한 줄 등).
    [Serializable]
    public sealed class TriggerRule
    {
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.SerializeReference]
#endif
        [FieldLabel("트리거")] public Trigger when;
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.SerializeReference]
#endif
        [FieldLabel("결과")] public List<Outcome> then = new List<Outcome>();

        public string Natural(INameResolver names) =>
            $"{Trigger.NaturalOrNone(when, names)} → {(then.Count == 0 ? "(결과 없음)" : string.Join(", ", then.Where(o => o != null).Select(o => o.Natural(names))))}";

        public string Describe(INameResolver names) =>
            $"{when?.Describe(names) ?? "(none)"}: {string.Join("; ", then.Where(o => o != null).Select(o => o.Describe(names)))}";
    }

    // ── 트리거 감시 ──────────────────────────────────────────────
    // 이벤트 받기 잎은 이벤트를 받을 때, 조건 충족 잎은 그 조건이 읽는 값이 바뀔 때(ConditionWatcher), 사용 잎은 Game.Use 때 검사한다.
    // 검사 결과가 참이면 발동한다. 이벤트·사용은 그 순간마다, 조건만으로 된 트리거는 참이 되는 순간에(반복이면 다시 거짓이 된 뒤 또).
    public sealed class TriggerSystem
    {
        sealed class Entry
        {
            public string Owner;
            public Trigger When;
            public bool Repeat;
            public Action<TriggerContext> Fire;
            public TriggerKinds Kinds;
            public string WatchKey;
        }

        readonly GameSession _session;
        readonly List<Entry> _entries = new List<Entry>();
        readonly Dictionary<string, List<Entry>> _byEvent = new Dictionary<string, List<Entry>>(StringComparer.Ordinal);
        int _watchSerial;
        // 결과가 이벤트를 쏴서 트리거가 다시 발동하는 사슬의 깊이. 한도를 넘으면 끊는다(같은 트리거가 자기를 부르는 무한 반복 방지).
        int _depth;
        public const int MaxDepth = 16;

        public TriggerSystem(GameSession session)
        {
            _session = session;
            Watcher = new ConditionWatcher(session);
            session.Events.SubscribeAll(OnEvent);
        }

        public ConditionWatcher Watcher { get; }

        // 지금 검사·발동 중인 순간(조건·결과 Block이 이벤트·사용자·대상을 볼 때). 밖에서는 null.
        public TriggerContext Current { get; private set; }

        public int Count => _entries.Count;

        // owner의 트리거를 감시한다. fire: 발동했을 때(결과 실행은 부르는 쪽이 RunOutcomes로). repeat이 아니면 한 번 발동하면 감시를 그만둔다.
        public void Register(string owner, Trigger when, bool repeat, Action<TriggerContext> fire)
        {
            if (when == null) return;
            var entry = new Entry { Owner = owner, When = when, Repeat = repeat, Fire = fire, Kinds = when.Kinds() };
            _entries.Add(entry);
            foreach (var type in when.Leaves().OfType<EventTrigger>().Select(e => e.ResolvedType).Where(t => !string.IsNullOrEmpty(t)).Distinct())
            {
                if (!_byEvent.TryGetValue(type, out var list)) _byEvent[type] = list = new List<Entry>();
                list.Add(entry);
            }
            if ((entry.Kinds & TriggerKinds.Condition) != 0) WatchConditions(entry, armed: true);
        }

        public void Unregister(string owner)
        {
            foreach (var entry in _entries.Where(e => e.Owner == owner).ToList()) Remove(entry);
        }

        public bool IsRegistered(string owner) => _entries.Any(e => e.Owner == owner);

        void Remove(Entry entry)
        {
            _entries.Remove(entry);
            foreach (var list in _byEvent.Values) list.Remove(entry);
            if (entry.WatchKey != null) Watcher.Unwatch(entry.WatchKey);
            entry.WatchKey = null;
        }

        // 조건이 들어 있는 트리거: 조건이 읽는 값이 바뀔 때 이벤트 없는 순간으로 검사한다(OR 안의 조건도 이렇게 발동한다).
        // armed가 아니면(반복 트리거가 방금 발동함) 먼저 거짓이 되는 것을 본 뒤에 다시 발동할 수 있다.
        void WatchConditions(Entry entry, bool armed)
        {
            entry.WatchKey = $"trigger:{entry.Owner}#{++_watchSerial}";
            var probe = new MomentCondition(this, entry, armed);
            Watcher.Watch(entry.WatchKey, probe, () =>
            {
                entry.WatchKey = null;
                Fire(entry, Moment(entry.Owner));
                if (entry.Repeat && _entries.Contains(entry)) WatchConditions(entry, armed: false);
            });
        }

        TriggerContext Moment(string owner) => new TriggerContext { Session = _session, Owner = owner };

        // 한 이벤트에 걸린 트리거의 순서: 조건이 없는 것(로그 기록 등)을 먼저, 조건이 있는 것(도전과제 등)을 나중에.
        // 그래야 "처치 받기 AND 처치 수 ≥ 10"이 같은 이벤트로 막 오른 처치 수를 본다.
        void OnEvent(object evt)
        {
            if (!_byEvent.TryGetValue(evt.GetType().FullName, out var list)) return;
            if (_depth >= MaxDepth)
            {
                _session.Journal.Append(JournalKind.Warning, "Triggers",
                    $"트리거 사슬이 {MaxDepth}단계를 넘어 끊었습니다({EventBus.DisplayNameOf(evt.GetType())}). 결과가 자기 트리거의 이벤트를 다시 쏘는지 확인하세요.");
                return;
            }
            _depth++;
            try
            {
                var ordered = list.Where(e => (e.Kinds & TriggerKinds.Condition) == 0).Concat(list.Where(e => (e.Kinds & TriggerKinds.Condition) != 0)).ToList();
                foreach (var entry in ordered)
                {
                    if (!_entries.Contains(entry)) continue;
                    var moment = new TriggerContext { Session = _session, Owner = entry.Owner, Event = evt };
                    if (Test(entry.When, moment)) Fire(entry, moment);
                }
            }
            finally { _depth--; }
        }

        // 사용하는 순간: owner의 트리거가 이 순간에 참인지(사용 조건). 트리거가 없으면 참.
        public bool CanUse(string owner, Trigger when, object user, IReadOnlyList<object> picked, out TriggerContext moment)
        {
            moment = new TriggerContext { Session = _session, Owner = owner, IsUse = true, User = user, Picked = picked ?? Array.Empty<object>() };
            return when == null || Test(when, moment);
        }

        public bool Test(Trigger when, TriggerContext moment)
        {
            var previous = Current;
            Current = moment;
            try { return when.Test(moment); }
            finally { Current = previous; }
        }

        void Fire(Entry entry, TriggerContext moment)
        {
            if (!entry.Repeat) Remove(entry);
            var previous = Current;
            Current = moment;
            try { entry.Fire?.Invoke(moment); }
            finally { Current = previous; }
        }

        // 결과를 차례로 실행한다. 결과 Block이 꺼낸 객체를 직접 고쳐도 기록된다.
        public void RunOutcomes(IEnumerable<Outcome> outcomes, TriggerContext context, string source)
        {
            var previous = Current;
            Current = context;
            try
            {
                foreach (var outcome in outcomes)
                {
                    if (outcome == null) continue;
                    if (!(outcome is RecordOutcome)) _session.Journal.Append(JournalKind.Reward, source, "결과: " + outcome.Natural(_session));
                    _session.Data.Track(() => outcome.Run(context));
                }
            }
            finally { Current = previous; }
        }

        public void Flush() => Watcher.Flush();

        // 조건 감시기에 넘기는 조건: 트리거 전체를 이벤트 없는 순간으로 검사한다. 감시할 값은 조건 잎들이 정한다.
        sealed class MomentCondition : Condition
        {
            readonly TriggerSystem _system;
            readonly Entry _entry;
            bool _armed;

            public MomentCondition(TriggerSystem system, Entry entry, bool armed)
            {
                _system = system;
                _entry = entry;
                _armed = armed;
            }

            public override bool Evaluate(GameSession session)
            {
                var result = _system.Test(_entry.When, _system.Moment(_entry.Owner));
                if (_armed) return result;
                if (!result) _armed = true;
                return false;
            }

            public override void CollectDependencies(ICollection<string> stateKeys)
            {
                foreach (var condition in _entry.When.Conditions()) condition.CollectDependencies(stateKeys);
            }

            public override bool AutoDependencies => true;
            public override double PollInterval => _entry.When.Conditions().Select(c => c.PollInterval).Where(p => p > 0).DefaultIfEmpty(0).Min();
            public override string Describe(INameResolver names) => _entry.When.Describe(names);
        }
    }
}
