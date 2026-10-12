# 15. 로그(수치 기록)

**한 줄 요약**: 로그는 이벤트를 받을 때마다 값을 규칙대로 쌓는 숫자다(처치 수, 받은 피해 총합, 최대 콤보, 최단 클리어 시간 …). 반영 규칙 = Trigger(이벤트 받기) → 결과(로그에 기록, 고정). 에디터 이름은 **로그**이고, 코드 형식 이름은 `StatData`·`StatDefinition`·`session.Stats`다.

> 로그 탭은 항목이 없으면 DB 사이드바에서 숨는다 — 프로젝트가 항목을 더하면(AI의 DB 반영 스크립트) 나타난다.

> GBible에서 "Stat"은 이 로그를 가리킨다. 캐릭터의 공격력·방어력 같은 스탯은 **스탯(Attribute)**라고 부르고, 장비·버프 보정(Modifier)과 함께 `28-Attributes-Intercepts.md`에서 다룬다.

**언제 쓰는가**

- 도전과제·퀘스트의 조건으로 쓸 숫자를 모을 때("처치 100회" = 로그 `kills` + 조건 `비교`).
- 플레이 통계(누적 플레이, 판마다의 기록)를 남길 때.
- 게임 코드가 셀 필요 없이, 이벤트만 쏘면 숫자가 쌓이게 하고 싶을 때.

이 문서에서 쓰는 용어:

| 용어 | 뜻 |
|---|---|
| Trigger | 언제 반영하는지. 로그에서는 이벤트 받기만(AND·OR·NOT 가능, → `11-Triggers-Conditions.md`). |
| Outcome | 무엇을 하는지. 로그에서는 "로그에 기록" 하나로 고정(→ `12-Outcomes.md`). |
| 반영 규칙 | Trigger 하나 + 결과(`TriggerRule`). 로그 하나에 여러 줄 둘 수 있다. |
| 범위 | 이번 판(세션 구역, 새 판이면 처음 값) / 누적(메타 구역, 프로필에 저장). |
| 집계 방식 | 받은 값을 어떻게 합치는지(합계·최고치·최저치·마지막 값). |
| Undo | 마지막 행동 되돌리기. |

---

## 1. 에디터에서 쓰는 법

### 1-1. 로그 만들기

1. `MomoiProject → DB` → 사이드바 **요소** 그룹의 **로그** 탭.
2. 목록 줄에 마우스를 올려 **`+ 로그`** → 새 항목. 제목(= ID)을 눌러 이름을 짓는다(예: `kills`, `bestCombo`).
3. 칸을 채운다(칸 이름표는 코드의 변수 이름, 마우스를 올리면 설명).

| 칸 | 툴팁 | 값 |
|---|---|---|
| `scope` | 범위 | `이번 판` / `누적`(기본) |
| `aggregate` | 집계 방식 | `합계`(기본) / `최고치` / `최저치` / `마지막 값` |
| `triggers` | 반영 규칙 | 규칙 리스트 |

### 1-2. 반영 규칙 더하기

1. `triggers`의 **`+ 추가`**로 규칙 한 줄을 더한다.
2. 규칙의 트리거 칸(`when`): `+ 블럭` → `이벤트 받기`(로그 칸에서는 이것만 나온다) → 이벤트를 고른다. 필요하면 이벤트 조건(`filter`)에 `필드 검사` 등을 건다.
3. 규칙의 결과 칸(`then`): `로그에 기록` 한 줄이 자동으로 들어 있다(고정, 추가·삭제 없음). 왼쪽에 "이 로그 · 고정", 오른쪽에 두 칸.
   - **반영할 값**: 트리거 이벤트의 숫자 필드(속성 포함, `weapon.power`처럼 점으로 내려갈 수 있다). 비우면 **횟수 1**.
   - **배수**: 곱할 값. 2배로 세기, `-1`로 빼기.
4. 규칙은 여러 줄 둘 수 있다. 같은 이벤트를 받는 규칙이 둘이면 둘 다 반영된다.

### 1-3. 집계 방식

| 집계 | 받은 값 v를 반영하는 법 | 기록이 없을 때 | 쓰는 예 |
|---|---|---|---|
| 합계 (`Sum`) | 지금 값 + v | 0 | 처치 수, 획득 골드 총합 |
| 최고치 (`Max`) | 더 큰 값 | 비어 있음 | 최대 콤보, 한 방 최대 피해 |
| 최저치 (`Min`) | 더 작은 값 | 비어 있음 | 최단 클리어 시간 |
| 마지막 값 (`Last`) | v로 덮어씀 | 0 | 마지막으로 고른 난이도 |

