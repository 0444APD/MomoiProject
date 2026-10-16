# 프로젝트 관례 (Momoi Project / GBible)

GBible로 만드는 모든 프로젝트에서 AI가 지키는 규칙이다(장르와 상관없이 같다). 게임마다 다른 것(규칙 함수의 동사·데이터 모양·켤 선택 기능 …)은 그 프로젝트의 **기획 명세서**, GBible에 무엇이 있나는 **참조**(`Reference.md`), 누가 무엇을 하나·진행 순서·질문은 **진행 매뉴얼**(`ProjectFlow.md`)이 정한다. 이 문서는 "어떻게 쓰나"만 정한다.

세 가지는 어떤 경우에도 지킨다(넘어야 하면 먼저 사람에게 말한다):
1. 틀리지 않고, 되돌아가지 않고, 예외가 생기지 않는다 — 틀에 맞지 않는 것은 예외 코드로 덧붙이지 않고 틀을 고친다(그래서 첫 단계에서 예외를 다 끌어낸다). 감싸는 형식·고유 문법은 피한다(어트리뷰트는 괜찮다).
2. 모든 게임 상태 변경은 행동(Action)으로 — 그래야 Undo·저장·리플레이·멀티플레이가 따라온다.
3. 실행 중 리플렉션 금지(게임이 켜질 때의 무거운 준비는 괜찮다).

---

## 0. 무엇을 쓸지 고르는 순서

- 만들기 전에 참조(`Reference.md` 2절)에서 같은 일을 하는 기능을 먼저 찾는다. 있으면 그것을 쓰고, 비슷한 것을 새로 만들지 않는다.
  - "가지고 있으면 발동"(유물·특성·패시브 스킬·장비 효과·조커·참)은 DB › 패시브 효과, "걸려 있는 동안·N턴 동안"은 상태 효과, 쿨다운은 상태 효과 + 조건 '상태 효과 검사'. 프로젝트 DB 종류(17장)는 이것들로 안 될 때만.
  - 놓인 순서대로 발동하는 조각(조커 칸·보드)은 패시브 효과 '놓인 것마다', 가진 동안 +N은 패시브 효과의 '가진 동안 스탯 보정'(트리거 없이도 된다, 조건부면 '보정 조건' `modifierWhen`). 한 조각이 순간마다 다른 결과(쌓기·빼기·내기)는 패시브 효과 하나의 '발동 더'(`rules`), N초마다는 '주기(초)'(`pulse`).
  - 효과 문장은 `GimmickCatalog.md`(기믹 목록)의 갈래로 쪼개고, 갈래에 적힌 부품으로 만든다.
- 없으면 이 순서로 만든다. 앞쪽일수록 개발자가 DB에서 읽고 값을 바꿀 수 있다.
  1. **DB Block 조합**(트리거·조건·결과·효과·대상 Block을 DB 항목에서 조립)
  2. **재사용 Block 하나 추가**(효과·대상·조건·결과 클래스. 다른 항목도 쓸 수 있게 일반적으로)
  3. **그 항목 전용 `[BindTo]` 코드**(한 항목만의 특수한 결과)
  4. **중간층 확장**(규칙 함수·행동 직전 이벤트 필드·새 행동 직전 이벤트 자리)
- **선택 기능(다이얼로그·인벤토리·입력·Steam)은 AI가 켠다** — 명세서 6절(부가 기능)에 켠 것이면 `ProjectSettings/MomoiProject/Features.json`의 `enabled`에 ID(`dialogue`·`inventory`·`input`·`steam`)를 더한다. Unity가 스크립트를 다시 컴파일하고 도메인을 다시 읽을 때 GBible이 스크립팅 심볼과 프로젝트 asmdef 참조를 맞춘다(`Features.json`만 고치면 일어나지 않는다 — 같은 박자에 다른 스크립트도 바꾼다). 그 기능의 형식을 쓰는 코드는 그 컴파일이 한 번 끝난 뒤에 쓴다(그 전에는 컴파일 검사가 그 어셈블리를 찾지 못한다 — 진행 매뉴얼 6절 박자 0). 입력·Steam은 외부 패키지(Input System·Steamworks.NET)가 있어야 켜진다 — 패키지를 들이는 것은 내려받기라 먼저 묻는다.
- GBible 자체에 없는 범용 기능이 필요하면(라이브러리를 고쳐야 하면) 고치지 않고 사람에게 알린다. 프로젝트 코드로 우회할 수 있으면 우회하고 보고한다. 화면 쪽 도구(UI·입력·카메라·이동)는 알리지 않고 프로젝트에 만든다(21절).
- **선택 요소**(상태 효과·패시브 효과·액티브 효과)는 DB 종류를 프로젝트가 가진다: 시스템 목록이 쓰기로 했으면 단계 1에서 템플릿(`Packages/com.momoi.gbible/Documentation~/AI/Templates/Elements/`)을 `Assets/MomoiProject/Code/Rules/Elements/`에 복사한다. 칸(속성·기능)이 더 필요하면 그 클래스에 더한다 — 프로젝트 변경이라 묻지 않고 보고만 한다. 엔진은 규격(`IStatusData`·`IPassiveData`·`IUsableData`)만 보므로 더한 칸은 프로젝트 규칙이 DB 항목에서 읽는다(Reference 4-1).

## 1. 폴더·어셈블리, 데이터와 코드

- `Assets/MomoiProject/Code/Rules`(어셈블리 `MomoiProject.Rules`): 게임 규칙 전부 — 데이터 클래스(RunState·Profile·원본 클래스), 행동, 규칙 함수(`Rules`), 이벤트·행동 직전 이벤트, Block, `[BindTo]` 함수. **씬·실행 API(MonoBehaviour·GameObject·씬·`Time`·`Input`·`Physics`·`UnityEngine.Random`)를 쓰지 않는다.** 데이터에 쓰는 Unity 형식은 된다 — DB 종류(`GameData` = ScriptableObject — 선택 요소 템플릿)·에셋 칸(`Sprite`)·어트리뷰트(`[SerializeField]`). 서버·테스트에서도 같은 코드가 돈다.
- `Assets/MomoiProject/Code/Client`: 화면·입력·연출·사운드. 규칙은 바꾸지 않고 행동만 보낸다.
- `Assets/MomoiProject/Code/Editor`: 에디터 도구만. `Assets/MomoiProject/Code/Tests`: 규칙 테스트(EditMode — 어셈블리 `MomoiProject.Tests`가 Rules·GBible.Core·GBible.Unity를 참조하고 Client는 참조하지 않는다. 에디터 전용이라 `#if UNITY_EDITOR` 멤버(`GameData.EditorAssignId`)도 쓴다).
- 생성 파일 `GameIds.cs`는 손대지 않는다(DB를 저장하면 다시 만들어진다). 자리는 플레이어 데이터 형식이 든 어셈블리 폴더의 `Generated/` — 시작 설정을 한 프로젝트는 `Assets/MomoiProject/Code/Rules/Generated/GameIds.cs`(형식이 Assembly-CSharp에 있으면 `Assets/MomoiProject/Generated/`).
- 파일 하나에 주제 하나: `Actions/BattleActions.cs`, `Rules/Rules.cs`, `Events/Events.cs`, `Events/Intents.cs`, `Cards/CardBlocks.cs` …
- **데이터는 DB, 동작은 코드.**
  - 같은 모양의 원본(카드·몬스터·아이템·스킬)은 코드에 클래스만 두고, 하나하나는 DB › 인스턴스(또는 액티브 효과·상태 효과 같은 전용 종류)에서 만든다.
  - 개발자가 바꿀 숫자는 DB › 밸런싱 값, 확률 표는 DB › 가중치 표, 플레이어에게 보일 글은 텍스트 표.
  - 판 데이터(RunState)에는 원본 ID와 **바뀌는 값**만 둔다(`DeckCard { id, card, upgraded }`). 원본은 게임 중에 고치지 않는다.
  - 코드가 필요 없는 설정(세션 옵션·판 저장·씬 흐름·고정 틱·Steam)은 DB에서 정한다(코드로 같은 값을 정하지 않는다).

## 2. 이름

