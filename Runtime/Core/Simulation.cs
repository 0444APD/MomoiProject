using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace GBible
{
    // 개발 도구(모니터 등)에서 값을 고치는 일을 행동 하나로 실행한다: Undo·기록이 게임 행동과 같다.
    // 고치는 일은 코드(대리자)라 네트워크·저장으로 보내지 않는다(개발용).
    [Serializable, SystemAction]
    public sealed class DeveloperEditAction : GameAction
    {
        public string label;
        [NonSerialized] public Action Edit;

        public override ActionVisibility Visibility => ActionVisibility.Developer;
        public override string Describe() => "[개발] " + (string.IsNullOrEmpty(label) ? "값 고침" : label);

        protected override void Run() => Edit?.Invoke();
    }

    // 테스트용 이벤트 받기. 값을 덮어쓰지 않고 게임과 똑같은 경로(행동 → 이벤트 → 수치 규칙 → 확정 → 도전과제 → 보상)를 탄다.
    // 행동으로 기록되므로 Undo·인과 추적이 그대로 적용된다.
    [Serializable, SystemAction]
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
            foreach (var pair in values) SetMember(instance, pair.Key, pair.Value);
            return instance;
        }

        static void SetMember(object instance, string name, object value)
        {
            var type = instance.GetType();
            var member = (MemberInfo)type.GetField(name, BindingFlags.Public | BindingFlags.Instance)
                         ?? type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (member == null) throw new ArgumentException($"[MomoiProject] '{type.Name}'에 '{name}' 멤버가 없습니다.");
            var converted = Convert(value, MemberType(member), name);
            if (member is FieldInfo field) field.SetValue(instance, converted);
            else ((PropertyInfo)member).SetValue(instance, converted);
        }

        // 반영 조건(이벤트 조건)에 맞게 값을 채운 새 이벤트. 맞추지 못한 값은 그대로다(부르는 쪽이 조건으로 다시 본다).
        static object Matching(Type type, ValueFilter filter)
        {
            var evt = CreateDefault(type);
            if (filter != null) Satisfy(evt, filter);
            return evt;
        }

        static bool IsFlag(Type type, string name)
        {
            var member = (MemberInfo)type.GetField(name, BindingFlags.Public | BindingFlags.Instance) ?? type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            return member != null && MemberType(member) == typeof(bool);
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
        // 수치의 반영 규칙을 거꾸로 따라가 필요한 이벤트를 만든다(규칙의 조건이 있으면 그 조건에 맞춘 이벤트). 같은 이벤트를 받는 다른 수치도 게임에서처럼 함께 움직인다.
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
                    // 참/거짓 값을 더하는 규칙은 참 하나가 1이라 횟수 규칙처럼 센다(3을 더하려면 참인 이벤트 3번).
                    var flag = definition.Aggregate == StatAggregate.Sum && !string.IsNullOrEmpty(rule.field) && IsFlag(type, rule.field);
                    if (string.IsNullOrEmpty(rule.field) || flag)
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
                            reasons.Add($"{name}: {(flag ? $"참/거짓 값({rule.field})" : "횟수 규칙")}이라 1~{MaxRepeat} 사이의 정수 배만 가능");
                            continue;
                        }
                        object Counted()
                        {
                            var made = Matching(type, rule.filter);
                            if (flag) SetMember(made, rule.field, true);
                            return made;
                        }
                        var first = Counted();
                        if (rule.filter != null && !rule.filter.Test(first))
                        {
                            reasons.Add($"{name}: 반영 조건 '{rule.filter.Natural()}'에 맞는 이벤트를 자동으로 만들 수 없음");
                            continue;
                        }
                        plan = new EventPlan { Description = $"{name} ×{count}" + (flag ? $" ({rule.field} = 참)" : "") };
                        plan.Events.Add(first);
                        for (int i = 1; i < count; i++) plan.Events.Add(Counted());
                        return true;
                    }

                    if (rule.field.Contains('.'))
                    {
                        reasons.Add($"{name}: 중첩 필드({rule.field})는 자동 생성하지 않음");
                        continue;
                    }
                    if (IsFlag(type, rule.field) && Math.Abs(raw) > 1e-9 && Math.Abs(raw - 1) > 1e-9)
                    {
                        reasons.Add($"{name}: 참/거짓 값({rule.field})은 0이나 1만 기록할 수 있음");
                        continue;
                    }
                    var evt = Matching(type, rule.filter);
                    SetMember(evt, rule.field, raw);
                    if (rule.filter != null && !rule.filter.Test(evt))
                    {
                        reasons.Add($"{name}: 반영 조건 '{rule.filter.Natural()}'에 맞는 이벤트를 자동으로 만들 수 없음({rule.field} = {raw.ToString("0.##", CultureInfo.InvariantCulture)})");
                        continue;
                    }
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
        //   다른 도전과제 달성 → 그 트리거도 같이 채운다(두 갈래가 같은 도전과제를 보면 한 번만) /
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
            PlanTrigger(session, definition.When, steps, events, problems, new HashSet<string> { achievementId }, new HashSet<string> { achievementId });
            // 같은 로그를 여러 조건이 보면 가장 큰 요구치 하나로 합친다.
            steps = steps.GroupBy(s => s.stat).Select(g => (g.Key, g.Max(s => s.amount))).ToList();
            error = problems.Count > 0 ? string.Join(" / ", problems) : null;
            return problems.Count == 0 && (steps.Count > 0 || events.Count > 0);
        }

        // 예전 형태(조건만): 로그 차이만.
        public static bool TryPlanForAchievement(GameSession session, string achievementId, out List<(string stat, double amount)> steps, out string error) =>
            TryPlanForAchievement(session, achievementId, out steps, out _, out error);

        // planned: 이 계획에 넣은 도전과제(다시 만나면 건너뛴다). path: 지금 따라 들어온 도전과제(다시 만나면 순환).
        static void PlanTrigger(GameSession session, Trigger trigger, List<(string stat, double amount)> steps, List<object> events, List<string> problems, HashSet<string> planned, HashSet<string> path)
        {
            switch (trigger)
            {
                case ConditionTrigger single:
                    if (single.condition != null) Collect(session, single.condition, steps, events, problems, planned, path);
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
                        try { Satisfy(evt, fired.filter); }
                        catch (Exception) { }   // 맞추지 못한 값(형식 범위 밖 등)은 아래 검사가 알린다
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
                    foreach (var child in all.Children) PlanTrigger(session, child, steps, events, problems, planned, path);
                    return;
                case AnyTrigger any:
                    // 계획이 서는 첫 갈래.
                    foreach (var child in any.Children)
                    {
                        var s = new List<(string, double)>();
                        var e = new List<object>();
                        var p = new List<string>();
                        var branch = new HashSet<string>(planned);
                        PlanTrigger(session, child, s, e, p, branch, path);
                        if (p.Count > 0) continue;
                        steps.AddRange(s);
                        events.AddRange(e);
                        planned.UnionWith(branch);
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
                            else if (FieldPaths.ElementTypeOf(type) is Type element && PlayerDataGrants.TryParse(element, field.value, out var item, out _))
                            {
                                var current = GetValue(member, evt);
                                if (type.IsArray)
                                {
                                    // 배열은 크기가 정해져 있어 늘린 새 배열로 바꾼다.
                                    var old = current as Array;
                                    var grown = Array.CreateInstance(element, (old?.Length ?? 0) + 1);
                                    old?.CopyTo(grown, 0);
                                    grown.SetValue(item, grown.Length - 1);
                                    value = grown;
                                }
                                else if ((current ?? DataModel.Create(type)) is System.Collections.IList list)
                                {
                                    list.Add(item);
                                    value = list;
                                }
                            }
                            break;
                    }
                    if (value == null) return;
                    if (member is FieldInfo f) f.SetValue(evt, value);
                    else if (member is PropertyInfo p && p.CanWrite) p.SetValue(evt, value);
                    return;
            }
        }

        static void Collect(GameSession session, Condition condition, List<(string stat, double amount)> steps, List<object> events, List<string> problems, HashSet<string> planned, HashSet<string> path)
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
                    var recorded = session.Stats.Get(compare.stat);
                    var current = double.IsNaN(recorded) ? 0 : recorded;
                    if (compare.op != CompareOp.AtLeast && compare.op != CompareOp.GreaterThan)
                    {
                        problems.Add($"'{condition.Describe(session)}'은 값을 늘려서 채울 수 없는 비교");
                        return;
                    }
                    // 최저치는 첫 기록만 올릴 수 있다(이미 기록이 있으면 더 큰 값을 기록해도 그대로다).
                    if (definition.Aggregate == StatAggregate.Min && !double.IsNaN(recorded))
                    {
                        problems.Add($"'{condition.Describe(session)}': 최저치 로그는 이미 기록된 값보다 크게 올릴 수 없음");
                        return;
                    }
                    var target = compare.op == CompareOp.GreaterThan ? compare.value + 1 : compare.value;
                    steps.Add(definition.Aggregate == StatAggregate.Sum ? (compare.stat, target - current) : (compare.stat, target));
                    return;

                case AchievementUnlockedCondition unlocked:
                    if (path.Contains(unlocked.achievement))
                    {
                        problems.Add("도전과제 순환 참조");
                        return;
                    }
                    // 다른 갈래가 이미 이 도전과제를 채운다.
                    if (!planned.Add(unlocked.achievement)) return;
                    var other = session.Achievements.Find(unlocked.achievement);
                    if (other?.When == null)
                    {
                        problems.Add($"'{session.AchievementName(unlocked.achievement)}'의 트리거가 없음");
                        return;
                    }
                    // 그 도전과제의 트리거 이벤트도 쏜다(달성은 행동이 끝난 뒤 — 이 조건은 그때 다시 평가된다).
                    path.Add(unlocked.achievement);
                    PlanTrigger(session, other.When, steps, events, problems, planned, path);
                    path.Remove(unlocked.achievement);
                    return;

                case AllOfCondition all:
                    foreach (var child in all.Children) Collect(session, child, steps, events, problems, planned, path);
                    return;

                case AnyOfCondition any:
                    // 계획이 서는 첫 갈래.
                    foreach (var child in any.Children)
                    {
                        var s = new List<(string, double)>();
                        var e = new List<object>();
                        var p = new List<string>();
                        var branch = new HashSet<string>(planned);
                        Collect(session, child, s, e, p, branch, path);
                        if (p.Count > 0) continue;
                        steps.AddRange(s);
                        events.AddRange(e);
                        planned.UnionWith(branch);
                        return;
                    }
                    problems.Add("OR의 어느 갈래도 자동으로 채울 수 없음");
                    return;

                default:
                    problems.Add($"'{condition.Describe(session)}'은 자동으로 채울 수 없는 조건");
                    return;
            }
        }
    }
}
