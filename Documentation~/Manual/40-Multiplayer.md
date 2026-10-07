# 40. 멀티플레이 — 4블록 · 테스트용 · 매칭큐 · 사설 호스트

**한 줄 요약**: 멀티는 세 가지다. **테스트용 멀티**(직접 접속, 인터넷 두 컴퓨터), **매칭큐 멀티**(턴제 경쟁 — 서버가 판정, 로비·줄·매치·점수), **사설 호스트 멀티**(친구 방 — 방장이 판정, Steam 로비·릴레이). 셋 다 같은 구조(4블록)다. 클라이언트는 **행동만 보내고**, 판정하는 쪽(서버·방장)이 실행해 **바뀐 곳과 이벤트를 돌려준다**. 전송만 갈아 끼우므로 게임 코드(`Game.Do`·`Data`·`SubscribeAfterAction`)는 싱글과 같다.

```
싱글:  [기능 로직] ─ [로컬 중개(GameSession)] ─ [UI]
멀티:  [기능 로직] ─ [서버 중개(NetServer)] ~~전송~~ [클라이언트 중개(NetClient)] ─ [UI]
```

**언제 쓰는가**

- 친구 넷이 같이 하는 실시간 협동(매지크래프트·세피리아 같은) → 사설 호스트.
- 랭크 매칭으로 둘이 겨루는 턴제(카드 대전) → 매칭큐.
- 개발 중 다른 컴퓨터의 팀원과 시험 → 테스트용(직접 접속), 혼자 시험 → 시험 모드(한 프로그램 안).

이 문서에서 쓰는 용어:

| 용어 | 뜻 |
|---|---|
| 서버 중개(`NetServer`) | 판정하는 세션(서버·방장)에 붙어 행동을 받아 실행하고, 바뀐 곳·이벤트를 클라이언트에 보낸다. |
| 클라이언트 중개(`NetClient`) | 이 세션을 '받는 쪽'(`SessionRole.Client`)으로 바꾼다. `Do`는 서버로 보내고, 받은 것을 적용한다. |
| 묶음(Batch) | 서버에서 일어난 한 묶음(행동 하나 또는 틱 하나)의 바뀐 곳(Mirror 앞으로 기록·단순 값)과 이벤트. |
| 공개 범위(`DataVisibility`) | 플레이어 데이터 키를 누구에게 보내는가: `Public`(모두) · `Owner`(주인만) · `Server`(보내지 않음). |
| 형식 번호표 | 접속할 때 서로 보내는 '번호 → 형식 이름' 표. 에디터와 빌드처럼 번호가 달라도 같은 형식으로 잇는다. |
| 시험 모드 | 서버를 이 프로그램 안에 띄우고 이 화면은 그 클라이언트가 되는 모드(메모리 전송, 일부러 지연). |
| 매칭큐 서버(`NetHub`) | 전송 하나로 로비·줄·여러 방(매치)을 돌리는 서버. 방 하나 = 서버 중개 하나 + 세션 하나. |
| 방(`NetRoom`) | 매치 하나. `room.Server`(서버 중개)·`room.Session`·`room.Accounts`. |
| 실시간 위치 맞추기(`NetTransformSync`) | 매 순간 움직이는 것(내 캐릭터·탄환)을 규칙 데이터 대신 위치만 보내 맞추는 컴포넌트. |
| 리플레이(`NetReplay`) | 받은 묶음을 시각과 함께 녹화한 것. 계산을 다시 하지 않고 그대로 다시 적용해 튼다. |
| 바꿔 끼우는 곳 | 실제 서비스(Steam·AWS)와 닿는 자리. 지금은 사설(가짜)로 대신하고, 계정이 준비되면 그 자리만 바꾼다(1-12). |

---

## 1. 동작 규칙

### 1-1. 4블록

- **클라이언트**(`SessionRole.Client`)
  - `Game.Do(행동)` → 서버로 보낸다(대상 객체는 id만, `ActionReferences.Detach`). 돌려주는 기록의 상태는 `ActionStatus.Sent`. 결과는 묶음으로 온다.
  - 생성 코드로 쓸 수 없는 필드(중첩 컬렉션·`KeyValuePair`·`HashSet`·readonly 필드·자동 속성·`object` 칸에 상자로 든 숫자·글자 등)가 있는 행동은 보내기 전에 `InvalidOperationException`("행동 'X'을(를) 서버로 보낼 수 없습니다: 필드 경로 …")으로 실패하고 기록에 Error를 남긴다(형식마다 한 번). 서버에 빈 칸으로 도착하는 대신 바로 드러난다.
  - 서버가 거절하면 `NetClient.ActionFailed`(예외가 아니다). Unity에서는 `GameScreens.ShowFailure`로 알림이 뜬다.
  - 받은 바뀐 곳은 **행동 밖 변경**으로 적용된다 → `Data.Changed`·`DataChanged`·화면 연결(binding-path)이 싱글과 똑같이 알림을 받는다.
  - 받은 이벤트는 **`SubscribeAfterAction`으로 받는 쪽에만** 온다(`EventBus.PublishRemote`). `Subscribe`·`SubscribeAll`(규칙 쪽)에는 오지 않는다 — 규칙은 서버에서 이미 돌았다.
  - 하지 않는 것: Undo(경고만), 조건 판정(트리거·도전과제), 시간 규칙(상태 효과·패시브 주기·예약). 시계는 받은 묶음의 서버 시각으로 맞춘다.
  - `Game.Preview`(미리보기)는 이쪽 데이터로 돈다(난수 시드가 서버와 달라 확률은 어긋날 수 있다).
  - 이벤트 안의 객체 필드는 **이쪽 데이터의 같은 객체**를 가리킨다(서버가 객체 번호로 보낸다). 이쪽이 받지 못한 데이터(숨긴 키)의 객체면 `null`.
