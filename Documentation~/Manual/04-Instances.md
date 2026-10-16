# 04. 인스턴스

**한 줄 요약**: 코드로 정의한 클래스(아이템·몬스터·카드 …)의 **원본 하나**(철검, 고블린, 화염구 카드 …)를 DB에서 값으로 채워 두는 것이 인스턴스다. 게임은 원본을 읽기만 하고, 판 데이터에는 원본 ID와 바뀌는 값을 둔다(원본이 곧 판 객체의 시작 모양이면 **깊은 사본**을 넣는다).

**언제 쓰는가**

- 같은 모양의 원본 여러 개(아이템·몬스터·카드): 클래스·로직·원본은 AI가 만들고, 개발자가 DB에서 수치를 바꿀 때
- 드롭·상점·소환·덱 구성처럼 "정해 둔 원본"으로 판의 객체를 게임 중에 만들 때
- 아이템마다 붙거나 안 붙는 기능(장착·사용·소모)을 필드 하나로 표현할 때

**꼭 지킬 것**(자세히는 괄호의 절)

- 원본 클래스는 규칙 어셈블리(`Assets/MomoiProject/Code/Rules`)의 `[Serializable]` 클래스, 원본 하나하나는 AI가 DB 반영 스크립트로 만든다(1.1). 개발자는 값만 바꾼다.
- **판 데이터에는 원본 ID + 바뀌는 값만 둔다**(AI 관례 1절). 판 객체 = 고유 `id` + 원본 ID 필드(`[DataRef(typeof(원본 클래스))]`) + 판에서 바뀌는 값(레벨·강화·쌓이는 수). 이름·설명·그림·등급·가격 같은 고정 값은 원본에서 읽는다(`(원본 클래스)Game.Session.Instances(id)`, 2.7). 조각(카드·유물·조커)은 늘 이 모양이다.
- 원본을 통째로 판에 복사하는 것(`CopyInstance`, 2.8)은 원본이 곧 판 객체의 시작 모양일 때만 쓴다 — 예: 적 유닛(체력·공격력이 판에서 바뀌고 스탯(28장)이 그 객체의 필드여야 함). 이때 원본 클래스에 스프라이트 같은 에셋 참조를 두지 않는다(판 저장에 담기지 않는다 — 01장 8절). 사본마다 `Game.NewId`로 새 id.
- 원본은 게임 중에 고치지 않는다(4절). 다른 데이터가 원본을 가리킬 때는 객체가 아니라 `[DataRef]` 글자 ID(2.10 아래).

---

## 0. 용어

| 용어 | 뜻 |
|---|---|
| 클래스 | 프로그래머가 코드로 만든 `[Serializable]` 클래스(예: `Item`). 한 개념 = 한 클래스. |
| 인스턴스 (`InstanceData`) | 클래스로 만든 원본 하나를 담은 DB 항목. 에셋은 저장용 껍데기이고, 실제 값은 `value` 필드의 클래스 객체다. DB 탭 이름은 "인스턴스". |
| 사본 | 원본을 깊게 복사한 새 객체. 안의 리스트·클래스까지 모두 새로 만든다. 원본·다른 사본과 공유하지 않는다. |
| 기능 필드 | 비어 있을 수 있는 `[SerializeReference]` 클래스 필드. 값이 있으면 그 기능이 붙은 것, `null`이면 없는 것. |
| 플레이어 데이터 | 게임 중에 바뀌는 값을 담는 저장소. 판 객체(원본 ID + 바뀌는 값)나 사본을 여기에 넣는다. [01-PlayerData.md](01-PlayerData.md) |
| 세션 (`GameSession`) | 게임 한 판의 실행 전체. 인스턴스 원본을 찾는 `Instances`와 사본을 만드는 `CopyInstance`가 여기 있다. |

---

## 1. 에디터에서 쓰는 법

### 1.1 인스턴스 만들기

DB › 인스턴스(`InstanceData`, 사이드바 **DB** 그룹)의 항목 추가·삭제·ID 바꾸기와 기능 필드 붙이기(**+ 기능**)는 개발자에게 잠겨 있다(21장 3-1). AI가 DB 반영 스크립트로 만든다(AI 관례 15절). 이미 있는 ID는 건너뛴다(개발자가 맞춘 값을 덮지 않는다).

