# 12. 부록 — 템플릿 · 체크리스트 · 기능 사용 표

본문에서 쓴 것을 다시 찾기 쉽게 모은다. 복사해 시작하는 뼈대(1절), 장마다의 체크리스트가 있는 곳(2절), 본문에서 쓰지 않은 기능(3절), 기능을 찾는 곳(4절)이다.

> [!note]- 이 장에서 쓰는 말
> | 말 | 뜻 |
> |---|---|
> | 행동 (`GameAction`) | 플레이어 입력 하나를 담은 데이터 클래스. 행동 하나 = Undo 한 단위 = 네트워크 한 묶음. |
> | Block | DB 항목 안에서 조립하는 로직 단위. 효과 Block(무엇을)·대상 Block(누구에게)·조건 Block(언제만)·코딩한 결과(`Reward`). AI가 클래스를 만들고 DB 반영 스크립트로 항목에 넣으며, 개발자는 DB에서 문장으로 읽고 칸의 숫자·참조를 바꾼다. |
> | Trigger · Outcome | DB 항목의 "언제(Trigger) → 무엇을(Outcome, 결과)" 두 칸. |
> | 컨텍스트 (`TriggerContext`) | Trigger가 발동한 순간의 정보(세션·받은 이벤트·사용자·고른 대상 …). |
> | `[BindTo]` | DB 항목 하나의 결과를 코드 함수 하나로 바꾸는 어트리뷰트. |

---

## 1. 템플릿

모두 카드 던전에 맞춘 이름이다. 복사한 뒤 이름과 내용을 바꾼다. 줄마다의 뜻은 뼈대 아래 접힌 칸에 있다.

### 1-1. 행동 — 대상이 있을 때

```csharp
[Serializable, BlockInfo("카드 강화")]
public class UpgradeCard : GameAction
{
    public DeckCard card;

    protected override void Run()
    {
        var data = Rules.CardData(Game, card.card);          // 원본: 읽기만
        if (card.upgraded || !data.upgradable) throw Game.Fail("fail.cannotUpgrade", "강화할 수 없는 카드입니다.");   // 실패 = 전체 취소
        var from = card.card;
        card.card = data.upgradeTo;                          // 판 데이터는 원본 ID만 바꾼다(핸드·덱이 가리키는 id는 그대로)
        card.upgraded = true;
        Game.Events.Publish(new CardUpgraded { cardId = card.id, from = from, to = card.card });
    }
}
```

부르는 쪽: `Game.Do(new UpgradeCard { card = deckCard })`.

> [!note]- 줄마다의 뜻
> - `[Serializable]` — 행동은 데이터다. 저장·리플레이·서버 전송에 실린다.
> - `[BlockInfo("…")]` — 모니터 행동 탭·타임라인에 보일 이름.
> - `GameAction` — 모든 행동이 상속하는 클래스 하나. 대상이 있든 없든 같다.
> - `public DeckCard card` — 대상은 객체 그대로 필드로 둔다. 형식에 `public string id;`가 있고 플레이어 데이터 안에 있는 클래스면 대상 필드가 된다. 시스템은 기록·전송에 id만 남기고, `Run` 직전에 그 id로 지금 데이터의 객체를 다시 찾아 넣는다(Undo·불러오기·멀티에서도 맞다). 대상이 여럿이면 필드를 더 두거나 `List<Fighter>`로 둔다. 없을 수 있으면 `null`로 둔다.
> - `Run()` — 매개변수가 없다. 필드와 `Game`으로 규칙을 쓴다.
> - `Rules.CardData` — 비용·강화 대상 같은 고정 값은 원본(인스턴스)에서 읽는다. 판 데이터(`DeckCard`)는 원본 ID와 바뀌는 값만 든다.
> - `throw Game.Fail(키, 기본 문장)` — 규칙상 할 수 없으면 플레이어용 실패(`GameFailure`)를 던진다. 이 행동이 바꾼 값·난수가 모두 되돌아가고 예외가 `Do`를 부른 쪽으로 간다. 글은 텍스트 표에 키가 있으면 번역되고, 없으면 기본 문장이다([06장](06-Presentation.md) 5절). 비용이면 `Game.Pay(키, 필드, 양)`.
> - `Game` — 이 행동이 도는 세션의 창구. `Run`이 도는 동안만 들어 있다.
> - `Game.Events.Publish` — 다른 기능(로그·도전과제·화면)이 반응할 순간에 이벤트를 쏜다.

