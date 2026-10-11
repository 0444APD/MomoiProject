# 03. 게임 루프 — 입력에서 화면까지

02장의 클래스로 게임을 돌린다. 규칙은 하나다. **값은 행동 안에서만 바꾸고, 화면은 읽고 이벤트를 받아 보여 주기만 한다.** 그 위에 판·전투·턴 진행을 행동의 연결로 쌓는다.

> [!note]- 이 장에서 쓰는 말
> | 말 | 뜻 |
> |---|---|
> | 행동 | 플레이어 입력 하나를 담은 데이터 클래스(`GameAction`). `Game.Do(행동)`으로 실행하고, 실행하는 즉시 끝난다. 행동 하나 = Undo 한 단위 = 네트워크 한 묶음. |
> | 런 스크립트 | 씬에 두는 MonoBehaviour 하나. 버튼·드래그 같은 입력을 행동으로 바꿔 보내는 창구. 카드 던전에서는 `RunDirector`. |
> | Sync | 시스템이 플레이어 데이터의 사본(Mirror)과 지금 값을 비교해 바뀐 곳만 기록하는 동작. 행동이 끝날 때 한 번 일어나고, 그 기록이 Undo·저장·멀티플레이 전송에 쓰인다. |
> | 행동 밖 변경 | 행동 실행 중이 아닐 때 바뀐 플레이어 데이터. 기록은 남지만 Undo되지 않고, 행동 묶음으로 전송되지도 않는다. |
> | 이벤트를 쏜다 / 받는다 | 게임 코드가 `Game.Events.Publish`로 일어난 일을 넘기는 것이 "쏜다", 로그·도전과제·유물의 Trigger나 화면의 `Subscribe`가 그것을 처리하는 것이 "받는다". |
> | Block | 기획자가 DB에서 조립하는 로직 단위(효과·대상·조건). 카드 효과는 효과 Block으로 실행된다. |
> | Trigger · Outcome | DB 항목의 "언제(Trigger) → 무엇을(Outcome, 결과)". 이벤트를 받으면 Trigger를 검사하고 결과를 실행한다. |
> | `GameFailure` | 플레이어에게 보일 실패. 텍스트 키와 기본 문장을 담는 `InvalidOperationException`이다. `Game.Fail`이 만들고, `Game.Pay`도 모자라면 이것을 던진다. |
> | 상태 효과 | DB › 상태 효과의 항목(화상·독·보호막). 대상에게 걸려 있는 동안 중첩·지속(턴 수·초)에 따라 효과를 낸다. |
> | 예약 | 행동 하나를 나중(이벤트를 N번 받은 순간, 게임 시간 N초 뒤)에 실행하게 맡겨 두는 것. |

---

## 1. 코드를 둘 자리

| 이런 코드라면 | 여기 | 폴더 |
|---|---|---|
| 플레이어 데이터를 바꾼다 | 행동의 `Run` 안(또는 그 안에서 불리는 Block·`[BindTo]`·규칙 함수) | Rules |
| 다른 기능이 반응할 순간이다 | 행동 안에서 `Game.Events.Publish` | Rules |
| 여러 행동이 같이 쓰는 계산이다 | 평범한 static 함수(카드 던전의 `Rules`) | Rules |
| 입력을 받아 무엇을 할지 고른다 | 런 스크립트: 행동을 만들어 `Game.Do` | Client |
| 값을 화면에 그린다 | 화면 코드: `Game.Data()`·`Game.Find`로 읽기만. 다시 그리기는 `GameView.Redraw` | Client |
| 맞음·쓰러짐 연출을 튼다 | 화면 코드: 이벤트를 `SubscribeAfterAction`으로 받아 연출 대기열(`GameView.Present`)에 넣는다 | Client |

## 2. 한 바퀴: 카드 한 장을 낼 때

플레이어가 화염구를 고블린 위에 놓았다.

```
[화면] 카드를 적 위에 놓음
  └ RunDirector.TryPlay(덱 카드, 적)
      └ Game.Do(new PlayCard { card = 덱 카드, target = 적 })
          ① 행동 시작 — card·target을 id로 지금 데이터에서 다시 찾아 넣음
          ② PlayCard.Run()
               Game.Pay(에너지, 비용)                    ← 행동 직전 이벤트를 거쳐 뺀다(모자라면 실패)
               손패 → 버린 더미
               Game.Use("fireball", 주인공, 적)           ← 행동 안의 행동
                 └ 피해 Block → Rules.Damage → DamageDealt·EnemyKilled를 쏜다
                 └ 상태 효과 걸기 → 적에게 화상(DB › 상태 효과 burn)
               CardPlayed를 쏜다
               적이 모두 쓰러졌으면 Game.Do(new WinBattle())
                 └ TurnEnded·BattleWon·GoldGained를 쏜다
          ③ 행동 끝 — Sync → Undo 한 단위로 기록
  └ [연출] ③ 뒤에 DamageDealt·EnemyKilled를 쏜 순서대로 받아 연출 대기열에  (SubscribeAfterAction → GameView.Present)
     [화면] 연출이 끝난 프레임 끝에 Game.Data().Run을 읽어 한 번 다시 그림    (GameView.Redraw)
```

