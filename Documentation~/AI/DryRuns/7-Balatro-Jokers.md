# 콘텐츠 시험 7 — 발라트로 조커 150개

발라트로(Balatro)의 조커 150개(시트 `발라트로_조커_툴팁.tsv`: 번호·조커·효과·가격·희귀도·해금 조건·유형·발동 시점)를 GBible로 옮긴다고 보고, 메인 룰(점수 파이프라인)을 먼저 세운 뒤 조커마다 효과와 해금 조건을 어떻게 만드는지 분류한 기록이다. 코드는 쓰지 않았고 문서·라이브러리는 고치지 않았다.

- 읽은 문서: `AI/ProjectConventions.md` · `AI/Reference.md` · `AI/GimmickCatalog.md` · `AI/PlanningSpecGuide.md`(2·7절) · `AI/PlanningSpec.md`(2-2 표), 매뉴얼 10 · 11 · 12 · 15 · 16 · 17 · 28 · 29 · 30 · 34 · 37장.
- 문서로 판단이 안 돼 코드를 본 곳: `Runtime/Core/Passives.cs`(빌려 쓰기·레벨 필드·놓인 것마다), `Triggers.cs`(같은 자리 패시브의 순서), `Intercepts.cs`·`Chance.cs`·`Abilities.cs`(기본 의도의 필드), `TargetBlocks.cs`, `AttributeBlocks.cs`(필드 바꾸기의 빌려 쓰기 거르기), `Runtime/Unity/PassiveData.cs`(검증)·`GameDatabase.cs`(등록 순서), `Editor/Data/EditorDatabase.cs`(ID 찾기). 5절에 "코드 확인"으로 적었다.
- 분류: **DB**(기본 Block·DB 칸 + 메인 룰이 둔 이벤트·의도·데이터만) · **Block**(프로젝트가 만든 재사용 Block을 씀) · **BindTo**(그 조커만의 코드) · **메인 룰** · **화면** · **예외** · **문법 밖**. 섞이면 가장 깊은 층 하나로 셌다(DB < Block < BindTo < 메인 룰). Block은 다시 **Block(시)** = 시스템층에서 메인 룰과 함께 만드는 Block(동사·`이벤트의 조각이 나`)만 씀, **Block(추)** = 콘텐츠 단계에서 Block을 더 만들어야 함으로 나눴다.

---

## 1. 한 줄 결론

조커 150개 중 **문법 밖 0 · 예외 0**이다(메인 룰 쪽에서 GBible 규칙과 부딪혀 우회한 곳은 6군데, 4절). 가장 큰 빈틈은 **패시브 하나에 발동·결과가 한 쌍뿐**이라는 것이다. "다른 순간에 쌓고(버림·행성 사용·라운드 끝) 자기 차례에 내는" 조커 **32개(21%)**가 문서에 없는 프로젝트 Block(`순간별 결과`)이나 조커마다 `[BindTo]`를 요구한다. 그다음은 **'가진 쪽 필드'를 쓴 패시브를 Blueprint가 빌리지 못한다**는 것이다. 그래서 조커마다 차례를 주는 메인 룰이 기본 칸 대신 프로젝트 조건 Block을 써야 한다.

---

## 2. 메인 룰을 GBible로 세우기

### 2-1. 조각 · 자리 · 순서(기믹 목록 1절)

| | 발라트로 | GBible |
|---|---|---|
| 조각 | 조커, 카드 | `Joker`·`PlayingCard` 객체(고유 `id` + 원본 ID 필드) |
| 자리 | 조커 칸(한 줄, 5칸 + 네거티브), 손패(한 줄) | `run.jokers`·`run.hand` = `List<T>`(줄 길이 0, 빼면 당긴다) |
| 조커의 효과 | 칸 순서대로 | DB › 패시브. 가진 목록 `run › jokers`, `matchField`(일치 필드) `joker`, `perElement`(놓인 것마다) 켬 — 같은 이벤트를 받는 조커끼리 칸 왼쪽부터 섞여 돈다 |
| 순서 | 보스 → On Played → 점수 카드(카드마다 On Scored 조커, 재발동) → 손에 든 카드 → 조커 왼쪽부터(에디션 foil·holo → 독립 효과 → 다른 조커에 반응(Baseball) → polychrome) | 행동 `PlayHand`가 단계마다 이벤트를 쏜다(2-3). **조커 단계만은 조커마다 이벤트 두 번**을 쏘고 그 사이에 에디션을 코드로 넣는다(한 이벤트로는 조커 사이에 메인 룰 코드를 끼울 수 없다) |

### 2-2. 데이터 모양

```csharp
[Serializable] public class Joker        // 조커 칸의 조각
{ public string id, card, joker;         // card = 원본(JokerCard 인스턴스 ID), joker = 패시브 ID(일치 필드, 디버프 중이면 "")
  public string edition; public int sellValue; public bool eternal, perishable, rental;
  public double chips, mult, xmult = 1; public int counter;           // 쌓이는 값(조커마다 쓰는 칸만 쓴다)
  public int targetRank; public string targetSuit, targetHand; }      // 라운드마다 바뀌는 목표(Idol·Ancient·Castle·Mail-In·To Do)
[Serializable] public class JokerCard    // DB › 인스턴스, CSV 150줄
{ [DataRef(typeof(PassiveData))] public string passive; public int price; [Keyword(typeof(Rarity))] public string rarity;
  public double startChips, startMult, startXmult = 1; public int startCounter; }
[Serializable] public class PlayingCard { public string id, suit, enhancement, seal, edition; public int rank, bonusChips; public bool playedThisRound;
  public bool IsDark => suit == "spade" || suit == "club" || enhancement == "wild"; }   // 계산 속성(조건이 읽는다)
[Serializable] public class RunStats     // id "stats" — 패시브 '가진 동안 능력치 보정'의 받는 쪽(modifierTarget "stats")
{ public string id = "stats"; public double handSize = 8, hands = 4, discards = 3, jokerSlots = 5, consumableSlots = 2, interestPer5 = 1, interestCap = 5;
  public double fourFingers, shortcut, smeared, pareidolia, splash, showman; }    // 판정 스위치(0 = 꺼짐)
// RunState: money, ante, round, handsLeft, discardsLeft, deck, drawPile, hand, jokers, consumables, tags,
//   handLevels·handPlays·handPlaysRound(Dictionary<string,int>), score { chips, mult }, roundScore,
//   blind { isBoss, chipsNeeded, disabled }, stats, flags(grosExtinct), tarotsUsed, planetsUsed, blindsSkipped, startDeckSize
```

