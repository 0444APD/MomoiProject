# 32. 화면 연결·연출 대기열·씬 흐름·월드 상태

**한 줄 요약**: 규칙(행동)은 즉시 끝나고 화면은 그 결과를 따라간다. **다시 그리기**(`GameView.Redraw`)는 플레이어 데이터나 언어가 바뀌면 프레임 끝에 한 번 그린다. **ViewList**는 데이터 목록(적·손패)을 프리팹 목록에, 컴포넌트 **데이터 연결**은 필드 하나(에너지)를 글자·숫자에 코드 없이 잇는다. 피해 숫자·죽는 모습 같은 연출은 **연출 대기열**(`GameView.Present`)에서 차례로 하나씩 돌고, 다시 그리기는 연출이 끝날 때까지 기다린다. 그래서 죽는 연출 전에 적이 화면에서 사라지지 않는다. 실시간 게임의 맞는 효과·피해 숫자는 대기열 밖(`GameView.Play`)에서 바로 튼다(줄 서지 않고 입력을 막지 않는다). **씬 흐름**은 판 데이터의 필드(`run.scene`)가 가리키는 씬을 화면이 따라 연다. **월드 상태**(`Game.SetWorld`)는 씬에 놓인 것(열린 상자·쓰러뜨린 보스·켜 둔 스위치)의 상태를 이름으로 기억하고, 컴포넌트 **월드 상태 연결**이 그 값대로 오브젝트를 켜고 끈다. 화면 쪽은 데이터를 읽기만 하고, 바꾸는 일은 모두 행동이다.

> UI Toolkit 화면(HUD·메뉴·팝업)은 [35장](35-Screens.md)의 DB › 화면 · `GameScreen` · binding-path로 만든다(인스펙터 연결 없음). 이 장의 `ViewList`·'데이터 연결'은 씬에 놓이는 오브젝트(캐릭터·카드 오브젝트)와 UGUI용이고, 다시 그리기·연출 대기열은 두 쪽이 함께 쓴다.

**언제 쓰는가**

- 체력·골드·에너지 표시, 적 목록·손패처럼 데이터를 그대로 그리는 화면(다시 그리기 · ViewList · 데이터 연결).
- 피해 숫자, 카드가 날아가는 모습, 적이 쓰러지는 모습처럼 시간을 두고 차례로 보여 줄 것(연출 대기열).
- 연출 중에 입력을 막고, 클릭하면 빨리 넘기고, 설정에서 연출 속도를 바꿀 때(`Busy` · `SkipPresentation` · `PresentationSpeed`).
- 실시간 게임의 맞는 효과·피해 숫자, 방치형의 클릭 숫자처럼 줄 서지 않고 바로·여럿이 겹쳐 뜨고 입력을 막지 않을 것(대기열 밖 연출, `GameView.Play`).
- 맵 → 전투 → 맵, 판이 끝나면 타이틀처럼 판 진행에 따라 씬이 바뀔 때(씬 흐름).
- 열린 상자는 다시 열려 있고, 잡은 보스는 다시 나오지 않고, 마을 문은 다음 판에도 열려 있을 때(월드 상태).

이 문서에서 쓰는 용어:

| 용어 | 뜻 |
|---|---|
| 다시 그리기 (`GameView.Redraw`) | 그리는 함수를 등록해 두면, 보는 키의 값이나 언어가 바뀐 프레임의 끝에 한 번 부른다. |
| 키 | 플레이어 데이터 ID(`run`). 다시 그리기가 "이 값이 바뀌면"을 정하는 단위다. |
| 연출 대기열 | 연출을 차례로 하나씩 재생하는 줄(`GameView.Present`). 하나가 끝나야 다음이 시작한다. |
| 연출 하나 | 대기열의 한 칸: 코루틴(`IEnumerator`) · 완료 콜백(`done`을 부르면 끝) · 쉬기(초). |
| 대기열 밖 연출 (`GameView.Play`) | 대기열에 넣지 않고 바로 트는 연출. 여럿이 동시에 돌고 `Busy`·다시 그리기와 상관없다(입력을 막지 않는다). 실시간 게임용. |
| `Busy` | 연출이 남아 있거나 선택을 기다리는 중. 이때 입력을 막는다. |
| 화면 오브젝트 등록 | 데이터 개체의 id ↔ 화면의 GameObject(`GameView.Register`·`Find`). 연출이 "그 적의 그림"을 찾을 때 쓴다. |
| ViewList | 데이터 목록 ↔ 화면 오브젝트 목록. 항목(id)마다 프리팹 하나를 만들고·없애고·순서를 맞추고·다시 그린다. |
| 데이터 연결 (`DataBinding`) | 컴포넌트 '데이터 연결'. 플레이어 데이터의 필드 하나를 글자·숫자·참거짓 이벤트로 보낸다(코드 없음). |
| 씬 흐름 (`GameScenes`) | 판 데이터의 필드(씬 이름)를 따라 씬을 여는 것. |
| 전환 연출 (`Transition`) | 씬 열기를 감싸는 코루틴(페이드 아웃 → 열기 → 페이드 인). |
| 월드 상태 | 이름 → 글자 값. 판 값(`$world`)과 프로필 값(`$world.meta`). 플레이어 데이터다. |
| 월드 상태 연결 (`WorldStateBinding`) | 컴포넌트 '월드 상태 연결'. 월드 상태 값에 따라 오브젝트를 켜고 끈다(코드 없음). |

---

## 1. 흐름 한눈에

```
입력(버튼)
  └ Game.Do(카드 내기)                 데이터는 이 줄이 끝나면 이미 최종 모습(적은 전투에서 빠졌고 체력은 깎였다)
행동이 성공
  └ SubscribeAfterAction 받는 쪽        피해 · 적 처치 이벤트를 쏜 순서대로 받는다
       └ GameView.Present(피해 숫자)    대기열에 쌓인다(곧바로 재생 시작)
프레임 끝(LateUpdate)
  연출이 남아 있다 → 연출을 기다리는 다시 그리기(ViewList · 데이터 연결 · 씬 흐름 · 월드 상태 연결)는 미룬다
  … 피해 숫자가 끝나 대기열이 빈다 …
  └ 다시 그리기: 적 목록 ViewList가 사라진 적을 찾는다
       └ OnRemove → 대기열에 "죽는 모습" → 끝나면 그 적의 오브젝트를 없앤다
```

> **규칙: 화면은 데이터를 읽기만 한다.** 버튼은 행동을 보내고(`Game.Do`), 그림은 데이터에서 그린다. 화면이 데이터를 직접 고치면 Undo·저장·멀티플레이가 그 변경을 모른다.

> **규칙: 연출은 `SubscribeAfterAction` → `Present`.** 행동이 성공한 뒤에만 받으므로 실패해 취소된 행동의 피해 숫자가 뜨지 않는다. 바로 받는 쪽(`Events.Subscribe`)은 규칙용이다. 실패한 행동·미리보기 실행(33장)에서도 불린다.

---

## 2. 동작 규칙

### 2-1. 다시 그리기

- 등록하면(`GameView.Redraw(draw, 키…)`) **그 프레임 끝에 처음 한 번** 그린다. 그 뒤로는 보는 키의 값이 바뀌었거나 언어가 바뀐 프레임의 끝(`LateUpdate`, 행동 밖)에 **한 번만** 그린다. 키를 비우면 어느 값이 바뀌어도 그린다.
- 알림은 한 행동에도 여러 번 온다(중간 Sync·실패한 행동의 되돌림·Undo). 다시 그리기는 표시만 해 두었다가 프레임 끝에 모아 한 번 그린다(23장 4절). 행동이 실행 중이면 다음 프레임으로 미룬다.
- **연출을 기다린다(기본).** 연출이 남아 있으면 그 연출이 다 끝난 뒤의 프레임 끝까지 미룬다. 기다리지 않을 화면(골드·에너지처럼 늘 지금 값이어야 하는 것)은 `Redraw(draw, waitForPresentation: false, 키…)`.
- **처음 한 번은 기다리지 않는다.** 연출 중이어도 등록한 프레임 끝에 지금 값으로 그린다(새로 연 화면·씬 전환 뒤의 새 씬은 그 전 모습이 없다). 기다리기는 그 뒤의 다시 그리기부터다. 씬 흐름(2-6)은 처음 열기도 연출을 기다린다.
- **세션이 바뀌면**(슬롯 바꾸기·다시 시작, 31장) 새 세션에 다시 연결하고 모두 다시 그린다.
- 그리는 함수가 예외를 내면 콘솔에 찍히고 다른 화면은 계속 그린다.
- `Dispose`하면 그만 그린다(`OnDisable`에서).

