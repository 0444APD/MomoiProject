#if GAME_SYSTEMS_READY
using UnityEditor;
using UnityEngine;
#if Addressable
using UnityEditor.AddressableAssets;
#endif

public class AddressablesPanel : ISystemPanel {
    public string Title => "Addressables";
    public int    Order => 5;

    private AddressableLoaderSettings _settings;
    private SerializedObject          _serialized;
    private Vector2                   _scroll;
    private bool                      _guideOpen = false;

    public void OnEnable()  => TryFindSettings();
    public void OnDisable() { }

    public void OnGUI() {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Addressables", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

#if Addressable
        DrawSettingsSection();
        EditorGUILayout.Space(8);
        DrawActionButtons();
#else
        EditorGUILayout.HelpBox(
            "Addressables 패키지가 설치되지 않았습니다.\n" +
            "Package Manager에서 'Addressables'를 설치하면 이 패널이 활성화됩니다.",
            MessageType.Warning);
#endif

        EditorGUILayout.Space(12);
        DrawGuide();
    }

    // ── Settings 섹션 ────────────────────────────────────────────
#if Addressable
    private void TryFindSettings() {
        if (_settings != null) return;
        var guids = AssetDatabase.FindAssets("t:AddressableLoaderSettings");
        if (guids.Length > 0)
            BindSettings(AssetDatabase.LoadAssetAtPath<AddressableLoaderSettings>(
                AssetDatabase.GUIDToAssetPath(guids[0])));
    }

    private void BindSettings(AddressableLoaderSettings s) {
        _settings   = s;
        _serialized = s != null ? new SerializedObject(s) : null;
    }

    private void DrawSettingsSection() {
        EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);

        // ── ObjectField ──────────────────────────────────────────
        EditorGUI.BeginChangeCheck();
        var next = (AddressableLoaderSettings)EditorGUILayout.ObjectField(
            new GUIContent("Settings Asset",
                "AddressableLoaderSettings 스크립터블 오브젝트를 여기에 연결합니다.\n" +
                "없으면 Project 우클릭 → Create → Tools → Addressable Loader Settings 로 생성하세요."),
            _settings, typeof(AddressableLoaderSettings), false);
        if (EditorGUI.EndChangeCheck()) BindSettings(next);

        // ── 미연결 안내 ──────────────────────────────────────────
        if (_settings == null) {
            EditorGUILayout.Space(4);
            GUILayout.BeginHorizontal();
            EditorGUILayout.HelpBox("Settings Asset이 연결되지 않았습니다.", MessageType.Info);
            GUILayout.BeginVertical(GUILayout.Width(90));
            GUILayout.Space(4);
            if (GUILayout.Button("자동 탐색", GUILayout.Height(30)))
                TryFindSettings();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            return;
        }

        // ── 폴더 목록 (SerializedObject로 인-패널 편집) ──────────
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField(new GUIContent(
            "대상 루트 폴더",
            "등록할 어드레서블 에셋이 들어 있는 루트 폴더 경로 목록입니다.\n" +
            "폴더 구조 규칙: 루트폴더 / 라벨폴더 / 에셋파일  (2단계 고정)"),
            EditorStyles.boldLabel);

        _serialized.Update();

        var prop = _serialized.FindProperty("targetRootFolders");
        _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(140));
        EditorGUILayout.PropertyField(prop, GUIContent.none, true);
        EditorGUILayout.EndScrollView();

        if (_serialized.ApplyModifiedProperties())
            EditorUtility.SetDirty(_settings);

        // ── 폴더 구조 규칙 요약 ──────────────────────────────────
        EditorGUILayout.Space(4);
        var ruleStyle = new GUIStyle(EditorStyles.helpBox) { fontSize = 10 };
        EditorGUILayout.LabelField(
            "Assets/루트폴더/\n" +
            "  ├─ 라벨A/  ← 이 폴더명이 어드레서블 레이블이 됨\n" +
            "  │    ├─ texture1.png  (address: \"texture1\")\n" +
            "  │    └─ texture2.png\n" +
            "  └─ 라벨B/\n" +
            "       └─ audio1.wav",
            ruleStyle);
    }

    private void DrawActionButtons() {
        GUILayout.BeginHorizontal();

        bool hasSettings   = _settings != null && _settings.targetRootFolders.Count > 0;
        bool hasAddrGroups = AddressableAssetSettingsDefaultObject.Settings != null;

        GUI.enabled = hasSettings && hasAddrGroups;
        if (GUILayout.Button(new GUIContent(
                "Addressables 업데이트",
                hasSettings && hasAddrGroups
                    ? "설정된 폴더를 기준으로 어드레서블 그룹·레이블·에셋을 자동 등록합니다."
                    : hasAddrGroups
                        ? "대상 루트 폴더를 먼저 설정하세요."
                        : "Window > Asset Management > Addressables > Groups 를 먼저 열어 초기화하세요."),
                GUILayout.Height(32)))
            AddressableLoader.UpdateAddressables();
        GUI.enabled = true;

        if (GUILayout.Button(new GUIContent(
                "Groups 창 열기",
                "Addressables Groups 관리 창을 엽니다."),
                GUILayout.Width(100), GUILayout.Height(32)))
            EditorApplication.ExecuteMenuItem("Window/Asset Management/Addressables/Groups");

        GUILayout.EndHorizontal();

        if (!hasAddrGroups)
            EditorGUILayout.HelpBox(
                "AddressableAssetSettings가 없습니다. Groups 창을 열어 초기화하세요.",
                MessageType.Warning);
    }
