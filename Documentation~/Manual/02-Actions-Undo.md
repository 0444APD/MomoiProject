# 02. 행동과 Undo

**한 줄 요약**: 플레이어 입력 하나(이동·공격·구매·카드 내기·턴 넘기기)를 **행동** 클래스로 만들어 `Game.Do`로 실행한다. 행동 하나가 Undo 한 단위이자, 멀티플레이에서 한 번에 보내는 변경 한 묶음이다.

**언제 쓰는가**

- 플레이어가 버튼·클릭으로 일으키는 모든 규칙 변화
- 되돌리기(Undo)가 필요한 턴제·퍼즐·카드 게임
- "이번 턴 처음으로"·"전투 처음으로 3번"처럼 여러 행동을 한 번에 되돌리기
- 실시간 게임의 입력(되돌리기는 스냅샷 방식으로)
- 실패하면 아무것도 바뀌지 않아야 하는 처리(구매·제작)

---

## 0. 용어

| 용어 | 뜻 |
|---|---|
| 행동 (`GameAction`) | 플레이어 입력 하나를 담은 **데이터** 클래스. 메서드 호출이 아니라 데이터라서 기록·리플레이·서버 전송이 된다. 모든 행동은 `GameAction` 하나를 상속한다. |
| `Game` (`GameApi`) | 게임 코드의 창구. 행동 클래스 안에서는 `Game` 속성으로 쓴다. 자세히는 [03-GameApi.md](03-GameApi.md). |
| 참조 필드 | 행동의 필드 중 형식이 "`string id`가 있는 클래스"(또는 그 리스트·배열)인 것. 프로그래머는 객체를 넣고, 시스템은 기록·전송에 id만 남겼다가 실행 직전에 그 id로 지금 데이터에서 객체를 다시 찾아 넣는다. |
| Transaction | 행동 하나가 일으킨 변경 전체. Undo는 Transaction 단위로 되돌린다(코드 형식 `Transaction`). |
| Sync · ChangeSet | 시스템이 플레이어 데이터의 사본(Mirror)과 실제 값을 비교해 바뀐 곳을 찾는 동작이 Sync, 찾아낸 변경 묶음이 ChangeSet이다. 자세히는 [01-PlayerData.md](01-PlayerData.md). |
| Undo | 마지막 행동(스냅샷 방식이면 마지막 스냅샷)으로 되돌리는 것. `session.Undo()`. |
| 지점 | 행동 이력의 한 자리(`session.HistoryMark`). `session.UndoTo(지점)`이 그 뒤의 행동을 모두 되돌린다(2.8). |
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

### 2.1 행동 만들기: `GameAction` + `Run()`

```csharp
using System;
using GBible;

[Serializable]
public class Board
{
    public int turn;
}

[Serializable, BlockInfo("턴 넘기기")]
public class EndTurn : GameAction
{
    protected override void Run() => Game.All<Board>()[0].turn++;
}
```

```csharp
Game.Do(new EndTurn());
```

| 요소 | 하는 일 |
|---|---|
| `GameAction` | 모든 행동의 바탕 클래스. 행동은 이것 하나만 상속한다. |
| 필드 | 행동이 실어 나르는 정보(대상·수치·선택지). 대상 객체도 필드로 넣는다(2.2). |
| `protected override void Run()` | 처리 내용. 매개변수가 없다. 필요한 것은 필드와 `Game`으로 얻는다. |
| `Game` (물려받은 속성) | 지금 세션의 `GameApi`(데이터·난수·이벤트·다른 행동). **`Run`이 실행되는 동안에만** 들어 있고, 끝나면 `null`이다. |
| `[Serializable]` | 행동은 데이터다. 붙여 두어야 저장·전송·리플레이에 쓸 수 있다. |
| `[BlockInfo("이름")]` | 모니터·타임라인에 보일 이름. 없으면 클래스 이름이 쓰인다. |

등록 없이 바로 실행된다.

### 2.2 대상은 객체로: 참조 필드

행동이 다룰 대상(유닛·카드·적·아이템)은 **객체를 그대로 필드에 넣는다.** 필드 형식이 `string id`가 있는 클래스면 그 필드는 참조 필드가 된다. 개수 제한은 없고, 리스트·배열도 되고, 없으면 `null`로 둔다.

```csharp
using System;
using System.Collections.Generic;
using GBible;

[Serializable]
public class Unit
{
    public string id;
    public int x, y;
    public int hp = 30;
}

[Serializable, BlockInfo("이동")]
public class MoveUnit : GameAction
{
    public Unit unit;                                        // 참조 필드(대상)
    public int x, y;                                         // 그냥 값

    protected override void Run() { unit.x = x; unit.y = y; } // 지금 데이터 안의 객체를 그냥 고친다
}

[Serializable, BlockInfo("연쇄 번개")]
public class ChainLightning : GameAction
{
    public Unit caster;                                      // 하나
    public List<Unit> targets = new List<Unit>();            // 여럿(리스트·배열) — 실행 직전에 새 목록에 지금 객체를 담아 바꿔 넣는다(넣어 준 목록은 고치지 않는다)
    public Unit bonus;                                       // 고르지 않았으면 null
    public int damage = 5;

    protected override void Run()
    {
        foreach (var t in targets) t.hp -= damage;
        if (bonus != null) bonus.hp -= damage * 2;
    }
}
```