### 2-2. 연출 대기열

- `Present`로 넣은 연출은 넣은 순서대로 **하나씩** 돈다. 앞의 것이 끝나야 다음이 시작한다. 연출 하나는 세 가지다.

| 넣는 것 | 끝나는 때 |
|---|---|
| 코루틴 `Present(IEnumerator)` | 코루틴이 끝날 때. 안에서 `yield return` 한 코루틴도 기다린다 |
| 완료 콜백 `Present(done => …)` | 연출 코드가 `done()`을 부를 때(트윈 라이브러리의 완료 콜백 등). 제한 시간(기본 실제 30초) 안에 `done()`이 오지 않으면 콘솔에 경고하고 다음으로 넘어간다 |
| 쉬기 `Present(초)` | 그 시간이 지날 때(속도 반영) |

- **남아 있는 동안**(재생 중 포함) `IsPresenting`이 참이다. `Busy`는 연출이 남아 있거나 선택을 기다리는 중(`Game.IsWaiting`, 29장)이면 참이다. 입력 창구가 `Busy`로 입력을 막는다(5-5). 시스템은 행동을 막지 않는다.
- 대기열이 비면(마지막 연출이 끝난 순간) `Presented` 이벤트를 쏜다.
- **속도**: `PresentationSpeed`(기본 1, 2 = 두 배). 연출 코드는 시간을 `GameView.DeltaTime`(= `Time.deltaTime` × 속도, 건너뛰기 중이면 ×10)이나 `GameView.Wait(초)`로 잰다. 그래야 속도·건너뛰기가 먹는다. `WaitForSeconds`·`Time.deltaTime`으로 잰 연출은 속도를 따르지 않는다.
- **건너뛰기**: `SkipPresentation()`은 남은 연출을 ×10으로 돌린다. 대기열이 비면 원래 속도로 돌아온다. 연출이 없을 때 부르면 아무 일도 없다.
- **버리기**: `ClearPresentation()`은 남은 연출을 버리고 재생 중인 것은 그 자리에서 멈춘다(씬을 직접 떠날 때). `Presented`도 쏜다.
- **연출 하나가 예외를 내면** 콘솔에 찍히고 그 연출만 끝난다. 대기열은 계속 돈다(안쪽 코루틴의 예외도 같다).
- 연출은 `[MomoiProject]` 오브젝트가 돌린다(씬이 바뀌어도 계속 돈다). 일시정지(`Time.timeScale` 0) 중에는 `DeltaTime`이 0이라 시간을 재는 연출이 멈춘다.
- 행동은 연출을 기다리지 않는다. 연출이 도는 동안에도 `Game.Do`는 그대로 실행된다(입력을 막는 것은 화면의 일).

**대기열 밖 연출 — `GameView.Play`**

대기열은 턴제에 맞춘 것이다. 실시간 게임에서 맞을 때마다 `Present`로 넣으면 피해 숫자가 줄을 서서 하나씩 늦게 뜨고, 그동안 `IsPresenting`·`Busy`가 참이라 입력 창구(`Busy`로 막는 버튼·`GameScreens.Do`)가 **입력을 계속 막는다**. 다시 그리기도 연출이 끝날 때까지 미뤄진다. 실시간 게임의 연출은 `Play`로 튼다.

| | `Present`(대기열) | `Play`(대기열 밖) |
|---|---|---|
| 도는 순서 | 넣은 순서대로 하나씩 | 부른 즉시, 여럿이 동시에 |
| `IsPresenting` · `Busy` | 남아 있는 동안 참 → 입력을 막는다 | 상관없다(입력을 막지 않는다) |
| 다시 그리기(연출 기다리기) | 끝날 때까지 미룬다 | 미루지 않는다 |
| `Presented` · `SkipPresentation` · `ClearPresentation` | 따른다 | 상관없다(`ClearPresentation`도 멈추지 않는다) |
| 시간(`GameView.DeltaTime` · `Wait`) | 속도·건너뛰기 반영 | 같다(대기열을 건너뛰는 동안이면 함께 ×10) |
| 쓰는 곳 | 턴제의 피해 숫자·카드 이동·죽는 모습 | 실시간의 맞는 효과·피해 숫자, 방치형의 클릭 숫자 |

- `Play(코루틴)` · `Play(done => …, timeout)` 두 가지다. 완료 콜백 쪽은 `Present`와 같이 제한 시간(기본 실제 30초) 안에 `done()`이 없으면 경고한다.
- 연출은 `[MomoiProject]` 오브젝트가 돌린다. 씬이 바뀌어도 계속 돌므로, 오브젝트가 먼저 없어질 수 있는 연출은 돌면서 `null`인지 본다. 예외가 나면 그 연출만 멈춘다(콘솔에 찍힌다).
- 게임이 시작되기 전(호스트가 없을 때) 부르면 틀지 않고 경고 "[MomoiProject] GameView.Play: 게임이 시작되지 않아 연출을 틀 수 없습니다."를 남긴다(`Present`는 넣어 두었다가 호스트가 생기면 튼다).
- 받는 쪽은 같다: `SubscribeAfterAction` → `Play`. 실패한 행동·미리보기의 이벤트는 오지 않는다.
- ViewList의 `OnRemove`(죽는 모습)는 대기열에 들어간다(2-4). 실시간에서 적이 쓰러질 때마다 `Busy`가 되므로, 실시간 적 목록은 `OnRemove`를 비워 그림을 바로 없애고, 죽는 모습은 그림의 자리를 먼저 읽어 두었다가 `Play`로 그 자리에 따로 튼다(5-9).

### 2-3. 화면 오브젝트 찾기

- `GameView.Register(id, 오브젝트)`로 등록하고 `Find(id)` · `Find<컴포넌트>(id)`로 찾는다. ViewList가 만든 오브젝트는 저절로 등록된다(항목의 id로).
- 오브젝트가 없어졌으면(씬을 떠남·`Destroy`) `Find`는 `null`을 돌려주고 등록을 지운다.
- 같은 id로 새 오브젝트를 등록하면 바뀐다. `Unregister(id, 오브젝트)`처럼 오브젝트를 주면 그 오브젝트로 등록되어 있을 때만 지운다.

### 2-4. ViewList

- 다시 그리기 위에서 돈다(보는 키 · 연출 기다리기 규칙이 같다).
- 그릴 때마다 목록을 처음부터 훑어 항목마다:
  1. 항목의 **열쇠**를 정한다: 항목이 글자면 그 글자, 아니면 `id` 필드. 같은 열쇠가 두 번째로 나오면 `"id#2"`, 세 번째는 `"id#3"`.
  2. 그 열쇠의 오브젝트가 없으면 프리팹을 부모 아래에 만들고, 첫 번째 것은 `GameView.Register(id)`로 등록하고, `OnCreate(view, item)`을 부른다.
  3. 목록 순서대로 자리(sibling 순서)를 맞추고 `bind(view, item)`으로 그린다(매번).
