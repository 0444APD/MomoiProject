# 11. Trigger와 조건

**한 줄 요약**: Trigger는 "언제 발동하는가"를 정하는 칸이다. 잎(이벤트 받기 · 조건 충족 · 사용 · 행동 직전 이벤트)을 `&& AND` · `|| OR` · `! NOT`과 괄호로 중첩해 만든다. 로그·도전과제·액티브 효과·사용 기능·프로젝트가 만든 기능(퀘스트 등)이 모두 같은 Trigger 칸을 쓰고, 기능마다 고를 수 있는 잎의 종류만 다르다.

**언제 쓰는가**

- "보스 처치를 받는 순간"처럼 이벤트에 반응할 때(이벤트 받기 + 값 조건).
- "골드가 1000 이상이 되는 순간"처럼 값의 상태에 반응할 때(조건 충족).
- "아이템·액티브 효과를 쓰는 순간, 사용자가 살아 있을 때만"처럼 사용을 막을 때(사용 AND 조건 충족).

이 문서에서 쓰는 용어:

| 용어 | 뜻 |
|---|---|
| Trigger | 언제 발동하는지를 담은 나무. 코드 형식 `Trigger`(추상)와 그 잎·논리 클래스. |
| Outcome | 발동했을 때 무엇을 하는지(결과). Trigger와 짝을 이룬다(→ `12-Outcomes.md`). |
| Block | 에디터에서 골라 조립하는 로직 단위. 조건 Block(`Condition`), 결과 Block(`Reward`), 효과 Block(`Effect`), 대상 Block(`TargetSelector`)이 있다. |
| 순간 | Trigger를 검사하는 한 시점. 이벤트를 받은 순간, 값이 바뀐 순간, 사용하는 순간, 행동 직전 이벤트를 받는 순간(행동 직전 이벤트가 지나는 순간) 중 하나다. |
| TriggerContext (컨텍스트) | 그 순간의 정보: 이벤트, 사용자, 고른 대상, 이 Trigger를 가진 항목 ID. 조건과 결과가 읽는다. |
| 값 조건 (ValueFilter) | 어떤 객체(이벤트, 플레이어 데이터 값, 리스트 항목)를 뿌리로 두고 필드를 비교하는 식. |

---

## 1. Trigger 나무의 구조

| 잎·논리 | 표시 이름 | 코드 형식 | 참이 되는 때 |
|---|---|---|---|
| 잎 | 이벤트 받기 | `EventTrigger` | 고른 이벤트를 받은 그 순간, 값 조건(`filter`)도 참일 때 |
| 잎 | 조건 충족 | `ConditionTrigger` | 안의 조건 Block(`condition`)이 그 순간 참일 때 |
| 잎 | 사용 | `UseTrigger` | `Game.Use`로 쓰는 순간 |
| 잎 | 행동 직전 이벤트 받기 | `InterceptTrigger` | 고른 행동 직전 이벤트가 `Game.Intercept`를 지나는 순간(일이 일어나기 직전), 행동 직전 이벤트 조건(`filter`)도 참일 때(→ `28-Attributes-Intercepts.md`) |
| 논리 | `&& AND` | `AllTrigger` | 안의 것(`triggers`)이 **같은 순간에** 모두 참 |
| 논리 | `\|\| OR` | `AnyTrigger` | 안의 것 중 하나가 참 |
| 논리 | `! NOT` | `NotTrigger` | 안의 것(`trigger`)이 거짓 |

**순간의 규칙**이 핵심이다.

- 이벤트 받기는 **그 이벤트를 받은 순간에만** 참이다. 다른 순간(값이 바뀐 순간, 다른 이벤트를 받은 순간)에는 거짓이다.
- 조건 충족은 검사하는 순간의 값으로 판정한다. 조건이 읽는 값이 바뀔 때마다 이벤트 없는 순간으로 다시 검사된다.
- 그래서 `보스 처치 받기 AND 골드 100 이상` = "골드가 100 이상인 상태에서 보스 처치를 받은 순간"이다. 골드가 나중에 100이 되어도 그 순간에는 이벤트가 없으므로 발동하지 않는다.
- 행동 직전 이벤트는 그 행동 직전 이벤트가 지나는 순간에만 참이다. 조건 충족과 `AND`로 묶으면 "그 상태일 때만 행동 직전 이벤트를 받아 고친다"가 된다. 결과의 `행동 직전 이벤트 고치기`가 그 행동 직전 이벤트를 바꾸거나 막는다.
- `OR`는 먼저 참이 되는 쪽에서 발동한다.
- 조건만으로 된 Trigger는 참이 되는 순간 발동한다. 등록할 때(게임이 켜질 때·항목을 정의할 때) 이미 참이면 첫 평가(Flush)에서 바로 발동한다. 반복하는 기능(로그·상태 효과·패시브 효과, `repeat: true`)이면 발동한 뒤 한 번 거짓이 되었다가 다시 참이 될 때 또 발동한다(참인 동안 계속 발동하지 않는다).
- 반복이 아닌 것(도전과제)이 행동 안에서 발동하면 그 행동이 성공해야 굳는다. 행동이 실패하거나 미리보기(`Preview`·`CanDo`)였으면 감시가 그대로 남는다.
- 서로 다른 이벤트 받기 둘을 `AND`로 묶으면 같은 순간에 오지 않으므로 발동하지 않는다. 검증이 경고한다.
- `NOT` 안에 이벤트 받기·사용을 넣으면 "그 순간이 아닌 모든 때"가 되어 뜻이 흐려진다. 검증이 경고한다. 이벤트의 값을 거르려면 이벤트 받기의 조건 칸을 쓴다.
- 조건 Block `확률`은 이벤트·행동 직전 이벤트를 받는 순간과 쓰는 순간에만 굴린다. 이벤트 받기·행동 직전 이벤트 받기·사용과 `AND`로 묶어 쓴다(2-6).

