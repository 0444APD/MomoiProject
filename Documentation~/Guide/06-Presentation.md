# 06. 화면과 연출

## 1. 이 단계의 목표

게임 규칙이 바꾼 값을 화면에 보이고, 일어난 일에 맞춰 연출(피해 숫자·소리·대사)을 튼다. 화면은 플레이어 데이터를 **읽기만** 하고, 값을 바꾸는 입력은 모두 행동으로 보낸다. 데이터는 행동이 끝나는 순간 최종 모습이 되고, 연출은 그 뒤를 시간을 두고 따라간다.

이 장에서 쓰는 말:

| 말 | 뜻 |
|---|---|
| 행동 | 플레이어 입력 하나를 담은 클래스. `Game.Do`로 실행하고, 하나가 Undo 한 단위다. |
| Undo | 마지막 행동이 바꾼 값을 행동 전으로 되돌리는 것. |
| Sync | 시스템이 들고 있는 플레이어 데이터의 사본(Mirror)과 지금 값을 비교해 바뀐 곳만 찾아 기록하는 동작. 행동 시작·끝, 행동 안의 `Game.Find`·`Game.All`, 매 프레임 끝에 일어난다. |
| ChangeSet | Sync 한 번이 찾아낸 변경 묶음. 멀티플레이에서 다른 쪽에 보내는 단위다. |
| 이벤트를 쏜다 / 받는다 | 게임 코드가 "방금 일어난 일"을 `Game.Events.Publish`로 넘기는 것이 쏜다, `Subscribe`로 등록한 함수가 그것을 넘겨받는 것이 받는다. |
| 문장 틀 | 텍스트 키에 들어 있는 글. `{값}`·조사·복수형을 문법으로 적는다. |

---

## 2. 할 일 순서

1. **화면이 읽을 값을 정한다.** 체력·골드·손패·물약 칸처럼 화면에 보일 값이 모두 플레이어 데이터에 있는지 확인한다(없으면 02장으로 돌아가 클래스에 필드를 더한다).
2. **데이터 변경 알림으로 화면을 갱신한다.** `session.Data.Changed`를 받아 "다시 그릴 것"으로 표시하고, 프레임 끝에 한 번 그린다.
3. **입력을 행동으로 보낸다.** 버튼은 id만 쥐고 `Game.Do(new 행동 { … })`를 부른다. 실패는 예외로 오므로 잡아서 안내한다.
4. **이벤트를 받아 연출을 튼다.** 행동 안에서 쏜 이벤트는 모아 두었다가 행동이 성공하면 차례로 튼다.
5. **모든 글을 텍스트 키로 바꾼다.** `Texts.Get(키, 값…)`과 문장 틀(조사·복수형)을 쓰고, 언어가 바뀌면 다시 그린다.
6. **다이얼로그를 붙인다.** 상점 주인처럼 대사가 필요한 곳은 다이얼로그 그래프로 만들고 `Dialogues.Play`로 연다.
7. **소리를 붙인다.** 배경 음악은 `GameAudio.PlayBgm`, 연출 소리는 `GameAudio.PlaySfx`.
8. **인벤토리 칸을 그린다.** 물약 칸은 `Inventory.slots`를 읽어 그리고, 마시기는 행동으로 보낸다.

---

## 3. 판단 기준

**어떤 알림을 받는가**

| 이럴 때 | 이것 |
|---|---|
| 어떤 플레이어 데이터가 바뀌었으면 화면을 다시 그린다 | `session.Data.Changed`(`Action<string>`, 바뀐 키) |
| 무엇이 바뀌었는지(손패에 한 장 추가, 적 하나 제거)에 따라 다르게 움직인다 | `session.Data.DataChanged`(키 + 바뀐 경로 목록 `DataChange`: 종류 Set·Insert·Remove·Reorder, 경로, 옛 값·새 값) |
| 다른 쪽(서버·클라이언트)에 변경을 보낸다 | `session.Data.Synced`(ChangeSet). 화면용이 아니다(11장) |
| "피해를 줬다", "적이 쓰러졌다"처럼 일어난 일에 맞춰 연출한다 | 이벤트를 받는다(`Game.Events.Subscribe<T>`) |
| 행동이 끝났을 때 한 번 무언가 한다 | `session.Actions.Executed`(`ActionRecord`, 안쪽 행동이면 `Nested`가 참) |
| 실패해 취소된 행동을 안다 | `session.Actions.TransactionAborted` |

