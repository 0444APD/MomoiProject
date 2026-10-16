# 04. 콘텐츠 더하기 — 카드·몬스터·물약·로그·도전과제·유물

게임 루프가 돌면 이후의 일은 대부분 "새 항목 더하기"다(개발자가 요청하면 — 진행 매뉴얼 8절). 항목은 DB 반영 스크립트로 만들고, 코드는 새 동작(Block)이나 그 항목만의 예외(`[BindTo]`)가 필요할 때만 쓴다. "가지고 있으면 발동"하는 유물은 기본 종류 **패시브 효과**로 코드 없이 만들고(8절 첫 접힌 칸), 기본 종류에 없는 규칙이 있는 새 종류만 프로젝트 DB 종류로 만든다(8절은 그 방법을 유물로 보인다).

> [!note]- 이 장에서 쓰는 말
> | 말 | 뜻 |
> |---|---|
> | 사용 기능(`Usable`) | 원본 클래스에 붙이는 기능 필드(`when` 트리거 · `then` 결과 · `failText`). 붙어 있는 인스턴스는 게임이 켜질 때 인스턴스 ID로 등록되어 `Game.Use(인스턴스 ID, 사용자, 대상들)`로 쓴다. |
> | Trigger | DB 항목의 "언제" 칸. 잎(이벤트 받기 · 조건 충족 · 사용 · 행동 직전 이벤트 받기)을 AND·OR·NOT으로 조립한다. |
> | Outcome(결과) | DB 항목의 "무엇을" 칸. 대상에게 효과 · 플레이어 데이터에 주기 · 로그에 기록 · 선택 요구 · 이벤트 쏘기 · 코딩한 결과 …(참조 5절). |
> | 상태 효과 | DB › 상태 효과의 항목. 대상에게 걸려 있는 동안 효과가 나는 버프·디버프. 중첩·지속·스탯 보정·트리거와 결과를 정하고, 효과 Block `상태 효과 걸기`로 건다. 선택 요소라 DB 종류(`StatusData`)는 템플릿을 프로젝트에 들인다(참조 4-1). |
> | 패시브 효과 | DB › 패시브 효과의 항목. 가진 목록에 그 ID가 있는 동안 발동마다 결과를 내고, 가진 동안 스탯 보정을 붙인다. 유물·특성·조커를 코드 없이 만드는 기본 종류(선택 요소, `PassiveData`). |
> | 선택 요구 | 후보 중에서 플레이어가 고르게 하는 결과 Block. 행동은 답을 기다리지 않고 끝나고, 고르면 고른 것에 효과가 따로 실행된다. |
> | `[BindTo]` | static 함수에 붙여 그 ID를 가진 DB 항목 하나의 결과를 코드로 바꾸는 어트리뷰트. 그 항목의 결과 칸은 잠긴다. |
> | 컨텍스트(`TriggerContext`) | Trigger가 발동한 순간의 정보: 이벤트(`Event`), 사용자(`User`), 고른 대상(`Picked`), 항목 ID(`Owner`), `Game`. 결과·`[BindTo]` 함수가 읽는다. |
> | 가중치 표 · 밸런싱 값 | DB › 가중치 표(무게를 붙인 뽑기 표, `Game.Roll`)와 DB › 밸런싱 값(이름 붙은 숫자·곡선, `Game.Balance`). 10절 접힌 칸. |

---

## 1. 표준 절차

새 항목마다 이 순서로 한다. **순서가 중요한 곳은 4 → 5 → 6이다**: `GameIds` 상수는 반영 스크립트가 돌고 Unity가 다시 컴파일한 뒤에야 생긴다(관례 15절).

1. **클래스**: 필요한 칸이 있는지 본다. 없으면 필드를 더한다(이미 있는 인스턴스는 필드 초기값을 받는다).
2. **이벤트**: 반응할 순간의 이벤트를 이미 쏘는지, 필요한 값이 이벤트 필드에 있는지 본다. 없으면 더하고 행동 안에서 쏜다.
3. **Block**: 이미 있는 Block(기본 Block — 참조 5절, 프로젝트 Block)으로 조립되는지 본다. 안 되면 Block 하나(2절).
4. **항목**: 반영 스크립트(`Assets/MomoiProject/Code/Editor/Seeds/<주제>Seeds.cs`의 `[DbSeed("버전")]`)로 항목을 만든다 — 값·사용 기능·Trigger·결과. 이미 있는 ID는 건너뛴다. 새 항목은 컴파일 뒤 저절로 적용된다.
5. **새로 읽기**: `Logs/MomoiProject/RunTests.request`를 만들어 Unity가 컴파일·반영 스크립트를 돌리게 하고, `GameIds.cs`에 새 상수가 생긴 것을 본다.
6. **`[BindTo]`**: 그 항목만의 결과가 있으면 이제 상수로 함수를 쓴다(2절).
7. **등장시키기**: 덱·상점·전투 목록처럼 게임에 나오는 자리에 ID를 넣는다(10절).
8. **시험과 검증**: 규칙 테스트, 얻기·쓰기·걸기 테스트 명령(관례 19절), `Logs/MomoiProject/DbStatus.txt` "검증 통과"(11절). 개발자에게 확인할 것을 알린다.

> [!note]- 코드가 필요한가
> | 새 항목이 | 할 일 | 코드 |
> |---|---|---|
> | 이미 있는 클래스·Block으로 수치만 다르다 | 반영 스크립트로 항목 | 반영 스크립트만 |
> | 이미 있는 동작을 새로 조합한다(피해 + 화상) | 결과 칸에 Block 여러 개 | 반영 스크립트만 |
> | 새 동작이 필요하고, 다른 항목도 쓸 만하다 | Block 클래스 하나 | Block 하나 |
> | 그 항목에만 있는 흐름이다 | `[BindTo]` 함수 하나 | 함수 하나 |
> | 반응할 순간이 아직 없다 | 이벤트(또는 필드)를 더하고 행동에서 쏜다 | 이벤트 + 쏘는 한 줄 |
> | 가지고 있으면 발동한다(유물·특성·장비 효과) | DB › 패시브 효과 항목 | 반영 스크립트만(8절 접힌 칸) |
> | 기존 기능(도전과제=한 번, 로그=숫자, 사용 기능=쓰는 순간, 패시브 효과=가진 동안, 상태 효과=걸린 동안)과 규칙이 다른 새 종류다 | DB 종류 + 감시 등록 | 8절 |

## 2. Block과 `[BindTo]` 고르기

- 여러 항목이 수치만 바꿔 다시 쓸 동작 → **Block**.
- 그 항목 하나에만 있는 흐름 → **`[BindTo]`**.
- 같은 `[BindTo]` 코드가 두 번째 항목에서도 필요해지면 그때 Block으로 올린다. Block을 미리 많이 만들지 않는다.

