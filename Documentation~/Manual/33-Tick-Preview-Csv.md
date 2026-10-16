# 33. 고정 틱·미리보기 실행·CSV

**한 줄 요약**: 실시간 게임은 DB › Bootstrap의 **고정 틱**(초당 틱 수)을 켠다. 게임 시간이 1/틱 초씩 일정하게 흐르고, 시간 규칙(시간 예약·상태 효과의 주기·시간 지속·패시브 효과 주기·시간 조건)과 `session.Ticked`가 틱마다 돌아 프레임 속도와 상관없이 같은 결과가 난다. **미리보기 실행**(`Game.Preview`)은 행동을 실제로 해 보고 결과만 읽은 뒤 난수까지 모두 되돌린다(실제로 하면 미리본 값과 같다 — 예상 피해). `Game.CanDo`는 지금 하면 되는지와 안 되는 이유다. **CSV**는 DB 목록을 내보내고 가져와 구글 시트·엑셀에서 수치를 한꺼번에 고친다(가져온 것은 적용 전). 실시간 게임의 **예산**(판정 보고 행동·틱 묶음·저장 간격·연출·오브젝트 풀)은 5-7.

**언제 쓰는가**

- 실시간 아레나·디펜스처럼 시간이 흐르는 게임에서 웨이브·재생·쿨다운 같은 시간 규칙이 기기마다 같게 돌아야 할 때(고정 틱).
- 카드·스킬에 마우스를 올리면 "예상 피해 12", "처치"를 보일 때(미리보기 실행).
- 버튼을 흐리게 하고 "에너지가 모자랍니다"처럼 안 되는 이유를 보일 때(`CanDo`).
- 밸런싱 값·카드 비용·몬스터 체력 수십 줄을 시트에서 고칠 때(CSV).
- 휘두르기 한 번에 적 수십 마리, 투사체 수백 발, 1초에 맞음 수십 번인 실시간 액션을 느려지지 않게 만들 때(실시간 예산).

이 문서에서 쓰는 용어:

| 용어 | 뜻 |
|---|---|
| 틱 | 게임 시간이 한 번 흐르는 단위. 고정 틱이면 1/초당 틱 수 초, 끄면 한 프레임. |
| 고정 틱 (`TickRate`) | 초당 틱 수. 0이면 끈다(프레임마다 그 프레임 시간만큼 흐른다). |
| `Ticked` | 틱마다(끄면 프레임마다) 쏘는 세션의 C# 이벤트. 행동 밖이다. |
| `TickAlpha` | 다음 틱까지 지난 비율(0~1). 화면이 두 틱 사이를 부드럽게 그릴 때(보간) 쓴다. |
| 미리보기 실행 (`Game.Preview`) | 행동을 해 보고 결과만 읽은 뒤 모두 되돌리는 것. |
| `CanDo` | 지금 그 행동을 하면 성공하는지(미리보기로 해 본다)와 실패 이유. |
| CSV | 쉼표로 나눈 표 글자 파일. 구글 시트·엑셀이 그대로 연다. |
| 작업본 | DB에서 고친 내용을 저장(Ctrl+S) 전까지 모아 두는 사본(21장 3절). CSV 가져오기도 여기에 들어간다. |
| 판정 보고 행동 | 화면(MonoBehaviour)이 정한 판정(누가 누구를 맞혔나·성공했나)을 규칙에 알리는 행동. 규칙은 그 결과로만 데이터를 바꾼다. |
| 실시간 예산 | 실시간 게임에서 행동 수·Sync·저장·연출이 프레임을 넘지 않게 지키는 규칙들(5-7). |

---

## 1. 무엇으로 만드나

| 기획 문장 | 만드는 것 |
|---|---|
| "10초마다 웨이브, 30fps 기기와 144fps 기기가 같게" | 고정 틱 30 + `Ticked`에서 시각 확인 → 행동(5-1) |
| "웨이브 남은 시간 막대가 부드럽게" | `TickAlpha`로 보간(5-2) |
| "카드에 마우스를 올리면 예상 피해" | `Game.Preview`(5-3) |
| "낼 수 없는 카드는 흐리게, 이유를 툴팁으로" | `Game.CanDo`(5-4) |
| "분석 전송은 실제로 했을 때만" | `Game.IsPreviewing`(5-5) |
| "몬스터 체력·카드 비용을 시트에서 한꺼번에" | DB 목록의 CSV(5-6) |
| "휘두르면 닿은 적 모두 피해, 등 뒤면 두 배" | 판정 보고 행동 — 틱마다 묶은 `Strike { hits }`(5-7) |
| "투사체 수백 발·경험치 보석 수백 개" | 화면 쪽 오브젝트 풀 + 규칙에는 수와 결과만(5-7) |
| "실시간이라 판을 자주 저장하면 끊긴다" | 판 저장 'N초마다 저장(실시간·방치형)'(5-7) |

> **규칙: 실시간 물리·이동은 화면 코드, 규칙에 필요한 결과만 행동.** 적이 다가오는 움직임은 `Update`·물리 엔진이 한다. "맞았다", "쓰러졌다", "주웠다"처럼 규칙에 필요한 결과만 행동(`Game.Do`)으로 보낸다. 물리 자체는 화면 코드의 자유 영역이고(28장 1절), 결과를 알리는 **판정 보고 행동**은 문법 안이다(5-7).

> **규칙: 틱 규칙은 가볍게.** 30틱이면 1초에 30번 돈다. 틱마다 읽기만 하고, 바꿀 때만 행동을 실행한다(5-1).

---

## 2. 동작 규칙

### 2-1. 고정 틱

호스트(`GBibleRuntime`)가 매 프레임 끝에 `session.EndFrame(Time.deltaTime)`을 부른다(07장 2.5). 고정 틱이면 그 안에서:

1. 프레임 시간을 쌓는다.
2. 쌓인 시간이 한 틱(1/틱 초) 이상인 동안 되풀이한다(한 프레임 최대 `TickLimit`틱 — 아래 '배속'):
   - 게임 시계를 한 틱 진행한다(`Clock.Tick` +1, `Clock.Time` + 1/틱 초).
   - 시간 조건을 확인한다(도전과제·로그의 시간 조건).
   - 상태 효과의 주기 결과를 내고, 시간 지속이 다 된 것을 끝낸다(행동 "상태 효과 만료", 29장 2-2).
   - 패시브 효과의 주기(`pulse`)를 낸다(17장).
   - 시각이 된 시간 예약을 실행한다(행동 "예약 실행", 29장 2-5).
   - `session.Ticked(1/틱 초)`를 쏜다.
3. 최대 틱(`TickLimit`)을 돌고도 시간이 남으면 남은 시간을 버린다(느린 프레임에서 따라잡느라 멈추지 않게. 게임이 잠깐 느려진다).
4. `TickAlpha` = 남은 시간 ÷ 한 틱.

- **프레임 속도와 상관없이 같다.** 60fps로 1초와 20fps로 1초는 같은 10틱(초당 10틱일 때)을 같은 게임 시각에 돈다. 그래서 시간 예약·상태 효과의 만료가 기기마다 같은 틱에 일어난다(재현·멀티플레이).
- 고정 틱이면 `Clock.Tick`은 틱 수다(프레임 수가 아니다). 행동의 `tick` 필드·타임라인의 틱도 그렇다.
- **끄면**(`TickRate` 0, 기본) 프레임마다 한 번: 시계가 그 프레임 시간만큼 흐르고 위의 일을 한 번 하며 `Ticked`도 그 프레임 시간으로 한 번 온다.
- 일시정지(`Time.deltaTime` 0) 중에는 `EndFrame`을 부르지 않으므로 틱도 없다. 배속이면 실제 1초에 틱이 그만큼 더 돈다(게임 시간 기준).
- **배속과 한 프레임 한도**: 배속이면 한 프레임에 틱이 배속만큼 더 들어오므로 한도 `TickLimit` = 8 × 올림(`HostSpeed`)(최대 `MaxTicksPerFrameAtSpeed` 128)이다. Unity에서는 `GameTime`(07장)이 매 프레임 이 프로그램의 세션들(화면의 세션·방장의 서버 세션·매칭 서버의 방)에 `HostSpeed`를 맞춘다. 세션을 직접 돌리는 호스트(서버·테스트)가 배속을 쓰면 `HostSpeed`를 직접 정한다(기본 1) — 정하지 않으면 한도 8에 걸려 남은 시간을 버리고 규칙 시간만 느려진다.
- `Ticked`는 **행동 밖**이다. 값을 바꾸려면 받는 쪽에서 `session.Do(행동)`을 부른다. 행동 실행 중인 틱(드물다)에는 시계·시간 조건만 진행하고 나머지는 건너뛴다.
- **멀티 클라이언트 세션**(`SessionRole.Client`)은 틱을 돌리지 않는다: 시간 규칙도 `Ticked`도 없고, 시계는 받은 묶음의 서버 시각으로 맞춘다(40장 1-1). 틱 규칙은 서버(방장) 세션에서 돈다.
- 스냅샷 방식(20장 3절)의 간격도 게임 시계로 잰다.

