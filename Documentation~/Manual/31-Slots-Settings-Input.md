# 31. 슬롯·설정·입력

**한 줄 요약**: **슬롯**은 저장 묶음(프로필 + 진행 중인 판) 하나다. 슬롯마다 저장 폴더가 따로이고, `GBibleRuntime.UseSlot(이름)`으로 바꾸면 지금 슬롯을 저장하고 그 슬롯으로 세션을 새로 시작한다. **설정**(DB › 설정)은 음량·언어·전체 화면·전투 속도 같은 설정 화면의 칸이다. 값은 기기마다 하나(`settings.json`)이고 슬롯·Undo·판 저장·멀티플레이와 상관없다. **적용 대상**을 고르면 라이브러리가 켤 때와 바꿀 때 바로 적용하고, 게임 코드는 `GameSettings.Changed`를 받는다. **입력**은 선택 기능 '입력(키 바꾸기)'의 `GameInput`이 Input System의 키 바꾸기·되돌리기·저장을 맡는다. 셋 다 기기·화면 쪽 일이라 행동(규칙)이 아니다.

> 설정 화면·슬롯 화면은 기본 화면으로 들어 있다(`GameScreens.Open(GameScreens.Settings)` · `GameScreens.Open(GameScreens.Slots, new SlotsRequest { … })`, [35장](35-Screens.md) 2-5). 이 장의 API는 그 화면들이 쓰는 것이고, 화면을 직접 만들 때 쓴다.

**언제 쓰는가**

- 타이틀의 "슬롯 1·2·3": 저장 시각·플레이 시간·"3층 · 골드 120"을 보이고, 골라서 시작하거나 지운다(슬롯).
- 설정 화면: 소리(브금·효과음 음량)·화면(전체 화면·해상도·수직 동기화)·게임(언어·전투 속도·자막)(설정).
- 게임 코드가 읽는 기기별 값: 전투 연출 속도, 화면 흔들림 끄기(설정, 적용 대상 없음).
- 키 바꾸기 화면: 키를 눌러 바꾸고, 겹치면 알리고, 기본으로 되돌린다(입력).

이 문서에서 쓰는 용어:

| 용어 | 뜻 |
|---|---|
| 슬롯 | 저장 묶음 하나: `profile.json`(프로필) · `run.json`(진행 중인 판) · `slot.json`(요약). 이름으로 부른다(`"1"`, `"auto"`). 이름 `""`이 기본 슬롯이다. |
| 지금 슬롯 | `GBibleRuntime.Slot`. 지금 세션이 읽고 쓰는 슬롯. |
| 슬롯 요약 (`SlotInfo`) | 슬롯 고르기 화면용: 저장 유무 · 마지막 저장 시각 · 플레이 시간 · 요약 한 줄. |
| 설정 | DB › 설정의 항목 하나 = 설정 화면의 칸 하나. 코드 형식은 `SettingData`. ID 상수는 `GameIds.Setting.X`. |
| 분류 (category) | 설정 화면에서 묶이는 이름(소리·화면·조작·게임 …). |
| 종류 (`SettingKind`) | 켜기/끄기 · 숫자(슬라이더) · 선택지 · 글. 종류마다 기본값 칸이 다르다. |
| 적용 대상 (`SettingTarget`) | 라이브러리가 바로 적용하는 곳(언어 · 음량 · 전체 화면 · 해상도 · 수직 동기화 · 최대 프레임). `없음`이면 게임 코드가 받아 적용한다. |
| 설정 파일 | `<persistentDataPath>/MomoiProject/settings.json`. 기기마다 하나. 설정 값과, 모듈·라이브러리의 기기별 원시 값(Raw: 바꾼 키, 마지막 슬롯)이 든다. |
| 동작 (`InputAction`) | Input System의 입력 하나(`Player/Jump`). 동작 묶음(액션 맵) 안에 있다. |
| 바인딩 (binding) | 동작에 연결된 키 하나. 동작마다 여럿일 수 있고(키보드·패드) 번호(`bindingIndex`)로 가리킨다. |
| composite | WASD처럼 여러 키를 하나로 묶은 바인딩. 묶음 자체의 다음 번호부터 안의 칸(Up·Down·Left·Right)이다. |

---

## 1. 무엇으로 만드나

| 기획 문장 | 만드는 것 |
|---|---|
| "타이틀에 슬롯 세 개, 각각 층·플레이 시간 표시" | `SlotInfoOf` · `SlotSummary` · `UseSlot`(5-1) |
| "슬롯 지우기", "슬롯 복사" | `DeleteSlot` · `CopySlot`(4-1) |
| "설정 화면: 소리·화면·게임 탭" | DB › 설정(분류) + `GameSettings.All`(5-2) |
| "언어 선택", "브금 음량" | 설정 + 적용 대상 `언어` · `브금 음량`(코드 없음, 5-4) |
| "전투 연출 속도 ×1~×3" | 설정(적용 대상 없음) + `GameSettings.Changed`(5-3) |
| "키 바꾸기, 겹치면 알림, 기본으로" | `GameInput.Rebind` · `RebindResult.Conflict` · `ResetBindings`(5-5) |

> **규칙: 설정은 플레이어 데이터가 아니다.** 음량·언어·키는 그 기기의 사람이 정하는 값이라 슬롯을 바꿔도, Undo해도, 판을 불러와도, 멀티플레이 상대가 바꿔도 그대로여야 한다. 그래서 행동이 아니라 `GameSettings.Set`으로 바로 바꾼다. 게임 규칙에 영향을 주는 값(난이도처럼 결과를 바꾸는 것)은 설정이 아니라 플레이어 데이터(프로필)에 두고 행동으로 바꾼다.

---

## 2. 동작 규칙

### 2-1. 슬롯과 저장 폴더

```
<persistentDataPath>/MomoiProject/
  settings.json        설정(기기마다 하나 — 슬롯과 상관없음)
  profile.json         기본 슬롯("")의 프로필
  run.json             기본 슬롯의 판
  slot.json            기본 슬롯의 요약
  slots/
    1/  profile.json · run.json · slot.json
    2/  …
```

