#if GAME_SYSTEMS_READY
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 대시보드 TextTable 탭.
///  • TextTable SO가 없으면 오류를 표시하고 재등록 UI를 제공합니다.
///  • SO가 있으면 Odin PropertyTree로 데이터를 직접 그려 경고 없이 즉시 수정 가능합니다.
/// </summary>
public class TextTablePanel : ISystemPanel
{
    public string Title => "TextTable";
    public int    Order => 45;

    private TextTable    _so;
    private PropertyTree _tree;
    private Vector2      _scroll;

    // ──────────────────────────────────────────────────────────────────────────
    // ISystemPanel
    // ──────────────────────────────────────────────────────────────────────────
    public void OnEnable()  => TryFindSO();
    public void OnDisable() => DisposeTree();

    public void OnGUI()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("TextTable", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        if (_so == null)
        {
            DrawMissingError();
            return;
        }

        DrawHeader();
        EditorGUILayout.Space(6);
        DrawInspector();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SO 탐색 / 바인딩
    // ──────────────────────────────────────────────────────────────────────────
    private void TryFindSO()
    {
        string[] guids = AssetDatabase.FindAssets("t:TextTable");
        if (guids.Length > 0)
            BindSO(AssetDatabase.LoadAssetAtPath<TextTable>(
                       AssetDatabase.GUIDToAssetPath(guids[0])));
    }

    private void BindSO(TextTable so)
    {
        DisposeTree();
        _so = so;
        if (_so != null)
            _tree = PropertyTree.Create(_so);
    }

    private void DisposeTree()
    {
        _tree?.Dispose();
        _tree = null;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SO 없음 — 오류 화면
    // ──────────────────────────────────────────────────────────────────────────
    private void DrawMissingError()
    {
        GUILayout.BeginVertical(GUI.skin.box);
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField(
            "⚠  TextTable ScriptableObject를 찾을 수 없습니다!",
            EditorStyles.boldLabel);
        EditorGUILayout.Space(2);
        EditorGUILayout.HelpBox(
            "SO가 삭제되었거나 경로가 변경된 경우,\n" +
            "아래 ObjectField에 기존 SO를 드래그하거나 새 SO를 생성하세요.",
            MessageType.Error);
        EditorGUILayout.Space(6);
        GUILayout.EndVertical();

        EditorGUILayout.Space(8);

        // ── 기존 SO 직접 할당 ────────────────────────────────────────
        EditorGUI.BeginChangeCheck();
        var drag = (TextTable)EditorGUILayout.ObjectField(
            "SO 할당", null, typeof(TextTable), false);
        if (EditorGUI.EndChangeCheck() && drag != null)
        {
            BindSO(drag);
            return;
        }

        EditorGUILayout.Space(6);

        // ── 새 SO 생성 / 재탐색 버튼 ─────────────────────────────────
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("새 TextTable SO 생성...", GUILayout.Width(220), GUILayout.Height(30)))
            CreateNewSO();
        GUILayout.Space(8);
        if (GUILayout.Button("↺ 재탐색", GUILayout.Width(80), GUILayout.Height(30)))
            TryFindSO();
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 헤더 (ObjectField + 재탐색 버튼)
    // ──────────────────────────────────────────────────────────────────────────
    private void DrawHeader()
    {
        GUILayout.BeginHorizontal();

        EditorGUI.BeginChangeCheck();
        var next = (TextTable)EditorGUILayout.ObjectField(
            "TextTable SO", _so, typeof(TextTable), false);
        if (EditorGUI.EndChangeCheck())
            BindSO(next);

        if (GUILayout.Button("↺", GUILayout.Width(28), GUILayout.Height(18)))
            TryFindSO();

        GUILayout.EndHorizontal();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Odin PropertyTree로 직접 그리기 (OdinEditor 경고 제외)
    // ──────────────────────────────────────────────────────────────────────────
    private void DrawInspector()
    {
        if (_tree == null)
        {
            EditorGUILayout.HelpBox("PropertyTree를 생성할 수 없습니다. SO를 다시 할당해 보세요.",
                                    MessageType.Error);
            return;
        }

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        _tree.Draw(applyUndo: true);
        EditorGUILayout.EndScrollView();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 새 SO 생성
    // ──────────────────────────────────────────────────────────────────────────
    private void CreateNewSO()
    {
        string path = EditorUtility.SaveFilePanelInProject(
            "TextTable SO 저장 위치", "TextTable", "asset",
            "TextTable ScriptableObject를 저장할 경로를 선택하세요.");

        if (string.IsNullOrEmpty(path)) return;

        var newSO = ScriptableObject.CreateInstance<TextTable>();
        AssetDatabase.CreateAsset(newSO, path);
        AssetDatabase.SaveAssets();
        BindSO(newSO);
        Debug.Log($"[TextTablePanel] 새 TextTable SO 생성: {path}");
    }
}
#endif
