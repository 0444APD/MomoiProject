# 03. Game API

**한 줄 요약**: `Game`(`GameApi`)은 게임 코드가 시스템을 쓰는 창구다. 플레이어 데이터 안의 객체를 찾고(`Find`·`All`), 행동을 실행하고(`Do`), 능력·아이템을 쓰고(`Use`, 쓸 수 있을 때만 `TryUse`), 결정론 난수(`Random`)·고칠 수 있는 확률(`Chance`)과 새 id(`NewId`·`NewIds`)를 얻는다. 현실 시각·상태 효과·밸런스 값처럼 다른 장에서 다루는 멤버는 2.16에 모았다.

**언제 쓰는가**

- 씬 코드가 화면을 그리려고 유닛·아이템을 모을 때
- 들고 있던 id로 객체를 다시 찾을 때
- 버튼 입력을 행동으로 실행할 때
- 행동·효과·조건 Block 안에서 다른 객체를 찾거나 난수를 쓸 때

---

## 0. 용어

| 용어 | 뜻 |
|---|---|
| `GameApi` | 세션 하나에 붙은 창구. 세션마다 하나(`session.Game`). |
| 세션 (`GameSession`) | 게임 한 판의 실행 전체. `Game.Session`으로 닿는다. |
| 플레이어 데이터 | 세션의 저장소(`session.Data`)에 ID별로 담긴 값. 자세히는 [01-PlayerData.md](01-PlayerData.md). |
| Mirror · Sync | 시스템이 플레이어 데이터마다 들고 있는 사본이 Mirror, 실제 값과 사본을 비교해 바뀐 곳을 기록하고 사본을 맞추는 동작이 Sync다. `Find`·`All`은 Sync한 뒤 사본을 따라가며 찾는다. |
| Block | 에디터에서 골라 조립하는 로직 단위(조건·효과·대상·보상). Block 클래스 안에서도 `Game` 속성을 쓴다. |
| 행동 (`GameAction`) | 플레이어 입력 하나. Undo 한 단위. 자세히는 [02-Actions-Undo.md](02-Actions-Undo.md). |

---

## 1. 에디터에서 보기

`Game`은 코드 전용이다. 결과는 **MomoiProject › 모니터**에서 본다: **데이터** 탭(찾는 대상인 플레이어 데이터), **행동** 탭(`Do`·`Use`로 실행한 행동), **타임라인** 탭(바뀐 값과 원인).

---

## 2. 코드로 쓰는 법 — 문법 요소

### 2.1 `Game`을 얻는 곳

| 위치 | 쓰는 법 |
|---|---|
| 행동(`GameAction`)·조건(`SimpleCondition`)·효과·대상 Block 안 | 물려받은 `Game` 속성. 그 코드가 실행되는 동안만 들어 있다. |
| 씬 코드(MonoBehaviour) | `GBibleRuntime.Game` (보통 `GameApi Game => GBibleRuntime.Game;`로 줄여 둔다) |
| 테스트·서버·시뮬레이션 | `session.Game` |

```csharp
using GBible;
using UnityEngine;

public class BoardView : MonoBehaviour
{
    GameApi Game => GBibleRuntime.Game;
}
```

`GBibleRuntime`은 게임이 켜질 때(씬을 불러오기 전) 세션을 만든다. 그 전에는 `GBibleRuntime.Game`이 `null`이다.

### 2.2 `Game.Session`

```csharp
GameSession session = Game.Session;
session.Undo();
var clock = session.Clock.Time;
var data = session.Data;
```

세션 전체(플레이어 데이터·이벤트·기록·시계·행동 목록 등)에 닿는다. 아래 메서드로 되는 일은 아래 메서드로 한다.

### 2.3 `Game.All<T>()` / `Game.All<T>(key)` — 모두 모으기

```csharp
List<Unit> everyone = Game.All<Unit>();                    // 모든 플레이어 데이터에서
List<Unit> mine = Game.All<Unit>("p1.board");               // 플레이어 데이터 하나에서
```

