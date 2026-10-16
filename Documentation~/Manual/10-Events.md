# 10. 이벤트

**한 줄 요약**: 이벤트는 "게임에서 방금 일어난 일"(유닛 처치, 골드 획득, 액티브 효과 사용 …)을 값과 함께 담은 평범한 C# 클래스다. 게임 코드가 이벤트를 **쏘고**, 로그·도전과제·퀘스트의 Trigger와 씬 코드가 이벤트를 **받는다**.

**언제 쓰는가**

- 로그(통계)를 셀 때: "유닛 처치를 받을 때마다 처치 수 +1".
- 도전과제·퀘스트가 "그 순간"에 반응해야 할 때: "보스 처치를 받는 순간 달성".
- 씬 코드가 연출·UI를 틀 때: "처치를 받으면 피격 이펙트 재생".

이 문서에서 쓰는 용어:

| 용어 | 뜻 |
|---|---|
| 쏜다 | 게임 코드(또는 모니터 도구)가 이벤트 인스턴스를 만들어 세션의 이벤트 버스(`EventBus`)에 넘긴다(`Publish`). |
| 받는다 | Trigger·로그 규칙·`Subscribe`·`SubscribeAfterAction`으로 등록한 함수가 쏜 이벤트를 넘겨받는다. |
| 행동 | `Game.Do`로 실행하는 값 변경 한 묶음. 실패하면 바꾼 것이 모두 취소되고, Undo 한 단위다(→ `02-Actions-Undo.md`). |
| Trigger | "언제"를 정하는 칸. 이벤트 받기·조건 충족·사용을 AND·OR·NOT으로 조립한다(→ `11-Triggers-Conditions.md`). |
| Journal | 세션의 통합 기록. 쏜 이벤트와 그 뒤에 일어난 변화가 원인과 함께 남는다(모니터의 타임라인). |

---

## 1. 에디터에서 보는 법

이벤트는 **코드에서만 만든다**. 에디터는 만든 이벤트를 보고, 고르고, 시험으로 쏘는 곳이다.

### 1-1. DB › 이벤트 탭

1. 메뉴 `MomoiProject → DB`을 연다.
2. 사이드바에서 **이벤트** 탭을 고른다. `[GameEvent]`가 붙은 클래스가 폴더(실제 디스크 폴더)별로 보인다. 읽기 전용이다(만들기·옮기기 없음).
3. 항목을 누르면 클래스 상세가 열린다: 필드의 이름·형식, 다른 클래스 형식이면 그 클래스로 이동, `cs 열기`.

목록에 나오는 이벤트는 "내 코드"의 `[GameEvent]` 클래스와 라이브러리 기본 이벤트(`도전과제 달성`, `액티브 효과 사용`, `상태 효과 걸림`, `상태 효과 끝남`, `선택 요구`, 2-10)다. 플러그인·패키지 폴더에 둔 이벤트는 빠진다. 보이지 않으면 `ProjectSettings/MomoiProject/ProjectCode.json`에 그 폴더·네임스페이스를 더한다(AI).

### 1-2. 이벤트를 고르는 칸

Trigger의 **이벤트 받기** 블럭(문장 `{eventType} 받기`)에서 이벤트 칸을 누르면 같은 이벤트 목록이 뜬다. 고른 뒤에는 그 이벤트의 필드로 값 조건(`filter`)을 걸고, 로그의 **반영할 값**에서 숫자 필드를 고른다. 이벤트의 필드가 그대로 고르는 목록이 되므로, 조건·기록에 쓸 값은 이벤트 필드로 둔다.

### 1-3. 모니터 › 이벤트 탭 ("이벤트 호출")

플레이 중 `MomoiProject → 모니터` 창의 **이벤트** 탭에서 이벤트를 직접 쏴 볼 수 있다. 이 도구를 "이벤트 호출"이라 부른다.

