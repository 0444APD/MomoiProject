# 37. 자리(Placement)

**한 줄 요약**: 조각이 칸에 놓이는 게임(조커 칸·지팡이 칸·손패·가방·보드)은 판 데이터의 **목록 하나**(`List<T>`)를 칸으로 본다. 감싸는 형식은 없다. 칸 번호 = 목록 위치이고, 빈 칸 = `null`이다. 한 줄이면 줄 길이 0, 격자면 줄 길이 = 열 수다. `Placement`의 함수가 옆 칸·같은 줄·주변 8칸·맨 앞·맞은편과 가장자리·맨 아래 줄·양옆이 빈 칸 같은 자리를 계산한다. DB에서는 대상 **'자리로 고르기'**와 조건 **'자리 검사'**로 코드 없이 쓴다. 플레이어·지팡이마다 둔 칸(`players[].charms`·`wands[].slots`)은 기준이 든 목록에서 본다. 놓인 순서대로 발동하는 조각은 패시브의 '놓인 것마다'(`perElement`, 17장)가, 옆 조각을 흉내 내는 조각은 결과 '빌려 쓰기'가 맡는다. 석판처럼 칸 무늬를 돌려 덮는 계산은 `Placement.Pattern`이다. 개체가 칸 위를 걸어 다니는 맵은 36장 `Grid`다.

**언제 쓰는가**

- 덱빌딩: 조커 칸 왼쪽부터 발동, "오른쪽 조커를 복사", "맨 왼쪽 조커를 복사"(발라트로).
- 보드 전투: 레인 2개 × 3칸 양편 보드, 맞은편 같은 레인 맨 앞을 공격, 적 먼저 앞 칸 위→아래 → 다음 칸 차례(와일드프로스트, 열 우선 보드).
- 주문 칸: 지팡이마다 칸 한 줄, 왼쪽부터 읽기, 다음 칸 주문을 한 번 더(매지크래프트).
- 가방 격자: "맨 아래 줄일 때만", "가장자리면", "양옆이 비면", 석판 무늬로 주변 칸 레벨 올리기, 석판 돌리기, 제약이 안 맞으면 꺼짐(세피리아).
- 손패: 맨 오른쪽 카드, 이웃한 카드.
- 협동: 플레이어마다 둔 참 칸에서 "내 줄의 오른쪽".

이 문서에서 쓰는 용어:

| 용어 | 뜻 |
|---|---|
| 자리 | 조각이 놓이는 칸들. 판 데이터의 목록 하나(`List<T>`)다. |
| 조각 | 칸에 놓이는 것(조커·유닛·아티팩트·석판·주문). 목록의 요소다. |
| 칸 번호 | 목록 위치(0부터). |
| 빈 칸 | 요소가 `null`인 칸. 칸은 남고 아무것도 없다. |
| 줄 길이 (`columns`) | 한 줄의 칸 수. 0이면 한 줄(조커 칸·손패), 6이면 0~5번이 첫 줄인 격자(가방). |
| 열 우선 보드 | 목록 순서가 "앞 칸 위→아래, 다음 칸"인 보드(와일드프로스트). 줄 길이 = 레인 수로 담으면 원작의 레인이 여기의 **열**이다(5-3). |
| 여러 목록 (`[]`) | 목록 경로의 `[]`(`players[].charms`). 플레이어·지팡이마다 칸 한 줄씩이다. 자리는 기준이 든 목록에서 센다(2-4). |
| 좌표 (`Cell`) | 칸의 (x, y). x = 열(오른쪽 +), y = 줄(아래 +). 화면 기준이다. "위"는 앞 줄이다. `Cell`은 36장의 칸 구조체를 그대로 쓴다. |
| 원본 ID 필드 | 조각 객체가 무엇인지 적는 필드(`joker`·`item`). 패시브의 '일치 필드'(`matchField`)가 이것을 본다(17장). |
| 기준 | 자리를 셀 때 출발하는 조각(사용자·앞의 대상·이벤트의 개체). |
| 방향 (`PlaceDirection`) | 기준에서 어느 칸을 고를지(왼쪽·같은 줄·주변 8칸·맨 앞 …). |
| 자리 (`PlaceSpot`) | 기준이 어떤 칸에 있는지(맨 위 줄·가장자리·양옆이 빈 칸 …). |
| 맞은편 | 다른 목록(적 보드)에서 기준과 같은 줄·같은 칸. 두 목록의 줄 길이가 같다. |
| 무늬 | 기준 칸에서 본 상대 좌표 목록(석판이 덮는 칸). |
| 회전 수 | 무늬를 시계 방향으로 90°씩 돌린 횟수. 4는 0과 같다. |

---

## 1. 무엇으로 만드나

| 기획 문장 | 만드는 것 |
|---|---|
| "조커는 왼쪽부터 발동한다(+4 배수 다음 ×2 배수)" | 패시브 '놓인 것마다'(17장) + 조각 목록 = 객체 목록(5-1) |
| "라운드 끝에 오른쪽 조커의 배수 +2" | 대상 '자리로 고르기'(오른쪽) + 효과 '필드 바꾸기'(5-1) |
| "오른쪽 조커를 없앤다(단검)" | `[BindTo]` + `Placement.Around`(5-1) |
| "오른쪽 조커의 능력을 복사(Blueprint)" · "맨 왼쪽 조커를 복사(Brainstorm)" | 결과 '빌려 쓰기' + '자리로 고르기'(오른쪽 · 목록 맨 처음)(5-2) |
| "맞은편 같은 레인 맨 앞의 적을 공격" | 보드를 열 우선으로 담고 '자리로 고르기'(같은 열 맨 앞, 다른 목록 = 적 보드)(5-3) |
| "적 먼저, 앞 칸 위→아래 → 다음 칸, 카운터가 0이면 발동" | 턴 행동이 칸 번호 순서로 돌며 `Game.TryUse`(5-3) |
| "같은 레인에 혼자면 공격 +3"(Fury) | 패시브 보정 + `modifierWhen`(보정 조건) '자리 검사'(같은 열에 혼자)(5-3) |
| "가방 맨 아래 줄일 때만" · "가장자리면" · "양옆이 빈 칸이면" · "손패 맨 오른쪽이면" | 조건 '자리 검사'(5-4) |
| "손패 맨 오른쪽 카드에"(누가 쓰든) | '자리로 고르기' 기준 없음 + 목록 맨 끝(2-2) |
| "주변 8칸의 아티팩트마다" | '자리로 고르기'(주변 8칸), 코드 `Placement.Around` |
| "석판: 무늬 칸의 레벨 +1, 돌릴 수 있다" · "제약이 안 맞으면 꺼진다" | 배치 행동 끝의 계산 함수가 조각의 레벨 필드에 최종 레벨(꺼지면 0) + `Placement.Pattern`(5-5) |
| "지팡이마다 칸 한 줄, 다음 칸 주문을 한 번 더" | 목록 `wands[].slots` + 패시브 '놓인 것마다' + '빌려 쓰기'(5-6) |
| "협동: 내 참 줄의 오른쪽 참" | 목록 `players[].charms`(2-4) |

> **규칙: 자리는 판 데이터의 목록 하나.** 칸 수만큼 요소를 두고 빈 칸은 `null`로 둔다(가방 34칸 = 요소 34개). 감싸는 형식·칸 객체를 따로 만들지 않는다. 평범한 목록이라 Undo·저장·Mirror가 그대로 따라온다. 양편 보드는 편마다 목록 하나다.

> **규칙: 조각 목록은 객체 목록.** 객체마다 고유 `id`와 원본 ID 필드(`joker`)를 둔다. 그래야 같은 조커를 둘 가질 수 있고, 조각마다 쌓이는 값(`mult`)을 둘 수 있다. ID 글자 목록(`List<string>`)도 계산은 되지만, 같은 ID가 둘이면 둘째도 첫째의 자리로 센다(2-4).

> **규칙: 걷는 맵은 `Grid`, 놓는 칸은 `Placement`.** 개체가 칸 좌표(`x`·`y`)를 들고 움직이면 36장 `Grid`다. 목록 위치가 곧 칸이면 `Placement`다.

| | `Grid`(36장) | `Placement`(이 장) |
|---|---|---|
| 데이터 | 개체의 `x`·`y`, 칸 키 딕셔너리 | 목록 하나(칸 번호 = 위치) |
| 맵의 끝 | 모른다(비용 함수가 거른다) | 목록 길이(밖은 저절로 빠진다) |
| y 방향 | 위가 +(`Grid.Up` = (0, 1)) | 아래가 +(화면 기준, 위 = 앞 줄) |
| 회전 | `Grid.Rotate` | `Placement.Rotate` |
| 맞는 게임 | SRPG·로그라이크 던전·농장 | 조커 칸·손패·가방·보드 |

---

## 2. 동작 규칙

### 2-1. 칸 번호와 좌표

