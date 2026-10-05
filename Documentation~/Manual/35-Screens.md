# 35. 화면(UI Toolkit) — DB › 화면 · 화면 코드 · 기본 화면

**한 줄 요약**: 화면 하나 = **UXML 파일**(모양) + **DB › 화면** 항목(뜨는 층·뒤로 닫기·열 씬) + 필요하면 **화면 코드**(`[BindTo]`로 이은 `GameScreen`). 값을 보이기만 하는 칸은 UXML에 `binding-path="run:gold"`만 적으면 코드 없이 바뀌고, 버튼은 `OnClick("end-turn", () => new EndTurn())`로 행동을 보낸다(연출·선택 대기 중이면 무시, 플레이어용 실패는 알림). 고르기 창·확인·설정·슬롯·일시정지·알림은 **기본 화면**으로 들어 있어 만들 필요가 없고, 모양은 **테마 USS**의 변수로 바꾼다. 모든 화면은 게임이 켜질 때 만들어지는 UIDocument 하나 안의 **층**(바탕 → 창 → 팝업 → 알림)에 뜬다. 화면은 플레이어 데이터를 읽기만 하고, 값을 바꾸는 입력은 모두 행동이다.

**언제 쓰는가**

- HUD·상점·인벤토리·지도·타이틀처럼 게임마다 다른 화면(DB › 화면 + UXML).
- 체력·골드·턴처럼 플레이어 데이터를 그대로 보이는 칸(binding-path, 코드 없음).
- 손패·적·상점 물건처럼 데이터 목록을 칸 목록으로(`ElementList`).
- 선택 대기(카드 1장 버리기·보상 셋 중 하나)의 고르기 창, "정말 지울까요?", 설정 화면(DB › 설정), 세이브 슬롯, 일시정지 메뉴, "에너지가 모자랍니다" 알림(기본 화면).

> **왜 UI Toolkit인가** — UXML·USS는 글 파일이라 AI가 직접 쓰고 고칠 수 있다(프리팹·씬은 사람이 Unity에서 만들어야 한다). 이 장의 도구로 화면은 "UXML·USS 파일 + DB 항목 + 화면 코드"만으로 끝나고, 인스펙터에서 연결할 것이 없다. 그림(스프라이트)·글꼴·소리 같은 에셋만 사람이 넣는다. 씬에 놓이는 것(캐릭터·카드 오브젝트)은 32장의 `ViewList`·'데이터 연결'을 쓴다.

이 문서에서 쓰는 용어:

| 용어 | 뜻 |
|---|---|
| 화면 | DB › 화면의 항목 하나(`ScreenData`): UXML · 더할 USS · 목록 칸 UXML · 층 · 아래 입력 막기 · 뒤로로 닫기 · 시간 멈춤 · 열 씬 · 게임이 켜지면 열기. ID 상수는 `GameIds.Screen.X`. |
| 화면 코드 (`GameScreen`) | 화면 하나의 C# 코드. `[BindTo(GameIds.Screen.X)]`로 잇는다. 열 때 `OnOpen`, 데이터·언어가 바뀌면 `Draw`, 열려 있는 동안 매 프레임 `Tick`, 닫을 때 `OnClose`. 없어도 된다(`PlainScreen`). |
| 층 (`ScreenLayer`) | 화면이 뜨는 자리: 바탕(HUD) · 창(메뉴) · 팝업(위에 뜨는 창) · 알림. 위의 층이 아래를 덮는다. 같은 층에서는 나중에 연 것이 위. |
| 연결 글 (binding-path) | UXML 칸의 `binding-path` 속성에 적는 글. `키:경로`(플레이어 데이터의 값) · `@텍스트키`(번역된 글) · 둘 다(`키:경로@텍스트키`, 값을 틀에 넣어서). `ScreenBinding`이 읽는다. |
| 목록 칸 | 데이터 목록의 항목마다 하나씩 만드는 칸(목록 칸 UXML 하나를 복제). `ElementList`가 만들고 없애고 순서를 맞춘다. |
| 기본 화면 | 라이브러리에 들어 있는 범용 화면: 고르기 창 · 확인 · 설정 · 슬롯 · 일시정지(+ 알림). 코드로 만들고 테마 USS로 모양을 바꾼다. |
| 테마 USS | 모든 화면에 붙는 USS(DB › 화면 설정 › 테마 USS, 기본 `Assets/UI/Theme.uss`). 색·글자 크기 변수(`--momoi-…`)와 클래스(`.momoi-button` …). |
| Panel Settings | UI Toolkit의 패널 설정(화면 크기 맞춤·기본 테마). `Assets/UI/PanelSettings.asset`. |
| 알림 (Toast) | 화면 아래쪽에 잠깐 뜨는 한 줄. 입력을 막지 않는다. |

---

## 1. 흐름 한눈에

```
게임이 켜짐(GBibleRuntime.Start)
  └ GameScreens.Initialize: DB › 화면·화면 설정을 읽고, 화면 코드를 한 번 만든다([BindTo] 클래스, 리플렉션은 여기서만)
       '게임이 켜지면 열기' 화면 · 지금 씬의 '열 씬' 화면을 연다

GameScreens.Open(GameIds.Screen.Shop)
  └ UXML을 만들어 그 층에 붙인다(팝업·막는 창은 뒤에 덮개) → binding-path 칸을 모은다
  └ 화면 코드 OnOpen(버튼 잇기·목록 만들기)
  └ 프레임 끝: binding-path 칸에 값 → 화면 코드 Draw          ← 그 키의 데이터·언어가 바뀔 때마다 다시(연출이 끝난 뒤)

버튼 → OnClick(…, () => new BuyCard { … }) → GameScreens.Do
  └ 연출·선택 대기 중이면 무시 → Game.Do(행동) → 실패(GameFailure)면 알림 "골드가 모자랍니다(30 / 50)."

Esc(뒤로) → GameScreens.Back: 맨 위 창·팝업을 닫는다(뒤로로 닫기가 꺼진 화면이면 아무것도 안 함)
선택 대기가 생김(Game.Ask·선택 요구·가중치 표에서 고르기) → 기본 고르기 창이 저절로 → 답하면 닫힘
```

> **규칙: 화면은 읽기만, 바꾸기는 행동.** 버튼이 플레이어 데이터를 직접 고치지 않는다(Undo·저장·멀티가 깨진다). 토글·슬라이더에 binding-path를 붙이면 값을 **보이기만** 하고, 플레이어가 바꾼 값은 `RegisterValueChangedCallback`에서 행동으로 보낸다. 설정(기기마다의 값)은 플레이어 데이터가 아니라 `GameSettings.Set`이다(기본 설정 화면이 한다).