- **값은 ②에서만 바뀐다.** 이벤트를 받은 로그·유물의 결과도 ② 안에서 실행된다. 그래서 Undo 한 번에 에너지·손패·피해·처치 수·유물 효과가 함께 돌아간다.
- **규칙은 이벤트를 그 자리에서 바로 받는다.** `Publish`는 `Subscribe`한 쪽을 모두 부른 뒤 돌아온다.
- **화면은 ③ 뒤의 결과만 본다.** 연출은 `SubscribeAfterAction`으로 행동이 성공한 뒤에 받고, 실패해 취소된 행동의 이벤트는 받지 않는다. 행동 중간 모습은 밖으로 나가지 않는다.

> [!note]- ①~③에서 일어나는 일 자세히
> - ① 그 전까지의 행동 밖 변경을 떼어 두고, 난수 상태를 보관한다. 대상 필드(`card`·`target`)는 id로 지금 데이터의 객체를 다시 찾아 넣는다. 싱글에서 방금 고른 객체면 같은 객체가 그대로 쓰이고, 못 찾으면 행동이 실패한다(02장 4절).
> - ② 비용 같은 고정 값은 카드 원본에서 읽는다(`Rules.CardData`). 에너지는 `Game.Pay`로 낸다(6절). 받는 쪽: `DamageDealt` → 연출(모아 두었다가 ③ 뒤에), `EnemyKilled` → 로그 `kills`, `TurnEnded` → 로그 `maxTurnDamage`(마지막 턴도 센다), `BattleWon` → 유물 불타는 피·도전과제 첫 승리.
> - ③ 모든 플레이어 데이터를 Sync해 Undo 한 단위로 기록하고, 변경 묶음을 전송용으로 내보낸다. 그다음 바뀐 값을 보는 조건(도전과제 등)을 다시 평가한다.
> - 판 저장이 "행동마다 저장"이면 그 프레임 끝(행동 밖)에서 `run.json`을 쓴다(DB › 시작 파일 › Bootstrap, [10장](10-SaveAndRelease.md)).
> - `EnemyKilled`를 쏜 다음 줄에서는 처치 수 로그가 이미 올라가 있다. 대기열이 없다.
> - 도전과제 달성과 그 결과만은 Undo 이력 밖에서 실행되어 Undo해도 남는다.

## 3. 런 스크립트 `RunDirector`

Client 폴더에 MonoBehaviour 하나를 두고, 게임에서 처음 한 번 뜨는 씬에 붙인다. 입력마다 창구 함수 하나를 둔다.

```csharp
public class RunDirector : MonoBehaviour
{
    static GameApi Game => GBibleRuntime.Game;

    void Start()
    {
        // 저장된 판이 있으면 이어 하고, 없으면 새 판.
        if (!GBibleRuntime.LoadRun(GBibleRuntime.Session) || !Game.Data().Run.IsActive) Game.Do(new StartRun());
    }

    // … TryPlay는 6절에서 본다

    public void EndTurn() => Game.Do(new EndTurn());

    public void Undo() => GBibleRuntime.Session.Undo();
}
```

판 저장은 코드가 없다. DB › 시작 파일 › Bootstrap의 **판 저장**·**판 끝 이벤트**대로 저절로 된다. 불러오기만 게임이 직접 부른다(`LoadRun`).

> [!note]- `Start`의 한 줄 자세히
> - 세션은 첫 씬을 불러오기 전에 만들어지므로 `Start`에서 바로 써도 된다. 씬 코드는 `GBibleRuntime.Game`을 속성으로 줄여 둔다.
> - `LoadRun`은 저장된 판을 불러오면 `true`다.
> - 불러온 판이 이미 끝난 판일 수 있다. 판 끝 이벤트(`RunEnded`)를 정해 두면 판이 끝날 때 판 저장이 지워지므로 보통은 없지만, 판 끝 이벤트를 비워 두고 "끌 때 저장"을 쓰면 주인공이 쓰러진 판도 저장된다. 그래서 `RunState.IsActive`(층이 0보다 큰가)도 함께 본다. "끝났다"를 판 데이터의 값으로 판단하므로 Undo·저장과 어긋나지 않는다.
> - `Undo`는 마지막 행동 하나를 되돌린다.

