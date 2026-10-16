# 11. 멀티플레이 대비

멀티는 지바이블에 있다(테스트용·매칭큐·사설 호스트 — 매뉴얼 [40장](../../Manual/40-Multiplayer.md)). 전송만 갈아 끼우므로 게임 코드는 싱글과 같은 문법(`Game.Do`·`Data`·`SubscribeAfterAction`)이다. 이 장은 카드 던전 코드가 멀티에서 그대로 돌려면 기능 로직(Rules)과 UI(Client)가 무엇을 지키나를 이유와 함께 보인다. 명세서 0절이 '있음'인 프로젝트에서 처음부터 지킬 목록은 AI 문서 [Multiplayer.md](../Multiplayer.md)가 주인이다 — 그것을 먼저 읽는다. 싱글 프로젝트도 2·3절을 지켜 두면 나중에 멀티를 붙일 때 데이터 구조를 고치지 않는다.

> [!note]- 이 장에서 쓰는 말
> | 말 | 뜻 |
> |---|---|
> | 서버 권한 | 게임 결과를 한 곳(매칭큐 = 서버, 친구 방 = 방장)만 계산하고 나머지는 결과를 받아 보여 주는 방식. GBible 멀티는 모두 이것이다. |
> | 기능 로직 | 게임 규칙 코드. 행동·Block·결과·`[BindTo]` 함수·데이터 클래스. `Assets/MomoiProject/Code/Rules`에 있다. 멀티에서는 판정하는 쪽(서버·방장)에서만 돈다. |
> | UI | 화면·입력·연출 코드. `Assets/MomoiProject/Code/Client`에 있다. 모든 프로그램에서 돈다. |
> | 중개 | 기능 로직과 UI 사이에서 행동을 넘기고 결과·이벤트를 돌려주는 층. 라이브러리 몫이다. 싱글은 세션(`GameSession`), 멀티는 서버 중개 `NetServer`·클라이언트 중개 `NetClient`. |
> | 묶음 | 서버에서 일어난 한 묶음(행동 하나 또는 틱 하나)의 바뀐 곳과 이벤트. 바뀐 곳은 Mirror·Sync·ChangeSet([09장](09-Habits.md) '이 장에서 쓰는 말')으로 찾는다. |
> | 공개 범위(`DataVisibility`) | 플레이어 데이터 키를 누구에게 보내나: `Public`(모두)·`Owner`(주인만)·`Server`(보내지 않음). **키 단위**다. |
> | 세션(`GameSession`) | 게임 한 판의 실행 전체. static 전역 상태가 없어서 서버는 매치(방)마다 세션을 하나씩 만든다. |

---

## 1. 구조

코드는 네 블록으로 나뉜다. 게임 코드가 쓰는 것은 양 끝(기능 로직·UI)뿐이고, 가운데 중개는 라이브러리가 갈아 끼운다.

```
싱글:  [기능 로직] ─ [로컬 중개(GameSession)] ─ [UI]
멀티:  [기능 로직] ─ [서버 중개(NetServer)] ~~전송~~ [클라이언트 중개(NetClient)] ─ [UI]
```

- **기능 로직** = Rules 폴더. 값을 바꾸고 결과를 정한다.
- **UI** = Client 폴더. 입력을 행동으로 보내고, 알림을 받아 그린다.
- UI는 중개 너머를 모른다. `Game.Do`가 그 자리에서 실행되든 서버로 보내지든 UI 코드는 같다.

