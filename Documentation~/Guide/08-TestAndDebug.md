# 08. 시험과 디버깅

버그를 보면 같은 판을 다시 만들고, 모니터에서 원인을 거슬러 올라가고, Undo로 앞뒤를 오가며 고친다. 바뀌지 않아야 할 규칙은 테스트로 묶어 둔다.

> [!note]- 이 장에서 쓰는 말
> | 말 | 뜻 |
> |---|---|
> | 모니터 | 메뉴 `MomoiProject → 모니터`. 플레이 중인 세션을 보고 조작하는 창. 탭은 타임라인 · 데이터 · 도전과제 · 로그 · 이벤트 · 행동 · 명령. |
> | 통합 기록(Journal) | 세션에서 일어난 모든 일(행동·이벤트·값 변경·달성·경고)을 원인과 함께 한 줄씩 남긴 기록. 타임라인 탭이 보여 준다. |
> | 행동 | 플레이어 입력 하나를 담은 클래스. 하나가 Undo 한 단위다. |
> | Undo | 마지막 행동이 바꾼 값을 행동 전으로 되돌리는 것. 난수도 함께 돌아간다. |
> | 시드 | 결정론 난수의 씨앗. 같은 시드에서 같은 행동을 하면 같은 결과가 나온다. |
> | 이벤트 호출 | 모니터 이벤트 탭에서 이벤트 값을 채워 직접 쏘는 도구. |
> | 시뮬레이션 | 값을 덮어쓰지 않고 실제 이벤트를 쏴서 게임과 같은 경로(이벤트 → 로그 → 도전과제 → 보상)로 움직이는 것. 쏜 이벤트 묶음은 행동 하나로 기록된다. |

---

## 1. 같은 판 다시 만들기

- **고정 시드**: DB › 시작 파일 › Bootstrap의 **개발 빌드에서 시드 고정**을 켜 둔다([01장](01-StartProject.md) 4절). 같은 순서로 하면 같은 판이 나온다. 모니터 머리 줄에 "시드 20261003 · …"이 보이면 된다.
- **판 저장**: 버그 직전에 모니터 › **명령** › 저장 › **판 저장**을 누른다. 다음 플레이에서 **판 불러오기**를 누르면 그 순간으로 돌아온다.

버그 보고에는 시드와 판 저장 파일을 붙인다.

> [!note]- 시드 자세히
> - 고정되면 덱 섞기·전리품·무작위 대상이 같게 나온다. 에디터 플레이는 개발 빌드로 친다. 출시 빌드는 늘 지금 시각을 시드로 쓴다.
> - 카드 던전은 난수를 쓰임마다 흐름으로 나눈다(`deck`·`loot`·`cardTarget`, [09장](09-Habits.md) 2절). 코드를 고쳐 한 흐름의 뽑는 횟수가 달라져도 다른 흐름은 그대로라 재현이 덜 흔들린다.
> - Bootstrap의 `Configure`에서 `options.Seed`를 바꾸면 코드가 이긴다. 그때 모니터 **타임라인**에 "코드(Configuring)가 DB › Bootstrap의 세션 옵션을 바꿨습니다: 시드 … → …"가 남는다.

> [!note]- 판 저장 자세히
> - 파일은 `GBibleRuntime.RunSavePath`(`…/MomoiProject/run.json`)다. 판 데이터(세션 구역)·난수 상태·게임 시계가 들어가므로, 불러오면 다음 난수까지 같다. 이 파일을 보고에 붙이면 다른 사람도 같은 상태에서 시작한다.
> - 게임 코드에서 이어 하려면 `GBibleRuntime.LoadRun(session)`을 부른다(타이틀의 이어 하기 등, [10장](10-SaveAndRelease.md)).
> - Bootstrap의 판 저장이 "행동마다 저장"이면 `run.json`은 늘 마지막 행동 뒤의 상태로 덮인다. 버그 직전 상태를 남기려면 그때 파일을 복사해 둔다.
> - 같은 묶음의 **새 판**은 판 데이터를 처음 값으로 되돌리고 판 저장을 지운다. **프로필 삭제**는 다음 플레이부터 적용된다.

> [!note]- 데이터 구조를 바꾸면 저장이 지워진다
> 개발 중에 플레이어 데이터의 필드를 지우거나 이름·형식을 바꾸면, 다음 플레이를 시작할 때 맞지 않는 저장이 지워지고 "개발 중 데이터 구조가 바뀌어 저장을 초기화했습니다" 경고가 남는다. 필드를 더하기만 했으면 그대로 이어 간다. 출시 뒤에는 지우지 않고 변환한다([10장](10-SaveAndRelease.md)).