> [!note]- 타이틀의 이어 하기 · 새 판
> 타이틀 화면이 있으면 `GBibleRuntime.HasSavedRun`으로 '이어 하기' 버튼을 켜고, '새 판'은 `GBibleRuntime.NewRun(session)`(판 값·판 저장 파일을 함께 지움) → `StartRun` 순서다(카드 던전 `RunSaves`, [10장](10-SaveAndRelease.md)).

## 4. 판 · 전투 · 턴 진행

진행은 행동의 연결이다.

| 단계 | 행동 | 하는 일 | 쏘는 이벤트 |
|---|---|---|---|
| 판 시작 | `StartRun` | 층·골드·주인공·시작 덱·유물·물약을 정하고 첫 전투를 연다 | — |
| 전투 시작 | `StartBattle` | 몬스터 원본으로 적을 만들고, 덱을 섞어 뽑을 더미로, 카드를 뽑는다 | 턴 시작 |
| 카드 뽑기 | `DrawCards` | 뽑을 더미가 비면 버린 더미를 섞어 다시 쓴다 | — |
| 카드 내기 | `PlayCard` | 2절 | 카드 사용(+ 피해·적 처치) |
| 턴 끝 | `EndTurn` | 턴 끝 효과(화상) → 적이 모두 쓰러졌으면 전투 승리, 아니면 적 공격 → 주인공이 쓰러지면 판 끝, 아니면 손패를 버리고 다음 턴 | 턴 끝, 턴 시작 |
| 전투 승리 | `WinBattle` | 마지막 턴을 마감하고(턴 끝을 아직 쏘지 않았으면), 보상 골드, 다음 층 | 턴 끝, 전투 승리, 골드 획득 |

> [!note]- 시작 체력·골드·보상 골드는 밸런스 값 — DB에 세 항목
> 기획자가 바꿀 숫자는 코드에 적지 않고 DB › 밸런스 값에 둔다. 카드 던전은 세 항목을 만든다.
>
> | ID | 값 | 곡선 | 읽는 곳 |
> |---|---|---|---|
> | `startHp` | 70 | 없음 | `StartRun`: 주인공 체력·최대 체력 |
> | `startGold` | 99 | 없음 | `StartRun`: 시작 골드 |
> | `winGold` | 15 | 일정하게, 단계마다 +3 | `WinBattle`: 그 층 값의 ⅔~4⁄3(1층 10~20, 3층 14~28) |
>
> - 코드는 `Game.Balance(GameIds.Balance.StartHp)`, 층마다 커지는 값은 `Game.Balance(GameIds.Balance.WinGold, run.floor)`.
> - 손으로 만들어도 되고, 메뉴 **MomoiProject › DB 추가 › 카드 던전 밸런스 값**을 누르면 세 항목이 적용 전으로 생긴다(예시의 `Editor/AddBalanceValues.cs`). DB에서 확인하고 Ctrl+S.
> - 항목이 없으면 `StartRun`이 "없는 밸런스 값"으로 실패한다(새 판이 시작되지 않는다). DB에 만들고 Ctrl+S.
> - 플레이 중에 DB에서 값을 고치면 저장 전이라도 다음 판·다음 보상부터 바로 반영된다. 테스트는 DB 대신 `session.Balance.Define`으로 넣는다([08장](08-TestAndDebug.md) 6절).
| 판 끝 | `EndRun` | 남길 값을 프로필로 옮기고 층을 0으로 | 판 끝 |

이벤트는 "그 일이 일어났다"를 알린다. 그 일이 일어나는 모든 자리에서 쏜다.

> [!note]- 전투 승리가 턴 끝 이벤트를 쏘는 이유
> 마지막 적은 플레이어가 턴을 끝내기 전에 카드로 쓰러지므로 `EndTurn`이 돌지 않는다. 턴 끝 이벤트를 받는 로그(한 턴 최대 피해)가 그 턴을 놓치지 않게, "턴이 끝나는 길"이 둘이면 두 길(`EndTurn`·`WinBattle`) 모두에서 쏜다.
>
> 반대로 턴 끝 이벤트를 받은 화상이 마지막 적을 쓰러뜨리면, 턴 끝은 이미 쏘았다. 그때 `EndTurn`은 `new WinBattle { turnEnded = true }`로 불러 같은 턴 끝을 두 번 쏘지 않는다.