> [!note]- 어느 코드가 어느 블록인가
> | 코드 | 블록(멀티에서 도는 곳) | 지킬 것 |
> |---|---|---|
> | 입력 처리(버튼·드래그) | UI(모든 프로그램) | 입력을 행동으로 만들어 보낸다(3절). 직접 값을 바꾸지 않는다 |
> | 행동의 `Run`, 효과·대상·조건 Block, 결과, `[BindTo]` 함수 | 기능 로직(서버·방장) | 게임 값을 바꾸는 곳은 여기뿐 |
> | 트리거 감시, 로그, 도전과제 판정, 시간 규칙(상태 효과·예약) | 기능 로직(서버·방장) | 클라이언트는 조건 판정·시간 규칙을 하지 않는다. 프로젝트 고유 기능의 등록 함수는 Rules에 두고 세션을 매개변수로 받는다(2절) |
> | 이벤트를 받는 화면 코드 | UI | 화면·연출만. 값을 바꾸지 않는다 |
> | 화면 그리기 | UI | 플레이어 데이터를 읽기만 한다 |
> | 미리보기(`Game.Preview`) | UI | 이쪽 데이터로 돈다(난수가 걸린 값은 서버와 어긋날 수 있다). 결과 판정은 행동이 한다 |
> | 판 저장 · Undo | 판정하는 쪽 | 4절 |

> [!note]- 통신이 붙으면 이렇게 이어진다
> ```
> [UI(클라이언트)]                                      [기능 로직(서버·방장)]
> 입력 → Game.Do(new PlayCard { … })  ──행동(대상은 id만)──▶  actorId를 보낸 사람으로 덮음 → 대상 id를 서버의 객체로 → Validate → 세션.Do(행동)
>                                                          │  Run → 값 변경 → 이벤트를 쏜다 → 행동 끝 Sync
>                                                          ▼
> 바뀐 곳을 '행동 밖 변경'으로 적용  ◀─묶음(바뀐 곳 + 이벤트, 공개 범위대로)─
> → 화면 연결·Data.Changed, 받은 이벤트 → SubscribeAfterAction 받는 쪽 → 연출
> ```
> 1. **처음 들어올 때**: 서버가 그 사람에게 보이는 키마다 처음 모습(통째)을 보낸다. 늦게 들어오거나 다시 접속해도 같다.
> 2. **입력**: 클라이언트의 `Game.Do`는 행동을 보내기만 한다(기록 상태 `Sent`). 결과는 묶음으로 나중에 온다. 서버가 거절하면 예외가 아니라 `NetClient.ActionFailed` → 화면 알림(`GameScreens.ShowFailure`).
> 3. **결과**: 서버가 행동을 실행하면(행동 안의 행동·`Game.Use`까지 한 묶음) 바뀐 곳과 그 행동이 쏜 이벤트를 키의 공개 범위대로 사람마다 골라 보낸다. 실패한 행동의 이벤트는 보내지 않는다.
> 4. **받는 쪽**: 바뀐 곳은 행동 밖 변경으로 적용되어 화면 연결(binding-path)·`Data.Changed`·`GameView.Redraw`가 싱글과 똑같이 알림을 받는다. 받은 이벤트는 `SubscribeAfterAction`으로 받는 쪽에만 온다.
> 5. **실시간**: 행동마다가 아니라 서버 틱마다 묶는다. 매 순간 움직이는 것(내 캐릭터·탄환)은 규칙 데이터가 아니라 컴포넌트 `NetTransformSync`로 위치만 맞춘다.
>
> 거절 문장·양 제한·형식 번호표 같은 자세한 동작은 매뉴얼 40장 1-1~1-5.