1. 왼쪽 목록에서 이벤트를 고른다(표시 이름. 마우스를 올리면 전체 형식 이름. `Subscribe`로 받는 곳이 있으면 그 수가 옆에 보인다).
2. 오른쪽 탐색기에서 이벤트의 값을 채운다(중첩 클래스·리스트 필드도 같은 화면에서).
3. 아래에 **반응하는 로그**(`처치 수 ← 횟수` 같은 줄)가 보인다. 없으면 "이 이벤트를 반영하는 로그가 없습니다."
4. 아래 작업 패널에 횟수(`회`)를 적고 **이벤트 호출** 버튼을 누른다. 적은 횟수만큼 사본을 만들어 쏜다(이후에 값을 고쳐도 이미 쏜 기록은 바뀌지 않는다).

호출한 이벤트는 게임과 같은 길로 간다: 행동(`GameAction`) 하나(행동 탭에 "[테스트] (이벤트 문장) ×횟수 — 모니터 이벤트 호출", 공개 범위 "개발자")로 기록되고, 로그·도전과제·트리거가 실제로 반응하며, **Undo**(마지막 행동 되돌리기)로 되돌릴 수 있다. 도전과제 달성과 보상은 Undo해도 남는다(→ `14-Achievements.md`).

이벤트를 모니터에서 쏘려면 이벤트 클래스에 **매개변수 없는 생성자**가 있어야 한다(구조체는 늘 된다). 없으면 탭에 오류가 뜬다.

같은 쏘기를 버튼으로 저장해 두려면 DB › 개발 › 개발 명령에 항목을 만들고 결과에 **이벤트 쏘기** Block(이벤트 · 값 줄 · 횟수)을 넣는다. 모니터 › 명령 탭에 버튼이 생긴다(→ `22-Monitor-Debug.md` 7절, `12-Outcomes.md`).

### 1-4. 모니터 › 타임라인

쏜 이벤트는 Journal에 "이벤트" 종류로 남는다. 그 줄을 펼치면 이 이벤트가 원인이 된 변화(로그 증가 → 도전과제 달성 → 보상)가 단계별로 이어진다. 줄의 글자는 이벤트가 정한다(아래 2-6).

---

## 2. 코드로 쓰는 법 — 문법 요소를 하나씩

### 2-1. `[GameEvent("표시 이름")]` — 이벤트 클래스 표시

```csharp
using System.Collections.Generic;
using GBible;

[GameEvent("유닛 처치")]
public class UnitKilled
{
}
```

- `class`와 `struct` 모두에 붙는다. 붙인 형식이 이벤트 목록(DB › 이벤트, Trigger의 이벤트 칸, 모니터)에 나온다.
- 인자는 화면에 보일 이름(한글)이다. 생략하면(`[GameEvent]`) 클래스 이름이 표시 이름이 된다.
- 기반 클래스를 상속하거나 인터페이스를 구현할 필요가 없다. 평범한 C# 클래스다.
- 프로젝트에서는 `Assets/MomoiProject/Code/Rules/Events/Events.cs`에 두고, 이름은 일어난 일을 과거형으로(`TurnEnded`, `EnemyKilled`) 짓는다(관례 1·2절). 다른 기능이 반응할 만한 일(처치·획득·턴 끝·판 끝)은 모두 이벤트로 쏜다(관례 9절).
- 멀티플레이: `[GameEvent("…", ServerOnly = true)]`는 클라이언트에 보내지 않는다. 남에게 숨길 이벤트(카드 뽑음)는 `IOwnedEvent`(`int Owner` — 그 플레이어에게만, 0이면 모두)를 구현한다(40장).

### 2-2. 필드 — 이벤트가 실어 나르는 값

```csharp
[GameEvent("유닛 처치")]
public class UnitKilled
{
    public string killerId;                              // 처치한 유닛 id
    public string victimId;                              // 처치된 유닛 id
    public int exp;                                      // 줄 경험치
    public int combo;                                    // 연속 처치 수
    public List<string> tags = new List<string>();       // "boss", "elite" …
}
```

