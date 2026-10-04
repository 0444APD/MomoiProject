using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace GBible
{
    // 이벤트 타입(class·struct)에 붙인다. DB 창의 이벤트 선택 목록에 표시된다.
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
    public sealed class GameEventAttribute : Attribute
    {
        public string DisplayName { get; }

        public GameEventAttribute(string displayName = null)
        {
            DisplayName = displayName;
        }
    }

    // 이벤트가 스스로 기록 문장을 정하고 싶을 때 구현한다.
    public interface IDescribable
    {
        string Describe();
    }

    // 알림 전용 이벤트 버스. 구독자는 이벤트를 읽기만 한다(값 수정 파이프라인은 별도 Modifiers 층의 몫).
    public sealed class EventBus
    {
        // 구독자는 타입을 모르는 호출(PublishObject)에서도 부를 수 있게 object 래퍼로 보관한다.
        sealed class Handler
        {
            public Action<object> Invoke;
            public Delegate Original;
        }

        readonly Dictionary<Type, List<Handler>> _handlers = new Dictionary<Type, List<Handler>>();
        readonly List<Action<object>> _listeners = new List<Action<object>>();
        // 행동이 성공한 뒤에 받는 쪽(연출): 행동 중에 쏜 이벤트는 모았다가 바깥 행동이 끝나면 차례로, 실패하면 버린다.
        readonly Dictionary<Type, List<Handler>> _afterAction = new Dictionary<Type, List<Handler>>();
        readonly List<(Handler handler, object evt)> _pending = new List<(Handler, object)>();
        // 지금 행동 중인가(세션이 넣는다). 없으면 늘 바로 준다.
        internal Func<bool> InAction;
        readonly Journal _journal;

        public event Action<object> Published;

        public EventBus(Journal journal)
        {
            _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        }

        public IDisposable Subscribe<T>(Action<T> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (!_handlers.TryGetValue(typeof(T), out var list))
                _handlers[typeof(T)] = list = new List<Handler>();
            var entry = new Handler { Invoke = o => handler((T)o), Original = handler };
            list.Add(entry);
            return new Subscription(() => list.Remove(entry));
        }

        // 행동이 성공한 뒤에만 받는다(화면 연출용). 행동 중에 쏜 이벤트는 바깥 행동이 성공하면 쏜 순서대로 오고, 실패하면 오지 않는다.
        // 행동 밖에서 쏜 이벤트는 바로 온다. 규칙(값을 바꾸는 쪽)은 행동 안에서 바로 받아야 하므로 Subscribe를 쓴다.
        public IDisposable SubscribeAfterAction<T>(Action<T> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (!_afterAction.TryGetValue(typeof(T), out var list))
                _afterAction[typeof(T)] = list = new List<Handler>();
            var entry = new Handler { Invoke = o => handler((T)o), Original = handler };
            list.Add(entry);
            return new Subscription(() =>
            {
                list.Remove(entry);
                _pending.RemoveAll(p => p.handler == entry);
            });
        }

        // 바깥 행동이 성공했을 때(세션이 부른다): 모아 둔 이벤트를 쏜 순서대로 준다.
        internal void FlushAfterAction()
        {
            if (_pending.Count == 0) return;
            var ready = _pending.ToArray();
            _pending.Clear();
            foreach (var (handler, evt) in ready) Invoke(handler.Invoke, evt, handler.Original);
        }

        // 행동이 실패해 취소됐을 때: 모아 둔 이벤트를 버린다.
        internal void DropAfterAction() => _pending.Clear();

        // 타입 무관하게 모든 이벤트를 받는다(수치 규칙 등).
        public IDisposable SubscribeAll(Action<object> listener)
        {
            if (listener == null) throw new ArgumentNullException(nameof(listener));
            _listeners.Add(listener);
            return new Subscription(() => _listeners.Remove(listener));
        }

        public void Publish<T>(T evt) => PublishObject(evt);

        // 런타임에만 타입을 아는 이벤트(모니터의 테스트 이벤트 등)도 게임과 똑같은 경로로 발행한다.
        public void PublishObject(object evt)
        {
            if (evt == null) throw new ArgumentNullException(nameof(evt));
            var entry = _journal.Append(JournalKind.Event, "Events", Describe(evt));
            entry.Payload = evt;

            using (_journal.Cause(entry.Id))
            {
                if (_handlers.TryGetValue(evt.GetType(), out var list))
                {
                    foreach (var handler in list.ToArray())
                        Invoke(handler.Invoke, evt, handler.Original);
                }
                foreach (var listener in _listeners.ToArray())
                    Invoke(listener, evt, listener);
                if (_afterAction.TryGetValue(evt.GetType(), out var after) && after.Count > 0)
                {
                    var inAction = InAction?.Invoke() ?? false;
                    foreach (var handler in after.ToArray())
                    {
                        if (inAction) _pending.Add((handler, evt));
                        else Invoke(handler.Invoke, evt, handler.Original);
                    }
                }
            }
            Published?.Invoke(evt);
        }

        public int SubscriberCount(Type eventType) => _handlers.TryGetValue(eventType, out var list) ? list.Count : 0;

        void Invoke(Action<object> call, object evt, Delegate target)
        {
            try
            {
                call(evt);
            }
            catch (Exception e)
            {
                // 행동 안이면 그 행동이 실패한다(바꾼 것이 모두 취소된다 — 반쯤 바뀐 데이터가 남지 않게). 플레이어용 실패(GameFailure)도 그대로 올라간다.
                if (InAction?.Invoke() ?? false) throw;
                // 행동 밖: 한 구독자의 오류가 다른 구독자를 막지 않게 하되, 조용히 삼키지 않고 기록한다.
                _journal.Append(JournalKind.Error, "Events", $"구독자 오류 ({target.Method.DeclaringType?.Name}.{target.Method.Name}): {e.Message}");
            }
        }

        static readonly Dictionary<Type, string> DisplayNames = new Dictionary<Type, string>();

        // [GameEvent]의 표시 이름(형식마다 처음 한 번 찾아 둔다).
        public static string DisplayNameOf(Type type)
        {
            lock (DisplayNames)
            {
                if (DisplayNames.TryGetValue(type, out var name)) return name;
                var attr = type.GetCustomAttribute<GameEventAttribute>();
                name = !string.IsNullOrEmpty(attr?.DisplayName) ? attr.DisplayName : type.Name;
                DisplayNames[type] = name;
                return name;
            }
        }

        // 기록 문장: "표시 이름 (필드=값, …)". 값은 생성 코드(FieldAccess)로 읽는다.
        public static string Describe(object evt)
        {
            if (evt is IDescribable describable) return describable.Describe();

            var type = evt.GetType();
            var builder = new StringBuilder(DisplayNameOf(type));
            var names = FieldAccess.NamesOf(type);
            if (names.Count == 0) return builder.ToString();

            builder.Append(" (");
            for (int i = 0; i < names.Count; i++)
            {
                if (i > 0) builder.Append(", ");
                var value = FieldAccess.Find(type, names[i])?.Get(evt);
                builder.Append(names[i]).Append('=')
                       .Append(value is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : value?.ToString() ?? "없음");
            }
            return builder.Append(')').ToString();
        }

        sealed class Subscription : IDisposable
        {
            Action _dispose;

            public Subscription(Action dispose) => _dispose = dispose;

            public void Dispose()
            {
                _dispose?.Invoke();
                _dispose = null;
            }
        }
    }
}