**값을 어디서 읽는가**

| 이럴 때 | 이것 |
|---|---|
| 판·전투 값(체력, 골드, 손패) | `Game.Data().Run` |
| 카드 이름·비용·그림, 몬스터 초상 같은 고정 값 | 원본 인스턴스. 카드 던전은 `Rules.CardData(game, deckCard.card)`처럼 원본 ID로 복사 없이 읽는다(읽기만 한다) |
| 플레이어에게 보일 이름 | 텍스트 `<ID>.name`(`Texts.NameOf(id)`, 또는 문장 틀에 ID를 값으로 넘김) |
| 매 프레임 움직이는 위치·애니메이션 | MonoBehaviour 필드(플레이어 데이터에 넣지 않는다) |

**연출에서 하지 않는 것**

| 하지 않는 것 | 이유 |
|---|---|
| 받는 쪽에서 플레이어 데이터를 고친다 | 행동 밖 변경이 되어 Undo·저장·멀티플레이에서 빠진다 |
| 행동 안에서 연출을 기다린다(코루틴·`await`) | 행동은 한 번에 끝나야 한다 |
| 연출의 무작위에 `Game.Random`을 쓴다 | Undo가 모든 난수 흐름을 되돌려 규칙 결과가 어긋난다. 연출은 `UnityEngine.Random` |
| 판 데이터에 스프라이트 같은 에셋 참조를 둔다 | 도감 항목이 아닌 에셋 참조는 판 저장에 담기지 않는다("저장할 수 없는 참조입니다" 경고). 판 데이터에는 원본 ID만 두고 그림은 원본에서 읽는다 |

---

## 4. 카드 던전에서 실제로

### 전투 화면: 알림은 표시만, 그리기는 프레임 끝에 한 번

카드 던전의 전투 화면은 UI Toolkit이다. 체력·골드·에너지 글자, 손패 버튼, 물약 칸을 그린다. 플레이어 데이터 `run` 하나에 판의 모든 값이 들어 있으므로, `Changed`가 `run` 키를 쏘면 화면 전체를 다시 그린다.

알림을 받은 자리에서 바로 그리지 않는 데는 이유가 있다. 행동 하나에도 알림은 여러 번 온다. 행동 안에서 `Game.Find`·`Game.All`이 중간에 Sync하면 그때 한 번, 행동이 끝날 때 또 한 번 온다. 행동이 실패하면 진행분을 Sync한 알림과 되돌린 알림이 함께 오고, Undo해도 온다. 그래서 알림에서는 표시만 하고, `LateUpdate`에서 표시가 있으면 한 번 그린다.

```csharp
// 행동 하나에도 알림이 여러 번(중간 Sync, 실패한 행동의 되돌림, Undo) 올 수 있다. 여기서는 표시만 한다.
void OnDataChanged(string key)
{
    if (key == GameIds.PlayerData.Run.Id) _dirty = true;
}

void LateUpdate()
{
    if (!_dirty) return;
    _dirty = false;
    Redraw();
}
```

받기는 `OnEnable`에서 `GBibleRuntime.Session.Data.Changed += OnDataChanged`, 그만 받기는 `OnDisable`에서 한다. 세션은 씬보다 오래 살아서, 그만 받지 않으면 사라진 화면 오브젝트를 계속 부른다. 게임을 끌 때는 세션이 먼저 사라질 수 있으므로 `GBibleRuntime.Session`이 `null`인지 보고 뺀다.

손패 버튼은 카드 객체가 아니라 덱의 한 장(`DeckCard`)의 **id**를 쥔다. Undo·불러오기 뒤에는 같은 카드가 다른 객체일 수 있지만 id는 같다. 버튼을 누르면 런 스크립트(`RunDirector.TryPlay`)가 `PlayCard` 행동을 보내고, 에너지가 모자라는 등의 실패는 행동이 바꾼 것을 모두 취소한 뒤 예외로 돌아온다. 화면은 그 메시지를 보여 주기만 한다.

```csharp
void Play(string cardId)
{
    var target = Rules.LivingEnemies(GBibleRuntime.Game).FirstOrDefault()?.id;   // 대상 고르기 화면은 생략
    _message.text = director.TryPlay(cardId, target, out var error) ? "" : error;
}
```

