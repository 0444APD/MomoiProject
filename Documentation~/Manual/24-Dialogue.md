# 24. 다이얼로그 시스템 (선택 모듈)

대사·선택지·조건 분기를 노드 그래프로 만들고, 배경·스탠딩·카메라·음악 같은 연출을 신호 체인으로 잇는다. 게임 코드는 `Dialogues.Play("다이얼로그 ID")` 한 줄로 연다. 모듈 위치는 `Optional/Dialogue`(어셈블리 `GBible.Dialogue` · `GBible.Dialogue.Editor`, 네임스페이스 `GBible.Dialogue`)이고, 코어는 이 모듈을 참조하지 않는다.

## 이 문서에서 쓰는 말

| 말 | 뜻 |
|---|---|
| 다이얼로그 | 노드 그래프 하나(`DialogueData`). 대사 한 줄 = 노드 하나. |
| 흐름(선) | 흰 선, 둥근 점. 다이얼로그가 어디로 가는가(시작 → 대사 → 선택지 → 분기 …). 나가는 흐름 점 하나는 한 곳으로만 간다. |
| 신호(선) | 분홍 선, 네모 점. 연출이 **언제** 시작하는가. 신호 점 하나는 여러 곳으로 갈 수 있다. |
| 효과 | 연출 노드. **상태 효과**는 유지되는 화면 모습(배경·스탠딩·카메라 …)을 바꾸고, **큐 효과**는 한 번 일어나고 끝난다(흔들기·효과음 …). |
| 체인 | 효과의 시작·진행 지점(n%)·완료 신호에 다른 효과를 이어 단 것. |
| 연출 프리셋 | 효과 묶음을 따로 만든 그래프(`EffectPresetData`). 다이얼로그 그래프에서는 노드 하나로 쓴다. |
| 스탠딩 프로필 | 다이얼로그 전용 화자 정보(스탠딩 그림 목록·이름 색 …, `StandingProfileData`). 게임의 캐릭터 데이터와 따로 관리한다. |
| 장소 | 배경 이미지 + 이름(`LocationData`). |
| 컨텍스트 | 조건·결과가 실행되는 순간의 정보(세션, 트리거 순간 등). |

---

## 1. 켜기와 준비

**한 줄 요약.** 도감 사이드바의 다이얼로그 시스템 › **기능 활성화**로 켜고, UI 템플릿을 한 번 만든다.

**언제.** 대사가 있는 게임에서 처음 한 번.

### 에디터에서

1. `MomoiProject → 도감` → 사이드바 "다이얼로그 시스템" 그룹의 **기능 활성화** → **켜기**. 다시 컴파일되면 **스탠딩 프로필**·**장소** 탭과 메뉴 `MomoiProject → 다이얼로그 시스템 → …`가 열린다(스크립팅 심볼 `GBIBLE_DIALOGUE`, `20-Setup.md` 6절).
2. `MomoiProject → 다이얼로그 시스템 → UI 템플릿 만들기`. `Assets/MomoiProjectData/DialogueUI/`에 모든 기능이 연결된 화면 한 벌(프리팹 `DialogueSystem`, UXML·USS·선택지 UXML, 패널 설정·런타임 테마, 기본 UI 모션, 흰 스프라이트)과 설정 `Resources/MomoiProject/DialogueSettings`를 만든다. 설정의 **playerPrefab**이 이 프리팹으로 정해진다.
3. 도감 › **스탠딩 프로필**에서 화자를 만든다.
   - **expressions**: 스탠딩 목록(**스탠딩 ID** · **스프라이트**, 기본 `default` 하나).
   - **defaultExpression**: 기본 스탠딩 ID(비우면 첫 스탠딩).
   - **nameColor**: 이름 색. **standingPrefab**: 스파인 등 `StandingView`가 붙은 프리팹(비우면 스프라이트로 그린다). **standingScale**: 크기 배율. **logIcon**: 대사 로그 얼굴(정사각형 권장, 스파인이면 꼭 넣는다). **extras**: 추가 글(키 · 글).
   - 화면에 보일 이름은 텍스트 `<프로필 ID>.name`(`23-Text-Localization.md`).
