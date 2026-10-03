# 02. 행동과 Undo

**한 줄 요약**: 플레이어 입력 하나(이동·공격·구매·카드 내기·턴 넘기기)를 **행동** 클래스로 만들어 `Game.Do`로 실행한다. 행동 하나가 Undo 한 단위이자, 멀티플레이에서 한 번에 보내는 변경 한 묶음이다.

**언제 쓰는가**

- 플레이어가 버튼·클릭으로 일으키는 모든 규칙 변화
- 되돌리기(Undo)가 필요한 턴제·퍼즐·카드 게임
- 실시간 게임의 입력(되돌리기는 스냅샷 방식으로)
- 실패하면 아무것도 바뀌지 않아야 하는 처리(구매·제작)

---

## 0. 용어

| 용어 | 뜻 |
|---|---|
| 행동 (`GameAction`) | 플레이어 입력 하나를 담은 **데이터** 클래스. 메서드 호출이 아니라 데이터라서 기록·리플레이·서버 전송이 된다. |
| `Game` (`GameApi`) | 게임 코드의 창구. 행동 클래스 안에서는 `Game` 속성으로 쓴다. 자세히는 [03-GameApi.md](03-GameApi.md). |
| Transaction | 행동 하나가 일으킨 변경 전체. Undo는 Transaction 단위로 되돌린다(코드 형식 `Transaction`). |
| Sync · ChangeSet | 시스템이 플레이어 데이터의 사본(Mirror)과 실제 값을 비교해 바뀐 곳을 찾는 동작이 Sync, 찾아낸 변경 묶음이 ChangeSet이다. 자세히는 [01-PlayerData.md](01-PlayerData.md). |
| Undo | 마지막 행동(스냅샷 방식이면 마지막 스냅샷)으로 되돌리는 것. `session.Undo()`. |
| 행동 밖 변경 | 행동 실행 중이 아닐 때 바뀐 플레이어 데이터. 기록은 되지만 Undo되지 않는다. |
| 스냅샷 | 일정 간격으로 찍어 두는 플레이어 데이터 전체 모습. 실시간 게임의 되감기에 쓴다. |

---

## 1. 에디터에서 보기 (모니터)

행동은 코드로 만든다. 에디터에서는 실행 결과를 보고 되돌린다.

1. 플레이 중에 **MomoiProject › 모니터**를 연다.
2. 위쪽 줄에 "Undo할 수 있는 행동 N"(스냅샷 방식이면 "되감기 스냅샷 N (0.5초 간격)")이 보인다.
3. 오른쪽 위 **Undo** 버튼은 `session.Undo()`와 같다(되돌리기 방식이 `None`이면 꺼진다).
4. **행동** 탭에서 실행한 행동과 상태(실행 · Undo됨 · 실패), 공개 범위(공개 · 본인만 · 개발자)를 본다.
5. **타임라인** 탭의 "묶어 보기"는 행동(또는 Undo·되감기) 한 줄 아래에 그 행동으로 바뀐 데이터·이벤트를 접어 둔다. 행동 밖 변경은 행동 줄 아래가 아니라 따로 보인다.
6. **명령** 탭의 "세션" 묶음에도 **Undo (Undo)** 명령이 있다.

모니터 화면 전체는 [22-Monitor-Debug.md](22-Monitor-Debug.md).

---

## 2. 코드로 쓰는 법 — 문법 요소

### 2.1 대상이 있는 행동: `GameAction<T>`

```csharp
using System;
using GBible;

[Serializable]
public class Unit
{
    public string id;
    public int x, y;
    public int hp = 30;
}

[Serializable]
public class MoveUnit : GameAction<Unit>
{
    public int x, y;                                         // 행동이 실어 나르는 값

    protected override void Run(Unit u) { u.x = x; u.y = y; } // 찾은 대상을 그냥 고친다
}
```

