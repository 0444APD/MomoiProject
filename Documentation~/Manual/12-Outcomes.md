# 12. 결과(Outcome)

**한 줄 요약**: Outcome은 Trigger가 발동했을 때 **무엇을 하는지**다. 결과 칸은 Outcome의 리스트이고, 위에서부터 차례로 실행된다. 종류는 다섯이다: **로그에 기록** · **플레이어 데이터에 주기** · **대상에게 효과** · **이벤트 쏘기** · **코딩한 결과**.

**언제 쓰는가**

- 숫자·글자·리스트처럼 플레이어 데이터의 필드 하나를 정해진 값만큼 바꿀 때 → 플레이어 데이터에 주기(코드 없이 에디터에서).
- 유닛 같은 개체를 골라 그 개체를 바꿀 때 → 대상에게 효과(대상 Block + 효과 Block. Block은 프로그래머가 코드로 만들고, 기획자가 조립·수치를 정한다).
- 그 밖의 고유 로직 → 코딩한 결과(`Reward` 상속), 또는 항목 하나에 함수를 연결하는 `[BindTo]`(→ `13-BindTo.md`).
- 로그 값을 올릴 때 → 로그에 기록.
- 이벤트를 정해진 값으로 쏴서 그 이벤트를 받는 로그·도전과제·트리거를 반응시킬 때 → 이벤트 쏘기(주로 DB › 개발 › 개발 명령에서 "한 턴 30 피해" 같은 시험 버튼으로).

> 결과에는 "아무 개체를 골라 아무 필드를 바꾸는" 범용 블럭이 없다. 개체를 고르고 바꾸는 로직은 프로그래머가 대상 Block·효과 Block·코딩한 결과·`[BindTo]` 함수로 쓴다.

이 문서에서 쓰는 용어:

| 용어 | 뜻 |
|---|---|
| Outcome | 결과 하나. 코드 형식 `Outcome`(추상). 결과 칸 = `List<Outcome>`. |
| Trigger | 언제 발동하는지(→ `11-Triggers-Conditions.md`). |
| Block | 에디터에서 골라 조립하는 로직 단위(조건·결과·효과·대상). |
| TriggerContext (컨텍스트) | 발동한 순간의 정보(이벤트·사용자·고른 대상·항목 ID). 결과 코드가 읽는다. |
| 인스턴스 | DB › 인스턴스에서 만든 클래스 객체 원본(아이템 A, 몬스터 B …). 결과는 원본이 아니라 깊은 사본을 넣는다. |
| Undo | 마지막 행동 되돌리기. |

---

## 1. 결과 종류와 기능별 잠금

| 종류 | 화면 이름 | 코드 형식 | `OutcomeKinds` |
|---|---|---|---|
| 로그에 기록 | 로그에 기록 | `RecordOutcome` | `Record` |
| 플레이어 데이터에 주기 | (필드를 고르면 한 줄) | `PlayerDataReward` | `PlayerData` |
| 대상에게 효과 | 대상에게 효과 | `TargetedOutcome` | `Targeted` |
| 이벤트 쏘기 | 이벤트 쏘기 | `PublishEventOutcome` | `Event` |
| 코딩한 결과 | `[BlockInfo]` 이름 | `Reward` 상속 클래스 | `Coded` |

| 기능 | 고를 수 있는 결과 |
|---|---|
| 로그 | 로그에 기록 하나로 고정(세부 칸만) |
| 도전과제 | 전부(`OutcomeKinds.All`) |
| 개발 명령(DB › 개발) | 전부(`OutcomeKinds.All`) |
| 능력 · 사용 기능 | 대상에게 효과 · 플레이어 데이터에 주기 · 코딩한 결과 |
| 프로젝트 기능 | 필드의 `[OutcomeSlot]`이 정한다(`All`이면 이벤트 쏘기도) |

---

## 2. 에디터에서 쓰는 법

결과 칸(`outcomes`, `then` 등. 칸 이름표는 코드의 변수 이름, 마우스를 올리면 "결과")은 결과 목록 편집기로 그려진다.

### 2-1. 결과 더하기

