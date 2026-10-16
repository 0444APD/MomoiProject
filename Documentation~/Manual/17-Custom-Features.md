# 17. 프로젝트 고유 기능 만들기(퀘스트 예)

**한 줄 요약**: 먼저 기본 종류 **패시브 효과**(DB › 패시브 효과)로 되는지 본다 — "가지고 있으면 발동"하는 것과 가진 동안 붙는 스탯은 코드 없이 패시브 효과로 만든다(0절). 패시브 효과에 없는 규칙(한 번만·하루 한도·단계·선행)이 있는 "Trigger → 결과" 기능(퀘스트·일일 미션·튜토리얼 단계)만 프로젝트 DB 종류로 만든다: 클래스에 `Trigger` 필드와 `List<Outcome>` 필드를 두고 `[TriggerSlot]`·`[OutcomeSlot]`을 붙이면 같은 편집 화면·검증이 붙고, 런타임에서는 `session.Triggers.Register`로 감시하고 `RunOutcomes`로 결과를 실행한다(1절부터).

> **선택 요소**: 패시브 효과의 DB 종류(데이터 칸 클래스)는 GBible에 없다 — 쓰는 프로젝트가 템플릿 `Documentation~/AI/Templates/Elements/PassiveData.cs.txt`를 `Assets/MomoiProject/Code/Rules/Elements/PassiveData.cs`로 들인다(AI가 단계 1에서). 칸이 더 필요하면 그 클래스에 더한다(프로젝트 변경). 엔진(이 장의 동작·기본 Block·`Game` 함수)은 GBible에 있고, 이 장의 칸 설명은 템플릿 그대로일 때다. 들이지 않은 프로젝트에는 탭·기본 Block이 보이지 않는다(AI 문서 Reference 4-1).

**언제 쓰는가**

- 유물·특성·장비 효과·축복·조커·보드 유닛처럼 가진 목록에 있는 동안 발동하는 것(놓인 순서대로·따라 하기·가진 동안 스탯·순간마다 다른 결과·N초마다 포함) → 기본 종류 패시브 효과(0절의 표). 새 DB 종류를 만들지 않는다.
- 퀘스트, 일일 미션, 튜토리얼 단계, 이벤트 기간 보상처럼 "언제(조건) → 무엇을(보상)"을 항목마다 조립하는데, 진행·저장·반복 규칙이 기본 기능(도전과제 = 한 번, 로그 = 숫자 기록, 패시브 효과 = 가진 동안 매번)과 다른 것 → 프로젝트 DB 종류(1절부터, 경계는 0-5).

이 문서에서 쓰는 용어:

| 용어 | 뜻 |
|---|---|
| Trigger | 언제 발동하는지(→ `11-Triggers-Conditions.md`). |
| Outcome | 무엇을 하는지(→ `12-Outcomes.md`). |
| DB 종류 | `GameData`를 상속한 클래스. 상속하면 코드 추가 없이 DB에 탭이 생긴다. |
| 감시 | `session.Triggers`가 Trigger를 지켜보다가 참이 되는 순간 등록한 함수를 부르는 것(`Register`). |
| TriggerContext (컨텍스트) | 발동한 순간의 정보(`Owner`·`Event` …). |
| Permanent | `session.Data.Permanent()` 안의 변경은 Undo 이력에 들어가지 않는다(도전과제 달성이 쓰는 방식). |
| 패시브 효과 | DB › 패시브 효과의 항목 하나("가지고 있으면 발동"). 칸은 0-2. 코드 형식 `PassiveData`(DB — 프로젝트가 템플릿에서 들인 클래스), `PassiveDefinition`(실행 중). ID 상수는 `GameIds.Passive.X`. |
| 가진 목록 | 플레이어 데이터 안의 ID 목록·객체 목록·ID → 레벨 딕셔너리(`run › relics`, `run › jokers`, `run › perks`). 패시브 효과 ID가 들어 있으면 가진 것이다. |
| 가진 쪽 | 가진 목록을 품은 객체(`run`, `hero`, 동료마다). 패시브 효과의 결과가 그 쪽을 사용자로 돈다. `perElement`(놓인 것마다)를 켜면 목록의 요소 하나하나(조커 하나)가 가진 쪽이다. |
| 조각 | 객체 목록의 요소 하나(조커 칸의 조커 하나). 조각마다 고유 `id`와, 어떤 패시브 효과인지 가리키는 필드(`joker`, 또는 원본 인스턴스 ID `card`)를 둔다. |
| 레벨 | 몇 개 가졌나. ID가 든 횟수, 딕셔너리의 숫자, 또는 `levelField`(레벨 필드)의 숫자. `Game.Owned`·값 틀 `{level}`이 읽는다. 0 이하면 꺼진 것이다(발동·보정·빌려 쓰기 없음). |
| 효과 배율 (Power) | 패시브 효과가 발동할 때·액티브 효과를 쓸 때 행동 직전 이벤트 `효과 배율`(`PowerIntent`)을 한 번 거친 배율(기본 1). 기본 효과 Block이 수치에 곱한다("이 유닛의 수치 2배", 16장 2-12, 28장 4-14). |
| Modifier | 스탯(숫자 필드)에 붙는 보정 한 줄(→ `28-Attributes-Intercepts.md`). 패시브 효과의 `modifiers`(가진 동안 스탯 보정)도 같은 모양으로 계산된다. |

---

## 0. 무엇으로 만드나 — 먼저 기본 종류 '패시브 효과'로 되는지 본다

| 기획 문장 | 만드는 것 |
|---|---|
| "유물 닻: 전투 첫 턴에 방어도 8" · "가시 갑옷: 받는 피해 −5" | DB › 패시브 효과(0-3), 코드 없음 |
| "특성 '중갑': 받는 피해 −2"(동료마다 다른 특성) | DB › 패시브 효과, 가진 목록 `party[].traits`(가진 쪽 = 그 동료) |
| "패시브 스킬 레벨 n: 처치마다 체력 +n" · "껍질 레벨 n: 받는 피해 −n" | DB › 패시브 효과, 가진 목록 = ID → 레벨 딕셔너리. 값 틀 `{level}`·`{level:2/4/7}` 또는 코드 `Game.Owned` |
| "조커: 왼쪽부터 차례로 +4 배수 · ×2 배수" | DB › 패시브 효과, 가진 목록 = 객체 목록 `run › jokers` + `matchField`(일치 필드) + `perElement`(놓인 것마다)(0-3) |
| "유물 힘의 부적: 가진 동안 공격력 +2 / +3 / +4 / +6(레벨별)" | DB › 패시브 효과의 `modifiers`(가진 동안 스탯 보정)(0-3). 트리거 없음 |
| "Blueprint: 오른쪽 조커의 액티브 효과를 따라 한다" · "Brainstorm: 맨 왼쪽 조커를 따라 한다" | DB › 패시브 효과 + 결과 '빌려 쓰기'(0-3) |
| "Green Joker: 손을 낼 때 배수 +1 · 버릴 때 −1 · 점수 때 그 배수를 더한다" · "Loyalty Card: 6번째 손마다 ×4" | DB › 패시브 효과의 `rules`(발동 더) + 조건 '사용자 필드 검사'(0-3) |
| "Fury: 같은 줄에 혼자면 공격력 +3" · "같은 아티팩트 둘이면 사본마다 값을 더한다" | DB › 패시브 효과의 `modifierWhen`(보정 조건) · `stacking`(여럿일 때)(0-3) |
| "가진 동안 5초마다 골드 +1" | DB › 패시브 효과의 `pulse`(주기)(0-3) |
| "버프 3턴" · "독" · "쿨다운" | 상태 효과(29장). 걸고 풀고 끝나는 것 |
| "슬라임 5마리를 잡으면 보상(한 번)" · "일일 미션(하루 3번)" · "튜토리얼 단계" | 이 장의 프로젝트 고유 기능(1절부터) |

> **규칙: "가지고 있으면 발동"은 DB › 패시브 효과로 만든다.** 유물·특성·패시브 스킬·장비 효과·축복이 모두 이것이다. 새 DB 종류(1절부터)는 완료 기록·한도·단계·선행처럼 패시브 효과에 없는 규칙이 있을 때만 만든다. 영구 상태 효과로 걸어 두는 우회도 하지 않는다(29장 1절). "가진 동안 스탯"도 얻을 때 보정을 붙이고 잃을 때 떼는 코드로 만들지 않는다. 패시브 효과의 `modifiers`(가진 동안 스탯 보정)에 둔다.

### 0-1. 동작 규칙

- 정의(DB › 패시브 효과의 항목)는 게임이 켜질 때 모두 등록된다. 순간마다(이벤트를 받을 때·행동 직전 이벤트를 가로챌 때·조건 값이 바뀔 때) **가진 목록에 그 ID가 있는 쪽(가진 쪽)마다** 발동을 따로 검사하고, 참인 가진 쪽마다 결과를 낸다(상태 효과의 "가진 쪽마다 검사"와 같다, 29장 2-3). 보통 패시브 효과는 가진 쪽마다 한 번이다. 같은 유물이 두 개라도 가진 쪽이 하나면 한 번 돌고, 개수는 레벨로 읽는다. `perElement`(놓인 것마다)를 켜면 요소마다 한 번이다(아래).
- **가진 목록**: 플레이어 데이터 하나 + 그 안의 필드. 담는 모양에 따라 "몇 개 가졌나"(레벨)가 정해진다.

| 가진 목록의 모양 | 예 | 몇 개(레벨) |
|---|---|---|
| ID 목록(`List<string>`) | `run › relics` | 그 ID가 든 횟수 |
| 객체 목록(`List<Joker>`) | `run › jokers` | 일치하는 요소 수. `levelField`(레벨 필드)를 고르면 그 숫자의 합(0 이하인 요소는 0) |
| ID → 레벨 딕셔너리(`Dictionary<string, int>`) | `run › perks` | 그 키의 숫자(없으면 0 = 가지지 않음, 0 아래도 0 = 꺼짐) |
| 글자 필드 하나 | `run › hero.weaponPassive` | 같으면 1 |

