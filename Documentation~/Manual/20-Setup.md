# 20. 시작하기

Momoi Project를 Unity 프로젝트에 넣고, 게임이 켜질 때 시스템이 어떻게 준비되는지, 그 준비 과정에 게임 코드를 어떻게 끼워 넣는지를 설명한다. 코드 안의 이름(네임스페이스·어셈블리·클래스)은 `GBible`이고, 메뉴·창에 보이는 이름은 `MomoiProject`다.

## 이 문서에서 쓰는 말

| 말 | 뜻 |
|---|---|
| 세션(`GameSession`) | 게임 한 판에 필요한 모든 시스템(플레이어 데이터·이벤트·난수·기록·행동 …)을 묶은 객체 하나. static 전역 상태를 두지 않으므로 멀티플레이 서버나 테스트는 세션을 여러 개 만들 수 있다. |
| `GameApi` | 게임 코드가 쓰는 창구(`All`·`Find`·`Do`·`Use`·`Random` …). 세션마다 하나(`session.Game`)다. |
| DB | `MomoiProject → DB` 창. 데이터 에셋(`GameData`)을 만들고 고치는 DB 창이다. 자세히는 `21-Editor-Database.md`. |
| 플레이어 데이터 | 게임 중 바뀌는 값(골드·인벤토리·유닛 …)을 담는 칸. DB › 플레이어 데이터에서 ID와 값 형식을 정한다. |
| Undo | 마지막 행동(`Game.Do`로 실행한 것)을 되돌리는 기능. 이름 그대로 쓴다. |

---

## 1. 설치

**한 줄 요약.** 저장소 폴더를 `Assets` 아래 아무 곳에 두면 된다. 외부 에셋(Odin·DOTween)은 필요 없다.

**언제.** 새 프로젝트에 시스템을 들일 때 한 번.

### 에디터에서

1. 저장소를 대상 프로젝트의 `Packages/com.momoi.gbible`에 복사하거나 `git clone`한다(embedded 패키지).
2. Unity 6(6000.6.3f1에서 확인)으로 연다.
3. 패키지 `com.unity.nuget.mono-cecil`이 프로젝트에 있는지 확인한다(Burst·Collections가 함께 가져오는 경우가 많다). 이 패키지가 있어야 컴파일할 때 플레이어 데이터 비교 코드가 만들어진다(아래 8절).
4. 메뉴 `MomoiProject → DB`을 한 번 연다. DB 창이 열리면서 데이터 에셋 목록을 모아 런타임용 목록(`GameDatabase`)을 `Assets/MomoiProject/Data/Resources/MomoiProject/GameDatabase.asset`에 만든다(항목이 하나라도 있을 때).
5. 플레이 버튼을 누른다. 하이어라키의 DontDestroyOnLoad 아래에 `[MomoiProject]`(세션 진행)과 `[오디오]` 오브젝트가 생기면 정상이다.

만들어지는 곳:

| 위치 | 내용 |
|---|---|
| `Assets/MomoiProject/` | 시스템·AI가 쓰는 곳(개발자는 들어가지 않는다): `Code/`(AI가 쓰는 코드 — Rules·Client·Editor·Tests·Keywords, DB 요소 탭이 여기서 읽는다) · `Data/`(아래) · `UI/`(화면 UXML·USS) · `Generated/`. 예전 자리(`Assets/Code`·`Assets/MomoiProjectData`·`Assets/UI`)는 에디터를 열 때·시작 설정 창을 열 때 저절로 옮겨진다(Unity가 읽지 않는 파일만 남은 예전 폴더는 `MomoiProjectBackups/<시각>-merge/`로). |
| `Assets/MomoiProject/Data/` | DB 항목 에셋(종류마다 영어 폴더 — `Ability`·`PlayerData` …, 예전 한글 폴더는 열 때 저절로 옮겨진다), `Resources/MomoiProject/`(GameDatabase·다국어 설정·오디오 설정·다이얼로그 설정), `Texts/`, `DialogueUI/` |
| `Assets/Dev/` | 개발자가 노는 곳. 안에 폴더를 만들면 DB 사이드바 'Dev' 그룹의 탭이 된다(21장 2-1). 에셋 관리의 가져오기·넣을 에셋 자리도 여기(`Dev/Art`·`Dev/Audio` …). 시스템·AI는 요청받을 때만 건드린다. |
| `Assets/MomoiProject/Generated/GameIds.cs` | 식별자 코드(7절). 플레이어 데이터 값 형식이 asmdef 안에 있으면 그 asmdef 폴더의 `Generated/`에 생긴다. |
| `Assets/MomoiProject/link.xml` | 코드 줄이기 보호(시작 설정이 만든다, 8절) |
| `ProjectSettings/MomoiProject/` | 팀이 함께 쓰는 에디터 설정(`Features.json`, `Ids.json`, `CodexFolders.json`, `ProjectCode.json`, `Layouts.json` …) |

### 주의할 점

- 패키지 폴더 안에는 게임 코드를 두지 않는다. DB의 요소 탭(클래스·이벤트)은 `Assets/MomoiProject/Code` 안의 스크립트만 보인다(폴더 경로는 보이지 않는다).
- 런타임이 `GameDatabase`를 찾지 못하면 기록(Journal)에 경고 "GameDatabase를 찾을 수 없습니다. 에디터에서 DB 창을 한 번 열면 자동으로 만들어집니다."가 남고, DB 데이터 없이 빈 세션으로 시작한다.

---

## 2. 런타임 자동 시작 — `GBibleRuntime`

**한 줄 요약.** 플레이를 시작하면 `GBibleRuntime`이 세션을 하나 만들고 DB 데이터를 모두 등록한다. 씬에 아무것도 둘 필요가 없다.

**언제.** 싱글 게임은 항상 이것 하나를 쓴다. 서버·테스트·시뮬레이션은 `new GameSession(...)`으로 직접 만든다.

### 흐름

`[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`로 첫 씬을 불러오기 전에 `GBibleRuntime.Start()`가 불린다. 맨 먼저 설정 파일(`settings.json`)을 읽어 적용 대상(언어·음량·화면 — 화면은 저장된 값이 있을 때만)을 적용하고, 마지막으로 쓴 저장 슬롯을 정한다(`31-Slots-Settings-Input.md`). 그다음 순서는 다음과 같다.

