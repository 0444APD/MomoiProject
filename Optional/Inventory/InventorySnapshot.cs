#if SYSTEM_INVENTORY && GAME_SYSTEMS_READY

using System;
using System.Collections.Generic;

// 하나의 인벤토리 전체를 저장하는 직렬화 구조체
[Serializable]
public class InventorySnapshot
{
    public string         inventoryId;
    public int            size;
    public List<ItemData> slots = new();
}

#endif