- 칸 번호 = 목록 위치다. 줄(y) = 번호 ÷ 열 수의 몫, 열(x) = 나머지다.
- 줄 길이 0이면 한 줄이다. x = 칸 번호, y = 0이다.
- 좌표는 화면 기준이다. x는 오른쪽으로, y는 아래로 늘어난다. "위"는 앞 줄(y가 작은 쪽)이다.
- **마지막 줄은 덜 차도 된다.** 34칸 가방(6열)은 6칸 × 5줄 + 4칸이다. 마지막 줄의 x = 4·5는 목록 밖이다.
- 목록 밖 칸은 어떤 계산에도 나오지 않는다. 맵 밖을 따로 거를 필요가 없다.
- 빈 칸(`null`)은 칸으로 남는다. 뒤 칸 번호가 밀리지 않는다.

```
줄 길이 6, 34칸
       x=0  x=1  x=2  x=3  x=4  x=5
y=0      0    1    2    3    4    5     ← 맨 위 줄(앞 줄)
y=1      6    7    8    9   10   11
y=2     12   13   14   15   16   17
y=3     18   19   20   21   22   23
y=4     24   25   26   27   28   29
y=5     30   31   32   33               ← 맨 아래 줄(덜 찬 마지막 줄)
```

### 2-2. 방향 — `PlaceDirection`

| 화면 이름 | 코드 | 고르는 칸 |
|---|---|---|
| 왼쪽 · 오른쪽 | `Left` · `Right` | 같은 줄에서 거리만큼 떨어진 한 칸 |
| 위(앞 줄) · 아래(뒤 줄) | `Up` · `Down` | 같은 열에서 거리만큼 떨어진 한 칸. 한 줄이면 없다 |
| 상하좌우 | `Around4` | 붙은 4칸(위 · 오른쪽 · 아래 · 왼쪽 차례) |
| 주변 8칸 | `Around8` | 대각선까지 8칸(왼쪽 위부터 줄마다 왼쪽에서 오른쪽) |
| 같은 줄 | `SameRow` | 같은 줄의 다른 칸 모두(왼쪽부터) |
| 같은 열 | `SameColumn` | 같은 열의 다른 칸 모두(위부터) |
| 같은 줄 맨 앞(차 있는 첫 칸) | `RowFirst` | 같은 줄에서 왼쪽부터 처음 차 있는 칸 |
| 같은 줄 맨 뒤(차 있는 끝 칸) | `RowLast` | 같은 줄에서 오른쪽부터 처음 차 있는 칸 |
| 같은 열 맨 앞(차 있는 첫 칸, 위부터) | `ColumnFirst` | 같은 열에서 위부터 처음 차 있는 칸(열 우선 보드의 "같은 레인 맨 앞") |
| 같은 열 맨 뒤(차 있는 끝 칸) | `ColumnLast` | 같은 열에서 아래부터 처음 차 있는 칸 |
| 목록 맨 처음(차 있는 첫 칸) | `First` | 목록 전체에서 처음 차 있는 칸 |
| 목록 맨 끝(차 있는 끝 칸) | `Last` | 목록 전체에서 마지막 차 있는 칸 |
| 같은 칸(다른 목록의 맞은편) | `Same` | 기준과 같은 칸 번호. 다른 목록에서 고를 때 쓴다 |
| 나를 뺀 모두 | `Others` | 기준을 뺀 모든 칸 |

- `distance`(거리)는 왼쪽·오른쪽·위·아래에만 쓴다. 거리 2는 두 칸 떨어진 칸 **하나**다(사이 칸은 아니다). 1보다 작으면 1로 본다.
- 방향은 줄을 넘지 않는다. 줄 끝의 오른쪽은 다음 줄 첫 칸이 아니라 "없음"이다.
- **같은 목록에서 고르면 기준 자신은 빠진다.** 그래서 기준이 그 줄의 맨 앞이면 '같은 줄 맨 앞'은 아무도 고르지 않는다(다음 칸을 고르지 않는다). '같은 열 맨 앞·맨 뒤'·'목록 맨 처음'·'목록 맨 끝'도 같다.
- '자리로 고르기'와 `Placement.Around`는 빈 칸을 뺀다. `Placement.Indices`는 빈 칸 번호도 준다.
- `other`(다른 목록에서 고르기, 맞은편): 기준의 칸 번호·좌표를 그대로 다른 목록에 대고 고른다. '같은 열 맨 앞'이면 맞은편 같은 열의 처음 차 있는 칸, '같은 칸'이면 바로 맞은편 칸이다. 같은 목록일 때와 달리 기준과 같은 칸 번호를 빼지 않는다: '같은 줄'·'같은 열'·'나를 뺀 모두'에 바로 맞은편 칸도 들어간다.
- **기준 없음**('자리로 고르기'의 `origin` `없음`): 목록 전체를 본다. 방향은 '목록 맨 처음'·'목록 맨 끝'·'나를 뺀 모두'(= 차 있는 칸 모두)만 되고, 나머지는 검증 오류다. "손패 맨 오른쪽 카드"·"맨 앞 적"을 누가 쓰든 고를 때 쓴다. 여러 목록(`[]`)이면 첫 목록이다.

### 2-3. 자리 — `PlaceSpot`

| 화면 이름 | 코드 | 참일 때 |
|---|---|---|
| 맨 위 줄 | `TopRow` | 첫 줄(y = 0) |
| 맨 아래 줄 | `BottomRow` | 목록의 마지막 줄(덜 찬 줄이면 그 줄) |
| 맨 왼쪽 열 | `LeftColumn` | x = 0 |
| 맨 오른쪽 열 | `RightColumn` | x = 열 수 − 1. 한 줄이면 목록의 마지막 칸(비어 있어도) |
| 가장자리(상하좌우 중 바깥이 있음) | `Edge` | 붙은 4칸 중 하나라도 목록 밖. 한 줄이면 모든 칸(위아래가 없다) |
| 안쪽(가장자리가 아님) | `Inner` | 붙은 4칸이 모두 목록 안 |
| 목록 맨 처음(차 있는 것 중) | `First` | 차 있는 칸 중 첫 칸 |
| 목록 맨 끝(차 있는 것 중) | `Last` | 차 있는 칸 중 끝 칸 |
| 같은 줄 맨 앞(차 있는 것 중) | `RowFirst` | 그 줄의 차 있는 칸 중 첫 칸 |
| 양옆이 빈 칸 | `SidesEmpty` | 왼쪽·오른쪽 칸이 비었거나 목록 밖 |
| 같은 줄에 혼자 | `RowAlone` | 같은 줄의 다른 칸이 모두 비었다 |
| n번째 칸(0부터) | `Index` | 칸 번호 = n |
| 같은 열 맨 앞(차 있는 것 중, 위부터) | `ColumnFirst` | 그 열의 차 있는 칸 중 위의 첫 칸(열 우선 보드의 "레인 맨 앞") |
| 같은 열에 혼자 | `ColumnAlone` | 같은 열의 다른 칸이 모두 비었다(열 우선 보드의 "레인에 혼자") |

- 덜 찬 마지막 줄도 센다. 34칸 가방에서 28번(x4, y4)은 아래 칸이 목록 밖이라 가장자리다. 27번(x3, y4)은 아래에 33번이 있어 안쪽이다. 28번은 맨 아래 줄은 아니다(맨 아래 줄은 30~33번).
- 빈 칸과 목록 밖은 같게 본다('양옆이 빈 칸').
- 손패 "맨 오른쪽 카드"는 '목록 맨 끝(차 있는 것 중)'이다. '맨 오른쪽 열'은 한 줄이면 칸이 비어 있어도 마지막 칸이다.
- 빈 칸 번호를 직접 물으면(`Placement.Is(목록, 칸 번호, …)`) '목록 맨 처음'·'목록 맨 끝'·'같은 줄 맨 앞'·'같은 열 맨 앞'은 거짓이다. 나머지 자리는 칸 모양만 본다.

### 2-4. 조각 찾기 — 같은 객체 · 같은 id · id 글자

`Placement.IndexOf(목록, 조각)`이 기준의 칸 번호를 찾는다. 두 Block도 이것을 쓴다.

1. 목록의 요소가 그 객체 자신이면 그 칸.
2. 아니면 id가 같은 요소. 조각이 id 글자여도, 요소가 id 글자여도 된다.
3. 없으면 −1. 기준이 그 목록에 없으면 '자리로 고르기'는 아무것도 고르지 않고 '자리 검사'는 거짓이다.

- id로도 찾으므로 **다른 목록의 같은 id 객체**(계산용으로 옮겨 둔 사본)로도 원래 목록의 자리를 찾는다.
- 앞에서부터 찾는다. 같은 id가 둘이면 앞의 것이다. ID 글자 목록(`["plus", "plus"]`)이면 둘째 조커도 첫째의 자리로 센다. 그래서 조각 목록은 고유 id를 가진 객체 목록으로 둔다.
- '자리로 고르기'가 고른 요소가 id 글자면 그 id의 개체(`Game.Find`)로 바꾼다. 찾지 못하면 뺀다.

