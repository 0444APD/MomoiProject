# 01. 프로젝트 시작하기

개발자가 패키지를 넣고 **시작 설정**을 적용하면 폴더·asmdef·시작 파일·DB·Claude Code 파일이 저절로 생긴다. 이 장에서는 그다음 AI가 판·프로필 데이터와 Bootstrap을 채운다. 예시 게임은 카드 던전(턴제 덱빌딩 로그라이크)이다.

> [!note]- 이 장에서 쓰는 말
> | 말 | 뜻 |
> |---|---|
> | 시작 설정 | 메뉴 `MomoiProject › 개발 › 시작 설정` 창. 새 프로젝트를 처음 열 때 한 번 저절로 열리고, 개발자가 적용한다. |
> | DB | 메뉴 `MomoiProject › DB` 창. 데이터 항목(플레이어 데이터·인스턴스·도전과제 …)과 프로젝트 설정. AI는 반영 스크립트로 항목을 만들고, 개발자는 여기서 읽고 값을 바꾼다(로직 쪽 항목의 추가·삭제·Block 조립은 잠겨 있다 — `EditorPolicy`). |
> | 모니터 | 메뉴 `MomoiProject › 모니터` 창. 플레이 중인 세션의 값·행동·기록을 보고 조작하는 개발자 도구. |
> | 세션(`GameSession`) | 한 판에 필요한 모든 시스템(플레이어 데이터·이벤트·난수·기록·행동)을 묶은 객체. 게임이 켜질 때 `GBibleRuntime`이 하나 만든다. |
> | 플레이어 데이터 | 게임 중에 바뀌는 값(골드·덱·체력 …)을 담는 칸. 값 형식은 기본 값(`int` …)이나 `[Serializable]` 클래스. |
> | `GameIds` | DB의 ID를 C# 상수와 접근자(`Game.Data().Run` …)로 만든 생성 파일(네임스페이스 `GBible.Ids`). 손대지 않는다. |
> | asmdef | Unity의 Assembly Definition. 시작 설정이 만들고, 켠 모듈에 따라 참조를 저절로 맞춘다. |

---

## 1. 설치와 시작 설정

1. (개발자) GBible 폴더를 프로젝트의 `Packages/com.momoi.gbible`에 넣고 Unity 6으로 연다.
2. (개발자) 저절로 열리는 **시작 설정** 창에서 Product Name·언어만 고르고 **적용**한다. 쓸 기능(선택 모듈)과 Undo 방식은 고르지 않는다 — AI가 단계 0의 기획 질문으로 정해 단계 1에서 넣는다(기능은 `Features.json` — 진행 매뉴얼 6절 박자 0, Undo 방식은 DB › Bootstrap — 관례 15절, 개발자 2026-10-17).
3. (개발자) 자료를 `Docs/Source`에 넣고 Claude Code에서 `/think-momoi`로 시작한다. 여기서부터 AI가 진행 매뉴얼의 단계대로 한다.

카드 던전은 선택 모듈 둘(인벤토리·다이얼로그)을 켜고, Undo 방식은 행동 단위(턴제)를 고른다.

> [!note]- 설치 자세히
> - Unity가 `package.json`을 읽고 `com.unity.nuget.mono-cecil`을 함께 설치한다. 이것이 있어야 플레이어 데이터 비교 코드가 생긴다. 없으면 "'…'은(는) 예전 방식(리플렉션)으로 비교합니다" 경고가 남는다.
> - [필수] AI는 GBible 라이브러리 코드를 고치지 않는다(관례 14절). 범용 기능이 모자라면 사람에게 알린다. 게임 코드는 이 폴더에 두지 않는다.

> [!note]- 시작 설정이 만드는 것
> ```
> Assets/
>   MomoiProject/                                         ← 시스템·AI가 쓰는 곳
>     Code/
>       Rules/    MomoiProject.Rules.asmdef   PlayerData.cs   ← RunState·Profile 클래스
>       Client/   MomoiProject.Client.asmdef  Bootstrap.cs    ← 시작 코드
>       Editor/   MomoiProject.Editor.asmdef  EditorAssembly.cs(자리 표시 — 스크립트가 생기면 지워도 된다)
>       Tests/    MomoiProject.Tests.asmdef   TestsAssembly.cs(자리 표시)
>     Data/
>       Resources/MomoiProject/GameDatabase.asset          ← DB 데이터·프로젝트 설정(런타임이 읽는다)
>       Resources/MomoiProject/LocalizationSettings.asset  ← 다국어 설정
>       Texts/Texts_ko.asset …                             ← 언어별 텍스트 표
>       PlayerData/run.asset  profile.asset                ← DB › 플레이어 데이터 run·profile
>     link.xml                                             ← 코드 줄이기가 게임 어셈블리를 지우지 않게
>   Dev/                                                  ← 개발자 폴더(안의 폴더 = DB 'Dev' 탭). Unity 기본 Assets/Scenes는 Dev/Scenes로 옮겨진다
> ProjectSettings/MomoiProject/Features.json              ← 켠 모듈(팀이 같이 쓴다)
> CLAUDE.md                                               ← Claude Code가 늘 읽는 규칙(메뉴얼 지도·관례를 불러온다)
> .claude/skills/think-momoi/  .claude/hooks/compile-check.ps1  .claude/settings.json  ← 스킬 /think-momoi, 응답을 끝낼 때 컴파일 검사
> Docs/PlanningSpec.md  Docs/Decisions.md                 ← 기획 명세서 빈 양식(AI가 자료로 채우고 묻는다) · 결정 기록
> ```
> 다시 적용해도 안전하다. 이미 있는 파일·항목은 덮어쓰지 않고 없는 것만 만든다.

