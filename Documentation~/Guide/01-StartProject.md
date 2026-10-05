# 01. 프로젝트 시작하기

패키지를 넣고 **시작 설정**을 적용하면 준비는 알아서 된다. 이 장에서는 그다음 판·프로필 데이터와 Bootstrap을 채운다. 예시 게임은 카드 던전(턴제 덱빌딩 로그라이크)이다.

> [!note]- 이 장에서 쓰는 말
> | 말 | 뜻 |
> |---|---|
> | 패키지(UPM) | Unity Package Manager가 관리하는 코드 묶음. GBible은 `Packages/com.momoi.gbible`에 들어가는 **embedded 패키지**(프로젝트 안에 복사본이 있어 바로 고칠 수 있는 패키지)다. |
> | 시작 설정 | 메뉴 `MomoiProject → 시작 설정` 창. 새 프로젝트에서 처음 한 번 저절로 열린다. |
> | DB | 메뉴 `MomoiProject → DB` 창. 데이터 항목(플레이어 데이터·인스턴스·도전과제 …)과 프로젝트 설정을 만들고 고치는 곳. |
> | 모니터 | 메뉴 `MomoiProject → 모니터` 창. 플레이 중인 세션의 값·행동·기록을 보고 조작한다. |
> | 세션(`GameSession`) | 게임 한 판에 필요한 모든 시스템(플레이어 데이터·이벤트·난수·기록·행동)을 묶은 객체. 게임이 켜질 때 `GBibleRuntime`이 하나 만든다. |
> | 플레이어 데이터 | 게임 중에 바뀌는 값(골드·덱·체력 …)을 담는 칸. 값 형식으로 기본 값(`int` …)이나 내가 쓴 `[Serializable]` 클래스를 고른다. |
> | `GameIds` | DB의 ID를 C# 상수와 접근자(`Game.Data().Run` …)로 만든 생성 파일. |
> | asmdef | Unity의 Assembly Definition. 폴더 하나를 별도 어셈블리로 묶고 참조를 적는다. 시작 설정이 만들어 주므로 직접 쓸 일은 없다. |

---

## 1. 설치와 시작 설정

1. GBible 폴더를 프로젝트의 `Packages/com.momoi.gbible`에 넣고 Unity 6으로 연다.
2. 저절로 열리는 **시작 설정** 창에서 Product Name·선택 모듈·Undo 방식·언어를 고르고 **적용**한다. 모두 나중에 바꿀 수 있다.

카드 던전은 선택 모듈 둘(인벤토리·다이얼로그)을 켜고, Undo 방식은 행동 단위(턴제)를 고른다.

> [!note]- 설치 자세히
> - `Packages` 폴더는 `Assets`와 나란히 있는 폴더다. 복사하거나 그 자리에 clone한다.
> - Unity가 패키지의 `package.json`을 읽고, 함께 필요한 `com.unity.nuget.mono-cecil`을 저절로 설치한다. 이 패키지가 있어야 플레이어 데이터 비교 코드가 생긴다. 없으면 콘솔에 "예전 방식(리플렉션)으로 비교합니다"가 뜬다.
> - Package Manager의 In Project 목록에 **GBible**이 보이면 된다.
> - GBible 코드를 고치는 방법은 예전과 같다. `Packages/com.momoi.gbible` 안의 파일을 고치면 바로 다시 컴파일된다. 게임 코드는 이 폴더에 두지 않는다.

> [!note]- 시작 설정이 만드는 것
> ```
> Assets/
>   Code/
>     Rules/    MomoiProject.Rules.asmdef   PlayerData.cs   ← RunState·Profile 클래스
>     Client/   MomoiProject.Client.asmdef  Bootstrap.cs    ← 시작 코드
>     Editor/   MomoiProject.Editor.asmdef
>     Tests/    MomoiProject.Tests.asmdef
>   MomoiProjectData/
>     Resources/MomoiProject/GameDatabase.asset          ← DB 데이터·프로젝트 설정(런타임이 읽는다)
>     Resources/MomoiProject/LocalizationSettings.asset  ← 다국어 설정
>     Texts/Texts_ko.asset …                             ← 언어별 텍스트 표
>     (플레이어 데이터 폴더)/run.asset  profile.asset      ← DB › 플레이어 데이터 run·profile
> ProjectSettings/MomoiProject/Features.json              ← 켠 모듈(팀이 같이 쓴다)
> CLAUDE.md                                               ← Claude Code가 늘 읽는 규칙(관례·참조 문서를 불러온다)
> .claude/skills/think-momoi/  .claude/hooks/compile-check.ps1  .claude/settings.json  ← 스킬 /think-momoi, 응답을 끝낼 때 컴파일 검사
> Docs/PlanningSpec.md                                    ← 기획 명세서 양식(기획자가 채운다)
> ```
> 다시 열어 다시 적용해도 안전하다. 이미 있는 파일·항목은 덮어쓰지 않고 없는 것만 만든다.

