# 06. 화면과 연출

화면은 플레이어 데이터를 **읽기만** 하고, 값을 바꾸는 입력은 모두 행동으로 보낸다. 데이터는 행동이 끝나는 순간 최종 모습이 되고, 연출(피해 숫자·소리·대사)은 그 뒤를 시간을 두고 따라간다. 이 장의 코드는 모두 **Client 폴더**에 둔다(카드 던전은 `Client/UI`·`Client/Presentation`).

> [!note]- 이 장에서 쓰는 말
> | 말 | 뜻 |
> |---|---|
> | 행동 | 플레이어 입력 하나를 담은 클래스. `Game.Do`로 실행하고, 하나가 Undo 한 단위다. |
> | Undo | 마지막 행동이 바꾼 값을 행동 전으로 되돌리는 것. |
> | Sync | 시스템이 들고 있는 플레이어 데이터의 사본(Mirror)과 지금 값을 비교해 바뀐 곳만 찾아 기록하는 동작. 행동 시작·끝, 행동 안의 `Game.Find`·`Game.All`, 매 프레임 끝에 일어난다. |
> | ChangeSet | Sync 한 번이 찾아낸 변경 묶음. 멀티플레이에서 다른 쪽에 보내는 단위다. |
> | 이벤트를 쏜다 / 받는다 | 게임 코드가 "방금 일어난 일"을 `Game.Events.Publish`로 넘기는 것이 쏜다, `Subscribe`로 등록한 함수가 그것을 넘겨받는 것이 받는다. |
> | 문장 틀 | 텍스트 키에 들어 있는 글. `{값}`·조사·복수형을 문법으로 적는다. |
> | 연출 대기열 | `GameView.Present`로 넣은 연출(코루틴 하나 = 연출 하나)을 하나씩 차례로 트는 줄. 남아 있는 동안 `GameView.IsPresenting`이 참이다. |
> | 미리보기 | 행동을 실제로 해 보고 결과만 읽은 뒤 모두 되돌리는 것(`Game.Preview`·`Game.CanDo`). 기록·Undo·연출이 남지 않는다. |
> | `GameFailure` | 플레이어에게 보일 실패(텍스트 키 + 기본 문장). `Message`를 읽을 때마다 지금 언어로 만든다. |

---

## 1. 화면 갱신

> [!note]- 새 프로젝트의 화면은 DB › 화면으로(UXML + 화면 코드)
> 카드 던전의 전투 화면은 씬의 UIDocument에 MonoBehaviour를 붙여 만들었다(아래). 새 프로젝트는 매뉴얼 [35장](../../Manual/35-Screens.md)의 **화면**으로 만든다. 인스펙터에서 연결할 것이 없어 AI가 파일만으로 끝낸다.
> 1. DB 반영 스크립트에서 `ScreenSetup.Ensure()`(에디터를 열 때도 저절로 — 메뉴는 없다).
> 2. `Assets/MomoiProject/UI/Screens/BattleHud.uxml`: 값 칸은 `binding-path`만 적는다 — `<ui:Label binding-path="run:gold@hud.gold" />`(텍스트 `hud.gold` = "골드 {value}"). 코드 없이 바뀐다.
> 3. DB › 화면 `battleHud`: UXML · 층 `바탕(HUD)` · 열 씬 `Battle`.
> 4. 버튼·손패만 코드로: `[BindTo(GameIds.Screen.BattleHud)] class BattleHud : GameScreen` 안에서 `OnClick("end-turn", () => new EndTurn())`(연출·선택 중이면 무시, "에너지가 모자랍니다"는 알림으로 저절로).
> 5. 고르기 창·설정·슬롯·일시정지·"정말 할까요?"·알림은 **기본 화면**이 있어 만들지 않는다. 색은 `Assets/MomoiProject/UI/Theme.uss`의 변수로.
>
> 아래 1~4절의 규칙(읽기만·입력은 행동·연출 뒤에 그리기)은 두 방식 모두 같다.


- 화면에 보일 값(체력·골드·손패·물약 칸)이 모두 플레이어 데이터나 원본 인스턴스에 있는지 본다. 없으면 [02장](02-DesignToCode.md)으로 돌아가 필드를 더한다.
- 그리는 함수 하나(`Redraw`)를 만들고 `GameView.Redraw(Redraw, 키)`에 등록한다. 그 플레이어 데이터가 바뀌거나 **언어가 바뀐** 프레임 끝에 한 번 불러 준다. 연출이 재생 중이면 끝난 뒤에 부른다(4절).
- 등록은 `OnEnable`, 해제(`Dispose`)는 `OnDisable`에서 한다.

카드 던전 전투 화면(`BattleHud`, UI Toolkit)은 이렇게 한다.

```csharp
IDisposable _view;

void OnEnable()
{
    // … 요소 찾기·버튼 연결
    _view = GameView.Redraw(Redraw, GameIds.PlayerData.Run.Id);   // run이나 언어가 바뀌면 프레임 끝에 한 번(연출이 끝난 뒤)
}

void OnDisable() => _view?.Dispose();

void Redraw()   // Game.Data().Run을 읽어 글자·손패·물약 칸을 다시 만든다
{
    var run = GBibleRuntime.Game.Data().Run;
    _hp.text = Texts.Get("hud.hp", ("hp", run.hero.hp), ("max", run.hero.maxHp));
    // … 골드·에너지·손패·물약 칸
}
```

