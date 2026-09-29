# Game Systems

팀 공용 게임 시스템 라이브러리. 어느 Unity 프로젝트에 넣어도 독립적으로 동작하고, 요소 추가·조건·보상은 전용 에디터에서 메뉴 선택과 숫자 입력만으로 만든다.

설계와 결정 사항은 [`Documentation~/Architecture.md`](Documentation~/Architecture.md)에 있다. 구현이 문서와 달라지면 문서를 먼저 고친다.

---

## 요구 사항

- Unity 6 (6000.6.3f1에서 검증)
- 외부 에셋·패키지 의존 없음. Odin Inspector, DOTween을 쓰지 않는다.

## 설치

이 저장소를 대상 프로젝트의 `Assets` 아래 원하는 위치에 둔다(복사 또는 `git clone`). 폴더 위치에 의존하지 않는다.

## 구조

| 폴더 | 어셈블리 | 내용 |
|---|---|---|
| `Runtime/Core` | `GameSystems.Core` | 게임 로직. **UnityEngine을 쓰지 않는다**(.NET 서버에서도 그대로 동작). |
| `Runtime/Unity` | `GameSystems.Unity` | 데이터 에셋(`GameData`), 런타임 시작, 저장 |
| `Editor` | `GameSystems.Editor` | 디자인 시스템(UI Toolkit), 데이터베이스·모니터·UI 샘플 창, 검증기 |
| `Samples` | `GameSystems.Samples` | 샘플. `GAME_SYSTEMS_SAMPLES` 심볼이 있을 때만 컴파일 |
| `Tests` | `GameSystems.Tests.*` | EditMode·PlayMode 테스트 |

## 에디터 메뉴

| 메뉴 | 용도 |
|---|---|
| **Game Systems → 도감** | `GameData`를 상속한 모든 타입을 시트·카드(세로·가로·바)로 보고 추가·검색·수정. 조건·보상은 블록 편집기. |
| **Game Systems → 모니터** | 플레이 중 통합 기록(인과 추적), 상태, 도전과제 진행도, 수치, 이벤트 발생기, 행동 로그(Undo), 디버그 명령 |
| **Game Systems → 상세** | 도감 항목의 상세 속성 창(도킹 가능). 위쪽에 크롬식 탭이 쌓이고, 클래스·배열·리스트를 누르면 오른쪽에 안쪽 탭이 열린다. 값은 바로 반영, Unity 적용은 도감 탭을 옮길 때. |
| **Game Systems → 키워드 연결** | 왼쪽 필드 · 오른쪽 키워드 그룹을 함께 보며 연결. 새 그룹·키워드를 만들어 바로 연결(필드 규칙 자동 생성). string 필드는 하나, string 배열·리스트은 여러 개. |
| **Game Systems → UI 샘플** | 디자인 시스템의 모든 부품. 새 도구는 이 부품을 조립해 만든다. |
| Game Systems → 샘플 → 샘플 데이터 만들기 | 샘플 수치·도전과제 생성(샘플 심볼 필요) |
| Game Systems → 샘플 → 필드 시험 항목 만들기 | 필드 종류·개수가 많은 항목(긴 글·빈 칸 포함)을 만들고 상세 패널을 연다. 입력 칸·스크롤·배치 편집 점검용(자동 점검: `WideDetailTests`). |
| **Game Systems → 대화 그래프** | 격자 배경에서 우클릭으로 노드를 만들고 점을 끌어 잇는다. 대사 한 줄 = 노드 하나. 흐름(흰 선)과 신호(분홍 선: 효과의 시작·n%·완료로 다음 효과를 잇는 체인). 자세히: `Documentation~/Dialogue.md` |
| **Game Systems → 연출 프리셋** | 효과 노드로 연출 묶음을 만든다. 대화 그래프에서는 노드 하나로 쓰고, 프리셋이 내보낸 신호가 그 노드의 점이 된다. |
| **Game Systems → 다국어** | 설정 열기(지원 언어·대체 순서·언어별 표), 시트 내보내기·가져오기(CSV, 머리줄 이름으로 읽음), 번역 검사(문법·누락·값·용어·태그 차이), 도감 이름 키 채우기. 자세히: `Documentation~/Localization.md` |
| Game Systems → 샘플 → 샘플 텍스트 만들기 | 한국어·영어 표와 조사·복수형·용어 예시 문장 |