> [!note]- Claude Code 파일과 AI 문서
> Claude Code 파일은 패키지의 AI 문서(`Documentation~/AI/`)를 가리킨다. 문서는 프로젝트로 복사하지 않고 패키지에서 읽는다.
>
> | 문서 | 하는 일 |
> |---|---|
> | `ProjectConventions.md`(관례) · `Reference.md`(참조) | CLAUDE.md가 늘 불러온다. 규칙과 기능 목록 |
> | `GimmickCatalog.md`(기믹 목록) | 기믹(조각의 효과 규칙) 갈래 → GBible 부품. 범위는 네 게임(발라트로·와일드프로스트·매지크래프트·세피리아)의 다양성이고, 장르는 자유다 |
> | `PlanningSpecGuide.md` | 기획 명세서를 읽는 절차. 스킬 `/think-momoi`가 기믹 목록과 함께 읽는다 |
> | `Docs/PlanningSpec.md`(기획 명세서) | 기획자가 채운다. 2-3 자리(조각이 놓이는 곳), 4절 메인 룰과 기믹, 6절 부가 기능 |
>
> - **메인 룰**: 놓인 조각이 언제, 어떤 순서로 발동하나. 명세서 4-1에 문장으로 적는다.
> - **메인 룰 구조**(4-2): 표시한 것(빌려 쓰기·놓인 순서·반복 발동 …)은 AI가 메인 룰을 만들 때 자리를 미리 만든다.
> - **기믹 갈래**(4-3): 표시한 갈래는 기믹 목록 2절의 부품으로 만든다.
> - **부가 기능**(6절): 대화·달력·현실 시간·칸 맵·큰 수처럼 기믹이 아닌 장르 부품. 표시하면 GBible 기능을 켜서 붙인다([07장](07-GenreRecipes.md) 1절).

> [!note]- 나중에 바꾸기
> | 바꿀 것 | 어디서 |
> |---|---|
> | Product Name | 시작 설정 또는 Player Settings |
> | 선택 모듈 | DB 사이드바의 모듈 그룹 첫 줄 **기능 활성화**(또는 시작 설정). asmdef 참조는 저절로 따라 바뀐다 |
> | Undo 방식 | DB › 시작 파일 › **Bootstrap**(아래 4절) |
> | 언어 | 시작 설정 또는 다국어 설정 |
>
> 처음부터 다시 하고 싶으면 메뉴 `MomoiProject → 처음 상태로 되돌리기…`. 깨끗한 프로젝트를 처음 열 때 남긴 기록으로 되돌리고, 개발자가 더하고 바꾼 것은 모두 `MomoiProjectBackups/`에 옮겨 둔다(매뉴얼 [20장](../Manual/20-Setup.md) 10절).

> [!note]- 선택 모듈을 켤까
> | 이럴 때 | 이렇게 |
> |---|---|
> | 아이템을 칸·개수로 들고 다닌다(가방·물약 칸) | 인벤토리 시스템을 켠다. 값 형식에 `Inventory`가 나온다 |
> | 대사·컷신·상점 주인 대화가 있다 | 다이얼로그 시스템을 켠다 |
> | 둘 다 없다 | 켜지 않는다. 꺼진 모듈의 코드는 빌드에 들어가지 않는다 |
>
> 모듈을 쓰는 게임 코드는 `#if GBIBLE_INVENTORY`·`#if GBIBLE_DIALOGUE`로 감쌀 수 있다. 모듈을 켜고 끄면 이 심볼도 따라 바뀐다.

## 2. 코드 폴더

`Assets/Code` 아래 네 폴더로 나뉜다.

- **Rules**: 게임 규칙. 데이터 클래스·행동·이벤트·Block·`[BindTo]` 함수 같은 일반 C# 클래스.
- **Client**: Unity에 붙는 코드. `MonoBehaviour`·UI·연출, 그리고 Bootstrap.
- **Editor**·**Tests**: 에디터 확장과 규칙 테스트. 거의 안 쓴다.

