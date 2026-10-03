# 09. 3요소를 지키는 습관

## 1. 이 단계의 목표

Momoi Project의 모든 설계는 세 가지를 지킨다: **코딩이 쉬울 것**, **멀티플레이와 연동되고 패킷 성능이 좋을 것**, **게임 중에 리플렉션 같은 무거운 작업을 하지 않을 것**. 시스템이 이 셋을 지켜 주는 길은 이미 깔려 있다. 이 장의 목표는 게임 코드가 그 길에서 벗어나지 않는 습관을 손에 붙이는 것이다. 습관 하나하나는 사소하지만, 하나를 어기면 Undo·저장·멀티플레이 중 어딘가가 조용히 어긋난다.

이 장에서 쓰는 말:

| 말 | 뜻 |
|---|---|
| 플레이어 데이터 | 게임 중에 바뀌는 값을 담는 칸. 세션의 저장소(`session.Data`)에 ID별로 산다. 카드 던전에서는 `run`(판)과 `profile`(프로필) 두 개다. |
| 행동 (`GameAction`) | 플레이어 입력 하나를 담은 데이터 클래스. 행동 하나 = Undo 한 단위 = 네트워크로 보낼 한 묶음. |
| Mirror · Sync | 시스템이 플레이어 데이터의 사본(Mirror)을 들고 있다가, 행동 끝·프레임 끝 같은 때에 지금 값과 비교해(Sync) 바뀐 곳만 기록한다. |
| ChangeSet | Sync 한 번이 찾아낸 변경 묶음. 앞으로 기록(새 값)은 멀티플레이에서 다른 쪽으로 보내고, 뒤로 기록(옛 값)은 Undo에 쓴다. |
| 생성 코드 | 컴파일할 때 `GBible.CodeGen`이 `[Serializable]`·`[GameEvent]` 형식 안에 넣는 비교·읽기·쓰기 메서드. 이것이 있으면 시스템이 실행 중에 리플렉션을 쓰지 않는다. |
| 예전 방식 | 생성 코드로 다룰 수 없는 데이터에 쓰는 느린 길(리플렉션으로 값 전체를 복제·비교). 동작은 하지만 기록 규칙이 다르고 느리다. |
| 리플렉션 | 실행 중에 형식 정보를 읽어 필드를 찾고 값을 꺼내는 .NET 기능. 느리고 할당이 생긴다. Momoi Project는 게임이 켜질 때 한 번만 쓴다. |

---

## 2. 할 일 순서

1. 새 데이터 클래스를 만들 때마다 `[Serializable]`을 붙였는지, 안쪽·부모 클래스까지 붙였는지 본다.
2. 값을 바꾸는 코드를 쓸 때마다 "이 코드는 행동 안에서 도는가"를 묻는다. 아니면 행동으로 옮긴다.
3. 행동 클래스의 필드를 볼 때마다 "id와 숫자·글자만 있는가"를 본다.
4. 이벤트를 받는 코드(씬의 `Subscribe`)가 플레이어 데이터를 고치지 않는지 본다.
5. 난수는 `Game.Random`, 시간은 게임 시계로 쓰고 있는지 본다.
6. static 변수에 게임 값을 두지 않았는지 본다.
7. 콘솔과 모니터 타임라인에 "예전 방식(리플렉션)" "읽기 코드가 없어 리플렉션으로 읽습니다" 경고가 없는지 본다.
8. 데이터가 커질 장르라면 아래 "데이터 크기·성능 기준"으로 한 번 어림한다.

---

## 3. 판단 기준

### 첫째, 코딩 쉬움 — 그냥 C#을 쓴다

시스템은 게임 코드에 특별한 형식을 요구하지 않는다. 바뀐 곳을 찾는 일은 Mirror가 비교로 해 주므로, 게임 코드는 필드에 그냥 대입하고 `List`·`Dictionary`를 그냥 고친다.

| 이럴 때 | 이렇게 한다 | 하지 않는 것 |
|---|---|---|
| 여러 값을 묶는다 | 평범한 `[Serializable]` 클래스 | 시스템 기반 클래스를 상속한 데이터 |
| 목록이 필요하다 | `List<T>`, 배열, `Dictionary<K, V>` | 래퍼 컬렉션을 따로 만들기 |
| 값을 바꾼다 | `hero.hp -= 5;` 그대로 | 값을 바꿀 때마다 "바뀌었다"고 알리는 호출 |
| 규칙이 여러 곳에서 쓰인다 | static 함수(카드 던전의 `Rules`)나 `GameApi` 확장 메서드 | 상태를 들고 있는 매니저 싱글턴 |
| 에디터 표시를 바꾼다 | 어트리뷰트(`[Hide]`·`[ShowIf]`·`[Keyword]`·`[BlockInfo]`) | 커스텀 인스펙터 |

