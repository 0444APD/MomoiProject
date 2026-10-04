# 17. 프로젝트 고유 기능 만들기(퀘스트 예)

**한 줄 요약**: 먼저 기본 종류 **패시브**(DB › 패시브)로 되는지 본다. 유물·특성·패시브 스킬처럼 "가지고 있으면 발동"하는 것은 코드 없이 패시브로 만든다(0절). 그 밖에 로그·도전과제·능력처럼 "Trigger → 결과" 구조를 가진 기능(퀘스트·일일 미션·튜토리얼 단계)은 프로젝트가 직접 만들 수 있다. DB 종류 클래스에 `Trigger` 필드와 `List<Outcome>` 필드를 두고 `[TriggerSlot]`·`[OutcomeSlot]` 표시만 붙이면 같은 편집 화면·검증이 붙고, 런타임에서는 `session.Triggers.Register`로 감시하고 `RunOutcomes`로 결과를 실행한다.

**언제 쓰는가**

- 유물·특성·장비 효과·축복처럼 가진 목록에 있는 동안 발동하는 것 → 기본 종류 패시브(0절). 새 DB 종류를 만들지 않는다.
- 퀘스트, 일일 미션, 튜토리얼 단계, 이벤트 기간 보상처럼 "언제(조건) → 무엇을(보상)"을 기획자가 DB에서 항목마다 조립해야 하는 기능(1절부터).
- 기본 기능(도전과제 = 한 번, 로그 = 숫자 기록, 패시브 = 가진 동안 매번)과 진행·저장·반복 규칙이 달라서 직접 다뤄야 할 때.

이 문서에서 쓰는 용어:

| 용어 | 뜻 |
|---|---|
| Trigger | 언제 발동하는지(→ `11-Triggers-Conditions.md`). |
| Outcome | 무엇을 하는지(→ `12-Outcomes.md`). |
| DB 종류 | `GameData`를 상속한 클래스. 상속하면 코드 추가 없이 DB에 탭이 생긴다. |
| 감시 | `session.Triggers`가 Trigger를 지켜보다가 참이 되는 순간 등록한 함수를 부르는 것(`Register`). |
| TriggerContext (컨텍스트) | 발동한 순간의 정보(`Owner`·`Event` …). |
| Permanent | `session.Data.Permanent()` 안의 변경은 Undo 이력에 들어가지 않는다(도전과제 달성이 쓰는 방식). |
| 패시브 | DB › 패시브의 항목 하나: 가진 목록 · 발동(Trigger) · 가진 쪽 필드 · 결과 · 아이콘. 코드 형식 `PassiveData`(DB), `PassiveDefinition`(실행 중). ID 상수는 `GameIds.Passive.X`. |
| 가진 목록 | 플레이어 데이터 안의 ID 목록·ID → 레벨 딕셔너리(`run › relics`, `run › perks`). 패시브 ID가 들어 있으면 가진 것이다. |
| 가진 쪽 | 가진 목록을 품은 객체(`run`, `hero`, 동료마다). 패시브의 결과가 그 쪽을 사용자로 돈다. |

---

## 0. 무엇으로 만드나 — 먼저 기본 종류 '패시브'로 되는지 본다

| 기획 문장 | 만드는 것 |
|---|---|
| "유물 닻: 전투 첫 턴에 방어도 8" · "가시 갑옷: 받는 피해 −5" | DB › 패시브(0-3), 코드 없음 |
| "특성 '중갑': 받는 피해 −2"(동료마다 다른 특성) | DB › 패시브, 가진 목록 `party[].traits`(가진 쪽 = 그 동료) |
| "패시브 스킬 레벨 n: 처치마다 체력 +n" | DB › 패시브, 가진 목록 = ID → 레벨 딕셔너리(`Game.Owned`) |
| "버프 3턴" · "독" · "쿨다운" | 상태 효과(29장). 걸고 풀고 끝나는 것 |
| "슬라임 5마리를 잡으면 보상(한 번)" · "일일 미션(하루 3번)" · "튜토리얼 단계" | 이 장의 프로젝트 고유 기능(1절부터) |

> **규칙: "가지고 있으면 발동"은 DB › 패시브로 만든다.** 유물·특성·패시브 스킬·장비 효과·축복이 모두 이것이다. 새 DB 종류(1절부터)는 완료 기록·한도·단계·선행처럼 패시브에 없는 규칙이 있을 때만 만든다. 영구 상태 효과로 걸어 두는 우회도 하지 않는다(29장 1절).

### 0-1. 동작 규칙

- 정의(DB › 패시브의 항목)는 게임이 켜질 때 모두 등록된다. 순간마다(이벤트를 받을 때·의도를 가로챌 때·조건 값이 바뀔 때) **가진 목록에 그 ID가 있는 쪽(가진 쪽)마다** 발동을 따로 검사하고, 참인 가진 쪽마다 결과를 낸다(상태 효과의 "가진 쪽마다 검사"와 같다, 29장 2-3).
- **가진 목록**: 플레이어 데이터 하나 + 그 안의 필드. 담는 모양에 따라 "몇 개 가졌나"(레벨)가 정해진다.

| 가진 목록의 모양 | 예 | 몇 개(레벨) |
|---|---|---|
| ID 목록(`List<string>`) | `run › relics` | 그 ID가 든 횟수 |
| ID → 레벨 딕셔너리(`Dictionary<string, int>`) | `run › perks` | 그 키의 숫자(없으면 0 = 가지지 않음) |
| 글자 필드 하나 | `run › hero.weaponPassive` | 같으면 1 |

