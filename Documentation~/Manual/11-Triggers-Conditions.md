# 11. Trigger와 조건

**한 줄 요약**: Trigger는 "언제 발동하는가"를 정하는 칸이다. 잎(이벤트 받기 · 조건 충족 · 사용)을 `&& AND` · `|| OR` · `! NOT`과 괄호로 중첩해 만든다. 로그·도전과제·능력·사용 기능·프로젝트가 만든 기능(퀘스트 등)이 모두 같은 Trigger 칸을 쓰고, 기능마다 고를 수 있는 잎의 종류만 다르다.

**언제 쓰는가**

- "보스 처치를 받는 순간"처럼 이벤트에 반응할 때(이벤트 받기 + 값 조건).
- "골드가 1000 이상이 되는 순간"처럼 값의 상태에 반응할 때(조건 충족).
- "아이템·능력을 쓰는 순간, 사용자가 살아 있을 때만"처럼 사용을 막을 때(사용 AND 조건 충족).

이 문서에서 쓰는 용어:

| 용어 | 뜻 |
|---|---|
| Trigger | 언제 발동하는지를 담은 나무. 코드 형식 `Trigger`(추상)와 그 잎·논리 클래스. |
| Outcome | 발동했을 때 무엇을 하는지(결과). Trigger와 짝을 이룬다(→ `12-Outcomes.md`). |
| Block | 에디터에서 골라 조립하는 로직 단위. 조건 Block(`Condition`), 결과 Block(`Reward`), 효과 Block(`Effect`), 대상 Block(`TargetSelector`)이 있다. |
| 순간 | Trigger를 검사하는 한 시점. 이벤트를 받은 순간, 값이 바뀐 순간, 사용하는 순간 중 하나다. |
| TriggerContext (컨텍스트) | 그 순간의 정보: 이벤트, 사용자, 고른 대상, 이 Trigger를 가진 항목 ID. 조건과 결과가 읽는다. |
| 값 조건 (ValueFilter) | 어떤 객체(이벤트, 플레이어 데이터 값, 리스트 항목)를 뿌리로 두고 필드를 비교하는 식. |

---

## 1. Trigger 나무의 구조

| 잎·논리 | 표시 이름 | 코드 형식 | 참이 되는 때 |
|---|---|---|---|
| 잎 | 이벤트 받기 | `EventTrigger` | 고른 이벤트를 받은 그 순간, 값 조건(`filter`)도 참일 때 |
| 잎 | 조건 충족 | `ConditionTrigger` | 안의 조건 Block(`condition`)이 그 순간 참일 때 |
| 잎 | 사용 | `UseTrigger` | `Game.Use`로 쓰는 순간 |
| 논리 | `&& AND` | `AllTrigger` | 안의 것(`triggers`)이 **같은 순간에** 모두 참 |
| 논리 | `\|\| OR` | `AnyTrigger` | 안의 것 중 하나가 참 |
| 논리 | `! NOT` | `NotTrigger` | 안의 것(`trigger`)이 거짓 |

**순간의 규칙**이 핵심이다.

- 이벤트 받기는 **그 이벤트를 받은 순간에만** 참이다. 다른 순간(값이 바뀐 순간, 다른 이벤트를 받은 순간)에는 거짓이다.
- 조건 충족은 검사하는 순간의 값으로 판정한다. 조건이 읽는 값이 바뀔 때마다 이벤트 없는 순간으로 다시 검사된다.
- 그래서 `보스 처치 받기 AND 골드 100 이상` = "골드가 100 이상인 상태에서 보스 처치를 받은 순간"이다. 골드가 나중에 100이 되어도 그 순간에는 이벤트가 없으므로 발동하지 않는다.
- `OR`는 먼저 참이 되는 쪽에서 발동한다.
- 조건만으로 된 Trigger는 거짓 → 참이 되는 순간 발동한다. 반복하는 기능(로그, `repeat: true`)이면 다시 거짓이 된 뒤에 참이 될 때 또 발동한다(참인 동안 계속 발동하지 않는다).
- 서로 다른 이벤트 받기 둘을 `AND`로 묶으면 같은 순간에 오지 않으므로 발동하지 않는다. 검증이 경고한다.
- `NOT` 안에 이벤트 받기·사용을 넣으면 "그 순간이 아닌 모든 때"가 되어 뜻이 흐려진다. 검증이 경고한다. 이벤트의 값을 거르려면 이벤트 받기의 조건 칸을 쓴다.

