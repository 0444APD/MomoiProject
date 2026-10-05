# 02. 기획을 코드 구조로

기획서를 받으면 코드를 쓰기 전에 문장마다 "무엇으로 만들지"를 정한다. 어디에 두느냐에 따라 Undo·저장·멀티플레이·기획자 편집이 저절로 되거나 안 된다. 이 장은 카드 던전 기획 한 장을 클래스 목록과 DB 항목 목록으로 옮긴다.

> [!note]- 이 장에서 쓰는 말
> | 말 | 뜻 |
> |---|---|
> | 플레이어 데이터 | 게임 중에 바뀌는 값을 담는 칸. 평범한 `[Serializable]` 클래스를 쓰고, 필드는 그냥 고친다. |
> | Sync | 시스템이 플레이어 데이터의 사본(Mirror)과 지금 값을 비교해 바뀐 곳만 기록하는 동작. 이 기록이 Undo·저장·멀티플레이 전송의 바탕이다. |
> | 구역 | 플레이어 데이터가 들어가는 저장 파일. 세션 구역(이번 판, `Session`)과 메타 구역(프로필, `Meta`) 둘뿐이다. |
> | 인스턴스 | 클래스의 원본 값 하나(타격 카드, 고블린 …)를 DB에 채워 둔 항목. 게임은 원본을 읽기만 하고(`session.Instances`), 바뀌는 값은 판 데이터에 따로 둔다. 통째로 고칠 사본이 필요하면 `CopyInstance`로 깊은 사본을 만든다. |
> | 이벤트 | "방금 일어난 일"을 값과 함께 담은 클래스. 게임 코드가 쏘고(`Publish`), 로그·도전과제·화면이 받는다. |
> | 행동 | 플레이어 입력 하나를 담은 데이터 클래스(`GameAction`). 행동 하나 = Undo 한 단위 = 네트워크 한 묶음. |
> | 키워드 | 등급·속성처럼 정해진 목록에서 고르는 글자 값. |
> | Block | 기획자가 DB에서 골라 조립하는 로직 단위. 대상 Block(누구에게)·효과 Block(무엇을)·조건 Block(언제만)·코딩한 결과가 있다. |
> | Trigger · Outcome | DB 항목의 "언제(Trigger) → 무엇을(Outcome, 결과)" 두 칸. 로그·도전과제·능력·사용 기능이 같은 칸을 쓴다. |
> | DB 종류 | `GameData`를 상속해 DB에 새 탭을 만드는 클래스(유물 등). 기본 기능에 맞지 않는 새 종류에 쓴다. |

---

## 1. 기획을 읽는 순서

기획서를 규칙 문장 하나에 번호 하나로 줄인다. 그다음 문장에서 아래를 차례로 뽑는다.

1. **명사** → 바뀌는 값은 플레이어 데이터, 기획자가 채우는 원본은 인스턴스, 화면만의 것은 MonoBehaviour(2절)
2. **남는 것 / 버리는 것** → 판(`run`)과 프로필(`profile`)(3절)
3. **동사** → 행동(4절)
4. **"~하면 / ~할 때"** → 이벤트(5절)
5. **고르는 값**(등급·종류·태그) → 키워드(6절)
6. **계속 늘어날 것** → 인스턴스·기본 기능·DB 종류(7절)
7. **효과** → Block, 한 항목만의 예외는 `[BindTo]`(8절)
8. **찾거나 가리킬 객체** → `id`(9절)
9. 클래스 목록과 DB 항목 목록으로 적어 팀과 맞춘다(10절)

모든 단계를 꿰는 기준은 하나다.

> **판 데이터에는 ID와 바뀌는 값만 둔다. 고정 값(이름·비용·그림·효과·공격 순서)은 원본에서 읽는다.**

> [!note]- 카드 던전 기획 한 장(아래에서 "기획 N번"으로 부른다)
> 1. 플레이어는 주인공 '모모이'로 던전을 오른다. 한 판(런)은 1층에서 시작하고, 주인공이 쓰러지면 끝난다.
> 2. 판을 시작하면 체력 70, 골드 99, 시작 덱(타격 5장·수비 4장·화염구 1장), 시작 유물 '불타는 피', 회복 물약 1개를 받는다.
> 3. 층마다 전투가 있다. 적은 고블린·슬라임·드래곤 같은 몬스터이고, 몬스터마다 턴별 공격 순서가 정해져 있다.
> 4. 턴이 시작되면 에너지 3을 받고 카드 5장을 뽑는다. 뽑을 카드가 없으면 버린 카드를 섞어 다시 쓴다.
> 5. 카드에는 비용·등급·속성·종류(공격·스킬·파워)와 그림이 있다. 카드를 내면 에너지를 쓰고 효과가 난다(피해, 방어도, 회복, 화상 같은 상태). 일부 카드는 강화하면 다른 카드가 된다.
> 6. 방어도는 그 턴 동안 피해를 먼저 막는다.
> 7. 턴을 끝내면 적들이 공격하고, 손패를 버리고, 다음 턴이 시작된다.
> 8. 적을 모두 쓰러뜨리면 골드 10~20을 얻고 다음 층으로 간다.
> 9. 상점에서 골드로 카드를 산다. 물약은 3칸까지 들고 다니며 마시면 효과가 난다.
> 10. 유물은 가지고 있으면 어떤 순간에 저절로 효과를 낸다(닻: 전투 첫 턴에 방어도, 불타는 피: 전투에 이기면 체력 회복). 기획자가 유물을 계속 더한다.
> 11. 판이 끝나도 가장 높이 오른 층, 모은 골드 합계, 해금한 카드가 남는다. 처음 전투에 이기면 '연쇄 번개' 카드가 해금된다. 연쇄 번개는 적마다 차례로 3, 4, 5 … 피해를 준다.
> 12. 처치 수·한 턴 최대 피해·얻은 골드 합계를 기록하고, 한 턴에 30 이상 피해를 주면 도전과제를 달성한다.
> 13. 잘못 낸 카드는 되돌릴 수 있다.