1. `Mirror.Initialize()` — 플레이어 데이터 비교 코드를 모은다. 리플렉션(실행 중에 형식 정보를 읽는 무거운 작업)은 게임이 켜질 때 여기서 한 번만 한다.
2. `[BindTo]` 코드 결과를 모으고, 모양이 맞지 않는 함수는 콘솔에 경고한다.
3. `GameDatabase.Load()`로 DB 에셋을 읽고, `SessionOptions`를 만들어 DB › 시작 파일 › Bootstrap 화면의 세션 옵션과 DB › 개발 › 저장 변환 표(버전 순서, 같은 버전은 ID 글자 순서)를 넣은 뒤 `Configuring` 이벤트를 쏜다(3절). 코드가 화면 값을 바꾸면 기록에 남긴다.
4. `new GameSession(options)`. 이때부터 경고·오류 기록을 콘솔로도 보낸다(프로필을 불러올 때의 저장 변환·읽기 실패 경고도 콘솔에 보이게). 개발 명령은 개발 빌드에서만 받도록 `session.Debug.Enabled = Debug.isDebugBuild`.
5. DB 데이터로 플레이어 데이터·로그·인스턴스·필드 규칙의 필수 값·상태 효과·밸런스 값·가중치 표·능력·사용 기능이 붙은 인스턴스·도전과제·개발 명령을 세션에 등록한다. `[BindTo("ID")]`의 ID가 DB의 어느 항목에도 없으면 콘솔에 "[MomoiProject] [BindTo("…")]: 이 ID의 DB 항목이 없어 연결되지 않았습니다" 경고를 낸다(오타·지운 항목).
6. 저장 호환 검사(개발 중이면 호환되지 않는 저장 파일만 옆으로 옮긴다 — `06-SaveLoad.md` 1.1), ID 이름 바꾸기 이력 적용, 프로필(`profile.json`) 불러오기(깨졌으면 바로 전 저장 `.bak`으로, `06-SaveLoad.md` 2.3).
7. 기본 개발 명령(저장·시간)을 등록한다.
8. `[MomoiProject]` 오브젝트를 만들어 매 프레임 세션을 진행시킨다(`LateUpdate`에서 `session.EndFrame`). 슬롯을 바꿔 세션을 다시 만들어도 오브젝트는 하나다.
9. Bootstrap 화면의 씬 흐름을 정해 두었으면 그 필드를 따라가기 시작한다(`GameScenes.Follow`, `32-View-Presentation-Scenes.md`).
10. `Started` 이벤트를 쏜다(4절 — 받는 쪽을 하나씩 부르므로 하나가 예외를 던져도 나머지와 이어 하기는 계속된다). 시작할 때 이어 하기를 켰으면 그 뒤에 판을 불러오거나 새 판 행동을 실행한다.

게임을 끝내면(`OnApplicationQuit`) `Stopping`을 쏘고, `AutoSaveRun`이 참이고 판이 살아 있으면 `SaveRun`(`Saving` → 프로필 → 판), 아니면 프로필만 저장한 뒤 `Stopped`를 쏜다(4절, `06-SaveLoad.md` 2.2).

### 코드로 쓰는 법

**`GBibleRuntime.Session`** — 지금 세션. 시작 전이면 `null`.

```csharp
using GBible;

var session = GBibleRuntime.Session;
```

**`GBibleRuntime.Game`** — 지금 세션의 `GameApi`. 씬 코드(MonoBehaviour)는 보통 이것을 속성으로 줄여 둔다.

```csharp
using GBible;
using UnityEngine;

public sealed class HudView : MonoBehaviour
{
    GameApi Game => GBibleRuntime.Game;
}
```

**`GBibleRuntime.Start()`** — 세션을 만들어 돌려준다. 이미 있으면 그것을 돌려준다. 자동 시작이 있으므로 직접 부를 일은 드물다.

**`GBibleRuntime.SavePath` / `RunSavePath`** — 지금 슬롯의 프로필(`profile.json`)과 판(`run.json`) 저장 파일 경로. 기본 슬롯이면 `<persistentDataPath>/MomoiProject/` 바로 아래, 다른 슬롯이면 `.../MomoiProject/slots/<이름>/`(슬롯은 `31-Slots-Settings-Input.md`).

**`GBibleRuntime.SaveProfile(session)` / `SaveRun(session)`** — 지금 상태를 파일에 쓴다. 프로필 = 메타 구역(판이 바뀌어도 남는 값), 판 = 세션 구역(이번 판)·난수·시간. `SaveRun`은 저장 직전 알림(`Saving`) → 프로필 → 판 차례로 프로필도 함께 쓴다(`06-SaveLoad.md` 2.3).

**`GBibleRuntime.HasSavedRun` / `LoadRun(session)`** — 저장한 판이 있는지(빈 파일은 없는 것으로), 있으면 불러온다(성공하면 `true`, 깨졌으면 `.bak`으로 이어 간다). 판 불러오기는 자동으로 하지 않는다. 타이틀의 '이어하기' 같은 곳에서 게임이 직접 부른다.

**`GBibleRuntime.NewRun(session)`** — 새 판. 이번 판 값을 처음 값으로 되돌리고(Undo 기록도 비움) 판 저장 파일을 지우고 판 끝 상태를 푼다. 행동 밖(버튼 등)에서 부른다(`06-SaveLoad.md` 2.3).

**`GBibleRuntime.DeleteRun()`** — 판 저장 파일만 지운다.

**`GBibleRuntime.AutoSaveRun`** — 끝낼 때 판을 자동 저장할지. 기본은 DB › 시작 파일 › Bootstrap의 **판 저장**을 따른다("저장하지 않음"이면 `false`). 코드에서 값을 넣으면 그 값이 우선한다. 끄기 전의 저장(행동마다·N초마다·이벤트를 받을 때)과 판 끝 이벤트도 같은 화면에서 정한다(3절 표, `06-SaveLoad.md` 2.2).

저장 파일의 내용·구역·호환 규칙은 `06-SaveLoad.md`에서 자세히 다룬다.

### 상황별 예제

**타이틀 화면의 이어하기·새 게임.**

```csharp
using GBible;
using UnityEngine;

public sealed class TitleMenu : MonoBehaviour
{
    // 버튼을 켤지 정할 때 읽는다.
    public bool CanContinue => GBibleRuntime.HasSavedRun;

    public void OnContinue()
    {
        if (!GBibleRuntime.LoadRun(GBibleRuntime.Session))
            Debug.LogWarning("판을 불러오지 못했습니다(모니터의 타임라인에서 이유를 볼 수 있다).");
    }

    public void OnNewGame()
    {
        GBibleRuntime.NewRun(GBibleRuntime.Session);   // 이번 판 값을 처음 값으로 + 예전 판 저장 지우기
    }
}
```

**중요한 순간에 바로 저장(자동 저장 지점).** 체크포인트·하루 끝처럼 이벤트로 정해지는 순간이면 보통은 Bootstrap 화면의 **판 저장**을 "이벤트를 받을 때 저장"으로 고르고 **판 저장 이벤트**를 정한다(코드 없음, `06-SaveLoad.md` 3.3). 코드로 하면 이렇다(행동 밖에서 부른다).

```csharp
using GBible;

public static class Checkpoint
{
    // Saving 알림 → 프로필 → 판 차례로 둘 다 쓴다(SaveProfile을 따로 부르지 않는다).
    public static void Save() => GBibleRuntime.SaveRun(GBibleRuntime.Session);
}
```

**판 자동 저장을 끄는 로그라이크.** 판이 끝나면 저장이 남지 않아야 하는 게임. 보통은 Bootstrap 화면의 **판 저장**을 "저장하지 않음"으로 고른다. 코드로 하면 이렇다.

```csharp
using GBible;
using UnityEngine;

public static class RoguelikeBoot
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void Hook() => GBibleRuntime.AutoSaveRun = false;
}
```

### 주의할 점

- 씬 코드의 `Awake`·`Start`가 불릴 때는 세션이 이미 만들어져 있다(첫 씬을 불러오기 전에 시작하므로). 그때 `GBibleRuntime.Session`을 바로 써도 된다.
- 도메인 리로드를 끈 설정(Enter Play Mode Options)에서도 이전 플레이의 세션·이벤트 연결이 남지 않도록 `SubsystemRegistration` 시점에 모두 비운다. 그래서 이벤트 연결은 아래 3·4절처럼 매 플레이마다 다시 한다.
- `GBibleRuntime.Game`은 클라이언트 편의용 정적 입구다. 서버·시뮬레이션 코드는 정적 입구 대신 세션마다 `session.Game`을 쓴다(멀티플레이를 위해).

---

## 3. 세션 옵션 — `Configuring`과 `SessionOptions`

**한 줄 요약.** Undo 방식·보관 수·시드·판 저장 시점은 DB › 시작 파일 › Bootstrap 화면에서 정한다. 화면에 없는 옵션(저장 버전 …)이나 조건에 따른 변경은 세션을 만들기 직전의 `Configuring` 이벤트에서 코드로 바꾼다. 코드가 이긴다.