### 기능별 잠금

| 기능 | 고를 수 있는 잎 |
|---|---|
| 로그(`StatData.triggers`) | 이벤트 받기 |
| 도전과제(`AchievementData.trigger`) | 이벤트 받기 · 조건 충족 |
| 액티브 효과(`AbilityData.trigger`, 기본 `사용`) | 사용 · 조건 충족 |
| 사용 기능(`Usable.when`, 기본 `사용`) | 사용 · 조건 충족 |
| 상태 효과(`StatusData.trigger`, 걸려 있는 동안 트리거) | 이벤트 받기 · 조건 충족 · 행동 직전 이벤트. 걸린 대상마다 결과가 돈다(→ `29-Statuses-Schedules-Choices.md` 2-3) |
| 패시브 효과(`PassiveData.trigger`, 발동) | 이벤트 받기 · 조건 충족 · 행동 직전 이벤트. 가진 목록에 그 ID가 있는 쪽마다 결과가 돈다. '놓인 것마다'면 목록의 요소마다, 목록 순서로 돈다(6-1, → `17-Custom-Features.md` 0절). `rules`(발동 더)의 줄도 같은 셋을 쓴다(사용은 패시브 효과에서 발동하지 않는다). `modifierWhen`(보정 조건)은 조건 충족만 |
| 프로젝트 기능 | 필드의 `[TriggerSlot]`이 정한다(5장). 행동 직전 이벤트는 로그·도전과제·액티브 효과·사용 기능 칸에는 없고, 상태 효과·패시브 효과와 잠금에 `Intercept`를 넣은 프로젝트 기능에서 고른다 |

액티브 효과·상태 효과·패시브 효과의 DB 종류(`AbilityData`·`StatusData`·`PassiveData`)는 프로젝트가 템플릿에서 들인 클래스다(`Assets/MomoiProject/Code/Rules/Elements/`, 관례 0절). 위 잠금은 템플릿의 `[TriggerSlot]` 값이고, 그 클래스에서 바꿀 수 있다.

---

## 2. 에디터에서 쓰는 법

DB 상세 패널에서 Trigger 필드(`trigger`, `when` 등. 칸 이름표는 코드의 변수 이름이고 `[FieldLabel]` 글자 "트리거"는 마우스를 올리면 보인다)는 **한 줄 식 편집기**로 그려진다.

### 2-1. 블럭 더하기

1. 칸이 비어 있으면 "트리거 없음 — 아래에서 블럭을 더하세요"가 보인다.
2. **`+ 블럭`**을 누르면 목록이 뜬다. 칸의 잠금에 있는 것만 나온다.
   - 그룹 **트리거**: `이벤트 받기`, `사용`, `행동 직전 이벤트 받기`(칸이 허락할 때)
   - 그룹 **조건 충족 · 분류**: 조건 Block들(`비교`, `플레이어 데이터 검사`, `도전과제 달성`, `상태 효과 검사`, `확률`, `자리 검사`, `사용자 필드 검사`, 프로젝트가 만든 조건 Block). 고르면 조건 충족 잎으로 감싸 넣고, 식 안에서는 조건 Block 그대로 그린다.
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
| 플레이어 데이터 검사 | 플레이어 데이터 | `PlayerDataCondition` | 플레이어 데이터 + 조건(값 조건 식) | 플레이어 데이터 값(클래스·컬렉션 포함)을 값 조건으로 검사. 숫자·글자·참거짓·목록 하나인 값(골드 등)은 필드를 비워 값 전체를 본다 |
| 도전과제 달성 | 도전과제 | `AchievementUnlockedCondition` | `{achievement} 달성` | 다른 도전과제를 달성했는지 |
| 상태 효과 검사 | 상태 효과 | `StatusCondition` | `{who}의 {status} {op} {stacks}` (누구: `사용자·가진 쪽` / `이벤트의 개체`(+ 이벤트 필드), 비교 `>=` `<=` `>` `<` `==`) | 사용자(상태 효과·패시브 효과면 가진 쪽) 또는 이벤트가 가리키는 개체에 그 상태 효과가 몇 중첩 걸려 있는지. 걸려 있지 않으면 0. `status`(상태 효과)를 비우고 `kind`(종류)를 고르면 그 종류(버프·디버프)가 몇 가지 걸려 있는지. 쿨다운 = 사용 조건 `사용자의 쿨다운 <= 0`(→ `29-Statuses-Schedules-Choices.md` 3-4·5-12) |
| 확률 | 조건 | `ChanceCondition` | `{percent}% 확률` (`percent`(확률(%)) · `stream`(난수 흐름(비우면 chance))) | 그 확률로 참. 행동 안에서 이벤트·행동 직전 이벤트·사용 순간에만 굴린다(2-6) |
| 자리 검사 | 조건 | `PlaceCondition` | `{spot}` (`who`(누구) · `list`(목록) · `columns`(줄 길이) · `spot`(자리) · `not`(아닐 때 참) …) | 사용자(가진 쪽)나 이벤트의 개체가 목록(조커 칸·보드·가방)에서 그 자리에 있는지(2-7) |
| 사용자 필드 검사 | 조건 | `UserFieldCondition` | `사용자의 {path} {op} {value}` (`path`(필드(사용자의)) · `op`(비교) · `value`(값(틀 가능))) | 사용자(액티브 효과를 쓴 개체, 상태 효과·패시브 효과면 가진 쪽, '놓인 것마다'면 그 조각)의 필드를 비교. 이 조커의 `counter` 이상 `6`, 이 아티팩트의 `kills` 이상 `40`(2-8) |
| (프로젝트 조건) | `[BlockInfo]`의 분류 | `SimpleCondition` 상속 | `[BlockInfo]`의 문장 | 코드로 쓴 조건(4장) |

