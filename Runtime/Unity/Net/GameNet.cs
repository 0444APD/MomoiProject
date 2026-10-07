using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using GBible.Data;
using UnityEngine;

namespace GBible
{
    // 멀티플레이 창구(Unity). 4블록 중 [서버 중개]·[클라이언트 중개]를 이 게임 프로그램에 붙이고 매 프레임 돌린다(GBibleHost).
    //   사설 호스트 방:             GameNet.CreateRoom(4) · JoinRoom(대상) · InviteToRoom() · LeaveRoom()  — 직접 접속이든 Steam이든 같다(Rooms)
    //   방 생성(전송을 직접 고름):   GameNet.Host(전송, "방장");          — 이 화면은 서버 세션을 그대로 쓴다
    //   방 참가:                    GameNet.Join(() => 전송, "철수");    — 이 화면의 세션이 '받는 쪽'으로 다시 시작한다(끊기면 다시 접속)
    //   시험 모드(한 프로그램):     GameNet.StartTestMode(latency: 0.1); — 서버를 이 안에 띄우고 이 화면은 클라이언트(메모리 전송·일부러 지연)
    //   리플레이:                   GameNet.RecordReplays = true → LastReplay · SaveReplay() · PlayReplay(리플레이)
    //   끝내기:                     GameNet.Leave();                     — 혼자 하기로 돌아간다
    // 화면 코드는 싱글과 같다(Game.Do·Data·SubscribeAfterAction). 내 번호는 Game.Session.LocalPlayer.
    //
    // 실제 서비스와 닿는 곳은 바꿔 끼우는 곳 둘뿐이다(지금은 사설로 대신한다 — Steam 모듈이 Steam이 준비되면 스스로 바꾼다):
    //   TokenSource  입장 표(인증 티켓)를 얻는 곳     기본: 없음(이름 = 계정)        Steam: Steam 웹 API용 인증 티켓
    //   Rooms        방 생성·참가·초대                기본: 직접 접속(주소 복사)       Steam: Steam 로비 + 릴레이(친구 초대)
    // 서버 쪽(인증 확인·매칭·저장)은 NetHub의 INetAuthenticator·INetMatchmaker·INetPlayerStore로 바꾼다.
    public static class GameNet
    {
        // 직접 접속의 기본 포트.
        public const int DefaultPort = 7777;
        // 리플레이 파일 확장자.
        public const string ReplayExtension = ".gbreplay";

        static readonly List<NetClient> Extras = new List<NetClient>();
        static MemoryNetwork _testNet;

        // 이 프로그램이 서버·방장이면(시험 모드 포함) 서버 중개.
        public static NetServer Server { get; private set; }
        // 이 프로그램의 화면이 클라이언트면(시험 모드 포함) 클라이언트 중개.
        public static NetClient Client { get; private set; }
        public static bool IsTestMode { get; private set; }
        public static bool IsOnline => Server != null || Client != null || Hub != null;
        // 방장(이 화면 = 서버 세션). 시험 모드는 아니다(시험 모드의 화면은 클라이언트).
        public static bool IsHost => Server != null && !IsTestMode;
        public static bool IsClient => Client != null;
        // 시험 모드의 메모리 네트워크(지연·흔들림·유실을 실행 중에 바꿀 수 있다).
        public static MemoryNetwork TestNetwork => _testNet;
        // 시험 모드: 화면이 없는 다른 클라이언트들.
        public static IReadOnlyList<NetClient> ExtraClients => Extras;

        // ── 바꿔 끼우는 곳 ──────────────────────────────────────
        // 입장 표(인증 티켓)를 얻는 곳: 접속할 때마다(다시 접속 포함) 부른다. 얻으면 done(표), 못 얻으면 done(null) — 표 없이 들어간다.
        // 비우면 표 없이(가짜 인증: 이름 = 계정). Steam 모듈이 Steam이 준비되면 채운다(매칭큐 서버의 SteamWebAuthenticator가 확인).
        public static Action<Action<string>> TokenSource;
        // 입장 표를 이만큼(초) 기다려도 오지 않으면 표 없이 접속한다.
        public static double TokenWaitSeconds = 10;
        // 사설 호스트의 방 창구. 기본 = 직접 접속. Steam 모듈이 Steam이 준비되면 Steam 로비로 바꾼다.
        public static INetRoomProvider Rooms = new DirectRoomProvider();