- 플레이어 데이터 안의 `T` 객체를 **모두** 모아 새 `List<T>`로 준다. 클래스 필드·리스트·배열·딕셔너리 값 안쪽까지 내려간다.
- 순서는 늘 같다: 플레이어 데이터 키 이름순 → 그 안에 들어 있는 순서(필드 순서 → 리스트 순서).
- `T`는 클래스(또는 인터페이스·추상 클래스)다. 자식 클래스 객체도 함께 모인다.
- 형식만 보고 "이 플레이어 데이터에 `T`가 들어 있을 수 있는가"를 판단해, 들어 있을 수 없는 키는 건너뛴다(판단 결과는 기억해 둔다).
- 단순 값 키(숫자·글자)는 보지 않는다.
- 돌려받은 리스트는 새 리스트다. 리스트에 더하거나 빼도 플레이어 데이터는 바뀌지 않는다. 리스트 안의 **객체**는 실제 객체이므로 그 필드를 고치면 플레이어 데이터가 바뀐다.

### 2.4 `Game.Find<T>(id)` — id로 하나 찾기

```csharp
Unit hero = Game.Find<Unit>("hero");      // 없으면 null
```

- `T` 객체 중 **`id` 필드**가 같은 첫 객체를 돌려준다. 없으면 `null`.
- `id`가 비어 있으면 `null`.
- `T`에 `string` 형식의 `id`(또는 `Id`) 필드가 없으면 "id 필드가 없어 Find로 찾을 수 없습니다. 'public string id;'를 더하세요." 예외다.
- 같은 id가 여러 개면 `All`과 같은 순서에서 처음 만난 것이다. id는 형식이 달라도 플레이어 데이터 전체에서 겹치지 않게 만든다(`NewId`, [02-Actions-Undo.md](02-Actions-Undo.md) 2.2).

형식을 실행 중에 정해야 할 때는 `Game.Find(Type, id)`를 쓴다. 규칙은 `Find<T>`와 같고 `object`를 돌려준다. 행동의 참조 필드를 실행 직전에 다시 찾을 때 시스템이 쓰는 찾기가 이것이다.

```csharp
object target = Game.Find(typeof(Unit), "orc1");   // Find<Unit>("orc1")와 같다
```

### 2.5 `Game.Find(id)` — 형식 없이 찾기

```csharp
object anything = Game.Find("hero");
```

형식을 모를 때 쓴다. 모든 플레이어 데이터를 훑어 `id`(또는 `Id`) 필드·속성이 같은 첫 객체를 돌려준다. 모든 데이터의 사본 나무를 훑으므로(리플렉션은 없다) 데이터가 크면 `Find<T>`보다 느리다. 형식을 알면 `Find<T>`를 쓴다.

### 2.6 `Game.Get<T>(key)` / `Game.Set(key, value)` — 단순 값

```csharp
int gold = Game.Get<int>("player.gold");
Game.Set("player.gold", gold + 100);
```

숫자·글자·참거짓 같은 **단순 값** 플레이어 데이터를 ID로 읽고 바꾼다(`session.Data.Get<T>`·`Set`과 같다). 생성 코드가 있으면 `Game.Data().PlayerGold`가 더 편하다. 클래스 플레이어 데이터는 `Find`·`All`·`Game.Data()`로 객체를 꺼내 필드를 고친다.

### 2.7 `Game.Data()` — 이름으로 읽고 쓰기 (생성 코드)

```csharp
using GBible.Ids;

Game.Data().PlayerGold += 100;                         // 단순 값: get/set
Game.Data().PlayerInventory.items.Add(new Item());     // 클래스 값: 실제 객체
```

`MomoiProject › 식별자 코드 다시 만들기`(DB를 저장하면 자동)로 생기는 확장 메서드다. 이름은 플레이어 데이터 ID의 파스칼 표기다.

### 2.8 `Game.Do(action)` — 행동 실행

```csharp
ActionRecord record = Game.Do(new MoveUnit { unit = hero, x = 3, y = 4 });   // hero: 데이터 안의 Unit 객체
```

`session.Do`와 같다. 행동 하나가 Undo 한 단위다. 대상은 객체로 넣는다: `string id`가 있는 클래스 형식의 필드는 기록에 id만 남고, 실행 직전에 그 id로 지금 데이터에서 다시 찾아 넣는다(못 찾으면 행동 실패). 자세히는 [02-Actions-Undo.md](02-Actions-Undo.md).

