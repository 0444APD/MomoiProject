# 장르 시험 4 — 수집형 RPG(오프라인 PC판)

목표 확인: 정해진 양식의 기획 명세서 + GBible 문서·규칙만으로, 새 프로젝트에서 레벨 디자인을 뺀 모든 시스템과 화면을 AI가 하루 안에 질문 20개 이하로 만들 수 있나. 이 문서는 수집형 RPG 하나로 그것을 해 보고 **빈틈**을 적는다. 코드는 고치지 않았다.

- 읽은 것: `AI/Reference.md` · `ProjectConventions.md` · `PlanningSpecGuide.md` · `PlanningSpec.md` · `PlanningSpec-CardDungeon.md`, 매뉴얼 02·06·07·11·12·14·15·16·17·28·29·30·31·35장, 가이드 07·10장. 의심스러운 곳은 코드(`Runtime/Core`·`Runtime/Unity`)를 읽어 확인했다(확인한 곳은 "코드 확인"으로 적는다).
- 판정: **A** = DB만 · **B** = 코드 한 단위(Block 하나·`[BindTo]`·행동 하나·규칙 함수 하나) · **C** = 중간층 확장(규칙 함수·의도 필드·새 가로채기 자리) · **D** = 문법 밖. 기믹마다 "그 기믹을 처음 만들 때 필요한 가장 깊은 층"으로 판정하고, 시스템층이 생긴 뒤 같은 종류를 하나 더 만들 때의 판정을 괄호에 적는다.

---

## 1. 기획 명세서 — 별의 계약자들 (채운 예)

> 기획자가 쓴 그대로다. [선택] 칸 몇 개는 일부러 비웠다.

### 0. 한 줄 소개

- 장르·참고 게임: 수집형 RPG. 붕괴: 스타레일의 턴 순서 + 서머너즈 워의 속도·게이지. 오프라인 PC판(결제 없음).
- 한 문장 소개: 뽑기로 모은 '계약자' 다섯 명을 편성해 장(챕터)마다 이어지는 스테이지를 자동 전투로 깨고, 레벨·돌파·장비로 키운다.
- 멀티플레이: 없음
- 플레이 한 판의 길이: 전투 하나 2~4분. 하루 30분쯤(스태미나를 다 쓰는 정도).

### 1. 한 판의 흐름

- 시간: 턴제. 전투는 속도로 순서가 정해지는 자동 전투(플레이어는 배속·일시정지만). 로비에는 시간이 없고, 현실 시간으로 스태미나·일일 임무·배너만 바뀐다.
- 한 판이 무엇인가: 판이라는 게 따로 없다. 켜면 타이틀 → 로비. 로비에서 뽑기·레벨업·돌파·장비·편성 → 지도에서 스테이지 고르기 → 스태미나를 내고 전투(웨이브 1~3) → 결과(별 0~3, 보상) → 로비. 별 3개를 받은 스테이지는 소탕(전투 없이 보상만, 1번·10번).
- 반복 단위: 전투의 '차례'. 아군 5명과 적 1~5명이 모두 행동 게이지를 가지고, 속도만큼 찬다. 다 찬 유닛이 행동한다: 궁극기(에너지 100) → 스킬(쿨다운 끝) → 기본 공격 순으로 저절로 고른다. 행동하면 게이지 0.
- 판이 끝나는 때: 마지막 웨이브의 적 전멸 = 승리. 아군 전멸 또는 차례가 150번을 넘기면 패배. 패배하면 스태미나는 1만 빼고 돌려준다.
- 판이 끝나도 남는 것: 거의 전부(계약자·장비·재화·스테이지 별·천장 횟수·임무 진행).
- [선택] 되돌리기(Undo): 없음
- [선택] 이어 하기: (비움)

### 2. 다루는 것들

#### 2-1. 주인공·유닛

- 아군(계약자)·적(몬스터) 모두: 체력·공격력·방어력·속도·치명타 확률·치명타 피해·회피·진영(불·바람·물)·에너지(최대 100).
- 계약자의 수치 = 원본 수치 × 레벨(1~60) × 돌파 보너스(단계마다 +5%) + 장비.
- 적의 수치 = 몬스터 원본 × 스테이지 레벨.
- [선택] 피해 공식: (비움)

#### 2-2. 원본 종류

| 종류 | 예 | 고정 값 | 판마다 바뀌는 값 | 하는 일 |
|---|---|---|---|---|
| 계약자 | 플레임 퀸·바람의 척후·조수의 사제·방패병 | 이름·등급(3·4·5성)·진영·역할·기본 수치·스킬 3개(기본·스킬·궁극기)·패시브 1~2개·그림 | 레벨·경험치·돌파 단계·남은 돌파 재료·장비 4칸 | 전투에서 저절로 스킬, 패시브는 지니면 발동 |
| 몬스터 | 슬라임·화염 골렘·보스 바다뱀 | 이름·진영·기본 수치·스킬·패시브 | (전투 중 체력·게이지) | 저절로 행동 |
| 장비 | 불꽃 검·바람 망토 | 이름·칸(무기·갑옷·장신구·유물)·등급·주 능력치·세트 | 강화 단계(0~10), 누가 꼈나 | 끼면 능력치 + 세트 효과 |
| 장비 세트 | 화염 세트·질풍 세트 | 2개 효과·4개 효과 | — | 같은 세트를 2·4개 끼면 |
| 스테이지 | 1-1 … 5-10 | 장·순서·스태미나 비용·적 웨이브·드롭 표·첫 클리어 보상·별 조건 3개 | 별 기록 | 전투 |
| 뽑기 배너 | 상시·픽업(플레임 퀸) | 픽업 대상·확률 표·비용 | — | 뽑기 |
| 재료·아이템 | 경험치 책(소·중·대)·강화석·5성 선택권 | 이름·종류 | 개수 | 쓰기 |
| 일일 임무 | 전투 3번·뽑기 1번·레벨업 1번·소탕 1번·적 30마리·스태미나 60 쓰기 | 조건·보상 | 진행·받음 | 깨면 '받기' |

- 키워드: 등급(3·4·5성), 진영(불·바람·물), 역할(공격·방어·회복·보조), 장비 칸.

#### 2-3. 자원

| 자원 | 판에만 / 남음 | 얻는 곳 | 쓰는 곳 |
|---|---|---|---|
| 스태미나 | 남음 | 6분마다 1(꺼 둔 동안에도), 최대 120. 보상으로는 120 넘게 가질 수 있음(그동안 회복 멈춤). 보석으로 충전(하루 6번, 60씩, 횟수마다 비싸짐) | 스테이지 입장·소탕 |
| 보석 | 남음 | 첫 클리어·임무·도전과제 | 뽑기(1회 160), 스태미나 충전 |
| 골드 | 남음 | 전투·소탕 | 레벨업·장비 강화 |
| 별빛 | 남음 | 돌파가 다 찬 계약자의 중복 | (나중에 별빛 상점) |
| 경험치 책·강화석 | 남음 | 드롭·일일 상자 | 레벨업·강화 |
| 돌파 재료 | 남음(계약자마다) | 중복 계약자 | 돌파 |

#### 2-4. 판이 끝나도 남는 것

- 계약자·장비·재료·재화·스테이지 별·천장 횟수·임무 진행 — 전부 남는다.
- [선택] 슬롯 수: (비움)

### 3. 핵심 동사

| 동사 | 무엇이 바뀌나 | 효과가 고칠 수 있어야 하나 |
|---|---|---|
| 피해 | 대상 체력, 0이면 쓰러짐 | 예(피해 감소·상성) |
| 회피 | 맞기 전에 회피 확률로 빗나감 | 예(필중) |
| 회복 | 체력(최대까지) | 예 |
| 게이지 | 행동 게이지 앞당기기·늦추기 | 예("게이지 감소 면역") |
| 에너지 | 궁극기 에너지 | 예 |
| 쓰러짐 | 전투에서 빠짐 | 예(부활 1회) |
| 뽑기 | 보석 −160, 계약자 하나 | 아니오 |
| 얻기 | 계약자·재료·재화 | 예(나중에 "드롭 +20%") |
| 레벨업 | 경험치 책·골드 → 레벨 | 아니오 |
| 돌파 | 돌파 재료 −1, 단계 +1 | 아니오 |
| 장착 | 장비 칸 | 아니오 |
| 스태미나 내기 | 스태미나 −비용 | 아니오 |

### 4. 효과의 폭

- 언제: [x] 즉시 · [x] 발동(맞으면·쓰러지면·치명타·궁극기를 쓰면·자기 차례가 끝나면·전투 시작) · [ ] 지연 · [x] 지속(N차례 동안 버프·디버프, 중첩)
- 누구에게: [x] 자신 · [ ] 플레이어가 고른 대상(자동이라 없음) · [x] 무작위 · [x] 전체 · [x] 조건으로 거른 대상(체력이 가장 낮은 아군, 맨 앞 적)
- 무엇을: [x] 숫자 바꾸기 · [x] 능력치 보정 · [ ] 만들기·없애기·바꾸기 · [x] 다른 효과 고치기(도발·부활·피해 감소) · [x] 효과 도중 고르기(5성 선택권) · [ ] 비용 바꾸기
- 이상한 효과(셋보다 많다):
  1. 천장: 5성이 89번 안 나오면 90번째는 반드시 5성. 픽업 배너에서 5성이 픽업이 아니면(50%) 다음 5성은 픽업 확정. 4성 이상은 10번마다 확정.
  2. 중복 계약자는 그 계약자의 돌파 재료로 바뀐다. 돌파가 6단계까지 찼으면 별빛(화폐)으로.
  3. 속도 기반 턴 순서: 속도가 높을수록 게이지가 빨리 찬다. "아군 전체 게이지 +30%", "적 하나 게이지 −20%", "속도 +20% 2차례".
  4. 진영 상성: 불 > 바람 > 물 > 불. 유리하면 주는 피해 +30%, 맞는 쪽 회피 −15%.
  5. 궁극기 에너지: 행동하면 +20, 맞으면 +10, 적을 쓰러뜨리면 +20. 100이 되면 다음 자기 차례에 궁극기를 저절로.
  6. 끼어들기(바람의 척후 패시브): 아군이 맞아 체력이 30% 아래로 떨어지면 그 차례가 끝난 직후 끼어들어 때린 적을 반격(전투당 1번).
  7. 장비 세트: 화염 2세트 = 공격력 +18%, 4세트 = 궁극기를 쓴 뒤 속도 +20% 1차례.
  8. 도발(방패병 스킬): 2차례 동안 적의 단일 공격은 도발을 건 쪽을 노린다.
  9. 부활(사제 패시브): 전투당 한 번, 쓰러질 때 체력 30%로 일어난다.
  10. 치명타를 내면 게이지 +10%(척후 패시브).