> [!note]- `EndTurn` 본문
> 턴 끝 이벤트를 먼저 쏘고(이번 턴 피해 합을 실어서), 적이 남았는지 본 뒤, 적이 차례로 공격하고 판이 끝났는지 본다.
> ```csharp
> var run = Game.Data().Run;
> var battle = run.battle ?? throw Game.Fail("fail.notInBattle", "전투 중이 아닙니다.");
> Game.Events.Publish(new TurnEnded { turn = battle.turn, damage = battle.damageThisTurn });
> // 턴 끝을 받는 효과(화상 같은 상태 효과)로 적이 모두 쓰러졌으면 전투 승리.
> if (battle.enemies.Count == 0)
> {
>     Game.Do(new WinBattle { turnEnded = true });
>     return;
> }
>
> foreach (var enemy in battle.enemies.ToList())
> {
>     var pattern = Rules.MonsterOf(Game, enemy.monster)?.attackPattern;
>     if (pattern != null && pattern.Count > 0) Rules.Damage(Game, enemy, run.hero, pattern[enemy.intentIndex % pattern.Count]);
>     enemy.intentIndex++;
> }
> if (run.hero.IsDead)
> {
>     Game.Do(new EndRun { victory = false });
>     return;
> }
> ```
> - 적의 공격 순서는 원본 몬스터에 있고, 적은 지금 차례(`intentIndex`)만 든다. 공격 순서를 비워 둔 몬스터는 차례만 넘긴다.
> - 피해는 `Rules.Damage` 하나가 맡는다. 방어도를 먼저 깎고, 피해 이벤트를 쏘고, 적이 쓰러지면 전투에서 빼고 처치 이벤트를 쏜다. 카드 Block·`[BindTo]`·적의 공격·화상이 모두 이 함수를 부르므로 피해 규칙이 한곳에만 있다.
> - 화상 피해는 `Publish(new TurnEnded …)` 한 줄 안에서 들어간다. 턴 끝을 받는 상태 효과가 그 자리에서 결과를 실행하기 때문이다(8절).

> [!note]- 다음 전투 열기
> 전투에 이긴 뒤 다음 전투를 여는 입력(지도에서 방 고르기, 상점 나가기)은 런 스크립트에 창구를 하나 더 두어 `StartBattle`을 보낸다. 예: `Game.Do(new StartBattle { monsters = { GameIds.Instance.Dragon } });`

## 5. 행동 안의 행동

행동 안에서 `Game.Do`·`Game.Use`를 부르면 새 행동이 따로 시작되지 않고 **바깥 행동의 일부로 그 자리에서** 실행된다.

```csharp
// StartRun의 끝: 판을 정한 다음 첫 전투를 연다
Game.Do(new StartBattle { monsters = { GameIds.Instance.Goblin, GameIds.Instance.Slime } });
```

`StartRun` → `StartBattle` → `DrawCards`가 이어져도 Undo는 `StartRun` 하나다.

| 이럴 때 | 이렇게 |
|---|---|
| 한 번 누른 결과로 함께 일어난다(카드 내기 → 효과 → 전투 승리) | 바깥 행동 안에서 `Game.Do`·`Game.Use`. Undo 한 번에 돌아간다 |
| 나중에 일어난다(2턴 뒤 폭발, 1초 뒤 폭발) | 상태 효과·예약에 맡긴다(8절). 행동 안에서 기다리지 않는다 |
| 플레이어가 따로 누른다(카드 두 장) | 행동 두 번 |

> [!note]- 알아 둘 점
> - 공통 단계를 행동으로 만들어 두면 혼자 쓸 때(모니터·개발 명령)와 다른 행동 안에서 쓸 때 같은 코드를 쓴다.
> - Sync·조건 평가(도전과제 달성 등)는 바깥 행동이 끝날 때 한 번 한다.
> - 모니터 **행동** 탭에는 안쪽 행동도 한 줄씩 보이지만, Undo는 바깥 행동 하나를 되돌린다.
> - 안쪽에서 난 예외는 바깥 행동으로 올라가 **전체**를 취소한다(6절).

> [!note]- 다른 장르라면: 턴제 전술
> "공격 → 반격"처럼 한 입력에 여러 단계가 따라오면, 반격을 별도 행동으로 만들고 공격 행동 안에서 `Game.Do`한다. 반격만 개발 명령으로 시험할 수 있고, Undo는 공격 하나로 돌아간다.

## 6. 실패 = 전체 취소

할 수 없으면 행동 안에서 예외를 던진다. 그 행동이 바꾼 값이 모두 되돌아간다.