### 2.9 `Game.Use(ability, user, params targets)` · `Game.TryUse` — 능력·아이템 쓰기

```csharp
Game.Use("ability.fireball", hero, orc1, orc2);      // 객체로
Game.Use("ability.fireball", "hero", "orc1");         // id로
Game.Use("ability.selfHeal", hero);                   // 대상 없이
```

DB › 능력의 능력, 또는 사용 기능(`Usable`)이 붙은 인스턴스를 ID로 쓴다. `user`·`targets`는 객체 또는 id 문자열이다(객체를 넘기면 그 객체의 `id` 필드를 읽어 id로 바꾼다). 내부적으로 행동 하나(`UseAbility`)라서 Undo 한 번에 통째로 되돌아간다. 행동 안에서 부르면 그 행동의 일부로 그 자리에서 실행된다. 자세히는 [16-Abilities-Usable.md](16-Abilities-Usable.md).

- 사용 조건이 안 맞으면 실패(`GameFailure`)를 던진다. 행동 안이면 그 행동 전체가 취소된다.
- 결과를 내기 직전에 의도 **'사용'**(`UseIntent { ability, userId, times = 1, cancelled }`)을 거친다. 가로채기가 `times`를 늘리면 그만큼 결과를 낸다("한 번 더 발동"). 0이면 내지 않는다. 막으면 실패를 던지지 않고 아무 일 없이 끝난다(28장). 이벤트 '능력 사용'(`AbilityUsed`)의 `Times`가 낸 횟수다.
- 그다음 의도 **'효과 배율'**(`PowerIntent { ownerId, userId, multiplier = 1 }`)을 한 번 거친다. 고친 배율이 이 사용의 수치(상태 효과 중첩·필드 바꾸기 더하기·능력치 보정 값)에 곱해진다(28장 4-14).

**`Game.TryUse(ability, user, params targets)` — 쓸 수 있을 때만 쓰기**

```csharp
for (int i = 0; i < board.Count; i++)                                // 칸 번호 순서로(빈 칸 = null)
    if (board[i] != null) Game.TryUse(board[i].ability, board[i]);   // 쓸 수 없는 유닛은 건너뛴다(턴 전체가 취소되지 않는다)
```

- 쓸 수 있으면 쓰고 `true`다. 사용 조건이 안 맞으면 아무것도 하지 않고 `false`다. 실패를 던지지 않는다.
- 의도 '사용'이 막았거나 `times`가 0이어도 결과를 내지 않으므로 `false`다.
- 조건을 한 번만 검사한다. 확률 조건(07장 2.15)도 한 번만 굴린다. `CanUse`로 묻고 `Use`하면 검사가 두 번이고, 확률 조건이 있으면 두 번의 답이 다를 수 있다.
- 조커·참·보드 유닛을 차례로 돌며 "되는 것만 발동"할 때 쓴다(37장 5-3).

### 2.10 `Game.CanUse(...)` — 지금 쓸 수 있는가

```csharp
bool ok = Game.CanUse("ability.heal", hero);                               // 대상 여러 개는 params
bool ok2 = Game.CanUse("ability.heal", hero, out string why);              // 안 될 때 이유
bool ok3 = Game.CanUse("ability.heal", hero, new object[] { ally }, out why);
```

능력의 트리거(사용 조건)만 검사한다. 아무것도 바꾸지 않는다. 미리 보기라 확률 조건은 굴리지 않고 참으로 본다(난수를 쓰지 않는다). 버튼을 미리 흐리게 하거나 이유를 툴팁으로 보일 때 쓴다. 없는 ID면 `false`와 "쓸 수 있는 능력·아이템이 아닙니다", 조건이 거짓이면 `Game.Use`가 던지는 실패와 같은 지금 언어의 글이 `reason`에 들어간다: 그 능력·아이템의 쓸 수 없을 때 글(DB › 능력 `failText`·`Usable.failText`, 텍스트 키 또는 글)이 있으면 그 글, 비었으면 `FailureKeys.CannotUse`의 글("{ability}: 사용 조건을 만족하지 않습니다({condition}).", `30-Balance-Tables.md` 2-4)이다.

### 2.11 `Game.Random(name)` · `Game.Chance` — 결정론 난수·확률

```csharp
int damage = 10 + Game.Random("damage").Range(0, 5);   // 0 이상 5 미만
```