> [!note]- 자세한 기준
> **Block**이 맞는 때:
> - 동작이 짧고 이름 하나로 설명된다("피해", "방어도", "모든 적").
> - 다른 카드·물약·유물도 쓸 수 있다.
> - 수치를 개발자가 DB에서 바꾼다. Block의 public 필드가 DB 칸(`Sentence`의 칸)이 된다.
> - 조합해서 새 항목이 생긴다. "피해 + 화상" 카드는 코드 없이 반영 스크립트로 조립되고, 개발자는 DB에서 문장으로 읽는다.
>
> **`[BindTo]`**가 맞는 때:
> - 그 항목 하나에만 있는 흐름이다(대상마다 수치가 바뀜, 조건 분기, 여러 값을 함께 계산).
> - Block으로 쪼개면 그 항목 하나 때문에 Block이 여러 개 생기거나, 블록 조합이 스크립트처럼 길어진다.
> - DB에서는 결과 칸이 잠기고 함수 이름만 보인다. 그래서 **안의 숫자는 코드에 적지 않고** 그 항목의 칸이나 밸런싱 값에서 읽는다(관례 18절). 개발자는 Trigger와 그 값을 바꾼다.
>
> 블록 조합으로 스크립트 언어를 만들지 않는 것이 원칙이다.

> [!note]- `[BindTo]` 함수의 모양
> `static`, 반환 `void`, 매개변수는 `()` · `(이벤트)` · `(TriggerContext)` · `(이벤트, TriggerContext)` 중 하나. 받을 이벤트는 첫 매개변수의 형식으로 정해진다. 사용 기능·액티브 효과에는 이벤트가 없으므로 `(TriggerContext c)`만 쓴다. 인자는 글자 대신 `GameIds` 상수다(오타·ID 바꾸기가 컴파일 오류로 먼저 드러난다). 같은 ID에 `[BindTo]`가 둘이면 모두 돌고 순서가 정해지지 않는다(검증 경고).

## 3. 몬스터 — 드래곤 (코드 없음)

인스턴스 `dragon`, 형식 `MonsterData`. 칸: `maxHp`, `attackPattern`(턴마다 차례로 주는 피해), `element`, `portrait`(그림 — 진짜 파일은 개발자가 넣는다, `AssetRequests.Sprite`로 자리를 만든다 — 관례 18절). 플레이어에게 보일 이름은 텍스트 `dragon.name`이다(원본에 이름 칸을 두지 않는다).

```csharp
// Assets/MomoiProject/Code/Editor/Seeds/CardSeeds.cs
static void Instance(string id, object value)
{
    if (EditorDatabase.Instance.FindById(id) != null) return;   // 이미 있으면 건너뛴다(개발자가 맞춘 값을 덮지 않는다)
    var data = (InstanceData)EditorDatabase.Staging.Add(typeof(InstanceData), id);
    data.instanceType = value.GetType().FullName;
    data.value = value;
}

[DbSeed("1")]
static void Monsters()
{
    Instance("dragon", new MonsterData { maxHp = 120, attackPattern = { 10, 14, 20 }, element = Element.Fire });
}
```

> [!note]- 전투에서 어떻게 쓰이나
> 전투를 여는 행동의 몬스터 목록(`StartBattle.monsters`)에 ID를 넣으면 나온다. 전투 시작 때 `Rules.NewEnemy`가 원본의 최대 체력을 읽어 `Enemy`를 만든다. 판 데이터에 들어가는 것은 원본 ID와 바뀌는 값(체력·공격 차례)뿐이고, 적은 턴마다 원본의 `attackPattern`을 읽는다. 그래서 개발자가 DB에서 공격 순서를 고치면 진행 중인 판의 드래곤에도 바로 반영된다.

## 4. 카드 — 화염구 (Block 조합)

인스턴스 `fireball`, 형식 `Card`. 칸 `cost`·`rarity`·`element`(`fire`)·`tags`(`attack`)·`art`, 그리고 사용 기능 `usable`: 트리거(`when`)는 기본 `사용` 그대로, 결과(`then`)에 `대상에게 효과` — 대상(`targets`)은 비우고(= 플레이어가 고른 적), 효과(`effects`)는 `피해` → `상태 효과 걸기`(`burn`, 중첩 1).

화상 `burn`은 DB › **상태 효과**의 항목이다(아래 접힌 칸). `상태 효과 걸기`(`ApplyStatusEffect`)는 기본 효과라 코드가 없다.

```csharp
[DbSeed("1")]
static void Fireball()
{
    var usable = new Usable();   // when = 기본 '사용'
    usable.then.Add(new TargetedOutcome { effects = { new DealDamage { amount = 8 }, new ApplyStatusEffect { status = "burn", stacks = 1 } } });
    Instance("fireball", new Card { cost = 2, element = Element.Fire, tags = { CardTag.Attack }, usable = usable });
}
```

`피해` 같은 효과 Block은 AI가 프로젝트 코드로 만든다. 동작 하나만 한다.

```csharp
[Serializable, BlockInfo("피해", Category = "카드", Description = "대상에게 피해를 줍니다(방어도 먼저).", Sentence = "{amount} 피해")]
public class DealDamage : Effect<Fighter>
{
    public int amount = 6;
    protected override void Apply(Fighter target) => Rules.Damage(Game, User<Fighter>(), target, amount);
    public override string Describe() => $"피해 {amount}";
}
```

> [!note]- 타격·수비와 카드를 내는 코드
> 타격은 같은 방법으로 `피해` 하나, 수비는 대상에 `사용자 자신`, 효과에 `방어도`를 둔다. 카드를 낼 때 `PlayCard`가 카드 원본 ID로 `Game.Use`를 부르고 주인공을 사용자로 넘기므로 `사용자 자신`이 주인공이 된다.
> ```csharp
> if (target == null) Game.Use(card.card, run.hero);
> else Game.Use(card.card, run.hero, target);
> ```
> `card.card`는 덱 카드가 든 원본 인스턴스 ID다. 사용 기능은 인스턴스 ID로 등록되므로 덱의 장마다 사용 기능을 들고 있을 필요가 없다. 효과를 고치면 모든 장에 바로 반영된다.

> [!note]- Block 코드 자세히
> - 피해는 규칙 함수(`Rules.Damage`)에 맡긴다. 그래서 방어도·처치 이벤트까지 카드와 적 공격이 같은 규칙을 탄다.
> - `[BlockInfo]`의 이름·분류·설명·문장이 개발자가 DB에서 읽는 글이 되고, `Describe`가 상세 위쪽 요약에 수치까지 보이게 한다([05장](05-WorkingWithDesigners.md) 5절).
> - 넣으면 안 되는 값은 Block의 `Validate`로 막는다. 0 이하 피해를 DB 검증 오류로 만드는 예는 [05장](05-WorkingWithDesigners.md) 7절.
> - 효과 배율(Lumin·Critical 같은 "수치 2배")을 받으려면 수치에 `Use.Context.Power`를 곱한다(관례 4절). 카드 던전 예제는 배율 기믹이 없어 생략했다.

**몇 턴 동안** 이어지는 효과(턴마다 피해를 주는 화상·독, 3턴 동안의 보호막)는 카드 결과에 직접 쓰지 않는다. DB › **상태 효과**에 항목으로 만들고, 카드 효과에는 기본 `상태 효과 걸기`를 둔다.

