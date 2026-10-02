using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace GBible
{
    // 이벤트 타입(class·struct)에 붙인다. 데이터베이스 창의 이벤트 선택 목록에 표시된다.
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
                        Invoke(() => handler.Invoke(evt), handler.Original);
                }
                foreach (var listener in _listeners.ToArray())
                    Invoke(() => listener(evt), listener);
            }
            Published?.Invoke(evt);
        }

        public int SubscriberCount(Type eventType) => _handlers.TryGetValue(eventType, out var list) ? list.Count : 0;

        void Invoke(Action call, Delegate target)
        {
            try
            {
                call();
            }
            catch (Exception e)
            {
                // 한 구독자의 오류가 다른 구독자를 막지 않게 하되, 조용히 삼키지 않고 기록한다.
                _journal.Append(JournalKind.Error, "Events", $"구독자 오류 ({target.Method.DeclaringType?.Name}.{target.Method.Name}): {e.Message}");
            }
        }

        public static string DisplayNameOf(Type type)
        {
            var attr = type.GetCustomAttribute<GameEventAttribute>();
            return !string.IsNullOrEmpty(attr?.DisplayName) ? attr.DisplayName : type.Name;
        }

        public static string Describe(object evt)
        {
            if (evt is IDescribable describable) return describable.Describe();

            var type = evt.GetType();
            var builder = new StringBuilder(DisplayNameOf(type));
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            if (fields.Length == 0) return builder.ToString();

            builder.Append(" (");
            for (int i = 0; i < fields.Length; i++)
            {
                if (i > 0) builder.Append(", ");
                var value = fields[i].GetValue(evt);
                builder.Append(fields[i].Name).Append('=')
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
