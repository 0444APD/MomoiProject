# 17. 프로젝트 고유 기능 만들기(퀘스트 예)

**한 줄 요약**: 로그·도전과제·능력처럼 "Trigger → 결과" 구조를 가진 기능을 프로젝트가 직접 만들 수 있다. DB 종류 클래스에 `Trigger` 필드와 `List<Outcome>` 필드를 두고 `[TriggerSlot]`·`[OutcomeSlot]` 표시만 붙이면 같은 편집 화면·검증이 붙고, 런타임에서는 `session.Triggers.Register`로 감시하고 `RunOutcomes`로 결과를 실행한다.

**언제 쓰는가**

- 퀘스트, 일일 미션, 튜토리얼 단계, 이벤트 기간 보상처럼 "언제(조건) → 무엇을(보상)"을 기획자가 DB에서 항목마다 조립해야 하는 기능.
- 기본 기능(도전과제 = 한 번, 로그 = 숫자 기록)과 진행·저장·반복 규칙이 달라서 직접 다뤄야 할 때.

이 문서에서 쓰는 용어:

| 용어 | 뜻 |
|---|---|
| Trigger | 언제 발동하는지(→ `11-Triggers-Conditions.md`). |
| Outcome | 무엇을 하는지(→ `12-Outcomes.md`). |
| DB 종류 | `GameData`를 상속한 클래스. 상속하면 코드 추가 없이 DB에 탭이 생긴다. |
| 감시 | `session.Triggers`가 Trigger를 지켜보다가 참이 되는 순간 등록한 함수를 부르는 것(`Register`). |
| TriggerContext (컨텍스트) | 발동한 순간의 정보(`Owner`·`Event` …). |
| Permanent | `session.Data.Permanent()` 안의 변경은 Undo 이력에 들어가지 않는다(도전과제 달성이 쓰는 방식). |

---

## 1. 에디터에서 쓰는 법

코드(2장)를 쓰고 컴파일하면:

1. `MomoiProject → DB` 사이드바에 `[Database]`에서 정한 이름의 탭(예: **진행** 그룹의 **퀘스트**)이 생긴다.
2. `+ 퀘스트`로 항목을 만들고 제목(= ID)을 짓는다. DB ID는 모든 종류에서 하나뿐이다(퀘스트 ID가 도전과제 ID와 겹칠 수 없다).
3. Trigger 필드(`trigger`)는 한 줄 식 편집기로, 결과 필드(`outcomes`)는 결과 목록 편집기로 그려진다. `+ 블럭`·`+ 결과 추가` 목록에는 필드의 잠금에 있는 종류만 나온다.
4. 검증(잠금 밖 종류, 비어 있는 칸, 플레이어 데이터 경로, 읽을 이벤트가 없는 반영할 값 …)이 기본 기능과 같은 규칙으로 돈다. 클래스의 `ValidateData`로 기능 고유의 검사를 더할 수 있다.
5. 결과를 코드로 쓰려면 `[BindTo(퀘스트 ID)]`. 에디터는 그 항목의 결과 칸을 잠그고 연결된 코드를 보인다. 런타임에서 그것을 결과로 쓰는 일은 기능 코드가 한다(2-8).

---

## 2. 코드로 쓰는 법 — 문법 요소를 하나씩

### 2-1. DB 종류: `GameData` + `[Database]`

```csharp
using System.Collections.Generic;
using GBible;
using GBible.Data;
using UnityEngine;

[Database("퀘스트", Group = "진행", Order = 30, IdPrefix = "quest")]
public class QuestData : GameData
{
}
```

| `[Database]` 인자 | 뜻 |
|---|---|
| 첫 값 | 탭 이름 |
| `Group` | 사이드바 그룹(기본 기능은 `선언`·`진행`·`DB`을 쓴다) |
| `Order` | 그룹 안 순서 |
| `IdPrefix` | 새 항목 ID 앞부분(없으면 클래스 이름에서) |

`GameData`가 ID(`Id`)와 검증 훅(`ValidateData`)을 준다. 표시 이름 필드는 따로 두지 않는다(ID가 이름이다).

### 2-2. Trigger 칸: `[SerializeReference, TriggerSlot(…)]`

```csharp
[FieldLabel("트리거"), SerializeReference, TriggerSlot(TriggerKinds.Event | TriggerKinds.Condition)]
public Trigger trigger;
```