- [선택] 고리: (비움)

### 5. 문법에 안 맞는 기믹

- 스태미나가 현실 시간 6분마다 1 회복(게임을 꺼 둔 동안에도), 최대 120.
- 일일 임무: 매일 새벽 4시(PC의 현지 시각)에 초기화. 6개 중 5개를 깨고 받으면 보상 상자. 스태미나 충전 횟수도 4시에 초기화.
- 픽업 배너가 현실 날짜로 2주마다 다음 배너로 바뀐다(목록을 순서대로 돈다).

### 6. 진행·화면

- 씬: 타이틀 → 로비 → 전투 → 로비.
- 화면: 로비(재화 줄·메뉴), 계약자 목록·상세(레벨업·돌파·장비), 장비 고르기, 편성(5칸), 지도(장·스테이지), 스테이지 정보(출격·소탕), 전투 HUD(체력·에너지·행동 순서·배속), 결과, 뽑기(배너·천장 횟수·1회/10회·확률 보기), 뽑기 결과(카드 연출·건너뛰기), 확률 공개, 일일 임무, 스태미나 충전.
- [선택] 연출이 끝날 때까지 입력을 막을 곳: (비움)
- [선택] 효과 도중 고르기 창: 5성 선택권(5성 계약자 중 하나).
- [선택] 대화: 없음
- [선택] 칸이 있는 가방: 없음(재료는 개수만)

### 7. 성장·밸런스

- 레벨 1~60, 다음 레벨까지 경험치 = 100 × 레벨^1.6. 기본 수치는 레벨마다 ×1.04.
- 돌파 단계마다 모든 수치 +5%.
- 적 수치는 스테이지 레벨마다 ×1.06.
- 장비 주 능력치는 강화 단계마다 +10%(원래 값 기준), 강화 비용 ×1.3/단계.
- 뽑기: 5성 0.6%, 4성 5.1%, 3성 94.3%. 천장 90(5성)·10(4성 이상). 픽업 5성 50%, 픽업 4성(3명) 50%.
- 드롭: 스테이지마다 표(경험치 책·강화석·장비), 일일 상자 표.
- 풀이 바뀌나: 픽업 배너가 현실 날짜로 바뀜, 상시 표는 업데이트로 계약자가 늘어남.
- 자주 바꿀 숫자: 스태미나 최대·회복 간격, 뽑기 비용·확률·천장, 첫 클리어 보석, 충전 비용.

### 8. 기록·도전과제

- 도전과제(Steam에도): 첫 5성, 천장 도달, 3장 클리어, 계약자 30명, 돌파 6단계.
- 기록: 뽑기 횟수, 전투 승리 수, 쓰러뜨린 적 수.

### 9. 저장·기기

- 저장 시점: 행동마다(뽑기 결과가 날아가면 안 됨).
- 설정 화면 항목: (비움)
- 키 바꾸기: (비움)
- Steam: 클라우드 저장 씀, 리치 프레즌스 "3장 진행 중".

### 10. 글·언어

- 원문 언어: 한국어. 출시 언어: 한국어·영어·일본어.

### 11. 기획자가 직접 하고 싶은 것

- 계약자·몬스터·장비·스테이지·임무의 수치와 스킬·패시브 조합은 DB. 새로운 종류의 효과만 코드.

---

## 2. AI의 질문 목록 (PlanningSpecGuide 1·4절)

빈 [필수] 칸은 없다. 칸끼리 모순도 없다. 질문은 "바꾸기 비싼 데이터 구조·흐름" 둘과 "문법 밖(5절)" 둘이다. 한 번에 보낸다.

**질문 수: 4개** (+ 답을 받은 뒤 시스템 목록 확인 1번)

1. **[흐름] 전투를 행동 몇 개로 나눌까**
   - 정하는 것: 전투 진행의 행동 단위.
   - 추천값: **차례 하나 = 행동 하나**(`AdvanceBattle`). 화면 쪽 자동 진행기가 연출이 끝날 때마다 다음 차례를 보낸다.
   - 이유: 화면은 연출이 끝난 뒤 그 행동의 값으로 다시 그린다(35장 2-2). 전투 전체를 행동 하나로 계산하면 데이터는 이미 끝 상태라 HUD가 연출 동안 이벤트 값으로 따로 그려야 한다. 나중에 수동 궁극기·자동 끄기를 붙여도 행동을 하나 더하면 된다. 모니터 타임라인이 차례로 읽힌다. 전투 규칙 함수·연출·HUD가 모두 이 단위로 짜이므로 나중에 바꾸기 비싸다.
   - 답이 없으면: 추천값.
2. **[데이터] 천장·50% 보장 횟수를 어디까지 나눌까**
   - 정하는 것: 프로필의 천장 데이터 모양(배너마다 / 종류마다).
   - 추천값: **픽업 배너끼리 하나**(배너가 바뀌어도 이어짐), **상시 배너 따로**. 4성 천장도 같은 범위.
   - 이유: 참고 게임과 같고, 5절의 2주 교체에서 횟수가 사라지면 플레이어가 손해를 본다. 프로필 구조(`gacha.pickup`·`gacha.standard`)와 가중치 표 조건의 경로가 이것으로 정해져 출시 뒤에 바꾸면 저장 변환이 든다.
   - 답이 없으면: 추천값.
3. **[문법 밖] 현실 시각(스태미나·4시 초기화·배너 교체)을 어떻게 규칙에 들일까**
   - 무엇이 밖인가: 규칙의 시간은 세션 시계뿐이고(켜져 있는 동안만 흐름, 07장 2.5), 규칙에서 `DateTime`을 쓰지 않으며(관례 3절), 영구 예약은 이벤트 예약만 된다(29장 2-5). "4시가 되면"을 받을 이벤트도 없다(7절 표의 "현실 시간").
   - 방법 (가) **근사 — 추천**: 화면 쪽이 지금 시각(UTC 틱 + 현지 시차)을 행동의 입력 필드로 넣고, 규칙은 "저장한 시각과의 차이"로 정산한다. 만들 것: 행동 `SyncRealTime`, 규칙 함수 `Rules.SettleStamina`·`Rules.EnsureDay`·`Rules.ActiveBanner`, 시각이 필요한 행동(출격·소탕·충전·임무 받기·뽑기)의 시각 필드, 화면 쪽 시계 감시(켜 둔 채 4시·다음 회복 시각을 지나면 `SyncRealTime`을 보냄). 시각이 행동 기록에 남아 같은 결과가 나오고, 정산이 시각 차이로만 계산되니 Undo·이어 하기와도 맞는다. 잃는 것: 시각을 쓰는 행동마다 필드 둘, 행동을 보내지 않는 동안 화면은 같은 순수 함수로 미리 계산해 보여야 한다.
   - 방법 (나) 별도 시스템: 행동 밖(`Update`)에서 시각을 보고 플레이어 데이터를 고침 — 관례 3절 위반(Undo·저장·멀티가 어긋남). 권하지 않는다.
   - 답이 없으면: (가).
4. **[문법 밖] 시각의 출처와 시계 조작**
   - 정하는 것: 어느 시계를 믿고, 시계가 바뀌면 어떻게 할지.
   - 추천값: **PC 시계**. 마지막으로 본 시각보다 시계가 뒤로 가면 정산·초기화를 건너뛴다(회복이 줄거나 날짜가 되돌지 않음). 앞으로 돌린 것은 막지 않는다(오프라인 1인용).
   - 다른 길: Steam 서버 시각(Steamworks.NET `SteamUtils.GetServerRealTime`). GBible Steam 모듈에 이 API가 없어 프로젝트가 직접 부르고, Steam이 꺼져 있으면 PC 시계로 돌아간다.
   - 답이 없으면: 추천값.

**묻지 않고 정해서 알릴 것**(되돌리기 쉽거나 장르 추천값)

