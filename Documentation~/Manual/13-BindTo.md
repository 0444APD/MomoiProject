# 13. `[BindTo]` — 항목 하나의 결과를 코드로 쓰기

**한 줄 요약**: `[BindTo(id)]`를 static 함수(또는 `Reward` 클래스)에 붙이면, 그 ID를 가진 DB 항목(도전과제·로그·액티브 효과·사용 기능이 붙은 인스턴스·개발 명령·상태 효과·패시브 효과)의 결과가 그 코드로 바뀐다. 코드가 대신하는 결과 칸은 에디터에서 잠기고, 연결된 코드 이름과 `cs 열기`만 보인다. 결과 Block 조합(DB)으로 안 되는, 그 항목만의 결과일 때만 쓴다(관례 0절 순서 3). 같은 어트리뷰트로 화면 코드(`GameScreen`)도 DB › 화면 항목에 잇는다(2-1).

**언제 쓰는가**

- 그 항목에만 있는 예외 로직: "첫 처치 도전과제는 처치한 유닛에게 경험치 50과 파티 골드 100", "전설의 포션은 체력을 다 채우고 상태 이상을 지운다".
- 로그의 기록 방식을 바꿀 때: "보스 처치는 10점, 일반 처치는 1점으로 센다".
- 에디터의 플레이어 데이터에 주기·대상에게 효과로 나타내기 어려운 결과.
- 규칙 함수를 거쳐야 하는 개발 명령: "적 모두 처치"는 피해 규칙을 거쳐야 처치 이벤트·로그가 게임과 같게 돈다.

원칙은 하나다: **예외 로직은 그 단위만 코드로 쓰고, 어느 항목에 쓸지는 코드에서 정한다.** Trigger(언제)는 그대로 에디터에서 조립하고, 결과(무엇을)만 코드가 맡는다.

이 문서에서 쓰는 용어:

| 용어 | 뜻 |
|---|---|
| Outcome | Trigger가 발동했을 때 하는 일(결과). `[BindTo]` 코드는 그 항목의 Outcome이 된다. |
| Trigger | 언제 발동하는지(→ `11-Triggers-Conditions.md`). `[BindTo]`는 Trigger를 바꾸지 않는다. |
| TriggerContext (컨텍스트) | 발동한 순간의 정보: `Session`·`Game`·`Owner`·`Event`·`IsUse`·`User`·`Picked` …(전체는 11장 4-2). |
| 연결 | `[BindTo(id)]`로 코드와 DB 항목을 잇는 것. |

---

## 1. 에디터에서 보이는 모습

`[BindTo]`는 에디터에서 만드는 것이 아니다. 코드를 쓰면 에디터가 따라 바뀐다.

1. 코드에 `[BindTo("firstBlood")]` 함수를 쓰고 Unity가 컴파일을 마친다.
2. `MomoiProject → DB`에서 그 ID의 항목(예: 도전과제 `firstBlood`)을 연다.
3. 결과 칸 자리에 다음이 보인다.
   - 안내: "코드로 쓴 결과가 이 항목에 연결되어 있습니다([BindTo]). 결과는 코드에서 고칩니다."
   - 연결된 코드마다 한 줄:
     - 함수면 굵은 이름 `클래스.함수`와 그 아래 **받는 이벤트: 형식 이름**(이벤트 매개변수가 없으면 "이벤트를 받지 않는 함수").
     - 클래스면 `[BlockInfo]` 이름(없으면 클래스 이름)과 전체 형식 이름 · 설명.
     - 오른쪽 **`cs 열기`** 버튼: 그 코드가 든 스크립트를 연다.
4. `+ 결과 추가`는 없다. 트리거 칸은 그대로 고칠 수 있다.

로그는 반영 규칙마다의 결과 칸이 모두 이 모습이 된다. 액티브 효과도 같다. 사용 기능이 붙은 인스턴스는 인스턴스 값 안의 사용 기능(`then`) 자리에 이 잠긴 보기가 나온다.

