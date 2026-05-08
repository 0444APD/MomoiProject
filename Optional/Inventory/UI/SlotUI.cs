#if SYSTEM_INVENTORY

using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif
#if DOTWEEN
using DG.Tweening;
#endif

public class SlotUI : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler,
    IPointerClickHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
{
#if ODIN_INSPECTOR
    [FoldoutGroup("References"), Required]
#else
    [Header("References")]
#endif
    [SerializeField] Image      _icon;

#if ODIN_INSPECTOR
    [FoldoutGroup("References")]
#endif
    [SerializeField] TMP_Text   _stackText;

#if ODIN_INSPECTOR
    [FoldoutGroup("References")]
#endif
    [SerializeField] TMP_Text   _priceText;

#if ODIN_INSPECTOR
    [FoldoutGroup("References")]
#endif
    [SerializeField] Slider     _gauge;

#if ODIN_INSPECTOR
    [FoldoutGroup("References")]
#endif
    [SerializeField] GameObject _highlight;

    // ── 전역 입력 핸들러 ─────────────────────────────────────────────────
    // 프로젝트에서 재할당해 동작 커스터마이즈. null이면 무시.
    // 디폴트:
    //   OnDoubleClick → 소비 아이템 스택 1 감소
    //   OnDrop        → 이동 / 스왑 / 스택 합산
    public static Action<SlotUI>         OnLeftClick   = null;
    public static Action<SlotUI>         OnRightClick  = null;
    public static Action<SlotUI>         OnDoubleClick = DoubleClickDefault;
    public static Action<SlotUI, SlotUI> OnSlotDrop     = DropDefault; // (from, to)

    // 더블클릭 판정 간격 (초)
    public static float DoubleClickInterval = 0.25f;

    // ── Static 상태 ──────────────────────────────────────────────────────
    static SlotUI _dragging;
    static SlotUI _lastClickSlot;
    static float  _lastClickTime = -1f;

    // ── 인스턴스 ────────────────────────────────────────────────────────
    public ItemType  AllowedType { get; set; } = ItemType.All;
    public int       SlotIndex   { get; private set; }
    public Inventory Owner       { get; private set; }

    public ItemData Data => Owner?.Slots[SlotIndex];

    // ── 초기화 ───────────────────────────────────────────────────────────
    public void Initialize(Inventory owner, int index, ItemType filter = ItemType.All)
    {
        Owner       = owner;
        SlotIndex   = index;
        AllowedType = filter;
        SetPriceVisible(false);
        UpdateUI(ItemData.Empty);
    }

    // ── 가격 텍스트 제어 ─────────────────────────────────────────────────
    public void SetPriceVisible(bool visible)
    {
        if (_priceText != null) _priceText.gameObject.SetActive(visible);
    }

    // ── UI 갱신 ──────────────────────────────────────────────────────────
    public void UpdateUI(ItemData data)
    {
        bool hasItem = data != null && !data.IsEmpty;

        if (_icon != null)
        {
            _icon.sprite  = hasItem ? data.so?.icon : null;
            _icon.enabled = hasItem;
        }

        if (_priceText != null && _priceText.gameObject.activeSelf)
            _priceText.text = hasItem ? data.so?.price.ToString("F0") : string.Empty;

        bool showGauge = hasItem && data.IsGaugeType;
        bool showStack = hasItem && !data.IsGaugeType && data.MaxStack > 1;

        if (_gauge != null)
        {
            _gauge.gameObject.SetActive(showGauge);
            if (showGauge) _gauge.value = data.gauge;
        }

        if (_stackText != null)
        {
            _stackText.gameObject.SetActive(showStack);
            if (showStack) _stackText.text = data.currentStack.ToString();
        }
    }

    // ── 하이라이트 ───────────────────────────────────────────────────────
    public void OnPointerEnter(PointerEventData e) => SetHighlight(true);
    public void OnPointerExit(PointerEventData e)  => SetHighlight(false);

    void SetHighlight(bool on)
    {
        if (_highlight == null) return;
#if DOTWEEN
        var img = _highlight.GetComponent<Image>();
        if (img != null)
        {
            _highlight.SetActive(true);
            img.DOFade(on ? 0.35f : 0f, 0.12f).OnComplete(() =>
            {
                if (!on) _highlight.SetActive(false);
            });
            return;
        }
#endif
        _highlight.SetActive(on);
    }

    // ── 클릭 / 더블클릭 ──────────────────────────────────────────────────
    public void OnPointerClick(PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Right)
        {
            OnRightClick?.Invoke(this);
            return;
        }

        if (e.button != PointerEventData.InputButton.Left) return;

        // 더블클릭 판정
        bool isDouble = _lastClickSlot == this
                     && Time.unscaledTime - _lastClickTime <= DoubleClickInterval;

        if (isDouble)
        {
            _lastClickTime = -1f;
            _lastClickSlot = null;
            OnDoubleClick?.Invoke(this);
        }
        else
        {
            _lastClickTime = Time.unscaledTime;
            _lastClickSlot = this;
            OnLeftClick?.Invoke(this);
        }
    }

    // ── 드래그 앤 드롭 ───────────────────────────────────────────────────
    public void OnBeginDrag(PointerEventData e)
    {
        if (Data == null || Data.IsEmpty) return;
        _dragging = this;
    }

    public void OnDrag(PointerEventData e) { }

    public void OnEndDrag(PointerEventData e)
    {
        _dragging = null;
    }

    public void OnDrop(PointerEventData e)
    {
        if (_dragging == null || _dragging == this) return;
        OnSlotDrop?.Invoke(_dragging, this);
        _dragging = null;
    }

    // ════════════════════════════════════════════════════════════════════
    // 디폴트 동작
    // ════════════════════════════════════════════════════════════════════

    // ── 더블클릭 디폴트: 소비 아이템 스택 1 감소 ──────────────────────
    // 게이지형은 감소량이 게임마다 다르므로 기본 처리 없음 (OnDoubleClick 재할당으로 구현)
    static void DoubleClickDefault(SlotUI slot)
    {
        var data = slot.Data;
        if (data == null || data.IsEmpty || data.so == null) return;
        if (data.so.type != ItemType.Consumable || data.IsGaugeType) return;

        data.currentStack--;
        if (data.currentStack <= 0)
            slot.Owner.Slots[slot.SlotIndex] = ItemData.Empty;
        slot.Owner.RefreshSlot(slot.SlotIndex);
    }

    // ── 드롭 디폴트: 이동 / 스왑 / 스택 합산 ────────────────────────
    static void DropDefault(SlotUI from, SlotUI to)
    {
        var a = from.Owner.Slots[from.SlotIndex];
        var b = to.Owner.Slots[to.SlotIndex];

        if (a == null || a.IsEmpty) return;
        if (to.AllowedType != ItemType.All && a.so != null && a.so.type != to.AllowedType) return;

        if (b == null || b.IsEmpty)
        {
            Move(from, to);
        }
        else if (CanStackMerge(a, b))
        {
            StackMerge(from, to, a, b);
        }
        else
        {
            if (from.AllowedType != ItemType.All && b.so != null && b.so.type != from.AllowedType) return;
            Swap(from, to);
        }
    }

    // ── 내부 유틸 ────────────────────────────────────────────────────────
    static void Move(SlotUI from, SlotUI to)
    {
        to.Owner.Slots[to.SlotIndex]     = from.Owner.Slots[from.SlotIndex];
        from.Owner.Slots[from.SlotIndex] = ItemData.Empty;
        Refresh(from, to);
    }

    static void Swap(SlotUI from, SlotUI to)
    {
        (from.Owner.Slots[from.SlotIndex], to.Owner.Slots[to.SlotIndex])
            = (to.Owner.Slots[to.SlotIndex], from.Owner.Slots[from.SlotIndex]);
        Refresh(from, to);
    }

    static void StackMerge(SlotUI from, SlotUI to, ItemData a, ItemData b)
    {
        int transfer = Mathf.Min(a.currentStack, b.MaxStack - b.currentStack);
        b.currentStack += transfer;
        a.currentStack -= transfer;
        if (a.currentStack <= 0)
            from.Owner.Slots[from.SlotIndex] = ItemData.Empty;
        Refresh(from, to);
    }

    static void Refresh(SlotUI from, SlotUI to)
    {
        from.Owner.RefreshSlot(from.SlotIndex);
        to.Owner.RefreshSlot(to.SlotIndex);
    }

    static bool CanStackMerge(ItemData a, ItemData b)
        => InventorySystem.UseStacking
        && !a.IsGaugeType
        && !b.IsGaugeType
        && a.itemId == b.itemId
        && b.HasStackSpace;
}

#endif
