# 12. 결과(Outcome)

**한 줄 요약**: Outcome은 Trigger가 발동했을 때 **무엇을 하는지**다. 결과 칸은 Outcome의 리스트이고, 위에서부터 차례로 실행된다. 종류는 다섯이다: **로그에 기록** · **플레이어 데이터에 주기** · **대상에게 효과** · **이벤트 쏘기** · **코딩한 결과**. 여기에 행동 직전 이벤트 전용 **행동 직전 이벤트 고치기**와 플레이어 데이터를 내는 **비용 내기**(→ `28-Attributes-Intercepts.md`), 플레이어에게 고르게 하는 **선택 요구**(→ `29-Statuses-Schedules-Choices.md`), 가중치 표에서 뽑아 넣는 **가중치 표에서 주기**·뽑은 것을 보이고 고르게 하는 **가중치 표에서 고르기**(→ `30-Balance-Tables.md`), 옆 조각의 패시브 효과를 한 번 더 내는 **빌려 쓰기**(2-4e)가 있다. 대상에게 효과의 기본 효과 Block에는 대상의 숫자 필드를 직접 바꾸는 **필드 바꾸기**(2-4a)가 있다.

**언제 쓰는가**

- 숫자·글자·리스트처럼 플레이어 데이터의 필드 하나를 정해진 값만큼 바꿀 때 → 플레이어 데이터에 주기(코드 없이 에디터에서).
- 유닛 같은 개체를 골라 그 개체를 바꿀 때 → 대상에게 효과(대상 Block + 효과 Block. 기본 제공 Block으로 되면 코드 없이, 아니면 프로그래머가 Block을 코드로 만들고 기획자가 조립·수치를 정한다).
- 고른 개체의 숫자 필드 하나를 바꿀 때("이 조커 배수 +1", "카운터 −1", "처치 횟수 +1") → 대상에게 효과 + 효과 `필드 바꾸기`(코드 없이, 2-4a).
- 옆 칸 조각의 패시브 효과를 한 번 더 낼 때(복사·흉내, "오른쪽 조커의 액티브 효과를 복사") → 빌려 쓰기(2-4e).
- 그 밖의 고유 로직 → 코딩한 결과(`Reward` 상속), 또는 항목 하나에 함수를 연결하는 `[BindTo]`(→ `13-BindTo.md`).
- 로그 값을 올릴 때 → 로그에 기록.
- 이벤트를 정해진 값으로 쏴서 그 이벤트를 받는 로그·도전과제·트리거를 반응시킬 때 → 이벤트 쏘기(주로 DB › 개발 › 개발 명령에서 "한 턴 30 피해" 같은 시험 버튼으로).
- 일어나기 직전의 일(피해·죽음·비용 …)을 바꾸거나 막을 때 → 행동 직전 이벤트 고치기(트리거 `행동 직전 이벤트 받기`와 함께, → `28-Attributes-Intercepts.md` 3-4).
- 에너지·골드를 내고, 모자라면 행동 전체를 실패시킬 때 → 비용 내기(→ `28-Attributes-Intercepts.md` 3-5).
- 후보 중에서 플레이어가 고르게 할 때("카드 1장을 골라 버린다", "적 하나를 골라 독") → 선택 요구(→ `29-Statuses-Schedules-Choices.md` 3-3).
- 드롭·보상처럼 표에서 확률로 뽑아 목록에 넣을 때 → 가중치 표에서 주기(→ `30-Balance-Tables.md` 3-3).

> 고르는 일과 바꾸는 일은 나뉜다. 개체를 고르는 것은 대상 Block이다. 고른 개체의 **숫자 필드 하나**를 더하기·곱하기·값으로 바꾸는 것은 기본 효과 `필드 바꾸기`(2-4a)로 코드 없이 된다. 글자·목록·여러 필드를 함께 바꾸거나 계산·분기가 있는 로직은 프로그래머가 효과 Block·코딩한 결과·`[BindTo]` 함수로 쓴다.

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
| 선택 요구 | 선택 요구 | `ChoiceOutcome` | `Targeted` |
| 이벤트 쏘기 | 이벤트 쏘기 | `PublishEventOutcome` | `Event` |
| 코딩한 결과 | `[BlockInfo]` 이름 | `Reward` 상속 클래스 | `Coded` |
| 행동 직전 이벤트 고치기 | 행동 직전 이벤트 고치기 | `ModifyIntentOutcome` | `Intent` |
| 비용 내기 | 비용 내기 | `PayOutcome` | `PlayerData` |
| 가중치 표에서 주기 | 가중치 표에서 주기 | `RollTableOutcome` | `PlayerData` |
| 가중치 표에서 고르기 | 가중치 표에서 고르기 | `ChooseFromTableOutcome` | `PlayerData` |
| 빌려 쓰기 | 빌려 쓰기 | `BorrowOutcome` | `Targeted` |

종류마다 값을 적는 방식에 덧붙는 규칙:

- **플레이어 데이터에 주기**의 필드는 딕셔너리의 키 하나(`stock[potion]`)나 목록에서 id로 고른 요소의 필드(`units[hero].hp`)도 된다. 그 키 하나에 숫자 증감·값 정하기를 한다(2-2, 필드 경로 11장 3-1).
- **행동 직전 이벤트 고치기**·**이벤트 쏘기**·효과 `필드 바꾸기`의 값에는 **값 틀**(`{user:mult}`·`{event:amount}`·`{data:run.gold}`·`{level}` …)을 쓸 수 있다. 발동한 순간의 사용자·이벤트·플레이어 데이터·레벨로 바뀐다(틀 목록 2-4a). **이벤트 쏘기**의 필드 경로는 마지막 마디가 딕셔너리의 키(`byElement[fire]`)여도 쓰고, 괄호 안의 점(`flags[forest.chest3]`)은 나누지 않는다. 쓰지 못하면(부모가 비었거나 구조체) 그 필드는 비운 채 쏘고 기록에 경고한다.
- **가중치 표에서 주기·고르기**가 뽑은 원본 ID는 넣을 곳의 형식대로 들어간다: 목록이면 요소로, 딕셔너리면 그 키의 개수 +1, 숫자 하나면 개수만큼 +, 값 하나(글자·enum·클래스)면 그 ID로(2-4c-2). 고르기는 후보 수에 플레이어 데이터의 숫자를 더할 수 있고, 기다리는 동안 다시 뽑을 수 있다.

