# 상태 저장소 사용 안내

이 문서는 게임 시스템의 상태 저장소(`StateStore`)와 식별자(ID) 체계를 처음 쓰는 분을 위한 안내서입니다. 개념, 사용 예제, 저장과 버전 관리, 식별자 짓기, 성능 기준, 자주 하는 실수까지 순서대로 설명드립니다.

---

## 1. 한눈에 보기

게임에서 의미 있는 상태(골드, 체력, 인벤토리, 장비, 턴, 도전과제 등)는 모두 상태 저장소 한곳에 둡니다. 저장소를 거치면 아래 기능이 추가 코드 없이 따라옵니다.

- 무르기: 확정 전 행동을 되돌립니다.
- 기록: 무엇이, 왜 바뀌었는지 모니터 타임라인에 남습니다(예: `인벤토리.items[#12].level 3 → 4`).
- 도전과제·조건: 값이 바뀌면 그 값을 보는 조건만 다시 검사합니다.
- 저장·불러오기: 메타 구역을 파일로 저장하고, 버전이 바뀌어도 이어서 읽습니다.
- 모니터: 실행 중 모든 상태를 한 화면에서 봅니다.

쓰는 규칙은 두 가지뿐입니다.

1. 키(ID)는 데이터베이스 창 한곳에서만 짓고, 나머지는 목록에서 고르거나 생성된 코드(`GameIds`)로 씁니다.
2. 저장소 안의 값은 반드시 `Set`(통째로 교체) 또는 `Modify`(일부 수정)로 바꿉니다.

---

## 2. 기본 개념

### 2.1 구역

| 구역 | 담는 것 | 저장 |
|---|---|---|
| 세션(`StateZone.Session`) | 이번 판의 내용: 맵, 유닛, 이번 판 인벤토리, 턴 | 저장하지 않음(판이 끝나면 사라짐) |
| 메타(`StateZone.Meta`) | 프로필: 누적 수치, 도전과제, 해금, 계정 인벤토리 | 저장함 |

두 구역은 저장 여부만 다르고, 쓰는 법과 무르기 규칙은 같습니다.

### 2.2 값의 규칙은 하나입니다

모든 값은 바꾸는 즉시 반영됩니다. 확정하기 전에는 무를 수 있고, 확정하면 굳어집니다. 되돌릴 수 없는 일(도전과제 보상 지급, 저장, 서버 전송)만 확정 시점에 일어납니다.

도전과제는 조건을 만족하는 순간 달성되고, 무르기를 해도 달성은 유지됩니다. 값(예: 골드)만 되돌아가므로 무르고 다시 해도 값이 쌓이지 않아 반복 획득을 막습니다.

### 2.3 담을 수 있는 값

| 종류 | 예 |
|---|---|
| 단순 값 | `bool`, 모든 숫자 형식, `string`, `enum`, `DateTime`, `TimeSpan`, `Guid`, `int?` 같은 nullable |
| 데이터 값 | 임의의 클래스·구조체, `List<T>`, 배열, `Dictionary<K,V>`(키는 숫자·글자·enum), `HashSet<T>`, `SortedSet<T>`, `LinkedList<T>`, `Queue<T>`, `Stack<T>`, 부모 타입 필드에 넣은 자식 클래스 |

필드 깊이와 중첩에는 제한이 없습니다. 장비 목록 안의 강화 효과 목록 안의 태그 목록도 그대로 담깁니다.

담을 수 없는 것은 함수(델리게이트) 필드, `object` 형식 필드, 다차원 배열, 서로를 가리키는 순환 참조입니다. 등록할 때 어느 필드가 문제인지 알려 드립니다.

### 2.4 저장소에 넣지 않는 것

- 매 프레임 바뀌는 값(위치, 속도, 애니메이션): 기록·비교 비용이 커서 저장소 밖에 둡니다.
- 정의 데이터(몬스터 기본 능력치 같은 데이터베이스 항목): 게임 중에 바뀌지 않으므로 넣을 이유가 없습니다.
- 화면 상태와 계산으로 얻을 수 있는 값(합산 공격력 등): 원본만 저장소에 두고 필요할 때 계산합니다.

