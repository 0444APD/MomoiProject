# 29. 상태 효과·예약·선택 대기

**한 줄 요약**: **상태 효과**는 대상에게 걸려 있는 동안 효과가 나는 버프·디버프다(독·분노·보호막·"3턴 뒤 폭발"). DB › 상태 효과에서 중첩·지속(영구 / 이벤트 횟수 / 게임 시간)·능력치 보정·트리거와 결과를 정하고, 효과 Block `상태 효과 걸기` 또는 `Game.ApplyStatus`로 건다. **예약**은 행동 하나를 나중에(게임 시간 N초 뒤, 또는 이벤트를 N번 받은 순간) 실행한다(코드). **선택 대기**는 행동 안에서 플레이어에게 고르기를 요구하고(`Game.Ask`·결과 `선택 요구`), 답이 오면 이어서 할 행동을 실행한다. 셋 다 플레이어 데이터에 들어 있어 Undo·판 저장·멀티플레이가 그대로 따라온다.

**언제 쓰는가**

- "턴이 끝날 때마다 독 피해, 3턴", "공격력 +10%, 최대 3중첩"처럼 **몇 턴·몇 초 동안** 이어지는 효과(상태 효과).
- "받는 피해 반감, 3턴"처럼 **걸려 있는 동안만 켜지는 가로채기**(가로채기 트리거를 가진 상태 효과).
- "3턴 뒤 폭발"처럼 **늦게 터지는 효과**(상태 효과의 끝날 때 결과. 코드라면 예약).
- "카드 1장을 골라 버린다", "보상 3개 중 1개"처럼 **플레이어가 고르는 효과**(선택 대기).

이 문서에서 쓰는 용어:

| 용어 | 뜻 |
|---|---|
| 상태 효과 (Status) | DB › 상태 효과의 항목 하나. 중첩·지속·능력치 보정·트리거·결과를 정한 **정의**다. 코드 형식은 `StatusData`(DB), `StatusDefinition`(실행 중의 정의). |
| 걸린 것 (`ActiveStatus`) | 상태 효과가 대상 하나에 걸린 기록 한 줄: 정의 ID · 가진 쪽 id · 중첩 · 남은 지속 · 출처. 플레이어 데이터 `$statuses`에 있다. |
| 가진 쪽 (holder) | 상태 효과가 걸린 대상. `id`가 있는 플레이어 데이터 객체(`Fighter`·`DeckCard` …)다. |
| 중첩 (stacks) | 같은 상태 효과가 몇 겹 걸렸는지. 능력치 보정은 중첩 × 값으로 붙고, 결과는 중첩 수를 읽을 수 있다. |
| 지속 (duration) | 언제 끝나는지: 영구(풀 때까지) · 이벤트 횟수(셀 이벤트를 N번 받으면) · 게임 시간(N초 뒤). |
| 셀 이벤트 | 지속이 이벤트 횟수일 때 받을 때마다 남은 횟수를 하나 줄이는 이벤트(예: `턴 끝`). |
| 가진 쪽 필드 | 이벤트·의도의 필드 하나. 그 값이 가진 쪽 id일 때만 발동하거나 센다(예: 피해 의도의 `targetId` → "내가 맞을 때만"). |
| 예약 (Schedule) | 행동 하나를 나중에 실행하도록 맡겨 둔 기록. 플레이어 데이터 `$schedule`에 있다. |
| 선택 대기 (Choice) | 플레이어의 답을 기다리는 고르기 요구 하나(`PendingChoice`). 플레이어 데이터 `$choices`에 있다. |
| 이어서 할 행동 (then) | 고르기를 요구할 때 함께 맡기는 행동. 답이 오면 고른 것을 받아 실행된다. |
| `[Chosen]` | 이어서 할 행동의 필드에 붙이는 어트리뷰트. 고른 것이 그 필드에 들어간다. |
| 컨텍스트 (`TriggerContext`) | 결과가 도는 순간의 정보. 상태 효과의 결과에서는 `User`가 가진 쪽, `Owner`가 상태 효과 ID다. |

> **상태 효과와 키워드** — 화상·빙결 같은 상태는 키워드 목록(05장)에 글자로 두지 않고 이 장의 상태 효과(DB › 상태 효과, ID 상수는 `GameIds.Status`)로 만든다. 걸린 것은 대상의 필드가 아니라 플레이어 데이터 `$statuses`에 있다(2-4). 그래야 지속·중첩·턴마다의 효과·만료가 붙는다.

---

## 1. 무엇으로 만드나

28장 1절 **효과의 문법**에서 이 장이 맡는 칸이다.

| 부품 | 종류 | 이 장에서 |
|---|---|---|
| 언제 | 지연("3턴 뒤") | 상태 효과의 끝날 때 결과(DB) · 예약(코드) |
| 언제 | 지속("3턴 동안") | 상태 효과(능력치 보정 · 걸려 있는 동안 트리거) |
| 무엇을 | 선택 요구("카드 1장을 고른다") | 선택 대기(`Game.Ask` · 결과 `선택 요구`) |

쪼개 보기:

| 기획 문장 | 만드는 것 |
|---|---|
| "독: 턴이 끝날 때마다 중첩만큼 피해, 3턴" | 상태 효과: 지속 이벤트 횟수 3(턴 끝) + 트리거 `턴 끝 받기` + 결과(5-1) |
| "분노: 중첩당 공격력 +10%, 최대 3" | 상태 효과: 최대 중첩 3 + 능력치 보정(5-2) |
| "보호막: 내가 받는 피해 반감, 3턴" | 상태 효과: 가로채기 트리거 + 가진 쪽 필드 `targetId`(5-3) |
| "3턴 뒤 모든 적에게 20 피해" | 상태 효과: 지속 3 + 끝날 때 결과(5-4) |
| "이번 턴 비용 0" | 상태 효과: 가로채기(비용 지불) + 의도 고치기 `막기`, 지속 턴 끝 1회(5-5) |
| "다음 턴 시작에 카드 2장 더" | 예약 `Game.ScheduleOn<TurnStarted>`(5-6) |
| "카드 1장을 골라 버린다" | 선택 대기 `Game.Ask` + `[Chosen]`(5-7) |
| "손패에서 1장 골라 이번 턴 비용 0" | 결과 `선택 요구` + 효과 `상태 효과 걸기`(5-9) |

> **규칙: 기획자가 DB에서 만드는 지연·지속 효과는 상태 효과로, 프로그래머가 코드에서 행동 하나를 나중에 돌릴 때는 예약으로.** 상태 효과는 걸린 것이 모니터에 보이고, 풀 수 있고, 중첩·갱신 규칙이 있다. 예약은 "이 행동을 그때 실행"만 한다.

> **규칙: 행동은 멈추지 않는다.** 행동 안에서 플레이어의 답을 기다리지 않는다(코루틴·`await` 없음). `Game.Ask`는 요구만 남기고 바로 돌아온다. 고른 뒤에 할 일은 모두 **이어서 할 행동**에 쓴다.

---

## 2. 동작 규칙

### 2-1. 걸기 · 다시 걸기 · 풀기

- 처음 걸면 걸린 것 한 줄이 생긴다(id `$status1`, `$status2` …). 중첩은 건 중첩과 최대 중첩 중 작은 값, 지속은 처음 길이로 시작한다. 능력치 보정이 붙고 **걸릴 때 결과**가 한 번 돈다.
- 같은 대상에 같은 상태 효과를 다시 걸면 새 줄이 생기지 않고 **다시 걸 때** 규칙을 따른다. 걸릴 때 결과는 다시 돌지 않는다.

| 다시 걸 때 (`StatusReapply`) | 중첩 | 남은 지속 |
|---|---|---|
| 중첩 더하기 (`AddStacks`) | 더한다(최대 중첩에서 멈춘다) | 그대로 |
| 지속 갱신 (`Refresh`) | 그대로 | 처음 길이로 |
| 중첩 더하고 지속 갱신 (`AddStacksAndRefresh`, DB 기본) | 더한다 | 처음 길이로 |
| 무시 (`Ignore`) | 그대로 | 그대로 |

- **풀기**(효과 `상태 효과 풀기`·`Game.RemoveStatus`): 중첩을 주면 그만큼 줄이고, 남는 중첩이 없으면 푼다. 0(기본)이면 통째로 푼다.
- 풀어서 없애면 **끝날 때 결과는 돌지 않는다.** 끝날 때 결과는 지속이 다 돼서 끝날 때만 돈다("해체하면 터지지 않는다").
- 대상은 `id`가 있는 플레이어 데이터 객체 또는 그 id 글자다. 카드 한 장(`DeckCard`)처럼 싸우는 존재가 아닌 객체에도 걸 수 있다(5-9).

### 2-2. 지속

| 방식 (`StatusDuration`) | 길이 | 끝나는 때 |
|---|---|---|
| 영구(풀 때까지) (`Permanent`, 기본) | 쓰지 않는다 | 풀 때만. 끝날 때 결과는 돌지 않는다 |
| 이벤트 횟수 (`Events`) | 횟수(반올림, 1 이상) | 셀 이벤트를 그 횟수만큼 받은 순간. 그 이벤트를 쏜 행동 안에서 끝난다 |
| 게임 시간(초) (`Seconds`) | 초 | 게임 시계가 끝 시각을 지난 프레임 끝. 행동 하나("상태 효과 만료")로 끝난다 |

- **결과가 먼저다.** 같은 이벤트가 걸려 있는 동안 트리거와 횟수 줄이기를 둘 다 하면, 결과가 먼저 돌고 횟수가 준다. "턴 끝마다 피해 1, 3턴" = 피해 세 번 뒤에 끝난다.
- 지속의 **가진 쪽 필드**를 고르면, 셀 이벤트의 그 필드가 가진 쪽 id일 때만 센다(예: 턴 끝 이벤트에 플레이어 id가 있으면 "내 턴이 끝날 때만"). 비우면 그 이벤트를 받을 때마다 센다.
- 셀 이벤트는 형식이 정확히 같아야 한다(상속한 형식은 따로다).
- 시간 만료도 행동이라 Undo된다(스냅샷 방식이면 되감기로 돌아온다).
- 끝날 때 결과는 가진 쪽이 아직 데이터에 있을 때만 돈다. 가진 쪽이 먼저 사라졌으면(쓰러져 전투에서 빠진 적) 그 행동이 끝날 때 결과 없이 정리된다(2-3).

### 2-3. 걸려 있는 동안

**능력치 보정** — 줄마다 `중첩 × 중첩당 값`인 Modifier 하나를 가진 쪽에 붙인다(28장 2-1의 공식). 출처는 걸린 것의 id(`$status1`)라서, 같은 상태 효과가 여러 대상에 걸려도 따로 붙고 따로 떨어진다. 중첩이 바뀌면 다시 계산하고, 풀리거나 끝나면 뗀다.

**트리거와 결과**