카드 던전의 피해 규칙은 평범한 static 함수다. 행동·Block·`[BindTo]` 함수가 모두 같은 함수를 부른다.

```csharp
public static void Damage(GameApi game, Fighter source, Fighter target, int amount)
{
    if (target == null || target.IsDead || amount <= 0) return;
    var blocked = Math.Min(target.block, amount);
    target.block -= blocked;
    var dealt = amount - blocked;
    target.hp -= dealt;                       // 그냥 대입: 행동 끝 Sync가 기록한다
    // … 이벤트를 쏘고, 쓰러지면 전투에서 뺀다
}
```

`game`을 매개변수로 받는 것이 요점이다. 이 함수는 어느 세션에서 불려도 그 세션의 값만 만진다(아래 "static 상태" 참고).

### 둘째, 멀티플레이·패킷 — 값은 행동 안에서, 행동은 데이터로

멀티플레이가 붙으면 클라이언트는 행동만 서버로 보내고, 서버가 행동을 실행해 생긴 ChangeSet을 모두에게 보낸다(→ [11장](11-MultiplayerReady.md)). 그래서 지금 싱글 게임에서도 다음을 지켜 두면 나중에 고칠 것이 없다.

| 이럴 때 | 이렇게 한다 | 이유 |
|---|---|---|
| 게임 값을 바꾼다 | 행동(`Game.Do`)·Block·결과·`[BindTo]` 함수 안에서 | 행동 밖에서 바꾼 값은 Undo되지 않고, 멀티플레이에서는 서버가 모르는 변경이 된다 |
| 행동이 대상을 가리킨다 | `target`·`targetId` 같은 **id 글자** | 객체 참조는 저장·전송·리플레이가 안 된다 |
| 행동이 값을 싣는다 | 숫자·글자·bool·enum·id 리스트 | 행동은 데이터다. 스프라이트·MonoBehaviour·델리게이트는 실을 수 없다 |
| 무언가를 오래 기억한다(선택한 카드 등) | id로 들고, 쓸 때 `Game.Find`로 다시 찾는다 | Undo·불러오기·받는 쪽에서는 다른 인스턴스일 수 있다 |
| 이벤트를 받아 무언가 한다 | 화면·연출만 | 받는 쪽에서 값을 바꾸면 Undo 단위와 서버 전송에서 빠진다 |
| 규칙에 난수가 든다 | `Game.Random("이름")` | 시드·Undo·저장·서버와 맞는 결정론 난수. 연출용 흔들림은 `UnityEngine.Random` |
| 시간에 따라 바뀐다(쿨다운 등) | 게임 시계(`session.Clock.Time`) 기준 시각을 플레이어 데이터에 | `Time.time`은 되감기·저장과 맞지 않는다 |
| 새 객체에 id가 필요하다 | `Game.NewId("접두어")` | 번호가 플레이어 데이터에 있어 Undo·저장·서버에서 같다 |
| 여러 세션이 돌 수 있다 | 값은 플레이어 데이터에, 세션은 매개변수(`c.Game`·`session`)로 | 서버는 매치마다 세션을 만든다. static 변수는 모든 세션이 나눠 쓴다 |

카드 던전의 카드 내기 행동은 이 규칙을 그대로 보여 준다. 필드는 덱 카드 id(`target`, 물려받은 필드)와 고른 적의 id(`targetId`)뿐이고, 에너지 지불·손패 이동·효과·이벤트가 모두 행동 안에서 일어난다. 비용 같은 고정 값은 카드 원본에서 읽는다(아래 "판 데이터에는 ID와 바뀌는 값만").

```csharp
[Serializable, BlockInfo("카드 내기")]
public class PlayCard : GameAction<DeckCard>
{
    public string targetId;                          // 고른 적: 객체가 아니라 id

    protected override void Run(DeckCard card)       // card = Game.Find<DeckCard>(target)
    {
        var run = Game.Data().Run;
        var data = Rules.CardData(Game, card.card);  // 카드 원본(읽기만)
        var battle = run.battle ?? throw new InvalidOperationException("전투 중이 아닙니다.");
        if (!battle.hand.Contains(card.id)) throw new InvalidOperationException("손에 없는 카드입니다.");
        if (battle.energy < data.cost) throw new InvalidOperationException("에너지가 모자랍니다.");

        battle.energy -= data.cost;
        battle.hand.Remove(card.id);
        battle.discard.Add(card.id);
        // … Game.Use(card.card, …)로 효과, Game.Events.Publish(new CardPlayed { … })로 이벤트
    }
}
```