- **서버·방장**(`SessionRole.Server`)
  - 받은 행동의 `actorId`를 **보낸 사람 번호로 덮고** `utc`·`tick`을 지운다(속일 수 없게).
  - `[SystemAction]`이 붙은 행동(예약·주기·선택 효과·개발자 편집)은 클라이언트가 보내도 실행하지 않는다.
  - `NetServer.Validate(플레이어, 행동)`이 거절 글을 돌려주면 실행하지 않는다(내 차례가 아님 등). 검사 코드가 예외를 내도 거절로 돌려준다("행동 검사 중 오류: …" — 결과가 오지 않아 클라이언트가 기다리기만 하지 않게).
  - 묶음: 마지막으로 보낸 뒤의 바뀐 곳·이벤트를 키의 공개 범위대로 사람마다 골라 보낸다. 미리보기·실패한 행동의 이벤트는 보내지 않는다. 방장의 미리보기(`Game.Preview`)는 객체의 네트워크 번호를 바꾸지 않는다(`Mirror.KeepIds`) — 클라이언트는 같은 인스턴스를 그대로 가리킨다. 실패한 행동을 되돌릴 때도 번호를 지키므로, 실패 뒤에도 같은 객체는 같은 인스턴스로 간다.
  - 데이터 변경은 받는 쪽에 보낼 것(`Data.Synced`)에 먼저 넣고 그다음 이쪽 알림(`Data.Changed`)을 부른다. 알림을 받은 코드가 다른 데이터를 바꿔도 받는 쪽에는 차례가 뒤바뀌어 가지 않는다.
  - 보낼 수 없는 이벤트(생성 코드로 쓸 수 없는 필드가 있음)는 빼고 기록에 Error("이벤트 'X'을(를) 클라이언트에 보낼 수 없어 뺐습니다: …", 형식마다 한 번)를 남긴다. 행동은 성공한다.
  - 늦게 들어왔거나 새로 보이게 된 키는 그 사람에게만 처음 모습(통째)을 보낸다.
- **받은 것 검사·보내는 양 제한**(서버·클라이언트 모두)
  - 받은 데이터의 객체 겹침은 256단계까지, 글자·목록 길이는 남은 바이트로 검사한다. 깨지거나 악의적인 메시지는 스택을 넘기거나 거대한 할당을 하지 못하고, 그 메시지만 버린 뒤 기록에 Error(서버 "…의 메시지를 처리하지 못했습니다" · 클라이언트 "서버 메시지를 처리하지 못했습니다")를 남긴다.
  - 서버는 연결마다 1초에 `NetServerOptions.MaxMessagesPerSecond`(기본 300, 0 = 제한 없음)개, 매칭큐 로비는 60개까지 받는다. 넘친 것은 버리고(버린 것이 행동이면 거절 결과 "요청이 너무 많아 이 행동을 처리하지 않았습니다…"를 돌려준다 — 보낸 쪽이 그 결과를 끝없이 기다리지 않게), 네 배를 넘기면 연결을 끊는다("메시지를 너무 많이 보냈습니다"). 형식 번호표(입장할 때 받는 형식 이름 목록)는 `NetProtocol.MaxTypeTable`(65536)개까지다.
  - 받은 이름은 제어 문자를 빼고 앞뒤 공백을 지운 뒤 32자로 자른다(`NetProtocol.CleanName` — 화면·기록·계정 키에 쓰인다).
  - WebSocket 전송: 메시지 하나는 32MB까지(조각을 이어 붙인 전체도). 받기 버퍼는 실제로 온 만큼 잡는다(머리에 적힌 길이만큼 미리 잡지 않는다). 제어 프레임(핑·퐁·닫기)은 125바이트까지. 보낼 대기열(WebSocket 핑에 전송이 스스로 하는 퐁 포함)이 8192개를 넘으면(받는 쪽이 멈춤) 연결을 닫고 끊김(`Disconnected`)으로 알린다. 연결 알림(`Connected`)은 그 연결에서 받은 것보다 늘 먼저 온다(악수 바로 뒤에 온 Hello도 잃지 않는다).
- **내 번호**: `session.LocalPlayer`(싱글 0). 클라이언트는 서버가 준 번호, 방장은 자기 번호. `Do`가 `actorId`를 비워 둔 행동에 채운다.

### 1-2. 공개 범위

| 정하지 않았을 때 | 공개 범위 |
|---|---|
| 세션 구역 키 | `Public` |
| 메타 구역 키(프로필) | `Server` — 프로필은 각자 자기 쪽 것을 쓴다(매칭큐의 계정 값은 서버 저장소) |
| `$schedule`(예약) · `$rewind`(Undo 지점) | `Server` |

