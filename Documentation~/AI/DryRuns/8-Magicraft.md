# 콘텐츠 시험 8 — 매지크래프트(Magicraft)

매지크래프트(실시간 액션 로그라이트)의 지팡이 97 · 주문 75 · 유물 85 · 세트 효과 5를 GBible로 옮긴다고 보고, 메인 룰(주문 해석기)을 먼저 세운 뒤 조각마다 어떻게 만드는지 분류한 기록이다. 코드는 쓰지 않았고 문서·라이브러리는 고치지 않았다.

- 자료: 시트 `매지크래프트_지팡이_툴팁.tsv`(97) · `매지크래프트_주문_툴팁.tsv`(75) · `매지크래프트_유물_툴팁.tsv`(85 + 세트 5).
- 읽은 문서: `AI/ProjectConventions.md` · `AI/Reference.md` · `AI/GimmickCatalog.md` · `AI/PlanningSpec.md`(4절) · `AI/PlanningSpecGuide.md`(7절), 매뉴얼 04 · 16 · 17 · 28 · 29 · 30 · 33 · 37장. 문서로 판단이 안 된 곳은 코드(`Runtime/Core/Passives.cs` · `Chance.cs` · `WeightTables.cs` · `GameApi.cs` · `PlayerDataStore.cs`, `Runtime/Unity/GBibleRuntime.cs`, `Editor/Data/EditorDatabase.cs`)를 봤다(5절에 "코드 확인"으로 적었다).
- 분류: **DB** · **Block** · **BindTo** · **메인 룰** · **화면** · **예외** · **문법 밖**(공통 지시의 정의). 조각 하나에 여러 층이 섞이면 가장 깊은 층 하나로 셌다(DB < Block < BindTo < 메인 룰). **화면**은 규칙 쪽 몫이 DB 칸(수치·참거짓)뿐이고 동작 자체가 물리·궤도·충돌·이동일 때만 쓴다. 화면 판정에 규칙 Block이 붙으면 규칙 쪽 층으로 세고 "(+화면)"을 붙였다.
- 메인 룰이 늘 읽는 기본 칸(MP·간격·쿨다운·피해·관통 같은 수치)은 **DB**로, 메인 룰에 따로 둔 자리(연계·동시 발사·횟수·다시 읽기·여러 칸·지속/충전 시전·소환·든 지팡이·시간 충전)가 처리하는 것은 **메인 룰**로 셌다.

---

## 1. 한 줄 결론

문법 밖 **0**, 예외 **3**(공통 2: **E1 원본 인스턴스와 패시브를 한 조각에 짝지을 수 없음**, **E2 묶음 판정 행동 안에서 MP가 모자란 연계** / 조각 1: **E3 Finite Gloves의 "일정 간격마다"** — 패시브에 주기가 없음). 가장 큰 빈틈은 **시전 계획을 규칙 쪽 어디에 두나**다. 연계(끝·명중 때 오른쪽 주문)와 명중 피해에는 쏜 순간의 계획이 규칙에 남아 있어야 하는데, 실시간 예산(33장 5-7)은 "개체 수백은 판 데이터에 두지 않는다"고 하고, 기믹 목록 1절은 해석기와 판정 보고 사이(화면 개체가 들고 다니는 규칙 계획)를 말하지 않는다.

---

## 2. 메인 룰 — 주문 해석기

기믹 목록 1절 매지크래프트 줄 그대로다: **규칙 함수(주문 해석기)가 시전 계획을 만들고, 화면이 쏘고, 끝남·명중을 판정 보고 행동으로 보낸다. 해석기가 연계를 시전하고, MP는 지팡이 필드에서 `Game.Pay`.**

### 2-1. 조각 · 자리 · 데이터 모양

```csharp
// 판 데이터(run)
public List<Wand> wands;                  // 가진 지팡이(객체 목록). heldWand = 든 지팡이 id
public string heldWand;
public Dictionary<string, int> relics;    // 유물 = 패시브 ID → 레벨(17장 0-1)
public Dictionary<string, int> sets;      // 세트 패시브 ID → 모은 개수(세트 계산이 쓴다)
public Hero hero;                         // id "hero". 주인공 능력치 + '모든 지팡이' 전역 칸(wandMaxMp·wandRegen·wandMpCost·radius·chargeEff·wandSlots·wandLimit·summonLimit …, 기본 0)
public List<CastTemplate> templates;      // 계획 틀(2-3)
public Room room;                         // enemies(List<Enemy{id, enemy, hp, maxHp, boss}>), summons, 상점·상자 물건

[Serializable] public class Wand  { public string id, wand, passive; public double mp, mpAt, energy, energyAt, readyAt; public int cursor, version; public List<Spell> slots; }
[Serializable] public class Spell { public string id, spell, passive; public int level = 1; public int kills; }   // 칸 조각. slots의 null = 빈 칸, 앞 n칸 = 일반 칸, 뒤 = 보조 칸(post)
```

| | 매지크래프트 | GBible |
|---|---|---|
| 조각 | 주문(칸 조각), 지팡이, 유물 | 주문·지팡이 = 객체 목록(고유 `id` + 원본 ID 칸). 유물 = 딕셔너리(ID → 레벨) |
| 자리 | 지팡이 칸 한 줄 + 보조 칸 | `Wand.slots` 하나(줄 길이 0). 보조 칸은 같은 목록 뒤쪽 — 패시브 `run › wands[].slots`가 일반·보조 칸을 함께 본다 |
| 원본 | 지팡이 수치, 주문 수치(레벨 1~3) | DB › 인스턴스 `WandData`(최대 MP·간격·재생·쿨다운·칸 수·보조 칸 수·퍼짐 각·동시 발사·자기 MP 비용·최종 피해·치명·`copies`·든 지팡이 칸·초당 충전·화면 플래그) · `SpellData`(종류·희귀도·최대 레벨·크기·MP·시전 지연·`levels[0..2]` 수치 줄·명중 상태 효과·연계 칸·궤도·화면 키). CSV 한 장씩(`levels[1].damage`) |
| 동작 | 지팡이 패시브, 칸에 두면 지팡이에 붙는 주문, 유물, 세트 | DB › 패시브(E1 때문에 조각에 `passive` 칸을 하나 더 둔다) |
| 능력치 | 지팡이 최대 MP·재생은 원본 값 + 보정 | `Rules.WandStat(w, 칸) = AttributeSystem.Apply(원본 값, Modifiers(w, 칸) ∪ Modifiers(hero, 전역 칸) ∪ 든 지팡이 칸)`(28장 4-6) |

### 2-2. 순서 — 행동과 해석기

```
SetTrigger { held }                              ← 화면: 발사 입력(누르는 동안). 입력 상태만 바꾼다
Ticked: held && readyAt <= Clock → Do(CastWand)   ← 시전 박자는 규칙 시계(33장 5-1 모양). MP 부족은 미리 보고 건너뛴다
CastWand { wand }
  1. MP 정산: mp = min(최대, mp + 재생 × (지금 − mpAt)), mpAt = 지금
  2. 틀 = templates[wand, cursor, version] — 없으면 Rules.Interpret(wand, cursor)로 만든다
  3. 노드마다 의도 '주문 시전'(SpellCastIntent) → 유물·지팡이 패시브가 고친다
  4. 뿌리 노드 MP 합: Game.Pay("run", "wands[w].mp", 합)
  5. cursor = 틀.next, readyAt = 지금 + 간격(끝에 닿았으면 + 쿨다운, cursor = 0)
  6. 이벤트 SpellCast(틀 id, 이번 시전의 count·copies·element) → 화면이 풀에서 발을 꺼낸다

Rules.Interpret(wand, start) — 순수 함수(난수 없음)
  ① start부터 오른쪽으로 읽는다. 강화는 모아 두었다가 오른쪽으로 처음 나오는 발사 주문에 합친다
  ② 동시 발사 수(지팡이 + Volley + Over Scatter)만큼 발사 주문을 모은다. Multi-Shot은 더 읽을 수, Arcane Nova는 다음 발사 주문의 발 수 ×20·MP ×5·피해 ×0.5
  ③ 연계 주문은 왼쪽 노드에 자식(끝 · 명중 · 비행 · 처치)으로 오른쪽 첫 발사 주문(과 그 강화)을 붙인다
  ④ Mimicry Cube = 오른쪽 조각을 한 번 더 읽음(레벨 = min, 읽는 중이면 끊음). 크기 2(Core of Flame)는 다음 칸을 '차지됨'으로 건너뜀
  ⑤ 끝에 닿으면 next = 0(쿨다운). Forced Cooldown을 만나면 거기서 끝. 모든 지팡이에 숨은 칸(Mirror of Vision의 Wand Spirit)
  ⑥ 노드 수치 = 원본 levels[레벨] + 강화 합. 칸마다 합치는 방식(더하기·곱하기·최댓값)은 해석기 코드 한 곳

SpellReport { hits, ends }                       ← 화면: 틱마다 묶음(판정 보고 행동)
  명중: Rules.SpellDamage(틀 노드, 대상) → 의도 '피해' → 처치 → 명중 상태 효과(Game.ApplyStatus) → 명중 연계(Echo)
  끝:   끝 연계(Fuse · Duet · Fireworks · Split · 폭발) → 자식 시전(위치 = 보고의 x, y). 오른쪽 MP는 이때 낸다(E2)
BeginChannel / EndChannel                        ← 지속 시전(Ray · Stream · Dragon Breath): 초당 MP를 지연 정산, 바닥날 시각에 ScheduleAfter로 끊음
BeginCharge / ReleaseCharge                      ← 충전 시전(Shining Star Arrow): 충전 시간은 규칙 시계로 잰다
SwitchWand · MoveReport{meters, still} · ContactReport · AuraReport · SummonCast · EnterRoom · GainRelic
Rules.Recalc()                                   ← 지팡이·칸·유물이 바뀌는 행동 끝: 재생이 바뀌기 전 모든 지팡이 MP 정산, 칸 수 정산(Drill·확장석),
                                                   세트 개수(run.sets), 틀 버전 +1
```

