# 시스템 API 설명서 (게임 코드에서 끌어다 쓰는 것)

C# 기본 문법은 빼고, 이 시스템(GBible)의 형식·메서드·어트리뷰트만 설명한다.
항목마다 **형태 → 하는 일 → 언제 쓰나 → 주의**. 예제의 `Unit`은 클래스 탭에서 만든 `[Serializable, PlayerData] class Unit { public string id; … }`.

---

## 0. `Game`을 얻는 곳

| 위치 | 쓰는 법 |
|---|---|
| 행동·효과·대상·조건 Block 안 | 부모가 물려준 `Game` 속성을 그대로 쓴다. 지금 실행 중인 세션의 것이 들어 있다. |
| 씬 코드(MonoBehaviour) | `GBibleRuntime.Game` (보통 `GameApi Game => GBibleRuntime.Game;`로 줄여 둔다) |
| 테스트·서버·시뮬레이션 | `session.Game` (세션마다 하나) |

`GameApi`는 세션 하나에 붙은 창구다. `Game.Session`으로 세션 전체(`Data`·`Events`·`Journal` 등)에 닿을 수 있지만, 아래 메서드로 되는 일은 아래 메서드로 한다.

---

## 0-1. 플레이어 데이터 클래스 — 평범한 `[Serializable]` 클래스

```csharp
[Serializable]
public class Unit { public string id; public int hp; public List<string> buffs = new List<string>(); }
```
- **하는 일**: 특별한 문법이 없다. 필드를 그냥 고치고(`u.hp -= 3`), `List`·`Dictionary`·배열·구조체를 그대로 쓴다. private 필드는 `[SerializeField]`(Unity와 같은 규칙).
- **기록되는 방식(Mirror)**: 시스템이 플레이어 데이터의 사본(Mirror)을 들고 있다가 행동이 끝날 때·Modify가 끝날 때·프레임이 끝날 때 Sync해서, 바뀐 곳만 경로로 기록한다(`units[#3].hp 30 → 20`). 행동 안의 변경은 그 행동의 Undo 단위(ChangeSet)가 되고, 같은 ChangeSet을 다른 쪽에 보내면 그대로 같아진다(멀티플레이).
- **조건**: 클래스·구조체에 `[Serializable]`(컴파일할 때 `GBible.CodeGen`이 그 형식 안에 비교 코드를 만든다). 없으면 예전 방식(리플렉션 비교)으로 동작하고 기록에 경고가 남는다.
- **다루지 않는 것**: 중첩 컬렉션(`List<List<int>>`) — 그 데이터는 예전 방식. 델리게이트 필드는 데이터가 아니라 빠진다. 같은 객체를 두 곳에서 가리키면 두 곳 모두 비교되지만, 받는 쪽·되돌린 뒤에는 각자 다른 객체가 될 수 있다(오래 들고 있을 것은 id로).

### `Game.Data()` — 플레이어 데이터를 이름으로
```csharp
Game.Data().PlayerGold += 100;                     // 단순 값: 읽고 쓰기(쓰면 기록)
Game.Data().PlayerInventory.slots.Add(slot);       // 클래스 값: 꺼내서 안쪽을 고친다
```
- 식별자 코드(`GameIds.cs`)와 함께 생성된다(`using GBible.Ids;`). 이름은 플레이어 데이터 ID의 파스칼 표기.

---

## 1. 읽기

### `Game.All<T>()` / `Game.All<T>(string key)`
- **하는 일**: 플레이어 데이터 전체(또는 `key` 하나) 안에서 `T` 객체를 모두 모아 `List<T>`로 준다. 리스트·배열·딕셔너리 값·클래스 필드 안쪽까지 내려간다. 순서는 늘 같다(키 이름순 → 들어 있는 순서).
- **언제**: "모든 유닛", "적 전부", "인벤토리 아이템 전부".
- **주의**
  - 플레이어 데이터 클래스면 어디서 고쳐도 기록된다. 다만 플레이어 입력은 행동(`Game.Do`)으로 해야 Undo 한 단위·서버 전송이 된다. 씬 코드는 읽기·그리기 위주로.
  - 같은 형식이 여러 플레이어 데이터에 있으면 전부 나온다. 한 곳만 원하면 `All<T>("키")`.
  - 키는 생성된 상수로: `Game.All<Unit>(GameIds.PlayerData.Units.Id)` (아래 `GameIds`).