```csharp
Game.Do(new MoveUnit { unit = hero, x = 3, y = 4 });
Game.Do(new ChainLightning { caster = hero, targets = new List<Unit> { orc1, orc2 } });
```

**시스템이 참조 필드에 하는 일**

1. 기록(타임라인)·전송에는 객체가 아니라 그 객체의 **id만** 남는다.
2. **실행 직전**(행동 안, `Run` 바로 앞)에 그 id로 지금 플레이어 데이터에서 객체를 다시 찾아 필드에 넣는다(`Game.Find(형식, id)`와 같은 찾기). 리스트·배열은 칸마다 바꿔 넣는다. `null`인 필드·칸은 건너뛴다.

그래서 `Run` 안의 참조 필드는 언제나 **지금 데이터 안의 실제 객체**다.

| 상황 | 결과 |
|---|---|
| 싱글에서 방금 고른 객체를 넣음 | 같은 객체가 그대로 쓰인다(확인만 한다). |
| Undo·판 불러오기 뒤에 버튼이 쥐고 있던 옛 객체를 넣음 | 같은 id의 지금 객체로 바뀐다. 데이터 밖의 옛 객체(유령 객체)를 고치는 일이 없다. |
| 멀티플레이(설계) | 객체 대신 id만 건너가고, 받는 쪽이 자기 데이터의 객체를 넣는다. |
| 타임라인·리플레이 | id로 남는다. |

**실패하는 경우** — 행동 안에서 일어나므로 행동이 실패하고 아무것도 바뀌지 않는다(2.7).

- 그 id의 객체가 지금 데이터에 없다(쓰러져 리스트에서 빠짐, Undo로 사라짐): "Unit 'orc2'을(를) 찾을 수 없습니다(행동 'ChainLightning'의 'targets')".
- id가 빈 객체를 넣었다(방금 `new`로 만든 객체): "… id가 비어 있는 Unit입니다. 데이터 안의 객체를 넣으세요(새 객체는 행동 안에서 만든다)." 새 객체는 행동 안에서 만들고 id는 `Game.NewId(접두어)`로 정한다.

**참조가 되는 것과 아닌 것**

| 필드 형식 | 다루는 법 |
|---|---|
| `string id`가 있는 클래스, 그 `List<>`·배열 | 참조. id로 기록하고 실행 직전에 다시 찾는다. |
| 숫자·글자·`bool`·`enum` | 값 그대로. |
| `id`가 없는 클래스 | 값으로 다룬다(내용이 그대로 기록·전송되고, 다시 찾지 않는다). |
| DB 원본(카드 원본·인스턴스 등)을 가리킬 때 | 객체가 아니라 `[DataRef(typeof(DB 종류))] public string` 필드. 원본은 플레이어 데이터가 아니라서 찾기 대상이 아니다. |

**대상 클래스의 조건**

- `public string id;` 필드(또는 `Id` 속성)가 있다.
- 플레이어 데이터 안에 객체로 들어 있다(깊이는 상관없다. 리스트 안의 리스트도 된다).
- id는 **플레이어 데이터 전체에서 하나**다. 형식이 달라도 겹치지 않게 둔다. `Game.Use`의 사용자·대상, 상태 효과의 가진 쪽, Modifier의 대상, 선택 대기의 후보는 형식 없이 id로 찾는다(`Game.Find(id)`). 형식이 다른 두 객체가 같은 id면 먼저 찾은 것(플레이어 데이터 키 이름순)이 쓰인다. 예: 프로필의 동료 `lea`와 전투에 넣은 사본 `lea` → `Game.Use(skill, "lea")`가 프로필의 동료를 잡는다. 사본·새 객체의 id는 `Game.NewId(접두어)`로 만든다.

Unity 객체(`MonoBehaviour`·`ScriptableObject`)·델리게이트·플레이어 데이터 밖의 객체는 대상으로 넣지 않는다.

### 2.3 행동 로그 문장: `Describe()`

```csharp
using System;
using GBible;

[Serializable, BlockInfo("회복")]
public class Heal : GameAction
{
    public Unit unit;
    public int amount = 10;
    protected override void Run() => unit.hp += amount;
    public override string Describe() => $"{unit?.id} 회복 +{amount}";
}
```

모니터의 행동 탭·타임라인에 보일 문장이다. 쓰지 않으면 "`[BlockInfo]` 이름(없으면 클래스 이름) (참조 필드의 id들)"이다. 예: `연쇄 번개 (hero, orc1, orc2)`, 참조 필드가 없거나 모두 `null`이면 `턴 넘기기`.

`Describe`는 참조 필드를 다시 찾기 **전**에도 불린다. 참조 필드에서는 id만 읽고, 체력 같은 값은 읽지 않는다(옛 객체의 값일 수 있다).

### 2.4 공통 멤버: `actorId` · `tick` · `utc` · `Visibility`