- 패시브 ID와 원본 인스턴스 ID는 같을 수 없어(DB ID가 종류를 넘어 하나뿐) 조각에 `card`·`joker` 두 필드를 둔다(4절 E4).
- 메인 룰은 `Game.Attribute(stats, …)`로 손패 수·슬롯·이자·판정 스위치를 읽는다. 조커의 "+1 손패"·"네거티브 +1 칸"·"판정 스위치"가 모두 보정으로 붙는다.

### 2-3. 행동 · 이벤트 · 의도

`PlayHand { cards }`(한 손 = 행동 하나):

1. 족보 판정 `Rules.EvaluateHand` — 판정 스위치(stats의 fourFingers·shortcut·smeared·splash, `Rules.IsFace`의 pareidolia, `Rules.HasSuit`의 smeared·와일드).
2. 보스 효과 `Rules.BossAbility` — 의도 `보스 능력`(`BossAbilityIntent`), 일어나면 `BossAbilityTriggered`.
3. `HandPlayed`(On Played). 필드 `hand`(HandInfo): handType · contains(포함 족보 목록) · cards · scoringCards · scoringFaceIds · playedCount · faceScored · isFirstHand · isLastHand · isMostPlayed · timesPlayed · playedBeforeThisRound. 키가 바뀌는 값(`handPlays[이 족보]`)은 값 조건·값 틀로 읽지 못하므로 메인 룰이 이벤트에 실어 준다.
4. 족보 레벨의 칩·배수로 `run.score`.
5. 점수 카드 왼쪽부터: 의도 `재발동`(`RetriggerIntent { cardId, phase, index, rank, isFace, times }`, 빨간 봉인이면 times 2) 횟수만큼 → 카드 칩 → 강화·봉인·에디션(코드, 유리·행운은 `Game.Chance`) → `CardScored`(card · rank · chipValue · isFace · isFirstFace · isHeart/isDiamond/isSpade/isClub · enhancement).
6. 손에 든 카드 왼쪽부터: `재발동`(phase Held) 횟수만큼 → 강철 ×1.5 → `HeldCardScored`(rank · chipValue · isFace · isLowest).
7. 조커 칸을 칸 번호로 돌며(그 순간의 칸을 읽는다): foil·holo(코드) → `JokerActivated { jokerId, hand }` → `JokerResolved { jokerId, rarity }` → polychrome(코드).
8. 합계 → `HandScored`(after). 손이 떨어지면 의도 `패배`(`DefeatIntent { ratio, cancelled }`).

그 밖: `SelectBlind`→`BlindSelected` · 첫 드로→`RoundStarted` · `DiscardCards`→`Discarded`(count·faceCount·handType·isRoyal·isFirstDiscard) + 카드마다 `CardDiscarded` · `RoundEnded`(isBoss·won·handsUsed·discardsUsed·lastHandType) · 정산→`CashedOut` · `ShopEntered`·`ShopRerolled`·`ShopLeft`·`PackOpened`·`PackSkipped`·`BlindSkipped` · `SellCard`→`CardSold { itemId, kind }`(**조각을 빼기 전에** 쏜다 — "팔면 발동"이 자기를 본다) · `UseConsumable`→`ConsumableUsed` · `CardAdded`·`CardDestroyed`·`LuckyTriggered`·`JokerAdded { count }` · `RunWon`·`RunLost`. 의도는 위 넷(`재발동`·`보스 능력`·`패배`·`상점 가격 { itemKind, itemId, price, cancelled }`)과 기본 `비용 지불`·`확률`.

### 2-4. 시스템층 Block(메인 룰과 함께 만든다)

| 이름 | 하는 일 | 왜 기본 Block이 아닌가 |
|---|---|---|
| V1 효과 `점수 주기` | 대상(점수를 내는 조커·카드)에서 칩 + · 배수 + · 배수 × . 값 = 숫자·틀, 또는 **대상의 필드**·**이벤트 필드** × 곱. `Rules.Score` → 연출 이벤트 `ScoreAdded` | 기본 `플레이어 데이터에 주기 run › score.mult`(37장 5-1)로도 되지만 결과가 이벤트를 쏘지 않아 "조커마다 +4가 튀는" 연출을 못 하고, 쌓인 소수 배수(X1.1)를 낼 값 틀이 없다(`{level}`은 정수) |
| V2 효과 `돈 주기` | V1과 같은 값 출처 | 같은 이유(연출) |
| V3 효과 `조커 없애기` | 대상 조커를 뺀다. Eternal은 건너뛰고, 빌려 쓰는 중이면 원본 자신은 없애지 않는다(필드 바꾸기와 같은 규칙) | 목록에서 요소를 빼는 기본 Block이 없다 |
| V4 효과 `조커 만들기·복사` | 가중치 표에서 뽑아 새 `Joker`(`Game.NewId`, 원본의 시작 값), 자리 있을 때만, 에디션 떼기 | '가중치 표에서 주기'는 새 id를 가진 조각을 만들지 못한다(30장 6절의 덱 카드와 같다) |
| V5 효과 `소모품 만들기·복사` | 타로·행성·스펙트럴 표, 자리 있을 때만, 네거티브 | 같은 이유 + 자리 = 능력치와 개수 비교 |
| V6 효과 `족보 레벨 올리기` | 이벤트의 handType 레벨 +1 | 딕셔너리 키가 이벤트 값 |
| V7 효과 `카드 넣기·복사·없애기·강화 바꾸기` | 덱 카드 동사(`CardAdded`·`CardDestroyed`를 쏜다) | 카드 객체 생성·제거 |
| C4 조건 `이벤트의 조각이 나` | 이벤트 필드(`jokerId`·`itemId`)가 사용자 조각의 id인가. 칸 `빌려 쓰는 중이면 참` | '가진 쪽 필드'로 거르면 Blueprint가 빌리지 못한다(4절 E1) |

V3~V5는 효과 Block이라 대상을 `사용자 자신 → 조건으로 거르기(counter 이하 0)`로 두면 "0이 되면 없앤다" 같은 조건부 결과가 기본 Block으로 된다(아래 표에서 "대상 조건"이라 적음).

**조커 쪽 공통 모양**

