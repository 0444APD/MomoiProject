# 콘텐츠 시험 10 — 와일드프로스트(Wildfrost)

와일드프로스트(턴제 덱빌딩·양편 2줄 × 3칸 보드 전투)의 핵심 메커니즘·스탯·버프/디버프·키워드·참(Charm)·왕관(Crown)·종(Bell)·일일 원정(Daily Voyage)을 GBible로 옮긴다고 보고, 조각(효과 문장)마다 어떻게 만드는지 분류한 기록이다. 코드는 쓰지 않았고 문서·라이브러리는 고치지 않았다.

- 자료: 위키 원문(영어) `Getting_Started` · `Stats` · `Counter` · `Attack` · `Keywords` · `Charms` · `Bells` · `Crowns` · `Leaders` · `Map` · `Daily_Voyage`(+ 같은 폴더의 `Clunkers` · `Companions` · `Items`). 참 목록 표는 위키 모듈이라 원문에 없어서, 참은 "효과의 모양"별로만 나눴다.
- 읽은 문서: `AI/ProjectConventions.md` · `AI/Reference.md` · `AI/GimmickCatalog.md` · `AI/PlanningSpec.md`(4절), 매뉴얼 11 · 12 · 16 · 17 · 28 · 29 · 30 · 37장. 문서로 판단이 안 된 두 곳은 코드(`Runtime/Core/Statuses.cs` · `Passives.cs`)를 봤다(5절에 "코드 확인"으로 적었다).
- 분류: **DB**(기본 Block·DB 칸만) · **Block**(재사용 Block 하나를 프로젝트가 더함) · **BindTo**(그 조각만의 코드) · **메인 룰**(메인 룰이나 메인 룰에 미리 둔 자리가 처리) · **화면** · **예외** · **문법 밖**. 조각 하나에 여러 층이 섞이면 가장 깊은 층 하나로 셌다(DB < Block < BindTo < 메인 룰).

---

## 1. 한 줄 결론

문법 밖 **0**, 예외 **5**(뿌리는 둘: **효과 수치 배율** 4 — Lumin · Critical · Lumin Bell · Boosted Leader의 "+1 효과", **침묵** 1 — Ink). 가장 큰 빈틈은 **카드 효과의 "주황 숫자"를 한꺼번에 키우는 자리가 GBible에 없다**는 것이다. 그래서 기본 효과 Block(`상태 효과 걸기` · `필드 바꾸기`)을 카드 효과에 쓰지 못하고 프로젝트 Block으로 다시 만들어야 한다. 그다음으로 큰 빈틈은 문서의 턴 순서가 원작과 다르다는 것이다(원작은 열 우선, 문서는 줄 우선).

---

## 2. 메인 룰을 GBible로 세우기

### 2-1. 조각 · 자리 · 순서

| | 와일드프로스트 | GBible |
|---|---|---|
| 조각 | 리더·동료·Clunker·적·소환(Shade) / 손패의 아이템 | 전투 사본 `Unit`(고유 `id` + 원본 ID `card`) — 덱 카드마다 전투 시작에 하나, 적·소환은 `Game.NewId` |
| 자리 | 양편 보드 2줄 × 3칸, 손패 한 줄 | `battle.board`·`battle.enemies` = `List<Unit>`(6칸, 빈 칸 `null`, 줄 길이 3, 두 보드 모두 x = 0이 앞) · `battle.hand`(줄 길이 0, 맨 오른쪽 = 목록 맨 끝) |
| 순서 | 적 먼저, 각 보드 **앞 열 위→아래, 다음 열 위→아래**(위키 그림 1·2 / 3·4 / 5·6) | 턴 함수가 칸 번호 `y*3 + x`를 **열 우선**(x 바깥, y 안쪽)으로 돈다. 목록 순서(줄 우선)와 다르다 → 5절 1 |
| 카운터 | 턴마다 −1, 0이면 발동 후 되돌림 | 필드 `counter`·`maxCounter` + 카운터 동사 `Rules.CountDown`(행동 직전 이벤트 `카운터` — Snow가 막는다) |
| 공격 대상 | 맞은편 같은 줄 맨 앞, 비면 다른 줄 | 프로젝트 대상 Block `공격 대상`(안에서 `Placement.Around(…, RowFirst/RowLast/SameRow, target: 맞은편)`, 비면 다른 줄, Aimless·Barrage·Longshot·Haze·Bombard·반응 대상). 카드 글의 고정 대상("뒤의 아군", "같은 줄 아군")은 기본 `자리로 고르기` |
| 반응 | 조건이 되면 발동, **발동 중인 카드의 효과가 끝난 뒤** | 패시브 '놓인 것마다'(`ownerPath` `board`/`enemies`, `matchField` `trait`)가 감지만 하고 결과 Block `반응 대기`로 대기열에 넣는다. 메인 룰이 발동이 끝날 때마다 대기열을 턴 순서로 비운다 |

### 2-2. 데이터 모양

```csharp
// 판(run): 덱은 객체 목록. 참·종은 붙일 때 이 필드를 "한 번" 바꾼다(다시 계산하지 않는다 — 원작 규칙)
[Serializable] public class CardStats {
    public string card, kind;                 // 원본 ID(DB › 인스턴스 CardData) · leader/companion/clunker/item
    public int hp, attack, hasAttack, counter, scrap, frenzy;
    public int aim;                           // 0 맨 앞 · 1 Aimless · 2 Barrage · 3 Longshot (서로 배타 = 필드 하나)
    public string ability, trait;             // 발동 때 쓰는 능력 ID(카드 글) · 반응·지속 패시브 ID
    public int abilityAmount, traitLevel;     // 리더처럼 카드마다 다른 수치(2~3)
    public List<StatusAmount> onHit;          // 맞힐 때 거는 것(Snowball·Truffle 참)
    public int explode, fury, greed, faith, consume, noomlin, zoomlin, recycle, critical, draw, trash, yank,
               smackback, spark, hogheaded, fragile, flourish, backline, frontline, unmovable;   // 키워드 = 정수(참이 필드 바꾸기로 붙인다)
    public string wildTag, soulboundTag;      // 카드에만 있는 키워드 패시브의 일치 필드(키워드마다 글자 필드 하나 = 패시브 ID)
}
[Serializable] public class DeckCard : CardStats { public string id; public List<string> charms; public int crowned, cursedCrown, injured; }
[Serializable] public class Unit : CardStats {
    public string id, deckCard, side; public int maxHp, maxCounter;
    public int triggered, snowedAtCountdown, inkedAtCountdown;          // 이번 턴 발동함 · 카운트다운 때 얼어/잉크 있었음
    public Dictionary<string, string> inflictedBy;                     // 상태 효과 ID → 마지막에 건 유닛 id(처치 기여)
}
// battle(세션): board/enemies(6칸) · hand/draw/discard(List<Unit>) · reactions(List<Reaction{unit,target,ability,order}>)
//               teeth(Dictionary<string,int> 반격 합산) · turn · phase(crown/play) · redraw · redrawCharged · wave · killsThisTurn
// run: deck · bling · inventory(List<string>: 종·참·"crown"·일일 변경) · map …
//      stats { id = "stats", handSize, companionLimit, charmLimit, redrawMax, waveDelay } — 종의 '가진 동안 능력치 보정'을 받는 객체(기믹 목록 2-9 '전체에')
```

### 2-3. 행동 · 규칙 함수 · 이벤트