> [!note]- 판단표 한눈에(위에서부터 처음 맞는 줄)
> | 기획에서 이런 것이 보이면 | 이것으로 만든다 | 카드 던전 예 |
> |---|---|---|
> | 기획자가 수치를 채우는 원본이 여러 개(같은 모양, 다른 값) | 클래스 + 인스턴스(DB › 인스턴스) | 카드·몬스터·물약 원본 |
> | 게임 중에 바뀌고, 되돌리거나·저장하거나·조건으로 검사할 값 | 플레이어 데이터. 원본의 고정 값은 복사하지 않고 원본 ID만 둔다 | `RunState`, `Profile`, 덱의 한 장 `DeckCard` |
> | 다른 값에서 계산되는 값 | 저장하지 않는다(읽기 속성이나 함수) | `Fighter.IsDead` |
> | 매 프레임 바뀌는 값, 화면 상태(위치·애니메이션·끌고 있는 카드·열린 창) | MonoBehaviour 필드(Client) | 카드를 끄는 손 위치 |
> | 플레이어 입력 하나, 또는 규칙이 한 걸음 나아가는 단위 | 행동(`GameAction`) | 카드 내기, 턴 끝 |
> | "~하면 / ~할 때" 다른 기능이 반응하는 순간 | 이벤트(`[GameEvent]`) | 턴 끝, 전투 승리 |
> | 정해진 목록에서 고르는 글자 값 | 키워드(`[KeywordGroup]` + `[Keyword]`) | 등급·속성·카드 종류 |
> | 대상에게 걸려 몇 턴 동안 효과를 내는 것(버프·디버프) | 상태 효과(DB › 상태 효과, 코드 없음) | 화상, 독 |
> | 한 번 이루면 끝나는 목표 | 도전과제(DB, 코드 없음 또는 `[BindTo]`) | 첫 승리, 한 턴 30 피해 |
> | 이벤트를 받을 때마다 쌓는 숫자 | 로그(DB) | 처치 수, 한 턴 최대 피해 |
> | 쓰는 순간 효과가 나는 원본 | 클래스에 사용 기능 필드(`Usable`) | 카드·물약 |
> | 가지고 있는 동안 어떤 순간마다 발동(유물·특성·조커) | DB › 패시브(코드 없음) | 유물 |
> | 항목마다 "언제 → 무엇"을 기획자가 조립하고, 위 기능과 규칙이 다른 새 종류(한 번만·하루 N번·단계) | DB 종류(`GameData` + `[Database]`) + Trigger 감시 | 퀘스트·일일 미션 |
> | 기획자가 조립할 동작 하나(누구에게 / 무엇을 / 언제만) | Block(`TargetSelector<T>`·`Effect<T>`·`SimpleCondition`·`Reward`) | 피해, 모든 적 |
> | 그 항목 하나에만 있는 예외 로직 | `[BindTo]` 함수 | 연쇄 번개 카드 |
> | 여러 행동·Block이 같이 쓰는 게임 규칙 계산 | 평범한 static 함수 | 피해 계산, 덱 섞기 |

> [!note]- 왜 원본 ID만 두나
> 덱에 든 카드 한 장이 이름·비용·그림·효과까지 들고 있으면, 같은 값이 덱의 장 수만큼 판 데이터에 복사된다. 그 복사본이 모두 Sync 대상이 되고, 판 저장 파일에 들어가고, 멀티플레이에서는 처음 맞출 때 통째로 전송된다. 그림 같은 에셋 참조는 판 저장에 담기지도 않는다.
>
> 원본 ID와 이 장만의 바뀌는 값(강화 여부)만 들면 저장·동기화가 작아지고, 기획자가 원본의 비용을 고치면 이미 덱에 든 카드에도 바로 반영된다. 원본은 `session.Instances`로 복사 없이 읽는다. 게임이 켜질 때 만든 표에서 찾으므로 자주 읽어도 빠르다. 원본은 모든 판이 같이 쓰는 값이라 게임 중에 고치지 않는다.

> [!note]- 헷갈리기 쉬운 경계
> - **원본과 판 데이터.** "고블린"이라는 몬스터 정의는 바뀌지 않으므로 인스턴스다. 전투에 나온 고블린 한 마리의 지금 체력은 바뀌므로 플레이어 데이터다. 같은 개념이 두 클래스로 갈라지는 것이 정상이다(`MonsterData` → `Enemy`, `Card` → `DeckCard`).
> - **이벤트와 행동.** 행동은 "하라"(입력), 이벤트는 "했다"(결과)다. `PlayCard` 행동이 실행되면 그 안에서 `CardPlayed` 이벤트를 쏜다. 로그·도전과제는 행동이 아니라 이벤트를 받는다.
> - **매번 일어나는 규칙과 항목마다 다른 규칙.** "적이 쓰러지면 전투에서 뺀다"처럼 늘 같은 규칙은 행동·규칙 함수에 바로 쓴다. "이 유물은 첫 턴에 방어도"처럼 항목마다 다르고 기획자가 바꾸고 싶은 것만 Trigger·결과로 둔다.
> - **키워드와 enum.** 기획자가 목록을 늘리고 이름을 고르는 값은 키워드다(6절).

## 2. 데이터: 바뀌는 값과 원본

명사를 세 칸에 나눈다.

- **게임 중에 바뀐다** → 플레이어 데이터 클래스(`[Serializable]`, Rules)
- **기획자가 채우는 원본** → 클래스 + DB › 인스턴스(Rules)
- **화면만의 것** → MonoBehaviour 필드(Client)

한 명사가 두 칸에 걸치면 클래스를 둘로 나눈다. 카드의 이름·비용·효과는 원본 `Card`에, "내 덱의 이 한 장은 강화했다"는 판 데이터 `DeckCard`에 둔다.

```csharp
[Serializable]
public class Card                      // 원본(DB › 인스턴스). 읽기만 한다
{
    public string title;
    public int cost = 1;
    [Keyword(typeof(Rarity))] public string rarity = Rarity.Common;
    [Keyword(typeof(Element))] public string element = Element.Neutral;
    [Keyword(typeof(CardTag))] public List<string> tags = new List<string>();
    public Sprite art;
    public bool upgradable;
    [ShowIf(nameof(upgradable)), DataRef(typeof(Card))] public string upgradeTo;
    [Hide] public int balanceScore;
    [SerializeReference] public Usable usable;   // 쓰는 순간의 효과(기능 = 필드 하나)
}

[Serializable]
public class DeckCard                  // 판 데이터: 덱에 든 한 장
{
    public string id;                                       // 장마다 다른 id(Game.NewId("card"))
    [DataRef(typeof(Card))] public string card;     // 원본 인스턴스 ID(Card 인스턴스만 고를 수 있다)
    public bool upgraded;
}
```

