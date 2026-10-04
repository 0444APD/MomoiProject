# 09. 3요소를 지키는 습관

Momoi Project의 설계는 세 가지를 지킨다: **코딩이 쉬울 것**, **멀티플레이와 연동되고 패킷 성능이 좋을 것**, **게임 중에 리플렉션 같은 무거운 작업을 하지 않을 것**. 시스템이 길은 깔아 두었다. 게임 코드는 아래 습관으로 그 길에서 벗어나지 않는다. 하나를 어기면 Undo·저장·멀티플레이 중 어딘가가 조용히 어긋난다.

> [!note]- 이 장에서 쓰는 말
> | 말 | 뜻 |
> |---|---|
> | 플레이어 데이터 | 게임 중에 바뀌는 값을 담는 칸. 세션의 저장소(`session.Data`)에 ID별로 산다. 카드 던전은 `run`(판)과 `profile`(프로필) 두 개다. |
> | 행동(`GameAction`) | 플레이어 입력 하나를 담은 데이터 클래스. 행동 하나 = Undo 한 단위 = 네트워크로 보낼 한 묶음. |
> | Mirror · Sync | 시스템이 플레이어 데이터의 사본(Mirror)을 들고 있다가, 행동 끝·프레임 끝 같은 때에 지금 값과 비교해(Sync) 바뀐 곳만 기록한다. |
> | ChangeSet | Sync 한 번이 찾아낸 변경 묶음. 앞으로 기록(새 값)은 멀티플레이에서 다른 쪽으로 보내고, 뒤로 기록(옛 값)은 Undo에 쓴다. |
> | 생성 코드 | 컴파일할 때 `GBible.CodeGen`이 `[Serializable]`·`[GameEvent]` 형식 안에 넣는 비교·읽기·쓰기 메서드. 이것이 있으면 실행 중에 리플렉션을 쓰지 않는다. |
> | 예전 방식 | 생성 코드로 다룰 수 없는 데이터에 쓰는 느린 길(리플렉션으로 값 전체를 복제·비교). 동작은 하지만 기록 규칙이 다르고 느리다. |
> | 리플렉션 | 실행 중에 형식 정보를 읽어 필드를 찾고 값을 꺼내는 .NET 기능. 느리고 할당이 생긴다. Momoi Project는 게임이 켜질 때 한 번만 쓴다. |

---

## 1. 코딩 쉬움 — 그냥 C#을 쓴다

바뀐 곳은 Mirror가 비교로 찾는다. 게임 코드는 특별한 형식 없이 쓴다.

- 데이터는 평범한 `[Serializable]` 클래스, 목록은 `List`·배열·`Dictionary`.
- 값은 그냥 대입한다(`hero.hp -= 5;`). "바뀌었다"고 알리는 호출은 없다.
- 여러 곳에서 쓰는 규칙은 static 함수로 두고, `GameApi game`을 매개변수로 받는다.
- **규칙 함수에 가로채기 자리를 둔다.** 피해·회복·죽음·사용·비용·뽑기처럼 장르의 핵심 규칙 함수는 일을 하기 직전에 의도를 `Game.Intercept`로 통과시키고, 돌려받은 값으로 일을 한다.
- **비용은 `Game.Pay`로 낸다.** `Game.Pay(GameIds.PlayerData.Run.Id, "battle.energy", data.cost, what: "energy.name")`는 비용 가로채기를 이미 거친다(`what`은 모자랄 때 실패 글에 들어갈 이름의 텍스트 키). 직접 빼면(`battle.energy -= data.cost`) "이번 턴 비용 0" 같은 효과가 끼어들 자리가 없다.
- **플레이어에게 보일 실패는 `throw Game.Fail(키, 기본 문장)`.** 글은 텍스트 표로 번역된다. 없는 ID 같은 프로그래머 실수는 그냥 `InvalidOperationException`.
- **숫자에는 이름을 붙인다.** 기획자가 바꿀 숫자(시작 체력·보상 골드·층마다 커지는 값)는 코드에 적지 않고 DB › 밸런스 값에 두어 `Game.Balance(GameIds.Balance.X)`(곡선이면 `Game.Balance(id, 층)`)로 읽는다. 플레이 중에 고쳐도 바로 반영된다. 카드 던전의 `StartRun`은 예시를 짧게 하려고 체력 70·골드 99를 코드에 두었다. 실제 프로젝트에서는 밸런스 값으로 옮긴다([04장](04-AddingContent.md) 10절).