- **행동**: `StartBattle`(덱 → `Unit`, Injured는 반, 왕관 단계) · `PlayCard { card, slot / target / row }` · `MoveUnit` · `Recall` · `RingRedrawBell` · `AttachCharm { charm, card }`(= `Game.Use(참 능력, run, 카드)`) · `MoveCrown` · 지도·상점·보상 행동.
- **흐름**: `PlayCard` → (아이템: 공격이 있으면 `Rules.Hit` → `Game.TryUse(ability)` → Consume / 동료: `Rules.Deploy` → Spark면 `Rules.Trigger`) → Noomlin·Zoomlin이 아니고 왕관 단계가 아니면 `Rules.Turn`.
- `Rules.Turn`: 적 보드 → 내 보드, 각각 열 우선. Snow면 `snowedAtCountdown = 1`(Ink도 같이 표시)하고 건너뛴다. 아니면 `Rules.CountDown(u, 1)` → 0이면 `Rules.Trigger(u)` + 되돌림. 끝에 `Rules.EndOfTurn`(이벤트 `TurnEnded` → Shroom · Summoned · 표시된 것만 Snow/Ink −1 · 웨이브 · 다시 뽑기 종).
- `Rules.Trigger(u, forced, byReaction)`: `1 + frenzy`번 { `공격 대상`마다 `Rules.Hit` → Teeth 합산 피해 한 번 → 살아 있으면 `Game.TryUse(u.ability, u)` } → 이벤트 `UnitTriggered`(Spice·Frost 끝) → `DrainReactions`. 반응으로 발동하면 카운터를 되돌리지 않는다.
- **동사(행동 직전 이벤트 → 이벤트)**: `Rules.Damage`(`피해` 행동 직전 이벤트 `{sourceId, targetId, amount, kind, hit}` → `DamageTaken`) · `Rules.Hit`(공격이 있으면 0이어도 `Hit`, 맞힐 때 디버프는 막혀도 건다) · `Rules.Destroy`(`Destroyed {unit, killer, sacrificed}`) · `Rules.Heal`(`최대 체력` 행동 직전 이벤트 — Fragile) · `Rules.CountDown`(`카운터` 행동 직전 이벤트) · `Rules.Consume`(`없애기` 행동 직전 이벤트 — Breakfast) · `Rules.GainBling`(`Bling` 행동 직전 이벤트 — Blingsack·Gold Blade) · `Rules.Draw` · `Rules.Deploy`(`Deployed`).
- **계산 함수**: `Rules.Attack(u)` = `Game.Attribute(u, "attack")`(Spice·Frost 보정) + Fury(`Placement.Is(…, RowAlone)`) + 오라(같은 편 지속 효과, Ink면 뺌) + 종(Battle Bell). `Rules.EffectValue(user, n)` = 효과 수치 배율(4절 1). `Rules.Has(u, 키워드)` = 필드 값, Ink면 0.
- **명세서 4-2 표시**: '놓인 순서·위치가 효과를 바꾼다' · '한 조각이 여러 칸을 차지한다'(보스 두 칸) · '효과가 반복 발동한다'(Frenzy) · '판정 규칙이 바뀐다'(대상 규칙). 4-2 목록에 없어 표시할 칸이 없는 것: **반응 대기열 · 효과 수치 배율 · 침묵 · 처치 기여(가장 최근에 건 쪽) · 카운트다운을 겪은 것만 턴 끝 감소**(5절 7).

---

## 3. 분류 표

### 3-1. 기본 스탯

| 조각 | 분류 | 만드는 법 |
|---|---|---|
| 1 Health(리더가 죽으면 판 끝) | 메인 룰 | `hp`·`maxHp` 필드, `Rules.Damage` → 0 이하면 `Rules.Destroy`, 리더면 판 패배. 핵심 동사라 4-2 표시는 없다 |
| 2 Scrap | 메인 룰 | `Rules.Damage` 안의 분기: 받은 뒤 양 ≥ 1이면 `scrap −1`(양과 상관없이). Shroom도 저절로 1. Overburn은 18 |
| 3 Attack(공격 없음 · 0 · 음수) | 메인 룰 | `attack` + `hasAttack` + `Rules.Attack`. 음수를 허용하고 피해에서 Bom을 더한 뒤 0으로 자른다. 공격이 있으면 0이어도 `Hit` |
| 4 Counter(Sun Rod 즉시 발동 · 동시에 0이면 턴 순서 · 최대 카운터 최소 1) | 메인 룰 | `Rules.CountDown(u, n)`: 행동 직전 이벤트 `카운터` → 0이 되면 바로 발동(턴 카운트다운 전), 여럿이면 턴 순서. 최대 카운터 바꾸기는 Block `최대 카운터 바꾸기`(최소 1, 지금 값도 같이) |
| 5 Reaction | 메인 룰 | 감지 = 카드 패시브(DB, `perElement` · `matchField` `trait` · `holderField` `targetId` 등), 결과 = Block `반응 대기`(능력 ID · 대상). `DrainReactions`가 살아 있음·Snow·Ink를 보고 `Rules.Trigger(u, 대상, byReaction: true)`. 4-2에 칸 없음 |
| 6 Frenzy | 메인 룰 | `frenzy` 필드, `Rules.Trigger`가 공격까지 반복(행동 직전 이벤트 '사용' 받기 `times`는 능력만 반복하므로 쓰지 않는다). Spice·Frost는 다 끝난 뒤 한 번 빠진다. 4-2 '반복 발동' |

### 3-2. 버프

| 조각 | 분류 | 만드는 법 |
|---|---|---|
| 7 Block | DB | 상태 효과 `block`(버프, 영구, 중첩 더하기). 트리거 행동 직전 이벤트 `피해` 받기(`holderField` `targetId`, 행동 직전 이벤트 조건 `amount > 0`, `order` 2) → 행동 직전 이벤트 고치기 `막기` + `상태 효과 풀기` 1. 맞을 때 디버프는 메인 룰이 따로 건다 |
| 8 Lumin(효과 두 배, 새로 걸면 앞의 것은 잃음) | **예외** | 4절 1. 하나만 남기기는 걸릴 때 결과의 `[BindTo]`(같은 한 번의 사용으로 건 것은 남김 — Barrage Lumin Vase) |
| 9 Shell | BindTo | 상태 효과 행동 직전 이벤트 `피해` 받기(`targetId`, `order` 3): 깎는 양이 피해에 따라 정해져 `amount`와 중첩을 서로 깎는다(기믹 목록 2-10 그대로) |
| 10 Resist Snow | BindTo | 상태 효과 행동 직전 이벤트 `상태 효과 걸기` 받기(`targetId`, `statusId` = snow) → `stacks = max(0, 1 − 지금 Snow)`, 0이면 막기. 계산이라 값 틀로 안 된다 |
| 11 Spice | DB | 상태 효과: 능력치 보정 `attack` +1/중첩, 지속 이벤트 1번(셀 이벤트 `UnitTriggered`, 가진 쪽 필드 `unitId`), 다시 걸 때 중첩 더하기 |
| 12 Teeth | Block | 상태 효과: 이벤트 `Hit`(`holderField` `targetId`) → Block `반격 쌓기`(`battle.teeth[sourceId] += {stacks}`). 메인 룰이 그 공격 뒤 **한 번의 피해**로 낸다(Barrage로 Teeth 여럿 = 한 번 — Block·Scrap·Demonize가 한 번만 반응). 출처 = Teeth를 가진 쪽(처치 기여) |

