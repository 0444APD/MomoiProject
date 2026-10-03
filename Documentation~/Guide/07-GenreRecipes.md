# 07. 장르별 설정

## 1. 이 단계의 목표

게임 장르에 맞게 세 가지를 정한다. **Undo를 어떤 단위로 하는가**(행동 하나 / 스냅샷 한 칸 / 하지 않음), **어떤 값을 어느 저장 구역에 두는가**(판 / 프로필), **난수를 어떤 흐름으로 나누는가**. 셋 다 `GBibleRuntime.Configuring`과 데이터 설계에서 정하고, 게임 코드는 장르와 상관없이 `Game.Do`·`session.Undo()`·`Game.Random(이름)`으로 같게 쓴다.

이 장에서 쓰는 말:

| 말 | 뜻 |
|---|---|
| Undo | 되돌리기. 어느 방식이든 게임 코드는 `session.Undo()` 하나로 부른다. |
| 되돌리기 방식 (`HistoryMode`) | `Actions`(행동 하나씩), `Snapshots`(일정 간격으로 찍은 전체 모습으로 되감기), `None`(되돌리지 않음, 기록 비용 없음). |
| 스냅샷 | 일정 간격(`SnapshotInterval`, 게임 시간 초)으로 찍어 두는 플레이어 데이터 전체·난수·게임 시계의 모습. 메모리에만 둔다. |
| 저장 구역 (`DataZone`) | `Session`(판: 판이 끝나면 버린다, `run.json`)과 `Meta`(프로필: 판이 바뀌어도 남는다, `profile.json`). |
| 난수 흐름 | `Game.Random("이름")`이 주는 이름별 결정론 난수. 흐름끼리 독립이라 한 곳에서 더 뽑아도 다른 곳의 결과가 바뀌지 않는다. |
| 게임 시계 | `session.Clock`. 프레임 끝마다 흐르는 게임 시간(`Time`)과 프레임 수(`Tick`). 스냅샷을 되감으면 함께 돌아간다. |

---

## 2. 할 일 순서

1. **Undo 방식을 고른다.** 턴제·카드·퍼즐은 `Actions`, 실시간은 `Snapshots`, 되돌리기가 없는 게임은 `None`. `Configuring`에서 `options.History`로 정한다.
2. **Undo 범위를 정한다.** 행동 방식이면 `HistoryLimit`(오래 플레이해도 기록이 끝없이 늘지 않게)과 "턴이 넘어가면 되돌릴 수 없다" 같은 규칙(`ClearHistory`)을, 스냅샷 방식이면 간격과 보관 수를 정한다.
3. **저장 구역을 나눈다.** 판이 끝나면 버릴 값은 `Session`, 판을 넘어 남길 값(해금·누적 재화·최고 기록)은 `Meta`. 플레이어 데이터를 구역별로 한 클래스씩 두면 관리가 쉽다.
4. **난수 흐름을 나눈다.** 덱 섞기·전리품·대상 고르기·맵 생성처럼 용도마다 이름을 따로 쓴다. 한 용도에서 뽑는 횟수가 바뀌어도 다른 용도의 결과가 그대로이게 하기 위해서다.
5. **시드를 정한다.** 개발 빌드는 고정 시드(버그 재현), 출시 빌드는 기본(현재 시각). 일일 도전처럼 모두 같은 판을 하려면 날짜로 시드를 정한다.
6. **시간 기준 값을 게임 시계로 적는다.** 쿨다운·지속 시간은 `Time.time`이 아니라 `session.Clock.Time` 기준 시각으로 플레이어 데이터에 적는다.

---

## 3. 판단 기준

| 장르 | Undo 방식 | 주로 쓰는 구역 | 난수 흐름 예 | 이어 하기 |
|---|---|---|---|---|
| 덱빌딩 로그라이크 | `Actions`(턴 안에서만 되돌리기도 흔함) | 판 = `Session`, 해금·기록 = `Meta` | `deck`·`loot`·`cardTarget`·`map.층` | 판 저장(`run.json`) |
| 턴제 전술 | `Actions` | 전장 = `Session`, 캠페인 진행 = `Meta` | `damage`·`ai`·`spawn` | 판 저장 |
| 실시간 액션 | `Snapshots` | 이번 판 = `Session`(위치·속도는 넣지 않는다) | `loot`·`spawn` | 체크포인트에서 판 저장 |
| 수집형(메타 성장) | `None` 또는 `Actions` | 대부분 `Meta` | `gacha`·`loot` | 프로필 저장이 곧 진행 |
| 방치형 | `None` | `Meta` | `loot` | 프로필 저장 |