- 플레이어에게 보일 실패는 `throw Game.Fail(텍스트 키, 기본 문장)`이다. 글은 텍스트 표로 번역된다([06장](06-Presentation.md) 5절).
- 비용은 `Game.Pay(플레이어 데이터, 필드, 양)`으로 낸다. 모자라면 같은 종류의 실패를 던지고, 넉넉하면 뺀다.

```csharp
var run = Game.Data().Run;
var data = Rules.CardData(Game, card.card);
var battle = run.battle ?? throw Game.Fail("fail.notInBattle", "전투 중이 아닙니다.");
if (!battle.hand.Contains(card.id)) throw Game.Fail("fail.notInHand", "손에 없는 카드입니다.");
Game.Pay(GameIds.PlayerData.Run.Id, "battle.energy", data.cost, what: "energy.name");   // 비용: 행동 직전 이벤트('이번 턴 비용 0' 등)를 거친 뒤 모자라면 실패

battle.hand.Remove(card.id);
battle.discard.Add(card.id);
```

런 스크립트는 `InvalidOperationException`만 잡아 화면에 이유를 보여 준다(`Game.Fail`·`Game.Pay`가 던지는 `GameFailure`도 그 한 종류다).

```csharp
public bool TryPlay(DeckCard card, Fighter target, out string error)
{
    error = null;
    try
    {
        Game.Do(new PlayCard { card = card, target = target });
        return true;
    }
    catch (System.InvalidOperationException e)
    {
        error = e.Message;
        return false;
    }
}
```

> [!note]- 실패 자세히
> - 예외가 나면 바뀐 플레이어 데이터가 모두 되돌아가고, 난수도 행동 전 상태로 돌아가고, Undo 이력에 들어가지 않는다. 예외는 `Do`를 부른 쪽으로 다시 던져진다.
> - `Game.Fail`은 예외를 만들기만 한다. 앞에 `throw`를 붙인다. 값이 들어가는 글은 `Game.Fail("fail.frozen", "얼어 있어 {turns}턴 동안 움직일 수 없습니다.", ("turns", 2))`.
> - `Game.Pay`는 비용 행동 직전 이벤트(`CostIntent`)를 행동 직전 이벤트에 통과시킨 뒤 지금 값이 모자라면 실패(키 `momoi.fail.notEnough`)를 던진다. 직접 `battle.energy -= data.cost`로 빼면 "이번 턴 비용 0" 같은 행동 직전 이벤트가 끼어들 자리가 없다. 화면에서 미리 볼 때는 `Game.CanPay`(행동 직전 이벤트 전 값)나 `Game.CanDo`(06장 2절).
> - 없는 ID 같은 프로그래머 실수는 `Game.Fail`이 아니라 그냥 `InvalidOperationException`으로 둔다(카드 던전의 `Rules.NewCard`). 플레이어에게 보일 글이 아니다.
> - 검사를 앞에 두는 것은 읽기 쉽게 하려는 것이지 안전을 위해서가 아니다. 에너지를 낸 뒤에 실패해도 낸 것까지 되돌아간다.
> - 행동 안의 행동에서 난 실패도 같다. 카드 효과의 사용 조건이 맞지 않아 `Game.Use`가 예외를 내면 `PlayCard` 전체가 취소된다.
> - 코드 버그로 난 `NullReferenceException` 같은 예외도 값은 똑같이 취소되지만, 잡지 않고 콘솔에 드러나게 둔다.
> - 실패한 행동은 모니터 **행동** 탭에 "실패" 배지로 남고, 마우스를 올리면 오류 글이 보인다.

> [!note]- 실패를 다루는 다른 방법
> | 이럴 때 | 이렇게 |
> |---|---|
> | 할 수 없다는 걸 미리 보여 주고 싶다(버튼 흐리게) | `Game.CanDo(행동, out 이유)`: 행동을 해 보고 되돌려 성공하는지와 실패 글을 준다([06장](06-Presentation.md) 2절). 사용 기능·능력의 사용 조건만 볼 때는 `Game.CanUse`. |
> | 그냥 `return`하고 싶다 | 하지 않는다. 아무것도 바꾸지 않은 행동도 Undo 이력에 한 칸 남는다. |

## 7. 화면과 시스템의 경계

화면 코드(Client)는 세 가지만 한다.

- **읽기**: `Game.Data().Run`에서 체력·에너지·손패를 읽는다.
- **보내기**: 입력은 런 스크립트의 창구 함수로 보낸다.
- **받기**: 연출은 `Game.Events.SubscribeAfterAction<DamageDealt>(…)`처럼 성공한 행동의 이벤트만 받아 연출 대기열(`GameView.Present`)에 넣는다. 값을 다시 그리는 것은 `GameView.Redraw(그리는 함수, 키)`에 맡긴다. 받은 `IDisposable`은 `OnDisable`에서 `Dispose`한다.