## 2. 디버그 명령

시험 준비(골드 넣기, 적 모두 처치 …)는 버튼 하나로 만든다. Bootstrap의 `Started`에서 등록하면 모니터 **명령** 탭에 보인다.

```csharp
if (Debug.isDebugBuild)
    session.Debug.Register("카드 던전", "골드 +100", () => session.Do(new DeveloperEditAction
    {
        label = "골드 +100",
        Edit = () => session.Game.Data().Run.gold += 100,
    }));
```

값을 바꾸는 명령은 `DeveloperEditAction`으로 감싼다.

> [!note]- 왜 `DeveloperEditAction`인가
> 넘긴 함수를 행동 하나 안에서 실행한다. 그래서 바꾼 값이 기록(행동 탭의 "[개발] 골드 +100", 공개 범위 "개발자")에 남고 Undo된다. 명령 안에서 값을 그냥 고치면 행동 밖 변경이 되어 Undo되지 않고, 그 뒤 Undo하면 덮어쓰기 경고가 난다.

> [!note]- 등록 규칙
> - 명령은 세션마다 등록되므로 `GBibleRuntime.Started`에서 등록한다. `Debug.isDebugBuild`로 출시 빌드에서는 뺀다.
> - 명령이 늘어나면 따로 파일을 두고 그 파일에서 `GBibleRuntime.Started += …`로 연결해도 된다(카드 던전의 `DebugTools`). 연결은 `[RuntimeInitializeOnLoadMethod(AfterAssembliesLoaded)]` 함수에서 한다.
> - 형식은 `session.Debug.Register(묶음, 이름, 실행, 설명)`. 설명은 버튼에 마우스를 올리면 보인다.
> - 명령 탭에는 기본 묶음이 이미 있다. **저장**(프로필 저장·프로필 삭제·판 저장·판 불러오기·새 판), **시간**(일시정지 / 재개·1프레임 진행·배속).

> [!note]- 기록에 한 줄 남기기
> `session.Journal.Append`는 게임 코드 어디서든 기록에 한 줄을 남긴다. 카드 던전은 버그 보고에 붙일 시드와 층을 남기는 명령을 둔다.
> ```csharp
> session.Debug.Register("카드 던전", "시드 기록", () =>
>     session.Journal.Append(JournalKind.System, "CardDungeon", $"시드 {session.Random.MasterSeed} · 층 {session.Game.Data().Run.floor}"),
>     "지금 시드와 층을 타임라인에 남긴다(버그 보고에 붙인다).");
> ```
> `JournalKind.Warning`·`Error`로 남기면 콘솔에도 `[MomoiProject] …`로 나간다.

## 3. 모니터로 원인 찾기

"턴을 넘겼더니 체력이 생각보다 많이 줄었다"를 예로 든다.

1. **타임라인** 탭의 **모든 값 ▾**에서 `run`을 고르고 **묶어 보기**를 켠다. "턴 끝" 행동 줄을 펼치면 그 행동이 쏜 이벤트와 바뀐 값(`hero.hp` …)이 차례로 보인다.
2. `hero.hp`가 바뀐 줄을 고르면 오른쪽 **인과 관계**에 뿌리 행동부터 그 줄까지가 보인다. 피해 이벤트를 **열기**하면 `sourceId`·`amount`로 어느 적이 얼마를 줬는지 나온다.
3. **데이터** 탭 › 판 데이터에서 그 적의 `intentIndex`를 보고 몬스터 원본의 `attackPattern`과 비교한다.
4. **행동** 탭에서 실패한 행동이 있었는지 본다.

**Alt**를 누르고 있으면 문장이 코드식으로 바뀌어 필드 경로가 정확히 보인다.