- 목록에 없는 열쇠의 오브젝트는 등록을 지운 뒤 없앤다. `OnRemove`가 있으면 바로 없애지 않고 **연출 대기열에 넣어** `OnRemove(view, done)`을 부르고, `done()` 뒤에 없앤다.
- `bind`가 예외를 내면 콘솔에 찍히고 다른 항목은 계속 그린다.
- 목록 함수가 `null`을 돌려주면 빈 목록으로 본다(전투 밖이면 `battle?.enemies`가 `null` → 적 오브젝트를 모두 없앤다).
- `Dispose()`는 다시 그리기를 끊고 만든 오브젝트를 모두 없앤다(`OnRemove` 없이). `OnDestroy`에서 부른다.

### 2-5. 데이터 연결

- 키(플레이어 데이터) · 필드 경로를 정하면, 그 키가 바뀐 프레임 끝에 값을 읽어 세 이벤트를 부른다.

| 이벤트 | 보내는 값 |
|---|---|
| `onText` (`string`) | 글자 틀(`{value}` 자리에 값)로 만든 글. 틀이 텍스트 키면 그 키의 번역된 틀 |
| `onNumber` (`float`) | 숫자로 바꾼 값(참거짓은 1/0, 숫자 글자는 그 숫자, 못 바꾸면 0) |
| `onBool` (`bool`) | 참거짓 값이면 그대로, 아니면 숫자가 0이 아니거나 글자가 비어 있지 않으면 참 |

- 값이 목록이면 `{value}`는 **개수**다(손패 장수).
- 필드 경로의 중간이 비어 있으면(전투 밖의 `battle.energy`) 값이 없다.
- 기본은 연출을 기다린다(`waitForPresentation`). 켜고 끄기는 컴포넌트가 켜질 때(`OnEnable`) 정해진다.

### 2-6. 씬 흐름

- `GameScenes.Follow(키, 필드, 빈 값일 때 씬)` 또는 DB › Bootstrap의 '씬 흐름(선택)'(3-1). 그 키가 바뀐 프레임 끝에 필드의 글자(씬 이름)를 읽는다.
  - 글자가 지금 씬과 같으면 아무 일도 없다.
  - 비어 있으면 '빈 값일 때 씬'을 연다(그것도 비면 그대로).
  - Build Settings의 씬 목록에 없는 씬이면 열지 않고 타임라인에 경고 "씬 'Battel'을(를) 열 수 없습니다(Build Settings의 씬 목록에 있는지 확인)."를 남긴다.
- **규칙은 행동 안에서 그 필드만 바꾼다**(`run.scene = "Battle"`). 씬을 직접 열지 않는다. 그래서:
  - 이어 하기로 판을 불러오면 그 판의 씬이 열린다.
  - 새 판·판 끝으로 값이 비면 '빈 값일 때 씬'(보통 타이틀)이 열린다.
  - Undo로 값이 돌아가면 씬도 돌아간다.
  - 한 행동 안에서 여러 번 바꿔도 프레임 끝의 마지막 값 하나만 본다.
- **연출이 끝난 뒤에 연다**(다시 그리기의 기다리기). 여는 동안은 연출 대기열의 한 칸이라 `Busy`가 참이다(입력이 막힌다). 연 뒤 `GameScenes.Loaded(씬 이름)`를 쏜다.
- 전환 연출(`GameScenes.Transition`)을 넣으면 씬 열기를 그 코루틴으로 감싼다(5-7).
- 슬롯을 바꾸면(31장) 런타임이 다시 따라가게 한다.

### 2-7. 월드 상태

- `Game.SetWorld(이름, 값)`: 판 값(`$world`, 세션 구역). `permanent: true`면 프로필 값(`$world.meta`, 메타 구역).
- `Game.World(이름)`: **판 값이 프로필 값보다 앞선다.** 판 값이 없으면 프로필 값, 둘 다 없으면 `null`.
- `Game.WorldFlag(이름)`: 값이 있고 `"false"`·`"0"`이 아니면 참.
- `Game.SetWorld(이름, true)`는 `"true"`를 넣고, `Game.SetWorld(이름, false)`(또는 값 `null`)는 그 구역의 값을 지운다. 단 판 값을 끌 때 같은 이름의 프로필 값이 있으면 지우는 대신 `"false"`를 둔다. 그래서 이번 판에서는 확실히 꺼지고, 새 판이 되면 프로필 값이 다시 보인다.
- `Game.ClearWorld(이름)`: 판 값과 프로필 값을 모두 지운다.
- 행동 안에서 바꾼다. 플레이어 데이터라서 Undo·판 저장·멀티플레이가 그대로다. 처음 바꿀 때 키가 저절로 정의된다.
- 새 판이면 판 값은 사라지고 프로필 값은 남는다.
- 이름이 비면 `ArgumentException`("[MomoiProject] 월드 상태 이름이 비어 있습니다."). 이름은 **"씬 이름.오브젝트 이름"**(`Map.chest3`)을 권한다. 씬마다 같은 오브젝트 이름이 있어도 겹치지 않는다.

### 2-8. 월드 상태 연결

- 이름 · 값을 정하면, 월드 상태(판·프로필 둘 다)가 바뀐 프레임 끝에 **맞음**을 정한다: 값을 비우면 `WorldFlag`가 참일 때, 적으면 값이 그 글자와 같을 때.
- 맞으면 '맞을 때 켤 것'을 켜고 '맞을 때 끌 것'을 끈다. 아니면 반대. 이 컴포넌트가 붙은 오브젝트 자신은 켜고 끄지 않는다(자기를 끄면 다시 켤 수 없다) → 대상은 자식 등 다른 오브젝트다.
- 그다음 `onMatched(맞음)`·`onValue(값, 없으면 빈 글자)`를 부른다.
- 다시 그리기 위에서 돌므로 연출을 기다리고, 씬을 다시 열거나 판을 불러와도 그 상태로 보인다.

---

## 3. 에디터에서 쓰는 법

### 3-1. DB › 시작 파일 › Bootstrap — 씬 흐름(선택)

`MomoiProject → DB` → 사이드바 **시작 파일 › Bootstrap** → **씬 흐름(선택)**. 고친 값은 바로 저장된다(Ctrl+S 필요 없음).

| 칸 | 값 |
|---|---|
| 플레이어 데이터 | 씬 이름이 든 플레이어 데이터(`run`). `(쓰지 않음)`이면 씬 흐름을 끈다 |
| 필드(씬 이름이 든 글자) | `scene` 또는 `map.scene`. 비우면 값 전체(글자 플레이어 데이터) |
| 빈 값일 때 씬 | 새 판·판 끝으로 값이 비었을 때 열 씬(보통 타이틀). Build Settings의 씬 목록에서 고른다. `(그대로)`면 열지 않는다 |

### 3-2. 컴포넌트 '데이터 연결'

글자(TMP_Text 등)가 있는 오브젝트에 **Add Component → MomoiProject → 데이터 연결**.

| 칸 | 툴팁 | 값 |
|---|---|---|
| `key` | 플레이어 데이터 ID(DB › 플레이어 데이터) | `run` |
| `path` | 필드 경로(비우면 값 전체). 예: battle.energy | `battle.energy` |
| `format` | 글자 틀. {value}가 값. 텍스트 키를 적으면 번역된 틀 | `{value} / 3`, 또는 텍스트 키 `hud.energy` |
| `waitForPresentation` | 연출 대기열이 끝난 뒤에 바꾼다 | 기본 켬 |
| `onText` · `onNumber` · `onBool` | (이벤트) | 인스펙터의 `+`로 대상과 속성을 고른다: `TMP_Text.text`(글자) · `Slider.value`(숫자) · `GameObject.SetActive`(참거짓) |

이벤트는 Unity의 UnityEvent다. **Dynamic** 쪽 속성(`text`·`value`·`SetActive`)을 고르면 보낸 값이 들어간다.

### 3-3. 컴포넌트 '월드 상태 연결'

씬의 오브젝트(상자의 부모 등)에 **Add Component → MomoiProject → 월드 상태 연결**.