- 기본 슬롯(`""`)은 `MomoiProject/` 바로 아래다(슬롯이 없던 때와 같은 자리). 다른 슬롯은 `MomoiProject/slots/<이름>/`이다.
- 슬롯 이름은 글자(한글 포함)·숫자·`-`·`_`만 쓴다. 다른 글자가 있으면 `ArgumentException`("[MomoiProject] 슬롯 이름 'a/b'에는 글자·숫자·-·_만 쓸 수 있습니다.").
- `slot.json`(저장 시각 · 플레이 시간 · 요약 한 줄)은 프로필이나 판을 저장할 때마다 함께 쓴다. 요약 한 줄은 게임이 정한다(`GBibleRuntime.SlotSummary`, 4-1).
- **마지막으로 쓴 슬롯을 기억한다.** `UseSlot`이 설정 파일(원시 값 `momoi.slot`, 2-3)에 남기고, 다음에 게임을 켜면 그 슬롯으로 시작한다. 처음 켤 때는 기본 슬롯이다.
- **플레이 시간**은 실제 시간이다(일시정지·배속과 상관없이 흐른다). 세션이 있는 동안 쌓이고 `slot.json`에 저장된다. 슬롯을 바꾸면 그 슬롯에 저장된 값부터 이어 센다.
- `SavePath`·`RunSavePath`·`HasSavedRun`·`SaveProfile`·`SaveRun`·`LoadRun`·`NewRun`(06장)은 모두 **지금 슬롯**의 파일을 쓴다.

### 2-2. 슬롯 바꾸기 · 지우기 · 복사

| 하는 일 | 순서 |
|---|---|
| `UseSlot(이름)` | 같은 슬롯이면 아무 일도 없다. 다르면 ① 지금 세션을 끝낸다(프로필 저장, `AutoSaveRun`이면 판 저장, `Stopped`). 행동을 한 번도 하지 않았고 그 슬롯에 저장 파일도 없던 세션(타이틀에서 막 켠 기본 슬롯)은 저장하지 않는다 → 빈 저장이 남지 않는다 ② 그 슬롯을 기억한다 ③ 세션을 새로 만든다(그 슬롯의 프로필을 불러오고 `Started`가 다시 온다) |
| `DeleteSlot(이름)` | 세 파일을 지운다. 지금 슬롯이면 **저장하지 않고** 세션을 끝내고, 지운 뒤 세션을 새로 시작한다(빈 프로필·새 판, `Started`가 다시 온다). 폴더는 남지만 `Slots()`에는 나오지 않는다 |
| `CopySlot(원본, 대상)` | 원본의 세 파일을 대상에 덮어쓴다(원본에 없는 파일은 대상에서도 지운다). 원본이 지금 슬롯이면 먼저 저장한다. 대상이 지금 슬롯이면(세션이 도는 중) `InvalidOperationException`("[MomoiProject] 지금 쓰는 슬롯 '1'(으)로는 복사할 수 없습니다. 다른 슬롯으로 바꾼 뒤(UseSlot) 복사하세요.") — 실행 중인 세션이 저장할 때 복사한 내용을 다시 덮기 때문이다 |

- `UseSlot` 뒤의 새 세션에서 **판은 자동으로 불러오지 않는다.** 이어 하려면 `GBibleRuntime.LoadRun`을 부른다(DB › Bootstrap의 '시작할 때 이어 하기'를 켰으면 런타임이 한다).
- 세션이 새로 만들어지면 `GameView.Redraw`·`ViewList`·컴포넌트 '데이터 연결'·'월드 상태 연결'·씬 흐름은 저절로 새 세션에 다시 붙고 모두 다시 그린다(32장). 직접 받은 이벤트(`session.Events.Subscribe…`·`session.Ticked`)는 **예전 세션** 것이라 새 세션에서 오지 않는다. 그래서 `UseSlot`은 타이틀처럼 게임 화면이 없을 때 부르고, 이벤트 연결은 `Started`에서 한다.
- 하이어라키의 `[MomoiProject]` 오브젝트는 슬롯을 바꿔도 하나다.

### 2-3. 설정 값

- **기기마다 하나**(`settings.json`). 플레이어 데이터가 아니라서 슬롯·Undo·판 저장·멀티플레이와 상관없다. 행동 밖(화면 코드)에서 그냥 부른다.
- **읽기**: 저장된 값이 있으면 그 값(종류에 맞게 고친 것), 없으면 기본값.
- **바꾸기**(`GameSettings.Set(ID, 값)`): ① 종류에 맞게 고친다 ② 지금 값과 같으면 끝(아무 일도 없다) ③ 파일에 저장 ④ 적용 대상이면 적용(2-4) ⑤ `Changed(ID)`를 쏜다.

| 종류 (`SettingKind`) | 값을 고치는 규칙 | 기본값 |
|---|---|---|
| 켜기/끄기 (`Toggle`) | `bool`. 글자 `"true"`/`"false"`도 읽는다. 숫자는 0이 아니면 참. 그 밖은 기본값 | `defaultToggle` |
| 숫자(슬라이더) (`Slider`) | 최솟값~최댓값으로 자른다(최댓값 > 최솟값일 때). 단위가 0보다 크면 최솟값부터 단위의 배수로 맞춘다(최솟값 0 · 단위 0.5면 0.7 → 0.5) | `defaultNumber`(같은 규칙으로 맞춘 값) |
| 선택지 (`Choice`) | 선택지의 값 중 하나여야 한다(아니면 기본값). 선택지를 비운 언어·해상도는 아무 글자나 받는다 | `defaultChoice`. 비우면 첫 선택지. 선택지도 없으면 언어는 지금 언어, 해상도는 지금 해상도 |
| 글 (`Text`) | 글자로 바꾼다 | `defaultText` |

- `Reset(ID)`: 저장된 값을 지운다(기본값으로). 저장된 값이 없었으면 아무 일도 없다(`Changed`도 오지 않는다).
- `ResetAll(분류)`: 그 분류(비우면 전부)의 설정을 하나씩 `Reset`한다. 바꾼 키(입력)는 설정이 아니라 원시 값이라 지우지 않는다 → `GameInput.ResetBindings()`.
- 설정 목록은 게임이 켜질 때 한 번 DB에서 읽는다(`GameSettings.All`, DB 순서).
- **원시 값**(`GameSettings.Raw`·`SetRaw`): 모듈·라이브러리의 기기별 값(마지막 슬롯 `momoi.slot`, 바꾼 키 `momoi.input`)도 같은 파일에 둔다. 파일 안에서는 키 앞에 `$`가 붙는다(`$momoi.slot`). DB ID는 `$`로 시작할 수 없으므로 설정 ID와 겹치지 않는다.

