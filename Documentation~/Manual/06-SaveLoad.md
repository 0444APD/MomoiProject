# 06. 저장·불러오기

**한 줄 요약**: 플레이어 데이터는 구역별로 두 파일에 저장된다. **프로필**(메타 구역: 재화·해금·도전과제·누적 로그)과 **판**(세션 구역: 이번 판 내용 + 난수 상태 + 게임 시간)이다. 싱글 게임은 `GBibleRuntime`이 시작할 때 프로필을 불러오고, 끌 때 둘 다 저장한다. 판을 끄기 전에도 저장할 때(행동마다·N초마다·이벤트를 받을 때)는 DB › 시작 파일 › Bootstrap에서 고른다(저장 코드를 쓰지 않는다). 판 이어하기는 게임이 원하는 때 `LoadRun`을 부르거나, 타이틀이 없는 게임은 Bootstrap의 **시작할 때 이어 하기**를 켠다.

**언제 쓰는가**

- 타이틀 화면의 "이어하기"·"새 게임"
- 판이 끝났을 때 프로필은 남기고 판만 지우기
- 실시간·방치형의 주기 저장, 하루 끝·체크포인트 저장
- 화면 쪽 상태(플레이어 위치 등)를 저장 직전에 판 데이터에 담기
- 출시 후 데이터 구조를 바꿀 때 예전 저장을 이어받기(버전 변환)
- 서버·테스트에서 세션 상태를 JSON 글자로 주고받기

---

## 0. 용어

| 용어 | 뜻 |
|---|---|
| 플레이어 데이터 | 세션의 저장소(`session.Data`)에 ID별로 담긴 값. [01-PlayerData.md](01-PlayerData.md) |
| 구역 (`DataZone`) | `Session`(이번 판) 또는 `Meta`(프로필). 저장 파일이 다를 뿐 쓰는 법·Undo 규칙은 같다. |
| 프로필 | 메타 구역 전체. `ExportMeta`/`ImportMeta`. 파일 `profile.json`. |
| 판(런) | 세션 구역 전체 + 난수 상태 + 게임 시계. `ExportRun`/`ImportRun`. 파일 `run.json`. |
| 저장 트리 | 저장 직전의 중간 모습. 키 → 값, 클래스는 "필드 이름 → 값" 딕셔너리, 리스트는 리스트. JSON으로 바뀐다. |
| 저장 버전 | 저장 파일에 적는 정수. 등록한 변환의 가장 높은 버전을 저절로 따라간다(`session.SaveVersion`, 변환이 없으면 1). |
| 변환(마이그레이션) | 예전 버전의 저장 트리를 지금 구조로 고치는 한 단계(`SaveMigrations`). 키·경로만 받는 변환은 DB › 개발 › 저장 변환 표에, 값을 계산하는 변환은 코드에 쓴다. |
| 새 판 | 이번 판 값을 처음 값으로 되돌리고 판 저장 파일도 지우는 것(`GBibleRuntime.NewRun(session)`). 프로필은 그대로. |
| 슬롯 | 저장 묶음(프로필 + 판) 하나. 슬롯마다 폴더가 따로다(2.1, `31-Slots-Settings-Input.md`). |
| 개발 중 / 출시됨 | 프로젝트 상태. 개발 중에는 호환되지 않는 저장을 초기화하고(그 파일을 옆으로 옮긴다), 출시 후에는 이어받는다. |

---

## 1. 에디터에서 쓰는 법

### 1.1 개발 중 · 출시됨 전환

메뉴 **MomoiProject › 개발 › 출시됨 (저장 호환 유지)**를 누르면 체크 표시가 바뀐다. 기본은 체크 없음(개발 중).

| 상태 | 게임 시작 때 저장 파일의 데이터 구조가 지금과 다르면 | ID 바꾸기 |
|---|---|---|
| 개발 중 | 키·필드가 없어졌거나 이름·형식이 바뀌었거나 저장된 enum 값 이름이 없어졌으면, 변환하지 않고 **맞지 않는 그 파일만 초기화한다**: 지우지 않고 `이름.reset-<시각>.json`(예: `run.reset-20261007-101500.json`)으로 옆에 옮긴다 — 판 파일 하나 때문에 멀쩡한(지금 구조와 맞는) 프로필까지 잃지 않는다. 파일마다 그 파일에 남은 구조 전체(판·프로필 키 모두)를 지금과 견주므로, 같은 때 같은 구조로 저장된 두 파일은 함께 옮겨진다. 콘솔·기록에 "개발 중 데이터 구조가 바뀌어 run.json을(를) 초기화했습니다: 이유. 원래 파일은 …. (출시 후에는 초기화하지 않고 이어받습니다)" 경고가 남는다. 새 키·필드를 **더하기만** 했으면 그대로 이어간다 | 이력을 남기지 않는다 |
| 출시됨 | 초기화하지 않는다. 구조 변경은 저장 버전 + 변환(1.4 · 2.7)으로 이어받는다 | 예전 ID → 새 ID 이력을 남겨 런타임이 자동으로 이어받는다 |

저장 파일에는 저장 당시의 데이터 구조(DB의 플레이어 데이터·로그·도전과제의 키, 필드 경로와 형식, enum 값 이름)가 함께 남는다. 구조 정보가 없는 예전 파일은 호환으로 본다.

### 1.2 모니터에서 저장 다루기

플레이 중 **MomoiProject › 모니터**:

- **데이터** 탭: **판 데이터**("이번 판 내용(세션 구역) · 판 저장에 들어감")와 **프로필 데이터**("판이 바뀌어도 유지(메타 구역) · 프로필 저장에 들어감")를 나눠 보여 준다.
- **명령** 탭의 **저장** 묶음:

| 명령 | 하는 일 |
|---|---|
| 프로필 저장 | 지금 프로필을 `profile.json`에 쓴다 |
| 프로필 삭제 | `profile.json`을 지운다(다음 플레이부터 적용) |
| 판 저장 | 진행 중인 판(세션 구역·난수·시간)을 `run.json`에 쓴다 |
| 판 불러오기 | `run.json`을 불러와 이어서 진행한다 |
| 새 판 | 이번 판 내용을 처음 값으로 되돌리고 판 저장을 지운 뒤 Bootstrap의 **새 판 행동**을 실행한다(`GBibleRuntime.StartNewRun`, 2.3) |

### 1.3 ID 바꾸기

DB 상세의 제목(ID)을 눌러 ID를 바꾸면 참조가 모두 따라가고, 저장(Ctrl+S)할 때 적용된다. 출시됨 상태라면 플레이어 데이터·로그·도전과제의 예전 ID로 저장된 값을 새 ID로 이어받는다(런타임이 시작할 때 별칭으로 등록). 개발 중이면 이력이 없으므로 다음 플레이에서 그 저장이 초기화된다.