원본은 규칙 도우미 하나로 읽는다. 행동·Block·화면이 모두 이 함수를 쓴다.

```csharp
public static Card CardData(GameApi game, string card) => game.Session.Instances?.Invoke(card) as Card;
```

> [!note]- 카드 던전의 명사 나누기
> | 게임 중에 바뀐다(플레이어 데이터) | 기획자가 채우는 원본(인스턴스) | 화면만의 것(MonoBehaviour) |
> |---|---|---|
> | 주인공의 체력·방어도, 걸린 상태 효과(라이브러리가 `$statuses`에 둔다), 적의 체력·공격 차례, 층, 골드, 덱(어느 카드가 몇 장, 강화 여부), 손패·뽑을 더미·버린 더미, 에너지, 턴, 가진 유물, 물약 칸, 최고 층, 누적 골드, 해금 카드 | 카드(이름·비용·등급·그림·효과), 몬스터(이름·최대 체력·공격 순서·초상화), 물약(이름·겹치는 수·효과) | 카드를 끄는 위치, 공격 애니메이션 |
>
> 원본 클래스에는 `id` 필드가 없다. 원본은 DB 항목 ID로 부르고, 게임 중에 `Find`로 찾을 일이 없다. 몬스터(`MonsterOf`)·물약(`PotionOf`)도 `CardData`와 같은 모양으로 읽는다. 돌려받은 원본은 읽기만 한다.

> [!note]- 판 데이터 클래스(주인공·적·전투)
> 주인공과 적은 체력·방어도를 함께 가지므로 공통 부분 `Fighter`를 둔다.
> ```csharp
> [Serializable]
> public class Fighter
> {
>     public string id;                 // Game.Find로 찾는 id(판 안에서 유일)
>     public string name;
>     public int hp;
>     public int maxHp;
>     public int block;                 // 이번 턴 방어도
>     // 화상 같은 상태 효과는 여기 두지 않는다: 걸린 것은 라이브러리가 플레이어 데이터 $statuses에 id로 둔다(DB › 상태 효과).
>
>     public bool IsDead => hp <= 0;
> }
>
> [Serializable]
> public class Hero : Fighter
> {
>     public int energyPerTurn = 3;
>     public int drawPerTurn = 5;
> }
>
> [Serializable]
> public class Enemy : Fighter
> {
>     public string monster;            // 원본 인스턴스 ID(예: "goblin")
>     public int intentIndex;           // 공격 패턴에서 이번 턴 차례
> }
>
> [Serializable]
> public class Battle
> {
>     public int turn;
>     public int energy;
>     public List<Enemy> enemies = new List<Enemy>();
>     public List<string> drawPile = new List<string>();   // 덱 카드(DeckCard) id
>     public List<string> hand = new List<string>();
>     public List<string> discard = new List<string>();
>     public int damageThisTurn;                            // 로그·도전과제용 집계
> }
> ```
> - `IsDead`는 체력에서 계산되므로 필드가 아니라 속성이다.
> - 걸린 상태 효과(화상 2턴 남음 …)는 `Fighter`의 필드가 아니다. 상태 효과 걸기가 플레이어 데이터 `$statuses`에 "누구에게·무엇이·몇 중첩·몇 번 남음"을 id로 적고, 화면은 `Game.Statuses(fighter)`로 읽는다([04장](04-AddingContent.md) 4절).
> - 적은 원본 몬스터의 ID(`monster`)와 공격 순서의 지금 차례(`intentIndex`)만 더 가진다. 공격 순서 자체는 원본에서 읽는다. 체력은 전투를 시작할 때 원본의 최대 체력으로 채운다.
> - 전투 중에만 있는 값은 `Battle`로 묶어 `RunState.battle`에 둔다(01장 3절). 전투 밖이면 `null`이다. 손패·더미는 카드 id 리스트다.
> - `damageThisTurn`은 기획 12번(한 턴 최대 피해)을 위한 값이다. 턴 끝 이벤트에 실어 보내려면 턴 동안 모아 둘 곳이 필요하다.

> [!note]- 클래스 하나 = 개념 하나, 기능은 필드
> - 카드를 "공격 카드 / 스킬 카드" 자식 클래스로 나누지 않는다. 종류는 키워드 필드(`tags`), 쓰는 효과는 사용 기능 필드(`usable`)가 맡는다. 새 종류가 생겨도 클래스가 늘지 않고, 기획자는 DB에서 값만 고른다.
> - 붙었다 떨어지는 기능은 비어 있을 수 있는 `[SerializeReference]` 필드 하나로 둔다. `null`이면 그 기능이 없다.
> - 상속은 "서로 다른 개념이 같은 부분을 나누고, 그 부분에 같은 규칙(Block)이 돌아야 할 때"만 쓴다. `Hero`와 `Enemy`는 다른 개념이지만 둘 다 피해·방어도를 받으므로 `Fighter`를 두고, 효과 Block은 `Effect<Fighter>` 하나로 둘 다에 쓴다.
> - 다른 값에서 계산되는 값은 필드로 두지 않는다. 두 값이 어긋날 수 있고 Sync할 것이 는다.

> [!note]- 원본 필드 꾸미기
> 강화 대상은 `[ShowIf]`로 강화 가능할 때만 보이고, `[DataRef]` 덕분에 인스턴스 목록에서 고르는 칸이 된다. 도구가 채우는 값은 `[Hide]`로 기획자 화면에서 숨긴다. 기획자 화면 다듬기는 [05장](05-WorkingWithDesigners.md).