### `Game.Find<T>(string id)` / `Game.Find(string id)`
- **하는 일**: `id` 필드가 같은 `T`를 찾는다(없으면 `null`). 형식 없이 부르면 id 필드가 있는 모든 객체에서 찾는다.
- **조건**: `T`에 `public string id;`(또는 `Id`) 필드·속성이 있어야 한다. 없으면 "id 필드를 더하라"는 예외.
- **언제**: 씬 코드가 들고 있던 id로 객체를 다시 찾을 때. 객체를 오래 들고 있지 말고 id로 들고 있다가 쓸 때 `Find`한다(Undo·불러오기 뒤에는 다른 인스턴스일 수 있다).

### `GameIds` (자동 생성 상수)
- 도감의 ID가 코드 상수로 생성된다(`MomoiProject > 식별자 코드 다시 만들기`, 네임스페이스 `GBible.Ids`).
  - 이름은 ID를 파스칼 표기로 바꾼 것(`player.gold` → `PlayerGold`, `ability.splash` → `AbilitySplash`)
  - `GameIds.PlayerData.PlayerGold` → `PlayerDataKey<int>` (형식이 붙은 플레이어 데이터 키, `.Id`가 ID 문자열)
  - `GameIds.Ability.AbilitySplash`, `GameIds.Achievement.FirstHunt` … → ID 문자열
  - 키워드는 그룹별: `GameIds.Keyword.Grade.Legendary`
- 문자열을 직접 쓰면 오타가 런타임에야 드러나므로 이걸 쓴다.

### `Game.Get<T>(key)` / `Game.Set(key, value)`
- 숫자·글자 같은 **단순 값** 플레이어 데이터(골드 등)를 ID로 읽고 바꾼다. 이름으로 쓰려면 `Game.Data().PlayerGold`.
- 클래스·리스트 플레이어 데이터는 `All`/`Find`/`Game.Data()`로 객체를 꺼내 필드를 고친다.

---

## 2. 바꾸기 = 행동(GameAction)

플레이어 입력(이동·사용·구매·턴 넘기기)은 행동 클래스로 만든다. 행동은 **데이터**라서 저장·서버 전송·리플레이가 되고, `Game.Do` 한 번이 Undo 한 단위다.

### `GameAction<T>` — 대상이 있는 행동
```csharp
[Serializable] public class MoveUnit : GameAction<Unit>
{
    public int x, y;                                         // 행동이 실어 나르는 값(데이터)
    protected override void Run(Unit u) { u.x = x; u.y = y; } // 대상 객체를 받아 그냥 고친다
}
Game.Do(new MoveUnit { target = unit.id, x = 3, y = 4 });
```
- `target`(string): 물려받은 필드. 대상의 **id**를 넣는다(객체가 아니라 id라서 데이터로 남는다).
- `Run(T)`: 시스템이 `Find<T>(target)`으로 찾은 객체를 넘겨준다. 못 찾으면 행동이 실패하고 아무것도 안 바뀐다.
- `Describe()`: 행동 로그 문장. 안 쓰면 `[BlockInfo]` 이름(없으면 클래스 이름) + 대상 id. 바꾸려면 `public override string Describe() => …`.
- 등록이 필요 없다(`Actions.Register` 불필요).

### `SimpleAction` — 대상이 없는 행동
```csharp
[Serializable] public class EndTurn : SimpleAction
{
    protected override void Run() => Game.All<Board>()[0].turn++;
}
```

### `Game.Do(action)` 안에서 시스템이 하는 일
1. 행동을 Journal에 남긴다.
2. `Run` 안의 쓰기를 이 행동의 Undo 단위로 묶는다. 플레이어 데이터 클래스는 쓰는 순간 기록되고, 아닌 클래스는 꺼낸 값을 끝날 때 비교해 기록한다. → **Modify 같은 감싸기가 필요 없는 이유**.
3. 예외가 나면 그 행동이 바꾼 것을 전부 되돌리고 예외를 다시 던진다.
4. 끝난 뒤 관련 조건(도전과제)을 다시 평가한다.

