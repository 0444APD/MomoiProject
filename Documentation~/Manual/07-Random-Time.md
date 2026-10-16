# 07. 난수와 시간

**한 줄 요약**: 게임 로직의 난수는 `Game.Random("이름")`(결정론 난수 스트림), 게임 로직의 시간은 `session.Clock`(게임 시계)을 쓴다. 둘 다 Undo·스냅샷 되감기·판 저장에 함께 따라간다. 일시정지·배속은 `GameTime`이 `Time.timeScale`로 한꺼번에 조절한다. 꺼 둔 동안에도 흐르는 현실 시각(오프라인 보상·일일 초기화)은 `Game.Now`·`Game.Day`를 쓴다. 행동에 찍혀 다시 돌려도 같다. 행운·"모든 확률 2배"가 고칠 수 있는 확률은 `Game.Chance`로 굴린다. 시드 입력·일일 도전은 시드를 주고 새 판을 연다(`GBibleRuntime.NewRun(session, seed)`).

**언제 쓰는가**

- 명중·치명타·드롭·맵 생성 같은 규칙의 무작위
- "25% 확률로"처럼 유물·패시브 효과가 바꿀 수 있는 확률(`Game.Chance`, DB 조건 '확률')
- 시드 입력("ABCD1234")·일일 도전(오늘은 모두 같은 판)
- 쿨다운·지속 시간처럼 게임 시간 기준의 값
- 메뉴·대화창이 열렸을 때 게임 멈추기, 슬로우 모션, 개발 중 프레임 단위 진행
- 오프라인 보상·스태미나 회복·일일·주간 초기화처럼 현실 시각 기준의 규칙

---

## 0. 용어

| 용어 | 뜻 |
|---|---|
| 결정론 난수 | 같은 시드에서 같은 순서로 부르면 기기·플랫폼과 상관없이 같은 수가 나오는 난수. 정수 연산만 쓴다(xoshiro256**). |
| 마스터 시드 (`SessionOptions.Seed`) | 세션 전체의 씨앗. 스트림마다의 시드가 여기서 나온다. |
| 스트림 (`RandomStream`) | 이름 하나("damage", "loot" …)에 붙은 난수 흐름. 스트림끼리 독립이라 한 곳에서 더 뽑아도 다른 곳의 결과가 바뀌지 않는다. "난수 흐름"이라고도 쓴다. |
| 행동 직전 이벤트 '확률' (`ChanceIntent`) | `Game.Chance`가 굴리기 직전에 행동 직전 이벤트에 보이는 내용(확률·흐름·굴리는 쪽). 행동 직전 이벤트가 확률을 바꾸거나 막는다(28장). |
| 글자 시드 | 플레이어가 적거나 나누는 시드 글자("ABCD1234", "2026-10-05"). `RandomStreams.SeedOf`가 숫자 시드로 바꾼다. |
| 게임 시계 (`GameClock`) | 세션의 시간. `Tick`(프레임 수)과 `Time`(초). 프레임 끝마다 흐른다. |
| `GameTime` | Unity 쪽 시간 조절(일시정지·배속·프레임 넘기기). `Time.timeScale`을 바꾼다. |
| UTC | 협정 세계시. 시간대와 상관없는 기준 시각. |
| 현실 시각 (`Game.Now`) | 실제 세계의 시각(UTC). 게임을 꺼 둔 동안·일시정지 중에도 흐른다. 바깥 행동이 시작할 때 한 번 찍혀 행동의 `utc`에 남는다. |
| 날짜 번호 (`Game.Day` · `Week`) | 정한 시간대·초기화 시각에 하루(한 주)마다 1씩 오르는 정수. 지난번 번호와 다르면 날(주)이 바뀐 것이다. |
| 행동 · Undo · 스냅샷 | [02-Actions-Undo.md](02-Actions-Undo.md) 참고. |

---

## 1. 에디터에서 쓰는 법 (모니터)

플레이 중 **MomoiProject › 모니터**:

- 시드 고정: DB › 시작 파일 › Bootstrap의 **개발 빌드에서 시드 고정** + **시드**(에디터 플레이는 개발 빌드다. 출시 빌드는 늘 지금 시각을 시드로 쓴다). 버그를 같은 순서로 재현할 때 켠다(2.3).
- 위쪽 줄에 "시드 … · 틱 … · …초"가 보인다(마스터 시드, 게임 시계).
- 위쪽 시간 조절: **일시정지**/**재개** 버튼(멈춘 이유가 툴팁에 보인다), **1프레임**(일시정지 중 한 프레임만 진행), 배속 선택 **0.25× · 0.5× · 1× · 2× · 4×**.
- **명령** 탭의 **시간** 묶음: **일시정지 / 재개**, **1프레임 진행**(일시정지 중에만 동작), **배속 0.25**, **배속 1**, **배속 2**.
- 모니터의 일시정지는 "개발 도구"라는 이유로 멈춘다. 게임 코드가 건 일시정지와 겹치며, 각자 풀어야 재개된다.
- **행동** 탭에서 행동 줄을 누르면 탐색기에 `tick`(게임 시계)과 `utc`(현실 시각, UTC 밀리초)가 보인다.
- 현실 시각의 출처·시간대(2.13)는 화면 칸이 없다. 코드(`Configuring`)로 정한다.

---

## 2. 코드로 쓰는 법 — 문법 요소

### 2.1 스트림 얻기: `Game.Random(name)`

```csharp
RandomStream damage = Game.Random("damage");      // session.Random.Get("damage")와 같다
```

처음 부를 때 그 이름의 스트림을 만들고, 그 뒤로는 같은 스트림을 준다(빈 이름은 `ArgumentException`). 스트림 시드는 마스터 시드와 이름으로 정해진다. 이름은 쓰임 하나에 하나로 나눈다("damage", "loot", "map" — 관례 2절). 그러면 전투에서 난수를 한 번 더 뽑아도 드롭 결과는 그대로다.

### 2.2 수 뽑기: `RandomStream`

```csharp
int roll = damage.Range(1, 7);         // 1 이상 7 미만의 정수(주사위). 편향 없음
double t = damage.NextDouble();         // 0 이상 1 미만
bool crit = damage.Chance(0.15);        // 15% 확률로 true
uint u = damage.NextUInt();
ulong raw = damage.NextULong();
```

| 멤버 | 뜻 |
|---|---|
| `Range(min, max)` | `min` 이상 `max` 미만 정수. `max <= min`이면 `ArgumentException` |
| `NextDouble()` | [0, 1) 실수 |
| `Chance(p)` | `NextDouble() < p` |
| `NextUInt()` · `NextULong()` | 32·64비트 정수 |
| `Name` | 스트림 이름 |
| `GetState()` · `SetState(ulong[4])` · `Reseed(seed)` | 상태 꺼내기·넣기·다시 시드(도구·테스트용) |

`rng.Chance(p)`는 행동 직전 이벤트를 거치지 않는다. 기획에 "N% 확률로"로 나오는 확률(명중·치명타·드롭·발동 확률 — 행운·"모든 확률 2배" 같은 기믹이 닿을 수 있는 것)은 모두 `Game.Chance`(2.15)로 굴린다(관례 4절). `rng.Chance`는 맵·배치 생성처럼 기믹이 닿지 않는 내부 무작위에만 쓴다.

### 2.3 시드 정하기: `SessionOptions.Seed`

```csharp
using GBible;
using UnityEngine;

public static class SeedSetup
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void Hook() => GBibleRuntime.Configuring += options => options.Seed = 20261003;   // 고정 시드(재현용)
}
```

`GBibleRuntime`은 기본으로 현재 시각을 시드로 쓴다(플레이마다 다름). 개발 중 버그 재현은 코드 없이 DB › 시작 파일 › Bootstrap의 **개발 빌드에서 시드 고정**을 켠다(1절). 출시 빌드까지 늘 같은 시드여야 할 때만 위처럼 `Configuring`에서 정한다(코드가 Bootstrap 값을 이긴다). 세션을 직접 만들면 `new GameSession(new SessionOptions { Seed = 7 })`. 판마다 시드를 정하는 시드 입력·일일 도전은 새 판을 열 때 시드를 준다(2.16).

### 2.4 스트림 묶음: `session.Random` (`RandomStreams`)

| 멤버 | 뜻 |
|---|---|
| `Get(name)` | 스트림(없으면 만든다) |
| `MasterSeed` | 마스터 시드 |
| `Reseed(seed)` | 마스터 시드를 바꾸고 모든 스트림을 처음 상태로 |
| `static SeedOf(text)` | 글자 시드 → 숫자 시드(`ulong`). 앞뒤 빈칸을 떼고 대문자로 맞춘다(2.16) |
| `Streams` | 만들어진 스트림들 |
| `ExportStates()` · `ImportStates(states)` · `Restore(states)` | 상태 꺼내기·넣기·되돌리기(시스템이 Undo·스냅샷·판 저장에 쓴다) |

### 2.5 게임 시계: `session.Clock` (`GameClock`)

```csharp
long tick = Game.Session.Clock.Tick;     // 프레임 끝을 지난 횟수(고정 틱이면 틱 수)
double now = Game.Session.Clock.Time;    // 게임 시간(초)
```

- `GBibleRuntime`이 매 프레임 끝(`LateUpdate`)에 `session.EndFrame(Time.deltaTime)`을 부르고, 그때 `Tick`이 1 오르고 `Time`에 `deltaTime`이 더해진다. `Time.deltaTime`은 `Time.timeScale`을 따르므로 배속·일시정지가 시계에 그대로 반영된다.
- 고정 틱(DB › Bootstrap의 초당 틱 수, `33-Tick-Preview-Csv.md`)을 켜면 프레임마다가 아니라 틱마다 `Tick`이 1 오르고 `Time`에 1/틱 초가 더해진다(한 프레임에 여러 틱일 수 있다).
- 시계가 한 번 흐를 때마다(행동 밖에서) 시간 규칙이 돈다: 상태 효과 주기·만료 → 패시브 효과 주기 → 시간 예약 → `session.Ticked`(29장·33장).
- 일시정지 중(`deltaTime`이 0)에는 `EndFrame`을 부르지 않는다. 시계가 멈추고 스냅샷도 찍히지 않지만, 플레이어 데이터 Sync와 조건 평가는 계속한다.
- 세션을 직접 만들었다면 `session.EndFrame(deltaTime)`을 직접 부른다.
- 행동을 실행하면 그때의 `Tick`이 행동의 `tick` 필드에 적힌다. 기록(Journal)의 각 줄도 틱을 갖는다.
- 쿨다운·지속 시간처럼 되감기·저장에 따라가야 하는 시각은 `Time.time`이 아니라 `Clock.Time` 기준으로 플레이어 데이터에 적는다.

### 2.6 일시정지: `GameTime.Pause` · `Resume`

```csharp
GameTime.Pause("메뉴");          // 이유별로 겹친다
GameTime.Pause("대화");
GameTime.Resume("메뉴");         // 아직 "대화"가 남아 있어 계속 멈춤
GameTime.Resume("대화");         // 이제 재개

GameTime.TogglePause("메뉴");
GameTime.ResumeAll();            // 모든 이유를 한 번에(개발 도구·긴급 복구용)
bool paused = GameTime.IsPaused;
foreach (var why in GameTime.PausedBy) { /* 멈춘 이유들 */ }
```

이유를 생략하면 `"게임"`이다. 멈춘 이유가 하나라도 남아 있으면 멈춘 상태다.

### 2.7 배속: `GameTime.SetSpeed`

```csharp
GameTime.SetSpeed(0.5f);   // 슬로우
GameTime.SetSpeed(2f);     // 두 배
float s = GameTime.Speed;
```

`GameTime.MinSpeed`(0.05)부터 `GameTime.MaxSpeed`(16)까지로 잘린다. 일시정지 중에 바꾸면 재개할 때 적용된다. 1보다 느리게 하면 물리 간격(`Time.fixedDeltaTime`)도 배속에 맞춰 줄여 물리가 끊기지 않게 한다.

### 2.8 프레임 넘기기: `GameTime.StepFrame`

```csharp
GameTime.StepFrame();      // 일시정지 중에만: 한 프레임 진행
GameTime.StepFrame(10);    // 열 프레임
bool stepping = GameTime.IsStepping;
```

개발용이다. 일시정지가 아니면 아무 일도 하지 않는다.

### 2.9 상태 바뀜: `GameTime.Changed`

```csharp
GameTime.Changed += () => pausePanel.SetActive(GameTime.IsPaused);
```

일시정지·배속·프레임 넘기기 상태가 바뀔 때 쏜다. 플레이를 시작할 때마다 `GameTime`의 상태와 `Changed` 받기 목록이 초기화되므로 플레이마다 다시 등록한다.

### 2.10 `GameTime`이 영향을 주는 것과 주지 않는 것

| 함께 멈추고 빨라지는 것 | 영향을 받지 않는 것 |
|---|---|
| `Time.deltaTime`을 쓰는 `Update` | `Time.unscaledDeltaTime`을 쓰는 코드 |
| 물리(`FixedUpdate`) | 애니메이터의 Unscaled Time 모드 |
| 애니메이터(Normal 모드), 파티클 | `WaitForSecondsRealtime` |
| 코루틴의 `WaitForSeconds` | 오디오 피치 |
| 트윈(기본 설정) | UI 연출 |
| 게임 시계(`session.Clock`)와 스냅샷 간격 | |

메뉴·일시정지 화면처럼 멈추면 안 되는 연출만 오른쪽 방식으로 만든다.

### 2.11 현실 시각: `Game.Now`

```csharp
DateTime now = Game.Now;                                   // UTC. 행동 안이면 그 행동이 시작한 순간
TimeSpan away = Game.Now - profile.lastSeen;               // 꺼 둔 시간(오프라인 보상)
```

- 바깥 행동이 시작할 때 시스템이 현실 시각을 한 번 찍어 행동의 `utc`(`long`, 1970-01-01부터 UTC 밀리초)에 적는다. 그 행동 안에서는 `Game.Now`가 늘 그 값이다. 안쪽 행동·트리거 결과·Block도 같은 값을 본다. 행동 하나 = 한 순간이다.
- 이미 `utc`가 있는 행동(멀티플레이로 받은 행동, 기록에서 다시 돌리는 행동)은 그 값을 그대로 쓴다. 그래서 어디서 언제 다시 돌려도 결과가 같다.
- 미리보기(`Game.Preview`·`CanDo`)도 시각을 찍고, 끝나면 지운다(같은 객체를 나중에 `Do`하면 그때 시각).
- 행동 밖(씬 코드)에서는 부를 때의 지금 시각이다. 화면의 "남은 시간" 표시에 쓴다.
- 규칙(행동·Block·트리거 결과)에서는 `DateTime.Now`·`DateTime.UtcNow`를 쓰지 않는다. `Game.Now`를 쓴다.
- 플레이어 데이터에 시각을 적을 때는 `DateTime` 필드(또는 `long` 밀리초)에 `Game.Now`를 넣는다. 저장·Undo가 그대로 따라간다.
- **기간 한정(이벤트 시작·끝 날짜)**: DB에는 날짜 칸이 없다. DB 종류에 UTC 글자 칸(`2026-11-01T00:00:00Z`)을 두고, 규칙이 `DateTime.Parse(글자, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)`로 읽어 `Game.Now`와 비교한다. 형식 검사는 그 DB 종류의 `ValidateData(IValidationContext context)`에서 한다(읽지 못하면 `context.Error(…)` — 개발자가 잘못 적으면 DB 검증에 뜬다).

### 2.12 날짜 번호: `Game.Day` · `Game.Week`

```csharp
long today = Game.Day();                          // 자정에 바뀐다
long resetDay = Game.Day(4);                      // 새벽 4시에 바뀐다(4시 전은 어제)
long week = Game.Week(4);                         // 월요일 새벽 4시에 바뀐다
long sundayWeek = Game.Week(0, DayOfWeek.Sunday); // 일요일 자정에 바뀐다
```

| 멤버 | 뜻 |
|---|---|
| `Day(resetHour = 0)` | `Game.Now`를 시간대(2.13)의 시각으로 바꿔, `resetHour`시에 바뀌는 날짜 번호. 1970-01-01이 0 |
| `Week(resetHour = 0, start = DayOfWeek.Monday)` | `start` 요일 `resetHour`시에 바뀌는 주 번호 |

- 번호 자체에는 뜻이 없다. 플레이어 데이터에 마지막 번호를 두고, 지금 번호와 다른지만 본다.
- 두 번호의 차가 지난 날 수다. 연속 출석은 `today - lastDay == 1`.
- `Game.Now`를 쓰므로 행동 안에서는 행동이 시작한 순간 기준이다.

### 2.13 시각의 출처와 시간대: `SessionOptions.RealClock` · `TimeZone`

```csharp
using System;
using GBible;
using UnityEngine;

