# 34. Steam

**한 줄 요약**: 선택 기능 **Steam**은 Steamworks.NET 위의 얇은 층이다. 설정은 **DB › Steam** 항목 하나에 두고, 게임이 켜지면 저절로 Steam에 붙는다(초기화·매 프레임 콜백·끌 때 정리). GBible 도전과제를 달성하면 Steam 도전과제가 달성되고, 연결한 로그 값은 Steam 통계로 올라간다. 게임 코드는 Steamworks를 직접 부르지 않고 `GameSteam`만 쓴다(리치 프레즌스·이름 등). 저장 동기화(Steam Cloud)는 코드 없이 Steamworks 설정만으로 한다.

**언제 쓰는가**

- Steam으로 출시하는 PC 게임(이 라이브러리의 고정 플랫폼).
- 도전과제·통계를 Steam 프로필에 보이고 싶을 때.
- 친구 목록에 "3층 · 전투 중" 같은 상태를 보이고 싶을 때.
- 다른 컴퓨터에서 이어 하기(Steam Cloud).

이 문서에서 쓰는 용어:

| 용어 | 뜻 |
|---|---|
| Steamworks.NET | Steam의 C++ API를 C#으로 그대로 옮긴 오픈 소스 라이브러리. Unity 패키지 `com.rlabrecque.steamworks.net`. |
| App ID | Steamworks에서 받은 게임 번호. 출시 전 시험은 Valve의 시험용 앱 **480**(Spacewar). |
| API Name | Steamworks에 적은 도전과제·통계의 코드 이름(`ACH_WIN_ONE_GAME`). GBible은 도전과제 ID를 그대로 API Name으로 쓰고, 다르면 표로 잇는다. |
| 리치 프레즌스 (Rich Presence) | 친구 목록·프로필에 보이는 "지금 하는 것". 키-값 쌍이고, `steam_display` 키는 Steamworks에 올린 현지화 문장(토큰)을 고른다. |
| Steam Cloud (Auto-Cloud) | Steam이 지정한 폴더의 파일을 계정에 올리고 다른 컴퓨터에 내려 주는 기능. 코드 없이 Steamworks 설정만으로 켠다. |

---

## 1. 준비

1. **패키지**: `Packages/manifest.json`의 `dependencies`에 Steamworks.NET을 넣는다(버전 태그를 고정한다. 내려받기라 AI는 먼저 묻는다 — 관례 0절).
   ```json
   "com.rlabrecque.steamworks.net": "https://github.com/rlabrecque/Steamworks.NET.git?path=/com.rlabrecque.steamworks.net#2025.164.1"
   ```
   Unity가 열 때 받는다. 프로젝트 폴더의 `steam_appid.txt`는 없으면 패키지의 에디터 코드와 GBible(Steam 기능이 컴파일될 때 — DB › Steam의 App ID, 없으면 480)이 만든다.
2. **켜기**(선택 기능 ID `steam`): AI는 `ProjectSettings/MomoiProject/Features.json`의 `enabled`에 `steam`을 더하고(명세서 6절에 켠 것만 — 관례 0절), 개발자는 `MomoiProject → DB` 사이드바 "Steam" 그룹의 **기능 활성화**를 누른다. Unity가 다시 컴파일할 때 스크립팅 심볼 `GBIBLE_STEAM`과 프로젝트 asmdef 참조(Client·Editor에 `GBible.Steam`)가 저절로 맞춰지고(`20-Setup.md` 6절), `GameSteam`을 쓰는 코드는 그 컴파일이 끝난 뒤에 쓴다. Steamworks.NET이 없으면 이 기능의 코드는 컴파일되지 않는다(어셈블리 `GBible.Steam`은 심볼 `GBIBLE_STEAMWORKS` — 패키지가 있을 때 저절로 — 도 있어야 컴파일된다).
3. **DB › Steam**(그룹 개발) 항목은 하나만 두는 설정이라, 기능이 컴파일된 뒤 에디터가 기본값(App ID 480)으로 저절로 만든다(콘솔 "[MomoiProject] DB › Steam 항목을 기본값으로 만들었습니다(프로젝트에 하나만 두는 설정)."). 여럿이면 첫 항목만 쓴다. 항목이 없으면 게임 시작 때 경고 하나를 남기고 Steam을 켜지 않는다.
4. 에디터·개발 빌드는 Steam 클라이언트를 켜 둔 채 실행한다. 게임이 그 계정으로 App ID에 붙는다.

