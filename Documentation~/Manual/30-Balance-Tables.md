# 30. 밸런스 값·곡선·가중치 표·플레이어용 실패

**한 줄 요약**: 기획자가 고치는 숫자(시작 에너지 3, 층마다 체력 +10%)는 코드에 적지 않고 **DB › 밸런스 값**에 이름을 붙여 두고 `Game.Balance(ID)`로 읽는다. **곡선**을 붙이면 층·레벨·웨이브(**단계**)에 따라 값이 변한다(`Game.Balance(ID, 단계)`). 드롭·보상·상자처럼 "후보 중에서 무게만큼의 확률로 뽑기"는 **DB › 가중치 표**에서 정하고 `Game.Roll` 또는 결과 `가중치 표에서 주기`로 뽑는다. 난수는 표마다 따로인 결정론 흐름이라 Undo해도 같은 결과다. 플레이어에게 보일 실패("얼어 있어 움직일 수 없습니다")는 `throw Game.Fail(키, 기본 문장, 값…)`로 던진다. 행동 전체가 취소되고, 메시지는 텍스트 표에서 지금 언어로 만든다.

**언제 쓰는가**

- 시작 에너지·상점 가격·엘리트 확률처럼 플레이해 보며 자주 고치는 숫자(밸런스 값).
- "층마다 적 체력 +10%", "레벨 n에 필요한 경험치 = 100 × n^1.5"처럼 단계에 따라 변하는 값(곡선).
- "엘리트를 잡으면 60% 유물, 30% 물약 1~2개, 10% 꽝", "카드 보상 3장(서로 다른 카드)"(가중치 표).
- "에너지가 모자랍니다", "이미 연 상자입니다"를 언어마다 다르게 보여 줄 때(플레이어용 실패).

이 문서에서 쓰는 용어:

| 용어 | 뜻 |
|---|---|
| 밸런스 값 | DB › 밸런스 값의 항목 하나: 값(숫자) · 곡선(선택) · 메모. 코드 형식은 `BalanceData`(DB), `BalanceSystem`(실행 중). ID 상수는 `GameIds.Balance.X`. |
| 단계 | 곡선이 따라가는 수. 층·레벨·웨이브. `Game.Balance(ID, 단계)`의 두 번째 인자. |
| 기준 값 | 밸런스 값의 `value`. 곡선이 시작 단계에서 내는 값이다. |
| 곡선 (`Curve`) | 단계에 따라 값이 변하는 규칙: 방식 · 시작 단계 · 방식별 칸 · 반올림 · 최솟값·최댓값. |
| 가중치 표 | DB › 가중치 표의 항목 하나: 항목 줄(항목 · 무게 · 개수 · 조건) + 꽝 무게. 코드 형식은 `WeightTableData`(DB), `WeightTableDefinition`(실행 중). ID 상수는 `GameIds.WeightTable.X`. |
| 무게 (weight) | 뽑힐 몫. 확률 = 무게 ÷ (후보 무게 합 + 꽝 무게). 합이 1일 필요는 없다. |
| 꽝 | 아무것도 나오지 않는 몫(`emptyWeight`). |
| 안쪽 표 | 항목으로 넣은 다른 가중치 표. 뽑히면 그 표에서 다시 뽑는다(등급 표 → 등급별 카드 표). |
| 플레이어용 실패 (`GameFailure`) | 플레이어에게 보일 이유가 든 실패 예외: 텍스트 키 · 값 · 기본 문장. `InvalidOperationException`을 상속한다. |
| `FailureKeys` | 라이브러리가 던지는 플레이어용 실패의 텍스트 키 모음(비용이 모자람·선택 개수 …). |

---

## 1. 무엇으로 만드나

| 기획 문장 | 만드는 것 |
|---|---|
| "시작 에너지 3" | 밸런스 값 `startEnergy` = 3 → `Game.Balance`(4-1) |
| "층마다 적 체력 +10%" | 밸런스 값 + 곡선 `비율로`(5-1) |
| "레벨 n에 필요한 경험치 = 100 × n^1.5" | 곡선 `거듭제곱`(5-2) |
| "상점 카드 가격: 1층 50, 5층 75, 10층 150" | 곡선 `표`(5-3) |
| "엘리트: 60% 유물, 30% 물약 1~2개, 10% 꽝" | 가중치 표 + 안쪽 표 + 개수 + 꽝(5-4) |
| "카드 보상 3장(서로 다른 카드) 중 하나" | `Game.Roll(ID, 3, unique: true)` + 선택 대기(5-5) |
| "3층부터 드래곤 비늘이 나온다" | 항목 줄의 조건(5-6) |
| "드롭 확률을 화면에 보인다" | `session.Tables.Chances`(5-7) |
| "얼어 있어 2턴 동안 움직일 수 없습니다"(언어별) | `throw Game.Fail` + 텍스트 표(5-8) |
| 수치 수십 개를 시트에서 한꺼번에 고친다 | DB 목록의 CSV(5-9, `33-Tick-Preview-Csv.md`) |

> **규칙: 기획자가 고칠 숫자는 코드에 적지 않는다.** `battle.energy = 3`이 아니라 `battle.energy = (int)Game.Balance(GameIds.Balance.StartEnergy)`. 그러면 숫자를 바꾸는 데 프로그래머가 필요 없고, 플레이 중에도 고쳐 볼 수 있다(2-1).

> **규칙: 플레이어에게 보일 실패는 `Game.Fail`, 프로그래머의 실수는 `InvalidOperationException`.** "없는 카드 ID"·"전투 중이 아닌데 카드 내기"처럼 화면이 막았어야 하는 일은 플레이어에게 보일 글이 아니다.

---

## 2. 동작 규칙

### 2-1. 밸런스 값 읽기

- `Game.Balance(ID)`는 값을 돌려준다. `Game.Balance(ID, 단계)`는 곡선이 있으면 그 단계의 값, 곡선이 없으면(방식 `없음`) 값을 돌려준다.
- **바뀌지 않는 설정이다.** 플레이어 데이터가 아니라 Undo·판 저장·멀티플레이 동기화에 들어가지 않는다. 행동 밖(화면 코드)에서 읽어도 된다.
- 게임이 켜질 때(`GBibleRuntime.Start`) DB의 밸런스 값 전부가 세션에 등록된다. 서버·테스트처럼 세션을 직접 만들면 직접 등록한다(4-2).
- 없는 ID면 `InvalidOperationException`("[MomoiProject] 없는 밸런스 값 'enmyHp'입니다(DB › 밸런스 값).")으로 그 행동이 취소된다. ID는 `GameIds.Balance` 상수로 쓴다.
- **플레이 중 고치기**: 에디터에서 플레이하는 동안 DB에서 값·곡선을 고치면 저장(Ctrl+S) 전이라도 0.5초 안에 실행 중인 세션에 반영된다(가중치 표도 같다). 반영은 **다음에 읽을 때부터**다. 이미 읽어서 플레이어 데이터에 넣은 값(만들어 둔 적의 체력)은 바뀌지 않는다.

### 2-2. 곡선

| 방식 (`CurveKind`) | 값 | 예(기준 100, 시작 단계 1) |
|---|---|---|
| 없음(고정) (`None`, 기본) | 기준 | 늘 100 |
| 일정하게(+단계마다) (`Linear`) | 기준 + 더할 값 × (단계 − 시작) | +15 → 3단계 130 |
| 비율로(×단계마다) (`Exponential`) | 기준 × 비율^(단계 − 시작) | ×1.1 → 3단계 121 |
| 거듭제곱(단계^지수) (`Power`) | 기준 × (단계 ÷ 시작)^지수 | 지수 1.5 → 4단계 800 |
| 표(점 사이를 잇기) (`Table`) | 점 사이를 직선으로 잇는다. 첫 점 앞은 첫 점의 값, 마지막 점 뒤는 마지막 점의 값. **기준 값은 쓰지 않는다** | 점 (1, 10)·(5, 50) → 3단계 30, 9단계 50 |