> [!note]- 다른 장르라면
> - **턴제 전술**: 전장(`Board`)과 유닛 리스트가 플레이어 데이터, 유닛 종류(검사·궁수)가 인스턴스다. 유닛의 칸 좌표는 턴마다 한 번 바뀌므로 플레이어 데이터에 두고, 이동 애니메이션 중의 화면 위치는 MonoBehaviour에 둔다.
> - **실시간 액션**: 매 프레임 바뀌는 위치·속도는 플레이어 데이터에 넣지 않는다(매 프레임 기록이 쌓인다). 체력·쿨다운 시각·인벤토리처럼 사건으로 바뀌는 값만 두고, 쿨다운은 `Time.time`이 아니라 게임 시계(`session.Clock.Time`) 기준 시각으로 적는다([07장](07-GenreRecipes.md)).

## 3. 구역: 판과 프로필

판이 끝나면 버릴 값은 `run`, 남길 값은 `profile`에 둔다. 두 항목은 시작 설정이 이미 만들어 두었다(01장 3절). 필드를 어느 클래스에 둘지만 정한다.

| 판이 끝나면 | 플레이어 데이터 | 저장 파일 | 카드 던전 |
|---|---|---|---|
| 버린다 | `run`(Session, `RunState`) | `run.json`(이어 하기) | 층·골드·주인공·덱·전투·유물·물약 |
| 남긴다 | `profile`(Meta, `Profile`) | `profile.json` | 최고 층·누적 골드·해금 카드 |

- 한 클래스에 판 값과 프로필 값을 섞지 않는다. 플레이어 데이터 하나는 구역 하나에만 들어간다.
- 남길 값은 판을 끝내는 행동(`EndRun`)이 프로필로 옮긴다.

```csharp
var run = Game.Data().Run;
var profile = Game.Data().Profile;
profile.bestFloor = Math.Max(profile.bestFloor, run.floor);
profile.totalGold += run.gold;
```

> [!note]- 판이 끝났는지는 어떻게 아나
> 따로 플래그를 두지 않고 이미 있는 값에서 계산한다. `EndRun`이 층을 0으로 돌리고, `RunState`의 읽기 속성 `IsActive`(층이 0보다 큰가)가 "진행 중인 판이 있는가"를 답한다.
>
> `EndRun`은 판 끝 이벤트 `RunEnded`도 쏜다. DB › 시작 파일 › Bootstrap의 **판 끝 이벤트**를 `RunEnded`로 정해 두면, 받는 순간 판 저장이 지워지고 프로필이 바로 저장된다. 이어 하기 쪽은 [03장](03-GameLoop.md) 3절.

> [!note]- 로그·도전과제의 구역
> 로그는 범위(이번 판 / 누적)를 DB에서 따로 고르고, 도전과제 달성은 늘 메타 구역이다. 이 둘은 클래스로 만들지 않는다.

> [!note]- 다른 장르라면: 수집형
> 메타 구역이 주인공이다. 보유 캐릭터·재화·강화 단계가 프로필(Meta)이고, 한 번의 전투가 세션 구역이다. 퀘스트·일일 미션처럼 "언제 → 보상"을 기획자가 계속 더하는 것은 유물처럼 DB 종류로 만든다(7절, [17. 프로젝트 고유 기능](../../Manual/17-Custom-Features.md)).

## 4. 행동: 동사

플레이어 입력 하나, 또는 규칙이 한 걸음 나아가는 단위를 행동으로 만든다.

- `GameAction`을 상속하고, 필요한 정보를 필드로 두고, `Run()`에 규칙을 쓴다.
- 대상 객체(덱 카드·적)는 **객체 그대로** 필드에 넣는다. 없으면 `null`로 둔다.

```csharp
[Serializable, BlockInfo("카드 내기")]
public class PlayCard : GameAction
{
    public DeckCard card;     // 낼 카드(덱 카드)
    public Fighter target;    // 고른 적(없으면 null)

    protected override void Run()
    {
        // … 본문은 03장에서 본다
    }
}
```

부르는 쪽은 `Game.Do(new PlayCard { card = deckCard, target = enemy })`다. 시스템은 대상 필드를 기록·전송할 때 id만 남기고, `Run` 직전에 그 id로 지금 데이터에서 객체를 다시 찾아 필드에 넣는다. 그래서 Undo·판 불러오기 뒤에 버튼이 옛 객체를 쥐고 있어도, 멀티에서 id만 건너가도 `Run` 안의 `card`·`target`은 늘 지금 데이터의 객체다.

대상 클래스의 조건은 두 가지다. `public string id;`가 있고, 플레이어 데이터 안에 들어 있다(9절).

> [!note]- 대상 필드 자세히
> - **대상으로 다뤄지는 필드**: 형식이 `string id`가 있는 클래스(`DeckCard`·`Fighter`·`Enemy` …)인 필드. 개수 제한이 없고 `List<Fighter> targets` 같은 리스트·배열도 된다.
> - **id 겹침**: 같은 형식 안에서 id가 겹치지 않아야 한다. 부모 형식(`Fighter`)으로 찾으면 그 부모를 나누는 형식끼리(`Hero`·`Enemy`)도 겹치지 않아야 한다. 데이터 안의 깊이는 상관없다.
> - **값으로 복사되는 필드**: 숫자·글자·bool·enum, id가 없는 클래스. DB 원본을 가리킬 때는 객체가 아니라 `[DataRef]` 원본 ID 글자다(원본은 플레이어 데이터가 아니라 찾기 대상이 아니다).
> - **못 찾으면**(죽은 적 등) "Fighter 'enemy2'을(를) 찾을 수 없습니다(행동 'PlayCard'의 'target')"로 행동이 실패하고 아무것도 바뀌지 않는다. id가 빈 새 객체를 넣어도 실패한다. 새 객체는 행동 안에서 `Game.NewId(접두어)`로 id를 주어 만든다.
> - **넣지 않는 것**: Unity 객체(에셋·컴포넌트)·델리게이트·플레이어 데이터 밖의 객체.
> - **타임라인 글**: `Describe`를 쓰지 않으면 "Block 이름 (대상 id들)"로 나온다(예: "카드 내기 (card7, enemy2)"). 바꾸고 싶을 때만 `Describe`를 덮어쓴다.
> - **처리기를 밖에서 등록**(`session.Actions.Register<T>(처리기)`)하는 방식도 있다. 그때도 실행 직전에 대상 필드를 다시 찾는다. `Run`도 처리기도 없으면 "행동 'X'의 처리기가 없습니다"로 실패한다([12장](12-Appendix.md) 3-1).

