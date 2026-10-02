using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace GBible
{
    // 코드로 쓴 결과를 도감 항목(도전과제·로그·사용 기능이 붙은 인스턴스 …)에 연결한다. 연결된 항목의 결과 칸은 에디터에서
    // 고를 수 없고(보기·cs 열기만), 이 코드가 그 항목의 결과가 된다. 예외적인 로직은 그 단위만 코드로 쓰고, 어디에 쓸지는 코드에서 정한다.
    //
    // 함수 하나(static, 반환 void). 매개변수로 받고 싶은 것만 적는다:
    //   [BindTo(GameIds.Achievement.FirstBlood)]
    //   static void FirstBlood(UnitKilled e, TriggerContext c) { c.Game.Find<Unit>(e.killerId).gold += 100; }
    //   매개변수 = 없음 / (이벤트) / (TriggerContext) / (이벤트, TriggerContext).
    //   이벤트 매개변수의 형식이 받을 이벤트 형식이다: 트리거가 받은 이벤트가 그 형식일 때만 불린다(아니면 경고를 남기고 건너뜀).
    //   TriggerContext: Owner = 연결된 항목 ID, Event = 받은 이벤트, User·Picked = 사용한 순간의 사용자·대상, Game = GameApi.
    //
    // 클래스(Reward를 상속)에 붙여도 된다(필드에 설정값을 두고 싶을 때):
    //   [BindTo("first_blood")] public class FirstBloodReward : Reward { public override void Grant(GameSession s) { … Context … } }
    //
    // 로그에 연결하면 그 로그의 모든 규칙에서 '로그에 기록' 대신 이 결과가 돈다(기록은 c.Session.Stats.Report(c.Owner, 값)).
    // 한 코드를 여러 항목에 연결할 수 있다([BindTo]를 여러 개).
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public sealed class BindToAttribute : Attribute
    {
        public string Id { get; }

        public BindToAttribute(string id)
        {
            Id = id;
        }
    }

    // [BindTo]로 연결된 코드 하나(클래스 또는 함수).
    public sealed class BoundCode
    {
        // 클래스면 그 클래스, 함수면 함수가 들어 있는 클래스(cs 열기).
        public Type Type { get; internal set; }
        // 함수면 그 함수(클래스면 null).
        public MethodInfo Method { get; internal set; }
        // 함수가 받는 이벤트 형식(받지 않으면 null).
        public Type EventType { get; internal set; }
        internal Action<TriggerContext> Call;

        public string Name => Method == null ? Type.Name : $"{Type.Name}.{Method.Name}";

        public Outcome Create() => Method == null ? (Outcome)Activator.CreateInstance(Type) : new BoundMethodOutcome(this);
    }

    // 함수로 연결된 결과(게임이 켜질 때 만든 대리자를 부른다 — 실행 중 리플렉션 없음).
    public sealed class BoundMethodOutcome : Outcome
    {
        public BoundCode Code { get; }

        public BoundMethodOutcome(BoundCode code)
        {
            Code = code;
        }

        public override OutcomeKinds Kind => OutcomeKinds.Coded;
        public override void Run(TriggerContext context) => Code.Call(context);
        public override string Describe(INameResolver names) => "코드: " + Code.Name;
    }

    // [BindTo]로 연결된 코드 목록(처음 쓸 때 한 번 모은다 — 리플렉션은 여기서만).
    public static class BoundOutcomes
    {
        static Dictionary<string, List<BoundCode>> _byId;
        static List<string> _problems;
        static readonly object Gate = new object();

        public static IReadOnlyList<BoundCode> For(string id)
        {
            Load();
            return id != null && _byId.TryGetValue(id, out var codes) ? codes : (IReadOnlyList<BoundCode>)Array.Empty<BoundCode>();
        }

        public static bool Has(string id) => For(id).Count > 0;

        public static IEnumerable<string> Ids
        {
            get
            {
                Load();
                return _byId.Keys;
            }
        }

        // 연결하지 못한 코드(형식이 맞지 않는 함수 등)의 설명.
        public static IReadOnlyList<string> Problems
        {
            get
            {
                Load();
                return _problems;
            }
        }

        // 연결된 결과의 새 인스턴스들(정의마다 따로).
        public static List<Outcome> Create(string id) => For(id).Select(c => c.Create()).ToList();

        public static void Reset()
        {
            lock (Gate)
            {
                _byId = null;
                _problems = null;
            }
        }

        static void Load()
        {
            if (_byId != null) return;
            lock (Gate)
            {
                if (_byId != null) return;
                var map = new Dictionary<string, List<BoundCode>>(StringComparer.Ordinal);
                var problems = new List<string>();
                void Add(string id, BoundCode code)
                {
                    if (string.IsNullOrEmpty(id)) return;
                    if (!map.TryGetValue(id, out var list)) map[id] = list = new List<BoundCode>();
                    list.Add(code);
                }
                const string core = "GBible.Core";
                const BindingFlags methods = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly.GetName().Name != core && !assembly.GetReferencedAssemblies().Any(r => r.Name == core)) continue;
                    Type[] types;
                    try { types = assembly.GetTypes(); }
                    catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                    foreach (var type in types)
                    {
                        var binds = type.GetCustomAttributes<BindToAttribute>(false).ToList();
                        if (binds.Count > 0)
                        {
                            if (type.IsAbstract || !typeof(Outcome).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) == null)
                                problems.Add($"[BindTo] '{type.FullName}': 클래스에 붙일 때는 Reward(Outcome)를 상속하고 매개변수 없는 생성자가 있어야 합니다.");
                            else
                                foreach (var bind in binds) Add(bind.Id, new BoundCode { Type = type });
                        }
                        foreach (var method in type.GetMethods(methods))
                        {
                            var methodBinds = method.GetCustomAttributes<BindToAttribute>(false).ToList();
                            if (methodBinds.Count == 0) continue;
                            var code = MakeCall(type, method, out var problem);
                            if (code == null)
                            {
                                problems.Add($"[BindTo] '{type.FullName}.{method.Name}': {problem}");
                                continue;
                            }
                            foreach (var bind in methodBinds) Add(bind.Id, code);
                        }
                    }
                }
                _problems = problems;
                _byId = map;
            }
        }

        // 함수를 TriggerContext 하나를 받는 대리자로 감싼다(게임이 켜질 때 한 번).
        static BoundCode MakeCall(Type type, MethodInfo method, out string problem)
        {
            problem = null;
            if (!method.IsStatic)
            {
                problem = "static 함수여야 합니다.";
                return null;
            }
            if (method.ReturnType != typeof(void) || method.IsGenericMethodDefinition)
            {
                problem = "반환이 void이고 제네릭이 아닌 함수여야 합니다.";
                return null;
            }
            var parameters = method.GetParameters();
            Type eventType = null;
            var withContext = false;
            for (var i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i].ParameterType;
                if (p == typeof(TriggerContext) && i == parameters.Length - 1 && !withContext) withContext = true;
                else if (i == 0 && eventType == null && !p.IsByRef && !p.IsPointer && p != typeof(TriggerContext)) eventType = p;
                else
                {
                    problem = "매개변수는 없음 / (이벤트) / (TriggerContext) / (이벤트, TriggerContext) 중 하나여야 합니다.";
                    return null;
                }
            }
            var code = new BoundCode { Type = type, Method = method, EventType = eventType };
            if (eventType == null)
            {
                if (withContext)
                {
                    code.Call = (Action<TriggerContext>)method.CreateDelegate(typeof(Action<TriggerContext>));
                }
                else
                {
                    var call = (Action)method.CreateDelegate(typeof(Action));
                    code.Call = _ => call();
                }
            }
            else
            {
                var maker = typeof(EventCall<>).MakeGenericType(eventType).GetMethod(nameof(EventCall<object>.Make), BindingFlags.Static | BindingFlags.Public);
                code.Call = (Action<TriggerContext>)maker.Invoke(null, new object[] { code, withContext });
            }
            return code;
        }

        // 이벤트 형식별 감싸기: 받은 이벤트가 그 형식일 때만 부른다(형 변환만, 리플렉션 없음).
        static class EventCall<TEvent>
        {
            public static Action<TriggerContext> Make(BoundCode code, bool withContext)
            {
                if (withContext)
                {
                    var call = (Action<TEvent, TriggerContext>)code.Method.CreateDelegate(typeof(Action<TEvent, TriggerContext>));
                    return c =>
                    {
                        if (c.Event is TEvent e) call(e, c);
                        else Mismatch(code, c);
                    };
                }
                var only = (Action<TEvent>)code.Method.CreateDelegate(typeof(Action<TEvent>));
                return c =>
                {
                    if (c.Event is TEvent e) only(e);
                    else Mismatch(code, c);
                };
            }
        }

        static void Mismatch(BoundCode code, TriggerContext context)
        {
            var got = context.Event == null ? (context.IsUse ? "사용(이벤트 없음)" : "이벤트 없음") : context.Event.GetType().Name;
            context.Session?.Journal.Append(JournalKind.Warning, "Outcomes",
                $"[BindTo] {code.Name}은(는) {code.EventType.Name} 이벤트를 받는데 '{context.Owner}'의 트리거는 {got}입니다. 실행하지 않았습니다.");
        }
    }
}