| 요소 | 하는 일 |
|---|---|
| `GameAction<T>` | 대상 하나에 대한 행동. `T`는 클래스이고 `string id` 필드가 있어야 한다. |
| `target` (물려받은 `string` 필드) | 대상의 **id**. 객체가 아니라 id라서 데이터로 남는다. |
| `Run(T target)` | 시스템이 `Game.Find<T>(target)`로 찾은 객체를 넘겨준다. 못 찾으면 "찾을 수 없습니다" 예외로 행동이 실패한다. |
| `Game` (물려받은 속성) | 지금 세션의 `GameApi`. **`Run`이 실행되는 동안에만** 들어 있고, 끝나면 `null`이다. |
| `[Serializable]` | 행동은 데이터다. 붙여 두어야 저장·전송·리플레이에 쓸 수 있다. |

```csharp
Game.Do(new MoveUnit { target = "hero", x = 3, y = 4 });
```

등록 없이 바로 실행된다.

### 2.2 대상이 없는 행동: `SimpleAction`

```csharp
using System;
using GBible;

[Serializable]
public class Board
{
    public int turn;
}

[Serializable]
public class EndTurn : SimpleAction
{
    protected override void Run() => Game.All<Board>()[0].turn++;
}
```

`Run()`에 대상이 없다. 필요한 것은 `Game.All`·`Game.Find`로 찾는다. `Game` 속성 규칙은 `GameAction<T>`와 같다.

### 2.3 행동 로그 문장: `Describe()`

```csharp
using System;
using GBible;

[Serializable]
public class Heal : GameAction<Unit>
{
    public int amount = 10;
    protected override void Run(Unit u) => u.hp += amount;
    public override string Describe() => $"{target} 회복 +{amount}";
}
```

모니터의 행동 탭·타임라인에 보일 문장이다. 쓰지 않으면 `GameAction<T>`는 "`[BlockInfo]` 이름(없으면 클래스 이름) (대상 id)", `SimpleAction`은 "`[BlockInfo]` 이름(없으면 클래스 이름)"이다.

### 2.4 공통 멤버: `actorId` · `tick` · `Visibility`

```csharp
[Serializable]
public class OpenChest : SimpleAction
{
    public override ActionVisibility Visibility => ActionVisibility.Private;
    protected override void Run() { /* … */ }
}

Game.Do(new OpenChest { actorId = 2 });
```

| 멤버 | 뜻 |
|---|---|
| `actorId` (`int`) | 행동한 플레이어 번호. 게임 코드가 채운다. |
| `tick` (`long`) | 실행할 때 시스템이 게임 시계의 틱(`session.Clock.Tick`)을 적는다. |
| `Visibility` | `Public`(모두) · `Private`(본인만) · `Developer`(개발자 모니터만). 지금은 모니터 행동 탭에 표시된다. |

### 2.5 처리기를 따로 등록하는 행동: `GameAction` + `Actions.Register`

처리 코드를 행동 클래스 밖(세션 준비 코드)에 두는 방식이다. 행동 클래스는 데이터만 갖는다.

```csharp
using System;
using GBible;
using UnityEngine;

[Serializable]
public sealed class AttackGoblin : GameAction
{
    public string target = "goblin";
    public override string Describe() => $"플레이어 {actorId}: {target} 공격";   // 필수(abstract)
}

public static class CombatSetup
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void Hook() => GBibleRuntime.Started += session =>
    {
        session.Data.Define("battle.combo", DataZone.Session, 0, "콤보");
        session.Actions.Register<AttackGoblin>(action =>
        {
            var hit = session.Random.Get("combat").Chance(0.7);
            session.Data.Set("battle.combo", hit ? session.Data.GetNumber("battle.combo") + 1 : 0);
        });
    };
}
```

- 한 형식에 처리기는 하나다. 두 번 등록하면 "이미 등록되어 있습니다" 예외다.
- `GameAction`을 직접 상속하고 처리기도 없으면 `Do`가 "처리기가 없습니다" 예외를 낸다.
- 처리기가 등록되어 있으면 클래스 안의 `Run`보다 처리기가 먼저다.