| 기능 | 고를 수 있는 결과 |
|---|---|
| 로그 | 로그에 기록 하나로 고정(세부 칸만) |
| 도전과제 | 전부(`OutcomeKinds.All`) |
| 개발 명령(DB › 개발) | 전부(`OutcomeKinds.All`) |
| 액티브 효과 · 사용 기능 | 대상에게 효과 · 플레이어 데이터에 주기 · 코딩한 결과 |
| 상태 효과(DB › 상태 효과) | 결과: 전부. 걸릴 때 결과·끝날 때 결과: 대상에게 효과 · 플레이어 데이터에 주기 · 코딩한 결과 · 이벤트 쏘기(→ `29-Statuses-Schedules-Choices.md` 3-1) |
| 패시브 효과(DB › 패시브 효과) | 전부(`OutcomeKinds.All`, 행동 직전 이벤트 고치기 포함, `rules`(발동 더)의 줄도 같다, → `17-Custom-Features.md` 0절) |
| 프로젝트 기능 | 필드의 `[OutcomeSlot]`이 정한다(`All`이면 이벤트 쏘기도). 행동 직전 이벤트 고치기는 `Intent`를 넣은 칸에서, 행동 직전 이벤트 받기 트리거와 함께 쓴다 |

비용 내기·가중치 표에서 주기·고르기는 플레이어 데이터 종류라서 플레이어 데이터에 주기를 고를 수 있는 칸(액티브 효과·사용 기능 …)에서 함께 쓸 수 있다. 선택 요구·빌려 쓰기는 대상에게 효과와 같은 종류(`Targeted`)라서 대상에게 효과를 고를 수 있는 칸이면 함께 나온다. 빌려 쓰기는 주로 패시브 효과의 결과에 둔다(2-4e).

---

## 2. 에디터에서 쓰는 법

결과 칸(`outcomes`, `then` 등. 칸 이름표는 코드의 변수 이름, 마우스를 올리면 "결과")은 결과 목록 편집기로 그려진다.

### 2-1. 결과 더하기

1. **`+ 결과 추가`**를 누른다. 검색 가능한 목록이 뜬다(칸의 잠금에 있는 것만).
   - 그룹 **결과**: `로그에 기록`, `대상에게 효과`, `선택 요구`, `빌려 쓰기`, `행동 직전 이벤트 고치기`, `이벤트 쏘기`, `비용 내기`, `가중치 표에서 주기`, `가중치 표에서 고르기`
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
| 리스트 · 배열 · 그 밖의 컬렉션 | 요소 칸들 + `+` | 요소를 끝에 넣는다(요소마다 숫자·값·인스턴스 규칙). 배열은 늘린 새 배열로 바꾼다. 그 밖의 컬렉션은 나열 차례의 끝이다(`Queue`는 뒤, `Stack`은 맨 아래, `HashSet`은 없던 것만) |
| 딕셔너리 | `[키] → [값]` 줄들 + `+` | 숫자 값은 있던 값에 더한다(없던 키는 0에서). 그 밖의 값·인스턴스는 덮어쓴다 |

- 고를 수 있는 필드: 값 자체(줄 수 있는 형식이면) + 클래스 안의 필드(세 단계까지). 리스트 **안쪽**의 필드(`gear[].power`)는 고를 수 없다.
- 리스트의 숫자 요소는 증감이 아니라 그 숫자를 **넣는다**.
- 줄 수 없는 형식(에셋 참조, 요소가 또 컬렉션인 컬렉션, 구조체 값)은 목록에 나오지 않는다.
- **키 경로**: 필드 경로에 `name[키]`를 쓰면 딕셔너리의 그 키 하나에 준다. `stock[potion]` = `stock`의 `potion` 키에 숫자 증감(없던 키는 0에서, 쓰면 키가 생긴다) 또는 값 정하기. `units[hero].hp`처럼 목록에서 id가 `hero`인 요소의 필드에도 준다. 키 하나에는 목록·딕셔너리를 넣지 않는다(경고). DB의 `+ 결과 추가` 목록은 키 경로를 만들지 않는다. 에디터에서는 딕셔너리 줄의 `[키] → [값]`으로 같은 일을 하고, 키 경로는 코드(3-1)에서 쓴다.

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

기본 제공 대상 Block(분류 "대상")은 `사용자 자신`(`SelfTarget`)과 장르 공통의 일곱 가지다: `플레이어 데이터 목록`(그 목록의 요소 모두) · `이벤트의 개체`(받은 이벤트의 `sourceId` 같은 필드가 가리키는 개체 — 반사) · `조건으로 거르기`(필드 검사) · `상태 효과로 거르기`(상태 효과 중첩 비교 — 독에 걸린 적 모두) · `무작위 n개` · `정렬해서 n개`(숫자 필드, 체력이 가장 낮은 적) · `자리로 고르기`(목록에서 기준이 놓인 칸의 옆 칸·같은 줄·맨 처음·맞은편 …, `PlaceTarget`). 이어 붙이면 차례로 돈다(→ `16-Abilities-Usable.md` 2-6·3-11). 기본 제공 효과 Block은 `스탯 보정`·`스탯 보정 떼기`(분류 "스탯", → `28-Attributes-Intercepts.md` 3-1·3-2), `상태 효과 걸기`·`상태 효과 풀기`·`상태 효과 줄이기`(분류 "상태 효과", → `29-Statuses-Schedules-Choices.md` 3-2), `필드 바꾸기`(분류 "필드", 2-4a)다. 나머지 대상·효과는 프로젝트가 만든다(3-5, 3-6).

