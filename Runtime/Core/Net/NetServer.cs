using System;
using System.Collections.Generic;
using System.Linq;

namespace GBible
{
    public sealed class NetServerOptions
    {
        // 최대 접속 인원(방장 포함).
        public int MaxPlayers = 8;
        // 게임 버전(비우면 검사하지 않는다). 다르면 입장을 거절한다.
        public string GameVersion = "";
        // true면 Update가 세션 시간을 흘린다(session.EndFrame). Unity 런타임처럼 이미 흘리는 곳이 있으면 false.
        public bool DriveClock;
        // 바뀐 것이 없어도 시계를 맞춰 주는 간격(초). 0이면 바뀐 것이 있을 때만 보낸다.
        public double TimeSyncInterval = 0.25;
        // 개발 명령(NetMessage.Dev)을 받아 실행하는가. 출시 빌드·경쟁 서버는 false.
        public bool AllowDeveloperCommands;
        // 클라이언트가 보낸 Stream(실시간 위치 등)을 다른 클라이언트에 그대로 전하는가.
        public bool RelayStreams = true;
        // 이만큼(초) 아무것도 오지 않는 연결은 끊는다(클라이언트는 2초마다 핑을 보낸다). 0이면 끊지 않는다.
        public double TimeoutSeconds = 15;
        // 닫을 때(Dispose) 들어와 있는 사람에게 "방이 닫혔다"고 알린다 — 클라이언트가 다시 접속을 시도하지 않는다(방장이 나가면 판이 끝난다).
        // 매칭큐의 방은 끄고 연결을 로비로 돌린다(NetHub가 정한다).
        public bool ByeOnClose = true;
        // 연결 하나가 1초에 보낼 수 있는 메시지 수. 넘친 것은 버리고, 네 배를 넘기면 끊는다(행동을 쏟아붓는 클라이언트가 서버를 멈추지 못하게). 0이면 제한 없음.
        public int MaxMessagesPerSecond = 300;
        // 계정 저장소(NetServer.Store)가 있을 때: 바뀐 계정 값을 이 간격(초)마다 쓴다(나갈 때 · 닫을 때는 바로). 0이면 간격 저장 없음.
        public double AccountSaveSeconds = 10;
    }

    // 서버 중개(4블록의 [서버 중개]): 이 세션(서버 권한)의 행동을 받아 실행하고, 바뀐 곳·이벤트를 클라이언트에 보낸다.
    //   var server = new NetServer(session, transport, new NetServerOptions { DriveClock = true });
    //   server.Start();
    //   매 프레임(또는 서버 틱): server.Update(deltaTime);
    //
    // 하는 일
    //   · 입장: Hello → 입장 확인(Admit) → Welcome(플레이어 번호·형식 번호표·명단) → 보이는 키의 처음 모습.
    //   · 행동: Act → actorId를 보낸 사람 번호로 덮고(속일 수 없게) → 검사(Validate) → session.Do → 묶음 보내기 → ActResult.
    //   · 묶음(Batch): 마지막으로 보낸 뒤 바뀐 곳(Mirror 앞으로 기록·단순 값)과 그 사이 이벤트를, 키의 공개 범위(DataVisibility)대로 사람마다 골라 보낸다.
    //   · 늦게 들어오거나 새로 보이게 된 키는 그 사람에게만 처음 모습을 보낸다.
    // 행동 밖에서 바뀐 것(시간 규칙·개발 명령·Undo)도 다음 묶음에 들어간다. 미리보기·실패한 행동의 이벤트는 보내지 않는다.
    public sealed class NetServer : IDisposable
    {
        sealed class Peer
        {
            public int Connection;
            public NetPlayer Player;
            public readonly MirrorReceiver Receiver = new MirrorReceiver();
            public readonly HashSet<string> Known = new HashSet<string>(StringComparer.Ordinal);
            public bool Joined;
            public bool NeedsScan = true;
            // 마지막으로 무언가 받은 시각(초) — 끊긴 연결을 알아챈다.
            public double LastHeard;
            // 녹화 관람자(전송 대신 리플레이에 쓴다). SeeAll = 주인만 보는 키·이벤트도 모두(서버만 아는 것은 빼고).
            public NetReplay Recorder;
            public bool SeeAll;
            public double RecordStart;
            // 초당 메시지 수 세기(MaxMessagesPerSecond).
            public double WindowStart;
            public int WindowCount;
        }

        readonly System.Diagnostics.Stopwatch _watch = System.Diagnostics.Stopwatch.StartNew();
        int _nextRecorder = -1000;

        // 시계(초). 비우면 실제 시계(테스트·매칭큐 서버가 정한다).
        public Func<double> Clock;
        double Now => Clock?.Invoke() ?? _watch.Elapsed.TotalSeconds;

        struct Record
        {
            public NetRecord Kind;
            public string Key;
            public byte[] Data;
        }

        readonly GameSession _session;
        readonly INetServerTransport _transport;
        readonly Dictionary<int, Peer> _peers = new Dictionary<int, Peer>();
        readonly List<NetPlayer> _players = new List<NetPlayer>();
        readonly List<Record> _records = new List<Record>();
        readonly HashSet<string> _values = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> _trees = new HashSet<string>(StringComparer.Ordinal);
        readonly List<object> _events = new List<object>();
        // 행동 안에서 쏜 이벤트와 그 행동 번호. 그 행동이 성공하면 보낼 것으로 옮기고, 취소되면 그 행동 것만 버린다 —
        // 성공한 행동 뒤(이 서버가 Executed를 받기 전, 연출이 그 이벤트를 받는 동안) 미리보기(CanDo)나 다른 행동이 실패해도 남는다.
        readonly List<(object evt, long transaction)> _pendingEvents = new List<(object, long)>();
        readonly HashSet<Type> _warnedEvents = new HashSet<Type>();
        readonly MirrorWriter _out = new MirrorWriter(1024);
        bool _scanAll;
        bool _started;
        int _nextPlayerId = 1;
        long _lastTick = -1;
        double _lastTimeSync;

        public NetServer(GameSession session, INetServerTransport transport, NetServerOptions options = null)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            Options = options ?? new NetServerOptions();
            if (session.Role == SessionRole.Client) throw new InvalidOperationException("[MomoiProject] 클라이언트 세션으로는 서버를 열 수 없습니다.");
            session.Role = SessionRole.Server;
            // 멀티의 도전과제는 사람마다(Steam 계정 것 — 서버가 판정해 그 사람에게 알린다).
            session.Achievements.UsePerPlayer();
            session.Data.Synced += OnSynced;
            session.Data.Changed += OnChanged;
            session.Data.KeyDefined += OnKeyChanged;
            session.Data.VisibilityChanged += OnKeyChanged;
            session.Events.Published += OnPublished;
            session.Events.Retracted += OnRetracted;
            session.Actions.Executed += OnExecuted;
            session.Actions.TransactionAborted += OnAborted;
            session.Actions.Committing += UpdateProjections;
        }

        public GameSession Session => _session;
        public NetServerOptions Options { get; }