### 2.6 실행: `Game.Do` / `session.Do`

```csharp
ActionRecord record = Game.Do(new MoveUnit { target = "hero", x = 1, y = 0 });
// record.Status: ActionStatus.Done / Undone / Failed
// record.Action, record.JournalId, record.TransactionId, record.Error
```

`Game.Do(action)`와 `session.Do(action)`는 같다. 행동은 실행되는 즉시 끝난다(기다리거나 확정하는 단계가 없다).

`Do` 안에서 시스템이 하는 일:

1. 처리 방법을 찾는다(등록한 처리기 → 클래스 안의 `Run`).
2. `tick`을 적고 기록(Journal)에 행동 한 줄을 남긴다.
3. **행동 시작**: 지금까지의 행동 밖 변경을 먼저 Sync해 따로 떼어 두고, Transaction을 연다. 모든 난수 스트림의 상태를 보관한다.
4. `Run`(또는 처리기)을 실행한다. 이 안에서 `Game.Find`·`Game.All`이 중간에 Sync해도 모두 같은 Transaction에 들어간다.
5. **행동 끝**: 모든 플레이어 데이터를 Sync해 Transaction을 닫는다. 행동 동안 모아 둔 ChangeSet을 차례대로 `Synced` 이벤트로 쏜다(한 행동 = 한 묶음). 되돌리기 방식이 행동 단위면 Undo 이력에 넣는다.
6. 바뀐 값을 보는 조건(도전과제·트리거)을 다시 평가한다.

값이 하나도 바뀌지 않은 행동(빗나간 공격 등)도 Undo 한 단위로 남는다. 난수 소비를 되돌려야 하기 때문이다.

### 2.7 실패: 예외가 나면 전부 취소

```csharp
[Serializable]
public class Buy : SimpleAction
{
    public int price;
    protected override void Run()
    {
        var wallet = Game.All<Wallet>()[0];
        wallet.gold -= price;                                                // 먼저 깎아도
        if (wallet.gold < 0) throw new InvalidOperationException("골드 부족"); // 예외면 깎은 것까지 되돌아간다
    }
}

[Serializable] public class Wallet { public int gold; }
```

행동 안에서 예외가 나면:

- 그 행동이 바꾼 플레이어 데이터를 모두 되돌린다(Transaction 단위 원자성).
- 난수 스트림도 행동 전 상태로 돌린다.
- 행동 기록의 상태는 `Failed`, 기록에 "행동 실패, 변경 취소" 오류가 남는다.
- Undo 이력에 들어가지 않는다.
- 예외는 `Do`를 부른 쪽으로 **다시 던져진다**. 예상되는 실패라면 부른 쪽에서 잡는다.

```csharp
try { Game.Do(new Buy { price = 50 }); }
catch (InvalidOperationException e) { /* "골드 부족" 안내 */ }
```

### 2.8 되돌리기: `session.Undo()`

```csharp
bool undone = Game.Session.Undo();   // 되돌릴 것이 없으면 false
int left = Game.Session.UndoCount;   // 되돌릴 수 있는 횟수
```

- 행동 단위 방식: 마지막 행동의 Transaction을 거꾸로 적용한다. 리스트에서 빠졌던 객체·갈아 끼워진 객체는 **원래 인스턴스**로 돌아온다(게임 코드가 쥔 참조가 이어진다). 몇 번이든 뒤에서부터 계속 되돌릴 수 있다.
- 되돌린 내용도 기록에 남는다. 클래스 데이터(Mirror로 비교하는 데이터)의 되돌린 내용은 다시 Sync되어 `Synced` 이벤트로도 나간다(받는 쪽도 같이 되돌아간다). 되돌린 행동의 상태는 `Undone`.
- **난수도 함께 되돌아간다.** Undo 후 같은 행동을 다시 하면 같은 결과가 나온다(되돌리기로 좋은 결과를 뽑는 것을 막는다).
- **도전과제 달성과 보상은 되돌리지 않는다.** 행동이 바꾼 값(주운 골드 등)만 돌아가므로, Undo하고 다시 해도 보상이 두 번 나오지 않고 값이 쌓이지 않는다.
- 행동 실행 중에는 Undo할 수 없다(예외).

