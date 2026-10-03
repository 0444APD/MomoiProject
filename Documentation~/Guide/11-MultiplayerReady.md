# 11. 멀티플레이 대비

## 1. 이 단계의 목표

Momoi Project의 네트워크 연결(서버·접속·매칭)은 아직 없다. 하지만 게임 코드가 지금 몇 가지 규칙을 지켜 두면, 통신이 붙을 때 게임 코드는 거의 그대로 두고 연결 부분만 더하면 된다. 이 장의 목표는 그 규칙을 알고 카드 던전 코드가 그것을 지키는지 확인하는 것, 그리고 통신이 붙으면 시스템의 어느 부분이 어디에 연결되는지 구조로 이해하는 것이다.

이 장에서 쓰는 말:

| 말 | 뜻 |
|---|---|
| 서버 권한 | 게임 결과를 한 곳(서버, 또는 친구 방이면 방장)만 계산하고 나머지는 결과를 받아 보여 주는 방식. Momoi Project의 멀티플레이 설계는 이것을 기준으로 한다. |
| 보내는 쪽 / 받는 쪽 | 결과를 계산해 보내는 서버와, 받아서 같은 모습을 만드는 클라이언트. |
| 행동 (`GameAction`) | 플레이어 입력 하나를 담은 데이터 클래스. 멀티플레이에서는 클라이언트가 서버로 보내는 유일한 것이다. |
| Mirror · Sync · ChangeSet | 시스템이 플레이어 데이터의 사본(Mirror)과 지금 값을 비교(Sync)해 바뀐 곳만 담은 묶음(ChangeSet)을 만든다. ChangeSet의 앞으로 기록(Forward)이 네트워크로 보낼 변경분이다. |
| Snapshot | 플레이어 데이터 하나의 지금 값 전체(객체 번호 포함). 받는 쪽이 처음 들어올 때 한 번 받는다. `session.Data.Snapshot(키)`. |
| `Synced` | Sync가 ChangeSet을 만들 때마다 쏘는 저장소 이벤트(`session.Data.Synced`). 행동 안의 변경은 행동이 끝날 때 한 묶음으로 나온다. |
| `MirrorReceiver` | 받는 쪽에서 Snapshot으로 처음 모습을 만들고, 이후 ChangeSet을 적용하는 객체. |
| 세션 (`GameSession`) | 게임 한 판의 실행 전체. static 전역 상태가 없어서 서버는 매치마다 세션을 하나씩 만든다. |

---

## 2. 할 일 순서

1. 모든 입력이 행동(`Game.Do`·`Game.Use`)으로 들어가는지 확인한다.
2. 행동 필드가 id와 숫자·글자뿐인지 확인한다.
3. 게임 결과를 정하는 코드가 모두 행동·Block·결과·`[BindTo]` 안에 있는지 확인한다(그 코드가 서버에서 돈다).
4. 이벤트를 받는 코드가 화면·연출만 하는지 확인한다(받는 쪽은 이벤트를 다시 쏜다).
5. 규칙 코드가 `GBibleRuntime` 정적 입구 대신 받은 세션을 쓰는지, static 상태가 없는지 확인한다.
6. 플레이어가 여럿인 게임이면 행동의 `actorId`를 채우고, 게임 밖에 영향을 주는 결과에는 표시를 단다.
7. 통신이 붙을 때의 연결 구조(아래)를 팀이 함께 이해한다.

---

## 3. 판단 기준

### 어느 코드가 어디서 도는가

통신이 붙으면 코드는 두 무리로 나뉜다. 지금 싱글 게임에서도 이 선을 지켜 두는 것이 대비의 전부다.

