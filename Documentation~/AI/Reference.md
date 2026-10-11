# GBible 참조 (AI용)

새 프로젝트에서 AI가 **무엇이 있고 무엇을 부르면 되는지** 찾는 문서다. 아래 문서를 함께 읽는다. 코드·DB 항목은 AI가 만들고, 개발자는 에디터에서 읽고·값을 바꾸고·테스트한다(관례 첫머리).

| 문서 | 맡는 것 |
|---|---|
| `ProjectFlow.md`(진행 매뉴얼) | **언제** 무엇을 하나: 단계 0 흡수 ~ 6 출시 준비, 질문 규칙, 예외 잡기, 결정 기록(`Docs/Decisions.md`) |
| `ProjectConventions.md`(프로젝트 관례) | **어떻게** 쓰나: 폴더·이름·규칙과 화면의 경계·행동 직전 이벤트 자리·테스트·보고·DB 반영 스크립트·개발자가 읽고 바꿀 모양·테스트 씬 |
| `PlanningSpecGuide.md`(기획 명세서 읽는 법) | 기획 명세서의 칸 → 무엇을 만들지, 질문 규칙, 진행 순서 |
| `NamingGuide.md`(명명 가이드라인) | 개발자 화면 이름(한글)을 짓는 규칙 · 밸런스 값의 표시 이름·메모·입력 칸 주석·묶음 · 개발자가 바로잡은 이름 표(먼저 본다) |
| `GimmickCatalog.md`(기믹 목록) | 네 게임(발라트로·와일드프로스트·매지크래프트·세피리아) 범위의 기믹 갈래 → GBible 부품, 메인 룰의 모양과 미리 둘 자리 |
| 이 문서 | **무엇이** 있나: 하고 싶은 일 → 기능·API·DB 종류·Block, 매뉴얼 장 |

- 프로젝트에서는 시작 설정이 만든 `CLAUDE.md`가 관례와 이 문서를 늘 불러오고, 스킬 `/think-momoi`가 명세서 읽는 법을 연다. 응답을 끝낼 때 컴파일 검사 훅이 돈다(매뉴얼 20장 11절).
- 세션을 시작할 때 이 문서를 끝까지 읽는다. 기믹(효과 문장)을 만들 때는 기믹 목록을 먼저 본다. 기능을 처음 쓰는 순간에는 그 장(`Documentation~/Manual/NN-….md`)의 1절(무엇으로 만드나)·2절(동작 규칙)·6절(주의할 점)을 읽는다. 코드 예는 5절(상황별 예제).
- 여기 없는 것을 새로 만들기 전에 매뉴얼 `00-Index.md`의 "상황별로 찾기"를 한 번 더 본다. 그래도 없으면 관례 0절 순서(DB 조합 → 재사용 Block → `[BindTo]` → 중간층 확장)로 만든다.
- 이 문서의 기능·Block·메뉴·행동 직전 이벤트 이름은 DB와 화면에 보이는 그대로다. 사람에게 보고할 때도 이 이름을 쓴다. DB 칸은 이름표가 코드 변수 이름이고 한글 이름은 마우스를 올리면 보이므로 `perElement`(놓인 것마다)처럼 둘을 함께 적는다.

---

## 1. 구조 한눈에

```
GBible.Core (Unity 없음 — 서버·테스트에서도 돈다)
  GameSession ─ Data(플레이어 데이터) · Actions(행동·Undo) · Events · Triggers · Journal(기록)
               Abilities(사용) · Attributes(능력치·Modifier) · Statuses · Schedules · Choices
               Balance · Tables(가중치 표) · World(월드 상태) · Stats(로그) · Achievements · Random · Clock
  GameApi(= Game) ─ 게임 코드의 창구(행동 안에서 쓴다)
GBible.Unity
  GBibleRuntime(시작·슬롯·저장) · DB(GameData 종류들) · GameView(다시 그리기·연출 대기열)
  GameScreens(UI Toolkit 화면) · ViewList·DataBinding(씬 오브젝트) · GameSettings · GameScenes · GameTime · GameAudio · Texts·GameLanguage
선택 모듈(DB 사이드바의 '기능 활성화' — AI는 ProjectSettings/MomoiProject/Features.json의 enabled로 켠다, 관례 0절): 다이얼로그 · 인벤토리 · 입력(키 바꾸기) · Steam
선택 요소(상태 효과 · 패시브 · 능력): 엔진(걸기·중첩·지속·가진 것 세기·Game.Use·기본 Block·Game 함수)은 GBible, DB 종류(데이터 칸 클래스)는 프로젝트가 템플릿에서 들인다(아래 '선택 요소')
에디터: DB 창(MomoiProject › DB, 구조 잠금 — EditorPolicy) · 모니터(플레이 중 기록·값·명령·테스트 상황) · 에셋 관리 · 시작 설정 · DB 반영 스크립트([DbSeed]) · CSV
테스트: 테스트 씬(TestScene) · 테스트 상황(TestScenarios) · 테스트 명령(DB › 개발 명령) · 자동 시뮬레이션
멀티(4블록): NetServer(서버 중개) · NetClient(클라이언트 중개) · NetHub(매칭큐 서버) · 전송(메모리·WebSocket·Steam 릴레이) · GameNet(Unity 창구) · NetTransformSync(실시간 위치)
```

- **한 판의 흐름**: 게임이 켜지면 `GBibleRuntime.Start`가 DB를 읽어 세션을 만들고(Bootstrap 옵션 → `Configuring` → 세션 → DB의 정의 등록 → `Started`), 프로필을 불러온다. 입력은 행동(`Game.Do`), 행동이 데이터를 바꾸고 이벤트를 쏜다. 화면은 프레임 끝에 데이터를 읽어 그린다.
- **프로젝트 폴더**(관례 1절): `Assets/MomoiProject/Code/Rules`(규칙, Unity API 없음) · `Assets/MomoiProject/Code/Client`(화면·입력·연출, `Client/Screens`) · `Assets/MomoiProject/Code/Editor`(DB 반영 스크립트 `Seeds/`) · `Assets/MomoiProject/Code/Tests` · `Assets/MomoiProject/UI`(UXML·USS·Panel Settings) · `Assets/MomoiProject/Scenes/Test.unity`(테스트 씬) · `Assets/MomoiProject/Data/Tests`(테스트 상황) · `Assets/MomoiProject/Code/Rules/Generated`(GameIds, 손대지 않음 — 플레이어 데이터 형식이 든 어셈블리 폴더) · `Docs/`(명세서·결정 기록·`Source`).

---

## 2. 하고 싶은 일 → 쓸 것

### 2-1. 데이터 · 행동 · Undo

| 하고 싶은 일 | 쓸 것 | 장 |
|---|---|---|
| 게임 중 바뀌는 값(판·프로필) | `[Serializable]` 클래스 + DB › 플레이어 데이터(키 `run` = 세션 구역, `profile` = 메타 구역). 시작 설정이 RunState·Profile을 만든다. 읽기 `Game.Data().Run`(생성 접근자)·`Game.Get<T>(키)` | 01 |
| 값을 바꾸기 | **행동 안에서** 필드를 그냥 고친다(Mirror·Sync가 기록). 행동 밖에서 바꾸지 않는다 | 01·02 |
| 플레이어 입력 하나 | `class X : GameAction { 필드 = 입력; protected override void Run() { … } }` → `Game.Do(new X { … })`. 하나 = Undo 한 단위 = 네트워크 한 묶음 | 02 |
| 되돌리기 | `session.Undo()`(Bootstrap Undo 방식: 행동 단위·스냅샷·쓰지 않음, 보관 수 — 새 DB 기본 200). 이번 턴만이면 Bootstrap 'Undo 끊는 이벤트'. "이번 턴 처음으로"·"되감기 3회"는 `session.HistoryMark`·`UndoTo(지점)` | 02 |
| id로 객체 찾기 | `Game.Find<T>(id)`·`Game.All<T>()`. id가 있는 클래스(`public string id`). 새 id는 `Game.NewId("card")` | 03 |
| 행동 필드에 객체 넣기 | 넣어도 된다: 기록·전송에는 id만 남고 실행 직전에 다시 찾는다(Undo·불러오기 뒤에도 맞다) | 02 |
| 판을 이어 하기·저장 | Bootstrap '판 저장'(행동마다·끌 때·저장 안 함·N초마다·이벤트를 받을 때)·'판 끝 이벤트'. `GBibleRuntime.SaveRun`(`Saving` → 프로필 → 판 — 프로필도 함께)·`LoadRun`(깨졌으면 `.bak`으로)·`NewRun`·`HasSavedRun`, 저장 직전·불러온 뒤·끄기 직전 이벤트 `Saving`·`RunLoaded`·`Stopping` | 06·10장(가이드) |
| 출시 뒤 저장 구조 변경 | DB › 개발 › 저장 변환(필드 옮김·키 이름 바꿈·키 삭제), 계산이 필요하면 `options.Migrations.Add` | 06 |
| 결정론 난수 | `Game.Random("흐름 이름").Range(최소, 최대 + 1)`·`NextDouble()`. 흐름은 쓰는 곳마다 이름. 행동 안에서만. 확률은 아래 `Game.Chance` | 07 |
| N% 확률(행운·"확률 2배"가 고침) | `Game.Chance(0.25, "흐름", source)`·조건 '확률'(이벤트 받기·행동 직전 이벤트 받기·사용 트리거와 AND, 미리 보기에서는 굴리지 않음). 행동 직전 이벤트 '확률'(`probability`)을 받아 고치면 모든 확률이 바뀐다 | 07·11 |
| 게임 시간·일시정지·배속 | `GameTime.Pause(이유)`·`Resume`·`SetSpeed`, 세션 시계(`session.Clock`) | 07 |
| 개체 여럿의 id | `Game.NewId("bat")` · 한 번에 `Game.NewIds("bat", 30)` | 03 |

