# 25. 인벤토리 시스템 (선택 모듈)

인벤토리는 "슬롯의 리스트"다. 슬롯 하나에 어떤 아이템이 몇 개 들어 있는지를 담고, 겹치기·슬롯 수 제한·넣기·빼기를 해 준다. 아이템은 프로젝트가 코드로 정의한 클래스의 **인스턴스**(DB › 인스턴스)이고, 슬롯에는 그 인스턴스의 ID가 들어간다. 모듈 위치는 `Optional/Inventory`(어셈블리 `GBible.Inventory`, 네임스페이스 `GBible.InventorySystem`).

지금 있는 것은 데이터와 규칙뿐이다. 슬롯 UI, 장비 슬롯, 드래그 규칙, 기본 제공 아이템 기능은 **지원하지 않는다**(프로젝트가 만든다 — UI는 35장 `List<T>`, 옮기기·고정 자리는 37장 자리, 관례 21절).

## 이 문서에서 쓰는 말

| 말 | 뜻 |
|---|---|
| 슬롯(`InventorySlot`) | 아이템 ID 하나와 개수. |
| 인벤토리(`Inventory`) | 슬롯 리스트 + 슬롯 수 제한. |
| 인스턴스 | DB › 인스턴스에서 만든, 클래스의 원본 객체 하나(포션, 철검 …). |
| 겹치기 최대 수(maxStack) | 한 슬롯에 들어가는 최대 개수. 아이템마다 다르므로 넣을 때마다 넘긴다. |
| 행동 | `Game.Do`로 실행하는 플레이어 입력 하나. Undo 한 단위. |
| 컨텍스트(`TriggerContext`) | 결과가 실행되는 순간의 정보: `Owner`(트리거를 가진 항목 ID), `User`(사용자), `Picked`(고른 대상), `Game`. |

---

## 1. 켜기와 준비

**한 줄 요약.** 기능을 켜고, 아이템 클래스를 코드로 쓰고, 인스턴스를 만들고, 인벤토리를 둘 곳(판·프로필 필드 또는 플레이어 데이터)을 정한다.

### 에디터에서

1. **켜기**(선택 기능 ID `inventory`): AI는 `ProjectSettings/MomoiProject/Features.json`의 `enabled`에 `inventory`를 더하고(명세서 6절에 켠 것만 — 관례 0절), 개발자는 `MomoiProject → DB` 사이드바 "인벤토리 시스템" 그룹의 **기능 활성화**를 누른다. Unity가 다시 컴파일할 때 스크립팅 심볼 `GBIBLE_INVENTORY`와 프로젝트 asmdef 참조(`GBible.Inventory` — 선택 기능 중 이것만 Rules·Tests에도 들어간다)가 저절로 맞춰진다(`20-Setup.md` 6절). `Inventory` 형식을 쓰는 코드는 그 컴파일이 끝난 뒤에 쓴다. 이 기능은 탭을 만들지 않는다.
2. 아이템 클래스를 코드로 쓴다(2절). 저장·컴파일하면 DB › 클래스에 보인다.
3. DB › **인스턴스**에 아이템마다 항목을 만든다: ID(예: `instance.potion`), **instanceType** = 아이템 클래스, **value** = 값. AI는 DB 반영 스크립트로 만든다(관례 15절).
4. 인벤토리를 둘 곳을 정한다.
   - **판·프로필 클래스의 필드**(`RunState`에 `public Inventory bag = new Inventory();`, 4.6): 키를 따로 만들지 않는다. 판에만 있는 인벤토리는 `RunState`, 판이 끝나도 남는 창고는 `Profile`(관례 8절).
   - **플레이어 데이터 키 하나**: DB › **플레이어 데이터**에 ID(예: `player.inventory`), **zone**(이번 판이면 `Session`, 계정에 남으면 `Meta`), **valueType** = `GBible.InventorySystem.Inventory`(기능을 켜면 목록에 나온다). 반영 스크립트가 돌고 Unity가 다시 컴파일하면 `GameIds.PlayerData.PlayerInventory`(`PlayerDataKey<Inventory>`)가 생긴다.
