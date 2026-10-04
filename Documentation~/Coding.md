# 게임 코드 쓰는 법

시스템 위에서 게임 로직을 쉽게 짜는 방법. 되돌리기(Undo)·기록(Journal)·저장·조건 감시·멀티플레이 기반은 시스템이 맡고, 게임 코드는 객체 필드를 그냥 읽고 바꾼다.
시스템 API 하나하나의 설명은 `CodingSyntax.md`.

## 용어

| 용어 | 뜻 |
|---|---|
| `Session` (`GameSession`) | 게임 한 판의 실행 전체. Data·Events·Random·Journal·Actions·Abilities를 들고 있다. |
| `Data` (`session.Data`) | 값 저장소(PlayerDataStore). 플레이어 데이터(DB › 플레이어 데이터)가 여기 등록된다. 플레이어 데이터는 전부 여기에 둔다. |
| 플레이어 데이터 클래스 | 플레이어 데이터에 담는 평범한 `[Serializable]` 클래스. 필드·`List`·`Dictionary`를 그냥 고친다. |
| Mirror · Sync · ChangeSet | 시스템이 플레이어 데이터의 사본(Mirror)을 들고 있다가 Sync할 때 바뀐 곳만 기록한다. 그 기록이 ChangeSet(앞으로 = 새 값, 뒤로 = 옛 값)이다. |
| `GameApi` | 게임 코드의 창구. Block 안에서는 `Game` 속성, 씬 코드에서는 `GBibleRuntime.Game`. |
| `GameAction` | 플레이어 입력 하나(이동·사용·구매). 데이터라서 서버 전송·리플레이가 되고, 실행 한 번이 Undo 한 단위. |
| `Block` | 에디터에서 골라 조립하는 로직 단위: `Condition`(조건), `Reward`(보상), `Effect`(효과), `TargetSelector`(대상). |
| `Ability` | DB › 능력. 대상 Block + 효과 Block을 조립한 것(아이템 사용 효과·스킬·버프 발동). `Game.Use`로 쓴다. |

## 예제 (턴제)

```csharp
// 데이터: 평범한 [Serializable] 클래스.
[Serializable]
public class Unit { public string id; public string team; public int x, y, hp; public List<string> buffs = new List<string>(); }

// 판 규칙: 그냥 정적 함수. 장르마다 다른 조회는 GameApi 확장 메서드로.
public static class Board
{
    public static int Distance(Unit a, Unit b) => Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y);
    public static IEnumerable<Unit> Around(this GameApi game, Unit center, int n) =>
        game.All<Unit>().Where(u => u != center && Distance(center, u) <= n);
}

// 플레이어 입력 = 행동. GameAction을 상속하고 Run을 쓴다. 등록 없이 Game.Do로 실행한다.
[Serializable, BlockInfo("이동")]
public class MoveUnit : GameAction
{
    public Unit unit;                                              // 대상은 객체로(기록에는 id만, 실행 직전에 지금 객체로 다시 찾는다)
    public int x, y;
    protected override void Run() { unit.x = x; unit.y = y; }      // 그냥 대입 → 행동 끝 Sync에서 기록
}

// 효과 Block: DB › 능력에서 조립하고 수치를 정한다.
[Serializable, BlockInfo("피해", Category = "전투")]
public class Damage : Effect<Unit>
{
    public int amount = 10;
    protected override void Apply(Unit target) => target.hp -= amount + Game.Random("damage").Range(0, 3);
}

// 대상 Block
[Serializable, BlockInfo("주변 n칸의 적 추가", Category = "대상")]
public class AddEnemiesAround : TargetSelector<Unit>
{
    public int range = 1;
    protected override IEnumerable<Unit> Select(IEnumerable<Unit> targets, Unit user)
    {
        var list = targets.ToList();
        return list.Concat(list.SelectMany(t => Game.Around(t, range))).Where(u => u.team != user.team).Distinct();
    }
}

// 조건 Block: 무엇을 보는지 선언하지 않아도 된다(읽은 값을 시스템이 기록해 감시).
[Serializable, BlockInfo("적 전멸", Category = "전투")]
public class AllEnemiesDown : SimpleCondition
{
    public string team = "enemy";
    protected override bool Check() => Game.All<Unit>().Where(u => u.team == team).All(u => u.hp <= 0);
}

// 씬: 입력을 행동으로, 그리기만.
public class RunManager : MonoBehaviour
{
    Unit _selected;                                                // 행동에 넣을 객체는 그대로 쥐어도 된다(Undo·불러오기 뒤에도 실행 직전에 지금 객체로 바뀐다)
    GameApi Game => GBibleRuntime.Game;

    public void OnClickUnit(Unit u) => _selected = u;
    public void OnClickTile(int x, int y) => Game.Do(new MoveUnit { unit = _selected, x = x, y = y });
    public void OnClickSkill(Unit target) => Game.Use("ability.splash", _selected, target);
    void Update() { foreach (var u in Game.All<Unit>()) { /* 그리기 */ } }
}
```

## 규칙

1. **플레이어 데이터는 플레이어 데이터에, 클래스는 플레이어 데이터 클래스로.** 유닛·판·턴처럼 바뀌는 값은 `[Serializable]` 클래스로 플레이어 데이터에 둔다. MonoBehaviour 필드에 두면 Undo·저장·동기화에서 빠진다.
2. **플레이어 입력은 행동으로.** 이동·사용·구매는 `GameAction`을 상속한 행동(필드 + `protected override void Run()`)으로 만들어 `Game.Do`/`Game.Use`로 실행한다(Undo 한 단위, 서버 전송·리플레이). 대상은 객체로 필드에 넣는다(`string id`가 있는 클래스면 시스템이 id로 기록하고 실행 직전에 지금 객체로 다시 찾는다). DB 원본은 `[DataRef]` 글자 ID로. 행동·효과 안에서는 객체 필드를 그냥 고친다.
3. **로직은 한 번에 끝낸다.** 행동·효과 안에서 기다리지 않는다(코루틴·await). 연출은 끝난 결과(이벤트·Journal)를 보고 따로 재생한다.