도전과제·액티브 효과는 DB 상세 위쪽 요약에 "트리거 …" 한 줄이 자연어로 보인다(로그는 "반영 …" 줄). **Alt**를 누르고 있으면 코드 형태(`on 유닛 처치 where combo >= 10 && …`)로 바뀐다. 문제가 있으면 검증 오류가 빨간 글씨로 뜨고, 오류가 있으면 빌드가 막힌다.

### 2-5. 행동 직전 이벤트 블럭

칸의 잠금에 행동 직전 이벤트가 있을 때만 `+ 블럭` 목록에 나온다(로그·도전과제·액티브 효과·사용 기능 칸에는 없다. 상태 효과의 트리거·패시브 효과의 발동과, 잠금을 연 프로젝트 기능에 있다).

1. 블럭 문장 `{intentType} 행동 직전 이벤트 받기`의 행동 직전 이벤트 칸을 눌러 행동 직전 이벤트(`[Intent]`가 붙은 클래스 — 프로젝트의 것과 기본 제공 `사용`·`비용 지불`·`상태 효과 걸기`·`확률`·`효과 배율`, 28장 4-14)를 고른다.
2. `filter`(툴팁 "행동 직전 이벤트 조건")는 이벤트 조건과 같은 식이다. 뿌리는 고른 행동 직전 이벤트다(예: `targetId` 일치 `hero`).
3. `order`(툴팁 "순서(작을수록 먼저)"): 같은 행동 직전 이벤트에 여럿이 걸렸을 때의 차례. 같으면 등록 순서.

결과에는 `행동 직전 이벤트 고치기`(필드 · 방법 · 값)를 둔다. 행동 직전 이벤트·규칙 함수 쪽 코드와 예제(받는 피해 −5, 죽음 막기, 비용 0)는 `28-Attributes-Intercepts.md` 3-3·5장.

### 2-6. 조건 `확률`

`+ 블럭` → **조건 충족 · 조건** → `확률`. 문장 `{percent}% 확률`. "25% 확률로 한 번 더", "1/4 확률로 골드" 같은 운을 DB에서 만든다.

| 칸 | 툴팁 | 값 |
|---|---|---|
| `percent` | 확률(%) | 기본 25. 0보다 크고 100 이하 |
| `stream` | 난수 흐름(비우면 chance) | 난수 흐름 이름(07장). 비우면 `chance`. 다른 뽑기와 결과가 섞이지 않게 하려면 이름을 따로 준다 |

**굴리는 때와 굴리지 않는 때** — 순간(1절)마다 다르다.

| 순간 | 결과 |
|---|---|
| 행동 안에서 이벤트를 받는 순간 · 행동 직전 이벤트를 받는 순간 · 쓰는 순간(`Game.Use`·`Game.TryUse`) | 굴린다. 그 확률로 참 |
| '쓸 수 있나' 미리 보기(`Game.CanUse`, 버튼 흐리게) | 굴리지 않고 **참**. 버튼은 켜져 있고, 실제로 쓸 때 굴린다 |
| 값이 바뀌어 다시 보는 순간(이벤트 없는 조건 충족) | **거짓**. 그래서 조건 충족만으로 된 Trigger(`확률` 하나)는 발동하지 않는다 |
| 행동 밖(Undo 기록 밖에서 검사될 때) | 굴리지 않고 **거짓** |

- 이벤트 받기·행동 직전 이벤트 받기·사용과 `AND`로 묶어 쓴다. "적을 처치할 때 25%로 골드 +5" = `적 처치 받기` AND `확률`(`percent`(확률(%)) 25).
- **식의 맨 뒤에 둔다.** `AND`는 앞에서부터 검사하다 거짓이 나오면 멈춘다. 확률을 맨 뒤에 두면 앞 조건이 맞을 때만 굴린다.
- 가진 쪽마다 검사하는 상태 효과·패시브 효과(29장 2-3, 17장 0절)는 가진 쪽마다 따로 굴린다. 같은 조커가 둘이면 두 번 굴린다.
- **행동 직전 이벤트 `확률`을 거친다**(코드의 `Game.Chance`와 같은 길, 07장 2.15). "모든 확률 2배" = 행동 직전 이벤트 `확률` 받기 → `행동 직전 이벤트 고치기` `probability` 곱하기 2. 이 조건에서 굴릴 때 `sourceId`는 이 Trigger를 가진 항목 ID, `userId`는 사용자·가진 쪽 id다. 확률이 바뀌거나 막혀도 난수는 한 번만 굴리고, Undo하고 다시 해도 같은 결과다.
- **사용 조건에 두면** `Game.Use`는 굴려서 거짓이면 실패를 던진다(그 행동이 취소된다, 16장). 되는 것만 쓰려면 `Game.TryUse`를 쓴다. 실패를 던지지 않고 거짓을 돌려주며, 조건을 한 번만 검사하므로 한 번만 굴린다.
- 코드에서 굴릴 때는 `Game.Chance(0.25, "loot", source: c.Owner, user: c.User)`(행동 안에서). 같은 행동 직전 이벤트를 거친다.
- 요약: `25% 확률`. 검증 경고: "확률: 0보다 크고 100 이하로 적으세요."

### 2-7. 조건 `자리 검사`