### 2.9 되돌리지 않을 변경: `Permanent()`

```csharp
using (Game.Session.Data.Permanent())
    Game.Session.Data.Set("tutorial.firstBattleSeen", true);
```

이 블록 안의 변경은 행동 안에서 일어나도 Undo 이력에 들어가지 않는다. 블록에 들어갈 때 그때까지의 변경을 행동의 것으로 Sync하고, 나올 때 안의 변경을 이력 밖으로 Sync한다. 도전과제 달성·보상처럼 "한 번 일어나면 되돌리지 않을 일"에 쓴다. 스냅샷 방식의 되감기는 이 구분 없이 찍어 둔 시점의 값으로 돌아간다.

### 2.10 되돌리기 방식: `SessionOptions`

```csharp
using GBible;
using UnityEngine;

public static class UndoSetup
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void Hook() => GBibleRuntime.Configuring += options =>
    {
        options.History = HistoryMode.Actions;   // 기본
        options.HistoryLimit = 200;              // 0이면 무제한
    };
}
```

`GBibleRuntime.Configuring`은 세션을 만들기 직전에 옵션을 넘겨준다. 세션을 직접 만들 때는 `new GameSession(new SessionOptions { … })`.

| 옵션 | 기본값 | 뜻 |
|---|---|---|
| `History` | `HistoryMode.Actions` | 되돌리기 방식(아래 표) |
| `HistoryLimit` | `0` | 행동 단위 방식에서 Undo할 수 있는 최대 행동 수. 0은 무제한. 넘으면 가장 오래된 행동부터 Undo할 수 없게 된다 |
| `SnapshotInterval` | `1.0` | 스냅샷 방식의 간격(게임 시간 초) |
| `SnapshotLimit` | `30` | 보관할 스냅샷 수 |

| `HistoryMode` | 되돌리는 단위 | 어울리는 장르 |
|---|---|---|
| `Actions` | 행동 하나 | 턴제·퍼즐·전략·카드 |
| `Snapshots` | 스냅샷 한 칸 | 실시간 액션 |
| `None` | 되돌리지 않음(기록 비용 없음) | 되돌리기가 없는 게임 |

어느 방식이든 게임 코드는 `session.Undo()` 하나로 되돌린다.

### 2.11 스냅샷 방식: `CaptureSnapshot`

- 매 프레임 끝에 `SnapshotInterval`초가 지났으면 시스템이 스냅샷을 찍는다(행동 실행 중이 아닐 때).
- `session.CaptureSnapshot()`으로 중요한 순간(보스 입장 등)에 직접 찍을 수 있다. 행동 실행 중에 부르면 예외다.
- `Undo()` 한 번 = 스냅샷 한 칸 되감기. 찍은 뒤로 아무 일도 없었으면 한 칸 더 뒤로 간다.
- 되감으면 세션·메타 구역의 값, 난수, **게임 시계(시간·틱)**가 함께 돌아간다. 되감은 구간의 행동은 "Undo됨"으로 표시되고, 그 구간에 달성한 도전과제·보상은 다시 적용되어 유지된다.
- 스냅샷 방식에서는 행동별 Undo 이력을 남기지 않는다.
- 스냅샷은 저장 파일과 같은 모습(저장 트리)으로 찍되 메모리 안에만 둔다. 그래서 도감 항목이 아닌 에셋 참조(스프라이트 등)도 되감은 뒤 그대로다.

### 2.12 행동 밖 변경과 Undo 경고

```csharp
var hero = Game.Find<Unit>("hero");
hero.hp = 1;                    // 행동 밖: 다음 Sync에 "행동 밖 변경"으로 기록, Undo 안 됨
```

