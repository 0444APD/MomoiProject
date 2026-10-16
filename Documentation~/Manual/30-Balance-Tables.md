# 30. 밸런싱 값·곡선·가중치 표·플레이어용 실패

**한 줄 요약**: 개발자가 고치는 숫자(시작 에너지 3, 층마다 체력 +10%)는 코드에 적지 않고 **DB › 밸런싱 값**에 이름을 붙여 두고 `Game.Balance(ID)`로 읽는다. **곡선**을 붙이면 층·레벨·웨이브(**단계**)에 따라 값이 변한다(`Game.Balance(ID, 단계)`). 드롭·보상·상자처럼 "풀(후보들)에서 무게만큼의 확률로 뽑기"는 **DB › 가중치 표**에서 정하고 `Game.Roll` 또는 결과 `가중치 표에서 주기`·`가중치 표에서 고르기`(보이고 고르게 하기)로 뽑는다. 풀은 뽑는 순간에 만든다: 미리 정한 항목 + 조건(진행·도전과제) + 플레이어 데이터 목록(판에서 만들어진 것) − 뺄 목록(가진 것). 표 없이 코드에서 만든 풀은 `Game.Roll(풀, …)`. 난수는 표마다 따로인 결정론 흐름이라 Undo해도 같은 결과다. 플레이어에게 보일 실패("얼어 있어 움직일 수 없습니다")는 `throw Game.Fail(키, 기본 문장, 값…)`로 던진다. 행동 전체가 취소되고, 메시지는 텍스트 표에서 지금 언어로 만든다.

**언제 쓰는가**

- 플레이해 보며 고치는 숫자(밸런싱 값), 단계(층·레벨)에 따라 변하는 값(곡선), 그 숫자를 한꺼번에 세는 "10개 사기"(`BalanceSum`·`BalanceCount`).
- 드롭·보상·조우처럼 확률로 뽑는 것(가중치 표) — 풀이 진행·판·가진 것에 따라 바뀌거나, 무게가 진행에 따라 오르거나(천장), 후보 수를 늘리고 다시 뽑게 할 때까지.
- "에너지가 모자랍니다"를 언어마다 다르게 보여 줄 때(플레이어용 실패).
- 기획 문장별로 무엇을 만드나는 1절 표.

이 문서에서 쓰는 용어:

| 용어 | 뜻 |
|---|---|
| 밸런싱 값 | DB › 밸런싱 값의 항목 하나: 값(숫자) · 곡선(선택) · 메모. 코드 형식은 `BalanceData`(DB), `BalanceSystem`(실행 중). ID 상수는 `GameIds.Balance.X`. |
| 단계 | 곡선이 따라가는 수. 층·레벨·웨이브. `Game.Balance(ID, 단계)`의 두 번째 인자. |
| 기준 값 | 밸런싱 값의 `value`. 곡선이 시작 단계에서 내는 값이다. |
| 곡선 (`Curve`) | 단계에 따라 값이 변하는 규칙: 방식 · 시작 단계 · 방식별 칸 · 반올림 · 최솟값·최댓값. |
| 가중치 표 | DB › 가중치 표의 항목 하나: 항목 줄(항목 · 무게 · 개수 · 조건) + 꽝 무게 + 뺄 목록. 코드 형식은 `WeightTableData`(DB), `WeightTableDefinition`(실행 중). ID 상수는 `GameIds.WeightTable.X`. |
| 풀 | 한 번 뽑을 때의 후보 전체. 뽑는 순간에 표에서 만든다(저장하지 않는다). |
| 목록 줄 | 줄 종류가 `플레이어 데이터 목록`인 항목 줄. 그 목록의 요소마다 후보 하나(판에서 만들어진 풀). |
| 뺄 목록 | 표에 붙이는 플레이어 데이터 목록. 거기 있는 ID는 후보가 아니다(가진 유물·본 이벤트). |
| 무게 (weight) | 뽑힐 몫. 고정 % 줄이 없으면 확률 = 무게 ÷ (후보 무게 합 + 꽝 무게). 합이 1일 필요는 없다. |
| 고정 % (percent) | 줄의 방식 '고정 %': 전체 중 그 %로 나온다(묶음 — 안쪽 표·목록 줄 — 이면 묶음 전체가 그 %). 무게 줄은 고정 %가 남긴 몫(100 − 고정 % 합)을 무게 비율로 나눈다. 조건으로 고정 % 줄이 빠지면 그 몫은 무게 줄이 나눠 가진다. 무게 줄이 없으면 고정 % 줄끼리 비율대로. |
| 꽝 | 아무것도 나오지 않는 몫(`emptyWeight`). |
| 안쪽 표 | 항목으로 넣은 다른 가중치 표. 뽑히면 그 표에서 다시 뽑는다(등급 표 → 등급별 카드 표). |
| 플레이어용 실패 (`GameFailure`) | 플레이어에게 보일 이유가 든 실패 예외: 텍스트 키 · 값 · 기본 문장. `InvalidOperationException`을 상속한다. |
| `FailureKeys` | 라이브러리가 던지는 플레이어용 실패의 텍스트 키 모음(비용이 모자람·선택 개수 …). |

---

## 1. 무엇으로 만드나

| 기획 문장 | 만드는 것 |
|---|---|
| "시작 에너지 3" | 밸런싱 값 `startEnergy` = 3 → `Game.Balance`(4-1) |
| "층마다 적 체력 +10%" | 밸런싱 값 + 곡선 `비율로`(5-1) |
| "레벨 n에 필요한 경험치 = 100 × n^1.5" | 곡선 `거듭제곱`(5-2) |
| "상점 카드 가격: 1층 50, 5층 75, 10층 150" | 곡선 `표`(5-3) |
| "엘리트: 60% 유물, 30% 물약 1~2개, 10% 꽝" | 가중치 표 + 안쪽 표 + 개수 + 꽝(5-4) |
| "카드 보상 3장(서로 다른 카드) 중 하나" | `Game.Roll(ID, 3, unique: true)` + 선택 대기(5-5) |
| "유물 셋 중 하나를 골라 가진다"(코드 없이) | 결과 `가중치 표에서 고르기`(3-3, 5-5) |
| "가진 유물은 다시 안 나온다" · "핸드에서 무작위 1장" · "해금한 카드만" | 뺄 목록 · 목록 줄 · 항목 줄의 조건(5-10) |
| 표 없이 코드에서 만든 후보에서 뽑기 | `Game.Roll(풀, 개수, unique, stream)`(4-4) |
| "천장: 뽑을수록 5성 확률이 오르고 90번째는 확정" | 가중치 표 줄의 `무게를 밸런싱 값에서`(표 곡선, 단계 = `profile.pity`)(5-11) |
| "레벨업 보상 셋 중 하나 — 고른 패시브 효과의 레벨 +1" | 결과 `가중치 표에서 고르기`, 넣을 곳이 ID → 레벨 딕셔너리(3-3) |
| "선택지 +1"(보상 후보가 하나 늘어난다) | `가중치 표에서 고르기`의 `countBonus`(후보 수에 더할 숫자, 플레이어 데이터의 숫자 — `id`가 있는 객체의 필드면 패시브 효과 보정까지)(3-3, 5-13) |
| "보상 다시 뽑기(남은 횟수 1 · 골드 5)" | 비용을 내는 행동 + `Game.RerollChoice`(4-6, 5-13) |
| "10개 사기" · "살 수 있는 만큼 사기"(가격이 산 개수에 따라 오름) | `Game.BalanceSum` · `Game.BalanceCount`(4-1, 5-12) |
| double(약 1e308)을 넘는 큰 수(방치형의 생산량·가격) | `BigNumber`(→ `36-Grid-BigNumber.md`) |
| "3층부터 드래곤 비늘이 나온다" | 항목 줄의 조건(5-6) |
| "드롭 확률을 화면에 보인다" | `session.Tables.Chances`(5-7) |
| "얼어 있어 2턴 동안 움직일 수 없습니다"(언어별) | `throw Game.Fail` + 텍스트 표(5-8) |
| 수치 수십 개를 시트에서 한꺼번에 고친다 | DB 목록의 CSV(5-9, `33-Tick-Preview-Csv.md`) |

> **규칙: 개발자가 고칠 숫자는 코드에 적지 않는다.** `battle.energy = 3`이 아니라 `battle.energy = (int)Game.Balance(GameIds.Balance.StartEnergy)`. 그러면 숫자를 바꾸는 데 프로그래머가 필요 없고, 플레이 중에도 고쳐 볼 수 있다(2-1).

> **규칙: 플레이어에게 보일 실패는 `Game.Fail`, 프로그래머의 실수는 `InvalidOperationException`.** "없는 카드 ID"·"전투 중이 아닌데 카드 내기"처럼 화면이 막았어야 하는 일은 플레이어에게 보일 글이 아니다.

---

## 2. 동작 규칙

### 2-1. 밸런싱 값 읽기

- `Game.Balance(ID)`는 값을 돌려준다. `Game.Balance(ID, 단계)`는 곡선이 있으면 그 단계의 값, 곡선이 없으면(방식 `없음`) 값을 돌려준다.
- **바뀌지 않는 설정이다.** 플레이어 데이터가 아니라 Undo·판 저장·멀티플레이 동기화에 들어가지 않는다. 행동 밖(화면 코드)에서 읽어도 된다.
- 게임이 켜질 때(`GBibleRuntime.Start`) DB의 밸런싱 값 전부가 세션에 등록된다. 서버·테스트처럼 세션을 직접 만들면 직접 등록한다(4-2).
- 없는 ID면 `InvalidOperationException`("[MomoiProject] 없는 밸런싱 값 'enmyHp'입니다(DB › 밸런싱 값).")으로 그 행동이 취소된다. ID는 `GameIds.Balance` 상수로 쓴다.
- **플레이 중 고치기**: 에디터에서 플레이하는 동안 DB에서 값·곡선을 고치면 저장(Ctrl+S) 전이라도 0.5초 안에 실행 중인 세션에 반영된다(가중치 표도 같다). 값이 바뀌었을 때만 반영하고, 멀티 시험 모드의 서버 세션에도 반영하며, 화면을 모두 다시 그린다(`GameView.RedrawAll`). 반영은 **다음에 읽을 때부터**다. 이미 읽어서 플레이어 데이터에 넣은 값(만들어 둔 적의 체력)은 바뀌지 않는다.

### 2-2. 곡선

| 방식 (`CurveKind`) | 값 | 예(기준 100, 시작 단계 1) |
|---|---|---|
| 없음(고정) (`None`, 기본) | 기준 그대로(반올림·자르기는 적용하지 않는다 — 그 칸은 숨는다) | 늘 100 |
| 일정하게(+단계마다) (`Linear`) | 기준 + 더할 값 × (단계 − 시작) | +15 → 3단계 130 |
| 비율로(×단계마다) (`Exponential`) | 기준 × 비율^(단계 − 시작) | ×1.1 → 3단계 121 |
| 거듭제곱(단계^지수) (`Power`) | 기준 × (단계 ÷ 시작)^지수 | 지수 1.5 → 4단계 800 |
| 표(점 사이를 잇기) (`Table`) | 점 사이를 직선으로 잇는다. 첫 점 앞은 첫 점의 값, 마지막 점 뒤는 마지막 점의 값. **기준 값은 쓰지 않는다** | 점 (1, 10)·(5, 50) → 3단계 30, 9단계 50 |