### 2-2. 규칙 · 효과

| 하고 싶은 일 | 쓸 것 | 장 |
|---|---|---|
| 일이 일어났다고 알리기 | `[Serializable, GameEvent("이름")] class X { … }` → `Game.Events.Publish(new X { … })`. 받기 `Subscribe<X>`(규칙)·`SubscribeAfterAction<X>`(연출) | 10 |
| 핵심 동사(피해·회복·사용·이동 …) | 규칙 함수(`Rules.Damage`) 하나 + 행동 직전 이벤트(`[Serializable, Intent("피해")] class DamageIntent { sourceId, targetId, amount, cancelled }`)를 `Game.Intercept(행동 직전 이벤트)`로 통과 + 끝난 뒤 이벤트 | 28, 관례 4절 |
| "받는 피해 −5"·"막기"·"두 배"·"대신 맞기" | DB에서 트리거 '행동 직전 이벤트 받기'(행동 직전 이벤트 형식) + 결과 '행동 직전 이벤트 고치기'(값으로 바꾸기·더하기·곱하기·막기) | 28 |
| 도발·대신 맞기·중첩·레벨만큼·조각의 값만큼 | 값 틀('행동 직전 이벤트 고치기'·'필드 바꾸기'·'이벤트 쏘기'·'사용자 필드 검사'): `{user}`(사용자 id) · `{user:경로}`(사용자의 필드) · `{event:경로}`(받은 이벤트·행동 직전 이벤트의 필드) · `{data:키.경로}` · `{count:키.경로}`(목록 개수) · `{owner}` · `{stacks}` · `{level}` · `{level:2/4/7}` · `{balance:ID}`. 예: `targetId` 값으로 바꾸기 `{user}`, `amount` 더하기 `-{stacks}` | 28 |
| GBible이 행동 직전 이벤트 자리를 둔 동작 | 행동 직전 이벤트 '사용'(`times`·막기) · '효과 배율'(`multiplier` — 능력을 쓸 때·패시브가 발동할 때, 기본 효과 Block이 곱함, 프로젝트 Block은 `Use.Context.Power`) · '비용 지불'(`amount`·`floor` = 빚 하한·`key`/`path` = 낼 곳·막기 = 공짜, 거르기 `forId` 무엇을 위한·`userId` 내는 쪽) · '상태 효과 걸기'(`statusId` 바꾸기·`stacks`·`length`·`maxStacks`·`pulse`·`kind`·`byId`·막기) · '확률'(`probability`) | 28·29 |
| 물리·충돌·투사체·미니게임 판정 | **판정 보고 행동**: 화면(MonoBehaviour)이 판정하고 결과(누가 누구를·성공)를 행동 하나로 보낸다(실시간은 틱마다 `Strike { hits }`). 규칙은 결과만. 위치·속도는 화면 쪽 | 33 |
| 능력치(공격력·방어)와 보정 | 숫자 필드 = 능력치. `Game.Attribute(대상, "attack")`(보정 포함 값), `Game.AddModifier(대상, "attack", ModifierKind.Percent, 0.2, 출처)`·`RemoveModifiers(출처)`. 같은 칸은 더하고 칸끼리 곱(플랫 → 퍼센트 → 독립 곱) | 28 |
| 비용 내기 | `Game.Pay(키, "battle.energy", 2, what: "energy.name", forId: 카드ID, user: 내는쪽)`(모자라면 플레이어용 실패, 행동 직전 이벤트 '비용 지불' 받기로 공짜·할인) · `Game.TryPay(…)`(모자라면 바꾸지 않고 거짓 — 틱 묶음 안의 "되면 낸다") · `Game.CanPay`(행동 직전 이벤트 전, 화면 미리 보기용). DB 결과 '비용 내기' | 28 |
| 플레이어에게 보일 실패 | `throw Game.Fail("fail.frozen", "얼어 있어 움직일 수 없습니다", ("turns", 2))`. 행동 전체 취소, 메시지는 읽을 때 지금 언어 | 30 |
| 언제 · 누구에게 · 무엇을(효과 조립) | 트리거(이벤트 받기·조건 충족·사용·행동 직전 이벤트 받기) → 결과(대상에게 효과(대상 Block → 효과 Block) · 플레이어 데이터에 주기 · 로그에 기록 · 이벤트 쏘기(값 틀) · 선택 요구 …) | 11·12, 기획 명세서 읽는 법 7절 |
| 조건("보스를 잡았고 골드 100 이상") | 조건 Block(필드 검사·플레이어 데이터 검사·배열 검사·AND/OR/NOT·로그 비교), 코드 조건은 `SimpleCondition` | 11 |
| 그 항목만의 특별한 결과 | `[BindTo(GameIds.X.Y)] static void F(이벤트 e, TriggerContext c)` 또는 `Reward` 클래스 | 13 |
| 스킬·아이템 쓰기 | 사용 기능 필드(`[SerializeReference] Usable usable`) 또는 DB › 능력 → `Game.Use(id, 사용자, 대상…)`·`Game.CanUse(…, out 이유)`. 조건이 안 맞을 때 글은 '쓸 수 없을 때 글' | 16 |
| 독·화상·보호막·분노(중첩·지속) | DB › 상태 효과(종류·중첩·다시 걸 때·지속(영구/이벤트 횟수/초)·능력치 보정·걸려 있는 동안 트리거·걸릴 때·끝날 때 결과), 효과 '상태 효과 걸기'·`Game.ApplyStatus`·`RemoveStatus`·`StatusStacks`·`HasStatus`·`Statuses` | 29 |
| 화상 → 푸른 화상·독 중첩 2배·면역·디버프 지속 +·최대 중첩 +·틱 속도 + | 행동 직전 이벤트 '상태 효과 걸기' 받기 + 행동 직전 이벤트 고치기(`statusId` 값으로·`stacks` 곱하기·막기·`length` 곱하기·`maxStacks` 더하기·`pulse` 곱하기) | 29 |
| 처치 기여(마지막에 건 쪽)·걸린 것으로 대상 거르기 | 걸린 것의 `by`(건 개체 id, 다시 걸면 바뀜 — 효과 '상태 효과 걸기'는 사용자, 코드 `ApplyStatus(…, by:)`) · 대상 '상태 효과로 거르기'(중첩·종류 가짓수 비교) | 29 |
| 패시브를 끄는 디버프(Ink·봉인) | 상태 효과 `silencesPassives`(걸린 쪽의 패시브를 끔 — 발동·보정·`Owned` 모두 0) | 29·17 |
| 버프·디버프로 묶기(정화·디버프 개수) | 상태 효과의 `kind`(종류) + 효과 '상태 효과 풀기'(종류 전부)·조건 '상태 효과 검사'(종류)·`Game.StatusCount(대상, 종류)`·`RemoveStatuses` | 29 |
| 실시간 지속 피해·재생(0.5초마다) | 상태 효과의 '주기(초)' + 결과 | 29 |
| 쿨다운 | 상태 효과(쿨다운, 지속 N턴·N초, 화면에 숨김) + 사용 조건 '상태 효과 검사'(<= 0) + 결과에 '상태 효과 걸기', 쿨다운 줄이기 = 효과 '상태 효과 줄이기'·`Game.ShortenStatus` | 29·16 |
| 가지고 있으면 발동(유물·특성·패시브 스킬·장비 효과·축복) | DB › 패시브(가진 목록 · 발동 · 가진 쪽 필드 · 결과), `Game.Owned(id)` = 개수·레벨 | 17·29 |
| 놓인 순서대로 발동(조커 칸·보드·참) | 패시브 `perElement`(놓인 것마다 — 가진 쪽 = 목록의 요소, 같은 목록끼리 목록 순서로 발동) · `matchField`(일치 필드 — 요소의 원본 ID 필드나 글자 목록) · `matchVia`(원본에서 읽기 — 원본(인스턴스)의 패시브 ID 목록) · `levelField`(레벨 필드, 0 이하 = 꺼짐). 조각 목록은 객체 목록(고유 id + 원본 ID) | 17 |
| 한 패시브가 순간마다 다른 결과(쌓기·빼기·내기·N번째마다) | 패시브 `rules`(발동 더 — 줄마다 트리거 + 결과) + 조건 '사용자 필드 검사'(조각의 카운터) | 17·11 |
| 가진 동안 N초마다 | 패시브 `pulse`·`pulseByLevel`(주기, 게임 시각 기준 — 멈추기·리셋은 필드 + 틱 행동) | 17 |
| 가진 동안 +N(유물 공격 +2, 레벨마다 다름) | 패시브 `modifiers`(가진 동안 능력치 보정 — 줄마다 `attribute`·`kind`·`value`·`perLevel` 레벨마다·`byLevel` 레벨별 값 3/6/10/14·`target` 받을 대상) · `modifierTarget`(보정 받을 대상, `party[]`면 그 모두) · `modifierWhen`(보정 조건 — 같은 줄에 혼자·체력 절반 이하) · `stacking`(여럿일 때 — 레벨 합·사본마다 값·가장 높은 하나). 저장하지 않고 읽을 때 계산 → 잃으면 저절로 빠짐. 트리거가 없어도 된다 | 17·28 |
| 대상의 숫자 필드를 바꾸기(이 조커 배수 +1·카운터 −1·처치 횟수 +1) | 효과 '필드 바꾸기'(더하기·곱하기·값으로 바꾸기, 값 틀, 0 아래로 내리지 않음, 더하기는 × 효과 배율) | 12 |
| 다른 조각의 패시브를 빌려 쓰기(Blueprint·Brainstorm) | 결과 '빌려 쓰기'(빌릴 조각 = 대상 Block, 예: 자리로 고르기 — 오른쪽). 원본 값을 읽고, 원본 자신의 쌓이는 값은 바꾸지 않고, 고리는 끊는다. 가진 쪽 필드는 빌리는 쪽 기준(빌린 결과 안에서 또 빌리면 처음 빌린 조각 — Brainstorm → Blueprint). 코드 `Game.BorrowPassive(ID, 원본)` | 17 |
| 조각을 차례로 쓰기(되는 것만) | `Game.TryUse(id, 사용자, 대상…)`(조건이 안 맞으면 실패 없이 거짓). 한 번 더 발동은 행동 직전 이벤트 '사용'의 `times` | 16 |
| 자리(옆 칸·같은 줄·맞은편·가장자리·석판 무늬) | 목록 + 줄 길이(0 = 한 줄, 빈 칸 = null). 코드 `Placement.Around`·`Is`·`Pattern(칸, 개수, 열, 무늬, 회전)`·`Rotate`, DB 대상 '자리로 고르기'(다른 목록 = 맞은편, 기준 '없음' = 목록 전체의 맨 처음·맨 끝)·조건 '자리 검사'. 열 우선 순서(와일드프로스트)는 목록을 열 우선으로 담고 '같은 열 …'. 플레이어마다 칸은 `players[].charms`(기준이 든 목록) | 37 |
| "3턴 뒤 폭발"·"2초 뒤" | 상태 효과의 끝날 때 결과(DB) / `Game.ScheduleAfter(초, 행동)`·`ScheduleOn<이벤트>(횟수, 행동)`(코드)·`CancelSchedule(s)` | 29 |
| 플레이어가 고르기("1장 버리기") | `Game.Ask(안내, 후보, new 이어서할행동())` + 행동의 `[Chosen]` 필드 → 화면이 `Game.Answer`. DB 결과 '선택 요구'(판에 있는 것에서)·'가중치 표에서 고르기'(DB 원본에서). 멀티: 답할 사람(`player`)을 생략하면 그 행동을 한 플레이어 — 그 사람 키(`$choices:번호`)에 두어 주인에게만 간다, 누구나는 `player: ""` | 29·30 |
| 대상 고르기(체력이 가장 낮은 적·불 속성 적 무작위 2·때린 쪽) | 기본 대상 Block: 플레이어 데이터 목록 → 정렬해서 n개 / 조건으로 거르기 → 무작위 n개 / 이벤트의 개체 | 16 |
| 예상 피해·"낼 수 없음" 이유 | `Game.Preview(행동, g => 읽기)`·`Game.CanDo(행동, out 이유)`(해 보고 되돌림, 기록·연출 없음) | 33 |
| 열린 상자·잡은 보스·스위치 | `Game.SetWorld("forest.chest3", true, permanent: true)`·`World`·`WorldFlag`·`ClearWorld`, 컴포넌트 '월드 상태 연결' | 32 |
| 실시간 게임의 같은 시간 규칙 | Bootstrap '초당 틱 수' → `session.Ticked`(틱마다)·`TickAlpha`(보간), 예약·상태 효과의 초 지속도 틱 | 33 |

