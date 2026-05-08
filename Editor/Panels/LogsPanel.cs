using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

public class LogsPanel : ISystemPanel {
    public string Title => "Logs";
    public int    Order => 40;

    private int     _historyTab  = 0;
    private Vector2 _staticScroll;
    private bool    _guideOpen = false;

    public void OnEnable()  { }
    public void OnDisable() { }

    public void OnGUI() {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Logs", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        if (Application.isPlaying)
            DrawRuntimeView();
        else
            DrawStaticView();

        EditorGUILayout.Space(12);
        DrawGuide();
    }

    // ── 런타임: RunLog / TotalLog / 런 히스토리 ──────────────────
    private void DrawRuntimeView() {
        DrawSettleButton();
        EditorGUILayout.Space(8);
        DrawLogTable("현재 런 (RunLog)", Logs.RunLog);
        EditorGUILayout.Space(8);
        DrawLogTable("누적 (TotalLog)", Logs.TotalLog);
        EditorGUILayout.Space(8);
        DrawHistory();
    }

    private void DrawSettleButton() {
        GUILayout.BeginHorizontal();
        EditorGUILayout.HelpBox(
            "Settle: 현재 런 로그를 누적에 합산하고 런 로그를 초기화합니다.",
            MessageType.None);
        GUILayout.BeginVertical(GUILayout.Width(80));
        GUILayout.Space(4);
        if (GUILayout.Button("Settle", GUILayout.Height(32))) Logs.Settle();
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
    }

    private void DrawLogTable(string header, LogData data) {
        EditorGUILayout.LabelField(header, EditorStyles.boldLabel);
        if (data.SumLogs.Count == 0 && data.MaxLogs.Count == 0) {
            EditorGUILayout.LabelField("  데이터 없음", EditorStyles.miniLabel);
            return;
        }
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        EditorGUILayout.LabelField("Key",  EditorStyles.toolbarButton, GUILayout.MinWidth(160));
        EditorGUILayout.LabelField("타입", EditorStyles.toolbarButton, GUILayout.Width(60));
        EditorGUILayout.LabelField("값",   EditorStyles.toolbarButton, GUILayout.Width(80));
        GUILayout.EndHorizontal();
        foreach (var kvp in data.SumLogs) DrawLogRow(kvp.Key, "Sum", kvp.Value);
        foreach (var kvp in data.MaxLogs) DrawLogRow(kvp.Key, "Max", kvp.Value);
    }

    private void DrawLogRow(string key, string type, float value) {
        GUILayout.BeginHorizontal();
        GUILayout.Space(4);
        EditorGUILayout.LabelField(key,                   GUILayout.MinWidth(160));
        EditorGUILayout.LabelField(type, EditorStyles.miniLabel, GUILayout.Width(60));
        EditorGUILayout.LabelField(value.ToString("F2"), EditorStyles.miniLabel, GUILayout.Width(80));
        GUILayout.EndHorizontal();
    }

    private void DrawHistory() {
        var history = Logs.RunHistory;
        if (history.Count == 0) return;
        EditorGUILayout.LabelField($"런 기록 (최근 {history.Count}회)", EditorStyles.boldLabel);
        var tabs = new string[history.Count];
        for (int i = 0; i < history.Count; i++) tabs[i] = $"Run {i + 1}";
        _historyTab = GUILayout.Toolbar(_historyTab, tabs);
        _historyTab = Mathf.Clamp(_historyTab, 0, history.Count - 1);
        DrawLogTable($"Run {_historyTab + 1}", history[_historyTab]);
    }

    // ── 비플레이: [LogProvider] 스캔 결과 ───────────────────────
    private const float LCOL_KEY   = 180f;
    private const float LCOL_MERGE = 72f;
    private const float LCOL_TYPE  = 160f;

    private static readonly Color AddColor = new Color(0.28f, 0.68f, 0.28f);
    private static readonly Color MaxColor = new Color(0.28f, 0.48f, 0.82f);