- **`matchField`(일치 필드)**: 객체 목록에서 요소의 어느 필드에 패시브 효과 ID가 있는지 정한다. 조커 칸 `run › jokers`의 요소가 `{ id: "j1", joker: "plusMult" }`이면 일치 필드 `joker`다. 그 필드가 **글자 목록**이면 그 안에 패시브 효과 ID가 있을 때 가진 것이다. 유닛의 `traits` = `["teeth", "wild"]`, 참(Charm)이 조각에 붙인 키워드처럼 조각 하나가 패시브 효과 여럿을 가진다. 비우면 요소 자신(ID 글자)이나 요소의 `id`가 패시브 효과 ID여야 한다. 같은 조커 둘을 놓으려면 조각마다 `id`가 달라야 하므로, 조각 목록은 고유 `id` + 원본 ID 필드로 두고 일치 필드를 쓴다.
- **`matchVia`(원본에서 읽기)**: 일치 필드가 원본(DB › 인스턴스) ID를 담을 때 쓴다. 그 원본의 이 필드(글자·글자 목록)에 패시브 효과 ID가 있으면 가진 것이다. 판 데이터의 조각은 `{ id, card: "card.foxee" }`만 두고, 패시브 효과 목록·가격·등급 같은 고정 값은 원본 `Card`(`passives = [wild, teeth]`)에 둔다(0-3). 원본은 `GameSession.Instances`(원본 ID → 원본 객체)로 찾는다. 게임(Unity)에서는 런타임이 DB › 인스턴스로 채우고, 서버·테스트처럼 세션을 직접 만들면 직접 넣는다.
- **`levelField`(레벨 필드)**: 요소의 이 숫자 필드가 그 요소의 레벨이다(`{ joker: "plusMult", level: 3 }`). 비우면 요소 하나 = 1. ID 목록·딕셔너리에는 쓰지 않는다(딕셔너리는 값이 곧 레벨).
- **레벨이 0 이하인 요소는 꺼진 것이다.** 발동도 보정도 없고 `Game.Owned`에서 0이며, 빌려 쓰기로도 빌리지 못한다. 제약으로 잠깐 꺼지는 조각을 레벨 0으로 둔다. 그래서 원작 레벨이 ★0부터인 게임은 레벨 필드 = ★ + 1(꺼지면 0)로 둔다.
- **가진 쪽** = 그 목록을 품은 객체. `run › relics`면 `run`(판 하나에 하나), `run › hero.perks`면 `hero`, 앞마디에 `[]`를 쓴 `run › party[].traits`면 그 목록을 가진 동료마다다.
- **결과의 컨텍스트**: `User` = 가진 쪽(놓인 것마다면 그 요소), `Picked` = 가진 쪽 하나, `Owner` = 패시브 효과 ID, `Event` = 받은 이벤트(행동 직전 이벤트 포함), `IsIntercept`, `Power` = 효과 배율. 결과 `대상에게 효과`의 대상을 비우거나 `사용자 자신`이면 가진 쪽이다. 가진 쪽이 `run`처럼 싸우는 개체가 아니면 대상 Block으로 고른다(기본 대상 Block 16장 2-6, 또는 프로젝트의 `주인공` Block — AI 문서 `BuildGuide/04-AddingContent.md` 9절).
- **효과 배율**: 발동할 때마다(가진 쪽마다) 행동 직전 이벤트 `효과 배율`(`PowerIntent { ownerId = 패시브 효과 ID, userId = 가진 쪽 id, multiplier = 1 }`)을 한 번 거쳐 `Power`에 둔다. 기본 효과 Block이 곱한다(`필드 바꾸기`의 더하기 값 · `상태 효과 걸기`의 중첩 · `스탯 보정`의 값, 16장 2-12). 행동 직전 이벤트 '효과 배율'을 받는 패시브 효과 자신의 결과는 배율을 거치지 않는다(같은 행동 직전 이벤트를 다시 만들어 끝없이 돌지 않게).
- **가진 쪽 필드**: 이벤트·행동 직전 이벤트의 그 필드가 가진 쪽 id일 때만 발동한다("내가 맞을 때만" = 피해 행동 직전 이벤트의 `targetId`). 가진 쪽에 `id`가 있어야 한다. `run`처럼 id가 없는 가진 쪽이면 이 칸을 채운 패시브 효과는 발동하지 않으므로 행동 직전 이벤트 조건(`targetId 일치 hero`)으로 거른다.
- **침묵**: 가진 쪽(조커·유닛처럼 `id`가 있는 것)에 '걸린 쪽의 패시브 효과를 끔'(`silencesPassives`) 상태 효과가 걸려 있으면 그 가진 쪽의 패시브 효과는 발동·보정이 모두 없고 `Game.Owned`도 0이다(Ink·디버프된 조커·봉인, 29장). 침묵한 조각은 빌려 쓰기로도 빌리지 못한다. 액티브 효과까지 막으려면 행동 직전 이벤트 `사용` 막기를 함께 건다(16장 2-11).
- **스위치 패시브 효과**: 발동·결과·보정·주기가 모두 없어도 된다. 가지고 있는지만 `Game.Owned`로 읽는 판정 스위치다(Four Fingers: 메인 룰이 `Game.Owned(GameIds.Passive.FourFingers) > 0`이면 카드 넉 장으로도 플러시를 판정).
- **얻기·잃기**는 가진 목록에 ID를 넣고 빼는 것이다(결과 `플레이어 데이터에 주기`·`가중치 표에서 주기`·`가중치 표에서 고르기`, 코드 `run.relics.Add(id)`). 객체 목록이면 조각 객체를 넣고 뺀다(`run.jokers.Add(new Joker { id = Game.NewId("joker"), joker = id })`). 가진 목록이 딕셔너리면 가중치 표에서 같은 ID를 다시 고를 때 그 키가 +1 된다(레벨업, 30장 3-3). 등록을 바꿀 일이 없고, 가진 것이 플레이어 데이터라 Undo·저장·멀티플레이가 따라온다.
- 상태 효과와 다른 점: 지속·중첩·만료가 없고, 가진 것이 게임의 데이터(유물 목록·특성 목록)에 그대로 있다.
- 이벤트 받기·행동 직전 이벤트로 발동한 결과는 그 행동 안에서 돈다. 조건 충족만으로 된 발동은 값이 바뀐 행동이 끝난 뒤의 평가에서 돈다(행동 밖, 29장 2-3과 같다). 판 값을 바꾸는 패시브 효과는 이벤트 받기·행동 직전 이벤트로 발동시킨다. 주기는 따로 행동 하나로 돈다(아래 `pulse`).

**`perElement`(놓인 것마다)** — 놓인 순서가 결과를 바꾸는 조각(조커·참·보드 유닛)에 켠다.

- 켜면 가진 쪽 = 목록의 요소 하나하나다. 결과의 `User`·`Picked`가 그 요소다. 결과 `대상에게 효과`의 `사용자 자신`도 그 조각이다.
- **같은 목록을 보는 `perElement` 패시브 효과끼리는 목록 순서(자리)대로 섞여서 발동한다.** 같은 목록 = 가진 목록의 플레이어 데이터와 필드가 같은 것이다. 패시브 효과의 등록 순서(DB 순서)가 아니라 조커 칸 왼쪽부터, 보드 앞 칸부터 돈다. 그래서 "+4 배수"가 왼쪽이고 "×2 배수"가 오른쪽이면 (1 + 4) × 2 = 10, 자리를 바꾸면 1 × 2 + 4 = 6이다. 순서가 목록 순서와 다른 게임(와일드프로스트는 열 우선)은 목록을 그 순서로 담는다(37장 5-3) — 그러면 이 차례가 원작과 같다.
- 행동 직전 이벤트로 발동하면 행동 직전 이벤트 잎의 순서(`order`)가 먼저다. 같은 순서 칸 안에서 목록 순서로 돈다(28장 2-3).
- 같은 조각이 둘이면 각자 발동한다(보통 패시브 효과는 가진 쪽마다 한 번).
- 발동 도중에 앞 칸의 결과가 목록에서 뺀 요소는 발동하지 않는다. 그 순간 목록에 남은 것만 돈다(단검이 오른쪽 조커를 없애면 그 조커는 이번에 돌지 않는다). 발동 도중에 새로 놓인 요소는 이번 순간에는 돌지 않는다.
- `perElement`를 끈 패시브 효과는 이 줄에 섞이지 않는다. 가진 쪽마다 한 번, 등록 순서로 돈다.
- 조건 충족만으로 된 발동은 순간이 패시브 효과마다 따로라 섞지 않는다. 순서가 중요한 조각은 이벤트 받기·행동 직전 이벤트로 발동시킨다.
- 가진 쪽 필드를 채우면 그 요소의 `id`와 비교한다("이 유닛이 맞을 때만").

**`rules`(발동 더(다른 순간))** — 같은 패시브 효과가 순간마다 다른 결과를 낼 때 줄마다 트리거 + 결과를 더한다. 본 발동(`trigger` → `outcomes`)이 첫 줄이고 `rules`가 그 아래 줄이다. Green Joker = 본 발동 `점수` 행동 직전 이벤트 받기 → 쌓인 배수 더하기, 줄 1 `손 냄` → 이 조커 `mult` +1, 줄 2 `버림` → −1(0-3).

- 줄마다 따로 검사해 참인 줄마다 그 줄의 결과를 낸다. 가진 쪽 · 가진 쪽 필드 · `perElement`의 목록 순서는 본 발동과 같다(가진 쪽 필드는 패시브 효과에 하나다).
- 같은 순간에 맞는 줄이 여럿이면 위 줄(본 발동)부터 돈다. 이벤트를 받는 순간에는 조건 충족 잎이 없는 줄이 있는 줄보다 먼저다(11장 6-1). 그래서 "처치마다 `counter` +1"과 "처치 AND `counter` ≥ 3이면 보상·`counter` 0"을 같은 이벤트에 걸어도 +1이 먼저 돈다.
- 쌓기(`필드 바꾸기`) · 쌓인 값으로 거르기(조건 `사용자 필드 검사`, 11장 2-8) · 쓰기(값 틀 `{user:경로}`, 12장 2-4a) · 되돌리기(`필드 바꾸기` 값으로 바꾸기 0)를 줄로 나눠 코드 없이 만든다.
- `[BindTo]`는 본 발동의 결과(`outcomes`)만 바꾼다. `rules`의 결과는 DB에 둔 그대로 돈다.

