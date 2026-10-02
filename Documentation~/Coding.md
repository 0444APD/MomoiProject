# 게임 코드 쓰는 법

시스템 위에서 게임 로직을 쉽게 짜는 방법. 되돌리기(Undo)·기록(Journal)·저장·조건 감시·멀티플레이 기반은 시스템이 맡고, 게임 코드는 객체 필드를 그냥 읽고 바꾼다.
시스템 API 하나하나의 설명은 `CodingSyntax.md`.

## 용어

| 용어 | 뜻 |
|---|---|
| `Session` (`GameSession`) | 게임 한 판의 실행 전체. Data·Events·Random·Journal·Actions·Abilities를 들고 있다. |
| `Data` (`session.Data`) | 값 저장소(PlayerDataStore). 플레이어 데이터(도감 › 플레이어 데이터)가 여기 등록된다. 플레이어 데이터는 전부 여기에 둔다. |
| 플레이어 데이터 클래스 | `[PlayerData]`가 붙은 클래스(클래스 탭이 만드는 데이터 클래스는 자동). 필드에 쓰는 순간 기록된다(위빙). |
| `GameList<T>` · `GameDictionary<K,V>` | 플레이어 데이터 클래스 안의 리스트·딕셔너리. 넣기·빼기도 그 순간 기록된다. |
| `GameApi` | 게임 코드의 창구. Block 안에서는 `Game` 속성, 씬 코드에서는 `GBibleRuntime.Game`. |
| `GameAction` | 플레이어 입력 하나(이동·사용·구매). 데이터라서 서버 전송·리플레이가 되고, 실행 한 번이 Undo 한 단위. |
| `Block` | 에디터에서 골라 조립하는 로직 단위: `Condition`(조건), `Reward`(보상), `Effect`(효과), `TargetSelector`(대상). |
| `Ability` | 도감 › 능력. 대상 Block + 효과 Block을 조립한 것(아이템 사용 효과·스킬·버프 발동). `Game.Use`로 쓴다. |

## 예제 (턴제)

```csharp
// 데이터: 도감 › 클래스에서 만든다([PlayerData]·GameList가 자동으로 붙는다).
[Serializable, PlayerData]
public class Unit { public string id; public string team; public int x, y, hp; public GameList<string> buffs = new GameList<string>(); }

// 판 규칙: 그냥 정적 함수. 장르마다 다른 조회는 GameApi 확장 메서드로.
public static class Board
{
    public static int Distance(Unit a, Unit b) => Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y);
    public static IEnumerable<Unit> Around(this GameApi game, Unit center, int n) =>
        game.All<Unit>().Where(u => u != center && Distance(center, u) <= n);
}

// 플레이어 입력 = 행동. 등록 없이 Game.Do로 실행한다.
[Serializable]
public class MoveUnit : GameAction<Unit>
{
    public int x, y;
    protected override void Run(Unit u) { u.x = x; u.y = y; }      // 그냥 대입 → 쓰는 순간 기록
}

// 효과 Block: 도감 › 능력에서 조립하고 수치를 정한다.
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
    string _selected;                                              // 오래 들고 있을 것은 id로
    GameApi Game => GBibleRuntime.Game;

    public void OnClickUnit(Unit u) => _selected = u.id;
    public void OnClickTile(int x, int y) => Game.Do(new MoveUnit { target = _selected, x = x, y = y });
    public void OnClickSkill(Unit target) => Game.Use("ability.splash", _selected, target);
    void Update() { foreach (var u in Game.All<Unit>()) { /* 그리기 */ } }
}
```

## 규칙

1. **플레이어 데이터는 플레이어 데이터에, 클래스는 플레이어 데이터 클래스로.** 유닛·판·턴처럼 바뀌는 값은 클래스 탭에서 만든 클래스로 플레이어 데이터에 둔다. MonoBehaviour 필드에 두면 Undo·저장·동기화에서 빠진다. 리스트·딕셔너리 필드는 `GameList`·`GameDictionary`.
2. **플레이어 입력은 행동으로.** 이동·사용·구매는 `GameAction<T>`·`SimpleAction`으로 만들어 `Game.Do`/`Game.Use`로 실행한다(Undo 한 단위, 서버 전송·리플레이). 행동·효과 안에서는 객체 필드를 그냥 고친다.
3. **로직은 한 번에 끝낸다.** 행동·효과 안에서 기다리지 않는다(코루틴·await). 연출은 끝난 결과(이벤트·Journal)를 보고 따로 재생한다.

그 밖: 난수는 `Game.Random`, 오래 들고 있을 것은 id(`Game.Find`로 다시 찾기), 개체로 찾을 클래스에는 `public string id;`.

## 쓰기가 기록되는 방식

- **플레이어 데이터 클래스**(권장): 필드에 `=`·`+=`·`++`로 쓰는 순간, `GameList`·`GameDictionary`에 넣고 빼는 순간 기록된다. 어디서 쓰든(행동·효과·씬 코드) 기록되고, 행동 안이면 Undo 단위에 들어간다. 비용은 쓴 만큼(유닛 500개에서 행동 한 번 약 0.3ms).
- **플레이어 데이터 클래스가 아닌 데이터 클래스**(예전 방식): 행동·효과가 꺼낸 값을 끝날 때 통째로 비교해 찾는다. 비용은 데이터 크기만큼(장비 500개에서 행동 한 번 약 5ms). 행동 밖에서 고치면 다음 행동 때 경고. 클래스 상세의 '플레이어 데이터 클래스로 바꾸기'로 옮긴다.

## 기록되지 않는 것(위빙의 한계)

- 지역 변수(`var hp = u.hp; hp = 3;`), MonoBehaviour·일반 클래스 필드: 플레이어 데이터가 아니다.
- 일반 `List<T>`의 Add·Remove: 메서드 호출이라 위빙으로 못 잡는다 → `GameList<T>`.
- 구조체 필드를 제자리에서 고치기(`u.pos.x = 3`): C# 구조체는 복사본이다 → `u.pos = new Pos(3, y)`.
- 리플렉션 쓰기, 미리 컴파일된 DLL 안의 코드: 위빙이 닿지 않는다. 개발 빌드는 이런 변경을 다음 행동 때 경고한다.

## 위빙

`GBible.CodeGen`(Unity ILPostProcessor)이 컴파일할 때마다 GBible.Core를 참조하는 어셈블리의 `[PlayerData]` 필드 쓰기를 기록 호출로 바꾼다. 필요한 패키지: `com.unity.nuget.mono-cecil`(Burst·Collections가 함께 가져온다). Unity 버전을 올리면 위빙이 도는지(코어 테스트 `PlayerDataTests`) 확인한다.