> **규칙: 값 칸은 binding-path, 코드는 그다음.** 체력·골드처럼 값 하나를 글로 보이는 칸은 화면 코드에 쓰지 않는다. 화면 코드는 버튼·목록·계산이 필요한 칸만 맡는다.

---

## 2. 동작 규칙

### 2-1. 열기 · 닫기 · 층

- `GameScreens.Open(id)`는 그 화면을 열고 화면 코드를 돌려준다. **이미 열려 있으면 그대로** 돌려준다(같은 화면이 둘 열리지 않는다). 다시 열려면 닫고 연다.
- DB에 없는 ID면 `InvalidOperationException`("[MomoiProject] 없는 화면 'shp'입니다(DB › 화면).").
- **층**: 바탕(HUD) · 창(메뉴) · 팝업 · 알림 순으로 위다. 같은 층에서는 나중에 연 것이 위다.
- **아래 입력 막기**: 팝업은 늘 어두운 덮개로 아래를 막는다. 창은 '아래 입력 막기'가 켜져 있으면 투명한 덮개로 막는다. 바탕·알림은 막지 않는다. 화면의 빈 곳(배경이 없는 곳)은 클릭이 아래로 지나간다.
- **뒤로(Esc)**: `GameScreens.Back()`이 맨 위의 창·팝업 하나를 닫는다. 그 화면의 '뒤로로 닫기'가 꺼져 있으면 아무것도 하지 않는다(고르기 창). 닫을 창·팝업이 없고 DB › 화면 설정 'Esc로 일시정지 메뉴'가 켜져 있으면 일시정지 메뉴를 연다. Esc 키(이전 입력 관리자를 쓸 때)와 UI의 취소 이벤트가 `Back`을 부른다. 입력(키 바꾸기) 기능의 "뒤로" 동작에도 `GameScreens.Back()`을 잇는다.
- **시간 멈춤**: '열린 동안 게임 시간 멈춤'이면 열 때 `GameTime.Pause("화면 ID")`, 닫을 때 `Resume`. 일시정지 메뉴가 이것이다.
- **열 씬**: 씬이 바뀌면(Single) 그 씬이 목록에 있는 화면을 열고, 없는 화면을 닫는다. 목록이 빈 화면은 씬과 상관없다.
- **게임이 켜지면 열기**: 처음 한 번 연다(닫으면 다시 열지 않는다).
- 닫으면 화면 코드의 `OnClose` → `Own`으로 맡긴 것 정리(목록 등) → 버튼 연결 끊기 → 트리 제거 순이다. 화면 코드 인스턴스는 **게임이 켜질 때 한 번 만들어** 두고 열 때마다 다시 쓴다(필드는 `OnOpen`에서 새로 채운다).

### 2-2. binding-path — 코드 없이 값 보이기

UXML 칸(Label·Button·ProgressBar·Toggle·Slider)의 `binding-path` 속성에 적는다.

| 연결 글 | 보이는 것 | 예 |
|---|---|---|
| `키:경로` | 플레이어 데이터 `키`의 `경로` 값. 목록이면 개수 | `<ui:Label binding-path="run:gold" />` → `99` |
| `키` | 값 전체 | `binding-path="turn"` |
| `키:경로@텍스트키` | 값을 텍스트 키의 틀(`{value}`)에 넣은 글 | `binding-path="run:gold@hud.gold"`, 텍스트 `hud.gold` = "골드 {value}" → `골드 99` |
| `@텍스트키` | 번역된 글(언어가 바뀌면 다시) | `<ui:Button binding-path="@hud.endTurn" />` → `턴 끝` |

- 틀이 없고 그 칸의 원래 글자(`text`)에 `{value}`가 있으면 원래 글자를 틀로 쓴다: `<ui:Label binding-path="run:battle.energy" text="에너지 {value} / 3" />`.
- 칸마다: 글자 칸(Label·Button)은 글, **ProgressBar**는 값(`value`, 최댓값 `high-value`는 UXML이나 코드에서) + 틀이 있으면 `title`, **Toggle**은 참거짓, **Slider·SliderInt**는 숫자. 토글·슬라이더는 보이기만 한다(바꾸면 알림을 보내지 않는다).
- 경로는 필드 경로 문법(11장)이다: `hero.hp`, `battle.energy`, 목록 펼치기 `deck[].upgraded`(첫 값). 없는 키·경로면 빈 글(0)이다.
- 숫자는 소수 둘째 자리까지(`0.##`), 목록은 개수, 참거짓은 `True`/`False`(글로 보일 때는 텍스트 키 틀을 쓴다).
- **다시 그리는 때**: 그 화면의 binding-path에 쓴 키가 바뀔 때(화면 코드의 `Watch`가 있으면 그 키), 그리고 언어가 바뀔 때. 연출 대기열이 재생 중이면 끝난 뒤(화면 코드의 `WaitForPresentation`이 거짓이면 바로).
- binding-path는 **화면을 열 때 UXML에 있던 칸만** 읽는다. 목록 칸(ElementList가 나중에 만드는 칸) 안의 binding-path는 쓰지 않는다 — 항목마다 값이 다르므로 목록의 bind에서 채운다.
- 틀린 글(`:gold`, `run@`)은 없는 것으로 친다. 값을 넣다 예외가 나면 경고 "[MomoiProject] 화면 'hud'의 binding-path 'run:hero.hp': …"를 남기고 다음 칸으로 간다.

### 2-3. 화면 코드

```csharp
[BindTo(GameIds.Screen.Shop)]
public class ShopScreen : GameScreen
{
    protected override void OnOpen() { … }   // 버튼·목록 잇기(열 때마다)
    protected override void Draw() { … }     // binding-path로 안 되는 칸(데이터·언어가 바뀔 때)
    protected override void Tick(float deltaTime) { … }   // 시간에 따라 바뀌는 칸(열려 있는 동안 매 프레임)
    protected override void OnClose() { … }  // 정리할 것이 있으면
}
```

