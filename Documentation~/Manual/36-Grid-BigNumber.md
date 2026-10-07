# 36. 격자·큰 수

**한 줄 요약**: 칸 단위 게임(전술·로그라이크 던전·보드·농장)은 칸 좌표 `Cell`과 `Grid`의 함수(거리·이웃·방향·이동 범위·길찾기·직선·시야·범위 모양)로 규칙을 쓴다. `Grid`는 맵을 들고 있지 않다. 맵은 게임의 데이터(바뀌지 않는 맵은 원본 인스턴스의 `rows` 글자 목록)이고, `Grid` 함수는 "이 칸에 들어가는 비용"·"이 칸이 시야를 막나"를 함수로 받는다. 플레이어 데이터에는 개체의 `x`·`y`(정수)만 두고, 칸마다의 판 값(심은 작물·부서진 벽)은 `Grid.Key(x, y)` = `"x,y"`를 키로 한 딕셔너리에 둔다. 같은 입력이면 늘 같은 길이 나와 Undo·멀티플레이·재현에서 같다. **큰 수**(`BigNumber`)는 double(약 1e308)을 넘는 방치형·인플레이션 수치를 담는 구조체다(1e300 아래는 double 값 그대로, 그 위는 가수 × 10^지수). 연산자로 평소처럼 계산하고, 플레이어 데이터에 그냥 두면 저장·Undo·Mirror가 따라오고, `ToString()`은 `"1.23K"`·`"4.56Qa"`·`"1.5e400"`이다. `Game.Pay`·DB의 숫자 칸은 double만 알아서 큰 수의 비용은 코드로 검사한다. 가격 곡선(30장)의 "10개 사기"·"살 수 있는 만큼"은 `Game.BalanceSum`·`Game.BalanceCount`다.

**언제 쓰는가**

- SRPG: 이동력 5로 갈 수 있는 칸 칠하기, 고른 칸까지 벽을 돌아 걷기, 창·활·화염구의 공격 범위, 벽 너머는 못 쏘기, 밀쳐 내기(격자).
- 로그라이크: 방향키로 한 칸씩, 몬스터가 다가오기, 보이는 칸만 밝히기(안개).
- 보드게임·퍼즐: 이웃 칸·한 줄로 셋.
- 농장: 밭 칸마다 작물이 자라고, 물뿌리개가 3×3에 물을 준다.
- 방치형·클리커: 쿠키 1e60개, 초당 생산, 건물 가격 ×1.15씩, "10개 사기"(큰 수 + 가격 곡선).

조각을 칸에 놓는 게임(조커 칸·손패·가방 격자·와일드프로스트 보드)은 이 장이 아니라 [37장 자리(Placement)](37-Placement.md)다. 개체가 좌표를 들고 걸어 다니면 `Grid`, 목록 위치가 곧 칸이면 `Placement`다.

이 문서에서 쓰는 용어:

| 용어 | 뜻 |
|---|---|
| 칸 (`Cell`) | 정수 좌표 `x`·`y` 하나(구조체). `+`·`−`·`==`가 된다. 계산용이다. |
| 칸 키 (`Grid.Key`) | 칸을 글자로 쓴 것 `"x,y"`(예 `"3,-2"`). 딕셔너리 키·저장용. `Grid.TryParse`로 되돌린다. |
| Manhattan · Chebyshev (`GridMetric`) | 거리 재는 방식. Manhattan = 상하좌우 4방향으로 센 거리(범위가 마름모), Chebyshev = 대각선도 한 걸음(범위가 정사각형). |
| 비용 함수 | `Func<Cell, int>`: 그 칸에 **들어가는** 데 드는 이동력. 음수면 못 들어간다(벽·물·맵 밖·적). |
| 막힘 함수 | `Func<Cell, bool>`: 그 칸이 시야를 막으면 참. |
| 이동력 (budget) | 한 번에 쓸 수 있는 비용의 합. |
| A* | 목표까지 남은 어림 거리를 보며 가장 싼 길부터 넓혀 가는 길찾기 방법. `Grid.Path`가 쓴다. |
| Bresenham | 두 칸 사이의 직선을 칸들로 근사하는 방법. `Grid.Line`이 쓴다. |
| 결정론 | 같은 입력이면 늘 같은 결과. 비용이 같은 길이 여럿이어도 늘 같은 길을 고른다. |
| 맵 | 칸마다의 지형. 바뀌지 않는 맵은 원본 인스턴스(DB › 인스턴스)의 `rows`(글자 목록, 글자 하나 = 칸 하나), 판에서 바뀌는 칸은 플레이어 데이터의 딕셔너리(칸 키 → 값). |
| 큰 수 (`BigNumber`) | 1e300 아래는 double 값 그대로(`m` = 값, `e` = 0), 그 위는 가수 `m`(1 ≤ \|m\| < 10, double) × 10^`e`(지수, long). `[Serializable]` 구조체. |
| 유효 숫자 | 정확히 담기는 자릿수. 1e300 아래는 double과 같다(정수는 2^53까지 정확). 그 위는 가수가 double이라 약 15자리다. |
| 가격 곡선 | 곡선을 붙인 밸런스 값(30장)에서 단계 = 이미 산 개수인 것. |

---

## 1. 무엇으로 만드나

| 기획 문장 | 만드는 것 |
|---|---|
| "이동력 4, 숲은 2칸 몫, 물·벽은 못 감, 적은 못 지나감" | `Grid.Reachable(시작, 4, 비용 함수)`(5-1) |
| "고른 칸까지 걸어간다(벽을 돌아서)" | `Grid.Path`(5-1) |
| "창은 앞 2칸 관통, 활은 2~4칸, 화염구는 반지름 1, 브레스는 앞쪽 삼각형" | `Grid.Ray` · `Grid.Area(…, minRadius)` · `Grid.Cone`(5-2) |
| "벽 너머는 못 쏜다" | `Grid.Visible`(5-3) |
| "밀쳐 내기 1칸, 벽·다른 유닛에 부딪히면 피해 +2" | `Grid.Direction` + 지형·유닛 검사(5-4) |
| "범위 모양을 바라보는 쪽으로 돌린다(90°씩)" | `Grid.Rotate`(2-1) |
| "조커 오른쪽 칸", "가방 맨 아래 줄", "석판 무늬" | 37장 `Placement` |
| "방향키로 한 칸, 몬스터가 다가온다, 본 칸만 밝다" | `Grid.Path`의 첫 칸 · `Grid.Area` + `Visible`(5-5) |
| "밭 칸마다 작물, 물뿌리개 3×3" | 칸 키 딕셔너리(`Grid.Key`) · `Grid.Area(…, Chebyshev)`(5-6) |
| "쿠키 1e60개, 초당 생산" | `BigNumber` 필드(5-7) |
| "건물 가격 ×1.15씩, 10개 사기·살 수 있는 만큼" | 밸런스 값 곡선 + `Game.BalanceSum` · `Game.BalanceCount`(5-7) |

> **규칙: 맵은 데이터, `Grid`는 계산.** 맵 모양은 원본 인스턴스(`rows`)나 판 데이터에 두고, `Grid` 함수에는 그 맵을 읽는 비용·막힘 함수를 넘긴다. 같은 규칙 함수(`Board.MoveRange`)를 행동도 화면(범위 칠하기)도 부른다. 화면 코드에 계산을 복사하지 않는다.

> **규칙: 플레이어 데이터에는 `x`·`y`, 계산은 `Cell`.** 조건·binding-path·CSV가 숫자 필드로 읽는다. 클래스에 `public Cell At => new Cell(x, y);`(속성이라 저장되지 않는다)를 두면 계산이 짧아진다.

> **규칙: 큰 수는 필요한 필드에만.** 쿠키 수·생산량처럼 1e308을 넘을 수 있는 값만 `BigNumber`로 둔다. 레벨·개수·체력은 그대로 `int`·`double`이다.

---

## 2. 동작 규칙

### 2-1. 칸 · 방향 · 칸 키