4. 도감 › **장소**: **background**(배경 이미지), **extras**(추가 글). 이름은 텍스트 `<장소 ID>.name`.

### 주의할 점

- 다이얼로그(`DialogueData`)와 연출 프리셋은 도감 사이드바에 없다. 그래프 창에서 만들고 고친다.
- 한글이 네모로 보이면 UI 오브젝트의 `DialogueUI › font`에 한글 TTF를 넣는다.

---

## 2. 그래프 편집기

**한 줄 요약.** `MomoiProject → 다이얼로그 시스템 → 그래프`(창 이름 "다이얼로그 그래프")에서 격자 배경에 노드를 놓고 점을 끌어 잇는다. 편집은 에셋에 바로 저장되고 Unity Undo(Ctrl+Z)가 된다.

### 에디터에서

**왼쪽 목록** — 도감과 같은 폴더 목록. 맨 위 "다이얼로그" 폴더 줄의 **+ 새로**(새 다이얼로그) · **+ 폴더**. 줄을 누르면 오른쪽에 연다. 끌어서 다른 폴더로 옮긴다(이 창의 폴더 변경은 바로 저장된다).

**새 다이얼로그** — "새 다이얼로그" 창의 **id**(lowerCamelCase, 예: `shopIntro`) → **만들기**. 시작 노드와 "첫 대사" 대사 노드가 이어진 채로 만들어진다.

**머리** — 다이얼로그 ID, 경고(노란 "⚠ …": 시작 노드가 없음, 시작 ID 중복, 대사 키 중복, "흐름이 들어오지 않는 노드 n개", "빈 대사 n개", "대사 문법 오류: …"), **프로젝트에서 보기**.

**미리보기 막대** — 4절.

**캔버스 조작**

| 조작 | 하는 일 |
|---|---|
| 우클릭 | 노드 만들기(검색 가능한 목록, "분류 › 이름") |
| 점을 끌어 다른 점에 놓기 | 잇기(흐름 점은 흐름 점과만, 신호 점은 신호 점과만, 나가는 점 → 들어오는 점) |
| 점을 끌어 빈 곳에 놓기 | 노드를 만들면서 바로 잇기 |
| 빈 곳 끌기 | 범위 선택(Shift로 더하기) |
| 가운데 버튼 · Alt · Space + 끌기 | 화면 이동 |
| 휠 / **－ ＋** | 확대·축소 |
| **전체 보기** / F | 모든 노드가 보이게 |
| Delete · Backspace | 선택한 노드·선 지우기 |
| Ctrl+C / Ctrl+V | 복사·붙여넣기(다른 다이얼로그·프리셋 창으로도. 그 그래프에서 못 쓰는 노드는 건너뛴다) |
| Ctrl+D | 선과 함께 복제 |
| Ctrl+F | 노드 찾기(종류·요약·대사 글) |
| 노드 우클릭 | 접기/펼치기, 복제, 노드에 이어진 선 모두 지우기, 지우기 |
| 선 우클릭 | 선 지우기 |

노드의 값은 노드 안의 칸에서 고친다(노드 우클릭 › 접기/펼치기). 도감과 같이 **칸 이름표는 코드의 변수 이름**(`speaker`, `text` …)이고, 마우스를 올리면 설명 글이 보인다. 아래에는 `변수 이름`(설명)으로 적는다.

### 흐름 노드