> [!note]- 무엇을 어디서 보나
> | 알고 싶은 것 | 볼 곳 |
> |---|---|
> | 이 값이 왜 바뀌었나 | 타임라인 › **모든 값 ▾**에서 값 고르기 → **묶어 보기** → 줄 고르기 → 오른쪽 **인과 관계** |
> | 지금 값이 무엇인가, 바꿔 보고 싶다 | 데이터 탭(**판 데이터**·**프로필 데이터**). 고친 값은 개발자 행동 하나로 기록되어 Undo된다 |
> | 내 행동이 실행됐나, 왜 실패했나 | 행동 탭(상태 **실행** · **Undo됨** · **실패**, 실패 줄에 마우스를 올리면 오류) |
> | 이 이벤트를 받으면 무엇이 반응하나 | 이벤트 탭(**이벤트 호출**, 아래 "반응하는 로그") |
> | 도전과제가 제대로 달성되나 | 도전과제 탭 **트리거로 달성**(실제 이벤트로), 안 되면 **강제 달성** |
> | 로그가 제대로 쌓이나 | 로그 탭 **이벤트로 더하기**·**이벤트로 기록**(실제 이벤트로) / **직접 수정**(값만) |
> | 몇 판을 해야 나오는 상황을 지금 보고 싶다 | 명령 탭의 프로젝트 디버그 명령 |

> [!note]- 실패한 행동
> 행동 안에서 예외가 나면 그 행동이 바꾼 것이 모두 취소된다. 화면에 보인 연출과 값이 다르다면 여기부터 의심한다. 에너지 부족처럼 예상된 실패는 정상이다.

## 4. Undo로 앞뒤 보기

모니터 머리의 **Undo**는 마지막 행동을 되돌린다. 난수도 행동 전으로 돌아가므로 같은 카드를 다시 내면 같은 결과가 나온다.

"카드를 낸다 → 결과를 본다 → Undo → 값을 바꾸거나 다른 대상으로 다시 낸다"를 반복해 같은 난수 조건에서 결과를 비교한다.

> [!note]- Undo 자세히
> - 게임 코드의 `session.Undo()`와 같다.
> - 다시 하기(Redo)는 없다. 앞으로 가려면 같은 행동을 다시 한다.
> - 도전과제 달성과 그 보상은 Undo해도 남는다. 다시 시험하려면 데이터 탭 › **도전과제 달성**에서 거짓으로 바꾸거나 도전과제 탭의 **초기화**를 쓴다.

## 5. 이벤트로 반응 보기

반응을 시험할 때는 값을 덮어쓰지 말고 이벤트를 쏜다. 같은 이벤트를 받는 다른 기능(유물·다른 로그)도 게임처럼 함께 반응한다.

예: 도전과제 `bigTurn`("로그 `maxTurnDamage`가 30 이상")을 한 턴 30 피해 덱 없이 확인한다.

- **이벤트** 탭에서 "턴 끝"을 고르고 `damage`에 30을 넣고 **이벤트 호출**. `maxTurnDamage`가 30이 되고 `bigTurn`이 달성된다.
- 또는 **도전과제** 탭에서 `bigTurn`을 고르고 **트리거로 달성**. 모자란 로그를 채울 이벤트를 계획해 쏜다.

> [!note]- 자주 하는 시험은 명령으로
> 코드로는 `session.SimulateEvents`(이벤트 목록을 행동 하나로 쏜다)와 `session.TrySimulateAchievement`(트리거로 달성)를 쓴다.
> ```csharp
> session.Debug.Register("카드 던전", "한 턴 30 피해", () =>
>     session.SimulateEvents(new object[] { new TurnEnded { turn = 1, damage = 30 } }, "한 턴 30 피해"),
>     "로그 maxTurnDamage와 도전과제 bigTurn이 반응하는지 본다.");
>
> session.Debug.Register("카드 던전", "bigTurn 트리거로 달성", () =>
> {
>     if (!session.TrySimulateAchievement(GameIds.Achievement.BigTurn, out var error)) Debug.LogWarning(error);
> });
> ```

> [!note]- 시뮬레이션 자세히
> - 쏜 이벤트는 행동 하나("[테스트] …")로 기록된다. Undo하면 로그는 돌아가고 달성은 남는다.
> - 이벤트 호출 아래 "반응하는 로그"에 이 이벤트를 받는 로그가 미리 보인다.
> - "턴 끝을 받는 유물이 엉뚱하게 발동한다" 같은 문제도 여기서 드러난다.
> - 모니터에서 이벤트를 쏘려면 이벤트 클래스에 매개변수 없는 생성자가 있어야 한다. 필드만 있는 클래스면 된다.

## 6. 규칙 테스트

카드 내기·실패 취소·Undo처럼 지켜야 할 규칙은 EditMode 테스트로 묶는다.

- `Assets/Code/Tests`에 테스트 파일을 둔다. asmdef는 시작 설정이 이미 만들었다.
- Unity의 Test Runner 창(`Window › General › Test Runner` › EditMode)에서 돈다.

