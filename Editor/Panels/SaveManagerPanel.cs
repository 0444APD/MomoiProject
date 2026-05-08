using UnityEditor;
using UnityEngine;
using System.IO;

public class SaveManagerPanel : ISystemPanel
{
    public string Title => "Save";
    public int Order => 10;

    private int _slotInput = 0;

    public void OnEnable() { }
    public void OnDisable() { }

    public void OnGUI()
    {
        EditorGUILayout.Space(10);
        DrawHeader();
        EditorGUILayout.Space(8);
        DrawSettings();
        EditorGUILayout.Space(8);
        DrawSlotControl();
        EditorGUILayout.Space(8);
        DrawSaveFiles();
    }

    private void DrawHeader()
    {
        EditorGUILayout.LabelField("Save Manager", EditorStyles.boldLabel);

#if NEWTONSOFT_JSON
        EditorGUILayout.HelpBox("Newtonsoft.Json 설치됨 — 정상 동작 중", MessageType.None);
#else
        EditorGUILayout.HelpBox("Newtonsoft.Json 미설치 — com.unity.nuget.newtonsoft-json 설치 필요", MessageType.Warning);
#endif
    }

    private void DrawSettings()
    {
        EditorGUILayout.LabelField("설정", EditorStyles.boldLabel);

        bool prev = SaveManager.isManualSaveLoad;
        bool next = EditorGUILayout.Toggle("수동 세이브/로드 모드", prev);
        if (next != prev)
            SaveManager.isManualSaveLoad = next;

        EditorGUILayout.HelpBox(
            SaveManager.isManualSaveLoad
                ? "수동 모드: 게임 시작 시 자동 로드를 건너뜁니다."
                : "자동 모드: 게임 시작 시 슬롯 0을 자동으로 로드합니다.",
            MessageType.Info);
    }

    private void DrawSlotControl()
    {
        EditorGUILayout.LabelField("슬롯 제어", EditorStyles.boldLabel);

        GUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("슬롯 번호", GUILayout.Width(80));
        _slotInput = EditorGUILayout.IntField(_slotInput, GUILayout.Width(60));
        GUILayout.EndHorizontal();

        EditorGUILayout.Space(4);

        GUILayout.BeginHorizontal();
        GUILayout.Space(12);

        if (GUILayout.Button("Load", GUILayout.Height(28)))
        {
            SaveManager.Load(_slotInput);
        }

        EditorGUI.BeginDisabledGroup(!Application.isPlaying);
        if (GUILayout.Button("Save", GUILayout.Height(28)))
        {
            SaveManager.Save();
        }
        EditorGUI.EndDisabledGroup();

        GUILayout.EndHorizontal();

        if (!Application.isPlaying)
            EditorGUILayout.HelpBox("Save 버튼은 플레이 모드에서만 사용할 수 있습니다.", MessageType.None);

        EditorGUILayout.Space(4);

        // 현재 상태
        if (Application.isPlaying)
        {
            EditorGUILayout.LabelField("현재 슬롯",  SaveManager.CurrentSlot.ToString(), EditorStyles.miniLabel);
            EditorGUILayout.LabelField("데이터",     SaveManager.Current != null ? "로드됨" : "없음", EditorStyles.miniLabel);
        }
    }

    private void DrawSaveFiles()
    {
        EditorGUILayout.LabelField("저장 파일", EditorStyles.boldLabel);

#if UNITY_EDITOR
        string dir = $"{Application.dataPath}/SaveData";
#else
        string dir = $"{Application.persistentDataPath}/SaveData";
#endif

        if (!Directory.Exists(dir))
        {
            EditorGUILayout.HelpBox("저장 파일 없음 (아직 한 번도 저장되지 않음)", MessageType.None);
            return;
        }

        var files = Directory.GetFiles(dir, "*.json");
        if (files.Length == 0)
        {
            EditorGUILayout.HelpBox("저장 파일 없음", MessageType.None);
            return;
        }

        foreach (var f in files)
        {
            var info = new FileInfo(f);
            GUILayout.BeginHorizontal();
            GUILayout.Space(12);
            EditorGUILayout.LabelField(info.Name, GUILayout.Width(160));
            EditorGUILayout.LabelField(
                $"{info.Length / 1024f:F1} KB  |  {info.LastWriteTime:MM/dd HH:mm}",
                EditorStyles.miniLabel);

            if (GUILayout.Button("열기", EditorStyles.miniButton, GUILayout.Width(40)))
                EditorUtility.RevealInFinder(f);

            GUILayout.EndHorizontal();
        }
    }
}