- `Cell`은 값 구조체다. `a + b`·`a - b`는 칸끼리 더하고 뺀다(`at + Grid.Right`). `==`는 x·y가 모두 같을 때 참이다. 딕셔너리 키·`HashSet`에 그대로 쓴다.
- 방향 상수: `Grid.Up` (0, 1) · `Grid.Down` (0, −1) · `Grid.Left` (−1, 0) · `Grid.Right` (1, 0). `Up`은 "y + 1"일 뿐이다. 화면에서 어느 쪽이 위인지는 그리는 쪽이 정한다(3-1).
- `Grid.Direction(from, to)`: 가장 가까운 4방향 하나. 가로 차이가 세로 차이보다 크거나 **같으면** 가로(`(4, 4)` → `Right`), 같은 칸이면 `(0, 0)`이다.
- `Grid.Rotate(상대 좌표, 회전 수)`: 상대 좌표를 시계 방향으로 90°씩 돌린다. Grid 좌표는 y가 위(+)라고 보고 돈다: 회전 1이면 오른쪽 (1, 0) → 아래 (0, −1), 아래 → 왼쪽, 왼쪽 → 위. 음수는 반시계, 4는 0과 같다. 무늬(`Area`·직접 적은 상대 칸 목록)를 바라보는 쪽에 맞출 때 쓴다.
- `rows` 맵처럼 y가 아래로 늘어나게 그리면(3-1) 화면에서는 `Grid.Rotate`가 반시계로 보인다. 그때는 회전 수에 −를 붙인다. 목록 칸(37장)은 화면 기준의 `Placement.Rotate`를 쓴다.
- `Grid.Key(x, y)` = `"x,y"`(공백 없음, 어느 나라 설정에서도 같다). `Cell.ToString()`도 같다. `Grid.TryParse(키, out cell)`은 `"3,-2"` → (3, −2)이고, 쉼표가 없거나 정수가 아니면 거짓이다.

### 2-2. 거리 · 이웃

| 함수 | Manhattan(기본) | Chebyshev |
|---|---|---|
| `Distance(a, b, metric)` | 가로 차 + 세로 차 | 둘 중 큰 것 |
| (0,0) → (3,2) | 5 | 3 |

- `Neighbors(cell, diagonal)`: 4방향이면 오른쪽 · 위 · 왼쪽 · 아래 순, 대각선을 넣으면 오른쪽부터 반시계로 8칸. **맵 밖도 그대로 준다**(맵을 모른다). 거르는 것은 비용 함수·맵 쪽이다.

### 2-3. 이동 범위 — `Reachable`

`Grid.Reachable(시작, 이동력, 비용 함수, diagonal = false)` → `Dictionary<Cell, int>`(갈 수 있는 칸 → 남은 이동력).

1. 시작 칸은 남은 이동력 = 이동력으로 들어 있다(시작 칸의 비용은 세지 않는다).
2. 싼 칸부터 넓혀 가며 이웃 칸마다 비용 함수를 부른다. 음수면 못 들어간다. 쓴 비용이 이동력을 넘으면 넣지 않는다.
3. 한 칸에 여러 길로 갈 수 있으면 가장 많이 남는 길의 값이 남는다.

- 비용 0인 칸(길·다리)도 된다. 대각선을 넣으면 대각선 한 걸음도 그 칸의 비용 그대로다(더 비싸지 않다).
- 결과는 **지나갈 수 있는** 칸 모두다. 아군이 선 칸처럼 지나갈 수는 있지만 멈출 수 없는 칸은 결과에서 직접 뺀다(5-1).
- 비용 함수는 들어가는 칸만 받는다(어디서 오는지는 모른다). 그래서 "벽 모서리를 대각선으로 빠져나가지 못한다"는 이 함수로 막을 수 없다 → 대각선 이동이 그래야 하면 4방향으로 하거나 이웃을 직접 거른다.

### 2-4. 길찾기 — `Path`

`Grid.Path(시작, 목표, 비용 함수, diagonal = false, maxCost = 0, minCost = 0)` → `List<Cell>`.

- A*로 가장 싼 길을 찾는다(`Reachable`과 같은 비용 — `minCost`가 모든 칸 비용 이하일 때, 기본 0이면 늘). 돌려주는 목록은 **시작 칸을 뺀** 칸들의 차례이고 마지막이 목표 칸이다. `path[0]`이 다음 걸음이다.
- 갈 수 없으면 빈 목록이다. 시작 칸이 목표면 빈 목록이다.
- **목표 칸의 비용도 센다.** 목표 칸이 음수면(적이 선 칸) 길이 없다 → 적에게 다가가는 길은 비용 함수가 목표 칸만 들어갈 수 있게 한다(5-5).
- `maxCost`를 주면 그보다 비싼 길은 찾지 않는다(0 = 제한 없음). 찾다가 10만 번을 넘으면 그만두고 빈 목록이다 → 비용 함수는 **맵 밖에서 음수**를 돌려줘야 한다. 그렇지 않으면 갈 수 없는 목표를 찾을 때 맵 밖으로 끝없이 퍼진다.
- 결정론: 비용이 같은 길이 여럿이면 늘 같은 길이다(넓혀 간 차례가 늘 같다).
- **`minCost`(들어갈 수 있는 칸 비용의 최솟값)**: 목표까지의 어림 = 남은 칸 수(대각선이면 Chebyshev, 아니면 Manhattan) × `minCost`라 실제로 남은 비용을 넘지 않는다. 기본 0이면 어림 없이 싼 칸부터 넓혀, 비용 0인 칸(길·다리)이 있어도 늘 가장 싼 길이다. 모든 칸이 1 이상인 맵은 `minCost: 1`을 주면 목표 쪽으로만 넓혀 더 빨리 찾는다. **`minCost`보다 싼 칸이 있으면 가장 싼 길이 아닐 수 있다** — 아직 보지 않은 싼 칸은 미리 알 수 없다. 찾다가 만난 칸이 더 싸면 그때부터 그 비용으로 어림을 낮추지만, 덜 빗나가게 할 뿐 보장은 아니다. 그러니 맵의 가장 싼 칸 비용보다 크게 주지 않는다.

### 2-5. 직선 · 시야 — `Line` · `Visible`

- `Grid.Line(a, b)`: Bresenham으로 a에서 b까지의 칸들(양 끝 포함).
- `Grid.Visible(a, b, 막힘 함수)`: a와 b **사이**(양 끝 뺀) 칸 중 막는 칸이 없으면 참. 붙은 칸은 늘 보인다. 대상이 벽 칸 위에 있어도 대상 칸은 보지 않으므로 보인다.
- **대칭이다.** 늘 같은 쪽 끝(x가 작은 쪽, 같으면 y가 작은 쪽)에서 긋고 필요하면 뒤집으므로, `Line(b, a)`는 `Line(a, b)`를 거꾸로 한 것이다. 그래서 A가 B를 보면 B도 A를 본다.
- 직선이 대각선으로 두 벽 사이를 지나가면 막지 않는다(한 칸씩 대각선으로 넘어간다).

### 2-6. 범위 모양 — `Area` · `Ray` · `Cone`

| 함수 | 모양 | 칸 수(예) |
|---|---|---|
| `Area(center, radius, Manhattan)` | 마름모(중심 포함) | 반지름 2 → 13칸 |
| `Area(center, radius, Chebyshev)` | 정사각형 | 반지름 1 → 9칸(3×3) |
| `Area(…, minRadius: 2)` | 가운데를 비운 링(거리 minRadius~radius) | 반지름 4, 최소 2 → 활 |
| `Ray(origin, dir, length)` | origin 다음 칸부터 dir로 length칸(관통·돌진) | 3 → 3칸 |
| `Cone(origin, dir, length)` | dir 쪽 앞 삼각형: 거리 d에서 좌우로 d−1칸씩 | 3 → 1 + 3 + 5 = 9칸 |

- 모두 **모양만** 준다(맵을 모른다). 벽에서 멈추거나 맵 밖을 빼는 것은 쓰는 쪽이다(`Ray(…).TakeWhile(c => !벽(c))`).
- `Area`는 아래 줄(y 작은 쪽)부터 왼쪽에서 오른쪽으로 준다. 순서가 늘 같다.
- `Cone`의 dir은 4방향 하나(`Grid.Up` … 또는 `Direction`의 값)다. 대각선을 넣으면 모양이 비뚤어진다.

### 2-7. 큰 수 — `BigNumber`