- DB ID: lowerCamelCase, 점으로 묶을 수 있다(`fireball`, `startHp`, `boss.dragon`). 코드에서는 늘 `GameIds.종류.이름` 상수로 쓴다(글자로 적지 않는다). ID는 모든 DB 종류를 통틀어 하나다 — 같은 개념이 여러 DB 종류로 나뉘면(인스턴스와 그 패시브 효과 · 캐릭터와 스탠딩 프로필 · 다이얼로그) 명명 가이드라인 1-1대로 짓는다.
- 클래스·필드: C# 기본(클래스 PascalCase, 데이터 클래스는 public 필드 lowerCamelCase).
- 이벤트: 일어난 일을 과거형으로 — `TurnEnded`, `EnemyKilled`, `CardPlayed` + `[Serializable, GameEvent("표시 이름")]`.
- 행동 직전 이벤트: 곧 일어날 일 + `Intent` — `DamageIntent`, `HealIntent` + `[Serializable, Intent("표시 이름")]`.
- 행동: 동사구 — `PlayCard`, `EndTurn`, `StartRun` + `[Serializable, BlockInfo("표시 이름")]`.
- Block: 효과는 동사(`DealDamage`), 대상은 명사구(`AllEnemies`), 조건은 서술(`HasLowHp`) + `[BlockInfo("표시 이름", Category = …, Description = …)]`. Description은 꼭 적는다(개발자가 목록에서 읽는다).
- 텍스트 키: `fail.*`(행동 실패), `hint.*`(미리보기 글), `hud.*`, `popup.*`, `menu.*`, DB 항목 이름은 `<id>.name`.
- 난수 흐름 이름: 쓰임 하나에 하나(`"deck"`, `"loot"`, `"ai"`).
- 폴더·파일 이름(만드는 것 모두 — `Assets/MomoiProject/Code`·`Assets/MomoiProject/UI`·`Assets/Dev/Art` …)은 영어. 한글은 쓰지 않는다.
- `Assets/Dev`는 개발자 폴더다(안의 폴더 = DB 'Dev' 탭). 요청받을 때만 만들거나 바꾼다. 예외: 넣을 에셋 자리(`AssetRequests`)는 `Assets/Dev/Art`·`Dev/Audio`·`Dev/Fonts` … — 개발자가 진짜 파일을 넣는 곳이다.
- AI가 쓰는 것은 모두 `Assets/MomoiProject/` 아래: 코드 `Code/`(Rules·Client·Editor·Tests — 키워드 그룹은 `Rules/Keywords/`, 데이터 클래스와 같은 어셈블리여야 참조된다), 화면 `UI/`, 테스트 씬 `Scenes/Test.unity`. `Assets` 바로 아래에 새 폴더를 만들지 않는다.
- 게임 씬은 `Assets/Dev/Scenes`에 둔다(개발자가 레벨을 배치한다). AI가 게임 씬을 처음 만들 때만 여기에 만들고(반영 스크립트에서 `GameSceneSetup.Ensure("Assets/Dev/Scenes/<이름>.unity", firstInBuild: true)` — 없을 때만 빈 씬(카메라·빛)을 만들고 빌드 목록에 넣는다, 19절), 그 뒤 배치는 개발자 몫이다. Unity 기본 `Assets/Scenes`는 시작 설정·에디터를 열 때 여기로 옮겨진다.
- **개발자 화면 이름(한글)은 `NamingGuide.md`(명명 가이드라인)를 따른다** — 먼저 4절 '바로잡은 이름' 표를 본다(왼쪽 말은 쓰지 않는다). 개발자가 이름을 바로잡으면 프로젝트의 화면 글·DB 이름·문서를 모두 바꾸고, 결정 기록에 적고, 그 표에 한 줄 더한다(라이브러리 커밋 — 진행 매뉴얼 2절).
- 기획 명세서에 없는 **새 개념**(새 DB 종류·새 시스템)의 이름만 사람에게 확인한다. 이벤트·행동 직전 이벤트·행동·Block 같은 나머지는 이 규칙대로 짓고 보고만 한다.

## 3. 규칙과 화면의 경계

- 플레이어 데이터를 바꾸는 코드는 **행동의 `Run`과 그 안에서 부르는 규칙 함수·Block·`[BindTo]`에만** 있다. 화면 코드는 읽기만 하고, 바꿀 때는 `Game.Do(new 행동 { … })`.
- 규칙 함수는 `public static class Rules`의 static 함수다(첫 인자 `GameApi game`). 행동·Block·`[BindTo]`가 같은 함수를 불러서 계산이 한 곳에만 있다.
- 행동 필드에는 객체를 넣는다(`new PlayCard { card = deckCard, target = enemy }`). 기록·전송에는 id만 남고 실행 직전에 지금 객체로 다시 찾는다(보낸 사이 그 대상이 사라졌으면 플레이어용 실패 `momoi.fail.targetGone` "대상이 이미 없습니다." — 행동은 아무것도 바꾸지 않고 취소된다). 행동 안에서 새 개체를 만들면 id는 `Game.NewId("접두어")`(여럿이면 `Game.NewIds`). id는 플레이어 데이터 전체에서 하나다(형식이 달라도 겹치지 않게 — 접두어로).
- 한 행동 안에서 같은 객체를 `Game.Find`로 여러 번 찾지 않는다(찾을 때마다 데이터 전체를 훑는다). 한 번 찾아 변수로.
- 행동은 실패하면 바꾼 것이 모두 취소된다. 그러니 검사를 미리 다 하려고 애쓰지 말고, 안 되는 순간 던진다(플레이어용이면 `Game.Fail`, 5절). 행동 안에서 다른 행동을 불러도 된다(바깥 행동의 일부가 된다). 행동 안에서 쏜 이벤트를 받은 규칙·트리거 결과가 던져도 그 행동 전체가 취소된다.
- **메인 룰**(조각이 자리에 놓이고 정해진 순서로 발동하는 게임의 뼈대)은 기믹 목록 1절의 모양으로 만든다: 조각 = 판 데이터의 객체 목록(고유 `id` + 원본 ID 필드), 자리 = 목록 + 줄 길이(`Placement`), 순서 = 메인 룰 행동이 단계마다 이벤트를 쏘거나 자리 순서로 돌며 `Game.TryUse`. 메인 룰 코드는 단계·순서·공식(스탯 변환 같은 계산식)만 갖고, 조각 하나하나의 효과는 DB(패시브 효과·액티브 효과)에 둔다 — 조각은 공식이 읽는 값(비율 스탯·필드)에 보정을 더할 뿐이다. 반응을 발동이 끝난 뒤로 미루는 대기열도 메인 룰이다(기믹 목록 3절).
- 행동은 빨리 끝난다. 기다리지 않는다(애니메이션·플레이어 선택·시간 경과를 행동 안에서 기다리지 않는다 → 6·10·11절).
- 난수는 `Game.Random("흐름 이름")`만 쓴다. `UnityEngine.Random`·`System.Random`·`DateTime`·`Time`은 규칙에서 쓰지 않는다(재현·Undo·멀티가 깨진다). 현실 시각은 `Game.Now`(행동에 기록된다)·날짜 번호 `Game.Day(초기화 시각)`, 게임 시간은 `session.Clock`.
- **판정 보고 행동**: 물리·충돌·투사체·미니게임처럼 화면이 정하는 판정은 화면(Client)이 하고, 그 결과(누가 누구를 맞혔나·잡았나·성공)를 행동 하나로 보낸다(실시간이면 틱마다 묶어 `Strike { hits }`). 묶음 안의 대상은 객체가 아니라 **id 글자**로 두고, 행동 안에서 지금 없는 것은 건너뛴다 — 같은 틱에 먼저 쓰러진 하나 때문에 묶음 전체가 targetGone으로 실패하지 않게. 규칙은 결과로만 데이터를 바꾼다. 위치·속도는 화면 쪽이고, 한 번 정해지는 값(폭발 지점)은 행동 필드로 넘긴다.
- static 전역 상태를 규칙에 두지 않는다(세션마다 따로 돌아야 한다). 필요한 값은 플레이어 데이터에.
- **코드로 거는 규칙 등록**(행동 처리기 `session.Actions.Register` · 고유 기능 감시 `session.Triggers.Register` · 규칙이 이벤트를 받는 것 · 틱 규칙 `session.Ticked`)은 Rules의 등록 함수(`XxxRules.Register(GameSession session)`)에 두고, `Client/Bootstrap.cs`의 `RegisterRules`(`GBibleRuntime.SessionCreated` — 화면 세션·멀티 서버·시험 모드·매칭 방까지 모든 세션)에서 한 번 부른다. 규칙 테스트는 세션을 만든 뒤 같은 함수를 부른다. 화면 연결(연출 받기·화면 설정)만 `Bootstrap.Started`(화면 세션에만 온다)에. 기능마다 `[RuntimeInitializeOnLoadMethod]`를 따로 두지 않는다(매뉴얼 20장 4절·17장 2-6).
- 실행 중 리플렉션 금지: 데이터 클래스는 그 클래스·부모·안쪽 클래스·리스트 요소까지 모두 `[Serializable]`, 이벤트는 `[GameEvent]`, 행동 직전 이벤트는 `[Intent]`. 컬렉션은 `List<T>`·`T[]`·`Dictionary<K, V>`(키는 글자·정수·enum).
- 데이터 클래스의 `Dictionary` 필드에는 public이어도 `[UnityEngine.SerializeField]`를 붙인다(안 붙이면 Unity 6.6 직렬화 분석기 경고 UAC1015. GBible 저장·비교는 같다). Rules에서도 어트리뷰트는 써도 된다.
- 컬렉션 인스턴스는 한 곳에서만 가리킨다(두 곳이면 변경이 두 번 기록된다 — 나눠 줄 때는 `new List<T>(원본)`). 객체는 같은 플레이어 데이터 키 안에서는 두 곳에서 가리켜도 되지만(서로 가리켜도 된다), 다른 키의 객체는 id로 가리킨다(두 키가 같은 객체를 가리키면 Undo가 두 번 적용된다). **옮기기는 된다**: 한 행동 안에서 한 키의 목록에서 빼고 다른 키의 목록에 넣으면(판 ↔ 프로필 — 탈출해서 창고로, 창고에서 꺼내 판으로) 같은 객체 · 같은 id 그대로 옮겨진다 — Undo하면 제자리로, 옮긴 뒤 고친 값도 새 키에서 Undo되고, 저장에는 새 키 쪽에만 남는다(매뉴얼 01장). 금지는 두 키가 동시에 가리키는 것이다. 규칙이 Dictionary를 훑는 순서에 기대지 않는다(Undo·저장·멀티에서 순서가 달라질 수 있다 — 차례가 필요하면 List). 구조체에는 객체·컬렉션 필드를 넣지 않는다(클래스로). 리스트에 더하기·빼기·순서 바꾸기는 행동 안에서(행동 밖에서 하면 앞 행동의 Undo가 막힌다).