```csharp
public static void Damage(GameApi game, Fighter source, Fighter target, int amount)
{
    if (target == null || target.IsDead || amount <= 0) return;
    // 가로채기 자리: 유물·상태 효과가 피해를 바꾸거나 막을 수 있다(기믹이 늘어도 이 함수는 그대로).
    var intent = game.Intercept(new DamageIntent { sourceId = source?.id, targetId = target.id, amount = amount });
    if (intent.cancelled || intent.amount <= 0) return;
    amount = intent.amount;
    var blocked = Math.Min(target.block, amount);
    target.block -= blocked;
    var dealt = amount - blocked;
    target.hp -= dealt;                       // 그냥 대입: 행동 끝 Sync가 기록한다
    // … 이벤트를 쏘고, 쓰러지면 전투에서 뺀다
}
```

> [!note]- 이럴 때 이렇게
> | 이럴 때 | 이렇게 한다 | 하지 않는 것 |
> |---|---|---|
> | 여러 값을 묶는다 | 평범한 `[Serializable]` 클래스 | 시스템 기반 클래스를 상속한 데이터 |
> | 목록이 필요하다 | `List<T>`, 배열, `Dictionary<K, V>` | 래퍼 컬렉션을 따로 만들기 |
> | 값을 바꾼다 | `hero.hp -= 5;` 그대로 | 값을 바꿀 때마다 "바뀌었다"고 알리는 호출 |
> | 규칙이 여러 곳에서 쓰인다 | static 함수(카드 던전의 `Rules`)나 `GameApi` 확장 메서드 | 상태를 들고 있는 매니저 싱글턴 |
> | 에디터 표시를 바꾼다 | 어트리뷰트(`[Hide]`·`[ShowIf]`·`[Keyword]`·`[BlockInfo]`) | 커스텀 인스펙터 |
> | 핵심 규칙(피해·회복·죽음·비용·뽑기)을 쓴다 | 일을 하기 직전에 `Game.Intercept` 자리를 둔다 | 새 기믹이 생길 때마다 규칙 함수에 `if (hasThornArmor) …`를 더하기 |
> | 에너지·골드 같은 비용을 낸다 | `Game.Pay(키, 필드, 양)` | `if (energy < cost) throw …; energy -= cost;`(가로채기가 비용을 고칠 수 없다) |
> | 플레이어에게 "할 수 없다"를 알린다 | `throw Game.Fail("fail.notInHand", "손에 없는 카드입니다.")` | 한국어 글을 그대로 던지기(번역할 수 없다) |
> | 기획자가 바꿀 숫자 | DB › 밸런스 값 + `Game.Balance(GameIds.Balance.X)` | 코드에 `70`·`99`·`+15`를 적기(바꿀 때마다 프로그래머가 필요하다) |
> | 몇 턴 동안·몇 턴 뒤의 효과 | DB › 상태 효과 + 효과 `상태 효과 걸기`, 또는 예약(`Game.ScheduleOn`) | 데이터 클래스에 상태 목록을 두고 `EndTurn`에서 손으로 처리하기 |

> [!note]- 왜 가로채기 자리를 미리 두나
> 이벤트(`DamageDealt`)는 일이 일어난 **뒤**에 온다. "받는 피해 −5", "죽는 대신 체력 1", "이번 턴 비용 0"처럼 **일을 고치는 효과**는 일이 일어나기 **직전**에 끼어들어야 한다. 자리를 미리 뚫어 두면 이런 기믹을 기획 단계에서 미리 알 필요가 없다. 기획자가 DB에 유물을 더하고(트리거 `가로채기` + 결과 `의도 고치기`), 규칙 함수는 그대로다.
> - 의도는 프로젝트가 정하는 작은 클래스다. 막을 수 있게 하려면 `cancelled`를 둔다.
> ```csharp
> [Serializable, Intent("피해")]
> public class DamageIntent
> {
>     public string sourceId;
>     public string targetId;
>     public int amount;
>     public bool cancelled;
> }
> ```
> - 가로채는 항목이 없으면 `Game.Intercept`는 바로 돌아온다. 자리를 두는 비용은 거의 없다.
> - 행동 안에서 돌므로 Undo·저장·멀티플레이가 그대로다. 가로채는 쪽은 되도록 의도만 고친다(체력 같은 값은 규칙 함수가 바꾼다).
> - 원래 인자가 아니라 돌려받은 의도(`intent.amount`)로 일을 한다. 원래 값을 쓰면 가로채기가 무시된다.
> - 예: [04장](04-AddingContent.md) 9절 가시 갑옷. 자세히: 매뉴얼 [28장](../Manual/28-Attributes-Intercepts.md) 2-3·5장.

> [!note]- 왜 `game`을 매개변수로 받나
> 카드 던전의 `Rules.Damage`는 행동·Block·`[BindTo]` 함수가 모두 부른다. `game`을 받으므로 어느 세션에서 불려도 그 세션의 값만 만진다. 서버는 매치마다 세션을 만들므로, static 변수나 `GBibleRuntime.Game`에 기대면 다른 세션을 건드린다.

