# 06. 화면과 연출

[필수] 화면은 플레이어 데이터를 **읽기만** 하고, 값을 바꾸는 입력은 모두 행동으로 보낸다. 데이터는 행동이 끝나는 순간 최종 모습이 되고, 연출(피해 숫자·소리·대사)은 그 뒤를 시간을 두고 따라간다. 이 장의 코드는 모두 **Client 폴더**에 둔다(카드 던전은 `Client/Screens`·`Client/Presentation`).

화면의 문법·레이아웃·디자인은 **`UiGuide.md`**(UI 가이드)와 UI 키트(`Templates/UI/`)가, 화면 기능의 모든 칸·API는 매뉴얼 [35장](../../Manual/35-Screens.md)(화면)·[32장](../../Manual/32-View-Presentation-Scenes.md)(씬 오브젝트·연출 대기열)이 맡는다. 이 장은 카드 던전으로 흐름과 습관만 보인다.

> [!note]- 이 장에서 쓰는 말
> | 말 | 뜻 |
> |---|---|
> | 화면 | UI Toolkit 화면 하나 = DB › 화면 항목(층·뒤로로 닫기·열 씬) + `Assets/MomoiProject/UI/Screens/<이름>.uxml`(+ `.uss`) + 필요하면 화면 코드(`[BindTo(GameIds.Screen.X)] class X : GameScreen`). |
> | `binding-path` | UXML 칸에 적는 데이터 경로(`run:gold`, `run:gold@hud.gold`, `@hud.endTurn`). 값만 보이는 칸은 코드 없이 이것으로 그린다. |
> | Sync · ChangeSet | Sync는 플레이어 데이터의 사본과 지금 값을 비교해 바뀐 곳을 찾는 동작(행동 시작·끝, 행동 안의 `Game.Find`·`Game.All`, 매 프레임 끝). ChangeSet은 Sync 한 번이 찾은 변경 묶음(멀티플레이 전송 단위). |
> | 문장 틀 | 텍스트 키에 들어 있는 글. `{값}`·조사·복수형을 문법으로 적는다. |
> | 연출 대기열 | `GameView.Present`로 넣은 연출(코루틴 하나 = 연출 하나)을 하나씩 차례로 트는 줄. 남아 있는 동안 `GameView.IsPresenting`이 참이다. |
> | 미리보기 | 행동을 실제로 해 보고 결과만 읽은 뒤 모두 되돌리는 것(`Game.Preview`·`Game.CanDo`). 기록·Undo·연출이 남지 않는다. |
> | `GameFailure` | 플레이어에게 보일 실패(텍스트 키 + 기본 문장). `Message`를 읽을 때마다 지금 언어로 만든다. |

---

## 1. 화면 갱신

- 화면에 보일 값(체력·골드·핸드·물약 슬롯)이 모두 플레이어 데이터나 원본 인스턴스에 있는지 본다. 없으면 [02장](02-DesignToCode.md)으로 돌아가 필드를 더한다.
- **UI(HUD·메뉴·팝업)는 화면으로 만든다.** 값만 보이는 칸은 UXML `binding-path`, 데이터 목록은 화면 코드의 `List<T>`, 그 밖의 칸은 `Draw`. 데이터·언어가 바뀐 프레임 끝에 한 번(연출이 끝난 뒤) 다시 그려진다.
- **씬에 놓이는 것(캐릭터·카드 오브젝트)은** `GameView.Redraw(그리기, 키…)`·`ViewList`·컴포넌트 '데이터 연결'(아래 접힌 칸).
- 고르기 창·확인·설정·세이브 슬롯·일시정지·알림은 **기본 화면**이 있어 만들지 않는다(매뉴얼 35장 2-5).

카드 던전 전투 HUD(전체 예제는 매뉴얼 35장 5-1):

```xml
<ui:Label binding-path="run:gold@hud.gold" />                <!-- 텍스트 hud.gold = "{value} 골드". 코드 없음 -->
<ui:Button name="end-turn" binding-path="@hud.endTurn" />
```

```csharp
[BindTo(GameIds.Screen.BattleHud)]
public class BattleHud : GameScreen
{
    protected override void OnOpen()
    {
        OnClick("end-turn", () => new EndTurn());
        var hand = List<string>("hand", Item("CardItem"), () => Game.Data().Run.battle?.hand ?? new List<string>(), DrawCard, GameIds.PlayerData.Run.Id);
        // hand.OnCreate — 카드 칸의 버튼 → 행동(2절)
    }

    protected override void Draw()   // binding-path로 안 되는 칸만(값 둘을 한 틀에)
    {
        var hero = Game.Data().Run.hero;
        Q<Label>("hp").text = Texts.Get("hud.hp", ("hp", hero.hp), ("max", hero.maxHp));
    }
    // DrawCard(칸, 카드 id): 이름·비용을 원본에서 읽어 넣는다(아래 2절의 미리보기도 여기서)
}
```

**만드는 순서**(관례 15절, 매뉴얼 35장 5-6): 반영 스크립트에서 `ScreenSetup.Ensure()` + DB › 화면 항목(`layout` = UXML) + UXML·USS → 새로 읽기(`RunTests.request`) → `GameIds.Screen.BattleHud`가 생긴 뒤 화면 코드. 화면을 만들거나 고치면 `Play.request`로 둘러보고 찍은 그림이 UI 가이드대로인지 본다(매뉴얼 38장 2-5).