| 이럴 때 | 이것 |
|---|---|
| 되돌린 뒤 같은 행동을 하면 같은 결과가 나와야 한다(되돌리기로 좋은 결과를 뽑는 것을 막는다) | 규칙의 무작위는 모두 `Game.Random`. Undo가 난수도 되돌린다 |
| 한 번 일어나면 되돌리지 않을 값(본 적 있는 보스, 해금) | 도전과제의 결과로 주거나, 행동 안에서 `using (Game.Session.Data.Permanent())` |
| 턴을 넘긴 뒤에는 그 전으로 못 돌아가게 | 턴 넘기기 행동 뒤에 `Game.Session.Data.ClearHistory()` |
| 실시간에서 중요한 순간을 꼭 되감을 수 있게 | `session.CaptureSnapshot()`(행동 밖에서) |
| 매 프레임 바뀌는 값 | 플레이어 데이터에 넣지 않는다(매 프레임 기록이 쌓인다). MonoBehaviour에 둔다 |

---

## 4. 카드 던전에서 실제로 (덱빌딩)

### Undo 방식과 시드

카드 던전은 행동 하나씩 되돌린다. 카드 한 장을 잘못 냈으면 그 카드만 되돌리고 다시 낸다. Undo 기록은 200개까지만 두고, 개발 빌드는 시드를 고정해 같은 판이 다시 나오게 한다.

```csharp
static void Configure(SessionOptions options)
{
    options.History = HistoryMode.Actions;   // 턴제: 행동 하나씩 Undo
    options.HistoryLimit = 200;              // 오래 플레이해도 Undo 기록이 끝없이 늘지 않게
    if (Debug.isDebugBuild) options.Seed = 20261003;   // 개발 빌드는 시드 고정(버그 재현)
}
```

`Configuring`은 세션을 만들기 직전에 불리므로 첫 씬보다 먼저 받아야 한다(`RuntimeInitializeLoadType.AfterAssembliesLoaded`에서 `GBibleRuntime.Configuring += Configure`). 출시 빌드는 시드를 정하지 않아 판마다 다르다. 일일 도전 모드를 만든다면 날짜에서 만든 수를 `options.Seed`에 넣는다.

되돌릴 수 있는 범위를 턴 안으로 줄이고 싶으면, 런 스크립트의 턴 끝 버튼(`RunDirector.EndTurn`)이 `Game.Do(new EndTurn())` 다음 줄에서 이력을 비운다.

```csharp
GBibleRuntime.Session.Data.ClearHistory();   // 이 시점 이전은 Undo할 수 없다
```

적의 공격은 `EndTurn` 행동 안에서 일어나므로, 이력을 비우지 않으면 플레이어가 맞은 뒤 턴 끝을 되돌려 공격을 피할 수 있다. 되돌리기를 얼마나 허락할지는 게임 디자인이 정하고, 코드는 이 한 줄로 정한다.

### 저장 구역

판의 모든 값은 플레이어 데이터 `run`(`RunState`, `Session` 구역)에, 판을 넘어 남는 값은 `profile`(`Profile`, `Meta` 구역)에 둔다. 판이 끝날 때 남길 것만 `EndRun` 행동이 프로필에 옮긴다.

```csharp
var run = Game.Data().Run;
var profile = Game.Data().Profile;
profile.bestFloor = Math.Max(profile.bestFloor, run.floor);
profile.totalGold += run.gold;
```

새 판을 열면 `session.NewRun()`이 `Session` 구역만 처음 값으로 돌리고 프로필은 그대로 둔다. 판 저장(`run.json`)에는 `Session` 구역과 난수 상태·게임 시계가 들어가므로, 이어 하면 다음 카드 섞기 결과까지 저장한 순간과 같다. 첫 승리로 해금하는 카드는 도전과제 `firstWin`의 결과(`[BindTo]` 함수)가 `profile.unlocked`에 넣는다. 도전과제의 달성과 결과는 Undo해도 남으므로, 승리한 행동을 되돌렸다 다시 이겨도 두 번 들어가지 않는다.

### 난수 흐름

카드 던전은 난수를 세 흐름으로 나눈다.

| 흐름 | 쓰는 곳 |
|---|---|
| `deck` | 덱 섞기(`Rules.Shuffle`) |
| `loot` | 전투 승리 골드(`WinBattle`) |
| `cardTarget` | 무작위 적을 고르는 대상 Block(`RandomEnemy`) |

```csharp
var random = game.Random("deck");
for (var i = pile.Count - 1; i > 0; i--)
{
    var j = random.Range(0, i + 1);
    (pile[i], pile[j]) = (pile[j], pile[i]);
}
```

흐름을 나눠 두면, 기획자가 무작위 대상 카드를 하나 더 넣어 `cardTarget`을 더 뽑게 되어도 덱 섞기와 전리품 결과는 같은 시드에서 그대로다. 맵을 만든다면 `map.1`·`map.2`처럼 층마다 흐름을 나눠, 앞 층에서 더 뽑아도 뒤 층의 맵이 바뀌지 않게 한다. 피해 숫자의 흔들림 같은 연출에는 `Game.Random`을 쓰지 않는다(06장).

---

## 5. 다른 장르라면

