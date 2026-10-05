# Momoi Project

팀 공용 게임 시스템 라이브러리. 어느 Unity 프로젝트에 넣어도 독립적으로 동작하고, 요소 추가·조건·보상은 전용 에디터에서 메뉴 선택과 숫자 입력만으로 만든다.

**기능별 사용법과 코드 예제는 [`Documentation~/Manual/00-Index.md`](Documentation~/Manual/00-Index.md)(매뉴얼)부터 본다.** **프로젝트를 진행하는 순서와 판단 기준은 [`Documentation~/Guide/00-Index.md`](Documentation~/Guide/00-Index.md)(프로그래머 가이드).** 설계와 결정 사항은 [`Documentation~/Architecture.md`](Documentation~/Architecture.md)에 있다. 구현이 문서와 달라지면 문서를 먼저 고친다.

**처음 이어받는 사람은 [`HANDOVER.md`](HANDOVER.md)부터 읽는다**: 모든 기능 설명, 설계 철학, 작업 규칙, 개발 흐름, 진행 중·앞으로의 안건, 미해결·보류 사항.

---

## 요구 사항

- Unity 6 (6000.6.3f1에서 검증)
- 외부 에셋·패키지 의존 없음. Odin Inspector, DOTween을 쓰지 않는다.

## 설치

이 저장소를 대상 프로젝트의 `Assets` 아래 원하는 위치에 둔다(복사 또는 `git clone`). 폴더 위치에 의존하지 않는다.

## 구조

| 폴더 | 어셈블리 | 내용 |
|---|---|---|
| `Runtime/Core` | `GBible.Core` | 게임 로직. **UnityEngine을 쓰지 않는다**(.NET 서버에서도 그대로 동작). |
| `Runtime/Unity` | `GBible.Unity` | 데이터 에셋(`GameData`), 런타임 시작, 저장 |
| `Editor` | `GBible.Editor` | 디자인 시스템(UI Toolkit), 데이터베이스·모니터·에셋 관리 창, 테스트 씬 도구, 검증기 |
| `Tests` | `GBible.Tests.*` | EditMode·PlayMode 테스트 |
| `Optional/Dialogue` | `GBible.Dialogue` · `.Editor` · `.Tests` | 선택 모듈: 다이얼로그 그래프·연출·UI. 쓰지 않으면 폴더째 지운다. |
| `Optional/Inventory` | `GBible.Inventory` | 선택 모듈: 인벤토리(칸의 리스트). |
| `Optional/Input` | `GBible.Input` | 선택 모듈: 키 바꾸기·저장(Input System 패키지 필요). |
| `Optional/Steam` | `GBible.Steam` | 선택 모듈: Steam 연동(Steamworks.NET 패키지 필요). |

## 에디터 메뉴