- 정하기: `session.Data.SetVisibility("hand:2", DataVisibility.Owner, owner: 2)`. **키 단위**다 — 남에게 숨길 것은 플레이어마다 키를 나눈다(`hand:1`·`hand:2`).
- 선택 대기(`Game.Ask`, 29장)도 같다: 답할 플레이어가 정해진 선택은 그 사람의 키(`$choices:번호`, 주인에게만 보냄)에 두므로 남의 후보·이어서 할 행동이 다른 클라이언트에 가지 않는다. 답할 사람을 정하지 않으면(`player` 생략) 멀티에서는 그 행동을 한 플레이어(`session.Actions.CurrentActor` — 지금 돌고 있는 바깥 행동의 `actorId`)에게 묻는다. 누구나 답하게 하려면 `player: ""`. `Game.Choice`·`Game.IsWaiting`(→ `GameView.Busy`)은 이 프로그램이 답할 선택(모두에게 온 것과 내 번호에게 온 것)만 본다 — 방장 화면에 남의 고르기 창이 뜨지 않고, 남의 선택 때문에 입력이 막히지 않는다. 서버는 다른 사람에게 온 선택의 답을 거절한다("다른 플레이어에게 온 선택입니다").
- 이벤트는 기본으로 모두에게 간다. 숨길 이벤트는 둘 중 하나로:
  - 주인만: 이벤트 클래스가 `IOwnedEvent`를 구현한다(`int Owner => player;`) — 그 플레이어(와 모두 보는 녹화)에게만 간다. `Owner`가 0이면 모두에게.
  - 서버만: `[GameEvent("…", ServerOnly = true)]` — 클라이언트에 보내지 않는다(서버 규칙끼리 주고받는 이벤트).

### 1-3. 되돌리기와 되감기

- 멀티에서 플레이어가 누르는 일반 '무르기'는 없다(클라이언트 `Undo`는 경고만).
- 되돌리기를 쓰는 곳: ① 규칙의 기믹(되감기 — `Game.Rewind`·`RewindKey`·`RewindEntity`, 1-4) ② 개발자 시험(서버의 개발 명령 '세션 › Undo', 테스트 패널의 Undo — 매칭큐 방은 Undo 이력을 두지 않는다, 1-6) ③ 판 밖 리플레이(받은 묶음을 녹화해 다시 트는 것 — 서버 없이).
- 서버의 Undo·되감기는 바뀐 곳으로 모두에게 간다.

### 1-4. 되감기(기믹)

- 턴이 시작될 때(턴 시작 행동 안): `Game.MarkRewind("3턴")` — 그때의 세션 구역을 지점으로 남긴다(`$rewind`, 판 저장에 들어감, 클라이언트에 보내지 않음, 기본 10개 — `session.Rewind.Keep`).
- 기믹 행동 안에서:
  - `Game.Rewind(2)` — 세션 구역 전체를 2지점 전 모습으로(그 뒤 생긴 키는 기본값).
  - `Game.RewindKey("hand:2", 1)` — 키 하나만.
  - `Game.RewindEntity(unit, 2)` — 객체 하나만(id로 찾는다). 그 객체에 남이 한 일(피해)은 함께 돌아가고, 그 객체가 남에게 한 일은 남는다. 예전 방식 데이터(01장 6절) 안의 객체도 된다.
- 지점은 컬렉션 요소마다 요소 번호를 남긴다. 되감으면 그 뒤 빠졌던 요소(쓰러진 유닛)는 원래 인스턴스로 돌아오고, 남은 요소는 순서가 바뀌었어도 자기 인스턴스를 지킨다 — 화면·코드가 쥔 객체가 엉뚱한 값을 받지 않는다.
- 되감기도 그 행동의 변경이다(예전 방식 데이터도) → Undo·판 저장·멀티 동기화·리플레이에 그대로 남는다. 난수는 되감지 않는다.
- 지점보다 멀리 가면 플레이어용 실패 `momoi.fail.rewindTooFar`.

### 1-5. 형식 번호표

- 접속할 때 서로 `Mirror.TypeTable()`을 보내고, 받는 쪽은 `MirrorReceiver.UseTypes`로 상대 번호를 자기 형식으로 바꿔 읽는다.
- 상대에게만 있는 형식이 실제로 오면 "서로 다른 버전의 게임입니다" 예외(그 형식 이름이 나온다). 게임 버전 글자(`NetServerOptions.GameVersion`·`Application.version`)가 다르면 입장부터 거절한다.

### 1-6. 매칭큐 서버(`NetHub`)

```
접속 → 입장 확인(INetAuthenticator) → 로비(점수·전적) → 줄(INetMatchmaker) → 성사 → 전원 수락 → 방(NetRoom) → RoomReady → … → EndMatch(결과) → 점수(Elo)·전적 저장(INetPlayerStore) → 로비
```