- 상태 효과마다 트리거를 한 번 등록해 두고, 발동하면 그 상태 효과가 걸린 대상마다 결과를 돌린다.
- 결과의 컨텍스트: `User` = 가진 쪽 객체, `Picked` = 가진 쪽 하나, `Owner` = 상태 효과 ID, `Event` = 받은 이벤트(가로채기면 의도), `IsIntercept`. 결과 안에서 중첩 수는 `Game.StatusStacks(c.User, c.Owner)`다.
- **가진 쪽 필드**를 고르면, 이벤트·의도의 그 필드가 가진 쪽 id인 대상의 결과만 돈다. 비우면 걸린 대상 모두의 결과가 돈다.
- 결과 `대상에게 효과`의 대상 Block을 비우면 고른 대상 = 가진 쪽이다. `사용자 자신`도 가진 쪽이다.
- 트리거가 가로채기면 **걸려 있는 동안만 켜지는 가로채기**가 된다(28장 2-3). 한 대상의 결과가 의도를 막으면(`cancelled`) 남은 대상은 건너뛴다.
- 결과가 돌 때마다 Journal에 "결과: …"(출처 `Status`)가 남는다.
- 트리거가 조건 충족만으로 되어 있으면, 값이 바뀐 행동이 끝난 뒤의 평가에서 결과가 돈다(행동 밖 변경). 판 값을 바꾸는 상태 효과는 이벤트 받기·가로채기로 발동시킨다.

**한계** — 트리거 안의 조건(조건 충족 잎·이벤트 조건)은 가진 쪽마다 따로 검사하지 않는다. 한 번 검사하고, 가진 쪽은 가진 쪽 필드로만 거른다. 가진 쪽의 값에 따라 달라지는 조건("체력이 절반 이하인 가진 쪽만")은 `[BindTo]` 코드에서 `c.User`를 보고 판단한다(4-3).

**가진 쪽이 사라지면** — 가진 쪽을 플레이어 데이터에서 뺀 행동(쓰러진 적을 전투에서 빼는 피해)이 끝나기 직전에, **그 행동 안에서** 그 대상에 걸린 상태 효과를 모두 지운다. 끝날 때 결과는 돌지 않는다. 그 대상의 이번 판 Modifier(상태 효과의 능력치 보정 포함, 28장 2-2)도 함께 지운다. 그 행동을 Undo하면 대상과 함께 돌아온다. 영구 Modifier는 지우지 않는다(대상이 다음 판에 다시 생긴다).

### 2-4. 걸린 것이 사는 곳

| 무엇 | 플레이어 데이터 키 | 구역 |
|---|---|---|
| 걸린 상태 효과 | `$statuses`(표시 이름 "상태 효과") | 세션 |
| 상태 효과의 능력치 보정 | `$modifiers`(출처 `$status1` …, 28장 2-2) | 세션 |

- 처음 걸 때 키가 저절로 정의된다. 플레이어 데이터라서 Undo·판 저장·멀티플레이가 그대로다. 판을 불러온 직후에도 보정이 바로 보인다.
- 세션 구역이라 **새 판이면 비워진다.** 전투가 끝나는 것은 시스템이 모르므로 전투가 끝나도 남는다. 전투마다 끝나야 하면 전투를 끝내는 행동에서 푼다(5-2).
- 걸린 것에는 정의 ID·가진 쪽 id·중첩·남은 지속·출처만 들어 있다. 무엇을 하는지는 정의에서 읽는다. 정의는 게임이 켜질 때 DB의 상태 효과 전부로 등록된다.

### 2-5. 예약

- `Game.ScheduleAfter(초, 행동)`: 게임 시간으로 그만큼 지난 뒤의 **프레임 끝**에 행동 하나("예약 실행", 공개 범위 개발자)로 실행한다. 같은 프레임에 여럿이면 시각 순서다.
- `Game.ScheduleOn<이벤트>(횟수, 행동)`: 그 이벤트를 그 횟수만큼 받은 **순간에, 그 이벤트를 쏜 행동 안에서** 실행한다. 그 행동과 함께 Undo된다. 이벤트 형식은 정확히 같아야 한다.
- **넘긴 행동은 예약에 맡긴다.** 대상 필드(id가 있는 객체)는 id만 남겨 저장하고, 실행 직전에 지금 데이터의 객체로 다시 찾는다(02장 2.2와 같다). 넘긴 행동 객체는 다시 쓰지 않는다.
- **대상이 그때 없으면**(쓰러진 적) 실행하지 않고 지운다. 타임라인에 "대상이 없어 예약을 실행하지 않았습니다: (행동)"이 남는다.
- **실패하면**
  - 시간 예약: 타임라인에 경고 "예약 실행이 실패해 지웁니다: (행동) — (이유)"를 남기고 지운다(매 프레임 다시 시도하지 않는다).
  - 이벤트 예약: 이벤트를 받는 쪽의 오류처럼 타임라인에 오류로 남고, 이벤트를 쏜 행동은 계속된다. 예약된 행동이 실패 전까지 바꾼 것은 남는다. 그래서 예약할 행동은 실패할 일(모자란 비용 등)을 먼저 확인하고 고친다.
- **저장**: `$schedule`(세션, "예약"), `permanent: true`면 `$schedule.meta`(메타, "영구 예약"). 판 저장·불러오기로 이어진다.
- **시각은 게임 시계**(`session.Clock.Time`) 기준이다. 게임 시계는 게임을 켤 때 0에서 시작하고, 판을 불러오면 그 판의 시각으로 돌아간다. 그래서 판을 넘는 시각은 정해지지 않는다. **영구 예약은 이벤트 예약(`ScheduleOn`)만** 쓸 수 있고, 시간 예약에 `permanent: true`를 주면 `InvalidOperationException`("[MomoiProject] 영구 예약은 이벤트 예약(ScheduleOn)만 쓸 수 있습니다(게임 시간은 판마다 0부터 다시 흐릅니다).")으로 그 행동이 취소된다.
- 고정 틱(33장)을 켜면 시간 예약은 틱마다 확인된다(프레임 속도와 상관없이 같은 틱에 실행).

### 2-6. 선택 대기

```
행동(카드 내기)
  └ Game.Ask(안내 글, 후보, 이어서 할 행동)
       선택을 플레이어 데이터($choices)에 넣고 ChoiceRequested를 쏜다. 다음 줄은 바로 실행된다
  행동이 끝난다                                         ─ Undo 한 단위
화면: Game.Choice를 그린다. 기다리는 동안(Game.IsWaiting) 다른 입력을 막는다
플레이어가 고른다 → Game.Answer(선택 id, 고른 것들)
  └ 행동 '선택: …'(ChoiceAnswer)
       개수·후보 검사 → 이어서 할 행동의 [Chosen] 필드에 고른 것 → 그 행동 실행   ─ Undo 한 단위
```

- **행동은 멈추지 않는다.** `Ask` 다음 줄은 고르기 전에 바로 실행된다.
- **이어서 할 행동은 예약처럼 맡긴다.** 대상 필드는 id만 저장하고 실행할 때 다시 찾는다. 고른 것은 `[Chosen]` 필드에 들어간다.
- **답이 틀리면 아무것도 바뀌지 않는다.** 개수가 최소~최대 밖이거나, 후보가 아닌 것을 골랐거나, 같은 것을 두 번 고르면 답하기가 실패하고 선택은 그대로 기다린다. 이어서 할 행동이 실패해도 같다.
- **후보가 없으면 기다리지 않는다.** 바로 이어서 할 행동을 실행한다(고른 것 없음).
- **개수**: 최소는 후보 수까지로 줄어든다. 최대는 후보 수를 넘지 않고, 0 이하면 후보 수다. 최소가 0이면 고르지 않고 답할 수 있다.
- 후보는 id로 구별한다. 같은 id가 두 번 들어오면 하나만 남는다.
- **여러 선택은 먼저 요구한 것부터.** `Game.Choice`가 맨 앞(가장 먼저 요구한 것)이다.
- **Undo**: 답하기를 Undo하면 다시 기다린다. 요구한 행동까지 Undo하면 선택도 사라진다.
- **판 저장**으로 이어진다. 불러온 뒤 그대로 답할 수 있다.
- 기다리는 동안 다른 입력을 막는 것은 **화면의 일**이다(`Game.IsWaiting`). 시스템은 다른 행동을 막지 않는다.
- `player`(답할 플레이어, 멀티플레이용)는 기록만 한다. 그 플레이어만 답하게 하는 검사는 화면·서버가 한다.

### 2-7. 라이브러리 내부 id

Modifier `$mod1`, 걸린 상태 효과 `$status1`, 예약 `$schedule1`, 선택 `$choice1`처럼 라이브러리가 만드는 id는 `$`로 시작한다. 그래서 DB 항목 ID와 겹치지 않는다. 번호는 플레이어 데이터(`$id.$status` 같은 키, 메타 구역)에 있어 Undo·저장·멀티플레이에서도 같다. 모니터 데이터 탭에서 보이는 이름이 이것이다.

---

## 3. 에디터에서 쓰는 법

### 3-1. DB › 상태 효과

`MomoiProject → DB` → 사이드바 **DB** 그룹의 **상태 효과** 탭 → **+ 새로 만들기** → 제목(= ID, 예: `poison`)을 짓는다. 저장하면 `GameIds.Status.Poison`이 생긴다. 칸 이름표는 변수 이름이고, 마우스를 올리면 `[FieldLabel]` 글자와 설명이 보인다.

| 칸 | 툴팁 | 값 |
|---|---|---|
| `maxStacks` | 최대 중첩 / 0이면 제한 없음 | 기본 1 |
| `reapply` | 다시 걸 때 | `중첩 더하기` / `지속 갱신` / `중첩 더하고 지속 갱신`(기본) / `무시` |
| `duration` › `kind` | 지속 › 방식 | `영구(풀 때까지)`(기본) / `이벤트 횟수` / `게임 시간(초)` |
| `duration` › `length` | 길이 / 이벤트 횟수 또는 초 | 기본 3. 이벤트 횟수·게임 시간일 때만 보인다 |
| `duration` › `tickEvent` | 셀 이벤트 | `[GameEvent]` 목록. 이벤트 횟수일 때만 보인다 |
| `duration` › `holderField` | 가진 쪽 필드 / 이 필드가 가진 쪽 id인 이벤트만 셉니다(예: 턴 끝의 플레이어 id → 내 턴이 끝날 때만). | 셀 이벤트의 필드. 비우면 "가진 쪽 모두". 이벤트 횟수일 때만 보인다 |
| `modifiers` | 능력치 보정 | 줄마다 `attribute`(능력치(숫자 필드 이름)) · `kind`(칸: `플랫(+값)` / `퍼센트(0.2 = +20%)` / `독립 곱(0.5 = ×1.5)`) · `valuePerStack`(중첩당 값) |
| `trigger` | 걸려 있는 동안 트리거 | `+ 블럭`: 이벤트 받기 · 조건 충족 · 가로채기 |
| `holderField` | 가진 쪽 필드 / 이벤트·의도의 이 필드가 가진 쪽 id일 때만 발동합니다(예: 피해 의도의 대상 id → 내가 맞을 때만). | 트리거가 고른 이벤트·의도의 필드. 비우면 "가진 쪽 모두" |
| `outcomes` | 결과 | 트리거가 발동할 때 가진 쪽마다. 결과 종류 전부 |
| `onApply` | 걸릴 때 결과 | 처음 걸 때 한 번. 결과 종류는 대상에게 효과(`선택 요구` 포함) · 플레이어 데이터(주기·`비용 내기` …) · 코딩한 결과 · 이벤트 쏘기 |
| `onExpire` | 끝날 때 결과 | 지속이 다 돼서 끝날 때만(풀 때는 돌지 않는다). 고를 수 있는 종류는 `onApply`와 같다 |

