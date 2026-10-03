# 03. 게임 루프 — 입력에서 화면까지

02장에서 정한 클래스로 게임을 실제로 돌린다. 플레이어가 카드를 끌어 놓는 순간부터 값이 바뀌고, 이벤트가 퍼지고, 화면이 다시 그려지기까지의 한 바퀴를 따라가며, 판·전투·턴이 어떤 행동으로 이어지는지, 실패하면 무엇이 취소되는지, 화면 코드와 게임 코드의 경계가 어디인지 정한다.

이 장에서 쓰는 말:

| 말 | 뜻 |
|---|---|
| 행동 | 플레이어 입력 하나를 담은 데이터 클래스(`GameAction`). `Game.Do(행동)`으로 실행하고, 실행하는 즉시 끝난다. 행동 하나 = Undo 한 단위 = 네트워크 한 묶음. |
| 런 스크립트 | 씬에 두는 MonoBehaviour 하나. 버튼·드래그 같은 입력을 행동으로 바꿔 보내는 창구. 카드 던전에서는 `RunDirector`. |
| Sync | 시스템이 플레이어 데이터의 사본(Mirror)과 지금 값을 비교해 바뀐 곳만 기록하는 동작. 행동이 끝날 때 한 번 일어나고, 그 기록이 Undo·저장·멀티플레이 전송에 쓰인다. |
| 행동 밖 변경 | 행동 실행 중이 아닐 때 바뀐 플레이어 데이터. 기록은 남지만 Undo되지 않고, 행동 묶음으로 전송되지도 않는다. |
| 이벤트를 쏜다 / 받는다 | 게임 코드가 `Game.Events.Publish`로 일어난 일을 이벤트 버스에 넘기는 것이 "쏜다", 로그·도전과제·유물의 Trigger나 화면의 `Subscribe`가 그것을 처리하는 것이 "받는다". |
| Block | 기획자가 도감에서 조립하는 로직 단위(효과·대상·조건). 카드 효과는 효과 Block으로 실행된다. |
| Trigger · Outcome | 도감 항목의 "언제(Trigger) → 무엇을(Outcome, 결과)". 이벤트를 받으면 Trigger를 검사하고 결과를 실행한다. |

---

## 1. 이 단계의 목표

입력 → 행동 → 이벤트 → 화면의 한 바퀴를 하나의 규칙으로 고정한다: **값을 바꾸는 일은 모두 행동 안에서, 화면은 읽고 이벤트를 받아 보여 주기만**. 그 위에 판·전투·턴 진행을 행동의 연결로 쌓는다.

## 2. 할 일 순서

1. 씬에 런 스크립트를 하나 두고, 입력마다 행동 하나를 보내는 창구 함수를 만든다.
2. 게임이 시작될 때 판을 이어 하거나 새로 여는 흐름을 런 스크립트의 `Start`에 둔다.
3. 판·전투·턴의 진행을 행동으로 쓴다. 다른 행동을 부르는 행동은 `Game.Do`를 그대로 부른다.
4. 실패할 수 있는 조건(에너지 부족, 손에 없는 카드)은 행동 앞부분에서 예외로 던지고, 런 스크립트에서 예상한 실패만 잡는다.
5. 다른 기능이 반응할 순간마다 행동 안에서 이벤트를 쏜다.
6. 화면은 플레이어 데이터를 읽어 그리고, 연출은 이벤트를 받아 튼다. 화면에서 플레이어 데이터를 고치지 않는다.
7. 시간이 걸리는 효과·씬 전환을 행동과 떼어 놓는다.

## 3. 판단 기준

**이 코드는 어디에 쓸까**

