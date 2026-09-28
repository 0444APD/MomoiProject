#if SYSTEM_INVENTORY && GAME_SYSTEMS_READY

using System.Collections.Generic;

// SaveData partial 확장 — 인벤토리 세이브 슬롯
// 키: inventoryId (예: "player", "warehouse", "corpse_123")
public partial class SaveData
{
    public List<InventorySnapshot> inventories = new();
}

#endif