> [!note]- 시험
> - **혼자**: 테스트 씬 › 컴포넌트 '테스트 씬' › 멀티플레이 = 시험 모드(지연 0.1 이상). 서버를 이 프로그램 안에 띄우고 이 화면은 클라이언트가 된다 — 싱글에서 숨는 문제(`Do` 직후 결과 읽기)가 드러난다. 모니터 머리에서 '서버' 세션을 고르면 서버 쪽 데이터·타임라인이 보인다.
> - **두 컴퓨터**: 테스트 패널(F1) › 멀티 › 방 생성·방 참가(직접 접속). 매칭큐는 매칭큐 서버 + 매칭 찾기로 로컬에서 끝까지(40장 2절).
> - **규칙 테스트**: 플레이어마다 데이터는 `session.Data.DefinePerPlayer(…)` + `AddPlayer(1)`·`AddPlayer(2)` + 행동의 `actorId`·`using (session.ActingAs(번호))`. 서버·클라이언트를 함께 띄우려면 코어의 `MemoryNetwork { Manual = true }` + `NetServer` + `NetClient`(40장 3-2, 관례 12절).
> - 서버 빌드는 같은 Unity 프로젝트의 Dedicated Server 빌드다(40장 1-9). 서버 중개(`NetServer`·`NetHub`)는 엔진 없는 코어라 테스트에서도 같은 코드가 돈다 — 규칙이 Unity API를 쓰지 않는 까닭이다(관례 1절).

## 2. 기능 로직(Rules)이 지킬 것

- [필수] 게임 값을 바꾸는 코드는 행동·Block·결과·`[BindTo]` 안에만 둔다.
- 행동에는 필요한 정보를 필드로 넣고, 대상(덱 카드·적)은 객체 그대로 넣는다. 보낼 때는 id만 실리고, 서버가 `Run` 직전에 그 id로 자기 데이터의 객체를 찾아 넣는다. 그래서 대상 클래스는 `public string id;`가 있고 플레이어 데이터 안에 있어야 한다. [필수] 에셋·델리게이트·데이터 밖 객체는 싣지 않는다.
- [필수] 행동·이벤트의 필드는 생성 코드가 쓸 수 있는 것만 둔다: `[Serializable]` 클래스, 중첩 컬렉션·`HashSet`·readonly 필드·자동 속성·`object` 칸 없음, `#if UNITY_EDITOR` 필드 없음. 보내지 않을 값이면 `[NonSerialized]`. 어기면 행동은 보내기 전에 실패하고 이벤트는 빠진다. 이벤트는 `[GameEvent]` 클래스다(구조체는 보낼 수 없다).
- [필수] 난수는 `Game.Random`만 쓴다.
- [필수] `GBibleRuntime`(정적 입구)을 부르지 않는다. 받은 것을 쓴다: Block은 물려받은 `Game`, `[BindTo]` 함수는 컨텍스트의 `c.Game`, 등록 코드는 매개변수 `session`. static 게임 상태를 두지 않는다.
- 누가 한 행동인지는 행동의 `actorId`로 본다. `Do`가 비어 있으면 이 프로그램의 번호(`session.LocalPlayer`, 싱글 0)로 채우고, 서버는 보낸 사람 번호로 덮으므로 규칙은 그대로 믿는다. "할 수 있는가"(내 차례·내 유닛)는 기본으로 행동 안의 `Game.Fail`로 막는다(Undo·규칙 테스트에 함께 든다). 행동을 돌리기 전에 막아야 할 때만 서버 규칙 `Validate`가 그 규칙 함수를 불러 거절한다 — 같은 검사를 두 곳에 쓰지 않는다(Multiplayer.md, 40장 1-1).

DB 항목(패시브 효과·상태 효과·도전과제·로그·액티브 효과 …)은 라이브러리가 세션마다 DB로 등록하므로 카드 던전의 유물(패시브 효과)은 매치마다 따로 돈다. 프로젝트가 직접 등록하는 규칙(직접 만든 DB 종류의 감시 — 04장 8절의 `RelicRules.Register`, 행동 처리기, 규칙이 받는 이벤트, 틱 규칙)은 받은 세션으로 등록하는 함수로 Rules에 둔다.

```csharp
public static void Register(GameSession session, IEnumerable<RelicData> relics)
{
    // … 항목마다 — 받은 session으로만 등록한다(GBibleRuntime.Session을 쓰지 않는다)
    session.Triggers.Register(id, relic.trigger, repeat: true, fire: moment =>
    {
        var run = session.Game.Data().Run;
        if (run == null || !run.relics.Contains(id)) return;
        session.Triggers.RunOutcomes(outcomes, moment, "Relics");
    });
}
```

