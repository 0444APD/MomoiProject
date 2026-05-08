#if GAME_SYSTEMS_READY
using System;
using System.Collections.Generic;
using UnityEngine;

// 이벤트 타입으로 사용할 struct에 붙이는 마킹용 어트리뷰트.
// 에디터 대시보드가 어셈블리를 스캔해 이벤트 목록을 빌드하는 데 사용한다.
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class GameEventAttribute : Attribute { }

// ref 파라미터를 받는 이벤트 핸들러 델리게이트.
// 구독자가 이벤트 데이터를 직접 수정할 수 있고, Trigger 호출부는 최종 수정 결과를 반환받는다.
public delegate void RefAction<T>(ref T e) where T : struct;

public static class Events {
    private class EventNode {
        public object   Observer { get; }
        public Delegate Action   { get; }

        public EventNode(object observer, Delegate action) {
            Observer = observer;
            Action   = action;
        }
    }

    private static readonly Dictionary<Type, List<EventNode>> subscribers =
        new Dictionary<Type, List<EventNode>>();

    // 에디터 패널용 런타임 읽기 전용 접근자
    public static Dictionary<Type, int> GetSubscriberCounts() {
        var result = new Dictionary<Type, int>();
        foreach (var kvp in subscribers)
            result[kvp.Key] = kvp.Value.Count;
        return result;
    }

    public static void Register<T>(object observer, RefAction<T> action) where T : struct {
        if (observer == null) return;

        var eventType = typeof(T);
        if (!subscribers.ContainsKey(eventType))
            subscribers[eventType] = new List<EventNode>();

        var list = subscribers[eventType];
        for (int i = 0; i < list.Count; i++) {
            if (list[i].Observer == observer) {
                Debug.LogError($"[Events] 중복 구독 감지: {observer.GetType().Name} 객체가 이미 {eventType.Name} 이벤트를 구독 중입니다.");
                return;
            }
        }
        list.Add(new EventNode(observer, action));
    }

    public static void Cancel<T>(RefAction<T> action) where T : struct {
        var eventType = typeof(T);
        if (!subscribers.TryGetValue(eventType, out var list)) return;
        for (int i = list.Count - 1; i >= 0; i--) {
            if (list[i].Action.Equals(action)) { list.RemoveAt(i); break; }
        }
    }

    public static void Cancel<T>(object observer) where T : struct {
        var eventType = typeof(T);
        if (!subscribers.TryGetValue(eventType, out var list)) return;
        for (int i = list.Count - 1; i >= 0; i--) {
            if (list[i].Observer == observer) { list.RemoveAt(i); break; }
        }
    }

    // 구독자들이 ref로 eventArgs를 수정하며 순회.
    // 최종 수정된 struct를 반환하므로 호출부에서 결과를 회수해 사용할 수 있다.
    // 단순 알림 이벤트라면 반환값을 무시해도 무방하다.
    public static T Trigger<T>(T eventArgs) where T : struct {
        var eventType = typeof(T);
        if (!subscribers.TryGetValue(eventType, out var list)) return eventArgs;

        var tempList     = new List<EventNode>(list);
        bool needCleanup = false;

        for (int i = 0; i < tempList.Count; i++) {
            var node = tempList[i];

            bool dead = node.Observer is UnityEngine.Object uObj
                ? uObj == null
                : node.Observer == null;

            if (dead) { needCleanup = true; continue; }

            if (node.Action is RefAction<T> action)
                action(ref eventArgs);
        }

        if (needCleanup) {
            list.RemoveAll(n =>
                (n.Observer is UnityEngine.Object u && u == null) ||
                n.Observer == null);
        }

        return eventArgs;
    }

#if UNITY_EDITOR
    [UnityEditor.Callbacks.DidReloadScripts]
    private static void InvalidateCache() => _cachedEventTypes = null;

    private static List<Type> _cachedEventTypes;

    public static IReadOnlyList<Type> ScanEventTypes() {
        if (_cachedEventTypes != null) return _cachedEventTypes;

        _cachedEventTypes = new List<Type>();
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
            if (asm.FullName.StartsWith("UnityEditor", StringComparison.Ordinal)) continue;
            foreach (var type in asm.GetTypes()) {
                if (type.IsDefined(typeof(GameEventAttribute), false))
                    _cachedEventTypes.Add(type);
            }
        }
        return _cachedEventTypes;
    }
#endif
}

/*
// 1. 이벤트 타입 struct 정의 — [GameEvent] 어트리뷰트를 붙이면 대시보드에 자동 등록된다
[GameEvent]
public struct PlayerDamageEvent {
    public float BaseDamage;
    public float FinalDamage;   // 구독자가 ref로 수정할 수 있는 필드
}

// 2. 구독 (ref 파라미터)
public class Equipment : MonoBehaviour {
    private void OnEnable()  => Events.Register<PlayerDamageEvent>(this, OnDamage);
    private void OnDisable() => Events.Cancel<PlayerDamageEvent>(this);

    private void OnDamage(ref PlayerDamageEvent e) {
        e.FinalDamage += e.BaseDamage * 0.1f;   // 10% 추가 피해 적용
    }
}

// 3. 발행 및 결과 회수
public class PlayerStats : MonoBehaviour {
    public void TakeDamage(float amount) {
        var evt    = new PlayerDamageEvent { BaseDamage = amount, FinalDamage = amount };
        var result = Events.Trigger(evt);        // 구독자들이 FinalDamage를 수정
        ApplyDamage(result.FinalDamage);         // 최종값 사용
    }
}
*/
#endif