요소의 `id`가 패시브 ID인 객체 목록도 센다(그 id인 요소 수).

- **가진 쪽** = 그 목록을 품은 객체. `run › relics`면 `run`(판 하나에 하나), `run › hero.perks`면 `hero`, 앞마디에 `[]`를 쓴 `run › party[].traits`면 그 목록을 가진 동료마다다.
- **결과의 컨텍스트**: `User` = 가진 쪽, `Picked` = 가진 쪽 하나, `Owner` = 패시브 ID, `Event` = 받은 이벤트(가로채기면 의도), `IsIntercept`. 결과 `대상에게 효과`의 대상을 비우거나 `사용자 자신`이면 가진 쪽이다. 가진 쪽이 `run`처럼 싸우는 개체가 아니면 대상 Block으로 고른다(기본 대상 Block 16장 2-6, 또는 프로젝트의 `주인공` Block — 가이드 04장 9절).
- **가진 쪽 필드**: 이벤트·의도의 그 필드가 가진 쪽 id일 때만 발동한다("내가 맞을 때만" = 피해 의도의 `targetId`). 가진 쪽에 `id`가 있어야 한다. `run`처럼 id가 없는 가진 쪽이면 이 칸을 채운 패시브는 발동하지 않으므로 의도 조건(`targetId 일치 hero`)으로 거른다.
- **얻기·잃기**는 가진 목록에 ID를 넣고 빼는 것이다(결과 `플레이어 데이터에 주기`·`가중치 표에서 주기`·`가중치 표에서 고르기`, 코드 `run.relics.Add(id)`). 가진 목록이 딕셔너리면 가중치 표에서 같은 ID를 다시 고를 때 그 키가 +1 된다(레벨업, 30장 3-3). 등록을 바꿀 일이 없고, 가진 것이 플레이어 데이터라 Undo·저장·멀티플레이가 따라온다.
- 상태 효과와 다른 점: 지속·중첩·만료가 없고, 가진 것이 게임의 데이터(유물 목록·특성 목록)에 그대로 있다.
- 이벤트 받기·가로채기로 발동한 결과는 그 행동 안에서 돈다. 조건 충족만으로 된 발동은 값이 바뀐 행동이 끝난 뒤의 평가에서 돈다(행동 밖, 29장 2-3과 같다). 판 값을 바꾸는 패시브는 이벤트 받기·가로채기로 발동시킨다.

### 0-2. 에디터에서 — DB › 패시브

`MomoiProject → DB` → 사이드바 **DB** 그룹의 **패시브** 탭 → **+ 새로 만들기** → ID(접두어 `passive`, 예: `thornArmor`). 저장하면 `GameIds.Passive.ThornArmor`가 생긴다.

| 칸 | 툴팁 | 값 |
|---|---|---|
| `ownerKey` | 가진 목록(플레이어 데이터) / 이 ID가 들어 있으면 가진 것 | 기본 `run` |
| `ownerPath` | 필드(목록·딕셔너리) / ID 목록·id가 있는 객체 목록·ID → 레벨 딕셔너리. 앞마디에 []를 쓰면 가진 쪽이 여럿(party[].traits) | 기본 `relics`. 비우면 값 전체 |
| `trigger` | 발동 | `+ 블럭`: 이벤트 받기 · 조건 충족 · 가로채기 |
| `holderField` | 가진 쪽 필드 / 이벤트·의도의 이 필드가 가진 쪽 id일 때만 발동합니다(예: 피해 의도의 targetId → 가진 쪽이 맞을 때만) | 트리거가 고른 이벤트·의도의 필드. 비우면 "가진 쪽 모두" |
| `outcomes` | 결과 | 결과 종류 전부(의도 고치기 포함) |
| `icon` | 아이콘 | `Sprite`. 규칙은 보지 않는다. 화면 코드가 유물 줄·특성 창에 그린다 |

- 결과를 코드로 쓰려면 그 ID에 `[BindTo]`(결과 칸이 잠긴다, 13장). 패시브는 기본 종류라 라이브러리가 연결을 스스로 확인한다(2-8의 `BoundOutcomes` 확인이 필요 없다).
- 검증 오류: "가진 목록의 플레이어 데이터를 고르세요." / "발동(트리거)이 없습니다." / "비어 있는 결과 칸이 있습니다(스크립트가 지워졌을 수 있음)." 경고: "결과가 없습니다."
- 모니터: 결과가 돌면 타임라인에 "결과: …"(출처 `Passive`)가 남는다. **데이터** 탭에서 가진 목록(`run › relics`)에 ID를 넣고 빼며 시험한다.

### 0-3. 예제 — 닻 · 가시 갑옷 · 동료 특성 · 레벨이 있는 패시브

가이드의 카드 던전 형식을 쓴다: 판 데이터 `run`(`RunState`)에 `hero`(id `hero`) · `relics`(`List<string>`)가 있고, 효과 Block `방어도`·`피해`, 의도 `피해`(`DamageIntent { sourceId, targetId, amount, cancelled }`)가 있다.

**닻 — 전투 첫 턴에 방어도 8**