## 4. 행동 직전 이벤트 자리

- 장르의 **핵심 규칙 함수**(기획 명세서의 기본 동사: 피해·회복·죽음·사용·비용·뽑기·이동·획득 …)는 일을 하기 직전에 행동 직전 이벤트를 만들어 `Game.Intercept`에 통과시킨다. 막혔으면(`cancelled`) 아무것도 하지 않는다.
  ```csharp
  var hit = game.Intercept(new DamageIntent { sourceId = source?.id, targetId = target.id, amount = amount, kind = kind });
  if (hit.cancelled || hit.amount <= 0) return;
  ```
- 행동 직전 이벤트의 필드는 기믹이 고칠 만한 것 전부: 주체·대상 id, 양, 종류, 횟수, `cancelled`. 나중에 필드를 더해도 기존 행동 직전 이벤트는 그대로 돈다.
- 이벤트는 일이 끝난 뒤(행동 직전 이벤트 다음, 실제로 바뀐 값으로) 쏜다. 함수 하나에 행동 직전 이벤트 하나·이벤트 하나.
- 비용은 직접 빼지 않고 `Game.Pay`(비용 행동 직전 이벤트를 이미 거친다, 5절).
- 이렇게 해 두면 기획 단계에서 몰랐던 기믹(반사·면역·두 배·대신 맞기)이 이 자리에 붙는다.
- GBible이 이미 행동 직전 이벤트 자리를 둔 동작: **사용**(`Game.Use`·`TryUse` — 횟수·막기), **효과 배율**(액티브 효과를 쓸 때·패시브 효과가 발동할 때 — 기본 효과 Block이 곱하고, 프로젝트 효과 Block은 수치에 `Use.Context.Power`를 곱한다), **비용 지불**(`Game.Pay`·`TryPay` — 양·공짜·하한(빚)·낼 곳, `forId`·`userId`로 거름), **상태 효과 걸기**(`Game.ApplyStatus`·효과 '상태 효과 걸기' — 바꾸기·중첩·길이·최대 중첩·주기·막기), **확률**(`Game.Chance`·조건 '확률' — 확률 배율). 이 동작은 이 함수로만 한다: 확률을 `Game.Random(…).Chance`로 직접 굴리거나 상태 효과 목록을 직접 고치면 행동 직전 이벤트가 붙지 않는다.

## 5. 숫자·비용·실패

- 개발자가 바꿀 숫자(시작 값·보상·확률·성장)는 코드에 적지 않고 DB › 밸런싱 값에서 읽는다: `Game.Balance(GameIds.Balance.X)`, 층·레벨마다 변하면 곡선 + `Game.Balance(id, 단계)`. 규칙 자체의 상수(덱 섞기 알고리즘 같은 것)는 코드에 둔다.
- 무게 있는 뽑기(드롭·보상·조우)는 DB › 가중치 표 + `Game.Roll`.
- 스탯(공격력 등)은 숫자 필드 + Modifier(읽기 `Game.Attribute`). 장비·버프의 보정을 필드에 직접 더하지 않는다(벗을 때 되돌릴 수 없다): 장비 · 유물처럼 가진 동안 붙는 것은 DB › 패시브 효과 '가진 동안 스탯 보정'(읽을 때 계산 — 벗으면 저절로 빠진다, 0절), 버프 · 디버프는 상태 효과의 스탯 보정. `Game.AddModifier`·`RemoveModifiers`는 그 밖에 코드가 잠깐 거는 보정(매뉴얼 28장)에만 쓰고 장비에 쓰지 않는다.
- 비용: `Game.Pay(키, 경로, 양, what: "energy.name", forId: 무엇을 위한, user: 내는 쪽)`. 모자라면 플레이어용 실패가 난다. 틱 묶음 행동 안에서 "되면 낸다"(연계 MP·카드마다 비용)는 `Game.TryPay`(모자라면 바꾸지 않고 거짓). 화면 미리 확인은 `Game.CanPay`·`Game.CanDo`(`CanPay`는 행동 직전 이벤트 전 값이라 규칙 판정에 쓰지 않는다).
- 실패:
  - 플레이어에게 보일 것(모자람·조건 안 맞음·지금 못 함) → `throw Game.Fail("fail.키", "기본 문장 {값}", ("값", x))`. 텍스트 표에 키를 넣으면 번역된다.
  - 프로그래머 실수(없는 ID·잘못된 상태) → `throw new InvalidOperationException("…")`. 플레이어에게 보이지 않는다.
  - 액티브 효과·아이템의 사용 조건이 안 맞을 때 플레이어에게 보일 글은 그 항목의 **쓸 수 없을 때 글**(DB 칸 `failText`, 텍스트 키)에 둔다. 비우면 라이브러리 기본 글({condition}이 DB 조건 문장 그대로)이 보인다.

## 6. 화면·연출

- **화면의 문법·레이아웃·디자인은 `UiGuide.md`(UI 가이드)가 정한다**(모든 프로젝트 공통 — 화면을 만들거나 고칠 때 끝까지 읽는다). 화면은 UI 키트(`Templates/UI/` — 키트 USS · 코드 KitPanel 등 · 견본 화면, 프리팹이 아님)를 복사해 짠다. 디자인 요점: 차가운 검정 + 종이 흰색 · 매트·플랫(그림자·빛남 없음) · 패널 면은 단색 + 아주 옅은 조각 · 패널 모양 4가지(직각·약간 둥근·모서리 자름·알약)·2중첩까지 · 기울인 패널·물건 소품 없음 · 뒷배경 3가지(DB › 화면 '뒷배경' 칸, 블러는 알림만) · 장면 위 UI(HUD·상태)는 묶는 패널 없이 여백으로(스프라이트를 품은 요소만 패널) · 게이지는 KitGauge(둥근 모양 없음) · 홀로 놓이는 글은 강조 숫자(글꼴 외곽선·그림자) · PC 크기(모바일처럼 크지 않게) — 자세히는 UiGuide 3절. 개발자는 화면마다 전체 레이아웃 참고 그림만 주고, 세부 배치는 AI가 정한다.
- **UI(HUD·메뉴·팝업)는 UI Toolkit 화면**(매뉴얼 35장): 화면 하나 = `Assets/MomoiProject/UI/Screens/<이름>.uxml` + DB › 화면 항목(UXML·층·뒷배경·뒤로(Esc)로 닫기·열 씬) + 필요하면 `Assets/MomoiProject/Code/Client/Screens/<이름>.cs`(`[BindTo(GameIds.Screen.X)] : GameScreen` — 상수가 생긴 뒤에, 15절). USS는 UiGuide 1-2(프로젝트 USS 한 파일). UXML·USS·화면 코드는 AI가 쓰고, 프리팹·인스펙터 연결은 쓰지 않는다.
  - 값을 보이기만 하는 칸은 UXML의 `binding-path`(`run:gold`, `run:gold@hud.gold`, `@hud.endTurn`). 화면 코드에 같은 일을 쓰지 않는다.
  - 버튼 → 행동은 `OnClick("이름", () => new 행동())`(Busy면 무시, 플레이어용 실패는 알림). 화면을 여는 버튼은 `OnClick("이름", () => GameScreens.Open(…))`.
  - 데이터 목록은 `List<T>("컨테이너", Item("칸 UXML 파일 이름"), …)`(목록 칸 UXML은 `Assets/MomoiProject/UI/Items/` — 화면 항목의 '목록 칸 UXML' 칸이나 화면 UXML의 `<ui:Template>`에 있어야 `Item`이 찾는다).
  - 고르기 창·확인·설정·슬롯·일시정지·알림은 **기본 화면**을 쓰고 새로 만들지 않는다. 기본 화면의 모양은 `Assets/MomoiProject/UI/Theme.uss`의 `--momoi-…` 변수로 맞춘다(프로젝트 화면은 키트 변수 `--kit-…` — UiGuide 3-4).
  - 1920×1080 기준 px. 그림(스프라이트·아이콘)·글꼴은 사람이 넣는다: 그 자리는 색 상자·임시 그림으로 두고(UiGuide 1-9) 보고에 "넣을 에셋(경로·크기·쓰는 곳)"을 적는다.