### 1.4 저장 변환 표: DB › 개발 › 저장 변환

출시 후 저장 구조를 바꿨을 때, 키·경로만으로 정해지는 변환은 기본으로 코드 대신 DB 표에 적는다. DB 사이드바 그룹 **개발**의 **저장 변환** 탭(`SaveMigrationData`). 항목 하나 = 변환 한 단계. AI는 DB 반영 스크립트(`[DbSeed]`)로 이 항목을 만든다(관례 15절).

| 칸 | 뜻 |
|---|---|
| 버전 | 이 버전으로 올릴 때 적용한다(버전 - 1 → 버전). 2부터(처음 출시가 버전 1) |
| 종류 | **필드 옮김** / **키 이름 바꿈** / **키 삭제** |
| 키 | 저장 파일 안의 플레이어 데이터 키(예전 이름). 예: `run` |
| 예전 경로 · 새 경로 | 필드 옮김일 때. 값 안의 필드 경로. 리스트의 모든 요소는 `items[].power`처럼 `[]` |
| 새 키 | 키 이름 바꿈일 때 |

- 게임이 켜질 때 버전 순서(같은 버전은 ID 글자 순서 — `migration10`이 `migration2`보다 앞이다. DB 목록에 보이는 차례와 다를 수 있다)로 `options.Migrations`에 등록되고, 그다음 코드의 `Configuring`이 더 등록할 수 있다. 같은 버전 안에서 순서가 중요하면(키 이름을 바꾼 뒤 그 키의 필드 옮김) 버전을 나눈다. 저장 버전은 등록된 변환(표 + 코드)의 가장 높은 버전을 저절로 따라간다.
- 표의 세 종류는 코드의 `MoveField`·`RenameKey`·`RemoveKey`(2.7)와 같다. 값을 계산해 바꾸는 변환(`Migrations.Add`)만 코드로 쓴다.
- 빈 키, 2보다 작은 버전, 빈 경로·새 키, 앞뒤 공백이 있는 키, 키와 같은 새 키, 경로 모양(빈 칸 · 공백 · 요소 번호 `[0]` · 마지막 칸의 `[]` · 같은 두 경로 · 리스트 요소 안에서 다른 리스트로처럼 옮길 수 없는 모양 — `SaveMigrations.CheckMove`)은 DB 검사가 오류로 알린다. 경로 모양이 맞아도 저장 파일에 그 키는 있는데 예전 경로를 하나도 찾지 못하면(오타·키의 값이 리스트) 불러올 때 기록(모니터 타임라인)에 "저장 변환 '…'을 적용하지 못했습니다" 경고가 남는다(그 값은 새 경로로 오지 않는다). 키의 값 자체가 리스트면 경로를 `[].power`처럼 `[]`로 시작한다.
- DB 항목 ID 바꾸기(1.3)는 이 표가 필요 없다.

예(카드 던전): 버전 2 · 필드 옮김 · 키 `run` · `hero.gold` → `gold`, 버전 3 · 키 삭제 · 키 `oldTutorial`.

---

## 2. 코드로 쓰는 법 — 문법 요소

### 2.1 저장 위치: `GBibleRuntime.SavePath` · `RunSavePath`

```csharp
string profilePath = GBibleRuntime.SavePath;      // {Application.persistentDataPath}/MomoiProject/profile.json (기본 슬롯)
string runPath = GBibleRuntime.RunSavePath;        // {Application.persistentDataPath}/MomoiProject/run.json
bool canContinue = GBibleRuntime.HasSavedRun;     // 이어 할 판이 있는가(빈 run.json은 없는 것으로)
```

경로는 **지금 슬롯**의 것이다. 이 장의 저장·불러오기(`SaveProfile`·`SaveRun`·`LoadRun`·`NewRun`·`DeleteRun`)도 모두 지금 슬롯의 파일을 쓴다.

```
<persistentDataPath>/MomoiProject/
  settings.json                      설정(기기마다 하나 — 슬롯과 상관없음)
  profile.json · run.json · slot.json   기본 슬롯("")
  slots/<이름>/profile.json · run.json · slot.json   그 밖의 슬롯
```

- `slot.json`은 슬롯 고르기 화면용 요약(저장 시각·플레이 시간·요약 한 줄)이다. 프로필이나 판을 저장할 때마다 함께 쓴다.
- **안전한 쓰기**: 저장 파일(프로필·판·`slot.json`·설정)은 임시 파일 `이름.tmp`에 끝까지 쓰고 디스크에 내린 뒤 한 번에 바꿔 넣는다. 바로 전 파일은 `이름.bak`(`profile.json.bak`)으로 하나 남는다. 쓰는 도중 꺼지거나 디스크가 차도 옛 저장이 반쪽이 되지 않는다(바꿔 넣기를 못 하는 파일 시스템에서도 `.bak`이 남는다). 깨진 파일·`.bak`만 남은 경우는 2.3.
- 폴더에 더 생길 수 있는 파일: `이름.broken-<시각>.json`(읽지 못한 원본의 사본, 2.3) · `이름.reset-<시각>.json`(개발 중 구조가 맞지 않아 치운 파일, 1.1).
- **설정 파일은 슬롯과 별개다.** 음량·언어·바꾼 키는 플레이어 데이터가 아니라 `settings.json` 하나에 남아, 슬롯을 바꾸거나 판을 지워도 그대로다(`GameSettings`). 없으면(처음 켬) 조용히 기본값으로, 깨졌으면 `.bak`으로 시작한다(31장 2-3).
- 슬롯 바꾸기·지우기·복사와 요약은 `31-Slots-Settings-Input.md` 2-1·2-2.

### 2.2 자동으로 일어나는 일