Rules에서 Client의 코드(예: `BattleHud`)를 부르면 컴파일 오류가 난다. 규칙이 화면에 기대지 않게 막는 장치다.

> [!note]- 왜 나누나
> - **규칙은 화면을 모른다.** 나중에 규칙만 떼어 서버로 옮길 때([11장](11-MultiplayerReady.md)) 화면 코드가 딸려 오지 않는다.
> - **화면은 규칙을 안다.** Client는 Rules를 참조하므로 `Game.Data().Run`·이벤트·행동을 그대로 쓴다. 화면이 데이터 필드를 직접 고치는 것까지 막지는 못하므로 "값은 행동으로만 바꾼다"는 습관으로 지킨다([09장](09-Habits.md)).
> - **테스트가 규칙을 부를 수 있다.** 테스트 asmdef는 기본 어셈블리(`Assembly-CSharp`)를 참조할 수 없어서, 규칙이 따로 묶여 있어야 한다.
> - **컴파일이 덜 느려진다.** UI만 고치면 Rules는 다시 컴파일되지 않는다.
> - Rules는 지금 엔진 참조가 열려 있어서 `Sprite` 같은 Unity 형식을 쓰는 것은 막지 않는다. 규칙에는 엔진 형식 대신 ID를 둔다([02장](02-DesignToCode.md)).

> [!note]- asmdef 참조(열어 볼 일은 없다)
> | 어셈블리 | 참조 |
> |---|---|
> | `MomoiProject.Rules` | `GBible.Core`, `GBible.Unity`, 켠 모듈 중 데이터 형식을 내놓는 것(`GBible.Inventory`) |
> | `MomoiProject.Client` | Rules, `GBible.Core`, `GBible.Unity`, 켠 모듈 전부 |
> | `MomoiProject.Editor` | 위 둘 + `GBible.Editor`. Editor 플랫폼 전용 |
> | `MomoiProject.Tests` | Rules, `GBible.Core`, `GBible.Unity`, 켠 데이터 모듈, Test Runner, NUnit. 테스트 실행 때만 |

> [!note]- 하위 폴더
> Rules·Client 아래 하위 폴더는 자유다. DB의 클래스·이벤트 탭은 `Assets/Code` 아래 실제 폴더별로 묶어 보여 준다(그룹 "Rules/Data" = 폴더 `Assets/Code/Rules/Data`). 카드 던전은 이렇게 나눴다.
> ```
> Assets/Code/
>   Rules/   Data/  Events/  Actions/  Cards/  Relics/  Run/  Generated/(GameIds.cs)
>   Client/  UI/  Presentation/  Bootstrap.cs  RunDirector.cs
> ```

## 3. 판·프로필 데이터

DB › 플레이어 데이터 탭에 두 항목이 기본으로 있다.

- `run`: 한 판 동안의 값. 판이 끝나면 버린다. 클래스는 `RunState`.
- `profile`: 판이 끝나도 남는 값(해금·최고 기록). 클래스는 `Profile`.

두 클래스는 `Rules/PlayerData.cs`에 빈 채로 있다. DB 사이드바 맨 위 **시작 파일 › RunState / Profile**을 누르면 바로 열린다. 필드를 채운다.

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

코드에서는 이렇게 불러온다.

```csharp
using GBible.Ids;

var run = Game.Data().Run;   // RunState 객체(저장소가 관리하는 실제 객체)
run.gold += 10;              // 필드는 그냥 고친다(Undo·저장·멀티에 기록된다)
```

> [!note]- RunState·Profile은 무엇인가
> GBible이 주는 타입이 아니라 시작 설정이 이름만 잡아 둔 **프로젝트의 평범한 클래스**다. 상속이나 래퍼 없이 `[Serializable]`만 붙는다. 필드 초기값(`new Hero()`, 빈 리스트, `capacity = 3`)이 새 판의 시작 상태가 된다.
> - 판 데이터에는 ID와 게임 중에 바뀌는 값만 둔다. 카드 이름·비용·그림은 DB의 원본에서 읽는다([02장](02-DesignToCode.md)).
> - 파일 위치는 자유다. 카드 던전은 `Hero`·`DeckCard`·`Battle`과 함께 두려고 `Rules/Data/Model.cs`로 옮겼다. 바로가기는 DB 항목이 가리키는 클래스를 따라가므로 옮겨도 열린다.
> - 클래스 이름을 바꾸면 DB › 플레이어 데이터에서 그 항목의 값 형식을 다시 고른다.
> - 판·프로필 말고 따로 둘 플레이어 데이터가 생기면 DB › 플레이어 데이터 › **+ 새로 만들기**로 더한다(ID·구역·값 형식).