| 코드 | 멀티플레이에서 도는 곳 | 지금 지킬 것 |
|---|---|---|
| 입력 처리(버튼·드래그) | 클라이언트 | 입력을 행동으로 만들어 `Game.Do`. 직접 값을 바꾸지 않는다 |
| 행동의 `Run`, 효과·대상·조건 Block, 결과, `[BindTo]` 함수 | 서버 | 게임 값을 바꾸는 곳은 여기뿐 |
| Trigger 감시, 로그, 도전과제 판정 | 서버 | 프로젝트 고유 기능의 등록도 `Started`에서 받은 세션으로 |
| 이벤트를 받는 씬 코드(`Subscribe`) | 클라이언트 | 화면·연출만. 값을 바꾸지 않는다 |
| 화면 그리기 | 클라이언트 | 플레이어 데이터를 읽기만 한다 |
| 미리보기(이 카드를 내면 피해가 얼마) | 클라이언트 | 읽기 전용 계산은 괜찮다. 결과 판정은 행동이 한다 |
| 판 저장 | 서버(또는 방장) | 행동 밖에서, 세션 단위로 |
| Undo | 서버만(허용 조건은 게임별 정책, 미정) | 싱글에서는 지금처럼 `session.Undo()` |

### 규칙별로 왜 그런가

| 규칙 | 지키지 않으면 |
|---|---|
| 입력은 행동이다 | 서버가 모르는 변경이 생긴다. 클라이언트끼리 모습이 갈린다 |
| 행동 필드는 id·값이다 | 객체 참조는 보낼 수 없다. id는 서버에서 `Game.Find`로 같은 객체를 찾는다 |
| 결과는 한 곳에서 계산한다 | 클라이언트마다 계산하면 난수·순서 차이로 결과가 갈린다. 서버 권한이면 치팅도 막는다 |
| 이벤트를 받는 쪽은 값을 바꾸지 않는다 | 받는 쪽은 서버가 보낸 이벤트를 자기 쪽에서 **다시 쏜다**. 받는 함수가 값을 바꾸면 서버가 이미 반영한 변경이 클라이언트에서 한 번 더 일어난다 |
| 난수는 `Game.Random` | 서버가 같은 시드·같은 행동으로 같은 결과를 낸다. 리플레이도 된다 |
| static 상태 금지, 세션은 매개변수로 | 서버는 매치마다 세션을 만든다. static 변수는 모든 매치가 나눠 쓴다 |
| 행동 하나 = 한 묶음 | 행동 안의 중간 모습은 밖으로 나가지 않는다. 카드 한 장의 비용·이동·효과가 한 ChangeSet으로 간다 |

### 게임 밖에 영향을 주는 결과

결제·플랫폼 도전과제 전송처럼 게임 밖에 영향을 주는 결과 Block(`Reward`)은 `Effect`를 `RewardEffect.Irreversible`로 둔다. 지금은 표시용이지만, 멀티플레이를 붙일 때 서버만 실행하게 하는 기준이 된다. 플레이어 데이터 안에서만 일어나는 결과는 `RewardEffect.Reversible`이다.

```csharp
public override RewardEffect Effect => RewardEffect.Irreversible;   // 게임 밖에 영향: 서버만 실행할 것
```

### 플레이어가 여럿일 때

- 행동의 `actorId`(행동한 플레이어 번호)는 게임 코드가 채운다. 서버 쪽 행동 코드가 이 번호로 "그 플레이어가 그 행동을 할 수 있는가"를 검사할 수 있게, 지금부터 채워 둔다.
- 행동의 `Visibility`는 `Public`(모두)·`Private`(본인만)·`Developer`(개발자 모니터만)다. 지금은 모니터 행동 탭에 표시되고, 상자 열기처럼 남에게 알리지 않을 행동에 `Private`를 둔다.
- 플레이어마다 보여 줄 값이 다르면(상대 손패) 그 값을 다른 플레이어 데이터 키로 나눠 둔다. Snapshot과 ChangeSet은 키 단위라서, 키가 나뉘어 있으면 누구에게 무엇을 보낼지 키로 가를 수 있다.

---

## 4. 통신이 붙으면 이렇게 연결된다

시스템 쪽은 이미 준비되어 있다. 빠진 것은 메시지를 주고받는 연결 층뿐이다.

```
[클라이언트]                                   [서버]
입력 → Game.Do(new PlayCard { … })  ──행동──▶  세션.Do(행동)
                                               │  Run 실행 → 값 변경 → 이벤트를 쏜다
                                               │  행동 끝 Sync → ChangeSet
                                               ▼
MirrorReceiver.Apply(ChangeSet)  ◀─ChangeSet + 이벤트─  session.Data.Synced
받은 이벤트를 다시 쏜다 → 화면·연출
```