- 씬 코드·코루틴·이벤트를 받는 코드에서 플레이어 데이터를 고치면 행동 밖 변경이다. 기록은 남고(Undo는 안 됨), 멀티플레이 전송도 행동 묶음이 아니라 Sync마다 따로 나간다.
- 행동 밖 변경이 **마지막 행동이 바꾼 곳과 겹치는데** 그 행동을 Undo하면, Undo가 행동 밖 변경을 덮어쓴다(값은 행동 전으로). 이때 기록(모니터 타임라인·콘솔)에 경고가 남는다: "Undo '행동'가 행동 밖에서 바뀐 값을 덮었습니다: … 되돌릴 수 있어야 하는 변경은 행동 안에서 하세요."
  - 겹침은 같은 경로이거나 한쪽이 다른 쪽을 품는 경우다(`units[#3]`과 `units[#3].hp`).
  - 단순 값(숫자·글자 같은 플레이어 데이터 키)도 같다: 행동이 남긴 값과 Undo 직전 값이 다르면 같은 경고를 남긴다.
  - 이 경고는 클래스 데이터(Mirror로 비교하는 데이터)에만 있고, `DescribeChanges`가 켜져 있을 때(기본)만 나온다. 숫자·글자 같은 단순 값 키는 경고 없이 행동 전 값으로 덮인다.
- 행동 안에서 다른 행동을 `Game.Do`하거나 `Game.Use`하면, 그 행동은 **바깥 행동의 일부로 그 자리에서** 실행된다(새 Undo 단위를 만들지 않는다). Undo·변경 묶음은 바깥 행동 하나이고, 안쪽에서 실패하면 바깥 행동 전체가 취소된다. 예: 카드 내기 = 에너지 지불 + 손패 이동 + `Game.Use(카드)` + 다 쓰러졌으면 `Game.Do(new WinBattle())`.

---

## 3. 나중에 일어나는 효과 (람다·코루틴)

코루틴, 타이머, 트윈 완료 콜백처럼 **나중에 실행되는 코드는 어떤 행동의 바깥에서 돈다.** 행동의 `Run` 안에서 만든 람다라도 실제로 실행되는 시점에는 그 행동이 이미 끝나 있다.

그 결과:

1. 람다·코루틴이 고친 값은 기록되지만 **행동 밖 변경**이다. Undo되지 않는다.
2. 행동 클래스의 `Game` 속성은 `Run`이 끝나면 `null`이다. 나중에 쓰려면 `GBibleRuntime.Game`을 쓰거나 `Run` 안에서 지역 변수로 받아 둔다.
3. 시작한 행동을 Undo해도 코루틴은 멈추지 않는다.
4. 그 사이에 대상이 Undo·사망·불러오기로 사라졌을 수 있다.

**되돌릴 수 있게 하려면 람다·코루틴 안에서 `Game.Do(...)`로 행동을 실행한다.** 그러면 지연 효과 한 번 한 번이 각각 Undo 한 단위가 된다. 그리고 실행하기 전에 대상이 아직 있는지 id로 다시 찾는다. 남은 횟수 같은 진행 상태는 플레이어 데이터에 두면 Undo·저장이 함께 따라온다.

```csharp
using System;
using System.Collections;
using GBible;
using UnityEngine;

[Serializable]
public class PoisonedUnit
{
    public string id;
    public int hp = 30;
    public int poisonTicks;          // 남은 횟수도 플레이어 데이터에(Undo·저장에 함께)
}

[Serializable]
public class ApplyPoison : GameAction<PoisonedUnit>
{
    public int ticks = 3;
    protected override void Run(PoisonedUnit u) => u.poisonTicks = ticks;
}

[Serializable]
public class PoisonTick : GameAction<PoisonedUnit>
{
    public int damage = 2;
    protected override void Run(PoisonedUnit u)
    {
        if (u.poisonTicks <= 0) return;
        u.hp -= damage;
        u.poisonTicks--;
    }
}

public class PoisonDriver : MonoBehaviour
{
    public void Poison(string unitId)
    {
        GBibleRuntime.Game.Do(new ApplyPoison { target = unitId, ticks = 3 });
        StartCoroutine(Tick(unitId));
    }

    IEnumerator Tick(string unitId)
    {
        while (true)
        {
            yield return new WaitForSeconds(1f);                  // 게임 시간(일시정지·배속을 따른다)
            var game = GBibleRuntime.Game;
            var unit = game?.Find<PoisonedUnit>(unitId);
            if (unit == null || unit.poisonTicks <= 0) yield break;   // Undo·사망으로 사라졌거나 끝났으면 멈춘다
            game.Do(new PoisonTick { target = unitId });           // 한 번 한 번이 Undo 한 단위
        }
    }
}
```

