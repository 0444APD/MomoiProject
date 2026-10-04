# 16. 능력과 사용 기능

**한 줄 요약**: 능력은 "쓰는 순간" 발동하는 기능(스킬, 아이템 사용 효과, 버프 발동)이다. Trigger = **사용**(+ 원하면 AND로 사용 조건), 결과 = 대상에게 효과 · 플레이어 데이터에 주기 · 코딩한 결과. 게임 코드는 `Game.Use(ID, 사용자, 대상들)` 한 줄로 쓰고, 그 전체가 행동 하나라 Undo 한 번에 되돌아간다. **사용 기능**(`Usable`)은 같은 것을 아이템 같은 인스턴스에 필드로 붙이는 형태다.

**언제 쓰는가**

- 플레이어가 고른 대상에게 스킬을 쓸 때: "파이어볼(고른 적 + 주변 1칸에 10 피해)".
- 아이템을 쓸 때: "포션(사용자 체력 30 회복)".
- 쓸 수 있는지 미리 보여 줄 때: 버튼을 흐리게 하고 이유를 툴팁으로(`Game.CanUse`).

이 문서에서 쓰는 용어:

| 용어 | 뜻 |
|---|---|
| Trigger | 언제 발동하는지(→ `11-Triggers-Conditions.md`). 능력은 `사용` 잎이 기본이다. |
| Outcome | 무엇을 하는지(→ `12-Outcomes.md`). |
| Block | 에디터에서 조립하는 로직 단위. 능력에서는 대상 Block(`TargetSelector`)·효과 Block(`Effect`)·조건 Block(`Condition`)을 주로 쓴다. |
| 사용자 | 능력을 쓰는 개체(`user`). |
| 고른 대상 | 플레이어가 고른 대상들(`targets`, 컨텍스트에서는 `Picked`). |
| 행동 (`GameAction`) | 플레이어 입력 하나. 데이터라서 서버 전송·리플레이가 되고, 실행 한 번이 Undo 한 단위다. |
| TriggerContext (컨텍스트) | 사용하는 순간의 정보(`IsUse = true`, `User`, `Picked`, `Owner` = 능력 ID). |
| 인스턴스 | DB › 인스턴스에서 만든 클래스 객체 원본(아이템 A …). |

---

## 1. 에디터에서 쓰는 법

### 1-1. 능력 만들기(DB › 능력)

1. `MomoiProject → DB` → 사이드바 **DB** 그룹의 **능력** 탭 → 목록 줄의 **`+ 능력`**. 제목(= ID)을 짓는다(예: `fireball`).
2. `trigger`(툴팁 "트리거"): 기본으로 `사용` 하나가 들어 있다(요약 "사용할 때").
   - 사용 조건을 붙이려면 `+ 블럭` → **조건 충족** 그룹의 조건 Block(예: 프로젝트가 만든 `사용자 체력 있음`)을 더한다. `사용`과 `AND`로 이어진다.
   - 이 칸에서 고를 수 있는 것은 `사용`과 조건 충족뿐이다.
3. `outcomes`(툴팁 "결과"): `+ 결과 추가` →
   - `대상에게 효과` → `+ 대상 추가`(비우면 고른 대상 그대로) → `+ 효과 추가`. 기본 제공 효과 `능력치 보정`·`능력치 보정 떼기`(분류 "능력치")로 버프를 코드 없이 만든다(3-10).
   - `플레이어 데이터에 주기 · …` → 필드와 값.
   - 코딩한 결과(`Reward` 클래스).
4. 검증 경고: "트리거에 '사용'이 없습니다. 쓰는 순간과 상관없이 조건만으로 쓸 수 있는지가 정해집니다." / "결과가 없습니다. '+ 결과 추가'로 무엇을 할지 더하세요."

결과를 코드로 쓰려면 `[BindTo("fireball")]`(→ `13-BindTo.md`). 연결되면 결과 칸이 잠긴다.

### 1-2. 인스턴스에 사용 기능 붙이기(DB › 인스턴스)

먼저 코드에서 아이템 클래스에 사용 기능 필드를 둔다(2-7).

1. `MomoiProject → DB` → **인스턴스** 탭 → **`+ 인스턴스`** → 제목(= 인스턴스 ID, 예: `potion`)을 짓는다.
2. `instanceType`(툴팁 "형식")에서 아이템 클래스(`Item`)를 고른다. 값(`value`)이 새로 만들어진다.
3. 값 탐색기 아래 **`+ 기능`**을 누르고 목록에서 `usable`(오른쪽 작은 글씨: 형식 이름)을 고른다. 사용 기능이 붙고 그 칸이 보인다. 줄의 빼기로 다시 뗀다.
4. 사용 기능의 트리거(`when`)는 기본 `사용`이다.