- 독립 효과(Indep) = 발동 `JokerActivated 받기` AND `이벤트의 조각이 나`(jokerId, 빌려 쓰는 중이면 참). 아래 표에서 **차례**라 줄여 적는다. 이벤트 조건 `hand.contains 포함 pair`처럼 거른다.
- On Scored·On Held = `CardScored`·`HeldCardScored` 받기(카드마다 조커 칸 순서로). 재발동 = 가로채기 `재발동` → 의도 고치기 `times` 더하기.
- 에디션 Negative = 패시브 `negative`(일치 필드 `edition`, 보정 jokerSlots +1 → stats). foil·holo·polychrome은 7단계 코드.
- 디버프(Perishable·보스) = 메인 룰이 `joker`를 비운다(4절 E5).
- 쌓이는 값 = 효과 `필드 바꾸기`(대상 `사용자 자신`). 빌려 쓰는 중에는 원본의 필드가 바뀌지 않는다.

### 2-5. 콘텐츠 단계에 더한 Block

| 이름 | 하는 일 | 쓰는 조커 |
|---|---|---|
| C1 효과 `개수만큼` | 점수·돈 = 단위 × (플레이어 데이터 숫자 · 목록 개수(항목 조건) · 목록 필드 합(나를 뺌)) ÷ 나눌 수 + 기준, 0 아래 없음 | 14 |
| C2 조건 `이벤트 값 = 조각 필드` | 이벤트 필드와 사용자 조각의 필드가 같은가(값 조건의 비교 값은 상수만 받는다) | 5 |
| C3 효과 `무작위로 다시 정하기` | 후보(값 목록·플레이어 데이터 목록의 필드)에서 하나를 골라 사용자 조각 필드에(지금 값 빼기, 빌려 쓰는 중이면 안 함) | 5 |
| C5 결과 `순간별 결과` | 칸: 발동(트리거 하나) + 결과 목록. 지금 순간이 그 발동에 맞을 때만 결과를 낸다(`session.Triggers.Test` → `RunOutcomes`, 같은 컨텍스트). 패시브의 발동은 모든 순간의 OR, 결과에 C5를 순간마다 하나 | 32 |

C5가 없으면 그 32개는 37장 5-2(버스)처럼 조커마다 `[BindTo]`(이벤트 형식으로 나누기)다.

### 2-6. 명세서 4-2에 표시할 것

놓인 순서·위치가 효과를 바꾼다 · 다른 것의 능력을 빌려 쓴다 · 효과가 반복 발동한다(재발동 의도) · 판정 규칙이 바뀐다 · 자원을 다른 곳에서 낸다(빚 — 돈은 모두 `Game.Pay`) · 발동 중에 목록이 바뀐다(Dagger·Madness). 기믹 목록 3절 표에 줄이 없는 셋: **조각 한 장의 차례 안에 메인 룰 단계가 끼어든다**(에디션·Baseball) · **조각 하나를 잠시 끈다**(디버프) · **뽑기 풀 규칙이 바뀐다**(Showman).

해금(조건이 있는 40개): DB › 도전과제 `unlock.<조커>`(발동만) + 희귀도 가중치 표 줄의 조건 `도전과제 달성`(30장 5-10). 누적 수는 로그(범위 누적). 전설 5개는 스펙트럴 Soul의 V4만 쓰는 표에 둔다.

---

## 3. 분류 표

### 3-1. 효과

