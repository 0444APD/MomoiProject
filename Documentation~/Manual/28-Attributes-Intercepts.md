# 28. 능력치·Modifier·가로채기·비용

**한 줄 요약**: 캐릭터의 공격력·방어력 같은 **능력치**는 객체의 숫자 필드이고, 장비·버프·유물이 붙이는 **Modifier**가 최종값을 만든다(같은 칸은 더하고 칸끼리는 곱한다). **가로채기**는 피해·죽음·비용 같은 일이 일어나기 **직전에** 그 내용(**의도**)을 고치거나 막는 단계다. **비용**(`Game.Pay`)은 가로채기를 거친 뒤 값을 내고, 모자라면 행동 전체를 취소한다. 모두 행동 안에서 돌아 Undo·저장·멀티플레이가 그대로 따라온다.

**언제 쓰는가**

- 장비를 끼면 공격력 +30, 버프 동안 +20%, 벗거나 끝나면 원래대로(Modifier).
- 판이 끝나도 남는 영구 강화(영구 Modifier).
- "받는 피해 −5", "죽는 대신 체력 1", "이번 턴 비용 0", "다음 스킬 2회"처럼 **효과를 고치는 효과**(가로채기).
- 에너지·마나·골드를 내고, 모자라면 아무것도 바뀌지 않게 할 때(비용).

이 문서에서 쓰는 용어:

| 용어 | 뜻 |
|---|---|
| 능력치 (Attribute) | `id`가 있는 객체(유닛·카드 …)의 숫자 필드(`public double attack;`). 필드 값이 기본값이다. 따로 선언하지 않는다. |
| Modifier | 능력치 하나에 붙는 보정 한 줄: 대상 id · 필드 이름 · 칸 · 값 · 출처. 플레이어 데이터에 저장된다. |
| 칸 (`ModifierKind`) | Modifier가 들어가는 자리. 플랫(`Flat`, +30) · 퍼센트(`Percent`, 0.2 = +20%) · 독립 곱(`Multiply`, 0.5 = ×1.5). |
| 출처 (source) | Modifier를 붙인 쪽의 ID(장비·유물·능력 …). 같은 출처의 Modifier를 한꺼번에 뗀다. |
| 의도 (Intent) | 곧 일어날 일의 내용을 담은 클래스(`DamageIntent { targetId, amount, cancelled }`). 프로젝트가 정한다. |
| 가로채기 (Intercept) | 의도를 일이 일어나기 직전에 고치거나 막는 단계. 트리거 잎 `가로채기` + 결과 `의도 고치기`(또는 `[BindTo]` 코드)로 만든다. |
| 규칙 함수 (중간층) | 행동·Block·`[BindTo]`가 같이 쓰는 프로젝트의 평범한 static 함수(`Rules.Damage`). 피해 계산 순서와 가로채기 자리가 여기 있다. |
| 비용 (Cost) | 플레이어 데이터의 숫자를 내는 일. `Game.Pay`·결과 `비용 내기`. 기본 제공 의도 `CostIntent`를 거친다. |
| 컨텍스트 (`TriggerContext`) | 발동한 순간의 정보. 가로채기로 발동하면 `IsIntercept`가 참이고 `Event`가 의도 객체다. |

> **"Stat"이 아니라 능력치.** GBible에서 Stat은 로그(15장, `StatData`·`session.Stats`)를 가리킨다. 캐릭터의 공격력·방어력은 **능력치(Attribute)**라고 부른다.

---

## 1. 효과의 문법 — 만들 수 있는 효과의 천장

기획서의 효과 문장은 네 부품의 조합으로 쪼갤 수 있다.

> **언제** + **누구에게** + **무엇을** + **안전장치**

이 문법이 GBible이 표현하려는 효과의 **천장**(끝)이다. 문장을 이 부품으로 쪼갤 수 있으면 만들 수 있고, 어느 부품에도 들지 않으면 효과가 아니라 화면 코드나 다른 시스템의 일이다.

| 부품 | 종류 | GBible에서 | 상태 |
|---|---|---|---|
| 언제 | 즉시 | 결과 Block · `[BindTo]` | 있음 |
| | 발동("~하면") | Trigger(이벤트 받기 · 조건 충족 · 사용 · 가로채기) | 있음 |
| | 지연("2턴 뒤") | 상태 효과의 끝날 때 결과 · 예약(`Game.ScheduleAfter`·`ScheduleOn`) | 있음(→ `29-Statuses-Schedules-Choices.md`) |
| | 지속("3턴 동안") | Modifier · 상태 효과 | 있음(상태 효과 → 29장) |
| 누구에게 | 대상 고르기 | 대상 Block(`TargetSelector`) | 있음 |
| 무엇을 | 데이터 바꾸기 | 플레이어 데이터에 주기 · 효과 Block · 능력치 보정 | 있음 |
| | 만들기 · 없애기 · 바꾸기(소환·제거·변신) | 효과 Block · `[BindTo]`(새 개체의 id는 `Game.NewId`) | 있음 |
| | 가로채기("대신", "막는다", "−5") | 가로채기 + 의도 고치기 | 있음 |
| | 선택 요구("카드 1장을 고른다") | 선택 대기(`Game.Ask` · 결과 `선택 요구`) | 있음(→ 29장) |
| | 비용 | `Game.Pay` · 비용 내기 | 있음 |
| 안전장치 | 연쇄 제한 | 사슬 16단계에서 끊기(`TriggerSystem.MaxDepth`) | 있음 |

**문법 밖**: 실시간 물리·이동(화면 코드의 자유 영역. 규칙에 필요한 결과만 행동으로 보낸다), 게임 상태를 통째로 되감는 효과("3턴 전으로 시간 되돌리기").

쪼개 보기:

| 기획 문장 | 언제 | 누구에게 | 무엇을 |
|---|---|---|---|
| "적에게 피해를 줄 때마다 그 적에게 화상 1" | 발동(피해 받기) | 그 적 | 데이터 바꾸기 |
| "착용하는 동안 공격력 +30" | 지속 | 착용한 유닛 | 데이터 바꾸기(Modifier) |
| "주인공이 받는 피해 −5" | 발동(피해 직전) | 그 피해 | 가로채기 |
| "에너지 2를 내고 모든 적에게 8 피해" | 즉시 | 모든 적 | 비용 + 데이터 바꾸기 |

---

## 2. 동작 규칙

### 2-1. 최종값 — 같은 칸은 합, 칸끼리는 곱

```
최종값 = (기본값 + 플랫 합) × (1 + 퍼센트 합) × (1 + 독립 곱₁) × (1 + 독립 곱₂) …
```

- **같은 칸은 더한다.** 퍼센트 +20%와 +30%는 +50%다(×1.2×1.3이 아니다). 그래서 퍼센트를 많이 쌓아도 곱으로 불어나지 않는다(리그 오브 레전드식).
- **칸끼리는 곱한다.** 한 칸의 결과가 10% 오르면 최종값도 10% 오른다(가치 보존).
- **독립 곱은 하나하나 따로 곱해진다.** 드물게 쓰는 강한 효과(궁극기 ×1.5)용이다. 기본은 비워 둔다.
- **기본값(필드)은 바뀌지 않는다.** 화면·규칙은 `Game.Attribute`로 최종값을 읽는다.

예: 기본 공격력 100, 검 플랫 +20, 반지 퍼센트 +20%, 오라 퍼센트 +30%, 궁극기 독립 곱 +50%
→ (100 + 20) × (1 + 0.2 + 0.3) × (1 + 0.5) = **270**. `hero.attack`은 여전히 100이다.