- **담는 모양**: 1e300 아래는 double 값 그대로 담는다(`m` = 값, `e` = 0 — `new BigNumber(1234)` → m 1234, e 0). 그 위는 가수(1 ≤ |m| < 10) × 10^`e`다(`new BigNumber(1.5, 400)` → m 1.5, e 400). 0은 m 0, e 0이다. `m`·`e`를 직접 고치지 않는다. 예전 방식으로 저장된 값(작은 값도 가수 × 10^지수)도 셈·비교할 때 맞춘다.
- `double`·`int`·`long`에서 **저절로** 바뀐다(`BigNumber x = 5;`, `run.cookies += 0.1`). 거꾸로는 명시해야 한다: `(double)x` 또는 `x.ToDouble()` — 1e308을 넘으면 ±무한대.
- 연산자: `+` `−` `×` `÷`, 부호 `−x`, 비교 `==` `!=` `<` `>` `<=` `>=`(`CompareTo`·`Equals`도 같다).
- **정밀도**: 1e300 아래는 double 셈 그대로라 오차가 쌓이지 않는다 — 1을 49번 더하면 정확히 49, 0.5씩 모아도 정확하고, 정수는 2^53(약 9e15)까지 정확하다(그보다 크면 double처럼 끝자리가 둥글어진다 — 쿠키 1e30개에 1을 더해도 그대로). 1e300 위는 가수가 double이라 유효 숫자 약 15자리이고, 더하기·빼기는 지수 차이가 17자리를 넘으면 작은 쪽을 버린다: 1.5e400 + 1 = 1.5e400.
- 무한대·NaN은 담을 수 없다. 넘치는 double 계산(1e308을 넘은 가격 합, `1.0 / 0`)을 넣으면 `ArgumentOutOfRangeException`("[MomoiProject] 큰 수: 무한·NaN은 담을 수 없습니다."). 0으로 나누면 `DivideByZeroException`("[MomoiProject] 큰 수: 0으로 나눌 수 없습니다.").
- `Pow(값, 지수)`: 작은 수는 double의 거듭제곱 그대로다(2^3 = 8, 5^2 = 25 정확히 — 넘치면 가수 × 10^지수로). 음수의 거듭제곱은 예외("[MomoiProject] 큰 수: 음수의 거듭제곱은 지원하지 않습니다."). 0^0 = 1, 0^p = 0.
- `Floor`: 1e300 아래는 소수를 버린다(음의 무한대 쪽, −1.5 → −2). 그 위는 그대로(소수가 없다).
- `[Serializable]` 구조체라 플레이어 데이터 필드·목록(`List<BigNumber>`)에 그냥 둔다. 판 저장·Undo·Mirror(Sync)가 다른 필드처럼 따라온다.
- **`Game.Pay`와 DB의 숫자 칸은 double 숫자 필드만 안다.** `Game.Pay(키, "cookies", …)`는 "[MomoiProject] 비용: 'run › cookies'는 숫자 필드가 아닙니다(BigNumber)."로 프로그래머 실수가 된다. DB Block의 숫자 비교·증감도 큰 수 필드에 쓰지 않는다. 큰 수의 비용은 코드로 검사하고 뺀다: `if (run.cookies < price) throw Game.Fail(…); run.cookies -= price;`(5-7).

### 2-8. 큰 수를 글로 — `ToString` · `Parse`

| 형식 | 쓰는 법 | 예 |
|---|---|---|
| 기본(짧게) | `x.ToString()` · binding-path · 문장 틀 `{cookies}` | `999` · `1.23K` · `45.6M` · `4.56Qa` … `999Dc`, 그 위(1e36부터)는 `1e36` |
| `"sci"` | `x.ToString("sci")` · `$"{x:sci}"` | `1.23e45`(지수가 −2~2면 `12.5`처럼 그대로). 반올림이 10에 닿으면 지수를 올린다(9.996e5 → `1e6`) |
| `"ko"` | `x.ToString("ko")` · `$"{x:ko}"` | `1.23만` · `1.23억` · `5000만` … `극`(1e48)까지, 그 위는 지수 |
| `"full"` | `x.ToString("full")` | `1,234,567`(1e15 아래. 소수는 0 쪽으로 버린다 — −0.004 → `0`), 그 위는 지수 |

- 짧은 단위: K(천) · M(백만) · B(십억) · T(조) · Qa · Qi · Sx · Sp · Oc · No · Dc(1e33). 숫자는 10 아래면 소수 둘째 자리까지(`1.23K`, `2K`), 100 아래면 첫째 자리까지(`45.6M`), 그 위는 정수(`456B`)다. 1000 아래는 그 수 그대로다(소수 둘째 자리까지).
- 한국어 단위: 만 · 억 · 조 · 경 · 해 · 자 · 양 · 구 · 간 · 정 · 재 · 극.
- **반올림이 단위 하나를 채우면 다음 단위로** 올린다: 999,600은 `1000K`가 아니라 `1M`, 한국어 99,999,999는 `1억`. 음수도 같은 자릿수로 반올림한다(−155,500 → `-156K`).
- 형식은 하나만 줘도 된다(`x.ToString("ko")` = `x.ToString("ko", null)`). 문자열 보간의 형식(`$"{x:ko}"`)도 같다.
- binding-path(`run:cookies`, 35장)와 문장 틀의 값(`Texts.Get("hud.cookies", ("count", run.cookies))`, `Game.Fail`의 값)은 기본 형식이다.
- `BigNumber.Parse(글)`: `"1.5e40"`(e·E) · 짧은 단위(`"3.2K"`) · 숫자(`"12345"`)를 읽는다. double로 담기는 크기는 글자 그대로 읽는다(`"1.13e2"` → 정확히 113). 비거나 공백이면 0. 한국어 단위(`"1.23억"`)는 읽지 못한다(`FormatException`).
- binding-path의 **숫자** 칸(ProgressBar·Slider)과 컴포넌트 '데이터 연결'의 `onNumber`는 double로 바꿀 수 있는 값만 읽어 큰 수는 0이다. 막대의 비율은 화면 코드에서 `(float)(a / b).ToDouble()`로 계산한다.

### 2-9. 가격 곡선의 합 · 개수 — `BalanceSum` · `BalanceCount`

- `Game.BalanceSum(ID, from, count)`: 단계 `from`, `from + 1`, … 의 값 `count`개를 더한다. "10개 사기" = `BalanceSum(가격, 가진 수, 10)`.
- `Game.BalanceCount(ID, from, budget)`: `budget`으로 단계 `from`부터 차례로 몇 개를 살 수 있나("살 수 있는 만큼"). 다음 하나를 더하면 넘을 때 멈춘다. 많아야 10000개까지 센다.
- 둘 다 double이다(밸런스 값이 double). 큰 수 예산은 `run.cookies.ToDouble()`로 넘긴다. 가격 자체가 1e308을 넘을 만큼 오르면 곡선 대신 `BigNumber.Pow`로 계산한다(5-7).
- 곡선의 반올림(30장 2-2)은 단계마다 먼저 적용된 뒤 더한다.
- 가격이 0 이하(또는 숫자가 아님)가 되는 단계에서 멈춘다(그 단계부터는 사지 못하고 더하지 않는다, 30장 4-1).

---

## 3. 에디터에서 쓰는 법

### 3-1. 맵 원본 — DB › 인스턴스의 `rows`

1. 맵 클래스를 코드로 쓴다(`Assets/MomoiProject/Code/Rules/Board.cs`, 5절 앞).

```csharp
[Serializable] public class MapData { public List<string> rows = new List<string>(); }
```

2. `MomoiProject → DB` → **인스턴스** → **+ 새로 만들기** → ID `forestMap` → **instanceType** `MapData`.
3. `rows`에 줄을 하나씩 더하고 글자로 맵을 그린다. 글자 하나가 칸 하나다.

```
#########
#..**...#
#..#~~..#
#..#....#
#.......#
#########
```

| 글자 | 칸 | 비용 | 시야 |
|---|---|---|---|
| `.` | 땅 | 1 | 보인다 |
| `*` | 숲 | 2 | 보인다 |
| `~` | 물 | 못 감 | 보인다 |
| `#` | 벽(맵 밖도 벽으로 본다) | 못 감 | 막는다 |
| `=` | 밭(농장, 5-6) | 1 | 보인다 |

4. 저장(Ctrl+S). `GameIds.Instance.ForestMap`이 생긴다. 판 데이터에는 맵의 ID만 둔다(`battle.map = GameIds.Instance.ForestMap`).

- **좌표**: 칸 (x, y)의 글자 = `rows[y][x]`. y = 0이 첫 줄이다. `Grid.Up`(y + 1)은 rows로 보면 **다음 줄**(아래)이다. Tilemap에 그릴 때는 `new Vector3Int(x, -y, 0)`처럼 y를 뒤집어 그리면 글자 모양 그대로 보인다.
- 줄 길이가 달라도 된다. 짧은 줄의 오른쪽은 맵 밖(벽)으로 본다(5절의 `Board.TileAt`).
- 글자의 뜻(비용·시야)은 코드 한 곳(`Board.TerrainCost`·`BlocksSight`)에 둔다. 기획자가 비용을 고치게 하려면 글자마다 밸런스 값을 두고 읽는다(`Game.Balance("forestCost")`).
- **판에서 바뀌는 칸**(부서진 벽·연 문·갈아 둔 밭)은 원본을 고치지 않는다(원본은 읽기만, 04장 2.7). 판 데이터에 `Dictionary<string, char>`(칸 키 → 바뀐 글자)를 두고, `TileAt`이 그것을 먼저 본다.
- 맵이 아주 크거나 Tilemap에서 그리고 싶으면 Tilemap → `rows`로 굽는 에디터 메뉴를 프로젝트가 만든다(DB 추가 메뉴, 관례 15절).

### 3-2. 맵을 CSV로 — 시트에서 고치기

맵 원본도 인스턴스라 33장의 CSV로 나간다.

1. DB › 인스턴스 → 맨 위 폴더 줄 **CSV** → **MapData 내보내기…**.
2. 한 줄이 맵 하나이고, 칸 `rows[0]` · `rows[1]` … 이 맵의 줄이다(시트에서는 옆으로 늘어선다).