`ApplyPoison`을 Undo하면 `poisonTicks`가 0으로 돌아가므로, 다음 틱에서 코루틴이 스스로 멈춘다.

**잘못된 예** — 나중에 도는 람다 안에서 값을 직접 고친다.

```csharp
using System;
using System.Collections.Generic;
using GBible;
using UnityEngine;

// 게임 코드가 직접 만든 간단한 지연 실행기(시스템 기능이 아니다).
public class DelayQueue : MonoBehaviour
{
    public static DelayQueue Instance;
    readonly List<(float at, Action run)> _pending = new List<(float, Action)>();

    void Awake() => Instance = this;
    public void After(float seconds, Action run) => _pending.Add((Time.time + seconds, run));

    void Update()
    {
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            if (_pending[i].at > Time.time) continue;
            var run = _pending[i].run;
            _pending.RemoveAt(i);
            run();
        }
    }
}

[Serializable]
public class Blast : GameAction<PoisonedUnit>
{
    public int damage;
    protected override void Run(PoisonedUnit u) => u.hp -= damage;
}

[Serializable]
public class DelayedBlast : GameAction<PoisonedUnit>
{
    protected override void Run(PoisonedUnit u)
    {
        var game = Game;   // Run이 끝나면 Game 속성은 null이 되므로 지역 변수로 받아 둔다
        var id = u.id;

        // 나쁨: 2초 뒤 직접 고침 → 행동 밖 변경, Undo 안 됨. u를 그대로 쓰면 Undo 뒤의 다른 객체를 놓칠 수도 있다.
        DelayQueue.Instance.After(2f, () => u.hp -= 10);

        // 좋음: 2초 뒤 행동으로 실행 → 그 효과가 Undo 한 단위. 대상이 아직 있는지 먼저 확인한다.
        DelayQueue.Instance.After(2f, () =>
        {
            if (game.Find<PoisonedUnit>(id) != null) game.Do(new Blast { target = id, damage = 10 });
        });
    }
}
```

`GameAction<T>`는 대상을 찾지 못하면 예외로 실패하므로, 지연 실행에서는 `Find`로 먼저 확인하거나 `Do`를 `try`로 감싼다.

---

## 4. 상황별 예제

### 4.1 턴제 전술: 이동 · 공격 · 턴 넘기기 · Undo 버튼

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using GBible;
using UnityEngine;

[Serializable]
public class TacticsUnit
{
    public string id;
    public string team;
    public int x, y;
    public int hp = 30;
    public bool acted;
}

[Serializable]
public class Battlefield
{
    public int turn = 1;
    public List<TacticsUnit> units = new List<TacticsUnit>();
}

[Serializable]
public class Move : GameAction<TacticsUnit>
{
    public int x, y;
    protected override void Run(TacticsUnit u)
    {
        if (u.acted) throw new InvalidOperationException("이미 행동했습니다.");
        u.x = x;
        u.y = y;
        u.acted = true;
    }
}

[Serializable]
public class Attack : GameAction<TacticsUnit>
{
    public string attacker;
    protected override void Run(TacticsUnit target)
    {
        var from = Game.Find<TacticsUnit>(attacker);
        var damage = 8 + Game.Random("damage").Range(0, 5);     // 결정론 난수: Undo하면 함께 되돌아간다
        target.hp -= damage;
        from.acted = true;
        if (target.hp <= 0) Game.All<Battlefield>()[0].units.Remove(target);
    }
}