> 빌드 대상이 PC(Windows·macOS·Linux)가 아니면 Steamworks.NET이 꺼지고(`DISABLESTEAMWORKS`), `GameSteam`은 아무것도 하지 않는다.

## 2. 동작 규칙

### 2-1. 켜고 끄기

- 화면 세션이 시작될 때(`GBibleRuntime.Started` — 시험 모드 서버·매칭 방처럼 따로 만든 세션에는 오지 않는다) 한 번 Steam을 켠다. 슬롯을 바꿔 세션이 다시 시작돼도 Steam은 다시 켜지 않고, 도전과제·통계만 새 세션에 다시 잇는다.
- 화면 없는 실행(Dedicated Server 빌드·`-batchmode`로 켠 빌드 — `GBibleRuntime.IsHeadless`)에서는 켜지 않는다. 서버에는 Steam 클라이언트가 없고, 출시 빌드의 '다시 시작'이 서버를 끄지 않게 한다.
- **Steam으로 다시 시작**(설정, 기본 켬): **출시 빌드**를 Steam 밖에서(exe를 바로) 켜면 `SteamAPI.RestartAppIfNecessary`로 Steam이 게임을 다시 켜고 지금 실행은 끝난다. App ID가 480이면 하지 않는다(Spacewar가 켜지고 이 게임은 꺼지기만 한다 — DB 검증이 경고). 에디터·개발 빌드는 하지 않고 바로 붙는다:
  - 에디터: 프로젝트 폴더의 `steam_appid.txt`.
  - 개발 빌드: 빌드에 든 DB › Steam의 App ID를 이 프로세스의 환경 변수 `SteamAppId`·`SteamGameId`로 넣고 붙는다. `steam_appid.txt`를 빌드 폴더에 복사하지 않는다 — 그 파일은 '현재 작업 폴더'에서 찾으므로 바로 가기·다른 폴더에서 켜기·macOS `.app`에서는 못 찾고, 빌드 폴더에 남으면 출시 빌드로 새어 나간다. Steam이 켠 실행(환경 변수가 이미 있음 — 베타 브랜치에 올린 개발 빌드 등)은 그대로 둔다.
- Steam에 붙지 못하면(클라이언트가 꺼짐·App ID가 다름) 경고 하나를 남기고 Steam 없이 돈다. Steam 라이브러리(`steam_api` 네이티브 파일)가 없거나 플랫폼·버전이 맞지 않아도(`DllNotFoundException` 등) 원인을 적은 경고 하나로 같다 — 게임 시작은 끊기지 않는다. 이때 `GameSteam.IsReady`가 거짓이고 모든 호출이 아무것도 하지 않는다 — 게임 코드는 분기하지 않아도 된다.
- 매 프레임 `SteamAPI.RunCallbacks`, 끌 때 남은 통계를 올리고 `SteamAPI.Shutdown`. 오브젝트 `[MomoiProject Steam]`(DontDestroyOnLoad)이 돌린다.

### 2-2. 오버레이·언어

- **오버레이가 열리면 일시정지**(기본 켬): Shift+Tab으로 Steam 오버레이가 열리면 `GameTime.Pause("Steam 오버레이")`, 닫히면 재개(다른 이유의 일시정지와 겹치면 각자 풀어야 재개 — 7장).
- **처음 실행 언어는 Steam 언어**(기본 켬): 플레이어가 언어를 고른 적이 없으면(`GameLanguage.HasSavedChoice`도 `GameSettings.HasSavedLanguage`도 거짓) Steam 클라이언트의 언어로 시작한다(`koreana` → `ko`, `schinese` → `zh-Hans` …). 지원하지 않는 언어면 가장 가까운 언어(`LocalizationSettings`)로. 기억하지는 않는다(Steam 언어를 바꾸면 다음 실행에 따라감).

### 2-3. 도전과제