| 칸 | 툴팁 | 값 |
|---|---|---|
| `id` | 월드 상태 이름(씬 이름.오브젝트 이름 권장) | `Map.chest3` |
| `value` | 이 값일 때 '맞음'. 비우면 값이 있고 거짓이 아니면 맞음 | `opened` |
| `showWhenMatched` | 맞을 때 켜고, 아닐 때 끈다 | 열린 상자 모습 |
| `hideWhenMatched` | 맞을 때 끄고, 아닐 때 켠다 | 닫힌 상자 모습 |
| `onMatched` · `onValue` | (이벤트) | 맞음(`bool`) · 값(`string`)을 받을 곳 |

### 3-4. 모니터에서 확인하기

- **데이터** 탭: 월드 상태는 `$world`("월드 상태", 판 데이터)·`$world.meta`("영구 월드 상태", 프로필 데이터) → `values`에 이름 → 값이 보인다.
- 씬 흐름의 경고(Build Settings에 없는 씬)는 타임라인과 콘솔에 남는다.
- 연출 대기열·다시 그리기는 화면의 일이라 모니터에 남지 않는다. 행동이 바꾼 값은 행동 탭·타임라인에서 본다.

---

## 4. 코드로 쓰는 법 — 문법 요소를 하나씩

### 4-1. `GameView.Redraw` — 다시 그리기

```csharp
void OnEnable()  => _view = GameView.Redraw(Redraw, GameIds.PlayerData.Run.Id);                              // 연출을 기다린다
void OnEnable()  => _gold = GameView.Redraw(DrawGold, waitForPresentation: false, GameIds.PlayerData.Run.Id);  // 기다리지 않는다
void OnDisable() => _view?.Dispose();
```

| 멤버 | 뜻 |
|---|---|
| `IDisposable Redraw(Action draw, params string[] keys)` | 키(비우면 아무 키나)·언어가 바뀌면 프레임 끝에 `draw`를 한 번. 연출을 기다린다(처음 한 번은 기다리지 않는다) |
| `IDisposable Redraw(Action draw, bool waitForPresentation, params string[] keys)` | 기다릴지 정한다 |

키는 `GameIds.PlayerData.X.Id`(또는 ID 글자)다. 라이브러리의 키(`ChoiceSystem.Key`, `WorldStateSystem.SessionKey` …)도 쓴다.

### 4-2. `GameView.Present` · `Play` — 연출 대기열 · 대기열 밖 연출

```csharp
GameView.Present(ShowDamage(e));                          // 코루틴
GameView.Present(done => cardView.FlyTo(slot, done));     // 끝나면 done()
GameView.Present(0.3f);                                   // 0.3초 쉬기
GameView.Play(ShowHit(e));                                // 대기열 밖: 바로, 여럿이 동시에, 입력을 막지 않는다(실시간)
```

| 멤버 | 뜻 |
|---|---|
| `void Present(IEnumerator routine)` | 코루틴 하나를 넣는다(`null`이면 무시). 부를 때마다 새 코루틴(`Show(e)`)을 넘긴다 |
| `void Present(Action<Action> step, float timeout = 30f)` | 완료 콜백 연출. `step(done)`을 부르고 `done()`까지 기다린다. `timeout`(실제 시간 초) 안에 오지 않으면 경고("[MomoiProject] 연출이 30초 안에 끝나지 않아 넘어갑니다(done()을 부르지 않았거나 오브젝트가 먼저 없어졌는지 확인).")하고 넘어간다. 0이면 끝없이 기다린다 |
| `void Present(float seconds)` | 쉬기(속도 반영) |
| `void Play(IEnumerator routine)` | 대기열 밖에서 바로 튼다(`null`이면 무시). `IsPresenting`·`Busy`·다시 그리기와 상관없다(2-2). 게임이 시작되기 전이면 경고하고 틀지 않는다 |
| `void Play(Action<Action> step, float timeout = 30f)` | 완료 콜백 연출을 대기열 밖에서. `timeout`은 `Present`와 같다 |
| `bool IsPresenting` | 남은 연출이 있다(재생 중 포함). `Play`로 튼 것은 세지 않는다 |
| `bool Busy` | `IsPresenting` 또는 `Game.IsWaiting`. 입력을 막을 때 |
| `event Action Presented` | 대기열이 비었을 때 |
| `float PresentationSpeed` | 연출 속도(기본 1). 매 플레이 시작 때 1로 돌아간다 |
| `float DeltaTime` | 이번 프레임의 연출 시간(속도·건너뛰기 반영) |
| `IEnumerator Wait(float seconds)` | 연출 안에서 쓰는 기다리기(`yield return GameView.Wait(0.5f)`). `WaitForSeconds` 대신 |
| `void SkipPresentation()` | 남은 연출을 ×10으로. 대기열이 비면 원래대로 |
| `void ClearPresentation()` | 남은 연출을 버린다. 재생 중인 것은 멈춘다(`Play`로 튼 것은 그대로 돈다) |

### 4-3. `GameView.Register` · `Find` — 화면 오브젝트

```csharp
void Awake() => GameView.Register("hero", gameObject);                 // 주인공 그림(판마다 id가 "hero")
var view = GameView.Find<EnemyView>(e.targetId);                       // 연출이 그 적의 그림을 찾는다
```

| 멤버 | 뜻 |
|---|---|
| `void Register(string id, GameObject view)` | 등록(같은 id면 바꾼다). id가 비거나 `null`이면 무시 |
| `void Unregister(string id, GameObject view = null)` | 지운다. `view`를 주면 그 오브젝트로 등록되어 있을 때만 |
| `GameObject Find(string id)` | 찾기(없거나 없어졌으면 `null`) |
| `T Find<T>(string id) where T : Component` | 그 오브젝트의 컴포넌트 |

### 4-4. `ViewList<TItem, TView>`

```csharp
_enemies = new ViewList<Enemy, EnemyView>(enemyPrefab, enemyRoot,
    () => Game.Data().Run.battle?.enemies,           // 목록
    (view, enemy) => view.Show(enemy),               // 그리기(매번)
    GameIds.PlayerData.Run.Id);                      // 보는 키
_enemies.OnRemove = (view, done) => view.PlayDeath(done);
void OnDestroy() => _enemies.Dispose();
```

| 멤버 | 뜻 |
|---|---|
| `ViewList(TView prefab, Transform parent, Func<IEnumerable<TItem>> items, Action<TView, TItem> bind, params string[] keys)` | 만든다(연출을 기다린다). `prefab`이 `null`이면 `ArgumentNullException` |
| `ViewList(…, bool waitForPresentation, params string[] keys)` | 기다릴지 정한다 |
| `Action<TView, TItem> OnCreate` | 새로 만든 직후(처음 `bind` 전). 클릭 연결 등 |
| `Action<TView, Action> OnRemove` | 사라질 때 연출. 연출 대기열에 들어가고 `done` 뒤에 없앤다. 비우면 바로 없앤다 |
| `IReadOnlyDictionary<string, TView> Views` | 지금 오브젝트들(열쇠 → 오브젝트) |
| `TView ViewOf(string id)` · `ViewOf(TItem item)` | 그 항목의 오브젝트(없으면 `null`) |
| `void Dispose()` | 그만 그리고 만든 오브젝트를 모두 없앤다 |

- `TView`는 프리팹의 컴포넌트 형식이다(`where TView : Component`). `TItem`은 `id`가 있는 클래스 또는 `string`(글자 자체가 id). `id`가 없는 클래스면 그릴 때 "…에 string id 필드가 없어 개체로 찾거나 가리킬 수 없습니다." 예외가 콘솔에 찍힌다.

### 4-5. `DataBinding` — 컴포넌트 '데이터 연결'