- 에디터와 조건이 읽는 것은 **public 필드**와 **읽을 수 있는 public 속성**(인덱서 제외)이다. private 필드는 보이지 않는다.
- 필드 형식은 숫자·글자·참거짓·enum·리스트·배열·딕셔너리·다른 클래스 무엇이든 된다. 조건은 형식에 맞는 비교만 보여 준다(숫자는 이상·이하, 리스트는 포함·개수 …).
- 객체를 통째로 넣기보다 **id**(`killerId`)를 넣는다. 이벤트는 기록에 남고 모니터에서 다시 만들어지므로, 값으로 다시 찾을 수 있는 것이 좋다. (행동과 다르다: 행동의 대상 필드는 객체를 넣어도 시스템이 id로 다루지만, 이벤트 필드는 그런 변환이 없다.)

### 2-3. 읽기 전용 속성 — 계산한 값도 조건에 쓴다

```csharp
[GameEvent("유닛 처치")]
public class UnitKilled
{
    public List<string> tags = new List<string>();
    public bool IsBoss => tags.Contains("boss");         // 이벤트 받기의 조건에서 'IsBoss 참'으로 고를 수 있다
    public int TagCount => tags.Count;                   // 로그의 '반영할 값'으로도 고를 수 있다(숫자)
}
```

- 이벤트 받기의 값 조건(필드 경로)과 로그에 기록의 반영할 값은 속성도 따라간다.
- Journal의 기본 문장(2-6)에는 읽을 수 있는 멤버(공개 필드·`[SerializeField]` 필드·공개 읽기 속성)가 모두 나온다.

### 2-4. `session.Events.Publish(이벤트)` — 쏘기

```csharp
using System;
using GBible;

[Serializable]
public class Unit { public string id; public int hp = 30; }   // 플레이어 데이터 클래스

[Serializable]
public class KillAction : GameAction
{
    public Unit killer;
    public Unit victim;
    protected override void Run()
    {
        victim.hp = 0;
        Game.Events.Publish(new UnitKilled { killerId = killer.id, victimId = victim.id, exp = 10 });
    }
}
```

- 이벤트 버스는 세션에 하나 있다: `session.Events`, 또는 Block·행동 안에서 `Game.Events`(`GameApi.Events`), 씬 코드에서 `GBibleRuntime.Game.Events`.
- `Publish<T>(T evt)`는 그 자리에서 바로 받는 쪽을 모두 부른 뒤 돌아온다(대기열 없음). 그래서 다음 줄에서는 로그가 이미 올라가 있다. 행동 안에서 받는 쪽이 예외를 던지면 `Publish`에서 그 예외가 나와 행동이 실패한다(2-7).
- **행동 안에서 쏘는 것을 기본으로 한다.** 행동 안에서 쏜 이벤트로 바뀐 값(로그 증가, 결과가 고친 값)은 그 행동의 Undo 단위에 들어간다. 행동 밖에서 쏘면 그 변화는 "행동 밖 변경"으로 기록되어 Undo로 되돌릴 수 없다.
- `null`을 쏘면 `ArgumentNullException`.

### 2-5. `PublishObject(object)` — 형식을 실행 중에만 알 때

```csharp
object evt = Activator.CreateInstance(eventType);   // 형식을 데이터로 받은 경우 등
session.Events.PublishObject(evt);
```

- `Publish<T>`도 내부에서 이것을 부른다. 받는 쪽이 고르는 기준은 **실제 형식**(`evt.GetType()`)이다.
- 모니터의 이벤트 호출도 이 길로 쏜다.

### 2-6. `IDescribable` — Journal에 남는 문장 정하기

```csharp
[GameEvent("유닛 처치")]
public class UnitKilled : IDescribable
{
    public string killerId, victimId;
    public string Describe() => $"{killerId}이(가) {victimId} 처치";
}
```

- 구현하지 않으면 기본 문장은 `표시 이름 (필드=값, 필드=값 …)`이다. 예: `유닛 처치 (killerId=hero, victimId=orc1, exp=10, …)`.
- 필드가 많거나 리스트가 있어 기본 문장이 읽기 어려울 때 구현한다.

### 2-7. 코드에서 받기 — 연출은 `SubscribeAfterAction<T>`, 규칙은 `Subscribe<T>`

받는 함수를 등록하는 길은 둘이다. 받는 **시점**이 다르다.