- `[SerializeReference]` — 필수. `Trigger`는 추상 형식이라 실제 형식째 저장해야 한다.
- `[TriggerSlot(TriggerKinds)]` — 고를 수 있는 잎. 등록해서 감시하는 기능은 `Event`·`Condition`을 쓴다(`Use`는 `Game.Use`로 쓰는 능력에만 의미가 있다).
- `[FieldLabel("…")]` — 마우스를 올렸을 때 보이는 설명(칸 이름표는 변수 이름).

### 2-3. 결과 칸: `[SerializeReference, OutcomeSlot(…)]`

```csharp
[FieldLabel("결과"), SerializeReference, OutcomeSlot(OutcomeKinds.PlayerData | OutcomeKinds.Coded | OutcomeKinds.Targeted)]
public List<Outcome> outcomes = new List<Outcome>();
```

- `OutcomeKinds.All`이면 로그에 기록까지 전부.
- `Fixed = true`면 그 종류 하나로 고정된다(로그처럼 세부 칸만).

### 2-4. 규칙 여러 줄: `List<TriggerRule>`

```csharp
[FieldLabel("단계"), TriggerSlot(TriggerKinds.Event | TriggerKinds.Condition), OutcomeSlot(OutcomeKinds.All)]
public List<TriggerRule> steps = new List<TriggerRule>();
```

`TriggerRule` = `when`(Trigger) + `then`(결과 리스트). 리스트 필드에 두 표시를 붙이면 규칙마다의 `when`·`then`에 적용된다. `TriggerRule`의 필드에는 이미 `[SerializeReference]`가 있다.

### 2-5. 고유 검사: `ValidateData`

```csharp
public override void ValidateData(IValidationContext context)
{
    if (trigger == null) context.Error("트리거가 없습니다(언제 완료되는지).");
    else trigger.Validate(context);
    if (outcomes.Count == 0) context.Warning("결과가 없습니다.");
    foreach (var outcome in outcomes)
        if (outcome == null) context.Error("비어 있는 결과 칸이 있습니다.");
        else outcome.Validate(context);
}
```

`context.Error`는 빌드를 막고, `context.Warning`은 표시만 한다.

### 2-6. 런타임 시작에 끼어들기: `GBibleRuntime.Started`

```csharp
public static class QuestRuntime
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void Hook() => GBibleRuntime.Started += Register;

    static void Register(GameSession session) { /* 2-7 */ }
}
```

`Started`는 세션이 준비된 뒤(플레이어 데이터·로그·능력·도전과제 정의와 프로필 불러오기가 끝난 뒤) 불린다. 데이터는 `GameDatabase.Load().All<QuestData>()`로 읽는다.

### 2-7. 감시 등록: `session.Triggers.Register(owner, when, repeat, fire)`

```csharp
session.Triggers.Register(quest.Id, quest.trigger, repeat: false, fire: moment => { /* 발동 */ });
```

| 인자 | 뜻 |
|---|---|
| `owner` | 항목 ID. 컨텍스트의 `Owner`가 되고, `Unregister(owner)`로 그만둘 때 쓴다 |
| `when` | Trigger(`null`이면 아무것도 하지 않는다) |
| `repeat` | `false`: 한 번 발동하면 감시를 그만둔다 / `true`: 계속(조건만으로 된 Trigger는 거짓이 된 뒤 다시 참이 될 때 또) |
| `fire` | 발동했을 때 불리는 함수. 인자는 그 순간의 `TriggerContext` |

- 이벤트 받기 잎은 이벤트를 받을 때, 조건 충족 잎은 그 조건이 읽는 값이 바뀔 때(행동 끝·프레임 끝의 Flush) 검사된다.
- 등록할 때 이미 참인 조건은 값이 다시 바뀌기 전까지 발동하지 않는다. 시작할 때 소급하려면 등록을 마친 뒤 `session.Triggers.Watcher.EvaluateAll()`을 부른다.
- 그 밖에: `Unregister(owner)`, `IsRegistered(owner)`.

### 2-8. 결과 실행: `RunOutcomes` + `[BindTo]` 확인

```csharp
IEnumerable<Outcome> outcomes = BoundOutcomes.Has(quest.Id) ? BoundOutcomes.Create(quest.Id) : quest.outcomes;
// fire 안에서:
session.Triggers.RunOutcomes(outcomes, moment, "Quests");
```

