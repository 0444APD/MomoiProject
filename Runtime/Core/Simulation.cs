using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace GBible
{
    // 테스트용 이벤트 받기. 값을 덮어쓰지 않고 게임과 똑같은 경로(행동 → 이벤트 → 수치 규칙 → 확정 → 도전과제 → 보상)를 탄다.
    // 행동으로 기록되므로 Undo·인과 추적이 그대로 적용된다.
    [Serializable]
    public sealed class SimulatedEventAction : GameAction
    {
        [NonSerialized] public List<object> Events = new List<object>();
        public string reason;

        public override ActionVisibility Visibility => ActionVisibility.Developer;

        public override string Describe()
        {
            var what = Events.Count == 1 ? EventBus.Describe(Events[0])
                     : Events.Count > 1 ? $"{EventBus.Describe(Events[0])} ×{Events.Count}"
                     : "(이벤트 없음)";
            return "[테스트] " + what + (string.IsNullOrEmpty(reason) ? "" : $" — {reason}");
        }
    }

    // 수치 하나를 이벤트로 움직이는 계획.
    public sealed class EventPlan
    {
        public readonly List<object> Events = new List<object>();
        public string Description;
    }

    public static class EventSimulator
    {
        const int MaxRepeat = 1000;

        // ── 이벤트 만들기 ───────────────────────────────────────

        // 테스트 화면에서 값을 채울 수 있는 멤버(공개 필드·쓰기 가능한 프로퍼티, 단순 형식만).
        public static IEnumerable<MemberInfo> EditableMembers(Type type)
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (!field.IsInitOnly && IsSimple(field.FieldType)) yield return field;
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0 && IsSimple(property.PropertyType))
                    yield return property;
        }

        public static Type MemberType(MemberInfo member) =>
            member is FieldInfo f ? f.FieldType : ((PropertyInfo)member).PropertyType;

        public static object GetValue(MemberInfo member, object target) =>
            member is FieldInfo f ? f.GetValue(target) : ((PropertyInfo)member).GetValue(target);

        public static bool IsSimple(Type type) =>
            type == typeof(string) || type.IsEnum || TypeResolver.IsNumeric(type);

        public static object CreateDefault(Type type)
        {
            if (type.IsValueType) return Activator.CreateInstance(type);
            if (type.GetConstructor(Type.EmptyTypes) == null)
                throw new InvalidOperationException($"[MomoiProject] 이벤트 '{type.Name}'에 매개변수 없는 생성자가 없어 테스트로 만들 수 없습니다.");
            return Activator.CreateInstance(type);
        }

        // 멤버 이름 → 값으로 이벤트를 만든다. 값은 멤버 형식으로 변환한다.
        public static object Create(Type type, IDictionary<string, object> values)
        {
            var instance = CreateDefault(type);
            if (values == null) return instance;
            foreach (var pair in values)
            {
                var member = (MemberInfo)type.GetField(pair.Key, BindingFlags.Public | BindingFlags.Instance)
                             ?? type.GetProperty(pair.Key, BindingFlags.Public | BindingFlags.Instance);
                if (member == null) throw new ArgumentException($"[MomoiProject] '{type.Name}'에 '{pair.Key}' 멤버가 없습니다.");
                var converted = Convert(pair.Value, MemberType(member), pair.Key);
                if (member is FieldInfo field) field.SetValue(instance, converted);
                else ((PropertyInfo)member).SetValue(instance, converted);
            }
            return instance;
        }

        static object Convert(object value, Type target, string name)
        {
            if (value == null) return target.IsValueType ? Activator.CreateInstance(target) : null;
            if (target.IsInstanceOfType(value)) return value;
            if (target.IsEnum) return value is string s ? Enum.Parse(target, s) : Enum.ToObject(target, System.Convert.ToInt64(value, CultureInfo.InvariantCulture));
            if (target == typeof(bool) && value is double d) return d != 0;
            var number = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
            if (IsInteger(target) && Math.Abs(number - Math.Round(number)) > 1e-9)
                throw new ArgumentException($"[MomoiProject] '{name}'은 정수 필드라 {number.ToString(CultureInfo.InvariantCulture)}을 넣을 수 없습니다.");
            return System.Convert.ChangeType(IsInteger(target) ? Math.Round(number) : number, target, CultureInfo.InvariantCulture);
        }

        static bool IsInteger(Type type)
        {
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                    return true;
            }
            return false;
        }

        // ── 수치를 이벤트로 움직이기 ─────────────────────────────

        // 합계형: amount만큼 더한다. 최고·최저·마지막 값형: amount를 한 번 기록한다.
        // 수치의 반영 규칙을 거꾸로 따라가 필요한 이벤트를 만든다. 같은 이벤트를 받는 다른 수치도 게임에서처럼 함께 움직인다.
        public static bool TryPlanForStat(StatSystem stats, string statId, double amount, out EventPlan plan, out string error)
        {
            plan = null;
            error = null;
            var definition = stats.Find(statId);
            if (definition == null)
            {
                error = $"정의되지 않은 수치 '{statId}'입니다.";
                return false;
            }
            if (!definition.SimpleRules().Any())
            {
                error = $"'{definition.DisplayName}'에는 반영 규칙이 없어 이벤트로 바꿀 수 없습니다. 직접 수정을 쓰세요.";
                return false;
            }

            var reasons = new List<string>();
            foreach (var rule in definition.SimpleRules())
            {
                var type = TypeResolver.Find(rule.eventType);
                if (type == null)
                {
                    reasons.Add($"이벤트 타입 '{rule.eventType}'을 찾을 수 없음");
                    continue;
                }
                if (Math.Abs(rule.multiplier) < 1e-12)
                {
                    reasons.Add("배수가 0인 규칙");
                    continue;
                }

                var name = EventBus.DisplayNameOf(type);
                var raw = amount / rule.multiplier;
                try
                {
                    if (string.IsNullOrEmpty(rule.field))
                    {
                        // 횟수 규칙: 이벤트 1회 = multiplier만큼 반영.
                        if (definition.Aggregate != StatAggregate.Sum)
                        {
                            reasons.Add($"{name}: 횟수 규칙은 합계형 수치에서만 이벤트로 재현할 수 있음");
                            continue;
                        }
                        var count = (int)Math.Round(raw);
                        if (count < 1 || Math.Abs(raw - count) > 1e-9 || count > MaxRepeat)
                        {
                            reasons.Add($"{name}: 횟수 규칙이라 1~{MaxRepeat} 사이의 정수 배만 가능");
                            continue;
                        }
                        plan = new EventPlan { Description = $"{name} ×{count}" };
                        for (int i = 0; i < count; i++) plan.Events.Add(CreateDefault(type));
                        return true;
                    }

                    if (rule.field.Contains('.'))
                    {
                        reasons.Add($"{name}: 중첩 필드({rule.field})는 자동 생성하지 않음");
                        continue;
                    }
                    var evt = Create(type, new Dictionary<string, object> { [rule.field] = raw });
                    plan = new EventPlan { Description = $"{name} ({rule.field} = {raw.ToString("0.##", CultureInfo.InvariantCulture)})" };
                    plan.Events.Add(evt);
                    return true;
                }
                catch (Exception e)
                {
                    reasons.Add($"{name}: {e.Message.Replace("[MomoiProject] ", "")}");
                }
            }

            error = "이벤트로 만들 수 있는 규칙이 없습니다: " + string.Join(", ", reasons);
            return false;
        }

        // ── 도전과제 조건 채우기 ─────────────────────────────────

        // 트리거를 따라가 테스트로 발동시키는 계획: 먼저 조건을 채울 로그 차이(로그 id, 반영할 값), 그다음 쏠 이벤트(값 조건을 맞춘 것).
        // 게임과 같은 경로라 조건이 맞은 상태에서 이벤트를 받아 트리거가 스스로 발동한다.
        //   조건 충족 → 부족한 로그를 이벤트로 채운다 / 이벤트 받기 → 그 이벤트를 만들고 값 조건(같음·이상·포함 …)에 맞춰 채운다 /
        //   AND → 모두 / OR → 계획이 서는 첫 갈래 / NOT·사용 → 자동으로 만들 수 없다(강제 달성을 쓴다).
        public static bool TryPlanForAchievement(GameSession session, string achievementId, out List<(string stat, double amount)> steps, out List<object> events, out string error)
        {
            steps = new List<(string, double)>();
            events = new List<object>();
            var problems = new List<string>();
            var definition = session.Achievements.Find(achievementId);
            if (definition?.When == null)
            {
                error = "트리거가 없는 도전과제입니다.";
                return false;
            }
            PlanTrigger(session, definition.When, steps, events, problems, new HashSet<string> { achievementId });
            // 같은 로그를 여러 조건이 보면 가장 큰 요구치 하나로 합친다.
            steps = steps.GroupBy(s => s.stat).Select(g => (g.Key, g.Max(s => s.amount))).ToList();
            error = problems.Count > 0 ? string.Join(" / ", problems) : null;
            return problems.Count == 0 && (steps.Count > 0 || events.Count > 0);
        }

        // 예전 형태(조건만): 로그 차이만.
        public static bool TryPlanForAchievement(GameSession session, string achievementId, out List<(string stat, double amount)> steps, out string error) =>
            TryPlanForAchievement(session, achievementId, out steps, out _, out error);

        static void PlanTrigger(GameSession session, Trigger trigger, List<(string stat, double amount)> steps, List<object> events, List<string> problems, HashSet<string> visiting)
        {
            switch (trigger)
            {
                case ConditionTrigger single:
                    if (single.condition != null) Collect(session, single.condition, steps, problems, visiting);
                    return;
                case EventTrigger fired:
                    var type = TypeResolver.Find(fired.eventType);
                    if (type == null)
                    {
                        problems.Add($"이벤트 '{fired.eventType}'을(를) 찾을 수 없음");
                        return;
                    }
                    object evt;
                    try { evt = CreateDefault(type); }
                    catch (InvalidOperationException e)
                    {
                        problems.Add(e.Message.Replace("[MomoiProject] ", ""));
                        return;
                    }
                    if (fired.filter != null)
                    {
                        Satisfy(evt, fired.filter);
                        if (!fired.filter.Test(evt))
                        {
                            problems.Add($"{EventBus.DisplayNameOf(type)}의 값 조건 '{fired.filter.Natural()}'을 자동으로 맞출 수 없음(이벤트 탭에서 값을 채워 직접 쏘세요)");
                            return;
                        }
                    }
                    events.Add(evt);
                    return;
                case AllTrigger all:
                    if (all.Children.Count(c => c is EventTrigger) > 1) problems.Add("서로 다른 이벤트를 AND로 묶어 같은 순간을 만들 수 없음");
                    foreach (var child in all.Children) PlanTrigger(session, child, steps, events, problems, visiting);
                    return;
                case AnyTrigger any:
                    // 계획이 서는 첫 갈래.
                    foreach (var child in any.Children)
                    {
                        var s = new List<(string, double)>();
                        var e = new List<object>();
                        var p = new List<string>();
                        PlanTrigger(session, child, s, e, p, new HashSet<string>(visiting));
                        if (p.Count > 0) continue;
                        steps.AddRange(s);
                        events.AddRange(e);
                        return;
                    }
                    problems.Add("OR의 어느 갈래도 자동으로 채울 수 없음");
                    return;
                case UseTrigger _:
                    problems.Add("'사용'은 테스트 이벤트로 재현할 수 없음(게임 코드의 Game.Use)");
                    return;
                default:
                    problems.Add($"'{trigger.Natural(session)}'은 자동으로 채울 수 없는 트리거");
                    return;
            }
        }

        // 이벤트 값을 값 조건에 맞게 채운다(필드 하나짜리 비교: 같음·이상·초과·이하·미만·참·거짓·포함, AND는 모두, OR는 첫 갈래).
        static void Satisfy(object evt, ValueFilter filter)
        {
            switch (filter)
            {
                case AllFilter all:
                    foreach (var child in all.Children) Satisfy(evt, child);
                    return;
                case AnyFilter any:
                    var first = any.Children.FirstOrDefault();
                    if (first != null) Satisfy(evt, first);
                    return;
                case FieldFilter field when !string.IsNullOrEmpty(field.path) && !field.path.Contains('.'):
                    var member = (MemberInfo)evt.GetType().GetField(field.path, BindingFlags.Public | BindingFlags.Instance)
                                 ?? evt.GetType().GetProperty(field.path, BindingFlags.Public | BindingFlags.Instance);
                    if (member == null) return;
                    var type = MemberType(member);
                    object value = null;
                    switch (field.op)
                    {
                        case FieldOp.Equal:
                        case FieldOp.AtLeast:
                        case FieldOp.AtMost:
                            PlayerDataGrants.TryParse(type, field.value, out value, out _);
                            break;
                        case FieldOp.Greater:
                        case FieldOp.Less:
                            if (TypeResolver.IsNumeric(type) && double.TryParse(field.value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                                value = PlayerDataGrants.ToNumber(field.op == FieldOp.Greater ? number + 1 : number - 1, type);
                            break;
                        case FieldOp.IsTrue:
                            if (type == typeof(bool)) value = true;
                            break;
                        case FieldOp.IsFalse:
                            if (type == typeof(bool)) value = false;
                            break;
                        case FieldOp.Contains:
                            if (type == typeof(string)) value = field.value;
                            else if (GetValue(member, evt) is System.Collections.IList list && FieldPaths.ElementTypeOf(type) is Type element &&
                                     PlayerDataGrants.TryParse(element, field.value, out var item, out _))
                                list.Add(item);
                            break;
                    }
                    if (value == null) return;
                    if (member is FieldInfo f) f.SetValue(evt, value);
                    else if (member is PropertyInfo p && p.CanWrite) p.SetValue(evt, value);
                    return;
            }
        }

        static void Collect(GameSession session, Condition condition, List<(string stat, double amount)> steps, List<string> problems, HashSet<string> visiting)
        {
            if (condition.Evaluate(session)) return;
            switch (condition)
            {
                case StatCompareCondition compare:
                    var definition = session.Stats.Find(compare.stat);
                    if (definition == null)
                    {
                        problems.Add($"없는 수치 '{compare.stat}'");
                        return;
                    }
                    var current = session.Stats.Get(compare.stat);
                    if (double.IsNaN(current)) current = 0;
                    if (compare.op != CompareOp.AtLeast && compare.op != CompareOp.GreaterThan)
                    {
                        problems.Add($"'{condition.Describe(session)}'은 값을 늘려서 채울 수 없는 비교");
                        return;
                    }
                    var target = compare.op == CompareOp.GreaterThan ? compare.value + 1 : compare.value;
                    steps.Add(definition.Aggregate == StatAggregate.Sum ? (compare.stat, target - current) : (compare.stat, target));
                    return;

                case AchievementUnlockedCondition unlocked:
                    if (!visiting.Add(unlocked.achievement))
                    {
                        problems.Add("도전과제 순환 참조");
                        return;
                    }
                    var other = session.Achievements.Find(unlocked.achievement);
                    if (other?.When == null) problems.Add($"'{session.AchievementName(unlocked.achievement)}'의 트리거가 없음");
                    else PlanTrigger(session, other.When, steps, new List<object>(), problems, visiting);
                    return;

                case AllOfCondition all:
                    foreach (var child in all.Children) Collect(session, child, steps, problems, visiting);
                    return;

                case AnyOfCondition any:
                    var first = any.Children.FirstOrDefault();
                    if (first != null) Collect(session, first, steps, problems, visiting);
                    return;

                default:
                    problems.Add($"'{condition.Describe(session)}'은 자동으로 채울 수 없는 조건");
                    return;
            }
        }
    }
}