- 걸릴 때·끝날 때 결과의 컨텍스트도 `User` = 가진 쪽, `Owner` = 상태 효과 ID다. 이벤트는 없다(`Event`가 비어 있다).
- 능력치 이름은 글자라서 오타를 검증이 잡지 못한다. 걸 때 대상에 그 숫자 필드가 없으면 28장 3-1과 같은 예외로 그 행동이 취소된다.
- 검증 오류: "지속: 횟수를 셀 이벤트를 고르세요." / "지속: 횟수는 1 이상이어야 합니다." / "지속: 시간은 0보다 커야 합니다." / "능력치 보정: 능력치(숫자 필드 이름)를 적으세요." / "비어 있는 결과 칸이 있습니다(스크립트가 지워졌을 수 있음)."
- 검증 경고: "지속이 영구라 끝날 때 결과가 돌지 않습니다(풀어서 없앨 때는 돌지 않습니다)." / "걸려 있는 동안 트리거가 없어 결과가 돌지 않습니다." / "트리거가 있는데 결과가 없습니다." 트리거·결과 자신의 검증도 함께 뜬다.

> **`[BindTo]`와 세 결과 칸** — 상태 효과 ID에 `[BindTo]`를 붙이면 코드가 `outcomes`(걸려 있는 동안의 결과)를 대신한다(4-3). DB에서도 `outcomes` 칸만 잠기고(연결된 함수 이름 · `cs 열기`), `onApply`·`onExpire`는 그대로 고친다. 두 칸은 `[OutcomeSlot(…, BindTo = false)]`라서 코드가 대신하지 않는다(13장 2-11). 걸릴 때·끝날 때 결과를 코드로 쓰려면 그 칸에 코딩한 결과 Block(`Reward`, 12장 3-2)을 둔다.

### 3-2. 효과 `상태 효과 걸기` · `상태 효과 풀기`

결과 칸 `+ 결과 추가` → `대상에게 효과` → `+ 효과 추가` → 분류 **상태 효과**. 최종 대상마다 실행한다. `선택 요구`의 고른 것에 효과(3-3)에도 같은 목록이 나온다.

**상태 효과 걸기** — 문장 `{status} {stacks}중첩 걸기`.

| 칸 | 툴팁 | 값 |
|---|---|---|
| `status` | 상태 효과 | DB › 상태 효과 목록에서 고른다 |
| `stacks` | 중첩 | 1 이상(기본 1) |

- 이미 걸려 있으면 그 상태 효과의 `reapply` 규칙을 따른다(2-1).
- 출처(`ActiveStatus.source`)는 이 효과를 가진 항목의 ID다(컨텍스트의 `Owner`: 능력 ID, 사용 기능이 붙은 인스턴스 ID, 유물 ID, 상태 효과 ID …).
- 요약: `poison 걸기`, `poison 3중첩 걸기`.
- 검증 오류: "상태 효과 걸기: 상태 효과를 고르세요." / "상태 효과 걸기: 중첩은 1 이상이어야 합니다."

**상태 효과 풀기**

| 칸 | 툴팁 | 값 |
|---|---|---|
| `status` | 상태 효과 | DB › 상태 효과 목록에서 고른다 |
| `stacks` | 중첩(0이면 모두) | 0이면 통째로 푼다. 숫자를 적으면 그만큼 줄이고, 남는 중첩이 없으면 푼다 |

- 끝날 때 결과는 돌지 않는다.
- 요약: `poison 풀기`, `poison 2중첩 풀기`.
- 검증 오류: "상태 효과 풀기: 상태 효과를 고르세요."

### 3-3. 결과 `선택 요구`

`+ 결과 추가` → 그룹 **결과** → `선택 요구`. 결과 종류가 대상에게 효과와 같아서(`OutcomeKinds.Targeted`), `대상에게 효과`를 고를 수 있는 칸이면 나온다.

| 칸 | 툴팁 | 값 |
|---|---|---|
| `prompt` | 안내 글 | 플레이어에게 보일 글(예: "버릴 카드") |
| `candidates` | 후보 | `+ 대상 추가`로 대상 Block을 더한다. `대상에게 효과`처럼 고른 대상에서 출발해 위에서부터 차례로 거른다. 비우면 고른 대상 그대로 |
| `min` | 최소 개수 | 기본 1. 0이면 고르지 않고 답할 수 있다 |
| `max` | 최대 개수 | 기본 1 |
| `effects` | 고른 것에 효과 | `+ 효과 추가`. 고른 것마다 모든 효과를 실행한다(효과의 대상 형식과 다른 것은 건너뛴다) |
| `then` | 고른 뒤 결과 | `+ 결과 추가`. 효과 다음에 한 번 돈다. 안에 `선택 요구`를 또 넣으면 이어 고르기가 된다 |

- 후보 중 `id`가 있는 객체만 후보가 된다. 후보 이름은 텍스트 표의 `<id>.name`이고, 없으면 id다. DB 원본처럼 이름 키가 있는 id는 이름이 보이고, 판 안에서 만든 개체(덱 카드 `card3`)는 id가 보인다. 그런 후보는 화면이 id로 객체를 찾아 이름을 그린다(5-10).
- 사용자·`Owner`는 요구한 순간 그대로 이어진다. 고른 것에 효과의 `User<T>()`, 고른 뒤 결과의 `User`가 요구할 때의 사용자이고, 고른 뒤 결과의 `Picked`는 고른 것들이다. 받은 이벤트는 이어지지 않는다(`Event`가 비어 있다).
- 효과·결과는 요구한 순간의 **사본**이 선택과 함께 저장된다. 그 사이 DB를 고쳐도 기다리는 선택에는 영향이 없다.
- 후보가 없으면 기다리지 않고 바로 이어 간다. 효과는 없고, 고른 뒤 결과는 돈다.
- 유물·상태 효과처럼 사용이 아닌 곳에는 고른 대상이 없으므로, 후보를 찾는 대상 Block(`모든 적` 같은)을 둔다.
- 요약: `{후보}에서 {개수} 고르기: {효과}`. 예: `손패에서 1개 고르기: free 걸기`.
- 검증 오류: "선택 요구: 개수는 최소 ≤ 최대, 최대 1 이상이어야 합니다." / "선택 요구: 최소 개수는 0 이상이어야 합니다." / "선택 요구: 비어 있는 Block이 있습니다(스크립트가 지워졌을 수 있음)."
- 검증 경고: "선택 요구: 고른 뒤 할 일(효과·결과)이 없습니다."

### 3-4. 어느 칸에서 쓸 수 있나

| Block | 쓸 수 있는 칸 |
|---|---|
| 상태 효과 걸기 · 풀기 | `대상에게 효과`(또는 `선택 요구`의 고른 것에 효과)를 고를 수 있는 칸 전부(능력·사용 기능·도전과제·개발 명령·상태 효과·프로젝트 기능) |
| 선택 요구 | `[OutcomeSlot]`에 `OutcomeKinds.Targeted`가 있는 칸(= 대상에게 효과와 같은 칸) |
| 가로채기(상태 효과의 트리거) | DB › 상태 효과의 `trigger`에는 처음부터 열려 있다. 결과 칸에도 `의도 고치기`가 나온다 |

- 상태 효과 걸기·선택 요구는 **행동 안에서 도는 결과**에 둔다(사용 기능·능력, 이벤트 받기·가로채기로 발동한 유물·상태 효과). 도전과제의 결과는 Undo 이력 밖이라(14장) 판 값인 상태 효과·선택을 두기에 맞지 않다.

### 3-5. 모니터에서 확인하기

- **걸린 상태 효과**: **데이터** 탭에서 `$statuses`(상태 효과) → `items`를 펼치면 한 줄씩 보인다: `id` · `status`(정의 ID) · `target`(가진 쪽 id) · `stacks` · `remaining`(이벤트 지속이면 남은 횟수, 시간 지속이면 끝나는 게임 시각) · `source`. 능력치 보정은 `$modifiers`에 출처 `$status1`로 보인다.
- **결과**가 돌면 타임라인에 "결과: …"(출처 `Status`)가 남는다. 시간 지속의 만료는 행동 "상태 효과 만료"(개발자)로 남는다.
- **예약**: 데이터 탭 `$schedule`(예약)·`$schedule.meta`(영구 예약)에 남은 예약이 보인다(`at` = 실행 시각, 이벤트 예약이면 `eventType`·`remaining`). 시간 예약은 행동 "예약 실행"(개발자) 안에 예약한 행동이 이어서 보인다.
- **선택 대기**: **명령** 탭 맨 위에 "선택 대기 · (안내 글)" 패널이 생긴다(오른쪽에 "1개 고르기"·"1~2개 고르기"). 후보 버튼을 누르면 앞에 ✓가 붙고, 다시 누르면 빠진다. 하나만 고르는 선택은 다른 버튼을 누르면 바꿔 고른다. **답하기**는 `Game.Answer`와 같다. 개수가 틀리면 "1개를 골라야 합니다(고른 것 0개)." 같은 알림이 뜨고 아무것도 바뀌지 않는다. 기다리는 선택이 여럿이면 패널도 여럿이다. 게임 화면이 아직 없어도 여기서 시험할 수 있다.
- 답하기는 행동 "선택: card3"(공개)으로 **행동** 탭에 남고 Undo된다. 그 안에 이어서 할 행동이 보인다(`선택 요구` Block이면 "고른 것에 효과", 개발자).

---

## 4. 코드로 쓰는 법 — 문법 요소를 하나씩

### 4-1. `Game.ApplyStatus` · `Game.RemoveStatus` — 걸기·풀기

```csharp
Game.ApplyStatus(enemy, GameIds.Status.Poison, stacks: 3);            // 객체로
Game.ApplyStatus("hero", GameIds.Status.Rage);                         // id 글자로도
Game.RemoveStatus(enemy, GameIds.Status.Poison, stacks: 1);            // 1중첩만 줄이기
Game.RemoveStatus(enemy, GameIds.Status.Poison);                       // 통째로 풀기
```

| 멤버 | 뜻 |
|---|---|
| `ActiveStatus ApplyStatus(object target, string statusId, int stacks = 1, string source = null)` | 건다. 이미 걸려 있으면 다시 걸 때 규칙을 따른다. 돌려주는 값은 걸린 것(새로 생겼거나 이미 있던 것). `stacks`가 1보다 작으면 1 |
| `bool RemoveStatus(object target, string statusId, int stacks = 0)` | 푼다. `stacks`를 주면 그만큼 줄이고 남는 중첩이 없으면 푼다. 0이면 통째로. 걸려 있지 않았으면 `false`. 끝날 때 결과는 돌지 않는다 |