판단 기준은 "되돌리거나, 저장하거나, 기록하거나, 조건으로 검사할 일이 있는가"입니다.

---

## 3. 시작하기

### 3.1 데이터 클래스 만들기

저장할 데이터는 평범한 클래스로 만듭니다. 특별한 상속이나 전용 어트리뷰트는 필요 없습니다.

```csharp
using System;
using System.Collections.Generic;

[Serializable]
public class Item
{
    public string name;
    public int level;
    public List<string> tags = new List<string>();
}

[Serializable]
public class Inventory
{
    public int gold;
    public List<Item> items = new List<Item>();
}
```

### 3.2 데이터베이스 창에서 상태 키 만들기

1. 메뉴 `Game Systems > 데이터베이스`를 엽니다.
2. 왼쪽의 `식별자 > 상태 키`를 고르고 `+ 추가`를 누릅니다.
3. 창이 뜨면 ID(예: `player.inventory`)와 표시 이름(예: `인벤토리`)을 입력합니다.
4. 상세 패널에서 구역(메타)과 값 형식(`Inventory`)을 목록에서 고릅니다.

값 형식 목록에는 기본 형식, 프로젝트의 enum, 저장소에 담을 수 있는 공개 데이터 클래스가 자동으로 나타납니다. 숫자·글자 같은 단순 형식은 기본값 칸에 기본값을 적을 수 있고, 클래스는 클래스의 초기값(필드 초기화·생성자)을 씁니다.

상태 키는 게임이 시작될 때 자동으로 저장소에 등록됩니다. 코드에서 따로 등록할 필요가 없습니다.

### 3.3 생성된 코드로 쓰기

상태 키를 만들거나 바꾸면 `GameIds.cs`가 자동으로 다시 만들어집니다. 코드에서는 ID를 글자로 쓰지 않고 자동완성으로 고릅니다.

```csharp
using GameSystems;
using GameSystems.Ids;

var state = GameSystemsRuntime.Session.State;

// 읽기: 저장소가 관리하는 실제 인스턴스
Inventory inventory = state.Get(GameIds.State.PlayerInventory);
int count = inventory.items.Count;

// 일부 수정
state.Modify(GameIds.State.PlayerInventory, inv => inv.gold += 100);
```

`GameIds.State.PlayerInventory`의 타입은 `StateKey<Inventory>`입니다. `Get`은 `Inventory`를 돌려주고, `Modify`의 람다도 `Inventory`를 받습니다. ID를 잘못 쓰면 컴파일 단계에서 오류가 나므로 오타가 생길 수 없습니다.

---

## 4. 읽기와 쓰기

모든 타입이 같은 문법을 씁니다.

| 하고 싶은 일 | 코드 |
|---|---|
| 읽기 | `state.Get(key)` 또는 `state.Get<T>("id")` |
| 통째로 바꾸기 | `state.Set(key, 값)` |
| 일부 바꾸기(클래스·컬렉션) | `state.Modify(key, 값 => { ... })` |
| 코드에서 직접 등록(테스트·임시) | `state.Define(key, 구역, 기본값)` |

### 4.1 단순 값

```csharp
state.Set(GameIds.State.PlayerGold, 150);          // StateKey<int>
int gold = state.Get(GameIds.State.PlayerGold);   // 150
```

숫자는 내부에서 `double`로 보관하지만, `Get`이 선언한 형식(`int` 등)으로 바꿔서 돌려 드립니다. 선언한 형식과 다른 값(숫자 키에 글자 등)을 넣으면 거부합니다.

### 4.2 클래스·컬렉션

```csharp
state.Modify(GameIds.State.PlayerInventory, inv =>
{
    inv.items.Add(new Item { name = "검", level = 1 });
    inv.items[0].tags.Add("근접");
    inv.gold -= 50;
});
```

람다 안에서는 무엇을 해도 됩니다. 여러 필드를 바꾸든, 추가·삭제·정렬을 하든 저장소가 전후를 비교해 바뀐 곳을 모두 찾습니다. 람다 안에서 오류가 나면 바꾸기 전으로 자동 복원됩니다.