| 때 | `GBibleRuntime`이 하는 일 |
|---|---|
| 게임 시작(씬을 불러오기 전) | 세션을 만들고, 개발 중이면 호환되지 않는 저장 파일을 옆으로 옮긴 뒤(1.1), `profile.json`을 불러온다(`ImportMeta`, 깨졌으면 `.bak` — 2.3) |
| 바깥 행동이 끝날 때·Undo 뒤(판 저장 "행동마다 저장"일 때) | 그 프레임 끝(행동 밖)에서 판을 저장한다. 시스템 행동(`[SystemAction]` — 상태 효과 주기·만료·예약·패시브 효과 주기 …)은 세지 않는다: 그 변경은 다음 플레이어 행동·끌 때 함께 저장된다("N초마다 저장"은 시스템 행동도 센다). 싱글에서도 같다 — 프로젝트의 틱 행동(전투 틱·정산)에 `[SystemAction]`을 붙이면 틱마다 판을 쓰지 않는다(붙인 행동은 멀티 서버가 클라이언트에게서 받지 않는다 — 40장) |
| 마지막 저장에서 N초(실제 시간)가 지난 프레임 끝(판 저장 "N초마다 저장"일 때) | 그사이 행동·Undo가 있었으면 판을 저장한다. 아무 일도 없었으면 쓰지 않는다 |
| 정한 이벤트를 쏜 행동이 끝날 때(판 저장 "이벤트를 받을 때 저장"일 때) | 그 프레임 끝에서 판을 저장한다. 그 행동이 실패해 취소되면 저장하지 않는다 |
| 위 셋에서 판을 저장할 때 | 차례는 `Saving` → 프로필 → 판이다. 그사이 프로필(메타 구역) 값이 바뀌었으면(`Saving` 안에서 바뀐 것도) 프로필도 함께 저장한다(까닭은 2.3) |
| 바깥 행동이 끝날 때·Undo 뒤("프로필도 행동마다 저장"을 켰을 때) | 지난 프로필 저장 뒤 프로필(메타 구역 — 누적 로그·도전과제 진행 포함) 값이 바뀌었으면 판 저장 방식과 상관없이 그 프레임 끝에서 프로필을 저장한다("N초마다 저장"이어도 간격을 기다리지 않는다). 프로필을 건드리지 않은 행동(판 안의 전투 행동)에서는 쓰지 않는다. 시스템 행동은 위 줄처럼 세지 않는다("N초마다 저장"일 때는 센다) |
| 판 끝 이벤트를 받은 행동이 끝날 때(설정했을 때) | 판 저장을 지우고 프로필을 바로 저장한다. 그 뒤 다른 플레이어 행동(시스템 행동 제외)이 세션 구역 값을 바꿀 때(= 새 판)나 `NewRun`까지 판을 저장하지 않는다. 판을 끝낸 행동을 Undo하거나 그 행동이 실패해 취소되면 판은 끝나지 않은 것이 된다 |
| 스냅샷 방식 되감기(Undo) 뒤 | 판·프로필을 저장할 것으로 표시한다(저장 시점은 위와 같다). 판 끝 이벤트 앞으로 되감았으면 판은 끝나지 않은 것이 되어 다시 저장한다 |
| 판을 저장하기 직전 · 판을 불러온 뒤 · 끄기 직전 | `Saving` · `RunLoaded` · `Stopping`을 쏜다(2.10) |
| 게임 종료(`OnApplicationQuit`) | `Stopping`을 쏜 뒤, `AutoSaveRun`이 참이고(판 저장이 "저장하지 않음"이 아니면) 판이 살아 있으면 `SaveRun`(`Saving` → 프로필 → 판), 아니면 프로필만 저장한다. 판 끝 이벤트 뒤 아직 새 판이 아니면 판은 저장하지 않는다 |

판은 **자동으로 불러오지 않는다.** 타이틀의 "이어하기"처럼 게임이 원하는 때 `LoadRun`을 부른다. 예외: Bootstrap의 **시작할 때 이어 하기**를 켜면 런타임이 `Started` 뒤에 `LoadRun`하고, 저장된 판이 없으면 **새 판 행동**(매개변수 없는 생성자가 있는 행동 클래스)을 실행한다. 보통 타이틀 화면 없이 바로 판으로 들어가는 게임에서 켠다(20장 3절).

판은 **살아 있을 때만** 저장한다: 이번 실행에서 판을 불러왔거나(`LoadRun`) 새 판을 열었거나(`NewRun`) 성공한 행동이 판 데이터(세션 구역)를 바꿨을 때다. 타이틀에서 판을 읽지 않고 끄거나 슬롯을 바꿔도 저장된 판을 빈 판으로 덮지 않는다.

판 저장 시점은 DB › 시작 파일 › **Bootstrap**에서 정한다(코드 없음): **판 저장**(아래 표) · **판 끝 이벤트**(위 표 — 판 저장을 지우고 프로필을 저장) · **프로필도 행동마다 저장**(기본 끔. 켜면 프로필을 바꾼 행동이 끝난 프레임에만 프로필을 쓴다(위 표). 끄면 프로필은 끌 때·판 끝 때·판과 함께 저장할 때만 쓴다. 판 밖에서 프로필을 바꾸는 일이 있는 게임 — 수집형·로비 뽑기·마을(거점)이 있는 게임 — 은 켠다. 켜도 되나의 기준은 **프로필을 바꾸는 행동이 얼마나 잦은가**다: 간격을 기다리지 않으므로 프로필을 매초 바꾸는 행동(생산을 프로필에 쌓는 1초마다 정산, 매 틱 오르는 누적 로그 …)이 있으면 끈다 — 판 저장 'N초마다'가 그 간격으로 바뀐 프로필도 함께 저장한다. 방치형도 정산이 판 데이터만 바꾸면 켜도 매초 쓰지 않는다(07장 4.7)).

| 판 저장 | 저장하는 때 | 함께 고르는 칸 | 맞는 게임 |
|---|---|---|---|
| 끌 때 저장(기본) | 끌 때만 | — | 이어 하기는 있지만 자주 쓸 필요는 없는 게임 |
| 행동마다 저장(턴제) | 바깥 행동이 끝날 때·Undo 뒤, 그리고 끌 때 | — | 턴제·카드 |
| 저장하지 않음(이어 하기 없음) | 저장하지 않는다 | — | 아케이드·퍼즐 |
| N초마다 저장(실시간·방치형) | 마지막 저장에서 N초가 지났고 그사이 행동이 있었을 때, 그리고 끌 때 | **저장 간격(초)**(기본 30, 1 이상) | 실시간·방치형. 행동이 잦아 매번 쓰기는 낭비인 게임 |
| 이벤트를 받을 때 저장(하루 끝·체크포인트) | 정한 이벤트를 쏜 행동이 끝날 때, 그리고 끌 때 | **판 저장 이벤트** | 생활 시뮬(하루 끝)·체크포인트·방 클리어 |

끌 때 판을 저장할지는 코드로 덮어쓸 수도 있다(코드 값이 우선). 끄기 전의 저장(행동마다·N초마다·이벤트)은 이 값과 상관없이 칸대로 돈다.

```csharp
GBibleRuntime.AutoSaveRun = false;    // 끌 때 판을 저장하지 않는다(설정보다 우선)
```

### 2.3 직접 저장·불러오기: `GBibleRuntime`