> [!note]- 왜 알림마다 그리지 않나
> 값이 바뀌었다는 알림은 행동 하나에도 여러 번 온다(행동 안의 `Game.Find`·`Game.All`이 중간에 Sync하면 그때, 행동이 끝날 때 또, 실패하면 되돌린 알림, Undo해도). 알림마다 그리면 같은 화면을 한 프레임에 여러 번 계산한다. 그래서 화면·`GameView`는 알림이 오면 "다시 그릴 것"으로 **표시만** 하고, 프레임 끝(행동 밖)에 한 번 그린다. 언어가 바뀌어도 같은 프레임 끝에 다시 그린다. 처음 한 번은 등록한(연) 프레임 끝에 그린다.

> [!note]- 키 고르기 · 해제
> - 화면 코드는 `Watch`(다시 그릴 키 — 기본은 binding-path에 쓴 키들), `GameView.Redraw`는 넘긴 키. 키를 비우면 어느 플레이어 데이터가 바뀌어도 그린다.
> - 카드 던전은 판의 모든 값이 `run` 하나에 있으므로 `run`이 바뀌면 화면 전체를 다시 그린다.
> - 연출을 기다리지 않을 것(골드 표시처럼 바로 바뀌어도 되는 것): 화면은 `WaitForPresentation`을 거짓으로, `GameView.Redraw(그리기, waitForPresentation: false, 키)`.
> - `GameView.Redraw`가 돌려준 `IDisposable`은 반드시 정리한다(화면 코드면 `Own(…)`, MonoBehaviour면 `OnDisable`에서 `Dispose`). 등록은 씬보다 오래 살아서, 사라진 오브젝트를 계속 부른다. 화면 코드의 `List<T>`는 닫을 때 저절로 정리된다.
> - 세션이 바뀌면(세이브 슬롯 바꾸기·다시 시작) 저절로 다시 연결하고 모두 다시 그린다.

> [!note]- 씬 오브젝트 — `ViewList`·데이터 연결·`GameView.Find`
> 적 그림처럼 씬에 프리팹으로 두는 것은 "목록 다시 만들기"를 라이브러리에 맡긴다.
> ```csharp
> // 적마다 프리팹 하나: 생기면 만들고, 사라지면 없애고, 순서를 맞추고, 값이 바뀌면 bind로 다시 그린다.
> _enemies = new ViewList<Enemy, EnemyView>(enemyPrefab, enemyRoot,
>     () => GBibleRuntime.Game.Data().Run.battle?.enemies, (view, enemy) => view.Show(enemy), GameIds.PlayerData.Run.Id);
> _enemies.OnRemove = (view, done) => view.PlayDeath(done);   // 사라질 때 연출(연출 대기열에 들어가고, done 뒤에 없앤다)
> // OnDestroy에서 _enemies.Dispose();
> ```
> - `ViewList`가 만든 오브젝트는 항목 id로 등록된다. 연출이 `GameView.Find<EnemyView>(e.targetId)`로 맞은 적의 오브젝트를 찾는다. 직접 등록은 `GameView.Register(id, gameObject)`. 화면(UI Toolkit) 칸은 `GameView.FindElement(id)`(매뉴얼 35장 5-4).
> - 값 하나를 TMP 글자·슬라이더에 잇기만 하면 코드도 없다: 컴포넌트 **데이터 연결**(`MomoiProject/데이터 연결`)에 키 `run`, 필드 경로 `battle.energy`, 글자 틀 `에너지 {value}`(텍스트 키를 적으면 번역된 틀). 이것도 연출이 끝난 뒤에 바뀐다. UI Toolkit 칸은 `binding-path`를 쓴다.
> - 씬 오브젝트를 누르는 입력은 화면 칸 위인지 먼저 보고, 누른 오브젝트의 데이터 id로 행동을 만든다(관례 21절).

> [!note]- 어떤 알림을 받나
> | 이럴 때 | 이것 |
> |---|---|
> | 플레이어 데이터나 언어가 바뀌면 다시 그린다 | 화면의 `binding-path`·`Draw`, 씬 오브젝트는 `GameView.Redraw(그리기, 키…)`(프레임 끝에 한 번) |
> | 무엇이 바뀌었는지(핸드에 한 장 추가, 적 하나 제거)에 따라 다르게 움직인다 | `session.Data.DataChanged`(키 + 바뀐 경로 목록 `DataChange`: 종류 Set·Insert·Remove·Reorder, 경로, 옛 값·새 값). 알림마다 오므로 바뀐 곳을 모아 두고 프레임 끝에 반영한다 |
> | 바뀐 키만 직접 받는다 | `session.Data.Changed`(`Action<string>`). `GameView`가 안에서 쓰는 알림이다 |
> | 다른 쪽(서버·클라이언트)에 변경을 보낸다 | `session.Data.Synced`(ChangeSet). 화면용이 아니다([11장](11-MultiplayerReady.md)) |
> | "피해를 줬다", "적이 쓰러졌다"처럼 일어난 일에 맞춰 연출한다 | 이벤트를 받는다(`session.Events.SubscribeAfterAction<T>`, 3절) |
> | 행동이 끝났을 때 한 번 무언가 한다 | `session.Actions.Executed`(`ActionRecord`, 안쪽 행동이면 `Nested`가 참) |
> | 실패해 취소된 행동을 안다 | `session.Actions.TransactionAborted` |