| | `SubscribeAfterAction<T>(Action<T>)` | `Subscribe<T>(Action<T>)` |
|---|---|---|
| 받는 때 | 행동 안에서 쏜 이벤트: **바깥 행동이 성공한 뒤**, 쏜 순서대로(안쪽 행동의 이벤트 포함. 받는 쪽 — 규칙·트리거 결과 — 이 받는 동안 쏜 이벤트는 그 이벤트 뒤). 행동 밖에서 쏜 이벤트: 바로 | 쏜 그 자리에서 바로(행동 안이면 행동 안에서) |
| 실패한 행동의 이벤트 | 오지 않는다(버린다) | 이미 받았다(값은 취소됐는데 연출은 나간 상태가 된다) |
| 받는 시점의 위치 | 늘 행동 밖(`session.Data.InTransaction` 거짓) | 행동 안일 수 있다 |
| 받는 함수가 예외를 던지면 | 기록만 남고 다른 받는 쪽은 계속 불린다 | 행동 안: **그 행동 전체가 실패해 취소된다**. 행동 밖: 기록만 남는다 |
| 쓰는 곳 | **화면 연출·UI**(피격 숫자, 효과음, 토스트) | **규칙**(값을 바꾸는 쪽, 행동 안에서 바로 반응해야 하는 것) |

```csharp
using System;
using GBible;
using UnityEngine;

public class KillEffects : MonoBehaviour
{
    IDisposable _onKill;

    // 성공한 행동의 처치만, 행동이 끝난 뒤 쏜 순서대로 온다.
    void OnEnable()  => _onKill = GBibleRuntime.Game.Events.SubscribeAfterAction<UnitKilled>(e => PlayHitEffect(e.victimId));
    void OnDisable() => _onKill?.Dispose();          // 그만 받기(모아 둔 것도 버린다)

    void PlayHitEffect(string unitId) { /* 연출 */ }
}
```

- 연출 스크립트는 예외 없이 `SubscribeAfterAction`을 쓴다. 행동이 성공했는지 기다리는 대기열·`Actions.Executed`/실패 처리·안쪽 행동 거르기 같은 코드를 직접 쓰지 않는다.
- 한 행동에서 여러 이벤트가 오면 한꺼번에 이어서 불린다. 시간 간격을 두고 틀려면 받은 것을 큐에 넣고 `Update`에서 하나씩 꺼낸다(3-7).
- 돌려받는 `IDisposable`을 `Dispose()`하면 그만 받는다. 그 순간부터다: 같은 이벤트를 먼저 받은 쪽이 Dispose하면(화면을 닫음) 뒤의 그 받는 쪽은 지금 이벤트도, 모아 둔 이벤트도 받지 않는다(`Subscribe`·`SubscribeAll`도 같다). 씬 오브젝트는 `OnDisable`·`OnDestroy`에서 꼭 Dispose한다(세션은 씬보다 오래 산다).
- `T`와 **정확히 같은 형식**의 이벤트만 받는다. `UnitKilled`를 상속한 `BossKilled`를 쏘면 `<UnitKilled>`로 받는 쪽은 받지 않는다.
- **행동 안에서 쏜 이벤트를 받는 함수가 예외를 던지면 그 행동 전체가 실패한다.** `Subscribe`로 받는 함수, 트리거·로그·상태 효과·패시브 효과의 결과처럼 시스템이 받는 쪽 모두 같다. 그 행동이 바꾼 것이 모두 되돌아가고, 예외(플레이어용 실패 `GameFailure` 포함)가 `Do`를 부른 쪽으로 올라간다(`02-Actions-Undo.md` 2.7). 그 뒤의 받는 쪽은 불리지 않는다. 이벤트 예약(`Game.ScheduleOn`)만은 따로다: 그 예약이 바꾼 것만 되돌리고 행동은 계속된다(`29-Statuses-Schedules-Choices.md` 2-5).
- 행동 밖에서 쏜 이벤트, 그리고 `SubscribeAfterAction`으로 받는 함수(늘 행동 밖)에서는 예외가 나도 다른 받는 쪽이 계속 불린다. 예외는 Journal에 오류("구독자 오류 (클래스.함수): …", 출처 `Events`)로 남는다.
- **연출 쪽(씬 코드)에서는 플레이어 데이터를 바꾸지 않는다.** 값을 바꾸는 일은 행동·결과(Outcome)나 `Subscribe`로 받는 규칙에서 한다(그래야 Undo·저장·멀티플레이가 맞는다).