```csharp
[Serializable, BlockInfo("상자 열기")]
public class OpenChest : GameAction
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
| `utc` (`long`) | 현실 시각(1970-01-01부터 UTC 밀리초). 바깥 행동이 시작할 때 시스템이 한 번 적고, 안쪽 행동은 같은 값이다. 이미 값이 있으면(멀티플레이로 받은 행동·다시 돌리는 행동) 그대로 둔다. 규칙에서는 `Game.Now`로 읽는다([07-Random-Time.md](07-Random-Time.md) 2.11). |
| `Visibility` | `Public`(모두) · `Private`(본인만) · `Developer`(개발자 모니터만). 지금은 모니터 행동 탭에 표시된다. |

`utc`가 이미 있으면 그대로 쓰므로, 같은 행동 객체를 다시 `Do`하면 처음 시각이 남는다. 행동은 `Do`할 때마다 `new`로 만든다.

### 2.5 처리기를 따로 등록하는 행동: `Actions.Register`

처리 코드를 행동 클래스 밖(세션 준비 코드)에 두는 방식이다. 행동 클래스는 데이터만 갖고 `Run`을 쓰지 않는다.

```csharp
using System;
using GBible;
using UnityEngine;

[Serializable, BlockInfo("공격")]
public sealed class AttackUnit : GameAction
{
    public Unit target;                                      // 참조 필드는 여기서도 똑같이 다룬다
}

public static class CombatSetup
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void Hook() => GBibleRuntime.Started += session =>
    {
        session.Data.Define("battle.combo", DataZone.Session, 0, "콤보");
        session.Actions.Register<AttackUnit>(action =>
        {
            // action.target은 이미 지금 데이터의 객체로 바뀌어 있다
            var hit = session.Random.Get("combat").Chance(0.7);
            if (hit) action.target.hp -= 5;
            session.Data.Set("battle.combo", hit ? session.Data.GetNumber("battle.combo") + 1 : 0);
        });
    };
}
```

- 처리기에도 실행 직전에 참조 필드를 다시 찾아 넣은 행동이 넘어온다(못 찾으면 처리기를 부르기 전에 실패).
- 처리기 안에서는 행동의 `Game` 속성이 비어 있다. `session.Game`을 쓴다.
- 한 형식에 처리기는 하나다. 두 번 등록하면 "이미 등록되어 있습니다" 예외다.
- 처리기가 등록되어 있으면 클래스 안의 `Run` 대신 처리기가 실행된다.
- `Run`도 처리기도 없으면 `Do`가 "행동 'X'의 처리기가 없습니다. 행동 클래스에 Run을 쓰거나 Actions.Register로 처리기를 등록하세요."로 실패한다.

### 2.6 실행: `Game.Do` / `session.Do`

```csharp
ActionRecord record = Game.Do(new MoveUnit { unit = hero, x = 1, y = 0 });
// record.Status: ActionStatus.Done / Undone / Failed
// record.Action, record.JournalId, record.TransactionId, record.Error
```

`Game.Do(action)`와 `session.Do(action)`는 같다. 행동은 실행되는 즉시 끝난다(기다리거나 확정하는 단계가 없다).

`Do` 안에서 시스템이 하는 일:

1. 처리 방법을 찾는다(등록한 처리기 → 클래스 안의 `Run`).
2. `tick`·`utc`를 적고 기록(Journal)에 행동 한 줄을 남긴다.
3. **행동 시작**: 지금까지의 행동 밖 변경을 먼저 Sync해 따로 떼어 두고, Transaction을 연다. 모든 난수 스트림의 상태를 보관한다.
4. 참조 필드를 id로 지금 데이터에서 다시 찾아 넣는다(못 찾으면 여기서 실패).
5. `Run`(또는 처리기)을 실행한다. 이 안에서 `Game.Find`·`Game.All`이 중간에 Sync해도 모두 같은 Transaction에 들어간다.
6. **행동 끝**: 모든 플레이어 데이터를 Sync해 Transaction을 닫는다. 행동 동안 모아 둔 ChangeSet을 차례대로 `Synced` 이벤트로 쏜다(한 행동 = 한 묶음). 되돌리기 방식이 행동 단위면 Undo 이력에 넣는다.
7. 바뀐 값을 보는 조건(도전과제·트리거)을 다시 평가한다.

값이 하나도 바뀌지 않은 행동(빗나간 공격 등)도 Undo 한 단위로 남는다. 난수 소비를 되돌려야 하기 때문이다.

### 2.7 실패: 예외가 나면 전부 취소

```csharp
[Serializable] public class Wallet { public string id; public int gold; }