**언제.** 실시간 게임이라 스냅샷 되감기를 쓸 때, 재현 가능한 고정 시드가 필요할 때, 출시 후 저장 구조를 바꿀 때.

### 에디터에서

DB 창 사이드바 맨 위 **시작 파일 › Bootstrap**을 누르면 세션 옵션 화면이 열린다. 고친 값은 DB 에셋(`GameDatabase`)에 바로 저장된다(Ctrl+S 필요 없음). 화면 위 **cs 열기**는 `Client/Bootstrap.cs`를 연다.

| 칸 | 같은 뜻의 `SessionOptions` | 기본 |
|---|---|---|
| Undo 방식(행동 단위·스냅샷·쓰지 않음) | `History` | 행동 단위 |
| Undo 보관 수(행동 단위일 때) | `HistoryLimit` | 200(새 DB. 전에 만든 DB는 저장된 값 그대로, 0 = 제한 없음) |
| 스냅샷 간격(초)·스냅샷 보관 수(스냅샷일 때) | `SnapshotInterval`·`SnapshotLimit` | 1초·30개 |
| 개발 빌드에서 시드 고정 + 시드 | 개발 빌드에서만 `Seed` | 끔(지금 시각) |
| Undo 끊는 이벤트(선택) | 그 이벤트를 쏜 바깥 행동이 끝나면 `session.ClearUndo()` | 없음 |
| 판 저장 | 끌 때 저장 / 행동마다 저장(턴제) / 저장하지 않음(이어 하기 없음) / N초마다 저장(실시간·방치형) / 이벤트를 받을 때 저장(하루 끝·체크포인트). 끌 때 저장 여부는 `GBibleRuntime.AutoSaveRun` | 끌 때 저장 |
| 저장 간격(초)(N초마다일 때) | 마지막 저장에서 이만큼 지났고 그사이 행동이 있었으면 판을 저장(실제 시간, 1 이상) | 30 |
| 판 저장 이벤트(이벤트를 받을 때일 때) | 이 이벤트를 쏜 행동이 끝난 프레임 끝에 판을 저장(행동이 실패하면 저장하지 않음) | 없음 |
| 판 끝 이벤트 | 받은 행동이 끝나면 판 저장을 지우고 프로필을 바로 저장 | 없음 |
| 프로필도 행동마다 저장 | 바깥 행동이 끝날 때마다 프로필도 저장(N초마다면 그 간격으로). 끄더라도 판을 저장할 때 프로필이 바뀌었으면 함께 저장한다 | 끔 |
| 시작할 때 이어 하기 + 새 판 행동(선택) | `Started` 뒤 `LoadRun`, 없으면 `session.Do(new 새 판 행동())` | 끔 |
| 고정 틱(실시간) › 초당 틱 수 | `TickRate` | 0(끔, 턴제·카드) |
| 씬 흐름(선택) › 플레이어 데이터 · 필드(씬 이름이 든 글자) · 빈 값일 때 씬 | `Started` 직전 `GameScenes.Follow(키, 필드, 빈 값일 때 씬)` | 쓰지 않음 |

게임이 켜질 때 이 값이 `SessionOptions`에 먼저 들어가고, 그다음 `Configuring`이 불린다. 코드가 같은 칸을 바꾸면 코드 값이 쓰이고, 모니터 **타임라인**에 "코드(Configuring)가 DB › Bootstrap의 세션 옵션을 바꿨습니다: …"가 남는다. 시작 설정의 Undo 방식도 이 화면과 같은 값이다.

출시 여부는 메뉴 `MomoiProject → 출시됨 (저장 호환 유지)`(체크 메뉴)로 정한다. 켜고 끌 때 확인 창("출시 상태")이 뜬다.

- 개발 중(체크 해제, 기본): 데이터 구조가 호환되지 않게 바뀌면 저장을 변환하지 않고 초기화한다 — 맞지 않는 그 파일만 `이름.reset-<시각>.json`으로 옆에 옮긴다(지우지 않는다). 기록에 "개발 중 데이터 구조가 바뀌어 run.json을(를) 초기화했습니다: …" 경고가 남는다(`06-SaveLoad.md` 1.1).
- 출시됨(체크): 저장을 초기화하지 않는다. ID 이름 바꾸기 이력을 남겨 예전 이름으로 저장된 값을 이어받고, 구조 변경은 저장 버전 변환(DB › 개발 › 저장 변환 표, 또는 코드의 `Migrations`)으로 처리한다.

### 코드로 쓰는 법

**`GBibleRuntime.Configuring`** — `Action<SessionOptions>`. 세션이 만들어지기 전에 받아야 하므로, 자동 시작보다 먼저 불리는 `AfterAssembliesLoaded` 시점에 연결한다.

```csharp
using GBible;
using UnityEngine;

public static class GameOptions
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void Hook() => GBibleRuntime.Configuring += Configure;

    static void Configure(SessionOptions options)
    {
        // 아래 항목들을 여기서 바꾼다.
    }
}
```

`SessionOptions`의 칸을 하나씩 본다.

**`History`** — 되돌리기 방식. `HistoryMode.Actions`(기본: 행동 하나씩 Undo, 턴제·퍼즐), `HistoryMode.Snapshots`(일정 간격 스냅샷을 쌓아 하나씩 되감기, 실시간 게임), `HistoryMode.None`(되돌리기 없음, 기록 비용 없음). 어느 방식이든 게임 코드는 `session.Undo()` 하나로 되돌린다.

```csharp
options.History = HistoryMode.Snapshots;
```

**`HistoryLimit`** — `Actions` 방식에서 Undo할 수 있는 최대 행동 수. `0`이면 무제한(행동마다 바뀐 값의 이전 모습을 보관하므로 메모리가 는다). `GBibleRuntime`은 Bootstrap 화면의 **Undo 보관 수**(새 DB 기본 200)를 넣는다. 세션을 직접 만들면 기본이 `0`이다.

```csharp
options.HistoryLimit = 100;
```

**`SnapshotInterval` / `SnapshotLimit`** — `Snapshots` 방식의 간격(게임 시간 초, 기본 1)과 보관 개수(기본 30).

```csharp
options.SnapshotInterval = 0.5;
options.SnapshotLimit = 60;
```

**`Seed`** — 난수 시드(`ulong`). 런타임은 기본으로 지금 시각(`DateTime.UtcNow.Ticks`)을 넣는다. 같은 시드 + 같은 행동이면 `Game.Random` 결과가 같다.

```csharp
options.Seed = 20261003UL;
```

**`TickRate`** — 고정 틱: 초당 틱 수(기본 0 = 끔). 켜면 게임 시간이 1/틱 초씩 일정하게 흐르고 시간 예약·상태 효과의 시간 지속·시간 조건·`session.Ticked`가 틱마다 돌아 프레임 속도와 상관없이 같은 결과가 난다(`33-Tick-Preview-Csv.md`). Bootstrap 화면 **고정 틱(실시간)**과 같다.

```csharp
options.TickRate = 30;
```

**`JournalCapacity`** — 통합 기록(Journal)에 보관할 줄 수(기본 4096). 넘치면 오래된 줄부터 밀려난다. Bootstrap 화면 **고급 › 기록 보관 줄 수**와 같다.