### 2-2. 미리보기 실행 · `CanDo`

**`Game.Preview(행동, g => 읽기)`**

1. 행동을 `Game.Do`와 같은 길로 실행한다(참조 필드를 지금 데이터의 객체로 다시 찾는 것, 행동이 끝나기 직전의 정리까지).
2. 바뀐 상태 그대로 `읽기`를 부르고 그 값을 받는다.
3. **모두 되돌린다**: 플레이어 데이터(`Permanent()` 안의 변경도 — 02장 2.9), 난수 상태. 그리고 아무것도 남기지 않는다.

| 남지 않는 것 | 그래서 |
|---|---|
| 기록(타임라인) · 행동 로그 · Undo 이력 | 모니터에도 보이지 않는다 |
| 행동 뒤에 받는 쪽(`SubscribeAfterAction`) | 피해 숫자·연출이 뜨지 않는다 |
| 도전과제 달성 | 미리보기로 달성되지 않는다 |
| 데이터 변경 알림(`Changed`·`DataChanged`·`Synced`) | 화면이 다시 그려지지 않고 멀티플레이 전송에도 실리지 않는다 |
| 난수 | 돌아가므로 실제로 하면 미리본 값과 같다 |

- **바로 받는 쪽**(`Events.Subscribe`·`SubscribeAll`)은 미리보기에서도 불린다. 규칙(로그·트리거·유물·예약·상태 효과)이 실제와 똑같이 돌아야 결과가 맞기 때문이다. 그래서 화면 연출은 `SubscribeAfterAction`으로 받는다(32장).
- 행동이 실패하면 바꾼 것을 되돌린 뒤 그 예외를 그대로 던진다(`GameFailure`면 지금 언어의 이유).
- **행동 밖(화면 코드)에서만** 부른다. 행동 안에서 부르면 `InvalidOperationException`("[MomoiProject] 행동 안에서는 미리보기를 할 수 없습니다(행동 밖, 화면 코드에서 부르세요).").
- `읽기`에서는 값을 꺼내 돌려준다(체력 숫자). 데이터 객체를 돌려주면 되돌린 뒤의 객체라 미리본 값이 아니다.
- 행동이 끝나기 직전의 정리(데이터에서 사라진 개체의 상태 효과·Modifier 지우기, 29장 2-3)도 `읽기` 전에 한다. 그래서 `읽기`는 실제 행동이 끝난 뒤와 같은 상태를 본다.
- 미리보기 중의 값 변경은 **알리지 않는다**(위 표). 바뀌었다가 그대로 돌아오는 변경이라, 다시 그리기(`GameView.Redraw`·ViewList·데이터 연결)가 다시 그리지 않고 멀티플레이 전송에도 실리지 않는다. 그래서 다시 그리기 안(ViewList의 `bind`)에서 불러도 된다(5-4).
- 미리보기는 행동 전체를 실제로 돌린다. 버튼이 많으면 그만큼 행동이 돈다(비용).
- 멀티 클라이언트에서는 이쪽 데이터로 돈다. 난수 시드가 서버와 달라 확률이 든 값은 어긋날 수 있다(40장 1-1).

**`Game.CanDo(행동, out 이유)`** — 미리보기로 해 보고 성공하면 참(이유 `null`), 실패하면 거짓(이유 = 예외의 메시지). `GameFailure`면 지금 언어의 글이다(30장 2-4). 프로그래머 실수(`InvalidOperationException`)도 거짓과 그 메시지가 된다. 행동 안에서 부르면 예외 대신 거짓과 "행동 안에서는 미리보기를 할 수 없습니다…"가 온다.

**`Game.IsPreviewing`** — 미리보기 실행 중이면 참. 규칙 코드가 밖으로 나가는 일(분석 전송·파일 쓰기·외부 SDK)을 건너뛸 때 본다(5-5).

### 2-3. CSV

- 한 줄 = 항목 하나(첫 칸 `id`, ID 순서). 칸 = 그 종류의 **단순 필드**(정수 · 숫자 · 참거짓 · 글자 · enum). 묶음 필드(곡선·지속 설정·설정 값)의 안쪽은 `curve.step`처럼 점으로 이어 칸이 된다.
- **목록·배열**은 번호마다 칸이 된다. 번호는 0부터다.

| 목록 | 칸 이름 |
|---|---|
| 묶음의 목록(가중치 표의 항목 줄) | `entries[0].weight` · `entries[1].weight` … |
| 단순 값의 목록(`List<string>`·`List<int>`) | `tags[0]` · `tags[1]` … |
| 묶음 필드 안의 목록(곡선의 점) | `curve.points[0].x` · `curve.points[2].y` … |

- 칸은 목록에 든 것만큼(번호마다) 생긴다. 항목마다 길이가 다르면 가장 긴 목록까지 칸이 생기고, 짧은 항목의 남는 칸은 빈칸이다. 모든 항목의 목록이 비어 있으면 그 목록의 칸은 나오지 않는다.
- **칸 순서**: 항목을 ID 순서로 훑어 필드가 처음 나온 순서다. 목록은 그 목록이 처음 나온 자리에 줄 번호 순서로 모이고, 한 줄 안에서는 필드 순서다(`entries[0].item` · `entries[0].weight` · `entries[1].item` · `entries[1].weight` …). 시트에서 한 줄(목록의 한 항목)이 옆으로 나란히 붙는다.
- **빠지는 칸**: Block 칸(트리거·조건·결과 — `[SerializeReference]`, 목록 줄 안의 것도), 에셋 참조(스프라이트·프리팹), 숨은 칸(ID·폴더).
- 인스턴스 탭은 **값의 클래스마다** 따로 내보낸다(`Card`·`MonsterData` …). 칸 = 그 클래스의 필드(`cost`, `maxHp` …).
- 파일은 UTF-8(BOM)이라 엑셀에서 한글이 깨지지 않는다. 숫자는 소수점 `.`, enum은 코드 이름(`Exponential`)으로 내보낸다.
- **가져오기**: 첫 줄은 칸 이름(`id` 칸이 있어야 한다). 줄마다 같은 종류의 그 ID 항목을 고치고, 없는 ID면 새 항목(인스턴스 탭이면 그 클래스의 새 값)을 만든다. 단 잠긴 종류(로직 쪽 — 21장 잠금 표)는 없는 ID를 건너뛴다(새 항목은 AI가 만든다). CSV에 없는 항목은 지우지 않는다.
- 파일은 UTF-8로만 읽는다. 다른 인코딩(Excel의 기본 'CSV' 등)이면 아무것도 바꾸지 않고 "CSV를 UTF-8로 저장해 주세요(Excel: CSV UTF-8)"를 알린다. 엑셀에 열어 둔 채로 가져와도 된다(마지막으로 저장한 내용을 읽는다).
- 손으로 쓴 CSV: 따옴표 칸(`"a, b"`)은 칸 맨 앞의 `"`로만 시작한다. 칸 가운데의 `"`(`5" 칼`)는 글자다(엑셀과 같다).
- **목록 가져오기**: CSV 한 줄(항목 하나)마다 목록 길이부터 맞춘다.
  - 길이 = 그 줄에서 값이 있는 목록 칸의 가장 큰 번호 + 1. 뒤쪽 번호의 칸을 모두 비우면 목록이 그만큼 줄어든다.
  - 그 목록의 칸이 머리 줄에 있는데 그 줄에서 모두 비면 목록을 **비운다**(길이 0). 머리 줄에 그 목록의 칸이 없으면 목록은 그대로다.
  - 목록 칸의 빈칸은 읽지 않고 그대로 둔다(이미 있던 번호면 지금 값). 가운데 번호의 칸만 비워도 그 번호는 지워지지 않는다. 가운데 것을 빼려면 뒤의 값을 한 칸씩 당겨 적고 마지막 번호를 비운다.
  - 새로 생긴 번호(묶음)는 그 클래스의 기본값에서 시작한다(앞 번호의 값·Block을 복사하지 않는다). 가중치 표의 항목 줄이면 무게 1, 개수 1~1, 조건은 빈칸이다. Block 칸은 DB에서 채운다. 단순 값의 목록(`tags`)에 새로 생긴 번호는 빈 값이다(`tags[0]`만 있는데 `tags[2]`를 적으면 `tags[1]`은 빈 글자).
  - 목록 맨 뒤의 빈 글자 요소는 내보낼 때 `""`로 적는다(빈칸이면 가져올 때 그 줄이 사라지므로). 가져올 때 `""`는 빈 글자로 읽는다.
  - 내보낸 파일에 없는 목록 칸도 머리 줄에 직접 적으면(`curve.points[0].x`) 가져올 때 그 번호가 생긴다.
