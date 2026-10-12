# 멀티플레이에서 처음부터 지킬 것 (AI용)

명세서 0절이 '있음'인 프로젝트에서 읽는다(관례 20절 · 기능 장 40). 프로젝트 관례에서 옮겨 온 것이다(2026-10-12 — 멀티가 없는 프로젝트는 읽지 않아도 되게).

구조는 4블록 하나다(매뉴얼 40장): 화면은 행동만 보내고, 판정하는 쪽(매칭큐 = 서버, 친구 방 = 방장)이 실행해 바뀐 곳·이벤트를 돌려준다. 게임 코드는 싱글과 같게 짜되 아래를 처음부터 지킨다(나중에 고치면 데이터 구조부터 바뀐다).

- **판정은 행동 안에서만.** 화면·연출 코드는 플레이어 데이터를 바꾸지 않는다(클라이언트에서 바꾸면 어긋난다). 화면은 `Do` 직후 결과를 읽지 않고 바뀐 곳 알림·`SubscribeAfterAction`으로 그린다. 실패는 `GameScreens.Do`가 알림으로 띄운다(멀티 거절도 같다).
- **플레이어마다 칸.** 플레이어 데이터에 플레이어 번호(`actorId`·`session.LocalPlayer`와 같은 번호)를 담는다: 목록 `players[]` 또는 키 `hand:1`·`hand:2`. 남에게 숨길 것은 **플레이어별 키 + `Data.SetVisibility(키, DataVisibility.Owner, 번호)`**(키 단위로만 숨긴다). 숨길 이벤트(카드를 뽑음)는 `IOwnedEvent`(주인만 받음), 서버 규칙끼리만 쓰는 이벤트는 `[GameEvent(ServerOnly = true)]`.
- **서버 규칙은 서버가 준비될 때 건다.** 친구 방·시험 모드: `GameNet.ServerStarted += server => …`, 매칭큐: `GameNet.RoomStarting += room => …`(플레이어별 키 정의·공개 범위·`Validate`), 첫 턴은 `GameNet.RoomReady`. `Validate`는 차례·주인(남의 유닛을 움직이려 함)을 거절 글로 막는다. `actorId`는 서버가 보낸 사람 번호로 덮으므로 규칙은 `actorId`를 믿고 쓴다.
- **매칭큐 결과**: 승패를 정한 규칙이 `GameNet.Hub.EndMatch(room, NetMatchResult[])`를 부른다(다음 틱에 점수·전적 저장 후 로비로). 재화·해금 같은 계정 값은 서버 저장소(`NetAccount.values`)에 두고 클라이언트 값을 믿지 않는다.
- **실시간 협동의 움직임**: 내 캐릭터·탄환처럼 매 순간 움직이는 것은 규칙 데이터가 아니라 `NetTransformSync`(entityId를 모든 프로그램에서 같게, owner = 플레이어 번호). 맞힘·피해는 방장의 규칙이 판정한다(클라이언트는 판정 보고 행동, 방장이 확인).
- **친구 방 화면**: 방 만들기·초대·참가 버튼은 `GameNet.CreateRoom`·`InviteToRoom`·`JoinRoom`만 부른다(Steam이 준비되면 Steam 로비, 아니면 직접 접속 — 화면 코드는 같다). 방장이 나가면 판이 끝난다(이어받기 없음) — `GameNet.Closed`에서 메뉴로 돌아간다.
- **끊김**: 다시 접속은 저절로 된다(`GameNet.AutoReconnect`). 화면은 `GameNet.Reconnecting`에서 '다시 접속하는 중'을 보이고 `Reconnected`에서 거둔다. `Joined`는 처음 한 번만 온다 — 다시 들어온 뒤에도 화면은 바뀐 곳 알림으로 저절로 맞는다.
- **되감기 기믹**: 턴 시작 행동에서 `Game.MarkRewind()`, 기믹은 `Game.Rewind`·`RewindKey`·`RewindEntity`(행동 안). 멀티에서 플레이어의 일반 무르기는 없다.
- **주고받는 클래스**: `[Serializable]`(생성 코드), `#if UNITY_EDITOR` 필드를 두지 않는다. 이벤트는 `[GameEvent]` 클래스(구조체 이벤트는 보낼 수 없다).
- **시험**: 단계 1에서 테스트 씬의 멀티플레이를 '시험 모드'로 한 번 돌려 본다(지연 0.1 이상 — 화면이 결과를 기다리는지). 매칭큐는 `GameNet.StartMatchServer` + 테스트 패널의 매칭 찾기로 로컬에서 끝까지(줄 → 수락 → 방 → `EndMatch` → 로비) 확인한다. 규칙 테스트는 코어에서 `NetServer`·`NetClient`·`MemoryNetwork`(Manual)로 서버 하나·클라이언트 둘을 띄워 단언할 수 있다(`Tests/Editor/NetTests.cs`가 본보기).
