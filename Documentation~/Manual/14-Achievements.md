# 14. 도전과제

**한 줄 요약**: 도전과제는 **Trigger가 처음 참이 되는 순간 달성되고 결과를 실행하는** 한 번짜리 기능이다. 달성 상태는 프로필(메타 구역)에 저장되고, 달성과 그 결과는 Undo(행동 되돌리기)로 취소되지 않는다. 행동 안에서 발동하면 그 행동이 성공한 뒤에 달성하고, 실패하면 달성하지 않는다(2-3·2-4).

> 도전과제 탭(사이드바 **개발** 그룹)은 항목이 없으면 숨는다 — 프로젝트가 항목을 더하면(AI의 DB 반영 스크립트) 나타난다.

**언제 쓰는가**

- "처치 100회", "골드 10000 모으기", "보스를 노히트로 처치", "10연속 처치"처럼 한 번 이루면 끝나는 목표.
- 달성 순간 보상(골드·칭호·장비)을 주거나, 플랫폼 업적을 보내거나, 다른 도전과제·기능을 여는 조건으로 쓸 때.

이 문서에서 쓰는 용어:

| 용어 | 뜻 |
|---|---|
| Trigger | 언제 달성되는지. 이벤트 받기·조건 충족을 AND·OR·NOT으로(→ `11-Triggers-Conditions.md`). |
| Outcome | 달성했을 때 하는 일(결과, → `12-Outcomes.md`). |
| 로그 | 이벤트를 세는 값(처치 수 등, → `15-Stats-Logs.md`). 도전과제는 보통 로그를 조건으로 본다. |
| Undo | 마지막 행동 되돌리기. |
| 메타 구역 | 판이 바뀌어도 남는 플레이어 데이터 구역(프로필 `profile.json`에 저장). |

---

## 1. 에디터에서 쓰는 법

### 1-1. 도전과제 만들기

도전과제는 로직 쪽 종류라 항목 추가·삭제와 트리거·결과 Block 조립이 DB에서 잠겨 있다(21장 3-1). **AI가 DB 반영 스크립트로 만든다**(관례 15절): `EditorDatabase.Staging.Add(typeof(AchievementData), "firstBlood")`로 항목을 만들고 아래 칸에 2절·3절과 같은 형식의 Block을 넣는다. ID는 lowerCamelCase와 점(`firstBlood`, `hunt.hunter100`), 코드에서는 `GameIds.Achievement.…`. 개발자는 `MomoiProject → DB` → 사이드바 **개발** 그룹의 **도전과제** 탭에서 읽고 문장 안의 값(`kills >= 100`의 100 등)만 바꾼다.

| 칸 | 툴팁 | 내용 |
|---|---|---|
| `icon` | 아이콘 | 스프라이트(UI용) |
| `trigger` | 트리거 | 언제 달성되는지. 고를 수 있는 것: 이벤트 받기 · 조건 충족(`비교`(로그 값) · `플레이어 데이터 검사` · `도전과제 달성` · 프로젝트 조건 Block), AND·OR·NOT(→ `11-Triggers-Conditions.md`) |
| `outcomes` | 결과 | 달성했을 때 할 일. 모든 결과 종류(→ `12-Outcomes.md`) |

### 1-2. 트리거 조립

DB 상세에서 트리거는 문장으로 보인다(예: 조건 `비교` = `{stat} {op} {value}` → `kills >= 100`). 조립 화면(`+ 블럭` · `+ 논리 연산자` · `+ 괄호`)은 구조 편집을 열었을 때만 보인다(11장 2절).

### 1-3. 결과 고르기

결과 칸 대신 코드로 쓰려면 `[BindTo]`(→ `13-BindTo.md`). 연결되면 이 칸은 잠기고 연결된 코드만 보인다.

상세 위쪽 요약에 "트리거 …"·"결과 …" 두 줄이 보인다. 트리거가 없으면 "트리거가 없습니다(언제 달성되는지)." 오류가 뜨고 빌드가 막힌다.

### 1-4. 모니터에서 확인하기(플레이 중)

