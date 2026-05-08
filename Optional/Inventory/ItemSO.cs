#if SYSTEM_INVENTORY

using UnityEngine;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif

[CreateAssetMenu(menuName = "Game Systems/Inventory/Item", fileName = "New Item")]
public class ItemSO : ScriptableObject
{
    // ID는 TextTable 키와 동일하게 사용 (이름은 TextTable에서 가져옴)
#if ODIN_INSPECTOR
    [LabelText("ID"), HorizontalGroup("Top"), HideLabel]
#else
    [Header("Base")]
#endif
    public string id;

#if ODIN_INSPECTOR
    [HorizontalGroup("Top", Width = 120), ValueDropdown(nameof(ItemTypeValues))]
#endif
    public ItemType type = ItemType.Misc;

#if ODIN_INSPECTOR
    [PreviewField(70, ObjectFieldAlignment.Left), HideLabel, HorizontalGroup("Icon", Width = 80)]
#endif
    public Sprite icon;

#if ODIN_INSPECTOR
    [HorizontalGroup("Icon"), VerticalGroup("Icon/Right")]
#endif
    public float price;

#if ODIN_INSPECTOR
    [VerticalGroup("Icon/Right"), LabelText("게이지형 소비아이템")]
#else
    [Header("Type")]
#endif
    public bool isGaugeType;

    // 게이지형 OFF → 스택 / 게이지형 ON → 최대 게이지
#if ODIN_INSPECTOR
    [BoxGroup("Stack"), HideIf(nameof(isGaugeType)), MinValue(1), LabelText("최대 스택")]
#else
    [Header("Stack")]
#endif
    public int maxStack = 1;

#if ODIN_INSPECTOR
    [BoxGroup("Stack"), ShowIf(nameof(isGaugeType)), MinValue(0.01f), LabelText("최대 게이지")]
#endif
    public float maxGauge = 1f;

#if ODIN_INSPECTOR
    static ItemType[] ItemTypeValues() =>
        new[] { ItemType.Equipment, ItemType.Consumable, ItemType.Misc };
#endif
}

#endif