화면에서 플레이어 데이터를 고치지 않는다. 이벤트를 받는 코드에서도 고치지 않는다.

> [!note]- 손패 한 장 읽기
> 손패는 덱 카드 id 리스트다. 한 장은 `Rules.CardOf(Game, id)`(안에서 `Game.Find<DeckCard>`)로 찾고, 이름·비용·그림은 `Rules.CardData(Game, deckCard.card)`로 원본에서 읽는다. 강화 여부처럼 판마다 다른 값만 덱 카드에서 읽는다.

> [!note]- 하지 않는 것, 그 이유
> - **화면에서 고치기**: `Find`로 꺼낸 객체를 씬 코드에서 고치면 행동 밖 변경이 된다. 기록은 남지만 Undo되지 않고, 멀티플레이에서 행동 묶음으로 나가지 않는다. 마지막 행동이 바꾼 곳과 겹치면 Undo가 덮어쓰고 "행동 밖에서 바뀐 값을 덮었습니다" 경고가 남는다.
> - **받는 코드에서 고치기**: 같은 이유다. 값을 바꿔야 하는 반응은 Trigger의 결과(도전과제·유물)나 이벤트를 쏜 행동 안에서 한다.
> - **들고 있던 객체를 직접 읽기**: 버튼이 덱 카드·적 객체를 쥐고 있다가 행동에 넣는 것은 괜찮다. 행동이 실행 직전에 id로 지금 객체를 다시 찾는다. 하지만 그 객체를 화면이 직접 읽으면 판 불러오기 뒤에는 옛 인스턴스일 수 있다. 그릴 때는 `Game.Data()`·`Find`로 다시 꺼낸다.
> - 창구를 런 스크립트에 모으는 이유: 화면이 행동을 직접 만들어도 되지만, 한곳에 모아 두면 입력 목록이 한눈에 보인다.
> - `Dispose`하는 이유: 세션은 씬보다 오래 산다.
> - `Subscribe`와 `SubscribeAfterAction`: `Subscribe`는 행동 안에서 바로 받는다(값을 바꾸는 규칙용). `SubscribeAfterAction`은 바깥 행동이 성공한 뒤 행동 밖에서 쏜 순서대로 받는다(화면용). 연출이 이벤트를 모아 두거나 실패한 행동을 거르는 코드를 직접 쓰지 않는다.
>
> 값이 바뀌면 프레임 끝에 한 번 다시 그리는 `GameView.Redraw`, 데이터는 한 번에 바뀌는데 연출은 시간을 두고 보여 주는 방법은 [06장](06-Presentation.md).

## 8. 지연 효과

행동은 한 번에 끝난다. 행동 안에서 코루틴·`await`로 기다리지 않는다. "나중"은 플레이어 데이터에 적어 두고, 그때 오는 행동 안에서 돈다.

- **몇 턴 동안·몇 턴 뒤의 효과는 상태 효과다.** "턴이 끝날 때마다 화상 피해 2, 2턴"은 DB › 상태 효과에 항목(`burn`)으로 만들고 카드 효과 `상태 효과 걸기`로 건다([04장](04-AddingContent.md) 4절). `EndTurn`이 턴 끝 이벤트를 쏘는 순간, 그 행동 안에서 피해가 들어가고 남은 횟수가 준다. `EndTurn`에 화상 코드는 없다.
- **상태 없이 "나중에 이 행동"만이면 예약이다.** 행동 안에서 `Game.ScheduleOn<TurnEnded>(2, new 행동 { … })`(턴 끝을 2번 받은 순간), `Game.ScheduleAfter(1.5, new 행동 { … })`(게임 시간 1.5초 뒤).