- **시작 단계**(기본 1)에서 일정하게·비율로·거듭제곱의 값이 기준 값이다. 0층부터 세는 게임은 시작 단계를 0으로 둔다(거듭제곱은 0보다 커야 한다).
- 거듭제곱은 단계가 0 이하면 값 0이다(지수가 0·음수여도 — 0^지수(∞)를 계산하지 않는다).
- 표의 점은 단계 순으로 다시 늘어놓고 쓴다(적은 순서는 상관없다). 같은 단계의 점이 둘이면 목록에서 뒤의 점만 쓴다(앞의 점은 없는 것으로 본다 — 그 단계의 앞뒤 구간도 뒤의 점으로 잇는다).
- 계산한 뒤 **반올림 → 자르기** 순서다.

| 반올림 (`CurveRounding`) | 뜻 |
|---|---|
| 그대로 (`None`, 기본) | 소수 그대로 |
| 반올림 (`Round`) | 0.5는 0에서 먼 쪽(2.5 → 3, −2.5 → −3) |
| 내림 (`Floor`) | 작은 정수로 |
| 올림 (`Ceiling`) | 큰 정수로 |

- 라이브러리가 숫자를 정수로 맞추는 곳은 모두 이 반올림이다(정수 필드에 넣는 값 · 중첩 · 이벤트 횟수 · `가중치 표에서 고르기`의 후보 수 더하기 …). 코드에서 같게 맞추려면 `GameNumbers.Round`·`GameNumbers.RoundToInt`를 쓴다. `Math.Round`의 기본은 짝수 쪽(2.5 → 2)이라 결과가 다르다.
- **자르기**: `최솟값으로 자르기`를 켜면 최솟값보다 작은 값을 최솟값으로, `최댓값으로 자르기`를 켜면 최댓값보다 큰 값을 최댓값으로 만든다. 두 쪽은 따로 켠다(최댓값만 켜면 "3배에서 멈춘다", 최솟값만 켜면 "1 아래로는 내려가지 않는다"). 둘 다 켰는데 최솟값이 최댓값보다 크면 검증 오류다.

### 2-3. 가중치 표 뽑기

한 번 뽑기는 다음 순서로 돈다.

1. **후보(풀)**를 모은다: 무게가 0보다 크고(숫자가 아닌 무게도 후보가 아니다 — 다른 줄은 그대로 나온다), 조건이 비었거나 **지금** 참인 줄. 줄의 `무게를 밸런싱 값에서`를 고른 줄은 무게가 그 밸런싱 값의 **지금 단계** 값이다(단계 = 고른 플레이어 데이터의 숫자, 비우면 기준 값. 천장·운·레벨에 따라 바뀌는 확률, 5-11). 목록 줄은 그 순간의 목록 요소마다 한 후보로 펼친다(요소가 글자면 그대로, id가 있는 객체면 그 id, 딕셔너리면 키를 이름순으로, 같은 ID는 한 번. 무게·개수는 그 줄의 값). 뺄 목록에 있는 ID는 뺀다(ID를 읽는 법은 목록 줄과 같다. 바깥 표의 뺄 목록은 안쪽 표에도 적용된다). 서로 다른 것만(`unique`)이면 이번 뽑기에서 이미 나온 항목도 뺀다(안쪽 표는 아래 '서로 다른 것만').
2. 몫을 정한다(`WeightTableSystem.Shares`): 고정 % 줄이 없으면 몫 = 무게, 합 = 후보의 무게 합 + 꽝 무게. 고정 % 줄(꽝도 고정 %일 수 있다)이 있으면 고정 % 줄은 적힌 %, 무게 줄은 (100 − 고정 % 합)을 무게 비율로 나누고 합 = 100. 무게 줄이 없거나 고정 % 합이 100 이상이면 고정 % 줄끼리 비율대로(무게 줄은 0). 목록 줄이 고정 %면 요소들이 그 %를 똑같이 나눈다. 합이 0 이하면 아무것도 나오지 않는다.
3. 0~합의 난수로 하나를 고른다. 꽝 몫에 떨어지면 끝(아무것도 넣지 않는다).
4. 고른 줄의 **개수**를 정한다: 최소와 최대가 같으면 그 수, 다르면 최소~최대 중 하나. 그 개수만큼 되풀이한다: 항목이 다른 가중치 표면 그 표에서 1~4를 다시 하고, 아니면 결과에 그 ID를 넣는다.

- 횟수 n이면 이 한 번을 n번 한다. 결과는 ID 목록이다. 꽝이 나오면 횟수보다 적고, 개수가 2 이상인 줄이 나오면 많을 수 있다.
- **끝없이 큰 무게**(∞ — 코드 풀의 `1 / 0` 같은 무게)의 줄은 늘 나온다(무게 ÷ 합 = 1, 여럿이면 그 줄끼리 같은 몫, 다른 줄·꽝은 0). 1e300보다 큰 무게는 1e300으로 본다(합이 double을 넘지 않게).
- **난수는 표마다 따로인 결정론 흐름**(`Game.Random("table:" + 표 ID)`, 07장)이다. 같은 시드·같은 행동이면 같은 결과가 나오고, Undo하면 난수도 돌아가 다시 해도 같은 드롭이 나온다(Undo로 다시 뽑기를 막는다). 표를 더하거나 다른 표를 뽑아도 이 표의 결과는 바뀌지 않는다. 안쪽 표는 그 표의 흐름을 쓴다.
- **행동 안에서 뽑는다.** 행동 밖에서 뽑으면 그 난수가 Undo·판 저장·멀티플레이와 어긋난다.
- **서로 다른 것만**: 한 번의 `Roll` 안에서 한 번 나온 항목은 다시 후보가 되지 않는다. 안쪽 표는 그 안에 아직 나올 것이 남아 있는 동안 후보로 남고(뺄 목록이 있을 때도 같다), 그 안에서 이미 나온 항목만 빠진다(등급 표 → 등급별 카드 표에서도 서로 다른 카드). 다 나온 안쪽 표는 후보에서 빠지므로, 표 전체에 서로 다른 항목이 충분하면 늘 요청한 개수가 나온다(꽝 몫은 따로). 모자라면 있는 만큼만 나온다. 개수가 2 이상인 줄은 그 개수만큼 같은 ID가 들어간다.
- 표끼리 8단계 넘게 서로를 부르면 예외(`InvalidOperationException`, "가중치 표 'a'가 8단계 넘게 서로를 부릅니다(표끼리 돌고 있는지 확인하세요).")로 그 행동이 취소된다. DB 검증은 자기 자신을 넣은 표만 잡는다.
- **풀은 저장하지 않는다.** 뽑을 때마다 표·조건·목록에서 다시 만든다. 업데이트로 DB에 항목이나 조건을 더하면 이미 진행 중인 저장에도 바로 적용되고, 저장을 변환할 일이 없다. "해금하면 풀에 들어간다"는 해금 목록을 풀로 저장하지 말고 항목 줄의 조건(도전과제 달성·`profile.unlocked`에 있음)이나 목록 줄로 둔다.
- **확률**(`session.Tables.Chances`, 5-7): 지금 상태로 본 후보마다 몫 ÷ 합(위 2. — 고정 % 줄이 없으면 무게 ÷ (후보 무게 합 + 꽝 무게)). 꽝 확률은 1 − (그 합)이다. 안쪽 표는 한 줄(그 표 ID)로 나온다. 서로 다른 것만·개수는 반영하지 않는다(한 번 뽑기의 확률).

### 2-4. 플레이어용 실패

- `throw Game.Fail(키, 기본 문장, (이름, 값)…)`. 행동 안에서 던지면 그 행동이 실패하고 바꾼 것이 모두 취소된다(02장). 예외는 `Game.Do`를 부른 쪽(화면 코드)으로 온다. 키는 `fail.*`(관례 2절)로 짓는다.
- **메시지는 읽을 때마다 지금 언어로 만든다.** 텍스트 표에 그 키가 있으면 그 틀에 값을 넣고, 없으면 기본 문장(같은 `{값}` 틀 문법, `23-Text-Localization.md` 3절)을 쓰고, 둘 다 없으면 `[키]`다. 그래서 언어를 바꾼 뒤 같은 예외의 `Message`를 다시 읽으면 새 언어의 글이다.
- `GameFailure`는 `InvalidOperationException`을 상속한다. `catch (InvalidOperationException e)`도 잡고, `e.Message`가 지금 언어의 글이다(둘을 나눠 잡을 때는 `GameFailure`를 먼저, 5-8).
- 라이브러리가 던지는 플레이어용 실패도 이 형식이다. 그 키를 텍스트 표에 넣으면 번역된다.

| `FailureKeys` | 텍스트 키 | 값 | 기본 문장 | 던지는 곳 |
|---|---|---|---|---|
| `NotEnough` | `momoi.fail.notEnough` | `{what}` `{have}` `{need}` | "{what}이(가) 모자랍니다({have} / {need})." | `Game.Pay` · 결과 `비용 내기`(28장 2-4) |
| `ChooseExactly` | `momoi.fail.chooseExactly` | `{count}` `{picked}` | "{count}개를 골라야 합니다(고른 것 {picked}개)." | `Game.Answer`(29장 4-10) |
| `ChooseBetween` | `momoi.fail.chooseBetween` | `{min}` `{max}` `{picked}` | "{min}~{max}개를 골라야 합니다(고른 것 {picked}개)." | `Game.Answer` |
| `NotACandidate` | `momoi.fail.notACandidate` | `{item}` | "{item}은(는) 고를 수 있는 후보가 아닙니다." | `Game.Answer` |
| `ChosenTwice` | `momoi.fail.chosenTwice` | — | "같은 것을 두 번 골랐습니다." | `Game.Answer` |
| `ChoiceGone` | `momoi.fail.choiceGone` | — | "이미 답했거나 취소된 선택입니다." | `Game.Answer` |
| `NotYourChoice` | `momoi.fail.notYourChoice` | — | "다른 플레이어에게 온 선택입니다." | `Game.Answer`(멀티 서버 — 답할 사람이 정해진 선택, 29장 2-6) |
| `RewindTooFar` | `momoi.fail.rewindTooFar` | `{steps}` `{marks}` | "{steps}지점 앞으로 되감을 수 없습니다(남은 지점 {marks}개)." | `Game.Rewind`·`RewindKey`·`RewindEntity`(40장) |
| `CannotUse` | `momoi.fail.cannotUse` | `{ability}` `{condition}` | "{ability}: 사용 조건을 만족하지 않습니다({condition})." | `Game.Use`(사용 조건이 거짓이고 그 액티브 효과·아이템의 쓸 수 없을 때 글이 비었을 때, 16장) |