**잠기는 것은 코드가 대신하는 칸뿐이다.** 상태 효과의 걸릴 때·끝날 때 결과, 패시브 효과의 `rules`(발동 더)는 잠기지 않고 그대로 고친다. 어느 칸이 잠기는지와 결과 안의 결과는 2-11.

연결하기 전에 에디터에서 골라 둔 결과는 지워지지 않고 숨겨질 뿐이다. `[BindTo]`를 빼면 다시 보이고 다시 쓰인다.

---

## 2. 코드로 쓰는 법 — 문법 요소를 하나씩

### 2-1. `[BindTo("항목 ID")]`

```csharp
using GBible;

public static class AchievementLogic
{
    [BindTo("firstBlood")]
    static void FirstBlood(UnitKilled e, TriggerContext c)
    {
        c.Game.Find<Unit>(e.killerId).exp += 50;
    }
}
```

- 인자는 DB 항목의 ID다. 도전과제·로그·액티브 효과·개발 명령·상태 효과·패시브 효과는 그 항목의 ID, 사용 기능은 **인스턴스의 ID**(DB › 인스턴스의 항목 ID)다.
- 함수와 클래스에 붙는다. 함수가 든 클래스는 어디든 된다(static 클래스가 아니어도, 이름이 무엇이든). 프로젝트에서는 규칙 쪽 결과면 `Assets/MomoiProject/Code/Rules` 안에 둔다(관례 1절 — 규칙은 Unity API를 쓰지 않는다).
- 화면: `[BindTo(GameIds.Screen.X)] public class X : GameScreen`은 DB › 화면 항목에 화면 코드를 잇는다(`Assets/MomoiProject/Code/Client/Screens/`, 35장). 결과가 아니므로 결과 칸·`BoundOutcomes.Has`와는 상관없다.

### 2-2. ID는 `GameIds` 상수로

```csharp
using GBible;
using GBible.Ids;

public static class AchievementLogic
{
    [BindTo(GameIds.Achievement.FirstBlood)]
    static void FirstBlood(UnitKilled e, TriggerContext c) { /* … */ }
}
```

- `GameIds`(DB를 저장하면 저절로 생성, 네임스페이스 `GBible.Ids`)의 도전과제·로그·액티브 효과·인스턴스·개발 명령·상태 효과·패시브 효과·화면 ID는 `const string`이라 어트리뷰트 인자로 쓸 수 있다: `GameIds.Achievement.X`, `GameIds.Stat.X`, `GameIds.Ability.X`, `GameIds.Instance.X`, `GameIds.DebugCommand.X`, `GameIds.Status.X`, `GameIds.Passive.X`, `GameIds.Screen.X`(항목이 하나라도 있는 종류만 생긴다).
- **상수는 DB 항목이 생긴 뒤 Unity가 다시 컴파일해야 생긴다.** 새 항목과 `[BindTo]`를 같이 만들 때는 DB 반영 스크립트를 먼저 넣고 → Unity가 새로 읽어 `GameIds.cs`에 상수가 생긴 것을 본 뒤 → `[BindTo(GameIds.…)]` 코드를 쓴다(관례 15절). 상수가 없는 채로 쓰면 컴파일 오류다.
- 글자를 직접 쓰면 오타나 ID 이름 바꾸기가 연결을 끊는다(게임이 켜질 때 콘솔에 "[MomoiProject] [BindTo("id")]: 이 ID의 DB 항목이 없어 연결되지 않았습니다(코드 이름)." 경고). 상수를 쓰면 컴파일 오류로 먼저 드러난다.

### 2-3. 함수의 모양

| 조건 | 내용 |
|---|---|
| static | 필수(인스턴스 함수는 연결되지 않는다) |
| 반환 | `void` |
| 제네릭 | 안 된다 |
| 접근 수준 | 상관없다(`private`도 된다) |
| 매개변수 | 아래 넷 중 하나 |

