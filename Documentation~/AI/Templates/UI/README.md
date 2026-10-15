# UI 키트 (AI용 템플릿)

모든 프로젝트의 화면을 이 키트로 짠다. **프리팹이 아니다** — UI Toolkit의 USS · UXML · 코드 틀이고, AI가 프로젝트에 복사해 쓴다. 디자인 규칙은 `../../UiGuide.md` 3절, 문법 · 레이아웃은 1 · 2절. 여기는 **복사해 쓰는 조각과 견본**이다.

- 2026-10-13 새로 만들었다: 실험실(`D:\Unity\Projects\UITemplateLab`)에서 개발자가 승인한 것만 옮겼고, 그 전의 키트(배치 A~H · 형식 A~D · 아치 · 비스듬한 칸 · 결 텍스처 재질)는 버렸다.
- **키트에는 범용 기능만 둔다**(개발자 2026-10-13): 구성 요소의 기능 · 구조(코드 · 클래스)만 키트에. **모양 · 색은 언제든 바꿀 수 있게** — 색은 `Kit.uss` 맨 위 변수(`--kit-…`, 연출 빛은 `--kit-fx-…`)에서 한 번에, 시간 · 크기 · 모서리는 각 요소의 속성 · 함수 매개변수로. 특정 게임의 모양(예: 검 실루엣)은 넣지 않는다 — 아이콘 자리는 무엇이든(진짜 그림 · `KitIcon`). 견본 화면 전용 모양은 `Samples/Samples.uss`(키트가 아니다).
- 패널 면은 **단색 + 아주 옅은 폴리곤 조각** — 그림 파일 없이 코드 `KitPanel`이 그린다. 모양은 4가지(직각 · 약간 둥근 · 모서리 자름 · 알약), 변칙은 선 · 둥글기 · 각으로만.
- 뒷배경은 **DB › 화면의 '뒷배경' 칸**(일반 배경 · 블러 = 알림만 · UI 배경)이 깐다 — 화면 UXML에 흐림 · 어둡게 막을 직접 넣지 않는다. UI 배경의 옅은 조각만 화면 UXML 맨 아래에 `KitPanel`(`kit-pn-faint`)로.
- 움직임은 GBible이 맡는다(화면 열고 닫기 · `momoi-motion` · `ScreenMotion.Show` — 나타남 0.189초 · 사라짐 0.135초, UiGuide 3-5). 버튼 · 칸의 올림 · 누름은 키트 USS에 있다.
- 글꼴: DB › 화면 설정의 '글꼴 에셋'(TextCore Font Asset — 굵기 표에 Bold를 이어 둔 것)을 쓴다. 한글 고딕(예: Pretendard, OFL)은 개발자가 승인하면 AI가 받아 `Assets/Dev/Fonts/`에 넣는다(내려받기라 먼저 묻는다).

## 설치(단계 2를 시작할 때 한 번)

1. `Kit.uss` → `Assets/MomoiProject/UI/Kit/Kit.uss`. 프로젝트 화면 USS(`UI/Screens/<프로젝트>.uss`) 첫 줄: `@import url("../Kit/Kit.uss");` 프로젝트 색은 `Kit.uss` 맨 위 변수 블록에서 바꾼다(또는 프로젝트 USS에서 같은 이름의 변수를 다시 정한다).
2. `Code/*.cs.txt` → `Assets/MomoiProject/Code/Client/Kit/<이름>.cs` — `KitPanel`(패널 + 모서리 기하 `KitGeometry`) · `KitOutline`(라인 디자인 — 요소 규격) · `KitAccent`(선 포인트 7가지) · `KitLine`(`KitRule` 나누는 선 · `KitSpark` 반짝임) · `KitFx`(순간 연출 — `KitFx` 연출 층 · `KitGlow` 빛 · `KitSheen` 훑는 빛 · `KitTween` 시간 흐름) · `KitIcon`(단색 벡터 아이콘 샘플 21개) · `KitGauge`(게이지 — 직선 · 원형 패널 · 원 테두리 · 도형 나열). 네임스페이스가 `MomoiProject`가 아니면 UXML의 `MomoiProject.Kit…`도 함께 바꾼다.
3. `Samples/`는 **보고 베끼는 견본 화면**이다(그대로 화면이 되지 않는다). 견본을 띄워 볼 때는 `Kit.uss`와 `Samples/Samples.uss`를 함께 붙인다(`ElementSpec`의 그라데이션은 `Textures/gradient_up.png`). 화면 UXML을 만들 때 가장 가까운 견본을 바탕으로 짜고, 견본 전용 모양(`sample-…`)은 프로젝트 USS로 옮겨 프로젝트 접두어로 바꾸고, 글은 텍스트 키로 바꾼다.
4. 테마(`UI/Theme.uss`)의 `--momoi-…` 변수(기본 화면의 색)도 키트 색과 맞춘다.

## 요소 — 무엇을 어떤 클래스로

