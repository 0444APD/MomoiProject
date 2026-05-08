#if GAME_SYSTEMS_READY
using System.IO;
using UnityEngine;
using System.Collections.Generic;

#if NEWTONSOFT_JSON
using Newtonsoft.Json;

#endif

#if UNITY_EDITOR
using UnityEditor;
#endif

[System.Serializable]
public partial class SaveData {
}

public static class SaveManager {
    public static SaveData Current { get;  set; }
    public static int CurrentSlot { get; private set; } = -1;

    public static bool isManualSaveLoad = false;

    private static string GetSavePath(int slot) {
#if UNITY_EDITOR
        string directory = $"{Application.dataPath}/SaveData";
#else
        string directory = $"{Application.persistentDataPath}/SaveData";
#endif

        if (!System.IO.Directory.Exists(directory)) {
            System.IO.Directory.CreateDirectory(directory);
        }

        return $"{directory}/SaveData_{slot}.json";
    }

    public static void Load(int slot = 0) {
#if NEWTONSOFT_JSON
        string path = GetSavePath(slot);
        CurrentSlot = slot;

        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            Current = JsonConvert.DeserializeObject<SaveData>(json);
            Debug.Log($"[SaveManager] ���� {slot} �ε� �Ϸ�.");
        }
        else
        {
            Current = new SaveData();
            Save(); 
        }
#endif
    }

    public static void Save() {
#if NEWTONSOFT_JSON
        if (Current == null || CurrentSlot < 0)
        {
            Debug.LogWarning("[SaveManager] �ε�� ���̺� �����Ͱ� ���� ������ �� �����ϴ�.");
            return;
        }

        string json = JsonConvert.SerializeObject(Current, Formatting.Indented);
        File.WriteAllText(GetSavePath(CurrentSlot), json);
        Debug.Log($"[SaveManager] ���� {CurrentSlot}�� ������ ���� �Ϸ�.");
#endif
    }

#if UNITY_EDITOR

    [MenuItem("Tools/Save System/Load Slot 0")]
    public static void EditorLoadSlotZero() {
        Load(0);
        Debug.Log("[SaveManager-Editor] 0�� ���� ���� �ε� �Ϸ�.");
    }

    [MenuItem("Tools/Save System/Save Slot 0")]
    public static void EditorSaveSlotZero() {
        if (!Application.isPlaying) {
            Debug.LogWarning("[SaveManager-Editor] ������ �÷��� ��忡�� Current �����Ͱ� ������ ���� �����մϴ�.");
            return;
        }

        int previousSlot = CurrentSlot;
        CurrentSlot = 0;
        Save();
        CurrentSlot = previousSlot;
        Debug.Log("[SaveManager-Editor] 0�� ���� ���� ���� �Ϸ�.");
    }
#endif
}
#endif