- **시작 단계**(기본 1)에서 일정하게·비율로·거듭제곱의 값이 기준 값이다. 0층부터 세는 게임은 시작 단계를 0으로 둔다(거듭제곱은 0보다 커야 한다).
- 거듭제곱은 단계가 0 이하면 0으로 본다(값 0).
- 표의 점은 단계 순으로 다시 늘어놓고 쓴다(적은 순서는 상관없다). 같은 단계의 점이 둘이면 목록에서 뒤의 점이 쓰인다.
- 계산한 뒤 **반올림 → 자르기** 순서다.

| 반올림 (`CurveRounding`) | 뜻 |
|---|---|
| 그대로 (`None`, 기본) | 소수 그대로 |
| 반올림 (`Round`) | 0.5는 0에서 먼 쪽(2.5 → 3, −2.5 → −3) |
| 내림 (`Floor`) | 작은 정수로 |
| 올림 (`Ceiling`) | 큰 정수로 |

- **자르기**: `최솟값으로 자르기`를 켜면 최솟값보다 작은 값을 최솟값으로, `최댓값으로 자르기`를 켜면 최댓값보다 큰 값을 최댓값으로 만든다. 두 쪽은 따로 켠다(최댓값만 켜면 "3배에서 멈춘다", 최솟값만 켜면 "1 아래로는 내려가지 않는다"). 둘 다 켰는데 최솟값이 최댓값보다 크면 검증 오류다.

### 2-3. 가중치 표 뽑기

한 번 뽑기는 다음 순서로 돈다.

1. **후보**를 모은다: 항목이 있고 무게가 0보다 큰 줄 중, 조건이 비었거나 **지금** 참인 줄. 서로 다른 것만(`unique`)이면 이번 뽑기에서 이미 나온 항목은 빼고, 안쪽 표의 줄은 그 표에 아직 나올 것이 남아 있을 때만 후보다.
2. 합 = 후보의 무게 합 + 꽝 무게. 합이 0 이하면 아무것도 나오지 않는다.
3. 0~합의 난수로 하나를 고른다. 꽝 몫에 떨어지면 끝(아무것도 넣지 않는다).
4. 고른 줄의 **개수**를 정한다: 최소와 최대가 같으면 그 수, 다르면 최소~최대 중 하나. 그 개수만큼 되풀이한다: 항목이 다른 가중치 표면 그 표에서 1~4를 다시 하고, 아니면 결과에 그 ID를 넣는다.

- 횟수 n이면 이 한 번을 n번 한다. 결과는 ID 목록이다. 꽝이 나오면 횟수보다 적고, 개수가 2 이상인 줄이 나오면 많을 수 있다.
- **난수는 표마다 따로인 결정론 흐름**(`Game.Random("table:" + 표 ID)`, 07장)이다. 같은 시드·같은 행동이면 같은 결과가 나오고, Undo하면 난수도 돌아가 다시 해도 같은 드롭이 나온다(Undo로 다시 뽑기를 막는다). 표를 더하거나 다른 표를 뽑아도 이 표의 결과는 바뀌지 않는다. 안쪽 표는 그 표의 흐름을 쓴다.
- **행동 안에서 뽑는다.** 행동 밖에서 뽑으면 그 난수가 Undo·판 저장·멀티플레이와 어긋난다.
- **서로 다른 것만**: 한 번의 `Roll` 안에서 한 번 나온 항목은 다시 후보가 되지 않는다. 안쪽 표는 그 안에 아직 나올 것이 남아 있는 동안 후보로 남고, 그 안에서 이미 나온 항목만 빠진다(등급 표 → 등급별 카드 표에서도 서로 다른 카드). 다 나온 안쪽 표는 후보에서 빠지므로, 표 전체에 서로 다른 항목이 충분하면 늘 요청한 개수가 나온다(꽝 몫은 따로). 모자라면 있는 만큼만 나온다. 개수가 2 이상인 줄은 그 개수만큼 같은 ID가 들어간다.
- 표끼리 8단계 넘게 서로를 부르면 예외(`InvalidOperationException`, "가중치 표 'a'가 8단계 넘게 서로를 부릅니다(표끼리 돌고 있는지 확인하세요).")로 그 행동이 취소된다. DB 검증은 자기 자신을 넣은 표만 잡는다.
- **확률**(`session.Tables.Chances`, 5-7): 지금 상태로 본 후보마다 무게 ÷ (후보 무게 합 + 꽝 무게). 꽝 확률은 1 − (그 합)이다. 안쪽 표는 한 줄(그 표 ID)로 나온다. 서로 다른 것만·개수는 반영하지 않는다(한 번 뽑기의 확률).

### 2-4. 플레이어용 실패

- `throw Game.Fail(키, 기본 문장, (이름, 값)…)`. 행동 안에서 던지면 원래 규칙대로 그 행동이 실패하고 바꾼 것이 모두 취소된다(02장). 예외는 `Game.Do`를 부른 쪽(화면 코드)으로 온다.
- **메시지는 읽을 때마다 지금 언어로 만든다.** 텍스트 표에 그 키가 있으면 그 틀에 값을 넣고, 없으면 기본 문장(같은 `{값}` 틀 문법, `23-Text-Localization.md` 3절)을 쓰고, 둘 다 없으면 `[키]`다. 그래서 언어를 바꾼 뒤 같은 예외의 `Message`를 다시 읽으면 새 언어의 글이다.
- `GameFailure`는 `InvalidOperationException`을 상속한다. 지금까지의 `catch (InvalidOperationException e)`도 그대로 잡고, `e.Message`가 지금 언어의 글이다.
- 라이브러리가 던지는 플레이어용 실패도 이 형식이다. 그 키를 텍스트 표에 넣으면 번역된다.

| `FailureKeys` | 텍스트 키 | 값 | 기본 문장 | 던지는 곳 |
|---|---|---|---|---|
| `NotEnough` | `momoi.fail.notEnough` | `{what}` `{have}` `{need}` | "{what}이(가) 모자랍니다({have} / {need})." | `Game.Pay` · 결과 `비용 내기`(28장 2-4) |
| `ChooseExactly` | `momoi.fail.chooseExactly` | `{count}` `{picked}` | "{count}개를 골라야 합니다(고른 것 {picked}개)." | `Game.Answer`(29장 4-10) |
| `ChooseBetween` | `momoi.fail.chooseBetween` | `{min}` `{max}` `{picked}` | "{min}~{max}개를 골라야 합니다(고른 것 {picked}개)." | `Game.Answer` |
| `NotACandidate` | `momoi.fail.notACandidate` | `{item}` | "{item}은(는) 고를 수 있는 후보가 아닙니다." | `Game.Answer` |
| `ChosenTwice` | `momoi.fail.chosenTwice` | — | "같은 것을 두 번 골랐습니다." | `Game.Answer` |
| `ChoiceGone` | `momoi.fail.choiceGone` | — | "이미 답했거나 취소된 선택입니다." | `Game.Answer` |
| `CannotUse` | `momoi.fail.cannotUse` | `{ability}` `{condition}` | "{ability}: 사용 조건을 만족하지 않습니다({condition})." | `Game.Use`(사용 조건이 거짓이고 그 능력·아이템의 쓸 수 없을 때 글이 비었을 때, 16장) |

