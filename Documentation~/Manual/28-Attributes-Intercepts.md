# 28. 스탯·Modifier·행동 직전 이벤트·비용

**한 줄 요약**: 캐릭터의 공격력·방어력 같은 **스탯**는 객체의 숫자 필드이고, 장비·버프·유물이 붙이는 **Modifier**가 최종값을 만든다(같은 칸은 더하고 칸끼리는 곱한다). **행동 직전 이벤트**는 피해·죽음·비용 같은 일이 일어나기 **직전에** 그 내용(**행동 직전 이벤트**)을 고치거나 막는 단계다. **비용**(`Game.Pay`)은 행동 직전 이벤트를 거친 뒤 값을 내고, 모자라면 행동 전체를 취소한다(`Game.TryPay`는 취소하지 않고 거짓). 액티브 효과·패시브 효과 하나의 수치를 통째로 키우는 **효과 배율**도 행동 직전 이벤트 하나로 고친다. 모두 행동 안에서 돌아 Undo·저장·멀티플레이가 그대로 따라온다.

**언제 쓰는가**

- 장비를 끼면 공격력 +30, 버프 동안 +20%, 벗거나 끝나면 원래대로(Modifier). 붙이는 곳은 셋이다: 장비·유물처럼 가진 동안은 DB › 패시브 효과의 가진 동안 스탯 보정(17장 0-1), N턴 버프는 상태 효과의 스탯 보정(29장), 그 둘로 안 되는 것만 코드가 붙이고 떼는 `Game.AddModifier`(5-1).
- 판이 끝나도 남는 영구 강화(영구 Modifier).
- "받는 피해 −5", "죽는 대신 체력 1", "이번 턴 비용 0", "다음 스킬 2회", "이 카드의 수치 2배"처럼 **효과를 고치는 효과**(행동 직전 이벤트).
- 에너지·마나·골드를 내고, 모자라면 아무것도 바뀌지 않게 할 때(비용).

이 문서에서 쓰는 용어:

| 용어 | 뜻 |
|---|---|
| 스탯 (Attribute) | `id`가 있는 객체(유닛·카드 …)의 숫자 필드(`public double attack;`). 필드 값이 기본값이다. 따로 선언하지 않는다. |
| Modifier | 스탯 하나에 붙는 보정 한 줄: 대상 id · 필드 이름 · 칸 · 값 · 출처. 플레이어 데이터에 저장된다. 패시브 효과의 `modifiers`(가진 동안 스탯 보정)는 저장하지 않고 읽을 때 계산한다(2-2). |
| 칸 (`ModifierKind`) | Modifier가 들어가는 자리. 플랫(`Flat`, +30) · 퍼센트(`Percent`, 0.2 = +20%) · 독립 곱(`Multiply`, 0.5 = ×1.5). |
| 출처 (source) | Modifier를 붙인 쪽의 ID(장비·유물·액티브 효과 …). 같은 출처의 Modifier를 한꺼번에 뗀다. |
| 행동 직전 이벤트 (코드 `Intent`) | 곧 일어날 일의 내용을 담은 클래스(`DamageIntent { targetId, amount, cancelled }`). 피해·죽음처럼 장르 규칙의 행동 직전 이벤트는 프로젝트가 정한다. GBible이 직접 하는 일(사용·비용 지불·상태 효과 걸기·확률·효과 배율)의 행동 직전 이벤트는 기본 제공이다(4-14). |
| 행동 직전 이벤트 받기 (코드 `Intercept`) | 규칙 함수가 `Game.Intercept(행동 직전 이벤트)`로 통과시키는 순간, 받는 쪽이 그 내용을 고치거나 막는 단계. 받는 쪽은 트리거 잎 `행동 직전 이벤트 받기` + 결과 `행동 직전 이벤트 고치기`(또는 `[BindTo]` 코드)로 만든다. |
| 규칙 함수 (중간층) | 행동·Block·`[BindTo]`가 같이 쓰는 프로젝트의 평범한 static 함수(`Rules.Damage`). 피해 계산 순서와 행동 직전 이벤트 자리가 여기 있다. |
| 비용 (Cost) | 플레이어 데이터의 숫자를 내는 일. `Game.Pay`·`Game.TryPay`·결과 `비용 내기`. 기본 제공 행동 직전 이벤트 `CostIntent`를 거친다. |
| 효과 배율 (Power) | 액티브 효과를 쓸 때·패시브 효과가 발동할 때 한 번 정해지는 수치 배율(기본 1). 기본 효과 Block이 수치에 곱한다. 기본 제공 행동 직전 이벤트 `효과 배율`(`PowerIntent`)로 고친다(4-14). |
| 컨텍스트 (`TriggerContext`) | 발동한 순간의 정보. 행동 직전 이벤트로 발동하면 `IsIntercept`가 참이고 `Event`가 행동 직전 이벤트 객체다. `Power`가 효과 배율이다. |

> **"Stat"이 아니라 스탯.** GBible에서 Stat은 로그(15장, `StatData`·`session.Stats`)를 가리킨다. 캐릭터의 공격력·방어력은 **스탯(Attribute)**라고 부른다.

---

## 1. 효과의 문법 — 만들 수 있는 효과의 천장

기획서의 효과 문장은 네 부품의 조합으로 쪼갤 수 있다.

> **언제** + **누구에게** + **무엇을** + **안전장치**

이 문법이 GBible이 표현하려는 효과의 **천장**(끝)이다. 문장을 이 부품으로 쪼갤 수 있으면 만들 수 있고, 어느 부품에도 들지 않으면 효과가 아니라 화면 코드나 다른 시스템의 일이다.

| 부품 | 종류 | GBible에서 | 상태 |
|---|---|---|---|
| 언제 | 즉시 | 결과 Block · `[BindTo]` | 있음 |
| | 발동("~하면") | Trigger(이벤트 받기 · 조건 충족 · 사용 · 행동 직전 이벤트) | 있음 |
| | 지연("2턴 뒤") | 상태 효과의 끝날 때 결과 · 예약(`Game.ScheduleAfter`·`ScheduleOn`) | 있음(→ `29-Statuses-Schedules-Choices.md`) |
| | 지속("3턴 동안") | Modifier · 상태 효과 | 있음(상태 효과 → 29장) |
| | 가진 동안("유물을 가진 동안 +2") | 패시브 효과의 `modifiers`(가진 동안 스탯 보정) | 있음(→ `17-Custom-Features.md` 0-1) |
| 누구에게 | 대상 고르기 | 대상 Block(`TargetSelector`, 기본 제공 목록·이벤트의 개체·거르기·무작위·정렬 → `16-Abilities-Usable.md` 2-6) | 있음 |
| | 화면이 판정한 대상(충돌·투사체·미니게임) | **판정 보고 행동**: 화면이 판정하고 결과(누가 누구를 맞혔나·성공 여부)를 행동 하나로 보낸다 | 있음(→ `33-Tick-Preview-Csv.md`) |
| 무엇을 | 데이터 바꾸기 | 플레이어 데이터에 주기 · 효과 Block · 스탯 보정 | 있음 |
| | 만들기 · 없애기 · 바꾸기(소환·제거·변신) | 효과 Block · `[BindTo]`(새 개체의 id는 `Game.NewId`·여럿이면 `Game.NewIds`) | 있음 |
| | 행동 직전 이벤트("대신", "막는다", "−5") | 행동 직전 이벤트 + 행동 직전 이벤트 고치기(값 틀 `{user}`·`{stacks}`·`{level}` …, 3-4) | 있음 |
| | 수치 배율("이 카드의 수치 2배") | 행동 직전 이벤트 `효과 배율` 받기 + 행동 직전 이벤트 고치기 `multiplier`(4-14, 5-12) | 있음 |
| | 선택 요구("카드 1장을 고른다") | 선택 대기(`Game.Ask` · 결과 `선택 요구`) | 있음(→ 29장) |
| | 비용 | `Game.Pay` · 비용 내기 | 있음 |
| | 되감기("이번 턴 처음으로", "한 전투에 3번 되감기") | `session.HistoryMark` · `session.UndoTo(지점)` | 있음(→ `02-Actions-Undo.md`) |
| 안전장치 | 연쇄 제한 | 사슬 16단계에서 끊기(`TriggerSystem.MaxDepth`) | 있음 |

**문법 밖**은 둘뿐이다: **저장을 넘는 되감기**("어제 저장한 판으로 돌아가기", 되감기 지점은 세션 안에서만 있고 판 저장에 Undo 이력은 들어가지 않는다)와 **난수를 다시 굴리는 되감기**(되감으면 난수 상태도 돌아가므로, 같은 행동을 다시 하면 결정론 난수라 같은 결과가 나온다 — 07장). 같은 세션 안의 되감기는 `UndoTo`(02장)로 문법 안이다.

실시간 물리·이동 자체는 화면 코드의 자유 영역이다. 화면이 정하는 판정(충돌·투사체·반사·미니게임)은 위 표의 **판정 보고 행동**이면 문법 안이다: 규칙은 결과만 받아 피해·행동 직전 이벤트·이벤트를 그대로 탄다. 위치·속도는 판 데이터에 넣지 않고, 한 번 정해지는 위치(폭발 지점)만 행동 필드로 넘긴다(실시간 묶음 `Strike { hits }` → `33-Tick-Preview-Csv.md`).

쪼개 보기:

| 기획 문장 | 언제 | 누구에게 | 무엇을 |
|---|---|---|---|
| "적에게 피해를 줄 때마다 그 적에게 화상 1" | 발동(피해 받기) | 그 적 | 데이터 바꾸기 |
| "착용하는 동안 공격력 +30" | 지속 | 착용한 유닛 | 데이터 바꾸기(Modifier) |
| "주인공이 받는 피해 −5" | 발동(피해 직전) | 그 피해 | 행동 직전 이벤트 |
| "에너지 2를 내고 모든 적에게 8 피해" | 즉시 | 모든 적 | 비용 + 데이터 바꾸기 |
| "도발: 적의 공격이 나에게 온다" | 발동(공격 직전) | 그 공격 | 행동 직전 이벤트(대상을 `{user}`로, 5-8) |
| "화살이 닿은 적에게 10 피해"(실시간) | 즉시(판정을 받은 행동) | 화면이 판정한 적 | 데이터 바꾸기(판정 보고 행동) |

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
- 패시브 효과의 `modifiers`(가진 동안 스탯 보정)도 같은 칸에 들어가 함께 더해진다(2-2).

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

**패시브 효과의 가진 동안 스탯 보정은 어디에도 저장되지 않는다.** DB › 패시브 효과의 `modifiers`(가진 동안 스탯 보정, 17장 0-1)는 `Game.Attribute`·`Game.Modifiers`를 읽을 때마다 가진 목록을 보고 계산한다.

- 가진 동안만 보이고, 가진 목록(플레이어 데이터)에서 빠지면 저절로 빠진다. 붙이고 떼는 코드가 없고, Undo·저장·멀티플레이는 가진 목록을 따라온다.
- 가진 쪽마다 `modifierWhen`(보정 조건)·레벨·침묵(29장 `silencesPassives`)·줄마다의 `target`(받을 대상)·`stacking`(여럿일 때)을 따져 계산한다(17장 0-1).
- `Game.Modifiers(대상, 스탯)` 목록에는 저장된 것과 같은 모양으로 들어 있다(`source` = 패시브 효과 ID, `id` = `"passive:ID"`, 값 = 위 규칙으로 계산한 값). 툴팁이 그대로 읽는다.
- `$modifiers`에는 없고, `Game.RemoveModifiers`·`session.Attributes.Remove`로 떼지지 않는다. 빼려면 가진 목록에서 그 ID를 뺀다.

### 2-3. 이벤트와 행동 직전 이벤트

```
규칙 함수(Rules.Damage)
  1. 행동 직전 이벤트를 만든다            new DamageIntent { targetId = "hero", amount = 10 }
  2. Game.Intercept(행동 직전 이벤트)     행동 직전 이벤트 항목들이 차례로 행동 직전 이벤트를 고친다(−5, ×0.5, 막기 …)
  3. 고쳐진 행동 직전 이벤트로 일을 한다    target.hp -= hit.amount
  4. 이벤트를 쏜다             DamageDealt — 로그·도전과제·화면이 받는다
```

