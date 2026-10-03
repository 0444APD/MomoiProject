# 12. 부록 — 템플릿 · 체크리스트 · 기능 사용 표

본문에서 쓴 것을 다시 찾기 쉽게 모은다. 복사해 시작하는 뼈대(1절), 장마다의 체크리스트(2절), 본문에서 쓰지 않은 기능(3절), 그리고 기능마다 매뉴얼 장과 이 가이드에서 쓴 절을 적은 표(4절)다.

> [!note]- 이 장에서 쓰는 말
> | 말 | 뜻 |
> |---|---|
> | 행동 (`GameAction`) | 플레이어 입력 하나를 담은 데이터 클래스. 행동 하나 = Undo 한 단위 = 네트워크 한 묶음. |
> | Block | 기획자가 DB에서 골라 조립하는 로직 단위. 효과 Block(무엇을)·대상 Block(누구에게)·조건 Block(언제만)·코딩한 결과(`Reward`). |
> | Trigger · Outcome | DB 항목의 "언제(Trigger) → 무엇을(Outcome, 결과)" 두 칸. |
> | 컨텍스트 (`TriggerContext`) | Trigger가 발동한 순간의 정보(세션·받은 이벤트·사용자·고른 대상 …). |
> | `[BindTo]` | DB 항목 하나의 결과를 코드 함수 하나로 바꾸는 어트리뷰트. |

---

## 1. 템플릿

모두 카드 던전에 맞춘 이름이다. 복사한 뒤 이름과 내용을 바꾼다. 줄마다의 뜻은 뼈대 아래 접힌 칸에 있다.

### 1-1. 대상이 있는 행동

```csharp
[Serializable, BlockInfo("카드 강화")]
public class UpgradeCard : GameAction<DeckCard>
{
    protected override void Run(DeckCard card)
    {
        var data = Rules.CardData(Game, card.card);          // 원본: 읽기만
        if (card.upgraded || !data.upgradable) throw new InvalidOperationException("강화할 수 없는 카드입니다.");   // 실패 = 전체 취소
        var from = card.card;
        card.card = data.upgradeTo;                          // 판 데이터는 원본 ID만 바꾼다(손패·더미가 가리키는 id는 그대로)
        card.upgraded = true;
        Game.Events.Publish(new CardUpgraded { cardId = card.id, from = from, to = card.card });
    }

    public override string Describe() => $"카드 강화: {target}";
}
```

> [!note]- 줄마다의 뜻
> - `[Serializable]` — 행동은 데이터다. 저장·리플레이·서버 전송에 실린다.
> - `[BlockInfo("…")]` — 모니터 행동 탭에 보일 이름(`Describe`가 없을 때).
> - `GameAction<DeckCard>` — 대상 하나에 대한 행동. 대상 클래스에는 `public string id;`가 있어야 한다. 물려받은 `target` 필드에 대상 id를 담아 보낸다: `Game.Do(new UpgradeCard { target = cardId })`.
> - `Run(DeckCard card)` — 시스템이 `Game.Find<DeckCard>(target)`로 찾은 객체를 넘긴다. 못 찾으면 행동이 실패한다.
> - `Rules.CardData` — 비용·강화 대상 같은 고정 값은 원본(인스턴스)에서 읽는다. 판 데이터(`DeckCard`)는 원본 ID와 바뀌는 값만 든다.
> - `throw` — 규칙상 할 수 없으면 예외를 던진다. 이 행동이 바꾼 값·난수가 모두 되돌아가고 예외가 `Do`를 부른 쪽으로 간다.
> - `Game` — 이 행동이 도는 세션의 창구. `Run`이 도는 동안만 들어 있다.
> - `Game.Events.Publish` — 다른 기능(로그·도전과제·화면)이 반응할 순간에 이벤트를 쏜다.
> - `Describe()` — 모니터 행동 탭·타임라인의 문장.

### 1-2. 대상이 없는 행동

```csharp
[Serializable, BlockInfo("휴식")]
public class Rest : SimpleAction
{
    public int percent = 30;

    protected override void Run()
    {
        var hero = Game.Data().Run.hero;
        hero.hp = Math.Min(hero.maxHp, hero.hp + hero.maxHp * percent / 100);
    }
}
```

> [!note]- 줄마다의 뜻
> - `SimpleAction` — 대상이 없다. 필요한 객체는 `Game.Data()`·`Game.Find`·`Game.All`로 찾는다.
> - `public int percent` — 행동이 실어 나르는 값. id와 숫자·글자만 둔다.
> - 다른 행동 안에서 `Game.Do(new Rest())`를 부르면 바깥 행동의 일부로 실행된다(Undo는 바깥 하나).

### 1-3. 이벤트

```csharp
[GameEvent("카드 강화")]
public class CardUpgraded
{
    public string cardId;   // 덱 카드 id
    public string from;     // 예전 원본 ID
    public string to;       // 새 원본 ID
}
```

> [!note]- 줄마다의 뜻
> - `[GameEvent("표시 이름")]` — DB·Trigger·모니터 목록에 이 이름으로 나오고, 값 읽기 코드가 생겨 조건·로그가 리플렉션 없이 읽는다.
> - 필드 — 조건·로그가 고를 값. 객체 대신 id를 둔다. 상속해서 종류를 나누지 않는다(필드로 나누고 값 조건으로 거른다).
> - 쏘기는 행동 안에서 `Game.Events.Publish(new CardUpgraded { … })`, 받기는 Trigger의 '이벤트 받기' 또는 화면의 `Game.Events.Subscribe<CardUpgraded>(…)`.

### 1-4. 효과 Block("무엇을")

```csharp
[Serializable, BlockInfo("에너지", Category = "카드", Description = "이번 턴 에너지를 얻습니다.", Sentence = "에너지 +{amount}")]
public class GainEnergy : Effect<Hero>
{
    public int amount = 1;

    protected override void Apply(Hero target)
    {
        var battle = Game.Data().Run.battle;
        if (battle != null) battle.energy += amount;
    }

    public override string Describe() => $"에너지 +{amount}";
}
```

> [!note]- 줄마다의 뜻
> - `BlockInfo` — 이름은 기획자가 고르는 목록의 글, `Category`는 묶음, `Description`은 설명, `Sentence`는 칸을 눌러 고치는 한 줄 문장(`{필드이름}` 자리가 칸이 된다).
> - `Effect<Hero>` — 대상 형식. 대상 Block이 고른 객체 중 이 형식인 것에만 `Apply`가 불린다.
> - `public int amount` — 기획자가 DB에서 정하는 수치. 필드 초기값이 기본값이다.
> - `Apply(Hero target)` — 대상 하나에 한 번. 값은 그냥 고친다. 사용자는 `User<Hero>()`로 받는다.
> - `Describe()` — 결과 요약에 보일 짧은 글.

### 1-5. 대상 Block("누구에게")

```csharp
[Serializable, BlockInfo("체력이 가장 낮은 적", Category = "대상")]
public class WeakestEnemy : TargetSelector<Fighter>
{
    protected override IEnumerable<Fighter> Select(IEnumerable<Fighter> targets, Fighter user) =>
        Rules.LivingEnemies(Game).OrderBy(e => e.hp).Take(1).Cast<Fighter>().ToList();
}
```

> [!note]- 줄마다의 뜻
> - `TargetSelector<Fighter>` — 앞 단계의 대상들(`targets`, 처음에는 플레이어가 고른 대상)을 받아 다음 대상들을 낸다. 여러 개를 이어 붙이면 차례로 거른다.
> - `user` — 사용자(카드를 낸 주인공). 유물·도전과제처럼 사용자가 없는 결과에서는 비어 있다([04장](04-AddingContent.md) 9절의 `주인공` Block).
> - 무작위로 고를 때는 `Game.Random("이름")`을 쓴다(카드 던전의 무작위 적은 `"cardTarget"`).
> - 대상 Block은 고르기만 하고 값을 바꾸지 않는다.

### 1-6. 조건 Block

```csharp
[Serializable, BlockInfo("전투 중", Category = "카드 던전")]
public class InBattle : SimpleCondition
{
    protected override bool Check() => Rules.InBattle(Game);
}
```

