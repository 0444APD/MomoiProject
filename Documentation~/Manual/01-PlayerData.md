# 01. 플레이어 데이터

**한 줄 요약**: 게임 중에 바뀌고, 되돌리거나(Undo) 저장하거나 조건으로 검사할 값은 모두 "플레이어 데이터"에 둔다. 평범한 `[Serializable]` C# 클래스를 그대로 쓰고, 시스템이 바뀐 곳을 찾아 기록한다.

**언제 쓰는가**

- 골드·경험치·턴 수처럼 게임 규칙이 바꾸는 값
- 인벤토리·덱·전장의 유닛 목록처럼 여러 값이 모인 객체
- 도전과제·로그·트리거 조건이 검사할 값
- 저장 파일에 남길 값(프로필, 진행 중인 판)

넣지 않는 것: 매 프레임 바뀌는 위치·속도·애니메이션 상태, 화면 상태, 다른 값으로 계산할 수 있는 값(합산 공격력 등), 게임 중에 바뀌지 않는 정의 데이터(DB 항목). 판단 기준은 "되돌리거나, 저장하거나, 기록하거나, 조건으로 검사할 일이 있는가"이다.

---

## 0. 용어

| 용어 | 뜻 |
|---|---|
| 플레이어 데이터 | ID(키) 하나에 값 하나를 담는 칸. 값은 숫자·글자 같은 **단순 값**이거나 클래스 객체 같은 **데이터 값**이다. 모든 플레이어 데이터는 세션의 `PlayerDataStore`(`session.Data`)에 산다. |
| 세션 (`GameSession`) | 게임 한 판의 실행 전체. 플레이어 데이터·이벤트·난수·기록·행동을 모두 들고 있다. 싱글 게임에서는 `GBibleRuntime.Session` 하나를 시스템이 만든다. |
| 구역 (`DataZone`) | 플레이어 데이터가 어느 저장 파일에 들어가는지. `Session`(이번 판)과 `Meta`(프로필) 두 가지뿐이다. |
| 행동 (`GameAction`) | 플레이어 입력 하나(이동·구매·턴 넘기기). 행동 하나가 Undo 한 단위다. 자세히는 [02-Actions-Undo.md](02-Actions-Undo.md). |
| Undo | 마지막 행동이 바꾼 값을 행동 전으로 되돌리는 것. |
| Mirror | 시스템이 데이터 값마다 들고 있는 사본. 마지막으로 비교한 순간의 모습이다. |
| Sync | 실제 값과 Mirror를 비교해 바뀐 곳만 기록하고 Mirror를 실제 값에 맞추는 동작. |
| ChangeSet | Sync 한 번이 찾아낸 변경 묶음. "앞으로 기록"(새 값)과 "뒤로 기록"(옛 값)을 함께 담는다. 앞으로 기록은 멀티플레이에서 다른 쪽에 보내 같은 모습을 만들고, 뒤로 기록은 Undo에 쓴다. |
| 비교 코드 | 컴파일할 때 `GBible.CodeGen`이 `[Serializable]` 클래스·구조체 안에 넣는 메서드(`__gb_*`). Sync는 이 코드로 필드를 비교하므로 실행 중 리플렉션이 없다. |
| 예전 방식 | 비교 코드로 다룰 수 없는 데이터에 쓰는 느린 비교(리플렉션으로 값 전체를 복제·비교). 동작은 하지만 기록 규칙이 다르다(6장). |

---

## 1. 에디터에서 정의하기

플레이어 데이터는 보통 DB에서 만든다. 만들어 두면 게임이 시작될 때 자동으로 등록되고, 코드에서는 생성된 상수로 쓴다.

1. 메뉴 **MomoiProject › DB**을 연다.
2. 왼쪽 사이드바의 **선언** 그룹에서 **플레이어 데이터** 탭을 고른다.
3. 오른쪽 위 **+ 새로 만들기**를 누른다.
4. **새 플레이어 데이터** 창에 ID를 적고 **만들기**를 누른다. ID는 영문 소문자로 시작하고 단어 사이를 대문자로 잇는다(띄어쓰기·밑줄 없음). 분류는 점(.)으로 나눈다. 예: `player.gold`, `player.inventory`, `battle.board`.
5. 목록에서 항목을 눌러 상세를 열고 세 칸을 채운다.
   - **구역**: `Session`(이번 판에만, 판 저장에 들어감) 또는 `Meta`(판이 바뀌어도 남음, 프로필 저장에 들어감).
   - **값 형식**: 목록에서 고른다. 목록은 세 묶음이다.
     - 기본 형식: 숫자 (double) · 정수 (int) · 큰 정수 (long) · 소수 (float) · 참/거짓 (bool) · 글자 (string) · 날짜·시각 (DateTime) · 시간 길이 (TimeSpan)
     - enum: 내 코드 범위 안의 enum
     - 데이터 클래스: 내 코드 범위 안의 공개 `[Serializable]` 클래스 중 추상 클래스가 아니고 담을 수 없는 필드가 없는 것(추가 등록 없이 자동으로 나타난다)
   - **기본값**: 단순 값에만 쓴다. 클래스는 기본값 칸을 쓰지 않고 클래스의 초기값(필드 초기화·생성자)으로 시작한다.
