# 04. 콘텐츠 더하기 — 카드·몬스터·물약·로그·도전과제·유물

게임 루프가 돌면 이후의 일은 대부분 "새 항목 더하기"다. 항목마다 같은 절차를 밟고, 코드는 새 동작(Block)이나 그 항목만의 예외(`[BindTo]`)가 필요할 때만 쓴다. "가지고 있으면 발동"하는 유물은 기본 종류 **패시브**로 코드 없이 만들 수 있고(8절 접힌 칸), 기본 기능에 없는 규칙이 있는 새 종류는 프로젝트가 DB 종류로 직접 만든다(8절은 그 방법을 유물로 보인다).

> [!note]- 이 장에서 쓰는 말
> | 말 | 뜻 |
> |---|---|
> | 인스턴스 | 클래스의 원본 값 하나(타격 카드, 드래곤 …)를 DB › 인스턴스에 채워 둔 항목. 게임 중에는 읽기만 하고, 판 데이터에는 원본 ID와 바뀌는 값만 둔다. |
> | 사용 기능(`Usable`) | 클래스에 붙이는 기능 필드. 붙어 있는 인스턴스는 게임이 켜질 때 인스턴스 ID로 등록되어 `Game.Use(인스턴스 ID, 사용자, 대상들)`로 쓴다. |
> | Trigger | DB 항목의 "언제" 칸. 잎(이벤트 받기 · 조건 충족 · 사용)을 AND·OR·NOT으로 조립한다. |
> | Outcome(결과) | DB 항목의 "무엇을" 칸. 로그에 기록 · 플레이어 데이터에 주기 · 대상에게 효과 · 선택 요구 · 이벤트 쏘기 · 코딩한 결과. |
> | 상태 효과 | DB › 상태 효과의 항목. 대상에게 걸려 있는 동안 효과가 나는 버프·디버프(독·보호막). 중첩·지속(턴 수)·능력치 보정·트리거와 결과를 정하고, 효과 Block `상태 효과 걸기`로 건다. |
> | 선택 요구 | 후보 중에서 플레이어가 고르게 하는 결과 Block. 행동은 답을 기다리지 않고 끝나고, 고르면 고른 것에 효과가 따로 실행된다. |
> | Block | 기획자가 DB에서 골라 조립하는 로직 단위. 대상 Block(누구에게, `TargetSelector<T>`)·효과 Block(무엇을, `Effect<T>`)·조건 Block(`SimpleCondition`)·코딩한 결과(`Reward`). 프로그래머가 동작 하나를 Block 하나로 만든다. |
> | `[BindTo]` | static 함수에 붙여 그 ID를 가진 DB 항목 하나의 결과를 코드로 바꾸는 어트리뷰트. 그 항목의 결과 칸은 잠긴다. |
> | 컨텍스트(`TriggerContext`) | Trigger가 발동한 순간의 정보: 이벤트(`Event`), 사용자(`User`), 고른 대상(`Picked`), 항목 ID(`Owner`), `Game`. 결과·`[BindTo]` 함수가 읽는다. |
> | DB 종류 | `GameData`를 상속한 클래스. 상속하면 DB에 탭이 생긴다. |
> | 패시브 | DB › 패시브의 항목. 가진 목록(`run › relics` 같은 ID 목록, 조커 칸 같은 객체 목록)에 그 ID가 있는 동안 발동(Trigger)마다 결과를 내고, 가진 동안 능력치 보정을 붙인다. 유물·특성·패시브 스킬·조커를 코드 없이 만드는 기본 종류. |
> | 가중치 표 · 밸런스 값 | DB › 가중치 표(무게를 붙인 뽑기 표, `Game.Roll`)와 DB › 밸런스 값(이름 붙은 숫자·곡선, `Game.Balance`). 10절 접힌 칸. |

---

## 1. 표준 절차

새 항목마다 이 순서로 한다.

1. **클래스**: 필요한 칸이 있는지 본다. 없으면 필드를 더한다(이미 있는 인스턴스는 필드 초기값을 받는다).
2. **항목**: DB에서 만들고 값을 채운다. 쓰는 순간 효과가 나면 **+ 기능**으로 사용 기능을 붙인다.
3. **이벤트**: 반응할 순간의 이벤트를 이미 쏘는지, 필요한 값이 이벤트 필드에 있는지 본다. 없으면 더하고 행동 안에서 쏜다.
4. **Trigger**: 언제 발동하는지 조립한다(사용 기능은 기본 `사용`).
5. **결과**: 이미 있는 Block으로 조립한다. 안 되면 Block 하나 또는 `[BindTo]` 함수 하나(2절).
6. **등장시키기**: 덱·상점·전투 목록처럼 게임에 나오는 자리에 ID를 넣는다(10절).
7. **저장**: Ctrl+S. `GameIds`에 새 상수가 생긴다.
8. **시험과 검증**: 모니터로 반응과 Undo를 보고, DB 사이드바 아래가 "검증 통과"인지 본다(11절).

> [!note]- 코드가 필요한가
> | 새 항목이 | 할 일 | 코드 |
> |---|---|---|
> | 이미 있는 클래스·Block으로 수치만 다르다 | DB에서 항목 만들기 | 없음 |
> | 이미 있는 동작을 새로 조합한다(피해 + 화상) | 결과 칸에 Block 여러 개 | 없음 |
> | 새 동작이 필요하고, 다른 항목도 쓸 만하다 | Block 클래스 하나 | Block 하나 |
> | 그 항목에만 있는 흐름이다 | `[BindTo]` 함수 하나 | 함수 하나 |
> | 반응할 순간이 아직 없다 | 이벤트(또는 필드)를 더하고 행동에서 쏜다 | 이벤트 + 쏘는 한 줄 |
> | 가지고 있으면 발동한다(유물·특성·장비 효과) | DB › 패시브에서 항목 만들기 | 없음(8절 접힌 칸) |
> | 기존 기능(도전과제=한 번, 로그=숫자, 사용 기능=쓰는 순간, 패시브=가진 동안)과 규칙이 다른 새 종류다 | DB 종류 + 감시 등록 | 8절 |

## 2. Block과 `[BindTo]` 고르기

- 여러 항목이 수치만 바꿔 다시 쓸 동작 → **Block**.
- 그 항목 하나에만 있는 흐름 → **`[BindTo]`**.
- 같은 `[BindTo]` 코드가 두 번째 항목에서도 필요해지면 그때 Block으로 올린다. Block을 미리 많이 만들지 않는다.

