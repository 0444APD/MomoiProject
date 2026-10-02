using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace GBible
{
    // 게임 코드가 쓰는 창구. 세션마다 하나(session.Game). 행동·효과·조건 Block 안에서는 Game 속성으로 바로 쓴다.
    //   Game.All<Unit>()        플레이어 데이터 안의 Unit 전부(리스트·클래스 안쪽까지)
    //   Game.Find<Unit>(id)     id 필드가 같은 Unit
    //   Game.Random("damage")   결정론적 난수
    //   Game.Do(new MoveUnit{…}) 행동 실행(Undo 한 단위)   Game.Use("ability.fireball", 사용자, 대상들)
    // 행동·효과 안에서 꺼낸 객체는 필드를 그냥 고치면 된다(시스템이 바뀐 곳을 찾아 기록한다: Undo·Journal·저장).
    // 오래 들고 있을 것은 객체가 아니라 id로(Undo·불러오기 뒤에는 다른 인스턴스일 수 있다).
    public sealed class GameApi
    {
        public GameSession Session { get; }

        public GameApi(GameSession session)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public EventBus Events => Session.Events;

        public RandomStream Random(string name) => Session.Random.Get(name);

        public ActionRecord Do(GameAction action) => Session.Do(action);

        // 능력(도감 › 능력)을 쓴다. 사용자·대상은 id 필드가 있는 객체(또는 id 문자열).
        public ActionRecord Use(string ability, object user, params object[] targets) =>
            Session.Do(new UseAbility
            {
                ability = ability,
                user = user as string ?? IdOf(user),
                targets = (targets ?? Array.Empty<object>()).Select(t => t as string ?? IdOf(t)).ToList(),
            });

        // 지금 쓸 수 있는지(능력·사용 아이템의 트리거 = 사용 조건만 본다. 아무것도 바꾸지 않는다). 버튼을 미리 흐리게 할 때.
        //   if (!Game.CanUse("ability.heal", hero, out var why)) button.tooltip = why;
        public bool CanUse(string ability, object user, params object[] targets) => CanUse(ability, user, targets, out _);

        public bool CanUse(string ability, object user, out string reason) => CanUse(ability, user, Array.Empty<object>(), out reason);

        public bool CanUse(string ability, object user, object[] targets, out string reason)
        {
            reason = null;
            var definition = Session.Abilities.Get(ability);
            if (definition == null)
            {
                reason = $"'{ability}'은(는) 쓸 수 있는 능력·아이템이 아닙니다.";
                return false;
            }
            var who = user is string userId ? Find(userId) : user;
            var picked = (targets ?? Array.Empty<object>()).Select(t => t is string id ? Find(id) : t).Where(t => t != null).ToList();
            if (Session.Triggers.CanUse(ability, definition.When, who, picked, out _)) return true;
            reason = "사용 조건을 만족하지 않습니다: " + definition.When.Natural(Session);
            return false;
        }

        // ── 찾기 ───────────────────────────────────────────────

        // 플레이어 데이터 전체에서 T를 모은다(키 이름순 → 들어 있는 순서. 순서가 늘 같다).
        public List<T> All<T>() where T : class
        {
            var result = new List<T>();
            foreach (var key in DataKeys())
                if (CanContain(Session.Data.TypeOf(key), typeof(T)))
                    Collect(Session.Data.Get(key), result, 0);
            return result;
        }

        // 플레이어 데이터 하나(key) 안의 T만.
        public List<T> All<T>(string key) where T : class
        {
            var result = new List<T>();
            Collect(Session.Data.Get(key), result, 0);
            return result;
        }

        public T Find<T>(string id) where T : class
        {
            if (string.IsNullOrEmpty(id)) return null;
            RequireId(typeof(T));
            return All<T>().FirstOrDefault(item => IdOf(item) == id);
        }

        // 형식을 모를 때: id 필드가 있는 모든 객체에서 찾는다.
        public object Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var all = new List<object>();
            foreach (var key in DataKeys()) Collect(Session.Data.Get(key), all, 0);
            return all.FirstOrDefault(item => IdMember(item.GetType()) != null && IdOf(item) == id);
        }

        // 단순 값(숫자·글자 등) 플레이어 데이터.
        public T Get<T>(string key) => Session.Data.Get<T>(key);
        public void Set(string key, object value) => Session.Data.Set(key, value);

        // 새 개체의 id(소환 등). 접두어별 번호를 플레이어 데이터 저장소에 두므로 Undo·저장·멀티플레이에서도 같다.
        public string NewId(string prefix)
        {
            var key = "$id." + prefix;
            if (!Session.Data.IsDefined(key)) Session.Data.Define(key, DataZone.Meta, 0.0, $"{prefix} 번호");
            var next = Session.Data.GetNumber(key) + 1;
            Session.Data.Set(key, next);
            return prefix + (long)next;
        }

        IEnumerable<string> DataKeys() =>
            Session.Data.Keys.Where(Session.Data.IsData).OrderBy(k => k, StringComparer.Ordinal).ToList();

        // ── id ────────────────────────────────────────────────
        // 개체로 찾으려면 클래스에 string id 필드(또는 속성)가 있어야 한다.

        static readonly Dictionary<Type, MemberInfo> IdMembers = new Dictionary<Type, MemberInfo>();

        static MemberInfo IdMember(Type type)
        {
            lock (IdMembers)
            {
                if (IdMembers.TryGetValue(type, out var member)) return member;
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                member = (MemberInfo)type.GetField("id", flags) ?? type.GetField("Id", flags) ?? (MemberInfo)type.GetProperty("Id", flags) ?? type.GetProperty("id", flags);
                if (member is FieldInfo f && f.FieldType != typeof(string)) member = null;
                if (member is PropertyInfo p && p.PropertyType != typeof(string)) member = null;
                IdMembers[type] = member;
                return member;
            }
        }

        public static string IdOf(object item)
        {
            if (item == null) return null;
            switch (IdMember(item.GetType()))
            {
                case FieldInfo field: return field.GetValue(item) as string;
                case PropertyInfo property: return property.GetValue(item) as string;
            }
            throw new InvalidOperationException($"[MomoiProject] {item.GetType().Name}에 string id 필드가 없어 개체로 찾거나 가리킬 수 없습니다. 'public string id;'를 더하세요.");
        }

        static void RequireId(Type type)
        {
            if (!type.IsAbstract && !type.IsInterface && IdMember(type) == null)
                throw new InvalidOperationException($"[MomoiProject] {type.Name}에 string id 필드가 없어 Find로 찾을 수 없습니다. 'public string id;'를 더하세요.");
        }

        // ── 모으기 ─────────────────────────────────────────────

        static readonly Dictionary<(Type, Type), bool> ContainCache = new Dictionary<(Type, Type), bool>();

        // holder 형식의 값 안에 target이 들어 있을 수 있는지(형식만 보고 판단, 결과는 기억).
        static bool CanContain(Type holder, Type target)
        {
            if (holder == null) return false;
            lock (ContainCache)
            {
                if (ContainCache.TryGetValue((holder, target), out var known)) return known;
                var result = CanContain(holder, target, new HashSet<Type>());
                ContainCache[(holder, target)] = result;
                return result;
            }
        }

        static bool CanContain(Type holder, Type target, HashSet<Type> seen)
        {
            if (holder == null || !seen.Add(holder)) return false;
            if (target.IsAssignableFrom(holder) || holder.IsAssignableFrom(target)) return true;
            var info = DataModel.Of(holder);
            switch (info.Kind)
            {
                case DataKind.List:
                case DataKind.Array:
                case DataKind.Collection:
                case DataKind.Dictionary:
                    return CanContain(info.ElementType, target, seen);
                case DataKind.Object:
                    return info.Fields.Any(f => CanContain(f.Type, target, seen));
            }
            return false;
        }

        static void Collect<T>(object value, List<T> into, int depth) where T : class
        {
            if (value == null || depth > 24 || value is string) return;
            if (value is T found) into.Add(found);
            var info = DataModel.Of(value.GetType());
            switch (info.Kind)
            {
                case DataKind.List:
                case DataKind.Array:
                case DataKind.Collection:
                case DataKind.Dictionary:
                    foreach (var item in FieldPaths.Items(value)) Collect(item, into, depth + 1);
                    return;
                case DataKind.Object:
                    foreach (var field in info.Fields) Collect(field.Field.GetValue(value), into, depth + 1);
                    return;
            }
        }
    }

    // ── 행동: 처리를 클래스 안에 쓴다(등록 없이 Game.Do로 실행) ──────────

    public interface ISelfRunningAction
    {
        void Run(GameApi game);
    }

    // 대상 하나에 대한 행동. 대상은 id로 저장되고(데이터라 서버 전송·리플레이 가능), Run에서는 객체로 받는다.
    //   [Serializable] public class MoveUnit : GameAction<Unit> { public int x, y; protected override void Run(Unit u) { u.x = x; u.y = y; } }
    //   Game.Do(new MoveUnit { target = unit.id, x = 3, y = 4 });
    [Serializable]
    public abstract class GameAction<T> : GameAction, ISelfRunningAction where T : class
    {
        public string target;

        [NonSerialized] GameApi _game;
        protected GameApi Game => _game;

        protected abstract void Run(T target);

        void ISelfRunningAction.Run(GameApi game)
        {
            _game = game;
            try
            {
                var found = game.Find<T>(target) ?? throw new InvalidOperationException($"[MomoiProject] {typeof(T).Name} '{target}'을(를) 찾을 수 없습니다.");
                Run(found);
            }
            finally
            {
                _game = null;
            }
        }

        public override string Describe() => $"{BlockNames.Of(GetType())} ({target})";
    }

    // 대상 없는 행동(턴 넘기기 등).
    [Serializable]
    public abstract class SimpleAction : GameAction, ISelfRunningAction
    {
        [NonSerialized] GameApi _game;
        protected GameApi Game => _game;

        protected abstract void Run();

        void ISelfRunningAction.Run(GameApi game)
        {
            _game = game;
            try { Run(); }
            finally { _game = null; }
        }

        public override string Describe() => BlockNames.Of(GetType());
    }

    // ── 조건: Check만 쓴다. 무엇을 읽는지는 시스템이 기록한다(CollectDependencies 불필요) ──
    [Serializable]
    public abstract class SimpleCondition : Condition
    {
        [NonSerialized] GameApi _game;
        protected GameApi Game => _game;
        // 트리거 안에서 검사 중이면 그 순간(이벤트·사용자·대상). 아니면 null. 예: Context?.User as Unit
        protected TriggerContext Context => _game?.Session.Triggers.Current;

        protected abstract bool Check();

        public override bool Evaluate(GameSession session)
        {
            _game = session.Game;
            try { return Check(); }
            finally { _game = null; }
        }

        public override void CollectDependencies(ICollection<string> stateKeys)
        {
        }

        public override bool AutoDependencies => true;

        public override string Describe(INameResolver names) => BlockNames.Of(GetType());
    }

    static class BlockNames
    {
        public static string Of(Type type) => type.GetCustomAttribute<BlockInfoAttribute>()?.DisplayName ?? type.Name;
    }
}