6. **저장  Ctrl+S**를 누른다. 저장하면 식별자 코드 `GameIds.cs`가 자동으로 다시 만들어진다(직접 만들려면 **MomoiProject › 식별자 코드 다시 만들기**).

DB 검증은 값 형식을 찾을 수 없거나, 담을 수 없는 필드가 있거나, 기본값을 읽을 수 없으면 오류를 띄운다. 클래스가 비교 코드로 다뤄지지 않으면 "비교 코드로 다룰 수 없어 예전 방식(리플렉션, 더 느림)으로 비교합니다" 경고를 띄운다.

플레이 중에는 **MomoiProject › 모니터**의 **데이터** 탭에서 **판 데이터**(세션 구역)와 **프로필 데이터**(메타 구역)를 탐색기로 보고 고칠 수 있다(모니터 자세히는 [22-Monitor-Debug.md](22-Monitor-Debug.md)).

---

## 2. 코드로 쓰는 법 — 문법 요소

### 2.1 데이터 클래스: `[Serializable]`

```csharp
using System;
using System.Collections.Generic;

[Serializable]
public class Item
{
    public string id;
    public string name;
    public int price;
    public int level = 1;
    public List<string> tags = new List<string>();
}

[Serializable]
public class Inventory
{
    public int gold;
    public List<Item> items = new List<Item>();
}
```

특별한 상속도 래퍼 타입도 없다. `[Serializable]`만 있으면 컴파일할 때 비교 코드가 생기고, 게임 코드는 `List`·`Dictionary`·배열·구조체를 그냥 쓴다. 안쪽 클래스(`Item`)에도 `[Serializable]`을 붙인다.

### 2.2 `[PlayerData]` 표시 (선택)

```csharp
using System;
using GBible;

[Serializable, PlayerData]
public class Party { public int gold; }
```

`[PlayerData]`는 "플레이어 데이터로 쓰는 클래스"라는 표시다. 비교 코드 생성에서는 `[Serializable]`과 같은 뜻이고, 없어도 동작은 같다. DB의 값 형식 목록과 Unity 직렬화(인스턴스 저장 등)는 `[Serializable]`을 보므로 `[PlayerData]`만 단독으로 쓰지 말고 항상 `[Serializable]`과 함께 쓴다.

### 2.3 생성된 키 `GameIds.PlayerData.X`

```csharp
using GBible;
using GBible.Ids;

PlayerDataKey<int> goldKey = GameIds.PlayerData.PlayerGold;          // ID "player.gold"
PlayerDataKey<Inventory> bagKey = GameIds.PlayerData.PlayerInventory; // ID "player.inventory"
string id = goldKey.Id;                                               // "player.gold"
```

DB의 플레이어 데이터마다 `PlayerDataKey<값 형식>` 상수가 생긴다. 이름은 ID를 파스칼 표기로 바꾼 것이다(`player.gold` → `PlayerGold`). `PlayerDataKey<T>`는 `string`으로 자동 변환되므로 문자열 키를 받는 곳에도 그대로 넣을 수 있다. 문자열 대신 이것을 쓰면 오타와 형식 실수가 컴파일 오류로 드러난다.

`GameIds.cs`를 만드는 위치에서 값 형식을 참조할 수 없으면 그 키는 `public const string`(ID 문자열)로만 만들어진다.

### 2.4 이름으로 읽고 쓰기: `Game.Data()`

```csharp
using GBible;
using GBible.Ids;

GameApi game = GBibleRuntime.Game;
game.Data().PlayerGold += 100;                        // 단순 값: 읽고 쓰기
int gold = game.Data().PlayerGold;
game.Data().PlayerInventory.items.Add(new Item());    // 데이터 값: 꺼내서 안쪽을 고친다
```

`GameIds.cs`와 함께 생성되는 확장 메서드다. 단순 값은 속성 `get`/`set`(내부적으로 `Get`/`Set`), 데이터 값은 읽기 전용 속성(실제 객체를 돌려줌)이다.