**효과 배율** — 액티브 효과를 쓸 때와 패시브 효과가 발동할 때 행동 직전 이벤트 `효과 배율`(`PowerIntent`)을 한 번 거친 배율이 컨텍스트의 `Power`에 든다(기본 1, → `16-Abilities-Usable.md` 2-12, `28-Attributes-Intercepts.md` 4-14). 기본 효과 Block 셋이 곱한다.

| 효과 | 곱하는 것 |
|---|---|
| `스탯 보정` | 값 × 배율 |
| `상태 효과 걸기` | 중첩 × 배율(반올림, 최소 1). 건 개체(`by`)는 사용자다 |
| `필드 바꾸기` | `더하기`의 값 × 배율(`곱하기`·`값으로 바꾸기`는 그대로) |

"이 유닛의 수치 2배"(Lumin)는 그 유닛이 쓸 때만 행동 직전 이벤트 `효과 배율`의 `multiplier`를 곱하기 2로 고친다(28장 5-12). `플레이어 데이터에 주기`·`행동 직전 이벤트 고치기`·`비용 내기`와 도전과제·상태 효과의 결과는 배율과 상관없다(배율 1). 프로젝트 Block은 `Use.Context.Power`(효과·대상 Block)·`context.Power`(결과)를 읽어 직접 곱한다(3-3).

### 2-4a. 효과 `필드 바꾸기`

`+ 효과 추가` → 분류 **필드** → `필드 바꾸기`. 문장은 `{field} {change} {value}`. 최종 대상마다 그 대상의 숫자 필드를 직접 바꾼다. 저장되는 값 자체를 바꾸므로 보정(Modifier, 원래 값 위에 얹어 계산하는 값)이 아니다. 잠깐 올렸다가 떼는 스탯은 `스탯 보정`(28장 3-1)으로 한다.

| 칸 | 툴팁 | 값 |
|---|---|---|
| `field` | 필드(대상의 숫자 필드·경로) | 대상 클래스의 숫자 필드 이름(`mult`). 점으로 안쪽 필드도 된다(`stats.kills`) |
| `change` | 방법 | `더하기`(기본) / `곱하기` / `값으로 바꾸기` |
| `value` | 값(틀 {level} {level:2/4/7} {stacks} {user:경로} {event:경로} {data:키.경로} {count:키.경로} {balance:ID}) | 숫자(기본 `1`). 음수면 뺀다. 틀은 아래 |
| `clampZero` | 0 아래로 내리지 않음 | 켜면 바꾼 값이 0보다 작을 때 0으로 둔다 |

**값 틀** — `필드 바꾸기`·`행동 직전 이벤트 고치기`(2-4c)·`이벤트 쏘기`(2-4b)의 값과 조건 `사용자 필드 검사`(11장 2-8)의 값에 쓴다. 발동한 순간의 값으로 바뀐 뒤 쓰인다(28장 3-4).

| 틀 | 바뀌는 값 |
|---|---|
| `{user}` | 사용자의 id(액티브 효과를 쓴 쪽, 상태 효과·패시브 효과면 가진 쪽, '놓인 것마다'면 그 조각). id가 없으면 빈 글 |
| `{user:경로}` | 사용자의 필드(`{user:mult}` = 이 조커가 쌓은 배수, `{user:stats.kills}`). 저장된 값이다(스탯 보정은 더하지 않는다) |
| `{event:경로}` | 받은 이벤트(행동 직전 이벤트 포함)의 필드(`{event:amount}`). 이벤트가 없으면 빈 글 |
| `{data:키.경로}` | 플레이어 데이터의 값(`{data:run.gold}`) |
| `{count:키.경로}` | 플레이어 데이터의 목록 개수(`{count:run.jokers}`, 빈 칸 null은 세지 않음). 없으면 0 |
| `{owner}` | 이 결과를 가진 항목 ID |
| `{level}` | 이 결과를 가진 패시브 효과의 레벨(가진 쪽 기준 `Game.Owned(Owner, User)`). '놓인 것마다' 패시브 효과면 그 조각의 레벨. 패시브 효과가 아닌 곳에서는 0 |
| `{level:2/4/7}` | 레벨 번째 값(1레벨 = 첫 값, 넘치면 마지막 값, 레벨 0이면 0) |
| `{stacks}` | 상태 효과의 결과에서 가진 쪽에 걸린 이 상태 효과의 중첩. 그 밖에서는 0 |
| `{balance:ID}` | 밸런싱 값(30장) |

틀 앞에 `-`를 붙이면 뺀다(`-{stacks}`). 틀의 값이 음수면 부호가 바뀌어 더한다(`-{x}`에서 x가 −2면 `2`). 합치는 것은 틀 앞의 `-`와 그 틀 값의 부호뿐이다 — 틀이 펼친 글자 안이나 틀 밖의 `--`는 그대로다.

패시브 효과·상태 효과의 트리거 안 조건(사용자 필드 검사 · 확률)에서도 `{owner}`·`{level}`·`{stacks}`는 그 패시브 효과·상태 효과 기준이다(검사하는 순간의 '가진 항목 ID'는 그 항목 ID — 확률 행동 직전 이벤트의 `sourceId`도).

| 기획 문장 | 대상 | `field`(필드) · `change`(방법) · `value`(값) |
|---|---|---|
| 쌓이는 조커 "점수를 낼 때마다 이 조커의 배수 +1" | `사용자 자신`('놓인 것마다' 패시브 효과의 조각, 17장 0절) | `mult` · 더하기 · `1` |
| 카운터 "턴이 끝날 때마다 −1(0에서 멈춤)" | `사용자 자신` | `counter` · 더하기 · `-1`, `clampZero`(0 아래로 내리지 않음) 켬 |
| 진화 "처치할 때마다 처치 횟수 +1" | `이벤트의 개체`(`killerId`) | `kills` · 더하기 · `1` |
| 레벨에 따라 "배수 +2/+4/+7" | `사용자 자신` | `mult` · 더하기 · `{level:2/4/7}` |
| "받은 피해만큼 쌓는다" | `사용자 자신` | `stored` · 더하기 · `{event:amount}` |
| "가진 조커 수만큼 배수 +" | `사용자 자신` | `mult` · 더하기 · `{count:run.jokers}` |
| "배수를 2배로" · "카운터를 3으로 되돌림" | `사용자 자신` | `mult` · 곱하기 · `2` / `counter` · 값으로 바꾸기 · `3` |