## 2. 멀티·패킷 — 값은 행동 안에서, 행동은 데이터로

멀티가 붙으면 클라이언트는 행동만 보내고, 서버가 행동을 실행해 생긴 ChangeSet을 모두에게 보낸다([11장](11-MultiplayerReady.md)). 싱글 게임에서도 지금 지켜 두면 나중에 고칠 것이 없다.

- **게임 값은 행동(`Game.Do`)·Block·결과·`[BindTo]` 함수 안에서만 바꾼다.**
- **행동에는 필요한 정보를 필드로 넣는다.** 대상(덱 카드·적)은 객체 그대로 넣는다. 시스템이 기록·전송에는 id만 남기고, `Run` 직전에 id로 지금 객체를 다시 찾아 넣는다.
- **대상 클래스는 `public string id;`가 있고 플레이어 데이터 안에 있다.** Unity 객체·델리게이트·데이터 밖 객체는 행동에 넣지 않는다.
- **이벤트를 받는 쪽은 화면·연출만 한다.** 값을 고치지 않는다.
- **규칙 난수는 `Game.Random("이름")`, 시간은 게임 시계(`session.Clock.Time`).**
- **세션은 매개변수로 받는다**(`c.Game`·`session`). 게임 값을 static 변수에 두지 않는다.
- **지연·지속 효과는 상태 효과로, 플레이어가 고르는 효과는 선택 대기로.** 행동을 멈추고 기다리지 말고, 나중의 일·고른 뒤의 일은 이어서 할 행동에 쓴다(매뉴얼 [29장](../Manual/29-Statuses-Schedules-Choices.md)).
- **연출은 `SubscribeAfterAction`으로 받아 `GameView.Present`에 넣는다.** 성공한 행동의 이벤트만 오고, 연출은 대기열이 차례로 튼다. 받는 쪽은 값을 바꾸지 않는다.
- **예상 값은 `Game.Preview`로 읽는다.** "이 카드를 내면 피해 얼마", "왜 못 내나"를 화면이 따로 계산하지 않는다. 행동을 해 보고 되돌리므로 결과를 정하는 곳이 규칙 한 곳뿐이다(`Game.CanDo`도 같다).

```csharp
[Serializable, BlockInfo("카드 내기")]
public class PlayCard : GameAction
{
    public DeckCard card;     // 낼 카드(덱 카드)
    public Fighter target;    // 고른 적(없으면 null)

    protected override void Run()
    {
        var run = Game.Data().Run;
        var data = Rules.CardData(Game, card.card);
        var battle = run.battle ?? throw Game.Fail("fail.notInBattle", "전투 중이 아닙니다.");
        if (!battle.hand.Contains(card.id)) throw Game.Fail("fail.notInHand", "손에 없는 카드입니다.");
        Game.Pay(GameIds.PlayerData.Run.Id, "battle.energy", data.cost, what: "energy.name");   // 비용: 가로채기('이번 턴 비용 0' 등)를 거친 뒤 모자라면 실패

        battle.hand.Remove(card.id);
        battle.discard.Add(card.id);

        if (target == null) Game.Use(card.card, run.hero);
        else Game.Use(card.card, run.hero, target);
        Game.Events.Publish(new CardPlayed { cardId = card.id, source = card.card, userId = run.hero.id, cost = data.cost });

        if (battle.enemies.Count == 0) Game.Do(new WinBattle());
    }
}
```

씬은 입력을 행동으로 바꿔 보내기만 한다: `Game.Do(new PlayCard { card = card, target = target })`. 화면은 데이터를 읽어 그린다.

> [!note]- 대상을 객체로 넣어도 되는 이유
> 행동은 저장·리플레이·서버 전송에 실리는 데이터다. 객체를 그대로 보낼 수는 없으므로, 시스템이 대상 필드(형식에 `string id`가 있는 클래스)를 기록·전송할 때 id만 남긴다. 실행 직전에는 그 id로 지금 데이터에서 객체를 다시 찾아 필드에 넣는다.
> - **싱글에서 방금 고른 객체**: 같은 객체가 그대로 쓰인다(확인만 한다).
> - **Undo·판 불러오기 뒤 버튼이 쥔 옛 객체**: 같은 id의 지금 객체로 바뀐다. 사라진 옛 객체를 고치는 일이 없다.
> - **멀티**: id만 건너가고 받는 쪽(서버)이 자기 데이터의 객체를 넣는다.
> - **타임라인·리플레이**: id로 남는다. `Describe`를 쓰지 않으면 "카드 내기 (card7, enemy2)"처럼 보인다.
> - **못 찾으면**(죽은 적 등) "Fighter 'enemy2'을(를) 찾을 수 없습니다"로 행동이 실패하고 아무것도 바뀌지 않는다.
> - DB 원본은 플레이어 데이터가 아니라서 찾기 대상이 아니다. 원본은 `[DataRef]` 원본 ID 글자로 넣는다(`BuyCard.source`).