> **다른 장르라면 — 턴제 전술** 카드 던전과 같이 `HistoryMode.Actions`를 쓴다. 이동·공격·턴 넘기기가 각각 행동이고, 대상은 유닛 id(`GameAction<Unit>`의 `target`)로 보낸다. 명중·피해는 `damage`, 적의 판단은 `ai` 흐름으로 나눠, 적 AI가 생각을 더 하게 바꿔도 플레이어 공격의 결과는 바뀌지 않게 한다. 소환된 유닛은 `Game.NewId("unit")`으로 id를 받으므로 Undo·저장·멀티플레이에서도 같은 id다. 아군 턴 전체를 되돌리게 할지, 유닛 하나의 행동만 되돌리게 할지는 `ClearHistory`를 부르는 자리로 정한다.

> **다른 장르라면 — 실시간 액션** 행동 하나씩 되돌리면 1초에도 행동이 수십 개라 의미가 없으므로 스냅샷 방식을 쓴다. 턴제의 `Configure` 대신 이렇게 정한다.
>
> ```csharp
> public static void Configure(SessionOptions options)
> {
>     options.History = HistoryMode.Snapshots;   // Undo = 스냅샷 한 칸 되감기
>     options.SnapshotInterval = 0.5;            // 게임 시간 0.5초마다
>     options.SnapshotLimit = 20;                // 최근 10초까지
> }
> ```
>
> `Undo()` 한 번이 스냅샷 한 칸 되감기이고, 플레이어 데이터·난수·게임 시계가 함께 돌아간다. 그래서 쿨다운은 게임 시계 기준 시각으로 적는다.
>
> ```csharp
> var now = Game.Session.Clock.Time;
> if (now < hero.dashReadyAt) throw new InvalidOperationException("쿨다운 중");
> hero.dashReadyAt = now + Cooldown;   // 되감으면 시계와 이 값이 함께 돌아간다
> ```
>
> 보스 방 입구처럼 꼭 돌아올 수 있어야 하는 순간은 간격을 기다리지 않고 `GBibleRuntime.Session.CaptureSnapshot()`으로 직접 찍는다(행동 실행 중에는 부를 수 없다). 위치·속도·애니메이션은 MonoBehaviour에 두고, 플레이어 데이터에는 체력·쿨다운·획득물처럼 규칙이 다루는 값만 둔다. 스냅샷 방식에서는 행동별 Undo 이력을 남기지 않는다.

> **다른 장르라면 — 수집형(메타 성장)** 캐릭터·장비·재화가 판을 넘어 쌓이므로 대부분 `Meta` 구역이다. 되돌리기가 필요 없으면 `HistoryMode.None`으로 기록 비용을 없앤다. 뽑기는 `gacha` 흐름으로 따로 두고, 경쟁·결제가 걸리면 결과를 서버에서 정한다(11장). 해금·첫 획득 보상은 도전과제의 결과로 주면 되돌리기·중복 지급 걱정이 없다.

> **다른 장르라면 — 방치형** 진행이 거의 모두 프로필(`Meta`)에 있고 되돌리기가 없으므로 `HistoryMode.None`을 쓴다. 마지막으로 정산한 시각을 플레이어 데이터에 `DateTime` 필드로 두고(지원하는 형식이다), 접속할 때 "보상 받기" 행동이 지난 시간만큼 보상을 더하고 시각을 갱신한다. 정산을 행동으로 하면 기록·모니터에 원인과 함께 남는다. 게임 시계(`session.Clock`)는 게임이 켜져 있는 동안만 흐르므로, 꺼져 있던 시간은 시각 필드로 계산한다.

---

## 6. 체크리스트

- [ ] `Configuring`에서 `History`(와 `HistoryLimit` 또는 `SnapshotInterval`·`SnapshotLimit`)를 정했다.
- [ ] 되돌릴 수 있는 범위(턴 안 / 무제한 / 없음)를 정하고, 필요하면 `ClearHistory`를 부르는 자리를 정했다.
- [ ] 판이 끝나면 버릴 값과 남길 값이 서로 다른 구역의 플레이어 데이터에 있다.
- [ ] 규칙의 무작위는 모두 `Game.Random(이름)`이고, 용도마다 흐름 이름이 다르다.
- [ ] 개발 빌드는 시드를 고정한다.
- [ ] 쿨다운·지속 시간은 게임 시계 기준 시각으로 플레이어 데이터에 적는다.
- [ ] 매 프레임 바뀌는 값을 플레이어 데이터에 넣지 않았다.

---

## 7. 더 보기

- [02. 행동과 Undo](../Manual/02-Actions-Undo.md) — `HistoryMode`·`HistoryLimit`·스냅샷·`Permanent()`·턴 안에서만 되돌리기
- [07. 난수와 시간](../Manual/07-Random-Time.md) — 난수 흐름·시드·게임 시계·`GameTime`
- [06. 저장·불러오기](../Manual/06-SaveLoad.md) — 구역별 저장, `NewRun`
- [01. 플레이어 데이터](../Manual/01-PlayerData.md) — 구역, 넣지 않는 값
- [14. 도전과제](../Manual/14-Achievements.md) — 되돌리지 않는 달성·보상