> [!note]- 화상 — 턴 끝마다 피해 2, 2턴(상태 효과, 코드 없음)
> 항목 `burn`(`StatusData`). 칸: `duration`(`kind` `이벤트 횟수` · `length` `2` · `tickEvent` 턴 끝), `trigger` `이벤트 받기`(턴 끝), `outcomes` `대상에게 효과`(대상 비움 → 효과 `피해` 2). 이름은 텍스트 `burn.name`("화상").
> ```csharp
> if (EditorDatabase.Instance.FindById("burn") == null)
> {
>     var burn = (StatusData)EditorDatabase.Staging.Add(typeof(StatusData), "burn");
>     burn.duration.kind = StatusDuration.Events;
>     burn.duration.length = 2;
>     burn.duration.tickEvent = typeof(TurnEnded).FullName;
>     burn.trigger = new EventTrigger { eventType = typeof(TurnEnded).FullName };
>     burn.outcomes.Add(new TargetedOutcome { effects = { new DealDamage { amount = 2 } } });
> }
> ```
> - 상태 효과의 결과에서는 사용자·고른 대상이 **가진 쪽**(화상이 걸린 적)이다. 그래서 대상을 비워 두면 화상이 걸린 적이 피해를 받는다.
> - `maxStacks`(1)·`reapply`(중첩 더하고 지속 갱신)는 기본값 그대로다. 화염구를 또 맞으면 중첩은 1 그대로, 남은 횟수가 2로 돌아간다.
> - 맨 위 칸 `kind`(종류: 없음·버프·디버프)는 `duration` 안의 `kind`(방식)와 다른 칸이다. 디버프로 두면 '상태 효과 풀기'(종류 전부 = 정화)·`Game.StatusCount(대상, 종류)`에 잡힌다.
> - 피해는 `피해` Block → `Rules.Damage`를 거치므로 방어도·가시 갑옷 같은 행동 직전 이벤트·처치 이벤트가 카드 피해와 같다. 마지막 적이 화상으로 쓰러지면 `EndTurn`이 전투 승리로 넘어간다([03장](03-GameLoop.md) 4절).
> - 시험(개발자): 화염구를 내고 모니터 **데이터** 탭 `$statuses`에서 `burn`의 `remaining`(2)을 본 뒤 턴을 끝낸다. 적이 2 피해를 받고 `remaining`이 1이 된다. **Undo**하면 피해와 남은 횟수가 함께 돌아간다. 같은 흐름을 규칙 테스트로도 둔다.

> [!note]- 독 — 턴 끝마다 중첩만큼 피해, 3턴(상태 효과 + `[BindTo]`)
> 1. 항목 `poison`: `maxStacks` `0`(제한 없음), `reapply` `중첩 더하고 지속 갱신`, `duration` 이벤트 횟수 3(턴 끝), `trigger` 이벤트 받기(턴 끝). 결과는 비운다.
> 2. 새로 읽기 뒤 `GameIds.Status.Poison`이 생기면 Rules에 함수를 쓴다(결과가 중첩 수를 읽는 계산이라).
> ```csharp
> [BindTo(GameIds.Status.Poison)]
> static void Poison(TurnEnded e, TriggerContext c)
> {
>     var holder = (Fighter)c.User;                                              // 가진 쪽(독이 걸린 적·주인공)
>     Rules.Damage(c.Game, null, holder, c.Game.StatusStacks(holder, c.Owner));   // Owner = 상태 효과 ID
> }
> ```
> 3. 카드 `poisonDart`(독침): 결과 `대상에게 효과` → 대상 비움(고른 적) → 효과 `피해`(4) → `상태 효과 걸기`(`poison`, 중첩 `3`).
>
> - 같은 턴 끝이 피해와 횟수 줄이기를 둘 다 하면 피해가 먼저다. 피해 세 번 뒤에 끝난다. 다시 걸면 중첩이 더해지고 3턴으로 돌아간다.
> - 걸린 독은 플레이어 데이터 `$statuses`에 들어 있어 Undo·판 저장이 따라온다.
> - 화상과 다른 점: 화상은 피해가 고정이라 Block으로 조립했고, 독은 중첩 수만큼이라 `[BindTo]`다.
> - 코드에서 걸 때는 `Game.ApplyStatus(enemy, GameIds.Status.Poison, stacks: 3)`, 화면의 상태 아이콘은 `Game.Statuses(fighter)`·`Game.StatusStacks`로 읽는다. 상태 효과 없이 "2턴 뒤 이 행동"만 걸 때는 예약(`Game.ScheduleOn<TurnEnded>(2, 행동)`).
> - 스탯 보정(분노 +10%)·행동 직전 이벤트(보호막 "내가 받는 피해 반감")·끝날 때 결과("3턴 뒤 폭발")도 같은 항목의 칸이다. 매뉴얼 [29장](../../Manual/29-Statuses-Schedules-Choices.md).

## 5. 카드 — 연쇄 번개 (`[BindTo]`)

"적마다 차례로 3, 4, 5 … 피해"는 그 카드만의 흐름이라 함수 하나로 쓴다.

1. 반영 스크립트: 인스턴스 `chainLightning`(형식 `Card`)에 사용 기능(`usable = new Usable()`)을 붙이고 결과는 비운다. 시작 피해·늘어나는 양은 밸런싱 값 `chainLightningBase`(3)·`chainLightningStep`(1)으로 함께 만든다.
2. 새로 읽기 → `GameIds.Instance.ChainLightning`·`GameIds.Balance.ChainLightningBase`가 생긴 것을 본다.
3. Rules에 함수를 쓴다.

```csharp
[BindTo(GameIds.Instance.ChainLightning)]
static void ChainLightning(TriggerContext c)
{
    var hero = c.User as Fighter;
    var damage = (int)c.Game.Balance(GameIds.Balance.ChainLightningBase);   // 숫자는 밸런싱 값에서(관례 18절)
    var step = (int)c.Game.Balance(GameIds.Balance.ChainLightningStep);
    foreach (var enemy in Rules.LivingEnemies(c.Game).ToList())
    {
        Rules.Damage(c.Game, hero, enemy, damage);
        damage += step;
    }
}
```

> [!note]- 자세히
> - 사용 기능은 결과가 비어도 붙여 둔다. 붙어 있어야 `Game.Use`로 쓸 수 있는 항목으로 등록된다.
> - 사용이라 이벤트가 없으므로 매개변수는 컨텍스트 하나다.
> - 컴파일하면 DB에서 이 인스턴스의 결과 칸 자리에 "코드로 쓴 결과가 이 항목에 연결되어 있습니다([BindTo]). 결과는 코드에서 고칩니다." 안내와 함수 이름이 보인다.

## 6. 물약 — 회복 물약 (코드 없음)

인스턴스 `healPotion`, 형식 `Potion`. 칸 `maxStack`, 사용 기능 → 결과 `대상에게 효과` → 대상 `사용자 자신` → 효과 `회복`(amount).

