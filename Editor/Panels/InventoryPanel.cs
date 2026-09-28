// #if SYSTEM_INVENTORY 없음 — 비활성 상태에서도 Dashboard에 항상 표시

using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
#if Addressable
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
#endif

public class InventoryPanel : ISystemPanel
{
    public string Title => "Inventory";
    public int    Order => 50;

    private Vector2 _scroll;
    const string Symbol      = "SYSTEM_INVENTORY";
    const string ReadySymbol = "GAME_SYSTEMS_READY";

    public void OnEnable()  { }
    public void OnDisable() { }

    public void OnGUI()
    {
        DrawAddonHeader();
        if (!IsSymbolDefined(Symbol)) return;

#if !Addressable
        EditorGUILayout.HelpBox("Inventory System은 Addressables 패키지를 필요로 합니다.", MessageType.Error);
        return;
#endif

#if SYSTEM_INVENTORY && GAME_SYSTEMS_READY
        EditorGUILayout.Space(4);
        DrawControls();
#endif
    }

    // ── 애드온 헤더 (항상 표시) ──────────────────────────────────────────
    void DrawAddonHeader()
    {
        bool isReady  = IsSymbolDefined(ReadySymbol);
        bool isActive = IsSymbolDefined(Symbol);

        // GAME_SYSTEMS_READY가 없어진 경우 SYSTEM_INVENTORY도 자동 해제
        if (!isReady && isActive)
        {
            SetSymbol(Symbol, false);
            isActive = false;
        }

        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Inventory Addon",
            new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 });
        GUILayout.FlexibleSpace();

        EditorGUI.BeginDisabledGroup(!isReady);
        bool next = EditorGUILayout.Toggle(isActive, GUILayout.Width(20));
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.EndHorizontal();

        if (!isReady)
        {
            EditorGUILayout.HelpBox(
                "GAME_SYSTEMS_READY 심볼이 필요합니다. " +
                "Home 탭에서 필수 패키지를 모두 설치하세요.",
                MessageType.Warning);
            return;
        }

        if (next != isActive) SetSymbol(Symbol, next);

        if (!isActive)
            EditorGUILayout.HelpBox(
                "비활성화 상태. 토글을 켜면 SYSTEM_INVENTORY 심볼이 추가됩니다.",
                MessageType.Info);
    }

#if SYSTEM_INVENTORY && GAME_SYSTEMS_READY
    // ── 활성화 시 컨트롤 ────────────────────────────────────────────────
    void DrawControls()
    {
        // 스택 시스템 토글
        EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
        bool prev = InventorySystem.UseStacking;
        bool next = EditorGUILayout.Toggle("Stack System", prev);
        if (next != prev) InventorySystem.UseStacking = next;

        EditorGUILayout.Space(6);

#if Addressable
        // SlotUI 프리팹 생성 버튼
        EditorGUILayout.LabelField("Setup", EditorStyles.boldLabel);
        if (GUILayout.Button("SlotUI 기본 프리팹 생성 & Addressables 등록"))
            CreateSlotUIPrefab();
        EditorGUILayout.LabelField(
            $"SlotUI Address: {InventorySystem.SlotUIPrefabAddress}",
            EditorStyles.miniLabel);
        EditorGUILayout.Space(6);
#endif

        // 런타임 인벤토리 현황
        EditorGUILayout.LabelField("Registered Inventories", EditorStyles.boldLabel);
        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Play mode에서만 표시됩니다.", MessageType.Info);
            return;
        }

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        foreach (var (id, inv) in InventorySystem.Instances)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                $"[{id}]  ({inv.Size} slots, UI: {inv.HasUI})",
                EditorStyles.boldLabel);

            for (int i = 0; i < inv.Size; i++)
            {
                var item = inv.Slots[i];
                if (item == null || item.IsEmpty) continue;
                string name    = item.itemId;
                string details = item.IsGaugeType       ? $"gauge {item.gauge:P0}"
                               : item.MaxStack > 1      ? $"x{item.currentStack}/{item.MaxStack}"
                               : string.Empty;
                EditorGUILayout.LabelField($"  [{i}]  {name}  {details}");
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }
        EditorGUILayout.EndScrollView();
    }

