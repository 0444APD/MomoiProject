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
        readonly List<object> _pendingEvents = new List<object>();
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
            session.Data.Synced += OnSynced;
            session.Data.Changed += OnChanged;
            session.Data.KeyDefined += OnKeyChanged;
            session.Data.VisibilityChanged += OnKeyChanged;
            session.Events.Published += OnPublished;
            session.Actions.Executed += OnExecuted;
            session.Actions.TransactionAborted += OnAborted;
        }

        public GameSession Session => _session;
        public NetServerOptions Options { get; }
        public IReadOnlyList<NetPlayer> Players => _players;
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
            PlayerJoined?.Invoke(player);
            BroadcastPlayers();
            return player;
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
        }

        // 받은 메시지를 모두 처리한다.
        public void Receive()
        {
            if (!_started) return;
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
                            try { Handle(peer, e.Data, e.Length); }
                            catch (Exception ex)
                            {
                                _session.Journal.Append(JournalKind.Error, "Net", $"{peer.Player?.ToString() ?? "#" + e.Connection}의 메시지를 처리하지 못했습니다: {ex.Message}");
                            }
                        }
                        break;
                }
            }
            DropSilent();
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
            _session.Actions.Executed -= OnExecuted;
            _session.Actions.TransactionAborted -= OnAborted;
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

        void OnHello(Peer peer, MirrorReader r)
        {
            if (peer.Joined) return;
            var version = (int)r.UVar();
            var hello = new NetHello { Name = r.Str(), Token = r.Str(), GameVersion = r.Str(), Connection = peer.Connection };
            var table = NetProtocol.ReadStrings(r);
            if (version != NetProtocol.Version) { Refuse(peer, $"멀티플레이 규격 버전이 다릅니다(서버 {NetProtocol.Version}, 클라이언트 {version})."); return; }
            if (!string.IsNullOrEmpty(Options.GameVersion) && hello.GameVersion != Options.GameVersion) { Refuse(peer, $"게임 버전이 다릅니다(서버 {Options.GameVersion}, 클라이언트 {hello.GameVersion})."); return; }

            NetAdmission admission;
            try { admission = Admit?.Invoke(hello) ?? NetAdmission.Accept(); }
            catch (Exception e) { admission = NetAdmission.Refuse("입장 확인 중 오류: " + e.Message); }
            if (!admission.Accepted) { Refuse(peer, admission.Reason ?? "입장이 거절됐습니다."); return; }

            var name = !string.IsNullOrEmpty(admission.Name) ? admission.Name : string.IsNullOrEmpty(hello.Name) ? "플레이어" : hello.Name;
            var account = !string.IsNullOrEmpty(admission.Account) ? admission.Account : name;
            // 같은 계정이 다시 오면 같은 플레이어(번호·데이터)로 잇는다.
            var player = _players.FirstOrDefault(p => p.Account == account);
            if (player != null && player.Connected) { Refuse(peer, "같은 계정이 이미 들어와 있습니다."); return; }
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
                if (action.GetType().IsDefined(typeof(SystemActionAttribute), false))
                    failMessage = $"'{action.GetType().Name}'은(는) 시스템 행동이라 보낼 수 없습니다.";
                else
                {
                    // 보낸 사람·시각은 서버가 정한다(속일 수 없게).
                    action.actorId = peer.Player.Id;
                    action.utc = 0;
                    action.tick = 0;
                    var refused = Validate?.Invoke(peer.Player, action);
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
            Flush();
            Send(peer.Connection, w =>
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
            });
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
            try { command.Execute(); }
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
            PlayerLeft?.Invoke(peer.Player);
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
        }

        void OnKeyChanged(string key) => _scanAll = true;

        void OnPublished(object evt)
        {
            if (_session.Actions.IsPreviewing) return;
            if (_session.Data.InTransaction) _pendingEvents.Add(evt);
            else _events.Add(evt);
        }

        void OnExecuted(ActionRecord record)
        {
            if (record.Nested) return;
            if (record.Status == ActionStatus.Done) _events.AddRange(_pendingEvents);
            _pendingEvents.Clear();
        }

        void OnAborted(long transaction) => _pendingEvents.Clear();

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
                    w.Str(record.Key);
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
                var visible = peer.SeeAll ? data.VisibilityOf(key, out _) != DataVisibility.Server : data.IsVisibleTo(key, peer.Player.Id);
                if (visible && !peer.Known.Contains(key))
                {
                    if (!WriteInitial(w, key, snapshots)) continue;
                    peer.Known.Add(key);
                    fresh.Add(key);
                    count++;
                }
                else if (!visible && peer.Known.Remove(key))
                {
                    w.Byte((byte)NetRecord.Hide);
                    w.Str(key);
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
            w.Str(key);
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
            w.Str(key);
            WriteKeyInfo(w, key);
            NetProtocol.WriteScalar(w, _session.Data.Get(key));
        }

        void WriteTree(MirrorWriter w, string key)
        {
            var data = _session.Data;
            w.Byte((byte)NetRecord.Tree);
            w.Str(key);
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
                w.Clear();
                Mirror.WriteNetValue(w, evt, _session.Data.NodeOf);
                encoded.Add((w.ToArray(), evt is IOwnedEvent owned ? owned.Owner : 0));
            }
            return encoded;
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