| 칸·문제 | 정한 것 | 이유 |
|---|---|---|
| "판"이 없음(1절) | 판 = 전투 한 번(세션 구역 `run`). 나머지는 모두 프로필. 판 끝 이벤트 `BattleEnded` | 3절 데이터 참고 |
| 이어 하기(비움) | 판 저장 행동마다 + 프로필도 행동마다 저장. 전투 중에 꺼도 그 차례부터 이어진다 | 가이드 7장 표의 수집형 추천값 "저장하지 않음"이면 끄는 순간 전투가 사라지고 스태미나만 나간다(문서 부족 4) |
| 되돌리기 없음 | Undo 방식 쓰지 않음 | 명세서 |
| 피해 공식(비움) | 공격력 × 스킬 계수 × (1 + 피해 증폭 합) × 상성 ×1.3(독립 곱) × 치명타 × 100/(100 + 방어) | PlanningSpecGuide 3절 기본 공식 + 상성은 따로 곱 |
| "회피 −15%" | 회피 확률에서 15%p 빼기(0 아래는 0) | 숫자 해석 |
| "게이지 +30%" | 게이지 전체 길이의 30% | 숫자 해석 |
| 중복이 돌파 재료일 때와 별빛일 때의 경계 | (돌파 단계 + 남은 돌파 재료) ≥ 6이면 별빛 | 쓰지 않은 재료가 남아도 7번째 중복은 낭비가 아니게 |
| 끼어들기의 때 | 그 차례가 끝난 직후, 다음 차례를 고르기 전. 끼어든 차례는 게이지를 쓰지 않는다 | 명세서 문장 |
| 웨이브가 바뀔 때 | 아군 게이지·상태 효과는 남고 적은 새로 | 참고 게임 |
| 고리(비움) | 사슬 한도 16. 끼어들기·부활은 전투당 1번이라 고리가 끝난다 | 3절 |
| 슬롯(비움) | 1개 | 3절 |
| 설정(비움) | 기본 5개 + 해상도 + 전투 배속(1×·2×) + 뽑기 연출 건너뛰기 | 3절 + 장르 |
| 키 바꾸기(비움) | 끔(마우스) | 턴제 |
| 연출 막기(비움) | 피해 숫자·쓰러짐·궁극기·뽑기 카드 공개 | 장르 |
| 4시를 넘긴 안 받은 임무 보상 | 사라진다 | 받기 버튼 방식의 보통 규칙 |
| 확률 공개 | 등급별 기본 확률 + 천장 규칙 글 + 등급 안 계약자별 확률 | 9절 |

---

## 3. 시스템 목록 (PlanningSpecGuide 5절)

### 3-1. 데이터 — 구역을 어떻게 나누나

판 개념이 약한 장르라 "판 = 전투 한 번"으로 본다. 라이브러리의 세션 구역 키(`$statuses`·이번 판 `$modifiers`·`$schedule`·`$choices`)가 전투 하나와 수명이 같고, 전투 유닛이 데이터에서 빠지면 그 유닛의 상태 효과·이번 판 Modifier가 저절로 정리된다(29장 2-3, 28장 2-2). 판 끝 이벤트 = 전투 끝이면 판 저장을 지우고 프로필을 바로 저장한다(06장 2.2).

- **`run`(RunState, Session)**: `scene`(""=타이틀 · "Lobby" · "Battle"), `battle`(로비면 `null`): `Battle { stage, wave, turns, allies, enemies, extraTurns(List<string>), result, stars, rewards }`.
- **`profile`(Profile, Meta)**: `gems`·`gold`·`starlight` · `stamina { value, at(DateTime UTC) }` · `materials`(Dictionary<string, int>) · `characters`(List<OwnedCharacter { id, character, level, exp, ascension, shards, gear(List<string> 4칸) }>) · `gear`(List<OwnedGear { id, gear, level, equippedBy }>) · `gacha { pickup, standard }`(PityState { count5, count4, guaranteed }) · `stages`(List<StageRecord { stage, stars }>) · `party`(List<string> 5칸) · `daily { day, claimed(List<string>), boxClaimed, staminaBuys }` · `clock { lastSeenUtc }`(시계 되감김 확인).
- **유닛**: `BattleUnit { id, side, slot, source, owned, level, hp, maxHp, attack, defense, speed, critRate, critDamage, evasion, element, gauge, energy, maxEnergy, cooldown }`. 숫자 필드가 곧 능력치(28장 4-1). 전투 시작 때 프로필의 계약자에서 계산해 넣고(`Rules.BuildUnit`: 원본 × 레벨 곡선 × 돌파 + 장비), 전투 중 버프는 이 객체에 Modifier.
- **id**: 계약자 `Game.NewId("char")`, 장비 `gear`, 전투 유닛 `unit`(번호는 메타 구역, 03장).

### 3-2. 원본 종류

| 원본 | 클래스 · DB | 판 데이터 모양 | 기능 필드 |
|---|---|---|---|
| 계약자 | `Character`(이름 키 `<id>.name`, `[Keyword(typeof(Rarity))] rarity`·`element`·`role`, 기본 수치, `[DataRef(typeof(AbilityData))] basic·skill·ultimate`, `[DataRef(typeof(StatusData))] List<string> passives`) · DB › 인스턴스 | `OwnedCharacter`(원본 ID + 바뀌는 값) | — |
| 몬스터 | `Monster` · 인스턴스 | `BattleUnit` | — |
| 장비 | `Gear { slot, rarity, mainStat, mainValue, set }` · 인스턴스 | `OwnedGear` | — |
| 장비 세트 | `GearSet { twoPiece, fourPiece }`(상태 효과 ID) · 인스턴스 | — | — |
| 스테이지 | `Stage { chapter, order, stamina, waves, dropTable, firstClear, requires }` + 별 조건 3칸(`[SerializeReference, TriggerSlot(Condition)] Trigger star1·star2·star3`) · 인스턴스 | `StageRecord` | — |
| 배너 | `Banner { kind(pickup/standard), pickups, fiveTable, fourTable, threeTable, rotationOrder }` · 인스턴스 | (프로필 `gacha`) | — |
| 재료·아이템 | `Material { kind }` · 인스턴스 | `materials`의 개수 | 선택권만 `[SerializeReference] Usable usable`(16장 1-2) |
| 스킬 | DB › 능력(16장) | — | — |
| 패시브·버프·디버프·세트 효과 | DB › 상태 효과(29장). 패시브 = 전투 시작 때 거는 영구 상태 효과 | `$statuses` | — |
| 일일 임무 | 고유 DB 종류 `DailyMissionData`(3-5) | `daily.claimed` | — |

키워드: `Rarity`(r3·r4·r5) · `Element`(fire·wind·water) · `Role` · `GearSlot`.

### 3-3. 핵심 동사

| 동사 | 규칙 함수 | 의도(필드) | 끝난 뒤 이벤트 |
|---|---|---|---|
| 피해 | `Rules.Damage(game, source, target, coefficient, kind)` | `DamageIntent { sourceId, targetId, amount, kind, element, advantage, isCrit, cancelled }` | `UnitDamaged { sourceId, targetId, amount, hpAfter, kind, advantage, isCrit }` |
| 명중 | `Rules.RollHit` | `HitIntent { sourceId, targetId, chance, advantage, cancelled }` | `AttackMissed { sourceId, targetId }` |
| 회복 | `Rules.Heal` | `HealIntent { sourceId, targetId, amount, cancelled }` | `UnitHealed` |
| 쓰러짐 | `Rules.Kill` | `DeathIntent { killerId, targetId, cancelled }` | `UnitDied { killerId, targetId, side }` |
| 게이지 | `Rules.ShiftGauge(game, source, target, ratio)` | `GaugeIntent { sourceId, targetId, ratio, cancelled }` | `GaugeShifted { targetId, ratio, gauge }` |
| 에너지 | `Rules.GainEnergy` | `EnergyIntent { targetId, amount, reason, cancelled }` | `EnergyGained` |
| 대상 고르기 | `Rules.PickTargets(game, user, ability)` | `TargetIntent { sourceId, targetId, single, cancelled }` | — |
| 차례 | `Rules.NextActor` · `Rules.TakeTurn` · `Rules.InsertExtraTurn` | `ExtraTurnIntent { unitId, reason, cancelled }` | `UnitTurnStarted`·`UnitTurnEnded { unitId, side, extra }` |
| 스킬 사용 | `Game.Use(능력, 유닛, 대상…)`(16장 2-1) | — | `AbilityUsed`(라이브러리) |
| 비용 | `Game.Pay`(28장 2-4) · `Rules.PayStamina(game, n, now)` | `CostIntent`(라이브러리) | `StaminaSpent { amount }` |
| 얻기 | `Rules.Grant(game, itemId, count, reason)` | `GainIntent { itemId, amount, reason, cancelled }` | `ItemGained` |
| 계약자 얻기 | `Rules.GrantCharacter(game, characterId, reason)` | `GainIntent` | `CharacterObtained { characterId, isNew, shards, starlight }` |
| 뽑기 | `Rules.PullOnce(game, banner)` | — | `GachaPulled { banner, characterId, rarity, pickup, pityHit }` |
| 레벨업·돌파·장착 | `Rules.GainExp` · `Rules.Ascend` · `Rules.Equip`·`Unequip` | — | `CharacterLeveled` · `CharacterAscended` · `GearEquipped` |
| 전투 끝 | `Rules.EndBattle(game, win)` | — | `BattleEnded { stage, win, stars, turns }`(판 끝 이벤트) · `StageCleared { stage, stars, first }` |
| 정산·하루 | `Rules.SettleStamina(game, now)` · `Rules.EnsureDay(game, now)` · `Rules.ActiveBanner(game, now)` | — | `DayStarted { day }` |

턴 엔진: 게이지 0~10000. `NextActor`는 `extraTurns` 큐가 있으면 그 유닛, 없으면 유닛마다 (10000 − gauge) ÷ `Game.Attribute(unit, "speed")`가 가장 작은 유닛(같으면 아군 → 칸 번호)을 고르고, 모든 유닛의 게이지를 속도 × 그 시간만큼 채운다. 행동한 유닛은 0. 속도 Modifier는 다음 계산부터 바로 반영된다. 자동 행동 고르기 `Rules.ChooseAction`(에너지 100 → 궁극기, 쿨다운 끝 → 스킬, 아니면 기본 공격).

### 3-4. 진행 흐름