[Serializable, BlockInfo("사기")]
public class Buy : GameAction
{
    public Wallet wallet;
    public int price;
    protected override void Run()
    {
        wallet.gold -= price;                                                // 먼저 깎아도
        if (wallet.gold < 0) throw new InvalidOperationException("골드 부족"); // 예외면 깎은 것까지 되돌아간다
    }
}
```

행동 안에서 예외가 나면(참조 필드를 찾지 못한 경우도, 행동 안에서 쏜 이벤트를 받는 `Subscribe`·트리거 결과가 던진 경우도 같다. [10-Events.md](10-Events.md) 2-7):

- 그 행동이 바꾼 플레이어 데이터를 모두 되돌린다(Transaction 단위 원자성).
- 난수 스트림도 행동 전 상태로 돌린다.
- 행동 기록의 상태는 `Failed`, 기록에 "행동 실패, 변경 취소" 오류가 남는다.
- Undo 이력에 들어가지 않는다.
- 예외는 `Do`를 부른 쪽으로 **다시 던져진다**. 예상되는 실패라면 부른 쪽에서 잡는다.

```csharp
try { Game.Do(new Buy { wallet = myWallet, price = 50 }); }
catch (InvalidOperationException e) { /* "골드 부족" 안내 */ }
```

**행동 안의 저장점: `Data.Mark()` · `Data.RollbackTo(point)`** — 행동 전체가 아니라 그 안의 한 부분만 실패 처리할 때 쓴다. `Mark()`는 지금까지의 변경을 굳혀 두고 저장점(`Savepoint`)을 돌려준다. `RollbackTo(저장점)`은 그 저장점 **뒤의 변경만** 되돌린다. 그 앞의 변경은 남고 행동은 계속되며, 되돌린 변경은 그 행동의 Undo 기록에서도 빠진다. 이벤트 예약이 실패할 때 그 예약이 바꾼 것만 되돌리는 것도 이 방식이다(`29-Statuses-Schedules-Choices.md` 2-5).

```csharp
// 행동의 Run 안
var data = Game.Session.Data;
var point = data.Mark();
try { GiveBonus(); }                                                  // 실패할 수 있는 덤(예외를 던질 수 있다)
catch (InvalidOperationException) { data.RollbackTo(point); }         // 덤이 바꾼 것만 되돌리고 행동은 계속
```

- 행동 밖에서는 `Mark()`가 `null`이다. `RollbackTo`는 `null`이나 다른 행동의 저장점을 받으면 아무것도 하지 않는다.
- `Mark()`는 그 순간 Sync를 한 번 한다(모든 플레이어 데이터 비교). 반복문 안처럼 자주 부르지 않는다.
- 되돌리는 것은 플레이어 데이터뿐이다. 난수 스트림은 되돌리지 않는다.

### 2.8 되돌리기: `session.Undo()`

```csharp
bool undone = Game.Session.Undo();   // 되돌릴 것이 없으면 false
int left = Game.Session.UndoCount;   // 되돌릴 수 있는 횟수
```

- 행동 단위 방식: 마지막 행동의 Transaction을 거꾸로 적용한다. 리스트에서 빠졌던 객체·갈아 끼워진 객체는 **원래 인스턴스**로 돌아온다(게임 코드가 쥔 참조가 이어진다). 몇 번이든 뒤에서부터 계속 되돌릴 수 있다.
- 되돌린 내용도 기록에 남는다. 클래스 데이터(Mirror로 비교하는 데이터)의 되돌린 내용은 다시 Sync되어 `Synced` 이벤트로도 나간다(받는 쪽도 같이 되돌아간다). 되돌린 행동의 상태는 `Undone`.
- **난수도 함께 되돌아간다.** Undo 후 같은 행동을 다시 하면 같은 결과가 나온다(되돌리기로 좋은 결과를 뽑는 것을 막는다).
- **도전과제 달성과 보상은 되돌리지 않는다.** 행동이 바꾼 값(주운 골드 등)만 돌아가므로, Undo하고 다시 해도 보상이 두 번 나오지 않고 값이 쌓이지 않는다.
- **시간 규칙이 낸 행동은 앞 행동과 함께 되돌린다.** 마지막 행동이 상태 효과 주기·만료, 시간 예약 실행, 패시브 주기면 Undo 한 번이 그 앞의 행동까지 되돌리고, 게임 시계도 그 행동이 시작한 때로 돌린다. 그 행동만 되돌리면 시각이 이미 지난 값이 돌아와 다음 프레임에 그대로 다시 실행되기 때문이다. 되돌린 사이의 시간 규칙은 시간이 다시 흐르면 다시 실행된다.
- 행동 실행 중에는 Undo할 수 없다(예외).

**지점까지 되돌리기: `session.HistoryMark` · `session.UndoTo(mark)`** — "이번 턴 처음으로"처럼 여러 행동을 한 번에 되돌린다. 되돌릴 자리에서 지점을 받아 두고, 나중에 그 지점 뒤를 모두 되돌린다.

```csharp
long turnStart = Game.Session.HistoryMark;     // 내 턴이 시작될 때(턴 시작 행동이 끝난 뒤)
// … 이동·공격 …
int undone = Game.Session.UndoTo(turnStart);   // 그 뒤의 행동을 모두 되돌린다. 되돌린 횟수
```

| 멤버 | 뜻 |
|---|---|
| `HistoryMark` (`long`) | 지금까지의 행동 이력 끝. 행동 단위 방식이면 마지막 행동의 Transaction 번호(이력이 비면 0), 스냅샷 방식이면 지금의 스냅샷 수, 되돌리기 없음이면 0 |
| `UndoTo(mark)` (`int`) | 지점 뒤의 행동(스냅샷)을 뒤에서부터 하나씩 `Undo()`한다. 되돌린 횟수를 돌려준다. 행동 안에서 부르면 예외 |

- Undo 한 번 한 번과 같다. 난수도 돌아가고, 도전과제 달성·보상은 남는다. 기록에는 Undo가 되돌린 횟수만큼 남는다.
- 시간 규칙 행동을 앞 행동과 묶어 되돌릴 때도 지점 앞으로는 가지 않는다.
- 지점이 Undo 보관 수(`HistoryLimit`) 밖으로 밀려났으면 남은 만큼만 되돌린다. 돌려받은 횟수로 확인한다.
- **지점은 이 세션 안에서만 뜻이 있다.** 판 저장에는 Undo 이력이 없다. 판을 불러오면(`GBibleRuntime.RunLoaded`) 지점을 다시 받는다.
- 되감기 횟수 같은 규칙은 되돌린 **뒤에** 행동으로 센다. 되돌리기 전에 센 값(지점 뒤의 행동이 바꾼 값)은 되돌리면 함께 돌아간다(4.6).
- 앞 지점으로 되돌린 뒤에는 그보다 뒤에 받아 둔 지점을 다시 받는다. 번호는 계속 오르므로, 옛 지점으로 되돌리면 되돌린 뒤의 새 행동까지 되돌아간다.
- 스냅샷 방식의 지점은 스냅샷 수라서, 보관 수가 차 오래된 스냅샷이 밀려나기 시작하면 받아 둔 시점과 어긋난다. 지점은 주로 행동 단위 방식에서 쓴다.

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
| `HistoryLimit` | `0`(세션을 직접 만들 때). `GBibleRuntime`은 DB › Bootstrap의 **Undo 보관 수**를 넣고, 새 DB의 기본은 `200` | 행동 단위 방식에서 Undo할 수 있는 최대 행동 수. 0은 무제한. 넘으면 가장 오래된 행동부터 Undo할 수 없게 된다 |
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
- 스냅샷은 저장 파일과 같은 모습(저장 트리)으로 찍되 메모리 안에만 둔다. 그래서 DB 항목이 아닌 에셋 참조(스프라이트 등)도 되감은 뒤 그대로다.

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
- 행동이 바꾼 리스트의 **구조**(더하기·빼기·순서 바꾸기)가 그 뒤 행동 밖에서 바뀌었으면 그 행동은 Undo하지 않는다(`Undo()`가 `false`). 되돌리면 번호가 밀려 엉뚱한 요소가 바뀌기 때문이다. 기록에 경고가 남는다: "Undo '행동'를 할 수 없습니다: 그 뒤 행동 밖에서 리스트 구조가 바뀌었습니다(…). … 리스트에 더하기·빼기·순서 바꾸기는 행동 안에서 하세요."
- Undo가 도중에 실패하면(예상하지 못한 구조 변화) 예외를 던지지 않는다. 경고 "Undo '행동'를 끝까지 되돌리지 못해 일부만 되돌렸습니다: …"를 남기고 지금 모습으로 다시 맞춘다.
- 행동 안에서 다른 행동을 `Game.Do`하거나 `Game.Use`하면, 그 행동은 **바깥 행동의 일부로 그 자리에서** 실행된다(새 Undo 단위를 만들지 않는다). 안쪽 행동의 참조 필드도 실행 직전에 다시 찾는다. Undo·변경 묶음은 바깥 행동 하나이고, 안쪽에서 실패하면 바깥 행동 전체가 취소된다. 예: 카드 내기 = 에너지 지불 + 손패 이동 + `Game.Use(카드)` + 다 쓰러졌으면 `Game.Do(new WinBattle())`.

---

## 3. 나중에 일어나는 효과 (람다·코루틴)

> 게임 규칙의 지연·지속 효과(독, "3턴 뒤 폭발", "2초 뒤 폭발")는 직접 만들지 않아도 된다. 상태 효과와 예약(`Game.ScheduleAfter`·`ScheduleOn`)이 저장·Undo·대상 다시 찾기까지 맡는다(→ `29-Statuses-Schedules-Choices.md`). 이 절은 그 밖의 나중 코드(코루틴·연출 콜백)와 그 원리다.

코루틴, 타이머, 트윈 완료 콜백처럼 **나중에 실행되는 코드는 어떤 행동의 바깥에서 돈다.** 행동의 `Run` 안에서 만든 람다라도 실제로 실행되는 시점에는 그 행동이 이미 끝나 있다.

그 결과:

1. 람다·코루틴이 고친 값은 기록되지만 **행동 밖 변경**이다. Undo되지 않는다.
2. 행동 클래스의 `Game` 속성은 `Run`이 끝나면 `null`이다. 나중에 쓰려면 `GBibleRuntime.Game`을 쓰거나 `Run` 안에서 지역 변수로 받아 둔다.
3. 시작한 행동을 Undo해도 코루틴은 멈추지 않는다.
4. 그 사이에 대상이 Undo·사망·불러오기로 사라졌을 수 있다.

**되돌릴 수 있게 하려면 람다·코루틴 안에서 `Game.Do(...)`로 행동을 실행한다.** 그러면 지연 효과 한 번 한 번이 각각 Undo 한 단위가 된다. 람다가 쥔 객체가 옛 객체여도 행동의 참조 필드는 실행 직전에 지금 객체로 바뀐다. 다만 대상이 아예 사라졌으면 행동이 실패하므로, 실행하기 전에 `Find`로 아직 있는지 확인한다. 남은 횟수 같은 진행 상태는 플레이어 데이터에 두면 Undo·저장이 함께 따라온다.

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

[Serializable, BlockInfo("독 걸기")]
public class ApplyPoison : GameAction
{
    public PoisonedUnit unit;
    public int ticks = 3;
    protected override void Run() => unit.poisonTicks = ticks;
}

[Serializable, BlockInfo("독 피해")]
public class PoisonTick : GameAction
{
    public PoisonedUnit unit;
    public int damage = 2;
    protected override void Run()
    {
        if (unit.poisonTicks <= 0) return;
        unit.hp -= damage;
        unit.poisonTicks--;
    }
}

public class PoisonDriver : MonoBehaviour
{
    public void Poison(PoisonedUnit unit)
    {
        GBibleRuntime.Game.Do(new ApplyPoison { unit = unit, ticks = 3 });
        StartCoroutine(Tick(unit.id));
    }

    IEnumerator Tick(string unitId)
    {
        while (true)
        {
            yield return new WaitForSeconds(1f);                  // 게임 시간(일시정지·배속을 따른다)
            var game = GBibleRuntime.Game;
            var unit = game?.Find<PoisonedUnit>(unitId);          // 지금 값(poisonTicks)을 보려면 지금 객체로
            if (unit == null || unit.poisonTicks <= 0) yield break;   // Undo·사망으로 사라졌거나 끝났으면 멈춘다
            game.Do(new PoisonTick { unit = unit });              // 한 번 한 번이 Undo 한 단위
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

[Serializable, BlockInfo("폭발")]
public class Blast : GameAction
{
    public PoisonedUnit unit;
    public int damage;
    protected override void Run() => unit.hp -= damage;
}

[Serializable, BlockInfo("지연 폭발")]
public class DelayedBlast : GameAction
{
    public PoisonedUnit unit;

    protected override void Run()
    {
        var game = Game;   // Run이 끝나면 Game 속성은 null이 되므로 지역 변수로 받아 둔다
        var u = unit;

        // 나쁨: 2초 뒤 직접 고침 → 행동 밖 변경, Undo 안 됨. 그 사이 Undo·불러오기가 있었으면 u는 데이터 밖의 옛 객체일 수도 있다.
        DelayQueue.Instance.After(2f, () => u.hp -= 10);

        // 좋음: 2초 뒤 행동으로 실행 → 그 효과가 Undo 한 단위. u가 옛 객체여도 실행 직전에 같은 id의 지금 객체로 바뀐다.
        DelayQueue.Instance.After(2f, () =>
        {
            if (game.Find<PoisonedUnit>(u.id) != null) game.Do(new Blast { unit = u, damage = 10 });
        });
    }
}
```