테스트는 DB 없이 세션을 손으로 만든다. 플레이어 데이터는 `Define`으로, 카드·몬스터 원본은 `session.Instances`에 함수로 넣는다.

```csharp
[SetUp]
public void SetUp()
{
    _session = new GameSession(new SessionOptions { Seed = 7 });
    _session.Data.Define(GameIds.PlayerData.Run.Id, DataZone.Session, new RunState(), "판");
    _session.Data.Define(GameIds.PlayerData.Profile.Id, DataZone.Meta, new Profile(), "프로필");

    // DB › 인스턴스 대신 원본을 코드로 넣는다(시험에 필요한 수치만).
    var originals = new Dictionary<string, object>
    {
        [GameIds.Instance.Strike] = new Card { title = "타격", cost = 1 },
        [GameIds.Instance.Goblin] = new MonsterData { title = "고블린", maxHp = 20, attackPattern = new List<int> { 6 } },
        // … 수비·화염구·슬라임·회복 물약
    };
    _session.Instances = id => originals.TryGetValue(id, out var value) ? value : null;
}
```

테스트는 게임과 똑같이 행동을 실행하고 값을 확인한다.

```csharp
[Test]
public void 에너지가_모자라면_아무것도_바뀌지_않는다()
{
    Game.Do(new StartRun());
    var battle = Game.Data().Run.battle;
    battle.energy = 0;                                   // 시험 준비(행동 밖 변경)
    var hand = new List<string>(battle.hand);
    Assert.Throws<InvalidOperationException>(() => Game.Do(new PlayCard { target = hand[0] }));
    CollectionAssert.AreEqual(hand, Game.Data().Run.battle.hand);
}
```

> [!note]- 왜 DB 없이 만드나
> - 게임은 덱의 한 장(`DeckCard`)이 든 원본 ID로 `Rules.CardData`·`MonsterOf`·`PotionOf`를 거쳐 원본을 읽는다. 테스트에 필요한 수치만 넣어 두면 기획자가 DB 수치를 바꿔도 테스트가 흔들리지 않는다.
> - `Define`은 DB가 없을 때만 쓴다. 게임에서는 DB › 플레이어 데이터가 정의한다([01장](01-StartProject.md) 3절).
> - 위 코드의 `Game`은 `GameApi Game => _session.Game;` 속성이다.

> [!note]- 카드의 사용 기능 넣기
> 카드를 내면 `Game.Use(card.card, …)`가 같은 ID의 능력을 찾는다. 테스트에서는 DB의 사용 기능 대신 능력(`AbilityDefinition`)을 코드로 정의한다.
> ```csharp
> void DefineCard(string id, TargetSelector target, Effect effect)
> {
>     var ability = new AbilityDefinition { Id = id };
>     if (target != null) ability.Targets.Add(target);
>     ability.Effects.Add(effect);
>     _session.Abilities.Define(ability);
> }
>
> // SetUp 안에서
> DefineCard(GameIds.Instance.Strike, null, new DealDamage { amount = 6 });
> DefineCard(GameIds.Instance.Defend, new SelfTarget(), new GainBlock { amount = 5 });
> ```

> [!note]- 카드 던전의 다른 테스트
> - **같은 시드면 첫 손패가 같다**: 같은 시드로 세션을 둘 만들어 `StartRun` 뒤 손패의 원본 ID를 비교한다.
> - **카드를 내고 Undo하면 전부 돌아온다**: 카드를 내고 `_session.Undo()` 뒤 에너지·적 체력·손패를 확인한다.
> - 전체는 예시 게임의 `CardDungeonRulesTests.cs`.

> [!note]- 테스트 자세히
> - `GBibleRuntime` 없이 만든 세션은 프레임이 흐르지 않는다. 프레임 끝 처리가 필요하면 `session.EndFrame(0.016)`을 직접 부른다.
> - Tests asmdef(`MomoiProject.Tests`)는 Rules·`GBible.Core`·`GBible.Unity`·켠 데이터 모듈(`GBible.Inventory` 등)·Test Runner·NUnit을 참조하고, 에디터에서 테스트를 돌릴 때만 컴파일된다. 화면 코드(Client)는 부를 수 없다. 규칙이 Rules에 있어야 하는 이유다.
> - 프로젝트 루트의 `Tools/RunCoreTests.ps1`은 라이브러리의 코어 테스트만 Unity 없이 돌린다. 라이브러리를 고쳤을 때 코어가 깨지지 않았는지 보는 용도이고, 게임 규칙 테스트는 넣지 않는다.

