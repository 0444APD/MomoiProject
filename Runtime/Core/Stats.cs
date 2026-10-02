using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;

namespace GBible
{
    // Run: 이번 판(세션 구역, 되감김). Total: 누적(메타 구역, 저장됨).
    public enum StatScope
    {
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("이번 판")]
#endif
        Run,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("누적")]
#endif
        Total,
    }

    public enum StatAggregate
    {
        // 값을 더한다(처치 수, 획득 골드).
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("합계")]
#endif
        Sum,
        // 최고치를 남긴다(최대 콤보, 한 방 최대 피해).
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("최고치")]
#endif
        Max,
        // 최저치를 남긴다(최단 클리어 시간).
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("최저치")]
#endif
        Min,
        // 마지막 값으로 덮어쓴다.
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("마지막 값")]
#endif
        Last,
    }

    // "이벤트를 받으면 필드 값을 로그에 반영한다"의 짧은 형태(예전 저장 형식·코드용). field가 비어 있으면 1(횟수)을 반영한다.
    // 실행할 때는 트리거·결과 규칙(이벤트 받기 → 로그에 기록)으로 바뀌어 다른 기능과 같은 길로 돈다(ToTriggerRule).
    [Serializable]
    public class StatRule
    {
        [EventTypeRef, FieldPathRoot(FieldPathRootKind.EventType), FieldLabel("이벤트")] public string eventType;
        [EventFieldRef(nameof(eventType)), FieldLabel("반영할 값")] public string field;
        [FieldLabel("배수")] public double multiplier = 1;
        // 이벤트를 걸러 내는 조건식(참일 때만 반영). 비어 있으면 모든 이벤트. 예: 태그 boss 포함 → 보스 처치만 센다.
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.SerializeReference]
#endif
        [FieldLabel("조건")] public ValueFilter filter;

        public string Describe(string statName)
        {
            var typeName = EventDisplayName(eventType);
            var source = string.IsNullOrEmpty(field) ? "횟수" : field;
            var factor = Math.Abs(multiplier - 1) < 1e-9 ? "" : $" × {multiplier.ToString("0.##", CultureInfo.InvariantCulture)}";
            var when = filter == null ? "" : $" ({filter.Natural()}일 때)";
            return $"{typeName} 받기 시{when} {source}{factor}를 {statName}에 반영";
        }

        // 코드 형태(Alt): on 적 처치 where (tags contains boss) : 처치 수 += Count × 2
        public string DescribeCode(string statName)
        {
            var typeName = EventDisplayName(eventType);
            var source = string.IsNullOrEmpty(field) ? "1" : field;
            var factor = Math.Abs(multiplier - 1) < 1e-9 ? "" : $" * {multiplier.ToString("0.##", CultureInfo.InvariantCulture)}";
            var where = filter == null ? "" : $" where {filter.Describe()}";
            return $"on {typeName}{where}: {statName} += {source}{factor}";
        }
        public TriggerRule ToTriggerRule() => new TriggerRule
        {
            when = new EventTrigger { eventType = eventType, filter = filter },
            then = { new RecordOutcome { field = field ?? "", multiplier = multiplier } },
        };

        public static string EventDisplayName(string typeName)
        {
            var type = TypeResolver.Find(typeName);
            if (type != null) return EventBus.DisplayNameOf(type);
            return string.IsNullOrEmpty(typeName) ? "(이벤트 없음)" : typeName;
        }
    }

    public sealed class StatDefinition
    {
        public string Id;
        public string DisplayName;
        public StatScope Scope;
        public StatAggregate Aggregate;
        public double? Initial;
        // 반영 규칙: 트리거(이벤트 받기) → 결과(로그에 기록). 짧은 형태 Rules도 같은 규칙으로 바뀌어 돈다.
        public readonly List<TriggerRule> Triggers = new List<TriggerRule>();
        public readonly List<StatRule> Rules = new List<StatRule>();

        public IEnumerable<TriggerRule> AllRules() => Rules.Where(r => r != null).Select(r => r.ToTriggerRule()).Concat(Triggers.Where(r => r != null));

        // "이벤트 하나 받기 → 이 로그에 기록" 꼴로 읽을 수 있는 규칙(시뮬레이션이 거꾸로 따라가 이벤트를 만들 때). 그 밖의 트리거는 뺀다.
        public IEnumerable<StatRule> SimpleRules()
        {
            foreach (var rule in Rules.Where(r => r != null)) yield return rule;
            foreach (var rule in Triggers.Where(r => r?.when is EventTrigger))
            {
                var when = (EventTrigger)rule.when;
                foreach (var record in rule.then.OfType<RecordOutcome>().Where(o => string.IsNullOrEmpty(o.stat) || o.stat == Id))
                    yield return new StatRule { eventType = when.eventType, filter = when.filter, field = record.field, multiplier = record.multiplier };
            }
        }
    }

    public static class PlayerDataKeys
    {
        public const string StatPrefix = "stat:";
        public const string AchievementPrefix = "achievement:";

        public static string Stat(string id) => StatPrefix + id;
        public static string Achievement(string id) => AchievementPrefix + id;
    }

    // 로그: 값의 기록. 반영 규칙은 공용 트리거 감시(TriggerSystem)에 등록해 다른 기능과 같은 길로 돈다.
    public sealed class StatSystem : IInspectable
    {
        readonly Dictionary<string, StatDefinition> _definitions = new Dictionary<string, StatDefinition>(StringComparer.Ordinal);
        readonly PlayerDataStore _state;
        readonly Journal _journal;
        readonly TriggerSystem _triggers;

        public StatSystem(PlayerDataStore state, TriggerSystem triggers, Journal journal)
        {
            _state = state;
            _journal = journal;
            _triggers = triggers;
        }

        public string InspectName => "로그";
        public IEnumerable<StatDefinition> Definitions => _definitions.Values;

        public void Define(StatDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (string.IsNullOrEmpty(definition.Id)) throw new ArgumentException("[MomoiProject] 수치 ID가 비어 있습니다.");

            _definitions[definition.Id] = definition;
            var zone = definition.Scope == StatScope.Run ? DataZone.Session : DataZone.Meta;
            _state.Define(PlayerDataKeys.Stat(definition.Id), zone, DefaultValue(definition), definition.DisplayName ?? definition.Id);

            _triggers.Unregister(definition.Id);
            foreach (var rule in definition.AllRules()) Bind(definition, rule);
        }

        void Bind(StatDefinition stat, TriggerRule rule)
        {
            if (rule.when == null)
            {
                _journal.Append(JournalKind.Warning, "Stats", $"로그 '{stat.DisplayName}'의 규칙에 트리거가 없습니다.");
                return;
            }
            foreach (var missing in rule.when.Leaves().OfType<EventTrigger>().Where(e => TypeResolver.Find(e.eventType) == null))
                _journal.Append(JournalKind.Warning, "Stats", $"로그 '{stat.DisplayName}' 규칙의 이벤트 타입을 찾을 수 없습니다: {missing.eventType}");
            // 코드로 쓴 결과가 이 로그에 연결되어 있으면([BindTo]) '로그에 기록' 대신 그것이 돈다.
            IEnumerable<Outcome> outcomes = BoundOutcomes.Has(stat.Id) ? BoundOutcomes.Create(stat.Id) : rule.then;
            _triggers.Register(stat.Id, rule.when, repeat: true, fire: moment => _triggers.RunOutcomes(outcomes, moment, "Stats"));
        }

        public bool IsDefined(string id) => id != null && _definitions.ContainsKey(id);

        public StatDefinition Find(string id) => id != null && _definitions.TryGetValue(id, out var d) ? d : null;

        public double Get(string id) => _state.GetNumber(PlayerDataKeys.Stat(RequireDefined(id).Id));

        // 수치의 집계 방식에 따라 값을 반영한다(Sum: 더함, Max/Min: 기록 갱신, Last: 덮어씀).
        public void Report(string id, double value)
        {
            var definition = RequireDefined(id);
            var key = PlayerDataKeys.Stat(id);
            var current = _state.GetNumber(key);
            double next;
            switch (definition.Aggregate)
            {
                case StatAggregate.Sum:
                    next = (double.IsNaN(current) ? 0 : current) + value;
                    break;
                case StatAggregate.Max:
                    next = double.IsNaN(current) ? value : Math.Max(current, value);
                    break;
                case StatAggregate.Min:
                    next = double.IsNaN(current) ? value : Math.Min(current, value);
                    break;
                default:
                    next = value;
                    break;
            }
            _state.Set(key, next);
        }

        public void Set(string id, double value) => _state.Set(PlayerDataKeys.Stat(RequireDefined(id).Id), value);

        // 이번 판(Run) 수치를 초기값으로 되돌린다.
        public void ResetRun()
        {
            foreach (var definition in _definitions.Values)
                if (definition.Scope == StatScope.Run)
                    _state.Set(PlayerDataKeys.Stat(definition.Id), DefaultValue(definition));
        }

        StatDefinition RequireDefined(string id)
        {
            if (id != null && _definitions.TryGetValue(id, out var definition)) return definition;
            throw new InvalidOperationException($"[MomoiProject] 정의되지 않은 수치 '{id}'입니다.");
        }

        static object DefaultValue(StatDefinition definition)
        {
            if (definition.Initial.HasValue) return definition.Initial.Value;
            // Max·Min은 기록이 없으면 비어 있어야 "첫 기록"을 구분할 수 있다.
            return definition.Aggregate == StatAggregate.Max || definition.Aggregate == StatAggregate.Min ? null : (object)0.0;
        }

        public void Inspect(InspectNode root)
        {
            foreach (var definition in _definitions.Values)
            {
                var node = root.Add(definition.DisplayName ?? definition.Id,
                                    PlayerDataStore.Format(_state.Get(PlayerDataKeys.Stat(definition.Id))),
                                    PlayerDataKeys.Stat(definition.Id));
                node.Tag = (definition.Scope == StatScope.Run ? "이번 판" : "누적") + " · " + AggregateText(definition.Aggregate);
            }
        }

        public static string AggregateText(StatAggregate aggregate)
        {
            switch (aggregate)
            {
                case StatAggregate.Sum: return "합계";
                case StatAggregate.Max: return "최고치";
                case StatAggregate.Min: return "최저치";
                default: return "마지막 값";
            }
        }
    }
}
