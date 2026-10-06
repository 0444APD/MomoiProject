# 11. 멀티플레이 대비

네트워크 연결(서버·접속·매칭)은 아직 없다(보류 중). 이 장은 지금 지켜 두면 통신이 붙을 때 게임 코드를 거의 고치지 않아도 되는 규칙만 다룬다. 기능 로직(Rules)과 UI(Client)가 지킬 것이 따로 있다.

> [!note]- 이 장에서 쓰는 말
> | 말 | 뜻 |
> |---|---|
> | 서버 권한 | 게임 결과를 한 곳(서버, 또는 친구 방이면 방장)만 계산하고 나머지는 결과를 받아 보여 주는 방식. Momoi Project의 멀티플레이 설계는 이것을 기준으로 한다. |
> | 기능 로직 | 게임 규칙 코드. 행동·Block·결과·`[BindTo]` 함수·데이터 클래스. `Assets/MomoiProject/Code/Rules`에 있다. 멀티에서는 서버에서 돈다. |
> | UI | 화면·입력·연출 코드. `Assets/MomoiProject/Code/Client`에 있다(`RunDirector`·화면·Bootstrap). 멀티에서는 클라이언트에서 돈다. |
> | 중개 | 기능 로직과 UI 사이에서 행동을 넘기고 결과·이벤트를 돌려주는 층. 라이브러리 몫이다. 싱글에서는 세션(`GameSession`)이 이 일을 한다. |
> | 행동(`GameAction`) | 플레이어 입력 하나를 담은 데이터 클래스. 멀티에서는 클라이언트가 서버로 보내는 유일한 것이다. |
> | Mirror · Sync · ChangeSet | 시스템이 플레이어 데이터의 사본(Mirror)과 지금 값을 비교(Sync)해 바뀐 곳만 담은 묶음(ChangeSet)을 만든다. ChangeSet의 앞으로 기록(Forward)이 네트워크로 보낼 변경분이다. |
> | Snapshot | 플레이어 데이터 하나의 지금 값 전체(객체 번호 포함). 받는 쪽이 처음 들어올 때 한 번 받는다. `session.Data.Snapshot(키)`. |
> | `Synced` | Sync가 ChangeSet을 만들 때마다 쏘는 저장소 이벤트(`session.Data.Synced`). 행동 안의 변경은 행동이 끝날 때 한 묶음으로 나온다. |
> | `MirrorReceiver` | 받는 쪽에서 Snapshot으로 처음 모습을 만들고, 이후 ChangeSet을 적용하는 객체. |
> | 세션(`GameSession`) | 게임 한 판의 실행 전체. static 전역 상태가 없어서 서버는 매치마다 세션을 하나씩 만든다. |

---

## 1. 구조

코드는 네 블록으로 나뉜다. 게임 코드가 쓰는 것은 양 끝(기능 로직·UI)뿐이고, 가운데 중개는 라이브러리가 갈아 끼운다.

```
멀티:  [기능 로직] ─ [서버 중개] ~~네트워크~~ [클라이언트 중개] ─ [UI]
싱글:  [기능 로직] ─ [로컬 중개(GameSession)] ─ [UI]
```

- **기능 로직** = Rules 폴더. 값을 바꾸고 결과를 정한다.
- **UI** = Client 폴더. 입력을 행동으로 보내고, 알림을 받아 그린다.
- UI는 중개 너머를 모른다. 지금 `Game.Do`가 그 자리에서 실행되든, 나중에 서버로 보내지든 UI 코드는 같다.

