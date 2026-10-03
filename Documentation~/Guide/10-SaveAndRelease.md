# 10. 저장과 출시

## 1. 이 단계의 목표

프로필 저장과 판 저장·이어 하기를 게임 흐름에 붙이고, 개발 중에는 저장이 언제 초기화되는지 알고 쓰며, 출시 상태로 바꾼 뒤에는 ID·필드를 바꿔도 플레이어의 저장이 이어지게 한다. 마지막에 출시 전 점검을 한 번에 끝낸다.

이 장에서 쓰는 말:

| 말 | 뜻 |
|---|---|
| 구역 (`DataZone`) | 플레이어 데이터가 들어가는 저장 파일. `Session`(이번 판)과 `Meta`(프로필) 둘뿐이다. 쓰는 법·Undo 규칙은 같고 파일만 다르다. |
| 프로필 | 메타 구역 전체(누적 로그·도전과제 달성 포함). 파일 `profile.json`. 게임이 켜질 때 자동으로 불러오고, 끌 때 자동으로 저장한다. |
| 판 | 세션 구역 전체 + 난수 상태 + 게임 시계. 파일 `run.json`. **자동으로 불러오지 않는다.** 게임이 원하는 때 `GBibleRuntime.LoadRun`을 부른다. |
| 저장 트리 | 저장 직전의 중간 모습. 클래스는 "필드 이름 → 값" 딕셔너리, 리스트는 리스트, 숫자는 `double`. 저장 변환 코드가 이것을 고친다. |
| 저장 버전 | 저장 파일에 적는 정수(`SessionOptions.SaveVersion`, 기본 1). |
| 변환 | 예전 버전의 저장 트리를 지금 구조로 고치는 코드(`SaveMigrations`). |
| 개발 중 / 출시됨 | 프로젝트 상태. 메뉴 **MomoiProject › 출시됨 (저장 호환 유지)** 로 바꾼다. 개발 중에는 호환되지 않는 저장을 지우고, 출시됨이면 이어받는다. |
| Undo | 마지막 행동을 되돌리는 것. Undo 이력은 저장하지 않는다. |

---

## 2. 할 일 순서

1. 플레이어 데이터마다 구역이 맞는지 본다(판이 끝나면 버릴 값 = `Session`, 남을 값 = `Meta`).
2. 판을 언제 저장할지 정한다(아래 판단 기준).
3. 판이 끝나는 순간을 잡아 판 저장을 지우고 프로필을 바로 저장한다.
4. '이어 하기'와 '새 판'을 붙인다.
5. 저장되지 않는 값(스프라이트 같은 에셋 참조, Undo 이력)에 기대는 화면이 없는지 본다.
6. 개발 중 저장 초기화가 언제 일어나는지 알고, 모니터의 저장 명령으로 저장·불러오기를 시험한다.
7. 출시 직전에 **출시됨**을 켠다.
8. 출시 후 구조를 바꿀 때는 바꾼 종류에 맞는 도구(아래 표)로 예전 저장을 이어받는다.
9. 출시 전 체크리스트를 끝낸다.

---

## 3. 판단 기준

### 어느 구역에 둘까

| 값 | 구역 | 카드 던전 |
|---|---|---|
| 판이 끝나면 버린다(덱·체력·층·전투) | `Session` | `run` (`RunState`) |
| 판이 끝나도 남는다(해금·최고 기록·누적 재화) | `Meta` | `profile` (`Profile`) |
| 도전과제 달성, 누적 범위 로그 | 시스템이 메타 구역에 둔다 | `firstWin`, `kills`(누적이라면) |
| 이번 판 범위 로그 | 시스템이 세션 구역에 둔다 | 판 저장에 들어가고 `NewRun` 때 처음 값으로 |

로그의 범위(이번 판/누적)는 도감 › 로그에서 정한다. 범위가 곧 구역이다.

### 판을 언제 저장할까