> 인스턴스 값 안의 사용 기능(`when`·`then`)은 능력 탭과 같은 트리거·결과 편집기로 그려진다. 결과를 코드로 쓰려면 `[BindTo(인스턴스 ID)]`를 붙인다(그러면 결과 칸은 잠기고 연결된 함수 이름·cs 열기가 보인다). 여러 아이템이 같은 효과를 나눠 쓰면 아이템이 **능력 ID를 가리키게** 하고 그 능력을 DB › 능력에서 조립해도 된다(3-5, 3-6).

### 1-3. 모니터에서 확인하기

능력 사용은 행동이라 모니터 **행동** 탭에 "능력 사용: fireball (hero)"처럼 남고, 거기서 Undo할 수 있다. **타임라인**에서 그 행동을 펼치면 효과로 바뀐 값과 `능력 사용` 이벤트가 이어서 보인다.

---

## 2. 코드로 쓰는 법 — 문법 요소를 하나씩

### 2-1. `Game.Use(능력 ID, 사용자, 대상들…)`

```csharp
GameApi Game => GBibleRuntime.Game;

void OnClickSkill(Unit caster, Unit target) => Game.Use("fireball", caster, target);
void OnClickPotion(Unit hero)               => Game.Use("potion", hero);              // 대상 없이
void OnClickById()                          => Game.Use("fireball", "hero", "orc1", "orc2");   // id 문자열도 된다
```

| 인자 | 뜻 |
|---|---|
| `ability` | 능력 ID 또는 사용 기능이 붙은 인스턴스 ID |
| `user` | 사용자 객체 또는 id 문자열. 객체면 `id` 필드가 있어야 한다 |
| `targets` (`params object[]`) | 고른 대상들(객체 또는 id 문자열) |

- 돌려주는 값은 행동 기록(`ActionRecord`)이다.
- 내부에서 `UseAbility` 행동 하나를 `Game.Do`한다. 다른 행동 안에서 부르면 그 행동의 일부로 그 자리에서 실행된다(Undo 한 단위 = 바깥 행동).
- 실패하면 `InvalidOperationException`을 던지고 **아무것도 바뀌지 않는다**:
  - 없는 ID: "능력 '…'이(가) 없습니다."
  - 사용 조건 거짓: "능력 '…'의 사용 조건을 만족하지 않습니다: (트리거 문장)"

### 2-2. 실행 흐름

1. 행동 시작(Undo 단위 시작).
2. 사용자·대상 id를 객체로 찾는다(플레이어 데이터 전체에서 `id`가 같은 객체).
3. **Trigger 검사**: 사용하는 순간(`IsUse = true`, `User`, `Picked`, `Owner` = 능력 ID)으로 검사한다. 거짓이면 예외 → 행동 중단.
4. **결과를 차례로**:
   - 대상에게 효과: 대상 Block을 위에서부터(앞 결과가 다음 입력, 비면 고른 대상) → 최종 대상마다 효과 Block.
   - 플레이어 데이터에 주기 · 코딩한 결과: 같은 컨텍스트로 실행.
5. **`AbilityUsed` 이벤트**(표시 이름 "능력 사용")를 쏜다: `Ability`(ID), `User`(id), `Targets`(효과를 받은 최종 대상들의 id, 중복 없이), `TargetCount`.
6. 행동 끝 → 바뀐 값 기록, 조건 다시 평가(도전과제 등).

### 2-3. `Game.CanUse` — 쓸 수 있는지만 보기

```csharp
bool ok = Game.CanUse("fireball", caster, target);                     // 대상까지
if (!Game.CanUse("potion", hero, out var why)) potionButton.tooltip = why;   // 이유
bool ok2 = Game.CanUse("fireball", caster, new object[] { target }, out var reason);
```

- Trigger(사용 조건)만 검사하고 아무것도 바꾸지 않는다. 버튼을 미리 흐리게 할 때 쓴다.
- 이유 글자: 없는 ID면 "'…'은(는) 쓸 수 있는 능력·아이템이 아닙니다.", 조건이 거짓이면 "사용 조건을 만족하지 않습니다: (트리거 문장)".