- 이벤트는 일이 **일어난 뒤** 알린다. 받는 쪽은 이미 일어난 결과를 바꾸지 않는다.
- 행동 직전 이벤트는 일이 **일어나기 직전에** 내용을 고치거나 막는다.
- **순서**: 행동 직전 이벤트 잎의 순서(`order`)가 작은 것부터, 같으면 등록 순서. 행동 직전 이벤트가 막히면(`cancelled = true`) 그 뒤 행동 직전 이벤트는 건너뛴다.
- 같은 순서 칸 안에서 같은 목록을 보는 `perElement`(놓인 것마다) 패시브 효과(조커)끼리는 등록 순서가 아니라 목록 순서(칸 왼쪽부터)로 돈다(17장 0-1).
- 그 행동 직전 이벤트를 받는 항목이 없으면 `Game.Intercept`는 바로 돌아온다.
- **사슬 한도**: 행동 직전 이벤트 결과가 다시 행동 직전 이벤트를 부르는 사슬은 이벤트 사슬과 같은 깊이를 세어 16단계에서 끊는다. 끊기면 행동 직전 이벤트를 고치지 않은 채 돌려주고 Journal에 경고를 남긴다(예외는 던지지 않는다).
- 모두 행동 안에서 돈다. Undo하면 고쳐진 결과까지 함께 돌아간다.

> **규칙: 장르 핵심 규칙 함수(피해·회복·죽음·사용·비용·뽑기)마다 행동 직전 이벤트 자리를 미리 둔다.** 그러면 나중에 생기는 기믹("받는 피해 −5", "회복량 2배", "죽음 막기")을 기획 단계에서 미리 알 필요가 없다. 규칙 함수는 그대로 두고 DB에 항목만 더한다. GBible이 직접 하는 일(액티브 효과 사용 `Game.Use`, 비용 `Game.Pay`, 상태 효과 걸기 `Game.ApplyStatus`, 확률 `Game.Chance`, 액티브 효과·패시브 효과의 효과 배율)에는 이미 행동 직전 이벤트 자리가 있다(기본 제공 행동 직전 이벤트, 4-14).

### 2-4. 비용

`Game.Pay(key, path, amount, what, forId, user)`는 다음 순서로 돈다.

1. 기본 제공 행동 직전 이벤트 `CostIntent { key, path, amount, floor = 0, forId, userId }`를 행동 직전 이벤트에 통과시킨다("비용 0"·"빚 허용"·"행성 카드는 공짜" 같은 효과가 여기서 고친다). `forId`는 무엇을 위한 비용인지(사는 카드·쓰는 주문 ID), `userId`는 내는 쪽 id다. 둘 다 거르기용이고 비우면 빈 글자다.
2. 막혔거나(`cancelled`) 양이 0 이하가 되었으면 아무것도 내지 않는다.
3. 낸 뒤 남을 값(지금 값 − 양)이 하한(`floor`, 기본 0)보다 작으면 플레이어용 실패(`GameFailure`, 키 `FailureKeys.NotEnough`, `30-Balance-Tables.md` 2-4) → 행동 실패, 그 행동에서 바꾼 것이 모두 취소된다(먼저 낸 다른 비용도). 하한이 0이면 "지금 값이 양보다 적으면 실패"와 같다. 아주 작은 차이는 같다고 본다(11장 8절의 숫자 비교 규칙 — 0.1씩 찬 0.9999999999999999로도 1을 낸다. 그때 남는 값은 하한 그대로). 실패 글의 `{what}`은 `what`(텍스트 키면 번역된 글, 비우면 "플레이어 데이터 › 필드")이다.
4. 아니면 그만큼 뺀다. 행동 직전 이벤트가 `key`·`path`를 바꿨으면 바뀐 곳에서 낸다.

`Game.TryPay(key, path, amount, forId, user)`는 같은 순서로 돌되 3에서 실패를 던지지 않고 **아무것도 내지 않은 채 거짓**을 돌려준다(냈거나 공짜면 참). 행동이 취소되지 않으므로 틱마다 도는 행동 안에서 "MP가 되면 쏜다"(연계 주문·자동 시전)처럼 되는 것만 할 때 쓴다(37장 5-6). 거짓이어도 비용 지불 행동 직전 이벤트의 결과가 바꾼 다른 값(행동 직전 이벤트 밖 값)은 남는다 — 행동 직전 이벤트는 행동 직전 이벤트만 고치는 것이 안전하다.

빚: 행동 직전 이벤트가 `floor`를 음수로 바꾸면 그만큼 0 아래로 내려갈 수 있다("−20까지 빚" = `floor` 값으로 바꾸기 `-20`, 5-11).

없는 플레이어 데이터·없는 필드·숫자가 아닌 필드는 "모자랍니다"가 아니라 프로그래머 실수(`InvalidOperationException`)다.

경로(필드 경로, 11장 3-1)로 낼 곳은 **숫자 하나**다(기본 숫자 형식 또는 `BigNumber`).

**큰 수 비용**(방치형): 낼 곳이 `BigNumber` 필드이거나 양이 `BigNumber`면(`Game.Pay(…, BigNumber 양)`) 큰 수로 계산한다 — 1e308을 넘어도 된다. 할인·두 배 같은 비용 기믹은 `CostIntent.multiplier`(곱할 배율, 기본 1 — 할인 0.8 · 두 배 2)를 고친다. `amount`는 double이라 1e308을 넘으면 무한대이므로 큰 수 비용에서는 `amount` 대신 `multiplier`로 고친다. 큰 수 양을 직접 바꿀 때만 `big`. 실패 글의 값도 큰 수 표기(`1.23K`·`1.5e400`)다.

- 딕셔너리의 키 하나: `stock[gold]` = `stock`(`Dictionary<string, int>`)의 `gold` 키. 재화·재료를 딕셔너리 하나에 모아 두는 게임이 그대로 쓴다. 없는 키는 0으로 보므로 양이 0보다 크면 "모자랍니다"로 실패한다(프로그래머 실수가 아니다).
- 목록에서 id로 고른 요소의 필드: `units[hero].mana`.
- 쓸 수 없는 것: 요소마다 펼치는 `[]`(`units[].mana` — `Game.Pay`·`TryPay`는 프로그래머 실수, 결과 `비용 내기`는 검증 오류), 부모가 구조체인 숫자(읽은 것이 사본이라 빼도 실제 값이 바뀌지 않는다 — `[Serializable]` 클래스로 둔다). 오류 문장은 4-12.

`Game.CanPay`는 지금 값만 본다(행동 직전 이벤트 전, 하한 0 기준). 비용 0·빚 허용은 모른다. 화면에서 그것까지 맞게 보려면 그 행동을 `Game.CanDo(행동, out 이유)`로 해 본다(행동 직전 이벤트까지 돌려 보고 안 되는 이유를 준다, 33장 2-2). 화면 코드에 "비용 0이면 …" 같은 규칙 계산을 따로 쓰지 않는다.

---

## 3. 에디터에서 쓰는 법

### 3-1. 효과 `스탯 보정`

결과 칸 `+ 결과 추가` → `대상에게 효과` → `+ 효과 추가` → 분류 **스탯** → `스탯 보정`. 최종 대상마다 Modifier 한 줄을 붙인다.

| 칸 | 툴팁 | 값 |
|---|---|---|
| `attribute` | 스탯(숫자 필드 이름) | 대상 클래스의 숫자 필드 이름 그대로(`attack`) |
| `kind` | 칸 | `플랫(+값)`(`Flat`) / `퍼센트(0.2 = +20%)`(`Percent`) / `독립 곱(0.5 = ×1.5)`(`Multiply`) |
| `value` | 값(퍼센트는 0.2 = +20%) | 숫자. 음수면 깎는다 |
| `permanent` | 영구(프로필에 남김) | 끄면 이번 판만 |

- **출처**는 이 효과를 가진 항목의 ID다(컨텍스트의 `Owner`: 액티브 효과 ID, 사용 기능이 붙은 인스턴스 ID, 유물 ID …).
- 요약: `attack +30`, `attack +20%`, `attack ×1.5`.
- 검증: "스탯 보정: 스탯(숫자 필드 이름)을 적으세요."
- 칸이 글자라서 필드 이름 오타는 검증이 잡지 못한다. 실행할 때 대상에 그 숫자 필드가 없으면 "[MomoiProject] Unit에 숫자 필드 'atack'가 없어 Modifier를 붙일 수 없습니다." 예외로 그 행동이 취소된다.

### 3-2. 효과 `스탯 보정 떼기`

같은 자리의 `스탯 보정 떼기`. 최종 대상에서 그 출처의 Modifier를 모두 뗀다.

| 칸 | 툴팁 | 값 |
|---|---|---|
| `source` | 출처(비우면 이 항목) | 비우면 이 효과를 가진 항목 ID. 다른 항목이 붙인 것을 떼려면 그 ID를 적는다 |

요약: `보정 떼기` / `보정 떼기(rage)`.

### 3-3. 트리거 잎 `행동 직전 이벤트 받기`

Trigger 칸 `+ 블럭` → 그룹 **트리거** → `행동 직전 이벤트 받기`. 칸의 잠금에 행동 직전 이벤트가 있을 때만 나온다(3-6). 문장은 `{intentType} 행동 직전 이벤트 받기`.

| 칸 | 툴팁 | 값 |
|---|---|---|
| `intentType` | 행동 직전 이벤트 | `[Intent]`가 붙은 형식 목록(프로젝트 것 + 기본 제공 `사용`·`비용 지불`·`상태 효과 걸기`·`확률`·`효과 배율`, 4-14). 하나도 없으면 "[Intent]가 붙은 행동 직전 이벤트 형식이 없습니다" |
| `filter` | 행동 직전 이벤트 조건 | 이벤트 조건과 같은 값 조건 식(`필드 검사` 등). 뿌리는 고른 행동 직전 이벤트다. 비우면 그 행동 직전 이벤트가 지날 때마다 |
| `order` | 순서(작을수록 먼저) | 정수. 같으면 등록 순서 |

- 요약: `피해 행동 직전 이벤트 받기(…일 때)`. Alt를 누르면 `intercept 피해 where …`.
- **순간의 규칙**: 행동 직전 이벤트 잎은 그 행동 직전 이벤트가 `Game.Intercept`를 지나는 순간에만 참이다. `AND`로 조건 충족(`플레이어 데이터 검사` 등)을 붙이면 "그 상태일 때만 행동 직전 이벤트를 받아 고친다"가 된다. 이벤트 받기와 `AND`로 묶으면 같은 순간에 오지 않아 발동하지 않는다.
- 검증 오류: "행동 직전 이벤트 받기: 행동 직전 이벤트가 선택되지 않았습니다." / "행동 직전 이벤트 받기: 행동 직전 이벤트 '…'을 찾을 수 없습니다(이름이 바뀌었는지 확인)." / "행동 직전 이벤트 받기(피해) 조건: …".

### 3-4. 결과 `행동 직전 이벤트 고치기`

`+ 결과 추가` → 그룹 **결과** → `행동 직전 이벤트 고치기`. 칸의 잠금에 행동 직전 이벤트 고치기가 있을 때만 나온다.

| 칸 | 툴팁 | 값 |
|---|---|---|
| `path` | 필드 | 행동 직전 이벤트의 필드(같은 Trigger의 행동 직전 이벤트 잎이 고른 행동 직전 이벤트에서 고른다). 마지막 마디는 딕셔너리의 키도 된다(`byElement[fire]`에 쓴다, 없던 키면 만든다). 괄호 안의 점(`x[forest.chest3]`)은 나누지 않는다 |
| `op` | 방법 | `값으로 바꾸기`(`Set`) / `더하기`(`Add`, 기본) / `곱하기`(`Multiply`) / `막기`(`Cancel`) |
| `value` | 값(숫자·글자, 틀 {user} {user:경로} {event:경로} {data:키.경로} {count:키.경로} {stacks} {level} {level:2/4/7} {balance:ID}) | 값으로 바꾸기는 그 필드 형식의 값, 더하기·곱하기는 숫자. 아래 틀을 쓸 수 있다. 막기는 비운다 |

**값 틀** — 값 칸의 `{…}`는 발동한 순간의 값으로 바뀐 뒤 쓰인다.