| 매개변수 | 받는 것 | 쓰는 곳 |
|---|---|---|
| `()` | 없음 | 게임 상태와 상관없는 일(외부 SDK 호출 등) |
| `(이벤트형식 e)` | 받은 이벤트(행동 직전 이벤트로 발동하면 행동 직전 이벤트, `28-Attributes-Intercepts.md` 4-10) | 이벤트 값만 필요할 때 |
| `(TriggerContext c)` | 컨텍스트 | 사용 기능·액티브 효과·개발 명령(이벤트가 없다), 조건 충족으로 발동하는 도전과제 |
| `(이벤트형식 e, TriggerContext c)` | 둘 다 | 이벤트로 발동하고 세션도 만져야 할 때 |

`TriggerContext`는 마지막 자리에만 온다. `(TriggerContext c, UnitKilled e)`는 연결되지 않는다.

### 2-4. 받을 이벤트 = 첫 매개변수의 형식

```csharp
[BindTo("bossHunter")]
static void OnBoss(UnitKilled e, TriggerContext c) { /* e는 이미 UnitKilled */ }
```

- 트리거가 받은 이벤트(`c.Event`)가 그 형식(또는 그 형식을 상속한 형식)일 때만 함수를 부른다. 형 변환을 직접 쓸 필요가 없다.
- 다르면 부르지 않고 Journal에 경고를 남긴다(출처 `Outcomes`): "[BindTo] AchievementLogic.OnBoss은(는) UnitKilled 이벤트를 받는데 'bossHunter'의 트리거는 (받은 것)입니다. 실행하지 않았습니다." 받은 것 자리에는 다른 이벤트 이름, "이벤트 없음"(조건 충족으로 발동), "사용(이벤트 없음)"(`Game.Use`)이 들어간다.
- 그래서 사용 기능·액티브 효과, 조건만으로 된 도전과제에는 이벤트 매개변수를 두지 않는다.

### 2-5. `TriggerContext` 멤버

`c.Session`(세션) · `c.Game`(`GameApi` — `Find`·`All`·`Random`·`Events` …) · `c.Owner`(연결된 항목의 ID — 한 함수를 여러 항목에 연결했을 때 어느 것인지) · `c.Event`(받은 이벤트, `object`) · `c.IsUse`(`Game.Use`로 발동했으면 `true`) · `c.User`(사용자, 상태 효과·패시브 효과면 가진 쪽) · `c.Picked`(고른 대상들) · `c.Power`(효과 배율). 뜻과 나머지 멤버는 11장 4-2.

### 2-6. 여러 항목에 연결 — `[BindTo]`를 여러 개

```csharp
[BindTo("potionSmall")]
[BindTo("potionLarge")]
static void Potion(TriggerContext c)
{
    var heal = c.Owner == "potionLarge" ? 80 : 30;
    if (c.User is Unit u) u.hp = Math.Min(u.maxHp, u.hp + heal);
}
```

- 같은 함수가 각 항목의 결과가 된다. `c.Owner`로 어느 항목인지 가른다.
- 항목마다 다른 숫자(회복량·골드)는 코드에 표로 두지 않고 그 항목의 칸(DB 항목은 `c.Game.Entry<T>(c.Owner)`, 인스턴스 원본은 `c.Session.Instances(c.Owner)` — 읽기만)이나 밸런싱 값(`Game.Balance`)에서 읽는다(관례 18절). 위 예의 30·80은 문법을 보이려고 적었다.
- 반대로 한 ID에 코드를 둘 이상 붙이지 않는다. 모두 실행되지만 순서가 정해지지 않아, 게임이 켜질 때 경고가 남는다(2-8). 한 함수로 합친다.

### 2-7. `Reward` 클래스에 붙이기

```csharp
using System;
using GBible;

[Serializable, BlockInfo("드래곤 슬레이어 보상", Category = "보상")]
[BindTo("dragonSlayer")]
public class DragonSlayerReward : Reward
{
    public int gold = 5000;                       // 필드 초기값이 그대로 쓰인다
    public string title = "드래곤 슬레이어";

    public override RewardEffect Effect => RewardEffect.Reversible;
    public override string Describe(INameResolver names) => $"골드 {gold}, 칭호 {title}";
    public override void Grant(GameSession session)
    {
        var party = session.Data.Get<Party>("party");
        party.gold += gold;
        party.titles.Add(title);
    }
}
```

