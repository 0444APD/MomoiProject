# 04. 콘텐츠 더하기 — 카드·몬스터·물약·유물·도전과제·로그

게임 루프가 돌기 시작하면 이후의 일은 대부분 "새 항목 더하기"다. 카드 한 장, 몬스터 한 마리, 유물 하나를 더할 때마다 같은 절차를 밟으면 프로그래머가 코드를 쓰는 일은 새 동작이 필요할 때로 줄어든다. 이 장은 그 표준 절차와, 효과를 Block으로 만들지 `[BindTo]`로 쓸지 고르는 기준, 그리고 기본 기능에 없는 새 종류(유물)를 프로젝트가 직접 만드는 법을 다룬다.

이 장에서 쓰는 말:

| 말 | 뜻 |
|---|---|
| 인스턴스 | 클래스의 원본 값 하나(타격 카드, 드래곤 …)를 도감 › 인스턴스에 채워 둔 항목. 게임 중에는 읽기만 하고, 판 데이터에는 원본 ID와 바뀌는 값만 둔다. |
| 사용 기능(`Usable`) | 클래스에 붙이는 기능 필드. 붙어 있는 인스턴스는 게임이 켜질 때 인스턴스 ID로 등록되어 `Game.Use(인스턴스 ID, 사용자, 대상들)`로 쓴다. |
| Trigger | 도감 항목의 "언제" 칸. 잎(이벤트 받기 · 조건 충족 · 사용)을 AND·OR·NOT으로 조립한다. |
| Outcome(결과) | 도감 항목의 "무엇을" 칸. 로그에 기록 · 플레이어 데이터에 주기 · 대상에게 효과 · 코딩한 결과. |
| Block | 기획자가 도감에서 골라 조립하는 로직 단위. 대상 Block(누구에게, `TargetSelector<T>`)·효과 Block(무엇을, `Effect<T>`)·조건 Block(`SimpleCondition`)·코딩한 결과(`Reward`). 프로그래머가 동작 하나를 Block 하나로 만든다. |
| `[BindTo]` | static 함수에 붙여 그 ID를 가진 도감 항목 하나의 결과를 코드로 바꾸는 어트리뷰트. 그 항목의 결과 칸은 잠긴다. |
| 컨텍스트(`TriggerContext`) | Trigger가 발동한 순간의 정보: 이벤트(`Event`), 사용자(`User`), 고른 대상(`Picked`), 항목 ID(`Owner`), `Game`. 결과·`[BindTo]` 함수가 읽는다. |
| 도감 종류 | `GameData`를 상속한 클래스. 상속하면 도감에 탭이 생긴다. |

---

## 1. 이 단계의 목표

새 콘텐츠를 "클래스 → 인스턴스 → 이벤트 → Trigger → 결과 → 모니터로 시험 → 검증"의 같은 절차로 더하고, 코드는 새 동작(Block)이나 그 항목만의 예외(`[BindTo]`)가 필요할 때만 쓴다.

## 2. 할 일 순서 — 표준 절차

1. **클래스**: 필요한 칸이 클래스에 있는지 본다. 없으면 필드를 더한다(이미 있는 인스턴스는 필드 초기값을 받는다).
2. **인스턴스(또는 항목)**: 도감에서 항목을 만들고 값을 채운다. 쓰는 순간 효과가 나면 **+ 기능**으로 사용 기능을 붙인다.
3. **이벤트**: 반응할 순간의 이벤트가 이미 쏘아지는지, 조건·로그가 읽을 값이 이벤트 필드에 있는지 본다. 없으면 이벤트나 필드를 더하고 행동 안에서 쏜다.
4. **Trigger**: 언제 발동하는지 조립한다(사용 기능은 기본 `사용`).
5. **결과**: 이미 있는 Block 조합으로 되면 조립한다. 새 동작이 필요하면 Block을 하나 만든다. 그 항목에만 있는 흐름이면 `[BindTo]` 함수를 쓴다.
6. **등장시키기**: 덱·상점·전투 목록처럼 그 항목이 게임에 나오는 자리에 ID를 넣는다.
7. **저장**: Ctrl+S. `GameIds`에 새 상수가 생긴다(`[BindTo]` 인자는 이 상수를 쓴다).
8. **모니터로 시험**: 이벤트 호출·트리거로 달성·명령 탭으로 반응을 보고, 행동 탭에서 Undo해 본다.
9. **검증**: 도감 사이드바 아래가 "검증 통과"인지 본다. 오류가 있으면 빌드가 막힌다.