### 2-3. 계획 틀 — 연계와 명중 피해를 위해 규칙에 남기는 것

- 화면의 발 하나는 `(틀 id, 노드, 발 번호)`만 들고 다닌다. 명중·끝 보고가 이것을 되보내고, 규칙은 틀의 노드에서 수치·연계를 읽는다(화면이 피해 양을 보내지 않는다 — 33장 6절).
- 틀은 **지팡이 칸 배치가 바뀔 때만** 만든다(`version`). 지팡이마다 시작 칸 수 이하라 수십 개를 넘지 않는다. 살아 있는 시전마다 기록하면 시전 간격 0.05초 · 지속 +11초(Orbit)에서 수백 개가 되어 실시간 예산을 넘는다.
- 시전마다 바뀌는 것(Replica Gloves의 복제, 무작위 원소, 이번 시전의 MP)은 `SpellCast` 이벤트에만 싣는다. 시전할 때의 전역 보정(유물)은 명중 때 다시 읽는다(차이는 근사로 받아들인다).
- 발마다 상태(Echo 최소 간격, Spell Absorption 첫 명중, Duet가 물려받는 왼쪽 피해)는 `(틀, 노드)`마다 마지막 시각·표시로 근사한다. 정확히 하려면 그 노드가 있는 시전만 `run.casts`에 짧게 기록한다.
- 끝 보고가 오지 않는 발(화면 밖으로 사라짐)은 화면이 지속이 다 되면 반드시 끝을 보고한다. 오래된 틀은 버전이 3개 넘게 지나면 지운다.

### 2-4. 의도 · 이벤트 · 계산 함수

| 종류 | 이름(제안) | 필드 |
|---|---|---|
| 의도 | `SpellCastIntent`("주문 시전") | `wandId` · `pieceId` · `spell` · `kind` · `element` · `mpCost` · `damageMul` · `count` · `copies` · `cancelled` |
| 의도 | `DamageIntent`("피해") | `sourceId` · `targetId` · `wandId` · `spell` · `amount` · `kind`(보통·무차별·독·가시·화상) · `crit` · `boss` · `execute` · `cancelled` |
| 의도 | `HealIntent`("회복") · `DeathIntent`("죽음", `surviveRatio`) · `ChargeIntent`("충전", `wandId`·`amount`) · `RoomIntent`("방 만들기", `doors`·`special`·`chestOrStatue`) · `RoomRewardIntent`("방 보상", `kind`·`amount`) | |
| 이벤트 | `SpellCast`("주문 시전함") · `SpellHit`("주문 명중", `wandId`·`pieceId`·`amount`) · `EnemyKilled`("적 처치", `victimId`·`wandId`·`pieceId`·`boss`) · `HeroDamaged`("주인공 피격", `kind`) · `RoomEntered` · `DoorEntered` · `AreaEntered` · `ChestOpened` · `PotionUsed` · `CoinsPicked`(`amount`) · `RelicGained`·`RelicLost`(`relic`) · `SpellGained` · `WandSwitched` · `ShopOpened` · `Moved`(`meters`) · `SecondPassed` | |
| 계산 | `Rules.Interpret` · `Rules.WandStat` · `Rules.Mp`·`SettleMp` · `Rules.SpellDamage`(스탯 → 강화 합 → 지팡이 최종 배율 → 가한 피해 → 치명 `Game.Chance` → 의도) · `Rules.Charge`(충전 동사, 효율 `hero.chargeEff`) · `Rules.Recalc` | |

### 2-5. 화면과 규칙의 경계

| 화면(Unity — 일부러 GBible 밖) | 규칙(GBible) | 둘 사이 |
|---|---|---|
| 발 생성·궤도(직선·유도·마우스 추적·궤도 돌기·호버·뱀 몸)·퍼짐 각 적용·반동·비행 속도 | 무엇이 몇 발, 최종 수치(피해·치명·관통 수·튕김 수·지속·반경·속도·퍼짐 각) | `SpellCast` → 화면이 틀을 읽어 풀에서 꺼냄 |
| 충돌: 적·벽·투사체끼리(관통·반사·튕김·벽 관통, Evil Slayer Sword·Rock'n'Ball·Guardian Sprite·Stagnant Droplet) | 피해 계산·치명 굴림·처치·명중 상태 효과·연계·MP | `SpellReport{hits, ends}` 틱마다 묶음 |
| 범위 판정: 폭발·장판·오라·사슬(Chain of Lightning·Umbilical Cord)·끌어당김(Black Hole, Strong Traction의 반경 안 후보) | 범위 안 대상에게 줄 양, 후보에서 고르기 | 보고에 대상·후보 목록 |
| 이동·비행·구덩이·대시·질주·엉덩방아·원격 시전 위치 | 이동 속도·몸 크기·반동 값(능력치), 비행 여부, 미터당·멈춘 초당 충전 | `MoveReport{meters, still}` |
| 소환수·적 AI(추적·공격 박자·은신 무시) | 소환 한도·소환수 수치·소환수 시전 틀 | `SummonCast{summon}` |
| 위치 | 위치를 저장하지 않는다 | 끝난 자리(x, y)만 보고 필드로 받아 자식 시전에 실어 돌려준다(한 번 정해지는 값) |

### 2-6. 명세서 4-2에 표시할 것

- [x] 다른 것의 능력을 빌려 쓴다 — Mimicry Cube(레벨 한도), Autonomous Grimoire(기록)
- [x] 놓인 순서·위치가 효과를 바꾼다 — 왼쪽부터 읽기, 강화 → 다음 발사 주문, 연계 → 오른쪽
- [x] 한 조각이 여러 칸을 차지한다 — Core of Flame
- [x] 위치·조건에 따라 태그·속성이 바뀐다 — Tricolor Scepter·Radiant Bouquet·Colorful Cloak(무작위 원소)
- [x] 자원을 다른 곳에서 내거나 나눠 쓴다 — Energized Orb, Enchanting Coin(골드), Arcane Barrier(MP 공유)
- [x] 효과가 반복 발동한다 — Multi-Shot·Arcane Nova·Dazzling Fireworks·Replica Gloves·Quadruple Staff·Split
- [ ] 판정 규칙이 바뀐다 — 반사 우선·벽 관통은 화면의 충돌 규칙이라 해당 없음

---

## 3. 분류 표

시트 상태: 지팡이 31·39·42는 패시브 글이 잘렸다. 59·81은 수치가 모두 0이고 패시브가 없다. 주문 9개(Parasite · Fusion Summon · Cadaver Explosion · Indomitability · Magic Reservoir · Tranquil Bloom · Charge Mode · Forced Cooldown · Capacity Expansion Stone)는 효과 칸도 설명도 비어 이름으로 추정했다("추정"). Meteor·Thunderstorm·Hover·Accelerator·Orbit의 `int1`·`float1`, Strong Traction Lv3의 "?verify"는 시트의 빈 값이다.

### 3-1. 지팡이 97

모든 지팡이: DB › 인스턴스 `WandData` 한 줄(최대 MP · 시전 간격 · MP 재생 · 쿨다운 · 칸 수). 아래는 "패시브" 칸만 나눈다.