- `NotEnough`의 `{what}`은 낼 것의 이름이다. `Game.Pay(키, 경로, 양, what: "energy.name")`·결과 `비용 내기`의 `보일 이름`에 텍스트 키를 주면 그 키의 번역된 글, 글자를 주면 그 글자가 들어간다. 비우면 "플레이어 데이터의 이름 › 필드 경로"(예: "판 › battle.energy")라서 DB의 이름과 코드 경로가 플레이어에게 보인다 → 플레이어가 보는 비용에는 늘 이름을 준다(5-8). `{have}`·`{need}`는 이미 글자로 바꾼 숫자(소수 둘째 자리까지)다.
- `CannotUse`의 `{ability}`는 능력·아이템의 ID다. 틀이 그 ID를 지금 언어의 이름(텍스트 표의 `<ID>.name`, 없으면 ID)으로 바꾼다. `{condition}`은 사용 조건(Trigger)의 문장(DB의 자연어 요약, 한국어)이라 번역되지 않는다. 그래서 플레이어에게 보일 글은 능력·아이템마다 **쓸 수 없을 때 글**(DB › 능력 `failText`, 사용 기능 `Usable.failText`, 16장)로 정한다: "체력이 가득 차서 {ability}을(를) 쓸 수 없습니다."처럼. 텍스트 키(`fail.fullHp`)를 적으면 텍스트 표에서 번역되고, 키가 아닌 글이면 그 글을 틀로 쓴다(`{ability}`·`{condition}`을 넣을 수 있다). 정하면 `Game.Use`의 실패도 `Game.CanUse`의 이유도 그 글이고, 비우면 이 기본 글이다. 모든 능력에 같은 글을 쓰려면 `{condition}`을 뺀 틀을 `momoi.fail.cannotUse`로 원문 표에 넣는다.
- 텍스트 키로 준 `{what}`과 `{ability}`의 이름도 틀과 함께 **읽을 때마다 지금 언어로** 바뀐다. 언어를 바꾼 뒤 같은 예외의 `Message`를 다시 읽으면 이름까지 새 언어다.
- **프로그래머의 실수**(없는 ID·잘못된 필드 이름)는 그대로 `InvalidOperationException`("[MomoiProject] …")이다. 플레이어에게 보일 글이 아니다. `Game.Pay`에 없는 필드나 숫자가 아닌 필드를 주면 "모자랍니다"가 아니라 이것이다("[MomoiProject] 비용: 'run'에 필드 'battle.enrgy'가 없습니다(…).").

---

## 3. 에디터에서 쓰는 법

### 3-1. DB › 밸런스 값

`MomoiProject → DB` → 사이드바 **DB** 그룹의 **밸런스 값** 탭 → **+ 새로 만들기** → ID(예: `enemyHpScale`)를 짓는다. 저장하면 `GameIds.Balance.EnemyHpScale`이 생긴다. 칸 이름표는 변수 이름이고, 마우스를 올리면 `[FieldLabel]` 글자와 설명이 보인다.

| 칸 | 툴팁 | 값 |
|---|---|---|
| `value` | 값 / 곡선이 있으면 시작 단계의 값 | 숫자 |
| `curve` › `kind` | 곡선(단계별) › 방식 | `없음(고정)`(기본) / `일정하게(+단계마다)` / `비율로(×단계마다)` / `거듭제곱(단계^지수)` / `표(점 사이를 잇기)` |
| `curve` › `startX` | 시작 단계 | 기본 1. 일정하게·비율로·거듭제곱일 때만 보인다 |
| `curve` › `step` | 단계마다 더할 값 | 일정하게일 때만 |
| `curve` › `rate` | 단계마다 곱할 비율(1.1 = +10%) | 기본 1.1. 비율로일 때만 |
| `curve` › `power` | 지수 | 기본 1.5. 거듭제곱일 때만 |
| `curve` › `points` | 점 | 줄마다 `x`(단계) · `y`(값). 표일 때만 |
| `curve` › `rounding` | 반올림 | `그대로`(기본) / `반올림` / `내림` / `올림`. 방식이 `없음`이 아닐 때 |
| `curve` › `useMin` · `min` | 최솟값으로 자르기 · 최솟값 | 켜면 그 아래로 내려가지 않는다. `min`은 켰을 때만 보인다. 방식이 `없음`이 아닐 때 |
| `curve` › `useMax` · `max` | 최댓값으로 자르기 · 최댓값 | 켜면 그 위로 올라가지 않는다. `max`는 켰을 때만 보인다. 방식이 `없음`이 아닐 때 |
| `note` | 메모 | 기획 메모(게임에서 쓰지 않는다) |

- 방식을 바꾸면 그 방식의 칸만 보인다(묶음 필드 안의 `[ShowIf]`, `21-Editor-Database.md` 5절).
- 검증 오류(빌드를 막는다): "곡선: 거듭제곱은 시작 단계가 0보다 커야 합니다." / "곡선: 비율은 0보다 커야 합니다(1.1 = 단계마다 +10%)." / "곡선: 표에 점이 없습니다." / "곡선: 최솟값이 최댓값보다 큽니다."(두 쪽을 다 켰을 때)
- 플레이 중에 고치면 0.5초 안에 게임에 반영된다(2-1). 마음에 들면 저장(Ctrl+S), 아니면 사이드바의 **되돌리기**.

### 3-2. DB › 가중치 표

같은 그룹의 **가중치 표** 탭 → **+ 새로 만들기** → ID(예: `eliteDrop`). 저장하면 `GameIds.WeightTable.EliteDrop`이 생긴다.

| 칸 | 툴팁 | 값 |
|---|---|---|
| `entries` | 항목 | 항목 줄 목록(아래) |
| `entries` › `item` | 항목 / DB 항목(인스턴스·다른 가중치 표 …). 다른 가중치 표면 그 표에서 다시 뽑는다 | DB의 모든 종류의 항목에서 고른다(인스턴스·유물·능력·상태 효과·가중치 표 …) |
| `entries` › `weight` | 무게 | 0 이상(기본 1). 0이면 후보가 아니다 |
| `entries` › `minCount` · `maxCount` | 최소 개수 · 최대 개수 | 기본 1 · 1. 뽑히면 그 사이의 개수만큼 같은 ID가 들어간다(안쪽 표면 그만큼 다시 뽑는다) |
| `entries` › `condition` | 조건(맞을 때만 후보) | 조건 Block(`플레이어 데이터 검사` 등, `11-Triggers-Conditions.md`). 비우면 늘 후보 |
| `emptyWeight` | 꽝 무게 / 이 무게만큼 아무것도 나오지 않습니다(0이면 꽝 없음) | 0 이상 |

- 검증 오류: "항목이 없습니다." / "항목을 고르지 않은 줄이 있습니다." / "표가 자기 자신을 항목으로 가지고 있습니다." / "'healPotion': 무게는 0 이상이어야 합니다." / "'healPotion': 개수는 0 ≤ 최소 ≤ 최대여야 합니다." / "꽝 무게는 0 이상이어야 합니다." / "무게가 모두 0이라 아무것도 나오지 않습니다."
- 조건은 그 항목 줄을 후보로 볼지만 정한다. 조건이 거짓인 줄은 확률 계산(분모)에서도 빠진다.

### 3-3. 결과 `가중치 표에서 주기`

`+ 결과 추가` → 그룹 **결과** → `가중치 표에서 주기`. 결과 종류가 플레이어 데이터(`OutcomeKinds.PlayerData`)라서, `플레이어 데이터에 주기`를 고를 수 있는 칸(도전과제·능력·사용 기능·상태 효과·개발 명령 …)이면 나온다.