- `Root`(이 화면의 트리), `Id`, `Argument`(`Open(id, 값)`으로 넘긴 것), `Data`(DB 항목), `Game`.
- **`Draw`와 `Tick`**: `Draw`는 값이 바뀔 때(프레임 끝에 한 번), `Tick`은 시간이 흐를 때(열려 있는 동안 매 프레임 끝)다. 쿨다운 원·남은 시간 타이머·버프 남은 초처럼 데이터는 그대로인데 시간에 따라 바뀌는 칸은 `Tick`에서 그린다. `deltaTime`은 실제 시간(초, `Time.unscaledDeltaTime`)이라 게임 일시정지·배속과 상관없다. 게임 시간으로 남은 시간을 보이려면 `deltaTime`을 쌓지 말고 그때마다 게임 시계(`Game.Session.Clock.Time`)를 읽는다. **`Tick`에서 데이터를 바꾸지 않는다**(바꾸려면 행동). 예외가 나면 콘솔에 찍히고 다른 화면은 계속 돈다.

```csharp
Label _dash;
protected override void OnOpen() => _dash = Q<Label>("dash-cooldown");      // 매 프레임 찾지 않게 열 때 한 번

protected override void Tick(float deltaTime)
{
    var hero = Game.Data().Run.hero;
    var left = hero.dashReadyAt - Game.Session.Clock.Time;                 // 게임 시간으로 남은 초(일시정지 중이면 멈춘다)
    _dash.text = left > 0 ? left.ToString("0.0") : "";
}
```
- `Q<Label>("name")`: 이름으로 찾는다. 없으면 예외 "[MomoiProject] 화면 'shop'의 UXML에 이름(name)이 'price'인 Label이(가) 없습니다." — UXML과 코드의 이름이 어긋난 것을 바로 알린다.
- `OnClick("buy", () => new BuyCard { … })`: 버튼 → 행동(2-4). `OnClick("close", Close)`·`OnClick("settings", () => GameScreens.Open(GameScreens.Settings))`: 버튼 → 화면 일(Busy여도 돈다).
- `List<T>("container", Item("CardItem"), () => 목록, (칸, 항목) => …, 키…)`: 목록 칸(2-6). 닫으면 저절로 정리된다.
- `Item("이름")`: 목록 칸 UXML을 찾는다 — DB › 화면의 '목록 칸 UXML', 또는 화면 UXML에 `<ui:Template name="CardItem" src="../Items/CardItem.uxml" />`로 적은 것.
- `Own(구독)`: 닫을 때 `Dispose`할 것(이벤트 구독·`GameView.Redraw` …).
- `Watch`: 다시 그릴 키. 기본(null)은 binding-path에 쓴 키들(그것도 없으면 어느 값이 바뀌어도). `new[] { GameIds.PlayerData.Run.Id }`처럼 정한다.
- 다른 DB 항목(능력·결과)에 붙는 `[BindTo]`와 같은 어트리뷰트다. 화면에 잇는 클래스는 `GameScreen`을 상속하고 매개변수 없는 생성자가 있어야 한다(아니면 게임이 켜질 때 경고하고 코드 없는 화면으로 연다).

### 2-4. 입력 → 행동

`OnClick(버튼, () => 행동)`과 `GameScreens.Do(() => 행동)`는 같다:

1. 게임이 없거나 `GameView.Busy`(연출 중·선택 대기 중)면 아무것도 하지 않는다.
2. `Game.Do(행동)`.
3. 플레이어용 실패(`GameFailure` — `Game.Fail`·`Game.Pay`가 모자람·`Game.Use` 조건, 30장 2-4)면 그 글을 **알림**으로 보인다(DB › 화면 설정 '실패를 알림으로', 끄면 경고 로그).
4. 프로그래머 실수(`InvalidOperationException`)는 그대로 던진다(콘솔에 보인다).

행동을 만드는 함수는 누를 때마다 돈다(그 순간의 데이터로 대상을 찾는다). 선택을 기다리는 동안에도 보내야 하는 버튼(고르기 창 안의 다시 뽑기)은 `GameScreens.Do(() => 행동, whileChoosing: true)` — 연출 중에는 그래도 막는다. 입력이 버튼이 아니면(드래그해서 놓기·키) 그 자리에서 `GameScreens.Do(() => …)`를 부른다.

### 2-5. 기본 화면

| ID (`GameScreens.…`) | 층 · 뒤로 | 하는 일 | 여는 법 |
|---|---|---|---|
| `Choice` (`momoi.choice`) | 팝업 · 닫히지 않음 · 시간 멈춤('고르는 동안 게임 시간 멈춤'을 켰을 때) | 지금 기다리는 선택(`Game.Choice`)의 안내 글·후보 버튼. 후보 버튼 안에 텍스트 `<후보 id>.desc`가 있으면 설명 글(`.momoi-option__desc`). 하나만 고르면 누르는 즉시 답하고, 여럿이면 고른 뒤 확인. 최소 0이면 건너뛰기. `GameScreens.Reroll`을 정하면 표로 만든 선택에 '다시 뽑기'(29장 4-10b). 틀린 답은 알림 | **저절로**: 선택 대기가 생기면 열리고 없어지면 닫힌다(DB › 화면 설정 '고르기 창 저절로') |
| `Confirmation` (`momoi.confirm`) | 팝업 · 아니오 | 글 + 예/아니오 | `GameScreens.Confirm(글, 예, 아니오)` |
| `Settings` (`momoi.settings`) | 창 · 닫기 | DB › 설정 항목을 분류마다: 켜기/끄기 → 토글, 숫자 → 슬라이더(0~1이면 %), 선택지 → 드롭다운(언어·해상도는 자동 목록), 글 → 입력칸. 바꾸면 바로 `GameSettings.Set`(저장·적용). '기본값으로'(확인 후 `ResetAll`) | `GameScreens.Open(GameScreens.Settings)` |
| `Slots` (`momoi.slots`) | 창 · 닫기 | 슬롯마다 요약(`GBibleRuntime.SlotSummary`)·플레이 시간. 고르면 `UseSlot` 후 `picked`. 지우기(확인 후, 지금 슬롯은 못 지움) | `GameScreens.Open(GameScreens.Slots, new SlotsRequest { count = 3, picked = … })` |
| `Pause` (`momoi.pause`) | 창 · 닫기 · 시간 멈춤 | 계속하기 · 설정 · 타이틀로(`GameScreens.QuitToTitle`이 있을 때) · 게임 종료 | `Open(GameScreens.Pause)` 또는 Esc('Esc로 일시정지 메뉴') |
| (알림) | 알림 | 아래쪽 한 줄, 2.5초. 최대 4줄 | `GameScreens.Toast(글)` |