public static class ClockSetup
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void Hook() => GBibleRuntime.Configuring += options =>
    {
        options.RealClock = () => ServerClock.UtcNow;   // 서버 시각(게임 코드). 비우면 DateTime.UtcNow
        options.TimeZone = TimeZoneInfo.CreateCustomTimeZone("KST", TimeSpan.FromHours(9), "KST", "KST");   // 모두 한국 시각으로
    };
}
```

| 옵션 | 기본 | 뜻 |
|---|---|---|
| `RealClock` (`Func<DateTime>`) | 비움 = 이 컴퓨터의 시계(`DateTime.UtcNow`) | 현실 시각의 출처. UTC(`DateTimeKind.Utc`)를 돌려준다. 시험에서는 고정 시각(4.9), 서버·Steam 시각을 쓸 때는 그것 |
| `TimeZone` (`TimeZoneInfo`) | 비움 = 싱글은 이 컴퓨터의 시간대, 멀티(서버·클라이언트)는 UTC | `Day`·`Week`를 셀 시간대 |

- 비우면 싱글 게임은 플레이어마다 자기 지역 시각으로 날이 바뀐다(싱글은 이대로 둔다). 멀티플레이(세션이 서버·클라이언트)는 서버·클라이언트 모두 UTC로 세고(판정과 미리보기가 다른 날을 세지 않게), 처음 셀 때 타임라인에 경고를 한 번 남긴다 — 멀티는 시간대를 정한다.
- 모든 플레이어가 같은 순간에 초기화돼야 하면(서버 이벤트·랭킹) 시간대를 정한다. 시간대 이름(`FindSystemTimeZoneById`)은 Windows·모바일이 달라서, 위처럼 고정 차이로 만든다.
- DB › Bootstrap에 칸은 없다. `Configuring`에서, 세션을 직접 만들면 `new SessionOptions { … }`에서 정한다.
- 개발 빌드·에디터에서는 `GBibleRuntime`이 `Configuring` 뒤에 이 시계 위로 테스트용 옮김(`TestClock.Offset` — 개발 명령 '시간 › 현실 시각 +1시간 / +1일 / 지금으로', 4.7)을 더한다(출시 빌드에서는 얹지 않는다). 규칙은 `TestClock`을 직접 읽지 않는다(`Game.Now`·`Game.Day`만).

### 2.14 게임 시계와 현실 시각 고르기

| | 게임 시계 `session.Clock` | 현실 시각 `Game.Now` |
|---|---|---|
| 흐르는 때 | 게임이 켜져 있고 멈추지 않았을 때. 배속을 따른다 | 늘. 꺼 둔 동안·일시정지 중에도 |
| 값 | `Time`(초)·`Tick` | `DateTime`(UTC) |
| 행동에 적히는 것 | `tick` | `utc` |
| 스냅샷 되감기 | 시계가 함께 돌아간다 | 돌아가지 않는다(규칙이 플레이어 데이터에 적은 시각만 돌아간다) |
| 판 저장 | 판에 저장, 불러오면 이어서 흐른다 | 저장하지 않는다(적어 둔 시각만 플레이어 데이터로 저장) |
| 맞는 곳 | 쿨다운·버프 지속·예약·상태 효과 | 오프라인 보상·스태미나 회복·일일·주간 초기화·기간 한정 이벤트 |

### 2.15 고칠 수 있는 확률: `Game.Chance`

```csharp
if (Game.Chance(0.25, "loot", source: GameIds.Passive.EightBall)) { … }   // 25%. 흐름을 비우면 "chance"
bool lucky = Game.Chance(0.1, user: hero);                                 // 굴리는 쪽(사용자)도 행동 직전 이벤트에 실린다
```

| 매개변수 | 뜻 |
|---|---|
| `probability` | 0~1. 1 이상이면 반드시 성공, 0 이하면 늘 실패 |
| `stream` | 난수 흐름 이름(기본 `"chance"`, 비워도 `"chance"`) |
| `source` | 굴리는 쪽의 항목 ID(패시브 효과·액티브 효과). 행동 직전 이벤트의 `sourceId` |
| `user` | 사용자·가진 쪽(객체 또는 id). 행동 직전 이벤트의 `userId` |

1. 굴리기 전에 행동 직전 이벤트 **'확률'**(`ChanceIntent { probability, stream, sourceId, userId, cancelled }`)을 행동 직전 이벤트에 보인다.
2. 행동 직전 이벤트가 `probability`를 바꾸면 바뀐 확률로 굴린다. "모든 확률 2배" = 행동 직전 이벤트 '확률' 받기 → `probability` 곱하기 2. 막으면(`cancelled`) 실패다.
3. 난수는 **늘 한 번** 굴린다. 확률이 바뀌거나 막혀도 같다. 그래서 같은 시드면 뒤의 결과가 밀리지 않는다.

- 행동 안에서 부른다. 행동 직전 이벤트 결과가 그 행동 안에서 돈다.
- DB에서는 조건 **'확률'**(칸: `percent`(확률(%)) · `stream`(난수 흐름(비우면 chance)))을 이벤트 받기·행동 직전 이벤트 받기·사용 트리거와 `&& AND`로 쓴다. 굴리는 때·굴리지 않는 때는 11장 2-6.
- 행동 직전 이벤트 '확률'을 고치는 법은 28장 4-14.

### 2.16 시드로 새 판: `NewRun(seed)` · `RandomStreams.SeedOf`

```csharp
GBibleRuntime.NewRun(session, RandomStreams.SeedOf("ABCD1234"));    // 시드 입력: 같은 글자면 같은 판(run.json도 지운다)
session.NewRun(RandomStreams.SeedOf("2026-10-05"));                  // 세션만(테스트·서버, 파일은 모른다)
GBibleRuntime.NewRun(session);                                       // 시드 없이: 난수는 이어서 흐른다
```

- `GBibleRuntime.NewRun(session, seed)` · `session.NewRun(seed)`: 판 값을 처음 값으로 되돌린 뒤 마스터 시드를 그 시드로 바꾸고 모든 스트림을 처음 상태로 맞춘다(`session.Random.Reseed`). 같은 시드 = 같은 판이다. 시드를 주지 않으면 스트림은 앞 판에서 이어서 흐른다.
- `RandomStreams.SeedOf(글자)`: 앞뒤 빈칸을 떼고 대문자로 맞춘 뒤 숫자 시드로 바꾼다. `" abcd1234 "`와 `"ABCD1234"`는 같은 판이다.
- 일일 도전 = 날짜 글자를 시드로 쓴다. 모두 같은 날 같은 판을 받는다.
- 같은 시드여도 판을 여는 행동이 난수를 뽑는 차례가 같아야 같은 판이다. 덱·맵은 `NewRun` 바로 뒤의 새 판 행동에서 만든다.
- 시드로 연 판도 판 저장에 마스터 시드·스트림 상태가 들어간다. 이어 하면 그 시드 그대로다(3절).
- 새 판 자체의 규칙(행동 밖에서 부른다, 판 저장 파일 삭제)은 06장 2.3이다.

---

## 3. Undo·스냅샷·저장과 함께 움직이는 방식

| 사건 | 난수 | 게임 시계 | 현실 시각 |
|---|---|---|---|
| 행동 실행 | 행동 직전에 **모든 스트림 상태**를 보관한다 | 행동에 `tick`을 적는다 | 행동에 `utc`를 적는다(이미 있으면 그대로, 안쪽 행동은 바깥 행동의 값) |
| 행동 실패(예외) | 보관한 상태로 되돌린다 | — | — |
| Undo (행동 단위 방식) | 그 행동 직전 상태로 되돌린다(그 뒤에 처음 만든 스트림은 처음 상태로) → 같은 행동을 다시 하면 같은 결과 | 되돌리지 않는다(시간은 계속 흐른다) | 되돌리지 않는다. 그 행동이 플레이어 데이터에 적은 시각은 돌아간다 |
| Undo (스냅샷 방식) | 스냅샷 때 상태로. 그 뒤에 처음 만든 스트림은 처음 상태로 | 스냅샷 때의 `Tick`·`Time`으로 되감는다 | 되돌리지 않는다 |
| 판 저장(`ExportRun`) | 마스터 시드와 모든 스트림 상태를 저장 | `Tick`·`Time`을 저장 | 저장하지 않는다 |
| 판 불러오기(`ImportRun`) | 마스터 시드·스트림 상태를 되살린다 → 다음 결과까지 같다 | 저장한 시각으로 | — (불러온 뒤의 행동은 그때의 시각) |
| 프로필(`ExportMeta`) | 저장하지 않는다 | 저장하지 않는다 | 저장하지 않는다 |
| 새 판(`NewRun(seed)`) | 시드를 주면 마스터 시드를 바꾸고 모든 스트림을 처음 상태로. 주지 않으면 그대로 이어서 | 그대로 | — |

**주의 — Undo는 "모든 스트림"을 행동 직전으로 되돌린다.** 그 행동 뒤에 행동 밖에서 같은 세션의 난수를 뽑았다면(씬 코드의 연출 등) 그 소비도 함께 되돌아간다. 연출용 무작위(파티클 흔들림, 대사 고르기 등)는 `UnityEngine.Random`을 쓰고, `Game.Random`은 규칙 계산에만 쓴다.

---

## 4. 상황별 예제

예제는 문법을 보이려고 숫자(명중률·8시간·새벽 4시)를 코드에 적었다. 프로젝트에서는 바꿀 숫자를 DB › 밸런싱 값(`Game.Balance`, 30장)에서 읽고, 피해·회복은 규칙 함수(행동 직전 이벤트 자리, 관례 4절)를 거친다.

### 4.1 턴제: 명중·치명타·피해

```csharp
using System;
using GBible;