5. 플레이 중 모니터 › 데이터에서 슬롯을 열어 보면 슬롯의 **item** 칸이 인스턴스 목록 드롭다운이다(`[DataRef(typeof(InstanceData))]`).

### 주의할 점

- 슬롯 수(`capacity`)의 처음 값은 0(= 제한 없음)이다. 제한은 3.6절.
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

인스턴스 편집에서 비어 있는 기능 필드는 **+ 기능**으로 붙인다. 사용 기능(`Usable`)의 칸은 `when`(트리거: 기본 "사용", AND로 사용 조건)과 `then`(결과: 대상에게 효과 · 플레이어 데이터에 주기 · 코딩한 결과)이다. 자세히는 `16-Abilities-Usable.md`, 인스턴스는 `04-Instances.md`.

### 주의할 점

- 아이템 클래스를 `GameData`로 만들지 않는다(그건 DB 종류다). 아이템 원본은 `InstanceData`가 감싸는 평범한 객체다.

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
- `Inventory.capacity`: 슬롯 수, `0`이면 제한 없음.
- `Inventory.slots`: 슬롯 리스트(앞에서부터).

### 3.2 꺼내기

**고치는 것은 행동(`GameAction`) 안에서만**(관례 3절). 행동 안에서는 꺼내서 바로 고친다.

```csharp
var inventory = Game.Get<Inventory>("player.inventory");
// 또는 생성 코드: Game.Data().PlayerInventory
```

행동 밖에서 고치는 `Modify`는 Undo 단위가 없고 멀티에서 서버로 가지 않으므로, 개발 도구·테스트 준비에서만 쓴다(한 번의 변경으로 기록된다).

```csharp
GBibleRuntime.Session.Data.Modify<Inventory>("player.inventory", inv => inv.Add("instance.potion", 1, 99));
```

### 3.3 `CountOf(itemId)` · `Has(itemId, count = 1)`

모든 슬롯의 개수 합 / 그만큼 있는지.

```csharp
int potions = inventory.CountOf("instance.potion");
bool canPay = inventory.Has("instance.goblinEar", 5);
```

### 3.4 `Add(itemId, count, maxStack)` → 남은 개수

같은 아이템의 덜 찬 슬롯부터 채우고, 남으면 새 슬롯을 만든다. 슬롯이 모자라 못 넣은 개수를 돌려준다(다 들어가면 0).

```csharp
int left = inventory.Add("instance.potion", 150, 99);   // 99 + 51 두 슬롯, left = 0
```

### 3.5 `RoomFor(itemId, maxStack)` — 넣을 수 있는 개수

값을 바꾸지 않고 미리 센다. 슬롯 수 제한이 없으면 `int.MaxValue`.

```csharp
if (inventory.RoomFor("instance.ironSword", 1) < 1) { /* 인벤토리가 가득 참 */ }
```

### 3.6 `capacity` — 슬롯 수 제한

슬롯 수는 개발자가 바꿀 숫자라 밸런싱 값으로 두고(관례 18절), 판 시작 행동(또는 인벤토리를 늘리는 행동) 안에서 넣는다.

```csharp
inventory.capacity = (int)Game.Balance(GameIds.Balance.BagSlots);   // 행동의 Run 안. bagSlots = DB › 밸런싱 값(예)
```

### 3.7 `Remove(itemId, count = 1)` → 실제로 뺀 개수

뒤쪽 슬롯부터 줄이고 빈 슬롯은 없앤다. 모자라면 있는 만큼만 뺀다.

```csharp
int removed = inventory.Remove("instance.potion", 3);
```

### 3.8 `TryRemove(itemId, count = 1)` → 성공 여부

다 있을 때만 빼고, 모자라면 아무것도 바꾸지 않는다(값을 치를 때).

```csharp
if (!inventory.TryRemove("instance.goblinEar", 5)) { /* 재료 부족 */ }
```

### 3.9 아이템 정보 읽기

슬롯에는 ID만 있다. 원본(읽기 전용)은 `session.Instances(id)`, 고쳐 쓸 사본은 `session.CopyInstance(id)`로 얻는다.

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
public sealed class BuyItem : GameAction
{
    [DataRef(typeof(InstanceData))] public string item;   // 인스턴스 ID(DB 원본은 객체가 아니라 글자 ID로)
    public int count = 1;