> [!note]- 자세한 기준
> **Block**이 맞는 때:
> - 동작이 짧고 이름 하나로 설명된다("피해", "방어도", "모든 적").
> - 다른 카드·물약·유물도 쓸 수 있다.
> - 수치를 기획자가 정하고 바꾼다. Block의 public 필드가 DB 칸이 된다.
> - 조합해서 새 항목이 생긴다. 기획자가 프로그래머 없이 "피해 + 화상" 카드를 만든다.
>
> **`[BindTo]`**가 맞는 때:
> - 그 항목 하나에만 있는 흐름이다(대상마다 수치가 바뀜, 조건 분기, 여러 값을 함께 계산).
> - Block으로 쪼개면 그 항목 하나 때문에 Block이 여러 개 생기거나, 블록 조합이 스크립트처럼 길어진다.
> - 수치까지 코드에 있어도 된다. 결과 칸이 잠기므로 기획자는 Trigger만 고친다.
>
> 블록 조합으로 스크립트 언어를 만들지 않는 것이 원칙이다.

> [!note]- `[BindTo]` 함수의 모양
> `static`, 반환 `void`, 매개변수는 `()` · `(이벤트)` · `(TriggerContext)` · `(이벤트, TriggerContext)` 중 하나. 받을 이벤트는 첫 매개변수의 형식으로 정해진다. 사용 기능·능력에는 이벤트가 없으므로 `(TriggerContext c)`만 쓴다. 인자는 글자 대신 `GameIds` 상수로 쓴다. 오타나 ID 이름 바꾸기가 컴파일 오류로 먼저 드러난다.

## 3. 몬스터 — 드래곤 (코드 없음)

1. DB › **인스턴스** → **+ 새로 만들기** → ID `dragon`.
2. 형식(`instanceType`)에서 `MonsterData`를 고른다.
3. 값을 채운다: `title`, `maxHp`, `attackPattern`(턴마다 차례로 주는 피해), `element`, `portrait`.
4. Ctrl+S. `GameIds.Instance.Dragon`이 생긴다.

> [!note]- 전투에서 어떻게 쓰이나
> 전투를 여는 행동의 몬스터 목록(`StartBattle.monsters`)에 ID를 넣으면 나온다. 전투 시작 때 `Rules.NewEnemy`가 원본에서 이름·최대 체력을 읽어 `Enemy`를 만든다. 판 데이터에 들어가는 것은 원본 ID와 바뀌는 값(체력·공격 차례)뿐이고, 적은 턴마다 원본의 `attackPattern`을 읽는다. 그래서 기획자가 공격 순서를 고치면 진행 중인 판의 드래곤에도 바로 반영된다.

## 4. 카드 — 화염구 (Block 조합)

1. 인스턴스 `fireball`, 형식 `Card`. `title`, `cost`, `rarity`, `element`(`fire`), `tags`(`attack`), `art`를 채운다.
2. **+ 기능** → `usable`. 트리거(`when`)는 기본 `사용` 그대로.
3. 결과(`then`) **+ 결과 추가** → `대상에게 효과`. 대상(`targets`)은 비워 둔다(= 플레이어가 고른 적).
4. 효과(`effects`) **+ 효과 추가** → `피해`(amount) → `상태 효과 걸기`(상태 효과 `burn`, 중첩 `1`).

화상 `burn`은 DB › **상태 효과**의 항목이다(아래 접힌 칸). `상태 효과 걸기`는 기본 제공 효과라 코드가 없다.

`피해` 같은 효과 Block은 프로그래머가 만든다. 동작 하나만 한다.