```csharp
var session = GBibleRuntime.Session;
GBibleRuntime.SaveProfile(session);         // profile.json 쓰기
GBibleRuntime.SaveRun(session);              // Saving 알림 → profile.json → run.json
bool loaded = GBibleRuntime.LoadRun(session); // run.json이 있으면 불러오고 true
GBibleRuntime.NewRun(session);               // 새 판: 이번 판 값을 처음 값으로 + run.json 지우기
GBibleRuntime.NewRun(session, RandomStreams.SeedOf("ABCD1234"));   // 시드로 새 판(시드 입력·일일 도전: 같은 시드 = 같은 판)
GBibleRuntime.StartNewRun(session);          // NewRun + Bootstrap의 '새 판 행동'(시드도 받는다. 모니터·테스트 패널의 '새 판'이 이것)
GBibleRuntime.DeleteRun();                   // run.json만 지우기(.bak·.tmp도)
```

**`SaveRun`은 프로필도 함께 저장한다.** 차례는 저장 직전 알림(`Saving`) → 프로필 → 판이다(끌 때·슬롯을 복사할 때도). 판만 앞서 저장되면, 꺼진 뒤 이어 할 때 판은 지금 모습인데 프로필은 예전 모습이 되어 쓴 스태미나·메타 재화·새 id 번호(`NewId`)가 되살아난다. `Saving` 안에서 바꾼 메타 값도 이번 프로필에 들어간다. "판을 저장하고 프로필도 저장"을 따로 부르지 않는다.

**실패와 깨진 파일**: 저장·불러오기는 예외를 밖으로 던지지 않고 기록에 남긴다.

- 파일이 비었거나(쓰는 도중 꺼짐·디스크 가득) 읽지 못하면(JSON이 깨짐, 게임보다 새 버전 …) 원본을 `이름.broken-<시각>.json`으로 옆에 복사해 두고 바로 전 저장(`.bak`)으로 이어 간다. 기록 Warning: "판 불러오기 실패: 파일이 비어 있습니다 — 바로 전 저장(.bak)으로 이어 갑니다. 원본은 …에 남겨 둡니다." 바꿔 넣는 사이에 꺼져 `.bak`만 남았어도 `.bak`에서 불러온다. 게임이 켜질 때 프로필도 같다("프로필 불러오기 실패: …").
- `.bak`도 읽지 못하면 기록 Error("판 불러오기 실패: … — 원본은 …에 남겨 둡니다.")를 남기고 `LoadRun`은 `false`를 돌려준다.
- `HasSavedRun`은 빈 `run.json`을 없는 것으로 본다(`run.json`이 없고 `.bak`만 있으면 있는 것으로).
- `DeleteRun`·`NewRun`·`DeleteSlot`·모니터의 프로필 삭제는 `.bak`·`.tmp`도 함께 지운다(지운 저장이 `.bak`에서 되살아나지 않게).

**`GBibleRuntime.NewRun(session, seed = null)`** — 새 판을 여는 한 줄. 다음을 한 번에 한다.

1. 메모리의 판 값(세션 구역)을 처음 값으로 되돌리고 Undo 기록을 비운다(`session.NewRun(seed)`). 프로필은 그대로. `seed`를 주면 마스터 시드와 모든 스트림을 그 시드의 처음 상태로 맞춘다(같은 시드 = 같은 판, `07-Random-Time.md` 2.16). 주지 않으면 난수는 이어서 흐른다.
2. 판 저장 파일 `run.json`을 지운다(`.bak`·`.tmp`도). 화면 세션(`GBibleRuntime.Session`)일 때만 — 다른 세션(시험 모드 서버 등)은 1만 한다.
3. 판 끝 상태(판 끝 이벤트 뒤 "새 판까지 판을 저장하지 않음")를 푼다. 판 끝 이벤트 뒤 아직 하지 않은 프로필 저장이 남아 있으면 먼저 한다. 그래서 판을 끝낸 행동 바로 뒤, 같은 프레임에 `NewRun`을 불러도 그 판의 보상이 프로필에 저장된다.

메모리와 파일을 함께 지워야 "화면은 새 판인데 이어하기는 예전 판"이 생기지 않는다. **행동 밖**(버튼 등)에서 부른다. 행동 안에서 부르면 `InvalidOperationException`("행동 실행 중에는 새 판을 시작할 수 없습니다"). 새 판의 첫 내용(덱·맵)을 만드는 것은 그 뒤에 행동으로 한다 — 그 행동을 Bootstrap의 **새 판 행동**으로 정해 두면 `StartNewRun`이 한 번에 한다.

```csharp
public static void NewGame(GameSession session)
{
    GBibleRuntime.NewRun(session);
    session.Do(new StartRun());     // 새 판 행동(게임이 만든 것)
}
```

### 2.4 글자로 저장·불러오기: `session.ExportMeta` · `ExportRun`

```csharp
string profileJson = session.ExportMeta();   // 메타 구역 → JSON
session.ImportMeta(profileJson);             // JSON → 메타 구역

string runJson = session.ExportRun();        // 세션 구역 + 난수 + 시간 → JSON
session.ImportRun(runJson);                  // 이어하기
session.NewRun();                            // 세션 구역을 정의할 때의 값으로, Undo 이력 비움(프로필은 그대로, 파일은 모른다)
session.NewRun(RandomStreams.SeedOf("2026-10-05"));   // 위 + 난수를 그 시드로 다시 맞춘다
```

| 메서드 | 담는 것 | 주의 |
|---|---|---|
| `ExportMeta()` | 메타 구역 | 판 내용은 들어가지 않는다 |
| `ImportMeta(json)` | 메타 구역을 덮는다 | 빈 글자면 아무 일도 하지 않는다 |
| `ExportRun()` | 세션 구역, 마스터 시드, 난수 스트림 상태, 틱·게임 시간 | 행동 실행 중에 부르면 예외 |
| `ImportRun(json)` | 위를 모두 되살린다 | 불러온 시점부터 Undo 이력·스냅샷을 새로 시작한다 |
| `NewRun(seed = null)` | 세션 구역을 처음 값으로. `seed`를 주면 마스터 시드·모든 스트림도 그 시드의 처음 상태로 | 파일이 없는 테스트·서버용. 판 파일은 지우지 않는다. Unity 게임은 `GBibleRuntime.NewRun(session, seed)`(2.3) |

- **Undo 이력은 저장하지 않는다.** 불러온 시점부터 새로 쌓인다.
- 아직 정의되지 않은 키의 저장 값은 보관했다가 그 키를 `Define`하는 순간 적용한다. 그래서 불러온 뒤에 코드로 정의해도 된다.
- 판을 불러오면 다음 난수 결과까지 저장한 순간과 같다.

