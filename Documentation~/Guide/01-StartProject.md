# 01. 프로젝트 시작하기

새 Unity 프로젝트에 Momoi Project를 넣고, 게임 코드를 둘 자리를 만들고, 게임이 켜질 때 시스템이 우리 설정대로 준비되는지 확인하는 단계다. 이 장이 끝나면 아직 게임 규칙은 없어도 "플레이 버튼을 누르면 세션이 뜨고, 모니터에서 판 데이터가 보이는" 상태가 된다. 예시 게임 카드 던전(턴제 덱빌딩 로그라이크)으로 따라간다.

이 장에서 쓰는 말:

| 말 | 뜻 |
|---|---|
| 세션(`GameSession`) | 게임 한 판에 필요한 모든 시스템(플레이어 데이터·이벤트·난수·기록·행동)을 묶은 객체. 싱글 게임은 `GBibleRuntime`이 게임이 켜질 때 하나를 만든다. |
| 도감 | 메뉴 `MomoiProject → 도감` 창. 기획자와 프로그래머가 데이터 항목(플레이어 데이터·인스턴스·도전과제 …)을 만들고 고치는 곳. |
| 모니터 | 메뉴 `MomoiProject → 모니터` 창. 플레이 중인 세션의 값·행동·기록을 보고 조작한다. |
| 플레이어 데이터 | 게임 중에 바뀌는 값(골드·덱·체력 …)을 담는 칸. 평범한 `[Serializable]` 클래스를 쓴다. |
| Undo | 마지막 행동을 되돌리는 기능. 이름 그대로 쓴다. 행동 하나가 Undo 한 단위다. |
| asmdef | Unity의 Assembly Definition 파일. 폴더 하나를 별도 어셈블리(컴파일 단위)로 묶고, 어느 어셈블리를 참조할지 적는다. |
| `GameIds` | 도감의 ID를 C# 상수로 만든 생성 파일. 코드에서 ID를 글자로 치지 않고 상수로 고른다. |

---

## 1. 이 단계의 목표

시스템을 설치하고, 쓸 선택 모듈을 켜고, 게임 코드를 "게임 로직 / 화면 / 에디터" 세 어셈블리로 나눠 둔다. 그리고 세션 옵션(Undo 방식·시드)과 시작 코드를 한 파일(Bootstrap)에 모아, 첫 플레이에서 그 설정이 적용되는지 확인한다.

## 2. 할 일 순서

1. 저장소 폴더를 `Assets` 아래에 넣고 `com.unity.nuget.mono-cecil` 패키지가 있는지 본다.
2. `MomoiProject → 도감`을 한 번 열어 런타임 데이터 목록(`GameDatabase`)이 생기게 한다.
3. 쓸 선택 모듈(인벤토리 시스템·다이얼로그 시스템)을 도감 사이드바에서 켠다.
4. 게임 코드 폴더와 asmdef 세 개(게임 로직·화면·에디터)를 만든다.
5. 플레이어 데이터가 될 데이터 클래스와 키워드부터 쓰고 컴파일한다.
6. 도감 › 플레이어 데이터에 판(`run`)·프로필(`profile`)을 만들고 저장(Ctrl+S)해서 `GameIds.cs`가 생기게 한다.
7. Bootstrap을 쓴다: 세션 옵션(`Configuring`)과 시작 뒤 할 일(`Started`).
8. 플레이해서 모니터로 확인한다.

순서가 중요한 곳은 5 → 6 → 7이다. `GameIds`는 도감 항목으로 만들어지고, 도감은 컴파일된 클래스만 고를 수 있다. 그래서 "클래스 → 도감 항목 → 생성 코드 → 그 상수를 쓰는 코드" 순서로 간다.

## 3. 판단 기준

**선택 모듈을 켤까**

| 이럴 때 | 이렇게 |
|---|---|
| 아이템을 칸·개수로 들고 다닌다(가방·물약 칸) | 인벤토리 시스템을 켠다. 플레이어 데이터의 값 형식에 `Inventory`가 나온다. |
| 대사·컷신·상점 주인 대화가 있다 | 다이얼로그 시스템을 켠다. |
| 둘 다 없다 | 켜지 않는다. 폴더(`Optional/…`)를 통째로 지워도 된다. |