**`pulse`(주기(초))** — `pulse` 또는 `pulseByLevel`(레벨별 주기(초, 1레벨부터))이 0보다 크면 가진 동안 게임 시간 그 초마다 **가진 쪽마다** 결과(`outcomes`)를 한 번 낸다. 트리거가 없어도 된다.

- 레벨별 주기는 레벨 번째 값이다(넘치면 마지막 값). 놓인 것마다면 조각마다 자기 레벨의 주기로 돈다(레벨 1 = 2초, 레벨 2 = 1초).
- **경계는 시계 기준이다.** 게임 시각 0부터 N초 단위(2초면 2·4·6초 …)에 낸다. 얻은 때부터 세지 않으므로 얻고 첫 결과까지가 N초보다 짧을 수 있다. 멈추기·앞당기기·처음부터 다시 세기는 없다. 그런 카운터는 조각의 필드 + 틱 행동·이벤트로 만든다(`rules` 한 줄이 이벤트를 받아 필드를 바꾼다). 상태 효과의 주기(29장)는 건 때부터 센다.
- 프레임 끝(고정 틱이면 틱마다)에 경계를 넘은 가진 쪽이 있으면 행동 하나("패시브 효과 주기" `PassivePulseRun`, 공개 범위 개발자)로 낸다. 한 프레임에 경계를 여러 번 넘었으면 넘은 만큼 낸다(가진 쪽마다 최대 8번).
- 앞 가진 쪽의 결과가 가진 쪽을 목록에서 뺐거나 껐으면(침묵·레벨 0) 그 가진 쪽은 그 주기를 내지 않는다. 밀린 회차를 내는 도중에 사라진 조각(터지고 사라짐)도 남은 회차를 내지 않는다. 이벤트 발동과 같다.
- **결과가 실패하면**(결과의 비용이 모자람 등) 그 행동은 취소되고, 가진 쪽의 회차마다 저장점으로 감싼 행동("패시브 효과 주기(실패 감싸기)")으로 한 번 더 낸다. 실패한 회차는 그 결과가 바꾼 것만 되돌리고 경고 "패시브 효과 '…'(가진 쪽 id)의 주기 결과가 실패해 그것이 바꾼 것만 되돌렸습니다 — (이유)"를 남긴다. 그 가진 쪽의 남은 밀린 회차는 버리고, 다른 가진 쪽·패시브 효과의 주기는 그대로 돈다. 지난 구간은 실패해도 지난 것으로 보므로 같은 실패가 매 프레임 되풀이되지 않고, 다음 경계에서 다시 낸다.
- **게임 시계가 옮겨지면 그 시각까지의 주기는 낸 것으로 본다.** 판을 불러올 때, Undo가 시간 규칙 행동(주기·만료)을 함께 되돌려 시계를 되돌릴 때, 스냅샷으로 되감을 때다. 앞으로 크게 옮겨도 밀린 주기를 한꺼번에 내지 않고, 뒤로 돌리면 그 시각 뒤의 경계에서 다시 낸다. 조건 충족의 폴링(일정 간격으로 다시 보는 조건) 시각도 함께 맞춘다.
- 주기에는 받은 이벤트가 없다(`Event` = `null`). 가진 쪽 필드도 보지 않는다. 본 발동이 있으면 둘이 같은 `outcomes`를 쓴다.

**`modifiers`(가진 동안 스탯 보정)** — 가진 동안만 붙는 스탯 보정(Modifier)이다. "가진 동안 공격력 +2"는 트리거 없이 이것만 둔다.

- 줄마다: `attribute`(스탯, 숫자 필드 이름) · `kind`(칸: 플랫·퍼센트·독립 곱) · `value`(값) · `perLevel`(레벨마다) · `byLevel`(레벨별 값) · `target`(받을 대상).
- 그 레벨의 값: `byLevel`(레벨별 값)이 있으면 레벨 번째 값이다(1레벨부터, 넘치면 마지막 값). 레벨별 값이 있으면 `value`·`perLevel`은 보지 않는다. 없으면 `value`이고, `perLevel`(레벨마다)을 켜면 값 × 레벨이다.
- **저장하지 않는다.** `Game.Attribute`·`Game.Modifiers`를 읽을 때마다 가진 목록을 보고 계산한다. 그래서 잃으면 저절로 빠지고, 얻는 순간·잃는 순간을 따로 다룰 일이 없다. Undo·저장과 따로 놀지 않는다(가진 목록이 돌아가면 보정도 돌아간다). 침묵·레벨 0인 가진 쪽은 보태지 않는다.
- 계산은 저장된 Modifier와 같다. 같은 칸끼리 더하고 칸끼리 곱한다(28장 2-1). `Game.Modifiers(대상, 스탯)` 목록에도 보인다(`source` = 패시브 효과 ID, `id` = `"passive:ID"`).
- **받는 쪽**: 비우면 가진 쪽(놓인 것마다면 그 요소)이다. `modifierTarget`(보정 받을 대상)에 가진 목록 플레이어 데이터 안의 객체 경로(`hero`)를 적으면 그 객체가 받는다. `party[]`처럼 `[]`를 쓰면 그 모두가 받는다("모든 동료 공격력 +2"). 줄의 `target`(받을 대상)을 적으면 그 줄만 그 대상이 받는다(비우면 `modifierTarget`).
- 받는 쪽은 스탯을 가진 개체라 `id`가 있어야 한다. 가진 목록이 `run › relics`면 가진 쪽 `run`에는 id가 없으므로 `modifierTarget`(보정 받을 대상)에 `hero`를 적는다.
- **`stacking`(여럿일 때)**: 받는 쪽을 경로로 정해 가진 쪽 여럿이 한 받는 쪽에 보탤 때 값을 합치는 방법이다(받는 쪽이 가진 쪽 자신이면 가진 쪽 하나라 상관없다). 예: 레벨별 값 `3`·`6`·`10`, 레벨 2와 레벨 3 사본 둘.

| `stacking` | 값 | 예 |
|---|---|---|
| `레벨을 더해 한 번`(`SumLevels`, 기본) | 레벨 합의 값 | 레벨 5 → 10(넘치면 마지막 값) |
| `가진 쪽마다 값을 더함`(`SumValues`) | 가진 쪽마다 그 레벨의 값을 더한다 | 6 + 10 = 16(같은 아티팩트 둘) |
| `가장 높은 레벨 하나만`(`Highest`) | 가장 높은 레벨의 값 | 10([고유]) |

- **`modifierWhen`(보정 조건)**: 조건 충족 잎으로 된 Trigger(`AND`·`OR`·`NOT`으로 묶어도 된다). 참인 가진 쪽의 보정만 붙는다. `Game.Attribute`로 읽을 때마다 가진 쪽마다 검사한다(사용자 = 가진 쪽). 예: `자리 검사` '같은 줄에 혼자'(Fury), `사용자 필드 검사` `kills` 이상 `40`. 조건이 같은 스탯을 다시 읽어 되돌아오면 그 보정은 없는 것으로 본다(끝없이 도는 것을 끊는다).

**빌려 쓰기** — 결과 `빌려 쓰기`(`BorrowOutcome`)는 다른 조각의 패시브 효과를 지금 순간(이벤트·행동 직전 이벤트)으로 한 번 더 낸다. 복사·흉내 조각(Blueprint·Brainstorm)이 이것이다.

- 칸은 `targets`(빌릴 조각) 하나다. 대상 Block 목록으로 고른다(예: `자리로 고르기` — 오른쪽, 목록 맨 처음. 자리는 `37-Placement.md`).
- 고른 조각의 패시브 효과는 `matchField`(일치 필드)·`matchVia`(원본에서 읽기)로 찾는다. 그 패시브 효과의 가진 목록에 그 조각이 들어 있고 일치하는 것이다. 조각 하나가 패시브 효과 여럿을 가지면 모두 빌린다. 빌려 쓰는 조각 자신과 꺼진 조각(침묵·레벨 0 이하)은 빌리지 못한다. 직접 발동하지 않는 조각은 빌려도 발동하지 않는다.
- 빌려 쓸 때 그 패시브 효과의 발동(본 발동과 `rules`의 줄마다)을 **원본 조각을 가진 쪽으로** 검사한다. 참인 줄마다 결과를 낸다. 그래서 빌려 쓰는 쪽의 발동과 원본의 발동이 모두 그 순간에 참이어야 한다. 여러 순간에 도는 조각을 모두 따라 하려면 빌려 쓰는 쪽의 발동을 `OR`로 묶는다.
- **가진 쪽 필드는 빌리는 쪽 기준이다.** 빌린 패시브 효과의 `holderField`(가진 쪽 필드)는 원본이 아니라 빌려 쓰는 조각의 `id`와 비교한다. 메인 룰이 조커마다 "이 조커의 차례" 이벤트(`pieceId`)를 쏘고 조커 패시브 효과가 가진 쪽 필드 `pieceId`로 제 차례만 받는 게임에서, Blueprint는 자기 차례에 오른쪽 조커를 빌린다. 빌린 결과 안에서 또 빌리면(Brainstorm → Blueprint → 오른쪽 조커) 처음 빌린 조각(그 차례의 조각)과 비교한다. 그래서 Brainstorm 차례에 Blueprint를 빌리면 Blueprint의 오른쪽 조커가 난다.
- 결과의 사용자 = **원본 조각**이다. 원본의 값(`{level}`, 원본의 필드)을 읽는다. 컨텍스트의 `IsBorrowed`가 참이다.
- 효과 `필드 바꾸기`는 빌려 쓰는 중에 **원본 조각 자신의 필드는 바꾸지 않는다**. 쌓이는 값은 원본이 직접 발동할 때만 쌓인다. 다른 대상(카드 등)의 필드는 바꾼다.
- 서로 빌리는 고리(Blueprint ↔ Brainstorm)는 끊는다. 이미 빌려 쓰는 중인 같은 패시브 효과·같은 원본을 다시 빌리면 하지 않는다.
- 빌리는 것은 결과뿐이다. 원본의 `modifiers`(가진 동안 스탯 보정)는 빌리지 않는다.

### 0-2. 에디터에서 — DB › 패시브 효과