시트와 카드는 읽기 전용이다. 카드를 클릭하거나 시트 행을 더블클릭하면 **상세 창**에 탭으로 열리고, 고친 값은 바로 작업본에 반영된다(바뀐 칸은 새 값만 보인다). 도감의 모든 변경(추가·수정·삭제·ID 바꾸기·순서)은 바로 Unity 에셋에 쓰지 않고 모아 두었다가, **탭을 옮길 때** 이 탭의 변경 목록(전후 값)을 보여 주고 **적용** 또는 **되돌리기**한다. 플레이 시작·창 닫기·Unity 종료 때도 묻고, 스크립트 재컴파일에도 유지된다(예외: 프리팹 컴포넌트 수정·스프라이트 베이크는 즉시 저장). ID를 가리키는 칸은 직접 입력하지 않고 목록에서 고른다. 모든 목록은 같은 디자인의 드롭다운이며, 항목이 많으면 검색창이 생긴다(한글 초성 검색 가능: `ㅊㅊ` → 처치 수).

**키워드 탭**은 그룹과 키워드를 한 화면에서 다룬다. `+ 새 그룹`(키워드 여러 개를 한 줄씩 함께 입력), 그룹 카드의 `+ 키워드`, 손잡이(⠿)로 그룹 순서 끌기, 키워드 칩을 끌어 순서 바꾸기·다른 그룹으로 옮기기. 그룹 이름·칩을 누르면 상세 화면.

모니터에서 값을 바꾸는 방법은 두 가지다.

- **직접 수정**: 상태 탭의 값 칸, 수치 탭 작업 패널의 '직접 수정', 도전과제 작업 패널의 '강제 달성'. 값만 덮어쓰고 이벤트·수치 규칙은 반응하지 않는다.
- **이벤트로 반영**: 수치의 '이벤트로 더하기/기록', 도전과제의 '이벤트로 달성', 이벤트 탭의 '이벤트 발생'. 게임에서 그 값을 얻은 것처럼 실제 이벤트를 발생시켜 다른 수치·도전과제·보상까지 연쇄 반응하고, 행동으로 기록되어 Undo할 수 있다.

도전과제·수치 탭은 항목을 클릭해 선택하면, 이벤트 탭은 이벤트를 고르면 오른쪽 아래 작업 패널에 조작 버튼이 뜬다.

## 핵심 개념

```
행동(GameAction) → 이벤트(Events) → 수치(Stats) → 조건 평가 → 도전과제 달성 → 보상
```