> [!note]- 줄마다의 뜻
> - `SimpleCondition` — `Check()`만 쓴다. 무엇을 보는지 따로 선언하지 않아도 된다(평가할 때 읽은 값을 시스템이 기록해, 그 값이 바뀌면 다시 평가한다).
> - Trigger 안에서 검사 중이면 `Context`(컨텍스트)로 그 순간의 사용자·이벤트를 볼 수 있다: `Context?.User as Hero`.
> - 값에 따라 "이 값이면 안 된다"를 검증하려면 `Validate(IValidationContext)`를 덮어쓴다([05장](05-WorkingWithDesigners.md) 7절).
> - 무엇을 보는지(`CollectDependencies`)와 평가(`Evaluate`)를 직접 쓰는 `Condition` 상속형도 있지만, 시간으로 바뀌는 조건처럼 특별한 경우에만 쓴다(매뉴얼 [11장](../Manual/11-Triggers-Conditions.md) 4-3).

### 1-7. `[BindTo]` 함수

```csharp
public static class TemplateCode
{
    // 축복 blessing.dawn(발동: 턴 시작 받기)의 결과. 이벤트로 발동하므로 이벤트와 컨텍스트를 받는다.
    [BindTo(GameIds.Blessing.Dawn)]
    static void Dawn(TurnStarted e, TriggerContext c)
    {
        if (e.turn == 1) c.Game.Data().Run.hero.block += 3;
    }
}
```

> [!note]- 줄마다의 뜻
> - `[BindTo(ID)]` — 그 ID의 DB 항목(도전과제·로그·능력·사용 기능이 붙은 인스턴스, 그리고 연결을 확인하는 프로젝트 고유 기능 — 1-8)의 결과가 이 함수가 된다. DB의 결과 칸은 잠기고 함수 이름과 `cs 열기`가 보인다. ID는 늘 `GameIds` 상수로(항목을 만들고 저장하면 생긴다).
> - `static void` — 필수. 접근 수준·클래스 이름은 상관없다.
> - 매개변수는 넷 중 하나: `()` · `(이벤트 e)` · `(TriggerContext c)` · `(이벤트 e, TriggerContext c)`. 받을 이벤트는 첫 매개변수의 형식이고, 컨텍스트는 늘 마지막이다.
> - 이벤트로 발동하는 항목이면 `(BattleWon e, TriggerContext c)`처럼 이벤트를 받는다. 조건으로 달성하는 도전과제·사용 기능·능력에는 이벤트가 없으므로 `(TriggerContext c)`만.
> - `c.Game` — 세션의 창구. `GBibleRuntime.Game`을 쓰지 않는다(세션이 여럿일 수 있다). static 변수에 상태를 두지 않는다.
> - 로그에 연결하면 자동 기록이 멈추므로 함수가 `c.Session.Stats.Report(c.Owner, 값)`으로 직접 기록한다.

### 1-8. 프로젝트 고유 기능(Trigger → 결과)

```csharp
[Database("축복", Group = "콘텐츠", Order = 41, IdPrefix = "blessing")]
public class BlessingData : GameData
{
    [FieldLabel("발동"), SerializeReference, TriggerSlot(TriggerKinds.Event | TriggerKinds.Condition)]
    public Trigger trigger;
    [FieldLabel("효과"), SerializeReference, OutcomeSlot(OutcomeKinds.PlayerData | OutcomeKinds.Coded)]
    public List<Outcome> outcomes = new List<Outcome>();
}
```

> [!note]- 데이터 쪽 줄마다의 뜻
> - `[Database("탭 이름", Group, Order, IdPrefix)]` + `GameData` — DB에 탭이 생긴다. 항목은 ID로만 식별한다.
> - `[SerializeReference]` — Trigger·Outcome은 추상 형식이라 실제 형식째 저장해야 한다. 필수.
> - `[TriggerSlot(…)]` — 이 칸에서 고를 수 있는 Trigger 잎. 감시하는 기능은 `Event`·`Condition`(`Use`는 `Game.Use`로 쓰는 것에만 의미가 있다).
> - `[OutcomeSlot(…)]` — 고를 수 있는 결과 종류(플레이어 데이터에 주기 `PlayerData`, 코딩한 결과 `Coded`, 대상에게 효과 `Targeted`, 로그에 기록 `Record`, 전부 `All`).
> - `[FieldLabel("…")]` — 마우스를 올리면 보이는 설명. 칸 이름표는 변수 이름 그대로다.
> - 검사는 `ValidateData(IValidationContext)`를 덮어써 더한다(`context.Error`는 빌드를 막고 `context.Warning`은 표시만, [05장](05-WorkingWithDesigners.md) 7절).

런타임 쪽 뼈대:

```csharp
// Rules: 등록 함수(서버에서도 그대로 쓴다)
public static class BlessingRules
{
    public static void Register(GameSession session, IEnumerable<BlessingData> blessings)
    {
        if (blessings == null) return;
        foreach (var blessing in blessings)
        {
            var id = blessing.Id;
            IEnumerable<Outcome> outcomes = BoundOutcomes.Has(id) ? BoundOutcomes.Create(id) : blessing.outcomes;   // [BindTo] 확인은 기능 코드의 몫
            session.Triggers.Register(id, blessing.trigger, repeat: true, fire: moment =>
            {
                // 여기서 "이 기능이 지금 켜져 있는가"(가졌는가·끝냈는가)를 플레이어 데이터로 확인한다.
                session.Triggers.RunOutcomes(outcomes, moment, "Blessings");
            });
        }
    }
}

// Client/Bootstrap.cs의 Started: 연결은 한 줄
BlessingRules.Register(session, GameDatabase.Load()?.All<BlessingData>());
```

> [!note]- 런타임 쪽 줄마다의 뜻
> - 나누는 까닭 — 등록 함수(`BlessingRules.Register`)는 규칙이라 Rules에 두고, Unity 시작에 거는 연결은 Client의 Bootstrap `Started` 한 줄로 한다. 서버라면 서버가 세션을 만든 뒤 같은 함수를 부른다([04장](04-AddingContent.md) 8절의 `RelicRules`).
> - `GameDatabase.Load()` — 런타임 데이터 목록. `All<T>()`로 그 종류의 항목만 넘긴다.
> - `BoundOutcomes.Has/Create` — 기본 기능과 달리 프로젝트 기능은 `[BindTo]` 연결을 스스로 확인해야 한다. 빼먹으면 에디터는 결과 칸을 잠가 보이는데 실제로는 숨은 예전 결과가 돈다.
> - `session.Triggers.Register(owner, when, repeat, fire)` — Trigger를 감시하다 참이 되는 순간 `fire`를 부른다. `repeat: false`면 한 번 발동하고 끝난다.
> - `RunOutcomes(결과들, 컨텍스트, 출처)` — 결과를 차례로 실행하고 기록(Journal)에 남긴다. `outcome.Run`을 직접 부르지 않는다.
> - 완료 여부처럼 남겨야 할 것은 플레이어 데이터에 둔다. 한 번 일어나면 되돌리지 않을 일이면 `session.Data.Permanent()`로 감싼다([04장](04-AddingContent.md) 8절, [07장](07-GenreRecipes.md) 2절, 매뉴얼 [17장](../Manual/17-Custom-Features.md) 2-9).
> - 시작할 때 이미 조건을 넘은 항목은 등록 뒤 `session.Triggers.Watcher.EvaluateAll()`로 소급한다.

### 1-9. Bootstrap

설정 값(Undo 방식·보관 수·스냅샷·Undo 끊는 이벤트·시드·판 저장·판 끝 이벤트·프로필도 행동마다 저장·시작할 때 이어 하기·기록 보관 줄 수·Modify 누락 검사)은 코드가 아니라 DB › 시작 파일 › **Bootstrap** 화면에서 고른다([01장](01-StartProject.md) 4절). 코드는 시작 설정이 만든 `Client/Bootstrap.cs`(화면 위 **cs 열기**)에 있다.

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
static void Hook()
{
    GBibleRuntime.Configuring += Configure;
    GBibleRuntime.Started += Started;
}

static void Configure(SessionOptions options)
{
    // 화면에서 고를 수 없는 것만. 주로 값을 계산하는 저장 변환(필드 옮김·키 이름 바꿈·키 삭제는 DB › 개발 › 저장 변환 표).
    // options.Migrations.Add(4, "골드 단위 10배", state => { … });
}