- 정수 필드는 계산 뒤 반올림한다. 계산 결과가 필드 형식에 담기지 않으면(`int` 칸에 30억) 자르지 않고 그 행동이 실패한다("…은(는) Int32 칸에 담기지 않습니다 — 더 큰 형식(long·double·BigNumber)의 필드를 쓰거나 계산을 고치세요.").
- **효과 배율**: `더하기`의 값에 컨텍스트의 `Power`(2-4)를 곱한다. 수치 2배가 걸린 조각이 쓰면 `mult +1`이 +2다. `곱하기`·`값으로 바꾸기`는 곱하지 않는다.
- 그 필드가 없거나, 숫자가 아니거나(글자·참거짓), 부모가 구조체인 대상은 건너뛰고 Journal에 경고 "필드 바꾸기: (형식)에서 숫자 필드 '…'를 찾지 못했습니다."(출처 `Effect`)가 남는다. 필드 이름 오타도 이 경고로 보인다.
- 틀을 바꾼 값이 숫자로 읽히지 않으면(틀이 빈 글이 됨 등) 그 대상은 건너뛰고 Journal에 경고 "필드 바꾸기: 값 '…'을(를) 숫자로 읽지 못했습니다."(출처 `Effect`)가 남는다.
- **빌려 쓰는 중에는 원본 자신의 필드를 바꾸지 않는다.** 빌려 쓰기(2-4e)로 낸 결과에서는 원본 조각 자신(`User`)의 필드는 그대로다. 쌓이는 값은 원본이 직접 발동할 때만 쌓인다. 다른 대상(카드·주인공 …)의 필드는 바꾼다.
- 행동 안에서 돌면 그 행동과 함께 Undo된다. 플레이어 데이터 객체의 필드라 저장·멀티플레이도 따라온다.
- 요약: `mult +1`, `counter -1`, `mult ×2`, `counter = 3`.
- 검증 오류: "필드 바꾸기: 필드를 적으세요." / "필드 바꾸기: 값에는 숫자(또는 {level}·{stacks}·{balance:ID} 틀)를 적으세요."

```csharp
// 코드로 만들 때: ChangeFieldEffect { field, change(FieldChange.Add·Multiply·Set), value(글자, 기본 "1"), clampZero }
var grow = new TargetedOutcome { targets = { new SelfTarget() }, effects = { new ChangeFieldEffect { field = "mult", value = "{level}" } } };
var countdown = new ChangeFieldEffect { field = "counter", value = "-1", clampZero = true };
```

### 2-4b. 이벤트 쏘기

고른 이벤트를 적은 값으로 쏜다. 그 이벤트를 받는 로그·도전과제·트리거·코드가 게임에서 쏜 것과 똑같이 반응한다.

| 칸 | 뜻 |
|---|---|
| `이벤트` | 쏠 이벤트(`[GameEvent]` 목록에서 고르기) |
| `값` | 줄마다 **필드**(경로) = **값**. 숫자·글자·참거짓·enum 필드만. 경로 `a.b`로 안쪽 필드도. 적지 않은 필드는 이벤트 클래스의 기본값. 값에 값 틀(2-4a)을 쓸 수 있다 |
| `횟수` | 몇 번 쏠지(1 이상) |

- **값 틀**은 쏘는 순간의 값으로 바뀐다. "이 조각이 발동했다" 이벤트 = 값 `pieceId = {user}`(사용자 = 그 조각). 받은 이벤트의 값을 옮기면 `amount = {event:amount}`, 목록 개수면 `count = {count:run.jokers}`.
- 요약 문장: `이벤트 쏘기: 턴 끝 (turn = 1, damage = 30)`, 2번 이상이면 `×횟수`.
- 검증: 이벤트를 고르지 않음·찾을 수 없음, 이벤트에 없는 필드 경로, 형식에 맞지 않는 값("'damage'에 'abc'를 넣을 수 없습니다(Int32)."), 횟수 0 이하를 오류로 띄운다.
- 실행 중 이벤트 형식을 찾지 못하거나 값(틀을 바꾼 값)을 넣지 못하면 그 줄은 건너뛰고 Journal에 경고(출처 `Events`)를 남긴다.
- 개발 명령(DB › 개발)의 시험 버튼, 그리고 패시브 효과·상태 효과의 결과에서 "발동했다"를 알리는 데 쓴다. 예: "한 턴 30 피해" = 이벤트 `TurnEnded`, 값 `turn = 1`, `damage = 30`(→ `22-Monitor-Debug.md` 7절).

### 2-4c. 행동 직전 이벤트 고치기 · 비용 내기

- **행동 직전 이벤트 고치기**: 행동 직전 이벤트 받기 트리거가 잡은 행동 직전 이벤트의 필드를 바꾸거나(`값으로 바꾸기` · `더하기` · `곱하기`) 막는다(`막기`). 칸은 필드 · 방법 · 값. 예: 유물 "가시 갑옷" = 행동 직전 이벤트(피해) → 행동 직전 이벤트 고치기 `amount` `더하기` `-5`. 행동 직전 이벤트가 아닌 Trigger의 결과에 두면 Journal 경고만 남고 아무 일도 하지 않는다.
  - 값에 **값 틀**(2-4a의 표)을 쓰면 발동한 순간의 값으로 바뀐다. 도발·대신 맞기 = `targetId` `값으로 바꾸기` `{user}`, 중첩만큼 감소 = `amount` `더하기` `-{stacks}`, 레벨별 감소 = `amount` `더하기` `-{level:2/4/7}`, 이 조커가 쌓은 배수 = `mult` `더하기` `{user:mult}`(→ `28-Attributes-Intercepts.md` 3-4·5-8~5-10).