| 번호 · 이름 | 분류 | 만드는 법 |
|---|---|---|
| 3·4·6·7·9·13·14·16·17·20·21·25·49·50·52·61·63·65·66·67·71·72·82·86·88·91(패시브 없음, 26) | DB | `WandData` 칸만 |
| 59 Warlock's Satchel · 81 Collector's Staff | DB | 수치 0·패시브 없음. 원작의 특수 지팡이라 동작이 정해지면 다시 분류 |
| 1·2·5·23·29·51·57·60·77·78(퍼짐 각만, 10) | DB | `WandData.scatter`. 규칙이 최종 각(Precise Shot −30°·Talisman of Focus까지)을 틀에 싣고, 흩뿌리는 것은 화면 |
| 24 Venomous Bite · 28 Shamanic Dagger · 68 Asteroid · 79 Shapeshifter · 80 Rainbow Electroweb | DB | 자기 지팡이 MP 비용 ×0.8/0.7/0.9 · 최종 피해 ×0.7 · 치명 +70% = 원본 칸 `mpCost`·`finalDmg`·`crit`. 고정 값이라 패시브가 필요 없다 |
| 8 Microcosmic Embrace · 12 Natural Convergence · 40 Deed Witness · 46 Sorcerer's Oath · 95 Shard of Time | DB | "모든 지팡이" = DB › 패시브(가진 목록 `run › wands`, `matchField` `passive`) `modifiers` → `modifierTarget` `hero`의 전역 칸(`wandMpCost` 독립 곱 −0.2 · `wandRegen` +3/+5/+8 · `radius` +0.5). 재생이 바뀌면 `Recalc`가 먼저 MP 정산. **E1** |
| 15 Novice's Grimoire | DB | 소환 주문 MP ×0.9 = 원본 칸 `summonMpCost`(해석기가 소환 노드에 곱함) |
| 38 Sacrificial Dagger | DB | 패시브: 가로채기 `피해`, 의도 조건 `sourceId 일치 hero` AND `targetId 일치 hero` → `amount` 곱하기 0.01. E1 |
| 39 Energizer Wand(글 잘림) | DB | "MP 소모가 …면 피해 +50%" → 패시브: 가로채기 `주문 시전`(`holderField` `wandId`), 의도 조건 `mpCost ≥ N` → `damageMul` 곱하기 1.5. 문턱이 최대 MP 비율이면 BindTo |
| 10 Featherweight · 22 Frenzy Enchanter · 55 Handy Wand · 94 Clearwater Resonance | Block | 보조 칸 충전(시전마다 3·4·3·1.5): 패시브(`perElement`(놓인 것마다) `run › wands`, 이벤트 `주문 시전함`, `holderField` `wandId`) → 효과 `에너지 충전`(`Rules.Charge`). '필드 바꾸기'가 아니라 동사 Block인 이유는 Infusion Hand Seal(충전 효율)이 양을 고치기 때문(5절 6). 94의 MP ×0.3 · 최종 ×0.3 · 퍼짐 −45°는 원본 칸 |
| 18 777 · 44 Double Flute · 84 The Lightless | Block | 같은 모양, 이벤트 `주문 명중`(1.7 · 2.2 · 3.3) |
| 62 Festive Wand · 73 Dormant Whisker · 87 The Faultless | Block | 이벤트 `적 처치`(`wandId`)(20 · 90 · 130) |
| 35 Vow of Honor · 85 The Formless | Block | 이벤트 `주인공 피격`(가진 쪽 필드 없음 — 가지고 있으면)(33 · 100) |
| 30 Rusty Blaster · 64 Voltage Stone | Block | "45 피해마다 20·22" → 이벤트 `주문 명중`, 이벤트 조건 `amount ≥ 45`. "누적 45마다"라면 조각에 남은 양을 쌓는 BindTo. 최종 ×1.2 · MP ×1.1은 원본 칸 |
| 19 Time Stasis · 83 The Shadeless · 93 Omen of Doom | Block(+화면) | 화면이 틱마다 `MoveReport{meters}` → 이벤트 `이동함` → `에너지 충전`(미터당 2 · 5 · 7.5 × 이벤트 칸 `meters` — Block이 이벤트 칸을 읽는다) |
| 31 Energy Reservoir · 42 Legion Banner(글 잘림) | Block | "for every …"의 조건에 따라 위 줄 중 하나. 42의 치명 +15%는 원본 칸 |
| 11 Harmonic Resonance · 32 Trident · 45 Trichord Lute · 58 Wand of Convergence · 75 Frozen Touch · 89 The Sourceless | 메인 룰 | 동시 발사 5·3·3·2·2 / 동시 시전 3 = 원본 칸 `simultaneous`를 해석기 '동시 발사' 자리(2-2 ②)가 읽는다. 4-2 '반복' |
| 33 Soul-Devouring Orb · 74 Siegemaker · 96 Stellar Drift · 43 Expanding Container | 메인 룰 | 동시 3·3·5·2 + 처치 충전 30(Block) / MP ×1.3·×2·치명 +20%(원본 칸) / 모든 지팡이 최대 MP ×1.3(패시브, E1) |
| 97 Quadruple Staff | 메인 룰(+화면) | 원본 칸 `copies` 4 → 의도 `주문 시전`의 `copies`. 네 방향은 화면. MP ×1.5 · 최종 ×0.5는 원본 칸 |
| 34 Legacy of Ancestors · 76 Kingdom's Guardian · 90 The Wordless | 메인 룰 | 초마다 충전 15·35·66: 패시브에 시간 발동이 없어(E3) 원본 칸 `chargePerSecond`를 해석기가 MP처럼 지연 정산하고, 가득 찰 시각에 `Game.ScheduleAfter`로 보조 칸 시전 |
| 54 Book of Tranquility | 메인 룰(+화면) | 멈춘 초마다 20: 화면이 `MoveReport{still}`로 멈춤 시작·끝 → 해석기가 멈춘 시간만큼 정산. "서 있으면 반동 없음"은 화면 |
| 37 Conch Whistle · 53 Swift Spellblade · 41 Swiftshot Staff · 70 Restless Fey · 92 Wand of Focus | 메인 룰 | '든 지팡이' 자리: 들고 있으면 이동 +20/40%, 재생 ×0.2/0.3/1.5(들 때)·×1.5/1.8/0.5(안 들 때) = 원본 칸 `holdMoveSpeed`·`regenHeld`·`regenIdle`을 `Rules.WandStat`·이동 속도 함수가 `heldWand`로 고른다. `SwitchWand`가 바꾸기 전에 모든 지팡이 MP 정산. 패시브 보정에는 조건을 달 수 없어 자리로 둔다(5절 10). 37의 미터당 3은 Block |
| 26 Tricolor Scepter · 36 Radiant Bouquet | BindTo | 주문 색 무작위: 패시브 가로채기 `주문 시전`(`wandId`) → `[BindTo]`가 `Game.Random("element")`로 `element`를 바꾼다(값 틀은 무작위를 못 고른다). 해석기 '태그 정하기' 단계 |
| 56 Ripple Wand | BindTo | 이벤트 `주문 시전함` + 확률 5% → `[BindTo]`가 그 지팡이 MP를 최대로(최대가 능력치라 값 틀로 못 씀). MP ×1.5는 원본 칸 |
| 69 Energized Orb | BindTo | 기믹 목록 2-6: 가로채기 `비용 지불` → `key`·`path`를 이 지팡이로(`[BindTo]`, 모자랄 때만, 이 지팡이 MP를 먼저 정산). 연계 지불을 미리 검사로 바꾸면(E2) 해석기가 이것도 함께 본다 |
| 27 Defiant Nature · 47 Restless Heart · 48 Witch's Broomstick | 화면 | 늘 거꾸로 쏨(퍼짐 −360°) · 반동 반대 · 들면 비행 = 원본 칸(참거짓)을 화면이 읽는다. 비행은 구덩이 충돌을 끄는 화면 일 |

### 3-2. 주문 75

모든 주문: DB › 인스턴스 `SpellData`(종류 · 희귀도 · 최대 레벨 · 크기 · MP · 시전 지연 · `levels[0..2]`). 칸 조각 `Spell { id, spell, level }`. 수치형 강화를 패시브(가로채기 `주문 시전` + `{level:…}`)로 만드는 길도 있지만, 수치가 Block 칸에 들어가 CSV로 못 고치고 강화 하나마다 가로채기가 한 번 돌아 원본 칸 + 해석기 합산을 골랐다(5절 3).