static void Started(GameSession session)
{
    // RelicRules.Register(session, …)  session.Actions.Register<…>(…)  session.Events.Subscribe<…>(…)
    // 개발 명령은 DB › 개발 › 개발 명령(결과 Block 조립, 코드가 필요하면 [BindTo]).
}
```

> [!note]- 줄마다의 뜻
> - `AfterAssembliesLoaded` — `GBibleRuntime`은 첫 씬을 불러오기 전에 세션을 만든다. 그보다 먼저 연결해야 하므로 이 시점이다. `Awake`에서는 늦다.
> - `Configuring` — 세션을 만들기 직전(세션은 아직 없다). 쓰는 곳은 둘이다. 출시 뒤 저장 변환 등록(`options.Migrations`, [10장](10-SaveAndRelease.md) 7절)과 조건에 따른 덮어쓰기(데모 빌드는 Undo 끄기, 일일 도전 시드 — [07장](07-GenreRecipes.md) 2·4절). 저장 변환은 반드시 여기서 등록한다(프로필을 곧바로 불러오므로). 저장 버전은 등록한 변환 중 가장 높은 번호를 따라 저절로 정해진다.
> - 코드가 화면 값을 바꾸면 코드가 이기고, 모니터 타임라인에 "코드(Configuring)가 DB › Bootstrap의 세션 옵션을 바꿨습니다: …"가 남는다. 화면 칸과 `SessionOptions`의 대응은 [07장](07-GenreRecipes.md) 2절.
> - `Started` — 세션 준비가 끝난 뒤(DB 데이터 등록·저장 불러오기 다음). 프로젝트 기능의 감시 등록 함수 호출(Rules의 `…Rules.Register`), 행동 처리기, 이벤트 받기를 등록한다. 개발 명령은 DB › 개발 › 개발 명령에 둔다(출시 빌드에서는 저절로 빠진다). 플레이어 데이터는 DB가 이미 정의했으므로 여기서 `Define`하지 않는다.
> - 도메인 리로드를 꺼도 매 플레이마다 다시 연결되도록 시스템이 이벤트를 비우므로, 이 연결은 매번 이렇게 한다.

---

## 2. 체크리스트 모음

장마다의 체크리스트를 그대로 모았다.

### 01. 프로젝트 시작하기

- [ ] 시작 설정을 적용했다. `Assets/Code/Rules·Client·Editor·Tests`, DB › 플레이어 데이터 `run`·`profile`이 있다.
- [ ] 쓸 선택 모듈을 켰다.
- [ ] 규칙 코드는 Rules에, Unity에 붙는 코드는 Client에 있다.
- [ ] `RunState`·`Profile`에 필드를 채웠다.
- [ ] DB › 시작 파일 › Bootstrap에서 Undo 방식·보관 수·시드·판 저장을 정했다.
- [ ] DB › 개발 › 개발 명령에 명령을 만들었고, 모니터에 고정 시드가 보이고, 그 명령 → Undo가 된다.

### 02. 기획을 코드 구조로

- [ ] 기획 문장마다 어느 절(데이터·구역·행동·이벤트·키워드·DB·Block·id)인지 적었다.
- [ ] 판 값은 `RunState`, 남길 값은 `Profile`에 있고, 섞이지 않았다.
- [ ] 판 데이터에는 원본 ID와 바뀌는 값만 있다. 고정 값은 원본에서 읽는다.
- [ ] 계산되는 값을 필드로 두지 않았고, 화면 상태는 MonoBehaviour 쪽이다.
- [ ] 종류를 자식 클래스가 아니라 키워드 필드·기능 필드로 나타냈다.
- [ ] 입력과 규칙의 한 걸음이 모두 행동이고, 행동 필드는 id와 값뿐이다.
- [ ] 반응할 순간이 모두 `[GameEvent]` 이벤트이고, 조건·로그가 읽을 값이 필드에 있다.
- [ ] 찾거나 가리킬 객체에 `id`가 있고, 연결은 id 글자다.
- [ ] 클래스 목록과 DB 항목 목록을 팀과 맞췄다.

### 03. 게임 루프

- [ ] 처음 뜨는 씬에 런 스크립트가 하나 있고, 입력마다 행동 하나를 보내는 창구가 있다.
- [ ] 판을 이어 하거나 새로 여는 코드가 게임에서 한 번만 돈다.
- [ ] 플레이어 데이터를 바꾸는 코드가 모두 행동(또는 그 안에서 불리는 Block·`[BindTo]`·규칙 함수) 안에 있다.
- [ ] 한 입력에 따라오는 단계가 Undo 한 번에 돌아가는 것을 모니터로 확인했다.
- [ ] 규칙상의 실패는 `InvalidOperationException`으로 던지고, 런 스크립트는 그것만 잡는다.
- [ ] 반응할 순간마다 행동 안에서 이벤트를 쏜다.
- [ ] 화면은 읽기·보내기·받기만 하고(연출은 `SubscribeAfterAction`, 다시 그리기는 `GameView.Redraw`), 받은 것을 `Dispose`한다.
- [ ] 시간이 걸리는 효과는 나중에 새 행동으로 실행한다.
- [ ] 씬 사이에 값을 static으로 넘기지 않고, 띄울 씬을 데이터로 정한다.

### 04. 콘텐츠 더하기

- [ ] 새 항목마다 클래스 → 항목 → 이벤트 → Trigger → 결과 → 등장 → 저장 → 시험·검증을 밟았다.
- [ ] 수치만 다른 항목에 코드를 쓰지 않았다.
- [ ] 쓰는 순간 효과가 나는 인스턴스에 사용 기능이 붙어 있다(`[BindTo]`로 결과를 쓰는 것도).
- [ ] 새 Block은 동작 하나만 하고 `[Serializable]`·`[BlockInfo]`가 있다. 대상 Block은 고르기만 한다.
- [ ] `[BindTo]` 인자는 `GameIds` 상수다.
- [ ] 판 데이터에는 원본 ID와 바뀌는 값만 들어간다.
- [ ] 도전과제의 결과는 프로필 쪽 값에 준다.
- [ ] 프로젝트 기능은 `BoundOutcomes`를 확인하고 `RunOutcomes`로 결과를 실행한다.
- [ ] 항목의 ID가 게임에 나오는 자리에 들어가 있다.
- [ ] 모니터에서 반응과 Undo를 확인했고, DB 사이드바가 "검증 통과"다.

### 05. 기획자와 함께 일하기

- [ ] 프로그래머가 만들 것과 기획자가 채울 것을 표로 적어 공유했다.
- [ ] 고르는 값은 모두 키워드 그룹이고 필드에 `[Keyword]`가 있다(enum 칸이 없다).
- [ ] 게임이 채우는 칸은 `[Hide]`, 조건부 칸은 `[ShowIf]`로 정리했다.
- [ ] 다른 DB 항목을 가리키는 칸(원본·판 데이터 모두)에 `[DataRef]`가 있다.
- [ ] 모든 Block에 `[BlockInfo]` 이름·분류·설명이 있고, 칸이 있는 Block에는 `Sentence`가 있다.
- [ ] 비면 안 되는 칸에 필수, 그림 칸에 스프라이트 규격 그룹을 정했다.
- [ ] DB 종류·Block에 필요한 `ValidateData`·`Validate`를 넣었다.
- [ ] 코드에는 ID 문자열 대신 `GameIds`를 쓴다.
- [ ] DB 사이드바가 "검증 통과"다.

### 06. 화면과 연출

- [ ] 화면 코드는 Client 폴더에 있고, 플레이어 데이터를 읽기만 한다.
- [ ] 화면 갱신은 `GameView.Redraw`로 등록해 프레임 끝에 한 번 그린다.
- [ ] 등록·받기는 `OnEnable`, 해제(`Dispose`)는 `OnDisable`에서 한다.
- [ ] 버튼은 id를 쥐고 행동을 보내며, 실패 예외를 잡아 안내한다.
- [ ] 연출은 `SubscribeAfterAction`으로 받아, 성공한 행동의 이벤트만 튼다.
- [ ] 연출의 무작위는 `UnityEngine.Random`, 규칙의 무작위는 `Game.Random`이다.
- [ ] 화면의 모든 글이 텍스트 키이고, DB 항목 이름은 `<ID>.name`이다. 언어를 바꾸면 다시 그려진다.
- [ ] 다이얼로그는 말·연출만, 규칙이 있는 일은 행동이 맡는다.
- [ ] 판 데이터에는 원본 ID와 바뀌는 값만 있고, 이름·그림은 원본에서 읽는다.

### 07. 장르별 설정

- [ ] Bootstrap 화면에서 Undo 방식과 보관 수(또는 스냅샷 간격·보관 수)를 골랐다.
- [ ] 되돌릴 수 있는 범위(턴 안 / 무제한 / 없음)를 정하고, 필요하면 Undo 끊는 이벤트를 골랐다.
- [ ] 판이 끝나면 버릴 값은 `run`, 남길 값은 `profile`에 있다.
- [ ] 규칙의 무작위는 모두 `Game.Random(이름)`이고, 용도마다 흐름 이름이 다르다.
- [ ] 개발 빌드는 시드를 고정한다.
- [ ] 쿨다운·지속 시간은 게임 시계 기준 시각으로 적고, 매 프레임 바뀌는 값은 플레이어 데이터에 넣지 않았다.

### 08. 시험과 디버깅

- [ ] DB › 시작 파일 › Bootstrap에서 개발 빌드 시드를 고정했다.
- [ ] 시험 준비용 개발 명령이 DB › 개발 › 개발 명령에 있고, 누르면 행동 하나로 기록·Undo된다. 규칙을 거쳐야 하는 명령만 `[BindTo]` 코드다.
- [ ] 버그를 보면 판을 저장하고, 저장 파일과 시드를 보고에 붙인다.
- [ ] 타임라인에서 값 하나의 변경 원인을 따라갈 수 있다.
- [ ] 도전과제·로그·유물의 반응을 이벤트 호출·트리거로 달성으로 확인했다.
- [ ] 지켜야 할 규칙(실패 취소, Undo, 같은 시드 같은 결과)이 `Assets/Code/Tests`에 테스트로 있다.
- [ ] 타임라인·콘솔에 `[MomoiProject]` 경고가 남아 있지 않다.

> [!note]- 다른 장르라면
> - **실시간 액션**: 행동 단위가 아니라 스냅샷으로 되감는다(Bootstrap의 Undo 방식 "스냅샷"). 모니터 머리에 "되감기 스냅샷 n (n초 간격)"이 보이고, **Undo**는 마지막 스냅샷으로 되감는다. 버그 직전으로 돌아가 보려면 `session.CaptureSnapshot()`을 부르는 개발 명령(`session.Debug.Register`)을 만들어 두고 그 순간 근처에서 누른다. 빠른 장면은 모니터의 **일시정지**·**1프레임**·배속(0.25× …)으로 늦춰 본다.
> - **턴제 전술**: AI처럼 판단이 복잡한 코드는 AI가 고른 행동의 `Describe()`를 읽기 쉽게 쓴다("오크1 → 영웅 공격"). 행동 탭·타임라인만으로 AI의 선택을 따라갈 수 있다.
> - **수집형·방치형**: 프로필이 오래 쌓이므로 명령 › 저장 › **프로필 삭제**(다음 플레이부터)와 **새 판**으로 처음 상태를 자주 시험한다. 오래된 프로필 파일을 몇 개 보관해 두었다가 출시 전 저장 변환을 시험한다.

### 09. 3요소를 지키는 습관

- [ ] 모든 데이터 클래스와 그 부모·안쪽·요소 클래스에 `[Serializable]`이 있다.
- [ ] 모든 이벤트 클래스에 `[GameEvent]`가 있다.
- [ ] `HashSet`·`Queue`·중첩 컬렉션·컬렉션 그대로의 플레이어 데이터 값이 없다.
- [ ] 플레이어 데이터를 바꾸는 코드는 모두 행동·Block·결과·`[BindTo]` 안에 있다.
- [ ] 행동 필드는 id와 숫자·글자·enum뿐이다.
- [ ] 판 데이터에는 원본 ID와 바뀌는 값만 있고, 고정 값(이름·비용·그림·사용 기능)은 원본에서 읽는다.
- [ ] 이벤트를 받는 씬 코드는 연출·UI만 하고(`SubscribeAfterAction`), `OnDisable`에서 `Dispose`한다.
- [ ] 규칙 난수는 `Game.Random`, 연출 난수는 `UnityEngine.Random`이다. 시간 규칙은 게임 시계 기준이다.
- [ ] 게임 값을 담은 static 변수가 없고, Block·결과 코드는 `GBibleRuntime` 대신 받은 세션을 쓴다.
- [ ] 텍스트 인자는 `("이름", 값)` 형태다.
- [ ] 콘솔·타임라인에 "예전 방식(리플렉션)", "읽기 코드가 없어 리플렉션으로 읽습니다", "저장할 수 없는 참조입니다" 경고가 없다(있다면 이유를 안다).
- [ ] DB › 시작 파일 › Bootstrap에서 Undo 보관 수를 정했다.

> [!note]- 다른 장르라면
> - **실시간 액션**: 위치·속도는 매 프레임 MonoBehaviour에서 움직이고, 규칙에 필요한 값(체력·쿨다운 시각·현재 칸)만 플레이어 데이터에 둔다. 입력은 여전히 행동이지만 Undo는 스냅샷 되감기(Bootstrap의 Undo 방식 "스냅샷")다. 쿨다운은 `Time.time`이 아니라 `session.Clock.Time` 기준 시각으로 적어야 되감을 때 함께 돌아간다.
> - **시뮬레이션·방치형(유닛 수천 개)**: 객체 수가 성능을 정한다. 계산으로 얻을 수 있는 값(누적 생산량 등)은 저장하지 않고, 자주 바뀌는 묶음을 별도 플레이어 데이터로 나눈다.

### 10. 저장과 출시

저장

- [ ] 판이 끝나면 버릴 값은 `Session`, 남을 값은 `Meta` 구역이다.
- [ ] DB › Bootstrap에서 판 저장과 판 끝 이벤트를 골랐다.
- [ ] 끝난 판은 이어 하기가 열지 않는다(`RunState.IsActive`).
- [ ] '이어 하기'는 `HasSavedRun`일 때만 켜지고, 불러오기 실패 때 새 판으로 넘어간다.
- [ ] '새 판'은 `GBibleRuntime.NewRun(session)`을 행동 밖에서 부른다.
- [ ] 플레이어 데이터에 에셋 참조 대신 ID를 둔다.
- [ ] 모니터 저장 명령으로 저장 → 끄기 → 불러오기를 해 보고 값·난수 결과가 이어지는 것을 봤다.

출시 전

- [ ] DB 사이드바 아래가 "검증 통과"다.
- [ ] 콘솔에 "예전 방식(리플렉션)"·"저장할 수 없는 참조입니다"·`[BindTo]` 연결 경고가 없다(있다면 이유를 안다).
- [ ] 저장 값 안의 DB 항목 ID 필드에 `[DataRef(종류)]`가 붙어 있다.
- [ ] `MomoiProject → 출시됨 (저장 호환 유지)`에 체크가 있다.
- [ ] 출시 뒤 구조를 바꾸면 DB › 개발 › 저장 변환에 줄을 더한다(값 계산이 필요하면 `Configure`에 `Migrations.Add`)는 것을 팀이 안다.

> [!note]- 출시 전 더 볼 것
> - 검증 오류가 있으면 빌드가 "데이터 검증 오류 n건으로 빌드를 중단합니다"로 막힌다.
> - 필드 규칙의 필수 값이 비어 있지 않다(모니터 머리에 "필수 값 비어 있음"이 없다).
> - 콘솔에 "읽기 코드가 없어 리플렉션으로 읽습니다"가 없다.
> - 개발용 시드 고정과 개발 명령은 출시 빌드에서 저절로 꺼진다(개발 명령은 `session.Debug.Enabled`가 거짓이면 등록되지 않는다). 코드로 건 `session.Debug.Register`도 같으니 `if (Debug.isDebugBuild)`로 감싸지 않는다.
> - 쓰지 않는 선택 모듈(다이얼로그·인벤토리)을 껐다.
> - Undo 보관 수와 판 저장 설정이 출시 빌드에 맞다.
> - `GameIds.cs`가 최신이다(DB를 저장하고 컴파일까지 끝났다).

### 11. 멀티플레이 대비

- [ ] 모든 입력이 `Game.Do`·`Game.Use`로 들어간다.
- [ ] 행동 필드에 객체 참조·에셋·델리게이트가 없다.
- [ ] 게임 값을 바꾸는 코드는 행동·Block·결과·`[BindTo]` 안에만 있다.
- [ ] 규칙 난수는 모두 `Game.Random`이다.
- [ ] Rules 코드에 `GBibleRuntime` 정적 입구와 static 게임 상태가 없다.
- [ ] UI는 `Do` 직후 결과를 읽지 않고 알림을 받아 그린다.
- [ ] 실패는 입력 창구 한 곳에서 받는다.
- [ ] 이벤트를 받는 화면 코드는 값을 바꾸지 않고, 연출은 `SubscribeAfterAction`으로 받는다.
- [ ] 플레이어가 여럿이면 `actorId`를 채우고, 남에게 숨길 값은 별도 키로 나눴다.
- [ ] 게임 밖에 영향을 주는 결과는 `RewardEffect.Irreversible`이다.

## 3. 본문에서 쓰지 않은 기능

본문(01~11장)의 카드 던전 흐름에 자리가 없었던 기능을 짧게 적는다. 자세한 것은 매뉴얼 장을 본다.

### 3-1. 처리기를 따로 등록하는 행동 — `GameAction` + `Actions.Register`

행동 클래스에는 데이터만 두고 처리 코드는 시작 코드에 둔다. 처리 코드를 행동 클래스 밖에 모으고 싶을 때(서버 쪽에서만 처리를 바꾸고 싶을 때 등) 쓴다. 01장 4절의 `Started` 표에 이름만 나온다.

```csharp
[Serializable]
public sealed class SkipFloor : GameAction
{
    public int floors = 1;
    public override ActionVisibility Visibility => ActionVisibility.Developer;
    public override string Describe() => $"[개발] {floors}층 건너뛰기";
}
```

`GameAction`을 직접 상속하면 `Describe()`를 꼭 써야 하고, 처리기를 `Started`에서 한 번 등록한다: `session.Actions.Register<SkipFloor>(action => session.Game.Data().Run.floor += action.floors);`. 한 형식에 처리기는 하나다. 대부분은 `SimpleAction`·`GameAction<T>`로 충분하다. → [02. 행동과 Undo](../Manual/02-Actions-Undo.md) 2.5

### 3-2. 코딩한 결과 Block — `Reward`

대상 없이 게임에 무언가를 하는 결과다. 기획자가 결과 칸의 **+ 결과 추가 › 코딩한 결과**에서 고른다.

```csharp
[Serializable, BlockInfo("누적 골드", Category = "카드 던전", Description = "프로필의 누적 골드를 늘립니다.")]
public class AddTotalGold : Reward
{
    public int amount = 50;