```csharp
// Assets/MomoiProject/Code/Editor/Seeds/ContentSeeds.cs
static void Instance(string id, object value)
{
    if (EditorDatabase.Instance.FindById(id) != null) return;
    var data = (InstanceData)EditorDatabase.Staging.Add(typeof(InstanceData), id);
    data.instanceType = value.GetType().FullName;      // 형식(클래스 전체 이름)
    data.value = value;                                 // 원본 값. 기능 필드도 여기서 채운다(usable = new Usable { … })
}

[DbSeed("1")]
static void Items() => Instance("ironSword", new GearItem { price = 120, weapon = new WeaponPart { attack = 8 } });   // GearItem·WeaponPart: 3.1
```

새 항목은 저절로 적용되고 `GameIds.Instance.IronSword`가 생긴다(Unity가 다시 컴파일한 뒤 — 20장 7절).

개발자는 항목 상세의 **값** 칸(탐색기)에서 필드 값을 바꾼다. `[Hide]` 필드는 보이지 않고, `[ShowIf]` 필드는 조건이 맞을 때만 보인다(2.2~2.4). **형식** 칸을 다른 클래스로 바꾸면 값이 그 클래스의 새 객체로 바뀐다(같은 형식을 다시 고르면 기존 값을 둔다).

DB 검증은 형식이 비었거나("형식이 선택되지 않았습니다."), 형식을 찾을 수 없거나, 값이 비었거나("값이 비어 있습니다. 형식을 다시 고르면 새로 만들어집니다."), 값의 형식이 고른 형식과 다르면 오류를 띄운다.

### 1.2 필드 숨기기를 에디터에서 바꾸기

`[Hide]`·`[ShowIf]`는 코드가 원본이다(AI가 코드에 붙인다). 사이드바 **요소 › 클래스** 탭에서 클래스를 누르면(또는 인스턴스 탭 머리의 **필드 연결**) 클래스 상세의 필드 줄에 보이기만 한다: "DB › 인스턴스에서 보이지 않는 필드입니다(값은 그대로 저장·복사).  [Hide]", "'kind'이(가) 'Weapon'일 때만 보임  [ShowIf]", 기능 필드 안내.

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

인스턴스의 형식 목록에는 내 코드 범위 안의 공개 `[Serializable]` 클래스 중 추상 클래스가 아니고 담을 수 없는 필드가 없는 것이 나온다. 필드 규칙(무엇이 저장·기록되는가)은 플레이어 데이터와 같다([01-PlayerData.md](01-PlayerData.md) 3절).

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

값은 여러 개 적을 수 있다. 그중 하나와 같으면 보인다.

```csharp
[ShowIf(nameof(kind), GearKind.Weapon, GearKind.Ring)] public int attack;   // 무기 또는 반지일 때
```

`[Hide]`는 인스턴스 편집 화면(DB 상세의 값 탐색기)에, `[ShowIf]`는 DB의 모든 칸(목록의 줄 · 상세 창 맨 위 칸 · 묶음 필드(곡선·지속 설정·설정 값 …, `21-Editor-Database.md` 5절) · 블록 안쪽 칸 · 조건식 블럭 · 값 탐색기)에 적용된다. 조건 필드 자신이 숨어 있으면 그에 딸린 필드도 숨는다(곡선 '방식'이 없음이면 '최솟값으로 자르기'가 숨고, 그에 딸린 '최솟값'도 숨는다). 모니터처럼 실제 값을 살피는 곳에서는 모든 필드를 보인다.

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
| `null` | 기능이 없다. 붙이는 것은 구조라 AI가 반영 스크립트에서 값을 넣는다(1.1). **+ 기능** 버튼은 구조 편집(`MomoiProject › 개발 › 구조 편집 열기`)에서만 보인다 |
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

코어가 제공하는 기능 클래스다. AI가 반영 스크립트에서 `usable = new Usable { … }`로 붙이고 트리거(사용 + 조건)·결과 Block을 채운다(1.1, 16장). 게임이 켜질 때 사용 기능이 붙은 인스턴스를 찾아 **인스턴스 ID**로 액티브 효과처럼 등록하므로 `Game.Use("potion", hero)`로 쓴다. 개수 줄이기 같은 뒤처리는 게임 코드가 한다. 자세히는 [16-Abilities-Usable.md](16-Abilities-Usable.md).

