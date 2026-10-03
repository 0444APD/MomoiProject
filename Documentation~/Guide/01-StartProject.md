# 01. 프로젝트 시작하기

새 Unity 프로젝트에 Momoi Project(패키지 이름 GBible)를 넣고, **시작 설정** 창으로 코드 자리와 기본 파일을 한 번에 만들고, 게임이 켜질 때 시스템이 우리 설정대로 준비되는지 확인하는 단계다. 이 장이 끝나면 아직 게임 규칙은 없어도 "플레이 버튼을 누르면 세션이 뜨고, 모니터에서 판 데이터가 보이는" 상태가 된다. 예시 게임 카드 던전(턴제 덱빌딩 로그라이크)으로 따라간다.

이 장에서 쓰는 말:

| 말 | 뜻 |
|---|---|
| 패키지(UPM) | Unity Package Manager가 관리하는 코드 묶음. GBible은 프로젝트의 `Packages/com.momoi.gbible` 폴더에 들어가는 **embedded 패키지**다. embedded는 "프로젝트 안에 복사본이 있어 바로 고칠 수 있는 패키지"라는 뜻이다. |
| 시작 설정 | 메뉴 `MomoiProject → 시작 설정` 창. 새 프로젝트에서 한 번, Product Name·선택 모듈·Undo 방식·언어를 고르고 '적용'하면 코드 폴더·asmdef·시작 파일·DB 에셋을 만든다. |
| 세션(`GameSession`) | 게임 한 판에 필요한 모든 시스템(플레이어 데이터·이벤트·난수·기록·행동)을 묶은 객체. 싱글 게임은 `GBibleRuntime`이 게임이 켜질 때 하나를 만든다. |
| DB | 메뉴 `MomoiProject → DB` 창. 기획자와 프로그래머가 데이터 항목(플레이어 데이터·인스턴스·도전과제 …)을 만들고 고치는 곳. |
| 모니터 | 메뉴 `MomoiProject → 모니터` 창. 플레이 중인 세션의 값·행동·기록을 보고 조작한다. |
| 플레이어 데이터 | 게임 중에 바뀌는 값(골드·덱·체력 …)을 담는 칸. 값 형식으로는 기본 값(`int`·`string` …)이나 **내가 쓴** `[Serializable]` 클래스를 고른다. |
| Undo | 마지막 행동을 되돌리는 기능. 이름 그대로 쓴다. 행동 하나가 Undo 한 단위다. |
| asmdef | Unity의 Assembly Definition 파일. 폴더 하나를 별도 어셈블리(컴파일 단위)로 묶고, 어느 어셈블리를 참조할지 적는다. 시작 설정이 만들어 주므로 직접 쓸 일은 없다. |
| `GameIds` | DB의 ID를 C# 상수와 접근자(`Game.Data().Run` …)로 만든 생성 파일. 코드에서 ID를 글자로 치지 않고 상수로 고른다. |

---

## 1. 이 단계의 목표

패키지를 넣고, 시작 설정으로 코드 구조(규칙 / Unity 쪽 / 에디터 / 테스트)와 시작 파일을 만든다. 판·프로필 데이터 클래스를 채워 DB에 등록하고, 세션 옵션과 시작 코드를 Bootstrap 한 파일에 모아 첫 플레이에서 그 설정이 적용되는지 확인한다.

## 2. 할 일 순서

1. GBible 폴더를 `Packages/com.momoi.gbible`에 넣는다.
2. 저절로 열리는 **시작 설정** 창에서 Product Name·선택 모듈·Undo 방식·언어를 고르고 **적용**한다.
3. 시작 설정이 만든 `Rules/PlayerData.cs`의 `RunState`·`Profile`에 필드를 채우고 컴파일한다.
4. DB › 플레이어 데이터에 판(`run`)·프로필(`profile`)을 만들고 저장(Ctrl+S)해서 `GameIds.cs`가 생기게 한다.
5. 세션 옵션(Undo 보관 수·시드)을 DB › 시작 파일 › **Bootstrap** 화면에서 정하고, 시작 뒤 할 일(`Started`)을 `Client/Bootstrap.cs`에 쓴다.
6. 플레이해서 모니터로 확인한다.