| 이런 코드라면 | 여기 |
|---|---|
| 플레이어 데이터를 바꾼다 | 행동의 `Run` 안(또는 행동 안에서 불리는 Block·`[BindTo]`·규칙 함수) |
| 버튼을 눌렀을 때 무엇을 할지 고른다 | 런 스크립트: 행동을 만들어 `Game.Do` |
| 값을 화면에 그린다 | 화면 코드: `Game.Data()`·`Game.Find`로 읽기만 |
| 맞음·쓰러짐 같은 연출을 튼다 | 화면 코드: 이벤트를 `Subscribe`로 받는다 |
| 다른 기능이 반응해야 할 순간이다 | 행동 안에서 `Game.Events.Publish` |
| 여러 행동이 같이 쓰는 계산이다 | 평범한 static 함수(카드 던전의 `Rules`) |

**행동을 하나로 묶을까, 나눌까**

| 이럴 때 | 이렇게 |
|---|---|
| 플레이어가 한 번 누른 결과로 함께 일어나야 한다(카드 내기 → 효과 → 마지막 적 처치 → 전투 승리) | 바깥 행동 안에서 `Game.Do`·`Game.Use`. Undo 한 번에 통째로 돌아간다. |
| 시간이 지나 따로 일어난다(1초 뒤 폭발, 다음 프레임의 연출 끝) | 나중에 도는 코드에서 새 행동을 `Game.Do`. 각각 Undo 한 단위. |
| 플레이어가 따로 누른다(카드 두 장) | 행동 두 번. |

**실패를 어떻게 다룰까**

| 이럴 때 | 이렇게 |
|---|---|
| 규칙상 할 수 없다(에너지·골드 부족) | 행동 안에서 `InvalidOperationException`을 던진다. 바뀐 값이 모두 취소되고 예외가 `Do`를 부른 쪽으로 돌아온다. |
| 할 수 없다는 걸 미리 보여 주고 싶다(버튼 흐리게) | 화면이 값을 읽어 판단한다. 사용 기능·능력의 사용 조건이면 `Game.CanUse`. |
| 그냥 `return`하고 싶다 | 하지 않는다. 아무것도 바꾸지 않은 행동도 Undo 이력에 한 칸 남는다. |

## 4. 카드 던전에서 실제로

### 4-1. 한 바퀴: 카드 한 장을 낼 때

플레이어가 화염구를 고블린 위에 놓았다. 일어나는 일을 순서대로 적으면 이렇다.

```
[화면] 카드를 적 위에 놓음
  └ RunDirector.TryPlay(카드 id, 적 id)
      └ Game.Do(new PlayCard { target = 카드 id, targetId = 적 id })
          ① 행동 시작 — 그 전까지의 행동 밖 변경을 떼어 두고, 난수 상태를 보관
          ② PlayCard.Run(덱 카드)
               비용은 카드 원본에서 읽음(Rules.CardData)
               에너지 − 비용, 손패 → 버린 더미
               Game.Use("fireball", 주인공, 적 id)   ← 행동 안의 행동
                 └ 카드 원본의 사용 기능: 피해 Block → Rules.Damage
                      └ DamageDealt를 쏜다  (받는 쪽: 연출)
                      └ 적이 쓰러지면 EnemyKilled를 쏜다  (받는 쪽: 로그 kills)
                 └ 상태 걸기 Block → 화상
               CardPlayed를 쏜다
               적이 모두 쓰러졌으면 Game.Do(new WinBattle())
                 └ TurnEnded를 쏜다  (받는 쪽: 로그 maxTurnDamage — 마지막 턴도 센다)
                 └ BattleWon을 쏜다  (받는 쪽: 유물 불타는 피, 도전과제 첫 승리)
          ③ 행동 끝 — 모든 플레이어 데이터를 Sync → Undo 한 단위로 기록, 변경 묶음을 전송용으로 내보냄
          ④ 바뀐 값을 보는 조건(도전과제 등)을 다시 평가
  └ [화면] 다음 그리기에서 Game.Data().Run을 읽어 에너지·손패·체력을 그림
     [연출] Subscribe로 받은 DamageDealt·EnemyKilled에 맞춰 숫자·쓰러짐 연출
```

여기서 지킬 것은 세 가지다.

