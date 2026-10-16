# 01. 플레이어 데이터

**한 줄 요약**: 게임 중에 바뀌고, 되돌리거나(Undo) 저장하거나 조건으로 검사할 값은 모두 "플레이어 데이터"에 둔다. 평범한 `[Serializable]` C# 클래스를 그대로 쓰고, 시스템이 바뀐 곳을 찾아 기록한다.

**언제 쓰는가**

- 골드·경험치·턴 수처럼 게임 규칙이 바꾸는 값
- 인벤토리·덱·전장의 유닛 목록처럼 여러 값이 모인 객체
- 도전과제·로그·트리거 조건이 검사할 값
- 저장 파일에 남길 값(프로필, 진행 중인 판)

넣지 않는 것: 매 프레임 바뀌는 위치·속도·애니메이션 상태, 화면 상태, 다른 값으로 계산할 수 있는 값(합산 공격력 등), 게임 중에 바뀌지 않는 정의 데이터(DB 항목). 판단 기준은 "되돌리거나, 저장하거나, 기록하거나, 조건으로 검사할 일이 있는가"이다.

**꼭 지킬 것**(자세히는 괄호의 절)

- 어디에: 시작 설정이 만든 키 두 개에 둔다 — `run`(`RunState`, 세션 구역 = 이번 판)·`profile`(`Profile`, 메타 구역 = 판이 끝나도 남음). 값은 이 클래스(`Assets/MomoiProject/Code/Rules/PlayerData.cs`)에 필드로 더한다. 새 키가 필요하면 AI가 DB 반영 스크립트로 DB › 플레이어 데이터 항목을 만든다(1절).
- 바꾸는 곳: 플레이어 데이터는 **행동 안에서만** 바꾼다. 행동 밖에서 고친 값은 기록되지만 Undo되지 않는다(2.8·5절).
- 클래스: 값이 되는 클래스·안쪽 클래스·부모·리스트 요소 클래스 모두 `[Serializable]`(3절). 컬렉션은 `List<T>`·`T[]`·`Dictionary<K, V>`(4절).
- 원본(DB 항목·인스턴스)은 넣지 않고 그 ID(`[DataRef]` 글자)와 **바뀌는 값**만 넣는다(04장). 스프라이트 같은 에셋 참조는 저장 파일에 담기지 않는다(8절).
- 찾을 객체는 `public string id;`를 두고, id는 데이터 전체에서 하나(`Game.NewId`, 02장 2.2). 다른 키의 객체는 id로 가리키고, 같은 컬렉션 인스턴스를 두 곳에서 가리키지 않는다(5절 7).

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
| 예전 방식 | 비교 코드로 다룰 수 없는 데이터에 쓰는 느린 비교(리플렉션으로 값 전체를 복제·비교). 동작은 하지만 기록 규칙이 다르다(6절). |

---

## 1. 에디터에서 정의하기

플레이어 데이터 키는 DB › 플레이어 데이터(`PlayerDataEntry`, 사이드바 **요소** 그룹) 항목이다. 게임이 시작될 때 저절로 등록되고, 코드에서는 생성된 상수(2.3)로 쓴다.

- **누가 만드나**: 시작 설정(`MomoiProject › 개발 › 시작 설정`)이 `run`(`RunState`, Session)·`profile`(`Profile`, Meta)을 만든다. 그 밖의 키는 AI가 DB 반영 스크립트로 만든다 — 이 탭의 **+ 새로 만들기**는 개발자에게 잠겨 있다(21장 3-1).

```csharp
// Assets/MomoiProject/Code/Editor/Seeds/DataSeeds.cs — [DbSeed] 함수 안(AI 관례 15절)
if (EditorDatabase.Instance.FindById("battle.board") == null)
{
    var entry = (PlayerDataEntry)EditorDatabase.Staging.Add(typeof(PlayerDataEntry), "battle.board");
    entry.zone = DataZone.Session;
    entry.valueType = typeof(Board).FullName;      // 값 형식(형식 전체 이름)
}
```