| 조각 | 분류 | 만드는 법 |
|---|---|---|
| 1 Joker | Block(시) | 차례 → V1 배수 +4 |
| 2~5 Greedy · Lusty · Wrathful · Gluttonous | Block(시) | `CardScored`(isDiamond / isHeart / isSpade / isClub 참) → V1 배수 +3 |
| 6~10 Jolly · Zany · Mad · Crazy · Droll | Block(시) | 차례, `hand.contains` 포함 pair / threeOfAKind / twoPair / straight / flush → V1 배수 +8 / 12 / 10 / 12 / 10 |
| 11~15 Sly · Wily · Clever · Devious · Crafty | Block(시) | 같은 모양 → V1 칩 +50 / 100 / 80 / 100 / 80 |
| 16 Half Joker | Block(시) | 차례, `hand.playedCount` 이하 3 → V1 배수 +20 |
| 17 Joker Stencil | BindTo | 차례 → ×(`Attribute(stats.jokerSlots)` − 조커 수 + 스텐실 수). 능력치와 개수를 섞는 계산 |
| 18 Four Fingers · 37 Pareidolia · 52 Splash · 69 Shortcut · 113 Smeared | 메인 룰 | 판정 스위치(4-2 '판정 규칙이 바뀐다'). 패시브는 보정만(stats.fourFingers +1 …), 판정 함수가 `Game.Attribute`로 본다. 트리거·보정이 없는 패시브는 검증 오류라 `Game.Owned`만 보는 모양은 못 쓴다(4절 E2) |
| 121 Showman | 메인 룰 | 상점·팩 뽑기 함수가 stats.showman을 보고 뺄 목록이 있는 표/없는 표를 고른다(기믹 목록 2-6, 희귀도마다 표 둘). 4-2 표에 맞는 줄 없음 |
| 19 Mime | DB | 가로채기 `재발동`(phase Held) → times +1 |
| 36 Hack · 109 Sock and Buskin · 115 Hanging Chad | DB | 가로채기 `재발동`(Scored; rank 2~5 / isFace / index 0) → times +1 / +1 / +2 |
| 28 Dusk | DB | 가로채기 `재발동`(Scored) AND 플레이어 데이터 검사 handsLeft 일치 0 → times +1 |
| 20 Credit Card | DB | 가로채기 `비용 지불`(key run, path money) → floor 값으로 바꾸기 −20 |
| 21 Ceremonial Dagger | BindTo | `BlindSelected`: 오른쪽 조커(`Placement.Around`)의 sellValue × 2를 자기 mult에 더하고 V3 함수로 없앤다(빌려 쓰는 중이면 쌓지 않음). 차례 → V1 배수 + mult. 남의 필드를 읽어 내 필드에 |
| 22 Banner | Block(추) | 차례 → C1 칩 30 × discardsLeft |
| 23 Mystic Summit | Block(시) | 차례 AND 플레이어 데이터 검사 discardsLeft 일치 0 → V1 배수 +15 |
| 24 Marble Joker | Block(시) | `BlindSelected` → V7 카드 넣기(돌 카드, 덱) |
| 25 Loyalty Card | Block(추) | C5: `HandScored` → counter +1, 대상 조건 counter 이상 6이면 값으로 바꾸기 0 / 차례 → 대상 조건 counter 일치 5 → V1 ×4 |
| 26 8 Ball | Block(시) | `CardScored`(rank 8) AND 확률 25 → V5 타로 |
| 27 Misprint | BindTo | 차례 → `Game.Random("misprint").Range(0, 24)`만큼 배수. 값 틀에 무작위가 없다 |
| 29 Raised Fist | Block(시) | `HeldCardScored`(isLowest) → V1 배수 + 이벤트 필드 chipValue × 2 |
| 30 Chaos the Clown | DB | `ShopEntered` → 사용자 자신에게 상태 효과 `freeReroll`(지속 = `ShopLeft` 1번). 그 상태 효과의 가로채기 `상점 가격`(itemKind reroll) → 막기 + 상태 효과 풀기 |
| 31 Fibonacci | Block(시) | `CardScored`(rank 14·2·3·5·8, OR) → V1 배수 +8 |
| 32 Steel Joker | Block(추) | 차례 → C1 ×(1 + 0.2 × deck 중 강철 수) |
| 33 Scary Face · 104 Smiley Face | Block(시) | `CardScored`(isFace) → V1 칩 +30 / 배수 +5 |
| 34 Abstract Joker | Block(추) | 차례 → C1 배수 3 × 조커 수 |
| 35 Delayed Gratification | Block(추) | `CashedOut` AND 이벤트 discardsUsed 0 → C1 돈 2 × discardsLeft |
| 38 Gros Michel | Block(추) | C5: 차례 → V1 배수 +15 / `RoundEnded` AND 확률 16.67 → V3 자신 + 플레이어 데이터에 주기 flags.grosExtinct 참(Gros·Cavendish 표 줄 조건이 이것을 본다) |
| 39 Even Steven · 40 Odd Todd | Block(시) | `CardScored`(짝수 / 홀수 rank, OR) → V1 배수 +4 / 칩 +31 |
| 41 Scholar · 101 Walkie Talkie | Block(시) | `CardScored`(rank 14 / 10·4) → V1 칩 +20·배수 +4 / 칩 +10·배수 +4 |
| 42 Business Card | Block(시) | `CardScored`(isFace) AND 확률 50 → V2 $2 |
| 43 Supernova | Block(시) | 차례 → V1 배수 + 이벤트 필드 hand.timesPlayed |
| 44 Ride the Bus | Block(추) | C5: `HandPlayed`(faceScored 거짓) → mult +1 / (참) → mult 값으로 0 / 차례 → V1 배수 + mult |
| 45 Space Joker | Block(시) | `HandPlayed` AND 확률 25 → V6 |
| 46 Egg | DB | `RoundEnded` → 사용자 자신 필드 바꾸기 sellValue +3 |
| 47 Burglar | DB | `BlindSelected` → 플레이어 데이터에 주기 handsLeft +3, 대상 `플레이어 데이터 목록 run` → 필드 바꾸기 discardsLeft 값으로 0(플레이어 데이터에 주기는 숫자를 증감만 한다) |
| 48 Blackboard | Block(시) | 차례 AND 플레이어 데이터 검사(hand 항목 검사 all: IsDark 참) → V1 ×3 |
| 49 Runner · 65 Square Joker · 98 Spare Trousers | Block(추) | C5: `HandPlayed`(contains straight / playedCount 4 / contains twoPair) → chips +15 / chips +4 / mult +2 ; 차례 → V1 + 그 필드 |
| 50 Ice Cream · 97 Popcorn | Block(추) | C5: 차례 → V1 칩 + chips(시작 100) / 배수 + mult(시작 20) ; `HandScored` / `RoundEnded` → −5 / −4, 대상 조건 0 이하면 V3 자신 |
| 51 DNA | Block(시) | `HandPlayed`(isFirstHand, playedCount 1) → 대상 이벤트의 개체 hand.cards → V7 카드 복사(덱·손패) |
| 53 Blue Joker | Block(추) | 차례 → C1 칩 2 × drawPile 수 |
| 54 Sixth Sense | Block(시) | `HandScored`(isFirstHand, playedCount 1, cards 항목 rank 6) → V7 없애기 + V5 스펙트럴 |
| 55 Constellation · 70 Hologram · 91 Lucky Cat · 120 Glass Joker · 146 Canio | Block(추) | C5: `ConsumableUsed`(planet) / `CardAdded` / `LuckyTriggered` / `CardDestroyed`(glass) / `CardDestroyed`(isFace) → xmult +0.1 / +0.25 / +0.25 / +0.75 / +1 ; 차례 → V1 × xmult |
| 56 Hiker | DB | `CardScored` → 대상 이벤트의 개체 cardId → 필드 바꾸기 bonusChips +5 |
| 57 Faceless Joker | Block(시) | `Discarded`(faceCount 이상 3) → V2 $5 |
| 58 Green Joker | Block(추) | C5: `HandPlayed` → mult +1 / `Discarded` → mult −1(0 아래로 안 내림) / 차례 → V1 배수 + mult |
| 59 Superposition | Block(시) | 차례, contains straight AND scoringCards 항목 검사 any rank 14 → V5 타로 |
| 60 To Do List | Block(추) | C5: `HandPlayed` AND C2(hand.handType = targetHand) → V2 $4 / `RoundEnded` → C3 targetHand(족보 목록, 지금 값 빼고) |
| 61 Cavendish | Block(추) | C5: 차례 → V1 ×3 / `RoundEnded` AND 확률 0.1 → V3 자신 |
| 62 Card Sharp | Block(시) | 차례, hand.playedBeforeThisRound 참 → V1 ×3 |
| 63 Red Card · 96 Flash Card | Block(추) | C5: `PackSkipped` / `ShopRerolled` → mult +3 / +2 ; 차례 → V1 배수 + mult |
| 64 Madness | Block(추) | C5: `BlindSelected`(isBoss 거짓) → xmult +0.5 + V3(대상 자리로 고르기 '나를 뺀 모두' → 조건으로 거르기 eternal 거짓 → 무작위 1) / 차례 → V1 × xmult |
| 66 Séance | Block(시) | 차례, handType 일치 straightFlush → V5 스펙트럴 |
| 67 Riff-Raff | Block(시) | `BlindSelected` → V4(일반 표, 2개, 자리 있을 때) |
| 68 Vampire | BindTo | `HandPlayed`: 강화된 점수 카드 수 × 0.1을 xmult에 더하고 그 카드들의 강화를 뗀다(V7 함수). 차례 → V1 × xmult. 개수로 정해지는 쌓기와 카드 바꾸기가 한 순간에 |
| 71 Vagabond | Block(시) | 차례 AND 플레이어 데이터 검사 money 이하 4 → V5 타로 |
| 72 Baron · 140 Shoot the Moon | Block(시) | `HeldCardScored`(rank 13 / 12) → V1 ×1.5 / 배수 +13 |
| 73 Cloud 9 | Block(추) | `CashedOut` → C1 돈 1 × deck 중 rank 9 수 |
| 74 Rocket | Block(추) | C5: `CashedOut` → V2 $ 필드 counter(시작 1) / `RoundEnded`(isBoss) → counter +2 |
| 75 Obelisk | Block(추) | C5: `HandPlayed`(isMostPlayed 거짓 → xmult +0.2 / 참 → xmult 값으로 1) / 차례 → V1 × xmult |
| 76 Midas Mask | Block(시) | `HandPlayed` → 대상 이벤트의 개체 hand.scoringFaceIds → V7 강화 바꾸기 gold |
| 77 Luchador | DB | `CardSold`, 가진 쪽 필드 itemId(팔린 그 조커일 때만 — 빌려 쓰기로 복사되지 않는다) → 플레이어 데이터에 주기 blind.disabled 참 |
| 78 Photograph | Block(시) | `CardScored`(isFirstFace) → V1 ×2 |
| 79 Gift Card | DB | `RoundEnded` → 대상 플레이어 데이터 목록 run › jokers, run › consumables → 필드 바꾸기 sellValue +1 |
| 80 Turtle Bean | Block(시) | 보정 handSize +1 레벨마다(레벨 필드 counter, 시작 5, 보정 받을 대상 stats). `RoundEnded` → counter −1, 대상 조건 0 이하면 V3 자신 |
| 81 Erosion | Block(추) | 차례 → C1 배수 4 × (startDeckSize − deck 수) |
| 82 Reserved Parking | Block(시) | `HeldCardScored`(isFace) AND 확률 50 → V2 $1 |
| 83 Mail-In Rebate | Block(추) | C5: `CardDiscarded` AND C2(rank = targetRank) → V2 $5 / `RoundEnded` → C3 targetRank(덱 카드의 rank) |
| 84 To the Moon | DB | 보정 stats.interestPer5 +1. 메인 룰의 이자 계산이 이 능력치를 읽는다 |
| 85 Hallucination | Block(시) | `PackOpened` AND 확률 50 → V5 타로 |
| 86 Fortune Teller | Block(추) | 차례 → C1 배수 1 × tarotsUsed |
| 87 Juggler · 88 Drunkard · 111 Troubadour · 125 Merry Andy | DB | 보정만: handSize +1 / discards +1 / handSize +2·hands −1 / discards +3·handSize −1(보정 받을 대상 stats, 트리거 없음) |
| 89 Stone Joker | Block(추) | 차례 → C1 칩 25 × deck 중 돌 수 |
| 90 Golden Joker | Block(시) | `CashedOut` → V2 $4 |
| 92 Baseball Card | Block(시) | `JokerResolved`(rarity uncommon) → V1 ×1.5(그 조커의 독립 효과 바로 뒤, polychrome 앞) |
| 93 Bull · 145 Bootstraps | Block(추) | 차례 → C1 칩 2 × money / 배수 2 × ⌊money ÷ 5⌋(0 아래 없음) |
| 94 Diet Cola | DB | `CardSold`(가진 쪽 필드 itemId) → 플레이어 데이터에 주기 tags 요소 넣기 double |
| 95 Trading Card | Block(시) | `Discarded`(isFirstDiscard, count 1) → 대상 이벤트의 개체 cards → V7 없애기 + V2 $3 |
| 99 Ancient Joker · 127 The Idol | Block(추) | C5: `CardScored` AND C2(suit = targetSuit / rank·suit = targetRank·targetSuit) → V1 ×1.5 / ×2 ; `RoundEnded` → C3(무늬 목록 / 덱 카드) |
| 100 Ramen | Block(추) | C5: 차례 → V1 × xmult(시작 2) / `CardDiscarded` → xmult −0.01, 대상 조건 1 이하면 V3 자신 |
| 102 Seltzer | Block(추) | C5: 가로채기 `재발동`(Scored) → times +1 / `HandScored` → counter +1, 대상 조건 이상 10이면 V3 자신 |
| 103 Castle | Block(추) | C5: `CardDiscarded` AND C2(suit = targetSuit) → chips +3 / `RoundEnded` → C3 targetSuit(덱 카드) / 차례 → V1 칩 + chips |
| 105 Campfire | Block(추) | C5: `CardSold` → xmult +0.25 / `RoundEnded`(isBoss) → xmult 값으로 1 / 차례 → V1 × xmult |
| 106 Golden Ticket · 116 Rough Gem | Block(시) | `CardScored`(enhancement gold / isDiamond) → V2 $4 / $1 |
| 107 Mr. Bones | Block(시) | 가로채기 `패배`(ratio 이상 0.25) → 막기 + V3 자신 |
| 108 Acrobat | Block(시) | 차례, hand.isLastHand 참 → V1 ×3 |
| 110 Swashbuckler | Block(추) | 차례 → C1 배수 1 × 다른 조커 sellValue 합 |
| 112 Certificate | Block(시) | `RoundStarted` → V7 카드 넣기(무작위 카드·무작위 봉인, 손패로) |
| 114 Throwback | Block(추) | 차례 → C1 ×(1 + 0.25 × blindsSkipped) |
| 117 Bloodstone · 118 Arrowhead · 119 Onyx Agate | Block(시) | `CardScored`(isHeart AND 확률 50 / isSpade / isClub) → V1 ×1.5 / 칩 +50 / 배수 +7 |
| 122 Flower Pot · 128 Seeing Double | BindTo | 차례: 점수 카드에 네 무늬가 다 있나 / 클럽과 다른 무늬가 함께 있나. 와일드를 어느 무늬로 셀지 짝을 맞추는 판정(`Rules.HasSuit`) |
| 123 Blueprint · 138 Brainstorm | Block(시) | 발동 = 차례 OR `CardScored` OR `HeldCardScored` OR `JokerResolved` OR `HandPlayed` OR `HandScored` OR `Discarded` OR `CardDiscarded` OR `BlindSelected` OR `RoundEnded` OR 상점·팩 이벤트 OR 가로채기 `재발동`. 결과 `빌려 쓰기`(자리로 고르기 오른쪽 / 목록 맨 처음). `CashedOut`·`CardSold`는 넣지 않는다(원작에서 복사되지 않는 정산 돈·팔기 효과) |
| 124 Wee Joker | Block(추) | C5: `CardScored`(rank 2) → chips +8 / 차례 → V1 칩 + chips |
| 126 Oops! All 6s | DB | 가로채기 `확률` → probability 곱하기 2 |
| 129 Matador | Block(시) | `BossAbilityTriggered` → V2 $8 |
| 130 Hit the Road | Block(추) | C5: `CardDiscarded`(rank 11) → xmult +0.5 / `RoundEnded` → xmult 값으로 1 / 차례 → V1 × xmult |
| 131~135 The Duo · Trio · Family · Order · Tribe | Block(시) | 차례, contains pair / threeOfAKind / fourOfAKind / straight / flush → V1 ×2 / 3 / 4 / 3 / 2 |
| 136 Stuntman | Block(시) | 차례 → V1 칩 +250, 보정 stats.handSize −2 |
| 137 Invisible Joker | Block(추) | C5: `RoundEnded` → counter +1 / `CardSold` AND C4(itemId, 빌려 쓰는 중이면 거짓) → 대상 조건 counter 이상 2 → V4 복사(무작위 다른 조커, 네거티브 떼기) |
| 139 Satellite | Block(추) | `CashedOut` → C1 돈 1 × planetsUsed 수 |
| 141 Driver's License | Block(시) | 차례 AND 플레이어 데이터 검사(deck 항목 검사 count ≥ 16: enhancement 아님 none) → V1 ×3 |
| 142 Cartomancer | Block(시) | `BlindSelected` → V5 타로 |
| 143 Astronomer | DB | 가로채기 `상점 가격`(itemKind planet·celestialPack) → 막기 |
| 144 Burnt Joker | Block(시) | `Discarded`(isFirstDiscard) → V6(이벤트 handType) |
| 147 Triboulet | Block(시) | `CardScored`(rank 13·12) → V1 ×2 |
| 148 Yorick | Block(추) | C5: `CardDiscarded` → counter +1, 대상 조건 counter 이상 23이면 xmult +1·counter 값으로 0 / 차례 → V1 × xmult |
| 149 Chicot | DB | 가로채기 `보스 능력` → 막기 |
| 150 Perkeo | Block(시) | `ShopLeft` → V5 복사(가진 소모품 무작위 1, 네거티브) |