**`Migrations`** — 저장 데이터 버전 변환. 출시 후 구조를 바꾸면 변환을 등록한다(버전 2부터). 키·경로만 받는 변환(필드 옮김·키 이름 바꿈·키 삭제)은 보통 **DB › 개발 › 저장 변환** 표에 적는다. 표는 `Configuring` 직전에 버전 순서(같은 버전은 ID 글자 순서)로 여기에 등록되고, 코드는 그 뒤에 더한다. 코드로는 값을 계산해 바꾸는 `Add`만 쓰는 것이 기본이다. 저장 버전은 등록한 변환의 가장 높은 버전으로 저절로 올라간다(`session.SaveVersion`). `SaveVersion` 옵션은 최솟값이라 보통 손대지 않는다. DB 항목 ID 바꾸기는 변환 없이 런타임이 이어받는다(`06-SaveLoad.md` 1.4 · 2.7).

```csharp
options.Migrations.RenameKey(2, "gold", "playerGold");   // 저장 버전 2
```

`SaveMigrations`에는 `RenameKey(버전, 예전 키, 새 키)`, `RemoveKey(버전, 키)`, `MoveField(버전, 키, 예전 경로, 새 경로)`, 직접 고치는 `Add(버전, 설명, Action<Dictionary<string, object>>)`가 있다. 필드 이름만 바뀐 경우는 필드에 Unity의 `[FormerlySerializedAs("예전이름")]`만 달면 변환 없이 따라간다.

**`DetectUntrackedChanges`** — 예전 방식(리플렉션 비교) 데이터에서 Modify를 거치지 않은 수정을 찾는 검사. 값 전체를 비교하므로 비싸다. 런타임은 개발 빌드(`Debug.isDebugBuild`)에서만, Bootstrap 화면 **고급 › 개발 빌드에서 Modify 누락 검사**가 켜져 있을 때 켠다.

### 상황별 예제

**실시간 액션 게임: 0.5초 간격 스냅샷 되감기(최근 30초).** 보통은 Bootstrap 화면에서 Undo 방식 "스냅샷", 간격 0.5, 보관 수 60을 고른다. 코드로 하면 이렇다.

```csharp
using GBible;
using UnityEngine;

public static class ActionGameOptions
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void Hook() => GBibleRuntime.Configuring += options =>
    {
        options.History = HistoryMode.Snapshots;
        options.SnapshotInterval = 0.5;
        options.SnapshotLimit = 60;
    };
}
```

**버그 재현용 고정 시드.** 개발 빌드에서만 시드를 고정한다. 보통은 Bootstrap 화면의 **개발 빌드에서 시드 고정**을 켠다. 코드로 하면 이렇다.

```csharp
using GBible;
using UnityEngine;

public static class ReplaySeed
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void Hook() => GBibleRuntime.Configuring += options =>
    {
        if (Debug.isDebugBuild) options.Seed = 12345UL;
    };
}
```

**출시 후 구조 변경: 인벤토리의 골드를 지갑으로 옮김.** 버전 3에서 `inventory` 값의 `gold` 필드를 `wallet.gold`로 옮긴다. 보통은 DB › 개발 › 저장 변환에 두 줄(버전 2 · 키 이름 바꿈 · `gold` → `playerGold`, 버전 3 · 필드 옮김 · `inventory` · `gold` → `wallet.gold`)을 적는다. 코드로 하면 이렇다.

```csharp
using GBible;
using UnityEngine;

public static class SaveVersions
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void Hook() => GBibleRuntime.Configuring += options =>
    {
        options.Migrations.RenameKey(2, "gold", "playerGold");
        options.Migrations.MoveField(3, "inventory", "gold", "wallet.gold");
    };
}
```

**서버·테스트: 런타임 없이 세션 만들기.**

```csharp
using GBible;

var session = new GameSession(new SessionOptions { Seed = 7, History = HistoryMode.None });
```

### 주의할 점

- `MonoBehaviour.Awake`에서 `Configuring`을 연결하면 늦다(세션이 이미 만들어졌다). 꼭 `AfterAssembliesLoaded`(또는 `BeforeSplashScreen`)에서 연결한다.
- `Configuring` 안에서는 세션이 아직 없다. 행동 처리기·코드로 쓰는 개발 명령은 `Started`에서 등록한다(DB › 개발 › 개발 명령의 항목은 런타임이 등록한다).
- `Configuring`은 받는 쪽을 하나씩 부른다. 하나가 예외를 던지면 콘솔에 남기고 나머지 받는 쪽과 세션 만들기는 계속한다(그 받는 쪽이 바꾸려던 옵션만 빠진다).
- 개발 중 상태에서는 키를 지우거나 형식을 바꾸면 그 저장 파일이 초기화된다(옆으로 옮겨진다). 키를 더하기만 한 경우는 그대로 이어간다.

---

## 4. 시작·끝 알림 — `Started` / `Stopping` / `Stopped`

**한 줄 요약.** 세션 준비가 끝나면 `Started`, 게임을 끝낼 때 저장하기 전에 `Stopping`, 저장한 뒤에 `Stopped`를 쏜다. 모두 `Action<GameSession>`이다. 판 저장 직전·판 불러온 뒤의 알림(`Saving`·`RunLoaded`)은 `06-SaveLoad.md` 2.10.

**언제.** 행동 처리기·프로젝트 기능의 감시·코드로만 정의하는 플레이어 데이터를 등록할 때(`Started`), 끄기 전에 마지막 값을 행동으로 남길 때(`Stopping`), 끝낼 때 외부 서비스에 알릴 때(`Stopped`).

### 코드로 쓰는 법

**`Started`** — 준비된 세션을 받는다. `Configuring`과 같은 이유로 `AfterAssembliesLoaded`에서 연결한다.

```csharp
using GBible;
using UnityEngine;

public static class GameBoot
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void Hook() => GBibleRuntime.Started += OnStarted;

    static void OnStarted(GameSession session)
    {
        session.Debug.Register("치트", "Undo 전부", () => { while (session.Undo()) { } }, "되돌릴 수 있는 행동을 모두 되돌립니다.");
    }
}
```

**`session.Debug.Register(분류, 이름, 동작, 설명)`** — 모니터의 명령 탭에 버튼을 코드로 더한다. 출시 빌드에서는 `session.Debug.Enabled`가 거짓이라 아무것도 하지 않으므로 `if (Debug.isDebugBuild)`로 감싸지 않는다. 결과 Block으로 조립할 수 있는 명령은 코드 대신 DB › 개발 › 개발 명령에 만든다(`22-Monitor-Debug.md` 7절).

**`session.Data.Define(키, 구역, 기본값, 표시 이름)`** — DB에 없는, 코드에서만 쓰는 값을 등록한다. 예: 콤보 수처럼 화면만 쓰는 값.

```csharp
session.Data.Define("sample:combo", DataZone.Session, 0, "현재 콤보");
```

**`Stopping`** — 끄기 직전, 저장보다 먼저 불린다(슬롯을 바꾸거나 지워 세션을 다시 만들 때도). 여기서 `session.Do`로 바꾼 값은 이어지는 저장에 들어간다. 오프라인 보상의 마지막 시각을 남기는 곳이다(`07-Random-Time.md` 4.7).

```csharp
GBibleRuntime.Stopping += session => session.Do(new Settle());   // Settle은 게임이 만든 정산 행동
```

**`Stopped`** — 프로필·판 저장이 끝난 뒤 불린다.

```csharp
GBibleRuntime.Stopped += session => UnityEngine.Debug.Log($"기록 {session.Journal.Count}줄로 끝냄");
```

### 상황별 예제

**개발 빌드에서만 치트 명령.** DB › 플레이어 데이터에 `playerGold`(숫자)가 있다고 하자. 보통은 DB › 개발 › 개발 명령에 항목을 만들고 결과에 `플레이어 데이터에 주기 · playerGold` 증감 1000을 넣는다. 코드는 없고, 누르면 행동 하나로 실행되어 Undo된다.