통째로 바꿀 때는 `Set`을 씁니다. 이때도 게임 코드가 쥐고 있는 인스턴스는 그대로 유지되고 내용만 바뀝니다.

```csharp
state.Set(GameIds.State.PlayerInventory, new Inventory { gold = 0 });
```

### 4.3 읽은 인스턴스를 직접 고치면 안 됩니다

```csharp
var inv = state.Get(GameIds.State.PlayerInventory);
inv.gold = 999;   // 잘못된 예: 저장소가 이 변화를 모릅니다
```

값은 바뀌지만 무르기·기록·도전과제가 이 순간을 놓칩니다. 개발 빌드에서는 행동이 끝날 때와 프레임 끝에 이런 변경을 찾아 "Modify를 거치지 않고 바뀌었습니다" 경고를 남기고 받아들입니다. 이 변경은 무르기로 되돌아가지 않습니다.

---

## 5. 행동, 무르기, 확정

플레이어의 행동은 데이터 클래스로 만들고 `session.Do`로 실행합니다. 행동 하나가 무르기 한 단위입니다.

```csharp
[Serializable]
public class EquipAction : GameAction
{
    public int itemIndex;
    public override string Describe() => $"장비 착용 #{itemIndex}";
}

GameSystemsRuntime.Started += session =>
{
    session.Actions.Register<EquipAction>(action =>
        session.State.Modify(GameIds.State.PlayerInventory, inv => inv.items[action.itemIndex].level++));
};

// 실행
session.Do(new EquipAction { itemIndex = 0 });
```

### 5.1 턴제(무르기가 있는 게임)

`SessionOptions.AutoCommit = false`로 두면 `Commit()`을 부를 때까지 행동이 무르기 대기 상태로 남습니다.

```csharp
GameSystemsRuntime.Configuring += options => options.AutoCommit = false;

session.Do(new EquipAction { itemIndex = 0 });
session.Undo();     // 방금 행동을 되돌림(값·난수 모두)
session.Do(new EquipAction { itemIndex = 1 });
session.Commit();   // 턴 종료: 굳히고, 미뤄 둔 도전과제 보상을 지급
```

무르면 게임 코드가 쥐고 있던 인스턴스가 그대로 제자리로 돌아갑니다. 목록에서 뺐던 장비도 같은 인스턴스, 같은 ID로 돌아옵니다. 행동 중에 예외가 나면 그 행동의 변화는 모두 자동으로 취소됩니다.

### 5.2 실시간(무르기가 없는 게임)

기본값 `AutoCommit = true`에서는 행동마다 즉시 확정됩니다. 되돌리기가 필요하면 무르기 대신 주기적으로 저장 트리(`State.Export`)를 스냅샷으로 남겨 두는 방식을 씁니다.

---

## 6. 필드 규칙과 어트리뷰트

저장소는 Unity가 원래 쓰는 필드 규칙을 그대로 따릅니다. 새로 배울 전용 어트리뷰트는 없습니다.

```csharp
[Serializable]
public class Hero
{
    public string name;                                // public 필드: 담깁니다
    [SerializeField] int level;                        // private이지만 담깁니다
    [NonSerialized] public float cachedPower;          // 담지 않습니다(계산용 임시값)
    [FormerlySerializedAs("atk")] public int power;    // 예전 이름 atk로 저장된 값을 이어받습니다
    public int Speed { get; set; }                     // 속성(property)은 담지 않습니다
}
```

속성(property)은 담지 않습니다. 담을 값은 필드로 선언하세요. `readonly` 필드도 담지 않습니다.

---

## 7. 컬렉션 요소의 ID

리스트·집합 등에 들어 있는 클래스 인스턴스에는 저장소가 내부 ID를 자동으로 붙입니다. 개발자는 인스턴스로만 다루면 되고 ID 필드를 따로 만들 필요가 없습니다.

