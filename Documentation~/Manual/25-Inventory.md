# 인벤토리 시스템 (선택 모듈)

인벤토리는 "칸의 리스트"다. 칸 하나에 어떤 아이템이 몇 개 들어 있는지를 담고, 겹치기·칸 수 제한·넣기·빼기를 해 준다. 아이템은 프로젝트가 코드로 정의한 클래스의 **인스턴스**(도감 › 인스턴스)이고, 칸에는 그 인스턴스의 ID가 들어간다. 모듈 위치는 `Optional/Inventory`(어셈블리 `GBible.Inventory`, 네임스페이스 `GBible.InventorySystem`).

지금 있는 것은 데이터와 규칙뿐이다. 슬롯 UI, 장비 칸, 드래그 규칙, 기본 제공 아이템 기능은 **지원하지 않는다**(프로젝트가 만든다).

## 이 문서에서 쓰는 말

| 말 | 뜻 |
|---|---|
| 칸(`InventorySlot`) | 아이템 ID 하나와 개수. |
| 인벤토리(`Inventory`) | 칸 리스트 + 칸 수 제한. |
| 인스턴스 | 도감 › 인스턴스에서 만든, 클래스의 원본 객체 하나(포션, 철검 …). 예전 이름은 템플릿. |
| 겹치기 최대 수(maxStack) | 한 칸에 들어가는 최대 개수. 아이템마다 다르므로 넣을 때마다 넘긴다. |
| 행동 | `Game.Do`로 실행하는 플레이어 입력 하나. Undo 한 단위. |
| 컨텍스트(`TriggerContext`) | 결과가 실행되는 순간의 정보: `Owner`(트리거를 가진 항목 ID), `User`(사용자), `Picked`(고른 대상), `Game`. |

---

## 1. 켜기와 준비

**한 줄 요약.** 기능을 켜고, 아이템 클래스를 코드로 쓰고, 인스턴스를 만들고, 인벤토리를 플레이어 데이터로 등록한다.

### 에디터에서

1. `MomoiProject → 도감` → 사이드바 "인벤토리 시스템" 그룹의 **기능 활성화** → **켜기**(스크립팅 심볼 `GBIBLE_INVENTORY`). 이 기능은 탭을 만들지 않는다.
2. 아이템 클래스를 코드로 쓴다(2절). 저장·컴파일하면 도감 › 클래스에 보인다.
3. 도감 › **인스턴스** → **+ 새로 만들기** → ID(예: `instance.potion`) → **instanceType**에 아이템 클래스를 고르고 값을 채운다. 아이템마다 반복한다.
4. 도감 › **플레이어 데이터** → **+ 새로 만들기** → ID(예: `player.inventory`), **zone**(이번 판이면 `Session`, 계정에 남으면 `Meta`), **valueType**에 `Inventory`(기능을 켜면 목록에 나온다).
5. 저장(Ctrl+S). `GameIds.PlayerData.PlayerInventory`(`PlayerDataKey<Inventory>`)가 생긴다.
6. 플레이 중 모니터 › 데이터에서 칸을 열어 보면 칸의 **item** 칸이 인스턴스 목록 드롭다운이다(`[DataRef(typeof(InstanceData))]`).

### 주의할 점

- 칸 수(`capacity`)의 처음 값은 클래스 초기값(0 = 제한 없음)이다. 처음부터 제한하려면 3.6절처럼 코드에서 정한다.
- 기능을 끄면 `Inventory` 형식을 쓰는 플레이어 데이터는 값 형식을 찾지 못해 검증 오류가 난다.

---

## 2. 아이템 클래스 = 평범한 `[Serializable]` 클래스

**한 줄 요약.** 아이템 종류를 상속으로 나누지 않는다. 한 클래스에 필드를 두고, 기능은 필드로 붙인다.

### 코드로 쓰는 법

**`public string id;`** — 인스턴스를 개체로 찾을 일이 있으면 둔다(`Game.Find`).

**키워드 필드** — 등급·종류는 키워드(`21-Editor-Database.md` 6.1).

**기능 필드 `[SerializeReference] public Usable usable;`** — 비어 있으면(null) 쓸 수 없는 아이템. 붙어 있으면 게임이 켜질 때 그 인스턴스가 `Game.Use(인스턴스 ID, …)`로 쓸 수 있는 것으로 등록된다(리플렉션은 시작할 때만).