**여러 목록(`[]`)** — 목록 경로에 `[]`가 있으면(`run` › `players[].charms`, `run` › `wands[].slots`) 목록이 여럿이다(플레이어마다·지팡이마다 칸 한 줄).

- 두 Block은 목록들을 차례로 보며 **기준이 든 목록**을 그 기준의 목록으로 쓴다. 2P의 참은 2P 줄에서 오른쪽을 고르고, 2P 줄의 자리를 검사한다.
- `other`(다른 목록)도 `[]`면 기준이 든 목록과 **같은 순번**의 목록이 맞은편이다(1P 줄 ↔ 1P 맞은편). 맞은편 목록이 모자라면 마지막 것이다.
- 기준이 어느 목록에도 없으면 고르지 않고, 검사는 거짓이다.

### 2-5. 무늬와 회전 — `Pattern` · `Rotate`

- 무늬 = 기준 칸에서 본 상대 좌표 목록이다. `(1, 0)` = 오른쪽 칸, `(0, 1)` = 아래 칸, `(-1, -1)` = 왼쪽 위 칸.
- `Placement.Rotate(상대 좌표, 회전 수)`: 화면에서 시계 방향으로 90°씩 돌린다. 회전 1이면 오른쪽 → 아래, 아래 → 왼쪽, 왼쪽 → 위, 위 → 오른쪽이다. 음수는 반시계 방향이다.
- `Placement.Pattern(칸 번호, 칸 수, 줄 길이, 무늬, 회전 수)`: 무늬를 돌린 뒤 그 칸에서 덮는 칸 번호들이다. 목록 밖은 뺀다. 칸 번호 자체가 목록 밖(−1 등)이면 빈 결과다. **줄 밖으로 나간 칸도 다음 줄로 넘기지 않고 뺀다**(5번 칸의 "오른쪽 칸"은 6번이 아니라 없음). 빈 칸도 낸다. 무늬의 차례대로 준다.

| 석판 칸 8번(x2, y1), 무늬 "오른쪽 두 칸" `(1, 0)` `(2, 0)`, 34칸 가방 | 덮는 칸 |
|---|---|
| 회전 0 | 9 · 10 |
| 회전 1(시계 90°) | 14 · 20(아래 두 칸) |
| 회전 2 | 7 · 6(왼쪽 두 칸) |
| 회전 3 | 2(위 한 칸 — 그 위는 목록 밖) |

- `Grid.Rotate`(36장)는 y가 위로 늘어나는 좌표에서 시계 방향이다. 같은 무늬를 넣어도 두 함수의 결과가 다르다. 목록 칸에는 `Placement.Rotate`를 쓴다.

### 2-6. 놓인 순서 — 패시브 '놓인 것마다'

- 패시브의 **`perElement`(놓인 것마다)**를 켜면 가진 쪽이 목록의 요소 하나하나다(17장). 결과의 사용자 = 그 조각이다. 그래서 '자리로 고르기'·'자리 검사'의 기준 '사용자'가 곧 그 조각의 자리다.
- 같은 목록(같은 `ownerKey`·`ownerPath`)을 보는 '놓인 것마다' 패시브끼리는 **패시브 등록 순서가 아니라 목록 순서**로 섞여 발동한다. 조커 칸은 왼쪽부터, 보드는 칸 번호 순서다(열 우선으로 담은 보드면 앞 칸 위→아래, 다음 칸, 5-3).
- 가진 목록이 여러 목록(`wands[].slots`·`players[].charms`)이면 **모든 목록이 한 줄로 이어진 순서**다. 첫 지팡이(1P)의 칸 전부 → 둘째 지팡이(2P)의 칸.
- 발동 도중에 앞 칸의 결과가 목록에서 뺀 조각은 발동하지 않는다. 그 순간 목록에 남은 것만 발동한다.
- 같은 조각이 둘이면 각자 발동한다. '놓인 것마다'를 끈 패시브는 가진 쪽(목록을 품은 객체, 예: `run`)마다 한 번이다. 이때 사용자 = `run`이라 자리 Block의 기준 '사용자'가 목록에 없다.
- 메인 룰이 조각을 직접 차례로 발동할 때(카운터가 차면 발동)는 행동이 칸 번호 순서로 돌며 `Game.TryUse`(16장)를 부른다(5-3).

### 2-7. 빌려 쓰기 — 결과 '빌려 쓰기'

- 결과 **'빌려 쓰기'**는 대상 Block이 고른 조각의 패시브를 지금 순간(받은 이벤트·가로챈 의도)으로 한 번 더 낸다. Blueprint·Brainstorm이 이것이다.
- 고른 조각의 패시브는 `matchField`(일치 필드)로 찾는다. 그 패시브의 가진 목록에 그 조각 객체가 들어 있어야 한다.
- 자기 자신은 빼고 빌린다.
- 빌린 패시브의 발동(트리거)을 원본 조각을 가진 쪽으로 다시 검사한다. 지금 순간에 맞지 않으면 아무 일도 없다. 점수 단계에 발동하는 조커를 라운드 끝에 빌리면 없다.
- 결과의 사용자 = **원본 조각**이다. 원본의 값(`mult`·레벨)을 읽는다.
- 효과 '필드 바꾸기'는 빌려 쓰는 중에 **원본 조각 자신의 필드를 바꾸지 않는다**. 쌓이는 값은 원본이 직접 발동할 때만 쌓인다. 다른 대상(카드 등)의 필드는 바꾼다.
- 서로 빌리는 고리(Blueprint ↔ Brainstorm)는 끊는다. 이미 빌려 쓰는 중인 같은 패시브·같은 원본은 다시 빌리지 않는다.
- 빌려 쓰는 동안 컨텍스트의 `IsBorrowed`가 참이다. 코드로 쓴 결과는 이것으로 쌓기를 거른다(5-2).

---

## 3. 에디터에서 쓰는 법

### 3-1. 대상 '자리로 고르기'

결과 `대상에게 효과`의 `targets`(대상)·`빌려 쓰기`의 `targets`(빌릴 조각)에 **자리로 고르기**(분류 "대상")를 넣는다. DB 화면의 칸 이름은 코드의 변수 이름이고, 마우스를 올리면 한글 설명(툴팁)이 보인다.

| 칸 | 툴팁 | 값 |
|---|---|---|
| `origin` | 기준 | 사용자(가진 쪽) · 앞의 대상 · 이벤트의 개체 · 없음(목록 전체 — 맨 처음·맨 끝·모두) |
| `eventField` | 이벤트 필드(id) | `origin`이 '이벤트의 개체'일 때만 보인다. 이벤트·의도의 필드(id 또는 개체) |
| `list` | 기준이 놓인 목록 | `key`(플레이어 데이터) › `path`(필드(목록)). 예: `run` › `jokers`. `[]`를 쓰면 여러 목록(`players[].charms`, 2-4) |
| `columns` | 줄 길이(0 = 한 줄) | 한 줄 0, 열 우선 보드 2, 가방 6 |
| `direction` | 방향 | 2-2의 표(선택지는 한글로 보인다) |
| `distance` | 거리(왼쪽·오른쪽·위·아래) | 기본 1 |
| `other` | 다른 목록에서 고르기(비우면 같은 목록) | 맞은편 보드(`run` › `enemies`). 기준의 칸 번호로 그 목록에서 고른다 |

- `origin` '사용자': 능력이면 쓴 개체, 상태 효과·패시브면 가진 쪽(놓인 것마다면 그 조각)이다.
- `origin` '앞의 대상': 위 대상 Block이 낸 대상마다 센다. 맨 위에 두면 고른 대상(패시브면 가진 쪽)에서 출발한다.
- `origin`이 '앞의 대상'이 아니면 앞 대상을 버리고 새로 고른다. 이어서 '조건으로 거르기'·'무작위 n개'로 줄인다(16장 2-6).
- 기준이 여럿이면 각자 고른 것을 모으고, 겹치는 것은 한 번만 낸다.
- `origin` '없음': 기준 없이 목록 전체에서 고른다(2-2). 앞 대상은 버린다.
- 검증 오류: "자리로 고르기: 기준이 놓인 목록(플레이어 데이터)을 고르세요." / "자리로 고르기: 기준이 없으면 방향은 목록 맨 처음·목록 맨 끝·나를 뺀 모두만 됩니다." / "자리로 고르기: 이벤트 필드를 적으세요." / "자리로 고르기: 줄 길이는 0 이상이어야 합니다."

### 3-2. 조건 '자리 검사'

트리거의 조건 칸(`조건 충족`, 또는 `이벤트 받기`·`가로채기`·`사용`과 `&& AND`)에 **자리 검사**(분류 "조건")를 넣는다.