- **값은 ②에서만 바뀐다.** 이벤트를 받은 로그·유물의 결과도 ② 안에서 실행되므로 같은 행동의 일부다. 그래서 Undo 한 번에 에너지·손패·피해·처치 수·유물 효과가 함께 돌아간다. 도전과제 달성과 그 결과만은 Undo 이력 밖에서 실행되어 남는다.
- **이벤트는 그 자리에서 바로 받는다.** `Publish`는 대기열 없이 받는 쪽을 모두 부른 뒤 돌아온다. `EnemyKilled`를 쏜 다음 줄에서는 처치 수 로그가 이미 올라가 있다.
- **화면은 ③ 뒤의 결과만 본다.** 행동 중간 모습은 밖으로 나가지 않는다.

### 4-2. 런 스크립트 `RunDirector`

런 스크립트는 입력을 행동으로 바꿔 보내는 일만 한다. 화면 버튼이 부르는 함수가 여기 모인다.

```csharp
public class RunDirector : MonoBehaviour
{
    static GameApi Game => GBibleRuntime.Game;

    void Start()
    {
        // 저장된 판이 있으면 이어 하고, 없으면 새 판.
        if (!GBibleRuntime.LoadRun(GBibleRuntime.Session) || !Game.Data().Run.IsActive) Game.Do(new StartRun());
    }

    // … TryPlay는 4-5에서 본다

    public void EndTurn() => Game.Do(new EndTurn());

    public void Undo() => GBibleRuntime.Session.Undo();
}
```

씬 코드는 `GBibleRuntime.Game`을 속성으로 줄여 둔다. 세션은 첫 씬을 불러오기 전에 만들어지므로 `Start`에서 바로 써도 된다. `Undo`는 마지막 행동 하나를 되돌린다. 판을 불러오는 일은 자동으로 일어나지 않고 게임이 직접 부른다(`LoadRun`은 성공하면 `true`).

불러오기에 성공해도 그 판이 이미 끝났을 수 있다. 판 자동 저장(`AutoSaveRun`, 기본 켬)은 게임을 끝낼 때 지금 판을 저장하므로, 주인공이 쓰러진 판도 저장 파일에 남는다. 그래서 판을 끝내는 `EndRun`이 층을 0으로 돌리고, 이어 하기는 `RunState.IsActive`(층이 0보다 큰가)를 보고 끝난 판이면 새 판을 연다. "끝났다"를 따로 저장하지 않고 판 데이터의 값으로 판단하므로 Undo·저장과 어긋나지 않는다.

### 4-3. 판 · 전투 · 턴 진행

카드 던전의 진행은 행동의 연결이다.

| 단계 | 행동 | 하는 일 | 쏘는 이벤트 |
|---|---|---|---|
| 판 시작 | `StartRun` | 층·골드·주인공·시작 덱·유물·물약을 정하고 첫 전투를 연다 | — |
| 전투 시작 | `StartBattle` | 몬스터 원본으로 적을 만들고, 덱을 섞어 뽑을 더미로, 카드를 뽑는다 | 턴 시작 |
| 카드 뽑기 | `DrawCards` | 뽑을 더미가 비면 버린 더미를 섞어 다시 쓴다 | — |
| 카드 내기 | `PlayCard` | 4-1 | 카드 사용(+ 효과 쪽의 피해·적 처치) |
| 턴 끝 | `EndTurn` | 적 공격 → 주인공이 쓰러지면 판 끝, 아니면 손패를 버리고 다음 턴 | 턴 끝, 턴 시작 |
| 전투 승리 | `WinBattle` | 마지막 턴을 마감하고, 보상 골드, 다음 층 | 턴 끝, 전투 승리, 골드 획득 |
| 판 끝 | `EndRun` | 남길 값을 프로필로 옮기고 층을 0으로 | 판 끝 |