### 3-3. 디버프

| 조각 | 분류 | 만드는 법 |
|---|---|---|
| 13 Bom | DB | 상태 효과 행동 직전 이벤트 `피해` 받기(`targetId`, `order` 0) → `amount` 더하기 `{stacks}`. 줄지 않는다. 0 공격 맞음에도 붙는다 |
| 14 Demonize | DB | 행동 직전 이벤트 `피해` 받기(`targetId`, 행동 직전 이벤트 조건 `amount >= 1`, `order` 1) → 곱하기 2 + `상태 효과 풀기` 1 |
| 15 Frost | DB | Spice와 같고 보정 −1/중첩. 음수 공격 허용은 3 |
| 16 Haze | Block | `공격 대상` Block이 Haze면 같은 편 다른 카드 무작위 하나. 공격마다 −1은 상태 효과 트리거 이벤트 `Attacked`(`attackerId`) → `상태 효과 풀기` 1(DB) |
| 17 Ink | **예외** | 4절 2. 턴 끝 −1은 Snow와 같은 메인 룰 |
| 18 Overburn | BindTo | 상태 효과 트리거 `OR`(`상태 효과 걸림` · `DamageTaken` · 최대 체력 바뀜, 가진 쪽 필드) AND 조건 `Overburn ≥ 지금 체력`(프로젝트 조건 Block) → 자기와 같은 줄 아군에게 중첩만큼 피해. Clunker는 걸리면 바로. 처치 기여 = 체력을 Overburn 아래로 만든 마지막 쪽(피해·추가 Overburn)이라 코드 |
| 19 Shroom | Block | 상태 효과(영구) 트리거 `TurnEnded` → 프로젝트 효과 `피해`(값 `{stacks}`, 맞음 아님, 출처 = `inflictedBy[shroom]`) + `상태 효과 풀기` 1. 거는 쪽은 Block `상태 효과 걸기(건 쪽 기록)` — 기본 Block은 출처가 항목 ID이고 다시 걸 때 출처를 바꾸지 않는다(5절 3) |
| 20 Snow(카운터·반응 멈춤, 맞을 때 효과는 그대로) | 메인 룰 | 상태 효과 행동 직전 이벤트 `카운터` 받기(`targetId`) → 막기(DB). 반응 막기는 `DrainReactions`. **카운트다운 때 얼어 있었던 것만** 턴 끝에 −1 → `snowedAtCountdown` 표시를 메인 룰이 보고 `Game.RemoveStatus(u, snow, 1)` |

### 3-4. 대상 키워드

| 조각 | 분류 | 만드는 법 |
|---|---|---|
| 21~23 Aimless · Barrage · Longshot | Block | 필드 `aim` 하나(서로 배타 = 나중 것이 이김). 프로젝트 대상 Block `공격 대상`이 `RowFirst`(기본)·`SameRow` + 무작위 1(Aimless)·`SameRow`(Barrage)·`RowLast`(Longshot)를 맞은편 목록에 대고 고르고, 줄이 비면 다른 줄. 반응 대상이 있으면 Aimless·Barrage는 **그 대상의 줄**, Longshot은 그 대상. 아이템은 고른 줄. 카드 하나만 보면 기본 `자리로 고르기`로도 되지만 참·Gachapomper가 실행 중 바꾸고 "빈 줄이면 다른 줄"이 `자리로 고르기`에 없다(37장 5-3에도 적혀 있음) |

### 3-5. 피해·공격 키워드

| 조각 | 분류 | 만드는 법 |
|---|---|---|
| 24 Bootleg | BindTo | 배치될 때(`Deployed`) 체력·공격·카운터를 2~5로 `Game.Random`. Faith는 그 뒤 |
| 25 Explode X | 메인 룰 | `Rules.Destroy`가 `explode`를 읽어 맞은편 줄 전체에 X(동시, 맞음 아님). X는 효과 수치라 배율을 탄다 |
| 26 Faith X | Block | 소환 효과 Block(`소환`)이 사용자의 `faith`만큼 공격(있을 때만)·체력을 더한다 |
| 27 Fury X | 메인 룰 | `Rules.Attack`이 `Placement.Is(side, u, 3, RowAlone)`이면 +X. 패시브 '가진 동안 능력치 보정'은 조건을 받지 않는다(37장 5-4) |
| 28 Greed | 메인 룰 | 공격·맞힐 때만 `피해` 행동 직전 이벤트에 `bling / 50` 더하기(늘 붙는 공격력이 아님). 적의 Greed도 플레이어 Bling을 본다 |
| 29 Soulbound | Block | 패시브 `soulbound`(`ownerPath` `board`, `perElement`, `matchField` `soulboundTag`): 남은 Soulbound 카드마다 이벤트 `Destroyed`(이벤트 조건 `sacrificed` 참 · `soulbound` 참) → `사용자 자신` → 효과 `피해` 100(자기 자신이 출처, 맞음 아님) |

### 3-6. 제한 키워드

| 조각 | 분류 | 만드는 법 |
|---|---|---|
| 30 Consume | 메인 룰 | `PlayCard` 끝(모든 효과·Frenzy 뒤)에 `Rules.Consume` — 행동 직전 이벤트 `없애기`를 거쳐 Breakfast Bell이 막는다. 없애진 뒤 효과(Strawberry·Boonfire 참)는 `Destroyed` 이벤트 |
| 31 Fragile | DB | 상태 효과 또는 패시브: 행동 직전 이벤트 `최대 체력` 받기(`targetId`) → 막기. 회복 대상으로는 여전히 고를 수 있다 |
| 32 Hogheaded | 메인 룰 | `Recall` 행동이 `Rules.Has(u, hogheaded)`면 `Game.Fail`(Ink면 된다) |
| 33 Recycle X | Block | 능력의 사용 조건 Block `손패의 Junk ≥ X`(프로젝트 조건) + 효과 Block `Junk 없애기`(오른쪽부터, 효과보다 먼저, Frenzy여도 한 번). X는 효과 수치라 Lumin·Cake가 키운다(4절 1) |
| 34 Summoned | 메인 룰 | 불러들이기 금지(32와 같음) + `EndOfTurn`이 `triggered`면 지금 체력 −1(피해 아님, Frenzy여도 한 번) |

### 3-7. 기타 키워드

| 조각 | 분류 | 만드는 법 |
|---|---|---|
| 35 Cleanse | DB | 효과 `상태 효과 풀기`(`kind` 디버프) |
| 36 Critical | **예외** | 손패 맨 오른쪽 판정은 DB(조건 `자리 검사` 목록 맨 끝, `hand`, 줄 길이 0)지만, 두 배는 4절 1. Lumin과 곱이 아니라 더하기(세 배) |
| 37 Draw X | Block | 효과 Block `뽑기` → `Rules.Draw`(덱이 모자라면 버린 더미를 섞어 마저, 없으면 알림) |
| 38 Flourish | BindTo | 이벤트 `Healed`(가진 쪽)의 회복량만큼 최대 체력 +. 값 틀이 이벤트 필드를 읽지 못한다 |
| 39 Noomlin | 메인 룰 | `PlayCard`가 `noomlin`이면 `Rules.Turn`을 부르지 않는다 |
| 40 Smackback | 메인 룰 | `Rules.Hit`이 맞은 쪽 `smackback`이면 반응 대기열에 (맞은 쪽, 때린 쪽). 같은 편이 때려도. Barrage로 여럿이면 턴 역순 |
| 41 Spark | 메인 룰 | `Rules.Deploy` 끝에 `Rules.Trigger`(카운터는 되돌리고 그 턴 카운트다운도 겪는다) |
| 42 Trash X | Block | 효과 Block `카드 만들기`(Junk X장, 손패 **왼쪽**에 — Critical을 방해하지 않음). Frenzy·대상 없음이어도 만든다 |
| 43 Yank | Block | 효과 Block `줄 맨 앞으로 당기기`(Backline·Unmovable·두 칸 보스 검사, Barrage와 함께면 줄 뒤집기) |
| 44 Zoomlin | 메인 룰 | 39와 같고 낸 뒤 `zoomlin = 0` |