> [!note]- 이럴 때 이렇게(이유)
> | 이럴 때 | 이렇게 한다 | 이유 |
> |---|---|---|
> | 게임 값을 바꾼다 | 행동(`Game.Do`)·Block·결과·`[BindTo]` 함수 안에서 | 행동 밖에서 바꾼 값은 Undo되지 않고, 멀티에서는 서버가 모르는 변경이 된다 |
> | 행동이 대상을 가리킨다 | 대상 객체를 필드에 그대로(`card`·`target`, 리스트도 된다) | 시스템이 id로 기록·전송하고 실행 직전에 지금 객체로 바꿔 넣는다 |
> | 행동이 값을 싣는다 | 숫자·글자·bool·enum, 원본 ID 글자, id 없는 작은 클래스(값으로 복사) | 행동은 데이터다. 스프라이트·MonoBehaviour·델리게이트는 실을 수 없다 |
> | 무언가를 오래 기억한다(선택한 카드 등) | 행동에 넣을 거라면 객체 그대로 둬도 된다. 화면이 직접 읽을 때는 `Game.Data()`·`Game.Find`로 다시 꺼낸다 | 불러오기 뒤에는 쥐고 있던 객체가 옛 인스턴스일 수 있다 |
> | 이벤트를 받아 무언가 한다 | 화면·연출만 | 받는 쪽에서 값을 바꾸면 Undo 단위와 서버 전송에서 빠진다 |
> | 규칙에 난수가 든다 | `Game.Random("이름")` | 시드·Undo·저장·서버와 맞는 결정론 난수. 연출용 흔들림은 `UnityEngine.Random` |
> | 화면에 예상 피해·"낼 수 없음"을 보인다 | `Game.Preview`·`Game.CanDo`(행동 밖에서, 그릴 때 한 번) | 화면이 피해 공식을 따로 가지면 규칙과 어긋나고, 멀티에서는 결과를 정하는 곳이 둘이 된다 |
> | 시간에 따라 바뀐다(쿨다운 등) | 게임 시계(`session.Clock.Time`) 기준 시각을 플레이어 데이터에 | `Time.time`은 되감기·저장과 맞지 않는다 |
> | 새 객체에 id가 필요하다 | `Game.NewId("접두어")` | 번호가 플레이어 데이터에 있어 Undo·저장·서버에서 같다 |
> | 여러 세션이 돌 수 있다 | 값은 플레이어 데이터에, 세션은 매개변수(`c.Game`·`session`)로 | 서버는 매치마다 세션을 만든다. static 변수는 모든 세션이 나눠 쓴다 |

> [!note]- 행동 안의 행동
> 행동 안에서 다른 행동(`Game.Do`)이나 능력(`Game.Use`)을 부르면 바깥 행동의 일부로 그 자리에서 실행된다(카드 내기 안의 `Game.Use`, 턴 끝 안의 `DrawCards`). Undo·ChangeSet은 바깥 행동 하나로 묶이고, 안쪽이 실패하면 바깥 전체가 취소된다. 그래서 "한 입력 = 한 묶음"이 유지된다.
>
> 공통 로직을 행동으로 만들지 함수로 만들지는 "혼자서도 입력이 될 수 있는가"로 정한다. 카드 뽑기는 개발 명령이나 다른 카드에서 혼자 실행될 수 있어 행동이고, 피해 계산은 입력이 아니라서 함수(`Rules.Damage`)다.

> [!note]- 이벤트를 받는 쪽 예
> 전투 화면은 피해 이벤트를 받아 숫자만 띄운다. 체력 바 값은 이벤트가 아니라 플레이어 데이터에서 읽는다(`GameView.Redraw`, [06장](06-Presentation.md) 1절).
> ```csharp
> _subscriptions.Add(session.Events.SubscribeAfterAction<DamageDealt>(e => GameView.Present(ShowDamage(e))));
> // OnDisable에서 Dispose
> ```
> 연출은 `SubscribeAfterAction`으로 받는다. 행동이 성공한 뒤에만 오므로, 실패해 취소된 행동의 피해 숫자가 뜨지 않는다. 받은 것은 연출 대기열에 넣어 차례로 튼다([06장](06-Presentation.md) 3·4절).
>
> `ShowDamage` 안에서 `hp`를 고치면 안 된다. 행동이 끝난 뒤의 변경이라 Undo 단위에서 빠지고, 서버가 모르는 변경이 된다.