[필수] **어디서 부르나 — `Bootstrap.RegisterRules` 한 곳.** `RegisterRules`는 `GBibleRuntime.SessionCreated`에 연결되어 **모든 세션**(싱글·방장·서버 빌드의 화면 세션, `GBibleRuntime.CreateSession()`으로 따로 만든 시험 모드의 서버와 봇·매칭큐의 방)이 DB로 만들어진 직후, 새 판 행동보다 먼저 온다([01장](01-StartProject.md) 4절). 그래서 규칙 쪽 등록은 여기 한 줄이면 모든 세션에서 돈다.

- `Started`는 화면 세션에만 온다. 규칙 등록을 `Started`에 두면 시험 모드 서버·매칭큐 방에서 그 규칙이 빠진다.
- [필수] 같은 세션에 두 번 등록하면 두 번 발동한다. `GameNet.ServerStarted`·`RoomStarting`에서 등록 함수를 다시 부르지 않는다. 거기서는 서버 객체가 있어야 거는 서버 규칙(`server.Validate`·투영 `server.Project`·녹화)만 건다(4절 접힌 칸). 플레이어마다 키는 DB 칸이 정의한다.

> [!note]- 규칙별로 왜 그런가
> | 규칙 | 지키지 않으면 |
> |---|---|
> | 값은 행동 안에서만 바꾼다 | 서버가 모르는 변경이 생긴다. 클라이언트끼리 모습이 갈린다 |
> | 대상은 id 있는 데이터 객체다 | 객체는 그대로 보낼 수 없다. id가 없거나 데이터 밖의 객체면 서버가 찾을 수 없다 |
> | 결과는 한 곳에서 계산한다 | 클라이언트마다 계산하면 난수·순서 차이로 결과가 갈린다. 서버 권한이면 치팅도 막는다 |
> | 난수는 `Game.Random` | 서버가 같은 시드·같은 행동으로 같은 결과를 낸다. 리플레이도 된다 |
> | static 상태 금지, 세션은 매개변수로 | 매칭큐 서버 한 대가 방마다 세션을 만든다. static 변수는 모든 방이 나눠 쓴다 |
> | 행동 하나 = 한 묶음 | 행동 안의 중간 모습은 밖으로 나가지 않는다. 카드 한 장의 비용·이동·효과가 한 묶음으로 간다 |
> | UnityEngine 형식(`MonoBehaviour`·`Time`·`UnityEngine.Random`)에 기대지 않는다 | 코어 서버·테스트에서 같은 코드가 돌지 않는다. 그림 같은 에셋 필드는 예외(규칙은 ID로 다룬다) |

> [!note]- 카드 던전에서 확인한 것
> - `PlayCard`는 덱 카드와 적을 객체로 들지만 보낼 때는 두 id만 실린다. `BuyCard`는 원본 ID와 가격, `DrinkPotion`은 물약 ID와 대상 id만 싣는다.
> - 싱글에서도 같은 길을 탄다. 실행 직전에 id로 다시 찾으므로 Undo·판 불러오기 뒤에 버튼이 쥔 옛 객체도 지금 객체로 바뀐다.
> - 피해(`Rules.Damage`), 무작위 대상(`RandomEnemy`, 난수 흐름 `"cardTarget"`), 보상 골드(`"loot"`), 연쇄 번개(`[BindTo]`), 유물 발동(패시브 효과), 로그(`kills`·`maxTurnDamage`)와 도전과제(`firstWin`)가 모두 기능 로직이다 — 멀티에서는 서버에서만 돈다.