> [!note]- 마시는 행동
> 물약은 인벤토리에 원본 ID와 개수로만 들어간다. 한 슬롯에 몇 개까지 겹칠지도 고정 값이라 넣을 때 원본의 `maxStack`을 읽는다(`Rules.PotionOf`). 마시는 행동이 개수를 줄인 뒤 원본의 사용 기능을 쓴다. 한 행동이라 Undo 한 번에 함께 돌아가고, 물약이 없으면 예외로 아무것도 바뀌지 않는다.
> ```csharp
> var run = Game.Data().Run;
> if (!run.potions.TryRemove(potion)) throw Game.Fail("fail.noPotion", "물약이 없습니다.");
> if (target == null) Game.Use(potion, run.hero);
> else Game.Use(potion, run.hero, target);
> ```

> [!note]- 다른 장르라면
> - **RPG의 장비·소모품**: 아이템 클래스 하나에 기능 필드(`[SerializeReference] public Equip equip;`, `public Usable usable;`)를 두고 인스턴스로 더한다. 슬롯·개수는 인벤토리([25. 인벤토리](../../Manual/25-Inventory.md)). 여러 아이템이 같은 효과를 나누면 아이템이 액티브 효과 ID를 가리키게 하고(`[DataRef(typeof(AbilityData))]`) 효과는 DB › 액티브 효과에서 한 번 조립한다.
> - **스킬**처럼 아이템이 아닌 "쓰는 것"은 DB › 액티브 효과에서 만든다. 게임 코드는 똑같이 `Game.Use(액티브 효과 ID, 사용자, 대상들)`이다.

## 7. 로그와 도전과제

**로그**는 이벤트를 받을 때마다 숫자를 쌓는다. 게임 코드는 이벤트만 쏘면 되고, 나머지는 DB › **로그** 항목(`StatData`)의 범위(`scope`)·집계(`aggregate`)·반영 규칙(`triggers`)이다.

| ID | 범위 · 집계 | 반영 규칙(이벤트 받기) | 반영할 값 |
|---|---|---|---|
| `kills` | 누적 · 합계 | 적 처치 | 비움(횟수 1) |
| `maxTurnDamage` | 누적 · 최고치 | 턴 끝 | `damage` |
| `goldEarned` | 누적 · 합계 | 골드 획득 | `amount` |

**도전과제** `bigTurn`(코드 없음): DB › **도전과제**(`AchievementData`) 항목, `trigger` = `조건 충족` → 조건 `비교`(로그 `maxTurnDamage` `>=` `30`). 결과는 기획이 정한다(예: 플레이어 데이터에 주기 → `profile`의 필드).

**도전과제** `firstWin`(`[BindTo]`): `trigger` = `이벤트 받기` → `전투 승리`. 결과(연쇄 번개 해금)는 코드로 쓴다. 해금은 프로필에 준다.

```csharp
[BindTo(GameIds.Achievement.FirstWin)]
static void FirstWin(BattleWon e, TriggerContext c)
{
    var profile = c.Game.Data().Profile;
    if (!profile.unlocked.Contains(GameIds.Instance.ChainLightning)) profile.unlocked.Add(GameIds.Instance.ChainLightning);
}
```

> [!note]- 반영할 값과 진행도
> - 반영할 값은 이벤트의 숫자 필드 목록에서 고른다. 한 턴 최대 피해를 셀 수 있는 것은 [02장](02-DesignToCode.md)에서 `TurnEnded`에 `damage` 필드를 두었기 때문이다. 이벤트에 필요한 값이 없으면 로그가 아니라 이벤트를 고친다.
> - `bigTurn`처럼 조건만으로 된 Trigger는 모니터 카드에 진행도(`18 / 30` 같은)가 보이고, 턴 끝 행동이 끝날 때 다시 평가되어 달성된다.
> - `firstWin`은 이벤트로 발동하므로 이벤트와 컨텍스트를 둘 다 받는다.

> [!note]- 결과가 Undo되는가 — 도전과제 결과는 프로필에
> | 어디서 발동했나 | Undo |
> |---|---|
> | 사용 기능(`Game.Use`), 로그, 행동 안에서 쏜 이벤트로 발동한 유물·패시브 효과·상태 효과 | 그 행동과 함께 돌아간다 |
> | 도전과제 | 돌아가지 않는다(달성·결과는 Undo 이력 밖) |
>
> 그래서 도전과제의 결과는 행동이 건드리지 않는 값(프로필 쪽)에 준다. 판 데이터에 주면, 달성시킨 행동을 Undo할 때 그 값이 행동 전으로 덮일 수 있다. 해금은 판이 끝나도 남아야 하므로 어차피 프로필이 맞다.

## 8. 새 DB 종류 — 유물

유물은 "가지고 있는 동안, 어떤 순간마다, 무언가 한다"는 규칙이라 기본 종류 **패시브 효과**가 그대로 맡는다(아래 첫 접힌 칸, 코드 없음). 카드 던전의 유물도 모두 패시브 효과다(관례 0절).

첫 접힌 칸 아래는 패시브 효과에 없는 규칙(한 번만 발동하고 완료를 저장·하루 N번·단계·선행 항목 — 매뉴얼 [17장](../../Manual/17-Custom-Features.md) 0-5)이 생겼을 때 DB 종류를 직접 만드는 법을 유물을 예로 보인다(카드 던전은 쓰지 않는다). 칸(가격·등급)이 모자란 것뿐이면 새 종류가 아니라 프로젝트의 `PassiveData`에 칸을 더한다(참조 4-1). 직접 만들 때 할 일은 둘이다.

> [!note]- 기본 종류 '패시브 효과'로 하면 — `RelicData`·`RelicRules` 없이
> 항목 `anchor`(`PassiveData`). 칸:
> - 가진 목록: `ownerKey` `run`, `ownerPath` `relics`(기본값). `run.relics`에 이 ID가 있는 동안 발동한다. 얻고 잃는 것은 그 목록에 넣고 빼는 것이다(행동 안에서).
> - 발동(`trigger`)·결과(`outcomes`)는 9절의 유물과 같다. 이벤트 받기·조건 충족·행동 직전 이벤트 받기와 행동 직전 이벤트 고치기가 처음부터 열려 있다.
> - 새로 읽기 뒤 `GameIds.Passive.Anchor`가 생긴다. 결과를 코드로 쓰려면 `[BindTo(GameIds.Passive.Anchor)]` — 등록과 `BoundOutcomes` 확인은 라이브러리가 한다.
>
> - 결과의 사용자(가진 쪽)는 목록을 품은 `run`이다('놓인 것마다'를 켜면 목록의 요소 하나). 주인공에게 줄 효과는 대상에 `주인공` Block(9절)을 둔다.
> - 몇 개 가졌나는 `Game.Owned(GameIds.Passive.Anchor)`.
> - 가진 목록 칸은 `[DataRef(typeof(PassiveData))] public List<string> relics`(`RunState`)다. 직접 만든 DB 종류와 섞어 쓰면 `typeof(GameData)`(모든 DB 항목)로 둔다.
> - 아이콘은 패시브 효과 항목의 `icon`, 설명은 텍스트 `<ID>.desc`.