- 한 계정은 한 줄·한 매치에만. 같은 계정이 다시 들어오면 옛 연결이 아직 살아 있을 때는 새 연결을 끊기만 하고(거절이 아니다 — 클라이언트가 잠시 뒤 다시 시도한다), 옛 연결이 3초 이상 조용하면 새 연결이 그 자리를 이어받는다(1-10).
- 전원이 수락해야 방을 만든다. 수락하지 않으면(거절·시간 넘김 `AcceptSeconds`) 그 사람은 로비로, 수락한 사람은 **기다린 시간 그대로** 다시 줄.
- 방이 만들어지면 묶인 차례대로 플레이어 번호 1, 2 …(`NetServer.Reserve`). `RoomStarting`에서 서버 규칙을 걸고, 정원이 다 들어오면 `RoomReady`(첫 턴 시작). `ReadySeconds` 안에 다 안 들어오면 매치를 없던 것으로(점수 변화 없음). 방을 열지 못하면(방 세션 만들기 실패) 수락한 사람들을 로비로 돌려보낸다(다시 줄을 세우지 않는다).
- 로비로 돌아오면(매치 끝·취소) 결과가 오지 않은 행동은 기다리지 않는다(`NetClient.PendingActions` = 0 — 방이 닫히는 사이에 보낸 행동은 결과가 오지 않는다).
- 매치 중에 끊겼다 다시 접속하면 로비를 거치지 않고 그 매치의 같은 자리로 들어가 지금 모습을 받는다.
- 진행 중인 매치를 저장한다(`NetHubOptions.SaveMatchEachAction`): 행동이 있었던 방만 `MatchSaveSeconds`(기본 2초, 0 = 행동마다)마다 한 번, 방이 준비될 때·서버를 닫을 때는 바로. 서버를 다시 켜면 `ResumeOpenMatches`로 다시 열고, 플레이어가 다시 접속하면 이어 간다. 이미 시작했던 매치면 정원이 다 돌아와도 `RoomReady`를 다시 쏘지 않는다(첫 턴을 다시 시작하지 않고 저장된 판에서 이어 간다). `ReadySeconds` 안에 다 돌아오지 않으면 없던 것으로.
- `EndMatch`를 같은 방에 두 번 불러도 한 번만 반영한다(결과에 같은 사람이 두 번 들어와도 한 번만 센다).
- `GameNet.StartMatchServer`가 만드는 방 세션은 Undo 이력과 바뀐 곳의 사람용 문장(기록·모니터용)을 만들지 않는다(`Data.RecordHistory`·`DescribeChanges`를 끔 — 서버 한 대가 방 수십 개를 돌린다). 되감기 기믹(1-4)은 이력이 아니라 지점 데이터를 쓰므로 그대로 된다.
- 점수: Elo(K=32, 여럿이면 둘씩 짝지어 나눔). 같은 등수 = 무승부.

| 창구 | 가짜(지금) | 진짜(출시, 계정 준비 뒤) |
|---|---|---|
| 인증 `INetAuthenticator` | `FakeAuthenticator`(이름을 믿음 — 입장 표는 보지 않는다) | `SteamWebAuthenticator`(Steam 인증 티켓 → Web API, 퍼블리셔 키는 서버만) |
| 매칭 `INetMatchmaker` | `LocalMatchmaker`(실력 폭 `BaseWindow` + 초당 `WidenPerSecond`, `MaxWaitSeconds`를 넘기면 아무나와) | AWS GameLift FlexMatch 어댑터(아직 — AWS 계정 준비 뒤) |
| 저장 `INetPlayerStore` | `FilePlayerStore`(폴더의 accounts/·matches/ JSON) | PostgreSQL·Redis 어댑터(아직 — 계정 준비 뒤) |

### 1-7. 사설 호스트(Steam)

- 화면은 `GameNet.CreateRoom(4)`·`InviteToRoom()`·`JoinRoom(대상)`·`LeaveRoom()`을 부른다. Steam이 준비되면 아래 Steam 로비로, 아니면 직접 접속(주소 복사)으로 간다(`GameNet.Rooms`).
- `SteamLobby.Create(4)` → Steam 로비(친구만)를 만들고 이 프로그램이 방장(`GameNet.Host` + `SteamServerTransport`). `SteamLobby.Invite()` = 친구 초대 창.
- 친구가 초대를 받거나(게임 참가) `+connect_lobby`로 게임을 켜면 그 로비에 들어가 방장에게 접속(`SteamClientTransport` — Steam Datagram Relay, NAT 통과).
- 방장이 나가면 판이 끝난다(모두 연결이 끊기고 `GameNet.Closed`). **방장 이어받기는 하지 않는다** — 한 번 만든 방은 끝까지 그 방장이 맡는다(2026-10-06 결정).
- 방장은 접속한 사람을 SteamID로 알아본다(계정 키 `steam:<SteamID>`) — 이름이 바뀌어도 다시 접속하면 같은 자리.
- 매 순간 움직이는 것: `NetTransformSync`(주인이 보내고 나머지는 보간, 방장이 중계). 판정(피해·드롭·상태 효과)은 방장의 규칙이 한다 — 위치는 그 재료다. 받는 쪽(방장 포함)은 보낸 사람이 주인인 물체만 움직인다(보낸 사람 번호는 서버가 붙인다 — 남의 캐릭터를 옮길 수 없다). 방장·서버가 보낸 것(보낸 사람 0)은 모두 받는다.

### 1-8. 전송

| 전송 | 쓰는 곳 | 비고 |
|---|---|---|
| `MemoryNetwork` | 시험 모드·테스트 | `Latency`·`Jitter`(믿는 메시지는 차례가 안 바뀜)·`Loss`(Stream만)·`Manual`(시간을 손으로) |
| `WebSocketServerTransport`·`WebSocketClientTransport` | 테스트용 멀티·매칭큐(로컬)·사설 호스트 시험 | TCP 위 직접 구현 — 관리자 권한 없이 포트 하나(방화벽 허용만) |
| `SteamServerTransport`·`SteamClientTransport` | 사설 호스트 | Steam Datagram Relay(선택 기능 Steam) |