### 2-3. 콘텐츠(DB) · 숫자 · 뽑기

| 하고 싶은 일 | 쓸 것 | 장 |
|---|---|---|
| 같은 모양의 원본 여러 개(카드·몬스터·아이템) | 원본 클래스(`[Serializable]`) + DB › 인스턴스. 판 데이터에는 원본 ID + 바뀌는 값만. `session.Instances(id)`·`CopyInstance(id)` | 04 |
| 등급·속성·태그 | `[KeywordGroup] static class Rarity { public const string Common = "common"; … }` + 필드 `[Keyword(typeof(Rarity))]` | 05 |
| 칸 숨기기·조건부로 보이기 | `[Hide]`·`[ShowIf(nameof(필드), 값…)]` | 04 |
| 프로젝트만의 DB 종류(유물·퀘스트) | `[Database("유물")] class RelicData : GameData { 트리거·결과 칸 }` + 감시 등록(`session.Triggers.Register`) | 17 |
| 기획자가 고칠 숫자 | DB › 밸런스 값 → `Game.Balance(GameIds.Balance.X)`, 곡선이면 `Game.Balance(id, 층)`(일정하게·비율로·거듭제곱·표, 반올림, 자르기) | 30 |
| 드롭·보상·조우(확률) | DB › 가중치 표 → `Game.Roll(id)`·`Roll(id, n, unique)`·결과 '가중치 표에서 주기'·'가중치 표에서 고르기'. 꽝·안쪽 표·개수·조건 | 30 |
| "이 묶음 = 전체의 n%"(밸런싱을 %로) | 가중치 표 줄의 방식 '고정 %'(`mode`·`percent`, 묶음 = 안쪽 표·목록 줄) + 나머지는 무게 줄이 나눔. 조건으로 빠진 고정 % 몫은 무게 줄이. 상세 위 요약에 줄마다 최종 % | 30 |
| 풀이 바뀜(해금·가진 것·판에서 생긴 것) | 줄의 조건(진행·도전과제) · 줄 종류 '플레이어 데이터 목록' · 뺄 목록 · 코드 풀 `Game.Roll(풀, n, unique, stream)`. 풀은 저장하지 않는다 | 30 5-10 |
| 보상 후보 +1·다시 뽑기 | '가중치 표에서 고르기'의 `countBonus`(후보 수에 더할 숫자, 플레이어 데이터) · `Game.RerollChoice(선택 id)`(같은 표·후보 수로 새로, 비용은 그 행동이). 기본 고르기 창의 버튼은 `GameScreens.Reroll = choice => 행동 또는 null` | 30·35 |
| 확률 공개 | `session.Tables.Chances(id)`(표의 지금 조건·목록·뺄 목록·밸런스 무게까지 — 같은 항목은 한 줄로 합친다. 코드 풀 `Game.Roll(풀…)`은 반영하지 않는다) | 30 |
| 천장(뽑을수록 확률이 오름)·운 | 가중치 표 줄 '무게를 밸런스 값에서'(단계 = 플레이어 데이터의 횟수 — id 있는 객체의 필드면 보정 포함 능력치, 행운 유물이 확률에 든다) | 30 |
| 딕셔너리 항목·목록의 id 요소를 가리키기 | 필드 경로 `stock[potion]`·`units[hero].hp`(조건·Game.Pay·주기·binding-path) | 11 |
| "10개 사기"·"살 수 있는 만큼" | `Game.BalanceSum(가격, 산 개수, 10)` · `Game.BalanceCount(가격, 산 개수, 가진 돈)` | 30 |
| 빚(돈이 −20까지) | 행동 직전 이벤트 '비용 지불' 받기 → `floor` 값으로 바꾸기 −20 | 28 |
| 한 번 달성(도전과제) | DB › 도전과제(트리거 → 결과), Steam이면 같은 ID로 | 14·34 |
| 쌓는 숫자(처치 수·최대 콤보) | DB › 로그(이벤트 받기 → 기록 규칙: 더하기·최대·마지막), `session.Stats.Get(id)` | 15 |
| 필수 값·스프라이트 틀 | 필드 규칙(클래스 상세)·스프라이트 틀 파일(Project 창 Create › MomoiProject › 스프라이트 틀 — 용도 하나의 크기 표(이름·가로·세로), 개발자가 Dev에서 만든다) | 27 |
| 수치 수십 개 한꺼번에 | DB 목록의 CSV(목록 칸 `entries[0].weight`) | 33 |
| AI가 DB 항목 만들기 | DB 반영 스크립트(`Assets/MomoiProject/Code/Editor/Seeds`, `[DbSeed("버전")]` 안에서 `EditorDatabase.Staging.Add(typeof(X), id)` → 컴파일 뒤 저절로 돈다 — 새 항목은 저절로 적용, 고치기·지우기는 '적용 전'으로 남아 개발자가 Ctrl+S) 또는 CSV | 관례 15절 |

### 2-4. 화면 · 연출 · 소리 · 글

| 하고 싶은 일 | 쓸 것 | 장 |
|---|---|---|
| HUD·상점·타이틀 같은 화면 | `Assets/MomoiProject/UI/Screens/X.uxml` + DB › 화면(층·뒤로로 닫기·열 씬) + `[BindTo(GameIds.Screen.X)] class X : GameScreen`. 먼저 메뉴 '화면 준비' | 35 |
| 값을 보이기만 하는 칸 | UXML `binding-path="run:gold"`·`"run:gold@hud.gold"`(텍스트 틀)·`"@hud.endTurn"`(번역 글). 코드 없음 | 35 2-2 |
| 버튼 → 행동 | `OnClick("이름", () => new 행동())`(Busy면 무시, 실패는 알림)·`GameScreens.Do(…)` | 35 2-4 |
| 데이터 목록 → 칸 목록 | `List<T>("컨테이너", Item("칸 UXML"), () => 목록, (칸, 항목) => …)`(`ElementList`) | 35 2-6 |
| 고르기 창·확인·설정·슬롯·일시정지·알림 | 기본 화면: 고르기 창은 저절로, `GameScreens.Confirm`·`Open(GameScreens.Settings)`·`Open(GameScreens.Slots, new SlotsRequest{…})`·`Open(GameScreens.Pause)`·`Toast` | 35 2-5 |
| 화면 색·글꼴 | `Assets/MomoiProject/UI/Theme.uss`의 `--momoi-…` 변수, DB › 화면 설정 › 글꼴(한글 TTF) | 35 2-7 |
| 씬에 놓인 오브젝트(캐릭터·카드) | `GameView.Redraw(그리기, 키…)`·`ViewList<항목, 컴포넌트>`(프리팹)·컴포넌트 '데이터 연결' | 32 |
| 피해 숫자·죽는 모습을 차례로 | `SubscribeAfterAction<이벤트>(e => GameView.Present(연출))`, 연출 시간은 `GameView.Wait`·`DeltaTime`, 입력 막기 `GameView.Busy` | 32 |
| 실시간 맞는 효과·클릭 숫자(입력을 막지 않음) | `GameView.Play(연출)`(대기열 밖, 동시에) | 32 |
| 쿨다운 원·타이머처럼 시간에 따라 바뀌는 칸 | `GameScreen.Tick(초)` | 35 |
| 연출을 UI 칸 위에 | `GameView.FindElement(id)` + `GameScreens.Overlay` | 35 5-4 |
| 씬 바꾸기 | 판 데이터의 씬 필드를 행동이 바꾸고 Bootstrap '씬 흐름'이 연다(`GameScenes.Transition`으로 페이드) | 32 |
| 플레이어에게 보일 글·번역 | 텍스트 표(키 → 글), `Texts.Get("hud.energy", ("energy", n))`·`Texts.NameOf(id)`(`<id>.name`). 틀 문법 `{값}`·조사 `{monster:을}`·복수 `{count, plural, one {…} other {…}}` | 23 |
| 언어 바꾸기 | `GameLanguage.Set(code)`(설정 '언어'가 한다)·`Available` | 23·31 |
| 소리 | `GameAudio.PlayBgm`·`PlaySfx`·`PlayUi`·`PlayVoice`·`StopBgm`, 채널 음량은 설정 적용 대상 | 26 |