> [!note]- 패시브 효과 더 — 놓인 것마다 · 가진 동안 스탯 보정 · 필드 바꾸기 · 빌려 쓰기
> 카드 던전의 유물은 ID 목록 하나라 위 칸으로 충분하다. 조커 칸·보드처럼 **놓인 자리와 순서**가 효과를 바꾸는 조각, 가진 동안 스탯을 올리는 조각은 칸을 더 쓴다. 칸의 뜻·전체 규칙은 매뉴얼 [17장](../../Manual/17-Custom-Features.md) 0절, 갈래별 조립은 [기믹 목록](../GimmickCatalog.md), 칸 이름 목록은 참조 4절.
> - **조각 목록은 객체 목록**(고유 `id` + 원본 ID 필드)으로 둔다 — 같은 조각 둘, 조각마다 쌓이는 값을 두기 위해서다. 원본 ID 필드는 `matchField`(일치 필드)로 잇는다.
> - **`perElement`(놓인 것마다)** 를 켜면 가진 쪽 = 목록의 요소 하나이고, 같은 목록을 보는 패시브 효과끼리 **목록 순서(자리)대로** 섞여 발동한다. "+4 배수"가 왼쪽, "×2 배수"가 오른쪽이면 (1+4)×2, 자리를 바꾸면 1×2+4. 끄면 패시브 효과마다(등록 순서) 가진 쪽마다 한 번 발동한다.
> - **`modifiers`(가진 동안 스탯 보정)** 는 저장하지 않고 `Game.Attribute`를 읽을 때 더한다. 그래서 얻을 때 `AddModifier`·잃을 때 `RemoveModifiers`를 부르지 않는다. 받는 쪽은 `id`가 있어야 하므로 가진 쪽이 `run`이면 `modifierTarget`(보정 받을 대상)에 `hero`를 적는다.
> - **조각마다 쌓이는 값**은 효과 '필드 바꾸기'(저장되는 값 자체를 더하기·곱하기·값으로). 잠깐 올리는 것은 '스탯 보정'이다. 값에 틀(`{level}`·`{stacks}`·`{balance:ID}` …)을 쓸 수 있다(매뉴얼 12장).
> - **옆 조각 흉내**(Blueprint·Brainstorm)는 결과 '빌려 쓰기'(빌릴 조각 = 대상 Block, 예: '자리로 고르기' 오른쪽). 원본의 쌓이는 값은 바꾸지 않고, 서로 빌리는 고리는 끊는다. 코드로는 `Game.BorrowPassive(패시브 효과 ID, 원본)`.
> - 그 밖의 칸: `matchVia`(원본에서 읽기) · `levelField`(레벨 필드) · `rules`(발동 더 — 순간마다 다른 결과) · `pulse`(주기(초)) · `modifierWhen`(보정 조건) · `stacking`(여럿일 때).

**데이터** — `GameData`를 상속하면 DB에 탭이 생긴다. Trigger 칸·결과 칸에 `[TriggerSlot]`·`[OutcomeSlot]`을 붙이면 도전과제와 같은 편집기·검증이 붙는다. Rules에 둔다.

```csharp
[Database("유물", Group = "콘텐츠", Order = 40, IdPrefix = "relic")]
public class RelicData : GameData
{
    [FieldLabel("아이콘")] public Sprite icon;
    [FieldLabel("발동"), SerializeReference, TriggerSlot(TriggerKinds.Event | TriggerKinds.Condition | TriggerKinds.Intercept)]
    public Trigger trigger;
    [FieldLabel("효과"), SerializeReference, OutcomeSlot(OutcomeKinds.PlayerData | OutcomeKinds.Coded | OutcomeKinds.Targeted | OutcomeKinds.Intent)]
    public List<Outcome> outcomes = new List<Outcome>();
}
```

**감시** — 모든 유물의 Trigger를 감시에 등록하고, 발동하면 그 유물을 가지고 있을 때만 결과를 실행한다. 등록 함수는 Rules에 둔다(서버에서도 그대로 부른다).

```csharp
public static class RelicRules
{
    public static void Register(GameSession session, IEnumerable<RelicData> relics)
    {
        if (relics == null) return;
        foreach (var relic in relics)
        {
            var id = relic.Id;
            IEnumerable<Outcome> outcomes = BoundOutcomes.Has(id) ? BoundOutcomes.Create(id) : relic.outcomes;
            session.Triggers.Register(id, relic.trigger, repeat: true, fire: moment =>
            {
                var run = session.Game.Data().Run;
                if (run == null || !run.relics.Contains(id)) return;
                // 패시브 효과에 없던 규칙(하루 N번·단계 …)은 여기서 플레이어 데이터로 확인한다
                session.Triggers.RunOutcomes(outcomes, moment, "Relics");
            });
        }
    }
}
```

게임이 켜질 때 부르는 연결은 Client의 `Bootstrap.RegisterRules`에 한 줄 더한다([01장](01-StartProject.md) 4절 — 모든 세션에 오므로 멀티 서버 세션에서도 감시가 돈다. `Started`에 두면 서버 세션에서 빠진다).

```csharp
RelicRules.Register(session, GameDatabase.Load()?.All<RelicData>());
```

가진 목록 `RunState.relics`의 `[DataRef]`는 이 종류(`typeof(RelicData)`)로 둔다.

> [!note]- 데이터 칸 자세히
> - `[Database]`로 탭 이름·사이드바 그룹·순서·새 ID 접두어를 정한다. 개발자가 항목을 추가·삭제해야 하는 종류(레벨 배치)만 `DeveloperAuthored = true`다. 유물은 로직 쪽이라 AI가 반영 스크립트로 더한다.
> - `Trigger`·`Outcome`은 추상 형식이라 `[SerializeReference]`가 꼭 있어야 한다.
> - 잠금은 기능에 맞게 고른다. 감시해서 발동하는 기능에 `사용`(`TriggerKinds.Use`)은 의미가 없다. 결과에서 `로그에 기록`(`Record`)을 뺀 것은, 유물 ID는 로그가 아니어서 그 기록이 아무것도 하지 않기 때문이다.
> - `TriggerKinds.Intercept`·`OutcomeKinds.Intent`를 넣으면 발동에 `행동 직전 이벤트 받기`, 효과에 `행동 직전 이벤트 고치기`가 나온다(9절 가시 갑옷).
> - 표시 이름·설명 필드는 두지 않는다. 플레이어에게 보일 글은 텍스트 `<ID>.name`·`<ID>.desc`다([06장](06-Presentation.md) 5절).
> - `ValidateData`를 덮어써 "발동이 없다", "비어 있는 효과 칸이 있다"를 오류로 낸다([05장](05-WorkingWithDesigners.md) 7절).