### 2.5 저장소 `session.Data`의 읽기: `Get`

```csharp
var data = GBibleRuntime.Session.Data;

int gold = data.Get(GameIds.PlayerData.PlayerGold);          // 형식이 붙은 키: T를 추론
Inventory bag = data.Get<Inventory>("player.inventory");     // 문자열 키 + 형식
object raw = data.Get("player.gold");                         // 꺼낸 그대로(숫자는 double)
double n = data.GetNumber("player.gold");
bool seen = data.GetBool("tutorial.seen");
string name = data.GetString("player.name");
```

- 단순 숫자는 저장소 안에서 `double`로 보관하고, `Get<T>`가 선언한 형식(`int` 등)으로 바꿔 준다.
- 데이터 값은 **저장소가 관리하는 실제 객체**를 돌려준다. 복사본이 아니다.
- 정의되지 않은 키를 읽으면 "정의되지 않은 플레이어 데이터" 예외가 난다.

### 2.6 통째로 바꾸기: `Set`

```csharp
data.Set(GameIds.PlayerData.PlayerGold, 150);       // Set<T>(PlayerDataKey<T>, T)
data.Set("player.gold", 150);                        // Set(string, object)
data.Set("player.inventory", new Inventory());      // 데이터 값: 내용을 복사해 넣는다
```

- 단순 값은 선언한 형식과 맞지 않는 값(숫자 키에 글자 등)을 넣으면 `ArgumentException`이다.
- 데이터 값은 넘긴 객체를 그대로 넣지 않고 **내용을 복사해 지금 객체에 덮는다**. 게임 코드가 쥐고 있던 객체 참조는 그대로 유효하다.

### 2.7 일부 바꾸기: `Modify`

```csharp
data.Modify(GameIds.PlayerData.PlayerInventory, inv =>
{
    inv.gold -= 50;
    inv.items.Add(new Item { name = "검", level = 1 });
});

data.Modify<Inventory>("player.inventory", inv => inv.items[0].level++);
```

람다 안에서 무엇을 하든(여러 필드, 추가·삭제·정렬) 끝날 때 그 키를 Sync해 바뀐 곳을 기록한다. 람다 안에서 예외가 나면 그 키를 바꾸기 전 모습으로 되돌리고 예외를 다시 던진다. `T`는 클래스여야 하고, 단순 값 키에 `Modify`를 부르면 "단순 값입니다. Set으로 바꾸세요" 예외가 난다.

### 2.8 꺼낸 객체를 그냥 고치기

```csharp
var bag = data.Get(GameIds.PlayerData.PlayerInventory);
bag.gold += 10;            // 그냥 고친다
bag.items.RemoveAt(0);
```

비교 코드로 다뤄지는 데이터(`[Serializable]` 클래스)는 `Modify` 없이 꺼내서 그냥 고쳐도 된다. 다음 Sync 때 바뀐 곳이 기록된다. 다만 **어디서 고쳤느냐**에 따라 기록의 성격이 다르다(4장).

- 행동 안에서 고침 → 그 행동의 변경. Undo로 되돌아간다.
- 행동 밖에서 고침 → "행동 밖 변경"으로 기록된다. Undo로 되돌아가지 않는다.

### 2.9 코드로 정의하기: `Define`

DB 없이 코드에서 직접 등록할 수 있다(테스트·서버·시제품, 또는 코드만 아는 내부 값).

```csharp
// 1) 값으로 정의: 기본값의 형식이 이 키의 형식이 된다. 저장소가 관리하는 실제 객체를 돌려준다.
Inventory bag = data.Define("player.inventory", DataZone.Meta, new Inventory(), "인벤토리");
int hp = data.Define("battle.hp", DataZone.Session, 10, "체력");

// 2) 형식이 붙은 키로 정의
var goldKey = new PlayerDataKey<int>("player.gold");
data.Define(goldKey, DataZone.Meta, 100);

// 3) 형식을 따로 지정(기본값이 null이면 기본 생성자로 만든다)
data.DefineOfType("player.bag", DataZone.Meta, typeof(Inventory), null);
```

| 매개변수 | 뜻 |
|---|---|
| `key` | ID. DB와 같은 규칙을 따르는 것이 좋다. |
| `zone` | `DataZone.Session` 또는 `DataZone.Meta`. |
| `defaultValue` | 시작 값. `NewRun()`이 세션 구역을 이 값으로 되돌린다. |
| `label` | 모니터·기록에 보일 이름(생략하면 ID). |