규칙 함수를 불러야 해서 코드로 쓸 때는 `Started`에서 등록한다. 출시 빌드에서는 런타임이 등록을 무시하므로 감싸는 검사가 없다.

```csharp
using GBible;
using UnityEngine;

public static class Cheats
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void Hook() => GBibleRuntime.Started += session =>
    {
        // 값을 바꾸는 명령은 행동으로 실행해야 기록·Undo된다(SkipToBoss는 게임이 만든 행동).
        session.Debug.Register("치트", "보스 앞으로", () => session.Do(new SkipToBoss()));
    };
}
```

같은 일을 DB 항목 + `[BindTo]`로도 쓸 수 있다(`13-BindTo.md` 3-10). 그쪽은 행동으로 감싸는 일을 런타임이 한다.

**씬이 바뀌어도 남는 매니저가 세션을 늦게 받는 경우.** `Started`를 놓쳤을 수 있으면 지금 세션을 먼저 확인한다.

```csharp
using GBible;
using UnityEngine;

public sealed class QuestTracker : MonoBehaviour
{
    void Start()
    {
        if (GBibleRuntime.Session != null) Bind(GBibleRuntime.Session);
    }

    void Bind(GameSession session)
    {
        // 이벤트 받기 등록 등
    }
}
```

### 주의할 점

- `Started`에서 쏜 이벤트·바꾼 값도 기록에 남는다. 처음 값은 DB의 기본값으로 정하는 편이 낫다.
- `Started`는 받는 쪽을 하나씩 부른다. 하나가 예외를 던져도 콘솔에 남기고 나머지 받는 쪽과 시작할 때 이어 하기는 계속한다(프로젝트 코드·선택 기능 하나가 시작 처리를 멈추지 않게).
- `Stopping`·`Stopped`는 `OnApplicationQuit`에서 불린다. 에디터에서 플레이를 멈출 때도 불린다. 모바일에서 앱을 강제로 끄면 오지 않을 수 있으므로, 꼭 남아야 하는 값은 그때그때 행동으로 남기고 저장한다(**프로필도 행동마다 저장**).

---

## 5. 런타임 데이터 목록 — `GameDatabase`

**한 줄 요약.** 모든 DB 항목(`GameData`)의 목록 에셋. 에디터가 자동으로 채우고, 런타임은 `GameDatabase.Load()`로 읽는다.

**언제.** 게임 코드가 DB 데이터(도전과제 목록, 스탠딩 프로필, 인스턴스 원본 …)를 직접 훑어볼 때.

### 에디터에서

직접 편집하지 않는다. DB에서 항목을 만들고 저장(Ctrl+S)하면, 또는 프로젝트 창에서 `GameData` 에셋이 추가·삭제·이동되면 목록이 자동으로 맞춰진다. 에셋 위치는 `Assets/MomoiProject/Data/Resources/MomoiProject/GameDatabase.asset`이다.

### 코드로 쓰는 법

**`GameDatabase.Load()`** — `Resources/MomoiProject/GameDatabase`를 읽는다. 없으면 `null`.

```csharp
using GBible.Data;

var database = GameDatabase.Load();
```

**`All<T>()`** — 그 종류의 항목만.

```csharp
foreach (var achievement in database.All<AchievementData>())
    UnityEngine.Debug.Log(achievement.Id);
```

**`Entries`** — 모든 항목(`IReadOnlyList<GameData>`). **`IsReleased`** — 출시 상태. **`Renames`** — ID 이름 바꾸기 이력.

**`GameData.Id`** — 항목은 ID로만 식별한다. `DisplayName`도 ID와 같다(플레이어에게 보일 이름은 텍스트 `<id>.name`, `23-Text-Localization.md`).

### 상황별 예제

**도전과제 목록 화면.** 이름은 다국어 텍스트, 달성 여부는 세션에서.

```csharp
using GBible;
using GBible.Data;
using UnityEngine;

public sealed class AchievementList : MonoBehaviour
{
    void OnEnable()
    {
        var session = GBibleRuntime.Session;
        foreach (var data in GameDatabase.Load().All<AchievementData>())
        {
            var name = Texts.NameOf(data.Id);
            var done = session.Achievements.IsUnlocked(data.Id);
            Debug.Log($"{(done ? "✓" : " ")} {name}");
        }
    }
}
```

**인스턴스 원본을 읽기만 할 때.** 원본은 고치지 않는다. 고칠 사본은 `session.CopyInstance(id)`로 받는다.

```csharp
using GBible.Data;
using System.Linq;

var potion = GameDatabase.Load().All<InstanceData>().First(i => i.Id == "instance.potion");
var copy = potion.CreateInstance();   // 깊은 사본
```

### 주의할 점

- 런타임에 `Resources.Load`는 한 번만 하고 결과를 들고 있는다(매 프레임 부르지 않는다).
- 저장(Ctrl+S)하지 않은 DB 변경은 목록에 없다. 플레이를 시작할 때 저장하지 않은 변경이 있으면 DB가 저장할지 묻는다.

---

## 6. 선택 기능 켜고 끄기 — `GameFeatures`

**한 줄 요약.** 다이얼로그 시스템·인벤토리 시스템처럼 프로젝트마다 쓸지 말지 정하는 모듈을 DB 사이드바의 스위치로 켜고 끈다.

**언제.** 모듈을 처음 쓸 때, 쓰지 않는 모듈을 빌드에서 뺄 때.

| 기능 | 스크립팅 심볼 | 어셈블리 | 켜면 생기는 탭 |
|---|---|---|---|
| 다이얼로그 시스템 | `GBIBLE_DIALOGUE` | `GBible.Dialogue` (+ `.Editor`) | 스탠딩 프로필, 장소, 메뉴 `MomoiProject → 다이얼로그 시스템 → …` |
| 인벤토리 시스템 | `GBIBLE_INVENTORY` | `GBible.Inventory` | 없음(플레이어 데이터의 값 형식에 `Inventory`·`InventorySlot`이 나온다) |
| 입력(키 바꾸기) | `GBIBLE_INPUT` | `GBible.Input`(Input System 패키지 `com.unity.inputsystem`이 있을 때만 컴파일) | 없음(`GameInput`: 키 바꾸기·되돌리기·저장, `31-Slots-Settings-Input.md`) |
| Steam | `GBIBLE_STEAM` | `GBible.Steam`(Steamworks.NET 패키지 `com.rlabrecque.steamworks.net`이 있을 때만 컴파일) | Steam(그룹 개발, `34-Steam.md`) |

스크립팅 심볼은 `#if`로 코드를 켜고 끄는 컴파일 표시다. 모듈의 asmdef는 이 심볼이 있을 때만 컴파일된다(asmdef의 defineConstraints).

### 에디터에서

1. `MomoiProject → DB`을 연다.
2. 왼쪽 사이드바 아래쪽에 기능 이름의 그룹(예: "다이얼로그 시스템")이 있다. 그 첫 줄 **기능 활성화**(오른쪽 끝 스위치)를 누른다.
3. 확인 창("기능 켜기" / "기능 끄기")에서 **켜기** 또는 **끄기**를 누른다.
4. Unity가 다시 컴파일한다. 컴파일이 끝나면 탭·메뉴가 열린다. 꺼진 기능의 탭은 사이드바에 흐리게 보이고 들어갈 수 없다(마우스를 올리면 "'다이얼로그 시스템' 기능이 꺼져 있습니다. 위의 '기능 활성화'를 눌러 켜면 열립니다.").