### 2-5. 기기 · 출시 · 개발 도구

| 하고 싶은 일 | 쓸 것 | 장 |
|---|---|---|
| 설정(음량·언어·화면·전투 속도) | DB › 설정(종류: 켜기/끄기·숫자·선택지·글, 적용 대상: 언어·음량·전체 화면·해상도·수직 동기화·최대 프레임) → `GameSettings.Number(id)`·`Toggle`·`Choice`·`Set`·`Changed`. 화면은 기본 설정 화면 | 31·35 |
| AI가 Unity 상태 읽기(창을 볼 수 없으므로) | DB 검증 오류·경고·'적용 전' 변경 = `Logs/MomoiProject/DbStatus.txt`(컴파일 뒤·DB가 바뀔 때 다시 쓰임, `DbStatusReport`) · Unity 로그 = `Logs/Editor.log`(Unity 6.6은 프로젝트 안) | 관례 12 |
| 플레이 중 값·기록 보기, 이벤트 쏴 보기, 버튼 하나로 시험 | 모니터(타임라인·데이터·행동·명령·이벤트 호출), DB › 개발 명령(결과 Block 조립 → 명령 탭 버튼) | 22 |
| DB 항목 만들기·고치기·지우기(AI) | DB 반영 스크립트: `Assets/MomoiProject/Code/Editor`의 `[DbSeed("버전")] static void F()` 안에서 `EditorDatabase.Staging.Add·Work·Remove` → 컴파일 뒤 저절로 돈다(새 항목은 저절로 적용, 고치기·지우기는 '적용 전' → 개발자가 Ctrl+S). 이미 있는 항목은 건너뛴다 | 관례 15, 21 |
| 개발자가 에디터에서 바꾸게(밸런싱) | 숫자 = 밸런스 값·DB 칸·인스턴스 필드, 가리키는 것 = `[DataRef(typeof(…))]` 드롭다운, 효과 = Block 문장(`[BlockInfo(Sentence = "…{필드}…")]`). DB는 구조를 잠그고 값만 연다(`EditorPolicy`) | 관례 18, 21 |
| 개발자가 추가·삭제하는 DB 종류(레벨 배치·대사) | `[Database("층", DeveloperAuthored = true)]` — 이 종류만 DB에서 추가·삭제·ID 바꾸기가 열린다. 인스턴스(DB › 인스턴스)는 잠겨 있으니 개발자가 배치할 레벨 데이터에 쓰지 않는다 | 관례 18, 21 |
| 테스트 씬·패널·상황 | `TestSceneSetup.Ensure(copyFrom:, scenario:)`(반영 스크립트에서) · 컴포넌트 `TestScene`(test 슬롯·시작 상황·패널 F1) · `TestScenarios.Save·Load(session, 이름, seed)` | 관례 19, 38 |
| 테스트 명령(주입·발동) | DB › 개발 명령(결과 Block, `category`로 묶음) — 반영 스크립트로 만든다. 코드면 `session.Debug.Register` | 22, 38 |
| 승률·평균 턴(자동 시뮬레이션) | `TestScenarios.Simulate(session, 상황, runs, step, measure)` → `TestReport`(`ToText`·`ToCsv`·`SaveReport`) | 38 |
| 그림·소리 자리와 넣을 것 목록 | `AssetRequests.Sprite(path, w, h, 쓰는 곳)`·`Audio(path, 쓰는 곳, seconds)`·`Request(path, 쓰는 곳)`(반영 스크립트에서) → 개발자가 에셋 관리 › 넣을 것에서 진짜 파일을 넣는다(같은 자리·GUID 유지) | 관례 18, 39 |
| 결정 기록 | `Docs/Decisions.md`(세션마다 먼저 읽기, 개발자의 답·추천 승인을 `D-n`으로 — 기획은 넘겨짚지 않는다, 답을 기다리는 것은 '확인 기다리는 것'에) | 진행 매뉴얼 2 |
| 규칙 테스트(Unity 없이) | `new GameSession(new SessionOptions { Seed = 1 })` → `Data.Define` → `Game.Do` → 단언. 플레이어용 실패는 `Assert.Throws<GameFailure>` | 관례 12절 |
| 멀티 시작(혼자 시험·직접 접속·친구 방·매칭큐) | 시험 모드 `GameNet.StartTestMode(latency)` · 직접 접속 `GameNet.HostDirect(포트)`·`JoinDirect(주소)` · 친구 방 `GameNet.CreateRoom(4)`·`InviteToRoom()`·`JoinRoom(대상)`(Steam이 준비되면 Steam 로비, 아니면 직접 접속 — `GameNet.Rooms`) · 매칭큐 서버 `GameNet.StartMatchServer(포트, 인원)` + `FindMatch(주소)` → `Client.Queue()`·`AnswerMatch(true)` · 화면 없는 서버 `StartDedicated`(명령줄 `-momoi-server`·`-momoi-matchserver`) · 테스트 씬 › 멀티플레이 · 다시 접속 `GameNet.AutoReconnect`·`Reconnecting`·`Reconnected` · 리플레이 `GameNet.RecordReplays`·`LastReplay`·`SaveReplay()`·`PlayReplay(…)`·`Replay.Seek(초)`·`StopReplay()` · 숨길 이벤트 `IOwnedEvent`·`[GameEvent(ServerOnly = true)]` · 실제 서비스 자리 `GameNet.TokenSource`·`Rooms`·`INetAuthenticator`·`INetMatchmaker`·`INetPlayerStore` | 관례 20, 40 |
| 멀티 서버 규칙(차례·숨김·결과) | `GameNet.ServerStarted`·`RoomStarting`에서 `server.Validate = (player, action) => 거절 글 또는 null` · 플레이어별 키 + `Data.SetVisibility(키, DataVisibility.Owner, 번호)` · `RoomReady`에서 첫 턴(서버를 다시 켜 이어 연 매치에는 다시 오지 않는다) · 끝나면 `GameNet.Hub.EndMatch(room, NetMatchResult[])`(Elo·전적 저장) · 내 번호 `session.LocalPlayer` | 관례 20, 40 |
| 실시간 협동의 움직임 | 컴포넌트 `NetTransformSync`(entityId·owner) — 내 것은 보내고 남의 것은 보간(받는 쪽은 주인이 보낸 위치만 받는다), 판정은 방장 규칙(클라이언트는 판정 보고 행동) | 40 |
| n턴 되돌리기 기믹 | 턴 시작 행동에서 `Game.MarkRewind()` → 기믹 행동에서 `Game.Rewind(n)`·`RewindKey(키, n)`·`RewindEntity(객체, n)`(하나의 행동 — Undo·멀티·저장에 남음) | 40 |

### 2-6. 부가 기능(명세서 6절 — 켜면 붙는 것)

기믹이 아닌 장르 부품이다. 명세서에 표시된 것만 붙인다.

| 부가 기능 | 쓸 것 | 장 |
|---|---|---|
| 대사·컷신 | 선택 기능 다이얼로그: 그래프 창, `Dialogues.Play(id)`·`Signaled`·`ResumeSaved`. 대화가 중심인 게임이면 24장의 지금 동작(대사 진행은 행동 밖·되감기 없음 등)을 먼저 본다 | 24 |
| 겹치는 가방 | 선택 기능 인벤토리: 필드 `Inventory { capacity }` → `Add`·`Remove`·`CountOf`·`Has`. 고정 칸·칸 정보가 필요하면 자리(`List<T>` + `Placement`) | 25 |
| 게임 안 달력·시각 | 판 데이터에 날짜·시각, 하루 넘김 행동이 이벤트를 쏜다 | 구현 가이드 07 |
| 현실 시각(오프라인 보상·스태미나·일일 초기화) | `Game.Now`(UTC, 행동에 기록되어 Undo·멀티에서 같다) · `Game.Day(초기화 시각)` · `Game.Week()`. 규칙에서 `DateTime.Now`를 쓰지 않는다. 꺼 둔 시간 = `Game.Now − profile.lastSeen` | 07 |
| 시드 입력·일일 도전 | `GBibleRuntime.NewRun(session, RandomStreams.SeedOf(글자))`(같은 글자 = 같은 판, 일일은 날짜 글자) | 07 |
| 칸 단위 맵(전술·던전·농장) | `Grid`: `Distance`·`Neighbors`·`Reachable`(이동 범위)·`Path`(길찾기 — 기본은 늘 가장 싼 길, `minCost`는 모든 칸 비용 이하일 때만 빨라지는 힌트)·`Line`·`Visible`(시야)·`Area`·`Ray`·`Cone`·`Rotate`, `Cell`·`Grid.Key(x, y)`. 맵은 원본 인스턴스(rows 글자 목록) 또는 `Dictionary<string, …>`(키 "x,y") | 36 |
| 1e308을 넘는 수 | `BigNumber`(필드 하나만, 연산자 그대로, "1.23Qa"·"1.23억" 표기 — `ToString("ko")`·`"sci"`·`"full"`). 1e300 아래는 double 그대로라 정수·0.5를 쌓아도 오차가 없다. 비용 검사는 코드 | 36 |
| 되감기("이번 턴 처음으로") | `session.HistoryMark`·`UndoTo(지점)`(행동 단위 Undo) | 02 |
| 세이브 슬롯 1·2·3 | `GBibleRuntime.UseSlot(이름)`·`SlotInfoOf`·`DeleteSlot`·`CopySlot`·`SlotSummary`(한 줄 요약 함수), 기본 슬롯 화면 | 31·35 |
| 키 바꾸기 | 선택 기능 입력: `GameInput.Use(actions)`·`Rebind`·`ResetBindings`·`BindingText` | 31 |
| Steam | 선택 기능 Steam + DB › Steam(App ID): 자동 초기화·도전과제·로그 → 통계·`GameSteam.SetPresence`·Cloud(Auto-Cloud 안내). 리더보드·친구 초대는 없다(필요하면 질문) | 34 |