| 틀 | 바뀌는 값 | 쓰는 예 |
|---|---|---|
| `{user}` | 컨텍스트의 사용자 id. 상태 효과·패시브 효과의 행동 직전 이벤트에서는 가진 쪽의 id(`perElement`(놓인 것마다)면 그 조각의 id). 가진 쪽에 `id`가 없으면(`run › relics`의 `run`) 빈 글자가 된다. 그러면 대상을 찾지 못하므로 쓰지 않는다 | 도발·대신 맞기: `targetId` `값으로 바꾸기` `{user}`(5-8, 5-9) |
| `{user:경로}` | 사용자의 그 필드 값(필드 경로, 저장된 값 그대로 — 보정은 넣지 않는다). 사용자가 없으면 빈 글자 | 이 조커에 쌓인 값만큼: `amount` `더하기` `{user:counter}` · 지팡이 레벨: `{user:level}` |
| `{event:경로}` | 받은 이벤트의 그 필드(행동 직전 이벤트면 그것의 필드) | 받은 피해만큼 회복: `{event:amount}` |
| `{data:키.경로}` | 플레이어 데이터 `키` 안의 그 필드. 키에 점이 있어도 된다 — 정의된 가장 긴 앞부분이 키다(`{data:player.inventory.gold}`의 키는 `player.inventory`). 없는 키면 빈 글자 | `{data:run.gold}` |
| `{count:키.경로}` | 그 목록·딕셔너리의 요소 수(빈 칸 `null`은 세지 않는다 — 34칸 가방이면 든 것의 수). 키는 `{data:…}`와 같이 찾는다. 목록이 아니면 0 | 조커 수만큼: `{count:run.jokers}` |
| `{owner}` | 이 결과를 가진 항목의 ID(상태 효과·패시브 효과·유물 ID) | 막은 쪽을 행동 직전 이벤트에 남길 때: `blockedBy` `값으로 바꾸기` `{owner}`(행동 직전 이벤트에 그런 글자 필드가 있을 때) |
| `{stacks}` | 가진 쪽에 걸린 **이 상태 효과**의 중첩(상태 효과의 결과에서만. 그 밖에서는 0) | 중첩만큼 피해 감소: `amount` `더하기` `-{stacks}`(5-10) |
| `{level}` | 이 결과를 가진 **패시브 효과**의 레벨. 가진 쪽 기준이다(`Game.Owned(c.Owner, c.User)`와 같다). `perElement`면 그 조각의 레벨(`levelField`), 빌려 쓰는 중이면 원본 조각의 레벨. 패시브 효과가 아닌 곳(상태 효과·액티브 효과)에서는 0 | 레벨만큼 피해 감소: `amount` `더하기` `-{level}` · 레벨만큼 더 발동: 행동 직전 이벤트 `사용`의 `times` `더하기` `{level}` |
| `{level:2/4/7}` | 레벨 번째 값. 1레벨이면 첫 값, 넘치면 마지막 값, 레벨 0이면 0. 소수도 된다(`{level:0.1/0.15/0.2}`) | 레벨 1·2·3에 −2·−4·−7: `amount` `더하기` `-{level:2/4/7}` |
| `{balance:ID}` | 밸런싱 값 ID의 값(30장, 곡선은 보지 않는다) | `amount` `곱하기` `{balance:shieldRatio}` — 수치를 개발자가 한곳에서 고친다 |

- 앞에 붙은 `-`는 틀과 함께 숫자 하나로 읽힌다(`-{stacks}`가 중첩 3이면 `-3`). 틀의 값이 음수면 부호가 바뀐다(`-{balance:x}`에서 x가 −2면 `2`). 합치는 것은 틀 앞의 `-`와 그 값의 부호뿐이라 틀이 펼친 글자 안·틀 밖의 `--`는 그대로다(`{data:run.title}`이 `--Dark--`면 그대로).
- 패시브 효과의 레벨은 `{level}`·`{level:…}`로 코드 없이 읽는다. 레벨로 계산이 더 필요하면(곱한 값·한도) 결과를 코드로 쓴다(`Game.Owned(c.Owner, c.User)`, 17장 0-4).
- 같은 틀을 효과 `필드 바꾸기`·결과 `이벤트 쏘기`의 값(12장)과 조건 `사용자 필드 검사`의 값(11장)에도 쓴다. "이 조각이 발동했다" 이벤트의 필드에 `{user}`를 넣는 식이다. 코드에서는 `ModifyIntentOutcome.Expand(글, 컨텍스트)`로 같은 틀을 편다(4-9).
- 정수 필드는 반올림해 넣는다(더하기·곱하기 뒤, 값으로 바꾸기도). 0.5는 0에서 먼 쪽이다(2.5 → 3, −2.5 → −3, 30장 2-2). 결과가 그 형식에 담기지 않으면(`int`에 30억) 자르지 않고 그 행동이 실패한다(필드 바꾸기와 같은 글, 12장).
- 막기는 행동 직전 이벤트의 `cancelled` 필드를 참으로 만든다. 그 뒤 행동 직전 이벤트는 건너뛰고, 규칙 함수가 일을 하지 않는다.
- 요약: `행동 직전 이벤트 amount -5`, `행동 직전 이벤트 amount ×0.5`, `행동 직전 이벤트 amount = 0`, `행동 직전 이벤트 targetId = {user}`, `행동 직전 이벤트 막기`.
- 검증 오류: "행동 직전 이벤트 고치기: 필드를 고르세요." / "행동 직전 이벤트 고치기: 더하기·곱하기에는 숫자(또는 {stacks}·{level}·{level:2/4/7}·{balance:ID} 틀)를 적으세요."
- 실행 경고(Journal, 출처 `Intercept`) — 아무것도 고치지 않고 알린다: "행동 직전 이벤트 고치기: 행동 직전 이벤트 받기로 발동한 것이 아니라 고칠 행동 직전 이벤트가 없습니다." / "행동 직전 이벤트 고치기: (형식)에 cancelled 필드가 없어 막을 수 없습니다." / "행동 직전 이벤트 고치기: '(형식)'에 '(필드)' 필드가 없습니다."(필드 경로가 그 행동 직전 이벤트에 없을 때) / "행동 직전 이벤트 고치기: '(필드)'에 '(값)'를 넣을 수 없습니다(형식)."(틀이 숫자·참거짓이 아닌 글자로 펼쳐졌을 때) / "행동 직전 이벤트 고치기: '(필드)'에 쓰지 못했습니다(그 필드·키가 없습니다)." / "행동 직전 이벤트 고치기: '(필드)'의 부모가 구조체라 고칠 수 없습니다(클래스로 바꾸세요)." / "행동 직전 이벤트 고치기: '(필드)'의 부모가 비어 있어 쓰지 못했습니다(경로 중간의 값이 없습니다)."(`info.amount`의 `info`가 비었거나 `units[hero]`의 `hero`가 없을 때. 요소마다 펼치는 `[]`가 빈 목록이면 알리지 않는다)

### 3-5. 결과 `비용 내기`

`+ 결과 추가` → 그룹 **결과** → `비용 내기`(플레이어 데이터를 줄 수 있는 칸이면 나온다). `Game.Pay`와 같다(행동 직전 이벤트 `비용 지불`을 거치고, 모자라면 그 행동 전체가 실패한다).

| 칸 | 툴팁 | 값 |
|---|---|---|
| `key` | 플레이어 데이터 | 낼 값이 든 플레이어 데이터 |
| `path` | 필드(비우면 값 전체) | 그 안의 숫자 필드(`energy`, `wallet.gold`). 딕셔너리의 키 하나(`stock[gold]`, 2-4)는 필드 고르기 창에서 고르지 못하므로 코드(`Game.Pay`·`PayOutcome.path`)에 적는다. 요소마다 펼치는 `[]`는 쓸 수 없다(2-4) |
| `amount` | 양 | 0 이상 |
| `what` | 보일 이름(모자랄 때 실패 글에, 텍스트 키 또는 글, 비우면 플레이어 데이터 › 필드) | `energy.name`(텍스트 키) 또는 "에너지". 실패 글의 `{what}` 자리. 기본 문장이면 "에너지이(가) 모자랍니다(1 / 2).", 조사는 텍스트 표의 틀 `{what:이}`로 맞춘다(30장 5-8) |

- 결과 종류는 플레이어 데이터(`OutcomeKinds.PlayerData`)다. 플레이어 데이터에 주기를 고를 수 있는 칸(액티브 효과·사용 기능 …)에서 쓴다.
- 행동 직전 이벤트 `비용 지불`의 `forId`는 이 결과를 가진 항목 ID(액티브 효과·카드 원본), `userId`는 사용자 id다. 그래서 "행성 카드는 공짜"를 행동 직전 이벤트 조건 `forId`로 거른다(4-14).
- 요약: `비용 battle › energy 2`.
- 검증 오류: "비용 내기: 플레이어 데이터를 고르세요." / "비용 내기: 양은 0 이상이어야 합니다(얻는 것은 '플레이어 데이터에 주기')." / "비용 내기: 필드 '…'에 []가 있어 낼 곳이 하나로 정해지지 않습니다. 낼 숫자 하나를 고르세요(목록의 그 요소는 [id]로)."
- `Game.CanUse`는 결과를 보지 않으므로 비용이 모자란 것을 모른다. 버튼을 미리 흐리게 하려면 트리거에 `플레이어 데이터 검사`(energy 2 이상)를 `AND`로 붙이거나, 그 행동을 `Game.CanDo`로 해 본다(결과까지 돌려 보고 이유를 준다, `33-Tick-Preview-Csv.md` 2-2).

### 3-6. 어느 칸에서 쓸 수 있나

| Block | 쓸 수 있는 칸 |
|---|---|
| 스탯 보정 · 스탯 보정 떼기 | `대상에게 효과`를 고를 수 있는 칸 전부(액티브 효과·사용 기능·상태 효과·패시브 효과·도전과제·개발 명령·프로젝트 기능) |
| 행동 직전 이벤트 | `[TriggerSlot]`에 `TriggerKinds.Intercept`가 있는 칸. 로그·도전과제·액티브 효과·사용 기능의 칸에는 없다 → DB › 상태 효과의 '걸려 있는 동안 트리거'(29장), DB › 패시브 효과의 '발동'(유물·특성, 17장 0절), 잠금을 연 프로젝트 기능(17장) |
| 행동 직전 이벤트 고치기 | `[OutcomeSlot]`에 `OutcomeKinds.Intent`가 있는 칸. 행동 직전 이벤트 잎과 같은 항목에서 쓴다 |
| 비용 내기 | `[OutcomeSlot]`에 `OutcomeKinds.PlayerData`가 있는 칸 |

프로젝트 기능에 행동 직전 이벤트를 열려면 두 칸의 잠금에 종류를 더한다.

```csharp
[FieldLabel("발동"), SerializeReference, TriggerSlot(TriggerKinds.Event | TriggerKinds.Condition | TriggerKinds.Intercept)]
public Trigger trigger;
[FieldLabel("효과"), SerializeReference, OutcomeSlot(OutcomeKinds.PlayerData | OutcomeKinds.Coded | OutcomeKinds.Targeted | OutcomeKinds.Intent)]
public List<Outcome> outcomes = new List<Outcome>();
```

### 3-7. 모니터에서 확인하기

- **Modifier**: 모니터 **데이터** 탭에서 플레이어 데이터 `$modifiers`(영구는 `$modifiers.meta`)를 펼치면 붙은 Modifier가 한 줄씩 보인다(대상·필드·칸·값·출처). 패시브 효과의 `modifiers`(가진 동안 스탯 보정)는 저장하지 않으므로 여기 없다. 가진 목록(`run › relics`)을 보거나 `Game.Modifiers`로 읽는다.
- **행동 직전 이벤트**: 결과가 돌 때마다 Journal에 "결과: 행동 직전 이벤트 amount -5"가 남는다(출처는 그 결과를 돌린 기능 — 패시브 효과면 `Passive`, 직접 등록이면 `RunOutcomes`에 준 글자). 행동 탭에서 그 행동을 Undo하면 고쳐진 피해까지 돌아간다.
- **사슬 경고**(출처 `Triggers`): "행동 직전 이벤트 사슬이 16단계를 넘어 끊었습니다(피해). 받는 쪽의 결과가 같은 행동 직전 이벤트를 다시 만드는지 확인하세요."

---

## 4. 코드로 쓰는 법 — 문법 요소를 하나씩

### 4-1. 스탯 = 숫자 필드

```csharp
[Serializable]
public class Unit
{
    public string id;             // 스탯을 가진 객체는 id가 있어야 한다
    public double attack = 100;   // 스탯 attack의 기본값
    public double armor = 50;
    public int hp = 300;
}
```