- 클래스는 `Outcome`(보통 `Reward`)을 상속하고, abstract가 아니고, 매개변수 없는 생성자가 있어야 한다. 연결된 항목마다 새 인스턴스를 만들어 쓴다.
- 결과 칸이 잠기므로 필드 값은 에디터에서 고칠 수 없다. **필드 초기값**이 쓰인다. 결과 코드를 클래스로 나누고 싶거나 `Validate`·`Describe`를 쓰고 싶을 때 쓴다.
- `Context`(protected)로 컨텍스트를 본다(이벤트 형식 거르기는 직접: `Context?.Event is UnitKilled e`).
- 이 클래스도 `Reward`라서 다른 항목의 `+ 결과 추가` › 코딩한 결과 목록에 나온다. `[Serializable]`·`[BlockInfo]`를 붙여 둔다.

### 2-8. 연결 실패 알림

게임이 켜질 때(`GBibleRuntime.Start`) 모든 `[BindTo]`를 한 번 모으고, 모양이 맞지 않는 것은 연결하지 않고 콘솔에 경고를 남긴다.

| 경고 | 원인 |
|---|---|
| `[MomoiProject] [BindTo] '형식.함수': static 함수여야 합니다.` | 인스턴스 함수 |
| `… 반환이 void이고 제네릭이 아닌 함수여야 합니다.` | 반환값이 있거나 제네릭 |
| `… 매개변수는 없음 / (이벤트) / (TriggerContext) / (이벤트, TriggerContext) 중 하나여야 합니다.` | 매개변수 모양 |
| `[MomoiProject] [BindTo] '형식': 클래스에 붙일 때는 abstract가 아니고 매개변수 없는 생성자가 있어야 합니다(결과는 Reward, 화면은 GameScreen을 상속).` | 클래스 조건 |
| `[MomoiProject] [BindTo("id")]가 N곳에 붙어 있습니다(…). 모두 실행되고 순서는 정해지지 않습니다. 하나로 합치세요.` | 한 ID에 코드가 둘 이상(2-6) |
| `[MomoiProject] [BindTo("id")]: 이 ID의 DB 항목이 없어 연결되지 않았습니다(…).` | ID 오타·지운 항목(2-2) |

서버·테스트처럼 `GBibleRuntime` 없이 세션을 만들면, 처음 연결을 찾을 때(항목을 처음 `Define`할 때) 모은다. 문제 목록은 `BoundOutcomes.Problems`(글자 리스트)를 직접 읽어 출력한다.

### 2-9. `BoundOutcomes` — 연결 목록 API

| 멤버 | 뜻 |
|---|---|
| `BoundOutcomes.Has(id)` | 그 ID에 **결과로** 연결된 코드가 있는지(화면 코드는 세지 않는다) |
| `BoundOutcomes.For(id)` | 연결된 코드들(`BoundCode`: `Type`, `Method`, `EventType`, `Name`, `IsOutcome`) |
| `BoundOutcomes.Create(id)` | 그 ID의 결과 인스턴스들(`List<Outcome>`) |
| `BoundOutcomes.ClassFor(id, baseType)` | 결과가 아닌 연결 클래스 중 `baseType`을 상속한 첫 형식(화면 코드 등, 없으면 `null`) |
| `BoundOutcomes.Ids` | 연결된 ID 전체 |
| `BoundOutcomes.Problems` | 연결하지 못한 코드의 설명 |
| `BoundOutcomes.Reset()` | 다시 모으게 비운다 |

### 2-10. 실행 방식