`MomoiProject → DB` → 사이드바 **DB** 그룹의 **패시브 효과** 탭. 로직 쪽 종류라 항목·Block 조립은 AI가 DB 반영 스크립트로 한다(`EditorDatabase.Staging.Add(typeof(PassiveData), "thornArmor")`, 접두어 `passive`, 21장 3-1). 새 항목은 저절로 적용되어 `GameIds.Passive.ThornArmor`가 생긴다(Unity가 다시 컴파일한 뒤 — 20장 7절). 개발자는 문장 안의 값을 바꾼다.

칸 이름표는 변수 이름이고, 마우스를 올리면 툴팁(아래 표의 이름 + 짧은 설명 — 뜻은 0-1)이 보인다.

| 칸 | 툴팁 | 값 |
|---|---|---|
| `ownerKey` | 가진 목록(플레이어 데이터) | 기본 `run` |
| `ownerPath` | 필드(목록·딕셔너리) | 기본 `relics`. 비우면 값 전체. 앞마디에 `[]`를 쓰면 가진 쪽이 여럿(`party[].traits`) |
| `matchField` | 일치 필드(객체 목록) | 요소의 글자·글자 목록 필드 이름(`joker`, `traits`). 비우면 요소 자신(ID 글자)·요소의 `id` |
| `matchVia` | 원본에서 읽기 | 원본 클래스의 필드 이름(`passives`). 보통 비운다 |
| `perElement` | 놓인 것마다 | 끔(기본) / 켬 |
| `levelField` | 레벨 필드 | 요소의 숫자 필드 이름(`level`). 비우면 요소 하나 = 1. 0 이하면 그 요소는 꺼진다 |
| `trigger` | 발동 | `+ 블럭`: 이벤트 받기 · 조건 충족 · 행동 직전 이벤트. 보정·주기만 쓰면 비운다 |
| `holderField` | 가진 쪽 필드 | 트리거가 고른 이벤트·행동 직전 이벤트의 필드(`targetId`). 비우면 "가진 쪽 모두". `rules`의 줄에도 쓰인다 |
| `outcomes` | 결과 | 결과 종류 전부(행동 직전 이벤트 고치기·빌려 쓰기 포함). 주기도 이 칸을 낸다 |
| `rules` | 발동 더(다른 순간) | 줄마다 트리거(이벤트 받기 · 조건 충족 · 행동 직전 이벤트)·결과 |
| `pulse` | 주기(초) | 기본 0(주기 없음) |
| `pulseByLevel` | 레벨별 주기(초, 1레벨부터) | `2`, `1`처럼 레벨마다 한 칸. 넘치면 마지막 값. 있으면 `pulse`보다 먼저 |
| `modifiers` | 가진 동안 스탯 보정 | 줄마다 아래 표의 칸 |
| `modifierTarget` | 보정 받을 대상 | 객체 경로(`hero`, 모든 동료면 `party[]`). 비우면 "가진 쪽"(`perElement`면 그 요소) |
| `modifierWhen` | 보정 조건 | `+ 블럭`: 조건 충족만. 비우면 늘 |
| `stacking` | 여럿일 때 | `레벨을 더해 한 번`(기본) / `가진 쪽마다 값을 더함` / `가장 높은 레벨 하나만` |
| `icon` | 아이콘 | `Sprite`. 규칙은 보지 않는다. 화면 코드가 유물 줄·특성 창에 그린다 |

`modifiers` 한 줄(`PassiveModifier`)의 칸:

| 칸 | 툴팁 | 값 |
|---|---|---|
| `attribute` | 스탯(숫자 필드 이름) | 받는 쪽 클래스의 숫자 필드 이름 그대로(`attack`) |
| `kind` | 칸 | `플랫(+값)` / `퍼센트(0.2 = +20%)` / `독립 곱(0.5 = ×1.5)` |
| `value` | 값(퍼센트는 0.2 = +20%) | 숫자 |
| `perLevel` | 레벨마다(값 × 레벨) | 켜면 값 × 레벨 |
| `byLevel` | 레벨별 값(1레벨부터, 비우면 위 값) | `2`, `3`, `4`, `6`처럼 레벨마다 한 칸. 넘치면 마지막 값. 있으면 `value`·`perLevel`보다 먼저 |
| `target` | 받을 대상(비우면 패시브 효과의 보정 받을 대상) | 이 줄만 받는 쪽 경로(`hero`, `party[]`) |

- 결과를 코드로 쓰려면 그 ID에 `[BindTo]`(결과 칸 `outcomes`가 잠긴다, 13장. `rules`의 결과는 그대로 돈다). 템플릿의 `PassiveData.ToDefinition()`이 연결을 확인하므로 2-8의 `BoundOutcomes` 확인을 따로 쓰지 않는다.
- 결과 `빌려 쓰기`: `+ 결과 추가` → 그룹 **결과** → `빌려 쓰기`. 칸 `targets`(툴팁 "빌릴 조각")에 대상 Block을 넣는다. 요약 `빌려 쓰기: …`. 검증 오류: "빌려 쓰기: 빌릴 조각을 고르는 대상 Block을 넣으세요(예: 자리로 고르기 — 오른쪽)." / "빌려 쓰기: 비어 있는 Block이 있습니다(스크립트가 지워졌을 수 있음)."
- 검증 오류: "가진 목록의 플레이어 데이터를 고르세요." / "발동 더 N번째 줄에 트리거가 없습니다." / "주기(초)는 0 이상이어야 합니다(0 = 주기 없음)." / "가진 동안 스탯 보정 N번째 줄의 스탯(숫자 필드 이름)이 비어 있습니다." / "비어 있는 결과 칸이 있습니다(스크립트가 지워졌을 수 있음)." 경고: "결과가 없습니다."(발동이 있는데 결과가 없을 때) / "발동(트리거)도 주기도 없어 결과가 돌지 않습니다. 가진 동안 스탯 보정만 쓰려면 결과를 비우세요."(결과만 있을 때) / "레벨별 주기(초)가 모두 0이라 어느 레벨에서도 주기 결과가 돌지 않습니다(…)." 발동·결과·보정·주기가 모두 없는 스위치 패시브 효과는 오류가 아니다.
- 스탯 칸은 글자라 필드 이름 오타를 검증이 잡지 못한다. 오타가 있으면 규칙·화면이 읽는 스탯(`attack`)과 이름이 달라 보정이 붙지 않는다.
- 모니터: 결과가 돌면 타임라인에 "결과: …"(출처 `Passive`)가 남는다. 주기 결과는 행동 "패시브 효과 주기"(개발자)로 남는다(결과가 실패한 프레임은 실패한 행동 · "패시브 효과 주기(실패 감싸기)" · 경고, 0-1). **데이터** 탭에서 가진 목록(`run › relics`)에 ID를 넣고 빼며 시험한다. `modifiers`(가진 동안 스탯 보정)는 저장하지 않으므로 `$modifiers`에는 없다. `Game.Attribute`·`Game.Modifiers`로 확인한다.

### 0-3. 예제 — 닻 · 가시 갑옷 · 동료 특성 · 레벨이 있는 패시브 효과 · 조커 · 힘의 부적 · Blueprint · Green Joker · Loyalty Card · Fury · 같은 아티팩트 둘 · 주기 · 원본에서 읽기

구현 가이드(AI/BuildGuide)의 카드 던전 형식을 쓴다: 판 데이터 `run`(`RunState`)에 `hero`(id `hero`) · `relics`(`List<string>`)가 있고, 효과 Block `방어도`·`피해`, 행동 직전 이벤트 `피해`(`DamageIntent { sourceId, targetId, amount, cancelled }`)가 있다.

**닻 — 전투 첫 턴에 방어도 8**

1. DB › 패시브 효과 `anchor`: `ownerKey` `run`, `ownerPath` `relics`.
2. `trigger`: `이벤트 받기` → `턴 시작`, 이벤트 조건 `turn` `일치` `1`.
3. `outcomes`: `대상에게 효과` → 대상 `주인공`(구현 가이드의 프로젝트 대상 Block) → 효과 `방어도`(8).

가진 쪽이 `run`이라 대상을 비우면 주인공이 아니다(방어도 Block이 `Fighter`만 받아 `run`을 건너뛴다). 그래서 주인공을 고르는 대상 Block을 둔다. 가진 목록을 주인공 안에 두면(`run › hero.relics`) 가진 쪽이 주인공이 되어 대상을 비워도 된다.

**가시 갑옷 — 주인공이 받는 피해 −5**

1. DB › 패시브 효과 `thornArmor`: 가진 목록 `run › relics`.
2. `trigger`: `행동 직전 이벤트 받기` → 행동 직전 이벤트 `피해`, 행동 직전 이벤트 조건 `targetId` `일치` `hero`.
3. `outcomes`: `행동 직전 이벤트 고치기` → 필드 `amount`, 방법 `더하기`, 값 `-5`.

"주인공이 맞을 때만"은 가진 쪽(`run`)에 id가 없어 가진 쪽 필드가 아니라 행동 직전 이벤트 조건으로 정한다(0-1). 등록 코드는 없다. `run.relics`에 `thornArmor`를 넣으면 그때부터 줄어든다.

**동료 특성 — '중갑': 그 동료가 받는 피해 −2** — 동료가 있는 게임이라고 본다: `run › party`(`List<Unit>`, 동료마다 `id`), 동료마다 `traits`(`List<string>`).

1. DB › 패시브 효과 `heavyArmor`: `ownerKey` `run`, `ownerPath` `party[].traits`.
2. `trigger`: `행동 직전 이벤트 받기` → `피해`. `holderField` `targetId`.
3. `outcomes`: `행동 직전 이벤트 고치기` `amount` `더하기` `-2`.

`traits`에 `heavyArmor`가 든 동료마다 따로 검사하고, 가진 쪽 필드 덕에 그 동료가 맞을 때만 줄인다. 같은 특성을 가진 동료가 둘이어도 맞은 쪽 하나만 발동한다.

**레벨이 있는 패시브 효과 — 적을 처치할 때마다 체력 + 레벨** — `run › perks`(`Dictionary<string, int>`, ID → 레벨), 처치 이벤트 `EnemyKilled`.

1. DB › 패시브 효과 `bloodlust`: 가진 목록 `run › perks`, `trigger` `이벤트 받기` → `적 처치`. 최대 체력에서 멈추는 계산이 있어 결과를 코드로 쓴다(레벨을 읽기만 하는 결과는 아래 껍질처럼 값 틀로 코드 없이 된다).

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