```
id,rows[0],rows[1],rows[2],rows[3],rows[4],rows[5]
forestMap,#########,#..**...#,#..#~~..#,#..#....#,#.......#,#########
caveMap,#####,#...#,#.#.#,#####,,
```

3. 고친 뒤 **MapData 가져오기…** → 적용 전 → Ctrl+S.

- 줄 수가 다른 맵끼리는 짧은 맵의 남는 칸이 빈칸이다. 가져올 때 목록 길이는 값이 있는 마지막 번호까지다(33장 2-3). 맵의 줄을 줄이려면 뒤쪽 칸을 비운다.
- 글자 칸은 앞뒤 공백까지 그대로 읽는다. 공백을 땅 글자로 쓰지 않는다(시트가 잘라 낼 수 있다).
- 줄이 `=` · `+` · `-` · `@`로 시작하면 시트가 수식으로 읽는다. 맵 테두리를 벽(`#`)으로 두면 생기지 않는다.
- 쉼표·큰따옴표는 CSV에서 따옴표로 감싸여 나간다. 칸 글자로 쓰지 않는 편이 시트에서 보기 쉽다.

### 3-3. 모니터에서 확인하기

- **데이터** 탭: 개체의 `x`·`y`, 칸 딕셔너리(키 `"3,4"`)가 보인다. 큰 수 필드는 구조체라 `m`·`e` 두 필드로 보인다(1e300 아래는 m = 값·e = 0 — 1234 = m 1234, e 0. 그 위는 가수·지수 — 1.5e400 = m 1.5, e 400).
- 큰 수에는 DB › 개발 명령의 '플레이어 데이터에 주기'를 쓸 수 없다(double 숫자 칸). 개발 명령 항목을 만들고 그 ID에 `[BindTo]`로 코드를 잇는다.

```csharp
[BindTo(GameIds.DebugCommand.CookiesX1000)]
static void CookiesX1000(TriggerContext c) => c.Game.Data().Run.cookies *= 1000;   // 개발 명령도 행동 하나(Undo된다)
```

---

## 4. 코드로 쓰는 법 — 문법 요소를 하나씩

```csharp
Func<Cell, int> cost = c => Board.TerrainCost(Board.TileAt(map, c));                          // 들어가는 비용(음수 = 못 감)
var range = Grid.Reachable(unit.At, unit.move, cost);                                          // 칸 → 남은 이동력
var path = Grid.Path(unit.At, goal, cost);                                                     // 시작 칸 뺀 차례, 없으면 빈 목록
bool see = Grid.Visible(a, b, c => Board.BlocksSight(Board.TileAt(map, c)));                   // 사이 칸이 막지 않으면 참
foreach (var c in Grid.Area(center, 2)) { … }                                                  // 마름모 범위
run.cookies += production * seconds;                                                           // 큰 수: 연산자는 평소처럼
if (run.cookies < price) throw Game.Fail("fail.noCookies", "쿠키가 모자랍니다.");            // 비용은 코드로
```

### 4-1. `Cell`

| 멤버 | 뜻 |
|---|---|
| `int x` · `int y` | 좌표 |
| `new Cell(x, y)` | 만든다 |
| `a + b` · `a - b` | 칸끼리 더하기·빼기(`at + Grid.Right`, `b - a` = 차이) |
| `a == b` · `a != b` · `Equals` · `GetHashCode` | x·y가 같은지. 딕셔너리 키·`HashSet`에 그대로 쓴다 |
| `ToString()` | `Grid.Key`와 같은 `"x,y"` |

### 4-2. `GridMetric`

| 값 | 뜻 |
|---|---|
| `Manhattan` | 상하좌우 4방향 거리(가로 차 + 세로 차). 범위가 마름모. 기본 |
| `Chebyshev` | 대각선 포함 8방향 거리(둘 중 큰 차). 범위가 정사각형 |

### 4-3. `Grid`

| 멤버 | 뜻 |
|---|---|
| `Cell Up` · `Down` · `Left` · `Right` | (0, 1) · (0, −1) · (−1, 0) · (1, 0) |
| `string Key(int x, int y)` · `Key(Cell cell)` | `"x,y"`(딕셔너리 키) |
| `bool TryParse(string key, out Cell cell)` | `"x,y"` → 칸. 틀린 글이면 거짓 |
| `int Distance(Cell a, Cell b, GridMetric metric = Manhattan)` | 거리(2-2) |
| `IEnumerable<Cell> Neighbors(Cell cell, bool diagonal = false)` | 이웃 4칸(대각선이면 8칸). 맵 밖도 준다 |
| `Cell Direction(Cell from, Cell to)` | 가장 가까운 4방향(같으면 가로). 같은 칸이면 (0, 0) |
| `Cell Rotate(Cell offset, int turns)` | 상대 좌표를 시계 방향으로 90° × `turns`(y 위 + 기준, 2-1). 목록 칸은 `Placement.Rotate`(37장) |
| `Dictionary<Cell, int> Reachable(Cell start, int budget, Func<Cell, int> cost, bool diagonal = false)` | 갈 수 있는 칸 → 남은 이동력(시작 칸 포함). `cost`가 음수면 못 들어간다(2-3) |
| `List<Cell> Path(Cell start, Cell goal, Func<Cell, int> cost, bool diagonal = false, int maxCost = 0, int minCost = 0)` | A* 길. 시작 칸을 뺀 차례(마지막 = 목표). 없으면 빈 목록. `maxCost` 0 = 제한 없음. `minCost` = 들어갈 수 있는 칸 비용의 최솟값(기본 0 — 늘 가장 싼 길. 주면 빨라지고, 그보다 싼 칸이 있으면 가장 싼 길이 아닐 수 있다, 2-4) |
| `List<Cell> Line(Cell a, Cell b)` | 직선의 칸들(양 끝 포함, Bresenham). `Line(b, a)`는 이것을 거꾸로 한 것(대칭) |
| `bool Visible(Cell a, Cell b, Func<Cell, bool> blocks)` | 사이 칸(양 끝 뺀)이 막지 않으면 참. a ↔ b 대칭 |
| `IEnumerable<Cell> Area(Cell center, int radius, GridMetric metric = Manhattan, int minRadius = 0)` | 거리 minRadius~radius인 칸들(2-6) |
| `IEnumerable<Cell> Ray(Cell origin, Cell direction, int length)` | origin 다음 칸부터 direction으로 length칸 |
| `IEnumerable<Cell> Cone(Cell origin, Cell direction, int length)` | direction(4방향) 쪽 앞 삼각형 |

- 모두 static이고 상태가 없다. 행동 안에서도 화면 코드에서도 같은 결과다.
- 비용·막힘 함수는 칸마다 여러 번 불린다. 그 안에서 `Game.Find`·`Game.All`을 부르지 않는다(부를 때마다 플레이어 데이터를 훑는다). 개체가 선 칸은 먼저 한 번 사전으로 만든다(5-1의 `Board.Occupants`).

### 4-4. `BigNumber`

| 멤버 | 뜻 |
|---|---|
| `double m` · `long e` | 1e300 아래는 값 · 0, 그 위는 가수(1 ≤ \|m\| < 10) · 10의 지수. 직접 고치지 않는다 |
| `new BigNumber(double value)` · `new BigNumber(double mantissa, long exponent)` | 만든다(`new BigNumber(1.5, 400)` = 1.5e400). 무한대·NaN이면 예외 |
| `BigNumber.Zero` · `BigNumber.One` | 0 · 1 |
| `bool IsZero` · `int Sign` | 0인지 · 부호(−1, 0, 1) |
| 저절로 바뀜 `double` · `int` · `long` → `BigNumber` | `BigNumber x = 5;` |
| 명시 `(double)x` · `double ToDouble()` | double로. 1e308을 넘으면 ±무한대 |
| `+` `−` `×` `÷` · 부호 `−` · `==` `!=` `<` `>` `<=` `>=` · `CompareTo` · `Equals` | 연산·비교(2-7) |
| `double Log10()` | 상용로그(절댓값의). 정수 부분 = 자릿수 − 1(1234 → 3.09). 0이면 음의 무한대 |
| `static BigNumber Pow(BigNumber value, double power)` | 거듭제곱(작은 수는 double 그대로 — 2^3 = 8). 음수의 거듭제곱은 예외 |
| `static BigNumber Floor(BigNumber value)` | 내림(1e300 위는 그대로) |
| `static BigNumber Max(a, b)` · `Min(a, b)` | 큰 것 · 작은 것 |
| `string ToString()` · `ToString(string format)` · `ToString(string format, IFormatProvider provider)` | 글로(2-8). 형식 `null`(짧게) · `"sci"` · `"ko"` · `"full"` |
| `static BigNumber Parse(string text)` | `"1.5e40"` · `"3.2K"` · `"12345"`를 읽는다 |