`+ 블럭` → **조건 충족 · 조건** → `자리 검사`. 플레이어 데이터의 목록 하나(`List<T>`)를 칸으로 보고, 누가 그 목록의 어느 자리에 있는지 본다. "가방 맨 아래 줄일 때만", "양옆이 비었을 때만", "맨 왼쪽 조커일 때만".

| 칸 | 툴팁 | 값 |
|---|---|---|
| `who` | 누구 | `사용자(가진 쪽)`(기본) / `이벤트의 개체`. `앞의 대상`은 검증 오류 |
| `eventField` | 이벤트 필드(id) | `이벤트의 개체`일 때만 보인다. 받은 이벤트(행동 직전 이벤트 포함)에서 개체 id가 든 필드 |
| `list` | 목록 | 플레이어 데이터 · 필드(목록). 예: `run › jokers`, `run › bag` |
| `columns` | 줄 길이(0 = 한 줄) | 한 줄(조커 칸·핸드)은 0, 격자(가방·보드)는 열 수 |
| `spot` | 자리 | 아래 표 |
| `index` | n(n번째 칸) | `spot`이 `n번째 칸(0부터)`일 때만 보인다 |
| `not` | 아닐 때 참 | 켜면 그 자리가 **아닐** 때 참 |

- **칸**: 칸 번호 = 목록 위치(0부터), 빈 칸 = `null`인 요소. 격자는 번호를 열 수로 나눠 줄·열을 정한다(6열이면 0~5가 첫 줄). 좌표는 화면 기준이라 "위" = 앞 줄이다. 마지막 줄은 덜 차도 된다(34칸 = 6 × 5 + 4).
- **누구**: `사용자(가진 쪽)`는 컨텍스트의 `User`다. 액티브 효과면 쓰는 쪽, '놓인 것마다' 패시브 효과면 그 조각이다. 목록에서 같은 객체 또는 같은 id(요소가 id 글자여도)인 칸을 찾는다.

| `spot`(자리) | 참인 때 |
|---|---|
| 맨 위 줄 · 맨 아래 줄 | 첫 줄 · 마지막 줄(덜 찬 줄이어도). 한 줄이면 늘 둘 다 |
| 맨 왼쪽 열 · 맨 오른쪽 열 | 첫 열 · 마지막 열(한 줄이면 첫 칸 · 끝 칸) |
| 가장자리(상하좌우 중 바깥이 있음) | 위·아래·왼쪽·오른쪽 중 하나라도 목록 밖이다. 한 줄은 위아래가 없어 모두 가장자리다 |
| 안쪽(가장자리가 아님) | 상하좌우가 모두 목록 안이다 |
| 목록 맨 처음 · 목록 맨 끝(차 있는 것 중) | 차 있는 칸 중 첫 칸 · 끝 칸 |
| 같은 줄 맨 앞(차 있는 것 중) | 그 줄에서 차 있는 칸 중 첫 칸 |
| 양옆이 빈 칸 | 왼쪽·오른쪽 칸이 빈 칸이거나 목록 밖 |
| 같은 줄에 혼자 | 같은 줄의 다른 칸이 모두 빈 칸 |
| n번째 칸(0부터) | 칸 번호가 `index`(n)다 |
| 같은 열 맨 앞(차 있는 것 중, 위부터) | 같은 열에서 차 있는 칸 중 맨 위 칸. 열 우선으로 담은 보드(한 열 = 한 레인)의 "레인 맨 앞" |
| 같은 열에 혼자 | 같은 열의 다른 칸이 모두 빈 칸(열 우선 보드의 "레인에 혼자") |

- **목록에서 찾지 못하면 거짓이다.** 목록이 없거나, 누구가 목록에 없으면 `not`(아닐 때 참)을 켜도 거짓이다.
- 값을 바꾸지 않으므로 사용 조건(액티브 효과·사용 기능)·패시브 효과의 발동 어디에나 둔다. 목록이 바뀌면 다시 평가된다.
- 예: "가방 맨 아래 줄일 때만" = `자리 검사`(`who`(누구) `사용자(가진 쪽)`, `list`(목록) `run › bag`, `columns`(줄 길이) `6`, `spot`(자리) `맨 아래 줄`). "옆에 아무도 없을 때 공격력 2배" = 사용 AND `자리 검사`(`spot`(자리) `양옆이 빈 칸`).
- 옆 칸·같은 줄의 개체를 **고르는** 것은 대상 Block `자리로 고르기`다(12장 2-4).
- 검증 오류: "자리 검사: 목록(플레이어 데이터)을 고르세요." / "자리 검사: 이벤트 필드를 적으세요." / "자리 검사: 누구는 사용자나 이벤트의 개체만 됩니다."

### 2-8. 조건 `사용자 필드 검사`

`+ 블럭` → **조건 충족 · 조건** → `사용자 필드 검사`. 문장 `사용자의 {path} {op} {value}`. 사용자의 필드를 비교한다: 조각이 쌓은 카운터, 아티팩트의 처치 수, 쓰는 쪽의 마나. `필드 검사`(이벤트 조건)는 이벤트·행동 직전 이벤트를, `플레이어 데이터 검사`는 고정 경로를 보는데, 이것은 **지금 순간의 사용자**를 본다.

| 칸 | 툴팁 | 값 |
|---|---|---|
| `path` | 필드(사용자의) | 사용자 클래스의 필드 경로를 글자로 적는다(`counter`, `stats.kills`, 경로 문법 3-1) |
| `op` | 비교 | `FieldOp`(이상 `AtLeast` · 이하 · 초과 · 미만 · 일치 · 아님 · 포함 …, 3-2). 기본 `AtLeast` |
| `value` | 값(틀 가능) | 비교할 값. 값 틀(`{user:경로}` · `{event:경로}` · `{data:키.경로}` · `{count:키.경로}` · `{level}` · `{balance:ID}` …, 12장 2-4a)을 쓰면 검사하는 순간의 값으로 바뀐다 |