- 숫자 형식(`int`·`float`·`double` …)의 필드가 그대로 스탯이다. 선언·등록은 없다.
- Modifier는 대상의 **id**로 붙는다. `id`가 없는 객체에 쓰면 "…에 string id 필드가 없어 개체로 찾거나 가리킬 수 없습니다" 예외가 난다.

### 4-2. `Game.Attribute` — 최종값 읽기

```csharp
var attack = Game.Attribute(hero, nameof(Unit.attack));         // 객체로
var armor  = Game.Attribute("goblin1", nameof(Unit.armor));      // id 글자로도
```

- 돌려주는 값은 `double`(2-1의 공식). 정수가 필요하면 규칙 함수에서 반올림한다.
- 저장된 Modifier에 더해 패시브 효과의 `modifiers`(가진 동안 스탯 보정, 2-2)도 그 자리에서 계산해 넣는다.
- 필드 이름은 `nameof`로 쓴다(이름을 바꾸면 컴파일러가 따라온다).
- 그 필드가 없거나 id로 대상을 찾지 못하면 예외 없이 기본값을 0으로 보고 계산한다.
- 숫자 형식이 아닌 필드의 기본값은 화면 칸(`binding-path`)과 같은 규칙으로 읽는다: 큰 수(`BigNumber`)는 double로, 숫자로 읽히지 않는 글자는 0, 목록은 개수. 예외는 나지 않는다(`정렬해서 n개`가 큰 수 체력으로 정렬할 때). Modifier는 숫자 필드에만 붙는다(4-3).
- 값을 바꾸지 않으므로 화면 코드(행동 밖)에서 읽어도 된다.

### 4-3. `Game.AddModifier` — 붙이기

```csharp
string id = Game.AddModifier(hero, nameof(Unit.attack), ModifierKind.Flat, 30, "bond");
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
int removed = Game.RemoveModifiers("bond", hero);        // hero에서 bond가 붙인 것만
Game.RemoveModifiers("aura");                              // 모든 대상에서 aura가 붙인 것
IReadOnlyList<Modifier> all    = Game.Modifiers(hero);                          // hero의 전부
IReadOnlyList<Modifier> attack = Game.Modifiers(hero, nameof(Unit.attack));     // attack 것만
```

| 멤버 | 뜻 |
|---|---|
| `int RemoveModifiers(string source, object entity = null)` | 그 출처의 Modifier를 뗀다(이번 판·영구 둘 다). `entity`를 주면 그 대상 것만. 돌려주는 값은 뗀 개수. 패시브 효과의 `modifiers`(가진 동안 스탯 보정)는 떼지 않는다(2-2) |
| `IReadOnlyList<Modifier> Modifiers(object entity, string attribute = null)` | 대상의 Modifier들. 필드를 주면 그 필드 것만. 패시브 효과의 `modifiers`(가진 동안 스탯 보정)도 들어 있다(`source` = 패시브 효과 ID, `id` = `"passive:ID"`). 툴팁("검 +20, 반지 +20%, 힘의 부적 +4")에 쓴다 |

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

`Game`의 스탯 멤버는 이것을 부른다.

| 멤버 | 뜻 |
|---|---|
| `Get(entity, attribute)` | = `Game.Attribute` |
| `Add(entity, attribute, kind, value, source, permanent)` | = `Game.AddModifier` |
| `RemoveBySource(source, entity)` | = `Game.RemoveModifiers` |
| `Remove(modifierId)` | Modifier 하나를 id로 뗀다. 뗐으면 `true` |
| `Of(entity, attribute)` | = `Game.Modifiers` |
| `static Apply(baseValue, modifiers)` | 기본값에 Modifier 목록을 적용한 값(2-1의 공식). 저장소를 건드리지 않는다 |
| `SessionKey` · `MetaKey` | `"$modifiers"` · `"$modifiers.meta"` |

`Apply`는 저장소를 건드리지 않고 공식만 계산할 때 쓴다: "이 보정 하나를 빼고 하나를 더하면 얼마가 되나".

```csharp
var others = Game.Modifiers(hero, nameof(Unit.attack)).Where(m => m.source != "bond");
var preview = AttributeSystem.Apply(hero.attack, others.Append(new Modifier { kind = ModifierKind.Flat, value = 40 }));
```

"이 장비로 갈아 끼우면 공격력이 얼마가 되나"처럼 행동이 바꿀 결과는 그 행동을 미리 해 본다 — `Game.Preview(new Equip { … }, g => g.Attribute(hero, nameof(Unit.attack)))`(33장). 패시브 효과의 장비 보정까지 그대로 센다.

### 4-7. 행동 직전 이벤트 클래스 — `[Intent]`

```csharp
using System;
using GBible;

[Serializable, Intent("피해")]
public class DamageIntent
{
    public string sourceId;
    public string targetId;
    public int amount;
    public bool cancelled;      // 있으면 막을 수 있다(행동 직전 이벤트 고치기 '막기')
}
```

1. **`[Intent("표시 이름")]`** — DB의 행동 직전 이벤트 목록(행동 직전 이벤트 잎의 `행동 직전 이벤트` 칸)에 이 이름으로 나오고, 컴파일 때 필드 읽기 코드가 생긴다(이벤트의 `[GameEvent]`와 같다).
2. **`class`** — `Game.Intercept`는 같은 객체를 고쳐서 돌려주므로 클래스여야 한다.
3. **필드** — 행동 직전 이벤트를 받는 쪽이 고르고 고칠 값. 객체 대신 id를 둔다.
4. **`public bool cancelled;`** — 막을 수 있게 하려면 둔다. 이름이 정확히 `cancelled`여야 한다.
5. 장르 규칙의 행동 직전 이벤트 형식은 프로젝트가 정한다. 자리는 규칙 어셈블리의 `Assets/MomoiProject/Code/Rules/Events/Intents.cs`(관례 1절 — Unity API 없이). 라이브러리가 주는 것은 GBible이 직접 하는 일의 행동 직전 이벤트 다섯이다: `UseIntent`(사용) · `CostIntent`(비용 지불) · `StatusIntent`(상태 효과 걸기) · `ChanceIntent`(확률) · `PowerIntent`(효과 배율). 필드와 쓰는 법은 4-14.

### 4-8. `Game.Intercept` — 규칙 함수의 행동 직전 이벤트 자리

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

- `T Intercept<T>(T intent) where T : class` — 행동 직전 이벤트를 그것을 받는 항목들에 차례로 통과시키고 **같은 객체**를 돌려준다(고쳐진 채로).
- 형식은 정확히 같아야 걸린다(상속한 형식은 따로다).
- 돌려받은 값으로 일을 한다. 원래 인자(`amount`)를 다시 쓰면 행동 직전 이벤트가 무시된다.
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
var taunt  = new ModifyIntentOutcome { path = "targetId", op = IntentOp.Set, value = "{user}" };     // 가진 쪽에게로(값 틀은 3-4 표 그대로 — "-{stacks}", "-{level:2/4/7}" …)
var debt   = new ModifyIntentOutcome { path = "floor", op = IntentOp.Set, value = "-20" };          // 비용 지불: −20까지 빚
```

| `InterceptTrigger` | 뜻 |
|---|---|
| `intentType` | 행동 직전 이벤트 형식의 전체 이름 |
| `filter` (`ValueFilter`) | 행동 직전 이벤트 조건(`FieldFilter`·`AllFilter` …). `null`이면 언제나 |
| `order` | 순서(작을수록 먼저) |
| `Kind` | `TriggerKinds.Intercept` |

| `ModifyIntentOutcome` | 뜻 |
|---|---|
| `path` | 행동 직전 이벤트의 필드 경로(점으로 내려간다) |
| `op` (`IntentOp`) | `Set` · `Add` · `Multiply` · `Cancel` |
| `value` | 글자로 적은 값(숫자도 `"-5"`). 틀 `{user}` · `{user:경로}` · `{event:경로}` · `{data:키.경로}` · `{count:키.경로}` · `{owner}` · `{stacks}` · `{level}` · `{level:2/4/7}` · `{balance:ID}`(3-4) |
| `static string Expand(string text, TriggerContext context)` | 틀을 그 순간의 값으로 바꾼 글자. 프로젝트의 결과 Block이 같은 틀을 쓰고 싶을 때 부른다 |
| `static string PickByLevel(string values, int level)` | `"2/4/7"`에서 레벨 번째 값(1레벨부터, 넘치면 마지막, 레벨 0이면 `"0"`). `{level:…}`이 쓰는 함수 |
| `Kind` | `OutcomeKinds.Intent` |

### 4-10. `[BindTo]`로 행동 직전 이벤트

조건 분기나 계산이 있으면 결과를 함수 하나로 쓴다(→ `13-BindTo.md`). **첫 매개변수가 행동 직전 이벤트**다.

```csharp
[BindTo(GameIds.Passive.Undying)]
static void Undying(DeathIntent i, TriggerContext c)
{
    i.cancelled = true;          // 행동 직전 이벤트를 고친다 = 행동 직전 이벤트 고치기와 같다
}
```

- 받을 행동 직전 이벤트 = 첫 매개변수의 형식. 다른 행동 직전 이벤트로 발동하면 실행하지 않고 경고를 남긴다(이벤트와 같은 규칙).
- `c.IsIntercept`가 `true`, `c.Event`가 그 행동 직전 이벤트, `c.Owner`가 연결된 항목 ID다.
- 연결은 그 항목의 Trigger(행동 직전 이벤트 잎)를 바꾸지 않는다. 프로젝트 기능은 등록 코드가 `BoundOutcomes`를 확인해야 한다(17장 2-8).

### 4-11. 직접 등록 · 기능 칸

DB 항목 없이 규칙 하나를 코드로 등록할 수도 있다. 개발자가 DB에서 읽고 값을 바꿀 효과(유물·버프·카드 효과)는 DB 항목(패시브 효과·상태 효과)으로 만들고, 직접 등록은 어느 항목에도 속하지 않는 늘 켜진 규칙에만 쓴다(관례 0절 순서).

등록 코드는 Rules의 등록 함수(`static void Register(GameSession session)`)에 두고, Client `Bootstrap.RegisterRules`(`GBibleRuntime.SessionCreated`에 연결된 함수 — 멀티 서버를 포함한 모든 세션)에서 한 번 부른다(17장 2-6, 20장 4절). 규칙 테스트는 세션을 만든 뒤 같은 함수를 부른다(부르지 않으면 그 규칙이 없다).

```csharp
session.Triggers.Register("rule.halfDamage",
    new InterceptTrigger { intentType = typeof(DamageIntent).FullName, order = 10 },
    repeat: true,
    fire: moment => session.Triggers.RunOutcomes(new Outcome[] { new ModifyIntentOutcome { path = "amount", op = IntentOp.Multiply, value = "0.5" } }, moment, "Rules"));