> [!note]- 왜 알림마다 그리지 않나
> 값이 바뀌었다는 알림은 행동 하나에도 여러 번 온다. 행동 안에서 `Game.Find`·`Game.All`이 중간에 Sync하면 그때 한 번, 행동이 끝날 때 또 한 번 온다. 행동이 실패하면 진행분을 Sync한 알림과 되돌린 알림이 함께 오고, Undo해도 온다. 알림마다 그리면 같은 화면을 한 프레임에 여러 번 계산하지만, 플레이어 눈에 보이는 것은 프레임당 한 번이다. 그래서 `GameView`는 알림이 오면 "다시 그릴 것"으로 **표시만** 하고, 프레임 끝(행동 밖)에 한 번 그린다.
>
> 언어가 바뀌어도 같은 프레임 끝에 다시 그린다. 화면 글은 모두 `Texts.Get`으로 만드므로(5절), 다시 그리면 새 언어로 바뀐다.
>
> 처음 한 번은 등록한 프레임 끝에 그린다. 화면이 켜질 때 따로 그릴 필요가 없다.

> [!note]- 키 고르기 · 해제
> - 키를 주면 그 플레이어 데이터가 바뀔 때만, 비우면(`GameView.Redraw(Redraw)`) 어느 플레이어 데이터가 바뀌어도 그린다. 키는 여러 개 줄 수 있다.
> - 카드 던전은 판의 모든 값이 플레이어 데이터 `run` 하나에 있으므로, `run`이 바뀌면 화면 전체를 다시 그린다.
> - 연출을 기다리지 않을 화면(골드 표시처럼 바로 바뀌어도 되는 것)은 `GameView.Redraw(Redraw, waitForPresentation: false, 키)`.
> - 해제를 빼먹으면 안 된다. 등록은 씬보다 오래 살아서, 사라진 화면 오브젝트의 `Redraw`를 계속 부른다.
> - 세션이 바뀌면(슬롯 바꾸기·다시 시작) 저절로 다시 연결하고 모두 다시 그린다.

> [!note]- 화면 연결 — 목록·값 하나를 덜 쓰고(`ViewList`·데이터 연결·`GameView.Find`)
> 프리팹(GameObject)으로 만든 화면이면 손으로 쓰던 "목록 다시 만들기"를 라이브러리에 맡긴다. 카드 던전 전투 화면은 UI Toolkit이라 `Redraw`에서 직접 그리지만, uGUI·월드 오브젝트로 적 그림을 둔다면 이렇게 한다.
> ```csharp
> // 적마다 프리팹 하나: 생기면 만들고, 사라지면 없애고, 순서를 맞추고, 값이 바뀌면 bind로 다시 그린다.
> _enemies = new ViewList<Enemy, EnemyView>(enemyPrefab, enemyRoot,
>     () => GBibleRuntime.Game.Data().Run.battle?.enemies, (view, enemy) => view.Show(enemy), GameIds.PlayerData.Run.Id);
> _enemies.OnRemove = (view, done) => view.PlayDeath(done);   // 사라질 때 연출(연출 대기열에 들어가고, done 뒤에 없앤다)
> // OnDestroy에서 _enemies.Dispose();
> ```
> - `ViewList`가 만든 오브젝트는 항목 id로 등록된다. 연출이 `GameView.Find<EnemyView>(e.targetId)`로 맞은 적의 오브젝트를 찾는다. 직접 등록은 `GameView.Register(id, gameObject)`.
> - 값 하나를 글자·슬라이더에 잇기만 하면 코드도 없다: 컴포넌트 **데이터 연결**(`MomoiProject/데이터 연결`)을 붙이고 키 `run`, 필드 경로 `battle.energy`, 글자 틀 `에너지 {value}`(`{value}`가 값. 텍스트 키를 적으면 번역된 틀)를 적은 뒤 `onText`를 TMP 글자에, `onNumber`를 슬라이더에 잇는다. 이것도 연출이 끝난 뒤에 바뀐다.
> - UI Toolkit의 `Label`은 컴포넌트가 아니라 데이터 연결의 대상이 될 수 없다. 카드 던전처럼 `Redraw`에서 넣는다.
> - 매뉴얼 [32장](../../Manual/32-View-Presentation-Scenes.md).

> [!note]- 어떤 알림을 받나
> | 이럴 때 | 이것 |
> |---|---|
> | 플레이어 데이터나 언어가 바뀌면 화면을 다시 그린다 | `GameView.Redraw(그리기, 키…)`(프레임 끝에 한 번) |
> | 무엇이 바뀌었는지(손패에 한 장 추가, 적 하나 제거)에 따라 다르게 움직인다 | `session.Data.DataChanged`(키 + 바뀐 경로 목록 `DataChange`: 종류 Set·Insert·Remove·Reorder, 경로, 옛 값·새 값). 알림마다 오므로 바뀐 곳을 모아 두고 프레임 끝에 반영한다 |
> | 바뀐 키만 직접 받는다 | `session.Data.Changed`(`Action<string>`, 바뀐 키). `GameView`가 안에서 쓰는 알림이다 |
> | 다른 쪽(서버·클라이언트)에 변경을 보낸다 | `session.Data.Synced`(ChangeSet). 화면용이 아니다([11장](11-MultiplayerReady.md)) |
> | "피해를 줬다", "적이 쓰러졌다"처럼 일어난 일에 맞춰 연출한다 | 이벤트를 받는다(`session.Events.SubscribeAfterAction<T>`, 3절) |
> | 행동이 끝났을 때 한 번 무언가 한다 | `session.Actions.Executed`(`ActionRecord`, 안쪽 행동이면 `Nested`가 참) |
> | 실패해 취소된 행동을 안다 | `session.Actions.TransactionAborted` |