- 함수는 게임이 켜질 때 대리자(delegate)로 한 번 만들어 둔다. 실행 중에는 리플렉션 없이 대리자를 부른다.
- 결과는 다른 결과와 같은 길로 돈다: 꺼낸 플레이어 데이터 객체를 그냥 고치면 기록된다.
  - 도전과제: 달성 순간에 실행되고 Undo 이력에 넣지 않는다(행동 안에서 발동하면 그 바깥 행동이 성공한 뒤 — 실패·미리보기면 실행하지 않는다, 14장 2-3).
  - 로그: 반영 규칙이 발동할 때마다 실행된다(행동 안이면 그 행동의 Undo 단위).
  - 액티브 효과·사용 기능: `Game.Use` 행동 안에서 실행된다(Undo 한 번에 통째로 되돌아간다).
  - 개발 명령: 모니터 › 명령 탭의 버튼을 누르면 행동 하나("[개발] 버튼 이름") 안에서 실행된다(Undo된다).
  - 상태 효과: 걸려 있는 동안 트리거가 발동할 때 가진 쪽마다 실행된다(`c.User` = 가진 쪽, `c.Owner` = 상태 효과 ID. 이벤트 받기·행동 직전 이벤트면 그 이벤트를 쏜 행동 안, `29-Statuses-Schedules-Choices.md` 4-3).

### 2-11. 연결이 적용되는 기능

| 기능 | ID | 연결되면 |
|---|---|---|
| 도전과제 | 도전과제 ID | 에디터에서 고른 결과(예전 보상 포함) 대신 연결된 코드 |
| 로그 | 로그 ID | **모든** 반영 규칙에서 '로그에 기록' 대신 연결된 코드. 기록은 코드가 직접(`c.Session.Stats.Report(c.Owner, 값)`) |
| 액티브 효과 | 액티브 효과 ID | 에디터에서 고른 결과 대신 연결된 코드 |
| 사용 기능이 붙은 인스턴스 | 인스턴스 ID | 사용 기능의 결과 대신 연결된 코드(`Game.Use(인스턴스 ID, …)`) |
| 개발 명령(DB › 개발 › 개발 명령) | 명령 ID | 에디터에서 조립한 결과 대신 연결된 코드(→ `22-Monitor-Debug.md` 7절) |
| 상태 효과(DB › 상태 효과) | 상태 효과 ID | `outcomes`(걸려 있는 동안의 결과) 대신 연결된 코드. `onApply`(걸릴 때 결과)·`onExpire`(끝날 때 결과)는 대신하지 않고 DB에서 고친 그대로 돈다(→ `29-Statuses-Schedules-Choices.md` 3-1·4-3) |
| 패시브 효과(DB › 패시브 효과) | 패시브 효과 ID | `outcomes`(결과) 대신 연결된 코드. `rules`(발동 더)의 줄마다 결과는 대신하지 않고 DB에서 고친 그대로 돈다 |
| 프로젝트 기능(퀘스트 등) | 항목 ID | 런타임 등록 코드가 `BoundOutcomes`를 확인할 때만(→ `17-Custom-Features.md` 2-8) |

**코드가 대신하지 않는 칸 — `[OutcomeSlot(…, BindTo = false)]`.** 한 항목에 결과 칸이 여럿이고 그중 일부만 코드가 대신한다면, 대신하지 않는 칸의 `[OutcomeSlot]`에 `BindTo = false`를 준다. 에디터는 그 ID에 `[BindTo]`가 있어도 그 칸을 잠그지 않고 그대로 고치게 한다. 상태 효과의 걸릴 때·끝날 때 결과와 패시브 효과의 발동 더(`rules`)가 이 칸이다. 결과 안에 든 결과 목록(선택 요구 › 가중치 표에서 고르기의 '고른 뒤 결과' …)은 위로 가장 가까운 결과 칸의 `BindTo`를 따르고(그 칸이 잠기면 같이 잠긴다), 위에 결과 칸이 없으면 잠기지 않는다.