- 모양은 테마 USS의 변수(2-7)와 클래스로 바꾼다: `.momoi-panel`(가운데 판) · `.momoi-title` · `.momoi-hint` · `.momoi-section` · `.momoi-message` · `.momoi-row` · `.momoi-button` · `.momoi-button--primary` · `.momoi-options` · `.momoi-option` · `.momoi-option--selected` · `.momoi-option__desc` · `.momoi-setting` · `.momoi-value` · `.momoi-slot` · `.momoi-toast` · `.momoi-backdrop`. 화면마다 판에 `.momoi-choice` · `.momoi-confirm` · `.momoi-settings` · `.momoi-slots` · `.momoi-pause`가 붙는다.
- **고르기 창의 설명 글**: 후보마다 텍스트 표에 `<후보 id>.desc`를 넣으면 버튼 안 이름 아래에 보인다(레벨업 특성 "불꽃 칼날" + "공격에 화상 1"). 키가 없으면 이름만 보인다. 이름은 선택 대기의 보일 글이다(비우면 `<id>.name`, 29장).
- **고르는 동안 게임 시간 멈춤**(DB › 화면 설정, 기본 끔): 켜면 기본 고르기 창이 떠 있는 동안 `GameTime.Pause`로 게임 시간을 멈춘다. 실시간 게임의 레벨업 선택에서 켠다(고르는 동안 적이 움직이지 않는다). `Time.timeScale`이라 시간 예약·초 지속·고정 틱과 함께 **연출도 멈춘다** → 턴제는 끈다. 화면 코드의 `Tick`(실제 시간)은 계속 돈다.
- **바꾸기**: 같은 ID로 DB › 화면 항목을 만들면 그 항목이 쓰인다. `[BindTo("momoi.settings")]` 클래스가 없으면 기본 코드가 그 UXML 위에 그린다(USS만 바꾸고 싶을 때). 통째로 바꾸려면 `[BindTo]`로 코드도 잇는다.
- **고르기 창을 바꿨을 때 '고르기 창 저절로'**: 같은 ID(`momoi.choice`)로 바꿨으면 **켜 둔다** — 저절로 여는 것이 이 ID라, 끄면 열리지 않는다. 다른 ID로 따로 만들어 직접 열 때만 끈다(켜 두면 기본 창과 둘 다 뜬다). 같은 ID의 DB 항목에서는 그 항목의 칸이 쓰인다: 층은 `팝업`, '뒤로(Esc)로 닫기'는 끈다(켜 두면 Esc로 닫혀도 선택 대기가 남아 다음 프레임에 다시 열린다), 시간 멈춤은 '고르는 동안 게임 시간 멈춤' 대신 그 항목의 '열린 동안 게임 시간 멈춤'이다.
- **글**: 텍스트 키가 있으면 그 번역, 없으면 한국어 기본 글. 출시 언어마다 텍스트 표에 넣는다.

| 텍스트 키 | 기본 글 |
|---|---|
| `momoi.ui.ok` · `momoi.ui.yes` · `momoi.ui.no` · `momoi.ui.close` · `momoi.ui.skip` | 확인 · 예 · 아니오 · 닫기 · 건너뛰기 |
| `momoi.ui.choose` · `momoi.ui.chooseCount` · `momoi.ui.chooseRange` | 고르기 · {count}개를 고르세요 · {min}~{max}개를 고르세요 |
| `momoi.ui.settings` · `momoi.ui.resetAll` · `momoi.ui.resetAllAsk` · `momoi.ui.noSettings` | 설정 · 기본값으로 · 모든 설정을 기본값으로 되돌릴까요? · 설정 항목이 없습니다(DB › 설정). |
| `momoi.ui.slots` · `momoi.ui.emptySlot` · `momoi.ui.slotLine` · `momoi.ui.delete` · `momoi.ui.deleteSlotAsk` | 슬롯 · {slot} — 빈 슬롯 · {slot} — {summary} ({time}) · 지우기 · 슬롯 {slot}을(를) 지울까요? |
| `momoi.ui.pause` · `momoi.ui.resume` · `momoi.ui.toTitle` · `momoi.ui.quit` | 일시정지 · 계속하기 · 타이틀로 · 게임 종료 |

- 설정 항목의 이름은 텍스트 `<설정 ID>.name`, 분류 이름은 분류 글자(텍스트 키면 번역), 선택지의 '보일 글'은 텍스트 키 또는 글(31장).

### 2-6. 목록 칸 — `ElementList`

- 항목 id(글자면 그대로, 객체면 `id`)마다 목록 칸 UXML을 하나 만든다. 같은 id가 여럿이면 `id#2`처럼 따로. 항목이 사라지면 칸을 없애고(`OnRemove`가 있으면 연출 대기열에서 그 연출 뒤에), 데이터 순서대로 놓는다.
- 값이 바뀌면 bind를 다시 돈다(키를 주면 그 키가 바뀔 때만). 칸에는 `.momoi-item` 클래스가 붙는다.
- 만든 칸은 `GameView.RegisterElement(id)`로 등록된다 → 연출이 `GameView.FindElement(id)`로 찾는다(5-4).
- `OnCreate`(처음 bind 전): 칸 안의 버튼 잇기. 칸의 버튼에서 행동을 보낼 때는 `GameScreens.Do(() => …)`(Busy·실패 알림이 같다).

### 2-7. 루트 · Panel Settings · 테마 · 글꼴

- 첫 화면을 열 때 `[MomoiProject] 화면` 오브젝트(DontDestroyOnLoad)에 UIDocument 하나를 만든다(정렬 100 — 프로젝트의 다른 UIDocument보다 위). Panel Settings는 DB › 화면 설정의 것. 없으면 경고하고 테마 없는 임시 설정으로 띄운다(기본 컨트롤 모양이 빠질 수 있다 → 3-1 화면 준비).
- 루트에 붙는 USS 순서: 라이브러리 기본(`Resources/MomoiProject/Screens.uss`) → 테마 USS(DB › 화면 설정, 위에서부터) → 화면마다 '더할 USS'. 뒤의 것이 이긴다.
- 테마 변수: `--momoi-text` · `--momoi-dim` · `--momoi-panel` · `--momoi-button` · `--momoi-button-hover` · `--momoi-accent` · `--momoi-accent-text` · `--momoi-backdrop` · `--momoi-radius` · `--momoi-font-size`. 프로젝트 화면의 USS도 이 변수를 쓰면 테마 하나로 모든 화면의 색이 바뀐다.
- **글꼴**: DB › 화면 설정 › 글꼴에 TTF·OTF를 넣으면 모든 화면에 쓰인다. 한글을 보이려면 한글이 든 글꼴이 필요하다(사람이 넣는다).
- 화면 크기: Panel Settings가 1920×1080 기준으로 늘이고 줄인다(가로세로 반반 맞춤). UXML은 1920×1080 기준 px로 쓴다.