- **행동은 데이터다.** 모든 플레이어 행동은 `session.Do(행동)`을 거친다. 행동 하나가 Undo 단위다.
- **모든 게임 상태는 상태 저장소(`State`)에 둔다.** 세션 구역(게임 내용, 되감김)과 메타 구역(도전과제·누적 수치, 되감기지 않음)으로 나뉜다.
- **값의 규칙은 하나.** 행동은 실행되는 즉시 끝나고, 이미 한 행동은 언제든 뒤에서부터 `Undo()`로 되돌린다(확정 단계 없음). 실시간 게임은 행동별 기록 대신 일정 간격 스냅샷으로 같은 `Undo()`가 되감는다. 도전과제 달성과 보상은 되돌리지 않는다(행동이 바꾼 값만 돌아가 반복 획득 불가). 읽기 `Get`, 교체 `Set`, 부분 수정 `Modify` — 모든 타입이 같은 문법.
- **식별자는 한 곳에서 짓는다.** 플레이어 데이터·키워드·수치·도전과제의 ID는 도감 창에서만 짓고, 에디터에서는 목록에서 고르고, 코드에서는 자동 생성되는 `GameIds`(예: `GameIds.PlayerData.PlayerInventory` → `StateKey<Inventory>`)로 쓴다. ID를 바꾸면 참조·에셋·생성 코드·저장 파일 호환이 함께 처리된다.
- **저장은 두 가지.** 프로필(메타 구역: 도전과제·누적 수치 등)은 `profile.json`, 진행 중인 판(세션 구역·난수·시간)은 `run.json`. 종료 시 자동 저장, 판 이어하기는 `GameSystemsRuntime.LoadRun`.
- **게임 시간 조절.** `GameTime.Pause/Resume`(이유별로 겹침), `GameTime.SetSpeed`(슬로우·배속), `GameTime.StepFrame`(개발용 1프레임 진행). `Time.timeScale`을 바꾸므로 물리·애니메이션·파티클·게임 시계가 모두 따라간다. 멈추면 안 되는 UI 연출만 unscaled 시간을 쓴다. 모니터 상단에서도 조절할 수 있다.
- **필드 경로와 조건 조립.** 이벤트·저장소 값의 필드를 깊이와 상관없이 폴더형 탐색기로 고르고(`items[].tags`), 형식에 맞는 비교(숫자: 이상·이하…, 글자·선택지: 일치·아님, 목록: 포함·개수, 참/거짓)만 목록에 나온다. 수치 규칙의 "조건"(예: 태그 boss 포함일 때만 반영)과 조건 블록 "상태 값 검사"(예: 인벤토리 › 아이템의 각 항목 › 태그에 legendary 포함)에 쓴다. 조건 묶음은 접고 펼치며, 줄마다 위·아래·복제·바꾸기·삭제.
- **데이터 보기(그리드 + 블록).** 한 객체를 가로 12칸 그리드에 블록을 끼워 그린다. 기본: 왼쪽 스프라이트 나열(2개씩) + 오른쪽 데이터 나열. 블록: 스프라이트 나열, 데이터 나열, 특정 스프라이트, 필드 카드, 클래스 카드(안쪽 필드 골라 표시). 타입별 배치는 "배치 편집"에서 바꿔 `ProjectSettings/GameSystems/Layouts.json`에 저장되고, 데이터베이스 상세 패널·모니터 탐색기 어디서 보든 같은 배치로 보인다. 프로젝트 전용 블록은 `GsLayoutBlock`을 상속하고 `[LayoutBlock("id", "이름")]`만 붙이면 목록에 나타난다.- **도감.** 미리 정의된 모든 것(아이템·몬스터 같은 데이터 항목, 템플릿, 프리팹)을 `Game Systems > 도감`에서 추가·확인·변경·삭제한다. 항목은 타입별 배치로 그려지고, 클래스·목록 필드는 탐색기로 들어가 고친다(조건·보상 같은 블록 트리는 문장 편집기). **템플릿**은 개발자가 만든 아무 클래스의 미리 채운 인스턴스(`TemplateData.CreateInstance<T>()`로 사본), **프리팹**은 연결한 프리팹의 스크립트 컴포넌트 필드를 탐색기로 고친다(에셋에 바로 저장, Undo 가능). 프리팹 항목은 도감에서 지우지 않고 '프로젝트에서 보기'로 이동해 프로젝트 창에서 지우며, 프리팹을 지우면 항목도 자동으로 정리된다. 탐색기의 딕셔너리는 한 요소를 한 줄(`[키] 값`)로 보이고 키를 더하거나 뺄 수 있다.
- **모니터의 데이터 탭.** 현재 세션·플레이어의 모든 값을 탐색기로 본다: 판 데이터(세션 구역) · 프로필 데이터(메타 구역) · 수치 · 도전과제 달성. 고치면 Set·Modify를 거쳐 기록되고 Undo할 수 있다. 타임라인·행동·이벤트 같은 기능 화면은 따로 두되, 그 안의 이벤트·행동·클래스 값을 누르면 탐색기로 연다. 이벤트 발생기도 실제 이벤트 인스턴스를 탐색기로 채운다.
- **조건식.** 필드 검사 · && AND · || OR · ! NOT · 배열·리스트 항목 검사(any/all/none/count)를 자유롭게 중첩한다(중첩 = 괄호). 편집 화면은 코드처럼 `[처치 수] [>=] [10]`으로 보이고, 마우스를 올리면 "처치 수가 10 이상이라면" 같은 설명이 뜬다. 도감 요약과 로그는 기본이 자연어("등급이 3 이상 그리고 (태그에 boss 포함 또는 …)", "인벤토리 › 아이템 #12 › 레벨 3 → 4")이고, **Alt를 누르고 있으면** 코드 형태(`등급 >= 3 && (…)`, `인벤토리.items[#12].level`)로 바뀐다(마우스를 올리면 다른 쪽이 툴팁으로). 수치 규칙의 조건과 "상태 값 검사" 조건 블록에 쓴다.
- **키워드와 필드 규칙.** 분류·등급·속성·태그 같은 "고르는 값"은 도감의 키워드(그룹 + 키워드)로 정의한다(코드는 `GameIds.Keyword.그룹.키워드`). 코드에는 빈 `string`(하나) 또는 `string` 배열·리스트(여러 개) 필드만 두고, 도감의 **필드 규칙**에서 "이 클래스의 이 필드는 어떤 키워드 그룹/어떤 도감 종류/플레이어 데이터에서 고른다, 필수다"를 연결한다(어트리뷰트 불필요, `[DataRef]`와 같은 대우). 모든 편집 칸·탐색기·조건식 값·ID 바꾸기·검증이 규칙을 따르고, 필수 값이 비면 도감은 검증 오류, 게임 중 저장소 값은 경고 로그 + 모니터 데이터 탭 맨 위 "필수 값 비어 있음"에 채워질 때까지 남는다.
- **스프라이트 규격.** 도감에 규격 묶음(예: 아이템 이미지)과 규격(아이콘 128×128, 카드 256×384 …)을 정의하고, 필드 규칙으로 스프라이트 필드에 묶음과 미리보기 규격을 연결한다. 그 필드에 원본 일러를 넣으면 맞춤 창이 떠서 한 번 중심(클릭)·배율(휠)을 잡으면 묶음의 규격이 모두 베이크된다(GPU, 원본 읽기 권한 불필요, `Sprites/묶음/규격/원본_규격.png`). 특정 규격만 따로 잡는 예외 촬영은 영구 기록되고, 규격 크기를 바꾸면 자동으로 다시 베이크하며 예외는 '확인 필요'로 표시된다. 목록·카드·데이터 보기는 미리보기 규격으로 보인다. 게임 코드: `SpriteSpecs.Get(원본, GameIds.SpritePreset.ItemIcon)`.
- **다국어 텍스트.** 어디서든 `Texts.Get("키", ("값", 3))`으로 현재 언어의 최종 글자를 받는다. 문장 틀은 ICU MessageFormat(복수·선택·숫자·날짜) + 한국어 조사 `{item:을}` + 용어 `[[burn:이]]`(키워드의 스타일·아이콘으로 꾸밈). 언어는 `GameLanguage.Set("en")`, 화면 글자는 `LocalizedText`. 자세히: `Documentation~/Localization.md`
- 자세한 사용법: `Documentation~/StateStore.md`
- **모든 변화는 통합 기록(Journal)에 원인과 함께 남는다.** 모니터에서 보상 한 줄을 선택하면 그 원인 행동까지 거슬러 올라간다.
- 싱글 게임은 `GameSystemsRuntime.Session` 하나를 쓴다. 멀티플레이·테스트는 `new GameSession(...)`으로 독립 세션을 만든다.