- `RunOutcomes(결과들, 컨텍스트, 출처)`는 결과를 차례로 실행하고, 결과마다 Journal에 "결과: …"(출처 `Quests`)를 남긴다. 결과가 꺼낸 객체를 고쳐도 기록된다.
- `[BindTo]` 연결은 기본 기능(도전과제·로그·능력)만 스스로 확인한다. 프로젝트 기능은 `BoundOutcomes.Has(id)`로 직접 확인해 바꿔 써야 한다. 확인하지 않으면 에디터에는 결과 칸이 잠겨 보이는데 실제로는 숨은 예전 결과가 돈다.

### 2-9. 완료 저장과 Undo: 플레이어 데이터 + `Permanent`

```csharp
[System.Serializable]
public class QuestLog
{
    public List<string> done = new List<string>();
}
// DB › 플레이어 데이터: ID "quest.log", 구역 메타, 값 형식 QuestLog
```

```csharp
using (session.Data.Permanent())
{
    session.Data.Modify<QuestLog>("quest.log", log => log.done.Add(quest.Id));
    session.Triggers.RunOutcomes(outcomes, moment, "Quests");
}
```

- 완료 여부는 기능이 직접 플레이어 데이터에 둔다(메타 구역이면 프로필에 저장된다). 시작할 때 이미 완료한 것은 등록하지 않는다.
- `Permanent()` 안의 변경은 Undo 이력에 들어가지 않는다. 도전과제처럼 "완료와 보상은 Undo로 취소되지 않는다"를 원하면 이렇게 감싼다. `repeat: false`로 등록한 감시는 발동하면 끝나므로, 완료를 Undo로 되돌릴 수 있게 두면 다시 완료할 길이 없어진다.

---

## 3. 전체 예제: 퀘스트

### 3-1. 코드

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using GBible;
using GBible.Data;
using UnityEngine;

// ── 데이터 ──────────────────────────────────────────────────
[Database("퀘스트", Group = "진행", Order = 30, IdPrefix = "quest")]
public class QuestData : GameData
{
    [FieldLabel("선행 퀘스트"), DataRef(typeof(QuestData))] public string requires = "";
    [FieldLabel("트리거"), SerializeReference, TriggerSlot(TriggerKinds.Event | TriggerKinds.Condition)]
    public Trigger trigger;
    [FieldLabel("결과"), SerializeReference, OutcomeSlot(OutcomeKinds.PlayerData | OutcomeKinds.Coded | OutcomeKinds.Targeted)]
    public List<Outcome> outcomes = new List<Outcome>();

    public override void ValidateData(IValidationContext context)
    {
        if (trigger == null) context.Error("트리거가 없습니다(언제 완료되는지).");
        else trigger.Validate(context);
        foreach (var outcome in outcomes)
            if (outcome == null) context.Error("비어 있는 결과 칸이 있습니다.");
            else outcome.Validate(context);
    }
}

// 완료 기록(DB › 플레이어 데이터: "quest.log", 메타 구역, 값 형식 QuestLog)
[Serializable]
public class QuestLog
{
    public List<string> done = new List<string>();
}

// 완료를 다른 기능(로그 "완료한 퀘스트 수", 연출)이 받을 수 있게 쏘는 이벤트
[GameEvent("퀘스트 완료")]
public class QuestCompleted
{
    public string questId;
}

// ── 런타임 ─────────────────────────────────────────────────
public static class QuestRuntime
{
    const string LogKey = "quest.log";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void Hook() => GBibleRuntime.Started += Start;

    static void Start(GameSession session)
    {
        var database = GameDatabase.Load();
        if (database == null || !session.Data.IsDefined(LogKey)) return;
        foreach (var quest in database.All<QuestData>()) Watch(session, quest);
        session.Triggers.Watcher.EvaluateAll();           // 이미 조건을 넘은 퀘스트도 완료
    }

    // 완료하지 않았고, 선행 퀘스트를 마친 퀘스트만 감시한다.
    static void Watch(GameSession session, QuestData quest)
    {
        var log = session.Data.Get<QuestLog>(LogKey);
        if (log.done.Contains(quest.Id) || session.Triggers.IsRegistered(quest.Id)) return;
        if (!string.IsNullOrEmpty(quest.requires) && !log.done.Contains(quest.requires)) return;

        var id = quest.Id;
        IEnumerable<Outcome> outcomes = BoundOutcomes.Has(id) ? BoundOutcomes.Create(id) : quest.outcomes;
        session.Triggers.Register(id, quest.trigger, repeat: false, fire: moment =>
        {
            session.Journal.Append(JournalKind.System, "Quests", $"퀘스트 완료: {id}");
            using (session.Data.Permanent())
            {
                session.Data.Modify<QuestLog>(LogKey, l => l.done.Add(id));
                session.Triggers.RunOutcomes(outcomes, moment, "Quests");
                session.Events.Publish(new QuestCompleted { questId = id });
            }
            // 이 퀘스트를 선행으로 가진 퀘스트를 열어 준다.
            foreach (var next in GameDatabase.Load().All<QuestData>().Where(q => q.requires == id)) Watch(session, next);
        });
    }