- **사용자**는 컨텍스트의 `User`다: 액티브 효과·사용 기능이면 쓰는 쪽, 상태 효과·패시브 효과면 가진 쪽, '놓인 것마다' 패시브 효과면 그 조각. 사용자가 없는 곳(도전과제·로그의 조건 충족)에서는 거짓이다.
- 필드에 저장된 값을 본다. 스탯 보정(Modifier)은 더하지 않는다(보정까지 본 값은 프로젝트 조건에서 `Game.Attribute`).
- 패시브 효과의 `rules`(발동 더)와 함께 쓰면 쌓았다가 쓰는 카운터가 코드 없이 된다. Loyalty Card = 본 발동 `손 냄` → `counter` +1, 줄 1 `점수` 행동 직전 이벤트 AND `사용자 필드 검사`(`counter` 이상 `6`) → ×4 · `counter` 값으로 바꾸기 0(17장 0-3).
- 값을 바꾸지 않으므로 사용 조건(액티브 효과의 "마나 3 이상" = `mana` 이상 `3`)·패시브 효과의 `modifierWhen`(보정 조건)에도 둔다.
- 요약: `사용자의 counter 6 이상`. 검증 오류: "사용자 필드 검사: 필드를 적으세요."

---

## 3. 값 조건과 필드 경로

### 3-1. 필드 경로 문법

| 경로 | 뜻 |
|---|---|
| `level` | 필드 하나 |
| `weapon.power` | 중첩 클래스의 필드 |
| `tags` | 컬렉션 자체(포함·개수·비어 있음 검사) |
| `items[].level` | 컬렉션의 각 요소의 필드 |
| `materials[book]` | 딕셔너리의 키 하나(`book` 키의 값) |
| `units[hero].hp` | 목록에서 `id`가 `hero`인 요소 하나의 필드(글자 목록이면 그 글자와 같은 요소) |
| (빈 경로) | 뿌리 값 자체(예: 숫자 플레이어 데이터) |

- `[]`(빈 괄호)는 요소마다 펼치고, `[키]`는 하나를 집는다. 딕셔너리를 `[]`로 펼치면 **값들**이 요소다.
- **`name[키]`의 규칙**
  - 딕셔너리면 그 키의 값이다. 키는 글자로 적는다(키 형식이 숫자·enum이면 그 값으로 읽는다). **없는 키는 비어 있음**이다: 비교(이상·일치 …)는 거짓, `비어 있음`은 참.
  - 목록이면 `id`가 그 키인 첫 요소다(`id`가 있는 객체 목록). 글자 목록이면 그 글자와 같은 요소다(`relics[anchor]` = 있으면 그 글자, 없으면 비어 있음 → "가지고 있나"를 `비어 있지 않음`으로).
  - `[ ]` 안의 점은 마디를 나누지 않는다: `flags[forest.chest3]`은 키 `forest.chest3` 하나다.
  - **쓰는 곳**에서는 마지막 마디의 `[키]`가 딕셔너리일 때만 쓴다(없던 키면 생긴다). 목록의 요소는 그 요소의 필드까지 적는다(`units[hero].hp`). 쓰는 곳: `Game.Pay`·결과 `비용 내기`(28장 2-4), 결과 `플레이어 데이터에 주기`(숫자 증감·값, 12장 2-2).
  - **읽는 곳**: 조건(`필드 검사`·`플레이어 데이터 검사`), 화면의 binding-path(35장), 가중치 표의 뺄 목록·단계 필드(30장), 대상 Block의 필드(16장 2-6) 등 필드 경로를 받는 곳 모두.
  - DB의 필드 고르기 창(탐색기)은 `[]`(각 항목)까지만 고르고 `[키]` 마디는 만들지 않는다. `[키]`는 코드(`FieldFilter.path`, `PlayerDataReward.path`, `Game.Pay`)와 binding-path에 글자로 적는다.
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