    public override RewardEffect Effect => RewardEffect.Reversible;            // 플레이어 데이터 안에서만
    public override string Describe(INameResolver names) => $"누적 골드 +{amount}";
    public override void Grant(GameSession session) => session.Game.Data().Profile.totalGold += amount;
}
```

> [!note]- 줄마다의 뜻
> - `Effect` — 플레이어 데이터 안에서만 일어나면 `Reversible`, 결제처럼 게임 밖에 영향을 주면 `Irreversible`([11장](11-MultiplayerReady.md) 2절).
> - `Grant(session)` — 결과를 실행한다. 그 순간의 컨텍스트는 `Context`(protected)로 본다.
> - `Validate(IValidationContext)`를 덮어쓰면 수치 검사를 더할 수 있다(`context.Error`는 빌드를 막는다).
> - `Reward` 클래스에 `[BindTo(ID)]`를 붙이면 그 항목의 결과가 이 클래스로 바뀐다(필드 값은 초기값 그대로 쓰인다).
> - `[BindTo]` 함수·클래스의 모양이 틀렸으면 게임이 켜질 때 콘솔에 경고가 난다. 서버·테스트에서는 `BoundOutcomes.Problems`(글자 리스트)를 직접 읽는다.

→ [12. 결과](../Manual/12-Outcomes.md) 3-2, [13. `[BindTo]`](../Manual/13-BindTo.md) 2-7·2-8

### 3-3. 달성·진행도·로그를 코드로 읽기

도전과제 목록 화면이나 진행 막대는 세션에서 읽는다.

```csharp
if (session.Achievements.IsUnlocked(achievementId)) return "달성";
return session.Achievements.TryGetProgress(achievementId, out var now, out var goal) ? $"{now} / {goal}" : "진행 중";
```

> [!note]- 자세히
> - 진행도는 조건(로그 비교 등)으로 달성하는 도전과제에만 있다.
> - 로그 값은 `session.Stats.Get(로그 ID)`, 이벤트 없이 코드에서 바로 기록할 때는 `session.Stats.Report(로그 ID, 값)`(집계 방식을 따른다).
> - 프로젝트 기능의 Trigger도 `trigger.TryGetProgress(session, out now, out goal)`로 같은 진행도를 준다.
> - 도전과제가 달성되는 순간 기본 이벤트 `AchievementUnlocked`(필드 `Id`·`DisplayName`)를 쏘므로, 달성 알림 화면은 그것을 받는다.

→ [14. 도전과제](../Manual/14-Achievements.md), [15. 로그](../Manual/15-Stats-Logs.md)

### 3-4. 기록 용량과 원인 묶기 — `Journal`

`session.Journal.Append`로 기록에 한 줄을 남기는 법은 [08장](08-TestAndDebug.md) 2절에서 썼다. 여러 줄을 한 원인 아래로 묶으려면 `Cause`를 쓴다.

```csharp
var root = session.Journal.Append(JournalKind.System, "Shop", "상점 열림");
using (session.Journal.Cause(root.Id)) session.Journal.Append(JournalKind.System, "Shop", "재고 새로 고침");
```

> [!note]- 자세히
> - `Cause` 범위 안에서 남긴 줄은 타임라인 묶어 보기에서 원인 줄 아래로 들어간다.
> - 기록은 고리 버퍼라 보관 줄 수(기본 4096줄)를 넘으면 오래된 줄부터 밀려난다. 긴 세션에서 앞부분이 사라지면 DB › 시작 파일 › Bootstrap › 고급의 **기록 보관 줄 수**를 늘린다(코드 값은 `SessionOptions.JournalCapacity`).

→ [22. 모니터와 디버깅](../Manual/22-Monitor-Debug.md) 2장

### 3-5. 키워드를 실행 중에 읽기 · 하위 그룹

화면에 키워드 그룹의 표시 이름이나 목록이 필요하면 `Keywords`로 읽는다: `Keywords.GroupOf(typeof(Rarity)).DisplayName`("등급"), `Keywords.Find("legendary")`. 목록은 처음 쓸 때 한 번 모은다. 그룹 static class 안에 static class를 하나 더 두면 하위 그룹이 된다(키워드 탭에서 폴더로 보인다). 키워드 ID는 모든 그룹에서 하나뿐이어야 한다. → [05. 키워드](../Manual/05-Keywords.md) 2.2·2.5

### 3-6. 이벤트 도구용 멤버

- 이벤트 클래스가 `IDescribable`을 구현하면(`public string Describe() => $"{cardId}: {from} → {to}";`) 기록의 문장이 그것이 된다. 필드가 많거나 리스트가 있어 기본 문장이 읽기 어려울 때 쓴다.
- `session.Events.SubscribeAll(evt => …)` — 형식과 상관없이 모든 이벤트를 받는다. 디버그 출력·이벤트 중계·리플레이 녹화용. 기록 문장은 `EventBus.Describe(evt)`, 표시 이름은 `EventBus.DisplayNameOf(형식)`.
- `session.Events.PublishObject(evt)` — 형식을 실행 중에만 알 때 쏜다. 콘솔 명령 같은 도구에서만 쓴다.
- `Game.Use`가 끝나면 기본 이벤트 `AbilityUsed`("능력 사용", 필드 `Ability`·`User`·`Targets`)를 쏜다. 카드·물약 사용 연출을 한곳에서 받을 때 쓸 수 있다.

→ [10. 이벤트](../Manual/10-Events.md) 2-5~2-10

### 3-7. 찾기·저장소 보조 멤버

- `Game.All<T>(키)` — 플레이어 데이터 하나 안의 `T`만 모은다(두 플레이어의 보드를 따로 볼 때).
- `Game.Find(id)` — 형식을 모를 때 id로 찾는다. 형식을 알면 `Find<T>`.
- `session.Data.Get<T>(키)`·`Set(키, 값)`·`Modify<T>(키, 람다)` — 생성된 `Game.Data()` 대신 키 글자로 읽고 쓴다. `Modify`는 람다가 끝날 때 그 키를 Sync하고, 람다 안에서 예외가 나면 그 키를 되돌린다.
- `session.UndoCount` — 되돌릴 수 있는 횟수(Undo 버튼을 흐리게 할 때).
- `session.Data.TransactionUndone` — Undo가 끝났을 때 오는 저장소 이벤트. 기본 판 저장("행동마다 저장")이 Undo 뒤 저장에 쓴다. 게임 코드가 직접 쓸 일은 드물다.
- `[PlayerData]` — "플레이어 데이터로 쓰는 클래스"라는 표시. 생성 코드에는 `[Serializable]`과 같은 뜻이고, 늘 `[Serializable]`과 함께 쓴다.
- **Modify 누락 검사**(DB › 시작 파일 › Bootstrap › 고급, 코드 값 `SessionOptions.DetectUntrackedChanges`) — 예전 방식 데이터를 `Modify` 밖에서 고친 것을 찾는 검사. 개발 빌드에서만 켜진다.

→ [01. 플레이어 데이터](../Manual/01-PlayerData.md) 2장, [03. Game API](../Manual/03-GameApi.md) 2장

### 3-8. 저장 보조 멤버

- `GameSession.IsSaveCompatible(json, session.SaveSchema, out reason)` — 저장 JSON이 지금 데이터 구조와 맞는지. 개발 중 저장 초기화의 판단 기준이다. 세이브 슬롯을 여럿 둘 때 "이 버전에서 못 읽는 저장"을 미리 표시하는 데 쓸 수 있다.
- `GBibleRuntime.Stopped` — 게임을 끝낼 때 프로필·판 저장이 끝난 뒤 불린다(외부 서비스에 끝을 알릴 때).

→ [06. 저장·불러오기](../Manual/06-SaveLoad.md) 2.9, [20. 시작하기](../Manual/20-Setup.md) 4절

### 3-9. 인스턴스를 에셋으로 다루기 · 코드로 인스턴스 주기

- `GameDatabase.Load().All<InstanceData>()` — 인스턴스 에셋을 직접 훑는다. `asset.CreateInstance<T>()`는 원본의 깊은 사본, `InstanceData.Copy(객체)`는 아무 객체의 깊은 사본이다. 보통은 ID만 알면 되므로 `session.CopyInstance(id)`(사본)나 `session.Instances(id)`(원본, 읽기만)를 쓴다.
- 결과 '플레이어 데이터에 주기'(`PlayerDataReward`)는 코드로도 만들어 `Grant(session)`할 수 있다.

> [!note]- `PlayerDataReward`를 코드로 쓸 때
> 클래스 필드나 클래스 리스트 필드에 인스턴스 ID를 주면 그 인스턴스의 **사본**이 들어간다(`GrantKind.Instance`·`List`). 카드 던전의 덱은 원본 사본이 아니라 원본 ID만 든 `DeckCard`라서 이 방법을 쓰지 않고 규칙 함수(`Rules.NewCard`)로 넣는다. 원본 사본을 판 데이터에 그대로 두는 게임(아이템마다 강화 수치가 다른 RPG 장비 등)에서 쓴다.

→ [04. 인스턴스](../Manual/04-Instances.md) 2.9·2.10

### 3-10. 에디터 설정과 도구

- **MomoiProject › 내 코드 범위** — 클래스를 고르는 목록(값 형식·인스턴스 형식·필드 규칙)에 보일 폴더·네임스페이스. 플러그인 폴더의 클래스가 목록에 섞이거나 내 클래스가 안 보일 때 고친다(`ProjectSettings/MomoiProject/ProjectCode.json`).
- **DB › 프리팹** — 프리팹을 DB에 등록해 컴포넌트 필드를 탐색기로 고친다. 수정은 바로 적용된다(작업본을 거치지 않는다).
- **`GameFeatures`**(에디터 코드, `GBible.EditorTools`) — 선택 기능이 켜졌는지 에디터 도구에서 확인한다: `GameFeatures.IsActive(GameFeatures.Find("dialogue"))`.
- **`TextCatalog`** — 테스트·도구에서 텍스트 표를 따로 만들어 문장 틀을 시험한다: `new TextCatalog { SourceLanguage = "ko", Language = "ko" }` → `SetTable` → `Get`.
- **결과 '다이얼로그 재생'** — 코드 없이 도전과제·유물 같은 항목의 결과로 다이얼로그를 연다. 카드 던전은 상점 층에서 코드(`Dialogues.Play`)로 열었다([06장](06-Presentation.md) 6절).

→ [21. DB](../Manual/21-Editor-Database.md) 6.9·8, [20. 시작하기](../Manual/20-Setup.md) 6절, [23. 텍스트와 현지화](../Manual/23-Text-Localization.md) 7절, [24. 다이얼로그](../Manual/24-Dialogue.md) 5.5

### 3-11. 난수 뽑기의 나머지 · 로그 시험 API

- `RandomStream`은 `Range(min, max)` 말고도 `Chance(p)`(p 확률로 참), `NextDouble()`([0, 1) 실수), `NextUInt()`·`NextULong()`을 준다. 치명타라면 `Game.Random("damage").Chance(0.15)`. 흐름 이름은 [07장](07-GenreRecipes.md) 4절처럼 용도마다 나눈다.
- `session.TrySimulateStat(로그 ID, 양, out 설명, out 오류)` — 모니터 로그 탭의 "이벤트로 더하기"와 같다. 반영 규칙을 거꾸로 따라가 실제 이벤트를 쏜다(행동 하나, Undo 가능). 테스트·개발 명령에서 "처치 100회"를 만들 때 쓴다.
- `EventSimulator.Create(형식, 이름→값)` — 이벤트 객체를 필드 이름으로 만든다(도구용).

→ [07. 난수와 시간](../Manual/07-Random-Time.md) 2.2, [22. 모니터와 디버깅](../Manual/22-Monitor-Debug.md) 4절

---

## 4. 기능 사용 표

프로그래머·개발자가 직접 쓰는 기능과, 매뉴얼 장, 이 가이드에서 쓴 곳이다. "§" 뒤는 그 장의 절 번호다(예: 03 §6 = 3장 6절, 12 §3-4 = 이 부록 3-4). 여러 곳이면 먼저 나온 곳부터 적었다. "(언급)"은 접힌 칸이나 한 줄로만 나온 곳이다.

### 시작·설정

| 기능 | 매뉴얼 | 가이드에서 쓴 곳 |
|---|---|---|
| 설치, `com.unity.nuget.mono-cecil` | 20 | 01 §1 |
| 시작 설정 창(코드 폴더·asmdef·`Bootstrap.cs`·`PlayerData.cs`·DB 에셋·`run`/`profile` 자동 생성) | 20 | 01 §1 |
| `GameDatabase`(DB 에셋) | 20 · 21 | 01 §1(시작 설정이 만든다) |
| 선택 기능 켜기(기능 활성화), `#if GBIBLE_*` | 20 | 01 §1 |
| 어셈블리·asmdef 참조 | 20 | 01 §2, 08 §6(Tests) |
| DB › 시작 파일(Bootstrap 화면, RunState·Profile 바로가기) | 20 · 21 | 01 §3·§4 |
| Bootstrap 화면: Undo 방식·보관 수·시드 고정 | 20 · 02 · 07 | 01 §4, 07 §2·§4 |
| Bootstrap 화면: 판 저장·판 끝 이벤트 | 20 · 06 | 01 §4, 10 §2 |
| Bootstrap 화면 › 고급: 기록 보관 줄 수(`JournalCapacity`) | 20 · 22 | 01 §4, 07 §2, 12 §3-4 |
| Bootstrap 화면 › 고급: Modify 누락 검사(`DetectUntrackedChanges`) | 20 · 01 | 01 §4, 07 §2, 12 §3-7 |
| `GBibleRuntime.Session`·`Game` | 20 · 03 | 03 §3 |
| `Configuring`·`SessionOptions`(코드로 덮어쓰기, 저장 변환) | 20 · 02 | 01 §4, 07 §2·§4, 10 §7 |
| `HistoryMode.Snapshots`·`SnapshotInterval`·`SnapshotLimit` | 02 | 01 §4, 07 §2·§6 |
| `HistoryMode.None` | 02 | 01 §4(데모 빌드 예), 07 §6 |
| `CaptureSnapshot` | 02 | 07 §2·§6, 08 §8(다른 장르라면) |
| `Started` | 20 | 01 §4 |
| `Stopped` | 20 | 12 §3-8 |
| `GameIds` 생성(식별자 코드 다시 만들기), `PlayerDataKey<T>` | 20 · 01 | 01 §3, 06 §1(`.Id`) |
| `Game.Data()` | 01 · 03 | 01 §3 |
| `GameDatabase.Load()`·`All<T>()` | 20 | 04 §8 |
| `GameFeatures`(에디터 API) | 20 | 12 §3-10 |