### 2-8. `SubscribeAll(Action<object>)` — 모든 이벤트 받기

```csharp
var all = session.Events.SubscribeAll(evt => Debug.Log(EventBus.Describe(evt)));
// …
all.Dispose();
```

- 형식과 상관없이 모든 이벤트를 받는다. 디버그 출력, 네트워크 중계, 리플레이 녹화처럼 이벤트 전체를 다루는 코드용이다.
- 트리거 감시(`session.Triggers`)도 이것으로 이벤트를 받는다.

### 2-9. 그 밖의 멤버

| 멤버 | 뜻 |
|---|---|
| `event Action<object> Published` | 이벤트마다 한 번, `SubscribeAfterAction`이 받는 차례와 같은 차례로 불린다: 행동 안이면 쏜 순간(받는 쪽보다 **먼저** — 받는 쪽이 안에서 쏜 이벤트보다 앞), 행동 밖이면 모든 받는 쪽을 부른 **뒤**. 멀티플레이 서버가 보낼 이벤트를 이것으로 모은다(클라이언트의 연출 차례가 방장과 같다). |
| `int SubscriberCount(Type)` | 그 형식을 `Subscribe`로 받는 곳의 수(`SubscribeAll`·`SubscribeAfterAction`은 세지 않는다). |
| `static string EventBus.DisplayNameOf(Type)` | `[GameEvent]`의 표시 이름(없으면 클래스 이름). |
| `static string EventBus.Describe(object)` | Journal에 쓰는 문장(2-6 규칙). |

### 2-10. 기본 제공 이벤트

| 형식 | 표시 이름 | 필드 | 쏘는 곳 |
|---|---|---|---|
| `AchievementUnlocked` | 도전과제 달성 | `Id`, `DisplayName` | 도전과제가 달성된 순간(보상 실행 뒤) |
| `AbilityUsed` | 액티브 효과 사용 | `Ability`, `User`, `Targets`(id 리스트), `Times`(결과를 낸 횟수 — 행동 직전 이벤트 '사용'이 바꾼 것), `TargetCount`(속성) | `Game.Use`가 끝날 때 |
| `StatusApplied` | 상태 효과 걸림 | `holder`(가진 쪽 id), `status`, `stacks`(걸린 뒤의 중첩), `source`, `by`(건 개체 id) | 상태 효과를 걸 때. 처음 걸 때와 다시 걸 때 모두(다시 걸기 규칙이 '무시'면 쏘지 않는다) |
| `StatusEnded` | 상태 효과 끝남 | `holder`, `status`, `expired`(지속이 다 되면 참, 풀면 거짓) | 상태 효과가 끝날 때. 지속이 다 되면 보정 떼기 → 끝날 때 결과 → 이 이벤트 순. 가진 쪽이 데이터에서 사라져 정리될 때는 쏘지 않는다 |
| `ChoiceRequested` | 선택 요구 | `choice`(선택 id), `prompt`, `player` | 선택 대기를 요구한 순간(`Game.Ask`·결과 '선택 요구', `29-Statuses-Schedules-Choices.md`) |
| `DialoguePlayRequested` | 다이얼로그 재생 요청 | `dialogue`, `entryId`, `player` | 결과 '다이얼로그 재생'(선택 기능 '다이얼로그'를 켰을 때만, 24장). 화면은 `SubscribeAfterAction`으로 받아 연다 |

상태 효과 아이콘 줄은 이벤트가 아니라 데이터(`$statuses` — `Game.Statuses`)를 보고 그린다(Undo·불러오기 뒤에도 맞게). `StatusApplied`·`StatusEnded`는 걸림·끝남 연출(반짝임·소리)에만 `SubscribeAfterAction`으로 받는다(29장 2-8 · 5-13).

---

## 3. 상황별 예제

### 3-1. 처치 이벤트를 정의하고 공격 행동에서 쏘기

