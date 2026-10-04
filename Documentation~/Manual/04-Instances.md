# 04. 인스턴스

**한 줄 요약**: 코드로 정의한 클래스(아이템·몬스터·카드 …)의 **원본 하나**(철검, 고블린, 화염구 카드 …)를 DB에서 값으로 채워 두는 것이 인스턴스다. 게임은 원본을 건드리지 않고 그 **깊은 사본**을 만들어 플레이어 데이터에 넣는다.

**언제 쓰는가**

- 기획자가 아이템·몬스터·카드의 수치를 에디터에서 채우고, 프로그래머는 클래스와 로직만 쓸 때
- 드롭·상점·소환·덱 구성처럼 "정해 둔 원본의 복사본"을 게임 중에 만들 때
- 아이템마다 붙거나 안 붙는 기능(장착·사용·소모)을 필드 하나로 표현할 때

---

## 0. 용어

| 용어 | 뜻 |
|---|---|
| 클래스 | 프로그래머가 코드로 만든 `[Serializable]` 클래스(예: `Item`). 한 개념 = 한 클래스. |
| 인스턴스 (`InstanceData`) | 클래스로 만든 원본 하나를 담은 DB 항목. 에셋은 저장용 껍데기이고, 실제 값은 `value` 필드의 클래스 객체다. DB 탭 이름은 "인스턴스". |
| 사본 | 원본을 깊게 복사한 새 객체. 안의 리스트·클래스까지 모두 새로 만든다. 원본·다른 사본과 공유하지 않는다. |
| 기능 필드 | 비어 있을 수 있는 `[SerializeReference]` 클래스 필드. 값이 있으면 그 기능이 붙은 것, `null`이면 없는 것. |
| 플레이어 데이터 | 게임 중에 바뀌는 값을 담는 저장소. 사본은 여기에 넣는다. [01-PlayerData.md](01-PlayerData.md) |
| 세션 (`GameSession`) | 게임 한 판의 실행 전체. 인스턴스 원본을 찾는 `Instances`와 사본을 만드는 `CopyInstance`가 여기 있다. |

---

## 1. 에디터에서 쓰는 법

### 1.1 인스턴스 만들기

1. 메뉴 **MomoiProject › DB**을 연다.
2. 사이드바의 **DB** 그룹에서 **인스턴스** 탭을 고른다.
3. **+ 새로 만들기** → **새 인스턴스** 창에 ID(예: `ironSword`)를 적고 **만들기**.
4. 항목을 눌러 상세를 연다.
5. **형식** 칸을 눌러 클래스를 고른다(목록 "형식 검색", 묶음은 기본 형식 · enum · 데이터 클래스). 클래스를 고르면 그 클래스의 새 객체가 **값** 칸에 만들어진다. 같은 형식을 다시 고르면 기존 값을 둔다. 다른 형식으로 바꾸면 값이 새 객체로 바뀐다.
6. **값** 칸(탐색기)에서 필드를 채운다.
   - `[Hide]` 필드는 보이지 않는다.
   - `[ShowIf]` 필드는 조건이 맞을 때만 보인다.
   - 비어 있는 기능 필드는 **+ 기능** 버튼으로 붙인다(목록 "기능 검색"에 필드 이름과 넣을 수 있는 형식이 나온다). 붙어 있는 기능은 줄 오른쪽 **✕**(빼기)로 뗀다.
7. **저장  Ctrl+S**.

DB 검증은 형식이 비었거나, 형식을 찾을 수 없거나, 값이 비었거나, 값의 형식이 고른 형식과 다르면 오류를 띄운다.

### 1.2 필드 숨기기를 에디터에서 바꾸기

1. 사이드바 **선언 › 클래스** 탭에서 클래스를 누른다(또는 인스턴스 탭의 **필드 연결** 버튼).
2. 클래스 상세의 필드 줄마다 **인스턴스에서 숨김  [Hide]** 토글이 있다. 그 클래스에서 선언한 필드에만 나온다.
3. 켜거나 끄고 **Ctrl+S**로 저장하면 코드 파일에 `[Hide]`가 붙거나 떨어지고 Unity가 다시 컴파일한다.
4. `[ShowIf]`는 코드에서만 정한다. 클래스 상세는 "'kind'이(가) 'Weapon'일 때만 보임  [ShowIf]"처럼 보여 주기만 한다.
5. 기능 필드에는 "기능 필드: 비어 있으면 그 기능이 없습니다. 인스턴스에서 '+ 기능'으로 붙입니다." 안내가 보인다.