### 플레이어 데이터

| 기능 | 매뉴얼 | 가이드에서 쓴 곳 |
|---|---|---|
| `[Serializable]` 데이터 클래스 | 01 | 01 §3, 02 §2 |
| `[PlayerData]` | 01 | 12 §3-7 |
| DB › 플레이어 데이터(구역·값 형식) | 01 · 21 | 01 §3 |
| 구역 `DataZone.Session`·`Meta` | 01 · 06 | 01 §3, 02 §3, 10 §1 |
| `session.Data.Define`·`IsDefined` | 01 | 01 §3(언급), 08 §6 |
| `session.Data.Get`·`Set`·`Modify` | 01 | 12 §3-7 |
| `Changed`·`DataChanged` | 01 | 06 §1 |
| `Synced`·`Snapshot`·`MirrorReceiver` | 01 · Coding.md | 06 §1(`Synced` 언급), 11 §1 |
| `Permanent()` | 02 | 04 §8, 07 §2 |
| `DescribeChanges` | 01 | 09 §6 |
| `TransactionUndone`(저장소 이벤트) | — | 12 §3-7 |

### 행동과 Undo

| 기능 | 매뉴얼 | 가이드에서 쓴 곳 |
|---|---|---|
| `SimpleAction`·`GameAction<T>`·`target` | 02 | 02 §4 |
| `Describe()` | 02 | 08 §8(다른 장르라면), 12 §1-1 |
| `GameAction` + `Actions.Register` | 02 | 01 §4(언급), 12 §3-1 |
| `Game.Do`·`ActionRecord` | 02 · 03 | 03 §2, 06 §1·§3 |
| 실패 = 예외 = 전체 취소 | 02 | 03 §6 |
| 행동 안의 행동(`Game.Do`·`Game.Use` 중첩) | 02 · 22 | 02 §4, 03 §5 |
| `session.Undo()` | 02 | 01 §5(모니터), 03 §3 |
| `UndoCount` | 02 | 12 §3-7 |
| Undo 끊는 이벤트(Bootstrap), `session.ClearUndo()` | 02 · 20 | 03 §9(다른 장르라면), 07 §2 |
| 시작할 때 이어 하기 + 새 판 행동(Bootstrap) | 06 · 20 | 01 §4, 03 §3, 10 §3 |
| 지연 효과(나중에 `Game.Do`) | 02 | 03 §8 |
| `actorId`·`Visibility` | 02 | 11 §4 |
| `DeveloperEditAction` | 22 | 01 §4, 08 §2 |
| `session.Actions.Executed`·`TransactionAborted` | 02 | 03 §2(언급) |
| `Events.SubscribeAfterAction<T>`(성공한 행동의 이벤트만) | 10 | 06 §3, 03 §7, 11 §3 |
| `GameView.Redraw`(값·언어가 바뀌면 프레임당 한 번 그리기) | 23 | 06 §1·§5 |