**껍질 — 주인공이 받는 피해 − 레벨(코드 없음)** — 같은 `run › perks`.

1. DB › 패시브 효과 `shell`: 가진 목록 `run › perks`.
2. `trigger`: `행동 직전 이벤트 받기` → `피해`, 행동 직전 이벤트 조건 `targetId` `일치` `hero`.
3. `outcomes`: `행동 직전 이벤트 고치기` `amount` `더하기` `-{level}`.

`{level}`은 이 결과를 가진 패시브 효과의 레벨이다(가진 쪽 기준, `Game.Owned(c.Owner, c.User)`와 같다). 레벨 2면 피해 5가 3이 된다. 레벨마다 정한 값이면 `-{level:2/4/7}`이다. 레벨 1이면 −2, 2면 −4, 3 이상이면 −7(넘치면 마지막 값), 0이면 0이다(28장 3-4).

**조커 — 놓인 순서대로(+4 배수 · ×2 배수)** — 조커가 있는 게임이라고 본다. 조커 칸은 객체 목록이고, 메인 룰(핸드 점수 함수)이 점수를 내기 직전에 행동 직전 이벤트 `점수`를 행동 직전 이벤트에 통과시킨다.

```csharp
[Serializable] public class Joker { public string id; public string joker; public int level = 1; public int mult; }   // 조각: 고유 id + 원본(패시브 효과) ID
// RunState에: public List<Joker> jokers = new List<Joker>();

[Serializable, Intent("점수")]
public class ScoreIntent { public double chips; public double mult = 1; public bool cancelled; }

// 메인 룰: var score = Game.Intercept(new ScoreIntent { chips = 30 });  total = score.chips * score.mult;
```

1. DB › 패시브 효과 `plusMult`: 가진 목록 `run › jokers`, `matchField` `joker`, `perElement` 켬. `trigger` `행동 직전 이벤트 받기` → 행동 직전 이벤트 `점수`. `outcomes` `행동 직전 이벤트 고치기` `mult` `더하기` `4`.
2. DB › 패시브 효과 `timesMult`: 같은 칸. `outcomes` `행동 직전 이벤트 고치기` `mult` `곱하기` `2`.
3. 조커를 얻으면 `run.jokers`에 `{ id: "j1", joker: "plusMult" }`처럼 조각을 넣는다.

| 조커 칸(왼쪽부터) | `mult` |
|---|---|
| `plusMult` · `timesMult` | (1 + 4) × 2 = 10 |
| `timesMult` · `plusMult` | 1 × 2 + 4 = 6 |
| `plusMult` · `timesMult` · `plusMult` | (1 + 4) × 2 + 4 = 14 |

- 두 패시브 효과가 같은 목록을 보므로 DB 순서가 아니라 칸 순서로 섞여서 돈다(0-1 `perElement`). 행동 직전 이벤트 잎의 순서(`order`)는 같게 둔다(기본 0). 다르면 순서가 먼저다.
- 조각마다 레벨이 있으면 `levelField` `level`, 값 `{level:4/8/12}`처럼 쓴다. `{level}`은 그 조각의 레벨이다.

**힘의 부적 — 가진 동안 공격력 +2 / +3 / +4 / +6(레벨별, 코드 없음)** — 유물에 레벨이 있는 게임이라 `run › relics`를 ID → 레벨 딕셔너리(`Dictionary<string, int>`)로 둔다고 본다. 주인공 `hero`(id `hero`)에는 숫자 필드 `attack`(기본 10)이 있다.

1. DB › 패시브 효과 `strengthCharm`: 가진 목록 `run › relics`. `trigger`는 비운다.
2. `modifiers` 한 줄: `attribute` `attack`, `kind` `플랫(+값)`, `byLevel` `2`, `3`, `4`, `6`.
3. `modifierTarget` `hero`.

| `relics`의 `strengthCharm` | `Game.Attribute(hero, "attack")`(기본 10) |
|---|---|
| 없음 | 10 |
| 1 | 12 |
| 4 | 16 |
| 7 | 16(넘치면 마지막 값) |

- 가진 쪽 `run`에는 id가 없어 `modifierTarget`(보정 받을 대상)을 `hero`로 정한다(가진 목록이 `run › hero.relics`면 비워도 된다). 유물을 잃으면 그 순간부터 보정이 없다(떼는 코드 없음, 0-1).
- 툴팁: `Game.Modifiers(hero, "attack")`에 출처 `strengthCharm`인 줄이 보인다.
- 레벨마다 같은 양이면 `value` `2` + `perLevel` 켬(2 / 4 / 6 / 8 …)이다.

**Blueprint · Brainstorm — 옆 조각의 패시브 효과를 빌려 쓰기(코드 없음)** — 위 조커 형식.

1. DB › 패시브 효과 `blueprint`: 가진 목록 `run › jokers`, `matchField` `joker`, `perElement` 켬. `trigger`는 따라 할 조커들과 같은 순간(`행동 직전 이벤트 받기` → `점수`).
2. `outcomes`: `빌려 쓰기` → `targets`(빌릴 조각)에 `자리로 고르기`(`origin` `사용자(가진 쪽)`, `list` `run › jokers`, `direction` `오른쪽`).
3. DB › 패시브 효과 `brainstorm`: 같은데 `direction` `목록 맨 처음(차 있는 첫 칸)`.

| 조커 칸(왼쪽부터) | `mult` |
|---|---|
| `plusMult` · `blueprint` · `timesMult` | (1 + 4) × 2 × 2 = 20 |
| `blueprint` · `plusMult` | 1 + 4 + 4 = 9 |
| `brainstorm` · `plusMult` | 1 + 4 = 5(맨 왼쪽이 자기 자신이라 빌리지 않는다) |
| `plusMult` · `brainstorm` | 1 + 4 + 4 = 9 |

- Blueprint는 자기 칸 차례에 오른쪽 조커의 패시브 효과를 낸다. 사용자(`{level}`)는 원본 조커, 원본 자신의 쌓이는 필드는 바꾸지 않음, 서로 가리키는 고리는 끊음 — 규칙은 0-1 빌려 쓰기.
- 따라 할 조커가 다른 순간(턴 끝 이벤트 등)에 돈다면 Blueprint의 `trigger`를 `OR`로 묶어 그 순간도 받게 한다.

**Green Joker — 손을 낼 때 배수 +1 · 버릴 때 −1 · 점수 때 쌓인 배수를 더한다(코드 없음, `rules`)** — 위 조커 형식(`Joker.mult`). 메인 룰이 손을 낼 때 이벤트 `손 냄`, 버릴 때 `버림`을 쏘고, 그 뒤 점수를 행동 직전 이벤트 `점수`로 행동 직전 이벤트에 통과시킨다고 본다.

1. DB › 패시브 효과 `greenJoker`: 가진 목록 `run › jokers`, `matchField` `joker`, `perElement` 켬.
2. `trigger` `행동 직전 이벤트 받기` → `점수`. `outcomes` `행동 직전 이벤트 고치기` `mult` `더하기` `{user:mult}`.
3. `rules` 줄 1: `이벤트 받기` → `손 냄`. 결과 `대상에게 효과` → `사용자 자신` → `필드 바꾸기`(`mult` 더하기 `1`).
4. `rules` 줄 2: `이벤트 받기` → `버림`. 결과 같은 모양(`mult` 더하기 `-1`, `clampZero` 켬).

- `{user:mult}`는 사용자(이 조커)의 필드를 읽는 값 틀이다(12장 2-4a). 쌓인 값을 읽는 결과를 코드로 쓰지 않는다.
- Blueprint가 Green Joker를 빌리면 점수 때 원본의 배수를 한 번 더 더하고, 쌓기는 원본만 한다(빌린 순간이 `점수`라 줄 1·2는 거짓).

**Loyalty Card — 6번째 손마다 ×4(카운터, 코드 없음)** — `Joker`에 `counter`(`int`)가 있다고 본다.

1. DB › 패시브 효과 `loyaltyCard`: 조커 형식(가진 목록 `run › jokers`, `matchField` `joker`, `perElement` 켬).
2. `trigger` `이벤트 받기` → `손 냄`. `outcomes` `대상에게 효과` → `사용자 자신` → `필드 바꾸기`(`counter` 더하기 `1`).
3. `rules` 줄 1: `행동 직전 이벤트 받기` → `점수` AND `사용자 필드 검사`(`path` `counter`, `op` 이상, `value` `6`). 결과 ① `행동 직전 이벤트 고치기` `mult` `곱하기` `4` ② `대상에게 효과` → `사용자 자신` → `필드 바꾸기`(`counter` 값으로 바꾸기 `0`).

- 쌓기(본 발동)와 쓰기·초기화(줄 1)를 다른 순간 둘로 나눈다. `사용자 필드 검사`는 사용자(이 조커)의 필드를 비교하는 조건이다(11장 2-8).
- 웨이브·판마다 N번까지도 같은 모양이다: 발동 AND `사용자 필드 검사`(`uses` 미만 `10`) → 결과 + `uses` +1, 줄 하나 더 `웨이브 시작 받기` → `uses` 값으로 바꾸기 `0`.

**Fury — 같은 줄에 혼자면 공격력 +3(보정 조건)** — 보드 `run › board`(`List<Unit>`, 3열 격자, 빈 칸은 `null`), 유닛마다 `id` · `attack` · 키워드 목록 `traits`(`List<string>`)가 있다고 본다.

1. DB › 패시브 효과 `fury`: 가진 목록 `run › board`, `matchField` `traits`, `perElement` 켬. `trigger`는 비운다.
2. `modifiers` 한 줄: `attribute` `attack`, `kind` `플랫(+값)`, `value` `3`.
3. `modifierWhen`: `자리 검사`(`who` `사용자(가진 쪽)`, `list` `run › board`, `columns` `3`, `spot` `같은 줄에 혼자`).

- `traits`가 글자 목록이라 `["fury", "teeth"]`인 유닛은 두 패시브 효과를 다 가진다.
- 유닛을 옮기면 다음에 읽을 때 저절로 붙고 빠진다(저장하지 않고 읽을 때 검사).
- "체력이 절반 이하일 때만"처럼 계산이 든 조건은 프로젝트 조건 Block(11장 4절)으로 둔다.

