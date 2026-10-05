# 이 프로젝트: GBible(MomoiProject)로 만드는 Unity 게임

- GBible 문서: `{{GBIBLE}}/Documentation~/` — 매뉴얼 `Manual/`(장마다 기능 하나), AI 문서 `AI/`.
- 기획 명세서: `Docs/PlanningSpec.md`(기획자가 채운다, 채운 예시 `{{GBIBLE}}/Documentation~/AI/PlanningSpec-CardDungeon.md`).
- 명세서로 시스템층을 만들 때, 개별 콘텐츠(카드·스킬·유물·적)를 옮길 때는 스킬 `/think-momoi`를 쓴다.
- 아래 두 문서가 이 프로젝트의 규칙과 기능 목록이다(늘 읽는다).

@{{GBIBLE}}/Documentation~/AI/ProjectConventions.md
@{{GBIBLE}}/Documentation~/AI/Reference.md

## 이 프로젝트에서 늘 지킬 것(관례의 요약)

- 데이터는 DB, 동작은 코드. 플레이어 데이터를 바꾸는 일은 모두 행동(`GameAction`) 안에서.
- 규칙(`Assets/Code/Rules`)은 Unity API를 쓰지 않는다. 화면·입력·연출은 `Assets/Code/Client`, UI는 `Assets/UI`(UXML·USS) + DB › 화면.
- 있는 기능부터 찾는다(Reference.md). 새로 만들기 전에 매뉴얼 `00-Index.md`의 "상황별로 찾기".
- 기믹(효과 문장)은 `{{GBIBLE}}/Documentation~/AI/GimmickCatalog.md`의 갈래로 쪼개 만든다. 조각(카드·조커·유물)이 놓이는 메인 룰은 그 문서 1절의 모양으로.
- DB 항목은 DB 추가 메뉴(`Assets/Code/Editor`)나 CSV로 만들고, 사람이 Ctrl+S로 적용한다.
- 응답을 끝낼 때 훅(`.claude/hooks/compile-check.ps1`)이 프로젝트 코드를 컴파일한다. 오류가 나오면 고친 뒤 끝낸다.
- 보고에는 사람이 할 일(누를 메뉴·Ctrl+S·넣을 에셋·Unity에서 볼 것)을 적는다.