> [!note]- 값을 어디서 읽나
> | 이럴 때 | 이것 |
> |---|---|
> | 판·전투 값(체력, 골드, 핸드) | `Game.Data().Run` |
> | 카드 비용·그림, 몬스터 초상 같은 고정 값 | 원본 인스턴스. `Rules.CardData(game, deckCard.card)`처럼 원본 ID로 복사 없이 읽는다(읽기만 한다) |
> | 플레이어에게 보일 이름 | 텍스트 `<ID>.name`(`Texts.NameOf(id)`, 또는 문장 틀에 ID를 값으로 넘김, 5절) |
> | 매 프레임 움직이는 위치·애니메이션 | Client 쪽(MonoBehaviour 필드). 플레이어 데이터에 넣지 않는다 |
>
> 판 데이터에 스프라이트 참조가 없으니 판 저장 경고("저장할 수 없는 참조입니다")도 없고, 이어 하기 뒤에도 그림이 그대로 나온다. 개발자가 원본의 비용을 고치면 진행 중인 판의 모든 장에 바로 반영된다.

> [!note]- 다른 장르라면
> - **실시간 액션**: 위치·속도·애니메이션은 화면 쪽이 들고 매 프레임 그린다. 플레이어 데이터 알림은 체력·탄약·쿨다운 같은 규칙 값의 HUD에만 쓰고, 피격 이펙트는 이벤트를 받아 튼다(동시에·입력을 막지 않을 것은 `GameView.Play`). 쿨다운 원·남은 초는 화면 코드의 `Tick`에서 `준비 시각 - Game.Session.Clock.Time`으로 그린다(매뉴얼 35장 2-3).
> - **턴제 전술**: 유닛이 많아 화면 전체를 다시 그리기 무겁다. `DataChanged`의 경로(`units[#12].hp`, `units[#3]` 제거)를 보고 바뀐 유닛만 갱신한다. 유닛 이동 연출은 이동 행동이 쏜 이벤트를 받아 칸에서 칸으로 움직인다.

## 2. 입력은 행동으로

- 버튼은 `OnClick("이름", () => new 행동 { … })`. 대상(덱 카드·적)은 **객체 그대로** 넣어도 된다. 원본(물약 등)은 원본 ID다. 행동 필드에는 무엇을 할지만 넣는다(가격 같은 값은 규칙이 읽는다 — 02장 4절).
- 버튼이 아닌 입력(카드를 적 위에 놓기·키)은 그 자리에서 `GameScreens.Do(() => …)`.
- 둘 다 연출이 남아 있거나 선택을 기다리는 동안(`GameView.Busy`)은 무시하고, 플레이어용 실패(에너지 부족 등)는 알림으로 보인다. 화면 코드에 `try`/`catch`나 `Busy` 검사를 쓰지 않는다(매뉴얼 35장 2-4).

```csharp
// 핸드 칸을 만들 때: 누르는 순간의 데이터로 행동을 만든다
hand.OnCreate = (element, id) => OnClick(element.Q<Button>("card"),
    () => new PlayCard { card = Rules.CardOf(Game, id), target = Rules.LivingEnemies(Game).FirstOrDefault() });   // 대상 고르기 화면은 생략
```

> [!note]- 왜 객체를 넣어도 되나
> Undo·불러오기 뒤에는 같은 카드가 다른 객체일 수 있다. 그래도 행동은 실행 직전에 대상 필드의 id로 지금 데이터의 객체를 다시 찾아 넣으므로, 맞는 카드가 나간다. 행동을 만드는 함수는 누를 때마다 돌아 그 순간의 데이터로 대상을 찾는다.

> [!note]- 왜 연출 중에는 입력을 막나 · `Busy`
> 다시 그리기는 연출이 끝난 뒤에 된다(4절). 피해 숫자가 뜨는 동안 핸드는 아직 낸 카드를 보여 준다. 그 카드를 또 누르면 "핸드에 없는 카드입니다"로 실패한다. 그래서 연출이 남아 있는 동안은 받지 않는다.
> - `GameView.Busy` = 연출 대기열이 남아 있거나(`IsPresenting`) 선택을 기다리는 중(`Game.IsWaiting`)이다. `OnClick`·`GameScreens.Do`가 이것을 본다. 고르기 창 안의 다시 뽑기처럼 선택 중에도 보내야 하면 `GameScreens.Do(() => 행동, whileChoosing: true)`.
> - 막는 것은 화면의 일이다. 시스템은 막지 않는다. 기다리는 선택은 기본 고르기 창이 저절로 그린다.
> - 연출을 빨리 넘기는 버튼은 `GameView.SkipPresentation()`(4절). 행동이 아닌 화면 일이라 `OnClick("skip", GameView.SkipPresentation)`처럼 `Action` 쪽으로 잇는다.

> [!note]- 미리보기 — 예상 피해 · 낼 수 없는 이유(`Game.Preview`·`Game.CanDo`)
> 화면이 규칙을 따로 계산하지 않는다. 행동을 실제로 해 보고 결과만 읽은 뒤 모두 되돌린다. 기록(타임라인)·Undo·`SubscribeAfterAction` 연출·도전과제 달성이 남지 않고 난수도 돌아가므로, 실제로 내면 미리본 값과 같다. 핸드 칸을 그릴 때(bind) 카드마다 한 번 불러 칸의 `tooltip`에 넣는다.
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
> - `Draw`·bind 안에서 불러도 된다. 미리보기는 데이터 변경 알림을 보내지 않으므로 다시 그리기를 부르지 않는다. 다만 행동 하나를 통째로 실행하므로 매 프레임(`Tick`) 부르지 말고 그릴 때 한 번 부른다.
> - 규칙 코드가 게임 밖으로 나가는 일(분석 전송 등)을 하면 `Game.IsPreviewing`일 때 건너뛴다.

## 3. 이벤트로 연출