> [!note]- 감시 코드 한 줄씩
> - **Rules와 Bootstrap으로 나누는 이유** — 01장 4절 '연결 시점과 위치'.
> - **`BoundOutcomes.Has(id)`** — `[BindTo]` 연결은 기본 종류만 스스로 확인한다. 프로젝트 종류는 이 한 줄로 직접 확인해 바꿔 쓴다. 빠뜨리면 DB에는 결과 칸이 잠겨 보이는데 실제로는 숨은 예전 결과가 돈다.
> - **`Register(owner, when, repeat, fire)`** — `owner`는 항목 ID(컨텍스트의 `Owner`가 된다). 유물은 여러 번 발동하므로 `repeat: true`. 한 번만 발동하는 기능(퀘스트)이면 `false`로 두고 완료를 플레이어 데이터에 저장한다.
> - **가졌는지 확인** — 모든 유물을 감시하되 발동 순간에 `run.relics`를 본다. 얻고 잃을 때마다 등록을 바꾸지 않아도 되고, 가진 목록이 플레이어 데이터라 Undo·저장이 따라온다.
> - **`RunOutcomes`** — 결과는 직접 `Run`하지 않고 이것으로 실행한다. 그래야 컨텍스트가 결과·Block에 보이고 기록에 "결과: …"(출처 `Relics`)가 남는다.
> - **`Permanent()`를 쓰지 않는다** — 유물 효과는 판의 일부라 이벤트를 쏜 행동과 함께 Undo되는 것이 맞다. 도전과제처럼 "되돌리지 않을 일"이면 `using (session.Data.Permanent())`로 감싼다.
> - **이벤트 받기로 발동시킨다** — 이벤트 받기로 발동한 유물은 이벤트를 쏜 행동 안에서 실행되므로 그 행동의 일부다. 조건 충족만으로 된 발동은 값이 바뀐 행동이 끝난 뒤의 평가에서 실행되어 행동 밖 변경이 된다. 판 값을 바꾸는 유물은 이벤트 받기로 만든다.

> [!note]- 다른 장르라면 — 퀘스트·일일 미션·튜토리얼 단계
> "언제 → 보상"이 계속 늘어나는 것은 유물과 같은 방식의 DB 종류다. 한 번 완료하는 기능은 `repeat: false`, 완료 기록은 메타 구역 플레이어 데이터, 완료와 보상은 `Permanent()` 안에서. 시작할 때 이미 조건을 넘은 항목은 등록 뒤 `session.Triggers.Watcher.EvaluateAll()`로 소급한다. 전체 예제는 [17. 프로젝트 고유 기능](../../Manual/17-Custom-Features.md).

## 9. 유물 — 닻 · 불타는 피

카드 던전의 유물은 DB › 패시브 효과 항목이다(8절 첫 접힌 칸 — 가진 목록 `run` › `relics`).

**닻** — "전투 첫 턴에 방어도". 패시브 효과 `anchor`: 발동 `이벤트 받기` → `턴 시작`, 이벤트 조건(`filter`)에 `필드 검사` → `turn` `일치` `1`. 결과 `대상에게 효과` → 대상 `주인공` → 효과 `방어도`(amount).

`주인공`은 새 대상 Block이다. 패시브 효과의 사용자(가진 쪽)는 목록을 품은 `run`이라 `사용자 자신`으로는 주인공을 고를 수 없다.

```csharp
[Serializable, BlockInfo("주인공", Category = "대상", Description = "이번 판의 주인공(고른 대상은 버립니다). 사용자가 주인공이 아닌 유물·도전과제의 결과에서 씁니다.")]
public class HeroTarget : TargetSelector<Fighter>
{
    protected override IEnumerable<Fighter> Select(IEnumerable<Fighter> targets, Fighter user)
    {
        var hero = Game.Data().Run.hero;
        if (hero != null && !hero.IsDead) yield return hero;
    }
}
```

**불타는 피** — "전투에 이기면 체력 회복, 최대 체력까지". 패시브 효과 `burningBlood`, 발동 = `이벤트 받기` → `전투 승리`, 결과는 코드로 쓴다(새로 읽기 뒤 `GameIds.Passive.BurningBlood`가 생기면). 회복량은 밸런싱 값 `burningBloodHeal`(6).

```csharp
[BindTo(GameIds.Passive.BurningBlood)]
static void BurningBlood(BattleWon e, TriggerContext c)
{
    var hero = c.Game.Data().Run.hero;
    var heal = (int)c.Game.Balance(GameIds.Balance.BurningBloodHeal);
    hero.hp = System.Math.Min(hero.maxHp, hero.hp + heal);
}
```

> [!note]- 왜 `주인공`은 Block인가
> - 유물은 `Game.Use`로 쓰는 것이 아니라 이벤트를 받아 발동하므로 주인공이 사용자가 아니다(패시브 효과면 가진 쪽 `run`, 직접 만든 DB 종류·도전과제면 사용자 없음). 플레이어가 고른 대상도 없다.
> - 사용자가 주인공이 아닌 다른 유물·도전과제도 이 Block을 쓸 수 있으니 `[BindTo]`가 아니라 Block이 맞다.
> - 대상 Block은 고르기만 하고 값을 바꾸지 않는다. 컴파일하면 대상 목록의 "대상" 분류에 `주인공`이 나온다.

> [!note]- 가시 갑옷 — 받는 피해 −5(패시브 효과 · 행동 직전 이벤트, 코드 없음)
> 이벤트는 일이 일어난 **뒤**에 온다. "받는 피해 −5"는 피해가 들어가기 **직전**에 내용을 고쳐야 하므로 행동 직전 이벤트로 만든다. 다른 유물처럼 패시브 효과다(8절 첫 접힌 칸).
>
> 항목 `thornArmor`(`PassiveData`). 칸: 가진 목록 기본값 `run` › `relics`, 발동 `행동 직전 이벤트 받기` → 행동 직전 이벤트 `피해`, 행동 직전 이벤트 조건(`filter`) `필드 검사` → `targetId` `일치` `hero`, 결과 `행동 직전 이벤트 고치기` → 필드 `amount`, 방법 `더하기`, 값 `-5`.
>
> - `Rules.Damage`가 피해를 주기 직전에 `DamageIntent`(`sourceId`·`targetId`·`amount`·`cancelled`)를 `Game.Intercept`로 통과시킨다. 이 패시브 효과가 `amount`를 줄이고, 0 이하가 되면 피해가 들어가지 않는다. 방어도는 줄어든 뒤의 값으로 깎인다.
> - "주인공이 맞을 때만"을 가진 쪽 필드가 아니라 행동 직전 이벤트 조건으로 정하는 이유: 가진 쪽이 목록을 품은 `run`이라 id가 없다. 가진 쪽 필드는 가진 쪽에 id가 있을 때(동료마다의 특성 `party[].traits`) 쓴다.
> - 패시브 효과의 발동·결과 칸은 행동 직전 이벤트 고치기를 처음부터 허락한다. 직접 만든 `RelicData`로 할 때만 잠금에 `TriggerKinds.Intercept`·`OutcomeKinds.Intent`를 넣는다(8절).
> - 시험: 테스트 명령 "가시 갑옷 얻기"(결과 플레이어 데이터에 주기 → `run › relics`에 `thornArmor`)를 만들어 두고, 개발자는 그것을 누른 뒤 턴을 끝낸다. 적 공격이 5씩 줄어 들어온다. Undo하면 함께 돌아간다.
> - "공격한 적에게 3 되돌려 주기"는 피해가 들어간 **뒤**의 일이라 이벤트 받기(`DamageDealt` 이벤트 — 이름이 같은 행동 직전 이벤트와는 목록이 따로다)로 만든다.
> - 행동 직전 이벤트 클래스는 `Rules/Events/Intents.cs`에 있다(`[Serializable, Intent("피해")]`). 순서·막기·`[BindTo]`로 쓰는 법은 매뉴얼 [28장](../../Manual/28-Attributes-Intercepts.md) 5-4·5-5.

