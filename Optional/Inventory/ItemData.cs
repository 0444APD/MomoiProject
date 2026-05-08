#if SYSTEM_INVENTORY

using System;
using UnityEngine;

// 아이템 런타임 인스턴스. 직렬화되어 SaveData에 저장됨.
[Serializable]
public class ItemData
{
    public string itemId;
    public int    currentStack = 1;
    public float  gauge        = 1f;  // 0~1, 게이지형 전용
    public string extraJson;          // 인챈트·강화 등 확장 데이터 (JSON)

    // 로드 후 InitializeAsync에서 연결, 직렬화 제외
    [NonSerialized] public ItemSO so;

    public bool IsEmpty       => string.IsNullOrEmpty(itemId);
    public bool IsGaugeType   => so != null && so.isGaugeType;

    // UseStacking OFF → 게이지형이든 일반이든 MaxStack = 1
    public int MaxStack
    {
        get
        {
            if (so == null || so.isGaugeType) return 1;
            return InventorySystem.UseStacking ? so.maxStack : 1;
        }
    }

    public bool HasStackSpace => currentStack < MaxStack;

    public static ItemData Empty => new ItemData();

    // SO 연결 후 스택 범위 보정 (로드 시 호출)
    public void LinkSO(ItemSO resolvedSO)
    {
        so = resolvedSO;
        if (so != null && !IsGaugeType)
            currentStack = Mathf.Clamp(currentStack, 1, MaxStack);
    }
}

#endif