---

## 3. API 요약

### 3-1. `Game` (`GameApi`) — 행동·결과·규칙 함수 안에서

| 분류 | 멤버 |
|---|---|
| 행동 | `Do(행동)` · `Use(능력 id, 사용자, 대상…)` · `TryUse(…)`(조건이 안 맞으면 거짓) · `CanUse(…, out 이유)` · `Preview<T>(행동, g => 읽기)` · `CanDo(행동, out 이유)` · `IsPreviewing` · `Now`(현실 시각) · `Day(초기화 시각)` · `Week()` |
| 데이터 | `Data()`(생성 접근자: `Game.Data().Run`) · `Get<T>(키)` · `Set(키, 값)` · `Find<T>(id)` · `Find(id)` · `All<T>()` · `All<T>(키)` · `NewId(접두)` · `NewIds(접두, n)` · `static IdOf(객체)` · `static HasId(형식)` |
| 이벤트·난수 | `Events`(`Publish`·`Subscribe`·`SubscribeAfterAction`) · `Random(흐름)` · `Chance(확률, 흐름, source, user)`(행동 직전 이벤트 '확률'을 거침) |
| 능력치 | `Attribute(대상, 이름)` · `AddModifier(대상, 이름, 칸, 값, 출처, permanent)` · `RemoveModifiers(출처, 대상)` · `Modifiers(대상, 이름)` |
| 행동 직전 이벤트·비용·실패 | `Intercept<T>(행동 직전 이벤트)` · `Pay(키, 경로, 양, what, forId, user)` · `TryPay(키, 경로, 양, forId, user)` · `CanPay(키, 경로, 양)` · `Fail(키, [기본 문장,] (이름, 값)…)` |
| 상태 효과·패시브 | `ApplyStatus(대상, id, 중첩 = 1, 출처, 길이 = 기본, by = 건 개체)`(행동 직전 이벤트 '상태 효과 걸기'를 거침, 막히면 null) · `RemoveStatus(대상, id, 중첩 = 0)` · `ShortenStatus(대상, id, 양)` · `StatusStacks` · `HasStatus` · `Statuses(대상)` · `StatusCount(대상, 종류)` · `RemoveStatuses(대상, 종류)` · `Owned(패시브 id, 가진 쪽)`(놓인 것마다면 가진 쪽 = 그 요소) · `BorrowPassive(패시브 id, 원본)` |
| 예약 | `ScheduleAfter(초, 행동, 출처, permanent)` · `ScheduleOn<이벤트>(횟수, 행동, 출처, permanent)` · `CancelSchedule(id)` · `CancelSchedules(출처)` |
| 선택 대기 | `Ask(안내, 후보(객체·id 글자), 이어서, min, max, source, player, label)` · `Ask(안내, ChoiceCandidate들, …)` · `Choice` · `Choices` · `IsWaiting`(`Choice`·`IsWaiting`은 이 프로그램이 답할 선택만 — 멀티면 모두에게 온 것과 내 번호의 것) · `Answer(선택 id, 고른 것…)` · `CancelChoice(id)` · `RerollChoice(id)`(표로 만든 선택을 새로 뽑기) |
| 밸런스·뽑기 | `Balance(id[, 단계])` · `BalanceSum(id, from, n)` · `BalanceCount(id, from, 예산)` · `Roll(표 id)` · `Roll(표 id, n, unique)` · `Roll(풀 (id, 무게)…, n, unique, stream)` · `Roll(id 목록, n, unique, stream)` |
| 월드 상태 | `World(id)` · `WorldFlag(id)` · `SetWorld(id, 값/참거짓, permanent)` · `ClearWorld(id)` |
| 세션 | `Session`(아래) |

### 3-2. `GameSession` · `SessionOptions`

- `Do(행동)` · `Undo()` · `UndoCount` · `HistoryMark`·`UndoTo(지점)` · `Data` · `Events` · `Journal`(`Append(JournalKind, 시스템, 글)`) · `Stats`(`Get`·`Report`) · `Achievements`(`IsUnlocked`·`Unlock`) · `Triggers`(`Register(주인, 트리거, repeat, fire)`) · `Tables`·`Balance`·`Statuses`·`Schedules`·`Choices`·`World`·`Attributes` · `Debug`(`Register(분류, 이름, 할 일, 설명)` — 모니터 명령 탭) · `Instances`·`CopyInstance(id)` · `Ticked`(이벤트)·`TickAlpha` · `ExportRun()`·`ImportRun(json)`.
- `SessionOptions`: `Seed` · `History`(`Actions`·`Snapshots`·`None` — DB 이름 '쓰지 않음') · `RealClock`(Game.Now의 출처) · `TimeZone`(Day·Week) · `HistoryLimit` · `SnapshotInterval`·`SnapshotLimit` · `TickRate` · `SaveVersion` · `Migrations` · `DetectUntrackedChanges`. Unity에서는 DB › Bootstrap이 채우고, 코드가 바꿀 것만 `GBibleRuntime.Configuring += o => …`.

### 3-3. 행동 `GameAction`

```csharp
[Serializable]
public class PlayCard : GameAction
{
    public DeckCard card;      // 입력(객체도 된다 — 기록에는 id)
    public Fighter target;
    protected override void Run()
    {
        var data = Rules.CardData(Game, card.card);
        Game.Pay(GameIds.PlayerData.Run.Id, "battle.energy", data.cost, what: "energy.name");
        Game.Use(card.card, Game.Data().Run.hero, target);
    }
}
```

- `Run` 안에서만 데이터를 바꾼다. 예외가 나면 그 행동이 바꾼 것이 모두 취소된다. 안에서 다른 행동을 `Game.Do`하면 같은 Undo 단위.
- `Describe()`(타임라인 글) · `Visibility`(`Public`·`Private`(본인에게만)·`Developer`) · `utc`(바깥 행동이 시작한 현실 시각, 저절로). static 상태·Unity API를 쓰지 않는다(Rules 어셈블리).

### 3-4. 플레이어 데이터

- DB › 플레이어 데이터: 키 · 구역(`Session` = 판, `Meta` = 프로필) · 값 형식. 생성 상수 `GameIds.PlayerData.Run`(`PlayerDataKey<RunState>`, `.Id`), 접근자 `Game.Data().Run`.
- 코드로: `session.Data.Define("run", DataZone.Session, new RunState(), "판")` · `Get<T>(키)` · `Set(키, 값)` · `IsDefined(키)` · `Changed`(이벤트).
- 담을 수 있는 것: 숫자·글자·참거짓·enum·`[Serializable]` 클래스·List·배열·Dictionary(글자/숫자 키). 에셋 참조는 원본(DB)에 두고 판 데이터에는 ID. 한 키 안에서 같은 객체를 두 곳에서 가리키거나 서로 가리켜도 된다(Undo·저장·불러오기가 같은 인스턴스로 지킨다 — 컬렉션 인스턴스는 한 곳에서만, 다른 키끼리는 나눠 쓰지 않는다).
- 라이브러리가 쓰는 키: `$statuses` · `$schedule` · `$choices` · `$world`(·`.meta`) · 로그 `stat:<ID>` · 도전과제 `achievement:<ID>`. 직접 고치지 않는다.

### 3-5. 이벤트 · 행동 직전 이벤트 · 컨텍스트

- 이벤트: `[Serializable, GameEvent("턴 끝")] public class TurnEnded { public int turn; public string playerId; }`. 필드 = 매개변수(받는 쪽·트리거 필터가 읽는다). 이름은 과거형(`EnemyKilled`). 쏘기 `Game.Events.Publish(…)`.
- 행동 직전 이벤트: `[Serializable, Intent("피해")] public class DamageIntent { public string sourceId, targetId; public int amount; public bool cancelled; }` → 규칙 함수가 `intent = Game.Intercept(intent); if (intent.cancelled) return;`. GBible 기본 행동 직전 이벤트: `UseIntent`(사용) · `PowerIntent`(효과 배율 → `TriggerContext.Power`) · `CostIntent`(비용 지불, `floor`·`forId`·`userId`) · `StatusIntent`(상태 효과 걸기, `maxStacks`·`pulse`·`byId`) · `ChanceIntent`(확률).
- 컨텍스트(`TriggerContext`): `Session` · `Game` · `Owner`(그 결과를 가진 DB 항목 ID, 상태 효과면 상태 효과 ID) · `Event`(받은 이벤트·받은 행동 직전 이벤트) · `IsIntercept` · `IsUse` · `User`(사용자, 상태 효과면 가진 쪽) · `Picked`(고른 대상·고른 것).