#else
    private void TryFindSettings() { }
#endif

    // ── 가이드 (접기/펼치기) ─────────────────────────────────────
    private void DrawGuide() {
        _guideOpen = EditorGUILayout.Foldout(_guideOpen, "로드 문법 및 주의사항", true,
                                             EditorStyles.foldoutHeader);
        if (!_guideOpen) return;

        EditorGUI.indentLevel++;

        DrawCodeSection("① 비동기 — Completed 콜백 (권장)",
            "var handle = Addressables.LoadAssetAsync<Sprite>(\"PlayerIcon\");\n" +
            "handle.Completed += op => {\n" +
            "    if (op.Status == AsyncOperationStatus.Succeeded)\n" +
            "        image.sprite = op.Result;\n" +
            "};");

        DrawCodeSection("② 비동기 — 코루틴",
            "IEnumerator LoadSprite() {\n" +
            "    var handle = Addressables.LoadAssetAsync<Sprite>(\"PlayerIcon\");\n" +
            "    yield return handle;          // 로드 완료까지 코루틴만 정지\n" +
            "    image.sprite = handle.Result;\n" +
            "}");

        DrawCodeSection("③ 비동기 — async / await",
            "// MonoBehaviour 메서드에서는 사용 불가.\n" +
            "// 일반 async Task 메서드에서만 동작.\n" +
            "async Task LoadSprite() {\n" +
            "    var sprite = await Addressables\n" +
            "        .LoadAssetAsync<Sprite>(\"PlayerIcon\").Task;\n" +
            "    image.sprite = sprite;\n" +
            "}");

        DrawCodeSection("④ 동기 — WaitForCompletion  ⚠ 블로킹 주의",
            "var handle = Addressables.LoadAssetAsync<Sprite>(\"PlayerIcon\");\n" +
            "Sprite sprite = handle.WaitForCompletion();  // ← 여기서 블로킹\n" +
            "image.sprite = sprite;");

        // 블로킹 범위 강조 박스
        var warnStyle = new GUIStyle(EditorStyles.helpBox);
        warnStyle.normal.textColor = new Color(0.95f, 0.80f, 0.30f);
        EditorGUILayout.LabelField(
            "⚠  WaitForCompletion 블로킹 범위\n" +
            "  • 호출 시점부터 로드 완료까지 메인 스레드 전체가 정지됩니다.\n" +
            "  • Update / FixedUpdate / 렌더링 / 입력 처리 모두 중단.\n" +
            "  • 로컬 번들: 수십 ms / 리모트 번들: 수 초 이상 가능.\n" +
            "  • 씬 전환 직전, 게임 최초 초기화 등 1회성 상황에만 사용하세요.",
            warnStyle);

        DrawCodeSection("⑤ 에셋 해제 — 반드시 Release 호출",
            "// 핸들로 해제\n" +
            "Addressables.Release(handle);\n\n" +
            "// 로드된 에셋 참조로 해제 (handle 보관 없이 사용 시)\n" +
            "Addressables.Release(sprite);\n\n" +
            "// 주의: Release 누락 시 레퍼런스 카운트가 남아 메모리 누수 발생.");

        EditorGUI.indentLevel--;
    }

    private void DrawCodeSection(string title, string code) {
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        EditorGUILayout.TextArea(code,
            new GUIStyle(EditorStyles.helpBox) { fontSize = 11, wordWrap = false });
    }
}
#endif