그 밖: 난수는 `Game.Random`, 값을 읽으려고 오래 들고 있을 것은 id(`Game.Find`로 다시 찾기), 개체로 찾거나 행동의 대상으로 넣을 클래스에는 `public string id;`.

## 쓰기가 기록되는 방식 (Mirror)

- 게임 코드는 어디서든(행동·효과·씬 코드) 필드·리스트·딕셔너리를 그냥 고친다. 시스템이 행동 시작·끝, Modify 끝, 프레임 끝에 Sync해서 바뀐 곳을 기록한다. 행동 안의 변경은 그 행동의 Undo 단위, 행동 밖의 변경은 행동 밖 변경으로 남는다(그 행동의 Undo에 섞이지 않는다).
- 비용은 데이터 크기에 비례하지만 생성 코드라 가볍다: 객체 2,500개(유닛 500)에서 Sync 한 번 약 0.02ms(Unity 에디터 Mono 약 0.06ms), 바뀐 것이 없으면 할당 0. 객체 10만 개에서 약 0.9ms(에디터 2.5ms) — 실시간 게임은 프레임마다 또는 서버 틱마다.
- 되돌리기는 뒤로 기록을 거꾸로 적용한다. 리스트에서 빠졌던 객체·갈아 끼워진 객체는 원래 인스턴스로 돌아온다(게임 코드가 쥔 참조가 이어진다).
- 같은 객체를 두 곳에서 가리켜도 된다(`slots[0] = items[3]`, 서로 가리키는 객체 포함). 사본도 하나라 변경은 한 번만(먼저 만난 자리의 경로로) 기록되고, 받는 쪽도 같은 인스턴스를 가리킨다(아래 받는 쪽).
- `[Serializable]`이 없는 클래스는 예전 방식(리플렉션으로 통째로 비교, 데이터가 크면 느림)이고 기록에 경고가 남는다.

## 기록되지 않는 것

- 지역 변수, MonoBehaviour·플레이어 데이터 밖의 객체 필드.
- 구조체는 값이라 `u.pos.x = 3`처럼 고쳐도 되지만(필드가 바뀌면 잡힌다), 지역 변수로 꺼낸 복사본을 고치면 원본은 그대로다(C# 규칙).

## Mirror 비교 코드

`GBible.CodeGen`(Unity ILPostProcessor)이 컴파일할 때마다 GBible.Core와 그것을 참조하는 어셈블리의 `[Serializable]`·`[PlayerData]`·`[GameEvent]` 클래스·구조체 안에 비교·쓰기·읽기·적용 메서드(`__gb_*`)와 멤버 읽기·쓰기(`__gb_get`·`__gb_set`)를 만든다. 게임 코드의 필드 쓰기는 고치지 않는다. 게임이 켜질 때 `Mirror.Initialize`가 한 번 모은다(리플렉션은 여기서만). 필요한 패키지: `com.unity.nuget.mono-cecil`(Burst·Collections가 함께 가져온다). Unity 버전을 올리면 코어 테스트 `MirrorTests`(생성 메서드 전체 JIT 검사 포함)로 확인한다.

- 멤버 읽기·쓰기(`FieldAccess`): 조건·필터의 필드 경로, '플레이어 데이터에 주기', '로그에 기록'의 이벤트 값, 이벤트 기록 문장, 필수 값 검사, 저장·불러오기(`DataModel`)가 이것으로 읽고 쓴다(실행 중 리플렉션 없음). 생성 코드가 없는 형식은 리플렉션으로 읽고 한 번 경고한다(`[Serializable]`, 이벤트면 `[GameEvent]`를 붙이면 사라진다).
- 남은 리플렉션(일부러 둠): 게임이 켜질 때(코드 모으기·형식 정보), 개발 도구(모니터의 이벤트 호출), 선택 패키지 연결(TextMeshPro 글자·Spine 색), 익명 객체로 넘긴 텍스트 인자(`Texts.Get(key, new { … })` — 리플렉션 없이 쓰려면 `("이름", 값)` 형태).

## 받는 쪽 (네트워크)

- 보내는 쪽: 받는 쪽이 처음 들어올 때 `session.Data.Snapshot(key)`(지금 값 전체 + 객체 번호), 이후 `session.Data.Synced`로 오는 ChangeSet의 앞으로 기록(Forward). 행동 안의 변경은 행동이 끝날 때 한 묶음으로 온다.
- 받는 쪽: `var receiver = new MirrorReceiver(); var party = (Party)receiver.Load(snapshot);` 이후 `receiver.Apply(party, changes)`. `changes.Snapshot`이 있으면(뿌리 값의 형식이 바뀜) `receiver.Load(changes.Snapshot)`으로 통째로 바꾼다.
- 객체 번호: 사본마다 번호가 있어, 앞으로 기록은 받는 쪽이 이미 가진 객체를 번호로만 가리킨다. 그래서 두 곳에서 가리키는 객체가 받는 쪽에서도 같은 인스턴스다. 받는 쪽은 인스턴스를 약하게 붙잡는다(데이터에서 빠진 객체는 사라져도 된다).
- 처음 모습 없이 `Mirror.Apply`로 적용하면 새 객체는 만들어지지만, 이미 있던 객체를 새 자리에서 가리키는 기록은 읽지 못한다(오류로 알린다).
