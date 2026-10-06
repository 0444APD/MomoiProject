using System;
using System.Collections.Generic;
using System.Linq;
using GBible.Data;
using UnityEngine;

namespace GBible
{
    // 멀티플레이 창구(Unity). 4블록 중 [서버 중개]·[클라이언트 중개]를 이 게임 프로그램에 붙이고 매 프레임 돌린다(GBibleHost).
    //   방장(사설 호스트·테스트):  GameNet.Host(전송, "방장");          — 이 화면은 서버 세션을 그대로 쓴다
    //   들어가기:                   GameNet.Join(전송, "철수");          — 이 화면의 세션이 '받는 쪽'으로 다시 시작한다
    //   시험 모드(한 프로그램):     GameNet.StartTestMode(latency: 0.1); — 서버를 이 안에 띄우고 이 화면은 클라이언트(메모리 전송·일부러 지연)
    //   끝내기:                     GameNet.Leave();                     — 혼자 하기로 돌아간다
    // 화면 코드는 싱글과 같다(Game.Do·Data·SubscribeAfterAction). 내 번호는 Game.Session.LocalPlayer.
    public static class GameNet
    {
        // 직접 접속의 기본 포트.
        public const int DefaultPort = 7777;

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

        // 모니터가 고를 수 있는 세션들(이름, 세션).
        public static IEnumerable<(string name, GameSession session)> Sessions
        {
            get
            {
                if (Server != null && Server.Session != GBibleRuntime.Session) yield return ("서버", Server.Session);
                if (GBibleRuntime.Session != null) yield return (IsHost ? "방장(서버)" : Client != null ? $"클라이언트 · {Client.Name}" : "이 화면", GBibleRuntime.Session);
                foreach (var extra in Extras) yield return ($"클라이언트 · {extra.Name}", extra.Session);
                if (Hub != null)
                    foreach (var room in Hub.Rooms) yield return ("방 " + room.MatchId, room.Session);
            }
        }

        // 서버 세션이 준비됐을 때(방장으로 열었거나 시험 모드의 서버를 띄웠을 때). 서버 쪽 규칙(차례 검사 Validate 등)을 여기서 건다.
        public static event Action<NetServer> ServerStarted;
        // 이 화면의 클라이언트가 처음 모습까지 받았을 때.
        public static event Action Joined;
        // 이 화면의 클라이언트 연결이 끝났을 때(이유).
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
        }

        // 지금 세션을 서버로 연다(방장). 이 화면의 행동은 방장 번호로 그 자리에서 실행된다.
        public static NetServer Host(INetServerTransport transport, string playerName = null, NetServerOptions options = null)
        {
            Leave();
            var session = GBibleRuntime.Session ?? GBibleRuntime.Start();
            Server = new NetServer(session, transport, options ?? new NetServerOptions { AllowDeveloperCommands = Debug.isDebugBuild });
            WireServer(Server);
            Server.Start();
            Server.AddLocalPlayer(string.IsNullOrEmpty(playerName) ? "방장" : playerName);
            ServerStarted?.Invoke(Server);
            return Server;
        }

        // 직접 접속(WebSocket)으로 방을 연다 — 테스트용 멀티·사설 호스트 시험. 다른 사람은 Address의 주소로 들어온다.
        public static NetServer HostDirect(int port = DefaultPort, string playerName = null) => Host(new WebSocketServerTransport(port), playerName);

        // 직접 접속으로 들어간다. address: "주소:포트" 또는 "ws://주소:포트"(포트를 빼면 7777).
        public static NetClient JoinDirect(string address, string playerName) => Join(new WebSocketClientTransport(address), playerName);

        // 화면 없는 서버(테스트 서버·서버 빌드): 지금 세션을 서버로 연다(방장 플레이어 없음 — 모두 접속해서 들어온다).
        public static NetServer StartDedicated(int port = DefaultPort, NetServerOptions options = null)
        {
            Leave();
            var session = GBibleRuntime.Session ?? GBibleRuntime.Start();
            Server = new NetServer(session, new WebSocketServerTransport(port), options ?? new NetServerOptions { AllowDeveloperCommands = Debug.isDebugBuild });
            WireServer(Server);
            Server.Start();
            Debug.Log($"[MomoiProject] 서버를 열었습니다 — 접속 주소: {Server.Address}");
            ServerStarted?.Invoke(Server);
            return Server;
        }