### 주의
- 행동 안에서 기다리지 않는다(코루틴·await). 로직은 한 번에 끝내고 연출은 결과를 보고 따로.
- 행동 안에서 다른 행동을 `Do`하지 않는다(예외). 공통 부분은 함수로 빼서 같이 부른다.
- 씬 코드에서 플레이어 데이터 클래스를 직접 고치면 기록은 되지만 Undo 단위가 없고(되돌릴 수 없음) 서버로 가지 않는다. 플레이어 입력은 행동으로.

### 되돌리기
`Game.Session.Undo()` — 마지막 행동을 되돌린다(값·난수 함께). 도전과제 달성·보상은 되돌리지 않는다.

---

## 2-1. 트리거·결과 — 로그·도전과제·능력·아이템 공통

도감의 "조건 → 보상" 기능은 모두 같은 두 칸으로 이루어진다. 두 칸의 편집 화면·동작은 하나이고, 기능마다 다른 것은 **칸에서 고를 수 있는 범위(잠금)**뿐이다.

| 칸 | 고르는 것 |
|---|---|
| **트리거**(언제) | `이벤트 발생`(이벤트 + 그 값의 조건) · `조건 충족`(조건 Block) · `사용`(`Game.Use`) — `AND`·`OR`·`NOT`·괄호로 중첩 |
| **결과**(무엇을) | `로그에 기록`(이벤트 값 × 배수) · `플레이어 데이터에 주기` · `대상에게 효과`(대상 Block + 효과 Block) · 코딩한 결과(`Reward` 상속) |

| 기능 | 트리거 잠금 | 결과 잠금 |
|---|---|---|
| 로그(`StatData.triggers`) | 이벤트 발생 | 로그에 기록 하나로 고정(세부 칸만) |
| 도전과제(`AchievementData`) | 이벤트 발생 · 조건 충족 | 전부 |
| 능력(`AbilityData`) | 사용 · 조건 충족(기본 `사용`, AND로 사용 조건) | 대상에게 효과 · 플레이어 데이터에 주기 · 코딩한 결과 |
| 아이템의 `사용` 기능(`UsableFeature`) | 능력과 같음 | 능력과 같음 |

- **AND**: 이벤트가 발생한 그 순간에 조건도 참이어야 발동한다(예: "보스 처치 발생 AND 골드 100 이상"). 서로 다른 이벤트 둘을 AND로 묶으면 같은 순간에 발생하지 않으니 검증이 경고한다.
- **OR**: 어느 쪽이든 먼저 참이 되는 순간에 발동한다.
- 조건만으로 된 트리거는 참이 되는 순간 발동한다(반복하는 기능이면 다시 거짓이 된 뒤에 또).

**새 기능(예: 퀘스트)도 필드 두 개에 표시만 붙이면 같은 편집 화면·검증·실행이 붙는다.**
```csharp
[Database("퀘스트")]
public class QuestData : GameData
{
    [SerializeReference, TriggerSlot(TriggerKinds.Event | TriggerKinds.Condition)] public Trigger trigger;
    [SerializeReference, OutcomeSlot(OutcomeKinds.All)] public List<Outcome> outcomes = new List<Outcome>();
}
// 런타임: session.Triggers.Register(quest.Id, quest.trigger, repeat: false,
//              fire: moment => session.Triggers.RunOutcomes(quest.outcomes, moment, "Quests"));
```
- 규칙 여러 줄(트리거 + 결과)이 필요하면 `List<TriggerRule>` 필드에 두 표시를 붙인다(로그처럼).
- 한 이벤트에 걸린 트리거는 조건이 없는 것(로그 기록 등)이 먼저, 조건이 있는 것(도전과제 등)이 나중에 검사된다: "처치 발생 AND 처치 수 ≥ 10"은 막 오른 처치 수를 본다.
- 결과가 자기 트리거의 이벤트를 다시 발생시키는 사슬은 16단계에서 끊기고 경고가 남는다.
- 플레이어 데이터에 주기: 숫자는 증감, 글자·enum·참거짓은 값, 클래스는 템플릿, 리스트·배열은 요소 넣기, **딕셔너리는 키 + 값 넣기**(숫자 값은 있던 값에 더함·없던 키는 0에서, 그 밖의 값·템플릿은 덮어쓰기).
- 결과를 코딩할 때: `Reward`를 상속하고 `Grant` 안에서 `Context`(트리거 순간: `Context.Event`·`Context.User`·`Context.Picked`)를 본다.
- 조건 Block(`SimpleCondition`) 안에서도 `Context`로 트리거 순간을 본다(예: 능력의 사용 조건에서 `Context?.User as Unit`).