> [!note]- 지연 효과 자세히
> - 걸린 상태 효과(`$statuses`)와 예약(`$schedule`)은 플레이어 데이터다. Undo·판 저장·멀티가 따라오고 난수도 결정론을 지킨다.
> - 같은 턴 끝이 피해와 남은 횟수 줄이기를 둘 다 하면 피해가 먼저다. "2턴"이면 피해 두 번 뒤에 끝난다.
> - 턴 끝 피해로 마지막 적이 쓰러질 수 있다. 그래서 `EndTurn`은 턴 끝을 쏜 바로 뒤에 적이 남았는지 본다(4절).
> - 이벤트 예약(`ScheduleOn`)은 그 이벤트를 쏜 행동 안에서 실행되어 함께 Undo된다. 시간 예약(`ScheduleAfter`)은 프레임 끝에 행동 하나("예약 실행")로 돌아 따로 Undo된다.
> - 예약에 넘긴 행동의 대상 필드는 id만 남겨 저장하고 실행 직전에 다시 찾는다. 그때 대상이 없으면(죽은 적) 실행하지 않고 지운다(타임라인에 "대상이 없어 예약을 실행하지 않았습니다").
> - 이벤트 예약이 실행 중에 실패하면 이벤트를 받는 쪽의 오류처럼 타임라인에 남고 바깥 행동은 계속된다. 실패할 일(모자란 비용 등)은 예약된 행동이 먼저 확인한다.
> - 코루틴·타이머에서 `GBibleRuntime.Game.Do(…)`해도 돌기는 하지만, 기다리는 중인 것은 판 저장에 들어가지 않고 Undo해도 멈추지 않는다. 그래서 상태 효과·예약에 맡긴다.
> - 카드 던전에서 시간이 걸리는 것은 연출뿐이다. 데이터는 행동 하나로 즉시 바뀌고, 화면이 이벤트를 받아 시간을 두고 보여 준다([06장](06-Presentation.md)).
> - 자세히: 매뉴얼 [29. 상태 효과·예약·선택 대기](../../Manual/29-Statuses-Schedules-Choices.md).

> [!note]- 다른 장르라면: 실시간 액션
> 입력이 프레임마다 들어오지만 규칙은 같다. 이동은 MonoBehaviour가 하고, 피격·스킬 사용·아이템 획득처럼 플레이어 데이터를 바꾸는 사건만 행동으로 보낸다. Undo 방식은 DB › 시작 파일 › Bootstrap에서 "스냅샷"을 고른다. 독·지속 피해는 상태 효과의 지속을 "게임 시간(초)"으로, "2초 뒤 폭발"은 `Game.ScheduleAfter`로 둔다. 초당 회복처럼 일정한 간격의 규칙은 Bootstrap의 **고정 틱**을 켜고 `session.Ticked`에서 행동을 `Do`한다([07장](07-GenreRecipes.md) 6절). 예제 전체는 [02. 행동과 Undo](../../Manual/02-Actions-Undo.md).

## 9. 씬 전환

세션은 씬과 상관없이 DontDestroyOnLoad의 `[MomoiProject]` 오브젝트와 함께 산다. 씬을 바꿔도 플레이어 데이터·Undo 이력·로그는 그대로다.

- 씬 사이에 값을 static으로 넘기지 않는다. 다음 씬은 `run.battle` 같은 플레이어 데이터를 읽는다.
- 띄울 씬은 데이터로 정한다: `SceneManager.LoadScene(Rules.InBattle(Game) ? "Battle" : "Map");`
- 씬 전환은 행동이 끝난 뒤 화면 쪽에서 한다. 행동 안에서 씬을 불러오지 않는다.

> [!note]- 씬 전환 자세히
> - **판을 여는 코드는 한 번만.** `RunDirector.Start`는 판을 불러오거나 새로 연다. 씬마다 `RunDirector`를 두면 씬을 옮길 때마다 저장을 다시 불러오거나 새 판을 열어 버린다. 런 스크립트는 처음 한 번 뜨는 씬(또는 씬을 옮겨도 남는 오브젝트)에 두고, 각 씬에는 그 씬의 화면 코드만 둔다.
> - **받기를 정리한다.** 씬 오브젝트가 `SubscribeAfterAction`·`GameView.Redraw`로 받은 `IDisposable`은 `OnDisable`·`OnDestroy`에서 `Dispose`한다. 하지 않으면 사라진 오브젝트를 세션이 계속 부른다.
> - 판을 불러온 뒤에도 같은 식(`Rules.InBattle`)으로 맞는 씬을 고른다.
> - 씬을 떠날 때 남은 연출은 `GameView.ClearPresentation()`으로 버린다([06장](06-Presentation.md) 4절).