1. **`+ 결과 추가`**를 누른다. 검색 가능한 목록이 뜬다(칸의 잠금에 있는 것만).
   - 그룹 **결과**: `로그에 기록`, `대상에게 효과`, `이벤트 쏘기`
   - 그룹 **플레이어 데이터에 주기 · (플레이어 데이터 ID)**: 그 플레이어 데이터에서 줄 수 있는 필드들. 오른쪽 작은 글씨가 주는 방식이다(`숫자 · 증감`, `… · 값`, `… · 인스턴스`, `… · 요소 넣기`, `… · 키와 값 넣기`). 이미 고른 필드는 다시 나오지 않는다.
   - 그룹 **코딩한 결과**: `Reward`를 상속한 프로젝트 클래스들(`[BlockInfo]` 이름).
2. 고르면 한 줄이 생긴다. 줄 오른쪽 `✕`로 지운다.
3. 고정 칸(로그)은 `+ 결과 추가`가 없고, 로그에 기록 한 줄이 자동으로 들어 있다.

### 2-2. 플레이어 데이터에 주기 한 줄

왼쪽은 고른 필드(`party › gold`)와 주는 방식, 오른쪽은 값 칸이다. 값 칸은 필드 형식이 정한다.

| 필드 형식 | 값 칸 | 실행 |
|---|---|---|
| 숫자 | `증감`(음수면 감소) | 지금 값 + 증감. 정수 형식이면 반올림 |
| 글자 · 참거짓 · enum | 값(참/거짓·enum·키워드가 연결된 글자는 목록에서) | 그 값으로 정한다 |
| 클래스 | `인스턴스 선택`(그 형식의 인스턴스 목록) | 인스턴스의 깊은 사본으로 바꾼다 |
| 리스트 · 배열 · 그 밖의 컬렉션 | 요소 칸들 + `+` | 요소를 끝에 넣는다(요소마다 숫자·값·인스턴스 규칙). 배열은 늘린 새 배열로 바꾼다 |
| 딕셔너리 | `[키] → [값]` 줄들 + `+` | 숫자 값은 있던 값에 더한다(없던 키는 0에서). 그 밖의 값·인스턴스는 덮어쓴다 |

- 고를 수 있는 필드: 값 자체(줄 수 있는 형식이면) + 클래스 안의 필드(세 단계까지). 리스트 **안쪽**의 필드(`gear[].power`)는 고를 수 없다.
- 리스트의 숫자 요소는 증감이 아니라 그 숫자를 **넣는다**.
- 줄 수 없는 형식(에셋 참조, 요소가 또 컬렉션인 컬렉션, 구조체 값)은 목록에 나오지 않는다.

### 2-3. 로그에 기록 한 줄

| 칸 | 뜻 |
|---|---|
| `로그` | 반영할 로그("비우면 이 결과를 가진 로그"). 로그 탭의 고정 칸에서는 숨긴다 |
| `반영할 값` | 같은 규칙 Trigger의 이벤트에서 읽을 숫자 필드("비우면 횟수 1") |
| `배수` | 곱할 값("예: 2배로 세기, 음수로 빼기") |

로그의 집계 방식(합계·최고치·최저치·마지막 값)을 따른다(→ `15-Stats-Logs.md`).

### 2-4. 대상에게 효과

`대상에게 효과`를 더하면 Block 편집기가 열린다.

1. `targets`(툴팁 "대상") 칸의 **`+ 대상 추가`**로 대상 Block을 더한다. 위에서부터 차례로 실행되고, 앞 Block의 결과가 다음 Block의 입력이다. 비어 있으면 사용할 때 고른 대상 그대로다.
2. `effects`(툴팁 "효과") 칸의 **`+ 효과 추가`**로 효과 Block을 더한다. 최종 대상마다 모든 효과를 실행한다.
3. Block의 칸(수치)을 채운다.

기본 제공 대상 Block은 `사용자 자신`(`SelfTarget`) 하나다. 나머지 대상·효과는 프로젝트가 만든다(3-5, 3-6).

### 2-4b. 이벤트 쏘기

고른 이벤트를 적은 값으로 쏜다. 그 이벤트를 받는 로그·도전과제·트리거·코드가 게임에서 쏜 것과 똑같이 반응한다.