---

## 2. 코드로 쓰는 법 — 문법 요소

### 2.1 클래스: 평범한 `[Serializable]` 클래스

```csharp
using System;
using System.Collections.Generic;

[Serializable]
public class Item
{
    public string id;
    public string name;
    public int price;
    public List<string> tags = new List<string>();
}
```

인스턴스의 형식 목록에는 내 코드 범위 안의 공개 `[Serializable]` 클래스 중 추상 클래스가 아니고 담을 수 없는 필드가 없는 것이 나온다. 필드 규칙(무엇이 저장·기록되는가)은 플레이어 데이터와 같다([01-PlayerData.md](01-PlayerData.md) 3장).

### 2.2 `[Hide]` — 항상 숨김

```csharp
using GBible;

[Serializable]
public class Monster
{
    public string id;
    public int hp = 20;
    [Hide] public int killCount;      // 게임 중에만 쓰는 값. 인스턴스 편집 화면에 보이지 않는다
}
```

인스턴스 편집 화면에서 이 필드를 보이지 않는다. 값은 그대로 저장·복사된다. 숨기기만 하므로 기본값(필드 초기화)이 사본에 그대로 들어간다.

### 2.3 `[ShowIf(필드)]` — 다른 필드가 "있을 때"만 보임

```csharp
using System;
using GBible;

[Serializable]
public class Gear
{
    public bool stackable;
    [ShowIf(nameof(stackable))] public int maxStack = 99;   // stackable이 참일 때만
    public string setName = "";
    [ShowIf(nameof(setName))] public int setBonus;          // setName이 비어 있지 않을 때만
}
```

조건 필드의 값으로 판단한다.

| 조건 필드의 형식 | 보이는 때 |
|---|---|
| `bool` | 참일 때 |
| `string` | 비어 있지 않을 때 |
| 그 밖(객체·기능 필드 등) | `null`이 아닐 때 |

### 2.4 `[ShowIf(필드, 값)]` — 다른 필드가 그 값일 때만 보임

```csharp
public enum GearKind { Weapon, Armor, Ring }

[Serializable]
public class Equipment
{
    public GearKind kind;
    [ShowIf(nameof(kind), GearKind.Weapon)] public int attack;
    [ShowIf(nameof(kind), GearKind.Armor)] public int defense;
    [ShowIf(nameof(kind), "Ring")] public string ringEffect = "";   // 글자로 써도 같다
}
```

조건 필드의 값과 적은 값을 **글자로 바꿔 비교**한다. enum은 이름(`"Weapon"`), 숫자는 숫자 글자(`"3"`), 참거짓은 `"True"`/`"False"`로 비교된다. 조건 필드는 같은 객체 안(부모 클래스 포함)에서 이름으로 찾고, 찾지 못하면 늘 보인다.

`[Hide]`·`[ShowIf]`는 인스턴스 편집 화면(DB 상세의 값 탐색기)에만 적용된다. 모니터처럼 실제 값을 살피는 곳에서는 모든 필드를 보인다.

### 2.5 기능 필드 — 기능마다 필드 하나

```csharp
using System;
using GBible;
using UnityEngine;

[Serializable]
public class Equip
{
    public string slot = "weapon";
    public int attack;
}

[Serializable]
public class Consumable
{
    public int heal = 30;
}

[Serializable]
public class Item
{
    public string id;
    public string name;
    [SerializeReference] public Equip equip;            // null = 장착 기능 없음
    [SerializeReference] public Consumable consumable;  // null = 소모 기능 없음
}
```

| 규칙 | 내용 |
|---|---|
| 형식 | 클래스 하나(리스트·배열·구조체·글자가 아님). 조건·보상·효과·대상 같은 Block 형식이 아님 |
| 표시 | `[SerializeReference]`가 붙어 있어야 기능 필드로 인정된다 |
| `null` | 기능이 없다. 인스턴스 편집 화면의 **+ 기능**으로 붙인다 |
| 넣을 수 있는 형식 | 선언한 형식(만들 수 있으면)과 그 자식 클래스 중 추상이 아니고 인자 없는 생성자가 있는 것 |

게임 코드에서는 `is` 패턴으로 확인하고 꺼낸다.

```csharp
if (item.equip is { } e) WearInSlot(e.slot, e.attack);   // WearInSlot은 게임 코드의 함수
if (item.consumable is { } c) hero.hp += c.heal;
```