씬 쪽은 입력을 행동으로 바꿔 보내기만 한다(`Game.Do(new PlayCard { target = cardId, targetId = targetId })`). 화면은 데이터를 읽어 그린다.

**행동 안의 행동.** 행동 안에서 다른 행동(`Game.Do`)이나 능력(`Game.Use`)을 부르면 바깥 행동의 일부로 그 자리에서 실행된다. 카드 내기 안의 `Game.Use`, 턴 끝 안의 카드 뽑기(`DrawCards`)가 그렇다. Undo·ChangeSet은 바깥 행동 하나로 묶이고, 안쪽이 실패하면 바깥 전체가 취소된다. 그래서 "한 입력 = 한 묶음"이 유지된다. 공통 로직을 행동으로 만들지 함수로 만들지는 "혼자서도 입력이 될 수 있는가"로 정한다. 카드 뽑기는 디버그 명령이나 다른 카드에서 혼자 실행될 수 있어 행동으로 두었고, 피해 계산은 입력이 아니라서 함수(`Rules.Damage`)다.

### 셋째, 실행 중 리플렉션 금지 — 생성 코드가 생기는 형식만 쓴다

시스템은 조건의 필드 경로, '플레이어 데이터에 주기', '로그에 기록'의 이벤트 값, 저장·불러오기, `Game.Find`·`All`을 모두 생성 코드로 읽는다. 생성 코드는 `[Serializable]`(데이터)과 `[GameEvent]`(이벤트)가 붙은 형식에만 생긴다. 붙이지 않으면 시스템이 리플렉션으로 대신 읽고 한 번 경고한다. 동작은 하므로 놓치기 쉽다.

| 이럴 때 | 이렇게 한다 | 피하는 것(예전 방식이 됨) |
|---|---|---|
| 데이터 클래스 | 그 클래스, 안쪽 클래스, 부모 클래스, 리스트 요소 클래스 모두 `[Serializable]` | 부모에 `[Serializable]`이 없음(부모 필드가 경고 없이 기록에서 빠진다) |
| 이벤트 | `[GameEvent("표시 이름")]` | 어트리뷰트 없는 이벤트 클래스 |
| 컬렉션 | `List<T>`, `T[]`, `Dictionary<K, V>`(키는 글자·정수·enum) | `HashSet`·`Queue`·`Stack`·`LinkedList`, `List<List<int>>` 같은 중첩 컬렉션, `float` 키 딕셔너리 |
| 플레이어 데이터 하나의 값 | 클래스(`RunState`) | 값 자체가 `List<T>`(클래스 하나로 감싼다) |
| 텍스트에 값 넣기 | `Texts.Get("키", ("gold", 10))` | `Texts.Get("키", new { gold = 10 })`(익명 객체는 리플렉션으로 읽는다) |
| 이벤트를 코드로 만든다 | `new CardPlayed { … }` | `Activator.CreateInstance`·`PublishObject`(도구·콘솔 명령에서만) |
| 객체의 id를 읽는다 | 형식을 알면 `card.id` | 게임 루프에서 `GameApi.IdOf`를 반복(형식마다 처음 한 번 리플렉션으로 멤버를 찾는다) |

리플렉션을 쓰는 곳이 남아 있기는 하다: 게임이 켜질 때(코드 모으기, 사용 기능 찾기), 개발 도구(모니터의 이벤트 호출, 시뮬레이션), 선택 패키지 연결. 모두 게임 진행 중의 반복 경로가 아니다.

---

## 4. 하지 말 것 목록과 이유