1. **처음 들어올 때**: 서버가 플레이어 데이터마다 `session.Data.Snapshot(키)`(지금 값 전체 + 객체 번호)를 보낸다. 받는 쪽은 `MirrorReceiver`의 `Load`로 같은 모습의 객체를 만든다.
2. **입력**: 클라이언트는 행동만 보낸다. 게임 코드의 문법은 싱글과 같은 `Game.Do(...)`다. 싱글에서는 그 자리에서 실행되고, 멀티에서는 서버로 보내진다.
3. **결과**: 서버가 행동을 실행하면 행동 끝 Sync가 ChangeSet을 만들고 `Synced` 이벤트로 나온다. 행동 안에서 다른 행동·능력을 불렀어도 한 묶음이다. 서버는 그 ChangeSet의 앞으로 기록과, 같은 행동에서 쏜 이벤트를 모든 클라이언트에 보낸다.
4. **받는 쪽**: `MirrorReceiver`의 `Apply`로 변경을 적용한다. 여러 곳에서 가리키는 같은 객체도 받는 쪽에서 같은 인스턴스로 이어진다. 그다음 받은 이벤트를 자기 세션에서 다시 쏜다. 화면·연출 코드는 싱글과 똑같이 이벤트를 받고 플레이어 데이터를 읽는다.
5. **실시간 게임**: 행동마다가 아니라 서버 틱(예: 초당 20번)마다 ChangeSet을 모아 보낸다.

ChangeSet은 바뀐 곳만 담는다. 작은 행동 하나는 수십 바이트, 50유닛 광역기도 약 1.8KB다. 처음 Snapshot은 2만 유닛 기준 약 1.6MB다.

### 테스트용 멀티플레이 (설계, 보류 중)

인터넷으로 떨어진 두 컴퓨터에서 시험하는 환경의 설계가 `../TestMultiplayer.md`에 있다. 작업은 보류 중이고, 문서 속 코드 이름은 모두 가칭이다. 요점만:

- AWS용 서버와 **같은 서버 프로그램**을 테스터의 컴퓨터에서 돌린다. 클라이언트는 주소·포트·이름만 넣고 접속한다.
- 게임 코드는 "어디에 어떻게 붙는지"를 모른다. 바뀌는 것은 접속 방식(테스트: 주소 직접 입력 / AWS: 매칭)뿐이고 그것은 설정으로 고른다.
- 서버는 Unity 없이 코어(`GBible.Core`)만으로 돈다. 그래서 게임 규칙 코드(행동·Block·`[BindTo]`·데이터 클래스)는 UnityEngine 없이 컴파일되는 쪽이 서버로 옮기기 쉽다.

---

## 5. 카드 던전에서 실제로

카드 던전은 싱글 게임이지만, 통신이 붙어도 바꿀 곳이 거의 없게 짜여 있다. 하나씩 확인한다.

**입력은 행동이다.** 런 스크립트의 입력 창구는 행동을 보내기만 한다. 실패(에너지 부족)는 예외로 돌아오고, 그때는 바뀐 것이 모두 취소되어 있다.

```csharp
public bool TryPlay(string cardId, string targetId, out string error)
{
    error = null;
    try
    {
        Game.Do(new PlayCard { target = cardId, targetId = targetId });
        return true;
    }
    catch (System.InvalidOperationException e)
    {
        error = e.Message;
        return false;
    }
}
```

멀티플레이에서 이 실패는 서버가 판정한다. 클라이언트가 미리 막고 싶으면(버튼 흐리게) 읽기만 하는 검사를 화면에 두되, 최종 판정은 행동 안의 검사다.

**행동 필드는 id다.** `PlayCard`는 덱 카드 id와 적 id, `BuyCard`는 원본 ID와 가격, `DrinkPotion`은 물약 ID와 대상 id만 싣는다.

**결과는 행동·Block·`[BindTo]` 안에서만 계산한다.** 피해(`Rules.Damage`), 무작위 대상(`RandomEnemy`, 난수 흐름 `"cardTarget"`), 보상 골드(`"loot"`), 연쇄 번개(`[BindTo]`), 유물 발동(`session.Triggers.Register`)이 모두 서버 쪽 코드가 된다. 유물 감시는 `Started`에서 받은 세션으로 등록하므로 매치마다 따로 돈다.