| 칸 | 뜻 |
|---|---|
| `이벤트` | 쏠 이벤트(`[GameEvent]` 목록에서 고르기) |
| `값` | 줄마다 **필드**(경로) = **값**. 숫자·글자·참거짓·enum 필드만. 경로 `a.b`로 안쪽 필드도. 적지 않은 필드는 이벤트 클래스의 기본값 |
| `횟수` | 몇 번 쏠지(1 이상) |

- 요약 문장: `이벤트 쏘기: 턴 끝 (turn = 1, damage = 30)`, 2번 이상이면 `×횟수`.
- 검증: 이벤트를 고르지 않음·찾을 수 없음, 이벤트에 없는 필드 경로, 형식에 맞지 않는 값("'damage'에 'abc'를 넣을 수 없습니다(Int32)."), 횟수 0 이하를 오류로 띄운다.
- 실행 중 이벤트 형식을 찾지 못하거나 값을 넣지 못하면 그 줄은 건너뛰고 Journal에 경고(출처 `Events`)를 남긴다.
- 주로 DB › 개발 › 개발 명령에서 쓴다. 예: "한 턴 30 피해" = 이벤트 `TurnEnded`, 값 `turn = 1`, `damage = 30`(→ `22-Monitor-Debug.md` 7절).

### 2-5. 요약과 검증

도전과제·능력은 상세 위쪽 요약에 "결과 …"가 자연어로 보인다(예: `party › gold 100 증가`). 검증은 없는 플레이어 데이터·필드, 형식에 맞지 않는 값, 비어 있는 인스턴스·요소, 잠금 밖 종류, 읽을 이벤트가 없는 반영할 값을 오류로 띄운다.

---

## 3. 코드로 쓰는 법 — 문법 요소를 하나씩

### 3-1. `PlayerDataReward` — 코드에서 직접 만들기

```csharp
var gold  = new PlayerDataReward { key = "party", path = "gold", amount = 100 };            // 숫자 증감
var title = new PlayerDataReward { key = "party", path = "titles", items = { "용사" } };      // 리스트에 넣기
var mats  = new PlayerDataReward { key = "party", path = "materials", keys = { "iron", "wood" }, items = { "3", "1" } }; // 딕셔너리
var sword = new PlayerDataReward { key = "party", path = "gear", items = { "sword" } };       // 리스트에 인스턴스 사본 넣기(인스턴스 ID)
```

| 필드 | 뜻 |
|---|---|
| `key` | 플레이어 데이터 ID |
| `path` | 필드 경로(비우면 값 전체) |
| `amount` | 숫자 증감 |
| `value` | 글자·참거짓·enum 값, 또는 클래스 필드면 인스턴스 ID |
| `items` | 리스트에 넣을 요소들(숫자·값·인스턴스 ID를 글자로), 딕셔너리면 값들 |
| `keys` | 딕셔너리 키들(`items`와 같은 순서) |

실행할 때 문제가 있으면(없는 플레이어 데이터, 없는 인스턴스, 읽을 수 없는 값) 그 줄은 건너뛰고 Journal에 경고(출처 `Reward`, "플레이어 데이터에 주기(…): …")를 남긴다.

### 3-2. `Reward` — 코딩한 결과 Block

```csharp
using System;
using GBible;

[Serializable, BlockInfo("골드 지급", Category = "보상", Description = "파티 골드를 늘립니다.", Sentence = "골드 {amount}")]
public class GiveGold : Reward
{
    public int amount = 100;

    public override RewardEffect Effect => RewardEffect.Reversible;
    public override void Grant(GameSession session) => session.Data.Get<Party>("party").gold += amount;
    public override string Describe(INameResolver names) => $"골드 +{amount}";
}
```

문법 요소를 차례로:

1. **`[Serializable]`** — 필수(DB 에셋에 저장된다).
2. **`[BlockInfo("표시 이름", Category, Description, Sentence)]`** — 필수. `+ 결과 추가` › 코딩한 결과 목록의 이름. `Sentence`의 `{필드이름}`은 누르면 바뀌는 칸.
3. **`: Reward`** — 기반 클래스(`Outcome`을 상속한다. `Kind`는 `Coded`).
4. **public 필드** — 에디터 칸. 항목마다 값이 다르다.
5. **`public override void Grant(GameSession session)`** — 할 일. 꺼낸 객체의 필드를 그냥 고친다(바뀐 곳은 시스템이 찾아 기록한다).
6. **`public override RewardEffect Effect`** — 영향 분류. `Reversible`(플레이어 데이터 안에서만 일어남) / `Irreversible`(게임 밖: 결제·플랫폼 전송). 지금은 분류 표시이며, 어느 쪽이든 실행 방식은 같다.
7. **`public override string Describe(INameResolver names)`** — 필수. 코드 형태 문장(Alt, Journal).
8. (선택) **`Natural(INameResolver)`** — 자연어 요약. 없으면 `Describe`.
9. (선택) **`Validate(IValidationContext context)`** — 에디터 검증. `context.Error("…")`는 빌드를 막고, `context.Warning("…")`은 표시만.
10. **`Context`**(protected) — 발동한 순간의 `TriggerContext`. Trigger 밖에서 직접 `Grant`를 부르면 `null`.

### 3-3. `TriggerContext` — 결과가 읽는 순간 정보

| 멤버 | 뜻 |
|---|---|
| `Session` | 세션 |
| `Game` | `Session.Game`(`GameApi`) |
| `Owner` | 결과를 가진 항목의 ID(도전과제·로그·능력·인스턴스 …) |
| `Event` | 이벤트를 받은 순간이면 그 이벤트(아니면 `null`) |
| `IsUse` | 사용하는 순간이면 `true` |
| `User` | 사용자(사용하는 순간) |
| `Picked` | 플레이어가 고른 대상들 |

```csharp
// 처치 이벤트의 exp만큼 파티 골드
[Serializable, BlockInfo("처치 보상 골드", Category = "보상")]
public class GoldFromKill : Reward
{
    public override RewardEffect Effect => RewardEffect.Reversible;
    public override string Describe(INameResolver names) => "처치 exp만큼 골드";
    public override void Grant(GameSession session)
    {
        if (Context?.Event is UnitKilled e) session.Data.Get<Party>("party").gold += e.exp;
    }
}
```

### 3-4. `TargetedOutcome` — 코드에서 만들기

```csharp
var heal = new TargetedOutcome
{
    targets = { new SelfTarget() },
    effects = { new Heal { amount = 30 } },
};
```

| 필드 | 뜻 |
|---|---|
| `targets` (`List<TargetSelector>`) | 대상 Block들. 차례로 이어진다. 비면 고른 대상(`Picked`) 그대로 |
| `effects` (`List<Effect>`) | 효과 Block들. 최종 대상마다 실행. 효과의 대상 형식과 다른 대상은 건너뛴다 |

### 3-5. `Effect<T>` — 효과 Block("무엇을")

```csharp
[Serializable, BlockInfo("체력 회복", Category = "전투", Sentence = "체력 {amount} 회복")]
public class Heal : Effect<Unit>
{
    public int amount = 10;
    protected override void Apply(Unit target) => target.hp = Math.Min(target.maxHp, target.hp + amount);
}
```

| 물려받는 것 | 뜻 |
|---|---|
| `protected abstract void Apply(T target)` | 대상 하나에 할 일. 대상이 `T`가 아니면 불리지 않는다 |
| `Game` | 지금 세션의 `GameApi` |
| `User<TUser>()` | 사용자를 형식으로(없거나 형식이 다르면 `null`) |
| `Use` (`AbilityUse`) | 이번 실행 정보: `Use.Ability`(능력에서 실행 중이면 그 정의, 아니면 `null`), `Use.User`, `Use.Picked`, `Use.Context`(`TriggerContext`) |
| `Describe()` | 요약 문장(기본 = `[BlockInfo]` 이름) |

### 3-6. `TargetSelector<T>` — 대상 Block("누구에게")

```csharp
[Serializable, BlockInfo("팀의 n번 자리", Category = "대상", Sentence = "{team} {slot}번 자리")]
public class UnitAtSlot : TargetSelector<Unit>
{
    public string team = "enemy";
    public int slot = 1;

    protected override IEnumerable<Unit> Select(IEnumerable<Unit> targets, Unit user)
    {
        var battle = Game.All<Battle>().FirstOrDefault();      // 지금 전투
        if (battle == null) return Enumerable.Empty<Unit>();
        return battle.units.Where(u => u.team == team && u.slot == slot && u.hp > 0);
    }
}
```

