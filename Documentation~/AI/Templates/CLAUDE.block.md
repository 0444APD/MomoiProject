# 이 프로젝트: GBible(MomoiProject)로 만드는 Unity 게임

- **역할**: 로직·DB 항목·화면·테스트 도구는 AI(너)가 코드와 DB 반영 스크립트로 만든다. 개발자는 에디터에서 읽고(DB·모니터), 밸런싱 값을 바꾸고, 다이얼로그 그래프를 만들고, 레벨을 배치하고, 리소스(스프라이트 시트·애니메이션 클립·소리·글꼴)를 폴더에 두고, 테스트한다. 프리팹은 AI가 개발자가 말로 준 양식대로 만들고 리소스를 적용한다(개발자는 프리팹을 만지지 않는다 — 관례 6절). 로직 쪽 DB 항목의 추가·삭제·로직 변경은 개발자가 말로 요청한다.
- 진행은 스킬 `/think-momoi`와 진행 매뉴얼 `{{GBIBLE}}/Documentation~/AI/ProjectFlow.md`를 따른다(단계 0 흡수 → 1 시스템층 → 2 UI·샘플 — 화면 전부와 단위 테스트를 위한 최소 로직 샘플. 그 뒤 콘텐츠·레벨·연출·밸런싱·출시는 개발자가 요청할 때).
- **결정 기록 `Docs/Decisions.md`를 세션마다 먼저 읽는다.** 정한 것은 다시 묻지 않는다. **기획은 넘겨짚지 않는다** — 자료·답에 없는 것은 추천을 붙여 묻고, 개발자가 답하거나 승인해야 확정이다. 확정한 것은 바로 적고 채팅에도 한 줄 남긴다(답을 기다리는 추천·가안은 '확인 기다리는 것'에).
- 기획 명세서 `Docs/PlanningSpec.md`(살아 있는 문서 — 답이 칸을 바꾸면 고치고 `(D-n)`), 자료 `Docs/Source/`. 채운 예시 `{{GBIBLE}}/Documentation~/AI/PlanningSpec-CardDungeon.md`.
- GBible 문서: `{{GBIBLE}}/Documentation~/` — 매뉴얼 `Manual/`(장마다 기능 하나), AI 문서 `AI/`.
- 늘 읽는 것은 아래 둘이다: 메뉴얼 지도(문서 목차 — 무엇을 할 때 어느 문서를 읽나)와 프로젝트 관례(모든 규칙). **코드를 쓰거나 고치는 작업을 시작할 때는 참조 `{{GBIBLE}}/Documentation~/AI/Reference.md`를 끝까지 읽는다**(있는 기능부터 쓴다). 나머지 문서는 지도를 보고 필요할 때 읽는다.

@{{GBIBLE}}/Documentation~/AI/ManualMap.md
@{{GBIBLE}}/Documentation~/AI/ProjectConventions.md

## 이 프로젝트에서 늘 지킬 것(관례의 요약)