전투 승리가 턴 끝 이벤트를 먼저 쏘는 데는 이유가 있다. 마지막 적은 플레이어가 턴을 끝내기 전에 카드로 쓰러지므로, 턴 끝 행동이 돌지 않는다. 턴 끝 이벤트를 받는 로그(한 턴 최대 피해)가 그 턴을 놓치지 않게, "턴이 끝나는 길"이 둘이면 두 길 모두에서 쏜다. 이벤트는 "행동이 실행됐다"가 아니라 "그 일이 일어났다"를 알리는 것이므로, 그 일이 일어나는 모든 자리에서 쏘아야 한다.

턴 끝은 이렇게 생겼다. 턴 끝 이벤트를 먼저 쏘고(이번 턴 피해 합을 실어서), 적이 차례로 공격한 뒤 판이 끝났는지 본다.

```csharp
var run = Game.Data().Run;
var battle = run.battle ?? throw new InvalidOperationException("전투 중이 아닙니다.");
Game.Events.Publish(new TurnEnded { turn = battle.turn, damage = battle.damageThisTurn });

foreach (var enemy in battle.enemies.ToList())
{
    var pattern = Rules.MonsterOf(Game, enemy.monster)?.attackPattern;
    if (pattern != null && pattern.Count > 0) Rules.Damage(Game, enemy, run.hero, pattern[enemy.intentIndex % pattern.Count]);
    enemy.intentIndex++;
}
if (run.hero.IsDead)
{
    Game.Do(new EndRun { victory = false });
    return;
}
```

적의 공격 순서는 원본 몬스터(인스턴스)에 있고, 적은 지금 차례(`intentIndex`)만 들고 있다. 원본은 `Rules.MonsterOf`로 복사 없이 읽는다. 기획자가 공격 순서를 비워 둔 몬스터는 공격 없이 차례만 넘긴다. 피해는 규칙 함수 `Rules.Damage` 하나가 맡는다. 방어도를 먼저 깎고, 피해 이벤트를 쏘고, 적이 쓰러지면 전투에서 빼고 처치 이벤트를 쏜다. 카드 효과 Block도, `[BindTo]` 함수도, 적의 공격도 이 함수를 부르므로 피해 규칙이 한곳에만 있다.

전투에 이긴 뒤 다음 전투를 여는 입력(지도에서 방 고르기, 상점 나가기)은 런 스크립트에 창구를 하나 더 두어 `StartBattle`을 보낸다. 예: `Game.Do(new StartBattle { monsters = { GameIds.Instance.Dragon } });`

### 4-4. 행동 안의 행동

행동 안에서 다른 행동(`Game.Do`)이나 능력·사용 기능(`Game.Use`)을 부르면, 새 행동이 따로 시작되지 않고 **바깥 행동의 일부로 그 자리에서** 실행된다.

```csharp
// StartRun의 끝: 판을 정한 다음 첫 전투를 연다
Game.Do(new StartBattle { monsters = { GameIds.Instance.Goblin, GameIds.Instance.Slime } });
```

`StartRun` → `StartBattle` → `DrawCards`가 한 줄로 이어져도 Undo는 `StartRun` 하나다. `PlayCard` 안의 `Game.Use`(카드 효과)와 `Game.Do(new WinBattle())`도 같다. 그래서 공통 단계를 행동으로 만들어 두면 혼자 쓸 때(모니터·디버그 명령)와 다른 행동 안에서 쓸 때 같은 코드를 쓴다.

알아 둘 점:

- Sync·조건 평가(도전과제 달성 등)는 바깥 행동이 끝날 때 한 번 한다. 안쪽 행동이 끝난 순간이 아니다.
- 모니터 **행동** 탭에는 안쪽 행동도 한 줄씩 보이지만, Undo는 바깥 행동 하나를 되돌린다.
- 안쪽에서 난 예외는 바깥 행동으로 올라가 **전체**를 취소한다(4-5).

### 4-5. 실패 = 전체 취소

카드 내기는 바꾸기 전에 할 수 있는지부터 본다. 할 수 없으면 예외를 던진다.

