# 38. 테스트 — 테스트 씬 · 테스트 상황 · 테스트 명령 · 자동 시뮬레이션

**한 줄 요약**: 개발자는 런타임에 원하는 상황을 만들어 기믹·밸런스·버그를 시험한다. AI는 프로젝트마다 **테스트 씬**(컴포넌트 `TestScene`)을 만들고, 그 위에서 쓸 **테스트 명령**(DB › 개발 명령)과 **테스트 상황**(`TestScenarios` — 판·프로필·난수·시각을 이름 붙여 저장한 파일)을 둔다. 같은 상황을 시드만 바꿔 여러 번 돌리는 **자동 시뮬레이션**(`TestScenarios.Simulate`)으로 밸런스 숫자를 본다. 모두 에디터·개발 빌드에서만 돈다.

**언제 쓰는가**

- "보스 바로 앞에서 유물 다섯 개 든 덱으로 시험하고 싶다" → 테스트 상황 하나 + 테스트 씬의 시작 상황.
- "독 10중첩 걸린 적을 만들어 보고 싶다" → 테스트 명령(적 만들기 + 상태 효과 걸기).
- "이 상황에서 승률이 몇 %인가" → 자동 시뮬레이션 200번.
- 버그 보고: 개발자가 저장한 상황 이름으로 AI가 재현한다.

이 문서에서 쓰는 용어:

| 용어 | 뜻 |
|---|---|
| 테스트 씬 | 게임 화면과 같은 클라이언트에 `TestScene` 컴포넌트를 둔 씬(`Assets/MomoiProject/Scenes/Test.unity`). |
| 테스트 패널 | 테스트 씬을 플레이하면 게임 화면 위에 뜨는 개발용 창(F1). 시간 · Undo · 새 판 · 테스트 상황 · 테스트 명령. |
| 테스트 상황 | 지금 판(세션 구역·난수·게임 시각)과 프로필(메타 구역)의 스냅숏 파일. `Assets/MomoiProject/Data/Tests/<이름>.json`. |
| 테스트 명령 | 버튼 하나로 도는 개발용 동작. DB › 개발 › 개발 명령 항목(결과 Block) 또는 코드 등록(`session.Debug.Register`). 22장. |
| 'test' 슬롯 | 테스트 씬이 쓰는 저장 슬롯. 개발자의 실제 저장(기본 슬롯)을 건드리지 않는다. 다음 실행의 시작 슬롯으로 기억되지 않는다. |
| 자동 시뮬레이션 | 상황을 시드만 바꿔 여러 번 불러오고, 프로젝트가 정한 '한 수'를 끝날 때까지 두게 한 뒤 숫자를 잰다. |

---

## 1. 무엇으로 만드나

| 하고 싶은 일 | 만드는 것 |
|---|---|
| 런타임에 원하는 모양을 만들어 시험 | 테스트 씬 + 테스트 명령(주입: 인스턴스 넣기·자원 주기·패시브 얻기·상태 효과 걸기·적 만들기 / 발동: 이벤트 쏘기·행동 실행) |
| 같은 장면을 다시 열기 | 테스트 상황 저장 → 테스트 씬의 시작 상황 · 패널 · 모니터 › 명령 |
| 밸런스 숫자(승률·평균 턴) | `TestScenarios.Simulate` + 결과 CSV |
| 버그 재현 | 개발자가 저장한 상황 → 규칙 테스트(코드로 같은 상황) |

## 2. 동작 규칙

### 2-1. 테스트 씬(`TestScene`)

다른 컴포넌트보다 먼저(실행 순서 −10000) `Awake`에서:

1. `separateSlot`이 켜져 있으면 `GBibleRuntime.UseSlot("test", remember: false)` — 'test' 슬롯으로 세션을 다시 시작한다.
2. `scenario`가 있으면 `TestScenarios.Load(session, scenario, seed)`. 비었거나 불러오지 못하면(파일 없음·형식이 틀림·저장한 뒤 데이터 구조가 바뀜 — 콘솔에 경고) `GBibleRuntime.StartNewRun(session, seed)`(새 판 + Bootstrap '새 판 행동'). 시험 모드 서버의 판도 같다. `seed`는 글자 시드(`RandomStreams.SeedOf`), 비우면 상황의 시드.
3. `GameTime.SetSpeed(speed)`.

출시 빌드(`Debug.isDebugBuild`가 거짓이고 에디터가 아님)에서는 아무것도 하지 않는다.

### 2-2. 테스트 상황(`TestScenarios`)