### 2-4. 적용 대상

| 적용 대상 (`SettingTarget`) | 맞는 종류 | 적용 | 언제 |
|---|---|---|---|
| 없음(게임 코드) (`None`) | 아무거나 | 게임 코드가 `Changed`를 받아 적용한다(5-3) | — |
| 언어 (`Language`) | 선택지(비우면 지원 언어가 저절로: `GameLanguage.Available`) | `GameLanguage.Set(값, remember: false)` | 언제나 |
| 전체·브금·목소리·효과음·UI 음량 (`MasterVolume` … `UiVolume`) | 숫자(0~1) | `GameAudio.SetVolume(채널, 값, save: false)` | 언제나 |
| 전체 화면 (`Fullscreen`) | 켜기/끄기 | `Screen.fullScreen` | 빌드에서만 |
| 해상도 (`Resolution`) | 선택지(비우면 `Screen.resolutions`가 저절로, 값은 `"1920x1080"`) | `Screen.SetResolution(가로, 세로, 지금 화면 모드)` | 빌드에서만 |
| 수직 동기화 (`VSync`) | 켜기/끄기 | `QualitySettings.vSyncCount` = 1 / 0 | 언제나 |
| 최대 프레임(0 = 제한 없음) (`FrameRate`) | 숫자 · 선택지 | `Application.targetFrameRate`(0 이하면 −1 = 제한 없음) | 언제나 |

- **게임이 켜질 때**(`GBibleRuntime.Start` 맨 처음, 세션보다 먼저) 설정 파일과 DB를 읽고 모든 설정의 적용 대상을 적용한다. 바꿀 때는 그 칸만 적용한다.
- 전체 화면·해상도는 에디터 창을 건드리지 않도록 빌드에서만 적용한다.
- 적용이 실패하면 콘솔에 경고("[MomoiProject] 설정 'resolution'를 적용하지 못했습니다: …")를 남긴다.
- 언어·음량의 값은 설정 파일에 남으므로 `GameLanguage`·`GameAudio`의 PlayerPrefs 저장은 쓰지 않는다(`remember`·`save`가 거짓). 그래서 **음량 설정을 둔 채널은 게임이 켜질 때 설정 값으로 맞춰진다.** 같은 채널을 다른 곳에서 `GameAudio.SetVolume`으로 바꾸면 다음 실행 때 설정 값으로 돌아간다. 음량은 설정으로만 바꾼다(26장).
- 언어도 같다. 설정에 저장된 언어가 있으면 켤 때 그 언어가 이긴다. 저장된 값이 없으면 `GameLanguage`가 고른 언어(저장된 언어 → 기기 언어 → 원문 언어, 23장 4절)가 그대로다.

### 2-5. 입력(키 바꾸기)

- 선택 기능 '입력(키 바꾸기)'이다(3-3). Input System 패키지(`com.unity.inputsystem`)가 있어야 컴파일된다.
- `GameInput.Use(동작 에셋)`: 게임이 켜질 때 한 번. 설정 파일에 저장해 둔 바꾼 키(원시 값 `momoi.input`, Input System의 override JSON)를 덮어 씌우고 켠다. 읽지 못하면 경고하고 기본 키로 시작한다.
- `GameInput.Rebind(동작, 번호, 결과 받기)`:
  1. 그 동작을 잠시 끄고 다음에 누른 키를 기다린다. **Esc는 취소**이고, 마우스 위치·움직임은 무시한다(0.1초 더 기다려 비슷한 컨트롤 중 맞는 것을 고른다).
  2. 키를 받으면 **같은 묶음(액션 맵)의 다른 바인딩**과 겹치는지 본다(대소문자 무시, 같은 동작의 다른 바인딩도 센다). 겹치면 원래 키로 되돌리고 결과의 `Conflict`에 겹친 동작을 담는다.
  3. 겹치지 않으면 설정 파일에 저장하고 `GameInput.Changed`를 쏜다.
  4. 동작을 다시 켜고(원래 켜져 있었으면) 결과를 넘긴다: `Done` · `Cancelled` · `Conflict`.
- 겹쳐도 바꾸려면 `allowDuplicates: true`.
- composite(WASD 묶음) 자체는 바꾸지 못한다(`InvalidOperationException`). 안의 칸을 하나씩 바꾼다(번호 + 1 …).
- 한 번에 하나만 기다린다. 기다리는 중에 `Rebind`를 또 부르면 앞의 것은 취소된다(`Cancelled` 결과).
- `ResetBindings(동작 = null)`: 바꾼 키를 지우고(동작을 주면 그 동작만) 저장한 뒤 `Changed`를 쏜다.
- **입력을 읽는 것은 Input System 그대로다**(`action.WasPressedThisFrame()`). 게임 규칙은 입력이 아니라 행동(`Game.Do`)으로 바뀐다(02장).

---

## 3. 에디터에서 쓰는 법

### 3-1. 슬롯

슬롯은 에디터 화면이 없다. 플레이 중 모니터 **명령** 탭의 저장 명령(**프로필 저장**·**판 저장**·**판 불러오기**·**새 판**)은 지금 슬롯의 파일을 다룬다(22장 7절). 파일은 `Application.persistentDataPath`(Windows: `%USERPROFILE%\AppData\LocalLow\<회사>\<제품>`) 아래 `MomoiProject`에 있다.

### 3-2. DB › 설정

`MomoiProject → DB` → 사이드바 **선언** 그룹의 **설정** 탭 → **+ 새로 만들기** → ID(예: `bgmVolume`). 저장하면 `GameIds.Setting.BgmVolume`이 생긴다. 항목 하나가 설정 화면의 칸 하나이고, 목록 순서가 `GameSettings.All`의 순서다.