```csharp
using System;
using System.Collections.Generic;
using GBible;

[Serializable]
public class Unit { public string id; public string team; public int hp = 30; public int exp; }

[GameEvent("유닛 처치")]
public class UnitKilled
{
    public string killerId;
    public string victimId;
    public int exp;
    public int combo;
    public List<string> tags = new List<string>();
    public bool IsBoss => tags.Contains("boss");
}

[Serializable]
public class Attack : GameAction
{
    public Unit attacker;
    public Unit target;
    public int damage;

    protected override void Run()
    {
        target.hp -= damage;
        if (target.hp > 0) return;
        Game.Events.Publish(new UnitKilled { killerId = attacker.id, victimId = target.id, exp = 10 });
    }
}

// 씬 코드: GBibleRuntime.Game.Do(new Attack { attacker = hero, target = orc, damage = 12 });   // hero·orc: 데이터 안의 Unit 객체
```

이 이벤트 하나로 로그("처치 수", "보스 처치 수")·도전과제("첫 처치")·퀘스트가 동시에 반응한다. 공격 행동은 그 기능들을 몰라도 된다.

### 3-2. 콤보 값을 함께 실어 "10연속 처치" 도전과제가 읽게 하기

```csharp
// 연속 처치 수는 플레이어 데이터에 둔다(행동 안에서 고치므로 Undo된다).
[Serializable]
public class Battle { public List<Unit> units = new List<Unit>(); public int combo; }

[Serializable]
public class FinishingBlow : GameAction
{
    public Unit attacker;
    public Unit target;
    protected override void Run()
    {
        target.hp = 0;
        var battle = Game.All<Battle>()[0];
        battle.combo++;
        Game.Events.Publish(new UnitKilled { killerId = attacker.id, victimId = target.id, combo = battle.combo });
    }
}
```

DB › 도전과제의 트리거를 `유닛 처치 받기`로 하고 조건을 `combo 10 이상`으로 걸면 된다(→ `14-Achievements.md` 3-3).

### 3-3. 구조체 이벤트

```csharp
[GameEvent("골드 획득")]
public struct GoldEarned
{
    public int amount;
    public string source;   // "chest", "quest" …
}

// 쏘기
session.Events.Publish(new GoldEarned { amount = 50, source = "chest" });
```

자주 쏘는 작은 이벤트는 구조체로 둬도 된다. 받는 쪽·Trigger·로그 모두 같은 방식으로 다룬다.

### 3-4. 도전과제 달성 연출(기본 이벤트 받기)

기본 이벤트도 2-7의 `KillEffects`와 같은 모양으로 받는다: `SubscribeAfterAction<AchievementUnlocked>(e => Show(e.DisplayName))`(`OnEnable`에서 받고 `OnDisable`에서 Dispose).

### 3-5. 모든 이벤트를 서버 로그로 보내기

```csharp
public static class EventRelay
{
    public static IDisposable Attach(GameSession session, Action<string> send) =>
        session.Events.SubscribeAll(evt => send(EventBus.DisplayNameOf(evt.GetType()) + ": " + EventBus.Describe(evt)));
}
```

### 3-6. 형식 이름으로 이벤트를 만들어 쏘기(도구·콘솔 명령)

```csharp
public static void FireByName(GameSession session, string fullName)
{
    var type = TypeResolver.Find(fullName);                       // 예: "MyGame.UnitKilled"
    if (type == null) return;
    session.SimulateEvents(new[] { Activator.CreateInstance(type) }, "콘솔 명령");   // 행동 하나로 쏜다(Undo 가능)
}
```

`session.SimulateEvents(이벤트들, 이유)`는 모니터의 이벤트 호출과 같은 길이다: 이벤트들을 행동 하나 안에서 차례로 쏜다.

### 3-7. 전투 연출을 시간 간격으로 차례차례