### 기능별 잠금

| 기능 | 고를 수 있는 잎 |
|---|---|
| 로그(`StatData.triggers`) | 이벤트 받기 |
| 도전과제(`AchievementData.trigger`) | 이벤트 받기 · 조건 충족 |
| 능력(`AbilityData.trigger`, 기본 `사용`) | 사용 · 조건 충족 |
| 사용 기능(`Usable.when`, 기본 `사용`) | 사용 · 조건 충족 |
| 프로젝트 기능 | 필드의 `[TriggerSlot]`이 정한다(5장) |

---

## 2. 에디터에서 쓰는 법

DB 상세 패널에서 Trigger 필드(`trigger`, `when` 등. 칸 이름표는 코드의 변수 이름이고 `[FieldLabel]` 글자 "트리거"는 마우스를 올리면 보인다)는 **한 줄 식 편집기**로 그려진다.

### 2-1. 블럭 더하기

1. 칸이 비어 있으면 "트리거 없음 — 아래에서 블럭을 더하세요"가 보인다.
2. **`+ 블럭`**을 누르면 목록이 뜬다. 칸의 잠금에 있는 것만 나온다.
   - 그룹 **트리거**: `이벤트 받기`, `사용`
   - 그룹 **조건 충족 · 분류**: 조건 Block들(`비교`, `플레이어 데이터 검사`, `도전과제 달성`, 프로젝트가 만든 조건 Block). 고르면 조건 충족 잎으로 감싸 넣고, 식 안에서는 조건 Block 그대로 그린다.
3. 더한 블럭은 식 끝에 붙고, 앞 항과는 `AND`로 이어진다.

### 2-2. 논리와 괄호

- **`+ 논리 연산자`**: `AND`(그리고) · `OR`(또는) · `NOT`(아님)을 식 끝에 더한다. 뒤에 빈칸이 생기고, 빈칸(**트리거 선택**)을 눌러 블럭을 고른다.
- **`+ 괄호`**: 빈 괄호를 더한다. 괄호 안의 `+`로 채우거나, 블럭 왼쪽 손잡이(`⠿`)를 끌어 넣는다.
- 항 사이의 연산자를 누르면 `AND` ↔ `OR`가 바뀐다. **AND가 OR보다 먼저 묶인다**(`A OR B AND C` = `A OR (B AND C)`).
- 항 오른쪽 `⋯` 메뉴: `NOT 붙이기`/`NOT 떼기`, `앞으로`, `뒤로`, `괄호로 감싸기`, `다른 블럭으로 바꾸기`, `괄호 풀기`, `삭제`.
- 손잡이 `⠿`를 끌어 순서를 바꾸고, 괄호 안으로 넣고, 밖으로 뺀다.

### 2-3. 이벤트 받기 블럭

1. 블럭 문장 `{eventType} 받기`의 이벤트 칸을 눌러 이벤트를 고른다.
2. `filter`(툴팁 "이벤트 조건") 칸이 같은 한 줄 식 편집기로 열린다. 뿌리는 고른 이벤트다.
   - `+ 블럭` → **필드 검사**(문장 `{path} {op} {value}`): 이벤트의 필드를 탐색기로 고르고, 형식에 맞는 비교와 값을 고른다.
   - `+ 블럭` → **배열·리스트 항목 검사**(문장 `{path} {mode} {count}`): 리스트의 각 항목에 안쪽 조건을 걸고 any/all/none/count로 판정한다.
   - `+ 논리 연산자`, `+ 괄호`도 같다.
3. 조건 칸을 비워 두면 그 이벤트를 받을 때마다 참이다.

### 2-4. 조건 Block 고르기