| 번호 · 이름 | 분류 | 만드는 법 |
|---|---|---|
| 37 Penetration · 48 DMG Enhanced · 49 Time Duration Enhanced · 50 Energy Saving Mode · 51 Precise Shot · 52 Accelerator · 53 Range Enhanced · 43 Rebound | DB | 강화: `levels[n]`의 더할 칸(관통 +1/2/4 · 피해 +25/50/100% · 지속 +2/3.5/6초 · 비용 ×0.6/0.5/0.4와 지속·피해 ×0.75/0.8 · 치명 +10/20%와 퍼짐 −30/−60° · 속도 · 반경 +30/60/120% · 튕김 +2/4/8). 해석기가 다음 발사 주문에 합친다. 관통·튕김·속도·반경은 화면이 쓰지만 최종값(돋보기 +20%까지)은 규칙이 낸다 |
| 1 Magic Bullet · 4 Laser · 13 Meteor · 14 Rainbow · 22 Boomerang Blade | DB | 발사 주문 수치(피해 16/32/64·치명 20%·관통 5/8/11, 발 7·관통 1, 작은 운석 수, 무차별 = `damageKind`). Magic Bullet Lv2·3 "맞은 적이 받는 피해 +50/100%, 4/8초" = 명중 상태 효과 칸 → DB › 상태 효과(초 지속, 가로채기 `피해` `holderField` `targetId` → `amount` 곱하기 1.5/2, 레벨마다 하나). Laser의 반사 · Meteor의 튕김은 화면, "지팡이가 더 빨리 쏨"은 시전 지연 칸 |
| 35 Slime Crystal · 45 Frost Crystal(추정 — 빙결) | DB | 명중 상태 효과 칸(레벨마다 상태 효과 ID: 이동 ×0.6/0.5/0.4 보정, 3/4/5초). "주문 비행 속도 ×0.7/0.6/0.5"는 틀의 수치를 화면이 쓴다 |
| 54 Troll Serum · 61 Essence of Soul(설명만 — 소환 강화) | DB | 소환 주문에 붙는 강화: 소환수 체력 +40/80/160%, 초당 회복 2.5/5/10(소환수에 거는 상태 효과의 주기) |
| 68 Magic Reservoir · 69 Tranquil Bloom(추정 — 지팡이 수치 패시브) | DB | 칸에 놓이면 그 지팡이에 붙는 패시브: 가진 목록 `run › wands[].slots`, `matchField` `passive`, `perElement` 끔(가진 쪽 = 그 지팡이), `levelField` `level`, `modifiers`(그 지팡이의 `maxMp` 등). E1 |
| 36 Venom Crystal | Block | 명중 상태 효과 `poison`(중첩 1/2/4, 3/4/5초, 주기 1초) + 결과 효과 `중첩만큼 피해`(29장 5-1의 `StackDamage`). 유물 Reaper가 의도 `상태 효과 걸기`의 `stacks`를 곱한다 |
| 30 Pillar of Light | Block(+화면) | Lv2·3: 효과 `최대 체력 비율 피해`(40/80%, 보스 비율 칸). 적 투사체를 막는 것은 화면 |
| 17 Adava Keravda | BindTo | 처치할수록 강해짐: 패시브(`perElement` `wands[].slots`, 이벤트 `적 처치` `holderField` `pieceId`) → '필드 바꾸기' `kills` +1, 문턱을 넘으면 `level`을 올리는 `[BindTo]`(기믹 목록 2-4 진화). 무차별은 원본 칸 |
| 18 Thunderstorm | BindTo | "한 적만 맞히면 +130/160%": 같은 낙뢰(같은 노드·같은 발)의 명중 수를 보고 묶음에서 센다 → 피해 함수의 주문별 `[BindTo]` 훅. 전도 횟수는 원본 칸 |
| 20 Enchanting Coin | BindTo(+화면) | 시전 때 골드 20%를 `Game.Pay`(양이 규칙으로 정해짐), 동전마다 기본 피해 +10%, 끝나면 동전이 바닥에(화면, 주우면 판정 보고). 4-2 '다른 곳에서 내기' |
| 47 Core of Thunder | BindTo | 여러 개면 낙뢰 확률은 가장 높은 것, 피해는 더함 — 칸마다 합치는 법이 다르다. 해석기 합치기에 칸별 방식(최댓값)을 두면 DB |
| 58 Strong Traction | BindTo(+화면) | 치명이면 반경 7/8/9m 안 다른 적 1/2/4를 끌어와 같은 피해: 화면이 명중 보고에 반경 안 후보를 싣고, 규칙이 `Game.Random`으로 고른다. 끌어오는 움직임은 화면 |
| 59 Fusion Summon(추정 — 소환수 합치기) | BindTo | 소환수 목록에서 골라 합친다 |
| 75 Arcane Barrier | BindTo(+화면) | 가로채기 `피해`(`targetId hero`) + `[BindTo]`: 이 주문을 가진 지팡이 중 하나라도 정산 MP가 가득이면 막고 그 MP를 깎는다(지팡이끼리 공유). 장벽 그림은 화면이 같은 값을 읽는다 |
| 5 Fuse · 64 Duet · 65 Dazzling Fireworks · 66 Serial · 67 Echo | 메인 룰 | '연계' 자리(2-2 ③). 원본 칸 `link`(끝 · 끝 · 끝 · 비행 · 명중), `linkCount`(Fireworks 4, MP도 ×4), `linkMp`(Fuse 0.9/0.8/0.7 · Serial 0.7/0.55/0.4 · Echo 0.8/0.65/0.5), `inherit`(Duet 0.3/0.6/1.2), `linkDamage`(Fireworks 0.35/0.45/0.6), `minInterval`(Echo 0.3/0.2/0.1). 비행(Serial)은 바로(또는 `ScheduleAfter`). 오른쪽 MP는 연계가 일어날 때(E2). 4-2 '놓인 순서' |
| 15 Arcane Nova · 32 Volley · 33 Multi-Shot · 34 Over Scatter | 메인 룰 | '동시 발사·횟수' 자리: Nova = 오른쪽 첫 발사 주문 발 수 ×20·MP ×5·피해 ×0.5 / Volley = 동시 +2/3/4, 더한 발마다 MP ×0.78/0.73/0.68·퍼짐 + / Multi-Shot = 이번 시전에 읽을 주문 +1/2/4·MP ×1.5 / Over Scatter = 동시 5/8/12·쿨다운 −0.15/0.25/0.4·퍼짐 +. 4-2 '반복' |
| 44 Split · 8 Arcane Explosion · 60 Cadaver Explosion(추정) · 46 Parasite(추정) | 메인 룰 | 끝·처치 연계의 변형: Split = 끝날 때 자기를 3/5/8개(피해 ×0.33) / Arcane Explosion Lv2·3 = 이 주문으로 처치하면 그 자리에 폭발(반경 ×0.7/1) / Cadaver = 처치한 적 자리 폭발 / Parasite = 명중 시 기생 상태 효과 → 그 적이 죽으면 그 자리에서 시전. 자리는 처치·끝 보고의 위치 필드 |
| 9 Spell Absorption | 메인 룰 | 처음 맞힐 때 낸 MP를 돌려받음: 노드가 낸 MP와 "첫 명중" 표시가 필요(2-3) |
| 55 Mimicry Cube · 31 Autonomous Grimoire | 메인 룰 | '다시 읽기' 자리: Cube = 오른쪽 주문을 레벨 min(오른쪽, Cube)으로 한 번 더 읽고 고리는 끊는다 / Grimoire = 오른쪽 첫 발사 주문의 틀을 소환수에 기록, 소환수 MP 재생 = 지팡이의 50/75/110%. 4-2 '빌려 쓰기' |
| 57 Core of Flame | 메인 룰(+Block) | '여러 칸' 자리(크기 2: 머리 칸 + 차지됨 칸). 피해 +100%는 강화 칸, 화상 = 상태 효과(주기 1초, 3초) + 효과 `최대 체력 비율 피해`(5%, 보스 0.3%). 4-2 '여러 칸' |
| 11 Ray of Disintegration · 19 High-pressure Stream · 25 Fierce Dragon Breath | 메인 룰 | '지속 시전' 자리: `BeginChannel`/`EndChannel`, 초당 MP 지연 정산 + 바닥날 시각에 `ScheduleAfter`로 끊음. Breath의 초당 +30/40/50%는 시작 시각에서 계산(원본 칸 `rampPerSecond`), "MP = 지속 × 16/22/28"·3초도 원본 칸. 명중은 틱마다 보고(DPS 60/120/240) |
| 26 Shining Star Arrow · 70 Charge Mode(추정) | 메인 룰 | '충전 시전' 자리: `BeginCharge`/`ReleaseCharge` 사이를 규칙 시계로 잰다(화면이 보낸 시간을 믿지 않는다) → 치명 +25%/초, 33/66/100%를 넘으면 최종 ×1.5/1.7/2 |
| 27 Pop · 23 Sword of Judgement · 28 Skull of the Cthulhu · 29 Hand of the Cthulhu · 71 Wand Spirit | 메인 룰(+화면) | '소환' 자리: 발사 대신 `room.summons`(다음 맵까지 따라오면 `run`)에 원본 사본(`Game.NewId`), 한도 = `hero.summonLimit`. Skull Lv2·3 피해 + 지팡이 최대 MP ×0.5/1, Hand 피해 10/20/40·지속 20/30/40초는 원본 칸. 움직임·노림·투사체 막기는 화면, 공격은 `SummonCast`·판정 보고 |
| 72 Forced Cooldown(추정) · 73 Capacity Expansion Stone(추정) · 74 Resonance Rune | 메인 룰 | 지팡이 읽기를 바꾸는 칸 주문: 여기서 읽기를 끝내고 쿨다운 / 칸 수 +(칸 목록 정산) / 이 지팡이가 시전하면 공명 주문을 가진 다른 지팡이도 시전, 공명으로 시작된 시전은 공명을 부르지 않는다 |
| 2 Rock'n'Ball · 3 Butterfly · 6 Floating Wisp · 7 Black Hole · 10 Shadow Serpent · 12 Deceptive Mine · 16 Lightning Dash · 21 Evil Slayer Sword · 24 Condensed Water Bubble | 화면 | 투사체 행동 자체: 적 투사체를 40/80/120 버팀 · 냄새로 날아가고 다른 주문에 부서짐 · 모여들기와 3/4초마다 복제 · 끌어당김과 반경 자람 · 길이 6/12/24m · 멈춘 뒤 터짐 · 대시(시전 중 무적은 시전 때 걸고 끝 보고에 푸는 상태 효과) · 적 투사체 지우기/되돌리기 · 다가오면 터짐. 수치는 원본 칸, 바닥 잔여물은 화면 장판 → 닿으면 판정 보고 → 상태 효과 |
| 39 Hover · 40 Orbit · 41 Track · 42 Automatic Navigate | 화면 | 궤도 덮어쓰기. 지속 +·속도 +는 강화 칸(DB), 궤도 종류는 틀의 `trajectory` 칸을 화면이 읽는다 |
| 38 Chain of Lightning · 56 Umbilical Cord | 화면 | 동시에 쏜 주문 사이·소환수 사이 선의 닿음 판정. 9/18/36 · 초당 25/50/100은 원본 칸 |
| 62 Reflection · 63 Indomitability(추정) | 화면 | 반사(관통보다 먼저) · 부서지지 않음은 충돌 규칙 |