### 3-8. 적 전용 키워드

| 조각 | 분류 | 만드는 법 |
|---|---|---|
| 45 Absorb | BindTo | 아군의 공격·체력·상태 효과(`Game.ApplyStatus`로 하나씩)·능력을 가져온다. `trait`(반응 패시브)가 칸 하나라 반응 둘을 합치지 못한다(5절 2) |
| 46 Backline | 메인 룰 | 적 배치 규칙: 늘 맨 뒤, 앞으로 당겨지지 않음, Yank 불가(Ink면 풀림) |
| 47 Bombard | Block | `공격 대상` Block이 유닛의 `bombard`(칸 번호 목록)를 친다. 맞힌 뒤 다시 굴리기(`Game.Random`). Haze면 아군 뒤에 그 칸도 |
| 48 Frontline | 메인 룰 | 배치 때 맨 앞, 다른 카드를 뒤로 민다 |
| 49 Split | BindTo | 체력·상태 효과 중첩을 반(올림)으로 나눈 사본 둘(`Game.NewId`), 참 유지 |
| 50 Unmovable | 메인 룰 | `MoveUnit`·Yank 검사. 불러들이기는 됨 |
| 51 Wild | DB | 패시브 `wild` 두 벌(`ownerPath` `enemies` · `board` — 맞은편 Wild가 부서져도 받는다, `perElement`, `matchField` `wildTag`): 이벤트 `Destroyed`(이벤트 조건 `wild` 참 · `inked` 거짓) AND `상태 효과 검사`(사용자의 ink ≤ 0) → `사용자 자신` → `필드 바꾸기` `frenzy` +1 |

### 3-9. 특수 · 숨은 키워드

| 조각 | 분류 | 만드는 법 |
|---|---|---|
| 52 Injured | 메인 룰 | 덱 카드 `injured`. `StartBattle`이 지금 체력·공격을 반(올림), 이긴 전투에서 죽지 않았으면 0으로(벤치에 있어도) |
| 53 Active | DB | 카드 패시브의 가진 목록 = 보드라 떠나면 저절로 멈춘다 |
| 54 Additional damage | DB | 상태 효과(능력치 보정 `attack`): 동료는 지속 이벤트 1번 `AttackFinished`(Frenzy 사이에도 빠짐), 아이템은 `TurnEnded` 1번. "~에게 추가 피해"는 행동 직전 이벤트 `피해` 받기 더하기 |
| 55 Ally | DB | `자리로 고르기` 나를 뺀 모두 · 같은 줄 · 오른쪽(= 뒤). 아이템은 손에서 내므로 `플레이어 데이터 목록` `battle.board` |
| 56 Apply | DB | 이벤트 `상태 효과 걸림` |
| 57 Deploy | 메인 룰 | `Rules.Deploy`(손패·소환·적 등장) → `Deployed` |
| 58 Destroy | 메인 룰 | `Rules.Destroy`: 체력·Scrap 0 이하 또는 "없앤다". 손패에서 부서져도 `Destroyed`(Gunk Gobbler) |
| 59 Healed | 메인 룰 | `Rules.Heal`: 체력 회복 또는 최대 체력 올림 → `Healed` |
| 60 Hit | 메인 룰 | `Rules.Hit`: 공격이 있는 카드·아이템이 칠 때만(0이어도). Shroom·Teeth·Explode는 맞음이 아니다 |
| 61 On kill(처치 기여) | 메인 룰 | `Destroyed.killer` = 마지막 피해를 준 쪽. Shroom·Overburn은 `inflictedBy`(가장 최근에 건 쪽), Bom·Demonize를 건 쪽은 아님, Teeth·Explode 튄 피해는 그 원인. Block `상태 효과 걸기(건 쪽 기록)`과 한 쌍 |
| 62 Sacrifice | 메인 룰 | `killer`가 같은 편이면 `sacrificed`(자기 자신은 아님) |

### 3-10. 전투 시스템

| 조각 | 분류 | 만드는 법 |
|---|---|---|
| 63 보드·턴 순서(양편 2 × 3, 적 먼저, 열 우선) | 메인 룰 | 2절. 4-2 '놓인 순서·위치' |
| 64 손패 6장 · 한 턴 한 장 · 아이템 대상 규칙(공격 > 0·Aimless·Barrage면 손패에 못 냄) | 메인 룰 | `PlayCard`, 대상 위치 검사는 능력의 사용 조건 Block `고른 대상이 손패가 아님` + `failText` |
| 65 동료 배치·옮기기·불러들이기(디버프 풀기 + 체력 5) · 동료 한도 · 벤치 | 메인 룰 | `Recall`: `상태 효과 풀기`(디버프)는 DB 효과, 체력 +5와 손패로 옮기기는 행동 |
| 66 Redraw Bell(4턴 안 쓰면 충전) | 메인 룰 | `battle.redraw`·`redrawCharged`, `RingRedrawBell`(충전 아니면 턴을 씀), 이벤트 `RedrawRung { charged }` |
| 67 Wave Bell(증원) | 메인 룰 | `battle.wave` 카운터, 0이면 다음 무리 배치. `run.waveDelay` 반영 |
| 68 보스 두 칸 | 메인 룰 | 4-2 '여러 칸': 머리 칸만 목록에, 나머지는 '차지됨' 표시 → `공격 대상`·Yank가 반영 |

### 3-11. 판 시스템 — 리더 · 참 · 왕관 · Bling · 보상