1. DB › 패시브 `anchor`: `ownerKey` `run`, `ownerPath` `relics`.
2. `trigger`: `이벤트 받기` → `턴 시작`, 이벤트 조건 `turn` `일치` `1`.
3. `outcomes`: `대상에게 효과` → 대상 `주인공`(가이드의 프로젝트 대상 Block) → 효과 `방어도`(8).

가진 쪽이 `run`이라 대상을 비우면 주인공이 아니다(방어도 Block이 `Fighter`만 받아 `run`을 건너뛴다). 그래서 주인공을 고르는 대상 Block을 둔다. 가진 목록을 주인공 안에 두면(`run › hero.relics`) 가진 쪽이 주인공이 되어 대상을 비워도 된다.

**가시 갑옷 — 주인공이 받는 피해 −5**

1. DB › 패시브 `thornArmor`: 가진 목록 `run › relics`.
2. `trigger`: `가로채기` → 의도 `피해`, 의도 조건 `targetId` `일치` `hero`.
3. `outcomes`: `의도 고치기` → 필드 `amount`, 방법 `더하기`, 값 `-5`.

가진 쪽(`run`)에 id가 없으므로 "주인공이 맞을 때만"은 가진 쪽 필드가 아니라 의도 조건으로 정한다. 등록 코드는 없다. `run.relics`에 `thornArmor`를 넣으면 그때부터 줄어든다.

**동료 특성 — '중갑': 그 동료가 받는 피해 −2** — 동료가 있는 게임이라고 본다: `run › party`(`List<Unit>`, 동료마다 `id`), 동료마다 `traits`(`List<string>`).

1. DB › 패시브 `heavyArmor`: `ownerKey` `run`, `ownerPath` `party[].traits`.
2. `trigger`: `가로채기` → `피해`. `holderField` `targetId`.
3. `outcomes`: `의도 고치기` `amount` `더하기` `-2`.

`traits`에 `heavyArmor`가 든 동료마다 따로 검사하고, 가진 쪽 필드 덕에 그 동료가 맞을 때만 줄인다. 같은 특성을 가진 동료가 둘이어도 맞은 쪽 하나만 발동한다.

**레벨이 있는 패시브 — 적을 처치할 때마다 체력 + 레벨** — `run › perks`(`Dictionary<string, int>`, ID → 레벨), 처치 이벤트 `EnemyKilled`.

1. DB › 패시브 `bloodlust`: 가진 목록 `run › perks`, `trigger` `이벤트 받기` → `적 처치`. 결과는 레벨을 읽는 계산이라 코드로 쓴다.

```csharp
[BindTo(GameIds.Passive.Bloodlust)]
static void Bloodlust(EnemyKilled e, TriggerContext c)
{
    var level = c.Game.Owned(c.Owner, c.User);                 // 이 가진 쪽의 레벨(perks의 값)
    var hero = c.Game.Data().Run.hero;
    hero.hp = Math.Min(hero.maxHp, hero.hp + level);
}
```

2. 레벨업 보상: 결과 `가중치 표에서 고르기`(`table` `perkPool`, `count` `3`, `key` `run`, `path` `perks`). 고른 ID의 레벨이 1 오른다(없던 키면 1, 30장 3-3).

### 0-4. 코드로 — `Game.Owned` · `PassiveDefinition` · `session.Passives`

```csharp
int relics = Game.Owned(GameIds.Passive.Anchor);                       // 모든 가진 쪽의 합
int level  = Game.Owned(GameIds.Passive.Bloodlust, run);               // 그 가진 쪽만(객체 또는 id)
bool has   = Game.Owned(GameIds.Passive.ThornArmor) > 0;
```

| 멤버 | 뜻 |
|---|---|
| `int Owned(string passiveId, object holder = null)` | 몇 개 가졌나(레벨, 0-1의 표). `holder`를 비우면 모든 가진 쪽의 합. 없는 패시브 ID면 0. 값을 바꾸지 않으므로 화면 코드에서도 읽는다 |

| `PassiveDefinition` | 뜻 | DB 칸 |
|---|---|---|
| `Id` · `DisplayName` | ID · 표시 이름 | 항목 ID |
| `OwnerKey` · `OwnerPath` | 가진 목록(플레이어 데이터 · 필드) | `ownerKey` · `ownerPath` |
| `When` · `HolderField` | 발동 · 가진 쪽 필드 | `trigger` · `holderField` |
| `Outcomes` | 결과 | `outcomes` |

`session.Passives`(`PassiveSystem`): `Define(definition)`(등록, 같은 ID면 바꾼다. 발동도 이때 등록한다) · `Find(id)` · `Definitions` · `Owned(id, holder)`. 게임(Unity)에서는 런타임이 DB의 패시브 전부를 등록한다. 서버·테스트처럼 세션을 직접 만들면 직접 등록한다.

```csharp
foreach (var passive in GameDatabase.Load().All<PassiveData>()) session.Passives.Define(passive.ToDefinition());
```

- `PassiveData.ToDefinition()`은 그 ID에 `[BindTo]`가 있으면 `Outcomes`를 그 코드로 바꾼다.
- 의도 고치기의 값 틀 `{stacks}`(28장 3-4)는 상태 효과의 중첩이다. 패시브의 레벨은 코드에서 `Game.Owned(c.Owner, c.User)`로 읽는다.
- 아이콘은 DB 항목에만 있다: `GameDatabase.Load().All<PassiveData>()`에서 ID로 찾아 `icon`을 읽는다.