- **ID**: 영문 소문자로 시작하고 단어 사이를 대문자로 잇는다(띄어쓰기·밑줄 없음). 분류는 점(.)으로 나눈다. 예: `player.gold`, `battle.board`.
- **칸 세 개**:
  - **구역**(`zone`): `Session`(이번 판에만, 판 저장에 들어감) 또는 `Meta`(판이 바뀌어도 남음, 프로필 저장에 들어감).
  - **값 형식**(`valueType`): 기본 형식(숫자 double · 정수 int · 큰 정수 long · 소수 float · 참/거짓 bool · 글자 string · 날짜·시각 DateTime · 시간 길이 TimeSpan) · 내 코드 범위 안의 enum · 내 코드 범위 안의 공개 `[Serializable]` 클래스 중 추상 클래스가 아니고 담을 수 없는 필드가 없는 것.
  - **기본값**(`defaultValue`): 단순 값에만 쓴다(글자로 적는다). 클래스는 기본값 칸을 쓰지 않고 클래스의 초기값(필드 초기화·생성자)으로 시작한다.
- **멀티 칸**(명세서 0절이 '있음'일 때): '플레이어마다'(`perPlayer` — 판 구역만, 실제 키 `ID:번호`, 코드·Block은 ID 그대로 쓰면 행동을 한 플레이어의 몫) · '그 사람에게만 보임'(`ownerOnly`) · '매칭 계정에 남김'(`onAccount` — 매칭큐의 계정에서 읽고 매치가 끝나면 쓴다). 40장 1-2 · 1-6.
- **`GameIds` 상수가 생기는 때**: 새 항목이 적용되면 `GameIds.cs`가 다시 만들어지고, Unity가 다시 컴파일한 **뒤에** `GameIds.PlayerData.X`·`Game.Data().X`를 코드에서 쓸 수 있다(20장 7절). 같은 응답 안의 코드는 그 전까지 ID 글자로 읽는다.

DB 검증은 값 형식을 찾을 수 없거나, 담을 수 없는 필드가 있거나, 기본값을 읽을 수 없으면 오류를 띄운다. 클래스가 비교 코드로 다뤄지지 않으면 "비교 코드로 다룰 수 없어 예전 방식(리플렉션, 더 느림)으로 비교합니다" 경고를 띄운다.

플레이 중에는 **MomoiProject › 모니터**의 **데이터** 탭에서 **판 데이터**(세션 구역)와 **프로필 데이터**(메타 구역)를 탐색기로 보고 고칠 수 있다(모니터 자세히는 [22-Monitor-Debug.md](22-Monitor-Debug.md)).

---

## 2. 코드로 쓰는 법 — 문법 요소

### 2.1 데이터 클래스: `[Serializable]`

```csharp
using System;
using System.Collections.Generic;
using GBible.Data;

[Serializable]
public class ItemDef                                    // 원본 클래스: DB › 인스턴스에 하나씩(04장)
{
    public int price;
    public List<string> tags = new List<string>();
}

[Serializable]
public class Item                                       // 판에 든 아이템 하나
{
    public string id;                                   // 고유 id(Game.NewId)
    [DataRef(typeof(ItemDef))] public string item;      // 원본 ID — 가격·태그 같은 고정 값은 원본에서 읽는다
    public int level = 1;                               // 판에서 바뀌는 값
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

// 행동의 Run 안(바꾸는 것은 행동 안에서)
Game.Data().PlayerGold += 100;                        // 단순 값: 읽고 쓰기
Game.Data().PlayerInventory.items.Add(new Item { id = Game.NewId("item") });   // 데이터 값: 꺼내서 안쪽을 고친다

// 화면 코드: 읽기만
int gold = GBibleRuntime.Game.Data().PlayerGold;
```

`GameIds.cs`와 함께 생성되는 확장 메서드다(네임스페이스 `GBible.Ids`). 단순 값은 속성 `get`/`set`(내부적으로 `Get`/`Set`), 데이터 값은 읽기 전용 속성(실제 객체를 돌려줌)이다. 이름은 ID의 파스칼 표기다(`run` → `Game.Data().Run`).

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

- 단순 숫자는 저장소 안에서 `double`로 보관하고, `Get<T>`가 선언한 형식(`int` 등)으로 바꿔 준다. 정수 형식으로 읽을 때는 0에서 먼 쪽으로 반올림한다(2.5 → 3, −2.5 → −3 — 규칙 전체가 쓰는 `GameNumbers.Round`와 같다).
- 데이터 값은 **저장소가 관리하는 실제 객체**를 돌려준다. 복사본이 아니다.
- 정의되지 않은 키를 읽으면 "정의되지 않은 플레이어 데이터" 예외가 난다.