> [!note]- 어느 코드가 어느 블록인가
> | 코드 | 블록(멀티에서 도는 곳) | 지금 지킬 것 |
> |---|---|---|
> | 입력 처리(버튼·드래그) | UI(클라이언트) | 입력을 행동으로 만들어 `Game.Do`. 직접 값을 바꾸지 않는다 |
> | 행동의 `Run`, 효과·대상·조건 Block, 결과, `[BindTo]` 함수 | 기능 로직(서버) | 게임 값을 바꾸는 곳은 여기뿐 |
> | Trigger 감시, 로그, 도전과제 판정 | 기능 로직(서버) | 프로젝트 고유 기능의 등록 함수는 Rules에 두고 세션을 매개변수로 받는다. Unity 시작에 거는 연결(`Started`)만 Bootstrap에 |
> | 이벤트를 받는 화면 코드 | UI(클라이언트) | 화면·연출만. 값을 바꾸지 않는다 |
> | 화면 그리기 | UI(클라이언트) | 플레이어 데이터를 읽기만 한다 |
> | 미리보기(이 카드를 내면 피해가 얼마) | UI(클라이언트) | 읽기 전용 계산은 괜찮다. 결과 판정은 행동이 한다 |
> | 판 저장 | 서버(또는 방장) | 행동 밖에서, 세션 단위로(4절) |
> | Undo | 서버만(허용 조건은 게임별 정책, 미정) | 싱글에서는 지금처럼 `session.Undo()` |

> [!note]- 통신이 붙으면 이렇게 이어진다
> 시스템 쪽(Snapshot·Sync·ChangeSet·`MirrorReceiver`)은 이미 있다. 빠진 것은 메시지를 주고받는 중개 층이다.
> ```
> [UI]                                           [기능 로직]
> 입력 → Game.Do(new PlayCard { … })  ──행동──▶  세션.Do(행동)
>                                                │  Run 실행 → 값 변경 → 이벤트를 쏜다
>                                                │  행동 끝 Sync → ChangeSet
>                                                ▼
> MirrorReceiver.Apply(ChangeSet)  ◀─ChangeSet + 이벤트─  session.Data.Synced
> 받은 이벤트를 다시 쏜다 → 화면·연출
> ```
> 1. **처음 들어올 때**: 서버가 플레이어 데이터마다 `session.Data.Snapshot(키)`를 보낸다. 받는 쪽은 `MirrorReceiver`의 `Load`로 같은 모습의 객체를 만든다.
> 2. **입력**: 클라이언트는 행동만 보낸다. 문법은 싱글과 같은 `Game.Do(...)`다.
> 3. **결과**: 서버가 행동을 실행하면 행동 끝 Sync가 ChangeSet을 만들고 `Synced`로 나온다. 행동 안에서 다른 행동·능력을 불렀어도 한 묶음이다. 서버는 그 ChangeSet의 앞으로 기록과, 같은 행동에서 쏜 이벤트를 모든 클라이언트에 보낸다.
> 4. **받는 쪽**: `MirrorReceiver`의 `Apply`로 변경을 적용한다. 여러 곳에서 가리키는 같은 객체도 같은 인스턴스로 이어진다. 그다음 받은 이벤트를 자기 세션에서 다시 쏜다. UI는 싱글과 똑같이 이벤트를 받고 플레이어 데이터를 읽는다.
> 5. **실시간 게임**: 행동마다가 아니라 서버 틱(예: 초당 20번)마다 ChangeSet을 모아 보낸다.
>
> ChangeSet은 바뀐 곳만 담는다. 작은 행동 하나는 수십 바이트, 50유닛 광역기도 약 1.8KB다. 처음 Snapshot은 2만 유닛 기준 약 1.6MB다.

> [!note]- 테스트 모드와 테스트용 멀티(계획, 보류 중)
> - **테스트 모드**(계획): 네 블록을 네트워크 없이 한 프로세스에서 모두 돌린다. 서버 중개·클라이언트 중개를 거치는 길을 에디터 안에서 바로 시험하려는 것이다.
> - **테스트용 멀티**(설계): 인터넷으로 떨어진 두 컴퓨터에서 시험한다. AWS용 서버와 같은 서버 프로그램을 테스터의 컴퓨터에서 돌리고, 클라이언트는 주소·포트·이름만 넣고 접속한다. 바뀌는 것은 접속 방식(테스트: 주소 직접 입력 / AWS: 매칭)뿐이고 설정으로 고른다. 설계는 [../TestMultiplayer.md](../../TestMultiplayer.md)에 있고, 문서 속 코드 이름은 모두 가칭이다.
> - 서버는 Unity 없이 코어(`GBible.Core`)만으로 돈다. 그래서 기능 로직은 UnityEngine 없이 컴파일되는 쪽이 서버로 옮기기 쉽다.