> [!note]- 게임이 켜지면 무슨 일이 일어나나
> ```
> ① DB 읽기 → "run" = new RunState() (Session), "profile" = new Profile() (Meta)
> ② 저장 파일 불러오기 → 있으면 위 객체에 값을 채운다
> ③ Started 이벤트를 쏜다 → Bootstrap(4절)
> ```
> 그래서 코드에서 따로 정의(`session.Data.Define`)할 필요가 없다. `Define`은 DB 없이 세션을 손으로 만드는 테스트([08장](08-TestAndDebug.md))에서나 쓴다. DB로 정의하면 `GameIds` 접근자, ID 이름 변경 이력, 저장 호환 검사가 따라온다.

> [!note]- `Game.Data().Run`이라는 이름
> DB를 저장하면(Ctrl+S) `Assets/Code/Rules/Generated/GameIds.cs`가 다시 만들어진다(직접 고치지 않는다). ID를 `.`·`_`·`-`에서 잘라 각 조각의 첫 글자를 대문자로 붙인 이름이 된다: `run` → `Run`, `best_floor` → `BestFloor`. 다른 이름을 원하면 DB에서 ID를 바꾼다.
>
> 구역은 저장 파일과 같다. `Session`은 이번 판(`run.json`), `Meta`는 프로필(`profile.json`). 무엇을 어느 쪽에 둘지는 [02장](02-DesignToCode.md) 3절.

## 4. Bootstrap

Bootstrap은 게임이 켜질 때 가장 먼저 도는 곳이다. 설정과 코드 두 부분이다.

**설정**: DB 사이드바 맨 위 **시작 파일 › Bootstrap**. 고치면 바로 저장된다(Ctrl+S 필요 없음).

| 칸 | 카드 던전 | 뜻 |
|---|---|---|
| Undo 방식 | 행동 단위 | 턴제는 행동 단위, 실시간은 스냅샷 |
| Undo 보관 수 | 200 | 최대 몇 번 되돌릴 수 있나(0 = 제한 없음) |
| 개발 빌드에서 시드 고정 | 켬, 20261003 | 같은 순서로 하면 같은 결과(버그 재현) |
| 판 저장 | 행동마다 저장 | 이어 하기용 판 파일을 언제 쓰나([10장](10-SaveAndRelease.md)) |
| 판 끝 이벤트 | `RunEnded` | 받으면 판 저장을 지우고 프로필을 바로 저장 |

**코드**: 화면 위 **cs 열기** → `Client/Bootstrap.cs`. `Configure`와 `Started` 두 함수가 있는데, 거의 `Started`만 쓴다.

| `Started`에서 하는 일 | 예 |
|---|---|
| 프로젝트 고유 기능의 감시 연결 | 유물 감시를 거는 한 줄 `RelicRules.Register(session, …)`. 등록 내용은 Rules에 있다([04장](04-AddingContent.md) 8절) |
| 행동 처리기 등록 | `session.Actions.Register<SkipFloor>(…)` |
| 이벤트 받기(분석·외부 서비스 알림 등) | `session.Events.Subscribe<BattleWon>(…)` |

카드 던전의 `Started`는 한 줄이다.

```csharp
static void Started(GameSession session)
{
    // 유물 감시(04장). 규칙은 Rules의 RelicRules, 여기서는 연결만
    RelicRules.Register(session, GameDatabase.Load()?.All<RelicData>());
}
```

**개발 명령**(모니터 **명령** 탭의 버튼)은 코드가 아니라 DB › 개발 › **개발 명령**에서 만든다. 항목 하나가 버튼 하나이고, 결과 칸에 결과 Block을 조립한다.

| 칸 | 카드 던전 `goldPlus100` |
|---|---|
| 버튼 이름 | 골드 +100 |
| 결과 | 플레이어 데이터에 주기 → `run › gold`, 증감 100 |