- 규칙이 쏜 이벤트를 받아 연출을 튼다. 카드 던전은 `Rules.Damage`가 쏘는 `DamageDealt`·`EnemyKilled`를 받는다.
- 연출은 `session.Events.SubscribeAfterAction<T>`로 받는다. 행동이 **성공한 뒤에만**, 쏜 순서대로 온다. 실패해 취소된 행동의 이벤트는 오지 않는다.
- 받은 이벤트는 연출 대기열(`GameView.Present`)에 넣는다(4절). [필수] 받는 쪽에서는 플레이어 데이터를 바꾸지 않는다.

```csharp
protected override void OnOpen()   // 화면 코드: Own에 맡기면 닫을 때 정리된다(MonoBehaviour면 OnDisable에서 Dispose)
{
    var events = Game.Session.Events;
    Own(events.SubscribeAfterAction<DamageDealt>(e => GameView.Present(ShowDamage(e))));
    Own(events.SubscribeAfterAction<EnemyKilled>(e => GameView.Present(ShowKill(e))));
}
```

> [!note]- 왜 행동이 끝난 뒤에 받나
> `Subscribe`로 받으면 이벤트를 쏘는 그 자리, 즉 행동 한가운데에서 불린다. 화염구가 피해를 준 뒤 같은 행동 안에서 예외가 나면, 값은 모두 취소되었는데 피해 숫자는 이미 떠 버린다. 멀티 클라이언트에서는 규칙이 서버에서 돌아 `Subscribe`가 아예 오지 않는다.
>
> `SubscribeAfterAction`은 행동 안에서 쏜 이벤트를 모아 두었다가, 바깥 행동이 성공하면 쏜 순서대로 넘기고 실패하면 버린다. 그래서 받는 순간은 늘 행동 밖이고, 데이터는 이미 최종 모습이다.
> - `PlayCard` 안에서 부르는 `Game.Use`·`Game.Do(new WinBattle())`가 쏜 이벤트도 바깥 행동(`PlayCard`)이 끝날 때 함께 온다.
> - 행동 밖에서 쏜 이벤트(도전과제 달성처럼 행동이 끝난 뒤 쏘는 것)는 바로 온다.
> - `Dispose`하면 아직 넘기지 않은 것도 버린다. 이미 연출 대기열에 넣은 것은 대기열이 튼다(씬을 떠날 때 버리려면 `GameView.ClearPresentation()`).
> - 미리보기(`Game.Preview`, 2절)에서 쏜 이벤트는 오지 않는다.

> [!note]- 연출에서 하지 않는 것
> 이 표는 모두 [필수].
>
> | 하지 않는 것 | 이유 |
> |---|---|
> | 받는 쪽에서 플레이어 데이터를 고친다 | 행동 밖 변경이 되어 Undo·저장·멀티플레이에서 빠진다 |
> | 행동 안에서 연출을 기다린다(코루틴·`await`) | 행동은 한 번에 끝나야 한다 |
> | 연출의 무작위에 `Game.Random`을 쓴다 | 연출이 규칙의 난수 흐름을 소비하면 재현·Undo가 어긋난다. 연출은 `UnityEngine.Random` |
> | 판 데이터에 스프라이트 같은 에셋 참조를 둔다 | DB 항목이 아닌 에셋 참조는 판 저장에 담기지 않는다("저장할 수 없는 참조입니다"). 판 데이터에는 원본 ID만 둔다 |

## 4. 데이터와 연출의 시간

- 연출은 데이터가 아니라 **연출 대기열**을 따라간다. `GameView.Present(코루틴)`으로 넣으면 앞의 연출이 끝난 뒤에 하나씩 튼다.
- 연출 하나 = 코루틴 하나. 기다리기는 `WaitForSeconds` 대신 `GameView.Wait(초)`.
- 화면(`binding-path`·`Draw`)과 `GameView.Redraw`는 대기열이 빌 때까지 기다렸다가 다시 그린다. 피해 숫자가 다 뜬 뒤에 체력·핸드가 바뀐다.

```csharp
// 연출 하나 = 코루틴 하나. 대기열은 앞의 연출이 끝나야 다음 것을 튼다.
IEnumerator ShowDamage(DamageDealt hit)
{
    Popup(Texts.Get("popup.damage", hit), hit.targetId);          // [GameEvent] 객체를 값 묶음으로(생성 코드로 읽는다). 적 칸 위에 띄우기는 35장 5-4
    GameAudio.PlaySfx(hitSound, pitch: UnityEngine.Random.Range(0.95f, 1.05f));   // 소리도 대기열 안에서 — 피해 숫자와 같은 차례(7절)
    yield return GameView.Wait(gap);                               // 연출 속도·건너뛰기를 따른다
}
```

> [!note]- 데이터는 이미 끝나 있다
> 화염구로 고블린을 잡으면 `PlayCard`가 끝나는 순간 데이터는 최종 모습이다. 고블린은 `battle.enemies`에서 빠졌고, 남은 적의 체력은 깎였고, 전투에서 이겼다면 `battle`이 `null`이 되고 층이 올랐다. 화면이 그 프레임에 데이터대로 그리면 고블린이 피해 숫자도 없이 바로 사라진다.
> - 그래서 다시 그리기는 기본으로 연출 대기열이 빌 때까지 기다린다(화면 `WaitForPresentation`, `GameView.Redraw`의 `waitForPresentation`). 피해 숫자 → 쓰러짐 연출이 끝난 다음 프레임 끝에 고블린이 화면에서 빠진다.
> - 사라지는 순간 자체를 연출하려면 `ViewList`·`ElementList`의 `OnRemove`(1절).
> - 체력 게이지는 다시 그릴 때 데이터 값으로 곧장 바꿔도 되고, 보이는 값을 데이터 값 쪽으로 시간을 두고 따라가게 해도 된다(`KitGauge` 기본 — 줄 때 0.35초 뒤 0.4초 동안, 찰 때 0.25초 뒤 0.3초 동안, UiGuide 3-7). 어느 쪽이든 데이터는 건드리지 않는다.