- 가져온 변경은 DB의 다른 변경처럼 **적용 전**(작업본)이다. 저장(Ctrl+S)하면 에셋에 쓰이고, 사이드바의 **되돌리기**로 버릴 수 있다. 가져온 뒤 검증이 다시 돌아 틀린 조합(최솟값 > 최댓값 …)을 알린다.

| 칸 형식 | 읽는 값 |
|---|---|
| 정수 | 정수. `3.0`처럼 소수가 0이면 받는다. 천 단위 쉼표(`1,000`)도 받는다. 필드 형식에 들어가지 않는 크기(`int` 칸의 `3000000000`, `byte` 칸의 `300`)는 넣지 않고 "이 칸의 정수 형식에 들어가지 않는 크기입니다" 오류 |
| 숫자 | 숫자. 소수점은 `.`. 천 단위 쉼표(`1,000` · `1,000.5`)는 빼고 읽고, 점이 없고 쉼표 뒤가 세 자리가 아니면 소수점으로 읽는다(`0,5` = 0.5). 그 밖(`1.000,5` · `0,500`)은 "쉼표가 천 단위인지 소수점인지 알 수 없습니다" 오류 |
| 참거짓 | `true`/`false` · `1`/`0` · `o`/`x` · `y`/`n` · `yes`/`no` · `참`/`거짓`(대소문자 무시). 빈칸은 거짓 |
| enum | 코드 이름(`Linear`, 대소문자 무시) 또는 DB에 보이는 이름(`일정하게(+단계마다)`) |
| 글자 | 칸의 글자 그대로(앞뒤 공백 포함) |

- 칸 이름이 그 종류에 없거나 단순 칸이 아니면 "모르는 칸"으로 건너뛴다. 값을 읽지 못한 칸은 그 칸만 건너뛰고 문제로 남긴다(목록이 아닌 숫자 칸을 비우면 "숫자가 아닙니다('')"). 목록 칸의 빈칸은 읽지 않으므로 문제가 되지 않는다(위). 참거짓 칸의 "빈칸은 거짓"도 목록이 아닌 칸만이다.

---

## 3. 에디터에서 쓰는 법

### 3-1. DB › 시작 파일 › Bootstrap — 고정 틱(실시간)

`MomoiProject → DB` → 사이드바 **시작 파일 › Bootstrap** → **고정 틱(실시간)** → **초당 틱 수**. 고친 값은 바로 저장된다(Ctrl+S 필요 없음).

| 칸 | 같은 뜻의 `SessionOptions` | 기본 |
|---|---|---|
| 초당 틱 수 | `TickRate` | 0(끔, 턴제·카드) |

- 예: 30이면 1/30초마다 한 틱. 0보다 작은 값은 0이 된다.
- 고정 틱을 켜는 게임은 같은 화면에서 Undo 방식을 "스냅샷"으로, 판 저장을 "N초마다 저장(실시간·방치형)"으로 함께 고른다(20장 3절·06장, 이유는 5-7 표).

### 3-2. CSV 내보내기 · 가져오기

1. `MomoiProject → DB`에서 탭을 고른다(밸런싱 값 · 가중치 표 · 인스턴스 · 액티브 효과 · 로그 · 상태 효과 · 설정 · 프로젝트의 DB 종류 …). 키워드·클래스·이벤트처럼 전용 화면을 쓰는 탭에는 없다.
2. 목록 맨 위 폴더 줄(종류 이름 줄)에 마우스를 올리면 도구 버튼이 나온다. **CSV**(툴팁 "CSV로 내보내기·가져오기(구글 시트·엑셀에서 수치를 한꺼번에 고칠 때). 가져온 것은 적용 전으로 들어간다")를 누른다.
3. 메뉴:

| 탭 | 메뉴 |
|---|---|
| 인스턴스 | 값의 클래스마다 **Card 내보내기…** · **Card 가져오기…**("id로 맞춰 고칩니다. 없는 id는 건너뜁니다(새 항목은 AI가 만든다)" — 구조 편집을 열었으면 "없는 id는 이 클래스의 새 인스턴스로 만듭니다"). 인스턴스가 없으면 "인스턴스가 아직 없습니다" |
| 그 밖의 탭 | **CSV로 내보내기…** · **CSV 가져오기…**(잠긴 종류: "id로 맞춰 고칩니다(적용 전). 없는 id는 건너뜁니다(새 항목은 AI가 만든다)", 개발자가 만드는 종류: "id로 맞춰 고치고, 없는 id는 새 항목으로 만듭니다(적용 전)") |

4. **내보내기**: 파일 저장 창("CSV로 내보내기", 기본 이름은 탭 이름 또는 클래스 이름 + `.csv`) → 저장하면 파일이 있는 폴더가 열린다.
5. **가져오기**: 파일 열기 창("CSV 가져오기") → 결과 창이 뜬다.

```
3개 고침, 1개 새로 만듦(적용 전 — Ctrl+S로 적용).
모르는 칸(건너뜀): hp
goblin.maxHp: 정수가 아닙니다('스무')
```

- 결과 창의 문제 줄은 20줄까지 보이고 나머지는 "… 외 n개"로 센다. 문제 형식: `ID.칸: 정수가 아닙니다('…')` / `이 칸의 정수 형식에 들어가지 않는 크기입니다('…')` / `숫자가 아닙니다('…')` / `참·거짓이 아닙니다('…')` / `고를 수 있는 값이 아닙니다('…': None, Linear, …)`. 새 항목을 만들 수 없는 ID(ID 규칙 위반 등)는 `ID: 이유`.
- 잠긴 종류에서 없는 ID가 있었으면 "없는 id n개는 건너뜀 — 새 항목은 AI가 만든다."가 더 붙는다.
- "고침"은 값이 실제로 바뀐 기존 항목의 수다(목록 길이가 바뀐 것 포함, 같은 값을 다시 넣은 줄은 세지 않는다). 실제로 바뀐 칸은 목록의 "변경 · 적용 전" 배지와 바뀐 칸의 이름표 색으로 본다.
- 첫 줄에 `id`가 없으면 "첫 줄에 'id' 칸이 없습니다.", 줄이 둘보다 적으면 "읽을 줄이 없습니다(첫 줄은 칸 이름, 그 아래가 항목)."

### 3-3. 모니터에서 확인하기

- 고정 틱이면 모니터 머리의 "틱 …"이 틱 수로 오른다. 시간 예약·만료는 행동 탭에 "예약 실행"·"상태 효과 만료"(개발자)로 남는다.
- 미리보기 실행은 아무것도 남기지 않는다(그것이 정상이다).

---

## 4. 코드로 쓰는 법 — 문법 요소를 하나씩

### 4-1. 고정 틱 — `SessionOptions.TickRate` · `Ticked` · `TickAlpha`

```csharp
GBibleRuntime.Configuring += options => options.TickRate = 30;     // Bootstrap 화면 대신 코드로(코드가 이긴다)
session.Ticked += dt => { /* 틱마다(dt = 1/30초). 행동 밖 */ };
var smooth = session.Clock.Time + session.TickAlpha / session.Options.TickRate;   // 지난 틱 + 다음 틱까지의 비율(고정 틱일 때만)
```