### 3-2. 해금 조건

| 조각 | 분류 | 만드는 법 |
|---|---|---|
| 1~105(Available from start) | DB | 희귀도 표(일반·고급·희귀) 줄, 조건 없음 |
| 146~150(Soul로만) | DB | 전설 표. 스펙트럴 Soul의 V4만 이 표를 쓰고 상점 표에는 없다. 해금 없음 |
| 106 Golden Ticket | DB | `HandPlayed`(playedCount 5 AND cards 항목 검사 all: enhancement gold) |
| 107 Mr. Bones · 108 Acrobat · 109 Sock and Buskin · 110 Swashbuckler · 144 Burnt Joker | DB | 로그(누적): `RunLost` 횟수 / `HandPlayed` 횟수 / `HandPlayed` 반영할 값 faceCount 합 / `CardSold`(kind joker) 횟수 / `CardSold` 횟수 + 도전과제 `비교` ≥ 5 / 200 / 300 / 20 / 50 |
| 111 Troubadour | BindTo | "5라운드 연속 한 손 승리": 로그 집계(합계·최고치·최저치·마지막 값)로는 끊길 때 0으로 못 돌린다 → 로그 `[BindTo]`(15장 2-6)로 연속 값을 직접 |
| 112 Certificate · 113 Smeared · 116~119 Rough Gem · Bloodstone · Arrowhead · Onyx Agate · 120 Glass Joker · 141 Driver's License · 145 Bootstraps | DB | 조건 충족: 플레이어 데이터 검사 + 항목 검사(deck: gold ∧ gold 봉인 ≥ 1 / wild ≥ 3 / 그 무늬 ≥ 30 / glass ≥ 5 / 강화 ≥ 16, jokers: polychrome ≥ 2) |
| 114 Throwback | 메인 룰 | "저장된 판 이어 하기": `RunLoaded`는 C# 이벤트라 도전과제가 받지 못한다 → 타이틀의 이어 하기가 행동 `ContinueRun`(이벤트 `RunContinued`)을 보낸다 |
| 115 Hanging Chad · 129 Matador | DB | `RoundEnded`(isBoss · won · lastHandType highCard) / (isBoss · won · handsUsed 1 · discardsUsed 0) |
| 121 Showman · 122 Flower Pot · 139 Satellite | DB | 조건 충족 ante ≥ 4 / ante ≥ 8 / money ≥ 400 |
| 123 Blueprint · 124 Wee Joker · 125 Merry Andy | DB | `RunWon` / (rounds ≤ 18) / (rounds ≤ 12) |
| 126 Oops! All 6s · 127 The Idol · 136 Stuntman | DB | `HandScored`(total ≥ 1e4 / 1e6 / 1e8, double로 충분) |
| 128 Seeing Double · 130 Hit the Road · 138 Brainstorm | DB | `HandPlayed`(cards 항목 count ≥ 4: rank 7 ∧ club) / `Discarded`(rank 11 ≥ 5) / `Discarded`(isRoyal) |
| 131~135 The Duo~Tribe | DB | `RunWon` AND 플레이어 데이터 검사 `handPlays[pair]` 비어 있음 OR 이하 0. `[키]` 경로는 DB 고르기 창이 만들지 못해 DB 추가 메뉴 코드에 적는다 |
| 137 Invisible Joker | DB | 로그(이번 판, 최고치) `JokerAdded.count` → `RunWon` AND 비교 ≤ 4 |
| 140 Shoot the Moon | DB | `RoundEnded` AND deck 항목 검사 none(heart ∧ playedThisRound 거짓). 카드의 '이번 라운드에 냄' 필드를 메인 룰이 둔다는 전제 |
| 142 Cartomancer · 143 Astronomer | DB | 조건 충족 profile.discoveredTarots ≥ 22 / discoveredPlanets ≥ 12(종류별 목록 — 한 글자 목록에 섞으면 요소를 종류로 거를 수 없다) |

