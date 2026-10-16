# 메뉴얼 지도 (AI용 — 늘 읽는다)

**메뉴얼** = AI가 프로젝트를 진행하는 데 필요한 문서 전체(명명 가이드라인 0절). 문서는 주제마다 나뉘어 있고, 이 지도가 목차다.
- **늘 읽는 것**(CLAUDE.md가 세션마다 불러온다): 이 지도 · 프로젝트 관례(`ProjectConventions.md` — 맨 위 '규칙의 세기'([필수] · 권장 · 예시)가 메뉴얼 전체에 걸린다 — UI 가이드는 규칙 전부가 [필수]).
- **그때그때 읽는 것**: 아래 '작업별로 읽을 문서'에서 고른다. 문서는 작아서 통째로 읽는다(기능 장은 맨 위 요약 · '동작 규칙' 절(있으면) · 마지막 '주의할 점' 절부터 — 장마다 절 짜임이 달라 번호로 고르지 않는다, `Manual/00-Index.md` 머리말). 매번 다시 찾지 않도록, 한 세션에서 한 번 읽은 문서는 다시 열지 않는다(바뀌었을 때만).

## 작업별로 읽을 문서

- **코드를 쓰거나 고치는 작업을 시작할 때** → 참조 `Reference.md`를 끝까지(무엇이 있나 — 있는 기능부터 쓴다). 기능을 처음 쓰면 그 기능 장(`Manual/NN`)의 맨 위 요약 · '동작 규칙' · 마지막 '주의할 점' 절, 그리고 쓰는 API의 절.
- **프로젝트 진행**(단계 · 질문 · 결정 기록 · 답 양식) → 진행 매뉴얼 `ProjectFlow.md` · 스킬 `/think-momoi`.
- **기획 명세서 읽기·채우기** → `PlanningSpecGuide.md` · 빈 양식 `PlanningSpec.md` · 채운 예시 `PlanningSpec-CardDungeon.md`.
- **기믹(효과 문장)·메인 룰** → 기믹 목록 `GimmickCatalog.md`. 네 게임을 따라 만든 기록은 `DryRuns/`.
- **이름 짓기**(화면 이름 · 밸런싱 값 표시 이름 · 게임 용어 · 스탯 이름) → 명명 가이드라인 `NamingGuide.md`(0절 말의 뜻 · 4절 바로잡은 이름 · 5절 게임 이름의 기본 · 6절 이름 목록).
- **화면·UI 템플릿**(문법 · 레이아웃 · 디자인 · 움직임 · 아이콘 · 플레이 확인) → UI 가이드 `UiGuide.md` + UI 키트 `Templates/UI/README.md` · 기능 장 35(화면).
- **멀티플레이**(명세서 0절이 '있음') → `Multiplayer.md`(지킬 것) · 기능 장 40.
- **DB 항목 만들기**(DB 반영 스크립트) → 관례 15절 · 기능 장 21.
- **테스트 · AI가 Unity를 돌리기**(규칙 테스트 · 플레이 둘러보기 · 녹화 · 영상 요청) → 기능 장 38 2-4·2-5.
- **에셋 자리**(넣을 에셋 · 임시 그림) → 기능 장 39 · 관례 18절. **스파인 캐릭터**(버전 호환 · 런타임 설치 · 알파 · 넣을 파일 · 조종 뼈) → 기능 장 39 5절.
- **선택 요소**(상태 효과 · 패시브 효과 · 액티브 효과의 DB 종류) → `Templates/Elements/` · 참조 4-1.
- **프로젝트 구현 요령**(옛 프로그래머 가이드 — 장르별 조립·습관) → `BuildGuide/`.
- **지바이블 자체를 고치는 작업**(라이브러리 개발) → 패키지 `HANDOVER.md`(인계 문서) · `Architecture.md` · `Coding.md`·`CodingSyntax.md`.

## 문서 목록

### AI 문서(`Documentation~/AI/`)
- `ManualMap.md` — 이 지도.
- `ProjectConventions.md` — 프로젝트 관례: 모든 프로젝트에서 지키는 규칙(폴더 · 이름 · 규칙과 화면의 경계 · 행동 직전 이벤트 · 숫자 · 화면 · 테스트 · DB 반영 스크립트 · 개발자가 읽고 바꿀 모양).
- `Reference.md` — 참조: 하고 싶은 일 → 지바이블 기능·API·DB 종류·Block.
- `ProjectFlow.md` — 진행 매뉴얼: 단계 0 흡수 → 1 시스템층 → 2 UI·샘플 → 요청 작업, 질문 규칙, 결정 기록, 답의 양식(13절).
- `PlanningSpecGuide.md` · `PlanningSpec.md` · `PlanningSpec-CardDungeon.md` — 기획 명세서 읽는 법 · 빈 양식 · 채운 예시.
- `GimmickCatalog.md` — 기믹 목록(발라트로 · 와일드프로스트 · 매지크래프트 · 세피리아 범위의 갈래 → 부품).
- `NamingGuide.md` — 명명 가이드라인.
- `UiGuide.md` — UI 가이드(UI 템플릿 정의 · 문법 · 레이아웃 · 디자인 · 움직임 · 아이콘 · 진행 · 보내기 전 확인).
- `Multiplayer.md` — 멀티플레이에서 처음부터 지킬 것.
- `Templates/` — 프로젝트로 복사하는 틀: `CLAUDE.block.md`(CLAUDE.md 칸) · `think-momoi.SKILL.md`(스킬) · `Decisions.md`(결정 기록) · `Elements/`(선택 요소 DB 종류) · `UI/`(UI 키트).
- `BuildGuide/` — 구현 가이드(00 목차 ~ 12 부록). `DryRuns/` — 장르 시험 기록(00 요약 ~ 12).