### Game API

| 기능 | 매뉴얼 | 가이드에서 쓴 곳 |
|---|---|---|
| `Game.Find<T>` | 03 | 02 §9, 03 §7 |
| `Game.All<T>()` | 03 | 06 §1(언급), 09 §6 |
| `Game.All<T>(키)` | 03 | 12 §3-7 |
| `Game.Find(id)` | 03 | 12 §3-7 |
| `Game.Use` | 03 · 16 | 03 §2, 04 §4 |
| `Game.CanUse` | 03 · 16 | 03 §6 |
| `Game.Random`·`RandomStream.Range` | 03 · 07 | 01 §4(언급), 07 §4 |
| `RandomStream`의 그 밖의 뽑기(`Chance`·`NextDouble` …) | 07 | 12 §3-11 |
| `session.Random.MasterSeed` | 07 | 08 §2 |
| `Game.NewId` | 03 | 02 §9 |
| `Game.Events` | 03 · 10 | 02 §5, 03 §1 |
| `GameApi` 확장 메서드 | 03 | 09 §1(언급) |
| `GameApi.IdOf` | 03 | 09 §3(언급) |

### 인스턴스·키워드

| 기능 | 매뉴얼 | 가이드에서 쓴 곳 |
|---|---|---|
| DB › 인스턴스 | 04 · 21 | 02 §2, 04 §3 |
| `session.Instances`(원본을 복사 없이 읽기) | 04 | 02 §2, 06 §1, 08 §6(테스트), 09 §4 |
| `session.CopyInstance` | 04 | 02(용어), 12 §3-9 |
| `[Hide]`·`[ShowIf]` | 04 | 02 §2, 05 §3 |
| 기능 필드(`[SerializeReference]`)·`Usable` | 04 · 16 | 02 §2·§7, 04 §4 |
| `InstanceData.CreateInstance`·`Copy` | 04 | 12 §3-9 |
| `PlayerDataReward`(코드)·`GrantKind.Instance` | 04 · 12 | 12 §3-9 |
| `[KeywordGroup]`·`[Keyword]` | 05 | 02 §6, 05 §2 |
| 하위 그룹 | 05 | 12 §3-5 |
| `Keywords`(실행 중 조회) | 05 | 12 §3-5 |