### 3-3. 유물 85

모든 유물: DB › 패시브, 가진 목록 `run › relics`(ID → 레벨). 레벨 수치는 `byLevel`·`{level:…}`. 희귀도 = 희귀도별 가중치 표(뺄 목록 = 가진 것). 최대 레벨·세트 구성은 패시브에 칸이 없어 E1. 대상 `주인공`은 시스템층의 대상 Block, 보정 받을 대상은 `hero`(전역 지팡이 칸 포함).

| 번호 · 이름 | 분류 | 만드는 법 |
|---|---|---|
| 2 Beast Fangs · 4 Blade of Fury · 13 Demon Mask · 30 Knight's Breast-plate · 52 Resilient Physique · 24 Four-Leaf Clover · 63 Titan's Pauldrons · 68 Wild Tentacles · 39 Merlin's Hat · 40 Merlin's Robe · 34 Magni-fying Glass · 37 Merlin's Beard · 71 Infusion Hand Seal | DB | `modifiers` → `hero`: 가한 피해 +20…80%(`byLevel`; 피해 함수가 `kind 독`이면 이 칸을 읽지 않아 "독에는 안 붙음") · 치명 피해 +0.5…2.5 · 치명 +10/18/26/34/42% · 받는 피해 −25% · 가한 +100%와 받는 +70%(두 줄) · 회피 11…55%(피해 함수가 `Game.Chance(회피)`) · 최대 HP +20·몸 크기 +15%·반동 −33%(화면이 읽는 값) · 이동 +1.5 · 전역 최대 MP +30 · 전역 재생 +2/4/6(정산) · 반경 +20% · 소환 한도 ×2 · 충전 효율 +20% |
| 1 Knight's Boots | DB | 가로채기 `피해`(`kind 가시`, `targetId hero`) OR 가로채기 `상태 효과 걸기`(`statusId poison`, `targetId hero`) → `막기`(두 의도 모두 `cancelled`) |
| 31 Knight's Helm | DB | 가로채기 `피해`(`kind 무차별`, `targetId hero`) → `amount` 곱하기 `{level:0.01/0.001}` |
| 49 Ranger's Boots | DB | 가로채기 `상태 효과 걸기`(`targetId hero`, 둔화, `sourceId`가 주문이 아님) → 막기 + `modifiers` 이동 +1 |
| 50 Reaper Mask | DB | 가로채기 `피해`(`sourceId hero`, `boss` 거짓) + 확률 1% → `execute` 값으로 참(피해 함수가 남은 체력만큼) |
| 12 Dashing Ear · 74 Invisible Wings · 10 Crimson Anklet · 53 Rogue Cloak | DB | 상태 효과로: 피격 → 0.8초 상태(이동 +160% + 피해 막기 가로채기) / 피격 → 지속 = 이벤트 `문 들어감` 1번(비행·이동 +30%) / 이동 ×0.7 보정 + 피격마다 '8초 대기' 다시 걸고 '빠름' 풀기, 대기가 끝날 때 '빠름'(×2) 걸기 / 시전마다 '5초 대기' 다시 걸고 은신 풀기, 끝날 때 은신. 비행·은신이 보이는 일(구덩이·적 AI)은 화면 |
| 3 Black Mark · 5 Blood Key · 36 Mer-chant's Bauble · 55 Silver Compass · 70 Explorer's Hat · 76 Cardiotonic · 77 Summoning Wealth Bell · 56 Silver Key | DB | 시스템층 의도·이벤트를 고친다: 이벤트 `상점 열림` → 무작위 1(상점 물건) '필드 바꾸기' `price` 값으로 0 / 의도 `방 만들기`(`special`·`chestOrStatue`·`doors` +1) / 의도 `방 보상`(`kind` 생명·동전) `amount` 곱하기 `{level:1.3/1.6/1.9}` / 의도 `비용 지불`(`key` 열쇠) 막기. Silver Compass의 "다음 방"은 고친 뒤 '플레이어 데이터에 주기'로 자기 레벨 −1 |
| 21 Endless Elixir · 26 Healing Belt · 84 Bird beak mask | DB | 이벤트 `물약 마심` + 확률 30% → 물약 +1 / 최대 HP +3·전역 최대 MP 기본 +2('필드 바꾸기', 쌓이는 값) / 물약 한도 보정 +1 · 2전투마다(숨긴 상태 효과 중첩으로 세기) 물약 +1 · 가로채기 `회복`(`source 물약`) `amount` +5 |
| 23 Fortune Earrings · 32 Lucky Bunny Ear · 27 Hexa-gonal Die · 42 Octagonal Die | DB | 이벤트 `유물 얻음`(`relic` 일치 자기) → `run.relicOptions`·`spellOptions` +1(가중치 표에서 고르기의 `countBonus`가 읽는 숫자) / 이벤트 `유물 얻음`·`주문 얻음` → 다시 뽑기 횟수 +`{level}`. 다시 뽑기는 시스템층(`Game.RerollChoice` + 횟수 `Game.Pay`). countBonus가 보정을 읽지 않는 점은 5절 5 |
| 28 Hourglass of Time | DB | 가로채기 `죽음`(`unitId hero`) → `막기` + `surviveRatio` 값으로 `{level:0.5/0.8}` + '플레이어 데이터에 주기'로 자기 값 0(가지지 않음) |
| 57 Soulbone Armor · 58 Soulbone Crown · 59 Soulbone Mantle · 64 Treant Circlet · 66 Treant Shoulder-guards | DB | 보호막 '필드 바꾸기': 처치 + 확률 20% → `shield` +1 / 처치마다 숨긴 상태 효과 +1중첩, 그 상태 효과가 3중첩에서 `tempShield` +1 하고 풀림(유물 하나에 패시브가 하나라 세기와 N번째 결과를 함께 못 둔다 — 5절 13) / 얻을 때 +20 + 같은 카운터 / 문 들어감 → `tempShield` +15 / `{level:3/6/9/12/15}` |
| 78 Coolant | DB | 가로채기 `상태 효과 걸기`(`statusId`가 유물 능력 쿨다운) → `length` 곱하기 `{level:0.75/0.5/…}` |
| 79 Swordsman Cloak | DB(+화면) | DB › 능력(스페이스 → `Game.Use`): 쿨다운 2초 + 질주 중 무적 + "한 번 공짜"(가로채기 `주문 시전` → `mpCost` 값으로 0, 지속 = 이벤트 `주문 시전함` 1번) 상태 효과. Evil Slayer Sword MP ×0.9 = 가로채기 `주문 시전`(`spell` 일치). 질주 움직임은 화면 |
| 6 Blood-thirsty Gaze · 65 Treant Robe | Block | 효과 `회복`(주인공): 처치 + 확률 20% → 1 / 문 들어감 → 5 |
| 47 Pros-pector's Vest | Block | 이벤트 `동전 주움` → 효과 `이벤트 양만큼 회복`(이벤트 칸 `amount`를 읽는 회복) |
| 44 Tree Spirit Pendant | Block | 피격 + 조건 체력 < 30 → 상태 효과 '나무 재생'(주기 1초) + 효과 `회복(상한 30)`, 30이 되면 스스로 풀림 |
| 75 Berseker | Block | 가로채기 `피해`(`sourceId hero`) + 조건 Block `체력 비율 이하`(50%) → `amount` 곱하기 1.5(필드 검사는 고정 값과만 비교) |
| 38 Merlin's Boots | Block | 처치 → 대상 Block `든 지팡이` → '필드 바꾸기' `mp` +4(지연 정산 MP에 더해도 값이 맞다) |
| 17 Electrified Crown | Block | 피격 → 모든 적 → 효과 `최대 체력 비율 피해`(10%·보스 0.2%·+30) |
| 7 Casket of Souls · 14 Demon-bone Tail | Block(+화면) | 처치 + 확률 30% → 효과 `주문 쏘기`(원본 주문 '추적 영혼', 피해 65, 위치 = 처치된 적) / 처치 → 효과 `소환`(따라다니는 마법 탄). 두 Block이 해석기·소환 자리를 부른다. 추적·따라다님은 화면 |
| 20 Endless Chest | Block(+화면) | `상자 엶` + 확률 30% → 효과 `상자 만들기`(방 물건 목록에 `Game.NewId`) |
| 45 Pros-pector's Gloves | Block(+화면) | 처치 + 조건 Block `레벨별 확률`(8/16/24%) → 이벤트 `동전 떨어짐`(화면이 바닥에 놓고, 주우면 판정 보고). 조건 '확률'은 레벨 틀을 받지 않는다(5절 4) |
| 19 Ember-heart · 83 Druid's Horn | Block(+화면) | 화면이 반경(3m / 3~5m) 안 적·아군을 `AuraReport{source, targets}`로 보냄(규칙은 출처마다 마지막 시각을 보고 1초 안이면 버린다) → 효과 `피해`(16) / `주인공 최대 체력 비율 피해`(10…50%)·`… 회복`(5…25%) |
| 82 Original Shape | Block(+화면) | DB › 능력(쿨다운 10초). 엉덩방아 이동·착지 반경 3m는 화면 → 착지 보고 → 효과 `최대 체력 비율 피해`(50%·보스 10%). 이벤트 `적 처치`(`source originalShape`) → 쿨다운 '상태 효과 풀기' |
| 35 Mask of the Hexer · 46 Pros-pector's Pickaxe · 48 Rageful Eye | BindTo | 가로채기 `피해`(`sourceId hero`) + `[BindTo]`: 저주 수 × 10/15% · 동전 3…7개당 1…5% · 잃은 HP 2당 1%(기믹 목록 2-9 '상태에 따라 변함') |
| 11 Pros-pector's Goblet | BindTo | `구역 들어감` → 동전 + min(10, 5%) |
| 16 Dusty Treasure · 85 Mystical Artifact | BindTo | 최대 레벨이 아닌 유물 하나 무작위 +1, 없으면 일반 유물 / 다른 일반·희귀·에픽 유물이 모두 최대 레벨일 때 대신 나옴(유물 고르기 결과 쪽), 레벨당 최대 HP +1은 `modifiers` `perLevel`. 둘 다 유물별 최대 레벨이 필요(E1) |
| 43 Pandora's Box · 81 Reaper | BindTo | 방 들어감 → 가방 앞 1/2/3칸 주문을 같은 희귀도 표에서 다시 뽑기 / 방 들어감 → 합칠 수 있는 주문 자동 합성. Reaper의 독 중첩 ×2/3은 DB(가로채기 `상태 효과 걸기` `stacks` 곱하기 `{level:2/3}`) |
| 54 Seed of Greed | BindTo | 가로채기 `회복`(`targetId hero`) → 막고 흡수량을 `run.relicCounters[seedOfGreed]`에 쌓음, 70이면 최대 HP +50(진화) |
| 62 Talisman of Focus | BindTo(+화면) | 멈춘 시간(화면 보고)에 따라 쿨다운·간격·퍼짐 최대 −60% — 능력치 함수의 계산 |
| 69 Reboot Key | BindTo | 주문·지팡이·유물·저주를 같은 희귀도로 다시 뽑고 구역 처음으로, 자기 없애기 |
| 72 Colorful Cloak | BindTo | 가로채기 `주문 시전` → 무작위 원소(Tricolor Scepter와 같은 함수) |
| 73 Ward Charm | BindTo | 방 들어감 + 확률 33% → 저주 하나를 무작위로 풀기(기본 '상태 효과 풀기'는 종류 전부만 푼다) |
| 15 Drill | 메인 룰 | `modifiers` → `hero.wandSlots` +1…5. 칸 목록 길이는 `Rules.Recalc`의 칸 수 정산(`Add(null)`) |
| 41 Mirror of Vision | 메인 룰(+화면) | 지팡이 수 한도 보정(DB) + "모든 지팡이에 Wand Spirit(+)" = 해석기가 모든 지팡이에 숨은 칸을 더해 읽는다. 늘 비행은 화면 |
| 51 Replica Gloves | 메인 룰 | 가로채기 `주문 시전` + 조건 Block `레벨별 확률`(25/45%) → `copies` +1 — 해석기의 복제 자리. 무작위 방향은 화면 |
| 8 Close Observation · 29 Hunter's Talisman | 화면 | 화면이 `Game.Owned`를 읽어 적 체력 막대(Lv2 숫자) · 시야 |
| 9 Cloud Piercer · 61 Stagnant Droplet · 67 Wand Lens · 18 Elven Ears | 화면 | 벽 관통 · 3m 안 적 투사체 느리게 · 넉백 +50% · 시전 중 감속 없음(화면 코드가 `Game.Owned`·보정 값을 읽는다) |
| 25 Guardian Sprite | 화면 | 정령 3/6/9/12/15(수 = `Game.Owned` × 3)가 적 주문을 막는다 |
| 33 Magician's Robe | 화면(+DB) | 5m 떨어진 곳에서 시전(시전 위치). 반경 −30%는 `modifiers` |
| 60 Sprite Wings | 화면 | 비행(구덩이 충돌 끔, 독 장판은 그대로) |
| 80 Bi'an | 화면(+DB) | 날아간 칼을 불러들이며 경로 판정 → 판정 보고. 능력·쿨다운 5초는 DB |
| 22 Finite Gloves | **예외(E3)** | "일정 간격마다 보스 아닌 적 절반 처치" — 패시브에 시간 발동이 없다(4절) |