- 씬에 놓이는 것(캐릭터·카드 오브젝트)은 `GameView.Redraw(그리기, 키…)`·`ViewList`·컴포넌트 '데이터 연결'. 값이 바뀔 때마다 그리지 말고 이 도구가 프레임 끝에 한 번 그리게 한다.
- **프리팹은 AI가 만든다**(개발자 2026-10-16). 개발자가 말로 준 양식(무엇이 붙나 — 스프라이트 · 애니메이션 · 충돌체 · 크기)대로 반영 스크립트에서 에디터 코드(`PrefabUtility.SaveAsPrefabAsset` · 애니메이션은 `AnimatorController`에 개발자가 둔 클립을 연결)로 `Assets/MomoiProject/Prefabs/`에 만들고 DB › 프리팹(`PrefabData`) 항목에 등록한다 — 화면 코드는 `Game.Entry<PrefabData>(GameIds.Prefab.X).prefab`로 읽는다(인스펙터 연결 없음, 매뉴얼 39장 3절). **개발자는 프리팹을 만지지 않는다** — 리소스(스프라이트 시트 · 애니메이션 클립)만 `Assets/Dev/Art` · `Assets/Dev/Animations`에 둔다. 양식이 바뀌거나 피드백이 오면 AI가 반영 스크립트 버전을 올려 같은 경로에 다시 만든다(GUID 그대로 — 씬에 놓인 것도 따라온다). 리소스가 아직 없으면 임시 에셋(18절)으로 자리를 만든다. 씬에 무엇을 어디 놓나(레벨)는 개발자 몫이다.
- 연출(피해 숫자·카드 이동·쓰러짐)은 규칙이 끝난 뒤의 일이다: `session.Events.SubscribeAfterAction<이벤트>(e => GameView.Present(연출(e)))`(연결은 `Bootstrap.Started`나 화면 코드 — 3절의 규칙 등록과 나눈다). 실패한 행동의 이벤트는 오지 않는다. 연출 코드는 데이터를 바꾸지 않는다.
- 연출 안의 시간은 `GameView.Wait`·`GameView.DeltaTime`(속도·건너뛰기를 따른다). `done()` 콜백 연출은 반드시 `done`을 부른다.
- 입력은 `GameView.Busy`(연출 중 · 선택을 기다림 · 대사가 열려 있음(다이얼로그 `Dialogues.BlocksInput`) · 프로젝트가 `GameView.AddBusy`로 더한 때)일 때 막는다.
- 예상 값(예상 피해·"낼 수 없음" 이유)은 화면에서 따로 계산하지 않고 `Game.Preview(행동, g => 읽기)`·`Game.CanDo(행동, out 이유)`.
- 화면 오브젝트를 데이터 id로 찾을 때는 `GameView.Find<T>(id)`(ViewList가 등록한다), UI Toolkit 칸은 `GameView.FindElement(id)`(ElementList가 등록한다). 화면 위에 잠깐 띄우는 요소(피해 숫자)는 `GameScreens.Overlay`에(그 클래스는 테마 USS에 — 화면 USS가 닿지 않는다, UiGuide 1-2).
- 씬 전환은 화면이 직접 하지 않는다: 판 데이터의 씬 필드를 행동이 바꾸고 씬 흐름(DB › Bootstrap)이 연다.

## 7. 글·다국어

- 플레이어에게 보일 글은 코드·UXML에 적지 않고 텍스트 키로: `Texts.Get("hud.energy", ("energy", n))`. DB 항목 이름은 `<id>.name`(값에 ID를 넣으면 그 이름으로 바뀐다).
- 원문 언어(기본 한국어) 표에 키를 채우고, 다른 언어는 시트로 번역한다.
- 실패 글은 5절의 `Game.Fail`.

## 8. 데이터 구역·저장

- 판(한 번의 플레이)에만 있는 것은 **Session** 구역(기본 `run` = RunState), 판이 끝나도 남는 것은 **Meta** 구역(기본 `profile` = Profile). 새 플레이어 데이터는 DB › 플레이어 데이터에 반영 스크립트로 만들고(15절) 구역을 고른다.
- 저장은 DB › Bootstrap의 판 저장 설정대로 저절로 된다. 저장 코드를 따로 쓰지 않는다. 세이브 슬롯이 여럿이면 `GBibleRuntime.UseSlot` — 슬롯 하나 = 프로필 + 판 묶음이라 칸마다 프로필이 따로다. 세이브 칸이 여럿이고 판이 끝나도 남는 것(앨범 · 해금 · 본 엔딩)이 칸을 넘어 함께여야 하면 만들기 전에 질문으로 올린다(진행 매뉴얼 5-3).
- 플레이어 데이터에 에셋(스프라이트·프리팹·ScriptableObject) 참조를 넣지 않고 DB ID를 넣는다. DB 항목 ID가 든 글자 필드에는 `[DataRef(typeof(종류))]`를 붙인다(ID를 바꾸면 따라간다).
- 출시 뒤 저장 구조를 바꾸면 DB › 개발 › 저장 변환에 줄을 더한다(필드 이름을 바꾸기 전에 확인).
- 기기마다 하나인 것(음량·언어·화면·키)은 DB › 설정(`GameSettings`). 판 규칙에 영향을 주는 값은 설정에 두지 않는다.

## 9. 이벤트·트리거로 잇기

- 다른 기능이 반응할 만한 일(처치·획득·턴 끝·판 끝)은 이벤트로 쏜다. 반응하는 쪽(도전과제·로그·유물·상태 효과)은 이벤트를 받는 트리거로 만든다(DB에서 조립 우선). 쏘는 쪽이 받는 쪽을 직접 부르지 않는다.
- 이벤트 필드에는 받는 쪽이 거를 수 있게 주체·대상 id와 양을 넣는다(가진 쪽 필드·이벤트 조건이 이것을 본다).
- 결과가 다시 이벤트(행동 직전 이벤트)를 쏘아 끝없이 도는 고리를 만들지 않는다(16단계에서 끊기고 경고 "… 사슬이 16단계를 넘어 끊었습니다"가 남는다).

## 10. 시간·턴

- 턴제: 턴은 행동(`EndTurn`)과 이벤트(`TurnEnded`)로 흐른다. "N턴 동안/N턴 뒤"는 상태 효과(이벤트 횟수 지속·끝날 때 결과)로, 코드에서 나중에 할 일은 `Game.ScheduleOn<이벤트>(횟수, 행동)`.
- 실시간: DB › Bootstrap의 고정 틱을 켠다. 시간 지속은 상태 효과(게임 시간, 주기(초)로 N초마다 결과), 가진 동안 N초마다는 패시브 효과 '주기(초)'(멈추기·앞당기기·리셋이 있는 카운터는 조각 필드 + 틱 행동), 나중 일은 `Game.ScheduleAfter(초, 행동)`. 틱마다의 가벼운 규칙은 `session.Ticked`에서 `Game.Do`로. 이동·물리는 화면 쪽 자유 영역이고, 규칙에 필요한 결과(맞음·주움)만 행동으로 보낸다(3절 판정 보고 행동).
  - 실시간 예산(매뉴얼 33장): 판 저장은 '행동마다' 대신 'N초마다', 연출은 `GameView.Play`(대기열 밖, 입력을 막지 않음), 개체 수백(투사체·보석)은 화면 쪽 오브젝트 풀로 두고 규칙에는 수와 결과만. 단 상태 효과(화상·얼림)나 DB 기믹이 걸리는 적·투사체는 판 데이터의 가벼운 목록(id·원본·hp·필요한 칸 — 위치는 화면 쪽)에 둔다(상태 효과는 판 데이터 객체에만 걸린다 — 구현 가이드 07 6절 서바이버).
- 부가 기능(현실 시간·게임 안 달력·칸 단위 맵·큰 수·되감기·시드 …)은 명세서 6절에 표시된 것만 붙인다(참조 문서 2-6). 현실 시각은 규칙에서 `Game.Now`·`Game.Day`만 쓴다.
- 조각의 자리(한 줄·격자·보드)는 `Placement`(목록 + 줄 길이). 목록 순서 = 발동 순서이므로, 순서가 열 우선인 보드(와일드프로스트)는 목록을 열 우선으로 담는다. 칸 단위로 움직이는 맵은 `Grid`.
- 보드·줄의 조각 자리는 '칸', 아이템 컨테이너의 한 칸은 '슬롯'(아이템 슬롯), 세이브는 '세이브 슬롯'이라 부른다(명명 가이드라인 5-7).
- `Update`에서 플레이어 데이터를 바꾸지 않는다.