순서가 중요한 곳은 3 → 4 → 5다. DB는 컴파일된 클래스만 값 형식으로 고를 수 있고, `GameIds`는 DB 항목으로 만들어진다. 그래서 **"클래스 → DB 항목 → 생성 코드 → 그 이름을 쓰는 코드"** 순서로 간다. 이 순서를 거꾸로 하면, 예를 들어 Bootstrap에서 `Game.Data().Run`을 먼저 쓰면 아직 생성되지 않은 이름이라 컴파일 오류가 난다.

## 3. 판단 기준

**선택 모듈을 켤까**

| 이럴 때 | 이렇게 |
|---|---|
| 아이템을 칸·개수로 들고 다닌다(가방·물약 칸) | 인벤토리 시스템을 켠다. 플레이어 데이터의 값 형식에 `Inventory`가 나온다. |
| 대사·컷신·상점 주인 대화가 있다 | 다이얼로그 시스템을 켠다. |
| 둘 다 없다 | 켜지 않는다. 꺼진 모듈의 코드는 컴파일·빌드에 들어가지 않는다. |

모듈은 나중에 언제든 켜고 끈다(4-2 끝). 켜고 끌 때 asmdef 참조는 저절로 맞춰진다.

**코드를 어느 폴더에 둘까**

시작 설정은 코드 자리를 `Assets/Code` 아래 네 폴더로 고정해서 만든다. 폴더마다 asmdef가 하나씩 있다. 프로그래머가 고를 것은 "이 파일을 어느 폴더에 둘까" 하나뿐이다.

| 폴더(어셈블리) | 들어가는 것 | 판단 질문 |
|---|---|---|
| `Rules` (`MomoiProject.Rules`) | 데이터 클래스·키워드·이벤트·행동·Block·`[BindTo]` 함수·규칙 도우미 | "서버가 있다면 서버에서 돌아야 하는가?" → 그렇다면 여기 |
| `Client` (`MomoiProject.Client`) | Bootstrap, 판 흐름을 모는 `MonoBehaviour`, UI·연출·사운드 재생 | "화면에 보여 주거나 Unity 씬에 붙는가?" → 그렇다면 여기 |
| `Editor` (`MomoiProject.Editor`) | 메뉴·검사 도구 같은 에디터 확장(Editor에서만 컴파일) | "에디터에서만 쓰는가?" |
| `Tests` (`MomoiProject.Tests`) | 규칙 EditMode 테스트([08장](08-TestAndDebug.md)) | "규칙을 플레이 없이 시험하는가?" |

이렇게 나눠 두면 다음이 구조로 지켜진다.

- **규칙은 화면을 모른다.** `Rules`는 `Client`를 참조하지 않는다. 그래서 행동 안에서 `BattleHud`를 부르면 그 자리에서 컴파일 오류가 난다. 나중에 규칙만 떼어 서버로 옮길 때([11장](11-MultiplayerReady.md)) 화면 코드가 딸려 오지 않는다.
- **화면은 규칙을 안다.** `Client`는 `Rules`를 참조하므로 `Game.Data().Run`·이벤트·행동을 그대로 쓴다. 반대로 말하면 화면이 데이터 필드를 직접 고치는 것까지 막지는 못한다. "값은 행동으로만 바꾼다"는 습관으로 지킨다([09장](09-Habits.md)).
- **테스트가 규칙을 부를 수 있다.** Unity 규칙상 테스트 asmdef는 기본 어셈블리(`Assembly-CSharp`)를 참조할 수 없다. 규칙이 `MomoiProject.Rules`에 있으므로 `Tests`가 그것을 참조해 시험한다.
- **컴파일이 덜 느려진다.** 고친 어셈블리와 그것을 참조하는 어셈블리만 다시 컴파일된다. 예를 들어 UI만 고치면 `Rules`는 다시 컴파일되지 않는다.

`Rules`는 지금 `GBible.Unity`를 참조하고 엔진 참조도 열려 있다. 그래서 규칙 코드에 `Sprite` 같은 엔진 형식을 쓰는 것은 컴파일러가 막지 않는다. 규칙에는 엔진 형식 대신 ID를 두는 습관으로 피한다([02장](02-DesignToCode.md)).

`Rules`·`Client` 아래에 하위 폴더는 자유롭게 만든다. DB의 클래스·이벤트 탭은 `Assets/Code` 아래의 실제 폴더별로 클래스를 묶어 보여 준다(그룹 "Rules/Data" = 폴더 `Assets/Code/Rules/Data`). 폴더 이름이 곧 기획자가 보는 분류가 되므로 `Data`·`Events`처럼 뜻이 드러나게 짓는다.