이 ID 덕분에 목록 중간의 요소를 빼도 뒤 요소들을 잘못 가리키지 않고, 기록은 `items[#12].level`처럼 같은 물건을 계속 가리킵니다. 내부 ID는 저장 파일에도 함께 저장되어 불러온 뒤에도 이어집니다. 코드에서 필요하면 `state.Identity.IdOf(인스턴스)`로 확인할 수 있습니다.

구조체와 숫자·글자 요소는 ID 없이 순번으로 다룹니다.

---

## 8. 변경 알림

```csharp
state.Changed += key => { /* key의 값이 바뀜(무르기 포함) */ };
state.DataChanged += (key, changes) =>
{
    foreach (var change in changes)
        Debug.Log($"{key}: {change}");   // 예: items[#12].level  3 → 4
};
```

도전과제와 조건은 이 알림을 받아, 바뀐 키를 보는 조건만 다시 검사합니다. 조건을 폴링하지 않습니다.

---

## 9. 저장과 불러오기

메타 구역만 저장합니다. 단일 게임은 `GameSystemsRuntime`이 종료할 때 자동으로 저장하고 시작할 때 불러옵니다. 직접 할 때는 아래처럼 씁니다.

```csharp
string json = session.ExportMeta();   // 확정 안 된 행동이 있으면 먼저 확정합니다
session.ImportMeta(json);
```

저장 파일은 필드 이름을 기준으로 쓰고, enum은 이름으로 저장합니다(enum 순서를 바꿔도 안전). 부모 타입 필드에 넣은 자식 클래스는 실제 형식 이름을 함께 남깁니다. 에셋 참조(데이터베이스 항목)는 ID로 저장합니다.

불러올 때 저장 파일에 없는 필드는 클래스의 기본값을 쓰고, 지금 클래스에 없는 필드는 무시한 뒤 경고로 알려 드립니다.

### 9.1 버전과 변환

출시 후 데이터 구조를 바꾸면 `SaveVersion`을 올리고 변환을 등록합니다.

```csharp
// 불러오기 전에 등록되어야 하므로 Configuring(세션 생성 직전)에서 등록합니다.
GameSystemsRuntime.Configuring += options =>
{
    options.SaveVersion = 3;
    options.Migrations.RenameKey(2, "player.money", "player.wallet");                        // v1 → v2
    options.Migrations.MoveField(3, "player.inventory", "items[].title", "items[].name");    // v2 → v3
    options.Migrations.Add(3, "골드 단위 변경", stateTree =>
    {
        // 저장 트리(딕셔너리)를 직접 고칩니다.
    });
};
```

불러올 때 저장 파일의 버전부터 게임 버전까지 변환을 차례로 적용합니다. 게임보다 새 버전의 저장 파일은 거부합니다(구 버전 게임이 새 저장을 망가뜨리지 않도록).

`GameSystemsRuntime`을 쓰지 않고 세션을 직접 만든다면 `new GameSession(options)` 전에 `options.Migrations`에 등록하거나, `ImportMeta`보다 먼저 `session.Migrations`에 등록하세요.

필드 이름만 바꾼 경우는 변환 없이 `[FormerlySerializedAs("예전이름")]`만 달면 됩니다. 상태 키·수치·도전과제의 ID를 데이터베이스 창에서 바꾼 경우도 변환이 필요 없습니다(11.3 참고).

---

## 10. 모니터에서 보기

메뉴 `Game Systems > 모니터`를 열고 플레이하면 아래 탭에서 상태를 봅니다.

- 상태 탭: 모든 키를 분류(ID의 첫 점 앞)별로 보여 드립니다. 단순 값은 클릭해서 고칠 수 있고, 데이터 값은 요약(`Inventory`, `List<Item> 3개` 등)으로 표시됩니다.
- 타임라인 탭: 모든 변화와 원인을 봅니다. 한 줄을 고르면 원인 행동까지 거슬러 올라갑니다.
- 행동 탭: 무르기 대기 중인 행동과 확정된 행동을 봅니다.

클래스는 카드로, 클래스 목록은 카드 격자로 보여 주는 화면은 다음 작업에서 추가합니다.

---

## 11. 식별자(ID) 체계

### 11.1 규칙