### 4-5. `Game.BalanceSum` · `Game.BalanceCount`

| 멤버 | 뜻 |
|---|---|
| `double BalanceSum(string id, double from, int count)` | 단계 `from`부터 `count`개(단계 1씩)의 값의 합. 값이 0 이하인 단계에서 멈춘다 |
| `int BalanceCount(string id, double from, double budget)` | `budget`으로 단계 `from`부터 살 수 있는 개수(최대 10000, 값이 0 이하인 단계 앞까지) |

`session.Balance.Sum(id, from, count)` · `session.Balance.MaxCount(id, from, budget, limit = 10000)`은 같은 일의 세션 쪽 이름이다(`limit`을 고를 수 있다). 값을 바꾸지 않으므로 화면 코드에서도 부른다(가격 표시).

---

## 5. 상황별 예제

5-1~5-6은 아래 규칙 파일을 함께 쓴다(`Assets/MomoiProject/Code/Rules/Board.cs`). 피해는 프로젝트의 피해 규칙 함수(`Rules.Damage`, 가로채기 자리가 있는 것, 28장 5-3)를 부른다고 본다. 밸런스 값 ID 상수(`GameIds.Balance.SkillPower` · `BowPower` · `HeroAttack` …)는 DB › 밸런스 값에 항목을 만들고 저장하면 생긴다.

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using GBible;
using GBible.Ids;

// 맵 원본(DB › 인스턴스, 3-1). 글자 하나 = 칸 하나, rows[y][x].
[Serializable] public class MapData { public List<string> rows = new List<string>(); }

[Serializable]
public class Unit
{
    public string id;
    public string side;                  // "ally" · "enemy"
    public int x, y;                     // 칸(플레이어 데이터에는 x·y)
    public int hp = 20, move = 4;
    public Cell At => new Cell(x, y);    // 계산용(속성이라 저장되지 않는다)
}

// 판 데이터: RunState에 public Battle battle; 을 둔다
[Serializable] public class Battle { public string map; public List<Unit> units = new List<Unit>(); }

public static class Board
{
    public static MapData MapOf(GameApi game, string id) =>
        game.Session.Instances?.Invoke(id) as MapData ?? throw new InvalidOperationException($"없는 맵 '{id}'입니다(DB › 인스턴스).");

    // 맵 밖은 벽: 비용 함수가 음수를 돌려줘 길찾기가 맵 밖으로 퍼지지 않는다(2-4).
    public static char TileAt(MapData map, Cell c) =>
        c.y < 0 || c.y >= map.rows.Count || c.x < 0 || c.x >= map.rows[c.y].Length ? '#' : map.rows[c.y][c.x];

    // 들어가는 비용: 땅·밭 1 · 숲 2 · 물·벽은 못 감(음수)
    public static int TerrainCost(char tile)
    {
        switch (tile)
        {
            case '.':
            case '=': return 1;
            case '*': return 2;
            default: return -1;
        }
    }

    public static bool BlocksSight(char tile) => tile == '#';

    // 칸 → 서 있는 유닛. 비용 함수가 칸마다 Game.Find·All을 부르지 않게 먼저 한 번 만든다.
    public static Dictionary<Cell, Unit> Occupants(Battle battle) =>
        battle.units.Where(u => u.hp > 0).ToDictionary(u => u.At);

    // 이동 비용: 지형 + 적이 선 칸은 못 지나간다(아군 칸은 지나간다).
    public static Func<Cell, int> MoveCost(MapData map, Dictionary<Cell, Unit> occupants, Unit mover) =>
        c => occupants.TryGetValue(c, out var other) && other.side != mover.side ? -1 : TerrainCost(TileAt(map, c));

    // 멈출 수 있는 칸 → 남은 이동력. 다른 유닛이 선 칸(지나온 아군 칸 포함)은 뺀다.
    public static Dictionary<Cell, int> MoveRange(MapData map, Battle battle, Unit mover)
    {
        var occupants = Occupants(battle);
        var range = Grid.Reachable(mover.At, mover.move, MoveCost(map, occupants, mover));
        foreach (var cell in occupants.Keys)
            if (cell != mover.At) range.Remove(cell);
        return range;
    }
}
```

### 5-1. SRPG — 이동 범위와 길

**규칙**

```csharp
[Serializable, GameEvent("유닛 이동")]
public class UnitMoved { public string unitId; public List<string> path = new List<string>(); }   // 지나간 칸 키(화면이 따라 걷는다)

[Serializable, BlockInfo("유닛 이동")]
public class MoveUnit : GameAction
{
    public Unit unit;            // 행동 필드는 객체(기록에는 id, 실행 직전에 지금 객체로 다시 찾는다)
    public int x, y;             // 고른 칸

    protected override void Run()
    {
        var battle = Game.Data().Run.battle;
        var map = Board.MapOf(Game, battle.map);
        var goal = new Cell(x, y);
        if (!Board.MoveRange(map, battle, unit).ContainsKey(goal))
            throw Game.Fail("fail.cantReach", "갈 수 없는 칸입니다.");
        var path = Grid.Path(unit.At, goal, Board.MoveCost(map, Board.Occupants(battle), unit), maxCost: unit.move);
        unit.x = goal.x;
        unit.y = goal.y;
        Game.Events.Publish(new UnitMoved { unitId = unit.id, path = path.Select(c => Grid.Key(c)).ToList() });
    }
}
```

**화면** — 고른 유닛의 이동 범위를 칠한다. 같은 규칙 함수를 부르고 읽기만 한다.

```csharp
var game = GBibleRuntime.Game;
var battle = game.Data().Run.battle;
var map = Board.MapOf(game, battle.map);
foreach (var pair in Board.MoveRange(map, battle, selected))                                                        // selected: 고른 유닛
    highlight.SetTile(new Vector3Int(pair.Key.x, -pair.Key.y, 0), pair.Value > 0 ? moveTile : lastStepTile);   // 남은 이동력 0 = 끝 칸

// 마우스를 올린 칸까지의 길 미리 보기
var preview = Grid.Path(selected.At, hovered, Board.MoveCost(map, Board.Occupants(battle), selected), maxCost: selected.move);
```

| 이동력 4, 숲 2 | 결과 |
|---|---|
| 땅만 4칸 | 갈 수 있다(남은 0) |
| 땅 2 + 숲 1 | 갈 수 있다(1 + 1 + 2 = 4) |
| 숲 2 + 땅 1 | 못 간다(2 + 2 + 1 = 5) |
| 아군이 선 칸 | 지나갈 수 있고 멈출 수 없다 |
| 적이 선 칸 · 적 뒤 | 못 들어간다 · 돌아서 간다 |

- 고른 칸이 범위 밖이면 행동이 실패하고 "갈 수 없는 칸입니다."가 뜬다. 화면은 `Game.CanDo(new MoveUnit { … }, out var why)`로 미리 막을 수 있다(33장).
- 제자리(시작 칸)를 고르면 길은 빈 목록이고 위치는 그대로다("대기").
- 같은 맵·같은 배치면 늘 같은 길이다. Undo한 뒤 다시 걸어도, 멀티플레이 상대 화면에서도 같은 칸을 지난다.

### 5-2. 공격 범위 — `Neighbors` · `Ray` · `Area` · `Cone`

| 무기 | 맞는 칸 | 코드 |
|---|---|---|
| 칼 | 붙은 4칸 | `Grid.Neighbors(at)` |
| 창 | 고른 쪽으로 직선 2칸(관통) | `Grid.Ray(at, Grid.Direction(at, aim), 2)` |
| 화염구 | 고른 칸 중심 반지름 1(5칸) | `Grid.Area(aim, 1)` |
| 브레스 | 고른 쪽 앞 삼각형 3칸 거리(9칸) | `Grid.Cone(at, Grid.Direction(at, aim), 3)` |
| 회오리 | 둘레 8칸 | `Grid.Area(at, 1, GridMetric.Chebyshev, minRadius: 1)` |
| 활(고를 수 있는 칸) | 거리 2~4 | `Grid.Area(at, 4, GridMetric.Manhattan, minRadius: 2)`(5-3) |

```csharp
// Board에 더한다: 무기마다 맞는 칸
public static IEnumerable<Cell> HitCells(string weapon, Unit user, Cell aim)
{
    var dir = Grid.Direction(user.At, aim);                      // 고른 칸 쪽 4방향
    switch (weapon)
    {
        case "spear": return Grid.Ray(user.At, dir, 2);
        case "fireball": return Grid.Area(aim, 1);
        case "breath": return Grid.Cone(user.At, dir, 3);
        case "whirl": return Grid.Area(user.At, 1, GridMetric.Chebyshev, minRadius: 1);
        default: return Grid.Neighbors(user.At);                 // 칼
    }
}

[Serializable, BlockInfo("범위 공격")]
public class AreaAttack : GameAction
{
    public Unit user;
    public string weapon;        // "sword" · "spear" · "fireball" · "breath" · "whirl"
    public int x, y;             // 고른 칸(방향·중심)

