# 이 프로젝트: GBible(MomoiProject)로 만드는 Unity 게임

- **역할**: 로직·DB 항목·화면·테스트 도구는 AI(너)가 코드와 DB 반영 스크립트로 만든다. 개발자는 에디터에서 읽고(DB·모니터), 밸런싱 값을 바꾸고, 다이얼로그 그래프를 만들고, 레벨을 배치하고, 에셋을 가져오고, 테스트한다. 로직 쪽 DB 항목의 추가·삭제·로직 변경은 개발자가 말로 요청한다.
- 진행은 스킬 `/think-momoi`와 진행 매뉴얼 `{{GBIBLE}}/Documentation~/AI/ProjectFlow.md`를 따른다(단계 0 흡수 → 1 시스템층 → 2 콘텐츠 → 3 레벨 디자인 → 4 연출·UI·사운드 → 5 밸런싱·테스트 → 6 출시 준비).
- **결정 기록 `Docs/Decisions.md`를 세션마다 먼저 읽는다.** 정한 것은 다시 묻지 않는다. **기획은 넘겨짚지 않는다** — 자료·답에 없는 것은 추천을 붙여 묻고, 개발자가 답하거나 승인해야 확정이다. 확정한 것은 바로 적고 채팅에도 한 줄 남긴다(답을 기다리는 추천·가안은 '확인 기다리는 것'에).
- 기획 명세서 `Docs/PlanningSpec.md`(살아 있는 문서 — 답이 칸을 바꾸면 고치고 `(D-n)`), 자료 `Docs/Source/`. 채운 예시 `{{GBIBLE}}/Documentation~/AI/PlanningSpec-CardDungeon.md`.
- GBible 문서: `{{GBIBLE}}/Documentation~/` — 매뉴얼 `Manual/`(장마다 기능 하나), AI 문서 `AI/`.
- 아래 두 문서가 이 프로젝트의 규칙과 기능 목록이다(늘 읽는다).

@{{GBIBLE}}/Documentation~/AI/ProjectConventions.md
@{{GBIBLE}}/Documentation~/AI/Reference.md

## 이 프로젝트에서 늘 지킬 것(관례의 요약)

- **예외를 첫 단계에서 막는다.** 틀에 맞지 않는 것이 나오면 예외 코드로 덧붙이지 않고 틀(메인 룰)을 고치는 쪽을 먼저 본다 — 범위가 크면 영향·비용·대안을 붙여 묻는다.
- 데이터는 DB, 동작은 코드. 플레이어 데이터를 바꾸는 일은 모두 행동(`GameAction`) 안에서.
- **개발자가 바꿀 것은 에디터에**: 숫자는 밸런스 값·DB 칸·인스턴스 필드, 참조는 드롭다운(`[DataRef]`). 코드에 숫자를 적지 않는다(관례 18절).
- **테스트할 수 있게**: 테스트 씬·테스트 명령·테스트 상황을 함께 만든다(관례 19절).
- **멀티**(명세서 0절이 있음): 4블록 규칙 — 판정은 행동 안·플레이어별 칸·숨길 것은 Owner 키·서버 규칙(Validate)·연출은 SubscribeAfterAction·실시간 움직임은 NetTransformSync(관례 20절, 매뉴얼 40장).
- 규칙(`Assets/MomoiProject/Code/Rules`)은 Unity API를 쓰지 않는다. 화면·입력·연출은 `Assets/MomoiProject/Code/Client`, UI는 `Assets/MomoiProject/UI`(UXML·USS) + DB › 화면.
- 있는 기능부터 찾는다(Reference.md). 새로 만들기 전에 매뉴얼 `00-Index.md`의 "상황별로 찾기".
- 상태 효과·패시브·능력은 선택 요소다: 쓰면 템플릿(`{{GBIBLE}}/Documentation~/AI/Templates/Elements/`)을 `Assets/MomoiProject/Code/Rules/Elements/`에 들이고, 칸이 모자라면 그 클래스에 더한다(프로젝트 변경 — 묻지 않고 보고만).
- 기믹(효과 문장)은 `{{GBIBLE}}/Documentation~/AI/GimmickCatalog.md`의 갈래로 쪼개 만든다. 조각이 놓이는 메인 룰은 그 문서 1절의 모양으로.
- DB 항목은 DB 반영 스크립트(`Assets/MomoiProject/Code/Editor`의 `[DbSeed]` 함수 — 컴파일 뒤 저절로 돈다)나 CSV로 만든다. 새 항목은 저절로 적용되고, 이미 있는 항목을 고치거나 지운 것만 '적용 전'으로 남아 개발자가 Ctrl+S로 적용한다.
- 응답을 끝낼 때 훅(`.claude/hooks/compile-check.ps1`)이 프로젝트 코드를 컴파일한다. 오류가 나오면 고친 뒤 끝낸다.
- 응답 끝에: 개발자가 할 일(적용할 DB 항목·넣을 에셋·돌려 볼 테스트 상황·Unity에서 볼 것) · 기록한 결정 · 확인 기다리는 것(승인 요청) · 지금 단계와 다음 할 일.