## 11. 플레이어 선택

- 행동 도중 플레이어가 골라야 하면(카드 1장 버리기·보상 3개 중 1개) 기다리지 않고 `Game.Ask(안내, 후보, new 이어서할행동())`. 고른 뒤 할 일은 모두 이어서 할 행동(`[Chosen]` 필드)에 쓴다. 안내 글은 텍스트 키(7절 — 기본 고르기 창이 그때 언어로 번역한다), 후보마다 다른 설명(레벨별 설명)은 `new ChoiceCandidate(id, 이름, detail)`(글 또는 텍스트 키, 비면 `<id>.desc`). DB에서는 결과 '선택 요구'(판에 있는 것에서)·'가중치 표에서 고르기'(DB 원본에서).
- 뽑기의 후보(풀)는 저장하지 않고 뽑을 때 만든다: 진행·해금은 가중치 표 줄의 조건, 판에서 만들어진 것은 목록 줄, 가진 것은 뺄 목록, 규칙이 계산한 후보는 `Game.Roll(풀, …, stream: "쓰는 곳 이름")`.
- 화면은 `Game.Choice`를 보여 주고 `Game.Answer`로 답한다. 기다리는 동안 입력은 `GameView.Busy`로 막힌다.
- 다시 뽑기는 행동 하나: 비용·남은 횟수를 내고, 표로 만든 선택이면 `Game.RerollChoice(선택 id)`, 코드 풀로 만든 선택이면 `Game.CancelChoice(선택 id)` 뒤 새 후보로 다시 `Game.Ask`. 기본 고르기 창의 '다시 뽑기' 버튼은 `GameScreens.Reroll`(`Bootstrap.Started`에서)이 행동을 돌려주는 모든 선택에 보인다. 후보 수를 늘리는 기믹은 '가중치 표에서 고르기'의 '후보 수에 더할 숫자'.

## 12. 테스트·검증·보고

- 규칙(행동·규칙 함수·Block)을 만들거나 고치면 `Tests`에 테스트를 하나 이상 둔다. 세션을 손으로 만든다: 플레이어 데이터 `Define`, 밸런싱 값 `session.Balance.Define`, 상태 효과 `session.Statuses.Define(StatusDefinition)` · 패시브 효과 `session.Passives.Define(PassiveDefinition)` · 가중치 표 `session.Tables.Define(WeightTableDefinition)`(DB 항목 객체를 만들었으면 `ToDefinition()`) · 액티브 효과 `session.Abilities.Define(항목.ToUsable())`, 원본 `session.DefineInstance(id, 값)`(`Game.InstancesOf<T>()`도 이것을 훑고, 사용 기능(`Usable` 필드)이 붙은 원본은 `Game.Use(인스턴스 ID, …)`로 쓸 수 있게 함께 등록된다), 규칙이 `Game.Entry`로 읽는 DB 항목은 `ScriptableObject.CreateInstance<T>()` + `EditorAssignId(id)`로 만들어 `session.Entries`·`EntriesOfType`에 넣는다(매뉴얼 17장 4-3). 시간이 흐르는 규칙(예약·상태 효과 주기·`Ticked`)은 `session.Advance(초)`로 흘린다(`EndFrame` 한 번은 8틱에서 잘린다).
- 테스트는 게임처럼 행동을 실행하고 값을 본다. 플레이어용 실패는 `Assert.Throws<GameFailure>`(NUnit은 정확한 형식만 잡는다). Undo가 맞는지(`session.Undo()` 뒤 값)도 본다.
- 코드를 고친 뒤에는 컴파일이 되는지 확인하고, Unity가 열려 있으면 콘솔 오류·DB 검증("검증 통과")을 본다. AI는 Unity 창을 볼 수 없으므로 **파일로 읽는다**: DB 상태 `Logs/MomoiProject/DbStatus.txt`(검증 오류·경고 · '적용 전' 변경 — 컴파일 뒤·DB가 바뀔 때 GBible이 다시 쓴다), Unity 로그 `Logs/Editor.log`(Unity 6.6은 프로젝트 안 — 컴파일 경고·`[MomoiProject]` 알림·DB 반영 스크립트 결과). 개발자에게 "경고가 뭐냐"고 묻기 전에 이 둘을 본다.
- 테스트도 AI가 돌린다: Unity가 열려 있으면 `Logs/MomoiProject/RunTests.request`를 만들고 `Logs/MomoiProject/TestResults.txt`(통과·실패, 실패 메시지·호출 위치)를 읽는다(매뉴얼 38장 2-4 — Unity가 먼저 에셋을 새로 읽어 방금 쓴 코드를 컴파일한 뒤 돌린다). Unity를 연 채로 배치 모드(`-batchmode`)는 쓰지 않는다. 요청 파일이 남아 있으면 Unity가 아직 돌리지 못한 것이다(플레이 중이거나 창이 오래 멈춤) — 개발자에게 Test Runner를 누르라고 하기 전에 이것으로 한다.
- 화면도 AI가 플레이해서 본다: `Logs/MomoiProject/Play.request`(열기·누르기·테스트 명령·찍기·`record` 단계)를 만들고 `Logs/MomoiProject/PlayResults.txt`와 찍은 그림(`Captures/*.png`)을 읽는다(매뉴얼 38장 2-5). 화면을 만들거나 고치면 둘러보기가 오류 없이 끝나고 그림이 UI 가이드대로일 때까지 고친 뒤 넘긴다. 요청 파일이 오래 남으면 에디터가 모달 창에서 멈춘 것이다 — 창 내용을 읽어 알리고, 단추는 대신 고르지 않는다(고른다면 아무것도 바꾸지 않는 '취소'만 — 고른 것을 보고한다).
- 응답을 끝낼 때 컴파일 검사 훅(`.claude/hooks/compile-check.ps1`, 매뉴얼 20장 11절)이 돈다. 오류가 나오면 고친 뒤 끝낸다. 세 번 막힌 뒤에도 남은 오류는 보고에 적는다.
- 사람에게 보고하는 양식은 진행 매뉴얼 13절. 기획 명세서와 달라진 점도 '알릴 것'에 넣는다.
- 기획 명세서와 코드가 다르면 코드를 맞추거나, 명세서가 틀렸으면 사람에게 묻는다(몰래 바꾸지 않는다).

## 13. 문법 밖 기믹

- 효과는 "언제(즉시·발동·지연·지속) + 누구에게(대상 고르기) + 무엇을(데이터 바꾸기·만들기/없애기·행동 직전 이벤트·선택 요구·비용) + 안전장치(연쇄 제한)"로 쪼갠다. 쪼개지면 0절 순서로 만든다.
- 문법 안으로 보는 것: 화면이 판정하고 결과를 행동으로 보내는 것(물리 충돌·투사체 반사·벽에 부딪힘·미니게임 — 3절 판정 보고 행동), 같은 세션 안의 되감기(`session.UndoTo(지점)`), 현실 시각(`Game.Now`), 큰 수(`BigNumber`).
- 쪼개지지 않는 것(저장을 넘어 과거로 되감기, 되감은 뒤 난수를 다시 굴리기, 규칙이 화면의 결과 없이 물리 결과를 알아야 하는 것 …)은 만들기 전에 표시하고 묻는다: 무엇이 문법 밖인지, 어떻게 만들 수 있는지(화면 쪽·별도 시스템·근사), 각각의 비용.
- 기획 명세서의 "문법에 안 맞는 기믹" 칸에 적힌 것은 그 답을 따른다.

## 14. 하지 않는 것

- GBible 라이브러리(`Packages/com.momoi.gbible`) 코드를 고치지 않는다.
- 생성 파일 `GameIds.cs`(1절)를 고치지 않는다.
- DB 에셋 파일(.asset)을 텍스트로 직접 쓰지 않는다(15절의 방법으로 만든다).
- 행동 밖에서 플레이어 데이터를 바꾸지 않는다(개발용 명령도 DB › 개발 명령이나 행동으로).
- 화면 코드에 규칙 계산을 복사하지 않는다(미리보기를 쓴다).

## 15. DB 항목 만들기 — DB 반영 스크립트

AI는 Unity 창을 누를 수 없다. DB 항목은 **DB 반영 스크립트**로 만든다: `Assets/MomoiProject/Code/Editor`의 static 함수(매개변수 없음)에 `[DbSeed("버전")]`을 붙이면 컴파일이 끝나고 에디터가 한가해질 때 GBible이 저절로 한 번 돌린다(버전이 바뀐 것만 다시 — 기록 `ProjectSettings/MomoiProject/DbSeeds.json`).