[Serializable]
public class Duelist
{
    public string id;
    public int hp = 40;
    public int accuracy = 80;     // %
}

[Serializable]
public class Slash : GameAction
{
    public Duelist target;                                            // 대상은 객체로
    public int baseDamage = 8;

    protected override void Run()
    {
        if (!Game.Chance(target.accuracy / 100.0, "combat")) return;  // 빗나감(난수 소비도 Undo 단위). "명중 +10%" 기믹이 고칠 수 있다
        var damage = baseDamage + Game.Random("combat").Range(0, 4);  // 피해 폭: 기믹이 닿지 않는 무작위
        if (Game.Chance(0.15, "combat")) damage *= 2;                 // 치명타
        target.hp -= damage;
    }
}
```

빗나가도 행동은 Undo 한 단위로 남는다. Undo 후 다시 베면 같은 난수가 나와 결과가 같다(되돌리기로 치명타를 다시 뽑을 수 없다).

### 4.2 드롭 테이블: 가중치 고르기

```csharp
using System;
using System.Collections.Generic;
using GBible;

[Serializable]
public class LootBag
{
    public List<string> items = new List<string>();
}

[Serializable]
public class OpenChest : GameAction
{
    static readonly (string item, int weight)[] Table = { ("potion", 60), ("ironSword", 30), ("dragonScale", 10) };