### 2.6 통째로 바꾸기: `Set`

2.6~2.8의 쓰기도 모두 행동 안에서 한다(행동 밖이면 기록만 되고 Undo되지 않는다 — 2.8).

```csharp
data.Set(GameIds.PlayerData.PlayerGold, 150);       // Set<T>(PlayerDataKey<T>, T)
data.Set("player.gold", 150);                        // Set(string, object)
data.Set("player.inventory", new Inventory());      // 데이터 값: 내용을 복사해 넣는다
```

- 단순 값은 선언한 형식과 맞지 않는 값(숫자 키에 글자 등)을 넣으면 `ArgumentException`이다.
- 데이터 값은 넘긴 객체를 그대로 넣지 않고 **내용을 복사해 지금 객체에 덮는다**. 게임 코드가 쥐고 있던 객체 참조는 그대로 유효하다. 넘긴 객체는 데이터 안으로 들어가지 않으므로 `Set` 뒤에 그 객체를 고쳐도 데이터는 그대로다. 넘긴 값 안에 지금 데이터의 리스트 요소가 들어 있으면 그 인스턴스를 요소 번호로 이어 다시 쓴다.

### 2.7 일부 바꾸기: `Modify`

```csharp
data.Modify(GameIds.PlayerData.PlayerInventory, inv =>
{
    inv.gold -= 50;
    inv.items.Add(new Item { id = Game.NewId("item"), item = "ironSword" });
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

비교 코드로 다뤄지는 데이터(`[Serializable]` 클래스)는 `Modify` 없이 꺼내서 그냥 고쳐도 된다. 다음 Sync 때 바뀐 곳이 기록된다. 다만 **어디서 고쳤느냐**에 따라 기록의 성격이 다르다(5절).

- 행동 안에서 고침 → 그 행동의 변경. Undo로 되돌아간다.
- 행동 밖에서 고침 → "행동 밖 변경"으로 기록된다. Undo로 되돌아가지 않는다.

### 2.9 코드로 정의하기: `Define`

DB 없이 코드에서 직접 등록한다. 쓰는 곳은 규칙 테스트·서버·시제품, 또는 코드만 아는 내부 값이다. 프로젝트가 쓰는 키는 DB 항목으로 만든다(1절 — `GameIds` 상수가 생기고 개발자가 DB에서 읽는다).

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
| `defaultValue` | 시작 값. 정의할 때 사본을 따로 떠 두고, `NewRun()`이 세션 구역을 그 사본으로 되돌린다(판에서 고친 것 — 카드 강화 등 — 이 다음 새 판의 처음 값에 남지 않는다). |
| `label` | 모니터·기록에 보일 이름(생략하면 ID). |

- 같은 키를 다시 `Define`하면 기본값만 바뀐다. 다른 구역이나 다른 데이터 형식으로 다시 정의하면 예외다.
- 저장 파일을 먼저 불러온 뒤에 `Define`해도 된다. 아직 정의되지 않은 키의 저장 값은 보관했다가 정의하는 순간 적용한다. 단순 값도 정의한 형식대로 읽는다(날짜 글자 → `DateTime`).
- 담을 수 없는 필드(함수 필드 등)가 있으면 `Define`이 어느 필드인지 적은 예외를 던진다.

Unity 게임에서 시작과 함께 코드로 등록해야 하면 `GBibleRuntime.SessionCreated += session => session.Data.Define(…)`(모든 세션 — 20장 4절, 시작 설정을 했으면 `Bootstrap.RegisterRules` 안에).

### 2.10 그 밖의 저장소 멤버

| 멤버 | 하는 일 |
|---|---|
| `IsDefined(key)` | 정의된 키인지 |
| `KeysOfAllPlayers(key)` · `PlayerDataStore.KeyFor(key, 번호)` · `IsPerPlayer(key)` | 플레이어마다 데이터의 모든 몫 · 그 플레이어 몫의 키 이름 · 플레이어마다인지(40장 1-2) |
| `IsData(key)` | 데이터 값(클래스·컬렉션) 키인지 |
| `TypeOf(key)` / `ZoneOf(key)` / `LabelOf(key)` | 형식·구역·표시 이름 |
| `Keys` | 모든 키 |
| `Changed` (이벤트 `Action<string>`) | 키의 값이 바뀌면(Undo 포함) 그 키를 쏜다. 한 행동에도 여러 번 올 수 있으므로(중간 Sync·실패한 행동의 되돌림) 화면 갱신은 이것을 직접 받지 않고 `GameView.Redraw(그리기, 키…)`를 쓴다: 값이나 언어가 바뀌면 그 프레임 끝에 한 번만 그린다([23-Text-Localization.md](23-Text-Localization.md) 4절) |
| `DataChanged` (이벤트) | 데이터 값이 바뀌면 키와 바뀐 경로 목록(`DataChange`)을 쏜다 |
| `Synced` (이벤트 `Action<ChangeSet>`) | Sync가 만든 ChangeSet을 쏜다. 행동 안의 변경은 행동이 끝날 때 차례대로 한꺼번에 쏜다(멀티플레이 전송용) |
| `Identity.IdOf(객체)` | 컬렉션 요소에 시스템이 붙인 내부 번호(기록의 `items[#12]`) |
| `Permanent()` | 이 안의 변경은 Undo 이력에 넣지 않는다. 행동 안에서 열어도 그 행동은 계속 진행 중이다([02-Actions-Undo.md](02-Actions-Undo.md) 2.9) |
| `Mark()` / `RollbackTo(point)` | 행동 안의 저장점을 만든다 / 그 뒤의 변경만 되돌린다(행동은 계속). `Mark()`는 Sync를 한 번 하므로 자주 부르지 않는다([02-Actions-Undo.md](02-Actions-Undo.md) 2.7) |
| `ResetKey(key)` / `ResetZone(zone)` | 키 하나 / 구역 전체를 처음 값(정의할 때 떠 둔 사본)으로 되돌린다. 행동 안이면 그 행동의 변경이다. 새 판(`session.NewRun`)이 `ResetZone(Session)`을 쓴다. 처음 값 자체는 데이터 안으로 들어가지 않는다 |
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

- 부모 클래스에 `[Serializable]`이 없으면 비교 코드가 그 부모의 필드를 다루지 않는다(기록·Undo·멀티에서 빠진다 — 컴파일할 때 콘솔 경고). 그 위의 `[Serializable]` 조상 클래스의 필드는 그대로 다룬다. 행동·이벤트면 보낼 때 그 필드를 밝힌 오류가 난다(받는 쪽에 기본값으로 가지 않게).
- 추상 클래스·인터페이스 형식의 필드·요소에 실제로 들어 있는 객체가 `[Serializable]`이 아니면, 그 객체는 참조만 비교되고 안쪽 변경은 따라가지 못한다(콘솔에 "비교 코드가 없어 안쪽 변경을 따라가지 못합니다" 경고가 형식마다 한 번). Mirror의 경고는 플레이할 때마다 다시 나온다.

---

## 4. 지원하는 형식

### 4.1 비교 코드로 다루는 것(빠른 길)

| 종류 | 예 |
|---|---|
| 숫자 | `int` `long` `float` `double` `byte` `sbyte` `short` `ushort` `uint` `ulong` `decimal` |
| 그 밖의 단순 값 | `bool` `char` `string` enum `DateTime` `TimeSpan` `DateTimeOffset` `Guid` |
| 비어 있을 수 있는 값 | `int?` `float?` `MyEnum?` 같은 `Nullable<T>`(필드·리스트 요소) |
| 구조체 | `[Serializable]` 구조체(필드 규칙은 클래스와 같다). 안쪽에 객체·컬렉션 필드가 없고 안쪽 구조체 필드도 이 표의 것인 것만 — 아니면 4.2 |
| Unity 구조체 | `Vector2` `Vector3` `Vector4` `Vector2Int` `Vector3Int` `Quaternion` `Color` `Color32` `Rect` `RectInt` `Bounds` |
| 클래스 | `[Serializable]` 클래스. 다른 클래스를 필드로 품어도 된다(256단계까지 — 그보다 깊게 겹친 값은 복제·저장이 멈추거나 경고한다). `null`도 된다. 같은 객체를 두 곳에서 가리키거나 서로 가리켜도 된다(5절 7) |
| 리스트·배열 | `List<T>`, 1차원 배열 `T[]` (요소는 위의 값·구조체·클래스) |
| 딕셔너리 | `Dictionary<K, V>`. 키 `K`는 `string`·정수 형식·`char`·`bool`·enum. 값 `V`는 위의 값·구조체·클래스 |
| 다형성 | 추상 클래스·인터페이스·부모 클래스 형식의 필드나 리스트 요소에 자식 객체를 넣는다. 실제 객체가 `[Serializable]`이면 안쪽까지 기록된다(컬렉션은 인터페이스 형식으로 선언하지 않는다 — 4.3) |
| 에셋 참조 | `Sprite`·`GameData` 같은 `UnityEngine.Object` 참조. 참조가 바뀌었는지만 비교한다 |

```csharp
using System;
using System.Collections.Generic;
using GBible;
using UnityEngine;

// 구조체 예: GBible.Cell(격자 칸, 36장)도 이렇게 들어간다. 직접 만든 [Serializable] struct도 된다.

[Serializable] public abstract class Buff { public int turns; }
[Serializable] public class Poison : Buff { public int damage; }
[Serializable] public class Shield : Buff { public int amount; }

[Serializable]
public class Unit
{
    public string id;
    public int hp = 30;
    public Cell cell;                                          // 구조체(GBible.Cell)
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
- 컬렉션 클래스를 상속한 클래스(`class Deck : List<Card>`). 요소는 상속한 `List`의 안쪽 칸이라 비교 코드가 보지 못한다. 컴파일할 때 "컬렉션 클래스 …을(를) 상속해 생성 코드가 요소의 변경을 따라가지 못합니다 … 상속 대신 List<T>·Dictionary<K, V> 필드를 두세요" 경고가 나고, 행동·이벤트에 담으면 보낼 때 오류다. 상속 대신 `List<Card> cards` 필드를 둔다
- `float`·`double`·`Guid`·`DateTime`·`DateTimeOffset`·`TimeSpan` 키 딕셔너리(저장·불러오기는 된다)
- 안쪽에 객체·컬렉션 필드(클래스·List·배열·Dictionary)가 있는 `[Serializable]` 구조체. 구조체는 값으로 복사되므로 안쪽 변경을 따라가지 못한다. 컴파일할 때 "구조체 안의 객체·컬렉션 필드(…)는 안쪽 변경을 따라가지 못합니다. … 구조체 대신 [Serializable] 클래스로 바꾸세요." 경고가 난다. 클래스로 바꾼다(예전 방식은 느리고 `Modify`가 필요하다, 6절)
- 쓰고 읽는 방법이 없는 구조체(`KeyValuePair<K, V>`·`(int, int)` 같은 튜플·`[Serializable]`이 없는 구조체)를 담은 칸. `[Serializable]` 구조체의 필드·요소로 들어 있어도 같다(그 구조체도 예전 방식)
- 뿌리 값이 컬렉션 그 자체

예전 방식 데이터를 정의하면 기록(모니터 타임라인·콘솔)에 "'이름'은(는) 예전 방식(리플렉션)으로 비교합니다: 이유" 경고가 한 번 남는다.

### 4.3 담을 수 없는 것

`Define`(DB 플레이어 데이터라면 게임 시작 시 등록)이 오류를 낸다.

- 함수(델리게이트)·포인터 필드
- `object` 형식 필드
- 다차원 배열(`int[,]`)
- 인터페이스·추상 컬렉션 형식의 칸(`IList<T>`·`IDictionary<K, V>`·`IReadOnlyList<T>` …): "인터페이스·추상 컬렉션 형식은 담을 수 없습니다 — List<T>·Dictionary<K, V>로 선언하세요". 불러올 때 만들 실제 형식을 정할 수 없고 비교 코드도 안쪽 변경을 따라가지 못한다
- 키가 숫자·글자·참거짓·enum·`Guid`·날짜·시간이 아닌 딕셔너리(클래스 키 등)

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
   - 새 객체가 이미 있던 객체를 가리켜도 된다. 같은 행동에서 그 객체를 고친 것도 기록된다(`u.hp -= 5; log.Add(new Hit { target = u });` → `hp` 변경과 새 `Hit` 모두).
7. **같은 객체를 두 곳에 넣어도 된다.** `slots[0] = items[3]`처럼 한 객체를 여러 곳에서 가리키거나 서로 가리켜도(`parent`·`child`) 된다. 복제·비교·Undo(제자리 복원)·저장·불러오기가 모두 같은 인스턴스를 지킨다: 그 객체의 변경은 한 번만(먼저 만난 자리의 경로로) 기록되고, Undo·불러오기·받는 쪽(`MirrorReceiver`)에서도 두 곳이 같은 인스턴스를 가리킨다(저장 파일에는 `"$n"`·`"$ref"`, [06-SaveLoad.md](06-SaveLoad.md) 2.5).
   - 이 약속은 **플레이어 데이터 키 하나 안에서만**이다. 다른 키의 객체는 id로 가리키고 `Game.Find`로 찾는다. 두 키가 **동시에** 같은 객체를 가리키면 Undo가 두 번 적용되고, 키마다 따로 저장되어 불러오면 둘로 갈라진다. 한 키에서 빼고 다른 키에 넣는 옮기기는 된다(아래 '키 사이 옮기기').
   - **컬렉션(List·배열·Dictionary)은 안 된다.** 같은 컬렉션 인스턴스를 두 곳에서 가리키면 변경이 두 번 기록되어 Undo·멀티에서 데이터가 깨질 수 있다. 콘솔에 "X.f: 다른 곳과 같은 컬렉션 인스턴스를 가리킵니다. … 컬렉션은 한 곳에서만 가리키게 하세요(필요하면 new List<T>(원본)처럼 복사)." 경고가 한 번 난다. 컬렉션은 한 곳에서만 가리키고, 나눠 줄 때는 `new List<T>(원본)`으로 복사한다.
8. **규칙이 Dictionary를 훑는 순서에 기대지 않는다.** 내용이 같아도 Undo·불러오기·받는 쪽(멀티)에서 훑는 순서가 달라질 수 있다. 차례가 필요하면 `List`에 둔다(또는 키를 정렬해 훑는다).

### 키 사이 옮기기

탈출해서 창고로(판 `run` → 프로필 `profile`), 창고에서 꺼내 판으로처럼 객체를 다른 플레이어 데이터 키로 보낼 때는 **같은 객체를 옮긴다**: 한 행동 안에서 한쪽 목록에서 빼고 다른 쪽 목록에 넣는다. id는 그대로 둔다.

```csharp
[Serializable, BlockInfo("창고에 넣기")]
public class StashItem : GameAction
{
    public Item item;                                  // 판 가방의 그 객체
    protected override void Run()
    {
        var run = Game.Data().Run;
        var profile = Game.Data().Profile;
        if (!run.bag.Remove(item)) throw new InvalidOperationException("가방에 없는 아이템입니다.");
        profile.stash.Add(item);                       // 같은 객체 그대로(사본·새 id 없음)
    }
}
```

- 어느 순간에도 한 키만 그 객체를 가리키므로 7의 금지(두 키가 동시에)에 걸리지 않는다. Undo하면 제자리로 돌아오고, 옮긴 뒤 새 키에서 고친 값도 Undo된다. 저장에는 새 키 쪽에만 남는다(판 → 프로필이면 `profile.json`). 엔진 테스트 `Tests/Editor/KeyMoveTests.cs`가 이것을 확인한다.
- 빼기와 넣기는 **같은 행동** 안에서 한다. 빼기를 잊으면 두 키가 같은 객체를 가리킨다(7).
- **사본은 옮기기가 아니다.** 같은 원본으로 여러 개를 만들 때(보상으로 인스턴스 주기 — `session.CopyInstance(인스턴스 ID)`, 04장 2.8)나 판 객체를 하나 더 만드는 기믹(복제 — `InstanceData.Copy(객체)`, `GBible.Data`)만 깊은 사본을 쓰고, 사본에는 새 id(`Game.NewId`)를 준다. 옮길 때 사본 + 원본 지우기를 쓰지 않는다(id가 바뀌어 그 객체를 id로 가리키던 것이 끊긴다).

---

## 6. 예전 방식 데이터의 규칙

비교 코드가 없는 데이터는 "꺼낸 객체를 그냥 고친다"가 늘 통하지 않는다.

- `Modify`·`Set`으로 바꾸면 기록된다.
- 행동 안에서는 그 행동 중에 `Get`·`Game.Find`·`Game.All`로 꺼낸 키의 변경이 기록된다. 행동 안의 저장점(`Mark`)·`Permanent` 경계에서 Sync해도 그 행동의 변경으로 남는다.
- 리스트·배열은 요소 번호로 짝지어 더하기·빼기·순서 바꾸기를 기록한다. 빈 칸(`null`)이 섞인 같은 길이 배열에서 요소를 다른 칸으로 옮긴 것(장비 칸 `[A, null]` → `[null, A]`)도 바뀐 것으로 기록되어 Undo된다.
- 그 밖의 직접 수정(행동 밖에서 들고 있던 객체를 고침 등)은 개발 빌드에서 다음 Sync 때 "'이름'이(가) Modify를 거치지 않고 바뀌었습니다" 경고와 함께 받아들이고, Undo와 원인 기록에서 빠진다. 이 검사(`SessionOptions.DetectUntrackedChanges`)는 값 전체를 비교하므로 `GBibleRuntime`은 개발 빌드에서만 켠다.
- 수정할 때마다 값 전체를 복제·비교하므로 요소가 많으면 느리다. 요소가 2만 개를 넘으면 "값이 큽니다" 경고가 한 번 난다.

가능하면 `[Serializable]`을 붙여 빠른 길로 옮긴다.

---

## 7. 상황별 예제

### 7.1 RPG: 지갑 골드와 인벤토리 (DB 정의)

DB 반영 스크립트로 두 플레이어 데이터(1절)와 `ItemDef` 인스턴스 `ironSword`(04장)를 만든다.

| ID | 구역 | 값 형식 | 기본값 |
|---|---|---|---|
| `player.gold` | Meta | 정수 (int) | `100` |
| `player.inventory` | Meta | `Inventory` | (클래스 초기값) |

`ItemDef`·`Item`·`Inventory`는 2.1의 클래스다.

```csharp
using System;
using GBible;
using GBible.Data;
using GBible.Ids;

// 구매는 플레이어 입력이므로 행동으로 만든다(Undo 한 단위).
[Serializable, BlockInfo("아이템 사기")]
public class BuyItem : GameAction
{
    [DataRef(typeof(ItemDef))] public string item;                     // 원본 ID

    protected override void Run()
    {
        var def = (ItemDef)Game.Session.Instances(item);                // 가격은 원본에서 읽는다
        Game.Pay(GameIds.PlayerData.PlayerGold, null, def.price);       // 모자라면 플레이어용 실패(28장 2-4)
        Game.Data().PlayerInventory.items.Add(new Item { id = Game.NewId("item"), item = item });
    }
}

// 화면은 읽기만 한다: 값 칸은 UXML binding-path, 버튼은 행동을 보낸다(35장).
//   OnClick("buy", () => new BuyItem { item = GameIds.Instance.IronSword });
```

골드가 모자라면 `Game.Pay`가 플레이어용 실패(`GameFailure`)를 던지고 행동이 바꾼 것이 모두 취소된다. 화면의 `OnClick`이 그 글을 알림으로 보인다(02장 2.7).

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
public class Strike : GameAction
{
    public TacticsUnit target;                                 // 대상은 객체로(기록에는 id만, 실행 직전에 지금 객체로)
    public int damage;
    protected override void Run()
    {
        target.hp -= damage;                                   // 그냥 고친다
        if (target.hp <= 0) Game.All<Board>()[0].units.Remove(target);
    }
}
```

전장을 DB의 `battle.board`(Session, `Board`)로 만들었거나 코드에서 `session.Data.Define("battle.board", DataZone.Session, new Board())`로 정의했다면, 행동은 `Game.Do(new Strike { target = orc, damage = 12 })`(`orc`는 전장의 `TacticsUnit` 객체)로 실행한다. Undo하면 HP와 리스트에서 빠진 유닛이 **같은 객체로** 제자리에 돌아온다.

### 7.3 카드 게임: 덱·핸드·카드별 사용 횟수

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
public class PlayCard : GameAction
{
    public string card;
    public int cost;

    protected override void Run()
    {
        var state = Game.All<CardState>()[0];
        if (!state.hand.Remove(card)) throw new InvalidOperationException("핸드에 없는 카드입니다.");
        state.mana -= cost;
        state.discard.Add(card);
        state.playedCount.TryGetValue(card, out var n);
        state.playedCount[card] = n + 1;
    }
}
```

리스트 이동·딕셔너리 값 변경이 모두 한 행동의 변경으로 묶여 Undo 한 번에 돌아간다.

이 예는 기록 단위를 보이려는 것이다. 실제 게임에서 비용은 `Game.Pay`(28장 2-4), 같은 카드 두 장·카드마다 강화가 있으면 글자 목록 대신 객체 목록(`DeckCard { id, card, upgraded }` — 04장)으로 둔다.

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
public class TickBuffs : GameAction
{
    public Fighter fighter;
    protected override void Run()
    {
        foreach (var buff in fighter.buffs)
        {
            if (buff is Poison p) fighter.hp -= p.damage;
            buff.turns--;
        }
        fighter.buffs.RemoveAll(b => b.turns <= 0);
    }
}
```

자식 클래스(`Poison`·`Shield`)에도 `[Serializable]`이 있어야 안쪽 필드(`turns`·`damage`)까지 기록된다. 저장 파일에는 실제 형식 이름이 함께 남아 불러올 때 같은 자식 클래스로 돌아온다.

> 이 예는 다형성 리스트가 어떻게 기록되는지를 보이려는 것이다. 게임의 독·보호막처럼 턴 수·중첩·만료가 있는 버프는 이렇게 직접 리스트를 두지 않고 DB › 상태 효과로 만든다. 지속·중첩·스탯 보정·턴마다의 효과·Undo가 이미 들어 있다(`29-Statuses-Schedules-Choices.md`).

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

session.Game.Do(new Strike { target = board.units[1], damage = 12 });   // 대상은 데이터 안의 객체
// board.units[1].hp == 18
session.Undo();
// board.units[1].hp == 30
```

### 7.6 행동 밖에서 고친 값

```csharp
var hero = session.Game.Find<TacticsUnit>("hero");
hero.hp = 12;               // 행동 밖: 다음 Sync(프레임 끝 등)에 행동 밖 변경으로 기록
session.EndFrame(0.016);    // GBibleRuntime 없이 직접 만든 세션이면 프레임 끝을 직접 부른다

session.Game.Do(new Strike { target = hero, damage = 2 });     // hp 10
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
| "Modify를 거치지 않고 바뀌었습니다" 경고 | 예전 방식 데이터를 행동·`Modify` 밖에서 고쳤다(6절). |
| "담을 수 없는 필드" 오류 | 함수·`object`·다차원 배열 필드가 있다. 데이터가 아니면 `[NonSerialized]`를 붙인다. |
| 두 키가 함께 쓰던 객체가 Undo 뒤 엉뚱하거나 불러온 뒤 둘로 갈라짐 | 다른 플레이어 데이터 키끼리 같은 객체를 가리켰다(Undo가 두 번 적용되고, 키마다 따로 저장된다). 같은 객체 공유는 한 키 안에서만 된다(5절 7). 다른 키에서는 id로 가리키고 `Game.Find`로 찾는다. 다른 키로 보내려던 것이면 같은 행동에서 원래 목록에서 뺀다(5절 '키 사이 옮기기'). |
| "인터페이스·추상 컬렉션 형식은 담을 수 없습니다" | 칸을 `IList<T>`·`IDictionary<K, V>`·`IReadOnlyList<T>`로 선언했다. `List<T>`·`Dictionary<K, V>`로 바꾼다(4.3). |
| 같은 행동인데 Undo·불러오기 뒤 결과 차례가 다름 | 규칙이 Dictionary를 훑는 순서에 기댔다. 차례가 필요하면 `List`로(5절 8). |
| 모니터 타임라인에 기록이 매 프레임 쌓임 | 매 프레임 바뀌는 값을 플레이어 데이터에 넣었다. MonoBehaviour 쪽으로 옮긴다. |
| 스프라이트 같은 에셋 참조가 불러온 뒤 비어 있음 | 저장 파일에는 `GameData`(DB 항목) 참조만 ID로 담긴다(되감기 스냅샷은 메모리 안이라 참조를 그대로 둔다). 그 밖의 에셋 참조는 저장 파일에 담기지 않고, 저장할 때 "저장할 수 없는 참조입니다" 경고가 난다. 에셋은 DB ID나 인스턴스 쪽에 두고 플레이어 데이터에는 ID를 둔다. |
| 오래 쥔 객체가 Undo·불러오기 뒤 엉뚱함 | Undo는 빠졌던 객체를 원래 인스턴스로 되돌리지만, 불러오기·멀티플레이 받는 쪽에서는 다른 인스턴스일 수 있다. 값을 읽으려고 오래 들고 있을 것은 id로 들고 `Game.Find`로 다시 찾는다. 행동의 참조 필드에 넣는 것은 옛 객체여도 된다(실행 직전에 같은 id의 지금 객체로 바뀐다, 02 2.2). |