### 2-4. `UseAbility` — 능력 사용 행동(데이터)

```csharp
Game.Do(new UseAbility { ability = "fireball", user = "hero", targets = { "orc1", "orc2" } });   // Game.Use와 같다
```

| 필드 | 뜻 |
|---|---|
| `ability` | 능력 ID |
| `user` | 사용자 id |
| `targets` (`List<string>`) | 고른 대상 id들 |

객체가 아니라 id만 담은 데이터라서, 클라이언트는 이 행동을 서버에 보내고 서버는 같은 행동을 실행하면 된다(리플레이도 같은 데이터로). 효과 Block의 난수는 `Game.Random`을 쓰므로 같은 시드·같은 행동이면 같은 결과가 나오고, Undo하면 난수도 되돌아간다.

### 2-5. `Effect<T>`·`TargetSelector<T>`·`SelfTarget`

```csharp
[Serializable, BlockInfo("피해", Category = "전투", Sentence = "{amount} 피해")]
public class Damage : Effect<Unit>
{
    public int amount = 10;
    public int spread;                                                       // 0 ~ spread 추가 피해
    protected override void Apply(Unit target) => target.hp -= amount + Game.Random("damage").Range(0, spread + 1);
}

[Serializable, BlockInfo("주변 n칸의 적 추가", Category = "대상", Sentence = "주변 {range}칸의 적 추가")]
public class AddEnemiesAround : TargetSelector<Unit>
{
    public int range = 1;
    protected override IEnumerable<Unit> Select(IEnumerable<Unit> targets, Unit user)
    {
        var list = targets.ToList();
        return list.Concat(list.SelectMany(t => Game.All<Unit>().Where(u => u != t && Distance(t, u) <= range)))
                   .Where(u => user == null || u.team != user.team).Distinct();
    }
    static int Distance(Unit a, Unit b) => Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y);
}
```

| 효과 Block(`Effect<T>`)에서 | 뜻 |
|---|---|
| `Apply(T target)` | 대상 하나에 할 일(대상이 `T`가 아니면 불리지 않는다) |
| `User<TUser>()` | 사용자를 형식으로. 예: `var caster = User<Unit>();` |
| `Use.Ability` | 실행 중인 능력 정의(`AbilityDefinition`: `Id`, `DisplayName` …) |
| `Use.User` / `Use.Picked` / `Use.Context` | 사용자 / 고른 대상 / 컨텍스트 |
| `Game` | `GameApi` |

| 대상 Block(`TargetSelector<T>`)에서 | 뜻 |
|---|---|
| `Select(IEnumerable<T> targets, T user)` | 앞 대상들과 사용자를 받아 다음 대상들을 돌려준다(고르기만) |
| `Game`, `Use` | 효과 Block과 같다 |

기본 제공 `SelfTarget`(`사용자 자신`): 고른 대상을 버리고 사용자 하나를 낸다.

기본 제공 효과 Block(→ `28-Attributes-Intercepts.md` 3-1·3-2):

| 효과 | 코드 형식 | 하는 일 |
|---|---|---|
| 능력치 보정 | `AttributeModifierEffect` | 대상의 숫자 필드에 Modifier를 붙인다(`attribute`·`kind`·`value`·`permanent`). 출처 = 이 능력 ID(인스턴스면 인스턴스 ID) |
| 능력치 보정 떼기 | `RemoveModifiersEffect` | 대상에서 그 출처의 Modifier를 모두 뗀다(`source`, 비우면 이 능력 ID) |

### 2-6. `AbilityDefinition` · `session.Abilities` — 코드로 정의하기

```csharp
var fireball = new AbilityDefinition
{
    Id = "fireball",
    DisplayName = "파이어볼",
    When = new AllTrigger { triggers = { new UseTrigger(), new ConditionTrigger { condition = new UserAlive() } } },
};
fireball.Outcomes.Add(new TargetedOutcome
{
    targets = { new AddEnemiesAround { range = 1 } },
    effects = { new Damage { amount = 10 } },
});
session.Abilities.Define(fireball);
```

| 멤버 | 뜻 |
|---|---|
| `Id` | 필수 |
| `DisplayName` | 표시 이름(없으면 ID) |
| `When` | Trigger. `null`이면 언제나 쓸 수 있다 |
| `Outcomes` | 결과 |
| `Targets` / `Effects` | 짧은 형태: 결과 앞에 "대상에게 효과" 하나로 실행된다 |