> [!note]- 값을 어디서 읽나
> | 이럴 때 | 이것 |
> |---|---|
> | 판·전투 값(체력, 골드, 손패) | `Game.Data().Run` |
> | 카드 이름·비용·그림, 몬스터 초상 같은 고정 값 | 원본 인스턴스. `Rules.CardData(game, deckCard.card)`처럼 원본 ID로 복사 없이 읽는다(읽기만 한다) |
> | 플레이어에게 보일 이름 | 텍스트 `<ID>.name`(`Texts.NameOf(id)`, 또는 문장 틀에 ID를 값으로 넘김, 5절) |
> | 매 프레임 움직이는 위치·애니메이션 | MonoBehaviour 필드(플레이어 데이터에 넣지 않는다) |
>
> 덱의 한 장(`DeckCard`)에는 장마다 다른 `id`, 원본 ID `card`, 강화 여부 `upgraded`만 있다. 이름·비용·그림·사용 기능은 원본 `Card`에 있고, 화면은 `session.Instances`가 주는 원본을 복사 없이 읽는다. 판 데이터에 스프라이트 참조가 없으니 판 저장 경고("저장할 수 없는 참조입니다")도 없고, 이어 하기 뒤에도 그림이 그대로 나온다. 기획자가 원본의 비용을 고치면 진행 중인 판의 모든 장에 바로 반영된다.

> [!note]- 다른 장르라면
> - **실시간 액션**: 위치·속도·애니메이션은 MonoBehaviour가 들고 매 프레임 그린다. 플레이어 데이터 알림은 체력·탄약·쿨다운 같은 규칙 값의 HUD에만 쓰고, 피격 이펙트는 이벤트를 받아 튼다. 쿨다운 표시는 `준비 시각 - Game.Session.Clock.Time`으로 계산한다([07장](07-GenreRecipes.md)).
> - **턴제 전술**: 유닛이 많아 화면 전체를 다시 그리기 무겁다. `DataChanged`의 경로(`units[#12].hp`, `units[#3]` 제거)를 보고 바뀐 유닛만 갱신한다. 유닛 이동 연출은 이동 행동이 쏜 이벤트를 받아 칸에서 칸으로 움직인다.

## 2. 입력은 행동으로

- 버튼은 행동에 넣을 값을 쥔다. 대상(덱 카드·적)은 **객체 그대로** 쥐어도 된다. 원본(물약 등)은 원본 ID다.
- 누르면 `Game.Do(new 행동 { … })`를 보낸다.
- 연출이 남아 있거나 선택을 기다리는 동안(`GameView.Busy`)은 입력을 받지 않는다.
- 실패(에너지 부족 등)는 예외로 온다. 잡아서 메시지만 보인다. 메시지는 이미 지금 언어다(5절).

```csharp
void Drink(string potion)
{
    if (GameView.Busy) return;
    try
    {
        GBibleRuntime.Game.Do(new DrinkPotion { potion = potion });
        _message.text = "";
    }
    catch (InvalidOperationException e)
    {
        _message.text = e.Message;   // 바뀐 값은 이미 모두 취소되어 있다
    }
}
```

> [!note]- 왜 객체를 쥐어도 되나 · 손패 버튼
> Undo·불러오기 뒤에는 같은 카드가 다른 객체일 수 있다. 그래도 행동은 실행 직전에 대상 필드의 id로 지금 데이터의 객체를 다시 찾아 넣으므로, 버튼이 옛 객체를 쥐고 있어도 맞는 카드가 나간다. 그래서 손패 버튼은 덱의 한 장(`DeckCard`) 객체를 그대로 쥔다(`Play(picked)`).
>
> 카드 던전은 입력을 행동으로 바꾸는 창구를 런 스크립트(`RunDirector.TryPlay`)에 모았다. 화면은 결과 메시지만 보인다.
> ```csharp
> void Play(DeckCard card)
> {
>     if (GameView.Busy) return;   // 연출이 남아 있거나 선택을 기다리는 중
>     var target = Rules.LivingEnemies(GBibleRuntime.Game).FirstOrDefault();   // 대상 고르기 화면은 생략
>     _message.text = director.TryPlay(card, target, out var error) ? "" : error;
> }
> ```

> [!note]- 왜 연출 중에는 입력을 막나 · `Busy`
> 다시 그리기는 연출이 끝난 뒤에 된다(4절). 피해 숫자가 뜨는 동안 손패 버튼은 아직 낸 카드를 보여 준다. 그 버튼을 또 누르면 "손에 없는 카드입니다"로 실패한다. 그래서 연출이 남아 있는 동안은 받지 않는다.
> - `GameView.Busy` = 연출 대기열이 남아 있거나(`IsPresenting`) 선택을 기다리는 중(`Game.IsWaiting`)이다. 턴 끝 버튼도 같은 검사를 거친다(`clicked += () => { if (!GameView.Busy) director.EndTurn(); }`).
> - 막는 것은 화면의 일이다. 시스템은 막지 않는다. 기다리는 선택(`Game.Choice`)을 그리는 창은 그 선택에 답하는 입력만 받는다.
> - 연출을 빨리 넘기는 버튼은 `GameView.SkipPresentation()`(4절).