        // 서버에 들어간다: 이 화면의 세션을 멈추고(프로필은 저장) '받는 쪽' 세션으로 다시 시작한다.
        public static NetClient Join(INetClientTransport transport, string playerName, string token = null, string gameVersion = null)
        {
            Leave();
            return JoinCore(transport, playerName, token, gameVersion);
        }

        // 시험 모드: 서버를 이 프로그램 안에 띄우고(화면 없음, DB로 만든 세션 + 새 판 행동), 이 화면은 그 서버의 클라이언트가 된다.
        // 메시지는 메모리로 오가며 latency(초)·jitter(±초)만큼 늦는다 — 싱글에서 숨는 화면 문제(Do 직후 결과 읽기 등)가 드러난다.
        // 이 프로그램이 매칭큐 서버면(로비·줄·여러 방).
        public static NetHub Hub { get; private set; }

        // 매칭큐 서버의 방이 만들어졌을 때(세션·자리 예약 끝): 서버 규칙(room.Server.Validate)·플레이어별 키·공개 범위를 건다.
        public static event Action<NetRoom> RoomStarting;
        // 방에 정원이 모두 들어왔을 때: 첫 턴 시작 등.
        public static event Action<NetRoom> RoomReady;

        // 매칭큐 서버를 연다(화면 없는 서버·로컬 시험). 방마다 DB로 세션을 만들고 '새 판 행동'을 돌린다.
        // 인증·매칭·저장을 비우면 가짜(이름으로 입장 · 로컬 대기열 · 이 컴퓨터의 파일 <persistentDataPath>/MomoiProject/server).
        public static NetHub StartMatchServer(int port = DefaultPort, int playersPerMatch = 2, INetAuthenticator auth = null, INetMatchmaker matchmaker = null, INetPlayerStore store = null, NetHubOptions options = null)
        {
            Leave();
            store ??= new FilePlayerStore(System.IO.Path.Combine(Application.persistentDataPath, "MomoiProject", "server"));
            matchmaker ??= new LocalMatchmaker { PlayersPerMatch = Math.Max(1, playersPerMatch) };
            Hub = new NetHub(new WebSocketServerTransport(port), room =>
            {
                var session = GBibleRuntime.CreateSession();
                StartRun(session);
                return session;
            }, auth, matchmaker, store, options ?? new NetHubOptions { AllowDeveloperCommands = Debug.isDebugBuild });
            Hub.Log += Debug.Log;
            Hub.RoomStarting += room => RoomStarting?.Invoke(room);
            Hub.RoomReady += room => RoomReady?.Invoke(room);
            Hub.Start();
            var resumed = Hub.ResumeOpenMatches();
            if (resumed > 0) Debug.Log($"[MomoiProject] 진행 중이던 매치 {resumed}개를 다시 열었습니다.");
            return Hub;
        }

        // 매칭큐 서버에 들어간다(로비). 이어서 GameNet.Client.Queue()로 줄을 선다 — 성사되면 Client.AnswerMatch(true).
        public static NetClient FindMatch(string address, string playerName, string token = null) => Join(new WebSocketClientTransport(address), playerName, token);