    protected override void Run()
    {
        var battle = Game.Data().Run.battle;
        var aim = new Cell(x, y);
        if (weapon == "fireball" && Grid.Distance(user.At, aim) > 4)
            throw Game.Fail("fail.tooFar", "{range}칸 안만 고를 수 있습니다.", ("range", 4));
        var occupants = Board.Occupants(battle);                 // 한 번 만들어 둔다
        var power = (int)Game.Balance(GameIds.Balance.SkillPower);
        foreach (var cell in Board.HitCells(weapon, user, aim))
            if (occupants.TryGetValue(cell, out var target) && target.side != user.side)
                Rules.Damage(Game, user, target, power);
    }
}
```

- 모양 함수는 맵을 모른다. 창이 벽을 뚫지 않게 하려면 `Grid.Ray(…).TakeWhile(c => Board.TerrainCost(Board.TileAt(map, c)) >= 0)`.
- 맞는 칸을 미리 칠할 때도 화면이 `Board.HitCells`를 부른다.
- 맞는 차례는 모양 함수의 차례(늘 같다)다. 처치 이벤트·로그도 같은 차례로 쌓인다.

### 5-3. 시야 — 벽 너머는 못 쏜다

```csharp
[Serializable, BlockInfo("활 쏘기")]
public class Shoot : GameAction
{
    public Unit user;
    public Unit target;

    protected override void Run()
    {
        var map = Board.MapOf(Game, Game.Data().Run.battle.map);
        var distance = Grid.Distance(user.At, target.At);
        if (distance < 2 || distance > 4)
            throw Game.Fail("fail.bowRange", "2~4칸 떨어진 대상만 쏠 수 있습니다.");
        if (!Grid.Visible(user.At, target.At, c => Board.BlocksSight(Board.TileAt(map, c))))
            throw Game.Fail("fail.noSight", "벽에 가려 보이지 않습니다.");
        Rules.Damage(Game, user, target, (int)Game.Balance(GameIds.Balance.BowPower));
    }
}
```

- 사이 칸만 본다. 물(`~`)은 시야를 막지 않으므로 물 너머로 쏠 수 있다.
- 다른 유닛도 가리게 하려면 막힘 함수에 더한다: `c => Board.BlocksSight(Board.TileAt(map, c)) || occupants.ContainsKey(c)`.
- 쏠 수 있는 적을 칠하는 화면은 적마다 `Game.CanDo(new Shoot { user = me, target = enemy }, out var why)`를 부른다(33장 5-4). 이유("벽에 가려 보이지 않습니다.")가 그대로 툴팁이 된다.
- 시야는 대칭이다. 쏘는 쪽이 보면 맞는 쪽도 쏘는 쪽을 본다(2-5).

### 5-4. 밀쳐 내기 — `Direction` + 막힘이면 추가 피해

"붙은 적을 1칸 민다. 벽·물·다른 유닛에 부딪히면 피해 +2."

**에디터**: DB › 밸런스 값 `shoveDamage` `1`, `shoveCrash` `2`.

```csharp
[Serializable, BlockInfo("밀쳐 내기")]
public class Shove : GameAction
{
    public Unit user;
    public Unit target;

    protected override void Run()
    {
        var battle = Game.Data().Run.battle;
        var map = Board.MapOf(Game, battle.map);
        if (Grid.Distance(user.At, target.At) != 1)
            throw Game.Fail("fail.notAdjacent", "붙어 있는 대상만 밀 수 있습니다.");
        var to = target.At + Grid.Direction(user.At, target.At);              // 미는 쪽 → 대상 쪽으로 한 칸
        var blocked = Board.TerrainCost(Board.TileAt(map, to)) < 0 || Board.Occupants(battle).ContainsKey(to);
        var damage = (int)Game.Balance(GameIds.Balance.ShoveDamage);
        if (blocked) damage += (int)Game.Balance(GameIds.Balance.ShoveCrash);  // 부딪힘
        else
        {
            target.x = to.x;
            target.y = to.y;
        }
        Rules.Damage(Game, user, target, damage);
    }
}
```

| 대상 뒤 칸 | 결과 |
|---|---|
| 땅·숲 | 1칸 밀리고 피해 1 |
| 벽·물·맵 밖 | 그 자리, 피해 1 + 2 |
| 다른 유닛 | 그 자리, 피해 1 + 2(부딪힌 유닛도 맞게 하려면 그 유닛에도 `Rules.Damage`) |

- 붙은 칸이라 `Direction`은 정확히 미는 쪽이다. 떨어진 대상을 밀 때(넉백 화살)도 같은 함수가 가장 가까운 4방향을 준다.
- 2칸 밀기: `foreach (var c in Grid.Ray(target.At, Grid.Direction(user.At, target.At), 2))` — 막힌 칸을 만나면 멈추고 부딪힘 피해, 아니면 그 칸으로 옮기고 다음 칸을 본다.
- 물에 빠뜨리는 규칙이면 물(`~`)을 막힘에서 빼고 따로 본다(`TileAt(map, to) == '~'` → 빠짐).
- 옮긴 뒤 피해를 준다. 피해 이벤트를 받는 쪽(가시 바닥 같은 칸 효과)이 새 칸을 본다.

### 5-5. 로그라이크 던전 — 한 칸씩, 몬스터 추적, 안개

```csharp
[Serializable]
public class Dungeon                                    // RunState에 public Dungeon dungeon; 을 둔다
{
    public string map;
    public Unit hero = new Unit { id = "hero", side = "ally" };
    public List<Unit> monsters = new List<Unit>();
    public Dictionary<string, bool> seen = new Dictionary<string, bool>();   // 본 칸(칸 키) — 안개
}

[Serializable, BlockInfo("한 칸 걷기")]
public class Step : GameAction
{
    public int dx, dy;                                  // 방향키: (1,0) · (−1,0) · (0,1) · (0,−1)

    protected override void Run()
    {
        var d = Game.Data().Run.dungeon;
        var map = Board.MapOf(Game, d.map);
        var to = d.hero.At + new Cell(dx, dy);
        var foe = d.monsters.FirstOrDefault(m => m.hp > 0 && m.At == to);
        if (foe != null) Rules.Damage(Game, d.hero, foe, (int)Game.Balance(GameIds.Balance.HeroAttack));   // 부딪히면 공격
        else if (Board.TerrainCost(Board.TileAt(map, to)) < 0) throw Game.Fail("fail.wall", "막혀 있습니다.");
        else
        {
            d.hero.x = to.x;
            d.hero.y = to.y;
        }
        MonstersTurn(d, map);                           // 한 번 움직이면 몬스터도 한 번
        Reveal(d, map);
    }

    void MonstersTurn(Dungeon d, MapData map)
    {
        bool Wall(Cell c) => Board.BlocksSight(Board.TileAt(map, c));
        foreach (var m in d.monsters.Where(m => m.hp > 0).ToList())                 // 목록 차례대로(늘 같다)
        {
            var distance = Grid.Distance(m.At, d.hero.At);
            if (distance == 1) { Rules.Damage(Game, m, d.hero, (int)Game.Balance(GameIds.Balance.MonsterAttack)); continue; }
            if (distance > 8 || !Grid.Visible(m.At, d.hero.At, Wall)) continue;    // 못 보면 가만히
            var taken = new HashSet<Cell>(d.monsters.Where(o => o != m && o.hp > 0).Select(o => o.At));
            var path = Grid.Path(m.At, d.hero.At,
                c => c == d.hero.At ? 1 : taken.Contains(c) ? -1 : Board.TerrainCost(Board.TileAt(map, c)),   // 목표 칸(주인공)은 들어갈 수 있게
                maxCost: 12, minCost: 1);                // 이 맵은 들어갈 수 있는 칸이 모두 1 이상
            if (path.Count > 1)
            {
                m.x = path[0].x;                        // 첫 칸만(마지막 칸은 주인공 자리)
                m.y = path[0].y;
            }
        }
    }