> [!note]- 개발 명령도 행동으로
> 개발용 "골드 +100"도 값을 직접 바꾸지 않는다. DB › 개발 › 개발 명령의 결과 Block(플레이어 데이터에 주기)으로 조립하면, 누를 때 행동 하나(`DeveloperEditAction`)로 실행되어 모니터에서 Undo되고 덮어쓰기 경고도 없다. 규칙을 거쳐야 하는 명령(적 모두 처치)만 그 ID에 `[BindTo]` 코드를 붙인다([08장](08-TestAndDebug.md) 2절).
>
> 출시 빌드에서는 개발 명령이 저절로 빠지므로 `if (Debug.isDebugBuild)`로 감싸지 않는다.

> [!note]- 난수 흐름 나누기
> 카드 던전은 난수 흐름을 쓰임마다 나눈다: 덱 섞기 `"deck"`, 무작위 대상 `"cardTarget"`, 보상 골드 `"loot"`. 한쪽에서 뽑는 횟수가 바뀌어도(카드 효과를 고쳐 대상 뽑기가 한 번 늘어도) 다른 쪽 결과는 그대로라서, 고정 시드 재현이 덜 흔들린다.
> ```csharp
> var gold = Game.Random("loot").Range(10, 21);   // 10 이상 21 미만
> ```

## 3. 리플렉션 금지 — 생성 코드가 생기는 형식만 쓴다

조건의 필드 경로, 보상·로그가 읽는 이벤트 값, 저장·불러오기, `Game.Find`·`All`은 모두 생성 코드로 읽는다. 생성 코드는 `[Serializable]`(데이터)과 `[GameEvent]`(이벤트)가 붙은 형식에만 생긴다.

- 데이터 클래스는 그 클래스·부모·안쪽 클래스·리스트 요소 클래스 모두 `[Serializable]`.
- 이벤트 클래스는 `[GameEvent("표시 이름")]`.
- 컬렉션은 `List<T>`·`T[]`·`Dictionary<K, V>`(키는 글자·정수·enum).
- 텍스트 인자는 `Texts.Get("키", ("gold", 10))`.

붙이지 않아도 동작은 한다. 대신 리플렉션으로 읽고 한 번 경고하므로, 콘솔·타임라인의 "예전 방식(리플렉션)"·"읽기 코드가 없어 리플렉션으로 읽습니다" 경고를 놓치지 않는다.

> [!note]- 이럴 때 이렇게(피할 것)
> | 이럴 때 | 이렇게 한다 | 피하는 것(예전 방식이 됨) |
> |---|---|---|
> | 데이터 클래스 | 그 클래스, 안쪽 클래스, 부모 클래스, 리스트 요소 클래스 모두 `[Serializable]` | 부모에 `[Serializable]`이 없음(부모 필드가 경고 없이 기록에서 빠진다) |
> | 이벤트 | `[GameEvent("표시 이름")]` | 어트리뷰트 없는 이벤트 클래스 |
> | 컬렉션 | `List<T>`, `T[]`, `Dictionary<K, V>`(키는 글자·정수·enum) | `HashSet`·`Queue`·`Stack`·`LinkedList`, `List<List<int>>` 같은 중첩 컬렉션, `float` 키 딕셔너리 |
> | 플레이어 데이터 하나의 값 | 클래스(`RunState`) | 값 자체가 `List<T>`(클래스 하나로 감싼다) |
> | 텍스트에 값 넣기 | `Texts.Get("키", ("gold", 10))` | `Texts.Get("키", new { gold = 10 })`(익명 객체는 리플렉션으로 읽는다) |
> | 이벤트를 코드로 만든다 | `new CardPlayed { … }` | `Activator.CreateInstance`·`PublishObject`(도구·콘솔 명령에서만) |
> | 객체의 id를 읽는다 | 형식을 알면 `card.id` | 게임 루프에서 `GameApi.IdOf`를 반복(형식마다 처음 한 번 리플렉션으로 멤버를 찾는다) |

> [!note]- 리플렉션이 남아 있는 곳
> 게임이 켜질 때(코드 모으기, 사용 기능 찾기), 개발 도구(모니터의 이벤트 호출, 시뮬레이션), 선택 패키지 연결. 모두 게임 진행 중에 반복되는 경로가 아니다.