"한 개념 = 한 클래스"다. 아이템 종류마다 상속으로 나누지 않고, 붙였다 뗐다 하는 기능 필드로 표현한다. 기능 클래스에 자식 클래스를 두면 같은 필드에 여러 변형을 넣을 수 있다(`Equip` → `TwoHandedEquip` 등).

### 2.6 사용 기능 `Usable`

```csharp
using System;
using GBible;
using UnityEngine;

[Serializable]
public class Potion
{
    public string id;
    [SerializeReference] public Usable usable;   // 붙어 있으면 Game.Use(인스턴스 ID, 사용자, 대상들)로 쓴다
}
```

코어가 제공하는 기능 클래스다. 인스턴스에서 **+ 기능**으로 붙이면 트리거(사용 + 조건)·결과 칸을 에디터에서 조립한다. 게임이 켜질 때 사용 기능이 붙은 인스턴스를 찾아 **인스턴스 ID**로 능력처럼 등록하므로 `Game.Use("potion", hero)`로 쓴다. 개수 줄이기 같은 뒤처리는 게임 코드가 한다. 자세히는 [16-Abilities-Usable.md](16-Abilities-Usable.md).

### 2.7 원본 찾기: `session.Instances`

```csharp
object original = Game.Session.Instances?.Invoke("ironSword");
```

`Func<string, object>`이다. 인스턴스 ID를 넣으면 **원본** 객체를 준다(없으면 `null`). `GBibleRuntime`이 게임 시작 때 DB 데이터로 채운다. 세션을 직접 만들었다면 직접 넣는다.

```csharp
var originals = new Dictionary<string, object> { ["ironSword"] = new Item { name = "철검", price = 120 } };
session.Instances = id => originals.TryGetValue(id, out var v) ? v : null;
```

원본은 읽기만 한다. 고치면 이후 모든 사본이 바뀌고, 에디터 플레이 중에는 DB 에셋 값까지 바뀔 수 있다.

### 2.8 사본 만들기: `session.CopyInstance(id)`

```csharp
var sword = (Item)Game.Session.CopyInstance("ironSword");   // 없는 ID면 null
```

원본의 **깊은 사본**을 돌려준다(`object`라서 형변환한다).

- 비교 코드가 있는 형식(`[Serializable]` 클래스)은 생성 코드로 복사한다(실행 중 리플렉션 없음). 비교 코드가 없는 형식만 예전 방식(리플렉션)으로 복사한다.
- 안의 리스트·클래스·기능 필드까지 모두 새 객체다. 원본, 그리고 같은 원본에서 만든 다른 사본과 공유하지 않는다.
- 스프라이트·DB 항목 같은 **에셋 참조는 같은 참조**를 그대로 잇는다(에셋을 복제하지 않는다).

생성된 상수가 있으면 문자열 대신 쓴다: DB 인스턴스 ID마다 `GameIds.Instance.IronSword`(ID `ironSword`)가 생긴다.

### 2.9 에셋을 직접 들고 있을 때: `InstanceData`

```csharp
using System.Linq;
using GBible.Data;

var database = GameDatabase.Load();
foreach (InstanceData asset in database.All<InstanceData>().Where(a => a.value is Item))
{
    Item copy = asset.CreateInstance<Item>();   // 깊은 사본
    object any = asset.CreateInstance();        // 형식을 모를 때
}
object same = InstanceData.Copy(someObject);    // 아무 객체의 깊은 사본(같은 규칙)
```

| 멤버 | 뜻 |
|---|---|
| `instanceType` | 고른 형식의 전체 이름(문자열) |
| `value` | 원본 객체(`[SerializeReference]`) |
| `InstanceType` | `instanceType`을 찾은 `Type` |
| `CreateInstance()` / `CreateInstance<T>()` | 원본의 깊은 사본 |
| `InstanceData.Copy(object)` | 넘긴 객체의 깊은 사본 |

`InstanceData`는 `GBible.Data` 네임스페이스에 있다. 보통은 ID만 알면 되므로 `session.CopyInstance`를 쓴다.

### 2.10 보상으로 사본 넣기: `GrantKind.Instance`

DB의 결과 칸 **플레이어 데이터에 주기**(`PlayerDataReward`)는 필드 형식에 따라 주는 방법이 정해진다(`PlayerDataGrants.KindOf`). 클래스 형식 필드는 `GrantKind.Instance`로, **인스턴스 ID를 고르면 그 사본**이 들어간다(여기서 새로 만들지는 않는다).