    void Reveal(Dungeon d, MapData map)
    {
        foreach (var c in Grid.Area(d.hero.At, 6, GridMetric.Chebyshev))
            if (Grid.Visible(d.hero.At, c, x => Board.BlocksSight(Board.TileAt(map, x))))
                d.seen[Grid.Key(c)] = true;
    }
}
```

- 걸음 하나 = 행동 하나 = Undo 한 칸. 주인공의 걸음·공격, 몬스터 차례, 안개가 한 행동이라 Undo하면 몬스터도 함께 돌아간다.
- 목표 칸(주인공 자리)을 비용 함수가 막으면 길이 없다(2-4). 목표 칸만 1로 둔다.
- 몬스터마다 `taken`을 다시 만드는 것은 앞의 몬스터가 이미 옮겼기 때문이다(같은 칸에 둘이 서지 않는다).
- `maxCost: 12`는 먼 길을 찾지 않게 하는 상한이다. 몬스터가 많아도 걸음 하나의 계산이 일정하다. 칸 비용이 모두 1 이상이라 `minCost: 1`로 목표 쪽으로만 넓힌다(2-4).
- 화면은 `d.seen.ContainsKey(Grid.Key(x, y))`인 칸만 그리고, 지금 보이는 칸(같은 `Area` + `Visible`)은 밝게 그린다.

### 5-6. 농장 — 칸 키 딕셔너리(`Grid.Key`)

밭 칸마다 작물이 있다. 칸 수만큼 객체를 미리 만들지 않고, 심은 칸만 딕셔너리에 둔다.

```csharp
[Serializable] public class Crop { public string seed; public int days; public bool watered; }   // seed = 씨앗 원본 ID
[Serializable] public class SeedData { public int growDays = 4; public string harvest; }          // 씨앗 원본(DB › 인스턴스)

[Serializable]
public class Farm                                       // RunState에 public Farm farm = new Farm(); 을 둔다
{
    public string map;                                  // 원본 맵(밭은 '=')
    public int canLevel;                                // 물뿌리개 단계: 0 = 한 칸, 1 = 3×3
    public Dictionary<string, Crop> crops = new Dictionary<string, Crop>();   // 칸 키 "x,y" → 작물
}
// RunState에 public Dictionary<string, int> seeds = new Dictionary<string, int>();  — 씨앗 원본 ID → 개수

[Serializable, BlockInfo("씨 심기")]
public class Plant : GameAction
{
    public int x, y;
    [DataRef(typeof(InstanceData))] public string seed;

    protected override void Run()
    {
        var run = Game.Data().Run;
        var key = Grid.Key(x, y);
        if (Board.TileAt(Board.MapOf(Game, run.farm.map), new Cell(x, y)) != '=')
            throw Game.Fail("fail.notField", "밭에만 심을 수 있습니다.");
        if (run.farm.crops.ContainsKey(key)) throw Game.Fail("fail.planted", "이미 심은 칸입니다.");
        Game.Pay(GameIds.PlayerData.Run.Id, $"seeds[{seed}]", 1, what: seed + ".name");   // 씨앗 하나(딕셔너리 경로 seeds[키])
        run.farm.crops[key] = new Crop { seed = seed };
    }
}

[Serializable, BlockInfo("물 주기")]
public class Water : GameAction
{
    public int x, y;                                    // 고른 칸(범위는 물뿌리개 단계 — 판 데이터에서 읽는다)

    protected override void Run()
    {
        var farm = Game.Data().Run.farm;
        foreach (var c in Grid.Area(new Cell(x, y), farm.canLevel, GridMetric.Chebyshev))
            if (farm.crops.TryGetValue(Grid.Key(c), out var crop)) crop.watered = true;
    }
}

// 하루 끝(EndDay 행동 안): 물 준 작물만 자란다. 난수처럼 차례가 결과를 바꾸는 일은 키를 정렬해 돈다.
var farm = Game.Data().Run.farm;
foreach (var key in farm.crops.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList())
{
    var crop = farm.crops[key];
    if (crop.watered) crop.days++;
    crop.watered = false;
    if (Game.Random("crow").Chance(0.02)) farm.crops.Remove(key);     // 까마귀가 하나를 먹는다
}
```

**화면** — 칸 키를 칸으로 되돌려 그린다.

```csharp
foreach (var pair in GBibleRuntime.Game.Data().Run.farm.crops)
    if (Grid.TryParse(pair.Key, out var c)) cropMap.SetTile(new Vector3Int(c.x, -c.y, 0), TileOf(pair.Value));
```

- 칸 키는 `Grid.Key`로만 만든다. 손으로 `$"{x}, {y}"`처럼 쓰면 공백 하나로 다른 키가 된다.
- 필드 경로로 칸 하나를 읽는다: 조건·binding-path `run:farm.crops[3,4].days`(`[ ]` 안은 키 그대로).
- 딕셔너리를 도는 차례에 기대지 않는다. 차례가 결과를 바꾸면(같은 난수 흐름을 칸마다 뽑기) 키를 정렬해 돈다. 그래야 Undo·저장·멀티에서 같다.
- 수확: `crop.days >= 원본.growDays`면 칸을 지우고 `harvest`를 준다. 다 자란 칸을 칠하는 것도 화면이 같은 비교로 읽는다.

### 5-7. 방치형 — 쿠키(`BigNumber`) + 가격 곡선(`BalanceSum` · `BalanceCount`)

**에디터**

- DB › 밸런스 값 `cursorPrice`: `value` `15`, 곡선 `비율로(×단계마다)`, `startX` `0`(단계 = 가진 수, 0개일 때 15), `rate` `1.15`, `rounding` `올림`(30장).
- DB › 인스턴스 `cursor`: `BuildingData`, `cps` `0.1`, `price` `cursorPrice`.
- DB › 시작 파일 › Bootstrap: 판 저장 "N초마다 저장(실시간·방치형)"(33장 5-7).

**규칙**

```csharp
// RunState에 더한다
public BigNumber cookies;                                                   // 큰 수: 연산자는 평소처럼
public Dictionary<string, int> buildings = new Dictionary<string, int>();   // 건물 원본 ID → 가진 수

[Serializable] public class BuildingData { public double cps; public string price; }   // 원본: 초당 생산 · 가격 곡선(밸런스 값 ID)

public static class Bakery
{
    public static BuildingData BuildingOf(GameApi game, string id) =>
        game.Session.Instances?.Invoke(id) as BuildingData ?? throw new InvalidOperationException($"없는 건물 '{id}'입니다.");

    // 초당 생산. 키를 정렬해 더한다(더하는 차례가 같아야 소수까지 같다).
    public static BigNumber Cps(GameApi game)
    {
        BigNumber cps = 0;
        var buildings = game.Data().Run.buildings;
        foreach (var id in buildings.Keys.OrderBy(k => k, StringComparer.Ordinal))
            cps += BuildingOf(game, id).cps * buildings[id];
        return cps;
    }
}

// 1초마다 굽는다(시간 예약으로 이어 간다. 판을 시작할 때 Game.ScheduleAfter(1, new Bake()) 한 번)
[Serializable, BlockInfo("굽기")]
public class Bake : GameAction
{
    protected override void Run()
    {
        Game.Data().Run.cookies += Bakery.Cps(Game);
        Game.ScheduleAfter(1, new Bake());
    }
}

[Serializable, BlockInfo("건물 사기")]
public class BuyBuilding : GameAction
{
    [DataRef(typeof(InstanceData))] public string building;
    public int count = 1;                               // 0 = 살 수 있는 만큼

    protected override void Run()
    {
        var run = Game.Data().Run;
        var data = Bakery.BuildingOf(Game, building);
        run.buildings.TryGetValue(building, out var owned);
        var n = count > 0 ? count : Math.Max(1, Game.BalanceCount(data.price, owned, run.cookies.ToDouble()));   // 하나도 못 사면 1개 값으로 실패 글
        BigNumber price = Game.BalanceSum(data.price, owned, n);                // 단계 owned부터 n개의 합(double → 저절로)
        if (run.cookies < price)                                                // Game.Pay는 큰 수를 모른다 → 코드로
            throw Game.Fail("fail.noCookies", "쿠키가 모자랍니다({have} / {need}).", ("have", run.cookies), ("need", price));
        run.cookies -= price;
        run.buildings[building] = owned + n;
    }
}
```

**화면**

```xml
<ui:Label binding-path="run:cookies@hud.cookies" />          <!-- 텍스트 hud.cookies = "쿠키 {value}" → "쿠키 1.23M" -->
```

```csharp
// 가격 표시(읽기만): 다음 하나, 10개
var game = GBibleRuntime.Game;
var run = game.Data().Run;
var owned = run.buildings.TryGetValue(GameIds.Instance.Cursor, out var have) ? have : 0;
BigNumber one = game.Balance(GameIds.Balance.CursorPrice, owned);
BigNumber ten = game.BalanceSum(GameIds.Balance.CursorPrice, owned, 10);
priceLabel.text = $"{one} · 10개 {ten}";                    // 가진 수 10이면 "61 · 10개 1.24K"
koreanLabel.text = $"{run.cookies:ko}개";                    // "1.23억개"
```

| 가진 수 | 다음 가격(15 × 1.15^n, 올림) | 10개 합(`BalanceSum`) |
|---|---|---|
| 0 | 15 | 308 |
| 10 | 61 | 1238(`1.24K`) |
| 100 | 약 1760만(`17.6M`) | 약 3.6억(`358M`) |

- 쿠키 수는 1e308을 넘어도 된다(`BigNumber`). 실패 글의 값(`{have}`·`{need}`)에 큰 수를 넣으면 기본 형식(`1.23K`)으로 들어간다.
- 클릭은 행동 하나(`Click`)다. 아주 빠른 연타는 33장 5-7처럼 틱마다 묶는다(`Click { count }`). 클릭 숫자는 `GameView.Play`로 띄운다(입력을 막지 않는다, 32장 5-9).
- 꺼 둔 동안의 생산은 켤 때 첫 정산 행동이 `Game.Now - profile.lastSeen`으로 계산한다(07장 2.11).
- DB 조건으로 "쿠키 1조 개를 모았다" 같은 도전과제를 만들려면 규칙이 자릿수 필드를 함께 둔다(`run.cookieDigits = run.cookies.IsZero ? 0 : (int)Math.Floor(run.cookies.Log10()) + 1`, 1조 = 13자리). 쿠키를 바꾸는 곳을 규칙 함수 하나로 모으고 거기서 함께 고친다. DB의 숫자 비교는 그 `int` 필드를 본다.

**가격이 1e308을 넘을 때** — 밸런스 값은 double이라 `BalanceSum`이 무한대가 되면 `BigNumber`로 담을 수 없다(2-7). 그만큼 오르는 곡선이면 같은 식을 큰 수로 계산한다. 기준·비율은 그대로 밸런스 값에서 읽는다.

```csharp
// 단계 n의 가격 = 기준 × 비율^n
public static BigNumber PriceAt(double basePrice, double rate, int n) => basePrice * BigNumber.Pow(rate, n);