**파일(`LoadRun`) 밖에서 판을 불러올 때**(프로젝트가 판 JSON을 따로 둔다 — 세이브 칸을 직접 관리하는 화면 등): `ImportRun`만 부르면 판 저장 주기·`RunLoaded`가 돌지 않는다. 행동 밖(버튼 등)에서 이 차례로 부른다.

```csharp
if (Dialogues.IsPlaying) Dialogues.Stop();  // 0. (다이얼로그 선택 기능, using GBible.Dialogue) 열린 대사를 먼저 닫는다 — 닫힐 때 진행 칸을 비우므로 불러온 뒤에 닫으면 불러온 진행이 지워진다
session.ImportRun(json);                    // 1. 판 데이터·난수·시계(형식이 틀리거나 게임보다 새 버전이면 FormatException — 판은 그대로)
GBibleRuntime.NotifyRunLoaded(session);     // 2. 판이 살아 있다고 보고 저장 주기를 다시 센다 + RunLoaded(화면이 판 데이터로 다시 맞춘다, 2.10)
GBibleRuntime.SaveRun(session);             // 3. 지금 슬롯의 run.json도 불러온 판으로(끄거나 튕긴 뒤 '이어 하기'가 예전 판을 열지 않게)
Dialogues.ResumeSaved();                    // 4. (다이얼로그) 저장된 대사부터 이어 간다 — 진행이 없으면 거짓
```

대사가 열린 채로 저장해도 된다: 다이얼로그 재생기의 **진행 저장**(`DialoguePlayer.saveProgress`, 기본 켬)이 진행을 판 구역 칸(`dialogue.progress`)에 남겨 판 JSON에 함께 들어간다. 진행 저장을 껐으면 대사 중 저장 버튼을 막는다 — 저장 버튼은 화면 일(`OnClick(이름, Action)`)이라 `GameView.Busy`여도 눌리므로 `Dialogues.IsPlaying`으로 거른다(24장).

### 2.5 저장 파일에 들어가는 모습

```json
{
  "version": 1,
  "state": {
    "player.gold": 150,
    "player.inventory": {
      "items": [
        { "$id": 12, "name": "지팡이", "level": 3, "element": "Ice",
          "effects": [ { "$type": "Game.Burn", "damage": 7 } ],
          "$n": 1, "$type": "Game.Item" }
      ],
      "equipped": { "$ref": 1 }
    }
  },
  "schema": { "…": "…" }
}
```

| 규칙 | 내용 |
|---|---|
| 필드 | 필드 **이름**으로 쓴다(필드 순서를 바꿔도 안전) |
| enum | 값 **이름**으로 쓴다(enum 순서를 바꿔도 안전). 여러 값을 겹친 플래그 enum은 `"A, B"` |
| 다형성 | 선언 형식과 실제 형식이 다르면 `"$type"`에 실제 형식 이름을 남긴다 |
| 컬렉션 요소 번호 | 시스템이 붙인 요소 번호를 `"$id"`로 남겨, 불러온 뒤에도 기록의 `items[#12]`가 같은 요소를 가리킨다 |
| 같은 객체 | 한 값(플레이어 데이터 하나) 안에서 같은 객체가 다시 나오면(두 곳에서 가리킴·서로 가리킴) 처음 쓴 곳에 `"$n": 번호`(와 실제 형식 `"$type"`)를 달고, 다시 나오는 곳에는 `{"$ref": 번호}`만 쓴다. 불러오면 두 곳이 같은 인스턴스를 가리킨다(위 예의 `equipped`는 `items`의 지팡이 그 객체) |
| 숫자 | 숫자로 쓴다. NaN·무한대, 2^53을 넘는 정수(Steam ID 같은 `long`·`ulong`), `decimal`은 글자로 써서 정확히 되읽는다 |
| 날짜·시간·Guid | 글자로 쓴다(`DateTime`·`DateTimeOffset`은 `o` 형식, `TimeSpan`은 `c` 형식). 딕셔너리 키로 써도 같은 형식으로 되읽는다 |
| 에셋 참조 | DB 항목(`GameData`)은 ID로 쓰고, 불러올 때 DB에서 찾는다. 그 밖의 에셋 참조(스프라이트 등)는 저장하지 않고 "저장할 수 없는 참조입니다" 경고를 남긴다 |
| 판 파일 | 위에 `"kind": "run"`, `"seed"`, `"tick"`, `"time"`, `"random"`(스트림별 상태)이 더해진다 |

불러올 때 — **한 곳이 깨져도 나머지는 읽는다.** 읽지 못한 그 자리만 기본값으로 두고 기록(모니터 타임라인)에 Warning을 남긴다.

- 저장 파일에 없는 필드는 클래스의 기본값(필드 초기화)을 쓴다.
- 지금 클래스에 없는 필드는 무시하고 "지금 …에 없는 필드라 무시합니다" 경고를 남긴다.
- 형식이 맞지 않는 값·객체 자리에 든 다른 값은 기본값을 쓰고 경고를 남긴다.
- 없어진 enum 이름은 값이면 기본값으로 읽고, 딕셔너리 키면 그 항목만 버린다(읽을 수 없는 키도 같다 — "키 '…'을(를) …(으)로 읽을 수 없어 이 항목은 버립니다").
- 찾을 수 없는 `"$type"`(지운 클래스)은 선언한 형식으로 읽는다. 선언 형식이 추상 클래스·인터페이스면 그 칸은 비운다(`null`).
- 깊이: 데이터 값은 256단계까지 쓰고 읽는다(그 아래는 저장하지 않고 경고, 읽을 때는 기본값). 저장 파일(JSON)이 512단계 넘게 겹쳐 있으면 깨진 파일로 보고 `.bak`으로 이어 간다(2.3). 손대거나 깨진 파일 하나로 스택이 넘쳐 게임이 꺼지지 않게 한다.
- 다 읽은 뒤 한 번에 넣는다. 키마다 읽다가 멈춰도 반쯤 불러온 모습이 남지 않는다. 받는 쪽(멀티)에 처음 모습을 보내는 것과 변경 알림(`Changed`)은 다 넣은 뒤에 한다.
- 저장 파일에 없는 이 구역의 키(저장한 뒤 처음 생긴 상태 효과·예약 …)는 처음 값으로 되돌린다(불러온 모습에 섞이지 않게).

### 2.6 필드 이름 바꾸기: `[FormerlySerializedAs]`

```csharp
using System;
using UnityEngine.Serialization;

[Serializable]
public class HeroSave
{
    [FormerlySerializedAs("hp")] public int health;   // 예전 이름 hp로 저장된 값을 이어받는다
}
```

필드 이름만 바꿨다면 변환 없이 이것만 단다.

### 2.7 저장 버전과 변환: `SaveMigrations`