- `Save(session, 이름)`: `{ kind, name, savedAt, run = session.ExportRun(), meta = session.ExportMeta() }`를 JSON으로. 에디터는 `Assets/MomoiProject/Data/Tests/`(프로젝트와 함께 커밋), 개발 빌드는 `<persistentDataPath>/MomoiProject/Tests/`.
- 개발 빌드에는 에디터의 상황이 같이 들어간다: 개발 빌드를 만들 때 `Assets/MomoiProject/Data/Tests/*.json`을 빌드의 `StreamingAssets/MomoiProject/Tests/`에 넣는다(`TestScenarios.BundledFolder`, 읽기 전용 — 프로젝트의 `Assets/StreamingAssets`는 건드리지 않는다). 출시 빌드에는 넣지 않는다. 개발 빌드의 목록·불러오기는 기기에 저장한 것과 이것을 함께 보고, 같은 이름이면 기기에 저장한 것을 쓴다. 저장·지우기는 기기 쪽만. Android·WebGL은 StreamingAssets를 파일로 열 수 없어 기기에 저장한 것만 보인다.
- `Load(session, 이름, seed = null)`: 프로필 → 판 순서로 불러온다(`ImportMeta` → `ImportRun`). Undo 이력은 비워진다. `seed`를 주면 불러온 뒤 난수만 그 시드로(`Random.Reseed`). 끝에 `GBibleRuntime.NotifyRunLoaded(session)` — 화면이 판 데이터로 다시 맞춘다(`RunLoaded`). 없거나 읽지 못하면 저널에 이유를 남기고 `false`(던지지 않는다). 프로필을 불러온 뒤 판에서 실패하면 프로필도 되돌린다(`false`면 프로필이 바뀌지 않는다 — 반쪽 상태가 판 저장으로 슬롯에 남지 않게).
- 행동 밖에서 부른다(행동 안이면 `Save`는 `ExportRun`이, `Load`는 스스로 예외).

### 2-3. 자동 시뮬레이션(`TestScenarios.Simulate`)

```
for i in 0..runs-1:
    Load(scenario, seed = firstSeed + i)
    while steps < maxSteps and step(session): steps++
    row = { seed, steps } + measure(session)
끝나면 Load(scenario, firstSeed)
```

- `step`은 "플레이어의 한 수"다 — 프로젝트(AI)가 행동을 골라 `Game.Do`. 더 둘 수 없으면(판 끝) 거짓.
- `measure`는 그 판에서 잴 숫자(승리 1/0·턴·남은 체력 …).
- `step`·`measure`가 예외를 던지면 그 회차는 `Failures`로 세고(저널에 이유) 다음으로. 끝나면(도중에 던져도) 처음 시드로 다시 연다.
- 라이브 세션에서 돈다(화면이 쌓은 연출은 회차마다 — 실패한 회차도 — 비운다). 턴제에 맞다. 실시간은 화면 판정을 `step`이 근사해야 한다.

### 2-4. AI가 규칙 테스트를 돌리기(요청 파일 → 결과 파일)

AI는 Test Runner 창을 누를 수 없고, Unity가 열려 있으면 배치 모드로 같은 프로젝트를 열 수 없다. 그래서 에디터가 파일로 받는다(DB 상태 파일 `DbStatus.txt`와 같은 생각).

1. AI가 `Logs/MomoiProject/RunTests.request`를 만든다. 내용이 비면 프로젝트의 EditMode 테스트 어셈블리(Assets 아래 코드이고 NUnit을 참조하는 것), `all`이면 GBible 테스트까지, 어셈블리 이름을 줄마다 적으면 그것만.
2. 에디터가 1초마다 본다. 요청 하나에 한 번 에셋을 새로 읽고(방금 쓴 코드가 컴파일된다 — 도메인을 다시 읽으면 이어서), 컴파일 오류가 있으면 그렇게 쓰고 끝낸다.
3. EditMode 테스트를 돌리고 `Logs/MomoiProject/TestResults.txt`에 쓴다: 첫 줄 `상태: 모두 통과 — …` 또는 `상태: 실패 n · 통과 m …`, 실패한 테스트마다 이름·메시지·호출 위치 몇 줄. 요청 파일은 지운다.

- 플레이 중이면 끝날 때까지 기다린다. 요청 파일이 오래 남아 있으면 에디터가 멈춰 있거나(모달 창) 플레이 중이다.
- 개발자가 Test Runner 창에서 직접 돌린 실행은 결과 파일에 쓰지 않는다(요청으로 시작한 실행만).