| 노드(분류 › 이름) | 점 | 하는 일 |
|---|---|---|
| 흐름 › **시작** | 신호 "시작 신호", 흐름 "다음" | 들어오는 곳. `entryId`(시작 ID, 한 다이얼로그 안에서 겹치지 않게, 기본 `start`). |
| 흐름 › **대사** | 흐름 입력, 신호 입력 "대사 시작", 신호 "진입"·"타이핑 완료"·"넘김", 흐름 "다음" 또는 선택지마다 하나 | 대사 한 줄(아래). |
| 흐름 › **조건 분기** | 흐름 "참" · "거짓" | `condition`(도전과제와 같은 조건 블록). 조건이 비면 참. |
| 흐름 › **값 바꾸기** | 흐름 "다음" | `rewards`(바꿀 값: 결과 블록 목록, 플레이어 데이터에 주기 …). |
| 흐름 › **신호 보내기** | 흐름 "다음" | `signal`(신호 이름). 게임 코드에 보낸다(상점 열기, 퀘스트 시작). |
| 흐름 › **다른 다이얼로그로** | (끝) | `dialogue`(비우면 이 다이얼로그) · `entryId`로 이어 간다. |
| 흐름 › **끝** | — | 다이얼로그 끝. 흐름이 이어지지 않은 점도 끝으로 친다. |
| 정리 › **메모** · **그룹 상자** | — | 실행에 영향 없음. 메모 `text`, 그룹 상자 `title`·`color`(머리를 끌면 안의 노드도 함께, 오른쪽 아래로 크기). |

**대사 노드의 칸**: `speaker`(화자 = 스탠딩 프로필, 비우면 해설) · `speakerName`(다른 이름: ???·병사 A, 프로필 없이 이름만 둘 때도) · `thought`(생각, 속마음) · `highlight`(화자 강조: 자동 / 그대로) · `expression`(화자 스탠딩 ID, 드롭다운, 비우면 그대로) · `expressionSprite`(화자 예외 스프라이트) · `text`(대사 원문) · `voice`(목소리, 비우면 이전 목소리를 멈춘다) · `options`(선택지: `text` 선택지 글 · `condition` 보일 조건(비우면 항상) · `showWhenLocked` 조건이 안 맞아도 흐리게 보여 준다) · `typingSpeed`(초당 글자, 0이면 기본) · `advance`(넘기는 방식: 클릭 / 시간 진행(클릭 무시) / 시간 진행(클릭 가능)) · `holdSeconds`(시간 진행: 글이 다 나온 뒤 머무는 시간) · `extras`(추가 글) · `comment`(메모, 번역 시트의 메모 칸으로).

대사·선택지에는 번역 키(`line.xxxxxxxxxx`)가 자동으로 붙는다. 바꾸지 않는다. 원문은 문장 틀 문법을 그대로 쓴다: `{playerGold}`(플레이어 데이터 ID), `{item:을}`, `[[burn]]`.

### 효과 노드

모든 효과의 공통 칸: `delay`(시작 전 기다림, 초) · `duration`(진행 시간, 초) · `ease`(움직임 곡선: 일정하게 · 천천히 시작 · 천천히 끝 · 천천히 시작·끝 · 살짝 넘쳤다 돌아옴 · 튕김) · `marks`(진행 신호 지점: `percent`를 더하면 그 점이 생긴다) · `keepAcrossLines`(다음 대사로 넘어가도 계속) · `blockInput`(진행 중 클릭 막기 = 효과 시간 보장, 기본 켬, 배경 음악·효과음은 끔). 점: 신호 입력 "재생", 신호 "시작" · "n%" · "완료".

| 분류 | 효과(메뉴 이름) · 고유 칸 | 종류 |
|---|---|---|
| 화면 | **배경**: `location`(장소) · `sprite`(예외 배경 이미지) · `viaBlack`(검은 화면을 거쳐 바꾼다) · `blackHold`(검은 화면 유지 시간) / **배경 어둡게**: `amount` / **레터박스**: `thickness` / **카메라**: `zoomPercent`(100 = 원래 크기) · `offset` | 상태 |
| 화면 | **번쩍임**: `color` / **UI 모션**: `element`(비우면 다이얼로그 상자) · `motion` · `useMotionLength` | 큐 |
| 소리 | **배경 음악**: `clip`(비우면 멈춤) · `volume` | 상태 |
| 소리 | **효과음**: `clip` · `volume` | 큐 |
| 스탠딩 | **스탠딩 등장**: `character` · `expression` · `sprite` · `slot`(자리) · `x`(직접 지정 위치) · `highlighted` · `frame`·`frameMask`(창 모드) / **스탠딩 퇴장**: `character` / **스탠딩 바꾸기**: `character` · `expression` · `sprite` / **스탠딩 이동**: `character` · `slot` · `x` / **강조**: `character` · `on` · `onlyThis`(나머지는 강조 끄기) / **창 바꾸기**: `character` · `frame` · `frameMask` | 상태 |
| 이미지 | **이미지 표시**: `slot`(자리 이름) · `sprite` · `screenPosition` · `size` / **이미지 지우기**: `slot`(비우면 모든 이미지) | 상태 |
| 움직임 | **흔들기**: `shakeCharacter`(끄면 화면 전체) · `character` · `strength` / **이펙트 생성**: `prefab` · `screenPosition` · `character`(붙일 캐릭터) | 큐 |
| 흐름 제어 | **기다리기** | 큐 |
| 프리셋 | **연출 프리셋**: `preset` · `character`(대상 캐릭터) | — |