[Serializable]
public class NextTurn : SimpleAction
{
    protected override void Run()
    {
        var field = Game.All<Battlefield>()[0];
        field.turn++;
        foreach (var u in field.units) u.acted = false;
    }
}

public class TacticsInput : MonoBehaviour
{
    string _selected;                                    // 오래 들고 있을 것은 id로
    GameApi Game => GBibleRuntime.Game;

    public void OnClickUnit(TacticsUnit u) => _selected = u.id;
    public void OnClickTile(int x, int y) => Game.Do(new Move { target = _selected, x = x, y = y });
    public void OnClickEnemy(TacticsUnit enemy) => Game.Do(new Attack { target = enemy.id, attacker = _selected });
    public void OnClickEndTurn() => Game.Do(new NextTurn());
    public void OnClickUndo() => Game.Session.Undo();
}
```

Undo하면 쓰러져 리스트에서 빠졌던 적이 같은 객체로 제자리에 돌아오고, 피해 난수도 되돌아가 다시 공격하면 같은 피해가 나온다.

### 4.2 카드 게임: 턴 안에서만 되돌리기

턴을 넘긴 뒤에는 그 전 행동을 되돌리지 못하게 하려면, 턴 넘기기 행동을 실행한 뒤 Undo 이력을 비운다(행동 단위 방식).

```csharp
using System;
using System.Collections.Generic;
using GBible;

[Serializable]
public class Duel
{
    public int mana = 3;
    public int turn = 1;
    public List<string> hand = new List<string>();
    public List<string> field = new List<string>();
}

[Serializable]
public class PlayCard : SimpleAction
{
    public string card;
    public int cost;
    protected override void Run()
    {
        var duel = Game.All<Duel>()[0];
        if (duel.mana < cost) throw new InvalidOperationException("마나 부족");
        if (!duel.hand.Remove(card)) throw new InvalidOperationException("손에 없는 카드");
        duel.mana -= cost;
        duel.field.Add(card);
    }
}

[Serializable]
public class PassTurn : SimpleAction
{
    protected override void Run()
    {
        var duel = Game.All<Duel>()[0];
        duel.turn++;
        duel.mana = Math.Min(10, duel.turn + 2);
    }
}

public static class DuelFlow
{
    public static void EndTurn(GameApi game)
    {
        game.Do(new PassTurn());
        game.Session.Data.ClearHistory();   // 이 시점 이전은 Undo할 수 없다
    }
}
```

### 4.3 RPG 상점: 조건이 맞지 않으면 아무것도 바꾸지 않기

```csharp
using System;
using System.Collections.Generic;
using GBible;

[Serializable]
public class Shop
{
    public int gold = 100;
    public Dictionary<string, int> stock = new Dictionary<string, int> { ["potion"] = 3 };
    public List<string> bag = new List<string>();
}

[Serializable]
public class Purchase : SimpleAction
{
    public string item;
    public int price;

    protected override void Run()
    {
        var shop = Game.All<Shop>()[0];
        if (!shop.stock.TryGetValue(item, out var left) || left <= 0) throw new InvalidOperationException("품절");
        if (shop.gold < price) throw new InvalidOperationException("골드 부족");
        shop.gold -= price;
        shop.stock[item] = left - 1;
        shop.bag.Add(item);
    }

    public override string Describe() => $"{item} 구매 ({price}골드)";
}
```

검사를 앞에 두고 실패하면 예외를 던진다. 중간에 실패해도 Transaction 전체가 되돌아가므로 골드만 깎이는 일이 없다. 조용히 아무 일도 하지 않고 `return`하면 "값이 바뀌지 않은 행동"으로 Undo 이력에 한 칸 남는다.

### 4.4 실시간 액션: 스냅샷 되감기와 스킬 쿨다운

```csharp
using System;
using GBible;
using UnityEngine;