| 칸 | 툴팁 | 값 |
|---|---|---|
| `category` | 분류 / 설정 화면에서 묶이는 이름(소리·화면·조작·게임 …) | 글자(기본 "게임") |
| `spec` › `kind` | 값 › 종류 | `켜기/끄기`(기본) / `숫자(슬라이더)` / `선택지` / `글` |
| `spec` › `defaultToggle` | 기본값 | 켜기/끄기일 때만 보인다 |
| `spec` › `defaultNumber` · `min` · `max` · `step` | 기본값 · 최솟값 · 최댓값 · 단위(0이면 연속) | 숫자일 때만. 최댓값 기본 1 |
| `spec` › `defaultChoice` | 기본값(선택지의 값, 비우면 첫 선택지) | 선택지일 때만 |
| `spec` › `choices` | 선택지(언어·해상도는 비우면 자동) | 줄마다 `value`(값) · `label`(보일 글(텍스트 키 또는 글)). 선택지일 때만 |
| `spec` › `defaultText` | 기본값 | 글일 때만 |
| `target` | 적용 대상 / 라이브러리가 바로 적용하는 것. 없음이면 게임 코드가 GameSettings.Changed로 받아 적용한다 | `없음(게임 코드)`(기본) / `언어` / `전체 음량` / `브금 음량` / `목소리 음량` / `효과음 음량` / `UI 음량` / `전체 화면` / `해상도` / `수직 동기화` / `최대 프레임(0 = 제한 없음)` |

- 종류를 바꾸면 그 종류의 칸만 보인다(묶음 필드 안의 `[ShowIf]`, 21장 5절).
- **칸 이름**은 텍스트 표의 `<ID>.name`이다(`GameSettings.Label`). `MomoiProject → 다국어 → DB 이름 키 채우기`로 키를 만들고 원문 표에서 "브금 음량"처럼 고친다(23장 1절). 선택지의 `label`에 텍스트 키를 적으면 번역된 글이 보인다.
- 검증 오류(빌드를 막는다): "최솟값이 최댓값보다 작아야 합니다." / "단위는 0 이상이어야 합니다(0이면 연속)." / "선택지가 없습니다."(언어·해상도 제외) / "기본값 'x'이(가) 선택지에 없습니다." / "선택지 값이 겹칩니다." / "적용 대상 '브금 음량'에는 종류 '숫자(슬라이더)'만 맞습니다."(음량 = 숫자, 언어·해상도 = 선택지, 전체 화면·수직 동기화 = 켜기/끄기, 최대 프레임 = 숫자 또는 선택지)
- 검증 경고: "음량은 0~1 사이 값입니다."(음량의 최솟값·최댓값이 0~1 밖)

### 3-3. 선택 기능 '입력(키 바꾸기)' 켜기

1. Input System 패키지를 설치한다(Package Manager의 `com.unity.inputsystem`).
2. `MomoiProject → DB` → 사이드바 아래쪽 **입력(키 바꾸기)** 그룹 → **기능 활성화**를 켠다(심볼 `GBIBLE_INPUT`, `20-Setup.md` 6절).
3. 다시 컴파일이 끝나면 어셈블리 `GBible.Input`의 `GameInput`을 쓸 수 있다. 게임 코드를 자기 asmdef에 두었으면 References에 `GBible.Input`과 `Unity.InputSystem`을 더한다.

탭은 생기지 않는다(DB 종류가 없는 기능). 패키지가 없으면 켜도 이 기능의 코드가 컴파일되지 않는다(`GameInput`이 없다). 이 기능을 쓰는 게임 코드는 `#if GBIBLE_INPUT`으로 감싸면 껐을 때도 컴파일된다.

### 3-4. 모니터에서 확인하기

- 설정·바꾼 키는 플레이어 데이터가 아니라서 **데이터** 탭에 없다. 값은 `settings.json`을 열어 본다.
- 슬롯을 바꾸면 타임라인이 새 세션의 것으로 바뀐다(새 세션이 시작된다).

---

## 4. 코드로 쓰는 법 — 문법 요소를 하나씩

### 4-1. 슬롯 — `GBibleRuntime`

```csharp
var info = GBibleRuntime.SlotInfoOf("2");
if (!info.IsEmpty) label.text = $"{info.Summary} · {info.SavedAt:g}";
GBibleRuntime.UseSlot("2");                                   // 지금 슬롯을 저장하고 슬롯 2로 세션을 새로 시작
```

| 멤버 | 뜻 |
|---|---|
| `string Slot` | 지금 슬롯 이름(기본 `""`) |
| `void UseSlot(string slot)` | 슬롯을 바꾼다(2-2). `null`이면 기본 슬롯 |
| `SlotInfo SlotInfoOf(string slot)` | 그 슬롯의 요약. 저장이 없으면 `IsEmpty`가 참 |
| `IReadOnlyList<SlotInfo> Slots()` | 저장이 있는 슬롯들(기본 슬롯 제외, 이름순) |
| `void DeleteSlot(string slot)` | 그 슬롯의 저장을 지운다(2-2) |
| `void CopySlot(string from, string to)` | 슬롯을 통째로 복사한다(덮어씀, 2-2). `to`가 지금 슬롯이면 `InvalidOperationException` |
| `string SlotFolder(string slot)` | 그 슬롯의 폴더 경로 |
| `Func<GameSession, string> SlotSummary` | 저장할 때마다 `slot.json`에 남길 한 줄을 만드는 함수(기본 `null` = 빈 글자). 실패하면 타임라인 경고 |
| `double PlayTime` | 지금 슬롯의 누적 플레이 시간(초, 실제 시간) |
| `SavePath` · `RunSavePath` · `HasSavedRun` | 지금 슬롯의 프로필·판 파일 경로, 판 저장이 있는지(06장 2.1) |

| `SlotInfo` 필드 | 뜻 |
|---|---|
| `Name` | 슬롯 이름 |
| `HasProfile` · `HasRun` | `profile.json` · `run.json`이 있는지 |
| `IsEmpty` | 둘 다 없음 |
| `SavedAt` | 마지막 저장 시각(지역 시각). `slot.json`이 없으면 `default(DateTime)` |
| `PlayTime` | 누적 플레이 시간(초) |
| `Summary` | `SlotSummary`가 남긴 한 줄(없으면 빈 글자) |

`SlotSummary`는 런타임이 매 플레이 시작 전에 비우므로, `AfterAssembliesLoaded`에서 넣는다(5-1).

### 4-2. 설정 — `GameSettings`

```csharp
var speed = GameSettings.Number(GameIds.Setting.BattleSpeed);
GameSettings.Set(GameIds.Setting.Subtitles, false);          // 저장 + 적용 대상이면 적용 + Changed
GameSettings.Changed += id => { if (id == GameIds.Setting.BattleSpeed) … };
```

