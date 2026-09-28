#if SYSTEM_INVENTORY && GAME_SYSTEMS_READY
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;

public static class InventorySystem
{
    // Addressables에 등록된 SlotUI 프리팹 주소 (InventoryPanel의 생성 버튼이 이 주소로 등록함)
    public const string SlotUIPrefabAddress = "Inventory/SlotUI";

    public static bool UseStacking { get; set; } = true;

    public static IReadOnlyDictionary<string, Inventory> Instances => _instances;

    private static readonly Dictionary<string, ItemSO>    _soRegistry = new();
    private static readonly Dictionary<string, Inventory> _instances  = new();

    // ── 초기화 ─────────────────────────────────────────────────────────
    // SaveManager.Load() 이후, 씬 로드 전에 호출
    public static async Task InitializeAsync()
    {
        _soRegistry.Clear();

        var handle = Addressables.LoadAssetsAsync<ItemSO>("Items", RegisterSO);
        await handle.Task;

        if (SaveManager.Current != null)
            LinkAllSOs(SaveManager.Current.inventories);
    }

    // ── 조회 ────────────────────────────────────────────────────────────
    public static ItemSO    GetSO       (string id) => _soRegistry.TryGetValue(id, out var so)  ? so  : null;
    public static Inventory GetInventory(string id) => _instances .TryGetValue(id, out var inv) ? inv : null;

    // ── 인벤토리 등록/해제 (Inventory 생성·파괴 시 자동 호출) ────────────
    public static void Register  (Inventory inv) => _instances[inv.Id] = inv;
    public static void Unregister(Inventory inv) => _instances.Remove(inv.Id);

    // ── SaveData 연동 ────────────────────────────────────────────────────
    public static void LinkAllSOs(List<InventorySnapshot> snapshots)
    {
        foreach (var snap in snapshots)
            foreach (var item in snap.slots)
                if (!item.IsEmpty)
                    item.LinkSO(GetSO(item.itemId));
    }

    // 현재 모든 Inventory를 SaveData에 기록
    public static void FlushToSaveData()
    {
        if (SaveManager.Current == null) return;
        SaveManager.Current.inventories.Clear();
        foreach (var inv in _instances.Values)
            SaveManager.Current.inventories.Add(inv.ToSnapshot());
    }

    static void RegisterSO(ItemSO so)
    {
        if (!string.IsNullOrEmpty(so.id))
            _soRegistry[so.id] = so;
    }
}

#endif