```csharp
static void Start(GameSession session)
{
    // …
    session.Triggers.Register(id, relic.trigger, repeat: true, fire: moment =>
    {
        var run = session.Game.Data().Run;
        if (run == null || !run.relics.Contains(id)) return;
        session.Triggers.RunOutcomes(outcomes, moment, "Relics");
    });
}
```

**이벤트는 연출용이다.** `DamageDealt`·`EnemyKilled`·`CardPlayed`는 행동 안에서 쏘고, 화면은 받아서 숫자·이펙트만 띄운다. 로그(`kills`·`maxTurnDamage`)와 도전과제(`firstWin`)는 서버의 Trigger가 받는다. 받는 쪽 클라이언트가 이벤트를 다시 쏘아도 화면만 반응하므로 값이 두 번 바뀌지 않는다.

**정적 입구는 씬 코드에서만.** `GBibleRuntime.Game`은 런 스크립트·화면 코드(클라이언트)에서만 쓴다. Block은 물려받은 `Game`, `[BindTo]` 함수는 컨텍스트의 `c.Game`, 시작 코드는 매개변수 `session`을 쓴다.

**저장은 세션 단위다.** 판 저장 컴포넌트는 싱글 클라이언트용이다. 멀티플레이에서는 서버가 매치 세션의 판을 저장하고(`session.ExportRun()`으로 JSON 글자를 받아 서버 저장소에), 클라이언트는 저장하지 않는다.

---

> **다른 장르라면** 턴제 경쟁 게임(유형 A: 자체 서버)은 완전 서버 권한이다. 재화·인벤토리·구매 내역은 서버에만 두고, 경쟁 매치의 난수 시드는 서버만 가진다. 카드 던전의 규칙 그대로 행동만 보내면 된다.

> **다른 장르라면** 실시간 친구 방(유형 B: 방장 권한)은 방장의 세션이 서버 역할을 한다. 행동마다가 아니라 틱마다 ChangeSet을 모아 보낸다. 위치·속도처럼 매 프레임 바뀌는 값은 플레이어 데이터가 아니라 별도 동기화(보간)로 다루고, 규칙 값(체력·점수)만 플레이어 데이터로 둔다.

---

## 6. 체크리스트

- [ ] 모든 입력이 `Game.Do`·`Game.Use`로 들어간다.
- [ ] 행동 필드에 객체 참조·에셋·델리게이트가 없다.
- [ ] 게임 값을 바꾸는 코드는 행동·Block·결과·`[BindTo]` 안에만 있다.
- [ ] 이벤트를 받는 씬 코드는 값을 바꾸지 않는다(다시 쏘아도 안전하다).
- [ ] 규칙 난수는 모두 `Game.Random`이다.
- [ ] Block·결과·`[BindTo]`·`Started` 코드에 `GBibleRuntime` 정적 입구와 static 게임 상태가 없다.
- [ ] 플레이어가 여럿이면 `actorId`를 채우고, 남에게 숨길 값은 별도 키로 나눴다.
- [ ] 게임 밖에 영향을 주는 결과는 `RewardEffect.Irreversible`이다.
- [ ] 게임 규칙 코드가 UnityEngine 형식(MonoBehaviour·`Time`·`UnityEngine.Random`)에 기대지 않는다(그림 같은 에셋 필드는 예외).

---

## 7. 더 보기

- [../Coding.md](../Coding.md) — "받는 쪽 (네트워크)": Snapshot·Synced·MirrorReceiver
- [01. 플레이어 데이터](../Manual/01-PlayerData.md) — Sync 시점, 같은 객체를 두 곳에서 가리키기, `Synced`
- [02. 행동과 Undo](../Manual/02-Actions-Undo.md) — `actorId`·`Visibility`, 행동 하나 = 한 묶음
- [10. 이벤트](../Manual/10-Events.md) — 쏘기·받기, `SubscribeAll`(이벤트 중계)
- [12. 결과(Outcome)](../Manual/12-Outcomes.md) — `Reward`·`RewardEffect`
- [../TestMultiplayer.md](../TestMultiplayer.md) — 테스트용 멀티플레이 설계(보류 중)