#if Addressable
    // ── 프리팹 생성 & Addressables 등록 ──────────────────────────────────
    static void CreateSlotUIPrefab()
    {
        const string dir  = "Assets";
        const string path = dir + "/SlotUI_Default.prefab";

        // ── 계층 구성 ────────────────────────────────────────────────
        var root = new GameObject("SlotUI_Default");
        root.AddComponent<RectTransform>().sizeDelta = new Vector2(80, 80);

        // 정적 배경 (마스크 밖)
        MakeChild<Image>(root, "Background", new Vector2(80, 80), Vector2.zero);

        // 마스크 컨테이너 — 투명 배경, 자식 UI 클리핑 전용
        var maskGo   = new GameObject("MaskContainer");
        maskGo.transform.SetParent(root.transform, false);
        var maskRect = maskGo.AddComponent<RectTransform>();
        maskRect.sizeDelta        = new Vector2(80, 80);
        maskRect.anchoredPosition = Vector2.zero;
        var maskImg = maskGo.AddComponent<Image>();
        maskImg.color = Color.white;
        var mask = maskGo.AddComponent<UnityEngine.UI.Mask>();
        mask.showMaskGraphic = false;

        // 마스크 내 자식들
        var iconImg  = MakeChild<Image>          (maskGo, "Icon",       new Vector2(60, 60), Vector2.zero);
        var stackTMP = MakeChild<TextMeshProUGUI>(maskGo, "StackText",  new Vector2(36, 20), new Vector2(22, -22));
        var priceTMP = MakeChild<TextMeshProUGUI>(maskGo, "PriceText",  new Vector2(60, 16), new Vector2(0, -32));

        var gaugeGo   = new GameObject("Gauge");
        gaugeGo.transform.SetParent(maskGo.transform, false);
        var gaugeRect = gaugeGo.AddComponent<RectTransform>();
        gaugeRect.sizeDelta        = new Vector2(70, 8);
        gaugeRect.anchoredPosition = new Vector2(0, -34);
        var gauge = gaugeGo.AddComponent<Slider>();

        // 하이라이트 — 마스크 위 반투명 어두운 오버레이
        var hlImg = MakeChild<Image>(root, "Highlight", new Vector2(80, 80), Vector2.zero);
        hlImg.color = new Color(0f, 0f, 0f, 0.35f);
        hlImg.gameObject.SetActive(false);

        // SlotUI 컴포넌트 + 직렬화 참조 연결
        var slotUI = root.AddComponent<SlotUI>();
        var so     = new SerializedObject(slotUI);
        so.FindProperty("_icon")      .objectReferenceValue = iconImg;
        so.FindProperty("_stackText") .objectReferenceValue = stackTMP;
        so.FindProperty("_priceText") .objectReferenceValue = priceTMP;
        so.FindProperty("_gauge")     .objectReferenceValue = gauge;
        so.FindProperty("_highlight") .objectReferenceValue = hlImg.gameObject;
        so.ApplyModifiedPropertiesWithoutUndo();

        // ── 프리팹 저장 ──────────────────────────────────────────────
        System.IO.Directory.CreateDirectory(dir);
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        UnityEngine.Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (prefab == null)
        {
            Debug.LogError("[InventoryPanel] 프리팹 저장 실패");
            return;
        }

        // ── Addressables 등록 ────────────────────────────────────────
        var addrSettings = AddressableAssetSettingsDefaultObject.Settings;
        if (addrSettings == null)
        {
            Debug.LogError("[InventoryPanel] Addressable Settings 없음. " +
                           "Window > Asset Management > Addressables > Groups에서 초기화하세요.");
            return;
        }

        string guid  = AssetDatabase.AssetPathToGUID(path);
        var    entry = addrSettings.CreateOrMoveEntry(guid, addrSettings.DefaultGroup);
        entry.address = InventorySystem.SlotUIPrefabAddress;
        addrSettings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true);
        AssetDatabase.SaveAssets();

        Debug.Log($"[InventoryPanel] 완료 — 경로: {path}  /  Address: {InventorySystem.SlotUIPrefabAddress}");
        EditorGUIUtility.PingObject(prefab);
    }

    static T MakeChild<T>(GameObject parent, string name, Vector2 size, Vector2 pos)
        where T : Component
    {
        var go   = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        var rect = go.AddComponent<RectTransform>();
        rect.sizeDelta        = size;
        rect.anchoredPosition = pos;
        return go.AddComponent<T>();
    }
#endif // Addressable
#endif // SYSTEM_INVENTORY

    // ── Define Symbol 유틸 ────────────────────────────────────────────────
    static bool IsSymbolDefined(string symbol)
    {
        var target  = NamedBuildTarget.FromBuildTargetGroup(
                          EditorUserBuildSettings.selectedBuildTargetGroup);
        var defines = PlayerSettings.GetScriptingDefineSymbols(target)
                          .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        return defines.Contains(symbol);
    }

    static void SetSymbol(string symbol, bool enable)
    {
        var target  = NamedBuildTarget.FromBuildTargetGroup(
                          EditorUserBuildSettings.selectedBuildTargetGroup);
        var defines = PlayerSettings.GetScriptingDefineSymbols(target)
                          .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                          .ToList();

        if (enable) { if (!defines.Contains(symbol)) defines.Add(symbol); }
        else        { defines.Remove(symbol); }

        PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines));
    }
}