켜짐 상태는 `ProjectSettings/MomoiProject/Features.json`에 저장되어 팀이 함께 쓴다. 심볼은 이 파일에 맞춰 지금 고른 플랫폼, 지금 빌드 대상의 플랫폼, PC(Standalone), 서버(Dedicated Server — 심볼 칸이 PC와 따로다)에 자동으로 넣고 빼며, 다른 플랫폼으로 바꾸는 순간 그 플랫폼에도 맞춘다(서버 빌드 프로필로 바꿔도 모듈을 쓰는 게임 코드가 컴파일된다). 예전 이름의 심볼(`GAME_SYSTEMS_*`)은 `GBIBLE_*`로 바꾼다.

### 코드로 쓰는 법

**`#if GBIBLE_DIALOGUE`** — 모듈을 쓰는 게임 코드를 감싸면 모듈을 꺼도 컴파일이 깨지지 않는다.

```csharp
#if GBIBLE_DIALOGUE
using GBible.Dialogue;
#endif

public static class Cutscenes
{
    public static void PlayIntro()
    {
#if GBIBLE_DIALOGUE
        Dialogues.Play("intro");
#else
        UnityEngine.Debug.Log("다이얼로그 시스템이 꺼져 있습니다.");
#endif
    }
}
```

**에디터 코드: `GameFeatures`** (`GBible.EditorTools`, 어셈블리 `GBible.Editor`, 에디터 전용) — `Find("dialogue")`, `IsEnabled(feature)`, `IsActive(feature)`(켜져 있고 컴파일까지 끝남), `SetEnabled(feature, bool)`, `Changed` 이벤트. `Editor` 폴더 안의 스크립트(또는 `GBible.Editor`를 참조하는 에디터 asmdef)에서 쓴다.

```csharp
// Assets/MomoiProject/Code/Editor/FeatureCheck.cs
using GBible.EditorTools;

static class FeatureCheck
{
    [UnityEditor.MenuItem("Tools/다이얼로그 켜짐 확인")]
    static void Check()
    {
        var dialogue = GameFeatures.Find("dialogue");
        UnityEngine.Debug.Log(GameFeatures.IsActive(dialogue) ? "켜짐" : "꺼짐 또는 컴파일 중");
    }
}
```

### 상황별 예제

**다이얼로그를 쓰지 않는 퍼즐 게임.** 기능 활성화를 끈다(또는 처음부터 켜지 않는다). 빌드에 다이얼로그 코드가 들어가지 않는다. 폴더(`Optional/Dialogue`)를 통째로 지워도 된다. 폴더가 없으면 스위치는 비활성이고 "이 프로젝트에 '다이얼로그 시스템'의 코드 폴더가 없어 켤 수 없습니다."라고 알린다.

**잠시 껐다가 다시 켜기.** 끄면 그 기능의 데이터 에셋(스탠딩 프로필·장소 …)은 지워지지 않고 남는다. 다시 켜면 그대로 돌아온다.

### 주의할 점

- 기능을 끄면 그 기능을 쓰는 게임 코드(`#if`로 감싸지 않은 것)가 컴파일 오류를 낸다. 확인 창이 이것을 알린다.
- 켠 직후에는 컴파일이 끝나야 탭이 열린다(스위치 툴팁: "켜는 중(다시 컴파일이 끝나면 탭이 열립니다)").

---

## 7. 식별자 코드 — `GameIds`

**한 줄 요약.** DB의 모든 ID를 C# 상수로 만든 파일 `GameIds.cs`. 코드에서 ID를 글자로 쓰지 않고 자동완성으로 골라 오타를 컴파일 단계에서 막는다.

**언제.** 게임 코드에서 플레이어 데이터·능력·도전과제·인스턴스 ID를 쓸 때마다.

### 에디터에서

- 자동: DB 변경을 저장(Ctrl+S)하면 다시 만든다. 내용이 같으면 파일을 건드리지 않는다.
- 수동: 메뉴 `MomoiProject → 식별자 코드 다시 만들기`. 콘솔에 "[MomoiProject] 식별자 코드를 만들었습니다: 경로"가 찍힌다.
- 위치: 플레이어 데이터의 값 형식이 들어 있는 asmdef 폴더의 `Generated/`(그래야 그 형식을 참조할 수 있다). 형식이 없거나 `Assembly-CSharp`에 있으면 `Assets/MomoiProject/Generated/`. `ProjectSettings/MomoiProject/Ids.json`의 `folder`에 경로를 적으면 그곳에 만든다.

### 코드로 쓰는 법

생성되는 모양(네임스페이스 `GBible.Ids`):

```csharp
namespace GBible.Ids
{
    public static class GameIds
    {
        public static class PlayerData
        {
            public static readonly global::GBible.PlayerDataKey<int> PlayerGold = new global::GBible.PlayerDataKey<int>("player.gold");
        }
        public static class Achievement
        {
            public const string FirstHunt = "firstHunt";
        }
    }
}
```

- **이름 규칙**: ID를 점·밑줄로 나눠 각 조각의 첫 글자를 대문자로 이은 것(`player.gold` → `PlayerGold`, `instance.potion` → `InstancePotion`).
- **분류 이름**: DB 종류 클래스 이름에서 끝의 `Data`를 뺀 것(`AchievementData` → `Achievement`, `InstanceData` → `Instance`, `StatData` → `Stat`, `StatusData` → `Status`, `BalanceData` → `Balance`, `WeightTableData` → `WeightTable`, `SettingData` → `Setting`). 플레이어 데이터만 `PlayerData`.
- **플레이어 데이터 = `PlayerDataKey<T>`**: 값 형식이 붙은 키. `.Id`가 ID 글자이고, `string`으로 바로 바뀐다(암시 변환).
- **그 밖의 항목 = `const string`**: `[BindTo(...)]` 같은 어트리뷰트 인자에도 쓸 수 있다.

**`PlayerDataKey<T>`로 읽고 쓰기**:

```csharp
using GBible;
using GBible.Ids;

var store = GBibleRuntime.Session.Data;
int gold = store.Get(GameIds.PlayerData.PlayerGold);
store.Set(GameIds.PlayerData.PlayerGold, gold + 100);
```

**`Game.Data()` — 플레이어 데이터를 이름으로**: 같은 파일에 `PlayerDataView`가 함께 생성된다. 단순 값은 읽고 쓰기, 클래스·리스트 값은 꺼내서 안쪽 필드를 고친다.

```csharp
using GBible;
using GBible.Ids;

GBibleRuntime.Game.Data().PlayerGold += 100;
```

### 상황별 예제

**도전과제 결과를 코드로 연결.** `[BindTo]`는 `const string`을 받으므로 생성 상수를 그대로 쓴다.

```csharp
using GBible;
using GBible.Ids;

public static class AchievementRewards
{
    [BindTo(GameIds.Achievement.FirstHunt)]
    static void FirstHunt(TriggerContext c) => c.Game.Data().PlayerGold += 100;
}
```

**능력 쓰기.**

```csharp
GBibleRuntime.Game.Use(GameIds.Ability.AbilityHeal, heroId, targetId);
```

(위 두 예제는 DB에 `firstHunt` 도전과제, `ability.heal` 능력, `player.gold`(int) 플레이어 데이터가 있을 때 컴파일된다.)

### 주의할 점

- 생성 파일은 직접 고치지 않는다(다시 만들면 덮인다).
- 저장(Ctrl+S)하지 않은 DB 항목은 생성 코드에 없다. 저장한 뒤 컴파일이 끝나야 새 상수가 보인다.
- 값 형식을 이 위치에서 참조할 수 없으면(예: 다른 asmdef의 클래스) 그 플레이어 데이터는 `PlayerDataKey<T>` 대신 `const string`으로 만들어지고 `Game.Data()`에도 빠진다. `Ids.json`의 `folder`를 그 형식의 asmdef 쪽으로 바꾸면 형식이 붙는다.
- 키워드는 `GameIds`에 없다. 키워드 그룹 코드(`[KeywordGroup] static class`)의 상수가 곧 ID다.