    // UI용: 진행도(조건 잎이 있을 때)
    public static string Progress(GameSession session, QuestData quest)
    {
        if (session.Data.Get<QuestLog>(LogKey).done.Contains(quest.Id)) return "완료";
        return quest.trigger != null && quest.trigger.TryGetProgress(session, out var now, out var goal) ? $"{now} / {goal}" : "진행 중";
    }
}
```

### 3-2. 에디터에서 퀘스트 조립

| 퀘스트 ID | `requires` | `trigger` | `outcomes` |
|---|---|---|---|
| `quest.slimes` | (비움) | `비교(slimeKills >= 5)` | `party › gold` 증감 100 |
| `quest.boss` | `quest.slimes` | `이벤트 받기(유닛 처치, IsBoss 참)` | `party › titles`에 `보스 사냥꾼` + `party › gear`에 인스턴스 `bossSword` |
| `quest.rich` | (비움) | `플레이어 데이터 검사(party: gold 5000 이상)` OR `도전과제 달성(hunter100)` | 코딩한 결과 `골드 이자` |

- `slimeKills`는 로그(유닛 처치 받기 + 이벤트 조건 `tags` 포함 `slime`)다.
- `quest.boss`는 `quest.slimes`를 마친 순간부터 감시된다.

### 3-3. 결과를 코드로(`[BindTo]`)

(`UnitKilled`·`Unit`·`Party`, 그리고 3-2의 `골드 이자`는 `12-Outcomes.md` 4장의 예제 형식이다.)

```csharp
public static class QuestRewards
{
    [BindTo("quest.boss")]
    static void BossQuest(UnitKilled e, TriggerContext c)
    {
        if (c.Game.Find<Unit>(e.killerId) is Unit killer) killer.maxHp += 10;
        c.Session.Data.Get<Party>("party").titles.Add("보스 사냥꾼");
    }
}
```

`QuestRuntime.Watch`가 `BoundOutcomes`를 확인하므로 이 함수가 `quest.boss`의 결과가 된다(에디터의 결과 칸은 잠긴다).

### 3-4. 완료한 퀘스트 수를 로그로

DB › 로그 `questsDone`: 반영 규칙 `퀘스트 완료 받기`(반영할 값 비움). 도전과제 "퀘스트 10개 완료" = `비교(questsDone >= 10)`.

---

## 4. 변형

### 4-1. 반복 미션(`repeat: true`)

```csharp
[Database("일일 미션", Group = "진행", IdPrefix = "daily")]
public class DailyMissionData : GameData
{
    [SerializeReference, TriggerSlot(TriggerKinds.Event)] public Trigger trigger;
    [SerializeReference, OutcomeSlot(OutcomeKinds.PlayerData | OutcomeKinds.Coded)] public List<Outcome> outcomes = new List<Outcome>();
    public int maxPerRun = 3;
}

[Serializable]
public class DailyLog { public Dictionary<string, int> counts = new Dictionary<string, int>(); }   // 플레이어 데이터 "daily.log"(세션 구역)

static void WatchDaily(GameSession session, DailyMissionData mission)
{
    var id = mission.Id;
    session.Triggers.Register(id, mission.trigger, repeat: true, fire: moment =>
    {
        var log = session.Data.Get<DailyLog>("daily.log");
        log.counts.TryGetValue(id, out var n);
        if (n >= mission.maxPerRun) return;              // 한도
        log.counts[id] = n + 1;                          // 결과와 함께 기록된다
        session.Triggers.RunOutcomes(mission.outcomes, moment, "Daily");
    });
}
```

`Permanent()` 없이 실행하면, 이벤트를 쏜 행동 안에서 발동한 미션의 횟수·보상은 그 행동의 Undo 단위가 된다(Undo하면 함께 돌아간다).

### 4-2. 단계 퀘스트(`List<TriggerRule>`)

```csharp
[Database("연계 퀘스트", Group = "진행", IdPrefix = "chain")]
public class ChainQuestData : GameData
{
    [TriggerSlot(TriggerKinds.Event | TriggerKinds.Condition), OutcomeSlot(OutcomeKinds.PlayerData | OutcomeKinds.Coded)]
    public List<TriggerRule> steps = new List<TriggerRule>();
}