        // ── 다시 접속 ───────────────────────────────────────────
        // 연결이 네트워크 문제로 끊기면(서버가 거절하거나 끊은 것이 아니고, 한 번은 들어갔었으면) 같은 곳에 다시 접속한다.
        // 같은 계정이면 같은 자리(번호·데이터)로 잇는다. 끄면 끊기는 즉시 Closed.
        public static bool AutoReconnect = true;
        // 다시 접속 사이 기다림(초). 다 써도 안 되면 Closed.
        public static double[] ReconnectDelays = { 1, 2, 4, 8, 15, 15 };
        public static bool IsReconnecting => _link != null && _link.Attempt > 0;
        // 다시 접속을 시작할 때(몇 번째 시도) / 다시 들어갔을 때(Joined는 처음 한 번만 — 다시 들어간 것은 이것으로).
        public static event Action<int> Reconnecting;
        public static event Action Reconnected;

        // ── 리플레이 ───────────────────────────────────────────
        // 켜 두면 녹화한다: 혼자·방장·서버 = 모두 본 것(시험 모드는 이 화면의 눈), 클라이언트 = 내가 본 것(들어갈 때부터 — 들어간 뒤 켜면 다음 입장부터).
        // 녹화가 끝나면(나가기·세션 다시 시작·끄기) LastReplay에 남는다. 파일: SaveReplay.
        public static bool RecordReplays;
        // 지금 녹화 중인 것(없으면 null).
        public static NetReplay Recording => _recording;
        public static NetReplay LastReplay { get; private set; }
        // 틀고 있는 리플레이(PlayReplay). 조작: Replay.Playing · Speed · Seek(초).
        public static NetReplayPlayer Replay { get; private set; }
        public static bool IsReplaying => Replay != null;
        public static string ReplayFolder => Path.Combine(Application.persistentDataPath, "MomoiProject", "Replays");

        sealed class Link
        {
            public Func<INetClientTransport> Factory;
            public string Name;
            public string Token;
            public string Version;
            public bool Reached;
            public int Attempt;
            public double RetryAt = -1;
        }

        static Link _link;
        static NetClient _awaitingToken;
        static double _tokenDeadline;
        static NetReplay _recording;
        static NetServer _localRecorder;
        static NetServer _recordedServer;
        static GameSession _recordedSession;

        static double Now => Time.realtimeSinceStartupAsDouble;

        // 모니터가 고를 수 있는 세션들(이름, 세션).
        public static IEnumerable<(string name, GameSession session)> Sessions
        {
            get
            {
                if (Server != null && Server.Session != GBibleRuntime.Session) yield return ("서버", Server.Session);
                if (GBibleRuntime.Session != null)
                    yield return (IsReplaying ? "리플레이" : IsHost ? "방장(서버)" : Client != null ? $"클라이언트 · {Client.Name}" : "이 화면", GBibleRuntime.Session);
                foreach (var extra in Extras) yield return ($"클라이언트 · {extra.Name}", extra.Session);
                if (Hub != null)
                    foreach (var room in Hub.Rooms) yield return ("방 " + room.MatchId, room.Session);
            }
        }

        // 서버 세션이 준비됐을 때(방장으로 열었거나 시험 모드의 서버를 띄웠을 때). 서버 쪽 규칙(차례 검사 Validate 등)을 여기서 건다.
        public static event Action<NetServer> ServerStarted;
        // 이 화면의 클라이언트가 처음 모습까지 받았을 때.
        public static event Action Joined;
        // 이 화면의 클라이언트 연결이 끝났을 때(이유). 다시 접속하는 중에는 오지 않는다(다 실패하면 온다).
        public static event Action<string> Closed;
        public static event Action PlayersChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Server = null;
            Hub = null;
            RoomStarting = null;
            RoomReady = null;
            Client = null;
            IsTestMode = false;
            _testNet = null;
            Extras.Clear();
            ServerStarted = null;
            Joined = null;
            Closed = null;
            PlayersChanged = null;
            TokenSource = null;
            TokenWaitSeconds = 10;
            Rooms = new DirectRoomProvider();
            AutoReconnect = true;
            ReconnectDelays = new double[] { 1, 2, 4, 8, 15, 15 };
            Reconnecting = null;
            Reconnected = null;
            RecordReplays = false;
            LastReplay = null;
            Replay = null;
            _link = null;
            _awaitingToken = null;
            _recording = null;
            _localRecorder = null;
            _recordedServer = null;
            _recordedSession = null;
        }

