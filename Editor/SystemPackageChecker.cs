using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using System;
using System.Linq;
using System.Collections.Generic;
#if Addressable
using UnityEditor.AddressableAssets;
#endif

/// <summary>
/// 필수 패키지 설치 여부를 일괄 검증하고 GAME_SYSTEMS_READY 심볼을 자동 추가/제거합니다.
/// 모든 패키지가 처음 갖춰진 시점에 초회 초기화도 수행합니다.
///
/// ▸ 새 필수 패키지 추가 : s_requiredSymbols 배열에 심볼명 한 줄 추가.
/// ▸ 새 초기화 항목 추가 : RunFirstTimeSetup() 안에 메서드 호출 한 줄 추가.
/// </summary>
[InitializeOnLoad]
public static class SystemPackageChecker
{
    // ──────────────────────────────────────────────────────────────────────────
    // 필수 패키지 심볼 목록                       ← 여기에 추가하여 확장
    // 각 패키지가 설치 시 자동으로 등록하는 스크립팅 정의 심볼을 기준으로 합니다.
    // ──────────────────────────────────────────────────────────────────────────
    private static readonly string[] s_requiredSymbols = new[]
    {
        "ODIN_INSPECTOR",   // Odin Inspector    (Odin이 자동 등록)
        "NEWTONSOFT_JSON",  // Newtonsoft.Json   (NewtonsoftSymbolManager 등록)
        "Addressable",      // Addressables      (AdressableSymbolManager 등록)
        "DOTWEEN",          // DOTween           (DOTween이 자동 등록)
    };

    // 통합 심볼명 — 이 심볼 하나로 모든 파일에서 #if GAME_SYSTEMS_READY 사용 가능
    private const string k_symbol = "GAME_SYSTEMS_READY";

    // 에디터 세션당 최초 1회 초기화 보장 키
    private const string k_sessionKey = "GameSystems_InitDone_v1";

    /// <summary>모든 필수 패키지가 현재 로드된 상태이면 true.</summary>
    public static bool AllInstalled { get; private set; }

    static SystemPackageChecker()
    {
        EditorApplication.delayCall += () => CheckAndInit();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 검증 + 심볼 관리 + 초회 초기화   (메뉴에서 수동 재실행도 가능)
    // ──────────────────────────────────────────────────────────────────────────
    [MenuItem("Game Systems/패키지 초기화 재실행", false, 200)]
    public static void CheckAndInitForced() => CheckAndInit(forceSetup: true);

    private static void CheckAndInit(bool forceSetup = false)
    {
        BuildTargetGroup group = EditorUserBuildSettings.selectedBuildTargetGroup;
        if (group == BuildTargetGroup.Unknown) return;

        NamedBuildTarget target = NamedBuildTarget.FromBuildTargetGroup(group);
        HashSet<string> currentDefines = new HashSet<string>(
            PlayerSettings.GetScriptingDefineSymbols(target)
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));

        AllInstalled = s_requiredSymbols.All(sym => currentDefines.Contains(sym));

        bool symbolChanged = UpdateSymbol(AllInstalled, currentDefines, target);

        if (!AllInstalled)
        {
            string missing = string.Join(", ",
                s_requiredSymbols.Where(sym => !currentDefines.Contains(sym)));
            Debug.LogWarning(
                $"[SystemPackageChecker] 미설치 패키지: {missing}\n" +
                "Game Systems Dashboard > Home 탭에서 패키지 상태를 확인하세요.");
            return;
        }

        // 자동 실행: 세션당 최초 1회 또는 심볼이 새로 추가된 직후에만 초기화
        // 수동 실행(forceSetup): 세션 키 무시하고 항상 초기화
        if (!forceSetup && !symbolChanged && SessionState.GetBool(k_sessionKey, false)) return;
        SessionState.SetBool(k_sessionKey, true);

        RunFirstTimeSetup();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GAME_SYSTEMS_READY 심볼 추가 / 제거
    // ──────────────────────────────────────────────────────────────────────────
    private static bool UpdateSymbol(bool allInstalled, HashSet<string> currentDefines, NamedBuildTarget target)
    {
        List<string> defines = currentDefines.ToList();
        bool hasSymbol = defines.Contains(k_symbol);

        if (allInstalled && !hasSymbol)
        {
            defines.Add(k_symbol);
            PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines));
            Debug.Log($"[SystemPackageChecker] 심볼 추가: {k_symbol}");
            return true;
        }

        if (!allInstalled && hasSymbol)
        {
            defines.Remove(k_symbol);
            PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines));
            Debug.LogWarning($"[SystemPackageChecker] 심볼 제거: {k_symbol}");
            return true;
        }

        return false;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 초회 초기화 항목 목록                       ← 여기에 추가하여 확장
    // ──────────────────────────────────────────────────────────────────────────
    private static void RunFirstTimeSetup()
    {
        Debug.Log("[SystemPackageChecker] 모든 필수 패키지 감지. 초회 초기화 시작.");
        EnsureDefaultAddressableGroup();   // 어드레서블 기본 그룹 생성
        EnsureTextTableInAddressables();   // TextTable SO 어드레서블 등록
        Debug.Log("[SystemPackageChecker] 초회 초기화 완료.");
    }

    // ── 어드레서블 기본 그룹 생성 ──────────────────────────────────────────────
    private static void EnsureDefaultAddressableGroup()
    {
#if Addressable
        var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
        if (settings == null)
        {
            Debug.LogWarning("[SystemPackageChecker] AddressableAssetSettings를 가져올 수 없습니다.");
            return;
        }

        const string groupName = "GameSystems Default";
        if (settings.groups.Any(g => g != null && g.name == groupName)) return;

        var schemasToClone = settings.DefaultGroup?.Schemas
            .OfType<UnityEditor.AddressableAssets.Settings.AddressableAssetGroupSchema>()
            .ToList();

        settings.CreateGroup(groupName,
            setAsDefaultGroup: false,
            readOnly:          false,
            postEvent:         true,
            schemasToCopy:     schemasToClone);

        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        Debug.Log($"[SystemPackageChecker] 어드레서블 그룹 '{groupName}' 생성 완료.");
#endif
    }

    // ── TextTable SO → 어드레서블 'TextTable' 주소 등록 ───────────────────────
    private static void EnsureTextTableInAddressables()
    {
#if Addressable
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) return;

        foreach (var group in settings.groups)
        {
            if (group == null) continue;
            if (group.entries.Any(e => e.address == "TextTable")) return;
        }

        string[] guids = AssetDatabase.FindAssets("t:TextTable");
        if (guids.Length == 0) return;

        string assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
        var entry = settings.CreateOrMoveEntry(guids[0], settings.DefaultGroup);
        if (entry == null) return;

        entry.address = "TextTable";
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        Debug.Log($"[SystemPackageChecker] TextTable SO를 어드레서블 'TextTable'로 등록: {assetPath}");
#endif
    }
}