| 물려받는 것 | 뜻 |
|---|---|
| `protected abstract IEnumerable<T> Select(IEnumerable<T> targets, T user)` | 앞 단계 대상(`T`만 걸러서)과 사용자(`T`가 아니거나 없으면 `null`)를 받아 다음 대상을 돌려준다. **고르기만 하고 값은 바꾸지 않는다** |
| `Game`, `Use` | 효과 Block과 같다 |
| `Describe()` | 요약 문장 |

- 기본 제공 `SelfTarget`("사용자 자신"): 앞 대상을 버리고 사용자 하나를 낸다.
- 도전과제·로그·퀘스트처럼 **사용이 아닌** Trigger에서는 사용자와 고른 대상이 없다. 대상 Block이 `Game`으로 직접 찾거나 `Use.Context.Event`에서 찾아야 한다(4-6).

### 3-7. 결과가 실행되는 방식

- 결과 칸의 결과들은 위에서부터 차례로 실행된다. 로그에 기록을 뺀 결과는 실행마다 Journal에 "결과: (자연어 요약)" 한 줄을 남긴다(출처: `Achievements`·`Stats`·`Abilities` 등. 능력·사용 기능의 대상에게 효과는 이 줄 없이 능력 사용 행동의 기록에 묶인다).
- 결과 안에서 꺼낸 플레이어 데이터 객체를 그냥 고쳐도 기록된다(Undo·저장·변경 전파).
- 행동 안에서 발동했으면 그 변화는 그 행동의 Undo 단위다. 단, **도전과제의 결과는 Undo 이력에 넣지 않는다**(→ `14-Achievements.md`).

---

## 4. 상황별 예제

예제는 다음 형식을 쓴다(DB › 플레이어 데이터에 `party`(Party, 메타 구역), `battle`(Battle, 세션 구역)이 있다고 본다).

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using GBible;

[Serializable] public class Unit { public string id; public string team; public int slot; public int hp = 30, maxHp = 30, exp; }
[Serializable] public class Battle { public List<Unit> units = new List<Unit>(); public int turn; }
[Serializable] public class Gear { public string name; public string grade; public int power; }
[Serializable] public class Party
{
    public int gold;
    public List<string> titles = new List<string>();
    public List<Gear> gear = new List<Gear>();
    public Dictionary<string, int> materials = new Dictionary<string, int>();
}