**같은 아티팩트 둘 — 사본마다 값을 더한다(`stacking`)** — 인벤토리 `run › bag`(`List<Artifact>`, `Artifact { id, artifact, level }`), 주인공 `hero`에 `attack`.

1. DB › 패시브 효과 `whetstone`: 가진 목록 `run › bag`, `matchField` `artifact`, `perElement` 켬, `levelField` `level`.
2. `modifiers` 한 줄: `attack` `플랫(+값)`, `byLevel` `3`, `6`, `10`. `modifierTarget` `hero`.
3. `stacking` `가진 쪽마다 값을 더함`.

| 인벤토리의 `whetstone` | 레벨을 더해 한 번 | 가진 쪽마다 값을 더함 | 가장 높은 레벨 하나만 |
|---|---|---|---|
| 레벨 2 하나 | +6 | +6 | +6 |
| 레벨 2 · 레벨 3 | +10(레벨 5, 넘치면 마지막 값) | 6 + 10 = +16 | +10 |
| 레벨 2 · 레벨 0(꺼짐) | +6 | +6 | +6 |

- 모든 동료가 받으면 `modifierTarget` `party[]`다.

**주기 — 동전 주머니: 가진 동안 5초마다 골드 +1(레벨 2면 3초마다, 코드 없음)** — 실시간 게임이고 `run › relics`가 ID → 레벨 딕셔너리, `run`에 `gold`가 있다고 본다.

1. DB › 패시브 효과 `coinPouch`: 가진 목록 `run › relics`. `trigger`는 비운다.
2. `pulseByLevel` `5`, `3`.
3. `outcomes`: `플레이어 데이터에 주기 · run` › `gold` 증감 `1`.

- 게임 시각 5·10·15초 …(레벨 2면 3·6·9초 …)에 행동 "패시브 효과 주기"로 낸다. 얻은 때부터 세지 않는다(0-1 `pulse`).

**원본에서 읽기 — 판 데이터에는 원본 ID만(Foxee)** — 보드 조각은 원본 ID만 들고, 패시브 효과 목록은 원본 카드에 둔다.

```csharp
[Serializable] public class Card { public string name; public int price; public List<string> passives = new List<string>(); }   // DB › 인스턴스(원본)
[Serializable] public class Unit { public string id; public string card; public int hp; public List<string> traits = new List<string>(); }   // 판 데이터의 조각
```

1. DB › 인스턴스 `card.foxee`: 형식 `Card`, `passives` = `wild`, `teeth`.
2. DB › 패시브 효과 `wild`·`teeth`: 가진 목록 `run › board`, `matchField` `card`, `matchVia` `passives`, `perElement` 켬. 발동·결과는 패시브 효과마다.
3. 카드를 놓을 때 `run.board`에 `new Unit { id = Game.NewId("unit"), card = "card.foxee" }`만 넣는다.

- 조각 하나에 패시브 효과 둘이 붙는다. 원본의 `passives`를 고치면 이미 놓인 조각도 따른다(판 데이터에 복사하지 않았으므로).
- 가격·등급 같은 고정 값도 원본에 둔다(0-5).
- 참(Charm)이 조각에 더한 키워드는 다른 패시브 효과의 `matchField` `traits`(글자 목록)로 함께 쓴다.
- 서버·테스트처럼 세션을 직접 만들면 `session.Instances = id => …`(원본 ID → 원본 객체)를 넣는다(게임에서는 런타임이 DB › 인스턴스로 채운다).

### 0-4. 코드로 — `Game.Owned` · `Game.BorrowPassive` · `PassiveDefinition` · `session.Passives`

```csharp
int relics = Game.Owned(GameIds.Passive.Anchor);                       // 모든 가진 쪽의 합
int level  = Game.Owned(GameIds.Passive.Bloodlust, run);               // 그 가진 쪽만(객체 또는 id)
bool four  = Game.Owned(GameIds.Passive.FourFingers) > 0;             // 스위치 패시브 효과(발동·보정 없이 가졌는지만)
int jokerLv = Game.Owned(GameIds.Passive.PlusMult, joker);             // perElement(놓인 것마다): 그 조각 하나의 레벨
double atk = Game.Attribute(hero, "attack");                           // 가진 동안 스탯 보정까지 더한 값(28장)
```

| 멤버 | 뜻 |
|---|---|
| `int Owned(string passiveId, object holder = null)` | 몇 개 가졌나(레벨, 0-1의 표). `holder`를 비우면 모든 가진 쪽의 합(`levelField`가 있으면 그 숫자의 합). `perElement`(놓인 것마다) 패시브 효과면 `holder` = 요소 하나(객체 또는 id) → 그 요소의 레벨. 침묵한 가진 쪽·레벨 0 이하(요소의 레벨 필드·딕셔너리의 값)는 0, 없는 패시브 효과 ID도 0. 값을 바꾸지 않으므로 화면 코드에서도 읽는다 |
| `bool BorrowPassive(string passiveId, object lender)` | 빌려 쓰기를 코드로: 지금 순간으로 그 패시브 효과를 원본(`lender`)의 것으로 한 번 더 낸다. 결과 `빌려 쓰기`와 같은 규칙(원본 값을 읽음, 원본 자신의 필드는 '필드 바꾸기'가 바꾸지 않음, 고리 끊기, 꺼진 원본(침묵·레벨 0 이하)은 빌리지 못함). 결과를 냈으면 `true`. 결과·`[BindTo]` 코드 안처럼 지금 순간이 있을 때 부른다(밖에서는 `false`) |

| `PassiveDefinition` | 뜻 | DB 칸 |
|---|---|---|
| `Id` · `DisplayName` | ID · 표시 이름 | 항목 ID |
| `OwnerKey` · `OwnerPath` | 가진 목록(플레이어 데이터 · 필드) | `ownerKey` · `ownerPath` |
| `MatchField` · `MatchVia` | 일치 필드 · 원본에서 읽기 | `matchField` · `matchVia` |
| `PerElement` | 놓인 것마다 | `perElement` |
| `LevelField` | 레벨 필드 | `levelField` |
| `When` · `HolderField` | 발동 · 가진 쪽 필드 | `trigger` · `holderField` |
| `Outcomes` | 결과 | `outcomes` |
| `Rules` (`List<PassiveRule>`) | 발동 더. 줄마다 `PassiveRule { When, Outcomes }` | `rules` |
| `Pulse` · `PulseByLevel` | 주기(초) · 레벨별 주기. `PulseAt(레벨)` = 그 레벨의 주기 | `pulse` · `pulseByLevel` |
| `Modifiers` (`List<PassiveModifier>`) | 가진 동안 스탯 보정(`attribute` · `kind` · `value` · `perLevel` · `byLevel` · `target`, `At(레벨)` = 그 레벨의 값) | `modifiers` |
| `ModifierTarget` | 보정 받을 대상 경로(`party[]`면 그 모두) | `modifierTarget` |
| `ModifierWhen` | 보정 조건(Trigger) | `modifierWhen` |
| `Stacking` (`PassiveStacking`) | 여럿일 때: `SumLevels`(기본) · `SumValues` · `Highest` | `stacking` |

`session.Passives`(`PassiveSystem`): `Define(definition)`(등록, 같은 ID면 바꾼다. 발동도 이때 등록한다) · `Find(id)` · `Definitions` · `Owned(id, holder)` · `ModifiersFor(대상, 대상 id, 스탯)`(그 대상에 붙는 가진 동안 스탯 보정을 `Modifier` 모양으로, 스탯을 비우면 전부) · `Borrow(id, 원본, 순간)`(= `Game.BorrowPassive`, 순간을 직접 넘긴다) · `MatchingFor(조각)`(그 조각을 가진 것으로 보는 패시브 효과들). 게임(Unity)에서는 런타임이 DB의 패시브 효과 전부를 등록한다. 서버·테스트처럼 세션을 직접 만들면 직접 등록한다.

```csharp
foreach (var passive in GameDatabase.Load().All<PassiveData>()) session.Passives.Define(passive.ToDefinition());
```

- `PassiveData.ToDefinition()`은 그 ID에 `[BindTo]`가 있으면 `Outcomes`를 그 코드로 바꾼다(`Rules`는 그대로).
- `matchVia`(원본에서 읽기)는 `GameSession.Instances`(`Func<string, object>`)로 원본을 찾는다(0-1).
- 패시브 효과의 레벨은 값 틀 `{level}`·`{level:2/4/7}`, 조각의 필드는 `{user:경로}`(12장 2-4a)로 코드 없이 읽는다. 코드에서는 `Game.Owned(c.Owner, c.User)`·`((Joker)c.User).mult`다. 값 틀 `{stacks}`는 상태 효과의 중첩이라 패시브 효과에서는 0이다.
- 효과 배율은 `c.Power`다(기본 1). `[BindTo]` 코드가 수치를 직접 바꾸면 배율도 직접 곱한다(16장 2-12).
- 빌려 쓰는 중인지는 컨텍스트의 `c.IsBorrowed`다. `빌려 쓰기`가 막아 주는 것은 효과 `필드 바꾸기`뿐이다. `[BindTo]` 코드가 원본 조각의 쌓이는 값을 직접 바꾼다면 `if (c.IsBorrowed) …`로 직접 거른다.
- 아이콘은 DB 항목에만 있다: `GameDatabase.Load().All<PassiveData>()`에서 ID로 찾아 `icon`을 읽는다.

### 0-5. 패시브 효과로 안 되는 것 — 경계

패시브 효과는 가진 동안 순간마다 그 자리에서 반응한다. 조각의 필드에 쌓고(`필드 바꾸기`), 쌓인 값으로 거르고(`사용자 필드 검사`), 쓰고 되돌리고(`rules`의 다른 순간), N초마다 내는(`pulse`) 것까지 패시브 효과 안에서 된다. 조각마다 다른 값(쌓은 배수·카운터·횟수)은 패시브 효과 정의가 아니라 조각 객체의 필드(`Joker.mult`)에 둔다. 아래는 패시브 효과 밖이다.

