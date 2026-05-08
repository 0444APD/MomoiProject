#if GAME_SYSTEMS_READY
using Sirenix.OdinInspector;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using UnityEditor.AddressableAssets;
using UnityEditor.Callbacks;
using UnityEngine;

// --- [������ ����ü] ---
[System.Serializable]
public struct TextData {
    public string ID;
    public Dictionary<string, string> Texts;

    public string GetCurrentString() {
        if (Texts != null && Texts.TryGetValue(LanguageManager.CurrentLanguage, out string text)) {
            return text;
        }
        return "";
    }
}

[System.Serializable]
public struct SheetTabInfo {
    [LabelText("�� �̸�")] public string tabName;
    [LabelText("CSV �� �Խ� ��ũ")] public string csvUrl;
}

[CreateAssetMenu(menuName = "Core/Text Table")]
public class TextTable : SerializedScriptableObject {

    [Title("���� ��Ʈ ���� (�Ϲ� ���� �ؽ�Ʈ)")]
    [ListDrawerSettings(ShowIndexLabels = true)]
    public List<SheetTabInfo> sheetTabs = new List<SheetTabInfo>();

    [Title("���� ��Ʈ ���� (Ű���� �� ���̺�)")]
    [InfoBox("1��: Ű���� (��: Gold) / 2��: ���� (��: <color=yellow>{Gold}</color>)")]
    public SheetTabInfo keywordSheetTab;

    [Title("������ ������ ������ (Bake �����)")]
    [DictionaryDrawerSettings(KeyLabel = "�� �̸�", ValueLabel = "������", IsReadOnly = true)]
    public Dictionary<string, Dictionary<string, TextData>> inspectorDB = new Dictionary<string, Dictionary<string, TextData>>();

    [DictionaryDrawerSettings(KeyLabel = "Ű����", ValueLabel = "���� ��", IsReadOnly = true)]
    public Dictionary<string, string> inspectorKeywordDB = new Dictionary<string, string>();

    // =========================================================
    // ������ �� ���� �׽�Ʈ (�̸�����)
    // =========================================================
#if UNITY_EDITOR
    [Title("���� �ð�ȭ �׽�Ʈ", "�ؽ�Ʈ ����(<color>, <b>, <i> ��)�� ��� �������Ǵ��� Ȯ���մϴ�.")]
    [TextArea(2, 5)]
    public string testInput = "<color=#FFD700>���</color>";

    [OnInspectorGUI]
    private void DrawTMPPreview() {
        if (string.IsNullOrEmpty(testInput)) return;

        // ����Ƽ ������ ���� ��ġ �ؽ�Ʈ ��Ÿ�� ����
        GUIStyle richStyle = new GUIStyle(GUI.skin.label)
        {
            richText = true,
            wordWrap = true,
            fontSize = 14
        };

        UnityEditor.EditorGUILayout.Space();
        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label(testInput, richStyle);
        GUILayout.EndVertical();
        UnityEditor.EditorGUILayout.Space();
    }
#endif

    // =========================================================
    // ��Ÿ�� ���
    // =========================================================
    private static Dictionary<string, TextData> _staticMasterDB;
    private static Dictionary<string, string> _staticKeywordRules;

    public static void InitRuntime(TextTable instance) {
        if (instance == null) {
            Debug.LogError("[TextTable] �ʱ�ȭ ����: SO �ν��Ͻ��� �����ϴ�.");
            return;
        }

        _staticMasterDB = new Dictionary<string, TextData>();
        _staticKeywordRules = instance.inspectorKeywordDB;

        foreach (var tab in instance.inspectorDB.Values) {
            foreach (var kvp in tab) {
                if (!_staticMasterDB.ContainsKey(kvp.Key)) {
                    _staticMasterDB.Add(kvp.Key, kvp.Value);
                }
            }
        }
        Debug.Log($"[TextTable] �ʱ�ȭ �Ϸ�. �� {_staticMasterDB.Count}�� �ؽ�Ʈ �ε��.");
    }

    public static string Get(string key, Dictionary<string, string> args = null) {
        if (string.IsNullOrEmpty(key) || key.Trim() == "") return "";

        if (_staticMasterDB != null && _staticMasterDB.TryGetValue(key, out TextData data)) {

            string rawText = data.GetCurrentString();
            if (string.IsNullOrEmpty(rawText)) return "[�ؽ�Ʈ �������]";

            if (args != null && args.Count > 0) {
                rawText = Regex.Replace(rawText, @"\{(.+?)\}", match => {
                    string argKey = match.Groups[1].Value;
                    return args.TryGetValue(argKey, out string val) ? val : match.Value;
                });
            }

            rawText = Regex.Replace(rawText, @"\[\[(.+?)\]\]", match => {
                string keyword = match.Groups[1].Value;

                if (_staticKeywordRules != null && _staticKeywordRules.TryGetValue(keyword, out string format)) {
                    return Regex.Replace(format, @"\{(.+?)\}", innerMatch => {
                        string innerKey = innerMatch.Groups[1].Value;
                        return GetLocalizedText(innerKey);
                    });
                }
                return GetLocalizedText(keyword);
            });

            return rawText;
        }

        return $"[{key}] �� ����";
    }