### 3-6. Block 만들기(재사용 부품)

| 종류 | 상속 | 뼈대 |
|---|---|---|
| 효과 | `Effect<T>` | `[Serializable, BlockInfo("피해")] public class DealDamage : Effect<Fighter> { public int amount = 6; protected override void Apply(Fighter target) => Rules.Damage(Game, User<Fighter>(), target, amount); }` |
| 대상 | `TargetSelector<T>` 또는 `TargetSelector` | `protected override IEnumerable<Fighter> Select(IEnumerable<Fighter> targets, Fighter user)`. 컨텍스트는 `Use.Context`(이벤트에서 대상 읽기) |
| 조건 | `SimpleCondition` | `protected override bool Check() => Game.Get<RunState>("run").floor >= 3;` |
| 결과(코드) | `Reward` | `public override void Grant(GameSession session) { … Context.Picked … }` + `Effect => RewardEffect.Reversible` |
| 항목 하나의 결과 | `[BindTo(id)]` 함수 | `static void F()` · `F(이벤트 e)` · `F(TriggerContext c)` · `F(이벤트 e, TriggerContext c)` (static, void) |

- `[BlockInfo("이름", Category = "…", Description = "…", Sentence = "{필드} …")]`: DB의 + 메뉴에 보일 이름·설명·요약 문장. 필드에는 `[FieldLabel("한글")]`.
- 결과 칸의 잠금: `[TriggerSlot(TriggerKinds.Event | …)]` · `[OutcomeSlot(OutcomeKinds.…, BindTo = false)]`.

### 3-7. 어트리뷰트

| 어트리뷰트 | 붙이는 곳 | 뜻 |
|---|---|---|
| `[Serializable]` | 데이터·행동·이벤트·Block | 저장·복사·기록 |
| `[GameEvent("이름")]` · `[Intent("이름")]` | 이벤트 · 행동 직전 이벤트 클래스 | DB에 보이는 이름 |
| `[BindTo(id)]` | static 함수 · `Reward` 클래스 · `GameScreen` 클래스 | 그 DB 항목에 코드 잇기 |
| `[Chosen]` | 이어서 할 행동의 필드(string·객체·그 목록) | 고른 것이 들어간다 |
| `[BlockInfo]` · `[FieldLabel]` | Block 클래스 · 필드 | DB에 보이는 이름 |
| `[KeywordGroup]` · `[Keyword(typeof(그룹))]` | static 클래스 · 글자 필드 | 고르는 값 |
| `[Hide]` · `[ShowIf(필드, 값…)]` | 필드 | 숨기기 · 조건부로 보이기 |
| `[DataRef(typeof(DB 종류))]` | 글자 필드 | DB 항목 ID 고르기 |
| `[PlayerDataRef]` · `[FieldPathRoot]` · `[FieldPath]` | 글자 필드 | 플레이어 데이터 키 · 필드 경로 고르기 |
| `[Database("이름", Group, Order, IdPrefix)]` | `GameData` 상속 클래스 | 프로젝트 DB 종류 |

### 3-8. Unity 쪽

| 형식 | 멤버 |
|---|---|
| `GBibleRuntime` | `Session` · `Game` · `Start()` · 이벤트 `Configuring`·`Started`·`Stopped`·`Saving`·`RunLoaded`·`Stopping` · `Slot`·`UseSlot`·`SlotInfoOf`·`Slots()`·`DeleteSlot`·`CopySlot`·`SlotSummary`·`PlayTime` · `SaveProfile`·`SaveRun`·`LoadRun`·`NewRun(session, 시드)`·`DeleteRun`·`HasSavedRun`·`AutoSaveRun` |
| `GameView` | `Redraw(그리기, [waitForPresentation,] 키…)` · `Present(코루틴 / done => … / 초)` · `Play(코루틴 / done => …)`(대기열 밖) · `IsPresenting` · `Busy` · `Wait(초)` · `DeltaTime` · `PresentationSpeed` · `SkipPresentation` · `ClearPresentation` · `Register`·`Find<T>(id)`(GameObject) · `RegisterElement`·`FindElement(id)`(UI 칸) |
| `ViewList<항목, 컴포넌트>` | `(프리팹, 부모, () => 목록, (뷰, 항목) => …, 키…)` · `OnCreate` · `OnRemove` · `ViewOf(id)` |
| `GameScreens` | `Open(id, 값)` · `Close` · `CloseAll` · `IsOpen` · `Get` · `All` · `Back()` · `HoldBack()`(Esc를 다른 뜻으로 쓰는 동안 뒤로 막기) · `Do(() => 행동, whileChoosing)` · `Reroll`(고르기 창의 다시 뽑기 행동) · `Toast` · `Confirm` · `Overlay` · `QuitToTitle` · `Quit()` · `Opened`·`Closed` · ID `Choice`·`Confirmation`·`Settings`·`Slots`·`Pause` |
| `GameScreen` | `OnOpen`·`Draw`·`OnClose`·`Tick(초)` · `Watch` · `WaitForPresentation` · `Q<T>(이름)` · `OnClick(이름, () => 행동 / 할 일)` · `List<T>(…)` · `Item(이름)` · `Own(…)` · `Close()` · `Root`·`Argument`·`Data` |
| `ElementList<항목>` | `(컨테이너, 칸 UXML, () => 목록, (칸, 항목) => …, 키…)` · `OnCreate` · `OnRemove` · `Find(id)` |
| `GameSettings` | `Toggle`·`Number`·`Choice`·`Text`·`Get(id)` · `Set(id, 값)` · `Reset`·`ResetAll(분류)` · `All` · `Label`·`ChoicesOf` · `Changed` · `Raw`·`SetRaw`(모듈용) |
| `GameScenes` | `Follow(키, 경로, 빈 값 씬)` · `Transition`(페이드 코루틴 감싸기) · `Loaded` · `Current` |
| `GameTime` | `Pause(이유)`·`Resume(이유)`·`ResumeAll`·`TogglePause` · `SetSpeed` · `StepFrame` · `IsPaused`·`Speed` |
| `GameAudio` | `PlayBgm(clip, volume, fade, loop)`·`StopBgm` · `PlaySfx`·`PlaySfxAt` · `PlayUi` · `PlayVoice`·`StopVoice` · `Stop(채널)`·`StopAll` · `Volume(채널)` |
| `Texts` · `GameLanguage` | `Texts.Get(키, (이름, 값)…)` · `NameOf(id)` · `Has(키)` · `Language` / `GameLanguage.Set(code)` · `Available` |
| `TestScenarios` · `TestScene` · `TestReport` | `Save(session, 이름)`·`Load(session, 이름, seed)`·`Delete`·`Names()`·`Simulate(session, 상황, runs, step, measure, maxSteps, firstSeed)`·`SaveReport` · 컴포넌트 `TestScene`(scenario·seed·separateSlot·speed·showPanel·panelKey) · `TestReport.Average·Min·Max·ToText·ToCsv` |
| `GameNet` · `NetTransformSync` | `StartTestMode(latency, jitter, extraClients, 이름, prepareServer)` · `Host(전송, 이름)`·`HostDirect(포트, 이름)` · `Join(전송, 이름)`·`JoinDirect(주소, 이름)` · `StartDedicated(포트)` · `StartMatchServer(포트, 인원, auth, matchmaker, store)` · `FindMatch(주소, 이름)` · `Leave()` · `Server`·`Client`·`Hub`·`IsHost`·`IsClient`·`IsTestMode`·`TestNetwork`·`Sessions` · 이벤트 `ServerStarted`·`RoomStarting`·`RoomReady`·`Joined`·`Closed`·`PlayersChanged` · 컴포넌트 `NetTransformSync`(entityId·owner·sendRate·delay·syncRotation) |
| 에디터(GBible.EditorTools) | `[DbSeed("버전")]`·`DbSeeds.RunPending` · `EditorDatabase.Staging.Add·Work·Remove` · `EditorPolicy.CanEditStructure·CanAddRemove(형식)` · `TestSceneSetup.Ensure(path, copyFrom, scenario)` · `AssetRequests.Sprite·Audio·Request·MarkDone` · `AssetTools.Import·Replace·UsedBy·Unused·MoveToUnused·Misplaced·Tidy·FolderFor` |

### 3-9. 선택 모듈

| 모듈(기능 활성화) | 멤버 |
|---|---|
| 다이얼로그 | 그래프 창(`MomoiProject → 다이얼로그 시스템 → 그래프`), DB › 스탠딩 프로필·장소, `Dialogues.Play(id, 시작 ID)` · `Advance` · `Choose(i)` · `Stop` · `IsPlaying` · `ResumeSaved()` · 이벤트 `Started`·`Ended`·`Signaled`(그래프의 신호 보내기), 결과 '다이얼로그 재생' |
| 인벤토리 | `Inventory { capacity, slots }` 필드 · `Add(id, 개수, 최대 겹침)` · `Remove` · `TryRemove` · `CountOf` · `Has` · `RoomFor` |
| 입력(키 바꾸기) | `GameInput.Use(InputActionAsset)` · `Find(이름)` · `Rebind(동작, i, done)` · `CancelRebind` · `ResetBindings` · `BindingText` (설정 파일에 저장) |
| Steam | DB › Steam · `GameSteam.IsReady` · `SetPresence(키, 값)` · `ClearPresence` · `StoreNow` (도전과제·통계는 저절로) · 친구 방 `SteamLobby.Create(인원)`·`Invite()`·`Join(로비)`·`Leave()`·`Current`·`IsHost`(화면은 보통 `GameNet.CreateRoom` — Steam이 준비되면 `SteamRoomProvider`가 들어간다) · 인증 티켓 `SteamTickets.Request`(GameNet.TokenSource에 저절로) · 전송 `SteamServerTransport`·`SteamClientTransport` |