> [!note]- Claude Code 파일과 AI 문서
> 문서는 프로젝트로 복사하지 않고 패키지(`Documentation~/AI/`)에서 읽는다. `CLAUDE.md`가 메뉴얼 지도(`ManualMap.md`)와 관례를 늘 불러오고, 지도가 작업마다 읽을 문서를 고른다(코드 작업을 시작할 때 참조 `Reference.md`, 명세서는 `PlanningSpecGuide.md`, 기믹은 `GimmickCatalog.md` …). 에디터를 열 때마다 `CLAUDE.md`·스킬·훅이 패키지에 맞춰진다.

> [!note]- 나중에 바꾸기
> | 바꿀 것 | 어디서 |
> |---|---|
> | Product Name | 시작 설정 또는 Player Settings |
> | 선택 모듈 | AI: `ProjectSettings/MomoiProject/Features.json`의 `enabled`에 ID(`dialogue`·`inventory`·`input`·`steam`)를 더한다. 개발자: DB 사이드바 모듈 그룹의 **기능 활성화**(시작 설정에는 없다). 어느 쪽이든 다음 컴파일 때 스크립팅 심볼·asmdef 참조가 따라 맞는다 |
> | Undo 방식 | DB › 시작 파일 › **Bootstrap**(4절) |
> | 언어 | 시작 설정 또는 다국어 설정 |
>
> 처음부터 다시 하려면 메뉴 `MomoiProject › 개발 › 처음 상태로 되돌리기…`. 개발자가 더하고 바꾼 것은 모두 `MomoiProjectBackups/`로 옮겨 둔다(매뉴얼 [20장](../../Manual/20-Setup.md) 10절).

> [!note]- 선택 모듈을 켤까
> | 이럴 때 | 이렇게 |
> |---|---|
> | 아이템을 슬롯·개수로 들고 다닌다(인벤토리·물약 슬롯) | 인벤토리(`inventory`). 값 형식에 `Inventory`가 나온다 |
> | 대사·컷신·상점 주인 대화가 있다 | 다이얼로그(`dialogue`) |
> | 둘 다 없다 | 켜지 않는다. 꺼진 모듈의 코드는 빌드에 들어가지 않는다 |
>
> - 켜는 기준은 명세서 6절(부가 기능)이다. 켠 모듈의 형식을 쓰는 코드는 Unity가 한 번 다시 컴파일한 뒤에 쓴다(그 전에는 컴파일 검사가 그 어셈블리를 찾지 못한다).
> - 입력·Steam은 외부 패키지(Input System·Steamworks.NET)가 있어야 켜진다. 패키지를 들이는 것은 내려받기라 먼저 묻는다(관례 0절).
> - 모듈을 쓰는 게임 코드는 `#if GBIBLE_INVENTORY`·`#if GBIBLE_DIALOGUE`로 감쌀 수 있다. 모듈을 켜고 끄면 심볼도 따라 바뀐다.

## 2. 코드 폴더

`Assets/MomoiProject/Code` 아래 네 폴더로 나뉜다(관례 1절).

- **Rules**: 게임 규칙 — 데이터 클래스·행동·이벤트·Block·`[BindTo]` 함수. [필수] 화면 API(`MonoBehaviour`·씬·입력·`Time`·`UnityEngine.Random`)를 쓰지 않는다. 서버·테스트에서도 같은 코드가 돈다.
- **Client**: Unity에 붙는 코드 — 화면(`Client/Screens`)·입력·연출, 그리고 Bootstrap.
- **Editor**: DB 반영 스크립트(`Editor/Seeds/<주제>Seeds.cs`)와 에디터 도구.
- **Tests**: 규칙 테스트(Rules만 참조). 규칙을 만들거나 고치면 하나 이상 둔다(관례 12절, [08장](08-TestAndDebug.md)).

Rules에서 Client의 코드(예: `BattleHud`)를 부르면 컴파일 오류가 난다. 규칙이 화면에 기대지 않게 막는 장치다.