    private static string GetLocalizedText(string key) {
        if (_staticMasterDB != null && _staticMasterDB.TryGetValue(key, out TextData data)) {
            string text = data.GetCurrentString();
            return string.IsNullOrEmpty(text) ? key : text;
        }
        return key;
    }

    // =========================================================
    // ���� ��Ʈ �Ľ� (Bake)
    // =========================================================
#if UNITY_EDITOR
    [Button("���� ��Ʈ ������ �������� (Bake)", ButtonSizes.Large)]
    [GUIColor(0.4f, 0.8f, 1f)]
    private async void BakeData() {
        inspectorDB.Clear();
        if (sheetTabs.Count == 0) return;

        using (HttpClient client = new HttpClient()) {
            foreach (var tab in sheetTabs) {
                if (string.IsNullOrEmpty(tab.tabName) || string.IsNullOrEmpty(tab.csvUrl)) continue;
                try {
                    string csvData = await client.GetStringAsync(tab.csvUrl);
                    ParseCSV(tab.tabName, csvData);
                }
                catch (System.Exception e) {
                    Debug.LogError($"[TextTable] �ٿ�ε� ����: {e.Message}");
                }
            }
        }

        UnityEditor.EditorApplication.delayCall += () =>
        {
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.AssetDatabase.SaveAssets();

            // ���� �Ϸ� �� ��巹���� ��� ���� �˻�
            CheckAddressableRegistration();

            Debug.Log($"[TextTable] Bake �Ϸ�!");
        };
    }

    [DidReloadScripts]
    private static void CheckAddressableRegistration() {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) return;

        bool isRegistered = false;

        // ��� ��巹���� �׷��� ��ȸ�ϸ� 'TextTable' �ּҿ� Ÿ���� ���� ������ �ִ��� Ȯ��
        foreach (var group in settings.groups) {
            if (group == null) continue;

            foreach (var entry in group.entries) {
                if (entry.address == "TextTable" && entry.MainAssetType == typeof(TextTable)) {
                    isRegistered = true;
                    break;
                }
            }
            if (isRegistered) break;
        }

        // �߰����� ���ߴٸ� ��� �α� ���
        if (!isRegistered) {
            Debug.LogWarning("[TextTable] ��巹���� �׷쿡 'TextTable' �ּҷ� ��ϵ� TextTable ������ �����ϴ�! SystemInitializer���� �ε� �� ������ �߻��� �� �ֽ��ϴ�.");
        }
    }

    private void ParseCSV(string tabName, string csvData) {
        if (!inspectorDB.ContainsKey(tabName)) inspectorDB[tabName] = new Dictionary<string, TextData>();

        string[] lines = csvData.Split(new[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length <= 1) return;

        Regex csvParser = new Regex(",(?=(?:[^\"]*\"[^\"]*\")*(?![^\"]*\"))");
        string[] headers = csvParser.Split(lines[0]);

        Dictionary<string, int> headerMap = new Dictionary<string, int>();
        for (int i = 0; i < headers.Length; i++) headerMap[headers[i].Trim().Trim('"')] = i;

        if (!headerMap.ContainsKey("ID")) return;

        string currentGroupPrefix = "";

        for (int i = 1; i < lines.Length; i++) {
            string[] row = csvParser.Split(lines[i]);
            if (row.Length <= headerMap["ID"]) continue;

            string rawID = row[headerMap["ID"]].Trim().Trim('"');
            if (string.IsNullOrEmpty(rawID) || rawID.StartsWith("//")) continue;

            if (rawID.StartsWith("##")) {
                currentGroupPrefix = rawID.Substring(2).Trim();
                continue;
            }

            string finalID = string.IsNullOrEmpty(currentGroupPrefix) ? rawID : $"{currentGroupPrefix}_{rawID}";

            TextData newData = new TextData
            {
                ID = finalID,
                Texts = new Dictionary<string, string>()
            };

            foreach (var header in headers) {
                string colName = header.Trim().Trim('"');
                if (colName == "ID") continue;

                if (headerMap.TryGetValue(colName, out int colIndex) && row.Length > colIndex) {
                    newData.Texts[colName] = row[colIndex].Trim().Trim('"').Replace("\"\"", "\"");
                }
            }

            inspectorDB[tabName][finalID] = newData;
        }
    }

    private void ParseKeywordCSV(string csvData) {
        string[] lines = csvData.Split(new[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length <= 1) return;

        Regex csvParser = new Regex(",(?=(?:[^\"]*\"[^\"]*\")*(?![^\"]*\"))");

        for (int i = 1; i < lines.Length; i++) {
            string[] row = csvParser.Split(lines[i]);
            if (row.Length < 2) continue;

            string key = row[0].Trim().Trim('"');
            string format = row[1].Trim().Trim('"').Replace("\"\"", "\"");

            if (!string.IsNullOrEmpty(key)) {
                inspectorKeywordDB[key] = format;
            }
        }
    }
#endif
}
#endif