### 0-5. 패시브로 안 되는 것 — 이 장의 고유 기능으로

- **한 번만** 발동하고 완료를 저장한다(퀘스트·튜토리얼). 패시브는 가진 동안 매번 발동한다.
- 하루·한 판에 **N번까지**, **단계**를 차례로 넘어간다, **선행** 항목이 있다(4절).
- 같은 항목에 칸(가격·등급·설명)을 더해야 한다. `PassiveData`에는 칸을 더할 수 없다. 이름·설명은 텍스트 표(`<ID>.name`·`<ID>.desc`), 수치는 밸런스 값(의도 고치기의 값 틀 `{balance:ID}`, 28장 3-4)에 두면 패시브로 충분하다. 꼭 한 항목에 있어야 하면 DB 종류를 만든다(1절부터).

---

## 1. 에디터에서 쓰는 법

코드(2장)를 쓰고 컴파일하면:

1. `MomoiProject → DB` 사이드바에 `[Database]`에서 정한 이름의 탭(예: **진행** 그룹의 **퀘스트**)이 생긴다.
2. `+ 퀘스트`로 항목을 만들고 제목(= ID)을 짓는다. DB ID는 모든 종류에서 하나뿐이다(퀘스트 ID가 도전과제 ID와 겹칠 수 없다).
3. Trigger 필드(`trigger`)는 한 줄 식 편집기로, 결과 필드(`outcomes`)는 결과 목록 편집기로 그려진다. `+ 블럭`·`+ 결과 추가` 목록에는 필드의 잠금에 있는 종류만 나온다.
4. 검증(잠금 밖 종류, 비어 있는 칸, 플레이어 데이터 경로, 읽을 이벤트가 없는 반영할 값 …)이 기본 기능과 같은 규칙으로 돈다. 클래스의 `ValidateData`로 기능 고유의 검사를 더할 수 있다.
5. 결과를 코드로 쓰려면 `[BindTo(퀘스트 ID)]`. 에디터는 그 항목의 결과 칸을 잠그고 연결된 코드를 보인다. 런타임에서 그것을 결과로 쓰는 일은 기능 코드가 한다(2-8).

---

## 2. 코드로 쓰는 법 — 문법 요소를 하나씩

### 2-1. DB 종류: `GameData` + `[Database]`

```csharp
using System.Collections.Generic;
using GBible;
using GBible.Data;
using UnityEngine;

[Database("퀘스트", Group = "진행", Order = 30, IdPrefix = "quest")]
public class QuestData : GameData
{
}
```

| `[Database]` 인자 | 뜻 |
|---|---|
| 첫 값 | 탭 이름 |
| `Group` | 사이드바 그룹(기본 기능은 `선언`·`진행`·`DB`·`개발`을 쓴다) |
| `Order` | 그룹 안 순서 |
| `IdPrefix` | 새 항목 ID 앞부분(없으면 클래스 이름에서) |

`GameData`가 ID(`Id`)와 검증 훅(`ValidateData`)을 준다. 표시 이름 필드는 따로 두지 않는다(ID가 이름이다).

### 2-2. Trigger 칸: `[SerializeReference, TriggerSlot(…)]`

```csharp
[FieldLabel("트리거"), SerializeReference, TriggerSlot(TriggerKinds.Event | TriggerKinds.Condition)]
public Trigger trigger;
```

- `[SerializeReference]` — 필수. `Trigger`는 추상 형식이라 실제 형식째 저장해야 한다.
- `[TriggerSlot(TriggerKinds)]` — 고를 수 있는 잎. 등록해서 감시하는 기능은 `Event`·`Condition`을 쓴다(`Use`는 `Game.Use`로 쓰는 능력에만 의미가 있다).
- `[FieldLabel("…")]` — 마우스를 올렸을 때 보이는 설명(칸 이름표는 변수 이름).

### 2-3. 결과 칸: `[SerializeReference, OutcomeSlot(…)]`

```csharp
[FieldLabel("결과"), SerializeReference, OutcomeSlot(OutcomeKinds.PlayerData | OutcomeKinds.Coded | OutcomeKinds.Targeted)]
public List<Outcome> outcomes = new List<Outcome>();
```

- `OutcomeKinds.All`이면 로그에 기록·이벤트 쏘기까지 전부.
- `Fixed = true`면 그 종류 하나로 고정된다(로그처럼 세부 칸만).

### 2-4. 규칙 여러 줄: `List<TriggerRule>`

```csharp
[FieldLabel("단계"), TriggerSlot(TriggerKinds.Event | TriggerKinds.Condition), OutcomeSlot(OutcomeKinds.All)]
public List<TriggerRule> steps = new List<TriggerRule>();
```

`TriggerRule` = `when`(Trigger) + `then`(결과 리스트). 리스트 필드에 두 표시를 붙이면 규칙마다의 `when`·`then`에 적용된다. `TriggerRule`의 필드에는 이미 `[SerializeReference]`가 있다.

### 2-5. 고유 검사: `ValidateData`

```csharp
public override void ValidateData(IValidationContext context)
{
    if (trigger == null) context.Error("트리거가 없습니다(언제 완료되는지).");
    else trigger.Validate(context);
    if (outcomes.Count == 0) context.Warning("결과가 없습니다.");
    foreach (var outcome in outcomes)
        if (outcome == null) context.Error("비어 있는 결과 칸이 있습니다.");
        else outcome.Validate(context);
}
```