```csharp
using System;
using System.Collections.Generic;
using GBible;
using UnityEngine;

public class BattlePresenter : MonoBehaviour
{
    [SerializeField] float gap = 0.25f;

    readonly Queue<object> _queue = new Queue<object>();
    readonly List<IDisposable> _subscriptions = new List<IDisposable>();
    float _wait;

    void OnEnable()
    {
        var events = GBibleRuntime.Session.Events;
        _subscriptions.Add(events.SubscribeAfterAction<DamageDealt>(e => _queue.Enqueue(e)));
        _subscriptions.Add(events.SubscribeAfterAction<UnitKilled>(e => _queue.Enqueue(e)));
    }

    void OnDisable()
    {
        foreach (var s in _subscriptions) s.Dispose();
        _subscriptions.Clear();
    }

    void Update()
    {
        _wait -= Time.deltaTime;
        if (_wait > 0 || _queue.Count == 0) return;
        Play(_queue.Dequeue());
        _wait = gap;
    }

    void Play(object e) { /* DamageDealt → 피해 숫자, UnitKilled → 처치 효과 */ }
}
```

데이터는 행동이 끝난 순간 이미 최종 모습이다(적은 빠졌고 체력은 깎였다). 연출은 그 뒤를 시간 간격으로 따라간다. 실패한 카드 내기의 피해는 큐에 들어오지 않는다.

---

## 4. 주의할 점 / 자주 하는 실수

- **이벤트 클래스 이름·네임스페이스를 바꾸면** 이미 만든 Trigger·로그 규칙이 그 이벤트를 찾지 못한다. 데이터에는 형식의 전체 이름(`네임스페이스.클래스`)이 저장되기 때문이다. DB 검증이 "이벤트 받기: 이벤트 '…'을 찾을 수 없습니다(이름이 바뀌었는지 확인)" 오류를 띄우므로, 바꾼 뒤 그 칸에서 이벤트를 다시 고른다.
- **필드 이름을 바꾸면** 그 필드를 쓰던 값 조건·반영할 값이 깨진다(DB 검증 오류로 보인다).
- **`[GameEvent]`를 빼먹으면** 이벤트 목록에 안 나오고, 조건·로그·기록 문장이 그 이벤트의 값을 리플렉션으로 읽는다(게임이 켜진 뒤 한 번 "…에 읽기 코드가 없어 리플렉션으로 읽습니다" 경고). `[GameEvent]`가 붙은 형식은 컴파일 때 읽기 코드가 생긴다.
- **연출을 `Subscribe`로 받는다.** 그 행동이 뒤에서 실패해 값이 취소돼도 연출은 이미 나가고, 연출에서 난 예외(사라진 오브젝트 등)가 행동까지 실패시킨다. 연출은 `SubscribeAfterAction`(2-7).
- **규칙 쪽 받는 함수가 던진다.** 행동을 통째로 취소한다(2-7). 플레이어에게 알릴 실패는 `throw Game.Fail(…)`, 건너뛰어도 되는 일은 던지지 말고 그냥 돌아간다. 모니터 행동 탭에 그 행동이 **실패**로 보이고, 마우스를 올리면 오류가 보인다.
- **상속으로 종류를 나눈다.** 받는 쪽·이벤트 받기는 형식이 정확히 같을 때만 받는다(2-7). 상속 대신 필드(`tags`, `kind`)를 두고 값 조건으로 거른다.
- **연출 쪽에서 값을 바꾼다.** `SubscribeAfterAction` 함수는 행동 밖이라 Undo 단위·서버 전송에서 빠진다. 그 일은 Trigger의 결과(Outcome)로 두거나, 이벤트를 쏜 행동 안에서 한다.
- **Dispose를 잊는다.** 씬이 바뀌어도 세션은 남아 있어, Dispose하지 않은 함수가 사라진 오브젝트를 계속 부른다.
- **결과가 같은 이벤트를 다시 쏜다.** 사슬이 16단계에서 끊기고 경고가 남는다("트리거 사슬이 16단계를 넘어 끊었습니다(…)"). 결과에서 이벤트를 쏠 때는 다른 이벤트를 쓴다.
- **모니터에서 쏘려면 매개변수 없는 생성자.** 생성자에 인자가 필요한 이벤트는 모니터의 이벤트 호출·**트리거로 달성** 버튼이 만들지 못한다.
- **Journal 문장을 바꾸고 싶으면** `IDescribable`을 구현한다(2-6).