| 칸 | 툴팁 | 값 |
|---|---|---|
| `who` | 누구 | 사용자(가진 쪽) · 이벤트의 개체. '앞의 대상'·'없음'은 고를 수 없다(검증 오류) |
| `eventField` | 이벤트 필드(id) | `who`가 '이벤트의 개체'일 때만 보인다 |
| `list` | 목록 | `key`(플레이어 데이터) › `path`(필드(목록)). `[]`면 기준이 든 목록(2-4) |
| `columns` | 줄 길이(0 = 한 줄) | 한 줄 0, 열 우선 보드 2, 가방 6 |
| `spot` | 자리 | 2-3의 표(선택지는 한글로 보인다) |
| `index` | n(n번째 칸) | `spot`이 'n번째 칸(0부터)'일 때만 보인다 |
| `not` | 아닐 때 참 | 켜면 그 자리가 아닐 때 참 |

- 사용자는 그 순간의 사용자다. 놓인 것마다 패시브면 그 조각, 능력이면 쓴 개체다. 패시브의 `modifierWhen`(보정 조건)에 두면 보정을 읽을 때마다 가진 쪽(그 조각)으로 검사한다(5-3 Fury).
- 사용자가 목록에 없으면 거짓이다. **`not`(아닐 때 참)을 켜도 거짓이다.**
- 검증 오류: "자리 검사: 목록(플레이어 데이터)을 고르세요." / "자리 검사: 이벤트 필드를 적으세요." / "자리 검사: 누구는 사용자나 이벤트의 개체만 됩니다."

### 3-3. 결과 '빌려 쓰기'

패시브의 결과 칸에 **빌려 쓰기**(분류 "결과")를 넣는다.

| 칸 | 툴팁 | 값 |
|---|---|---|
| `targets` | 빌릴 조각 | 대상 Block 목록. 보통 '자리로 고르기' 하나(`direction` 오른쪽 · 목록 맨 처음) |

- 요약은 "빌려 쓰기: " 뒤에 대상 Block이 화살표로 이어진다.
- 검증 오류: "빌려 쓰기: 빌릴 조각을 고르는 대상 Block을 넣으세요(예: 자리로 고르기 — 오른쪽)." / "빌려 쓰기: 비어 있는 Block이 있습니다(스크립트가 지워졌을 수 있음)."

패시브 쪽 칸(17장)은 `ownerKey`·`ownerPath`(가진 목록) · `matchField`(일치 필드) · `perElement`(놓인 것마다) · `levelField`(레벨 필드) · `trigger`(발동) · `holderField`(가진 쪽 필드) · `outcomes`(결과) · `modifiers`(가진 동안 능력치 보정) · `modifierTarget`(보정 받을 대상) · `modifierWhen`(보정 조건) · `stacking`(여럿일 때)이다. 5절 예제는 이 이름으로 적는다.

### 3-4. 모니터에서 확인하기

- **데이터** 탭: 자리 목록(`run › jokers`, `run › bag`)의 요소 번호가 칸 번호다. 요소를 넣고 빼며 시험한다.
- **타임라인** 탭: 패시브의 결과는 "결과: …"(출처 `Passive`)로 남는다. 놓인 것마다 패시브는 목록 순서대로 쌓인다. 빌려 쓴 결과도 같은 출처로 남는다.
- 자리 Block은 값을 바꾸지 않는다. 고른 대상이 이상하면 줄 길이와 목록 경로를 먼저 본다.

---

## 4. 코드로 쓰는 법 — 문법 요소를 하나씩

```csharp
var right = Placement.Around(run.jokers, joker, 0, PlaceDirection.Right).FirstOrDefault();                          // 오른쪽 조커(없으면 null)
var front = Placement.Around(run.board, unit, 2, PlaceDirection.ColumnFirst, target: run.enemies).FirstOrDefault(); // 맞은편 같은 레인 맨 앞(열 우선 보드)
bool bottom = Placement.Is(run.bag, piece, 6, PlaceSpot.BottomRow);                                                // 가방 맨 아래 줄인가
foreach (var at in Placement.Pattern(index, run.bag.Count, 6, slate.pattern, piece.turns)) { … }                  // 석판이 덮는 칸 번호
Cell cell = Placement.CellOf(13, 6);                                                                               // (1, 2)
int back = Placement.IndexOf(new Cell(1, 2), run.bag.Count, 6);                                                    // 13(목록 밖이면 −1)
```

### 4-1. `Placement`

| 멤버 | 뜻 |
|---|---|
| `Cell CellOf(int index, int columns)` | 칸 번호 → 좌표. `columns` 0이면 (번호, 0) |
| `int IndexOf(Cell cell, int count, int columns)` | 좌표 → 칸 번호. 목록 밖이면 −1(`count` = 목록 길이) |
| `int IndexOf(IList list, object item)` | 그 조각의 칸 번호(같은 객체 → 같은 id → id 글자, 2-4). 없으면 −1 |
| `Cell Rotate(Cell offset, int turns)` | 상대 좌표를 화면에서 시계 방향으로 90° × `turns` |
| `IEnumerable<int> Pattern(int index, int count, int columns, IEnumerable<Cell> offsets, int turns = 0)` | 돌린 무늬가 덮는 칸 번호. 목록 밖·줄 밖은 빼고 빈 칸은 낸다 |
| `IEnumerable<int> Indices(int index, int count, int columns, PlaceDirection direction, int distance = 1, IList list = null, bool includeSelf = false)` | 그 칸에서 방향의 칸 번호. 빈 칸도 낸다. '맨 앞·맨 끝' 방향은 `list`로 차 있는 칸을 찾는다(비우면 모든 칸을 차 있다고 본다). 맞은편이면 그 목록의 길이와 그 목록을 주고 `includeSelf`를 참으로(같은 줄·같은 열·모두에 같은 번호 칸도 낸다). `index`가 −1(기준 없음)이면 '목록 맨 처음'·'목록 맨 끝'·'나를 뺀 모두'(모든 칸)만 내고 나머지는 빈 결과다 |
| `IEnumerable<T> Around<T>(IList<T> list, T item, int columns, PlaceDirection direction, int distance = 1, IList<T> target = null)` | 그 조각 기준 방향의 **차 있는** 요소들. 같은 목록이면 자기는 빠진다. `target`을 주면 그 목록(맞은편)에서 고른다 |
| `bool Is(IList list, int index, int columns, PlaceSpot spot, int n = 0)` | 그 칸이 그 자리인가. 목록 밖이면 거짓 |
| `bool Is<T>(IList<T> list, T item, int columns, PlaceSpot spot, int n = 0)` | 그 조각이 그 자리인가. 목록에 없으면 거짓 |

- 모두 static이고 상태가 없다. 값을 바꾸지 않으므로 화면 코드(옆 칸 강조, 놓을 자리 미리 보기)도 같은 함수를 부른다.
- `PlaceDirection`·`PlaceSpot`의 값과 화면 이름은 2-2·2-3의 표다.
- `Cell`은 36장의 구조체다(`+`·`−`·`==`, 4-1).

### 4-2. Block의 코드 형식

| 화면 이름 | 코드 형식 | 필드 |
|---|---|---|
| 자리로 고르기(대상) | `PlaceTarget` | `origin`(`PlaceOrigin.User` · `Previous` · `EventField` · `None`) · `eventField` · `list`(`PlayerDataListRef { key, path }`) · `columns` · `direction` · `distance` · `other` |
| 자리 검사(조건) | `PlaceCondition` | `who`(`User` · `EventField`) · `eventField` · `list` · `columns` · `spot` · `index` · `not` |
| 빌려 쓰기(결과) | `BorrowOutcome` | `targets`(`List<TargetSelector>`) |

```csharp
// "맞은편 같은 레인 맨 앞의 적에게 3"(열 우선 보드, 5-3) — 코드로 조립하면
var strike = new TargetedOutcome
{
    targets =
    {
        new PlaceTarget
        {
            list = new PlayerDataListRef { key = "run", path = "board" }, columns = 2, direction = PlaceDirection.ColumnFirst,
            other = new PlayerDataListRef { key = "run", path = "enemies" },
        },
    },
    effects = { new Damage { amount = 3 } },
};

// Blueprint의 결과
var borrowRight = new BorrowOutcome { targets = { new PlaceTarget { list = new PlayerDataListRef { key = "run", path = "jokers" }, direction = PlaceDirection.Right } } };

// 누가 쓰든 손패 맨 오른쪽 카드(기준 없음)
var rightmost = new PlaceTarget { origin = PlaceOrigin.None, list = new PlayerDataListRef { key = "run", path = "hand" }, direction = PlaceDirection.Last };
```

### 4-3. 빌려 쓰기 — `Game.BorrowPassive` · `session.Passives`