`context.Error`는 빌드를 막고, `context.Warning`은 표시만 한다.

### 2-6. 등록 함수는 Rules에, 연결은 Bootstrap에

기능 하나를 두 곳에 나눠 쓴다.

| 무엇 | 어디 | 왜 |
|---|---|---|
| **등록 함수** — 세션과 DB 항목을 받아 감시를 등록하는 함수(`QuestRules.Register(session, quests)`) | Rules(규칙 코드) | 게임 규칙이라 서버에서도 그대로 쓴다. 서버는 세션을 직접 만든 뒤 같은 함수를 부른다 |
| **연결** — Unity가 켜질 때 그 함수를 부르는 한 줄 | Client의 `Bootstrap.Started` | Unity 시작 방식(`GBibleRuntime.Started`)은 클라이언트에만 있다 |

```csharp
// Rules 쪽: 등록 함수(Unity 시작 방식을 모른다)
public static class QuestRules
{
    public static void Register(GameSession session, IEnumerable<QuestData> quests) { /* 2-7 */ }
}

// Client의 Bootstrap.cs: 연결 한 줄
static void Started(GameSession session)
{
    QuestRules.Register(session, GameDatabase.Load()?.All<QuestData>());
}
```

`Bootstrap.Started`는 `GBibleRuntime.Started`에 연결된 함수다(`20-Setup.md` 4절). 세션이 준비된 뒤(플레이어 데이터·로그·능력·도전과제 정의와 프로필 불러오기가 끝난 뒤) 불린다. 기능마다 자기 `[RuntimeInitializeOnLoadMethod]`와 `Started +=`를 따로 두지 않는다. 그러면 연결이 여러 파일에 흩어지고, 서버에서 같은 등록을 부를 길이 없다.

> [!note]- 예: 카드 던전의 유물
> 유물은 기본 종류 패시브(0절)로 등록 코드 없이 만들 수 있다. 아래는 프로젝트 기능의 등록 함수 모양을 보이는 예다(패시브에 없는 규칙을 더할 때 이 모양에서 시작한다).
>
> ```csharp
> // Rules/Relics/Relics.cs
> public static class RelicRules
> {
>     public static void Register(GameSession session, IEnumerable<RelicData> relics)
>     {
>         if (relics == null) return;
>         foreach (var relic in relics)
>         {
>             var id = relic.Id;
>             IEnumerable<Outcome> outcomes = BoundOutcomes.Has(id) ? BoundOutcomes.Create(id) : relic.outcomes;
>             session.Triggers.Register(id, relic.trigger, repeat: true, fire: moment =>
>             {
>                 var run = session.Game.Data().Run;
>                 if (run == null || !run.relics.Contains(id)) return;   // 가지고 있을 때만
>                 session.Triggers.RunOutcomes(outcomes, moment, "Relics");
>             });
>         }
>     }
> }
>
> // Client/Bootstrap.cs
> static void Started(GameSession session)
> {
>     RelicRules.Register(session, GameDatabase.Load()?.All<RelicData>());
> }
> ```

### 2-7. 감시 등록: `session.Triggers.Register(owner, when, repeat, fire)`

```csharp
session.Triggers.Register(quest.Id, quest.trigger, repeat: false, fire: moment => { /* 발동 */ });
```

| 인자 | 뜻 |
|---|---|
| `owner` | 항목 ID. 컨텍스트의 `Owner`가 되고, `Unregister(owner)`로 그만둘 때 쓴다 |
| `when` | Trigger(`null`이면 아무것도 하지 않는다) |
| `repeat` | `false`: 한 번 발동하면 감시를 그만둔다 / `true`: 계속(조건만으로 된 Trigger는 거짓이 된 뒤 다시 참이 될 때 또) |
| `fire` | 발동했을 때 불리는 함수. 인자는 그 순간의 `TriggerContext` |

- 이벤트 받기 잎은 이벤트를 받을 때, 조건 충족 잎은 그 조건이 읽는 값이 바뀔 때(행동 끝·프레임 끝의 Flush) 검사된다.
- 등록할 때 이미 참인 조건은 값이 다시 바뀌기 전까지 발동하지 않는다. 시작할 때 소급하려면 등록을 마친 뒤 `session.Triggers.Watcher.EvaluateAll()`을 부른다.
- 그 밖에: `Unregister(owner)`, `IsRegistered(owner)`.

### 2-8. 결과 실행: `RunOutcomes` + `[BindTo]` 확인

```csharp
IEnumerable<Outcome> outcomes = BoundOutcomes.Has(quest.Id) ? BoundOutcomes.Create(quest.Id) : quest.outcomes;
// fire 안에서:
session.Triggers.RunOutcomes(outcomes, moment, "Quests");
```

- `RunOutcomes(결과들, 컨텍스트, 출처)`는 결과를 차례로 실행하고, 결과마다 Journal에 "결과: …"(출처 `Quests`)를 남긴다. 결과가 꺼낸 객체를 고쳐도 기록된다.
- `[BindTo]` 연결은 기본 기능(도전과제·로그·능력·개발 명령)만 스스로 확인한다. 프로젝트 기능은 `BoundOutcomes.Has(id)`로 직접 확인해 바꿔 써야 한다. 확인하지 않으면 에디터에는 결과 칸이 잠겨 보이는데 실제로는 숨은 예전 결과가 돈다.