**Undo 방식과 시드**

| 이럴 때 | DB › 시작 파일 › Bootstrap 화면 | 같은 뜻의 `SessionOptions` |
|---|---|---|
| 턴제·카드·퍼즐: 행동 하나씩 되돌린다 | Undo 방식 "행동 단위", 오래 플레이하면 Undo 보관 수로 상한 | `History = HistoryMode.Actions`, `HistoryLimit` |
| 실시간 액션: 일정 간격으로 되감는다 | Undo 방식 "스냅샷" + 스냅샷 간격·보관 수 | `History = HistoryMode.Snapshots`, `SnapshotInterval`·`SnapshotLimit` |
| 되돌리기가 없다 | Undo 방식 "쓰지 않음"(기록 비용 없음) | `History = HistoryMode.None` |
| 버그를 같은 순서로 재현해야 한다 | "개발 빌드에서 시드 고정"을 켜고 시드를 적는다 | 개발 빌드에서만 `Seed` 고정 |

이 값들은 에디터 화면에서 정한다(4-5). 코드(`Configure`)로도 같은 값을 바꿀 수 있고, 그때는 코드가 이긴다.

## 4. 카드 던전에서 실제로

### 4-1. 설치

1. GBible 저장소 폴더를 프로젝트의 `Packages/com.momoi.gbible`에 둔다(복사하거나 그 자리에 clone한다). `Packages` 폴더는 `Assets`와 나란히 있는 폴더다.
2. Unity 6으로 연다. Unity가 패키지의 `package.json`을 읽고, 함께 필요한 `com.unity.nuget.mono-cecil`을 저절로 설치한다. 이 패키지가 있어야 컴파일할 때 플레이어 데이터 비교 코드가 생긴다.
3. Package Manager 창의 In Project 목록에 **GBible**이 보이면 된다.

embedded 패키지라서 GBible 코드를 고치는 방법은 예전과 같다. `Packages/com.momoi.gbible` 안의 파일을 고치고 저장하면 Unity가 바로 다시 컴파일한다. 게임 코드는 이 폴더 안에 두지 않는다(게임 코드 자리는 4-2가 만드는 `Assets/Code`).

### 4-2. 시작 설정

시작 설정을 마치지 않은 프로젝트를 열면 **시작 설정** 창이 저절로 한 번 열린다. 닫았다면 메뉴 `MomoiProject → 시작 설정`으로 연다. 고르는 것은 네 가지다.

| 칸 | 카드 던전의 선택 | 쓰이는 곳 |
|---|---|---|
| Product Name | `Card Dungeon` | 빌드 파일·창 제목·저장 폴더 이름(Unity Player Settings의 Product Name) |
| 선택 모듈 | 인벤토리 시스템 ✔, 다이얼로그 시스템 ✔ | 물약을 3칸 가방에 들고 다니고(인벤토리), 상점 주인과 대화한다(다이얼로그) |
| Undo 방식 | 행동 단위(턴제) | 카드 한 장을 잘못 냈을 때 되돌린다. DB › 시작 파일 › Bootstrap 화면과 같은 값이다 |
| 언어 | 원문 한국어, 영어 ✔ | 다국어 설정과 언어별 텍스트 표 |

**적용**을 누르면 다음이 만들어진다. 창 아래에 만든 것 목록이 나온다.

```
Assets/
  Code/
    Rules/    MomoiProject.Rules.asmdef   PlayerData.cs   ← RunState·Profile 뼈대
    Client/   MomoiProject.Client.asmdef  Bootstrap.cs    ← 시작 코드(Configuring·Started)
    Editor/   MomoiProject.Editor.asmdef
    Tests/    MomoiProject.Tests.asmdef
  MomoiProjectData/
    Resources/MomoiProject/GameDatabase.asset            ← DB 데이터·세션 옵션(런타임이 읽는다)
    Resources/MomoiProject/LocalizationSettings.asset    ← 다국어 설정
    Texts/Texts_ko.asset  Texts_en.asset                 ← 언어별 텍스트 표
Packages/com.momoi.gbible/                                ← GBible(손대지 않아도 된다)
ProjectSettings/MomoiProject/Features.json                ← 켠 모듈(팀이 같이 쓴다)
```