| 하지 말 것 | 생기는 일 | 대신 |
|---|---|---|
| MonoBehaviour 필드에 게임 값(골드·체력·덱)을 둔다 | Undo·저장·동기화에서 빠진다 | 플레이어 데이터에 둔다 |
| 씬 코드·코루틴·이벤트를 받는 함수에서 플레이어 데이터를 고친다 | "행동 밖 변경"으로 기록되고 Undo되지 않는다. 그 행동을 Undo하면 "행동 밖에서 바뀐 값을 덮었습니다" 경고 | 행동을 만들어 `Game.Do` |
| 행동 안에서 기다린다(코루틴·`await`) | 행동은 한 번에 끝나야 한다 | 결과를 이벤트로 쏘고 연출은 따로. 시간이 걸리는 효과는 나중에 별도 행동으로 |
| 행동 필드에 객체 참조를 둔다 | 저장·리플레이·서버 전송이 안 된다 | id |
| 객체를 오래 쥐고 있는다 | 불러오기·받는 쪽에서 엉뚱한 인스턴스 | id로 쥐고 `Game.Find` |
| 규칙 난수에 `UnityEngine.Random` | Undo로 다시 뽑기가 되고, 시드 재현·서버 판정이 깨진다 | `Game.Random("이름")` |
| 연출 난수에 `Game.Random` | Undo가 모든 난수 흐름을 행동 직전으로 되돌리므로 연출 소비까지 되감기고, 규칙 결과가 연출 때문에 달라진다 | `UnityEngine.Random` |
| `[BindTo]` 함수·Block 안에 static 변수로 상태를 둔다 | 세션이 여러 개면 섞인다 | 플레이어 데이터 |
| Block·결과·`Started` 코드에서 `GBibleRuntime.Game`을 쓴다 | 서버·시뮬레이션의 다른 세션에서 틀린 세션을 만진다 | Block의 `Game` 속성, 컨텍스트의 `c.Game`, 매개변수 `session` |
| 실패를 조용한 `return`으로 처리한다 | "아무것도 바뀌지 않은 행동"이 Undo 이력에 한 칸 남는다 | 예외를 던진다(바뀐 것 전부 취소, 부른 쪽이 잡는다) |
| 매 프레임 바뀌는 위치·속도를 플레이어 데이터에 | 매 프레임 기록·ChangeSet이 쌓인다 | MonoBehaviour 쪽에 두고, 규칙에 필요한 칸 좌표만 데이터로 |
| 원본(인스턴스)을 통째로 복사해 판 데이터에 넣는다 | 고정 값이 장마다 복제되어 Sync·저장·전송이 커지고, 스프라이트는 저장되지 않으며, 원본을 고쳐도 반영되지 않는다 | 판 데이터에는 원본 ID와 바뀌는 값만, 고정 값은 원본에서 읽는다 |
| 다른 값으로 계산되는 값(합산 공격력)을 저장 | 두 값이 어긋날 수 있고 데이터가 커진다 | 읽을 때 계산(속성·함수) |

---

## 5. 데이터 크기·성능 기준

Sync 비용은 데이터 크기에 비례하지만 생성 코드라 가볍다. 매뉴얼의 측정값(행동 한 번의 Sync):

| 데이터 크기 | .NET | Unity 에디터(Mono) |
|---|---|---|
| 객체 약 2,500개(유닛 500) | 약 0.02ms | 약 0.06ms |
| 객체 약 10만 개 | 약 0.9ms | 약 2.5ms |
| 바뀐 것이 없을 때 | 할당 0 | 할당 0 |

멀티플레이 전송량의 감: 작은 행동 하나의 변경분은 수십 바이트, 50유닛 광역기는 약 1.8KB다. 처음 접속한 쪽에 보낼 전체 모습(Snapshot)은 2만 유닛 기준 약 1.6MB다.

이 숫자에서 나오는 기준:

- 턴제·카드 게임은 크기를 거의 신경 쓰지 않아도 된다. 카드 던전의 판 데이터는 객체 수백 개 수준이다.
- 객체가 수만 개를 넘는 장르(대규모 시뮬레이션)는 자주 바뀌는 부분과 거의 안 바뀌는 부분을 다른 플레이어 데이터 키로 나누는 것을 검토한다.
- 예전 방식 데이터는 수정할 때마다 값 전체를 복제·비교한다. 요소가 2만 개를 넘으면 "값이 큽니다" 경고가 난다. 이 경고가 보이면 `[Serializable]`부터 확인한다.
- 화면을 그릴 때 `Game.Find`를 객체마다 반복하지 않는다. `Game.All`을 프레임에 한 번 부르고 결과를 재사용한다.
- `HistoryLimit`(Undo할 수 있는 최대 행동 수)을 정한다. `0`(무제한)이면 행동마다 바뀐 값의 옛 모습이 계속 쌓인다. 카드 던전은 200이다.
- 변경 기록 문장이 필요 없는 서버·시뮬레이션은 `session.Data.DescribeChanges = false`로 Sync를 더 가볍게 할 수 있다(대신 모니터 기록의 변경 목록과 Undo 덮어쓰기 경고가 사라진다).