> [!note]- 연출 대기열 자세히
> | 이럴 때 | 이것 |
> |---|---|
> | 코루틴으로 쓴 연출 · 콜백 연출(트윈) · 잠깐 쉬기 | `GameView.Present(ShowDamage(e))` · `Present(done => card.MoveTo(slot, done))`(`done`을 반드시 부른다 — 30초 넘으면 경고 후 다음) · `Present(0.3f)` |
> | 대기열 밖에서 동시에, 입력을 막지 않고(실시간 피격·클릭 숫자) | `GameView.Play(…)` |
> | 연출 속도 · 넘기기 · 버리기 · 빈 순간 | `GameView.PresentationSpeed`(시간을 `DeltaTime`·`Wait`로 재야 따른다) · `SkipPresentation()`(남은 연출을 10배로) · `ClearPresentation()`(씬을 떠날 때) · 이벤트 `Presented` |
>
> 연출 하나가 예외를 내도 그 연출만 끝나고 대기열은 계속된다. 매뉴얼 [32장](../../Manual/32-View-Presentation-Scenes.md).

> [!note]- 게임 시간과 멈추지 않는 시간
> `GameView.Wait`·`DeltaTime`은 `Time.deltaTime`(게임 시간)에 연출 속도를 곱한 값이라, 다이얼로그·일시정지 메뉴가 `GameTime.Pause`로 게임을 멈추면 연출도 멈춘다. 멈추면 안 되는 연출은 대기열 밖에서 `Time.unscaledDeltaTime`을 쓴다(화면 코드의 `Tick`은 실제 시간이다).

## 5. 텍스트와 현지화

- 화면의 모든 글은 텍스트 키로 찾는다(관례 7절): UXML은 `binding-path="@hud.endTurn"`(번역 글)·`"run:gold@hud.gold"`(값 + 틀), 코드는 `Texts.Get(키, (이름, 값), …)`. 코드·UXML에 글을 적지 않는다.
- DB 항목 이름은 값으로 **ID**를 넘기면 문장 틀이 그 언어의 `<ID>.name` 텍스트로 바꾼다. 이름만이면 `Texts.NameOf(id)`.
- 언어가 바뀌면 화면과 `GameView.Redraw`에 등록한 것은 그 프레임 끝에 저절로 다시 그려진다(1절). 따로 받을 것이 없다.

```csharp
// "card"에 원본 ID를 넘기면 문장 틀이 <원본 ID>.name 텍스트(현재 언어의 카드 이름)로 바꾼다.
element.Q<Label>("title").text = Texts.Get("card.button", ("card", deckCard.card), ("cost", Rules.CardData(Game, deckCard.card)?.cost ?? 0));
```

> [!note]- 카드 던전 전투 화면의 키
> | 키 | ko | en |
> |---|---|---|
> | `hud.hp` | `체력 {hp}/{max}` | `HP {hp}/{max}` |
> | `hud.gold` | `{value} 골드` | `{value} Gold` |
> | `hud.energy` | `에너지 {value}` | `Energy {value}` |
> | `hud.endTurn` | `턴 끝` | `End Turn` |
> | `card.button` | `{card} ({cost})` | `{card} ({cost})` |
> | `hud.potion` | `{potion} ×{count}` | `{potion} ×{count}` |
> | `hud.emptySlot` | `빈 슬롯` | `Empty` |
> | `popup.damage` | `-{amount}` | `-{amount}` |
> | `popup.kill` | `{monster:을} 쓰러뜨렸다!` | `Defeated {monster}!` |
> | `hint.damage` | `예상 피해 {amount}` | `{amount} damage` |
> | `fail.notInBattle` · `fail.notInHand` · `fail.noPotion` | `전투 중이 아닙니다.` · `핸드에 없는 카드입니다.` · `물약이 없습니다.` | `Not in battle.` · `That card is not in your hand.` · `No potion left.` |
> | `momoi.fail.notEnough`(라이브러리) | `{what:이} 모자랍니다 ({have}/{need}).` | `Not enough {what} ({have}/{need}).` |
> | `energy.name` · `gold.name` | `에너지` · `골드` | `energy` · `gold` |
> | `strike.name` · `goblin.name` · `healPotion.name` · `burn.name` … | `타격` · `고블린` · `회복 물약` · `화상` … | `Strike` · `Goblin` · `{count, plural, one {Healing Potion} other {Healing Potions}}` · `Burn` … |
>
> `binding-path`의 틀에는 값이 `{value}`로 들어간다. 값 둘 이상(체력/최대)은 `Draw`에서 `Texts.Get`으로 넣는다.

> [!note]- 조사·복수형 · 값 묶음(문법 전부는 매뉴얼 23장)
> - `{monster:을}`은 한국어 조사("고블린을"·받침 없으면 "를"), 영어 이름 텍스트는 함께 넘긴 `count`로 `{count, plural, …}`를 고른다. 게임 코드는 언어마다 분기하지 않는다.
> - `[GameEvent]` 이벤트 객체를 값 묶음으로 통째로 넘길 수 있다(`Texts.Get("popup.damage", hit)` — 생성 코드로 읽어 리플렉션이 없다). 익명 객체(`new { gold = 3 }`)는 부를 때마다 리플렉션이라 자주 그리는 곳은 튜플을 쓴다.
> - 틀에서 점으로 객체 안의 필드를 읽는다(`{hero.hp}`). 다이얼로그 대사도 같은 규칙이다(6절).