> [!note]- 미리보기 — 예상 피해 · 낼 수 없는 이유(`Game.Preview`·`Game.CanDo`)
> 화면이 규칙을 따로 계산하지 않는다. 행동을 실제로 해 보고 결과만 읽은 뒤 모두 되돌린다. 기록(타임라인)·Undo·행동 로그·`SubscribeAfterAction` 연출·도전과제 달성이 남지 않고 난수도 돌아가므로, 실제로 내면 미리본 값과 같다. `BattleHud.Hint`는 손패를 그릴 때(`DrawHand`) 카드마다 한 번 불러 버튼의 tooltip(마우스를 올리면 보이는 글)에 넣는다: `new Button(…) { text = …, tooltip = Hint(picked) }`.
> ```csharp
> public string Hint(DeckCard card)
> {
>     var game = GBibleRuntime.Game;
>     var target = Rules.LivingEnemies(game).FirstOrDefault();
>     var play = new PlayCard { card = card, target = target };
>     if (!game.CanDo(play, out var reason)) return reason;            // 해 보고 되돌린다. 실패 글은 지금 언어
>     if (target == null) return "";
>     var before = target.hp;
>     var after = game.Preview(play, g => g.Find<Enemy>(target.id)?.hp ?? 0);   // 쓰러지면 전투에서 빠져 찾을 수 없다
>     return before > after ? Texts.Get("hint.damage", ("amount", before - after)) : "";
> }
> ```
> - 피해 계산·방어도·행동 직전 이벤트(가시 갑옷)·유물까지 실제 규칙을 그대로 탄다. 카드를 고칠 때 미리보기 코드를 같이 고칠 일이 없다.
> - 행동 밖(화면 코드)에서만 부른다. 행동 안에서 부르면 예외다.
> - `Redraw` 안에서 불러도 된다. 미리보기는 데이터 변경 알림을 보내지 않으므로 다시 그리기를 부르지 않는다. 다만 행동 하나를 통째로 실행하므로 매 프레임 부르지 말고 그릴 때 한 번 부른다.
> - 규칙 코드가 게임 밖으로 나가는 일(분석 전송 등)을 하면 `Game.IsPreviewing`일 때 건너뛴다. 연출은 원래대로 `SubscribeAfterAction`으로 받으면 미리보기에서 불리지 않는다.

## 3. 이벤트로 연출

- 규칙이 쏜 이벤트를 받아 연출을 튼다. 카드 던전은 `Rules.Damage`가 쏘는 `DamageDealt`·`EnemyKilled`를 받는다.
- 연출은 `session.Events.SubscribeAfterAction<T>`로 받는다. 행동이 **성공한 뒤에만**, 쏜 순서대로 온다. 실패해 취소된 행동의 이벤트는 오지 않는다.
- 받은 이벤트는 연출 대기열(`GameView.Present`)에 넣는다(4절). 받는 쪽에서는 플레이어 데이터를 바꾸지 않는다.

```csharp
void OnEnable()
{
    var session = GBibleRuntime.Session;
    // 행동이 성공한 뒤에만, 쏜 순서대로 온다(실패해 취소된 행동의 이벤트는 오지 않는다).
    _subscriptions.Add(session.Events.SubscribeAfterAction<DamageDealt>(e => GameView.Present(ShowDamage(e))));
    _subscriptions.Add(session.Events.SubscribeAfterAction<EnemyKilled>(e => GameView.Present(ShowKill(e))));
}

void OnDisable()
{
    foreach (var subscription in _subscriptions) subscription.Dispose();
    _subscriptions.Clear();
}
```

> [!note]- 왜 행동이 끝난 뒤에 받나
> `Subscribe`로 받으면 이벤트를 쏘는 그 자리, 즉 행동 한가운데에서 불린다. 화염구가 피해를 준 뒤 같은 행동 안에서 예외가 나면, 값은 모두 취소되었는데 피해 숫자는 이미 떠 버린다. 실패한 행동의 연출은 보여 주면 안 된다.
>
> `SubscribeAfterAction`은 행동 안에서 쏜 이벤트를 모아 두었다가, 바깥 행동이 성공하면 쏜 순서대로 넘기고 실패하면 버린다. 그래서 받는 순간은 늘 행동 밖이고, 데이터는 이미 최종 모습이다.
> - `PlayCard` 안에서 부르는 `Game.Use`·`Game.Do(new WinBattle())`가 쏜 이벤트도 바깥 행동(`PlayCard`)이 끝날 때 함께 온다. 연출이 반쯤 끊겨 나오지 않는다.
> - 행동 밖에서 쏜 이벤트(도전과제 달성처럼 행동이 끝난 뒤 쏘는 것)는 바로 온다.
> - `Dispose`하면 아직 넘기지 않은 것도 버린다. 이미 연출 대기열에 넣은 것은 대기열이 튼다(씬을 떠날 때 버리려면 `GameView.ClearPresentation()`).
> - 미리보기(`Game.Preview`, 2절)에서 쏜 이벤트는 오지 않는다. 연출이 미리보기 때문에 뜨는 일이 없다.

> [!note]- `Subscribe`와 `SubscribeAfterAction`
> | 받는 쪽 | 이것 |
> |---|---|
> | 화면 연출(피해 숫자·소리·흔들림) | `SubscribeAfterAction` |
> | 규칙 코드(행동 안에서 바로 반응해 값을 바꾸는 쪽) | `Subscribe` |

> [!note]- 연출에서 하지 않는 것
> | 하지 않는 것 | 이유 |
> |---|---|
> | 받는 쪽에서 플레이어 데이터를 고친다 | 행동 밖 변경이 되어 Undo·저장·멀티플레이에서 빠진다 |
> | 행동 안에서 연출을 기다린다(코루틴·`await`) | 행동은 한 번에 끝나야 한다 |
> | 연출의 무작위에 `Game.Random`을 쓴다 | Undo가 모든 난수 흐름을 되돌려 규칙 결과가 어긋난다. 연출은 `UnityEngine.Random` |
> | 판 데이터에 스프라이트 같은 에셋 참조를 둔다 | DB 항목이 아닌 에셋 참조는 판 저장에 담기지 않는다("저장할 수 없는 참조입니다" 경고). 판 데이터에는 원본 ID만 두고 그림은 원본에서 읽는다 |

## 4. 데이터와 연출의 시간

- 연출은 데이터가 아니라 **연출 대기열**을 따라간다. `GameView.Present(코루틴)`으로 넣으면 앞의 연출이 끝난 뒤에 하나씩 튼다.
- 연출 하나 = 코루틴 하나. 기다리기는 `WaitForSeconds` 대신 `GameView.Wait(초)`.
- `GameView.Redraw`로 그리는 화면은 대기열이 빌 때까지 기다렸다가 다시 그린다. 피해 숫자가 다 뜬 뒤에 체력·손패가 바뀐다.

