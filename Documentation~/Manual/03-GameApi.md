# 03. Game API

**한 줄 요약**: `Game`(`GameApi`)은 게임 코드가 시스템을 쓰는 창구다. 플레이어 데이터 안의 객체를 찾고(`Find`·`All`), 행동을 실행하고(`Do`), 능력·아이템을 쓰고(`Use`), 결정론 난수(`Random`)와 새 id(`NewId`)를 얻는다.

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
| 행동(`GameAction<T>`·`SimpleAction`)·조건(`SimpleCondition`)·효과·대상 Block 안 | 물려받은 `Game` 속성. 그 코드가 실행되는 동안만 들어 있다. |
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
- 같은 id가 여러 개면 `All`과 같은 순서에서 처음 만난 것이다. id는 겹치지 않게 만든다(`NewId`).

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

`MomoiProject › 식별자 코드 다시 만들기`(도감을 저장하면 자동)로 생기는 확장 메서드다. 이름은 플레이어 데이터 ID의 파스칼 표기다.

### 2.8 `Game.Do(action)` — 행동 실행

```csharp
ActionRecord record = Game.Do(new MoveUnit { target = "hero", x = 3, y = 4 });
```

`session.Do`와 같다. 행동 하나가 Undo 한 단위다. 자세히는 [02-Actions-Undo.md](02-Actions-Undo.md).

### 2.9 `Game.Use(ability, user, params targets)` — 능력·아이템 쓰기

```csharp
Game.Use("ability.fireball", hero, orc1, orc2);      // 객체로
Game.Use("ability.fireball", "hero", "orc1");         // id로
Game.Use("ability.selfHeal", hero);                   // 대상 없이
```

도감 › 능력의 능력, 또는 사용 기능(`Usable`)이 붙은 인스턴스를 ID로 쓴다. `user`·`targets`는 객체 또는 id 문자열이다(객체를 넘기면 그 객체의 `id` 필드를 읽어 id로 바꾼다). 내부적으로 행동 하나(`UseAbility`)라서 Undo 한 번에 통째로 되돌아간다. 행동 안에서 부르면 그 행동의 일부로 그 자리에서 실행된다. 자세히는 [16-Abilities-Usable.md](16-Abilities-Usable.md).

### 2.10 `Game.CanUse(...)` — 지금 쓸 수 있는가

```csharp
bool ok = Game.CanUse("ability.heal", hero);                               // 대상 여러 개는 params
bool ok2 = Game.CanUse("ability.heal", hero, out string why);              // 안 될 때 이유
bool ok3 = Game.CanUse("ability.heal", hero, new object[] { ally }, out why);
```

능력의 트리거(사용 조건)만 검사한다. 아무것도 바꾸지 않는다. 버튼을 미리 흐리게 하거나 이유를 툴팁으로 보일 때 쓴다. 없는 ID면 `false`와 "쓸 수 있는 능력·아이템이 아닙니다", 조건이 거짓이면 "사용 조건을 만족하지 않습니다: …"가 `reason`에 들어간다.

### 2.11 `Game.Random(name)` — 결정론 난수

```csharp
int damage = 10 + Game.Random("damage").Range(0, 5);   // 0 이상 5 미만
```

이름별로 따로 흐르는 난수 스트림(`RandomStream`)을 준다. 행동을 Undo하면 함께 되돌아간다. 자세히는 [07-Random-Time.md](07-Random-Time.md).

### 2.12 `Game.NewId(prefix)` — 새 id

```csharp
string id = Game.NewId("unit");    // "unit1", "unit2", …
```

소환·드롭처럼 객체를 새로 만들 때 겹치지 않는 id를 준다. 번호는 플레이어 데이터(메타 구역의 `$id.접두어` 키)에 있어서, 행동 안에서 만들면 Undo하면 번호도 돌아가고, 저장·멀티플레이에서도 같다. 메타 구역이므로 판이 바뀌어도(`NewRun`) 번호는 이어서 늘어난다.

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

### 2.15 확장 메서드로 장르 규칙 더하기

`GameApi`는 `sealed`지만 확장 메서드를 붙일 수 있다. 장르마다 자주 쓰는 조회는 여기에 모은다(`Unit`은 4.0의 클래스).

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using GBible;

public static class Grid
{
    public static int Distance(Unit a, Unit b) => Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y);

    public static IEnumerable<Unit> Around(this GameApi game, Unit center, int range) =>
        game.All<Unit>().Where(u => u != center && Distance(center, u) <= range);

    public static Unit At(this GameApi game, int x, int y) =>
        game.All<Unit>().FirstOrDefault(u => u.x == x && u.y == y);
}
```

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
public class Shockwave : GameAction<Unit>
{
    public int range = 1;
    public int damage = 6;

    protected override void Run(Unit caster)
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
public class SummonWolf : SimpleAction
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
game.Do(new Shockwave { target = "hero" });
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
| 오래 쥔 객체가 엉뚱한 값 | 불러오기·멀티플레이 받는 쪽에서는 다른 인스턴스일 수 있다. id로 들고 있다가 쓸 때 `Find`한다. |
| 행동 클래스 밖에서 `Game` 속성이 `null` | 행동·Block의 `Game` 속성은 그 코드가 실행되는 동안만 있다. 씬 코드는 `GBibleRuntime.Game`. |
| 프레임이 느려짐 | `Find`를 객체마다 반복했다. `All`을 한 번 불러 쓰거나 id → 객체 사전을 프레임마다 한 번 만든다. |