> [!note]- 카드 던전의 클래스 점검
> 판·프로필 데이터 클래스(`Fighter`·`Hero`·`Enemy`·`DeckCard`·`Battle`·`RunState`·`Profile`)는 모두 `[Serializable]`이고, `Hero`·`Enemy`의 부모 `Fighter`에도 붙어 있다. 손패·뽑을 더미·버린 더미는 카드 객체가 아니라 카드 id 리스트라서, 카드가 더미 사이를 옮겨 다녀도 변경분은 id 글자 몇 개다.
> ```csharp
> [Serializable]
> public class Battle
> {
>     public int turn;
>     public int energy;
>     public List<Enemy> enemies = new List<Enemy>();
>     public List<string> drawPile = new List<string>();   // 덱 카드(DeckCard) id
>     public List<string> hand = new List<string>();
>     public List<string> discard = new List<string>();
>     public int damageThisTurn;
> }
> ```

## 4. 판 데이터에는 ID와 바뀌는 값만

기준은 하나다: **"이 값이 판 중에 그 객체마다 따로 바뀔 수 있는가?"** 그렇다면 판 데이터, 아니라면 원본(DB › 인스턴스)에서 읽는다.

카드 던전의 덱 한 장은 원본 ID와 강화 여부만 든다. 이름·비용·그림·사용 기능은 원본에서 읽는다.

```csharp
[Serializable]
public class DeckCard
{
    public string id;                                           // 장마다 다른 id(Game.NewId("card"))
    [DataRef(typeof(Card))] public string card;         // 원본 인스턴스 ID(능력 이름으로도 쓴다)
    public bool upgraded;
}

// 원본은 규칙 함수 한 줄로 읽는다(읽기만 한다).
public static Card CardData(GameApi game, string card) => game.Session.Instances?.Invoke(card) as Card;
```

화면도 같다: 손패의 그림은 `Rules.CardData(Game, deckCard.card).art`.

> [!note]- 원본을 통째로 복사하면
> 원본 `Card`를 덱에 복사하면 이름·비용·그림(`art`)·사용 기능(`usable`)까지 장마다 들어간다.
> - **크기**: 같은 카드 다섯 장이면 고정 값이 다섯 벌이다. Sync·판 저장·멀티의 첫 Snapshot이 그만큼 커진다.
> - **저장**: 스프라이트는 DB 항목(`GameData`)이 아니라서 판 저장에 들어가지 않는다. 저장마다 "저장할 수 없는 참조입니다" 경고가 나고, 이어 한 뒤에는 그림이 비어 있다.
> - **원본 수정**: 기획자가 비용을 고쳐도 이미 복사된 장은 예전 값 그대로다.

> [!note]- 원본 자세히
> - `session.Instances`는 인스턴스 ID로 원본을 복사 없이 주는 함수다. 게임 중에는 읽기만 한다.
> - 몬스터도 같다. 전투 중의 적(`Enemy`)은 원본 ID(`monster`)와 체력·공격 차례(`intentIndex`)처럼 바뀌는 값만 들고, 공격 순서 같은 고정 값은 `Rules.MonsterOf`로 원본 `MonsterData`에서 읽는다.
> - 다른 값으로 계산되는 값(합산 공격력 등)도 저장하지 않고 읽을 때 계산한다(속성·함수).

## 5. 하지 말 것

앞 절을 어기는 흔한 실수다.

- MonoBehaviour 필드에 게임 값(골드·체력·덱)을 둔다 → 플레이어 데이터에 둔다.
- 씬 코드·코루틴·이벤트를 받는 함수에서 플레이어 데이터를 고친다 → 행동을 만들어 `Game.Do`.
- 행동 안에서 기다린다(코루틴·`await`) → 결과를 이벤트로 쏘고 연출은 따로.
- 실패를 조용한 `return`으로 처리한다 → 예외를 던진다(플레이어에게 보일 실패는 `throw Game.Fail(…)`).
- 비용을 직접 뺀다 → `Game.Pay`.
- Block·결과·`Started` 코드에서 `GBibleRuntime.Game`을 쓴다 → 받은 세션을 쓴다.