### 2-2. Modifier가 사는 곳

| 구분 | 플레이어 데이터 키 | 구역 | 새 판이면 |
|---|---|---|---|
| 이번 판(기본) | `$modifiers` | 세션 | 비워진다 |
| 영구(`permanent`) | `$modifiers.meta` | 메타(프로필) | 남는다 |

- 플레이어 데이터라서 Undo·저장·멀티플레이가 그대로다. 처음 붙일 때 키가 저절로 정의된다.
- 행동 안에서 붙이고 뗀다. 같은 행동 안에서 붙인 뒤 바로 읽어도 보인다.
- 대상이 데이터에서 사라지면(쓰러져 전투에서 빠진 적) 그 대상의 **이번 판** Modifier는 그 행동이 끝나기 직전에 그 행동 안에서 지워진다(Undo하면 함께 돌아온다). 영구 Modifier는 지우지 않는다(대상이 다음 판에 다시 생긴다).

### 2-3. 이벤트와 가로채기

```
규칙 함수(Rules.Damage)
  1. 의도를 만든다            new DamageIntent { targetId = "hero", amount = 10 }
  2. Game.Intercept(의도)     가로채기 항목들이 차례로 의도를 고친다(−5, ×0.5, 막기 …)
  3. 고쳐진 의도로 일을 한다    target.hp -= hit.amount
  4. 이벤트를 쏜다             DamageDealt — 로그·도전과제·화면이 받는다
```

- 이벤트는 일이 **일어난 뒤** 알린다. 받는 쪽은 이미 일어난 결과를 바꾸지 않는다.
- 가로채기는 일이 **일어나기 직전에** 내용을 고치거나 막는다.
- **순서**: 가로채기 잎의 순서(`order`)가 작은 것부터, 같으면 등록 순서. 의도가 막히면(`cancelled = true`) 그 뒤 가로채기는 건너뛴다.
- 그 의도 형식을 가로채는 항목이 없으면 `Game.Intercept`는 바로 돌아온다.
- **사슬 한도**: 가로채기 결과가 다시 가로채기를 부르는 사슬은 이벤트 사슬과 같은 깊이를 세어 16단계에서 끊는다. 끊기면 의도를 고치지 않은 채 돌려주고 Journal에 경고를 남긴다(예외는 던지지 않는다).
- 모두 행동 안에서 돈다. Undo하면 고쳐진 결과까지 함께 돌아간다.

> **규칙: 장르 핵심 규칙 함수(피해·회복·죽음·사용·비용·뽑기)마다 가로채기 자리를 미리 둔다.** 그러면 나중에 생기는 기믹("받는 피해 −5", "회복량 2배", "죽음 막기")을 기획 단계에서 미리 알 필요가 없다. 규칙 함수는 그대로 두고 DB에 항목만 더한다.

### 2-4. 비용

`Game.Pay(key, path, amount, what)`는 다음 순서로 돈다.

1. 기본 제공 의도 `CostIntent { key, path, amount }`를 가로채기에 통과시킨다("비용 0" 같은 효과가 여기서 고친다).
2. 막혔거나(`cancelled`) 양이 0 이하가 되었으면 아무것도 내지 않는다.
3. 지금 값이 양보다 적으면 플레이어용 실패(`GameFailure`, 키 `FailureKeys.NotEnough`, `30-Balance-Tables.md` 2-4) → 행동 실패, 그 행동에서 바꾼 것이 모두 취소된다(먼저 낸 다른 비용도). 실패 글의 `{what}`은 `what`(텍스트 키면 번역된 글, 비우면 "플레이어 데이터 › 필드")이다.
4. 아니면 그만큼 뺀다.

없는 플레이어 데이터·없는 필드·숫자가 아닌 필드는 "모자랍니다"가 아니라 프로그래머 실수(`InvalidOperationException`)다.

`Game.CanPay`는 지금 값만 본다(가로채기 전). 버튼을 미리 흐리게 하는 화면용이다.

---

## 3. 에디터에서 쓰는 법

### 3-1. 효과 `능력치 보정`

결과 칸 `+ 결과 추가` → `대상에게 효과` → `+ 효과 추가` → 분류 **능력치** → `능력치 보정`. 최종 대상마다 Modifier 한 줄을 붙인다.

| 칸 | 툴팁 | 값 |
|---|---|---|
| `attribute` | 능력치(숫자 필드 이름) | 대상 클래스의 숫자 필드 이름 그대로(`attack`) |
| `kind` | 칸 | `플랫(+값)`(`Flat`) / `퍼센트(0.2 = +20%)`(`Percent`) / `독립 곱(0.5 = ×1.5)`(`Multiply`) |
| `value` | 값(퍼센트는 0.2 = +20%) | 숫자. 음수면 깎는다 |
| `permanent` | 영구(프로필에 남김) | 끄면 이번 판만 |

- **출처**는 이 효과를 가진 항목의 ID다(컨텍스트의 `Owner`: 능력 ID, 사용 기능이 붙은 인스턴스 ID, 유물 ID …).
- 요약: `attack +30`, `attack +20%`, `attack ×1.5`.
- 검증: "능력치 보정: 능력치(숫자 필드 이름)를 적으세요."
- 칸이 글자라서 필드 이름 오타는 검증이 잡지 못한다. 실행할 때 대상에 그 숫자 필드가 없으면 "[MomoiProject] Unit에 숫자 필드 'atack'가 없어 Modifier를 붙일 수 없습니다." 예외로 그 행동이 취소된다.

### 3-2. 효과 `능력치 보정 떼기`

같은 자리의 `능력치 보정 떼기`. 최종 대상에서 그 출처의 Modifier를 모두 뗀다.

| 칸 | 툴팁 | 값 |
|---|---|---|
| `source` | 출처(비우면 이 항목) | 비우면 이 효과를 가진 항목 ID. 다른 항목이 붙인 것을 떼려면 그 ID를 적는다 |

요약: `보정 떼기` / `보정 떼기(rage)`.

### 3-3. 트리거 잎 `가로채기`

Trigger 칸 `+ 블럭` → 그룹 **트리거** → `가로채기`. 칸의 잠금에 가로채기가 있을 때만 나온다(3-6). 문장은 `{intentType} 가로채기`.

| 칸 | 툴팁 | 값 |
|---|---|---|
| `intentType` | 의도 | `[Intent]`가 붙은 형식 목록(프로젝트 것 + 기본 제공 `비용 지불`). 하나도 없으면 "[Intent]가 붙은 의도 형식이 없습니다" |
| `filter` | 의도 조건 | 이벤트 조건과 같은 값 조건 식(`필드 검사` 등). 뿌리는 고른 의도다. 비우면 그 의도가 지날 때마다 |
| `order` | 순서(작을수록 먼저) | 정수. 같으면 등록 순서 |

- 요약: `피해 가로채기(…일 때)`. Alt를 누르면 `intercept 피해 where …`.
- **순간의 규칙**: 가로채기 잎은 그 의도가 `Game.Intercept`를 지나는 순간에만 참이다. `AND`로 조건 충족(`플레이어 데이터 검사` 등)을 붙이면 "그 상태일 때만 가로챈다"가 된다. 이벤트 받기와 `AND`로 묶으면 같은 순간에 오지 않아 발동하지 않는다.
- 검증 오류: "가로채기: 의도가 선택되지 않았습니다." / "가로채기: 의도 '…'을 찾을 수 없습니다(이름이 바뀌었는지 확인)." / "가로채기(피해) 조건: …".

### 3-4. 결과 `의도 고치기`