- ID는 데이터베이스 창에서 항목을 만들 때 한 번 짓습니다.
- 형식은 영문 소문자로 시작하고 소문자·숫자·밑줄만 씁니다. 분류는 점(.)으로 나눕니다. 예: `player.inventory`, `kills_total`, `boss`
- 한글 이름은 표시 이름에 적습니다. 표시 이름은 자유롭게 바꿔도 됩니다.
- 모든 ID는 프로젝트 전체에서 하나뿐이어야 합니다.

### 11.2 모든 곳에서 목록으로 고릅니다

에디터에서 ID를 가리키는 칸(조건의 수치, 보상, 태그, 상태 키의 값 형식 등)은 직접 입력하는 칸이 없고 목록에서만 고릅니다. 코드에서는 `GameIds`를 씁니다.

```csharp
string killsId = GameIds.Stat.KillsTotal;          // "kills_total"
string bossTag = GameIds.Tag.Boss;                 // "boss"
StateKey<Inventory> key = GameIds.State.PlayerInventory;
```

직접 만든 데이터 클래스에서 다른 항목을 가리키는 필드가 있으면 `[DataRef]`를 붙이세요. 그러면 에디터가 그 종류의 목록을 띄우고, 검증기가 없는 ID를 찾아내고, ID 이름을 바꿀 때 이 필드의 값도 함께 바뀝니다.

```csharp
[Database("몬스터", Group = "전투")]
public class MonsterData : GameData
{
    [DataRef(typeof(TagData))] public List<string> weakTo = new List<string>();
    [DataRef(typeof(ItemData))] public string dropItem;
}
```

### 11.3 ID 바꾸기

상세 패널의 `ID 바꾸기`에서 새 ID를 입력하면 아래가 한꺼번에 처리됩니다.

- 이 ID를 가리키는 모든 필드(조건·보상·태그·`[DataRef]` 칸)
- 에셋 파일 이름
- `GameIds.cs`(코드에서 쓰던 이름은 컴파일 오류로 알려 드리므로 고칠 곳을 바로 찾을 수 있습니다)
- 이미 배포된 저장 파일: 예전 ID로 저장된 값을 새 ID로 이어받도록 변경 이력이 남습니다(런타임이 자동 적용).

### 11.4 삭제 보호

다른 곳에서 가리키는 항목은 삭제할 수 없습니다. 삭제를 누르면 사용처 목록을 보여 드리니, 그곳들의 선택을 먼저 바꾸세요.

### 11.5 태그

태그도 식별자입니다. `식별자 > 태그`에서 태그를 만들고, 모든 데이터의 태그 칸에서는 목록에서 고릅니다. 없는 태그를 가리키면 검증 오류가 나고 빌드가 막힙니다.

### 11.6 생성 코드의 위치

`GameIds.cs`는 상태 키의 값 형식이 들어 있는 어셈블리(asmdef) 폴더 아래 `Generated/`에 만들어집니다. 그래야 그 형식을 참조해 `StateKey<Inventory>`처럼 타입을 붙일 수 있습니다. 형식이 없거나 asmdef 밖(Assembly-CSharp)에 있으면 `Assets/GameSystemsGenerated/`에 만들어집니다.

위치를 직접 정하려면 `ProjectSettings/GameSystems/Ids.json`의 `folder`에 폴더 경로를 적습니다. 그 위치에서 참조할 수 없는 형식의 키는 타입 없이 ID 문자열로만 만들어집니다. 수동으로 다시 만들려면 메뉴 `Game Systems > 식별자 코드 다시 만들기`를 누릅니다.

---

## 12. 성능

데이터 값을 수정할 때마다 저장소는 그 키의 값 전체를 복제·비교합니다. 그래서 편의 대신 약간의 비용이 듭니다. 실측값은 아래와 같습니다(장비 500개, 각 장비에 태그 2개·효과 2개, 요소 약 6천 개).

| 빌드 | 행동 한 번(수정 1회 포함) |
|---|---|
| 개발 빌드(누락 검사 켬) | 약 7.3ms |
| 출시 빌드(누락 검사 끔) | 약 5.0ms |