- `target`은 `id`가 있는 객체 또는 id 글자, `statusId`는 상태 효과 ID(`GameIds.Status.X`), `source`는 출처로 남길 글자(비우면 빈 글자)다.
- 행동 안에서 부른다(Undo·저장·멀티플레이 기록).
- 없는 상태 효과면 `InvalidOperationException`("[MomoiProject] 없는 상태 효과 'poisn'입니다."), 대상에 id가 없으면 "[MomoiProject] 상태 효과를 걸 대상의 id가 없습니다." 예외로 그 행동이 취소된다.

### 4-2. 읽기 — `StatusStacks` · `HasStatus` · `Statuses` · `ActiveStatus`

```csharp
int stacks = Game.StatusStacks(enemy, GameIds.Status.Poison);   // 걸려 있지 않으면 0
bool shielded = Game.HasStatus(run.hero, GameIds.Status.Shield);
foreach (var s in Game.Statuses(run.hero)) icons.Add(s.status, s.stacks);   // 화면: 상태 아이콘 줄
```

| 멤버 | 뜻 |
|---|---|
| `int StatusStacks(object target, string statusId)` | 중첩 수. 걸려 있지 않으면 0 |
| `bool HasStatus(object target, string statusId)` | 걸려 있는지(중첩 > 0) |
| `IReadOnlyList<ActiveStatus> Statuses(object target)` | 그 대상에 걸린 것 전부(새 목록) |

| `ActiveStatus` 필드 | 뜻 |
|---|---|
| `id` | 걸린 것의 id(`$status1` …). 능력치 보정의 출처이기도 하다 |
| `status` | 상태 효과 ID |
| `target` | 가진 쪽 id |
| `stacks` | 중첩 |
| `remaining` | 이벤트 지속: 남은 횟수 / 시간 지속: 끝나는 게임 시각(`session.Clock.Time` 기준) / 영구: 0 |
| `source` | 출처 |

값을 바꾸지 않으므로 화면 코드(행동 밖)에서 읽어도 된다. 걸린 것의 필드를 직접 고치지 않는다(걸기·풀기로 바꾼다).

### 4-3. 결과를 코드로 — `[BindTo(상태 효과 ID)]`

계산이나 분기가 있으면 걸려 있는 동안의 결과를 함수 하나로 쓴다(→ `13-BindTo.md`).

```csharp
public static class StatusCode
{
    // 트리거가 이벤트 받기면 첫 매개변수가 그 이벤트
    [BindTo(GameIds.Status.Poison)]
    static void Poison(TurnEnded e, TriggerContext c)
    {
        var holder = (Fighter)c.User;                                              // 가진 쪽
        Rules.Damage(c.Game, null, holder, c.Game.StatusStacks(holder, c.Owner));   // Owner = 상태 효과 ID
    }

    // 트리거가 가로채기면 첫 매개변수가 의도
    [BindTo(GameIds.Status.Shield)]
    static void Shield(DamageIntent i, TriggerContext c) => i.amount /= 2;
}
```

- 연결된 코드는 `outcomes`(걸려 있는 동안의 결과)를 대신한다. `onApply`·`onExpire`는 대신하지 않고 DB에서 그대로 고친다(3-1).
- 가진 쪽마다 한 번씩 불린다. 가진 쪽 필드로 걸러진 뒤다.
- 조건 충족만으로 된 트리거에는 이벤트가 없으므로 `(TriggerContext c)`만 받는다.
- 가진 쪽에 따른 조건은 여기서 판단한다: `if (((Fighter)c.User).hp * 2 > ((Fighter)c.User).maxHp) return;`

### 4-4. 정의를 코드로 — `StatusDefinition` · `session.Statuses`

게임(Unity)에서는 런타임이 시작할 때 DB의 상태 효과 전부를 등록한다. 서버·테스트처럼 세션을 직접 만들 때는 직접 등록한다.

```csharp
foreach (var status in GameDatabase.Load().All<StatusData>()) session.Statuses.Define(status.ToDefinition());

// DB 없이 코드로(테스트 등)
var poison = new StatusDefinition
{
    Id = "poison", MaxStacks = 0, Reapply = StatusReapply.AddStacksAndRefresh,
    Duration = StatusDuration.Events, Length = 3, TickEvent = typeof(TurnEnded).FullName,
    When = new EventTrigger { eventType = typeof(TurnEnded).FullName },
};
poison.Outcomes.Add(new PoisonTick());          // 코딩한 결과(Reward) 등
session.Statuses.Define(poison);
```

| `StatusDefinition` | 뜻 | DB 칸 |
|---|---|---|
| `Id` · `DisplayName` | ID · 표시 이름 | 항목 ID |
| `MaxStacks` | 최대 중첩(0 = 제한 없음, 코드 기본 0) | `maxStacks` |
| `Reapply` | 다시 걸 때(코드 기본 `AddStacks`) | `reapply` |
| `Duration` · `Length` | 지속 방식 · 길이 | `duration.kind` · `duration.length` |
| `TickEvent` · `TickHolderField` | 셀 이벤트(형식의 전체 이름) · 지속의 가진 쪽 필드 | `duration.tickEvent` · `duration.holderField` |
| `Modifiers` (`List<StatusModifier>`) | 능력치 보정(`attribute` · `kind` · `valuePerStack`) | `modifiers` |
| `When` · `HolderField` | 걸려 있는 동안 트리거 · 가진 쪽 필드 | `trigger` · `holderField` |
| `Outcomes` · `OnApply` · `OnExpire` | 결과 · 걸릴 때 결과 · 끝날 때 결과 | `outcomes` · `onApply` · `onExpire` |

| `session.Statuses` (`StatusSystem`) | 뜻 |
|---|---|
| `Define(definition)` | 등록(같은 ID면 바꾼다). 트리거도 이때 등록한다 |
| `Find(id)` · `Definitions` | 정의 찾기 · 전부 |
| `Apply` · `Remove` · `Stacks` · `Of` | `Game.ApplyStatus` · `RemoveStatus` · `StatusStacks` · `Statuses`와 같다 |
| `Key` | `"$statuses"` |

- `StatusData.ToDefinition()`은 DB 항목을 정의로 바꾼다. 그 ID에 `[BindTo]`가 있으면 `Outcomes`가 그 코드가 된다.
- 코드 정의의 기본값은 DB와 다르다(DB는 최대 중첩 1, 다시 걸 때 `중첩 더하고 지속 갱신`). 코드로 만들 때는 두 값을 적는다.

### 4-5. 효과 Block 형식 — `ApplyStatusEffect` · `RemoveStatusEffect`

```csharp
var dart = new TargetedOutcome { effects = { new DealDamage { amount = 4 }, new ApplyStatusEffect { status = "poison", stacks = 3 } } };
var cleanse = new TargetedOutcome { targets = { new SelfTarget() }, effects = { new RemoveStatusEffect { status = "poison" } } };
```

| 형식 | 화면 이름 | 필드 |
|---|---|---|
| `ApplyStatusEffect` | 상태 효과 걸기 | `status`(상태 효과 ID) · `stacks`(기본 1) |
| `RemoveStatusEffect` | 상태 효과 풀기 | `status` · `stacks`(0 = 모두) |

### 4-6. 예약 — `Game.ScheduleAfter` · `ScheduleOn` · `CancelSchedule(s)`

```csharp
string id = Game.ScheduleAfter(2.5, new DelayedHit { target = enemy, amount = 20 });               // 게임 시간 2.5초 뒤
Game.ScheduleOn<TurnStarted>(1, new DrawCards { count = 2 }, source: GameIds.Instance.Foresight);   // 턴 시작을 1번 받은 순간
Game.CancelSchedule(id);                                                                           // 하나 취소
Game.CancelSchedules(GameIds.Instance.Foresight);                                                  // 그 출처의 예약 모두 취소
```

| 멤버 | 뜻 |
|---|---|
| `string ScheduleAfter(double seconds, GameAction action, string source = null, bool permanent = false)` | 게임 시간 `seconds`초 뒤(0보다 작으면 0) 프레임 끝(고정 틱이면 그 틱)에 실행. 돌려주는 값은 예약 id(`$schedule1` …). `permanent: true`는 예외(2-5) |
| `string ScheduleOn<TEvent>(int count, GameAction action, string source = null, bool permanent = false)` | `TEvent`를 `count`번(1보다 작으면 1) 받은 순간, 그 이벤트를 쏜 행동 안에서 실행 |
| `bool CancelSchedule(string id)` | 그 예약을 지운다. 지웠으면 `true` |
| `int CancelSchedules(string source)` | 그 출처의 예약을 모두 지운다. 지운 개수 |

- `action`은 행동 객체다. 대상 필드는 id만 남고 실행 직전에 다시 찾는다(2-5). 행동 클래스는 다른 행동처럼 `[Serializable]`로 둔다.
- `source`는 나중에 한꺼번에 지울 때 쓰는 글자(보통 이 예약을 건 항목 ID)다.
- `permanent: true`면 메타 구역(프로필)에 저장되어 새 판에도 남는다. 이벤트 예약(`ScheduleOn`)만 된다. `ScheduleAfter`에 주면 `InvalidOperationException`(2-5).
- 걸기·취소 모두 행동 안에서 부른다.
- `action`이 `null`이면 `ArgumentNullException`.

### 4-7. `session.Schedules` · `ScheduledAction`

| `session.Schedules` (`ScheduleSystem`) | 뜻 |
|---|---|
| `After(seconds, action, source, permanent)` | = `Game.ScheduleAfter` |
| `On(Type eventType, count, action, source, permanent)` | = `Game.ScheduleOn`(형식을 실행 중에 정할 때) |
| `Cancel(id)` · `CancelBySource(source)` | = `Game.CancelSchedule` · `CancelSchedules` |
| `All` | 남은 예약 전부(`IReadOnlyList<ScheduledAction>`, 세션·메타 둘 다) |
| `SessionKey` · `MetaKey` | `"$schedule"` · `"$schedule.meta"` |

| `ScheduledAction` 필드 | 뜻 |
|---|---|
| `id` · `source` | 예약 id · 출처 |
| `at` | 시간 예약이면 실행할 게임 시각, 아니면 -1 |
| `eventType` · `remaining` | 이벤트 예약이면 이벤트 형식 이름 · 남은 횟수 |
| `action` | 맡긴 행동(대상 필드는 id만 든 객체) |

화면에 "2턴 뒤 폭발" 같은 표시를 하려면 `session.Schedules.All`에서 `source`로 찾아 `remaining`을 읽는다.

### 4-8. 선택 대기 — `Game.Ask`

```csharp
// 후보 = id가 있는 객체(덱 카드·적 …)
Game.Ask("버릴 카드", handCards, new DiscardChosen(), min: 1, max: 1, label: o => Rules.CardData(Game, ((DeckCard)o).card)?.title);

// 후보 = id 글자(손패 battle.hand 같은 id 목록을 그대로, DB 원본 ID)
Game.Ask("버릴 카드", Game.Data().Run.battle.hand, new DiscardChosen());

// 후보 = 글 선택지(id와 보일 글을 직접: "공격/방어" 같은 선택)
Game.Ask("자세", new[] { new ChoiceCandidate("attack", "공격"), new ChoiceCandidate("guard", "방어") }, new ChooseStance());
```