- `NotEnough`의 `{what}`은 낼 것의 이름이다. `Game.Pay(키, 경로, 양, what: "energy.name")`·결과 `비용 내기`의 `보일 이름`에 텍스트 키를 주면 그 키의 번역된 글, 글자를 주면 그 글자가 들어간다. 비우면 "플레이어 데이터의 이름 › 필드 경로"(예: "판 › battle.energy")라서 DB의 이름과 코드 경로가 플레이어에게 보인다 → 플레이어가 보는 비용에는 늘 이름을 준다(5-8). `{have}`·`{need}`는 이미 글자로 바꾼 숫자(소수 둘째 자리까지)다.
- `CannotUse`의 `{ability}`는 액티브 효과·아이템의 ID다(틀이 지금 언어의 이름 `<ID>.name`으로 바꾼다). `{condition}`은 사용 조건의 DB 문장(한국어)이라 번역되지 않는다. 그래서 플레이어에게 보일 글은 액티브 효과·아이템마다 **쓸 수 없을 때 글**(DB › 액티브 효과 `failText`, 사용 기능 `Usable.failText` — 텍스트 키(`fail.fullHp`) 또는 틀 글, 16장 2-1)로 정한다. 정하면 `Game.Use`의 실패도 `Game.CanUse`의 이유도 그 글이다. 모든 액티브 효과에 같은 글을 쓰려면 `{condition}`을 뺀 틀을 `momoi.fail.cannotUse`로 원문 표에 넣는다.
- 텍스트 키로 준 `{what}`과 `{ability}`의 이름도 틀과 함께 **읽을 때마다 지금 언어로** 바뀐다. 언어를 바꾼 뒤 같은 예외의 `Message`를 다시 읽으면 이름까지 새 언어다.
- **프로그래머의 실수**(없는 ID·잘못된 필드 이름)는 그대로 `InvalidOperationException`("[MomoiProject] …")이다. 플레이어에게 보일 글이 아니다. `Game.Pay`에 없는 필드나 숫자가 아닌 필드를 주면 "모자랍니다"가 아니라 이것이다("[MomoiProject] 비용: 'run'에 필드 'battle.enrgy'가 없습니다(…).").

---

## 3. 에디터에서 쓰는 법

### 3-1. DB › 밸런싱 값

**탭 모양(전용 화면)**: 줄마다 **표시 이름**(개발자가 한글로 고치는 이름, 없으면 ID) · 오른쪽에 값. 상세 탭으로 넘어가지 않는다.
- **이름을 누르면 펼치고 접는다.** 펼치면 아래 칸이 보인다 — 곡선을 쓰는 값만 곡선 칸(방식 · 시작 단계 …)이 있고, 방식을 바꾸면 그 방식의 칸이 생기고 사라진다. 펼칠 칸이 없으면 화살표도 없다.
- **줄에 마우스를 올리면 오른쪽에 입력 칸**이 뜬다. 입력 칸 주석이 있으면 칸 안에 흐리게 보인다(예: `1 = 그대로, 0 = 멈춤`). 방식 같은 고르는 칸은 고르기, 켬/끔은 체크, **목록(곡선의 점)만 새 창**에서 고친다.
- **줄 맨 오른쪽 ✎**: 표시 이름 · 입력 칸 주석 · 메모 · 곡선 쓰기(켜면 펼친 칸에 곡선 칸이 생긴다). ID는 코드(`GameIds`)·저장이 쓰는 이름이라 보이기만 한다.
- 폴더(묶음)는 머리글로 보인다. 바꾼 것은 저장(Ctrl+S)할 때 적용한다.

`MomoiProject → DB` → 사이드바 **DB** 그룹의 **밸런싱 값** 탭. 로직 쪽 종류라 **+ 새로 만들기**·**삭제**가 없다(21장 3-1): 항목은 AI가 DB 반영 스크립트(`[DbSeed]`, 관례 15절)로 만들고, 값과 함께 표시 이름·메모·입력 칸 주석·묶음(`EditorAssignFolder`)을 넣는다(`AI/NamingGuide.md` 3절). 개발자는 값·곡선과 ✎의 글을 고친다. 새 항목의 상수(`GameIds.Balance.EnemyHpScale`)는 반영 스크립트가 돈 뒤 Unity가 다시 컴파일해야 생기므로, 그 상수를 쓰는 코드는 그다음에 쓴다(관례 15절). 칸 이름표는 변수 이름이고, 마우스를 올리면 `[FieldLabel]` 글자와 설명이 보인다.

| 칸 | 툴팁 | 값 |
|---|---|---|
| `label` | 표시 이름 / DB 밸런싱 값 탭에 보일 이름(한글로 자유롭게). 비우면 ID | 글자 — 줄의 ✎ |
| `hint` | 입력 칸 주석 / 입력 칸 안에 흐리게 보일 글 | 글자 — 줄의 ✎ |
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
- 검증 경고: "곡선: N단계에서 0 이하가 됩니다. 가격으로 쓰면 BalanceCount·BalanceSum이 그 단계부터 세지 않습니다(0 이하는 값을 매길 수 없는 가격). 최솟값으로 자르기를 켜세요."(표는 "점 N에서 …"). 시작 단계부터 100단계(표는 점마다)를 계산해 0보다 크던 값이 0 이하로 내려가면 알린다 — 단계마다 줄어드는 `일정하게`, 0 아래로 가는 표의 점. 처음부터 0 이하인 곡선은 가격이 아닌 값으로 보고 묻지 않고, `최솟값으로 자르기`의 최솟값이 0보다 크면 보지 않는다.
- 플레이 중에 고치면 0.5초 안에 게임에 반영된다(2-1). 마음에 들면 저장(Ctrl+S), 아니면 사이드바의 **되돌리기**.

### 3-2. DB › 가중치 표

같은 그룹의 **가중치 표** 탭. 밸런싱 값처럼 표는 AI가 DB 반영 스크립트로 만들고(3-1), 상수 `GameIds.WeightTable.EliteDrop`도 같은 때 생긴다. 개발자는 무게·%·개수·꽝·참조 같은 값을 고친다.

| 칸 | 툴팁 | 값 |
|---|---|---|
| `entries` | 항목 | 항목 줄 목록(아래) |
| `entries` › `source` | 줄 종류 | `항목 하나`(기본) / `플레이어 데이터 목록`. 고른 쪽의 칸만 보인다 |
| `entries` › `item` | 항목 / DB 항목(인스턴스·다른 가중치 표 …). 다른 가중치 표면 그 표에서 다시 뽑는다 | DB의 모든 종류의 항목에서 고른다(인스턴스·유물·액티브 효과·상태 효과·가중치 표 …). 항목 하나일 때 |
| `entries` › `listKey` · `listPath` | 플레이어 데이터 · 필드(목록) | 목록 줄일 때. 요소가 ID 글자거나 id가 있는 객체인 목록(`run › relics`, `run › battle.hand`), 또는 키가 ID인 딕셔너리(`run › perks`) |
| `entries` › `mode` | 방식 / 고정 %: 전체 중 이 %(묶음이면 묶음 전체). 무게: 고정 % 줄이 남긴 몫을 무게 비율로 | `무게(나머지를 나눔)`(기본) / `고정 %`. 고른 쪽의 칸만 보인다 |
| `entries` › `weight` | 무게 | 방식이 무게일 때. 0 이상(기본 1). 0이면 후보가 아니다 |
| `entries` › `percent` | %(전체 중) / 목록 줄이면 요소들이 이 %를 나눠 가진다 | 방식이 고정 %일 때. 0 이상(기본 10) |
| `entries` › `weightBalance` | 값을 밸런싱 값에서 / 고르면 무게(고정 %면 %) = 이 밸런싱 값(곡선)의 단계 값. 천장·운·레벨·깊이에 따라 바뀌는 확률 | DB › 밸런싱 값. 고르면 `weight`는 쓰지 않는다 |
| `entries` › `weightStepKey` · `weightStepPath` | 단계(플레이어 데이터) / 곡선의 단계로 쓸 숫자. 비우면 기준값 · 단계 필드 | `weightBalance`를 골랐을 때만 보인다. 예: `profile` › `pity`. 뽑는 순간의 값을 읽는다. `id`가 있는 객체의 필드면 스탯(보정 포함 — `countBonus`와 같다, 3-3)으로 읽는다 |
| `entries` › `minCount` · `maxCount` | 최소 개수 · 최대 개수 | 기본 1 · 1. 뽑히면 그 사이의 개수만큼 같은 ID가 들어간다(안쪽 표면 그만큼 다시 뽑는다) |
| `entries` › `condition` | 조건(맞을 때만 후보) | 조건 Block(`플레이어 데이터 검사` 등, `11-Triggers-Conditions.md`). 비우면 늘 후보 |
| `emptyMode` · `emptyWeight` · `emptyPercent` | 꽝 방식 · 꽝 무게 · 꽝 % | 꽝도 무게(나머지를 나눔)나 고정 %(전체 중 그 %). 0 이상 |
| `exclude` | 뺄 목록 / 이 플레이어 데이터 목록에 있는 ID는 뽑히지 않습니다 | 줄마다 플레이어 데이터 · 필드(목록). 안쪽 표에도 적용된다 |

- 검증 오류: "항목이 없습니다." / "항목을 고르지 않은 줄이 있습니다." / "목록 줄의 플레이어 데이터를 고르지 않았습니다." / "뺄 목록에 플레이어 데이터를 고르지 않은 줄이 있습니다." / "표가 자기 자신을 항목으로 가지고 있습니다." / "'healPotion': 무게는 0 이상이어야 합니다."(고정 % 줄이면 "%는 …") / "'healPotion': 개수는 0 ≤ 최소 ≤ 최대여야 합니다." / "꽝은 0 이상이어야 합니다." / "무게·%가 모두 0이라 아무것도 나오지 않습니다." / "고정 % 합이 100을 넘습니다(지금 N%). 무게 줄은 몫을 받지 못하고, 고정 % 줄끼리 비율대로 나눕니다." 줄의 조건 Block의 검증("존재하지 않는 도전과제 'firstWn'를 참조합니다." · "'&& AND' 안에 조건이 없습니다." …)도 함께 나온다.
- 검증 경고: "무게 줄 없이 고정 % 합이 N%입니다. 합이 100이 되게 비율대로 맞춰 뽑습니다(남는 몫을 꽝으로 하려면 꽝을 고정 %로)."
- **상세 위 요약 '확률'**: 줄마다 최종 %를 보인다(안쪽 표는 › 붙여 그 안의 줄까지 전체 기준으로 곱해서). 조건은 맞다고, 목록 줄은 한 줄로, 밸런싱 값은 기준값으로 본 값이다.
- 조건은 그 항목 줄을 후보로 볼지만 정한다. 조건이 거짓인 줄은 확률 계산(분모)에서도 빠진다(고정 % 줄이 빠지면 그 몫은 무게 줄이 나눠 가진다).
- 밸런싱 값에서 읽은 무게가 0 이하(또는 숫자가 아님)인 단계에서는 그 줄이 후보가 아니다. 그 줄의 `weight`·`percent`는 쓰지 않으므로 기본값 그대로 둔다. 밸런싱 값을 쓰는 줄이 있으면 "무게·%가 모두 0" 검사는 하지 않고, 고정 % 합 검사는 그 줄을 세지 않는다(뽑는 순간에 정해진다).

**반영 스크립트로 만들 때**(AI): DB 항목은 `WeightTableData`, 줄은 **`WeightTableEntry`**(둘 다 `GBible.Data` — 위 표의 칸이 필드 이름 그대로다). 코드로 정의를 등록할 때 쓰는 `WeightEntry`(4-5, 테스트·서버)와 다른 형식이다. 항목이 하나도 없는 표는 검증 오류("항목이 없습니다.")이므로 표를 만들 때 줄을 함께 넣는다. 5-4의 `eliteDrop`:

```csharp
// Assets/MomoiProject/Code/Editor/Seeds/TableSeeds.cs
using GBible;              // PlayerDataListRef
using GBible.Data;         // WeightTableData · WeightTableEntry · WeightMode · WeightEntrySource
using GBible.EditorTools;  // DbSeed · EditorDatabase

static class TableSeeds
{
    [DbSeed("1")]
    static void Drops()
    {
        if (EditorDatabase.Instance.FindById("eliteDrop") != null) return;      // 개발자가 맞춘 값을 덮지 않는다
        var table = (WeightTableData)EditorDatabase.Staging.Add(typeof(WeightTableData), "eliteDrop");
        table.EditorAssignFolder("드롭");
        table.entries.Add(new WeightTableEntry { item = "relicPool", weight = 6 });                          // 안쪽 표
        table.entries.Add(new WeightTableEntry { item = "healPotion", weight = 3, minCount = 1, maxCount = 2 });
        // 다른 모양의 줄: 고정 % — new WeightTableEntry { item = "dragonScale", mode = WeightMode.Percent, percent = 2 }
        //                 판의 목록 — new WeightTableEntry { source = WeightEntrySource.List, listKey = "run", listPath = "relics" }
        //                 무게를 밸런싱 값에서 — new WeightTableEntry { item = "ssr", weightBalance = "ssrRate", weightStepKey = "profile", weightStepPath = "pity" }
        table.emptyWeight = 1;                                                                            // 꽝
        table.exclude.Add(new PlayerDataListRef { key = "run", path = "relics" });                       // 가진 유물은 뽑지 않는다
    }
}
```

`item`은 DB에 있는 ID여야 한다(없으면 검증 오류 — 안쪽 표 `relicPool`·인스턴스 `healPotion`도 반영 스크립트로 함께 만든다). 줄의 조건(`condition`)에는 조건 Block 객체를 넣는다(5-6의 코드 줄과 같은 모양).

### 3-3. 결과 `가중치 표에서 주기` · `가중치 표에서 고르기`

`+ 결과 추가` → 그룹 **결과** → `가중치 표에서 주기`. 결과 종류가 플레이어 데이터(`OutcomeKinds.PlayerData`)라서, `플레이어 데이터에 주기`를 고를 수 있는 칸(도전과제·액티브 효과·사용 기능·상태 효과·개발 명령 …)이면 나온다.

| 칸 | 툴팁 | 값 |
|---|---|---|
| `table` | 가중치 표 | DB › 가중치 표에서 고른다 |
| `count` | 횟수 | 1 이상(기본 1) |
| `unique` | 서로 다른 것만 | 켜면 한 번 나온 항목은 다시 나오지 않는다(2-3) |
| `key` | 넣을 플레이어 데이터 | 플레이어 데이터 |
| `path` | 필드(목록) | 그 안의 목록 필드(`relics`). 비우면 "값 전체" |

- 뽑은 ID들을 넣을 곳의 형식에 따라 넣는다(**원본 ID 넣기**, `가중치 표에서 고르기`도 같다). 꽝이면 아무것도 넣지 않는다.

| 넣을 곳(`key` › `path`)의 형식 | 넣는 방법 | 예 |
|---|---|---|
| 목록(글자) | ID를 요소로 넣는다 | `run › relics`에 `anchor` |
| 목록(인스턴스 형식) | 그 인스턴스의 사본을 넣는다(`플레이어 데이터에 주기`의 요소 넣기와 같다, 12장 2-2) | `run › potions`에 `healPotion` 사본 |
| 딕셔너리(ID → 숫자) | 그 키의 개수를 나온 만큼 더한다(없던 키는 0에서) | `run › perks`의 `bloodlust` 1 → 2(레벨업), `profile › materials`의 `iron` +2 |
| 숫자 하나 | 나온 개수만큼 더한다(어떤 ID인지는 버린다) | `run › keysFound` +1 |
| 값 하나(글자·enum·클래스) | 그 ID로 바꾼다(클래스면 그 인스턴스의 사본). 여럿이면 차례로 넣어 마지막 것 | 고른 직업 → `run › job` = `mage` |

- 딕셔너리는 같은 ID가 두 번 나오면 +2다(개수가 2 이상인 줄, 횟수 2 이상). 가진 목록이 ID → 레벨 딕셔너리인 패시브 효과(17장 0-1)는 이 결과로 레벨이 오른다.
- 요약: `relicPool에서 뽑아 run › relics에 넣기`, 횟수가 2 이상이면 `cardReward에서 3개 뽑아 …`.
- 검증 오류: "가중치 표에서 주기: 표를 고르세요." / "가중치 표에서 주기: 넣을 플레이어 데이터를 고르세요." / "가중치 표에서 주기: 횟수는 1 이상이어야 합니다."
- 목록의 요소 형식이 표의 항목과 맞아야 한다. 카드 던전의 덱(`run.deck`, `List<DeckCard>`)에는 카드 원본(`Card` 인스턴스)이 들어가지 않는다(타임라인 경고 "인스턴스 'strike'의 형식(Card)이 DeckCard과(와) 다릅니다"). 덱에 넣는 일은 코드로 한다(5-5).

**`가중치 표에서 고르기`** — 표에서 뽑은 것을 후보로 **보이고**, 플레이어가 고른 것을 넣는다("유물 셋 중 하나"). 같은 자리(결과 종류 플레이어 데이터)에 나온다.

| 칸 | 툴팁 | 값 |
|---|---|---|
| `prompt` | 안내 글 | 고르기 창 제목(텍스트 키 또는 글) |
| `table` | 가중치 표 | DB › 가중치 표 |
| `count` | 후보 수 | 1 이상(기본 3). 이만큼 뽑아 후보로 보인다 |
| `countBonus` | 후보 수에 더할 숫자(플레이어 데이터) / 비우면 더하지 않습니다. 예: run › rewardBonus(유물 '선택지 +1'이 올린다) | 플레이어 데이터 · 필드(숫자). 요구하는 순간의 값을 `count`에 더한다 |
| `unique` | 서로 다른 것만 | 기본 켬 |
| `min` · `max` | 최소 개수 · 최대 개수 | 기본 1 · 1. 최소 0이면 건너뛰기 |
| `key` · `path` | 고른 것을 넣을 플레이어 데이터 · 필드(목록) | 고른 원본 ID를 넣는다(`가중치 표에서 주기`와 같은 원본 ID 넣기 — 목록이면 요소, 딕셔너리면 그 키 +1, 숫자면 +개수, 값 하나면 그 ID로). 비우면 넣지 않는다 |
| `then` | 고른 뒤 결과 | 고른 다음에 도는 결과. 컨텍스트의 `Picked`가 고른 원본 ID(글자)들 |

- 뽑기는 요구하는 순간(그 행동 안)에 끝나고 후보는 선택 대기(`$choices`, 멀티에서는 그 행동을 한 플레이어에게 묻는 `$choices:번호` — 29장 2-6)에 남는다. 그래서 끄고 켜거나 답하기를 Undo해도 후보가 바뀌지 않는다(29장 2-6). 후보를 바꾸는 것은 다시 뽑기(아래)뿐이다.
- **후보 수** = `count`(후보 수) + `countBonus`(후보 수에 더할 숫자)가 가리키는 숫자(반올림 — 0.5는 0에서 먼 쪽, 2-2). 합이 1보다 작으면 1이다. 플레이어 데이터를 비웠거나, 그 키가 없거나, 숫자로 읽을 수 없으면 0을 더한다. 요구하는 순간의 값을 읽으므로 그 행동 안에서 앞서 바꾼 값까지 들어간다. 요약 문장에는 `count`와 더할 숫자의 자리만 보인다(`relicPool에서 3개(+run › rewardBonus) 보여 …` — 값은 요구할 때 읽는다).
- **보정까지 읽는다**: 그 숫자 필드가 `id`가 있는 객체의 필드면(`run` › `hero.options`의 `hero`) 필드 값이 아니라 **스탯 최종값**(`Game.Attribute`, Modifier·패시브 효과의 가진 동안 스탯 보정 포함, 28장 2-1)으로 읽는다. 그래서 "선택지 +1" 유물을 필드를 올리는 결과 대신 패시브 효과의 `modifiers`(가진 동안 스탯 보정) `options` +1로 만들면, 잃을 때 저절로 빠진다(5-13). 부모에 `id`가 없으면(`run` › `rewardBonus`의 `run`) 필드 값 그대로다.
- **다시 뽑기**: 기다리는 동안 다른 행동 안에서 `Game.RerollChoice(선택 id)`를 부르면 같은 표·같은 후보 수·같은 `unique`로 후보를 새로 뽑는다(후보 수는 처음 요구할 때 더한 값 그대로). 선택 id는 같고 `ChoiceRequested`를 다시 쏜다(화면이 새로 그린다). 비용·남은 횟수는 부르는 행동이 정한다. 그 행동을 Undo하면 원래 후보다(4-6, 5-13, 29장 4-10b).
- 후보 이름은 텍스트 표의 `<ID>.name`(없으면 ID). 꽝이 나와 후보가 줄면 그만큼만 보이고, 후보가 하나도 없으면 기다리지 않고 고른 뒤 결과만 돈다.
- 덱처럼 원본 ID가 바로 들어가지 않는 목록은 `key`를 비우고 **고른 뒤 결과**에 프로젝트 결과 Block 하나(`Context.Picked`의 ID로 `DeckCard`를 만들어 넣는 `Reward`)를 둔다(5-5).
- 요약: `relicPool에서 3개 보여 1개 고르기 run › relics에 넣기`.
- 검증 오류: "가중치 표에서 고르기: 표를 고르세요." / "…후보 수는 1 이상이어야 합니다." / "…개수는 최소 ≤ 최대, 최대 1 이상이어야 합니다." / "…최소 개수는 0 이상이어야 합니다." / "…고른 것을 넣을 플레이어 데이터나 고른 뒤 결과가 있어야 합니다." / "…비어 있는 Block이 있습니다(스크립트가 지워졌을 수 있음)." 고른 뒤 결과의 검증도 함께 나온다.

### 3-4. 실패 메시지 번역 — 텍스트 표

- `Game.Fail`의 키를 원문 표·다른 언어 표에 더한다(`23-Text-Localization.md` 1절, 시트로도). 틀 문법(값·조사·복수형)은 다른 문장과 같다.
- 원문 언어에서는 표에 키가 없으면 코드의 기본 문장이 쓰인다. 그래서 다른 언어 표에만 넣어도 된다. 다만 기본 문장은 코드에만 있으므로 번역가에게 시트로 보내려면 원문 표에 그 키를 기본 문장과 같은 글로 넣어 둔다.
- 라이브러리의 키(`momoi.fail.*`, 2-4 표)도 같다. 원문 표에 넣으면 한국어 문장도 바꿀 수 있다.

### 3-5. 모니터에서 확인하기

- 밸런싱 값·가중치 표는 플레이어 데이터가 아니라서 **데이터** 탭에 없다. 뽑은 결과는 그 행동이 바꾼 값(`run › relics`에 넣은 ID)으로 보인다.
- 행동을 Undo한 뒤 같은 행동을 다시 하면 같은 드롭이 나오는 것을 볼 수 있다(2-3).
- 실패한 행동은 타임라인에 오류 "행동 실패, 변경 취소: 카드 내기 … — 얼어 있어 2턴 동안 움직일 수 없습니다."로 남는다(그때의 언어).

---

## 4. 코드로 쓰는 법 — 문법 요소를 하나씩

### 4-1. `Game.Balance` — 밸런싱 값 읽기

```csharp
var energy = (int)Game.Balance(GameIds.Balance.StartEnergy);              // 값
var scale = Game.Balance(GameIds.Balance.EnemyHpScale, run.floor);        // 곡선으로 그 층의 값
var ten = Game.BalanceSum(GameIds.Balance.CursorPrice, run.cursors, 10);  // 지금부터 10개를 살 때의 합
var max = Game.BalanceCount(GameIds.Balance.CursorPrice, run.cursors, run.gold);   // 가진 골드로 살 수 있는 개수
```