```csharp
// 딕셔너리의 키 하나(Party에 Dictionary<string, int> materials가 있을 때): book이 3개 이상
var books = new PlayerDataCondition { key = "party", filter = new FieldFilter { path = "materials[book]", op = FieldOp.AtLeast, value = "3" } };
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
   - `Description`: 목록·툴팁 설명. 꼭 적는다(개발자가 목록에서 읽는다 — 관례 2절. 아래 짧은 예제는 줄였다).
   - `Sentence`: 편집 화면의 한 줄 문장. `{필드이름}`이 누르면 바뀌는 칸이 된다. 예: `Sentence = "{team} 팀 전멸"`. 개발자가 바꿀 값(기준 수·이름)은 필드로 두고 문장에 드러낸다(관례 18절).
3. **`: SimpleCondition`** — 기반 클래스.
4. **public 필드** — 에디터 칸이 된다(항목마다 값이 다르다). `[FieldLabel("설명")]`을 붙이면 마우스를 올렸을 때 그 글이 보인다.
5. **`protected override bool Check()`** — 판정. 값을 바꾸지 않는다.
6. **`Game`** — 지금 세션의 `GameApi`(`All`·`Find`·`Get` …).
7. **`Context`** — 지금 검사 중인 순간(`TriggerContext`). Trigger 밖에서 평가되면 `null`이다.

무엇을 보는지 선언하지 않는다. 평가할 때 읽은 플레이어 데이터를 시스템이 기록해 두고, 그 값이 바뀌면 다시 평가한다(4-4).

### 4-2. `Context`로 순간을 읽는다

```csharp
// 사용 조건: 사용자가 살아 있을 때만(액티브 효과·사용 기능의 트리거에 AND로).
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
| `Owner` | 이 Trigger를 가진 항목의 ID(로그·도전과제·액티브 효과 …) |
| `Event` | 이벤트를 받은 순간이면 그 이벤트, 행동 직전 이벤트 순간이면 그 행동 직전 이벤트, 아니면 `null` |
| `IsIntercept` | 행동 직전 이벤트 순간이면 `true`(→ `28-Attributes-Intercepts.md`) |
| `IsUse` | 사용하는 순간이면 `true` |
| `IsCheck` | '쓸 수 있나'만 미리 보는 순간(`Game.CanUse`)이면 `true`. 조건 `확률`은 이때 굴리지 않고 참이다(2-6). 난수를 쓰는 프로젝트 조건도 이 값을 보고 미리 보기에서는 굴리지 않게 쓴다 |
| `IsBorrowed` | 다른 조각의 패시브 효과를 빌려 쓰는 순간이면 `true`(12장 2-4e). 이때 `User`는 빌려 준 원본 조각 |
| `User` | 사용자(사용하는 순간). 상태 효과·패시브 효과의 트리거에서는 검사 중인 가진 쪽('놓인 것마다'면 그 조각) |
| `Picked` | 플레이어가 고른 대상들(사용하는 순간, 없으면 빈 리스트). 가진 쪽마다 검사할 때는 그 가진 쪽 하나 |
| `Power` | 효과 배율(행동 직전 이벤트 `효과 배율`을 거친 값, 기본 1). 기본 효과 Block은 수치에 곱하고, 프로젝트 효과 Block은 직접 곱한다(16장 2-12) |

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
4. 평가할 때마다 읽은 키를 다시 모은다(조건 안의 분기가 바뀌어도 따라간다). 평가하다 던진 조건도 그때까지 읽은 키로 감시한다(오류는 Journal에 "조건 평가 오류 (…)" — 그 값이 바뀌면 다시 평가한다).
5. 조건 충족 → 결과 → 다른 값 변화 → 다른 조건 충족으로 이어지면 더 바뀌지 않을 때까지 반복한다. 16회를 넘으면 "조건 연쇄 평가가 16회를 넘었습니다. 순환 참조를 확인하세요." 경고를 남기고 멈춘다.
6. `PollInterval`이 있는 조건은 그 간격마다 다시 평가한다.
7. **새 키가 정의되면** 읽은 값으로 감시하는 조건(`SimpleCondition`·조건 충족 잎)과 그 키를 선언한 조건을 다음 Flush에서 다시 평가한다. 처음 쓸 때 정의되는 라이브러리 키(`$statuses`·`$world`·`$modifiers`·`$schedule` …)와 나중에 정의한 플레이어 데이터는 정의되기 전에는 읽을 수 없어(정의를 보고 건너뛰거나 읽다가 던진다) 읽은 키에 없기 때문이다. 그래서 "보스에게 독이 걸려 있으면"(`Game.HasStatus`) · "상자를 열었으면"(`Game.WorldFlag`) 같은 조건도 처음 거는·처음 여는 행동 뒤에 평가된다.
8. **행동 밖에서 발동한 결과가 던지면**(행동이 끝난 뒤·프레임 끝의 Flush) Journal에 오류 "조건 충족 처리 오류 (…): …"를 남기고 남은 조건을 계속 평가한다. 그 행동은 이미 성공했으므로 `Game.Do`는 던지지 않는다. 반복하는 Trigger는 그 뒤에도 감시한다(다시 거짓 → 참이면 또 발동). 행동 안에서 Flush했다면 그 행동이 실패한다.

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
| `[TriggerSlot(TriggerKinds)]` | `Event`·`Condition`·`Use`·`Intercept`(행동 직전 이벤트)·`All`을 `\|`로 | `+ 블럭` 목록과 검증이 이 종류만 허락 |
| `[OutcomeSlot(OutcomeKinds)]` | `Record`·`PlayerData`·`Coded`·`Targeted`·`Event`·`Intent`(행동 직전 이벤트 고치기)·`All` | `+ 결과 추가` 목록과 검증이 이 종류만 허락 |
| `Fixed = true` | (OutcomeSlot) | 결과가 그 종류 하나로 고정. 비어 있으면 하나를 자동으로 두고 세부 칸만 고친다(추가·삭제 없음) |
| `BindTo = false` | (OutcomeSlot, 기본 참) | 이 항목 ID에 `[BindTo]`가 있어도 코드가 이 칸을 대신하지 않는다(13장 2-11) |

- `Trigger` 필드, `List<Outcome>` 필드에 직접 붙이거나, `List<TriggerRule>` 필드에 붙이면 규칙마다의 `when`·`then`에 적용된다.
- `Trigger`·`Outcome`을 담는 필드에는 `[SerializeReference]`가 필요하다(`TriggerRule`의 `when`·`then`은 이미 붙어 있다).
- 표시가 없는 칸은 전부 허락한다.
- 검증(DB 저장·빌드 때): 잠금 밖의 종류 "이 칸에서는 '…' 트리거를 쓸 수 없습니다", 고정 칸의 개수, 값을 관찰하지 않는 조건, 플레이어 데이터 검사·주기의 경로를 모든 기능에서 같은 규칙으로 본다.

---

## 6. 실행 규칙(`session.Triggers`)

### 6-1. 같은 이벤트에 여러 Trigger가 걸렸을 때의 순서