`character`(스탠딩 프로필) 칸을 비우면 **현재 화자**를 가리킨다(프리셋 안에서는 프리셋 노드의 대상 캐릭터).

### 상황별 예제 — 그래프 짜기

**연출이 끝난 뒤 말하기(예전 Prefix 잠금).** 대사의 "진입" → 배경(진행 신호 지점 90%) → 그 "90%" 점을 대사의 "대사 시작"에 잇는다. 배경이 90% 진행될 때까지 글자가 나오지 않는다.

**체인.** 배경 "완료" → 스탠딩 등장 "재생", 스탠딩 등장 "완료" → 흔들기 "재생"(시작 전 기다림 0.3초).

**선택지에 조건.** 상점 대사의 선택지 "비싼 검 사기"에 보일 조건 "플레이어 데이터 검사(골드 ≥ 1000)" + 흐리게 보여 주기 → 골드가 모자라면 흐리게 보이고 고를 수 없다.

**값 바꾸기 + 신호.** 선택지 "받는다" → 값 바꾸기(플레이어 데이터에 주기: 골드 +100) → 신호 보내기 `openShop` → 대사.

### 주의할 점

- 상태 효과는 **시작하는 순간 논리 상태를 끝 모습으로** 바꾸고, 화면은 앞뒤 모습 사이를 그린다. 그래서 클릭·스킵·되감기·저장이 모두 같은 결과 화면을 만든다.
- 노드를 복사하면 대사 키가 바뀐다. 그래도 겹치면 경고 "대사 키 '…'가 겹칩니다".
- 대사 없이 도는 흐름 고리는 일정 단계 뒤 끊고 "[다이얼로그] 흐름이 너무 길게 이어집니다" 경고를 낸다.

---

## 3. 연출 프리셋

**한 줄 요약.** 자주 쓰는 연출 묶음을 별도 창에서 만들고, 다이얼로그 그래프에서는 '연출 프리셋' 노드 하나로 쓴다.

### 에디터에서

1. `MomoiProject → 다이얼로그 시스템 → 연출 프리셋`(창 이름 "연출 프리셋"). 새 프리셋은 **프리셋 시작** 노드 하나로 시작한다.
2. 프리셋 시작의 "시작" 신호에 효과들을 잇는다.
3. 프리셋 › **신호 내보내기**(`signal` = 신호 이름) 노드를 두면, 다이얼로그 그래프의 프리셋 노드에 그 이름의 신호 점이 생긴다.
4. 다이얼로그 그래프에서 프리셋 › **연출 프리셋** 노드 → `preset`에서 프리셋 고르기. 점: "재생", "시작", 내보낸 신호들, "완료"(안에서 시작한 효과가 모두 끝났을 때).
5. 미리보기는 **▶ 프리셋 재생**.

### 상황별 예제

**깜짝 놀람.** 프리셋 `surprise`: 시작 → 스탠딩 등장(0.2초) → 완료 → 흔들기(캐릭터) → 흔들기 "시작" → 신호 내보내기 `hit`. 다이얼로그: 대사 "진입" → 연출 프리셋(`surprise`, 대상 비움 = 현재 화자) → `hit` → 대사 "대사 시작".

### 주의할 점

- 경고: "'프리셋 시작'에 이어진 효과가 없습니다", "내보내는 신호 '…'가 n곳에 있습니다(한 이름은 한 곳에서)".