`+ 결과 추가` → 그룹 **결과** → `의도 고치기`. 칸의 잠금에 의도 고치기가 있을 때만 나온다.

| 칸 | 툴팁 | 값 |
|---|---|---|
| `path` | 필드 | 의도의 필드(같은 Trigger의 가로채기 잎이 고른 의도에서 고른다) |
| `op` | 방법 | `값으로 바꾸기`(`Set`) / `더하기`(`Add`, 기본) / `곱하기`(`Multiply`) / `막기`(`Cancel`) |
| `value` | 값 | 값으로 바꾸기는 그 필드 형식의 값, 더하기·곱하기는 숫자. 막기는 비운다 |

- 정수 필드는 더하기·곱하기 뒤 반올림한다.
- 막기는 의도의 `cancelled` 필드를 참으로 만든다. 그 뒤 가로채기는 건너뛰고, 규칙 함수가 일을 하지 않는다.
- 요약: `의도 amount -5`, `의도 amount ×0.5`, `의도 amount = 0`, `의도 막기`.
- 검증 오류: "의도 고치기: 필드를 고르세요." / "의도 고치기: 더하기·곱하기에는 숫자를 적으세요."
- 실행 경고(Journal, 출처 `Intercept`): "의도 고치기: 가로채기로 발동한 것이 아니라 고칠 의도가 없습니다." / "의도 고치기: (형식)에 cancelled 필드가 없어 막을 수 없습니다."

### 3-5. 결과 `비용 내기`

`+ 결과 추가` → 그룹 **결과** → `비용 내기`(플레이어 데이터를 줄 수 있는 칸이면 나온다). `Game.Pay`와 같다(가로채기 `비용 지불`을 거치고, 모자라면 그 행동 전체가 실패한다).

| 칸 | 툴팁 | 값 |
|---|---|---|
| `key` | 플레이어 데이터 | 낼 값이 든 플레이어 데이터 |
| `path` | 필드(비우면 값 전체) | 그 안의 숫자 필드(`energy`, `wallet.gold`) |
| `amount` | 양 | 0 이상 |
| `what` | 보일 이름(모자랄 때 실패 글에, 텍스트 키 또는 글, 비우면 플레이어 데이터 › 필드) | `energy.name`(텍스트 키) 또는 "에너지". 실패 글의 `{what}` 자리. 기본 문장이면 "에너지이(가) 모자랍니다(1 / 2).", 조사는 텍스트 표의 틀 `{what:이}`로 맞춘다(30장 5-8) |

- 결과 종류는 플레이어 데이터(`OutcomeKinds.PlayerData`)다. 플레이어 데이터에 주기를 고를 수 있는 칸(능력·사용 기능 …)에서 쓴다.
- 요약: `비용 battle › energy 2`.
- 검증 오류: "비용 내기: 플레이어 데이터를 고르세요." / "비용 내기: 양은 0 이상이어야 합니다(얻는 것은 '플레이어 데이터에 주기')."
- `Game.CanUse`는 결과를 보지 않으므로 비용이 모자란 것을 모른다. 버튼을 미리 흐리게 하려면 트리거에 `플레이어 데이터 검사`(energy 2 이상)를 `AND`로 붙이거나, 그 행동을 `Game.CanDo`로 해 본다(결과까지 돌려 보고 이유를 준다, `33-Tick-Preview-Csv.md` 2-2).

### 3-6. 어느 칸에서 쓸 수 있나

| Block | 쓸 수 있는 칸 |
|---|---|
| 능력치 보정 · 능력치 보정 떼기 | `대상에게 효과`를 고를 수 있는 칸 전부(능력·사용 기능·도전과제·개발 명령·프로젝트 기능) |
| 가로채기 | `[TriggerSlot]`에 `TriggerKinds.Intercept`가 있는 칸. 로그·도전과제·능력·사용 기능의 칸에는 없다 → DB › 상태 효과의 '걸려 있는 동안 트리거'(29장), 프로젝트 기능(유물 등, 17장) |
| 의도 고치기 | `[OutcomeSlot]`에 `OutcomeKinds.Intent`가 있는 칸. 가로채기 잎과 같은 항목에서 쓴다 |
| 비용 내기 | `[OutcomeSlot]`에 `OutcomeKinds.PlayerData`가 있는 칸 |

프로젝트 기능에 가로채기를 열려면 두 칸의 잠금에 종류를 더한다.

```csharp
[FieldLabel("발동"), SerializeReference, TriggerSlot(TriggerKinds.Event | TriggerKinds.Condition | TriggerKinds.Intercept)]
public Trigger trigger;
[FieldLabel("효과"), SerializeReference, OutcomeSlot(OutcomeKinds.PlayerData | OutcomeKinds.Coded | OutcomeKinds.Targeted | OutcomeKinds.Intent)]
public List<Outcome> outcomes = new List<Outcome>();
```

### 3-7. 모니터에서 확인하기

- **Modifier**: 모니터 **데이터** 탭에서 플레이어 데이터 `$modifiers`(영구는 `$modifiers.meta`)를 펼치면 붙은 Modifier가 한 줄씩 보인다(대상·필드·칸·값·출처).
- **가로채기**: 결과가 돌 때마다 Journal에 "결과: 의도 amount -5"(출처는 기능의 출처 글자, 예 `Relics`)가 남는다. 행동 탭에서 그 행동을 Undo하면 고쳐진 피해까지 돌아간다.
- **사슬 경고**: "가로채기 사슬이 16단계를 넘어 끊었습니다(피해). 가로채는 결과가 같은 의도를 다시 만드는지 확인하세요."

---

## 4. 코드로 쓰는 법 — 문법 요소를 하나씩

### 4-1. 능력치 = 숫자 필드

```csharp
[Serializable]
public class Unit
{
    public string id;             // 능력치를 가진 객체는 id가 있어야 한다
    public double attack = 100;   // 능력치 attack의 기본값
    public double armor = 50;
    public int hp = 300;
}
```

- 숫자 형식(`int`·`float`·`double` …)의 필드가 그대로 능력치다. 선언·등록은 없다.
- Modifier는 대상의 **id**로 붙는다. `id`가 없는 객체에 쓰면 "…에 string id 필드가 없어 개체로 찾거나 가리킬 수 없습니다" 예외가 난다.

### 4-2. `Game.Attribute` — 최종값 읽기

```csharp
var attack = Game.Attribute(hero, nameof(Unit.attack));         // 객체로
var armor  = Game.Attribute("goblin1", nameof(Unit.armor));      // id 글자로도
```

- 돌려주는 값은 `double`(2-1의 공식). 정수가 필요하면 규칙 함수에서 반올림한다.
- 필드 이름은 `nameof`로 쓴다(이름을 바꾸면 컴파일러가 따라온다).
- 그 필드가 없거나 id로 대상을 찾지 못하면 예외 없이 기본값을 0으로 보고 계산한다.
- 값을 바꾸지 않으므로 화면 코드(행동 밖)에서 읽어도 된다.

### 4-3. `Game.AddModifier` — 붙이기

```csharp
string id = Game.AddModifier(hero, nameof(Unit.attack), ModifierKind.Flat, 30, "ironSword");
Game.AddModifier(hero, nameof(Unit.attack), ModifierKind.Percent, 0.2, "rage");
Game.AddModifier("hero", nameof(Unit.attack), ModifierKind.Flat, 5, "smith", permanent: true);
```