```csharp
using System;
using GBible;
using UnityEngine;

[KeywordGroup("아이템 종류")]
public static class ItemKind
{
    public const string Consumable = "consumable";
    public const string Material = "material";
    public const string Equipment = "equipment";
}

[Serializable]
public class Item
{
    public string id;
    public Sprite icon;
    [Keyword(typeof(ItemKind))] public string kind = "";
    public int maxStack = 99;
    public int price;
    [SerializeReference] public Usable usable;            // 사용 기능(없으면 null)
    [ShowIf(nameof(usable))] public int cooldown;          // 사용 기능이 있을 때만 보이는 칸
}
```

인스턴스 편집에서 비어 있는 기능 필드는 **+ 기능**으로 붙인다. 사용 기능(`Usable`)의 칸은 **트리거**(기본 "사용", AND로 사용 조건)와 **결과**(대상에게 효과 · 플레이어 데이터에 주기 · 코딩한 결과)다.

### 주의할 점

- 아이템 클래스를 `GameData`로 만들지 않는다(그건 도감 종류다). 아이템 원본은 `InstanceData`가 감싸는 평범한 객체다.

---

## 3. 인벤토리 API — 문법 요소를 하나씩

모든 메서드는 `Inventory` 객체에 있다. 플레이어 데이터에서 꺼낸 그 객체를 그냥 고치면 시스템이 Sync로 기록한다(Undo·기록·저장·조건 검사). Sync는 시스템이 들고 있는 사본(Mirror)과 지금 값을 비교해 바뀐 곳만 기록하는 동작이다.

### 3.1 데이터 모양

```csharp
// GBible.InventorySystem
[Serializable] public sealed class InventorySlot { public string item; public int count; }
[Serializable] public sealed class Inventory { public int capacity; public List<InventorySlot> slots; /* + 메서드 */ }
```

- `InventorySlot.item`: 인스턴스 ID(`[DataRef(typeof(InstanceData))]`).
- `Inventory.capacity`: 칸 수, `0`이면 제한 없음.
- `Inventory.slots`: 칸 리스트(앞에서부터).

### 3.2 꺼내기

행동(`GameAction`·`SimpleAction`) 안에서는 꺼내서 바로 고친다.

```csharp
var inventory = Game.Get<Inventory>("player.inventory");
// 또는 생성 코드: Game.Data().PlayerInventory
```

행동 밖(씬 코드)에서 고칠 때는 `Modify`로 감싸 한 번의 변경으로 기록한다.

```csharp
GBibleRuntime.Session.Data.Modify<Inventory>("player.inventory", inv => inv.Add("instance.potion", 1, 99));
```

### 3.3 `CountOf(itemId)` · `Has(itemId, count = 1)`

모든 칸의 개수 합 / 그만큼 있는지.

```csharp
int potions = inventory.CountOf("instance.potion");
bool canPay = inventory.Has("instance.goblinEar", 5);
```

### 3.4 `Add(itemId, count, maxStack)` → 남은 개수

같은 아이템의 덜 찬 칸부터 채우고, 남으면 새 칸을 만든다. 칸이 모자라 못 넣은 개수를 돌려준다(다 들어가면 0).

```csharp
int left = inventory.Add("instance.potion", 150, 99);   // 99 + 51 두 칸, left = 0
```

### 3.5 `RoomFor(itemId, maxStack)` — 넣을 수 있는 개수

값을 바꾸지 않고 미리 센다. 칸 수 제한이 없으면 `int.MaxValue`.

```csharp
if (inventory.RoomFor("instance.ironSword", 1) < 1) { /* 가방이 가득 참 */ }
```

### 3.6 `capacity` — 칸 수 제한

```csharp
GBibleRuntime.Session.Data.Modify<Inventory>("player.inventory", inv => inv.capacity = 20);
```

### 3.7 `Remove(itemId, count = 1)` → 실제로 뺀 개수

뒤쪽 칸부터 줄이고 빈 칸은 없앤다. 모자라면 있는 만큼만 뺀다.

```csharp
int removed = inventory.Remove("instance.potion", 3);
```

### 3.8 `TryRemove(itemId, count = 1)` → 성공 여부