> [!note]- 게임 밖에 영향을 주는 결과
> 결제·플랫폼 전송처럼 게임 밖에 영향을 주는 코딩한 결과(`Reward`)는 `Effect`를 `RewardEffect.Irreversible`로 둔다. 지금은 표시용이다(실행 방식은 같다). 멀티에서는 결과가 늘 판정하는 쪽에서만 돌므로 따로 막을 것은 없다. 플레이어 데이터 안에서만 일어나는 결과는 `RewardEffect.Reversible`이다.
> ```csharp
> public override RewardEffect Effect => RewardEffect.Irreversible;   // 게임 밖에 영향
> ```

## 3. UI(Client)가 지킬 것

- [필수] 입력은 행동으로만 보낸다. 화면 버튼은 `GameScreens.Do(() => new 행동 { … })`(화면 코드에서는 `OnClick`)으로 — 연출·선택 대기 중(`GameView.Busy`)이면 막고, 플레이어용 실패(싱글의 `GameFailure`·멀티의 서버 거절)를 같은 알림으로 띄운다. 값을 직접 바꾸지 않는다.
- `Do` 바로 뒤에서 결과를 읽지 않는다. 바뀐 곳 알림(화면 연결 binding-path·`GameView.Redraw`·`Data.Changed`)과 이벤트를 받고 그린다.
- 연출용 이벤트는 `session.Events.SubscribeAfterAction<T>`로 받는다. 행동이 성공한 뒤에만 오고([06장](06-Presentation.md) 3절), 클라이언트에서는 서버가 보낸 이벤트가 이쪽으로만 온다. [필수] 받는 함수에서 값을 바꾸지 않는다.
- "내 것"은 플레이어마다 데이터의 이름 그대로 읽는다: 내 핸드 = `Game.Get<Hand>("hand")`(생성 접근자 `Game.Data()`도 같다) — 행동 밖이면 이 화면 플레이어의 몫(`hand:번호`)으로 풀린다. binding-path·`GameView.Redraw` 키에도 `hand`를 그대로 적는다(이 화면 몫이 바뀌면 그린다). 내 번호는 매치마다 바뀌므로 화면이 번호를 붙잡아 두지 않는다(필요하면 `Game.Session.LocalPlayer`를 그때그때, 40장 1-2).

카드 던전의 카드 누르기:

```csharp
// 실패 알림·Busy 막기는 GameScreens.Do가 맡는다. 결과는 Redraw·SubscribeAfterAction이 그린다.
GameScreens.Do(() => new PlayCard { card = card, target = target });
```

> [!note]- 왜 그런가
> - **결과를 바로 읽지 않는다**: 싱글에서는 `Do`가 끝나면 값이 이미 바뀌어 있지만, 멀티 클라이언트는 행동을 보내기만 하고 결과는 나중에 온다. 카드 던전의 `BattleHud`는 `GameView.Redraw`에 그리는 함수를 등록해 두고, `run`이 바뀐 프레임 끝에 한 번 그린다. 결과가 언제 오든 같은 코드다.
> - **실패를 한 곳에서**: 싱글은 `Do`가 예외(`GameFailure`)로, 멀티 클라이언트는 `ActionFailed` 알림으로 돌려준다. `GameScreens.Do`가 둘을 같은 알림으로 바꾸므로 화면 곳곳에서 예외를 잡지 않는다. 버튼을 미리 흐리게 하는 검사(`Game.CanDo`)는 화면에 둬도 되지만 최종 판정은 행동 안의 검사다.
> - **이벤트는 행동이 끝난 뒤**: `Subscribe`는 쏘는 자리에서 받는 쪽을 바로 부른다. 행동이 나중에 실패하면 값은 취소되었는데 연출만 남는다. `SubscribeAfterAction`은 행동이 성공하면 넘기고 실패하면 버린다. 클라이언트에서 `Subscribe`로 받는 연출은 아예 불리지 않는다(규칙은 서버에서 이미 돌았다). 카드 던전의 `BattlePresenter`는 `SubscribeAfterAction`으로 받는다.
> - **받는 함수에서 값을 바꾸지 않는다**: 바뀐 곳은 서버가 이미 보냈다. 받는 함수가 값을 바꾸면 서버와 어긋난다.