덱의 한 장(`DeckCard`)에는 장마다 다른 `id`, 원본 ID `card`, 강화 여부 `upgraded`만 들어 있다. 이름·비용·그림·사용 기능 같은 고정 값은 원본 `Card`에 있고, 화면은 `Rules.CardData(game, deckCard.card)`로 원본을 복사 없이 읽는다(`session.Instances`가 주는 원본이라 읽기만 한다). 판 데이터에 스프라이트 참조가 없으니 판 저장 경고도 없고, 이어 하기 뒤에도 그림이 그대로 나온다. 기획자가 원본의 비용을 고치면 진행 중인 판의 모든 장에 바로 반영된다.

### 피해 숫자: 이벤트를 모았다가 행동이 성공하면 튼다

`Rules.Damage`는 피해를 줄 때마다 `DamageDealt`를, 적이 쓰러지면 `EnemyKilled`를 쏜다. 연출은 이 두 이벤트를 받는다.

여기에 함정이 하나 있다. 이벤트는 쏘는 그 자리에서 받는 쪽을 모두 부른다(대기열이 없다). 그래서 카드 효과가 피해를 준 뒤 같은 행동 안에서 예외가 나면, 값은 모두 취소되었는데 피해 숫자는 이미 떠 버린다. 그래서 행동 안에서 받은 이벤트는 모아 두었다가 `Executed`(행동 성공)에서 틀 차례로 옮기고, `TransactionAborted`(행동 실패)에서 버린다. 행동 밖에서 쏜 이벤트(도전과제 달성처럼 행동이 끝난 뒤 쏘는 것)는 바로 틀 차례에 넣는다.

```csharp
void Receive(object e)
{
    if (_session.Data.InTransaction) _pending.Add(e);   // 행동이 아직 실패할 수 있다
    else _queue.Enqueue(e);
}

void OnExecuted(ActionRecord record)
{
    if (record.Nested) return;   // 안쪽 행동은 바깥 행동이 끝날 때 함께 튼다
    foreach (var e in _pending) _queue.Enqueue(e);
    _pending.Clear();
}

void OnAborted(long transactionId) => _pending.Clear();   // 실패해 취소된 행동의 연출은 버린다
```

받기 등록은 `_session.Events.Subscribe<DamageDealt>(Receive)`이고, 돌려받은 `IDisposable`을 `OnDisable`에서 `Dispose`한다. `PlayCard` 안에서 부르는 `Game.Use`·`Game.Do(new WinBattle())`는 바깥 행동의 일부로 실행되며 `Executed`도 `Nested`가 참인 기록으로 한 번 더 온다. 그래서 `Nested`를 걸러야 연출이 반쯤 끊겨 나오지 않는다.

### 데이터와 연출의 시간을 나눈다

화염구로 고블린을 잡으면 `PlayCard`가 끝나는 순간 데이터는 이미 최종 모습이다. 고블린은 `battle.enemies`에서 빠졌고, 남은 적의 체력은 깎였고, 전투에서 이겼다면 `battle`이 `null`이 되고 층이 올랐다. 화면이 데이터만 보고 그리면 고블린이 피해 숫자도 없이 바로 사라진다.

그래서 연출은 데이터가 아니라 대기열을 따라간다. 틀 차례가 된 이벤트를 하나씩, 간격(`gap`)을 두고 튼다.

```csharp
void Update()
{
    _wait -= Time.deltaTime;   // 게임 시간: 일시정지하면 연출도 멈춘다
    if (_wait > 0 || _queue.Count == 0) return;
    Play(_queue.Dequeue());
    _wait = gap;
}
```

적 그림처럼 "데이터에서는 이미 없지만 연출이 끝날 때까지 보여야 하는 것"은 화면이 따로 들고 있다가 `EnemyKilled` 연출을 틀 때 치운다. 체력 막대는 데이터 값으로 곧장 바꿔도 되고, 보이는 값을 데이터 값 쪽으로 몇 프레임에 걸쳐 따라가게 해도 된다. 어느 쪽이든 데이터는 건드리지 않는다.

`Update`의 `Time.deltaTime`은 게임 시간이라 모니터나 메뉴가 `GameTime.Pause`로 게임을 멈추면 연출도 멈춘다. 일시정지 메뉴처럼 멈추면 안 되는 연출은 `Time.unscaledDeltaTime`을 쓴다.