## 7. 경고 메시지별 대응

타임라인·콘솔의 `[MomoiProject]` 경고는 쌓아 두지 않는다. 보이면 그때 아래 표로 고친다.

> [!note]- 대응표
> | 경고·오류(요약) | 뜻 | 대응 |
> |---|---|---|
> | Undo '…'가 행동 밖에서 바뀐 값을 덮었습니다 | 그 행동이 바꾼 곳을 행동 밖(씬 코드·이벤트를 받는 코드)에서 또 바꿨고, Undo가 행동 전 값으로 덮었다 | 되돌릴 수 있어야 하는 변경은 행동 안에서 한다 |
> | '…'이(가) Modify를 거치지 않고 바뀌었습니다 | 예전 방식(리플렉션 비교) 데이터를 행동·`Modify` 밖에서 고쳤다 | 행동 안에서 고친다. 클래스에 `[Serializable]`을 붙여 빠른 비교로 옮긴다 |
> | '…'은(는) 예전 방식(리플렉션)으로 비교합니다: 이유 | 비교 코드로 다룰 수 없는 형식이 들어 있다(`[Serializable]` 없음, `HashSet`, 중첩 컬렉션, 뿌리가 컬렉션 …) | 이유에 적힌 형식을 `List`·`Dictionary`·`[Serializable]` 클래스로 바꾼다 |
> | '…' 값이 큽니다(요소 n개) | 예전 방식 값이 커서 수정마다 전체를 복제·비교한다 | 빠른 비교로 옮기거나, 자주 바뀌는 부분을 다른 키로 나눈다 |
> | 정의되지 않은 플레이어 데이터 '…' | DB에 없거나 `Define` 전에 읽었다 | DB › 플레이어 데이터에 만들고 저장한다. 테스트는 `Define`을 먼저 한다 |
> | … '…'을(를) 찾을 수 없습니다 | `GameAction<T>`의 `target`에 없는 id를 넣었다(Undo로 사라졌을 수도) | 입력이 쥔 id가 최신인지 확인하고, 지연 실행이면 `Find`로 먼저 확인한다 |
> | …에 string id 필드가 없어 Find로 찾을 수 없습니다 | `Find`·`GameAction<T>`의 형식에 `public string id;`가 없다 | id 필드를 더한다 |
> | 행동 실패, 변경 취소: … | 행동 안에서 예외가 났고 바꾼 것이 모두 취소됐다 | 예상된 실패(에너지 부족 등)면 정상이다. 아니면 오류 메시지를 따라간다 |
> | 트리거 사슬이 16단계를 넘어 끊었습니다 | 결과가 자기 트리거의 이벤트를 다시 쏜다 | 결과에서는 다른 이벤트를 쏜다 |
> | 조건 연쇄 평가가 16회를 넘었습니다 | 조건 충족 → 결과 → 다른 조건 충족이 끝없이 돈다 | 서로를 조건으로 보는 항목을 끊는다 |
> | [BindTo("…")]: 이 ID의 DB 항목이 없어 연결되지 않았습니다 | `[BindTo]`의 ID가 DB에 없다(오타·지운 항목·저장 전) | DB 항목을 만들고 저장하거나, `GameIds` 상수를 쓴다 |
> | 구독자 오류 (클래스.함수): … | 이벤트를 받는 함수에서 예외가 났다(다른 받는 쪽은 계속 불린다) | 그 함수를 고친다. 대개 사라진 화면 오브젝트를 `Dispose`하지 않았다 |
> | …: 저장할 수 없는 참조입니다 | 판·프로필 데이터 안에 DB 항목이 아닌 에셋 참조(스프라이트 등)가 있다 | 플레이어 데이터에는 ID를 두고 그림은 원본 인스턴스에서 찾는다([09장](09-Habits.md) 4절) |
> | 개발 중 데이터 구조가 바뀌어 저장을 초기화했습니다 | 개발 중 상태에서 필드를 지우거나 이름·형식을 바꿨다 | 개발 중에는 정상이다. 출시 뒤에는 출시됨 상태로 바꾸고 저장 변환을 쓴다([10장](10-SaveAndRelease.md)) |
> | 판 끝 이벤트 '…'를 찾을 수 없습니다 | DB › Bootstrap의 판 끝 이벤트 클래스 이름이 바뀌었거나 지워졌다 | Bootstrap 화면에서 판 끝 이벤트를 다시 고른다 |
> | 필수 값이 비어 있습니다: … | 필수로 정한 칸이 게임 중 빈 값이 됐다(모니터 데이터 탭 맨 위에도 보인다) | 그 값을 만드는 코드를 고친다 |
> | …에 읽기 코드가 없어 리플렉션으로 읽습니다 | `[Serializable]`·`[GameEvent]`가 없는 형식을 조건·문장 틀이 읽는다 | 어트리뷰트를 붙인다 |
> | 이벤트 '…'에 매개변수 없는 생성자가 없어 테스트로 만들 수 없습니다 | 이벤트 호출·트리거로 달성이 이벤트를 만들지 못한다 | 매개변수 없는 생성자를 둔다 |
> | [다국어] '…'에 텍스트 '…'가 없습니다 | 그 언어(와 대체 언어)에 키가 없다. 화면에는 `[키]`가 보인다 | 원문 표에 키를 더하고, `MomoiProject → 다국어 → 번역 검사`로 빠진 것을 찾는다 |
> | [다이얼로그] 다이얼로그 재생기가 없습니다 … | 씬에 재생기도, 설정의 프리팹도 없다 | `MomoiProject → 다이얼로그 시스템 → UI 템플릿 만들기` |
> | GameDatabase를 찾을 수 없습니다 | DB 에셋(GameDatabase)이 없다 | 시작 설정을 적용하거나 DB 창을 한 번 연다 |