> [!note]- 투척 단검 — 적 하나를 골라 피해(선택 요구, 코드 없음)
> 유물에는 플레이어가 고른 대상이 없다. "전투 첫 턴에 적 하나를 골라 피해 5"처럼 플레이어가 고르게 하려면 결과 `선택 요구`를 쓴다.
>
> 패시브 효과 `throwingKnife`: 발동은 닻과 같다(`턴 시작`, `turn` `일치` `1`). 결과 `선택 요구` → `prompt` `피해를 줄 적`, `candidates` `모든 적`, `min` `1`, `max` `1`, `effects` `피해`(5). DB 요약은 `모든 적에서 1개 고르기: 피해 5`.
>
> - 턴 시작을 쏜 행동은 답을 기다리지 않고 끝난다. 플레이어가 고르면 답하기가 행동 하나로 실행되어 피해가 들어간다. Undo 한 번이면 피해만 돌아가고 다시 고르기를 기다린다.
> - 고르기 창은 기본 화면이 저절로 연다(매뉴얼 35장 2-5). 기다리는 동안(`Game.IsWaiting`) `GameView.Busy`라 `OnClick` 입력이 막힌다. 시스템은 막지 않는다.
> - 개발자 시험: 모니터 **명령** 탭 맨 위 "선택 대기 · 피해를 줄 적" 패널에서 적 버튼을 누르고 **답하기**. 버튼에는 후보의 텍스트 `<id>.name`이, 없으면 id(`enemy1` …)가 보인다.
> - 코드에서 고르게 하는 `Game.Ask`(카드 1장 버리기·보상 3개 중 1개)는 매뉴얼 [29장](../../Manual/29-Statuses-Schedules-Choices.md).

## 10. 등장시키기

항목을 만들어도 게임 어딘가에서 그 ID를 쓰지 않으면 나오지 않는다.

| 항목 | 들어가는 자리 |
|---|---|
| 카드 | 시작 덱(`StartRun`), 상점 목록 |
| 몬스터 | 전투 목록(`StartBattle.monsters`) |
| 물약 | 인벤토리(`RunState.potions`) |
| 유물·패시브 효과 | 가진 유물(`RunState.relics`) |
| 상태 효과 | 거는 효과(`상태 효과 걸기`)·`Game.ApplyStatus` |
| 로그·도전과제 | 없음(이벤트를 받으면 저절로) |

조각마다 얻기·쓰기·걸기 **테스트 명령**(DB › 개발 › 개발 명령)을 반영 스크립트로 함께 만든다(관례 19절, [01장](01-StartProject.md) 4절). `category`로 묶는다("조각"·"판" …).

- 값을 넣기만 하면 되는 것은 결과 Block으로 끝난다. 예: 유물 닻 얻기 → 결과 `플레이어 데이터에 주기` → `run › relics`, 넣을 요소 `anchor`.
- 규칙 함수를 거쳐야 하는 것은 그 항목 ID에 `[BindTo]`. 예: 덱 카드(원본 ID + 이 장의 id)는 `Rules.NewCard`가 만든다.

```csharp
[BindTo(GameIds.DebugCommand.GetChainLightning)]
static void GetChainLightning(TriggerContext c)
{
    var game = c.Game;
    game.Data().Run.deck.Add(Rules.NewCard(game, GameIds.Instance.ChainLightning));
}
```

누르면 행동 하나로 실행되어 Undo된다. 출시 빌드에는 버튼이 없다.

> [!note]- 보상과 수치도 DB에 — 가중치 표 · 밸런싱 값
> **가중치 표**(DB › 가중치 표): "무엇이 얼마나 자주 나오나"를 표로 둔다. 예: 전투 보상 유물 `relicDrop` — 줄 `anchor` 무게 3, `thornArmor` 무게 1, 꽝 무게 4.
> - 코드 없이 주기: 결과 **가중치 표에서 주기**(표 · 횟수 · 넣을 플레이어 데이터 `run` · 필드 `relics`). 꽝이면 아무것도 넣지 않는다.
> - 코드 없이 고르게 하기: 결과 **가중치 표에서 고르기**(표 · 후보 수 `3` · 고른 것을 넣을 곳). 셋을 보이고 고른 것만 넣는다.
> - 코드에서(행동 안): `Game.Roll(GameIds.WeightTable.RelicDrop)`(하나, 꽝이면 `null`), `Game.Roll(id, 3, unique: true)`(서로 다른 셋 — 카드 보상 3장을 뽑아 `Game.Ask`로 1장 고르게 할 때. 덱은 `DeckCard` 목록이라 원본 ID를 바로 넣지 못해 코드로 한다).
> - 풀이 바뀌는 것(가진 유물은 다시 안 나오게 · 해금한 카드만 · 판에서 만들어진 목록)은 표의 뺄 목록 · 줄의 조건 · 줄 종류 플레이어 데이터 목록으로 둔다. 풀은 저장하지 않고 뽑을 때마다 만든다(매뉴얼 30장 5-10). 확률 공개는 `session.Tables.Chances(id)`.
>
> **밸런싱 값**(DB › 밸런싱 값): 개발자가 바꿀 숫자에 이름을 붙인다. 만드는 법·코드는 [03장](03-GameLoop.md) 4절. 없는 ID면 `InvalidOperationException`이다(코드에는 늘 `GameIds` 상수).
>
> 둘 다 플레이 중에 개발자가 DB에서 고치면 저장 전이라도 0.5초 안에 실행 중인 게임에 반영된다([08장](08-TestAndDebug.md) 2절). 수치가 많으면 CSV로 주고받는다(관례 15절, 매뉴얼 [30장](../../Manual/30-Balance-Tables.md)·[33장](../../Manual/33-Tick-Preview-Csv.md)).

## 11. 모니터로 시험하고 검증하기

AI는 파일로 확인한다(관례 12절).

1. 규칙 테스트: 얻기·쓰기·발동·Undo를 테스트로 두고 `Logs/MomoiProject/RunTests.request` → `Logs/MomoiProject/TestResults.txt`.
2. DB 검증: `Logs/MomoiProject/DbStatus.txt`가 "상태: 검증 통과"인지, '적용 전'으로 남은 고치기·지우기가 있는지 본다. 오류가 있으면 빌드가 막힌다.
3. 개발자에게 모니터로 볼 것(아래 표)과 Ctrl+S가 필요한 '적용 전' 항목을 알린다.