| 멤버 | 뜻 |
|---|---|
| `string Ask(string prompt, IEnumerable<object> candidates, GameAction then, int min = 1, int max = 1, string source = null, string player = null, Func<object, string> label = null)` | 후보로 요구한다. 후보는 `id`가 있는 객체 또는 id 글자(글자 자체가 id). `label`은 후보마다 보일 이름을 만드는 함수. 비우거나 `null`을 돌려주면 텍스트 표의 `<id>.name`(없으면 id) |
| `string Ask(string prompt, IEnumerable<ChoiceCandidate> options, GameAction then, int min = 1, int max = 1, string source = null, string player = null)` | 글 선택지로 요구한다(보일 글을 직접 준다) |

- 돌려주는 값은 선택 id(`$choice1` …). 후보가 없어 바로 이어 간 경우는 `null`.
- `then`은 이어서 할 행동이다. `[Chosen]` 필드가 있어야 한다(4-9). 없으면 `InvalidOperationException`("[MomoiProject] 이어서 할 행동 'X'에 고른 것을 받을 [Chosen] 필드가 없습니다.").
- `source`는 요구한 항목 ID(기록용), `player`는 답할 플레이어(2-6).
- 행동 안에서 부른다. 다음 줄은 고르기 전에 실행된다.
- 후보가 글자면 그 글자가 id다. 카드 던전의 손패(`battle.hand`)는 덱 카드 **id 글자 리스트**라서 그대로 넘긴다. 이어서 할 행동의 `[Chosen]`이 객체 칸(`List<DeckCard>`)이면 답할 때 그 id로 지금 데이터의 덱 카드를 찾아 넣는다(4-9).
- 객체 후보 형식에 `id`가 없으면 예외다("[MomoiProject] Unit에 string id 필드가 없어 개체로 찾거나 가리킬 수 없습니다.").

### 4-9. `[Chosen]` — 고른 것을 받는 칸

```csharp
[Serializable, BlockInfo("고른 카드 버리기")]
public class DiscardChosen : GameAction
{
    [Chosen] public List<DeckCard> cards = new List<DeckCard>();   // 고른 것이 여기 들어온다
    protected override void Run() { /* cards로 할 일 */ }
}
```

| `[Chosen]` 필드 형식 | 들어오는 것 |
|---|---|
| `string` | 고른 id 하나(고르지 않았으면 `null`) |
| `id`가 있는 클래스(`DeckCard`) | 고른 객체 하나. 실행 직전에 id로 지금 데이터에서 찾는다 |
| 위의 `List<…>` · 배열 | 고른 것 여럿(고른 순서대로) |

- 한 행동에 하나만 둔다(처음 것만 쓴다). 다른 형식이면 요구할 때 "[MomoiProject] 'X.f': [Chosen] 필드는 string, id가 있는 클래스, 그 리스트·배열이어야 합니다." 예외.
- 객체 칸은 답하는 순간 다시 찾는다. 그 사이 후보가 데이터에서 사라졌으면 "… 'card3'을(를) 찾을 수 없습니다" 로 답하기가 실패한다. 사라질 수 있는 후보는 `string` 칸으로 받고 이어서 할 행동에서 `Game.Find`로 확인한다.
- 글 선택지(`ChoiceCandidate`)와 DB 원본 ID 후보는 `string` 칸으로 받는다(플레이어 데이터 객체가 아니다).
- 이어서 할 행동의 다른 필드(수치·원본 ID·대상 객체)는 요구할 때 채워 둔다. 대상 필드는 예약처럼 id만 저장된다.

### 4-10. 답하기·읽기 — `Choice` · `Choices` · `IsWaiting` · `Answer` · `CancelChoice`

```csharp
var choice = Game.Choice;                                  // 지금 기다리는 선택(없으면 null)
if (Game.IsWaiting) return;                                // 입력 막기
Game.Answer(choice.id, pickedCard);                        // 객체로
Game.Answer(choice.id, "card2", "card5");                  // id로
Game.Answer(choice.id, picked.Cast<object>().ToList());    // 목록으로
```

| 멤버 | 뜻 |
|---|---|
| `PendingChoice Choice` | 지금 답을 기다리는 선택(먼저 요구한 것). 없으면 `null` |
| `IReadOnlyList<PendingChoice> Choices` | 기다리는 선택 전부(먼저 요구한 순서) |
| `bool IsWaiting` | 기다리는 선택이 있는지 |
| `ActionRecord Answer(string choiceId, IEnumerable<object> picked)` · `Answer(string choiceId, params object[] picked)` | 답한다(행동 밖, 플레이어 입력에서). 고른 것은 객체 또는 id 글자. 행동 `ChoiceAnswer` 하나로 실행된다 |
| `bool CancelChoice(string choiceId)` | 기다리는 선택을 답 없이 지운다(이어서 할 행동은 돌지 않는다). 행동 안에서 부른다 |

답하기가 실패하면 예외가 `Answer`를 부른 쪽으로 온다. 플레이어에게 보일 실패라 `GameFailure`(`InvalidOperationException`을 상속, `30-Balance-Tables.md` 2-4)이고, 메시지는 텍스트 표에 그 키가 있으면 번역된 글이다.

| 경우 | 메시지(기본 문장) | 텍스트 키(`FailureKeys`) |
|---|---|---|
| 개수가 맞지 않음(최소 = 최대) | "{count}개를 골라야 합니다(고른 것 {picked}개)." | `ChooseExactly` |
| 개수가 맞지 않음(범위) | "{min}~{max}개를 골라야 합니다(고른 것 {picked}개)." | `ChooseBetween` |
| 후보가 아님 | "{item}은(는) 고를 수 있는 후보가 아닙니다." | `NotACandidate` |
| 같은 것을 두 번 | "같은 것을 두 번 골랐습니다." | `ChosenTwice` |
| 이미 답했거나 취소됨 | "이미 답했거나 취소된 선택입니다." | `ChoiceGone` |

### 4-11. `PendingChoice` · `ChoiceCandidate` · `ChoiceRequested` · `ChoiceAnswer`

| `PendingChoice` 필드 | 뜻 |
|---|---|
| `id` | 선택 id(`$choice1` …) |
| `prompt` | 안내 글 |
| `player` | 답할 플레이어(비우면 누구나) |
| `source` | 요구한 항목 ID |
| `options` (`List<ChoiceCandidate>`) | 후보들 |
| `min` · `max` | 고를 개수(2-6의 규칙으로 맞춘 값) |
| `then` | 이어서 할 행동 |

- `ChoiceCandidate { id, label }` — 후보 하나. `new ChoiceCandidate(id, label)`에서 `label`을 비우면 id가 이름이다.
- `ChoiceRequested`(`[GameEvent("선택 요구")]`: `choice` · `prompt` · `player`) — 선택을 요구한 순간 쏜다. 화면은 `SubscribeAfterAction`으로 받아 고르기 창을 연다(요구한 행동이 실패하면 오지 않는다). 답하기를 Undo해서 다시 기다리게 될 때는 다시 쏘지 않는다. 창의 내용은 `Game.Choice`로 그린다(5-10).
- `ChoiceAnswer`(`choice` · `picked`) — 답하기 행동. 공개 행동이라 타임라인·행동 탭에 "선택: card3"(고르지 않았으면 "선택: 고르지 않음")으로 남는다. 멀티플레이에서는 이 행동이 서버로 간다.
- `session.Choices`(`ChoiceSystem`): `Ask(prompt, options, then, min, max, source, player)`(글 선택지 형태) · `Current`(= `Game.Choice`) · `All` · `Cancel(id)` · `Key`(`"$choices"`).

### 4-12. `ChoiceOutcome` — 결과 '선택 요구'를 코드로

```csharp
var prepare = new ChoiceOutcome
{
    prompt = "비용 0으로 만들 카드",
    candidates = { new HandCards() },
    min = 1, max = 1,
    effects = { new ApplyStatusEffect { status = "free" } },
};
```

| 필드 | 뜻 |
|---|---|
| `prompt` | 안내 글 |
| `candidates` (`List<TargetSelector>`) | 후보를 고르는 대상 Block들. 비면 고른 대상(`Picked`) 그대로 |
| `min` · `max` | 고를 개수 |
| `effects` (`List<Effect>`) | 고른 것마다 실행할 효과 |
| `then` (`List<Outcome>`) | 효과 다음에 실행할 결과 |
| `Kind` | `OutcomeKinds.Targeted` |

이어서 할 행동은 라이브러리의 `ChoiceEffectsRun`(개발자 행동 "고른 것에 효과")이다. 직접 만들 일은 없다.

### 4-13. 도우미

| 멤버 | 뜻 |
|---|---|
| `ActionReferences.Detach(action)` | 행동의 대상 필드를 id만 든 객체로 바꾼다. 예약·선택 대기가 행동을 맡을 때 쓴다 |
| `ActionReferences.CanResolve(action, game)` | 대상 필드가 가리키는 객체가 지금 데이터에 모두 있는지. 예약이 실행 전에 확인한다 |
| `StatusExpireRun` · `ScheduledRun` | 시간 만료·시간 예약을 실행하는 라이브러리 행동(개발자, "상태 효과 만료"·"예약 실행") |

---

## 5. 상황별 예제

예제는 가이드의 카드 던전 형식을 쓴다(`Tools/GuideExample`). 이 장에 필요한 부분만 옮긴다.

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using GBible;
using GBible.Ids;

[Serializable] public class Fighter { public string id; public string name; public int hp, maxHp, block; public bool IsDead => hp <= 0; }
[Serializable] public class Hero : Fighter { public int energyPerTurn = 3, drawPerTurn = 5; }
[Serializable] public class Enemy : Fighter { public string monster; public int intentIndex; }
[Serializable] public class DeckCard { public string id; public string card; public bool upgraded; }   // card = 원본(DB › 인스턴스) ID

[Serializable]
public class Battle
{
    public int turn, energy;
    public List<Enemy> enemies = new List<Enemy>();
    public List<string> drawPile = new List<string>(), hand = new List<string>(), discard = new List<string>();   // 덱 카드 id
}

[GameEvent("턴 끝")] public class TurnEnded { public int turn; public int damage; }
[GameEvent("턴 시작")] public class TurnStarted { public int turn; }
[Serializable, Intent("피해")] public class DamageIntent { public string sourceId, targetId; public int amount; public bool cancelled; }