### 2-5. AI가 Unity를 플레이해 화면을 보기(플레이 요청 → 결과 · 찍은 그림)

AI는 Unity 창을 누르거나 볼 수 없다. 그래서 에디터가 파일로 받아 **플레이에 들어가 단계를 차례로 돌리고, 게임 화면을 그림으로 찍어** 돌려준다(`PlayRequests`). AI는 결과 파일과 그림(PNG — AI가 그림으로 읽는다)으로 화면을 확인하고 고친 뒤 다시 돌린다.

1. AI가 `Logs/MomoiProject/Play.request`에 한 줄에 하나씩 단계를 적는다(`#` 뒤는 주석).

| 단계 | 하는 일 |
|---|---|
| `scene <경로>` | 플레이할 씬(비우면 테스트 씬 `Assets/MomoiProject/Scenes/Test.unity`, 없으면 지금 씬) |
| `scenario <이름>` | 테스트 상황 불러오기 |
| `wait <초>` | 기다리기(실제 초) |
| `command <DB ID 또는 버튼 이름>` | 테스트 명령(DB › 개발 명령의 ID·버튼 이름, 코드로 등록한 명령의 이름) |
| `open <화면 ID> [값]` · `close <화면 ID>` · `back` | 화면 열기·닫기·뒤로(Esc) |
| `click <화면 ID>/<요소 이름>[@n]` | 요소 누르기(같은 이름의 n번째 — 0부터). Button은 '제출', 그 밖은 클릭 이벤트. 목록 칸은 `tile@0`처럼 |
| `speed <배속>` | 게임 속도 |
| `capture <이름>` | 게임 화면을 `Logs/MomoiProject/Captures/<이름>.png`로(게임 뷰 크기) |
| `record <이름> [초] [fps]` | 지금부터 게임 화면(UI 포함)을 녹화해 `Captures/<이름>.mp4`(기본 2초 · 30fps · 너비 1280까지 — Unity에 든 인코더 `MediaEncoder`, 설치 없음). 다음 단계는 녹화와 **함께** 돈다(`record` 뒤에 `open`·`click` …). 영상의 시간은 실제 시간에 맞춘다(게임 프레임이 늦으면 같은 화면을 여러 칸에). 녹화 중의 다음 `record`는 앞 녹화가 끝날 때까지 기다리고, 모든 단계가 끝나도 녹화가 끝난 뒤 플레이를 멈춘다. AI가 볼 수 있게 고르게 뽑은 칸 16개를 한 장으로 `Captures/<이름>-frames.png`(왼쪽 위부터 4열 — 칸마다의 시각과 실제로 잡은 수는 결과에) |
| `screens` | 열린 화면 목록을 결과에 |
| `testpanel off·on` | 테스트 패널 숨기기·보이기(기본은 숨김 — 찍은 그림을 가리지 않게) |

2. 에디터가 1초마다 본다. 요청 하나에 한 번 **에셋을 새로 읽는다**(방금 쓴 코드·UXML·USS — Unity는 창을 볼 때만 저절로 새로 읽으므로). 컴파일되면 도메인을 다시 읽고 이어서. 컴파일 오류면 그렇게 쓰고 끝낸다.
3. 열린 씬에 저장하지 않은 변경이 있으면 시작하지 않는다(결과 파일에 까닭 — 개발자의 작업을 덮지 않는다). 그 밖이면 플레이에 들어가 단계를 돌리고, 끝나면 플레이를 멈추고 처음 씬으로 돌아간다.
4. `Logs/MomoiProject/PlayResults.txt`: 첫 줄 상태(`끝남 — 모든 단계를 돌렸습니다` · `실패한 단계가 있습니다(아래 ✗)` · 시작하지 못한 까닭), 이어서 DB의 '적용 전' 건수 · 새로 읽을 때 난 오류(XML로 읽히지 않는 UXML은 경로와 함께) · 단계마다 결과 · 그동안의 콘솔 오류·경고(같은 오류는 처음 한 번만, 코드 자리와 함께). 요청 파일은 지운다.