## 2. 기능 로직(Rules)이 지킬 것

- 게임 값을 바꾸는 코드는 행동·Block·결과·`[BindTo]` 안에만 둔다.
- 행동에는 필요한 정보를 필드로 넣고, 대상(덱 카드·적)은 객체 그대로 넣는다. 시스템이 보낼 때는 id만 싣고, 받는 쪽(서버)이 `Run` 직전에 그 id로 자기 데이터의 객체를 찾아 넣는다.
- 그래서 대상 클래스는 `public string id;`가 있고 플레이어 데이터 안에 있어야 한다. 에셋·델리게이트·데이터 밖 객체는 싣지 않는다.
- 난수는 `Game.Random`만 쓴다.
- `GBibleRuntime`(정적 입구)을 부르지 않는다. 받은 것을 쓴다: Block은 물려받은 `Game`, `[BindTo]` 함수는 컨텍스트의 `c.Game`, 등록 코드는 매개변수 `session`.
- static 게임 상태를 두지 않는다.

카드 던전의 유물 감시(`RelicRules.Register`, Rules)는 받은 세션으로 등록하므로 매치마다 따로 돈다.

```csharp
public static void Register(GameSession session, IEnumerable<RelicData> relics)
{
    // … 유물마다
    session.Triggers.Register(id, relic.trigger, repeat: true, fire: moment =>
    {
        var run = session.Game.Data().Run;
        if (run == null || !run.relics.Contains(id)) return;
        session.Triggers.RunOutcomes(outcomes, moment, "Relics");
    });
}
```

Unity에서는 Client의 Bootstrap이 `Started`에서 이 함수를 한 줄로 부른다: `RelicRules.Register(session, GameDatabase.Load()?.All<RelicData>());`

> [!note]- 규칙별로 왜 그런가
> | 규칙 | 지키지 않으면 |
> |---|---|
> | 값은 행동 안에서만 바꾼다 | 서버가 모르는 변경이 생긴다. 클라이언트끼리 모습이 갈린다 |
> | 대상은 id 있는 데이터 객체다 | 객체는 그대로 보낼 수 없다. 시스템이 id로 바꿔 보내고 서버가 같은 id의 객체를 넣는데, id가 없거나 데이터 밖의 객체면 찾을 수 없다 |
> | 결과는 한 곳에서 계산한다 | 클라이언트마다 계산하면 난수·순서 차이로 결과가 갈린다. 서버 권한이면 치팅도 막는다 |
> | 난수는 `Game.Random` | 서버가 같은 시드·같은 행동으로 같은 결과를 낸다. 리플레이도 된다 |
> | static 상태 금지, 세션은 매개변수로 | 서버는 매치마다 세션을 만든다. static 변수는 모든 매치가 나눠 쓴다 |
> | 행동 하나 = 한 묶음 | 행동 안의 중간 모습은 밖으로 나가지 않는다. 카드 한 장의 비용·이동·효과가 한 ChangeSet으로 간다 |
> | UnityEngine 형식(`MonoBehaviour`·`Time`·`UnityEngine.Random`)에 기대지 않는다 | 서버는 Unity 없이 돈다. 그림 같은 에셋 필드는 예외(규칙은 ID로 다룬다) |

> [!note]- 카드 던전에서 확인한 것
> - `PlayCard`는 덱 카드와 적을 객체로 들지만 보낼 때는 두 id만 실린다. `BuyCard`는 원본 ID와 가격, `DrinkPotion`은 물약 ID와 대상 id만 싣는다.
> - 지금 싱글에서도 같은 길을 탄다. 실행 직전에 id로 다시 찾으므로 Undo·판 불러오기 뒤에 버튼이 쥔 옛 객체도 지금 객체로 바뀐다.
> - 피해(`Rules.Damage`), 무작위 대상(`RandomEnemy`, 난수 흐름 `"cardTarget"`), 보상 골드(`"loot"`), 연쇄 번개(`[BindTo]`), 유물 발동(`session.Triggers.Register`)이 모두 기능 로직이다.
> - 로그(`kills`·`maxTurnDamage`)와 도전과제(`firstWin`)는 서버의 Trigger가 받는다.
> - 유물 감시의 등록 내용은 Rules의 함수(`RelicRules.Register(session, relics)`)로 두었다. 지금은 Client의 Bootstrap이 `GBibleRuntime.Started`에서 부르지만, 서버라면 서버가 세션을 만드는 자리에서 같은 함수를 부르면 된다. 원칙: 등록 함수는 Rules에, Unity 시작에 거는 연결은 Bootstrap에.