> [!note]- 대상 필드 자세히
> - 못 찾으면(사라진 카드·죽은 적) "DeckCard 'card7'을(를) 찾을 수 없습니다(행동 'UpgradeCard'의 'card')"로 행동이 실패하고 아무것도 바뀌지 않는다.
> - id가 빈 새 객체를 넣어도 실패한다. 새 객체는 행동 안에서 `Game.NewId("접두어")`로 id를 주어 만든다.
> - id는 플레이어 데이터 전체에서 하나여야 한다(형식이 달라도 겹치지 않게 — `Game.NewId`의 접두어로 나눈다, 관례 3절).
> - DB 원본은 객체가 아니라 `[DataRef]` 원본 ID 글자로 둔다. Unity 객체·델리게이트는 넣지 않는다.
> - `Describe()`를 쓰지 않으면 타임라인 글은 "Block 이름 (대상 id들)"이다(예: "카드 강화 (card7)"). 다른 문장이 필요할 때만 덮어쓴다: `public override string Describe() => $"카드 강화: {card.id}";`

### 1-2. 행동 — 대상이 없을 때

```csharp
[Serializable, BlockInfo("휴식")]
public class Rest : GameAction
{
    protected override void Run()
    {
        var hero = Game.Data().Run.hero;
        var percent = Game.Balance(GameIds.Balance.RestHealPercent);   // 바꿀 숫자는 밸런싱 값(% 0~100)
        hero.hp = Math.Min(hero.maxHp, hero.hp + (int)(hero.maxHp * percent / 100));
    }
}
```

> [!note]- 줄마다의 뜻
> - 1-1과 같은 `GameAction`이다. 대상 필드가 없을 뿐이다. 필요한 객체는 `Game.Data()`·`Game.Find`·`Game.All`로 찾는다.
> - 회복 %처럼 개발자가 바꿀 숫자는 행동 필드 기본값이 아니라 밸런싱 값으로 읽는다(관례 18절). 행동 필드에는 입력(고른 대상·고른 선택지)을 싣는다 — 숫자·글자·bool·enum·원본 ID는 값 그대로 실린다.
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
        if (battle != null) battle.energy += (int)Math.Round(amount * (Use?.Context?.Power ?? 1));   // 효과 배율("수치 2배")을 곱한다
    }

    public override string Describe() => $"에너지 +{amount}";
}
```

> [!note]- 줄마다의 뜻
> - `BlockInfo` — 이름은 DB의 Block 목록에 보이는 글, `Category`는 묶음, `Description`은 설명(꼭 적는다), `Sentence`는 칸을 눌러 고치는 한 줄 문장(`{필드이름}` 자리가 칸이 된다).
> - `Effect<Hero>` — 대상 형식. 대상 Block이 고른 객체 중 이 형식인 것에만 `Apply`가 불린다.
> - `public int amount` — DB에서 정하는 수치(개발자가 바꾼다). 필드 초기값이 기본값이다. 프로젝트 효과 Block은 수치에 효과 배율 `Use.Context.Power`를 곱한다(관례 4절 — 기본 효과 Block은 스스로 곱한다).
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
> - 무엇을 보는지(`CollectDependencies`)와 평가(`Evaluate`)를 직접 쓰는 `Condition` 상속형도 있지만, 시간으로 바뀌는 조건처럼 특별한 경우에만 쓴다(매뉴얼 [11장](../../Manual/11-Triggers-Conditions.md) 4-3).

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
> - `[BindTo(ID)]` — 그 ID의 DB 항목(도전과제·로그·액티브 효과·사용 기능이 붙은 인스턴스, 그리고 연결을 확인하는 프로젝트 고유 기능 — 1-8)의 결과가 이 함수가 된다. DB의 결과 칸은 잠기고 함수 이름과 `cs 열기`가 보인다. ID는 늘 `GameIds` 상수로 — 상수는 DB 반영 스크립트가 항목을 만들고 Unity가 다시 컴파일한 뒤에 생기므로, 반영 스크립트를 먼저 넣고 `Logs/MomoiProject/RunTests.request`로 새로 읽게 한 뒤 `GameIds.cs`에 생긴 것을 보고 쓴다(관례 15절).
> - `static void` — 필수. 접근 수준·클래스 이름은 상관없다.
> - 매개변수는 넷 중 하나: `()` · `(이벤트 e)` · `(TriggerContext c)` · `(이벤트 e, TriggerContext c)`. 받을 이벤트는 첫 매개변수의 형식이고, 컨텍스트는 늘 마지막이다.
> - 이벤트로 발동하는 항목이면 `(BattleWon e, TriggerContext c)`처럼 이벤트를 받는다. 조건으로 달성하는 도전과제·사용 기능·액티브 효과에는 이벤트가 없으므로 `(TriggerContext c)`만.
> - `c.Game` — 세션의 창구. `GBibleRuntime.Game`을 쓰지 않는다(세션이 여럿일 수 있다). static 변수에 상태를 두지 않는다.
> - 로그에 연결하면 자동 기록이 멈추므로 함수가 `c.Session.Stats.Report(c.Owner, 값)`으로 직접 기록한다.

### 1-8. 프로젝트 고유 기능(Trigger → 결과)

패시브 효과(가진 동안 발동)·상태 효과(걸린 동안)로 안 되는 규칙(한 번만 발동하고 완료를 저장·하루 N번·단계 — 매뉴얼 [17장](../../Manual/17-Custom-Features.md) 0-5)일 때만 만든다(관례 0절, [04장](04-AddingContent.md) 8절).

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

// Client/Bootstrap.cs의 RegisterRules: 연결은 한 줄
BlessingRules.Register(session, GameDatabase.Load()?.All<BlessingData>());
```