| 인자 | 뜻 |
|---|---|
| `entity` | 대상 객체 또는 id 글자 |
| `attribute` | 숫자 필드 이름 |
| `kind` | `ModifierKind.Flat` / `Percent` / `Multiply` |
| `value` | 값(퍼센트·독립 곱은 비율: 0.2 = 20%) |
| `source` | 출처 ID(뗄 때 쓴다). `null`이면 빈 글자 |
| `permanent` | 참이면 메타 구역(프로필)에 저장 |

- 돌려주는 값은 Modifier id(`$mod1`, `$mod2` …). 라이브러리가 만드는 id는 `$`로 시작해 DB 항목 ID와 겹치지 않는다(29장 2-7).
- 행동 안에서 부른다(Undo·저장·멀티플레이 기록).
- 대상 객체에 그 숫자 필드가 없으면 `InvalidOperationException`("[MomoiProject] Unit에 숫자 필드 'speed'가 없어 Modifier를 붙일 수 없습니다."), 이름이 비면 `ArgumentException`.
- id 글자로 줬는데 지금 데이터에서 대상을 찾지 못하면 필드 검사 없이 그 id에 붙인다. 판 밖에서 영구 강화를 붙일 때 쓴다(5-2). 이번 판 Modifier는 대상이 없으면 그 행동이 끝날 때 지워지므로(2-2) 이렇게 붙이는 것은 영구일 때만 뜻이 있다.

### 4-4. `Game.RemoveModifiers` · `Game.Modifiers` — 떼기·보기

```csharp
int removed = Game.RemoveModifiers("ironSword", hero);   // hero에서 ironSword가 붙인 것만
Game.RemoveModifiers("aura");                              // 모든 대상에서 aura가 붙인 것
IReadOnlyList<Modifier> all    = Game.Modifiers(hero);                          // hero의 전부
IReadOnlyList<Modifier> attack = Game.Modifiers(hero, nameof(Unit.attack));     // attack 것만
```

| 멤버 | 뜻 |
|---|---|
| `int RemoveModifiers(string source, object entity = null)` | 그 출처의 Modifier를 뗀다(이번 판·영구 둘 다). `entity`를 주면 그 대상 것만. 돌려주는 값은 뗀 개수 |
| `IReadOnlyList<Modifier> Modifiers(object entity, string attribute = null)` | 대상의 Modifier들. 필드를 주면 그 필드 것만. 툴팁("검 +20, 반지 +20%")에 쓴다 |

Modifier 하나만 떼려면 `session.Attributes.Remove(modifierId)`(4-6).

### 4-5. `ModifierKind` · `Modifier`

| `ModifierKind` | 계산 | 값 예 |
|---|---|---|
| `Flat` | 기본값에 더한다 | `30` = +30 |
| `Percent` | 같은 칸끼리 더한 뒤 한 번 곱한다 | `0.2` = +20% |
| `Multiply` | 하나하나 따로 곱한다(드물게) | `0.5` = ×1.5 |

| `Modifier` 필드 | 뜻 |
|---|---|
| `id` | Modifier id |
| `target` | 대상 객체의 id |
| `attribute` | 숫자 필드 이름 |
| `kind` · `value` | 칸 · 값 |
| `source` | 출처 ID |

`ModifierList`(`items`)는 저장 껍데기다. 직접 다룰 일은 없다.

### 4-6. `session.Attributes` — `AttributeSystem`

`Game`의 능력치 멤버는 이것을 부른다.

| 멤버 | 뜻 |
|---|---|
| `Get(entity, attribute)` | = `Game.Attribute` |
| `Add(entity, attribute, kind, value, source, permanent)` | = `Game.AddModifier` |
| `RemoveBySource(source, entity)` | = `Game.RemoveModifiers` |
| `Remove(modifierId)` | Modifier 하나를 id로 뗀다. 뗐으면 `true` |
| `Of(entity, attribute)` | = `Game.Modifiers` |
| `static Apply(baseValue, modifiers)` | 기본값에 Modifier 목록을 적용한 값(2-1의 공식). 저장소를 건드리지 않는다 |
| `SessionKey` · `MetaKey` | `"$modifiers"` · `"$modifiers.meta"` |

`Apply`는 미리보기에 쓴다: "이 장비로 갈아 끼우면 공격력이 얼마가 되나".

```csharp
var others = Game.Modifiers(hero, nameof(Unit.attack)).Where(m => m.source != oldGear.id);
var preview = AttributeSystem.Apply(hero.attack, others.Append(new Modifier { kind = ModifierKind.Flat, value = newGear.attack }));
```

### 4-7. 의도 클래스 — `[Intent]`

```csharp
using System;
using GBible;

[Serializable, Intent("피해")]
public class DamageIntent
{
    public string sourceId;
    public string targetId;
    public int amount;
    public bool cancelled;      // 있으면 막을 수 있다(의도 고치기 '취소')
}
```

1. **`[Intent("표시 이름")]`** — DB의 의도 목록(가로채기 잎의 `의도` 칸)에 이 이름으로 나오고, 컴파일 때 필드 읽기 코드가 생긴다(이벤트의 `[GameEvent]`와 같다).
2. **`class`** — `Game.Intercept`는 같은 객체를 고쳐서 돌려주므로 클래스여야 한다.
3. **필드** — 가로채는 쪽이 고르고 고칠 값. 객체 대신 id를 둔다.
4. **`public bool cancelled;`** — 막을 수 있게 하려면 둔다. 이름이 정확히 `cancelled`여야 한다.
5. 의도 형식은 프로젝트가 정한다. 라이브러리가 주는 것은 `CostIntent`(비용 지불) 하나다.

### 4-8. `Game.Intercept` — 규칙 함수의 가로채기 자리

```csharp
public static void Damage(GameApi game, Unit source, Unit target, int amount)
{
    if (target == null || target.hp <= 0 || amount <= 0) return;
    var hit = game.Intercept(new DamageIntent { sourceId = source?.id, targetId = target.id, amount = amount });
    if (hit.cancelled || hit.amount <= 0) return;      // 막혔다
    target.hp -= hit.amount;                            // 고쳐진 값으로
    game.Events.Publish(new DamageDealt { sourceId = source?.id, targetId = target.id, amount = hit.amount });
}
```

- `T Intercept<T>(T intent) where T : class` — 의도를 그 형식을 가로채는 항목들에 차례로 통과시키고 **같은 객체**를 돌려준다(고쳐진 채로).
- 형식은 정확히 같아야 걸린다(상속한 형식은 따로다).
- 돌려받은 값으로 일을 한다. 원래 인자(`amount`)를 다시 쓰면 가로채기가 무시된다.
- 일을 하기 **직전**에 부르고, 일이 끝난 **뒤**에 이벤트를 쏜다.
- `session.Triggers.Intercept(intent)`와 같다.

### 4-9. 코드로 만들기 — `InterceptTrigger` · `ModifyIntentOutcome`

```csharp
var when = new InterceptTrigger
{
    intentType = typeof(DamageIntent).FullName,                                   // 형식의 전체 이름
    filter = new FieldFilter { path = "targetId", op = FieldOp.Equal, value = "hero" },
    order = 1,
};
var minus5 = new ModifyIntentOutcome { path = "amount", op = IntentOp.Add, value = "-5" };
var block  = new ModifyIntentOutcome { op = IntentOp.Cancel };
```

| `InterceptTrigger` | 뜻 |
|---|---|
| `intentType` | 의도 형식의 전체 이름 |
| `filter` (`ValueFilter`) | 의도 조건(`FieldFilter`·`AllFilter` …). `null`이면 언제나 |
| `order` | 순서(작을수록 먼저) |
| `Kind` | `TriggerKinds.Intercept` |