### 2.7 원본 찾기: `session.Instances`

```csharp
object original = Game.Session.Instances?.Invoke("ironSword");
var def = (GearItem)Game.Session.Instances(owned.gear);   // owned: 판 객체(3.1의 OwnedGear). 고정 값(가격·그림)은 원본에서
```

`Func<string, object>`이다. 인스턴스 ID를 넣으면 **원본** 객체를 준다(없으면 `null`). `GBibleRuntime`이 게임 시작 때 DB 데이터로 채운다. 세션을 직접 만든 규칙 테스트는 직접 넣는다. 원본 읽기는 규칙 함수 하나(`Rules`, AI 관례 3절)에 모아 행동·Block이 같은 길로 읽게 한다.

```csharp
var originals = new Dictionary<string, object> { ["ironSword"] = new Item { name = "철검", price = 120 } };
session.Instances = id => originals.TryGetValue(id, out var v) ? v : null;
```

원본은 읽기만 한다. 고치면 이후 모든 사본이 바뀌고, 에디터 플레이 중에는 DB 에셋 값까지 바뀔 수 있다.

### 2.8 사본 만들기: `session.CopyInstance(id)`

```csharp
var foe = (Foe)Game.Session.CopyInstance("goblin");   // 없는 ID면 null
foe.id = Game.NewId("goblin");                        // 사본은 원본의 id 값까지 복사한다 — 새로 준다
```

원본의 **깊은 사본**을 돌려준다(`object`라서 형변환한다). 원본이 곧 판 객체의 시작 모양일 때만 쓴다(맨 위 '꼭 지킬 것' — 예: 3.2의 적). 그 밖에는 판 객체에 원본 ID만 둔다.

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

DB의 결과 칸 **플레이어 데이터에 주기**(`PlayerDataReward`)는 필드 형식에 따라 주는 방법이 정해진다(`PlayerDataGrants.KindOf`). 클래스 형식 필드는 `GrantKind.Instance`로, **인스턴스 ID를 고르면 그 사본**이 들어간다(여기서 새로 만들지는 않는다). 사본을 통째로 넣으므로 2.8과 같은 경우에만 쓴다. 판 객체가 원본 ID + 바뀌는 값이면 그 객체를 만드는 프로젝트 결과 Block(12장)이나 행동으로 넣는다. 원본 ID 글자 목록(`List<string>`)이면 글자(인스턴스 ID)가 그대로 들어간다.

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

### 3.1 RPG: 장비 원본 · 판에는 원본 ID + 강화 수치 · 드롭

```csharp
using System;
using System.Collections.Generic;
using GBible;
using GBible.Data;
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
public class GearItem                                   // 원본(DB › 인스턴스). 보일 이름은 텍스트 키 '<id>.name'(23장)
{
    public Sprite icon;                                 // 원본에만 둔다(판 저장에 넣지 않는다)
    [Keyword(typeof(Rarity))] public string rarity = Rarity.Common;
    public int price;
    [SerializeReference] public WeaponPart weapon;      // 무기 기능
    [SerializeReference] public Usable usable;          // 사용 기능
}

[Serializable]
public class OwnedGear                                  // 판에 든 장비 하나
{
    public string id;                                   // 고유 id
    [DataRef(typeof(GearItem))] public string gear;     // 원본 ID
    public int enhanceLevel;                            // 판에서 오르는 값
}

[Serializable]
public class Bag
{
    public List<OwnedGear> items = new List<OwnedGear>();
}

[Serializable, BlockInfo("전리품 얻기")]
public class DropLoot : GameAction
{
    [DataRef(typeof(WeightTableData))] public string table;   // DB › 가중치 표(30장)

    protected override void Run()
    {
        var gear = Game.Roll(table);                    // 원본 ID 하나(꽝이면 null)
        if (gear == null) return;
        Game.All<Bag>()[0].items.Add(new OwnedGear { id = Game.NewId("gear"), gear = gear });
    }
}

// 고정 값 읽기: var def = (GearItem)Game.Session.Instances(owned.gear);  def.weapon?.attack · def.icon
```

DB 반영 스크립트로 `ironSword`(`GearItem`, `weapon` 채움)·`potion`(`usable` 채움)과 가중치 표를 만든다(1.1). 같은 원본을 두 번 얻어도 판 객체는 둘이고 `enhanceLevel`이 따로 오른다. 행동 안에서 넣었으므로 Undo하면 드롭이 통째로 사라진다(난수도 돌아가 다시 하면 같은 아이템).

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