// 단계 n부터 k개의 합(등비): 기준 × 비율^n × (비율^k − 1) ÷ (비율 − 1)
public static BigNumber PriceSum(double basePrice, double rate, int n, int k) =>
    PriceAt(basePrice, rate, n) * (BigNumber.Pow(rate, k) - 1) / (rate - 1);

// 살 수 있는 개수: 비율^k ≤ 예산 × (비율 − 1) ÷ 단계 n 가격 + 1. 반올림으로 하나 많을 수 있어 PriceSum으로 다시 확인한다.
public static int Affordable(BigNumber budget, double basePrice, double rate, int n) =>
    (int)Math.Floor((budget * (rate - 1) / PriceAt(basePrice, rate, n) + 1).Log10() / Math.Log10(rate));
```

---

## 6. 주의할 점 · 자주 하는 실수

| 증상 | 원인과 해결 |
|---|---|
| 길찾기가 오래 걸리다 빈 목록 | 비용 함수가 맵 밖에서도 0 이상을 돌려준다. 갈 수 없는 목표면 맵 밖으로 퍼지다가 10만 번에서 멈춘다. 맵 밖은 음수(5절 `TileAt`이 `'#'`), 필요하면 `maxCost`(2-4). |
| 적·주인공에게 가는 길이 늘 빈 목록 | 목표 칸(누가 선 칸)을 비용 함수가 막았다. 목표 칸만 들어갈 수 있게 하거나 이웃 칸을 목표로 한다(5-5). |
| 이동 범위에 아군이 선 칸이 들어 있음 | `Reachable`은 지나갈 수 있는 칸을 모두 준다. 멈출 수 없는 칸은 결과에서 뺀다(5-1의 `MoveRange`). |
| 길찾기·범위 계산이 든 행동이 느림 | 비용·막힘 함수 안에서 `Game.Find`·`Game.All`을 불렀다(칸마다 데이터 전체를 훑는다). 개체가 선 칸은 먼저 한 번 사전으로(`Board.Occupants`). |
| 대각선 이동으로 벽 모서리를 빠져나감 | 비용 함수는 들어가는 칸만 받아 모서리를 모른다(2-3). 4방향으로 하거나 이웃을 직접 거른다. |
| 대각선 걸음이 더 비싸지 않음 | `diagonal: true`면 8방향 모두 그 칸의 비용 그대로다. 대각선을 비싸게 하는 것은 이 함수로 되지 않는다. |
| 길찾기가 가장 싼 길이 아님 | `minCost`를 맵에서 가장 싼 칸 비용보다 크게 줬다(비용 0인 길이 있는데 `minCost: 1`). 비우면(0) 늘 가장 싼 길이다(2-4). |
| 벽 칸 위의 대상이 보임 | `Visible`은 양 끝을 보지 않는다. 대상 칸도 막게 하려면 따로 검사한다. |
| 창·돌진이 벽을 뚫음 | `Ray`·`Cone`·`Area`는 모양만 준다. `TakeWhile`로 막힌 칸에서 멈춘다(5-2). |
| 브레스 모양이 비뚤어짐 | `Cone`에 대각선 방향을 넣었다. 4방향(`Grid.Up` … 또는 `Direction`) 하나를 넣는다. |
| 맵이 위아래가 뒤집혀 그려짐 | `rows[0]`이 y = 0이고 `Grid.Up`은 y + 1이다. Tilemap에는 `-y`로 그린다(3-1). |
| `Grid.Rotate`로 돌린 범위가 화면에서 반대로 돎 | `Grid.Rotate`는 y 위 + 기준의 시계 방향이다. `rows`처럼 y가 아래로 늘어나게 그리면 회전 수에 −를 붙인다(2-1). |
| 조커 칸·가방의 옆 칸을 `Grid`로 계산하다 줄 끝·빈 칸이 꼬임 | 목록 위치가 칸인 자리는 37장 `Placement`(줄 길이·빈 칸 `null`·덜 찬 마지막 줄)를 쓴다. |
| `Grid.Reachable`·`Grid.Path`가 없다는 컴파일 오류(CS0117) | 프로젝트에 같은 이름의 `Grid` 클래스(장르 확장)가 있어 `GBible.Grid`를 가린다. 프로젝트 쪽 이름을 바꾸거나(`TacticsRules`) `GBible.Grid`로 쓴다. 칸 구조체도 `Cell`을 따로 만들지 않는다. |
| 칸 딕셔너리에서 칸을 못 찾음 | 키를 손으로 만들었다(`"3, 4"`). 키는 `Grid.Key`로만 만든다. |
| 작물·생산 결과가 Undo나 다시 하기 뒤에 달라짐 | 딕셔너리를 도는 차례에 기대었다(난수·소수 합). 키를 정렬해 돈다(5-6·5-7). |
| 원본 맵이 게임 중에 바뀜 | `session.Instances`의 원본을 고쳤다. 바뀌는 칸은 판 데이터의 딕셔너리로(3-1). |
| 시트에서 맵 줄이 수식으로 바뀜 | 줄이 `=`·`+`·`-`·`@`로 시작한다. 테두리를 `#`으로(3-2). |
| `Game.Pay`가 "[MomoiProject] 비용: 'run › cookies'는 숫자 필드가 아닙니다(BigNumber)." | `Game.Pay`·DB 숫자 칸은 double 숫자 필드만 안다. `if (run.cookies < price) throw Game.Fail(…); run.cookies -= price;`(5-7). |
| 쿠키에 1을 더해도 그대로 | 정상이다. 1e300 아래는 double 셈이라 정수는 2^53(약 9e15)까지만 정확하고, 그 위는 유효 숫자 약 15자리라 차이가 17자리를 넘으면 작은 쪽은 사라진다(2-7). |
| "[MomoiProject] 큰 수: 무한·NaN은 담을 수 없습니다." | double 계산이 먼저 넘쳤다(1e308을 넘은 `BalanceSum`, `x / 0.0`). 넘칠 값은 처음부터 `BigNumber`로 계산한다(5-7의 `PriceSum`). |
| "[MomoiProject] 큰 수: 0으로 나눌 수 없습니다." | 0인 `BigNumber`로 나눴다. 생산량 0일 때 "다음까지 몇 초"를 나누기 전에 `IsZero`를 본다. |
| `(double)x`가 무한대 | 1e308을 넘은 값이다. 비교·계산은 `BigNumber`끼리 한다. |
| `BigNumber.Parse("1.23억")`이 `FormatException` | `Parse`는 지수·짧은 단위·숫자만 읽는다. 큰 수를 글로 바꿔 저장하지 않는다: 필드 그대로 두면 판 저장이 `m`·`e`를 그대로 담는다. `ToString`의 글은 소수 둘째 자리까지라 다시 읽으면 정밀도가 준다(보이기용). |
| 진행 막대·슬라이더·'데이터 연결'의 숫자가 0 | 숫자 칸은 double로 바꿀 수 있는 값만 읽는다. 비율을 화면 코드에서 계산한다(2-8). |
| DB 조건으로 쿠키 수를 비교할 수 없음 | DB의 숫자 비교는 double 숫자 필드용이다. 규칙이 자릿수 같은 `int` 필드를 함께 두고 그것을 본다(5-7). |
| 큰 수 필드를 고쳤는데 값이 이상함 | `m`·`e`를 직접 고쳤다(1e300 아래는 m = 값·e = 0, 그 위만 가수 1~10 × 10^지수). 연산자·생성자로만 바꾼다. |
