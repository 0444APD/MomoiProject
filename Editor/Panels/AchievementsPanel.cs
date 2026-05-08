using UnityEditor;
using UnityEngine;

public class AchievementsPanel : ISystemPanel {
    public string Title => "Achievements";
    public int    Order => 30;

    private string  _testUnlockKey = "";
    private Vector2 _clearedScroll;
    private Vector2 _progressScroll;
    private Vector2 _staticScroll;
    private bool    _guideOpen = false;

    public void OnEnable()  { }
    public void OnDisable() { }

    public void OnGUI() {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Achievements", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        if (Application.isPlaying)
            DrawRuntimeView();
        else
            DrawStaticView();

        EditorGUILayout.Space(12);
        DrawGuide();
    }

    // ── 런타임: 달성 현황 ────────────────────────────────────────
    private void DrawRuntimeView() {
        DrawSummary();
        EditorGUILayout.Space(8);
        DrawClearedList();
        EditorGUILayout.Space(8);
        DrawProgressList();
        EditorGUILayout.Space(8);
        DrawTestUnlock();
    }

    private void DrawSummary() {
        int defined    = Achievements.Definitions.Count;
        int cleared    = Achievements.SaveData.ClearedIds.Count;
        int inProgress = Achievements.SaveData.Progresses.Count;

        GUILayout.BeginHorizontal();
        DrawStatBox("정의됨",  defined.ToString(),    new Color(0.25f, 0.25f, 0.25f));
        DrawStatBox("달성",    cleared.ToString(),    new Color(0.20f, 0.55f, 0.25f));
        DrawStatBox("진행 중", inProgress.ToString(), new Color(0.25f, 0.40f, 0.70f));
        GUILayout.EndHorizontal();
    }

    private void DrawStatBox(string label, string value, Color bg) {
        Rect r = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                                          GUILayout.Height(50), GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(r, bg);
        var valStyle = new GUIStyle(EditorStyles.boldLabel)
            { fontSize = 20, alignment = TextAnchor.MiddleCenter,
              normal = { textColor = Color.white } };
        var lblStyle = new GUIStyle(EditorStyles.miniLabel)
            { alignment = TextAnchor.MiddleCenter,
              normal = { textColor = new Color(0.85f, 0.85f, 0.85f) } };
        EditorGUI.LabelField(new Rect(r.x, r.y + 4,     r.width, 26), value, valStyle);
        EditorGUI.LabelField(new Rect(r.x, r.yMax - 18, r.width, 16), label, lblStyle);
    }