| `ModifyIntentOutcome` | 뜻 |
|---|---|
| `path` | 의도의 필드 경로(점으로 내려간다) |
| `op` (`IntentOp`) | `Set` · `Add` · `Multiply` · `Cancel` |
| `value` | 글자로 적은 값(숫자도 `"-5"`) |
| `Kind` | `OutcomeKinds.Intent` |

### 4-10. `[BindTo]`로 가로채기

조건 분기나 계산이 있으면 결과를 함수 하나로 쓴다(→ `13-BindTo.md`). **첫 매개변수가 의도**다.

```csharp
[BindTo("undying")]
static void Undying(DeathIntent i, TriggerContext c)
{
    i.cancelled = true;          // 의도를 고친다 = 의도 고치기와 같다
}
```

- 받을 의도 = 첫 매개변수의 형식. 다른 의도로 발동하면 실행하지 않고 경고를 남긴다(이벤트와 같은 규칙).
- `c.IsIntercept`가 `true`, `c.Event`가 그 의도, `c.Owner`가 연결된 항목 ID다.
- 연결은 그 항목의 Trigger(가로채기 잎)를 바꾸지 않는다. 프로젝트 기능은 등록 코드가 `BoundOutcomes`를 확인해야 한다(17장 2-8).

### 4-11. 직접 등록 · 기능 칸

DB 항목 없이 규칙 하나를 코드로 등록할 수도 있다(시작 코드 `Started`에서 한 번).

```csharp
session.Triggers.Register("rule.halfDamage",
    new InterceptTrigger { intentType = typeof(DamageIntent).FullName, order = 10 },
    repeat: true,
    fire: moment => session.Triggers.RunOutcomes(new Outcome[] { new ModifyIntentOutcome { path = "amount", op = IntentOp.Multiply, value = "0.5" } }, moment, "Rules"));
```

- 가로채기로 발동한 순간의 컨텍스트: `IsIntercept = true`, `Event` = 의도 객체, `Owner` = 등록한 owner.
- 결과는 `RunOutcomes`로 실행한다(컨텍스트가 결과에 보이고 Journal에 남는다).
- 등록은 플레이어 데이터가 아니다. "켜졌는지"는 행동 안에서 등록을 바꾸지 말고, 늘 등록해 둔 채 플레이어 데이터 조건(`AND 조건 충족`)으로 정한다(5-6, 5-7).

기능 칸에 여는 법은 3-6. `TriggerKinds.Intercept`(트리거 잎 종류), `OutcomeKinds.Intent`(결과 종류)를 더한다.

### 4-12. `Game.Pay` · `Game.CanPay` · `CostIntent` · `PayOutcome`

```csharp
Game.Pay("battle", "energy", 2, what: "energy.name");     // 행동 안에서. 모자라면 "에너지이(가) 모자랍니다(1 / 2)."(energy.name = "에너지")
bool canPay = Game.CanPay("battle", "energy", 2);         // 화면 미리보기(가로채기 전)
var pay = new PayOutcome { key = "battle", path = "energy", amount = 2, what = "energy.name" };   // 결과 '비용 내기'
```

| 멤버 | 뜻 |
|---|---|
| `void Pay(string key, string path, double amount, string what = null)` | 2-4의 순서로 낸다. 모자라면 `GameFailure`(`FailureKeys.NotEnough`). 메시지의 앞부분 `{what}`은 `what`(텍스트 키면 번역된 글, 글자면 그대로). 비우면 플레이어 데이터의 이름 › 필드("전투 › energy이(가) 모자랍니다(1 / 2).") |
| `bool CanPay(string key, string path, double amount)` | 지금 값 ≥ 양. 가로채기를 거치지 않는다 |
| `CostIntent` | 기본 제공 의도 "비용 지불": `key`·`path`·`amount`·`cancelled` |
| `PayOutcome` | 결과 '비용 내기'(`key`·`path`·`amount`·`what`). `Run`이 `Game.Pay`를 부른다 |

- `path`를 비우면 값 전체(숫자 플레이어 데이터)를 낸다.
- 프로그래머 실수는 `InvalidOperationException`: 없는 플레이어 데이터("[MomoiProject] 비용: 없는 플레이어 데이터 '…'입니다."), 없는 필드("[MomoiProject] 비용: 'battle'에 필드 'enrgy'가 없습니다(…)."), 숫자가 아닌 필드("[MomoiProject] 비용: 'battle › name'는 숫자 필드가 아닙니다(String).").
- 실패가 행동 단위로 취소되므로 행동 안에서 부른다.
- 플레이어에게 보일 실패의 번역·`{what}` 이름은 `30-Balance-Tables.md` 2-4·5-8.

### 4-13. 도우미

| 멤버 | 뜻 |
|---|---|
| `Intents.DisplayNameOf(type)` | `[Intent]`의 표시 이름(없으면 형식 이름) |
| `Intents.IsCancelled(intent)` | 의도의 `cancelled`가 참인지 |
| `[IntentTypeRef]` | 글자 필드에 붙이면 에디터가 의도 형식 목록을 띄운다(프로젝트 기능이 의도 형식을 고르는 칸을 둘 때) |
| `TriggerSystem.MaxDepth` | 사슬 한도(16) |

---

## 5. 상황별 예제

예제는 다음 형식을 쓴다(DB › 플레이어 데이터에 `battle`(Battle, 세션 구역, 이름 "전투"), `party`(Party, 세션 구역), `profile`(Profile, 메타 구역)이 있다고 본다. 주인공의 id는 판마다 `"hero"`).

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using GBible;
using GBible.Data;
using UnityEngine;

[Serializable]
public class Unit
{
    public string id;
    public string team;
    public double attack = 100, armor = 50;
    public double damageAmp;                                   // 피해 증폭 합(기본 0, 플랫으로 쌓는다)
    public int hp = 300, maxHp = 300;
    public List<string> gear = new List<string>();             // 착용한 장비 id
}
[Serializable] public class Gear { public string id; public string name; public double attack, attackPercent; }
[Serializable]
public class Battle
{
    public List<Unit> units = new List<Unit>();
    public int turn, energy = 3;
    public bool freeCost;                                      // 이번 턴 비용 0
    public int echo;                                           // 다음 스킬 2회 충전
    public bool undyingUsed;                                   // 불사를 이번 전투에 썼는가
}
[Serializable] public class Party { public List<Gear> bag = new List<Gear>(); public List<string> relics = new List<string>(); }
[Serializable] public class Profile { public int gold; public int smithLevel; }
[Serializable] public class Card { public string title; public int cost = 1; [SerializeReference] public Usable usable; }   // 카드 원본(DB › 인스턴스)

[GameEvent("피해")] public class DamageDealt { public string sourceId, targetId; public int amount; }
[GameEvent("유닛 처치")] public class UnitKilled { public string killerId, victimId; }

// 유물: 프로젝트 기능(17장, 가이드 04장 8절). 가로채기를 쓰도록 두 칸의 잠금을 열었다.
[Database("유물", Group = "콘텐츠", Order = 40, IdPrefix = "relic")]
public class RelicData : GameData
{
    [FieldLabel("발동"), SerializeReference, TriggerSlot(TriggerKinds.Event | TriggerKinds.Condition | TriggerKinds.Intercept)]
    public Trigger trigger;
    [FieldLabel("효과"), SerializeReference, OutcomeSlot(OutcomeKinds.PlayerData | OutcomeKinds.Coded | OutcomeKinds.Targeted | OutcomeKinds.Intent)]
    public List<Outcome> outcomes = new List<Outcome>();
}
// 등록 함수 RelicRules.Register는 17장·가이드 04장 8절과 같다(발동 순간 party.relics에 있을 때만 결과를 실행).
```

### 5-1. 장비 착용·해제 — Modifier를 출처로 붙이고 뗀다

장비마다 다른 id를 출처로 쓴다. 같은 반지 두 개를 끼어도 하나만 뗄 수 있다.

```csharp
[Serializable, BlockInfo("장비 착용")]
public class Equip : GameAction
{
    public Unit unit;
    public Gear gear;                    // 가방(party.bag) 안의 장비