    protected override void Run()
    {
        var info = Game.Session.Instances?.Invoke(item) as Item
                   ?? throw new InvalidOperationException($"[상점] 없는 아이템 '{item}'입니다.");   // 화면이 막았어야 하는 일(프로그래머 실수)
        var inventory = Game.Get<Inventory>("player.inventory");
        if (inventory.RoomFor(item, info.maxStack) < count)
            throw Game.Fail("fail.bagFull", "인벤토리에 자리가 없습니다.");                          // 플레이어에게 보일 실패
        Game.Pay("player.gold", null, info.price * count, what: "gold.name");                       // 모자라면 "골드이(가) 모자랍니다(30 / 50)."
        inventory.Add(item, count, info.maxStack);
    }

    public override string Describe() => $"{item} ×{count} 사기";
}

// 화면 코드(GameScreen.OnOpen): 누르면 행동을 보내고, 플레이어용 실패(GameFailure)의 글은 알림으로 뜬다(35장 2-4).
OnClick("buy", () => new BuyItem { item = "instance.potion", count = 3 });
```

- 안 될 때 조용히 `return`하지 않고 던진다. 행동이 실패하면 그때까지 바꾼 것이 모두 취소되므로(인벤토리에 넣은 뒤 골드가 모자라도 인벤토리가 돌아간다) 검사 순서를 맞출 필요가 없고, 플레이어는 이유를 본다. 조용히 끝내면 행동이 "성공"으로 남아 Undo 한 칸을 차지하고 아무 글도 뜨지 않는다.
- 골드는 직접 빼지 않고 `Game.Pay(키, 경로, 양, what: 이름)`. 모자라면 플레이어용 실패를 던지고, 비용을 고치는 행동 직전 이벤트("상점 가격 −20%")가 이 자리에 붙는다(28장 2-4). 단순 값 플레이어 데이터(`player.gold`)면 경로는 `null`이다. `what`에는 텍스트 키(`gold.name` = "골드")를 준다(30장 2-4).

### 4.2 전리품: 인벤토리가 차면 남은 것을 바닥에

```csharp
using System;
using GBible;
using GBible.InventorySystem;

[GameEvent("아이템 떨어짐")]
public class ItemDropped { public string item; public int count; }

[Serializable]
public sealed class PickUpLoot : GameAction
{
    [DataRef(typeof(InstanceData))] public string item;
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
public sealed class DeliverQuestItems : GameAction
{
    [DataRef(typeof(InstanceData))] public string item = "instance.goblinEar";
    public int count = 5;
    public int rewardGold = 300;

