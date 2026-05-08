#if UNITY_EDITOR && !Addressable
using UnityEditor;
using UnityEditor.Build;
using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine; // Debug.Log를 사용하기 위해 추가

[InitializeOnLoad]
public class AddressableSymalManager {
    private const string SymbolName = "Addressable";

    static AddressableSymalManager() {
        UpdateSymbol();
    }

    // MenuItem 속성을 추가하여 에디터 상단 메뉴에서 클릭으로 실행할 수 있게 만듭니다.
    public static void UpdateSymbol() {
        bool isInstalled = AppDomain.CurrentDomain.GetAssemblies()
            .Any(assembly => assembly.GetName().Name == "Unity.Addressables");

        BuildTargetGroup targetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;

        if (targetGroup == BuildTargetGroup.Unknown) return;

        NamedBuildTarget namedBuildTarget = NamedBuildTarget.FromBuildTargetGroup(targetGroup);

        string definesString = PlayerSettings.GetScriptingDefineSymbols(namedBuildTarget);
        List<string> allDefines = definesString.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();

        bool hasSymbol = allDefines.Contains(SymbolName);
        bool isChanged = false;

        if (isInstalled && !hasSymbol) {
            allDefines.Add(SymbolName);
            isChanged = true;
        }
        else if (!isInstalled && hasSymbol) {
            allDefines.Remove(SymbolName);
            isChanged = true;
        }

        // 수동 실행 시 결과를 콘솔에서 확인할 수 있도록 로그를 추가했습니다.
        if (isChanged) {
            PlayerSettings.SetScriptingDefineSymbols(namedBuildTarget, string.Join(";", allDefines));
            Debug.Log($"[SymbolManager] 심볼 업데이트 완료: {SymbolName} {(isInstalled ? "추가됨" : "제거됨")}");
        }
        else {
            Debug.Log($"[SymbolManager] 변경 사항이 없습니다. (현재 상태: {(isInstalled ? "설치됨" : "미설치")})");
        }
    }
}
#endif