    protected override void Run()
    {
        var rng = Game.Random("loot");
        var total = 0;
        foreach (var row in Table) total += row.weight;
        var pick = rng.Range(0, total);
        foreach (var row in Table)
        {
            if (pick < row.weight) { Game.All<LootBag>()[0].items.Add(row.item); return; }
            pick -= row.weight;
        }
    }
}
```

"loot" 스트림만 쓰므로, 전투에서 난수를 몇 번 뽑든 상자 결과의 순서는 바뀌지 않는다. 이 예는 스트림 나누기만 보인다 — 실제 드롭·보상·조우 표는 코드에 두지 않고 DB › 가중치 표 + `Game.Roll`(표마다 따로인 결정론 흐름, 30장)로 만든다.

### 4.3 맵 생성: 판 시드로 같은 맵 다시 만들기

```csharp
using System;
using System.Collections.Generic;
using GBible;

[Serializable]
public class DungeonMap
{
    public int width = 8, height = 8;
    public List<int> walls = new List<int>();
}

[Serializable]
public class GenerateFloor : GameAction
{
    public int floor;
    protected override void Run()
    {
        var map = Game.All<DungeonMap>()[0];
        var rng = Game.Random("map." + floor);        // 층마다 다른 스트림
        map.walls.Clear();
        for (int i = 0; i < map.width * map.height; i++)
            if (rng.Chance(0.2)) map.walls.Add(i);
    }
}
```

같은 마스터 시드면 같은 층에서 같은 맵이 나온다. 층마다 스트림을 나누면 앞 층에서 더 뽑아도 뒤 층 맵이 바뀌지 않는다.

### 4.4 실시간: 게임 시계 기준 버프 지속 시간

```csharp
using System;
using System.Collections.Generic;
using GBible;
using UnityEngine;

[Serializable]
public class TimedBuff
{
    public string name;
    public double endsAt;           // session.Clock.Time 기준
}

[Serializable]
public class Runner
{
    public string id;
    public List<TimedBuff> buffs = new List<TimedBuff>();
}

[Serializable]
public class DrinkHaste : GameAction
{
    public Runner runner;
    public double seconds = 5;
    protected override void Run() =>
        runner.buffs.Add(new TimedBuff { name = "haste", endsAt = Game.Session.Clock.Time + seconds });
}

[Serializable]
public class ExpireBuffs : GameAction
{
    public Runner runner;
    protected override void Run()
    {
        var now = Game.Session.Clock.Time;
        runner.buffs.RemoveAll(b => b.endsAt <= now);
    }
}

public class BuffTicker : MonoBehaviour
{
    public string runnerId = "player";

    void Update()
    {
        var game = GBibleRuntime.Game;
        var runner = game?.Find<Runner>(runnerId);
        if (runner == null) return;
        var now = game.Session.Clock.Time;
        if (runner.buffs.Exists(b => b.endsAt <= now)) game.Do(new ExpireBuffs { runner = runner });
    }
}
```

일시정지 중에는 시계가 멈춰 버프 시간도 줄지 않는다. 스냅샷을 되감으면 시계와 `endsAt`이 함께 돌아간다. 만료 처리도 행동이라 되감기·기록에 함께 들어간다. 이 예는 `Clock.Time` 기준으로 적는 법만 보인다 — 실제 버프·쿨다운("N초 동안")은 상태 효과(게임 시간 지속, 29장)로 만든다(관례 10절).

### 4.5 메뉴·대화창 일시정지

```csharp
using GBible;
using UnityEngine;

public class PauseMenu : MonoBehaviour
{
    public GameObject panel;

    void OnEnable() => GameTime.Changed += Refresh;
    void OnDisable() => GameTime.Changed -= Refresh;

    public void Open() => GameTime.Pause("메뉴");
    public void Close() => GameTime.Resume("메뉴");

    void Refresh() => panel.SetActive(System.Linq.Enumerable.Contains(GameTime.PausedBy, "메뉴"));
}

public class DialogueBox : MonoBehaviour
{
    public void Show() => GameTime.Pause("대화");
    public void Hide() => GameTime.Resume("대화");
}
```

메뉴를 연 채로 대화가 끝나도 메뉴의 일시정지가 남아 있어 게임이 재개되지 않는다.

### 4.6 테스트: 같은 시드 = 같은 결과

```csharp
using GBible;