> [!note]- 씬 오브젝트의 글
> 씬의 TextMeshPro 글은 컴포넌트 **다국어 텍스트**(`LocalizedText`)로 인스펙터만으로 번역한다(붙을 때 한 번 글 넣는 대리자를 만들어 리플렉션을 반복하지 않는다). `GameView`·화면을 쓰지 않는 코드라면 `GameLanguage.Changed`를 직접 받는다.

> [!note]- 행동의 실패 글 — `Game.Fail`과 텍스트 표
> 행동은 플레이어에게 보일 실패를 텍스트 키와 기본 문장으로 던진다([03장](03-GameLoop.md) 6절). `GameFailure`는 `Message`를 읽을 때마다 지금 언어로 만든다.
> - 텍스트 표에 키가 있으면 그 틀, 없으면 기본 문장, 둘 다 없으면 `[키]`. 그래서 번역 전에도 한국어 기본 문장이 보이고, 번역가는 원문 표에 키만 채운다.
> - 라이브러리의 실패도 같은 형식이다. `Game.Pay`가 모자라면 `momoi.fail.notEnough`(값 `{what}`·`{have}`·`{need}`), 선택 대기의 답이 틀리면 `momoi.fail.chooseExactly` 등(키 목록은 `FailureKeys`). 이 키를 텍스트 표에 넣으면 번역된다. `{what}`에는 `Game.Pay`의 `what`(텍스트 키면 번역된 글)이 들어간다. `what`을 비우면 "판 › battle.energy"처럼 데이터 이름과 필드 경로가 들어가므로 플레이어에게 보일 비용에는 늘 넘긴다.
> - 화면은 실패를 잡지 않는다. `OnClick`·`GameScreens.Do`가 `GameFailure`의 글을 알림으로 보인다(DB › 화면 설정 '실패를 알림으로'). 없는 ID 같은 프로그래머 실수는 `GameFailure`가 아니라 그냥 `InvalidOperationException`이라 콘솔에 드러난다.

## 6. 다이얼로그

상점 층에 들어가면 상점 주인이 말을 걸고, "구경한다"를 고르면 상점 화면이 열린다. [필수] 대사·그래프는 **개발자 몫**이다(관례 18절). 코드는 **열기와 신호 받기**만 한다.

1. (AI) 다이얼로그를 켠다: `ProjectSettings/MomoiProject/Features.json`의 `enabled`에 `dialogue`(01장 1절). 다시 컴파일된 뒤 반영 스크립트에서 다이얼로그 UI 템플릿 `GBible.EditorTools.Graph.DialogueTemplate.Create()`를 한 번 부른다(부를 때마다 새 폴더를 만드므로 한 번만 도는 반영 스크립트 버전 하나에).
2. (개발자) DB › **스탠딩 프로필**에 상점 주인(`merchant`)과 스탠딩 그림을 넣고, `MomoiProject › 다이얼로그 시스템 › 그래프`에서 다이얼로그 `shopkeeper`를 만든다: 시작 → 대사("어서 와, 모험가.", 화자 `merchant`) → 선택지 "구경한다" → **신호 보내기**(`openShop`) → 끝, 선택지 "나간다" → 끝. 시작 노드를 하나 더 두고 시작 ID를 `noGold`로 한다(대사 "돈이 모자라는군."). 다이얼로그·스탠딩 프로필은 개발자가 추가·삭제하는 종류다.
3. (AI) 신호 이름(`openShop`)·시작 ID(`noGold`)를 정해 개발자에게 알리고, 코드를 쓴다. 요청받으면 예시 그래프 하나만 만든다.

```csharp
// 상점 층의 화면 코드
protected override void OnOpen() => Dialogues.Signaled += OnSignal;
protected override void OnClose() => Dialogues.Signaled -= OnSignal;

public void Enter()   // 상점 층에 들어올 때
{
    GameAudio.PlayBgm(shopMusic);   // 같은 곡이면 끊기지 않고 이어진다
    if (!Dialogues.IsPlaying) Dialogues.Play(GameIds.Dialogue.Shopkeeper);
}

void OnSignal(string signal)
{
    if (signal == "openShop") GameScreens.Open(GameIds.Screen.Shop);
}

void Buy(string card)   // 상점 화면의 카드 버튼 — 돈이 모자라면 알림 대신 대사로
{
    if (!Game.CanDo(new BuyCard { source = card }, out _)) Dialogues.Play(GameIds.Dialogue.Shopkeeper, "noGold");   // 같은 다이얼로그의 다른 시작 ID
    else GameScreens.Do(() => new BuyCard { source = card });
}
```

> [!note]- 다이얼로그와 행동의 몫
> 그래프의 **값 바꾸기** 노드도 값을 바꿀 수 있다(행동 하나로 기록된다). 하지만 골드 검사·덱에 카드 넣기처럼 규칙이 있는 일은 행동(`BuyCard`)에 두고, 다이얼로그는 말과 연출만 맡긴다.
>
> 비싼 카드 선택지가 있다면 그 선택지의 **보일 조건**에 "플레이어 데이터 검사"(`run`의 `gold` 이상)를 걸고 흐리게 보여 주기를 켠다(개발자가 그래프에서).