| 칸 | 툴팁 | 값 |
|---|---|---|
| `table` | 가중치 표 | DB › 가중치 표에서 고른다 |
| `count` | 횟수 | 1 이상(기본 1) |
| `unique` | 서로 다른 것만 | 켜면 한 번 나온 항목은 다시 나오지 않는다(2-3) |
| `key` | 넣을 플레이어 데이터 | 플레이어 데이터 |
| `path` | 필드(목록) | 그 안의 목록 필드(`relics`). 비우면 "값 전체" |

- 뽑은 ID들을 그 목록에 넣는다. 인스턴스 목록이면 그 인스턴스의 사본을, 글자 목록이면 ID를 넣는다(`플레이어 데이터에 주기`의 요소 넣기와 같다, 12장 2-2). 꽝이면 아무것도 넣지 않는다.
- 요약: `relicPool에서 뽑아 run › relics에 넣기`, 횟수가 2 이상이면 `cardReward에서 3개 뽑아 …`.
- 검증 오류: "가중치 표에서 주기: 표를 고르세요." / "가중치 표에서 주기: 넣을 플레이어 데이터를 고르세요." / "가중치 표에서 주기: 횟수는 1 이상이어야 합니다."
- 목록의 요소 형식이 표의 항목과 맞아야 한다. 카드 던전의 덱(`run.deck`, `List<DeckCard>`)에는 카드 원본(`Card` 인스턴스)이 들어가지 않는다(타임라인 경고 "인스턴스 'strike'의 형식(Card)이 DeckCard과(와) 다릅니다"). 덱에 넣는 일은 코드로 한다(5-5).

### 3-4. 실패 메시지 번역 — 텍스트 표

- `Game.Fail`의 키를 원문 표·다른 언어 표에 더한다(`23-Text-Localization.md` 1절, 시트로도). 틀 문법(값·조사·복수형)은 다른 문장과 같다.
- 원문 언어에서는 표에 키가 없으면 코드의 기본 문장이 쓰인다. 그래서 다른 언어 표에만 넣어도 된다. 다만 기본 문장은 코드에만 있으므로 번역가에게 시트로 보내려면 원문 표에 그 키를 기본 문장과 같은 글로 넣어 둔다.
- 라이브러리의 키(`momoi.fail.*`, 2-4 표)도 같다. 원문 표에 넣으면 한국어 문장도 바꿀 수 있다.

### 3-5. 모니터에서 확인하기

- 밸런스 값·가중치 표는 플레이어 데이터가 아니라서 **데이터** 탭에 없다. 뽑은 결과는 그 행동이 바꾼 값(`run › relics`에 넣은 ID)으로 보인다.
- 행동을 Undo한 뒤 같은 행동을 다시 하면 같은 드롭이 나오는 것을 볼 수 있다(2-3).
- 실패한 행동은 타임라인에 오류 "행동 실패, 변경 취소: 카드 내기 … — 얼어 있어 2턴 동안 움직일 수 없습니다."로 남는다(그때의 언어).

---

## 4. 코드로 쓰는 법 — 문법 요소를 하나씩

### 4-1. `Game.Balance` — 밸런스 값 읽기

```csharp
var energy = (int)Game.Balance(GameIds.Balance.StartEnergy);              // 값
var scale = Game.Balance(GameIds.Balance.EnemyHpScale, run.floor);        // 곡선으로 그 층의 값
```

| 멤버 | 뜻 |
|---|---|
| `double Balance(string id)` | 값(`value`). 곡선은 보지 않는다 |
| `double Balance(string id, double x)` | 곡선이 있으면 단계 `x`의 값, 없으면 값 |

- 돌려주는 값은 `double`이다. 정수가 필요하면 곡선의 반올림을 고르거나 `(int)`·`Math.Round`로 바꾼다.
- 값을 바꾸지 않으므로 행동 밖(화면 코드)에서 읽어도 된다. 없는 ID면 `InvalidOperationException`(2-1).

### 4-2. `session.Balance` — `BalanceSystem`

| 멤버 | 뜻 |
|---|---|
| `Define(string id, double value, Curve curve = null)` | 등록(같은 ID면 바꾼다). ID가 비면 무시 |
| `Has(id)` · `Ids` | 등록되어 있는지 · 등록된 ID 전부 |
| `Get(id)` · `Get(id, x)` | = `Game.Balance` |

게임(Unity)에서는 런타임이 DB의 밸런스 값 전부를 등록한다. 서버·테스트처럼 세션을 직접 만들면 직접 등록한다.

```csharp
foreach (var balance in GameDatabase.Load().All<BalanceData>()) balance.Define(session.Balance);

// DB 없이 코드로(테스트 등)
session.Balance.Define("startEnergy", 3);
session.Balance.Define("enemyHp", 40, new Curve { kind = CurveKind.Linear, step = 8 });   // 1층 40, 3층 56
```

`BalanceData`(DB 항목): `value` · `curve` · `note`, `ActiveCurve`(방식이 `없음`이면 `null`), `Define(BalanceSystem)`(그 세션에 등록).

### 4-3. `Curve` — 코드에서 곡선 쓰기

`Curve`는 코어 클래스라 밸런스 값 밖에서도 쓴다.

```csharp
var exp = new Curve { kind = CurveKind.Power, power = 1.5, rounding = CurveRounding.Round };
double need = exp.Evaluate(100, 4);          // 800
string text = exp.Preview(100, 5);           // "1 → 100 · 2 → 283 · 3 → 520 · 4 → 800 · 5 → 1118"
```

| 필드 · 멤버 | 뜻 |
|---|---|
| `kind` (`CurveKind`) | `None` · `Linear` · `Exponential` · `Power` · `Table` |
| `startX` | 시작 단계(기본 1) |
| `step` · `rate` · `power` | 일정하게의 더할 값 · 비율로의 비율(기본 1.1) · 거듭제곱의 지수(기본 1.5) |
| `points` (`List<CurvePoint>`) | 표의 점(`x` 단계 · `y` 값) |
| `rounding` (`CurveRounding`) | `None` · `Round` · `Floor` · `Ceiling` |
| `useMin` · `min` | 켜면 `min`보다 작은 값을 `min`으로 |
| `useMax` · `max` | 켜면 `max`보다 큰 값을 `max`로 |
| `double Evaluate(double baseValue, double x)` | 기준 값 `baseValue`에서 단계 `x`의 값(2-2) |
| `string Preview(double baseValue, int count = 5)` | 시작 단계부터 `count`개의 "단계 → 값" 글(툴팁·로그용) |
| `Validate(IValidationContext context, string label)` | 3-1의 검증. 프로젝트의 DB 종류에 곡선 칸을 둘 때 `ValidateData`에서 부른다 |

### 4-4. `Game.Roll` — 가중치 표에서 뽑기

```csharp
string drop = Game.Roll(GameIds.WeightTable.ChestDrop);                          // 하나(꽝이면 null)
List<string> cards = Game.Roll(GameIds.WeightTable.CardReward, 3, unique: true); // 3번, 서로 다른 것
```

| 멤버 | 뜻 |
|---|---|
| `string Roll(string tableId)` | 한 번 뽑아 **첫** ID를 돌려준다. 꽝이면 `null`. 개수가 2 이상인 줄이 나와도 하나만 돌려준다(그때 표마다 한 번 타임라인 경고, 5-4) |
| `List<string> Roll(string tableId, int count, bool unique = false)` | `count`번 뽑은 ID 목록(꽝은 빠진다). `unique`면 서로 다른 것만(2-3) |