**asmdef를 어떻게 나눌까**

| 어셈블리 | 들어가는 것 | 참조 |
|---|---|---|
| 게임 로직 | 데이터 클래스·키워드·이벤트·행동·Block·`[BindTo]` 함수·규칙 도우미·Bootstrap·런 스크립트 | `GBible.Core`, `GBible.Unity`, 켠 모듈 |
| 화면 | UI·연출·사운드 재생처럼 값을 읽어 보여 주기만 하는 코드 | 게임 로직 + `GBible.Core`, `GBible.Unity`(+ 쓰는 모듈) |
| 에디터 | 메뉴·검사 도구 같은 에디터 확장 | 게임 로직 + `GBible.Editor`, Editor 플랫폼 전용 |

- 화면은 게임 로직을 참조하고, 게임 로직은 화면을 모른다. 이렇게 두면 "화면이 값을 바꾸는" 실수가 구조로 막히고, 나중에 게임 로직만 따로 시험하기도 쉽다.
- 플레이어 데이터 클래스가 게임 로직 asmdef에 있으면 `GameIds.cs`는 그 asmdef 폴더의 `Generated/`에 생긴다. 화면 asmdef는 게임 로직을 참조하므로 `GameIds`와 `Game.Data()`를 그대로 쓴다.
- asmdef 이름을 `Unity`나 `GBible.`로 시작하게 짓지 않고, `Assets/Plugins` 아래에 두지 않는다. 도감은 그런 어셈블리를 "내 코드"가 아닌 것으로 보고 클래스 목록에서 뺀다.
- asmdef를 나누지 않고 `Assembly-CSharp`에 두어도 동작한다(자동 참조). 이때 `GameIds.cs`는 `Assets/MomoiProjectGenerated/`에 생긴다.

**Undo 방식과 시드**

| 이럴 때 | `SessionOptions` |
|---|---|
| 턴제·카드·퍼즐: 행동 하나씩 되돌린다 | `History = HistoryMode.Actions`, 오래 플레이하면 `HistoryLimit`로 상한 |
| 실시간 액션: 일정 간격으로 되감는다 | `History = HistoryMode.Snapshots` + `SnapshotInterval`·`SnapshotLimit` |
| 되돌리기가 없다 | `History = HistoryMode.None`(기록 비용 없음) |
| 버그를 같은 순서로 재현해야 한다 | 개발 빌드에서만 `Seed`를 고정한다 |

## 4. 카드 던전에서 실제로

### 4-1. 설치

1. 저장소를 `Assets/Code/System`에 둔다(위치는 어디든 되지만, 게임 코드는 이 폴더 안에 두지 않는다).
2. Unity 6으로 열고 Package Manager에서 `com.unity.nuget.mono-cecil`이 있는지 본다. 이 패키지가 있어야 컴파일할 때 플레이어 데이터 비교 코드가 생긴다. 없으면 느린 예전 방식으로 돌고 기록에 "예전 방식(리플렉션)으로 비교합니다" 경고가 남는다.
3. `MomoiProject → 도감`을 한 번 연다. 항목이 하나라도 생기면 `Assets/MomoiProjectData/Resources/MomoiProject/GameDatabase.asset`이 만들어진다. 이것이 없으면 런타임은 도감 데이터 없이 빈 세션으로 시작한다.

### 4-2. 선택 모듈 켜기

카드 던전은 물약을 3칸 가방에 들고 다니고(인벤토리), 상점 주인과 대화한다(다이얼로그). 그래서 둘 다 켠다.

1. 도감 사이드바 아래쪽 "인벤토리 시스템" 그룹의 첫 줄 **기능 활성화**를 누른다.
2. 확인 창 "기능 켜기"에서 **켜기**. Unity가 다시 컴파일한다.
3. "다이얼로그 시스템"도 같은 방법으로 켠다.