| 필드 | 뜻 |
|---|---|
| `key` · `path` | 플레이어 데이터 ID · 필드 경로 |
| `format` | 글자 틀(기본 `"{value}"`). 텍스트 키면 그 키의 틀 |
| `waitForPresentation` | 연출을 기다릴지(기본 참) |
| `onText` (`UnityEvent<string>`) · `onNumber` (`UnityEvent<float>`) · `onBool` (`UnityEvent<bool>`) | 2-5 |

코드에서 붙일 일은 드물다. 붙이면 `OnEnable` 전에 칸을 채운다.

### 4-6. `GameScenes` — 씬 흐름

```csharp
GameScenes.Follow(GameIds.PlayerData.Run.Id, "scene", emptyScene: "Title");   // Bootstrap 화면 대신 코드로
GameScenes.Transition = load => Fade(load);                                    // 전환 연출
GameScenes.Loaded += scene => GameAudio.PlayBgm(MusicOf(scene));
```

| 멤버 | 뜻 |
|---|---|
| `void Follow(string key, string path, string emptyScene = null)` | 따라가기 시작(앞의 것은 그만둔다) |
| `void Stop()` | 그만 따라간다 |
| `Func<IEnumerator, IEnumerator> Transition` | 씬 열기(`load`)를 감싸는 코루틴. 안에서 `yield return load`. 비우면 바로 연다 |
| `event Action<string> Loaded` | 씬을 연 뒤(씬 이름) |
| `string Current` | 지금 씬 이름(`SceneManager.GetActiveScene().name`) |

- `Transition`·`Loaded`는 매 플레이 시작 때 비워진다. 플레이마다 다시 넣는다(5-7).
- DB › Bootstrap에 씬 흐름을 정해 두면 런타임이 `Started` 직전에 `Follow`를 부른다.

### 4-7. 월드 상태 — `Game.World` · `SetWorld` · `session.World`

```csharp
Game.SetWorld("Map.chest3", "opened");                   // 판 값
Game.SetWorld("Map.boss", true);                         // "true"
Game.SetWorld("Village.gate", "open", permanent: true);  // 프로필 값(다음 판에도)
bool beaten = Game.WorldFlag("Map.boss");
string lever = Game.World("Cave.lever");                 // 없으면 null
Game.ClearWorld("Map.chest3");                           // 판·프로필 둘 다 지운다
```

| 멤버 | 뜻 |
|---|---|
| `string World(string id)` | 값(판 값 먼저, 없으면 프로필 값, 둘 다 없으면 `null`) |
| `bool WorldFlag(string id)` | 값이 있고 `"false"`·`"0"`이 아니면 참 |
| `void SetWorld(string id, string value, bool permanent = false)` | 값을 넣는다. `value`가 `null`이면 그 구역의 값을 지운다(판에서 끌 때 프로필 값이 있으면 `"false"`를 둔다) |
| `void SetWorld(string id, bool flag, bool permanent = false)` | 참이면 `"true"`, 거짓이면 그 구역의 값을 지운다. 단 판에서 끌 때 프로필 값이 있으면 `"false"`를 둔다(2-7) |
| `void ClearWorld(string id)` | 두 구역 모두 지운다 |

| `session.World` (`WorldStateSystem`) | 뜻 |
|---|---|
| `Get(id)` · `Set(id, value, permanent)` · `Clear(id)` | = `Game.World` · `SetWorld` · `ClearWorld` |
| `static bool IsTrue(string value)` | `WorldFlag`의 판정 |
| `SessionKey` · `MetaKey` | `"$world"` · `"$world.meta"`(다시 그리기의 키) |

`WorldStateTable`(`values`: 이름 → 값)은 저장 껍데기다. 직접 다룰 일은 없다.

### 4-8. `WorldStateBinding` — 컴포넌트 '월드 상태 연결'

| 필드 | 뜻 |
|---|---|
| `id` · `value` | 이름 · 맞을 값(비우면 참이면 맞음) |
| `showWhenMatched` · `hideWhenMatched` (`GameObject[]`) | 맞을 때 켤 것 · 끌 것(자기 자신은 건너뛴다) |
| `onMatched` (`UnityEvent<bool>`) · `onValue` (`UnityEvent<string>`) | 맞음 · 값 |

---

## 5. 상황별 예제

예제는 구현 가이드(AI/BuildGuide)의 카드 던전 형식을 쓴다(`Tools/GuideExample`). 전투 화면 중 ViewList를 쓰는 부분은 UGUI 프리팹(`MonoBehaviour`)으로 그린다.

```csharp
using System;
using System.Collections;
using System.Linq;
using GBible;
using GBible.Ids;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 데이터(Tools/GuideExample/Data/Model.cs): Fighter(id · name · hp · maxHp · block) · Enemy · DeckCard(id · card) · Battle(enemies · hand …)
// 이벤트: DamageDealt(sourceId · targetId · amount) · EnemyKilled(killerId · enemyId · monster) · CardPlayed
// Rules.Damage: 체력을 깎고 피해를 쏜 뒤, 쓰러진 적은 battle.enemies에서 빼고 적 처치를 쏜다.

// 적 하나의 그림(프리팹의 컴포넌트)
public class EnemyView : MonoBehaviour
{
    [SerializeField] TMP_Text nameText, hpText;
    [SerializeField] Animator animator;

    public void Show(Enemy enemy)
    {
        nameText.text = enemy.name;
        hpText.text = $"{enemy.hp} / {enemy.maxHp}";
    }

    public void PlayDeath(Action done) => StartCoroutine(Death(done));

    IEnumerator Death(Action done)
    {
        animator.SetTrigger("die");
        yield return GameView.Wait(0.6f);     // 속도·건너뛰기 반영
        done();                               // 꼭 부른다: 부르지 않으면 대기열이 제한 시간(30초)까지 기다린다
    }
}

// 카드 한 장의 그림
public class CardView : MonoBehaviour
{
    [SerializeField] TMP_Text titleText, costText;

    public void Show(DeckCard card)
    {
        titleText.text = Texts.NameOf(card.card);                                       // 원본 ID의 이름(<ID>.name)
        costText.text = (Rules.CardData(GBibleRuntime.Game, card.card)?.cost ?? 0).ToString();
    }

    public void FlyTo(Vector3 target, Action done) => StartCoroutine(Fly(target, done));

    IEnumerator Fly(Vector3 target, Action done)
    {
        var start = transform.position;
        for (float t = 0; t < 1; t += GameView.DeltaTime * 4) { transform.position = Vector3.Lerp(start, target, t); yield return null; }
        done();
    }
}
```

### 5-1. 피해 숫자 — `SubscribeAfterAction` → `Present`

```csharp
public class DamagePopups : MonoBehaviour
{
    [SerializeField] TMP_Text numberPrefab;      // 떠오르는 숫자
    [SerializeField] AudioClip hitSound;
    IDisposable _onHit;

    // 행동이 성공한 뒤에만, 쏜 순서대로 온다(실패한 행동·미리보기의 피해는 오지 않는다)
    void OnEnable() => _onHit = GBibleRuntime.Session.Events.SubscribeAfterAction<DamageDealt>(e => GameView.Present(Show(e)));
    void OnDisable() => _onHit?.Dispose();

    IEnumerator Show(DamageDealt e)
    {
        var target = GameView.Find(e.targetId);                   // ViewList·Register로 등록된 적·주인공 그림
        if (target == null) yield break;                           // 화면에 없는 대상
        var number = Instantiate(numberPrefab, target.transform);
        number.text = Texts.Get("popup.damage", e);                // "{amount}" — [GameEvent] 객체를 값 묶음으로
        GameAudio.PlaySfx(hitSound, pitch: UnityEngine.Random.Range(0.95f, 1.05f));   // 연출의 무작위는 UnityEngine.Random
        for (float t = 0; t < 0.5f; t += GameView.DeltaTime)
        {
            number.transform.localPosition = Vector3.up * (t * 80f);
            yield return null;
        }
        Destroy(number.gameObject);
    }
}
```