### 3-4. 세트 5

세트 계산(메인 룰, 기믹 목록 2-8): DB › 인스턴스 `SetData`(구성 유물 목록 `[DataRef(typeof(PassiveData))]` + 세트 패시브 ID)를 `Rules.Recalc`가 읽어 `run.sets`(세트 패시브 ID → 모은 개수)를 쓴다. 세트 효과는 가진 목록 `run › sets`인 패시브다.

| 세트 | 분류 | 만드는 법 |
|---|---|---|
| Knight Series(3) | DB | 이벤트 `방 들어감` AND `플레이어 데이터 검사` `sets[knightSet] ≥ 3` → 주인공에게 상태 효과 '무적 8초'(가로채기 `피해` 막기) |
| Merlin Series(4) | DB | `modifiers` `hero.wandMaxMp` `byLevel` 0/0/0/300 |
| Tree Spirit Series(4) | DB | `modifiers` `hero.maxHp` `byLevel` 0/0/0/100 |
| Soul Bone Series(4) | BindTo | 방 들어감 + 4개 → `tempShield`의 20%를 `shield`로(두 칸에 걸친 계산) |
| Gold Rush Series(4) | Block(+화면) | 화면이 주인공에 닿은 적을 `ContactReport`로 → 4개면 효과 `동전으로 바꾸기`(보스 제외: 보상 없이 없애고 동전 1) |

---

## 4. 예외 · 문법 밖

### E1. 원본 인스턴스와 패시브를 한 조각에 짝짓기(공통)