> [!note]- 개발 명령 자세히
> - 누르면 결과 전체가 행동 하나(`DeveloperEditAction`)로 실행된다. 게임 행동처럼 **타임라인**에 "[개발] 골드 +100"으로 남고 Undo된다.
> - 칸: 버튼 이름(비우면 ID), 분류(명령 탭에서 묶이는 이름, 기본 "개발"), 설명, 결과.
> - 결과 Block: 플레이어 데이터에 주기 · 이벤트 쏘기 · 로그에 기록 · 대상에게 효과 · 코딩한 결과. **이벤트 쏘기**는 이벤트를 고르고 값 줄(필드 경로 = 값)과 횟수를 적는다. 그 이벤트를 받는 로그·도전과제·트리거가 반응한다(카드 던전 "한 턴 30 피해": `TurnEnded` turn = 1, damage = 30).
> - 규칙 함수를 거쳐야 하는 명령은 항목을 만들고 그 ID에 `[BindTo]`를 붙인다. 카드 던전 "적 모두 처치"는 피해 규칙을 거쳐야 처치 이벤트·로그가 게임과 같게 돈다.
> ```csharp
> [BindTo(GameIds.DebugCommand.KillAll)]
> static void KillAll(TriggerContext c)
> {
>     var game = c.Game;
>     foreach (var enemy in Rules.LivingEnemies(game).ToArray()) Rules.Damage(game, game.Data().Run.hero, enemy, 9999);
> }
> ```
> - 출시 빌드에서는 개발 명령이 등록되지 않는다(`session.Debug.Enabled`가 거짓). 그래서 `if (Debug.isDebugBuild)`로 감싸지 않는다.
> - 코드로 직접 버튼을 거는 `session.Debug.Register(분류, 이름, 동작)`도 그대로 있다. 출시 빌드에서 무시되는 것도 같다.

> [!note]- 설정 칸 자세히
> - Undo 방식을 "스냅샷"으로 바꾸면 보관 수 대신 스냅샷 간격(초)·스냅샷 보관 수 칸이 나온다. 간격 × 개수 = 되감을 수 있는 시간.
> - 시드는 개발 빌드에서만 고정된다. 에디터 플레이는 개발 빌드로 친다. 출시 빌드는 늘 지금 시각을 시드로 쓴다. 같은 시드에 같은 행동이면 `Game.Random`의 결과(덱 섞기·보상 골드)가 같다.
> - 판 저장·판 끝 이벤트는 [10장](10-SaveAndRelease.md)에서 자세히 본다. 프로필은 이 설정과 상관없이 끌 때 늘 저장된다.
> - **선택 칸**: **Undo 끊는 이벤트**(그 이벤트를 쏜 행동이 끝나면 Undo 기록을 비운다. 예: 턴 끝, [07장](07-GenreRecipes.md) 2절), **시작할 때 이어 하기** + **새 판 행동**(타이틀 화면 없이 바로 판으로 들어가는 게임, [10장](10-SaveAndRelease.md) 3절). 비워 두면 아무 일도 하지 않는다.
> - **고급**(접혀 있음): 기록 보관 줄 수, 개발 빌드의 Modify 누락 검사. 보통 그대로 둔다.
> - 이 값들은 게임이 켜질 때 `SessionOptions`에 먼저 들어간다. 코드(`Configure`)가 같은 값을 바꾸면 코드가 이기고, 모니터 **타임라인**에 "코드(Configuring)가 DB › Bootstrap의 세션 옵션을 바꿨습니다: …"가 남는다.

> [!note]- `Configure`는 언제 쓰나
> 화면에서 고를 수 있는 값은 화면에서 정한다. 코드가 필요한 경우는 둘뿐이다.
> - 출시 뒤 저장 구조를 바꿨을 때, 값을 계산해 고치는 변환(`options.Migrations.Add`, [10장](10-SaveAndRelease.md) 7절). 필드 옮김·키 이름 바꿈·키 삭제는 코드 없이 DB › 개발 › **저장 변환** 표에 적는다. 저장 버전은 등록한 변환을 따라 저절로 올라간다.
> - 조건에 따라 바꿀 때. 예: "데모 빌드는 Undo를 끈다".
> ```csharp
> static void Configure(SessionOptions options)
> {
> #if DEMO_BUILD
>     options.History = HistoryMode.None;   // 화면 값보다 이 줄이 이긴다
> #endif
> }
> ```

