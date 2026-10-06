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
| 테스트 씬 | 게임 화면과 같은 클라이언트에 `TestScene` 컴포넌트를 둔 씬(`Assets/Scenes/Test.unity`). |
| 테스트 패널 | 테스트 씬을 플레이하면 게임 화면 위에 뜨는 개발용 창(F1). 시간 · Undo · 새 판 · 테스트 상황 · 테스트 명령. |
| 테스트 상황 | 지금 판(세션 구역·난수·게임 시각)과 프로필(메타 구역)의 스냅숏 파일. `Assets/MomoiProjectData/Tests/<이름>.json`. |
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
2. `scenario`가 있으면 `TestScenarios.Load(session, scenario, seed)`, 없으면 `GBibleRuntime.NewRun(session, seed)`. `seed`는 글자 시드(`RandomStreams.SeedOf`), 비우면 상황의 시드.
3. `GameTime.SetSpeed(speed)`.

출시 빌드(`Debug.isDebugBuild`가 거짓이고 에디터가 아님)에서는 아무것도 하지 않는다.

### 2-2. 테스트 상황(`TestScenarios`)

- `Save(session, 이름)`: `{ kind, name, savedAt, run = session.ExportRun(), meta = session.ExportMeta() }`를 JSON으로. 에디터는 `Assets/MomoiProjectData/Tests/`(프로젝트와 함께 커밋), 개발 빌드는 `<persistentDataPath>/MomoiProject/Tests/`.
- `Load(session, 이름, seed = null)`: 프로필 → 판 순서로 불러온다(`ImportMeta` → `ImportRun`). Undo 이력은 비워진다. `seed`를 주면 불러온 뒤 난수만 그 시드로(`Random.Reseed`). 끝에 `GBibleRuntime.NotifyRunLoaded(session)` — 화면이 판 데이터로 다시 맞춘다(`RunLoaded`).
- 행동 밖에서 부른다(행동 안이면 `ExportRun`이 예외).

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
- 예외가 나면 그 회차는 `Failures`로 세고 다음으로.
- 라이브 세션에서 돈다(화면이 쌓은 연출은 회차마다 비운다). 턴제에 맞다. 실시간은 화면 판정을 `step`이 근사해야 한다.

## 3. 에디터에서 쓰는 법

- 메뉴 **MomoiProject › 테스트 › 테스트 씬 열기** / **지금 씬을 복사해 테스트 씬 만들기** / **테스트 상황 폴더 열기**.
- 테스트 씬의 오브젝트 '[테스트]' › 컴포넌트 **테스트 씬**: 시작 상황(드롭다운 — 저장된 상황 목록) · 시드 · 따로 슬롯 · 속도 · 패널 펼침 · 패널 키 · 멀티플레이(혼자·시험 모드·방 생성·방 참가, 지연·흔들림·화면 없는 클라이언트 수·포트·주소·이름 — 40장).
- 플레이 → **테스트 패널**(F1): 시간(×0.5·×1·×2·×4·멈춤) · Undo · 새 판 · 테스트 상황(지금 저장·불러오기) · 테스트 명령(분류별 버튼) · 멀티(시험 모드·방 생성·방 참가·매칭큐 — 클라이언트일 때 Undo·새 판·테스트 상황은 서버에서).
- 모니터 › **명령** 탭에도 **테스트 상황** 패널(지금 저장·불러오기)이 있다(22장).

## 4. 코드로 쓰는 법

```csharp
// 테스트 씬 만들기(DB 반영 스크립트에서 — 관례 15·19절). 이미 있으면 건너뛴다.
[DbSeed("1")]
static void TestScene() => TestSceneSetup.Ensure(copyFrom: "Assets/Scenes/Battle.unity", scenario: "보스 직전");

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
| `TestScenarios.Folder` · `PathOf(이름)` · `Exists` · `Names()` | 상황 파일 위치·목록 |
| `Save(session, 이름)` · `Load(session, 이름, seed)` · `Delete(이름)` | 저장·불러오기·지우기 |
| `Simulate(session, 상황, runs, step, measure, maxSteps = 1000, firstSeed = 1)` | 자동 시뮬레이션 → `TestReport` |
| `SaveReport(report, 이름)` | CSV(엑셀용 UTF-8 BOM) |
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
- 테스트 명령·패널은 출시 빌드에 없다. 게임 기능(치트 코드 등)으로 쓰지 않는다.