## 3. 판단 기준

### 3-1. 코드가 필요한가

| 새 항목이 | 할 일 | 코드 |
|---|---|---|
| 이미 있는 클래스·Block으로 수치만 다르다 | 도감에서 항목 만들기 | 없음 |
| 이미 있는 동작을 새로 조합한다(피해 + 화상) | 결과 칸에 Block 여러 개 | 없음 |
| 새 동작이 필요하고, 다른 항목도 쓸 만하다 | Block 클래스 하나 | Block 하나 |
| 그 항목에만 있는 흐름이다 | `[BindTo]` 함수 하나 | 함수 하나 |
| 반응할 순간이 아직 없다 | 이벤트(또는 필드)를 더하고 행동에서 쏜다 | 이벤트 + 쏘는 한 줄 |
| 기존 기능(도전과제=한 번, 로그=숫자, 사용 기능=쓰는 순간)과 규칙이 다른 새 종류다 | 도감 종류 + 런타임 감시 | 4-10 |

### 3-2. Block으로 만들까, `[BindTo]`로 쓸까

**Block**이 맞는 때:

- 동작이 짧고 이름 하나로 설명된다("피해", "방어도", "모든 적").
- 다른 카드·물약·유물도 쓸 수 있다.
- 수치를 기획자가 정하고 바꾼다. Block의 public 필드가 도감 칸이 된다.
- 조합해서 새 항목이 생긴다. 기획자가 프로그래머 없이 "피해 + 화상" 카드를 만든다.

**`[BindTo]`**가 맞는 때:

- 그 항목 하나에만 있는 흐름이다(대상마다 수치가 바뀜, 조건 분기, 여러 값을 함께 계산).
- Block으로 쪼개면 그 항목 하나 때문에 Block이 여러 개 생기거나, 블록 조합이 스크립트처럼 길어진다.
- 수치까지 코드에 있어도 된다. 결과 칸이 잠기므로 기획자는 Trigger만 고친다.

같은 `[BindTo]` 코드가 두 번째 항목에서도 필요해지면 그때 Block으로 올린다. Block을 미리 많이 만들지 않는다. 블록 조합으로 스크립트 언어를 만들지 않는 것이 원칙이다.

`[BindTo]` 함수의 모양: `static`, 반환 `void`, 매개변수는 `()` · `(이벤트)` · `(TriggerContext)` · `(이벤트, TriggerContext)` 중 하나. 받을 이벤트는 첫 매개변수의 형식으로 정해진다. 사용 기능·능력에는 이벤트가 없으므로 `(TriggerContext c)`만 쓴다.

### 3-3. 결과가 Undo되는가

| 어디서 발동했나 | Undo |
|---|---|
| 사용 기능(`Game.Use`), 로그, 행동 안에서 쏜 이벤트로 발동한 유물 | 그 행동과 함께 돌아간다 |
| 도전과제 | 돌아가지 않는다(달성·결과는 Undo 이력 밖) |

그래서 도전과제의 결과는 행동이 건드리지 않는 값(프로필 쪽)에 준다. 판 데이터에 주면, 달성시킨 행동을 Undo할 때 그 값이 행동 전으로 덮일 수 있다.

## 4. 카드 던전에서 실제로

### 4-1. 몬스터 — 드래곤 (코드 없음)