| `session.Abilities` | 뜻 |
|---|---|
| `Define(definition)` | 등록. 정의를 **복사**해 둔다(Block 인스턴스가 세션마다 따로라 여러 세션이 섞이지 않는다). 등록한 뒤 원본을 고쳐도 반영되지 않는다. 그 ID에 `[BindTo]`가 있으면 결과가 그 코드로 바뀐다 |
| `Get(id)` / `All` | 찾기 / 전체 |

게임에서는 런타임이 시작할 때 DB의 능력 전부와, 사용 기능이 붙은 인스턴스 전부를 등록한다.

### 2-7. `Usable` — 클래스에 붙이는 사용 기능

```csharp
using System;
using GBible;
using UnityEngine;

[Serializable]
public class Item
{
    public string id;
    public string name;
    [SerializeReference] public Usable usable;      // null = 쓸 수 없는 아이템
}
```

문법 요소를 차례로:

1. **필드 형식은 정확히 `Usable`**. 런타임이 시작할 때 각 인스턴스 값에서 이 형식의 필드를 찾는다(부모 클래스 필드 포함, 형식마다 한 번만 찾는다).
2. **`[SerializeReference]`** — 붙여야 인스턴스 편집에서 "기능 필드"(비어 있을 수 있는 필드)가 되고 `+ 기능`으로 붙일 수 있다.
3. **`null`이면 기능 없음.** 게임 코드는 `if (item.usable is { } use) …`로 확인한다.
4. `Usable`의 칸:
   - `when`(`Trigger`, 기본 `new UseTrigger()`, 잠금 `사용 · 조건 충족`)
   - `then`(`List<Outcome>`, 잠금 `대상에게 효과 · 플레이어 데이터에 주기 · 코딩한 결과`)
5. 등록 ID = **인스턴스 ID**(DB › 인스턴스 항목의 ID). `Item.id` 필드 값과는 상관없다.
6. 코드로 능력 정의를 만들려면 `usable.ToDefinition(id, displayName)`.

개수 줄이기 같은 뒤처리는 게임 코드가 한다(결과 안에서 하면 같은 Undo 단위가 된다. 3-4).

### 2-8. 사용 조건 Block

```csharp
[Serializable, BlockInfo("사용자 체력 있음", Category = "사용")]
public class UserAlive : SimpleCondition
{
    protected override bool Check() => Context?.User is Unit u && u.hp > 0;
}
```

`Context.User`·`Context.Picked`·`Context.Owner`(능력·인스턴스 ID)를 본다. `Game.Use`와 `Game.CanUse` 모두 이 조건으로 판정한다.

---

## 3. 상황별 예제

예제는 다음 형식을 쓴다(DB › 플레이어 데이터에 `battle`(Battle), `party`(Party)가 있다고 본다).

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using GBible;
using UnityEngine;

[Serializable] public class Unit { public string id; public string team; public int x, y; public int hp = 30, maxHp = 30, mana = 10; }
[Serializable] public class Battle { public List<Unit> units = new List<Unit>(); public int turn; }
[Serializable] public class Party { public int gold; public Dictionary<string, int> bag = new Dictionary<string, int>(); }   // 인스턴스 ID → 개수
[Serializable] public class Item { public string id; public string name; [SerializeReference] public Usable usable; }
```

### 3-1. 스플래시: 고른 적 + 주변 1칸의 적에게 10 피해

**에디터**(DB › 능력 `splash`): `trigger` = `사용` / `outcomes` = `대상에게 효과`(대상 `주변 n칸의 적 추가` range 1, 효과 `피해` amount 10).

**게임 코드**: `Game.Use("splash", hero, orc1);`

결과: `orc1`과 그 주변 1칸의 적(같은 팀 제외)의 체력이 10 줄고, `AbilityUsed.Targets`에 그 id들이 담긴다. `Game.Session.Undo()` 한 번에 모두 돌아간다.

### 3-2. 자가 회복

**에디터**: `outcomes` = `대상에게 효과` → 대상 `사용자 자신` → 효과 `체력 회복`(→ `12-Outcomes.md` 3-5의 `Heal`, amount 15).

```csharp
var heal = new AbilityDefinition { Id = "selfHeal" };
heal.Targets.Add(new SelfTarget());
heal.Effects.Add(new Heal { amount = 15 });     // 짧은 형태
session.Abilities.Define(heal);
session.Game.Use("selfHeal", "hero");
```

### 3-3. 마나를 쓰는 능력: 조건 + 소모 + 피해

```csharp
[Serializable, BlockInfo("사용자 마나", Category = "사용", Sentence = "사용자 마나 {cost} 이상")]
public class UserManaAtLeast : SimpleCondition
{
    public int cost = 3;
    protected override bool Check() => Context?.User is Unit u && u.mana >= cost;
}