---

## 3. 능력 = 아이템 사용 효과·스킬·버프 발동

### `Game.Use(string ability, object user, params object[] targets)`
- 도감 › 능력의 능력을 쓴다. `user`·`targets`는 객체 또는 id 문자열. 내부적으로 `UseAbility` 행동 하나라 Undo 한 번에 통째로 되돌아간다.
- 흐름: 트리거 검사(사용 조건이 거짓이면 행동 실패) → 결과를 차례로(`대상에게 효과`: 대상 Block들을 위에서부터, 앞 결과가 다음 입력, 대상 Block이 없으면 고른 대상 그대로 → 대상마다 효과 Block) → `AbilityUsed` 이벤트.
- 아이템도 같다: 아이템에 `사용` 기능을 붙이면 `Game.Use(아이템 ID, 사용자, 대상들)`로 쓴다(개수 줄이기는 인벤토리 쪽에서).

### `Game.CanUse(string ability, object user, params object[] targets)` / `Game.CanUse(ability, user, out string reason)`
- 지금 쓸 수 있는지만 본다(트리거 = 사용 조건 검사, 아무것도 바꾸지 않는다). 버튼을 미리 흐리게 하거나 이유를 툴팁으로 보일 때.
- `if (!Game.CanUse("ability.heal", hero, out var why)) healButton.SetEnabled(false);`

### `Effect<T>` — 효과 Block("무엇을")
```csharp
[Serializable, BlockInfo("피해", Category = "전투")]
public class Damage : Effect<Unit>
{
    public int amount = 10;                                           // 에디터 칸(능력마다 값이 다르다)
    protected override void Apply(Unit target) { target.hp -= amount; }
}
```
| 물려받는 것 | 뜻 |
|---|---|
| `Apply(T target)` | 대상 하나에 할 일. 대상이 `T`가 아니면 불리지 않는다. 받은 객체를 그냥 고친다. |
| `Game` | 지금 세션의 `GameApi`. |
| `User<TUser>()` | 능력을 쓴 개체(사용자). 예: `var caster = User<Unit>();` (없거나 형식이 다르면 null) |
| `Use` | 이번 사용 정보: `Use.Ability`(능력 정의·ID), `Use.User`, `Use.Picked`(플레이어가 고른 대상들). |

### `TargetSelector<T>` — 대상 Block("누구에게")
```csharp
[Serializable, BlockInfo("주변 n칸의 적 추가", Category = "대상")]
public class AddEnemiesAround : TargetSelector<Unit>
{
    public int range = 1;
    protected override IEnumerable<Unit> Select(IEnumerable<Unit> targets, Unit user) => …;
}
```
- `Select(targets, user)`: 앞 단계 대상들과 사용자를 받아 다음 대상들을 돌려준다. **고르기만 하고 값은 바꾸지 않는다.**
- `Game`, `Use`도 물려받는다.
- 기본 제공: `SelfTarget`("사용자 자신" — 나에게 부여).

### 능력 데이터
- 도감 › 능력(`AbilityData`): `trigger`(기본 `사용`) + `outcomes`(결과 목록)를 에디터에서 조립(2-1).
- 코드로 정의(테스트 등): `session.Abilities.Define(new AbilityDefinition { Id = "…", When = … })` 후 `Outcomes`에 결과 추가(짧은 형태 `Targets`·`Effects`도 된다).