| 게임 | 저장 시점 | 이유 |
|---|---|---|
| 턴제·카드(행동이 띄엄띄엄) | 행동이 끝날 때마다 | 판 파일이 작고 행동이 드물다. 이어 하기가 마지막 입력 직후 모습이 된다. 난수 상태도 함께 저장되므로 껐다 켜서 결과를 다시 뽑을 수 없다 |
| 실시간 | 체크포인트(방 입장·보스 앞)와 끌 때 | 행동이 잦아 매번 쓰기는 낭비다 |
| 이어 하기가 없는 게임 | 저장하지 않음(`GBibleRuntime.AutoSaveRun = false`) | 끌 때 판을 남기지 않는다 |

판 저장은 **행동 밖에서만** 된다. 행동 안(`Run` 안, 이벤트를 받는 함수 안)에서 부르면 "행동 실행 중에는 판을 저장할 수 없습니다" 오류가 기록에 남고 저장되지 않는다. 그래서 "행동이 끝났다"는 표시만 남겨 두었다가 프레임 끝에서 저장한다.

### 출시 후 무엇을 바꿨나 → 무엇으로 이어받나

개발 중에는 저장 파일에 남은 데이터 구조(플레이어 데이터·로그·도전과제의 키, 필드 경로와 형식, enum 값 이름)가 지금과 다르면 **프로필과 판 저장을 모두** 지운다. 필드·키를 더하기만 했으면 그대로 이어간다. 출시됨이면 지우지 않으므로 바꾼 것마다 이어받을 방법이 있어야 한다.

| 바꾼 것 | 개발 중 | 출시됨에서 이어받는 법 |
|---|---|---|
| 필드·키를 더함 | 그대로 이어감 | 그대로(없던 필드는 클래스 초기값) |
| 필드 이름 | 저장 초기화 | 필드에 `[FormerlySerializedAs("예전이름")]` |
| 필드를 다른 자리로 옮김 | 저장 초기화 | `Migrations.MoveField(버전, 키, 예전 경로, 새 경로)` |
| 필드 형식·값의 뜻(단위 등) | 저장 초기화 | `Migrations.Add(버전, 설명, 트리 고치는 함수)` |
| 필드를 지움 | 저장 초기화 | 할 일 없음(불러올 때 무시하고 경고를 남긴다) |
| 도감의 플레이어 데이터·로그·도전과제 ID | 저장 초기화 | 자동(출시됨일 때 ID를 바꾸면 이력이 남고 런타임이 이어받는다) |
| 코드로 정의한 플레이어 데이터 키 | 저장 초기화 | `Migrations.RenameKey`, 또는 `session.Data.AddAlias(예전, 새)` |
| 쓰지 않게 된 키 | 저장 초기화 | `Migrations.RemoveKey` |
| **인스턴스·유물 같은 도감 항목 ID**(저장 값 안에 글자로 들어간 ID) | 초기화되지 않는다(구조가 아니라 값이라서) | `Migrations.Add`로 저장 값 안의 ID를 직접 바꾼다. 런타임은 이어받지 않는다 |

마지막 줄이 가장 놓치기 쉽다. 덱 카드의 원본 ID(`DeckCard.card`), 인벤토리 칸의 물약 ID, 가진 유물 ID, 해금한 카드 ID는 모두 저장 파일 안의 **값**이다. 도감에서 ID를 바꾸면 도감 안의 참조는 따라가지만 플레이어의 저장 파일 안 글자는 그대로다. 개발 중에도 저장이 초기화되지 않으므로, 개발 중에 인스턴스 ID를 바꿨다면 모니터의 **새 판**으로 판을 지우고 시험한다.

---

## 4. 카드 던전에서 실제로

### 4-1. 구역 확인

도감 › 플레이어 데이터에 `run`(구역 `Session`, 값 형식 `RunState`)과 `profile`(구역 `Meta`, 값 형식 `Profile`)이 있다. 판이 끝날 때 남길 것은 판 끝 행동(`EndRun`)이 프로필로 옮긴다.