> [!note]- 왜 나누나
> - **규칙은 화면을 모르고, 화면은 규칙을 안다.** 규칙만 떼어 서버·테스트로 옮길 수 있다([11장](11-MultiplayerReady.md) — 테스트 asmdef는 `Assembly-CSharp`를 참조할 수 없어 규칙이 따로 묶여 있어야 한다). 화면이 데이터 필드를 직접 고치는 것까지는 컴파일러가 막지 못하므로 "값은 행동으로만 바꾼다"로 지킨다([09장](09-Habits.md)).
> - **Rules asmdef는 엔진 참조가 열려 있다**(`GBible.Unity` 참조). 그래서 원본 클래스·DB 종류의 `Sprite icon` 같은 에셋 칸은 쓸 수 있다. 막히지 않을 뿐 규칙 코드에서 Unity API를 부르지는 않고, 판 데이터에는 에셋 대신 ID를 둔다([02장](02-DesignToCode.md) 9절).

> [!note]- asmdef 참조(열어 볼 일은 없다)
> | 어셈블리 | 참조 |
> |---|---|
> | `MomoiProject.Rules` | `GBible.Core`, `GBible.Unity`, 켠 모듈 중 데이터 형식을 내놓는 것(`GBible.Inventory`) |
> | `MomoiProject.Client` | Rules, `GBible.Core`, `GBible.Unity`, 켠 모듈 전부 |
> | `MomoiProject.Editor` | 위 둘 + `GBible.Editor`(+ 켠 모듈의 에디터 어셈블리). Editor 플랫폼 전용 |
> | `MomoiProject.Tests` | Rules, `GBible.Core`, `GBible.Unity`, 켠 데이터 모듈, Test Runner, NUnit. 테스트 실행 때만 |

> [!note]- 하위 폴더
> Rules·Client 아래 하위 폴더는 자유다. 파일 하나에 주제 하나(관례 1절). DB의 클래스·이벤트 탭은 실제 폴더별로 묶어 보여 준다(그룹 "Rules/Data" = 폴더 `Assets/MomoiProject/Code/Rules/Data`). 카드 던전은 이렇게 나눴다.
> ```
> Assets/MomoiProject/Code/
>   Rules/   Data/  Events/  Actions/  Cards/  Relics/  Run/  Generated/(GameIds.cs)
>   Client/  Screens/  Presentation/  Bootstrap.cs
>   Editor/  Seeds/(CardSeeds.cs · RelicSeeds.cs · BalanceSeeds.cs · DebugCommandSeeds.cs · SetupSeeds.cs)
>   Tests/   RunFlowTests.cs …
> ```

## 3. 판·프로필 데이터

DB › 플레이어 데이터에 두 항목이 기본으로 있다.

- `run`(세션 구역): 한 판 동안의 값. 판이 끝나면 버린다. 클래스는 `RunState`.
- `profile`(메타 구역): 판이 끝나도 남는 값(해금·최고 기록). 클래스는 `Profile`.

두 클래스는 `Assets/MomoiProject/Code/Rules/PlayerData.cs`에 빈 채로 있다(개발자는 DB 사이드바의 바로가기로 연다). 필드를 채운다.

```csharp
[Serializable]
public class RunState
{
    public int floor;
    public int gold;
    public string scene;                                  // 지금 씬 — 씬 흐름이 연다(03장 9절)
    public Hero hero = new Hero();
    public List<DeckCard> deck = new List<DeckCard>();    // 카드 원본 ID + 이 장만의 바뀌는 값
    public Battle battle;                                 // 전투 밖이면 null
    public Inventory potions = new Inventory();           // 슬롯 수(capacity)는 StartRun이 밸런싱 값에서 넣는다

    public bool IsActive => floor > 0;                    // 진행 중인 판이 있는지
}

[Serializable]
public class Profile
{
    public int totalGold;
    public int bestFloor;
    [DataRef(typeof(Card))] public List<string> unlocked = new List<string>();   // 해금한 카드 원본 ID(Card는 02장 2절)
}
```

코드에서는 이렇게 읽고 고친다. [필수] **고치는 것은 행동의 `Run` 안에서만**이다([03장](03-GameLoop.md)).

```csharp
using GBible.Ids;

var run = Game.Data().Run;   // RunState 객체(저장소가 관리하는 실제 객체)
run.gold += 10;              // 행동 안이면 그냥 고친다 — 행동이 끝날 때 Sync가 기록해 Undo·저장·멀티가 따라온다
```