```csharp
[FieldLabel("결과"), SerializeReference, OutcomeSlot(OutcomeKinds.All)] public List<Outcome> outcomes = new List<Outcome>();          // [BindTo]가 대신한다(잠김)
[FieldLabel("끝날 때 결과"), SerializeReference, OutcomeSlot(OutcomeKinds.Targeted | OutcomeKinds.Coded, BindTo = false)]
public List<Outcome> onExpire = new List<Outcome>();                                                                                       // 늘 DB에서 고친다
```

`BindTo = false`는 에디터의 잠금만 정한다. 실행할 때 어느 칸을 코드로 바꿀지는 그 기능의 정의·등록 코드가 정한다: 대신할 칸에만 `BoundOutcomes.Has(id) ? BoundOutcomes.Create(id) : outcomes`를 쓴다. 템플릿에서 들인 상태 효과·패시브 효과 클래스(`Assets/MomoiProject/Code/Rules/Elements/`)는 `ToDefinition`에서 이미 그렇게 하므로(액티브 효과는 엔진이 한다), 그 클래스에 결과 칸을 더할 때도 이 두 가지를 맞춘다. 프로젝트 기능은 17장 2-8.

---

## 3. 상황별 예제

예제는 짧게 쓰려고 ID를 글자로, 숫자(경험치 50·골드 100)를 코드에 적었다. 프로젝트 코드는 ID에 `GameIds` 상수(2-2), 바꿀 숫자에 그 항목의 칸이나 밸런싱 값(2-6)을 쓰고, 피해·회복은 규칙 함수(행동 직전 이벤트 자리, 관례 4절)를 거친다(3-10).

예제는 다음 형식을 쓴다.

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using GBible;

[Serializable] public class Unit { public string id; public string team; public int slot; public int hp = 30, maxHp = 30, exp; }
[Serializable] public class Party
{
    public int gold;
    public List<string> titles = new List<string>();
    public Dictionary<string, int> bag = new Dictionary<string, int>();   // 인스턴스 ID → 개수
}

[GameEvent("유닛 처치")]
public class UnitKilled
{
    public string killerId, victimId;
    public int exp, combo;
    public List<string> tags = new List<string>();
    public bool IsBoss => tags.Contains("boss");
}
```

### 3-1. 도전과제 보상: 처치한 유닛에게 경험치 + 파티 골드

**에디터**: DB › 도전과제 `firstBlood`의 트리거를 `유닛 처치 받기`로 둔다.

```csharp
public static class AchievementRewards
{
    [BindTo("firstBlood")]
    static void FirstBlood(UnitKilled e, TriggerContext c)
    {
        if (c.Game.Find<Unit>(e.killerId) is Unit killer) killer.exp += 50;
        c.Session.Data.Get<Party>("party").gold += 100;
    }
}
```

처치한 유닛은 이벤트의 id로 찾는다(`Game.Find<Unit>`은 `id` 필드가 같은 `Unit`을 플레이어 데이터 전체에서 찾는다).

### 3-2. 조건으로 달성하는 도전과제: 컨텍스트만

**에디터**: `richParty`의 트리거를 `플레이어 데이터 검사(party: gold 10000 이상)`으로 둔다.

```csharp
[BindTo("richParty")]
static void RichParty(TriggerContext c)
{
    c.Session.Data.Get<Party>("party").titles.Add("부자");
    c.Game.Events.Publish(new TitleEarned { title = "부자" });   // 연출용 이벤트
}