```csharp
[Serializable, BlockInfo("피해", Category = "카드", Description = "대상에게 피해를 줍니다(방어도 먼저).")]
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
> - `[BlockInfo]`의 이름·분류·설명이 기획자가 보는 목록이 되고, `Describe`가 상세 위쪽 요약에 수치까지 보이게 한다. 한 줄 문장(`Sentence`) 등 기획자 화면 다듬기는 [05장](05-WorkingWithDesigners.md) 5절.
> - 기획자가 넣으면 안 되는 값은 Block의 `Validate`로 막는다. 0 이하 피해를 DB 검증 오류로 만드는 예는 [05장](05-WorkingWithDesigners.md) 7절.

**몇 턴 동안** 이어지는 효과(턴마다 피해를 주는 화상·독, 3턴 동안의 보호막)는 카드 결과에 직접 쓰지 않는다. DB › **상태 효과**에 항목으로 만들고, 카드 효과에는 기본 제공 `상태 효과 걸기`를 둔다.

> [!note]- 화상 — 턴 끝마다 피해 2, 2턴(상태 효과, 코드 없음)
> 1. DB › **상태 효과** → **+ 새로 만들기** → ID `burn`.
> 2. `duration`: `kind` `이벤트 횟수`, `length` `2`, `tickEvent` `턴 끝`.
> 3. `trigger`: **+ 블럭** → `이벤트 받기` → `턴 끝`.
> 4. `outcomes`: **+ 결과 추가** → `대상에게 효과` → 대상은 비워 둔다 → 효과 `피해`(2).
> 5. Ctrl+S. 플레이어에게 보일 이름은 텍스트 `burn.name`("화상").
>
> - 상태 효과의 결과에서는 사용자·고른 대상이 **가진 쪽**(화상이 걸린 적)이다. 그래서 대상을 비워 두면 화상이 걸린 적이 피해를 받는다.
> - `maxStacks`(1)·`reapply`(중첩 더하고 지속 갱신)는 기본값 그대로다. 화염구를 또 맞으면 중첩은 1 그대로, 남은 횟수가 2로 돌아간다.
> - 맨 위 칸 `kind`(종류: 없음·버프·디버프)는 `duration` 안의 `kind`(방식)와 다른 칸이다. 디버프로 두면 '상태 효과 풀기'(종류 전부 = 정화)·`Game.StatusCount(대상, 종류)`에 잡힌다.
> - 피해는 `피해` Block → `Rules.Damage`를 거치므로 방어도·가시 갑옷 같은 가로채기·처치 이벤트가 카드 피해와 같다. 마지막 적이 화상으로 쓰러지면 `EndTurn`이 전투 승리로 넘어간다([03장](03-GameLoop.md) 4절).
> - 시험: 화염구를 내고 모니터 **데이터** 탭 `$statuses`에서 `burn`의 `remaining`(2)을 본 뒤 턴을 끝낸다. 적이 2 피해를 받고 `remaining`이 1이 된다. **Undo**하면 피해와 남은 횟수가 함께 돌아간다.

> [!note]- 독 — 턴 끝마다 중첩만큼 피해, 3턴(상태 효과)
> 1. DB › **상태 효과** → **+ 새로 만들기** → ID `poison`.
> 2. `maxStacks` `0`(제한 없음), `reapply` `중첩 더하고 지속 갱신`.
> 3. `duration`: `kind` `이벤트 횟수`, `length` `3`, `tickEvent` `턴 끝`.
> 4. `trigger`: **+ 블럭** → `이벤트 받기` → `턴 끝`.
> 5. 결과는 중첩 수를 읽는 계산이라 함수 하나로 쓴다. Ctrl+S로 `GameIds.Status.Poison`을 만든 뒤 Rules에 쓴다.
> ```csharp
> [BindTo(GameIds.Status.Poison)]
> static void Poison(TurnEnded e, TriggerContext c)
> {
>     var holder = (Fighter)c.User;                                              // 가진 쪽(독이 걸린 적·주인공)
>     Rules.Damage(c.Game, null, holder, c.Game.StatusStacks(holder, c.Owner));   // Owner = 상태 효과 ID
> }
> ```
> 6. 카드 `poisonDart`(독침): 결과 `대상에게 효과` → 대상 비움(고른 적) → 효과 `피해`(4) → `상태 효과 걸기`(`poison`, 중첩 `3`).
>
> - 같은 턴 끝이 피해와 횟수 줄이기를 둘 다 하면 피해가 먼저다. 피해 세 번 뒤에 끝난다. 다시 걸면 중첩이 더해지고 3턴으로 돌아간다.
> - 독 피해도 `Rules.Damage`를 거치므로 가시 갑옷 같은 가로채기를 탄다(9절).
> - 걸린 독은 플레이어 데이터 `$statuses`에 들어 있어 Undo·판 저장이 따라온다. 시험: 독침을 내고 모니터 **데이터** 탭 `$statuses`에서 `stacks`·`remaining`을 본 뒤 턴을 끝낸다. **Undo**하면 피해와 남은 횟수가 함께 돌아간다.
> - 화상과 다른 점: 화상은 피해가 고정이라 결과를 Block으로 조립했고(코드 없음), 독은 중첩 수만큼이라 결과를 `[BindTo]` 함수로 쓴다.
> - 코드에서 걸 때는 `Game.ApplyStatus(enemy, GameIds.Status.Poison, stacks: 3)`, 화면의 상태 아이콘은 `Game.Statuses(fighter)`·`Game.StatusStacks`로 읽는다. 상태 효과 없이 "2턴 뒤 이 행동"만 걸 때는 예약(`Game.ScheduleOn<TurnEnded>(2, 행동)`)을 쓴다.
> - 능력치 보정(분노 +10%)·가로채기(보호막 "내가 받는 피해 반감")·끝날 때 결과("3턴 뒤 폭발")도 같은 항목의 칸이다. 매뉴얼 [29장](../Manual/29-Statuses-Schedules-Choices.md) 5장.

## 5. 카드 — 연쇄 번개 (`[BindTo]`)

"적마다 차례로 3, 4, 5 … 피해"는 그 카드만의 흐름이라 함수 하나로 쓴다.

1. 인스턴스 `chainLightning`(형식 `Card`)에 **+ 기능** → `usable`. 결과는 비워 둔다.
2. Ctrl+S로 `GameIds.Instance.ChainLightning`을 만든다.
3. Rules에 함수를 쓴다.

```csharp
[BindTo(GameIds.Instance.ChainLightning)]
static void ChainLightning(TriggerContext c)
{
    var hero = c.User as Fighter;
    var damage = 3;
    foreach (var enemy in Rules.LivingEnemies(c.Game).ToList()) Rules.Damage(c.Game, hero, enemy, damage++);
}
```

> [!note]- 자세히
> - 사용 기능은 결과가 비어도 붙여 둔다. 붙어 있어야 `Game.Use`로 쓸 수 있는 항목으로 등록된다.
> - 사용이라 이벤트가 없으므로 매개변수는 컨텍스트 하나다.
> - 컴파일하면 DB에서 이 인스턴스의 결과 칸 자리에 "코드로 쓴 결과가 이 항목에 연결되어 있습니다([BindTo])." 안내와 `CardCode.ChainLightning`, **cs 열기**가 보인다.

## 6. 물약 — 회복 물약 (코드 없음)

1. 인스턴스 `healPotion`, 형식 `Potion`. `title`, `maxStack`을 채운다.
2. **+ 기능** → `usable` → 결과 `대상에게 효과` → 대상 `사용자 자신` → 효과 `회복`(amount).

> [!note]- 마시는 행동
> 물약은 가방(인벤토리)에 원본 ID와 개수로만 들어간다. 한 칸에 몇 개까지 겹칠지도 고정 값이라 가방에 넣을 때 원본의 `maxStack`을 읽는다(`Rules.PotionOf`). 마시는 행동이 개수를 줄인 뒤 원본의 사용 기능을 쓴다. 한 행동이라 Undo 한 번에 함께 돌아가고, 물약이 없으면 예외로 아무것도 바뀌지 않는다.
> ```csharp
> var run = Game.Data().Run;
> if (!run.potions.TryRemove(potion)) throw Game.Fail("fail.noPotion", "물약이 없습니다.");
> if (target == null) Game.Use(potion, run.hero);
> else Game.Use(potion, run.hero, target);
> ```

> [!note]- 다른 장르라면
> - **RPG의 장비·소모품**: 아이템 클래스 하나에 기능 필드(`[SerializeReference] public Equip equip;`, `public Usable usable;`)를 두고 인스턴스로 더한다. 칸·개수는 인벤토리 시스템([25. 인벤토리](../Manual/25-Inventory.md)). 여러 아이템이 같은 효과를 나누면 아이템이 능력 ID를 가리키게 하고(`[DataRef(typeof(AbilityData))]`) 효과는 DB › 능력에서 한 번 조립한다.
> - **스킬**처럼 아이템이 아닌 "쓰는 것"은 DB › 능력에서 만든다. 게임 코드는 똑같이 `Game.Use(능력 ID, 사용자, 대상들)`이다.

## 7. 로그와 도전과제

**로그**는 이벤트를 받을 때마다 숫자를 쌓는다. 게임 코드는 이벤트만 쏘면 되고, 나머지는 DB › **로그** → **+ 새로 만들기**에서 범위(`scope`)·집계(`aggregate`)·반영 규칙(`triggers`)을 고른다.

| ID | 범위 · 집계 | 반영 규칙(이벤트 받기) | 반영할 값 |
|---|---|---|---|
| `kills` | 누적 · 합계 | 적 처치 | 비움(횟수 1) |
| `maxTurnDamage` | 누적 · 최고치 | 턴 끝 | `damage` |
| `goldEarned` | 누적 · 합계 | 골드 획득 | `amount` |

**도전과제** `bigTurn`(코드 없음): DB › **도전과제**에서 만들고, `trigger` **+ 블럭** → `비교` → `maxTurnDamage` `>=` `30`. 결과는 기획이 정한다(예: 플레이어 데이터에 주기 · `profile`의 필드).

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
> | 사용 기능(`Game.Use`), 로그, 행동 안에서 쏜 이벤트로 발동한 유물 | 그 행동과 함께 돌아간다 |
> | 도전과제 | 돌아가지 않는다(달성·결과는 Undo 이력 밖) |
>
> 그래서 도전과제의 결과는 행동이 건드리지 않는 값(프로필 쪽)에 준다. 판 데이터에 주면, 달성시킨 행동을 Undo할 때 그 값이 행동 전으로 덮일 수 있다. 해금은 판이 끝나도 남아야 하므로 어차피 프로필이 맞다.

## 8. 새 DB 종류 — 유물

유물은 "가지고 있는 동안, 어떤 순간마다, 무언가 한다"는 규칙이다. 이 규칙만이면 기본 종류 **패시브**가 그대로 맡는다(아래 첫 접힌 칸, 코드 없음). 이 절은 패시브에 없는 규칙(한 번만·하루 N번·단계·유물만의 칸)이 생길 때를 위해 DB 종류를 직접 만드는 법을 유물로 보인다. 할 일은 둘이다.

> [!note]- 기본 종류 '패시브'로 하면 — `RelicData`·`RelicRules` 없이
> "가지고 있는 동안 발동"은 DB › **패시브**가 기본으로 맡는다. 아래의 `RelicData`·`RelicRules`를 만들지 않고 같은 유물을 코드 없이 만든다.
> 1. DB › **패시브** → **+ 새로 만들기** → ID(예: `anchor`).
> 2. 가진 목록: `ownerKey` `run`, `ownerPath` `relics`(기본값 그대로). `run.relics`에 이 ID가 있는 동안 발동한다. 얻고 잃는 것은 그 목록에 넣고 빼는 것이다.
> 3. 발동(`trigger`)·결과(`outcomes`)는 9절의 유물과 같다. 이벤트 받기·조건 충족·가로채기와 의도 고치기가 처음부터 열려 있다.
> 4. Ctrl+S. `GameIds.Passive.Anchor`가 생긴다. 결과를 코드로 쓰려면 `[BindTo(GameIds.Passive.Anchor)]` — 등록과 `BoundOutcomes` 확인은 라이브러리가 한다.
>
> - 결과의 사용자(가진 쪽)는 목록을 품은 `run`이다('놓인 것마다'를 켜면 목록의 요소 하나, 아래 접힌 칸). 주인공에게 줄 효과는 대상에 `주인공` Block(9절)을 둔다.
> - 몇 개 가졌나는 `Game.Owned(GameIds.Passive.Anchor)`. 동료마다 다른 특성(`party[].traits`), 레벨이 있는 패시브(ID → 레벨 딕셔너리)도 같은 종류다(매뉴얼 [17장](../Manual/17-Custom-Features.md) 0절).
> - 가진 목록 칸의 `[DataRef(typeof(RelicData))]`는 `typeof(PassiveData)`로 바꾼다. 유물 DB 종류와 패시브를 섞어 쓰는 동안은 `typeof(GameData)`(모든 DB 항목)로 둔다.
> - 아이콘은 패시브 항목의 `icon`이다. 설명은 텍스트 `<ID>.desc`로 둔다.

> [!note]- 패시브 더 — 놓인 것마다 · 일치 필드 · 가진 동안 능력치 보정 · 필드 바꾸기 · 빌려 쓰기
> 카드 던전의 유물은 ID 목록 하나라 위 칸으로 충분하다. 조커 칸·보드처럼 **놓인 자리와 순서**가 효과를 바꾸는 조각, 가진 동안 능력치를 올리는 조각은 아래 칸을 더 쓴다. 기믹 갈래 전체는 AI 문서 [기믹 목록](../AI/GimmickCatalog.md).
>
> | 칸(툴팁) | 뜻 | 예 |
> |---|---|---|
> | `matchField`(일치 필드) | 객체 목록에서 요소의 이 필드(글자·글자 목록)에 패시브 ID가 있으면 가진 것. 비우면 요소 자신(ID 글자)·요소의 `id` | `run › jokers`(`{ id, joker, mult }`)의 `joker`, 유닛의 `traits` |
> | `perElement`(놓인 것마다) | 켜면 가진 쪽 = 목록의 요소 하나하나. 결과의 사용자 = 그 요소 | 조커 한 장 |
> | `levelField`(레벨 필드) | 요소의 이 숫자 필드가 레벨(0 이하면 꺼짐). 비우면 요소 하나 = 1 | 아티팩트의 `level` |
> | `modifiers`(가진 동안 능력치 보정) | 줄마다 능력치 · 칸(플랫·퍼센트·독립 곱) · 값 · 레벨마다 · 레벨별 값 | 힘의 부적: `attack` 플랫, 레벨별 값 2/3/4/6 |
> | `modifierTarget`(보정 받을 대상) | 가진 목록 플레이어 데이터 안의 객체 경로. 비우면 가진 쪽 | `hero` |
>
> 칸이 더 있다: `matchVia`(원본에서 읽기 — 원본 인스턴스의 필드에서 패시브 ID 찾기) · `rules`(발동 더(다른 순간) — 순간마다 다른 결과, Green Joker) · `pulse`(주기(초) — N초마다 결과) · `modifierWhen`(보정 조건 — 참일 때만 보정, Fury) · `stacking`(여럿일 때 — 레벨 합·값 합·가장 높은 것 하나). 매뉴얼 [17장](../Manual/17-Custom-Features.md) 0절.
>
> **놓인 것마다 — 목록 순서대로 발동**
> - 같은 목록을 보는 '놓인 것마다' 패시브끼리는 패시브 등록 순서가 아니라 목록 순서(자리)대로 섞여 발동한다. 조커 칸 왼쪽부터다.
> - "+4 배수" 조커가 왼쪽, "×2 배수" 조커가 오른쪽이면 (1+4)×2다. 자리를 바꾸면 1×2+4다.
> - 같은 조각이 둘이면 각자 발동한다. 발동 도중에 앞 칸의 결과가 목록에서 뺀 요소는 발동하지 않는다.
> - '놓인 것마다'를 끄면 예전처럼 가진 쪽마다 한 번 발동한다.
> - 조각 목록은 객체 목록(고유 `id` + 원본 ID 필드)으로 둔다. 같은 조각을 둘 가지고, 조각마다 쌓이는 값을 두기 위해서다.
> - 몇 개 가졌나: `Game.Owned(ID, 가진 쪽)`. 놓인 것마다면 가진 쪽 = 요소 하나(그 요소의 레벨)다.
>
> **가진 동안 능력치 보정 — 읽을 때 계산**
> - 저장하지 않는다. `Game.Attribute`·`Game.Modifiers`를 읽을 때 더한다.
> - 그래서 얻을 때 `AddModifier`, 잃을 때 `RemoveModifiers`를 부르지 않는다. 목록에서 빼면 저절로 빠지고, Undo·저장과 어긋나지 않는다.
> - `Game.Modifiers(대상, 능력치)` 목록에는 출처(`source`) = 패시브 ID로 보인다.
> - 레벨별 값은 1레벨부터다. 레벨이 넘치면 마지막 값이다. 레벨별 값이 있으면 값·레벨마다보다 먼저다.
> - 받는 쪽은 능력치를 가진 개체라 `id`가 있어야 한다. 카드 던전처럼 가진 목록이 `run › relics`면 가진 쪽 `run`에 id가 없으므로 보정 받을 대상에 `hero`를 적는다.
> - 받는 쪽 하나에 가진 쪽이 여럿이면(보정 받을 대상을 정했을 때 등) 기본은 레벨을 더해 한 번이다(`stacking`).
> - 트리거 없이 보정만 있어도 되고, 둘 다 없이 가졌는지만 보는 스위치(`Game.Owned`로 읽음)여도 된다.
>
> **조각마다 쌓이는 값 — 효과 '필드 바꾸기'**
> - 대상의 숫자 필드를 더하기·곱하기·값으로 바꾼다. 저장되는 값 자체를 바꾼다(보정이 아니다).
> - "이 조커 배수 +1": 결과 `대상에게 효과` → 대상 `사용자 자신`(놓인 것마다라 그 조커) → 효과 `필드 바꾸기`(`field` `mult`, `change` `더하기`, `value` `1`).
> - 값에는 틀 `{level}`·`{level:2/4/7}`·`{stacks}`·`{user:경로}`·`{event:경로}`·`{data:키.경로}`·`{count:키.경로}`·`{balance:ID}`를 쓸 수 있다(매뉴얼 12장 2-4a). 더하기 값은 효과 배율(Lumin 같은 "수치 2배")만큼 곱해진다. 카운터 −1은 '0 아래로 내리지 않음'을 켠다. 정수 필드는 반올림한다.
> - 잠깐 올리는 것은 '능력치 보정'이다.
>
> **옆 조각 흉내 — 결과 '빌려 쓰기'**
> - 빌릴 조각(대상 Block)이 고른 조각의 패시브를 지금 순간(이벤트·의도)으로 한 번 더 낸다. Blueprint·Brainstorm이다.
> - Blueprint = 패시브(`ownerPath` `jokers`, `matchField` `joker`, `perElement` 켬) + 결과 `빌려 쓰기`(빌릴 조각 `자리로 고르기` — `origin` `사용자(가진 쪽)`, `list` `run › jokers`, `direction` `오른쪽`). Brainstorm은 `direction` `목록 맨 처음`.
> - 빌려 쓰는 쪽의 발동도 그 순간에 참이어야 한다. 원본이 도는 순간이 여럿이면 발동을 `OR`로 묶는다.
> - 원본의 발동은 원본 조각을 가진 쪽으로 검사한다. 결과의 사용자는 원본 조각이다(원본의 값을 읽는다). 자기 자신은 빼고 빌린다.
> - 빌려 쓰는 중의 '필드 바꾸기'는 원본 자신의 필드를 바꾸지 않는다. 쌓이는 값은 원본이 직접 발동할 때만 쌓인다. 빌리는 것은 결과뿐이고, 원본의 가진 동안 능력치 보정은 빌리지 않는다.
> - 서로 빌리는 고리(Blueprint ↔ Brainstorm)는 끊는다. 코드로는 `Game.BorrowPassive(패시브 ID, 원본)`.

**데이터** — `GameData`를 상속하면 DB에 탭이 생긴다. Trigger 칸·결과 칸에 `[TriggerSlot]`·`[OutcomeSlot]`을 붙이면 도전과제와 같은 편집기·검증이 붙는다. Rules에 둔다.

```csharp
[Database("유물", Group = "콘텐츠", Order = 40, IdPrefix = "relic")]
public class RelicData : GameData
{
    [FieldLabel("설명")] public string description = "";
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
                session.Triggers.RunOutcomes(outcomes, moment, "Relics");
            });
        }
    }
}
```

게임이 켜질 때 부르는 연결은 Client의 Bootstrap `Started`에 한 줄 더한다([01장](01-StartProject.md) 4절).

```csharp
RelicRules.Register(session, GameDatabase.Load()?.All<RelicData>());
```

`RunState`에 가진 유물 목록 `[DataRef(typeof(RelicData))] public List<string> relics`를 더한다.

> [!note]- 데이터 칸 자세히
> - `[Database]`로 탭 이름·사이드바 그룹·순서·새 ID 접두어를 정한다. 유물 탭은 DB 사이드바 **콘텐츠** 그룹에 생긴다.
> - `Trigger`·`Outcome`은 추상 형식이라 `[SerializeReference]`가 꼭 있어야 한다.
> - 잠금은 기능에 맞게 고른다. 감시해서 발동하는 기능에 `사용`(`TriggerKinds.Use`)은 의미가 없다. 결과에서 `로그에 기록`(`Record`)을 뺀 것은, 유물 ID는 로그가 아니어서 로그 칸을 비운 기록이 아무것도 하지 않기 때문이다.
> - `TriggerKinds.Intercept`·`OutcomeKinds.Intent`를 넣으면 발동에 `가로채기`, 효과에 `의도 고치기`가 나온다. "받는 피해 −5"처럼 일어나기 직전의 일을 고치는 유물에 쓴다(9절 가시 갑옷).
> - 표시 이름 필드는 두지 않는다. ID가 이름이고, 플레이어에게 보일 이름은 텍스트로 붙인다([06장](06-Presentation.md)).
> - `ValidateData`를 덮어써 "발동이 없다", "비어 있는 효과 칸이 있다"를 오류로 낸다([05장](05-WorkingWithDesigners.md) 7절).

> [!note]- 감시 코드 한 줄씩
> - **Rules와 Bootstrap으로 나누는 이유** — 무엇을 감시하고 무엇을 할지는 규칙이라 Rules에 둔다. Unity의 게임 시작에 거는 것(`Started`, `GameDatabase.Load`)만 Client에 둔다. 서버라면 세션을 만든 뒤 같은 `RelicRules.Register`를 부른다.
> - **`BoundOutcomes.Has(id)`** — `[BindTo]` 연결은 기본 기능만 스스로 확인한다. 프로젝트 기능은 이 한 줄로 직접 확인해 바꿔 쓴다. 빠뜨리면 DB에는 결과 칸이 잠겨 보이는데 실제로는 숨은 예전 결과가 돈다.
> - **`Register(owner, when, repeat, fire)`** — `owner`는 항목 ID(컨텍스트의 `Owner`가 된다). 유물은 여러 번 발동하므로 `repeat: true`. 한 번만 발동하는 기능(퀘스트)이면 `false`로 두고 완료를 플레이어 데이터에 저장한다.
> - **가졌는지 확인** — 모든 유물을 감시하되 발동 순간에 `run.relics`를 본다. 유물을 얻고 잃을 때마다 등록을 바꾸지 않아도 되고, 가진 유물 목록이 플레이어 데이터라 Undo·저장이 따라온다.
> - **`RunOutcomes`** — 결과는 직접 `Run`하지 않고 이것으로 실행한다. 그래야 컨텍스트가 결과·Block에 보이고 Journal에 "결과: …"(출처 `Relics`)가 남는다.
> - **`Permanent()`를 쓰지 않는다** — 유물 효과는 판의 일부라, 이벤트를 쏜 행동과 함께 Undo되는 것이 맞다. 도전과제처럼 "되돌리지 않을 일"이면 `using (session.Data.Permanent())`로 감싼다.
> - **이벤트 받기로 발동시킨다** — 이벤트 받기로 발동한 유물은 이벤트를 쏜 행동 안에서 실행되므로 그 행동의 일부다. 조건 충족만으로 된 발동은 값이 바뀐 행동이 끝난 뒤의 평가에서 실행되어 행동 밖 변경이 된다. 판 값을 바꾸는 유물은 이벤트 받기로 만든다.

> [!note]- 다른 장르라면 — 퀘스트·일일 미션·튜토리얼 단계
> "언제 → 보상"을 기획자가 계속 더하는 것은 유물과 같은 방식의 DB 종류다. 한 번 완료하는 기능은 `repeat: false`, 완료 기록은 메타 구역 플레이어 데이터, 완료와 보상은 `Permanent()` 안에서. 시작할 때 이미 조건을 넘은 항목은 등록 뒤 `session.Triggers.Watcher.EvaluateAll()`로 소급한다. 전체 예제는 [17. 프로젝트 고유 기능](../Manual/17-Custom-Features.md).

## 9. 유물 — 닻 · 불타는 피

**닻** — "전투 첫 턴에 방어도".

1. DB › 유물 → `anchor`.
2. 발동(`trigger`): **+ 블럭** → `이벤트 받기` → `턴 시작`, 이벤트 조건(`filter`)에 `필드 검사` → `turn` `일치` `1`.
3. 효과(`outcomes`): `대상에게 효과` → 대상 `주인공` → 효과 `방어도`(amount).

`주인공`은 새 대상 Block이다. 유물은 이벤트로 발동해 사용자가 없으므로 `사용자 자신`이 아무도 고르지 않는다.

```csharp
[Serializable, BlockInfo("주인공", Category = "대상", Description = "이번 판의 주인공(고른 대상은 버립니다). 사용자가 없는 유물·도전과제의 결과에서 씁니다.")]
public class HeroTarget : TargetSelector<Fighter>
{
    protected override IEnumerable<Fighter> Select(IEnumerable<Fighter> targets, Fighter user)
    {
        var hero = Game.Data().Run.hero;
        if (hero != null && !hero.IsDead) yield return hero;
    }
}
```

**불타는 피** — "전투에 이기면 체력 6 회복, 최대 체력까지". 유물 `burningBlood`, 발동 = `이벤트 받기` → `전투 승리`, 결과는 코드로 쓴다.

```csharp
[BindTo(GameIds.Relic.BurningBlood)]
static void BurningBlood(BattleWon e, TriggerContext c)
{
    var hero = c.Game.Data().Run.hero;
    hero.hp = System.Math.Min(hero.maxHp, hero.hp + 6);
}
```

> [!note]- 왜 `주인공`은 Block인가
> - 유물은 `Game.Use`로 쓰는 것이 아니라 이벤트를 받아 발동하므로 사용자도, 고른 대상도 없다.
> - 사용자가 없는 다른 유물·도전과제도 이 Block을 쓸 수 있으니 `[BindTo]`가 아니라 Block이 맞다.
> - 대상 Block은 고르기만 하고 값을 바꾸지 않는다. 컴파일하면 **+ 대상 추가** 목록의 "대상" 분류에 `주인공`이 나온다.

> [!note]- 가시 갑옷 — 받는 피해 −5(패시브 · 가로채기, 코드 없음)
> 이벤트는 일이 일어난 **뒤**에 온다. "받는 피해 −5"는 피해가 들어가기 **직전**에 내용을 고쳐야 하므로 가로채기로 만든다. 가진 동안 늘 켜진 규칙이라 기본 종류 패시브로 만든다(8절 첫 접힌 칸). `RelicData`·등록 코드가 필요 없다.
> 1. DB › **패시브** → `thornArmor`. 가진 목록은 기본값 `run` › `relics` 그대로.
> 2. 발동(`trigger`): **+ 블럭** → `가로채기` → 의도 `피해`. 의도 조건(`filter`)에 `필드 검사` → `targetId` `일치` `hero`.
> 3. 결과(`outcomes`): **+ 결과 추가** → `의도 고치기` → 필드 `amount`, 방법 `더하기`, 값 `-5`.
> 4. Ctrl+S. `GameIds.Passive.ThornArmor`가 생긴다.
>
> - `Rules.Damage`가 피해를 주기 직전에 `DamageIntent`(`sourceId`·`targetId`·`amount`·`cancelled`)를 `Game.Intercept`로 통과시킨다. 이 패시브가 `amount`를 줄이고, 0 이하가 되면 피해가 들어가지 않는다. 방어도는 줄어든 뒤의 값으로 깎인다.
> - "주인공이 맞을 때만"을 가진 쪽 필드가 아니라 의도 조건으로 정하는 이유: 가진 쪽이 목록을 품은 `run`이라 id가 없다. 가진 쪽 필드는 가진 쪽에 id가 있을 때(동료마다의 특성 `party[].traits`) 쓴다.
> - 패시브의 발동·결과 칸은 가로채기·의도 고치기를 처음부터 허락한다. 직접 만든 `RelicData`로 할 때만 잠금에 `TriggerKinds.Intercept`·`OutcomeKinds.Intent`를 넣는다(8절).
> - 시험: 모니터 **데이터** 탭에서 `run › relics`에 `thornArmor`를 넣고 턴을 끝낸다. 적 공격이 5씩 줄어 들어온다. 빼면 그때부터 그대로 들어온다. Undo하면 함께 돌아간다. 가진 목록 칸의 `[DataRef]`는 8절 첫 접힌 칸처럼 패시브 ID도 받게 둔다.
> - "공격한 적에게 3 되돌려 주기"는 피해가 들어간 **뒤**의 일이라 이벤트 받기(`피해` 이벤트 `DamageDealt`, 이름이 같은 의도와는 목록이 따로다)로 만든다. 가로채는 쪽은 의도만 고친다.
> - 의도 클래스는 `Rules/Events/Intents.cs`에 있다(`[Intent("피해")]`). 순서·막기·`[BindTo]`로 쓰는 법은 매뉴얼 [28장](../Manual/28-Attributes-Intercepts.md) 5-4·5-5.

> [!note]- 투척 단검 — 적 하나를 골라 피해(선택 요구, 코드 없음)
> 유물에는 사용자도 고른 대상도 없다. "전투 첫 턴에 적 하나를 골라 피해 5"처럼 플레이어가 고르게 하려면 결과 `선택 요구`를 쓴다.
> 1. DB › 유물 → `throwingKnife`.
> 2. 발동(`trigger`): 닻과 같다(`이벤트 받기` → `턴 시작`, `turn` `일치` `1`).
> 3. 효과(`outcomes`): **+ 결과 추가** → `선택 요구` → `prompt` `피해를 줄 적`, `candidates` **+ 대상 추가** → `모든 적`, `min` `1`, `max` `1`, `effects` **+ 효과 추가** → `피해`(5).
>
> - 요약: `모든 적에서 1개 고르기: 피해 5`.
> - 턴 시작을 쏜 행동은 답을 기다리지 않고 끝난다. 플레이어가 고르면 답하기가 행동 하나로 실행되어 피해가 들어간다. Undo 한 번이면 피해만 돌아가고 다시 고르기를 기다린다.
> - 시험: 데이터 탭에서 `run › relics`에 `throwingKnife`를 넣고, 전투 중에 이벤트 탭에서 `턴 시작`(turn 1)을 호출한다. 모니터 **명령** 탭 맨 위 "선택 대기 · 피해를 줄 적" 패널에서 적 버튼을 누르고 **답하기**. 버튼에는 후보의 텍스트 `<id>.name`이, 없으면 id(`enemy1` …)가 보인다.
> - 게임 화면은 기다리는 선택(`Game.Choice`)을 그리고, 기다리는 동안(`Game.IsWaiting`) 카드 내기·턴 끝을 막는다. 시스템은 막지 않는다.
> - 화면 코드, 코드에서 고르게 하는 `Game.Ask`(카드 1장 버리기·보상 3개 중 1개)는 매뉴얼 [29장](../Manual/29-Statuses-Schedules-Choices.md) 5-7~5-10.

## 10. 등장시키기

항목을 만들어도 게임 어딘가에서 그 ID를 쓰지 않으면 나오지 않는다.

| 항목 | 들어가는 자리 |
|---|---|
| 카드 | 시작 덱(`StartRun`), 상점(`BuyCard.source`) |
| 몬스터 | 전투 목록(`StartBattle.monsters`) |
| 물약 | 가방(`RunState.potions`) |
| 유물 | 가진 유물(`RunState.relics`) |
| 로그·도전과제 | 없음(이벤트를 받으면 저절로) |

시험할 때는 개발 명령으로 넣으면 빠르다. DB › 개발 › **개발 명령**에 항목을 하나 더한다([01장](01-StartProject.md) 4절).

- 값을 넣기만 하면 되는 것은 결과 Block으로 끝난다. 예: 유물 닻 → 결과 `플레이어 데이터에 주기` → `run › relics`, 넣을 요소 `anchor`.
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

> [!note]- 보상과 수치도 DB에 — 가중치 표 · 밸런스 값
> **가중치 표**(DB › 가중치 표): "무엇이 얼마나 자주 나오나"를 표로 둔다. 예: 전투 보상 유물 `relicDrop` — 항목 줄 `anchor` 무게 3, `thornArmor` 무게 1, 꽝 무게 4(절반은 아무것도 없음).
> - 코드 없이 주기: 결과 칸(**+ 결과 추가**)의 **가중치 표에서 주기** → 가중치 표 `relicDrop` · 횟수 `1` · 넣을 플레이어 데이터 `run` · 필드 `relics`. `relics`는 글자 목록이라 뽑힌 ID가 들어간다(꽝이면 아무것도 넣지 않는다).
> - 코드 없이 고르게 하기: **가중치 표에서 고르기** → 표 · 후보 수 `3` · 고른 것을 넣을 플레이어 데이터 `run` · 필드 `relics`. 셋을 보이고 고른 것만 넣는다(유물 셋 중 하나).
> - 코드에서(행동 안): `var relic = Game.Roll(GameIds.WeightTable.RelicDrop);`(하나, 꽝이면 `null`), `Game.Roll(id, 3, unique: true)`(서로 다른 셋 — 카드 보상 3장을 뽑아 `Game.Ask`로 1장 고르게 할 때. 덱은 `DeckCard` 목록이라 원본 ID를 바로 넣지 못해 코드로 한다).
> - 풀이 바뀌는 것: 가진 유물은 다시 안 나오게 하려면 표의 **뺄 목록**에 `run › relics`, 해금한 카드만 나오게 하려면 그 줄의 조건(도전과제 달성 등), "손패에서 무작위 1장"처럼 판에서 만들어진 목록에서 뽑으려면 줄 종류 **플레이어 데이터 목록**. 풀은 뽑을 때마다 다시 만들어진다(매뉴얼 30장 5-10).
> - 항목 줄마다 최소·최대 개수와 조건(맞을 때만 후보)을 둘 수 있고, 항목이 다른 가중치 표면 그 표에서 다시 뽑는다. `session.Tables.Chances(id)`는 지금 상태의 확률 목록이다(확률 공개 화면).
> - 표마다 따로인 난수 흐름이라 Undo하면 같은 결과가 나오고, 표를 더해도 다른 표의 결과는 그대로다.
>
> **밸런스 값**(DB › 밸런스 값): 기획자가 바꿀 숫자에 이름을 붙인다. 예: `winGold` 값 15, 곡선 "일정하게" +3/단계.
> - 코드: `Game.Balance(GameIds.Balance.WinGold)` → 15, `Game.Balance(GameIds.Balance.WinGold, run.floor)` → 그 층의 값(3층이면 15 + 3 × 2 = 21). 곡선은 일정하게·비율로·거듭제곱·표(점 사이 직선)와 반올림, 최솟값·최댓값 자르기(쪽마다 켜고 끈다)를 고른다.
> - 없는 ID면 `InvalidOperationException`이다(코드에는 늘 `GameIds` 상수).
>
> 둘 다 플레이 중에 DB에서 고치면 저장 전이라도 0.5초 안에 실행 중인 게임에 반영된다([08장](08-TestAndDebug.md) 2절). 수치가 많아지면 DB 목록 위의 **CSV** 버튼으로 표 계산기(구글 시트·엑셀)와 주고받는다. 매뉴얼 [30장](../Manual/30-Balance-Tables.md)·[33장](../Manual/33-Tick-Preview-Csv.md).

## 11. 모니터로 시험하고 검증하기

1. 플레이 중 `MomoiProject → 모니터`에서 항목이 반응하는지 본다(아래 표).
2. 머리의 **Undo**로 돌려 본다.
3. DB로 돌아가 사이드바 아래가 "검증 통과"인지 본다. 오류가 있으면 빌드가 막힌다. 탭 옆의 "오류 n"을 따라가 고친다.

> [!note]- 항목마다 확인할 것
> | 항목 | 모니터에서 | 확인할 것 |
> |---|---|---|
> | 카드·물약 | 게임 화면에서 내기·마시기 → **행동** 탭 | "카드 내기" 아래 "능력 사용: fireball (hero)"가 안쪽 행동으로 보이고, **타임라인** 묶어 보기에서 그 아래에 바뀐 값·이벤트가 이어진다. **Undo** 한 번에 에너지·피해·물약 개수가 함께 돌아간다. |
> | 로그 | **이벤트** 탭에서 `턴 끝`을 고르고 `damage`를 채워 **이벤트 호출**(자주 쓰면 개발 명령으로: 카드 던전 **명령** 탭의 "한 턴 30 피해" = 결과 `이벤트 쏘기`) | 아래 "반응하는 로그"에 `maxTurnDamage`가 보이고, **로그** 탭의 값이 바뀐다. |
> | 도전과제 | **도전과제** 탭에서 카드를 누르고 **트리거로 달성** | 부족한 로그를 이벤트로 채워 게임과 같은 길로 달성된다. Undo하면 로그는 돌아가고 달성은 남는다. 연쇄 반응 없이 결과만 보려면 **강제 달성**. |
> | 유물 닻 | **데이터** 탭에서 `run › relics`에 **+ 추가**로 `anchor`를 넣고, 이벤트 탭에서 `턴 시작`(turn 1)을 호출 | `run › hero › block`이 오른다. turn 2로 호출하면 그대로다. |
> | 유물 불타는 피 | 데이터 탭에서 `run › hero › hp`를 최대보다 낮추고, 이벤트 탭에서 `전투 승리` 호출 | 체력이 6 오른다(최대 체력까지). 시작 유물이라 `relics`에 이미 들어 있다. |
>
> 이벤트 호출로 쏜 이벤트는 행동 하나("[테스트] … — 모니터 이벤트 호출")로 기록되어 Undo된다. 이벤트를 모니터에서 쏘려면 이벤트 클래스에 매개변수 없는 생성자가 있어야 한다.

> [!note]- 자주 보는 검증 오류
> - "이벤트 받기: 이벤트가 선택되지 않았습니다." — 트리거의 이벤트를 고르지 않았다.
> - "로그에 기록의 반영할 값 … 을 읽을 이벤트가 트리거에 없습니다" — 반영할 값을 고른 뒤 트리거의 이벤트를 바꿨다.
> - "없는 … 를 가리킵니다" — `[DataRef]` 칸의 항목이 지워졌거나 ID가 틀렸다.
> - "필수 값 … 이 비어 있습니다" — 필수로 정한 칸(그림 등)이 비었다([05장](05-WorkingWithDesigners.md) 6절).
> - "발동(트리거)이 없습니다." — 유물의 발동이 비었다(`RelicData.ValidateData`가 낸다).
> - 경고 "발동(트리거)도 주기도 없어 결과가 돌지 않습니다." — 패시브(DB › 패시브)에 결과만 넣었다. 발동이나 주기를 정하거나, 보정만 쓰려면 결과를 비운다.
> - 경고 "대상에게 효과: 효과가 없습니다." — 결과는 넣었지만 효과 Block을 고르지 않았다.

## 12. 체크리스트

- [ ] 새 항목마다 클래스 → 항목 → 이벤트 → Trigger → 결과 → 등장 → 저장 → 시험·검증을 밟았다.
- [ ] 수치만 다른 항목에 코드를 쓰지 않았다.
- [ ] 쓰는 순간 효과가 나는 인스턴스에 사용 기능이 붙어 있다(`[BindTo]`로 결과를 쓰는 것도).
- [ ] 새 Block은 동작 하나만 하고 `[Serializable]`·`[BlockInfo]`가 있다. 대상 Block은 고르기만 한다.
- [ ] `[BindTo]` 인자는 `GameIds` 상수다.
- [ ] 판 데이터에는 원본 ID와 바뀌는 값만 들어간다.
- [ ] 도전과제의 결과는 프로필 쪽 값에 준다.
- [ ] 프로젝트 기능은 `BoundOutcomes`를 확인하고 `RunOutcomes`로 결과를 실행한다.
- [ ] 항목의 ID가 게임에 나오는 자리에 들어가 있다.
- [ ] 모니터에서 반응과 Undo를 확인했고, DB 사이드바가 "검증 통과"다.

> [!note]- 더 보기(매뉴얼)
> - [04. 인스턴스](../Manual/04-Instances.md) — 만들기, 기능 필드, 사본
> - [16. 능력과 사용 기능](../Manual/16-Abilities-Usable.md) — `Usable`, `Effect<T>`·`TargetSelector<T>`, `Game.Use`의 흐름
> - [11. Trigger와 조건](../Manual/11-Triggers-Conditions.md) — 이벤트 받기·값 조건, 코드로 쓰는 조건 Block
> - [12. 결과(Outcome)](../Manual/12-Outcomes.md) — 결과 종류, 대상에게 효과, `Reward`
> - [13. `[BindTo]`](../Manual/13-BindTo.md) — 함수 모양, 연결 실패 알림
> - [14. 도전과제](../Manual/14-Achievements.md) · [15. 로그](../Manual/15-Stats-Logs.md)
> - [17. 프로젝트 고유 기능](../Manual/17-Custom-Features.md) — 패시브(놓인 것마다·가진 동안 능력치 보정·빌려 쓰기), DB 종류 + 감시, 퀘스트 전체 예제
> - [36. 격자와 BigNumber](../Manual/36-Grid-BigNumber.md) — 자리(`Placement`), 대상 '자리로 고르기'·조건 '자리 검사'
> - [29. 상태 효과·예약·선택 대기](../Manual/29-Statuses-Schedules-Choices.md) — 상태 효과의 칸, 선택 요구, `Game.Ask`, 예약
> - [30. 밸런스 값·곡선·가중치 표·플레이어용 실패](../Manual/30-Balance-Tables.md) — 곡선, `Game.Roll`, 결과 '가중치 표에서 주기'
> - [22. 모니터와 디버깅](../Manual/22-Monitor-Debug.md) — 이벤트 호출, 트리거로 달성