| 멤버 | 뜻 |
|---|---|
| `int SessionOptions.TickRate` | 초당 틱 수. 0이면 끈다 |
| `event Action<double> GameSession.Ticked` | 틱마다(끄면 프레임마다). 인자는 그 틱(프레임)의 게임 시간(초) |
| `double GameSession.TickAlpha` | 다음 틱까지 지난 비율(0~1). 끄면 0 |
| `const int GameSession.MaxTicksPerFrame` | 한 프레임에 따라잡는 최대 틱 수(보통 속도에서 8) |
| `const int GameSession.MaxTicksPerFrameAtSpeed` | 배속을 반영한 한도의 상한(128) |
| `double GameSession.HostSpeed` | 호스트의 배속(기본 1). 한 프레임 한도만 늘린다(시간은 `deltaTime`에 이미 배속이 들어 있다). Unity에서는 `GameTime`이 매 프레임 맞춘다 |
| `int GameSession.TickLimit` | 이번 프레임의 한도 = `MaxTicksPerFrame` × 올림(`HostSpeed`), 최대 `MaxTicksPerFrameAtSpeed`(1 아래는 1로 본다) |
| `void GameSession.EndFrame(double deltaTime)` | 호스트가 매 프레임 부른다. 세션을 직접 만들면 직접 부른다(서버). 한 번에 `TickLimit`틱까지만 돈다 |
| `void GameSession.Advance(double seconds)` | 규칙 테스트·도구용: 그 초만큼 한 틱씩 끝까지 `EndFrame`을 부른다(한도에 잘리지 않는다). `EndFrame(1.0)`은 30틱 게임에서 8틱뿐이다(2-1). 고정 틱이 꺼져 있으면 `EndFrame(초)` 한 번 |

- `Ticked`는 세션의 이벤트다. 슬롯을 바꾸면 세션이 새로 만들어지므로(31장) 새 세션마다 다시 건다. 틱 규칙은 Rules의 등록 함수(`ArenaRules.Register(session)`)에 두고, 연결은 `Client/Bootstrap.cs`의 `RegisterRules`(`GBibleRuntime.SessionCreated`) 한 곳에서 그 함수를 부른다(17장 2-6, 5-1). `RegisterRules`는 새 세션마다(슬롯을 바꿔도 · 멀티 서버 세션에도) 불린다.

### 4-2. `Game.Preview` · `Game.CanDo` · `Game.IsPreviewing`

```csharp
int hpAfter = Game.Preview(new PlayCard { card = card, target = enemy }, g => g.Find<Enemy>(enemy.id)?.hp ?? 0);
if (!Game.CanDo(new PlayCard { card = card, target = enemy }, out var why)) tooltip.text = why;
if (!Game.IsPreviewing) Telemetry.Send("kill");
```

| 멤버 | 뜻 |
|---|---|
| `T Preview<T>(GameAction action, Func<GameApi, T> read)` | 행동을 해 보고 `read`의 값을 돌려준 뒤 모두 되돌린다(2-2). 행동이 실패하면 그 예외를 던진다. `action`이 `null`이면 `ArgumentNullException` |
| `bool CanDo(GameAction action, out string reason)` | 성공하면 참·`reason` `null`, 실패하면 거짓·`reason` = 메시지 |
| `bool IsPreviewing` | 미리보기 실행 중인지 |

`session.Actions.Preview<T>(action, Func<T> read)` · `session.Actions.IsPreviewing`은 같은 일의 세션 쪽 이름이다(`read`가 인자를 받지 않는다).

### 4-3. CSV — 에디터 코드 `DataCsv`

에디터 도구를 직접 만들 때 쓴다(`GBible.EditorTools`, 어셈블리 `GBible.Editor`, 에디터 전용).

| 멤버 | 뜻 |
|---|---|
| `string Export(Type kind, Type valueClass = null)` | 그 종류(인스턴스면 그 값의 클래스)의 CSV 글자 |
| `string Import(Type kind, Type valueClass, string text)` | CSV 글자를 작업본에 가져오고 결과 글(3-2)을 돌려준다 |
| `void ExportToFile(Type kind, Type valueClass = null)` · `ImportFromFile(…)` | 파일 창과 함께(DB의 CSV 메뉴가 부른다) |
| `IEnumerable<Type> InstanceClasses()` | 인스턴스 값의 클래스들(이름순) |

```csharp
// 메뉴 하나로 밸런싱 값을 정해 둔 경로에 내보낸다(Assets/MomoiProject/Code/Editor/BalanceExport.cs)
using System.IO;
using System.Text;
using GBible.Data;
using GBible.EditorTools;
using UnityEditor;

static class BalanceExport
{
    [MenuItem("Tools/밸런싱 값 CSV 내보내기")]
    static void Export() => File.WriteAllText("Balance/balance.csv", DataCsv.Export(typeof(BalanceData)), new UTF8Encoding(true));
}
```

---

## 5. 상황별 예제

### 5-1. 실시간 아레나 — 30틱 + `Ticked`에서 가벼운 규칙

카드 던전을 실시간 아레나로 바꾼 모양이다. 10초마다 적 웨이브가 나온다.

**에디터**(DB 항목은 AI가 DB 반영 스크립트로 만든다 — 관례 15절. `GameIds.Balance.WaveSize` 같은 상수는 스크립트가 돈 뒤 Unity가 다시 컴파일해야 생긴다)

- DB › 시작 파일 › Bootstrap: Undo 방식 "스냅샷"(간격 `0.5`초, 보관 수 `20` = 최근 10초), 고정 틱 초당 틱 수 `30`, 판 저장 "N초마다 저장(실시간·방치형)"(5-7).
- DB › 플레이어 데이터 `arena`: 구역 `Session`, 값 형식 `ArenaState`(아래 클래스).
- DB › 밸런싱 값 `waveInterval` `10`, `waveSize` `3`(곡선 `일정하게` +1/웨이브, 30장).

**규칙** — `Assets/MomoiProject/Code/Rules/Arena.cs`. 순수 C#이다(`UnityEngine`도 Unity 시작 방식도 쓰지 않는다 → 서버·테스트에서 그대로 돈다).

```csharp
using System;
using System.Collections.Generic;
using GBible;
using GBible.Ids;

[Serializable] public class ArenaFoe { public string id; public int hp = 10; }
[Serializable]
public class ArenaState                          // 플레이어 데이터 "arena"(세션 구역)
{
    public int wave;
    public double nextWaveAt;                    // 게임 시계(session.Clock.Time) 기준 시각
    public List<ArenaFoe> foes = new List<ArenaFoe>();
}

[Serializable, BlockInfo("웨이브 소환")]
public class SpawnWave : GameAction
{
    protected override void Run()
    {
        var arena = Game.Data().Arena;                 // 플레이어 데이터 arena(GameIds가 만든 창구 — 글자 키를 쓰지 않는다)
        arena.wave++;
        var count = (int)Game.Balance(GameIds.Balance.WaveSize, arena.wave);
        foreach (var id in Game.NewIds("foe", count)) arena.foes.Add(new ArenaFoe { id = id });   // 번호를 한 번만 쓴다
        arena.nextWaveAt = Game.Session.Clock.Time + Game.Balance(GameIds.Balance.WaveInterval);
    }
}

// 등록 함수: 세션을 받아 틱 규칙을 건다. Unity가 언제 켜지는지는 모른다(연결은 Client).
public static class ArenaRules
{
    public static void Register(GameSession session) => session.Ticked += _ => Tick(session);

    static void Tick(GameSession session)
    {
        var arena = session.Game.Data().Arena;
        if (session.Clock.Time < arena.nextWaveAt) return;     // 틱마다 읽기만 — 거의 늘 여기서 끝난다
        session.Do(new SpawnWave());                          // 바꿀 때만 행동
    }
}
```

**연결** — `Assets/MomoiProject/Code/Client/Bootstrap.cs`의 `RegisterRules`에 한 줄(17장 2-6).

```csharp
static void RegisterRules(GameSession session)   // Bootstrap — GBibleRuntime.SessionCreated(모든 세션)
{
    ArenaRules.Register(session);      // 새 세션마다 불린다(슬롯을 바꿔도 · 멀티 서버 세션에도)
}
```