[Serializable]
public class ChainLog { public Dictionary<string, int> step = new Dictionary<string, int>(); }   // 플레이어 데이터 "chain.log"(메타)

static void WatchStep(GameSession session, ChainQuestData quest)
{
    var log = session.Data.Get<ChainLog>("chain.log");
    log.step.TryGetValue(quest.Id, out var index);
    if (index >= quest.steps.Count) return;              // 다 끝남
    var rule = quest.steps[index];
    session.Triggers.Register(quest.Id, rule.when, repeat: false, fire: moment =>
    {
        using (session.Data.Permanent())
        {
            session.Data.Modify<ChainLog>("chain.log", l => l.step[quest.Id] = index + 1);
            session.Triggers.RunOutcomes(rule.then, moment, "ChainQuests");
        }
        WatchStep(session, quest);                       // 다음 단계
    });
}
```

에디터에서는 `steps`의 `+ 추가`로 단계를 더하고, 단계마다 트리거·결과를 조립한다.

### 4-3. Unity 없이(서버·테스트)

DB 없이도 같은 API로 돈다. Trigger·결과를 코드로 만들어 등록한다.

```csharp
var session = new GameSession();
session.Data.Define("quest.log", DataZone.Meta, new QuestLog());
session.Data.Define("party", DataZone.Meta, new Party());
var when = new EventTrigger { eventType = typeof(QuestCompleted).FullName };
var rewards = new List<Outcome> { new PlayerDataReward { key = "party", path = "gold", amount = 50 } };
session.Triggers.Register("bonus", when, repeat: true, fire: m => session.Triggers.RunOutcomes(rewards, m, "Bonus"));
```

값을 직접 바꾼 뒤 조건 잎을 검사하게 하려면 `session.Triggers.Flush()`를 부른다(행동으로 바꾸면 자동).

---

## 5. 주의할 점 / 자주 하는 실수

- **`[BindTo]`를 확인하지 않는다.** 에디터는 연결된 항목의 결과 칸을 잠그지만, 런타임에서 그 코드를 쓰는 것은 기능 코드의 몫이다. `BoundOutcomes.Has(id) ? BoundOutcomes.Create(id) : 결과` 한 줄을 넣는다.
- **완료를 저장하지 않는다.** `repeat: false` 감시는 세션 안에서만 한 번이다. 저장하지 않으면 다음 실행에서 다시 발동한다.
- **시작할 때 이미 참인 조건.** 등록만으로는 발동하지 않는다. 등록 뒤 `session.Triggers.Watcher.EvaluateAll()`.
- **`Use` 잎을 허락한다.** 등록한 감시는 `사용`으로 발동하지 않는다(`사용`은 `Game.Use`의 사용 조건 검사에서만 참이다). 감시하는 기능의 잠금은 `Event`·`Condition`으로 둔다.
- **`fire` 안에서 결과를 직접 부른다.** `outcome.Run(…)`을 직접 부르지 말고 `RunOutcomes`를 쓴다. 그래야 컨텍스트(`Current`)가 결과·조건 Block에 보이고 Journal에 남는다.
- **`fire` 안에서 행동을 실행할 때**: 이벤트는 대개 행동 안에서 쏘이므로 `fire`도 행동 안에서 불린다. 여기서 부른 `Game.Do`·`Game.Use`는 그 행동의 일부로 실행된다. 조건 충족으로 발동하면(행동이 끝난 뒤) 새 행동이 된다.
- **로그에 기록을 허락하고 로그 칸을 비운다.** 프로젝트 기능에서 로그에 기록의 로그 칸을 비우면 그 항목 ID는 로그가 아니라 아무것도 기록하지 않고 경고만 남는다. `OutcomeKinds.Record`를 허락한다면 로그를 꼭 고르게 하거나 `ValidateData`로 검사한다.
- **인스턴스 기능 필드로 트리거·결과를 둘 때**: 인스턴스 값 안의 트리거·결과도 DB와 같은 편집기로 그려진다. 다만 원본 하나에 하나씩 정하는 값이라, 여러 항목이 공유하거나 기획자가 따로 관리할 기능은 DB 종류(`GameData`)로 만든다.
