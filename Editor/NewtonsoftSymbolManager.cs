#if UNITY_EDITOR && !NEWTONSOFT_JSON
using UnityEditor;
using UnityEditor.Build; // NamedBuildTarget을 사용하기 위해 필요합니다.
using System;
using System.Linq;
using System.Collections.Generic;

[InitializeOnLoad]
public class NewtonsoftSymbolManager {
    private const string SymbolName = "NEWTONSOFT_JSON";

    static NewtonsoftSymbolManager() {
        UpdateSymbol();
    }


    private static void UpdateSymbol() {
        // 현재 유니티 도메인에 로드된 어셈블리 중 Newtonsoft.Json이 있는지 검사
        bool isInstalled = AppDomain.CurrentDomain.GetAssemblies()
            .Any(assembly => assembly.GetName().Name == "Newtonsoft.Json");

        // 최신 유니티 권장 방식: NamedBuildTarget 사용
        BuildTargetGroup targetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;

        // BuildTargetGroup이 Unknown일 경우 예외가 발생할 수 있으므로 방어 코드 추가
        if (targetGroup == BuildTargetGroup.Unknown) return;

        NamedBuildTarget namedBuildTarget = NamedBuildTarget.FromBuildTargetGroup(targetGroup);

        // 새로운 API를 사용하여 심볼 목록 가져오기
        string definesString = PlayerSettings.GetScriptingDefineSymbols(namedBuildTarget);
        List<string> allDefines = definesString.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();

        bool hasSymbol = allDefines.Contains(SymbolName);

        // Newtonsoft가 있는데 심볼이 없으면 추가
        if (isInstalled && !hasSymbol) {
            allDefines.Add(SymbolName);
            PlayerSettings.SetScriptingDefineSymbols(namedBuildTarget, string.Join(";", allDefines));
        }
        // Newtonsoft가 없는데 심볼이 남아있으면 제거
        else if (!isInstalled && hasSymbol) {
            allDefines.Remove(SymbolName);
            PlayerSettings.SetScriptingDefineSymbols(namedBuildTarget, string.Join(";", allDefines));
        }
    }
}
#endif