    private void DrawClearedList() {
        EditorGUILayout.LabelField($"달성 목록 ({Achievements.SaveData.ClearedIds.Count})",
                                   EditorStyles.boldLabel);
        if (Achievements.SaveData.ClearedIds.Count == 0) {
            EditorGUILayout.LabelField("  달성된 도전과제 없음", EditorStyles.miniLabel);
            return;
        }
        _clearedScroll = EditorGUILayout.BeginScrollView(_clearedScroll, GUILayout.MaxHeight(120));
        foreach (var id in Achievements.SaveData.ClearedIds) {
            GUILayout.BeginHorizontal();
            GUILayout.Space(8);
            var green = new GUIStyle(EditorStyles.label);
            green.normal.textColor = new Color(0.35f, 0.80f, 0.40f);
            GUILayout.Label("✔", green, GUILayout.Width(16));
            GUILayout.Label(id);
            GUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
    }

    private void DrawProgressList() {
        if (Achievements.SaveData.Progresses.Count == 0) return;
        EditorGUILayout.LabelField("진행 중인 목록", EditorStyles.boldLabel);
        _progressScroll = EditorGUILayout.BeginScrollView(_progressScroll, GUILayout.MaxHeight(120));
        foreach (var kvp in Achievements.SaveData.Progresses) {
            GUILayout.BeginHorizontal();
            GUILayout.Space(8);
            EditorGUILayout.LabelField(kvp.Key,           GUILayout.Width(160));
            EditorGUILayout.LabelField($"{kvp.Value:F1}", GUILayout.Width(60));
            Rect bar = GUILayoutUtility.GetRect(0, 14, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(bar, new Color(0.2f, 0.2f, 0.2f));
            GUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
    }

    private void DrawTestUnlock() {
        EditorGUILayout.LabelField("테스트 — 강제 달성", EditorStyles.boldLabel);
        GUILayout.BeginHorizontal();
        _testUnlockKey = EditorGUILayout.TextField("Key", _testUnlockKey);
        if (GUILayout.Button("Unlock", GUILayout.Width(70)) &&
            !string.IsNullOrWhiteSpace(_testUnlockKey))
            Achievements.Unlock(_testUnlockKey);
        GUILayout.EndHorizontal();
    }

    // ── 비플레이: [AchievementProvider] 스캔 결과 ───────────────
    private const float COL_KEY  = 200f;
    private const float COL_TYPE = 160f;

    private void DrawStaticView() {
        var defs = Achievements.ScanDefinitions();

        if (defs.Count == 0) {
            EditorGUILayout.HelpBox(
                "[AchievementProvider] 어트리뷰트가 붙은 클래스가 없습니다.",
                MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField($"정의된 도전과제 ({defs.Count})", EditorStyles.boldLabel);

        // 헤더
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label("Key",  EditorStyles.toolbarButton, GUILayout.Width(COL_KEY));
        GUILayout.Label("타입", EditorStyles.toolbarButton, GUILayout.Width(COL_TYPE));
        GUILayout.EndHorizontal();

        // 행
        _staticScroll = EditorGUILayout.BeginScrollView(_staticScroll);
        var rowBg = new Color(0.22f, 0.22f, 0.22f);
        for (int i = 0; i < defs.Count; i++) {
            var def = defs[i];
            Rect rowRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(20));
            if (i % 2 == 0 && Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rowRect, rowBg);
            GUILayout.Space(4);
            GUILayout.Label(def.Key,               GUILayout.Width(COL_KEY  - 4));
            GUILayout.Label(def.GetType().Name,    EditorStyles.miniLabel,
                            GUILayout.Width(COL_TYPE));
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
    }

    // ── 가이드 (접기/펼치기) ─────────────────────────────────────
    private void DrawGuide() {
        _guideOpen = EditorGUILayout.Foldout(_guideOpen, "사용 예시", true, EditorStyles.foldoutHeader);
        if (!_guideOpen) return;

        EditorGUI.indentLevel++;

        DrawCodeSection("① 도전과제 정의 — [AchievementProvider] 클래스",
            "[AchievementProvider]\n" +
            "public static class MyAchievements {\n" +
            "    // 단발성: 조건 충족 시 즉시 달성\n" +
            "    public static readonly AchievementDefinition ReachLevel10 =\n" +
            "        new ConditionAchievement<PlayerLevelUpEvent>(\n" +
            "            \"ReachLevel10\",\n" +
            "            e => e.NewLevel >= 10,\n" +
            "            () => Debug.Log(\"보상 지급!\")   // rewardAction (선택)\n" +
            "        );\n\n" +
            "    // 누적: 진행 수치가 목표에 도달하면 달성\n" +
            "    public static readonly AchievementDefinition Deal10kDamage =\n" +
            "        new ProgressAchievement<PlayerDamageEvent>(\n" +
            "            \"Deal10kDamage\",\n" +
            "            10000f,\n" +
            "            e => e.FinalDamage\n" +
            "        );\n" +
            "}");

        DrawCodeSection("② 초기화 (게임 시작 시 1회)",
            "void Start() {\n" +
            "    // 세이브 데이터 주입 (없으면 생략)\n" +
            "    Achievements.LoadData(mySaveData);\n\n" +
            "    // 도전과제 등록\n" +
            "    Achievements.AddDefinition(MyAchievements.ReachLevel10);\n" +
            "    Achievements.AddDefinition(MyAchievements.Deal10kDamage);\n\n" +
            "    // 미달성 항목만 이벤트 구독 시작\n" +
            "    Achievements.Initialize();\n" +
            "}");

        DrawCodeSection("③ 달성 흐름 (자동)",
            "// 이벤트가 발행되면 ConditionAchievement / ProgressAchievement가\n" +
            "// 내부적으로 조건을 검사하고 달성 시 Achievements.Unlock()을 호출한다.\n\n" +
            "// Unlock()은 AchievementUnlockedEvent를 발행하므로\n" +
            "// UI 등에서 해당 이벤트를 구독해 알림 처리를 할 수 있다.\n" +
            "Events.Register<AchievementUnlockedEvent>(this, (ref AchievementUnlockedEvent e) => {\n" +
            "    ShowToast($\"{e.AchievementKey} 달성!\");\n" +
            "});");

        DrawCodeSection("④ 강제 달성 (테스트 / 외부 트리거)",
            "Achievements.Unlock(\"ReachLevel10\");");

        EditorGUI.indentLevel--;
    }

    private void DrawCodeSection(string sectionTitle, string code) {
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField(sectionTitle, EditorStyles.boldLabel);
        var style = new GUIStyle(EditorStyles.helpBox) { fontSize = 11, wordWrap = false };
        EditorGUILayout.TextArea(code, style);
    }
}