- **적은 판 데이터의 가벼운 목록이다**(`arena.foes` — id·hp와 기믹이 읽는 칸만, 위치·속도는 화면 쪽). 적에게 상태 효과(화상·얼림)·DB 기믹(패시브 효과·액티브 효과의 대상)이 걸리는 게임은 이렇게 둔다 — 상태 효과·대상 Block은 플레이어 데이터 안의 객체(id)에만 걸린다(29장). 걸리는 것이 없는 순수 탄막·보석·이펙트만 화면 쪽 오브젝트 풀이다(5-7). 적 목록은 HUD가 보는 키(`run`)와 다른 키(`arena`)에 둔다(5-7 '화면 다시 그리기').
- 규칙 테스트는 세션을 만들고 `ArenaRules.Register(session)`을 부른 뒤 `session.Advance(10.5)`로 시간을 흘려 웨이브를 본다(4-1 — `EndFrame`을 직접 부르면 한도 8틱에서 잘린다, 38장 4절).
- 규칙 파일에 `[RuntimeInitializeOnLoadMethod]`와 `GBibleRuntime.SessionCreated +=`를 따로 두지 않는다. 연결이 파일마다 흩어지고, 서버·테스트는 세션을 직접 만든 뒤 `ArenaRules.Register(session)`을 부를 길이 없어진다.
- 웨이브는 30fps 기기와 144fps 기기에서 같은 틱에 나온다. 되감기(스냅샷)·판 저장도 시계와 `nextWaveAt`이 함께 돌아가 맞는다.
- 같은 일은 시간 예약으로도 된다: `SpawnWave.Run` 끝에 `Game.ScheduleAfter(간격, new SpawnWave())`(29장 2-5). 시간 예약도 틱마다 확인되므로 결과가 같다. 틱 받는 쪽은 "체력이 절반 이하면 분노"처럼 조건을 매 틱 보는 규칙에 맞다.
- 적의 움직임은 화면 코드가 `Update`에서 한다. 적이 주인공에게 닿으면 `Game.Do(new HitHero { foeId = foe.Id })`처럼 누가 닿았는지만 행동으로 보낸다. 피해 양은 행동이 밸런싱 값에서 읽는다(판정 보고 행동, 5-7).
- 틱마다 행동을 실행하면(재생 +1을 매 틱) 행동이 1초에 30개씩 쌓인다. "1초마다 1 회복"은 시각을 적어 두고 그 시각이 됐을 때만 행동을 실행한다(위와 같은 모양).

### 5-2. 남은 시간 막대 — `TickAlpha` 보간

게임 시계는 틱마다 1/30초씩 뛴다. 144fps 화면이 그 값을 그대로 그리면 막대가 계단처럼 움직인다. 막대는 HUD 화면(35장)의 게이지 `KitGauge`(UI 키트, UiGuide 3-7)이고, 화면 코드의 `Tick`(열려 있는 동안 매 프레임)에서 값을 넣는다.

```csharp
// Assets/MomoiProject/Code/Client/Screens/ArenaHud.cs — HUD UXML에 <MomoiProject.KitGauge name="wave" … class="kit-gauge" />
using GBible;
using GBible.Ids;
using MomoiProject;      // KitGauge(UI 키트를 복사한 Client/Kit의 네임스페이스)
using UnityEngine;

[BindTo(GameIds.Screen.ArenaHud)]
public class ArenaHud : GameScreen
{
    KitGauge _wave;

    protected override void OnOpen() => _wave = Q<KitGauge>("wave");

    protected override void Tick(float deltaTime)
    {
        var session = GBibleRuntime.Session;
        var now = session.Clock.Time + session.TickAlpha / session.Options.TickRate;   // 두 틱 사이의 부드러운 시각
        var interval = Game.Balance(GameIds.Balance.WaveInterval);
        _wave.value = Mathf.Clamp01((float)((Game.Data().Arena.nextWaveAt - now) / interval));
    }
}
```

- 틱마다 바뀌는 다른 값도 같다: 틱 받는 쪽에서 직전 값·지금 값을 적어 두고, 화면 코드의 `Tick`(월드 오브젝트면 `Update`)에서 `Mathf.Lerp(직전, 지금, (float)session.TickAlpha)`로 그린다(한 틱 늦게 보이는 대신 부드럽다).
- 읽기만 하므로 행동 밖에서 해도 된다. `Tick`에서 데이터를 바꾸지 않는다.

### 5-3. 예상 피해 — `Game.Preview`

카드에 마우스를 올리면 첫 적에게 들어갈 피해를 HUD 칸에 보인다. 방어도·보호막(행동 직전 이벤트)·분노(Modifier)·유물까지 실제 규칙 그대로다. 카드는 32장 5절의 월드 오브젝트(`CardView` + `DataTag`)이므로, 마우스 아래 카드는 HUD 화면 코드의 `Tick`에서 `WorldPointer.Pick`으로 찾는다(5-4와 같은 모양).

```csharp
// BattleHud(35장 5-1)에 더한다. UXML에 <ui:Label name="card-predict" /> — 코드가 쓰는 Label이라 binding-path를 달지 않는다
Label _predict;
string _predictFor;        // 마지막으로 미리본 카드 id — 같은 카드면 다시 하지 않는다

// OnOpen 안
_predict = Q<Label>("card-predict");
_predictFor = null;

// Draw 안(플레이어 데이터가 바뀌었다): 마우스 아래 카드를 다음 Tick에 다시 미리본다
_predictFor = null;

// Tick 안: 마우스 아래 카드(32장 5절 머리의 WorldPointer — HUD 칸 위면 null)
var tag = WorldPointer.Pick<DataTag>(WorldPointer.Position);
var card = tag != null ? Game.Find<DeckCard>(tag.id) : null;             // 적 그림이면 null
if (card?.id != _predictFor)
{
    _predictFor = card?.id;
    var enemy = Rules.LivingEnemies(Game).FirstOrDefault();
    _predict.text = card != null && enemy != null ? Predict(Game, card, enemy) : "";
}

// BattleHud 안의 함수
static string Predict(GameApi game, DeckCard card, Enemy enemy)
{
    try
    {
        var hpAfter = game.Preview(new PlayCard { card = card, target = enemy },
            g => g.Find<Enemy>(enemy.id)?.hp ?? 0);              // 쓰러지면 전투에서 빠진다 → 0
        return hpAfter <= 0 ? Texts.Get("hint.kill") : Texts.Get("hint.damage", ("amount", enemy.hp - hpAfter));   // "처치" / "예상 피해 {amount}"
    }
    catch (GameFailure f)
    {
        return f.Message;                                         // 낼 수 없으면 그 이유(지금 언어)
    }
}
```

- 난수가 든 카드(피해 4~6)도 같은 순간에 내면 미리본 값과 같다. 유물·로그·예약도 실제처럼 돌고 피해 숫자 연출은 뜨지 않는다(2-2).
- 미리보기는 마우스 아래 카드가 바뀔 때와 데이터가 바뀐 뒤(`Draw`)에만 한다. `Tick`마다 하면 프레임마다 행동이 한 번씩 돈다. 모든 카드에 늘 보이려면 핸드 ViewList의 `bind`에서 해도 된다(비용은 5-4).
- 행동 객체는 미리보기마다 새로 만든다.
- 고른 적(32장 5-5의 `BattleInput`)에게 보이려면 고른 적 id를 화면 쪽 한 곳(예: `BattleInput`의 static 속성)에 두고 여기서 `Game.Find<Enemy>(그 id)`로 읽는다 — 화면에만 있는 고르기 상태라 플레이어 데이터에 두지 않는다.

### 5-4. 낼 수 없는 카드 — `Game.CanDo`로 막기와 이유

핸드를 그릴 때 카드마다 낼 수 있는지 보고, 낼 수 없으면 흐리게 하고 이유를 툴팁에 둔다. 카드는 32장 5절의 월드 오브젝트(`CardView` — SpriteRenderer)이고, 툴팁은 HUD 화면(UI Toolkit)의 칸이다.