### 텍스트와 현지화

화면의 모든 글은 텍스트 키로 찾는다. `Texts.Get(키, (이름, 값), …)`이 현재 언어의 최종 글자를 돌려준다. 카드 던전 전투 화면이 쓰는 키와 원문 표의 문장 틀은 다음과 같다.

| 키 | ko | en |
|---|---|---|
| `hud.hp` | `체력 {hp}/{max}` | `HP {hp}/{max}` |
| `hud.gold` | `{gold} 골드` | `{gold} Gold` |
| `hud.energy` | `에너지 {energy}` | `Energy {energy}` |
| `card.button` | `{card} ({cost})` | `{card} ({cost})` |
| `hud.potion` | `{potion} ×{count}` | `{potion} ×{count}` |
| `hud.emptySlot` | `빈 칸` | `Empty` |
| `popup.damage` | `-{amount}` | `-{amount}` |
| `popup.kill` | `{monster:을} 쓰러뜨렸다!` | `Defeated {monster}!` |
| `strike.name` · `goblin.name` · `healPotion.name` … | `타격` · `고블린` · `회복 물약` … | `Strike` · `Goblin` · `{count, plural, one {Healing Potion} other {Healing Potions}}` … |

값으로 도감 ID를 넘기면 문장 틀이 그 언어의 `<ID>.name` 텍스트로 바꾼다. 그래서 손패 버튼은 카드 제목 칸이 아니라 원본 ID를 넘긴다.

```csharp
// "card"에 원본 ID를 넘기면 문장 틀이 <원본 ID>.name 텍스트(현재 언어의 카드 이름)로 바꾼다.
_hand.Add(new Button(() => Play(id)) { text = Texts.Get("card.button", ("card", card.card), ("cost", Rules.CardData(GBibleRuntime.Game, card.card)?.cost ?? 0)) });
```

`{monster:을}`은 한국어 조사다. 이름이 "고블린"이면 "고블린을", "슬라임"이면 "슬라임을", 받침 없는 이름이면 "를"이 붙는다. `hud.potion`의 `{potion}`에는 `count`도 함께 넘어가므로, 영어 이름 텍스트가 `{count, plural, …}`로 단수·복수를 고른다. 게임 코드는 언어마다 분기하지 않는다.

`[GameEvent]` 이벤트 객체를 값 묶음으로 통째로 넘길 수도 있다. `Texts.Get("popup.damage", hit)`는 이벤트의 필드(`amount` …)를 컴파일 때 생긴 읽기 코드로 읽는다(실행 중 리플렉션이 없다). 익명 객체(`new { gold = 3 }`)는 부를 때마다 리플렉션으로 읽으므로 자주 그리는 곳은 튜플을 쓴다.

언어가 바뀌면 `GameLanguage.Changed`가 온다. 전투 화면은 이것도 "다시 그릴 것"으로 받는다(`GameLanguage.Changed += MarkDirty`). 인스펙터만으로 쓰는 `LocalizedText` 컴포넌트는 같은 오브젝트의 `text` 속성이 있는 컴포넌트(TextMeshPro 등)에 글을 넣는다. 붙을 때 한 번 글 넣는 대리자를 만들어 두므로 새로 고칠 때마다 리플렉션을 쓰지 않는다. 다만 UI Toolkit의 `Label`은 컴포넌트가 아니라 대상이 될 수 없으므로, 이처럼 `Texts.Get`과 `GameLanguage.Changed`로 직접 넣는다.

행동이 던지는 실패 메시지("에너지가 모자랍니다.")는 코드에 적힌 한국어 그대로 화면에 온다. 여러 언어로 낼 게임이면 행동이 텍스트 키를 담아 던지고 화면이 `Texts.Get`으로 바꾸게 규칙을 정해 둔다.

### 다이얼로그: 상점 주인

상점 층에 들어가면 상점 주인이 말을 걸고, 대화에서 "구경한다"를 고르면 상점 화면이 열린다. 대사와 연출은 기획자가 그래프로 만들고, 게임 코드는 열기와 신호 받기만 한다.

에디터에서 할 일:

1. 도감 사이드바 "다이얼로그 시스템"의 **기능 활성화**로 켜고, `MomoiProject → 다이얼로그 시스템 → UI 템플릿 만들기`를 한 번 한다.
2. 도감 › **스탠딩 프로필**에 상점 주인(`merchant`)을 만들고 스탠딩 그림을 넣는다. 이름은 텍스트 `merchant.name`이다.
3. `MomoiProject → 다이얼로그 시스템 → 그래프`에서 다이얼로그 `shopkeeper`를 만든다. 시작 → 대사("어서 와, 모험가.", 화자 `merchant`) → 선택지 "구경한다" → **신호 보내기**(`openShop`) → 끝, 선택지 "나간다" → 끝.
4. 같은 그래프에 시작 노드를 하나 더 두고 시작 ID를 `noGold`로 한다(대사 "돈이 모자라는군.").
5. 비싼 카드 선택지가 있다면 **보일 조건**에 "플레이어 데이터 검사"(`run`의 `gold` 이상)를 걸고 흐리게 보여 주기를 켠다.

코드는 상점 층에 들어올 때 배경 음악을 바꾸고 다이얼로그를 연다. 그래프의 신호는 `Dialogues.Signaled`로 받는다.

```csharp
public void Enter()
{
    GameAudio.PlayBgm(shopMusic);   // 같은 곡이면 끊기지 않고 이어진다
    if (!Dialogues.IsPlaying) Dialogues.Play(GameIds.Dialogue.Shopkeeper);
}

void OnSignal(string signal)
{
    if (signal == "openShop") shopPanel.SetActive(true);
}
```

카드 구매는 상점 화면의 버튼이 `BuyCard` 행동으로 보낸다. 골드가 모자라 행동이 실패하면 같은 다이얼로그를 `noGold` 시작 ID로 연다(`Dialogues.Play(GameIds.Dialogue.Shopkeeper, "noGold")`). 대사 그래프의 **값 바꾸기** 노드도 값을 바꿀 수 있지만(행동 하나로 기록된다), 골드 검사·덱에 카드 넣기처럼 규칙이 있는 일은 행동에 두고 다이얼로그는 말과 연출만 맡긴다.

알아 둘 점:

- 다이얼로그가 열려 있는 동안 재생기가 `GameTime.Pause("다이얼로그")`로 게임 시간을 멈춘다. 전투 연출의 `Update`도 함께 멈춘다.
- 대사 원문의 `{…}`에는 모든 플레이어 데이터가 ID로 넘어가고, 점으로 값 안의 필드를 읽는다. 카드 던전은 판의 값을 `run` 하나에 모아 두었으므로 "가진 돈은 {run.gold}골드군."처럼 쓴다(이름이 점을 포함한 플레이어 데이터가 따로 있으면 그것을 먼저 쓴다). 화면 코드의 `Texts.Get`도 같은 규칙이라, 값 묶음에 객체를 넘기고 틀에서 `{hero.hp}`처럼 읽을 수 있다.
- 판을 불러온 뒤 `Dialogues.ResumeSaved()`를 부르면 저장할 때 보던 대사부터 이어 간다. 이어 하기(`GBibleRuntime.LoadRun`)가 성공한 다음에 부른다.

### 소리

오디오는 게임이 켜질 때 자동으로 만들어지고 씬이 바뀌어도 남는다. 상점의 배경 음악은 위의 `GameAudio.PlayBgm`이고, 전투 연출은 피해 숫자를 띄울 때 효과음을 낸다.

```csharp
case DamageDealt hit:
    Popup(Texts.Get("popup.damage", hit), hit.targetId);          // [GameEvent] 객체를 값 묶음으로(생성 코드로 읽는다)
    GameAudio.PlaySfx(hitSound, pitch: UnityEngine.Random.Range(0.95f, 1.05f));   // 연출의 무작위는 UnityEngine.Random
    break;
```

소리는 게임 시간과 상관없이 흐른다. 다이얼로그나 메뉴로 게임이 멈춰도 배경 음악은 계속 나고, 다이얼로그가 자기 배경 음악을 틀었다면 끝날 때 전의 곡으로 돌아간다. 설정 화면의 음량 슬라이더는 `GameAudio.SetVolume(AudioChannel.Bgm, 값)`(기기에 저장)과 `GameAudio.VolumeChanged`로 만든다.

### 물약 칸: 인벤토리 화면