최고치·최저치는 첫 기록 전까지 비어 있다(코드에서 `NaN`). 비어 있는 로그로 하는 `비교`는 언제나 거짓이다.

상세 위쪽 요약에 "방식"(예: `누적 · 합계`)과 "반영"(규칙마다 한 줄, 예: `유닛 처치 받기 → 횟수를 이 로그에 반영`) 두 줄이 보인다. Alt를 누르면 코드 형태로 바뀐다.

### 1-4. 검증

- "규칙 n: 트리거가 없습니다." — 규칙에 이벤트 받기를 넣는다.
- "로그에 기록의 반영할 값 '…'을 읽을 이벤트가 트리거에 없습니다(비우면 횟수 1)." / "(이벤트): '…'는 숫자가 아닙니다" — 반영할 값을 다시 고른다. OR로 이벤트 둘을 받으면 **두 이벤트 모두에** 그 숫자 필드가 있어야 한다.
- (경고) "횟수 반영은 합계형 로그에서만 의미가 있습니다." — 최고치·최저치·마지막 값 로그에서 반영할 값을 비웠다.
- (경고) "로그에 기록: 배수가 0이라 아무것도 반영되지 않습니다."

### 1-5. 모니터에서 확인하기(플레이 중)

`MomoiProject → 모니터` → **로그** 탭: 로그마다 지금 값과 "누적 · 합계" 같은 표시가 보인다. 줄을 누르면 작업 패널:

| 버튼 | 하는 일 |
|---|---|
| **직접 수정** | 값만 덮어쓴다. 이벤트·다른 로그·도전과제는 반응하지 않는다 |
| **이벤트로 더하기**(합계) / **이벤트로 기록**(그 밖) | 반영 규칙을 거꾸로 따라가 그 값이 되도록 실제 이벤트를 쏜다(버튼 툴팁에 "쏠 이벤트: …"). 같은 이벤트를 받는 다른 로그·도전과제도 반응하고, Undo할 수 있다 |

**이벤트** 탭에서 이벤트를 고르면 "반응하는 로그: kills ← 횟수 · damageDealt ← damage"처럼 그 이벤트를 받는 로그가 보인다. **명령** 탭의 "이번 판 수치 초기화"는 범위가 이번 판인 로그를 처음 값으로 되돌린다.

---

## 2. 코드로 쓰는 법 — 문법 요소를 하나씩

### 2-1. `StatDefinition`

```csharp
var kills = new StatDefinition { Id = "kills", DisplayName = "처치 수", Scope = StatScope.Total, Aggregate = StatAggregate.Sum };
kills.Triggers.Add(new TriggerRule
{
    when = new EventTrigger { eventType = typeof(UnitKilled).FullName },
    then = { new RecordOutcome() },                       // 반영할 값 비움 = 횟수 1
});
session.Stats.Define(kills);
```

| 멤버 | 뜻 |
|---|---|
| `Id` | 필수 |
| `DisplayName` | 표시 이름 |
| `Scope` (`StatScope`) | `Run`(이번 판) / `Total`(누적) |
| `Aggregate` (`StatAggregate`) | `Sum` / `Max` / `Min` / `Last` |
| `Initial` (`double?`) | 처음 값(없으면 합계·마지막 값은 0, 최고치·최저치는 비어 있음) |
| `Triggers` (`List<TriggerRule>`) | 반영 규칙 |
| `Rules` (`List<StatRule>`) | 짧은 형태의 규칙(2-3) |

게임에서는 런타임이 시작할 때 DB의 모든 로그(`StatData.ToDefinition()`)를 정의한다.

### 2-2. `RecordOutcome` — 로그에 기록

```csharp
new RecordOutcome { field = "damage", multiplier = 2 }      // 이벤트의 damage × 2
new RecordOutcome { stat = "kills" }                       // 다른 기능(도전과제 등)에서: 로그를 꼭 정한다
```

| 필드 | 뜻 |
|---|---|
| `stat` | 반영할 로그 ID. 비우면 이 결과를 가진 항목(로그 규칙 안에서는 그 로그 자신) |
| `field` | 트리거 이벤트에서 읽을 숫자 필드·속성 경로(점으로 내려간다). 비우면 1 |
| `multiplier` | 배수(기본 1) |

이벤트 없이 발동했는데 `field`가 있으면 기록하지 않고 Journal에 경고("… 값을 읽을 이벤트가 없습니다")를 남긴다. 값을 읽는 함수는 이벤트 형식별로 한 번 만들어 기억한다.