```csharp
var run = Game.Data().Run;
var data = Rules.CardData(Game, card.card);   // 비용 같은 고정 값은 원본에서
var battle = run.battle ?? throw new InvalidOperationException("전투 중이 아닙니다.");
if (!battle.hand.Contains(card.id)) throw new InvalidOperationException("손에 없는 카드입니다.");
if (battle.energy < data.cost) throw new InvalidOperationException("에너지가 모자랍니다.");

battle.energy -= data.cost;
battle.hand.Remove(card.id);
battle.discard.Add(card.id);
```

행동 안에서 예외가 나면 그 행동이 바꾼 플레이어 데이터가 모두 되돌아가고, 난수도 행동 전 상태로 돌아가고, Undo 이력에 들어가지 않는다. 검사를 앞에 두는 것은 읽기 쉽게 하려는 것이지 안전을 위해서가 아니다. 에너지를 깎은 뒤에 실패해도 깎은 것까지 되돌아간다. 행동 안의 행동에서 난 실패도 같다. 예를 들어 카드 효과의 사용 조건이 맞지 않아 `Game.Use`가 예외를 내면, 이미 깎은 에너지와 옮긴 카드까지 `PlayCard` 전체가 취소된다.

예외는 `Do`를 부른 쪽으로 다시 던져진다. 런 스크립트는 **예상한 실패만** 잡아 화면에 이유를 보여 준다.

```csharp
public bool TryPlay(string cardId, string targetId, out string error)
{
    error = null;
    try
    {
        Game.Do(new PlayCard { target = cardId, targetId = targetId });
        return true;
    }
    catch (System.InvalidOperationException e)
    {
        error = e.Message;
        return false;
    }
}
```

규칙상의 실패는 `InvalidOperationException`으로 던지고 그것만 잡는다. 코드 버그로 난 `NullReferenceException` 같은 예외도 값은 똑같이 취소되지만, 잡지 않고 콘솔에 드러나게 둔다. 실패한 행동은 모니터 **행동** 탭에 "실패" 배지로 남고, 마우스를 올리면 오류 글이 보인다.

### 4-6. 화면과 시스템의 경계

화면 코드(화면 asmdef, 01장 4-3)는 다음만 한다.

- **읽기**: `Game.Data().Run`에서 체력·에너지·손패를 읽는다. 손패는 덱 카드 id 리스트이므로 한 장은 `Rules.CardOf(Game, id)`(안에서 `Game.Find<DeckCard>`)로 찾고, 그 장의 이름·비용·그림은 `Rules.CardData(Game, deckCard.card)`로 원본에서 읽는다. 강화 여부처럼 판마다 다른 값만 덱 카드에서 읽는다.
- **보내기**: 입력은 런 스크립트의 창구 함수로 보낸다. 화면이 행동을 직접 만들어도 되지만, 창구를 한곳에 모아 두면 입력 목록이 한눈에 보인다.
- **받기**: 맞음·쓰러짐·카드 사용 연출은 `Game.Events.Subscribe<DamageDealt>(…)`처럼 이벤트를 받아 튼다. 받은 `IDisposable`은 `OnDisable`에서 `Dispose`한다. 세션은 씬보다 오래 산다.

하지 않는 것:

- **화면에서 플레이어 데이터를 고치지 않는다.** `Find`로 꺼낸 객체를 씬 코드에서 고치면 행동 밖 변경이 된다. 기록은 남지만 Undo되지 않고, 멀티플레이에서 행동 묶음으로 나가지 않는다. 그 값이 마지막 행동이 바꾼 곳과 겹치면 Undo가 덮어쓰고 "행동 밖에서 바뀐 값을 덮었습니다" 경고가 남는다.
- **이벤트를 받는 코드에서 값을 바꾸지 않는다.** 같은 이유다. 값을 바꿔야 하는 반응은 Trigger의 결과(도전과제·유물)나 이벤트를 쏜 행동 안에서 한다.
- **객체를 오래 들고 있지 않는다.** 고른 카드·적은 id로 들고 있다가 쓸 때 `Find`한다. Undo는 같은 객체로 되돌리지만, 판 불러오기 뒤에는 다른 인스턴스일 수 있다.