---

## 6. 카드 던전에서 실제로

### 클래스 점검

카드 던전의 판·프로필 데이터 클래스(`Fighter`·`Hero`·`Enemy`·`DeckCard`·`Battle`·`RunState`·`Profile`)는 모두 `[Serializable]`이고, `Hero`·`Enemy`의 부모 `Fighter`에도 붙어 있다. 컬렉션은 `List<string>`·`List<DeckCard>`·`List<Enemy>`뿐이다. 손패·뽑을 더미·버린 더미는 카드 객체가 아니라 카드 id 리스트(`List<string>`)라서, 카드가 더미 사이를 옮겨 다녀도 변경분은 id 글자 몇 개다.

```csharp
[Serializable]
public class Battle
{
    public int turn;
    public int energy;
    public List<Enemy> enemies = new List<Enemy>();
    public List<string> drawPile = new List<string>();   // 덱 카드(DeckCard) id
    public List<string> hand = new List<string>();
    public List<string> discard = new List<string>();
    public int damageThisTurn;
}
```

### 판 데이터에는 ID와 바뀌는 값만, 고정 값은 원본에서 읽는다

덱에 든 카드 한 장을 판 데이터에 어떻게 둘까. 원본(도감 › 인스턴스의 `Card`)을 통째로 복사해 덱에 넣으면 이름·비용·그림(`art`)·사용 기능(`usable` — 트리거와 결과 Block 묶음)까지 장마다 들어간다. 그러면 세 가지가 나빠진다.

- **크기.** 같은 카드 다섯 장이면 같은 고정 값이 다섯 벌이다. Sync가 비교할 것도, 판 저장도, 멀티플레이에서 처음 보낼 Snapshot도 그만큼 커진다.
- **저장.** 스프라이트는 도감 항목(`GameData`)이 아니라서 판 저장 파일에 들어가지 않는다. 저장할 때마다 "저장할 수 없는 참조입니다" 경고가 나고, 이어 한 뒤에는 그림이 비어 있다.
- **원본 수정.** 기획자가 카드의 비용을 고쳐도, 이미 덱에 복사된 장은 예전 값 그대로다.

그래서 카드 던전은 덱에 **원본 ID와 그 장만의 바뀌는 값**만 담은 가벼운 클래스를 둔다.

```csharp
[Serializable]
public class DeckCard
{
    public string id;                                           // 장마다 다른 id(Game.NewId("card"))
    [DataRef(typeof(InstanceData))] public string card;         // 원본 인스턴스 ID(능력 이름으로도 쓴다)
    public bool upgraded;
}
```

이름·비용·그림·사용 기능 같은 고정 값은 쓸 때 원본에서 읽는다. 원본은 `session.Instances`가 복사 없이 주는 객체이고, 게임 중에는 읽기만 한다. 규칙 함수에 한 줄로 모아 둔다.

```csharp
public static Card CardData(GameApi game, string card) => game.Session.Instances?.Invoke(card) as Card;
public static MonsterData MonsterOf(GameApi game, string monster) => game.Session.Instances?.Invoke(monster) as MonsterData;
```

화면도 같다. 손패의 카드 그림은 `Rules.CardData(Game, deckCard.card).art`로 읽는다. 판 저장에 그림이 없으니 경고도 없고, 이어 한 뒤에도 그림이 그대로 나온다. 몬스터도 같다: 전투 중의 적(`Enemy`)은 원본 ID(`monster`)와 체력·공격 차례처럼 바뀌는 값만 들고, 공격 순서 같은 고정 값은 원본 `MonsterData`에서 읽는다.

판단 기준은 하나다: **"이 값이 판 중에 그 객체마다 따로 바뀔 수 있는가?"** 그렇다면 판 데이터, 아니라면 원본이다. 강화 여부(`upgraded`)는 장마다 바뀌므로 판 데이터에, 카드 이름은 바뀌지 않으므로 원본에 둔다.

### 이벤트를 받는 쪽은 그리기만

전투 화면이 피해 숫자를 띄우는 코드는 이벤트를 받아 연출만 한다. 체력 바 값은 이벤트가 아니라 플레이어 데이터에서 읽는다.

```csharp
_onDamage = GBibleRuntime.Game.Events.Subscribe<DamageDealt>(e => ShowDamageNumber(e.targetId, e.amount));
// OnDisable에서 _onDamage.Dispose();
```