> [!note]- 씬 흐름 — 데이터가 씬을 고르게(DB › Bootstrap, 코드 없이)
> DB › 시작 파일 › Bootstrap의 **씬 흐름(선택)**에서 플레이어 데이터(`run`)·필드(씬 이름이 든 글자, 예: `scene`)·빈 값일 때 씬(예: `Title`)을 고르면, 그 필드가 가리키는 씬을 화면이 따라 연다. 행동은 `run.scene = "Battle";`처럼 필드만 바꾼다.
> - 이어 하기로 판을 불러오면 그 씬이, 새 판·판 끝으로 값이 비면 빈 값일 때 씬이 열린다. Undo로 값이 돌아가면 씬도 돌아간다.
> - 연출이 끝난 뒤에 열고, 여는 동안은 연출 대기열의 한 칸이다(그동안 `GameView.Busy`). 페이드는 `GameScenes.Transition = load => Fade(load)`(`load`를 감싸는 코루틴).
> - Build Settings에 없는 씬이면 타임라인에 경고가 남는다. 코드로 켜려면 `GameScenes.Follow(키, 필드, 빈 값일 때 씬)`.
> - 카드 던전은 `RunState`에 씬 필드가 없어 위의 `SceneManager.LoadScene`을 쓴다. 맵·상점 씬이 늘면 `public string scene;`을 더하고 씬 흐름으로 옮긴다.
> - 씬에 놓인 상자·문·쓰러뜨린 보스의 상태는 월드 상태로 기억한다: 행동 안에서 `Game.SetWorld("forest.chest3", "opened")`, 씬에는 컴포넌트 '월드 상태 연결'. 매뉴얼 [32장](../../Manual/32-View-Presentation-Scenes.md).

> [!note]- 다른 장르라면: 턴을 넘기면 Undo 못 하게
> DB › 시작 파일 › Bootstrap의 **Undo 끊는 이벤트**로 턴 끝 이벤트를 고른다([07장](07-GenreRecipes.md) 2절). 카드 던전은 판 전체에서 Undo를 허락하고, 상한은 DB › 시작 파일 › Bootstrap의 Undo 보관 수(200)로 둔다.

## 10. 체크리스트

- [ ] 처음 뜨는 씬에 런 스크립트가 하나 있고, 입력마다 행동 하나를 보내는 창구가 있다.
- [ ] 판을 이어 하거나 새로 여는 코드가 게임에서 한 번만 돈다.
- [ ] 플레이어 데이터를 바꾸는 코드가 모두 행동(또는 그 안에서 불리는 Block·`[BindTo]`·규칙 함수) 안에 있다.
- [ ] 한 입력에 따라오는 단계가 Undo 한 번에 돌아가는 것을 모니터로 확인했다.
- [ ] 플레이어에게 보일 실패는 `throw Game.Fail(키, 기본 문장)`, 비용은 `Game.Pay`로 던지고, 런 스크립트는 `InvalidOperationException`만 잡는다.
- [ ] 반응할 순간마다 행동 안에서 이벤트를 쏜다.
- [ ] 화면은 읽기·보내기·받기만 하고(연출은 `SubscribeAfterAction` → `GameView.Present`, 다시 그리기는 `GameView.Redraw`), 받은 것을 `Dispose`한다.
- [ ] 몇 턴 동안·몇 턴 뒤의 효과는 상태 효과·예약에 맡기고, 행동 안에서 기다리지 않는다.
- [ ] 씬 사이에 값을 static으로 넘기지 않고, 띄울 씬을 데이터로 정한다(씬이 많으면 Bootstrap의 씬 흐름).

> [!note]- 더 보기(매뉴얼)
> - [02. 행동과 Undo](../../Manual/02-Actions-Undo.md) — `Do` 안에서 일어나는 일, 실패, Undo, 지연 효과
> - [29. 상태 효과·예약·선택 대기](../../Manual/29-Statuses-Schedules-Choices.md) — 상태 효과의 칸, `ScheduleAfter`·`ScheduleOn`
> - [28. 능력치·Modifier·행동 직전 이벤트·비용](../../Manual/28-Attributes-Intercepts.md) — `Game.Pay`와 비용 행동 직전 이벤트
> - [30. 밸런스 값·곡선·가중치 표·플레이어용 실패](../../Manual/30-Balance-Tables.md) — `Game.Fail`·`GameFailure`
> - [32. 화면 연결·연출 대기열·씬 흐름·월드 상태](../../Manual/32-View-Presentation-Scenes.md) — 씬 흐름, 월드 상태
> - [03. Game API](../../Manual/03-GameApi.md) — `Find`·`All`·`Do`·`Use`, 씬 코드에서 쓰는 법
> - [10. 이벤트](../../Manual/10-Events.md) — 쏘기·받기, `Subscribe`와 `Dispose`
> - [16. 능력과 사용 기능](../../Manual/16-Abilities-Usable.md) — `Game.Use`의 실행 흐름, `Game.CanUse`
> - [20. 시작하기](../../Manual/20-Setup.md) — `GBibleRuntime`, 판 저장·불러오기
> - [22. 모니터와 디버깅](../../Manual/22-Monitor-Debug.md) — 행동 탭, 타임라인 묶어 보기