---

## 3. 에디터에서 쓰는 법

### 3-1. 화면 준비 — `MomoiProject › 화면 › 화면 준비`

한 번 누르면 만든다(이미 있으면 건너뛴다):

| 만드는 것 | 뜻 |
|---|---|
| `Assets/UI/DefaultTheme.tss` | UI Toolkit 기본 테마(버튼·슬라이더의 기본 모양) |
| `Assets/UI/PanelSettings.asset` | 1920×1080 기준 화면 맞춤 + 위 테마 |
| `Assets/UI/Theme.uss` | 프로젝트 테마(2-7의 변수). 색·크기를 여기서 고친다 |
| `Assets/UI/Screens/` · `Assets/UI/Items/` | 화면 UXML·USS, 목록 칸 UXML을 둘 폴더 |
| DB › 화면 설정 `screenSettings`(적용 전) | 위 Panel Settings·테마가 연결된 항목 → Ctrl+S |

### 3-2. DB › 화면 설정(사이드바 선언 그룹)

| 칸 | 툴팁 | 값 |
|---|---|---|
| `panelSettings` | Panel Settings | `Assets/UI/PanelSettings.asset` |
| `theme` | 테마 USS | 모든 화면에 붙는 USS 목록 |
| `font` | 글꼴 | 한글이 든 TTF·OTF |
| `choiceWindow` | 고르기 창 저절로 | 기본 켬. 고르기 창을 같은 ID(`momoi.choice`)로 바꿨으면 켜 둔다. 다른 ID로 따로 만들었을 때만 끈다(2-5) |
| `pauseWhileChoosing` | 고르는 동안 게임 시간 멈춤 | 기본 끔. 실시간 게임의 레벨업 선택에서 켠다. `Time.timeScale`이라 연출도 멈춘다 → 턴제는 끈다(2-5) |
| `failureToast` | 실패를 알림으로 | 기본 켬 |
| `pauseMenu` | Esc로 일시정지 메뉴 | 기본 끔 |

- 항목은 하나만 둔다(둘 이상이면 첫 항목). 검증: Panel Settings가 비면 경고, 테마 USS에 빈 칸이 있으면 오류.

### 3-3. DB › 화면(사이드바 DB 그룹)

**+ 새로 만들기** → ID(예: `battleHud`). 저장하면 `GameIds.Screen.BattleHud`가 생긴다.

| 칸 | 툴팁 | 값 |
|---|---|---|
| `layout` | UXML | 화면 UXML(`Assets/UI/Screens/BattleHud.uxml`) |
| `styles` | 더할 USS | 이 화면에만 붙는 USS |
| `items` | 목록 칸 UXML | 목록 한 칸 UXML들(코드 `Item("파일 이름")`) |
| `layer` | 층 | `바탕(HUD)` / `창(메뉴)`(기본) / `팝업(위에 뜨는 창)` / `알림(입력을 막지 않음)` |
| `blockInput` | 아래 입력 막기 | 창일 때. 기본 켬 |
| `closeOnBack` | 뒤로(Esc)로 닫기 | 기본 켬. HUD는 끈다 |
| `pauseTime` | 열린 동안 게임 시간 멈춤 | 기본 끔 |
| `scenes` | 열 씬 | 씬 이름들. 그 씬이면 열고 아니면 닫는다 |
| `openOnStart` | 게임이 켜지면 열기 | 기본 끔 |

- 검증: UXML이 없으면 오류. '게임이 켜지면 열기'와 '열 씬'을 함께 켜면 경고.

### 3-4. 확인하기

- 플레이 중 하이어라키의 `[MomoiProject] 화면` › UIDocument를 고르고 **UI Toolkit Debugger**(Window › UI Toolkit › Debugger)로 층·화면 트리(`momoi-layer-…` › 화면 ID)를 본다.
- binding-path 칸이 바뀌지 않으면: 키 이름(DB › 플레이어 데이터 ID)·경로를 모니터 › 데이터 탭과 맞춰 본다.
- 선택 기능 '확인용 패널·샘플'의 **화면** 탭: 확인용 HUD(DB › 화면 `checkHud`)와 기본 화면을 하나씩 연다(`PendingChecks.md`).

---

## 4. 코드로 쓰는 법 — 문법 요소를 하나씩

### 4-1. `GameScreens`

| 멤버 | 뜻 |
|---|---|
| `GameScreen Open(string id, object argument = null)` | 연다(열려 있으면 그대로). 화면 코드를 돌려준다 |
| `bool Close(string id)` · `Close(GameScreen)` | 닫는다(열려 있었으면 참) |
| `CloseAll(ScreenLayer? layer = null)` | 그 층(비우면 전부)을 나중에 연 것부터 닫는다 |
| `bool IsOpen(id)` · `GameScreen Get(id)` · `T Get<T>()` · `All` | 열려 있는지 · 열린 화면 코드 · 열린 화면들 |
| `bool Back()` | 뒤로(2-1) |
| `bool Do(Func<GameAction> make)` | 화면 입력 → 행동(2-4). 했으면 참 |
| `Toast(string text, float seconds = 2.5f)` | 알림(텍스트 키면 번역) |
| `Confirm(message, yes, no = null, yesText = null, noText = null)` | 예/아니오 팝업 |
| `VisualElement Overlay` | 맨 위 겹침 층(입력을 막지 않음). 피해 숫자처럼 잠깐 띄우는 요소를 여기에(5-4) |
| `Action QuitToTitle` | 일시정지 메뉴 '타이틀로'가 할 일 |
| `Quit()` | 게임 끄기(에디터에서는 플레이 멈춤) |
| `event Opened` · `Closed` (`Action<GameScreen>`) | 열고 닫을 때 |
| `Choice` · `Confirmation` · `Settings` · `Slots` · `Pause` | 기본 화면 ID |

### 4-2. `GameScreen`