---

## 4. 예외 · 문법 밖

조커 단위로는 없다. 아래는 **메인 룰을 세우다 GBible의 정해진 규칙과 부딪혀 우회한 것**이다. 우회가 시스템층에 있어서 조커 표에는 Block·DB로 셌다.

| | 무엇이 안 되나 · 왜 | 우회 | 라이브러리에 있으면 없어지는 것 |
|---|---|---|---|
| E1 | 조커 차례 이벤트를 조커마다 쏘고 '가진 쪽 필드'(`jokerId`)로 거르면 **Blueprint·Brainstorm이 아무것도 빌리지 못한다**. 빌려 쓸 때 원본의 발동을 원본 조각 기준으로 다시 보는데 가진 쪽 필드도 원본 id와 비교한다(이벤트의 jokerId는 Blueprint). 또 가진 쪽 필드는 발동 전체에 걸려 다른 이벤트(버림·라운드 끝)를 함께 받는 조커에는 못 쓴다 | 시스템층 조건 `이벤트의 조각이 나`(빌려 쓰는 중이면 참). 팔기처럼 복사되면 안 되는 것만 가진 쪽 필드를 쓴다 | 기본 조건 '이벤트의 개체가 사용자'(빌려 쓰는 중 처리 옵션), 또는 빌려 쓸 때 가진 쪽 필드를 빌리는 쪽 id로 보는 옵션 |
| E2 | 판정 스위치 조커(Four Fingers 등 6개). 기믹 목록 2-6은 "판정 함수가 `Game.Owned(패시브 ID)`를 본다"인데, 발동도 보정도 없는 패시브는 DB 검증 오류(빌드가 막힘)다 | stats의 스위치 능력치 +1 보정으로 두고 판정 함수가 `Game.Attribute`를 본다 | '가지고만 있는' 패시브 허용, 또는 기믹 목록 문구를 이 우회로 |
| E3 | 순간이 둘 이상인 조커 32개(쌓기·내기·되돌리기·소멸). 패시브는 발동 하나 + 결과 하나라 순간마다 다른 결과를 못 둔다 | 결과 Block C5 `순간별 결과`(문서에 이런 Block의 예가 없다), 안 되면 조커마다 `[BindTo]` | 패시브에 '규칙 여러 줄'(로그·퀘스트의 `List<TriggerRule>`처럼 발동·결과 쌍 목록). 그러면 C5 32개가 DB가 된다 |
| E4 | 조커의 고정 값(가격·희귀도·시작 값)을 둘 곳. `PassiveData`에는 칸을 더할 수 없고(17장 0-5), 인스턴스로 두면 ID가 패시브와 같을 수 없어 원본 ID 필드 하나로 둘을 가리키지 못한다. 기획 명세서 읽는 법 2-2(`OwnedJoker { id, joker … }` + 인스턴스 + 일치 필드 = 원본 ID 필드)대로는 안 된다 | 인스턴스 `JokerCard`에 `passive` 칸, 조각에 `card`·`joker` 두 필드(원본의 고정 값 하나를 판 데이터에 복사) | 인스턴스(또는 프로젝트 DB 종류)에서 패시브를 정의하는 길(인스턴스 ID = 패시브 ID로 등록), 또는 패시브에 프로젝트 칸 |
| E5 | 조각 하나를 잠시 끄기(Perishable 5라운드 뒤, 보스의 디버프). '놓인 것마다' 패시브에 요소 단위로 끄는 칸이 없다. 패시브 150개에 조건을 다 붙일 수도 없다 | 메인 룰이 `joker`를 비워 일치를 끊고 `card.passive`로 되돌린다 | 패시브 '꺼짐 필드'(요소의 bool이 참이면 발동·보정·빌려 쓰기에서 빠짐) |
| E6 | 상점 할인·공짜(Astronomer, Chaos). 기믹 목록 2-11은 가로채기 '비용 지불'인데 `CostIntent`는 key·path·amount·floor·cancelled뿐이라 무엇을 사는지(행성·리롤)로 거를 수 없다 | 프로젝트 의도 `상점 가격 { itemKind, itemId, price }`를 상점 가격 함수에 | '비용 지불' 의도에 `what`(무엇에 내나) 글자 필드 |