public static class SeedCheck
{
    public static bool SameSequence()
    {
        var a = new GameSession(new SessionOptions { Seed = 99 }).Random.Get("combat");
        var b = new GameSession(new SessionOptions { Seed = 99 }).Random.Get("combat");
        for (int i = 0; i < 100; i++)
            if (a.NextULong() != b.NextULong()) return false;
        return true;
    }
}
```

### 4.7 방치형: 오프라인 보상

**정산** = 마지막으로 정산한 현실 시각부터 지금(`Game.Now`)까지의 생산을 더하고 그 시각을 지금으로 옮기는 행동 하나다. 켜 둔 동안은 1초마다, 이어 할 때·끌 때 한 번씩 같은 행동을 부른다. 쿠키를 쓰는 행동(사기)은 규칙 함수 `IdleRules.Settle`을 먼저 부른다.

**에디터**(DB 항목은 AI가 반영 스크립트로 — 관례 15절)

- DB › 플레이어 데이터 `run`(세션 구역 = 환생 한 주기): 아래 `RunState`. 마지막 정산 시각도 판 데이터라 판 저장에 쿠키와 함께 남는다(따로 저장되면 어긋난다).
- DB › 밸런싱 값 `offlineMaxHours` `8`(꺼 둔 시간 상한(시간)) · `onlineGapSeconds` `5`(켜 둔 것으로 볼 정산 간격(초) — 넘는 몫은 꺼 둔 시간).
- DB › 시작 파일 › Bootstrap: 시작할 때 이어 하기 켬, 새 판 행동 `StartRun`, 판 저장 'N초마다 저장'. **프로필도 행동마다 저장**은 이 예처럼 정산이 판 데이터만 바꾸면 켜도 매초 쓰지 않는다(프로필을 바꾼 행동에서만 쓴다). 정산이 프로필(영구 배율·누적 로그·도전과제 진행)을 매초 바꾸면 끈다 — N초마다 판 저장이 바뀐 프로필도 함께 저장한다(06장 2.2).

```csharp
// Assets/MomoiProject/Code/Rules/Idle.cs
using System;
using GBible;
using GBible.Ids;

[Serializable]
public class RunState                              // 플레이어 데이터 run(세션 구역)
{
    public double cookies;
    public double perSecond = 1;
    public DateTime lastSettled;                   // 마지막으로 정산한 현실 시각(UTC)
}

[Serializable, BlockInfo("새 판")]
public class StartRun : GameAction                 // DB › Bootstrap '새 판 행동'
{
    protected override void Run()
    {
        Game.Data().Run.lastSettled = Game.Now;    // 새 판은 지금부터 센다(꺼 둔 시간이 없다)
        // … 시작 건물·화폐
    }
}

[Serializable, BlockInfo("정산")]
public class Settle : GameAction
{
    protected override void Run() => IdleRules.Settle(Game);
}

public static class IdleRules
{
    public static void Settle(GameApi game)
    {
        var run = game.Data().Run;
        var now = game.Now;
        if (run.lastSettled == default) { run.lastSettled = now; return; }          // 시각이 비었으면 시각만(5절 마지막 줄)
        var elapsed = Math.Max(0, (now - run.lastSettled).TotalSeconds);           // 시계를 뒤로 돌렸으면 0
        var online = Math.Min(elapsed, game.Balance(GameIds.Balance.OnlineGapSeconds));                          // 켜 둔 몫
        var offline = Math.Min(elapsed - online, game.Balance(GameIds.Balance.OfflineMaxHours) * 3600);           // 넘는 몫 = 꺼 둔 시간(상한까지)
        run.cookies += (online + offline) * run.perSecond;
        run.lastSettled = now;
    }

    // 켜 둔 동안 1초마다 정산(Bootstrap.RegisterRules에서 부른다 — 33장 5-1의 등록 함수 모양)
    public static void Register(GameSession session)
    {
        var wait = 0.0;                            // 세션마다 따로(규칙에 static 상태를 두지 않는다)
        session.Ticked += dt =>
        {
            wait += dt;
            if (wait < 1) return;                  // 틱마다 행동하지 않는다(33장 5-1)
            wait = 0;
            session.Do(new Settle());
        };
    }
}
```

```csharp
// Assets/MomoiProject/Code/Client/Bootstrap.cs(시작 설정이 만든 파일)에 더한다
static void Hook()
{
    GBibleRuntime.Configuring += Configure;
    GBibleRuntime.SessionCreated += RegisterRules;
    GBibleRuntime.Started += Started;
    GBibleRuntime.RunLoaded += session => session.Do(new Settle());   // 이어 하기(켤 때)·판 불러오기: 꺼 둔 시간만큼
    GBibleRuntime.Stopping += session => session.Do(new Settle());    // 끌 때: 저장 전에 마지막 시각을 남긴다
}

static void RegisterRules(GameSession session)
{
    IdleRules.Register(session);
}
```

- **켤 때 정산 자리는 `RunLoaded`와 새 판 행동이다**(`Started`가 아니다). `Started`는 프로필만 불러온 뒤에 오고, '시작할 때 이어 하기'는 그 **뒤에** 판 저장을 불러온다(`RunLoaded`) — 저장이 없으면 새 판 행동을 돌린다(20장 3절). `Started`에서 정산하면 판이 아직 처음 값이라 시각만 적히고, 곧 불러온 판이 그것을 덮는다.
- **켜 둔 시간과 꺼 둔 시간의 경계**: 켜 둔 동안은 정산이 1초마다 돌아 간격이 `onlineGapSeconds`를 넘지 않는다. 넘는 몫은 그 사이 게임이 돌지 않은 시간(꺼 둠·일시정지·백그라운드에서 멈춤 — Run In Background를 끄면 창이 뒤에 있을 때 프레임이 멈춘다)이라 꺼 둔 시간으로 센다. 꺼 둔 몫에 다른 배율(예: 50%)을 쓰는 게임이면 그 배율도 밸런싱 값으로 두고 `offline`에 곱한다.
- 강제 종료되면 `Stopping`이 오지 않는다. 다음에 켜면 마지막으로 **저장된** 판(쿠키와 정산 시각이 함께 저장됨)부터 다시 세므로 두 번 더하지 않는다.
- **시험**: 플레이 중 개발 명령 **시간 › 현실 시각 +1시간**(테스트 패널·모니터 명령 탭, 22장)을 누르면 다음 정산이 한 시간을 꺼 둔 시간으로 더한다. **+1일**이면 상한에서 잘리는지 본다. **현실 시각 지금으로**는 되돌린다(뒤로 간 시간은 0). 옮긴 시각은 개발 빌드·에디터에서만 `Game.Now`에 얹히고 플레이를 멈추면 돌아온다(`TestClock`, 38장). 테스트 상황은 저장한 순간의 현실 시각을 남기고 불러올 때 그 시각으로 맞추므로, 며칠 뒤 열어도 오프라인 보상이 끼지 않는다. 규칙 테스트는 4.9처럼 `RealClock`을 정해 두고 옮긴다.

### 4.8 일일·주간 초기화(새벽 4시)

```csharp
using System;
using GBible;

[Serializable]
public class DailyProfile
{
    public long lastDay = -1;          // 마지막으로 초기화한 날짜 번호
    public long lastWeek = -1;
    public int questsDone;
    public int weeklyBossTries;
}

[Serializable, BlockInfo("날짜 확인")]
public class CheckReset : GameAction
{
    public const int ResetHour = 4;