| 멤버 | 뜻 |
|---|---|
| `double Balance(string id)` | 값(`value`). 곡선은 보지 않는다 |
| `double Balance(string id, double x)` | 곡선이 있으면 단계 `x`의 값, 없으면 값 |
| `double BalanceSum(string id, double from, int count)` | 단계 `from`부터 1씩 `count`개의 값을 더한다("10개 사기": 가격 곡선, 단계 = 이미 산 개수). 단계마다 곡선의 반올림·자르기를 한 값의 합이다 |
| `int BalanceCount(string id, double from, double budget)` | `budget`으로 단계 `from`부터 몇 개를 살 수 있나("살 수 있는 만큼"). 앞에서부터 더해 `budget`을 넘기 직전까지(최대 10000개까지 센다) |

- 돌려주는 값은 `double`이다. 정수가 필요하면 곡선의 반올림을 고르거나 코드에서 `GameNumbers.RoundToInt`로 바꾼다(2-2).
- `BalanceSum`·`BalanceCount`는 단계마다 곡선을 한 번씩 계산해 더한다(개수만큼 돈다).
- **값이 0 이하(또는 숫자가 아님)인 단계에서 멈춘다.** 값을 매길 수 없는 가격이라 그 단계부터는 살 수 없고(`BalanceCount`) 합에도 더하지 않는다(`BalanceSum` — 음수가 되지 않는다). 곡선이 0 아래로 내려가는 것은 데이터 실수라 DB 검증이 경고한다(3-1). 내려가면 안 되는 가격은 `최솟값으로 자르기`를 켠다.
- 숫자가 double의 끝(약 1e308)을 넘을 만큼 커지는 게임은 그 값만 `BigNumber`로 둔다(→ `36-Grid-BigNumber.md`). 밸런싱 값·`Game.Pay`·DB 숫자 칸은 double만 알므로, 그때의 가격 계산과 비용 검사는 코드로 한다.
- 값을 바꾸지 않으므로 행동 밖(화면 코드)에서 읽어도 된다. 없는 ID면 `InvalidOperationException`(2-1).

### 4-2. `session.Balance` — `BalanceSystem`

| 멤버 | 뜻 |
|---|---|
| `Define(string id, double value, Curve curve = null)` | 등록(같은 ID면 바꾼다). ID가 비면 무시 |
| `Has(id)` · `Ids` | 등록되어 있는지 · 등록된 ID 전부 |
| `Get(id)` · `Get(id, x)` | = `Game.Balance` |
| `Sum(id, from, count)` · `MaxCount(id, from, budget, limit = 10000)` | = `Game.BalanceSum` · `Game.BalanceCount`(`limit`개까지만 센다) |

게임(Unity)에서는 런타임이 DB의 밸런싱 값 전부를 등록한다. 서버·테스트처럼 세션을 직접 만들면 직접 등록한다.

```csharp
foreach (var balance in GameDatabase.Load().All<BalanceData>()) balance.Define(session.Balance);

// DB 없이 코드로(테스트 등)
session.Balance.Define("startEnergy", 3);
session.Balance.Define("enemyHp", 40, new Curve { kind = CurveKind.Linear, step = 8 });   // 1층 40, 3층 56
```

`BalanceData`(DB 항목): `value` · `curve` · `note`, `ActiveCurve`(방식이 `없음`이면 `null`), `Define(BalanceSystem)`(그 세션에 등록).

### 4-3. `Curve` — 코드에서 곡선 쓰기

`Curve`는 코어 클래스라 밸런싱 값 밖에서도 쓴다.

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
| `Validate(IValidationContext context, string label)` · `Validate(context, label, double? baseValue)` | 3-1의 검증. 기준 값을 넘기면 0 이하로 내려가는 곡선의 경고까지 본다(넘기지 않으면 기준 값과 상관없이 정해지는 것만 — 표의 점, 단계마다 줄어드는 `일정하게`). 프로젝트의 DB 종류에 곡선 칸을 둘 때 `ValidateData`에서 부른다 |

### 4-4. `Game.Roll` — 가중치 표에서 뽑기

```csharp
string drop = Game.Roll(GameIds.WeightTable.ChestDrop);                          // 하나(꽝이면 null)
List<string> cards = Game.Roll(GameIds.WeightTable.CardReward, 3, unique: true); // 3번, 서로 다른 것
```

| 멤버 | 뜻 |
|---|---|
| `string Roll(string tableId)` | 한 번 뽑아 **첫** ID를 돌려준다. 꽝이면 `null`. 개수가 2 이상인 줄이 나와도 하나만 돌려준다(그때 표마다 한 번 타임라인 경고, 5-4) |
| `List<string> Roll(string tableId, int count, bool unique = false)` | `count`번 뽑은 ID 목록(꽝은 빠진다). `unique`면 서로 다른 것만(2-3) |
| `List<string> Roll(IEnumerable<(string item, double weight)> pool, int count = 1, bool unique = false, string stream = "pool")` | 표 없이 **코드에서 만든 풀**에서 뽑는다. 항목이 가중치 표 ID면 그 표에서 다시 뽑는다. 난수 흐름은 `"pool:" + stream` |
| `List<string> Roll(IEnumerable<string> pool, int count = 1, bool unique = false, string stream = "pool")` | 무게가 모두 1인 풀(ID 목록) |

```csharp
// 판에서 규칙으로 만든 풀: 이번 층에 나온 적 종류 중 하나, 무게는 남은 체력
var pick = Game.Roll(battle.enemies.Select(e => (e.id, (double)e.hp)), 1, stream: "curseTarget");
```

- 행동 안에서 부른다(난수가 행동과 함께 되돌아간다).
- 코드 풀의 `stream`은 쓰는 곳마다 다른 이름으로 준다. 같은 이름을 두 곳에서 쓰면 한쪽에 뽑기를 더할 때 다른 쪽 결과가 바뀐다.
- 없는 표면 `InvalidOperationException`("[MomoiProject] 없는 가중치 표 'eliteDorp'입니다(DB › 가중치 표).").

### 4-5. `session.Tables` — `WeightTableSystem` · `WeightTableDefinition` · `WeightEntry`

| `session.Tables` | 뜻 |
|---|---|
| `Define(WeightTableDefinition table)` | 등록(같은 ID면 바꾼다) |
| `Has(id)` · `Find(id)` | 등록되어 있는지 · 정의(없으면 `null`) |
| `List<string> Roll(id, count = 1, unique = false)` | = `Game.Roll`의 목록 형태 |
| `IReadOnlyList<(string item, double chance)> Chances(id)` | 항목별 확률(조건은 지금 상태, 꽝을 포함한 분모. 같은 항목이 여러 줄에 있으면 몫을 합쳐 한 줄 — 처음 나온 줄의 차례). 없는 표면 빈 목록 |

| `WeightTableDefinition` | 뜻 | DB 칸 |
|---|---|---|
| `Id` | 표 ID | 항목 ID |
| `EmptyWeight` · `EmptyPercent` | 꽝 무게(또는 %) · 참이면 꽝이 고정 % | `emptyMode`에 따라 `emptyWeight`·`emptyPercent` |
| `Entries` (`List<WeightEntry>`) | 항목 줄 | `entries` |
| `Exclude` (`List<PlayerDataListRef>`, `key` · `path`) | 뺄 목록 | `exclude` |

`PlayerDataNumberRef`(`key` · `path`)는 플레이어 데이터 안의 숫자 하나를 가리킨다. `Read(session)`이 그 값을 읽는다(비었거나 없으면 0). 경로의 부모가 `id`가 있는 객체면 그 필드를 스탯으로 읽는다(`session.Attributes.Get`, 보정 포함 — 3-3). `가중치 표에서 고르기`의 `countBonus`가 이 형식이다(4-6).

| `WeightEntry` 필드 | 뜻 |
|---|---|
| `item` | 항목 ID(다른 표의 ID면 안쪽 표) |
| `weight` | 무게(기본 1). `percent`가 참이면 고정 %의 % |
| `percent` | 참이면 고정 % 줄(DB 칸 `mode` `고정 %`, 2-3) |
| `minCount` · `maxCount` | 개수(기본 1 · 1) |
| `condition` (`Condition`) | 맞을 때만 후보(`null`이면 늘) |
| `listKey` · `listPath` | 비어 있지 않으면 목록 줄(그 목록의 요소마다 후보, `item`은 쓰지 않음) |
| `weightBalance` | 비어 있지 않으면 무게 = 이 밸런싱 값의 단계 값(`weight`는 쓰지 않음) |
| `weightStepKey` · `weightStepPath` | 단계로 쓸 플레이어 데이터 · 그 안의 숫자 경로(비우면 기준 값). 경로는 필드 경로라 `pity[limited]`(딕셔너리의 키, 11장 3-1)도 된다. 경로의 부모가 `id`가 있는 객체면 그 필드를 스탯으로 읽는다(`PlayerDataNumberRef`와 같은 규칙 — 행운 유물의 보정이 확률에도 든다) |

```csharp
foreach (var table in GameDatabase.Load().All<WeightTableData>()) session.Tables.Define(table.ToDefinition());

// DB 없이 코드로
var drop = new WeightTableDefinition { Id = "eliteDrop", EmptyWeight = 1 };
drop.Entries.Add(new WeightEntry { item = "relicPool", weight = 6 });
drop.Entries.Add(new WeightEntry { item = "healPotion", weight = 3, minCount = 1, maxCount = 2 });
session.Tables.Define(drop);

// 판에서 만들어지는 풀 + 가진 것 빼기
var shop = new WeightTableDefinition { Id = "shopCards" };
shop.Entries.Add(new WeightEntry { item = "commonCards", weight = 3 });
shop.Entries.Add(new WeightEntry { listKey = "profile", listPath = "unlocked", weight = 1 });   // 해금한 카드마다 무게 1
shop.Exclude.Add(new PlayerDataListRef { key = "run", path = "deck[].card" });                 // 덱에 있는 카드는 빼기
session.Tables.Define(shop);
```

`WeightTableData.ToDefinition()`은 DB 항목을 정의로 바꾼다(항목·목록을 고르지 않은 줄은 빼고, 꽝 무게는 0 이상으로).

### 4-6. `RollTableOutcome` — 결과 '가중치 표에서 주기'를 코드로

```csharp
var reward = new RollTableOutcome { table = "relicPool", count = 1, key = "run", path = "relics" };
```

| 필드 | 뜻 |
|---|---|
| `table` | 가중치 표 ID |
| `count` | 횟수(1보다 작으면 1) |
| `unique` | 서로 다른 것만 |
| `key` · `path` | 넣을 플레이어 데이터 · 필드 경로(목록·딕셔너리·숫자·값 하나, 원본 ID 넣기 3-3) |
| `Kind` | `OutcomeKinds.PlayerData` |

넣는 규칙은 `GrantIds.Into(session, key, path, ids)` 하나다(두 결과가 함께 쓴다). 프로젝트의 결과 Block이 뽑은 ID를 같은 규칙으로 넣을 때도 부른다.

**`ChooseFromTableOutcome`** — 결과 '가중치 표에서 고르기'.

```csharp
var pick = new ChooseFromTableOutcome
{
    prompt = "유물 하나", table = "relicPool", count = 3, unique = true, key = "run", path = "relics",
    countBonus = new PlayerDataNumberRef { key = "run", path = "rewardBonus" },   // 선택지 +1
};
```