이름별로 따로 흐르는 난수 스트림(`RandomStream`)을 준다. 행동을 Undo하면 함께 되돌아간다. 자세히는 [07-Random-Time.md](07-Random-Time.md).

```csharp
if (Game.Chance(0.25, "loot", source: GameIds.Passive.EightBall)) { … }   // 25%, 흐름을 비우면 "chance"
```

`Game.Chance(확률, 흐름 = "chance", source, user)`는 굴리기 전에 의도 '확률'(`ChanceIntent`)을 거친다. 행운·"모든 확률 2배"가 확률을 바꾸고, 막으면 실패다. 확률이 바뀌어도 난수는 한 번만 굴린다(같은 시드면 뒤가 밀리지 않는다). 유물·패시브가 고칠 수 있어야 하는 확률은 `Game.Random(…).Chance(p)` 대신 이것을 쓴다(07장 2.15).

### 2.12 `Game.NewId(prefix)` · `Game.NewIds(prefix, count)` — 새 id

```csharp
string id = Game.NewId("unit");               // "unit1", "unit2", …
List<string> wave = Game.NewIds("slime", 30);  // "slime1" … "slime30"(번호를 한 번만 쓴다)
```

소환·드롭처럼 객체를 새로 만들 때 겹치지 않는 id를 준다. 번호는 플레이어 데이터(메타 구역의 `$id.접두어` 키)에 있어서, 행동 안에서 만들면 Undo하면 번호도 돌아가고, 저장·멀티플레이에서도 같다. 메타 구역이므로 판이 바뀌어도(`NewRun`) 번호는 이어서 늘어난다.

적 수십 마리처럼 한 번에 많이 만들 때는 `NewIds`를 쓴다. `NewId`를 반복하면 그만큼 번호를 읽고 쓰지만, `NewIds`는 한 번에 끝낸다. `count`가 0 이하면 빈 목록이다.

### 2.13 `Game.Events` — 이벤트

```csharp
Game.Events.Publish(new EnemyKilled { enemy = "orc1" });                // 쏜다
IDisposable sub = Game.Events.Subscribe<EnemyKilled>(e => PlayHitEffect(e.enemy));   // 받는다(연출)
sub.Dispose();                                                            // 그만 받는다
```

`session.Events`(`EventBus`)와 같다. 이벤트를 받는 코드에서 플레이어 데이터를 바꾸면 행동 밖 변경이 되므로, 받는 쪽은 연출·UI만 한다. 이벤트 정의(`[GameEvent]`)와 쏘기·받기는 [10-Events.md](10-Events.md).

### 2.14 `GameApi.IdOf(item)`

```csharp
string id = GameApi.IdOf(hero);   // hero.id
```

객체의 `id` 필드(또는 `Id` 속성) 값을 읽는다. 그런 멤버가 없으면 예외다. 멤버는 형식마다 처음 한 번 찾고, 값은 생성 코드로 읽는다(리플렉션 없음). 형식을 알면 `hero.id`를 바로 쓴다.

`GameApi.HasId(type)`은 그 형식에 `string id`(필드·속성)가 있는지(찾거나 가리킬 수 있는 객체인지)를 알려 준다. 행동의 필드가 참조 필드인지도 이것으로 정해진다.

### 2.15 확장 메서드로 장르 규칙 더하기

`GameApi`는 `sealed`지만 확장 메서드를 붙일 수 있다. 장르마다 자주 쓰는 조회는 여기에 모은다(`Unit`은 4.0의 클래스).

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using GBible;

[Serializable]
public class Terrain
{
    public Dictionary<string, string> cells = new Dictionary<string, string>();   // Grid.Key(x, y) → "grass" · "water" · "wall"
}

public static class TacticsQueries
{
    public static Cell CellOf(Unit u) => new Cell(u.x, u.y);

    public static IEnumerable<Unit> Around(this GameApi game, Unit center, int range) =>
        game.All<Unit>().Where(u => u != center && Grid.Distance(CellOf(center), CellOf(u)) <= range);

