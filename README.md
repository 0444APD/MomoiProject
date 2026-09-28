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
| **Game Systems → 데이터베이스** | `GameData`를 상속한 모든 타입을 시트·카드(세로·가로·바)로 보고 추가·검색·수정. 조건·보상은 블록 편집기. |
| **Game Systems → 모니터** | 플레이 중 통합 기록(인과 추적), 상태, 도전과제 진행도, 수치, 이벤트 발생기, 행동 로그(확정·무르기), 디버그 명령 |
| **Game Systems → UI 샘플** | 디자인 시스템의 모든 부품. 새 도구는 이 부품을 조립해 만든다. |
| Game Systems → 샘플 → 샘플 데이터 만들기 | 샘플 수치·도전과제 생성(샘플 심볼 필요) |

시트와 카드는 읽기 전용이다. 카드를 클릭하거나 시트 행을 더블클릭하면 공용 **상세 패널**이 열리고, 편집은 그 안에서 사본에 한다. 바뀐 값은 위에 이전 값, 아래에 현재 값(사이 V자 화살표)으로 보이고, **저장**(변경 목록 확인 후 반영) 또는 **되돌리기**로 끝낸다. ID를 가리키는 칸은 직접 입력하지 않고 목록에서 고른다. 모든 목록은 같은 디자인의 드롭다운이며, 항목이 많으면 검색창이 생긴다(한글 초성 검색 가능: `ㅊㅊ` → 처치 수).

모니터에서 값을 바꾸는 방법은 두 가지다.

- **직접 수정**: 상태 탭의 값 칸, 수치 탭 작업 패널의 '직접 수정', 도전과제 작업 패널의 '강제 달성'. 값만 덮어쓰고 이벤트·수치 규칙은 반응하지 않는다.
- **이벤트로 반영**: 수치의 '이벤트로 더하기/기록', 도전과제의 '이벤트로 달성', 이벤트 탭의 '이벤트 발생'. 게임에서 그 값을 얻은 것처럼 실제 이벤트를 발생시켜 다른 수치·도전과제·보상까지 연쇄 반응하고, 행동으로 기록되어 무를 수 있다. 수동 확정 게임에서는 '테스트 후 즉시 확정'(기본 꺼짐)으로 바로 확정할지 고른다.

도전과제·수치 탭은 항목을 클릭해 선택하면, 이벤트 탭은 이벤트를 고르면 오른쪽 아래 작업 패널에 조작 버튼이 뜬다.

## 핵심 개념

```
행동(GameAction) → 이벤트(Events) → 수치(Stats) → 조건 평가 → 도전과제 달성 → 보상
```

- **행동은 데이터다.** 모든 플레이어 행동은 `session.Do(행동)`을 거친다. 행동 하나가 무르기 단위다.
- **모든 게임 상태는 상태 저장소(`State`)에 둔다.** 세션 구역(게임 내용, 되감김)과 메타 구역(도전과제·누적 수치, 되감기지 않음)으로 나뉜다.
- **확정(Commit)** 전의 행동은 무를 수 있다. 무르면 상태와 난수가 함께 되돌아간다. 도전과제는 조건을 만족하는 즉시 달성되고 무르기로 취소되지 않는다. 값만 되돌아가므로 되풀이해도 쌓이지 않는다(되감기 파밍 불가). 달성 보상은 확정 시 지급한다.
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
    // 턴제·되감기 게임이면 자동 확정을 끈다(기본은 행동마다 즉시 확정).
    GameSystemsRuntime.Configuring += options => options.AutoCommit = false;

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
GameSystemsRuntime.Session.Commit();   // 턴 종료
GameSystemsRuntime.Session.Undo();     // 확정 전 무르기
```

### 3. 데이터(에디터, 코드 없음)

데이터베이스 창에서:

1. **수치** 추가 → 범위(이번 판/누적)·집계 방식 선택 → 반영 규칙: 이벤트 `[적 처치 ▾]`, 반영할 값 `[Count ▾]`
2. **도전과제** 추가 → 달성 조건 `+ 조건 선택 → 수치 비교` → 문장 `[처치 수] [100] [이상]`의 단어를 하나씩 클릭해 고른다 → 보상 `+ 보상 추가 → 획득` → `[골드] [500] 획득`

상세 패널 위쪽에 "조건 처치 수 100 이상 달성 시 / 보상 골드 500 획득" 두 줄 요약이 즉시 표시되고, 문제가 있으면 빨간 글씨로 알린다. 검증 오류가 있으면 빌드가 막힌다.

## 확장

| 추가할 것 | 방법 | 도구에 자동 반영 |
|---|---|---|
| 새 데이터 타입(아이템, 캐릭터 …) | `GameData` 상속 클래스 작성. 필드만 쓰면 된다. 선택: `[Database("아이템", Group = "인벤토리")]` | 데이터베이스 창 목록, 시트·카드, 검색, 검증 |
| 새 조건 블록 | `Condition` 상속 + `[Serializable]` + `[BlockInfo("이름", Category = "분류")]`. `Evaluate`, `CollectDependencies`, `Describe` 구현 | "+ 조건 선택" 메뉴, 미리보기, 계약 테스트 |
| 새 보상 블록 | `Reward` 상속 + 위와 같은 어트리뷰트. `Grant`, `Describe`, `Effect` 구현 | "+ 보상 추가" 메뉴, 계약 테스트 |
| 새 이벤트 | `[GameEvent("표시 이름")]` 클래스 | 수치 규칙의 이벤트·필드 선택 목록 |

- 필드 이름을 한국어로 보이려면 `[FieldLabel("이름")]`.
- 다른 항목의 ID를 담는 문자열 필드는 `[StatRef]`, `[AchievementRef]`를 붙인다 → 직접 입력 대신 목록 선택 + 존재 여부 검증.
- **블록 계약**: 조건은 의존하는 관찰 대상(`CollectDependencies`)이나 확인 주기(`PollInterval`) 중 하나를 반드시 선언한다. 둘 다 없으면 계약 테스트가 실패한다.

카드·시트에 어떤 필드를 보여줄지는 코드 없이 데이터베이스 창의 **보기 설정**에서 고른다(비워 두면 자동 추론). 설정은 `ProjectSettings/GameSystems/DatabaseViews.json`에 저장되어 팀과 공유된다.

## 규칙

- `Runtime/Core`에서 UnityEngine을 쓰지 않는다. Unity 전용 어트리뷰트는 `#if UNITY_2020_1_OR_NEWER`로 감싼다.
- 게임 로직에서 `UnityEngine.Random`·`System.Random`·`Time`을 쓰지 않는다 → `session.Random.Get("용도")`, `session.Clock`.
- 게임 상태를 static 변수나 필드에 두지 않는다 → `session.State` (무르기·되감기·저장·모니터에서 빠지지 않게).
- 새 시스템은 관찰 계약(`IInspectable`)을 구현한다.

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
