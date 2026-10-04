# GBible 참조 (AI용)

새 프로젝트에서 AI가 **무엇이 있고 무엇을 부르면 되는지** 찾는 문서다. 세 문서를 함께 읽는다.

| 문서 | 맡는 것 |
|---|---|
| `ProjectConventions.md`(프로젝트 관례) | **어떻게** 쓰나: 폴더·이름·규칙과 화면의 경계·가로채기 자리·테스트·보고 |
| `PlanningSpecGuide.md`(기획 명세서 읽는 법) | 기획 명세서의 칸 → 무엇을 만들지, 질문 규칙, 진행 순서 |
| 이 문서 | **무엇이** 있나: 하고 싶은 일 → 기능·API·DB 종류·Block, 매뉴얼 장 |

- 세션을 시작할 때 이 문서를 끝까지 읽는다. 기능을 처음 쓰는 순간에는 그 장(`Documentation~/Manual/NN-….md`)의 1절(무엇으로 만드나)·2절(동작 규칙)·6절(주의할 점)을 읽는다. 코드 예는 5절(상황별 예제).
- 여기 없는 것을 새로 만들기 전에 매뉴얼 `00-Index.md`의 "상황별로 찾기"를 한 번 더 본다. 그래도 없으면 관례 0절 순서(DB 조합 → 재사용 Block → `[BindTo]` → 중간층 확장)로 만든다.
- 이 문서의 이름(기능·칸·메뉴)은 DB와 화면에 보이는 그대로다. 사람에게 보고할 때도 이 이름을 쓴다.

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
선택 모듈(DB 사이드바의 '기능 활성화'): 다이얼로그 · 인벤토리 · 입력(키 바꾸기) · Steam · 확인용 패널·샘플
에디터: DB 창(MomoiProject › DB) · 모니터(플레이 중 기록·값·명령) · 시작 설정 · DB 추가 메뉴 · CSV
```

- **한 판의 흐름**: 게임이 켜지면 `GBibleRuntime.Start`가 DB를 읽어 세션을 만들고(Bootstrap 옵션 → `Configuring` → 세션 → DB의 정의 등록 → `Started`), 프로필을 불러온다. 입력은 행동(`Game.Do`), 행동이 데이터를 바꾸고 이벤트를 쏜다. 화면은 프레임 끝에 데이터를 읽어 그린다.
- **프로젝트 폴더**(관례 1절): `Assets/Code/Rules`(규칙, Unity API 없음) · `Assets/Code/Client`(화면·입력·연출, `Client/Screens`) · `Assets/Code/Editor`(DB 추가 메뉴) · `Assets/Code/Tests` · `Assets/UI`(UXML·USS·Panel Settings) · `Assets/MomoiProjectGenerated`(GameIds, 손대지 않음).

---

## 2. 하고 싶은 일 → 쓸 것

### 2-1. 데이터 · 행동 · Undo

| 하고 싶은 일 | 쓸 것 | 장 |
|---|---|---|
| 게임 중 바뀌는 값(판·프로필) | `[Serializable]` 클래스 + DB › 플레이어 데이터(키 `run` = 세션 구역, `profile` = 메타 구역). 시작 설정이 RunState·Profile을 만든다. 읽기 `Game.Data().Run`(생성 접근자)·`Game.Get<T>(키)` | 01 |
| 값을 바꾸기 | **행동 안에서** 필드를 그냥 고친다(Mirror·Sync가 기록). 행동 밖에서 바꾸지 않는다 | 01·02 |
| 플레이어 입력 하나 | `class X : GameAction { 필드 = 입력; protected override void Run() { … } }` → `Game.Do(new X { … })`. 하나 = Undo 한 단위 = 네트워크 한 묶음 | 02 |
| 되돌리기 | `session.Undo()`(Bootstrap Undo 방식: 행동 단위·스냅샷·없음, 보관 수). 이번 턴만이면 Bootstrap 'Undo 끊는 이벤트' | 02 |
| id로 객체 찾기 | `Game.Find<T>(id)`·`Game.All<T>()`. id가 있는 클래스(`public string id`). 새 id는 `Game.NewId("card")` | 03 |
| 행동 필드에 객체 넣기 | 넣어도 된다: 기록·전송에는 id만 남고 실행 직전에 다시 찾는다(Undo·불러오기 뒤에도 맞다) | 02 |
| 판을 이어 하기·저장 | Bootstrap '판 저장'(행동마다·끌 때·저장 안 함)·'판 끝 이벤트'. `GBibleRuntime.SaveRun`·`LoadRun`·`NewRun`·`HasSavedRun` | 06·10장(가이드) |
| 세이브 슬롯 1·2·3 | `GBibleRuntime.UseSlot(이름)`·`SlotInfoOf`·`DeleteSlot`·`CopySlot`·`SlotSummary`(한 줄 요약 함수), 기본 슬롯 화면 | 31·35 |
| 출시 뒤 저장 구조 변경 | DB › 개발 › 저장 변환(필드 옮김·키 이름 바꿈·키 삭제), 계산이 필요하면 `options.Migrations.Add` | 06 |
| 결정론 난수 | `Game.Random("흐름 이름").Range(최소, 최대 + 1)`·`NextDouble()`·`Chance(확률)`. 흐름은 쓰는 곳마다 이름. 행동 안에서만 | 07 |
| 게임 시간·일시정지·배속 | `GameTime.Pause(이유)`·`Resume`·`SetSpeed`, 세션 시계(`session.Clock`) | 07 |

### 2-2. 규칙 · 효과

| 하고 싶은 일 | 쓸 것 | 장 |
|---|---|---|
| 일이 일어났다고 알리기 | `[Serializable, GameEvent("이름")] class X { … }` → `Game.Events.Publish(new X { … })`. 받기 `Subscribe<X>`(규칙)·`SubscribeAfterAction<X>`(연출) | 10 |
| 핵심 동사(피해·회복·사용·이동 …) | 규칙 함수(`Rules.Damage`) 하나 + 의도(`[Serializable, Intent("피해")] class DamageIntent { sourceId, targetId, amount, cancelled }`)를 `Game.Intercept(의도)`로 통과 + 끝난 뒤 이벤트 | 28, 관례 4절 |
| "받는 피해 −5"·"막기"·"두 배"·"대신 맞기" | DB에서 트리거 '가로채기'(의도 형식) + 결과 '의도 고치기'(값으로 바꾸기·더하기·곱하기·막기) | 28 |
| 능력치(공격력·방어)와 보정 | 숫자 필드 = 능력치. `Game.Attribute(대상, "attack")`(보정 포함 값), `Game.AddModifier(대상, "attack", ModifierKind.Percent, 0.2, 출처)`·`RemoveModifiers(출처)`. 같은 칸은 더하고 칸끼리 곱(플랫 → 퍼센트 → 독립 곱) | 28 |
| 비용 내기 | `Game.Pay(키, "battle.energy", 2, what: "energy.name")`(모자라면 플레이어용 실패, 가로채기 '비용 지불'로 공짜·할인), `Game.CanPay`. DB 결과 '비용 내기' | 28 |
| 플레이어에게 보일 실패 | `throw Game.Fail("fail.frozen", "얼어 있어 움직일 수 없습니다", ("turns", 2))`. 행동 전체 취소, 메시지는 읽을 때 지금 언어 | 30 |
| 언제 · 누구에게 · 무엇을(효과 조립) | 트리거(이벤트 받기·조건 충족·사용·가로채기) → 결과(대상에게 효과(대상 Block → 효과 Block) · 플레이어 데이터에 주기 · 로그에 기록 · 이벤트 쏘기 · 선택 요구 …) | 11·12, 기획 명세서 읽는 법 7절 |
| 조건("보스를 잡았고 골드 100 이상") | 조건 Block(필드 검사·플레이어 데이터 검사·배열 검사·AND/OR/NOT·로그 비교), 코드 조건은 `SimpleCondition` | 11 |
| 그 항목만의 특별한 결과 | `[BindTo(GameIds.X.Y)] static void F(이벤트 e, TriggerContext c)` 또는 `Reward` 클래스 | 13 |
| 스킬·아이템 쓰기 | 사용 기능 필드(`[SerializeReference] Usable usable`) 또는 DB › 능력 → `Game.Use(id, 사용자, 대상…)`·`Game.CanUse(…, out 이유)`. 조건이 안 맞을 때 글은 '쓸 수 없을 때 글' | 16 |
| 독·화상·보호막·분노(중첩·지속) | DB › 상태 효과(중첩·다시 걸 때·지속(영구/이벤트 횟수/초)·능력치 보정·걸려 있는 동안 트리거·걸릴 때·끝날 때 결과), 효과 '상태 효과 걸기'·`Game.ApplyStatus`·`RemoveStatus`·`StatusStacks`·`HasStatus`·`Statuses` | 29 |
| "3턴 뒤 폭발"·"2초 뒤" | 상태 효과의 끝날 때 결과(DB) / `Game.ScheduleAfter(초, 행동)`·`ScheduleOn<이벤트>(횟수, 행동)`(코드)·`CancelSchedule(s)` | 29 |
| 플레이어가 고르기("1장 버리기") | `Game.Ask(안내, 후보, new 이어서할행동())` + 행동의 `[Chosen]` 필드 → 화면이 `Game.Answer`. DB 결과 '선택 요구'(판에 있는 것에서)·'가중치 표에서 고르기'(DB 원본에서) | 29·30 |
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
| 드롭·보상·조우(확률) | DB › 가중치 표 → `Game.Roll(id)`·`Roll(id, n, unique)`·결과 '가중치 표에서 주기'·'가중치 표에서 고르기'. 꽝 무게·안쪽 표·개수·조건 | 30 |
| 풀이 바뀜(해금·가진 것·판에서 생긴 것) | 줄의 조건(진행·도전과제) · 줄 종류 '플레이어 데이터 목록' · 뺄 목록 · 코드 풀 `Game.Roll(풀, n, unique, stream)`. 풀은 저장하지 않는다 | 30 5-10 |
| 확률 공개 | `session.Tables.Chances(id)` | 30 |
| 한 번 달성(도전과제) | DB › 도전과제(트리거 → 결과), Steam이면 같은 ID로 | 14·34 |
| 쌓는 숫자(처치 수·최대 콤보) | DB › 로그(이벤트 받기 → 기록 규칙: 더하기·최대·마지막), `session.Stats.Get(id)` | 15 |
| 필수 값·스프라이트 규격 | 필드 규칙(클래스 상세)·DB › 스프라이트 규격 | 27 |
| 수치 수십 개 한꺼번에 | DB 목록의 CSV(목록 칸 `entries[0].weight`) | 33 |
| AI가 DB 항목 만들기 | DB 추가 메뉴(`Assets/Code/Editor`, `EditorDatabase.Staging.Add(typeof(X), id)` → 적용 전 → 사람이 Ctrl+S) 또는 CSV | 관례 15절 |

### 2-4. 화면 · 연출 · 소리 · 글

| 하고 싶은 일 | 쓸 것 | 장 |
|---|---|---|
| HUD·상점·타이틀 같은 화면 | `Assets/UI/Screens/X.uxml` + DB › 화면(층·뒤로로 닫기·열 씬) + `[BindTo(GameIds.Screen.X)] class X : GameScreen`. 먼저 메뉴 '화면 준비' | 35 |
| 값을 보이기만 하는 칸 | UXML `binding-path="run:gold"`·`"run:gold@hud.gold"`(텍스트 틀)·`"@hud.endTurn"`(번역 글). 코드 없음 | 35 2-2 |
| 버튼 → 행동 | `OnClick("이름", () => new 행동())`(Busy면 무시, 실패는 알림)·`GameScreens.Do(…)` | 35 2-4 |
| 데이터 목록 → 칸 목록 | `List<T>("컨테이너", Item("칸 UXML"), () => 목록, (칸, 항목) => …)`(`ElementList`) | 35 2-6 |
| 고르기 창·확인·설정·슬롯·일시정지·알림 | 기본 화면: 고르기 창은 저절로, `GameScreens.Confirm`·`Open(GameScreens.Settings)`·`Open(GameScreens.Slots, new SlotsRequest{…})`·`Open(GameScreens.Pause)`·`Toast` | 35 2-5 |
| 화면 색·글꼴 | `Assets/UI/Theme.uss`의 `--momoi-…` 변수, DB › 화면 설정 › 글꼴(한글 TTF) | 35 2-7 |
| 씬에 놓인 오브젝트(캐릭터·카드) | `GameView.Redraw(그리기, 키…)`·`ViewList<항목, 컴포넌트>`(프리팹)·컴포넌트 '데이터 연결' | 32 |
| 피해 숫자·죽는 모습을 차례로 | `SubscribeAfterAction<이벤트>(e => GameView.Present(연출))`, 연출 시간은 `GameView.Wait`·`DeltaTime`, 입력 막기 `GameView.Busy` | 32 |
| 연출을 UI 칸 위에 | `GameView.FindElement(id)` + `GameScreens.Overlay` | 35 5-4 |
| 씬 바꾸기 | 판 데이터의 씬 필드를 행동이 바꾸고 Bootstrap '씬 흐름'이 연다(`GameScenes.Transition`으로 페이드) | 32 |
| 플레이어에게 보일 글·번역 | 텍스트 표(키 → 글), `Texts.Get("hud.energy", ("energy", n))`·`Texts.NameOf(id)`(`<id>.name`). 틀 문법 `{값}`·조사 `{monster:을}`·복수 `{count, plural, one {…} other {…}}` | 23 |
| 언어 바꾸기 | `GameLanguage.Set(code)`(설정 '언어'가 한다)·`Available` | 23·31 |
| 소리 | `GameAudio.PlayBgm`·`PlaySfx`·`PlayUi`·`PlayVoice`·`StopBgm`, 채널 음량은 설정 적용 대상 | 26 |
| 대사·컷신 | 선택 기능 다이얼로그: 그래프 창, `Dialogues.Play(id)`·`Signaled`·`ResumeSaved` | 24 |
| 칸 있는 가방 | 선택 기능 인벤토리: 필드 `Inventory { capacity }` → `Add`·`Remove`·`CountOf`·`Has` | 25 |

### 2-5. 기기 · 출시 · 개발 도구

| 하고 싶은 일 | 쓸 것 | 장 |
|---|---|---|
| 설정(음량·언어·화면·전투 속도) | DB › 설정(종류: 켜기/끄기·숫자·선택지·글, 적용 대상: 언어·음량·전체 화면·해상도·수직 동기화·최대 프레임) → `GameSettings.Number(id)`·`Toggle`·`Choice`·`Set`·`Changed`. 화면은 기본 설정 화면 | 31·35 |
| 키 바꾸기 | 선택 기능 입력: `GameInput.Use(actions)`·`Rebind`·`ResetBindings`·`BindingText` | 31 |
| Steam | 선택 기능 Steam + DB › Steam(App ID): 자동 초기화·도전과제·로그 → 통계·`GameSteam.SetPresence`·Cloud(Auto-Cloud 안내) | 34 |
| 플레이 중 값·기록 보기, 이벤트 쏴 보기, 버튼 하나로 시험 | 모니터(타임라인·데이터·행동·명령·이벤트 호출), DB › 개발 명령(결과 Block 조립 → 명령 탭 버튼) | 22 |
| 규칙 테스트(Unity 없이) | `new GameSession(new SessionOptions { Seed = 1 })` → `Data.Define` → `Game.Do` → 단언. 플레이어용 실패는 `Assert.Throws<GameFailure>` | 관례 12절 |

---

## 3. API 요약

### 3-1. `Game` (`GameApi`) — 행동·결과·규칙 함수 안에서

| 분류 | 멤버 |
|---|---|
| 행동 | `Do(행동)` · `Use(능력 id, 사용자, 대상…)` · `CanUse(…, out 이유)` · `Preview<T>(행동, g => 읽기)` · `CanDo(행동, out 이유)` · `IsPreviewing` |
| 데이터 | `Data()`(생성 접근자: `Game.Data().Run`) · `Get<T>(키)` · `Set(키, 값)` · `Find<T>(id)` · `Find(id)` · `All<T>()` · `All<T>(키)` · `NewId(접두)` · `static IdOf(객체)` · `static HasId(형식)` |
| 이벤트·난수 | `Events`(`Publish`·`Subscribe`·`SubscribeAfterAction`) · `Random(흐름)` |
| 능력치 | `Attribute(대상, 이름)` · `AddModifier(대상, 이름, 칸, 값, 출처, permanent)` · `RemoveModifiers(출처, 대상)` · `Modifiers(대상, 이름)` |
| 가로채기·비용·실패 | `Intercept<T>(의도)` · `Pay(키, 경로, 양, what)` · `CanPay(키, 경로, 양)` · `Fail(키, [기본 문장,] (이름, 값)…)` |
| 상태 효과 | `ApplyStatus(대상, id, 중첩 = 1, 출처)` · `RemoveStatus(대상, id, 중첩 = 0)` · `StatusStacks` · `HasStatus` · `Statuses(대상)` |
| 예약 | `ScheduleAfter(초, 행동, 출처, permanent)` · `ScheduleOn<이벤트>(횟수, 행동, 출처, permanent)` · `CancelSchedule(id)` · `CancelSchedules(출처)` |
| 선택 대기 | `Ask(안내, 후보(객체·id 글자), 이어서, min, max, source, player, label)` · `Ask(안내, ChoiceCandidate들, …)` · `Choice` · `Choices` · `IsWaiting` · `Answer(선택 id, 고른 것…)` · `CancelChoice(id)` |
| 밸런스·뽑기 | `Balance(id[, 단계])` · `Roll(표 id)` · `Roll(표 id, n, unique)` · `Roll(풀 (id, 무게)…, n, unique, stream)` · `Roll(id 목록, n, unique, stream)` |
| 월드 상태 | `World(id)` · `WorldFlag(id)` · `SetWorld(id, 값/참거짓, permanent)` · `ClearWorld(id)` |
| 세션 | `Session`(아래) |

### 3-2. `GameSession` · `SessionOptions`

- `Do(행동)` · `Undo()` · `UndoCount` · `Data` · `Events` · `Journal`(`Append(JournalKind, 시스템, 글)`) · `Stats`(`Get`·`Report`) · `Achievements`(`IsUnlocked`·`Unlock`) · `Triggers`(`Register(주인, 트리거, repeat, fire)`) · `Tables`·`Balance`·`Statuses`·`Schedules`·`Choices`·`World`·`Attributes` · `Debug`(`Register(분류, 이름, 할 일, 설명)` — 모니터 명령 탭) · `Instances`·`CopyInstance(id)` · `Ticked`(이벤트)·`TickAlpha` · `ExportRun()`·`ImportRun(json)`.
- `SessionOptions`: `Seed` · `History`(`Actions`·`Snapshots`·`None`) · `HistoryLimit` · `SnapshotInterval`·`SnapshotLimit` · `TickRate` · `SaveVersion` · `Migrations` · `DetectUntrackedChanges`. Unity에서는 DB › Bootstrap이 채우고, 코드가 바꿀 것만 `GBibleRuntime.Configuring += o => …`.

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
- `Describe()`(타임라인 글) · `Visibility`(`Public`·`Developer`). static 상태·Unity API를 쓰지 않는다(Rules 어셈블리).

### 3-4. 플레이어 데이터

- DB › 플레이어 데이터: 키 · 구역(`Session` = 판, `Meta` = 프로필) · 값 형식. 생성 상수 `GameIds.PlayerData.Run`(`PlayerDataKey<RunState>`, `.Id`), 접근자 `Game.Data().Run`.
- 코드로: `session.Data.Define("run", DataZone.Session, new RunState(), "판")` · `Get<T>(키)` · `Set(키, 값)` · `IsDefined(키)` · `Changed`(이벤트).
- 담을 수 있는 것: 숫자·글자·참거짓·enum·`[Serializable]` 클래스·List·배열·Dictionary(글자/숫자 키). 에셋 참조는 원본(DB)에 두고 판 데이터에는 ID.
- 라이브러리가 쓰는 키: `$statuses` · `$schedule` · `$choices` · `$world`(·`.meta`) · 로그 `stat:<ID>` · 도전과제 `achievement:<ID>`. 직접 고치지 않는다.

### 3-5. 이벤트 · 의도 · 컨텍스트

- 이벤트: `[Serializable, GameEvent("턴 끝")] public class TurnEnded { public int turn; public string playerId; }`. 필드 = 매개변수(받는 쪽·트리거 필터가 읽는다). 이름은 과거형(`EnemyKilled`). 쏘기 `Game.Events.Publish(…)`.
- 의도: `[Serializable, Intent("피해")] public class DamageIntent { public string sourceId, targetId; public int amount; public bool cancelled; }` → 규칙 함수가 `intent = Game.Intercept(intent); if (intent.cancelled) return;`.
- 컨텍스트(`TriggerContext`): `Session` · `Game` · `Owner`(그 결과를 가진 DB 항목 ID, 상태 효과면 상태 효과 ID) · `Event`(받은 이벤트·가로챈 의도) · `IsIntercept` · `IsUse` · `User`(사용자, 상태 효과면 가진 쪽) · `Picked`(고른 대상·고른 것).

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
| `[GameEvent("이름")]` · `[Intent("이름")]` | 이벤트 · 의도 클래스 | DB에 보이는 이름 |
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
| `GBibleRuntime` | `Session` · `Game` · `Start()` · 이벤트 `Configuring`·`Started`·`Stopped` · `Slot`·`UseSlot`·`SlotInfoOf`·`Slots()`·`DeleteSlot`·`CopySlot`·`SlotSummary`·`PlayTime` · `SaveProfile`·`SaveRun`·`LoadRun`·`NewRun`·`DeleteRun`·`HasSavedRun`·`AutoSaveRun` |
| `GameView` | `Redraw(그리기, [waitForPresentation,] 키…)` · `Present(코루틴 / done => … / 초)` · `IsPresenting` · `Busy` · `Wait(초)` · `DeltaTime` · `PresentationSpeed` · `SkipPresentation` · `ClearPresentation` · `Register`·`Find<T>(id)`(GameObject) · `RegisterElement`·`FindElement(id)`(UI 칸) |
| `ViewList<항목, 컴포넌트>` | `(프리팹, 부모, () => 목록, (뷰, 항목) => …, 키…)` · `OnCreate` · `OnRemove` · `ViewOf(id)` |
| `GameScreens` | `Open(id, 값)` · `Close` · `CloseAll` · `IsOpen` · `Get` · `All` · `Back()` · `Do(() => 행동)` · `Toast` · `Confirm` · `Overlay` · `QuitToTitle` · `Quit()` · `Opened`·`Closed` · ID `Choice`·`Confirmation`·`Settings`·`Slots`·`Pause` |
| `GameScreen` | `OnOpen`·`Draw`·`OnClose` · `Watch` · `WaitForPresentation` · `Q<T>(이름)` · `OnClick(이름, () => 행동 / 할 일)` · `List<T>(…)` · `Item(이름)` · `Own(…)` · `Close()` · `Root`·`Argument`·`Data` |
| `ElementList<항목>` | `(컨테이너, 칸 UXML, () => 목록, (칸, 항목) => …, 키…)` · `OnCreate` · `OnRemove` · `Find(id)` |
| `GameSettings` | `Toggle`·`Number`·`Choice`·`Text`·`Get(id)` · `Set(id, 값)` · `Reset`·`ResetAll(분류)` · `All` · `Label`·`ChoicesOf` · `Changed` · `Raw`·`SetRaw`(모듈용) |
| `GameScenes` | `Follow(키, 경로, 빈 값 씬)` · `Transition`(페이드 코루틴 감싸기) · `Loaded` · `Current` |
| `GameTime` | `Pause(이유)`·`Resume(이유)`·`ResumeAll`·`TogglePause` · `SetSpeed` · `StepFrame` · `IsPaused`·`Speed` |
| `GameAudio` | `PlayBgm(clip, volume, fade, loop)`·`StopBgm` · `PlaySfx`·`PlaySfxAt` · `PlayUi` · `PlayVoice`·`StopVoice` · `Stop(채널)`·`StopAll` · `Volume(채널)` |
| `Texts` · `GameLanguage` | `Texts.Get(키, (이름, 값)…)` · `NameOf(id)` · `Has(키)` · `Language` / `GameLanguage.Set(code)` · `Available` |

### 3-9. 선택 모듈

| 모듈(기능 활성화) | 멤버 |
|---|---|
| 다이얼로그 | 그래프 창(`MomoiProject → 다이얼로그 시스템 → 그래프`), DB › 스탠딩 프로필·장소, `Dialogues.Play(id, 시작 ID)` · `Advance` · `Choose(i)` · `Stop` · `IsPlaying` · `ResumeSaved()` · 이벤트 `Started`·`Ended`·`Signaled`(그래프의 신호 보내기), 결과 '다이얼로그 재생' |
| 인벤토리 | `Inventory { capacity, slots }` 필드 · `Add(id, 개수, 최대 겹침)` · `Remove` · `TryRemove` · `CountOf` · `Has` · `RoomFor` |
| 입력(키 바꾸기) | `GameInput.Use(InputActionAsset)` · `Find(이름)` · `Rebind(동작, i, done)` · `CancelRebind` · `ResetBindings` · `BindingText` (설정 파일에 저장) |
| Steam | DB › Steam · `GameSteam.IsReady` · `SetPresence(키, 값)` · `ClearPresence` · `StoreNow` (도전과제·통계는 저절로) |

---

## 4. DB 종류

| 사이드바 그룹 › 탭 | 형식 · ID 앞부분 | 항목 하나 | 상수 | 장 |
|---|---|---|---|---|
| 시작 파일 › Bootstrap | `GameDatabase` | 세션 옵션: Undo 방식·보관 수·Undo 끊는 이벤트·판 저장·판 끝 이벤트·시작할 때 이어 하기·초당 틱 수·씬 흐름·개발 시드 | — | 20 |
| 시작 파일 › RunState · Profile | (cs 파일) | 판·프로필 클래스 | — | 01 |
| 선언 › 키워드 · 클래스 · 이벤트 | (코드) | 읽기 전용 목록 | — | 05·10 |
| 선언 › 로그 | `StatData` · `stat` | 쌓는 숫자 | `GameIds.Stat` | 15 |
| 선언 › 플레이어 데이터 | `PlayerDataEntry` · `playerData` | 키·구역·형식 | `GameIds.PlayerData` | 01 |
| 선언 › 설정 | `SettingData` · `setting` | 설정 칸 하나 | `GameIds.Setting` | 31 |
| 선언 › 화면 설정 | `ScreenSettingsData` · `screens` | Panel Settings·테마·글꼴(하나) | — | 35 |
| 진행 › 도전과제 | `AchievementData` · `achievement` | 트리거 → 결과 | `GameIds.Achievement` | 14 |
| DB › 능력 | `AbilityData` · `ability` | 쓰는 것(조건·대상·효과·쓸 수 없을 때 글) | `GameIds.Ability` | 16 |
| DB › 상태 효과 | `StatusData` · `status` | 버프·디버프 | `GameIds.Status` | 29 |
| DB › 밸런스 값 | `BalanceData` · `balance` | 숫자 + 곡선 | `GameIds.Balance` | 30 |
| DB › 가중치 표 | `WeightTableData` · `table` | 풀 + 무게 | `GameIds.WeightTable` | 30 |
| DB › 화면 | `ScreenData` · `screen` | UI Toolkit 화면 | `GameIds.Screen` | 35 |
| DB › 인스턴스 | `InstanceData` · `instance` | 원본 하나(클래스의 값) | `GameIds.Instance` | 04 |
| DB › 프리팹 | `PrefabData` · `prefab` | 프리팹 연결 | `GameIds.Prefab` | 21 |
| 이미지 › 스프라이트 규격 | `SpritePresetData` | 그림 크기·피벗 규격 | — | 27 |
| 데이터 › (프로젝트 종류) | `GameData` 상속 | 유물·퀘스트 … | `GameIds.<이름>` | 17 |
| 개발 › 개발 명령 · 저장 변환 · Steam | `DebugCommandData` · `SaveMigrationData` · `SteamData` | 명령 탭 버튼 · 변환 한 단계 · Steam 설정 | — | 22·06·34 |
| 다이얼로그 시스템 › 스탠딩 프로필 · 장소 | (선택 기능) | 화자 그림 · 장소 | — | 24 |

- 저장(Ctrl+S)하면 `GameIds.cs`가 다시 만들어진다. 항목 이름(플레이어에게 보일 글)은 텍스트 `<ID>.name`.
- DB는 열 때·바뀔 때 검증한다. 오류가 있으면 빌드가 막힌다(21장 7절).

## 5. 기본 Block(코드 없이 DB에서)

| 분류 | Block |
|---|---|
| 트리거 | 이벤트 받기(이벤트 형식 + 필드 필터) · 조건 충족 · 사용 · 가로채기(의도 형식) · AND/OR/NOT |
| 조건 | 필드 검사 · 플레이어 데이터 검사 · 배열·리스트 항목 검사 · 로그 비교 · 도전과제 달성(다른 도전과제를 달성했나) |
| 결과 | 플레이어 데이터에 주기(숫자 증감·값·인스턴스·목록에 넣기·딕셔너리) · 로그에 기록 · 대상에게 효과 · 선택 요구 · 의도 고치기 · 이벤트 쏘기 · 비용 내기 · 가중치 표에서 주기 · 가중치 표에서 고르기 · 다이얼로그 재생(다이얼로그 기능) |
| 대상 | 사용자 자신(프로젝트가 고른 대상·전체 적·무작위 적·주인공 …을 `TargetSelector`로 더한다) |
| 효과 | 상태 효과 걸기 · 상태 효과 풀기 · 능력치 보정 · 능력치 보정 떼기(피해·회복 같은 동사 효과는 프로젝트가 `Effect<T>`로) |

## 6. 정해진 숫자

| 무엇 | 값 |
|---|---|
| Undo 보관 수(행동 단위) | 200(Bootstrap) |
| 사슬 한도(이벤트·트리거가 서로 부르는 깊이) | 16 |
| 가중치 표 안쪽 표 깊이 | 8 |
| 고정 틱 한 프레임 최대 | 8틱 |
| 플레이 중 DB 반영(밸런스 값·가중치 표) | 0.5초 |
| 화면 기준 크기 | 1920×1080 |
| 알림 | 2.5초, 최대 4줄 |
| 연출 `done` 기다림 | 30초(넘으면 경고 후 다음) |

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
14. 생성 파일(`Assets/MomoiProjectGenerated`)을 고쳤다 → DB를 저장하면 덮인다.
15. 같은 ID에 `[BindTo]`를 둘 → 모두 돌고 순서가 정해지지 않는다(검증 경고).