| 멤버 | 뜻 |
|---|---|
| `bool Toggle(string id)` | 켜기/끄기 값 |
| `double Number(string id)` | 숫자 값(선택지 값이 숫자 글자여도 읽는다) |
| `string Choice(string id)` | 선택지의 값 |
| `string Text(string id)` | 글 값 |
| `object Get(string id)` | 종류와 상관없이 지금 값 |
| `void Set(string id, object value)` | 바꾼다(2-3) |
| `void Reset(string id)` | 기본값으로 |
| `void ResetAll(string category = null)` | 그 분류(비우면 전부)를 기본값으로 |
| `IReadOnlyList<SettingData> All` | 설정 전부(DB 순서) |
| `SettingData Find(string id)` | 설정 하나(없으면 `null`) |
| `string Label(string id)` | 칸 이름: 텍스트 표의 `<ID>.name`(없으면 ID) |
| `IReadOnlyList<(string value, string label)> ChoicesOf(string id)` | 선택지(값 · 보일 글). 언어·해상도는 선택지를 비우면 저절로 채운다 |
| `event Action<string> Changed` | 값이 바뀐 설정의 ID |
| `string Raw(string key)` · `void SetRaw(string key, string value)` | 설정 파일의 원시 값(모듈·라이브러리의 기기별 값). `null`을 넣으면 지운다. 파일에는 키 앞에 `$`를 붙여 둔다(2-3) |
| `string FilePath` | `settings.json` 경로 |
| `void Load()` | 파일·DB를 읽고 적용한다. 런타임이 켤 때 부르고, 두 번째부터는 아무것도 하지 않는다 |

- 없는 ID면 `InvalidOperationException`("[MomoiProject] 없는 설정 'bgmVolum'입니다(DB › 설정)."). ID는 `GameIds.Setting` 상수로 쓴다.
- `Changed`는 매 플레이 시작 전에 비워진다. 받는 쪽은 `AfterAssembliesLoaded`·`Started`에서 연결한다(5-3).
- 원시 값은 파일 안에서 `$` 붙은 키(`$momoi.slot`)로 따로 저장되어 설정 ID와 겹치지 않는다. 라이브러리는 `momoi.slot`·`momoi.input`을 쓰므로, 프로젝트의 원시 값은 `mygame.*`처럼 다른 앞부분을 쓴다.

| `SettingData` 필드 | 뜻 |
|---|---|
| `Id` | 설정 ID |
| `category` | 분류 |
| `spec` (`SettingSpec`) | `kind` · `defaultToggle` · `defaultNumber` · `min` · `max` · `step` · `defaultChoice` · `choices`(`SettingChoice`: `value` · `label`) · `defaultText` |
| `target` (`SettingTarget`) | 적용 대상 |
| `static bool IsVolume(SettingTarget)` | 음량 대상인지 |

### 4-3. 입력 — `GameInput` · `RebindResult`

```csharp
GameInput.Use(actions);                                                // 게임이 켜질 때 한 번
var endTurn = GameInput.Find("Player/EndTurn");
label.text = GameInput.BindingText(endTurn);                           // "Space"
GameInput.Rebind(endTurn, 0, result => { if (result.Conflict != null) … });
```

| 멤버 | 뜻 |
|---|---|
| `void Use(InputActionAsset actions, bool enable = true)` | 동작 에셋을 쓴다. 저장된 바꾼 키를 덮어 씌우고 `enable`이면 켠다 |
| `InputActionAsset Actions` | `Use`로 넘긴 에셋(전이면 `null`) |
| `InputAction Find(string action)` | `"Player/Jump"` 또는 겹치지 않으면 `"Jump"`. 없으면 `InvalidOperationException`("[MomoiProject] 입력 동작 'Jump'이(가) 없습니다(GameInput.Use로 넘긴 .inputactions 확인).") |
| `string BindingText(InputAction action, int bindingIndex = -1)` | 키 표시 글자(`"Space"`, `"W/A/S/D"`). −1이면 그 동작의 모든 키 |
| `void Rebind(InputAction action, int bindingIndex, Action<RebindResult> done = null, bool allowDuplicates = false)` | 키 하나를 바꾼다(2-5). 번호가 범위 밖이면 `ArgumentOutOfRangeException` |
| `void CancelRebind()` | 기다리는 바꾸기를 취소한다(`Cancelled` 결과) |
| `bool IsRebinding` | 키를 기다리는 중인지 |
| `void ResetBindings(InputAction action = null)` | 바꾼 키를 기본으로(동작을 주면 그 동작만) |
| `event Action Changed` | 키가 바뀌었을 때(바꾸기·되돌리기). 설정 화면이 글자를 다시 그린다 |

| `RebindResult` 필드 | 뜻 |
|---|---|
| `Done` | 바뀌었다 |
| `Cancelled` | Esc로(또는 `CancelRebind`·다른 `Rebind`로) 취소했다 |
| `Conflict` (`InputAction`) | 같은 묶음의 이 동작과 겹쳐 바꾸지 않았다(아니면 `null`) |

---

## 5. 상황별 예제

예제는 가이드의 카드 던전 형식을 쓴다(`Tools/GuideExample`). 화면은 UI Toolkit(`UIDocument`)으로 그린다.

```csharp
// 플레이어 데이터 "run"(RunState, 세션 구역): floor · gold · hero · deck · battle …  IsActive = floor > 0(진행 중인 판)
// StartRun: 새 판을 연다. RunDirector: 입력을 행동으로 보내는 씬 스크립트.
```

### 5-1. 타이틀의 슬롯 3개

**요약 한 줄** — 저장할 때마다 `slot.json`에 남는다.

```csharp
using GBible;
using GBible.Ids;
using UnityEngine;

public static class SlotBoot
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void Hook() => GBibleRuntime.SlotSummary = session =>
    {
        var run = session.Game.Data().Run;
        return run.IsActive ? $"{run.floor}층 · 골드 {run.gold}" : "진행 중인 판 없음";
    };
}
```

**슬롯 화면**:

```csharp
using System;
using GBible;
using GBible.Ids;
using UnityEngine;
using UnityEngine.UIElements;

public class SlotScreen : MonoBehaviour
{
    static readonly string[] Names = { "1", "2", "3" };
    [SerializeField] UIDocument document;
    VisualElement _list;

    void OnEnable()
    {
        _list = document.rootVisualElement.Q("slots");
        Draw();
    }

    void Draw()
    {
        _list.Clear();
        foreach (var name in Names)
        {
            var info = GBibleRuntime.SlotInfoOf(name);
            var row = new VisualElement();
            row.Add(new Label(info.IsEmpty
                ? $"슬롯 {name} · 비어 있음"
                : $"슬롯 {name} · {info.Summary} · {TimeSpan.FromSeconds(info.PlayTime):h\\:mm} · {info.SavedAt:g}"));
            row.Add(new Button(() => Pick(name)) { text = info.HasRun ? "이어 하기" : "새 게임" });
            if (!info.IsEmpty) row.Add(new Button(() => Delete(name)) { text = "지우기" });
            _list.Add(row);
        }
    }

    void Pick(string name)
    {
        GBibleRuntime.UseSlot(name);                       // 지금 슬롯을 저장하고 이 슬롯으로 세션을 새로 시작(Started가 다시 온다)
        var session = GBibleRuntime.Session;
        if (!GBibleRuntime.LoadRun(session) || !session.Game.Data().Run.IsActive)
            session.Do(new StartRun());                     // 판이 없거나 끝난 판이면 새 판
    }

    void Delete(string name)
    {
        GBibleRuntime.DeleteSlot(name);                     // 지금 슬롯이면 저장하지 않고 세션을 새로 시작한다
        Draw();
    }
}
```

- 씬 흐름(32장)을 쓰면 `LoadRun`·`StartRun`이 `run.scene`을 정해 그 씬이 저절로 열린다. 쓰지 않으면 여기서 씬을 연다.
- 슬롯을 고른 뒤에는 게임 씬의 화면이 새 세션에서 시작하므로 이벤트 연결이 꼬이지 않는다(2-2). 게임 중에 "슬롯 바꾸기" 메뉴를 두려면 먼저 타이틀로 돌아온다.
- 게임을 켜면 마지막으로 고른 슬롯으로 세션이 시작되어 있다. 타이틀에서 그 슬롯을 표시하려면 `GBibleRuntime.Slot`을 본다.
- 자동 저장 슬롯을 따로 두려면: 체크포인트에서 `GBibleRuntime.CopySlot(GBibleRuntime.Slot, "auto")`(지금 슬롯이 원본이면 먼저 저장한다). 대상(`auto`)이 지금 슬롯이면 예외이므로, `auto`를 이어 할 때는 `UseSlot("auto")`로 바꾸고 거기서는 복사하지 않는다.

### 5-2. 설정 화면 만들기

DB › 설정(예):

| ID | `category` | `kind` | 기본값 · 범위 | `target` |
|---|---|---|---|---|
| `language` | 게임 | 선택지(선택지 비움) | — | 언어 |
| `battleSpeed` | 게임 | 숫자 | 1 · 1~3 · 단위 0.5 | 없음(5-3) |
| `screenShake` | 게임 | 켜기/끄기 | 켬 | 없음 |
| `bgmVolume` | 소리 | 숫자 | 0.8 · 0~1 · 단위 0.05 | 브금 음량 |
| `sfxVolume` | 소리 | 숫자 | 1 · 0~1 · 단위 0.05 | 효과음 음량 |
| `fullscreen` | 화면 | 켜기/끄기 | 켬 | 전체 화면 |
| `resolution` | 화면 | 선택지(선택지 비움) | — | 해상도 |
| `vsync` | 화면 | 켜기/끄기 | 켬 | 수직 동기화 |

**화면** — 분류마다 묶고, 종류마다 위젯 하나.

```csharp
using System.Collections.Generic;
using System.Linq;
using GBible;
using GBible.Data;
using UnityEngine;
using UnityEngine.UIElements;

public class SettingsScreen : MonoBehaviour
{
    [SerializeField] UIDocument document;
    VisualElement _list;
    readonly Dictionary<string, VisualElement> _widgets = new Dictionary<string, VisualElement>();

    void OnEnable()
    {
        _list = document.rootVisualElement.Q("settings");
        Build();
        GameSettings.Changed += Refresh;
        GameLanguage.Changed += Build;          // 언어를 바꾸면 칸 이름·선택지 글도 바뀐다
    }

    void OnDisable()
    {
        GameSettings.Changed -= Refresh;
        GameLanguage.Changed -= Build;
    }

    void Build()
    {
        _list.Clear();
        _widgets.Clear();
        foreach (var group in GameSettings.All.GroupBy(s => s.category))     // DB 순서대로, 분류마다
        {
            _list.Add(new Label(group.Key));
            foreach (var setting in group)
            {
                var widget = Widget(setting);
                _widgets[setting.Id] = widget;
                _list.Add(widget);
            }
            var category = group.Key;
            _list.Add(new Button(() => GameSettings.ResetAll(category)) { text = "기본값으로" });
        }
    }

    static VisualElement Widget(SettingData setting)
    {
        var id = setting.Id;
        var label = GameSettings.Label(id);                                   // 텍스트 표의 '<ID>.name'
        switch (setting.spec.kind)
        {
            case SettingKind.Toggle:
                var toggle = new Toggle(label) { value = GameSettings.Toggle(id) };
                toggle.RegisterValueChangedCallback(e => GameSettings.Set(id, e.newValue));
                return toggle;
            case SettingKind.Slider:
                var slider = new Slider(label, (float)setting.spec.min, (float)setting.spec.max) { value = (float)GameSettings.Number(id) };
                slider.RegisterValueChangedCallback(e => GameSettings.Set(id, e.newValue));     // 범위·단위는 Set이 맞춘다
                return slider;
            case SettingKind.Choice:
                var choices = GameSettings.ChoicesOf(id);
                var dropdown = new DropdownField(label, choices.Select(c => c.label).ToList(), IndexOf(choices, GameSettings.Choice(id)));
                dropdown.RegisterValueChangedCallback(_ => GameSettings.Set(id, choices[dropdown.index].value));
                return dropdown;
            default:
                var text = new TextField(label) { value = GameSettings.Text(id) };
                text.RegisterCallback<FocusOutEvent>(_ => GameSettings.Set(id, text.value));
                return text;
        }
    }

    // 값이 바뀌면(이 화면·기본값으로·다른 코드) 그 칸만 맞춘다. 고친 값(단위에 맞춘 슬라이더 값)이 보인다.
    void Refresh(string id)
    {
        if (!_widgets.TryGetValue(id, out var widget)) return;
        switch (widget)
        {
            case Toggle toggle: toggle.SetValueWithoutNotify(GameSettings.Toggle(id)); break;
            case Slider slider: slider.SetValueWithoutNotify((float)GameSettings.Number(id)); break;
            case DropdownField dropdown: dropdown.index = IndexOf(GameSettings.ChoicesOf(id), GameSettings.Choice(id)); break;
            case TextField text: text.SetValueWithoutNotify(GameSettings.Text(id)); break;
        }
    }

    static int IndexOf(IReadOnlyList<(string value, string label)> choices, string value) =>
        System.Math.Max(0, choices.ToList().FindIndex(c => c.value == value));
}
```