    protected override void Run()
    {
        if (!Game.Get<Inventory>("player.inventory").TryRemove(item, count))
            throw Game.Fail("fail.questItems", "{item:이} {count}개 있어야 합니다.", ("item", item), ("count", count));
        Game.Set("player.gold", Game.Get<int>("player.gold") + rewardGold);
    }
}
```

- 모자라면 던진다. `TryRemove`가 거짓이면 아무것도 바뀌지 않았고, 던지면 행동이 실패로 끝나 화면이 이유를 보인다. 값 `{item}`에 인스턴스 ID를 넣으면 틀이 지금 언어의 이름(`<ID>.name`)으로 바꾼다(23장).
- 재료가 한 아이템이 아니라 여럿이면 하나씩 `TryRemove`하고 모자란 곳에서 던진다. 앞에서 뺀 재료는 행동 실패로 함께 돌아간다.

### 4.5 인벤토리 화면 그리기 (읽기만)

화면은 UI Toolkit 화면 코드의 목록(35장 2-3·2-6)으로 그린다. 슬롯에는 `id`가 없어 목록 칸은 자리 번호로 이어진다.

```csharp
using GBible;
using GBible.Ids;
using GBible.InventorySystem;
using UnityEngine.UIElements;

[BindTo(GameIds.Screen.Bag)]                       // DB › 화면 'bag' + UI/Screens/Bag.uxml(목록 칸 UXML 'BagSlot')
public class BagScreen : GameScreen
{
    protected override void OnOpen() =>
        List("slots", Item("BagSlot"), () => Game.Get<Inventory>("player.inventory").slots, (cell, slot) =>
        {
            var item = Game.Session.Instances?.Invoke(slot.item) as Item;     // 원본 — 읽기만
            cell.Q<Label>("name").text = Texts.NameOf(slot.item);
            cell.Q<Label>("count").text = slot.count.ToString();
            if (item?.icon != null) cell.Q("icon").style.backgroundImage = new StyleBackground(item.icon);
        }, "player.inventory");                     // 이 키가 바뀌면 다시 그린다
}
```

### 4.6 여러 인벤토리(장비창·창고)

인벤토리는 값 형식이므로 플레이어 데이터를 여러 개 만들면 된다(`player.inventory`, `player.storage`). 또는 프로젝트 클래스(판·프로필) 안에 필드로 둔다.

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

이 클래스를 플레이어 데이터의 값 형식으로 고르면 처음 값부터 슬롯 수가 정해진다. 개발자가 바꿀 수이면 숫자를 적지 않고 판 시작 행동에서 밸런싱 값으로 넣는다(3.6).

---

## 5. 주의할 점 / 자주 하는 실수

- **씬 코드에서 꺼내 그냥 고치기**: 기록은 되지만 행동 밖 변경이라 Undo 단위가 없고, 멀티플레이에서 서버로 가지 않는다. 플레이어 입력(사기·쓰기·버리기)은 행동으로 만든다.
- **사용과 개수 줄이기를 한 단위로**: 행동 하나 안에서 `TryRemove` 후 `Game.Use`를 부르면 된다(행동 안의 `Game.Use`는 그 행동의 일부로 실행된다). 4.3처럼 사용 기능의 결과 안에서 빼도 된다.
- **원본 인스턴스 고치기**: `session.Instances(id)`가 돌려주는 객체는 DB 에셋의 원본이다. 고치지 않는다. 고쳐 쓸 것은 `CopyInstance`.
- **maxStack 불일치**: `Add`·`RoomFor`는 넘긴 `maxStack`만 본다. 아이템 클래스의 값(예: `Item.maxStack`)을 일관되게 넘긴다.
- **잘못된 값**: 빈 ID나 0 이하 개수로 `Add`하면 아무것도 바꾸지 않고 넘긴 개수를 그대로 돌려준다(0 이하면 0). 0 이하로 `Remove`하면 0.
- **인스턴스 ID 바꾸기**: 슬롯은 인스턴스 ID 글자를 담는다. DB에서 인스턴스 ID를 바꾸면 에셋 안의 `[DataRef]` 참조는 따라가지만, 저장된 판·프로필(`run.json`·`profile.json`)의 슬롯 값은 출시됨 상태에서 바꾼 경우에만 불러올 때 새 ID로 바뀐다(런타임이 ID 이름 변경 이력으로 `[DataRef]` 필드를 고친다). 개발 중에 바꿨다면 모니터 › 명령 탭의 **새 판**(22장 7절)으로 판을 지우고 시험한다.
- **보상 블록 "플레이어 데이터에 주기"로 슬롯 더하기**: 겹치기 규칙(`Add`)을 거치지 않는다. 겹쳐 넣으려면 `Add`를 부르는 코딩한 결과를 만든다(4.3의 방식).
- **안 될 때 조용히 `return`**: 하지 않는다(4.1).
- **이 인벤토리의 한계**: 슬롯은 앞에서부터 채우고 빈 슬롯은 없애는 목록이라 **고정 슬롯**(3번 슬롯에 두기)·**슬롯 옮기기**(드래그로 자리 바꾸기)·**슬롯마다의 정보**(품질·내구도·강화 수치)가 없다. 필요하면 프로젝트 클래스에 목록 필드로 직접 둔다(`[Serializable] class BagSlot { public string item; public int count; public int quality; }`의 `List<BagSlot>` + 자리 `Placement`, 37장). 슬롯 수·겹치기·장비 슬롯·옮기는 법이 명세서에 없으면 만들기 전에 묻는다(관례 21절).