- 씬 흐름: `run` › `scene`, 빈 값 = `Title`. 행동이 필드만 바꾼다(32장 2-6).
- 타이틀: '시작' → 판 저장이 있으면 `GBibleRuntime.LoadRun`(전투 중이었으면 전투로), 없으면 `EnterLobby`. 켤 때 `SyncRealTime`.
- 로비 행동: `SyncRealTime { utcTicks, offsetMinutes }` · `Pull { banner, count, … }` · `LevelUp { character, book, count }` · `Ascend { character }` · `EquipGear { character, gear }` · `UnequipGear` · `EnhanceGear` · `SetParty { slot, character }` · `ClaimDailyMission { mission }` · `ClaimDailyBox` · `BuyStamina` · `UseItem { item }`(선택권 → 선택 대기) · `SweepStage { stage, times }`.
- 전투 행동: `StartBattle { stage }`(스태미나 → 유닛 만들기 → 패시브·세트 상태 효과 걸기 → scene = Battle) → `AdvanceBattle` 되풀이(차례 하나, 웨이브 넘김, 끝이면 `EndBattle` → `BattleEnded`) → 결과 화면 '확인' `LeaveBattle`(battle = null, scene = Lobby). 포기 `RetreatBattle`.
- 반복 단위의 이벤트: `UnitTurnEnded`가 상태 효과의 셀 이벤트(가진 쪽 필드 `unitId`).

### 3-5. 프로젝트 고유 기능

- **일일 임무** `[Database("일일 임무", Group = "진행", IdPrefix = "daily")] DailyMissionData : GameData`: `[SerializeReference, TriggerSlot(TriggerKinds.Condition)] Trigger trigger`(예 `비교(daily.battles >= 3)`) · `[SerializeReference, OutcomeSlot(OutcomeKinds.PlayerData | OutcomeKinds.Coded)] List<Outcome> outcomes`. 감시하지 않고 받기 행동이 `session.Triggers.Test`(11장 6-4)로 확인한 뒤 `RunOutcomes`(17장 2-8, `BoundOutcomes.Has` 확인). 진행도 `Trigger.TryGetProgress`(11장 6-3). 진행은 로그 `daily.*`(범위 누적)이고 `EnsureDay`가 0으로 돌린다.
- **장비 세트**: DB 종류를 만들지 않는다. 인스턴스 `GearSet` + `StartBattle`이 착용 수를 세어 상태 효과를 건다.

### 3-6. 재사용 Block

- 효과(`Effect<BattleUnit>`): `DealDamage { coefficient, kind }` · `HealByAttack { coefficient }` · `HealPercent { ratio }` · `ShiftGauge { ratio }` · `GainEnergy { amount }` · `InsertExtraTurn { ability }`.
- 대상(`TargetSelector<BattleUnit>`): `AllEnemies` · `AllAllies` · `RandomEnemies { count }`(흐름 `battle.ai`) · `FrontEnemy` · `LowestHpAlly` · `Attacker`(이벤트의 `sourceId`) · `DamagedAlly`(이벤트의 `targetId`). 기본 `사용자 자신`.
- 조건(`SimpleCondition`): `DamagedAllyBelow { ratio }` · `SourceIsEnemyOfHolder` · `AbilityIsUltimate` · 별 조건용 `BattleTurnsAtMost { n }` · `NoAllyDown` · `PartyHasElement { element, count }`.
- 결과(`Reward`): `GrantPickedCharacters`(선택권, `Context.Picked`) · `RedirectToHolder`(도발 — 빈틈 6 때문) · `GrantRolledCounts`(드롭을 재료 개수로 — 빈틈 8 때문).

### 3-7. DB 설정

- Bootstrap: Undo 방식 **쓰지 않음** · 판 저장 **행동마다** · 판 끝 이벤트 `BattleEnded` · **프로필도 행동마다 저장 켬**(빈틈 5 우회) · 시작할 때 이어 하기 끔(타이틀이 정함) · 고정 틱 0 · 씬 흐름 `run` › `scene`(빈 값 `Title`) · 개발 빌드 시드 고정 켬(빈틈 11 주의).
- 설정: `masterVolume`·`bgmVolume`·`sfxVolume`·`language`·`fullscreen`·`resolution`(적용 대상), `battleSpeed`(선택지 1·2, 적용 대상 없음 → `GameSettings.Changed`에서 `GameView.PresentationSpeed`, 31장 5-3), `skipGachaAnimation`(켜기/끄기).
- Steam: DB › Steam(App ID 480으로 시작), 도전과제 5개, 로그 → 통계(`pulls`·`wins`), `GameSteam.SetPresence`, 클라우드는 Auto-Cloud 안내(34장).

### 3-8. 선택 기능

Steam 켬. 다이얼로그·인벤토리·입력 끔(대화 없음, 재료는 개수 딕셔너리, 마우스).

### 3-9. 밸런스 값·가중치 표·로그·도전과제 이름

- 밸런스 값: `expToNext`(거듭제곱 100·1.6, 반올림) · `charStatGrowth`(비율로 ×1.04) · `ascensionBonus`(일정하게 +0.05, 시작 단계 0) · `enemyStatScale`(비율로 ×1.06) · `gearMainStatScale`(일정하게 +0.1, 시작 단계 0) · `gearEnhanceCost`(비율로 ×1.3) · `staminaMax`(120) · `staminaIntervalMinutes`(6) · `staminaBuyAmount`(60) · `staminaBuyCost`(표, 횟수마다) · `staminaBuyLimit`(6) · `defeatStaminaCost`(1) · `gacha.cost`(160) · `gacha.rate5`(표: (1, 0.006)·(89, 0.006)·(90, 1)) · `gacha.rate4`(0.051) · `gacha.pity4`(10) · `dupe.starlight.r5`·`r4`·`r3` · `element.advantageDamage`(0.3) · `element.advantageEvasion`(0.15) · `energy.perAction`(20)·`perHit`(10)·`perKill`(20) · `battle.turnLimit`(150) · `daily.resetHour`(4) · `daily.boxNeed`(5) · `banner.rotationDays`(14).
- 가중치 표: `gacha.standard.five`·`.four`·`.three` · 배너마다 `gacha.pickup.<배너>.five`(픽업 1 + 안쪽 표 `gacha.standard.five` 1, 그 줄의 조건 = 보장이 아님)·`.four` · `drop.<스테이지>` · `daily.box` · `select.fiveStar`.
- 로그: `pulls`·`wins`·`kills`(누적), `daily.battles`·`daily.pulls`·`daily.levelUps`·`daily.sweeps`·`daily.kills`·`daily.staminaSpent`(누적, 날마다 0).
- 도전과제: `firstFiveStar`(이벤트 받기 `GachaPulled`, `rarity` = r5) · `pityHit`(`pityHit` 참) · `chapter3Clear` · `collect30`(플레이어 데이터 검사 `characters` 개수 30 이상) · `ascension6`.

### 3-10. 화면 목록

| 화면 | 층 | 보여 주는 것 · 누르는 것 | 기본 화면? |
|---|---|---|---|
| 타이틀 | 바탕(씬 Title) | 시작 · 설정 · 종료 | 설정은 기본 |
| 로비 | 바탕(씬 Lobby) | 재화 줄(스태미나 n/120 + 다음 회복까지, 보석·골드·별빛) · 메뉴 | — |
| 계약자 목록 | 창 | 보유 목록(진영·등급 거르기, 레벨 정렬) | — |
| 계약자 상세 | 창 | 최종 능력치 · 레벨·경험치 · 돌파 n/6·남은 재료 · 스킬·패시브 글 · 장비 4칸 → 레벨업·돌파·장비 칸 | — |
| 장비 고르기 | 팝업 | 그 칸 장비 목록·세트 · 장착·해제·강화 | — |
| 편성 | 창 | 5칸 + 보유 목록 · 진영 분포 | — |
| 지도 | 창 | 장 탭 · 스테이지(별·잠김) | — |
| 스테이지 정보 | 팝업 | 적 진영·드롭 확률(`Chances`)·별 조건·스태미나 → 출격 · 소탕 ×1·×10 | — |
| 전투 HUD | 바탕(씬 Battle) | 아군·적 체력·에너지 · 행동 순서(다음 8명, 순수 함수 `Rules.PredictOrder`) · 웨이브 · 배속 | 일시정지는 기본 |
| 전투 결과 | 팝업 | 승패·별·보상 → 확인 · 다시 | — |
| 뽑기 | 창 | 배너 탭(남은 기간) · 천장 n/90·보장 표시 · 1회·10회 · 확률 보기 | — |
| 뽑기 결과 | 팝업 | 카드 공개 연출(`GameView.Present`) · 새·중복(재료·별빛) · 건너뛰기 | — |
| 확률 공개 | 팝업 | 등급별·계약자별 확률 · 천장 규칙 | — |
| 일일 임무 | 창 | 6개 진행도·받기 · 상자 5/5 · 초기화까지 남은 시간 | — |
| 스태미나 충전 | — | "보석 50으로 60 충전? 오늘 2/6" | **기본 확인 창** `GameScreens.Confirm` |
| 5성 선택권 | — | 5성 계약자 중 하나 | **기본 고르기 창**(선택 대기가 생기면 저절로) |
| 설정 · 일시정지 · 실패 알림 | — | — | **기본 화면**(설정·일시정지·알림) |

### 3-11. 질문으로 정한 것과 추천값으로 정한 것

- 질문: 1 전투 단위 · 2 천장 범위 · 3 현실 시각 방식 · 4 시계 출처.
- 추천값: 2절 표 전부.

---

## 4. 기믹·효과 쪼개기 (PlanningSpecGuide 7절)

### 4-1. 쪼개기 표