- **새 항목(`Staging.Add`)은 저절로 적용된다**(무엇이 들어왔는지는 콘솔·DB 창 알림). **이미 있는 항목의 고치기(`Work`)·지우기(`Remove`)·ID 바꾸기(`Rename`)만 '적용 전'으로 남고**, 개발자가 읽어 보고 Ctrl+S로 적용한다 — 개발자가 만지던 항목을 말없이 바꾸지 않게. '적용 전'이 남은 버전은 적용했을 때 "돌았다"로 기록된다(버리면 다음 컴파일 뒤 다시 돈다). 메뉴 `MomoiProject › 개발 › DB 반영 스크립트 — 새 항목은 바로 적용`을 끄면 새 항목도 '적용 전'으로 둔다(`ProjectSettings/MomoiProject/DbSeedOptions.json`).
- **`GameIds` 상수는 한 박자 늦게 생긴다**: 반영 스크립트가 돌아 새 항목이 적용되고 → Unity가 다시 컴파일한 뒤에야 `GameIds.cs`에 상수(와 `Game.Data()`의 새 키 접근자)가 생긴다. 그 상수를 쓰는 코드를 반영 스크립트와 함께 넣으면 컴파일이 깨져 반영 스크립트도 돌지 않는다. 그래서 **한 응답 안에서 박자로** 나눈다 — 차례(0 기능 켜기 → 가 형식 · 반영 스크립트 → 나 새로 읽기 → 다 상수를 쓰는 코드 → 필요하면 나 한 번 더 → 라 테스트)와 반영 스크립트 파일을 박자별로 나누는 법은 **진행 매뉴얼 6절 박자 표**.
  - 새로 읽기(나): `Logs/MomoiProject/RunTests.request`를 만들어 Unity에 새로 읽기를 부르고, `GameIds.cs`를 다시 읽어 상수가 생길 때까지 기다린다(Unity는 요청을 받으면 새로 읽어 컴파일하고 테스트를 돌린 뒤 요청 파일을 지운다 — 지워지지 않고 남으면 Unity가 닫혔거나 플레이 중·멈춘 것, 12절. 요청이 지워졌는데 상수가 아직 없으면 잠시 뒤 다시 읽고(반영 스크립트는 컴파일 뒤 에디터가 한가해질 때 돈다), 그래도 없으면 `Logs/Editor.log`에서 반영 스크립트 오류를 본다).
  - Unity가 닫혀 있으면 첫 박자에서 응답을 끝내고 개발자에게 Unity를 열어 달라고 한다(반영 스크립트는 Unity가 돌린다). 상수 대신 ID 글자를 코드에 쓰지 않는다(2절).

```csharp
// Assets/MomoiProject/Code/Editor/Seeds/AchievementSeeds.cs
using GBible; using GBible.Data; using GBible.EditorTools;

static class AchievementSeeds
{
    [DbSeed("1")]   // 내용을 바꾸면 "2"로 — 바뀐 버전만 다시 돈다
    static void FirstWin()
    {
        var db = EditorDatabase.Instance;
        if (db.FindById("firstWin") == null)   // 이미 있으면 건너뛴다(개발자가 맞춘 값을 덮지 않는다)
        {
            var a = (AchievementData)EditorDatabase.Staging.Add(typeof(AchievementData), "firstWin");
            a.trigger = new EventTrigger { eventType = typeof(BattleWon).FullName };
            // a.outcomes.Add(…);
        }
    }
}
```

- 함수 하나 = 주제 하나(종류 하나·콘텐츠 묶음 하나). 파일은 `Assets/MomoiProject/Code/Editor/Seeds/<주제>Seeds.cs` — 같은 주제라도 넣는 박자가 다르면(다에서 만든 행동 · Block을 쓰는 항목) 파일을 나눈다(진행 매뉴얼 6절).
- **이미 있는 항목은 건너뛴다.** 개발자가 밸런싱한 값을 덮지 않는다. 구조(Block·참조 칸의 종류)를 고쳐야 하면 버전을 올리고 `EditorDatabase.Staging.Work(항목)`으로 작업본을 받아 그 칸만 고친다(숫자 칸은 건드리지 않는다).
- **지우기**는 버전을 올리고 `EditorDatabase.Staging.Remove(항목)`. 그 ID를 쓰는 코드(`GameIds` 상수)도 같이 지운다. **ID 바꾸기**는 `EditorDatabase.Staging.Rename(항목, 새 ID)`(DB 안의 가리키는 칸이 함께 바뀐다) — 코드의 상수 이름도 바꾼다.
- **DB › 시작 파일 › Bootstrap 칸**(Undo 방식·판 저장·판 끝 이벤트·시작할 때 이어 하기·새 판 행동·고정 틱·씬 흐름)은 항목이 아니라 DB 에셋의 값이다: `AssetDatabase.LoadAssetAtPath<GameDatabase>(EditorDatabase.DatabaseAssetPath)`로 꺼내 결정한 값(결정 기록·장르 추천)을 `EditorSet…`(참조 3-8)으로 넣고 `EditorUtility.SetDirty`·`AssetDatabase.SaveAssetIfDirty`. '적용 전'을 거치지 않고 바로 저장된다(구현 가이드 01 4절). 이 값은 '적용 전'을 거치지 않으므로 **지금 값이 기본값(`ScriptableObject.CreateInstance<GameDatabase>()`의 값)일 때만 넣는다** — 기본값이 아니면 개발자가 고른 것(시작 설정 · DB 화면)이라 넣지 않고, 결정 기록과 다르면 보고에 적어 묻는다. 그래야 메뉴 '모두 다시 돌리기'도 개발자 값을 덮지 않는다. 결정이 바뀌어 다시 넣어야 하면 개발자에게 알리고 버전을 올린다.
- **Run In Background**(창이 뒤로 가도 게임이 돈다 — DB 칸이 없는 Player Settings 값): 실시간·방치형·멀티는 같은 반영 스크립트에서 `PlayerSettings.runInBackground = true`. 틱 수·저장 간격처럼 AI가 정해 추천 승인 목록에 올린다(진행 매뉴얼 3절).
- **밸런싱 값은 표시 이름·메모·입력 칸 주석·묶음(폴더)을 함께 만든다**(짓는 법은 `NamingGuide.md` 3절 — 확률은 %(0~100)로 두고 규칙이 100으로 나눈다). 이미 있는 항목은 비어 있는 칸만 채운다.
- **값이 많은 것**(밸런싱 값·카드 수치·몬스터 수치·가중치 표 줄 — 숫자·글자·enum과 그 목록)은 CSV도 된다: 첫 칸 `id`, 나머지 칸 이름 = 필드 이름(묶음 필드는 `curve.step`, 목록은 `entries[0].weight`). 개발자가 그 DB 종류 탭의 **CSV › CSV 가져오기**로 넣는다. Block 칸(트리거·조건·결과)은 CSV에 없다.
- 메뉴 `MomoiProject › 개발 › DB 반영 스크립트 모두 다시 돌리기`가 모든 스크립트를 다시 돌린다(이미 있는 항목은 건너뛰므로 안전하다).
- 보고할 때: 저절로 적용된 새 항목 목록, '적용 전'으로 남는 고치기·지우기(있으면 Ctrl+S 요청), 확인할 것(DB 아래 "검증 통과").
- 플레이어 데이터 `run`(RunState)·`profile`(Profile)은 시작 설정이 이미 만들었다 — 필드는 그 클래스(코드)에 더하고, 새 플레이어 데이터 키만 반영 스크립트로(`PlayerDataEntry`, 8절). 화면은 반영 스크립트에서 `ScreenSetup.Ensure()`(Panel Settings·테마·DB › 화면 설정)를 먼저 부르고, 화면 항목의 `layout`을 `AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/MomoiProject/UI/Screens/….uxml")`로 넣는다(매뉴얼 35장 5-6). 그림·글꼴·소리 에셋은 개발자가 가져온다(AI는 넣을 목록과 자리를 만든다 — 18절).

## 16. 기본 진행 순서(단계 1 시스템층 → 단계 2 UI·샘플)

단계 1 — 시스템층:

1. 결정 기록·기획 명세서로 확인받은 시스템 목록(`PlanningSpecGuide.md` 5절 — 메인 룰·데이터·조각 종류·핵심 동사·이벤트·진행 흐름·부가 기능·DB 설정·개발자 몫)을 따른다.
2. 데이터 클래스(RunState·Profile·원본 클래스)와 플레이어 데이터, 쓰는 선택 요소(상태 효과·패시브 효과·액티브 효과)의 DB 종류(템플릿 복사 — 0절).
3. 규칙 함수(핵심 동사마다 행동 직전 이벤트 자리·이벤트)와 행동 직전 이벤트·이벤트 클래스.
   메인 룰(메인 룰 행동·단계 이벤트·계산 함수)과 명세서 4-2에 표시된 자리(빌려 쓰기·태그 정하기·반복 횟수 행동 직전 이벤트·판정 스위치 …, 기믹 목록 3절).