```csharp
protected override void Run()
{
    var run = Game.Data().Run;
    var profile = Game.Data().Profile;
    profile.bestFloor = Math.Max(profile.bestFloor, run.floor);   // 메타 구역: 프로필 저장에 들어간다
    profile.totalGold += run.gold;
    Game.Events.Publish(new RunEnded { floor = run.floor, victory = victory });
    run.battle = null;
    run.floor = 0;   // 판 끝: 이어 하기에서 다시 열리지 않는다(RunState.IsActive)
}
```

`floor`를 0으로 돌리는 줄이 중요하다. 판 데이터에 "진행 중인가"(`RunState.IsActive => floor > 0`)가 남아 있으므로, 끝난 판이 어떤 이유로 저장되어도 이어 하기가 그것을 열지 않는다.

첫 승리 도전과제가 연쇄 번개 카드를 해금하는 것도 `profile.unlocked`(메타)에 쓴다. 도전과제 달성과 그 결과는 Undo되지 않으므로, 해금은 판을 되돌려도 남는다.

### 4-2. 판 저장 정책: 행동마다 저장, 판이 끝나면 지움

카드 던전은 턴제라서 행동이 끝날 때마다 판을 저장한다. 씬의 런 스크립트 옆에 저장만 맡는 컴포넌트를 하나 둔다. 하는 일은 세 가지다.

- 바깥 행동이 끝나거나(`session.Actions.Executed`, 안쪽 행동은 건너뜀) Undo하면(`session.Data.TransactionUndone`) "저장할 것 있음" 표시를 남긴다.
- 판 끝 이벤트(`RunEnded`)를 받으면 "판 끝남" 표시를 남긴다. 받는 쪽은 표시만 남기고 값은 바꾸지 않는다.
- 프레임 끝(`LateUpdate`)에 행동 밖인지 확인하고 표시에 따라 저장하거나 판을 닫는다.

```csharp
void OnExecuted(ActionRecord record)
{
    if (record.Nested || record.Status != ActionStatus.Done) return;   // 안쪽 행동은 바깥 행동이 끝날 때 한 번에
    _dirty = true;
}

void LateUpdate()
{
    if (_session == null || _session.Data.InTransaction) return;      // 행동 실행 중에는 판을 저장할 수 없다
    if (_ended)
    {
        _ended = _dirty = false;
        RunSaves.Finish(_session);
    }
    else if (_dirty && _session.Game.Data().Run.IsActive)
    {
        _dirty = false;
        GBibleRuntime.SaveRun(_session);
    }
}
```

끝난 판(`IsActive`가 거짓)은 저장하지 않는다. 이 컴포넌트는 켜질 때 `GBibleRuntime.AutoSaveRun = false`로 끌 때의 판 자동 저장도 끈다. 행동마다 이미 저장했기 때문이다. 프로필은 `AutoSaveRun`과 상관없이 끌 때 늘 저장된다.

판이 끝났을 때 하는 일은 둘이다: 판 저장을 지우고, 프로필을 바로 쓴다.

```csharp
public static void Finish(GameSession session)
{
    GBibleRuntime.DeleteRun();
    GBibleRuntime.SaveProfile(session);
}
```

결과 화면은 판 끝 이벤트(`RunEnded`)가 실어 온 층·승리 여부를 받아 그린다(판 끝 행동이 `floor`를 0으로 돌리기 전에 쏜다). 남은 판 값은 다음 판을 열 때 지운다.

### 4-3. 이어 하기와 새 판

런 스크립트는 씬이 시작될 때 저장된 판이 있으면 이어 하고, 없거나 끝난 판이면 새 판을 연다.

```csharp
void Start()
{
    // 저장된 판이 있으면 이어 하고, 없으면 새 판.
    if (!GBibleRuntime.LoadRun(GBibleRuntime.Session) || !Game.Data().Run.IsActive) Game.Do(new StartRun());
}
```