---

## 4. 조건 Block

### `SimpleCondition` — 쉬운 형태
```csharp
[Serializable, BlockInfo("적 전멸", Category = "전투")]
public class AllEnemiesDown : SimpleCondition
{
    public string team = "enemy";
    protected override bool Check() => Game.All<Unit>().Where(u => u.team == team).All(u => u.hp <= 0);
}
```
- `Check()`만 쓴다. 평가할 때 읽은 플레이어 데이터를 시스템이 기록해 그것이 바뀔 때 다시 평가한다(무엇을 보는지 선언할 필요 없음).
- 트리거 칸(도전과제·능력의 사용 조건 …)의 `조건 충족` 블럭으로 나온다. 트리거 순간은 `Context`로 본다.
- 조건에서는 값을 바꾸지 않는다.

### `Condition` — 전부 직접 쓰는 형태(필요할 때만)
| 멤버 | 뜻 |
|---|---|
| `bool Evaluate(GameSession s)` | 판정 |
| `void CollectDependencies(ICollection<string> keys)` | 보는 플레이어 데이터 키를 직접 선언(선언한 것 + 읽은 것 모두 감시된다) |
| `string Describe(INameResolver names)` | 코드식 문장(필수) / `Natural(…)` 자연어 문장 |
| `double PollInterval` | 시간이 흐르기만 해도 바뀌는 조건이면 확인 주기(초) |
| `bool TryGetProgress(s, out current, out target)` | 진행도 표시(예: 37/100) |

---

## 5. 결과 Block 직접 만들기 (필요할 때만)

기본 결과(플레이어 데이터에 주기·대상에게 효과·로그에 기록)로 안 되는 것(결제 지급·플랫폼 연동 등)만. `Reward`를 상속하면 모든 기능의 결과 칸 '코딩한 결과'에 나온다.
```csharp
[Serializable, BlockInfo("칭호 지급", Category = "보상")]
public class GrantTitle : Reward
{
    public string title;
    public override RewardEffect Effect => RewardEffect.Reversible;   // 플레이어 데이터 안의 일
    public override void Grant(GameSession s) { s.Game.Find<Profile>("me").titles.Add(title); }
    public override string Describe(INameResolver names) => $"칭호 {title}";
}
```
- `RewardEffect.Reversible`: 플레이어 데이터 안에서만 일어남 / `Irreversible`: 게임 밖에 영향(결제·플랫폼). 멀티플레이에서는 서버만 실행.
- 보상 지급은 Undo 대상이 아니다(한 번 받은 보상은 유지).

---

## 6. Block·필드에 붙이는 어트리뷰트

### `[BlockInfo(이름, …)]` — Block 클래스에 (필수)
| 속성 | 뜻 |
|---|---|
| 첫 값 | 에디터 메뉴에 보일 이름 |
| `Category` | 메뉴 분류 |
| `Description` | 메뉴·툴팁 설명 |
| `Sentence` | 편집 화면 한 줄 문장. `{필드이름}`이 누르면 바뀌는 칸이 된다. 예: `"{amount} 피해"` |

Block 클래스에는 `[Serializable]`도 필수. 둘 다 계약 테스트가 검사한다. 만들면 에디터 메뉴에 자동으로 나타난다(테스트 어셈블리의 Block은 숨김).

### 필드에 붙이는 것 — 에디터 칸을 "고르기"로 만든다
| 어트리뷰트 | 칸이 하는 일 |
|---|---|
| `[FieldLabel("설명")]` | 마우스를 올리면 보이는 설명 |
| `[DataRef(typeof(AbilityData))]` (string 필드) | 그 도감 종류의 항목 목록에서 고르기. 키워드는 `typeof(KeywordData)` 또는 클래스 상세에서 그룹 연결 |
| `[PlayerDataRef]` (string) | 플레이어 데이터 목록에서 고르기 |
| `[StatRef]` (string) | 로그 목록에서 고르기 |
| `[AchievementRef]` (string) | 도전과제 목록에서 고르기 |
| `[EventTypeRef]` (string) | `[GameEvent]` 이벤트 형식 목록에서 고르기 |
| `[FieldPath]` + 같은 클래스에 `[FieldPathRoot(…)]` | 뿌리(플레이어 데이터·이벤트) 안의 필드 경로를 탐색기로 고르기 |
| `[SerializeReference]` | 필드 형식이 Block(`Condition`·`Reward`·`Effect`·`TargetSelector`)이거나 그 목록일 때 필수. 에디터가 Block 고르기·조립 화면으로 그린다 |