| 조각 | 분류 | 만드는 법 |
|---|---|---|
| 69 부족(시작 덱·카드 풀·해금) | DB | 인스턴스(부족별 시작 덱) + 가중치 표(줄 조건 = 해금) |
| 70 Leaders(부족마다 무작위 이름·능력·수치, 33% Yank) | BindTo | 결과 `가중치 표에서 고르기`로 템플릿 셋 → 고른 뒤 `[BindTo]`가 범위를 `Game.Random`, Yank는 `Game.Chance(0.33)`, 수치는 `abilityAmount`·`traitLevel` |
| 71 Pets | Block | 프로필 해금(DB) + 덱에 넣기는 Block `카드 받기`(77) |
| 72 Charms(붙이면 바로 바꾸고 다시 계산 안 함 · 순서 중요 · 한도 3 · 없는 특성은 못 바꿈 · 겹치거나 맞지 않는 키워드 불가 · 전투 중 붙이면 다음 전투부터) | Block | 참 = DB › 능력(사용자 `run`, 대상 = 덱 카드). 사용 조건 = 붙일 수 있나(프로젝트 조건 `고른 카드 필드 검사` · `참 수 < run.charmLimit`), 효과 = `필드 바꾸기`(공격 +2 · 카운터 −1 · 키워드 = 1 · `aim` 값으로) · Block `맞힐 때 효과 더하기`(+ `hasAttack` = 1). Durian·Jimbo처럼 바꾸는 것은 `[BindTo]`, Lumin Ring·Cake는 4절 1. 덱 카드만 바꾸므로 전투 사본에는 다음 전투부터 |
| 73 Cursed Charms(한도를 차지) | Block | 72와 같은 모양(음수 효과) |
| 74 Crowns(리더와 함께 먼저 배치 · 공짜로 냄 · 전투 사이 옮김 · 가격 75 Bling · 만든 카드는 왕관 아님) | 메인 룰 | `StartBattle`의 왕관 단계: 왕관 카드를 먼저 손에 → 내도 턴을 쓰지 않음, 왕관 아닌 카드는 `Game.Fail("fail.crownFirst", "왕관 카드를 먼저 내세요")`. 못 내는 왕관 카드는 다시 뽑기로 버림. `MoveCrown` 행동, 가격은 밸런스 값 |
| 75 Cursed Crowns(못 뗌, 체력·공격 −1 "가능하면") | BindTo | 최소 체력 1·공격 0, 공격·체력이 없는 카드에도 붙는다 |
| 76 Bling · 콤보 처치 · Gobling | Block | `run.bling` + `Game.Pay`(쓰기). 얻기는 효과 Block `Bling 얻기`(행동 직전 이벤트 `Bling`을 거쳐야 Blingsack·Gold Blade가 붙는다). 콤보 = 메인 룰이 턴마다 처치 수를 세서 `Rules.GainBling(…, source: combo)` |
| 77 덱에 새 카드 받기(보상 공통) | Block | 결과 Block `카드 받기`: 원본 ID → `DeckCard { id = Game.NewId("card"), card = ID, … }`. `가중치 표에서 고르기`·`플레이어 데이터에 주기`는 객체 목록에 새 id 조각을 만들지 못한다(5절 4) |
| 78 보스 보상(Sun Vault: 종·참·왕관 중 정확히 2개) | Block | `가중치 표에서 고르기`(최소 2 · 최대 2, 보스마다 표) → `run.inventory`(종·참·`crown`을 한 ID 목록에. 종 패시브의 가진 목록도 이것) + 고른 뒤 결과 Block `받음 알리기`(고른 ID마다 이벤트 `RewardTaken { id }` — 바로 바꾸는 종이 받는다. `이벤트 쏘기`는 값이 고정이라 고른 ID를 못 싣는다) |

### 3-12. 종(Bell)

종 = DB › 패시브(가진 목록 `run.inventory`). 한도·수치를 바꾸는 종은 '가진 동안 능력치 보정'(받는 쪽 `stats`)이고 메인 룰이 `Game.Attribute(run.stats, …)`를 읽는다. "바로 바꾸는" 종(Health·Strength·Noomlin)은 이벤트 `RewardTaken`(이벤트 조건 `id` = 자기 ID)을 받아 덱 카드를 한 번 바꾼다(참처럼 다시 계산하지 않음 — 원작 규칙).

| 조각 | 분류 | 만드는 법 |
|---|---|---|
| 79~83 Sun Bell of Hands · Fellowship · the Bell · Time · Charge | DB | 가진 동안 능력치 보정(받는 쪽 `stats`): `handSize` +1 / `companionLimit`·`charmLimit` +1 / `redrawMax` −1 / `waveDelay` +1. Charge는 이벤트 `BattleStarted` → `battle.redrawCharged` = 참 |
| 84 Sun Bell of Health | DB | `RewardTaken` → `플레이어 데이터 목록` `run.deck` → `조건으로 거르기` `kind == leader` → `필드 바꾸기` `hp` +6 |
| 85 Sun Bell of Recall | DB | 이벤트 `Recalled`(조건 `kind == companion`) → `battle.redraw` −2 |
| 86 Noomlin Sun Bell | DB | `RewardTaken` → `run.deck` → `무작위 n개` 2 → `필드 바꾸기` `noomlin` 값으로 1 |
| 87 Sun Bell of Strength | DB | `RewardTaken` → `run.deck` → 거르기 `kind == item`, `hasAttack == 1` → `attack` +2(얻은 뒤의 아이템은 아님) |
| 88 Breakfast Sun Bell | DB | 행동 직전 이벤트 `없애기` 받기 AND `플레이어 데이터 검사`(`battle.breakfastUsed` 거짓) → 막기 + `breakfastUsed` = 참 |
| 89 Infinity Sun Bell | Block | 이벤트 턴 시작 AND `redrawCharged` → 효과 Block `뽑기` 1 |
| 90 Storm Bell 고르기(점수 한도 5 → 최대에서 이길 때마다 +1, 10까지 · 10점이면 진짜 마지막 싸움 · 금테 · Overcrank) | 메인 룰 | 판 시작 설정 행동이 점수 합을 `profile.stormLimit`와 비교(`Game.Fail`). 금테·Overcrank는 도전과제(DB), 진짜 마지막 싸움은 지도 표 줄의 조건(DB) |
| 91 Blingsnail Bell | BindTo | 상점 가격 행동 직전 이벤트에 +5, 왕관은 + 산 수 × 10(곱이라 값 틀로 안 됨) |
| 92 Bell of Death | DB | 이벤트 `Destroyed`(동료) → `이벤트의 개체` `deckCard` → `필드 바꾸기` `injured` 값으로 1 |
| 93 Titan Bell | Block | 이벤트 적 등장(보스·중간 보스) → 효과 Block `참 붙이기`(보스마다 가중치 표). 두 단계 보스는 첫 단계만 |
| 94, 96, 99, 102 Frosthand · Icebourne · Frostbourne · Dread Bell | Block | 이벤트 `BattleStarted`(전투 번호 조건) → `battle.enemies` → 거르기(보스 아님 · Gobling 등 아님) → `무작위 n개` 2(Heart of the Storm은 1) → `참 붙이기` |
| 95 Gunk Bell | Block | 보스 처치 → `카드 받기`(Gunk Fruit) |
| 97 Horde Bell | DB | 가진 동안 능력치 보정 `stats.waveDelay` −1 |
| 98 Gloom Bell | BindTo | 카드 보상 후보를 만들 때 일부에 저주 참(보상 생성 자리) |
| 100 Gobbler Bell | Block | 보스 전투 `BattleStarted` → 효과 Block `적 소환`(Gobbler) |
| 101 Tyrant Bell | BindTo | 지도 생성: 2~6번째 싸움 사이 상점 정확히 하나에 저주 왕관 |
| 103 Blood Bell | DB | 보스 처치 → `run.deck` → 거르기(팀) → `정렬해서 n개` `hp` 큰 것 1 → `hp` −2 |
| 104 Battle Bell(모든 동료 공격 +1) | 메인 룰 | `Rules.Attack`이 `Game.Owned(battleBell)`을 더한다. 패시브 '가진 동안 능력치 보정'은 받는 쪽이 하나라 "모든 동료"가 안 된다 |
| 105, 113 Blingsack · Gold Blade Bell | DB | 행동 직전 이벤트 `Bling` 받기(행동 직전 이벤트 조건 `source` = gobling / combo) → 곱하기 2 |
| 106 Bombskull Bell | Block | 아군 Clunker에 `참 붙이기`(Bombskull) |
| 107 Broken Bell | Block | 이벤트 `RedrawRung`(`charged` 거짓) → `뽑기` 3 |
| 108, 112 Fog · Goat Bell | Block | 102와 같은 모양(Ooba · Demon Eye 참, Gobling·Naked Gnome 제외) |
| 109 Frenzy Bell | DB | 판 시작 → `run.deck` → 거르기 아이템 → `frenzy` +1 |
| 110 Frozen Heart Bell | BindTo | 98과 같은 보상 생성 자리(Block 참, 싸움 사이 정확히 하나) |
| 111 Heart Bell(동료·적 체력 +2) | 메인 룰 | 전투 사본을 만들 때·적 등장 때 +2. 체력은 지금 값 필드라 Modifier가 맞지 않는다 |
| 114 Lumin Bell(모든 카드 효과 +1) | **예외** | 4절 1(더하기 쪽) |
| 115 Party Bell | DB | 가진 동안 능력치 보정 `stats.companionLimit` +큰 값 |