// 플레이어 데이터 "run"(RunState, 세션 구역): hero · deck(List<DeckCard>) · battle(전투 밖이면 null) …
// Rules.Damage(game, source, target, amount): 가로채기(DamageIntent) → 방어도 → 체력 → 피해 이벤트 → 쓰러진 적은 전투에서 뺀다.
// Rules.CardOf(game, id): 덱 카드 찾기. Rules.CardData(game, 원본 ID): 카드 원본(Card: title · cost …) 읽기.
// EndTurn: 턴 끝(TurnEnded)을 쏜다 → 적이 공격한다 → 손패를 버린다 → 카드를 뽑는다 → 턴 시작(TurnStarted)을 쏜다.
// WinBattle(전투 승리)도 그 턴의 턴 끝을 쏜다.
```

- ID 상수(`GameIds.Status.Poison`, `GameIds.Instance.PoisonDart` …)는 DB에 항목을 만들고 저장하면 생긴다.
- 5-2는 `Fighter`에 능력치 `public double attack = 10;`(공격력)이 있다고 본다(가이드 예제에는 없다).

### 5-1. 독 — 턴 끝마다 중첩만큼 피해, 3턴

**에디터**(DB › 상태 효과 `poison`):

1. `maxStacks` `0`(제한 없음), `reapply` `중첩 더하고 지속 갱신`.
2. `duration`: `kind` `이벤트 횟수`, `length` `3`, `tickEvent` `턴 끝`. 가진 쪽 필드는 비운다.
3. `trigger`: `+ 블럭` → `이벤트 받기` → `턴 끝`. `holderField`는 비운다(턴 끝에는 가진 쪽 id가 없다. 걸린 대상 모두가 받는다).
4. 결과는 중첩 수를 읽는 계산이라 코드로 쓴다(4-3의 `StatusCode.Poison`). `outcomes` 칸은 잠기고 함수 이름이 보인다.

**카드** `poisonDart`(독침): 사용 기능 결과 `대상에게 효과` → 대상 비움(고른 적) → 효과 `피해`(4) → `상태 효과 걸기`(`poison`, 중첩 `3`). 요약 `고른 대상: 피해 4, poison 3중첩 걸기`.

숫자로 따라가기 — 고블린에게 독침을 두 번:

| 순간 | 독 피해 | 중첩 | 남은 횟수 |
|---|---|---|---|
| 턴 1: 독침 | — | 3 | 3 |
| 턴 1 끝 | 3 | 3 | 2 |
| 턴 2: 독침 한 번 더 | — | 6 | 3(갱신) |
| 턴 2 끝 | 6 | 6 | 2 |
| 턴 3 끝 | 6 | 6 | 1 |
| 턴 4 끝 | 6 | — | 0 → 끝 |

- 같은 턴 끝이 피해와 횟수 줄이기를 둘 다 하므로 피해가 먼저다(2-2). 그래서 마지막 턴 끝에도 피해가 들어간다.
- 독 피해도 `Rules.Damage`를 거치므로 보호막(5-3)·가시 갑옷 같은 가로채기를 탄다. 출처는 `null`이라 "이번 턴 준 피해"에 들어가지 않는다.
- 카드 던전은 전투 승리(`WinBattle`)도 턴 끝을 쏘므로 그때도 한 번 센다.
- 코드 없이 쓰려면 "중첩만큼 피해" 효과 Block을 하나 만들어 `outcomes` = `대상에게 효과`(대상 비움 = 가진 쪽) → 효과 `중첩만큼 피해`로 조립한다. 이 Block은 상태 효과의 결과에서만 뜻이 있다(`Owner`가 상태 효과 ID일 때).

```csharp
[Serializable, BlockInfo("중첩만큼 피해", Category = "상태 효과", Sentence = "중첩 × {perStack} 피해")]
public class StackDamage : Effect<Fighter>
{
    public int perStack = 1;
    protected override void Apply(Fighter target) =>
        Rules.Damage(Game, null, target, perStack * Game.StatusStacks(target, Use.Context.Owner));
}
```

### 5-2. 분노 — 중첩당 공격력 +10%, 최대 3

**에디터**(DB › 상태 효과 `rage`): `maxStacks` `3`, `reapply` `중첩 더하기`, `duration` › `kind` `영구(풀 때까지)`, `modifiers` 한 줄: `attribute` `attack`, `kind` `퍼센트(0.2 = +20%)`, `valuePerStack` `0.1`. 트리거·결과는 비운다.

**카드** `battleCry`(함성): 결과 `대상에게 효과` → 대상 `사용자 자신` → 효과 `상태 효과 걸기`(`rage`).

| 함성을 쓴 횟수 | 중첩 | `Game.Attribute(hero, nameof(Fighter.attack))` |
|---|---|---|
| 0 | — | 10 |
| 1 | 1 | 11 |
| 2 | 2 | 12 |
| 3 | 3 | 13 |
| 4 | 3(최대) | 13 |

- 보정은 Modifier 한 줄(출처 `$status1`, 값 0.1 × 중첩)이다. 중첩이 바뀔 때마다 다시 계산된다. `hero.attack` 필드는 10 그대로다.
- 출처가 상태 효과 ID가 아니라 걸린 것의 id라서 `능력치 보정 떼기(rage)`·`Game.RemoveModifiers("rage")`로는 떨어지지 않는다. 풀어서 뗀다.
- 영구라 풀 때까지 남는다. 전투가 끝날 때 전투 동안의 상태 효과를 모두 풀려면 전투 승리 행동에 더한다.

```csharp
// WinBattle.Run 안
var hero = Game.Data().Run.hero;
foreach (var status in Game.Statuses(hero)) Game.RemoveStatus(hero, status.status);   // 끝날 때 결과는 돌지 않는다
```

### 5-3. 보호막 — 내가 받는 피해 반감, 3턴

**에디터**(DB › 상태 효과 `shield`):

1. `maxStacks` `1`, `reapply` `지속 갱신`.
2. `duration`: `kind` `이벤트 횟수`, `length` `3`, `tickEvent` `턴 시작`.
3. `trigger`: `+ 블럭` → `가로채기` → 의도 `피해`.
4. `holderField`: `targetId`. 피해 의도의 대상이 가진 쪽일 때만 발동한다.
5. `outcomes`: `+ 결과 추가` → `의도 고치기` → 필드 `amount`, 방법 `곱하기`, 값 `0.5`.

**카드** `barrier`(방벽): 결과 `대상에게 효과` → 대상 `사용자 자신` → 효과 `상태 효과 걸기`(`shield`).

- 고블린의 공격 10 → 5. 정수 필드는 곱한 뒤 반올림한다(28장 3-4).
- **가진 쪽 필드가 "내가"를 만든다.** `targetId`를 고르면 주인공에게 걸면 주인공이 맞을 때만, 적에게 걸면 그 적이 맞을 때만 반감한다. 비우면 걸려 있는 동안 누가 맞든 반감한다. 유물 가시 갑옷(28장 5-4)이 의도 조건 `targetId 일치 hero`로 대상을 고정한 것과 다르다.
- **셀 이벤트가 턴 시작인 이유**: `EndTurn`은 턴 끝을 쏜 **다음에** 적이 공격하고, 마지막에 턴 시작을 쏜다. 턴 끝으로 세면 세 번째 턴 끝에서 먼저 끝나 세 번째 적 공격을 막지 못한다. 턴 시작으로 세면 적 공격 세 번을 막고 끝난다.
- 같은 피해에 다른 가로채기도 걸려 있으면 가로채기 잎의 순서(`order`)가 작은 것부터다(28장 5-4).
- 코드로 쓰려면 4-3의 `StatusCode.Shield`(정수 나눗셈이라 내림).

### 5-4. 3턴 뒤 폭발 — 끝날 때 결과

카드 "시한폭탄": 주인공에게 타이머를 걸고, 세 번째 턴이 끝날 때 모든 적에게 20 피해.

**에디터**(DB › 상태 효과 `bomb`):

1. `maxStacks` `1`, `reapply` `무시`(다시 써도 처음 타이머 그대로).
2. `duration`: `kind` `이벤트 횟수`, `length` `3`, `tickEvent` `턴 끝`.
3. `onExpire`(끝날 때 결과): `대상에게 효과` → 대상 `모든 적` → 효과 `피해`(20).

**카드** `timeBomb`: 결과 `대상에게 효과` → 대상 `사용자 자신` → 효과 `상태 효과 걸기`(`bomb`).

- 세 번째 턴 끝(적 공격 전)에 터진다.
- 끝날 때 결과의 사용자는 가진 쪽(주인공)이다. 그래서 `피해` Block의 출처(`User<Fighter>()`)가 주인공이다.
- 풀면 터지지 않는다. "해체" 카드 = `상태 효과 풀기`(`bomb`).
- 적에게 걸었다면, 그 적이 먼저 쓰러지면 터지지 않는다(끝날 때 결과는 가진 쪽이 있을 때만).
- `reapply`를 `지속 갱신`으로 바꾸면 다시 쓸 때 타이머가 3턴으로 돌아간다.
- 코드로 같은 일을 하려면 예약(5-6)이지만, 기획자가 DB에서 만들고 모니터에서 남은 횟수를 보려면 이 방법이 맞다.

### 5-5. 이번 턴 비용 0 — 상태 효과로

28장 5-6의 "집중"을 상태 효과로 만든다. 켜졌는지 담는 플래그(`freeCost`)·턴 끝에 되돌리는 코드·직접 등록 코드가 모두 필요 없다.

**먼저**: 카드를 내는 행동이 비용을 `Game.Pay`로 내야 가로채기 `비용 지불`을 거친다(가이드 예제의 `PlayCard`는 에너지를 직접 뺀다).

```csharp
// PlayCard.Run: 에너지를 직접 검사하고 빼던 줄을 바꾼다
var data = Rules.CardData(Game, card.card);
Game.Pay(GameIds.PlayerData.Run.Id, "battle.energy", data.cost);   // 가로채기(비용 지불) → 모자라면 실패
```

**에디터**(DB › 상태 효과 `focus`):

1. `maxStacks` `1`, `reapply` `지속 갱신`.
2. `duration`: `kind` `이벤트 횟수`, `length` `1`, `tickEvent` `턴 끝`.
3. `trigger`: `+ 블럭` → `가로채기` → 의도 `비용 지불`. 의도 조건 `filter`에 `필드 검사` → `key` `일치` `run`, `AND` `필드 검사` → `path` `일치` `battle.energy`.
4. `holderField`는 비운다.
5. `outcomes`: `의도 고치기` → 방법 `막기`.

**카드** `concentrate`(집중): 결과 `대상에게 효과` → 대상 `사용자 자신` → 효과 `상태 효과 걸기`(`focus`).

- 턴이 끝나면 지속이 다 되어 저절로 풀린다. Undo하면 함께 돌아간다.
- 가진 쪽 필드를 비운 이유: 비용 의도(`CostIntent`)에는 누가 내는지가 없다. 그래서 이 상태 효과는 주인공에게만 건다.
- 상점의 골드 비용(`run › gold`)은 의도 조건에 걸리지 않아 그대로 낸다.
- 버튼 미리보기: `Game.CanPay`는 가로채기 전 값이다. `Game.CanPay(GameIds.PlayerData.Run.Id, "battle.energy", cost) || Game.HasStatus(run.hero, GameIds.Status.Focus)`처럼 화면이 함께 본다.

### 5-6. 코드로 예약 — `ScheduleOn` · `ScheduleAfter`

**다음 턴 시작에 2장 더 뽑기**(카드 `foresight` "예지"의 결과):

```csharp
[BindTo(GameIds.Instance.Foresight)]
static void Foresight(TriggerContext c) =>
    c.Game.ScheduleOn<TurnStarted>(1, new DrawCards { count = 2 }, source: c.Owner);