asmdef의 참조는 시작 설정이 채운다. 열어 볼 일은 없지만 내용은 이렇다.

| 어셈블리 | 참조 |
|---|---|
| `MomoiProject.Rules` | `GBible.Core`, `GBible.Unity`, 켠 모듈 중 데이터 형식을 내놓는 것(`GBible.Inventory`) |
| `MomoiProject.Client` | `MomoiProject.Rules`, `GBible.Core`, `GBible.Unity`, 켠 모듈 전부(`GBible.Inventory`, `GBible.Dialogue`) |
| `MomoiProject.Editor` | 위 둘 + `GBible.Editor`, 켠 모듈. Editor 플랫폼 전용 |
| `MomoiProject.Tests` | `MomoiProject.Rules`, `GBible.Core`, `GBible.Unity`, Test Runner, NUnit. 테스트 실행 때만 컴파일 |

`Rules`에 다이얼로그 모듈이 빠져 있는 것은 의도다. 대화는 화면 쪽 기능이라 규칙이 몰라도 된다.

**시작 파일 바로가기.** 시작 설정이 만든 파일은 프로젝트마다 하나씩이고 자주 연다. 그래서 DB 창 사이드바 맨 위 **시작 파일** 그룹에 모여 있다.

| 항목 | 누르면 |
|---|---|
| **Bootstrap** | 세션 옵션 화면(4-5)이 열린다. 화면 위 **cs 열기**로 `Client/Bootstrap.cs`를 연다 |
| **RunState** | 판 데이터 클래스의 cs 파일을 바로 연다 |
| **Profile** | 프로필 데이터 클래스의 cs 파일을 바로 연다 |

RunState·Profile 바로가기는 DB의 플레이어 데이터 `run`·`profile`이 가리키는 클래스를 따라간다. 그래서 클래스 이름을 바꾸거나 다른 파일로 옮겨도 그 클래스의 파일이 열리고, 사이드바에도 바뀐 이름이 보인다. 아직 DB 항목이 없으면 시작 설정이 만든 `Rules/PlayerData.cs`를 연다.

**나중에 바꾸기.** 시작 설정은 다시 열어 다시 적용해도 안전하다. 이미 있는 파일(asmdef·Bootstrap·PlayerData)은 덮어쓰지 않고, 없는 것만 만든다.

| 바꿀 것 | 방법 |
|---|---|
| Product Name | 시작 설정을 다시 적용하거나 Player Settings에서 바꾼다 |
| 선택 모듈 | DB 사이드바의 해당 모듈 그룹 첫 줄 **기능 활성화**(또는 시작 설정). asmdef 참조는 저절로 따라 바뀐다 |
| Undo 방식 | DB › 시작 파일 › **Bootstrap** 화면(또는 시작 설정) |
| 언어 | 시작 설정에서 더하거나 다국어 설정에서 더하고 뺀다 |

모듈을 쓰는 게임 코드는 `#if GBIBLE_DIALOGUE`·`#if GBIBLE_INVENTORY`로 감쌀 수 있다. 모듈을 켜고 끄면 이 심볼도 저절로 켜지고 꺼지므로, 감싼 코드는 모듈을 꺼도 컴파일이 깨지지 않는다. 카드 던전은 판 데이터 안에 인벤토리를 두므로(아래 4-3) 인벤토리를 늘 켜 두는 전제로 감싸지 않는다.

카드 던전은 두 폴더 아래를 이렇게 나눴다.

```
Assets/Code/
  Rules/
    Data/  Events/  Actions/  Cards/  Relics/  Run/
    Generated/            ← GameIds.cs (DB가 만든다, 4-4)
  Client/
    UI/  Presentation/    ← BattleHud, 연출, 상점 대화
    Bootstrap.cs  RunDirector.cs
  Editor/
  Tests/
```

### 4-3. 데이터 클래스 채우기

시작 설정이 만든 `Rules/PlayerData.cs`에는 빈 클래스 둘이 있다.

```csharp
namespace MomoiProject
{
    // 판 하나 동안의 값. 판이 끝나면 버린다.
    [Serializable] public class RunState { }

    // 판이 끝나도 남는 값(해금·누적 기록·설정).
    [Serializable] public class Profile { }
}
```