- 같은 키를 다시 `Define`하면 기본값만 바뀐다. 다른 구역이나 다른 데이터 형식으로 다시 정의하면 예외다.
- 저장 파일을 먼저 불러온 뒤에 `Define`해도 된다. 아직 정의되지 않은 키의 저장 값은 보관했다가 정의하는 순간 적용한다.
- 담을 수 없는 필드(함수 필드 등)가 있으면 `Define`이 어느 필드인지 적은 예외를 던진다.

게임 시작과 함께 등록하려면 `GBibleRuntime.Started`에서 한다.

```csharp
using GBible;
using UnityEngine;

public static class BattleSetup
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void Hook() => GBibleRuntime.Started += session =>
    {
        session.Data.Define("battle.board", DataZone.Session, new Board(), "전장");
    };
}
```

### 2.10 그 밖의 저장소 멤버

| 멤버 | 하는 일 |
|---|---|
| `IsDefined(key)` | 정의된 키인지 |
| `IsData(key)` | 데이터 값(클래스·컬렉션) 키인지 |
| `TypeOf(key)` / `ZoneOf(key)` / `LabelOf(key)` | 형식·구역·표시 이름 |
| `Keys` | 모든 키 |
| `Changed` (이벤트 `Action<string>`) | 키의 값이 바뀌면(Undo 포함) 그 키를 쏜다. 한 행동에도 여러 번 올 수 있으므로(중간 Sync·실패한 행동의 되돌림) 화면 갱신은 이것을 직접 받지 않고 `GameView.Redraw(그리기, 키…)`를 쓴다: 값이나 언어가 바뀌면 그 프레임 끝에 한 번만 그린다([23-Text-Localization.md](23-Text-Localization.md) 4절) |
| `DataChanged` (이벤트) | 데이터 값이 바뀌면 키와 바뀐 경로 목록(`DataChange`)을 쏜다 |
| `Synced` (이벤트 `Action<ChangeSet>`) | Sync가 만든 ChangeSet을 쏜다. 행동 안의 변경은 행동이 끝날 때 차례대로 한꺼번에 쏜다(멀티플레이 전송용) |
| `Identity.IdOf(객체)` | 컬렉션 요소에 시스템이 붙인 내부 번호(기록의 `items[#12]`) |
| `Permanent()` | 이 안의 변경은 Undo 이력에 넣지 않는다([02-Actions-Undo.md](02-Actions-Undo.md)) |
| `DescribeChanges` | 바뀐 곳마다 사람이 읽는 기록을 만들지(기본 `true`). 끄면 Sync가 가벼워지지만 기록·`DataChanged`의 목록이 비고, 행동 밖 변경을 덮는 Undo 경고도 나오지 않는다 |

---

## 3. 어떤 필드가 기록되는가

비교 코드는 **Unity가 직렬화하는 필드와 같은 규칙**으로 필드를 고른다.

```csharp
using System;
using System.Collections.Generic;
using GBible;
using UnityEngine;

[Serializable]
public class Hero
{
    public string id;                                    // 기록됨
    public int level;                                    // 기록됨
    [SerializeField] int exp;                            // 기록됨(private이지만 [SerializeField])
    [SerializeReference] public StatusEffect effect;     // 기록됨
    [field: SerializeField] public int Mana { get; set; } // 기록·저장됨(자동 속성의 숨은 필드)

    int cache;                                           // 기록 안 됨(private, 어트리뷰트 없음)
    [NonSerialized] public float power;                  // 기록 안 됨
    public readonly int seed = 3;                        // 기록 안 됨(readonly)
    public static int count;                             // 기록 안 됨(static)
    public const int Max = 99;                           // 기록 안 됨(const)
    public int Speed { get; set; }                       // 기록 안 됨(속성)
}

[Serializable] public class StatusEffect { public int turns; }
```

| 선언 | 기록(Undo·Sync) |
|---|---|
| `public` 필드 | 된다 |
| `[SerializeField]` private 필드 | 된다 |
| `[SerializeReference]` 필드 | 된다 |
| `[field: SerializeField]` 자동 속성 | 된다 |
| 어트리뷰트 없는 private·protected 필드 | 안 된다 |
| `[NonSerialized]` · `readonly` · `static` · `const` | 안 된다 |
| 속성(property) | 안 된다 |
| 함수(델리게이트) 필드 | 데이터가 아니다. `[NonSerialized]`가 없으면 `Define`이 오류를 낸다 |

**`[Serializable]`이 꼭 필요하다.** 플레이어 데이터의 값이 되는 클래스, 그 안에 든 클래스, 구조체, 리스트 요소 클래스, 부모 클래스 모두에 붙인다.