        // 이 게임의 기본 서버 옵션(개발 빌드면 개발 명령 허용 · 게임 버전이 다르면 입장 거절). maxPlayers가 0이면 기본 인원.
        public static NetServerOptions ServerOptions(int maxPlayers = 0)
        {
            var options = new NetServerOptions { AllowDeveloperCommands = Debug.isDebugBuild, GameVersion = Application.version };
            if (maxPlayers > 0) options.MaxPlayers = maxPlayers;
            return options;
        }

        // 접속할 사람에게 알려 줄 주소(방을 열 때 한 번 정해 둔다 — 화면이 매 프레임 네트워크 카드를 읽지 않게).
        public static string Address { get; private set; }

        // 서버를 연다. 포트가 이미 쓰이는 중 등으로 실패하면 반쯤 열린 서버를 남기지 않고 닫은 뒤 그 예외를 던진다.
        static void StartServer()
        {
            try { Server.Start(); }
            catch (Exception)
            {
                Server.Dispose();
                Server = null;
                IsTestMode = false;
                _testNet = null;
                throw;
            }
            Address = Server.Address;
        }

        // ── 사설 호스트 방(화면은 이것만 부른다) ──────────────────
        // 방을 만든다(이 프로그램 = 방장. 방장이 나가면 판이 끝난다 — 방장 이어받기는 하지 않는다).
        public static void CreateRoom(int maxPlayers = 4, string playerName = null) => Rooms.Create(maxPlayers, playerName);
        // 방에 들어간다(직접 접속: "주소:포트" · Steam: 로비 번호 — 보통은 친구 초대를 받아 들어간다).
        public static void JoinRoom(string target, string playerName = null) => Rooms.Join(target, playerName);
        // 친구를 부른다(직접 접속: 접속 주소를 클립보드에 복사 · Steam: 친구 초대 창).
        public static void InviteToRoom() => Rooms.Invite();
        public static void LeaveRoom() => Rooms.Leave();

        // 지금 세션을 서버로 연다(방장). 이 화면의 행동은 방장 번호로 그 자리에서 실행된다.
        public static NetServer Host(INetServerTransport transport, string playerName = null, NetServerOptions options = null)
        {
            Leave();
            var session = GBibleRuntime.Session ?? GBibleRuntime.Start();
            Server = new NetServer(session, transport, options ?? ServerOptions());
            WireServer(Server);
            StartServer();
            Server.AddLocalPlayer(string.IsNullOrEmpty(playerName) ? "방장" : playerName);
            ServerStarted?.Invoke(Server);
            return Server;
        }

        // 직접 접속(WebSocket)으로 방을 연다 — 테스트용 멀티·사설 호스트 시험. 다른 사람은 Address의 주소로 들어온다.
        public static NetServer HostDirect(int port = DefaultPort, string playerName = null, int maxPlayers = 0) =>
            Host(new WebSocketServerTransport(port), playerName, ServerOptions(maxPlayers));

        // 직접 접속으로 들어간다. address: "주소:포트" 또는 "ws://주소:포트"(포트를 빼면 7777).
        public static NetClient JoinDirect(string address, string playerName) => Join(() => new WebSocketClientTransport(address), playerName);

        // 화면 없는 서버(테스트 서버·서버 빌드): 지금 세션을 서버로 연다(방장 플레이어 없음 — 모두 접속해서 들어온다).
        public static NetServer StartDedicated(int port = DefaultPort, NetServerOptions options = null)
        {
            Leave();
            var session = GBibleRuntime.Session ?? GBibleRuntime.Start();
            Server = new NetServer(session, new WebSocketServerTransport(port), options ?? ServerOptions());
            WireServer(Server);
            StartServer();
            Debug.Log($"[MomoiProject] 서버를 열었습니다 — 접속 주소: {Address}");
            ServerStarted?.Invoke(Server);
            return Server;
        }

