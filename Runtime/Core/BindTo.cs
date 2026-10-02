using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace GBible
{
    // 코드로 쓴 결과를 도감 항목(도전과제·로그 …)에 연결한다. 연결된 항목의 결과 칸은 에디터에서 고를 수 없고(보기·cs 열기만),
    // 이 클래스가 그 항목의 결과가 된다. 예외적인 로직은 그 단위만 코드로 쓰고, 어디에 쓸지는 코드에서 정한다.
    //   [BindTo(GameIds.Achievement.FirstBlood)]
    //   public class FirstBloodReward : Reward
    //   {
    //       protected override void Grant() { … }      // Context.Owner = 연결된 항목 ID, Context.Event = 트리거의 이벤트
    //   }
    // 로그에 연결하면 그 로그의 모든 규칙에서 '로그에 기록' 대신 이 결과가 돈다(기록은 Game.Session.Stats.Add(Context.Owner, 값)).
    // 한 클래스를 여러 항목에 연결할 수 있다([BindTo]를 여러 개).
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class BindToAttribute : Attribute
    {
        public string Id { get; }

        public BindToAttribute(string id)
        {
            Id = id;
        }
    }

    // [BindTo]로 연결된 결과 클래스 목록(처음 쓸 때 한 번 모은다 — 리플렉션은 여기서만).
    public static class BoundOutcomes
    {
        static Dictionary<string, List<Type>> _byId;
        static readonly object Gate = new object();

        public static IReadOnlyList<Type> For(string id)
        {
            Load();
            return id != null && _byId.TryGetValue(id, out var types) ? types : (IReadOnlyList<Type>)Array.Empty<Type>();
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

        // 연결된 결과의 새 인스턴스들(정의마다 따로).
        public static List<Outcome> Create(string id) => For(id).Select(t => (Outcome)Activator.CreateInstance(t)).ToList();

        public static void Reset()
        {
            lock (Gate) _byId = null;
        }

        static void Load()
        {
            if (_byId != null) return;
            lock (Gate)
            {
                if (_byId != null) return;
                var map = new Dictionary<string, List<Type>>(StringComparer.Ordinal);
                const string core = "GBible.Core";
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly.GetName().Name != core && !assembly.GetReferencedAssemblies().Any(r => r.Name == core)) continue;
                    Type[] types;
                    try { types = assembly.GetTypes(); }
                    catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                    foreach (var type in types)
                    {
                        if (type.IsAbstract || !typeof(Outcome).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) == null) continue;
                        foreach (var bind in type.GetCustomAttributes<BindToAttribute>(false))
                        {
                            if (string.IsNullOrEmpty(bind.Id)) continue;
                            if (!map.TryGetValue(bind.Id, out var list)) map[bind.Id] = list = new List<Type>();
                            list.Add(type);
                        }
                    }
                }
                _byId = map;
            }
        }
    }
}
