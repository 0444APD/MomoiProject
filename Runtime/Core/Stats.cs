using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;

namespace GameSystems
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

    // "이벤트가 발생하면 필드 값을 수치에 반영한다" 규칙. field가 비어 있으면 1(횟수)을 반영한다.
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
            var when = filter == null ? "" : $" ({filter.Describe()}일 때)";
            return $"{typeName} 발생 시{when} {source}{factor}를 {statName}에 반영";
        }

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
        public readonly List<StatRule> Rules = new List<StatRule>();
    }

    public static class StateKeys
    {
        public const string StatPrefix = "stat:";
        public const string AchievementPrefix = "achievement:";

        public static string Stat(string id) => StatPrefix + id;
        public static string Achievement(string id) => AchievementPrefix + id;
    }

    public sealed class StatSystem : IInspectable
    {
        sealed class BoundRule
        {
            public StatDefinition Stat;
            public StatRule Rule;
            public Func<object, double> Getter;
        }

        readonly Dictionary<string, StatDefinition> _definitions = new Dictionary<string, StatDefinition>(StringComparer.Ordinal);
        readonly Dictionary<Type, List<BoundRule>> _rulesByEvent = new Dictionary<Type, List<BoundRule>>();
        readonly StateStore _state;
        readonly Journal _journal;

        public StatSystem(StateStore state, EventBus events, Journal journal)
        {
            _state = state;
            _journal = journal;
            events.SubscribeAll(OnEvent);
        }

        public string InspectName => "수치";
        public IEnumerable<StatDefinition> Definitions => _definitions.Values;

        public void Define(StatDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (string.IsNullOrEmpty(definition.Id)) throw new ArgumentException("[GameSystems] 수치 ID가 비어 있습니다.");

            _definitions[definition.Id] = definition;
            var zone = definition.Scope == StatScope.Run ? StateZone.Session : StateZone.Meta;
            _state.Define(StateKeys.Stat(definition.Id), zone, DefaultValue(definition), definition.DisplayName ?? definition.Id);

            foreach (var rule in definition.Rules) Bind(definition, rule);
        }

        void Bind(StatDefinition stat, StatRule rule)
        {
            var type = TypeResolver.Find(rule.eventType);
            if (type == null)
            {
                _journal.Append(JournalKind.Warning, "Stats", $"수치 '{stat.DisplayName}' 규칙의 이벤트 타입을 찾을 수 없습니다: {rule.eventType}");
                return;
            }

            Func<object, double> getter;
            if (string.IsNullOrEmpty(rule.field))
            {
                getter = _ => 1;
            }
            else
            {
                getter = TypeResolver.NumberGetter(type, rule.field, out var error);
                if (getter == null)
                {
                    _journal.Append(JournalKind.Warning, "Stats", $"수치 '{stat.DisplayName}' 규칙 오류: {error}");
                    return;
                }
            }

            if (!_rulesByEvent.TryGetValue(type, out var list))
                _rulesByEvent[type] = list = new List<BoundRule>();
            list.Add(new BoundRule { Stat = stat, Rule = rule, Getter = getter });
        }

        void OnEvent(object evt)
        {
            if (!_rulesByEvent.TryGetValue(evt.GetType(), out var rules)) return;
            foreach (var bound in rules)
            {
                if (bound.Rule.filter != null && !bound.Rule.filter.Test(evt)) continue;
                Report(bound.Stat.Id, bound.Getter(evt) * bound.Rule.multiplier);
            }
        }

        public bool IsDefined(string id) => id != null && _definitions.ContainsKey(id);

        public StatDefinition Find(string id) => id != null && _definitions.TryGetValue(id, out var d) ? d : null;

        public double Get(string id) => _state.GetNumber(StateKeys.Stat(RequireDefined(id).Id));

        // 수치의 집계 방식에 따라 값을 반영한다(Sum: 더함, Max/Min: 기록 갱신, Last: 덮어씀).
        public void Report(string id, double value)
        {
            var definition = RequireDefined(id);
            var key = StateKeys.Stat(id);
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

        public void Set(string id, double value) => _state.Set(StateKeys.Stat(RequireDefined(id).Id), value);

        // 이번 판(Run) 수치를 초기값으로 되돌린다.
        public void ResetRun()
        {
            foreach (var definition in _definitions.Values)
                if (definition.Scope == StatScope.Run)
                    _state.Set(StateKeys.Stat(definition.Id), DefaultValue(definition));
        }

        StatDefinition RequireDefined(string id)
        {
            if (id != null && _definitions.TryGetValue(id, out var definition)) return definition;
            throw new InvalidOperationException($"[GameSystems] 정의되지 않은 수치 '{id}'입니다.");
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
                                    StateStore.Format(_state.Get(StateKeys.Stat(definition.Id))),
                                    StateKeys.Stat(definition.Id));
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