    // 갈 수 있는 칸 → 남은 이동력. All은 한 번만 부르고, 칸마다는 사전에서 찾는다.
    public static Dictionary<Cell, int> MoveRange(this GameApi game, Unit unit, int move)
    {
        var terrain = game.All<Terrain>()[0].cells;
        var occupied = new HashSet<Cell>(game.All<Unit>().Where(u => u != unit).Select(CellOf));
        return Grid.Reachable(CellOf(unit), move, c =>
            occupied.Contains(c) || !terrain.TryGetValue(Grid.Key(c), out var t) || t == "wall" ? -1   // 음수 = 못 감
            : t == "water" ? 2 : 1);
    }
}
```

- 거리·이웃·이동 범위·길·시야 같은 칸 계산은 라이브러리의 `Grid`·`Cell`([36-Grid-BigNumber.md](36-Grid-BigNumber.md))을 쓴다. 이름이 `Grid`인 클래스를 직접 만들지 않는다(`using GBible;`의 `Grid`와 겹친다).
- **칸마다 `Game.All`·`Find`를 부르지 않는다.** 부를 때마다 그 형식이 들어 있을 수 있는 키를 Sync하고 사본을 훑는다(3절). "이 칸에 누가 있나"를 칸마다 `All`로 찾으면 이동 범위 하나에 수십 번 돈다. 한 번 모아 사전(칸 → 유닛, 칸 → 지형)을 만들고 칸마다는 사전에서 찾는다.
- 맵이 판 중에 바뀌지 않으면 지형은 원본 인스턴스(예: 한 줄이 글자 하나인 `rows` 목록 `"..#.."`)에 두고 읽기만 해도 된다. 유닛의 칸은 플레이어 데이터의 `x`·`y`다.

### 2.16 다른 장에서 다루는 멤버

| 멤버 | 하는 일 | 장 |
|---|---|---|
| `Now` · `Day(resetHour)` · `Week(resetHour, start)` | 현실 시각(UTC, 행동 안이면 그 행동이 시작한 순간), 날짜·주 번호. 규칙은 `DateTime.Now` 대신 이것 | [07](07-Random-Time.md) 2.11~2.13 |
| `Attribute` · `AddModifier` · `RemoveModifiers` · `Modifiers` | 능력치 최종값, Modifier 붙이기·떼기 | [28](28-Attributes-Intercepts.md) |
| `Intercept` · `Pay(key, path, 양, what, forId, user)` · `CanPay` | 가로채기, 비용 내기(가로채기 '비용 지불'이 `floor`를 낮추면 빚을 낼 수 있다). `forId`(무엇을 위한 비용 — 사는 카드·쓰는 주문 ID)·`user`(내는 쪽)는 의도에 실려 가로채기가 거른다("행성 카드는 공짜") | [28](28-Attributes-Intercepts.md) |
| `TryPay(key, path, 양, forId, user)` | 낼 수 있으면 내고 `true`, 모자라면 아무것도 바꾸지 않고 `false`(실패를 던지지 않는다). 틱마다 도는 행동 안에서 "MP가 되면 쏜다" | [28](28-Attributes-Intercepts.md) |
| `ApplyStatus(대상, id, 중첩, 출처, 길이, by)` · `RemoveStatus` · `StatusStacks` · `HasStatus` · `Statuses` | 상태 효과 걸기(길이를 비우면 상태 효과의 길이)·풀기·읽기. `by`(건 개체, 객체 또는 id)는 걸린 것의 `by`가 되고 다시 걸면 마지막에 건 쪽으로 바뀐다(처치 기여). 걸기 직전에 의도 '상태 효과 걸기'(`StatusIntent`)를 거친다. 가로채기가 상태 효과를 바꾸거나(화상 → 푸른 화상) 중첩·길이·최대 중첩·주기를 고치고, 막으면(면역) `ApplyStatus`는 `null`이다 | [29](29-Statuses-Schedules-Choices.md) |
| `StatusCount(대상, 종류)` · `RemoveStatuses(대상, 종류)` | 그 종류(`StatusKind`: 버프·디버프)가 몇 가지 걸려 있나 · 그 종류를 모두 푼다(정화, 푼 가짓수) | [29](29-Statuses-Schedules-Choices.md) |
| `ShortenStatus(대상, id, 양)` | 남은 횟수·초를 줄인다. 다 되면 지속이 끝난 것으로 끝낸다. 영구거나 없으면 `false` | [29](29-Statuses-Schedules-Choices.md) |
| `Owned(패시브 id, 가진 쪽 = null)` | 그 패시브를 몇 개(레벨) 가졌나. 가진 쪽을 비우면 모두의 합(레벨 필드가 있으면 그 합). '놓인 것마다' 패시브면 가진 쪽 = 목록의 요소 하나 → 그 요소의 레벨. 레벨 0 이하인 요소와 침묵한 가진 쪽(상태 효과 '걸린 쪽의 패시브를 끔')은 0으로 센다 | [17](17-Custom-Features.md) · [29](29-Statuses-Schedules-Choices.md) |
| `BorrowPassive(패시브 id, 원본)` | 빌려 쓰기: 지금 순간(트리거 결과·`[BindTo]` 안)으로 그 패시브를 원본 조각의 것으로 한 번 더 낸다(Blueprint를 코드로). 결과 '빌려 쓰기'와 같다. 결과를 냈으면 `true` | [37](37-Placement.md) 4-3 · [17](17-Custom-Features.md) |
| `ScheduleAfter` · `ScheduleOn<T>` · `CancelSchedule` · `CancelSchedules` | 예약 | [29](29-Statuses-Schedules-Choices.md) |
| `Ask` · `Answer` · `Choice` · `CancelChoice` | 선택 대기 | [29](29-Statuses-Schedules-Choices.md) |
| `RerollChoice(선택 id)` | 다시 뽑기(행동 안에서): '가중치 표에서 고르기'로 만든 선택의 후보를 같은 표·같은 후보 수로 새로 뽑는다. 표로 만든 선택이 아니면 `false`. 비용·남은 횟수는 부르는 행동이 맡는다(`Game.Pay`). Undo하면 원래 후보 | [29](29-Statuses-Schedules-Choices.md) · [30](30-Balance-Tables.md) |
| `Balance(id)` · `Balance(id, x)` | 밸런스 값, 곡선의 그 단계 값 | [30](30-Balance-Tables.md) |
| `BalanceSum(id, from, count)` · `BalanceCount(id, from, budget)` | 단계 `from`부터 `count`개의 합("10개 사기"), 예산으로 단계 `from`부터 살 수 있는 개수 | [30](30-Balance-Tables.md) |
| `Roll` · `Fail` | 가중치 표 뽑기, 플레이어용 실패 | [30](30-Balance-Tables.md) |
| `World` · `WorldFlag` · `SetWorld` · `ClearWorld` | 월드 상태 | [32](32-View-Presentation-Scenes.md) |
| `Preview` · `CanDo` · `IsPreviewing` | 행동을 해 보고 되돌리는 미리보기 | [33](33-Tick-Preview-Csv.md) |

---

## 3. `Find`·`All`은 어떻게 찾는가

```
Game.Find<Unit>("orc1")
 │
 ├─ 플레이어 데이터 키를 이름순으로 하나씩 (단순 값 키는 건너뜀)
 │    └─ 이 키의 형식 안에 Unit이 들어 있을 수 있나? (형식만 보고 판단, 결과 기억)
 │         아니면 다음 키
 │
 ├─ 그 키를 Sync한다 (사본을 지금 값에 맞춤. 행동 안이면 행동의 변경, 밖이면 행동 밖 변경으로 기록)
 │
 ├─ 사본(Mirror) 나무를 깊이 우선으로 따라간다: 필드 순서 → 리스트·배열·딕셔너리 요소 순서
 │    └─ 객체가 Unit이고, 그 객체의 string 필드 id(또는 Id) 값이 "orc1"이면 → 그 실제 객체를 돌려준다
 │
 └─ 끝까지 없으면 null