> [!note]- 런타임 쪽 줄마다의 뜻
> - 나누는 까닭 — 등록 함수(`BlessingRules.Register`)는 규칙이라 Rules에 두고, Unity 시작에 거는 연결은 Client의 `Bootstrap.RegisterRules` 한 줄로 한다([04장](04-AddingContent.md) 8절의 `RelicRules`). `RegisterRules`는 `GBibleRuntime.SessionCreated`에 연결되어 모든 세션(화면 세션·멀티 서버·시험 모드·매칭 방)에 오므로 서버 세션에서도 감시가 돈다. `Started`(화면 세션에만 온다)에 두지 않고, `GameNet.ServerStarted`·`RoomStarting`에서 다시 부르지도 않는다(두 번 등록하면 두 번 발동한다).
> - `GameDatabase.Load()` — 런타임 데이터 목록. `All<T>()`로 그 종류의 항목만 넘긴다.
> - `BoundOutcomes.Has/Create` — 기본 기능과 달리 프로젝트 기능은 `[BindTo]` 연결을 스스로 확인해야 한다. 빼먹으면 에디터는 결과 칸을 잠가 보이는데 실제로는 숨은 예전 결과가 돈다.
> - `session.Triggers.Register(owner, when, repeat, fire)` — Trigger를 감시하다 참이 되는 순간 `fire`를 부른다. `repeat: false`면 한 번 발동하고 끝난다.
> - `RunOutcomes(결과들, 컨텍스트, 출처)` — 결과를 차례로 실행하고 기록(Journal)에 남긴다. `outcome.Run`을 직접 부르지 않는다.
> - 완료 여부처럼 남겨야 할 것은 플레이어 데이터에 둔다. 한 번 일어나면 되돌리지 않을 일이면 `session.Data.Permanent()`로 감싼다([04장](04-AddingContent.md) 8절, [07장](07-GenreRecipes.md) 2절, 매뉴얼 [17장](../../Manual/17-Custom-Features.md) 2-9).
> - 시작할 때 이미 조건을 넘은 항목은 등록 뒤 `session.Triggers.Watcher.EvaluateAll()`로 소급한다.

### 1-9. Bootstrap

설정 값(Undo 방식·보관 수·스냅샷·Undo 끊는 이벤트·시드·판 저장·판 끝 이벤트·프로필도 행동마다 저장·시작할 때 이어 하기·고정 틱·씬 흐름·기록 보관 줄 수·Modify 누락 검사)은 코드가 아니라 DB › 시작 파일 › **Bootstrap** 화면의 칸이다. AI는 DB 반영 스크립트에서 `EditorSet…` 함수로 넣는다([01장](01-StartProject.md) 4절). 코드는 시작 설정이 만든 `Client/Bootstrap.cs`(화면 위 **cs 열기**)에 있다.

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
static void Hook()
{
    GBibleRuntime.Configuring += Configure;
    GBibleRuntime.SessionCreated += RegisterRules;
    GBibleRuntime.Started += Started;
}

static void Configure(SessionOptions options)
{
    // 화면에서 고를 수 없는 것만. 주로 값을 계산하는 저장 변환(필드 옮김·키 이름 바꿈·키 삭제는 DB › 개발 › 저장 변환 표).
    // options.Migrations.Add(4, "골드 단위 10배", state => { … });
}

static void RegisterRules(GameSession session)
{
    // 규칙 쪽 등록 — 모든 세션: BlessingRules.Register(session, …)  session.Actions.Register<…>(…)  session.Events.Subscribe<…>(…)  session.Ticked += …
    // 개발 명령은 DB › 개발 › 개발 명령(결과 Block 조립, 코드가 필요하면 [BindTo]).
}