- 행동 안에서 부른다(난수가 행동과 함께 되돌아간다).
- 없는 표면 `InvalidOperationException`("[MomoiProject] 없는 가중치 표 'eliteDorp'입니다(DB › 가중치 표).").

### 4-5. `session.Tables` — `WeightTableSystem` · `WeightTableDefinition` · `WeightEntry`

| `session.Tables` | 뜻 |
|---|---|
| `Define(WeightTableDefinition table)` | 등록(같은 ID면 바꾼다) |
| `Has(id)` · `Find(id)` | 등록되어 있는지 · 정의(없으면 `null`) |
| `List<string> Roll(id, count = 1, unique = false)` | = `Game.Roll`의 목록 형태 |
| `IReadOnlyList<(string item, double chance)> Chances(id)` | 항목별 확률(조건은 지금 상태, 꽝을 포함한 분모). 없는 표면 빈 목록 |

| `WeightTableDefinition` | 뜻 | DB 칸 |
|---|---|---|
| `Id` | 표 ID | 항목 ID |
| `EmptyWeight` | 꽝 무게 | `emptyWeight` |
| `Entries` (`List<WeightEntry>`) | 항목 줄 | `entries` |

| `WeightEntry` 필드 | 뜻 |
|---|---|
| `item` | 항목 ID(다른 표의 ID면 안쪽 표) |
| `weight` | 무게(기본 1) |
| `minCount` · `maxCount` | 개수(기본 1 · 1) |
| `condition` (`Condition`) | 맞을 때만 후보(`null`이면 늘) |

```csharp
foreach (var table in GameDatabase.Load().All<WeightTableData>()) session.Tables.Define(table.ToDefinition());

// DB 없이 코드로
var drop = new WeightTableDefinition { Id = "eliteDrop", EmptyWeight = 1 };
drop.Entries.Add(new WeightEntry { item = "relicPool", weight = 6 });
drop.Entries.Add(new WeightEntry { item = "healPotion", weight = 3, minCount = 1, maxCount = 2 });
session.Tables.Define(drop);
```

`WeightTableData.ToDefinition()`은 DB 항목을 정의로 바꾼다(항목을 고르지 않은 줄은 빼고, 꽝 무게는 0 이상으로).

### 4-6. `RollTableOutcome` — 결과 '가중치 표에서 주기'를 코드로

```csharp
var reward = new RollTableOutcome { table = "relicPool", count = 1, key = "run", path = "relics" };
```

| 필드 | 뜻 |
|---|---|
| `table` | 가중치 표 ID |
| `count` | 횟수(1보다 작으면 1) |
| `unique` | 서로 다른 것만 |
| `key` · `path` | 넣을 플레이어 데이터 · 목록 필드 경로 |
| `Kind` | `OutcomeKinds.PlayerData` |

### 4-7. `Game.Fail` · `GameFailure` · `FailureKeys`

```csharp
throw Game.Fail("fail.frozen", "얼어 있어 {turns}턴 동안 움직일 수 없습니다.", ("turns", 2));   // 기본 문장과 함께
throw Game.Fail("fail.tooFar", ("range", 3));                                                  // 기본 문장 없이(표에 키가 있어야 한다)
```

| 멤버 | 뜻 |
|---|---|
| `GameFailure Fail(string key, params (string name, object value)[] args)` | 기본 문장 없이 만든다. 표에도 키가 없으면 메시지가 `[키]` |
| `GameFailure Fail(string key, string fallback, params (string name, object value)[] args)` | 기본 문장(`fallback`, `{값}` 틀)과 함께 만든다 |

- `Game.Fail`은 예외를 **만들기만** 한다. `throw`를 붙인다.
- 값은 다른 문장 틀처럼 이름 붙인 튜플이다(`23-Text-Localization.md` 2절). DB ID를 값으로 넘기면 틀의 `{item}` 자리에 그 이름(`<ID>.name`)이 들어간다.

| `GameFailure` 멤버 | 뜻 |
|---|---|
| `Key` | 텍스트 키 |
| `Args` (`IReadOnlyDictionary<string, object>`) | 값 |
| `Fallback` | 기본 문장(없으면 `null`) |
| `Message` · `Text()` | 지금 언어의 글(읽을 때마다 다시 만든다, 2-4) |
| `new GameFailure(key, args = null, fallback = null)` | 직접 만들 때(`Game.Fail`과 같다) |

`FailureKeys`: `NotEnough` · `ChooseExactly` · `ChooseBetween` · `NotACandidate` · `ChosenTwice` · `ChoiceGone` · `CannotUse`(2-4 표). `const string`이라 텍스트 표 도구 코드에서도 쓴다.

`Game.Pay(string key, string path, double amount, string what = null)`의 `what`은 모자랄 때 실패 글의 `{what}`에 들어갈 이름이다(텍스트 키 또는 글, 28장 4-12).

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
[Serializable] public class MonsterData { public string title; public int maxHp = 20; public List<int> attackPattern = new List<int> { 6 }; }
[Serializable] public class Potion { public string title; public int maxStack = 2; }

// 플레이어 데이터 "run"(RunState, 세션 구역): floor · gold · hero · deck · battle(전투 밖이면 null) · relics(유물 ID 목록) · potions(인벤토리)
// 플레이어 데이터 "profile"(Profile, 메타 구역)
// Rules.NewEnemy(game, monster): 몬스터 원본으로 적을 만든다. Rules.CardData(game, ID) · Rules.PotionOf(game, ID): 원본 읽기.
// WinBattle(전투 승리): 골드를 주고 다음 층으로.
```

- ID 상수(`GameIds.Balance.EnemyHpScale`, `GameIds.WeightTable.EliteDrop` …)는 DB에 항목을 만들고 저장하면 생긴다.
- 5-2는 `Profile`에 `public int level = 1, exp;`가 있다고 본다(가이드 예제에는 없다).

### 5-1. 층별 적 체력 곡선

몬스터마다 체력이 다르므로(고블린 20, 드래곤 …) 층마다의 **배율**을 밸런스 값으로 둔다.

**에디터**(DB › 밸런스 값 `enemyHpScale`): `value` `1`, `curve` › `kind` `비율로(×단계마다)`, `startX` `1`, `rate` `1.1`, `useMax` 켬 · `max` `3`(3배에서 멈춘다).

**코드** — 적을 만드는 규칙 함수가 배율을 곱한다.

```csharp
public static Enemy NewEnemy(GameApi game, string monster)
{
    var data = MonsterOf(game, monster) ?? throw new InvalidOperationException($"몬스터 '{monster}'이(가) DB에 없습니다.");
    var floor = Math.Max(1, game.Data().Run.floor);
    var hp = (int)Math.Round(data.maxHp * game.Balance(GameIds.Balance.EnemyHpScale, floor));
    return new Enemy { id = game.NewId("enemy"), name = data.title, hp = hp, maxHp = hp, monster = monster };
}
```

| 층 | 배율 | 고블린(20) |
|---|---|---|
| 1 | 1 | 20 |
| 2 | 1.1 | 22 |
| 3 | 1.21 | 24 |
| 5 | 1.4641 | 29 |
| 10 | 2.358 | 47 |
| 13 이상 | 3(최댓값) | 60 |

- 모든 적에 같은 체력을 주는 게임이면 체력 자체를 곡선으로 둔다: `enemyHp` `value` `40`, `kind` `일정하게(+단계마다)`, `step` `8` → 1층 40, 2층 48, 3층 56.
- 플레이 중 `rate`를 1.15로 고쳐 보면 **다음에 만드는 적**부터 바뀐다. 지금 싸우는 적의 체력은 이미 판 데이터에 들어 있다.

### 5-2. 레벨별 경험치 — 거듭제곱

**에디터**(DB › 밸런스 값 `expToNext`): `value` `100`, `kind` `거듭제곱(단계^지수)`, `startX` `1`, `power` `1.5`, `rounding` `반올림`.

| 레벨 | 100 × 레벨^1.5 | 다음 레벨까지 |
|---|---|---|
| 1 | 100 | 100 |
| 2 | 282.84 | 283 |
| 3 | 519.62 | 520 |
| 4 | 800 | 800 |
| 5 | 1118.03 | 1118 |

```csharp
[Serializable, BlockInfo("경험치 얻기")]
public class GainExp : GameAction
{
    public int amount;