### 3-13. 일일 원정(Daily Voyage)

| 조각 | 분류 | 만드는 법 |
|---|---|---|
| 116 날짜 시드 판 | 메인 룰 | 판 시작 행동 → `GBibleRuntime.NewRun(session, RandomStreams.SeedOf("daily-" + 날짜))`(부가 기능 '시드') |
| 117 하루 한 번 | 메인 룰 | `profile.lastDaily`와 `Game.Day(초기화 시각)` 비교 → `Game.Fail` |
| 118 모든 카드·부족·이벤트·참 해금 | DB | 가중치 표 줄 조건을 `해금 OR run.daily`로 |
| 119 덱 무작위화(1~2장 빼기 · 1~4장 더하기 · 참 0~3) | BindTo | 판 시작 `[BindTo]`(시드 흐름 `Game.Random("daily")`) |
| 120 Boosted Leader(3가지 강화 중 "+1 카드 효과") | **예외** | 공격·체력·카운터 쪽은 `필드 바꾸기`, "+1 효과"가 4절 1 |
| 121 Frenzy Leader | DB | 리더 `frenzy` +1 |
| 122, 123 Helping Hand · Parting Gift | Block | `카드 받기`(무작위 동료 / 아이템 2) |
| 124, 125 Bad starting cards · Bad starting charms | Block | `카드 받기`(Gunk Fruit 1~2) / `참 붙이기`(저주 참 2) |
| 126 Remove starting cards | BindTo | 동료 포함 1~2장 무작위로 빼기 |
| 127 Half starting companion health | DB | `run.deck` → 거르기 동료·리더 → `hp` 곱하기 0.5(그 뒤 얻은 동료는 아님) |
| 128 Modifier Bell 2~3개 | DB | `가중치 표에서 주기`(Modifier Bell + Storm Bell, 서로 다른 것, 셋째는 꽝 무게) → `run.inventory` |

### 3-14. 지도와 지도 이벤트

| 조각 | 분류 | 만드는 법 |
|---|---|---|
| 129 지도(지역 3 · 단계마다 두 갈래 · 이벤트 수 3~4/1~2 · 싸움·보스 차례) | 메인 룰 | 지도 생성 규칙 함수 + 가중치 표(지역별 이벤트), 행동 직전 이벤트 `지도 만들기`(종이 고치는 자리), 길 고르기 행동 |
| 130, 131 Frozen Travellers(1/3, 건너뛰기 없음) · Treasure Chest(1/3, 건너뛰기) | Block | `가중치 표에서 고르기`(후보 3, 최소 1 / 0, 넣을 곳 비움) → 고른 뒤 결과 Block `카드 받기`(고른 원본 ID → 새 덱 카드) |
| 132 Blingsnail Cave(40~80) | DB | 가중치 표 한 줄(개수 40~80) → `run.bling`(숫자면 개수만큼 +) |
| 133 Charm Dispenser | DB | `가중치 표에서 주기` → `run.inventory` |
| 134 Gnome Traveller(몇 개든 고름, 둘은 Junk가 따라옴) | BindTo | `선택 요구`(최소 0 · 최대 3) → 고른 뒤 결과 `[BindTo]`가 짝 Junk까지 넣는다 |
| 135 Muncher(최대 2장 빼기) | Block | `선택 요구`(덱, 0~2) → 효과 Block `카드 빼기` |
| 136 Shade Sculptor(아이템·Clunker 복제, 참 유지·왕관 아님) | Block | `선택 요구` → 효과 Block `카드 복제`(`Game.NewId`, `crowned` = 0) |
| 137, 138 Woolly Snail · Charm Merchant | 메인 룰 | 상점 생성(아이템 4 · 하나 50% · 참 50/70/90 · 왕관 75 / 참 3 + 참 붙은 카드 1), 가격은 밸런스 값 + 행동 직전 이벤트 `상점 가격`, `Buy` 행동이 `Game.Pay` |
| 139 Injured Companion(지난 판이 졌으면 그 판의 동료, 참 유지) | BindTo | 판이 질 때 동료 하나를 `profile`에 남기고, 다음 판 지도 표 줄 조건 + 받을 때 `injured` = 1 |
| 140 해금(Snowdwell 건물·도전 → 부족·카드·이벤트·Pet) | DB | 도전과제 → 프로필 해금 목록, 가중치 표 줄 조건 |

---

## 4. 예외 · 문법 밖

문법 밖은 없다. 예외는 뿌리 둘이다.

### 4-1. 효과 수치 배율 — Lumin · Critical · Lumin Bell · Boosted Leader "+1 효과" (예외 4)

- **무엇**: 카드 글의 주황 숫자(Apply 2 Snow · Restore 3 · Gain +1 공격 · Deal 4 additional · Explode X · Draw X · Trash X · Recycle X(비용까지) · 카운터 −2)를 한꺼번에 키운다. Lumin·Critical은 각각 "기본값의 +100%"를 **더한다**(둘이면 세 배), 다른 값 바꾸기 뒤에 맨 마지막에 붙는다. Lumin Bell·Boosted Leader는 +1. **공격 스탯(주황 아님)은 키우지 않는다.**
- **왜 GBible 기능으로 안 되나**: 기믹 목록 2-2는 "동사 행동 직전 이벤트의 `amount` 곱하기"라고 하지만
  1. 공격 피해와 효과 피해가 같은 `피해` 행동 직전 이벤트를 지나 공격까지 두 배가 된다(행동 직전 이벤트에 `kind`를 넣으면 피하지만 아래가 남는다).
  2. 기본 `상태 효과 걸기`가 행동 직전 이벤트에 넣는 `sourceId`는 **항목 ID**(능력 ID)라서 Lumin을 가진 카드를 가리키지 못한다. 같은 능력을 가진 카드 둘을 구별하지 못한다.
  3. `필드 바꾸기`(Gain +1 공격·카운터 −2)·Explode·Draw·Trash·Recycle 비용은 지나는 행동 직전 이벤트가 없다.
  4. 기본 효과 Block의 수치 칸(`상태 효과 걸기`의 `stacks`는 정수)에 "사용자 기준 배율" 값 틀이 없다(`{level}`은 패시브에서만, 능력에서는 0).