판 데이터의 `potions`는 인벤토리 모듈의 `Inventory`다(칸 수 3). 칸에는 물약 원본 ID(`item`)와 개수(`count`)만 들어 있다. 화면은 칸마다 버튼을 그리고, 남은 칸은 빈 칸으로 채운다.

```csharp
foreach (var slot in run.potions.slots)
{
    var potion = slot.item;
    _potions.Add(new Button(() => Drink(potion)) { text = Texts.Get("hud.potion", ("potion", potion), ("count", slot.count)) });
}
for (var i = run.potions.slots.Count; i < run.potions.capacity; i++)
    _potions.Add(new Label(Texts.Get("hud.emptySlot")));
```

버튼은 `Game.Do(new DrinkPotion { potion = potion })`를 보낸다. 칸에서 하나 빼기와 물약의 사용 기능(회복)이 한 행동이라, Undo 한 번에 개수와 체력이 함께 돌아온다. 물약 아이콘이 필요하면 원본(`Rules.PotionOf(game, slot.item)`)에서 읽는다.

---

## 5. 다른 장르라면

> **다른 장르라면** 실시간 액션은 위치·속도·애니메이션을 MonoBehaviour가 들고 매 프레임 그린다. 플레이어 데이터 알림은 체력·탄약·쿨다운 같은 규칙 값의 HUD에만 쓰고, 피격 이펙트는 이벤트를 받아 튼다. 쿨다운 표시는 `준비 시각 - Game.Session.Clock.Time`으로 계산한다(07장).

> **다른 장르라면** 턴제 전술은 유닛이 많아 화면 전체를 다시 그리기 무겁다. `DataChanged`의 경로(`units[#12].hp`, `units[#3]` 제거)를 보고 바뀐 유닛만 갱신한다. 유닛 이동 연출은 이동 행동이 쏜 이벤트를 받아 칸에서 칸으로 움직인다.

> **다른 장르라면** 대사가 이야기의 중심인 게임은 다이얼로그 화면(UXML·USS)을 프로젝트에 맞게 고치고, 필요한 동작은 `DialogueUIExtension`을 붙여 더한다. 코드는 요소를 이름(`dialogue-root`·`dialogue-text` …)으로만 찾으므로 모양은 자유롭게 바꿔도 된다.

---

## 6. 체크리스트

- [ ] 화면에 보일 값이 모두 플레이어 데이터 또는 원본 인스턴스에 있다.
- [ ] 화면은 `Data.Changed`(필요하면 `DataChanged`)를 받아 표시만 하고, 프레임 끝에 한 번 그린다.
- [ ] 받기는 `OnEnable`, 그만 받기(`-=`·`Dispose`)는 `OnDisable`에서 한다.
- [ ] 버튼은 id를 쥐고 행동을 보내며, 실패 예외를 잡아 안내한다.
- [ ] 받는 쪽 코드에서 플레이어 데이터를 바꾸지 않는다.
- [ ] 행동 안에서 받은 이벤트는 행동이 성공한 뒤 틀고, 실패하면 버린다.
- [ ] 연출의 무작위는 `UnityEngine.Random`, 규칙의 무작위는 `Game.Random`이다.
- [ ] 화면의 모든 글이 텍스트 키이고, 도감 항목 이름은 `<ID>.name`이다.
- [ ] 언어를 바꾸면 화면 글자가 다시 그려진다.
- [ ] 다이얼로그는 말·연출만, 규칙이 있는 일은 행동이 맡는다.
- [ ] 판 데이터에는 원본 ID와 바뀌는 값만 있고, 이름·그림 같은 고정 값은 원본에서 읽는다.

---

## 7. 더 보기

- [01. 플레이어 데이터](../Manual/01-PlayerData.md) — `Changed`·`DataChanged`·`Synced`, Sync가 일어나는 때
- [10. 이벤트](../Manual/10-Events.md) — `Subscribe`·`Dispose`, 받는 쪽 규칙
- [07. 난수와 시간](../Manual/07-Random-Time.md) — `GameTime`, 게임 시계
- [23. 텍스트와 현지화](../Manual/23-Text-Localization.md) — `Texts`, 문장 틀, `GameLanguage`
- [24. 다이얼로그](../Manual/24-Dialogue.md) — 그래프·신호·저장 이어 가기
- [26. 오디오](../Manual/26-Audio.md) — 채널·음량
- [25. 인벤토리](../Manual/25-Inventory.md) — 칸·더하기·빼기