```

- 사본에는 객체마다 원래 객체와 필드 값이 들어 있다. 비교 코드가 형식마다 `id` 필드의 위치를 알고 있어서 **실행 중 리플렉션 없이** 찾는다.
- 사본은 마지막 Sync 때의 모습이라, 찾기 전에 그 키를 Sync한다. 그래서 방금 고친 id도 바로 찾힌다.
- 예전 방식 데이터([01-PlayerData.md](01-PlayerData.md)의 6장)는 사본이 없어서 리플렉션으로 필드를 훑는다.
- 찾는 비용은 데이터 크기에 비례한다. 매 프레임 많은 객체를 그릴 때는 `All`을 한 번 불러 리스트를 재사용하고, 객체마다 `Find`를 반복하지 않는다.

`id`는 필드(`public string id;`)든 속성(`public string Id { get; set; }`)이든 된다. 필드면 사본의 값으로, 속성이면 생성된 읽기 코드로 읽는다(둘 다 리플렉션 없음). 형식은 `string`이어야 한다.

---

## 4. 상황별 예제

### 4.0 예제에서 함께 쓰는 클래스

```csharp
using System;

[Serializable]
public class Unit
{
    public string id;
    public string team;
    public int x, y;
    public int hp = 30;
}
```

### 4.1 씬 코드: 유닛 그리기와 선택

```csharp
using System.Collections.Generic;
using GBible;
using UnityEngine;