**`RunState`·`Profile`은 GBible이 주는 타입이 아니다.** 시작 설정이 이름만 잡아 둔 **우리 프로젝트의 평범한 클래스**다. 특별한 상속이나 래퍼 없이 `[Serializable]`만 붙어 있다. 필드는 우리가 채운다. 이름도 바꿔도 된다(바꾸려면 DB 항목을 만들기 전, 즉 4-4 전에 바꾸는 편이 깔끔하다).

카드 던전은 이렇게 채운다. 어떤 클래스를 둘지는 [02장](02-DesignToCode.md)에서 정하고, 여기서는 두 덩어리만 보자.

```csharp
[Serializable]
public class RunState
{
    public int floor;
    public int gold;
    public Hero hero = new Hero();
    public List<DeckCard> deck = new List<DeckCard>();    // 카드 원본 ID + 이 장만의 바뀌는 값
    public Battle battle;                                 // 전투 밖이면 null
    public Inventory potions = new Inventory { capacity = 3 };

    public bool IsActive => floor > 0;                    // 진행 중인 판이 있는지
}

[Serializable]
public class Profile
{
    public int totalGold;
    public int bestFloor;
    public List<string> unlocked = new List<string>();    // 해금한 카드 원본 ID
}
```

(발췌다. 실제 클래스에는 가진 유물 목록도 있다.) 파일 위치는 자유다. 카드 던전은 판에 쓰이는 클래스(`Hero`·`DeckCard`·`Battle` …)와 함께 두려고 두 클래스를 `Rules/Data/Model.cs`로 옮겼다([02장](02-DesignToCode.md) 4-10). 옮겨도 `Rules` 폴더 안이면 아무것도 바뀌지 않는다.

- 판 데이터에는 ID와 게임 중에 바뀌는 값만 둔다. 덱의 카드 한 장(`DeckCard`)은 카드 원본의 ID와 강화 여부만 들고, 이름·비용·그림은 DB의 원본에서 읽는다. 이 기준은 [02장](02-DesignToCode.md)에서 자세히 본다.
- `Inventory`는 인벤토리 시스템의 형식이다. 4-2에서 모듈을 켜서 `Rules`가 `GBible.Inventory`를 참조하게 된 것이 여기서 쓰인다.
- 필드의 초기값(`new Hero()`, 빈 리스트, `capacity = 3`)이 새 판의 시작 상태가 된다.

저장하고 컴파일이 끝나면 다음으로 간다.

### 4-4. DB에 플레이어 데이터 만들기 → `GameIds`

1. DB 사이드바 **선언** 그룹의 **플레이어 데이터** 탭 → **+ 새로 만들기**.
2. ID `run`으로 만들고 상세에서 `zone`(구역)을 `Session`, `valueType`(값 형식)을 `RunState`로 고른다. 값 형식 목록에는 기본 값(`int`·`string` …)과 4-3처럼 우리가 쓴 `[Serializable]` 클래스가 나온다. 클래스 값이라 `defaultValue`는 비워 둔다(클래스의 필드 초기값으로 시작한다).
3. 같은 방법으로 `profile`을 만든다: `zone` = `Meta`, `valueType` = `Profile`.
4. **Ctrl+S**로 저장한다.

저장하면 `GameIds.cs`가 자동으로 다시 만들어진다(수동으로는 `MomoiProject → 식별자 코드 다시 만들기`, 콘솔에 "[MomoiProject] 식별자 코드를 만들었습니다: 경로"). 값 형식이 `MomoiProject.Rules` 안에 있으므로 파일은 `Assets/Code/Rules/Generated/GameIds.cs`에 생긴다. 이 파일은 직접 고치지 않는다.

생긴 파일(네임스페이스 `GBible.Ids`)에는 형식이 붙은 키와 이름으로 읽는 확장 메서드가 함께 들어 있다. 그래서 게임 코드는 이렇게 쓴다.

```csharp
using GBible.Ids;

var run = Game.Data().Run;   // RunState 객체(저장소가 관리하는 실제 객체)
run.gold += 10;              // 필드는 그냥 고친다
```

**게임이 켜지면 무슨 일이 일어나나.** 이 DB 항목이 "저장소에 이 칸을 만들라"는 선언이다. `GBibleRuntime`은 게임이 켜질 때 DB를 읽어 항목마다 칸을 만든다. `run` 칸에는 `new RunState()`를 하나 만들어 넣고, 저장된 판이 있으면 그 값으로 채운다. 그래서 코드에서 따로 `Define`을 부를 필요가 없다.