### 2-9. 완료 저장과 Undo: 플레이어 데이터 + `Permanent`

```csharp
[System.Serializable]
public class QuestLog
{
    public List<string> done = new List<string>();
}
// DB › 플레이어 데이터: ID "quest.log", 구역 메타, 값 형식 QuestLog
```

```csharp
using (session.Data.Permanent())
{
    session.Data.Modify<QuestLog>("quest.log", log => log.done.Add(quest.Id));
    session.Triggers.RunOutcomes(outcomes, moment, "Quests");
}
```

- 완료 여부는 기능이 직접 플레이어 데이터에 둔다(메타 구역이면 프로필에 저장된다). 시작할 때 이미 완료한 것은 등록하지 않는다.
- `Permanent()` 안의 변경은 Undo 이력에 들어가지 않는다. 도전과제처럼 "완료와 보상은 Undo로 취소되지 않는다"를 원하면 이렇게 감싼다. `repeat: false`로 등록한 감시는 발동하면 끝나므로, 완료를 Undo로 되돌릴 수 있게 두면 다시 완료할 길이 없어진다.

---

## 3. 전체 예제: 퀘스트

### 3-1. 코드

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using GBible;
using GBible.Data;
using UnityEngine;

// ── 데이터 ──────────────────────────────────────────────────
[Database("퀘스트", Group = "진행", Order = 30, IdPrefix = "quest")]
public class QuestData : GameData
{
    [FieldLabel("선행 퀘스트"), DataRef(typeof(QuestData))] public string requires = "";
    [FieldLabel("트리거"), SerializeReference, TriggerSlot(TriggerKinds.Event | TriggerKinds.Condition)]
    public Trigger trigger;
    [FieldLabel("결과"), SerializeReference, OutcomeSlot(OutcomeKinds.PlayerData | OutcomeKinds.Coded | OutcomeKinds.Targeted)]
    public List<Outcome> outcomes = new List<Outcome>();

    public override void ValidateData(IValidationContext context)
    {
        if (trigger == null) context.Error("트리거가 없습니다(언제 완료되는지).");
        else trigger.Validate(context);
        foreach (var outcome in outcomes)
            if (outcome == null) context.Error("비어 있는 결과 칸이 있습니다.");
            else outcome.Validate(context);
    }
}

// 완료 기록(DB › 플레이어 데이터: "quest.log", 메타 구역, 값 형식 QuestLog)
[Serializable]
public class QuestLog
{
    public List<string> done = new List<string>();
}

// 완료를 다른 기능(로그 "완료한 퀘스트 수", 연출)이 받을 수 있게 쏘는 이벤트
[GameEvent("퀘스트 완료")]
public class QuestCompleted
{
    public string questId;
}

// ── 규칙(Rules): 등록 함수. 서버에서도 그대로 부른다 ──────────────
public static class QuestRules
{
    const string LogKey = "quest.log";

    public static void Register(GameSession session, IEnumerable<QuestData> quests)
    {
        if (quests == null || !session.Data.IsDefined(LogKey)) return;
        var all = quests.ToList();
        foreach (var quest in all) Watch(session, quest, all);
        session.Triggers.Watcher.EvaluateAll();           // 이미 조건을 넘은 퀘스트도 완료
    }

    // 완료하지 않았고, 선행 퀘스트를 마친 퀘스트만 감시한다.
    static void Watch(GameSession session, QuestData quest, List<QuestData> all)
    {
        var log = session.Data.Get<QuestLog>(LogKey);
        if (log.done.Contains(quest.Id) || session.Triggers.IsRegistered(quest.Id)) return;
        if (!string.IsNullOrEmpty(quest.requires) && !log.done.Contains(quest.requires)) return;

        var id = quest.Id;
        IEnumerable<Outcome> outcomes = BoundOutcomes.Has(id) ? BoundOutcomes.Create(id) : quest.outcomes;
        session.Triggers.Register(id, quest.trigger, repeat: false, fire: moment =>
        {
            session.Journal.Append(JournalKind.System, "Quests", $"퀘스트 완료: {id}");
            using (session.Data.Permanent())
            {
                session.Data.Modify<QuestLog>(LogKey, l => l.done.Add(id));
                session.Triggers.RunOutcomes(outcomes, moment, "Quests");
                session.Events.Publish(new QuestCompleted { questId = id });
            }
            // 이 퀘스트를 선행으로 가진 퀘스트를 열어 준다.
            foreach (var next in all.Where(q => q.requires == id)) Watch(session, next, all);
        });
    }

    // UI용: 진행도(조건 잎이 있을 때)
    public static string Progress(GameSession session, QuestData quest)
    {
        if (session.Data.Get<QuestLog>(LogKey).done.Contains(quest.Id)) return "완료";
        return quest.trigger != null && quest.trigger.TryGetProgress(session, out var now, out var goal) ? $"{now} / {goal}" : "진행 중";
    }
}