[Serializable]
public class Caster
{
    public string id;
    public int mp = 50;
    public double fireballReadyAt;      // 게임 시계 기준 시각(초)
}

[Serializable]
public class CastFireball : GameAction<Caster>
{
    public const double Cooldown = 3.0;

    protected override void Run(Caster c)
    {
        var now = Game.Session.Clock.Time;
        if (now < c.fireballReadyAt) throw new InvalidOperationException("쿨다운 중");
        if (c.mp < 10) throw new InvalidOperationException("MP 부족");
        c.mp -= 10;
        c.fireballReadyAt = now + Cooldown;
    }
}

public static class RealtimeSetup
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void Hook() => GBibleRuntime.Configuring += options =>
    {
        options.History = HistoryMode.Snapshots;
        options.SnapshotInterval = 0.5;    // 0.5초마다
        options.SnapshotLimit = 20;        // 최근 10초까지
    };
}
```

쿨다운을 `Time.time`이 아니라 게임 시계(`session.Clock.Time`)로 적어 두면, 스냅샷을 되감을 때 시계와 `fireballReadyAt`이 함께 돌아가 쿨다운이 어긋나지 않는다. 남은 쿨다운 표시는 `c.fireballReadyAt - Game.Session.Clock.Time`.

### 4.5 행동 안에서 되돌리지 않을 기록 남기기

```csharp
using System;
using GBible;

[Serializable]
public class EnterBossRoom : SimpleAction
{
    protected override void Run()
    {
        var data = Game.Session.Data;
        data.Set("dungeon.floor", 10.0);                     // Undo 대상
        using (data.Permanent())
            data.Set("codex.bossSeen", true);                 // 한 번 본 보스 도감은 되돌리지 않는다
    }
}
```

두 키 모두 정의되어 있어야 한다(도감 또는 `Define`).

---

## 5. 주의할 점 · 자주 하는 실수

| 증상 | 원인과 해결 |
|---|---|
| Undo해도 값이 안 돌아옴 | 행동 밖에서 고쳤다(씬 코드·코루틴·이벤트를 받는 코드). 행동으로 실행한다. `Permanent()` 안이었거나, `HistoryLimit`을 넘었거나, 방식이 `None`일 수도 있다. |
| 지연 효과에서 `NullReferenceException` | 행동 클래스의 `Game` 속성은 `Run`이 끝나면 `null`이다. `GBibleRuntime.Game`을 쓰거나 지역 변수로 받아 둔다. |
| "처리기가 없습니다" | `GameAction`을 직접 상속했는데 `Actions.Register`를 하지 않았다. `GameAction<T>`·`SimpleAction`을 상속하면 등록이 필요 없다. |
| "… '…'을(를) 찾을 수 없습니다" | `target`에 없는 id를 넣었다(또는 Undo로 대상이 사라졌다). `Find`로 먼저 확인한다. |
| "id 필드가 없어 Find로 찾을 수 없습니다" | `GameAction<T>`의 `T`에 `public string id;` 필드가 없다. |
| Undo 뒤 "행동 밖에서 바뀐 값을 덮었습니다" 경고 | 행동이 바꾼 곳을 행동 밖에서 다시 고쳤다. 그 변경도 행동으로 만든다. |
| 행동 안에서 기다리기(코루틴·`await`) | 행동은 한 번에 끝나야 한다. 연출은 끝난 결과(이벤트·기록)를 보고 따로 재생하고, 시간이 걸리는 효과는 3장처럼 나눈다. |
| Undo로 좋은 난수를 다시 뽑으려 함 | Undo하면 난수 상태도 돌아가 같은 행동은 같은 결과를 낸다. |
| 실패한 행동의 예외가 게임을 멈춤 | `Do`는 행동이 실패하면 변경을 취소한 뒤 예외를 다시 던진다. 예상되는 실패는 부르는 쪽에서 잡는다. |