| 표시 이름 | 분류 | 코드 형식 | 문장·칸 | 뜻 |
|---|---|---|---|---|
| 비교 | 로그 | `StatCompareCondition` | `{stat} {op} {value}` (`>=`, `<=`, `>`, `<`, `==`) | 로그 값을 기준값과 비교 |
| 플레이어 데이터 검사 | 플레이어 데이터 | `PlayerDataCondition` | 플레이어 데이터 + 조건(값 조건 식) | 플레이어 데이터 값(클래스·컬렉션 포함)을 값 조건으로 검사 |
| 도전과제 달성 | 도전과제 | `AchievementUnlockedCondition` | `{achievement} 달성` | 다른 도전과제를 달성했는지 |
| (프로젝트 조건) | `[BlockInfo]`의 분류 | `SimpleCondition` 상속 | `[BlockInfo]`의 문장 | 코드로 쓴 조건(4장) |

도전과제·능력은 DB 상세 위쪽 요약에 "트리거 …" 한 줄이 자연어로 보인다(로그는 "반영 …" 줄). **Alt**를 누르고 있으면 코드 형태(`on 유닛 처치 where combo >= 10 && …`)로 바뀐다. 문제가 있으면 검증 오류가 빨간 글씨로 뜨고, 오류가 있으면 빌드가 막힌다.

---

## 3. 값 조건과 필드 경로

### 3-1. 필드 경로 문법

| 경로 | 뜻 |
|---|---|
| `level` | 필드 하나 |
| `weapon.power` | 중첩 클래스의 필드 |
| `tags` | 컬렉션 자체(포함·개수·비어 있음 검사) |
| `items[].level` | 컬렉션의 각 요소의 필드 |
| (빈 경로) | 뿌리 값 자체(예: 숫자 플레이어 데이터) |

- 딕셔너리는 **값들**을 요소로 본다(키로 한 항목을 집는 경로는 없다).
- 필드 외에 읽을 수 있는 public 속성도 따라간다(이벤트 클래스의 계산 속성 등).
- 중간에 `null`이 있으면 그 갈래는 값이 없다.

### 3-2. 형식별 비교(`FieldOp`)

| 경로 끝 형식 | 고를 수 있는 비교(화면 글자 / 코드 기호 / `FieldOp`) |
|---|---|
| 숫자 | 이상 `>=` `AtLeast` · 이하 `<=` `AtMost` · 초과 `>` `Greater` · 미만 `<` `Less` · 일치 `==` `Equal` · 아님 `!=` `NotEqual` |
| 글자 | 일치 · 아님 · 비어 있음 `IsEmpty` · 비어 있지 않음 `IsNotEmpty` |
| 참/거짓 | 참 `IsTrue` · 거짓 `IsFalse` |
| enum | 일치 · 아님 |
| 컬렉션 | 포함 `Contains` · 미포함 `NotContains` · 개수 이상 `CountAtLeast` · 개수 이하 `CountAtMost` · 개수 일치 `CountEqual` · 비어 있음 · 비어 있지 않음 |
| 그 밖의 클래스 | 비어 있음 · 비어 있지 않음 |

값 칸은 경로 끝 형식에 맞춰 바뀐다: enum·참거짓·키워드가 연결된 글자는 목록에서 고르고, 숫자는 숫자만 받는다. 참거짓 값은 `true`/`false`(또는 `참`/`거짓`)로 저장된다.

### 3-3. `[]` 경로가 여러 값을 낼 때

`items[].level 3 이상`처럼 값이 여러 개면:

- 이상·이하·초과·미만·일치·포함·참·거짓: **하나라도** 만족하면 참.
- 아님(`NotEqual`)·미포함: **모두** 만족해야 참.
- 개수·비어 있음: 전체 기준.

"N개 이상이 만족" 같은 판정은 **배열·리스트 항목 검사**(`ElementFilter`)를 쓴다.

| `mode` (화면) | 뜻 |
|---|---|
| `any` | 안쪽 조건을 만족하는 항목이 하나 이상 |
| `all` | 모든 항목이 만족(항목이 없으면 참) |
| `none` | 만족하는 항목이 없음 |
| `count >=` / `count <=` / `count ==` | 만족하는 항목 수가 `count` 이상 / 이하 / 같음 |

안쪽 조건(`filter`)의 뿌리는 리스트의 요소 형식이다.

### 3-4. 코드로 만들기

