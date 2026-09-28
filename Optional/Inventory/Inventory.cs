#if SYSTEM_INVENTORY && GAME_SYSTEMS_READY

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public class Inventory
{
    public string     Id    { get; private set; }
    public int        Size  { get; private set; }
    public ItemData[] Slots { get; private set; }

    private Transform                    _slotParent;
    private SlotUI[]                     _slotUIs;
    private GameObject                   _slotPrefab;
    private AsyncOperationHandle<GameObject> _prefabHandle;

    public bool HasUI => _slotParent != null && _slotUIs != null;

    // ── 초기화 (데이터만, UI는 SetSlotParent로 별도 지정) ──────────────
    public void Initialize(string id, int size)
    {
        Id    = id;
        Size  = size;
        Slots = new ItemData[size];
        for (int i = 0; i < size; i++)
            Slots[i] = ItemData.Empty;
        InventorySystem.Register(this);
    }

    // ── 슬롯 Parent 지정 (언제든 호출 가능) ─────────────────────────────
    // 호출 즉시 Addressables에서 SlotUI 프리팹 로드 → 인스턴스 생성 → UI 업데이트
    public async Task SetSlotParent(Transform parent)
    {
        ClearUI();
        if (_prefabHandle.IsValid()) Addressables.Release(_prefabHandle);

        _slotParent   = parent;
        _prefabHandle = Addressables.LoadAssetAsync<GameObject>(InventorySystem.SlotUIPrefabAddress);
        _slotPrefab   = await _prefabHandle.Task;

        BuildUI();
    }

    // 슬롯 Parent 제거 및 UI 파괴
    public void RemoveSlotParent()
    {
        ClearUI();
        _slotParent = null;
        if (_prefabHandle.IsValid()) Addressables.Release(_prefabHandle);
        _slotPrefab = null;
    }

    // ── 슬롯 타입 필터 ───────────────────────────────────────────────────
    public void SetSlotFilter(int index, ItemType filter)
    {
        if (HasUI && index < _slotUIs.Length)
            _slotUIs[index].AllowedType = filter;
    }

    // ── 리사이즈 체크 ────────────────────────────────────────────────────
    // 확장이거나 축소 시 아이템 손실이 없으면 true / overflowCount = 0
    // 아이템 손실 발생 시 false / overflowCount = 넘치는 아이템 수
    public bool CheckResize(int newSize, out int overflowCount)
    {
        if (newSize >= Size) { overflowCount = 0; return true; }

        int itemsInExcess = 0;
        for (int i = newSize; i < Size; i++)
            if (Slots[i] != null && !Slots[i].IsEmpty) itemsInExcess++;

        int emptyInRemaining = 0;
        for (int i = 0; i < newSize; i++)
            if (Slots[i] == null || Slots[i].IsEmpty) emptyInRemaining++;

        overflowCount = Mathf.Max(0, itemsInExcess - emptyInRemaining);
        return overflowCount == 0;
    }

    // ── 강제 리사이즈 ────────────────────────────────────────────────────
    // 확장: 빈 슬롯 추가 (HasUI면 SlotUI도 생성)
    // 축소: 초과 슬롯 아이템 → 앞 빈칸 이동 → 남으면 overflow로 반환 + 인벤토리에서 제거
    public void Resize(int newSize, out List<ItemData> overflow)
    {
        overflow = new List<ItemData>();
        if (newSize == Size) return;

        int oldSize = Size;

        if (newSize > oldSize)
        {
            var expanded = new ItemData[newSize];
            Slots.CopyTo(expanded, 0);
            for (int i = oldSize; i < newSize; i++) expanded[i] = ItemData.Empty;
            Slots = expanded;
            Size  = newSize;

            if (HasUI)
            {
                var newUIs = new SlotUI[newSize];
                _slotUIs.CopyTo(newUIs, 0);
                for (int i = oldSize; i < newSize; i++)
                {
                    var go = UnityEngine.Object.Instantiate(_slotPrefab, _slotParent);
                    newUIs[i] = go.GetComponent<SlotUI>();
                    newUIs[i].Initialize(this, i);
                    newUIs[i].UpdateUI(Slots[i]);
                }
                _slotUIs = newUIs;
            }
        }
        else
        {
            // 초과 슬롯의 아이템을 [0, newSize-1] 범위의 빈 칸으로 앞당김
            for (int i = newSize; i < oldSize; i++)
            {
                if (Slots[i] == null || Slots[i].IsEmpty) continue;

                int emptyIdx = -1;
                for (int j = 0; j < newSize; j++)
                    if (Slots[j] == null || Slots[j].IsEmpty) { emptyIdx = j; break; }

                if (emptyIdx >= 0) Slots[emptyIdx] = Slots[i];
                else               overflow.Add(Slots[i]);
            }

            var truncated = new ItemData[newSize];
            Array.Copy(Slots, truncated, newSize);
            Slots = truncated;
            Size  = newSize;

            if (HasUI)
            {
                for (int i = newSize; i < oldSize; i++)
                    if (_slotUIs[i] != null) UnityEngine.Object.Destroy(_slotUIs[i].gameObject);
                var newUIs = new SlotUI[newSize];
                Array.Copy(_slotUIs, newUIs, newSize);
                _slotUIs = newUIs;
            }
        }

        RefreshAll();
    }

    // ── 세이브/로드 ──────────────────────────────────────────────────────
    public void LoadFromSnapshot(InventorySnapshot snapshot)
    {
        for (int i = 0; i < Size && i < snapshot.slots.Count; i++)
        {
            Slots[i] = snapshot.slots[i];
            _slotUIs?[i]?.UpdateUI(Slots[i]);
        }
    }

    public InventorySnapshot ToSnapshot()
    {
        var snap = new InventorySnapshot { inventoryId = Id, size = Size };
        snap.slots.AddRange(Slots);
        return snap;
    }

    // ── UI 갱신 ──────────────────────────────────────────────────────────
    public void RefreshSlot(int index)
    {
        if (HasUI && index < _slotUIs.Length)
            _slotUIs[index].UpdateUI(Slots[index]);
    }

    public void RefreshAll()
    {
        if (!HasUI) return;
        for (int i = 0; i < Size; i++)
            _slotUIs[i].UpdateUI(Slots[i]);
    }

    // ── 정리 ─────────────────────────────────────────────────────────────
    public void Destroy()
    {
        ClearUI();
        if (_prefabHandle.IsValid()) Addressables.Release(_prefabHandle);
        InventorySystem.Unregister(this);
    }

    // ── Internal ─────────────────────────────────────────────────────────
    void BuildUI()
    {
        if (_slotParent == null || _slotPrefab == null) return;
        _slotUIs = new SlotUI[Size];
        for (int i = 0; i < Size; i++)
        {
            var go = UnityEngine.Object.Instantiate(_slotPrefab, _slotParent);
            _slotUIs[i] = go.GetComponent<SlotUI>();
            _slotUIs[i].Initialize(this, i);
            _slotUIs[i].UpdateUI(Slots[i]);
        }
    }

    void ClearUI()
    {
        if (_slotUIs == null) return;
        foreach (var slot in _slotUIs)
            if (slot != null) UnityEngine.Object.Destroy(slot.gameObject);
        _slotUIs = null;
    }
}

#endif