| 필드 | 뜻 |
|---|---|
| `prompt` · `table` · `count` · `unique` | 안내 글 · 표 · 후보 수 · 서로 다른 것만 |
| `countBonus` (`PlayerDataNumberRef`) | 후보 수에 더할 플레이어 데이터의 숫자(`key` · `path`). 비우면 0. `id`가 있는 객체의 필드면 보정까지 읽는다(3-3) |
| `min` · `max` | 고를 개수 |
| `key` · `path` | 고른 원본 ID를 넣을 목록(비우면 넣지 않음) |
| `then` (`List<Outcome>`) | 고른 뒤 결과. 컨텍스트의 `Picked`가 고른 ID(글자) |

안에서는 `Game.Roll(table, count + 더할 숫자, unique)`로 뽑아 `Choices.Ask`에 넘기고, 이어서 할 행동은 라이브러리의 `ChoiceEffectsRun`(`ids = true`, 개발자 행동 "고른 것 넣기")이다. 기다리는 선택이 생겼으면 `Choices.MarkTable(선택 id, 표, 후보 수, unique)`로 표로 만든 선택이라고 적어 둔다(다시 뽑기용).

**다시 뽑기 — `Game.RerollChoice`**

```csharp
// 행동 안에서: 남은 횟수를 내고 후보를 새로 뽑는다
Game.Pay(GameIds.PlayerData.Run.Id, "rerolls", 1, what: "reroll.name");
if (!Game.RerollChoice(choiceId)) throw Game.Fail("fail.cannotReroll", "다시 뽑을 수 없는 선택입니다.");
```

| 멤버 | 뜻 |
|---|---|
| `bool RerollChoice(string choiceId)` | 표로 만든 선택(`PendingChoice.table`이 있는 것)의 후보를 같은 표·같은 후보 수·같은 `unique`로 새로 뽑고 `ChoiceRequested`를 다시 쏜다. 표로 만든 선택이 아니거나, 이미 답했거나, 아무것도 나오지 않으면(모두 꽝) `false`이고 후보는 그대로다 |

- 행동 안에서 부른다. 난수는 그 표의 흐름(`table:` + 표 ID)을 이어서 쓴다. Undo하면 후보도 난수도 돌아간다.
- 비용·남은 횟수는 보지 않는다. 부르는 행동이 정한다. `false`일 때 낸 비용을 돌리려면 위처럼 `throw`로 행동을 실패시킨다.
- 앞 후보를 빼지 않는다. 같은 항목이 다시 나올 수 있다(5-13).
- 화면 쪽(다시 뽑기 버튼·입력 막기)은 `29-Statuses-Schedules-Choices.md` 4-10b.

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

`FailureKeys`: `NotEnough` · `ChooseExactly` · `ChooseBetween` · `NotACandidate` · `ChosenTwice` · `ChoiceGone` · `NotYourChoice` · `RewindTooFar` · `CannotUse`(2-4 표). `const string`이라 텍스트 표 도구 코드에서도 쓴다.

`Game.Pay`의 `what`(모자랄 때 실패 글의 `{what}`에 들어갈 이름)은 2-4, 서명은 28장 4-12.

---

## 5. 상황별 예제

예제는 구현 가이드(`AI/BuildGuide/`)의 카드 던전 형식을 쓴다. 이 장에 필요한 부분만 옮긴다.

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

- 예제의 DB 항목(밸런싱 값·가중치 표)은 AI가 DB 반영 스크립트로 만든다. ID 상수(`GameIds.Balance.EnemyHpScale`, `GameIds.WeightTable.EliteDrop` …)는 그 스크립트가 돈 뒤 Unity가 다시 컴파일해야 생긴다(3-1).
- 5-2는 `Profile`에 `public int level = 1, exp;`가 있다고 본다(가이드 예제에는 없다).

### 5-1. 층별 적 체력 곡선

몬스터마다 체력이 다르므로(고블린 20, 드래곤 …) 층마다의 **배율**을 밸런싱 값으로 둔다.

**에디터**(DB › 밸런싱 값 `enemyHpScale`): `value` `1`, `curve` › `kind` `비율로(×단계마다)`, `startX` `1`, `rate` `1.1`, `useMax` 켬 · `max` `3`(3배에서 멈춘다).

**코드** — 적을 만드는 규칙 함수가 배율을 곱한다.

```csharp
public static Enemy NewEnemy(GameApi game, string monster)
{
    var data = MonsterOf(game, monster) ?? throw new InvalidOperationException($"몬스터 '{monster}'이(가) DB에 없습니다.");
    var floor = Math.Max(1, game.Data().Run.floor);
    var hp = GameNumbers.RoundToInt(data.maxHp * game.Balance(GameIds.Balance.EnemyHpScale, floor));   // 0.5는 0에서 먼 쪽(2-2)
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

**에디터**(DB › 밸런싱 값 `expToNext`): `value` `100`, `kind` `거듭제곱(단계^지수)`, `startX` `1`, `power` `1.5`, `rounding` `반올림`.

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

**에디터**(DB › 밸런싱 값 `shopCardPrice`): `value`는 쓰지 않는다(0 그대로), `kind` `표(점 사이를 잇기)`, `points` (1, 50) · (5, 75) · (10, 150), `rounding` `반올림`.

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
        Game.Pay(GameIds.PlayerData.Run.Id, "gold", price, what: "gold.name", forId: source);   // 모자라면 "골드이(가) 모자랍니다(…)" — 아무것도 바뀌지 않는다
        run.deck.Add(Rules.NewCard(Game, source));
    }
}
```

- 가격을 행동 안에서 계산한다. 화면이 가격을 행동에 넣어 보내면(`price` 필드) 멀티플레이에서 상대가 값을 속일 수 있다. 화면은 같은 `Game.Balance`로 표시만 한다.
- 골드는 직접 빼지 않고 `Game.Pay`로 낸다. 그래야 행동 직전 이벤트 `비용 지불`을 거쳐 할인·빚·"이 카드만 공짜"(`forId`) 같은 효과가 붙는다(28장 2-4).

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

- `Game.Roll(id)`(하나)는 첫 ID만 돌려주므로 물약이 2개 나와도 하나만 받는다(세션마다 표마다 한 번 타임라인 경고, 6절). 개수가 2 이상인 줄이 있는 표는 목록 형태 `Game.Roll(id, 1)`로 받는다.
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

**코드 없이(DB만)** — 원본 ID가 그대로 들어가는 목록이면 결과 `가중치 표에서 고르기` 하나로 끝난다. 예: 엘리트를 잡으면 유물 셋 중 하나.

1. 도전과제·유물·개발 명령 등의 결과에 `가중치 표에서 고르기`: `table` `relicPool`, `count` `3`, `unique` 켬, `key` `run`, `path` `relics`.
2. 요약: `relicPool에서 3개 보여 1개 고르기 run › relics에 넣기`.

덱(`List<DeckCard>`)처럼 넣을 때 만들어야 하는 목록은 `key`를 비우고 고른 뒤 결과에 프로젝트 결과 Block을 하나 둔다. 그 뒤로는 개발자가 표·후보 수만 바꾼다.

```csharp
[Serializable, BlockInfo("고른 카드 덱에 넣기", Category = "결과", Description = "가중치 표에서 고르기로 고른 카드 원본을 덱에 넣습니다.")]
public class AddPickedCards : Reward
{
    public override RewardEffect Effect => RewardEffect.Reversible;
    public override void Grant(GameSession session)
    {
        foreach (var id in Context.Picked.OfType<string>())
            session.Game.Data().Run.deck.Add(Rules.NewCard(session.Game, id));
    }
}
```

### 5-6. 조건 붙은 항목 — 3층 이상

"드래곤 비늘(유물)은 3층부터 엘리트 드롭에 나온다."

**에디터**: 5-4의 `eliteDrop`에 줄을 더한다: `item` `dragonScale`, `weight` `2`, `condition` `+ 블럭` → `플레이어 데이터 검사` → 플레이어 데이터 `run`, 조건 `floor` `이상` `3`.

| 층 | 유물 풀 | 물약 | 드래곤 비늘 | 꽝 |
|---|---|---|---|---|
| 1~2 | 6/10 = 60% | 30% | — | 10% |
| 3 이상 | 6/12 = 50% | 25% | 2/12 ≈ 17% | 1/12 ≈ 8% |

- 다른 줄의 확률을 그대로 두고 싶으면 그 줄들을 고정 %로 한다(조건 줄이 끼어도 고정 % 줄은 적힌 대로, 몫은 무게 줄이 낸다). 무게 줄만이면 조건이 거짓인 줄은 분모에서도 빠진다. 그래서 3층부터 다른 줄의 확률이 조금씩 준다. 무게만으로 다른 확률을 그대로 두고 싶으면 꽝 무게를 그만큼 줄인 표를 따로 둔다.
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
        if (!battle.hand.Contains(card.id)) throw new InvalidOperationException("핸드에 없는 카드입니다.");

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
- 선택 대기의 실패(`momoi.fail.chooseExactly` …)와 사용 조건 실패(`momoi.fail.cannotUse`, 또는 액티브 효과의 쓸 수 없을 때 글에 적은 키)도 같은 방식으로 번역한다.

**화면 쪽** — 화면 코드는 행동을 `OnClick("이름", () => 행동)`(화면 버튼, 35장 2-4)이나 `GameScreens.Do(() => 행동)`(월드를 눌렀을 때, 32장 5절)으로 보낸다. 둘 다 플레이어용 실패(`GameFailure`)를 잡아 그 글(지금 언어)을 알림으로 띄운다(DB › 화면 설정 '실패를 알림으로'를 끄면 경고 로그). 바꾼 것은 이미 모두 취소되어 있다. 프로그래머 실수(`InvalidOperationException`)는 잡지 않고 그대로 던진다 — 콘솔에 남고 플레이어에게는 보이지 않는다. 연출·선택 대기 중(`GameView.Busy`)이면 보내지 않고 거짓을 돌려준다.

```csharp
// 월드의 카드를 적 위에 놓았을 때(화면 코드)
GameScreens.Do(() => new PlayCard { card = card, target = target });
```

- 실패 글을 알림 대신 다른 곳(카드 위 말풍선)에 보이려면 직접 `try { Game.Do(…) } catch (GameFailure f) { … f.Message … }`. `GameFailure`는 `InvalidOperationException`을 상속하므로 둘을 함께 잡으면 `GameFailure`를 먼저 잡는다. 떠 있는 동안 언어를 바꿔도 글이 따라가게 하려면 글자 대신 `GameFailure`를 들고 있다가 다시 그릴 때 `f.Message`를 다시 읽는다.
- 버튼을 미리 흐리게 하고 이유를 툴팁으로 보이려면 `Game.CanDo`(`33-Tick-Preview-Csv.md` 2-2).

### 5-9. CSV로 밸런싱 값 한꺼번에 조정

밸런싱 값이 수십 개가 되면 시트에서 한 번에 고치는 편이 빠르다.

1. DB › 밸런싱 값 탭 → 맨 위 폴더 줄의 **CSV** → **CSV로 내보내기…** → 파일을 저장한다(UTF-8).
2. 구글 시트·엑셀에서 연다. 한 줄 = 항목 하나(첫 칸 `id`), 칸 = `BalanceData`의 단순 필드 차례(`label` · `hint` · `value` · `curve.kind` · `curve.startX` · … · `curve.max` · `note`). 표의 점은 점이 있는 항목이 있을 때만 `curve.points[0].x` …로 나온다. 칸 규칙(목록 칸·빠지는 칸·값 읽기)은 `33-Tick-Preview-Csv.md` 2-3.