```csharp
// CardView(32장 5절 머리)에 더한다: 흐리게는 그림의 색, 이유는 들고 있다가 HUD가 보인다
public string Why { get; private set; }

public void SetPlayable(bool playable, string why)
{
    face.color = playable ? Color.white : new Color(1f, 1f, 1f, 0.5f);   // 흐리게
    Why = why ?? "";                                                        // "얼어 있어 2턴 동안 움직일 수 없습니다." / "에너지가 모자랍니다(1/2)."
}

// HandView(32장 5-3)의 bind: 그릴 때마다 카드마다 해 본다
(view, id) =>
{
    var card = Rules.CardOf(Game, id);
    view.Show(card, Game.Data().Run.battle.hand.IndexOf(id));
    var target = Rules.LivingEnemies(Game).FirstOrDefault();
    view.SetPlayable(Game.CanDo(new PlayCard { card = card, target = target }, out var why), why);
}

// BattleHud(35장 5-1)의 Tick: 마우스 아래 카드의 이유를 툴팁 칸에(_why = Q<Label>("card-why") — 코드가 쓰는 Label이라 binding-path를 달지 않는다)
var hovered = WorldPointer.Pick<CardView>(WorldPointer.Position);          // 32장 5절 머리 — HUD 칸 위면 null
_why.text = hovered != null ? hovered.Why : "";
```

- 이유는 규칙이 던진 글 그대로다(30장 5-8의 `Game.Fail`·`FailureKeys`). 같은 문장을 화면 코드에 따로 쓰지 않는다.
- 핸드가 UI Toolkit 화면의 목록(35장 `List<T>`)이면 같은 `CanDo`를 그 `bind`에서 부르고, 흐리게는 USS 클래스(`EnableInClassList`)로, 이유는 칸 안의 `Label`에 넣는다.
- `CanDo`는 다시 그리기를 일으키지 않으므로 `bind` 안에서 불러도 된다(2-2). 판 데이터가 바뀔 때마다 카드 수만큼 행동이 돈다. 카드가 아주 많거나 행동이 무거우면 마우스를 올릴 때만 부른다(5-3처럼).
- 눌렀을 때는 어차피 `GameScreens.Do`가 같은 이유로 실패해 알림으로 보인다(32장 5-5). 흐리게 하는 것은 누르기 전에 보여 주기 위해서다.
- 언어를 바꾸면 다시 그리기가 돌아 이유도 새 언어로 바뀐다.

### 5-5. 밖으로 나가는 일 — `Game.IsPreviewing`

미리보기 중에도 규칙 코드는 그대로 돈다. 규칙 코드 안에서 게임 밖으로 무언가를 보내면 미리보기 때도 보내진다.

```csharp
public static void Damage(GameApi game, Fighter source, Fighter target, int amount)
{
    // … 피해 계산(28장 5-3) …
    if (target is Enemy enemy && enemy.IsDead && !game.IsPreviewing)
        Telemetry.Kill(enemy.monster);       // 분석 전송(프로젝트 코드): 미리보기에서는 건너뛴다
}
```

- 가능하면 그런 일은 `SubscribeAfterAction` 받는 쪽에 둔다. 미리보기·실패한 행동에서는 오지 않으므로 `IsPreviewing`을 볼 필요도 없다.
- 플레이어 데이터를 바꾸는 일에는 `IsPreviewing`을 보지 않는다. 미리보기도 실제와 같게 돌아야 하고, 어차피 되돌아간다.

### 5-6. CSV로 수치 한꺼번에 고치기

**밸런싱 값**(30장 5-9)과 같은 흐름으로 인스턴스도 고친다. 예: 몬스터 체력·공격 표.

1. DB › 인스턴스 탭 → 맨 위 폴더 줄 **CSV** → **MonsterData 내보내기…** → `MonsterData.csv`.
2. 시트에서 연다. 칸은 `MonsterData`의 단순 필드(`title` · `maxHp` · `element` …)와 공격 패턴(`attackPattern`, 숫자 목록)의 줄마다 칸(`attackPattern[0]` …)이다. 초상화(스프라이트)는 나오지 않는다.

```
id,title,maxHp,attackPattern[0],attackPattern[1],attackPattern[2],element
dragon,드래곤,120,12,8,20,fire
goblin,고블린,20,6,,,neutral
slime,슬라임,14,4,4,,neutral
```

3. 세 몬스터의 `maxHp`를 고치고, 고블린의 공격 패턴을 `7,9`로 바꾼 뒤 CSV로 받는다.
4. **MonsterData 가져오기…** → "3개 고침, 0개 새로 만듦(적용 전 — Ctrl+S로 적용)." → 고친 몬스터가 "변경 · 적용 전"으로 보인다. 고블린의 공격 패턴은 `7`, `9` 두 개다(값이 있는 마지막 칸이 `attackPattern[1]`).
5. 저장(Ctrl+S).

- 새 몬스터 줄(`orc,오크,30,7,9,,neutral`)을 더해도 인스턴스는 잠긴 종류라 건너뛴다("없는 id 1개는 건너뜀 — 새 항목은 AI가 만든다."). 새 몬스터는 AI에게 요청한다.
- 시트에서 행을 지워도 DB 항목은 지워지지 않는다. 지우기는 DB에서 한다(21장 4절).
- 같은 파일을 팀이 함께 고치면 마지막에 가져온 값이 남는다. 가져오기 전에 DB 쪽 변경을 먼저 저장해 두면 되돌리기가 가져온 것만 버린다.
- 키워드 칸(`element`)은 글자 칸이라 아무 글자나 들어간다. 키워드 그룹에 없는 값은 가져온 뒤 검증 오류로 보인다(21장 6.1).
- 드래곤의 공격 패턴을 두 개로 줄이려면 `attackPattern[2]` 칸만 비운다. 공격 패턴 칸 셋을 모두 비우면 그 몬스터의 공격 패턴이 빈 목록이 된다(2-3).

### 5-7. 실시간 예산 — 판정 보고 행동 · 틱 묶음 · 저장 · 오브젝트 풀

행동 하나에는 일이 따라온다: 실행 직전에 참조 필드를 지금 객체로 다시 찾고, 끝날 때 바뀐 키를 Sync하고, 기록·Undo 단위·이벤트 받는 쪽이 돌고, 판 저장이 '행동마다'면 그 프레임 끝에 파일을 쓴다. 턴제는 1초에 행동 몇 개라 상관없다. 실시간 액션은 1초에 맞음이 수십 번이고 투사체가 수백 개다. 아래가 예산 안에 드는 모양이다.

| 무엇 | 예산 안에서 |
|---|---|
| 충돌·투사체·히트박스·미니게임 판정 | 화면(MonoBehaviour)이 판정하고 결과(누가 누구를 맞혔나·성공했나)만 행동으로 보낸다 = **판정 보고 행동**. 규칙은 결과로만 데이터를 바꾼다 |
| 한 틱에 맞음 여러 번 | 틱마다 묶어 행동 하나(`Strike { hits }`, 아래). 적 40마리를 한 번에 맞혀도 행동 하나 |
| 묶음 안의 대상 | **id 글자**로 두고(`Hit { foeId }`) 행동 안에서 목록에서 찾아 **없는 것은 건너뛴다**. 객체 참조 필드(`List<ArenaFoe>`)로 두면 보낸 사이 하나만 사라져도(같은 틱에 먼저 쓰러짐) 실행 직전 다시 찾기에서 묶음 전체가 실패한다(플레이어용 실패 `momoi.fail.targetGone`, 02장 2.2) |
| 위치·속도 | 화면 쪽이다(판 데이터에 넣지 않는다). 한 번 정해지는 위치(던진 폭탄이 터질 자리)만 행동 필드로 넘긴다 |
| `Game.Find` · `Game.All` | 플레이어 데이터를 훑고 행동 안에서 그 키를 한 번 Sync한다. 한 행동에서 같은 객체를 여러 번 찾지 말고 한 번 꺼내 변수로. 하나를 가리키는 행동 필드는 객체로 둔다(실행 직전에 한 번 찾는다) |
| 개체 여럿 만들기 | `Game.NewIds(접두어, n)`(번호를 한 번만 쓴다, 5-1) |
| 적 · 투사체에 상태 효과(화상·얼림)나 DB 기믹이 걸림 | 판 데이터의 **가벼운 목록**(id · 원본 ID · hp · 그 기믹이 읽는 칸만 — 위치는 화면 쪽, 5-1의 `arena.foes`). 상태 효과·대상 Block은 플레이어 데이터 안의 객체에만 걸린다 |
| 걸리는 것이 없는 개체 수백(순수 탄막·경험치 보석·이펙트) | 플레이어 데이터에 넣지 않는다. 화면 쪽 **오브젝트 풀**로 두고, 규칙에는 수(모은 보석 수·남은 탄)와 결과(맞음·주움)만 |
| 화면 다시 그리기 | 틱마다 바뀌는 키(적 목록 `arena`)와 HUD가 보는 키(레벨·경험치·골드 `run`)를 나눈다. 한 키 안의 무엇이 바뀌어도 그 키를 보는 화면 전체가 다시 그려진다 — 같은 키면 HUD가 초당 30번(35장 2-3 `Watch`) |
| 시간에 따라 바뀌는 값 | 틱마다 행동하지 않는다. 시각을 적어 두고 그때만(5-1) |
| 판 저장 | DB › Bootstrap 판 저장을 '행동마다 저장(턴제)' 대신 **'N초마다 저장(실시간·방치형)'**. '저장 간격(초)'(기본 30, 실제 시간)이 지났고 판 데이터가 바뀌었을 때만 저장하고, 끌 때도 저장한다(06장) |
| 연출 | `GameView.Present`는 차례로 하나씩 돌고 그동안 `Busy`라 입력이 막힌다. 실시간은 `GameView.Play`(32장 2-2·5-9) |
| Undo | 스냅샷 방식(5-1) |