        // 게임 버전(Application.version)이 서버와 다르면 입장이 거절된다(서버 쪽 기본 옵션도 같은 값).
        // 서버에 들어간다: 이 화면의 세션을 멈추고(프로필은 저장) '받는 쪽' 세션으로 다시 시작한다.
        // 전송을 하나만 넘기면 끊겼을 때 다시 접속하지 않는다(한 번 쓴 전송은 다시 쓰지 않는다) — 다시 접속하려면 만드는 함수를 넘긴다.
        public static NetClient Join(INetClientTransport transport, string playerName, string token = null, string gameVersion = null)
        {
            if (transport == null) throw new ArgumentNullException(nameof(transport));
            Leave();
            return JoinCore(transport, null, playerName, token, gameVersion);
        }

        // 서버에 들어간다(끊기면 connect로 새 전송을 만들어 다시 접속한다).
        public static NetClient Join(Func<INetClientTransport> connect, string playerName, string token = null, string gameVersion = null)
        {
            if (connect == null) throw new ArgumentNullException(nameof(connect));
            Leave();
            return JoinCore(connect(), connect, playerName, token, gameVersion);
        }

        // 이 프로그램이 매칭큐 서버면(로비·줄·여러 방).
        public static NetHub Hub { get; private set; }

        // 매칭큐 서버의 방이 만들어졌을 때(세션·자리 예약 끝): 서버 규칙(room.Server.Validate)·플레이어별 키·공개 범위를 건다.
        // 방마다 녹화하려면 여기서 room.Server.AddRecorder().
        public static event Action<NetRoom> RoomStarting;
        // 방에 정원이 모두 들어왔을 때: 첫 턴 시작 등.
        public static event Action<NetRoom> RoomReady;

        // 매칭큐 서버를 연다(화면 없는 서버·로컬 시험). 방마다 DB로 세션을 만들고 '새 판 행동'을 돌린다.
        // 인증·매칭·저장을 비우면 가짜(이름으로 입장 · 로컬 대기열 · 이 컴퓨터의 파일 <persistentDataPath>/MomoiProject/server).
        public static NetHub StartMatchServer(int port = DefaultPort, int playersPerMatch = 2, INetAuthenticator auth = null, INetMatchmaker matchmaker = null, INetPlayerStore store = null, NetHubOptions options = null)
        {
            Leave();
            if (store == null)
            {
                var files = new FilePlayerStore(Path.Combine(Application.persistentDataPath, "MomoiProject", "server"));
                files.Log += message => Debug.LogWarning("[MomoiProject] " + message);
                store = files;
            }
            matchmaker ??= new LocalMatchmaker { PlayersPerMatch = Math.Max(1, playersPerMatch) };
            Hub = new NetHub(new WebSocketServerTransport(port), room =>
            {
                var session = GBibleRuntime.CreateSession();
                // 방 세션은 서버만 판정한다: 플레이어 무르기가 없으니 Undo 이력을 두지 않고, 바뀐 곳의 사람용 문장(기록·모니터용)도 만들지 않는다
                // (방마다 메모리·CPU — 방 수십 개를 한 서버가 돌린다). 되감기 기믹(Game.Rewind)은 이력이 아니라 지점 데이터를 쓰므로 그대로 된다.
                session.Data.RecordHistory = false;
                session.Data.DescribeChanges = false;
                GBibleRuntime.RunNewRunAction(session);
                return session;
            }, auth, matchmaker, store, options ?? new NetHubOptions { AllowDeveloperCommands = Debug.isDebugBuild, GameVersion = Application.version });
            Hub.Log += Debug.Log;
            Hub.RoomStarting += room => RoomStarting?.Invoke(room);
            Hub.RoomReady += room => RoomReady?.Invoke(room);
            try { Hub.Start(); }
            catch (Exception)
            {
                // 포트가 이미 쓰이는 중 등: 반쯤 열린 서버를 남기지 않는다.
                Hub.Dispose();
                Hub = null;
                throw;
            }
            Address = Hub.Address;
            var resumed = Hub.ResumeOpenMatches();
            if (resumed > 0) Debug.Log($"[MomoiProject] 진행 중이던 매치 {resumed}개를 다시 열었습니다.");
            return Hub;
        }