---

## 8. 어셈블리와 asmdef

**한 줄 요약.** 게임 코드가 어느 어셈블리를 참조해야 하는지, 플레이어 데이터 비교 코드가 어디에 만들어지는지.

| 어셈블리 | 폴더 | 내용 | 자동 참조 |
|---|---|---|---|
| `GBible.Core` | `Runtime/Core` | 게임 로직(세션·플레이어 데이터·이벤트·트리거·텍스트 …). Unity 없이 .NET에서도 컴파일되도록 쓴다. | 예 |
| `GBible.Unity` | `Runtime/Unity` | `GameData`·`GameDatabase`·`GBibleRuntime`·`GameTime`·오디오·다국어 Unity 쪽 | 예 |
| `GBible.Editor` | `Editor` | DB·모니터·오디오 창, 검증기, 생성 코드 | 예(에디터) |
| `GBible.Dialogue` · `.Editor` | `Optional/Dialogue` | 선택 모듈(`GBIBLE_DIALOGUE`) | 예 |
| `GBible.Inventory` | `Optional/Inventory` | 선택 모듈(`GBIBLE_INVENTORY`) | 예 |
| `GBible.Input` | `Optional/Input` | 선택 모듈(`GBIBLE_INPUT`, Input System 패키지가 있을 때만). 참조: `Unity.InputSystem` | 예 |
| `GBible.Steam` | `Optional/Steam` | 선택 모듈(`GBIBLE_STEAM`, Steamworks.NET 패키지가 있을 때만, PC·에디터). 참조: `com.rlabrecque.steamworks.net` | 예 |
| `Unity.GBible.CodeGen` | `CodeGen` | 컴파일할 때 Mirror 비교 코드를 만든다(에디터 전용, `com.unity.nuget.mono-cecil`이 있을 때만) | 아니오 |

### 코드로 쓰는 법

- **`Assembly-CSharp`(asmdef 없는 게임 코드)**: 자동 참조라 아무것도 하지 않아도 된다.
- **게임 코드를 자기 asmdef에 둘 때**: References에 `GBible.Core`, `GBible.Unity`를 더한다. 모듈을 쓰면 `GBible.Dialogue`, `GBible.Inventory`도 더한다. 에디터 확장을 쓰면 에디터 asmdef에 `GBible.Editor`를 더한다.

```json
{
    "name": "MyGame",
    "references": [ "GBible.Core", "GBible.Unity", "GBible.Inventory" ]
}
```

### Mirror 비교 코드

플레이어 데이터는 Mirror로 기록된다. Mirror는 시스템이 들고 있는 플레이어 데이터의 사본이고, Sync는 지금 값과 사본을 비교해 바뀐 곳만 기록하는 동작이다(`Coding.md`). 비교 코드는 `Unity.GBible.CodeGen`이 컴파일할 때마다 `GBible.Core`와 **그것을 참조하는 어셈블리**의 `[Serializable]` 클래스·구조체 안에 만든다. 그래서:

- 플레이어 데이터 클래스는 `GBible.Core`를 참조하는 어셈블리(`Assembly-CSharp` 포함)에 둔다.
- `com.unity.nuget.mono-cecil` 패키지가 없으면 비교 코드가 만들어지지 않고, 예전 방식(리플렉션 비교, 느림)으로 돌며 기록에 "예전 방식(리플렉션)으로 비교합니다" 경고가 남는다. DB › 플레이어 데이터 검증에도 같은 경고가 뜬다.

### 코드 줄이기(Managed Stripping)

IL2CPP 빌드의 코드 줄이기(Player Settings › **Managed Stripping Level**)는 코드에서 가리키지 않는 것을 지운다. GBible이 리플렉션으로만 찾는 것이 지워지면 빌드에서만 조용히 망가진다(행동이 서버에서 거절됨·이벤트가 빠짐·화면이 동작하지 않음). 그래서 셋이 지킨다.

- **생성 코드**: 생성기가 Mirror 비교 코드의 등록 메서드(`__gb_register`)에 `[Preserve]`를 붙인다. 등록 메서드가 나머지 생성 메서드를 가리키므로 함께 남는다. 할 일은 없다.
- **게임 코드**: 시작 설정이 `Assets/MomoiProject/link.xml`을 만든다(없을 때만 — 지우면 에디터를 열 때 다시 만든다). `MomoiProject.Rules`·`MomoiProject.Client`와 빌드에 들어가는 그 밖의 `MomoiProject.*` 어셈블리, `Assembly-CSharp`를 통째로 남긴다(`[BindTo]` 함수·`Reward` 클래스·화면 코드·DB가 이름으로 가리키는 행동·이벤트). 이미 있는 파일은 덮지 않으므로 손으로 더한 줄은 그대로다. 빌드에 들어가는 `MomoiProject.*` 어셈블리를 새로 만들면 `<assembly fullname="…" preserve="all" />` 줄을 하나 더한다.
- **빌드 직전 검사**: 줄이기 단계가 Low 이상(Low·Medium·High — Minimal은 게임 어셈블리를 줄이지 않는다)인데 link.xml이 없거나 읽을 수 없거나 빠진 `MomoiProject.*` 어셈블리가 있으면 콘솔에 "[MomoiProject] Managed Stripping Level이 …입니다. …" 경고를 낸다. 빌드는 막지 않는다. 전용 서버 빌드는 Server 대상의 단계를 본다.

### 주의할 점

- 테스트 어셈블리는 자동 참조가 아니다. 테스트 어셈블리에는 비교 코드 생성 경고를 내지 않고, 그 형식은 Unity Test Runner로 돌릴 때만 훑는다(`38-Testing.md` 6절).
- Unity 버전을 올린 뒤에는 Mirror 생성 코드가 정상인지 코어 테스트(`MirrorTests`)로 확인한다.

---

## 10. 처음 상태로 되돌리기

**한 줄 요약.** 메뉴 `MomoiProject → 처음 상태로 되돌리기…`는 프로젝트를 "막 만들고 Momoi Project와 필요한 에셋만 설치한" 상태로 되돌린다. 개발자가 더하고 바꾼 것은 모두 백업 폴더로 옮겨지고, Unity가 다시 열린다.

**언제.** 시험 삼아 이것저것 만들어 본 프로젝트를 깨끗하게 다시 시작할 때.

### 처음 상태 기록
- 프로젝트 폴더 바로 아래 `MomoiProjectBaseline/`에 Assets·ProjectSettings·Packages(GBible 자신은 빼고)의 사본을 둔다. Assets 밖이라 Unity가 읽지 않는다.
- **저절로 남는 때:** 시스템이 아직 아무것도 만들지 않은 깨끗한 프로젝트(`Assets/MomoiProject`·`Assets/Dev`·시작 설정 표시가 없음)를 처음 열 때, 시작 설정 창이 뜨기 전에 남긴다. 콘솔에 "처음 상태를 기록했습니다"가 나온다.
- **직접 남기기:** 메뉴 `MomoiProject → 지금 상태를 처음 상태로 기록`. 이미 있으면 덮어쓴다. 프로젝트를 막 만든 깨끗한 때에만 한다(예: Odin 같은 필수 에셋까지 설치한 직후).

