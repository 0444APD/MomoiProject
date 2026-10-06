using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GBible
{
    // 테스트 씬을 플레이할 때의 멀티플레이.
    public enum TestNetMode
    {
        // 혼자 하기.
        [InspectorName("혼자")] Off,
        // 시험 모드: 한 프로그램 안에 서버(화면 없음) + 이 화면은 그 클라이언트(메모리 전송, 일부러 지연).
        [InspectorName("시험 모드")] TestMode,
        // 방 생성: 직접 접속으로 방을 만든다(이 화면 = 방장).
        [InspectorName("방 생성")] Host,
        // 방 참가: 직접 접속으로 들어간다.
        [InspectorName("방 참가")] Join,
    }

    // 테스트 씬: 프로젝트마다 AI가 만드는 씬(Assets/Scenes/Test.unity)에 이 컴포넌트를 하나 둔다.
    //   플레이하면 ① 'test' 슬롯으로 옮겨(개발자의 실제 저장을 건드리지 않음) ② 시작 테스트 상황을 불러오고(없으면 새 판)
    //   ③ 게임 화면 위에 테스트 패널을 띄운다: 테스트 명령(DB › 개발 명령·코드 등록) 버튼 · 상황 저장·불러오기 · 시간 · Undo.
    // 에디터와 개발 빌드에서만 돈다(출시 빌드에서는 아무것도 하지 않는다). 다른 컴포넌트보다 먼저 돈다.
    [DefaultExecutionOrder(-10000)]
    [AddComponentMenu("MomoiProject/테스트 씬")]
    public sealed class TestScene : MonoBehaviour
    {
        [Tooltip("시작할 테스트 상황(Assets/MomoiProjectData/Tests). 비우면 새 판")] public string scenario = "";
        [Tooltip("글자 시드. 비우면 상황의 시드(상황이 없으면 무작위)")] public string seed = "";
        [Tooltip("켜면 'test' 슬롯에서 돈다 — 개발자의 실제 저장(기본 슬롯)을 건드리지 않는다")] public bool separateSlot = true;
        [Tooltip("시작할 때의 게임 속도"), Range(0f, 8f)] public float speed = 1f;
        [Tooltip("시작할 때 테스트 패널을 펼친다")] public bool showPanel = true;
        [Tooltip("테스트 패널을 펼치고 접는 키")] public KeyCode panelKey = KeyCode.F1;

        [Header("멀티플레이")]
        [Tooltip("플레이할 때 멀티로 시작: 시험 모드(한 프로그램) · 방 생성 · 방 참가(직접 접속)")] public TestNetMode multiplayer = TestNetMode.Off;
        [Tooltip("시험 모드: 한쪽 방향 지연(초)"), Range(0f, 1f)] public float latency = 0.08f;
        [Tooltip("시험 모드: 지연 흔들림(±초)"), Range(0f, 0.5f)] public float jitter = 0.02f;
        [Tooltip("시험 모드: 화면 없는 클라이언트 수(명단·공개 범위 시험)"), Range(0, 7)] public int extraClients;
        [Tooltip("방 생성: 열 포트")] public int port = GameNet.DefaultPort;
        [Tooltip("방 참가: 주소(IP:포트)")] public string address = "127.0.0.1:7777";
        [Tooltip("멀티에서 보일 이름(비우면 컴퓨터 사용자 이름)")] public string playerName = "";

        public const string Slot = "test";
        const int WindowId = 0x6D6F6D6F;   // IMGUI 창 번호(다른 창과 겹치지 않게 고정)

        static bool Allowed => Application.isEditor || Debug.isDebugBuild;

        bool _open;
        Vector2 _scroll;
        string _saveName = "";
        string _message = "";
        Rect _window = new Rect(12, 12, 340, 560);

        void Awake()
        {
            if (!Allowed)
            {
                enabled = false;
                return;
            }
            _open = showPanel;
            if (separateSlot) GBibleRuntime.UseSlot(Slot, remember: false);
            var session = GBibleRuntime.Session;
            if (session == null) return;
            ulong? fixedSeed = string.IsNullOrWhiteSpace(seed) ? (ulong?)null : RandomStreams.SeedOf(seed);
            if (!string.IsNullOrWhiteSpace(scenario))
            {
                if (!TestScenarios.Load(session, scenario, fixedSeed)) Debug.LogWarning($"[MomoiProject] 테스트 씬: 테스트 상황 '{scenario}'를 불러오지 못했습니다(콘솔·모니터 저널).");
            }
            else GBibleRuntime.NewRun(session, fixedSeed);
            GameTime.SetSpeed(speed);
            StartNet(fixedSeed);
        }

        string NameOrDefault() => string.IsNullOrWhiteSpace(playerName) ? Environment.UserName : playerName.Trim();

        void StartNet(ulong? fixedSeed)
        {
            switch (multiplayer)
            {
                case TestNetMode.TestMode:
                    var start = scenario;
                    // 서버 세션의 판: 시작 상황이 있으면 그것을, 없으면 DB의 새 판 행동.
                    GameNet.StartTestMode(latency, jitter, extraClients, NameOrDefault(),
                        string.IsNullOrWhiteSpace(start) ? (Action<GameSession>)null : server => TestScenarios.Load(server, start, fixedSeed));
                    break;
                case TestNetMode.Host:
                    GameNet.HostDirect(port, NameOrDefault());
                    break;
                case TestNetMode.Join:
                    GameNet.JoinDirect(address, NameOrDefault());
                    break;
            }
        }

        // 판을 바꾸는 쪽(Undo·새 판·테스트 상황): 서버 세션이 이 프로그램에 있으면 그것(시험 모드·방장·혼자), 남의 서버에 들어갔으면 null.
        static GameSession Authority => GameNet.Server?.Session ?? (GameNet.Client == null ? GBibleRuntime.Session : null);

        void OnGUI()
        {
            if (!Allowed || GBibleRuntime.Session == null) return;
            // 키는 IMGUI 이벤트로 받는다(입력 방식 — 예전 Input·Input System — 과 상관없이 돈다).
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == panelKey)
            {
                _open = !_open;
                Event.current.Use();
            }
            if (!_open)
            {
                if (GUI.Button(new Rect(12, 12, 90, 24), $"테스트({panelKey})")) _open = true;
                return;
            }
            _window = GUILayout.Window(WindowId, _window, DrawWindow, "테스트 패널");
        }

        void DrawWindow(int id)
        {
            var session = GBibleRuntime.Session;
            GUILayout.BeginHorizontal();
            GUILayout.Label($"슬롯 '{GBibleRuntime.Slot}' · 시각 {session.Clock.Time:0.0}초");
            if (GUILayout.Button("접기", GUILayout.Width(48))) _open = false;
            GUILayout.EndHorizontal();
            _scroll = GUILayout.BeginScrollView(_scroll);

            // 시간·Undo
            GUILayout.Label("시간");
            GUILayout.BeginHorizontal();
            foreach (var s in new[] { 0.5f, 1f, 2f, 4f })
                if (GUILayout.Toggle(Mathf.Approximately(GameTime.Speed, s), $"×{s}", "Button")) GameTime.SetSpeed(s);
            if (GUILayout.Button(GameTime.IsPaused ? "계속" : "멈춤")) GameTime.TogglePause("테스트");
            GUILayout.EndHorizontal();
            // Undo·새 판·테스트 상황은 판을 가진 쪽(서버)에서 한다.
            var authority = Authority;
            var client = GameNet.Client;
            GUILayout.BeginHorizontal();
            if (authority != null)
            {
                GUI.enabled = authority.UndoCount > 0 && !authority.Data.InTransaction;
                if (GUILayout.Button($"Undo ({authority.UndoCount})")) Run(() => authority.Undo());
                GUI.enabled = true;
                if (GUILayout.Button("새 판")) Run(() =>
                {
                    if (authority == GBibleRuntime.Session) GBibleRuntime.NewRun(authority);
                    else authority.NewRun();
                });
            }
            else if (client != null)
            {
                if (GUILayout.Button("Undo(서버)")) Run(() => client.RunDeveloperCommand("세션", "Undo (Undo)"), "서버에 보냄");
                if (GUILayout.Button("새 판(서버)")) Run(() => client.RunDeveloperCommand("저장", "새 판"), "서버에 보냄");
            }
            GUILayout.EndHorizontal();

            DrawNet(session);

            // 테스트 상황
            GUILayout.Space(6);
            GUILayout.Label("테스트 상황");
            if (authority == null) GUILayout.Label("남의 서버에 들어가 있어 저장·불러오기는 서버에서 합니다.");
            else
            {
                GUILayout.BeginHorizontal();
                _saveName = GUILayout.TextField(_saveName);
                GUI.enabled = !string.IsNullOrWhiteSpace(_saveName);
                if (GUILayout.Button("지금 저장", GUILayout.Width(80))) Run(() => TestScenarios.Save(authority, _saveName), $"저장: {_saveName}");
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                foreach (var name in TestScenarios.Names())
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(name);
                    if (GUILayout.Button("불러오기", GUILayout.Width(80))) Run(() => TestScenarios.Load(authority, name), $"불러옴: {name}");
                    GUILayout.EndHorizontal();
                }
            }

            // 테스트 명령(DB › 개발 명령 · 코드 등록)
            foreach (var group in session.Debug.All.GroupBy(c => c.Group ?? "개발"))
            {
                GUILayout.Space(6);
                GUILayout.Label(group.Key);
                foreach (var command in group)
                    if (GUILayout.Button(new GUIContent(command.Name, command.Description ?? ""))) Run(command.Execute, command.Name);
            }

            if (!string.IsNullOrEmpty(_message))
            {
                GUILayout.Space(6);
                GUILayout.Label(_message);
            }
            GUILayout.EndScrollView();
            GUI.DragWindow();
        }

        // 멀티: 시작·상태(내 번호·명단·핑)·시험 모드 지연·나가기.
        void DrawNet(GameSession session)
        {
            GUILayout.Space(6);
            GUILayout.Label("멀티");
            if (!GameNet.IsOnline)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("시험 모드")) Run(() => GameNet.StartTestMode(latency, jitter, extraClients, NameOrDefault()), "시험 모드");
                if (GUILayout.Button($"방 생성 :{port}")) Run(() => GameNet.HostDirect(port, NameOrDefault()), "방을 만들었습니다");
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                address = GUILayout.TextField(address);
                if (GUILayout.Button("방 참가", GUILayout.Width(80))) Run(() => GameNet.JoinDirect(address, NameOrDefault()), "참가하는 중…");
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button($"매칭큐 서버 :{port}")) Run(() => GameNet.StartMatchServer(port), "매칭큐 서버를 열었습니다");
                if (GUILayout.Button("매칭 찾기(주소)")) Run(() => GameNet.FindMatch(address, NameOrDefault()), "매칭큐 서버에 들어가는 중…");
                GUILayout.EndHorizontal();
                return;
            }
            if (GameNet.Hub is NetHub hub)
            {
                GUILayout.Label($"매칭큐 서버 · 로비 {hub.LobbyCount}명 · 방 {hub.Rooms.Count}개");
                GUILayout.Label("접속 주소: " + hub.Address);
                foreach (var room in hub.Rooms)
                    GUILayout.Label($"  방 {room.MatchId}{(room.Ready ? "" : " (기다리는 중)")}: {string.Join(", ", room.Server.Players.Select(p => p.Name + (p.Connected ? "" : "(나감)")))}");
                if (GUILayout.Button("서버 닫기")) Run(GameNet.Leave, "닫았습니다");
                return;
            }
            var client = GameNet.Client;
            if (client != null && client.Account != null && !client.IsJoined && client.State != NetClientState.Joining)
            {
                // 매칭큐 로비.
                GUILayout.Label($"{client.Account.Name} · 점수 {client.Account.Rating:0} · {client.Account.Wins}승 {client.Account.Losses}패 {client.Account.Draws}무");
                switch (client.State)
                {
                    case NetClientState.Lobby:
                        if (GUILayout.Button("줄 서기")) Run(() => client.Queue(), "줄을 섰습니다");
                        break;
                    case NetClientState.Queued:
                        GUILayout.Label("매칭을 기다리는 중…");
                        if (GUILayout.Button("취소")) Run(client.CancelQueue);
                        break;
                    case NetClientState.MatchFound:
                        GUILayout.Label("매치 성사: " + string.Join(", ", client.Match?.Players ?? Array.Empty<string>()));
                        GUILayout.BeginHorizontal();
                        if (GUILayout.Button("수락")) Run(() => client.AnswerMatch(true), "수락했습니다");
                        if (GUILayout.Button("거절")) Run(() => client.AnswerMatch(false), "거절했습니다");
                        GUILayout.EndHorizontal();
                        break;
                }
                if (GUILayout.Button("나가기(혼자 하기)")) Run(GameNet.Leave, "혼자 하기로 돌아왔습니다");
                return;
            }
            var role = GameNet.IsTestMode ? "시험 모드" : GameNet.IsHost ? "방장" : "클라이언트";
            var state = client == null ? "" : client.IsJoined ? $" · 핑 {client.RoundTrip * 1000:0}ms" : $" · {client.State}";
            GUILayout.Label($"{role} · 내 번호 #{session.LocalPlayer}{state}");
            if (GameNet.IsHost) GUILayout.Label("접속 주소: " + GameNet.Server.Address);
            if (GameNet.Server != null)
                foreach (var player in GameNet.Server.Players) GUILayout.Label($"  #{player.Id} {player.Name}{(player.Connected ? "" : " (나감)")}");
            else if (client != null)
                foreach (var player in client.Players) GUILayout.Label($"  #{player.Id} {player.Name}{(player.Connected ? "" : " (나감)")}");
            if (GameNet.TestNetwork is MemoryNetwork net)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"지연 {net.Latency * 1000:0}ms", GUILayout.Width(90));
                net.Latency = GUILayout.HorizontalSlider((float)net.Latency, 0f, 0.5f);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label($"흔들림 {net.Jitter * 1000:0}ms", GUILayout.Width(90));
                net.Jitter = GUILayout.HorizontalSlider((float)net.Jitter, 0f, 0.2f);
                GUILayout.EndHorizontal();
            }
            if (GUILayout.Button("나가기(혼자 하기)")) Run(GameNet.Leave, "혼자 하기로 돌아왔습니다");
        }

        void Run(Action action, string done = null)
        {
            try
            {
                action?.Invoke();
                _message = done ?? "";
            }
            catch (GameFailure failure)
            {
                _message = "플레이어용 실패: " + failure.Message;
            }
            catch (Exception e)
            {
                _message = "오류: " + e.Message;
                Debug.LogException(e);
            }
        }
    }
}