    protected override void Run()
    {
        if (unit.gear.Contains(gear.id)) throw new InvalidOperationException("이미 착용한 장비입니다.");
        unit.gear.Add(gear.id);
        if (gear.attack != 0) Game.AddModifier(unit, nameof(Unit.attack), ModifierKind.Flat, gear.attack, gear.id);
        if (gear.attackPercent != 0) Game.AddModifier(unit, nameof(Unit.attack), ModifierKind.Percent, gear.attackPercent, gear.id);
    }
}

[Serializable, BlockInfo("장비 해제")]
public class Unequip : GameAction
{
    public Unit unit;
    public Gear gear;

    protected override void Run()
    {
        if (!unit.gear.Remove(gear.id)) throw new InvalidOperationException("착용하지 않은 장비입니다.");
        Game.RemoveModifiers(gear.id, unit);          // 이 장비가 붙인 보정을 모두 뗀다
    }
}

// 화면: 최종값과 내역
attackText.text = $"{Game.Attribute(hero, nameof(Unit.attack)):0}";
attackTooltip.text = string.Join("\n", Game.Modifiers(hero, nameof(Unit.attack)).Select(m => $"{m.source}: {m.kind} {m.value}"));
```

- 착용·해제가 행동이라 Undo 한 번에 장비 목록과 Modifier가 함께 돌아간다.
- 장비 데이터(`gear.attack`)를 고쳐도 이미 붙은 Modifier는 그대로다. 다시 끼면 새 값으로 붙는다.
- "쓰면 붙고 나중에 떼는" 버프는 코드 없이 능력의 `능력치 보정`·`능력치 보정 떼기`로 조립한다(→ `16-Abilities-Usable.md` 3-10). 그때 출처는 항목 ID라서, 같은 항목을 한 유닛에 두 번 붙이면 떼기도 함께 된다. 몇 턴 동안만 붙었다가 저절로 떨어지는 버프는 상태 효과의 능력치 보정으로 만든다(→ `29-Statuses-Schedules-Choices.md` 5-2).

### 5-2. 영구 강화 — 대장간

골드를 내고 주인공 공격력 +5. 판이 끝나도 남는다.

```csharp
[Serializable, BlockInfo("대장간 강화")]
public class SmithUpgrade : GameAction
{
    protected override void Run()
    {
        var profile = Game.Get<Profile>("profile");
        Game.Pay("profile", "gold", 100 + profile.smithLevel * 50, what: "gold.name");     // 모자라면 여기서 실패 — 아무것도 바뀌지 않는다
        profile.smithLevel++;
        Game.AddModifier("hero", nameof(Unit.attack), ModifierKind.Flat, 5, "smith", permanent: true);
    }
}

// 강화 초기화(환불 화면 등)
// Game.RemoveModifiers("smith");    // 영구 구역까지 smith가 붙인 것을 모두 뗀다
```

- 대상은 id 글자 `"hero"`다. 영구 Modifier는 id로 붙으므로 **판마다 같은 id**인 대상에만 쓴다. 판 밖(주인공이 데이터에 없을 때)에서도 붙는다(4-3).
- 강화할 때마다 한 줄씩 늘어 플랫끼리 더해진다(3번이면 +15).
- 에디터: `능력치 보정`의 `permanent`를 켠다.

### 5-3. 피해 계산 — 중간층 규칙 함수

계산 순서(②~④)는 장르마다 달라서 라이브러리가 아니라 프로젝트의 규칙 함수에 둔다. 효과 Block은 수치만 들고 규칙 함수를 부른다.

| 단계 | 계산 | 어디서 |
|---|---|---|
| ① 스탯 | `Game.Attribute`(Modifier까지 반영한 최종 공격력) | 라이브러리 |
| ② 스킬 피해 | 기본 + 계수 × 스탯 | 규칙 함수 |
| ③ 증폭 | × (1 + 피해 증폭 합). 증폭끼리는 더한다 | 규칙 함수 |
| ④ 추가 피해 | + 플랫. 증폭을 받지 않는다 | 규칙 함수 |
| 방어 | × 100 / (100 + 방어). 추가 피해도 받는다 | 규칙 함수(공식은 프로젝트가 정한다) |
| 가로채기 · 적용 | `Game.Intercept` → 체력 → 이벤트 → 죽음 | 규칙 함수 |

```csharp
[Serializable, Intent("죽음")]
public class DeathIntent
{
    public string unitId;
    public string killerId;
    public int surviveHp = 1;         // 막혔을 때 남길 체력
    public bool cancelled;
}

public static class Rules
{
    // 스킬 피해: ①~④ → 방어 → 적용
    public static void SkillDamage(GameApi game, Unit source, Unit target, double baseDamage, double ratio, double bonus)
    {
        var stat = game.Attribute(source, nameof(Unit.attack));                          // ①
        var skill = baseDamage + ratio * stat;                                            // ②
        var amplified = skill * (1 + game.Attribute(source, nameof(Unit.damageAmp)));     // ③
        var raw = amplified + bonus;                                                      // ④
        var armor = game.Attribute(target, nameof(Unit.armor));
        Damage(game, source, target, (int)Math.Round(raw * 100 / (100 + armor)));         // 방어
    }

    // 고정 피해: 증폭·방어를 받지 않는 별도 종류
    public static void FixedDamage(GameApi game, Unit source, Unit target, int amount) => Damage(game, source, target, amount);

    // 적용: 가로채기 자리 → 체력 → 이벤트 → 죽음
    public static void Damage(GameApi game, Unit source, Unit target, int amount)
    {
        if (target == null || target.hp <= 0 || amount <= 0) return;
        var hit = game.Intercept(new DamageIntent { sourceId = source?.id, targetId = target.id, amount = amount });
        if (hit.cancelled || hit.amount <= 0) return;
        target.hp -= hit.amount;
        game.Events.Publish(new DamageDealt { sourceId = source?.id, targetId = target.id, amount = hit.amount });
        if (target.hp <= 0) Kill(game, source, target);
    }

    // 죽음: 가로채기 자리(불사·부활) → 처치
    static void Kill(GameApi game, Unit killer, Unit unit)
    {
        var death = game.Intercept(new DeathIntent { unitId = unit.id, killerId = killer?.id });
        if (death.cancelled)
        {
            unit.hp = Math.Max(1, death.surviveHp);    // 죽음이 막혔다
            return;
        }
        unit.hp = 0;
        game.Events.Publish(new UnitKilled { killerId = killer?.id, victimId = unit.id });
    }
}