        // ── 계정 저장(사설 서버 · 방장 · 시험 모드) ───────────
        // 멀티의 데이터는 서버 것이다(기기 = 설정만, Steam = 도전과제): 사람마다 남는 값(DB 플레이어 데이터 '플레이어마다' + '계정에 남김')을
        // 이 저장소의 그 사람 계정(NetAccount.values[ID])에 둔다 — 들어올 때 읽고(없으면 처음 값), 나갈 때 · AccountSaveSeconds마다 · 닫을 때 쓴다.
        // 매칭큐의 방은 매칭큐 서버(NetHub)가 계정 값을 맡으므로 비워 둔다. 비우면 계정 값을 남기지 않는다.
        public INetPlayerStore Store;
        readonly HashSet<int> _accountRead = new HashSet<int>();
        readonly HashSet<int> _accountUnread = new HashSet<int>();
        readonly HashSet<int> _accountDirty = new HashSet<int>();
        HashSet<string> _accountKeys;
        double _lastAccountSave;

        HashSet<string> AccountKeySet => _accountKeys ??= new HashSet<string>(_session.Data.AccountKeys, StringComparer.Ordinal);

        // 들어올 때: 그 사람 계정의 값을 그 사람 몫에 넣는다(한 번만 — 다시 접속하면 서버에 남은 값 그대로). 읽지 못하면 그 사람 계정에는 쓰지 않는다.
        void ReadAccount(NetPlayer player)
        {
            if (Store == null || player == null || string.IsNullOrEmpty(player.Account) || !_accountRead.Add(player.Id)) return;
            if (AccountKeySet.Count == 0 || _session.Data.InTransaction) return;
            NetAccount account;
            try { account = Store.Load(player.Account); }
            catch (Exception e)
            {
                _accountUnread.Add(player.Id);
                _session.Journal.Append(JournalKind.Error, "Net", $"{player}의 계정 값을 읽지 못해 처음 값으로 둡니다(이 사람 계정에는 쓰지 않습니다): {e.Message}");
                return;
            }
            foreach (var key in AccountKeySet)
            {
                // 계정에 없는 값은 처음 값으로(그 번호의 몫에 남아 있던 다른 사람 값이 넘어가지 않게).
                if (account?.values == null || !account.values.TryGetValue(key, out var json))
                {
                    _session.Data.ResetKey(PlayerDataStore.KeyFor(key, player.Id));
                    continue;
                }
                if (_session.Data.ImportAccountValue(key, player.Id, json)) continue;
                _accountUnread.Add(player.Id);
                _session.Journal.Append(JournalKind.Warning, "Net", $"{player}의 계정 값 '{key}'을(를) 읽지 못해 처음 값으로 둡니다(이 사람 계정에는 쓰지 않습니다).");
            }
            _accountDirty.Remove(player.Id);
        }