값이 바뀐 것을 알고 화면을 다시 그리는 방법(`Changed`·`DataChanged`), 데이터는 한 번에 바뀌는데 연출은 시간을 두고 보여 주는 방법은 [06장](06-Presentation.md)에서 다룬다.

### 4-7. 지연 효과

행동은 한 번에 끝난다. 행동 안에서 코루틴·`await`로 기다리지 않는다. "나중에 일어나는 효과"는 둘로 나눠 생각한다.

**턴제의 "나중"은 다음 행동 안이다.** 카드 던전의 화상처럼 "턴이 끝날 때마다 피해"는 시간이 아니라 진행에 묶인 효과다. 남은 횟수·상태는 플레이어 데이터(`Fighter.statuses`)에 두고, 그 순간을 처리하는 행동(`EndTurn`) 안에서 처리한다. 그러면 Undo·저장이 따라오고 난수도 결정론을 지킨다. 카드 던전의 지금 `EndTurn`은 상태를 아직 처리하지 않는다. 화상·빙결이 효과를 내게 하려면 적 공격 앞뒤에 상태 처리를 더한다.

**시간이 걸리는 효과는 나눠서 나중에 `Game.Do`한다.** 코루틴·타이머 콜백처럼 나중에 도는 코드는 어떤 행동의 바깥이다. 행동의 `Run` 안에서 만든 람다라도 실제로 도는 시점에는 그 행동이 끝나 있고, 행동의 `Game` 속성은 `null`이다. 그래서:

- 나중에 도는 코드는 `GBibleRuntime.Game`을 쓰고, 값을 바꾸려면 그때 새 행동을 `Game.Do`한다. 지연 효과 한 번 한 번이 Undo 한 단위가 된다.
- 실행 전에 대상이 아직 있는지 id로 다시 찾는다(Undo·사망으로 사라졌을 수 있다).
- 시작한 행동을 Undo해도 코루틴은 멈추지 않는다. 남은 횟수를 플레이어 데이터에 두면, Undo로 횟수가 0이 되었을 때 코루틴이 스스로 멈추게 쓸 수 있다.

카드 던전에서 시간이 걸리는 것은 연출뿐이다. 데이터는 행동 하나로 즉시 바뀌고, 화면이 이벤트를 받아 시간을 두고 보여 준다([06장](06-Presentation.md)).

### 4-8. 씬 전환

세션은 씬과 상관없이 DontDestroyOnLoad의 `[MomoiProject]` 오브젝트와 함께 산다. 씬을 바꿔도 플레이어 데이터·Undo 이력·로그는 그대로다. 그래서:

- **씬 사이에 값을 넘기지 않는다.** 다음 씬이 필요한 값은 이미 플레이어 데이터에 있다. static 필드로 "지금 싸울 몬스터"를 넘기지 말고 `run.battle`을 읽는다.
- **어느 씬을 띄울지는 데이터로 정한다.** 전투 중인지는 `Rules.InBattle(Game)`(`run.battle != null`)로 안다. 예: `SceneManager.LoadScene(Rules.InBattle(Game) ? "Battle" : "Map");` 판을 불러온 뒤에도 같은 식으로 맞는 씬을 고르면 된다.
- **씬 전환은 행동이 아니다.** 행동이 끝난 뒤(입력 처리 끝, 또는 연출이 끝난 뒤) 화면 쪽에서 한다. 행동 안에서 씬을 불러오지 않는다.
- **판을 여는 코드는 한 번만 돌게 둔다.** `RunDirector.Start`는 판을 불러오거나 새로 연다. 씬마다 `RunDirector`를 두면 씬을 옮길 때마다 저장 파일을 다시 불러오거나 새 판을 열어 버린다. 런 스크립트는 처음 한 번 뜨는 씬(또는 씬을 옮겨도 남는 오브젝트)에 두고, 각 씬에는 그 씬의 화면 코드만 둔다.
- **받기를 정리한다.** 씬 오브젝트가 `Subscribe`한 것은 `OnDisable`·`OnDestroy`에서 `Dispose`한다. 하지 않으면 사라진 오브젝트를 세션이 계속 부른다.