| 멤버 | 뜻 |
|---|---|
| `bool Game.BorrowPassive(string passiveId, object lender)` | 지금 순간으로 그 패시브를 원본 `lender`의 것으로 한 번 더 낸다(결과 '빌려 쓰기'와 같다). 결과를 냈으면 참. 트리거가 맞지 않거나, 같은 패시브·원본을 이미 빌려 쓰는 중이거나, 지금 순간이 없으면(트리거 결과·`[BindTo]` 밖) 거짓 |
| `bool session.Passives.Borrow(string id, object lender, TriggerContext moment)` | 순간을 직접 준다 |
| `IEnumerable<PassiveDefinition> session.Passives.MatchingFor(object element)` | 그 조각을 가진 것으로 보는 패시브들. 패시브의 가진 목록에 그 조각 **객체 자신**이 들어 있고 일치해야 한다(id만 같은 다른 객체는 아니다) |
| `TriggerContext.IsBorrowed` | 빌려 쓰는 중이면 참 |

```csharp
// 흉내: 나를 뺀 켜진 아티팩트 중 레벨이 가장 높은 것의 패시브를 빌린다(5-5의 가방, 레벨 0 = 꺼짐·석판)
[BindTo(GameIds.Passive.Mimic)]
static void Mimic(TriggerContext c)
{
    var me = (BagPiece)c.User;                                                      // 놓인 것마다: 사용자 = 이 아티팩트
    var best = c.Game.Data().Run.bag.Where(a => a != null && a != me && a.level > 0).OrderByDescending(a => a.level).FirstOrDefault();
    if (best != null) c.Game.BorrowPassive(best.item, best);                        // 원본은 목록 안의 그 객체 그대로
}
```

---

## 5. 상황별 예제

예제는 아래 판 데이터를 함께 쓴다. `RunState`(판 데이터 `run`)에는 17장처럼 `hero`(id `hero`)가 있다고 본다. 피해 효과 `Damage`와 의도 `피해`(`DamageIntent { sourceId, targetId, amount, cancelled }`)는 프로젝트의 것이다(28장).

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using GBible;
using GBible.Ids;

[Serializable] public class Joker { public string id; public string joker; public int mult; }      // joker = 원본 ID(패시브 ID), mult = 이 조커에 쌓인 배수
[Serializable] public class BoardUnit { public string id; public string ability; public List<string> traits = new List<string>(); public int attack = 3; public int hp = 10; public int counter = 3; public int maxCounter = 3; }   // traits = 특성 패시브 ID들
[Serializable] public class BagPiece { public string id; public string item; public int turns; public int level; }   // item = 원본 ID(아티팩트 = 패시브 ID, 석판 = 인스턴스 ID), turns = 돌린 횟수, level = 최종 레벨(5-5가 쓴다)

// RunState에 더한다
public List<Joker> jokers = new List<Joker>();                            // 조커 칸(한 줄, 줄 길이 0). 빈 칸 없이 앞으로 당긴다
public List<BoardUnit> board = new List<BoardUnit>(new BoardUnit[6]);     // 내 보드 레인 2개 × 3칸, 열 우선(줄 길이 2, 5-3). 처음엔 빈 칸 6개
public List<BoardUnit> enemies = new List<BoardUnit>(new BoardUnit[6]);   // 적 보드(같은 모양)
public List<BagPiece> bag = new List<BagPiece>(new BagPiece[34]);         // 가방 6열 34칸(마지막 줄 4칸)
public int mult = 1;                                                      // 이번 손의 배수
```

### 5-1. 조커 칸 — 왼쪽부터 발동, 옆 조커 키우기 · 없애기

게임의 행동 `PlayHand`가 점수 단계에서 이벤트 `손 점수`(`HandScored`)를 쏜다고 본다.

**에디터**

1. DB › 패시브 `plusMult`(+4 배수): `ownerKey` `run`, `ownerPath` `jokers`, `matchField`(일치 필드) `joker`, `perElement`(놓인 것마다) 켬. `trigger`(발동) `이벤트 받기` → `손 점수`. `outcomes`(결과) `플레이어 데이터에 주기` → `key` `run`, `path` `mult`, `amount`(증감) `4`.
2. DB › 패시브 `timesMult`(×2 배수): `ownerKey`·`ownerPath`·`matchField`·`perElement`·`trigger`는 1과 같다. `outcomes` `대상에게 효과` → `targets` `플레이어 데이터 목록`(`key` `run`, `path` 비움 = 값 전체) → `effects` `필드 바꾸기`(`field` `mult`, `change`(방법) `곱하기`, `value` `2`).
3. DB › 패시브 `cheerleader`(라운드 끝에 오른쪽 조커의 배수 +2): 1과 같고 `trigger` `이벤트 받기` → `라운드 끝`. `outcomes` `대상에게 효과` → `targets` `자리로 고르기`(`origin` 사용자(가진 쪽), `list` `run` › `jokers`, `columns` `0`, `direction` 오른쪽) → `effects` `필드 바꾸기`(`field` `mult`, `change` 더하기, `value` `2`).
4. DB › 패시브 `dagger`(손 점수 때 오른쪽 조커를 없앤다): 1과 같고 결과는 코드다.

```csharp
[BindTo(GameIds.Passive.Dagger)]
static void Dagger(TriggerContext c)
{
    var run = c.Game.Data().Run;
    var right = Placement.Around(run.jokers, (Joker)c.User, 0, PlaceDirection.Right).FirstOrDefault();   // 사용자 = 이 칸의 단검
    if (right != null) run.jokers.Remove(right);                                                        // 조커 칸은 빈 칸 없이 당긴다
}
```

| 조커 칸(왼쪽 → 오른쪽) | 손 점수 뒤 배수(처음 1) |
|---|---|
| +4 · ×2 | (1 + 4) × 2 = 10 |
| ×2 · +4 | 1 × 2 + 4 = 6 |
| +4 · 단검 · +4 · +4 | 1 + 4 + 4 = 9. 셋째 칸은 단검이 없애 발동하지 않는다 |
| +4 · +4 | 1 + 4 + 4 = 9. 같은 조커 둘은 각자 발동한다 |

- 넷 다 같은 목록(`run › jokers`)을 보는 '놓인 것마다' 패시브라 패시브를 만든 차례와 상관없이 칸 순서로 발동한다(2-6).
- `cheerleader`가 맨 오른쪽이면 오른쪽 조커가 없어 아무 일도 없다.
- 조커를 팔거나 없애면 `Remove`로 뺀다. 조커 칸은 당기는 것이 맞다. 가방·보드처럼 칸이 고정이면 `null`로 비운다(5-3).

### 5-2. Blueprint · Brainstorm — 결과 '빌려 쓰기'

**에디터**

1. DB › 패시브 `blueprint`: `ownerKey` `run`, `ownerPath` `jokers`, `matchField` `joker`, `perElement` 켬.
   - `trigger`(발동): 빌려 올 조커가 발동하는 순간을 모두 담는다. `|| OR` 안에 `이벤트 받기` → `손 점수`, `이벤트 받기` → `라운드 끝` ….
   - `outcomes`(결과): `빌려 쓰기` → `targets`(빌릴 조각) `자리로 고르기`(`origin` 사용자(가진 쪽), `list` `run` › `jokers`, `columns` `0`, `direction` 오른쪽).
2. DB › 패시브 `brainstorm`: 1과 같고 `direction`만 목록 맨 처음(차 있는 첫 칸).

쌓이는 조커 `bus`(손 점수마다 자기 `mult` +1, 그다음 자기 `mult`만큼 배수 +)를 코드로 쓰면 빌려 쓰는 중인지 본다.

```csharp
[BindTo(GameIds.Passive.Bus)]
static void Bus(TriggerContext c)
{
    var me = (Joker)c.User;                    // 빌려 쓰는 중이면 원본 버스
    if (!c.IsBorrowed) me.mult += 1;           // 쌓는 것은 원본이 직접 발동할 때만
    c.Game.Data().Run.mult += me.mult;
}
```

| 조커 칸(왼쪽 → 오른쪽) | 손 점수 뒤 |
|---|---|
| +4 · Blueprint · +4 | 배수 1 + 4 + 4 + 4 = 13. Blueprint가 오른쪽 +4를 한 번 더 |
| Blueprint · 버스(`mult` 2) | Blueprint가 원본 값 2를 더하고, 버스가 3으로 쌓은 뒤 3을 더한다. 배수 1 + 2 + 3 = 6, 버스 `mult` = 3(한 번만 쌓인다) |
| Brainstorm · +4 | Brainstorm이 맨 처음 칸이라 자기 자신은 빼고 아무것도 빌리지 않는다. 배수 1 + 4 = 5 |
| Blueprint · Brainstorm | 서로 빌리는 고리를 끊어 몇 번 안에 끝난다 |

- 결과의 사용자는 원본 조커다. 빌린 결과의 '자리로 고르기'·'자리 검사'도 원본 조커의 자리로 센다.
- '필드 바꾸기'로 쌓으면 따로 거르지 않아도 원본만 쌓인다(2-7). 코드로 쌓을 때만 `c.IsBorrowed`를 본다.
- Blueprint의 발동에 그 순간이 없으면 Blueprint가 아예 발동하지 않는다. 빌려 올 조커들이 받는 이벤트·의도를 모두 넣는다.
- 코드로 빌리는 것은 `Game.BorrowPassive`(4-3)다.

### 5-3. 와일드프로스트 — 열 우선 보드, 맞은편 같은 레인 맨 앞

보드는 편마다 레인(가로줄) 2개 × 3칸이고, 차례는 적 먼저, 각 보드에서 **앞 칸 위→아래 → 가운데 칸 → 뒤 칸**이다(열 우선). 보드 목록을 이 차례대로 담는다(**열 우선으로 담기**, 줄 길이 = 레인 수 2).

- 목록 순서 = 턴 순서다. 칸 번호로 도는 턴 행동도, '놓인 것마다' 패시브의 차례(2-6)도 원작과 같다.
- 자리 계산의 '줄'(y)은 앞뒤 깊이(y = 0이 앞 칸, '위(앞 줄)' = 한 칸 앞), '열'(x)은 레인(x = 0 위 레인, x = 1 아래 레인)이다. **원작의 '같은 줄'(레인)은 여기의 '같은 열'**이다.
- 화면은 칸 번호를 원작 모양(레인이 가로줄, 적 보드는 좌우 뒤집기)으로 그린다.

```
             적 보드 enemies(줄 길이 2)            내 보드 board(줄 길이 2)
              뒤   가운데   앞                      앞   가운데   뒤
 위 레인     [4]   [2]   [0]     ← 맞은편 →      [0]   [2]   [4]     x = 0
 아래 레인   [5]   [3]   [1]                     [1]   [3]   [5]     x = 1
             y=2   y=1   y=0                     y=0   y=1   y=2