        void WriteAccount(NetPlayer player)
        {
            if (Store == null || player == null || string.IsNullOrEmpty(player.Account) || !_accountRead.Contains(player.Id) || _accountUnread.Contains(player.Id)) return;
            if (AccountKeySet.Count == 0) return;
            try
            {
                var account = Store.Load(player.Account) ?? new NetAccount { id = player.Account, name = player.Name };
                account.values ??= new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var key in AccountKeySet)
                {
                    var json = _session.Data.ExportAccountValue(key, player.Id);
                    if (json != null) account.values[key] = json;
                }
                if (string.IsNullOrEmpty(account.name)) account.name = player.Name;
                Store.Save(account);
                _accountDirty.Remove(player.Id);
            }
            catch (Exception e)
            {
                _session.Journal.Append(JournalKind.Error, "Net", $"{player}의 계정 값을 저장하지 못했습니다: {e.Message}");
            }
        }

        // 지금 들어와 있던 모든 사람의 계정 값을 쓴다(닫기 전 · 개발 명령).
        public void SaveAccounts()
        {
            foreach (var player in _players.ToList()) WriteAccount(player);
        }
        public IReadOnlyList<NetPlayer> Players => _players;

        // 방 주인: 방장(이 프로그램의 플레이어)이 있으면 방장, 없으면(시험 모드 · 화면 없는 서버) 들어와 있는 사람 가운데 번호가 가장 작은 사람.
        // "방장만 하는 행동"(던전 시작 · 강퇴)의 검사: server.Validate = (p, a) => a is StartDungeon && p != server.Owner ? "방장만 시작할 수 있습니다." : null;
        public NetPlayer Owner => _players.FirstOrDefault(p => p.IsLocal) ?? _players.Where(p => p.Connected).OrderBy(p => p.Id).FirstOrDefault();
        public string Address => _transport.Address;
        public bool IsRunning => _started;

        // 입장 확인(가짜 인증이 기본: 누구나 이름으로 들어온다). 매칭큐 서버는 인증 서비스로 바꾼다.
        public Func<NetHello, NetAdmission> Admit = hello => NetAdmission.Accept();
        // 행동 검사: 거절할 이유(사람이 읽는 글)를 돌려주면 실행하지 않는다. null이면 실행한다.
        // 예: 내 차례가 아닐 때, 남의 유닛을 움직이려 할 때.
        public Func<NetPlayer, GameAction, string> Validate;

        public event Action<NetPlayer> PlayerJoined;
        public event Action<NetPlayer> PlayerLeft;
        // 클라이언트가 보낸 Stream(실시간 위치 등). (보낸 사람, 채널, 바이트, 길이) — 바이트는 이 호출 안에서만 쓴다.
        public event Action<NetPlayer, byte, byte[], int> StreamReceived;

        public void Start()
        {
            if (_started) return;
            _transport.Start();
            _started = true;
            _session.Journal.Append(JournalKind.System, "Net", $"서버를 열었습니다({_transport.Address ?? "주소 없음"}).");
        }

        // 서버와 같은 프로그램의 플레이어(방장). 이 프로그램의 화면은 서버 세션을 그대로 쓴다(session.LocalPlayer = 그 번호).
        public NetPlayer AddLocalPlayer(string name)
        {
            var player = new NetPlayer { Id = _nextPlayerId++, Name = string.IsNullOrEmpty(name) ? "방장" : name, Account = "local:" + name, Connection = -1, Connected = true, IsLocal = true };
            _players.Add(player);
            _session.LocalPlayer = player.Id;
            // 플레이어마다 데이터: 혼자 하기 세션이 방이 되면 방장이 혼자 하기의 몫(0번)을 이어받는다(방장 번호의 몫으로 옮김).
            if (!_session.Data.InTransaction)
            {
                _session.Data.AddPlayer(player.Id);
                if (_players.Count(p => p.IsLocal) == 1) _session.Data.MovePlayer(0, player.Id);
                ReadAccount(player);
            }
            foreach (var key in _projections.Keys.ToList()) RefreshProjection(key, player.Id);   // 방장 눈 녹화가 투영 모습을 받게
            PlayerJoined?.Invoke(player);
            BroadcastPlayers();
            return player;
        }

        // ── 객체 단위 공개(투영) ─────────────────────────────────
        // 키 하나를 플레이어마다 다르게 보이게 한다(안개 속 유닛 · 상대의 숨긴 칸): view(플레이어 번호, 원래 값)가 그 사람에게 보일 모습을 만든다
        // (새 객체 — 원래 값을 고치지 않는다. 안에 원래 값의 요소를 넣어도 된다 — 저장소가 복사한다).
        // 서버는 바깥 행동이 끝나기 직전(그 행동 안 — Undo도 같이)마다 '<키>@<번호>'(그 사람만 보는 키)를 다시 만들고, 보낼 때는 원래 이름 <키>로 보낸다 —
        // 클라이언트 코드는 싱글과 똑같이 <키>를 읽는다. 원래 키는 서버만 안다. 방장(서버와 같은 프로그램)은 서버 세션을 그대로 쓰므로 다 본다.
        // 등록은 서버 규칙 자리에서(GameNet.ServerStarted · RoomStarting): server.Project<Army>(GameIds.PlayerData.Units.Id, (player, all) => 보일 모습);
        public void Project(string key, Func<int, object, object> view)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("[MomoiProject] 투영할 플레이어 데이터 ID가 비어 있습니다.", nameof(key));
            _projections[key] = view ?? throw new ArgumentNullException(nameof(view));
            if (_session.Data.IsDefined(key)) _session.Data.SetVisibility(key, DataVisibility.Server);
            foreach (var player in _players) RefreshProjection(key, player.Id);
        }

        public void Project<T>(string key, Func<int, T, T> view) where T : class => Project(key, (player, value) => view(player, value as T));

        // 상태 효과($statuses) · 이번 판 보정($modifiers)도 그 사람에게 보이는 가진 쪽 것만 보낸다 — 숨긴 유닛에 걸린 것 · 쓴 순간(쿨다운)이 새지 않게.
        //   server.HideStatuses((player, holderId) => Sees(server.Session, player, holderId));
        public void HideStatuses(Func<int, string, bool> visibleHolder)
        {
            if (visibleHolder == null) throw new ArgumentNullException(nameof(visibleHolder));
            Project<StatusList>(StatusSystem.Key, (player, all) => new StatusList { items = (all?.items ?? new List<ActiveStatus>()).Where(s => s != null && visibleHolder(player, s.target)).ToList() });
            Project<ModifierList>(AttributeSystem.SessionKey, (player, all) => new ModifierList { items = (all?.items ?? new List<Modifier>()).Where(m => m != null && visibleHolder(player, m.target)).ToList() });
        }

        readonly Dictionary<string, Func<int, object, object>> _projections = new Dictionary<string, Func<int, object, object>>(StringComparer.Ordinal);

        static string ProjectionKey(string key, int player) => key + "@" + player.ToString(System.Globalization.CultureInfo.InvariantCulture);

        // 보낼 때의 이름: 투영 키('<키>@<번호>')면 원래 키 이름.
        string WireName(string key)
        {
            if (_projections.Count == 0 || key == null) return key;
            var at = key.LastIndexOf('@');
            return at > 0 && _projections.ContainsKey(key.Substring(0, at)) && int.TryParse(key.Substring(at + 1), out _) ? key.Substring(0, at) : key;
        }

        bool IsProjectionKey(string key) => WireName(key) != key;

        // 행동이 끝나기 직전: 투영 키를 모두 다시 만든다(원래 키가 아직 없으면 건너뛴다 — 생기면 그때).
        // 방장도 만든다: 방장 화면은 원래 키를 읽지만, 방장 눈 녹화(AddRecorder(방장 번호))는 방장 몫의 투영으로 받는다.
        void UpdateProjections()
        {
            if (_projections.Count == 0) return;
            foreach (var key in _projections.Keys.ToList())
                foreach (var player in _players)
                    RefreshProjection(key, player.Id);
        }

        void RefreshProjection(string key, int player)
        {
            var data = _session.Data;
            if (!data.IsDefined(key)) return;
            if (data.VisibilityOf(key, out _) != DataVisibility.Server) data.SetVisibility(key, DataVisibility.Server);
            var projected = ProjectionKey(key, player);
            if (!data.IsDefined(projected))
            {
                data.DefineOfType(projected, data.ZoneOf(key), data.TypeOf(key), null, data.LabelOf(key));
                data.MarkDerived(projected);
                data.SetVisibility(projected, DataVisibility.Owner, player);
            }
            object view;
            try { view = _projections[key](player, data.Get(key)); }
            catch (Exception e)
            {
                _session.Journal.Append(JournalKind.Error, "Net", $"투영 '{key}'(플레이어 {player}) 중 오류: {e.Message}");
                return;
            }
            if (view != null) data.Set(projected, view);
        }

        public NetPlayer FindPlayer(int id) => _players.FirstOrDefault(p => p.Id == id);

        public NetPlayer FindAccount(string account) => _players.FirstOrDefault(p => p.Account == account);

        // 들어올 사람의 자리를 미리 만든다(매칭큐: 묶인 차례대로 번호 1, 2 …). 그 계정이 들어오면 이 자리(번호·데이터)를 쓴다.
        public NetPlayer Reserve(string account, string name)
        {
            if (string.IsNullOrEmpty(account)) throw new ArgumentException("[MomoiProject] 계정 키가 비어 있습니다.", nameof(account));
            var existing = FindAccount(account);
            if (existing != null) return existing;
            var player = new NetPlayer { Id = _nextPlayerId++, Name = string.IsNullOrEmpty(name) ? "플레이어" : name, Account = account, Connection = -1, Connected = false };
            _players.Add(player);
            return player;
        }

        // 받은 것을 처리하고(행동 실행 포함), 필요하면 시간을 흘리고, 바뀐 곳을 보낸다.
        public void Update(double deltaTime)
        {
            Receive();
            if (Options.DriveClock) _session.EndFrame(deltaTime);
            Flush();
            if (Store != null && _accountDirty.Count > 0 && Options.AccountSaveSeconds > 0 && Now - _lastAccountSave >= Options.AccountSaveSeconds)
            {
                _lastAccountSave = Now;
                foreach (var id in _accountDirty.ToList()) WriteAccount(FindPlayer(id));
            }
        }

        // 받은 메시지를 모두 처리한다.
        public void Receive()
        {
            if (!_started) return;
            _receiving = true;
            try
            {
                ReceiveAll();
            }
            finally
            {
                _receiving = false;
            }
            SendPendingResults();
            DropSilent();
        }

        void ReceiveAll()
        {
            while (_transport.Poll(out var e))
            {
                switch (e.Kind)
                {
                    case NetEventKind.Connected:
                        _peers[e.Connection] = new Peer { Connection = e.Connection, LastHeard = Now };
                        break;
                    case NetEventKind.Disconnected:
                        Drop(e.Connection, e.Reason);
                        break;
                    case NetEventKind.Data:
                        if (_peers.TryGetValue(e.Connection, out var peer))
                        {
                            peer.LastHeard = Now;
                            if (!WithinBudget(peer))
                            {
                                RefuseOverBudget(peer, e.Data, e.Length);
                                break;
                            }
                            try { Handle(peer, e.Data, e.Length); }
                            catch (Exception ex)
                            {
                                _session.Journal.Append(JournalKind.Error, "Net", $"{peer.Player?.ToString() ?? "#" + e.Connection}의 메시지를 처리하지 못했습니다: {ex.Message}");
                            }
                        }
                        break;
                }
            }
        }

        // 초당 메시지 수를 넘으면 그 메시지를 버리고(거짓), 네 배를 넘기면 연결을 끊는다.
        bool WithinBudget(Peer peer)
        {
            var limit = Options.MaxMessagesPerSecond;
            if (limit <= 0) return true;
            var now = Now;
            if (now - peer.WindowStart >= 1)
            {
                peer.WindowStart = now;
                peer.WindowCount = 0;
            }
            if (++peer.WindowCount <= limit) return true;
            if (peer.WindowCount == limit * 4)
            {
                _session.Journal.Append(JournalKind.Warning, "Net", $"{peer.Player?.ToString() ?? "#" + peer.Connection}이(가) 1초에 메시지를 {limit * 4}개 넘게 보내 끊었습니다.");
                _transport.Disconnect(peer.Connection, "메시지를 너무 많이 보냈습니다.");
                Drop(peer.Connection, "메시지를 너무 많이 보냈습니다");
            }
            return false;
        }

        // 한도를 넘어 버린 행동에도 결과(거절)를 돌려준다 — 결과가 오지 않으면 보낸 쪽이 그 행동을 끝없이 기다린다(PendingActions·대사 진행).
        // 입장 전·행동이 아닌 메시지·끊은 연결(네 배를 넘김)은 그냥 버린다. 거절은 요청 번호만 읽고 행동은 읽지 않는다(가볍게).
        void RefuseOverBudget(Peer peer, byte[] data, int length)
        {
            if (!peer.Joined || !_peers.ContainsKey(peer.Connection) || NetProtocol.KindOf(data, length) != NetMessage.Act) return;
            long request;
            try { request = (long)new MirrorReader(data, 1, length).UVar(); }
            catch (FormatException) { return; }
            var connection = peer.Connection;
            QueueResult(() => Send(connection, w =>
            {
                NetProtocol.Begin(NetMessage.ActResult, w);
                w.UVar((ulong)request);
                w.Bool(false);
                w.Str("");
                w.Str("요청이 너무 많아 이 행동을 처리하지 않았습니다(1초에 보낼 수 있는 메시지 수를 넘음).");
                w.UVar(0);
            }));
        }

        // 아무것도 오지 않은 지 오래된 연결을 끊는다(갑자기 꺼진 컴퓨터 — 전송이 알아채지 못한 끊김).
        void DropSilent()
        {
            if (Options.TimeoutSeconds <= 0) return;
            var now = Now;
            foreach (var peer in _peers.Values.Where(p => p.Recorder == null && now - p.LastHeard > Options.TimeoutSeconds).ToList())
            {
                _transport.Disconnect(peer.Connection, "응답이 없어 끊었습니다.");
                Drop(peer.Connection, $"{Options.TimeoutSeconds:0}초 동안 응답이 없었습니다");
            }
        }

        // ── 녹화(리플레이) ─────────────────────────────────────
        // 관람자처럼 묶음을 받아 리플레이에 쓴다. asPlayer = 0이면 모두 보는 눈(주인만 보는 키·이벤트 포함, 서버만 아는 것 제외),
        // 아니면 그 플레이어의 눈. 혼자 하는 게임도 녹화할 수 있다(전송 없이 이 서버만 — StartLocalRecording).
        public NetReplay AddRecorder(NetReplay replay = null, int asPlayer = 0)
        {
            replay ??= new NetReplay { Source = asPlayer == 0 ? "서버(모두)" : "플레이어 #" + asPlayer };
            var peer = new Peer
            {
                Connection = _nextRecorder--, Joined = true, Recorder = replay, SeeAll = asPlayer == 0, RecordStart = Now,
                Player = new NetPlayer { Id = asPlayer, Name = "녹화", Connected = true },
            };
            _peers[peer.Connection] = peer;
            var w = NetProtocol.Begin(NetMessage.Welcome);
            w.UVar((ulong)asPlayer);
            NetProtocol.WriteStrings(w, Mirror.TypeTable());
            WritePlayers(w);
            Deliver(peer, w);
            return replay;
        }

        // 혼자 하는 게임(또는 서버가 아닌 세션)을 녹화한다: 전송 없는 서버를 붙이고 모두 보는 녹화를 시작한다.
        // 매 프레임(행동 뒤) server.Flush()를 불러야 쌓인다. 끝나면 server.Dispose()(세션은 다시 혼자 하기로).
        public static NetServer StartLocalRecording(GameSession session, out NetReplay replay)
        {
            var server = new NetServer(session, new NullServerTransport(), new NetServerOptions { TimeoutSeconds = 0, TimeSyncInterval = 0 });
            // 녹화만 한다: 세션은 혼자 하기 그대로(서버 역할의 검사 — 선택지 주인 확인 등 — 를 켜지 않는다).
            session.Role = SessionRole.Local;
            server.Start();
            replay = server.AddRecorder(new NetReplay { Source = "혼자" });
            return server;
        }

        public void RemoveRecorder(NetReplay replay)
        {
            foreach (var pair in _peers.Where(p => p.Value.Recorder == replay).ToList()) _peers.Remove(pair.Key);
        }

        // 연결 하나에 메시지를 보낸다(녹화 관람자면 리플레이에 쓴다).
        void Deliver(Peer peer, MirrorWriter w, bool reliable = true)
        {
            if (peer.Recorder != null) peer.Recorder.Add(Now - peer.RecordStart, w.Data, w.Length);
            else _transport.Send(peer.Connection, w.Data, w.Length, reliable);
        }

        public void Kick(NetPlayer player, string reason)
        {
            if (player == null || player.IsLocal || !player.Connected) return;
            Send(player.Connection, w =>
            {
                NetProtocol.Begin(NetMessage.Bye, w);
                w.Str(reason ?? "");
            });
            _transport.Disconnect(player.Connection, reason);
            Drop(player.Connection, reason);
        }

        public void Dispose()
        {
            _session.Data.Synced -= OnSynced;
            _session.Data.Changed -= OnChanged;
            _session.Data.KeyDefined -= OnKeyChanged;
            _session.Data.VisibilityChanged -= OnKeyChanged;
            _session.Events.Published -= OnPublished;
            _session.Events.Retracted -= OnRetracted;
            SaveAccounts();   // 닫기 전에 사람마다 계정 값을 쓴다
            _session.Actions.Executed -= OnExecuted;
            _session.Actions.TransactionAborted -= OnAborted;
            _session.Actions.Committing -= UpdateProjections;
            foreach (var peer in _peers.Values.ToList())
            {
                if (peer.Recorder != null) continue;
                if (Options.ByeOnClose && peer.Joined)
                    Send(peer.Connection, w =>
                    {
                        NetProtocol.Begin(NetMessage.Bye, w);
                        w.Str("방이 닫혔습니다.");
                    });
                _transport.Disconnect(peer.Connection, "서버가 닫혔습니다.");
            }
            _peers.Clear();
            _transport.Dispose();
            _started = false;
            if (_session.Role == SessionRole.Server) _session.Role = SessionRole.Local;
            // 방장이 방을 닫으면 혼자 하기로: 방장의 플레이어마다 몫을 0번으로 되돌리고 남의 몫은 지운다(이어 하는 판 · 저장에 없는 플레이어가 남지 않게).
            var host = _players.FirstOrDefault(p => p.IsLocal);
            if (host != null && !_session.Data.InTransaction)
            {
                foreach (var other in _players.Where(p => p != host)) _session.Data.RemovePlayer(other.Id);
                _session.Data.MovePlayer(host.Id, 0);
                if (_session.LocalPlayer == host.Id) _session.LocalPlayer = 0;
            }
        }

        // ── 받기 ───────────────────────────────────────────────

        void Handle(Peer peer, byte[] data, int length)
        {
            var r = new MirrorReader(data, 1, length);
            switch (NetProtocol.KindOf(data, length))
            {
                case NetMessage.Hello:
                    OnHello(peer, r);
                    break;
                case NetMessage.Act when peer.Joined:
                    OnAct(peer, r);
                    break;
                case NetMessage.Dev when peer.Joined:
                    OnDev(peer, r);
                    break;
                case NetMessage.Ping:
                    Send(peer.Connection, w =>
                    {
                        NetProtocol.Begin(NetMessage.Pong, w);
                        w.Double(r.Double());
                    });
                    break;
                case NetMessage.Stream when peer.Joined:
                    OnStream(peer, data, length);
                    break;
                case NetMessage.Bye:
                    _transport.Disconnect(peer.Connection, "나갔습니다.");
                    Drop(peer.Connection, "나갔습니다.");
                    break;
            }
        }

        // 같은 계정의 옛 연결이 이만큼(초) 조용하면 끊긴 것으로 보고 새 연결이 이어받는다(핑 간격 2초 + 여유).
        const double TakeoverSilence = 3;

        void OnHello(Peer peer, MirrorReader r)
        {
            if (peer.Joined) return;
            var version = (int)r.UVar();
            var hello = new NetHello { Name = NetProtocol.CleanName(r.Str()), Token = r.Str(), GameVersion = r.Str(), Device = r.Str(), Connection = peer.Connection };
            var table = NetProtocol.ReadStrings(r, NetProtocol.MaxTypeTable);
            if (version != NetProtocol.Version) { Refuse(peer, $"멀티플레이 규격 버전이 다릅니다(서버 {NetProtocol.Version}, 클라이언트 {version})."); return; }
            if (!string.IsNullOrEmpty(Options.GameVersion) && hello.GameVersion != Options.GameVersion) { Refuse(peer, $"게임 버전이 다릅니다(서버 {Options.GameVersion}, 클라이언트 {hello.GameVersion})."); return; }

            NetAdmission admission;
            try { admission = Admit?.Invoke(hello) ?? NetAdmission.Accept(); }
            catch (Exception e) { admission = NetAdmission.Refuse("입장 확인 중 오류: " + e.Message); }
            if (!admission.Accepted) { Refuse(peer, admission.Reason ?? "입장이 거절됐습니다."); return; }

            var name = !string.IsNullOrEmpty(admission.Name) ? admission.Name : string.IsNullOrEmpty(hello.Name) ? "플레이어" : hello.Name;
            // 계정 키(입장 확인이 정하지 않았으면): 기기 + 이름 — 다른 컴퓨터의 같은 이름(기본 이름 "플레이어" 둘)이 한 자리로 잇지 않게. 같은 기기 · 같은 이름이면 다시 접속해 같은 자리.
            var account = !string.IsNullOrEmpty(admission.Account) ? admission.Account : string.IsNullOrEmpty(hello.Device) ? name : "device:" + hello.Device + "/" + name;
            // 같은 계정이 다시 오면 같은 플레이어(번호·데이터)로 잇는다.
            var player = _players.FirstOrDefault(p => p.Account == account);
            if (player != null && player.Connected)
            {
                if (player.IsLocal) { Refuse(peer, "같은 계정이 이미 들어와 있습니다."); return; }
                // 다시 접속: 옛 연결이 한동안 조용하면(네트워크가 끊겼는데 서버는 아직 모름) 새 연결이 그 자리를 이어받는다.
                // 옛 연결이 아직 살아 있으면 거절하지 않고 끊기만 한다 — 클라이언트가 잠시 뒤 다시 시도한다(거절은 다시 접속을 멈추게 한다).
                var old = _peers.Values.FirstOrDefault(p => p != peer && p.Player == player && p.Recorder == null);
                if (old != null && Now - old.LastHeard < TakeoverSilence)
                {
                    _peers.Remove(peer.Connection);
                    _transport.Disconnect(peer.Connection, "같은 계정이 아직 연결되어 있습니다 — 잠시 뒤 다시 접속합니다.");
                    return;
                }
                if (old != null)
                {
                    _transport.Disconnect(old.Connection, "같은 계정이 다시 접속했습니다.");
                    // 옛 연결은 나간 것으로 알린다(PlayerLeft) — 이어서 새 연결로 들어온다(PlayerJoined). 들어옴·나감이 짝을 이룬다.
                    Drop(old.Connection, "같은 계정이 다시 접속했습니다");
                }
                player.Connected = false;
            }
            if (player == null)
            {
                if (_players.Count(p => p.Connected) >= Options.MaxPlayers) { Refuse(peer, $"방이 찼습니다(최대 {Options.MaxPlayers}명)."); return; }
                player = new NetPlayer { Id = _nextPlayerId++, Account = account };
                _players.Add(player);
            }
            player.Name = name;
            player.Connection = peer.Connection;
            player.Connected = true;
            peer.Player = player;
            var missing = peer.Receiver.UseTypes(table);
            if (missing.Count > 0)
                _session.Journal.Append(JournalKind.Warning, "Net", $"{player}의 게임에는 있고 서버에는 없는 형식 {missing.Count}개: {string.Join(", ", missing.Take(3))}{(missing.Count > 3 ? " …" : "")}");

            Send(peer.Connection, w =>
            {
                NetProtocol.Begin(NetMessage.Welcome, w);
                w.UVar((ulong)player.Id);
                NetProtocol.WriteStrings(w, Mirror.TypeTable());
                WritePlayers(w);
            });
            peer.Joined = true;
            peer.NeedsScan = true;
            _session.Journal.Append(JournalKind.System, "Net", $"{player}이(가) 들어왔습니다.");
            // 플레이어마다 따로인 데이터 · 투영 키는 들어오는 사람 몫을 만들어 둔다(처음 모습이 다음 묶음에 간다).
            // 방장이 없는 서버(화면 없는 서버)가 혼자 하기 세션에서 열렸으면 그 0번 몫은 첫 사람이 들어올 때 지운다(없는 플레이어가 끼지 않게).
            if (!_session.Data.InTransaction && !_players.Any(p => p.IsLocal)) _session.Data.RemovePlayer(0);
            _session.Data.AddPlayer(player.Id);
            ReadAccount(player);
            foreach (var key in _projections.Keys.ToList()) RefreshProjection(key, player.Id);
            PlayerJoined?.Invoke(player);
            BroadcastPlayers();
            Flush();
        }

        void Refuse(Peer peer, string reason)
        {
            Send(peer.Connection, w =>
            {
                NetProtocol.Begin(NetMessage.Reject, w);
                w.Str(reason);
            });
            _session.Journal.Append(JournalKind.Warning, "Net", $"입장 거절(#{peer.Connection}): {reason}");
            _transport.Disconnect(peer.Connection, reason);
            _peers.Remove(peer.Connection);
        }

        // Receive 안에서 받은 행동의 결과(ActResult): 프레임 끝에 묶음 한 번 뒤 차례대로 보낸다. Receive 밖이면 바로.
        readonly List<Action> _pendingResults = new List<Action>();
        bool _receiving;

        void QueueResult(Action send)
        {
            if (_receiving)
            {
                _pendingResults.Add(send);
                return;
            }
            Flush();
            send();
        }

        void SendPendingResults()
        {
            if (_pendingResults.Count == 0) return;
            Flush();
            foreach (var send in _pendingResults)
            {
                try { send(); }
                catch (Exception e) { _session.Journal.Append(JournalKind.Error, "Net", "행동 결과를 보내지 못했습니다: " + e.Message); }
            }
            _pendingResults.Clear();
        }

        void OnAct(Peer peer, MirrorReader r)
        {
            var request = (long)r.UVar();
            NetProtocol.ReadBlock(r, out var start, out var end);
            string failKey = null, failMessage = null;
            IReadOnlyDictionary<string, object> failArgs = null;
            GameAction action = null;
            try
            {
                action = peer.Receiver.Load(r.Data, start, end) as GameAction;
                if (action == null) failMessage = "행동이 아닙니다.";
            }
            catch (Exception e)
            {
                failMessage = "행동을 읽지 못했습니다: " + e.Message;
            }
            if (action != null)
            {
                if (IsSystemAction(action.GetType()))
                    failMessage = $"'{action.GetType().Name}'은(는) 시스템 행동이라 보낼 수 없습니다.";
                else
                {
                    // 보낸 사람·시각은 서버가 정한다(속일 수 없게).
                    action.actorId = peer.Player.Id;
                    action.utc = 0;
                    action.tick = 0;
                    // 검사 코드의 오류도 거절로 돌려준다(결과가 오지 않으면 클라이언트가 그 행동을 끝없이 기다린다).
                    string refused = null;
                    try
                    {
                        if (Validate != null)
                        {
                            // 받은 행동의 참조 필드(유닛 · 카드 …)는 id만 든 껍데기다 — 검사 코드가 주인 · 칸 같은 값을 읽게 지금 서버 데이터의 객체로 먼저 바꿔 넣는다.
                            // 검사 안에서 플레이어마다 데이터('hand')는 보낸 사람의 몫이다.
                            using (_session.ActingAs(peer.Player.Id))
                            {
                                ActionReferences.Resolve(action, _session.Game);
                                refused = Validate(peer.Player, action);
                            }
                        }
                    }
                    catch (GameFailure failure)
                    {
                        // Validate 안에서 던진 Game.Fail은 키 · 인자도 보낸다(받는 쪽이 자기 언어로).
                        refused = failure.Text();
                        failKey = failure.Key;
                        failArgs = failure.Args;
                    }
                    catch (Exception e) { refused = "행동 검사 중 오류: " + e.Message; }
                    if (refused != null) failMessage = refused;
                    else
                    {
                        try { _session.Do(action); }
                        catch (GameFailure failure)
                        {
                            failKey = failure.Key;
                            failArgs = failure.Args;
                            failMessage = failure.Text();
                        }
                        catch (Exception e)
                        {
                            failMessage = e.Message;
                        }
                    }
                }
            }
            // 결과는 이 프레임의 행동을 다 돌린 뒤 묶음(바뀐 곳) 한 번 다음에 보낸다 — 받은 행동마다 Sync·묶음을 만들지 않게(Receive 끝).
            // 받는 쪽은 바뀐 곳을 먼저 받고 결과를 받는다(지금과 같은 차례).
            var connection = peer.Connection;
            QueueResult(() => Send(connection, w =>
            {
                NetProtocol.Begin(NetMessage.ActResult, w);
                w.UVar((ulong)request);
                w.Bool(failMessage == null);
                if (failMessage == null) return;
                w.Str(failKey ?? "");
                w.Str(failMessage);
                // 받는 쪽이 자기 언어로 다시 만들 수 있게 인자도(단순 값으로).
                w.UVar((ulong)(failArgs?.Count ?? 0));
                if (failArgs == null) return;
                foreach (var pair in failArgs)
                {
                    w.Str(pair.Key);
                    NetProtocol.WriteScalar(w, pair.Value);
                }
            }));
            if (failMessage != null)
                _session.Journal.Append(JournalKind.Warning, "Net", $"{peer.Player}의 행동 거절: {failMessage}");
        }

        void OnDev(Peer peer, MirrorReader r)
        {
            var group = r.Str();
            var name = r.Str();
            if (!Options.AllowDeveloperCommands)
            {
                _session.Journal.Append(JournalKind.Warning, "Net", $"{peer.Player}의 개발 명령을 막았습니다(서버 설정에서 꺼져 있음): {group} › {name}");
                return;
            }
            var command = _session.Debug.All.FirstOrDefault(c => c.Group == group && c.Name == name);
            if (command == null) return;
            // 누른 사람으로 돈다(플레이어마다 데이터 'bag'은 그 사람의 몫).
            try { using (_session.ActingAs(peer.Player.Id)) command.Execute(); }
            catch (Exception e) { _session.Journal.Append(JournalKind.Error, "Net", $"개발 명령 '{group} › {name}' 실패: {e.Message}"); }
            Flush();
        }

        void OnStream(Peer peer, byte[] data, int length)
        {
            if (length < 2) return;
            var channel = data[1];
            StreamReceived?.Invoke(peer.Player, channel, data, length);
            if (!Options.RelayStreams) return;
            // 받은 사람이 누가 보냈는지 알게: Stream, 보낸 사람 번호, 채널, 내용.
            var w = NetProtocol.Begin(NetMessage.Stream, new MirrorWriter(length + 8));
            w.UVar((ulong)peer.Player.Id);
            w.Byte(channel);
            w.Bytes(data, 2, length - 2);
            foreach (var other in _peers.Values)
                if (other.Joined && other != peer && other.Recorder == null) _transport.Send(other.Connection, w.Data, w.Length, reliable: false);
        }

        // 서버에서 실시간 바이트를 모두에게(또는 한 사람에게) 보낸다(보낸 사람 번호 0 = 서버).
        public void SendStream(byte channel, byte[] data, int length, NetPlayer to = null, int from = 0)
        {
            var w = NetProtocol.Begin(NetMessage.Stream, new MirrorWriter(length + 8));
            w.UVar((ulong)from);
            w.Byte(channel);
            w.Bytes(data, 0, length);
            foreach (var peer in _peers.Values)
                if (peer.Joined && peer.Recorder == null && (to == null || peer.Player == to)) _transport.Send(peer.Connection, w.Data, w.Length, reliable: false);
        }

        void Drop(int connection, string reason)
        {
            if (!_peers.TryGetValue(connection, out var peer)) return;
            _peers.Remove(connection);
            if (peer.Player == null) return;
            peer.Player.Connected = false;
            peer.Player.Connection = -1;
            _session.Journal.Append(JournalKind.System, "Net", $"{peer.Player}이(가) 나갔습니다{(string.IsNullOrEmpty(reason) ? "" : ": " + reason)}.");
            WriteAccount(peer.Player);   // 나가면 그 사람 계정 값을 바로 쓴다(다시 오면 서버에 남은 값으로 이어 간다)
            // 게임 코드의 오류가 받기(남은 메시지)·이어받기(새 연결의 입장)를 멈추지 않게.
            try { PlayerLeft?.Invoke(peer.Player); }
            catch (Exception e) { _session.Journal.Append(JournalKind.Error, "Net", $"PlayerLeft 처리 중 오류({peer.Player}): {e.Message}"); }
            BroadcastPlayers();
        }

        // ── 모으기 ─────────────────────────────────────────────

        void OnSynced(ChangeSet changes)
        {
            if (changes.Snapshot != null) _records.Add(new Record { Kind = NetRecord.Reset, Key = changes.Key, Data = changes.Snapshot });
            else if (!changes.IsEmpty) _records.Add(new Record { Kind = NetRecord.Delta, Key = changes.Key, Data = changes.Forward.ToArray() });
        }

        void OnChanged(string key)
        {
            var data = _session.Data;
            if (!data.IsData(key)) _values.Add(key);
            else if (!data.IsMirroredKey(key)) _trees.Add(key);
            // 계정 값이 바뀐 사람(간격 저장)
            if (Store != null && key != null)
            {
                var colon = key.LastIndexOf(':');
                if (colon > 0 && AccountKeySet.Contains(key.Substring(0, colon))) _accountDirty.Add(PlayerDataStore.PlayerOfKey(key));
            }
        }

        void OnKeyChanged(string key) => _scanAll = true;

        void OnPublished(object evt)
        {
            if (_session.Actions.IsPreviewing) return;
            if (_session.Data.InTransaction) _pendingEvents.Add((evt, _session.Data.ActiveTransactionId));
            else _events.Add(evt);
        }

        void OnExecuted(ActionRecord record)
        {
            if (record.Nested) return;
            if (record.Status == ActionStatus.Done)
                foreach (var (evt, transaction) in _pendingEvents)
                    if (transaction == record.TransactionId) _events.Add(evt);
            _pendingEvents.RemoveAll(p => p.transaction == record.TransactionId);
        }

        void OnAborted(long transaction) => _pendingEvents.RemoveAll(p => p.transaction == transaction);

        // 행동 안에서 일부만 되돌린 이벤트(실패한 이벤트 예약)는 보내지 않는다.
        void OnRetracted(object evt)
        {
            var i = _pendingEvents.FindLastIndex(p => Equals(p.evt, evt));
            if (i >= 0) _pendingEvents.RemoveAt(i);
        }

        // ── 보내기 ─────────────────────────────────────────────

        // 모아 둔 바뀐 곳·이벤트를 지금 보낸다(행동 안에서는 하지 않는다 — 행동이 끝난 뒤).
        public void Flush()
        {
            if (!_started || _session.Data.InTransaction) return;
            _session.Data.Sync();
            var anyJoined = false;
            foreach (var peer in _peers.Values) anyJoined |= peer.Joined;
            if (!anyJoined)
            {
                ClearPending();
                return;
            }

            var records = _records.ToArray();
            var values = _values.ToArray();
            var trees = _trees.ToArray();
            var events = EncodeEvents();
            ClearPending();
            var scanAll = _scanAll;
            _scanAll = false;

            var tick = _session.Clock.Tick;
            var time = _session.Clock.Time;
            var timeSync = Options.TimeSyncInterval > 0 && tick != _lastTick && time - _lastTimeSync >= Options.TimeSyncInterval;
            var snapshots = new Dictionary<string, byte[]>(StringComparer.Ordinal);

            foreach (var peer in _peers.Values)
            {
                if (!peer.Joined) continue;
                var w = NetProtocol.Begin(NetMessage.Batch, _out);
                w.Long(tick);
                w.Double(time);
                var count = 0;
                var fresh = new HashSet<string>(StringComparer.Ordinal);
                if (scanAll || peer.NeedsScan)
                {
                    peer.NeedsScan = false;
                    count += WriteVisibility(peer, w, fresh, snapshots);
                }
                foreach (var record in records)
                {
                    if (!peer.Known.Contains(record.Key) || fresh.Contains(record.Key)) continue;
                    w.Byte((byte)record.Kind);
                    w.Str(WireName(record.Key));
                    if (record.Kind == NetRecord.Reset) WriteKeyInfo(w, record.Key);
                    NetProtocol.WriteBlock(w, record.Data, 0, record.Data.Length);
                    count++;
                }
                foreach (var key in values)
                {
                    if (!peer.Known.Contains(key) || fresh.Contains(key) || !_session.Data.IsDefined(key)) continue;
                    WriteValue(w, key);
                    count++;
                }
                foreach (var key in trees)
                {
                    if (!peer.Known.Contains(key) || fresh.Contains(key) || !_session.Data.IsDefined(key)) continue;
                    WriteTree(w, key);
                    count++;
                }
                foreach (var (evt, owner) in events)
                {
                    if (owner != 0 && !peer.SeeAll && peer.Player.Id != owner) continue;
                    w.Byte((byte)NetRecord.Event);
                    NetProtocol.WriteBlock(w, evt, 0, evt.Length);
                    count++;
                }
                w.Byte(0);
                if (count > 0 || timeSync) Deliver(peer, w);
            }
            if (timeSync || records.Length > 0 || values.Length > 0 || events.Count > 0)
            {
                _lastTick = tick;
                _lastTimeSync = time;
            }
        }

        void ClearPending()
        {
            _records.Clear();
            _values.Clear();
            _trees.Clear();
            _events.Clear();
        }

        // 보이게 된 키는 처음 모습을, 보이지 않게 된 키는 Hide를 쓴다. 쓴 기록 수.
        int WriteVisibility(Peer peer, MirrorWriter w, HashSet<string> fresh, Dictionary<string, byte[]> snapshots)
        {
            var data = _session.Data;
            var count = 0;
            foreach (var key in data.Keys.ToArray())
            {
                // 녹화 관람자(SeeAll)는 투영 전의 원래 키를 보고 투영 키는 받지 않는다(같은 이름이 둘이 되지 않게).
                var visible = peer.SeeAll
                    ? (_projections.ContainsKey(key) || (data.VisibilityOf(key, out _) != DataVisibility.Server && !IsProjectionKey(key)))
                    : data.IsVisibleTo(key, peer.Player.Id);
                if (visible && !peer.Known.Contains(key))
                {
                    if (!WriteInitial(w, key, snapshots)) continue;
                    peer.Known.Add(key);
                    fresh.Add(key);
                    count++;
                }
                else if (!visible && peer.Known.Remove(key))
                {
                    // 투영으로 바뀐 원래 키는 숨기지 않는다 — 같은 이름으로 그 사람 몫(투영 키)의 처음 모습이 간다(차례에 따라 지워지지 않게).
                    if (_projections.ContainsKey(key) && !peer.SeeAll) continue;
                    w.Byte((byte)NetRecord.Hide);
                    w.Str(WireName(key));
                    count++;
                }
            }
            return count;
        }

        bool WriteInitial(MirrorWriter w, string key, Dictionary<string, byte[]> snapshots)
        {
            var data = _session.Data;
            if (!data.IsData(key))
            {
                WriteValue(w, key);
                return true;
            }
            if (!data.IsMirroredKey(key))
            {
                WriteTree(w, key);
                return true;
            }
            if (!snapshots.TryGetValue(key, out var snapshot)) snapshots[key] = snapshot = data.Snapshot(key);
            if (snapshot == null) return false;
            w.Byte((byte)NetRecord.Reset);
            w.Str(WireName(key));
            WriteKeyInfo(w, key);
            NetProtocol.WriteBlock(w, snapshot, 0, snapshot.Length);
            return true;
        }

        void WriteKeyInfo(MirrorWriter w, string key)
        {
            var data = _session.Data;
            w.Byte(data.IsDefined(key) ? (byte)data.ZoneOf(key) : (byte)0);
            w.Str(data.LabelOf(key));
        }

        void WriteValue(MirrorWriter w, string key)
        {
            w.Byte((byte)NetRecord.Value);
            w.Str(WireName(key));
            WriteKeyInfo(w, key);
            NetProtocol.WriteScalar(w, _session.Data.Get(key));
        }

        void WriteTree(MirrorWriter w, string key)
        {
            var data = _session.Data;
            w.Byte((byte)NetRecord.Tree);
            w.Str(WireName(key));
            WriteKeyInfo(w, key);
            w.Str(data.TypeOf(key)?.FullName ?? "");
            w.Str(MiniJson.Serialize(data.ExportTree(key)));
        }

        // 보낼 이벤트(바이트, 주인 — 0이면 모두). [GameEvent(ServerOnly = true)]는 빼고, IOwnedEvent는 주인만.
        List<(byte[] data, int owner)> EncodeEvents()
        {
            var encoded = new List<(byte[], int)>(_events.Count);
            if (_events.Count == 0) return encoded;
            var w = new MirrorWriter();
            foreach (var evt in _events)
            {
                var type = evt.GetType();
                if (IsServerOnly(type)) continue;
                if (type.IsValueType || Mirror.Find(type) == null)
                {
                    if (_warnedEvents.Add(type))
                        _session.Journal.Append(JournalKind.Warning, "Net", $"이벤트 '{type.Name}'은(는) 클라이언트에 보낼 수 없습니다(구조체이거나 비교 코드가 없음 — [GameEvent] 클래스로 만드세요).");
                    continue;
                }
                // 생성 코드가 온전히 쓰지 못하는 이벤트(중첩 컬렉션·쓸 수 없는 구조체·생성 코드가 없는 값·readonly 필드 …)는 보내지 않는다 —
                // 받는 쪽에 빈 칸으로 도착하거나 쓰다가 멈추는 대신, 어느 필드인지 밝힌 오류를 남긴다. 형식 검사는 형식마다 처음 한 번(Mirror.CanSend).
                if (!Mirror.CanSend(type, out var reason))
                {
                    Unsendable(type, reason);
                    continue;
                }
                w.Clear();
                try { Mirror.WriteNetValue(w, evt, _checkedNode ??= new Func<object, MirrorNode>(CheckedNode)); }
                catch (Exception e)
                {
                    Unsendable(type, e.Message);
                    continue;
                }
                encoded.Add((w.ToArray(), evt is IOwnedEvent owned ? owned.Owner : 0));
            }
            return encoded;
        }

        Func<object, MirrorNode> _checkedNode;

        // 이벤트를 쓰는 동안 객체마다: 받는 쪽이 가진 데이터 안의 객체면 번호로 가리키고(그대로), 아니면 내용째 쓰므로
        // 보낼 수 있는 값인지 본다(object 칸의 숫자·글자, 생성 코드가 없는 객체 등 — Mirror.CanSendValue).
        MirrorNode CheckedNode(object value)
        {
            var node = _session.Data.NodeOf(value);
            if ((node == null || !node.Sent) && !Mirror.CanSendValue(value, out var reason)) throw new InvalidOperationException(reason);
            return node;
        }

        // 보낼 수 없는 이벤트: 빼고 오류를 남긴다(형식마다 한 번).
        void Unsendable(Type type, string reason)
        {
            if (_warnedEvents.Add(type))
                _session.Journal.Append(JournalKind.Error, "Net", $"이벤트 '{type.Name}'을(를) 클라이언트에 보낼 수 없어 뺐습니다: {reason}");
        }

        // [SystemAction]인가(형식마다 처음 한 번 찾아 둔다 — 받은 행동마다 리플렉션하지 않게).
        static readonly Dictionary<Type, bool> SystemActions = new Dictionary<Type, bool>();

        static bool IsSystemAction(Type type)
        {
            lock (SystemActions)
            {
                if (SystemActions.TryGetValue(type, out var found)) return found;
                return SystemActions[type] = type.IsDefined(typeof(SystemActionAttribute), false);
            }
        }

        // [GameEvent(ServerOnly = true)]인가(형식마다 처음 한 번 찾아 둔다).
        static readonly Dictionary<Type, bool> ServerOnlyEvents = new Dictionary<Type, bool>();

        static bool IsServerOnly(Type type)
        {
            lock (ServerOnlyEvents)
            {
                if (ServerOnlyEvents.TryGetValue(type, out var found)) return found;
                var attribute = (GameEventAttribute)Attribute.GetCustomAttribute(type, typeof(GameEventAttribute));
                return ServerOnlyEvents[type] = attribute != null && attribute.ServerOnly;
            }
        }

        void WritePlayers(MirrorWriter w)
        {
            w.UVar((ulong)_players.Count);
            foreach (var player in _players)
            {
                w.UVar((ulong)player.Id);
                w.Str(player.Name);
                w.Bool(player.Connected);
            }
        }

        void BroadcastPlayers()
        {
            foreach (var peer in _peers.Values)
            {
                if (!peer.Joined || peer.Recorder != null) continue;
                Send(peer.Connection, w =>
                {
                    NetProtocol.Begin(NetMessage.Players, w);
                    WritePlayers(w);
                });
            }
        }

        void Send(int connection, Action<MirrorWriter> write)
        {
            var w = new MirrorWriter();
            write(w);
            _transport.Send(connection, w.Data, w.Length);
        }
    }
}