- **창을 띄우지 않는다**: 플레이할 때 DB의 '적용 전' 변경을 묻는 창("DB에 저장 안 된 변경")을 건너뛰고 **적용된 데이터로** 돈다 — 답할 사람이 없으면 Unity가 그 창에서 멈추기 때문이다. 적용(Ctrl+S)은 개발자 몫이라 AI가 대신 고르지 않는다.
- 플레이에 들어가지 못하면(다른 도구가 막음) 5초 뒤 결과에 까닭을 쓰고 요청을 지운다.
- 요청 파일이 오래 남아 있으면 에디터가 모달 창에서 멈춰 있다. AI는 그 창의 내용을 읽어 개발자에게 알린다(창의 단추를 대신 고르지 않는다 — 고른다면 아무것도 바꾸지 않는 '취소'만).
- 개발자가 Unity에서 작업하는 중에는 요청하지 않는다(플레이에 들어간다).
- 진행 매뉴얼의 화면 단계(UiGuide 4절 6)는 화면을 만든 뒤 이 둘러보기로 확인한다: 화면마다 한 장, 상호작용(고르기 → 쓰기 → 대상)마다 앞뒤 한 장.
- 움직임(나타나기·사라지기·자리 옮김 — UiGuide 3-5)은 `record`로 본다: AI는 모음 그림(`-frames.png`)에서 칸마다의 투명도·자리를 읽어 시간(0.14초·0.1초)과 방향을 확인하고, 영상(`.mp4`)은 개발자에게 보낸다. 실제로 잡은 수가 칸 수보다 많이 적으면(결과에 '끊겨 보일 수 있음') 에디터가 느린 것 — 짧게(1초 안팎) 나눠 찍는다.

```
# 예: 로비 → 정비 → 런 → 아이템을 캐릭터에게
wait 2
capture 10-lobby
open party
click party/tab-gear
capture 11-prep-gear
close party
command test.startRun
wait 1.5
command test.giveBread
open storage
click storage/tile@0
click storage/act-use
capture 20-targeting
click runHud/member@0
wait 2
capture 21-used
# 움직임: 창고를 여는 순간을 1초 녹화(영상 + 모음 그림)
record 30-storage-open 1.0
wait 0.15
open storage
wait 1.2
```

## 3. 에디터에서 쓰는 법

- 메뉴 **MomoiProject › 테스트 › 테스트 씬 열기** / **지금 씬을 복사해 테스트 씬 만들기** / **테스트 상황 폴더 열기**.
- 테스트 씬의 오브젝트 '[테스트]' › 컴포넌트 **테스트 씬**: 시작 상황(드롭다운 — 저장된 상황 목록) · 시드 · 따로 슬롯 · 속도 · 패널 펼침 · 패널 키 · 멀티플레이(혼자·시험 모드·방 생성·방 참가, 지연·흔들림·화면 없는 클라이언트 수·포트·주소·이름 — 40장).
- 플레이 → **테스트 패널**(F1): 시간(×0.5·×1·×2·×4·멈춤) · Undo · 새 판 · 테스트 상황(지금 저장·불러오기) · 테스트 명령(분류별 버튼) · 멀티(시험 모드·방 생성·방 참가·매칭큐 — 클라이언트일 때 Undo·새 판·테스트 상황은 서버에서).
- 모니터 › **명령** 탭에도 **테스트 상황** 패널(지금 저장·불러오기)이 있다(22장).

## 4. 코드로 쓰는 법

```csharp
// 테스트 씬 만들기(DB 반영 스크립트에서 — 관례 15·19절). 이미 있으면 건너뛴다.
[DbSeed("1")]
static void TestScene() => TestSceneSetup.Ensure(copyFrom: "Assets/Dev/Scenes/Battle.unity", scenario: "보스 직전");

// 테스트 상황
TestScenarios.Save(session, "보스 직전");
TestScenarios.Load(session, "보스 직전", seed: 7);
var names = TestScenarios.Names();

// 자동 시뮬레이션
var report = TestScenarios.Simulate(session, "보스 직전", runs: 200,
    step: s => AutoPlay.Step(s),                     // 한 수(프로젝트가 만든다). 판이 끝나면 false
    measure: s => new Dictionary<string, double> { ["win"] = Rules.Won(s) ? 1 : 0, ["turns"] = s.Game.Get<RunState>("run").turn });
Debug.Log(report.ToText());                         // "win: 평균 0.62 · 최소 0 · 최대 1" …
TestScenarios.SaveReport(report);                   // Tests/Reports/보스 직전-날짜.csv
```