[Serializable, BlockInfo("적 불러오기")]
public class SpawnWave : GameAction
{
    [DataRef(typeof(Foe))] public string monster = "goblin";   // Foe 인스턴스만
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

적은 원본(체력·공격력)이 곧 판 객체의 시작 모양이고 그 값이 판에서 바뀌므로(공격력은 Modifier가 붙는 스탯 — 28장) 사본을 쓴다(2.8). 원본 ID가 필요하면 `Foe`에 `[DataRef(typeof(Foe))] public string monster` 같은 칸을 두고 사본에 채운다.

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

개발자가 `kind`를 바꾸면 그 종류에 맞는 칸만 보인다. 숨겨진 칸의 값도 저장·복사는 된다.

### 3.4 버프 원본을 상태 리스트에 붙이기

턴 수·중첩·만료가 있는 버프는 인스턴스 사본으로 직접 리스트를 만들지 않고 DB › 상태 효과로 만든다(29장 — 지속·중첩·스탯 보정·턴마다의 효과·Undo가 들어 있다). `Game.ApplyStatus(hero, GameIds.Status.Rage)`.

행동에서 DB 원본(인스턴스)을 가리킬 때는 객체가 아니라 `[DataRef]` 글자 ID로 둔다. 원본은 플레이어 데이터가 아니라서 참조 필드로 넣으면 "찾을 수 없습니다"로 실패한다([02-Actions-Undo.md](02-Actions-Undo.md) 2.2).

---

## 4. 주의할 점 · 자주 하는 실수

| 증상 | 원인과 해결 |
|---|---|
| 원본 값이 게임 중에 바뀜 | `session.Instances`로 받은 원본을 고쳤다. 원본은 읽기만 하고, 바뀌는 값은 판 객체에 둔다. |
| 판 데이터에 이름·가격·그림이 복사돼 개발자가 고친 값이 진행 중인 판에 안 먹음 | 원본을 통째로 판에 넣었다. 판 객체에는 원본 ID + 바뀌는 값만 두고 고정 값은 원본에서 읽는다(맨 위 '꼭 지킬 것'). |
| 불러온 뒤 아이콘이 비어 있음·저장할 때 "저장할 수 없는 참조입니다" 경고 | 스프라이트 필드가 있는 원본을 사본으로 판 데이터에 넣었다. 에셋 참조는 원본에만 두고 판 객체에는 원본 ID를 둔다(01장 8절). |
| 같은 아이템 두 개가 서로 값을 공유 | 같은 사본을 두 곳에 넣었다. 넣을 때마다 `CopyInstance`를 따로 부른다. |
| `Game.Find`가 엉뚱한 사본을 찾음 | 사본은 원본의 `id` 값까지 복사한다. 사본마다 `Game.NewId`로 id를 새로 준다. |
| 형식 목록에 내 클래스가 없음 | `[Serializable]`이 없거나, 추상 클래스이거나, 공개가 아니거나, 담을 수 없는 필드(함수·`object`·다차원 배열)가 있거나, 내 코드 범위(`ProjectSettings/MomoiProject/ProjectCode.json`) 밖이다. |
| 기능 필드로 인정되지 않음 | 필드에 `[SerializeReference]`가 없거나, 리스트·글자·Block 형식이다(2.5). 넣을 형식이 추상이거나 인자 없는 생성자가 없으면 넣을 수 없다. |
| `[ShowIf]`가 늘 보임 | 조건 필드 이름이 틀렸다(찾지 못하면 늘 보인다). `nameof`로 쓴다. 값 비교는 글자 비교이므로 enum은 이름과 같아야 한다. |
| 검증 오류 "'goblin'은(는) Card 인스턴스가 아닙니다" | `[DataRef(typeof(Card))]` 칸에 다른 클래스의 인스턴스 ID가 들어 있다. 그 클래스의 인스턴스를 고른다. |
| 사본의 스프라이트를 바꿨더니 다른 사본도 바뀜 | 에셋 참조는 복제하지 않고 같은 참조를 잇는다. 스프라이트 객체 자체를 고치지 말고 필드에 다른 스프라이트를 넣는다. |