- 부모 클래스에 `[Serializable]`이 없으면 비교 코드가 그 부모의 필드를 다루지 않는다(경고 없이 기록에서 빠진다).
- 추상 클래스·인터페이스 형식의 필드·요소에 실제로 들어 있는 객체가 `[Serializable]`이 아니면, 그 객체는 참조만 비교되고 안쪽 변경은 따라가지 못한다(콘솔에 "비교 코드가 없어 안쪽 변경을 따라가지 못합니다" 경고가 형식마다 한 번).

---

## 4. 지원하는 형식

### 4.1 비교 코드로 다루는 것(빠른 길)

| 종류 | 예 |
|---|---|
| 숫자 | `int` `long` `float` `double` `byte` `sbyte` `short` `ushort` `uint` `ulong` `decimal` |
| 그 밖의 단순 값 | `bool` `char` `string` enum `DateTime` `TimeSpan` `DateTimeOffset` `Guid` |
| 비어 있을 수 있는 값 | `int?` `float?` `MyEnum?` 같은 `Nullable<T>`(필드·리스트 요소) |
| 구조체 | `[Serializable]` 구조체(필드 규칙은 클래스와 같다) |
| Unity 구조체 | `Vector2` `Vector3` `Vector4` `Vector2Int` `Vector3Int` `Quaternion` `Color` `Color32` `Rect` `RectInt` `Bounds` |
| 클래스 | `[Serializable]` 클래스. 다른 클래스를 필드로 품어도 된다(깊이 제한 없음). `null`도 된다 |
| 리스트·배열 | `List<T>`, 1차원 배열 `T[]` (요소는 위의 값·구조체·클래스) |
| 딕셔너리 | `Dictionary<K, V>`. 키 `K`는 `string`·정수 형식·`char`·`bool`·enum. 값 `V`는 위의 값·구조체·클래스 |
| 다형성 | 추상 클래스·인터페이스·부모 클래스 형식의 필드나 리스트 요소에 자식 객체를 넣는다. 실제 객체가 `[Serializable]`이면 안쪽까지 기록된다 |
| 에셋 참조 | `Sprite`·`GameData` 같은 `UnityEngine.Object` 참조. 참조가 바뀌었는지만 비교한다 |

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable] public struct Cell { public int x, y; }

[Serializable] public abstract class Buff { public int turns; }
[Serializable] public class Poison : Buff { public int damage; }
[Serializable] public class Shield : Buff { public int amount; }