- 마지막 적을 쓰러뜨리는 카드도 피해 숫자가 그 적의 그림 위에 뜬다. 적은 데이터에서 이미 빠졌지만, 적 목록의 다시 그리기가 연출을 기다리므로 그림은 아직 있다(5-2).
- 독 피해처럼 턴 끝에 여러 번 들어오는 피해도 차례로 하나씩 뜬다. 사이를 띄우려면 `GameView.Present(0.1f)`를 함께 넣는다.
- 게임 규칙의 난수(`Game.Random`)를 연출에 쓰지 않는다. 연출의 무작위는 `UnityEngine.Random`(07장).
- 턴제의 모양이다. 실시간 게임이면 `Present` 대신 `Play`로 튼다(5-9). 대기열에 쌓이면 숫자가 줄을 서서 늦게 뜨고 그동안 입력이 막힌다(2-2).

### 5-2. 적 목록 — ViewList + 죽는 연출(`OnRemove`)

```csharp
public class EnemyRow : MonoBehaviour
{
    [SerializeField] EnemyView enemyPrefab;
    [SerializeField] Transform enemyRoot;             // Horizontal Layout Group
    [SerializeField] TargetPicker picker;             // 대상 고르기(프로젝트 코드)
    ViewList<Enemy, EnemyView> _enemies;

    void Start()
    {
        _enemies = new ViewList<Enemy, EnemyView>(enemyPrefab, enemyRoot,
            () => GBibleRuntime.Game.Data().Run.battle?.enemies,     // 전투 밖이면 null → 모두 없앤다
            (view, enemy) => view.Show(enemy),
            GameIds.PlayerData.Run.Id);
        _enemies.OnCreate = (view, enemy) => view.GetComponent<Button>().onClick.AddListener(() => picker.Pick(enemy.id));
        _enemies.OnRemove = (view, done) => view.PlayDeath(done);   // 사라진 적: 죽는 모습 → 없애기
    }

    void OnDestroy() => _enemies?.Dispose();
}
```

한 장으로 고블린을 쓰러뜨렸을 때:

| 순간 | 데이터 | 화면 |
|---|---|---|
| `Game.Do(카드 내기)` 직후 | 고블린 체력 0, `battle.enemies`에서 빠짐 | 그대로(고블린 체력 6) |
| 행동 성공 | — | 피해 숫자가 대기열에(5-1) |
| 피해 숫자 재생 중 | — | 다시 그리기를 미룬다 |
| 대기열이 빔 | — | 적 목록이 다시 그려진다: 고블린이 없다 → `OnRemove` → 죽는 모습이 대기열에 |
| 죽는 모습 끝 | — | 고블린 그림을 없앤다. 남은 적이 자리를 채운다 |

- 클릭 연결은 `OnCreate`에서 한 번 한다(`bind`는 그릴 때마다 불리므로 거기서 `AddListener`하면 겹친다).
- 클릭은 객체가 아니라 id로 넘긴다. 행동이 실행 직전에 지금 데이터의 객체로 다시 찾는다(02장).
- 죽는 모습이 도는 동안 그 적은 이미 등록이 지워져 `GameView.Find(id)`가 `null`이다.

### 5-3. 손패 — ViewList(글자 id 목록)

손패(`battle.hand`)는 덱 카드 **id 글자** 목록이라 글자 자체를 열쇠로 쓴다.

```csharp
public class HandView : MonoBehaviour
{
    [SerializeField] CardView cardPrefab;
    [SerializeField] Transform handRoot;
    [SerializeField] Transform discardPile;          // 버린 더미 그림
    [SerializeField] RunDirector director;
    ViewList<string, CardView> _hand;
    static GameApi Game => GBibleRuntime.Game;

    void Start()
    {
        _hand = new ViewList<string, CardView>(cardPrefab, handRoot,
            () => Game.Data().Run.battle?.hand,
            (view, id) => view.Show(Rules.CardOf(Game, id)),
            GameIds.PlayerData.Run.Id);
        _hand.OnCreate = (view, id) => view.GetComponent<Button>().onClick.AddListener(() => Play(id));
        _hand.OnRemove = (view, done) => view.FlyTo(discardPile.position, done);    // 손에서 빠진 카드는 버린 더미로 날아간다
    }

    void OnDestroy() => _hand?.Dispose();

    void Play(string id)
    {
        var target = Rules.LivingEnemies(Game).FirstOrDefault();
        if (!director.TryPlay(Rules.CardOf(Game, id), target, out var error)) Debug.Log(error);   // 실패 이유(30장 5-8)
    }
}
```

- 새로 뽑은 카드는 만들어지고, 낸 카드·턴 끝에 버린 카드는 날아간 뒤 없어지고, 남은 카드는 자리만 바뀐다. 손패 순서가 바뀌면 그림 순서도 따라간다.
- 만든 카드 그림은 덱 카드 id로 등록된다. "이 카드를 강화" 같은 연출이 `GameView.Find<CardView>(cardId)`로 찾는다.
- 같은 id가 여러 번 들어가는 목록(같은 유물 둘 `run.relics`)은 `"burningBlood"`, `"burningBlood#2"`처럼 따로 만들어진다. 등록(`Find`)은 첫 번째 것만이다.

### 5-4. 에너지 표시 — 데이터 연결(코드 없음)

1. 전투 화면의 에너지 글자(TMP_Text) 오브젝트에 **Add Component → MomoiProject → 데이터 연결**.
2. `key` `run`, `path` `battle.energy`, `format` `hud.energy`(텍스트 표: "에너지 {value}" / "Energy {value}").
3. `onText`의 `+` → 같은 오브젝트의 `TMP_Text` → Dynamic string `text`.
4. 에너지 막대가 있으면 `onNumber` → `Slider` → Dynamic float `value`.
5. `waitForPresentation`을 끈다: 카드를 내면 피해 숫자를 기다리지 않고 에너지가 바로 준다.

- 손패 장수는 `path` `battle.hand`, `format` `{value}장` — 목록이라 `{value}`가 개수다.
- 언어를 바꾸면 `format`의 텍스트 키가 다시 번역된다.

### 5-5. 입력 막기 — `GameView.Busy`

```csharp
// RunDirector(입력 창구)
public bool TryPlay(DeckCard card, Fighter target, out string error)
{
    error = null;
    if (GameView.Busy) { error = Texts.Get("ui.wait"); return false; }    // 연출 중이거나 선택을 기다리는 중
    try { Game.Do(new PlayCard { card = card, target = target }); return true; }
    catch (GameFailure f) { error = f.Message; return false; }
}

public void EndTurn()
{
    if (!GameView.Busy) Game.Do(new EndTurn());
}

// BattleHud: Busy는 플레이어 데이터가 아니라 다시 그리기가 따라오지 않는다 → 매 프레임 본다
void Update() => _endTurnButton.SetEnabled(!GameView.Busy);
```

- 연출 중에 다음 카드를 내도 규칙은 틀리지 않는다(행동은 늘 즉시 끝난다). 막는 이유는 화면이 데이터보다 늦게 따라가 플레이어가 헷갈리기 때문이다.
- Undo 버튼도 연출 중에는 막거나, `GameView.ClearPresentation()` 뒤에 Undo한다(되돌린 데이터와 남은 연출이 섞이지 않게).

### 5-6. 연출 빠르게 — `SkipPresentation` · `PresentationSpeed`

```csharp
// 화면 전체를 덮는 투명 버튼(연출 중에만 켠다)의 클릭
public void OnSkipClicked() => GameView.SkipPresentation();     // 남은 연출 ×10, 비면 원래 속도

void Update() => skipCatcher.SetActive(GameView.IsPresenting);
```

- 설정의 전투 속도로 늘 빠르게 하려면 `GameView.PresentationSpeed`(31장 5-3).
- 속도가 먹지 않는 연출은 `WaitForSeconds`·`Time.deltaTime`·애니메이터 속도로 시간을 쟀다. `GameView.Wait`·`GameView.DeltaTime`으로 바꾸고, 애니메이터는 `animator.speed = GameView.PresentationSpeed`로 맞춘다.