4. 행동(진행 흐름: 판 시작·전투 시작·턴 끝·판 끝 …)과 재사용 Block(효과·대상·조건).
5. 프로젝트 고유 기능(DB에서 읽고 값을 바꿀 종류 — 패시브 효과·상태 효과로 안 될 때만, 0절)이 필요하면 DB 종류 클래스 + 감시 등록(Rules의 등록 함수 → `Bootstrap.RegisterRules`, 3절).
6. 설정 DB 항목(Bootstrap·런 설정 같은 흐름에 꼭 필요한 것 — 15절)과 밸런싱 값 이름(18절 — 바꿀 숫자는 모두 에디터에).
7. 테스트(12절) — 흐름 하나(판 시작부터 전투 승리까지)가 행동만으로 끝까지 도는지(데이터는 테스트 코드가 만든다). 메인 룰은 자리 순서가 결과를 바꾸는 경우를 하나 넣는다.
8. 게임 씬(`GameSceneSetup.Ensure(경로, firstInBuild: true)` — 빈 씬 + 빌드 목록, `Assets/Dev/Scenes`) → 같은 반영 스크립트에서 이어 테스트 씬(`TestSceneSetup.Ensure(copyFrom: 그 경로)`)·기본 테스트 명령(19절).
9. 보고(12절).

한 응답 안에서는 진행 매뉴얼 6절 박자 표대로 나눈다: 선택 기능 켜기 → 2·3의 형식(데이터·이벤트·행동 직전 이벤트)과 6·8의 반영 스크립트 → 새로 읽기 → 3·4의 규칙 함수·행동·Block(그것을 쓰는 반영 스크립트 — 개발 명령 · 새 판 행동 — 는 여기서, 그 뒤 새로 읽기 한 번 더) → 7 테스트.

단계 2 — UI·샘플(진행 매뉴얼 7절):

1. 화면: `UiGuide.md` 4절 순서대로 — 참고 그림 → UI 키트 → USS → UXML → DB 항목(반영 스크립트) → 새로 읽기(15절 — 상수가 생긴 뒤) → 화면 코드 → 다이얼로그 화면 → 월드 장면 컴포넌트 → AI가 플레이로 둘러보고 고친다(12절).
2. 로직 샘플: 조각 종류·기믹 갈래마다 대표 하나와 판이 끝까지 돌 레벨 데이터 예시 하나(반영 스크립트 — 18절) + 샘플마다 규칙 테스트·테스트 명령.
3. 넣을 에셋 목록 · 넘겨주기 · 보고.

진짜 콘텐츠 전부(조각 하나하나의 효과)는 개발자가 요청할 때 0절 순서로 더하고(진행 매뉴얼 8절), 레벨 배치·대사는 개발자 몫이다. 시스템층은 그것을 쉽게 더하고, 개발자가 에디터에서 배치·밸런싱할 수 있게 해 두는 데까지다.

## 17. 질문

진행 매뉴얼 3절(질문 규칙)·2절(결정 기록)을 따른다. 요약:

- 결정 기록·명세서에 답이 있으면 묻지 않는다. 물을 것은 한 번에 모아서, 질문마다 추천·이유를 붙여(양식은 진행 매뉴얼 3절·13절). **기획은 넘겨짚지 않는다** — 자료·답에 없는 기획 사항은 추천을 붙여 묻고, 개발자가 답하거나 승인해야 확정이다. 뒤집기 비용이 큰 것(데이터 구조·메인 룰·진행 흐름·핵심 동사·저장 모양·멀티·문법 밖 기믹·개발자 몫의 범위)은 하나씩, 되돌리기 쉬운 것(이름·수치·화면 배치)은 추천 승인 목록으로 묶어 묻는다. 단계 1 이후 만들다 생긴 되돌리기 쉬운 것은 가안으로 넣고 진행할 수 있지만 승인 전에는 확정이 아니다. 구현 방식(코드 이름·내부 구조)은 묻지 않고 AI가 정한다.
- 답·승인은 바로 `Docs/Decisions.md`에 적고 채팅에도 한 줄 남긴다. 답을 기다리는 추천·가안은 '확인 기다리는 것'에 둔다.

## 18. 개발자가 읽고 바꿀 수 있는 모양으로

개발자는 코드를 읽지 않는다. 에디터(DB·모니터)에서 읽고, 밸런싱 값을 바꾸고, 테스트한다. 그래서 AI가 만드는 것은 이 모양이어야 한다.

- **숫자는 에디터에**(5절). 개발자가 바꿀 만한 숫자(체력·피해·비용·확률·보상 양·성장·시간)는 밸런싱 값·DB 항목의 칸·원본 인스턴스의 필드에 둔다. 코드에는 규칙 자체의 상수(덱 섞기 방식)만.
- **고르는 값은 enum·키워드, 가리키는 것은 참조.** 여러 개 고르는 태그 · 이벤트 조건으로 거르는 값 · 늘거나 순서가 바뀔 목록은 키워드, 규칙 코드가 값마다 다르게 도는 닫힌 목록은 enum(DB에 저장되면 값은 끝에만 더한다 — 매뉴얼 05장). "보상으로 줄 아이템", "소환할 적", "걸 상태 효과"는 글자 ID 칸이 아니라 `[DataRef(typeof(…))]` 칸으로 둔다 — DB에서 드롭다운으로 고를 수 있다. 인스턴스를 가리키면 `[DataRef(typeof(원본 클래스))]`(그 클래스의 인스턴스만 나온다). 비우는 것이 뜻이 있는 칸(적 편성의 빈 칸, 비우면 끄는 표)은 `Optional = true`(비어도 경고가 뜨지 않는다 — 꼭 골라야 하는 칸만 경고로 남긴다).
- **효과는 DB에서 문장으로 읽히게.** 가능하면 Block으로 조립한다(DB에 문장으로 보이고, 문장 안의 숫자·참조를 개발자가 바꾼다). 프로젝트 Block에는 `[BlockInfo("표시 이름", Sentence = "…{필드}…")]`를 붙여 문장에 칸이 드러나게 한다. `[BindTo]` 코드는 DB에서 이름·설명만 보이므로, 그 안의 숫자는 그 항목의 칸이나 밸런싱 값에서 읽는다.
- **이름은 개발자 화면용으로.** 플레이어 데이터·DB 칸에는 `[FieldLabel("한글")]`, Block·이벤트·행동 직전 이벤트에는 한글 표시 이름 — 짓는 규칙과 개발자가 바로잡은 이름은 `NamingGuide.md`. 코드 이름(클래스·필드·함수)은 영어로, 뜻이 드러나게, 같은 일은 같은 접두어로.
- **지켜보기 좋게.** 판의 상태는 플레이어 데이터에 있어야 모니터에 보인다(규칙이 쓰는 값을 static이나 화면 쪽에 숨기지 않는다). 다른 기능이 반응할 만한 일은 이벤트로 쏜다(모니터 타임라인에 남는다).
- **개발자 몫은 개발자 도구로.** 레벨 배치·다이얼로그 그래프·에셋은 개발자가 하므로, AI는 그 데이터 모양과 연결을 만들고 직접 채우지 않는다 — 레벨 데이터 · 그래프는 예시 하나만(단계 2 로직 샘플의 '판이 끝까지 돌 만큼'이 그 예시다, 진행 매뉴얼 7-2 — 그 밖에는 요청받을 때).
  - **레벨 데이터**: DB에서 배치하게 하려면 프로젝트 DB 종류에 `[Database("층", Group = "레벨", DeveloperAuthored = true)]`를 붙인다 — 이 종류만 개발자가 추가·삭제·ID 바꾸기를 할 수 있다(DB는 로직 쪽 종류의 추가·삭제를 잠근다, 매뉴얼 21장 3-1). 인스턴스(DB › 인스턴스)는 잠겨 있으므로 레벨 배치에 쓰지 않는다. 맵 모양만 보이는 게임(실시간 액션)은 Unity 씬·타일맵에 두고, 스폰 지점·이벤트 칸은 컴포넌트로 만들어 규칙 데이터로 읽힌다. **규칙이 지형을 읽는 칸 게임**(택틱스 · 퍼즐 · 로그라이크)은 지형을 이 레벨 DB 종류의 칸(글자 줄 `rows` 등 — 매뉴얼 36장 3-1)에 둔다 — 규칙(서버 · 규칙 테스트 포함)은 씬을 읽지 않는다. 개발자가 타일맵으로 그리게 하려면 AI가 '타일맵 → 그 DB 항목' 굽기 도구(에디터 코드)를 만든다. **대화 중심 게임**(미연시 · 생활 시뮬의 마을 이벤트)의 레벨 배치는 이벤트 표(이 `DeveloperAuthored` 종류 — 언제 · 어디서 · 조건 · 열 다이얼로그)와 다이얼로그 그래프다(진행 매뉴얼 9절).
  - **에셋**: 개발자는 리소스만 둔다 — 프리팹 · 애니메이터 · 연결은 AI(6절). 그림·소리가 필요한 곳은 반영 스크립트에서 `AssetRequests.Sprite(경로, 너비, 높이, 쓰는 곳, 느낌)`·`Audio(경로, 쓰는 곳, 초)`·`Request(경로, 쓰는 곳)`로 임시 에셋과 넣을 것 목록을 만든다. 화면·DB는 그 경로의 에셋을 바로 쓴다 — 개발자가 에셋 관리에서 진짜 파일을 넣으면(같은 자리·GUID 유지) 다시 연결할 필요가 없다(매뉴얼 39장). 경로는 종류별 폴더(그림 `Assets/Dev/Art` · 소리 `Assets/Dev/Audio` · 글꼴 `Assets/Dev/Fonts` …).