## 사용법

### 1. 이벤트와 행동 정의(코드)

```csharp
[GameEvent("적 처치")]
public sealed class EnemyKilled
{
    public int Count = 1;
    public int Combo;
}

[System.Serializable]
public sealed class AttackAction : GameAction
{
    public string target;
    public override string Describe() => $"{target} 공격";
}
```

### 2. 행동 처리기 등록

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
static void Hook()
{
    // 되돌리기 방식: 기본은 행동 단위. 실시간 게임은 스냅샷(options.History = HistoryMode.Snapshots).
    GameSystemsRuntime.Configuring += options => options.History = HistoryMode.Actions;

    GameSystemsRuntime.Started += session =>
    {
        session.State.Define("battle:combo", StateZone.Session, 0, "현재 콤보");
        session.Actions.Register<AttackAction>(action =>
        {
            var hit = session.Random.Get("combat").Chance(0.7);   // 용도별 시드 난수
            if (hit) session.Events.Publish(new EnemyKilled { Count = 1 });
        });
    };
}
```

```csharp
GameSystemsRuntime.Session.Do(new AttackAction { target = "고블린" });
GameSystemsRuntime.Session.Undo();     // 마지막 행동 Undo(몇 번이든)
```

### 3. 데이터(에디터, 코드 없음)

도감 창에서:

1. **수치** 추가 → 범위(이번 판/누적)·집계 방식 선택 → 반영 규칙: 이벤트 `[적 처치 ▾]`, 반영할 값 `[Count ▾]`
2. **도전과제** 추가 → 달성 조건 `+ 조건 선택 → 수치 비교` → 문장 `[처치 수] [100] [이상]`의 단어를 하나씩 클릭해 고른다 → 보상 `+ 보상 추가 → 획득` → `[골드] [500] 획득`

상세 패널 위쪽에 "조건 처치 수 100 이상 달성 시 / 보상 골드 500 획득" 두 줄 요약이 즉시 표시되고, 문제가 있으면 빨간 글씨로 알린다. 검증 오류가 있으면 빌드가 막힌다.

## 확장

| 추가할 것 | 방법 | 도구에 자동 반영 |
|---|---|---|
| 새 데이터 타입(아이템, 캐릭터 …) | `GameData` 상속 클래스 작성. 필드만 쓰면 된다. 선택: `[Database("아이템", Group = "인벤토리")]` | 도감 창 목록, 시트·카드, 검색, 검증 |
| 새 조건 블록 | `Condition` 상속 + `[Serializable]` + `[BlockInfo("이름", Category = "분류")]`. `Evaluate`, `CollectDependencies`, `Describe` 구현 | "+ 조건 선택" 메뉴, 미리보기, 계약 테스트 |
| 새 보상 블록 | `Reward` 상속 + 위와 같은 어트리뷰트. `Grant`, `Describe`, `Effect` 구현 | "+ 보상 추가" 메뉴, 계약 테스트 |
| 새 이벤트 | `[GameEvent("표시 이름")]` 클래스 | 수치 규칙의 이벤트·필드 선택 목록 |
| 새 화면 배치 블록 | `GsLayoutBlock` 상속 + `[LayoutBlock("id", "이름", Description = "설명")]` | 배치 편집의 블록 목록, 도움말 항목 |
| 도움말·설정 단계 | `IHelpProvider`(도움말 항목), `ISetupProvider`(시작하기 단계) 구현 클래스 작성 | 도움말 창, `?` 버튼, 시작하기 탭 |

- 필드 이름을 한국어로 보이려면 `[FieldLabel("이름")]`.
- 다른 항목의 ID를 담는 문자열 필드는 `[StatRef]`, `[AchievementRef]`를 붙인다 → 직접 입력 대신 목록 선택 + 존재 여부 검증.
- **블록 계약**: 조건은 의존하는 관찰 대상(`CollectDependencies`)이나 확인 주기(`PollInterval`) 중 하나를 반드시 선언한다. 둘 다 없으면 계약 테스트가 실패한다.

카드·시트에 어떤 필드를 보여줄지는 코드 없이 도감 창의 **보기 설정**에서 고른다(비워 두면 자동 추론). 설정은 `ProjectSettings/GameSystems/DatabaseViews.json`에 저장되어 팀과 공유된다.

## 규칙

- `Runtime/Core`에서 UnityEngine을 쓰지 않는다. Unity 전용 어트리뷰트는 `#if UNITY_2020_1_OR_NEWER`로 감싼다.
- 게임 로직에서 `UnityEngine.Random`·`System.Random`·`Time`을 쓰지 않는다 → `session.Random.Get("용도")`, `session.Clock`.
- 게임 상태를 static 변수나 필드에 두지 않는다 → `session.State` (Undo·되감기·저장·모니터에서 빠지지 않게).
- 새 시스템은 관찰 계약(`IInspectable`)을 구현한다.
- (보류 중 — 도움말·시작하기는 가장 마지막에 다시 켠다) **새 기능에는 도움말을 함께 넣는다.** 도감 종류·모니터 탭은 직접 쓴 도움말 항목(`Editor/Help/BuiltInHelp.cs` 또는 기능별 `IHelpProvider`), 블록은 `Description`이 필수다. 사용자가 설정해야 하는 기능이면 시작하기 단계(`ISetupProvider`)도 추가한다. 빠지면 `HelpCoverageTests`가 실패한다.
- 도움말 문체: 매뉴얼보다 약간 친절하게, 비유 없이. 첫 문장에 핵심, 이어서 사용법·주의점. 빈 줄로 문단, `## `로 소제목, `- `로 목록.

## 검증

테스트 프로젝트(`UnitySystemProject`)의 `Tools` 폴더:

| 스크립트 | 내용 |
|---|---|
| `Verify.ps1` | 코어를 Unity 없이 컴파일 + Unity 배치모드 EditMode·PlayMode 테스트 전부 |
| `VerifyServerCore.ps1` | `Runtime/Core`가 UnityEngine 없이(.NET 서버 조건) 경고 없이 컴파일되는지 |
| `CompileOffline.ps1` | 에디터를 연 채로 라이브러리 전체 어셈블리 컴파일 검사(테스트 실행은 안 함) |

Unity 에디터가 프로젝트를 열고 있으면 배치모드 검증(Verify.ps1)은 실행되지 않는다. 그때는 CompileOffline.ps1로 컴파일만 확인하고, 테스트는 에디터의 Test Runner로 돌린다.

## 이전 코드

Odin·DOTween 기반의 이전 시스템(Events, SaveManager, Logs, Achievements, TextTable, UIDragController, Inventory, Dialogue)은 `legacy` 브랜치에 보존되어 있다. 새 구조로 옮길 때 참고한다.