// 효과 Block: 스킬마다 다른 수치만 DB에서
[Serializable, BlockInfo("스킬 피해", Category = "전투", Sentence = "{baseDamage} + 공격력 × {ratio}")]
public class SkillDamageEffect : Effect<Unit>
{
    public double baseDamage = 30;
    public double ratio = 0.8;         // 계수(0.8 = 80%)
    public double bonus;               // 추가 피해(증폭을 받지 않는다)
    protected override void Apply(Unit target) => Rules.SkillDamage(Game, User<Unit>(), target, baseDamage, ratio, bonus);
}
```

숫자로 따라가기 — 공격력 기본 100(검 +20, 반지 +20%), 증폭 버프 +10%·+15%, 스킬 30 + 80%, 추가 피해 20, 대상 방어 50, 대상이 가시 갑옷(5-4)을 가짐:

| 단계 | 계산 | 값 |
|---|---|---|
| ① | (100 + 20) × (1 + 0.2) | 144 |
| ② | 30 + 0.8 × 144 | 145.2 |
| ③ | 145.2 × (1 + 0.1 + 0.15) | 181.5 |
| ④ | 181.5 + 20 | 201.5 |
| 방어 | 201.5 × 100 / 150 | 134.3 → 134 |
| 가로채기 | 가시 갑옷 −5 | 129 |

- 피해 증폭은 기본 0인 능력치(`damageAmp`)에 **플랫**(0.1 = 10%)으로 쌓는다. 그러면 `Game.Attribute`가 곧 증폭 합이다.
- 방어 100 / (100 + 방어)는 기본 제안이다. 방어 50이면 받는 피해 67%, 100이면 50%로, 방어를 아무리 올려도 0이 되지 않는다.
- 카드·적 공격·개발 명령이 모두 `Rules.Damage`를 거치므로 가로채기도 모두 같이 탄다.

### 5-4. 가시 갑옷 — 받는 피해 −5(코드 없음)

**에디터**(DB › 유물 `thornArmor`):

1. `trigger`: `+ 블럭` → `가로채기` → 의도 `피해`. `filter`(의도 조건) `+ 블럭` → `필드 검사` → `targetId` `일치` `hero`.
2. `outcomes`: `+ 결과 추가` → `의도 고치기` → 필드 `amount`, 방법 `더하기`, 값 `-5`.

요약: `피해 가로채기(…일 때) → 의도 amount -5`. 주인공이 이 유물을 가진 동안 `Rules.Damage`를 지나는 피해가 5 줄어든다. 5 이하의 피해는 0이 되어 들어오지 않는다(규칙 함수의 `hit.amount <= 0`).

**순서**: 같은 피해에 "받는 피해 반감"(의도 고치기 `amount` `곱하기` `0.5`)도 걸려 있으면, 순서가 작은 쪽이 먼저다.

| 순서 | 피해 20일 때 |
|---|---|
| 가시 갑옷 1, 반감 2 | 20 − 5 = 15 → × 0.5 = 7.5 → 8 |
| 반감 1, 가시 갑옷 2 | 20 × 0.5 = 10 → − 5 = 5 |

**되받아치기는 가로채기가 아니다.** "공격한 쪽에 3 피해"는 피해가 들어온 **뒤**의 일이라 이벤트 받기(`피해`, `targetId` 일치 `hero`)로 만들고, 결과에서 피해 규칙을 부른다(가로채는 쪽은 의도만 고친다).

### 5-5. 불사 — 죽음 막기(`[BindTo]`)

"전투마다 한 번, 죽는 대신 체력 1로 버틴다". 한 번만이라는 분기가 있어 결과를 코드로 쓴다.

**에디터**(DB › 유물 `undying`): `trigger` = `가로채기` → 의도 `죽음`, 의도 조건 `unitId` `일치` `hero`. 결과 칸은 아래 연결 때문에 잠긴다.

```csharp
public static class RelicCode
{
    [BindTo("undying")]
    static void Undying(DeathIntent i, TriggerContext c)
    {
        var battle = c.Session.Data.Get<Battle>("battle");
        if (battle.undyingUsed) return;      // 이번 전투에 이미 썼다
        battle.undyingUsed = true;
        i.cancelled = true;                  // 의도를 막는다 → 규칙 함수가 surviveHp(1)로 남긴다
    }
}
```

- 체력은 가로채는 쪽이 아니라 규칙 함수(5-3의 `Kill`)가 바꾼다. 가로채는 쪽은 의도만 고친다. "체력 절반으로 부활"이면 `i.surviveHp = …`로 의도에 실어 보낸다.
- 전투를 시작하는 행동이 `undyingUsed = false`로 되돌린다.
- 코드 없이 쓰려면: 트리거 `가로채기(죽음)` AND `플레이어 데이터 검사(battle: undyingUsed 거짓)`, 결과 `의도 고치기` `막기` + `플레이어 데이터에 주기` `battle › undyingUsed` 참.

### 5-6. 이번 턴 비용 0

카드 "집중": 이번 턴에 내는 에너지 비용이 모두 0.

**카드를 내는 행동** — 비용을 `Game.Pay`로 낸다.

```csharp
[Serializable, BlockInfo("카드 내기")]
public class PlayCard : GameAction
{
    public Unit user;
    public string card;        // 카드 원본(인스턴스) ID
    public Unit target;

    protected override void Run()
    {
        var cost = Game.Session.Instances?.Invoke(card) is Card data ? data.cost : 0;
        Game.Pay("battle", "energy", cost, what: "energy.name");   // 가로채기(비용 지불) → 모자라면 실패
        if (target == null) Game.Use(card, user);
        else Game.Use(card, user, target);
    }
}
```

**집중 카드**(사용 기능): 결과 `플레이어 데이터에 주기 · battle` › `freeCost` = 참. 턴 끝 행동이 `battle.freeCost = false;`로 돌린다.

**가로채는 쪽** — 늘 등록해 두고, 켜졌는지는 `battle.freeCost`로 정한다(`Started`에서 한 번).

```csharp
session.Triggers.Register("rule.freeCost",
    new AllTrigger
    {
        triggers =
        {
            new InterceptTrigger
            {
                intentType = typeof(CostIntent).FullName,
                filter = new AllFilter { filters =
                {
                    new FieldFilter { path = "key", op = FieldOp.Equal, value = "battle" },      // 에너지 비용만
                    new FieldFilter { path = "path", op = FieldOp.Equal, value = "energy" },
                } },
            },
            new ConditionTrigger { condition = new PlayerDataCondition { key = "battle",
                filter = new FieldFilter { path = "freeCost", op = FieldOp.IsTrue } } },
        },
    },
    repeat: true,
    fire: moment => session.Triggers.RunOutcomes(new Outcome[] { new ModifyIntentOutcome { op = IntentOp.Cancel } }, moment, "Rules"));
```

- 가로채기를 고를 수 있는 프로젝트 기능의 항목으로 둔다면 같은 것을 조립한다: 발동 `가로채기(비용 지불; key 일치 battle, path 일치 energy)` AND `플레이어 데이터 검사(battle: freeCost 참)`, 효과 `의도 고치기` `막기`.
- 이런 "잠깐 켜지는 가로채기"는 **상태 효과**로 만들면 더 짧다. DB › 상태 효과 `focus`: 트리거 `가로채기(비용 지불; key 일치 battle, path 일치 energy)`, 결과 `의도 고치기` `막기`, 지속 `이벤트 횟수` 1(셀 이벤트 턴 끝). 카드는 사용자에게 `상태 효과 걸기`(`focus`)만 한다. 켜졌는지 담는 `freeCost`, 턴 끝에 되돌리는 코드, 위의 등록 코드가 모두 필요 없고, 턴이 끝나면 저절로 풀린다(→ `29-Statuses-Schedules-Choices.md` 5-5).
- 상점의 골드 비용(`profile › gold`)은 의도 조건에 걸리지 않아 그대로 낸다.
- 버튼 미리보기: `Game.CanPay`는 가로채기 전 값이다. `Game.CanPay("battle", "energy", cost) || battle.freeCost`처럼 화면이 함께 본다.

### 5-7. 다음 스킬 2회 발동 — `CastIntent { times }`

스킬을 쓰는 규칙 함수에 가로채기 자리를 두면, "2회 발동"·"침묵(시전 막기)"·"다른 스킬로 바꾸기"가 모두 의도 고치기로 된다.

```csharp
[Serializable, Intent("스킬 시전")]
public class CastIntent
{
    public string casterId;
    public string ability;       // 능력 ID(설정으로 다른 스킬로 바꿀 수 있다)
    public int times = 1;        // 몇 번 쓸지
    public bool cancelled;       // 침묵
}