| 메뉴 | 용도 |
|---|---|
| **MomoiProject → DB** | 선언부 5개 탭(키워드 · 클래스 · 로그 · 이벤트 · 플레이어 데이터)과 그 밖의 DB 종류(도전과제·인스턴스·프리팹·스프라이트 규격 …), 선택 기능의 탭을 한 창에서 다룬다. 모든 탭이 클래스 탭과 같은 폴더 목록(이름순, 끌어 놓아 옮기기)이고 추가·검색·수정한다. 조건·보상은 블록 편집기. |
| **MomoiProject → 모니터** | 플레이 중 통합 기록(인과 추적), 데이터, 도전과제 진행도, 수치, 이벤트 호출, 행동 로그(Undo), 디버그 명령 |
| **MomoiProject → 다이얼로그 시스템 → 그래프** | (선택 기능, DB 사이드바의 다이얼로그 시스템 › 기능 활성화로 켠다) 격자 배경에서 우클릭으로 노드를 만들고 점을 끌어 잇는다. 대사 한 줄 = 노드 하나. 흐름(흰 선)과 신호(분홍 선: 효과의 시작·n%·완료로 다음 효과를 잇는 체인). 자세히: `Documentation~/Dialogue.md` |
| **MomoiProject → 다이얼로그 시스템 → 연출 프리셋** | 효과 노드로 연출 그룹을 만든다. 다이얼로그 그래프에서는 노드 하나로 쓰고, 프리셋이 내보낸 신호가 그 노드의 점이 된다. |
| **MomoiProject → 다이얼로그 시스템 → UI 템플릿 만들기** | 모든 기능이 연결된 다이얼로그 화면 한 벌(프리팹·UXML·USS·모션·패널 설정). UI Toolkit, 요소는 이름 규격으로 연결(인스펙터에서 확인), 애니메이션은 USS 상태 클래스 + UI 모션 에셋. 스탠딩은 스프라이트·스파인. |
| **MomoiProject → 오디오** | 채널(전체·브금·목소리·효과음·UI) 음량, 클립 미리 듣기(채널별 방식 그대로), 지금 나는 소리, 오디오 설정. 오디오 클립 우클릭 › MomoiProject › 오디오 재생. 코드: `GameAudio.PlayBgm/PlayVoice/PlaySfx/PlayUi`. 자세히: `Documentation~/Audio.md` |
| **MomoiProject → 다국어** | 설정 열기(지원 언어·대체 순서·언어별 표), 시트 내보내기·가져오기(CSV, 머리줄 이름으로 읽음), 번역 검사(문법·누락·값·용어·태그 차이), DB 이름 키 채우기. 자세히: `Documentation~/Localization.md` |
| MomoiProject → 내 코드 범위 | 클래스를 고르는 목록(클래스 탭·값 형식·인스턴스·이벤트)에 들어갈 스크립트 확인. 기본: 이 프로젝트에서 만든 데이터 클래스만(Unity·패키지·라이브러리·Plugins·기능 스크립트 제외). 필요할 때만 폴더·네임스페이스를 더한다(ProjectSettings/MomoiProject/ProjectCode.json). |
| MomoiProject → 식별자 코드 다시 만들기 · 스프라이트 규격 다시 베이크 · 출시됨 | 도구 |

실전에 쓰는 기능만 `MomoiProject` 메뉴에 두고, 점검·예시용은 `Tests` 메뉴에 둔다. 상세 창과 키워드 연결은 메뉴가 없다: 상세 창은 DB에서 항목이나 클래스를 열 때만 탭으로 뜨고, 키워드 연결은 코드의 `[Keyword(typeof(그룹))]`다(클래스 상세에서 보기만).

**DB의 구성(2026-09-30 개편).**

- **선언부 5개 탭**: 키워드 · 클래스 · 로그 · 이벤트 · 플레이어 데이터.
- **클래스 탭**(읽기 전용): 프로젝트가 코드로 정의한 클래스(아이템·유닛 …)와 DB 종류를 `Assets/Code` 폴더별로 본다. 클래스는 코드에서 만들고 고친다(평범한 `[Serializable]` 클래스, `List`·`Dictionary` 그대로). 다시 컴파일되면 DB에 그대로 보인다(Unity가 컴파일 때 만들어 두는 형식 목록을 읽을 뿐이라 감시 비용이 없다).
- **목록 모양(모든 DB 탭 공통, `CodexTree`)**: 폴더(중첩) 안에 항목이 한 줄씩, 한 폴더 안에서 폴더 먼저 그다음 항목, 각각 이름순. 폴더 줄은 버튼 빼고 어디를 눌러도 접고 펼치며(마우스를 올린 줄만 밝아지고 `+`·`↗`·`⋯` 버튼이 보인다), 항목 줄은 누르면 상세가 열린다. 항목·폴더를 끌어 다른 폴더 위에 놓으면 그 안으로 옮긴다. 폴더는 클래스·이벤트 탭만 실제 디스크 폴더이고, 키워드·스프라이트 규격 탭은 그룹(그룹 안에 그룹), 나머지 탭은 정리용 폴더다(`ProjectSettings/MomoiProject/CodexFolders.json`).
- **이벤트 탭**: `[GameEvent]` 클래스를 같은 방식으로 본다·만든다.
- **클래스 상세**(클래스를 누르면 상세 창의 탭으로 열린다): 필드마다 이름·형식·연결된 키워드 그룹, 형식이 다른 클래스면 `→ 클래스`로 이동, `cs 열기`. 키워드 그룹·`[Hide]`·`[ShowIf]`는 코드에서 정한 것을 보여 준다(보기만). 에디터 설정인 스프라이트 규격 그룹·미리보기 규격·필수 여부도 여기서.
- **선택 기능**(다이얼로그 시스템 등): 사이드바에 기능 이름의 그룹(다이얼로그 시스템 · 인벤토리 시스템)이 있고 첫 줄이 **기능 활성화** 토글 버튼이다(오른쪽 끝에 스위치). 켜야 그 기능의 탭(스탠딩 프로필·장소 …)이 열리고, 기능의 클래스가 클래스 탭의 기능 이름 그룹에 자동으로 들어온다(잠김: 필드는 못 고치고 연결만 정한다). 끄면 모두 빠지고 빌드에도 들어가지 않으며, 만든 데이터는 남는다. 상태는 `ProjectSettings/MomoiProject/Features.json`, 스크립팅 심볼(`GBIBLE_DIALOGUE`)은 그 파일에 맞춰 자동으로 들어간다.
- **스프라이트 규격**도 클래스의 하나지만 어느 프로젝트나 쓰는 기본 클래스라 잠겨 있다(지울 수 없음). 그룹은 이름만으로 만들고, 규격은 ID와 크기(x·y, 왼쪽 체크박스로 종횡비 고정)로 만든다. 베이크는 클래스 상세에서 규격 그룹을 연결한 스프라이트 필드에 원본을 넣는 순간 맞춤 창이 열려 이루어진다.
- 용어: '묶음'이라는 말은 쓰지 않는다(그룹이면 그룹, 컬렉션이면 그 형식 이름). '대화'는 '다이얼로그'(기능 이름은 '다이얼로그 시스템').