```csharp
// 연출 하나 = 코루틴 하나. 대기열은 앞의 연출이 끝나야 다음 것을 튼다.
IEnumerator ShowDamage(DamageDealt hit)
{
    Popup(Texts.Get("popup.damage", hit), hit.targetId);          // [GameEvent] 객체를 값 묶음으로(생성 코드로 읽는다)
    GameAudio.PlaySfx(hitSound, pitch: UnityEngine.Random.Range(0.95f, 1.05f));   // 연출의 무작위는 UnityEngine.Random
    yield return GameView.Wait(gap);                               // WaitForSeconds 대신: 연출 속도·건너뛰기를 따른다
}
```

> [!note]- 데이터는 이미 끝나 있다
> 화염구로 고블린을 잡으면 `PlayCard`가 끝나는 순간 데이터는 최종 모습이다. 고블린은 `battle.enemies`에서 빠졌고, 남은 적의 체력은 깎였고, 전투에서 이겼다면 `battle`이 `null`이 되고 층이 올랐다. 화면이 그 프레임에 데이터대로 그리면 고블린이 피해 숫자도 없이 바로 사라진다.
> - 그래서 `GameView.Redraw`는 기본으로 연출 대기열이 빌 때까지 기다린다. 피해 숫자 → 쓰러짐 연출이 끝난 다음 프레임 끝에 고블린이 화면에서 빠진다.
> - 기다리지 않을 화면은 `GameView.Redraw(그리기, waitForPresentation: false, 키)`. 사라지는 순간 자체를 연출하려면 `ViewList`의 `OnRemove`(1절).
> - 체력 막대는 다시 그릴 때 데이터 값으로 곧장 바꿔도 되고, 보이는 값을 데이터 값 쪽으로 몇 프레임에 걸쳐 따라가게 해도 된다. 어느 쪽이든 데이터는 건드리지 않는다.

> [!note]- 연출 대기열 자세히
> | 이럴 때 | 이것 |
> |---|---|
> | 코루틴으로 쓴 연출 | `GameView.Present(ShowDamage(e))` |
> | 끝나면 콜백을 부르는 연출(트윈 라이브러리) | `GameView.Present(done => card.MoveTo(slot, done))` |
> | 잠깐 쉬기 | `GameView.Present(0.3f)` |
> | 연출이 남아 있나 / 입력을 막을까 | `GameView.IsPresenting` / `GameView.Busy`(연출 중이거나 선택 대기 중, 2절) |
> | 연출 속도(2 = 두 배) | `GameView.PresentationSpeed`. 연출 코드는 시간을 `GameView.DeltaTime`·`GameView.Wait`로 재야 이 값을 따른다 |
> | 클릭으로 넘기기 | `GameView.SkipPresentation()` — 남은 연출을 10배로 끝내고, 대기열이 비면 원래 속도로 |
> | 씬을 떠날 때 남은 연출 버리기 | `GameView.ClearPresentation()` |
> | 대기열이 빈 순간 | 이벤트 `GameView.Presented` |
>
> 연출 하나가 예외를 내도 그 연출만 끝나고 대기열은 계속된다(안쪽 코루틴까지). 받기는 `SubscribeAfterAction` → `Present`가 기본 모양이다. 매뉴얼 [32장](../../Manual/32-View-Presentation-Scenes.md).

> [!note]- 게임 시간과 멈추지 않는 시간
> `GameView.Wait`·`DeltaTime`은 `Time.deltaTime`(게임 시간)에 연출 속도를 곱한 값이라, 모니터·메뉴·다이얼로그가 `GameTime.Pause`로 게임을 멈추면 연출도 멈춘다. 일시정지 메뉴처럼 멈추면 안 되는 연출은 대기열 밖에서 `Time.unscaledDeltaTime`을 쓴다.

## 5. 텍스트와 현지화

- 화면의 모든 글은 텍스트 키로 찾는다: `Texts.Get(키, (이름, 값), …)`.
- DB 항목 이름은 값으로 **ID**를 넘기면 문장 틀이 그 언어의 `<ID>.name` 텍스트로 바꾼다.
- 언어가 바뀌면 `GameView.Redraw`에 등록한 화면은 그 프레임 끝에 저절로 다시 그려진다(1절). 따로 받을 것이 없다.

```csharp
// "card"에 원본 ID를 넘기면 문장 틀이 <원본 ID>.name 텍스트(현재 언어의 카드 이름)로 바꾼다.
_hand.Add(new Button(() => Play(picked)) { text = Texts.Get("card.button", ("card", card.card), ("cost", Rules.CardData(GBibleRuntime.Game, card.card)?.cost ?? 0)) });
```

> [!note]- 카드 던전 전투 화면의 키
> | 키 | ko | en |
> |---|---|---|
> | `hud.hp` | `체력 {hp}/{max}` | `HP {hp}/{max}` |
> | `hud.gold` | `{gold} 골드` | `{gold} Gold` |
> | `hud.energy` | `에너지 {energy}` | `Energy {energy}` |
> | `card.button` | `{card} ({cost})` | `{card} ({cost})` |
> | `hud.potion` | `{potion} ×{count}` | `{potion} ×{count}` |
> | `hud.emptySlot` | `빈 칸` | `Empty` |
> | `popup.damage` | `-{amount}` | `-{amount}` |
> | `popup.kill` | `{monster:을} 쓰러뜨렸다!` | `Defeated {monster}!` |
> | `hint.damage` | `예상 피해 {amount}` | `{amount} damage` |
> | `fail.notInBattle` · `fail.notInHand` · `fail.noPotion` | `전투 중이 아닙니다.` · `손에 없는 카드입니다.` · `물약이 없습니다.` | `Not in battle.` · `That card is not in your hand.` · `No potion left.` |
> | `momoi.fail.notEnough`(라이브러리) | `{what:이} 모자랍니다 ({have}/{need}).` | `Not enough {what} ({have}/{need}).` |
> | `energy.name` · `gold.name` | `에너지` · `골드` | `energy` · `gold` |
> | `strike.name` · `goblin.name` · `healPotion.name` · `burn.name` … | `타격` · `고블린` · `회복 물약` · `화상` … | `Strike` · `Goblin` · `{count, plural, one {Healing Potion} other {Healing Potions}}` · `Burn` … |