```

**에디터**

1. DB › 능력 `allyStrike`: 결과 `대상에게 효과` → `targets` `자리로 고르기`(`origin` 사용자(가진 쪽), `list` `run` › `board`, `columns` `2`, `direction` 같은 열 맨 앞(차 있는 첫 칸, 위부터), `other` `run` › `enemies`) → `effects` `피해`(3).
2. DB › 능력 `enemyStrike`: 1과 같고 `list` `run` › `enemies`, `other` `run` › `board`.
3. 유닛 원본의 `ability`에 이 ID를 넣는다.
4. DB › 패시브 `fury`(같은 레인에 혼자면 공격 +3): `ownerKey` `run`, `ownerPath` `board`, `matchField`(일치 필드) `traits`(글자 목록), `perElement`(놓인 것마다) 켬. `modifiers`(가진 동안 능력치 보정) `attack` 플랫 `3`(받을 대상 비움 = 그 유닛). `modifierWhen`(보정 조건) `조건 충족` → `자리 검사`(`who` 사용자(가진 쪽), `list` `run` › `board`, `columns` `2`, `spot` 같은 열에 혼자).

**규칙**

```csharp
[Serializable, BlockInfo("턴 끝")]
public class EndTurn : GameAction
{
    protected override void Run()
    {
        var run = Game.Data().Run;
        foreach (var side in new[] { run.enemies, run.board })        // 적 먼저
            for (int i = 0; i < side.Count; i++)                      // 칸 번호 순서 = 앞 칸 위→아래, 가운데, 뒤(열 우선)
            {
                var unit = side[i];                                   // 그 순간의 칸을 읽는다(앞에서 쓰러졌으면 null)
                if (unit == null || --unit.counter > 0) continue;
                unit.counter = unit.maxCounter;
                Game.TryUse(unit.ability, unit);                      // 쓸 수 없으면 아무 일 없이 넘어간다
            }
    }
}

// 피해 규칙에서: 쓰러진 유닛은 칸을 비운다(Remove하면 뒤 칸이 당겨져 자리가 바뀐다)
var side = run.board.Contains(unit) ? run.board : run.enemies;
side[side.IndexOf(unit)] = null;
```

| 내 유닛 | 맞은편 레인(앞 → 뒤) | 맞는 적 |
|---|---|---|
| 0번(위 레인, 앞 칸) | 적 위 레인 [0 빈 칸, 2 e1, 4 e2] | e1(빈 칸을 건너뛴다) |
| 3번(아래 레인, 가운데 칸) | 적 아래 레인 [1 e3, 3 빈 칸, 5 빈 칸] | e3 |
| 아래 레인, 적 아래 레인이 모두 빔 | — | 아무도 안 맞는다 |

맞은편 레인이 비면 다른 레인을 치는 규칙이면 프로젝트 대상 Block(16장 2-5)으로 만든다.

```csharp
[Serializable, BlockInfo("맞은편 맨 앞(없으면 다른 레인)")]
public class FrontTarget : TargetSelector<BoardUnit>
{
    protected override IEnumerable<BoardUnit> Select(IEnumerable<BoardUnit> targets, BoardUnit user)
    {
        var run = Game.Data().Run;
        var mine = run.board.Contains(user) ? run.board : run.enemies;
        var theirs = mine == run.board ? run.enemies : run.board;
        var front = Placement.Around(mine, user, 2, PlaceDirection.ColumnFirst, target: theirs).FirstOrDefault()
                    ?? theirs.FirstOrDefault(u => u != null);        // 같은 레인이 비면 칸 번호 순서로 처음 = 가장 앞의 적
        if (front != null) yield return front;
    }
}
```

- 턴 하나가 행동 하나다. 모든 유닛의 발동이 Undo 한 칸에 묶인다.
- `Game.TryUse`는 사용 조건이 안 맞으면(예: 얼어 있음 조건) 실패를 던지지 않고 거짓이다. 그래서 한 유닛 때문에 턴 전체가 취소되지 않는다(16장).
- "맨 뒤를 친다(Longshot)"는 방향 `같은 열 맨 뒤`, "레인 전체(Barrage)"는 `같은 열`이다. 다른 목록이라 바로 맞은편 칸도 친다.
- Fury의 보정 조건은 보정을 읽을 때마다 그 유닛으로 검사한다(17장 0-1). 같은 레인에 다른 유닛이 들어오면 저절로 빠진다.
- 줄 우선(레인 = 줄, 줄 길이 3)으로 담아도 `같은 줄 맨 앞`·`같은 줄`·`같은 줄에 혼자`로 같은 대상을 고르지만, 목록 순서가 "레인마다 앞→뒤"라 차례가 원작과 다르다. 차례가 중요한 보드는 차례대로 담는다.

### 5-4. 가방 — 맨 아래 줄 · 가장자리 · 양옆이 빈 칸

아티팩트 패시브의 가진 목록은 가방 목록 자체(`run › bag`)다. 석판·제약으로 레벨이 바뀌면 5-5처럼 `levelField`(레벨 필드)를 `level`로 정한다.

**에디터** — 셋 다 DB › 패시브, `ownerKey` `run`, `ownerPath` `bag`, `matchField` `item`, `perElement` 켬.

1. 은접시 `silverPlate`(맨 아래 줄이면 방에 들어갈 때 보호막 +5): `trigger` `&& AND` → `이벤트 받기` → `방 입장`, `조건 충족` → `condition` `자리 검사`(`who` 사용자(가진 쪽), `list` `run` › `bag`, `columns` `6`, `spot` 맨 아래 줄). `outcomes` `플레이어 데이터에 주기` → `key` `run`, `path` `hero.shield`, `amount` `5`.
2. 차가운 자물쇠 `coldLock`(양옆이 빈 칸이면 주인공의 피해 +5): `trigger` `&& AND` → `가로채기` → `intentType` `피해`(`filter` `sourceId` 일치 `hero`), `조건 충족` → `자리 검사`(1과 같고 `spot` 양옆이 빈 칸). `outcomes` `의도 고치기` → `path` `amount`, `op` 더하기, `value` `5`.
3. 용골 파편 `keelShard`(가장자리에 있으면 방에 들어갈 때 체력 +2): `trigger`는 1과 같고 `spot`만 가장자리(상하좌우 중 바깥이 있음). `outcomes` `플레이어 데이터에 주기` → `key` `run`, `path` `hero.hp`, `amount` `2`. 반대로 "가장자리가 아닐 때"는 `not`(아닐 때 참)을 켠다.

| 34칸 가방의 칸 | 맨 아래 줄 | 가장자리 | 양옆이 빈 칸 |
|---|---|---|---|
| 31번(x1, y5) | 예 | 예 | 30·32번이 비면 예 |
| 28번(x4, y4) | 아니요 | 예(아래 칸이 목록 밖) | 27·29번이 비면 예 |
| 27번(x3, y4) | 아니요 | 아니요(아래에 33번) | 26·28번이 비면 예 |
| 5번(x5, y0) | 아니요 | 예 | 4번이 비면 예(오른쪽은 목록 밖) |

- 사용자 = 그 아티팩트다(놓인 것마다). 같은 아티팩트가 둘이면 각자의 자리로 따로 검사한다.
- 손패도 같다. "손패 맨 오른쪽 카드를 낼 때 피해 2배" = `자리 검사`(`who` 이벤트의 개체, `eventField` `card`, `list` `run` › `hand`, `columns` `0`, `spot` 목록 맨 끝(차 있는 것 중)).
- 위치에 따라 늘 붙는 능력치(맨 아래 줄이면 공격 +3)는 가진 동안 능력치 보정 + `modifierWhen`(보정 조건) `자리 검사`(맨 아래 줄)다. 보정만 자리에 따라 켜지고 꺼진다(5-3 Fury와 같다).
- **세피리아식 제약**("가장자리에 있어야 작동" — 맞지 않으면 그 아티팩트의 발동·보정이 **모두** 꺼진다)은 패시브마다 조건을 붙이지 않는다. 5-5의 계산 함수가 그 조각의 최종 레벨을 0으로 쓴다. 레벨 0 이하인 조각은 꺼진 것이다(17장 0-1).

### 5-5. 석판 — 무늬·회전으로 레벨 계산

석판은 무늬 칸에 있는 아티팩트의 레벨을 올린다. 석판을 돌리면 무늬도 돈다. 배치가 바뀌는 행동 끝에서 계산 함수가 조각마다 **최종 레벨**을 다시 계산해 그 조각의 `level` 필드에 쓴다(제약이 안 맞으면 0 = 꺼짐). 아티팩트 패시브는 가방 목록 자체를 가진 목록으로 삼고 그 필드를 레벨 필드로 본다.

> 켜진 아티팩트만 담은 목록을 계산할 때마다 새로 만들면 조각에 쌓인 값(처치 수·카운터)이 새 객체에 없어 사라진다. 그래서 목록을 따로 만들지 않고 가방 조각에 최종 레벨을 쓴다.

**에디터**

- DB › 인스턴스 `slateLine`: `SlateData`, `pattern` `(1, 0)` `(2, 0)`(오른쪽 두 칸), `bonus` `1`.
- DB › 인스턴스 `slateCross`: `pattern` `(0, -1)` `(1, 0)` `(0, 1)` `(-1, 0)`(상하좌우), `bonus` `1`, `edgeOnly` 켬(가장자리에 놓였을 때만).
- DB › 패시브 `ember`(불씨): `ownerKey` `run`, `ownerPath` `bag`, `matchField` `item`, `perElement` 켬, `levelField`(레벨 필드) `level`. `modifiers`(가진 동안 능력치 보정) 한 줄: `attribute` `attack` · `kind`(칸) 플랫 · `byLevel`(레벨별 값) `2` `3` `4` `6`. `modifierTarget`(보정 받을 대상) `hero`.
- 세피리아는 ★0부터 센다. **레벨 필드 = ★ + 1(꺼지면 0)**로 두면 ★0이 레벨 1이고 `byLevel`(레벨별 값)의 첫 칸이 ★0의 값이다.

**규칙**

```csharp
// 석판 원본(DB › 인스턴스): 상대 칸 무늬(화면 기준, y 아래 +)
[Serializable] public class SlateData { public List<Cell> pattern = new List<Cell>(); public int bonus = 1; public bool edgeOnly; }