### 저장

| 기능 | 매뉴얼 | 가이드에서 쓴 곳 |
|---|---|---|
| `GBibleRuntime.LoadRun` | 06 · 20 | 03 §3, 10 §3 |
| `SaveRun`·`SaveProfile`·`DeleteRun`·`HasSavedRun` | 06 · 20 | 03 §3(언급), 07 §6, 10 §2·§3 |
| 판 저장 설정(끌 때 / 행동마다 / 저장하지 않음), `AutoSaveRun`(코드로 덮어쓰기) | 06 · 20 | 01 §4, 10 §2 |
| 판 끝 이벤트(받으면 판 저장 지우기·프로필 바로 저장) | 06 · 20 | 01 §4, 02 §3, 10 §2 |
| `GBibleRuntime.NewRun(session)`(판 값 + 판 저장 파일 함께), `session.NewRun()`(테스트·서버) | 06 | 03 §3, 10 §3 |
| `ExportMeta`·`ImportMeta`·`ExportRun`·`ImportRun` | 06 | 10 §3(다른 장르라면), 11 §4 |
| `SavePath`·`RunSavePath` | 06 | 08 §1, 10 §5 |
| 개발 중 저장 초기화, 모니터 저장 명령 | 06 · 22 | 08 §1, 10 §5 |
| 출시됨 (저장 호환 유지) | 06 · 20 | 05 §8, 10 §6 |
| 저장 값 안의 DB 항목 ID 자동 변환(`[DataRef]` 필드) | 06 · 21 | 02 §9(언급), 05 §8, 10 §7 |
| DB › 개발 › 저장 변환(필드 옮김·키 이름 바꿈·키 삭제), `options.Migrations.Add`(값 계산), 저장 버전(변환을 따라 저절로) | 06 · 21 | 01 §4(언급), 10 §7 |
| `[FormerlySerializedAs]` | 06 | 10 §5·§7 |
| `session.Data.AddAlias` | 06 | 10 §7 |
| `IsSaveCompatible`·`SaveSchema` | 06 | 12 §3-8 |