    protected override void Run()
    {
        var p = Game.All<DailyProfile>()[0];
        var today = Game.Day(ResetHour);
        if (p.lastDay != today) { p.questsDone = 0; p.lastDay = today; }
        var week = Game.Week(ResetHour);                       // 월요일 새벽 4시
        if (p.lastWeek != week) { p.weeklyBossTries = 3; p.lastWeek = week; }
    }
}
```

켤 때(`Started`)와 일일 임무를 쓰는 행동 안에서 먼저 `Game.Do(new CheckReset())`을 부른다. 켜 둔 채 4시를 넘겨도 다음 행동에서 초기화된다. 행동 안에서 부르면 바깥 행동의 일부라 시각도 같다.

### 4.9 시험: 시각을 정해 두고 날 넘기기

```csharp
using System;
using GBible;

public static class ResetCheck
{
    public static bool ResetsAtFour()
    {
        var clock = new DateTime(2026, 10, 4, 3, 59, 0, DateTimeKind.Utc);
        var session = new GameSession(new SessionOptions { RealClock = () => clock, TimeZone = TimeZoneInfo.Utc });
        session.Data.Define("daily", DataZone.Meta, new DailyProfile());
        session.Do(new CheckReset());                 // 3시 59분: 어제 날짜로 센다
        var before = session.Data.Get<DailyProfile>("daily").lastDay;
        clock = clock.AddMinutes(2);                  // 4시 1분
        session.Do(new CheckReset());
        return session.Data.Get<DailyProfile>("daily").lastDay == before + 1;   // 새 날
    }
}
```

`RealClock`이 늘 정한 시각을 돌려주므로 날짜·시간대에 걸린 규칙을 기다리지 않고 시험한다.

### 4.10 시드 입력 · 일일 도전

```csharp
using GBible;

public static class RunStarter
{
    // 타이틀의 "시드로 시작" 버튼(행동 밖). 칸이 비면 시드 글자를 새로 만든다 — 판마다 나눌 수 있는 시드가 생긴다.
    public static void Start(string seedText)
    {
        var session = GBibleRuntime.Session;
        var text = string.IsNullOrWhiteSpace(seedText) ? NewSeedText() : seedText.Trim().ToUpperInvariant();
        GBibleRuntime.NewRun(session, RandomStreams.SeedOf(text));
        session.Do(new StartRun { seedText = text });          // 새 판 행동(게임이 만든 것): 덱·맵을 만들고 시드 글자를 판 데이터에 적는다
    }

    // "오늘의 도전": 날짜 번호가 같은 사람은 모두 같은 판
    public static void StartDaily()
    {
        var session = GBibleRuntime.Session;
        var text = "DAILY-" + session.Game.Day();              // 행동 밖이라 지금 시각. 모두 같은 순간에 바뀌게 하려면 TimeZone을 정한다(2.13)
        GBibleRuntime.NewRun(session, RandomStreams.SeedOf(text));
        session.Do(new StartRun { seedText = text, daily = true });
    }

    // 판을 고르는 일이라 규칙 난수가 아니다: Unity 난수로 글자만 만든다(그 뒤 판의 모든 무작위는 이 시드의 Game.Random).
    static string NewSeedText()
    {
        const string letters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var chars = new char[8];
        for (int i = 0; i < chars.Length; i++) chars[i] = letters[UnityEngine.Random.Range(0, letters.Length)];
        return new string(chars);
    }
}
```

- 시드 글자는 판 데이터(`StartRun`이 적는 `run.seedText`)에 두고 화면에 보인다. 다른 사람이 그 글자를 넣으면 같은 판이 나온다.
- 일일 도전의 점수·완료는 프로필(메타 구역)에 날짜 번호와 함께 남긴다. 판 데이터는 새 판마다 지워진다.
- `NewRun`은 행동 안에서 부를 수 없다. 버튼·화면 코드에서 부르고, 판 내용은 그 뒤 행동으로 만든다(06장 2.3).

### 4.11 일일 임무: 날마다 N개 뽑기 · 진행 · 받기

새 기능 없이 있는 부품으로 조립한다: 날짜 번호(`Game.Day`, 2.12) + 가중치 표로 N개 뽑기(`Game.Roll`, 30장) + 프로필의 오늘 진행 + 받기 행동. 임무 하나하나(무엇을 몇 번 · 보상)는 원본(DB › 인스턴스, 04장)이라 개발자가 DB에서 고친다.

**에디터**: 원본 클래스 `DailyQuest`의 인스턴스들 · DB › 가중치 표 `dailyQuests`(줄 = 임무 인스턴스, 무게) · 밸런싱 값 `dailyCount` `3`(하루 임무 수) · `dailyResetHour` `4`(바뀌는 시각(시)).

```csharp
// Assets/MomoiProject/Code/Rules/Daily.cs
using System;
using System.Collections.Generic;
using GBible;
using GBible.Ids;

[KeywordGroup("임무 목표")]
public static class QuestGoal { public const string Kill = "kill"; public const string Craft = "craft"; }

[Serializable]
public class DailyQuest                                        // 원본(DB › 인스턴스)
{
    [Keyword(typeof(QuestGoal))] public string goal = QuestGoal.Kill;
    public int count = 10;
    public int reward = 50;
}

[Serializable]
public class DailyEntry                                        // 오늘 뽑힌 임무 하나(프로필)
{
    public string id;
    [DataRef(typeof(DailyQuest))] public string quest;
    public int done;
    public bool claimed;
}

// Profile(메타 구역)에: public long dailyDay = -1; public List<DailyEntry> daily = new List<DailyEntry>(); public int gems;

public static class DailyRules
{
    // 날이 바뀌었으면 오늘 임무를 새로 뽑는다. 행동 안에서(켤 때 · 진행 · 받기 앞).
    public static void Refresh(GameApi game)
    {
        var p = game.Data().Profile;
        var today = game.Day((int)game.Balance(GameIds.Balance.DailyResetHour));
        if (p.dailyDay == today) return;
        p.dailyDay = today;
        p.daily.Clear();
        foreach (var quest in game.Roll(GameIds.WeightTable.DailyQuests, (int)game.Balance(GameIds.Balance.DailyCount), unique: true))
            p.daily.Add(new DailyEntry { id = game.NewId("daily"), quest = quest });
    }

    public static void Progress(GameApi game, string goal, int amount = 1)
    {
        Refresh(game);
        foreach (var entry in game.Data().Profile.daily)
            if (((DailyQuest)game.Session.Instances(entry.quest)).goal == goal) entry.done += amount;
    }

    // Bootstrap.RegisterRules에서: 진행을 세는 이벤트를 받는다(그 이벤트를 쏜 행동 안이라 같이 Undo·저장된다)
    public static void Register(GameSession session) =>
        session.Events.Subscribe<EnemyKilled>(e => Progress(session.Game, QuestGoal.Kill));   // EnemyKilled: 프로젝트 이벤트
}