- **비용 내기**: 플레이어 데이터의 숫자를 낸다. 칸은 플레이어 데이터 · 필드(비우면 값 전체) · 양 · 보일 이름(모자랄 때 실패 글에 들어갈 이름, 텍스트 키 또는 글). 행동 직전 이벤트 `비용 지불` 받기을 거치고, 모자라면 이 행동의 변경이 모두 취소된다(실패 글은 `30-Balance-Tables.md` 2-4). 행동 직전 이벤트의 `forId`(무엇을 위한 비용)는 이 결과를 가진 항목 ID, `userId`(내는 쪽)는 사용자다("행성 카드는 공짜"가 `forId`로 거른다).

자세한 칸·검증·예제는 `28-Attributes-Intercepts.md` 3-4·3-5·5장.

### 2-4c-2. 가중치 표에서 주기 · 가중치 표에서 고르기

DB › 가중치 표에서 무게만큼의 확률로 뽑은 항목을 플레이어 데이터에 넣는다(`가중치 표에서 고르기`는 뽑은 것을 후보로 보이고 고른 것만 넣는다). 칸은 가중치 표 · 횟수 · 서로 다른 것만 · 넣을 플레이어 데이터 · 필드. 꽝이면 아무것도 넣지 않는다. 예: 개발 명령 "유물 하나 받기" = 가중치 표에서 주기(`relicPool` → `run › relics`).

`가중치 표에서 고르기`에는 **후보 수에 더할 숫자**(`countBonus`, 플레이어 데이터의 숫자 하나) 칸이 더 있다. 요구하는 순간 그 값을 후보 수에 더한다("선택지 +1" = `run › rewardBonus`). 비우면 0이다. 이 결과로 만든 선택은 기다리는 동안 행동 안에서 `Game.RerollChoice(선택 id)`로 같은 표·같은 후보 수로 다시 뽑을 수 있다(비용은 부르는 행동이 낸다, `30-Balance-Tables.md` 3-3·5-13).

뽑은 원본 ID를 넣는 규칙(**원본 ID 넣기**)은 넣을 곳의 형식이 정한다.

| 넣을 곳 | 들어가는 것 |
|---|---|
| 글자 목록 | ID를 요소로 |
| 인스턴스 형식의 목록 | 그 인스턴스의 사본을 요소로 |
| 딕셔너리(ID → 숫자) | 그 키의 개수 +1(나온 만큼, 없던 키는 0에서). ID → 레벨 패시브 효과의 레벨업, 재료 개수 |
| 숫자 하나 | 나온 개수만큼 + |
| 값 하나(글자·enum·클래스) | 그 ID로 바꾼다(클래스면 그 인스턴스의 사본). 여럿이면 차례로 넣어 마지막 것. 고른 직업 → `run › job` |

난수 규칙·칸·검증은 `30-Balance-Tables.md` 2-3·3-3.

### 2-4d. 선택 요구

후보(대상 Block) 중에서 플레이어가 고르게 하고, 답이 오면 고른 것마다 효과를 준 뒤 '고른 뒤 결과'를 실행한다. 칸은 안내 글 · 후보 · 최소 개수 · 최대 개수 · 고른 것에 효과 · 고른 뒤 결과. 요구한 행동은 답을 기다리지 않고 끝나고, 답하기가 따로 행동 하나가 된다. 예: 카드 "준비" = 선택 요구(후보 `손패`, 1개) → 고른 것에 효과 `상태 효과 걸기`(이번 턴 비용 0). 칸·흐름·화면 쪽 코드는 `29-Statuses-Schedules-Choices.md` 3-3·5-9·5-10.

### 2-4e. 빌려 쓰기

옆 칸·맨 왼쪽 같은 다른 **조각**의 패시브 효과를 지금 순간(받은 이벤트·받은 행동 직전 이벤트)으로 한 번 더 낸다. 조각은 패시브 효과의 가진 목록(조커 칸 `run › jokers` 같은 객체 목록)에 놓인 요소 하나다. 그 요소의 '일치 필드'(`joker`)가 어느 패시브 효과인지 정한다(17장 0절). 복사·흉내 기믹(오른쪽 조커의 액티브 효과 복사 · 맨 왼쪽 조커의 액티브 효과 복사)을 코드 없이 만든다.

`+ 결과 추가` → 그룹 **결과** → `빌려 쓰기`. 결과 종류가 `Targeted`라 대상에게 효과를 고를 수 있는 칸이면 나온다. 보통 '놓인 것마다' 패시브 효과의 결과에 둔다.

| 칸 | 툴팁 | 값 |
|---|---|---|
| `targets` | 빌릴 조각 | `+ 대상 추가`로 대상 Block을 더한다(보통 `자리로 고르기` — `origin`(기준) `사용자(가진 쪽)`, `list`(기준이 놓인 목록) `run › jokers`, `direction`(방향) `오른쪽`). 위에서부터 차례로 거른다. 비우면 검증 오류 |

```
'청사진'(놓인 것마다 패시브 효과)이 점수 단계 이벤트를 받아 결과 '빌려 쓰기'를 낸다
  1. 빌릴 조각 고르기     대상 Block → 오른쪽 조커(자기 자신은 뺀다)
  2. 그 조각의 패시브 효과 찾기  가진 목록에 그 조각이 있고 '일치 필드'가 맞는 패시브 효과(예: '+4 배수'). 여럿이면 모두
  3. 지금 순간으로 검사    그 패시브 효과의 발동(본 발동·발동 더의 줄마다)을 원본 조각을 가진 쪽으로 검사
                          가진 쪽 필드는 빌리는 조각(청사진)의 id와 비교
  4. 참인 줄마다 결과      User = 원본 조각, Owner = 빌린 패시브 효과 ID, IsBorrowed = 참
```