몬스터는 수치만 다르므로 도감 일이다.

1. 도감 **인스턴스** 탭 → **+ 새로 만들기** → ID `dragon`.
2. `instanceType`(형식)에서 `MonsterData`를 고른다. `value`(값)에 새 객체가 생긴다.
3. 값을 채운다: `title`, `maxHp`, `attackPattern`(턴마다 차례로 주는 피해 리스트), `element`(속성 키워드), `portrait`.
4. Ctrl+S. `GameIds.Instance.Dragon`이 생긴다.

전투에 나오게 하는 것은 전투를 여는 행동의 몬스터 목록이다(`StartBattle.monsters`에 `GameIds.Instance.Dragon`). 전투 시작 때 `Rules.NewEnemy`가 원본에서 이름·최대 체력을 읽어 `Enemy`를 만든다. 판 데이터에 들어가는 것은 원본 ID와 바뀌는 값(체력·공격 차례)뿐이고, 적은 턴마다 원본의 `attackPattern`을 읽어 차례로 쓴다. 그래서 기획자가 드래곤의 공격 순서를 고치면 진행 중인 판의 드래곤에도 바로 반영된다.

### 4-2. 카드 — 타격·수비·화염구 (Block 조합, 코드 없음)

카드는 `Card` 인스턴스에 사용 기능을 붙이고 결과를 Block으로 조립한다.

1. 인스턴스 `fireball`, 형식 `Card`. `title`, `cost`, `rarity`, `element`(`fire`), `tags`(`attack`), `art`를 채운다.
2. 값 탐색기 아래 **+ 기능** → `usable`. 사용 기능의 `when`(트리거)은 기본 `사용`이다.
3. `then`(결과)의 **+ 결과 추가** → `대상에게 효과`.
4. `targets`(대상)는 비워 둔다. 비어 있으면 플레이어가 고른 대상, 곧 카드를 끌어 놓은 적이 그대로 쓰인다.
5. `effects`(효과)의 **+ 효과 추가** → `피해`(amount를 정한다) → 한 번 더 `상태 걸기`(status `burn`).

타격은 같은 방법으로 `피해` 하나, 수비는 대상에 `사용자 자신`, 효과에 `방어도`를 둔다. 카드를 낼 때 `PlayCard`가 카드 원본 ID로 `Game.Use`를 부르고 주인공을 사용자로 넘기므로 `사용자 자신`이 주인공이 된다.

```csharp
if (string.IsNullOrEmpty(targetId)) Game.Use(card.card, run.hero);
else Game.Use(card.card, run.hero, targetId);
```

`card.card`는 덱 카드가 든 원본 인스턴스 ID다. 사용 기능은 인스턴스 ID로 등록되므로, 덱에 든 장이 사용 기능을 따로 들고 있을 필요가 없다. 효과를 고치면 모든 장에 바로 반영된다.

효과 Block은 동작 하나만 한다. 피해는 규칙 함수에 맡기므로 방어도·처치 이벤트까지 카드와 적 공격이 같은 규칙을 탄다.

```csharp
[Serializable, BlockInfo("피해", Category = "카드", Description = "대상에게 피해를 줍니다(방어도 먼저).")]
public class DealDamage : Effect<Fighter>
{
    public int amount = 6;
    protected override void Apply(Fighter target) => Rules.Damage(Game, User<Fighter>(), target, amount);
    public override string Describe() => $"피해 {amount}";
}
```

`[BlockInfo]`의 이름·분류·설명이 기획자가 보는 목록이 되고, `Describe`가 상세 위쪽 요약에 수치까지 보이게 한다. 기획자 화면을 다듬는 일은 [05장](05-WorkingWithDesigners.md).