[Serializable, BlockInfo("오늘 임무 확인")]
public class RefreshDaily : GameAction { protected override void Run() => DailyRules.Refresh(Game); }

[Serializable, BlockInfo("일일 임무 받기")]
public class ClaimDaily : GameAction
{
    public DailyEntry entry;                                   // 참조 필드(02장 2.2)
    protected override void Run()
    {
        var quest = (DailyQuest)Game.Session.Instances(entry.quest);
        if (entry.claimed || entry.done < quest.count) throw Game.Fail("fail.dailyNotDone", "아직 끝나지 않은 임무입니다.");
        entry.claimed = true;
        Game.Data().Profile.gems += quest.reward;
    }
}
```

- 켤 때(`Started` — 프로필은 이때 이미 불러와 있다)와 임무 화면을 열 때 `RefreshDaily`를 보낸다. 켜 둔 채 초기화 시각을 넘기면 다음 진행·확인에서 새로 뽑힌다. 날이 바뀌어 지난 임무가 사라진 뒤 그 칸의 받기를 누르면 "대상이 이미 없습니다"(02장 2.2)다.
- 뽑기는 가중치 표라 "해금한 뒤에만 나오는 임무"는 표 줄의 조건으로, 임무 수를 늘리는 기믹은 밸런싱 값으로 둔다. 같은 날에는 다시 뽑지 않으므로 뽑은 목록이 프로필에 남는다(진행·받음과 함께 저장).
- 시험: 개발 명령 **시간 › 현실 시각 +1일**(4.7)로 다음 날 임무가 새로 뽑히는지 본다. 규칙 테스트는 4.9처럼 `RealClock`을 옮긴다.

---

## 5. 주의할 점 · 자주 하는 실수

| 증상 | 원인과 해결 |
|---|---|
| 리플레이·멀티에서 결과가 어긋남 | 규칙 계산에 `UnityEngine.Random`·`System.Random`을 썼다. 버전·플랫폼마다 결과가 다를 수 있다. 규칙은 `Game.Random`만 쓴다. |
| Undo 후 연출 난수가 반복됨 | 연출에 `Game.Random`을 썼다. Undo가 모든 스트림을 되돌린다. 연출은 `UnityEngine.Random`. |
| 전투에서 한 번 더 뽑았더니 드롭이 바뀜 | 같은 스트림을 함께 썼다. 용도별로 이름을 나눈다. |
| 플레이마다 결과가 다름 | `GBibleRuntime`의 기본 시드는 현재 시각이다. 재현이 필요하면 DB › Bootstrap의 **개발 빌드에서 시드 고정**을 켠다(2.3). 판마다 정한 시드면 `GBibleRuntime.NewRun(session, seed)`(2.16). |
| 같은 시드로 새 판을 열었는데 판이 다름 | `NewRun`에 시드를 주지 않았다(난수가 이어서 흐른다). 또는 판을 여는 행동이 난수를 뽑는 차례가 다르다(조건에 따라 한 번 더 뽑기). 새 판 행동은 같은 차례로 뽑는다(2.16). |
| 시드 글자의 대소문자·빈칸만 달라도 다른 판일까 걱정 | `RandomStreams.SeedOf`가 앞뒤 빈칸을 떼고 대문자로 맞춘다. 같은 판이다. |
| "모든 확률 2배" 유물이 먹히지 않음 | 확률을 `Game.Random(…).Chance(p)`로 굴렸다. 행동 직전 이벤트를 거치지 않는다. 기획의 확률은 `Game.Chance`(2.15) 또는 DB 조건 '확률'. |
| 쿨다운이 되감기·이어하기 뒤 어긋남 | `Time.time`으로 적었다. `session.Clock.Time` 기준으로 플레이어 데이터에 적는다. |
| 일시정지했는데 쿨다운이 줄어듦 | `Time.unscaledTime`·`Time.realtimeSinceStartup` 같은 실제 시간을 썼다. |
| 대화창을 닫았는데 게임이 안 움직임 | 다른 이유(메뉴 등)의 일시정지가 남아 있다. `GameTime.PausedBy`로 확인한다. 모니터의 일시정지 버튼 툴팁에도 멈춘 이유가 보인다. |
| `StepFrame`이 아무 일도 안 함 | 일시정지 상태가 아니다. |
| `Range(5, 5)`에서 예외 | 최댓값은 포함하지 않는다. `max > min`이어야 한다. |
| 다시 돌리기·멀티에서 시각에 걸린 결과가 어긋남 | 규칙에서 `DateTime.Now`·`DateTime.UtcNow`를 썼다. 부를 때마다 값이 달라 행동에 남지 않는다. 규칙은 `Game.Now`를 쓴다. |
| 오프라인 보상이 늘 0 | 게임 시계(`session.Clock`)로 쟀다. 게임 시계는 켜져 있을 때만 흐른다. 꺼 둔 시간은 `Game.Now`로 잰다(2.14). |
| 일시정지 중에도 쿨다운이 줄어듦 | 게임 안의 쿨다운을 `Game.Now`로 쟀다. 게임 안의 시간은 `session.Clock`, 현실 시간 규칙(스태미나·일일)만 `Game.Now`. |
| 기기 시계를 앞으로 돌려 보상을 더 받음 | 기기 시계는 플레이어가 바꿀 수 있다. 서버·Steam 시각이 있으면 `RealClock`으로 넘긴다. 없으면 상한을 두고 뒤로 간 시간은 0으로 센다(4.7). |
| 일일 초기화 시각이 플레이어마다 다름 | 싱글에서 `TimeZone`을 비워 각자 지역 시각으로 센다(멀티는 비우면 모두 UTC). 모두 같은 순간이어야 하면 `TimeZone`을 정한다(2.13). |
| `Game.Now`가 옛 시각 | 같은 행동 객체를 두 번 `Do`했다(실패한 것을 다시 `Do`한 것 포함). `utc`는 이미 값이 있으면 그대로 쓴다. `Do`할 때마다 `new`로 만든다(미리보기가 찍은 시각은 지워지므로 괜찮다). |
| 행동 안에서 `Game.Now`가 흐르지 않음 | 의도된 동작이다. 행동 하나는 한 순간이다. |
| 처음 켰더니 오프라인 보상이 엄청남 | `DateTime` 필드의 처음 값(`default`, 0001년)부터 쟀다. 처음이면 시각만 적는다(4.7). |
| 이어 하기로 켰는데 오프라인 보상이 한 박자 늦게·엉뚱하게 들어옴 | 켤 때 정산을 `GBibleRuntime.Started`에 걸었다. 판은 `Started` 뒤에 불러온다 — `RunLoaded`와 새 판 행동에서 한다(4.7). |