// ── 연결(Client/Bootstrap.cs의 Started에 한 줄) ──────────────────
// QuestRules.Register(session, GameDatabase.Load()?.All<QuestData>());
```

### 3-2. 에디터에서 퀘스트 조립

| 퀘스트 ID | `requires` | `trigger` | `outcomes` |
|---|---|---|---|
| `quest.slimes` | (비움) | `비교(slimeKills >= 5)` | `party › gold` 증감 100 |
| `quest.boss` | `quest.slimes` | `이벤트 받기(유닛 처치, IsBoss 참)` | `party › titles`에 `보스 사냥꾼` + `party › gear`에 인스턴스 `bossSword` |
| `quest.rich` | (비움) | `플레이어 데이터 검사(party: gold 5000 이상)` OR `도전과제 달성(hunter100)` | 코딩한 결과 `골드 이자` |

- `slimeKills`는 로그(유닛 처치 받기 + 이벤트 조건 `tags` 포함 `slime`)다.
- `quest.boss`는 `quest.slimes`를 마친 순간부터 감시된다.

### 3-3. 결과를 코드로(`[BindTo]`)

(`UnitKilled`·`Unit`·`Party`, 그리고 3-2의 `골드 이자`는 `12-Outcomes.md` 4장의 예제 형식이다.)

```csharp
public static class QuestRewards
{
    [BindTo("quest.boss")]
    static void BossQuest(UnitKilled e, TriggerContext c)
    {
        if (c.Game.Find<Unit>(e.killerId) is Unit killer) killer.maxHp += 10;
        c.Session.Data.Get<Party>("party").titles.Add("보스 사냥꾼");
    }
}
```

`QuestRules.Watch`가 `BoundOutcomes`를 확인하므로 이 함수가 `quest.boss`의 결과가 된다(에디터의 결과 칸은 잠긴다).

### 3-4. 완료한 퀘스트 수를 로그로

DB › 로그 `questsDone`: 반영 규칙 `퀘스트 완료 받기`(반영할 값 비움). 도전과제 "퀘스트 10개 완료" = `비교(questsDone >= 10)`.

---

## 4. 변형

### 4-1. 반복 미션(`repeat: true`) — 판마다 한도 · 하루마다 한도(일일 미션)

`repeat: true`로 등록하면 발동할 때마다 결과가 돈다. 한도는 **횟수를 담는 플레이어 데이터가 언제 비워지느냐**로 정해진다. 세션 구역에 두면 새 판에서 비워지므로 "판마다 N번"이다. **하루**에 N번(일일 미션)은 날짜가 바뀌었는지를 현실 시각으로 보고 직접 비운다. 날짜 번호는 `Game.Day(초기화 시각)`(07장)다.

```csharp
[Database("일일 미션", Group = "진행", IdPrefix = "daily")]
public class DailyMissionData : GameData
{
    [SerializeReference, TriggerSlot(TriggerKinds.Event)] public Trigger trigger;
    [SerializeReference, OutcomeSlot(OutcomeKinds.PlayerData | OutcomeKinds.Coded)] public List<Outcome> outcomes = new List<Outcome>();
    public int maxPerDay = 3;
}

[Serializable]
public class DailyLog
{
    public long day = -1;                                                  // 마지막으로 센 날짜 번호(Game.Day)
    public Dictionary<string, int> counts = new Dictionary<string, int>();
}   // 플레이어 데이터 "daily.log"(메타 구역 — 판이 바뀌어도 그날 센 횟수가 남는다)

const int ResetHour = 4;                                                   // 새벽 4시에 하루가 바뀐다

static void WatchDaily(GameSession session, DailyMissionData mission)
{
    var id = mission.Id;
    IEnumerable<Outcome> outcomes = BoundOutcomes.Has(id) ? BoundOutcomes.Create(id) : mission.outcomes;
    session.Triggers.Register(id, mission.trigger, repeat: true, fire: moment =>
    {
        var log = session.Data.Get<DailyLog>("daily.log");
        var today = session.Game.Day(ResetHour);                           // 행동 안이면 그 행동의 시각(Undo·멀티에서 같다)
        if (log.day != today) { log.day = today; log.counts.Clear(); }     // 날이 바뀌었으면 처음부터
        log.counts.TryGetValue(id, out var n);
        if (n >= mission.maxPerDay) return;              // 오늘의 한도
        log.counts[id] = n + 1;                          // 결과와 함께 기록된다
        session.Triggers.RunOutcomes(outcomes, moment, "Daily");
    });
}
```

- **판마다 한도**면 `daily.log`를 세션 구역에 두고 날짜 칸과 날짜 비교를 뺀다(새 판이면 비워진다). 이름도 '판마다 미션'처럼 짓는다.
- 날짜는 `DateTime.Now`가 아니라 `Game.Day`로 본다. 행동 안에서는 그 행동이 시작한 시각 하나라서 Undo·다시 돌리기·멀티플레이에서 같은 날로 읽힌다. 시간대는 `SessionOptions.TimeZone`(비우면 이 컴퓨터, 07장).
- 화면의 "오늘 남은 횟수"도 같은 비교로 읽는다: `log.day != Game.Day(4)`이면 오늘은 아직 0번이다(행동 밖의 `Game.Day`는 지금 시각).
- 주마다 초기화는 `Game.Week(초기화 시각)`로 같은 모양이다.
- `Permanent()` 없이 실행하면, 이벤트를 쏜 행동 안에서 발동한 미션의 횟수·보상은 그 행동의 Undo 단위가 된다(Undo하면 함께 돌아간다).

### 4-2. 단계 퀘스트(`List<TriggerRule>`)

```csharp
[Database("연계 퀘스트", Group = "진행", IdPrefix = "chain")]
public class ChainQuestData : GameData
{
    [TriggerSlot(TriggerKinds.Event | TriggerKinds.Condition), OutcomeSlot(OutcomeKinds.PlayerData | OutcomeKinds.Coded)]
    public List<TriggerRule> steps = new List<TriggerRule>();
}