## 4. 저장·Undo·여러 플레이어

- **판 저장**: 클라이언트는 판을 저장하지 않는다(판 저장이 저절로 꺼진다). 방장·화면 없는 서버는 화면 세션이라 DB › Bootstrap의 판 저장대로 저장하고, 매칭큐 서버는 진행 중인 매치를 `NetHubOptions.SaveMatchEachAction`으로 저장한다(40장 1-6). [필수] 멀티의 데이터는 서버 것이다(기기 = 설정만, Steam = 도전과제 — 40장 1-1): 클라이언트는 이 기기의 프로필을 불러오지도 쓰지도 않고, 사람마다 남는 값은 '방이 끝나도 서버에 남김'(서버의 계정 저장소 — 사설 서버 · 매칭큐 모두)에 둔다. 서버 세션의 프로필(메타 구역)은 그 서버 전체의 저장이다(공개 범위 기본 `Server`).
- **Undo**: 멀티에는 플레이어가 누르는 무르기가 없다(클라이언트의 `Undo`는 경고만). "n턴 되돌리기" 같은 기믹은 되감기다: 턴 시작 행동에서 `Game.MarkRewind()`, 기믹 행동에서 `Game.Rewind`·`RewindKey`·`RewindEntity`(40장 1-4). 싱글은 지금처럼 `session.Undo()`.
- **플레이어가 여럿**: 플레이어마다 칸을 둔다 — 판에 하나인 목록 `players[]`, 또는 DB › 플레이어 데이터의 '플레이어마다'(실제 키 `hand:1`·`hand:2`, 코드는 `hand` 그대로). 남에게 숨길 것은 그 칸의 '그 사람에게만 보임'(Owner), 그 사람에게도 숨길 것(덱 순서)은 '서버만'. 키 하나 안의 일부만 숨길 것(안개 속 유닛·덮인 카드)은 투영 `server.Project`(+ 상태 효과·보정은 `HideStatuses`, 40장 1-2). 숨길 이벤트는 `IOwnedEvent`(주인만 받음), 서버 규칙끼리만 쓰는 이벤트는 `[GameEvent(ServerOnly = true)]`.

> [!note]- 플레이어가 여럿일 때 더
> - 플레이어마다 키는 DB › 플레이어 데이터의 칸으로 정의한다('플레이어마다' + '그 사람에게만 보임' · '서버만(그 사람에게도 숨김)' · 매칭큐면 '방이 끝나도 서버에 남김') — 몫은 플레이어가 들어올 때 서버가 만든다. 서버 객체가 필요한 것(`Validate`·투영·녹화)만 서버가 준비될 때 건다(친구 방·시험 모드 `GameNet.ServerStarted`, 매칭큐 `GameNet.RoomStarting`, 첫 턴은 `GameNet.RoomReady` — 40장 4-1 예제). 감시·처리기 같은 규칙 등록은 여기가 아니라 `Bootstrap.RegisterRules`다(2절).
> - 행동의 `Visibility`(`Public`·`Private`·`Developer`)는 모니터 행동 탭의 표시일 뿐이다. 남에게 숨기는 것은 데이터 키의 공개 범위로 한다.
> - 매칭큐의 승패는 규칙이 정하고 `GameNet.Hub.EndMatch(room, NetMatchResult[])`로 알린다(행동 안이 아니라 행동 뒤에 — 예: 결정 이벤트를 `SubscribeAfterAction`으로 받아서).