`MomoiProject → 모니터` → **도전과제** 탭: 카드마다 배지 `달성` / `진행 중`, 진행 중이면 진행도(`37 / 100`, 조건 충족 잎이 진행도를 낼 때). 카드를 누르면 아래 작업 줄에 **트리거로 달성**(게임과 같은 길로 이벤트를 쏜다 — Undo하면 로그는 돌아가고 달성은 남는다) · **강제 달성**(조건·이벤트 없이 달성만, 결과는 실행된다) · **초기화**(달성한 카드의 달성 표시만 지운다)가 뜬다. 자세히는 `22-Monitor-Debug.md` 4절.

---

## 2. 코드로 쓰는 법 — 문법 요소를 하나씩

### 2-1. `AchievementDefinition`

```csharp
var def = new AchievementDefinition
{
    Id = "hunter100",
    DisplayName = "사냥꾼",
    When = new ConditionTrigger { condition = new StatCompareCondition { stat = "kills", op = CompareOp.AtLeast, value = 100 } },
};
def.Outcomes.Add(new PlayerDataReward { key = "party", path = "gold", amount = 500 });
session.Achievements.Define(def);
```

| 멤버 | 뜻 |
|---|---|
| `Id` | 필수. 비면 예외 |
| `DisplayName` | 표시 이름(Journal·모니터·`AchievementUnlocked`). DB 항목은 ID가 표시 이름이다 |
| `When` (`Trigger`) | 언제 달성되는지. `null`이면 달성될 수 없고 Journal에 경고 |
| `Outcomes` (`List<Outcome>`) | 결과 |
| `Condition` (짧은 형태) | 조건 하나로 된 Trigger를 읽고 쓴다(`When = new ConditionTrigger { condition = … }`과 같다) |
| `Rewards` (짧은 형태, `List<Reward>`) | 코딩한 결과만. `Outcomes` 뒤에 실행된다 |

게임에서는 런타임이 시작할 때 DB의 모든 도전과제(`AchievementData.ToDefinition()`)를 정의한다. 게임의 도전과제는 DB 항목으로 두고(1-1 — 개발자가 읽고 값을 바꾼다), `Define`을 직접 부르는 것은 규칙 테스트·DB 밖에서 만드는 도전과제용이다. DB 항목의 `trigger`·`outcomes`에 넣는 Block은 여기의 `When`·`Outcomes`와 같은 형식이다.

### 2-2. `session.Achievements` — `AchievementSystem`