- **예외를 첫 단계에서 막는다.** 틀에 맞지 않는 것이 나오면 예외 코드로 덧붙이지 않고 틀(메인 룰)을 고치는 쪽을 먼저 본다 — 범위가 크면 영향·비용·대안을 붙여 묻는다.
- 데이터는 DB, 동작은 코드. 플레이어 데이터를 바꾸는 일은 모두 행동(`GameAction`) 안에서.
- **개발자가 바꿀 것은 에디터에**: 숫자는 밸런싱 값·DB 칸·인스턴스 필드, 참조는 드롭다운(`[DataRef]`). 코드에 숫자를 적지 않는다(관례 18절).
- **테스트할 수 있게**: 테스트 씬·테스트 명령·테스트 상황을 함께 만든다(관례 19절).
- **멀티**(명세서 0절이 있음): `{{GBIBLE}}/Documentation~/AI/Multiplayer.md`를 읽고 4블록 규칙대로 — 판정은 행동 안·플레이어별 칸·숨길 것은 Owner 키·서버 규칙(Validate)·연출은 SubscribeAfterAction·실시간 움직임은 NetTransformSync(관례 20절, 매뉴얼 40장).
- 규칙(`Assets/MomoiProject/Code/Rules`)은 Unity API를 쓰지 않는다. 화면·입력·연출은 `Assets/MomoiProject/Code/Client`, UI는 `Assets/MomoiProject/UI`(UXML·USS) + DB › 화면.
- **화면은 `{{GBIBLE}}/Documentation~/AI/UiGuide.md`대로**(모든 프로젝트 공통): 문법(클래스로만 모양 · 텍스트 키 · 창은 가운데 감싸개 · 월드 장면과 UI 나누기) · 레이아웃(1920×1080, 위 띠·아래 띠 자리, 모바일처럼 크지 않게) · 디자인(차가운 검정 + 종이 흰색, 매트·플랫 — 그림자·빛남 없음, 패널 면은 단색 + 아주 옅은 조각, 패널 모양 4가지(직각·약간 둥근·모서리 자름·알약)와 선·둥글기·각의 변칙, 패널 2중첩까지, 기울인 패널·물건 소품 없음, 뒷배경 3가지 — DB › 화면 '뒷배경' 칸, 블러는 알림만, 장면 위 UI(HUD · 상태)는 묶는 패널 없이 여백으로 — 스프라이트를 품은 요소만 패널 · 게이지는 KitGauge(둥근 모양 없음) · 홀로 놓이는 글은 강조 숫자(글꼴 외곽선 · 그림자)) · UI 키트(`{{GBIBLE}}/Documentation~/AI/Templates/UI/` — 키트 USS · KitPanel 등 코드 · 견본 화면 — 프리팹 아님)를 복사해 짠다. 개발자는 화면마다 전체 레이아웃 참고 그림만 주고 세부 배치는 묻지 않는다.
- 있는 기능부터 찾는다(Reference.md). 새로 만들기 전에 매뉴얼 `00-Index.md`의 "상황별로 찾기".
- 상태 효과·패시브 효과·액티브 효과는 선택 요소다: 쓰면 템플릿(`{{GBIBLE}}/Documentation~/AI/Templates/Elements/`)을 `Assets/MomoiProject/Code/Rules/Elements/`에 들이고, 칸이 모자라면 그 클래스에 더한다(프로젝트 변경 — 묻지 않고 보고만).
- 개발자에게 보이는 이름(DB·칸·Block·메뉴·밸런싱 값)은 `{{GBIBLE}}/Documentation~/AI/NamingGuide.md`대로 짓는다 — '바로잡은 이름' 표를 먼저 본다. 밸런싱 값은 표시 이름·메모·입력 칸 주석·묶음을 함께. 개발자가 이름을 바로잡으면 그 표에 더한다.
- 기믹(효과 문장)은 `{{GBIBLE}}/Documentation~/AI/GimmickCatalog.md`의 갈래로 쪼개 만든다. 조각이 놓이는 메인 룰은 그 문서 1절의 모양으로.
- DB 항목은 DB 반영 스크립트(`Assets/MomoiProject/Code/Editor`의 `[DbSeed]` 함수 — 컴파일 뒤 저절로 돈다)나 CSV로 만든다. 새 항목은 저절로 적용되고, 이미 있는 항목을 고치거나 지우거나 ID를 바꾼 것만 '적용 전'으로 남아 개발자가 Ctrl+S로 적용한다. 새 항목의 `GameIds` 상수는 반영 스크립트가 돈 뒤 다시 컴파일돼야 생긴다 — 그 상수를 쓰는 코드는 그다음에 쓴다(관례 15절).
- 응답을 끝낼 때 훅(`.claude/hooks/compile-check.ps1`)이 프로젝트 코드를 컴파일한다. 오류가 나오면 고친 뒤 끝낸다.
- Unity 상태는 파일로 읽는다: DB 검증·'적용 전' 변경은 `Logs/MomoiProject/DbStatus.txt`, Unity 로그(컴파일 경고·DB 반영 스크립트 결과)는 `Logs/Editor.log`. 규칙 테스트는 `Logs/MomoiProject/RunTests.request`를 만들면 Unity가 돌려 `Logs/MomoiProject/TestResults.txt`에 쓴다(Unity를 연 채로 배치 모드는 쓰지 않는다).
- 답은 진행 매뉴얼 13절 양식: 진행 답은 제목(답 · 진행한 것 · 이슈·버그 한 줄씩 · 질문 — `Q번호.` + 추천·이유, 표 없이 · 알릴 것 — 할 일·결정·확인 기다리는 것·단계)으로 나누고 길게 늘이지 않는다. 할 말이 없는 제목은 통째로 뺀다("없습니다"도 쓰지 않음). 개발자의 질문·요청에는 그에 맞게 답한다.