> [!note]- 다른 장르라면
> - **턴제 경쟁(매칭큐)**: 완전 서버 권한이다. [필수] 재화·인벤토리·구매 내역은 서버 저장소에만 두고, 경쟁 매치의 난수 시드는 서버만 가진다. 카드 던전의 규칙 그대로 행동만 보내고, 차례 검사는 행동 안(돌리기 전에 막을 것만 `Validate`), 핸드는 '플레이어마다' + '그 사람에게만 보임', 덱 순서는 '서버만'. 재화·컬렉션은 계정 값이고, 매치 밖의 상점·덱 편성은 계정 방(40장 1-6).
> - **실시간 친구 방(사설 호스트)**: 방장의 세션이 서버 역할을 하고, 방장이 나가면 판이 끝난다(이어받기 없음). 묶음은 틱마다 간다. 위치·속도처럼 매 프레임 바뀌는 값은 플레이어 데이터가 아니라 `NetTransformSync`(entityId를 모든 프로그램에서 같게, owner = 플레이어 번호)로, 규칙 값(체력·점수)만 플레이어 데이터로 둔다. 맞힘은 클라이언트가 판정 보고 행동으로 보내고 방장의 규칙이 확인한다(그 행동 안에서 사거리·쿨다운·살아 있나를 판 데이터로 검사). 적은 `owner = 0`(방장 화면이 움직이고 공격 결정도 방장 화면의 AI가 판정 보고 행동으로 — 40장 4-2).

## 5. 체크리스트

- [ ] 모든 입력이 행동으로 들어간다(화면 버튼은 `GameScreens.Do`·`OnClick`).
- [ ] 행동의 대상 필드는 id가 있는 플레이어 데이터 객체이고, 행동·이벤트에 에셋·델리게이트·생성 코드가 쓸 수 없는 필드가 없다.
- [ ] 게임 값을 바꾸는 코드는 행동·Block·결과·`[BindTo]` 안에만 있다.
- [ ] 규칙 난수는 모두 `Game.Random`이다.
- [ ] Rules 코드에 `GBibleRuntime` 정적 입구와 static 게임 상태가 없다.
- [ ] 규칙 쪽 등록(직접 만든 DB 종류의 감시·행동 처리기·규칙이 받는 이벤트·틱 규칙)은 모두 `Bootstrap.RegisterRules`에서 한 번 불리고, `Started`·`ServerStarted`·`RoomStarting`에서 다시 부르지 않는다.
- [ ] UI는 `Do` 직후 결과를 읽지 않고 알림을 받아 그린다. 연출은 `SubscribeAfterAction`으로 받고, 받는 함수는 값을 바꾸지 않는다.
- [ ] 플레이어가 여럿이면 플레이어마다 칸이 있고(DB '플레이어마다'), 남에게 숨길 값은 '그 사람에게만 보임'·'서버만', 한 키 안의 일부는 투영이다. 규칙은 `actorId`로 누가 했는지 본다.
- [ ] 멀티 프로젝트면 테스트 씬의 시험 모드(지연 0.1 이상)로 한 번 돌려 봤다.

> [!note]- 더 보기
> - [Multiplayer.md](../Multiplayer.md) — 멀티 프로젝트에서 처음부터 지킬 것(주인 문서)
> - [40. 멀티플레이](../../Manual/40-Multiplayer.md) — 4블록·공개 범위·되감기·매칭큐·사설 호스트·시험·`GameNet` API
> - [../Coding.md](../../Coding.md) — "받는 쪽 (네트워크)": Snapshot·Synced·MirrorReceiver
> - [01. 플레이어 데이터](../../Manual/01-PlayerData.md) — Sync 시점, 같은 객체를 두 곳에서 가리키기
> - [02. 행동과 Undo](../../Manual/02-Actions-Undo.md) — `actorId`·`Visibility`, 행동 하나 = 한 묶음
> - [10. 이벤트](../../Manual/10-Events.md) — 쏘기·받기, `SubscribeAfterAction`
> - [12. 결과(Outcome)](../../Manual/12-Outcomes.md) — `Reward`·`RewardEffect`
> - [../TestMultiplayer.md](../../TestMultiplayer.md) — 테스트용 멀티 설계 기록