- **도전과제를 Steam에 올리기**(기본 켬): GBible 도전과제(14장)를 달성하는 순간 같은 API Name의 Steam 도전과제를 달성하고 바로 올린다(`StoreStats`). 켤 때 이미 달성해 둔 것도(오프라인에서 달성 등) Steam에 없으면 올린다.
- API Name = 도전과제 ID. 다르면 **도전과제 이름(ID와 다를 때)** 표에 (도전과제 · Steam 이름)을 적는다.
- Steamworks에 그 이름이 없으면 경고 한 번("Steam 도전과제 'X'을(를) 달성하지 못했습니다…"). GBible 쪽 달성은 그대로다.
- 미리보기(33장)에서는 도전과제가 달성되지 않으므로 Steam에도 올라가지 않는다.

### 2-4. 로그 → Steam 통계

- **로그 → Steam 통계** 표: (로그 · Steam 통계 이름 · 정수(INT)). 로그(15장) 값이 바뀌면 모아 두었다가 **2초마다** `SetStat` + `StoreStats`로 올린다. 켤 때 한 번 전부 올린다.
- 정수면 반올림한 `int`, 아니면 `float`. Steamworks의 통계 형식(INT/FLOAT)과 맞춘다(틀리면 경고 한 번). `int` 범위(±2,147,483,647)를 넘는 값은 끝값으로 자르고 경고 한 번(큰 값은 FLOAT 통계로), 숫자가 아닌 값(NaN)은 올리지 않는다.
- Steam은 되돌리지 않는다. Undo로 로그 값이 줄면 줄어든 값이 올라간다(Steamworks에서 "증가만"으로 둔 통계는 Steam이 거절한다).
- 미리보기는 데이터 변경 알림을 보내지 않으므로 올라가지 않는다.

## 3. 에디터 — DB › Steam

| 칸 | 기본 | 뜻 |
|---|---|---|
| `appId` App ID | 480 | Steamworks 앱 ID. 고쳐 저장하면(DB Ctrl+S 포함) 프로젝트 폴더의 `steam_appid.txt`도 같이 고친다(에디터가 그 앱에 붙음). 개발 빌드는 빌드에 든 이 값으로 붙는다(2-1). |
| `restartThroughSteam` Steam 밖에서 켜면 Steam으로 다시 시작 | 켬 | 출시 빌드에서만, App ID 480이면 하지 않음(2-1). |
| `pauseOnOverlay` 오버레이가 열리면 일시정지 | 켬 | 2-2. |
| `languageFromSteam` 처음 실행 언어는 Steam 언어 | 켬 | 2-2. |
| `achievements` 도전과제를 Steam에 올리기 | 켬 | 2-3. |
| `achievementNames` 도전과제 이름(ID와 다를 때) | 비어 있음 | 줄마다 도전과제 · Steam 이름(API Name). |
| `stats` 로그 → Steam 통계 | 비어 있음 | 줄마다 로그 · Steam 통계 이름 · 정수(INT). |

검증: App ID 0, 같은 도전과제를 두 번 적음, 도전과제를 고르지 않은 줄, 로그를 고르지 않은 줄, Steam 통계 이름이 빈 줄, 같은 Steam 통계에 로그 둘 → 오류. App ID 480인데 '다시 시작'이 켜짐 → 경고(출시 전에 실제 App ID로 — 빌드는 막지 않는다).

모니터 › 명령 탭(개발 빌드): **Steam › Steam 통계·도전과제 모두 지우기** — 시험용으로 이 계정의 이 앱 Steam 통계·도전과제를 모두 지운다(GBible 쪽은 그대로). 다시 달성을 시험할 때 GBible 쪽은 모니터 › 도전과제의 초기화를 쓴다.

## 4. 코드 — `GameSteam`

| 멤버 | 뜻 |
|---|---|
| `bool IsReady` | Steam에 붙었다. 거짓이면 아래 호출은 모두 아무것도 하지 않는다(빈 값을 돌려줌). |
| `event Action Ready` | 초기화가 끝났을 때(성공한 경우만). |
| `bool SetPresence(string key, string value)` | 리치 프레즌스 한 칸. 실패하거나 Steam이 없으면 `false`. |
| `void ClearPresence()` | 리치 프레즌스를 모두 지운다. |
| `string PlayerName` | Steam 이름(없으면 `""`). |
| `ulong SteamId` | 64비트 Steam 사용자 ID(없으면 0). |
| `string Language` | Steam 클라이언트 언어(BCP-47, 없으면 `null`). |
| `void StoreNow()` | 모아 둔 통계·도전과제를 다음 프레임에 바로 올린다(보통은 저절로). |