기획자가 넣으면 안 되는 값이 있으면 Block에 검사를 둔다. 효과·대상 Block은 `Validate`를 덮어쓸 수 있고, 도감이 '대상에게 효과'를 검증할 때 안의 Block마다 이것을 부른다. 예를 들어 피해 Block에 이렇게 더하면 0 이하의 피해가 도감 검증 오류가 된다.

```csharp
public override void Validate(IValidationContext context)
{
    if (amount <= 0) context.Error("피해는 1 이상이어야 합니다.");
}
```

`context.Error`는 빌드를 막고, `context.Warning`은 표시만 한다. 실행 중에 걸러 내는 것보다 도감에서 저장할 때 빨간 글씨로 보이는 편이 기획자에게 빠르다.

### 4-3. 카드 — 연쇄 번개 (`[BindTo]`)

"적마다 차례로 3, 4, 5 … 피해"는 대상마다 수치가 바뀌는 그 카드만의 흐름이다. Block을 만들지 않고 결과를 함수 하나로 쓴다.

1. 인스턴스 `chainLightning`(형식 `Card`)을 만들고 **+ 기능** → `usable`을 붙인다. 결과는 비워 둔다. 사용 기능이 붙어 있어야 `Game.Use`로 쓸 수 있는 항목으로 등록된다.
2. Ctrl+S로 `GameIds.Instance.ChainLightning`을 만든다.
3. 함수를 쓴다.

```csharp
[BindTo(GameIds.Instance.ChainLightning)]
static void ChainLightning(TriggerContext c)
{
    var hero = c.User as Fighter;
    var damage = 3;
    foreach (var enemy in Rules.LivingEnemies(c.Game).ToList()) Rules.Damage(c.Game, hero, enemy, damage++);
}
```

컴파일하면 도감에서 이 인스턴스의 결과 칸 자리에 "코드로 쓴 결과가 이 항목에 연결되어 있습니다([BindTo])." 안내와 `CardCode.ChainLightning`, **cs 열기**가 보인다. 사용이라 이벤트가 없으므로 매개변수는 컨텍스트 하나다. ID는 글자 대신 `GameIds` 상수로 쓴다. 오타나 ID 이름 바꾸기가 컴파일 오류로 먼저 드러난다.

### 4-4. 물약 — 회복 물약 (코드 없음)

1. 인스턴스 `healPotion`, 형식 `Potion`. `title`, `maxStack`을 채운다.
2. **+ 기능** → `usable` → 결과 `대상에게 효과` → 대상 `사용자 자신` → 효과 `회복`(amount).

물약은 가방(인벤토리)에 원본 ID와 개수로만 들어간다. 한 칸에 몇 개까지 겹칠지도 고정 값이라 가방에 넣을 때 원본의 `maxStack`을 읽는다(`Rules.PotionOf`). 마시는 행동이 개수를 줄인 뒤 원본의 사용 기능을 쓴다. 개수 줄이기와 효과가 한 행동이라 Undo 한 번에 함께 돌아가고, 물약이 없으면 예외로 아무것도 바뀌지 않는다.

```csharp
var run = Game.Data().Run;
if (!run.potions.TryRemove(potion)) throw new InvalidOperationException("물약이 없습니다.");
if (string.IsNullOrEmpty(targetId)) Game.Use(potion, run.hero);
else Game.Use(potion, run.hero, targetId);
```

### 4-5. 로그 — 처치 수 · 한 턴 최대 피해 · 얻은 골드

로그는 이벤트를 받을 때마다 숫자를 쌓는다. 게임 코드는 이벤트만 쏘고 있으면 되고, 로그 자체는 도감 일이다. 도감 **로그** 탭 → **+ 새로 만들기** → 칸 `scope`(범위)·`aggregate`(집계 방식)·`triggers`(반영 규칙).

| ID | 범위 · 집계 | 반영 규칙(이벤트 받기) | 반영할 값 |
|---|---|---|---|
| `kills` | 누적 · 합계 | 적 처치 | 비움(횟수 1) |
| `maxTurnDamage` | 누적 · 최고치 | 턴 끝 | `damage` |
| `goldEarned` | 누적 · 합계 | 골드 획득 | `amount` |