- **우회**: 메인 룰에 `Rules.EffectValue(user, n, scalable)` = `(n + 종·리더 보너스) × (1 + Lumin + Critical)`을 두고, 카드 효과는 모두 프로젝트 효과 Block(`피해` · `회복` · `상태 효과 걸기(건 쪽 기록)` · `필드 바꾸기(배율)` · `카운터` · `뽑기` · `카드 만들기` · `Junk 없애기`)으로 만든다. 이 Block들이 수치를 이 함수에 통과시킨다. 기본 `상태 효과 걸기`·`필드 바꾸기`는 카드 효과에 쓰지 않는다(참·종처럼 카드 밖에서 덱을 바꾸는 데만).
- **라이브러리에 있으면 없어지는 것**: 기본 효과 Block이 수치를 내기 직전에 지나는 기본 행동 직전 이벤트(예: `효과 수치 { userId, ability, field, amount }`)나, 효과 기반 클래스의 "수치 칸 배율" 자리. 그러면 Lumin = 상태 효과 행동 직전 이벤트(`userId` 가진 쪽) 곱하기 2, Lumin Bell = 패시브 더하기 1로 DB가 된다. 덤으로 `상태 효과 걸기`의 출처를 사용자 id로 남기는 칸(4절 밖, 5절 3)도 같이 풀린다.

### 4-2. 침묵 — Ink (예외 1)

- **무엇**: "글이 빈 것처럼" 카드의 효과를 모두 끈다 — 발동 능력, 반응, 지속 효과("가진 동안 모든 아군 공격 +1"), 키워드(Backline·Wild·Bombard·Summoned·Hogheaded …). 공격·카운터는 그대로.
- **왜 GBible 기능으로 안 되나**: 패시브를 **가진 쪽마다 끄는 스위치가 없다**. 트리거는 패시브마다 조건(`상태 효과 검사` Ink ≤ 0)을 넣어야 하고(빠뜨리기 쉽다), `modifiers`(가진 동안 능력치 보정)는 조건을 받지 않아 끌 수 없다.
- **우회**: ① 카드 글은 모두 능력으로 내게 한다 — 발동은 `Game.TryUse(ability)`, 반응도 대기열에서 `TryUse`. Ink 상태 효과가 행동 직전 이벤트 `사용` 받기(`holderField` `userId`) → 막기(DB). ② 오라는 패시브 보정이 아니라 `Rules.Attack`이 같은 편의 오라 필드를 모으며 Ink를 뺀다. ③ 키워드는 `Rules.Has(u, 키워드)`가 Ink면 0. ④ 턴 끝 −1은 Snow와 같은 메인 룰 표시(`inkedAtCountdown`).
- **라이브러리에 있으면 없어지는 것**: 패시브에 "가진 쪽 조건"(트리거와 가진 동안 능력치 보정 둘 다에 걸리는 조건) 칸, 또는 상태 효과의 종류에 '침묵'(걸린 쪽의 패시브를 모두 끔).

### 4-3. 예외는 아니지만 기믹 목록 3절·명세서 4-2에 자리가 없어 메인 룰로 만든 것

| 무엇 | 메인 룰에 둔 것 | 있으면 좋은 것 |
|---|---|---|
| 반응은 발동 중인 카드의 효과가 끝난 뒤(Booshu 회복 → Spike Wall Smackback) | 반응 대기열 + `DrainReactions`(턴 순서, Barrage면 역순). `Game.ScheduleOn<UnitTriggered>(1, …)`로도 되지만 차례를 정렬할 수 없다 | 기믹 목록 3절에 '반응을 미룬다' 표시 |
| 턴 순서는 열 우선 | 턴 함수가 `y*3 + x`를 x 바깥으로 돈다 | '놓인 것마다' 차례를 열 우선으로 고르는 칸, 또는 `PlaceDirection`의 열 쪽 짝(열 맨 앞·열 맨 끝·열에 혼자) — 그러면 보드를 돌려 담을 수 있다 |
| 카운트다운을 겪은 것만 턴 끝 −1(Snow·Ink) | `snowedAtCountdown`·`inkedAtCountdown` 표시 → `EndOfTurn` | — |
| 처치 기여 = 가장 최근에 건 쪽 | `inflictedBy` + Block `상태 효과 걸기(건 쪽 기록)` | 다시 걸 때 `source`를 새 출처로 바꾸는 선택, 효과 '상태 효과 걸기'의 출처 = 사용자 id 선택 |
| Barrage로 Teeth 여럿 = 피해 한 번 | `battle.teeth` 합산 | — |
| 한 카드에 글 여럿(Absorb, 반응 + 참 반응) | `trait` 하나 + 키워드 필드 | 5절 2 |

---

## 5. 문서 빈틈

1. **턴 순서가 원작과 다르다.** 위키 그림(`11 9 7 || 1 3 5` / `12 10 8 || 2 4 6`)은 적 먼저, **앞 열 위→아래 → 가운데 열 → 뒤 열**(열 우선)이다. 기믹 목록 1절("적 먼저, 줄마다 앞에서 뒤로"), 명세서 4-1 예시("각 보드에서 위 줄부터 앞에서 뒤로"), 37장 5-3 코드 주석("칸 번호 순서 = 줄마다 앞에서 뒤로")은 줄 우선이다. 메인 룰 루프는 코드라 고치면 되지만, '놓인 것마다' 패시브의 차례는 목록 순서(줄 우선)로 고정이라 바꿀 수 없고, 보드를 열 우선으로 돌려 담으면 `같은 줄 맨 앞`·`같은 줄에 혼자`에 해당하는 열 쪽 방향·자리가 없어 '자리로 고르기'·'자리 검사'를 못 쓴다.
2. **'반응 = 패시브 놓인 것마다'(기믹 목록 1절)의 한계가 문서에 없다.** (코드 확인: `Passives.cs` `Matches`·`OrderedHoldersFor`)
   - `matchField`(일치 필드)는 글자 하나와 같은지만 본다 → 조각 하나에 '놓인 것마다' 패시브 하나. 카드 글 + 참이 더한 반응, Absorb처럼 글이 여럿이면 못 담는다.
   - `ownerPath`에 `board[].traits`처럼 조각 안의 목록을 쓰면 조각 하나에 패시브 여럿이 되지만, 그때 요소가 글자라 '놓인 것마다'를 켜면 가진 쪽이 글자가 된다 → 둘을 함께 쓸 수 없다(17장에 없음).
   - 찾은 우회: 키워드마다 글자 필드 하나(`wildTag` = "wild")를 두고 그 패시브의 `matchField`로 쓰면 조각 하나에 '놓인 것마다' 패시브가 여럿 붙는다(Wild·Soulbound). 다만 `필드 바꾸기`는 숫자만 바꾸므로 참이 붙이는 키워드에는 못 쓴다. 이 모양을 17장에 적어 두면 좋다.
   - 패시브의 가진 목록은 하나라서, 양편 보드(`board`·`enemies`)에 다 있을 수 있는 반응(Dread Bell로 적에게 붙는 Punchfist 참의 Smackback)은 DB 항목이 두 벌 필요하다(Wild는 그렇게 했다). 양편 보드를 어떻게 다루는지 문서에 없다. 참이 붙이는 키워드는 정수 필드 + 메인 룰로 돌렸다.
   - 원작 반응은 "발동 중인 카드의 효과가 끝난 뒤"인데, 패시브는 이벤트를 받은 그 자리에서 돈다. 미루는 자리가 3절에 없다.
