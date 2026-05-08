#if GAME_SYSTEMS_READY
using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using System.Reflection;
#endif

// 도전과제 정의를 담는 static 클래스에 붙이는 어트리뷰트.
// 에디터 대시보드가 어셈블리를 스캔해 도전과제 목록을 빌드하는 데 사용한다.
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AchievementProviderAttribute : Attribute { }

// ---------------------------------------------------------

[Serializable]
public class AchievementSaveData {
    public List<string>              ClearedIds = new List<string>();
    public Dictionary<string, float> Progresses = new Dictionary<string, float>();
}

[GameEvent]
public struct AchievementUnlockedEvent {
    public string AchievementKey;
}

// ---------------------------------------------------------
// 도전과제 베이스 클래스
// ---------------------------------------------------------
public abstract class AchievementDefinition {
    public string Key          { get; }
    public Action RewardAction { get; }

    protected AchievementDefinition(string key, Action rewardAction = null) {
        Key          = key;
        RewardAction = rewardAction;
    }

    public abstract void Subscribe();
}

// ---------------------------------------------------------
// A. 단발성 도전과제 — 이벤트 조건이 충족되면 즉시 달성
// ---------------------------------------------------------
public class ConditionAchievement<TEvent> : AchievementDefinition where TEvent : struct {
    private readonly Func<TEvent, bool> _condition;

    public ConditionAchievement(string key, Func<TEvent, bool> condition, Action rewardAction = null)
        : base(key, rewardAction) {
        _condition = condition;
    }

    public override void Subscribe() {
        Events.Register<TEvent>(this, OnEvent);
    }

    // 이벤트 데이터를 읽기만 하므로 ref로 받되 수정하지 않는다
    private void OnEvent(ref TEvent e) {
        if (!_condition(e)) return;
        Achievements.Unlock(Key);
        Events.Cancel<TEvent>(this);
    }
}

// ---------------------------------------------------------
// B. 누적 도전과제 — 진행 수치가 목표치에 도달하면 달성
// ---------------------------------------------------------
public class ProgressAchievement<TEvent> : AchievementDefinition where TEvent : struct {
    private readonly float               _goal;
    private readonly Func<TEvent, float> _extractor;

    public ProgressAchievement(string key, float goal, Func<TEvent, float> extractor, Action rewardAction = null)
        : base(key, rewardAction) {
        _goal      = goal;
        _extractor = extractor;
    }

    public override void Subscribe() {
        Events.Register<TEvent>(this, OnEvent);
    }

    private void OnEvent(ref TEvent e) {
        float current = Achievements.AddProgress(Key, _extractor(e));
        if (current >= _goal) {
            Achievements.Unlock(Key);
            Events.Cancel<TEvent>(this);
        }
    }
}

// ---------------------------------------------------------
// 중앙 관리자
// ---------------------------------------------------------
public static class Achievements {
    public static AchievementSaveData SaveData { get; private set; } = new AchievementSaveData();

    private static readonly Dictionary<string, AchievementDefinition> definitions =
        new Dictionary<string, AchievementDefinition>();

    public static IReadOnlyDictionary<string, AchievementDefinition> Definitions => definitions;

    public static void LoadData(AchievementSaveData loadedData) {
        if (loadedData != null) SaveData = loadedData;
    }

    public static void AddDefinition(AchievementDefinition def) {
        definitions[def.Key] = def;
    }

    public static void Initialize() {
        foreach (var def in definitions.Values) {
            if (SaveData.ClearedIds.Contains(def.Key)) continue;
            def.Subscribe();
        }
    }

    public static float AddProgress(string key, float amount) {
        if (!SaveData.Progresses.ContainsKey(key))
            SaveData.Progresses[key] = 0f;
        SaveData.Progresses[key] += amount;
        return SaveData.Progresses[key];
    }

    public static void Unlock(string key) {
        if (SaveData.ClearedIds.Contains(key)) return;
        SaveData.ClearedIds.Add(key);
        Debug.Log($"[Achievements] 도전과제 달성: {key}");
        if (definitions.TryGetValue(key, out var def) && def.RewardAction != null)
            def.RewardAction.Invoke();
        Events.Trigger(new AchievementUnlockedEvent { AchievementKey = key });
    }

#if UNITY_EDITOR
    [UnityEditor.Callbacks.DidReloadScripts]
    private static void InvalidateCache() => _cachedDefinitions = null;

    private static List<AchievementDefinition> _cachedDefinitions;

    public static IReadOnlyList<AchievementDefinition> ScanDefinitions() {
        if (_cachedDefinitions != null) return _cachedDefinitions;

        _cachedDefinitions = new List<AchievementDefinition>();
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
            if (asm.FullName.StartsWith("UnityEditor", StringComparison.Ordinal)) continue;
            foreach (var type in asm.GetTypes()) {
                if (!type.IsDefined(typeof(AchievementProviderAttribute), false)) continue;
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static)) {
                    if (!typeof(AchievementDefinition).IsAssignableFrom(field.FieldType)) continue;
                    if (field.GetValue(null) is AchievementDefinition def)
                        _cachedDefinitions.Add(def);
                }
            }
        }
        return _cachedDefinitions;
    }
#endif
}

/*
// ── 도전과제 정의 (별도 파일 권장) ──────────────────────────
[AchievementProvider]
public static class MyAchievements {
    public static readonly AchievementDefinition ReachLevel10 =
        new ConditionAchievement<PlayerLevelUpEvent>(
            "ReachLevel10",
            e => e.NewLevel >= 10,
            () => Debug.Log("보상 지급!")
        );

    public static readonly AchievementDefinition Deal10kDamage =
        new ProgressAchievement<PlayerDamageEvent>(
            "Deal10kDamage",
            10000f,
            e => e.FinalDamage
        );
}

// ── 초기화 (게임 시작 MonoBehaviour) ────────────────────────
public void Start() {
    Achievements.LoadData(savedData);   // 세이브 데이터 주입 (없으면 생략)

    Achievements.AddDefinition(MyAchievements.ReachLevel10);
    Achievements.AddDefinition(MyAchievements.Deal10kDamage);

    Achievements.Initialize();          // 미달성 항목만 이벤트 구독
}
*/
#endif