        // prepareServer: 서버 세션의 판을 직접 준비할 때(테스트 상황 불러오기 등). 비우면 DB의 새 판 행동.
        public static void StartTestMode(double latency = 0.08, double jitter = 0.02, int extraClients = 0, string playerName = "나", Action<GameSession> prepareServer = null)
        {
            Leave();
            _testNet = new MemoryNetwork { Latency = Math.Max(0, latency), Jitter = Math.Max(0, jitter) };
            IsTestMode = true;
            var serverSession = GBibleRuntime.CreateSession();
            Server = new NetServer(serverSession, _testNet.Server(), new NetServerOptions { AllowDeveloperCommands = true });
            WireServer(Server);
            Server.Start();
            if (prepareServer != null) prepareServer(serverSession);
            else StartRun(serverSession);
            ServerStarted?.Invoke(Server);
            for (var i = 0; i < Math.Max(0, extraClients); i++)
            {
                var extra = new NetClient(GBibleRuntime.CreateSession(), _testNet.Client(), $"봇{i + 1}");
                extra.Connect();
                Extras.Add(extra);
            }
            JoinCore(_testNet.Client(), playerName, null, null);
        }

        // 멀티를 끝내고 혼자 하기로 돌아간다(클라이언트였으면 새 혼자 하기 세션으로 다시 시작).
        public static void Leave()
        {
            var wasClient = Client != null;
            Close();
            if (wasClient) GBibleRuntime.RestartLocal();
        }

        // 게임을 끌 때: 나간다고 알리고 닫는다(세션을 다시 만들지 않는다).
        internal static void Shutdown() => Close();

        static void Close()
        {
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
        }

        static NetClient JoinCore(INetClientTransport transport, string playerName, string token, string gameVersion)
        {
            NetClient created = null;
            GBibleRuntime.RestartAsClient(session =>
            {
                created = new NetClient(session, transport, string.IsNullOrEmpty(playerName) ? "플레이어" : playerName, token, gameVersion ?? "");
                RemapDeveloperCommands(session, created);
                created.ActionFailed += failure => GameScreens.ShowFailure(failure.Message);
                created.Joined += () => Joined?.Invoke();
                created.PlayersChanged += () => PlayersChanged?.Invoke();
                created.Closed += reason =>
                {
                    GameScreens.Toast("서버 연결이 끝났습니다: " + reason, 4f);
                    Closed?.Invoke(reason);
                };
            });
            Client = created;
            created?.Connect();
            return created;
        }

        static void WireServer(NetServer server)
        {
            server.PlayerJoined += _ => PlayersChanged?.Invoke();
            server.PlayerLeft += _ => PlayersChanged?.Invoke();
        }

        // 클라이언트의 개발 명령(모니터 › 명령·테스트 패널)은 서버에서 돈다. 화면·시간·프로필 명령은 이쪽 그대로.
        static void RemapDeveloperCommands(GameSession session, NetClient client)
        {
            foreach (var command in session.Debug.All.ToList())
            {
                if (command.Group == "시간" || command.Name.StartsWith("프로필", StringComparison.Ordinal)) continue;
                var group = command.Group;
                var name = command.Name;
                session.Debug.Register(group, name, () => client.RunDeveloperCommand(group, name), (command.Description ?? "") + " — 서버에서 실행");
            }
        }

        // 시험 모드 서버의 판: DB › 시작 파일 › Bootstrap의 '새 판 행동'(있으면). 실행 중 리플렉션이 아니라 시작할 때 한 번.
        static void StartRun(GameSession session)
        {
            var database = GameDatabase.Load();
            var type = database == null || string.IsNullOrEmpty(database.NewRunAction) ? null : TypeResolver.Find(database.NewRunAction);
            if (type == null || type.IsAbstract || !typeof(GameAction).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) == null) return;
            try { session.Do((GameAction)Activator.CreateInstance(type)); }
            catch (Exception e) { session.Journal.Append(JournalKind.Error, "Net", $"시험 모드 서버의 새 판 행동이 실패했습니다 — {e.Message}"); }
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
            else             if (Array.IndexOf(args, "-momoi-server") >= 0)
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
            NetTransformSync.Pump();
        }

        // GBibleHost.LateUpdate(이 화면 세션의 EndFrame 다음): 시험 모드 서버의 시간을 흘리고, 바뀐 곳을 보낸다.
        internal static void LateUpdate(float deltaTime)
        {
            if (Server == null) return;
            try
            {
                if (IsTestMode && deltaTime > 0) Server.Session.EndFrame(deltaTime);
                Server.Flush();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