반영할 값은 이벤트의 숫자 필드 목록에서 고른다. 한 턴 최대 피해를 셀 수 있는 것은 02장에서 `TurnEnded`에 `damage` 필드를 두었기 때문이다. 이벤트에 필요한 값이 없으면 로그가 아니라 이벤트를 고친다.

### 4-6. 도전과제 — 한 턴 30 피해 (코드 없음) · 첫 승리 (`[BindTo]`)

**`bigTurn`**: 도감 **도전과제** 탭에서 만들고 `trigger` 칸의 **+ 블럭** → `비교` → `maxTurnDamage` `>=` `30`. 결과는 기획이 정한다(예: 플레이어 데이터에 주기 · `profile`의 필드). 조건만으로 된 Trigger라 모니터 카드에 진행도(`18 / 30` 같은)가 보이고, 턴 끝 행동이 끝날 때 다시 평가되어 달성된다.

**`firstWin`**: `trigger` = `이벤트 받기` → `전투 승리`. 결과(연쇄 번개 해금)는 코드로 쓴다. 이벤트로 발동하므로 이벤트와 컨텍스트를 둘 다 받는다.

```csharp
[BindTo(GameIds.Achievement.FirstWin)]
static void FirstWin(BattleWon e, TriggerContext c)
{
    var profile = c.Game.Data().Profile;
    if (!profile.unlocked.Contains(GameIds.Instance.ChainLightning)) profile.unlocked.Add(GameIds.Instance.ChainLightning);
}
```

해금은 프로필(메타 구역)에 준다. 도전과제의 결과는 Undo 이력 밖이고, 판이 끝나도 남아야 하기 때문이다(3-3).

### 4-7. 유물 — 닻 (새 대상 Block 하나) · 불타는 피 (`[BindTo]`)

유물은 4-10에서 만드는 프로젝트 고유 기능이다. 도감 사이드바 **콘텐츠** 그룹에 **유물** 탭이 생기고, 칸은 `description`·`icon`·`trigger`(발동)·`outcomes`(효과)다.

**닻** — "전투 첫 턴에 방어도".

1. 유물 `anchor`를 만든다.
2. `trigger`: **+ 블럭** → `이벤트 받기` → `턴 시작`, 이벤트 조건(`filter`)에 **+ 블럭** → `필드 검사` → `turn` `일치` `1`.
3. `outcomes`: `대상에게 효과` → 효과 `방어도`(amount).

대상에서 막힌다. 유물은 `Game.Use`로 쓰는 것이 아니라 이벤트를 받아 발동하므로 사용자도, 고른 대상도 없다. `사용자 자신`을 넣으면 아무도 고르지 않는다. 그래서 "주인공"을 고르는 대상 Block을 하나 만든다. 사용자가 없는 다른 유물·도전과제도 이 Block을 쓸 수 있으니 Block이 맞다.

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

대상 Block은 고르기만 하고 값을 바꾸지 않는다. 컴파일하면 `+ 대상 추가` 목록의 "대상" 분류에 `주인공`이 나온다. 닻의 대상에 `주인공`을 넣으면 끝이다.

**불타는 피** — "전투에 이기면 체력 6 회복, 최대 체력까지". 유물 `burningBlood`의 `trigger` = `이벤트 받기` → `전투 승리`, 결과는 코드로 쓴다.

```csharp
[BindTo(GameIds.Relic.BurningBlood)]
static void BurningBlood(BattleWon e, TriggerContext c)
{
    var hero = c.Game.Data().Run.hero;
    hero.hp = System.Math.Min(hero.maxHp, hero.hp + 6);
}
```
### 4-8. 등장시키기

항목을 만들어도 게임 어딘가에서 그 ID를 쓰지 않으면 나오지 않는다. 카드 던전에서 ID가 들어가는 자리:

| 항목 | 들어가는 자리 |
|---|---|
| 카드 | 시작 덱(`StartRun`), 상점(`BuyCard.source`) |
| 몬스터 | 전투 목록(`StartBattle.monsters`) |
| 물약 | 가방(`RunState.potions`) |
| 유물 | 가진 유물(`RunState.relics`) |
| 로그·도전과제 | 없음(이벤트를 받으면 저절로) |

시험할 때는 디버그 명령으로 넣으면 빠르다. Bootstrap의 `Started`에 한 줄을 더한다: `session.Debug.Register("카드 던전", "연쇄 번개 받기", () => session.Do(new BuyCard { source = GameIds.Instance.ChainLightning, price = 0 }));`

### 4-9. 모니터로 시험하고 검증하기

`MomoiProject → 모니터`(플레이 중)에서 항목마다 이렇게 본다.

| 항목 | 모니터에서 | 확인할 것 |
|---|---|---|
| 카드·물약 | 게임 화면에서 내기·마시기 → **행동** 탭 | "카드 내기" 아래 "능력 사용: fireball (hero)"가 안쪽 행동으로 보이고, **타임라인** 묶어 보기에서 그 아래에 바뀐 값·이벤트가 이어진다. 머리의 **Undo** 한 번에 에너지·피해·물약 개수가 함께 돌아간다. |
| 로그 | **이벤트** 탭에서 `턴 끝`을 고르고 `damage`를 채워 **이벤트 호출** | 아래 "반응하는 로그"에 `maxTurnDamage`가 보이고, **로그** 탭의 값이 바뀐다. |
| 도전과제 | **도전과제** 탭에서 카드를 누르고 **트리거로 달성** | 부족한 로그를 이벤트로 채워 게임과 같은 길로 달성된다. Undo하면 로그는 돌아가고 달성은 남는다. 연쇄 반응 없이 결과만 보려면 **강제 달성**. |
| 유물 닻 | **데이터** 탭에서 `run › relics`에 **+ 추가**로 `anchor`를 넣고, 이벤트 탭에서 `턴 시작`(turn 1)을 호출 | `run › hero › block`이 오른다. turn 2로 호출하면 그대로다. |
| 유물 불타는 피 | 데이터 탭에서 `run › hero › hp`를 최대보다 낮추고, 이벤트 탭에서 `전투 승리` 호출 | 체력이 6 오른다(최대 체력까지). 시작 유물이라 `relics`에 이미 들어 있다. |

이벤트 호출로 쏜 이벤트는 행동 하나("[테스트] … — 모니터 이벤트 호출")로 기록되어 Undo된다. 이벤트를 모니터에서 쏘려면 이벤트 클래스에 매개변수 없는 생성자가 있어야 한다.

다 됐으면 도감으로 돌아가 사이드바 아래를 본다. "검증 통과"가 아니면 탭 옆의 "오류 m"을 따라가 고친다. 자주 보는 것: 사용 기능의 결과가 비었음, 이벤트 받기의 이벤트를 고르지 않음, 반영할 값을 읽을 이벤트가 없음, 유물의 발동이 없음("발동(트리거)이 없습니다." — 4-10의 `ValidateData`가 낸다).

### 4-10. 프로젝트 고유 기능 만들기 — 유물

유물은 "가지고 있는 동안, 어떤 순간마다, 무언가 한다"는 규칙이라 기본 기능 어디에도 맞지 않는다(02장 4-7). 이럴 때 도감 종류를 직접 만든다. 할 일은 둘이다: **데이터**(도감 탭과 편집 화면)와 **런타임**(감시하고 결과를 실행).

**데이터.** `GameData`를 상속하고 `[Database]`로 탭 이름·그룹·순서·새 ID 접두어를 정한다. Trigger 칸과 결과 칸에 `[TriggerSlot]`·`[OutcomeSlot]`을 붙이면 도전과제와 같은 한 줄 식 편집기·결과 목록 편집기·검증이 붙는다.