---

## 4. 미리보기

**한 줄 요약.** 그래프 창 위 막대에서 플레이 모드로 실제 화면을 띄워 재생·시간 이동한다.

### 에디터에서

막대: **▶ 선택한 노드부터**(노드를 고르지 않으면 시작 노드부터, 효과 노드를 고르면 그 효과 체인만) · **⏸ / ▶**(일시정지/계속) · 시간 막대 · 상태 글("1.25초 · 타이핑", 연출 중 · 클릭 대기 · 선택지 · 끝 · 클릭 막힘 · 일시정지) · **다음 ▷**(클릭한 것처럼) · **자동 / 자동 ✓** · **선택지까지 ⏭** · **■**(정지).

- 플레이 중이 아니면 "플레이 모드에서 재생합니다". 씬의 `DialoguePlayer`로 재생하고, 없으면 UI 템플릿 프리팹을 잠시 꺼내 쓴다(이름 뒤 "(미리보기)").
- 시간 막대를 끌면 일시정지하고 그 시점으로 간다: 처음부터 다시 시작해 그 시점까지 클릭 없이 감는다. 트윈으로 그리는 효과는 정확한 진행도로, 스파인·파티클은 자기 시간으로 처음부터 다시 돈다. 감는 동안 '클릭' 대사는 `DialoguePlayer.previewClickDelay`초 뒤 넘어간 것으로 치고, 선택지 앞에서 멈춘다.

---

## 5. 코드로 쓰는 법

### 5.1 다이얼로그 관리자 — `Dialogues`

어디서든 부르는 정적 입구. 씬에 `DialoguePlayer`가 있으면 그것을, 없으면 설정의 프리팹을 만들어 씬이 바뀌어도 남겨 둔다. 설정의 **createOnStart**(기본 켬)면 게임 시작 때 미리 만들어 숨겨 두어 첫 다이얼로그도 바로 뜬다.

**`Dialogues.Play(다이얼로그 ID, 시작 ID = "start")`** — 열기. 성공하면 `true`.

```csharp
using GBible.Dialogue;

Dialogues.Play("shopIntro");
Dialogues.Play("shopIntro", "afterBuy");
```

**`Dialogues.IsPlaying`**, **`Dialogues.Advance()`**(클릭), **`Dialogues.Choose(번호)`**(선택지, `Choices` 순서), **`Dialogues.SkipToChoice()`**, **`Dialogues.Stop()`**.

**`Dialogues.Started`**(`Action<string>`, 다이얼로그 ID) · **`Ended`** · **`Signaled`**(`Action<string>`, 신호 이름) — 받는다.

```csharp
Dialogues.Signaled += signal =>
{
    if (signal == "openShop") UnityEngine.Debug.Log("상점 열기");
};
```

**`Dialogues.ResumeSaved()`** — 판을 불러온 뒤 저장된 다이얼로그 진행이 있으면 그 대사부터 이어 간다(5.4).

**`Dialogues.Player`** — 지금 쓰는 `DialoguePlayer`(없으면 만든다, 못 만들면 `null`).

### 5.2 재생기 컴포넌트 — `DialoguePlayer`

프리팹 구성: `DialoguePlayer`(실행기·입력·게임 시간 멈춤·신호) › `Stage`(`DialogueStage`: 배경·어둡게·스탠딩·이펙트·카메라·음악) · `UI`(`UIDocument` + `DialogueUI`).

인스펙터 칸: **typingSpeed**(초당 글자, 기본 30) · **pauseGame**(다이얼로그 중 `GameTime.Pause("다이얼로그")`, 기본 켬) · **onStarted** · **onSignal**(`UnityEvent<string>`) · **onEnded** · **saveProgress**(진행을 판 저장에 남김, 기본 켬) · **autoMode** · **autoDelay**(기본 1.2초) · **previewClickDelay**.

```csharp
using GBible.Dialogue;
using UnityEngine;

public sealed class NpcTalk : MonoBehaviour
{
    [SerializeField] DialoguePlayer player;
    [SerializeField] string dialogueId = "villagerHello";

    public void Talk()
    {
        if (!player.IsPlaying) player.Play(dialogueId);
    }
}
```