[Serializable]
public class Unit
{
    public string id;
    public int hp = 30;
    public Cell cell;                                          // 구조체
    public Vector3 lastSeen;                                   // Unity 구조체
    public List<Buff> buffs = new List<Buff>();                // 다형성 리스트
    public Dictionary<string, int> cooldowns = new Dictionary<string, int>();
    public int[] slots = new int[4];
    public Sprite portrait;                                    // 에셋 참조
}
```

한 플레이어 데이터의 **값 자체**(뿌리)는 `[Serializable]` 클래스여야 빠른 길로 다뤄진다. 값 형식이 `List<Unit>`나 배열·딕셔너리 그 자체이면 예전 방식이 된다. 컬렉션은 클래스 하나로 감싼다(`class Board { public List<Unit> units; }`).

### 4.2 예전 방식으로 다루는 것(동작은 하지만 느린 길)

아래가 하나라도 들어 있으면 **그 플레이어 데이터 전체**가 예전 방식이 된다.

- `[Serializable]`이 없는 클래스
- `HashSet<T>` `SortedSet<T>` `Queue<T>` `Stack<T>` `LinkedList<T>` 같은 그 밖의 컬렉션
- 중첩 컬렉션(`List<List<int>>`, 값이 리스트인 딕셔너리 등). 컴파일할 때 "중첩 컬렉션·이 형식은 비교 코드로 다루지 않습니다" 경고가 난다
- `float`·`double` 키 딕셔너리
- 뿌리 값이 컬렉션 그 자체

예전 방식 데이터를 정의하면 기록(모니터 타임라인·콘솔)에 "'이름'은(는) 예전 방식(리플렉션)으로 비교합니다: 이유" 경고가 한 번 남는다.

### 4.3 담을 수 없는 것

`Define`(DB 플레이어 데이터라면 게임 시작 시 등록)이 오류를 낸다.

- 함수(델리게이트)·포인터 필드
- `object` 형식 필드
- 다차원 배열(`int[,]`)
- 키가 숫자·글자·enum이 아닌 딕셔너리
- 객체가 자기 자신을 다시 가리키는 순환 참조("순환 참조" 오류). 자기 형식을 필드로 갖는 것 자체(`class Node { public Node next; }`)는 괜찮다

---

## 5. Sync는 언제 일어나는가

| 시점 | 무엇을 Sync하나 | 기록의 성격 |
|---|---|---|
| 행동 시작 | 모든 데이터 | 그 전까지의 변경을 **행동 밖 변경**으로 먼저 떼어 둔다 |
| 행동 처리 끝 / 행동 끝 | 모든 데이터 | 행동의 변경(Undo 한 단위) |
| 행동 중단(예외) | 모든 데이터 | 진행분을 기록한 뒤 전부 되돌린다 |
| `Modify` 끝 (데이터 값 `Set` 포함) | 그 키 | 행동 안이면 행동의 변경, 밖이면 행동 밖 변경. 행동 밖이면 `Modify` 직전에도 그 키를 Sync해 그 전의 직접 수정과 섞지 않는다 |
| `Game.Find<T>` · `Game.All<T>` | 찾는 키들 | 행동 안이면 행동의 변경, 밖이면 행동 밖 변경 |
| `Permanent()` 들어갈 때·나올 때 | 모든 데이터 | 들어가기 전 = 행동의 변경, 안 = Undo 이력 밖 |
| `Undo` · 스냅샷 되감기(`Restore`) | 앞: 모든 데이터 / 뒤: 되돌린 키 | 앞 = 행동 밖 변경, 뒤 = 되돌린 내용(기록·전파되지만 이력에는 안 남음) |
| 매 프레임 끝 | 모든 데이터 | 행동 밖 변경. 일시정지 중에도 한다(시계만 멈춘다). 행동 실행 중이면 건너뛴다 |

매 프레임 끝의 Sync는 `GBibleRuntime`이 만든 호스트(`LateUpdate`)가 한다. `GBibleRuntime` 없이 세션을 직접 만든 경우(테스트·서버)는 `session.EndFrame(deltaTime)`을 직접 불러야 한다. `session.Data.Sync()`를 직접 불러도 되지만 게임 코드가 부를 일은 거의 없다.

### 코드에 주는 의미

1. **행동 밖에서 고친 값은 다음 Sync에 한 번 기록된다.** 그 사이에 여러 번 바꿔도 마지막 모습만 남는다(`hp`를 30 → 25 → 20으로 바꿨으면 기록은 `30 → 20` 하나).
2. **행동 밖 변경은 Undo되지 않는다.** 되돌릴 수 있어야 하는 변경은 행동 안에서 한다.
3. **행동 하나는 한 묶음이다.** 행동 안에서 `Game.All`이 중간에 Sync해도 Undo 단위는 하나이고, `Synced` 이벤트는 행동이 끝날 때 모아 둔 순서대로 쏜다. 행동 중간 모습은 밖(네트워크)으로 나가지 않는다.
4. **조건 검사는 Sync 뒤에 한다.** 도전과제·트리거 조건은 Sync가 찾아낸 바뀐 키를 보는 것만 다시 평가한다. 행동 밖 변경도 그 프레임 끝에 반영된다.
5. **매 프레임 바뀌는 값을 넣으면 매 프레임 기록이 쌓인다.** 위치·속도 같은 값은 MonoBehaviour 쪽에 둔다.
6. **저장소에 넣지 않은 객체는 기록되지 않는다.** `new Unit()`을 만들어 필드를 고쳐도, 플레이어 데이터 안에 넣기 전까지는 아무 기록이 없다. 리스트에서 빠진 객체를 고쳐도 무시된다.
7. **같은 객체를 두 곳에 넣어도 된다.** `slots[0] = items[3]`처럼 한 객체를 여러 곳에서 가리키거나 서로 가리켜도 된다. 그 객체의 변경은 한 번만(먼저 만난 자리의 경로로) 기록되고, Undo·받는 쪽(`MirrorReceiver`)에서도 같은 인스턴스로 이어진다.

---

## 6. 예전 방식 데이터의 규칙

비교 코드가 없는 데이터는 "꺼낸 객체를 그냥 고친다"가 늘 통하지 않는다.

- `Modify`·`Set`으로 바꾸면 기록된다.
- 행동 안에서는 그 행동 중에 `Get`·`Game.Find`·`Game.All`로 꺼낸 키의 변경이 기록된다.
- 그 밖의 직접 수정(행동 밖에서 들고 있던 객체를 고침 등)은 개발 빌드에서 다음 Sync 때 "'이름'이(가) Modify를 거치지 않고 바뀌었습니다" 경고와 함께 받아들이고, Undo와 원인 기록에서 빠진다. 이 검사(`SessionOptions.DetectUntrackedChanges`)는 값 전체를 비교하므로 `GBibleRuntime`은 개발 빌드에서만 켠다.
- 수정할 때마다 값 전체를 복제·비교하므로 요소가 많으면 느리다. 요소가 2만 개를 넘으면 "값이 큽니다" 경고가 한 번 난다.

가능하면 `[Serializable]`을 붙여 빠른 길로 옮긴다.

---

## 7. 상황별 예제

### 7.1 RPG: 지갑 골드와 인벤토리 (DB 정의)

DB에서 두 플레이어 데이터를 만든다.

| ID | 구역 | 값 형식 | 기본값 |
|---|---|---|---|
| `player.gold` | Meta | 정수 (int) | `100` |
| `player.inventory` | Meta | `Inventory` | (클래스 초기값) |

`Item`·`Inventory`는 2.1의 클래스다.

```csharp
using System;
using GBible;
using GBible.Ids;
using UnityEngine;