> [!note]- 알아 둘 점
> - 다이얼로그가 열려 있는 동안 재생기가 `GameTime.Pause`로 게임 시간을 멈춘다. 전투 연출도 함께 멈춘다. `GameView.Busy`도 참이라 화면 버튼의 행동이 막힌다(걸으면서 듣는 대사면 `Dialogues.BlocksInput = false`).
> - 대사는 한 채널이다(매뉴얼 24장 5.1): 열린 대사가 있을 때 다른 행동 · 코드(위 `Dialogues.Play`)가 열면 끼어들었다가 끝나면 그 대사로 돌아오고, 같은 행동이 한꺼번에 연 것은 차례로 열린다. `Dialogues.Stop`은 기다리던 것 · 돌아갈 곳도 비운다.
> - 대사 원문의 `{…}`에는 모든 플레이어 데이터가 ID로 넘어가고, 점으로 값 안의 필드를 읽는다. "가진 돈은 {run.gold}골드군."
> - 판을 불러온 뒤 `Dialogues.ResumeSaved()`를 부르면 저장할 때 보던 대사부터 이어 간다. 이어 하기(`GBibleRuntime.LoadRun`)가 성공한 다음에 부른다.
> - 다이얼로그 화면(UXML·USS)은 프로젝트에 맞게 고치고, 필요한 동작은 `DialogueUIExtension`을 붙여 더한다. 코드는 요소를 이름(`dialogue-root`·`dialogue-text` …)으로만 찾으므로 모양은 자유롭게 바꿔도 된다. 다이얼로그의 배경 일러는 월드 쪽이다(UiGuide 1-8).

**대화가 중심인 게임(비주얼 노벨)에서 기믹을 거는 곳.** 이때도 다이얼로그는 말과 연출만 맡고, 기믹은 패시브 효과·상태 효과가 맡는다. 그래프는 플레이어 데이터와 이벤트로 넘겨줄 뿐이다.

1. **값 바꾸기**: 패시브 효과의 가진 목록에 ID를 넣는 것(가호 얻기)·플래그는 결과 '플레이어 데이터에 주기'로 된다. 호감도처럼 효과가 고칠 값(핵심 동사 — 행동 직전 이벤트를 거친다)은 '플레이어 데이터에 주기'로 필드를 직접 올리지 않는다 — 행동 직전 이벤트를 건너뛰어 "호감도 두 배" 같은 패시브 효과가 붙지 않는다. 그 규칙 함수를 부르는 프로젝트 결과 Block(`Reward` — `ChangeAffection { character, amount }`가 `Rules.ChangeAffection`, Rules에)을 만들어 값 바꾸기 노드에서 고른다(참조 2-2).
2. **신호 보내기**: Client 코드가 받아 행동을 보낸다. 행동이 이벤트를 쏘고 상태 효과를 건다 → 패시브 효과·상태 효과가 받는다.
3. **조건 분기·선택지의 보일 조건**: 기믹이 바꾼 플레이어 데이터를 '플레이어 데이터 검사'로 읽는다.

> [!note]- 비주얼 노벨에서 알아 둘 지금 동작
> 대사 진행은 행동이 아니라 Undo로 돌아가지 않고 되감기·읽은 대사 기록이 없다. 신호 보내기는 대화를 멈추지 않는다(기다려야 하면 신호 다음을 끝으로 두고 다른 시작 ID로 다시 연다). 그래프의 조건에는 트리거 순간이 없어 '확률'은 늘 거짓이다. 값 바꾸기에는 `Reward`만 고른다(상태 효과 걸기·이벤트 쏘기는 신호 → 행동). `Reward`는 Rules에, `Dialogues`를 부르는 코드는 Client에 둔다(Rules는 다이얼로그 모듈을 참조하지 않는다). 전부는 매뉴얼 [24장](../../Manual/24-Dialogue.md) 2절(기믹 걸기)·8절(현재 동작) — 이 게임 형태면 만들기 전에 먼저 읽는다.

## 7. 소리

- 배경 음악은 `GameAudio.PlayBgm(clip)`, 효과음은 `GameAudio.PlaySfx(clip)`. 오디오는 게임이 켜질 때 저절로 만들어지고 씬이 바뀌어도 남는다.
- 효과음은 연출 코루틴 안에서 낸다(4절 `ShowDamage`). 그러면 피해 숫자와 같은 차례에 난다.
- 소리 파일은 개발자가 넣는다. 자리는 반영 스크립트의 `AssetRequests.Audio(경로, 쓰는 곳, 초)`로 만든다(관례 18절).

> [!note]- 소리와 게임 시간 · 음량
> - 소리는 게임 시간과 상관없이 흐른다. 다이얼로그나 메뉴로 게임이 멈춰도 배경 음악은 계속 난다.
> - 다이얼로그가 자기 배경 음악을 틀었다면 끝날 때 전의 곡으로 돌아간다.
> - 음량은 DB › 설정의 항목(값 종류 숫자(슬라이더), 적용 대상 "브금 음량" 등)으로 만들고 기본 설정 화면이 보인다. `GameSettings.Set(id, 값)`이 기기에 저장하고 적용한다([10장](10-SaveAndRelease.md) 1절). 코드로 바로 바꿀 때는 `GameAudio.SetVolume(AudioChannel.Bgm, 값)`·`GameAudio.VolumeChanged`.

## 8. 인벤토리 칸