        // 매칭큐 서버에 들어간다(로비). 이어서 GameNet.Client.Queue()로 줄을 선다 — 성사되면 Client.AnswerMatch(true).
        // token을 비우면 TokenSource(Steam이면 인증 티켓)로 얻는다.
        public static NetClient FindMatch(string address, string playerName, string token = null) => Join(() => new WebSocketClientTransport(address), playerName, token);

        // 시험 모드: 서버를 이 프로그램 안에 띄우고(화면 없음, DB로 만든 세션 + 새 판 행동), 이 화면은 그 서버의 클라이언트가 된다.
        // 메시지는 메모리로 오가며 latency(초)·jitter(±초)만큼 늦는다 — 싱글에서 숨는 화면 문제(Do 직후 결과 읽기 등)가 드러난다.
        // prepareServer: 서버 세션의 판을 직접 준비할 때(테스트 상황 불러오기 등). 비우면 DB의 새 판 행동.
        public static void StartTestMode(double latency = 0.08, double jitter = 0.02, int extraClients = 0, string playerName = "나", Action<GameSession> prepareServer = null)
        {
            Leave();
            var net = _testNet = new MemoryNetwork { Latency = Math.Max(0, latency), Jitter = Math.Max(0, jitter) };
            IsTestMode = true;
            var serverSession = GBibleRuntime.CreateSession();
            Server = new NetServer(serverSession, net.Server(), new NetServerOptions { AllowDeveloperCommands = true, GameVersion = Application.version });
            WireServer(Server);
            StartServer();
            if (prepareServer != null) prepareServer(serverSession);
            else GBibleRuntime.RunNewRunAction(serverSession);
            ServerStarted?.Invoke(Server);
            // 화면 클라이언트가 먼저 들어간다(내 번호 #1). 화면 없는 클라이언트는 그 뒤.
            JoinCore(net.Client(), net.Client, playerName, null, null);
            for (var i = 0; i < Math.Max(0, extraClients); i++)
            {
                var extra = new NetClient(GBibleRuntime.CreateSession(), net.Client(), $"봇{i + 1}");
                extra.Connect();
                Extras.Add(extra);
            }
        }

        // 다시 접속 시험: 이 화면의 연결을 네트워크가 끊긴 것처럼 끊는다(AutoReconnect면 곧 다시 접속한다).
        public static void SimulateDrop() => Client?.Drop();

        // 멀티(또는 리플레이)를 끝내고 혼자 하기로 돌아간다(클라이언트·리플레이였으면 새 혼자 하기 세션으로 다시 시작).
        public static void Leave()
        {
            var restart = Client != null || Replay != null;
            Close();
            if (restart) GBibleRuntime.RestartLocal();
        }

        // 게임을 끌 때: 나간다고 알리고 닫는다(세션을 다시 만들지 않는다).
        internal static void Shutdown() => Close();

        static void Close()
        {
            if (_recording != null) FinishRecording();
            _link = null;
            _awaitingToken = null;
            Replay = null;
            if (Client != null)
            {
                Client.Dispose();
                Client = null;
            }
            foreach (var extra in Extras) extra.Dispose();
            Extras.Clear();
            if (Server != null)
            {
                Server.Dispose();
                Server = null;
            }
            if (Hub != null)
            {
                Hub.Dispose();
                Hub = null;
            }
            IsTestMode = false;
            _testNet = null;
            Address = null;
        }

        // ── 클라이언트 · 입장 표 · 다시 접속 ────────────────────

        static NetClient JoinCore(INetClientTransport first, Func<INetClientTransport> factory, string playerName, string token, string gameVersion)
        {
            var link = _link = new Link
            {
                Factory = factory,
                Name = string.IsNullOrEmpty(playerName) ? "플레이어" : playerName,
                Token = token,
                Version = gameVersion ?? Application.version,
            };
            NetClient created = null;
            GBibleRuntime.RestartAsClient(session => created = CreateClient(session, first, link, resume: false));
            Client = created;
            // Started에서 등록된 개발 명령도 서버에서 돌게 한 번 더 맞춘다(위의 맞추기는 Started 전에 돈다).
            if (created != null) RemapDeveloperCommands(created.Session);
            if (created != null) Connect(created, link);
            return created;
        }