// 구매는 플레이어 입력이므로 행동으로 만든다(Undo 한 단위).
[Serializable]
public class BuyItem : SimpleAction
{
    public string itemName;
    public int price;

    protected override void Run()
    {
        var data = Game.Data();
        if (data.PlayerGold < price) throw new InvalidOperationException("골드가 부족합니다.");
        data.PlayerGold -= price;
        data.PlayerInventory.items.Add(new Item { id = Game.NewId("item"), name = itemName, price = price });
    }
}

// 화면: 읽기만 한다.
public class WalletView : MonoBehaviour
{
    void Update()
    {
        var game = GBibleRuntime.Game;
        if (game == null) return;
        int gold = game.Data().PlayerGold;
        int count = game.Data().PlayerInventory.items.Count;
        // gold, count를 UI에 그린다
    }

    public void OnClickBuy() => GBibleRuntime.Game.Do(new BuyItem { itemName = "검", price = 50 });
}
```

골드가 모자라면 행동 안에서 예외가 나고, 행동이 바꾼 것이 모두 취소된다(`Do`를 부른 쪽으로 예외가 다시 던져진다).

### 7.2 전술 게임: 전장과 유닛 HP (세션 구역)

```csharp
using System;
using System.Collections.Generic;
using GBible;

[Serializable]
public class TacticsUnit
{
    public string id;
    public string team;
    public int x, y;
    public int hp = 30;
}

[Serializable]
public class Board
{
    public int turn;
    public List<TacticsUnit> units = new List<TacticsUnit>();
}

[Serializable]
public class Strike : GameAction<TacticsUnit>
{
    public int damage;
    protected override void Run(TacticsUnit target)
    {
        target.hp -= damage;                                   // 그냥 고친다
        if (target.hp <= 0) Game.All<Board>()[0].units.Remove(target);
    }
}
```

전장을 DB의 `battle.board`(Session, `Board`)로 만들었거나 코드에서 `session.Data.Define("battle.board", DataZone.Session, new Board())`로 정의했다면, 행동은 `Game.Do(new Strike { target = "orc1", damage = 12 })`로 실행한다. Undo하면 HP와 리스트에서 빠진 유닛이 **같은 객체로** 제자리에 돌아온다.

### 7.3 카드 게임: 덱·손패·카드별 사용 횟수

```csharp
using System;
using System.Collections.Generic;
using GBible;

[Serializable]
public class CardState
{
    public List<string> deck = new List<string>();
    public List<string> hand = new List<string>();
    public List<string> discard = new List<string>();
    public Dictionary<string, int> playedCount = new Dictionary<string, int>();
    public int mana = 3;
}

[Serializable]
public class PlayCard : SimpleAction
{
    public string card;
    public int cost;

    protected override void Run()
    {
        var state = Game.All<CardState>()[0];
        if (!state.hand.Remove(card)) throw new InvalidOperationException("손에 없는 카드입니다.");
        state.mana -= cost;
        state.discard.Add(card);
        state.playedCount.TryGetValue(card, out var n);
        state.playedCount[card] = n + 1;
    }
}
```

리스트 이동·딕셔너리 값 변경이 모두 한 행동의 변경으로 묶여 Undo 한 번에 돌아간다.

### 7.4 상태 효과 리스트(다형성)

```csharp
using System;
using System.Collections.Generic;
using GBible;