인터넷으로 닿게 하는 법(직접 접속): 가상 랜 도구(Tailscale 등, 권장 — 공유기 설정 없음) · 공유기 포트 포워딩 · 작은 서버 임대.

### 1-9. 서버 빌드(매칭큐·화면 없는 서버)

서버는 **게임과 같은 Unity 프로젝트의 서버 빌드**다(2026-10-06 결정 — 게임 규칙 코드·DB를 그대로 쓴다. 따로 만드는 .NET 서버 프로그램은 없다).

- 만들기: Build Profiles에서 플랫폼 **Dedicated Server**(Linux 또는 Windows)로 빌드한다. 화면을 그리지 않는 빌드라 가볍다.
- 켜기: `게임.x86_64 -batchmode -nographics -momoi-matchserver 7777 -momoi-players 2`(매칭큐) 또는 `-momoi-server 7777`(방 하나). 서버는 초당 30프레임으로 돈다.
- 클라우드(AWS 등)에는 이 Linux 서버 빌드를 컨테이너로 올린다. 서버 한 대가 여러 매치(방)를 맡는다 — 방마다 세션 하나.
- 클라이언트와 서버는 **같은 게임 버전**으로 빌드한다(`Application.version`이 다르면 입장을 거절한다).
- 서버 코드(`NetServer`·`NetHub`)는 엔진 없는 코어라, 테스트(Unity 없이)에서도 같은 코드가 돈다.

### 1-10. 끊김 · 다시 접속

- 응답 확인: 클라이언트는 2초마다 핑을 보내고 서버는 답한다. 서버는 `NetServerOptions.TimeoutSeconds`(15초) 동안 아무것도 오지 않은 연결을 끊고(매칭큐 로비도 같다), 클라이언트도 `NetClient.TimeoutSeconds`(15초) 동안 서버에서 아무것도 오지 않으면 끊긴 것으로 본다.
- 끊긴 이유: `NetClient.ServerClosed`가 참이면 서버가 거절했거나 끊었다(다시 접속해도 소용없다). 거짓이면 네트워크 문제다. 방장이 방을 닫으면(나가기·게임 끄기) 들어온 사람에게 "방이 닫혔습니다"를 보내므로 다시 접속하지 않는다(`NetServerOptions.ByeOnClose`). 방장 프로그램이 갑자기 죽으면 다시 접속을 시도하다 끝난다.
- 다시 접속(`GameNet.AutoReconnect`, 기본 켬): 네트워크 문제로 끊겼고 한 번은 들어갔었으면 1·2·4·8·15·15초 뒤에 같은 곳으로 다시 접속한다(`ReconnectDelays`). 같은 계정이면 같은 자리(번호·데이터)로 잇고, 떨어져 있던 사이 바뀐 것은 처음 모습으로 다시 받는다. 다 실패하면 `GameNet.Closed`.
  - 같은 계정의 옛 연결이 서버에 남아 있고 3초 안에 무언가 왔으면 새 연결은 끊기만 한다("같은 계정이 아직 연결되어 있습니다 — 잠시 뒤 다시 접속합니다." — 거절이 아니라 다시 시도한다). 옛 연결이 3초 이상 조용하면(네트워크는 끊겼는데 서버가 아직 모름, `TakeoverSilence`) 새 연결이 같은 자리를 이어받고 옛 연결을 끊는다(옛 연결은 `PlayerLeft`로 알린 뒤 새 연결이 `PlayerJoined` — 들어옴·나감이 짝을 이룬다). 서버 중개(`NetServer`)와 매칭큐 로비(`NetHub`)가 같다. 방장 자신의 계정으로 들어오면 거절한다.
  - 알림: `GameNet.Reconnecting(몇 번째)`·`Reconnected`. 화면에는 알림 글이 뜬다. `Joined`는 처음 한 번만 온다.
  - 다시 접속하려면 전송을 만드는 함수로 들어간다(`GameNet.Join(() => 전송, 이름)`). `JoinDirect`·`FindMatch`·Steam 로비·시험 모드는 이미 그렇다. 전송 하나만 넘긴 `Join(전송, 이름)`은 다시 접속하지 않는다.
  - 매칭큐: 다시 접속하면 로비를 거치지 않고 그 매치의 같은 자리로 들어간다(1-6).

### 1-11. 리플레이

- 녹화: 받은 메시지(형식 번호표·묶음)를 시각과 함께 쌓는다. 계산을 다시 하지 않으므로 어느 기기에서 틀어도 같다.
  - 혼자·방장·화면 없는 서버: 모두 보는 눈(주인만 보는 키·이벤트 포함, 서버만 아는 것 제외). 시험 모드는 이 화면의 눈.
  - 클라이언트: 내가 본 것만. 들어갈 때부터 녹화한다(들어간 뒤 켜면 다음 입장부터). 다시 접속해도 한 녹화로 이어진다.
  - 매칭큐 서버의 방: `GameNet.RoomStarting`에서 `room.Server.AddRecorder()`.