## 19. 테스트할 수 있게

개발자는 런타임에 원하는 상황을 만들어 기믹·밸런스·버그를 시험한다. AI는 프로젝트마다 그 환경을 만든다.

- **테스트 씬**: 단계 1에서 한 반영 스크립트로 `GameSceneSetup.Ensure("Assets/Dev/Scenes/게임씬.unity", firstInBuild: true)`(게임 씬 — 없을 때만 빈 씬, 빌드 목록에 넣는다) 다음 `TestSceneSetup.Ensure(copyFrom: "Assets/Dev/Scenes/게임씬.unity", scenario: "")`를 부른다 — 게임 씬을 복사해 `Assets/MomoiProject/Scenes/Test.unity`를 만들고 컴포넌트 `TestScene`(시작할 테스트 상황 · 시드 · 따로 슬롯 · 시간 배속 · 테스트 패널)을 붙인다. 테스트 씬이 이미 있으면 건너뛰고 `copyFrom`이 없으면 빈 씬으로 만들므로, **게임 씬을 먼저 만든다**. 플레이하면 그 상황으로 바로 시작하고, 게임 화면 위에 테스트 패널(테스트 명령 버튼 · 상황 저장·불러오기 · 시간 · Undo)이 뜬다(개발 빌드·에디터에서만).
- **테스트 명령** = DB › 개발 명령(`DebugCommandData`) 항목: 주입(인스턴스를 목록에 넣기 · 자원 주기 · 패시브 효과 얻기 · 상태 효과 걸기 · 적 만들기)과 발동(이벤트 쏘기)을 결과 Block으로 조립한다. 문장 안의 값(어느 인스턴스·몇 개·어느 대상)은 개발자가 드롭다운으로 바꾼다. 프로젝트 행동 실행처럼 기본 Block이 없는 것만 그 항목의 `[BindTo]`(안에서 `Game.Do(new 행동())`) — `[BindTo]` 명령에는 개발자가 바꿀 값 칸이 없으므로, 값이 필요한 명령은 프로젝트 결과 Block(값 칸이 있는 `Reward`)으로 만든다. 모두 행동으로 돌아 Undo·기록이 남는다. 반영 스크립트로 만들고 분류(`category`)로 묶는다(예: "판", "조각", "적", "상황").
- **테스트 상황** = 지금 플레이어 데이터·시드·게임 시각을 이름 붙여 저장한 파일(`TestScenarios`, `Assets/MomoiProject/Data/Tests/`). 개발자가 패널이나 모니터에서 저장하고, AI는 명세서 13절·버그 보고의 상황을 테스트 명령으로 만들어 두고 저장 방법을 알린다. 규칙 테스트는 같은 상황을 코드로 다시 만든다.
- **로직 샘플(단계 2)과 요청으로 더한 조각마다** 얻기·쓰기·걸기 테스트 명령을 만든다. 버그 보고를 받으면 같은 상황을 테스트 상황·규칙 테스트로 재현한 뒤 고친다.
- 테스트 도구는 출시 빌드에 들어가지 않는다(`session.Debug.Enabled`가 거짓이면 명령이 생기지 않고, `TestScene`은 개발 빌드에서만 패널을 띄운다).

## 20. 멀티플레이(명세서 0절이 '있음'일 때)

명세서 0절이 '있음'이면 **`Multiplayer.md`(멀티플레이에서 처음부터 지킬 것)를 끝까지 읽고** 그대로 한다(나중에 고치면 데이터 구조부터 바뀐다). 요약: 판정은 행동 안에서만 · 플레이어마다 칸(숨길 것은 Owner 키) · 서버 규칙(`Validate`) · 연출은 `SubscribeAfterAction` · 실시간 움직임은 `NetTransformSync` · 시험 모드로 지연을 넣어 확인.

## 21. 화면 쪽 도구는 프로젝트에 만든다(UI·입력·카메라·이동)

GBible에는 넣지 않는다(2026-10-06 결정 — 장르마다 달라서). 필요하면 AI가 그 프로젝트의 `Assets/MomoiProject/Code/Client`에 만든다. 3절·6절을 그대로 따르고, 아래를 더 지킨다.

- **입력은 행동까지만.** 입력 코드가 하는 일은 "무엇을 하려는가"를 행동 하나로 바꿔 `Game.Do`(화면 버튼이면 `GameScreens.Do`)에 넘기는 것까지다. 키·패드는 Input System의 동작(선택 기능 '입력'을 켰으면 `GameInput.Use(동작 에셋)` — 키 바꾸기가 따라온다), 화면 버튼은 UI Toolkit(6절). `GameView.Busy`일 때는 받지 않는다.
- **월드를 누르기 전에 화면 칸 위인지 본다.** 화면(UI Toolkit) 칸 위에서 누른 것은 월드로 보내지 않는다.
  ```csharp
  // screen: Pointer.current.position(왼쪽 아래가 원점)
  public static bool OverScreen(Vector2 screen)
  {
      var root = GameScreens.Overlay?.parent;   // 화면 층들을 담은 뿌리
      if (root?.panel == null) return false;
      var picked = root.panel.Pick(RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screen.x, Screen.height - screen.y)));
      return picked != null && picked != root && root.Contains(picked);
  }
  ```
- **누른 오브젝트 → 데이터.** 3D는 `Physics.Raycast(camera.ScreenPointToRay(screen), out var hit)`, 2D는 `Physics2D.OverlapPoint(camera.ScreenToWorldPoint(screen))`. 오브젝트를 만들 때(`ViewList`의 bind) 데이터 id를 담는 프로젝트 컴포넌트(`Client/DataTag.cs` — `public string id`)를 채워 두고, 맞은 것에서 `GetComponentInParent<DataTag>()` → `Game.Find<T>(id)`로 찾은 객체를 행동 필드에 넣는다.
- **좌표 변환은 한 곳에.** 칸 단위 게임은 칸 좌표가 데이터다(매뉴얼 36장 격자). 칸 ↔ 월드 위치 변환은 Client의 함수 한 쌍(`Board.ToWorld(칸)`·`ToCell(위치)`)에만 두고 화면 코드가 모두 그것을 쓴다.
- **이동.** 위치가 규칙 데이터인 게임(칸·턴제)은 행동이 칸을 바꾸고 화면은 그 변화를 연출로 옮긴다(Transform 보간 — `GameView.Present`). 실시간 액션은 위치가 화면 쪽이다: 방식(Transform·Rigidbody·CharacterController·NavMesh)은 명세서의 장르·조작으로 고르고, 규칙에는 판정 보고 행동만 보낸다(3절). 멀티면 움직이는 것에 `NetTransformSync`(20절).
- **카메라.** Client의 컴포넌트 하나가 맡는다(따라가기·경계·확대). 흔들기·확대 같은 연출은 `GameView.Present`·`Play`의 연출 안에서 부른다. 새 패키지(Cinemachine 등)를 들여야 하면 먼저 사람에게 묻는다(내려받기).
- **인벤토리·슬롯 UI.** 데이터는 인벤토리(매뉴얼 25장)·자리(37장 — 옮기기 예제), UI는 6절의 `List<T>` 목록 칸. 인벤토리 선택 기능은 같은 원본을 개수로 겹치는 것(슬롯 = 원본 ID + 개수)이다 — **아이템이 하나하나 다르면**(무작위 접사 · 내구도 · 강화 수치) 그 기능 대신 판 데이터의 객체 목록(고유 `id` + 원본 ID + 바뀌는 값) + 규칙 함수(슬롯 수 · 겹치기 · 나누기)로 둔다. 옮기기(끌어 놓기·두 번 누르기)는 놓는 순간 행동 하나이고, 끄는 동안에는 화면만 움직인다. 놓을 수 있는지는 `Game.CanDo`로 미리 보이고, 슬롯 수·겹치기·장비 슬롯 규칙은 규칙 함수에 둔다. 명세서에 슬롯 수·겹치기·장비 슬롯·옮기는 법이 없으면 묻는다(데이터 모양이라 뒤집기 비용이 크다).
- **생김새·배치는 가안으로 먼저 만든다**(6절). 참고를 해석한 곳·참고가 없는 화면만 '확인 기다리는 것'에 올려 승인받는다(17절). 다른 UI 방식(UGUI 프리팹·UI 에셋)은 들이지 않는다.