목록은 읽기 전용이다. 항목 줄을 누르면 **상세 창**에 탭으로 열리고, 고친 값은 바로 작업본에 반영된다(바뀐 칸은 새 값만 보인다). DB의 모든 변경(추가·수정·삭제·ID 바꾸기·폴더 옮기기)은 바로 Unity 에셋에 쓰지 않고 모아 두었다가, **Ctrl+S**(DB·상세 창에서) 또는 사이드바의 **저장** 버튼으로 한 번에 저장한다. 탭을 옮겨도 묻지 않고 그대로 남으며, 사이드바의 "저장 안 된 변경 n건"을 누르면 목록(전후 값)을 보고 저장하거나 되돌릴 수 있다. DB 창 닫기·플레이 시작·Unity 종료 때는 저장할지 묻고, 스크립트 재컴파일에도 유지된다. 클래스 상세의 에디터 설정(스프라이트 규격 그룹·필수), 정리용 폴더도 함께 저장된다(DB는 코드를 고치지 않는다)(예외로 즉시: 프리팹 컴포넌트 수정·스프라이트 베이크·선택 기능 켜고 끄기). ID를 가리키는 칸은 직접 입력하지 않고 목록에서 고른다. 모든 목록은 같은 디자인의 드롭다운이며, 항목이 많으면 검색창이 생긴다(한글 초성 검색 가능: `ㅊㅊ` → 처치 수).

**키워드 탭**: 키워드는 코드가 원본이다(`[KeywordGroup] public static class Rarity { public const string Rare = "rare"; }`). 탭은 그 코드를 읽어 그룹 = 폴더(하위 그룹 = 안쪽 클래스), 키워드 = 줄로 보여 준다(읽기 전용 — 키워드는 AI가 코드에서 만든다, 관례상 `Assets/Code/Keywords/그룹.cs`). 그룹 아래에는 그 그룹을 쓰는 필드가 보인다.

모니터에서 값을 바꾸는 방법은 두 가지다.

- **직접 수정**: 상태 탭의 값 칸, 수치 탭 작업 패널의 '직접 수정', 도전과제 작업 패널의 '강제 달성'. 값만 덮어쓰고 이벤트·수치 규칙은 반응하지 않는다.
- **이벤트로 반영**: 수치의 '이벤트로 더하기/기록', 도전과제의 '트리거로 달성'(조건을 채운 뒤 트리거의 이벤트를 값 조건에 맞춰 쏨), 이벤트 탭의 '이벤트 받기'. 게임에서 그 값을 얻은 것처럼 실제 이벤트를 쏴서 다른 수치·도전과제·보상까지 연쇄 반응하고, 행동으로 기록되어 Undo할 수 있다.