`LoadRun`은 실패해도(파일이 깨졌거나 게임보다 새 버전) 예외를 던지지 않고 기록에 "판 불러오기 실패: …"를 남긴 뒤 `false`를 준다. 그러면 새 판으로 넘어간다. 불러왔는데 끝난 판이어도(`IsActive`가 거짓) 새 판으로 넘어간다. 불러온 판은 난수 상태까지 저장한 순간과 같으므로, 이어 한 뒤의 덱 섞기·보상 골드도 끄기 전과 같은 결과가 나온다. Undo 이력은 불러온 시점부터 새로 쌓인다.

결과 화면의 '새 판' 버튼은 이번 판 값을 처음 값으로 되돌리고(`NewRun`, Undo 이력도 비운다) 판 저장을 지운 뒤 새 판 행동을 실행한다.

```csharp
public static void NewGame(GameSession session)
{
    session.NewRun();              // 세션 구역을 정의할 때의 값으로. 프로필은 그대로
    GBibleRuntime.DeleteRun();     // NewRun은 파일을 지우지 않는다
    session.Do(new StartRun());
}
```

타이틀 화면이 따로 있다면 '이어 하기' 버튼은 `GBibleRuntime.HasSavedRun`일 때만 켠다.

### 4-4. 저장되지 않는 값: 그림은 원본에서

스프라이트 같은 에셋 참조는 도감 항목(`GameData`)이 아니면 판 저장 파일에 들어가지 않는다. 판 데이터에 들어 있으면 저장할 때 "저장할 수 없는 참조입니다" 경고가 나고, 이어 한 뒤에는 비어 있다(Undo와 스냅샷 되감기는 메모리 안에서 참조를 그대로 이으므로 이어 하기에서만 드러난다).

카드 던전의 덱은 그래서 카드 원본을 복사하지 않고 원본 ID와 바뀌는 값만 든다(`DeckCard { id, card, upgraded }`, [09장](09-Habits.md)). 카드 그림·이름·비용은 화면이 쓸 때 원본에서 읽는다. `session.Instances`는 인스턴스 ID로 **원본**을 복사 없이 주는 함수이고, 원본은 읽기만 한다.

```csharp
public static Card CardData(GameApi game, string card) => game.Session.Instances?.Invoke(card) as Card;
```

손패를 그리는 화면은 `Rules.CardData(Game, deckCard.card).art`로 그림을 꺼낸다. 판 저장에는 원본 ID 글자만 있으므로 경고가 없고, 이어 한 뒤에도 그림이 그대로다. 규칙으로 정리하면: **플레이어 데이터에는 ID를 두고, 그림·소리 같은 에셋은 도감·인스턴스에서 찾는다.**

### 4-5. 개발 중 저장 초기화

개발하다 `Fighter.block`을 `armor`로 이름을 바꾸고 플레이하면, 콘솔에 다음과 같은 경고가 나고 프로필과 판 저장이 모두 지워진다.

> 개발 중 데이터 구조가 바뀌어 저장을 초기화했습니다: 'run.hero.block'가 없어졌거나 이름이 바뀌었습니다. (출시 후에는 초기화하지 않고 이어받습니다)

의도된 동작이다. 개발 중에는 변환 코드를 쓰지 않고 저장을 버리는 편이 빠르다. 저장을 지키며 시험하고 싶다면 필드를 더하기만 하거나, 그때만 `[FormerlySerializedAs]`를 단다.

저장·불러오기는 플레이 중 **MomoiProject › 모니터**의 **명령** 탭 **저장** 묶음으로 시험한다: **프로필 저장 · 프로필 삭제 · 판 저장 · 판 불러오기 · 새 판**. **데이터** 탭은 **판 데이터**(세션 구역)와 **프로필 데이터**(메타 구역)를 나눠 보여 주므로, 값이 어느 파일에 들어가는지 눈으로 확인할 수 있다. 저장 파일 위치는 `GBibleRuntime.SavePath`(프로필)·`RunSavePath`(판)이며 `{Application.persistentDataPath}/MomoiProject/` 아래다.