> [!note]- 하지 말 것 전체와 이유
> | 하지 말 것 | 생기는 일 | 대신 |
> |---|---|---|
> | MonoBehaviour 필드에 게임 값(골드·체력·덱)을 둔다 | Undo·저장·동기화에서 빠진다 | 플레이어 데이터에 둔다 |
> | 씬 코드·코루틴·이벤트를 받는 함수에서 플레이어 데이터를 고친다 | "행동 밖 변경"으로 기록되고 Undo되지 않는다. 그 행동을 Undo하면 "행동 밖에서 바뀐 값을 덮었습니다" 경고 | 행동을 만들어 `Game.Do` |
> | 행동 안에서 기다린다(코루틴·`await`) | 행동은 한 번에 끝나야 한다 | 결과를 이벤트로 쏘고 연출은 따로. 몇 턴·몇 초 뒤의 효과는 상태 효과·예약으로, 플레이어의 고르기는 `Game.Ask`(이어서 할 행동이 따로 돈다)로 |
> | 행동 필드에 Unity 객체·델리게이트·데이터 밖 객체를 둔다 | id로 바꿀 수 없어 저장·리플레이·서버 전송이 안 된다 | 대상은 데이터 안의 id 있는 객체, 원본은 원본 ID |
> | 쥐고 있던 객체를 화면이 그대로 읽는다 | 불러오기 뒤에는 옛 인스턴스를 읽는다 | 그릴 때 `Game.Data()`·`Game.Find`로 다시 꺼낸다(행동에 넣는 것은 괜찮다) |
> | 규칙 난수에 `UnityEngine.Random` | Undo로 다시 뽑기가 되고, 시드 재현·서버 판정이 깨진다 | `Game.Random("이름")` |
> | 연출 난수에 `Game.Random` | Undo가 모든 난수 흐름을 행동 직전으로 되돌리므로 연출 소비까지 되감기고, 규칙 결과가 연출 때문에 달라진다 | `UnityEngine.Random` |
> | `[BindTo]` 함수·Block 안에 static 변수로 상태를 둔다 | 세션이 여러 개면 섞인다 | 플레이어 데이터 |
> | Block·결과·`Started` 코드에서 `GBibleRuntime.Game`을 쓴다 | 서버·시뮬레이션의 다른 세션에서 틀린 세션을 만진다 | Block의 `Game` 속성, 컨텍스트의 `c.Game`, 매개변수 `session` |
> | 실패를 조용한 `return`으로 처리한다 | "아무것도 바뀌지 않은 행동"이 Undo 이력에 한 칸 남는다 | 예외를 던진다(바뀐 것 전부 취소, 부른 쪽이 잡는다) |
> | 매 프레임 바뀌는 위치·속도를 플레이어 데이터에 | 매 프레임 기록·ChangeSet이 쌓인다 | MonoBehaviour 쪽에 두고, 규칙에 필요한 칸 좌표만 데이터로 |
> | 원본(인스턴스)을 통째로 복사해 판 데이터에 넣는다 | Sync·저장·전송이 커지고, 스프라이트는 저장되지 않으며, 원본을 고쳐도 반영되지 않는다 | 판 데이터에는 원본 ID와 바뀌는 값만(4절) |
> | 다른 값으로 계산되는 값(합산 공격력)을 저장 | 두 값이 어긋날 수 있고 데이터가 커진다 | 읽을 때 계산(속성·함수) |
> | 비용을 직접 뺀다(`energy -= cost`) | 비용을 고치는 가로채기(유물·상태 효과)가 무시된다 | `Game.Pay` |
> | 연출을 손으로 만든 대기열·`WaitForSeconds`로 튼다 | `Redraw`가 연출을 기다리지 못하고, 연출 속도·건너뛰기·`Busy`가 먹지 않는다 | `GameView.Present` + `GameView.Wait` |

## 6. 데이터 크기·성능 기준

턴제·카드 게임은 크기를 거의 신경 쓰지 않아도 된다(카드 던전의 판 데이터는 객체 수백 개). 큰 장르만 아래를 지킨다.

- 객체가 수만 개를 넘으면 자주 바뀌는 부분과 거의 안 바뀌는 부분을 다른 플레이어 데이터로 나눈다.
- 화면을 그릴 때 `Game.Find`를 객체마다 반복하지 않는다. `Game.All`을 프레임에 한 번 부르고 재사용한다.
- Undo 보관 수를 정한다(DB › 시작 파일 › Bootstrap). 0(제한 없음)이면 행동마다 옛 값이 계속 쌓인다. 카드 던전은 200이다.

> [!note]- 측정값
> 행동 한 번의 Sync(매뉴얼 측정):
>
> | 데이터 크기 | .NET | Unity 에디터(Mono) |
> |---|---|---|
> | 객체 약 2,500개(유닛 500) | 약 0.02ms | 약 0.06ms |
> | 객체 약 10만 개 | 약 0.9ms | 약 2.5ms |
> | 바뀐 것이 없을 때 | 할당 0 | 할당 0 |
>
> 멀티 전송량의 감: 작은 행동 하나의 변경분은 수십 바이트, 50유닛 광역기는 약 1.8KB다. 처음 접속한 쪽에 보낼 전체 모습(Snapshot)은 2만 유닛 기준 약 1.6MB다.

> [!note]- 더 가볍게
> - 예전 방식 데이터는 수정할 때마다 값 전체를 복제·비교한다. 요소가 2만 개를 넘으면 "값이 큽니다" 경고가 난다. 보이면 `[Serializable]`부터 확인한다.
> - 변경 기록 문장이 필요 없는 서버·시뮬레이션은 `session.Data.DescribeChanges = false`로 Sync를 더 가볍게 할 수 있다. 대신 모니터 기록의 변경 목록과 Undo 덮어쓰기 경고가 사라진다.