> [!note]- 카드 던전의 행동 목록
> | 기획 | 행동 | 필드 |
> |---|---|---|
> | 판을 시작한다(2) | `StartRun` | 없음 |
> | 전투를 연다(3) | `StartBattle` | 몬스터 ID 리스트 |
> | 카드를 뽑는다(4) | `DrawCards` | 장 수 |
> | 카드를 낸다(5) | `PlayCard` | 덱 카드 `card`, 고른 적 `target`(객체) |
> | 턴을 끝낸다(7) | `EndTurn` | 없음 |
> | 전투에 이긴다(8) | `WinBattle` | 없음 |
> | 카드를 산다(9) | `BuyCard` | 원본 ID·가격 |
> | 물약을 마신다(9) | `DrinkPotion` | 물약 원본 ID(`[DataRef]`)·대상(`Fighter`, 없으면 null) |
> | 판을 끝낸다(1) | `EndRun` | 승리 여부 |

> [!note]- 플레이어가 누르지 않는 것도 행동으로
> `StartBattle`·`DrawCards`·`WinBattle`·`EndRun`은 플레이어가 직접 누르지 않아도 행동으로 만든다. 다른 행동 안에서 불리면(`Game.Do`) 바깥 행동의 일부로 그 자리에서 실행되므로, 혼자 쓸 때(디버그 명령·모니터)와 다른 행동 안에서 쓸 때 모두 같은 코드다. 이어지는 방식은 [03장](03-GameLoop.md) 5절.

## 5. 이벤트: "~하면 / ~할 때"

다른 기능(로그·도전과제·유물·연출)이 반응하는 순간마다 이벤트 클래스를 만든다. 받는 쪽이 하나라도 있으면 만든다.

- 조건·로그가 읽을 값을 필드로 둔다. 로그의 "반영할 값"과 Trigger의 값 조건은 이벤트 필드 목록에서 고른다.
- 객체를 통째로 넣지 않고 id를 넣는다.

```csharp
[GameEvent("턴 끝")]
public class TurnEnded
{
    public int turn;
    public int damage;        // 이번 턴에 준 피해 합(로그 "한 턴 최대 피해"가 읽는다)
}
```

> [!note]- 카드 던전의 이벤트 목록
> | 순간 | 받는 쪽 | 이벤트 | 실어 나르는 값 |
> |---|---|---|---|
> | 적이 쓰러지면(12) | 로그 처치 수 | `EnemyKilled` "적 처치" | 처치한 쪽 id, 적 id, 몬스터 ID |
> | 턴이 시작되면(10) | 유물 닻 | `TurnStarted` "턴 시작" | 턴 번호 |
> | 턴이 끝나면(12) | 로그 한 턴 최대 피해 | `TurnEnded` "턴 끝" | 턴 번호, 이번 턴 피해 합 |
> | 전투에 이기면(10, 11) | 유물 불타는 피, 도전과제 첫 승리 | `BattleWon` "전투 승리" | 층, 걸린 턴 수 |
> | 골드를 얻으면(12) | 로그 얻은 골드 합계 | `GoldGained` "골드 획득" | 양 |
> | 판이 끝나면(1, 11) | 판 저장 정리(DB › Bootstrap의 판 끝 이벤트), 연출 | `RunEnded` "판 끝" | 층, 승리 여부 |
> | 카드를 내면 · 피해를 주면 | 연출, 앞으로 생길 로그·도전과제 | `CardPlayed` · `DamageDealt` | id·수치 |

> [!note]- `[GameEvent]`가 하는 일, 상속하지 않는 이유
> - `[GameEvent("표시 이름")]`을 붙이면 DB·모니터 목록에 이 이름으로 나오고, 컴파일할 때 값 읽기 코드가 생겨 조건·로그가 실행 중 리플렉션 없이 값을 읽는다.
> - 이벤트는 상속하지 않는다. 이벤트 받기와 `Subscribe<T>`·`SubscribeAfterAction<T>`는 형식이 정확히 같을 때만 받는다. 종류를 나누려면 필드를 두고 값 조건으로 거른다.

## 6. 키워드: 고르는 값

등급·속성·카드 종류처럼 목록에서 고르는 값은 키워드다. 그룹 하나 = static class 하나, 키워드 하나 = `const string` 하나.

```csharp
[KeywordGroup("등급")]
public static class Rarity
{
    public const string Common = "common";
    public const string Rare = "rare";
    public const string Legendary = "legendary";
}
```

필드에 `[Keyword(typeof(Rarity))]`를 붙인다. `string`은 하나, `List<string>`은 여러 개를 고른다. 게임 코드는 상수로 비교한다(`card.rarity == Rarity.Legendary`).

> [!note]- 키워드 자세히
> - 코드가 곧 키워드 목록이다. DB › 키워드 탭은 이 파일을 읽기만 한다.
> - enum 대신 키워드를 쓰는 이유: 저장 값이 글자라 목록 순서가 바뀌어도 저장이 깨지지 않는다.
> - 카드 종류는 여러 개일 수 있으므로 `tags`는 리스트다.
> - 화상·빙결 같은 "상태"는 이름만 고르는 값이 아니라 중첩·지속·턴마다 효과가 있으므로 키워드가 아니라 DB › 상태 효과의 항목으로 둔다(8절).

## 7. 기획자가 계속 더할 것

"계속 늘어난다"고 할 만한 것을 찾아 모양에 따라 나눈다.

| 늘어나는 모양 | 만드는 것 | 카드 던전 |
|---|---|---|
| 같은 모양의 원본 | 클래스만. 항목은 기획자가 DB › 인스턴스에서 더한다 | 카드·몬스터·물약 |
| 쓰는 순간 효과가 나는 원본 | 클래스에 `[SerializeReference] public Usable usable;` | 카드·물약 |
| 한 번 이루는 목표, 쌓는 숫자 | 없음. DB의 도전과제·로그. 필요한 것은 이벤트(5절)뿐 | 첫 승리·한 턴 30 피해, 처치 수·얻은 골드 |
| 가지고 있는 동안 발동 | 없음. DB › 패시브 | 유물 |
| 어디에도 맞지 않는 새 종류 | DB 종류(`GameData` + `[Database]`) + 런타임 감시 | (카드 던전에서는 DB 종류를 만드는 법을 보이려고 유물로 만든다) |