| 멤버 | 뜻 |
|---|---|
| `Define(definition)` | 정의하고 감시를 시작한다(이미 달성했으면 감시하지 않는다). 같은 ID면 바꾼다 |
| `IsDefined(id)` / `Find(id)` / `Definitions` | 정의 확인·찾기·전체 |
| `IsUnlocked(id)` | 달성했는지 |
| `TryGetProgress(id, out current, out target)` | 진행도(Trigger의 조건 잎에서. AND는 가장 덜 된 것, OR는 가장 많이 된 것) |
| `Unlock(id, reason = null)` | 직접 달성(트리거와 상관없이). 정의되지 않은 ID면 예외. 이미 달성했으면 아무것도 하지 않는다. 행동 안에서 부르면 그 행동이 성공한 뒤에 달성하고, 미리보기 안에서는 하지 않는다(2-3) |
| `ResetProgress(id)` | 달성 표시를 지우고 다시 감시(개발용) |
| `EvaluateAll()` | 모든 조건을 지금 값으로 다시 평가(불러온 뒤 소급 달성) |
| `Flush()` | 바뀐 값에 걸린 조건을 평가(행동 끝·프레임 끝에 자동) |
| `event Action<AchievementDefinition> Unlocked` | 달성한 뒤 불린다(C# 이벤트) |

### 2-3. 달성 순간에 일어나는 일

**언제 달성하나** — 트리거가 발동한 곳에 따라 다르다.

- **행동 안에서 발동하면 바깥 행동이 성공한 뒤에 달성한다.** 이벤트는 보통 행동 안에서 쏘므로 이벤트 받기로 발동한 도전과제가 이렇다. 행동이 성공하면 그 안에서 발동한 달성을 발동한 차례대로 한다(Journal의 달성 줄은 그 이벤트를 원인으로 잇는다).
- **행동이 실패하면 달성하지 않는다.** 기다리던 달성은 버려지고 트리거는 그대로 감시한다. 행동 안에서 일부만 되돌린 이벤트(실패한 이벤트 예약, 29장 2-5)로 발동한 것도 같다. 없던 일이 된 실행이 되돌릴 수 없는 달성을 남기지 않게 한다. 버리는 것은 그 실패한 행동에서 발동한 달성뿐이다: 성공한 행동 뒤에 받는 쪽(`SubscribeAfterAction` 연출·화면) 안에서 한 미리보기·`CanDo`나 실패한 다른 행동은 그 성공한 행동의 달성을 버리지 않는다.
- **미리보기·`CanDo`(33장)는 달성하지 않고 트리거도 쓰지 않는다.** 해 보기만 한 실행이 끝나면 감시가 그대로 남는다.
- 조건 충족으로 발동하면(행동이 끝난 뒤의 평가) 그 자리에서 달성한다.

**달성할 때**

1. 감시를 그만둔다(도전과제는 한 번만 발동한다).
2. Journal에 "달성: (이름) (이유)"를 남긴다. 이유는 "(이벤트 이름) 받음" 또는 "조건 충족".
3. **Undo 이력 밖에서**(`session.Data.Permanent()` 안) 다음을 한다:
   1. 달성 표시 `achievement:(ID)`를 참으로(메타 구역).
   2. 결과를 차례로 실행한다(컨텍스트는 발동한 순간: 이벤트로 발동했으면 `Event`가 그 이벤트 — 행동이 끝난 뒤로 미룬 달성도 같다).
   3. `AchievementUnlocked` 이벤트를 쏜다(`Id`, `DisplayName`).
4. `Unlocked` C# 이벤트를 부른다.

미룬 달성의 결과가 실패(예외)해도 행동은 이미 성공했으므로 실패로 바뀌지 않는다. Journal에 오류 "도전과제 '…'을(를) 달성하다 오류가 났습니다: …"를 남기고 다음 달성으로 넘어간다.

### 2-4. Undo와 되감기에서의 동작

- 달성과 결과는 Undo 이력에 넣지 않는다. 그래서 Undo해도 **달성과 보상은 남는다.**
- Undo로 돌아가는 것은 그 행동이 바꾼 값(로그·소지품)이다. 같은 행동을 다시 해서 조건을 다시 채워도 이미 달성했으므로 보상이 다시 나오지 않는다(Undo로 보상을 반복해 받을 수 없다).
- 스냅샷 방식(실시간 게임)으로 되감으면, 그 구간에 달성한 도전과제의 달성 표시와 결과를 다시 적용한다. 결과는 달성할 때의 순간으로 낸다(이벤트로 발동했으면 `Context.Event`가 그 이벤트 — 이벤트 값을 쓰는 결과·`[BindTo]` 함수가 같은 값을 다시 준다).
- 달성 표시는 메타 구역이라 프로필에 저장되고, 새 판(`NewRun`)에도 남는다.
- **프로필을 불러오면 감시를 불러온 달성 상태에 맞춘다**(`session.ImportMeta` — 테스트 상황 불러오기, 서버·테스트 코드). 불러온 프로필에서 달성한 것은 감시를 그만두고, 이 세션에서 달성했지만 불러온 프로필에서는 아직인 것은 다시 감시한다. 다시 감시한 것은 다음 평가(Flush)에서 지금 값으로 보므로 이미 조건을 넘었으면 소급 달성한다.

### 2-5. 달성 표시를 데이터 키로 읽기

```csharp
bool done = session.Data.GetBool(PlayerDataKeys.Achievement("hunter100"));   // = session.Achievements.IsUnlocked("hunter100")
```

조건 Block `도전과제 달성`(`AchievementUnlockedCondition`)은 이 키를 본다. 다른 도전과제의 조건으로 쓰면 연쇄 달성이 된다.

---

## 3. 상황별 예제

예제는 다음 형식을 쓴다.

```csharp
using System;
using System.Collections.Generic;
using GBible;

[Serializable] public class Unit { public string id; public string team; public int hp = 30, maxHp = 30, exp; }
[Serializable] public class Party { public int gold; public List<string> titles = new List<string>(); }

[GameEvent("유닛 처치")]
public class UnitKilled
{
    public string killerId, victimId;
    public int exp, combo, damageTaken;
    public List<string> tags = new List<string>();
    public bool IsBoss => tags.Contains("boss");
}
```

### 3-1. 첫 처치: 골드 100

**에디터**: `trigger` = `+ 블럭` → `이벤트 받기` → `유닛 처치`. `outcomes` = `party › gold` 증감 `100`.

**코드**:

```csharp
var firstBlood = new AchievementDefinition
{
    Id = "firstBlood",
    DisplayName = "첫 처치",
    When = new EventTrigger { eventType = typeof(UnitKilled).FullName },
};
firstBlood.Outcomes.Add(new PlayerDataReward { key = "party", path = "gold", amount = 100 });
session.Achievements.Define(firstBlood);
```

### 3-2. 처치 100회(로그 + 비교)

1. DB › 로그 `kills`: 범위 `누적`, 집계 `합계`, 반영 규칙 `유닛 처치 받기`(반영할 값 비움 = 횟수).
2. DB › 도전과제 `hunter100`: `trigger` = `비교`(`kills >= 100`).

모니터 카드에 `37 / 100` 같은 진행도가 보인다. 처치 행동이 끝날 때(Flush) 조건이 다시 평가되어 100번째 처치 행동 직후 달성된다.

### 3-3. 10연속 처치

**방법 A — 이벤트 값으로**: 처치 이벤트에 연속 처치 수(`combo`)를 실어 쏜다(→ `10-Events.md` 3-2).

`trigger` = `이벤트 받기`(유닛 처치) → 이벤트 조건 `+ 블럭` → `필드 검사` → `combo` `이상` `10`.

```csharp
When = new EventTrigger
{
    eventType = typeof(UnitKilled).FullName,
    filter = new FieldFilter { path = "combo", op = FieldOp.AtLeast, value = "10" },
},
```

**방법 B — 최고 기록 로그로**: 로그 `bestCombo`(집계 `최고치`, 반영 규칙 `유닛 처치 받기`, 반영할 값 `combo`)를 두고 `trigger` = `비교(bestCombo >= 10)`. 진행도(`7 / 10`)가 보이고, 이미 기록이 있는 프로필에서 도전과제를 새로 추가해도 시작할 때 소급 달성된다.

### 3-4. 노히트 보스 처치

`trigger` = `이벤트 받기`(유닛 처치) + 이벤트 조건 `IsBoss 참` **AND** `damageTaken 일치 0`(같은 이벤트 받기의 조건 칸 안에서 AND).

### 3-5. 골드 100 이상 가진 채 보스 처치(이벤트 AND 조건)

`trigger` = `이벤트 받기(유닛 처치, IsBoss 참)` **AND** `플레이어 데이터 검사(party: gold 100 이상)`.

골드가 100 미만일 때의 보스 처치로는 달성되지 않고, 나중에 골드가 100이 되어도 그 순간에는 이벤트가 없으므로 달성되지 않는다. 골드가 100 이상인 상태에서 **다음** 보스 처치를 받을 때 달성된다.

### 3-6. 처치 10회째에 그 처치의 값을 쓰는 보상

`trigger` = `이벤트 받기(유닛 처치)` **AND** `비교(kills >= 10)`. 같은 이벤트에 걸린 Trigger는 로그 기록이 먼저 돌기 때문에(→ `11-Triggers-Conditions.md` 6-1), 10번째 처치를 받는 순간 `kills`는 이미 10이다. 결과는 그 처치 이벤트를 `Context.Event`로 본다.

```csharp
[Serializable, BlockInfo("처치한 유닛에게 칭호", Category = "보상")]
public class TitleToKiller : Reward
{
    public string title = "베테랑";
    public override RewardEffect Effect => RewardEffect.Reversible;
    public override string Describe(INameResolver names) => $"처치한 유닛 칭호 {title}";
    public override void Grant(GameSession session)
    {
        if (Context?.Event is UnitKilled e) session.Data.Get<Party>("party").titles.Add($"{e.killerId}: {title}");
    }
}
```

### 3-7. 도전과제 연쇄: 세 개를 모두 달성하면

`trigger` = `도전과제 달성(hunter100)` AND `도전과제 달성(firstBlood)` AND `도전과제 달성(richParty)`.

마지막 하나가 달성되는 순간(같은 Flush 안에서) 이 도전과제도 달성된다.

### 3-8. 전용 보상을 코드로(`[BindTo]`)

```csharp
public static class AchievementRewards
{
    [BindTo("dragonSlayer")]
    static void DragonSlayer(UnitKilled e, TriggerContext c)
    {
        if (c.Game.Find<Unit>(e.killerId) is Unit killer) killer.maxHp += 20;
        c.Session.Data.Get<Party>("party").titles.Add("드래곤 슬레이어");
    }
}
```

`dragonSlayer`의 결과 칸은 잠기고 `AchievementRewards.DragonSlayer` · 받는 이벤트: UnitKilled · `cs 열기`가 보인다.

### 3-9. 달성 연출

연출·알림은 기본 이벤트 `AchievementUnlocked`(`Id`, `DisplayName`)를 `SubscribeAfterAction`으로 받는다(연출은 `Subscribe`로 받지 않는다, `10-Events.md` 2-7). 예는 `10-Events.md` 3-4. `Id`로 아이콘(`Game.Entry<AchievementData>(e.Id)?.icon`)·번역 이름(텍스트 `<ID>.name`)을 찾는다.

### 3-10. 진행도 UI

```csharp
var achievements = GBibleRuntime.Session.Achievements;
foreach (var def in achievements.Definitions)
{
    var text = achievements.IsUnlocked(def.Id) ? "달성"
             : achievements.TryGetProgress(def.Id, out var now, out var goal) ? $"{now} / {goal}"
             : "미달성";
}
```

---

## 4. 주의할 점 / 자주 하는 실수

- **두 이벤트를 AND로 묶는다.** "보스 처치 받기 AND 상자 열기 받기"는 달성되지 않는다(이벤트는 한 순간에 하나). 각각을 로그로 세고 `비교` 둘을 AND로 묶는다.
- **반복 보상을 기대한다.** 도전과제는 한 번만 발동한다. 반복하는 일은 로그(반영 규칙은 받을 때마다 돈다)나 프로젝트 기능(`repeat: true`)으로 만든다.
- **보상이 행동과 같은 값을 바꾼다.** 도전과제의 결과는 Undo 이력 밖이다. 달성시킨 행동이 같은 필드를 바꿨다면 그 행동을 Undo할 때 그 필드는 행동 전 값으로 돌아가고 Journal에 "Undo …가 행동 밖에서 바뀐 값을 덮었습니다" 경고가 남을 수 있다. 보상은 행동이 건드리지 않는 값(메타 구역의 재화·칭호 등)에 주는 것이 안전하다.
- **조건만으로 된 도전과제에 이벤트 값을 쓰는 결과를 둔다.** 조건 충족으로 발동하면 `Context.Event`가 `null`이다. 이벤트 값이 필요하면 이벤트 받기를 AND로 넣는다.
- **로그에 기록의 로그 칸을 비운다.** 도전과제 안의 로그에 기록은 로그를 꼭 고른다(비우면 아무것도 기록하지 않고 경고만 남는다).
- **ID를 바꾼 뒤 저장 파일.** DB에서 ID를 바꾸면 이름 바꾸기 기록으로 예전 달성 표시가 이어진다(출시 뒤). 코드의 `[BindTo]`는 `GameIds` 상수를 쓰면 함께 맞춰진다.
- **서버·테스트에서 Flush를 잊는다.** `Data.Set`으로 값을 직접 바꾼 뒤에는 `session.Achievements.Flush()`를 불러야 조건이 평가된다(행동으로 바꾸면 자동).
- **강제 달성과 트리거로 달성을 헷갈린다.** 강제 달성은 로그·이벤트를 건드리지 않는다. 연쇄 반응(다른 로그·도전과제)까지 보려면 트리거로 달성을 쓴다.