- 슬라이더를 움직이는 동안 `Set`이 여러 번 불려도 괜찮다. 같은 값이면 아무 일도 없고, 값마다 파일에 저장한다.
- 분류 이름(`group.Key`)은 DB에 적은 글자 그대로다. 번역하려면 분류에 텍스트 키를 적고 화면에서 `Texts.Get`으로 바꾼다.
- 적용 대상이 있는 칸(언어·음량·화면)은 이 코드만으로 바로 적용된다(5-4). 전투 속도처럼 게임 코드가 쓰는 칸은 5-3.

### 5-3. 전투 속도 — 게임 코드가 쓰는 설정

DB › 설정 `battleSpeed`: 분류 게임, 종류 숫자, 기본값 1, 최솟값 1, 최댓값 3, 단위 0.5, 적용 대상 `없음(게임 코드)`.

```csharp
using GBible;
using GBible.Ids;
using UnityEngine;

public static class BattleSpeedSetting
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void Hook() => GBibleRuntime.Started += _ =>
    {
        Apply(GameIds.Setting.BattleSpeed);                 // 켤 때 지금 값으로
        GameSettings.Changed -= Apply;                      // 슬롯을 바꿔 Started가 다시 와도 하나만
        GameSettings.Changed += Apply;
    };

    static void Apply(string id)
    {
        if (id == GameIds.Setting.BattleSpeed)
            GameView.PresentationSpeed = (float)GameSettings.Number(id);   // 연출 대기열의 속도(32장 2-2)
    }
}
```

- 연출 속도는 화면의 일이라 행동이 아니다. 같은 판을 다른 기기에서 이어 해도 그 기기의 속도로 보인다.
- `screenShake` 같은 켜기/끄기도 같다: 연출 코드가 `GameSettings.Toggle(GameIds.Setting.ScreenShake)`를 읽는다.

### 5-4. 언어·음량 — 적용 대상(코드 없음)

- `language`(선택지, 선택지 비움, 적용 대상 `언어`): 선택지가 지원 언어(`GameLanguage.Available`의 코드 · 그 언어의 이름)로 저절로 채워진다. 고르면 `GameLanguage.Set`이 불리고, 화면 글자(`LocalizedText`·`GameView.Redraw`)가 다시 그려진다(23장).
- `bgmVolume`(숫자 0~1, 적용 대상 `브금 음량`): 슬라이더를 움직이면 `GameAudio.SetVolume(AudioChannel.Bgm, 값, save: false)`가 불린다. 값은 설정 파일에 남고 다음에 켤 때 그 값으로 시작한다.
- `resolution`(선택지 비움, 적용 대상 `해상도`): 선택지가 기기의 해상도 목록("1920 × 1080")으로 채워진다. 에디터에서는 적용하지 않는다(빌드에서 확인).
- 같은 채널의 음량을 다른 곳(`GameAudio.SetVolume`)에서도 바꾸면 다음 실행 때 설정 값으로 돌아간다(2-4). 음량 슬라이더는 설정 화면 하나에만 둔다.

### 5-5. 키 바꾸기 화면

**켤 때 한 번** — 타이틀 씬의 오브젝트에 둔다.

```csharp
#if GBIBLE_INPUT
using GBible;
using UnityEngine;
using UnityEngine.InputSystem;

public class InputBoot : MonoBehaviour
{
    [SerializeField] InputActionAsset actions;     // 프로젝트의 .inputactions

    void Awake()
    {
        if (GameInput.Actions == null) GameInput.Use(actions);   // 저장해 둔 키를 덮어 씌우고 켠다
    }
}
#endif
```

**화면**:

```csharp
#if GBIBLE_INPUT
using GBible;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

public class KeyBindingScreen : MonoBehaviour
{
    [SerializeField] UIDocument document;
    VisualElement _list;
    Label _message;

    void OnEnable()
    {
        var root = document.rootVisualElement;
        _list = root.Q("bindings");
        _message = root.Q<Label>("bindings-message");
        root.Q<Button>("bindings-reset").clicked += () => GameInput.ResetBindings();   // 모두 기본 키로
        GameInput.Changed += Build;
        Build();
    }

    void OnDisable()
    {
        GameInput.Changed -= Build;
        GameInput.CancelRebind();            // 기다리던 바꾸기가 있으면 취소
    }

    void Build()
    {
        _list.Clear();
        foreach (var action in GameInput.Actions.FindActionMap("Player").actions)
            for (var i = 0; i < action.bindings.Count; i++)
            {
                var binding = action.bindings[i];
                if (binding.isComposite) continue;                         // WASD 묶음 자체는 못 바꾼다 → 안의 칸을 줄마다
                var index = i;
                var row = new VisualElement();
                row.Add(new Label(binding.isPartOfComposite ? $"{action.name} {binding.name}" : action.name));
                row.Add(new Button(() => Rebind(action, index)) { text = GameInput.BindingText(action, index) });
                _list.Add(row);
            }
    }

    void Rebind(InputAction action, int index)
    {
        _message.text = "바꿀 키를 누르세요(Esc: 취소)";
        GameInput.Rebind(action, index, result =>
        {
            if (result.Done) _message.text = "";                                          // Changed가 와서 목록을 다시 그린다
            else if (result.Conflict != null) _message.text = $"'{result.Conflict.name}'에서 이미 쓰는 키입니다.";
            else _message.text = "취소했습니다.";
        });
    }
}
#endif
```