- Unity: `GameNet.RecordReplays = true` → 녹화가 끝나면(나가기·세션 다시 시작·끄기) `GameNet.LastReplay`. 파일: `GameNet.SaveReplay()`(`<persistentDataPath>/MomoiProject/Replays/<시각>.gbreplay`). 틀기: `GameNet.PlayReplay(리플레이 또는 경로)` → 조작은 `GameNet.Replay`(`Playing`·`Speed`·`Seek(초)`·`Step()`), 끝내기 `StopReplay()`.
- 틀 때 이 화면의 세션은 '보는 쪽'이 된다 — 화면 코드는 그대로 돌고, 연출 이벤트(`SubscribeAfterAction`)도 온다. 행동은 하지 않는다. 뒤로 찾아가면 처음부터 다시 적용한다(이벤트 없이 데이터만).
- 게임 버전이 다르면 형식이 어긋날 수 있다(형식 번호표로 이름이 같은 형식은 읽는다).

### 1-12. 바꿔 끼우는 곳(실제 서비스)

실제 서비스와 닿는 곳만 인터페이스로 떼어 두고, 지금은 사설(가짜)로 대신한다. 계정(Steamworks 앱·AWS)이 준비되면 **그 자리만** 바꾸고 나머지 코드는 그대로 간다.

| 자리 | 지금(사설) | 실제 | 바꾸는 법 |
|---|---|---|---|
| 입장 표 `GameNet.TokenSource` | 없음(이름 = 계정) | Steam 웹 API용 인증 티켓(`SteamTickets.Request`) | Steam 모듈이 Steam이 준비되면 스스로 넣는다 |
| 방 창구 `GameNet.Rooms`(`INetRoomProvider`) | `DirectRoomProvider`(직접 접속 · 주소 복사) | `SteamRoomProvider`(로비 · 릴레이 · 친구 초대) | Steam 모듈이 스스로 바꾼다 |
| 인증 확인 `INetAuthenticator` | `FakeAuthenticator` | `SteamWebAuthenticator(퍼블리셔 키, App ID)` | 서버 빌드의 `StartMatchServer(auth: …)` |
| 매칭 `INetMatchmaker` | `LocalMatchmaker` | AWS GameLift FlexMatch 어댑터(아직) | `StartMatchServer(matchmaker: …)` |
| 저장 `INetPlayerStore` | `FilePlayerStore` | PostgreSQL·Redis 어댑터(아직) | `StartMatchServer(store: …)` |
| 전송 | `WebSocket` · `Memory` | `Steam` 릴레이(사설 호스트) · 클라우드의 같은 `WebSocket`(매칭큐) | 이미 있다 |

- 부분 유료화(결제·상품)는 아직 없다 — 팔 상품이 정해진 프로젝트가 생기면 이 표에 한 줄(결제 확인 자리)로 더한다.

## 2. 에디터에서 쓰는 법

- **테스트 씬 › 컴포넌트 '테스트 씬' › 멀티플레이**: 플레이할 때 멀티로 시작(혼자 · 시험 모드 · 방 생성 · 방 참가), 시험 모드 지연·흔들림·화면 없는 클라이언트 수, 포트·주소·이름.
- **테스트 패널(F1) › 멀티**: 시험 모드 · 방 생성 · 방 참가 · 매칭큐 서버 · 매칭 찾기. 들어가 있으면 내 번호·명단·핑, 시험 모드 지연 조절, 로비(점수·줄 서기·수락·거절), 끊김 시험(네트워크가 끊긴 것처럼 끊어 다시 접속을 본다), 나가기.
- **테스트 패널(F1) › 리플레이**: 녹화 켜기 · 저장 · 틀기(지금·지난 녹화, 저장한 파일 5개). 트는 중에는 위치 막대 · 재생·멈춤 · 한 칸 · 배속 · 그만 보기.
  - 클라이언트일 때 Undo·새 판·테스트 상황은 판을 가진 쪽(서버)에서 한다(시험 모드는 이 프로그램 안의 서버, 남의 서버면 개발 명령으로).
- **모니터 머리**: 멀티(시험 모드·방장·매칭큐 서버)이면 세션을 고른다 — 서버 · 이 화면(클라이언트) · 화면 없는 클라이언트 · 방마다.
- 컴포넌트 **실시간 위치 맞추기**(`NetTransformSync`): 이름(entityId) · 주인(플레이어 번호) · 초당 보내는 횟수 · 늦게 그리기 · 회전.

## 3. 코드로 쓰는 법

### 3-1. Unity 창구(`GameNet`)