조건 충족 잎이 **없는** Trigger(로그 기록 등)를 먼저, 조건이 **있는** Trigger(도전과제 등)를 나중에 검사한다. 정의한 순서와 상관없다. 그래서 `유닛 처치 받기 AND 처치 수 >= 10`은 같은 처치로 막 오른 처치 수를 본다(10번째 처치 순간에 발동).

두 묶음 안에서는 **등록한 순서**다(게임이 켜질 때 DB 항목을 등록한 순서, 코드면 `Register`를 부른 순서). 패시브 효과의 `rules`(발동 더) 줄은 본 발동 다음에 위 줄부터 등록된다. 그래서 같은 이벤트에 걸린 줄은 조건이 없는 줄 → 조건이 있는 줄, 같은 묶음 안에서는 위 줄부터다(17장 0-1).

**예외 — '놓인 것마다' 패시브 효과는 목록 순서다.** 같은 목록(같은 `ownerKey`·`ownerPath`(가진 목록))을 보는 '놓인 것마다'(`perElement`) 패시브 효과들(17장 0절)은 패시브 효과의 등록 순서가 아니라 **목록 순서(자리)대로 섞여** 발동한다. 조커 칸이면 왼쪽 칸부터다. "+4 배수" 조커가 왼쪽, "×2 배수" 조커가 오른쪽이면 (1 + 4) × 2 = 10, 자리를 바꾸면 1 × 2 + 4 = 6이다.

- 그 묶음은 위 순서에서 묶음의 첫 패시브 효과가 올 차례에 한꺼번에 돈다. 묶음 안에 조건 충족 잎이 있는 패시브 효과가 섞여 있어도 함께 돈다.
- 발동 도중에 앞 칸의 결과가 목록에서 뺀 요소는 발동하지 않는다(그 순간 목록에 남은 것만).
- 같은 조각이 둘이면 각자 발동한다.
- 행동 직전 이벤트는 같은 `order`(순서) 칸 안에서 목록 순서다. `order`가 다르면 `order`가 먼저다.

### 6-2. 사슬 한도

결과가 이벤트를 쏘고, 그 이벤트가 다시 Trigger를 발동하는 사슬은 깊이 16(`TriggerSystem.MaxDepth`)에서 끊긴다. Journal에 "트리거 사슬이 16단계를 넘어 끊었습니다(…). 결과가 자기 트리거의 이벤트를 다시 쏘는지 확인하세요." 경고가 남는다. 예외는 던지지 않는다. 행동 직전 이벤트 사슬도 같은 깊이를 센다(→ `28-Attributes-Intercepts.md` 2-3).

### 6-3. 진행도

`Trigger.TryGetProgress(session, out current, out target)`은 조건 잎의 진행도를 모은다: `AND`는 가장 덜 된 조건, `OR`는 가장 많이 된 조건. 이벤트·사용 잎은 진행도가 없다. `비교`는 `>=`·`>`이고 기준값이 0보다 클 때만 진행도를 낸다.

### 6-4. `TriggerSystem` API

| 멤버 | 뜻 |
|---|---|
| `Register(string owner, Trigger when, bool repeat, Action<TriggerContext> fire, Func<TriggerContext, IEnumerable<object>> holders = null, Func<TriggerContext, IEnumerable<(object holder, int order)>> orderedHolders = null, string orderGroup = null, string item = null)` | 감시를 시작한다. 발동하면 `fire`(결과 실행은 부르는 쪽이 `RunOutcomes`로). `repeat`가 아니면 한 번 발동한 뒤 감시를 그만둔다(행동 안에서 발동했으면 그 행동이 성공할 때 — 1절). `when`이 `null`이면 아무것도 하지 않는다. `item`: 컨텍스트의 `Owner`로 보일 항목 ID(감시 키 `owner`에 종류를 붙였을 때 — 패시브 효과 `passive:<ID>` → `<ID>`, 비우면 `owner`). `holders`를 주면 순간마다 그 목록의 객체마다 컨텍스트의 `User`를 그 객체로 두고 트리거 전체(조건 포함)를 따로 검사해, 참인 객체마다 `fire`를 부른다(상태 효과의 가진 쪽마다 검사, `29-Statuses-Schedules-Choices.md` 2-3). `orderedHolders`(가진 쪽 · 자리)와 `orderGroup`(묶음 이름)을 주면 같은 묶음의 항목끼리 가진 쪽을 모아 자리가 작은 것부터(같으면 등록 순서) 발동한다(6-1의 '놓인 것마다') |
| `Unregister(string owner)` / `IsRegistered(owner)` | 그 owner의 감시를 모두 그만둔다 / 감시 중인지 |
| `Touch(string owner)` | 그 owner의 조건 감시를 다음 Flush에서 다시 평가하게 한다(불러온 뒤 새로 감시를 시작한 것 — 이미 참이면 그때 발동) |
| `RunOutcomes(IEnumerable<Outcome>, TriggerContext, string source)` | 결과를 차례로 실행한다. `source`는 Journal의 출처 글자 |
| `CanUse(owner, when, user, picked, out TriggerContext, bool checkOnly = false)` | 사용하는 순간으로 검사. `checkOnly`면 미리 보기 순간(`IsCheck`)이라 조건 `확률`을 굴리지 않는다(`Game.CanUse`가 이것). 아니면 확률을 굴린다(`Game.Use`·`TryUse`) |
| `Test(Trigger, TriggerContext)` | 그 순간으로 검사 |
| `Intercept<T>(T intent)` | 행동 직전 이벤트를 행동 직전 이벤트 잎들에 순서대로 통과시키고 돌려준다(= `Game.Intercept`, → `28-Attributes-Intercepts.md` 4-8) |
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