- **가진 쪽 필드는 빌리는 쪽 기준이다.** 메인 룰이 조커마다 "이 조커의 차례" 이벤트(`pieceId`)를 쏘고 패시브 효과가 가진 쪽 필드 `pieceId`로 제 차례만 받으면, 청사진의 차례 이벤트에서 오른쪽 조커의 패시브 효과가 난다(원본의 차례가 아니어도).
- **원본의 값을 읽는다.** 결과의 사용자는 원본 조각이다. 그래서 빌린 결과의 `사용자 자신`·`{level}`은 원본 조각이고, 원본의 배수·레벨을 읽는다.
- **쌓이는 값은 원본만.** 빌려 쓰는 중에는 효과 `필드 바꾸기`가 원본 조각 자신의 필드를 바꾸지 않는다(2-4a). "점수를 낼 때마다 이 조커 배수 +1"을 빌리면 배수는 쌓이지 않고, 쌓인 값을 쓰는 부분만 한 번 더 난다. 다른 대상의 필드는 바꾼다.
- **차례**: 빌린 결과는 빌려 쓰는 조각의 차례에 난다. '놓인 것마다' 패시브 효과는 목록 순서로 발동하므로(11장 6-1), 왼쪽의 청사진이 오른쪽 조커를 빌리면 오른쪽 조커가 직접 발동하기 전에 한 번 더 난다.
- **고리를 끊는다.** 이미 빌려 쓰는 중인 같은 패시브 효과·같은 원본을 다시 빌리면 하지 않는다. 오른쪽을 빌리는 조각과 맨 왼쪽을 빌리는 조각이 서로를 빌려도 몇 번 안에 끝난다.
- **지금 순간이 필요하다.** 받은 이벤트·행동 직전 이벤트로 검사하므로, 조건 충족만으로 발동한 결과(이벤트 없음)에서는 이벤트 받기 패시브 효과를 빌리지 못한다.
- 빌릴 패시브 효과가 없는 조각(빈 칸, 패시브 효과가 없는 요소)과 침묵한 조각('걸린 쪽의 패시브 효과를 끔' 상태 효과)은 건너뛴다. 가진 목록이 글자 목록·딕셔너리인 패시브 효과(`run › relics`)는 조각이 없어 빌릴 수 없다.
- 검증 오류: "빌려 쓰기: 빌릴 조각을 고르는 대상 Block을 넣으세요(예: 자리로 고르기 — 오른쪽)." / "빌려 쓰기: 비어 있는 Block이 있습니다(스크립트가 지워졌을 수 있음)."

```csharp
// 코드로: BorrowOutcome { targets }(결과) · Game.BorrowPassive(패시브 효과 ID, 원본 조각)(지금 순간으로, 결과·[BindTo] 안에서)
var blueprint = new BorrowOutcome { targets = { new PlaceTarget { list = new PlayerDataListRef { key = "run", path = "jokers" }, direction = PlaceDirection.Right } } };
c.Game.BorrowPassive(GameIds.Passive.PlusFour, rightJoker);   // 결과를 냈으면 true
```

| 멤버 | 뜻 |
|---|---|
| `bool Game.BorrowPassive(string passiveId, object lender)` | 지금 검사·발동 중인 순간(`session.Triggers.Current`)으로 그 패시브 효과를 원본 `lender`의 것으로 한 번 낸다. 순간 밖이거나, 트리거가 거짓이거나, 이미 빌려 쓰는 중이면 `false` |
| `session.Passives.Borrow(id, lender, moment)` | 순간을 직접 준다 |
| `session.Passives.MatchingFor(element)` | 그 조각을 가진 것으로 보는 패시브 효과들 |
| `TriggerContext.IsBorrowed` | 빌려 쓰는 순간이면 `true`(3-3) |

### 2-5. 요약과 검증

도전과제·액티브 효과는 상세 위쪽 요약에 "결과 …"가 자연어로 보인다(예: `party › gold 100 증가`). 검증은 없는 플레이어 데이터·필드, 형식에 맞지 않는 값, 비어 있는 인스턴스·요소, 잠금 밖 종류, 읽을 이벤트가 없는 반영할 값을 오류로 띄운다.

---

## 3. 코드로 쓰는 법 — 문법 요소를 하나씩

### 3-1. `PlayerDataReward` — 코드에서 직접 만들기

```csharp
var gold  = new PlayerDataReward { key = "party", path = "gold", amount = 100 };            // 숫자 증감
var title = new PlayerDataReward { key = "party", path = "titles", items = { "용사" } };      // 리스트에 넣기
var mats  = new PlayerDataReward { key = "party", path = "materials", keys = { "iron", "wood" }, items = { "3", "1" } }; // 딕셔너리
var sword = new PlayerDataReward { key = "party", path = "gear", items = { "sword" } };       // 리스트에 인스턴스 사본 넣기(인스턴스 ID)
var iron  = new PlayerDataReward { key = "party", path = "materials[iron]", amount = 3 };    // 딕셔너리의 키 하나(없던 키면 0에서 3)
var heal  = new PlayerDataReward { key = "battle", path = "units[hero].hp", amount = 5 };    // 목록에서 id가 hero인 요소의 필드
```

| 필드 | 뜻 |
|---|---|
| `key` | 플레이어 데이터 ID |
| `path` | 필드 경로(비우면 값 전체). `name[키]`로 딕셔너리의 키 하나·목록에서 id로 고른 요소(2-2, 11장 3-1) |
| `amount` | 숫자 증감 |
| `value` | 글자·참거짓·enum 값, 또는 클래스 필드면 인스턴스 ID |
| `items` | 리스트에 넣을 요소들(숫자·값·인스턴스 ID를 글자로), 딕셔너리면 값들 |
| `keys` | 딕셔너리 키들(`items`와 같은 순서) |