### 5-7. 씬 흐름 — 맵 · 전투 · 타이틀

**준비**

1. Build Settings에 `Title` · `Map` · `Battle` 씬을 넣는다.
2. `RunState`에 `public string scene = "";`을 더한다(가이드 예제에는 없다).
3. DB › Bootstrap › 씬 흐름: 플레이어 데이터 `run`, 필드 `scene`, 빈 값일 때 씬 `Title`.

**규칙** — 행동이 필드만 바꾼다.

```csharp
// StartBattle.Run 끝에
run.scene = "Battle";
// WinBattle.Run 끝에
run.scene = "Map";
// EndRun.Run 끝에
run.scene = "";          // 빈 값 → Title
```

| 일 | `run.scene` | 열리는 씬 |
|---|---|---|
| 게임을 켬(판 없음) | `""` | `Title`(이미 열려 있으면 그대로) |
| 타이틀 '새 게임' → `StartRun`(안에서 `StartBattle`) | `"Battle"` | `Battle`(한 행동 안의 마지막 값) |
| 마지막 적 처치 → `WinBattle` | `"Map"` | 피해 숫자·죽는 모습이 끝난 뒤 `Map` |
| `WinBattle`을 Undo | `"Battle"` | `Battle`(그 판의 전투가 그대로) |
| 판 끝 `EndRun` | `""` | `Title` |
| 타이틀 '이어 하기' → `LoadRun` | 저장된 값 | 그 씬 |

**페이드** — 씬을 넘어 사는 오브젝트에 둔다(타이틀 씬에 하나).

```csharp
public class SceneFader : MonoBehaviour
{
    [SerializeField] CanvasGroup curtain;      // 화면을 덮는 검은 판

    void Awake()
    {
        DontDestroyOnLoad(gameObject);
        GameScenes.Transition = Fade;            // 매 플레이 시작 때 비워지므로 여기서 넣는다
    }

    IEnumerator Fade(IEnumerator load)
    {
        for (float t = 0; t < 1; t += Time.unscaledDeltaTime * 4) { curtain.alpha = t; yield return null; }   // 일시정지와 상관없이
        curtain.alpha = 1;
        yield return load;                       // 씬 열기
        for (float t = 1; t > 0; t -= Time.unscaledDeltaTime * 4) { curtain.alpha = t; yield return null; }
        curtain.alpha = 0;
    }
}
```

- 여는 동안(페이드 포함) `Busy`가 참이라 입력이 막힌다.
- 씬마다 배경음: `GameScenes.Loaded += scene => GameAudio.PlayBgm(…)`.
- 전투가 끝난 뒤 Undo로 전투 씬에 돌아가지 않게 하려면 DB › Bootstrap의 'Undo 끊는 이벤트'를 `전투 승리`로 둔다(20장 3절).

### 5-8. 월드 상태 — 상자 · 보스 · 스위치

맵 씬에 상자·보스 방·동굴 레버가 있다고 본다.

**규칙**

```csharp
[Serializable, BlockInfo("상자 열기")]
public class OpenChest : GameAction
{
    public string chest;                     // 월드 상태 이름("Map.chest3")

    protected override void Run()
    {
        if (Game.World(chest) == "opened") throw Game.Fail("fail.chestOpened", "이미 연 상자입니다.");
        Game.SetWorld(chest, "opened");
        var drop = Game.Roll(GameIds.WeightTable.ChestDrop);          // 30장
        if (drop != null) Game.Data().Run.relics.Add(drop);
    }
}

[Serializable, BlockInfo("레버 당기기")]
public class PullLever : GameAction
{
    public string lever;
    protected override void Run() => Game.SetWorld(lever, !Game.WorldFlag(lever));      // 켜져 있으면 끄고, 꺼져 있으면 켠다
}

// 보스 전투의 WinBattle 안: 이번 판에는 다시 나오지 않는다
Game.SetWorld("Map.boss", true);
// 마을 문: 한 번 열면 다음 판에도 열려 있다(프로필)
Game.SetWorld("Village.gate", "open", permanent: true);
```

**씬** — 컴포넌트 '월드 상태 연결'(3-3).

| 오브젝트 | `id` | `value` | `showWhenMatched` | `hideWhenMatched` |
|---|---|---|---|---|
| 상자(부모) | `Map.chest3` | `opened` | 열린 뚜껑 | 닫힌 뚜껑 |
| 보스 자리(부모) | `Map.boss` | (비움) | 쓰러진 흔적 | 보스 |
| 레버(부모) | `Cave.lever` | (비움) | 레버 내림 · 열린 문 | 레버 올림 · 닫힌 문 |
| 마을 문(부모) | `Village.gate` | `open` | 열린 문 | 닫힌 문 |

**클릭** — 상자(부모, 콜라이더가 있는 오브젝트)의 스크립트는 이름만 들고 행동을 보낸다.

```csharp
public class ChestClick : MonoBehaviour
{
    [SerializeField] TMP_Text toast;                                   // 알림 글자
    string Id => $"{gameObject.scene.name}.{gameObject.name}";         // "Map.chest3"

    void OnMouseDown()
    {
        if (GameView.Busy) return;
        try { GBibleRuntime.Game.Do(new OpenChest { chest = Id }); }
        catch (GameFailure f) { toast.text = f.Message; }              // "이미 연 상자입니다."(지금 언어)
    }
}
```

- 상자를 열면 뚜껑은 드롭 연출이 끝난 뒤 바뀐다(연결도 연출을 기다린다). 맵 씬을 떠났다 돌아와도, 판을 저장하고 이어 해도 열린 채다. Undo하면 닫힌다.
- 레버를 끄면(`SetWorld(이름, false)`) 판 값이 지워진다. 같은 이름에 프로필 값(`permanent`)이 있으면 대신 `"false"`가 들어가 이번 판에서는 꺼진 채다(2-7).
- 새 판이면 상자·보스·레버는 처음 모습으로 돌아오고, 마을 문은 열린 채다.

### 5-9. 실시간 — 대기열 밖 연출(`GameView.Play`)

실시간 아레나(33장 5-1·5-7)에서 적이 맞을 때마다 숫자가 뜨고, 쓰러진 적의 자리에 연기가 남는다. 입력은 한 번도 막히지 않는다. 이벤트 `FoeHit { foeId, amount }`·`FoeKilled { foeId }`는 33장 5-7의 것이다.

```csharp
// 적 목록: 기다리지 않는 다시 그리기 + OnRemove 없음 → 쓰러진 적은 그 프레임 끝에 바로 없어진다(대기열·Busy 없음)
_foes = new ViewList<ArenaFoe, FoeView>(foePrefab, foeRoot,
    () => GBibleRuntime.Session.Data.Get<ArenaState>("arena").foes,
    (view, foe) => view.Show(foe),
    waitForPresentation: false, "arena");
_foes.OnCreate = (view, foe) => view.Id = foe.id;                  // 히트박스가 판정 보고에 쓴다(33장 5-7)

public class RealtimeHits : MonoBehaviour
{
    [SerializeField] TMP_Text numberPrefab;      // 떠오르는 숫자(월드 공간 TMP)
    [SerializeField] GameObject smokePrefab;     // 쓰러진 자리에 남는 연기
    IDisposable _onHit, _onKill;

    void OnEnable()
    {
        var events = GBibleRuntime.Session.Events;
        _onHit = events.SubscribeAfterAction<FoeHit>(e => GameView.Play(ShowNumber(e)));
        _onKill = events.SubscribeAfterAction<FoeKilled>(e =>
        {
            var view = GameView.Find(e.foeId);                              // 행동 직후라 그림은 아직 있다(없애는 것은 프레임 끝)
            if (view != null) GameView.Play(Smoke(view.transform.position));  // 자리를 먼저 읽어 둔다
        });
    }

    void OnDisable()
    {
        _onHit?.Dispose();
        _onKill?.Dispose();
    }

    IEnumerator ShowNumber(FoeHit e)
    {
        var target = GameView.Find(e.foeId);                  // Play는 부른 즉시 첫 yield까지 돈다 → 지금 자리
        if (target == null) yield break;
        var number = Instantiate(numberPrefab, target.transform.position, Quaternion.identity);   // 적의 자식이 아니다(적이 먼저 없어져도 남는다)
        number.text = e.amount.ToString();
        for (float t = 0; t < 0.4f; t += GameView.DeltaTime)
        {
            if (number == null) yield break;                  // 씬이 바뀌어 없어졌다
            number.transform.position += Vector3.up * (GameView.DeltaTime * 2f);
            yield return null;
        }
        if (number != null) Destroy(number.gameObject);
    }

    IEnumerator Smoke(Vector3 at)
    {
        var smoke = Instantiate(smokePrefab, at, Quaternion.identity);
        yield return GameView.Wait(0.8f);
        if (smoke != null) Destroy(smoke);
    }
}
```