[Serializable] public abstract class Buff { public int turns; }
[Serializable] public class Poison : Buff { public int damage = 3; }
[Serializable] public class Shield : Buff { public int amount = 10; }

[Serializable]
public class Fighter
{
    public string id;
    public int hp = 50;
    public List<Buff> buffs = new List<Buff>();
}

[Serializable]
public class TickBuffs : GameAction<Fighter>
{
    protected override void Run(Fighter f)
    {
        foreach (var buff in f.buffs)
        {
            if (buff is Poison p) f.hp -= p.damage;
            buff.turns--;
        }
        f.buffs.RemoveAll(b => b.turns <= 0);
    }
}
```

자식 클래스(`Poison`·`Shield`)에도 `[Serializable]`이 있어야 안쪽 필드(`turns`·`damage`)까지 기록된다. 저장 파일에는 실제 형식 이름이 함께 남아 불러올 때 같은 자식 클래스로 돌아온다.

### 7.5 코드만으로 정의하는 시제품·테스트

```csharp
using GBible;

var session = new GameSession(new SessionOptions { Seed = 7 });
var board = session.Data.Define("battle.board", DataZone.Session, new Board
{
    units =
    {
        new TacticsUnit { id = "hero", team = "ally" },
        new TacticsUnit { id = "orc1", team = "enemy", x = 3 },
    },
}, "전장");

session.Game.Do(new Strike { target = "orc1", damage = 12 });
// board.units[1].hp == 18
session.Undo();
// board.units[1].hp == 30
```

### 7.6 행동 밖에서 고친 값

```csharp
var hero = session.Game.Find<TacticsUnit>("hero");
hero.hp = 12;               // 행동 밖: 다음 Sync(프레임 끝 등)에 행동 밖 변경으로 기록
session.EndFrame(0.016);    // GBibleRuntime 없이 직접 만든 세션이면 프레임 끝을 직접 부른다

session.Game.Do(new Strike { target = "hero", damage = 2 });   // hp 10
session.Undo();                                                 // hp 12 (행동 밖 변경은 그대로)
```

---

## 8. 주의할 점 · 자주 하는 실수

| 증상 | 원인과 해결 |
|---|---|
| "정의되지 않은 플레이어 데이터" 예외 | DB에서 만들지 않았거나 `Define` 전에 읽었다. 코드에는 `GameIds`를 쓴다. |
| Undo했는데 값이 그대로 | 행동 밖에서 바꾼 값이다. 플레이어 입력은 행동으로 만든다. `Permanent()` 안의 변경이거나, 이력 한도(`HistoryLimit`)를 넘은 오래된 행동이거나, 되돌리기 방식이 `None`일 수도 있다. |
| 어떤 필드만 Undo·기록에서 빠짐 | private 필드에 `[SerializeField]`가 없다, 속성이다, `readonly`다, 또는 부모 클래스에 `[Serializable]`이 없다. |
| 안쪽 값이 기록되지 않음 | 리스트·필드에 든 자식 객체 클래스에 `[Serializable]`이 없다. |
| "예전 방식(리플렉션)으로 비교합니다" 경고 | 4.2의 형식이 들어 있다. 가능하면 `List`·`Dictionary`·`[Serializable]`로 바꾼다. |
| "Modify를 거치지 않고 바뀌었습니다" 경고 | 예전 방식 데이터를 행동·`Modify` 밖에서 고쳤다(6장). |
| "담을 수 없는 필드" 오류 | 함수·`object`·다차원 배열 필드가 있다. 데이터가 아니면 `[NonSerialized]`를 붙인다. |
| 모니터 타임라인에 기록이 매 프레임 쌓임 | 매 프레임 바뀌는 값을 플레이어 데이터에 넣었다. MonoBehaviour 쪽으로 옮긴다. |
| 스프라이트 같은 에셋 참조가 불러온 뒤 비어 있음 | 저장 파일에는 `GameData`(DB 항목) 참조만 ID로 담긴다(되감기 스냅샷은 메모리 안이라 참조를 그대로 둔다). 그 밖의 에셋 참조는 저장 파일에 담기지 않고, 저장할 때 "저장할 수 없는 참조입니다" 경고가 난다. 에셋은 DB ID나 인스턴스 쪽에 두고 플레이어 데이터에는 ID를 둔다. |
| 오래 쥔 객체가 Undo·불러오기 뒤 엉뚱함 | Undo는 빠졌던 객체를 원래 인스턴스로 되돌리지만, 불러오기·멀티플레이 받는 쪽에서는 다른 인스턴스일 수 있다. 오래 들고 있을 것은 id로 들고 `Game.Find`로 다시 찾는다. |