실행할 때 문제가 있으면(없는 플레이어 데이터, 없는 인스턴스, 읽을 수 없는 값 — 필드 형식에 담기지 않는 숫자(`int`에 30억)도) 그 줄은 건너뛰고 Journal에 경고(출처 `Reward`, "플레이어 데이터에 주기(…): …")를 남긴다. DB 검증도 같은 값을 오류로 보인다.

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
| `Owner` | 결과를 가진 항목의 ID(도전과제·로그·액티브 효과·인스턴스 …) |
| `Event` | 이벤트를 받은 순간이면 그 이벤트, 행동 직전 이벤트 순간이면 그 행동 직전 이벤트(아니면 `null`) |
| `IsIntercept` | 행동 직전 이벤트로 발동했으면 `true`(이때 `Event`를 고치면 일에 반영된다) |
| `IsUse` | 사용하는 순간이면 `true` |
| `IsBorrowed` | 빌려 쓰기(2-4e)로 낸 결과면 `true`. 이때 `User`는 빌려 준 원본 조각이다 |
| `User` | 사용자(사용하는 순간). 상태 효과·패시브 효과의 결과에서는 가진 쪽('놓인 것마다'면 그 조각) |
| `Picked` | 플레이어가 고른 대상들 |
| `Power` | 효과 배율(기본 1). 액티브 효과·패시브 효과의 결과에서 행동 직전 이벤트 `효과 배율`을 거친 값이다(2-4). 프로젝트 결과·효과 Block이 수치에 곱하려면 직접 곱한다 |

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
| `Use` (`AbilityUse`) | 이번 실행 정보: `Use.Ability`(액티브 효과에서 실행 중이면 그 정의, 아니면 `null`), `Use.User`, `Use.Picked`, `Use.Context`(`TriggerContext`). 효과 배율은 `Use.Context.Power`(16장 2-12) |
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

- 기본 제공 `SelfTarget`("사용자 자신"): 앞 대상을 버리고 사용자 하나를 낸다. 그 밖의 기본 대상 Block(`DataListTarget`·`EventFieldTarget`·`FilterTarget`·`StatusFilterTarget`·`RandomTarget`·`SortTarget`)은 `16-Abilities-Usable.md` 2-6.
- 도전과제·로그·퀘스트처럼 **사용이 아닌** Trigger에서는 사용자와 고른 대상이 없다. 대상 Block이 `Game`으로 직접 찾거나 `Use.Context.Event`에서 찾아야 한다(4-6). 목록에서 찾기·이벤트의 필드에서 찾기는 기본 대상 Block으로 코드 없이 된다.

### 3-7. 결과가 실행되는 방식

- 결과 칸의 결과들은 위에서부터 차례로 실행된다. 로그에 기록을 뺀 결과는 실행마다 Journal에 "결과: (자연어 요약)" 한 줄을 남긴다(출처: `Achievements`·`Stats`·`Abilities` 등. 액티브 효과·사용 기능의 대상에게 효과는 이 줄 없이 액티브 효과 사용 행동의 기록에 묶인다).
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

### 4-5. 고른 대상 + 주변 적에게 피해(액티브 효과)

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

액티브 효과의 결과: `대상에게 효과` → 대상 `주변 n칸의 적 추가`(range 1) → 효과 `피해`(10). 게임 코드: `Game.Use("splash", hero, orc)`.

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

`처치한 유닛` 같은 "이벤트의 id 필드가 가리키는 개체"는 기본 대상 Block `이벤트의 개체`(`field` `killerId`)로도 된다(코드 없음, `16-Abilities-Usable.md` 2-6). 정해진 양만 주면 효과도 기본 Block `필드 바꾸기`(`field`(필드) `exp`, `change`(방법) `더하기`, `value`(값) `50`, 2-4a)로 된다. 그러면 이 예는 코드가 하나도 없다. 이벤트의 `exp`를 더하는 계산(`addEventExp`)이 있을 때만 위의 `경험치` Block이 필요하다. 위 코드는 대상·효과 Block을 직접 만드는 모양을 보이는 예다.

같은 일을 함수 하나로 쓰려면 `[BindTo]`가 더 짧다(→ `13-BindTo.md` 3-1).

> 매번 일어나는 게임 규칙("모든 처치에 경험치")은 처치를 처리하는 행동 코드에 바로 쓰는 것이 자연스럽다. Trigger·결과는 데이터로 고르고 바꾸고 싶은 단위(도전과제·퀘스트·아이템·액티브 효과)에 쓴다.

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

### 4-9. 쌓이는 조커와 복사 — `필드 바꾸기` · `빌려 쓰기`(코드 없음)

조커 칸 게임: "버스: 점수를 낼 때마다 이 조커의 배수 +1, 그리고 이 조커의 배수만큼 판 배수 +." "청사진: 오른쪽 조커의 액티브 효과를 복사." 플레이어 데이터 `run`에 `jokers`(`List<Joker>`, 왼쪽부터 칸 순서)와 `mult`(판 배수)가 있다고 본다.

```csharp
[Serializable] public class Joker { public string id; public string joker; public int mult; }   // joker = 패시브 효과 ID
```

**에디터**(DB › 패시브 효과, 둘 다 `ownerKey`·`ownerPath`(가진 목록) `run › jokers`, `matchField`(일치 필드) `joker`, `perElement`(놓인 것마다) 켬, 17장 0절):

1. `bus`: `trigger`(발동) `점수 단계 받기`. `outcomes`(결과) ① `대상에게 효과` → 대상 `사용자 자신` → 효과 `필드 바꾸기`(`field`(필드) `mult`, `change`(방법) `더하기`, `value`(값) `1`) ② 판 배수에 이 조커의 배수를 더하는 결과(프로젝트 결과 Block, `User`의 `mult`를 읽는다).
2. `blueprint`: `trigger`(발동) `점수 단계 받기`. `outcomes`(결과) `빌려 쓰기` → `targets`(빌릴 조각) `자리로 고르기`(`origin`(기준) `사용자(가진 쪽)`, `list`(기준이 놓인 목록) `run › jokers`, `direction`(방향) `오른쪽`).

조커 칸이 [청사진, 버스(배수 2)]이고 판 배수가 1일 때 점수 단계 한 번:

| 차례 | 일어나는 일 | 버스의 `mult` | 판 배수 |
|---|---|---|---|
| 1. 청사진(왼쪽) | 버스를 빌린다: `필드 바꾸기`는 원본 자신이라 건너뛴다. 원본의 배수 2를 더한다 | 2 | 1 + 2 = 3 |
| 2. 버스 | 자기 배수 +1 → 3. 배수 3을 더한다 | 3 | 3 + 3 = 6 |

- 쌓이는 값(`mult`)은 버스가 직접 발동할 때만 오른다. 청사진은 쌓기 전의 원본 값(2)을 읽는다.
- 두 조커의 자리를 바꾸면 청사진의 오른쪽이 비어 빌릴 것이 없다. 버스만 발동한다.
- 레벨마다 다르게 쌓으려면 `value`(값)를 `{level:1/2/3}`처럼 둔다. 레벨은 패시브 효과의 `levelField`(레벨 필드)가 정한다(17장 0절).
- 점수를 행동 직전 이벤트(`점수` 행동 직전 이벤트)로 내는 게임이면 ②도 `행동 직전 이벤트 고치기` `mult` `더하기` `{user:mult}`로 코드 없이 된다(17장 0-3 Green Joker).

---

## 5. 주의할 점 / 자주 하는 실수

- **사용이 아닌 Trigger에서 대상 없이 `대상에게 효과`를 쓴다.** 도전과제·로그에는 고른 대상이 없어서, 대상 Block이 비어 있거나 `사용자 자신`만 있으면 아무 일도 일어나지 않는다. 대상을 직접 찾는 대상 Block(기본 제공 `플레이어 데이터 목록`·`이벤트의 개체`, 또는 프로젝트의 Block)을 둔다.
- **딕셔너리의 키 하나에 주려고 딕셔너리 전체를 고른다.** 에디터에서는 딕셔너리 줄에 `[키] → [값]`을 적는다. 코드에서는 `path = "materials[iron]"`처럼 키 경로로 준다(2-2).
- **행동 직전 이벤트 고치기·필드 바꾸기의 `{stacks}`를 상태 효과가 아닌 곳에 쓴다.** 패시브 효과·프로젝트 기능에서는 0이다. 패시브 효과의 레벨은 `{level}`·`{level:2/4/7}`로 읽는다(반대로 `{level}`은 패시브 효과가 아닌 곳에서 0이다).
- **`필드 바꾸기`와 `스탯 보정`을 헷갈린다.** `필드 바꾸기`는 저장된 값을 바꿔 그대로 남는다(쌓이는 값·카운터). 장비·버프처럼 떼면 돌아가야 하는 값은 `스탯 보정`(28장 3-1)이다.
- **`필드 바꾸기`의 필드 이름이 틀린다.** 그 필드가 없는 대상은 건너뛰고 Journal에 경고만 남는다. 값이 안 바뀌면 경고와 대상의 형식을 본다(2-4a).
- **빌려 쓴 결과로 쌓이는 값이 오를 거라 기대한다.** 빌려 쓰는 중에는 `필드 바꾸기`가 원본 자신의 필드를 바꾸지 않는다. 쌓이는 값은 원본이 직접 발동할 때만 오른다(2-4e).
- **`빌려 쓰기`를 '놓인 것마다'가 아닌 패시브 효과에 둔다.** 그때 사용자는 가진 목록을 품은 쪽(`run`)이라 `자리로 고르기`의 기준(사용자)이 목록에 없어 빌릴 조각이 없다. 조각을 기준으로 고르려면 `perElement`(놓인 것마다)를 켠다.
- **대상 Block에서 값을 바꾼다.** 대상 Block은 고르기만 한다. 바꾸는 일은 효과 Block에서.
- **효과의 형식과 대상의 형식이 다르다.** `Effect<Unit>`에 `Gear`가 오면 조용히 건너뛴다.
- **액티브 효과·사용 기능의 대상에 `id`가 없다.** `Game.Use`가 끝날 때 최종 대상들의 `id`로 `액티브 효과 사용` 이벤트를 쏜다. `public string id;`가 없는 대상이면 예외가 나고 그 사용 전체가 취소된다.
- **로그가 아닌 기능에서 `로그에 기록`의 로그 칸을 비운다.** 비어 있으면 "이 결과를 가진 항목"의 로그에 쓰려 하는데, 도전과제 ID는 로그가 아니다. 이때는 아무것도 기록하지 않고 경고("로그에 기록: 기록할 로그를 고르지 않았습니다")만 남는다. 도전과제·프로젝트 기능에서는 로그를 꼭 고른다.
- **`반영할 값`을 읽을 이벤트가 없다.** 조건 충족만으로 된 Trigger에는 이벤트가 없으므로 반영할 값을 비워(횟수 1) 쓰거나, 이벤트 받기를 둔다(검증 오류로 보인다).
- **리스트 안쪽 필드를 주기로 바꾸려 한다.** `gear[].power`는 고를 수 없다. 리스트 요소를 바꾸는 일은 대상 Block(`플레이어 데이터 목록` 등) + 효과 Block으로 쓴다. 숫자 필드 하나면 효과 `필드 바꾸기`(2-4a)로 코드 없이 된다. 그 밖에는 프로젝트 효과 Block이나 코딩한 결과다.
- **구조체 안의 필드에 준다.** 부모가 구조체면 바꿀 수 없다는 경고만 남고 건너뛴다. 클래스로 바꾼다.
- **`Reward` 클래스에 `[Serializable]`·`[BlockInfo]`를 빠뜨린다.** 에셋에 저장되지 않거나 메뉴 이름이 클래스 이름으로 보이고, 계약 검사에서 걸린다.
- **결과에서 행동을 `Do`할 때**: 결과가 행동 안에서 돌면(액티브 효과 사용 등) 그 행동의 일부로 실행되고, 행동 밖에서 돌면(프레임 끝의 도전과제 달성 등) 새 행동 하나가 된다. 어느 쪽이 될지 신경 쓰고 싶지 않으면 공통 로직을 함수로 빼서 부른다.