> [!note]- 사용 기능이 붙은 인스턴스
> 사용 기능이 붙은 인스턴스는 게임이 켜질 때 인스턴스 ID로 등록되어 `Game.Use(인스턴스 ID, 사용자, 대상들)`로 쓴다. 효과는 기획자가 Block으로 조립한다.

> [!note]- 유물은 패시브로, 카드 던전은 왜 DB 종류로 만드나
> 유물은 "가지고 있는 동안, 어떤 순간마다(반복), 무언가 한다"(기획 10번). 이 규칙은 기본 종류 DB › 패시브가 그대로 맡는다(코드 없음, [04장](04-AddingContent.md) 8절 첫 접힌 칸). 새 프로젝트에서는 패시브로 만든다.
> 카드 던전은 패시브에 없는 규칙(한 번만·하루 N번·단계·유물만의 칸)이 생길 때를 대비해 DB 종류를 직접 만드는 법을 유물로 보인다. 도전과제는 한 번이라 맞지 않고, 로그는 숫자만 쌓고, 사용 기능은 쓰는 순간에만 발동한다. 그래서 `GameData`를 상속한 `RelicData`에 Trigger 칸과 결과 칸을 두면 DB에 '유물' 탭이 생기고 기본 기능과 같은 편집 화면을 쓴다. 런타임에서 감시하고 결과를 실행하는 코드는 프로젝트가 쓴다. 등록 함수(`RelicRules.Register`)는 Rules에, Unity 시작에 거는 연결은 `Bootstrap.Started` 한 줄로 둔다([04장](04-AddingContent.md)).

## 8. 효과: Block · `[BindTo]` · 규칙 함수

카드·물약·유물의 효과를 동작 단위로 쪼갠다.

- **조립할 동작 하나** → Block. 효과(무엇을)와 대상(누구에게)으로 나눈다.
- **그 항목 하나에만 있는 예외** → `[BindTo]` 함수로 그 항목의 결과를 쓴다.
- **여럿이 같이 쓰는 계산** → 평범한 static 함수(`Rules.Damage`, `Rules.Shuffle`).
- **핵심 규칙 함수**(피해·회복·죽음·비용·뽑기)는 일을 하기 직전에 가로채기 자리(`Game.Intercept`)를 둔다. "받는 피해 −5" 같은 효과를 고치는 효과는 나중에 DB 항목으로 더하고, 규칙 함수는 고치지 않는다.
- **캐릭터 능력치**(공격력·방어력)는 숫자 필드 + Modifier다. 장비·버프는 효과 `능력치 보정`이나 `Game.AddModifier`로 붙이고, 읽을 때는 `Game.Attribute`.
- **몇 턴 동안 이어지는 효과**(화상·독·보호막)는 Block을 만들지 않는다. DB › 상태 효과에 항목으로 두고, 카드에는 기본 제공 효과 `상태 효과 걸기`를 둔다.

자세히: 매뉴얼 [28. 능력치·Modifier·가로채기·비용](../../Manual/28-Attributes-Intercepts.md), [29. 상태 효과·예약·선택 대기](../../Manual/29-Statuses-Schedules-Choices.md).

| 종류  | Block                                                          | 기획        |
| --- | -------------------------------------------------------------- | --------- |
| 효과  | 피해 `DealDamage`, 방어도 `GainBlock`, 회복 `Heal` (화상은 기본 제공 `상태 효과 걸기`) | 5번        |
| 대상  | 모든 적 `AllEnemies`, 무작위 적 `RandomEnemy`                         | 범위·무작위 공격 |

> [!note]- 코드 없이 조립되는 카드
> 대상을 고르지 않으면 플레이어가 고른 대상(카드를 끌어 놓은 적)이 그대로 쓰이고, 기본 제공 `사용자 자신`은 카드를 낸 주인공을 고른다. 그래서 타격(고른 적에게 피해)·수비(나에게 방어도)·화염구(고른 적에게 피해 + `상태 효과 걸기` 화상)는 코드 없이 조립된다. 화상 자체(턴 끝마다 피해, 2턴)도 DB › 상태 효과의 항목이라 코드가 없다([04장](04-AddingContent.md) 4절).

`[BindTo(DB 항목 ID)]`를 static 함수에 붙이면, 그 항목의 결과 칸 대신 이 함수가 돈다(DB에서 결과 칸은 잠기고 함수 이름이 보인다).

```csharp
public static class CardCode
{
    // 연쇄 번개: 적마다 차례로 3, 4, 5 … 피해
    [BindTo(GameIds.Instance.ChainLightning)]
    static void ChainLightning(TriggerContext c)
    {
        var hero = c.User as Fighter;                 // 컨텍스트: 카드를 쓴 쪽
        var damage = 3;
        foreach (var enemy in Rules.LivingEnemies(c.Game).ToList()) Rules.Damage(c.Game, hero, enemy, damage++);
    }

    // 첫 승리 도전과제: 연쇄 번개 해금. 첫 매개변수로 받을 이벤트 형식을 적으면 그 이벤트 값을 바로 쓴다.
    [BindTo(GameIds.Achievement.FirstWin)]
    static void FirstWin(BattleWon e, TriggerContext c)
    {
        var profile = c.Game.Data().Profile;
        if (!profile.unlocked.Contains(GameIds.Instance.ChainLightning)) profile.unlocked.Add(GameIds.Instance.ChainLightning);
    }
}
```

> [!note]- `[BindTo]`로 쓰는 것
> - 연쇄 번개(11번) "적마다 차례로 3, 4, 5 … 피해": 대상마다 수치가 달라지는 동작은 그 카드에만 있으므로 Block을 만들지 않는다.
> - 첫 승리 도전과제의 결과 "연쇄 번개 해금"(11번).
> - 유물 불타는 피([04장](04-AddingContent.md) 9절).
> - 함수 모양은 넷 중 하나: `()`, `(이벤트)`, `(TriggerContext)`, `(이벤트, TriggerContext)`. 컨텍스트(`TriggerContext`)에는 발동한 순간의 정보(쓴 쪽 `User`, 고른 대상 `Picked`, 받은 이벤트 `Event`, `Game`)가 들어 있다.
> - ID가 DB의 어느 항목에도 없으면(오타·지운 항목) 게임을 켤 때 콘솔에 경고가 뜬다. 자세한 내용은 [04장](04-AddingContent.md) 5절, 매뉴얼 [13. BindTo](../../Manual/13-BindTo.md).