| 하고 싶은 것 | 어디서 |
|---|---|
| **한 번만** 발동하고 완료를 저장한다(퀘스트·튜토리얼). **하루**에 N번(현실 날짜), **단계**를 차례로, **선행** 항목 | 이 장의 고유 기능(1절부터, 4절). 판·웨이브마다 N번은 패시브 효과로 된다(횟수 필드 + `사용자 필드 검사` + 초기화 줄, 0-3 Loyalty Card) |
| 새 조각을 만든다(덱에 새 카드, 소환 유닛) | 결과를 `[BindTo]` 코드로(`run.deck.Add(new Card { id = Game.NewId("card"), card = … })`). `플레이어 데이터에 주기`의 인스턴스 넣기는 원본의 사본이라 고유 `id`를 새로 짓지 않는다 |
| 반응이 다 끝난 뒤에 도는 일(발동 대기열, 연쇄를 모아 한 번에 정산) | 메인 룰(행동 코드)이나 프로젝트 Block. 패시브 효과의 결과는 받은 순간 그 자리에서 돈다 |
| 계산·분기가 든 결과(최대 체력에서 멈춤, 두 값을 곱함) | 결과를 `[BindTo]`로(0-3 `bloodlust`), 또는 프로젝트 효과·결과 Block |
| 주기를 멈추거나 앞당기거나 처음부터 다시 센다 | 조각의 필드 + 틱 행동·이벤트(`rules` 한 줄이 받아 필드를 바꾼다). `pulse`는 시계 기준이다 |
| 항목에 칸(가격·등급·마나 비용)을 더한다 | 프로젝트의 `PassiveData`(템플릿 사본)에 칸을 더한다 — 프로젝트 변경이라 묻지 않고 보고만 한다(AI 문서 Reference 4-1). 엔진은 `ToDefinition()`이 넘기는 칸만 보므로, 더한 칸은 프로젝트 규칙이 그 항목에서 직접 읽는다(`Game.Entry<PassiveData>(id)`, 2-1). 조각마다 다른 고정 값(카드 가격)은 DB › 인스턴스 원본에 두고 `matchVia`(원본에서 읽기)로 찾는다(0-3). 이름·설명은 텍스트 표(`<ID>.name`·`<ID>.desc`) |

---

## 1. 에디터에서 쓰는 법

코드(2장)를 쓰고 컴파일하면:

1. `MomoiProject → DB` 사이드바에 `[Database]`에서 정한 이름의 탭(예: **진행** 그룹의 **퀘스트**)이 생긴다.
2. 로직 쪽 종류라(구조 잠금, 21장 3-1) 항목 추가·삭제·ID 바꾸기와 Block 조립은 AI가 DB 반영 스크립트로 한다(`EditorDatabase.Staging.Add(typeof(QuestData), "quest.slimes")`). 개발자는 문장 안의 값을 바꾼다. 개발자가 항목을 늘리고 줄여야 하는 종류(레벨 배치)만 `[Database(…, DeveloperAuthored = true)]`로 연다. DB ID는 모든 종류에서 하나뿐이다(퀘스트 ID가 도전과제 ID와 겹칠 수 없다).
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
| `Group` | 사이드바 그룹(비우면 `데이터`. 기본 종류는 `요소`·`DB`·`개발`을 쓴다. `DB` 그룹의 로직 종류는 항목이 없으면 탭이 숨는다) |
| `Order` | 그룹 안 순서 |
| `IdPrefix` | 새 항목 ID 앞부분(없으면 클래스 이름에서) |
| `DeveloperAuthored` · `Single` · `HideWhenEmpty` · `Hidden` · `Page` | 21장 1절 |

`GameData`가 ID(`Id`)와 검증 훅(`ValidateData`)을 준다. 표시 이름 필드는 따로 두지 않는다(ID가 이름이다).

**규칙에서 항목의 칸을 읽기**: 행동·규칙 함수·Block 안에서 `Game.Entry<QuestData>(id)`(없거나 형식이 다르면 null), 그 종류 모두는 `Game.Entries<QuestData>()`(하나만 두는 설정 종류는 `.FirstOrDefault()`). 런타임이 게임을 켤 때 DB 항목으로 채운다(Rules 어셈블리라 Unity 시작 방식을 몰라도 된다). 항목은 원본이다 — 읽기만 하고, 게임 중에 바뀌는 값은 플레이어 데이터에 둔다. 규칙 테스트는 세션에 직접 넣는다: `session.Entries = id => …`·`session.EntriesOfType = type => …`(4-3).

### 2-2. Trigger 칸: `[SerializeReference, TriggerSlot(…)]`

```csharp
[FieldLabel("트리거"), SerializeReference, TriggerSlot(TriggerKinds.Event | TriggerKinds.Condition)]
public Trigger trigger;
```

- `[SerializeReference]` — 필수. `Trigger`는 추상 형식이라 실제 형식째 저장해야 한다.
- `[TriggerSlot(TriggerKinds)]` — 고를 수 있는 잎. 등록해서 감시하는 기능은 `Event`·`Condition`을 쓴다(`Use`는 `Game.Use`로 쓰는 액티브 효과에만 의미가 있다).
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
| **연결** — Unity가 켜질 때 그 함수를 부르는 한 줄 | Client의 `Bootstrap.RegisterRules` | Unity 시작 방식(`GBibleRuntime.SessionCreated` — 모든 세션)은 클라이언트에만 있다 |

```csharp
// Rules 쪽: 등록 함수(Unity 시작 방식을 모른다)
public static class QuestRules
{
    public static void Register(GameSession session, IEnumerable<QuestData> quests) { /* 2-7 */ }
}

// Client의 Bootstrap.cs: 연결 한 줄
static void RegisterRules(GameSession session)
{
    QuestRules.Register(session, GameDatabase.Load()?.All<QuestData>());
}
```

`Bootstrap.RegisterRules`는 `GBibleRuntime.SessionCreated`에 연결된 함수다(`20-Setup.md` 4절). **모든 세션**(화면 세션 · 멀티 서버 · 시험 모드 · 매칭 방)이 DB로 만들어진 직후(플레이어 데이터·로그·액티브 효과·도전과제 정의가 끝난 뒤, 새 판 행동보다 먼저) 불린다. `Started`(화면 세션에만 온다)에 두면 멀티 서버 세션에서 감시가 빠진다. 기능마다 자기 `[RuntimeInitializeOnLoadMethod]`와 `SessionCreated +=`를 따로 두지 않는다 — 연결이 여러 파일에 흩어지고, 테스트에서 같은 등록을 부를 길이 없다. 등록 함수의 전체 모양은 3-1.

### 2-7. 감시 등록: `session.Triggers.Register(owner, when, repeat, fire)`

```csharp
session.Triggers.Register(quest.Id, quest.trigger, repeat: false, fire: moment => { /* 발동 */ });
```

| 인자 | 뜻 |
|---|---|
| `owner` | 항목 ID. 컨텍스트의 `Owner`가 되고, `Unregister(owner)`로 그만둘 때 쓴다 |
| `when` | Trigger(`null`이면 아무것도 하지 않는다) |
| `repeat` | `false`: 한 번 발동하면 감시를 그만둔다. 행동 안에서 발동했으면 그 행동이 성공할 때 그만두고, 행동이 실패하거나 미리보기(`Preview`·`CanDo`)였으면 그대로 감시한다 / `true`: 계속(조건만으로 된 Trigger는 거짓이 된 뒤 다시 참이 될 때 또) |
| `fire` | 발동했을 때 불리는 함수. 인자는 그 순간의 `TriggerContext` |

- 이벤트 받기 잎은 이벤트를 받을 때, 조건 충족 잎은 그 조건이 읽는 값이 바뀔 때(행동 끝·프레임 끝의 Flush) 검사된다.
- 선택 인자 — 여러 대상이 같은 Trigger를 가질 때(상태 효과·패시브 효과가 쓰는 방식):

| 인자 | 뜻 |
|---|---|
| `holders` | 순간마다 가진 쪽 목록을 돌려주는 함수. 가진 쪽마다 컨텍스트의 `User`로 넣어 Trigger 전체(조건 포함)를 따로 검사하고, 참인 가진 쪽마다 `fire`를 부른다 |
| `orderedHolders` · `orderGroup` | 가진 쪽과 그 자리(`(holder, order)`)를 돌려주는 함수와 묶음 이름. 같은 묶음 이름의 항목끼리는 등록 순서가 아니라 자리가 작은 것부터 섞여서 발동한다(패시브 효과 `perElement`(놓인 것마다), 0-1). 행동 직전 이벤트는 같은 순서(`order`) 칸 안에서 섞는다 |

- 등록할 때 이미 참인 조건은 값이 다시 바뀌기 전까지 발동하지 않는다. 시작할 때 소급하려면 등록을 마친 뒤 `session.Triggers.Watcher.EvaluateAll()`을 부른다.
- 그 밖에: `Unregister(owner)`, `IsRegistered(owner)`, `Touch(owner)`(그 조건 감시를 다음 Flush에서 다시 평가 — 불러온 뒤 새로 감시를 시작했을 때).

### 2-8. 결과 실행: `RunOutcomes` + `[BindTo]` 확인

```csharp
IEnumerable<Outcome> outcomes = BoundOutcomes.Has(quest.Id) ? BoundOutcomes.Create(quest.Id) : quest.outcomes;
// fire 안에서:
session.Triggers.RunOutcomes(outcomes, moment, "Quests");
```

- `RunOutcomes(결과들, 컨텍스트, 출처)`는 결과를 차례로 실행하고, 결과마다 Journal에 "결과: …"(출처 `Quests`)를 남긴다. 결과가 꺼낸 객체를 고쳐도 기록된다.
- `[BindTo]` 연결은 기본 기능(도전과제·로그·개발 명령)과 템플릿에서 들인 선택 요소(액티브 효과·상태 효과·패시브 효과의 `ToDefinition`·`ToUsable`)만 스스로 확인한다. 프로젝트 기능은 `BoundOutcomes.Has(id)`로 직접 확인해 바꿔 써야 한다. 확인하지 않으면 에디터에는 결과 칸이 잠겨 보이는데 실제로는 숨은 예전 결과가 돈다.

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
- **행동이 실패하거나 미리보기일 때**: `Permanent()` 안의 변경도 함께 취소된다(02장 2.9 — 일어나지 않은 일). 반복이 아닌 감시도 그때 되살아나므로(2-7) 둘이 맞는다. 다만 `fire` 안에서 플레이어 데이터 밖(화면·소리·외부 전송)으로 나가는 일은 취소되지 않으니, 미리보기면(`session.Actions.IsPreviewing`) 건너뛰고, 그런 일은 바깥 행동이 성공한 뒤(`session.Actions.Executed`, `Nested`가 거짓 — 도전과제가 이렇게 한다, 14장 2-3)나 `SubscribeAfterAction`으로 한다.

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
            // 미리보기에서는 완료하지 않는다(기록 줄을 남기지 않게) · 이미 완료했으면 건너뛴다(2-9)
            if (session.Actions.IsPreviewing || session.Data.Get<QuestLog>(LogKey).done.Contains(id)) return;
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