| 멤버 | 뜻 |
|---|---|
| `Id` · `Root` · `Argument` · `Data` · `IsOpen` | 2-3 |
| `Game` | `GBibleRuntime.Game` |
| `virtual string[] Watch` | 다시 그릴 키(기본 null = binding-path의 키들) |
| `virtual bool WaitForPresentation` | 연출이 끝난 뒤에 그리나(기본 참) |
| `virtual OnOpen()` · `Draw()` · `OnClose()` | 열 때 · 그릴 때 · 닫을 때 |
| `virtual Tick(float deltaTime)` | 열려 있는 동안 매 프레임 끝. `deltaTime`은 실제 시간(초, 일시정지와 상관없이). 쿨다운 원·타이머처럼 시간에 따라 바뀌는 칸. 데이터는 바꾸지 않는다(2-3) |
| `Close()` | 이 화면 닫기 |
| `T Q<T>(name)` · `VisualElement Q(name)` | 이름으로 찾기(없으면 예외) |
| `OnClick(name 또는 Button, Func<GameAction>)` | 버튼 → 행동 |
| `OnClick(name 또는 Button, Action)` | 버튼 → 화면 일 |
| `ElementList<T> List<T>(container, item, items, bind, keys…)` | 목록 칸 |
| `VisualTreeAsset Item(name)` | 목록 칸 UXML 찾기 |
| `T Own<T>(T disposable)` | 닫을 때 정리 |

### 4-3. `ElementList<TItem>`

```csharp
var hand = new ElementList<string>(Q("hand"), Item("CardItem"), () => Game.Data().Run.battle.hand, BindCard, GameIds.PlayerData.Run.Id);
hand.OnCreate = (element, id) => element.Q<Button>("card").clicked += () => GameScreens.Do(() => new PlayCard { card = Rules.CardOf(Game, id) });
hand.OnRemove = (element, done) => { element.AddToClassList("card--gone"); element.schedule.Execute(done).StartingIn(250); };
```

| 멤버 | 뜻 |
|---|---|
| 생성자 `(container, item, items, bind, [waitForPresentation = true,] keys…)` | `item`이 null이면 빈 `VisualElement` |
| `OnCreate` · `OnRemove` | 2-6 |
| `Find(id)` · `Find(item)` · `Elements` | 칸 찾기 |
| `Dispose()` | 칸을 모두 없애고 구독을 끊는다(`GameScreen.List`로 만들면 닫을 때 저절로) |

### 4-4. `ScreenBinding` (코어)

`ScreenBinding.Parse("run:gold@hud.gold")` → `Key` · `Path` · `TextKey` · `HasData`. `Read(session)`(값) · `Text(session, 틀)`(보일 글) · `Shown`·`Format`·`Number`·`Flag`(값 바꾸기). 화면 밖(테스트·서버)에서도 같은 규칙으로 읽는다.

### 4-5. `GameView.FindElement` · `RegisterElement`

`ElementList`가 칸을 항목 id로 등록한다. 연출은 `GameView.FindElement(e.targetId)`로 그 칸(화면에 붙어 있을 때만)을 찾는다. 직접 만든 요소도 `RegisterElement(id, 요소)`·`UnregisterElement(id)`.

---

## 5. 상황별 예제

### 5-1. 전투 HUD — UXML + DB + 화면 코드

`Assets/UI/Screens/BattleHud.uxml`:

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements">
    <ui:Template name="CardItem" src="../Items/CardItem.uxml" />
    <ui:VisualElement class="hud">
        <ui:VisualElement class="hud__top">
            <ui:Label binding-path="run:hero.hp@hud.hp" />            <!-- 텍스트 hud.hp = "체력 {value}" -->
            <ui:Label binding-path="run:gold@hud.gold" />
            <ui:Label binding-path="run:battle.energy@hud.energy" />
        </ui:VisualElement>
        <ui:VisualElement name="hand" class="hud__hand" />
        <ui:VisualElement class="hud__buttons">
            <ui:Button name="undo" binding-path="@hud.undo" class="momoi-button" />
            <ui:Button name="end-turn" binding-path="@hud.endTurn" class="momoi-button momoi-button--primary" />
        </ui:VisualElement>
    </ui:VisualElement>
</ui:UXML>
```

`Assets/UI/Items/CardItem.uxml`:

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements">
    <ui:Button name="card" class="card">
        <ui:Label name="cost" class="card__cost" />
        <ui:Label name="title" class="card__title" />
    </ui:Button>
</ui:UXML>
```

DB › 화면 `battleHud`: UXML `BattleHud.uxml` · 층 `바탕(HUD)` · 뒤로로 닫기 끔 · 열 씬 `Battle`.

`Assets/Code/Client/Screens/BattleHud.cs`:

```csharp
[BindTo(GameIds.Screen.BattleHud)]
public class BattleHud : GameScreen
{
    protected override void OnOpen()
    {
        OnClick("end-turn", () => new EndTurn());
        OnClick("undo", () => Game.Session.Undo());
        var hand = List<string>("hand", Item("CardItem"), () => Game.Data().Run.battle?.hand ?? new List<string>(), (element, id) =>
        {
            var card = Rules.CardData(Game, Rules.CardOf(Game, id).card);
            element.Q<Label>("cost").text = card.cost.ToString();
            element.Q<Label>("title").text = Texts.NameOf(Rules.CardOf(Game, id).card);
        }, GameIds.PlayerData.Run.Id);
        hand.OnCreate = (element, id) => element.Q<Button>("card").clicked +=
            () => GameScreens.Do(() => new PlayCard { card = Rules.CardOf(Game, id) });
    }
}
```

- 체력·골드·에너지는 코드가 없다(binding-path). 에너지가 모자라면 `PlayCard` 안의 `Game.Pay`가 실패하고 그 글이 알림으로 뜬다(30장 2-4).
- 피해 연출이 재생 중이면 손패·체력은 연출이 끝난 뒤 바뀌고, 그동안 버튼은 무시된다.

### 5-2. 상점 — 목록 + 사기

```csharp
[BindTo(GameIds.Screen.Shop)]
public class ShopScreen : GameScreen
{
    protected override void OnOpen()
    {
        OnClick("leave", Close);
        var goods = List<ShopItem>("goods", Item("GoodsItem"), () => Game.Data().Run.shop, (element, item) =>
        {
            element.Q<Label>("title").text = Texts.NameOf(item.card);
            element.Q<Label>("price").text = Price().ToString();                // 표시만: 행동과 같은 밸런스 값을 읽는다
            element.EnableInClassList("goods--sold", item.sold);
        }, GameIds.PlayerData.Run.Id);
        goods.OnCreate = (element, item) => element.Q<Button>("buy").clicked +=
            () => GameScreens.Do(() => new BuyCard { source = item.card });     // 무엇을 살지만 보낸다. 가격은 규칙이
    }

    int Price() => (int)Game.Balance(GameIds.Balance.ShopCardPrice, Game.Data().Run.floor);
}

// 규칙 쪽(30장 5-3): 가격은 행동이 밸런스 값에서 읽는다
[Serializable, BlockInfo("카드 사기")]
public class BuyCard : GameAction
{
    public string source;     // 살 카드 원본 ID

    protected override void Run()
    {
        var run = Game.Data().Run;
        var price = (int)Game.Balance(GameIds.Balance.ShopCardPrice, run.floor);
        Game.Pay(GameIds.PlayerData.Run.Id, "gold", price, what: "gold.name");   // 모자라면 플레이어용 실패 → 알림
        run.deck.Add(Rules.NewCard(Game, source));
    }
}
```