게임 규칙은 Steam을 모른다: 도전과제·통계는 GBible의 도전과제·로그로 만들고, Steam은 그것을 따라 올라간다.

**친구 방(멀티)**: `SteamLobby.Create(인원)`·`Invite()`·`Join(로비)`·`Leave()` — Steam 로비 + Steam Datagram Relay로 방장에게 접속한다(사설 호스트 멀티, 40장). Steam이 준비되면 `GameNet.Rooms`가 Steam 로비로 바뀌므로 화면은 `GameNet.CreateRoom`·`InviteToRoom`만 불러도 된다.

**인증 티켓(매칭큐)**: Steam이 준비되면 접속할 때마다 Steam 웹 API용 인증 티켓을 받아 입장 표로 보낸다(`SteamTickets.Request` → `GameNet.TokenSource`). 매칭큐 서버가 `SteamWebAuthenticator(퍼블리셔 키, App ID)`로 확인한다 — 퍼블리셔 키는 자기 앱(Steam Direct)이 있어야 나온다. 시험 앱 480으로는 로비·릴레이까지만 시험할 수 있다.

**아직 없는 것**: 리더보드(Leaderboard — Steam이 보관하는 점수 순위표). `GameSteam`에 해당 멤버가 없다.

## 5. 상황별 예제

### 5-1. 처음 붙여 보기(App ID 480)

1. Steam 클라이언트를 켠다. 선택 기능 Steam을 켠다(DB › Steam 항목은 App ID 480으로 저절로 생긴다 — 1절 3).
2. 플레이 → 콘솔에 "[MomoiProject] Steam에 붙었습니다: <Steam 이름> · App ID 480. …"이 찍히고 Steam 친구 목록에 "Spacewar 플레이 중"이 보이면 붙은 것이다. 오버레이(Shift+Tab)는 에디터에서는 뜨지 않으므로 빌드에서 확인한다(열리면 게임이 멈춘다).
3. 480에는 이 게임의 도전과제가 없으므로 도전과제를 달성하면 "Steam 도전과제 '…'을(를) 달성하지 못했습니다" 경고가 한 번 나온다(정상). 실제 App ID로 바꾼 뒤 Steamworks에 도전과제를 만든다.

### 5-2. 도전과제 잇기

- Steamworks › 도전과제에 API Name을 GBible 도전과제 ID와 같게 만든다(`firstWin` ↔ `firstWin`). 그러면 표에 적을 것이 없다.
- 이미 다른 이름으로 만들어 두었으면 DB › Steam › 도전과제 이름 표에 (`firstWin` · `ACH_WIN_ONE_GAME`)를 적는다.
- 달성 시험: 모니터 › 도전과제의 강제 달성 → Steam 오버레이에 알림. 다시 시험하려면 명령 탭의 "Steam 통계·도전과제 모두 지우기" + GBible 도전과제 초기화.

### 5-3. 통계 잇기

로그 `kills`(처치 수, 합계)를 Steam 통계 `TOTAL_KILLS`(INT)에: DB › Steam › 로그 → Steam 통계에 (`kills` · `TOTAL_KILLS` · 정수 켬). 적을 처치할 때마다 로그가 오르고, 2초 안에 Steam에 올라간다. Steamworks 도전과제를 "통계가 100 이상이면 달성"으로 두면 진행도 막대가 Steam에 보인다.

### 5-4. 리치 프레즌스

```csharp
// 화면 쪽(씬이 바뀔 때·전투 시작 등). 규칙 코드가 아니다.
GameSteam.SetPresence("floor", run.floor.ToString());
GameSteam.SetPresence("steam_display", "#Status_Battle");   // Steamworks 현지화 파일의 토큰
```
Steamworks › 리치 프레즌스 현지화 파일(언어마다):
```
"lang" { "Language" "korean" "Tokens" { "#Status_Battle" "{#floor}층 · 전투 중" } }
```
`{#floor}`처럼 다른 키 값을 끼워 넣는다. 토큰을 올리지 않으면 친구 목록에 보이지 않는다.