### 되돌리기가 하는 일
| 대상 | 처리 |
|---|---|
| 기록에 없는 파일(새로 만든 스크립트·에셋·씬·패키지 폴더) | 치운다 |
| 기록과 내용이 다른 파일(바꾼 설정·씬) | 기록의 것으로 되돌린다 |
| 기록에는 있는데 지운 파일 | 다시 만든다 |
| 저장 파일(`persistentDataPath/MomoiProject` 전체 — 슬롯 `slots/`·`Replays/`·`server/`·`Tests/` 포함) | 치운다 |
| 프로젝트 폴더의 시스템 파일(`CLAUDE.md`·`.claude/`·`Docs/PlanningSpec.md`·`Docs/Decisions.md`·`steam_appid.txt`) | 치운다(AI가 예전 결정을 따르지 않게). 'Claude Code 파일 › 맞추지 않기'를 켰으면 Claude 파일은 그대로. `Docs/Source` 등 Docs의 다른 파일은 그대로 |
| PlayerPrefs | 모두 지운다 |
| 그대로 두는 것 | `Packages/com.momoi.gbible`(시스템 자신), `ProjectSettings/ProjectVersion.txt`, `Library`·`UserSettings` |

- 확인 창이 두 번 뜬다. 첫 창에 치울·되돌릴·다시 만들 파일 수와 일부 경로가 보인다.
- 치우거나 덮어쓴 것은 지우지 않고 모두 `MomoiProjectBackups/<시각>/`에 옮긴다. 되돌리기를 되돌리려면 Unity를 닫고 그 폴더의 Assets·ProjectSettings·Packages(와 `CLAUDE.md`·`.claude`·`Docs`·`steam_appid.txt` — 있으면)를 프로젝트 폴더에 덮어쓴다(`_saves`는 저장 파일 — 하위 폴더 그대로).
- 열린 씬·DB의 저장 안 된 변경은 먼저 묻는다(저장·버리기·취소). 그 뒤 열린 씬은 빈 씬으로 바뀐다.
- 끝나면 Unity를 다시 연다(ProjectSettings·패키지를 깨끗이 다시 읽도록). 처음 상태에는 시작 설정을 하기 전이라 시작 설정 창이 다시 뜬다.
- 플레이 중에는 할 수 없다.

### 기록이 없을 때
처음 상태 기록이 없으면 **시스템이 만든 것만** 치울지 묻는다: `Assets/MomoiProject` 전체(코드·DB 데이터·생성 코드·UI·씬), `ProjectSettings/MomoiProject`, 프로젝트 폴더의 시스템 파일(위 표), 모듈 심볼(`GBIBLE_…`), 저장 파일, PlayerPrefs. 그 밖에 개발자가 더한 에셋·패키지·설정은 남는다. 치운 것은 마찬가지로 백업 폴더에 옮긴다.

### 주의할 점
- 처음 기록하기 전에 바꾼 것은 "처음 상태"에 들어가 버린다. 깨끗할 때 기록됐는지 `MomoiProjectBaseline/baseline.json`의 시각으로 확인한다.
- 큰 에셋이 많으면 옮기는 데 시간이 걸린다(같은 드라이브 안에서 옮기기라 복사보다는 빠르다).
- 버전 관리(git 등)를 쓰면 `MomoiProjectBaseline/`은 팀이 같이 쓰도록 올리고, `MomoiProjectBackups/`는 올리지 않는 편이 낫다.

## 9. 설정 순서

새 프로젝트를 손으로 설정한다면 이 순서다(지금은 AI가 시작 설정 뒤 진행 매뉴얼의 단계 1에서 한다).

1. DB를 한 번 연다(GameDatabase 생성).
2. 키워드 그룹을 만들고 필드에 연결한다(`21-Editor-Database.md` 키워드 탭).
3. 플레이어 데이터를 만든다(ID·구역·값 형식).
4. 필요하면 스프라이트 틀, 로그, 도전과제, 인스턴스를 만든다.
5. 저장(Ctrl+S) → `GameIds.cs`가 만들어졌는지 본다.
6. 사이드바 아래가 "검증 통과"인지 본다(오류가 있으면 빌드가 막힌다).
7. 플레이하며 모니터로 확인한다(`22-Monitor-Debug.md`).

---

## 11. Claude Code 파일

**한 줄 요약.** 시작 설정을 적용하면 프로젝트 루트에 AI(Claude Code)가 쓸 파일을 만든다. 에디터를 열 때마다 패키지 경로·템플릿이 바뀌었으면 다시 맞춘다(메뉴 `MomoiProject → Claude Code 파일 → 지금 맞추기`).

| 파일 | 하는 일 | 다시 맞출 때 |
|---|---|---|
| `CLAUDE.md` | Claude Code가 세션마다 읽는다. 관례(`ProjectConventions.md`)·참조(`Reference.md`)를 `@가져오기`로 불러온다 | `<!-- MomoiProject:begin -->`~`end` 칸만 다시 쓴다. 칸 밖에 쓴 것은 그대로 |
| `.claude/skills/think-momoi/SKILL.md` | 스킬 `/think-momoi`: 진행 매뉴얼(`ProjectFlow.md`)의 단계대로 — 0 흡수(자료 → 명세서·예외 잡기·질문) → 1 시스템층 → 2 콘텐츠 → 3 레벨 디자인 지원 → 4 연출 → 5 밸런싱·테스트 → 6 출시 준비, 결정 기록을 읽고 이어 간다 | 통째로 |
| `.claude/hooks/compile-check.ps1` · `.claude/settings.json` | 응답을 끝낼 때(Stop 훅) 프로젝트 코드를 컴파일한다. 오류가 있으면 Claude가 고친 뒤 다시 끝낸다 | 스크립트는 통째로, settings.json은 훅 한 줄이 없을 때만 더한다 |
| `Docs/PlanningSpec.md` | 기획 명세서 빈 양식(AI가 자료로 채우고 질문한다, 개발자가 채워 와도 된다) | 없을 때만 |
| `Docs/Decisions.md` | 결정 기록(AI가 질문·답·추천값으로 정한 것을 쌓는다 — `D-n`) | 없을 때만 |

- 템플릿은 패키지 `Documentation~/AI/Templates`, 컴파일 검사는 `Tools~/compile-check.ps1`이다. `{{GBIBLE}}` 자리에 프로젝트에서 본 패키지 경로(`Packages/com.momoi.gbible` 또는 `Library/PackageCache/com.momoi.gbible@…`)가 들어간다.
- **컴파일 검사**: Unity가 만든 `.csproj`(Preferences › External Tools의 프로젝트 파일)의 참조·심볼을 빌려 `Assets` 아래 asmdef마다 그 폴더의 `.cs`를 직접 컴파일한다(Unity를 연 채로도, 방금 만든 파일도). 마지막으로 통과한 뒤 바뀐 파일·폴더가 없으면 바로 끝난다. Unity 설치 폴더는 `ProjectVersion.txt`의 버전으로 Hub 폴더에서 찾는다(못 찾으면 환경 변수 `MOMOI_UNITY_EDITOR_DATA`에 `…\Editor\Data`). csproj가 아직 없는 어셈블리는 건너뛰고 알린다. 같은 오류로 세 번 넘게 막지는 않는다. 손으로: `powershell -NoProfile -ExecutionPolicy Bypass -File .claude/hooks/compile-check.ps1 -Always`.
- **맞추지 않기**: 메뉴 `MomoiProject → Claude Code 파일 → 맞추지 않기`(표시 `ProjectSettings/MomoiProject/ClaudeFiles.off`). 켜면 만들지도 고치지도 않는다. 이미 있는 파일은 그대로다.
- `.claude/settings.json`을 읽지 못하면(손으로 쓴 JSON 오류) 건드리지 않고 콘솔에 경고한다. 그때는 `hooks.Stop`에 위 명령을 직접 넣는다.