| 대상 필드 | 주는 방법 | 칸 |
|---|---|---|
| 클래스 필드(또는 값 전체) | `Instance`: 고른 인스턴스의 사본으로 바꾼다 | 값(클래스면 인스턴스 ID) |
| 클래스 리스트·배열 | `List`: 요소마다 인스턴스의 사본을 넣는다 | 넣을 요소(숫자·값·인스턴스 ID) |
| 값이 클래스인 딕셔너리 | `Dictionary`: 키 + 인스턴스 사본(있던 키는 덮어쓴다) | 딕셔너리 키 + 넣을 요소 |

코드로 같은 일을 하면 다음과 같다.

```csharp
new PlayerDataReward { key = "player.inventory", path = "items", items = { "ironSword", "potion" } }.Grant(session);
new PlayerDataReward { key = "player.hero", path = "weapon", value = "ironSword" }.Grant(session);
```

없는 인스턴스 ID는 넣지 않고 경고만 남긴다.

---

**인스턴스를 가리키는 칸: `[DataRef]`.** 다른 데이터에서 인스턴스를 가리킬 때는 글자 ID 칸에 `[DataRef]`를 단다(원본은 객체로 들고 다니지 않는다).

| 어트리뷰트 | 목록에 뜨는 것 | 다른 것을 고르면 |
|---|---|---|
| `[DataRef(typeof(Card))]` (인스턴스 값의 클래스) | 값이 `Card`(또는 그 하위 클래스)인 인스턴스만 | 검증 오류 "'goblin'은(는) Card 인스턴스가 아닙니다" |
| `[DataRef(typeof(InstanceData))]` | 모든 인스턴스 | — |

```csharp
[Serializable] public class DeckCard
{
    public string id;
    [DataRef(typeof(Card))] public string card;          // 카드 칸에 몬스터가 들어갈 수 없다
}
[Serializable] public class Enemy
{
    public string id;
    [DataRef(typeof(Monster))] public string monster;
}
```

클래스로 거른 칸도 ID 바꾸기 따라가기, 출시 뒤 저장 안의 ID 이어받기는 똑같이 된다.

## 3. 상황별 예제

### 3.1 RPG: 장비 아이템 정의 · DB에서 채우기 · 드롭

```csharp
using System;
using System.Collections.Generic;
using GBible;
using UnityEngine;

[KeywordGroup("등급")]
public static class Rarity
{
    public const string Common = "common";
    public const string Rare = "rare";
    public const string Legendary = "legendary";
}

[Serializable]
public class WeaponPart
{
    public int attack = 5;
    public float critRate;
}

[Serializable]
public class GearItem
{
    public string id;
    public string name;
    public Sprite icon;
    [Keyword(typeof(Rarity))] public string rarity = Rarity.Common;
    public int price;
    [SerializeReference] public WeaponPart weapon;   // 무기 기능
    [SerializeReference] public Usable usable;       // 사용 기능
    [Hide] public int enhanceLevel;                  // 게임 중에만 오르는 값
}

[Serializable]
public class Bag
{
    public List<GearItem> items = new List<GearItem>();
}

[Serializable]
public class DropLoot : GameAction
{
    [DataRef(typeof(InstanceData))] public List<string> table = new List<string> { "ironSword", "steelSword", "potion" };   // DB 원본은 글자 ID로

    protected override void Run()
    {
        var pick = table[Game.Random("drop").Range(0, table.Count)];
        var item = (GearItem)Game.Session.CopyInstance(pick);
        if (item == null) throw new InvalidOperationException($"인스턴스 '{pick}'이(가) 없습니다.");
        item.id = Game.NewId("item");             // 사본마다 다른 id
        Game.All<Bag>()[0].items.Add(item);
    }
}
```

DB에서 `ironSword`(형식 `GearItem`, **+ 기능** → `weapon`)와 `potion`(**+ 기능** → `usable`)을 만든다. 같은 원본에서 만든 사본은 원본의 `id` 값까지 그대로 복사되므로, 개체로 찾을 객체는 `Game.NewId`로 사본마다 새 id를 준다. 행동 안에서 넣었으므로 Undo하면 드롭이 통째로 사라진다(난수도 돌아가 다시 하면 같은 아이템).

### 3.2 전술: 몬스터 원본으로 적 배치