> [!note]- 조사·복수형
> `{monster:을}`은 한국어 조사다. "고블린"이면 "고블린을", 받침 없는 이름이면 "를"이 붙는다. `hud.potion`의 `{potion}`에는 `count`도 함께 넘어가므로, 영어 이름 텍스트가 `{count, plural, …}`로 단수·복수를 고른다. 게임 코드는 언어마다 분기하지 않는다.

> [!note]- 값 묶음으로 객체 넘기기
> - `[GameEvent]` 이벤트 객체를 값 묶음으로 통째로 넘길 수 있다. `Texts.Get("popup.damage", hit)`는 이벤트의 필드(`amount` …)를 컴파일 때 생긴 읽기 코드로 읽는다(실행 중 리플렉션이 없다).
> - 익명 객체(`new { gold = 3 }`)는 부를 때마다 리플렉션으로 읽으므로 자주 그리는 곳은 튜플을 쓴다.
> - 틀에서 점으로 객체 안의 필드를 읽는다(`{hero.hp}`). 다이얼로그 대사도 같은 규칙이다(6절).

> [!note]- `LocalizedText` 컴포넌트와 UI Toolkit
> 인스펙터만으로 쓰는 `LocalizedText` 컴포넌트는 같은 오브젝트의 `text` 속성이 있는 컴포넌트(TextMeshPro 등)에 글을 넣는다. 붙을 때 한 번 글 넣는 대리자를 만들어 두므로 새로 고칠 때마다 리플렉션을 쓰지 않는다. UI Toolkit의 `Label`은 컴포넌트가 아니라 대상이 될 수 없으므로, 카드 던전처럼 `Redraw`에서 `Texts.Get`으로 넣고 `GameView.Redraw`로 언어가 바뀔 때 다시 그린다. `GameView`를 쓰지 않는 코드라면 `GameLanguage.Changed`를 직접 받는다.

> [!note]- 행동의 실패 글 — `Game.Fail`과 텍스트 표
> 행동은 플레이어에게 보일 실패를 텍스트 키와 기본 문장으로 던진다: `throw Game.Fail("fail.notInHand", "손에 없는 카드입니다.")`([03장](03-GameLoop.md) 6절). 던져지는 `GameFailure`는 `Message`를 읽을 때마다 지금 언어로 만든다.
> - 텍스트 표에 키가 있으면 그 틀, 없으면 기본 문장, 둘 다 없으면 `[키]`. 그래서 번역 전에도 한국어 기본 문장이 보이고, 번역가는 원문 표에 키만 채운다.
> - 값은 같은 `{이름}` 틀로 넘긴다: `Game.Fail("fail.frozen", "얼어 있어 {turns}턴 동안 움직일 수 없습니다.", ("turns", 2))`.
> - 라이브러리의 실패도 같은 형식이다. `Game.Pay`가 모자라면 `momoi.fail.notEnough`(값 `{what}`·`{have}`·`{need}`), 선택 대기의 답이 틀리면 `momoi.fail.chooseExactly` 등(키 목록은 `FailureKeys`). 이 키를 텍스트 표에 넣으면 번역된다. `{what}`에는 `Game.Pay`의 `what`(텍스트 키면 번역된 글)이 들어간다. 카드 던전은 `what: "energy.name"`·`"gold.name"`을 넘긴다. `what`을 비우면 "판 › battle.energy"처럼 데이터 이름과 필드 경로가 들어가므로 플레이어에게 보일 비용에는 늘 넘긴다.
> - 화면은 지금처럼 `InvalidOperationException`을 잡아 `e.Message`를 보이면 된다(`GameFailure`는 그 한 종류다). 플레이어용 실패만 골라 보이려면 `catch (GameFailure f)`.
> - 없는 ID 같은 프로그래머 실수는 `GameFailure`가 아니라 그냥 `InvalidOperationException`이다. 플레이어에게 보일 글이 아니므로 번역하지 않는다.
> - 언어를 바꾼 뒤에 같은 실패를 다시 보여 주면 새 언어로 나온다(`Message`를 그때 다시 만든다).

## 6. 다이얼로그

상점 층에 들어가면 상점 주인이 말을 걸고, "구경한다"를 고르면 상점 화면이 열린다. 대사와 연출은 기획자가 그래프로 만들고, 코드는 **열기와 신호 받기**만 한다.

에디터에서:

1. DB 사이드바 "다이얼로그 시스템"의 **기능 활성화**로 켜고, DB 반영 스크립트에서 `DialogueTemplate.Create()`를 한 번 부른다.
2. DB › **스탠딩 프로필**에 상점 주인(`merchant`)을 만들고 스탠딩 그림을 넣는다. 이름은 텍스트 `merchant.name`.
3. `MomoiProject → 다이얼로그 시스템 → 그래프`에서 다이얼로그 `shopkeeper`를 만든다. 시작 → 대사("어서 와, 모험가.", 화자 `merchant`) → 선택지 "구경한다" → **신호 보내기**(`openShop`) → 끝, 선택지 "나간다" → 끝.
4. 같은 그래프에 시작 노드를 하나 더 두고 시작 ID를 `noGold`로 한다(대사 "돈이 모자라는군.").