> [!note]- 게임 밖에 영향을 주는 결과
> 결제·플랫폼 도전과제 전송처럼 게임 밖에 영향을 주는 결과 Block(`Reward`)은 `Effect`를 `RewardEffect.Irreversible`로 둔다. 지금은 표시용이지만, 멀티플레이를 붙일 때 서버만 실행하게 하는 기준이 된다. 플레이어 데이터 안에서만 일어나는 결과는 `RewardEffect.Reversible`이다.
> ```csharp
> public override RewardEffect Effect => RewardEffect.Irreversible;   // 게임 밖에 영향: 서버만 실행할 것
> ```

## 3. UI(Client)가 지킬 것

- 입력은 행동으로만 보낸다(`Game.Do`·`Game.Use`). 값을 직접 바꾸지 않는다.
- `Do` 바로 뒤에서 결과를 읽지 않는다. 알림(데이터 변경·이벤트)을 받고 그린다.
- 실패는 입력 창구 한 곳에서 받아 메시지로 넘긴다. 화면 곳곳에서 예외를 잡지 않는다.
- 연출용 이벤트는 `session.Events.SubscribeAfterAction<T>`로 받는다. 행동이 성공한 뒤에만 오므로 연출이 늘 행동 뒤에 튼다([06장](06-Presentation.md) 3절). 받는 함수에서 값을 바꾸지 않는다.

카드 던전의 입력 창구는 `RunDirector.TryPlay` 하나다. 화면은 돌려받은 메시지를 보여 주기만 한다.

```csharp
public bool TryPlay(DeckCard card, Fighter target, out string error)
{
    error = null;
    try
    {
        Game.Do(new PlayCard { card = card, target = target });
        return true;
    }
    catch (System.InvalidOperationException e)
    {
        error = e.Message;   // 실패하면 바뀐 값은 모두 취소되어 있다
        return false;
    }
}
```

> [!note]- 왜 그런가
> - **결과를 바로 읽지 않는다**: 싱글에서는 `Do`가 끝나면 값이 이미 바뀌어 있지만, 멀티에서는 행동을 보내기만 하고 결과(ChangeSet)는 나중에 온다. 카드 던전의 `BattleHud`는 `GameView.Redraw`에 그리는 함수를 등록해 두고, `run`이 바뀐 프레임 끝에 한 번 그린다. 결과가 언제 오든 같은 코드다.
> - **실패를 한 곳에서**: 지금 라이브러리는 실패를 `Do`의 예외로 돌려준다. 멀티에서는 실패도 서버가 판정해 나중에 알려 준다. 예외를 잡는 곳이 창구 하나면 그때 그 창구만 고친다. 버튼을 미리 흐리게 하는 읽기 전용 검사는 화면에 둬도 되지만, 최종 판정은 행동 안의 검사다.
> - **이벤트는 행동이 끝난 뒤**: `Subscribe`는 쏘는 자리에서 받는 쪽을 바로 부른다. 행동이 나중에 실패하면 값은 취소되었는데 연출만 남는다. `SubscribeAfterAction`은 행동 안에서 쏜 이벤트를 모아 두었다가 행동이 성공하면 넘기고 실패하면 버린다. 카드 던전의 `BattlePresenter`는 이것으로 받는다.
> - **받는 함수에서 값을 바꾸지 않는다**: 받는 쪽 클라이언트는 서버가 보낸 이벤트를 자기 세션에서 **다시 쏜다**. 받는 함수가 값을 바꾸면 서버가 이미 반영한 변경이 한 번 더 일어난다.

## 4. 저장·Undo·여러 플레이어