```
id,label,hint,value,curve.kind,curve.startX,curve.step,curve.rate,curve.power,curve.rounding,curve.useMin,curve.min,curve.useMax,curve.max,note
enemyHpScale,적 체력 — 층 배율,1 = 그대로,1,Exponential,1,0,1.15,1.5,None,false,0,true,3,층마다 +15%
expToNext,경험치 — 다음 레벨까지,,100,Power,1,0,1.1,1.8,Round,false,0,false,0,
startEnergy,시작 에너지,,3,None,1,0,1.1,1.5,None,false,0,false,0,
```

3. 고친 뒤 CSV로 다시 받아 **CSV 가져오기…** → 결과 창("3개 고침, 0개 새로 만듦(적용 전 — Ctrl+S로 적용).")을 확인하고 저장(Ctrl+S)한다. 마음에 안 들면 **되돌리기**.

- 밸런싱 값·가중치 표는 로직 쪽 종류라 CSV에 없는 ID를 적어도 새 항목이 생기지 않는다(결과 창 "없는 id N개는 건너뜀 — 새 항목은 AI가 만든다."). 새 값은 AI에게 요청한다.
- `curve.kind`에는 코드 이름(`Exponential`)과 DB에 보이는 이름(`비율로(×단계마다)`)을 모두 적을 수 있다. 내보낼 때는 코드 이름이다.
- 가중치 표 탭도 같다: 항목 줄은 `entries[0].…`(줄 종류·항목·방식·무게·% …)가 한 줄씩 나란히, 꽝은 `emptyMode`·`emptyWeight`·`emptyPercent`. 줄의 조건(Block)은 나오지 않으므로 DB에서 본다.

### 5-10. 풀이 바뀌는 세 가지 — 진행 · 판 · 가진 것

| 풀이 바뀌는 이유 | 만드는 것 | 예 |
|---|---|---|
| 진행·도전과제(판이 끝나도 남음) | 항목 줄의 조건 | 연쇄 번개 줄의 조건 = 도전과제 `firstWin` 달성 / `profile.unlocked`에 `chainLightning` 있음 |
| 판에서 만들어진 것 | 목록 줄 | "핸드에서 무작위 1장" = 목록 줄 `run › battle.hand`. "이번 판에 만난 몬스터 중 하나" = 목록 줄 `run › metMonsters` |
| 이미 가진 것·이미 본 것 | 뺄 목록 | 유물 풀의 뺄 목록 `run › relics`, 이벤트 풀의 뺄 목록 `run › seenEvents` |
| 규칙이 계산한 후보·무게 | 코드 `Game.Roll(풀, …)` | 남은 체력이 많은 적일수록 잘 걸리는 저주 |

- 셋 다 **뽑는 순간**에 다시 본다. 풀을 플레이어 데이터에 따로 만들어 두지 않는다(업데이트로 항목이 늘어도 저장을 고칠 일이 없다, 2-3).
- 해금처럼 "프로필에 남는 진행"은 조건으로 두는 것이 기본이다. 목록 줄(`profile.unlocked`)은 해금한 것 전부를 같은 무게로 섞을 때 쓴다.
- "한 판에 한 번만 나오는 이벤트"는 뺄 목록 `run › seenEvents` + 이벤트를 시작하는 행동이 그 ID를 목록에 넣는다. Undo하면 목록도 돌아가므로 풀도 돌아간다.
- 확률 표시(`Chances`)도 지금의 목록·뺄 목록으로 계산한다(5-7).

### 5-11. 천장 — 뽑을수록 오르는 확률(무게를 밸런싱 값에서)

수집형 뽑기: "5성 0.6%. 74번째부터 오르고, 90번째는 사실상 확정. 5성이 나오면 처음부터." `Profile`에 `public int pity;`(지난 5성 뒤로 뽑은 횟수) · `public Dictionary<string, int> stock`(재화) · `public List<string> owned`(가진 캐릭터)가 있다고 본다. `Rules.RarityOf(game, id)`는 원본의 등급을 읽는 프로젝트 함수다.

**에디터**:

1. DB › 밸런싱 값 `pityFiveWeight`: `value` `0.6`, `curve` › `kind` `표(점 사이를 잇기)`, `points` (0, 0.6) · (73, 0.6) · (88, 60) · (89, 100000).
2. DB › 가중치 표 `banner`(안쪽 표 `fiveStar`·`fourStar`·`threeStar`는 등급별 캐릭터 표):

| `item` | `weight` | `weightBalance` | `weightStepKey` · `weightStepPath` |
|---|---|---|---|
| `fiveStar` | 1(쓰지 않음) | `pityFiveWeight` | `profile` · `pity` |
| `fourStar` | 5.1 | — | — |
| `threeStar` | 94.3 | — | — |

**코드** — 뽑는 행동. 뽑는 순간의 `pity`가 단계이고, 뽑은 뒤에 고친다.

```csharp
[Serializable, BlockInfo("뽑기")]
public class Pull : GameAction
{
    protected override void Run()
    {
        var profile = Game.Data().Profile;
        Game.Pay(GameIds.PlayerData.Profile.Id, "stock[gem]", 160, what: "gem.name");   // 딕셔너리의 키 하나(28장 2-4)
        var id = Game.Roll(GameIds.WeightTable.Banner);                                  // 5성 줄의 무게 = pityFiveWeight(pity)
        if (Rules.RarityOf(Game, id) == 5) profile.pity = 0; else profile.pity++;
        profile.owned.Add(id);
    }
}
```

| `pity`(이번이 몇 번째 − 1) | 5성 무게 | 5성 확률 |
|---|---|---|
| 0 ~ 73 | 0.6 | 0.6 / 100 = 0.6% |
| 80 | 0.6 + 59.4 × 7/15 ≈ 28.3 | 28.3 / 127.7 ≈ 22% |
| 88 | 60 | 60 / 159.4 ≈ 38% |
| 89(90번째) | 100000 | ≈ 99.9% |

- 무게는 뽑는 **순간**에 단계(`profile › pity`)를 읽는다. 그래서 `pity`를 고치는 줄은 `Game.Roll` 뒤에 둔다.
- 완전히 확정하려면 다른 두 줄도 89에서 0이 되는 밸런싱 값으로 둔다(4성 점 (88, 5.1) · (89, 0), 3성 점 (88, 94.3) · (89, 0) — 무게 0인 줄은 후보가 아니다). 또는 코드에서 `pity >= 89`이면 `Game.Roll(GameIds.WeightTable.FiveStar)`로 바로 뽑는다.
- 확률 공개 화면(`session.Tables.Chances`, 5-7)도 지금 `pity`로 계산된다.
- 배너마다 천장을 따로 세려면 단계 경로를 `pity[limited]`처럼 딕셔너리의 키로 둔다(`Dictionary<string, int> pity`, 11장 3-1).

### 5-12. 10개 사기 · 살 수 있는 만큼 — `BalanceSum` · `BalanceCount`

방치형: "커서 가격 15, 살 때마다 ×1.15(올림)." `RunState`에 `public int cursors;`(가진 개수)가 있다고 본다.

**에디터**(DB › 밸런싱 값 `cursorPrice`): `value` `15`, `kind` `비율로(×단계마다)`, `startX` `0`(단계 = 이미 가진 개수, 0부터), `rate` `1.15`, `rounding` `올림`.

```csharp
[Serializable, BlockInfo("커서 사기")]
public class BuyCursors : GameAction
{
    public int count = 1;         // 0이면 살 수 있는 만큼

    protected override void Run()
    {
        var run = Game.Data().Run;
        var n = count > 0 ? count : Game.BalanceCount(GameIds.Balance.CursorPrice, run.cursors, run.gold);
        if (n <= 0) throw Game.Fail("fail.noGold", "골드가 모자랍니다.");
        Game.Pay(GameIds.PlayerData.Run.Id, "gold", Game.BalanceSum(GameIds.Balance.CursorPrice, run.cursors, n), what: "gold.name");
        run.cursors += n;
    }
}
```

| 가진 개수(단계) | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 |
|---|---|---|---|---|---|---|---|---|---|---|
| 가격(올림) | 15 | 18 | 20 | 23 | 27 | 31 | 35 | 40 | 46 | 53 |

- 0개에서 10개 사기 = `BalanceSum(cursorPrice, 0, 10)` = 308.
- 골드 100으로 살 수 있는 만큼 = `BalanceCount(cursorPrice, 0, 100)` = 4(15 + 18 + 20 + 23 = 76, 다섯 번째까지면 103).
- 버튼 글("10개 — 308")은 화면이 같은 `BalanceSum`으로 그린다. 가격은 행동 안에서 다시 계산한다(5-3과 같은 이유). double을 넘는 가격은 4-1의 `BigNumber`.

### 5-13. 선택지 +1 · 보상 다시 뽑기 — 후보 수에 더할 숫자 · `Game.RerollChoice`

"엘리트를 잡으면 유물 셋 중 하나. 상점 상품 '넓은 시야'를 사면 넷 중 하나. 다시 뽑기는 판마다 2번, 한 번에 골드 5." `RunState`에 `public int rewardBonus;`(후보 수에 더할 숫자) · `public int rerolls = 2;`(남은 다시 뽑기)가 있고, `relicPool`에 유물이 8개 있다고 본다.

**에디터**:

1. 엘리트 승리의 결과에 `가중치 표에서 고르기`: `table`(가중치 표) `relicPool`, `count`(후보 수) `3`, `countBonus`(후보 수에 더할 숫자) `run` › `rewardBonus`, `unique`(서로 다른 것만) 켬, `key`·`path`(고른 것을 넣을 플레이어 데이터·필드) `run` › `relics`.
2. 상점 상품 "넓은 시야"의 결과: `플레이어 데이터에 주기 · run` › `rewardBonus` 증감 `1`.

**코드** — 다시 뽑기 행동과 기본 고르기 창의 버튼.

```csharp
[Serializable, BlockInfo("보상 다시 뽑기")]
public class RerollReward : GameAction
{
    public string choice;     // 기다리는 선택 id

    protected override void Run()
    {
        Game.Pay(GameIds.PlayerData.Run.Id, "rerolls", 1, what: "reroll.name");   // 남은 횟수
        Game.Pay(GameIds.PlayerData.Run.Id, "gold", 5, what: "gold.name");        // 골드
        if (!Game.RerollChoice(choice)) throw Game.Fail("fail.cannotReroll", "다시 뽑을 수 없는 선택입니다.");
    }
}

// Client 시작 코드(Bootstrap.Started)에서 한 번: 기본 고르기 창은 행동을 돌려주는 모든 선택에 '다시 뽑기' 버튼을 보인다(null이면 숨김)
// — 표로 만든 선택(table이 있다)만 고른다. 코드로 Ask한 선택에도 돌려주면 RerollChoice가 false라 누를 때 실패한다(29장 4-10b)
GameScreens.Reroll = choice => Game.Data().Run.rerolls > 0 && !string.IsNullOrEmpty(choice.table) ? new RerollReward { choice = choice.id } : null;
```

- 고르기 창을 직접 만들었으면 버튼은 `GameScreens.Do(() => new RerollReward { choice = Game.Choice.id }, whileChoosing: true)`로 보낸다. 보통의 `OnClick`·`GameScreens.Do`는 선택을 기다리는 동안 행동을 보내지 않는다(29장 4-10b).

| 순간 | 후보 | `rerolls` | `gold` |
|---|---|---|---|
| 엘리트 승리(`rewardBonus` 1) | 유물 4개 | 2 | 50 |
| 다시 뽑기 | 새 유물 4개 | 1 | 45 |
| Undo | 처음 4개 | 2 | 50 |
| 다시 뽑기 · 다시 뽑기 | 새 4개 · 또 새 4개 | 0 | 40 |
| 한 번 더 | 그대로(플레이어용 실패 `NotEnough` — 아무것도 바뀌지 않음) | 0 | 40 |