[Serializable, BlockInfo("마나 소모", Category = "전투", Sentence = "마나 {cost} 소모")]
public class SpendMana : Effect<Unit>
{
    public int cost = 3;
    protected override void Apply(Unit target) => target.mana -= cost;
}
```

**에디터**(DB › 능력 `fireball`):

- `trigger`: `사용` AND `사용자 마나`(cost 3)
- `outcomes`:
  1. `대상에게 효과` → 대상 `사용자 자신` → 효과 `마나 소모`(cost 3)
  2. `대상에게 효과` → 대상 비움(고른 대상) → 효과 `피해`(amount 12)

마나가 모자라면 `Game.Use`는 예외로 실패하고 아무것도 바뀌지 않는다. UI는 `Game.CanUse("fireball", caster, out var why)`로 미리 흐리게 한다.

### 3-4. 포션(사용 기능 + `[BindTo]`)

1. 코드: 위의 `Item` 클래스.
2. DB › 인스턴스 `potion`: 형식 `Item`, `+ 기능` → `usable`.
3. 결과를 코드로:

```csharp
public static class ItemUses
{
    [BindTo("potion")]
    static void Potion(TriggerContext c)
    {
        var bag = c.Session.Data.Get<Party>("party").bag;
        if (!bag.TryGetValue(c.Owner, out var count) || count <= 0)
            throw new InvalidOperationException("포션이 없습니다.");   // 행동이 중단되고 아무것도 바뀌지 않는다
        bag[c.Owner] = count - 1;
        if (c.User is Unit u) u.hp = Math.Min(u.maxHp, u.hp + 30);
    }
}
```

4. 게임 코드: `Game.Use("potion", hero);` — 회복과 개수 줄이기가 한 행동이라 Undo 한 번에 함께 돌아간다.

결과 코드에서 예외를 던지면 그 사용 행동 전체가 취소된다. 단, `Game.CanUse`는 Trigger만 보므로 이 검사를 모른다. 버튼을 미리 흐리게 하려면 가방 개수를 UI 코드가 직접 보거나 3-6의 방식으로 사용 조건 Block을 쓴다.

### 3-5. 아이템이 능력을 가리키게 하기(에디터에서 전부 조립)

```csharp
using GBible.Data;

[Serializable]
public class Consumable
{
    public string id;
    [DataRef(typeof(AbilityData))] public string ability;   // DB › 능력 목록에서 고르는 칸
    public int maxStack = 10;
}

// 게임 코드: 가방에서 아이템을 쓸 때
void UseItem(Consumable item, Unit user, params object[] targets) => Game.Use(item.ability, user, targets);
```

능력 쪽은 1-1처럼 트리거(사용 조건 포함)·결과를 DB에서 조립한다. 같은 능력을 여러 아이템이 함께 쓸 수 있다.

### 3-6. 가방에 있을 때만 쓸 수 있는 능력

```csharp
[Serializable, BlockInfo("가방에 있음", Category = "사용", Sentence = "가방에 {item} 있음")]
public class InBag : SimpleCondition
{
    public string item;    // 인스턴스 ID(비우면 이 능력·인스턴스 ID)
    protected override bool Check()
    {
        var id = string.IsNullOrEmpty(item) ? Context?.Owner : item;
        var party = Game.All<Party>().FirstOrDefault();
        return party != null && id != null && party.bag.TryGetValue(id, out var n) && n > 0;
    }
}
```

능력 `drinkPotion`: `trigger` = `사용` AND `가방에 있음`(item `potion`), `outcomes` = `대상에게 효과`(사용자 자신 → 체력 회복 30) + `플레이어 데이터에 주기 · party` › `bag`(키와 값 넣기: `potion` → `-1`). 코드 없이 조립되고 `Game.CanUse`도 개수를 안다.

### 3-7. 사용 버튼 상태

```csharp
public class SkillButton : MonoBehaviour
{
    public string abilityId;
    public UnityEngine.UI.Button button;
    string _casterId;

    public void Bind(string casterId) => _casterId = casterId;

    void Update()
    {
        var game = GBibleRuntime.Game;
        if (game == null || _casterId == null) return;
        button.interactable = game.CanUse(abilityId, _casterId, out _);
    }