## 8. 체크리스트

- [ ] DB › 시작 파일 › Bootstrap에서 개발 빌드 시드를 고정했다.
- [ ] 시험 준비용 디버그 명령이 있고, 값을 바꾸는 명령은 `DeveloperEditAction`으로 Undo된다.
- [ ] 버그를 보면 판을 저장하고, 저장 파일과 시드를 보고에 붙인다.
- [ ] 타임라인에서 값 하나의 변경 원인을 따라갈 수 있다.
- [ ] 도전과제·로그·유물의 반응을 이벤트 호출·트리거로 달성으로 확인했다.
- [ ] 지켜야 할 규칙(실패 취소, Undo, 같은 시드 같은 결과)이 `Assets/Code/Tests`에 테스트로 있다.
- [ ] 타임라인·콘솔에 `[MomoiProject]` 경고가 남아 있지 않다.

> [!note]- 다른 장르라면
> - **실시간 액션**: 행동 단위가 아니라 스냅샷으로 되감는다(Bootstrap의 Undo 방식 "스냅샷"). 모니터 머리에 "되감기 스냅샷 n (n초 간격)"이 보이고, **Undo**는 마지막 스냅샷으로 되감는다. 버그 직전으로 돌아가 보려면 `session.CaptureSnapshot()`을 부르는 디버그 명령을 만들어 두고 그 순간 근처에서 누른다. 빠른 장면은 모니터의 **일시정지**·**1프레임**·배속(0.25× …)으로 늦춰 본다.
> - **턴제 전술**: AI처럼 판단이 복잡한 코드는 AI가 고른 행동의 `Describe()`를 읽기 쉽게 쓴다("오크1 → 영웅 공격"). 행동 탭·타임라인만으로 AI의 선택을 따라갈 수 있다.
> - **수집형·방치형**: 프로필이 오래 쌓이므로 명령 › 저장 › **프로필 삭제**(다음 플레이부터)와 **새 판**으로 처음 상태를 자주 시험한다. 오래된 프로필 파일을 몇 개 보관해 두었다가 출시 전 저장 변환을 시험한다.

> [!note]- 더 보기(매뉴얼)
> - [22. 모니터와 디버깅](../Manual/22-Monitor-Debug.md) — 탭별 조작, 디버그 명령, 시뮬레이션 API
> - [10. 이벤트](../Manual/10-Events.md) — 이벤트 호출
> - [02. 행동과 Undo](../Manual/02-Actions-Undo.md) — 실패와 취소, Undo 경고
> - [07. 난수와 시간](../Manual/07-Random-Time.md) — 시드, 같은 시드 같은 결과
> - [06. 저장·불러오기](../Manual/06-SaveLoad.md) — 판 저장·불러오기, 개발 중 저장 초기화
> - [01. 플레이어 데이터](../Manual/01-PlayerData.md) — 예전 방식 비교, 주의할 점