값 틀의 한계도 Block을 늘렸다: 의도 고치기·필드 바꾸기의 값 틀은 `{level}`(정수)·`{stacks}`·`{balance:ID}`뿐이고 값 조건의 비교 값은 상수만 받는다. 그래서 쌓인 소수 배수·이벤트 값으로 내기(V1의 값 출처), 개수만큼(C1), 이벤트 값과 조각 필드 비교(C2)가 프로젝트 Block이 됐다. `{user:필드}`·`{event:필드}`·`{count:경로}` 같은 틀과 비교 값의 틀이 있으면 C1·C2와 V1의 값 출처가 기본 Block으로 된다(효과 쪽 Block(추) 46개 중 C1 14개가 DB로).

---

## 5. 문서 빈틈

**코드를 봐야 했던 곳**

1. 빌려 쓰기가 '가진 쪽 필드'를 누구 기준으로 보는지. 12장 2-4e는 "가진 쪽 필드도 본다"뿐 → 코드 확인: 원본(lender) id와 비교(`Passives.Borrow` → `HolderFieldMatches(definition, lender, …)`). E1의 원인이다.
2. 레벨 필드가 소수를 받는지, 레벨 0인 조각도 발동하는지 → 코드 확인: 정수로 반올림(`LevelOf`), 놓인 것마다는 레벨을 보지 않고 발동(`OrderedHoldersFor`). "쌓인 값 = 레벨 필드 + `{level}`" 꼼수는 정수 값에만 된다.
3. 한 조각에 맞는 패시브가 둘일 때의 순서(에디션을 패시브로 둘 때 foil → 조커 → polychrome을 맞추려고) → 코드 확인: 같은 자리면 등록 순서, 등록 순서 = DB 항목 순서(`GameDatabase.All`). 11장 6-4에 "같으면 등록 순서"는 있지만 DB 항목 순서를 정하는 법이 문서에 없다. 그래서 이 길을 버리고 조커마다 이벤트를 쏘는 메인 룰로 갔다.
4. DB ID가 종류를 넘어 하나뿐인지 → 17장 1절 한 줄뿐이라 코드 확인(`EditorDatabase.FindById`가 모든 종류에서 찾는다). E4의 원인이다.
5. 대상 `플레이어 데이터 목록`의 경로 끝이 목록도 id 글자도 아닌 객체(run)일 때 → 16장 2-6 표에 없고 37장 5-1 예에서만 쓴다. 코드 확인: 그 객체 하나를 낸다(`DataListTarget`).
6. 기본 의도의 필드(`CostIntent`에 무엇을 사는지가 없음) → 28장 4-14 표와 코드가 같다. 어긋난 것은 기믹 목록 2-11(E6).

**문서끼리 어긋난 곳**