```csharp
[Database("유물", Group = "콘텐츠", Order = 40, IdPrefix = "relic")]
public class RelicData : GameData
{
    [FieldLabel("설명")] public string description = "";
    [FieldLabel("아이콘")] public Sprite icon;
    [FieldLabel("발동"), SerializeReference, TriggerSlot(TriggerKinds.Event | TriggerKinds.Condition)]
    public Trigger trigger;
    [FieldLabel("효과"), SerializeReference, OutcomeSlot(OutcomeKinds.PlayerData | OutcomeKinds.Coded | OutcomeKinds.Targeted)]
    public List<Outcome> outcomes = new List<Outcome>();
}
```

- `Trigger`·`Outcome`은 추상 형식이라 `[SerializeReference]`가 꼭 있어야 한다.
- 잠금은 기능에 맞게 고른다. 감시해서 발동하는 기능에 `사용`(`TriggerKinds.Use`)은 의미가 없다. 결과에서 `로그에 기록`(`Record`)을 빼 둔 것은, 유물 ID는 로그가 아니어서 로그 칸을 비운 기록이 아무것도 하지 않기 때문이다.
- 표시 이름 필드는 두지 않는다. ID가 이름이고, 플레이어에게 보일 이름은 텍스트로 붙인다([06장](06-Presentation.md)).
- `ValidateData`를 덮어써 "발동이 없다", "비어 있는 효과 칸이 있다"를 오류로 낸다. 오류는 빌드를 막는다.

**런타임.** 게임이 켜질 때(`Started`) 모든 유물의 Trigger를 감시에 등록하고, 발동하면 그 유물을 가지고 있을 때만 결과를 실행한다.

```csharp
foreach (var relic in database.All<RelicData>())
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
```

한 줄씩 짚으면:

- **`BoundOutcomes.Has(id)`** — `[BindTo]` 연결은 기본 기능만 스스로 확인한다. 프로젝트 기능은 이 한 줄로 직접 확인해 바꿔 쓴다. 빠뜨리면 도감에는 결과 칸이 잠겨 보이는데 실제로는 숨은 예전 결과가 돈다.
- **`Register(owner, when, repeat, fire)`** — `owner`는 항목 ID(컨텍스트의 `Owner`가 된다). 유물은 여러 번 발동하므로 `repeat: true`. 한 번만 발동하는 기능(퀘스트)이면 `false`로 두고 완료를 플레이어 데이터에 저장한다.
- **가졌는지 확인** — 모든 유물을 감시하되 발동 순간에 `run.relics`를 본다. 유물을 얻고 잃을 때마다 등록을 바꾸지 않아도 되고, 가진 유물 목록이 플레이어 데이터라 Undo·저장이 따라온다.
- **`RunOutcomes`** — 결과는 직접 `Run`하지 않고 이것으로 실행한다. 그래야 컨텍스트가 결과·Block에 보이고 Journal에 "결과: …"(출처 `Relics`)가 남는다.
- **`Permanent()`를 쓰지 않는다** — 유물 효과는 판의 일부라, 이벤트를 쏜 행동과 함께 Undo되는 것이 맞다. 도전과제처럼 "되돌리지 않을 일"이면 `using (session.Data.Permanent())`로 감싼다.

이벤트 받기로 발동한 유물은 이벤트를 쏜 행동 안에서 실행되므로 그 행동의 일부다. 조건 충족만으로 된 발동은 값이 바뀐 행동이 끝난 뒤의 평가에서 실행되어 행동 밖 변경이 된다. 판 값을 바꾸는 유물은 이벤트 받기로 만든다.

## 5. 다른 장르라면