멤버: `Play`, `Advance`, `Choose`, `SkipToChoice`, `FastForward`(진행 중 연출을 끝 모습으로 + 글 다 보이기), `Stop`, `ResumeSaved`, `Paused`(시간 멈춤), `IsPlaying`, `Runner`(아래 실행기), `Signaled` 이벤트.

입력: 클릭·Space·Enter·숫자 1~9는 `DialogueUI`가 받는다. 게임패드 등 다른 입력은 `Advance`/`Choose`를 직접 부른다.

### 5.3 실행기 — `DialogueRunner`

Unity 컴포넌트 없이 도는 실행기(테스트·도구·직접 만든 화면용). 게임 쪽은 `Tick`(프레임 시간), `Advance`(클릭), `Choose`(선택)만 부른다.

```csharp
using GBible;
using GBible.Data;
using GBible.Dialogue;

var runner = DialogueRunner.FromDatabase(GameDatabase.Load());
runner.Session = GBibleRuntime.Session;    // 조건·값 바꾸기·문장 틀의 {플레이어 데이터}
runner.Stage = null;                       // IDialogueStage 구현(화면). null이면 시간만 흐른다.
runner.Signaled += signal => UnityEngine.Debug.Log(signal);
runner.Start("shopIntro");
runner.Tick(0.016f);
runner.Advance();
```

| 멤버 | 뜻 |
|---|---|
| `Start(id[, entry])` / `Start(DialogueData[, entry])` | 시작 |
| `Phase` | `Idle` · `WaitingForText`(연출 중) · `Typing` · `Shown`(클릭 대기) · `Choosing` · `Ended` |
| `Line` · `Text` · `VisibleCharacters` · `Choices` | 지금 대사·글·보이는 글자 수·선택지(`DialogueChoice`: Index·OptionId·Text·Enabled) |
| `Advance()` · `Choose(i)` · `FastForward()` · `SkipToChoice()` · `Stop()` | 입력 |
| `AutoMode` · `AutoDelay` · `ClickCooldown`(기본 0.12초) · `DefaultTypingSpeed` · `AutoHighlightSpeaker` | 동작 설정 |
| `InputBlocked` · `IsActive` · `RunningEffects` · `State`(`StageState`) | 상태 |
| `History` · `Log` | 지나온 줄(그때 화면 상태 포함) · 대사 로그 |
| `Snapshot()` / `Resume(snapshot)` · `Progress` / `Restore(progress)` | 중간 저장·되감기 / 진행 기록 저장·이어 가기 |
| `LineEntered` · `Signaled` · `Ended` · `EffectStarted` · `ProgressChanged` | 받는 이벤트 |
| `SpeakerNameOf(line)` · `TextArguments()` | 화면에 보일 화자 이름 · 문장 틀에 넘기는 값(모든 플레이어 데이터) |

### 5.4 저장과 이어 가기

진행 기록(시작 위치, 분기·선택 순서, 지난 대사 수)이 플레이어 데이터 `dialogue.progress`(세션 구역)에 남아 판 저장(`run.json`)에 함께 들어간다. 판을 불러온 뒤 `Dialogues.ResumeSaved()`를 부르면 같은 길을 조용히(값 바꾸기·신호 없이) 다시 밟아 저장한 대사부터 이어 가고, 화면은 지나온 연출의 끝 모습으로 맞춰진다.

```csharp
using GBible;
using GBible.Dialogue;

public static class ContinueGame
{
    public static void Continue()
    {
        if (GBibleRuntime.LoadRun(GBibleRuntime.Session)) Dialogues.ResumeSaved();
    }
}
```

### 5.5 코드 없이 열기 — 결과 블록 "다이얼로그 재생"

도전과제·능력 등의 결과(코딩한 결과 목록)와 대사 그래프의 '값 바꾸기'에 **다이얼로그 재생**(분류 "다이얼로그 시스템") 블록이 있다. 칸: `dialogue`(다이얼로그) · `entryId`(시작 ID). 예: 도전과제 달성 연출. 코드 형식은 `PlayDialogueReward`.