| # | 기믹·효과 | 언제 | 누구에게 | 무엇을 — 부품(장·절) | 시스템층에 있어야 할 것 | 판정 |
|---|---|---|---|---|---|---|
| 1 | 천장 90 · 4성 10 | 뽑기 한 번마다(행동 `Pull` 안, **한 번씩 도는 고리**) | — | 5성 확률 = 밸런스 값 `gacha.rate5`의 곡선 '표'(단계 = `count5 + 1`, 30장 2-2·5-3). 4성 = `count4 + 1 ≥ 10`이면 남은 몫 전부. → 코드 풀 `Game.Roll((gacha.pickup.X.five, p5), (….four, p4), (….three, 나머지), stream: "gacha.pickup")`(30장 4-4, 항목이 가중치 표면 그 표에서 다시 뽑음) → 횟수 갱신. `Game.Roll(표, 10)`은 열 번 사이에 횟수가 안 바뀌어 쓸 수 없다(30장 2-3: 조건은 뽑는 순간이고 한 `Roll` 안에서 상태가 그대로) | `Rules.PullOnce`, 프로필 `PityState`, 이벤트 `GachaPulled` | **B** |
| 2 | 50% 빗나가면 다음은 픽업 확정 | 5성이 나온 순간 | — | 표 `gacha.pickup.X.five`: 줄 픽업 계약자 무게 1 + 줄 안쪽 표 `gacha.standard.five` 무게 1, 그 줄의 조건 `플레이어 데이터 검사(profile: gacha.pickup.guaranteed 거짓)`(30장 3-2·5-6) → 보장이면 픽업 줄만 남아 100%. 뽑힌 뒤 `guaranteed = (픽업 아님)`은 1의 함수 | 1과 같음 | **B**(표·조건은 A) |
| 3 | 중복 → 돌파 재료 → 6단계면 별빛 | 계약자를 얻는 모든 곳(뽑기·선택권·보상) | 그 계약자 | `Rules.GrantCharacter`: 없으면 새 `OwnedCharacter`(`Game.NewId("char")`), 있으면 (돌파 + 재료) < 6이면 `shards + 1`, 아니면 `starlight += Balance(dupe.starlight.<등급>)`. `GainIntent`(관례 4절) + `CharacterObtained`. 돌파 행동 `Ascend`는 계약자 객체의 `shards`를 직접 빼고 `Game.Fail`(→ 빈틈 7: `Game.Pay`가 목록 속 객체를 못 가리킴) | 얻기 동사 | **B** |
| 4 | 행동 게이지 +30% (적 −20%) | 즉시(스킬 결과) | `AllAllies`·`사용자 자신`·`FrontEnemy` | 효과 Block `ShiftGauge { ratio }`(16장 2-5) → `Rules.ShiftGauge` → `GaugeIntent`. "게이지 감소 면역" = 상태 효과 트리거 '가로채기'(GaugeIntent, 의도 조건 `ratio < 0`, 가진 쪽 필드 `targetId`) + 의도 고치기 '막기'(28장 3-3·3-4, 29장 3-1) | 턴 엔진(3-3), 게이지 의도·이벤트 | **C** (스킬 하나는 A) |
| 5 | 속도 +20% 2차례 | 지속 | 사용자 자신 등 | 상태 효과 `speedUp`: 능력치 보정 `speed` 퍼센트 0.2(중첩당) · 지속 이벤트 횟수 2 · 셀 이벤트 `UnitTurnEnded` · 가진 쪽 필드 `unitId`(29장 3-1) + 효과 '상태 효과 걸기'(29장 3-2) | 턴 엔진이 최종 속도(`Game.Attribute`)를 매번 읽음 | **A** |
| 6 | 진영 상성(피해 +30%, 회피 −15%) | 피해·명중 계산 | — | `Rules.Advantage(공격, 맞는 쪽)`(규칙 상수 불>바람>물>불) → `DamageIntent.advantage`, 배율 `Balance(element.advantageDamage)`(독립 곱 자리). `Rules.RollHit` → `HitIntent`, 회피 = `Game.Attribute(t, "evasion") − Balance(element.advantageEvasion)`, 난수 `Game.Random("battle.hit")`(07장 2.1). "유리할 때 피해 +10%" = 상태 효과 가로채기(DamageIntent, 의도 조건 `advantage 참`, 가진 쪽 필드 `sourceId`) + 의도 고치기 `amount ×1.1` | 피해·명중 의도의 `advantage`·`element` | **C** (상성 효과 하나는 A) |
| 7 | 스태미나 6분마다 1(꺼 둔 동안에도), 최대 120 | 현실 시간 | — | 프로필 `stamina { value, at }`. `Rules.SettleStamina(game, now)`: n = ⌊(now − at) ÷ 6분⌋, value = min(120, value + n), at += n × 6분(120 이상이면 at = now). 내기 = 정산 → `Game.Pay(profile, "stamina.value", 비용, what: "stamina.name")`(28장 2-4). 보이기 = 화면이 같은 순수 함수 `Rules.StaminaAt(profile, 지금)`을 1초마다(UI Toolkit `schedule`). 켜 둔 동안은 화면 쪽 감시가 다음 회복 시각에 `SyncRealTime`(07장 4.4의 `BuffTicker` 방식) | 지금 시각이 들어올 길(빈틈 1), 회복형 자원(빈틈 4) | **D** (근사하면 B) |
| 8 | 일일 임무 4시 초기화 + 5개면 상자 | 현지 4시 뒤 첫 행동 · 켜 둔 채 4시를 넘긴 순간 | — | `Rules.EnsureDay(game, now)`: day = (현지 시각 − 4시간)의 날짜 번호. 바뀌면 `daily.*` 로그를 `session.Stats.Set(id, 0)`(15장 2-4), `daily.claimed` 비움, 충전 횟수 0, `DayStarted`. 시각을 쓰는 모든 행동이 맨 앞에서 부른다(안 그러면 어제 진행으로 센다). 임무 = 고유 DB 종류(3-5). 상자 = `ClaimDailyBox`: 조건 `daily.claimed` 개수 5 이상 → 표 `daily.box` → 재료 개수라 `GrantRolledCounts`(빈틈 8) | 지금 시각, 날짜 번호(빈틈 2), 임무 종류(빈틈 3) | **D** (+C) |
| 9 | 픽업 배너 2주 교체 | 현실 날짜 | — | `Rules.ActiveBanner(game, now) = rotation[⌊(day − 기준일) ÷ 14⌋ mod n]`. 뽑기 행동이 지금 배너인지 확인하고, 화면 '남은 기간'도 같은 함수 | 지금 시각 | **D** |
| 10 | 궁극기 에너지 → 자동 시전 | 발동(행동·맞음·처치) → 다음 자기 차례 | 사용자 자신 | `Rules.GainEnergy` → `EnergyIntent` → `EnergyGained`. 자기 차례에 에너지 ≥ 최대면 `Game.Use(원본.ultimate, unit, 대상…)` 후 0. "처치하면 +20" = 패시브 상태 효과: 트리거 '이벤트 받기'(UnitDied), 가진 쪽 필드 `killerId` → 효과 `GainEnergy 20` | `Rules.ChooseAction` | **C** (궁극기 하나는 A) |
| 11 | 끼어들기 반격(체력 30% 아래, 전투당 1번) | 발동(아군이 맞은 뒤) | 대상 Block `Attacker`(`Use.Context.Event`의 `sourceId` — 상태 효과의 결과도 컨텍스트를 넘긴다, 코드 확인 `Triggers.cs` `AbilityUse.Context`) | 패시브 상태 효과 `counterReady`(영구, 전투 시작에 건다): 트리거 '이벤트 받기'(UnitDamaged) AND 조건 `DamagedAllyBelow { ratio = 0.3 }`(`Context.User` = 가진 쪽, 11장 4-2) → 효과 `InsertExtraTurn { ability = counter }` + '상태 효과 풀기 counterReady'. 고리: 반격이 다른 아군의 끼어들기를 불러도 각자 1번이라 끝난다(사슬 한도 16) | 추가 차례 큐 `battle.extraTurns` | **C** (끼어들기 하나는 A) |
| 12 | 장비 세트 2·4 | 지속 / 발동(궁극기 뒤) | 착용자 | `StartBattle`이 세트별 착용 수를 세어 `Game.ApplyStatus(unit, set.twoPiece)`(29장 4-1). 2세트 = 상태 효과 능력치 보정 `attack` 퍼센트 0.18. 4세트 = 상태 효과 트리거 '이벤트 받기'(능력 사용 `AbilityUsed`, 가진 쪽 필드 `User`) AND `AbilityIsUltimate` → '상태 효과 걸기 speedUp'(지속 1) | 전투 시작에 거는 규칙 | **C** (세트 하나는 A) |
| 13 | 도발 2차례 | 지속 | 도발을 건 쪽 | `Rules.PickTargets` → `TargetIntent`. 상태 효과 `taunt`: 트리거 '가로채기'(TargetIntent, 의도 조건 `single 참`) AND `SourceIsEnemyOfHolder` → 의도 `targetId`를 **가진 쪽 id로** 바꿔야 하는데 '의도 고치기'의 값은 고정 글자뿐(빈틈 6, 코드 확인 `Intercepts.cs` `ModifyIntentOutcome`) → 결과 Block `RedirectToHolder` | 대상 고르기 의도 | **C** (도발 하나는 B → 빈틈 6이 메워지면 A) |
| 14 | 부활(전투당 1번, 체력 30%) | 발동(쓰러지기 직전) | 가진 쪽 | 상태 효과 `revive`(영구): 트리거 '가로채기'(DeathIntent), 가진 쪽 필드 `targetId` → 의도 고치기 '막기' + 효과 `HealPercent 0.3`(사용자 자신) + '상태 효과 풀기 revive' | 죽음 의도 | **A** |
| 15 | 치명타면 게이지 +10% | 발동 | 사용자 자신 | 패시브 상태 효과: 트리거 '이벤트 받기'(UnitDamaged, 필드 검사 `isCrit 참`), 가진 쪽 필드 `sourceId` → 효과 `ShiftGauge 0.1` | 피해 이벤트의 `isCrit`·`sourceId` | **A** |
| 16 | 소탕 ×1·×10 | 즉시 | — | `SweepStage { stage, times }`: 별 3 확인 → `Rules.PayStamina(비용 × times)` → times번 `Game.Roll(stage.dropTable, 1)`(표 흐름 `table:<ID>`) → `Rules.Grant` | 얻기 동사 | **B** |
| 17 | 5성 선택권 | 즉시(쓰기) | — | 재료 `Material.usable` → 결과 '가중치 표에서 고르기'(표 `select.fiveStar`, `key` 비움, 고른 뒤 결과 = `GrantPickedCharacters`, 30장 3-3·5-5) → 기본 고르기 창. 선택권 개수(딕셔너리)를 빼는 것은 '비용 내기'로 안 돼 행동 `UseItem`이 직접(빈틈 7) | 얻기 동사 | **B** |
| 18 | 스태미나 충전 하루 6번 | 즉시 | — | `BuyStamina`: `EnsureDay` → `daily.staminaBuys < 6` → `Game.Pay(profile, "gems", Balance(staminaBuyCost, 횟수 + 1), what: "gems.name")` → `stamina.value += 60`(상한을 넘어도 됨). `GameScreens.Confirm` | 지금 시각 | **B** (+D 공유) |

