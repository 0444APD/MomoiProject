using System;
using System.Collections.Generic;
using GBible.Data;

namespace GBible.InventorySystem
{
    // 인벤토리 한 칸: 어떤 아이템이 몇 개.
    [Serializable, PlayerData]
    public sealed class InventorySlot
    {
        [FieldLabel("아이템"), DataRef(typeof(ItemData))] public string item = "";
        [FieldLabel("개수")] public int count;
    }

    // 인벤토리: 칸의 리스트. 플레이어 데이터의 값 형식으로 쓴다(도감 › 플레이어 데이터에서 값 형식으로 고른다).
    // 플레이어 데이터 클래스라서 꺼내 그냥 고치면 된다(쓰는 순간 기록: 되돌리기·기록·저장·조건 검사).
    //   Game.All<Inventory>(GameIds.PlayerData.PlayerInventory.Id)[0].Add(potion, 3);
    [Serializable, PlayerData]
    public sealed class Inventory
    {
        [FieldLabel("칸 수(0이면 제한 없음)")] public int capacity;
        [FieldLabel("칸")] public List<InventorySlot> slots = new List<InventorySlot>();

        // 가진 개수(모든 칸의 합).
        public int CountOf(string itemId)
        {
            var total = 0;
            foreach (var slot in slots)
                if (slot != null && slot.item == itemId) total += slot.count;
            return total;
        }

        public bool Has(string itemId, int count = 1) => CountOf(itemId) >= count;

        // 넣는다: 같은 아이템의 덜 찬 칸부터 채우고, 남으면 새 칸을 만든다. 돌려주는 값: 들어가지 못하고 남은 개수(칸이 모자랄 때).
        public int Add(ItemData item, int count = 1) => item == null ? count : Add(item.Id, count, item.maxStack);

        public int Add(string itemId, int count, int maxStack)
        {
            if (string.IsNullOrEmpty(itemId) || count <= 0) return Math.Max(0, count);
            maxStack = Math.Max(1, maxStack);
            var left = count;
            foreach (var slot in slots)
            {
                if (left <= 0) break;
                if (slot == null || slot.item != itemId || slot.count >= maxStack) continue;
                var put = Math.Min(left, maxStack - slot.count);
                slot.count += put;
                left -= put;
            }
            while (left > 0 && (capacity <= 0 || slots.Count < capacity))
            {
                var put = Math.Min(left, maxStack);
                slots.Add(new InventorySlot { item = itemId, count = put });
                left -= put;
            }
            return left;
        }

        // 넣을 수 있는 개수를 미리 센다(값을 바꾸지 않는다).
        public int RoomFor(string itemId, int maxStack)
        {
            maxStack = Math.Max(1, maxStack);
            long room = 0;
            foreach (var slot in slots)
                if (slot != null && slot.item == itemId) room += Math.Max(0, maxStack - slot.count);
            if (capacity <= 0) return int.MaxValue;
            room += (long)Math.Max(0, capacity - slots.Count) * maxStack;
            return (int)Math.Min(int.MaxValue, room);
        }

        // 뺀다: 뒤쪽 칸부터 줄이고, 비게 된 칸은 없앤다. 돌려주는 값: 실제로 뺀 개수(모자라면 있는 만큼만).
        public int Remove(string itemId, int count = 1)
        {
            if (count <= 0) return 0;
            var left = count;
            for (int i = slots.Count - 1; i >= 0 && left > 0; i--)
            {
                var slot = slots[i];
                if (slot == null || slot.item != itemId) continue;
                var take = Math.Min(left, slot.count);
                slot.count -= take;
                left -= take;
                if (slot.count <= 0) slots.RemoveAt(i);
            }
            return count - left;
        }

        // 있는 만큼만 빼지 않고, 다 있을 때만 뺀다(값을 치를 때).
        public bool TryRemove(string itemId, int count = 1)
        {
            if (!Has(itemId, count)) return false;
            Remove(itemId, count);
            return true;
        }
    }
}