[필수] 출시 후 데이터 구조를 호환되지 않게 바꾸면 변환을 더한다(필드 이름만 바꿨으면 2.6. 개발 중에는 맞지 않는 저장을 초기화하므로 필요 없다). 변환만 등록하면 된다. 저장 버전은 등록한 변환의 가장 높은 버전(아래 예에서 4)으로 저절로 올라간다.

키·경로만 받는 변환(`RenameKey`·`RemoveKey`·`MoveField`)은 DB › 개발 › 저장 변환 표(1.4)에 적는다. 표가 먼저 등록되고 코드가 그 뒤에 더한다. 코드로는 보통 값을 계산해 바꾸는 `Add`만 쓴다(아래 예는 문법을 보이려고 넷을 모두 코드로 썼다).

DB 항목 ID(인스턴스·유물 …)를 바꾼 것은 변환이 필요 없다(1.3 — 출시됨 상태면 `[DataRef]`가 붙은 string·string 리스트 필드의 예전 ID도 불러올 때 새 ID로 바뀐다).

```csharp
using System.Collections.Generic;
using GBible;
using UnityEngine;

public static class SaveSetup
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void Hook() => GBibleRuntime.Configuring += options =>
    {
        options.Migrations.RenameKey(2, "player.money", "player.wallet");                      // v1 → v2
        options.Migrations.MoveField(3, "player.inventory", "items[].title", "items[].name");  // v2 → v3
        options.Migrations.RemoveKey(3, "player.oldFlag");                                      // v2 → v3
        options.Migrations.Add(4, "골드 단위 10배", state =>                                    // v3 → v4
        {
            if (state.TryGetValue("player.wallet", out var wallet) && wallet is Dictionary<string, object> w
                && w.TryGetValue("gold", out var gold) && gold is double g)
                w["gold"] = g * 10;
        });
    };
}
```

| 멤버 | 뜻 |
|---|---|
| `session.SaveVersion` | 지금 게임의 저장 버전 = `SessionOptions.SaveVersion`(최솟값, 기본 1)과 등록한 변환의 가장 높은 버전 중 큰 쪽. 저장 파일에 적힌다 |
| `Migrations.Add(toVersion, label, Action<Dictionary<string, object>>)` | `toVersion - 1` → `toVersion` 변환. 저장 트리(키 → 값)를 직접 고친다. `toVersion`은 2부터 |
| `Migrations.RenameKey(toVersion, oldKey, newKey)` | 플레이어 데이터 키 이름 바꾸기 |
| `Migrations.RemoveKey(toVersion, key)` | 더 이상 쓰지 않는 키 지우기 |
| `Migrations.MoveField(toVersion, key, fromPath, toPath)` | 데이터 값 안의 필드를 경로로 옮긴다. 리스트의 모든 요소에 적용하려면 `items[].power`처럼 `[]`를 붙인다 |
| `Migrations.Steps` | 등록된 변환 목록 `(toVersion, label)` |

- 불러올 때 저장 파일의 버전부터 게임 버전까지 변환을 차례로 적용하고, 하나마다 기록에 "저장 데이터 변환 v1 → v2: …"가 남는다.
- **게임보다 새 버전의 저장 파일은 거부한다**(`FormatException`). 예전 게임이 새 저장을 망가뜨리지 않게 하기 위해서다.
- 변환은 불러오기 전에 등록되어야 한다. `GBibleRuntime`은 시작하자마자 프로필을 불러오므로 반드시 `Configuring`에서 등록한다(DB 표는 그 직전에 저절로 등록된다). 세션을 직접 만든다면 `new GameSession(options)` 전에 `options.Migrations`에, 또는 `ImportMeta`·`ImportRun` 전에 `session.Migrations`에 등록한다.
- 저장 트리에서 숫자는 `double`, 클래스는 `Dictionary<string, object>`, 리스트는 `List<object>`다.

### 2.8 예전 키 이어받기: `session.Data.AddAlias`

```csharp
session.Data.AddAlias("m:coins", "m:gold");   // 예전 키 m:coins로 저장된 값을 m:gold로 불러온다
```

DB에서 ID를 바꾸면(출시 후) 런타임이 자동으로 등록한다. 코드로 정의한 키의 이름을 바꿨을 때 직접 쓴다. 불러오기 전에 등록한다.

### 2.9 호환 검사: `GameSession.IsSaveCompatible` · `SaveSchema`

```csharp
bool ok = GameSession.IsSaveCompatible(json, session.SaveSchema, out string reason);
```

`session.SaveSchema`는 저장 파일에 함께 남길 데이터 구조(키·필드 경로 → 형식)다. `GBibleRuntime`이 DB로 채운다. 이 검사가 개발 중 저장 초기화의 판단 기준이다. 구조 정보가 없는 파일은 호환으로 본다.

### 2.10 저장 알림: `GBibleRuntime.Saving` · `RunLoaded` · `Stopping`

```csharp
using GBible;
using UnityEngine;

public static class SaveHooks
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void Hook()
    {
        GBibleRuntime.Saving += session => { /* 판을 저장하기 직전 */ };
        GBibleRuntime.RunLoaded += session => { /* 판을 불러온 뒤 */ };
        GBibleRuntime.Stopping += session => { /* 끄기 직전, 저장 전 */ };
    }
}
```

| 이벤트(`Action<GameSession>`) | 오는 때 | 쓰는 곳 |
|---|---|---|
| `Saving` | 판을 저장하기 직전. `SaveRun`마다 온다(행동마다·N초마다·이벤트를 받을 때·끌 때·모니터 명령·직접 부름) | 화면 쪽에만 있는 상태(플레이어 위치 등)를 판 데이터에 담는 마지막 기회. 행동 밖이므로 담을 때는 `Game.Do`로 |
| `RunLoaded` | 저장한 판을 불러온 뒤(`LoadRun` 성공, 시작할 때 이어 하기·테스트 상황 불러오기(38장)·파일 밖에서 불러온 뒤의 `NotifyRunLoaded`(2.4) 포함) | 화면 쪽 상태를 판 데이터에서 다시 놓는다. Undo 지점(`HistoryMark`)을 다시 받는다 |
| `Stopping` | 게임을 끄기 직전, 슬롯을 바꾸거나 지워 세션을 다시 만들기 직전. 저장한다면 저장보다 먼저 | 마지막 정산(오프라인 보상의 마지막 시각 등)을 `Game.Do`로 남긴다. 그 변경도 이어서 저장된다(지금 슬롯을 지울 때는 저장하지 않는다) |