> [!note]- 규칙 함수
> 피해 계산(방어도가 먼저 깎임, 적이 쓰러지면 빼고 이벤트를 쏨)과 덱 섞기는 행동·Block·`[BindTo]` 함수가 모두 쓰므로 한곳에 모은다. 래퍼나 특별한 문법 없이 그냥 C#이다.
>
> `Rules.Damage`는 피해를 주기 직전에 의도 `DamageIntent`를 `Game.Intercept`로 통과시킨다(가로채기 자리). 유물 "가시 갑옷"([04장](04-AddingContent.md) 9절)이 여기서 피해를 줄인다. 이 습관은 [09장](09-Habits.md) 1절.

> [!note]- 효과의 문법 — 기획 문장을 쪼개는 틀
> 효과 문장은 **언제 + 누구에게 + 무엇을 + 안전장치**로 쪼갤 수 있다. 이것이 만들 수 있는 효과의 천장이다. 쪼갠 부품마다 만드는 것이 정해져 있다.
>
> | 부품 | 카드 던전 예 | 만드는 것 |
> |---|---|---|
> | 언제 · 즉시 | 타격: 내면 바로 피해 | 결과 Block · `[BindTo]` |
> | 언제 · 발동 | 닻: 전투 첫 턴에 | Trigger |
> | 언제 · 지연 | 2턴 뒤 폭발 | 상태 효과의 끝날 때 결과 · 예약(`Game.ScheduleOn`) |
> | 언제 · 지속 | 화상: 2턴 동안 턴 끝마다 피해 | 상태 효과 · Modifier |
> | 누구에게 | 모든 적, 무작위 적 | 대상 Block |
> | 무엇을 · 데이터 바꾸기 | 피해, 방어도, 회복 | 효과 Block · 플레이어 데이터에 주기 |
> | 무엇을 · 만들기·없애기·바꾸기 | 카드 강화(다른 카드가 된다) | 행동 · 효과 Block · `[BindTo]` |
> | 무엇을 · 가로채기 | 가시 갑옷: 받는 피해 −5 | 트리거 `가로채기` + 결과 `의도 고치기` |
> | 무엇을 · 선택 요구 | 손패 1장 버리기 | 선택 대기(결과 `선택 요구` · `Game.Ask`) |
> | 무엇을 · 비용 | 카드의 에너지 비용 | `Game.Pay` · 결과 `비용 내기` |
> | 안전장치 | 유물끼리 서로 부르는 연쇄 | 사슬 16단계에서 끊기 |
>
> 문법 밖: 실시간 물리·이동(화면 코드), 게임 상태를 통째로 되감는 효과. 기획 문장이 어느 부품에도 들지 않으면 효과가 아니라 화면이나 다른 시스템의 일이다.

## 9. id와 연결

- 찾거나 가리킬 객체에 `public string id;`를 둔다.
- 객체 사이는 참조가 아니라 **id 글자**로 잇는다. 손패는 카드 id 리스트, 가진 유물은 유물 ID 리스트다.
- DB 항목 ID를 담는 글자 필드에는 `[DataRef(typeof(종류))]`를 붙인다. DB에서 목록으로 고르는 칸이 된다.
- 스프라이트 같은 에셋은 판 데이터에 두지 않고 원본에 둔다.

> [!note]- `id`가 필요한 때
> | 이럴 때 | 왜 |
> |---|---|
> | `Game.Find<T>(id)`로 다시 찾는다 | `Find`는 `id` 필드로 찾는다. |
> | 행동의 대상 필드다(`PlayCard.card`·`target`) | 행동에는 객체를 넣지만, 시스템은 기록·전송에 id만 남기고 실행 직전에 그 id로 지금 객체를 다시 찾는다(4절). |
> | `Game.Use`의 사용자·대상이다 | 사용 행동은 id만 담고, 끝날 때 최종 대상의 id로 `능력 사용` 이벤트를 쏜다. `id`가 없으면 예외로 사용 전체가 취소된다. |
> | 같은 원본을 가리키는 판 데이터가 여러 개다(같은 카드 여러 장) | 원본 ID로는 장을 구별할 수 없다. `Game.NewId("접두어")`로 장마다 새 id를 준다. |
>
> 필요 없는 객체: 플레이어 데이터의 뿌리(`RunState`·`Profile`, `Game.Data()`로 꺼낸다), 한 객체 안에 하나뿐인 하위 객체(`Battle`), 인스턴스 원본(DB 항목 ID가 곧 이름).

> [!note]- 카드 던전의 id
> | 객체 | id | 이유 · 만드는 법 |
> |---|---|---|
> | `Hero` | 있음 | `Game.Use`의 사용자, 피해 이벤트의 출처. 판마다 `"hero"` |
> | `Enemy` | 있음 | 카드의 고른 대상, `Find`. `Game.NewId("enemy")` |
> | `DeckCard` | 있음 | `PlayCard`의 대상, 손패·더미가 가리킨다. `Game.NewId("card")` |
> | `RunState`·`Profile` | 없음 | `Game.Data().Run`·`.Profile` |
> | `Battle` | 없음 | `RunState` 안에 하나 |
> | `Card`·`MonsterData`·`Potion` | 없음 | 인스턴스 ID로 부른다. 판 데이터에는 원본 ID만 든다(`DeckCard.card`, `Enemy.monster`, 물약 가방 칸) |
>
> `Game.NewId`의 번호는 메타 구역에 있어 판이 바뀌어도 이어서 늘어난다. 새 판의 카드 id가 지난 판과 겹치지 않는다.