DB › 화면 `shop`: 층 `창(메뉴)` · 아래 입력 막기 · 뒤로로 닫기. 골드가 모자라면 `BuyCard`의 `Game.Pay`가 알림을 띄운다.

- **화면이 가격을 행동에 넣지 않는다**(`new BuyCard { source, price = … }`). 행동 필드는 보내는 쪽이 정하는 값이라 멀티플레이에서 상대가 가격 0을 보낼 수 있고, 가격 규칙이 화면 코드에 복사된다. 행동은 무엇을 할지(살 카드)만 받고, 가격은 원본·밸런스 값에서 규칙이 읽는다. 화면은 같은 값을 읽어 **보이기만** 한다(30장 5-3).
- 물건마다 가격이 다르면 원본에 가격을 두고(`Rules.CardData(Game, source).price`) 행동이 그것을 읽는다. 상점이 열릴 때 정해지는 가격(무작위 할인)이면 상점을 여는 행동이 판 데이터(`run.shop[i].price`)에 적고, `BuyCard`가 판 데이터에서 그 물건을 찾아 읽는다.

### 5-3. 타이틀 — 이어 하기 · 새 게임 · 슬롯 · 설정 · 종료

```csharp
[BindTo(GameIds.Screen.Title)]
public class TitleScreen : GameScreen
{
    protected override void OnOpen()
    {
        OnClick("continue", () => GBibleRuntime.LoadRun(GBibleRuntime.Session));
        OnClick("new", NewGame);
        OnClick("slots", () => GameScreens.Open(GameScreens.Slots, new SlotsRequest { count = 3 }));
        OnClick("settings", () => GameScreens.Open(GameScreens.Settings));
        OnClick("quit", GameScreens.Quit);
    }

    protected override void Draw() => Q<Button>("continue").SetEnabled(GBibleRuntime.HasSavedRun);

    void NewGame()
    {
        GBibleRuntime.NewRun(Game.Session);      // 1) 행동 밖에서: 판 값을 처음 값으로, Undo 기록과 판 저장 파일(run.json)을 지운다
        Game.Do(new StartRun());                 // 2) 그다음 새 판을 여는 행동(주인공·시작 덱·첫 전투)
    }
}
```

DB › 화면 `title`: 층 `창(메뉴)` · 뒤로로 닫기 끔 · 열 씬 `Title`. `GBibleRuntime.LoadRun`은 행동이 아니므로 `Action` 쪽 `OnClick`이다(bool을 돌려주지만 버린다).

- **'새 게임'은 `NewRun` 다음 `StartRun`이다.** `OnClick("new", () => new StartRun())`만 하면 `StartRun`이 다시 채우지 않는 지난 판의 값(세션 구역의 다른 플레이어 데이터 — 월드 상태 등)과 Undo 기록, `run.json`이 남는다(`NewRun`은 세션 구역 전체를 처음 값으로 되돌린다). 화면은 새 판인데 '이어 하기'는 예전 판이 된다. `GBibleRuntime.NewRun`은 행동 안에서 부르면 예외("[MomoiProject] 행동 실행 중에는 새 판을 시작할 수 없습니다. 행동 밖(버튼 등)에서 부르세요.")라 `StartRun` 안에 넣지 않는다 → 화면 일(`Action` 쪽 `OnClick`)로 둘을 차례로 부른다.
- `NewGame`은 `Action` 쪽이라 `Busy`를 보지 않고 실패 알림도 없다. `StartRun`이 플레이어용 실패를 던질 수 있으면 `GameScreens.Do(() => new StartRun())`로 부른다(2-4).

### 5-4. 피해 숫자를 적 칸 위에 — `FindElement` · `Overlay`

```csharp
Game.Session.Events.SubscribeAfterAction<DamageDealt>(e => GameView.Present(done =>
{
    var target = GameView.FindElement(e.targetId);
    if (target == null) { done(); return; }
    var number = new Label(e.amount.ToString());
    number.AddToClassList("damage-number");
    var at = target.worldBound.center;
    number.style.position = Position.Absolute;
    number.style.left = at.x;
    number.style.top = at.y;
    GameScreens.Overlay.Add(number);
    number.schedule.Execute(() => { number.RemoveFromHierarchy(); done(); }).StartingIn(600);
}));
```

- 적 목록을 `ElementList`로 만들었으면 칸이 적 id로 등록되어 있다. `worldBound`는 패널 좌표라 `Overlay`(같은 패널)에 그대로 놓는다.

### 5-5. 테마 바꾸기

`Assets/UI/Theme.uss`:

```css
:root {
    --momoi-panel: rgb(250, 244, 232);
    --momoi-text: rgb(40, 32, 24);
    --momoi-button: rgb(232, 220, 196);
    --momoi-accent: rgb(214, 92, 64);
    --momoi-accent-text: rgb(255, 255, 255);
    --momoi-font-size: 24px;
}
.momoi-panel { border-width: 3px; border-color: rgb(120, 90, 60); }
```

고르기 창·설정·알림을 포함해 모든 기본 화면과, 같은 변수를 쓰는 프로젝트 화면의 색이 바뀐다.

### 5-6. AI가 화면 하나를 만드는 순서

1. 화면 목록(기획 명세서 6절)에서 기본 화면으로 되는 것(고르기·확인·설정·슬롯·일시정지·알림)을 빼고 남은 화면마다:
2. `Assets/UI/Screens/<이름>.uxml`(+ 필요하면 `.uss`), 목록이 있으면 `Assets/UI/Items/<칸 이름>.uxml`. 1920×1080 기준. 색·크기는 테마 변수(`var(--momoi-…)`)를 쓴다. 값 칸은 binding-path, 버튼·목록 컨테이너에는 `name`.
3. 버튼·목록이 있으면 `Assets/Code/Client/Screens/<이름>.cs`(`[BindTo(GameIds.Screen.X)] : GameScreen`).
4. DB 추가 메뉴(관례 15절)에 화면 항목을 더한다 — `ScreenSetup.Ensure()`를 먼저 부르고, `layout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/Screens/<이름>.uxml")`, 층·뒤로·열 씬을 정한다.
5. 그림이 필요한 곳(카드 그림·아이콘)은 지금은 색 상자(배경색)로 두고, 사람이 넣을 에셋 목록(경로·크기·어디에 쓰는지)을 보고에 적는다.