참조 필드의 대상을 찾지 못하면 행동이 예외로 실패하므로, 지연 실행에서는 `Find`로 먼저 확인하거나 `Do`를 `try`로 감싼다.

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

[Serializable, BlockInfo("이동")]
public class Move : GameAction
{
    public TacticsUnit unit;
    public int x, y;
    protected override void Run()
    {
        if (unit.acted) throw new InvalidOperationException("이미 행동했습니다.");
        unit.x = x;
        unit.y = y;
        unit.acted = true;
    }
}

[Serializable, BlockInfo("공격")]
public class Attack : GameAction
{
    public TacticsUnit attacker;
    public TacticsUnit target;
    protected override void Run()
    {
        var damage = 8 + Game.Random("damage").Range(0, 5);     // 결정론 난수: Undo하면 함께 되돌아간다
        target.hp -= damage;
        attacker.acted = true;
        if (target.hp <= 0) Game.All<Battlefield>()[0].units.Remove(target);
    }
}

[Serializable, BlockInfo("턴 넘기기")]
public class NextTurn : GameAction
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
    TacticsUnit _selected;                               // 행동에 넣을 객체는 그대로 쥐어도 된다(실행 직전에 지금 객체로 바뀐다)
    GameApi Game => GBibleRuntime.Game;

    public void OnClickUnit(TacticsUnit u) => _selected = u;
    public void OnClickTile(int x, int y) => Game.Do(new Move { unit = _selected, x = x, y = y });
    public void OnClickEnemy(TacticsUnit enemy) => Game.Do(new Attack { attacker = _selected, target = enemy });
    public void OnClickEndTurn() => Game.Do(new NextTurn());
    public void OnClickUndo() => Game.Session.Undo();
}
```

Undo하면 쓰러져 리스트에서 빠졌던 적이 같은 객체로 제자리에 돌아오고, 피해 난수도 되돌아가 다시 공격하면 같은 피해가 나온다. 타임라인에는 `공격 (hero, orc1)`처럼 id로 남는다.

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

[Serializable, BlockInfo("카드 내기")]
public class PlayCard : GameAction
{
    public string card;          // 손패는 카드 이름 글자 목록이므로 그냥 값
    public int cost;
    protected override void Run()
    {
        var duel = Game.All<Duel>()[0];
        if (duel.mana < cost) throw new InvalidOperationException("마나 부족");
        if (!duel.hand.Remove(card)) throw new InvalidOperationException("손에 없는 카드");
        duel.mana -= cost;
        duel.field.Add(card);
    }
    public override string Describe() => $"카드 내기 {card}";
}

[Serializable, BlockInfo("턴 넘기기")]
public class PassTurn : GameAction
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
        game.Session.ClearUndo();   // 이 시점 이전은 Undo할 수 없다(이벤트로 정해지면 DB › Bootstrap의 Undo 끊는 이벤트로 코드 없이)
    }
}
```