```csharp
using System;
using System.Collections.Generic;
using GBible;

[Serializable]
public class Foe
{
    public string id;
    public string team = "enemy";
    public int x, y;
    public int hp = 20;
    public int attack = 4;
}

[Serializable]
public class Stage
{
    public List<Foe> units = new List<Foe>();
}

[Serializable]
public class SpawnWave : GameAction
{
    [DataRef(typeof(Monster))] public string monster = "goblin";   // Monster 인스턴스만
    public int count = 3;

    protected override void Run()
    {
        var stage = Game.All<Stage>()[0];
        for (int i = 0; i < count; i++)
        {
            var foe = (Foe)Game.Session.CopyInstance(monster);
            foe.id = Game.NewId(monster);
            foe.x = 5 + i;
            stage.units.Add(foe);
        }
    }
}
```

### 3.3 카드: 종류에 따라 다른 칸 보이기

```csharp
using System;
using GBible;

public enum CardKind { Attack, Skill, Power }

[Serializable]
public class Card
{
    public string id;
    public CardKind kind;
    public int cost = 1;
    [ShowIf(nameof(kind), CardKind.Attack)] public int damage = 6;
    [ShowIf(nameof(kind), CardKind.Skill)] public int block = 5;
    public bool exhaust;
    [ShowIf(nameof(exhaust))] public string exhaustText = "";
    [Hide] public int timesPlayed;
}
```

기획자가 `kind`를 바꾸면 그 종류에 맞는 칸만 보인다. 숨겨진 칸의 값도 저장·복사는 된다.

### 3.4 버프 원본을 상태 리스트에 붙이기

```csharp
using System;
using System.Collections.Generic;
using GBible;

[Serializable]
public class BuffState
{
    public string name;
    public int turns = 3;
    public int attackBonus;
}

[Serializable]
public class Hero
{
    public string id;
    public List<BuffState> buffs = new List<BuffState>();
}

[Serializable]
public class ApplyBuff : GameAction
{
    public Hero hero;                                              // 플레이어 데이터 안의 객체: 참조 필드
    [DataRef(typeof(InstanceData))] public string buff = "rage";   // DB 원본: 글자 ID
    protected override void Run()
    {
        if (Game.Session.CopyInstance(buff) is BuffState copy) hero.buffs.Add(copy);
    }
}
```

사본마다 `turns`가 따로 줄어든다(원본은 그대로).

행동에서 DB 원본(인스턴스)을 가리킬 때는 객체가 아니라 `[DataRef]` 글자 ID로 둔다. 원본은 플레이어 데이터가 아니라서 참조 필드로 넣으면 "찾을 수 없습니다"로 실패한다([02-Actions-Undo.md](02-Actions-Undo.md) 2.2).

---

## 4. 주의할 점 · 자주 하는 실수

| 증상 | 원인과 해결 |
|---|---|
| 원본 값이 게임 중에 바뀜 | `session.Instances`로 받은 원본을 고쳤다. 늘 `CopyInstance`의 사본을 쓴다. |
| 같은 아이템 두 개가 서로 값을 공유 | 같은 사본을 두 곳에 넣었다. 넣을 때마다 `CopyInstance`를 따로 부른다. |
| `Game.Find`가 엉뚱한 사본을 찾음 | 사본은 원본의 `id` 값까지 복사한다. 사본마다 `Game.NewId`로 id를 새로 준다. |
| 형식 목록에 내 클래스가 없음 | `[Serializable]`이 없거나, 추상 클래스이거나, 공개가 아니거나, 담을 수 없는 필드(함수·`object`·다차원 배열)가 있거나, 내 코드 범위(**MomoiProject › 내 코드 범위**) 밖이다. |
| **+ 기능** 버튼이 안 보임 | 비어 있는 기능 필드가 없다. 필드에 `[SerializeReference]`가 있는지, 리스트·글자·Block 형식이 아닌지 확인한다. |
| **+ 기능** 목록에 원하는 형식이 없음 | 그 형식이 추상이거나 인자 없는 생성자가 없다. |
| `[ShowIf]`가 늘 보임 | 조건 필드 이름이 틀렸다(찾지 못하면 늘 보인다). `nameof`로 쓴다. 값 비교는 글자 비교이므로 enum은 이름과 같아야 한다. |
| 에디터에서 `[Hide]`를 껐는데 코드가 그대로 | 저장(Ctrl+S) 전이다. 저장하면 코드에 쓰고 다시 컴파일한다. |
| 사본의 스프라이트를 바꿨더니 다른 사본도 바뀜 | 에셋 참조는 복제하지 않고 같은 참조를 잇는다. 스프라이트 객체 자체를 고치지 말고 필드에 다른 스프라이트를 넣는다. |