```

`EndTurn`이 다음 턴의 카드를 뽑고 턴 시작을 쏘는 순간, 그 행동 안에서 `DrawCards`가 한 번 더 돈다. 턴 끝 Undo 한 번에 같이 돌아간다.

**2턴 뒤 그 적에게 15 피해**(카드 `cursedArrow` "저주 화살"의 결과) — 대상이 있는 행동을 맡긴다.

```csharp
[Serializable, BlockInfo("지연 피해")]
public class DelayedHit : GameAction
{
    public Fighter target;      // 맡길 때 id만 남고, 실행 직전에 지금 객체로 다시 찾는다
    public int amount;
    protected override void Run() => Rules.Damage(Game, null, target, amount);
}

[BindTo(GameIds.Instance.CursedArrow)]
static void CursedArrow(TriggerContext c)
{
    foreach (var enemy in c.Picked.OfType<Fighter>())
        c.Game.ScheduleOn<TurnEnded>(2, new DelayedHit { target = enemy, amount = 15 }, source: c.Owner);
}
```

- 그 사이 적이 쓰러졌으면 실행하지 않고 지운다. 타임라인에 "대상이 없어 예약을 실행하지 않았습니다: 지연 피해 (enemy2)".
- 예약은 판 동안 남는다. 전투가 끝나기 전에 횟수가 차지 않으면 다음 전투로 넘어간다(카드 던전은 전투 승리도 턴 끝을 쏜다). 전투마다 끝나야 하면 전투 승리 행동에서 지운다: `Game.CancelSchedules(GameIds.Instance.CursedArrow);`

**실시간이면 게임 시간으로**:

```csharp
Game.ScheduleAfter(2.5, new DelayedHit { target = enemy, amount = 20 }, source: "bomb");   // 2.5초 뒤 프레임 끝
```

일시정지·배속을 따르고, 스냅샷 되감기로 함께 돌아간다. 실행은 행동 하나("예약 실행")라 실패하면 경고만 남고 그 예약은 지워진다.

### 5-7. 카드 1장 버리기 — `Game.Ask` + `[Chosen]`

카드 `survey`(정리): "카드 2장을 뽑고, 손패에서 1장을 버린다."

```csharp
[BindTo(GameIds.Instance.Survey)]
static void Survey(TriggerContext c)
{
    var game = c.Game;
    game.Do(new DrawCards { count = 2 });
    var hand = game.Data().Run.battle.hand;                                           // 덱 카드 id 리스트를 그대로 후보로
    game.Ask("버릴 카드", hand, new DiscardChosen(), min: 1, max: 1, source: c.Owner,
        label: o => Rules.CardData(game, Rules.CardOf(game, (string)o).card)?.title);  // 보일 이름 = 카드 원본의 이름
    // 여기 아래는 고르기 전에 바로 실행된다. 고른 뒤의 일은 DiscardChosen에 쓴다.
}

[Serializable, BlockInfo("고른 카드 버리기")]
public class DiscardChosen : GameAction
{
    [Chosen] public List<DeckCard> cards = new List<DeckCard>();

    protected override void Run()
    {
        var battle = Game.Data().Run.battle ?? throw new InvalidOperationException("전투 중이 아닙니다.");
        foreach (var card in cards)
        {
            battle.hand.Remove(card.id);
            battle.discard.Add(card.id);
        }
    }
}
```

| 단계 | Undo 단위 |
|---|---|
| 정리를 낸다: 에너지 · 카드 2장 뽑기 · 선택 요구(`ChoiceRequested`) | `PlayCard` 하나 |
| 플레이어가 고른다: `Game.Answer(choice.id, card)` → 버리기 | `ChoiceAnswer` 하나 |

- Undo 한 번이면 버리기만 돌아가고 다시 고르기를 기다린다. 한 번 더 Undo하면 정리를 내기 전으로 돌아간다.
- 후보는 id 글자 그대로지만 `[Chosen]`이 `List<DeckCard>`라서 답할 때 덱 카드 객체로 찾아 넣는다. id 그대로 받고 싶으면 `[Chosen] public List<string> cardIds;`.
- `label`을 비우면 후보 이름은 텍스트 표의 `<id>.name`이다. 덱 카드 id(`card3`)에는 이름 키가 없으므로 id가 보인다. 그래서 카드 원본의 이름을 `label`로 준다.
- 손패가 비어 있으면 기다리지 않고 바로 `DiscardChosen`이 돈다(버릴 것 없음).

### 5-8. 보상 3개 중 1개 — DB 원본 ID · 글 선택지

**카드 보상**: 후보가 플레이어 데이터 객체가 아니라 DB 원본 ID이므로 글 선택지로 요구하고 `string`으로 받는다.

```csharp
// WinBattle.Run 끝에
var pool = new[] { GameIds.Instance.Strike, GameIds.Instance.Defend, GameIds.Instance.Fireball };   // 보통은 가중치 표로 뽑는다(30장 5-5)
var options = pool.Select(id => new ChoiceCandidate(id, Rules.CardData(Game, id).title));
Game.Ask("카드 보상", options, new TakeCardReward(), min: 0, max: 1);   // 최소 0 = 건너뛰기 허용

[Serializable, BlockInfo("카드 보상 받기")]
public class TakeCardReward : GameAction
{
    [Chosen] public string card;        // 고른 원본 ID(건너뛰면 null)

    protected override void Run()
    {
        if (string.IsNullOrEmpty(card)) return;            // 건너뛰기
        Game.Data().Run.deck.Add(Rules.NewCard(Game, card));
    }
}
```

**보상 종류 고르기**(글 선택지):

```csharp
Game.Ask("보상", new[]
{
    new ChoiceCandidate("gold", "골드 30"),
    new ChoiceCandidate("heal", "체력 10 회복"),
    new ChoiceCandidate("card", "카드 보상"),
}, new TakeReward());

[Serializable, BlockInfo("보상 받기")]
public class TakeReward : GameAction
{
    [Chosen] public string pick;

    protected override void Run()
    {
        var run = Game.Data().Run;
        switch (pick)
        {
            case "gold": run.gold += 30; break;
            case "heal": run.hero.hp = Math.Min(run.hero.maxHp, run.hero.hp + 10); break;
            case "card": /* 위의 카드 보상을 이어서 요구한다 */ break;
        }
    }
}
```

- 이어서 할 행동 안에서 `Game.Ask`를 또 부르면 이어 고르기가 된다("카드 보상"을 고르면 카드 세 장 중 하나).
- 판을 저장하고 끄면 고르기 화면에서 이어진다.

### 5-9. 손패에서 1장 골라 이번 턴 비용 0 — `선택 요구`(DB)

기획자가 DB에서만 조립한다. 프로그래머는 처음 한 번 두 가지를 해 둔다.

**코드(한 번만)** — 대상 Block `손패`와, 카드 비용이 상태 효과 `free`를 보게 하는 한 줄.

```csharp
[Serializable, BlockInfo("손패", Category = "대상", Description = "지금 손에 든 카드 모두(고른 대상은 버립니다).")]
public class HandCards : TargetSelector
{
    protected override IEnumerable<object> Select(IEnumerable<object> targets)
    {
        var battle = Game.Data().Run.battle;
        if (battle == null) yield break;
        foreach (var id in battle.hand) yield return Rules.CardOf(Game, id);
    }
}

// PlayCard.Run(5-5의 Game.Pay 줄)
var cost = Game.HasStatus(card, GameIds.Status.Free) ? 0 : data.cost;
Game.Pay(GameIds.PlayerData.Run.Id, "battle.energy", cost);
```

**에디터**:

1. DB › 상태 효과 `free`(공짜): `duration` › `kind` `이벤트 횟수`, `length` `1`, `tickEvent` `턴 끝`. 나머지는 비운다(표시만 하는 상태 효과).
2. 카드 `prepare`(준비): 사용 기능 결과 `+ 결과 추가` → `선택 요구`.
   - `prompt` `비용 0으로 만들 카드`
   - `candidates`: `+ 대상 추가` → `손패`
   - `min` `1`, `max` `1`
   - `effects`: `+ 효과 추가` → `상태 효과 걸기`(`free`)
3. 요약: `손패에서 1개 고르기: free 걸기`.

흐름:

1. 준비를 내면 `PlayCard`가 준비를 손패에서 뺀 뒤 사용 기능을 쓴다. 그래서 준비 자신은 후보에 없다.
2. `선택 요구`가 손패의 덱 카드를 후보로 선택을 요구한다. `PlayCard`가 끝난다.
3. 플레이어가 한 장을 고르면 그 덱 카드에 `free`가 걸린다(상태 효과는 id가 있는 객체면 무엇에든 걸린다).
4. 그 카드를 내면 비용 0. 턴이 끝나면 `free`가 저절로 풀린다.

- 시험: 모니터 **명령** 탭의 "선택 대기 · 비용 0으로 만들 카드" 패널에서 고른다. 버튼에는 후보 이름(텍스트 표의 `<id>.name`)이 보이는데, 덱 카드 id에는 이름 키가 없어 `card3`처럼 id가 보인다. 데이터 탭 `$statuses`에서 `target`이 그 id인 `free` 줄을 확인한다.
- 손패가 비어 있으면 기다리지 않는다(효과 없음).
- 같은 조립으로 "손패에서 2장 골라 버리기"(`max` 2, 효과 `버리기` Block), "적 하나를 골라 독"(후보 `모든 적`, 효과 `상태 효과 걸기`(`poison`))도 된다.

### 5-10. 화면 쪽 — `ChoiceRequested` 받기 · `Game.IsWaiting`로 입력 막기

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using GBible;
using GBible.Ids;
using UnityEngine;
using UnityEngine.UIElements;

public class ChoicePanel : MonoBehaviour
{
    [SerializeField] UIDocument document;
    [SerializeField] AudioClip openSound;

    VisualElement _panel, _options;
    Label _prompt, _message;
    readonly List<string> _picked = new List<string>();
    IDisposable _onAsk, _view;

    static GameApi Game => GBibleRuntime.Game;

    void OnEnable()
    {
        var root = document.rootVisualElement;
        _panel = root.Q("choice");
        _options = root.Q("choice-options");
        _prompt = root.Q<Label>("choice-prompt");
        _message = root.Q<Label>("choice-message");
        root.Q<Button>("choice-confirm").clicked += Confirm;

        // 요구한 순간(성공한 행동의 이벤트만 온다): 고른 것을 비우고 연출
        _onAsk = GBibleRuntime.Session.Events.SubscribeAfterAction<ChoiceRequested>(e =>
        {
            _picked.Clear();
            GameAudio.PlaySfx(openSound);
        });
        // 그리기는 데이터에서: 선택 목록($choices)이 바뀌면(요구·답하기·Undo·불러오기) 프레임 끝에 다시 그린다
        _view = GameView.Redraw(Redraw, ChoiceSystem.Key);
    }

    void OnDisable()
    {
        _onAsk?.Dispose();
        _view?.Dispose();
    }

    void Redraw()
    {
        var choice = Game.Choice;
        _panel.style.display = choice == null ? DisplayStyle.None : DisplayStyle.Flex;
        if (choice == null) return;
        _prompt.text = choice.prompt;
        _options.Clear();
        foreach (var option in choice.options)
        {
            var id = option.id;
            _options.Add(new Button(() => Toggle(choice, id)) { text = (_picked.Contains(id) ? "✓ " : "") + NameOf(option) });
        }
    }

    // 후보 이름은 텍스트 표의 '<id>.name'(없으면 id)이다. 덱 카드는 이름 키가 없어 id가 이름이므로 카드 이름으로 바꿔 그린다.
    static string NameOf(ChoiceCandidate option) =>
        Rules.CardOf(Game, option.id) is DeckCard card ? Rules.CardData(Game, card.card)?.title ?? option.label : option.label;

    void Toggle(PendingChoice choice, string id)
    {
        if (!_picked.Remove(id))
        {
            if (choice.max == 1) _picked.Clear();     // 하나만 고르는 선택은 바꿔 고른다
            _picked.Add(id);
        }
        Redraw();
    }

    void Confirm()
    {
        var choice = Game.Choice;
        if (choice == null) return;
        try
        {
            Game.Answer(choice.id, _picked.Cast<object>().ToList());
            _picked.Clear();
            _message.text = "";
        }
        catch (InvalidOperationException e)
        {
            _message.text = e.Message;    // "1개를 골라야 합니다(고른 것 0개)." — 아무것도 바뀌지 않았다
        }
    }
}
```