- **판 저장**: DB › Bootstrap의 판 저장 설정은 싱글 클라이언트용이다. 멀티에서는 서버가 매치 세션의 판을 저장하고(`session.ExportRun()`으로 JSON 글자를 받아 서버 저장소에), 클라이언트는 저장하지 않는다.
- **Undo**: 서버만 한다. 허용 조건은 아직 정하지 않았다. 싱글에서는 지금처럼 `session.Undo()`.
- **플레이어가 여럿**이면 행동의 `actorId`(행동한 플레이어 번호)를 지금부터 채운다.

> [!note]- 플레이어가 여럿일 때 더
> - `actorId`는 게임 코드가 채운다. 서버 쪽 행동 코드가 이 번호로 "그 플레이어가 그 행동을 할 수 있는가"를 검사할 수 있게 한다.
> - 행동의 `Visibility`는 `Public`(모두)·`Private`(본인만)·`Developer`(개발자 모니터만)다. 지금은 모니터 행동 탭에 표시된다. 상자 열기처럼 남에게 알리지 않을 행동에 `Private`를 둔다.
> - 플레이어마다 보여 줄 값이 다르면(상대 손패) 그 값을 다른 플레이어 데이터 키로 나눠 둔다. Snapshot과 ChangeSet은 키 단위라서, 키가 나뉘어 있으면 누구에게 무엇을 보낼지 키로 가를 수 있다.

> [!note]- 다른 장르라면
> - **턴제 경쟁 게임(유형 A: 자체 서버)**: 완전 서버 권한이다. 재화·인벤토리·구매 내역은 서버에만 두고, 경쟁 매치의 난수 시드는 서버만 가진다. 카드 던전의 규칙 그대로 행동만 보내면 된다.
> - **실시간 친구 방(유형 B: 방장 권한)**: 방장의 세션이 서버 역할을 한다. 행동마다가 아니라 틱마다 ChangeSet을 모아 보낸다. 위치·속도처럼 매 프레임 바뀌는 값은 플레이어 데이터가 아니라 별도 동기화(보간)로 다루고, 규칙 값(체력·점수)만 플레이어 데이터로 둔다.

## 5. 체크리스트

- [ ] 모든 입력이 `Game.Do`·`Game.Use`로 들어간다.
- [ ] 행동의 대상 필드는 id가 있는 플레이어 데이터 객체이고, 에셋·델리게이트가 없다.
- [ ] 게임 값을 바꾸는 코드는 행동·Block·결과·`[BindTo]` 안에만 있다.
- [ ] 규칙 난수는 모두 `Game.Random`이다.
- [ ] Rules 코드에 `GBibleRuntime` 정적 입구와 static 게임 상태가 없다.
- [ ] UI는 `Do` 직후 결과를 읽지 않고 알림을 받아 그린다.
- [ ] 실패는 입력 창구 한 곳에서 받는다.
- [ ] 이벤트를 받는 화면 코드는 값을 바꾸지 않고, 연출은 `SubscribeAfterAction`으로 받는다.
- [ ] 플레이어가 여럿이면 `actorId`를 채우고, 남에게 숨길 값은 별도 키로 나눴다.
- [ ] 게임 밖에 영향을 주는 결과는 `RewardEffect.Irreversible`이다.

> [!note]- 더 보기
> - [../Coding.md](../../Coding.md) — "받는 쪽 (네트워크)": Snapshot·Synced·MirrorReceiver
> - [01. 플레이어 데이터](../../Manual/01-PlayerData.md) — Sync 시점, 같은 객체를 두 곳에서 가리키기, `Synced`
> - [02. 행동과 Undo](../../Manual/02-Actions-Undo.md) — `actorId`·`Visibility`, 행동 하나 = 한 묶음
> - [10. 이벤트](../../Manual/10-Events.md) — 쏘기·받기, `SubscribeAll`(이벤트 중계)
> - [12. 결과(Outcome)](../../Manual/12-Outcomes.md) — `Reward`·`RewardEffect`
> - [../TestMultiplayer.md](../../TestMultiplayer.md) — 테스트용 멀티플레이 설계(보류 중)