---

## 4. DB 종류

| 사이드바 그룹 › 탭 | 형식 · ID 앞부분 | 항목 하나 | 상수 | 장 |
|---|---|---|---|---|
| 시작 파일 › Bootstrap | `GameDatabase` | 세션 옵션: Undo 방식·보관 수·Undo 끊는 이벤트·판 저장·판 끝 이벤트·시작할 때 이어 하기·초당 틱 수·씬 흐름·개발 시드 | — | 20 |
| 요소 › 플레이어 데이터의 cs ↗ | (PlayerData.cs) | 판·프로필 클래스(RunState·Profile) | — | 01 |
| 선언 › 키워드 · 클래스 · 이벤트 | (코드) | 읽기 전용 목록 | — | 05·10 |
| 선언 › 로그 | `StatData` · `stat` | 쌓는 숫자(항목이 없으면 탭이 숨는다) | `GameIds.Stat` | 15 |
| 선언 › 플레이어 데이터 | `PlayerDataEntry` · `playerData` | 키·구역·형식 | `GameIds.PlayerData` | 01 |
| 선언 › 설정 | `SettingData` · `setting` | 설정 칸 하나 | `GameIds.Setting` | 31 |
| 선언 › 화면 설정 | `ScreenSettingsData` · `screens` | Panel Settings·테마·글꼴(하나) | — | 35 |
| 개발 › 도전과제 | `AchievementData` · `achievement` | 트리거 → 결과(항목이 없으면 탭이 숨는다) | `GameIds.Achievement` | 14 |
| DB › 능력 (선택 요소) | 프로젝트의 `AbilityData`(규격 `IUsableData`) · `ability` | 쓰는 것(조건·대상·효과·쓸 수 없을 때 글) | `GameIds.Ability` | 16 |
| DB › 상태 효과 (선택 요소) | 프로젝트의 `StatusData`(규격 `IStatusData`) · `status` | 버프·디버프·쿨다운(`kind` 종류·중첩·지속·주기·보정·트리거·결과·아이콘·화면에 숨김) | `GameIds.Status` | 29 |
| DB › 패시브 (선택 요소) | 프로젝트의 `PassiveData`(규격 `IPassiveData`) · `passive` | 가지고 있으면 발동(`ownerKey`·`ownerPath` 가진 목록 · `matchField` 일치 필드 · `matchVia` 원본에서 읽기 · `perElement` 놓인 것마다 · `levelField` 레벨 필드 · `trigger` 발동 · `holderField` 가진 쪽 필드 · `outcomes` 결과 · `rules` 발동 더 · `pulse`·`pulseByLevel` 주기 · `modifiers` 가진 동안 능력치 보정 · `modifierTarget` 보정 받을 대상 · `modifierWhen` 보정 조건 · `stacking` 여럿일 때 · `icon`) | `GameIds.Passive` | 17·29 |
| DB › 밸런스 값 | `BalanceData` · `balance` | 숫자 + 곡선(쓰는 값만) · 표시 이름(`label`, 개발자가 한글로 고침)·입력 칸 주석(`hint`)·메모. 전용 탭: 접고 펼치기·마우스를 올리면 입력 칸·✎ | `GameIds.Balance` | 30 |
| DB › 가중치 표 | `WeightTableData` · `table` | 풀 + 무게 또는 고정 %(줄마다) | `GameIds.WeightTable` | 30 |
| DB › 화면 | `ScreenData` · `screen` | UI Toolkit 화면 | `GameIds.Screen` | 35 |
| DB › 인스턴스 | `InstanceData` · `instance` | 원본 하나(클래스의 값) | `GameIds.Instance` | 04 |
| DB › 프리팹 | `PrefabData` · `prefab` | 프리팹 연결 | `GameIds.Prefab` | 21 |
| (파일 — Dev) 스프라이트 틀 | `SpriteFrameData` | 용도 하나의 크기 표(이름·가로·세로) | `GameIds.SpriteFrame` · `GameIds.SpriteSize.<틀>.<크기>` | 27 |
| 데이터 › (프로젝트 종류) | `GameData` 상속 | 유물·퀘스트 … | `GameIds.<이름>` | 17 |
| 개발 › 개발 명령 · 저장 변환 · Steam | `DebugCommandData` · `SaveMigrationData` · `SteamData` | 명령 탭 버튼 · 변환 한 단계 · Steam 설정 | — | 22·06·34 |
| 다이얼로그 시스템 › 스탠딩 프로필 · 장소 | (선택 기능) | 화자 그림 · 장소 | — | 24 |

- 저장(Ctrl+S)하면 `GameIds.cs`가 다시 만들어진다. 항목 이름(플레이어에게 보일 글)은 텍스트 `<ID>.name`.
- 'DB' 그룹의 로직 종류와 도전과제·로그는 항목이 없으면 탭이 숨는다 — 그 프로젝트가 쓰는 것만 보인다(AI가 반영 스크립트로 항목을 만들면 나타난다).

### 4-1. 선택 요소(상태 효과 · 패시브 · 능력)

- 어떤 프로젝트는 상태 효과가 없다. 그래서 이 셋의 **DB 종류(데이터 칸 클래스)는 GBible에 없고 프로젝트가 가진다**. 엔진(걸기·중첩·지속·주기·가진 것 세기·놓인 것마다·`Game.Use`·행동 직전 이벤트 '상태 효과 걸기'·기본 Block·`Game.ApplyStatus`·`Game.Owned` …)은 GBible에 있다.
- 쓰기로 했으면(시스템 목록) 단계 1에서 템플릿을 복사한다: `Packages/com.momoi.gbible/Documentation~/AI/Templates/Elements/{StatusData,PassiveData,AbilityData}.cs.txt` → `Assets/MomoiProject/Code/Rules/Elements/<이름>.cs`(네임스페이스는 프로젝트 것 그대로 `MomoiProject`). 템플릿의 칸은 이 문서·매뉴얼(16·17·29장)이 말하는 칸과 같다.
- **규격**: 엔진은 클래스 이름이 아니라 규격 인터페이스만 본다 — 상태 효과 `IStatusData.ToDefinition()`, 패시브 `IPassiveData.ToDefinition()`, 능력 `IUsableData.ToUsable()`. 게임이 켜질 때 그 규격을 붙인 DB 항목을 모두 세션에 등록한다. `[DataRef(typeof(IStatusData))]`는 그 규격을 붙인 종류의 항목을 고른다(기본 Block이 이렇게 가리킨다).
- **칸이 더 필요하면 그 클래스에 더한다**(전투용·런용, 마나 비용, 날아가는 시간 …). 라이브러리 변경이 아니라 프로젝트 변경이다 — 사람에게 묻지 않고 보고에 적는다. 더한 칸은 엔진의 정의(`ToDefinition`)에 넣지 않고, 프로젝트 규칙이 DB 항목에서 직접 읽는다.
- 그 요소를 들이지 않은 프로젝트에서는 탭도, 그 요소의 기본 Block(상태 효과 걸기·풀기·줄이기·검사·거르기, 빌려 쓰기)·행동 직전 이벤트('상태 효과 걸기')·이벤트('상태 효과 걸림'·'끝남')도 고르는 목록에 보이지 않는다(`[Element]`). 엔진 코드는 잠든 채 있다.
- DB는 열 때·바뀔 때 검증한다. 오류가 있으면 빌드가 막힌다(21장 7절).

## 5. 기본 Block(코드 없이 DB에서)

| 분류 | Block |
|---|---|
| 트리거 | 이벤트 받기(이벤트 형식 + 필드 필터) · 조건 충족 · 사용 · 행동 직전 이벤트 받기(형식) · AND/OR/NOT |
| 조건 | 필드 검사(경로에 `[키]`) · 플레이어 데이터 검사 · 배열·리스트 항목 검사 · 로그 비교 · 도전과제 달성(다른 도전과제를 달성했나) · 상태 효과 검사(사용자·이벤트의 개체, 중첩 비교 — 쿨다운, 또는 종류의 가짓수) · 확률(%) · 사용자 필드 검사(조각의 카운터 이상 N, 값 틀) · 자리 검사(맨 위 줄·맨 아래 줄·가장자리·안쪽·양옆이 빈 칸·같은 줄에 혼자·같은 열 맨 앞·같은 열에 혼자·맨 처음·맨 끝·n번째) |
| 결과 | 플레이어 데이터에 주기(숫자 증감·값·인스턴스·목록에 넣기·딕셔너리) · 로그에 기록 · 대상에게 효과 · 선택 요구 · 행동 직전 이벤트 고치기 · 이벤트 쏘기 · 비용 내기 · 가중치 표에서 주기 · 가중치 표에서 고르기(후보 수 더하기) · 빌려 쓰기(옆 조각의 패시브) · 다이얼로그 재생(다이얼로그 기능) |
| 대상 | 사용자 자신 · 플레이어 데이터 목록 · 이벤트의 개체 · 조건으로 거르기 · 상태 효과로 거르기 · 무작위 n개 · 정렬해서 n개 · 자리로 고르기(옆·같은 줄·주변 8칸·맨 처음·맞은편 같은 줄 맨 앞 …). 이어 붙이면 차례로. 주인공처럼 게임마다 다른 것은 `TargetSelector`로 |
| 효과 | 상태 효과 걸기(길이 바꾸기, 중첩 × 효과 배율, 건 개체 = 사용자) · 상태 효과 풀기(종류 전부 = 정화) · 상태 효과 줄이기 · 능력치 보정 · 능력치 보정 떼기 · 필드 바꾸기(더하기·곱하기·값으로). 피해·회복 같은 동사 효과는 프로젝트가 `Effect<T>`로 |

## 6. 정해진 숫자