- 한 틱에 적 열 마리를 맞혀도 숫자 열 개가 한꺼번에 뜬다(`Strike { hits }` 하나가 `FoeHit`을 열 번 쏜다, 33장 5-7).
- 숫자를 적의 자식으로 만들면 적이 쓰러진 프레임 끝에 함께 없어진다. 따로 만들고 자리만 읽는다.
- 레벨업 고르기 창처럼 입력을 정말 막아야 하는 것은 선택 대기다(`Game.IsWaiting` → `Busy`, 29장·35장).

---

## 6. 주의할 점 · 자주 하는 실수

| 증상 | 원인과 해결 |
|---|---|
| 적이 죽는 연출 전에 화면에서 사라짐 | 적 목록을 기다리지 않는 다시 그리기(`waitForPresentation: false`)로 그렸거나, 연출이 대기열 밖에서 돈다(직접 `StartCoroutine`·`GameView.Play`). 턴제의 연출은 `GameView.Present`로 넣는다. 실시간이면 사라지는 것이 맞고, 죽는 모습은 자리를 읽어 두고 따로 튼다(5-9). |
| 실시간 게임에서 버튼·스킬 입력이 자꾸 안 먹음, 피해 숫자가 줄을 서서 늦게 뜸 | 맞을 때마다 `Present`로 넣었다. 대기열은 차례로 하나씩 돌고, 그동안 `IsPresenting`·`Busy`가 참이라 입력 창구가 입력을 막는다. 실시간 연출은 `GameView.Play`(2-2, 5-9). ViewList의 `OnRemove`도 대기열이다 → 실시간 적 목록은 비운다. |
| `Play`로 튼 숫자가 씬을 바꾼 뒤 `MissingReferenceException` | 대기열 밖 연출도 `[MomoiProject]` 오브젝트가 돌려 씬이 바뀌어도 계속 돈다. 돌면서 오브젝트가 `null`인지 본다(5-9). |
| "GameView.Play: 게임이 시작되지 않아 연출을 틀 수 없습니다" | 게임(호스트)이 시작되기 전에 불렀다(씬의 `Awake` 등). `GBibleRuntime.Started` 뒤에 부른다. |
| 골드·에너지가 연출이 끝나야 바뀜 | 기본은 연출을 기다린다. 늘 지금 값이어야 하는 표시는 `waitForPresentation: false`(데이터 연결은 칸을 끈다). |
| 실패한 카드의 피해 숫자가 뜸 | 연출을 `Events.Subscribe`(바로 받는 쪽)로 받았다. `SubscribeAfterAction`으로 받는다. |
| 마우스를 올려 예상 피해를 볼 때 피해 숫자가 뜸 | 같은 원인. 미리보기 실행(33장)에서도 바로 받는 쪽은 불린다. |
| 연출이 한참 멈췄다가 "연출이 30초 안에 끝나지 않아 넘어갑니다" 경고 | 완료 콜백 연출이 `done()`을 부르지 않았다(오브젝트가 먼저 없어졌거나 예외). 끝나는 길마다 `done()`을 부르거나 코루틴으로 넣는다. 원래 긴 연출이면 `Present(step, timeout: 60f)`처럼 늘린다(0 = 끝없이 기다림). 씬을 직접 떠날 때는 `ClearPresentation()`. |
| 속도·건너뛰기가 먹지 않음 | 시간을 `WaitForSeconds`·`Time.deltaTime`으로 쟀다. `GameView.Wait`·`GameView.DeltaTime`(5-6). |
| 일시정지하니 연출이 멈춤 | `DeltaTime`은 게임 시간을 따른다. 일시정지와 상관없어야 하는 것(메뉴 페이드)은 `Time.unscaledDeltaTime`. |
| 버튼을 누를 때마다 클릭이 여러 번 | `bind`에서 `AddListener`했다(그릴 때마다 불린다). `OnCreate`에서 한다(5-2). |
| ViewList가 "…에 string id 필드가 없어 …" | 항목 클래스에 `public string id;`가 없다. 글자 목록이면 `ViewList<string, …>`. |
| 같은 항목 두 개 중 하나만 `GameView.Find`로 찾힘 | 같은 id의 두 번째부터는 `"id#2"`로 만들어지고 등록은 첫 번째만이다. `ViewList.Views["id#2"]`로 찾는다. |
| 씬을 나갔다 왔더니 ViewList 오브젝트가 남음·사라지지 않음 | `Dispose`를 부르지 않았다. `OnDestroy`에서 `Dispose`. |
| 데이터 연결이 아무것도 안 보임 | `key`가 없는 플레이어 데이터이거나 `path`의 중간이 비었다(전투 밖의 `battle.energy`). 이벤트의 Dynamic 속성을 골랐는지도 본다(Static을 고르면 인스펙터에 적은 값이 들어간다). |
| 데이터 연결의 글자가 `{value}` 그대로 | `format`에 `{value}`가 아닌 다른 이름을 적었다. 값 이름은 `value` 하나다. |
| `Busy`로 막았는데 버튼이 흐려지지 않음 | `Busy`는 플레이어 데이터가 아니라 다시 그리기가 오지 않는다. `Update`에서 보거나 `Presented`를 받는다(5-5). |
| 씬이 열리지 않고 타임라인에 "씬 '…'을(를) 열 수 없습니다" | Build Settings의 씬 목록에 없거나 이름이 다르다. |
| 씬이 바로 바뀌지 않고 연출 뒤에 바뀜 | 정상이다. 씬 흐름은 연출이 끝난 뒤에 연다(2-6). |
| 씬을 직접 열었더니 씬 흐름이 되돌려 놓음 | 씬 흐름은 데이터의 씬을 따라간다. `SceneManager.LoadScene` 대신 행동에서 필드를 바꾼다. |
| Undo했더니 씬이 바뀜 | 정상이다. 값이 돌아가면 씬도 돌아간다. 막으려면 'Undo 끊는 이벤트'(5-7). |
| `Transition`·`Loaded`가 두 번째 플레이부터 안 돎 | 매 플레이 시작 때 비워진다. 플레이마다 넣는다(5-7의 `Awake`). |
| 다음 판에 상자가 다시 닫힘 | 판 값이다. 판을 넘어 남기려면 `permanent: true`. |
| 월드 상태 연결이 자기 자신을 끄지 않음 | 자기 자신은 켜고 끄지 않는다. 부모에 붙이고 자식을 대상으로 둔다. |
| 다른 씬의 같은 이름 오브젝트가 함께 바뀜 | 이름이 겹친다. "씬 이름.오브젝트 이름"을 쓴다. |
| 화면이 데이터를 고쳤더니 Undo·저장에서 빠짐 | 화면 코드(행동 밖)에서 고쳤다. 고치는 일은 행동으로(`Game.Do`). |