[Serializable]
public class ChainLog { public Dictionary<string, int> step = new Dictionary<string, int>(); }   // 플레이어 데이터 "chain.log"(메타)

static void WatchStep(GameSession session, ChainQuestData quest)
{
    var log = session.Data.Get<ChainLog>("chain.log");
    log.step.TryGetValue(quest.Id, out var index);
    if (index >= quest.steps.Count) return;              // 다 끝남
    var rule = quest.steps[index];
    session.Triggers.Register(quest.Id, rule.when, repeat: false, fire: moment =>
    {
        using (session.Data.Permanent())
        {
            session.Data.Modify<ChainLog>("chain.log", l => l.step[quest.Id] = index + 1);
            session.Triggers.RunOutcomes(rule.then, moment, "ChainQuests");
        }
        WatchStep(session, quest);                       // 다음 단계
    });
}
```

에디터에서는 `steps`의 `+ 추가`로 단계를 더하고, 단계마다 트리거·결과를 조립한다.

### 4-3. Unity 없이(서버·테스트)

DB 없이도 같은 API로 돈다. 등록 함수를 Rules에 둔 기능은 서버가 세션을 만든 뒤 같은 함수(`QuestRules.Register(session, quests)`)를 부르면 된다. 아래는 Trigger·결과를 코드로 만들어 직접 등록하는 예다.

```csharp
var session = new GameSession();
session.Data.Define("quest.log", DataZone.Meta, new QuestLog());
session.Data.Define("party", DataZone.Meta, new Party());
var when = new EventTrigger { eventType = typeof(QuestCompleted).FullName };
var rewards = new List<Outcome> { new PlayerDataReward { key = "party", path = "gold", amount = 50 } };
session.Triggers.Register("bonus", when, repeat: true, fire: m => session.Triggers.RunOutcomes(rewards, m, "Bonus"));
```

값을 직접 바꾼 뒤 조건 잎을 검사하게 하려면 `session.Triggers.Flush()`를 부른다(행동으로 바꾸면 자동).

---

## 5. 주의할 점 / 자주 하는 실수

- **"가지고 있으면 발동"을 새 DB 종류로 만든다.** 유물·특성·장비 효과는 기본 종류 패시브(0절)로 등록 코드 없이 된다. 이 장의 방식은 패시브에 없는 규칙(한 번만·한도·단계·선행)이 있을 때만.
- **패시브의 가진 쪽 필드를 `run › relics`에 쓴다.** 가진 쪽(`run`)에 id가 없어 발동하지 않는다. 의도 조건(`targetId 일치 hero`)으로 거르거나, 가진 목록을 id가 있는 객체 안에 둔다(0-1).
- **일일 미션의 횟수를 세션 구역에 둔다.** 새 판이면 비워져 하루가 아니라 판마다 한도가 된다. 메타 구역 + `Game.Day`로 날짜가 바뀔 때 비운다(4-1).
- **`[BindTo]`를 확인하지 않는다.** 에디터는 연결된 항목의 결과 칸을 잠그지만, 런타임에서 그 코드를 쓰는 것은 기능 코드의 몫이다. `BoundOutcomes.Has(id) ? BoundOutcomes.Create(id) : 결과` 한 줄을 넣는다.
- **완료를 저장하지 않는다.** `repeat: false` 감시는 세션 안에서만 한 번이다. 저장하지 않으면 다음 실행에서 다시 발동한다.
- **시작할 때 이미 참인 조건.** 등록만으로는 발동하지 않는다. 등록 뒤 `session.Triggers.Watcher.EvaluateAll()`.
- **`Use` 잎을 허락한다.** 등록한 감시는 `사용`으로 발동하지 않는다(`사용`은 `Game.Use`의 사용 조건 검사에서만 참이다). 감시하는 기능의 잠금은 `Event`·`Condition`으로 둔다.
- **`fire` 안에서 결과를 직접 부른다.** `outcome.Run(…)`을 직접 부르지 말고 `RunOutcomes`를 쓴다. 그래야 컨텍스트(`Current`)가 결과·조건 Block에 보이고 Journal에 남는다.
- **`fire` 안에서 행동을 실행할 때**: 이벤트는 대개 행동 안에서 쏘이므로 `fire`도 행동 안에서 불린다. 여기서 부른 `Game.Do`·`Game.Use`는 그 행동의 일부로 실행된다. 조건 충족으로 발동하면(행동이 끝난 뒤) 새 행동이 된다.
- **로그에 기록을 허락하고 로그 칸을 비운다.** 프로젝트 기능에서 로그에 기록의 로그 칸을 비우면 그 항목 ID는 로그가 아니라 아무것도 기록하지 않고 경고만 남는다. `OutcomeKinds.Record`를 허락한다면 로그를 꼭 고르게 하거나 `ValidateData`로 검사한다.
- **인스턴스 기능 필드로 트리거·결과를 둘 때**: 인스턴스 값 안의 트리거·결과도 DB와 같은 편집기로 그려진다. 다만 원본 하나에 하나씩 정하는 값이라, 여러 항목이 공유하거나 기획자가 따로 관리할 기능은 DB 종류(`GameData`)로 만든다.