- **무엇**: 지팡이·주문은 수치가 많아 DB › 인스턴스(CSV)로 두는데, 일부는 동작(모든 지팡이 보정 · 가로채기 · 이벤트 충전 · 칸 주문의 지팡이 보정)이 있어 DB › 패시브도 필요하다. 패시브는 조각의 '일치 필드' 값이 패시브 ID와 같을 때 가진 것으로 보는데, DB ID는 종류를 넘어 하나뿐이라(17장 1절, 코드 `EditorDatabase.CanUseId`) 인스턴스와 패시브가 같은 ID를 가질 수 없다. 유물은 반대로 패시브뿐이라 최대 레벨 · 세트 구성 같은 고정 칸을 둘 곳이 없다(17장 0-5 "PassiveData에는 칸을 더할 수 없다").
- **어디**: 지팡이 패시브 33(모든 지팡이 보정 6 · 가로채기 2 · 충전 21 · 색 2 · BindTo 2), 주문 4(Magic Reservoir · Tranquil Bloom · Adava Keravda · Arcane Barrier), 유물 최대 레벨(Dusty Treasure · Mystical Artifact · "최대 레벨이면 다시 안 나옴"), 세트 구성.
- **우회**: ① 조각에 원본 ID 칸을 둘 둔다 — `Wand { wand, passive }`, 만들 때 원본의 `[DataRef(typeof(PassiveData))] passive`를 옮겨 적는다(참조 7절 3 "원본의 고정 값을 판 데이터에 복사하지 않는다"에 약하게 어긋난다. 이 보고서는 이것을 골랐다). ② 이름 규칙 — 패시브 `oldWand`, 인스턴스 `wand.oldWand`, 코드가 접두어로 찾는다(DataRef 검증·ID 바꾸기 추적이 끊긴다). ③ 유물 고정 칸은 인스턴스 `RelicInfo`(ID `relicInfo.X`)나 밸런스 값 `relicMax.X`.
- **라이브러리에 있으면 없어지는 것**: 패시브 '일치 필드'가 "조각의 원본 인스턴스가 가리키는 패시브"를 따라가는 길(예: 일치 필드에 `wand → passive` 같은 원본 칸 경로), 또는 인스턴스에 붙이는 패시브 기능 필드(사용 기능 `Usable`처럼 `[SerializeReference]`로 원본 안에 패시브를 두고 ID = 인스턴스 ID).

### E2. 묶음 판정 행동 안에서 MP가 모자란 연계(공통)

- **무엇**: Echo · Fuse · Duet · Serial · Dazzling Fireworks의 오른쪽 주문은 연계가 일어날 때 MP를 낸다. 명중·끝은 틱마다 `SpellReport` 하나로 온다. `Game.Pay`가 모자라 실패하면 그 묶음 전체(다른 명중·처치)가 취소된다. MP 부족은 이 게임에서 흔한 정상 상황이다. 발사 입력의 `CastWand`도 같다(실패가 알림으로 가지 않게 화면이 받아야 한다).
- **우회**: (가) 해석기가 MP를 정산한 뒤 `Game.CanPay`로 미리 보고 모자라면 그 연계만 건너뛴다. `CanPay`는 가로채기 `비용 지불`을 보지 않으므로, MP 비용을 바꾸는 효과는 모두 의도 `주문 시전`의 `mpCost`에서 고치고(Swordsman Cloak의 "한 번 공짜"도), Energized Orb는 해석기가 직접 본다(기믹 목록 2-6의 '비용 지불 가로채기' 대신). (나) 연계마다 `Data.Mark()`/`RollbackTo`(02장 2.7) — `Mark`가 매번 Sync라 틱마다 수십 번이면 예산 밖.
- **라이브러리에 있으면 없어지는 것**: `Game.TryPay`(`TryUse`처럼 모자라면 거짓, 가로채기 `비용 지불`은 거친다).

### E3. 가진 동안 N초마다 — Finite Gloves(조각 1)

- **무엇**: 패시브 발동은 이벤트 받기 · 조건 충족 · 가로채기뿐이고 주기가 없다. 기믹 목록 2-7은 "실시간 'N초마다'는 상태 효과의 주기"라 하지만, 유물을 영구 상태 효과로 걸어 두는 우회는 17장 0절 · 29장 1절이 막는다.
- **우회**: 메인 룰이 방 시계로 1초마다 이벤트 `초 지남`을 쏜다(행동 1개/초, 실시간 예산 안). 패시브가 받고 `[BindTo]`가 간격을 세어 보스 아닌 적 절반을 무작위로 처치한다. 지팡이 '초마다 충전'(34 · 76 · 90)은 같은 이유로 메인 룰의 지연 정산으로 옮겼다(예외로 세지 않음).
- **라이브러리에 있으면 없어지는 것**: 패시브에 `pulse`(주기(초)) 칸 — 상태 효과의 주기와 같은 규칙(가진 쪽마다, 프레임 끝 행동 하나에 묶음).

### 문법 밖 — 0

- 투사체가 끝난 순간 · 명중 · 범위 안 대상 · 반경 안 후보 · 이동 거리 · 멈춤 · 접촉 · 착지는 모두 화면이 판정하고 결과를 보내는 판정 보고 행동(관례 3절)이라 문법 안이다. 규칙이 화면 없이 물리 결과를 알아야 하는 것은 없었다.
- 다만 기획 명세서 읽는 법 7절 끝 줄은 "물리 충돌 결과, 화면 애니메이션이 끝나는 순간"을 문법 밖이라 적는다. 그 문장대로면 "주문이 끝날 때"(Fuse · Duet · Split …)가 모두 문법 밖이 된다(5절 12).

---

## 5. 문서 빈틈