### 이벤트·Trigger·결과

| 기능 | 매뉴얼 | 가이드에서 쓴 곳 |
|---|---|---|
| `[GameEvent]`·`Publish` | 10 | 02 §5 |
| `Subscribe`·`Dispose` | 10 | 03 §7, 06 §3 |
| `IDescribable` | 10 | 12 §3-6 |
| `SubscribeAll`·`PublishObject`·`EventBus.Describe` | 10 | 12 §3-6 |
| 기본 이벤트 `AchievementUnlocked` | 10 · 14 | 12 §3-3 |
| 기본 이벤트 `AbilityUsed` | 10 · 16 | 12 §3-6 |
| Trigger 조립(이벤트 받기·조건·사용, AND·OR·NOT) | 11 | 04 §7·§9 |
| 필드 경로 조건(이벤트 값 조건, 플레이어 데이터 검사) | 11 | 04 §9, 06 §6(선택지 조건) |
| `SimpleCondition` | 11 | 02 §1(판단표), 12 §1-6 |
| `Condition`(전부 직접 쓰는 조건) | 11 | 12 §1-6(언급) |
| `[TriggerSlot]`·`[OutcomeSlot]` | 11 · 17 | 04 §8 |
| `session.Triggers.Register`·`RunOutcomes` | 11 · 17 | 04 §8, 11 §2 |
| `Triggers.Watcher.EvaluateAll` | 11 · 17 | 04 §8(다른 장르라면) |
| `TryGetProgress`(도전과제·Trigger 진행도) | 14 · 17 | 12 §3-3 |
| 결과 '플레이어 데이터에 주기'(DB) | 12 | 04 §7 |
| 결과 '로그에 기록'·로그 반영 규칙 | 12 · 15 | 04 §7·§8 |
| 결과 '대상에게 효과'·`Effect<T>`·`TargetSelector<T>` | 12 · 16 | 02 §8, 04 §4·§9 |
| 기본 대상 '사용자 자신'(`SelfTarget`) | 16 | 02 §8, 04 §4, 08 §6 |
| `Reward`(코딩한 결과) | 12 | 02 §1(판단표), 12 §3-2 |
| `RewardEffect` | 12 | 11 §2 |
| 컨텍스트 `TriggerContext` | 12 · 13 | 04 §5 |
| `[BlockInfo]`(`Category`·`Description`·`Sentence`) | 11 · 12 | 02 §4, 04 §4, 05 §5 |
| `Validate`(조건·결과·효과·대상 Block) | 11 · 12 | 05 §7 |
| `[BindTo]` 함수 | 13 | 04 §5 |
| `[BindTo]` + `Reward` 클래스 | 13 | 12 §3-2 |
| `BoundOutcomes.Has`·`Create` | 13 · 17 | 04 §8 |
| `BoundOutcomes.Problems` | 13 | 12 §3-2 |

### DB 기능

| 기능 | 매뉴얼 | 가이드에서 쓴 곳 |
|---|---|---|
| 도전과제(DB) | 14 | 04 §7 |
| `session.Achievements.IsUnlocked` | 14 | 12 §3-3 |
| 로그(DB, 범위·집계) | 15 | 04 §7 |
| `session.Stats.Report`·`Get` | 15 | 12 §1-7·§3-3 |
| 능력(DB › 능력) | 16 | 04 §6(다른 장르라면) |
| `AbilityDefinition`·`session.Abilities.Define` | 16 | 08 §6 |
| DB 종류 `GameData`·`[Database]` | 17 · 21 | 02 §7, 04 §8 |
| `ValidateData` | 17 | 04 §8, 05 §7 |
| `[DataRef]` | 17 · 21 | 02 §2·§9, 05 §4 |
| `[FieldLabel]` | 17 | 04 §8, 05 §3 |
| `[PlayerDataRef]`·`[StatRef]`·`[AchievementRef]`·`[EventTypeRef]` | 11 · 12 | 05 §4 |
| DB 저장 흐름(작업본 → Ctrl+S) | 21 | 01 §3, 05 §9 |
| ID 바꾸기·삭제 보호 | 21 | 05 §8 |
| 검증(사이드바 "검증 통과", 빌드 중단) | 21 | 04 §11, 05 §7 |
| 클래스 탭(클래스 상세, `[Hide]` 토글, 필수) | 21 | 05 §3·§6 |
| 내 코드 범위 | 21 | 12 §3-10 |
| 프리팹 탭 | 21 | 12 §3-10 |
| 필드 규칙(필수) | 27 | 05 §6 |
| 스프라이트 규격·`SpriteSpecs.Get` | 27 | 05 §6 |

### 모니터·디버깅

| 기능 | 매뉴얼 | 가이드에서 쓴 곳 |
|---|---|---|
| 모니터 열기, 데이터·명령·행동 탭, Undo 버튼 | 22 | 01 §5 |
| DB › 개발 › 개발 명령(결과 Block 조립, `[BindTo]`), 결과 Block "이벤트 쏘기" | 22 · 12 | 01 §4, 04 §10, 08 §2 |
| `session.Debug.Register`(코드로 등록), `session.Debug.Enabled`(출시 빌드에서 꺼짐) | 22 | 08 §2 |
| 타임라인(묶어 보기·인과 관계) | 22 | 04 §11, 08 §3 |
| 이벤트 호출 | 22 | 04 §11, 08 §5 |
| 도전과제·로그 테스트(트리거로 달성·강제 달성·이벤트로 더하기) | 22 | 04 §11, 08 §3·§5 |
| `SimulateEvents`·`TrySimulateAchievement` | 22 | 08 §5 |
| `TrySimulateStat`·`EventSimulator` | 22 | 12 §3-11 |
| `Journal.Append` | 22 | 08 §2 |
| `Journal.Cause` | 22 | 12 §3-4 |
| `GameTime`(일시정지·배속·프레임 넘기기) | 07 · 22 | 06 §4, 08 §2(명령 › 시간), 08 §8(다른 장르라면) |
| 게임 시계 `session.Clock` | 07 | 02 §2(다른 장르라면), 07 §5·§6 |
| `session.EndFrame`(세션을 직접 만들 때) | 01 | 08 §6 |
| EditMode 테스트, `Tools/RunCoreTests.ps1` | — | 08 §6 |
| 경고 메시지 대응표 | 22 | 08 §7 |

### 텍스트·모듈

| 기능 | 매뉴얼 | 가이드에서 쓴 곳 |
|---|---|---|
| `Texts.Get`·`Texts.NameOf`·문장 틀(조사·복수·용어) | 23 | 06 §5 |
| `GameLanguage`(`Changed`) | 23 | 06 §5 |
| `LocalizedText` | 23 | 06 §5(언급) |
| 다국어 › DB 이름 키 채우기 · 번역 검사 | 23 | 05 §9, 08 §7(대응표) |
| 시트(CSV) 내보내기·가져오기 | 23 | 05 §9(다른 장르라면, 언급) |
| `TextCatalog` | 23 | 12 §3-10 |
| 다이얼로그 그래프·스탠딩 프로필·UI 템플릿 만들기 | 24 | 06 §6 |
| `Dialogues.Play`·`Signaled`·`IsPlaying`·`ResumeSaved` | 24 | 06 §6 |
| `DialogueUIExtension` | 24 | 06 §6(다른 장르라면) |
| 결과 '다이얼로그 재생' | 24 | 12 §3-10 |
| 인벤토리 `Inventory` 형식 | 25 | 01 §3 |
| 인벤토리 API(`TryRemove`·`slots` …) | 25 | 04 §6, 06 §8 |
| 오디오 `GameAudio`(`PlayBgm`·`PlaySfx`·`SetVolume`·`VolumeChanged`) | 26 | 06 §7 |

---

## 더 보기

- [매뉴얼 목차](../Manual/00-Index.md) — 기능별 사전
- [../Coding.md](../Coding.md) — 게임 코드 규칙
- [../TestMultiplayer.md](../TestMultiplayer.md) — 테스트용 멀티플레이 설계(보류 중)