```

- 행동 직전 이벤트로 발동한 순간의 컨텍스트: `IsIntercept = true`, `Event` = 행동 직전 이벤트 객체, `Owner` = 등록한 owner.
- 결과는 `RunOutcomes`로 실행한다(컨텍스트가 결과에 보이고 Journal에 남는다).
- 등록은 플레이어 데이터가 아니다. "켜졌는지"는 행동 안에서 등록을 바꾸지 말고, 늘 등록해 둔 채 플레이어 데이터 조건(`AND 조건 충족`)으로 정한다(5-6, 5-7).

기능 칸에 여는 법은 3-6. `TriggerKinds.Intercept`(트리거 잎 종류), `OutcomeKinds.Intent`(결과 종류)를 더한다.

### 4-12. `Game.Pay` · `Game.TryPay` · `Game.CanPay` · `CostIntent` · `PayOutcome`

```csharp
Game.Pay("battle", "energy", 2, what: "energy.name");     // 행동 안에서. 모자라면 "에너지이(가) 모자랍니다(1 / 2)."(energy.name = "에너지")
Game.Pay("run", "gold", price, what: "gold.name", forId: card.id, user: hero);   // 무엇을 위한 비용 · 내는 쪽(행동 직전 이벤트가 거른다)
if (Game.TryPay("run", "wands[w1].mp", 8, forId: spell.spell, user: wand)) Cast(spell);   // 모자라면 거짓 — 행동은 계속된다
bool canPay = Game.CanPay("battle", "energy", 2);         // 화면 미리보기(행동 직전 이벤트 전)
var pay = new PayOutcome { key = "battle", path = "energy", amount = 2, what = "energy.name" };   // 결과 '비용 내기'
Game.Pay("party", "stock[gold]", 100, what: "gold.name"); // 딕셔너리의 키 하나(Party.stock: Dictionary<string, int>)
```

| 멤버 | 뜻 |
|---|---|
| `void Pay(string key, string path, double amount, string what = null, string forId = null, object user = null)` | 2-4의 순서로 낸다. 낸 뒤 값이 하한(`floor`)보다 작으면 `GameFailure`(`FailureKeys.NotEnough`). 메시지의 앞부분 `{what}`은 `what`(텍스트 키면 번역된 글, 글자면 그대로). 비우면 플레이어 데이터의 이름 › 필드("전투 › energy이(가) 모자랍니다(1 / 2)."). `forId`·`user`(객체 또는 id)는 행동 직전 이벤트의 `forId`·`userId`가 된다 |
| `bool TryPay(string key, string path, double amount, string forId = null, object user = null)` | 같은 순서로 내되, 모자라면 아무것도 내지 않고 `false`(실패를 던지지 않는다). 냈거나 행동 직전 이벤트가 공짜로 만들었으면 `true` |
| `bool CanPay(string key, string path, double amount)` | 지금 값 ≥ 양. 행동 직전 이벤트를 거치지 않는다(비용 0·빚 하한을 모른다) |
| `Pay` · `TryPay` · `CanPay`의 `BigNumber amount` 판 | 큰 수 비용(2-4). 낼 곳은 `BigNumber` 필드든 숫자 필드든 된다 |
| `CostIntent` | 기본 제공 행동 직전 이벤트 "비용 지불": `key`·`path`·`amount`·`multiplier`(곱할 배율, 기본 1)·`big`(큰 수 양)·`floor`·`forId`·`userId`·`cancelled`(4-14) |
| `PayOutcome` | 결과 '비용 내기'(`key`·`path`·`amount`·`what`). `Run`이 `Game.Pay`를 부른다(`forId` = 이 결과를 가진 항목, `user` = 사용자) |

- `path`를 비우면 값 전체(숫자 플레이어 데이터)를 낸다.
- `path`에 쓸 수 있는 경로는 2-4. 행동 직전 이벤트 `CostIntent.path`에는 그 글자 그대로 실리므로, "골드 비용 0"으로 고치는 행동 직전 이벤트 조건은 `path 일치 stock[gold]`다.
- 프로그래머 실수는 `InvalidOperationException`: 없는 플레이어 데이터("[MomoiProject] 비용: 없는 플레이어 데이터 '…'입니다."), 없는 필드("[MomoiProject] 비용: 'battle'에 필드 'enrgy'가 없습니다(…)."), 숫자가 아닌 필드("[MomoiProject] 비용: 'battle › name'는 숫자 필드가 아닙니다(String)."), 요소마다 펼치는 경로("[MomoiProject] 비용: 'party › units[].mana'에 []가 있어 낼 곳이 하나로 정해지지 않습니다. 숫자 필드 하나를 가리키세요(목록의 한 요소는 [id]로)."), 구조체 안의 숫자("[MomoiProject] 비용: '…'의 부모(…)가 구조체라 낸 값을 쓸 수 없습니다. [Serializable] 클래스로 바꾸세요." — 플레이어 데이터 값 자체가 구조체인 바로 아래 필드는 된다).
- 실패가 행동 단위로 취소되므로 행동 안에서 부른다. `TryPay`도 값을 바꾸므로 행동 안에서 부른다. 없는 플레이어 데이터·필드는 `TryPay`도 위 예외다(거짓이 아니다).
- 플레이어에게 보일 실패의 번역·`{what}` 이름은 `30-Balance-Tables.md` 2-4·5-8.

### 4-13. 도우미

| 멤버 | 뜻 |
|---|---|
| `Intents.DisplayNameOf(type)` | `[Intent]`의 표시 이름(없으면 형식 이름) |
| `Intents.IsCancelled(intent)` | 행동 직전 이벤트의 `cancelled`가 참인지 |
| `[IntentTypeRef]` | 글자 필드에 붙이면 에디터가 행동 직전 이벤트 형식 목록을 띄운다(프로젝트 기능이 행동 직전 이벤트 형식을 고르는 칸을 둘 때) |
| `TriggerSystem.MaxDepth` | 사슬 한도(16) |

### 4-14. GBible 기본 제공 행동 직전 이벤트 — 사용 · 비용 지불 · 상태 효과 걸기 · 확률 · 효과 배율

GBible이 직접 하는 일은 그 일을 하기 직전에 아래 행동 직전 이벤트를 `Game.Intercept`에 통과시킨다. 프로젝트는 규칙 함수를 만들지 않고 DB에서 행동 직전 이벤트 잎의 `행동 직전 이벤트` 칸에 고르기만 한다. 행동 직전 이벤트 조건으로 거르고, `행동 직전 이벤트 고치기`(또는 `[BindTo]`)로 고친다.

| 행동 직전 이벤트(DB 이름) | 코드 형식 | 거치는 곳 |
|---|---|---|
| `사용` | `UseIntent` | `Game.Use`·`Game.TryUse`가 결과를 내기 직전(16장 2-11) |
| `비용 지불` | `CostIntent` | `Game.Pay`·`Game.TryPay`·결과 `비용 내기`가 내기 직전(2-4) |
| `상태 효과 걸기` | `StatusIntent` | `Game.ApplyStatus`·효과 `상태 효과 걸기`가 걸기 직전(29장) |
| `확률` | `ChanceIntent` | `Game.Chance`·조건 `확률`이 굴리기 직전 |
| `효과 배율` | `PowerIntent` | 액티브 효과를 쓸 때(`사용`을 지난 뒤 한 번) · 패시브 효과가 결과를 낼 때마다(가진 쪽마다·주기마다·빌려 쓸 때) |

**`사용`(`UseIntent`)**

| 필드 | 뜻 · 고치면 |
|---|---|
| `ability` | 액티브 효과·사용 아이템 ID. 거르기용이다(바꿔도 다른 액티브 효과가 되지 않는다) |
| `userId` | 사용자 id. 거르기용 |
| `times` | 결과를 몇 번 내나(기본 1). 더하기 1 = 두 번 발동(재발동·광란), 0이면 내지 않는다 |
| `cancelled` | 막기 = 봉인·침묵. 실패를 던지지 않고 아무 일 없이 끝난다 |

**`비용 지불`(`CostIntent`)**

| 필드 | 뜻 · 고치면 |
|---|---|
| `key` · `path` | 낼 곳(플레이어 데이터 · 숫자 필드 경로). 바꾸면 다른 곳에서 낸다("골드 대신 체력으로") |
| `amount` | 낼 양. 곱하기 0.5 = 반값, 0 이하면 내지 않는다 |
| `floor` | 낸 뒤 남은 값의 하한(기본 0). 이보다 작아지면 실패한다. 음수로 바꾸면 빚을 허용한다("−20까지 빚" = 값으로 바꾸기 `-20`, 5-11) |
| `forId` · `userId` | 무엇을 위한 비용(사는 카드·쓰는 주문 ID) · 내는 쪽 id. 거르기용. 부르는 쪽이 주지 않으면 빈 글자다(결과 `비용 내기`는 이 결과를 가진 항목 · 사용자를 준다). "행성 카드는 공짜" = 행동 직전 이벤트 조건 `forId` `일치` `planet` + `막기` |
| `cancelled` | 막기 = 공짜(내지 않는다) |

**`상태 효과 걸기`(`StatusIntent`)**

| 필드 | 뜻 · 고치면 |
|---|---|
| `targetId` | 걸릴 대상 id. 바꾸면 그 대상에게 건다. 찾지 못하면 걸지 않고 Journal에 경고를 남긴다 |
| `statusId` | 상태 효과 ID. 값으로 바꾸면 그 상태 효과가 걸린다(화상 → 푸른 화상). 길이를 따로 정하지 않았으면 바뀐 상태 효과의 기본 길이를 쓴다 |
| `stacks` | 걸 중첩. 곱하기 2 = 독 2배. 0 이하가 되면 걸지 않는다 |
| `length` | 지속 길이(이벤트 횟수·초). 처음에 그 상태 효과의 길이로 채워져 오므로 곱하기가 된다(디버프 지속 +20% = 곱하기 1.2). 이벤트 횟수면 반올림한다 |
| `sourceId` | 건 항목 ID(액티브 효과·패시브 효과·상태 효과). 걸린 것의 출처가 된다 |
| `byId` | 건 개체 id(사용자). 걸린 것의 `by`가 되고, 다시 걸면 마지막에 건 쪽으로 바뀐다(처치 기여) |
| `kind` | 그 상태 효과의 종류(`없음`·`버프`·`디버프`). 거르기용("디버프만" = `kind 일치 Debuff`) |
| `maxStacks` · `pulse` | 이번에 걸 것의 최대 중첩(0 = 제한 없음) · 주기(초). 처음 값은 정의의 값이다. "화상 최대 중첩 +2" = `maxStacks` 더하기 2, "화상 틱 속도 +50%" = `pulse` 곱하기 `0.6667`(= 1/1.5)(29장 2-1b) |
| `cancelled` | 막기 = 면역. `Game.ApplyStatus`가 `null`을 돌려준다 |

**`확률`(`ChanceIntent`)**

| 필드 | 뜻 · 고치면 |
|---|---|
| `probability` | 확률(0~1). 곱하기 2 = "모든 확률 2배". 1 이상이면 반드시 성공 |
| `stream` | 난수 흐름 이름(비우면 `chance`) |
| `sourceId` · `userId` | 굴리는 쪽(항목 ID) · 사용자·가진 쪽 id. 거르기용 |
| `cancelled` | 막기 = 반드시 실패 |

- 확률을 바꿔도 난수는 한 번만 굴린다. 같은 시드면 뒤의 결과가 밀리지 않는다(07장).
- 조건 `확률`은 이벤트를 받는 순간·행동 직전 이벤트를 받는 순간·쓰는 순간에만 굴린다. `Game.CanUse` 같은 미리 보기에서는 굴리지 않고 참이다(16장 2-3). 이벤트 없이 값만 보는 `조건 충족`만으로 두거나 행동 밖이면 굴리지 않고 거짓이다 — 그래서 `확률`은 이벤트 받기·행동 직전 이벤트 받기·사용 트리거와 `AND`로 쓴다.

**`효과 배율`(`PowerIntent`)**

| 필드 | 뜻 · 고치면 |
|---|---|
| `ownerId` | 액티브 효과·패시브 효과 ID. 거르기용 |
| `userId` | 사용자(액티브 효과를 쓴 개체)·가진 쪽 id(놓인 것마다면 그 조각, 빌려 쓰는 중이면 원본 조각). 거르기용 |
| `multiplier` | 배율(기본 1). 곱하기 2 = "이 카드의 수치 2배" |

- 고친 배율은 컨텍스트의 `Power`에 들어가 그 사용·발동의 결과 전체에 걸린다. 액티브 효과는 `times`(두 번 발동)와 상관없이 한 번 정한다.
- 기본 효과 Block이 곱한다: `상태 효과 걸기`의 중첩(반올림, 최소 1) · `필드 바꾸기`의 **더하기** 값(곱하기·값으로 바꾸기는 그대로) · `스탯 보정`의 값. 프로젝트 Block은 직접 곱한다: 효과·대상 Block은 `Use.Context.Power`, 결과(`Reward`)·`[BindTo]`는 `c.Power`.
- 패시브 효과의 `modifiers`(가진 동안 스탯 보정)·상태 효과의 결과에는 배율이 없다(늘 1).
- 효과 배율 행동 직전 이벤트를 받아 고치는 패시브 효과 자신의 결과는 배율을 거치지 않는다(같은 행동 직전 이벤트를 다시 만들어 끝없이 돌지 않게).

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
```

- 유물은 DB › 패시브 효과(17장 0절)로 만들고, 가진 목록은 `party › relics`(칸 `ownerKey` `party`, `ownerPath` `relics`)다. 등록 코드가 필요 없다.

### 5-1. 장비 착용·해제 — Modifier를 출처로 붙이고 뗀다