코드(`ShopkeeperTalk`):

```csharp
void OnEnable() => Dialogues.Signaled += OnSignal;
void OnDisable() => Dialogues.Signaled -= OnSignal;

public void Enter()   // 상점 층에 들어올 때
{
    GameAudio.PlayBgm(shopMusic);   // 같은 곡이면 끊기지 않고 이어진다
    if (!Dialogues.IsPlaying) Dialogues.Play(GameIds.Dialogue.Shopkeeper);
}

void OnSignal(string signal)
{
    if (signal == "openShop") shopPanel.SetActive(true);
}

public void Buy(string source, int price)   // 상점 화면의 카드 버튼
{
    try { GBibleRuntime.Game.Do(new BuyCard { source = source, price = price }); }
    catch (InvalidOperationException) { Dialogues.Play(GameIds.Dialogue.Shopkeeper, "noGold"); }   // 같은 다이얼로그의 다른 시작 ID
}
```

> [!note]- 다이얼로그와 행동의 몫
> 그래프의 **값 바꾸기** 노드도 값을 바꿀 수 있다(행동 하나로 기록된다). 하지만 골드 검사·덱에 카드 넣기처럼 규칙이 있는 일은 행동(`BuyCard`)에 두고, 다이얼로그는 말과 연출만 맡긴다.
>
> 비싼 카드 선택지가 있다면 그 선택지의 **보일 조건**에 "플레이어 데이터 검사"(`run`의 `gold` 이상)를 걸고 흐리게 보여 주기를 켠다.

> [!note]- 알아 둘 점
> - 다이얼로그가 열려 있는 동안 재생기가 `GameTime.Pause("다이얼로그")`로 게임 시간을 멈춘다. 전투 연출의 `Update`도 함께 멈춘다.
> - 대사 원문의 `{…}`에는 모든 플레이어 데이터가 ID로 넘어가고, 점으로 값 안의 필드를 읽는다. 카드 던전은 판의 값을 `run` 하나에 모았으므로 "가진 돈은 {run.gold}골드군."처럼 쓴다(이름이 점을 포함한 플레이어 데이터가 따로 있으면 그것을 먼저 쓴다).
> - 판을 불러온 뒤 `Dialogues.ResumeSaved()`를 부르면 저장할 때 보던 대사부터 이어 간다. 이어 하기(`GBibleRuntime.LoadRun`)가 성공한 다음에 부른다.

**대화가 중심인 게임(비주얼 노벨)에서 기믹을 거는 곳.** 이때도 다이얼로그는 말과 연출만 맡고, 기믹은 패시브 효과(가진 동안 발동하는 DB › 패시브 효과 항목)·상태 효과(걸려 있는 동안 효과가 나는 DB › 상태 효과 항목)가 맡는다. 그래프는 플레이어 데이터와 이벤트로 넘겨줄 뿐이다.

1. **값 바꾸기**(플레이어 데이터에 주기): 패시브 효과의 가진 목록에 ID를 넣거나(가호 얻기), 호감도 같은 수치를 올린다.
2. **신호 보내기**: Client 코드가 받아 행동을 `Game.Do`한다. 행동이 이벤트를 쏘고 상태 효과를 건다 → 패시브 효과·상태 효과가 받는다.
3. **조건 분기·선택지의 보일 조건**: 기믹이 바꾼 플레이어 데이터를 '플레이어 데이터 검사'로 읽는다.

> [!note]- 비주얼 노벨에서 알아 둘 지금 동작
> - 대사 진행(클릭·선택지)은 행동이 아니다. Undo로 대사가 돌아가지 않고, 이전 대사로 되감기·읽은 대사 기록·다이얼로그 대기열은 없다.
> - 대사만 넘긴 진행은 다음 판 저장(행동 뒤·끌 때)에 함께 들어간다. 세이브 30칸처럼 프로필 하나에 판 저장 여러 칸을 두는 기능은 없다(슬롯은 프로필까지 묶인다).
> - 신호 보내기는 대화를 멈추지 않는다. 상점·고르기(`Game.Ask`)를 기다려야 하면 신호 다음을 끝으로 두고, 끝난 뒤 다른 시작 ID로 다시 연다(위 `noGold`처럼).
> - 그래프의 조건에는 트리거 순간이 없어 '확률'은 늘 거짓이고 사용자를 보는 조건은 쓰지 못한다. 무작위 분기는 값 바꾸기의 프로젝트 `Reward`가 행동 안에서 뽑아 플레이어 데이터에 적는다.
> - 값 바꾸기에는 `Reward`만 고른다('플레이어 데이터에 주기'·'다이얼로그 재생'·프로젝트 `Reward`). 상태 효과 걸기·이벤트 쏘기는 신호 → 행동으로 한다.
> - `Reward`는 Rules에, `Dialogues`를 부르는 코드는 Client에 둔다. Rules 어셈블리는 다이얼로그 모듈을 참조하지 않는다.
> - 자세히는 매뉴얼 [24장](../../Manual/24-Dialogue.md) 2절(기믹 걸기)·8절(현재 동작).

> [!note]- 다른 장르라면
> 대사가 이야기의 중심인 게임은 다이얼로그 화면(UXML·USS)을 프로젝트에 맞게 고치고, 필요한 동작은 `DialogueUIExtension`을 붙여 더한다. 코드는 요소를 이름(`dialogue-root`·`dialogue-text` …)으로만 찾으므로 모양은 자유롭게 바꿔도 된다.

## 7. 소리