7. 기믹 목록 2-6 "판정 스위치 = `Game.Owned(패시브 ID)`" ↔ 17장 0-2 검증 "발동이나 보정이 있어야 합니다"(E2).
8. 기획 명세서 읽는 법 2-2 "원본 클래스 + 인스턴스, 판 데이터는 원본 ID 필드, 패시브의 일치 필드 = 원본 ID 필드" ↔ ID가 종류를 넘어 하나뿐(E4). 기획 명세서 양식 2-2 표의 조커 예도 고정 값에 '희귀도·가격'을 적는데 둘 곳이 없다.
9. 16장 3-14는 조커를 능력(`Joker { id, ability }` + `Game.TryUse`)으로, 기믹 목록·17장·37장은 패시브 '놓인 것마다'로 만든다. 17장 0-3은 점수를 의도 `점수` 가로채기로, 37장 5-1은 이벤트 `손 점수` + 플레이어 데이터로 만든다. 어느 쪽이 기본인지 한 줄이 없다.
10. 기믹 목록 2-11은 Bull 같은 돈 비례를 `[BindTo]`로 적지만, 같은 모양이 14개라 관례 0절(재사용 Block 먼저)대로면 C1이다.

**문서에 없는 것**

11. 기믹 목록 1절 발라트로 줄은 조커 단계를 이벤트 하나로 보고, **에디션(foil·holo·polychrome)과 Baseball Card처럼 조커 한 장의 차례 안에 끼는 단계**를 다루지 않는다. 3절 표에도 줄이 없다(2-6의 셋).
12. 기믹 목록 2-7 '쌓이는 값'은 쌓는 법만 있다. 쌓는 순간과 내는 순간이 다르다는 것(E3), 쌓인 소수 배수를 점수로 내는 법이 없다. 문서의 예(37장 5-2 버스)는 `[BindTo]`다.
13. C5처럼 결과 Block 안에 발동·결과 목록을 두는 프로젝트 Block이 되는지(에디터 그리기·검증·`RunOutcomes`로 같은 컨텍스트 넘기기)가 문서에 없다. 라이브러리 결과 '가중치 표에서 고르기'의 '고른 뒤 결과'가 같은 모양이라 될 것으로 보았다.
14. "팔면 발동"(Luchador·Diet Cola·Invisible)은 조각이 목록에 있을 때만 패시브가 돈다. 팔기 행동이 이벤트를 **빼기 전에** 쏴야 한다는 것이 문서에 없다.
15. `GBibleRuntime.RunLoaded`는 참조 문서 2-1에 '이벤트'로 함께 적혀 있지만 GameEvent가 아니라 도전과제 발동이 못 받는다(Throwback 해금).
16. 해금을 도전과제로 만들면 Steam 도전과제로 모두 올라간다(34장 2-3, 항목별로 빼는 칸 없음 — Steamworks에 없으면 경고). 해금 40개가 Steam 목록과 섞인다.
17. Blueprint의 발동에 "빌릴 조커가 받는 모든 순간"을 OR로 적어야 한다(37장 5-2). 이벤트가 20개 가까이라 하나를 더할 때마다 Blueprint·Brainstorm을 고쳐야 한다. '모든 순간' 칸이 없다.
18. 가중치 표의 '뺄 목록'을 조건으로 켜고 끌 수 없어 Showman은 표가 둘씩(희귀도 4 × 2) 필요하다. 기믹 목록 2-6에 길은 있지만 표가 두 배가 된다는 말은 없다.

**이름이 헷갈린 곳**

19. GBible의 '레벨'(패시브를 몇 개 가졌나·레벨 필드, 값 틀 `{level}`)과 발라트로의 '족보 레벨'이 한 프로젝트에서 부딪힌다. 쌓인 값을 레벨 필드로 읽는 꼼수까지 쓰면 더 헷갈린다.
20. '가진 쪽 필드'(holderField)는 "이벤트의 이 필드가 가진 쪽 id일 때만"인데 이름만 보면 가진 쪽의 필드처럼 읽힌다.

---

## 6. 숫자

**효과(150)**

| 분류 | 개수 | 내역 |
|---|---|---|
| DB | 21 | 재발동 5, 보정만 5(손패·버리기·이자), 가로채기 4(빚·확률·보스·상점 가격), 상태 효과 1(Chaos), 필드 바꾸기·플레이어 데이터에 주기 6(Egg · Burglar · Hiker · Luchador · Gift Card · Diet Cola) |
| Block | 117 | Block(시) 71 — V1~V7·C4만 / Block(추) 46 — C1 14, C5 32(그중 C2·C3도 쓰는 것 5) |
| BindTo | 6 | Joker Stencil · Ceremonial Dagger · Misprint · Vampire · Flower Pot · Seeing Double |
| 메인 룰 | 6 | 판정 스위치 5(Four Fingers · Pareidolia · Splash · Shortcut · Smeared) + Showman |
| 화면 | 0 | |
| 예외 | 0 | 메인 룰 쪽 우회 6(E1~E6) |
| 문법 밖 | 0 | |

**해금 조건(150)**: 조건 없음 105 · DB 43(조건 있는 38 + Soul 전용 5) · BindTo 1(Troubadour) · 메인 룰 1(Throwback).

**Block 수**: 시스템층 8(V1 `점수 주기` · V2 `돈 주기` · V3 `조커 없애기` · V4 `조커 만들기·복사` · V5 `소모품 만들기·복사` · V6 `족보 레벨 올리기` · V7 카드 동사 · C4 `이벤트의 조각이 나`) + 콘텐츠 단계 4(C1 `개수만큼` · C2 `이벤트 값 = 조각 필드` · C3 `무작위로 다시 정하기` · C5 `순간별 결과`). 콘텐츠 단계에서 조커마다 코드가 드는 것은 `[BindTo]` 6개(해금 포함 7개)다.

---

## 보강 뒤(2026-10-05)

4절의 우회 6은 모두 없어졌다.

- E1 → 빌려 쓰기는 가진 쪽 필드를 **빌리는 쪽** 기준으로 본다(Blueprint 차례 이벤트에서 오른쪽 조커를 빌린다).
- E2 → 발동·보정·주기가 없는 **스위치 패시브**를 허용한다(`Game.Owned`로 읽는다).
- E3 → 패시브 `rules`(발동 더): 순간마다 한 줄(낼 때 +1 · 버릴 때 −1 · 점수 때 내기). `순간별 결과` Block이 필요 없다.
- E4 → 패시브 `matchVia`(원본에서 읽기): 조각은 원본 ID만 담고, 원본(인스턴스)의 패시브 ID 목록에서 찾는다. 고정 값(가격·희귀도)은 원본에.
- E5 → 상태 효과 `silencesPassives`(걸린 쪽의 패시브를 끔) 또는 레벨 필드 0(꺼짐).
- E6 → `CostIntent`의 `forId`(무엇을 위한)·`userId`(내는 쪽), `Game.Pay(…, forId:)`.
- 값 틀 → `{user:경로}`·`{event:경로}`·`{data:키.경로}`·`{count:키.경로}`, 조건 '사용자 필드 검사'(비교 값에도 틀).