```csharp
using GBible;

// 이벤트 받기: 유닛 처치 중 보스이고 콤보 10 이상
var boss10 = new EventTrigger
{
    eventType = typeof(UnitKilled).FullName,          // 형식의 전체 이름
    filter = new AllFilter
    {
        filters =
        {
            new FieldFilter { path = "tags", op = FieldOp.Contains, value = "boss" },
            new FieldFilter { path = "combo", op = FieldOp.AtLeast, value = "10" },
        },
    },
};

// 플레이어 데이터 검사: party의 장비 중 등급 legendary가 2개 이상
var twoLegendaries = new ConditionTrigger
{
    condition = new PlayerDataCondition
    {
        key = "party",
        filter = new ElementFilter
        {
            path = "gear",
            mode = ElementMode.CountAtLeast,
            count = 2,
            filter = new FieldFilter { path = "grade", op = FieldOp.Equal, value = "legendary" },
        },
    },
};
```

- `FieldFilter`의 `value`는 언제나 글자다(숫자도 `"10"`).
- 논리는 `AllFilter`(`filters`) · `AnyFilter`(`filters`) · `NotFilter`(`filter`).

---

## 4. 코드로 쓰는 조건 Block

### 4-1. `SimpleCondition` — `Check()`만 쓴다

```csharp
using System;
using System.Linq;
using GBible;

[Serializable, BlockInfo("적 전멸", Category = "전투", Description = "적 팀 유닛의 체력이 모두 0 이하입니다.")]
public class AllEnemiesDown : SimpleCondition
{
    public string team = "enemy";
    protected override bool Check() => Game.All<Unit>().Where(u => u.team == team).All(u => u.hp <= 0);
}
```

문법 요소를 차례로:

1. **`[Serializable]`** — 필수. Block은 DB 에셋에 저장된다.
2. **`[BlockInfo("표시 이름")]`** — 필수. `+ 블럭` 목록에 이 이름으로 나온다.
   - `Category`: 목록의 분류(트리거 칸에서는 "조건 충족 · 분류"로 묶인다).
   - `Description`: 목록·툴팁 설명.
   - `Sentence`: 편집 화면의 한 줄 문장. `{필드이름}`이 누르면 바뀌는 칸이 된다. 예: `Sentence = "{team} 팀 전멸"`.
3. **`: SimpleCondition`** — 기반 클래스.
4. **public 필드** — 에디터 칸이 된다(항목마다 값이 다르다). `[FieldLabel("설명")]`을 붙이면 마우스를 올렸을 때 그 글이 보인다.
5. **`protected override bool Check()`** — 판정. 값을 바꾸지 않는다.
6. **`Game`** — 지금 세션의 `GameApi`(`All`·`Find`·`Get` …).
7. **`Context`** — 지금 검사 중인 순간(`TriggerContext`). Trigger 밖에서 평가되면 `null`이다.

무엇을 보는지 선언하지 않는다. 평가할 때 읽은 플레이어 데이터를 시스템이 기록해 두고, 그 값이 바뀌면 다시 평가한다(4-4).

### 4-2. `Context`로 순간을 읽는다

```csharp
// 사용 조건: 사용자가 살아 있을 때만(능력·사용 기능의 트리거에 AND로).
[Serializable, BlockInfo("사용자 체력 있음", Category = "사용")]
public class UserAlive : SimpleCondition
{
    protected override bool Check() => Context?.User is Unit unit && unit.hp > 0;
}

// 이벤트 받기와 AND로: 처치한 쪽이 아군일 때만.
[Serializable, BlockInfo("처치한 유닛이 아군", Category = "전투")]
public class KillerIsAlly : SimpleCondition
{
    protected override bool Check() =>
        Context?.Event is UnitKilled e && Game.Find<Unit>(e.killerId)?.team == "ally";
}
```

| `TriggerContext` 멤버 | 뜻 |
|---|---|
| `Session` | 세션 |
| `Game` | `Session.Game` |
| `Owner` | 이 Trigger를 가진 항목의 ID(로그·도전과제·능력 …) |
| `Event` | 이벤트를 받은 순간이면 그 이벤트, 아니면 `null` |
| `IsUse` | 사용하는 순간이면 `true` |
| `User` | 사용자(사용하는 순간) |
| `Picked` | 플레이어가 고른 대상들(사용하는 순간, 없으면 빈 리스트) |