> [!note]- RunState·Profile은 무엇인가
> GBible이 주는 타입이 아니라 시작 설정이 이름만 잡아 둔 **프로젝트의 평범한 클래스**다. 상속이나 래퍼 없이 `[Serializable]`만 붙는다. 필드 초기값(`new Hero()`, 빈 리스트)이 새 판의 시작 상태가 된다. [필수] 개발자가 바꿀 시작 숫자(체력·골드·슬롯 수)는 초기값에 적지 않고 판을 여는 행동(`StartRun`)이 밸런싱 값에서 넣는다(관례 18절, [03장](03-GameLoop.md) 4절).
> - 판 데이터에는 ID와 게임 중에 바뀌는 값만 둔다. 카드 이름·비용·그림은 DB의 원본에서 읽는다([02장](02-DesignToCode.md)).
> - 파일 위치는 자유다. 카드 던전은 `Hero`·`DeckCard`·`Battle`과 함께 두려고 `Rules/Data/Model.cs`로 옮겼다. 시작 설정은 같은 클래스가 이미 있으면 `PlayerData.cs`를 다시 만들지 않는다.
> - 클래스 이름을 바꾸면 DB › 플레이어 데이터에서 그 항목의 값 형식도 바꾼다.
> - 판·프로필 말고 따로 둘 플레이어 데이터가 생기면 DB › 플레이어 데이터에 항목(ID·구역·값 형식)을 더한다.

> [!note]- 게임이 켜지면 무슨 일이 일어나나
> ```
> ① DB 읽기 → "run" = new RunState() (Session), "profile" = new Profile() (Meta), 그 밖의 DB 정의(밸런싱 값·상태 효과·패시브 효과·도전과제 …)
> ② 프로필(profile.json) 불러오기 → 있으면 위 객체에 값을 채운다
> ③ SessionCreated → Bootstrap.RegisterRules(4절) — 모든 세션
> ④ 기본 개발 명령·씬 흐름·화면 준비 → Started → Bootstrap.Started(4절) — 화면 세션만
> ⑤ '시작할 때 이어 하기'면 판(run.json) 불러오기, 없으면 새 판 행동
> ```
> 그래서 게임 코드에서 따로 정의(`session.Data.Define`)하지 않는다. `Define`은 DB 없이 세션을 손으로 만드는 규칙 테스트([08장](08-TestAndDebug.md))에서 쓴다. DB로 정의하면 `GameIds` 접근자, ID 이름 변경 이력, 저장 호환 검사가 따라온다.

> [!note]- `Game.Data().Run`이라는 이름과 GameIds가 생기는 때
> - `Assets/MomoiProject/Code/Rules/Generated/GameIds.cs`는 DB가 바뀌어 적용될 때(개발자의 Ctrl+S, 반영 스크립트의 새 항목 자동 적용) 다시 만들어진다. [필수] 직접 고치지 않는다.
> - 새 상수는 **그 뒤 Unity가 다시 컴파일해야** 코드에서 보인다. 그래서 반영 스크립트와 그 상수를 쓰는 코드를 한꺼번에 넣지 않고 한 응답 안에서 박자로 나눈다: 형식·반영 스크립트를 넣고 → `Logs/MomoiProject/RunTests.request`로 Unity에 새로 읽기를 부르고 → `GameIds.cs`에 상수가 생긴 것을 본 뒤 이어 쓴다. 선택 기능을 켜는 박자 · 새로 읽기를 한 번 더 도는 때는 진행 매뉴얼 6절 박자 표(Unity가 닫혀 있으면 첫 박자에서 멈추고 열어 달라고 한다).
> - 이름은 ID의 첫 글자를 대문자로, 점으로 나뉜 조각을 이어 붙인다: `run` → `Run`, `bestFloor` → `BestFloor`, `boss.dragon` → `BossDragon`. 다른 이름을 원하면 ID를 바꾼다(05장 8절).
> - 구역은 저장 파일과 같다. `Session`은 이번 판(`run.json`), `Meta`는 프로필(`profile.json`). 무엇을 어느 쪽에 둘지는 [02장](02-DesignToCode.md) 3절.

## 4. Bootstrap

Bootstrap은 게임이 켜질 때 가장 먼저 도는 곳이다. 설정과 코드 두 부분이다.

**설정**: DB › 시작 파일 › **Bootstrap**. DB 항목이 아니라 DB 에셋에 바로 저장되는 값이다(작업본·Ctrl+S를 거치지 않는다).

| 칸 | 카드 던전 | 뜻 |
|---|---|---|
| Undo 방식 | 행동 단위(턴제) | 턴제는 행동 단위, 실시간은 스냅샷 |
| Undo 보관 수 | 200 | 최대 몇 번 되돌릴 수 있나(0 = 제한 없음) |
| 개발 빌드에서 시드 고정 | 켬, 20261003 | 같은 순서로 하면 같은 결과(버그 재현) |
| 판 저장 | 행동마다 저장(턴제) | 이어 하기용 판 파일을 언제 쓰나([10장](10-SaveAndRelease.md)) |
| 판 끝 이벤트 | `RunEnded` | 받으면 판 저장을 지우고 프로필을 바로 저장 |