**게임 쪽에서 읽기** — 입력은 Input System 그대로 읽고, 규칙은 행동으로.

```csharp
void Update()
{
    if (GameView.Busy) return;                                               // 연출·선택 대기 중에는 받지 않는다(32장)
    if (GameInput.Find("Player/EndTurn").WasPressedThisFrame()) director.EndTurn();   // → Game.Do(new EndTurn())
}
```

- 겹친 키는 바꾸지 않는다. 겹친 쪽을 먼저 다른 키로 바꾸거나, 같은 키를 두 동작에 쓰는 게임이면 `allowDuplicates: true`.
- 다른 묶음(예: `UI` 맵)의 키와는 겹쳐도 알리지 않는다(묶음마다 따로 쓰는 것이 보통이다).
- 바꾼 키는 슬롯과 상관없이 기기마다 하나다. 설정 화면의 "기본값으로"(`ResetAll`)는 키를 되돌리지 않는다 → `ResetBindings`.

---

## 6. 주의할 점 · 자주 하는 실수

| 증상 | 원인과 해결 |
|---|---|
| `ArgumentException`("슬롯 이름 '…'에는 글자·숫자·-·_만 쓸 수 있습니다.") | 이름에 `/`·공백·`.` 같은 글자가 있다. 글자·숫자·`-`·`_`만 쓴다. |
| 슬롯을 바꿨더니 판이 처음 상태 | `UseSlot`은 판을 불러오지 않는다. `GBibleRuntime.LoadRun`을 부른다(5-1). |
| 슬롯을 바꾼 뒤 연출·화면 일부가 반응하지 않음 | 게임 화면이 예전 세션에 직접 연결(`session.Events.Subscribe…`·`Ticked`)해 두었다. `UseSlot`은 타이틀처럼 게임 화면이 없을 때 부르고, 연결은 `Started`·`OnEnable`에서 다시 한다(2-2). `GameView.Redraw`·`ViewList`는 저절로 다시 붙는다. |
| 지운 슬롯이 `Slots()`에 안 보이는데 폴더는 남음 | 정상이다. `DeleteSlot`은 세 파일만 지운다. 폴더가 비면 목록에서 빠진다. |
| `CopySlot`이 "지금 쓰는 슬롯 '…'(으)로는 복사할 수 없습니다" | 대상이 지금 슬롯이다(실행 중인 세션이 저장할 때 다시 덮는다). 다른 슬롯으로 `UseSlot`한 뒤 복사하거나, 대상을 지금 슬롯이 아닌 곳으로 한다. |
| 슬롯 목록의 시각이 비어 있음(`0001-01-01`) | 그 슬롯을 저장한 적이 없다(`slot.json`이 없다). `IsEmpty`·`HasRun`으로 먼저 거른다. |
| 요약이 늘 빈 글자 | `SlotSummary`를 넣지 않았거나 `Awake`처럼 늦게 넣었다(플레이 시작 전에 비운다). `AfterAssembliesLoaded`에서 넣는다(5-1). 함수가 예외를 내면 타임라인 경고. |
| "[MomoiProject] 없는 설정 '…'입니다(DB › 설정)." | ID 오타이거나 저장 전 항목이다. `GameIds.Setting` 상수로 쓴다. |
| 설정을 바꿨는데 Undo하니 그대로 | 정상이다. 설정은 플레이어 데이터가 아니다(2-3). 판에 따라 달라야 하는 값은 플레이어 데이터로. |
| 음량을 바꿔도 다음 실행 때 돌아감 | 같은 채널을 `GameAudio.SetVolume`으로 바꿨는데 음량 설정이 따로 있다. 켤 때 설정 값이 적용된다. 음량은 `GameSettings.Set`으로만 바꾼다(2-4). |
| 전체 화면·해상도가 에디터에서 안 바뀜 | 빌드에서만 적용한다(에디터 창을 건드리지 않게). 빌드로 확인한다. |
| 슬라이더가 0.5 단위로 튐 | 단위(`step`)가 0.5다. 연속이면 0. 화면에는 `Changed`에서 고친 값을 다시 넣는다(5-2의 `Refresh`). |
| 선택지 설정이 늘 첫 선택지로 돌아감 | 저장된 값이 선택지에 없다(선택지 값을 바꿨다). 기본값으로 돌아간다. |
| 검증 오류 "적용 대상 '언어'에는 종류 '선택지'만 맞습니다." | 적용 대상과 종류가 맞지 않는다(3-2). 언어·해상도 = 선택지, 음량 = 숫자, 전체 화면·수직 동기화 = 켜기/끄기. |
| `GameSettings.Changed`가 두 번씩 옴 | `Started`에서 `+=`만 했다. 슬롯을 바꾸면 `Started`가 다시 온다. `-=` 뒤 `+=`(5-3). |
| "기본값으로"를 눌렀는데 키가 그대로 | `ResetAll`은 설정만 되돌린다. 키는 `GameInput.ResetBindings()`. |
| `GameInput`이 없다는 컴파일 오류 | 선택 기능 '입력(키 바꾸기)'이 꺼져 있거나 Input System 패키지가 없다(3-3). 이 기능을 쓰는 코드는 `#if GBIBLE_INPUT`으로 감싼다. |
| `GameInput.Find`가 "입력 동작 '…'이(가) 없습니다" | `Use` 전에 불렀거나 이름이 틀렸다. `"맵/동작"`으로 쓴다. |
| WASD를 누르자 "묶음 키(WASD 같은 composite)는 그 안의 칸 하나씩 바꿉니다" | composite 자체의 번호를 넘겼다. 그 다음 번호(Up·Down …)를 넘긴다(5-5). |
| Esc로 바꿀 수 없음 | Esc는 바꾸기 취소 키다. |
| 키를 바꿨는데 "이미 쓰는 키" | 같은 묶음의 다른 바인딩(같은 동작의 다른 키 포함)과 겹친다. 겹친 쪽을 먼저 바꾸거나 `allowDuplicates: true`. |
| 키 바꾸기 중에 게임 입력이 들어감 | 바꾸는 동안에는 그 동작만 꺼진다. 다른 동작은 `GameInput.IsRebinding`으로 막는다. |