public class UnitBoardView : MonoBehaviour
{
    GameApi Game => GBibleRuntime.Game;
    string _selected;                          // 객체가 아니라 id로 들고 있는다

    void Update()
    {
        if (Game == null) return;
        List<Unit> units = Game.All<Unit>();   // 프레임마다 한 번
        foreach (var u in units)
        {
            // u.x, u.y, u.hp로 그린다(씬 코드는 읽기만)
        }
    }

    public void Select(Unit u) => _selected = u.id;

    public Unit Selected => Game.Find<Unit>(_selected);   // Undo·불러오기 뒤에도 지금 객체
}
```

### 4.2 전술: 주변 적에게 범위 공격

```csharp
using System;
using System.Linq;
using GBible;

[Serializable]
public class Shockwave : GameAction
{
    public Unit caster;                        // 참조 필드: 실행 직전에 지금 객체로 다시 찾는다
    public int range = 1;
    public int damage = 6;

    protected override void Run()
    {
        foreach (var enemy in Game.Around(caster, range).Where(u => u.team != caster.team).ToList())
            enemy.hp -= damage + Game.Random("damage").Range(0, 3);
    }
}
```

`Game.Around`는 2.15의 확장 메서드다. 모든 피해가 행동 하나에 묶여 Undo 한 번에 돌아간다.

### 4.3 소환: 새 id로 객체 더하기

```csharp
using System;
using System.Collections.Generic;
using GBible;

[Serializable]
public class Arena
{
    public List<Unit> units = new List<Unit>();
}

[Serializable]
public class SummonWolf : GameAction
{
    public int x, y;
    protected override void Run()
    {
        var arena = Game.All<Arena>()[0];
        arena.units.Add(new Unit { id = Game.NewId("wolf"), team = "ally", x = x, y = y, hp = 15 });
    }
}
```

Undo하면 늑대가 빠지고 번호도 돌아가므로, 다시 소환하면 같은 id(`wolf1`)를 받는다.

### 4.4 두 플레이어의 보드를 따로 모으기

```csharp
using System;
using System.Collections.Generic;
using GBible;

[Serializable]
public class PlayerBoard
{
    public List<Unit> units = new List<Unit>();
}

public static class Versus
{
    public static void Setup(GameSession session)
    {
        session.Data.Define("p1.board", DataZone.Session, new PlayerBoard(), "1P 보드");
        session.Data.Define("p2.board", DataZone.Session, new PlayerBoard(), "2P 보드");
    }

    public static int AliveCount(GameApi game, string board)
    {
        var count = 0;
        foreach (var u in game.All<Unit>(board)) if (u.hp > 0) count++;
        return count;
    }
}
```

`Game.All<Unit>()`는 두 보드의 유닛을 모두(`p1.board` → `p2.board` 순서) 주고, `Game.All<Unit>("p1.board")`는 한쪽만 준다.

### 4.5 스킬 버튼: 쓸 수 있는지 미리 보고 쓰기

```csharp
using GBible;
using UnityEngine;
using UnityEngine.UI;

public class SkillButton : MonoBehaviour
{
    public Button button;
    public string ability = "ability.heal";
    public string casterId = "hero";
    GameApi Game => GBibleRuntime.Game;

    void Update()
    {
        var caster = Game?.Find<Unit>(casterId);
        if (caster == null) return;
        button.interactable = Game.CanUse(ability, caster, out _);
    }