| 무엇 | 값 |
|---|---|
| Undo 보관 수(행동 단위) | 새 DB 기본 200(Bootstrap, 0 = 제한 없음) |
| 상태 효과 주기가 밀렸을 때 한 번에 | 8번 |
| 사슬 한도(이벤트·트리거가 서로 부르는 깊이) | 16 |
| 가중치 표 안쪽 표 깊이 | 8 |
| 고정 틱 한 프레임 최대 | 8틱(배속이면 8 × 올림(배속), 최대 128틱 — `GameSession.TickLimit`, 남은 시간은 버림) |
| 플레이 중 DB 반영(밸런스 값·가중치 표) | 0.5초 |
| 화면 기준 크기 | 1920×1080 |
| 알림 | 2.5초, 최대 4줄 |
| 연출 `done` 기다림 · 코루틴 연출의 한 걸음 | 30초(실제 초, 게임이 멈춘 동안은 세지 않음 — 넘으면 경고 후 다음) |
| 정수로 맞추는 반올림 | 0에서 먼 쪽(2.5 → 3, −2.5 → −3 — `GameNumbers.Round`). `Get<int>`·정수 필드에 넣는 값·중첩·곡선·설정 단위·Steam INT 통계가 모두 같다 |
| 숫자 비교의 같음 | 차이 1e-9 이하(1000을 넘는 수는 그 크기의 1e-12배 — `FieldOps.Near`). 일치·이상·이하·초과·미만·비용이 모두 같다(0.1 × 10 = 1) |

## 7. 자주 틀리는 것

1. 행동 밖(화면·코루틴)에서 플레이어 데이터를 고쳤다 → Undo·저장·멀티가 어긋난다. 모든 변경은 행동.
2. 행동 밖에서 `Game.Random`·`Game.Roll` → 결과가 Undo·이어 하기와 어긋난다.
3. 판 데이터에 원본의 고정 값(이름·비용·그림)을 복사했다 → 원본 ID만 두고 원본에서 읽는다.
4. 플레이어에게 보일 실패를 `InvalidOperationException`으로 던졌다 → `throw Game.Fail(…)`.
5. 규칙 어셈블리에서 `MonoBehaviour`·`Time.deltaTime`·`UnityEngine.Random` → 세션 시계·`Game.Random`.
6. `Game.Ask` 다음 줄에 고른 뒤의 일을 썼다 → 그 줄은 고르기 전에 돈다. 이어서 할 행동에.
7. 같은 `Game.Roll(풀…, stream)` 이름을 두 곳에서 → 한쪽을 고치면 다른 쪽 결과가 바뀐다.
8. 풀(해금 목록 등)을 플레이어 데이터에 따로 만들어 저장 → 줄의 조건·목록 줄·뺄 목록으로(30장 5-10).
9. 값 칸을 화면 코드로 그렸다 → UXML `binding-path`.
10. 화면 버튼에서 `Game.Do`를 직접 불렀다 → `OnClick(…, () => 행동)`(Busy·실패 알림이 같이 온다).
11. 고르기 창·설정·확인 창을 새로 만들었다 → 기본 화면.
12. 텍스트를 코드·UXML에 글로 적었다 → 텍스트 키(`@hud.endTurn`, `Texts.Get`).
13. `GameIds` 상수를 쓰지 않고 ID를 글자로 적었다 → 오타가 컴파일에서 잡히지 않는다.
14. 생성 파일(`Assets/MomoiProject/Code/Rules/Generated/GameIds.cs`)을 고쳤다 → DB를 저장하면 덮인다.
15. 같은 ID에 `[BindTo]`를 둘 → 모두 돌고 순서가 정해지지 않는다(검증 경고).
16. 규칙에서 `DateTime.Now`·`Time.time` → `Game.Now`(현실)·`session.Clock`(게임 시간).
17. 실시간에서 맞는 효과를 `Present`로 → 차례로 돌고 그동안 `Busy`라 입력이 막힌다. 동시에·입력을 막지 않을 것은 `GameView.Play`.
18. 한 행동에서 같은 객체를 `Game.Find`로 여러 번 → 데이터 전체를 훑는다(행동 안이면 Sync도). 변수로 들고, 행동 필드는 객체로(실행 직전 한 번만 찾는다).
19. 개체 수백(투사체·보석·적 무리)을 플레이어 데이터에 → 화면 쪽 오브젝트 풀로 두고 규칙에는 수·결과만(판정 보고 행동).
20. 인스턴스(원본) 칸에 조건·트리거 Block을 두려 함 → 인스턴스에는 사용 기능(Usable)만. 발동이 필요하면 DB › 패시브·상태 효과·프로젝트 DB 종류(17장).
21. 확률을 `Game.Random(…)`으로 직접 굴렸다 → "확률 2배"·행운이 붙지 않는다. `Game.Chance`·조건 '확률'.
22. 조각(조커·참·아티팩트) 목록을 ID 글자 목록으로 → 같은 조각 둘·조각마다 쌓이는 값을 둘 수 없다. 객체 목록(고유 id + 원본 ID 필드) + 패시브 '일치 필드'.
23. 놓인 순서가 결과를 바꾸는 조각을 보통 패시브로 → 패시브 등록 순서로 발동한다. '놓인 것마다'를 켠다.
24. 가진 동안 +N을 얻을 때 보정 붙이기·잃을 때 떼기로 → 놓치면 남는다. 패시브 '가진 동안 능력치 보정'(읽을 때 계산).
25. 켜진 조각 목록(세피리아 계산 결과)을 계산할 때마다 새로 만들었다 → 조각에 쌓인 값이 사라진다. 원래 목록을 가진 목록으로 두고, 꺼짐은 레벨 필드 0.
26. 프로젝트 효과 Block이 수치를 그대로 썼다 → Lumin·Critical(효과 배율)이 붙지 않는다. 수치에 `Use.Context.Power`를 곱한다.
27. 틱 묶음 행동 안에서 `Game.Pay` → 모자라면 묶음 전체가 취소된다. "되면 낸다"는 `Game.TryPay`.
28. 같은 패시브의 다른 순간(쌓기·빼기·내기)을 패시브 둘이나 `[BindTo]`로 → 패시브 `rules`(발동 더) 한 패시브에.
29. 붙이는 순간 한 번 바꾸는 것(와일드프로스트 참·종)을 가진 동안 보정으로 → 붙인 순서·다시 계산이 원작과 달라진다. 능력(대상 = 덱 카드) + '필드 바꾸기'.
30. 턴 순서가 열 우선인 보드를 줄 우선으로 담았다 → '놓인 것마다' 차례가 원작과 다르다. 열 우선으로 담고 방향·자리는 '같은 열 …'.
31. DB 항목을 사람이 누르는 메뉴(`[MenuItem("…/DB 추가/…")]`)로 → 개발자 손이 한 번 더 간다. `[DbSeed("버전")]` 반영 스크립트(컴파일 뒤 저절로).
32. 반영 스크립트가 이미 있는 항목의 숫자를 다시 덮음 → 개발자가 밸런싱한 값이 사라진다. 없을 때만 만들고, 구조를 고칠 때만 버전을 올려 그 칸만 `Work`.
33. 개발자가 바꿀 숫자를 코드에·참조를 글자 ID 칸에 → 에디터에서 못 바꾼다. 밸런스 값·DB 칸, `[DataRef(typeof(…))]` 드롭다운.
34. 개발자가 배치할 레벨 데이터를 인스턴스로 → DB에서 추가·삭제가 잠겨 배치할 수 없다. `[Database(…, DeveloperAuthored = true)]` 종류나 씬.
35. 시스템층을 테스트 도구 없이 끝냄 → 개발자가 시험할 수 없다. 테스트 씬·기본 테스트 명령·첫 테스트 상황(관례 19절).
36. 결정 기록을 읽지 않고 질문 → 같은 것을 다시 묻는다. 세션마다 `Docs/Decisions.md`부터.
37. 그림·소리가 필요한 곳에 에셋 없이 화면만 → 개발자가 무엇을 넣을지 모른다. `AssetRequests.Sprite·Audio·Request`로 임시 자리와 넣을 것 목록.
38. 멀티 화면에서 `Do` 직후 결과를 읽음 → 클라이언트에서는 결과가 나중에 온다. 바뀐 곳 알림(`Data.Changed`·binding-path)이나 `SubscribeAfterAction`으로 그린다(시험 모드의 지연으로 확인).
39. 연출을 `Subscribe`로 받음 → 멀티 클라이언트에는 오지 않는다(규칙 쪽은 서버에서 돈다). 연출은 `SubscribeAfterAction`.
40. 숨길 정보(손패)를 모두가 보는 키나 이벤트에 → 상대에게 간다. 플레이어마다 키를 나누고 `SetVisibility(키, DataVisibility.Owner, 번호)`, 이벤트에는 넣지 않는다.
41. 서버에서 차례·주인을 검사하지 않음 → 클라이언트가 아무 행동이나 보낸다. `server.Validate`(내 차례·내 유닛), `actorId`는 서버가 보낸 사람 번호로 덮는다.
42. 주고받는 클래스에 `#if UNITY_EDITOR` 필드 → 에디터와 빌드의 필드 차례가 달라 읽기가 어긋난다. 이벤트는 `[GameEvent]` 클래스(구조체는 보낼 수 없다).
43. 데이터 클래스가 `List<T>`·`Dictionary`를 상속(`class Deck : List<Card>`) → 요소는 상속한 클래스의 안쪽 칸이라 비교 코드가 보지 못한다(예전 비교 방식·보낼 때 오류, 컴파일 경고). 상속 대신 `List<Card> cards` 필드.
44. 상태 효과·패시브·능력의 DB 종류를 GBible에서 찾음 → 없다. 선택 요소라 템플릿을 프로젝트에 들인다(4-1). 그 클래스에 칸이 모자라 'GBible을 고칠까요'라고 물음 → 프로젝트 클래스에 칸을 더한다(프로젝트 변경, 보고만).