> [!note]- 왜 글자로 잇나
> - 저장·리플레이·네트워크에 그대로 실린다.
> - Undo·불러오기 뒤에 객체가 바뀌어도 `Find`로 지금 객체를 다시 찾는다.
> - 판 저장에는 DB 항목 참조만 ID로 담기고, 그 밖의 에셋 참조는 "저장할 수 없는 참조입니다" 경고와 함께 빠진다.
> - `[DataRef]` 필드(또는 필드 규칙으로 DB 종류에 연결한 필드)는 DB에서 ID를 바꾸면 값도 따라 바뀐다. 출시 뒤 ID를 바꿔도 저장 파일 안의 예전 ID는 불러올 때 저절로 새 ID로 바뀐다(변환을 따로 쓰지 않는다, [10장](10-SaveAndRelease.md)).

## 10. 결과: 클래스 목록과 DB 항목 목록

이 목록을 팀과 맞추면 다음 장부터는 채워 가기만 하면 된다. 폴더는 01장 2절의 Rules·Client다.

| 폴더 · 파일 | 들어가는 것 | 기획 |
|---|---|---|
| `Rules/Data/Keywords.cs` | 키워드 그룹 `Rarity`·`Element`·`CardTag` | 5 |
| `Rules/Data/Model.cs` | 판: `Fighter`·`Hero`·`Enemy`·`DeckCard`·`Battle`·`RunState` / 프로필: `Profile` / 원본: `Card`·`MonsterData`·`Potion` | 1~11 |
| `Rules/Events/Events.cs` | `CardPlayed`·`DamageDealt`·`EnemyKilled`·`TurnStarted`·`TurnEnded`·`BattleWon`·`GoldGained`·`RunEnded` | 5절 |
| `Rules/Events/Intents.cs` | 의도 `DamageIntent`(`Rules.Damage`의 가로채기 자리) | 8절 |
| `Rules/Actions/RunActions.cs` | `StartRun`·`BuyCard`·`DrinkPotion`·`EndRun` | 1, 2, 9, 11 |
| `Rules/Actions/BattleActions.cs` | `StartBattle`·`DrawCards`·`PlayCard`·`EndTurn`·`WinBattle` | 3~8 |
| `Rules/Cards/CardBlocks.cs` | 효과 Block 셋, 대상 Block 둘 | 5 |
| `Rules/Cards/CardCode.cs` | `[BindTo]`: 연쇄 번개, 첫 승리 | 11 |
| `Rules/Relics/Relics.cs` | DB 종류 `RelicData`, 감시 등록 함수 `RelicRules.Register`, `[BindTo]` 불타는 피 | 10 |
| `Rules/Run/Rules.cs` | 규칙 함수(원본 읽기 `CardData`·`MonsterOf`·`PotionOf`, 덱 카드·적 만들기, 피해, 섞기) | 4, 6 |
| `Client/Bootstrap.cs` | 시작 코드(01장 4절). `Started`에서 유물 감시 연결 한 줄(`RelicRules.Register`) | 10 |
| `Client/RunDirector.cs` | 씬의 런 스크립트([03장](03-GameLoop.md)) | 13 |

| DB 탭 | 항목 |
|---|---|
| 플레이어 데이터 | `run`(Session, `RunState`), `profile`(Meta, `Profile`) — 시작 설정이 만든다 |
| 인스턴스 | 카드 `strike`·`defend`·`fireball`·`chainLightning`, 몬스터 `goblin`·`slime`·`dragon`, 물약 `healPotion` |
| 상태 효과 | `burn`(화상, [04장](04-AddingContent.md) 4절) |
| 로그 | `kills`, `maxTurnDamage`, `goldEarned` |
| 도전과제 | `firstWin`, `bigTurn` |
| 유물 | `anchor`, `burningBlood` |
| 다이얼로그 | `shopkeeper`([06장](06-Presentation.md)) |
| 개발 › 개발 명령 | `goldPlus100`·`turnDamage30`·`killAll`(`killAll`만 `[BindTo]` 코드, [08장](08-TestAndDebug.md)) |

기획 13번(되돌리기)은 클래스가 아니다. DB › 시작 파일 › Bootstrap의 Undo 방식 "행동 단위"와 "값은 행동 안에서만 바꾼다"는 규칙([03장](03-GameLoop.md))으로 해결된다.

## 11. 체크리스트

- [ ] 기획 문장마다 어느 절(데이터·구역·행동·이벤트·키워드·DB·Block·id)인지 적었다.
- [ ] 판 값은 `RunState`, 남길 값은 `Profile`에 있고, 섞이지 않았다.
- [ ] 판 데이터에는 원본 ID와 바뀌는 값만 있다. 고정 값은 원본에서 읽는다.
- [ ] 계산되는 값을 필드로 두지 않았고, 화면 상태는 MonoBehaviour 쪽이다.
- [ ] 종류를 자식 클래스가 아니라 키워드 필드·기능 필드로 나타냈다.
- [ ] 입력과 규칙의 한 걸음이 모두 행동이고, 필요한 정보가 필드에 있다(대상은 id가 있는 플레이어 데이터 객체로).
- [ ] 반응할 순간이 모두 `[GameEvent]` 이벤트이고, 조건·로그가 읽을 값이 필드에 있다.
- [ ] 찾거나 가리킬 객체에 `id`가 있고, 연결은 id 글자다.
- [ ] 클래스 목록과 DB 항목 목록을 팀과 맞췄다.

> [!note]- 더 보기(매뉴얼)
> - [01. 플레이어 데이터](../../Manual/01-PlayerData.md) — 무엇을 넣고 넣지 않는가, 기록되는 필드, 지원 형식
> - [04. 인스턴스](../../Manual/04-Instances.md) — 원본과 사본, `[Hide]`·`[ShowIf]`, 기능 필드
> - [05. 키워드](../../Manual/05-Keywords.md)
> - [10. 이벤트](../../Manual/10-Events.md) — 필드 설계, 상속한 이벤트
> - [02. 행동과 Undo](../../Manual/02-Actions-Undo.md) — 행동 클래스, 대상 필드
> - [16. 능력과 사용 기능](../../Manual/16-Abilities-Usable.md) — `Usable`
> - [17. 프로젝트 고유 기능](../../Manual/17-Custom-Features.md) — DB 종류 + Trigger 감시
> - [03. Game API](../../Manual/03-GameApi.md) — `Find`·`NewId`
> - [28. 능력치·Modifier·가로채기·비용](../../Manual/28-Attributes-Intercepts.md) — 능력치 계산, 가로채기 자리, 효과의 문법