도전과제·수치 탭은 항목을 클릭해 선택하면, 이벤트 탭은 이벤트를 고르면 오른쪽 아래 작업 패널에 조작 버튼이 뜬다.

## 핵심 개념

```
행동(GameAction) → 이벤트(Events) → 수치(Stats) → 조건 평가 → 도전과제 달성 → 보상
```

- **행동은 데이터다.** 모든 플레이어 행동은 `session.Do(행동)`을 거친다. 행동 하나가 Undo 단위다.
- **모든 플레이어 데이터는 플레이어 데이터 저장소(`session.Data`)에 둔다.** 세션 구역(게임 내용, 되감김)과 메타 구역(도전과제·누적 수치, 되감기지 않음)으로 나뉜다.
- **값의 규칙은 하나.** 행동은 실행되는 즉시 끝나고, 이미 한 행동은 언제든 뒤에서부터 `Undo()`로 되돌린다(확정 단계 없음). 실시간 게임은 행동별 기록 대신 일정 간격 스냅샷으로 같은 `Undo()`가 되감는다. 도전과제 달성과 보상은 되돌리지 않는다(행동이 바꾼 값만 돌아가 반복 획득 불가). 게임 코드는 `GameApi`(`Game.All`·`Find`·`Do`·`Use`·`Data()`)로 객체를 꺼내 필드를 그냥 고친다(행동 끝·프레임 끝 Sync가 바뀐 곳을 기록 — `Documentation~/Coding.md`). 저수준 API: 읽기 `Get`, 교체 `Set`, 부분 수정 `Modify`.
- **식별자는 한 곳에서 짓는다.** 플레이어 데이터·키워드·수치·도전과제의 ID는 DB 창에서만 짓고, 에디터에서는 목록에서 고르고, 코드에서는 자동 생성되는 `GameIds`(예: `GameIds.PlayerData.PlayerInventory` → `PlayerDataKey<Inventory>`)로 쓴다. ID를 바꾸면 참조·에셋·생성 코드·저장 파일 호환이 함께 처리된다.
- **저장은 두 가지.** 프로필(메타 구역: 도전과제·누적 수치 등)은 `profile.json`, 진행 중인 판(세션 구역·난수·시간)은 `run.json`. 종료 시 자동 저장, 판 이어하기는 `GBibleRuntime.LoadRun`.
- **게임 시간 조절.** `GameTime.Pause/Resume`(이유별로 겹침), `GameTime.SetSpeed`(슬로우·배속), `GameTime.StepFrame`(개발용 1프레임 진행). `Time.timeScale`을 바꾸므로 물리·애니메이션·파티클·게임 시계가 모두 따라간다. 멈추면 안 되는 UI 연출만 unscaled 시간을 쓴다. 모니터 상단에서도 조절할 수 있다.
- **필드 경로와 조건 조립.** 이벤트·저장소 값의 필드를 깊이와 상관없이 폴더형 탐색기로 고르고(`items[].tags`), 형식에 맞는 비교(숫자: 이상·이하…, 글자·선택지: 일치·아님, 목록: 포함·개수, 참/거짓)만 목록에 나온다. 수치 규칙의 "조건"(예: 태그 boss 포함일 때만 반영)과 조건 블록 "플레이어 데이터 검사"(예: 인벤토리 › 아이템의 각 항목 › 태그에 legendary 포함)에 쓴다. 조건 그룹은 접고 펼치며, 줄마다 위·아래·복제·바꾸기·삭제.
- **데이터 보기(그리드 + 블록).** 한 객체를 가로 12칸 그리드에 블록을 끼워 그린다. 기본: 왼쪽 스프라이트 나열(2개씩) + 오른쪽 데이터 나열. 블록: 스프라이트 나열, 데이터 나열, 특정 스프라이트, 필드 카드, 클래스 카드(안쪽 필드 골라 표시). 타입별 배치는 "배치 편집"에서 바꿔 `ProjectSettings/MomoiProject/Layouts.json`에 저장되고, 데이터베이스 상세 패널·모니터 탐색기 어디서 보든 같은 배치로 보인다. 프로젝트 전용 블록은 `GsLayoutBlock`을 상속하고 `[LayoutBlock("id", "이름")]`만 붙이면 목록에 나타난다.- **DB.** 미리 정의된 모든 것(아이템·몬스터 같은 데이터 항목, 인스턴스, 프리팹)을 `MomoiProject > DB`에서 추가·확인·변경·삭제한다. 항목은 타입별 배치로 그려지고, 클래스·목록 필드는 탐색기로 들어가 고친다(조건·보상 같은 블록 트리는 문장 편집기). **인스턴스**은 개발자가 만든 아무 클래스의 미리 채운 인스턴스(`InstanceData.CreateInstance<T>()`로 사본), **프리팹**은 연결한 프리팹의 스크립트 컴포넌트 필드를 탐색기로 고친다(에셋에 바로 저장, Undo 가능). 프리팹 항목은 DB에서 지우지 않고 '프로젝트에서 보기'로 이동해 프로젝트 창에서 지우며, 프리팹을 지우면 항목도 자동으로 정리된다. 탐색기의 딕셔너리는 한 요소를 한 줄(`[키] 값`)로 보이고 키를 더하거나 뺄 수 있다.
- **모니터의 데이터 탭.** 현재 세션·플레이어의 모든 값을 탐색기로 본다: 판 데이터(세션 구역) · 프로필 데이터(메타 구역) · 수치 · 도전과제 달성. 고치면 Set·Modify를 거쳐 기록되고 Undo할 수 있다. 타임라인·행동·이벤트 같은 기능 화면은 따로 두되, 그 안의 이벤트·행동·클래스 값을 누르면 탐색기로 연다. 이벤트 호출도 실제 이벤트 인스턴스를 탐색기로 채운다.
- **트리거·결과(모든 "조건 → 보상" 기능 공통).** 로그·도전과제·능력·아이템 사용은 같은 두 칸이다: 트리거(이벤트 받기·조건 충족·사용, AND·OR·NOT 중첩) + 결과(로그에 기록·플레이어 데이터에 주기·대상에게 효과·코딩한 결과). 화면·검증·실행은 하나이고 기능마다 고를 수 있는 범위(잠금)만 다르다: 로그 = 이벤트 받기 → 로그에 기록(고정), 도전과제 = 이벤트·조건 → 무엇이든, 능력·아이템 = 사용(+사용 조건) → 효과·주기. 새 기능은 `[TriggerSlot]`·`[OutcomeSlot]` 표시만 붙이면 같은 칸이 생긴다(`Documentation~/CodingSyntax.md` 2-1).
- **조건식.** 편집은 한 줄 식으로 한다: 조건 블럭 · 논리 연산자(AND·OR·NOT) · 괄호 세 요소를 `+ 블럭` · `+ 논리 연산자` · `+ 괄호`로 더하고, 연산자를 누르면 AND↔OR가 바뀐다(AND가 먼저 묶인다). 블럭과 괄호는 왼쪽 손잡이를 끌어 옮긴다(순서 바꾸기, 괄호 안으로 넣기·밖으로 빼기). 필드 검사 · && AND · || OR · ! NOT · 배열·리스트 항목 검사(any/all/none/count)를 자유롭게 중첩한다(중첩 = 괄호). 편집 화면은 코드처럼 `[처치 수] [>=] [10]`으로 보이고, 마우스를 올리면 "처치 수가 10 이상이라면" 같은 설명이 뜬다. DB 요약과 로그는 기본이 자연어("등급이 3 이상 그리고 (태그에 boss 포함 또는 …)", "인벤토리 › 아이템 #12 › 레벨 3 → 4")이고, **Alt를 누르고 있으면** 코드 형태(`등급 >= 3 && (…)`, `인벤토리.items[#12].level`)로 바뀐다(마우스를 올리면 다른 쪽이 툴팁으로). 수치 규칙의 조건과 "플레이어 데이터 검사" 조건 블록에 쓴다.
- **키워드와 필드 규칙.** 분류·등급·속성·태그 같은 "고르는 값"은 키워드 그룹 코드로 정의하고(게임 코드는 `Rarity.Rare`처럼 상수로 쓴다), 필드는 `[Keyword(typeof(Rarity))]`로 연결한다(string = 하나, string 리스트·배열 = 여러 개). 그 밖의 에디터 설정(필수·스프라이트 규격 그룹·DB 항목 출처)은 필드 규칙으로 클래스 상세에서 정한다. 모든 편집 칸·탐색기·조건식 값·검증이 이를 따르고, 필수 값이 비면 DB는 검증 오류, 게임 중 저장소 값은 경고 로그 + 모니터 데이터 탭 맨 위 "필수 값 비어 있음"에 채워질 때까지 남는다.
- **스프라이트 규격.** DB에 규격 그룹(예: 아이템 이미지)과 규격(아이콘 128×128, 카드 256×384 …)을 정의하고, 클래스 상세에서 스프라이트 필드에 그룹과 미리보기 규격을 연결한다. 그 필드에 원본 일러를 넣으면 맞춤 창이 떠서 한 번 중심(클릭)·배율(휠)을 잡으면 그룹의 규격이 모두 베이크된다(GPU, 원본 읽기 권한 불필요, `Sprites/그룹/규격/원본_규격.png`). 특정 규격만 따로 잡는 예외 촬영은 영구 기록되고, 규격 크기를 바꾸면 자동으로 다시 베이크하며 예외는 '확인 필요'로 표시된다. 목록·카드·데이터 보기는 미리보기 규격으로 보인다. 게임 코드: `SpriteSpecs.Get(원본, GameIds.SpritePreset.ItemIcon)`.
- **기능 조립(아이템 등).** 종류별로 상속하지 않고 한 클래스에 `[SerializeReference] List<기능>`을 두고 기능 클래스(장착·사용·겹침…)을 붙인다. 한 아이템에 여러 기능이 함께 붙고, 동작은 각 기능 클래스가 가진다. 코드는 종류가 아니라 기능을 묻는다: `item.features.GetFeature<Consumable>()`·`HasFeature<T>()`. 규칙(한 번만·함께 못 붙음·먼저 있어야 함)은 `[FeatureRule]`, 검사는 `Features.Validate`(DB 검증). 상세 창에서는 블록처럼 더하고 뺀다. 예시: 인벤토리 시스템의 아이템(`ItemData`).
- **다국어 텍스트.** 어디서든 `Texts.Get("키", ("값", 3))`으로 현재 언어의 최종 글자를 받는다. 문장 틀은 ICU MessageFormat(복수·선택·숫자·날짜) + 한국어 조사 `{item:을}` + 용어 `[[burn:이]]`(키워드의 스타일·아이콘으로 꾸밈). 언어는 `GameLanguage.Set("en")`, 화면 글자는 `LocalizedText`. 자세히: `Documentation~/Localization.md`
- 자세한 사용법: `Documentation~/PlayerDataStore.md`
- **모든 변화는 통합 기록(Journal)에 원인과 함께 남는다.** 모니터에서 보상 한 줄을 선택하면 그 원인 행동까지 거슬러 올라간다.
- 싱글 게임은 `GBibleRuntime.Session` 하나를 쓴다. 멀티플레이·테스트는 `new GameSession(...)`으로 독립 세션을 만든다.

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
    GBibleRuntime.Configuring += options => options.History = HistoryMode.Actions;

    GBibleRuntime.Started += session =>
    {
        session.Data.Define("battle:combo", DataZone.Session, 0, "현재 콤보");
        session.Actions.Register<AttackAction>(action =>
        {
            var hit = session.Random.Get("combat").Chance(0.7);   // 용도별 시드 난수
            if (hit) session.Events.Publish(new EnemyKilled { Count = 1 });
        });
    };
}
```

```csharp
GBibleRuntime.Session.Do(new AttackAction { target = "고블린" });
GBibleRuntime.Session.Undo();     // 마지막 행동 Undo(몇 번이든)
```

### 3. 데이터(에디터, 코드 없음)

DB 창에서:

1. **로그** 추가 → 범위(이번 판/누적)·집계 방식 선택 → 반영 규칙: 트리거 `+ 블럭 → 이벤트 받기 [적 처치 ▾]`, 결과(로그에 기록, 고정)의 반영할 값 `[Count ▾]`
2. **도전과제** 추가 → 트리거 `+ 블럭 → 비교`(조건 충족) → 문장 `[처치 수] [100] [이상]`의 단어를 하나씩 클릭해 고른다(AND·OR로 이벤트 받기와 섞을 수 있다) → 결과 `+ 결과 추가` → 플레이어 데이터의 필드(예: `player.gold`)를 고르고 → 증감 `500`

상세 패널 위쪽에 "트리거 처치 수 100 이상 / 결과 골드 500 증가" 두 줄 요약이 즉시 표시되고, 문제가 있으면 빨간 글씨로 알린다. 검증 오류가 있으면 빌드가 막힌다.

## 확장

| 추가할 것 | 방법 | 도구에 자동 반영 |
|---|---|---|
| 새 데이터 타입(아이템, 캐릭터 …) | `GameData` 상속 클래스 작성. 필드만 쓰면 된다. 선택: `[Database("아이템", Group = "인벤토리")]` | DB 창 목록(폴더), 검색, 검증 |
| 새 조건 블록 | `Condition` 상속 + `[Serializable]` + `[BlockInfo("이름", Category = "분류")]`. `Evaluate`, `CollectDependencies`, `Describe` 구현 | "+ 조건 선택" 메뉴, 미리보기, 계약 테스트 |
| 새 결과 블록 | `Reward` 상속 + 위와 같은 어트리뷰트. `Grant`, `Describe`, `Effect` 구현(`Context`로 트리거 순간을 본다) | 모든 기능의 "+ 결과 추가" › 코딩한 결과, 계약 테스트 |
| 트리거·결과 칸이 있는 새 기능(퀘스트 등) | `Trigger` 필드에 `[TriggerSlot(…)]`, `List<Outcome>` 필드에 `[OutcomeSlot(…)]` | 로그·도전과제·능력과 같은 편집 화면·검증·실행(`session.Triggers`) |
| 새 이벤트 | `[GameEvent("표시 이름")]` 클래스 | 수치 규칙의 이벤트·필드 선택 목록 |
| 새 화면 배치 블록 | `GsLayoutBlock` 상속 + `[LayoutBlock("id", "이름", Description = "설명")]` | 배치 편집의 블록 목록 |

- 필드 이름을 한국어로 보이려면 `[FieldLabel("이름")]`.
- 다른 항목의 ID를 담는 문자열 필드는 `[StatRef]`, `[AchievementRef]`를 붙인다 → 직접 입력 대신 목록 선택 + 존재 여부 검증.
- **블록 계약**: 조건은 의존하는 관찰 대상(`CollectDependencies`)이나 확인 주기(`PollInterval`) 중 하나를 반드시 선언한다. 둘 다 없으면 계약 테스트가 실패한다.

목록 줄의 부제목은 보기 설정의 부제목 필드를 쓴다(비워 두면 자동 추론, `ProjectSettings/MomoiProject/DatabaseViews.json`). 예전 시트·카드 보기와 보기 설정 패널은 숨겨 두었다(`DatabaseWindow.LegacyViews`).

## 규칙

- `Runtime/Core`에서 UnityEngine을 쓰지 않는다. Unity 전용 어트리뷰트는 `#if UNITY_2020_1_OR_NEWER`로 감싼다.
- 게임 로직에서 `UnityEngine.Random`·`System.Random`·`Time`을 쓰지 않는다 → `session.Random.Get("용도")`, `session.Clock`.
- 플레이어 데이터를 static 변수나 필드에 두지 않는다 → `session.Data` (Undo·되감기·저장·모니터에서 빠지지 않게).
- 새 시스템은 관찰 계약(`IInspectable`)을 구현한다.
- 블록은 `Description`이 필수다(DB의 문장 설명·툴팁에 쓰인다).

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