[GameEvent("칭호 획득")] public class TitleEarned { public string title; }
```

이 도전과제에 `(UnitKilled e, …)` 함수를 연결하면 이벤트가 없어서 실행되지 않고 경고만 남는다.

### 3-3. 처치 로그를 직접 기록: 보스는 10점

**에디터**: DB › 로그 `huntScore`(합계), 반영 규칙 `유닛 처치 받기`.

```csharp
public static class LogRules
{
    [BindTo("huntScore")]
    static void HuntScore(UnitKilled e, TriggerContext c) =>
        c.Session.Stats.Report(c.Owner, e.IsBoss ? 10 : 1);     // c.Owner = "huntScore"
}
```

- 연결되면 '로그에 기록'이 돌지 않으므로 기록은 코드가 `Report`로 한다. `Report`는 로그의 집계 방식(합계·최고치 …)을 따른다.
- 반영 규칙이 여러 개면 규칙마다 이 함수가 돈다.

### 3-4. 같은 처치를 다른 로그에 함께 기록

```csharp
[BindTo("killsByTeam")]
static void KillsByTeam(UnitKilled e, TriggerContext c)
{
    c.Session.Stats.Report(c.Owner, 1);
    if (c.Game.Find<Unit>(e.killerId)?.team == "ally") c.Session.Stats.Report("allyKills", 1);   // 다른 로그도 정의되어 있어야 한다
}
```

### 3-5. 포션 사용: 사용자 회복 + 개수 줄이기

**에디터**: 아이템 클래스에 사용 기능 필드를 두고(→ `16-Abilities-Usable.md`), DB › 인스턴스에 `potion`을 만들어 `+ 기능` → `usable`을 붙인다.

```csharp
public static class ItemUses
{
    [BindTo("potion")]
    static void Potion(TriggerContext c)
    {
        if (!(c.User is Unit user)) return;
        user.hp = Math.Min(user.maxHp, user.hp + 30);
        var bag = c.Session.Data.Get<Party>("party").bag;
        bag[c.Owner] = bag.TryGetValue(c.Owner, out var n) ? n - 1 : 0;   // c.Owner = "potion"
    }
}

// 게임 코드
// GBibleRuntime.Game.Use("potion", hero);        // 행동 하나: Undo 한 번에 회복·개수가 함께 돌아간다
```

"가방에 있을 때만" 같은 사용 조건은 Trigger(사용 AND 조건)에 둔다. 연결은 결과만 바꾼다.

### 3-6. 포션 여러 종류를 한 함수로

2-6과 같다: `[BindTo]`를 여러 개 붙이고 `c.Owner`로 가른다. 포션마다 다른 회복량은 코드의 표가 아니라 그 인스턴스의 필드(`c.Session.Instances(c.Owner)`)에서 읽는다 — 개발자가 DB에서 바꾼다(관례 18절).

### 3-7. 액티브 효과: 고른 대상에게 튕기는 번개

```csharp
[BindTo(GameIds.Ability.ChainLightning)]
static void ChainLightning(TriggerContext c)
{
    var damage = c.Game.Balance(GameIds.Balance.ChainLightningDamage) * c.Power;   // 숫자는 밸런싱 값, 효과 배율을 곱한다
    var falloff = c.Game.Balance(GameIds.Balance.ChainLightningFalloff) / 100;     // % (0~100) — 다음 대상은 이만큼
    foreach (var target in c.Picked.OfType<Unit>())
    {
        Rules.Damage(c.Game, c.User as Unit, target, (int)damage);              // 피해는 규칙 함수로(행동 직전 이벤트 자리를 지난다)
        damage *= falloff;
    }
}

// Game.Use(GameIds.Ability.ChainLightning, caster, orc1, orc2, orc3);
```

### 3-8. 이벤트 값만 필요한 함수 / 매개변수 없는 함수

```csharp
[BindTo("comboMaster")]
static void ComboMaster(UnitKilled e) => UnityEngine.Debug.Log($"{e.combo}연속 처치!");   // 이벤트만

[BindTo("tutorialDone")]
static void TutorialDone() => Analytics.Send("tutorial_done");                           // 게임 상태와 상관없는 일(프로젝트의 분석 코드)
```

매개변수가 없으면 세션에 닿을 수 없다. 플레이어 데이터를 바꿔야 하면 `TriggerContext`를 받는다.

### 3-9. 단계별 도전과제를 한 함수로

2-6과 같다: `hunter1`·`hunter2`·`hunter3`에 한 함수를 붙이고 `c.Owner`로 단계를 가른다(ID 글자를 잘라 숫자를 만들지 말고, 단계마다 다른 보상은 결과 Block `플레이어 데이터에 주기`로 DB에서 조립하는 편이 먼저다 — 관례 0절). 각 도전과제의 트리거(처치 수 10·100·1000)는 에디터에서 따로 정한다.

### 3-10. 개발 명령: 적 모두 처치

**에디터**: DB › 개발 › 개발 명령에 `killAll`(버튼 이름 "적 모두 처치")을 만든다. 결과 칸은 비워 둔다(연결되면 잠긴다).

```csharp
using GBible.Ids;