- 받는 함수가 예외를 던지면 콘솔에 남기고 저장·불러오기는 계속한다. 받는 함수는 하나씩 불리므로 하나가 던져도 나머지는 불린다.
- `Saving` 안에서 실행한 행동은 이번 저장에 들어가고, "저장할 것 있음"으로 세지 않는다(저장이 되풀이되지 않는다). 그래도 값이 그대로면 `Game.Do`를 건너뛴다(저장할 때마다 빈 행동이 기록에 쌓이지 않게).
- 화면 쪽 상태를 판 데이터에 늘 넣어 두지 않는다(위치는 프레임마다 바뀐다). 저장 직전에 한 번 담고, 불러온 뒤 한 번 놓는다(3.3).

---

## 3. 상황별 예제

### 3.1 타이틀 화면: 이어하기 / 새 게임

```csharp
using GBible;
using UnityEngine;

public class TitleMenu : MonoBehaviour
{
    public GameObject continueButton;

    void Start() => continueButton.SetActive(GBibleRuntime.HasSavedRun);

    public void OnContinue()
    {
        if (!GBibleRuntime.LoadRun(GBibleRuntime.Session))
            Debug.LogWarning("판을 불러오지 못했습니다. 모니터 타임라인의 오류를 확인하세요.");
        // 전투 씬으로
    }

    public void OnNewGame()
    {
        GBibleRuntime.NewRun(GBibleRuntime.Session);   // 판 값 초기화(Undo 이력 비움) + run.json 삭제
        // 첫 씬으로
    }
}
```

### 3.2 로그라이크: 판이 끝나면 판만 지우고 보상은 프로필에

```csharp
using System;
using GBible;

[Serializable]
public class EndRun : GameAction
{
    public int earnedShards;
    protected override void Run()
    {
        var data = Game.Session.Data;
        data.Set("meta.shards", data.Get<int>("meta.shards") + earnedShards);   // 메타 구역: 프로필에 남는다
    }
}

public static class RunFlow
{
    public static void Finish(GameApi game, int shards)
    {
        game.Do(new EndRun { earnedShards = shards });
        GBibleRuntime.NewRun(game.Session);      // 세션 구역(맵·유닛·이번 판 인벤토리) 초기화 + 이어하기 파일 삭제
        GBibleRuntime.SaveProfile(game.Session); // 프로필은 바로 저장
    }
}
```

`meta.shards`는 메타 구역 정수 플레이어 데이터로 정의되어 있어야 한다. `NewRun`은 행동 밖에서 부르므로 `EndRun` 행동이 끝난 뒤에 둔다. 판 끝을 이벤트로 알리는 게임이라면 DB › Bootstrap의 **판 끝 이벤트**가 판 저장 삭제·프로필 저장을 대신한다(2.2).

### 3.3 체크포인트에서 판 저장(이벤트를 받을 때)

DB › 시작 파일 › Bootstrap: **판 저장** "이벤트를 받을 때 저장(하루 끝·체크포인트)", **판 저장 이벤트** `CheckpointReached`. 게임 코드는 이벤트를 쏘는 행동과, 화면 쪽 위치를 담고 놓는 일뿐이다.

```csharp
using System;
using GBible;
using UnityEngine;

[GameEvent("체크포인트 도착")]
public class CheckpointReached { public string checkpoint; }

[Serializable]
public class Explorer { public string id = "hero"; public string checkpoint; public float x, y; }

[Serializable, BlockInfo("체크포인트 도착")]
public class ReachCheckpoint : GameAction
{
    public Explorer explorer;
    public string checkpoint;
    protected override void Run()
    {
        explorer.checkpoint = checkpoint;
        Game.Events.Publish(new CheckpointReached { checkpoint = checkpoint });   // 이 행동이 끝난 프레임 끝에 판을 저장한다
    }
}

[Serializable, BlockInfo("위치 기록")]
public class RecordPosition : GameAction
{
    public Explorer explorer;
    public float x, y;
    protected override void Run() { explorer.x = x; explorer.y = y; }
}

public class Checkpoint : MonoBehaviour
{
    public string id;
    void OnTriggerEnter(Collider other) =>                                  // 판정은 화면, 결과는 행동
        GBibleRuntime.Game.Do(new ReachCheckpoint { explorer = GBibleRuntime.Game.Find<Explorer>("hero"), checkpoint = id });
}

public class HeroBody : MonoBehaviour
{
    void OnEnable()  { GBibleRuntime.Saving += OnSaving; GBibleRuntime.RunLoaded += OnLoaded; }
    void OnDisable() { GBibleRuntime.Saving -= OnSaving; GBibleRuntime.RunLoaded -= OnLoaded; }

    void OnSaving(GameSession session)                                      // 저장 직전: 위치를 판 데이터에
    {
        var hero = session.Game.Find<Explorer>("hero");
        var p = transform.position;
        if (hero == null || (hero.x == p.x && hero.y == p.y)) return;       // 그대로면 행동을 만들지 않는다
        session.Do(new RecordPosition { explorer = hero, x = p.x, y = p.y });
    }

    void OnLoaded(GameSession session)                                      // 불러온 뒤: 위치를 화면에
    {
        var hero = session.Game.Find<Explorer>("hero");
        if (hero != null) transform.position = new Vector3(hero.x, hero.y, 0);
    }
}
```

- 판 저장은 `ReachCheckpoint`가 끝난 프레임 끝(행동 밖)에서 한다. 그 행동이 실패해 취소되면 저장하지 않는다.
- 하루 끝에 저장하는 생활 시뮬도 같다. 판 저장 이벤트로 "하루 넘김" 이벤트를 고른다.
- 직접 저장하려면 행동 밖에서 `GBibleRuntime.SaveRun(session)`을 부른다(`session.Data.InTransaction`이 거짓인 곳). 이때도 `Saving`이 오고, 프로필도 함께 저장된다.

### 3.4 서버·테스트: 글자로 왕복

```csharp
using System;
using System.Collections.Generic;
using GBible;

[Serializable] public class Bag { public int gold; public List<string> items = new List<string>(); }

public static class SaveRoundTrip
{
    public static void Run()
    {
        var played = new GameSession(new SessionOptions { Seed = 11 });
        played.Data.Define("run.bag", DataZone.Session, new Bag());
        played.Data.Define("meta.gold", DataZone.Meta, 0);
        played.Data.Modify<Bag>("run.bag", b => b.items.Add("검"));
        played.Data.Set("meta.gold", 99);
        string profile = played.ExportMeta();
        string run = played.ExportRun();

        var resumed = new GameSession(new SessionOptions { Seed = 11 });
        resumed.ImportMeta(profile);
        resumed.ImportRun(run);
        resumed.Data.Define("run.bag", DataZone.Session, new Bag());    // 불러온 뒤에 정의해도 값이 들어간다
        resumed.Data.Define("meta.gold", DataZone.Meta, 0);
        // resumed.Data.Get<Bag>("run.bag").items[0] == "검", resumed.Data.Get<int>("meta.gold") == 99
    }
}
```