`ShowDamageNumber` 안에서 `hp`를 고치면 안 된다. 그 순간은 행동 안일 수도, 밖일 수도 있고, 어느 쪽이든 서버가 모르는 변경이 된다.

### 디버그 명령도 행동으로

개발용 "골드 +100" 명령도 값을 직접 바꾸지 않고 `DeveloperEditAction`(개발자에게만 보이는 행동)으로 감싼다. 그러면 모니터에서 Undo할 수 있고, Undo 덮어쓰기 경고도 생기지 않는다.

```csharp
session.Debug.Register("카드 던전", "골드 +100", () =>
    session.Do(new DeveloperEditAction { label = "골드 +100", Edit = () => session.Game.Data().Run.gold += 100 }));
```

### 난수 흐름 나누기

카드 던전은 난수 흐름을 쓰임마다 나눈다: 덱 섞기 `"deck"`, 무작위 대상 `"cardTarget"`, 보상 골드 `"loot"`. 흐름을 나눠 두면 한쪽에서 뽑는 횟수가 바뀌어도(카드 효과를 고쳐 대상 뽑기가 한 번 늘어도) 다른 쪽 결과는 그대로라서, 고정 시드로 버그를 재현할 때 덜 흔들린다.

```csharp
var gold = Game.Random("loot").Range(10, 21);   // 10 이상 21 미만
```

---

> **다른 장르라면** 실시간 액션은 위치·속도를 매 프레임 MonoBehaviour에서 움직이고, 규칙에 필요한 값(체력·쿨다운 시각·현재 칸)만 플레이어 데이터에 둔다. 입력은 여전히 행동이지만 Undo는 행동 단위가 아니라 스냅샷 되감기(`HistoryMode.Snapshots`)를 쓴다. 쿨다운은 `Time.time`이 아니라 `session.Clock.Time` 기준 시각으로 적어야 되감을 때 함께 돌아간다.

> **다른 장르라면** 유닛 수천 개의 시뮬레이션·방치형은 객체 수가 성능을 정한다. 계산으로 얻을 수 있는 값(누적 생산량을 시간으로 계산 등)을 저장하지 않고, 자주 바뀌는 묶음을 별도 키로 나눈다.

---

## 7. 체크리스트

- [ ] 모든 데이터 클래스와 그 부모·안쪽·요소 클래스에 `[Serializable]`이 있다.
- [ ] 모든 이벤트 클래스에 `[GameEvent]`가 있다.
- [ ] `HashSet`·`Queue`·중첩 컬렉션·컬렉션 그대로의 플레이어 데이터 값이 없다.
- [ ] 플레이어 데이터를 바꾸는 코드는 모두 행동·Block·결과·`[BindTo]` 안에 있다.
- [ ] 행동 필드는 id와 숫자·글자·enum뿐이다.
- [ ] 판 데이터에는 원본 ID와 바뀌는 값만 있고, 고정 값(이름·비용·그림·사용 기능)은 원본에서 읽는다.
- [ ] 이벤트를 받는 씬 코드는 연출·UI만 하고 `OnDisable`에서 `Dispose`한다.
- [ ] 규칙 난수는 `Game.Random`, 연출 난수는 `UnityEngine.Random`이다.
- [ ] 시간 규칙은 게임 시계 기준이다.
- [ ] 게임 값을 담은 static 변수가 없고, Block·결과 코드는 `GBibleRuntime` 대신 받은 세션을 쓴다.
- [ ] 텍스트 인자는 `("이름", 값)` 형태다.
- [ ] 콘솔·타임라인에 "예전 방식(리플렉션)", "읽기 코드가 없어 리플렉션으로 읽습니다", "저장할 수 없는 참조입니다" 경고가 없다(있다면 이유를 안다).
- [ ] `HistoryLimit`을 정했다.

---

## 8. 더 보기

- [01. 플레이어 데이터](../Manual/01-PlayerData.md) — 기록되는 필드, 지원 형식, Sync 시점, 예전 방식
- [02. 행동과 Undo](../Manual/02-Actions-Undo.md) — 행동 밖 변경, 지연 효과, 실패와 취소
- [03. Game API](../Manual/03-GameApi.md) — `Find`·`All`의 비용, `NewId`, 확장 메서드
- [07. 난수와 시간](../Manual/07-Random-Time.md) — 난수 흐름, 게임 시계, Undo·저장과 함께 움직이는 방식
- [10. 이벤트](../Manual/10-Events.md) — 쏘기·받기 규칙
- [../Coding.md](../Coding.md) — 게임 코드 규칙과 Mirror 비용