**판정 합계**: A 3(5·14·15) · B 6(1·2·3·16·17·18) · **C 6**(4·6·10·11·12·13) · **D 3**(7·8·9).

- C 여섯은 모두 시스템층이 하루 안에 만들 몫(전투 동사의 의도·이벤트·턴 엔진)이고, 만들고 나면 같은 종류의 효과는 A다. 7절의 "시스템층에 있어야 할 것" 칸이 그대로 시스템 목록이 된다.
- D 셋은 한 가지 원인(규칙에 현실 시각이 없음)이다. 질문 3·4로 근사하면 B로 내려가지만, 장르마다 같은 것을 다시 만든다(빈틈 1~4).

### 4-2. 현실 시각 · 결정론 난수 · Undo · 저장

| 일 | 현실 시각 | 난수 | Undo(쓰지 않음) | 저장 |
|---|---|---|---|---|
| 게임을 켬 | `SyncRealTime`이 정산·초기화. 시계가 `lastSeenUtc`보다 뒤면 건너뜀 | 마스터 시드 새로(출시 = 지금 시각, 개발 = 고정) | — | 프로필 불러옴. 판 저장은 타이틀이 `LoadRun` |
| 10연차 | 뽑기 행동의 시각 필드로 지금 배너 확인 | 등급 `pool:gacha.pickup`, 등급 안 `table:<표 ID>`(표마다 저절로, 30장 2-3) | 보석이 모자라면 `GameFailure` → 바꾼 것과 **난수까지** 행동 직전으로(07장 3절) → 다시 눌러도 같은 순서 | 프레임 끝 프로필 저장(프로필도 행동마다) |
| 뽑기 직후 강제 종료 | — | 프로필에는 난수 상태가 없다(07장 3절 표) → 다음 실행은 새 시드 | — | 그 프레임 끝 저장 전에 죽으면 뽑기가 사라지고 다시 뽑을 수 있다(아주 짧은 틈). 개발 빌드 시드 고정이면 켤 때마다 같은 뽑기 순서(빈틈 11) |
| 전투 중 강제 종료 | — | run.json에 마스터 시드·모든 흐름 상태(06장 2.4) → 이어 하면 다음 명중·치명타까지 같다(끄고 켜서 다시 뽑기 불가) | — | 판 저장 행동마다. 프로필도 행동마다를 켜야 스태미나·`$id.*` 번호가 판과 맞는다(빈틈 5) |
| 4시를 켜 둔 채 넘김 | 화면 쪽 감시 → `SyncRealTime` → `EnsureDay` | — | — | 프로필 저장 |
| Undo를 켰다면(개발 중 모니터) | 정산은 "저장한 시각과의 차이"라 Undo 뒤 새 시각으로 다시 해도 같은 총량(멱등) | 뽑기를 Undo하고 다시 뽑아도 같은 결과 | 행동 단위 | Undo 뒤에도 저장 |
| 시계를 앞으로 돌림 | 스태미나·초기화 이득 — 막지 않음(질문 4) | — | — | — |
| 시계를 뒤로 돌림 | `lastSeenUtc`보다 이르면 정산·초기화 건너뜀, 날짜 번호는 줄지 않음 | — | — | — |

- 난수 흐름: `pool:gacha.pickup`·`pool:gacha.standard`(등급), `table:gacha.*`(등급 안, 표마다), `table:drop.*`·`table:daily.box`, `battle.hit`·`battle.crit`·`battle.ai`. 가이드 7장 수집형의 `gacha` 흐름은 직접 `Game.Random("gacha")`일 때만 생긴다(문서 오류 3).
- 리플레이·기록: 지금 방식(질문 3의 가)에서는 시각이 행동 필드라 기록에 남는다. 빈틈 1의 `Game.Now`도 같은 성질을 지켜야 한다(읽은 값을 기록).

---

## 5. 빈틈 목록

### 5-1. 기능 빈틈

**1. 규칙 안에서 쓸 현실 시각이 없다**
- 무엇: 규칙은 `DateTime` 금지(관례 3절), 세션 시계는 켜져 있는 동안만(07장 2.5), 영구 예약은 이벤트 예약뿐(29장 2-5). 지금 시각을 행동에 넣는 정해진 길이 없다(코드 확인: `SessionOptions`·`GameApi`에 현실 시계 없음).
- 왜·얼마나 자주: 이 장르의 D 셋이 모두 여기서 온다. 시각을 쓰는 행동마다 AI가 시각 필드를 만들고 화면 쪽에서 채우는 도우미를 만든다. 한 행동이라도 빠뜨리면 그 행동의 정산이 어긋난다.
- 다른 장르: 방치형(오프라인 보상), 로그라이크 일일 도전, 기간 이벤트·우편 만료가 있는 모든 게임.
- 제안(이름 가안 — 확인 필요): `SessionOptions.RealClock`(`Func<DateTimeOffset>`) + `Game.Now`. 행동 안에서 처음 읽은 값을 행동 기록에 남기고 그 행동 안에서는 같은 값을 준다(리플레이·검증이 같은 시각). Unity는 DB › Bootstrap '현실 시각 출처'(PC 시계 / Steam 서버 시각 — Steam 기능이 켜졌을 때 `SteamUtils.GetServerRealTime`, 꺼지면 PC 시계). 테스트는 고정 시계. 가이드 7장 방치형·매뉴얼 07장에 "꺼져 있던 시간 = `Game.Now` − 저장한 시각".
- 3요소: ① 행동마다 필드를 두지 않아 쉬워진다 ② 멀티에서는 서버가 시각을 정해 클라이언트 패킷은 그대로, 기록에는 읽은 행동만 8바이트 ③ 리플렉션 없음.
- 지금 우회: 행동 필드 `utcTicks`·`offsetMinutes` + 화면 쪽 도우미.

**2. 주기(매일·매주) 경계와 날이 바뀌는 순간이 없다**
- 무엇: "현지 4시 기준 날짜 번호", "다음 초기화까지 남은 시간", "날이 바뀌면 0으로 돌아가는 로그"를 만들 부품이 없다. 로그 범위는 이번 판·누적뿐(15장 1-1, 코드 확인 `StatScope`). 날이 바뀐 것을 받을 이벤트도 없어 시각을 쓰는 행동마다 `EnsureDay`를 먼저 불러야 한다.
- 다른 장르: 방치형·로그라이크 일일 도전·주간 보스·시즌.
- 제안(이름 가안): 코어 도우미 `GameCalendar.DayIndex(now, resetHour)`·`WeekIndex(now, startDay, resetHour)`·`NextReset(now, resetHour)`, DB › Bootstrap '하루 시작 시각'. 1의 `Game.Now`로 날이 바뀐 뒤 첫 행동에서 라이브러리가 `DayStarted { day }`를 쏜다 → 로그 범위 '매일'·'매주'(그 이벤트를 받으면 0), 고유 기능·도전과제도 그 이벤트를 받는다.
- 3요소: ① DB 칸·이벤트 하나 ② 이벤트는 행동 안 ③ 없음.

**3. 반복·주기 임무의 기본 종류가 없다**
- 무엇: 도전과제는 한 번·자동 지급(14장 2-3), 로그는 숫자뿐. "진행 조건 + 받기 버튼 + 날마다 초기화 + N개 받으면 상자"는 매번 고유 DB 종류(17장)로 만든다. 17장 4-1의 '일일 미션' 예는 하루 초기화가 없다(문서 오류 5).
- 다른 장르: 메타 성장이 있는 거의 모든 게임(주간 임무·시즌 패스·튜토리얼 단계).
- 제안(이름 가안): 선택 기능 '임무' — DB 종류(진행 조건 · 결과 · 주기 없음/매일/매주 · 받는 방식 자동/버튼 · 묶음 보상), 기본 행동 `ClaimMission`, 받은 상태는 메타 구역, 초기화는 2의 이벤트.
- 3요소: ① DB만 ② 해당 없음 ③ 등록은 시작 때.