### 3.5 출시 후 필드 구조 바꾸기

v1에서 `Inventory.coins`였던 것을 v2에서 `Inventory.wallet.gold`로 옮긴다.

**DB로**(기본): DB › 개발 › 저장 변환에 항목 하나 — 버전 2 · 필드 옮김 · 키 `player.inventory` · 예전 경로 `coins` · 새 경로 `wallet.gold`. 클래스(`Inventory`에 `Wallet wallet` 필드)만 바꾸면 코드는 없다.

코드로 같은 일을 하면 `Configuring`에서 `options.Migrations.MoveField(2, "player.inventory", "coins", "wallet.gold")`다(2.7). 저장 버전은 2로 저절로 오른다.

---

## 4. 주의할 점 · 자주 하는 실수

| 증상 | 원인과 해결 |
|---|---|
| 개발 중 플레이를 시작했더니 저장이 사라짐 | 키·필드를 지웠거나 이름·형식을 바꿨다. 개발 중에는 의도된 동작이다(콘솔 경고에 이유가 있다). 맞지 않는 파일만 `이름.reset-<시각>.json`으로 옆에 옮겨져 있다(1.1). 출시 후에는 **MomoiProject › 개발 › 출시됨 (저장 호환 유지)**을 켠다. |
| 불러온 판이 한 단계 앞 모습 · 폴더에 `*.broken-<시각>.json`이 생김 | 저장 파일이 비었거나(쓰는 도중 꺼짐·디스크 가득) 읽지 못해 바로 전 저장(`.bak`)으로 이어 갔다. 원본은 `.broken-` 사본에 남아 있다(2.3). 기록의 "… 불러오기 실패" 경고에 이유가 있다. |
| 불러온 값 일부만 기본값 · "… 기본값으로 읽습니다" 경고 | 그 자리만 읽지 못했다(없어진 enum 이름·지운 클래스·형식이 바뀐 값 …). 나머지는 그대로 불러온다(2.5). 출시 후라면 저장 변환을 더한다. |
| 불러온 값이 기본값 | 필드 이름을 바꿨다. `[FormerlySerializedAs("예전이름")]`을 단다. |
| "이 게임보다 새 버전입니다" | 저장 파일의 버전이 지금 게임의 저장 버전(등록한 변환의 가장 높은 버전)보다 크다. 게임을 업데이트하거나, 변환을 지우지 않았는지 확인한다. |
| 변환이 적용되지 않음 | `Configuring`보다 늦게(예: `SessionCreated`·`Started`에서) 등록했다. 프로필은 세션을 만들자마자(`SessionCreated` 전에) 불러온다. |
| 이어하기 후 Undo가 안 됨 | Undo 이력은 저장하지 않는다. 불러온 시점부터 새로 쌓인다. |
| 판 저장에 프로필 값이 없음 | 판 파일(`run.json`)은 세션 구역만 담는다. 메타 구역은 `profile.json`이다. `SaveRun`은 두 파일을 함께 쓴다(2.3). |
| 스프라이트 필드가 불러온 뒤 비어 있음 | DB 항목(`GameData`)이 아닌 에셋 참조는 저장되지 않는다. 플레이어 데이터에는 DB·인스턴스 ID를 두고 그림은 거기서 찾는다. |
| "행동 실행 중에는 판을 저장할 수 없습니다" | 행동의 `Run` 안에서 `ExportRun`·`SaveRun`을 불렀다. 행동이 끝난 뒤 부른다. |
| 새 판을 열었는데 이어하기 버튼이 보임 | `session.NewRun()`만 불렀다(파일을 지우지 않는다). `GBibleRuntime.NewRun(session)`을 쓴다. |
| "행동 실행 중에는 새 판을 시작할 수 없습니다" | 행동의 `Run` 안에서 `GBibleRuntime.NewRun`을 불렀다. 버튼처럼 행동 밖에서 부르고, 새 판 내용은 그 뒤 행동으로 만든다. |
| 저장 변환 표를 적었는데 적용되지 않음 | 버전이 저장 파일의 버전보다 크지 않다(이미 그 버전으로 저장된 파일에는 돌지 않는다). 또는 키가 저장 파일의 예전 이름과 다르다. |
| 저장 파일이 `MomoiProject/` 바로 아래에 없음 | 다른 슬롯을 쓰고 있다(`slots/<이름>/`). `GBibleRuntime.Slot`·`SavePath`로 확인한다(2.1). |
| 판을 지웠는데(새 판) 음량·언어가 그대로 | 정상이다. 설정은 슬롯·판과 상관없는 `settings.json`에 있다(2.1). |
| "이벤트를 받을 때 저장"인데 저장되지 않음 | 그 이벤트를 쏜 행동이 실패해 취소됐거나, **판 저장 이벤트** 칸이 비어 있거나(그러면 끌 때만 저장한다), 이벤트 클래스 이름이 바뀌어 찾지 못했다(기록에 "판 저장 이벤트 '…'를 찾을 수 없습니다" 경고). 칸을 다시 고른다. |
| "N초마다 저장"인데 "판 저장" 기록이 없음 | 그사이 행동이 없었다. 바뀐 것이 없으면 쓰지 않는다. |
| "행동마다 저장"인데 상태 효과 만료·예약 뒤 판 저장 기록이 없음 | 정상이다. 시스템 행동(`[SystemAction]`)은 세지 않고, 그 변경은 다음 플레이어 행동·끌 때 함께 저장된다(2.2). |
| 이어 한 뒤 플레이어가 처음 자리에 있음 | 위치가 화면 쪽에만 있었다. `Saving`에서 판 데이터에 담고 `RunLoaded`에서 놓는다(3.3). |
| 판 밖(로비) 뽑기를 껐다 켜서 다시 뽑음 | 프로필에는 난수 상태가 없다(`07-Random-Time.md` 3절). 끌 때만 저장하면 강제 종료로 뽑기 전으로 돌아가고, 다음 실행은 다른 시드라 다른 결과가 나온다. **프로필도 행동마다 저장**을 켜 결과를 그 프레임 끝에 바로 저장한다(판 저장 방식과 상관없다). |
| "프로필도 행동마다 저장"을 켰는데 전투 행동 뒤 프로필 저장 기록이 없음 | 정상이다. 프로필(메타 구역)을 바꾼 행동에서만 쓴다(2.2). |
| `ImportRun`으로 불러왔더니 화면이 옛 판 · 끄고 이어 하면 예전 판 | `NotifyRunLoaded`·`SaveRun`을 부르지 않았다. 파일 밖에서 불러올 때의 차례는 2.4. |