**에디터**: DB › 밸런싱 값 `heroAttack` `5`.

**규칙** — 5-1의 `Rules/Arena.cs`에 더한다.

```csharp
[Serializable] public class Hit { public string foeId; public bool backstab; }      // 맞은 적 하나(화면이 판정)

[Serializable, GameEvent("적 맞음")] public class FoeHit { public string foeId; public int amount; }
[Serializable, GameEvent("적 쓰러짐")] public class FoeKilled { public string foeId; }

// 판정 보고 행동: 이번 틱에 히트박스가 닿은 적들. 피해 양은 규칙이 정한다(화면은 누구를 맞혔나만 보낸다).
[Serializable, BlockInfo("휘두르기 판정")]
public class Strike : GameAction
{
    public List<Hit> hits = new List<Hit>();

    protected override void Run()
    {
        var arena = Game.Data().Arena;                                   // 한 번 꺼내 변수로
        var power = (int)Game.Balance(GameIds.Balance.HeroAttack);
        foreach (var hit in hits)
        {
            var foe = arena.foes.Find(f => f.id == hit.foeId);          // 목록에서 직접(맞은 수만큼 Game.Find를 부르지 않는다)
            if (foe == null) continue;                                   // 같은 틱에 먼저 쓰러졌다 — 건너뛴다(묶음 전체를 실패시키지 않는다)
            ArenaRules.Damage(Game, arena, foe, hit.backstab ? power * 2 : power);
        }
    }
}

// ArenaRules에 더한다: 피해 규칙 함수. 실제 게임은 여기에 행동 직전 이벤트 자리를 둔다(28장 5-3).
public static void Damage(GameApi game, ArenaState arena, ArenaFoe foe, int amount)
{
    foe.hp -= amount;
    game.Events.Publish(new FoeHit { foeId = foe.id, amount = amount });
    if (foe.hp > 0) return;
    arena.foes.Remove(foe);
    game.Events.Publish(new FoeKilled { foeId = foe.id });
}
```

**화면** — 히트박스는 닿은 적을 모으기만 하고, 틱마다 한 번 행동으로 보낸다.

```csharp
// Client/StrikeReporter.cs
public static class StrikeReporter
{
    static readonly List<Hit> Pending = new List<Hit>();

    public static void Report(string foeId, bool backstab) => Pending.Add(new Hit { foeId = foeId, backstab = backstab });

    public static void Flush(GameSession session)
    {
        if (Pending.Count == 0) return;                                  // 거의 늘 여기서 끝난다
        var strike = new Strike { hits = new List<Hit>(Pending) };
        Pending.Clear();
        session.Do(strike);
    }
}

// Client/Bootstrap.cs — 규칙은 RegisterRules(모든 세션), 화면이 모은 판정을 보내는 일은 Started(화면 세션에만)
static void RegisterRules(GameSession session)
{
    ArenaRules.Register(session);
}

static void Started(GameSession session)
{
    session.Ticked += _ => StrikeReporter.Flush(session);              // 틱마다 한 번(초당 30틱이면 1초에 많아야 30개)
}

// 휘두를 때 잠깐 켜지는 히트박스(콜라이더 + 이 컴포넌트)
public class HeroHitbox : MonoBehaviour
{
    readonly HashSet<string> _hitThisSwing = new HashSet<string>();     // 한 번 휘두를 때 같은 적은 한 번

    void OnEnable() => _hitThisSwing.Clear();

    void OnTriggerEnter(Collider other)
    {
        var foe = other.GetComponentInParent<FoeView>();                // 적 그림(ViewList의 OnCreate에서 Id = 적 id)
        if (foe == null || !_hitThisSwing.Add(foe.Id)) return;
        var behind = Vector3.Dot(foe.transform.forward, (foe.transform.position - transform.position).normalized) > 0.5f;
        StrikeReporter.Report(foe.Id, behind);                          // 판정은 화면, 결과만 모은다
    }
}
```

- 적 40마리를 한 번에 맞혀도 행동 하나·Sync 한 번·타임라인 한 줄이다. 충돌마다 `Game.Do`를 부르면 같은 장면이 행동 40개다.
- 멀티 클라이언트 세션에는 `Ticked`가 오지 않아(2-1) 위 연결로는 `Flush`가 돌지 않는다. 클라이언트는 화면 쪽 프레임(컴포넌트의 `Update`)에서 1/틱 초마다 `Flush`해 서버로 보낸다(40장).
- 피해 양·등 뒤 배율은 규칙이 정한다. 화면은 판정(누구를·등 뒤였나)만 보낸다. 그래서 멀티플레이에서 화면이 값을 속일 수 없고, 행동 직전 이벤트·유물·상태 효과가 피해 규칙 함수의 자리에 그대로 붙는다.
- 맞은 숫자·쓰러지는 모습은 `FoeHit`·`FoeKilled`를 `SubscribeAfterAction`으로 받아 `GameView.Play`로 튼다(32장 5-9).
- 투사체는 화면 쪽 오브젝트 풀에서 꺼내고 돌려놓는다. 맞으면 같은 `StrikeReporter.Report`로 보고한다. 경험치 보석도 떨어지는 것은 화면(풀), 줍는 것은 판정 보고(`Collect { count }`)다. 규칙에는 모은 수만 있다.
- 이어 하기에 화면 쪽 위치가 필요하면 판 저장 직전 `GBibleRuntime.Saving`(행동 밖이라 담을 때는 `Game.Do`)에서 판 데이터에 담고, 불러온 뒤 `GBibleRuntime.RunLoaded`에서 다시 놓는다. 매 프레임 담지 않는다.
- `StrikeReporter`의 static 목록은 화면 쪽(Client)이다. 규칙에는 static 상태를 두지 않는다(관례 3절).

---

## 6. 주의할 점 · 자주 하는 실수