AI는 이 값을 반영 스크립트에서 정한다. DB 에셋을 꺼내 `EditorSet…` 함수로 **결정한 값**(결정 기록·장르 추천)을 넣는다. [필수] 이 값은 '적용 전'을 거치지 않으므로 **지금 값이 기본값(`ScriptableObject.CreateInstance<GameDatabase>()`의 값)일 때만 넣는다** — 기본값이 아니면 개발자가 고른 것(시작 설정 · DB 화면)이라 넣지 않고, 결정 기록과 다르면 보고에 적어 묻는다. 그래야 메뉴 `MomoiProject › 개발 › DB 반영 스크립트 모두 다시 돌리기`도 개발자 값을 덮지 않는다(관례 15절).

```csharp
[DbSeed("1")]
static void Bootstrap()
{
    var database = AssetDatabase.LoadAssetAtPath<GameDatabase>(EditorDatabase.DatabaseAssetPath);
    if (database == null) return;
    var defaults = ScriptableObject.CreateInstance<GameDatabase>();               // 기본값과 같을 때만 넣는다(개발자가 고른 값은 그대로)
    if (database.RunSave == defaults.RunSave && database.RunEndEvent == defaults.RunEndEvent)
        database.EditorSetSaveOptions(RunSaveMode.AfterEachAction, typeof(RunEnded).FullName, profileEachAction: false);
    else Debug.Log("[MomoiProject] Bootstrap 판 저장은 개발자가 고른 값 그대로 둡니다(결정 기록과 다르면 보고).");
    Object.DestroyImmediate(defaults);
    EditorUtility.SetDirty(database);
    AssetDatabase.SaveAssetIfDirty(database);
}
```

> [!note]- 설정 함수와 칸 자세히
> - 함수: `EditorSetHistory(Undo 방식, 보관 수 = 그대로)`(Undo 방식 하나만 — 나머지 칸은 그대로) · `EditorSetSessionOptions(Undo 방식, 보관 수, 스냅샷 간격, 스냅샷 보관 수, 시드 고정, 시드)`(여섯 칸을 함께 — 바꾸지 않을 칸은 지금 값 `database.SnapshotInterval`·`DevelopmentSeed` …를 넘긴다) · `EditorSetSaveOptions(판 저장, 판 끝 이벤트, 프로필도 행동마다)` · `EditorSetSaveTiming(N초, 저장 이벤트)` · `EditorSetFlowOptions(Undo 끊는 이벤트, 시작할 때 이어 하기, 새 판 행동)` · `EditorSetTickRate(초당 틱 수)` · `EditorSetSlotsShareProfile(bool)`(세이브 슬롯이 프로필을 함께 씀 — 매뉴얼 31장 2-1) · `EditorSetSceneFlow(키, 필드, 빈 값일 때 씬)`. 이벤트·행동은 형식 전체 이름(`typeof(X).FullName`)이다. 지금 값은 `GameDatabase`의 읽기 속성(`History`·`HistoryLimit`·`RunSave`·`SaveProfileEachAction`·`SlotsShareProfile`·`TickRate` … — 참조 3-8)으로 읽는다.
> - Undo 방식을 "스냅샷"으로 두면 보관 수 대신 스냅샷 간격(초)·스냅샷 보관 수를 쓴다. 간격 × 개수 = 되감을 수 있는 시간.
> - 시드는 개발 빌드에서만 고정된다. 에디터 플레이는 개발 빌드로 친다. 출시 빌드는 늘 지금 시각을 시드로 쓴다. 같은 시드에 같은 행동이면 `Game.Random`의 결과가 같다.
> - 판 저장·판 끝 이벤트는 [10장](10-SaveAndRelease.md). 프로필은 이 설정과 상관없이 끌 때 늘 저장된다.
> - **선택 칸**: **Undo 끊는 이벤트**(그 이벤트를 쏜 행동이 끝나면 Undo 기록을 비운다 — 예: 턴 끝, [07장](07-GenreRecipes.md) 2절), **시작할 때 이어 하기** + **새 판 행동**(타이틀 화면 없이 바로 판으로 들어가는 게임, [03장](03-GameLoop.md) 3절), **초당 틱 수**(실시간), **씬 흐름**(03장 9절). 비워 두면 아무 일도 하지 않는다.
> - **고급**(접혀 있음): 기록 보관 줄 수, 개발 빌드의 Modify 누락 검사. 보통 그대로 둔다.
> - 이 값들은 게임이 켜질 때 `SessionOptions`에 먼저 들어간다. 코드(`Configure`)가 같은 값을 바꾸면 코드가 이기고, 모니터 **타임라인**에 "코드(Configuring)가 DB › Bootstrap의 세션 옵션을 바꿨습니다: …"가 남는다.
> - **Run In Background**(창이 뒤로 가도 게임이 돈다)는 Bootstrap 칸이 아니라 Player Settings다. 실시간·방치형·멀티는 같은 반영 스크립트에서 `PlayerSettings.runInBackground = true;`. 틱 수·저장 간격처럼 AI가 정해 추천 승인 목록에 올린다(진행 매뉴얼 3절).