`card`는 참조 필드가 아니라 글자라서 기본 `Describe`에는 보이지 않는다. 보이게 하려고 `Describe`를 덮어썼다.

### 4.3 RPG 상점: 조건이 맞지 않으면 아무것도 바꾸지 않기

```csharp
using System;
using System.Collections.Generic;
using GBible;

[Serializable]
public class Shop
{
    public string id = "shop";
    public int gold = 100;
    public Dictionary<string, int> stock = new Dictionary<string, int> { ["potion"] = 3 };
    public List<string> bag = new List<string>();
}

[Serializable, BlockInfo("구매")]
public class Purchase : GameAction
{
    public Shop shop;                                         // 플레이어 데이터 안의 객체: 참조 필드
    [DataRef(typeof(InstanceData))] public string item;       // DB 원본(인스턴스): 글자 ID
    public int price;

    protected override void Run()
    {
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

상점(`Shop`)은 플레이어 데이터라서 객체로 넣고, 팔 아이템은 DB 원본이라서 `[DataRef]` 글자로 넣는다. 원본 객체를 참조 필드에 넣으면 플레이어 데이터에 없으므로 "찾을 수 없습니다"로 실패한다.

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

[Serializable, BlockInfo("파이어볼")]
public class CastFireball : GameAction
{
    public const double Cooldown = 3.0;
    public Caster caster;

    protected override void Run()
    {
        var now = Game.Session.Clock.Time;
        if (now < caster.fireballReadyAt) throw new InvalidOperationException("쿨다운 중");
        if (caster.mp < 10) throw new InvalidOperationException("MP 부족");
        caster.mp -= 10;
        caster.fireballReadyAt = now + Cooldown;
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

쿨다운을 `Time.time`이 아니라 게임 시계(`session.Clock.Time`)로 적어 두면, 스냅샷을 되감을 때 시계와 `fireballReadyAt`이 함께 돌아가 쿨다운이 어긋나지 않는다. 남은 쿨다운 표시는 `caster.fireballReadyAt - Game.Session.Clock.Time`.

되감기·불러오기 뒤에는 씬 코드가 쥔 `Caster`가 데이터 밖의 옛 객체일 수 있다. 행동에 넣는 것은 그대로 괜찮지만(실행 직전에 같은 id의 지금 객체로 바뀐다), 화면에 값을 그릴 때는 `Game.Find<Caster>(id)`로 지금 객체를 읽는다.

### 4.5 행동 안에서 되돌리지 않을 기록 남기기

```csharp
using System;
using GBible;