켜짐 상태는 `ProjectSettings/MomoiProject/Features.json`에 남아 팀이 같이 쓴다. 모듈을 쓰는 게임 코드는 모듈을 꺼도 컴파일이 깨지지 않게 `#if GBIBLE_DIALOGUE`·`#if GBIBLE_INVENTORY`로 감쌀 수 있다. 카드 던전은 판 데이터 안에 인벤토리를 두므로(아래 4-4) 인벤토리를 늘 켜 두는 전제로 감싸지 않는다.

### 4-3. 폴더와 asmdef

```
Assets/Code/
  System/                 ← Momoi Project(손대지 않는다)
  CardDungeon/            ← 게임 로직: CardDungeon.asmdef
    Data/  Events/  Actions/  Cards/  Relics/  Run/
    Generated/            ← GameIds.cs (도감이 만든다)
  CardDungeon.View/       ← 화면: CardDungeon.View.asmdef
  CardDungeon.Editor/     ← 에디터: CardDungeon.Editor.asmdef (Editor 플랫폼만)
```

`Assets/Code` 아래에 두는 이유가 있다. 도감의 클래스·이벤트 탭은 `Assets/Code` 아래의 실제 폴더별로 클래스를 묶어 보여 준다. 폴더 이름이 곧 기획자가 보는 분류가 되므로 `Data`·`Events`처럼 뜻이 드러나게 짓는다.

게임 로직 asmdef는 이 정도면 된다.

```json
{
    "name": "CardDungeon",
    "references": [ "GBible.Core", "GBible.Unity", "GBible.Inventory" ]
}
```

화면 asmdef는 여기에 `CardDungeon`을, 상점 대화를 화면에서 튼다면 `GBible.Dialogue`를 더한다. 에디터 asmdef는 Include Platforms를 Editor만 고르고 `CardDungeon`·`GBible.Editor`를 참조한다.

### 4-4. 데이터 클래스부터

도감이 고를 수 있도록 플레이어 데이터가 될 클래스를 먼저 쓴다. 어떤 클래스를 둘지는 [02장](02-DesignToCode.md)에서 정하고, 여기서는 판 데이터와 프로필 데이터 두 덩어리만 보자. 특별한 상속이나 래퍼 없이 `[Serializable]`만 붙인 평범한 클래스다.

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

(발췌다. 실제 클래스에는 가진 유물 목록도 있다.) 판 데이터에는 ID와 게임 중에 바뀌는 값만 둔다. 덱의 카드 한 장(`DeckCard`)은 카드 원본의 ID와 강화 여부만 들고, 이름·비용·그림은 도감의 원본에서 읽는다. 이 기준은 [02장](02-DesignToCode.md)에서 자세히 본다. `Inventory`는 인벤토리 시스템의 형식이라 4-2에서 모듈을 켜 둔 것이 여기서 쓰인다. 저장하고 컴파일이 끝나면 다음으로 간다.

### 4-5. 도감에 플레이어 데이터 만들기 → `GameIds`

1. 도감 사이드바 **선언** 그룹의 **플레이어 데이터** 탭 → **+ 새로 만들기**.
2. ID `run`으로 만들고 상세에서 `zone`(구역)을 `Session`, `valueType`(값 형식)을 `RunState`로 고른다. 클래스 값이라 `defaultValue`는 비워 둔다(클래스의 필드 초기값으로 시작한다).
3. 같은 방법으로 `profile`을 만든다: `zone` = `Meta`, `valueType` = `Profile`.
4. **Ctrl+S**로 저장한다.

저장하면 `GameIds.cs`가 자동으로 다시 만들어진다(수동으로는 `MomoiProject → 식별자 코드 다시 만들기`, 콘솔에 "[MomoiProject] 식별자 코드를 만들었습니다: 경로"). 값 형식이 `CardDungeon` asmdef 안에 있으므로 파일은 `Assets/Code/CardDungeon/Generated/GameIds.cs`에 생긴다. 이 파일은 직접 고치지 않는다.

생긴 파일에는 형식이 붙은 키와 이름으로 읽는 확장 메서드가 함께 들어 있다. 그래서 게임 코드는 이렇게 쓴다.

```csharp
var run = Game.Data().Run;   // RunState 객체(저장소가 관리하는 실제 객체)
run.gold += 10;              // 필드는 그냥 고친다
```