값이 바뀌어 다시 평가되는 순간에는 `Event`가 `null`이다. 그래서 `KillerIsAlly`는 혼자 쓰면 발동하지 않고, 이벤트 받기와 `AND`로 묶어 쓴다.

### 4-3. `Condition` — 전부 직접 쓰는 형태(필요할 때만)

시간이 흐르기만 해도 바뀌는 조건처럼, 읽은 값으로 감시할 수 없을 때 쓴다.

```csharp
using System;
using System.Collections.Generic;
using GBible;

[Serializable, BlockInfo("플레이 시간", Category = "시간", Sentence = "{seconds}초 이상 플레이")]
public class PlayedSeconds : Condition
{
    public double seconds = 600;

    public override bool Evaluate(GameSession session) => session.Clock.Time >= seconds;
    public override void CollectDependencies(ICollection<string> keys) { }   // 보는 플레이어 데이터 없음
    public override double PollInterval => 1;                                // 1초마다 확인
    public override string Describe(INameResolver names) => $"플레이 {seconds}초 이상";

    public override bool TryGetProgress(GameSession session, out double current, out double target)
    {
        current = Math.Min(session.Clock.Time, seconds);
        target = seconds;
        return true;
    }
}
```

| 멤버 | 필수 | 뜻 |
|---|---|---|
| `bool Evaluate(GameSession)` | 예 | 판정 |
| `void CollectDependencies(ICollection<string> keys)` | 예 | 보는 플레이어 데이터 키를 직접 넣는다(로그는 `PlayerDataKeys.Stat(id)`, 도전과제는 `PlayerDataKeys.Achievement(id)`) |
| `string Describe(INameResolver)` | 예 | 코드 형태 문장(Alt) |
| `string Natural(INameResolver)` | 아니오 | 자연어 문장(기본 = `Describe`) |
| `string Explain(INameResolver)` | 아니오 | 마우스를 올렸을 때 설명 |
| `double PollInterval` | 조건부 | 시간 기반이면 0보다 크게(초) |
| `bool TryGetProgress(…)` | 아니오 | 진행도(예: 37 / 100) |
| `void Validate(IValidationContext)` | 아니오 | 에디터 검증(`context.Error`·`context.Warning`) |

보는 값도 없고(`CollectDependencies`가 비고) `PollInterval`도 0이면 영원히 다시 평가되지 않는다. DB 검증이 "조건이 아무 값도 관찰하지 않아 발동할 수 없습니다(블록 계약 위반)." 오류를 띄운다.

### 4-4. 자동 다시 평가(`ConditionWatcher`)

조건 충족 잎은 `session.Triggers.Watcher`(`ConditionWatcher`)가 감시한다.

1. 등록할 때 조건을 한 번 평가해 보며 **실제로 읽은 플레이어 데이터 키**를 모은다(`CollectDependencies`로 선언한 키도 더한다).
2. 그 키의 값이 바뀌면 그 조건만 "다시 볼 것"으로 표시한다(값이 바뀐 키에 걸린 조건만 본다. 매 프레임 전부 훑지 않는다).
3. **Flush** 때 표시된 조건을 평가한다. Flush는 행동(`Game.Do`·`Game.Use`)이 끝날 때, Undo 뒤, 판을 불러온 뒤, 그리고 런타임이 매 프레임 끝에 부른다.
4. 평가할 때마다 읽은 키를 다시 모은다(조건 안의 분기가 바뀌어도 따라간다).
5. 조건 충족 → 결과 → 다른 값 변화 → 다른 조건 충족으로 이어지면 더 바뀌지 않을 때까지 반복한다. 16회를 넘으면 "조건 연쇄 평가가 16회를 넘었습니다. 순환 참조를 확인하세요." 경고를 남기고 멈춘다.
6. `PollInterval`이 있는 조건은 그 간격마다 다시 평가한다.

Unity 밖(서버·테스트)에서 세션을 직접 돌리면 매 프레임 Flush가 없다. 값을 직접 바꾼 뒤에는 `session.Triggers.Flush()`(또는 `session.Achievements.Flush()`, 같은 감시기)를 부른다. 행동으로 바꾸면 자동이다.

---

## 5. 칸 표시 어트리뷰트 `[TriggerSlot]` · `[OutcomeSlot]`