3. **상태 효과의 출처로 처치 기여를 못 한다.** 기믹 목록 2-10 "처치 기여: 걸린 상태 효과의 출처(`source`)를 처치 이벤트의 주체로"는 (a) 효과 `상태 효과 걸기`가 출처로 **항목 ID**를 넣어(29장 3-2) 어느 카드가 걸었는지 모르고, (b) 이미 걸려 있을 때 다시 걸면 `source`를 바꾸지 않아(코드 확인: `Statuses.cs` `Apply`의 `existing` 분기 — `StatusApplied.source`만 새 값) "가장 최근에 건 쪽"이 안 된다. (b)는 29장에 적혀 있지 않다.
4. **보상으로 새 조각을 만드는 것이 기믹 목록에서 DB처럼 읽힌다.** 기믹 목록 1절·참조 7-22는 조각 목록을 "고유 id + 원본 ID 필드"의 객체 목록으로 두라고 하고, 2-11은 "N개 중 고르기 → '가중치 표에서 고르기'"라고만 한다. 그런데 객체 목록(덱)에는 원본 ID가 바로 들어가지 않아 고른 뒤 결과에 프로젝트 결과 Block(`Picked`의 ID → `Game.NewId`)이 필요하다 — 30장 3-3(“덱에 넣는 일은 코드로”)에는 적혀 있지만 기믹 목록에서는 보이지 않는다. 그래서 덱 카드 받기(Pet · 지도 이벤트 · Gunk Bell · 일일 원정)가 모두 Block이 된다. 기믹 목록 2-11에 한 줄, 또는 '원본 ID 필드 + 새 id로 넣기' 칸이 있으면 DB가 된다.
5. **참의 모양이 원작과 다르게 안내된다.** 기믹 목록 2-9·참조는 Battle Charm(공격 +2)을 패시브 '가진 동안 능력치 보정'으로 든다. 와일드프로스트의 참·종은 **붙이는 순간 한 번 바꾸고 다시 계산하지 않는다**(Snowball → Durian 순서와 반대 순서의 결과가 다르고, Sun Bell of Health 뒤 Sunglass Charm이면 +6이 사라진다). 보정으로 만들면 결과가 달라지고, "붙일 수 있나" 조건도 둘 곳이 없다. "참 = 덱 카드 필드를 바꾸는 능력(사용 조건 = 붙일 수 있나)"을 1절 표의 "참 → 카드의 참 목록" 옆에 적어 두면 헷갈리지 않는다.
6. **기믹 목록 2-1의 Fury → 조건 '자리 검사'**는 트리거 조건이다. Fury는 늘 붙는 공격력이라 '가진 동안 능력치 보정'에 조건을 걸 수 없어 능력치 함수로 가야 한다(37장 5-4에는 적혀 있다). 2-1에 "늘 붙는 값이면 계산 함수"를 덧붙이면 맞는다.
7. **명세서 4-2·기믹 목록 3절에 와일드프로스트에 필요한 자리 다섯이 없다**: 반응 대기열 · 효과 수치 배율 · 침묵 · 처치 기여(가장 최근에 건 쪽) · 카운트다운을 겪은 것만 턴 끝 감소. 이것들은 나중에 나오면 메인 룰을 고쳐야 하는 종류다.
8. **중첩이 이벤트마다 1씩 주는 상태 효과**(Shroom · Snow · Ink · Haze)는 '지속'(이벤트 횟수 — 다 되면 중첩째 끝남)이 아니라 트리거 + `상태 효과 풀기` 1이다. 기믹 목록 2-10 "Shroom → DB › 상태 효과(중첩·지속·주기·결과)"만 보고는 지속으로 만들기 쉽다. 29장 예제에 "중첩이 곧 남은 턴" 모양 하나가 있으면 좋다.
9. 작은 것: 기본 대상 Block에 **상태 효과로 거르기**가 없다("Snow 없는 적 중 무작위", "Ink 아닌 아군") — `조건으로 거르기`는 필드만 본다(Wild는 받는 쪽 패시브의 조건으로 돌렸다). 값 틀이 **이벤트 필드**를 읽지 못한다(Flourish의 회복량). `플레이어 데이터에 주기`의 숫자는 증감만이라 "값으로 정하기"는 참거짓 필드로 돌려야 한다(Charge Bell).
10. 이름: '참'(Charm)이 값 `참`(true)과 같은 글자라 DB 칸 설명("`redrawCharged` = 참", "이벤트 조건 `sacrificed` 참")과 섞이면 읽기 어렵다. 기믹 목록·참조가 이미 '참'을 쓰므로 이 문서도 따랐다.

---

## 6. 숫자

| 분류 | 개수 |
|---|---|
| DB | 37 |
| Block | 39 |
| BindTo | 17 |
| 메인 룰 | 42 |
| 화면 | 0 |
| 예외 | 5 |
| 문법 밖 | 0 |
| 합계 | 140 |

- 버프·디버프 14개: DB 5(Block · Spice · Bom · Demonize · Frost) · Block 3 · BindTo 3 · 메인 룰 1(Snow) · 예외 2(Lumin · Ink).
- 키워드 31개(대상 3 · 피해 6 · 제한 5 · 기타 10 · 적 전용 7): DB 3 · Block 10 · BindTo 4 · 메인 룰 13 · 예외 1(Critical). 키워드가 메인 룰로 많이 가는 것은 참·종이 키워드를 실행 중에 붙이므로 패시브 대신 카드의 정수 필드 + 메인 룰로 두었기 때문이다.
- 종 37개(Sun 11 · Storm 고르기 1 · Storm 13 · Modifier 12): DB 17 · Block 12 · BindTo 4 · 메인 룰 3 · 예외 1.
- 덱에 새 카드를 넣는 조각(Pet · 지도 이벤트 · Gunk Bell · 일일 원정)이 Block으로 간 것은 5절 4 때문이다.

---

## 보강 뒤(2026-10-05)

예외 5는 모두 없어졌다.

- 효과 수치 배율(Lumin·Critical·Lumin Bell·Boosted Leader) → 기본 행동 직전 이벤트 **'효과 배율'**(`PowerIntent`). 능력을 쓸 때·패시브가 발동할 때 한 번 거치고, 기본 효과 Block('상태 효과 걸기'·'필드 바꾸기'·'능력치 보정')이 곱한다. 프로젝트 Block은 `Use.Context.Power`.
- 침묵(Ink) → 상태 효과 `silencesPassives`. 능력까지 막으려면 행동 직전 이벤트 '사용' 받기 막기.
- 턴 순서(열 우선) → 보드 목록을 열 우선으로 담는다(줄 길이 2). 원작의 '같은 줄' = 여기의 '같은 열': 방향 '같은 열 맨 앞·맨 뒤', 자리 '같은 열 맨 앞'·'같은 열에 혼자'(Fury).
- 키워드 여럿 → `matchField`가 글자 목록. 처치 기여 → 걸린 것의 `by`(다시 걸면 바뀜). 대상 '상태 효과로 거르기'. 값 틀 `{event:경로}`. Fury 같은 조건부 보정 → `modifierWhen`.
- 문서: 참(Charm)은 붙이는 순간 한 번 바꾸는 능력(가진 동안 보정 아님), Shroom·Snow는 트리거 + 풀기 1, 덱에 넣기는 고른 뒤 결과의 프로젝트 Block, 반응 대기열은 메인 룰(기믹 목록 3절·명세서 4-2에 표시 칸).