| 요소 | 쓰는 것 | 메모 |
|---|---|---|
| 글 | `kit-t-display`(44 굵게 — 화면 이름) · `-title`(28) · `-heading`(20) · `-body`(16) · `-small`(14) · `-caption`(12 굵게, 자간 2) | 글 색은 본문 · 흐린 글 · 꼬리표 세 단계. **강조 숫자** `kit-t-number`(글 단계 클래스와 함께) — 장면 위에 글만 놓이는 숫자(피해 · 재화 · 레벨 · %)는 판 대신 글꼴의 외곽선 · 그림자로(개발자 2026-10-15). 색 `--kit-number-*`, 숫자 전용 글꼴 에셋이 있으면 `-unity-font-definition`으로 바꿔 끼운다 |
| 패널 | `<MomoiProject.KitPanel shape="Round" pattern="Shards" seed="n" class="kit-pn-dark kit-pn-c12">` | 색 `kit-pn-dark · paper · gray · stone · sand`, 조각 `kit-pn-faint`(0.005 — UI 배경) · 기본 0.009 · `kit-pn-max`(0.014), 모서리 `kit-pn-c6 · c12 · c18 · c28`, 모서리마다 `corners="tr:24 bl:24"`(r 둥글게 · c 자르기), 선 `line="Inset · Outer · Double"` |
| 라인 디자인 | `<MomoiProject.KitOutline layer="Front" gap="10" cut="…" split="…" marks="tl:cross:on" class="kit-outline kit-ol-on-paper" />` | 요소 규격(UiGuide 3-1): 앞 · 뒤, 치우침 `skew`, 끊기 `cut`, 불규칙 분할 `split`, 두 겹 `doubled`, 약 · 강 `weight`, 8지점 장식 `marks`. 비켜 갈 칸에 `kit-ol-block` |
| 선 포인트 | `<MomoiProject.KitAccent kind="BackBox" class="kit-accent kit-ac-on-dark" />` | 7가지(네 모서리 ㄱ · 두 모서리 ㄱ · 두 면 · 마주 보는 ㄱ · 끊긴 테두리 · 원호 모서리 · 뒤 상자). **한 화면에 포인트 패널 하나** |
| 버튼 | `<ui:Button text="…" class="kit-bt kit-bt--pill kit-bt--black" />` | 모양 `--circle · --pill · --square · --cut · --leaf`(변칙은 면 `kit-bt-face` KitPanel + 글 `kit-bt-text`), 색 `--white · --sand · --black · --off`, 반전색 아웃라인 `--line`(KitPanel `line="Double"`) · 글자만 `--text`, 작게 `--small`. 판 맥락 `kit-on-paper · kit-on-dark`(아웃라인 · 글자만의 색). **판과 같은 색 면 버튼은 쓰지 않는다** |
| 가로 메뉴(토글) | 막대 `<MomoiProject.KitPanel shape="Pill" pattern="None" class="kit-menu">` + 고른 칸 `kit-menu-on`(흰 안쪽 판) 안의 `kit-menu-item` | 프로젝트마다 한 방식 — 기본 B3. 다른 방식(A · B1 · B2 · B4)은 견본 Menus(`sample-tabs-*` — 고르면 프로젝트 USS로) |
| 제목 막대 | `<MomoiProject.KitPanel … class="kit-titlebar">` + `kit-titlebar-text` · `-count` | 변칙은 그 화면의 것 |
| 칸(여럿 늘어서는 요소) | `kit-slot`(단순 칸) · `kit-card`(그림 + 이름, KitPanel `pattern="None"`) · `--on` · `--empty` | 베젤 없이, 중첩 · 선 최소, 라인 디자인 없음 |
| 정보 정리 | `kit-info-*`(견본 조각 `Samples/InfoBody.uxml`) · 꼬리표 `kit-tag` | 팝업 · 상세 안. 틀 안에 안판이 있으면 바깥에 `kit-no-third` |
| 화면 머리 · 목록 | `kit-head-*` · `kit-money-*` · `kit-nav-*` | UI 배경 위 나눈 화면 |
| 틀 | `kit-frame-*`(검정 틀 + 머리 띠 + 종이 안판) | 작은 팝업 · 알림의 안쪽 모양은 견본(`sample-pop-*` · `sample-notice-*`) |
| 순간 연출 | 연출 층 `<MomoiProject.KitFx class="kit-fx" />`(화면 맨 끝) · 훑는 빛 `<MomoiProject.KitSheen class="kit-sheen" />`(카드 안) | 코드: `fx.Reward(칸)` · `fx.Reveal(칸, 뒤 층)` · `fx.Buff(아이콘, 색, 오름, 가운데에 할 일)` · `fx.NumberPop(글)` · `fx.Tap(자리)` · `fx.Burst · Ring · Rays` · `KitGlow.Set · Neon · Flash · Pulse` · `KitTween.Run`. 빛 색은 `--kit-fx-…`(기본 금빛), 시간 · 크기는 매개변수. 예: `Samples/EffectsSample.cs.txt` |
| 게이지(슬라이더) | `<MomoiProject.KitGauge kind="Line" shape="Slant" frame="Outline" value="0.7" class="kit-gauge kit-gauge--hp" />` — `kind` Line · Orb(원형 패널) · Ring(원 테두리) · Units(도형 나열) · `shape` Rect · Slant(평행사변형) · Diamond(도형 나열) · `frame` Flush(꽉 채움) · Inset(단색 뒤 판이 더 두껍게) · 색 `color` · `end-color` · `track-color`(뒷 게이지 `loss-color` · `gain-color`는 비우면 게이지 색에서) · `segments`(눈금) · `units` · `gradient` · `from-right` · `text` | 체력 · 마나 · 경험치 · 로딩 · 버프 지속은 이 하나로, **둥근 모양 없음**(개발자 2026-10-15 — UiGuide 3-7). 크기 `kit-gauge--thin` · 기본 · `--thick`, 특수 `gradient` · `glow`. 코드: 깎일 때 `Drop(값)` → `Follow()`(밝은 뒷 게이지가 따라 줆), 찰 때 `Raise(값)` → `FillUp()`(어두운 뒷 게이지가 먼저), 지름길 `Set(값)` · 잃을 몫 표시 `Preview(-양)` · `Text`. 예: `Samples/GaugesSample.cs.txt` |
| 아이콘 | `<MomoiProject.KitIcon glyph="Close" class="kit-icon" />` | 진짜 아이콘이 오면 같은 자리를 그림으로(UiGuide 3-6) |