public static class DebugCode
{
    [BindTo(GameIds.DebugCommand.KillAll)]
    static void KillAll(TriggerContext c)
    {
        var game = c.Game;
        foreach (var enemy in Rules.LivingEnemies(game).ToArray()) Rules.Damage(game, game.Data().Run.hero, enemy, 9999);   // 피해 규칙을 거친다
    }
}
```

- 개발 명령에는 이벤트가 없으므로 `(TriggerContext c)`나 `()`를 쓴다.
- 값 하나 바꾸기(골드 +100)·이벤트 쏘기(한 턴 30 피해)처럼 결과 Block으로 조립할 수 있는 명령은 코드 없이 DB에서 만든다(`22-Monitor-Debug.md` 7절).

---

## 4. 주의할 점 / 자주 하는 실수

- **ID 오타·이름 바꾸기.** 연결할 항목이 없는 ID는 연결되지 않고, 게임이 켜질 때 콘솔에 경고가 한 번 뜬다. `GameIds` 상수를 쓴다(2-2).
- **사용 기능·액티브 효과에 이벤트 매개변수.** `Game.Use`에는 이벤트가 없으므로 `(UnitKilled e, …)` 함수는 실행되지 않고 경고만 남는다(2-4). `(TriggerContext c)`를 쓴다.
- **사용 기능은 인스턴스 ID로.** `Item.id` 같은 클래스 필드가 아니라 DB › 인스턴스 항목의 ID에 연결한다. 그 인스턴스의 값에 사용 기능 필드가 붙어 있어야(`null`이 아니어야) 쓸 수 있는 항목으로 등록된다.
- **로그에 연결하고 Report를 잊는다.** 연결된 로그는 자동 기록이 멈춘다. 함수가 `c.Session.Stats.Report(c.Owner, 값)`을 불러야 값이 오른다.
- **결과 칸이 왜 잠겼는지 모른다.** 다른 사람이 쓴 `[BindTo]`가 그 ID에 연결되어 있다. `cs 열기`로 코드를 확인한다. 연결을 빼면 예전 결과가 다시 쓰인다.
- **상태 효과에 연결했는데 걸릴 때·끝날 때 결과가 그대로 돈다.** 정상이다. 코드가 대신하는 것은 `outcomes`뿐이다(2-11). 그 두 칸도 코드로 쓰려면 칸에 코딩한 결과 Block(`Reward`)을 둔다.
- **`cs 열기`가 파일을 못 찾는다.** 스크립트 파일 이름에 클래스 이름이 들어 있어야 찾는다(`AchievementRewards` 클래스 → `AchievementRewards.cs`). 못 찾으면 콘솔에 "스크립트 파일을 찾지 못했습니다" 경고가 뜬다.
- **함수 안의 static 상태.** 세션은 여러 개일 수 있다(멀티플레이 서버·시뮬레이션). 바뀌는 값은 static 변수가 아니라 플레이어 데이터에 둔다.
- **함수 안에서 행동을 실행한다.** 액티브 효과 사용·로그 반영은 이미 행동 안에서 돌 수 있다. 그 안에서 `Game.Do`·`Game.Use`를 부르면 바깥 행동의 일부로 실행된다(Undo는 바깥 행동 하나).
- **이벤트를 다시 쏜다.** 연결된 결과가 자기 Trigger의 이벤트를 쏘면 사슬이 16단계에서 끊긴다.
- **매개변수 순서.** `TriggerContext`는 마지막. 순서가 바뀌면 연결되지 않고 시작할 때 경고가 뜬다.