1. **시전 계획을 어디에 두나(가장 큼)**. 기믹 목록 1절은 "해석기가 계획을 만들고, 화면이 쏘고, 끝남·명중을 보고한다"까지만 말한다. 명중 피해 · 연계 · Spell Absorption(낸 MP) · Duet(물려받는 피해)에는 쏜 순간의 계획이 규칙에 있어야 한다. 33장 5-7은 "피해 양은 규칙이 정한다"면서 예가 주인공 공격력 하나(아레나)라 발마다 다른 수치를 다루지 않고, 같은 절이 "개체 수백은 판 데이터에 넣지 않는다"고 한다. 시전 간격 0.05초 · 지속 +11초면 살아 있는 시전이 수백이다. 정해지지 않은 것: 계획의 보관 단위, 화면 발이 들고 다니는 참조, 끝 보고가 오지 않을 때의 정리, 발마다 상태(Echo 최소 간격 · 첫 명중). 이 보고서는 2-3의 '계획 틀'을 골랐다. 기믹 목록 1절 · 33장 5-7에 "규칙이 만든 계획을 화면 개체가 들고 다니다 끝·명중 때 되보내는 모양" 한 절이 있으면 된다.
2. **원본 + 패시브의 ID 짝**(E1). 기믹 목록 1절 "조각 = 고유 id + 원본 ID 필드, 패시브는 일치 필드로 원본 ID 필드를 본다"는 원본 ID = 패시브 ID라고 은연중 가정한다. 원본이 인스턴스(수치)이기도 한 조각(지팡이·주문)은 어떻게 하는지 없다. (코드 확인: `EditorDatabase.CanUseId` — 모든 종류에서 ID 하나.)
3. **능력·사용 기능에 레벨이 없다**. 28장 3-4 "`{level}`은 패시브가 아닌 곳(상태 효과·능력)에서는 0". 레벨 1~3인 주문의 명중 효과를 사용 기능(`Game.Use`)으로 만들 수 없어 레벨마다 능력을 따로 두거나 원본 칸 + 해석기 코드로 가야 한다. 관례 3절 "조각 하나하나의 효과는 DB(패시브·능력)에 두고 메인 룰 코드에 쓰지 않는다"와 부딪힌다 — 수치형 조각(강화 주문)을 원본 칸으로 두고 해석기가 합치는 것이 그 규칙 안인지 문서가 말하지 않는다. 인스턴스는 사용 기능을 하나만 가져(맨 위 `Usable` 필드 하나, 코드 확인: `GBibleRuntime.UsableOf`) 명중 · 끝 두 순간을 따로 둘 수도 없다(04장·16장 2-8의 "형식마다 한 번만 찾는다"가 "둘째 필드는 무시"인지 문장으로는 알 수 없었다).
4. **조건 '확률'이 레벨 틀을 받지 않는다**(퍼센트는 숫자 칸, 코드 확인: `ChanceCondition.percent`). 레벨마다 확률이 다른 유물(Pros-pector's Gloves 8/16/24% · Replica Gloves 25/45%)은 프로젝트 조건 Block `레벨별 확률`이 필요하다. 기믹 목록 2-5에 한 줄이면 된다.
5. **후보 +1을 유물이 어떻게 올리나**. 기믹 목록 2-11 "후보 +1 → 후보 수에 더할 숫자(플레이어 데이터)"만 있다. `countBonus`는 필드 값을 그대로 읽어 패시브 `modifiers`(가진 동안 능력치 보정)가 붙지 않는다(코드 확인: `PlayerDataNumberRef.Read`). 얻을 때·잃을 때 숫자를 바꾸는 이벤트(`유물 얻음`·`유물 잃음`)를 시스템층이 쏴야 한다. 같은 이벤트가 세트 계산·칸 수 정산을 거는 자리이기도 한데, 유물을 결과 '가중치 표에서 고르기'로 바로 넣으면 프로젝트 코드가 끼어들 자리가 없다 — 기믹 목록 2-8의 "계산 함수"가 언제 도는지(세피리아는 "배치 행동 끝"으로 적혀 있다) 유물 쪽에는 없다.
6. **보조 칸 충전은 동사여야 한다**. 기믹 목록 2-7 "보조 슬롯 에너지 → 숫자 필드 + '필드 바꾸기'"는 Infusion Hand Seal(충전 효율 +20%)이 있으면 고칠 자리가 없다. 같은 칸의 "7초마다 충전 → 상태 효과의 주기"는 지팡이·유물(패시브)에는 쓸 수 없다(E3).
7. **'놓인 것마다' + 경로 `[]`의 조합**. 17장은 `party[].traits`(가진 쪽 = 동료마다)와 '놓인 것마다'를 따로 설명할 뿐, 함께 쓰면(`run › wands[].slots`) 모든 지팡이의 칸이 한 줄로 이어져 순서가 정해진다는 것은 코드에만 있다(코드 확인: `PassiveSystem.OrderedHoldersFor`·`Collections`). 가진 쪽 필드가 같음만 보는 것(목록 포함은 없음)도 코드로 확인했다(`HolderFieldMatches`).
8. **원본에 기본값이 있는 능력치**. 28장 4-1 "능력치 = 조각의 숫자 필드(필드 값이 기본값)"와 참조 7절 3 "판 데이터에 원본 고정 값을 복사하지 않는다"가 지팡이 최대 MP · 재생에서 부딪힌다. `AttributeSystem.Apply(원본 값, Game.Modifiers(조각, 칸))`(28장 4-6)로 풀리지만 문서는 이것을 미리보기 용도로만 적는다.
9. **초당 차오르는 값**. 33장 5-1 "시각을 적어 두고 그때만"은 끊어지는 일(웨이브)만 다룬다. MP처럼 초당 차오르는 값을 지연 정산하면, 재생 속도가 바뀌는 순간(가진 동안 보정은 읽을 때 계산이라 지난 시간에 거꾸로 적용된다, 든 지팡이 바꾸기)마다 먼저 정산해야 한다는 점이 없다.
10. **'든 것일 때만' 보정**. 패시브 `modifiers`에는 조건을 달 수 없다. 기믹 목록 2-9에 "든 지팡이·고른 조각일 때만" 줄이 없어 메인 룰 자리로 만들었다.
11. **보정 받을 대상은 패시브 하나에 하나**. 기믹 목록 2-9 '전체에'는 `run.stats`처럼 따로 된 객체를 예로 드는데, 한 유물이 주인공 칸과 전역 칸을 함께 고치면(Mirror of Vision: 지팡이 수 + 비행) 대상이 둘이 된다. 전역 칸을 `hero` 한 객체에 모으라고 적는 편이 맞다.
12. **문서끼리 어긋남**: 기획 명세서 읽는 법 7절 끝 "마지막 칸이 규칙 밖에서 정해지는 것(물리 충돌 결과, 화면 애니메이션이 끝나는 순간)이면 문법 밖" ↔ 관례 3·13절 · 28장 1절 · 기믹 목록 4절 "판정 보고 행동이면 문법 안".
13. **카운터 기믹을 패시브 하나로**. 기믹 목록 2-7 "Loyalty Card(6번째마다) → 숫자 필드 + '필드 바꾸기' + 조건"은 발동 하나 · 결과 목록 하나인 패시브로 "세기"와 "N번째의 결과"를 함께 두는 법이 없다. 같은 조각을 두 패시브가 가질 수도 없다(일치 필드 = 패시브 ID). 이 보고서는 숨긴 상태 효과의 중첩으로 셌다(Soulbone Crown · Bird beak mask).
14. **Arcane Nova의 자리**. 기믹 목록 2-2는 Arcane Nova(×20, 피해 반)를 '효과 두 배 — 동사 의도의 `amount` 곱하기'에 두었다. 실제로는 오른쪽 첫 발사 주문의 **발 수** ×20 · MP ×5 · 피해 ×0.5로, 시전 의도(`count`·`mpCost`·`damageMul`)를 고치는 '반복' 줄이다.
15. **37장의 주문 칸**. "언제 쓰는가"에 "주문 칸: 지팡이 칸을 왼쪽부터 읽기, 다음 칸 주문에 강화 붙이기(매지크래프트)"를 들면서 5절 예제가 없다. '자리로 고르기'의 방향에는 "오른쪽으로 처음 나오는 ○○(발사 주문)"이 없고, 방향은 줄을 넘지 않아 끝에서 처음으로 도는 읽기도 코드다. "해석기 코드로 읽는다" 한 줄이면 된다.
16. **누르고 있는 입력**. 실시간 발사는 `OnClick`이 아니다. 35장의 실패 알림은 버튼용이고, "누르는 동안" 입력을 `SetTrigger { held }` + `Ticked`에서 행동(33장 5-1 모양)으로 만들면 실패 대신 조용히 건너뛸 수 있다는 모양이 문서에 없다.
17. **이름**: GBible의 '슬롯'은 세이브 슬롯(31장)이라 매지크래프트의 wand slot · post slot은 '칸' · '보조 칸'으로 불렀다. 강화 주문(Enhancement)의 '강화'와 28장 5-2 '영구 강화'(대장간)가 같은 낱말이다.

---

## 6. 숫자

| 분류 | 지팡이 97 | 주문 75 | 유물 85 | 세트 5 | 합 262 |
|---|---|---|---|---|---|
| DB | 51 | 19 | 44 | 3 | **117** |
| Block | 19 | 2 | 14 | 1 | **36** |
| BindTo | 4 | 7 | 13 | 1 | **25** |
| 메인 룰 | 20 | 30 | 3 | 0 | **53** |
| 화면 | 3 | 17 | 10 | 0 | **30** |
| 예외 | 0 | 0 | 1 | 0 | **1**(+ 공통 2: E1 · E2) |
| 문법 밖 | 0 | 0 | 0 | 0 | **0** |

- 추정으로 분류한 것: 주문 9(효과 칸·설명 없음) + 지팡이 5(31 · 39 · 42 글 잘림, 59 · 81 수치 0).
- "(+화면)"이 붙은 규칙 쪽 조각: 지팡이 5 · 주문 9 · 유물 10 · 세트 1. 화면 30과 합치면 화면이 판정을 맡는 조각은 55다.
- E1을 쓰는 조각: 지팡이 33 · 주문 4 · 유물 2(+ 세트 구성 5).
- 메인 룰 자리(명세서 4-2와 2-2): 읽기·커서·쿨다운 · 강화 합치기 · 동시 발사·횟수·복제 · 연계(끝·명중·비행·처치) · 다시 읽기 · 여러 칸 · 지속 시전 · 충전 시전 · 소환 · MP 지연 정산 · 든 지팡이 · 보조 칸 충전(동사·시간) · 공명·강제 쿨다운·숨은 칸 · 태그 정하기(원소) · 세트 계산 · 칸 수 정산.

---

## 보강 뒤(2026-10-05)

예외 3은 모두 없어졌다.

- E1 → 패시브 `matchField`가 글자 목록도 받고, `matchVia`(원본에서 읽기)로 조각의 원본 ID → 원본의 패시브 ID 목록을 본다.
- E2 → `Game.TryPay`: 모자라면 바꾸지 않고 거짓(틱 묶음 행동이 취소되지 않는다).
- E3 → 패시브 `pulse`·`pulseByLevel`(가진 동안 N초마다, 게임 시각 기준).
- 그 밖: 값 틀 `{user:경로}`·`{event:경로}`(지팡이 레벨·주문 값), '후보 수에 더할 숫자'가 패시브 보정을 읽음, 보정 줄마다 `target`, "든 지팡이일 때만" = `modifierWhen`(보정 조건).
- 시전 계획을 둘 곳(가장 큰 빈틈)은 기믹 목록 1절 매지크래프트 줄에 적었다: 계획 틀은 칸 배치가 바뀔 때만 판 데이터에, 화면의 발은 (틀, 노드, 발 번호)만.
