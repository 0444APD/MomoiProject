using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;

public class HomePanel : ISystemPanel
{
    public string Title => "Home";
    public int Order => 0;

    private static readonly Color s_green  = new Color(0.30f, 0.75f, 0.35f);
    private static readonly Color s_red    = new Color(0.82f, 0.28f, 0.28f);
    private static readonly Color s_yellow = new Color(0.85f, 0.75f, 0.20f);

    private bool _newtonsoftInstalled;
    private bool _addressableInstalled;
    private bool _odinInstalled;
    private bool _dotweenInstalled;

    public void OnEnable()
    {
        var defines = GetCurrentDefines();
        _odinInstalled        = defines.Contains("ODIN_INSPECTOR");
        _newtonsoftInstalled  = defines.Contains("NEWTONSOFT_JSON");
        _addressableInstalled = defines.Contains("Addressable");
        _dotweenInstalled     = defines.Contains("DOTWEEN");
    }

    private static HashSet<string> GetCurrentDefines()
    {
        var group = EditorUserBuildSettings.selectedBuildTargetGroup;
        if (group == BuildTargetGroup.Unknown) return new HashSet<string>();
        var target = NamedBuildTarget.FromBuildTargetGroup(group);
        return new HashSet<string>(
            PlayerSettings.GetScriptingDefineSymbols(target)
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
    }

    public void OnDisable() { }

    public void OnGUI()
    {
        EditorGUILayout.Space(12);
        DrawTitle();
        EditorGUILayout.Space(16);
        DrawPackageStatus();
        EditorGUILayout.Space(16);
        DrawSystemStatus();
    }

    private void DrawTitle()
    {
        var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            { fontSize = 18, alignment = TextAnchor.MiddleCenter };

        EditorGUILayout.LabelField("Game Systems Dashboard", titleStyle, GUILayout.Height(28));

        var subStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 11 };
        EditorGUILayout.LabelField("Assets/Code/System 에 포함된 공용 시스템 모음", subStyle);
    }

    private void DrawPackageStatus()
    {
        EditorGUILayout.LabelField("패키지 상태", EditorStyles.boldLabel);

        DrawStatusRow("Newtonsoft.Json",         _newtonsoftInstalled,  "NEWTONSOFT_JSON 심볼 자동 설정됨");
        DrawStatusRow("Addressables",             _addressableInstalled, "Addressable 심볼 자동 설정됨");
        DrawStatusRow("Odin Inspector (선택)",    _odinInstalled,        "OdinEditorWindow 계열 기능 활성화");
        DrawStatusRow("DOTween (선택)",           _dotweenInstalled,     "SlotUI 호버 등 트윈 연출 활성화");
    }

    private void DrawStatusRow(string label, bool installed, string note)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Space(12);

        var dotStyle = new GUIStyle(EditorStyles.label) { fontSize = 16 };
        dotStyle.normal.textColor = installed ? s_green : s_red;
        GUILayout.Label(installed ? "●" : "○", dotStyle, GUILayout.Width(20));

        GUILayout.Label(label, GUILayout.Width(180));

        var noteStyle = new GUIStyle(EditorStyles.miniLabel);
        noteStyle.normal.textColor = installed ? new Color(0.5f, 0.5f, 0.5f) : s_yellow;
        GUILayout.Label(installed ? note : "미설치 — 해당 기능 비활성화", noteStyle);

        GUILayout.EndHorizontal();
        GUILayout.Space(2);
    }

    private void DrawSystemStatus()
    {
        EditorGUILayout.LabelField("시스템 상태", EditorStyles.boldLabel);

#if GAME_SYSTEMS_READY
        var playStyle = new GUIStyle(EditorStyles.helpBox);
        bool isPlaying = Application.isPlaying;

        if (isPlaying)
        {
            DrawInfoRow("모드",       "플레이 중 ▶");
            DrawInfoRow("SaveManager", $"슬롯 {SaveManager.CurrentSlot}  |  데이터: {(SaveManager.Current != null ? "로드됨" : "없음")}");
            DrawInfoRow("Events",      $"등록된 이벤트 타입: {Events.GetSubscriberCounts().Count}개");
            DrawInfoRow("Achievements", $"달성: {Achievements.SaveData.ClearedIds.Count}개  |  정의: {Achievements.Definitions.Count}개");
            DrawInfoRow("Logs",        $"정의: {Logs.Definitions.Count}개  |  기록: {Logs.RunHistory.Count}회");
        }
        else
        {
            EditorGUILayout.HelpBox("시스템 상태는 플레이 모드에서 실시간으로 확인할 수 있습니다.", MessageType.Info);
        }
#else
        EditorGUILayout.HelpBox("필수 패키지가 모두 설치되면 시스템이 활성화됩니다.", MessageType.Warning);
#endif
    }

    private void DrawInfoRow(string label, string value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Space(12);
        EditorGUILayout.LabelField(label, GUILayout.Width(110));
        EditorGUILayout.LabelField(value, EditorStyles.miniLabel);
        GUILayout.EndHorizontal();
    }
}
