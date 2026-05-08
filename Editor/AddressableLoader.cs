#if UNITY_EDITOR && Addressable
using UnityEngine;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using System.IO;
using System.Linq;

public class AddressableLoader {
    [MenuItem("Tools/Update Table Addressables")]
    public static void UpdateAddressables() {
        string[] settingGuids = AssetDatabase.FindAssets("t:AddressableLoaderSettings");

        if (settingGuids.Length == 0) {
            Debug.LogError("[AddressableLoader] AddressableLoaderSettings 에셋을 찾을 수 없습니다. Project 창에서 우클릭 -> Create -> Tools -> Addressable Loader Settings를 만들어 경로를 세팅해 주세요.");
            return;
        }

        var settingsData = AssetDatabase.LoadAssetAtPath<AddressableLoaderSettings>(AssetDatabase.GUIDToAssetPath(settingGuids[0]));
        if (settingsData.targetRootFolders.Count == 0) {
            Debug.LogWarning("[AddressableLoader] 등록된 루트 폴더 경로가 없습니다. 세팅 에셋을 확인해 주세요.");
            return;
        }

        bool hasError = false;

        // 엄격한 계층 구조 검사 및 문제 파일 상세 로깅
        foreach (string rootFolder in settingsData.targetRootFolders) {
            if (!AssetDatabase.IsValidFolder(rootFolder)) {
                Debug.LogError($"[AddressableLoader] 유효하지 않은 폴더 경로입니다: {rootFolder}");
                hasError = true;
                continue;
            }

            // [규칙 1] 루트 폴더 바로 아래에는 폴더(라벨)만 있어야 함
            var filesInRoot = Directory.GetFiles(rootFolder).Where(f => !f.EndsWith(".meta")).ToArray();
            if (filesInRoot.Length > 0) {
                foreach (var file in filesInRoot) {
                    string cleanPath = file.Replace("\\", "/");
                    Debug.LogError($"[AddressableLoader] 그룹(루트) 폴더 바로 아래에 파일이 존재합니다. 파일은 반드시 라벨 폴더 안에 넣어주세요.\n문제 파일: {cleanPath}");
                }
                hasError = true;
            }

            // [규칙 2] 라벨 폴더 안에는 파일만 있어야 함 (하위 폴더 존재 시 에러)
            var labelFolders = Directory.GetDirectories(rootFolder);
            foreach (var labelFolder in labelFolders) {
                var subFolders = Directory.GetDirectories(labelFolder);
                if (subFolders.Length > 0) {
                    foreach (var subFolder in subFolders) {
                        string cleanPath = subFolder.Replace("\\", "/");
                        Debug.LogError($"[AddressableLoader] 라벨 폴더 안에 또 다른 하위 폴더가 존재합니다. 2중첩 구조까지만 허용됩니다.\n문제 폴더: {cleanPath}");
                    }
                    hasError = true;
                }
            }
        }

        if (hasError) {
            Debug.LogError("[AddressableLoader] 계층 구조 규칙 위반이 발견되어 어드레서블 등록을 중단합니다. 위 에러 로그들을 확인해 주세요.");
            return;
        }

        // 3. 어드레서블 세팅 에셋 가져오기
        AddressableAssetSettings addressableSettings = AddressableAssetSettingsDefaultObject.Settings;
        if (addressableSettings == null) {
            Debug.LogError("[AddressableLoader] AddressableAssetSettings를 찾을 수 없습니다. Window > Asset Management > Addressables > Groups 창을 열어주세요.");
            return;
        }

        string logResult = "[어드레서블 자동 등록 결과]\n";
        int totalFiles = 0;

        // 4. 구조를 기반으로 어드레서블 그룹/라벨/주소 세팅
        foreach (string rootFolder in settingsData.targetRootFolders) {
            string groupName = Path.GetFileName(rootFolder);

            AddressableAssetGroup targetGroup = addressableSettings.FindGroup(groupName);
            if (targetGroup == null) {
                targetGroup = addressableSettings.CreateGroup(groupName, false, false, false, addressableSettings.DefaultGroup.Schemas, typeof(BundledAssetGroupSchema));
            }

            var labelFolders = Directory.GetDirectories(rootFolder);
            int groupFileCount = 0;

            foreach (string labelDir in labelFolders) {
                string labelName = Path.GetFileName(labelDir);
                addressableSettings.AddLabel(labelName);

                string unityLabelPath = labelDir.Replace("\\", "/");
                string[] assetGuids = AssetDatabase.FindAssets("", new[] { unityLabelPath });

                foreach (string guid in assetGuids) {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);

                    if (AssetDatabase.IsValidFolder(assetPath)) continue;

                    AddressableAssetEntry entry = addressableSettings.CreateOrMoveEntry(guid, targetGroup, readOnly: false, postEvent: false);

                    if (entry != null) {
                        entry.SetAddress(Path.GetFileNameWithoutExtension(assetPath));
                        entry.labels.Clear();
                        entry.SetLabel(labelName, true, true, false);

                        groupFileCount++;
                        totalFiles++;
                    }
                }
            }

            if (groupFileCount > 0) {
                logResult += $"[{groupName} 그룹] : {groupFileCount}개 파일 등록 완료\n";
            }
        }

        logResult += $"총 {totalFiles}개의 에셋이 처리되었습니다.";
        Debug.Log(logResult);

        EditorUtility.SetDirty(addressableSettings);
        AssetDatabase.SaveAssets();
    }
}
#endif