구역은 저장 파일과 같다. `Session`은 이번 판(`run.json`), `Meta`는 판이 바뀌어도 남는 프로필(`profile.json`)이다. 무엇을 어느 쪽에 둘지는 [02장](02-DesignToCode.md) 3절에서 정한다.

### 4-6. Bootstrap — 세션 옵션과 시작 코드

게임이 켜질 때 끼어드는 코드는 한 파일에 모은다. 끼어드는 지점은 둘이다.

- `GBibleRuntime.Configuring` — 세션을 만들기 **직전**. `SessionOptions`를 받아 Undo 방식·시드 같은 옵션을 바꾼다. 세션은 아직 없다.
- `GBibleRuntime.Started` — 세션 준비가 **끝난 뒤**(도감 데이터 등록·프로필 불러오기까지). 디버그 명령, 코드로만 정의하는 값을 등록한다.

`GBibleRuntime`은 첫 씬을 불러오기 전에 세션을 만든다. 그래서 두 이벤트는 그보다 먼저인 `AfterAssembliesLoaded` 시점에 연결해야 한다. `MonoBehaviour.Awake`에서 연결하면 이미 늦다.

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
static void Hook()
{
    GBibleRuntime.Configuring += Configure;
    GBibleRuntime.Started += Started;
}

static void Configure(SessionOptions options)
{
    options.History = HistoryMode.Actions;   // 턴제: 행동 하나씩 Undo
    options.HistoryLimit = 200;              // 오래 플레이해도 Undo 기록이 끝없이 늘지 않게
    if (Debug.isDebugBuild) options.Seed = 20261003;   // 개발 빌드는 시드 고정(버그 재현)
}
```

카드 던전은 카드 한 장을 잘못 냈을 때 되돌릴 수 있어야 하므로 행동 단위 Undo를 쓴다. 행동마다 바뀐 값의 이전 모습을 보관하므로 `HistoryLimit`로 상한을 둔다. 시드는 개발 빌드에서만 고정한다. 에디터 플레이는 개발 빌드로 취급되므로 에디터에서는 늘 같은 시드로 시작하고, 출시 빌드는 런타임 기본값(지금 시각)을 쓴다. 같은 시드에 같은 행동이면 `Game.Random`의 결과(덱 섞기·보상 골드)가 같으므로 버그를 같은 순서로 재현할 수 있다.

`Started`에서는 두 가지를 한다. 첫째, 도감에 플레이어 데이터가 없을 때만 코드로 정의해 둔다. 도감 에셋 없이 돌리는 테스트·서버에서도 같은 코드가 돌게 하기 위해서다.

```csharp
if (!session.Data.IsDefined(GameIds.PlayerData.Run.Id))
    session.Data.Define(GameIds.PlayerData.Run.Id, DataZone.Session, new RunState(), "판");
if (!session.Data.IsDefined(GameIds.PlayerData.Profile.Id))
    session.Data.Define(GameIds.PlayerData.Profile.Id, DataZone.Meta, new Profile(), "프로필");
```

둘째, 개발용 명령을 모니터 **명령** 탭에 버튼으로 더한다. 값을 고치는 명령도 `DeveloperEditAction`으로 감싸 행동 하나로 실행하면, 게임 행동과 똑같이 기록되고 Undo된다.

```csharp
if (Debug.isDebugBuild)
    session.Debug.Register("카드 던전", "골드 +100", () => session.Do(new DeveloperEditAction
    {
        label = "골드 +100",
        Edit = () => session.Game.Data().Run.gold += 100,
    }));