[Serializable, BlockInfo("보스 방 입장")]
public class EnterBossRoom : GameAction
{
    protected override void Run()
    {
        var data = Game.Session.Data;
        data.Set("dungeon.floor", 10.0);                     // Undo 대상
        using (data.Permanent())
            data.Set("codex.bossSeen", true);                 // 한 번 본 보스 DB는 되돌리지 않는다
    }
}
```

두 키 모두 정의되어 있어야 한다(DB 또는 `Define`).

### 4.6 전술: "이번 턴 처음으로"와 "전투 처음으로 3번"

```csharp
using System;
using GBible;
using UnityEngine;

[GameEvent("턴 시작")]
public class TurnStarted { public int turn; }

[Serializable]
public class Skirmish
{
    public int turn;
    public int rewindsLeft = 3;           // 이번 전투에서 남은 되감기
}

[Serializable, BlockInfo("턴 시작")]
public class BeginTurn : GameAction
{
    protected override void Run()
    {
        var battle = Game.All<Skirmish>()[0];
        battle.turn++;
        Game.Events.Publish(new TurnStarted { turn = battle.turn });
    }
}

[Serializable, BlockInfo("되감기 쓰기")]
public class SpendRewind : GameAction
{
    protected override void Run()
    {
        var battle = Game.All<Skirmish>()[0];
        if (battle.rewindsLeft <= 0) throw Game.Fail("fail.noRewind", "되감기를 다 썼습니다.");
        battle.rewindsLeft--;
    }
}

public class RewindButtons : MonoBehaviour
{
    GameApi Game => GBibleRuntime.Game;
    long _turnStart, _battleStart;
    IDisposable _sub;

    void OnEnable()
    {
        _sub = Game.Events.SubscribeAfterAction<TurnStarted>(e =>
        {
            _turnStart = Game.Session.HistoryMark;            // 턴 시작 행동이 끝난 뒤
            if (e.turn == 1) _battleStart = _turnStart;       // 첫 턴 시작 = 전투 처음
        });
        GBibleRuntime.RunLoaded += OnRunLoaded;
    }

    void OnDisable()
    {
        _sub?.Dispose();
        GBibleRuntime.RunLoaded -= OnRunLoaded;
    }

    void OnRunLoaded(GameSession session) => _turnStart = _battleStart = session.HistoryMark;   // 불러온 판에는 Undo 이력이 없다

    public void OnClickTurnStart() => Game.Session.UndoTo(_turnStart);