**장비는 이 방식으로 만들지 않는다.** 장비 효과는 DB › 패시브 효과의 `modifiers`(가진 동안 스탯 보정)이고, 가진 목록 = 장비 칸(`battle › units[].gear` — 착용 = 그 목록에 넣는 행동, 해제 = 빼는 행동)이다(17장 0-1). 붙이고 떼는 코드가 없어 벗으면 저절로 빠지고 Undo·저장이 가진 목록을 따라온다. 같은 반지 둘은 일치하는 요소 둘(레벨 2)이라 `perLevel`(레벨마다)을 켜면 하나를 벗을 때 그 몫만 빠진다. 아이템마다 굴린 수치(무작위 접사)는 접사 종류마다 패시브 효과를 두고, 아이템의 레벨 필드에 굴린 값(정수)을 넣고, 보정 값 1(퍼센트면 0.01) + `perLevel`로 나른다(AI 문서 Reference 7-24).

`Game.AddModifier`·`Game.RemoveModifiers`로 출처를 붙이고 떼는 것은 **코드가 잠깐 거는 보정**이다 — 가진 동안(패시브 효과)·N턴 동안(상태 효과)으로 나타낼 수 없고, 한 행동이 계산해 붙이고 다른 행동이 출처로 떼는 것. 예: 전투 시작 순간의 아군 수로 정해지는 결속 보너스(아군이 쓰러져도 그 전투 동안 그대로).

```csharp
[Serializable, BlockInfo("결속 걸기")]
public class ApplyBond : GameAction                     // 전투 시작 행동 안에서 부른다
{
    protected override void Run()
    {
        var allies = Game.Get<Battle>("battle").units.Where(u => u.team == "ally").ToList();
        var bonus = allies.Count * Game.Balance(GameIds.Balance.BondAttackPerAlly);   // 이 순간의 아군 수로 정한다
        foreach (var unit in allies)
            Game.AddModifier(unit, nameof(Unit.attack), ModifierKind.Flat, bonus, "bond");
    }
}

[Serializable, BlockInfo("결속 풀기")]
public class ClearBond : GameAction                     // 전투 끝 행동 안에서 부른다
{
    protected override void Run() => Game.RemoveModifiers("bond");   // 모든 대상에서 bond가 붙인 것
}

// 화면: 최종값과 내역(패시브 효과의 장비 보정도 같은 목록에 보인다 — source = 패시브 효과 ID)
attackText.text = $"{Game.Attribute(hero, nameof(Unit.attack)):0}";
attackTooltip.text = string.Join("\n", Game.Modifiers(hero, nameof(Unit.attack)).Select(m => $"{m.source}: {m.kind} {m.value}"));
```

- 걸기·풀기가 행동이라 Undo 한 번에 Modifier가 함께 돌아간다.
- 값은 붙이는 순간에 정해진다. 늘 지금 값을 따라야 하면(아군 수가 줄면 함께 준다) 코드 보정이 아니라 패시브 효과의 `modifierWhen`·레벨로 만든다(17장 0-1).
- 같은 출처를 한 대상에 두 번 붙이면 `RemoveModifiers(출처, 대상)`가 둘 다 뗀다. 따로 떼야 하면 붙일 때마다 다른 출처(`Game.NewId`)를 쓴다.
- "쓰면 붙고 나중에 떼는" 버프는 코드 없이 액티브 효과의 `스탯 보정`·`스탯 보정 떼기`로 조립한다(→ `16-Abilities-Usable.md` 3-10). 그때 출처는 항목 ID라서, 같은 항목을 한 유닛에 두 번 붙이면 떼기도 함께 된다. 몇 턴 동안만 붙었다가 저절로 떨어지는 버프는 상태 효과의 스탯 보정으로 만든다(→ `29-Statuses-Schedules-Choices.md` 5-2).

### 5-2. 영구 강화 — 대장간

골드를 내고 주인공 공격력 +5. 판이 끝나도 남는다. 가격(강화 단계마다 곡선)과 오르는 양은 DB › 밸런싱 값 `smithCost`·`smithAttack`이다(30장).

```csharp
[Serializable, BlockInfo("대장간 강화")]
public class SmithUpgrade : GameAction
{
    protected override void Run()
    {
        var profile = Game.Get<Profile>("profile");
        Game.Pay("profile", "gold", Game.Balance(GameIds.Balance.SmithCost, profile.smithLevel), what: "gold.name");   // 모자라면 여기서 실패 — 아무것도 바뀌지 않는다
        profile.smithLevel++;
        Game.AddModifier("hero", nameof(Unit.attack), ModifierKind.Flat, Game.Balance(GameIds.Balance.SmithAttack), "smith", permanent: true);
    }
}

// 강화 초기화(환불 화면 등)
// Game.RemoveModifiers("smith");    // 영구 구역까지 smith가 붙인 것을 모두 뗀다
```

- 대상은 id 글자 `"hero"`다. 영구 Modifier는 id로 붙으므로 **판마다 같은 id**인 대상에만 쓴다. 판 밖(주인공이 데이터에 없을 때)에서도 붙는다(4-3).
- 강화할 때마다 한 줄씩 늘어 플랫끼리 더해진다(3번이면 +15).
- 에디터: `스탯 보정`의 `permanent`를 켠다.

### 5-3. 피해 계산 — 중간층 규칙 함수

계산 순서(②~④)는 장르마다 달라서 라이브러리가 아니라 프로젝트의 규칙 함수에 둔다. 효과 Block은 수치만 들고 규칙 함수를 부른다.

| 단계 | 계산 | 어디서 |
|---|---|---|
| ① 스탯 | `Game.Attribute`(Modifier까지 반영한 최종 공격력) | 라이브러리 |
| ② 스킬 피해 | 기본 + 계수 × 스탯 | 규칙 함수 |
| ③ 증폭 | × (1 + 피해 증폭 합). 증폭끼리는 더한다 | 규칙 함수 |
| ④ 추가 피해 | + 플랫. 증폭을 받지 않는다 | 규칙 함수 |
| 방어 | × 100 / (100 + 방어). 추가 피해도 받는다 | 규칙 함수(공식은 프로젝트가 정한다) |
| 행동 직전 이벤트 · 적용 | `Game.Intercept` → 체력 → 이벤트 → 죽음 | 규칙 함수 |

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
        Damage(game, source, target, GameNumbers.RoundToInt(raw * 100 / (100 + armor)));  // 방어(반올림은 라이브러리와 같게, 30장 2-2)
    }

    // 고정 피해: 증폭·방어를 받지 않는 별도 종류
    public static void FixedDamage(GameApi game, Unit source, Unit target, int amount) => Damage(game, source, target, amount);

    // 적용: 행동 직전 이벤트 자리 → 체력 → 이벤트 → 죽음
    public static void Damage(GameApi game, Unit source, Unit target, int amount)
    {
        if (target == null || target.hp <= 0 || amount <= 0) return;
        var hit = game.Intercept(new DamageIntent { sourceId = source?.id, targetId = target.id, amount = amount });
        if (hit.cancelled || hit.amount <= 0) return;
        target.hp -= hit.amount;
        game.Events.Publish(new DamageDealt { sourceId = source?.id, targetId = target.id, amount = hit.amount });
        if (target.hp <= 0) Kill(game, source, target);
    }

    // 죽음: 행동 직전 이벤트 자리(불사·부활) → 처치
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
| 행동 직전 이벤트 | 가시 갑옷 −5 | 129 |

- 피해 증폭은 기본 0인 스탯(`damageAmp`)에 **플랫**(0.1 = 10%)으로 쌓는다. 그러면 `Game.Attribute`가 곧 증폭 합이다.
- 방어 100 / (100 + 방어)는 기본 제안이다. 방어 50이면 받는 피해 67%, 100이면 50%로, 방어를 아무리 올려도 0이 되지 않는다.
- 카드·적 공격·개발 명령이 모두 `Rules.Damage`를 거치므로 행동 직전 이벤트도 모두 같이 탄다.

### 5-4. 가시 갑옷 — 받는 피해 −5(코드 없음)

**에디터**(DB › 패시브 효과 `thornArmor`, 가진 목록 `party › relics`, 17장 0-3):

1. `trigger`(발동): `+ 블럭` → `행동 직전 이벤트 받기` → 행동 직전 이벤트 `피해`. `filter`(행동 직전 이벤트 조건) `+ 블럭` → `필드 검사` → `targetId` `일치` `hero`.
2. `outcomes`(결과): `+ 결과 추가` → `행동 직전 이벤트 고치기` → 필드 `amount`, 방법 `더하기`, 값 `-5`.

요약: `피해 행동 직전 이벤트 받기(…일 때) → 행동 직전 이벤트 amount -5`. `party › relics`에 이 ID가 있는 동안 `Rules.Damage`를 지나는 주인공의 피해가 5 줄어든다. 5 이하의 피해는 0이 되어 들어오지 않는다(규칙 함수의 `hit.amount <= 0`). 가진 쪽(`party`)에 `id`가 없어 주인공은 `holderField`(가진 쪽 필드)가 아니라 조건으로 고른다(동료마다 가진 특성은 `holderField` `targetId`, 17장 0-3).

**순서**: 같은 피해에 "받는 피해 반감"(행동 직전 이벤트 고치기 `amount` `곱하기` `0.5`)도 걸려 있으면, 순서가 작은 쪽이 먼저다.

| 순서 | 피해 20일 때 |
|---|---|
| 가시 갑옷 1, 반감 2 | 20 − 5 = 15 → × 0.5 = 7.5 → 8 |
| 반감 1, 가시 갑옷 2 | 20 × 0.5 = 10 → − 5 = 5 |

**되받아치기는 행동 직전 이벤트가 아니다.** "공격한 쪽에 3 피해"는 피해가 들어온 **뒤**의 일이라 이벤트 받기(`피해`, `targetId` 일치 `hero`)로 만들고, 결과에서 피해 규칙을 부른다(행동 직전 이벤트를 받는 쪽은 행동 직전 이벤트만 고친다). 때린 쪽은 기본 대상 Block `이벤트의 개체`(`sourceId`)로 고른다(`16-Abilities-Usable.md` 3-11).

### 5-5. 불사 — 죽음 막기(`[BindTo]`)

"전투마다 한 번, 죽는 대신 체력 1로 버틴다". 한 번만이라는 분기가 있어 결과를 코드로 쓴다.

**에디터**(DB › 패시브 효과 `undying`, 가진 목록 `party › relics`): `trigger` = `행동 직전 이벤트 받기` → 행동 직전 이벤트 `죽음`, 행동 직전 이벤트 조건 `unitId` `일치` `hero`. 결과 칸은 아래 연결 때문에 잠긴다.

```csharp
public static class RelicCode
{
    [BindTo(GameIds.Passive.Undying)]
    static void Undying(DeathIntent i, TriggerContext c)
    {
        var battle = c.Session.Data.Get<Battle>("battle");
        if (battle.undyingUsed) return;      // 이번 전투에 이미 썼다
        battle.undyingUsed = true;
        i.cancelled = true;                  // 행동 직전 이벤트를 막는다 → 규칙 함수가 surviveHp(1)로 남긴다
    }
}
```

- 체력은 행동 직전 이벤트를 받는 쪽이 아니라 규칙 함수(5-3의 `Kill`)가 바꾼다. 행동 직전 이벤트를 받는 쪽은 행동 직전 이벤트만 고친다. "체력 절반으로 부활"이면 `i.surviveHp = …`로 행동 직전 이벤트에 실어 보낸다.
- 전투를 시작하는 행동이 `undyingUsed = false`로 되돌린다.
- 코드 없이 쓰려면: 트리거 `행동 직전 이벤트(죽음)` AND `플레이어 데이터 검사(battle: undyingUsed 거짓)`, 결과 `행동 직전 이벤트 고치기` `막기` + `플레이어 데이터에 주기` `battle › undyingUsed` 참.

### 5-6. 이번 턴 비용 0

카드 "집중": 이번 턴에 내는 에너지 비용이 모두 0.