> [!note]- 연결 시점과 위치
> - 두 함수는 `[RuntimeInitializeOnLoadMethod(AfterAssembliesLoaded)]`인 `Hook`에서 `GBibleRuntime.Configuring`·`GBibleRuntime.Started`에 연결되어 있다(시작 설정이 만든 파일 그대로). `GBibleRuntime`은 첫 씬을 불러오기 전에 세션을 만들므로, `MonoBehaviour.Awake`에서 연결하면 늦다.
> - `Configure`는 세션을 만들기 **직전**(세션 없음), `Started`는 준비가 **끝난 뒤**(DB 등록·저장 불러오기까지)다.
> - Bootstrap이 Client에 있는 이유: Unity의 게임 시작에 붙는 코드라서. 서버라면 서버가 세션을 만드는 자리에서 같은 일을 한다.
> - 프로젝트 고유 기능은 나눠 둔다. **등록 함수**(무엇을 감시하고 무엇을 할지)는 Rules에(서버에서도 그대로 부른다), **Unity 시작에 거는 연결**은 `Started`에 한 줄. 카드 던전의 유물은 `RelicRules.Register`(Rules) + 위 한 줄(Bootstrap)이다. `GameDatabase`를 쓰려면 파일 위에 `using GBible.Data;`.
> - `Started`에서 하지 않는 것: 플레이어 데이터 정의(DB가 했다), 처음 값 넣기(판을 시작하는 행동 `StartRun`이나 클래스 초기값이 정한다, [03장](03-GameLoop.md)), 개발 명령(DB › 개발 명령이 게임이 켜질 때 저절로 등록된다).

## 5. 첫 플레이 확인

플레이 버튼을 누르고 본다.

1. `MomoiProject → 모니터` 머리 줄이 "시드 20261003 · …"으로 시작한다.
2. **데이터** 탭에 `run`·`profile`이 있고, 안에 `RunState`의 필드가 클래스 초기값으로 있다.
3. **명령** 탭의 **골드 +100**을 누르면 `run › gold`가 100 오르고, 머리의 **Undo**로 0으로 돌아간다.
4. 콘솔에 "[MomoiProject]" 경고가 없다.

> [!note]- 더 확인할 것
> - 하이어라키의 DontDestroyOnLoad 아래에 `[MomoiProject]`·`[오디오]` 오브젝트가 있다. 세션은 씬과 상관없이 이 오브젝트와 함께 산다.
> - **타임라인** 탭에 "[개발] 골드 +100"이 생긴다.
> - 아직 판을 여는 행동이 없으므로 `run`은 빈 값 그대로다. 다음 장에서 기획을 클래스로 옮긴다.

> [!note]- 다른 장르라면
> - **실시간 액션**: Bootstrap 화면에서 Undo 방식 "스냅샷", 간격·보관 수로 되감기 범위를 정한다. 게임 코드는 어느 방식이든 `session.Undo()` 하나([07장](07-GenreRecipes.md)).
> - **이어 하기가 없는 로그라이크**: 판 저장 "저장하지 않음"([10장](10-SaveAndRelease.md)).
> - **대사가 없는 퍼즐**: 다이얼로그 시스템을 켜지 않는다.

## 6. 체크리스트

- [ ] 시작 설정을 적용했다. `Assets/Code/Rules·Client·Editor·Tests`, DB › 플레이어 데이터 `run`·`profile`이 있다.
- [ ] 쓸 선택 모듈을 켰다.
- [ ] 규칙 코드는 Rules에, Unity에 붙는 코드는 Client에 있다.
- [ ] `RunState`·`Profile`에 필드를 채웠다.
- [ ] DB › 시작 파일 › Bootstrap에서 Undo 방식·보관 수·시드·판 저장을 정했다.
- [ ] DB › 개발 › 개발 명령에 명령을 만들었고, 모니터에 고정 시드가 보이고, 그 명령 → Undo가 된다.

> [!note]- 더 보기(매뉴얼)
> - [20. 시작하기](../Manual/20-Setup.md) — 자동 시작 순서, `SessionOptions`, 선택 기능, `GameIds` 규칙
> - [01. 플레이어 데이터](../Manual/01-PlayerData.md) — DB에서 만들기, `Game.Data()`, `Define`
> - [21. DB](../Manual/21-Editor-Database.md) — 사이드바, 저장 흐름(작업본)
> - [22. 모니터와 디버깅](../Manual/22-Monitor-Debug.md) — 탭 구성, 명령