### 5.6 프로젝트 전용 노드

**흐름 노드 — `CustomFlowNode`**: `Run`이 돌려준 점 ID로 흐름이 이어진다(`null`이면 끝). `[GraphNode("이름", Category = …)]`를 붙이면 우클릭 메뉴에 나온다.

```csharp
using System;
using GBible.Dialogue;

[Serializable, GraphNode("골드 많으면 통과", Category = "흐름")]
public sealed class RichCheckNode : CustomFlowNode
{
    public double threshold = 1000;

    public override string Run(DialogueRunner runner)
    {
        var gold = runner.Session?.Data.GetNumber("playerGold") ?? 0;
        return gold >= threshold ? NextPort : null;   // 모자라면 다이얼로그 끝
    }
}
```

**효과 노드 — `EffectNode`**: 화면 그리기는 `CreatePlayer`에서 `IEffectPlayer`(`Evaluate(t)`·`Finish()`)를 만들고, 유지되는 값은 `IsState`를 참으로 하고 `ApplyTo`에서 `state.Custom("키", true)`에 적는다(스킵·저장·되감기에 따라간다). 메뉴의 그래프 종류는 `Graphs = GraphKinds.Both`(다이얼로그·프리셋 둘 다).

```csharp
using System;
using GBible.Dialogue;

[Serializable, GraphNode("날씨", Category = "화면", Graphs = GraphKinds.Both)]
public sealed class WeatherEffect : EffectNode
{
    public string weather = "rain";
    public override bool IsState => true;
    public override string Summary => weather;

    public override void ApplyTo(StageState state, EffectContext context) =>
        state.Custom("weather", true).text = weather;

    public override IEffectPlayer CreatePlayer(DialogueStage stage, EffectContext context, StageState before, StageState after) =>
        new Player(stage);

    sealed class Player : IEffectPlayer
    {
        readonly DialogueStage _stage;
        public Player(DialogueStage stage) => _stage = stage;
        public void Evaluate(float t) { /* t = 0~1(곡선 적용) 로 비·눈 파티클을 서서히 */ }
        public void Finish() { }
    }
}
```

### 5.7 UI 확장

- **글 더하기(코드 없음)**: 프로필·장소·대사 노드의 추가 글에 키(예: `affiliation`)와 글을 적고, UXML에 `name="dialogue-extra-<키>"` Label을 둔다. 글이 비면 그 요소에 `dialogue-extra--empty`, 있으면 루트에 `dialogue--has-<키>` 클래스가 붙는다. 번역은 `<프로필 ID>.<키>`·`<대사 키>.<키>` 텍스트.
- **동작 더하기**: `DialogueUIExtension`(또는 `IDialogueUIExtension`)을 상속한 컴포넌트를 UI 오브젝트(또는 자식)에 붙이면 열림·닫힘·대사·선택지 순간에 불린다. 요소는 `ui.Element("이름")`.

```csharp
using GBible.Dialogue;
using UnityEngine.UIElements;

public sealed class AffinityGauge : DialogueUIExtension
{
    public override void OnLine(DialogueUI ui, DialogueLineInfo line)
    {
        if (ui.Element("affinity") is Label label)
            label.text = string.IsNullOrEmpty(line.Speaker) ? "" : line.Speaker;
    }
}
```

- **모양**: UXML·USS를 고친다. 코드는 요소를 **이름**으로만 찾는다. 필수: `dialogue-root`·`dialogue-name`·`dialogue-text`·`dialogue-choices`, 나머지(`dialogue-box`, `dialogue-next`, `dialogue-letterbox-top/bottom`, `dialogue-flash`, `dialogue-fade`, `dialogue-tooltip`, `dialogue-log` …)는 없으면 그 기능만 꺼진다. 인스펙터 아래 '요소 연결 확인'이 이름 규격의 요소가 있는지 보여 준다. 상태 클래스: `dialogue--open`·`--staging`·`--typing`·`--waiting`·`--choosing`·`--narration`·`--thought`·`--speaker-<ID>`·`--has-<키>`·`--log-open`.
- **UI 모션**: `Create → MomoiProject → 다이얼로그 시스템 → UI 모션`. `DialogueUI`의 모션 칸(순간 × 요소 이름 × 모션)이나 그래프의 'UI 모션' 효과로 재생한다.