        static NetClient CreateClient(GameSession session, INetClientTransport transport, Link link, bool resume)
        {
            var client = new NetClient(session, transport, link.Name, null, link.Version) { ClearOnJoin = resume };
            // 시험 모드는 서버 쪽 녹화가 맡는다(같은 녹화에 두 번 쌓이지 않게).
            if (RecordReplays && !IsTestMode) client.StartRecording(resume ? _recording : null);
            if (!resume) RemapDeveloperCommands(session);
            client.ActionFailed += failure => GameScreens.ShowFailure(failure.Message);
            client.Joined += () =>
            {
                if (client == Client && !resume) Joined?.Invoke();
            };
            client.PlayersChanged += () =>
            {
                if (client == Client) PlayersChanged?.Invoke();
            };
            client.Closed += reason => OnClientClosed(client, reason);
            return client;
        }

        // 입장 표를 얻어(TokenSource) 접속한다. 표를 직접 넘겼으면 그것으로.
        static void Connect(NetClient client, Link link)
        {
            if (link.Token != null || TokenSource == null)
            {
                client.Token = link.Token ?? "";
                client.Connect();
                return;
            }
            _awaitingToken = client;
            _tokenDeadline = Now + Math.Max(0, TokenWaitSeconds);
            try
            {
                TokenSource(token =>
                {
                    if (_awaitingToken != client) return;
                    _awaitingToken = null;
                    client.Token = token ?? "";
                    client.Connect();
                });
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (_awaitingToken == client)
                {
                    _awaitingToken = null;
                    client.Connect();
                }
            }
        }

        static void OnClientClosed(NetClient client, string reason)
        {
            if (client != Client) return;
            var link = _link;
            if (link != null)
            {
                link.Reached |= client.Reached;
                var delays = ReconnectDelays ?? Array.Empty<double>();
                if (link.Factory != null && AutoReconnect && link.Reached && !client.ServerClosed && link.Attempt < delays.Length)
                {
                    link.RetryAt = Now + Math.Max(0, delays[link.Attempt]);
                    link.Attempt++;
                    GameScreens.Toast($"연결이 끊겼습니다 — 다시 접속하는 중({link.Attempt}/{delays.Length})…", 3f);
                    Reconnecting?.Invoke(link.Attempt);
                    return;
                }
            }
            GameScreens.Toast("서버 연결이 끝났습니다: " + reason, 4f);
            Closed?.Invoke(reason);
        }

        // 같은 세션에 새 연결을 붙인다(판 데이터는 서버가 처음 모습을 다시 보내 맞춘다).
        static void Retry()
        {
            var link = _link;
            link.RetryAt = -1;
            var session = Client?.Session ?? GBibleRuntime.Session;
            if (session == null) return;
            INetClientTransport transport;
            try { transport = link.Factory(); }
            catch (Exception e)
            {
                Debug.LogException(e);
                OnClientClosed(Client, "다시 접속하지 못했습니다: " + e.Message);
                return;
            }
            Client?.Dispose();
            var fresh = CreateClient(session, transport, link, resume: true);
            Client = fresh;
            Connect(fresh, link);
        }

        static void UpdateLink()
        {
            if (_awaitingToken != null && Now > _tokenDeadline)
            {
                var waiting = _awaitingToken;
                _awaitingToken = null;
                Debug.LogWarning("[MomoiProject] 입장 표(인증 티켓)를 받지 못해 표 없이 접속합니다.");
                waiting.Connect();
            }
            var link = _link;
            if (link == null || Client == null) return;
            // 새 연결이 실제로 닿았을 때만(기다리는 동안의 Client는 끊긴 옛 연결이라 Reached가 참이다).
            if (link.Attempt > 0 && link.RetryAt < 0 && Client.State != NetClientState.Closed && Client.Reached)
            {
                link.Attempt = 0;
                GameScreens.Toast("다시 접속했습니다.", 2.5f);
                Reconnected?.Invoke();
            }
            if (link.RetryAt >= 0 && Now >= link.RetryAt) Retry();
        }