## 7. 체크리스트

- [ ] 모든 데이터 클래스와 그 부모·안쪽·요소 클래스에 `[Serializable]`이 있다.
- [ ] 모든 이벤트 클래스에 `[GameEvent]`가 있다.
- [ ] `HashSet`·`Queue`·중첩 컬렉션·컬렉션 그대로의 플레이어 데이터 값이 없다.
- [ ] 플레이어 데이터를 바꾸는 코드는 모두 행동·Block·결과·`[BindTo]` 안에 있다.
- [ ] 핵심 규칙 함수(피해·회복·죽음·비용 …)에 가로채기 자리(`Game.Intercept`)가 있고, 비용은 `Game.Pay`로 낸다.
- [ ] 플레이어에게 보일 실패는 `Game.Fail`(키 + 기본 문장)이고, 기획자가 바꿀 숫자는 밸런스 값이다.
- [ ] 행동의 대상 필드는 id가 있는 플레이어 데이터 객체이고, Unity 객체·델리게이트가 없다.
- [ ] 판 데이터에는 원본 ID와 바뀌는 값만 있고, 고정 값(이름·비용·그림·사용 기능)은 원본에서 읽는다.
- [ ] 이벤트를 받는 씬 코드는 연출·UI만 하고(`SubscribeAfterAction` → `GameView.Present`), `OnDisable`에서 `Dispose`한다.
- [ ] 예상 값은 화면이 계산하지 않고 `Game.Preview`·`Game.CanDo`로 읽는다.
- [ ] 규칙 난수는 `Game.Random`, 연출 난수는 `UnityEngine.Random`이다. 시간 규칙은 게임 시계 기준이다.
- [ ] 게임 값을 담은 static 변수가 없고, Block·결과 코드는 `GBibleRuntime` 대신 받은 세션을 쓴다.
- [ ] 텍스트 인자는 `("이름", 값)` 형태다.
- [ ] 콘솔·타임라인에 "예전 방식(리플렉션)", "읽기 코드가 없어 리플렉션으로 읽습니다", "저장할 수 없는 참조입니다" 경고가 없다(있다면 이유를 안다).
- [ ] DB › 시작 파일 › Bootstrap에서 Undo 보관 수를 정했다.

> [!note]- 다른 장르라면
> - **실시간 액션**: 위치·속도는 매 프레임 MonoBehaviour에서 움직이고, 규칙에 필요한 값(체력·쿨다운 시각·현재 칸)만 플레이어 데이터에 둔다. 입력은 여전히 행동이지만 Undo는 스냅샷 되감기(Bootstrap의 Undo 방식 "스냅샷")다. 쿨다운은 `Time.time`이 아니라 `session.Clock.Time` 기준 시각으로 적어야 되감을 때 함께 돌아간다.
> - **시뮬레이션·방치형(유닛 수천 개)**: 객체 수가 성능을 정한다. 계산으로 얻을 수 있는 값(누적 생산량 등)은 저장하지 않고, 자주 바뀌는 묶음을 별도 플레이어 데이터로 나눈다.

> [!note]- 더 보기(매뉴얼)
> - [01. 플레이어 데이터](../Manual/01-PlayerData.md) — 기록되는 필드, 지원 형식, Sync 시점, 예전 방식
> - [02. 행동과 Undo](../Manual/02-Actions-Undo.md) — 행동 밖 변경, 지연 효과, 실패와 취소
> - [03. Game API](../Manual/03-GameApi.md) — `Find`·`All`의 비용, `NewId`, 확장 메서드
> - [07. 난수와 시간](../Manual/07-Random-Time.md) — 난수 흐름, 게임 시계, Undo·저장과 함께 움직이는 방식
> - [10. 이벤트](../Manual/10-Events.md) — 쏘기·받기 규칙
> - [28. 능력치·Modifier·가로채기·비용](../Manual/28-Attributes-Intercepts.md) — 가로채기 자리, `Game.Pay`
> - [30. 밸런스 값·곡선·가중치 표·플레이어용 실패](../Manual/30-Balance-Tables.md) — `Game.Balance`, `Game.Fail`
> - [32. 화면 연결·연출 대기열·씬 흐름·월드 상태](../Manual/32-View-Presentation-Scenes.md) — `GameView.Present`
> - [33. 고정 틱·미리보기·CSV](../Manual/33-Tick-Preview-Csv.md) — `Game.Preview`
> - [../Coding.md](../Coding.md) — 게임 코드 규칙과 Mirror 비용