---

## 6. 동작 규칙 (클릭·시간·오디오)

- 한 번 클릭하면 한 단위 진행: 연출 대기 중이면 막지 않는 효과를 끝 모습으로 보내고 대사를 시작, 타이핑 중이면 글을 다 보이고, 다 나왔으면 다음 대사로.
- **진행 중 클릭 막기**가 켜진 효과가 도는 동안(시작 전 기다림 포함) 클릭·선택은 무시된다. 클릭을 받은 뒤 0.12초 동안은 다음 클릭을 무시한다(연타 방지).
- '시간 진행(클릭 무시)' 대사는 클릭을 받지 않고, 머무는 시간은 막는 효과가 끝난 뒤부터 잰다.
- 자동 진행: '클릭' 대사가 다 나오고 막는 효과가 없으면 autoDelay 뒤 클릭한 것으로 친다. 목소리가 나오는 동안은 기다린다.
- 다음 줄로 넘어갈 때 이 줄에서 시작한 효과는 끝 모습으로 마무리한다(**다음 대사로 넘어가도 계속** 효과 제외).
- 화자 자동 강조: 화자가 나와 있으면 화자만 밝게, 없거나 해설이면 모두 원래대로. 대사 노드에서 '그대로'를 고르면 그 줄은 건드리지 않는다.
- 오디오: 배경 음악 → 브금 채널(다이얼로그가 끝나면 다이얼로그 전 브금으로, 브금을 정하지 않은 다이얼로그는 건드리지 않는다), 효과음 → 효과음 채널, 목소리 → 목소리 채널(`26-Audio.md`).
- 대사 로그: 로그 버튼 또는 글상자 위에서 휠을 위로. 보는 동안 다이얼로그 시간이 멈춘다.

## 7. 시트

`MomoiProject → 다국어 → 시트 내보내기 (CSV)`에 다이얼로그 대본이 재생 순서대로(분기·선택지 모두) 함께 나간다: 키, 문맥 열 `dialogue`·`location`(그때 장소 이름)·`speaker`(화자 이름, "(생각)", 해설, 선택지는 "▶ 선택지"), 원문, 번역 칸, 메모. 가져올 때 대사의 원문 칸은 읽지 않는다(원문은 그래프가 기준). 시트 규격은 아직 임시다.

## 8. 주의할 점 / 자주 하는 실수

- **세션 없이 재생**: `DialogueRunner`를 직접 만들 때 `Session`을 비우면 조건 분기·선택지 조건은 항상 참, 값 바꾸기는 하지 않는다. `DialoguePlayer`는 재생할 때 `GBibleRuntime.Session`을 넣는다.
- **값 바꾸기는 행동 하나다**: 대사의 '값 바꾸기'는 `DialogueSetAction` 행동으로 실행되어 Undo 한 단위가 된다(행동 탭에 "다이얼로그 값 바꾸기 (다이얼로그 ID)"). 이미 행동 안에서 연 다이얼로그(도전과제 보상 등)면 그 행동 안에서 바로 바뀐다.
- **씬에 재생기도 설정도 없음**: "[다이얼로그] 다이얼로그 재생기가 없습니다. 씬에 다이얼로그 UI 프리팹을 두거나 'MomoiProject › 다이얼로그 시스템 › UI 템플릿 만들기'로 설정을 만드세요." → UI 템플릿을 만든다.
- **시작 ID가 없음**: "[다이얼로그] '…'에 시작 ID '…'가 없습니다." → 시작 노드의 시작 ID를 확인한다.
- **문장 틀의 값 이름**: 대사 원문의 `{…}`에는 플레이어 데이터 **ID**를 쓴다(예: `{playerGold}`). 다른 값은 넘어오지 않는다.
- **스파인 스탠딩**: 예외 스프라이트는 스파인 스탠딩에 쓸 수 없고, 로그 얼굴은 프로필의 logIcon을 넣어야 보인다.