### 2-3. `StatRule` — 짧은 형태

```csharp
kills.Rules.Add(new StatRule { eventType = typeof(UnitKilled).FullName });                          // 횟수
damage.Rules.Add(new StatRule { eventType = typeof(UnitKilled).FullName, field = "damage" });        // 값
bossKills.Rules.Add(new StatRule
{
    eventType = typeof(UnitKilled).FullName,
    filter = new FieldFilter { path = "tags", op = FieldOp.Contains, value = "boss" },
});
```

"이벤트 하나 받기 → 이 로그에 기록" 한 줄을 짧게 쓰는 형태다(`eventType`, `field`, `multiplier`, `filter`). 실행할 때 같은 반영 규칙으로 바뀌어 돈다.

### 2-4. `session.Stats` — `StatSystem`

| 멤버 | 뜻 |
|---|---|
| `Define(definition)` | 정의하고 규칙을 감시에 등록한다. 같은 ID면 바꾼다 |
| `Get(id)` | 지금 값(`double`, 비어 있으면 `NaN`). 정의되지 않은 ID면 예외 |
| `Report(id, value)` | 집계 방식대로 값을 반영한다(규칙 없이 코드에서 직접) |
| `Set(id, value)` | 값을 그대로 덮어쓴다 |
| `ResetRun()` | 범위가 이번 판인 로그를 처음 값으로 |
| `IsDefined(id)` / `Find(id)` / `Definitions` | 확인·찾기·전체 |

저장 위치는 플레이어 데이터 키 `stat:(ID)`(`PlayerDataKeys.Stat(id)`)다. 이번 판은 세션 구역, 누적은 메타 구역이다. 값이 바뀌면 그 키를 보는 조건(`비교`)이 다시 평가된다.

### 2-5. 행동과 Undo

- 행동 안에서 쏜 이벤트로 오른 로그는 그 행동의 Undo 단위에 들어간다. Undo하면 로그도 돌아간다.
- 행동 밖에서 쏜 이벤트(씬 코드에서 바로 `Publish`)로 오른 로그는 "행동 밖 변경"으로 기록되어 Undo되지 않는다.

### 2-6. 기록을 코드로 바꾸기 — `[BindTo]`

로그 ID에 `[BindTo]`를 연결하면 **모든** 반영 규칙에서 '로그에 기록' 대신 그 코드가 돈다. 기록은 코드가 `Report`로 한다(→ `13-BindTo.md` 3-3).

```csharp
[BindTo("huntScore")]
static void HuntScore(UnitKilled e, TriggerContext c) => c.Session.Stats.Report(c.Owner, e.IsBoss ? 10 : 1);
```

---

## 3. 상황별 예제

예제 이벤트:

```csharp
using System;
using System.Collections.Generic;
using GBible;

[GameEvent("유닛 처치")]
public class UnitKilled
{
    public string killerId, victimId;
    public int damage, combo;
    public List<string> tags = new List<string>();
    public bool IsBoss => tags.Contains("boss");
}

[GameEvent("유닛 포획")] public class UnitCaptured { public string unitId; }
[GameEvent("스테이지 클리어")] public class StageCleared { public string stage; public double seconds; public string difficulty; public int difficultyLevel; }
[GameEvent("골드 획득")] public class GoldEarned { public int amount; }
[GameEvent("골드 사용")] public class GoldSpent { public int amount; }
```

### 3-1. 처치 수(횟수)

| 칸 | 값 |
|---|---|
| ID | `kills` |
| 범위 · 집계 | 누적 · 합계 |
| 규칙 1 | `유닛 처치 받기` → 반영할 값 비움, 배수 1 |

### 3-2. 보스 처치 수(이벤트 값으로 거르기)

규칙 1: `유닛 처치 받기` + 이벤트 조건 `필드 검사` → `tags` `포함` `boss`(또는 `IsBoss` `참`). 요약: `유닛 처치 받기(tags에 boss 포함일 때) → 횟수를 이 로그에 반영`.

### 3-3. 준 피해 총합(값 반영)

규칙 1: `유닛 처치 받기` → 반영할 값 `damage`.

### 3-4. 최대 콤보(최고치)

범위 누적, 집계 **최고치**, 규칙 `유닛 처치 받기` → 반영할 값 `combo`. 도전과제 "10연속 처치"의 조건 `비교(bestCombo >= 10)`에 쓴다.

### 3-5. 최단 클리어 시간(최저치, 이번 판)

범위 **이번 판**, 집계 **최저치**, 규칙 `스테이지 클리어 받기` → 반영할 값 `seconds`. 같은 판 안에서 가장 빠른 기록이 남고, 새 판이면 비워진다.