- 버튼 · 메뉴 · 제목 막대의 **변칙(모서리 자름 · 잎)은 한 화면에 한 가지**를 골라 그 화면 전체에 같게 쓴다(견본 VariantSet).
- 변칙 면을 KitPanel로 그리는 요소(버튼 면 · 메뉴 막대 · 제목 막대)는 `pattern="None"`(조각 없음 — 작은 요소는 단색).

## 견본(`Samples/`) — 화면의 짜임은 뒷배경으로 고른다

| 견본 | 무엇 | DB › 화면 |
|---|---|---|
| `PopupPlain.uxml` | **일반 배경** 앞 작은 팝업 — 정보가 적어 화면 일부만, 뒤 장면이 그대로. 검정 틀 + 모래색 머리 + 종이 안판 + 뒤 상자 선 포인트 | 층 팝업 · 뒷배경 일반 배경 |
| `NoticeBlur.uxml` | **블러** 앞 알림 — 알림성 정보만. 종이 판 + 원 표지 | 층 팝업 · 뒷배경 블러 |
| `SplitScreen.uxml` | **UI 배경** 위 나눈 화면 — 왼쪽 목록 \| 가운데 칸 판 \| 오른쪽 상세(검정 틀 → 종이 안판 + `InfoBody`). 가운데 팝업 하나만 두지 않는다 | 층 창 · 뒷배경 UI 배경 |
| `InfoBody.uxml` | 정보 정리 조각(머리 → 줄 → 그림 \| 스탯 → 설명 → 꼬리표) | 다른 화면에 `<ui:Template>` |
| `Panels.uxml` | 패널 모양 4가지와 면 색 | — |
| `LinePoints.uxml` | 선 포인트 7가지 | — |
| `ElementSpec.uxml` | 요소 규격으로 짠 카드(흰 메인 패널 · 아래 20% 그라데이션 · 육각 제목 · 아이콘 칸 · 앞 선 끊김 · 뒤 선 치우침) | — |
| `Menus.uxml` | 가로 메뉴 — B3 기본(+ 변칙 자름 · 잎)과 A · B1 · B2 · B4 | — |
| `Buttons.uxml` | 버튼 — 종이 판 위 · 검정 판 위, 보통 · 작게 | — |
| `VariantSet.uxml` | 변칙 돌려쓰기 — 한 화면에 한 변칙(제목 막대 · 메뉴 막대 · 버튼), 가운데 칸은 베젤 없이 | — |
| `Effects.uxml` + `EffectsSample.cs.txt` | 순간 연출 — 보상 펑 · 희귀 공개(빛살은 아이템 뒤 층) · 버프(자체 발광 아이콘이 떠올라 사라짐 → 수치 펑) · 훑는 빛 · 발광 맥박. 화면 코드에서 부르는 법 | — |
| `Gauges.uxml` + `GaugesSample.cs.txt` | 게이지의 경우의 수 전부(직선 사각 · 평행사변형 × 꽉 채움 · 뒤 판 두껍게, 눈금, 그라데이션 + 발광, 원형 패널, 원 테두리, 도형 나열) + 장면 위 상태 UI의 쓰는 법(묶는 패널 없이 여백 · 초상 · 아이콘 칸만 패널 · 강조 숫자). 버튼으로 지름길 · 찰 때(뒷 게이지만 → 채우기) · 줄 때(깎기만 → 따라 줄기) · 잃을 몫 표시 | — |

- 플레이 화면(HUD) · 거점 · 이벤트의 짜임은 아직 새 견본이 없다 — UiGuide 2-2(자리) · 3절(디자인)대로 짜고, 만든 화면을 견본으로 올려 승인받는다.
- 견본의 글은 바로 적혀 있다. 프로젝트 화면에서는 텍스트 키(`binding-path` 또는 코드)로 바꾼다.