다른 입력은 기다리는 동안 막는다(시스템은 막지 않는다).

```csharp
// RunDirector(입력 창구)
public bool TryPlay(DeckCard card, Fighter target, out string error)
{
    error = null;
    if (Game.IsWaiting) { error = "먼저 고르세요."; return false; }
    try { Game.Do(new PlayCard { card = card, target = target }); return true; }
    catch (InvalidOperationException e) { error = e.Message; return false; }
}

public void EndTurn()
{
    if (!Game.IsWaiting) Game.Do(new EndTurn());
}

// BattleHud: 선택이 바뀌어도 다시 그리도록 키를 더하고, 기다리는 동안 손패를 흐리게
_view = GameView.Redraw(Redraw, GameIds.PlayerData.Run.Id, ChoiceSystem.Key);
_hand.SetEnabled(!GBibleRuntime.Game.IsWaiting);     // Redraw 안
```

- **창을 여는 신호는 `ChoiceRequested`, 창의 내용은 `Game.Choice`.** 답하기를 Undo하면 선택이 다시 기다리지만 `ChoiceRequested`는 다시 오지 않는다. 판을 불러온 직후도 같다. 그래서 내용은 `GameView.Redraw(…, ChoiceSystem.Key)`로 데이터에서 그린다.
- 고르는 중인 체크 표시(`_picked`)는 화면 상태다. 플레이어 데이터에 두지 않는다. 답을 보낼 때만 행동이 된다.
- Undo 버튼은 막지 않아도 된다. 기다리는 중에 Undo하면 요구한 행동이 돌아가고 선택도 사라진다.

---

## 6. 주의할 점 · 자주 하는 실수

| 증상 | 원인과 해결 |
|---|---|
| 독이 마지막 턴에도 피해를 줌(또는 한 번 덜 줄 거라 생각함) | 같은 이벤트가 결과와 횟수 줄이기를 둘 다 하면 결과가 먼저다. "피해 1, 3턴" = 피해 세 번 뒤 끝(2-2). |
| 보호막이 세 번째 적 공격을 못 막음 | 카드 던전의 `EndTurn`은 턴 끝을 쏜 다음에 적이 공격한다. 셀 이벤트를 턴 시작으로 둔다(5-3). |
| 다른 적이 맞을 때도 보호막이 발동 | 가진 쪽 필드가 비어 있다. 피해 의도의 `targetId`를 고른다. |
| 결과를 넣었는데 돌지 않음 / 검증 경고 "걸려 있는 동안 트리거가 없어 결과가 돌지 않습니다." | 트리거와 결과는 짝이다. 결과만 있고 트리거가 없거나, 그 반대다. 능력치 보정만 하는 상태 효과는 둘 다 비운다. |
| "[MomoiProject] 없는 상태 효과 'poisn'입니다." | ID 오타이거나 정의가 등록되지 않았다. ID는 `GameIds.Status` 상수로 쓴다. 세션을 직접 만들면 `session.Statuses.Define`이 필요하다(4-4). |
| "[MomoiProject] 상태 효과를 걸 대상의 id가 없습니다." | 대상 클래스에 `public string id;`가 없거나 id가 비었다. |
| 풀었더니 폭발하지 않음 | 끝날 때 결과는 지속이 다 돼서 끝날 때만 돈다. 풀어서 터뜨리려면 푸는 쪽에서 같은 결과를 함께 둔다. |
| 영구 상태 효과의 끝날 때 결과가 안 돎 | 영구는 풀 때만 끝나고, 풀 때는 끝날 때 결과가 돌지 않는다. 검증 경고로 보인다. |
| 전투가 끝났는데 분노가 남아 있음 | 상태 효과는 판(세션 구역) 동안 남는다. 전투를 끝내는 행동에서 푼다(5-2). |
| `Game.RemoveModifiers("rage")`로 분노 보정이 안 떨어짐 | 상태 효과의 보정 출처는 걸린 것의 id(`$status1`)다. `Game.RemoveStatus`로 푼다. |
| 상태 효과 결과 안에서 "체력이 절반 이하인 가진 쪽만" 조건이 안 먹음 | 트리거의 조건은 가진 쪽마다 따로 검사하지 않는다(조건 Block의 `Context.User`가 비어 있다). `[BindTo]` 코드에서 `c.User`를 보고 판단한다(4-3). |
| 상태 효과의 결과가 Undo되지 않음 | 트리거가 조건 충족만으로 되어 있어 행동이 끝난 뒤에 돌았다. 판 값을 바꾸는 상태 효과는 이벤트 받기·가로채기로 발동시킨다. |
| `[BindTo]`를 붙였는데 걸릴 때·끝날 때 결과가 그대로 돎 | 정상이다. 코드가 대신하는 것은 `outcomes`(걸려 있는 동안의 결과)뿐이다. 걸릴 때·끝날 때 결과는 DB에서 그대로 고친다(3-1). |
| 쓰러진 적에게 걸려 있던 상태 효과가 모니터에서 사라짐 | 정상이다. 대상을 데이터에서 뺀 행동이 끝날 때 그 행동 안에서 지운다(끝날 때 결과 없음, 2-3). Undo하면 돌아온다. |
| 능력치 보정이 든 상태 효과를 걸었더니 행동이 실패("…숫자 필드 '…'가 없어…") | `attribute` 오타이거나 그 대상 형식에 숫자 필드가 없다. 칸이 글자라 DB 검증이 잡지 못한다. |
| 예약한 행동이 다음 전투에서 실행됨 | 예약은 판 동안 남는다. 카드 던전은 전투 승리도 턴 끝을 쏜다. 전투를 끝내는 행동에서 `Game.CancelSchedules(출처)`(5-6). |
| 예약이 실행되지 않고 "대상이 없어 예약을 실행하지 않았습니다" | 실행할 때 대상 필드의 객체가 데이터에 없다(쓰러진 적). 정상 동작이다. |
| 이벤트 예약이 실패했는데 앞부분 변경이 남음 | 이벤트 예약은 이벤트를 받는 쪽처럼 돈다. 실패해도 이벤트를 쏜 행동은 계속되고, 예약된 행동이 실패 전까지 바꾼 것은 남는다. 실패할 일을 먼저 확인한다(2-5). |
| "영구 예약은 이벤트 예약(ScheduleOn)만 쓸 수 있습니다" | `ScheduleAfter`에 `permanent: true`를 줬다. 게임 시계는 판마다 다시 흐르므로 판을 넘는 시간 예약은 없다. 판을 넘는 예약은 이벤트 예약으로(2-5). |
| `Ask` 다음 줄이 고르기 전에 실행됨 | 행동은 멈추지 않는다. 고른 뒤의 일은 이어서 할 행동(`[Chosen]`이 있는 행동)에 쓴다. |
| "[MomoiProject] 이어서 할 행동 'X'에 고른 것을 받을 [Chosen] 필드가 없습니다." | 이어서 할 행동에 `[Chosen]` 필드를 둔다(4-9). |
| "…[Chosen] 필드는 string, id가 있는 클래스, 그 리스트·배열이어야 합니다." | `int`·id 없는 클래스 같은 형식에 붙였다. |
| 후보를 넘겼더니 "…에 string id 필드가 없어 개체로 찾거나 가리킬 수 없습니다." | 후보 객체의 클래스에 `id`가 없다. `public string id;`를 두거나 id 글자·`ChoiceCandidate`로 넘긴다(4-8). |
| 답하기가 "1개를 골라야 합니다(고른 것 0개)."로 실패 | 개수가 최소~최대 밖이다. 아무것도 바뀌지 않았고 선택은 그대로 기다린다. 화면이 메시지를 보여 주고 다시 고르게 한다. |
| 고른 카드가 답하기 전에 없어져 답하기가 실패("…을(를) 찾을 수 없습니다") | `[Chosen]`이 객체 칸이면 답하는 순간 다시 찾는다. 사라질 수 있는 후보는 `string` 칸으로 받고 이어서 할 행동에서 확인한다(4-9). |
| 답하기를 Undo했더니 고르기 창이 안 열림 | `ChoiceRequested`는 다시 오지 않는다. 창의 내용은 `GameView.Redraw(…, ChoiceSystem.Key)`로 `Game.Choice`에서 그린다(5-10). |
| 고르는 중에 다른 카드가 눌림 | 시스템은 다른 행동을 막지 않는다. 입력 창구가 `Game.IsWaiting`으로 막는다(5-10). |
| 선택 요구의 버튼에 id만 보임 | 후보 이름은 텍스트 표의 `<id>.name`이고 없으면 id다. DB 항목 ID면 이름 키를 채운다(23장 1절). 판 안의 개체(`card3`)는 이름 키가 없으므로 화면이 id로 객체를 찾아 이름을 그린다(5-10). 코드의 `Game.Ask`는 `label`로 이름을 줄 수 있다. |
| 유물의 선택 요구가 기다리지 않고 지나감 | 후보가 없었다. 유물·상태 효과에는 고른 대상이 없으므로 후보 칸에 대상 Block(`모든 적` 등)을 둔다. 전투 밖이면 적이 없어 후보도 없다. |
| 멀티플레이에서 다른 플레이어가 대신 답함 | `player`는 기록만 한다. 답할 플레이어 검사는 화면·서버가 한다. |