public static class Bag
{
    public const int Columns = 6;

    static SlateData SlateOf(GameApi game, BagPiece piece) => piece == null ? null : game.Session.Instances?.Invoke(piece.item) as SlateData;

    // 배치가 바뀐 행동 끝에서 부른다: 석판 무늬(돌린 뒤)가 덮은 칸의 레벨을 올리고, 조각마다 최종 레벨을 level에 쓴다.
    public static void Recalculate(GameApi game)
    {
        var bag = game.Data().Run.bag;
        var bonus = new int[bag.Count];
        for (int i = 0; i < bag.Count; i++)
        {
            var slate = SlateOf(game, bag[i]);
            if (slate == null) continue;
            if (slate.edgeOnly && !Placement.Is(bag, i, Columns, PlaceSpot.Edge)) continue;           // 가장자리일 때만 켜지는 석판
            foreach (var at in Placement.Pattern(i, bag.Count, Columns, slate.pattern, bag[i].turns))
                bonus[at] += slate.bonus;                                                            // 목록 밖·줄 밖 칸은 Pattern이 뺀다
        }
        for (int i = 0; i < bag.Count; i++)
        {
            var piece = bag[i];
            if (piece == null) continue;
            if (SlateOf(game, piece) != null) { piece.level = 0; continue; }                         // 석판은 아티팩트가 아니다
            piece.level = Rules.ConstraintMet(game, bag, i) ? 1 + bonus[i] : 0;                      // ★ + 1, 제약이 안 맞으면 0(꺼짐)
        }
    }
}

[Serializable, BlockInfo("가방 칸 옮기기")]
public class MovePiece : GameAction
{
    public int from, to;                                      // 칸 번호
    protected override void Run()
    {
        var bag = Game.Data().Run.bag;
        if (from < 0 || from >= bag.Count || to < 0 || to >= bag.Count) throw Game.Fail("fail.noCell", "없는 칸입니다.");
        (bag[from], bag[to]) = (bag[to], bag[from]);          // 바꿔 놓기(빈 칸이면 옮기기)
        Bag.Recalculate(Game);
    }
}

[Serializable, BlockInfo("석판 돌리기")]
public class RotatePiece : GameAction
{
    public BagPiece piece;                                    // 참조 필드: 실행 직전에 지금 객체로 다시 찾는다
    protected override void Run()
    {
        piece.turns = (piece.turns + 1) % 4;                  // 시계 방향 90°
        Bag.Recalculate(Game);
    }
}
```

| 석판 `slateLine` 8번, 불씨 14번 | 불씨 레벨 | 주인공 공격 보정 |
|---|---|---|
| 회전 0(덮는 칸 9 · 10) | 1 | +2 |
| 회전 1(덮는 칸 14 · 20) | 2 | +3 |
| 회전 1 + 12번에 `slateLine` 하나 더(회전 0, 덮는 칸 13 · 14) | 3 | +4 |

- 배치가 바뀌는 행동(옮기기·돌리기·넣기·빼기) 끝에서 모두 `Bag.Recalculate`를 부른다. 계산 결과가 판 데이터(조각의 `level`)라 Undo·저장이 따라온다. 새로 넣은 조각은 계산 전에는 레벨 0(꺼짐)이다.
- `Rules.ConstraintMet(game, bag, i)`는 그 아티팩트 원본의 제약(가장자리·맨 아래 줄 …)을 `Placement.Is`로 보는 프로젝트 함수다(제약이 없으면 참). 제약이 안 맞은 조각은 레벨 0이라 발동·보정이 모두 없고 `Game.Owned`도 0이다(5-4).
- 화면이 "돌리면 어떻게 되나"를 미리 보려면 `Game.Preview(new RotatePiece { piece = p }, g => g.Data().Run.bag.Where(a => a != null).Select(a => (a.id, a.level)).ToList())`(33장)로 해 보고 값을 복사해 읽는다. 계산을 화면 코드에 복사하지 않는다.
- 가진 목록이 가방 자체라 '자리 검사'(목록 `run` › `bag`)가 그대로 그 조각의 자리를 보고, 조각에 쌓인 값(`kills` 같은 필드)도 계산과 상관없이 남는다.
- `modifierTarget`(보정 받을 대상)을 정하면 받는 쪽(`hero`) 하나에 가진 쪽(불씨)이 여럿이다. `stacking`(여럿일 때)이 기본 `레벨을 더해 한 번`이면 불씨 둘(레벨 1·2)이 레벨 3의 값 4, `가진 쪽마다 값을 더함`이면 2 + 3 = 5, `가장 높은 레벨 하나만`이면 3이다(17장 0-1).
- 콤보(같은 태그 N개면 단계 효과)는 같은 계산 함수가 태그 → 개수 딕셔너리를 판 데이터에 쓰고, 그것을 가진 목록으로 삼는 패시브(레벨별 값)로 만든다.
- 크기가 여러 칸인 조각은 목록에 머리 칸만 두고 나머지 칸은 '차지됨' 표시로 둔다. 계산 함수가 셀 때 그 칸을 반영한다.

### 5-6. 지팡이 칸 — 지팡이마다 `slots` 한 줄(매지크래프트)

지팡이마다 칸 한 줄(줄 길이 0)을 두고, 일반 칸 뒤에 보조 칸을 잇는다. 쏘는 행동이 일반 칸을 왼쪽부터 읽으며 칸마다 `주문 시전` 이벤트를 쏜다.

```csharp
[Serializable] public class Wand { public string id; public string wand; public double mp = 100, maxMp = 100; public List<Spell> slots = new List<Spell>(); }   // slots = 일반 칸 + 보조 칸, 빈 칸 = null
[Serializable] public class Spell { public string id; public string spell; public int level = 1; }   // spell = 원본 ID(패시브 ID)
[GameEvent("주문 시전")] public class SpellCast { public string wandId, pieceId; }
// RunState에 더한다: public List<Wand> wands = new List<Wand>();