        // ── 리플레이 ───────────────────────────────────────────

        // 리플레이를 튼다: 멀티를 끝내고 이 화면의 세션을 '보는 쪽'으로 다시 시작한다(행동은 하지 않는다). 끝내기: StopReplay(혼자 하기로).
        public static NetReplayPlayer PlayReplay(NetReplay replay)
        {
            if (replay == null) throw new ArgumentNullException(nameof(replay));
            Close();
            NetReplayPlayer created = null;
            GBibleRuntime.RestartAsClient(session => created = new NetReplayPlayer(session, replay));
            Replay = created;
            return created;
        }

        public static NetReplayPlayer PlayReplay(string path) => PlayReplay(NetReplay.Load(path));

        public static void StopReplay()
        {
            if (Replay != null) Leave();
        }

        // 리플레이를 파일로 저장한다(비우면 지금 녹화 중인 것까지, 없으면 지난 녹화). 돌려주는 값: 경로(저장할 것이 없으면 null).
        public static string SaveReplay(NetReplay replay = null, string path = null)
        {
            replay ??= _recording ?? LastReplay;
            if (replay == null || replay.Count == 0) return null;
            path ??= Path.Combine(ReplayFolder, DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ReplayExtension);
            replay.Save(path);
            return path;
        }

        // 저장된 리플레이 파일(새것부터).
        public static IEnumerable<string> ReplayFiles() =>
            Directory.Exists(ReplayFolder) ? Directory.GetFiles(ReplayFolder, "*" + ReplayExtension).OrderByDescending(f => f, StringComparer.Ordinal) : Enumerable.Empty<string>();

        static void UpdateRecording()
        {
            if (_recording != null && !StillRecording()) FinishRecording();
            if (_recording == null && RecordReplays) BeginRecording();
        }

        static bool StillRecording()
        {
            if (!RecordReplays || IsReplaying) return false;
            if (_localRecorder != null) return !IsOnline && GBibleRuntime.Session == _recordedSession;
            if (_recordedServer != null) return Server == _recordedServer;
            return Client != null && Client.Recording == _recording;
        }

        static void BeginRecording()
        {
            if (IsReplaying || Hub != null) return;
            if (Server != null)
            {
                // 방장 = 방장의 눈, 시험 모드 = 이 화면(클라이언트)의 눈, 화면 없는 서버 = 모두 보는 눈.
                var asPlayer = IsTestMode ? Client?.PlayerId ?? 0 : IsHost ? GBibleRuntime.Session?.LocalPlayer ?? 0 : 0;
                if (IsTestMode && asPlayer == 0) return;
                _recordedServer = Server;
                _recording = Server.AddRecorder(new NetReplay { Source = asPlayer == 0 ? "서버(모두)" : "플레이어 #" + asPlayer }, asPlayer);
            }
            else if (Client != null)
            {
                // 클라이언트는 접속할 때 녹화를 켠다(CreateClient) — 들어간 뒤 켜면 다음 입장부터.
                _recording = Client.Recording;
            }
            else if (GBibleRuntime.Session is GameSession session && session.Role == SessionRole.Local)
            {
                _recordedSession = session;
                _localRecorder = NetServer.StartLocalRecording(session, out _recording);
            }
        }