다 있을 때만 빼고, 모자라면 아무것도 바꾸지 않는다(값을 치를 때).

```csharp
if (!inventory.TryRemove("instance.goblinEar", 5)) { /* 재료 부족 */ }
```

### 3.9 아이템 정보 읽기

칸에는 ID만 있다. 원본(읽기 전용)은 `session.Instances(id)`, 고쳐 쓸 사본은 `session.CopyInstance(id)`로 얻는다.

```csharp
var item = GBibleRuntime.Session.Instances?.Invoke(slot.item) as Item;   // 원본: 고치지 않는다
var copy = GBibleRuntime.Session.CopyInstance(slot.item) as Item;         // 깊은 사본
```

---

## 4. 상황별 예제

### 4.1 상점에서 사기 (행동 하나 = Undo 한 번)

```csharp
using System;
using GBible;
using GBible.InventorySystem;

[Serializable]
public sealed class BuyItem : SimpleAction
{
    public string item;      // 인스턴스 ID
    public int count = 1;

    protected override void Run()
    {
        var data = Game.Session.Data;
        var info = Game.Session.Instances?.Invoke(item) as Item;
        if (info == null) return;
        var inventory = data.Get<Inventory>("player.inventory");
        var cost = info.price * count;
        var gold = data.Get<int>("player.gold");
        if (gold < cost || inventory.RoomFor(item, info.maxStack) < count) return;   // 살 수 없으면 아무것도 안 바꾼다
        inventory.Add(item, count, info.maxStack);
        data.Set("player.gold", gold - cost);
    }

    public override string Describe() => $"{item} ×{count} 사기";
}

// 씬 코드(버튼):  GBibleRuntime.Game.Do(new BuyItem { item = "instance.potion", count = 3 });
```

### 4.2 전리품: 가방이 차면 남은 것을 바닥에

```csharp
using System;
using GBible;
using GBible.InventorySystem;

[GameEvent("아이템 떨어짐")]
public class ItemDropped { public string item; public int count; }

[Serializable]
public sealed class PickUpLoot : SimpleAction
{
    public string item;
    public int count;
    public int maxStack = 99;

    protected override void Run()
    {
        var left = Game.Get<Inventory>("player.inventory").Add(item, count, maxStack);
        if (left > 0) Game.Events.Publish(new ItemDropped { item = item, count = left });
    }
}
```

### 4.3 포션 사용 — 사용 기능 + 쓴 아이템 하나 빼기

포션 인스턴스의 `usable` 결과에 "대상에게 효과(회복)"와 아래 코딩한 결과 "쓴 아이템 하나 빼기"를 넣는다. 사용은 `Game.Use(인스턴스 ID, 사용자, 대상)` 행동 하나라 Undo 한 번에 효과와 개수가 함께 돌아간다. 결과의 컨텍스트 `Owner`가 쓴 인스턴스 ID다.

```csharp
using System;
using GBible;
using GBible.InventorySystem;

[Serializable, BlockInfo("쓴 아이템 하나 빼기", Category = "인벤토리", Description = "사용 기능의 결과에 넣는다. 쓴 인스턴스를 인벤토리에서 하나 뺀다.")]
public sealed class ConsumeUsedItem : Reward
{
    [PlayerDataRef] public string inventory = "player.inventory";

    public override RewardEffect Effect => RewardEffect.Reversible;

    public override void Grant(GameSession session)
    {
        var itemId = Context?.Owner;
        if (string.IsNullOrEmpty(itemId)) return;
        session.Data.Modify<Inventory>(inventory, inv => inv.Remove(itemId, 1));
    }

    public override string Describe(INameResolver names) => "쓴 아이템 1개 빼기";
}
```

씬 코드:

```csharp
using GBible;
using GBible.InventorySystem;

public static class ItemButtons
{
    public static void UsePotion(string heroId)
    {
        var game = GBibleRuntime.Game;
        if (!game.Get<Inventory>("player.inventory").Has("instance.potion")) return;
        if (!game.CanUse("instance.potion", heroId, out var why)) { UnityEngine.Debug.Log(why); return; }
        game.Use("instance.potion", heroId, heroId);
    }
}
```

결과를 통째로 코드로 쓰고 싶으면 `[BindTo]`를 인스턴스 ID에 붙인다(그 인스턴스의 결과 칸은 잠기고 이 함수가 결과가 된다).