    public void OnClickBattleStart()
    {
        if (Game.All<Skirmish>()[0].rewindsLeft <= 0) return;
        Game.Session.UndoTo(_battleStart);                    // 전투 처음 모습으로
        Game.Do(new SpendRewind());                           // 횟수는 되돌린 뒤의 행동으로 센다
        _turnStart = _battleStart = Game.Session.HistoryMark; // 지점을 '횟수를 쓴 뒤'로 옮긴다
    }
}
```

- `SubscribeAfterAction`은 턴 시작 행동이 성공해 이력에 들어간 뒤에 불리므로, 그때 받은 지점은 "턴 시작 행동까지 한 모습"이다.
- 되감은 뒤 지점을 `SpendRewind` 뒤로 옮겨야, 다음 되감기가 줄인 횟수를 되살리지 않는다. 턴 지점도 함께 옮긴다(옛 턴 지점으로 되돌리면 `SpendRewind`까지 되돌아간다).
- 턴 안에서만 되돌리게 하려면 DB › Bootstrap의 **Undo 끊는 이벤트**를 쓴다(4.2). 전투 처음으로 가려면 전투 동안 이력이 남아야 하므로 이 예에서는 비워 두고, 보관 수(`HistoryLimit`)는 한 전투의 행동 수보다 크게 둔다.

---

## 5. 주의할 점 · 자주 하는 실수

| 증상 | 원인과 해결 |
|---|---|
| Undo해도 값이 안 돌아옴 | 행동 밖에서 고쳤다(씬 코드·코루틴·이벤트를 받는 코드). 행동으로 실행한다. `Permanent()` 안이었거나, `HistoryLimit`을 넘었거나, 방식이 `None`일 수도 있다. |
| 지연 효과에서 `NullReferenceException` | 행동 클래스의 `Game` 속성은 `Run`이 끝나면 `null`이다. `GBibleRuntime.Game`을 쓰거나 지역 변수로 받아 둔다. |
| "행동 'X'의 처리기가 없습니다" | 행동 클래스에 `protected override void Run()`을 쓰지 않았고 `Actions.Register`도 하지 않았다. |
| "X 'id'을(를) 찾을 수 없습니다(행동 'A'의 '필드')" | 참조 필드에 넣은 객체의 id가 지금 데이터에 없다(쓰러져 빠짐·Undo로 사라짐). 지연 실행이면 `Find`로 먼저 확인한다. DB 원본 객체를 넣었다면 `[DataRef]` 글자 필드로 바꾼다. |
| "… id가 비어 있는 X입니다" | 방금 `new`로 만든 객체(데이터 밖)를 넣었다. 새 객체는 행동 안에서 만들고 id는 `Game.NewId(접두어)`로 정한다. |
| Undo·불러오기 뒤 행동이 화면에 안 보이는 옛 객체를 고침 | 대상 클래스에 `public string id;`가 없어 참조가 아니라 값으로 다뤄졌다. id를 더한다. |
| 엉뚱한 객체가 고쳐짐 | 플레이어 데이터 안에서 id가 겹친다. 형식이 달라도 겹치면 안 된다(사용·상태 효과·Modifier·선택은 형식 없이 id로 찾는다, 2.2). 사본·새 객체의 id는 `Game.NewId`로 만든다. |
| `UndoTo`가 지점까지 가지 못함 | 지점이 Undo 보관 수(`HistoryLimit`, 새 DB 기본 200) 밖으로 밀려났거나, Undo 끊는 이벤트가 이력을 비웠다. 보관 수를 늘리거나 끊는 이벤트를 다시 고른다. |
| 판을 불러온 뒤 `UndoTo`가 엉뚱하게 되돌림 | 불러오기 전에 받은 지점을 썼다. Undo 이력은 저장되지 않는다. `GBibleRuntime.RunLoaded`에서 지점을 다시 받는다. |
| 되감기 횟수가 되감으면 되살아남 | 되돌리기 전에 횟수를 줄였거나, 줄인 뒤 지점을 옮기지 않았다. 되돌린 뒤의 행동으로 줄이고 지점을 그 뒤로 옮긴다(4.6). |
| `Game.Now`가 옛 시각 | 같은 행동 객체를 다시 `Do`했다. `utc`는 이미 값이 있으면 그대로다. `Do`할 때마다 `new`로 만든다(2.4). |
| `Describe`에 이상한 값이 보임 | `Describe`는 참조 필드를 다시 찾기 전에도 불린다. 참조 필드에서는 id만 읽는다. |
| Unity 객체·델리게이트를 필드에 넣음 | 행동은 데이터라서 기록·전송·리플레이가 안 된다. 플레이어 데이터 안의 객체, DB 원본은 `[DataRef]` 글자, 그 밖은 값으로 넣는다. |
| Undo 뒤 "행동 밖에서 바뀐 값을 덮었습니다" 경고 | 행동이 바꾼 곳을 행동 밖에서 다시 고쳤다. 그 변경도 행동으로 만든다. |
| Undo가 `false`이고 "리스트 구조가 바뀌었습니다" 경고 | 행동이 바꾼 리스트에 행동 밖에서 더하기·빼기·순서 바꾸기를 했다. 그 변경도 행동으로 만든다(2.12). |
| Undo 한 번에 행동 둘이 되돌아감 | 마지막 행동이 시간 규칙(상태 효과 주기·만료, 시간 예약, 패시브 주기)이라 앞 행동과 묶였다(2.8). 정상이다. |
| 행동 안에서 기다리기(코루틴·`await`) | 행동은 한 번에 끝나야 한다. 연출은 끝난 결과(이벤트·기록)를 보고 따로 재생하고, 시간이 걸리는 효과는 3장처럼 나눈다. |
| Undo로 좋은 난수를 다시 뽑으려 함 | Undo하면 난수 상태도 돌아가 같은 행동은 같은 결과를 낸다. |
| 실패한 행동의 예외가 게임을 멈춤 | `Do`는 행동이 실패하면 변경을 취소한 뒤 예외를 다시 던진다. 예상되는 실패는 부르는 쪽에서 잡는다. |
| 행동 본문은 맞는데 행동이 실패함 | 행동 안에서 쏜 이벤트를 받는 쪽(`Subscribe`·트리거 결과)이 예외를 던졌다. 그 행동 전체가 취소된다. 모니터 행동 탭의 실패 줄에 마우스를 올려 오류를 본다([10-Events.md](10-Events.md) 2-7). |
