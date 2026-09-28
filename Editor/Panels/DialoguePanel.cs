// #if SYSTEM_DIALOGUE 없음 — 비활성 상태에서도 Dashboard에 항상 표시

using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

public class DialoguePanel : ISystemPanel
{
    public string Title => "Dialogue";
    public int    Order => 60;

    private Vector2 _scroll;
    const string Symbol      = "SYSTEM_DIALOGUE";
    const string ReadySymbol = "GAME_SYSTEMS_READY";
    const string TmpSymbol   = "DOTWEEN_TEXTMESHPRO";

    public void OnEnable()  { }
    public void OnDisable() { }

    public void OnGUI()
    {
        DrawAddonHeader();
        if (!IsSymbolDefined(Symbol)) return;

        if (!IsSymbolDefined(TmpSymbol))
        {
            EditorGUILayout.HelpBox(
                "Dialogue는 DOTween Pro의 TextMeshPro 모듈이 필요합니다.\n" +
                "Tools > Demigiant > DOTween Utility Panel > Setup DOTween... 에서 TextMeshPro를 켜세요.",
                MessageType.Warning);
            return;
        }

#if SYSTEM_DIALOGUE && GAME_SYSTEMS_READY && DOTWEEN_TEXTMESHPRO
        EditorGUILayout.Space(4);
        DrawRuntimeInfo();
#endif
    }

    // ── 애드온 헤더 (항상 표시) ──────────────────────────────────────────
    void DrawAddonHeader()
    {
        bool isReady  = IsSymbolDefined(ReadySymbol);
        bool isActive = IsSymbolDefined(Symbol);

        // GAME_SYSTEMS_READY가 없어진 경우 SYSTEM_DIALOGUE도 자동 해제
        if (!isReady && isActive)
        {
            SetSymbol(Symbol, false);
            isActive = false;
        }

        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Dialogue Addon",
            new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 });
        GUILayout.FlexibleSpace();

        EditorGUI.BeginDisabledGroup(!isReady);
        bool next = EditorGUILayout.Toggle(isActive, GUILayout.Width(20));
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.EndHorizontal();

        if (!isReady)
        {
            EditorGUILayout.HelpBox(
                "GAME_SYSTEMS_READY 심볼이 필요합니다. " +
                "Home 탭에서 필수 패키지(DOTween, Odin Inspector 등)를 모두 설치하세요.",
                MessageType.Warning);
            return;
        }

        if (next != isActive) SetSymbol(Symbol, next);

        if (!isActive)
            EditorGUILayout.HelpBox(
                "비활성화 상태. 토글을 켜면 SYSTEM_DIALOGUE 심볼이 추가됩니다.",
                MessageType.Info);
    }

#if SYSTEM_DIALOGUE && GAME_SYSTEMS_READY && DOTWEEN_TEXTMESHPRO
    // ── 런타임 상태 표시 ────────────────────────────────────────────────
    void DrawRuntimeInfo()
    {
        EditorGUILayout.LabelField("Dialogue State", EditorStyles.boldLabel);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Play mode에서만 상태가 표시됩니다.", MessageType.Info);
            return;
        }

        if (DialogueManager.Instance == null)
        {
            EditorGUILayout.HelpBox("DialogueManager가 씬에 없습니다.", MessageType.Warning);
            return;
        }

        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        EditorGUILayout.LabelField("State",
            DialogueManager.Instance.currentState.ToString(),
            EditorStyles.boldLabel);

        string directorName = DialogueManager.Instance.ActiveDirector != null
            ? DialogueManager.Instance.ActiveDirector.name
            : "없음";
        EditorGUILayout.LabelField("Active Director", directorName);

        EditorGUILayout.EndVertical();

        EditorGUILayout.EndScrollView();
    }
#endif

    // ── Define Symbol 유틸 ────────────────────────────────────────────────
    static bool IsSymbolDefined(string symbol)
    {
        var target  = NamedBuildTarget.FromBuildTargetGroup(
                          EditorUserBuildSettings.selectedBuildTargetGroup);
        var defines = PlayerSettings.GetScriptingDefineSymbols(target)
                          .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        return defines.Contains(symbol);
    }

    static void SetSymbol(string symbol, bool enable)
    {
        var target  = NamedBuildTarget.FromBuildTargetGroup(
                          EditorUserBuildSettings.selectedBuildTargetGroup);
        var defines = PlayerSettings.GetScriptingDefineSymbols(target)
                          .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                          .ToList();

        if (enable) { if (!defines.Contains(symbol)) defines.Add(symbol); }
        else        { defines.Remove(symbol); }

        PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines));
    }
}