### 3-6. 마지막 난이도(마지막 값)

집계 **마지막 값**, 규칙 `스테이지 클리어 받기` → 반영할 값 `difficultyLevel`(숫자 필드여야 한다. 글자 필드 `difficulty`를 넣으면 검증 오류가 난다).

### 3-7. 사냥 점수 = 일반 처치 1점 + 보스 처치 10점(규칙 두 줄)

| 규칙 | 트리거 | 반영할 값 · 배수 |
|---|---|---|
| 1 | `유닛 처치 받기`(`IsBoss` 거짓) | 비움 · 1 |
| 2 | `유닛 처치 받기`(`IsBoss` 참) | 비움 · 10 |

### 3-8. 사냥 수 = 처치 또는 포획(OR)

규칙 1의 트리거: `이벤트 받기(유닛 처치)` `OR` `이벤트 받기(유닛 포획)`, 반영할 값 비움. 반영할 값을 쓰려면 두 이벤트 모두에 같은 이름의 숫자 필드가 있어야 한다.

### 3-9. 골드 수지(배수로 빼기)

| 규칙 | 트리거 | 반영할 값 · 배수 |
|---|---|---|
| 1 | `골드 획득 받기` | `amount` · 1 |
| 2 | `골드 사용 받기` | `amount` · -1 |

### 3-10. 코드로 정의하고 확인하기(서버·테스트)

```csharp
var session = new GameSession(new SessionOptions { Seed = 1 });

var bossDamage = new StatDefinition { Id = "bossDamage", Aggregate = StatAggregate.Sum };
bossDamage.Triggers.Add(new TriggerRule
{
    when = new EventTrigger
    {
        eventType = typeof(UnitKilled).FullName,
        filter = new FieldFilter { path = "IsBoss", op = FieldOp.IsTrue },
    },
    then = { new RecordOutcome { field = "damage", multiplier = 2 } },
});
session.Stats.Define(bossDamage);

session.Events.Publish(new UnitKilled { damage = 10, tags = { "boss" } });
session.Events.Publish(new UnitKilled { damage = 99 });
session.Events.Publish(new UnitKilled { damage = 5, tags = { "boss" } });
// session.Stats.Get("bossDamage") == 30   (보스만, 피해 × 2)
```

### 3-11. 이벤트 없이 코드에서 바로 기록

```csharp
[Serializable]
public class EndTurn : GameAction
{
    protected override void Run()
    {
        Game.All<Battle>()[0].turn++;
        Game.Session.Stats.Report("turnsPlayed", 1);     // 로그 turnsPlayed는 규칙 없이 정의만 해 둔다
    }
}

[Serializable] public class Battle { public int turn; }
```

이벤트를 받는 쪽이 로그뿐이고 다른 기능(도전과제의 이벤트 받기, 연출)이 그 순간을 받을 일이 없을 때만 이렇게 쓴다. 보통은 이벤트를 쏘고 규칙으로 센다.

---

## 4. 주의할 점 / 자주 하는 실수

- **글자 필드를 반영하려 한다.** 반영할 값은 숫자 필드·속성만 된다.
- **최고치 로그에 반영할 값을 비운다.** 횟수 1이 매번 최고치로 들어가 언제나 1이다(검증 경고).
- **OR로 받은 두 이벤트의 필드 이름이 다르다.** 반영할 값은 두 이벤트 모두에서 읽을 수 있어야 한다. 다르면 규칙을 두 줄로 나눈다.
- **조건 충족을 로그 트리거에 넣으려 한다.** 로그 칸은 이벤트 받기만 된다. 상태(값)를 기록하고 싶으면 그 값이 바뀌는 곳에서 이벤트를 쏘거나 `Report`를 직접 부른다.
- **행동 밖에서 이벤트를 쏜다.** 씬 코드에서 바로 쏜 이벤트로 오른 로그는 Undo되지 않는다. 플레이어 입력은 행동으로 처리하고 그 안에서 쏜다.
- **정의되지 않은 ID로 `Get`·`Report`.** 예외("정의되지 않은 수치 '…'입니다")가 난다. `IsDefined`로 먼저 확인하거나 DB에 로그를 만든다.
- **`[BindTo]`로 연결하고 `Report`를 잊는다.** 연결된 로그는 자동 기록이 멈춘다.
- **범위를 잘못 고른다.** 이번 판 로그는 새 판에서 처음 값으로 돌아가고, 누적 로그는 프로필에 남는다. 도전과제 "누적 처치 1000"은 누적 로그로 센다.