**카드를 내는 행동** — 비용을 `Game.Pay`로 낸다(에너지를 직접 빼면 행동 직전 이벤트 `비용 지불`을 거치지 않아 "비용 0"이 붙을 자리가 없다).

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
        Game.Pay("battle", "energy", cost, what: "energy.name", forId: card, user: user);   // 행동 직전 이벤트(비용 지불) → 모자라면 실패
        if (target == null) Game.Use(card, user);
        else Game.Use(card, user, target);
    }
}
```

**집중** — "잠깐 켜지는 행동 직전 이벤트"는 **상태 효과**로 만든다(칸까지는 `29-Statuses-Schedules-Choices.md` 5-5). DB › 상태 효과 `focus`: 트리거 `행동 직전 이벤트 받기(비용 지불; key 일치 battle, path 일치 energy)`, 결과 `행동 직전 이벤트 고치기` `막기`, 지속 `이벤트 횟수` 1(셀 이벤트 턴 끝). 카드는 사용자에게 `상태 효과 걸기`(`focus`)만 한다. 턴이 끝나면 저절로 풀리고, Undo하면 함께 돌아간다.

- 상태 효과 없이 하면 더 든다: 카드가 `battle › freeCost`를 참으로 하고, 늘 등록한 규칙(4-11 — 모양은 5-7의 `rule.echo`와 같은 `AllTrigger` = `InterceptTrigger` + `ConditionTrigger`)이 `행동 직전 이벤트 받기(비용 지불; key 일치 battle, path 일치 energy)` AND `플레이어 데이터 검사(battle: freeCost 참)`일 때 `막기`, 턴 끝 행동이 `freeCost = false`로 되돌린다.
- 상점의 골드 비용(`profile › gold`)은 행동 직전 이벤트 조건에 걸리지 않아 그대로 낸다.
- `forId`(무엇을 위한 비용)에 카드 원본 ID를 넘겼으므로 "이 카드만 공짜"는 행동 직전 이벤트 조건 `forId` `일치` 그 ID, "주인공이 낼 때만"은 `userId` `일치` `hero`로 거른다.
- 버튼 미리보기: `Game.CanPay`는 비용 0을 모른다. 화면은 `Game.CanDo(new PlayCard { … }, out var reason)`로 본다(2-4).

### 5-7. 다음 스킬 2회 발동 — `CastIntent { times }`

> 액티브 효과를 `Game.Use`로 쓰기만 하면 프로젝트 행동 직전 이벤트가 필요 없다. 기본 제공 행동 직전 이벤트 `사용`(`UseIntent`, 4-14)에 이미 `times`·막기가 있다. 메아리 = 행동 직전 이벤트 `사용` 받기 AND `플레이어 데이터 검사(battle: echo 1 이상)`, 결과 `행동 직전 이벤트 고치기` `times` `곱하기` `2` + `플레이어 데이터에 주기` `battle › echo` −1(순서는 크게). 아래 `CastIntent`는 "다른 스킬로 바꾸기"처럼 `사용`에 없는 것이 필요할 때의 모양이다(`UseIntent.ability`는 바꿔도 다른 액티브 효과가 되지 않는다).

스킬을 쓰는 규칙 함수에 행동 직전 이벤트 자리를 두면, "2회 발동"·"침묵(시전 막기)"·"다른 스킬로 바꾸기"가 모두 행동 직전 이벤트 고치기로 된다.

```csharp
[Serializable, Intent("스킬 시전")]
public class CastIntent
{
    public string casterId;
    public string ability;       // 액티브 효과 ID(설정으로 다른 스킬로 바꿀 수 있다)
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

**행동 직전 이벤트를 받는 쪽** — 늘 등록해 두고, 켜졌는지는 `battle.echo`로 정한다(4-11의 등록 함수에서):

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

- 충전을 쓰는 것은 행동 직전 이벤트 밖의 값을 바꾸는 일이다. 순서를 크게(100) 두어 다른 행동 직전 이벤트가 먼저 돌게 한다. 앞에서 침묵이 시전을 막으면(`cancelled`) 메아리는 건너뛰어 충전이 남는다.
- 두 번의 `Game.Use`가 `CastSkill` 행동 하나 안에서 돈다. Undo 한 번에 두 번 모두, 충전까지 돌아간다.

### 5-8. 도발 — 적의 공격을 나에게(값 틀 `{user}`)

"도발: 2턴 동안 적의 공격이 모두 나에게 온다." 공격 대상을 정하는 규칙 함수에 행동 직전 이벤트 자리를 둔다.

```csharp
[Serializable, Intent("공격")]
public class AttackIntent
{
    public string attackerId;
    public string attackerTeam;       // 행동 직전 이벤트 조건으로 "적의 공격만" 거르려고 싣는다
    public string targetId;
    public bool cancelled;
}

public static class Rules             // 5-3의 Rules에 더한다
{
    public static void Attack(GameApi game, Unit attacker, Unit target)
    {
        var attack = game.Intercept(new AttackIntent { attackerId = attacker.id, attackerTeam = attacker.team, targetId = target.id });
        if (attack.cancelled) return;
        var finalTarget = attack.targetId == target.id ? target : game.Find<Unit>(attack.targetId) ?? target;   // 고쳐진 대상으로 다시 찾는다
        SkillDamage(game, attacker, finalTarget, 0, 1, 0);
    }
}
```

**에디터**(DB › 상태 효과 `taunt`, 29장 3-1):

1. `maxStacks` `1`, `reapply` `지속 갱신`, 지속은 게임에 맞게(예: `이벤트 횟수` 2, 셀 이벤트 턴 끝).
2. `trigger`: `행동 직전 이벤트 받기` → 행동 직전 이벤트 `공격`, 행동 직전 이벤트 조건 `attackerTeam` `일치` `enemy`. `holderField`는 비운다(누구를 노린 공격이든).
3. `outcomes`: `행동 직전 이벤트 고치기` → 필드 `targetId`, 방법 `값으로 바꾸기`, 값 `{user}`.

액티브 효과 `provoke`: `대상에게 효과` → `사용자 자신` → `상태 효과 걸기`(`taunt`).

- `{user}`는 가진 쪽(도발을 건 기사)의 id다. 적 AI가 누구를 고르든 `Rules.Attack`을 지나면 기사에게 간다.
- **규칙 함수가 고쳐진 `targetId`로 대상을 다시 찾아야 한다.** 원래 인자(`target`)를 그대로 쓰면 도발이 무시된다.
- 도발한 쪽이 둘이면 둘 다 차례로 고쳐 마지막에 고친 쪽이 남는다(걸린 순서). 하나만 남기려면 도발을 걸 때 다른 도발을 푼다(`상태 효과 풀기`).
- 아군끼리의 공격(혼란)은 `attackerTeam`이 `ally`라 걸리지 않는다.

### 5-9. 대신 맞기 — 아군이 받는 피해를 내가

"수호: 이번 턴 아군이 받는 피해를 내가 대신 받는다." 피해 행동 직전 이벤트의 대상을 바꾼다. 행동 직전 이벤트에 맞는 쪽의 팀을 실어 보낸다.

```csharp
[Serializable, Intent("피해")]
public class DamageIntent
{
    public string sourceId;
    public string targetId;
    public string targetTeam;         // 더한 칸: 행동 직전 이벤트 조건 "아군이 맞을 때"
    public int amount;
    public bool cancelled;
}

// 5-3의 Rules.Damage에서 행동 직전 이벤트 다음 줄을 바꾼다
var hit = game.Intercept(new DamageIntent { sourceId = source?.id, targetId = target.id, targetTeam = target.team, amount = amount });
if (hit.cancelled || hit.amount <= 0) return;
if (hit.targetId != target.id) target = game.Find<Unit>(hit.targetId) ?? target;   // 대신 맞는 쪽
target.hp -= hit.amount;
```

**에디터**(DB › 상태 효과 `cover`): 지속 `이벤트 횟수` 1(셀 이벤트 턴 끝). `trigger` = `행동 직전 이벤트 받기` → `피해`, 행동 직전 이벤트 조건 `targetTeam` `일치` `ally`, **순서 `-10`**. `outcomes` = `행동 직전 이벤트 고치기` `targetId` `값으로 바꾸기` `{user}`. 액티브 효과 `guardian`: `사용자 자신` → `상태 효과 걸기`(`cover`).

- 순서를 작게 두어 다른 행동 직전 이벤트보다 먼저 돌게 한다. 그러면 뒤의 행동 직전 이벤트(가시 갑옷의 `targetId 일치 hero`, 29장 5-3 보호막의 가진 쪽 필드 `targetId`)가 **바뀐 대상**을 기준으로 본다. 대신 맞는 기사에게 보호막이 있으면 그 피해가 반감된다.
- 기사 자신이 맞는 피해는 `targetId`가 이미 기사라 바뀌지 않는다.
- "옆 칸의 아군만", "체력이 절반 이하인 아군만"처럼 거르기가 계산이면 결과를 `[BindTo]`로 쓴다(4-10): `i.targetId = GameApi.IdOf(c.User);`.

### 5-10. 중첩만큼 받는 피해 감소(값 틀 `{stacks}`)

"철피: 중첩마다 받는 피해 −1(쓸 때마다 1중첩)."

**에디터**(DB › 상태 효과 `ironSkin`): `maxStacks` `0`(제한 없음), `reapply` `중첩 더하기`, 지속 `영구(풀 때까지)`. `trigger` = `행동 직전 이벤트 받기` → `피해`, `holderField` `targetId`. `outcomes` = `행동 직전 이벤트 고치기` `amount` `더하기` `-{stacks}`. 액티브 효과 `harden`: `사용자 자신` → `상태 효과 걸기`(`ironSkin`, 중첩 1).

| 중첩 | 피해 10이 들어올 때 |
|---|---|
| 1 | 9 |
| 3 | 7 |
| 12 | −2 → 들어오지 않는다(규칙 함수의 `hit.amount <= 0`) |

- `{stacks}`는 가진 쪽에 걸린 **이 상태 효과**의 중첩이다(결과를 실행하는 순간의 값). 상태 효과가 아닌 곳(패시브 효과·프로젝트 기능)에서는 0이다.
- 중첩당 감소량을 개발자가 고치게 하려면 감소량을 밸런싱 값으로 두고 결과를 코드로 쓴다: `i.amount -= (int)(c.Game.Balance(GameIds.Balance.IronSkinPerStack) * c.Game.StatusStacks(c.User, c.Owner));`(값 틀은 곱한 값을 만들지 못한다).
- 고정 비율이면 틀 하나로 된다: `amount` `곱하기` `{balance:shieldRatio}`.
- 패시브 효과(유물·특성)라면 같은 자리에 `-{level}`을 쓴다. 레벨이 2인 껍질은 피해를 2 줄인다(17장 0-3).

### 5-11. 빚 — 신용카드: 골드가 −20까지 내려간다(`floor`)

"신용카드: 골드가 모자라도 −20까지 빚을 지고 산다." 상점이 `Game.Pay("profile", "gold", 가격, what: "gold.name")`(또는 결과 `비용 내기`)로 낸다고 본다.

**에디터**(DB › 패시브 효과 `creditCard`, 가진 목록 `party › relics`):

1. `trigger`: `행동 직전 이벤트 받기` → 행동 직전 이벤트 `비용 지불`, 행동 직전 이벤트 조건 `key` `일치` `profile` AND `path` `일치` `gold`.
2. `outcomes`: `행동 직전 이벤트 고치기` → 필드 `floor`, 방법 `값으로 바꾸기`, 값 `-20`.

| 골드 | 가격 | 결과 |
|---|---|---|
| 5 | 8 | 낸다 → −3 |
| −3 | 10 | 낸다 → −13 |
| −13 | 10 | 실패(−23은 −20보다 작다). 아무것도 바뀌지 않는다 |
| 5 | 25 | 낸다 → −20(하한과 같으면 된다) |
| 5 | 26 | 실패(−21) |

- 하한은 "낸 뒤 남는 값"과 비교한다. 카드가 없으면 하한 0이라 골드가 가격보다 적을 때 "모자랍니다"로 실패한다.
- 행동 직전 이벤트 조건이 `profile › gold`만 고르므로 에너지 같은 다른 비용에는 빚이 생기지 않는다.
- 버튼 미리보기: `Game.CanPay`는 하한을 모른다. 화면은 사는 행동을 `Game.CanDo(행동, out 이유)`로 본다(2-4).
- "골드 대신 체력으로 낸다"는 같은 자리에서 `key`·`path`를 값으로 바꾼다(낼 곳이 바뀐다).

### 5-12. 이 유닛의 수치 2배 — 효과 배율(Lumin)

"Lumin: 이것이 걸린 유닛의 효과 수치가 2배." 액티브 효과·패시브 효과의 수치를 하나하나 고치지 않고 행동 직전 이벤트 `효과 배율` 하나로 키운다.

**에디터**(DB › 상태 효과 `lumin`, 29장 3-1): `maxStacks` `1`, `duration` › `kind` `영구(풀 때까지)`. `trigger`(걸려 있는 동안 트리거) `행동 직전 이벤트 받기` → 행동 직전 이벤트 `효과 배율`. `holderField`(가진 쪽 필드) `userId`. `outcomes`(결과) `행동 직전 이벤트 고치기` → `path`(필드) `multiplier`, `op`(방법) `곱하기`, `value`(값) `2`.

| 유닛이 쓴 액티브 효과의 효과 | lumin 없음 | lumin 걸림 |
|---|---|---|
| `상태 효과 걸기`(`snow` 2중첩) | 2중첩 | 4중첩 |
| `필드 바꾸기`(`attack` 더하기 3) | +3 | +6 |
| 프로젝트 효과 Block(`Use.Context.Power`를 곱하는 피해 5) | 5 | 10 |

- `holderField`(가진 쪽 필드) `userId`가 "이것이 걸린 유닛이 쓰거나 발동할 때만"을 만든다. 비우면 누가 쓰든 2배다.
- 그 유닛의 패시브 효과(놓인 것마다, 가진 쪽 = 그 유닛)가 발동할 때도 같은 행동 직전 이벤트를 거치므로 함께 2배다.
- "맨 오른쪽일 때만 2배"(Critical)는 트리거에 `AND` `자리 검사`(`who` 사용자, 그 목록, `spot` 목록 맨 끝(차 있는 것 중))를 붙인다. 쓰는 순간 그 목록에 있어야 참이다(37장 3-2).
- 둘이 함께 걸리면 곱이 쌓인다(2 × 2 = 4배).
- 프로젝트 효과 Block이 `Power`를 곱하지 않으면 그 Block의 수치는 그대로다(4-14).

---

## 6. 주의할 점 · 자주 하는 실수

| 증상 | 원인과 해결 |
|---|---|
| 장비를 꼈는데 공격력 표시가 그대로 | 기본값 필드(`hero.attack`)를 읽었다. 최종값은 `Game.Attribute(hero, nameof(Unit.attack))`. |
| `Game.Attribute`가 0 | 필드 이름 오타이거나 id로 대상을 찾지 못했다. 이때 읽기는 예외 없이 기본값을 0으로 본다. 필드 이름은 `nameof`로 쓴다. |
| 스탯 보정이 든 액티브 효과를 썼더니 행동이 실패("…숫자 필드 '…'가 없어…") | 에디터 칸 `attribute`의 오타, 또는 그 대상 형식에 숫자 필드가 없다. 칸이 글자라 DB 검증이 잡지 못한다. |
| 장비 하나를 벗었는데 같은 장비 둘의 보정이 다 빠짐 | 장비 보정을 `Game.AddModifier`(또는 효과 `스탯 보정`)로 같은 출처에 붙였다. 장비 효과는 패시브 효과의 가진 동안 스탯 보정으로 만든다 — 같은 장비 둘은 가진 목록의 요소 둘이고, `perLevel`(레벨마다)을 켜면 하나를 벗을 때 그 몫만 빠진다(5-1, 17장 0-1). || 퍼센트 30을 적었더니 31배 | 퍼센트·독립 곱 값은 비율이다. +30%는 `0.3`. |
| 강화가 곱으로 불어남 | 독립 곱(`Multiply`)을 흔하게 썼다. 기본은 퍼센트(같은 칸 합). 독립 곱은 드문 강한 효과에만. |
| 피해 증폭 둘이 곱해짐 | 증폭 스탯(기본 0)에 퍼센트를 썼다. 증폭은 플랫(0.1 = 10%)으로 쌓아야 합이 된다(5-3). |
| 영구 강화가 다음 판에 안 보임 | 영구 Modifier는 대상 id로 붙는다. 판마다 id가 바뀌는 개체(`Game.NewId`로 만든 적 등)에 붙였다. 판마다 같은 id(`"hero"`)에 붙인다. |
| 이벤트를 받아 피해를 줄였는데 체력이 이미 깎여 있음 | 이벤트는 일어난 뒤에 온다. 줄이거나 막는 효과는 행동 직전 이벤트로. |
| `+ 블럭`에 `행동 직전 이벤트 받기`가 없음 | 그 칸의 `[TriggerSlot]`에 `TriggerKinds.Intercept`가 없다. 도전과제·액티브 효과·로그 칸에는 없다. 걸려 있는 동안만 행동 직전 이벤트를 받으려면 상태 효과(29장)로, 가지고 있는 동안 행동 직전 이벤트를 받으려면 DB › 패시브 효과(17장 0절)로, 그 밖의 늘 켜진 규칙은 프로젝트 기능의 잠금에 더하거나(3-6) 코드로 직접 등록한다(4-11). |
| 행동 직전 이벤트 목록이 비어 있음 | 행동 직전 이벤트 클래스에 `[Intent]`가 없다. |
| 행동 직전 이벤트가 걸리지 않음 | 규칙 함수가 `Game.Intercept`를 부르지 않거나, 다른 형식(상속한 형식)을 통과시켰다. 형식이 정확히 같아야 한다. 규칙 함수를 거치지 않고 `hp`를 직접 깎는 곳이 있으면 그곳은 행동 직전 이벤트를 건너뛴다. |
| 행동 직전 이벤트 고치기가 아무 일도 하지 않음 | 규칙 함수가 돌려받은 행동 직전 이벤트가 아니라 원래 인자를 썼다(`var hit = Game.Intercept(…)` 뒤 `hit.amount`). 또는 행동 직전 이벤트가 아닌 Trigger의 결과에 넣었다(Journal 경고 "고칠 행동 직전 이벤트가 없습니다"). |
| 취소가 안 됨("cancelled 필드가 없어 막을 수 없습니다") | 행동 직전 이벤트 클래스에 `public bool cancelled;`가 없다. |
| 순서에 따라 결과가 바뀜 | 같은 행동 직전 이벤트에 여럿이 걸리면 순서(`order`)가 작은 것부터다. 같은 순서면 등록(DB) 순서에 기대게 되므로, 결과가 달라지는 조합에는 서로 다른 순서를 준다(5-4). |
| 막힌 시전에도 충전이 빠짐 | 행동 직전 이벤트 밖의 값을 바꾸는 행동 직전 이벤트가 막는 행동 직전 이벤트보다 먼저 돌았다. 그런 행동 직전 이벤트는 순서를 크게 둔다(5-7). 체력·골드 같은 게임 값은 행동 직전 이벤트를 받는 쪽에서 바꾸지 말고 행동 직전 이벤트에 실어 규칙 함수가 바꾼다(5-5). |
| "행동 직전 이벤트 사슬이 16단계를 넘어 끊었습니다" | 행동 직전 이벤트 결과가 같은 행동 직전 이벤트를 다시 만든다(행동 직전 이벤트 안에서 `Rules.Damage`를 부르는 등). 끊긴 뒤의 행동 직전 이벤트는 적용되지 않는다. |
| 버튼은 켜져 있는데 누르면 실패 | `Game.CanUse`는 결과(비용 내기)를 모른다. 사용 조건에 `플레이어 데이터 검사`(≥)를 두거나, 그 행동을 `Game.CanDo`로 해 본다(3-5). |
| 비용 0·빚 허용인데 `CanPay`가 거짓 | `CanPay`는 행동 직전 이벤트 전 값(하한 0)이다. 화면은 `Game.CanDo(행동, out 이유)`로 본다(2-4). 화면 코드에 비용 0·하한 계산을 따로 쓰지 않는다. |
| 규칙 테스트에서만 직접 등록한 규칙이 걸리지 않음 | 등록은 등록 함수를 부른 세션에만 있다. 테스트가 세션을 만든 뒤 같은 등록 함수를 부른다(4-11). |
| 사용·비용·상태 효과 걸기·확률·수치 배율 행동 직전 이벤트를 받으려고 프로젝트 행동 직전 이벤트를 만듦 | GBible이 이미 행동 직전 이벤트 자리를 둔 일이다(기본 행동 직전 이벤트 `사용`·`비용 지불`·`상태 효과 걸기`·`확률`·`효과 배율`, 4-14). 기본 행동 직전 이벤트에 없는 것(다른 액티브 효과로 바꾸기 등)이 필요할 때만 프로젝트 행동 직전 이벤트를 둔다(5-7). |
| 효과 배율을 2배로 했는데 어떤 수치는 그대로 | 그 수치를 가진 프로젝트 Block이 `Use.Context.Power`(결과·`[BindTo]`는 `c.Power`)를 곱하지 않는다. 또는 상태 효과의 결과·패시브 효과의 가진 동안 보정이라 배율이 없다(4-14). |
| "행성 카드는 공짜"를 걸었는데 행성 카드도 돈을 냄 | `Game.Pay`를 부를 때 `forId`를 넘기지 않았다(빈 글자). 사는 카드의 ID를 `forId`로 넘긴다. 결과 `비용 내기`는 이 결과를 가진 항목 ID를 넘긴다(4-12). |
| 틱마다 도는 행동이 MP 부족 한 번으로 통째로 취소됨 | `Game.Pay`는 실패를 던진다. 되는 것만 할 때는 `Game.TryPay`(2-4). |
| `Game.Pay`가 실패했는데 앞의 변경이 남음 | 행동 밖에서 불렀다. 취소는 행동 단위다. 행동 안에서 부른다. |
| 얻는 것을 비용 내기 음수로 | 검증 오류다. 얻는 것은 `플레이어 데이터에 주기`. |
| 도발·대신 맞기를 걸었는데 원래 대상이 맞음 | 규칙 함수가 고쳐진 `targetId`로 대상을 다시 찾지 않고 원래 인자를 썼다(5-8, 5-9). |
| 대신 맞았는데 가시 갑옷·보호막이 원래 대상을 기준으로 적용됨 | 대신 맞기가 그것들보다 뒤에 돌았다. 대신 맞기의 순서(`order`)를 작게 둔다(5-9). |
| 값 `-{stacks}`가 늘 0 | 상태 효과의 결과가 아니다(패시브 효과·프로젝트 기능). `{stacks}`는 가진 쪽에 걸린 그 상태 효과의 중첩이다. 패시브 효과 레벨은 `{level}`(3-4). |
| 값 `-{level}`이 늘 0 | 패시브 효과의 결과가 아니다(상태 효과·액티브 효과). `{level}`은 이 결과를 가진 패시브 효과의 레벨이다. 상태 효과의 중첩은 `{stacks}`. |
| `{user}`로 대상을 바꿨더니 아무도 맞지 않음 | 가진 쪽에 id가 없어(`party › relics`의 `party`) `{user}`가 빈 글자가 되었다. 가진 목록을 id가 있는 개체 안에 두거나 값을 직접 적는다. |
| 패시브 효과 유물을 잃었는데 보정을 떼는 코드를 찾음 | 패시브 효과의 `modifiers`(가진 동안 스탯 보정)는 읽을 때 계산한다(2-2). 가진 목록에서 빼면 빠진다. `RemoveModifiers`로는 떼지지 않는다. |
| `$modifiers`에 패시브 효과 보정이 안 보임 | 저장하지 않는 보정이다. `Game.Modifiers(대상)`에는 `source` = 패시브 효과 ID로 보인다. |
| 빚 하한을 바꿨는데 다른 비용까지 빚이 됨 | `행동 직전 이벤트 받기`(비용 지불)의 행동 직전 이벤트 조건(`key`·`path`)을 비웠다. 낼 곳을 고른다(5-11). |
| 행동 직전 이벤트 `사용`의 `ability`를 바꿨는데 원래 액티브 효과가 나감 | `ability`·`userId`는 거르기용이다. 다른 액티브 효과로 바꾸려면 프로젝트 행동 직전 이벤트를 둔다(5-7). |
| 경로 `stock[gold]`로 냈는데 골드가 있는데도 "모자랍니다" | 그 키가 없다(없는 키는 0, 2-4). 키 이름이 대소문자까지 같은지 본다. |
| 비용 경로에 "…[]가 있어 낼 곳이 하나로 정해지지 않습니다" (예외·검증 오류) | `units[].mana`처럼 요소마다 펼치는 경로다. 낼 곳은 숫자 하나다. 그 요소를 id로 고른다(`units[hero].mana`, 2-4). |
| 물리 충돌을 규칙에서 판정하려다 막힘 | 판정은 화면이 하고 결과만 행동으로 보낸다(판정 보고 행동, 1절). 위치·속도는 판 데이터에 넣지 않는다. |
