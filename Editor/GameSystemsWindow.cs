using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;

public class GameSystemsWindow : EditorWindow
{
    // --- 상태 ---
    [SerializeField] private int _selectedIndex;
    private List<ISystemPanel> _panels = new List<ISystemPanel>();
    private Vector2 _scroll;

    // --- 스타일 상수 ---
    private static readonly Color s_barBg     = new Color(0.17f, 0.17f, 0.17f);
    private static readonly Color s_activeLine = new Color(0.28f, 0.48f, 0.82f);
    private static readonly Color s_border     = new Color(0.10f, 0.10f, 0.10f);
    private static readonly Color s_locked     = new Color(0.38f, 0.38f, 0.38f);
    private const float TabBarH = 36f;

    // GAME_SYSTEMS_READY 심볼 존재 여부 (동적 체크)
    private static bool IsSystemReady()
    {
        var group = EditorUserBuildSettings.selectedBuildTargetGroup;
        if (group == BuildTargetGroup.Unknown) return false;
        var target  = NamedBuildTarget.FromBuildTargetGroup(group);
        var defines = PlayerSettings.GetScriptingDefineSymbols(target)
                          .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        return Array.IndexOf(defines, "GAME_SYSTEMS_READY") >= 0;
    }

    // -----------------------------------------------------------
    // 진입점
    // -----------------------------------------------------------
    [MenuItem("Game Systems/Dashboard")]
    public static void Open()
    {
        var w = GetWindow<GameSystemsWindow>("Game Systems");
        w.minSize = new Vector2(960, 340);
        w.Show();
    }

    // -----------------------------------------------------------
    // 생명주기
    // -----------------------------------------------------------
    private void OnEnable()
    {
        titleContent = new GUIContent("Game Systems");
        BuildPanels();
        EnableCurrent();

        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private void OnDisable()
    {
        DisableCurrent();
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
    }

    // 플레이모드 전환 시 패널 재활성화(상태 갱신 목적)
    private void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode ||
            state == PlayModeStateChange.EnteredEditMode)
        {
            DisableCurrent();
            EnableCurrent();
            Repaint();
        }
    }

    // 플레이 모드에서 실시간 갱신
    private void Update()
    {
        if (Application.isPlaying)
            Repaint();
    }

    // -----------------------------------------------------------
    // 패널 목록 구성 (ISystemPanel 구현체 자동 탐색)
    // -----------------------------------------------------------
    private void BuildPanels()
    {
        DisableCurrent();

        _panels = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
            .Where(t => typeof(ISystemPanel).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .Select(t => { try { return (ISystemPanel)Activator.CreateInstance(t); } catch { return null; } })
            .Where(p => p != null)
            .OrderBy(p => p.Order)
            .ToList();

        _selectedIndex = Mathf.Clamp(_selectedIndex, 0, Mathf.Max(0, _panels.Count - 1));
        EnableCurrent();
    }

    private void EnableCurrent()
    {
        if (_panels.Count > 0) _panels[_selectedIndex].OnEnable();
    }

    private void DisableCurrent()
    {
        if (_panels != null && _panels.Count > 0 && _selectedIndex < _panels.Count)
            _panels[_selectedIndex].OnDisable();
    }

    // -----------------------------------------------------------
    // 그리기
    // -----------------------------------------------------------
    private void OnGUI()
    {
        DrawTabBar();
        DrawContent();
    }

    private void DrawTabBar()
    {
        // 배경
        EditorGUI.DrawRect(new Rect(0, 0, position.width, TabBarH), s_barBg);

        GUILayout.BeginArea(new Rect(0, 0, position.width, TabBarH));
        GUILayout.BeginHorizontal();
        GUILayout.Space(6);

        for (int i = 0; i < _panels.Count; i++)
            DrawTab(i);

        GUILayout.FlexibleSpace();

        // 새로고침 버튼
        var btnStyle = new GUIStyle(EditorStyles.miniButton)
            { fixedHeight = 22, fixedWidth = 26, fontSize = 14 };

        GUILayout.BeginVertical();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("↺", btnStyle))
            BuildPanels();
        GUILayout.FlexibleSpace();
        GUILayout.EndVertical();

        GUILayout.Space(6);
        GUILayout.EndHorizontal();
        GUILayout.EndArea();

        // 하단 구분선
        EditorGUI.DrawRect(new Rect(0, TabBarH, position.width, 1), s_border);
        GUILayout.Space(TabBarH + 2);
    }

    private void DrawTab(int index)
    {
        bool active  = index == _selectedIndex;
        bool isHome  = _panels[index].Order == 0;
        bool locked  = !isHome && !IsSystemReady();

        var style = new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle  = active ? FontStyle.Bold : FontStyle.Normal,
            fontSize   = 12,
            padding    = new RectOffset(12, 12, 0, 0),
        };

        if (locked)
            style.normal.textColor = s_locked;
        else
        {
            style.normal.textColor = active ? Color.white : new Color(0.58f, 0.58f, 0.58f);
            style.hover.textColor  = new Color(0.88f, 0.88f, 0.88f);
        }

        var title   = locked ? $"{_panels[index].Title} 🔒" : _panels[index].Title;
        var content = new GUIContent(title);
        Rect r = GUILayoutUtility.GetRect(content, style, GUILayout.Height(TabBarH), GUILayout.MinWidth(60));

        if (active)
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 2, r.width, 2), s_activeLine);

        if (!locked && GUI.Button(r, content, style) && !active)
        {
            DisableCurrent();
            _selectedIndex = index;
            EnableCurrent();
            GUI.FocusControl(null);
            Repaint();
        }
        else if (locked)
        {
            // 잠긴 탭은 클릭 이벤트를 소비하되 아무 동작 안 함
            GUI.Label(r, content, style);
        }
    }

    private void DrawContent()
    {
        if (_panels.Count == 0)
        {
            EditorGUILayout.Space(20);
            EditorGUILayout.HelpBox("등록된 패널이 없습니다.", MessageType.Info);
            return;
        }

        // GAME_SYSTEMS_READY 없는 상태에서 Home이 아닌 탭이 선택돼 있으면 Home으로 강제 이동
        bool isHome = _panels[_selectedIndex].Order == 0;
        if (!isHome && !IsSystemReady())
        {
            DisableCurrent();
            _selectedIndex = 0;
            EnableCurrent();
        }

        _scroll = GUILayout.BeginScrollView(_scroll);
        _panels[_selectedIndex].OnGUI();
        GUILayout.EndScrollView();
    }
}