**코드**: `Assets/MomoiProject/Code/Client/Bootstrap.cs`. 시작 설정이 만든 세 함수가 있다.

| 함수 | 연결된 알림 | 오는 때 |
|---|---|---|
| `Configure` | `GBibleRuntime.Configuring` | 세션을 만들기 직전(세션은 아직 없다). 거의 쓰지 않는다(아래 접힌 칸) |
| `RegisterRules` | `GBibleRuntime.SessionCreated` | **모든 세션**(화면 세션 · 멀티 서버 · 시험 모드의 서버와 봇 · 매칭 방 · 서버 빌드)이 DB로 만들어진 직후. DB 정의 등록·프로필 불러오기 뒤, 새 판 행동보다 먼저 |
| `Started` | `GBibleRuntime.Started` | **화면 세션에만**, 준비가 끝난 뒤(`RegisterRules` 다음). `GBibleRuntime.CreateSession()`으로 따로 만든 세션(시험 모드의 서버·봇, 매칭 방)에는 오지 않는다 |

[필수] 기준은 하나다: **규칙 쪽 등록은 `RegisterRules`, 화면 쪽 연결은 `Started`.** 규칙 등록을 `Started`에 두면 멀티 서버 세션에서 그 규칙이 빠진다.

| `RegisterRules`에서 하는 일(규칙 쪽) | 예 |
|---|---|
| 프로젝트 고유 기능(패시브 효과로 안 되어 직접 만든 DB 종류)의 감시 연결 | `RelicRules.Register(session, …)` 한 줄. 등록 내용은 Rules에 있다([04장](04-AddingContent.md) 8절) |
| 행동 처리기 등록 | `session.Actions.Register<SkipFloor>(…)`([12장](12-Appendix.md) 3-1) |
| 규칙이 받는 이벤트 · 틱마다의 규칙 | `session.Events.Subscribe<T>(…)` · `session.Ticked += …`(값은 그 안에서 행동으로) |
| 코드로 쓰는 개발 명령 중 데이터·규칙을 바꾸는 것 | `session.Debug.Register(…)`([08장](08-TestAndDebug.md) 2절) |
| 코드로만 정의하는 플레이어 데이터 | `session.Data.Define(…)`(DB에 둘 수 있으면 DB로) |

| `Started`에서 하는 일(화면 쪽) | 예 |
|---|---|
| 화면 세션 전체에 걸 연출 받기 | `session.Events.SubscribeAfterAction<T>(e => GameView.Present(…))`(화면 하나에 딸린 연출은 그 화면 코드의 `OnOpen`, [06장](06-Presentation.md) 3절) |
| 화면·기기 설정·입력 연결 | `GameScreens.Reroll = …` · `GameSettings.Changed += …` · `GameInput.Use(…)`([10장](10-SaveAndRelease.md) 1절) |
| 화면 세션에서 한 번 할 일 | 화면만 쓰는 시간 배속 명령 · 판을 불러온 뒤 할 일의 연결(`GBibleRuntime.RunLoaded` — 정적 알림이라 `-=` 뒤 `+=`). 켤 때 정산(`Settle` — 게임이 만든 행동)은 여기서 바로 하지 않는다: 판은 `Started` 다음에 불러오므로(시작할 때 이어 하기) 이어 하기는 `RunLoaded`에서, 새 판은 새 판 행동 안에서 정산한다(매뉴얼 07장 4.7) |

카드 던전은 유물을 패시브 효과로 만들고(라이브러리가 DB에서 등록한다, [04장](04-AddingContent.md) 8절) 연출을 화면 코드에 두므로 세 함수가 모두 비어 있다. 패시브 효과로 안 되는 DB 종류를 직접 만들면 그 감시 연결을 `RegisterRules`에 한 줄 둔다.

```csharp
static void RegisterRules(GameSession session)
{
    // 직접 만든 DB 종류의 감시(04장 8절의 예). 규칙은 Rules의 RelicRules, 여기서는 연결만
    RelicRules.Register(session, GameDatabase.Load()?.All<RelicData>());
}
```

이미 시작 설정을 한 프로젝트의 `Bootstrap.cs`에 `RegisterRules`가 없으면 매뉴얼 [20장](../../Manual/20-Setup.md) 4절대로 더한다.

> [!note]- `Configure`는 언제 쓰나
> DB에서 정할 수 있는 값은 DB(Bootstrap)에서 정한다. 코드가 필요한 경우는 보통 둘이다.
> - 출시 뒤 저장 구조를 바꿨을 때, 값을 계산해 고치는 변환(`options.Migrations.Add`, [10장](10-SaveAndRelease.md) 7절). 필드 옮김·키 이름 바꿈·키 삭제는 코드 없이 DB › 개발 › **저장 변환** 표에 적는다. 저장 버전은 등록한 변환을 따라 저절로 올라간다.
> - 조건에 따라 바꿀 때. 예: "데모 빌드는 Undo를 끈다".
> ```csharp
> static void Configure(SessionOptions options)
> {
> #if DEMO_BUILD
>     options.History = HistoryMode.None;   // DB 값보다 이 줄이 이긴다
> #endif
> }
> ```