    public void OnClick() => Game.Use(ability, casterId);
}
```

### 4.6 테스트·서버: 세션을 직접 만들어 쓰기

```csharp
using GBible;

var session = new GameSession(new SessionOptions { Seed = 7 });
session.Data.Define("arena", DataZone.Session, new Arena
{
    units = { new Unit { id = "hero", team = "ally" }, new Unit { id = "orc1", team = "enemy", x = 1 } },
});
var game = session.Game;
game.Do(new Shockwave { caster = game.Find<Unit>("hero") });
var orc = game.Find<Unit>("orc1");    // hp가 줄어 있다
session.Undo();                        // 다시 30
```

---

## 5. 주의할 점 · 자주 하는 실수

| 증상 | 원인과 해결 |
|---|---|
| `Find<T>`가 늘 `null` | 그 객체가 아직 플레이어 데이터 안에 들어가지 않았거나, id 필드·속성의 형식이 `string`이 아니다. |
| "id 필드가 없어 Find로 찾을 수 없습니다" | `T`에 `string id`(또는 `Id`) 필드가 없다. |
| 씬 코드에서 고친 값이 Undo되지 않음 | `Find`·`All`로 꺼낸 객체를 행동 밖에서 고쳤다. 씬 코드는 읽기·그리기만 하고, 바꾸는 일은 `Game.Do`로 한다. |
| `All<T>()`가 원하지 않는 객체까지 줌 | 같은 형식이 여러 플레이어 데이터에 있다. `All<T>("키")`로 한 곳만 본다. |
| `All<T>()`가 준 리스트에 더했는데 반영되지 않음 | 돌려받은 리스트는 새 리스트다. 플레이어 데이터 안의 리스트(예: `board.units`)에 더한다. |
| 오래 쥔 객체가 엉뚱한 값 | 불러오기·멀티플레이 받는 쪽에서는 다른 인스턴스일 수 있다. 값을 읽을 때는 id로 들고 있다가 `Find`한다. 행동에 넣는 것은 그 객체 그대로도 된다(실행 직전에 같은 id의 지금 객체로 바뀐다). |
| 유닛 하나가 쓸 수 없어서 턴 전체가 취소됨 | 차례로 돌며 `Game.Use`를 불렀다. 사용 조건이 안 맞으면 실패를 던져 바깥 행동까지 취소된다. 되는 것만 발동할 때는 `Game.TryUse`(2.9). 비용도 같다: 모자라면 건너뛸 때는 `Game.Pay` 대신 `Game.TryPay`(2.16). |
| "모든 확률 2배" 유물이 먹히지 않음 | 확률을 `Game.Random(…).Chance(p)`로 굴렸다. 의도 '확률'을 거치지 않는다. `Game.Chance`(2.11). |
| `ApplyStatus`가 돌려준 값에서 `NullReferenceException` | 의도 '상태 효과 걸기'를 가로채기가 막았다(면역). 돌려받은 값이 `null`인지 먼저 본다. |
| 행동 클래스 밖에서 `Game` 속성이 `null` | 행동·Block의 `Game` 속성은 그 코드가 실행되는 동안만 있다. 씬 코드는 `GBibleRuntime.Game`. |
| 프레임이 느려짐 | `Find`를 객체마다 반복했다. `All`을 한 번 불러 쓰거나 id → 객체 사전을 프레임마다 한 번 만든다. |
| 이동 범위·AI 계산이 느림 | 칸마다(후보마다) `All`·`Find`를 불렀다. 한 번 모아 칸 → 유닛, 칸 → 지형 사전을 만들고 칸 계산은 `Grid`로 한다(2.15). |
| 행동이 느림 | 한 행동 안에서 같은 객체를 여러 번 `Find`했다. 찾을 때마다 데이터를 훑고 Sync한다. 한 번 찾아 변수로 들고, 대상은 행동 필드에 객체로 넣는다(실행 직전 한 번만 찾는다). 많이 만들 때 id는 `NewIds`로. |
| `Grid`가 모호하다는 컴파일 오류·엉뚱한 `Grid` | 게임 코드에 이름이 `Grid`인 클래스가 있다. 라이브러리의 `GBible.Grid`를 쓰고 직접 만든 것은 이름을 바꾼다. |