Trigger·Outcome 칸이 있는 클래스(DB 항목, 인스턴스 기능)는 필드에 표시를 붙여 고를 수 있는 범위를 잠근다.

```csharp
[SerializeReference, TriggerSlot(TriggerKinds.Event | TriggerKinds.Condition)] public Trigger trigger;
[SerializeReference, OutcomeSlot(OutcomeKinds.All)] public List<Outcome> outcomes = new List<Outcome>();
[TriggerSlot(TriggerKinds.Event), OutcomeSlot(OutcomeKinds.Record, Fixed = true)] public List<TriggerRule> rules = new List<TriggerRule>();
```

| 어트리뷰트 | 인자 | 뜻 |
|---|---|---|
| `[TriggerSlot(TriggerKinds)]` | `Event`·`Condition`·`Use`·`All`을 `\|`로 | `+ 블럭` 목록과 검증이 이 종류만 허락 |
| `[OutcomeSlot(OutcomeKinds)]` | `Record`·`PlayerData`·`Coded`·`Targeted`·`All` | `+ 결과 추가` 목록과 검증이 이 종류만 허락 |
| `Fixed = true` | (OutcomeSlot) | 결과가 그 종류 하나로 고정. 비어 있으면 하나를 자동으로 두고 세부 칸만 고친다(추가·삭제 없음) |

- `Trigger` 필드, `List<Outcome>` 필드에 직접 붙이거나, `List<TriggerRule>` 필드에 붙이면 규칙마다의 `when`·`then`에 적용된다.
- `Trigger`·`Outcome`을 담는 필드에는 `[SerializeReference]`가 필요하다(`TriggerRule`의 `when`·`then`은 이미 붙어 있다).
- 표시가 없는 칸은 전부 허락한다.
- 검증(DB 저장·빌드 때): 잠금 밖의 종류 "이 칸에서는 '…' 트리거를 쓸 수 없습니다", 고정 칸의 개수, 값을 관찰하지 않는 조건, 플레이어 데이터 검사·주기의 경로를 모든 기능에서 같은 규칙으로 본다.

---

## 6. 실행 규칙(`session.Triggers`)

### 6-1. 같은 이벤트에 여러 Trigger가 걸렸을 때의 순서

조건 충족 잎이 **없는** Trigger(로그 기록 등)를 먼저, 조건이 **있는** Trigger(도전과제 등)를 나중에 검사한다. 정의한 순서와 상관없다. 그래서 `유닛 처치 받기 AND 처치 수 >= 10`은 같은 처치로 막 오른 처치 수를 본다(10번째 처치 순간에 발동).

### 6-2. 사슬 한도

결과가 이벤트를 쏘고, 그 이벤트가 다시 Trigger를 발동하는 사슬은 깊이 16(`TriggerSystem.MaxDepth`)에서 끊긴다. Journal에 "트리거 사슬이 16단계를 넘어 끊었습니다(…). 결과가 자기 트리거의 이벤트를 다시 쏘는지 확인하세요." 경고가 남는다. 예외는 던지지 않는다.

### 6-3. 진행도

`Trigger.TryGetProgress(session, out current, out target)`은 조건 잎의 진행도를 모은다: `AND`는 가장 덜 된 조건, `OR`는 가장 많이 된 조건. 이벤트·사용 잎은 진행도가 없다. `비교`는 `>=`·`>`이고 기준값이 0보다 클 때만 진행도를 낸다.

### 6-4. `TriggerSystem` API

| 멤버 | 뜻 |
|---|---|
| `Register(string owner, Trigger when, bool repeat, Action<TriggerContext> fire)` | 감시를 시작한다. 발동하면 `fire`(결과 실행은 부르는 쪽이 `RunOutcomes`로). `repeat`가 아니면 한 번 발동한 뒤 감시를 그만둔다. `when`이 `null`이면 아무것도 하지 않는다. |
| `Unregister(string owner)` / `IsRegistered(owner)` | 그 owner의 감시를 모두 그만둔다 / 감시 중인지 |
| `RunOutcomes(IEnumerable<Outcome>, TriggerContext, string source)` | 결과를 차례로 실행한다. `source`는 Journal의 출처 글자 |
| `CanUse(owner, when, user, picked, out TriggerContext)` | 사용하는 순간으로 검사(아무것도 바꾸지 않는다) |
| `Test(Trigger, TriggerContext)` | 그 순간으로 검사 |
| `Current` | 지금 검사·발동 중인 순간(밖에서는 `null`) |
| `Watcher` / `Flush()` | 조건 감시기 / 다시 평가 |
| `MaxDepth` | 사슬 한도(16) |