### 기능 장(`Documentation~/Manual/` — 장마다 기능 하나, 목차 `00-Index.md`)
- 데이터와 행동: 01 플레이어 데이터 · 02 행동과 Undo · 03 Game API · 04 인스턴스 · 05 키워드 · 06 저장·불러오기 · 07 난수와 시간
- 이벤트·트리거·결과: 10 이벤트 · 11 트리거와 조건 · 12 결과 · 13 `[BindTo]` · 14 도전과제 · 15 로그 · 16 액티브 효과와 사용 기능 · 17 프로젝트 고유 기능
- 시작·도구·모듈: 20 시작하기(컴파일 검사 훅 11절) · 21 DB · 22 모니터와 디버깅 · 23 텍스트와 현지화 · 24 다이얼로그 · 25 인벤토리 · 26 오디오 · 27 필드 규칙과 스프라이트 틀
- 게임 규칙: 28 스탯·Modifier·행동 직전 이벤트·비용 · 29 상태 효과·예약·선택 대기 · 30 밸런싱 값·곡선·가중치 표·플레이어용 실패 · 31 슬롯·설정·입력 · 32 씬 오브젝트·연출·씬 흐름 · 33 고정 틱·미리보기·CSV · 34 Steam · 35 화면 · 36 격자·큰 수 · 37 자리 · 38 테스트(AI의 테스트·플레이 요청) · 39 에셋 · 40 멀티플레이

### 사람용(`Documentation~/Guide/` — 개발자가 읽는 운영 가이드)
- 00 목차 · 01 시작 · 02 AI와 일하기 · 03 읽고 밸런싱하기 · 04 모니터 · 05 테스트 · 06 에셋 · 07 다이얼로그와 레벨 · 08 출시 · 09 문제 해결.

### 그 밖(`Documentation~/`)
- `Architecture.md`(설계 결정 기록) · `PlayerDataStore.md` · `Localization.md` · `Dialogue.md` · `Audio.md` · `Coding.md` · `CodingSyntax.md` · `Review.md`(2026-10-01 점검) · `TestMultiplayer.md`(멀티 시험 환경 설계 기록) · `PendingChecks.md`(사람이 Unity에서 확인할 목록).

## 메뉴얼을 고치라는 지시를 받으면 — 어디에 넣나

개발자가 "메뉴얼을 업데이트해라 / 이 부분·이 지시를 추가해라"라고 하면 알맞은 곳(여러 곳일 수 있다)을 골라 고치고, 지금 프로젝트와 앞으로의 모든 프로젝트가 따르게 한다.
- 진행 순서 · 질문 · 답 양식 · 단계 → `ProjectFlow.md` (+ 스킬 틀)
- 모든 프로젝트에서 지킬 규칙 → `ProjectConventions.md` (+ CLAUDE 틀의 요약 줄)
- 지바이블 기능의 쓰는 법 → 그 기능 장 + `Reference.md` 표
- 이름 → `NamingGuide.md` · 화면·UI 템플릿 → `UiGuide.md` + `Templates/UI/` · 기믹 → `GimmickCatalog.md` · 명세서 칸 → `PlanningSpecGuide.md` + `PlanningSpec.md`
- 사람(개발자)이 읽을 운영 안내 → `Guide/` · 지바이블 개발 기록 → `HANDOVER.md`
- 더하거나 고친 규칙도 '규칙의 세기'(관례 맨 위)대로 적는다 — `[필수]`는 그 기준에 드는 것만, 고르는 여지가 있는 것은 권장("기본은 …")으로, 조립 예는 `예:`로.
- 틀(`Templates/CLAUDE.block.md` · `think-momoi.SKILL.md`)을 고쳤으면 지금 프로젝트의 `CLAUDE.md` 칸과 `.claude/skills/think-momoi/SKILL.md` 사본도 같은 줄로 맞춘다. 새 문서를 만들면 이 지도에 한 줄 더한다.