    protected override void Run()
    {
        var profile = Game.Data().Profile;
        profile.exp += amount;
        while (profile.exp >= Game.Balance(GameIds.Balance.ExpToNext, profile.level))
        {
            profile.exp -= (int)Game.Balance(GameIds.Balance.ExpToNext, profile.level);   // 반올림을 골라 정수다
            profile.level++;
        }
    }
}

// 화면: "다음 레벨까지 217"
var need = Game.Balance(GameIds.Balance.ExpToNext, profile.level) - profile.exp;
```

- 지수를 1.5에서 1.8로 바꾸면 후반이 가팔라진다. 1이면 일정하게(+100씩)와 같다.

### 5-3. 상점 가격 — 표 곡선

"1층 50, 5층 75, 10층 150. 그 사이는 고르게."

**에디터**(DB › 밸런스 값 `shopCardPrice`): `value`는 쓰지 않는다(0 그대로), `kind` `표(점 사이를 잇기)`, `points` (1, 50) · (5, 75) · (10, 150), `rounding` `반올림`.

| 층 | 가격 |
|---|---|
| 1 | 50 |
| 3 | 62.5 → 63 |
| 5 | 75 |
| 8 | 120 |
| 12 | 150(마지막 점 뒤는 끝 값) |

```csharp
[Serializable, BlockInfo("카드 사기")]
public class BuyCard : GameAction
{
    public string source;     // 살 카드 원본 ID

    protected override void Run()
    {
        var run = Game.Data().Run;
        var price = (int)Game.Balance(GameIds.Balance.ShopCardPrice, run.floor);
        if (run.gold < price) throw Game.Fail("fail.noGold", "골드가 모자랍니다({gold} / {price}).", ("gold", run.gold), ("price", price));
        run.gold -= price;
        run.deck.Add(Rules.NewCard(Game, source));
    }
}
```

- 가격을 행동 안에서 계산한다. 화면이 가격을 행동에 넣어 보내면(`price` 필드) 멀티플레이에서 상대가 값을 속일 수 있다. 화면은 같은 `Game.Balance`로 표시만 한다.

### 5-4. 엘리트 드롭 — 꽝 · 안쪽 표 · 개수

"엘리트를 잡으면 60% 유물(유물 풀에서 하나), 30% 회복 물약 1~2개, 10% 꽝."

**에디터**:

1. DB › 가중치 표 `relicPool`: 항목 `anchor` 무게 `1`, `burningBlood` 무게 `1`.
2. DB › 가중치 표 `eliteDrop`:

| `item` | `weight` | `minCount` | `maxCount` |
|---|---|---|---|
| `relicPool`(가중치 표) | 6 | 1 | 1 |
| `healPotion`(인스턴스) | 3 | 1 | 2 |

   `emptyWeight` `1`.

**코드** — 엘리트 전투를 이긴 행동 안에서.

```csharp
var run = Game.Data().Run;
foreach (var id in Game.Roll(GameIds.WeightTable.EliteDrop, 1))     // 물약은 1~2개라 목록으로 받는다
{
    if (Rules.PotionOf(Game, id) is Potion potion) run.potions.Add(id, 1, potion.maxStack);
    else run.relics.Add(id);                                         // relicPool에서 나온 유물 ID
}
```

| 나온 것 | 확률 | 결과 |
|---|---|---|
| 유물 | 6 / 10 | `anchor` 또는 `burningBlood` 하나(각 30%) |
| 물약 | 3 / 10 | `healPotion` 1개 또는 2개 |
| 꽝 | 1 / 10 | 빈 목록 |

- `Game.Roll(id)`(하나)는 첫 ID만 돌려주므로 물약이 2개 나와도 하나만 받는다. 개수가 2 이상인 줄이 있는 표는 목록 형태 `Game.Roll(id, 1)`로 받는다. 하나 형태로 뽑았는데 실제로 2개 이상이 나오면 타임라인에 경고 "가중치 표 'eliteDrop'에서 개수가 2 이상인 항목이 나와 Game.Roll(id)은 첫 ID만 돌려줍니다. 개수까지 쓰려면 Game.Roll(id, 1)로 목록을 받으세요."가 표마다 한 번 남는다(세션마다).
- Undo하고 다시 이겨도 같은 드롭이다(2-3). 유물 풀에 유물을 더해도 `eliteDrop`의 "유물이냐 물약이냐"는 그대로이고, 유물 풀 안의 결과만 바뀐다.
- 코드 없이 유물만 주려면 결과 `가중치 표에서 주기`(`table` `relicPool`, `key` `run`, `path` `relics`)를 쓴다. 예: DB › 개발 › 개발 명령 "유물 하나 받기"(22장 7절). `relics`는 글자 목록이라 유물 ID가 들어간다.

### 5-5. 카드 보상 3장 — 서로 다른 것

"등급별 확률로 서로 다른 카드 3장을 보이고, 하나를 고르거나 건너뛴다."

**에디터**:

1. DB › 가중치 표 `commonCards`: `strike` · `defend` · …(무게 1씩). `rareCards`: `fireball` · `chainLightning`(무게 1씩).
2. DB › 가중치 표 `cardReward`: 항목 `commonCards` 무게 `7`, `rareCards` 무게 `3`.

**코드** — 29장 5-8의 카드 보상에서 후보를 표로 뽑는다.

```csharp
// WinBattle.Run 끝에
var cards = Game.Roll(GameIds.WeightTable.CardReward, 3, unique: true);     // 서로 다른 카드 원본 ID 3개
Game.Ask("카드 보상", cards, new TakeCardReward(), min: 0, max: 1);         // 후보 = ID 글자, 이름은 텍스트 표의 '<ID>.name'

[Serializable, BlockInfo("카드 보상 받기")]
public class TakeCardReward : GameAction
{
    [Chosen] public string card;        // 고른 원본 ID(건너뛰면 null)