### 4-6. 출시 상태로 바꾸기

출시 빌드를 만들기 직전에 메뉴 **MomoiProject › 출시됨 (저장 호환 유지)** 를 누른다. 확인 창("출시 상태")에서 **바꾸기**를 누르면 메뉴에 체크가 생긴다. 이 상태는 런타임 데이터 목록(`GameDatabase`)에 저장되어 빌드에 따라간다.

출시됨이 되면:

- 데이터 구조가 달라도 저장을 지우지 않는다.
- 도감에서 플레이어 데이터·로그·도전과제의 ID를 바꾸면 "예전 ID → 새 ID" 이력이 남고, 런타임이 시작할 때 예전 ID로 저장된 값을 새 ID로 이어받는다.
- 그 밖의 구조 변경은 저장 버전과 변환으로 직접 이어받는다.

### 4-7. 출시 후 첫 업데이트: 저장 버전 2

출시 후 기획자가 카드 `strike`의 ID를 `slash`로 바꿨다고 하자. 도감 안의 참조와 `GameIds`는 따라 바뀌지만, 플레이어들의 저장 파일에는 덱 카드의 `"card": "strike"`가 그대로 있다. 저장 버전을 2로 올리고 v1 → v2 변환을 등록한다. 변환은 프로필을 불러오기 전에 등록되어야 하므로 반드시 `Configuring`(세션을 만들기 직전)에서 한다. 카드 던전이라면 시작 설정의 `Configure`에 한 줄을 더해 아래 함수를 부른다.

```csharp
public static void Configure(SessionOptions options)
{
    options.SaveVersion = 2;
    options.Migrations.Add(2, "카드 strike → slash", state => RenameInstance(state, "strike", "slash"));
}
```

변환 함수는 저장 트리를 고친다. 트리에서 클래스는 `Dictionary<string, object>`, 리스트는 `List<object>`다. 이 변환은 프로필 파일과 판 파일에 각각 한 번씩 돌고, 그 파일에 없는 키는 건너뛴다.

```csharp
if (state.TryGetValue(GameIds.PlayerData.Run.Id, out var runValue) && runValue is Dictionary<string, object> run)
{
    foreach (var card in Items(run, "deck")) Replace(card, "card", oldId, newId);                // 덱 카드의 원본 ID
    if (run.TryGetValue("potions", out var bag) && bag is Dictionary<string, object> potions)
        foreach (var slot in Items(potions, "slots")) Replace(slot, "item", oldId, newId);       // 인벤토리 칸의 인스턴스 ID
}
// profile의 unlocked 리스트도 같은 방법으로
```

(`Items`는 리스트 필드를 꺼내는 작은 도우미, `Replace`는 그 필드 값이 예전 ID면 새 ID로 바꾸는 도우미다.) 예전 ID는 이제 `GameIds`에 없으므로 글자로 적는다.

불러올 때 저장 파일의 버전부터 게임 버전까지 변환이 차례로 돌고, 하나마다 기록에 "저장 데이터 변환 v1 → v2: 카드 strike → slash"가 남는다. 게임보다 새 버전의 저장 파일은 거부한다(예전 게임이 새 저장을 망가뜨리지 않게).

다른 종류의 변경은 한 줄로 끝난다.

```csharp
[FormerlySerializedAs("block")] public int armor;                     // 필드 이름만 바꿨을 때
options.Migrations.MoveField(3, "run", "hero.gold", "gold");          // 필드를 옮겼을 때(v2 → v3)
options.Migrations.RemoveKey(3, "oldTutorial");                       // 쓰지 않게 된 키
```

---

> **다른 장르라면** RPG·수집형처럼 판 개념이 없는 게임은 대부분의 값이 메타 구역(프로필)이다. 세이브 슬롯이 여러 개라면 `session.ExportMeta()`·`ImportMeta(json)`로 JSON 글자를 받아 슬롯 파일 이름을 게임이 정해 쓴다. 판 쪽도 `ExportRun()`·`ImportRun(json)`이 있다.