```
게임 시작
  ① DB 읽기 → "run" = new RunState() (Session), "profile" = new Profile() (Meta)
  ② 저장 파일 불러오기 → 있으면 위 객체에 값을 채운다
  ③ Started 이벤트를 쏜다 → Bootstrap (4-5)
```

DB로 정의하면 코드 정의(`session.Data.Define(…)`)에는 없는 것이 따라온다.

- `GameIds`의 형식이 붙은 접근자(`Game.Data().Run`). ID 오타는 컴파일 오류로 잡힌다.
- ID 이름 변경 이력. 출시 뒤 `run`을 다른 이름으로 바꿔도 예전 저장 파일의 값을 이어받는다.
- 저장 호환 검사, 그리고 코드를 고치지 않고 DB에서 바꾸는 표시 이름·기본값.

코드로 직접 `Define`하는 것은 DB 없이 세션을 손으로 만드는 테스트([08장](08-TestAndDebug.md)) 정도다.

구역은 저장 파일과 같다. `Session`은 이번 판(`run.json`), `Meta`는 판이 바뀌어도 남는 프로필(`profile.json`)이다. 무엇을 어느 쪽에 둘지는 [02장](02-DesignToCode.md) 3절에서 정한다.

### 4-5. Bootstrap — 세션 옵션과 시작 코드

Bootstrap은 "게임이 켜질 때 한 번 정하는 것"을 모으는 곳이다. 두 부분으로 되어 있다.

- **세션 옵션**(Undo 방식·보관 수·시드)은 에디터 화면에서 정한다. 값만 고르면 되는 것이라 코드를 열 필요가 없다.
- **시작 코드**(`Client/Bootstrap.cs`)에는 코드로만 할 수 있는 일을 둔다. 세션이 준비된 뒤의 등록 작업(`Started`)과, 조건에 따라 옵션을 바꾸는 일(`Configure`)이다.

**세션 옵션 화면.** DB 창 사이드바 맨 위 **시작 파일 › Bootstrap**을 누른다. 고친 값은 DB 에셋(`GameDatabase`)에 바로 저장된다. Ctrl+S를 누를 필요가 없다. 카드 던전은 이렇게 정한다.

| 칸 | 카드 던전 | 이유 |
|---|---|---|
| Undo 방식 | 행동 단위(턴제) | 카드 한 장을 잘못 냈을 때 되돌린다(시작 설정에서 고른 값이 들어 있다) |
| Undo 보관 수 | 200 | 행동마다 바뀐 값의 이전 모습을 보관하므로, 오래 플레이해도 기록이 끝없이 늘지 않게 상한을 둔다(0 = 제한 없음) |
| 개발 빌드에서 시드 고정 | 켬, 시드 20261003 | 버그를 같은 순서로 재현한다 |

Undo 방식을 "스냅샷"으로 바꾸면 보관 수 대신 스냅샷 간격(초)·스냅샷 보관 수 칸이 나온다.

시드는 개발 빌드에서만 고정된다. 에디터 플레이는 개발 빌드로 치므로 에디터에서는 늘 같은 시드로 시작하고, 출시 빌드는 지금 시각을 시드로 쓴다. 같은 시드에 같은 행동이면 `Game.Random`의 결과(덱 섞기·보상 골드)가 같으므로 버그를 같은 순서로 재현할 수 있다.

**시작 코드.** 화면 위의 **cs 열기**로 `Client/Bootstrap.cs`를 연다. 게임이 켜질 때 끼어드는 지점은 둘이다.

- `GBibleRuntime.Configuring` — 세션을 만들기 **직전**. `SessionOptions`를 받는다. 위 화면의 값이 이미 들어 있다. 세션은 아직 없다.
- `GBibleRuntime.Started` — 세션 준비가 **끝난 뒤**(DB 데이터 등록·저장 불러오기까지). 준비된 세션을 받는다.