```

카드 던전의 Bootstrap에는 "적 모두 처치" 명령도 있는데, 이것은 피해 규칙(`Rules.Damage`)을 쓰므로 규칙을 만든 뒤([02장](02-DesignToCode.md)·[03장](03-GameLoop.md)) 더한다. 처음 값(시작 골드 등)은 `Started`에서 넣지 않는다. 그 값은 판을 시작하는 행동(`StartRun`, 03장)이나 클래스의 필드 초기값이 정한다.

### 4-7. 첫 플레이 확인

플레이 버튼을 누르고 다음을 본다.

1. 하이어라키의 DontDestroyOnLoad 아래에 `[MomoiProject]`와 `[오디오]` 오브젝트가 있다. 세션은 씬과 상관없이 이 오브젝트와 함께 산다.
2. `MomoiProject → 모니터`를 연다. 머리 줄이 "시드 20261003 · …"으로 시작하면 `Configuring`이 적용된 것이다.
3. **데이터** 탭의 **판 데이터**에 `run`, **프로필 데이터**에 `profile`이 보인다. 들어가 보면 `RunState`의 필드(`floor`·`gold`·`hero`·`deck` …)가 클래스 초기값으로 있다.
4. **명령** 탭에 "카드 던전" 분류와 **골드 +100**이 있다. 누르면 **행동** 탭에 "[개발] 골드 +100"이 생기고, 데이터 탭의 `run › gold`가 100 오른다. 머리의 **Undo**를 누르면 0으로 돌아간다.
5. 콘솔에 "[MomoiProject]"로 시작하는 경고가 없다.

여기까지 되면 시스템과 게임 코드가 연결된 것이다. 아직 판을 여는 행동이 없으므로 `run`은 빈 값 그대로다. 다음 장에서 기획을 클래스로 옮긴다.

## 5. 다른 장르라면

> **다른 장르라면** — 실시간 액션 게임은 `Configuring`에서 `options.History = HistoryMode.Snapshots`로 바꾸고 `SnapshotInterval`(초)·`SnapshotLimit`(개수)로 되감기 범위를 정한다. 게임 코드는 어느 방식이든 `session.Undo()` 하나로 되돌린다. 장르별 설정은 [07장](07-GenreRecipes.md).

> **다른 장르라면** — 이어 하기가 없는 로그라이크라면 판 자동 저장을 끈다. 게임이 끝날 때 진행 중인 판을 저장하는 `GBibleRuntime.AutoSaveRun`은 기본이 `true`다. Bootstrap의 `Hook`에서 `GBibleRuntime.AutoSaveRun = false;` 한 줄이면 된다. 저장 정책 전체는 [10장](10-SaveAndRelease.md).

> **다른 장르라면** — 대사가 없는 퍼즐 게임은 다이얼로그 시스템을 켜지 않는다. 꺼진 모듈의 코드는 빌드에 들어가지 않는다.

## 6. 체크리스트

- [ ] 저장소가 `Assets` 아래에 있고, 게임 코드는 그 폴더 밖(`Assets/Code/<게임>/`)에 있다.
- [ ] `com.unity.nuget.mono-cecil` 패키지가 있다.
- [ ] 도감을 한 번 열었고 `GameDatabase.asset`이 생겼다.
- [ ] 쓸 선택 모듈을 켰다(`Features.json`이 바뀌었다).
- [ ] 게임 로직·화면·에디터 asmdef가 있고, 화면 → 게임 로직 방향으로만 참조한다. 이름이 `Unity`·`GBible.`로 시작하지 않는다.
- [ ] 판·프로필 플레이어 데이터를 도감에 만들고 저장했다. `GameIds.cs`가 게임 로직 폴더의 `Generated/`에 있다.
- [ ] Bootstrap이 `AfterAssembliesLoaded`에서 `Configuring`·`Started`를 연결한다.
- [ ] 모니터 머리에 고정 시드가 보이고, 디버그 명령 → Undo가 된다.
- [ ] 콘솔에 "[MomoiProject]" 경고가 없다.

## 7. 더 보기

- [20. 시작하기](../Manual/20-Setup.md) — 설치, 자동 시작 순서, `SessionOptions`의 모든 칸, 선택 기능, `GameIds` 규칙, 어셈블리
- [01. 플레이어 데이터](../Manual/01-PlayerData.md) — 도감에서 만들기, `Game.Data()`, `Define`
- [21. 도감](../Manual/21-Editor-Database.md) — 사이드바, 저장 흐름(작업본)
- [22. 모니터와 디버깅](../Manual/22-Monitor-Debug.md) — 탭 구성, 명령
- [02. 행동과 Undo](../Manual/02-Actions-Undo.md) — Undo 방식