[Serializable, BlockInfo("지팡이 쏘기")]
public class CastWand : GameAction
{
    public Wand wand;
    protected override void Run()
    {
        foreach (var piece in wand.slots.Take(Rules.MainSlots(Game, wand)).ToList())   // 일반 칸만 왼쪽부터(칸 수는 지팡이 원본에서)
        {
            if (piece == null) continue;
            if (!Game.TryPay(GameIds.PlayerData.Run.Id, $"wands[{wand.id}].mp", Rules.ManaOf(Game, piece), forId: piece.spell, user: wand)) break;   // MP가 모자라면 여기서 멈춘다
            Game.Events.Publish(new SpellCast { wandId = wand.id, pieceId = piece.id });
        }
    }
}
```

**에디터**(DB › 패시브, 모두 `ownerKey` `run`, `ownerPath` `wands[].slots`, `matchField` `spell`):

1. 주문마다(`spark` …): `perElement`(놓인 것마다) 켬, `levelField` `level`. `trigger` `이벤트 받기` → `주문 시전`, `holderField`(가진 쪽 필드) `pieceId`(내 칸 차례만). `outcomes`는 주문의 효과.
2. 메아리 `echo`(다음 칸 주문을 한 번 더): 1과 같고 `outcomes` `빌려 쓰기` → `자리로 고르기`(`origin` 사용자(가진 쪽), `list` `run` › `wands[].slots`, `direction` 오른쪽).
3. 마나 저장소 `reservoir`(칸에 두면 그 지팡이 최대 MP +20): `perElement` 끔 → 가진 쪽 = 그 칸 목록을 품은 지팡이. `modifiers`(가진 동안 능력치 보정) `maxMp` 플랫 `20`(받을 대상 비움 = 그 지팡이). 규칙·화면은 `Game.Attribute(wand, nameof(Wand.maxMp))`로 읽는다(28장 4-2).

- 경로에 `[]`가 있어 메아리는 **자기가 든 지팡이의 줄**에서 오른쪽을 고른다. 다른 지팡이의 칸과 섞이지 않는다(2-4).
- 빌린 패시브의 `holderField`(가진 쪽 필드)는 빌리는 쪽 기준이다. 메아리 차례(`pieceId` = 메아리)에 오른쪽 주문이 한 번 더 나고, 그 주문은 자기 차례에 또 난다(17장 0-1). 메아리 둘이 이어지면 첫 메아리 차례에 둘째 메아리가 빌린 오른쪽 주문도 난다(빌린 결과 안에서 또 빌리면 처음 빌린 조각 기준).
- 모든 주문이 같은 이벤트를 받으면(방 입장) '놓인 것마다' 차례는 첫 지팡이의 칸 전부 → 둘째 지팡이의 칸이다(2-6).
- MP는 `Game.TryPay`(28장 2-4): 모자라면 아무것도 바꾸지 않고 거짓이라 그 시전만 멈추고 행동은 계속된다. `forId`(무엇을 위한 비용) = 주문 원본 ID라 "불 주문 MP 반값"이 의도 조건 `forId`로 거른다.
- 보조 칸의 충전(시전마다 에너지 +3, 차면 보조 칸 주문 시전)은 프로젝트 동사로 둔다: 효과 Block `에너지 충전`이 규칙 함수 `Rules.Charge`를 부르고, 그 안에 가로채기 자리(프로젝트 의도)를 둔다. "충전 효율 +20%" 같은 기믹이 양을 고쳐야 하므로 '필드 바꾸기'로 바로 더하지 않는다(28장 2-3 규칙).

---

## 6. 주의할 점 · 자주 하는 실수

| 증상 | 원인과 해결 |
|---|---|
| 조각 하나를 뺐더니 다른 조각의 자리가 바뀜 | `Remove`는 뒤 칸을 당긴다. 칸이 고정인 자리(가방·보드)는 `list[i] = null`로 비운다. 당기는 것이 맞는 자리(조커 칸·손패)만 `Remove`한다. |
| 가방에 놓으려는 칸이 없음 | 목록을 칸 수만큼 미리 채우지 않았다. `new List<BagPiece>(new BagPiece[34])`. 칸이 늘면 행동이 `Add(null)`로 늘린다. |
| 같은 조커 둘 중 둘째가 첫째의 자리로 계산됨 | 조각 목록이 ID 글자 목록이다. 같은 ID면 앞의 것을 찾는다(2-4). 고유 id를 가진 객체 목록 + `matchField`(일치 필드)로 바꾼다. |
| '자리로 고르기'가 아무도 고르지 않음 | 기준이 그 목록에 없다(`perElement`를 끈 패시브는 사용자가 `run`이다 — 누가 쓰든 맨 끝을 고르려면 기준 `없음`), `columns`(줄 길이)가 목록과 다르다, 기준 자신이 맨 앞이라 '같은 줄 맨 앞'·'목록 맨 처음'이 비었다(2-2), 고른 칸이 모두 빈 칸이다. |
| 보드의 차례가 원작과 다름(레인마다 앞→뒤로 돎) | 보드를 줄 우선(레인 = 줄)으로 담았다. 차례가 "앞 칸 위→아래, 다음 칸"이면 그 순서로 담고(줄 길이 = 레인 수) 레인 쪽 방향·자리는 `같은 열 …`을 쓴다(5-3). |
| 2P의 조각이 1P 줄을 기준으로 고름 | 목록 경로에 `[]`를 쓰지 않고 한 사람의 목록을 적었다. `players[].charms`처럼 적으면 기준이 든 목록에서 본다(2-4). |
| 석판·제약을 다시 계산할 때마다 조각에 쌓인 값이 사라짐 | 켜진 아티팩트 목록을 새 객체로 다시 만들었다. 가방 목록 자체를 가진 목록으로 두고 최종 레벨을 조각 필드에 쓴다(꺼지면 0, 5-5). |
| 석판 무늬가 줄 끝을 넘어 다음 줄 첫 칸을 덮을 거라 생각함 | `Pattern`은 줄 밖으로 나간 칸을 버린다(2-5). |
| '자리 검사'의 `not`(아닐 때 참)을 켰는데도 거짓 | 사용자가 그 목록에 없다. 목록에 없으면 늘 거짓이다(3-2). |
| 위·아래·같은 열이 아무것도 고르지 않음 | `columns`(줄 길이)가 0(한 줄)이다. 격자면 열 수를 넣는다. |
| 줄 끝의 오른쪽이 다음 줄 첫 칸이 아님 | 정상이다. 방향은 줄을 넘지 않는다. 목록 순서의 다음 칸이 필요하면 칸 번호 + 1을 코드로 본다. |
| 아래가 비었는데 '맨 아래 줄'이 거짓 | 마지막 줄이 덜 찼다. 맨 아래 줄은 목록의 마지막 줄이다(34칸이면 30~33번). 그 위 줄의 오른쪽 칸은 '가장자리'로 잡는다(2-3). |
| 석판 무늬가 반대로 돈다 | `Grid.Rotate`(y 위 +)를 썼다. 목록 칸은 `Placement.Rotate`·`Pattern`(화면 기준)을 쓴다. |
| 조커가 칸 순서가 아니라 패시브를 만든 차례로 발동 | `perElement`(놓인 것마다)가 꺼져 있거나, 패시브마다 가진 목록(`ownerKey`·`ownerPath`)이 다르다. 같은 목록끼리만 한 줄로 선다(2-6). |
| 앞에서 쓰러진 유닛이 차례에 또 발동 | 목록을 `ToList()`로 복사해 돌았다. 칸 번호로 돌며 그 순간의 칸을 읽는다(5-3). |
| Blueprint가 아무것도 하지 않음 | Blueprint의 발동에 그 순간(이벤트·의도)이 없다, 오른쪽 조커의 발동이 그 순간에 맞지 않는다, 오른쪽 조커의 패시브가 그 조커를 가진 것으로 보지 않는다(`ownerKey`·`ownerPath`·`matchField`를 확인한다). |
| Blueprint가 빌리면 원본 조커의 쌓인 값이 두 번 오름 | 쌓기를 코드로 썼다. `c.IsBorrowed`면 쌓지 않는다(5-2). '필드 바꾸기'는 저절로 거른다. |
| `Game.BorrowPassive`가 늘 거짓 | 트리거 결과·`[BindTo]` 밖에서 불렀다(지금 순간이 없다). 또는 원본이 그 패시브의 가진 목록에 든 객체 자신이 아니다(id만 같은 사본은 안 된다). |
| 석판 레벨이 Undo 뒤 어긋남 | 계산 결과를 화면 코드(행동 밖)에서 썼다. 배치를 바꾸는 행동 안에서 `Bag.Recalculate`를 부른다. |
| `Placement`·`Cell`이 없다는 컴파일 오류 | `using GBible;`이 없다. 프로젝트에 같은 이름의 클래스가 있으면 `GBible.Placement`로 쓰거나 프로젝트 쪽 이름을 바꾼다. |