// ── 연결(Client/Bootstrap.cs의 RegisterRules에 한 줄 — 2-6) ─────────
// QuestRules.Register(session, GameDatabase.Load()?.All<QuestData>());
```

### 3-2. 에디터에서 퀘스트 조립

DB에 보이는 모양이다. 항목과 Block은 AI가 DB 반영 스크립트로 조립하고(1절), 개발자는 문장 안의 값(5 · 100 · 보상 인스턴스)을 바꾼다.

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
- 날마다 임무 N개를 뽑아 진행을 세고 다 하면 받는 **일일 임무**는 새 DB 종류 없이 인스턴스 + 가중치 표 + 프로필로 된다(07장 4.11). 이 절은 "발동마다 결과, 하루 N번까지"인 미션이다.
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
        // 미리보기거나 이미 이 단계를 넘겼으면 건너뛴다(3-1과 같은 까닭, 2-9)
        if (session.Actions.IsPreviewing || session.Data.Get<ChainLog>("chain.log").step.TryGetValue(quest.Id, out var at) && at > index) return;
        using (session.Data.Permanent())
        {
            session.Data.Modify<ChainLog>("chain.log", l => l.step[quest.Id] = index + 1);
            session.Triggers.RunOutcomes(rule.then, moment, "ChainQuests");
        }
        WatchStep(session, quest);                       // 다음 단계
    });
}
```

`steps`의 단계(규칙 줄)를 더하고 단계마다 트리거·결과를 조립하는 것은 구조라 AI가 DB 반영 스크립트로 한다(21장 3-1). 에디터에서는 단계마다 문장 안의 값을 바꾼다.

### 4-3. Unity 없이(서버·테스트)

DB 없이도 같은 API로 돈다. 등록 함수를 Rules에 둔 기능은 서버가 세션을 만든 뒤 같은 함수(`QuestRules.Register(session, quests)`)를 부르면 된다(Unity 안의 멀티 서버·방 세션은 `Bootstrap.RegisterRules`가 이미 부른다 — 2-6, 또 부르지 않는다). 아래는 Trigger·결과를 코드로 만들어 직접 등록하는 예다.

```csharp
var session = new GameSession();
session.Data.Define("quest.log", DataZone.Meta, new QuestLog());
session.Data.Define("party", DataZone.Meta, new Party());
var when = new EventTrigger { eventType = typeof(QuestCompleted).FullName };
var rewards = new List<Outcome> { new PlayerDataReward { key = "party", path = "gold", amount = 50 } };
session.Triggers.Register("bonus", when, repeat: true, fire: m => session.Triggers.RunOutcomes(rewards, m, "Bonus"));
```

값을 직접 바꾼 뒤 조건 잎을 검사하게 하려면 `session.Triggers.Flush()`를 부른다(행동으로 바꾸면 자동).

규칙이 `Game.Entry`·`Game.Entries`로 DB 항목을 읽으면 테스트가 항목을 넣어 준다. 항목은 `ScriptableObject.CreateInstance<QuestData>()`로 만들고 **`EditorAssignId("ID")`로 ID를 붙인 뒤** 칸을 채운다 — ID는 에디터 도구만 붙이는데(`#if UNITY_EDITOR`), 프로젝트 규칙 테스트(EditMode, 어셈블리 `MomoiProject.Tests`는 `GBible.Unity`를 참조한다)는 에디터에서 도므로 이것을 쓸 수 있다. 그러면 3-1처럼 `quest.Id`를 읽는 코드가 테스트에서도 그대로 돈다.

```csharp
var first = ScriptableObject.CreateInstance<QuestData>();
first.EditorAssignId("quest.first");
first.trigger = when;
var quests = new List<QuestData> { first };
session.Entries = id => quests.FirstOrDefault(q => q.Id == id);
session.EntriesOfType = type => quests.Where(type.IsInstanceOfType);
QuestRules.Register(session, quests);
// 테스트 끝(finally)에서 Object.DestroyImmediate(first)
```

인스턴스 원본(DB › 인스턴스)은 이렇게 넣지 않고 `session.DefineInstance(id, 원본)`(04장 2.7).

---

## 5. 주의할 점 / 자주 하는 실수

- **"가지고 있으면 발동"을 새 DB 종류로 만든다.** 유물·특성·장비 효과는 기본 종류 패시브 효과(0절)로 등록 코드 없이 된다. 이 장의 방식은 패시브 효과에 없는 규칙(한 번만·한도·단계·선행)이 있을 때만.
- **패시브 효과의 가진 쪽 필드를 `run › relics`에 쓴다.** 가진 쪽(`run`)에 id가 없어 발동하지 않는다. 행동 직전 이벤트 조건(`targetId 일치 hero`)으로 거르거나, 가진 목록을 id가 있는 객체 안에 둔다(0-1).
- **놓인 순서가 결과를 바꾸는 조각을 보통 패시브 효과로 둔다.** `perElement`(놓인 것마다)를 끄면 패시브 효과 등록(DB) 순서로 돌아 칸 순서가 무시된다. 조커·참·보드 유닛은 `perElement`를 켠다(0-1).
- **조각 목록을 ID 목록으로 둔다.** ID 글자에는 `id`도 필드도 없어 조각마다 레벨·쌓은 값·보정을 따로 둘 수 없고, 같은 조커 둘을 가려 가리킬 수 없다. 객체 목록(고유 `id` + 원본 ID 필드)으로 두고 `matchField`(일치 필드)를 고른다.
- **가진 동안 스탯을 얻을 때 붙이고 잃을 때 떼는 코드로 만든다.** 잃는 길(팔기·파괴·Undo)을 하나라도 놓치면 보정이 남는다. 패시브 효과의 `modifiers`(가진 동안 스탯 보정)는 읽을 때 계산하므로 뗄 일이 없다(0-1).
- **`modifiers`(가진 동안 스탯 보정)를 `Game.RemoveModifiers`로 떼려 한다.** 저장된 보정이 아니라 떼지지 않는다. 가진 목록에서 빼면 빠진다.
- **가진 목록이 `run › relics`인데 `modifierTarget`(보정 받을 대상)을 비운다.** 받는 쪽이 `run`이 되어 주인공에게 붙지 않는다. `modifierTarget`에 `hero`를 적는다.
- **같은 아티팩트 둘이 사본마다 값을 더할 거라 기대한다.** `stacking`(여럿일 때)의 기본은 레벨을 더해 한 번이다. 사본마다 값이면 `가진 쪽마다 값을 더함`, [고유]면 `가장 높은 레벨 하나만`(0-1).
- **레벨 0인 조각이 1레벨처럼 돌 거라 기대한다.** 레벨 0 이하는 꺼진 것이다(Blueprint가 빌려도 돌지 않는다). ★0부터인 레벨은 레벨 필드 = ★ + 1로 둔다(0-1).
- **주기 패시브 효과가 얻은 때부터 N초 뒤에 돌 거라 기대한다.** 경계는 게임 시각 0부터 N초 단위다. 다시 세야 하면 조각의 필드로 센다(0-1).
- **Blueprint의 발동을 원본과 다른 순간으로 둔다.** 빌려 쓰기는 지금 순간으로 원본의 발동을 검사하므로, 순간이 다르면 아무것도 내지 않는다. 따라 할 순간을 Blueprint의 발동에 넣는다(0-3).
- **일일 미션의 횟수를 세션 구역에 둔다.** 새 판이면 비워져 하루가 아니라 판마다 한도가 된다. 메타 구역 + `Game.Day`로 날짜가 바뀔 때 비운다(4-1).
- **`[BindTo]`를 확인하지 않는다.** 에디터는 연결된 항목의 결과 칸을 잠그지만, 런타임에서 그 코드를 쓰는 것은 기능 코드의 몫이다. `BoundOutcomes.Has(id) ? BoundOutcomes.Create(id) : 결과` 한 줄을 넣는다.
- **완료를 저장하지 않는다.** `repeat: false` 감시는 세션 안에서만 한 번이다. 저장하지 않으면 다음 실행에서 다시 발동한다.
- **시작할 때 이미 참인 조건.** 등록만으로는 발동하지 않는다. 등록 뒤 `session.Triggers.Watcher.EvaluateAll()`.
- **`Use` 잎을 허락한다.** 등록한 감시는 `사용`으로 발동하지 않는다(`사용`은 `Game.Use`의 사용 조건 검사에서만 참이다). 감시하는 기능의 잠금은 `Event`·`Condition`으로 둔다.
- **`fire` 안에서 결과를 직접 부른다.** `outcome.Run(…)`을 직접 부르지 말고 `RunOutcomes`를 쓴다. 그래야 컨텍스트(`Current`)가 결과·조건 Block에 보이고 Journal에 남는다.
- **`fire` 안에서 행동을 실행할 때**: 이벤트는 대개 행동 안에서 쏘이므로 `fire`도 행동 안에서 불린다. 여기서 부른 `Game.Do`·`Game.Use`는 그 행동의 일부로 실행된다. 조건 충족으로 발동하면(행동이 끝난 뒤) 새 행동이 된다.
- **로그에 기록을 허락하고 로그 칸을 비운다.** 프로젝트 기능에서 로그에 기록의 로그 칸을 비우면 그 항목 ID는 로그가 아니라 아무것도 기록하지 않고 경고만 남는다. `OutcomeKinds.Record`를 허락한다면 로그를 꼭 고르게 하거나 `ValidateData`로 검사한다.
- **인스턴스 기능 필드로 트리거·결과를 둘 때**: 인스턴스 값 안의 트리거·결과도 DB와 같은 편집기로 그려진다. 다만 원본 하나에 하나씩 정하는 값이라, 여러 항목이 공유하거나 개발자가 따로 관리할 기능은 DB 종류(`GameData`)로 만든다.