    protected override void Run()
    {
        if (string.IsNullOrEmpty(card)) return;
        Game.Data().Run.deck.Add(Rules.NewCard(Game, card));                  // 덱에는 DeckCard를 넣는다(원본 사본이 아니다)
    }
}
```

- 서로 다른 것만이라 3장이 모두 다르다. 안쪽 표(`commonCards`·`rareCards`)는 계속 후보로 남고 그 안에서 이미 나온 카드만 빠진다.
- `rareCards`에 카드가 2장뿐이어도 3장이 나온다. 두 장이 다 나오면 `rareCards`는 후보에서 빠지고 세 번째는 `commonCards`에서 나온다(2-3).
- 판을 저장하고 끄면 고르기 화면에서 이어진다(선택 대기는 플레이어 데이터, 29장 2-6).

### 5-6. 조건 붙은 항목 — 3층 이상

"드래곤 비늘(유물)은 3층부터 엘리트 드롭에 나온다."

**에디터**: 5-4의 `eliteDrop`에 줄을 더한다: `item` `dragonScale`, `weight` `2`, `condition` `+ 블럭` → `플레이어 데이터 검사` → 플레이어 데이터 `run`, 조건 `floor` `이상` `3`.

| 층 | 유물 풀 | 물약 | 드래곤 비늘 | 꽝 |
|---|---|---|---|---|
| 1~2 | 6/10 = 60% | 30% | — | 10% |
| 3 이상 | 6/12 = 50% | 25% | 2/12 ≈ 17% | 1/12 ≈ 8% |

- 조건이 거짓이면 그 줄은 분모에서도 빠진다. 그래서 3층부터 다른 줄의 확률이 조금씩 준다. 다른 확률을 그대로 두고 싶으면 꽝 무게를 그만큼 줄인 표를 따로 둔다.
- 조건은 뽑는 **순간**의 상태로 본다(행동 안이면 그 행동이 바꾼 값까지).
- 코드로: `new WeightEntry { item = "dragonScale", weight = 2, condition = new PlayerDataCondition { key = "run", filter = new FieldFilter { path = "floor", op = FieldOp.AtLeast, value = "3" } } }`.

### 5-7. 드롭 확률 표시 — `Chances`

엘리트 아이콘에 마우스를 올리면 "유물 50% · 회복 물약 25% · 드래곤 비늘 17% · 없음 8%".

```csharp
public static string DropText(GameSession session, string table)
{
    var chances = session.Tables.Chances(table);
    var lines = chances.Select(c => $"{Texts.NameOf(c.item)} {c.chance:P0}").ToList();   // 안쪽 표는 그 표의 이름 한 줄
    var nothing = 1 - chances.Sum(c => c.chance);
    if (nothing > 0.0001) lines.Add($"{Texts.Get("drop.nothing")} {nothing:P0}");
    return string.Join(" · ", lines);
}

// 툴팁: 층이 바뀌면 확률도 바뀌므로 판 데이터가 바뀔 때 다시 만든다
_view = GameView.Redraw(() => tooltip.text = DropText(GBibleRuntime.Session, GameIds.WeightTable.EliteDrop), GameIds.PlayerData.Run.Id);
```

- 안쪽 표 안의 확률까지 보이려면 곱한다: `foreach (var (inner, p) in session.Tables.Chances("relicPool")) … 0.5 × p`.
- `Chances`는 한 번 뽑기의 확률이다. `Roll(id, 3, unique: true)`의 "적어도 한 장이 희귀일 확률" 같은 것은 직접 계산한다.
- 이름 `Texts.NameOf(ID)`는 텍스트 표의 `<ID>.name`이다(없으면 ID). 가중치 표 자신의 이름(`relicPool.name` = "유물")도 같은 키로 둔다.

### 5-8. 실패 메시지와 번역 — `Game.Fail` · `FailureKeys`

**규칙 쪽** — 카드를 내는 행동에서 플레이어에게 보일 실패와 프로그래머 실수를 나눈다.

```csharp
[Serializable, BlockInfo("카드 내기")]
public class PlayCard : GameAction
{
    public DeckCard card;
    public Fighter target;