턴제와 일반적인 상태 변경에는 충분합니다. 다음 기준을 지키면 문제가 생기지 않습니다.

- 자주 같이 바뀌는 것끼리 한 키에 담고, 크기가 크고 자주 바뀌는 부분은 별도 키로 나눕니다. 한 키의 요소가 2만 개를 넘으면 경고가 한 번 뜹니다.
- 매 프레임 바뀌는 값은 저장소에 넣지 않습니다.
- 한 행동 안에서 같은 키를 여러 번 고쳐도 무르기 기록은 하나로 합쳐지므로 메모리는 늘지 않습니다.
- 누락 검사(`SessionOptions.DetectUntrackedChanges`)는 개발 빌드에서만 켜집니다.

---

## 13. 멀티플레이(방향, 아직 구현 전)

게임 코드의 문법은 싱글과 같습니다. 유형 A(턴제 경쟁, 서버 권한)는 서버가 매치 세션을 가지고, 바뀐 경로 목록을 클라이언트에 보냅니다. 클라이언트는 행동만 보낼 수 있습니다. 유형 B(친구 방, 방장 권한)는 방장이 방의 세션을 가지고 같은 방식으로 전달합니다. 무르기용으로 만든 경로 단위 변경 기록을 그대로 네트워크 동기화에 씁니다.

---

## 14. 자주 하는 실수

| 증상 | 원인과 해결 |
|---|---|
| "정의되지 않은 상태 키" 오류 | 데이터베이스 창에서 상태 키를 만들지 않았습니다. 코드에는 `GameIds`를 쓰세요. |
| "Modify를 거치지 않고 바뀌었습니다" 경고 | `Get`으로 받은 인스턴스를 직접 고쳤습니다. `Modify` 안에서 고치세요. |
| "담을 수 없는 필드" 오류 | 함수·`object` 필드가 있습니다. `[NonSerialized]`를 붙이거나 데이터로 바꾸세요. |
| "순환 참조" 오류 | 객체끼리 서로를 가리킵니다. 한쪽은 ID(`[DataRef]` 문자열)로 가리키세요. |
| 불러온 값이 기본값으로 나옴 | 필드 이름을 바꿨습니다. `[FormerlySerializedAs("예전이름")]`을 다세요. |
| 속성(property) 값이 저장되지 않음 | 속성은 담지 않습니다. 필드로 선언하세요. |
| 무르기를 했는데 값이 안 돌아옴 | 자동 확정 세션이거나 이미 확정했습니다. 또는 직접 수정(4.3)한 값입니다. |

---

## 15. API 요약

| 대상 | 멤버 | 설명 |
|---|---|---|
| `StateStore` | `Get(key)`, `Get<T>(id)` | 읽기(데이터 값은 실제 인스턴스) |
| | `Set(key, value)` | 통째로 바꾸기 |
| | `Modify(key, action)` | 클래스·컬렉션 일부 바꾸기 |
| | `Define(key, zone, default)` | 코드에서 직접 등록(보통은 데이터베이스 창) |
| | `Changed`, `DataChanged` | 변경 알림 |
| | `Identity.IdOf(instance)` | 컬렉션 요소의 내부 ID |
| | `CheckUntrackedChanges()` | 누락 검사 수동 실행 |
| | `AddAlias(old, new)` | 예전 키로 저장된 값 이어받기(ID 바꾸기가 자동 처리) |
| `GameSession` | `Do(action)`, `Undo()`, `Commit()` | 행동 실행, 무르기, 확정 |
| | `ExportMeta()`, `ImportMeta(json)` | 저장·불러오기 |
| `SessionOptions` | `AutoCommit`, `SaveVersion`, `Migrations`, `DetectUntrackedChanges` | 확정 방식, 저장 버전, 버전 변환, 누락 검사 |
| `StateKey<T>` | `GameIds.State.XXX` | 타입이 붙은 키(생성 코드) |
| 어트리뷰트 | `[DataRef(typeof(T))]` | 다른 데이터베이스 항목을 가리키는 문자열 필드 |