| 멤버 | 하는 일 |
|---|---|
| `TestScenarios.Folder` · `PathOf(이름)` · `Exists` · `Names()` | 상황 파일 위치(저장하는 곳)·목록 |
| `TestScenarios.BundledFolder` · `BundledPath` | 개발 빌드에 같이 넣은 상황(읽기 전용, 에디터·출시 빌드는 `null`) · 그 자리(`StreamingAssets` 아래) |
| `Save(session, 이름)` · `Load(session, 이름, seed)` · `Delete(이름)` | 저장·불러오기·지우기(지우기는 저장한 것만) |
| `Simulate(session, 상황, runs, step, measure, maxSteps = 1000, firstSeed = 1)` | 자동 시뮬레이션 → `TestReport` |
| `SaveReport(report, 이름)` | CSV(엑셀용 UTF-8 BOM, RFC 4180 — 쉼표·따옴표·줄바꿈이 든 칸은 따옴표로 감싼다) |
| `TestReport.Runs` · `Columns` · `Average(열)` · `Min` · `Max` · `Failures` · `ToText()` · `ToCsv()` | 결과 |
| `TestSceneSetup.Ensure(path, copyFrom, scenario)` (에디터) | 테스트 씬 만들기 |
| `GBibleRuntime.UseSlot(슬롯, remember: false)` · `NotifyRunLoaded(session)` | 기억하지 않는 슬롯 · 판을 불러온 알림 |

테스트 명령은 22장(개발 명령). 결과 Block으로 조립하고 `category`로 묶는다(예: "판", "조각", "적", "상황").

## 5. 상황별 예제

### 5-1. 보스 직전 상황을 테스트 씬의 시작으로

1. AI: 테스트 명령 '상황: 보스 직전'(결과: 층 = 10 · 덱에 카드 20장 넣기 · 유물 셋 얻기)을 반영 스크립트로 만든다.
2. 개발자: 테스트 씬 플레이 → 패널에서 '상황: 보스 직전' → 이름 "보스 직전"으로 지금 저장.
3. 테스트 씬 › 시작 상황 = 보스 직전. 다음 플레이부터 바로 그 장면.

### 5-2. 버그 재현

개발자 보고: "상황: 독 폭발 / 한 일: 턴 끝 / 기대: 적 하나만 / 실제: 모든 적". AI는 같은 판을 규칙 테스트에서 만들고(`Data.Define` + 행동들) 기대값을 단언한 뒤 고친다. 상황 파일을 `TestScenarios.Load`로 쓰는 EditMode 테스트로 재현해도 된다(Unity 안에서만 돈다).

## 6. 주의할 점 · 자주 하는 실수

- 테스트 상황은 저장할 때의 데이터 구조로 남는다. 데이터 클래스가 바뀌면 오래된 상황은 불러올 때 경고·빈 값이 생길 수 있다 — 다시 저장한다(출시 뒤 저장 변환처럼 맞추지 않는다).
- 테스트 씬에서 '따로 슬롯'을 끄면 개발자의 실제 저장으로 돈다(판 저장이 덮일 수 있다).
- 시뮬레이션의 `step`에서 플레이어의 선택 대기(`Game.Ask`)가 생기면 답(`Game.Answer`)까지 `step`이 해야 한다. 안 하면 그 자리에서 멈춘다(`maxSteps`까지 헛돈다).
- 시뮬레이션은 현실 시각(`Game.Now`)을 고정하지 않는다 — 회차마다 지금 시각이다. 현실 시각으로 계산하는 규칙(오프라인 보상·일일 초기화)은 회차마다 결과가 조금 다를 수 있으니, 그런 숫자는 `measure`로 재지 않거나 `step`이 정한 값으로 시험한다(게임 안 시간 `session.Clock`·난수는 시드대로 같다).
- 테스트 명령·패널은 출시 빌드에 없다. 게임 기능(치트 코드 등)으로 쓰지 않는다.
- **Unity Test Runner로 규칙 테스트를 돌릴 때**: 에디터·게임은 평소 테스트 어셈블리의 형식(`[BindTo]`·키워드·Mirror 비교 코드)을 훑지 않는다(DB 창·검증에 테스트용 형식이 섞이지 않게). Test Runner의 실행 하나가 도는 동안만 넣는다 — 그동안 테스트가 게임 세션을 만들어도 빠지지 않는다(`GameAssemblies.KeepTests`, 에디터 `TestRuns`가 실행 시작·끝에 켜고 끈다. PlayMode 테스트가 도메인을 다시 읽어도 그대로). Test Framework 패키지(`com.unity.test-framework` 1.1 이상)가 있을 때만 돈다(`GBIBLE_TEST_FRAMEWORK` — `GBible.Editor.asmdef`의 versionDefines).
- 테스트 어셈블리에는 Mirror 비교 코드 생성 경고를 내지 않는다(일부러 다루지 못하는 형식을 담아 시험하기도 하므로). 콘솔에는 게임 코드의 경고만 나온다.