- 배경 음악은 `GameAudio.PlayBgm(clip)`, 효과음은 `GameAudio.PlaySfx(clip)`.
- 오디오는 게임이 켜질 때 저절로 만들어지고 씬이 바뀌어도 남는다.

전투 연출은 피해 숫자를 띄울 때 효과음을 낸다(4절 `BattlePresenter.ShowDamage`의 한 줄).

```csharp
GameAudio.PlaySfx(hitSound, pitch: UnityEngine.Random.Range(0.95f, 1.05f));   // 연출의 무작위는 UnityEngine.Random
```

소리도 연출 대기열 안에서 내면 피해 숫자와 같은 차례에 난다.

> [!note]- 소리와 게임 시간 · 음량
> - 소리는 게임 시간과 상관없이 흐른다. 다이얼로그나 메뉴로 게임이 멈춰도 배경 음악은 계속 난다.
> - 다이얼로그가 자기 배경 음악을 틀었다면 끝날 때 전의 곡으로 돌아간다.
> - 설정 화면의 음량 슬라이더는 DB › 설정의 항목(값 종류 숫자(슬라이더), 적용 대상 "브금 음량")으로 만든다. `GameSettings.Set(id, 값)`이 기기에 저장하고 음량에 적용한다([10장](10-SaveAndRelease.md) 1절). 코드로 바로 바꿀 때는 `GameAudio.SetVolume(AudioChannel.Bgm, 값)`·`GameAudio.VolumeChanged`.

## 8. 인벤토리 칸

- 판 데이터의 `potions`는 인벤토리 모듈의 `Inventory`다(칸 수 3). 칸에는 물약 원본 ID(`item`)와 개수(`count`)만 있다.
- 칸마다 버튼을 그리고, 남은 칸은 빈 칸으로 채운다. 버튼은 `DrinkPotion` 행동을 보낸다(2절).

```csharp
foreach (var slot in run.potions.slots)
{
    var potion = slot.item;
    _potions.Add(new Button(() => Drink(potion)) { text = Texts.Get("hud.potion", ("potion", potion), ("count", slot.count)) });
}
for (var i = run.potions.slots.Count; i < run.potions.capacity; i++)
    _potions.Add(new Label(Texts.Get("hud.emptySlot")));
```

> [!note]- 마시기 한 번 = Undo 한 번
> 칸에서 하나 빼기와 물약의 사용 기능(회복)이 한 행동이라, Undo 한 번에 개수와 체력이 함께 돌아온다. 물약 아이콘이 필요하면 원본(`Rules.PotionOf(game, slot.item)`)에서 읽는다.

## 9. 체크리스트

- [ ] 화면 코드는 Client 폴더에 있고, 플레이어 데이터를 읽기만 한다.
- [ ] 화면 갱신은 `GameView.Redraw`로 등록해 프레임 끝에 한 번(연출이 끝난 뒤) 그린다. 프리팹 목록은 `ViewList`, 값 하나는 컴포넌트 데이터 연결.
- [ ] 등록·받기는 `OnEnable`, 해제(`Dispose`)는 `OnDisable`에서 한다.
- [ ] 버튼은 행동에 넣을 값(대상 객체·원본 ID)을 쥐고 행동을 보내며, 실패 예외를 잡아 안내한다. `GameView.Busy`인 동안은 입력을 받지 않는다.
- [ ] 예상 피해·버튼 막기는 화면에서 따로 계산하지 않고 `Game.Preview`·`Game.CanDo`로, 그릴 때 한 번 부른다.
- [ ] 연출은 `SubscribeAfterAction`으로 받아 `GameView.Present`에 넣고, 기다리기는 `GameView.Wait`다.
- [ ] 연출의 무작위는 `UnityEngine.Random`, 규칙의 무작위는 `Game.Random`이다.
- [ ] 화면의 모든 글이 텍스트 키이고, DB 항목 이름은 `<ID>.name`이다. 행동의 실패 글은 `Game.Fail`의 키로 번역된다. 언어를 바꾸면 다시 그려진다.
- [ ] 다이얼로그는 말·연출만, 규칙이 있는 일은 행동이 맡는다.
- [ ] 판 데이터에는 원본 ID와 바뀌는 값만 있고, 이름·그림은 원본에서 읽는다.

> [!note]- 더 보기(매뉴얼)
> - [01. 플레이어 데이터](../../Manual/01-PlayerData.md) — `Changed`·`DataChanged`·`Synced`, Sync가 일어나는 때
> - [10. 이벤트](../../Manual/10-Events.md) — `Subscribe`·`SubscribeAfterAction`·`Dispose`, 받는 쪽 규칙
> - [32. 화면 연결·연출 대기열·씬 흐름·월드 상태](../../Manual/32-View-Presentation-Scenes.md) — `GameView.Present`·`Busy`·`Find`, `ViewList`, 데이터 연결
> - [33. 고정 틱·미리보기·CSV](../../Manual/33-Tick-Preview-Csv.md) — `Game.Preview`·`CanDo`·`IsPreviewing`
> - [30. 밸런싱 값·곡선·가중치 표·플레이어용 실패](../../Manual/30-Balance-Tables.md) — `GameFailure`·`Game.Fail`·`FailureKeys`
> - [07. 난수와 시간](../../Manual/07-Random-Time.md) — `GameTime`, 게임 시계
> - [23. 텍스트와 현지화](../../Manual/23-Text-Localization.md) — `Texts`, 문장 틀, `GameLanguage`
> - [24. 다이얼로그](../../Manual/24-Dialogue.md) — 그래프·신호·저장 이어 가기
> - [26. 오디오](../../Manual/26-Audio.md) — 채널·음량
> - [25. 인벤토리](../../Manual/25-Inventory.md) — 칸·더하기·빼기