> [!note]- 연결 시점과 위치
> - 세 함수는 `[RuntimeInitializeOnLoadMethod(AfterAssembliesLoaded)]`인 `Hook`에서 `GBibleRuntime.Configuring`·`SessionCreated`·`Started`에 연결되어 있다(시작 설정이 만든 파일 그대로). `GBibleRuntime`은 첫 씬을 불러오기 전에 세션을 만들므로 `MonoBehaviour.Awake`에서 연결하면 늦다. 기능마다 따로 `Hook`을 두지 않고 이 세 함수 안에 더한다.
> - 순서(화면 세션): `Configure` → 세션 만들기(DB 정의 등록·프로필 불러오기) → `RegisterRules` → 기본 개발 명령·씬 흐름·화면 준비 → `Started` → (시작할 때 이어 하기면) 판 불러오기 또는 새 판 행동. `GBibleRuntime.CreateSession()`으로 따로 만든 세션(시험 모드의 서버·봇, 매칭 방)은 `RegisterRules`까지만 오고, 서버면 그 뒤 새 판 행동이 돈다. 방장·서버 빌드(`StartDedicated`)는 화면 세션을 서버로 열므로 `Started`도 온다. 세이브 슬롯을 바꾸거나(`UseSlot`) 멀티에 들어가면 화면 세션이 새로 만들어져 `RegisterRules`·`Started`가 다시 온다 — 세션이 아닌 정적 알림(`GameSettings.Changed`)에 걸 때는 두 번 걸리지 않게 `-=` 뒤 `+=`.
> - `RegisterRules`에서는 받은 `session`만 쓴다. 이때 `GBibleRuntime.Session`·`GBibleRuntime.Game`은 아직 그 세션이 아니다(화면 세션이면 아직 null, 따로 만든 서버 세션이면 화면 세션).
> - [필수] 같은 세션에 두 번 등록하면 두 번 발동한다. 멀티의 `GameNet.ServerStarted`·`RoomStarting`에서 등록 함수를 다시 부르지 않는다(거기서는 서버 객체가 필요한 서버 규칙만 — [11장](11-MultiplayerReady.md) 2절).
> - Bootstrap은 Unity의 게임 시작에 붙는 코드라 Client에 있다. 그래서 등록 내용(무엇을 감시하고 무엇을 할지)은 Rules에 두고 여기서는 한 줄로 부른다 — 규칙 테스트는 손으로 만든 세션에 같은 함수를 부른다. `GameDatabase`를 쓰려면 `using GBible.Data;`.
> - `RegisterRules`·`Started` 어디에서도 하지 않는 것: DB에 있는 플레이어 데이터 정의(DB가 했다), 처음 값 넣기(판을 여는 행동 `StartRun`이나 클래스 초기값, [03장](03-GameLoop.md)), DB › 개발 › 개발 명령의 항목 등록(저절로 된다), 패시브 효과·상태 효과·액티브 효과 등록(DB 항목이라 저절로).

**개발 명령**(모니터 **명령** 탭의 버튼 = 테스트 명령, 관례 19절)은 코드가 아니라 DB › 개발 › **개발 명령**(`DebugCommandData`) 항목이다. 항목 하나가 버튼 하나이고, 반영 스크립트로 만든다. 칸은 `label`(버튼 이름, 비우면 ID) · `category`(분류, 명령 탭의 묶음) · `description` · `outcomes`(결과 Block).

| 칸 | 카드 던전 `goldPlus100` |
|---|---|
| `label` | 골드 +100 |
| `category` | 판 |
| `outcomes` | 플레이어 데이터에 주기 → `run › gold`, 증감 100 |