        static void FinishRecording()
        {
            try
            {
                if (_localRecorder != null)
                {
                    _localRecorder.Flush();
                    _localRecorder.Dispose();
                }
                else if (_recordedServer != null) _recordedServer.RemoveRecorder(_recording);
                else if (Client != null && Client.Recording == _recording) Client.StopRecording();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            // 형식 번호표 하나뿐인(바뀐 것이 없는) 녹화는 지난 녹화를 덮지 않는다.
            if (_recording != null && _recording.Count >= 2) LastReplay = _recording;
            _recording = null;
            _localRecorder = null;
            _recordedServer = null;
            _recordedSession = null;
        }

        static void WireServer(NetServer server)
        {
            server.PlayerJoined += _ => PlayersChanged?.Invoke();
            server.PlayerLeft += _ => PlayersChanged?.Invoke();
        }

        // 클라이언트의 개발 명령(모니터 › 명령·테스트 패널)은 서버에서 돈다(다시 접속해도 지금 연결로). 화면·시간·프로필 명령은 이쪽 그대로.
        const string RemoteSuffix = " — 서버에서 실행";

        static void RemapDeveloperCommands(GameSession session)
        {
            foreach (var command in session.Debug.All.ToList())
            {
                if (command.Group == "시간" || command.Name.StartsWith("프로필", StringComparison.Ordinal)) continue;
                if ((command.Description ?? "").EndsWith(RemoteSuffix, StringComparison.Ordinal)) continue;
                var group = command.Group;
                var name = command.Name;
                session.Debug.Register(group, name, () => Client?.RunDeveloperCommand(group, name), (command.Description ?? "") + RemoteSuffix);
            }
        }

        // 명령줄로 시작(빌드 여러 개를 띄워 시험할 때·서버 빌드):
        //   -momoi-server [포트]                 화면 없는 서버(방장 없음). 서버 빌드는 -batchmode -nographics와 함께.
        //   -momoi-matchserver [포트] -momoi-players 인원   매칭큐 서버(로비·줄·여러 방)
        //   -momoi-host [포트] -momoi-name 이름   방장으로 연다
        //   -momoi-join 주소 -momoi-name 이름     들어간다(주소 = "IP:포트")
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void FromCommandLine()
        {
            string[] args;
            try { args = Environment.GetCommandLineArgs(); }
            catch (Exception) { return; }
            string ValueAfter(string flag)
            {
                var index = Array.IndexOf(args, flag);
                return index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith("-", StringComparison.Ordinal) ? args[index + 1] : null;
            }
            int PortAfter(string flag) => int.TryParse(ValueAfter(flag), out var port) && port > 0 ? port : DefaultPort;
            var name = ValueAfter("-momoi-name");
            if (Array.IndexOf(args, "-momoi-matchserver") >= 0)
            {
                Application.targetFrameRate = 30;
                var players = int.TryParse(ValueAfter("-momoi-players"), out var count) && count > 0 ? count : 2;
                StartMatchServer(PortAfter("-momoi-matchserver"), players);
            }
            else if (Array.IndexOf(args, "-momoi-server") >= 0)
            {
                // 서버는 화면을 그리지 않으므로 프레임을 낮춰 CPU를 아낀다(턴제·느린 실시간 서버 틱).
                Application.targetFrameRate = 30;
                StartDedicated(PortAfter("-momoi-server"));
            }
            else if (Array.IndexOf(args, "-momoi-host") >= 0) HostDirect(PortAfter("-momoi-host"), name);
            else if (ValueAfter("-momoi-join") is string address) JoinDirect(address, name ?? Environment.UserName);
        }

        // GBibleHost.Update: 받은 메시지를 먼저 처리한다.
        internal static void Update(float deltaTime)
        {
            if (Hub != null)
            {
                try { Hub.Update(deltaTime); }
                catch (Exception e) { Debug.LogException(e); }
            }
            if (Server != null)
            {
                try { Server.Receive(); }
                catch (Exception e) { Debug.LogException(e); }
            }
            Client?.Update(deltaTime);
            foreach (var extra in Extras) extra.Update(deltaTime);
            UpdateLink();
            if (Replay != null)
            {
                // 리플레이는 게임 속도(멈춤·배속)가 아니라 자기 배속(Replay.Speed)으로 흐른다.
                try { Replay.Update(Time.unscaledDeltaTime); }
                catch (Exception e) { Debug.LogException(e); }
            }
            UpdateRecording();
            NetTransformSync.Pump();
        }

        // GBibleHost.LateUpdate(이 화면 세션의 EndFrame 다음): 시험 모드 서버의 시간을 흘리고, 바뀐 곳을 보낸다(녹화도 여기서 쌓인다).
        internal static void LateUpdate(float deltaTime)
        {
            try
            {
                if (Server != null)
                {
                    if (IsTestMode && deltaTime > 0) Server.Session.EndFrame(deltaTime);
                    Server.Flush();
                }
                _localRecorder?.Flush();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