> **다른 장르라면** 실시간 액션은 행동마다 저장하지 않고 체크포인트에서 `GBibleRuntime.SaveRun`을 부른다. 체크포인트 트리거가 행동 안에서 불릴 수 있다면 `session.Data.InTransaction`을 확인해 행동 밖에서만 저장한다.

> **다른 장르라면** 이어 하기가 없는 아케이드·퍼즐은 `GBibleRuntime.AutoSaveRun = false`만 두고 판 저장을 쓰지 않는다. 최고 점수만 메타 구역에 남긴다.

---

## 5. 체크리스트

### 저장

- [ ] 판이 끝나면 버릴 값은 `Session`, 남을 값은 `Meta` 구역이다.
- [ ] 판 저장 시점을 정했고, 저장은 행동 밖에서만 부른다.
- [ ] 판이 끝나면 판 저장을 지우고 프로필을 바로 저장한다.
- [ ] 끝난 판은 저장하지 않고, 저장되어 있더라도 이어 하기가 열지 않는다(`RunState.IsActive`).
- [ ] '이어 하기'는 `HasSavedRun`일 때만 켜지고, 불러오기 실패 때 새 판으로 넘어간다.
- [ ] '새 판'은 `NewRun`과 `DeleteRun`을 함께 부른다.
- [ ] 플레이어 데이터의 스프라이트 같은 에셋 참조에 기대는 화면이 없다(ID로 원본에서 읽는다).
- [ ] 모니터의 저장 명령으로 저장 → 끄기 → 불러오기를 해 보고 값·난수 결과가 이어지는 것을 봤다.

### 출시 전

- [ ] 도감 사이드바 아래가 "검증 통과"다(검증 오류가 있으면 빌드가 "데이터 검증 오류 n건으로 빌드를 중단합니다"로 막힌다).
- [ ] 필드 규칙의 필수 값이 비어 있지 않다(모니터 머리에 "필수 값 비어 있음"이 없다).
- [ ] 콘솔에 "예전 방식(리플렉션)"·"읽기 코드가 없어 리플렉션으로 읽습니다"·"저장할 수 없는 참조입니다"·`[BindTo]` 연결 경고가 없다(있다면 이유를 안다).
- [ ] 개발용 시드 고정·디버그 명령이 개발 빌드(`Debug.isDebugBuild`)에서만 켜진다.
- [ ] 쓰지 않는 선택 기능(다이얼로그·인벤토리)을 껐다.
- [ ] `HistoryLimit`과 저장 정책이 출시 빌드에 맞다.
- [ ] 저장할 때 `GameIds.cs`가 최신이다(도감을 저장하고 컴파일까지 끝났다).
- [ ] **MomoiProject › 출시됨 (저장 호환 유지)** 에 체크가 있다.
- [ ] `SaveVersion`이 정해져 있고(처음 출시는 1), 출시 뒤 구조를 바꾸면 버전을 올리고 변환을 등록한다는 것을 팀이 안다.
- [ ] 출시 뒤에는 인스턴스·유물 ID를 함부로 바꾸지 않는다. 바꿀 때는 저장 값 안의 ID를 옮기는 변환을 함께 넣는다.

---

## 6. 더 보기

- [06. 저장·불러오기](../Manual/06-SaveLoad.md) — 저장 파일 모습, 불러오기 규칙, 변환 API 전체, 호환 검사
- [20. 시작하기](../Manual/20-Setup.md) — 자동 시작 순서, `Configuring`·`Started`·`Stopped`, 출시 상태
- [21. 도감](../Manual/21-Editor-Database.md) — ID 바꾸기·지우기, 검증 메시지 모음
- [04. 인스턴스](../Manual/04-Instances.md) — 원본(`session.Instances`)과 사본(`CopyInstance`)
- [22. 모니터와 디버깅](../Manual/22-Monitor-Debug.md) — 데이터 탭, 저장 명령