public static class Rules          // 5-3의 Rules에 더한다
{
    public static void Cast(GameApi game, Unit caster, string ability, params object[] targets)
    {
        var cast = game.Intercept(new CastIntent { casterId = caster.id, ability = ability });
        if (cast.cancelled) return;
        for (var i = 0; i < cast.times; i++) game.Use(cast.ability, caster, targets);
    }
}

[Serializable, BlockInfo("스킬 쓰기")]
public class CastSkill : GameAction
{
    public Unit caster;
    public string ability;
    public Unit target;
    protected override void Run() => Rules.Cast(Game, caster, ability, target);
}
```

**메아리 카드**: 결과 `플레이어 데이터에 주기 · battle` › `echo` +1.

**가로채는 쪽**(`Started`에서 한 번):

```csharp
session.Triggers.Register("rule.echo",
    new AllTrigger
    {
        triggers =
        {
            new InterceptTrigger { intentType = typeof(CastIntent).FullName, order = 100 },   // 맨 뒤에
            new ConditionTrigger { condition = new PlayerDataCondition { key = "battle",
                filter = new FieldFilter { path = "echo", op = FieldOp.AtLeast, value = "1" } } },
        },
    },
    repeat: true,
    fire: moment => session.Triggers.RunOutcomes(new Outcome[]
    {
        new ModifyIntentOutcome { path = "times", op = IntentOp.Multiply, value = "2" },
        new PlayerDataReward { key = "battle", path = "echo", amount = -1 },                  // 충전 하나 쓰기
    }, moment, "Rules"));
```

- 충전을 쓰는 것은 의도 밖의 값을 바꾸는 일이다. 순서를 크게(100) 두어 다른 가로채기가 먼저 돌게 한다. 앞에서 침묵이 시전을 막으면(`cancelled`) 메아리는 건너뛰어 충전이 남는다.
- 두 번의 `Game.Use`가 `CastSkill` 행동 하나 안에서 돈다. Undo 한 번에 두 번 모두, 충전까지 돌아간다.

---

## 6. 주의할 점 · 자주 하는 실수

| 증상 | 원인과 해결 |
|---|---|
| 장비를 꼈는데 공격력 표시가 그대로 | 기본값 필드(`hero.attack`)를 읽었다. 최종값은 `Game.Attribute(hero, nameof(Unit.attack))`. |
| `Game.Attribute`가 0 | 필드 이름 오타이거나 id로 대상을 찾지 못했다. 이때 읽기는 예외 없이 기본값을 0으로 본다. 필드 이름은 `nameof`로 쓴다. |
| 능력치 보정이 든 능력을 썼더니 행동이 실패("…숫자 필드 '…'가 없어…") | 에디터 칸 `attribute`의 오타, 또는 그 대상 형식에 숫자 필드가 없다. 칸이 글자라 DB 검증이 잡지 못한다. |
| 장비 하나를 벗었는데 같은 장비 둘의 보정이 다 빠짐 | 출처가 같다. 에디터의 능력치 보정은 출처가 항목 ID다. 같은 것을 여럿 끼는 게임은 장비마다 다른 id(`Game.NewId`)를 출처로 코드에서 붙인다(5-1). |
| 퍼센트 30을 적었더니 31배 | 퍼센트·독립 곱 값은 비율이다. +30%는 `0.3`. |
| 강화가 곱으로 불어남 | 독립 곱(`Multiply`)을 흔하게 썼다. 기본은 퍼센트(같은 칸 합). 독립 곱은 드문 강한 효과에만. |
| 피해 증폭 둘이 곱해짐 | 증폭 능력치(기본 0)에 퍼센트를 썼다. 증폭은 플랫(0.1 = 10%)으로 쌓아야 합이 된다(5-3). |
| 영구 강화가 다음 판에 안 보임 | 영구 Modifier는 대상 id로 붙는다. 판마다 id가 바뀌는 개체(`Game.NewId`로 만든 적 등)에 붙였다. 판마다 같은 id(`"hero"`)에 붙인다. |
| 이벤트를 받아 피해를 줄였는데 체력이 이미 깎여 있음 | 이벤트는 일어난 뒤에 온다. 줄이거나 막는 효과는 가로채기로. |
| `+ 블럭`에 `가로채기`가 없음 | 그 칸의 `[TriggerSlot]`에 `TriggerKinds.Intercept`가 없다. 도전과제·능력·로그 칸에는 없다. 걸려 있는 동안만 가로채려면 상태 효과(29장)로, 늘 가로채려면 프로젝트 기능의 잠금에 더한다(3-6). |
| 의도 목록이 비어 있음 | 의도 클래스에 `[Intent]`가 없다. |
| 가로채기가 걸리지 않음 | 규칙 함수가 `Game.Intercept`를 부르지 않거나, 다른 형식(상속한 형식)을 통과시켰다. 형식이 정확히 같아야 한다. 규칙 함수를 거치지 않고 `hp`를 직접 깎는 곳이 있으면 그곳은 가로채기를 건너뛴다. |
| 의도 고치기가 아무 일도 하지 않음 | 규칙 함수가 돌려받은 의도가 아니라 원래 인자를 썼다(`var hit = Game.Intercept(…)` 뒤 `hit.amount`). 또는 가로채기가 아닌 Trigger의 결과에 넣었다(Journal 경고 "고칠 의도가 없습니다"). |
| 취소가 안 됨("cancelled 필드가 없어 막을 수 없습니다") | 의도 클래스에 `public bool cancelled;`가 없다. |
| 순서에 따라 결과가 바뀜 | 같은 의도에 여럿이 걸리면 순서(`order`)가 작은 것부터다. 같은 순서면 등록(DB) 순서에 기대게 되므로, 결과가 달라지는 조합에는 서로 다른 순서를 준다(5-4). |
| 막힌 시전에도 충전이 빠짐 | 의도 밖의 값을 바꾸는 가로채기가 막는 가로채기보다 먼저 돌았다. 그런 가로채기는 순서를 크게 둔다(5-7). 체력·골드 같은 게임 값은 가로채는 쪽에서 바꾸지 말고 의도에 실어 규칙 함수가 바꾼다(5-5). |
| "가로채기 사슬이 16단계를 넘어 끊었습니다" | 가로채기 결과가 같은 의도를 다시 만든다(가로채기 안에서 `Rules.Damage`를 부르는 등). 끊긴 뒤의 가로채기는 적용되지 않는다. |
| 버튼은 켜져 있는데 누르면 실패 | `Game.CanUse`는 결과(비용 내기)를 모른다. 사용 조건에 `플레이어 데이터 검사`(≥)를 둔다. |
| 비용 0인데 `CanPay`가 거짓 | `CanPay`는 가로채기 전 값이다. 화면이 비용 0 상태를 함께 본다(5-6). |
| `Game.Pay`가 실패했는데 앞의 변경이 남음 | 행동 밖에서 불렀다. 취소는 행동 단위다. 행동 안에서 부른다. |
| 얻는 것을 비용 내기 음수로 | 검증 오류다. 얻는 것은 `플레이어 데이터에 주기`. |