static void Started(GameSession session)
{
    // 화면 쪽 연결 — 화면 세션만: session.Events.SubscribeAfterAction<…>(e => GameView.Present(…))  GameSettings.Changed += …  GameInput.Use(…)
}
```

> [!note]- 줄마다의 뜻
> - `AfterAssembliesLoaded` — `GBibleRuntime`은 첫 씬을 불러오기 전에 세션을 만든다. 그보다 먼저 연결해야 하므로 이 시점이다. `Awake`에서는 늦다.
> - `Configuring` — 세션을 만들기 직전(세션은 아직 없다). 쓰는 곳은 둘이다. 출시 뒤 저장 변환 등록(`options.Migrations`, [10장](10-SaveAndRelease.md) 7절)과 조건에 따른 덮어쓰기(데모 빌드는 Undo 끄기, 게임 전체 시드 고정 — [07장](07-GenreRecipes.md) 2·4절. 일일 도전처럼 판마다 시드를 정하면 `GBibleRuntime.NewRun(session, 시드)`). 저장 변환은 반드시 여기서 등록한다(프로필을 곧바로 불러오므로). 저장 버전은 등록한 변환 중 가장 높은 번호를 따라 저절로 정해진다.
> - 코드가 화면 값을 바꾸면 코드가 이기고, 모니터 타임라인에 "코드(Configuring)가 DB › Bootstrap의 세션 옵션을 바꿨습니다: …"가 남는다. 화면 칸과 `SessionOptions`의 대응은 [07장](07-GenreRecipes.md) 2절.
> - `RegisterRules`(`SessionCreated`) — **모든 세션**(화면 세션 · 멀티 서버 · 시험 모드의 서버와 봇 · 매칭 방 · 서버 빌드)이 DB로 만들어진 직후(DB 정의 등록·프로필 불러오기 뒤, 새 판 행동보다 먼저). 규칙 쪽 등록 전부: 프로젝트 기능의 감시 등록 함수(Rules의 `…Rules.Register`), 행동 처리기, 규칙이 받는 이벤트, 틱 규칙, 데이터를 바꾸는 코드 개발 명령. 받은 `session`만 쓴다(`GBibleRuntime.Session`은 아직 그 세션이 아니다). 개발 명령은 되도록 DB › 개발 › 개발 명령에 둔다(출시 빌드에서는 저절로 빠진다). DB에 있는 플레이어 데이터는 이미 정의되어 있으므로 `Define`하지 않는다.
> - `Started` — **화면 세션에만**, 준비가 끝난 뒤(`RegisterRules` → 기본 개발 명령·씬 흐름·화면 준비 다음, 시작할 때 이어 하기의 판 불러오기보다 먼저). 화면 쪽 연결: 화면 세션 전체의 연출 받기(`SubscribeAfterAction` → `GameView.Present`), `GameScreens.Reroll`·`GameSettings.Changed`·`GameInput.Use`, 화면 세션에서 한 번 할 일. 세이브 슬롯을 바꾸면(`UseSlot`) 세션이 새로 만들어져 둘 다 다시 온다 — 세션이 아닌 정적 알림(`GameSettings.Changed`)에 걸 때는 두 번 걸리지 않게 `-=` 뒤 `+=`.
> - 예전에 만든 `Bootstrap.cs`에 `RegisterRules`가 없으면 매뉴얼 [20장](../../Manual/20-Setup.md) 4절대로 더한다.
> - 도메인 리로드를 꺼도 매 플레이마다 다시 연결되도록 시스템이 이벤트를 비우므로, 이 연결은 매번 이렇게 한다.

---

## 2. 체크리스트 모음

체크리스트는 장마다 마지막 절에 있다. 여기에 다시 적지 않는다(한 곳에서만 고친다).

| 장 | 체크리스트 |
|---|---|
| [01. 프로젝트 시작하기](01-StartProject.md) | 6절 |
| [02. 기획을 코드 구조로](02-DesignToCode.md) | 11절 |
| [03. 게임 루프](03-GameLoop.md) | 10절 |
| [04. 콘텐츠 더하기](04-AddingContent.md) | 12절 |
| [05. 개발자와 함께 일하기](05-WorkingWithDesigners.md) | 10절 |
| [06. 화면과 연출](06-Presentation.md) | 9절 |
| [07. 장르별 설정](07-GenreRecipes.md) | 7절 |
| [08. 시험과 디버깅](08-TestAndDebug.md) | 8절 |
| [09. 3요소를 지키는 습관](09-Habits.md) | 7절 |
| [10. 저장과 출시](10-SaveAndRelease.md) | 8절(저장 · 출시 전) |
| [11. 멀티플레이 대비](11-MultiplayerReady.md) | 5절 |

## 3. 본문에서 쓰지 않은 기능

본문(01~11장)의 카드 던전 흐름에 자리가 없었던 기능을 짧게 적는다. 자세한 것은 매뉴얼 장을 본다.

### 3-1. 처리기를 따로 등록하는 행동 — `Actions.Register`

행동 클래스에는 데이터만 두고 처리 코드는 시작 코드에 둔다. 처리 코드를 행동 클래스 밖에 모으고 싶을 때(서버 쪽에서만 처리를 바꾸고 싶을 때 등) 쓴다. 01장 4절의 `RegisterRules` 표에 이름만 나온다.

```csharp
[Serializable]
public sealed class SkipFloor : GameAction
{
    public int floors = 1;
    public override ActionVisibility Visibility => ActionVisibility.Developer;
    public override string Describe() => $"[개발] {floors}층 건너뛰기";
}
```

`Run`을 쓰지 않고, 처리기를 `Bootstrap.RegisterRules`에서 한 번 등록한다(모든 세션 — `Started`에 두면 멀티 서버 세션에 처리기가 없다): `session.Actions.Register<SkipFloor>(action => session.Game.Data().Run.floor += action.floors);`. 한 형식에 처리기는 하나다. 이때도 대상 필드는 처리기가 불리기 직전에 id로 다시 찾아 넣는다. `Run`도 처리기도 없으면 "행동 'SkipFloor'의 처리기가 없습니다"로 실패한다. 대부분은 행동 클래스에 `Run`을 쓰는 것(1-1)으로 충분하다. → [02. 행동과 Undo](../../Manual/02-Actions-Undo.md) 2.5

### 3-2. 코딩한 결과 Block — `Reward`

대상 없이 게임에 무언가를 하는 결과다. 결과 칸의 **+ 결과 추가 › 코딩한 결과**에 목록으로 나온다(AI는 반영 스크립트에서 항목의 결과 목록에 `new AddTotalGold { amount = 50 }`처럼 넣는다).

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

→ [12. 결과](../../Manual/12-Outcomes.md) 3-2, [13. `[BindTo]`](../../Manual/13-BindTo.md) 2-7·2-8

### 3-3. 달성·진행도·로그를 코드로 읽기

도전과제 목록 화면이나 진행 게이지는 세션에서 읽는다.

```csharp
if (session.Achievements.IsUnlocked(achievementId)) return "달성";
return session.Achievements.TryGetProgress(achievementId, out var now, out var goal) ? $"{now} / {goal}" : "진행 중";
```

> [!note]- 자세히
> - 진행도는 조건(로그 비교 등)으로 달성하는 도전과제에만 있다.
> - 로그 값은 `session.Stats.Get(로그 ID)`, 이벤트 없이 코드에서 바로 기록할 때는 `session.Stats.Report(로그 ID, 값)`(집계 방식을 따른다).
> - 프로젝트 기능의 Trigger도 `trigger.TryGetProgress(session, out now, out goal)`로 같은 진행도를 준다.
> - 도전과제가 달성되는 순간 기본 이벤트 `AchievementUnlocked`(필드 `Id`·`DisplayName`)를 쏘므로, 달성 알림 화면은 그것을 받는다.

→ [14. 도전과제](../../Manual/14-Achievements.md), [15. 로그](../../Manual/15-Stats-Logs.md)

### 3-4. 기록 용량과 원인 묶기 — `Journal`

`session.Journal.Append(JournalKind, 시스템 이름, 글)`은 기록(모니터 타임라인)에 한 줄을 남기고 그 줄(`JournalEntry`)을 돌려준다. 여러 줄을 한 원인 아래로 묶으려면 `Cause`를 쓴다.

```csharp
var root = session.Journal.Append(JournalKind.System, "Shop", "상점 열림");
using (session.Journal.Cause(root.Id)) session.Journal.Append(JournalKind.System, "Shop", "재고 새로 고침");
```

> [!note]- 자세히
> - `Cause` 범위 안에서 남긴 줄은 타임라인 묶어 보기에서 원인 줄 아래로 들어간다.
> - 기록은 고리 버퍼라 보관 줄 수(기본 4096줄)를 넘으면 오래된 줄부터 밀려난다. 긴 세션에서 앞부분이 사라지면 DB › 시작 파일 › Bootstrap › 고급의 **기록 보관 줄 수**를 늘린다(코드 값은 `SessionOptions.JournalCapacity`).

→ [22. 모니터와 디버깅](../../Manual/22-Monitor-Debug.md) 2장

### 3-5. 키워드를 실행 중에 읽기 · 하위 그룹

화면에 키워드 그룹의 표시 이름이나 목록이 필요하면 `Keywords`로 읽는다: `Keywords.GroupOf(typeof(Rarity)).DisplayName`("등급"), `Keywords.Find("legendary")`. 목록은 처음 쓸 때 한 번 모은다. 그룹 static class 안에 static class를 하나 더 두면 하위 그룹이 된다(키워드 탭에서 폴더로 보인다). 키워드 ID는 모든 그룹에서 하나뿐이어야 한다. → [05. 키워드](../../Manual/05-Keywords.md) 2.2·2.5

### 3-6. 이벤트 도구용 멤버

- 이벤트 클래스가 `IDescribable`을 구현하면(`public string Describe() => $"{cardId}: {from} → {to}";`) 기록의 문장이 그것이 된다. 필드가 많거나 리스트가 있어 기본 문장이 읽기 어려울 때 쓴다.
- `session.Events.SubscribeAll(evt => …)` — 형식과 상관없이 모든 이벤트를 받는다. 디버그 출력·이벤트 중계·리플레이 녹화용. 기록 문장은 `EventBus.Describe(evt)`, 표시 이름은 `EventBus.DisplayNameOf(형식)`.
- `session.Events.PublishObject(evt)` — 형식을 실행 중에만 알 때 쏜다. 콘솔 명령 같은 도구에서만 쓴다.
- `Game.Use`가 끝나면 기본 이벤트 `AbilityUsed`("액티브 효과 사용", 필드 `Ability`·`User`·`Targets`·`Times`)를 쏜다. 카드·물약 사용 연출을 한곳에서 받을 때 쓸 수 있다.

→ [10. 이벤트](../../Manual/10-Events.md) 2-5~2-10

### 3-7. 찾기·저장소 보조 멤버

- `Game.All<T>(키)` — 플레이어 데이터 하나 안의 `T`만 모은다(두 플레이어의 보드를 따로 볼 때).
- `Game.Find(id)` — 형식을 모를 때 id로 찾는다. 형식을 알면 `Find<T>`. 형식을 실행 중에 정할 때는 `Game.Find(형식, id)`.
- `GameApi.HasId(형식)` — 그 형식에 `string id`가 있는지(행동의 대상 필드가 되는지).
- `session.Data.Get<T>(키)`·`Set(키, 값)`·`Modify<T>(키, 람다)` — 생성된 `Game.Data()` 대신 키 글자로 읽고 쓴다. `Modify`는 람다가 끝날 때 그 키를 Sync하고, 람다 안에서 예외가 나면 그 키를 되돌린다.
- `session.UndoCount` — 되돌릴 수 있는 횟수(Undo 버튼을 흐리게 할 때).
- `session.Data.TransactionUndone` — Undo가 끝났을 때 오는 저장소 이벤트. 기본 판 저장("행동마다 저장")이 Undo 뒤 저장에 쓴다. 게임 코드가 직접 쓸 일은 드물다.
- `[PlayerData]` — "플레이어 데이터로 쓰는 클래스"라는 표시. 생성 코드에는 `[Serializable]`과 같은 뜻이고, 늘 `[Serializable]`과 함께 쓴다.
- **Modify 누락 검사**(DB › 시작 파일 › Bootstrap › 고급, 코드 값 `SessionOptions.DetectUntrackedChanges`) — 예전 방식 데이터를 `Modify` 밖에서 고친 것을 찾는 검사. 개발 빌드에서만 켜진다.

→ [01. 플레이어 데이터](../../Manual/01-PlayerData.md) 2장, [03. Game API](../../Manual/03-GameApi.md) 2장

### 3-8. 저장 보조 멤버

- `GameSession.IsSaveCompatible(json, session.SaveSchema, out reason)` — 저장 JSON이 지금 데이터 구조와 맞는지. 개발 중 저장 초기화의 판단 기준이다. 세이브 슬롯을 여럿 둘 때 "이 버전에서 못 읽는 저장"을 미리 표시하는 데 쓸 수 있다.
- `GBibleRuntime.Stopped` — 게임을 끝낼 때 프로필·판 저장이 끝난 뒤 불린다(외부 서비스에 끝을 알릴 때).

→ [06. 저장·불러오기](../../Manual/06-SaveLoad.md) 2.9, [20. 시작하기](../../Manual/20-Setup.md) 4절

### 3-9. 인스턴스를 에셋으로 다루기 · 코드로 인스턴스 주기

- `GameDatabase.Load().All<InstanceData>()` — 인스턴스 에셋을 직접 훑는다. `asset.CreateInstance<T>()`는 원본의 깊은 사본, `InstanceData.Copy(객체)`는 아무 객체의 깊은 사본이다. 보통은 ID만 알면 되므로 `session.CopyInstance(id)`(사본)나 `session.Instances(id)`(원본, 읽기만)를 쓴다.
- 결과 '플레이어 데이터에 주기'(`PlayerDataReward`)는 코드로도 만들어 `Grant(session)`할 수 있다.

> [!note]- `PlayerDataReward`를 코드로 쓸 때
> 클래스 필드나 클래스 리스트 필드에 인스턴스 ID를 주면 그 인스턴스의 **사본**이 들어간다(`GrantKind.Instance`·`List`). 카드 던전의 덱은 원본 사본이 아니라 원본 ID만 든 `DeckCard`라서 이 방법을 쓰지 않고 규칙 함수(`Rules.NewCard`)로 넣는다. 원본 사본을 판 데이터에 그대로 두는 게임(아이템마다 강화 수치가 다른 RPG 장비 등)에서 쓴다.

→ [04. 인스턴스](../../Manual/04-Instances.md) 2.9·2.10

### 3-10. 에디터 설정과 도구

- **내 코드 범위**(창 없음 — `ProjectSettings/MomoiProject/ProjectCode.json`을 고친다) — 클래스를 고르는 목록(값 형식·인스턴스 형식·필드 규칙)에 보일 폴더·네임스페이스. 플러그인 폴더의 클래스가 목록에 섞이거나 내 클래스가 안 보일 때 고친다.
- **DB › 프리팹** — 프리팹을 DB에 등록해 컴포넌트 필드를 탐색기로 고친다. 수정은 바로 적용된다(작업본을 거치지 않는다).
- **`GameFeatures`**(에디터 코드, `GBible.EditorTools`) — 선택 기능이 켜졌는지 에디터 도구에서 확인한다: `GameFeatures.IsActive(GameFeatures.Find("dialogue"))`.
- **`TextCatalog`** — 테스트·도구에서 텍스트 표를 따로 만들어 문장 틀을 시험한다: `new TextCatalog { SourceLanguage = "ko", Language = "ko" }` → `SetTable` → `Get`.
- **결과 '다이얼로그 재생'** — 코드 없이 도전과제·유물 같은 항목의 결과로 다이얼로그를 연다. 카드 던전은 상점 층에서 코드(`Dialogues.Play`)로 열었다([06장](06-Presentation.md) 6절).

→ [21. DB](../../Manual/21-Editor-Database.md) 6.9·8, [20. 시작하기](../../Manual/20-Setup.md) 6절, [23. 텍스트와 현지화](../../Manual/23-Text-Localization.md) 7절, [24. 다이얼로그](../../Manual/24-Dialogue.md) 5.5

### 3-11. 난수 뽑기의 나머지 · 로그 시험 API

- `RandomStream`은 `Range(min, max)` 말고도 `Chance(p)`(p 확률로 참), `NextDouble()`([0, 1) 실수), `NextUInt()`·`NextULong()`을 준다. 치명타라면 `Game.Random("damage").Chance(0.15)`. 흐름 이름은 [07장](07-GenreRecipes.md) 4절처럼 용도마다 나눈다.
- `session.TrySimulateStat(로그 ID, 양, out 설명, out 오류)` — 모니터 로그 탭의 "이벤트로 더하기"와 같다. 반영 규칙을 거꾸로 따라가 실제 이벤트를 쏜다(행동 하나, Undo 가능). 테스트·개발 명령에서 "처치 100회"를 만들 때 쓴다.
- `EventSimulator.Create(형식, 이름→값)` — 이벤트 객체를 필드 이름으로 만든다(도구용).

→ [07. 난수와 시간](../../Manual/07-Random-Time.md) 2.2, [22. 모니터와 디버깅](../../Manual/22-Monitor-Debug.md) 4절

### 3-12. 기믹 부품 — 사용·확률·상태 효과·자리·다시 뽑기

카드 던전에는 없지만 조커 칸·보드·인벤토리 격자 같은 게임에 쓰는 부품이다. 무엇에 무엇을 쓰나는 AI 문서 [기믹 목록](../GimmickCatalog.md)에 갈래별로 있다. 패시브 효과 쪽(놓인 것마다·가진 동안 스탯 보정·필드 바꾸기·빌려 쓰기)은 [04장](04-AddingContent.md) 8절 접힌 칸.

- **되는 조각만 쓰기**: `Game.TryUse(id, 사용자, 대상…)`. 쓸 수 있으면 쓰고 참, 사용 조건이 안 맞으면 아무것도 하지 않고 거짓이다(실패를 던지지 않는다). 보드의 유닛을 차례로 돌며 부를 때 쓴다.
- **기본 행동 직전 이벤트 다섯**: GBible이 행동 직전 이벤트 자리를 미리 둔 동작이다. 트리거 `행동 직전 이벤트 받기` + 결과 `행동 직전 이벤트 고치기`로 고친다.

| 행동 직전 이벤트 | 거치는 곳 | 고치는 예 |
|---|---|---|
| '사용'(`UseIntent`) | `Game.Use`·`TryUse`가 결과를 내기 직전 | `times` 더하기 1 = 한 번 더 발동, 막기 = 아무 일 없음 |
| '효과 배율'(`PowerIntent`) | 액티브 효과를 쓸 때·패시브 효과가 발동할 때 | `multiplier` 곱하기 2 = 이 조각의 수치 2배(Lumin). 기본 효과 Block이 곱하고, 프로젝트 Block은 `Use.Context.Power` |
| '상태 효과 걸기'(`StatusIntent`) | `Game.ApplyStatus`·효과 '상태 효과 걸기' | `statusId` 값으로 바꾸기(화상 → 푸른 화상), `stacks` 곱하기 2, `length` 곱하기, `maxStacks` 더하기, `pulse` 곱하기, 막기(면역 → `ApplyStatus`가 null) |
| '확률'(`ChanceIntent`) | `Game.Chance`·조건 '확률' | `probability` 곱하기 2 = 모든 확률 2배 |
| '비용 지불'(`CostIntent`) | `Game.Pay`·`TryPay`·결과 '비용 내기' | `floor` 값으로 바꾸기 −20 = −20까지 빚, `key`·`path` = 다른 곳에서 내기, 거르기 `forId`(행성 카드만 공짜) |

```csharp
if (Game.Chance(0.25, "loot", source: GameIds.Passive.EightBall)) …   // 흐름을 비우면 "chance"
var debuffs = Game.StatusCount(enemy, StatusKind.Debuff);              // 디버프 가짓수
Game.RemoveStatuses(hero, StatusKind.Debuff);                          // 정화(푼 가짓수를 돌려준다)
```

> [!note]- 자세히
> - **확률**: 확률이 바뀌어도 난수는 한 번만 굴린다. 같은 시드면 뒤가 밀리지 않는다. 조건 '확률'(확률(%) · 난수 흐름)은 이벤트를 받는 순간·행동 직전 이벤트를 받는 순간·쓰는 순간에만 굴린다. `Game.CanUse`(미리 보기)에서는 굴리지 않고 참, 이벤트 없는 조건 충족에서는 거짓이다.
> - **상태 효과 종류**: DB › 상태 효과의 `kind`(없음·버프·디버프). 효과 '상태 효과 풀기'는 상태 효과를 비우고 종류를 고르면 그 종류를 모두 푼다. 조건 '상태 효과 검사'는 상태 효과를 비우고 종류를 고르면 그 가짓수를 비교한다.
> - **값 틀**(행동 직전 이벤트 고치기·필드 바꾸기·이벤트 쏘기·사용자 필드 검사의 값): `{level}`·`{level:2/4/7}`·`{user:경로}`·`{event:경로}`·`{data:키.경로}`·`{count:키.경로}`·`{user}`·`{owner}`·`{stacks}`·`{balance:ID}` — 뜻은 매뉴얼 [12장](../../Manual/12-Outcomes.md) 2-4a.
> - **자리**: 목록 하나(`List<T>`)를 칸으로 본다. 한 줄(조커 칸·핸드)은 줄 길이 0, 격자(인벤토리·보드)는 줄 길이 = 열 수다. 칸 번호 = 목록 위치, 빈 칸 = null. 좌표는 화면 기준(x 오른쪽 +, y 아래 +)이다. DB 대상 '자리로 고르기'(옆 칸·같은 줄·주변 8칸·맨 처음, '다른 목록에서 고르기'면 맞은편 보드)와 조건 '자리 검사'(맨 아래 줄·가장자리·양옆이 빈 칸 …)로 쓰고, 코드는 `Placement.Around(run.bag, artifact, 6, PlaceDirection.Left)`·`Placement.Is(run.jokers, joker, 0, PlaceSpot.First)`·석판 무늬 `Placement.Pattern(칸, 개수, 열, 무늬, 회전)`. 회전은 `Placement.Rotate`(화면 기준), `Grid` 좌표(y 위 +)에서는 `Grid.Rotate`. 둘 다 시계 방향 90°씩이다.
> - **후보 +1·다시 뽑기**: 결과 '가중치 표에서 고르기'의 '후보 수에 더할 숫자'(플레이어 데이터의 숫자)가 후보 수에 더해진다. 행동 안에서 `Game.RerollChoice(선택 id)`를 부르면 같은 표·같은 후보 수로 새로 뽑고 `ChoiceRequested`를 다시 쏜다. 비용·남은 횟수는 부르는 행동이 맡는다(`Game.Pay`). Undo하면 원래 후보로 돌아간다.
> - **시드로 새 판**: `GBibleRuntime.NewRun(session, RandomStreams.SeedOf(글자))`([07장](07-GenreRecipes.md) 4절).

→ [16. 액티브 효과와 사용 기능](../../Manual/16-Abilities-Usable.md), [28. 스탯·Modifier·행동 직전 이벤트·비용](../../Manual/28-Attributes-Intercepts.md), [29. 상태 효과·예약·선택 대기](../../Manual/29-Statuses-Schedules-Choices.md), [30. 밸런싱 값·곡선·가중치 표·플레이어용 실패](../../Manual/30-Balance-Tables.md), [36. 격자·큰 수](../../Manual/36-Grid-BigNumber.md)

---

## 4. 기능 사용 표

기능을 찾는 표는 한 곳씩만 둔다.

- **기능 → 쓸 것 → 매뉴얼 장**: 참조 [`Reference.md`](../Reference.md) 2절(하고 싶은 일 → 쓸 것)·3절(API 요약). 코드를 쓰기 시작할 때 끝까지 읽는 문서다.
- **상황 → 매뉴얼 장**: 매뉴얼 [00-Index](../../Manual/00-Index.md)의 '상황별로 찾기'.
- **이 가이드의 장 → 다루는 기능**: [00. 목차](00-Index.md)의 '읽는 순서' 표. 본문에 자리가 없었던 기능은 이 장 3절.

---

## 더 보기

- [매뉴얼 목차](../../Manual/00-Index.md) — 기능별 사전
- [../Coding.md](../../Coding.md) — 게임 코드 규칙
- [../TestMultiplayer.md](../../TestMultiplayer.md) — 테스트용 멀티플레이 설계 기록(지금 동작은 매뉴얼 [40장](../../Manual/40-Multiplayer.md))