> [!note]- 개발 명령 자세히
> - 누르면 결과 전체가 행동 하나(`DeveloperEditAction`)로 실행된다. 게임 행동처럼 **타임라인**에 "[개발] 골드 +100"으로 남고 Undo된다.
> - 결과 Block: 플레이어 데이터에 주기 · 이벤트 쏘기 · 로그에 기록 · 대상에게 효과 · 코딩한 결과 …(결과 전부). **이벤트 쏘기**는 이벤트를 고르고 값 줄(필드 경로 = 값)과 횟수를 적는다. 그 이벤트를 받는 로그·도전과제·트리거가 반응한다(카드 던전 "한 턴 30 피해": `TurnEnded` turn = 1, damage = 30).
> - 규칙 함수를 거쳐야 하는 명령은 항목을 만들고 그 ID에 `[BindTo]`를 붙인다(결과가 비어도 된다). 카드 던전 "적 모두 처치"는 피해 규칙을 거쳐야 처치 이벤트·로그가 게임과 같게 돈다.
> ```csharp
> [BindTo(GameIds.DebugCommand.KillAll)]
> static void KillAll(TriggerContext c)
> {
>     var game = c.Game;
>     foreach (var enemy in Rules.LivingEnemies(game).ToArray()) Rules.Damage(game, game.Data().Run.hero, enemy, 9999);
> }
> ```
> - 출시 빌드에서는 등록되지 않는다(`session.Debug.Enabled`가 거짓). 그래서 `if (Debug.isDebugBuild)`로 감싸지 않는다. 코드로 직접 버튼을 거는 `session.Debug.Register(분류, 이름, 동작)`도 같다.
> - 테스트 씬(`TestSceneSetup.Ensure`)·테스트 상황은 관례 19절, [08장](08-TestAndDebug.md).

## 5. 첫 확인

AI는 Unity 창을 볼 수 없으므로 파일로 확인한다(관례 12절).

1. 응답을 끝낼 때 컴파일 검사 훅이 돈다. 오류가 없다.
2. `Logs/MomoiProject/DbStatus.txt`가 "상태: 검증 통과"다. 반영 스크립트의 결과·`[MomoiProject]` 경고는 `Logs/Editor.log`.
3. 규칙 테스트를 `Logs/MomoiProject/RunTests.request`로 돌려 `Logs/MomoiProject/TestResults.txt`를 읽는다. 화면은 `Logs/MomoiProject/Play.request`로 플레이해 본다(매뉴얼 38장 2-4·2-5).

개발자에게는 플레이해서 이렇게 보라고 알린다.

- `MomoiProject › 모니터` 머리 줄이 "시드 20261003 · …"으로 시작한다.
- **데이터** 탭에 `run`·`profile`이 있고, 안에 `RunState`의 필드가 클래스 초기값으로 있다.
- **명령** 탭의 **골드 +100**을 누르면 `run › gold`가 100 오르고 **타임라인**에 "[개발] 골드 +100"이 생긴다. 머리의 **Undo**로 돌아간다.

> [!note]- 더 확인할 것
> - 하이어라키의 DontDestroyOnLoad 아래에 `[MomoiProject]`·`[오디오]` 오브젝트가 있다. 세션은 씬과 상관없이 이 오브젝트와 함께 산다.
> - 아직 판을 여는 행동이 없으므로 `run`은 빈 값 그대로다. 다음 장에서 기획을 클래스로 옮긴다.

> [!note]- 다른 장르라면
> - **실시간 액션**: Undo 방식 "스냅샷", 간격·보관 수로 되감기 범위를 정한다. 게임 코드는 어느 방식이든 `session.Undo()` 하나([07장](07-GenreRecipes.md)).
> - **이어 하기가 없는 로그라이크**: 판 저장 "저장하지 않음"([10장](10-SaveAndRelease.md)).
> - **대사가 없는 퍼즐**: 다이얼로그를 켜지 않는다.

## 6. 체크리스트

- [ ] 시작 설정을 적용했다. `Assets/MomoiProject/Code/Rules·Client·Editor·Tests`, DB › 플레이어 데이터 `run`·`profile`이 있다.
- [ ] 명세서 6절대로 선택 모듈을 켰다(`Features.json`).
- [ ] 규칙 코드는 Rules에, Unity에 붙는 코드는 Client에, DB 항목·Bootstrap 값은 Editor의 반영 스크립트에 있다.
- [ ] `RunState`·`Profile`에 필드를 채웠다.
- [ ] Bootstrap의 Undo 방식·보관 수·시드·판 저장을 정했다(반영 스크립트, 기본값일 때만).
- [ ] `Bootstrap.cs`에 규칙 쪽 등록은 `RegisterRules`, 화면 쪽 연결은 `Started`에 있다.
- [ ] 개발 명령을 만들었고, `DbStatus.txt`가 "검증 통과"이며, 개발자에게 확인할 것(시드·명령 → Undo)을 알렸다.

> [!note]- 더 보기(매뉴얼)
> - [20. 시작하기](../../Manual/20-Setup.md) — 자동 시작 순서, `SessionOptions`, 선택 기능, `GameIds` 규칙, 컴파일 검사 훅(11절)
> - [01. 플레이어 데이터](../../Manual/01-PlayerData.md) — DB에서 만들기, `Game.Data()`, `Define`
> - [21. DB](../../Manual/21-Editor-Database.md) — 사이드바, 저장 흐름(작업본), 잠금(3-1), DB 반영 스크립트
> - [22. 모니터와 디버깅](../../Manual/22-Monitor-Debug.md) — 탭 구성, 명령
> - [38. 테스트](../../Manual/38-Testing.md) — AI의 테스트·플레이 요청(2-4·2-5), 테스트 씬