## 5. 다른 장르라면

> **다른 장르라면** — 실시간 액션 게임은 입력이 프레임마다 들어오지만 규칙은 같다. 이동은 MonoBehaviour가 하고, 피격·스킬 사용·아이템 획득처럼 플레이어 데이터를 바꾸는 사건만 행동으로 보낸다. Undo는 스냅샷 되감기(`HistoryMode.Snapshots`)를 쓰고, 독·지속 피해 같은 시간 효과는 4-7처럼 코루틴에서 틱마다 행동을 `Game.Do`한다. 예제 전체는 [02. 행동과 Undo](../Manual/02-Actions-Undo.md) 3장.

> **다른 장르라면** — 턴을 넘긴 뒤에는 그 전 행동을 되돌리지 못하게 하려면, 턴 넘기기 행동을 실행한 다음 `Game.Session.Data.ClearHistory();`로 Undo 이력을 비운다. 카드 던전은 판 전체에서 Undo를 허락하고 `HistoryLimit`로 상한만 둔다.

> **다른 장르라면** — 턴제 전술 게임의 "공격 → 반격"처럼 한 입력에 여러 단계가 따라오면, 반격을 별도 행동으로 만들고 공격 행동 안에서 `Game.Do`한다. 반격만 디버그 명령으로 시험할 수 있고, Undo는 공격 하나로 돌아간다.

## 6. 체크리스트

- [ ] 씬에 런 스크립트가 하나 있고, 입력마다 행동 하나를 보내는 창구가 있다.
- [ ] 판을 이어 하거나 새로 여는 코드가 게임에서 한 번만 돈다.
- [ ] 플레이어 데이터를 바꾸는 코드가 모두 행동(또는 행동 안에서 불리는 Block·`[BindTo]`·규칙 함수) 안에 있다.
- [ ] 한 입력에 따라오는 단계는 바깥 행동 안에서 `Game.Do`·`Game.Use`로 부르고, Undo 한 번에 돌아가는 것을 모니터로 확인했다.
- [ ] 규칙상의 실패는 행동 안에서 `InvalidOperationException`으로 던지고, 런 스크립트는 그것만 잡는다.
- [ ] 다른 기능이 반응할 순간마다 행동 안에서 이벤트를 쏜다.
- [ ] 화면은 읽기·보내기·받기만 하고, `Subscribe`한 것을 `Dispose`한다.
- [ ] 시간이 걸리는 효과는 나중에 새 행동으로 실행하고, 실행 전에 대상을 id로 다시 찾는다.
- [ ] 씬 사이에 값을 static으로 넘기지 않고, 띄울 씬을 데이터로 정한다.

## 7. 더 보기

- [02. 행동과 Undo](../Manual/02-Actions-Undo.md) — `Do` 안에서 일어나는 일, 실패, Undo, 지연 효과
- [03. Game API](../Manual/03-GameApi.md) — `Find`·`All`·`Do`·`Use`, 씬 코드에서 쓰는 법
- [10. 이벤트](../Manual/10-Events.md) — 쏘기·받기, `Subscribe`와 `Dispose`
- [16. 능력과 사용 기능](../Manual/16-Abilities-Usable.md) — `Game.Use`의 실행 흐름, `Game.CanUse`
- [20. 시작하기](../Manual/20-Setup.md) — `GBibleRuntime`, 판 저장·불러오기
- [22. 모니터와 디버깅](../Manual/22-Monitor-Debug.md) — 행동 탭, 타임라인 묶어 보기