> **다른 장르라면** — RPG의 장비·소모품은 아이템 클래스 하나에 기능 필드(`[SerializeReference] public Equip equip;`, `public Usable usable;`)를 두고 인스턴스로 더한다. 칸·개수는 인벤토리 시스템([25. 인벤토리](../Manual/25-Inventory.md)). 여러 아이템이 같은 효과를 나누면 아이템이 능력 ID를 가리키게 하고(`[DataRef(typeof(AbilityData))]`) 효과는 도감 › 능력에서 한 번 조립한다.

> **다른 장르라면** — 퀘스트·일일 미션·튜토리얼 단계처럼 "언제 → 보상"을 기획자가 계속 더하는 것은 유물과 같은 방식의 도감 종류다. 한 번 완료하는 기능은 `repeat: false`, 완료 기록은 메타 구역 플레이어 데이터, 완료와 보상은 `Permanent()` 안에서. 시작할 때 이미 조건을 넘은 항목은 등록 뒤 `session.Triggers.Watcher.EvaluateAll()`로 소급한다. 전체 예제는 [17. 프로젝트 고유 기능](../Manual/17-Custom-Features.md).

> **다른 장르라면** — 스킬처럼 아이템이 아닌 "쓰는 것"은 도감 › 능력에서 만든다. 게임 코드는 똑같이 `Game.Use(능력 ID, 사용자, 대상들)`이다.

## 6. 체크리스트

- [ ] 새 항목마다 클래스 → 인스턴스(항목) → 이벤트 → Trigger → 결과 → 등장 → 저장 → 시험 → 검증을 밟았다.
- [ ] 쓰는 순간 효과가 나는 인스턴스에 사용 기능이 붙어 있다(`[BindTo]`로 결과를 쓰는 인스턴스도).
- [ ] 수치만 다른 항목에 코드를 쓰지 않았다.
- [ ] 새 Block은 동작 하나만 하고, `[Serializable]`·`[BlockInfo]`가 있다. 대상 Block은 고르기만 한다. 넣으면 안 되는 값은 `Validate`로 막았다.
- [ ] 새 항목의 고정 값은 원본에만 있고, 판 데이터에는 원본 ID와 바뀌는 값만 들어간다.
- [ ] `[BindTo]` 인자는 `GameIds` 상수이고, 사용 기능·능력 함수에는 이벤트 매개변수가 없다.
- [ ] 도전과제의 결과는 프로필 쪽 값에 준다.
- [ ] 로그·조건이 읽을 값이 이벤트 필드에 있다.
- [ ] 항목의 ID가 게임에 나오는 자리(덱·상점·전투·가방)에 들어가 있다.
- [ ] 모니터에서 반응을 보고 Undo까지 확인했다.
- [ ] 도감 사이드바가 "검증 통과"다.
- [ ] 프로젝트 기능은 `BoundOutcomes`를 확인하고 `RunOutcomes`로 결과를 실행한다.

## 7. 더 보기

- [04. 인스턴스](../Manual/04-Instances.md) — 만들기, 기능 필드, 사본
- [16. 능력과 사용 기능](../Manual/16-Abilities-Usable.md) — `Usable`, `Effect<T>`·`TargetSelector<T>`, `Game.Use`의 흐름
- [11. Trigger와 조건](../Manual/11-Triggers-Conditions.md) — 이벤트 받기·값 조건, 코드로 쓰는 조건 Block
- [12. 결과(Outcome)](../Manual/12-Outcomes.md) — 결과 종류, 대상에게 효과, `Reward`
- [13. `[BindTo]`](../Manual/13-BindTo.md) — 함수 모양, 연결 실패 알림
- [14. 도전과제](../Manual/14-Achievements.md) · [15. 로그](../Manual/15-Stats-Logs.md)
- [17. 프로젝트 고유 기능](../Manual/17-Custom-Features.md) — 도감 종류 + 감시, 퀘스트 전체 예제
- [22. 모니터와 디버깅](../Manual/22-Monitor-Debug.md) — 이벤트 호출, 트리거로 달성