```csharp
GameNet.StartTestMode(latency: 0.1);                        // 시험 모드(한 프로그램)
GameNet.HostDirect(7777, "방장");                           // 직접 접속으로 방 생성(이 화면 = 방장)
GameNet.JoinDirect("100.64.1.2:7777", "철수");              // 직접 접속으로 방 참가
GameNet.StartDedicated(7777);                               // 화면 없는 서버(방장 없음)
GameNet.StartMatchServer(7777, playersPerMatch: 2);         // 매칭큐 서버(가짜 인증·로컬 매칭·파일 저장)
GameNet.FindMatch("100.64.1.2:7777", "철수");               // 매칭큐 로비 → GameNet.Client.Queue() → Client.AnswerMatch(true)
GameNet.Leave();                                             // 혼자 하기로

GameNet.CreateRoom(4);  GameNet.InviteToRoom();              // 사설 호스트 방(직접 접속 또는 Steam — GameNet.Rooms)
GameNet.JoinRoom("100.64.1.2:7777");                       // Steam이면 로비 번호(보통은 친구 초대로 들어온다)
GameNet.Reconnecting += n => ShowSpinner();                  // 다시 접속(AutoReconnect)
GameNet.RecordReplays = true;                                // 리플레이 녹화 → LastReplay · SaveReplay()
GameNet.PlayReplay(GameNet.LastReplay);                      // 틀기 → GameNet.Replay.Seek(30) · StopReplay()

GameNet.ServerStarted += server => server.Validate = (player, action) => IsMyTurn(server.Session, player.Id, action) ? null : "내 차례가 아닙니다.";
GameNet.RoomStarting += room => SetupPlayers(room.Session, room.Server.Players);
GameNet.RoomReady += room => room.Session.Do(new StartFirstTurn());
GameNet.Hub.EndMatch(room, new[] { new NetMatchResult { Player = 2, Place = 1 }, new NetMatchResult { Player = 1, Place = 2 } });
```

명령줄(빌드 여러 개를 띄워 시험·서버 빌드): `-momoi-server [포트]` · `-momoi-matchserver [포트] -momoi-players 인원` · `-momoi-host [포트] -momoi-name 이름` · `-momoi-join 주소 -momoi-name 이름`. 서버 빌드는 `-batchmode -nographics`와 함께.

### 3-2. 코어(엔진 없음 — 어떤 서버 프로그램에서도 같다)

```csharp
var server = new NetServer(session, new WebSocketServerTransport(7777), new NetServerOptions { DriveClock = true, MaxPlayers = 4 });
server.Start();
// 매 틱: server.Update(deltaTime);

var client = new NetClient(new GameSession(), new WebSocketClientTransport("127.0.0.1:7777"), "철수");
client.Connect();
// 매 프레임: client.Update(deltaTime);

var hub = new NetHub(new WebSocketServerTransport(7777), room => NewSession(), new FakeAuthenticator(),
                     new LocalMatchmaker { PlayersPerMatch = 2 }, new FilePlayerStore(folder));
hub.Start();
hub.ResumeOpenMatches();
// 매 틱: hub.Update(deltaTime);
```

| 멤버 | 하는 일 |
|---|---|
| `NetServer.Start` · `Update(dt)` · `Receive` · `Flush` | 열기 · 받기+시간+보내기 · 받기만 · 보내기만 |
| `NetServer.AddLocalPlayer(이름)` · `Reserve(계정, 이름)` · `FindPlayer` · `FindAccount` · `Kick` · `Players` | 방장 · 자리 예약 · 찾기 · 강퇴 |
| `NetServer.Admit` · `Validate` · `PlayerJoined` · `PlayerLeft` · `StreamReceived` · `SendStream` | 입장 확인 · 행동 검사 · 알림 · 실시간 바이트 |
| `NetServer.AddRecorder(리플레이, 눈)` · `RemoveRecorder` · `NetServer.StartLocalRecording(세션, out 리플레이)` | 녹화(서버 쪽 · 혼자 하는 판) |
| `NetServerOptions` | `MaxPlayers` · `GameVersion` · `DriveClock` · `TimeSyncInterval` · `AllowDeveloperCommands` · `RelayStreams` · `TimeoutSeconds` · `ByeOnClose` · `MaxMessagesPerSecond` |
| `NetClient.Connect` · `Update` · `Disconnect` · `State` · `PlayerId` · `Players` · `RoundTrip` · `PendingActions` | 접속 · 상태 |
| `NetClient.Joined` · `Closed` · `ActionFailed` · `PlayersChanged` · `RunDeveloperCommand` · `SendStream` · `StreamReceived` | 알림 · 서버 개발 명령 · 실시간 바이트 |
| `NetClient.Token` · `TimeoutSeconds` · `ServerClosed` · `Reached` · `ClearOnJoin` · `Drop()` | 입장 표 · 끊김 판단 · 다시 접속 |
| `NetClient.StartRecording(이어 쌓을 녹화)` · `StopRecording` · `Recording` | 녹화(내가 본 것) |
| `NetReplay.Save` · `Load` · `ToBytes` · `FromBytes` · `Duration` · `Source` / `NetReplayPlayer(세션, 리플레이)` · `Update` · `Seek` · `Step` · `Speed` · `Playing` | 리플레이 |
| `IOwnedEvent` · `[GameEvent(ServerOnly = true)]` | 주인만 받는 이벤트 · 서버만 아는 이벤트 |
| `NetClient.Queue(종류)` · `CancelQueue` · `AnswerMatch(수락)` · `Account` · `Match` · `LobbyChanged` · `MatchFound` · `MatchCancelled` · `MatchEnded` | 매칭큐 |
| `NetHub.RoomStarting` · `RoomReady` · `RoomEnded` · `EndMatch(방, 결과)` · `ResumeOpenMatches` · `Rooms` · `Log` · `Options` | 매칭큐 서버 |
| `NetHubOptions` | `AcceptSeconds` · `GameVersion` · `AllowDeveloperCommands` · `SaveMatchEachAction` · `MatchSaveSeconds` · `ReadySeconds` · `TimeoutSeconds` |
| `session.Data.SetVisibility(키, 범위, 주인)` · `VisibilityOf` · `IsVisibleTo` | 공개 범위 |
| `session.Role` · `IsClient` · `LocalPlayer` · `session.Actions.CurrentActor` | 역할·내 번호 · 지금 도는 바깥 행동의 `actorId`(행동 밖·시스템 행동이면 0) |
| `Game.MarkRewind` · `Rewind` · `RewindKey` · `RewindEntity` · `RewindMarks` | 되감기 |
| `[SystemAction]` | 클라이언트가 보낼 수 없는 시스템 행동 |

