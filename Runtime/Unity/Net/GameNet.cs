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
        static readonly List<NetClient> Extras = new List<NetClient>();
        static MemoryNetwork _testNet;

        // 이 프로그램이 서버·방장이면(시험 모드 포함) 서버 중개.
        public static NetServer Server { get; private set; }
        // 이 프로그램의 화면이 클라이언트면(시험 모드 포함) 클라이언트 중개.
        public static NetClient Client { get; private set; }
        public static bool IsTestMode { get; private set; }
        public static bool IsOnline => Server != null || Client != null;
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

        // 서버에 들어간다: 이 화면의 세션을 멈추고(프로필은 저장) '받는 쪽' 세션으로 다시 시작한다.
        public static NetClient Join(INetClientTransport transport, string playerName, string token = null, string gameVersion = null)
        {
            Leave();
            return JoinCore(transport, playerName, token, gameVersion);
        }

        // 시험 모드: 서버를 이 프로그램 안에 띄우고(화면 없음, DB로 만든 세션 + 새 판 행동), 이 화면은 그 서버의 클라이언트가 된다.
        // 메시지는 메모리로 오가며 latency(초)·jitter(±초)만큼 늦는다 — 싱글에서 숨는 화면 문제(Do 직후 결과 읽기 등)가 드러난다.
        public static void StartTestMode(double latency = 0.08, double jitter = 0.02, int extraClients = 0, string playerName = "나")
        {
            Leave();
            _testNet = new MemoryNetwork { Latency = Math.Max(0, latency), Jitter = Math.Max(0, jitter) };
            IsTestMode = true;
            var serverSession = GBibleRuntime.CreateSession();
            Server = new NetServer(serverSession, _testNet.Server(), new NetServerOptions { AllowDeveloperCommands = true });
            WireServer(Server);
            Server.Start();
            StartRun(serverSession);
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

        // GBibleHost.Update: 받은 메시지를 먼저 처리한다.
        internal static void Update(float deltaTime)
        {
            if (Server != null)
            {
                try { Server.Receive(); }
                catch (Exception e) { Debug.LogException(e); }
            }
            Client?.Update(deltaTime);
            foreach (var extra in Extras) extra.Update(deltaTime);
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