[GameEvent("유닛 처치")]
public class UnitKilled { public string killerId, victimId; public int exp, combo; public List<string> tags = new List<string>(); }
```

### 4-1. 플레이어 골드 수정

**에디터**: `+ 결과 추가` → `플레이어 데이터에 주기 · party` › `gold`(숫자 · 증감) → 증감 `500`. 줄이려면 `-500`.

**코드(조립)**: `new PlayerDataReward { key = "party", path = "gold", amount = 500 }`.

**코드(로직)**: 값이 계산으로 정해지면 3-2의 `GiveGold`처럼 `Reward`를 쓴다. 예: 지금 골드의 10%.

```csharp
[Serializable, BlockInfo("골드 이자", Category = "보상", Sentence = "골드 {percent}% 이자")]
public class GoldInterest : Reward
{
    public int percent = 10;
    public override RewardEffect Effect => RewardEffect.Reversible;
    public override string Describe(INameResolver names) => $"골드 +{percent}%";
    public override void Grant(GameSession session)
    {
        var party = session.Data.Get<Party>("party");
        party.gold += party.gold * percent / 100;
    }
}
```

### 4-2. 칭호·장비·재료 주기(에디터만)

| 하고 싶은 것 | 고르는 필드 | 값 칸 |
|---|---|---|
| 칭호 "용사" 추가 | `party › titles` (요소 넣기) | `+` → `용사` |
| 검 인스턴스 하나 지급 | `party › gear` (Gear 리스트 · 요소 넣기) | `+` → 인스턴스 `sword` |
| 철 3개·나무 1개 | `party › materials` (키와 값 넣기) | `+` → `iron` → `3`, `+` → `wood` → `1` |
| 철 1개 소모 | `party › materials` | `iron` → `-1` |

인스턴스를 줄 때마다 원본의 **깊은 사본**이 들어가므로, 두 번 줘도 서로 다른 객체다. 원본(DB › 인스턴스)은 바뀌지 않는다.

### 4-3. 아군 하수인 3번의 체력 증가

**코드(Block 두 개, 한 번만 만든다)**: 3-6의 `UnitAtSlot`, 3-5의 `Heal`.

**에디터**: `+ 결과 추가` → `대상에게 효과` → `+ 대상 추가` → `팀의 n번 자리`(team `ally`, slot `3`) → `+ 효과 추가` → `체력 회복`(amount `20`).

요약 줄에는 Block 이름이 이어져 보인다: `팀의 n번 자리: 체력 회복`(Block의 `Describe()`를 고치면 수치까지 보이게 할 수 있다).

### 4-4. 현재 전투의 적 2번의 체력 깎기

```csharp
[Serializable, BlockInfo("피해", Category = "전투", Sentence = "{amount} 피해")]
public class Damage : Effect<Unit>
{
    public int amount = 10;
    protected override void Apply(Unit target) => target.hp = Math.Max(0, target.hp - amount);
}
```

**에디터**: `대상에게 효과` → 대상 `팀의 n번 자리`(team `enemy`, slot `2`) → 효과 `피해`(amount `15`).

같은 `UnitAtSlot`·`Damage`로 "적 1번에 10, 적 3번에 5"처럼 결과 줄을 여러 개 둘 수 있다.

### 4-5. 고른 대상 + 주변 적에게 피해(능력)

```csharp
[Serializable, BlockInfo("주변 n칸의 적 추가", Category = "대상", Sentence = "주변 {range}칸의 적 추가")]
public class AddEnemiesAround : TargetSelector<Unit>
{
    public int range = 1;
    protected override IEnumerable<Unit> Select(IEnumerable<Unit> targets, Unit user)
    {
        var picked = targets.ToList();
        var battle = Game.All<Battle>().First();
        var around = battle.units.Where(u => picked.Any(p => Math.Abs(p.slot - u.slot) <= range));
        return picked.Concat(around).Where(u => user == null || u.team != user.team).Distinct();
    }
}
```

능력의 결과: `대상에게 효과` → 대상 `주변 n칸의 적 추가`(range 1) → 효과 `피해`(10). 게임 코드: `Game.Use("splash", hero, orc)`.

### 4-6. 처치 이벤트를 받아 처치한 유닛에게 경험치

도전과제("첫 처치" 등)처럼 이벤트로 발동한 결과에서, 그 이벤트가 가리키는 개체를 대상으로 삼는다.

```csharp
[Serializable, BlockInfo("처치한 유닛", Category = "대상", Description = "받은 유닛 처치 이벤트의 처치한 유닛입니다.")]
public class KillerOfEvent : TargetSelector<Unit>
{
    protected override IEnumerable<Unit> Select(IEnumerable<Unit> targets, Unit user)
    {
        if (Use.Context?.Event is UnitKilled e && Game.Find<Unit>(e.killerId) is Unit killer) yield return killer;
    }
}

[Serializable, BlockInfo("경험치", Category = "성장", Sentence = "경험치 {amount}")]
public class GainExp : Effect<Unit>
{
    public int amount = 10;
    public bool addEventExp;   // 켜면 처치 이벤트의 exp도 더한다
    protected override void Apply(Unit target)
    {
        target.exp += amount;
        if (addEventExp && Use.Context?.Event is UnitKilled e) target.exp += e.exp;
    }
}
```

**에디터**: 도전과제 트리거 `유닛 처치 받기` → 결과 `대상에게 효과` → 대상 `처치한 유닛` → 효과 `경험치`(amount 50).

같은 일을 함수 하나로 쓰려면 `[BindTo]`가 더 짧다(→ `13-BindTo.md` 3-1).

> 매번 일어나는 게임 규칙("모든 처치에 경험치")은 처치를 처리하는 행동 코드에 바로 쓰는 것이 자연스럽다. Trigger·결과는 데이터로 고르고 바꾸고 싶은 단위(도전과제·퀘스트·아이템·능력)에 쓴다.

### 4-7. 결과에서 이벤트 쏘기

값이 정해져 있으면 코드 없이 **이벤트 쏘기** Block(2-4b)을 쓴다: `+ 결과 추가` → `이벤트 쏘기` → 이벤트 `칭호 획득`, 값 `title = 부자`.

값을 계산해야 하면 코딩한 결과로 쓴다.

```csharp
[Serializable, BlockInfo("칭호 획득 연출", Category = "연출")]
public class TitleFanfare : Reward
{
    public string title;
    public override RewardEffect Effect => RewardEffect.Reversible;
    public override string Describe(INameResolver names) => $"칭호 연출 {title}";
    public override void Grant(GameSession session) => session.Events.Publish(new TitleEarned { title = title });
}