    public void OnClick(Unit target)
    {
        try { GBibleRuntime.Game.Use(abilityId, _casterId, target); }
        catch (InvalidOperationException e) { Debug.Log(e.Message); }
    }
}
```

(사용자는 오래 들고 있지 말고 id로 들고 있는다. Undo·불러오기 뒤에는 다른 인스턴스일 수 있다.)

### 3-8. 능력 사용 연출

```csharp
IDisposable _sub = GBibleRuntime.Game.Events.Subscribe<AbilityUsed>(e =>
{
    foreach (var id in e.Targets) PlayHitEffect(id);        // 효과를 받은 최종 대상들
});
```

### 3-9. 결과를 함수로: 고른 대상에게 튕기는 번개

```csharp
[BindTo("chainLightning")]
static void ChainLightning(TriggerContext c)
{
    var damage = 20;
    foreach (var t in c.Picked.OfType<Unit>()) { t.hp -= damage; damage = damage * 7 / 10; }
}
```

DB › 능력 `chainLightning`의 트리거는 에디터에서 그대로 정하고(사용 조건 포함), 결과만 코드다.

### 3-10. 버프: 사용자 공격력 +20%(코드 없음)

`Unit`에 숫자 필드 `public double attack = 10;`이 있다고 본다.

**에디터**:

- DB › 능력 `rage`: `outcomes` = `대상에게 효과` → 대상 `사용자 자신` → 효과 `능력치 보정`(attribute `attack`, kind `Percent`, value `0.2`). 요약: `사용자 자신: attack +20%`.
- DB › 능력 `calm`: `outcomes` = `대상에게 효과` → 대상 `사용자 자신` → 효과 `능력치 보정 떼기`(source `rage`).

**게임 코드**: `Game.Use("rage", hero);` → `Game.Attribute(hero, nameof(Unit.attack))`가 12가 된다(`hero.attack` 필드는 10 그대로). 전투가 끝날 때 한꺼번에 떼려면 그 행동 안에서 `Game.RemoveModifiers("rage");`.

- 출처가 능력 ID(`rage`)라서 두 번 쓰면 +20%가 두 줄 붙어 +40%가 되고, 떼기는 둘 다 뗀다. 겹치지 않게 하려면 `rage`의 트리거에 사용 조건 Block을 AND로 둔다(예: `Check() => Context?.User is Unit u && Game.Modifiers(u).All(m => m.source != "rage")`).
- 사용이 행동이라 Undo 한 번에 Modifier도 돌아간다. 계산 규칙·영구 보정·코드 API는 `28-Attributes-Intercepts.md`.

---

## 4. 주의할 점 / 자주 하는 실수

- **대상·사용자에 `id`가 없다.** 사용자·대상은 id로 실려 다니고, 끝날 때 최종 대상의 id로 `능력 사용` 이벤트를 쏜다. `public string id;`가 없는 객체가 최종 대상이 되면 예외로 사용 전체가 취소된다.
- **id가 겹친다.** 사용자·대상은 형식 없이 id만으로 플레이어 데이터 전체에서 찾는다. 서로 다른 형식이라도 같은 id가 있으면 먼저 찾은 것이 쓰인다. 소환 개체 등은 `Game.NewId("unit")`로 겹치지 않게 만든다.
- **사용 기능을 인스턴스의 `id` 필드로 부른다.** `Game.Use`의 ID는 DB › 인스턴스 항목의 ID다. 가방에는 인스턴스 ID를 담아 두는 것이 편하다.
- **사용 기능 필드의 형식이 다르다.** `Usable`을 상속한 형식이나 `object` 필드는 찾지 않는다(필드 형식이 정확히 `Usable`이어야 한다). 인스턴스 값의 맨 위 필드만 본다.
- **사용 조건을 결과 코드의 예외로만 둔다.** 동작은 하지만 `Game.CanUse`가 모른다. 미리 보여야 하는 조건은 Trigger에 조건 Block으로 둔다.
- **`Define` 뒤에 정의를 고친다.** 능력 정의는 등록할 때 복사된다. 바꾸려면 다시 `Define`한다.
- **사용자 없이 `사용자 자신`을 쓴다.** 사용자가 `null`이거나 찾지 못하면 `사용자 자신`은 아무도 고르지 않는다.
- **도전과제의 결과에서 고른 대상을 기대한다.** 고른 대상(`Picked`)은 `Game.Use`로 쓸 때만 있다.
