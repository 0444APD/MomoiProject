# 07. 난수와 시간

**한 줄 요약**: 게임 로직의 난수는 `Game.Random("이름")`(결정론 난수 스트림), 게임 로직의 시간은 `session.Clock`(게임 시계)을 쓴다. 둘 다 Undo·스냅샷 되감기·판 저장에 함께 따라간다. 일시정지·배속은 `GameTime`이 `Time.timeScale`로 한꺼번에 조절한다.

**언제 쓰는가**

- 명중·치명타·드롭·맵 생성 같은 규칙의 무작위
- 쿨다운·지속 시간처럼 게임 시간 기준의 값
- 메뉴·대화창이 열렸을 때 게임 멈추기, 슬로우 모션, 개발 중 프레임 단위 진행

---

## 0. 용어

| 용어 | 뜻 |
|---|---|
| 결정론 난수 | 같은 시드에서 같은 순서로 부르면 기기·플랫폼과 상관없이 같은 수가 나오는 난수. 정수 연산만 쓴다(xoshiro256**). |
| 마스터 시드 (`SessionOptions.Seed`) | 세션 전체의 씨앗. 스트림마다의 시드가 여기서 나온다. |
| 스트림 (`RandomStream`) | 이름 하나("damage", "loot" …)에 붙은 난수 흐름. 스트림끼리 독립이라 한 곳에서 더 뽑아도 다른 곳의 결과가 바뀌지 않는다. |
| 게임 시계 (`GameClock`) | 세션의 시간. `Tick`(프레임 수)과 `Time`(초). 프레임 끝마다 흐른다. |
| `GameTime` | Unity 쪽 시간 조절(일시정지·배속·프레임 넘기기). `Time.timeScale`을 바꾼다. |
| 행동 · Undo · 스냅샷 | [02-Actions-Undo.md](02-Actions-Undo.md) 참고. |

---

## 1. 에디터에서 쓰는 법 (모니터)

플레이 중 **MomoiProject › 모니터**:

- 위쪽 줄에 "시드 … · 틱 … · …초"가 보인다(마스터 시드, 게임 시계).
- 위쪽 시간 조절: **일시정지**/**재개** 버튼(멈춘 이유가 툴팁에 보인다), **1프레임**(일시정지 중 한 프레임만 진행), 배속 선택 **0.25× · 0.5× · 1× · 2× · 4×**.
- **명령** 탭의 **시간** 묶음: **일시정지 / 재개**, **1프레임 진행**(일시정지 중에만 동작), **배속 0.25**, **배속 1**, **배속 2**.
- 모니터의 일시정지는 "개발 도구"라는 이유로 멈춘다. 게임 코드가 건 일시정지와 겹치며, 각자 풀어야 재개된다.

---

## 2. 코드로 쓰는 법 — 문법 요소

### 2.1 스트림 얻기: `Game.Random(name)`

```csharp
RandomStream damage = Game.Random("damage");      // session.Random.Get("damage")와 같다
```

처음 부를 때 그 이름의 스트림을 만들고, 그 뒤로는 같은 스트림을 준다. 스트림 시드는 마스터 시드와 이름으로 정해진다. 이름은 용도별로 나눈다("damage", "loot", "map"). 그러면 전투에서 난수를 한 번 더 뽑아도 드롭 결과는 그대로다.

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

`GBibleRuntime`은 기본으로 현재 시각을 시드로 쓴다(플레이마다 다름). 버그 재현·리플레이·일일 도전처럼 같은 결과가 필요하면 `Configuring`에서 고정한다. 세션을 직접 만들면 `new GameSession(new SessionOptions { Seed = 7 })`.

### 2.4 스트림 묶음: `session.Random` (`RandomStreams`)

| 멤버 | 뜻 |
|---|---|
| `Get(name)` | 스트림(없으면 만든다) |
| `MasterSeed` | 마스터 시드 |
| `Reseed(seed)` | 마스터 시드를 바꾸고 모든 스트림을 처음 상태로 |
| `Streams` | 만들어진 스트림들 |
| `ExportStates()` · `ImportStates(states)` · `Restore(states)` | 상태 꺼내기·넣기·되돌리기(시스템이 Undo·스냅샷·판 저장에 쓴다) |

### 2.5 게임 시계: `session.Clock` (`GameClock`)

```csharp
long tick = Game.Session.Clock.Tick;     // 프레임 끝을 지난 횟수(고정 틱이면 틱 수)
double now = Game.Session.Clock.Time;    // 게임 시간(초)
```

- `GBibleRuntime`이 매 프레임 끝(`LateUpdate`)에 `session.EndFrame(Time.deltaTime)`을 부르고, 그때 `Tick`이 1 오르고 `Time`에 `deltaTime`이 더해진다. `Time.deltaTime`은 `Time.timeScale`을 따르므로 배속·일시정지가 시계에 그대로 반영된다.
- 고정 틱(DB › Bootstrap의 초당 틱 수, `33-Tick-Preview-Csv.md`)을 켜면 프레임마다가 아니라 틱마다 `Tick`이 1 오르고 `Time`에 1/틱 초가 더해진다(한 프레임에 여러 틱일 수 있다).
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

---

## 3. Undo·스냅샷·저장과 함께 움직이는 방식

| 사건 | 난수 | 게임 시계 |
|---|---|---|
| 행동 실행 | 행동 직전에 **모든 스트림 상태**를 보관한다 | 행동에 `tick`을 적는다 |
| 행동 실패(예외) | 보관한 상태로 되돌린다 | — |
| Undo (행동 단위 방식) | 그 행동 직전 상태로 되돌린다(그 뒤에 처음 만든 스트림은 처음 상태로) → 같은 행동을 다시 하면 같은 결과 | 되돌리지 않는다(시간은 계속 흐른다) |
| Undo (스냅샷 방식) | 스냅샷 때 상태로. 그 뒤에 처음 만든 스트림은 처음 상태로 | 스냅샷 때의 `Tick`·`Time`으로 되감는다 |
| 판 저장(`ExportRun`) | 마스터 시드와 모든 스트림 상태를 저장 | `Tick`·`Time`을 저장 |
| 판 불러오기(`ImportRun`) | 마스터 시드·스트림 상태를 되살린다 → 다음 결과까지 같다 | 저장한 시각으로 |
| 프로필(`ExportMeta`) | 저장하지 않는다 | 저장하지 않는다 |

**주의 — Undo는 "모든 스트림"을 행동 직전으로 되돌린다.** 그 행동 뒤에 행동 밖에서 같은 세션의 난수를 뽑았다면(씬 코드의 연출 등) 그 소비도 함께 되돌아간다. 연출용 무작위(파티클 흔들림, 대사 고르기 등)는 `UnityEngine.Random`을 쓰고, `Game.Random`은 규칙 계산에만 쓴다.

---

## 4. 상황별 예제

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
        var rng = Game.Random("combat");
        if (rng.Range(0, 100) >= target.accuracy) return;            // 빗나감(난수 소비도 Undo 단위)
        var damage = baseDamage + rng.Range(0, 4);
        if (rng.Chance(0.15)) damage *= 2;                            // 치명타
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

"loot" 스트림만 쓰므로, 전투에서 난수를 몇 번 뽑든 상자 결과의 순서는 바뀌지 않는다.

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

일시정지 중에는 시계가 멈춰 버프 시간도 줄지 않는다. 스냅샷을 되감으면 시계와 `endsAt`이 함께 돌아간다. 만료 처리도 행동이라 되감기·기록에 함께 들어간다.

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

---

## 5. 주의할 점 · 자주 하는 실수

| 증상 | 원인과 해결 |
|---|---|
| 리플레이·멀티에서 결과가 어긋남 | 규칙 계산에 `UnityEngine.Random`·`System.Random`을 썼다. 버전·플랫폼마다 결과가 다를 수 있다. 규칙은 `Game.Random`만 쓴다. |
| Undo 후 연출 난수가 반복됨 | 연출에 `Game.Random`을 썼다. Undo가 모든 스트림을 되돌린다. 연출은 `UnityEngine.Random`. |
| 전투에서 한 번 더 뽑았더니 드롭이 바뀜 | 같은 스트림을 함께 썼다. 용도별로 이름을 나눈다. |
| 플레이마다 결과가 다름 | `GBibleRuntime`의 기본 시드는 현재 시각이다. 재현이 필요하면 `Configuring`에서 `Seed`를 고정한다. |
| 쿨다운이 되감기·이어하기 뒤 어긋남 | `Time.time`으로 적었다. `session.Clock.Time` 기준으로 플레이어 데이터에 적는다. |
| 일시정지했는데 쿨다운이 줄어듦 | `Time.unscaledTime`·`Time.realtimeSinceStartup` 같은 실제 시간을 썼다. |
| 대화창을 닫았는데 게임이 안 움직임 | 다른 이유(메뉴 등)의 일시정지가 남아 있다. `GameTime.PausedBy`로 확인한다. 모니터의 일시정지 버튼 툴팁에도 멈춘 이유가 보인다. |
| `StepFrame`이 아무 일도 안 함 | 일시정지 상태가 아니다. |
| `Range(5, 5)`에서 예외 | 최댓값은 포함하지 않는다. `max > min`이어야 한다. |