`GBibleRuntime`은 첫 씬을 불러오기 전에 세션을 만든다. 그래서 두 이벤트는 그보다 먼저인 `AfterAssembliesLoaded` 시점에 연결해야 한다. `MonoBehaviour.Awake`에서 연결하면 이미 늦다. 시작 설정이 만든 파일은 이미 이 형태다.

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
static void Hook()
{
    GBibleRuntime.Configuring += Configure;
    GBibleRuntime.Started += Started;
}
```

Bootstrap이 `Rules`가 아니라 `Client`에 있는 이유는, 이것이 Unity의 게임 시작(`RuntimeInitializeOnLoadMethod`·`GBibleRuntime`)에 붙는 코드라서다. 서버라면 서버가 세션을 만드는 자리에서 같은 옵션을 따로 정한다.

**`Configure`는 보통 비워 둔다.** 화면에서 고를 수 있는 값은 화면에서 정한다. 코드가 필요한 경우는 두 가지다.

- **화면에 없는 옵션을 정할 때.** 예를 들어 출시 뒤 저장 구조를 바꿨을 때의 저장 버전과 변환(`SaveVersion`·`Migrations`, [10장](10-SaveAndRelease.md))이 그렇다.
- **조건에 따라 바꿀 때.** 예를 들어 "데모 빌드는 Undo를 끈다"가 그렇다.

```csharp
static void Configure(SessionOptions options)
{
#if DEMO_BUILD
    options.History = HistoryMode.None;   // 화면 값보다 이 줄이 이긴다
#endif
}
```

코드가 화면 값을 바꾸면 플레이 중 모니터 **기록**에 "코드(Configuring)가 DB › Bootstrap의 세션 옵션을 바꿨습니다: Undo 방식 Actions → None"이 남는다. "화면에서 바꿨는데 왜 안 먹지?"를 바로 알 수 있다.

**`Started`에서 하는 일.** 세션이 있어야 할 수 있는 등록을 한다. 개발용 명령만 두는 곳이 아니라, **출시 빌드에서도 도는 등록**이 여기 온다.

| 하는 일 | 예 | 출시 빌드 |
|---|---|---|
| 프로젝트 고유 기능의 감시 등록 | 유물마다 발동 조건을 `session.Triggers.Register`로 건다([04장](04-AddingContent.md)) | 돈다 |
| 행동 처리기 등록 | `session.Actions.Register<SkipFloor>(…)` | 돈다 |
| 이벤트 받기(분석·외부 서비스 알림 등) | `session.Events.Subscribe<BattleWon>(…)` | 돈다 |
| 개발용 명령 | 모니터 **명령** 탭의 버튼 | `Debug.isDebugBuild`로 감싸 빼낸다 |

카드 던전은 처음에 개발용 명령 하나만 둔다. 값을 고치는 명령도 `DeveloperEditAction`으로 감싸 행동 하나로 실행하면, 게임 행동과 똑같이 기록되고 Undo된다. `Game.Data().Run`을 쓰므로 파일 위에 `using GBible.Ids;`를 더한다.

```csharp
static void Started(GameSession session)
{
    if (Debug.isDebugBuild)
        session.Debug.Register("카드 던전", "골드 +100", () => session.Do(new DeveloperEditAction
        {
            label = "골드 +100",
            Edit = () => session.Game.Data().Run.gold += 100,
        }));
}
```

등록할 것이 늘어나면 `Started` 한 곳에 몰아 두지 않아도 된다. 기능마다 자기 파일에서 `GBibleRuntime.Started += …`로 따로 연결해도 똑같이 돈다. 카드 던전의 유물 감시(`RelicRuntime`)가 그렇게 되어 있다.

`Started`에서 하지 않는 것이 둘 있다.

- **플레이어 데이터 정의.** 4-4의 DB 항목이 이미 ①에서 만들었다.
- **처음 값 넣기(시작 골드 등).** 그 값은 판을 시작하는 행동(`StartRun`, [03장](03-GameLoop.md))이나 클래스의 필드 초기값이 정한다.

카드 던전의 Bootstrap에는 "적 모두 처치" 명령도 있다. 이것은 피해 규칙(`Rules.Damage`)을 쓰므로 규칙을 만든 뒤([02장](02-DesignToCode.md)·[03장](03-GameLoop.md)) 더한다.

### 4-6. 첫 플레이 확인

플레이 버튼을 누르고 다음을 본다.

1. 하이어라키의 DontDestroyOnLoad 아래에 `[MomoiProject]`와 `[오디오]` 오브젝트가 있다. 세션은 씬과 상관없이 이 오브젝트와 함께 산다.
2. `MomoiProject → 모니터`를 연다. 머리 줄이 "시드 20261003 · …"으로 시작하면 Bootstrap 화면의 시드가 적용된 것이다.
3. **데이터** 탭의 **판 데이터**에 `run`, **프로필 데이터**에 `profile`이 보인다. 들어가 보면 `RunState`의 필드(`floor`·`gold`·`hero`·`deck` …)가 클래스 초기값으로 있다.
4. **명령** 탭에 "카드 던전" 분류와 **골드 +100**이 있다. 누르면 **행동** 탭에 "[개발] 골드 +100"이 생기고, 데이터 탭의 `run › gold`가 100 오른다. 머리의 **Undo**를 누르면 0으로 돌아간다.
5. 콘솔에 "[MomoiProject]"로 시작하는 경고가 없다. "예전 방식(리플렉션)으로 비교합니다"가 보이면 mono-cecil 패키지가 설치되지 않은 것이다(Package Manager에서 확인).

여기까지 되면 시스템과 게임 코드가 연결된 것이다. 아직 판을 여는 행동이 없으므로 `run`은 빈 값 그대로다. 다음 장에서 기획을 클래스로 옮긴다.

## 5. 다른 장르라면

> **다른 장르라면** — 실시간 액션 게임은 DB › 시작 파일 › Bootstrap 화면(또는 시작 설정)에서 Undo 방식을 "스냅샷"으로 고르고, 스냅샷 간격(초)·보관 수로 되감기 범위를 정한다(간격 × 개수 = 되감을 수 있는 시간). 게임 코드는 어느 방식이든 `session.Undo()` 하나로 되돌린다. 장르별 설정은 [07장](07-GenreRecipes.md).

> **다른 장르라면** — 이어 하기가 없는 로그라이크라면 판 자동 저장을 끈다. 게임이 끝날 때 진행 중인 판을 저장하는 `GBibleRuntime.AutoSaveRun`은 기본이 `true`다. Bootstrap의 `Hook`에서 `GBibleRuntime.AutoSaveRun = false;` 한 줄이면 된다. 저장 정책 전체는 [10장](10-SaveAndRelease.md).

> **다른 장르라면** — 대사가 없는 퍼즐 게임은 시작 설정에서 다이얼로그 시스템을 켜지 않는다. 꺼진 모듈의 코드는 빌드에 들어가지 않는다.

## 6. 체크리스트

- [ ] GBible이 `Packages/com.momoi.gbible`에 있고, Package Manager에 GBible과 `com.unity.nuget.mono-cecil`이 보인다.
- [ ] 시작 설정을 적용했다. `Assets/Code/Rules·Client·Editor·Tests`와 각 asmdef, `GameDatabase.asset`이 생겼다.
- [ ] 쓸 선택 모듈을 켰다(`Features.json`이 바뀌었다).
- [ ] 규칙 코드는 `Rules`에, 화면·씬 코드는 `Client`에 있다.
- [ ] 시작 설정이 만든 `RunState`·`Profile` 뼈대에 필드를 채웠다.
- [ ] 판·프로필 플레이어 데이터를 DB에 만들고 저장했다. `GameIds.cs`가 `Assets/Code/Rules/Generated/`에 있다.
- [ ] DB › 시작 파일 › Bootstrap 화면에서 Undo 방식·보관 수·시드를 정했다. `Started`에서 플레이어 데이터를 다시 정의하지 않는다.
- [ ] 모니터 머리에 고정 시드가 보이고, 디버그 명령 → Undo가 된다.
- [ ] 콘솔에 "[MomoiProject]" 경고가 없다.

## 7. 더 보기

- [20. 시작하기](../Manual/20-Setup.md) — 설치, 자동 시작 순서, `SessionOptions`의 모든 칸, 선택 기능, `GameIds` 규칙, 어셈블리
- [01. 플레이어 데이터](../Manual/01-PlayerData.md) — DB에서 만들기, `Game.Data()`, `Define`
- [21. DB](../Manual/21-Editor-Database.md) — 사이드바, 저장 흐름(작업본)
- [22. 모니터와 디버깅](../Manual/22-Monitor-Debug.md) — 탭 구성, 명령
- [02. 행동과 Undo](../Manual/02-Actions-Undo.md) — Undo 방식