```csharp
// Assets/Code/Editor/AddScreens.cs — DB 추가 메뉴
[MenuItem("MomoiProject/DB 추가/화면")]
static void Run()
{
    ScreenSetup.Ensure();
    Add("battleHud", "Assets/UI/Screens/BattleHud.uxml", ScreenLayer.Base, back: false, scene: "Battle");
    Add("shop", "Assets/UI/Screens/Shop.uxml", ScreenLayer.Window, back: true);
    EditorDatabase.Staging.Touch();
    DatabaseWindow.Open();
}

static void Add(string id, string uxml, ScreenLayer layer, bool back, string scene = null)
{
    if (EditorDatabase.Instance.FindById(id) != null) return;
    var screen = (ScreenData)EditorDatabase.Staging.Add(typeof(ScreenData), id);
    screen.layout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxml);
    screen.layer = layer;
    screen.closeOnBack = back;
    if (scene != null) screen.scenes.Add(scene);
}
```

---

## 6. 주의할 점 · 자주 하는 실수

| 증상 | 원인 · 고치는 법 |
|---|---|
| 경고 "Panel Settings가 없어 테마 없는 임시 설정으로…", 슬라이더·토글 모양이 없음 | 화면 준비(3-1)를 하지 않았다. 메뉴를 누르고 Ctrl+S. |
| 한글이 □로 보인다 | DB › 화면 설정 › 글꼴에 한글 글꼴이 없다. |
| "[MomoiProject] 없는 화면 'shp'입니다" | ID 오타. `GameIds.Screen` 상수를 쓴다. 저장(Ctrl+S) 전이면 상수가 아직 없다. |
| "화면 'shop'의 UXML에 이름(name)이 'buy'인 Button이(가) 없습니다" | UXML의 `name`과 코드의 이름이 다르다. 목록 칸 안의 버튼은 `Q`가 아니라 `element.Q<Button>()`(칸 안에서 찾기). |
| binding-path 칸이 비어 있다 | 키·경로가 틀렸거나 그 값이 아직 없다(모니터 › 데이터 탭). 목록 칸 안의 binding-path는 읽지 않는다(2-2). |
| 버튼을 눌러도 아무 일이 없다 | 연출이 재생 중이거나 선택을 기다리는 중이다(`GameView.Busy`). 고르기 창이 떠 있는지 본다. |
| 버튼이 두 번 돈다 | `OnOpen` 밖(생성자)이나 `Draw`에서 `clicked +=`를 했다. 버튼 잇기는 `OnOpen`(열 때마다 새 트리)이나 `OnCreate`(칸마다 한 번)에서. |
| HUD 아래의 월드를 클릭할 수 없다 | 화면 전체를 덮는 요소에 배경이 있거나 `picking-mode`가 Position이다. 빈 컨테이너에는 `picking-mode="Ignore"`. |
| Esc가 HUD를 닫는다 | HUD의 '뒤로로 닫기'가 켜져 있다(기본 켬). HUD는 끈다. |
| 고르기 창이 두 개 뜬다(프로젝트 것과 기본 것) | 프로젝트가 **다른 ID**로 고르기 화면을 만들었다. 그때만 '고르기 창 저절로'를 끈다. 같은 ID(`momoi.choice`)로 바꾸면 하나만 뜬다(2-5). |
| 고르기 창을 같은 ID로 바꿨더니 열리지 않는다 | '고르기 창 저절로'를 껐다. 저절로 여는 것이 `momoi.choice`라 켜 둔다(2-5). |
| 바꾼 고르기 창이 Esc로 닫혔다가 바로 다시 뜬다 | 같은 ID의 DB 항목에서 '뒤로(Esc)로 닫기'가 켜져 있다(기본 켬). 선택 대기가 남아 있어 다음 프레임에 다시 열린다. 끈다. |
| 실시간 레벨업 고르기 중에 적이 계속 움직인다 | DB › 화면 설정 '고르는 동안 게임 시간 멈춤'을 켠다(같은 ID로 바꾼 창이면 그 항목의 '열린 동안 게임 시간 멈춤'). |
| 턴제에서 고르기 창이 뜨면 피해 숫자·카드 연출이 멈춘다 | '고르는 동안 게임 시간 멈춤'이 켜져 있다(`Time.timeScale`이라 연출도 멈춘다). 턴제는 끈다. |
| 고르기 창 후보에 설명이 없다 | 텍스트 표에 `<후보 id>.desc`가 없다. 키를 넣는다(2-5). |
| 쿨다운 원이 데이터가 바뀔 때만 움직인다 | `Draw`에서 그렸다. `Draw`는 값이 바뀔 때만 돈다. 시간에 따라 바뀌는 칸은 `Tick`(2-3). |
| 일시정지했는데 쿨다운 숫자가 계속 준다 | `Tick`의 `deltaTime`(실제 시간)을 쌓았다. 게임 시간 쿨다운은 그때마다 게임 시계(`Game.Session.Clock.Time`)에서 남은 시간을 계산한다. |
| 타이틀 '새 게임' 뒤 '이어 하기'가 예전 판을 연다 | `StartRun`만 보냈다. `GBibleRuntime.NewRun(session)` 다음 `Game.Do(new StartRun())`(행동 밖, 5-3). |
| "행동 실행 중에는 새 판을 시작할 수 없습니다" | `NewRun`을 행동(`StartRun.Run`) 안에서 불렀다. 화면 일로 행동 밖에서 부른다(5-3). |
| 상점 가격이 화면마다 다르게 들어간다 | 화면이 가격을 행동 필드에 넣었다. 행동은 살 것만 받고 가격은 규칙이 읽는다(5-2). |
| 설정 화면에서 슬라이더를 끌면 값이 띄엄띄엄 | 설정의 '단위'가 0보다 크다(31장). 의도한 것이다. |
| 화면이 씬을 바꾸면 사라진다 | '열 씬'에 다른 씬이 적혀 있다. 비우면 씬과 상관없다. |