---

## 7. 상황별 예제

예제는 다음 형식을 쓴다.

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using GBible;

[Serializable] public class Unit { public string id; public string team; public int slot; public int hp = 30, maxHp = 30, exp; }
[Serializable] public class Battle { public List<Unit> units = new List<Unit>(); public int turn; }
[Serializable] public class Gear { public string name; public string grade; public int power; }
[Serializable] public class Party { public int gold; public List<Gear> gear = new List<Gear>(); public List<string> titles = new List<string>(); }

[GameEvent("유닛 처치")]
public class UnitKilled
{
    public string killerId, victimId;
    public int exp, combo;
    public List<string> tags = new List<string>();
    public bool IsBoss => tags.Contains("boss");
}
```

### 7-1. 보스 처치만 받기(에디터)

1. Trigger 칸 `+ 블럭` → `이벤트 받기`.
2. 이벤트 칸 → `유닛 처치`.
3. `filter` 칸 `+ 블럭` → `필드 검사` → 필드 `tags`, 비교 `포함`, 값 `boss`.
   (또는 필드 `IsBoss`, 비교 `참`.)

요약: `유닛 처치 받기(tags에 boss 포함일 때)`.

### 7-2. "골드 100 이상인 상태에서 보스 처치"(AND)

에디터: `+ 블럭` → 이벤트 받기(유닛 처치, `IsBoss 참`) → `+ 블럭` → `플레이어 데이터 검사`(플레이어 데이터 `party`, 조건 `gold 100 이상`). 두 항이 `AND`로 이어진다.

```csharp
var when = new AllTrigger
{
    triggers =
    {
        new EventTrigger { eventType = typeof(UnitKilled).FullName,
                           filter = new FieldFilter { path = "IsBoss", op = FieldOp.IsTrue } },
        new ConditionTrigger { condition = new PlayerDataCondition { key = "party",
                           filter = new FieldFilter { path = "gold", op = FieldOp.AtLeast, value = "100" } } },
    },
};
```

### 7-3. "보스를 잡거나, 골드를 1000 모으면"(OR)

```csharp
var when = new AnyTrigger
{
    triggers =
    {
        new EventTrigger { eventType = typeof(UnitKilled).FullName, filter = new FieldFilter { path = "IsBoss", op = FieldOp.IsTrue } },
        new ConditionTrigger { condition = new PlayerDataCondition { key = "party", filter = new FieldFilter { path = "gold", op = FieldOp.AtLeast, value = "1000" } } },
    },
};
```

골드 쪽은 골드가 바뀌는 행동이 끝날 때 다시 평가되어 발동한다.

### 7-4. 전설 장비 2개 이상(리스트 항목 검사)

에디터: `+ 블럭` → `플레이어 데이터 검사` → 플레이어 데이터 `party` → 조건 `+ 블럭` → `배열·리스트 항목 검사` → 배열·리스트 `gear`, 판정 `count >=`, 개수 `2`, 항목 조건 `+ 블럭` → `필드 검사`(`grade` 일치 `legendary`).

요약: `party의 gear 중 [grade(이)가 legendary]인 항목이 2개 이상`.

### 7-5. 같은 처치로 오른 로그를 보는 AND

로그 `kills`(유닛 처치 받기 → 횟수 기록)가 있을 때, 도전과제 Trigger를 `유닛 처치 받기 AND 비교(kills >= 10)`로 두면 **10번째 처치를 받는 순간** 달성된다(6-1 순서 규칙). 조건 충족만(`비교(kills >= 10)`)으로 두어도 달성되지만, 그때는 처치 행동이 끝난 뒤의 Flush에서 달성된다. 결과가 그 처치 이벤트의 값(`Context.Event`)을 써야 하면 AND 형태를 쓴다.

### 7-6. 코드 조건: 적 전멸 / 아군 하수인 수

```csharp
[Serializable, BlockInfo("적 전멸", Category = "전투")]
public class AllEnemiesDown : SimpleCondition
{
    protected override bool Check()
    {
        var battle = Game.All<Battle>().FirstOrDefault();
        return battle != null && battle.units.Where(u => u.team == "enemy").All(u => u.hp <= 0);
    }
}