적 전멸은 4-1의 `AllEnemiesDown`이다. 칸 하나를 문장에 드러낸 조건:

```csharp
[Serializable, BlockInfo("아군 하수인 수", Category = "전투", Description = "살아 있는 아군 하수인 수를 봅니다.", Sentence = "아군 하수인 {count}명 이상")]
public class AllyCountAtLeast : SimpleCondition
{
    public int count = 3;   // DB에서 항목마다 바꾸는 칸
    protected override bool Check() => Game.All<Unit>().Count(u => u.team == "ally" && u.hp > 0) >= count;
}
```

`Unit`이 든 플레이어 데이터가 바뀌는 행동이 끝날 때마다 자동으로 다시 평가된다(4-4).

### 7-7. 사용 조건(액티브 효과·아이템)

```csharp
[Serializable, BlockInfo("사용자 체력 비율", Category = "사용", Sentence = "사용자 체력 {percent}% 이하")]
public class UserHpBelow : SimpleCondition
{
    public int percent = 50;
    protected override bool Check() => Context?.User is Unit u && u.hp * 100 <= u.maxHp * percent;
}

// 액티브 효과의 트리거: 사용 AND 사용자 체력 50% 이하
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

- 숫자 비교(일치·이상·이하·초과·미만)는 아주 작은 차이(1e-9, 1000을 넘는 수는 그 크기의 1e-12배)를 같다고 본다(`FieldOps.Near`) — 0.1을 열 번 더한 0.9999999999999999도 '일치 1'이고 '이상 1'이다(같은데 이상은 아닌 모순이 없다). 비용(`Game.Pay`·`CanPay`, 28장)도 같다.
- float 필드(`0.1f`)는 적힌 소수 그대로 비교한다 — '일치 0.1'·'이하 0.1'이 참이다(double로 바뀐 0.10000000149…로 보지 않는다).
- 경로의 `[키]`는 읽을 때도 쓸 때처럼 키 형식으로 바꿔 찾는다(`flags[true]`·`byLevel[01]`이 같은 칸 — bool·int 키).

- **이벤트 둘을 AND로 묶는다.** "보스 처치 받기 AND 골드 획득 받기"는 영원히 발동하지 않는다. 둘 다 일어난 뒤를 원하면 각각을 로그로 세고 `비교` 조건 둘을 AND로 묶는다.
- **조건 Block에서 값을 바꾼다.** 조건은 읽기만 한다. 평가 횟수·순서는 보장되지 않는다(등록 때 한 번 미리 평가하고, 값이 바뀔 때마다 평가한다).
- **조건이 플레이어 데이터 밖을 읽는다.** MonoBehaviour 필드, static 변수, `UnityEngine.Time`은 감시되지 않는다. 바뀌어도 다시 평가되지 않는다. 감시할 값은 플레이어 데이터에 두거나 `PollInterval`을 쓴다.
- **`Context?.Event`를 혼자 쓴다.** 값이 바뀌어 평가되는 순간에는 `Event`가 `null`이다. 이벤트를 보는 조건은 이벤트 받기와 AND로 묶는다.
- **이벤트 형식이 아니라 상위 형식을 고른다.** 이벤트 받기는 형식이 정확히 같아야 한다.
- **Unity 밖에서 Flush를 잊는다.** 서버·테스트에서 `Data.Set`으로 값을 바꾼 뒤 `session.Triggers.Flush()`를 부르지 않으면 조건 충족 잎이 발동하지 않는다.
- **`NOT`으로 이벤트를 거른다.** "보스가 아닌 처치"는 `NOT(이벤트 받기)`가 아니라 이벤트 받기의 조건 칸에 `IsBoss 거짓`(또는 `tags` 미포함 `boss`)으로 쓴다.
- **필드 경로가 깨진다.** 이벤트·플레이어 데이터 클래스의 필드 이름을 바꾸면 경로 글자는 따라가지 않는다. DB 검증 오류("'…'에 '…' 필드가 없습니다.")가 뜬 칸을 다시 고른다.
- **`[키]`를 컬렉션이 아닌 필드에 쓴다.** 검증 오류 "'gold'는 컬렉션이 아니라 [x]를 쓸 수 없습니다."가 뜬다(`gold[x]`). `[키]`는 딕셔너리·목록 필드에만.
- **`확률`만으로 Trigger를 만든다.** 조건 충족만으로 된 순간에는 거짓이라 발동하지 않는다(2-6).
- **`확률`을 사용 조건에 두고 `Game.Use`를 부른다.** 굴려서 거짓이면 실패를 던져 그 행동이 취소된다. 되는 것만 쓰려면 `Game.TryUse`(2-6).
- **`Game.CanUse`가 참인데 썼더니 안 됨.** 미리 보기는 `확률`을 굴리지 않고 참으로 본다(2-6).
- **`자리 검사`의 `not`(아닐 때 참)이 목록에 없을 때도 참일 거라 기대한다.** 누구를 목록에서 찾지 못하면 늘 거짓이다(2-7).
- **'놓인 것마다' 패시브 효과가 등록 순서로 돌 거라 기대한다.** 같은 목록을 보는 것끼리는 목록 순서(왼쪽 칸부터)다(6-1).
- **없는 키를 "0"으로 기대한다.** 읽기(조건)에서 없는 키는 0이 아니라 비어 있음이라 `materials[book] 이하 0`은 거짓이다. "없거나 0"은 `비어 있음` OR `이하 0`으로 쓴다. 쓰는 쪽(`Game.Pay`·`플레이어 데이터에 주기`)은 없는 키를 0에서 시작한다.