- 다시 뽑아도 후보 수는 처음 요구할 때의 4다. 그 사이 "넓은 시야"를 더 사도 지금 선택의 후보 수는 늘지 않는다.
- "넓은 시야"가 가지고 있는 동안만 켜지는 유물이면 필드를 올리지 않고 보정으로 둔다: `RunState`의 `hero`(id `hero`)에 `public int options;`, `countBonus` `run` › `hero.options`, DB › 패시브 효과 `wideView`(가진 목록 `run › relics`) `modifiers`(가진 동안 스탯 보정) `options` 플랫 `1` · `modifierTarget`(보정 받을 대상) `hero`. `hero`에 `id`가 있어 보정까지 읽으므로(3-3) 유물을 잃으면 후보도 저절로 줄어든다.
- Undo한 뒤 다시 뽑으면 같은 새 후보가 나온다. 난수도 행동과 함께 돌아가기 때문이다(2-3).
- 다시 뽑기는 앞 후보를 빼지 않는다. 앞에 보인 것을 빼려면 표의 뺄 목록에 `run › seenRewards`를 두고, `RerollReward`가 다시 뽑기 전에 `Game.Choice.options`의 id를 그 목록에 넣는다(보상을 고르면 비운다).
- `rerolls`를 판마다 2로 두는 것은 세션 구역이라 새 판이면 기본값으로 돌아오기 때문이다. 보상마다 2번이면 보상을 요구하는 행동에서 2로 되돌린다.
- 실패 글의 `{what}`은 텍스트 표의 `reroll.name`·`gold.name`이다(2-4).

---

## 6. 주의할 점 · 자주 하는 실수

| 증상 | 원인과 해결 |
|---|---|
| "[MomoiProject] 없는 밸런싱 값 '…'입니다(DB › 밸런싱 값)." | ID 오타이거나 그 항목이 DB에 없다(반영 스크립트가 돌았는지 `Logs/Editor.log`·DB 탭을 본다). ID는 `GameIds.Balance` 상수로 쓴다. 세션을 직접 만들면(서버·규칙 테스트) `session.Balance.Define`이 필요하다(4-2). |
| 곡선을 붙였는데 층이 올라도 값이 그대로 | 단계 없이 `Game.Balance(id)`로 읽었다(곡선을 보지 않는다). `Game.Balance(id, 층)`으로 읽는다. 또는 방식이 `없음`이다. |
| 1층 값이 기준 값과 다름 | 시작 단계가 1이 아니다. 0층부터 세면 시작 단계를 0으로(거듭제곱은 0보다 커야 한다). |
| 표 곡선에 적은 기준 값이 무시됨 | 표는 기준 값을 쓰지 않는다. 점의 값이 곧 값이다. |
| 최솟값·최댓값을 적었는데 자르지 않음 | `최솟값으로 자르기`·`최댓값으로 자르기`를 켜지 않았다. 자를 쪽을 켠다(2-2). |
| 소수가 나와 체력이 1.21 | 곡선은 `double`이다. 반올림을 고르거나 코드에서 `GameNumbers.RoundToInt`(라이브러리와 같은 반올림, 2-2). |
| `BalanceCount`가 예산이 남는데 멈춤 · `BalanceSum`이 생각보다 작음 / 경고 "곡선: N단계에서 0 이하가 됩니다…" | 가격 곡선이 그 단계에서 0 이하가 된다(단계마다 줄어드는 곡선). 값을 매길 수 없는 가격이라 그 단계부터 세지 않는다. `최솟값으로 자르기`를 켠다(4-1). |
| 방치형 후반에 값이 `∞`가 되거나 큰 수의 끝자리가 안 맞음 | DB 숫자 칸·밸런싱 값·`Game.Pay`는 `double`이다. 약 1e308을 넘으면 ∞이고, 15~16자리를 넘는 정수는 끝자리가 정확하지 않다. 그 값은 `BigNumber`로 두고 계산·비용 검사는 코드로 한다(→ `36-Grid-BigNumber.md`). |
| 천장을 걸었는데 확률이 그대로 | 줄의 `단계(플레이어 데이터)`를 비워 늘 기준 값이다. 또는 단계 값(`pity`)을 올리는 코드가 없거나 `Game.Roll` 앞에서 0으로 돌린다. 밸런싱 값의 곡선 방식이 `없음`이어도 그대로다(5-11). |
| 플레이 중 고친 값이 지금 적에게 안 보임 | 반영은 다음에 읽을 때부터다. 이미 만든 적의 체력은 판 데이터에 들어 있다. |
| 플레이 중 고친 값이 플레이를 끄니 사라짐 | DB의 작업본이다. 저장(Ctrl+S)해야 에셋에 남는다. |
| 밸런싱 값·가중치 표 탭에 **+ 새로 만들기**가 없음 · CSV의 새 ID가 건너뛰어짐 | 로직 쪽 종류라 항목 추가·삭제는 AI가 DB 반영 스크립트로 한다(3-1, 21장 3-1). |
| 물약이 1~2개 나와야 하는데 늘 1개 / 경고 "…Game.Roll(id)은 첫 ID만 돌려줍니다. 개수까지 쓰려면 Game.Roll(id, 1)로 목록을 받으세요." | `Game.Roll(id)`(하나)는 첫 ID만 돌려준다(2개 이상이 나오면 표마다 한 번 이 경고). `Game.Roll(id, 1)`의 목록으로 받는다(5-4). |
| 서로 다른 3장인데 2장만 나옴 | 꽝이 나왔거나, 표 전체(조건이 맞는 줄)에 서로 다른 항목이 3개보다 적다. 꽝 무게·조건·항목 수를 본다(2-3). |
| 서로 다른 것만인데 같은 ID가 둘 | 그 줄의 개수(최소~최대)가 2 이상이다. 개수만큼은 같은 ID가 들어간다. |
| Undo하고 다시 해도 같은 드롭 | 정상이다. 난수가 행동과 함께 돌아간다(2-3). |
| "가중치 표 '…'가 8단계 넘게 서로를 부릅니다" | 표 A가 B를, B가 A를 항목으로 가진다. DB 검증은 자기 자신만 잡으므로 표끼리의 고리를 끊는다. |
| 표시한 확률과 실제 횟수가 다름 | `Chances`는 지금 조건으로 본 한 번 뽑기의 확률이다. 안쪽 표는 한 줄, 서로 다른 것만·개수는 반영하지 않는다(2-3). 적은 횟수에서는 확률대로 나오지 않는다. |
| 3층이 됐는데 조건 줄이 안 나옴 | 조건의 플레이어 데이터·필드가 틀렸거나, 층을 올리는 행동보다 먼저 뽑았다. 조건은 뽑는 순간의 상태다. |
| `가중치 표에서 주기`로 덱에 카드가 안 들어감(경고 "인스턴스 'strike'의 형식(Card)이 DeckCard과(와) 다릅니다") | 덱은 `DeckCard` 목록이라 원본 사본이 들어가지 않는다. `Game.Roll`로 뽑아 코드에서 `Rules.NewCard`로 넣는다(5-5). |
| 드롭이 Undo·이어하기 뒤 달라짐 | 행동 밖(화면 코드)에서 `Game.Roll`을 불렀다. 행동 안에서 부른다. |
| 뺄 목록에 넣었는데 계속 나옴 | 목록의 요소가 표의 항목 ID와 다르다(덱 카드 id `card3` ≠ 카드 원본 `strike`). 원본 ID가 든 경로를 고른다(`run › deck[].card`). |
| 목록 줄에서 아무것도 안 나옴 | 그 순간 목록이 비었거나, 요소가 글자도 id 있는 객체도 아니다(숫자 목록 등). |
| 코드 풀 결과가 다른 뽑기를 더한 뒤 바뀜 | 두 곳이 같은 `stream`을 쓴다. 쓰는 곳마다 이름을 다르게 준다(4-4). |
| 실패 메시지가 `[fail.frozen]` | 기본 문장 없이 `Game.Fail(키, 값…)`을 썼고 표에도 키가 없다. 기본 문장을 함께 적거나 원문 표에 키를 넣는다. |
| 언어를 바꿨는데 실패 메시지가 그대로 | 그 언어 표에 키가 없다(기본 문장이 쓰인다). 또는 `Message`를 글자로 저장해 두었다. `GameFailure`를 들고 다시 읽는다(5-8). |
| "판 › battle.energy이(가) 모자랍니다" | 이름을 주지 않았다. `Game.Pay(…, what: "energy.name")` 또는 결과 `비용 내기`의 `보일 이름`에 텍스트 키·글을 적는다(2-4, 5-8). |
| `Game.Pay`가 "모자랍니다" 대신 "[MomoiProject] 비용: '…'에 필드 '…'가 없습니다" | 경로 오타이거나 숫자가 아닌 필드다. 프로그래머 실수라 플레이어용 실패가 아니다. 경로를 고친다. |
| 테스트에서 `Assert.Throws<InvalidOperationException>`이 실패("GameFailure가 났다") | NUnit의 `Assert.Throws<T>`는 정확히 그 형식만 받는다. 플레이어용 실패(`Game.Fail`·`Game.Pay`·`Game.Answer`·`Game.Use`)는 `Assert.Throws<GameFailure>`로 잡는다(상속한 형식까지 받으려면 `Assert.Catch<InvalidOperationException>`). |
| `catch (GameFailure)`가 컴파일 오류(이미 잡힌 형식) | `catch (InvalidOperationException)`를 위에 두었다. 상속한 형식(`GameFailure`)을 먼저 둔다. |
| `Game.Fail(…)`을 썼는데 행동이 그대로 진행 | `throw`가 없다. `Game.Fail`은 예외를 만들기만 한다. |
| 프로그래머 실수 메시지가 플레이어에게 보임 | 화면이 모든 `InvalidOperationException`의 `Message`를 보여 준다. `GameFailure`만 보여 주고 나머지는 로그로(5-8). |
| `countBonus`(후보 수에 더할 숫자)를 골랐는데 후보가 늘지 않음 | 그 필드가 0이거나 숫자로 읽을 수 없다(0으로 본다). 표에 서로 다른 항목이 모자라거나 꽝이 나왔다. 요약 문장은 더할 숫자의 자리만 보이니(`(+run › rewardBonus)`) 실제 수는 고르기 창에서 본다(3-3). 패시브 효과 보정으로 올렸다면 그 필드의 부모 객체에 `id`가 있어야 보정까지 읽는다(`id`가 없는 `run` 바로 아래 필드는 보정을 읽지 않는다). |
| `Game.RerollChoice`가 `false` | `가중치 표에서 고르기`로 만든 선택이 아니다(`Game.Ask`·`선택 요구`). 이미 답했다. 표에서 아무것도 나오지 않았다(모두 꽝)(4-6). |
| 다시 뽑았더니 같은 유물이 또 나옴 | 다시 뽑기는 앞 후보를 빼지 않는다. 뺄 목록으로 뺀다(5-13). |
| 다시 뽑기 버튼이 반응하지 않음 | 보통의 `OnClick`·`GameScreens.Do`는 기다리는 동안 행동을 보내지 않는다. `GameScreens.Do(() => 행동, whileChoosing: true)`, 기본 고르기 창이면 `GameScreens.Reroll`(29장 4-10b). |
