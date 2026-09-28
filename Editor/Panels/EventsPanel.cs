#if GAME_SYSTEMS_READY
using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;

public class EventsPanel : ISystemPanel {
    public string Title => "Events";
    public int    Order => 20;

    private bool _guideOpen = false;

    public void OnEnable()  { }
    public void OnDisable() { }

    public void OnGUI() {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Events", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        if (Application.isPlaying)
            DrawRuntimeView();
        else
            DrawStaticView();

        EditorGUILayout.Space(12);
        DrawGuide();
    }

    // ── 런타임: 실시간 구독자 수 ────────────────────────────────
    private void DrawRuntimeView() {
        var counts = Events.GetSubscriberCounts();
        if (counts.Count == 0) {
            EditorGUILayout.HelpBox("현재 구독 중인 이벤트가 없습니다.", MessageType.None);
            return;
        }
        DrawSubscriberTable(counts);
    }

    private const float COL_TYPE_RT = 200f;
    private const float COL_BAR    = 80f;

    private void DrawSubscriberTable(Dictionary<Type, int> counts) {
        // 헤더
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label("이벤트 타입", EditorStyles.toolbarButton, GUILayout.Width(COL_TYPE_RT));
        GUILayout.Label("구독자 수",   EditorStyles.toolbarButton, GUILayout.Width(COL_BAR));
        GUILayout.EndHorizontal();

        var activeColor = new Color(0.28f, 0.48f, 0.82f, 0.6f);
        var zeroColor   = new Color(0.5f,  0.5f,  0.5f,  0.3f);
        var rowBg       = new Color(0.22f, 0.22f, 0.22f);
        int idx = 0;

        foreach (var kvp in counts) {
            Rect rowRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(20));
            if (idx % 2 == 0 && Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rowRect, rowBg);

            GUILayout.Space(4);
            GUILayout.Label(kvp.Key.Name, GUILayout.Width(COL_TYPE_RT - 4));

            // 구독자 수 바 — 헤더 COL_BAR 폭과 동일하게 확보
            Rect bar = GUILayoutUtility.GetRect(COL_BAR, 16, GUILayout.Width(COL_BAR));
            if (Event.current.type == EventType.Repaint) {
                float fill = Mathf.Clamp01(kvp.Value / 10f);
                EditorGUI.DrawRect(bar, new Color(0.15f, 0.15f, 0.15f));
                EditorGUI.DrawRect(new Rect(bar.x, bar.y, bar.width * fill, bar.height),
                                   kvp.Value > 0 ? activeColor : zeroColor);
                EditorGUI.LabelField(bar, $"  {kvp.Value}", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndHorizontal();
            idx++;
        }
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField($"총 {counts.Count}개 이벤트 타입 등록됨",
                                   EditorStyles.centeredGreyMiniLabel);
    }

    // ── 비플레이: [GameEvent] 스캔 결과 ─────────────────────────
    private const float COL_NAME = 200f;

    private void DrawStaticView() {
        var types = Events.ScanEventTypes();

        if (types.Count == 0) {
            EditorGUILayout.HelpBox(
                "[GameEvent] 어트리뷰트가 붙은 struct가 없습니다.",
                MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField($"정의된 이벤트 타입 ({types.Count})", EditorStyles.boldLabel);

        // 헤더
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label("struct 명",    EditorStyles.toolbarButton, GUILayout.Width(COL_NAME));
        GUILayout.Label("네임스페이스", EditorStyles.toolbarButton, GUILayout.ExpandWidth(true));
        GUILayout.EndHorizontal();

        // 행 — 헤더와 동일한 Width 상수 사용
        var rowBg = new Color(0.22f, 0.22f, 0.22f);
        for (int i = 0; i < types.Count; i++) {
            var t = types[i];
            Rect rowRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(20));
            if (i % 2 == 0 && Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rowRect, rowBg);
            GUILayout.Space(4);
            GUILayout.Label(t.Name, GUILayout.Width(COL_NAME - 4));
            GUILayout.Label(string.IsNullOrEmpty(t.Namespace) ? "—" : t.Namespace,
                            EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();
        }
    }

    // ── 가이드 (접기/펼치기) ─────────────────────────────────────
    private void DrawGuide() {
        _guideOpen = EditorGUILayout.Foldout(_guideOpen, "사용 예시", true, EditorStyles.foldoutHeader);
        if (!_guideOpen) return;

        EditorGUI.indentLevel++;

        DrawCodeSection("① 이벤트 struct 정의",
            "[GameEvent]\n" +
            "public struct PlayerDamageEvent {\n" +
            "    public float BaseDamage;    // 원본 피해\n" +
            "    public float FinalDamage;   // 구독자가 ref로 수정할 최종 피해\n" +
            "}");

        DrawCodeSection("② 구독 — ref로 데이터를 직접 수정",
            "// MonoBehaviour 예시\n" +
            "private void OnEnable() {\n" +
            "    Events.Register<PlayerDamageEvent>(this, OnDamage);\n" +
            "}\n" +
            "private void OnDisable() {\n" +
            "    Events.Cancel<PlayerDamageEvent>(this);\n" +
            "}\n\n" +
            "// ref 파라미터로 이벤트 데이터 수정 가능\n" +
            "private void OnDamage(ref PlayerDamageEvent e) {\n" +
            "    e.FinalDamage += e.BaseDamage * 0.1f;  // 10% 추가 피해\n" +
            "}");

        DrawCodeSection("③ 발행 및 결과 회수",
            "var evt = new PlayerDamageEvent {\n" +
            "    BaseDamage  = rawDamage,\n" +
            "    FinalDamage = rawDamage    // 초기값은 BaseDamage와 동일하게\n" +
            "};\n\n" +
            "// Trigger는 구독자들이 수정한 최종 struct를 반환\n" +
            "var result = Events.Trigger(evt);\n" +
            "ApplyDamage(result.FinalDamage);\n\n" +
            "// 단순 알림용이라면 반환값 무시 가능\n" +
            "Events.Trigger(new GameStartEvent());");

        DrawCodeSection("④ 구독 해제 방법",
            "// 방법 A: 구독자 객체로 해제 (권장)\n" +
            "Events.Cancel<PlayerDamageEvent>(this);\n\n" +
            "// 방법 B: 델리게이트 참조로 해제\n" +
            "Events.Cancel<PlayerDamageEvent>(OnDamage);");

        EditorGUI.indentLevel--;
    }

    private void DrawCodeSection(string sectionTitle, string code) {
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField(sectionTitle, EditorStyles.boldLabel);
        var style = new GUIStyle(EditorStyles.helpBox) { fontSize = 11, wordWrap = false };
        EditorGUILayout.TextArea(code, style);
    }
}
#endif