> [!note]- 항목마다 모니터에서 확인할 것(개발자)
> | 항목 | 모니터에서 | 확인할 것 |
> |---|---|---|
> | 카드·물약 | 게임 화면에서 내기·마시기 → **행동** 탭 | "카드 내기" 아래 "액티브 효과 사용: fireball (hero)"가 안쪽 행동으로 보이고, **타임라인** 묶어 보기에서 그 아래에 바뀐 값·이벤트가 이어진다. **Undo** 한 번에 에너지·피해·물약 개수가 함께 돌아간다. |
> | 로그 | **이벤트** 탭에서 `턴 끝`을 고르고 `damage`를 채워 **이벤트 호출**(자주 쓰면 개발 명령 "한 턴 30 피해" = 결과 `이벤트 쏘기`) | 아래 "반응하는 로그"에 `maxTurnDamage`가 보이고, **로그** 탭의 값이 바뀐다. |
> | 도전과제 | **도전과제** 탭에서 카드를 누르고 **트리거로 달성** | 부족한 로그를 이벤트로 채워 게임과 같은 길로 달성된다. Undo하면 로그는 돌아가고 달성은 남는다. 연쇄 반응 없이 결과만 보려면 **강제 달성**. |
> | 유물 닻 | 명령 "닻 얻기"를 누르고, 이벤트 탭에서 `턴 시작`(turn 1)을 호출 | `run › hero › block`이 오른다. turn 2로 호출하면 그대로다. |
> | 유물 불타는 피 | 데이터 탭에서 `run › hero › hp`를 최대보다 낮추고, 이벤트 탭에서 `전투 승리` 호출 | 체력이 `burningBloodHeal`만큼 오른다(최대 체력까지). 시작 유물이라 `relics`에 이미 들어 있다. |
>
> 이벤트 호출로 쏜 이벤트는 행동 하나("… — 모니터 이벤트 호출")로 기록되어 Undo된다. 이벤트를 모니터에서 쏘려면 이벤트 클래스에 매개변수 없는 생성자가 있어야 한다.

> [!note]- 자주 보는 검증 오류
> - "이벤트 받기: 이벤트가 선택되지 않았습니다." — 트리거의 이벤트를 고르지 않았다(반영 스크립트에서 `eventType`을 비웠다).
> - "… 로그에 기록의 반영할 값 '…'을 읽을 이벤트가 트리거에 없습니다(비우면 횟수 1)." — 반영할 값을 고른 뒤 트리거의 이벤트를 바꿨다.
> - "…: 없는 인스턴스 '…'를 가리킵니다." · "…: 없는 … '…'를 가리킵니다. 목록에서 다시 고르거나 …" — `[DataRef]` 칸의 항목이 지워졌거나 ID가 틀렸다.
> - "필수 값 '…'(…)이 비어 있습니다." — 필수로 정한 칸(그림 등)이 비었다([05장](05-WorkingWithDesigners.md) 6절).
> - "…은(는) 키워드 그룹 …에 없는 키워드입니다." — 키워드 칸 값이 코드의 목록에 없다.
> - "발동(트리거)이 없습니다." — 직접 만든 DB 종류(8절 `RelicData`)의 발동이 비었다(그 종류의 `ValidateData`가 낸다).
> - 경고 "발동(트리거)도 주기도 없어 결과가 돌지 않습니다." — 패시브 효과에 결과만 넣었다. 발동이나 주기를 정하거나, 보정만 쓰려면 결과를 비운다.
> - 경고 "대상에게 효과: 효과가 없습니다." — 결과는 넣었지만 효과 Block이 비었다.

## 12. 체크리스트

- [ ] 새 항목마다 클래스 → 이벤트 → Block → 반영 스크립트 → 새로 읽기 → `[BindTo]` → 등장 → 시험·검증을 밟았다.
- [ ] 수치만 다른 항목에 코드를 쓰지 않았다. `[BindTo]` 안의 숫자도 항목 칸·밸런싱 값에서 읽는다.
- [ ] 반영 스크립트는 이미 있는 ID를 건너뛴다(개발자가 맞춘 값을 덮지 않는다).
- [ ] 쓰는 순간 효과가 나는 인스턴스에 사용 기능이 붙어 있다(`[BindTo]`로 결과를 쓰는 것도).
- [ ] 새 Block은 동작 하나만 하고 `[Serializable]`·`[BlockInfo]`(설명·문장)가 있다. 대상 Block은 고르기만 한다.
- [ ] `[BindTo]` 인자는 `GameIds` 상수다.
- [ ] 판 데이터에는 원본 ID와 바뀌는 값만 들어간다.
- [ ] 도전과제의 결과는 프로필 쪽 값에 준다.
- [ ] 프로젝트 DB 종류는 `BoundOutcomes`를 확인하고 `RunOutcomes`로 결과를 실행한다.
- [ ] 항목의 ID가 게임에 나오는 자리에 들어가 있고, 얻기·쓰기·걸기 테스트 명령이 있다.
- [ ] 규칙 테스트가 통과하고 `DbStatus.txt`가 "검증 통과"다. 개발자에게 모니터로 볼 것을 알렸다.

> [!note]- 더 보기(매뉴얼)
> - [04. 인스턴스](../../Manual/04-Instances.md) — 만들기, 기능 필드, 사본
> - [16. 액티브 효과와 사용 기능](../../Manual/16-Abilities-Usable.md) — `Usable`, `Effect<T>`·`TargetSelector<T>`, `Game.Use`의 흐름
> - [11. Trigger와 조건](../../Manual/11-Triggers-Conditions.md) — 이벤트 받기·값 조건, 코드로 쓰는 조건 Block
> - [12. 결과(Outcome)](../../Manual/12-Outcomes.md) — 결과 종류, 대상에게 효과, `Reward`, 값 틀
> - [13. `[BindTo]`](../../Manual/13-BindTo.md) — 함수 모양, 연결 실패 알림
> - [14. 도전과제](../../Manual/14-Achievements.md) · [15. 로그](../../Manual/15-Stats-Logs.md)
> - [17. 프로젝트 고유 기능](../../Manual/17-Custom-Features.md) — 패시브 효과(놓인 것마다·가진 동안 스탯 보정·빌려 쓰기), DB 종류 + 감시, 퀘스트 전체 예제
> - [37. 자리](../../Manual/37-Placement.md) — `Placement`, 대상 '자리로 고르기'·조건 '자리 검사'
> - [29. 상태 효과·예약·선택 대기](../../Manual/29-Statuses-Schedules-Choices.md) — 상태 효과의 칸, 선택 요구, `Game.Ask`, 예약
> - [30. 밸런싱 값·곡선·가중치 표·플레이어용 실패](../../Manual/30-Balance-Tables.md) — 곡선, `Game.Roll`, 결과 '가중치 표에서 주기'
> - [21. DB](../../Manual/21-Editor-Database.md) — DB 반영 스크립트, 잠금
> - [22. 모니터와 디버깅](../../Manual/22-Monitor-Debug.md) — 이벤트 호출, 트리거로 달성