| 증상 | 원인과 해결 |
|---|---|
| 고정 틱을 켰는데 `Ticked`가 프레임마다 옴 | 초당 틱 수가 0이다. DB › Bootstrap 또는 `Configuring`의 `TickRate`. 코드가 0으로 덮지 않았는지 타임라인의 "코드(Configuring)가 … 바꿨습니다"를 본다. |
| `Ticked`에서 값을 바꿨더니 Undo·저장에서 빠짐 | `Ticked`는 행동 밖이다. `session.Do(행동)`으로 바꾼다. |
| 실시간 게임에서 Undo 이력이 금방 차고 느려짐 | 틱마다 행동을 실행했다. 시각을 적어 두고 그때만 실행한다(5-1). 실시간 게임은 Undo 방식을 스냅샷으로. |
| 프레임이 크게 끊긴 뒤 게임이 잠깐 느려짐 | 한 프레임에 8틱(배속이면 8 × 올림(배속), 최대 128)까지만 따라잡고 남은 시간은 버린다(2-1). 의도된 동작이다. |
| 서버·테스트에서 배속을 올렸더니 규칙 시간만 덜 흐름 | 세션을 직접 돌리면서 배속만큼 큰 `deltaTime`을 넘겼다. 한도가 8틱이라 남은 시간을 버린다. `session.HostSpeed`에 배속을 넣는다(Unity에서는 `GameTime`이 한다, 2-1). |
| 슬롯을 바꾼 뒤 틱 규칙이 안 돎 | 예전 세션의 `Ticked`에 연결했다. `Client/Bootstrap.cs`의 `RegisterRules`에서 등록 함수를 부른다(새 세션마다 다시 온다, 5-1). |
| 서버·테스트에서 틱 규칙이 안 돎 | 규칙 파일이 `[RuntimeInitializeOnLoadMethod]`·`GBibleRuntime.Started +=`로 스스로 걸었다(Unity 클라이언트에서만 돈다). 등록 함수(`ArenaRules.Register(session)`)로 바꾸고 `Bootstrap.RegisterRules`(`GBibleRuntime.SessionCreated` — 모든 세션)에서 부른다. 규칙 테스트는 세션을 만든 뒤 직접 부른다(5-1). |
| 멀티 클라이언트에서 `Ticked`가 오지 않음 | 정상이다. 클라이언트 세션은 시간 규칙을 돌리지 않는다(서버가 돌린다, 2-1). 화면 쪽 일(틱 묶음 보내기)은 `Update`에서(5-7). |
| 휘두를 때마다 프레임이 떨어지고 타임라인이 금방 참 | 충돌마다 `Game.Do`를 불렀다. 닿은 적을 모았다가 틱마다 행동 하나로 보낸다(`Strike { hits }`, 5-7). |
| 판정 보고가 가끔 통째로 실패("대상이 이미 없습니다.") | 묶음의 대상을 객체 참조 필드로 넣었다. 하나가 먼저 쓰러지면 다시 찾기에서 묶음 전체가 실패한다. id 글자로 두고 없는 것을 건너뛴다(5-7). |
| 규칙 테스트에서 `EndFrame(1.0)`을 불렀는데 1초 규칙이 안 돎 | 한 번에 한도(8틱)까지만 돈다. `session.Advance(1.0)`(4-1). |
| 적에게 화상·얼림 상태 효과를 걸 수 없음 | 적을 화면 쪽 오브젝트로만 두었다. 상태 효과는 판 데이터의 객체에만 걸린다 — 적을 판 데이터의 가벼운 목록으로(5-1·5-7). |
| 실시간 게임이 행동이 많은 순간마다 끊김 | 판 저장이 '행동마다 저장'이라 행동이 끝난 프레임마다 파일을 쓴다. 'N초마다 저장(실시간·방치형)'으로(5-7). |
| 투사체·보석이 많아지면 느려짐 | 투사체·보석을 판 데이터의 목록에 넣었다(Sync·저장이 개수만큼). 화면 쪽 오브젝트 풀로 옮기고 규칙에는 수와 결과만(5-7). |
| 실시간인데 입력이 자꾸 막힘 | 연출을 `GameView.Present`로 넣었다. 대기열이 차례로 도는 동안 `Busy`라 입력 창구가 막는다. `GameView.Play`로(32장 2-2). |
| 화면이 보낸 피해 양이 그대로 들어감 | 판정 보고 행동에 양(`amount`)을 넣었다. 화면은 누구를 맞혔나만 보내고 양은 규칙이 밸런싱 값에서 정한다(5-7). |
| 일시정지했는데 웨이브 시간이 흐름 | 시각을 `Time.time`으로 쟀다. 게임 시계(`session.Clock.Time`)로 적는다(07장). |
| 막대가 계단처럼 움직임 | 틱마다 뛰는 게임 시계를 그대로 그렸다. `TickAlpha`로 보간한다(5-2). |
| `Game.Preview`가 "행동 안에서는 미리보기를 할 수 없습니다" | 행동·`[BindTo]`·Block 안에서 불렀다. 화면 코드에서 부른다. |
| `CanDo`가 늘 거짓이고 이유가 "행동 안에서는 미리보기를…" | 같은 원인. `CanDo`는 예외 대신 거짓을 돌려준다. |
| 미리보기 값과 실제 값이 다름 | 미리보기와 실제 사이에 데이터가 바뀌었거나(다른 카드를 먼저 냄), 규칙 코드가 `UnityEngine.Random`·`Time.time` 같은 세션 밖의 값을 쓴다. 규칙은 `Game.Random`·게임 시계만 쓴다. |
| 미리본 체력이 늘 지금 체력 | `read`가 데이터 객체를 돌려주고 나중에 읽었다. 되돌린 뒤의 객체다. `read` 안에서 숫자를 꺼낸다. |
| 마우스를 올렸을 뿐인데 피해 숫자가 뜸 | 연출을 `Events.Subscribe`로 받았다. 미리보기에서도 불린다. `SubscribeAfterAction`으로(32장). |
| 핸드를 다시 그릴 때마다 느려짐 | `bind`에서 카드마다 `CanDo`·`Preview`로 행동 전체를 돌린다. 카드가 많거나 행동이 무거우면 마우스를 올릴 때만 부른다(5-3). |
| 테스트에서 `Assert.Throws<InvalidOperationException>(() => Game.Preview(…))`가 실패 | 미리보기는 행동의 예외를 그대로 던진다. 플레이어용 실패면 `GameFailure`라, 정확한 형식만 받는 `Assert.Throws`에는 `Assert.Throws<GameFailure>`를 쓴다(30장 6절). |
| 미리보기로 분석 이벤트가 전송됨 | 규칙 코드에서 밖으로 보냈다. `Game.IsPreviewing`을 보거나 `SubscribeAfterAction`으로 옮긴다(5-5). |
| CSV에 트리거·조건·결과 Block이 없음 | Block 칸은 CSV에 나오지 않는다(목록 줄 안의 조건도, 2-3). DB에서 고친다. |
| 곡선의 점 칸(`curve.points[0].x` …)이 하나도 없음 | 내보낸 항목 모두 그 목록이 비어 있다. 칸은 목록에 든 것만큼만 생긴다. 머리 줄에 직접 적으면 가져올 때 그 번호가 생긴다(2-3). |
| 가져왔더니 목록이 통째로 비워짐 | 그 목록의 칸이 머리 줄에 있는데 그 줄에서 모두 빈칸이다(길이 0으로 맞춘다). 그대로 두려면 칸을 채우거나 그 목록의 칸을 머리 줄에서 지운다. |
| 목록 가운데 번호의 칸을 비웠는데 그 값이 남음 | 목록 칸의 빈칸은 읽지 않는다. 길이는 값이 있는 마지막 번호로 정해진다. 뒤의 값을 당겨 적고 마지막 번호를 비운다(2-3). |
| 시트에서 더한 가중치 표 항목 줄의 조건이 비어 있음 | 새로 생긴 번호는 기본값에서 시작하고 Block 칸은 비어 있다. 조건은 DB에서 고른다. |
| 가져왔는데 게임에 반영 안 됨 | 적용 전(작업본)이다. 저장(Ctrl+S). |
| 가져오기 결과에 "모르는 칸(건너뜀)" | 칸 이름이 필드 이름과 다르다(시트에서 머리 줄을 고쳤다). 내보낸 머리 줄을 그대로 쓴다. |
| "숫자가 아닙니다('')" | 목록이 아닌 숫자 칸을 비웠다. 빈칸은 숫자로 읽지 않는다(그 칸은 그대로). 0을 적는다. |
| "이 칸의 정수 형식에 들어가지 않는 크기입니다" | 필드가 `int`(약 ±21억)·`byte`(0~255) 같은 형식인데 그보다 큰 값을 적었다. 그 칸은 그대로다. 더 큰 값이 필요하면 필드 형식을 `long`·`double`로(AI에게 요청). |
| "쉼표가 천 단위인지 소수점인지 알 수 없습니다" | `0,500`처럼 쉼표 뒤가 세 자리면 천 단위로도 소수로도 읽힌다. 시트의 숫자 서식을 쉼표 없이(소수점은 `.`). |
| enum 칸에 "고를 수 있는 값이 아닙니다" | 코드 이름도 DB 이름도 아니다. 오류 줄에 고를 수 있는 코드 이름이 나온다. |
| 엑셀에서 한글이 깨짐 | 직접 만든 파일이 UTF-8(BOM)이 아니다. 내보낸 파일을 고쳐 쓰거나, 엑셀에서 "CSV UTF-8"로 저장한다. |
| 시트에서 지운 줄이 DB에 남음 | 가져오기는 지우지 않는다. DB에서 지운다. |