## 4. 상황별 예제

### 4-1. 턴제 대전(매칭큐) — 차례 검사·손패 숨기기·결과

```csharp
GameNet.RoomStarting += room =>
{
    var data = room.Session.Data;
    foreach (var player in room.Server.Players)
    {
        data.Define("hand:" + player.Id, DataZone.Session, new Hand(), $"{player.Name}의 손패");
        data.SetVisibility("hand:" + player.Id, DataVisibility.Owner, player.Id);   // 남의 손패는 오지 않는다
    }
    room.Server.Validate = (player, action) => room.Session.Game.Data().Run.turnPlayer == player.Id ? null : "내 차례가 아닙니다.";
};
GameNet.RoomReady += room => room.Session.Do(new StartMatch());
// 규칙이 승패를 정하면(서버의 행동 안이 아니라 행동 뒤에 — 예: 이벤트를 SubscribeAfterAction으로 받아):
room.Session.Events.SubscribeAfterAction<MatchDecided>(e => GameNet.Hub.EndMatch(room, e.results));
```

화면(클라이언트)은 싱글과 같다: 내 손패 = `Game.Data().Get<Hand>("hand:" + Game.Session.LocalPlayer)`, 카드 내기 = `GameScreens.Do(() => new PlayCard { card = c })`.

### 4-2. 친구 협동(사설 호스트) — 내 캐릭터는 각자, 판정은 방장

```csharp
// 메뉴: 방 만들기 / 초대(Steam이 준비되면 Steam 로비, 아니면 직접 접속)
GameNet.CreateRoom(4);  GameNet.InviteToRoom();
// 캐릭터를 만들 때(모든 프로그램에서 같은 이름으로):
var sync = hero.AddComponent<NetTransformSync>();
sync.entityId = "hero:" + playerId;
sync.owner = playerId;
// 맞혔다는 판정은 방장이: 클라이언트는 '판정 보고 행동'을 보내고 방장의 규칙이 확인해 피해를 준다.
GameScreens.Do(() => new ReportHit { target = enemy, skill = "fireball" });
```

### 4-3. 혼자 시험(시험 모드)

테스트 씬 › 멀티플레이 = 시험 모드, 지연 0.15. 플레이하면 이 화면은 클라이언트다. 행동을 하고 0.15초 뒤에 결과가 오는지, 기다리는 동안 화면이 이상하지 않은지 본다. 모니터 머리에서 '서버'를 고르면 서버 쪽 데이터·타임라인이 보인다.

## 5. 주의할 점 · 자주 하는 실수

- **화면 코드에서 플레이어 데이터를 직접 바꾸지 않는다**(싱글에서도 같은 규칙). 클라이언트에서 바꾸면 서버와 어긋난다.
- **`Do` 직후 결과를 읽지 않는다.** 멀티에서는 결과가 나중에 온다 — 바뀐 곳 알림(`Data.Changed`·화면 연결)이나 `SubscribeAfterAction`으로 그린다. 시험 모드의 지연으로 미리 잡는다.
- **연출은 `SubscribeAfterAction`으로 받는다.** `Subscribe`로 받는 연출은 클라이언트에서 불리지 않는다.
- **실패는 예외가 아니라 알림으로.** 클라이언트의 거절은 `ActionFailed`로 온다(`GameScreens.Do`는 싱글 실패와 같은 알림을 띄운다).
- **이벤트는 `[GameEvent]` 클래스로.** 구조체 이벤트는 보낼 수 없다(서버 기록에 경고).
- **행동·이벤트의 필드는 생성 코드가 쓸 수 있는 것만.** 중첩 컬렉션·`KeyValuePair`·`HashSet`·readonly 필드·자동 속성·`object` 칸은 쓰지 않는다 — 행동은 보내기 전에 실패하고, 이벤트는 빠진다(1-1). 보내지 않을 값이면 `[NonSerialized]`.
- **주고받는 클래스에 `#if UNITY_EDITOR` 필드를 두지 않는다**(에디터와 빌드의 필드 차례가 달라진다).
- **숨길 정보는 플레이어마다 키를 나누고 Owner로.** 키 하나 안의 일부만 숨길 수는 없다. 숨길 이벤트(카드를 뽑음)는 `IOwnedEvent`로.
- 매칭큐 서버의 계정 값(재화·점수)은 서버 저장소에만 있다. 클라이언트가 보낸 값을 믿지 않는다 — 바꾸는 것은 서버의 규칙(행동)이다.
- 방장이 서버 세션을 그대로 쓰므로 방장 화면은 모든 데이터를 본다(친구 방 — 숨김이 필요 없는 게임에 쓴다).
- 직접 접속이 안 될 때: 주소·포트(기본 7777)·Windows 방화벽 허용, 같은 가상 랜에 들어와 있는지, 게임 버전이 같은지.