[GameEvent("칭호 획득")]
public class TitleEarned { public string title; }
```

씬 코드는 `TitleEarned`를 `SubscribeAfterAction`으로 받아 연출만 한다(→ `10-Events.md` 2-7). 결과가 자기 Trigger의 이벤트를 다시 쏘면 사슬이 16단계에서 끊긴다.

### 4-8. 게임 밖에 영향을 주는 결과

```csharp
[Serializable, BlockInfo("플랫폼 업적 전송", Category = "플랫폼")]
public class PlatformAchievement : Reward
{
    public string apiName;
    public override RewardEffect Effect => RewardEffect.Irreversible;
    public override string Describe(INameResolver names) => $"플랫폼 업적 {apiName}";
    public override void Grant(GameSession session) { /* 플랫폼 SDK 호출 */ }
    public override void Validate(IValidationContext context)
    {
        if (string.IsNullOrEmpty(apiName)) context.Error("플랫폼 업적 이름이 비어 있습니다.");
    }
}
```

---

## 5. 주의할 점 / 자주 하는 실수

- **사용이 아닌 Trigger에서 대상 없이 `대상에게 효과`를 쓴다.** 도전과제·로그에는 고른 대상이 없어서, 대상 Block이 비어 있거나 `사용자 자신`만 있으면 아무 일도 일어나지 않는다. 대상을 직접 찾는 대상 Block을 둔다.
- **대상 Block에서 값을 바꾼다.** 대상 Block은 고르기만 한다. 바꾸는 일은 효과 Block에서.
- **효과의 형식과 대상의 형식이 다르다.** `Effect<Unit>`에 `Gear`가 오면 조용히 건너뛴다.
- **능력·사용 기능의 대상에 `id`가 없다.** `Game.Use`가 끝날 때 최종 대상들의 `id`로 `능력 사용` 이벤트를 쏜다. `public string id;`가 없는 대상이면 예외가 나고 그 사용 전체가 취소된다.
- **로그가 아닌 기능에서 `로그에 기록`의 로그 칸을 비운다.** 비어 있으면 "이 결과를 가진 항목"의 로그에 쓰려 하는데, 도전과제 ID는 로그가 아니다. 이때는 아무것도 기록하지 않고 경고("로그에 기록: 기록할 로그를 고르지 않았습니다")만 남는다. 도전과제·프로젝트 기능에서는 로그를 꼭 고른다.
- **`반영할 값`을 읽을 이벤트가 없다.** 조건 충족만으로 된 Trigger에는 이벤트가 없으므로 반영할 값을 비워(횟수 1) 쓰거나, 이벤트 받기를 둔다(검증 오류로 보인다).
- **리스트 안쪽 필드를 주기로 바꾸려 한다.** `gear[].power`는 고를 수 없다. 리스트 요소를 바꾸는 일은 대상 Block + 효과 Block이나 코딩한 결과로 쓴다.
- **구조체 안의 필드에 준다.** 부모가 구조체면 바꿀 수 없다는 경고만 남고 건너뛴다. 클래스로 바꾼다.
- **`Reward` 클래스에 `[Serializable]`·`[BlockInfo]`를 빠뜨린다.** 에셋에 저장되지 않거나 메뉴 이름이 클래스 이름으로 보이고, 계약 검사에서 걸린다.
- **결과에서 행동을 `Do`할 때**: 결과가 행동 안에서 돌면(능력 사용 등) 그 행동의 일부로 실행되고, 행동 밖에서 돌면(프레임 끝의 도전과제 달성 등) 새 행동 하나가 된다. 어느 쪽이 될지 신경 쓰고 싶지 않으면 공통 로직을 함수로 빼서 부른다.