**4. 회복형 자원(N분마다 1, 최대 M) 부품이 없다**
- 무엇: 값 + 마지막 회복 시각, 정산, 상한 넘김(보상으로 120 이상), 낼 때 먼저 정산, 화면에는 정산한 값. binding-path는 저장된 값만 보여 실제 값과 어긋난다.
- 다른 장르: 모바일식 하트·방치형 생산·시간 상자.
- 제안(이름 가안): 코어 데이터 클래스 `Regen { value, at }` + 순수 함수 `Regen.ValueAt(now, interval, max)` + `Game.Settle(regen, interval, max)`. `Game.Pay`는 경로 끝이 `Regen`이면 정산 뒤 낸다. binding-path는 `Regen`을 정산한 값으로 보인다(13의 다시 그리기 간격과 함께).
- 3요소: ① 필드 형식 하나 — 사용자 값을 감싸는 형식이 아니라 인벤토리 `Inventory`처럼 값 자체 ② 정산은 행동 안 ③ 없음.

**5. 판 저장과 프로필 저장이 어긋날 수 있다 (코드 확인)**
- 무엇: `RunSaver.FrameEnd`는 판 저장 '행동마다'면 판을 저장하지만 프로필은 '프로필도 행동마다 저장'을 켰을 때만 저장한다(기본 끔). 강제 종료·정전 뒤에는 run.json이 profile.json보다 새롭다 → (가) 전투를 시작하며 낸 스태미나(프로필)는 돌아오고 전투(판)는 남는다 (나) `Game.NewId` 번호(`$id.*`, 메타 구역 — `GameApi.NewId`)가 되돌아가 이어 한 판 안의 id와 새 id가 겹칠 수 있다.
- 다른 장르: 판 저장 '행동마다'를 쓰는 모든 장르. 카드 던전도(프로필 '끌 때' + 적·카드 `NewId`).
- 제안: `RunSaver`가 바깥 행동이 메타 구역 키를 바꿨을 때 프로필도 저장(`Data.Changed` + `ZoneOf`로 구역별 표시). 메타를 안 바꾸는 행동(전투 차례)에서는 프로필을 쓰지 않으니 지금의 '프로필도 행동마다'보다 디스크 쓰기도 준다. 더해 `$id.*`를 판 저장에도 적고 불러올 때 큰 값을 쓴다.
- 3요소: ① Bootstrap 칸이 하나 줄어든다 ② 해당 없음 ③ 없음.
- 지금 우회: '프로필도 행동마다 저장' 켬(두 파일 사이의 아주 짧은 틈은 남는다).

**6. '의도 고치기'의 값이 고정 글자다 (코드 확인)**
- 무엇: `ModifyIntentOutcome.value`는 `TryParse`한 고정 값뿐이라 "targetId = 가진 쪽 id"(도발·대신 맞기), "amount × (1 − 0.05 × 중첩)", "밸런스 값만큼"을 DB로 쓸 수 없다. 전투 유닛 id는 실행 중에 생겨(`unit37`) 글자로 적을 수도 없다.
- 왜: 도발·대신 맞기·중첩 비례 감소는 RPG·전술·카드에 흔하다. PlanningSpecGuide 7절·참조 2-2는 DB로 된다고 적었다(문서 오류 1).
- 제안: 값 칸에 `{user}`(가진 쪽·사용자 id) · `{owner}` · `{stacks}`(중첩) · `{balance:ID}`를 허용(결과를 등록할 때 한 번 나눔). 같은 규칙을 '이벤트 쏘기'의 값·'플레이어 데이터에 주기'의 양에도.
- 3요소: ① DB만 ② 해당 없음 ③ 없음.
- 지금 우회: 결과 Block `RedirectToHolder`(B).

**7. 객체 하나의 숫자·딕셔너리 키를 경로로 가리킬 수 없다 (코드 확인)**
- 무엇: 필드 경로에 딕셔너리 키가 없다(11장 3-1, `FieldPaths`는 딕셔너리를 값 목록으로만). `Game.Pay(key, path)`는 뿌리 키에서의 경로라 목록 속 특정 객체(계약자의 `shards`)도, 재료 딕셔너리의 한 칸(`materials`의 `expBook`)도 낼 수 없다. 같은 이유로 DB 조건 "경험치 책 10개 이상"·binding-path로 재료 한 칸 보이기가 안 된다.
- 왜: 레벨업·강화·돌파·선택권이 모두 재료 비용이다 → 직접 빼고 `Game.Fail`을 쓰면 비용 가로채기("강화 비용 −20%")를 건너뛴다.
- 다른 장르: 제작·RPG·방치형(재료가 많은 모든 게임), 전술(유닛마다 마나).
- 제안: 경로 문법 `materials[expBook]`(조건·비용·binding-path·플레이어 데이터에 주기 공통) + `Game.Pay(object entity, string field, double amount, string what)` 오버로드(`CostIntent`에 `targetId`).
- 3요소: ① 쉬워진다 ② 해당 없음 ③ 경로는 지금처럼 처음 한 번 나눠 둔다.

**8. 결과 '가중치 표에서 주기'가 개수(딕셔너리)·인벤토리에 넣지 못한다 (코드 확인)**
- 무엇: `RollTableOutcome`은 뽑은 ID를 `PlayerDataReward.items`로 목록에만 넣는다. 드롭이 "경험치 책 ×2"처럼 개수면 코드.
- 다른 장르: RPG·제작·로그라이트 재료.
- 제안: 넣을 곳이 `Dictionary<string, 숫자>`면 키 = 뽑은 ID, 값 += 개수 / 인벤토리 필드면 `Add`. '가중치 표에서 고르기'도 같게.
- 3요소: 모두 해당 없음.

**9. 뽑기 천장(횟수에 따른 확률 상승·확정)을 표만으로 못 쓴다**
- 무엇: 줄 무게가 고정 숫자다. "N번째부터 확률 상승·확정"은 밸런스 곡선 + 코드 풀로 짠다. `session.Tables.Chances`는 코드 풀·곡선을 모른다(30장 2-3).
- 다른 장르: 로그라이크의 희귀 보상 보정(안 나올수록 희귀 확률이 오름), 드롭 보정.
- 제안: 가중치 표 줄에 '무게를 밸런스 값에서'(`weightBalance` + 단계 = 플레이어 데이터의 숫자 경로) → 확률 상승·확정을 DB로. `Chances(풀)` 코드 풀 오버로드(안쪽 표를 곱해 펼침). 횟수 갱신은 그대로 코드(B).
- 3요소: 모두 해당 없음.

**10. 계산된 기본값을 능력치로 쓸 길이 없다 (코드 확인)**
- 무엇: 능력치 = 객체의 숫자 필드(28장 4-1). 계약자의 공격력은 원본 × 레벨 곡선 × 돌파라 필드가 없다. 영구 Modifier(장비)를 계약자에 붙이면 `AttributeSystem.Add`가 대상을 찾은 뒤 그 숫자 필드를 검사해 예외를 낸다 → 덧없는 필드를 두거나(값이 낡는다), 장비 합산을 `AttributeSystem.Apply`로 따로 계산한다.
- 다른 장르: 레벨제 RPG 전부, 로그라이트 영구 강화.
- 제안(이름 가안): 클래스에 붙이는 `[Attributes("attack", "defense", …)]`(계산형 능력치 이름 선언) + `Game.Attribute(entity, name, baseValue)` 오버로드. 28장에 "원본 × 레벨 곡선" 예.
- 3요소: ① 어트리뷰트 ② 해당 없음 ③ 시작 때 한 번 모은다.

**11. 메타 중심 게임의 난수 상태가 저장되지 않는다**
- 무엇: 난수 상태는 판 저장에만 들어간다(07장 3절 표 "프로필: 저장하지 않는다"). 뽑기가 판 밖(로비)에서 일어나면 켤 때마다 마스터 시드가 바뀐다. 개발 빌드 시드 고정이면 켤 때마다 같은 뽑기 순서가 되풀이되어 시험하는 사람을 속인다.
- 다른 장르: 방치형·로그라이트 메타 상점.
- 제안: DB › Bootstrap '프로필에 저장할 난수 흐름'(이름·접두어 목록, 예 `table:gacha.`·`pool:gacha.`) → 메타 키 `$random.meta`.
- 3요소: 모두 해당 없음.

**12. 자동 진행(연출이 끝나면 다음 행동) 부품이 없다**
- 무엇: 자동 전투는 "연출·선택 대기·일시정지가 아니면 다음 차례 행동"을 화면 쪽이 계속 보내야 한다. AI가 매번 `Update` 감시를 쓴다.
- 다른 장르: 오토 배틀러·방치형·턴제의 적 차례 재생·리플레이.
- 제안(이름 가안): `GameView.Autoplay(Func<GameAction> next, Func<bool> active)` — `GameView.Busy`·`GameTime.IsPaused`가 아니면 `GameScreens.Do(next)`.
- 3요소: ① 함수 하나 ② 행동은 그대로 ③ 없음.

**13. 목록 칸 안의 binding-path · 시간에 따라 바뀌는 칸**
- 무엇: 목록 칸 UXML의 binding-path는 읽지 않는다(35장 2-2). 계약자·장비·임무·스테이지·배너 목록마다 bind 코드로 이름·레벨·등급을 채운다. 남은 시간(회복·초기화·배너)은 데이터가 안 바뀌어 다시 그려지지 않는다.
- 다른 장르: 목록이 있는 모든 화면(상점·인벤토리·손패).
- 제안: 목록 칸에서 항목 기준 경로 `item:level`·`item:character@<틀 키>`. DB › 화면 칸 '다시 그리기 간격(초)'.
- 3요소: ③ 칸을 만들 때 경로를 한 번 나눈다.