```csharp
using GBible;
using GBible.InventorySystem;

public static class PotionLogic
{
    [BindTo("instance.potion")]
    static void Drink(TriggerContext c)
    {
        c.Session.Data.Modify<Inventory>("player.inventory", inv => inv.Remove(c.Owner, 1));
        // 회복: c.User가 사용자 객체(id 필드가 있는 플레이어 데이터 안의 객체)
    }
}
```

### 4.4 퀘스트 납품 — 모두 있을 때만

```csharp
using System;
using GBible;
using GBible.InventorySystem;

[Serializable]
public sealed class DeliverQuestItems : SimpleAction
{
    public string item = "instance.goblinEar";
    public int count = 5;
    public int rewardGold = 300;

    protected override void Run()
    {
        if (!Game.Get<Inventory>("player.inventory").TryRemove(item, count)) return;
        Game.Set("player.gold", Game.Get<int>("player.gold") + rewardGold);
    }
}
```

### 4.5 가방 화면 그리기 (읽기만)

```csharp
using GBible;
using GBible.InventorySystem;
using UnityEngine;

public sealed class BagView : MonoBehaviour
{
    void OnEnable()
    {
        var session = GBibleRuntime.Session;
        var inventory = session.Data.Get<Inventory>("player.inventory");
        foreach (var slot in inventory.slots)
        {
            var item = session.Instances?.Invoke(slot.item) as Item;
            Debug.Log($"{Texts.NameOf(slot.item)} ×{slot.count} (아이콘 {(item?.icon != null ? item.icon.name : "없음")})");
        }
    }
}
```

### 4.6 여러 가방(장비창·창고)

인벤토리는 값 형식이므로 플레이어 데이터를 여러 개 만들면 된다(`player.inventory`, `player.storage`). 또는 프로젝트 클래스 안에 필드로 둔다.

```csharp
using System;
using GBible.InventorySystem;

[Serializable]
public class PlayerBags
{
    public Inventory bag = new Inventory { capacity = 20 };
    public Inventory storage = new Inventory { capacity = 100 };
}
```

이 클래스를 플레이어 데이터의 값 형식으로 고르면 처음 값부터 칸 수가 정해진다.

---

## 5. 주의할 점 / 자주 하는 실수

- **씬 코드에서 꺼내 그냥 고치기**: 기록은 되지만 행동 밖 변경이라 Undo 단위가 없고, 멀티플레이에서 서버로 가지 않는다. 플레이어 입력(사기·쓰기·버리기)은 행동으로 만든다.
- **행동 안에서 `Game.Use`를 부르기**: `Game.Use`도 행동이다. 행동 안에서 다른 행동을 실행하면 예외가 난다. 사용과 개수 줄이기를 한 단위로 하려면 4.3처럼 사용 기능의 결과 안에서 뺀다.
- **원본 인스턴스 고치기**: `session.Instances(id)`가 돌려주는 객체는 도감 에셋의 원본이다. 고치지 않는다. 고쳐 쓸 것은 `CopyInstance`.
- **maxStack 불일치**: `Add`·`RoomFor`는 넘긴 `maxStack`만 본다. 아이템 클래스의 값(예: `Item.maxStack`)을 일관되게 넘긴다.
- **잘못된 값**: 빈 ID나 0 이하 개수로 `Add`하면 아무것도 바꾸지 않고 넘긴 개수를 그대로 돌려준다(0 이하면 0). 0 이하로 `Remove`하면 0.
- **인스턴스 ID 바꾸기**: 칸은 인스턴스 ID 글자를 담는다. 도감에서 인스턴스 ID를 바꾸면 에셋 안의 `[DataRef]` 참조는 따라가지만, 이미 저장된 판·프로필(`run.json`·`profile.json`)의 칸 값은 바뀌지 않는다. 출시 후에는 인스턴스 ID를 바꾸지 않거나, 저장 버전 변환(`SessionOptions.Migrations.Add`)으로 고친다(`20-Setup.md` 3절).
- **보상 블록 "플레이어 데이터에 주기"로 칸 더하기**: 겹치기 규칙(`Add`)을 거치지 않는다. 겹쳐 넣으려면 `Add`를 부르는 코딩한 결과를 만든다(4.3의 방식).