- 판 데이터의 `potions`는 인벤토리 모듈의 `Inventory`다(슬롯 수 `capacity`는 밸런싱 값). 슬롯(`InventorySlot`)에는 물약 원본 ID(`item`)와 개수(`count`)만 있다.
- 슬롯마다 칸을 그리고, 남은 슬롯(`capacity − slots.Count`)은 빈 슬롯으로 채운다. 칸의 버튼은 `DrinkPotion` 행동을 보낸다(2절).

```csharp
var potions = List<InventorySlot>("potions", Item("PotionSlot"), () => Game.Data().Run.potions.slots, (element, slot) =>
{
    element.Q<Label>("name").text = Texts.Get("hud.potion", ("potion", slot.item), ("count", slot.count));
    element.userData = slot.item;   // 슬롯에 id가 없어 칸은 자리 번호로 이어진다 — 누를 때 지금 값을 읽게 그릴 때마다 적는다
}, GameIds.PlayerData.Run.Id);
potions.OnCreate = (element, _) => OnClick(element.Q<Button>("drink"), () => new DrinkPotion { potion = (string)element.userData });
```

> [!note]- 마시기 한 번 = Undo 한 번
> 슬롯에서 하나 빼기와 물약의 사용 기능(회복)이 한 행동이라, Undo 한 번에 개수와 체력이 함께 돌아온다. 물약 아이콘이 필요하면 원본(`Rules.PotionOf(game, slot.item)`)에서 읽는다. 슬롯 옮기기(끌어 놓기)는 놓는 순간 행동 하나다(관례 21절).

## 9. 체크리스트

- [ ] 화면 코드는 Client 폴더에 있고, 플레이어 데이터를 읽기만 한다. UI는 `UiGuide.md`대로 DB › 화면 + UXML이다.
- [ ] 값만 보이는 칸은 `binding-path`, 목록은 `List<T>`, 그 밖은 `Draw`다. 씬 오브젝트는 `GameView.Redraw`·`ViewList`·데이터 연결이다.
- [ ] 화면 코드(`[BindTo(GameIds.Screen.X)]`)는 반영 스크립트로 DB › 화면 항목을 넣고 새로 읽어 상수가 생긴 뒤에 썼다.
- [ ] 받은 구독·`GameView.Redraw`는 `Own(…)`(화면 코드) 또는 `OnDisable`(MonoBehaviour)에서 정리한다.
- [ ] 버튼은 `OnClick(…, () => 행동)`, 버튼이 아닌 입력은 `GameScreens.Do`로 보낸다. 화면 코드에 `try`/`catch`·`Busy` 검사가 없다.
- [ ] 예상 피해·버튼 막기는 화면에서 따로 계산하지 않고 `Game.Preview`·`Game.CanDo`로, 그릴 때 한 번 부른다.
- [ ] 연출은 `SubscribeAfterAction`으로 받아 `GameView.Present`에 넣고, 기다리기는 `GameView.Wait`다.
- [ ] 연출의 무작위는 `UnityEngine.Random`, 규칙의 무작위는 `Game.Random`이다.
- [ ] 화면의 모든 글이 텍스트 키이고, DB 항목 이름은 `<ID>.name`이다. 행동의 실패 글은 `Game.Fail`의 키로 번역된다.
- [ ] 다이얼로그는 말·연출만, 규칙이 있는 일은 행동이 맡는다. 그래프는 개발자 몫이고 신호 이름·시작 ID를 알렸다.
- [ ] 판 데이터에는 원본 ID와 바뀌는 값만 있고, 이름·그림은 원본·텍스트에서 읽는다. 그림·소리 자리는 `AssetRequests`로 만들었다.
- [ ] `Play.request`로 화면을 둘러보고 찍은 그림을 UI 가이드와 맞춰 봤다.

> [!note]- 더 보기(매뉴얼)
> - [35. 화면](../../Manual/35-Screens.md) — DB › 화면, `binding-path`, `GameScreen`(`OnClick`·`List`·`Own`·`Draw`·`Tick`), 기본 화면, 예제(전투 HUD·상점·타이틀·피해 숫자)
> - [32. 화면 연결·연출 대기열·씬 흐름·월드 상태](../../Manual/32-View-Presentation-Scenes.md) — `GameView.Present`·`Busy`·`Find`, `ViewList`, 데이터 연결
> - [33. 고정 틱·미리보기·CSV](../../Manual/33-Tick-Preview-Csv.md) — `Game.Preview`·`CanDo`·`IsPreviewing`
> - [10. 이벤트](../../Manual/10-Events.md) — `Subscribe`·`SubscribeAfterAction`·`Dispose`, 받는 쪽 규칙
> - [01. 플레이어 데이터](../../Manual/01-PlayerData.md) — `Changed`·`DataChanged`·`Synced`, Sync가 일어나는 때
> - [30. 밸런싱 값·곡선·가중치 표·플레이어용 실패](../../Manual/30-Balance-Tables.md) — `GameFailure`·`Game.Fail`·`FailureKeys`
> - [07. 난수와 시간](../../Manual/07-Random-Time.md) — `GameTime`, 게임 시계
> - [23. 텍스트와 현지화](../../Manual/23-Text-Localization.md) — `Texts`, 문장 틀, `GameLanguage`
> - [24. 다이얼로그](../../Manual/24-Dialogue.md) — 그래프·신호·저장 이어 가기
> - [26. 오디오](../../Manual/26-Audio.md) — 채널·음량
> - [25. 인벤토리](../../Manual/25-Inventory.md) — 슬롯·더하기·빼기
> - [38. 테스트](../../Manual/38-Testing.md) — 화면 둘러보기(`Play.request`, 2-5)