---

## 7. 이벤트

```csharp
[GameEvent("적 처치")] public class EnemyKilled { public string unit; public int combo; public List<string> tags; }

Game.Events.Publish(new EnemyKilled { unit = target.id, combo = 3 });        // 효과·행동 안에서 내기
var sub = Game.Events.Subscribe<EnemyKilled>(e => PlayEffect(e.unit));     // 씬 코드에서 받기(연출)
sub.Dispose();                                                              // 그만 받기
```
- `[GameEvent("이름")]`: 도감 › 이벤트 탭과 로그 규칙의 이벤트 목록에 나온다.
- 로그(도감 › 로그)는 이벤트를 규칙대로 집계한다(횟수·합계·최대, 이벤트 필드 조건으로 거르기). 도전과제는 그 로그를 본다.
- 이벤트를 받는 쪽에서 플레이어 데이터를 바꾸지 않는다. 연출·UI만(상태를 바꿀 일은 행동·효과에서).
- 기본 제공 이벤트: `AbilityUsed`(능력 ID·사용자·최종 대상들).

---

## 8. 난수·새 id·템플릿

| 형태 | 뜻 |
|---|---|
| `Game.Random("이름").Range(min, max)` | 정수 `min` 이상 `max` 미만. 이름별로 따로 흐르는 결정론적 난수(같은 시드·같은 행동 → 같은 결과, Undo하면 되돌아감). 이름은 용도별로("damage", "loot") 나누면 한 곳의 소비가 다른 곳 결과를 흔들지 않는다. |
| `Game.NewId("unit")` | "unit1", "unit2" … 소환처럼 개체를 새로 만들 때. 번호가 플레이어 데이터에 있어서 Undo·저장·멀티플레이에서도 같다. |
| `Game.Session.CreateFromTemplate("template.sword")` | 도감 › 템플릿의 원본 사본(원본은 안 바뀜). 예: 행동 안에서 `bag.items.Add((Item)Game.Session.CreateFromTemplate(id));` |

`UnityEngine.Random`·`System.Random`은 게임 로직에 쓰지 않는다(연출용 흔들림 정도만).

---

## 9. 검증 (에디터의 오류 표시)

Block·도감 데이터 클래스에서 잘못된 칸을 에디터 검증에 띄운다.
```csharp
public override void Validate(IValidationContext context)        // Block(Condition·Reward)
{
    if (amount <= 0) context.Error("피해량은 1 이상이어야 합니다.");
    if (string.IsNullOrEmpty(title)) context.Warning("칭호가 비어 있습니다.");
}
public override void ValidateData(IValidationContext context) { … } // 도감 데이터 클래스(GameData)
```
- `Error`는 빌드를 막는다. `Warning`은 표시만.

---

## 10. 런타임

| 형태 | 뜻 |
|---|---|
| `GBibleRuntime.Start()` | 세션을 만들고 도감 데이터(플레이어 데이터·로그·도전과제·능력·템플릿)를 등록. 이미 있으면 그것을 준다. |
| `GBibleRuntime.Session` / `.Game` | 지금 세션 / 그 `GameApi` |
| `session.ExportMeta()` / `ImportMeta(json)` | 프로필(메타 구역: 계정 단위로 남는 것) 저장·불러오기 |
| `session.ExportRun()` / `ImportRun(json)` / `NewRun()` | 이번 판(세션 구역) 저장·불러오기·새 판 |

플레이어 데이터의 "구역"(도감 › 플레이어 데이터의 칸): 메타 = 판이 바뀌어도 남음(재화·도감 해금), 세션 = 이번 판에만(유닛 위치·턴).