    protected override void Run()
    {
        var run = Game.Data().Run;
        var battle = run.battle ?? throw new InvalidOperationException("전투 중이 아닙니다.");         // 화면이 막았어야 한다(프로그래머 실수)
        if (!battle.hand.Contains(card.id)) throw new InvalidOperationException("손에 없는 카드입니다.");

        var frozen = Game.Statuses(run.hero).FirstOrDefault(s => s.status == GameIds.Status.Frozen);
        if (frozen != null)
            throw Game.Fail("fail.frozen", "얼어 있어 {turns}턴 동안 움직일 수 없습니다.", ("turns", frozen.remaining));   // 플레이어에게 보일 실패

        var data = Rules.CardData(Game, card.card);
        Game.Pay(GameIds.PlayerData.Run.Id, "battle.energy", data.cost, what: "energy.name");    // 모자라면 FailureKeys.NotEnough({what} = 에너지)
        battle.hand.Remove(card.id);
        battle.discard.Add(card.id);
        if (target == null) Game.Use(card.card, run.hero);
        else Game.Use(card.card, run.hero, target);
    }
}
```

**텍스트 표**:

| 키 | 원문 표(ko) | 영어 표(en) |
|---|---|---|
| `fail.frozen` | (비움 — 기본 문장이 쓰인다) | `Frozen for {turns, plural, one {# turn} other {# turns}}.` |
| `energy.name` | `에너지` | `energy` |
| `momoi.fail.notEnough` | `{what:이} 모자랍니다({have}/{need}).` | `Not enough {what} ({have}/{need}).` |

- `what: "energy.name"`이라 `{what}`에 "에너지"/"energy"가 들어간다. 골드를 내는 곳은 `what: "gold.name"`처럼 그 이름을 준다. 비우면 "판 › battle.energy"가 보인다(2-4).
- 원문 표에 `momoi.fail.notEnough`를 넣지 않으면 기본 문장 "{what}이(가) 모자랍니다({have} / {need})."가 쓰인다. 조사를 맞추려면 위처럼 `{what:이}`로 넣는다.
- 선택 대기의 실패(`momoi.fail.chooseExactly` …)와 사용 조건 실패(`momoi.fail.cannotUse`, 또는 능력의 쓸 수 없을 때 글에 적은 키)도 같은 방식으로 번역한다.

**화면 쪽** — 플레이어용 실패는 그대로 보여 주고, 그 밖의 실패는 개발자에게 알린다.

```csharp
public bool TryPlay(DeckCard card, Fighter target, out string error)
{
    error = null;
    try
    {
        Game.Do(new PlayCard { card = card, target = target });
        return true;
    }
    catch (GameFailure f)                       // 먼저 잡는다(InvalidOperationException을 상속한다)
    {
        error = f.Message;                      // 지금 언어의 글. 바꾼 것은 이미 모두 취소되어 있다
        return false;
    }
    catch (InvalidOperationException e)
    {
        Debug.LogException(e);                  // 프로그래머 실수: 플레이어에게 보일 글이 아니다
        error = Texts.Get("ui.cannotPlay");
        return false;
    }
}
```

- 알림 창이 떠 있는 동안 언어를 바꾸면 글자도 바뀌게 하려면, 글자 대신 `GameFailure`를 들고 있다가 다시 그릴 때 `f.Message`를 다시 읽는다.
- 버튼을 미리 흐리게 하고 이유를 툴팁으로 보이려면 `Game.CanDo`(`33-Tick-Preview-Csv.md` 2-2).

### 5-9. CSV로 밸런스 값 한꺼번에 조정

밸런스 값이 수십 개가 되면 시트에서 한 번에 고치는 편이 빠르다.

1. DB › 밸런스 값 탭 → 맨 위 폴더 줄의 **CSV** → **CSV로 내보내기…** → 파일을 저장한다(UTF-8).
2. 구글 시트·엑셀에서 연다. 한 줄 = 항목 하나(첫 칸 `id`), 칸 = `value` · `curve.kind` · `curve.startX` · `curve.step` · `curve.rate` · `curve.power` · `curve.rounding` · `curve.useMin` · `curve.min` · `curve.useMax` · `curve.max` · `note`. 표의 점(`curve.points`)은 점이 있는 항목이 있으면 점마다 칸이 된다(`curve.points[0].x` · `curve.points[1].x` … · `curve.points[0].y` …). 아래 셋은 표 곡선이 아니라 점 칸이 없다.

```
id,value,curve.kind,curve.startX,curve.step,curve.rate,curve.power,curve.rounding,curve.useMin,curve.min,curve.useMax,curve.max,note
enemyHpScale,1,Exponential,1,0,1.15,1.5,None,false,0,true,3,층마다 +15%
expToNext,100,Power,1,0,1.1,1.8,Round,false,0,false,0,
startEnergy,3,None,1,0,1.1,1.5,None,false,0,false,0,
```

3. 고친 뒤 CSV로 다시 받아 **CSV 가져오기…** → 결과 창("3개 고침, 0개 새로 만듦(적용 전 — Ctrl+S로 적용).")을 확인하고 저장(Ctrl+S)한다. 마음에 안 들면 **되돌리기**.

- `curve.kind`에는 코드 이름(`Exponential`)과 DB에 보이는 이름(`비율로(×단계마다)`)을 모두 적을 수 있다. 내보낼 때는 코드 이름이다.
- 가중치 표 탭도 같다. 항목 줄은 한 줄씩 나란히 `entries[0].item` · `entries[0].weight` · `entries[0].minCount` · `entries[0].maxCount` · `entries[1].item` …, 꽝 무게는 `emptyWeight` 칸이다. 줄의 조건(Block)은 나오지 않으므로 DB에서 고친다. 시트에서 번호를 늘려 항목 줄을 더하면 새 항목 줄은 무게 1, 개수 1~1에서 시작한다(`33-Tick-Preview-Csv.md` 2-3).
- 칸 규칙·가져오기 결과·인스턴스(카드 비용 …) 내보내기는 `33-Tick-Preview-Csv.md` 3절.

---

## 6. 주의할 점 · 자주 하는 실수

| 증상 | 원인과 해결 |
|---|---|
| "[MomoiProject] 없는 밸런스 값 '…'입니다(DB › 밸런스 값)." | ID 오타이거나 저장(Ctrl+S) 전 항목이다. ID는 `GameIds.Balance` 상수로 쓴다. 세션을 직접 만들면 `session.Balance.Define`이 필요하다(4-2). |
| 곡선을 붙였는데 층이 올라도 값이 그대로 | 단계 없이 `Game.Balance(id)`로 읽었다(곡선을 보지 않는다). `Game.Balance(id, 층)`으로 읽는다. 또는 방식이 `없음`이다. |
| 1층 값이 기준 값과 다름 | 시작 단계가 1이 아니다. 0층부터 세면 시작 단계를 0으로(거듭제곱은 0보다 커야 한다). |
| 표 곡선에 적은 기준 값이 무시됨 | 표는 기준 값을 쓰지 않는다. 점의 값이 곧 값이다. |
| 최솟값·최댓값을 적었는데 자르지 않음 | `최솟값으로 자르기`·`최댓값으로 자르기`를 켜지 않았다. 자를 쪽을 켠다(2-2). |
| 소수가 나와 체력이 1.21 | 곡선은 `double`이다. 반올림을 고르거나 코드에서 `(int)Math.Round`. |
| 플레이 중 고친 값이 지금 적에게 안 보임 | 반영은 다음에 읽을 때부터다. 이미 만든 적의 체력은 판 데이터에 들어 있다. |
| 플레이 중 고친 값이 플레이를 끄니 사라짐 | DB의 작업본이다. 저장(Ctrl+S)해야 에셋에 남는다. |
| 물약이 1~2개 나와야 하는데 늘 1개 / 경고 "…Game.Roll(id)은 첫 ID만 돌려줍니다. 개수까지 쓰려면 Game.Roll(id, 1)로 목록을 받으세요." | `Game.Roll(id)`(하나)는 첫 ID만 돌려준다(2개 이상이 나오면 표마다 한 번 이 경고). `Game.Roll(id, 1)`의 목록으로 받는다(5-4). |
| 서로 다른 3장인데 2장만 나옴 | 꽝이 나왔거나, 표 전체(조건이 맞는 줄)에 서로 다른 항목이 3개보다 적다. 꽝 무게·조건·항목 수를 본다(2-3). |
| 서로 다른 것만인데 같은 ID가 둘 | 그 줄의 개수(최소~최대)가 2 이상이다. 개수만큼은 같은 ID가 들어간다. |
| Undo하고 다시 해도 같은 드롭 | 정상이다. 난수가 행동과 함께 돌아간다(2-3). |
| "가중치 표 '…'가 8단계 넘게 서로를 부릅니다" | 표 A가 B를, B가 A를 항목으로 가진다. DB 검증은 자기 자신만 잡으므로 표끼리의 고리를 끊는다. |
| 표시한 확률과 실제 횟수가 다름 | `Chances`는 지금 조건으로 본 한 번 뽑기의 확률이다. 안쪽 표는 한 줄, 서로 다른 것만·개수는 반영하지 않는다(2-3). 적은 횟수에서는 확률대로 나오지 않는다. |
| 3층이 됐는데 조건 줄이 안 나옴 | 조건의 플레이어 데이터·필드가 틀렸거나, 층을 올리는 행동보다 먼저 뽑았다. 조건은 뽑는 순간의 상태다. |
| `가중치 표에서 주기`로 덱에 카드가 안 들어감(경고 "인스턴스 'strike'의 형식(Card)이 DeckCard과(와) 다릅니다") | 덱은 `DeckCard` 목록이라 원본 사본이 들어가지 않는다. `Game.Roll`로 뽑아 코드에서 `Rules.NewCard`로 넣는다(5-5). |
| 드롭이 Undo·이어하기 뒤 달라짐 | 행동 밖(화면 코드)에서 `Game.Roll`을 불렀다. 행동 안에서 부른다. |
| 실패 메시지가 `[fail.frozen]` | 기본 문장 없이 `Game.Fail(키, 값…)`을 썼고 표에도 키가 없다. 기본 문장을 함께 적거나 원문 표에 키를 넣는다. |
| 언어를 바꿨는데 실패 메시지가 그대로 | 그 언어 표에 키가 없다(기본 문장이 쓰인다). 또는 `Message`를 글자로 저장해 두었다. `GameFailure`를 들고 다시 읽는다(5-8). |
| "판 › battle.energy이(가) 모자랍니다" | 이름을 주지 않았다. `Game.Pay(…, what: "energy.name")` 또는 결과 `비용 내기`의 `보일 이름`에 텍스트 키·글을 적는다(2-4, 5-8). |
| `Game.Pay`가 "모자랍니다" 대신 "[MomoiProject] 비용: '…'에 필드 '…'가 없습니다" | 경로 오타이거나 숫자가 아닌 필드다. 프로그래머 실수라 플레이어용 실패가 아니다. 경로를 고친다. |
| 테스트에서 `Assert.Throws<InvalidOperationException>`이 실패("GameFailure가 났다") | NUnit의 `Assert.Throws<T>`는 정확히 그 형식만 받는다. 플레이어용 실패(`Game.Fail`·`Game.Pay`·`Game.Answer`·`Game.Use`)는 `Assert.Throws<GameFailure>`로 잡는다(상속한 형식까지 받으려면 `Assert.Catch<InvalidOperationException>`). |
| `catch (GameFailure)`가 컴파일 오류(이미 잡힌 형식) | `catch (InvalidOperationException)`를 위에 두었다. 상속한 형식(`GameFailure`)을 먼저 둔다. |
| `Game.Fail(…)`을 썼는데 행동이 그대로 진행 | `throw`가 없다. `Game.Fail`은 예외를 만들기만 한다. |
| 프로그래머 실수 메시지가 플레이어에게 보임 | 화면이 모든 `InvalidOperationException`의 `Message`를 보여 준다. `GameFailure`만 보여 주고 나머지는 로그로(5-8). |