[Serializable, BlockInfo("아군 하수인 수", Category = "전투", Sentence = "아군 하수인 {count}명 이상")]
public class AllyCountAtLeast : SimpleCondition
{
    public int count = 3;
    protected override bool Check() => Game.All<Unit>().Count(u => u.team == "ally" && u.hp > 0) >= count;
}
```

`battle` 데이터가 바뀌는 행동이 끝날 때마다 자동으로 다시 평가된다.

### 7-7. 사용 조건(능력·아이템)

```csharp
[Serializable, BlockInfo("사용자 체력 비율", Category = "사용", Sentence = "사용자 체력 {percent}% 이하")]
public class UserHpBelow : SimpleCondition
{
    public int percent = 50;
    protected override bool Check() => Context?.User is Unit u && u.hp * 100 <= u.maxHp * percent;
}

// 능력의 트리거: 사용 AND 사용자 체력 50% 이하
var when = new AllTrigger { triggers = { new UseTrigger(), new ConditionTrigger { condition = new UserHpBelow { percent = 50 } } } };
```

거짓이면 `Game.Use`가 실패하고(예외, 아무것도 바뀌지 않음), `Game.CanUse`는 `false`와 이유를 돌려준다(→ `16-Abilities-Usable.md`).

### 7-8. 직접 감시 등록(프로젝트 코드)

```csharp
// 골드가 500 이상이 될 때마다(떨어졌다가 다시 오르면 또) 상점 알림 UI를 연다.
session.Triggers.Register("shopHint",
    new ConditionTrigger { condition = new PlayerDataCondition { key = "party", filter = new FieldFilter { path = "gold", op = FieldOp.AtLeast, value = "500" } } },
    repeat: true,
    fire: moment => ShopHintUI.Show());
```

`fire` 안에서 값을 바꾸려면 `session.Triggers.RunOutcomes(결과들, moment, "출처")`를 거친다(→ `17-Custom-Features.md`).

---

## 8. 주의할 점 / 자주 하는 실수

- **이벤트 둘을 AND로 묶는다.** "보스 처치 받기 AND 골드 획득 받기"는 영원히 발동하지 않는다. 둘 다 일어난 뒤를 원하면 각각을 로그로 세고 `비교` 조건 둘을 AND로 묶는다.
- **조건 Block에서 값을 바꾼다.** 조건은 읽기만 한다. 평가 횟수·순서는 보장되지 않는다(등록 때 한 번 미리 평가하고, 값이 바뀔 때마다 평가한다).
- **조건이 플레이어 데이터 밖을 읽는다.** MonoBehaviour 필드, static 변수, `UnityEngine.Time`은 감시되지 않는다. 바뀌어도 다시 평가되지 않는다. 감시할 값은 플레이어 데이터에 두거나 `PollInterval`을 쓴다.
- **`Context?.Event`를 혼자 쓴다.** 값이 바뀌어 평가되는 순간에는 `Event`가 `null`이다. 이벤트를 보는 조건은 이벤트 받기와 AND로 묶는다.
- **이벤트 형식이 아니라 상위 형식을 고른다.** 이벤트 받기는 형식이 정확히 같아야 한다.
- **Unity 밖에서 Flush를 잊는다.** 서버·테스트에서 `Data.Set`으로 값을 바꾼 뒤 `session.Triggers.Flush()`를 부르지 않으면 조건 충족 잎이 발동하지 않는다.
- **`NOT`으로 이벤트를 거른다.** "보스가 아닌 처치"는 `NOT(이벤트 받기)`가 아니라 이벤트 받기의 조건 칸에 `IsBoss 거짓`(또는 `tags` 미포함 `boss`)으로 쓴다.
- **필드 경로가 깨진다.** 이벤트·플레이어 데이터 클래스의 필드 이름을 바꾸면 경로 글자는 따라가지 않는다. DB 검증 오류("'…'에 '…' 필드가 없습니다.")가 뜬 칸을 다시 고른다.