    private void DrawStaticView() {
        var defs = Logs.ScanDefinitions();

        if (defs.Count == 0) {
            EditorGUILayout.HelpBox(
                "[LogProvider] 어트리뷰트가 붙은 클래스가 없습니다.",
                MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField($"정의된 로그 항목 ({defs.Count})", EditorStyles.boldLabel);

        // 헤더
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label("Key",        EditorStyles.toolbarButton, GUILayout.Width(LCOL_KEY));
        GUILayout.Label("MergeType",  EditorStyles.toolbarButton, GUILayout.Width(LCOL_MERGE));
        GUILayout.Label("정의 클래스", EditorStyles.toolbarButton, GUILayout.Width(LCOL_TYPE));
        GUILayout.EndHorizontal();

        // 행
        _staticScroll = EditorGUILayout.BeginScrollView(_staticScroll);
        var rowBg = new Color(0.22f, 0.22f, 0.22f);
        var mergeStyle = new GUIStyle(EditorStyles.miniLabel);

        for (int i = 0; i < defs.Count; i++) {
            var def = defs[i];
            Rect rowRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(20));
            if (i % 2 == 0 && Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rowRect, rowBg);

            GUILayout.Space(4);
            GUILayout.Label(def.Key, GUILayout.Width(LCOL_KEY - 4));

            mergeStyle.normal.textColor = def.MergeType == LogMergeType.Add ? AddColor : MaxColor;
            GUILayout.Label(def.MergeType.ToString(), mergeStyle, GUILayout.Width(LCOL_MERGE));

            GUILayout.Label(def.GetType().Name, EditorStyles.miniLabel, GUILayout.Width(LCOL_TYPE));
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
    }

    // ── 가이드 (접기/펼치기) ─────────────────────────────────────
    private void DrawGuide() {
        _guideOpen = EditorGUILayout.Foldout(_guideOpen, "사용 예시", true, EditorStyles.foldoutHeader);
        if (!_guideOpen) return;

        EditorGUI.indentLevel++;

        DrawCodeSection("① 로그 정의 — [LogProvider] 클래스",
            "[LogProvider]\n" +
            "public static class MyLogs {\n" +
            "    // Add: 런마다 수치를 합산 (예: 총 딜량, 처치 수)\n" +
            "    public static readonly LogDefinition DamageDealt =\n" +
            "        new LogDefinition<PlayerDamageEvent>(\n" +
            "            \"TotalDamageDealt\",\n" +
            "            LogMergeType.Add,\n" +
            "            e => e.FinalDamage\n" +
            "        );\n\n" +
            "    // Max: 런마다 최고치를 보존 (예: 최대 콤보, 최대 피해)\n" +
            "    public static readonly LogDefinition MaxCombo =\n" +
            "        new LogDefinition<PlayerComboEvent>(\n" +
            "            \"MaxComboRecord\",\n" +
            "            LogMergeType.Max,\n" +
            "            e => e.ComboCount\n" +
            "        );\n" +
            "}");

        DrawCodeSection("② 초기화 (게임 시작 시 1회)",
            "void Start() {\n" +
            "    Logs.AddDefinition(MyLogs.DamageDealt);\n" +
            "    Logs.AddDefinition(MyLogs.MaxCombo);\n\n" +
            "    // 이벤트 구독 및 로그 키 초기화\n" +
            "    Logs.Initialize();\n" +
            "}");

        DrawCodeSection("③ 런 진행 중 (자동)",
            "// 이벤트가 발행될 때마다 LogDefinition이 자동으로 값을 기록한다.\n" +
            "// 별도의 로그 코드 없이 Events.Trigger() 호출만으로 동작.\n\n" +
            "var result = Events.Trigger(new PlayerDamageEvent { ... });\n" +
            "// → MyLogs.DamageDealt가 result.FinalDamage를 RunLog에 자동 기록");

        DrawCodeSection("④ 런 종료 — Settle()",
            "void OnRunEnd() {\n" +
            "    // RunLog 값을 TotalLog에 합산(Add) 또는 최고치 갱신(Max)\n" +
            "    // RunLog를 0으로 초기화\n" +
            "    // 직전 RunLog의 스냅샷을 RunHistory에 저장 (최대 5회)\n" +
            "    Logs.Settle();\n" +
            "}");

        DrawCodeSection("⑤ 데이터 읽기",
            "// 현재 런\n" +
            "float thisDmg = Logs.RunLog.SumLogs[\"TotalDamageDealt\"];\n\n" +
            "// 누적 전체\n" +
            "float totalDmg = Logs.TotalLog.SumLogs[\"TotalDamageDealt\"];\n\n" +
            "// 이전 런 기록 (0 = 가장 오래된 것)\n" +
            "float prevCombo = Logs.RunHistory[^1].MaxLogs[\"MaxComboRecord\"];");

        EditorGUI.indentLevel--;
    }

    private void DrawCodeSection(string sectionTitle, string code) {
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField(sectionTitle, EditorStyles.boldLabel);
        var style = new GUIStyle(EditorStyles.helpBox) { fontSize = 11, wordWrap = false };
        EditorGUILayout.TextArea(code, style);
    }
}
