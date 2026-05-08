#if GAME_SYSTEMS_READY
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using System.Reflection;
#endif

// 로그 정의를 담는 static 클래스에 붙이는 어트리뷰트.
// 에디터 대시보드가 어셈블리를 스캔해 로그 목록을 빌드하는 데 사용한다.
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class LogProviderAttribute : Attribute { }

// ---------------------------------------------------------

public enum LogMergeType {
    Add,    // 런 간 합산 (예: 총 딜량)
    Max     // 최고치 보존 (예: 최대 콤보)
}

[Serializable]
public class LogData {
    public Dictionary<string, float> SumLogs = new Dictionary<string, float>();
    public Dictionary<string, float> MaxLogs = new Dictionary<string, float>();

    public LogData Clone() => new LogData {
        SumLogs = new Dictionary<string, float>(SumLogs),
        MaxLogs = new Dictionary<string, float>(MaxLogs)
    };
}

// ---------------------------------------------------------
// 로그 정의 베이스 클래스
// ---------------------------------------------------------
public abstract class LogDefinition {
    public string       Key       { get; }
    public LogMergeType MergeType { get; }

    protected LogDefinition(string key, LogMergeType mergeType) {
        Key       = key;
        MergeType = mergeType;
    }

    public abstract void Subscribe(object observer);
}

public class LogDefinition<TEvent> : LogDefinition where TEvent : struct {
    private readonly Func<TEvent, float> _extractor;

    public LogDefinition(string key, LogMergeType mergeType, Func<TEvent, float> extractor)
        : base(key, mergeType) {
        _extractor = extractor;
    }

    public override void Subscribe(object observer) {
        // 로그는 이벤트 데이터를 읽기만 하므로 ref로 받되 수정하지 않는다.
        // _extractor는 this 필드이므로 ref 람다에서 캡처 가능.
        Events.Register<TEvent>(observer, (ref TEvent e) => {
            Logs.RecordValue(Key, _extractor(e), MergeType);
        });
    }
}

// ---------------------------------------------------------
// 중앙 로그 시스템
// ---------------------------------------------------------
public static class Logs {
    public static LogData       RunLog     { get; private set; } = new LogData();
    public static LogData       TotalLog   { get; private set; } = new LogData();
    public static List<LogData> RunHistory { get; private set; } = new List<LogData>();

    private const int MaxHistoryCount = 5;

    private static readonly List<LogDefinition> definitions    = new List<LogDefinition>();
    private static readonly object              systemObserver = new object();

    public static IReadOnlyList<LogDefinition> Definitions => definitions.AsReadOnly();

    public static void AddDefinition(LogDefinition def) {
        definitions.Add(def);
    }

    public static void Initialize() {
        foreach (var def in definitions) {
            def.Subscribe(systemObserver);

            if (def.MergeType == LogMergeType.Add) {
                RunLog.SumLogs[def.Key] = 0f;
                if (!TotalLog.SumLogs.ContainsKey(def.Key))
                    TotalLog.SumLogs[def.Key] = 0f;
            }
            else {
                RunLog.MaxLogs[def.Key] = 0f;
                if (!TotalLog.MaxLogs.ContainsKey(def.Key))
                    TotalLog.MaxLogs[def.Key] = 0f;
            }
        }
    }

    public static void RecordValue(string key, float value, LogMergeType mergeType) {
        if (mergeType == LogMergeType.Add)
            RunLog.SumLogs[key] += value;
        else
            RunLog.MaxLogs[key] = Mathf.Max(RunLog.MaxLogs[key], value);
    }

    public static void Settle() {
        foreach (var kvp in RunLog.SumLogs)
            TotalLog.SumLogs[kvp.Key] += kvp.Value;
        foreach (var kvp in RunLog.MaxLogs)
            TotalLog.MaxLogs[kvp.Key] = Mathf.Max(TotalLog.MaxLogs[kvp.Key], kvp.Value);

        RunHistory.Add(RunLog.Clone());
        if (RunHistory.Count > MaxHistoryCount)
            RunHistory.RemoveAt(0);

        foreach (var key in RunLog.SumLogs.Keys.ToList()) RunLog.SumLogs[key] = 0f;
        foreach (var key in RunLog.MaxLogs.Keys.ToList()) RunLog.MaxLogs[key] = 0f;
    }

#if UNITY_EDITOR
    [UnityEditor.Callbacks.DidReloadScripts]
    private static void InvalidateCache() => _cachedDefinitions = null;

    private static List<LogDefinition> _cachedDefinitions;

    public static IReadOnlyList<LogDefinition> ScanDefinitions() {
        if (_cachedDefinitions != null) return _cachedDefinitions;

        _cachedDefinitions = new List<LogDefinition>();
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
            if (asm.FullName.StartsWith("UnityEditor", StringComparison.Ordinal)) continue;
            foreach (var type in asm.GetTypes()) {
                if (!type.IsDefined(typeof(LogProviderAttribute), false)) continue;
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static)) {
                    if (!typeof(LogDefinition).IsAssignableFrom(field.FieldType)) continue;
                    if (field.GetValue(null) is LogDefinition def)
                        _cachedDefinitions.Add(def);
                }
            }
        }
        return _cachedDefinitions;
    }
#endif
}

/*
// ── 로그 정의 (별도 파일 권장) ──────────────────────────────
[LogProvider]
public static class MyLogs {
    public static readonly LogDefinition DamageDealt =
        new LogDefinition<PlayerDamageEvent>(
            "TotalDamageDealt", LogMergeType.Add, e => e.FinalDamage);

    public static readonly LogDefinition MaxCombo =
        new LogDefinition<PlayerComboEvent>(
            "MaxComboRecord", LogMergeType.Max, e => e.ComboCount);
}

// ── 초기화 (게임 시작 MonoBehaviour) ────────────────────────
public void Start() {
    Logs.AddDefinition(MyLogs.DamageDealt);
    Logs.AddDefinition(MyLogs.MaxCombo);
    Logs.Initialize();
}

// ── 런 종료 시 ──────────────────────────────────────────────
public void OnRunEnd() {
    Logs.Settle();   // RunLog → TotalLog 합산, RunLog 초기화, 히스토리 저장
}
*/
#endif