### 5-5. Steam Cloud — 코드 없이 저장 동기화

GBible의 저장은 `Application.persistentDataPath/MomoiProject/` 아래 파일이다(6장·31장). Steamworks › 앱 관리 › Steam Cloud에서:

1. 사용자당 바이트 할당량·파일 수를 정한다(예: 10MB, 50개).
2. **Auto-Cloud** 루트 경로를 추가한다.
   | 칸 | 값 |
   |---|---|
   | 루트 | `WinAppDataLocalLow` |
   | 하위 폴더 | `<회사 이름>/<제품 이름>/MomoiProject` (Player Settings의 Company Name·Product Name) |
   | 패턴 | `*.json` |
   | 하위 폴더까지 | 예 (`slots/<이름>/…`도) |
   | 제외 | `settings.json` (설정은 기기마다 하나 — 해상도·음량이 컴퓨터를 따라다니지 않게) |
3. macOS·Linux도 내면 루트 재정의를 더한다(macOS `MacAppSupport` + `<회사>/<제품>/MomoiProject`, Linux `LinuxXdgDataHome`… Unity의 `persistentDataPath`가 가리키는 자리와 맞춘다).

게임이 켜지기 전에 Steam이 내려 주고 끈 뒤에 올리므로, 게임 코드는 그대로 파일을 읽고 쓴다. 두 컴퓨터에서 같은 슬롯을 따로 진행하면 Steam이 충돌 창을 띄운다.

## 6. 주의할 점

| 증상 | 원인과 해결 |
|---|---|
| "Steam에 붙지 못해 Steam 없이 시작합니다" | Steam 클라이언트가 꺼져 있거나, App ID(에디터는 프로젝트 폴더의 `steam_appid.txt`, 개발 빌드는 DB › Steam의 App ID)가 계정이 가진 앱이 아니다. 480은 누구나 쓸 수 있다. |
| "Steam을 켜지 못해 Steam 없이 시작합니다: Steam 라이브러리(steam_api)를…" | Steamworks.NET 패키지의 네이티브 파일(`steam_api64.dll`·`libsteam_api.so`·`steam_api.bundle`)이 빌드에 없거나 플랫폼·CPU·버전이 맞지 않는다. 패키지 버전과 Plugins의 플랫폼 설정을 확인한다. |
| "DB › Steam 항목이 없어 Steam을 켜지 않습니다" | 선택 기능은 켰는데 DB › Steam에 항목이 없다(기능이 아직 컴파일되지 않았거나 항목을 지웠다). 에디터가 다시 컴파일한 뒤 기본값으로 만든다(1절 3) — 빌드 전에 항목이 있는지 본다. |
| 도전과제 달성 경고("달성하지 못했습니다") | Steamworks에 그 API Name이 없거나 아직 게시(Publish)하지 않았다. ID를 맞추거나 표에 적고, Steamworks에서 변경 사항을 게시한다. |
| 통계 경고("올리지 못했습니다") | 통계 이름이 없거나 형식(INT/FLOAT)이 '정수' 칸과 다르다. |
| exe를 바로 켰더니 꺼졌다가 Steam으로 다시 켜짐 | 출시 빌드의 '다시 시작'(2-1)이 정상으로 도는 것. 끄려면 `restartThroughSteam`을 끈다(Steam DRM이 필요 없는 배포처 등). |
| 오버레이를 닫았는데 게임이 안 움직임 | 다른 이유의 일시정지(메뉴·다이얼로그)가 남아 있다. `GameTime.IsPaused`·모니터로 이유를 본다. |
| 컴퓨터를 바꿨더니 설정(해상도)이 초기화 | 의도한 것이다(Auto-Cloud 제외에 `settings.json`). 따라다니게 하려면 제외에서 뺀다. |
| Steam 쪽 통계가 Undo로 줄어든 값이 됨 | Steam은 되돌리지 않는다(2-4). 줄면 안 되는 통계는 GBible 로그를 '최고치'로 두거나 Steamworks에서 증가만 허용한다. |