**작은 것**
- 개발 빌드에서만 Undo: Undo '쓰지 않음' 게임은 모니터에서 뽑기·정산을 되돌려 볼 수 없다 → Bootstrap '개발 빌드 Undo 방식'(시드 고정 칸과 같은 모양).
- 대상 고르기 의도: 도발·은신을 붙이려면 대상 고르기에 가로채기 자리가 있어야 하는데, 관례 4절·PlanningSpecGuide 3절의 기본 동사 목록에 없다(문서 부족 11).

### 5-2. 문서 오류·부족

| # | 어디 | 무엇 | 고칠 것 |
|---|---|---|---|
| 1 | `PlanningSpecGuide.md` 7절 '대신 맞기' 줄 · `Reference.md` 2-2 | "의도 고치기 `targetId` 값으로 바꾸기(자기 id)" — 값 칸이 고정 글자라 DB로 안 된다(코드 확인) | 빈틈 6을 넣거나, 그때까지 "프로젝트 결과 Block(B)"으로 |
| 2 | 가이드 07장 6절 수집형 | "뽑기 확률은 가중치 표(`Game.Roll(표, 10)`)" — 천장이 있으면 열 번 사이에 횟수가 안 바뀌어 틀린다 | "천장이 있으면 한 번씩 뽑는 고리 + 밸런스 곡선 + 코드 풀" |
| 3 | 가이드 07장 6절 수집형 | "뽑기는 `gacha` 흐름" — `Game.Roll(표)`는 `table:<ID>`, 코드 풀은 `pool:<stream>`을 저절로 쓴다 | 흐름 이름을 30장 2-3·4-4에 맞춤 |
| 4 | 가이드 07장 1절 표 수집형 · `PlanningSpecGuide.md` 2절 '1 이어 하기' | 추천값 "판 저장하지 않음" — 전투가 있는 수집형은 끄는 순간 전투가 사라지고 스태미나만 나간다 | "전투가 있으면 행동마다 + 프로필도 행동마다, 판 = 전투 한 번" |
| 5 | 매뉴얼 17장 4-1 | '일일 미션' 예가 세션 구역 `daily.log` + `maxPerRun`이라 하루가 아니라 판마다 한도다 | 이름을 '판마다 한도 미션'으로 바꾸거나 날짜 초기화 예(빈틈 1·2) |
| 6 | 가이드 07장 6절 방치형 | "정산 시각을 `DateTime` 필드로 … 접속할 때 보상 받기 행동이" — 지금 시각을 어디서 얻는지 없고 관례 3절(규칙에서 `DateTime` 금지)과 맞물리지 않는다 | "지금 시각은 행동의 입력 필드로(빈틈 1이 생기면 `Game.Now`)" |
| 7 | `Reference.md` 4절 Bootstrap 줄 · 2-1 '판을 이어 하기·저장' 줄 | '프로필도 행동마다 저장'이 없다(가이드 07·10장에만) — 수집형의 핵심 칸이라 AI가 놓친다(찾기 어려움) | 두 줄에 더하고, `PlanningSpecGuide.md` 2절 '9 저장 시점'에 "진행이 프로필이면 프로필도 행동마다" |
| 8 | 매뉴얼 06장 2.2 · 가이드 10장 2절 | 판 '행동마다' + 프로필 '끌 때' 조합이 강제 종료에서 어긋난다는 주의가 없다(빈틈 5) | 주의 한 줄 |
| 9 | 가이드 10장 2절 | "난수 상태도 함께 저장되므로 껐다 켜서 결과를 다시 뽑을 수 없다" — 판 밖(로비) 뽑기에는 맞지 않는다(07장 3절 표) | "판 밖 뽑기는 프로필도 행동마다 저장으로 막는다(빈틈 11)" |
| 10 | `PlanningSpec.md` 1절 · `PlanningSpecGuide.md` 2절 | 양식이 '한 판'을 전제한다. 판 밖에서 하는 일(뽑기·성장·편성·소탕)을 적을 칸이 없어 기획자가 "판 없음"이라고 쓴다 | 양식에 "1-2. 판 밖에서 하는 일(로비·메타)", 가이드 2절에 "판 밖 행동 → 프로필을 바꾸는 행동 목록, 판이 약하면 판 = 한 전투" |
| 11 | `PlanningSpecGuide.md` 2절 '2-2 지니면 발동' · 3절 기본 동사 | 유닛의 패시브는 "전투 시작에 거는 영구 상태 효과(가진 쪽 필드)"가 고유 DB 종류보다 가볍다는 말이 없다. 자동 전투·전술의 '대상 고르기'·'차례(게이지)' 동사가 기본 동사에 없다 | 두 줄 더함 |
| 12 | 매뉴얼 28장 | 레벨 곡선으로 계산되는 기본값·영구 Modifier가 대상 객체의 필드 검사를 받는다는 점과 예가 없다(빈틈 10) | 5절 예 하나 |
| 13 | `Reference.md` 2-3 '확률 공개' | `Chances`가 코드 풀·천장을 반영하지 않는다는 말이 없다(30장 2-3에만) | 한 줄 |

### 5-3. 확인 못 한 가정

- 장비가 천 개를 넘는 큰 프로필에서 매 프레임 Sync(32장·02장의 "매 프레임 끝") 비용 — 코드만 읽었고 재지 않았다.
- 인스턴스 값 안의 `Trigger` 필드(스테이지 별 조건 세 칸)가 DB 편집기로 그려진다 — 17장 5절 문장이 근거, Unity에서 보지 않았다.
- 행동 안의 `session.Stats.Set`(일일 로그를 0으로) — Undo를 쓰지 않으니 이 게임에는 영향이 없다고 보았다.

---

## 6. 하루 안에 되나

**판단: 시스템층은 된다. 화면까지 하루는 빠듯하다(조건부).**

- 시스템층이 되는 이유: 기믹 18개 중 문법 밖은 한 원인(현실 시각)뿐이고, 나머지는 있는 부품 — 상태 효과(가진 쪽 필드·이벤트 횟수 지속·가로채기 트리거), 가로채기 + 의도, 능력, 가중치 표(조건 줄·안쪽 표·코드 풀), 밸런스 곡선(표·거듭제곱·비율), 고유 기능, 로그·도전과제, 설정·Steam — 으로 조립된다. 질문은 4개다. 직접 짜는 것은 장르 고유의 턴 게이지 엔진·뽑기 함수·성장 계산과, 빈틈을 메우는 코드(시각 필드·날짜 번호·회복형 자원·자동 진행·재료 비용)다.
- 화면이 빠듯한 이유: 이 장르의 화면은 목록이 중심인데 목록 칸마다 bind 코드를 쓴다(빈틈 13). 남은 시간 표시처럼 시각에 따라 바뀌는 칸을 직접 갱신한다(빈틈 4·13). 전투 HUD의 행동 순서 막대·궁극기·카드 공개 연출은 장르 고유다. 기본 화면(확인·고르기·설정·일시정지·알림)이 덜어 주는 것은 작은 창들뿐이다.
- 하루 안으로 당기는 것: 빈틈 1·2·4(현실 시각·주기·회복형 자원), 12(자동 진행), 13(목록 칸 binding-path). 이 다섯이 들어가면 D 셋이 B 이하로 내려가고 화면 코드의 대부분이 UXML로 옮겨 간다.
- 하루 안에 드러나지 않는 위험: 빈틈 5(저장 어긋남·id 겹침)는 시험 플레이에서 보이지 않고 출시 뒤 강제 종료에서 나타난다. 이 프로젝트는 '프로필도 행동마다 저장'으로 피하지만, 라이브러리에서 고치는 것이 맞다.

## 보강 뒤(2026-10-04)

이 시험의 빈틈을 받아 GBible을 고친 결과다. 구현 = 라이브러리에 들어감(매뉴얼 장), 문서 = 문서·관례를 고침, 보류 = 아직 없음(이유와 지금의 우회).

| 빈틈 | 처리 |
|---|---|
| 1 현실 시각 | 구현: `Game.Now`(행동에 기록)·`SessionOptions.RealClock`(07장) |
| 2 날짜 경계 | 구현: `Game.Day(초기화 시각)`·`Week`. 보류: `DayStarted` 이벤트·로그 범위 '매일' |
| 3 반복·주기 임무 종류 | 보류. 도전과제·로그 + `Game.Day` |
| 4 회복형 자원 | 문서(패턴): 값·시각 두 필드 + 쓰기 전 정산(가이드 07장 수집형) |
| 5 판·프로필 저장 어긋남 | 구현(고침): 판을 저장할 때 메타 구역이 바뀌었으면 프로필도 |
| 6 의도 고치기 값 틀 | 구현: `{user}`·`{owner}`·`{stacks}`·`{balance:ID}`(28장) |
| 7 딕셔너리·목록 하나 가리키기 | 구현: 경로 `name[키]`(11·12·28장) |
| 8 가중치 표에서 주기 → 딕셔너리 | 구현: 딕셔너리면 개수 +1(30장) |
| 9 천장 | 구현: 줄 '무게를 밸런스 값에서'(30장). 보류: 코드 풀의 Chances |
| 10 계산되는 기본 능력치 | 보류 |
| 11 메타 중심 게임의 난수 상태 | 문서: 로비 뽑기는 '프로필도 행동마다 저장'(가이드 10장). 보류: 프로필에 저장할 난수 흐름 |
| 12 자동 진행 | 보류 |
| 13 목록 칸 binding-path·남은 시간 | 구현(일부): `GameScreen.Tick`. 보류: 항목 기준 경로 |
| 문서 오류 1~13 | 문서: 대신 맞기(틀로 됨), 천장과 10연차, 난수 흐름 이름, 수집형 판 저장 추천, 일일 미션, DateTime → Game.Now, 판 밖에서 하는 일 칸 |

문법 밖: 0. 스태미나·4시 초기화·배너 교체 = `Game.Now`·`Game.Day`로 행동 안에서 정산.
