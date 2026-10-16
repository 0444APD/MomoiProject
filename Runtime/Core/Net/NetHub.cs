using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace GBible
{
    public sealed class NetHubOptions
    {
        // 수락을 기다리는 시간(초). 넘기면 수락하지 않은 사람을 빼고 나머지는 다시 줄을 선다(기다린 시간 그대로).
        public double AcceptSeconds = 15;
        public string GameVersion = "";
        public bool AllowDeveloperCommands;
        // 진행 중인 매치를 저장한다(서버가 꺼져도 ResumeOpenMatches로 이어 하기). 행동이 있었던 매치를 MatchSaveSeconds마다 한 번.
        public bool SaveMatchEachAction = true;
        // 저장 간격(초). 0이면 행동마다(실시간 방은 행동이 틱마다라 서버가 느려진다). 서버를 닫을 때·방이 준비될 때는 바로 저장한다.
        public double MatchSaveSeconds = 2;
        // 방에 정원이 다 들어오지 않을 때 기다리는 시간(초). 넘기면 매치를 없던 것으로 하고(아무도 점수를 잃지 않는다) 로비로 돌려보낸다.
        public double ReadySeconds = 30;
        // 이만큼(초) 아무것도 오지 않는 연결은 끊는다(로비·줄·방 모두 — 클라이언트는 2초마다 핑). 0이면 끊지 않는다.
        public double TimeoutSeconds = 15;
    }

    // 방 하나 = 매치 하나(서버 중개 하나 + 세션 하나).
    public sealed class NetRoom
    {
        public string MatchId;
        public string Mode;
        public NetServer Server;
        public GameSession Session => Server?.Session;
        // 이 매치의 계정들(묶인 차례 = 플레이어 번호 1, 2 …).
        public readonly List<string> Accounts = new List<string>();
        // 정원이 모두 한 번 들어왔다(RoomReady를 쏜 뒤).
        public bool Ready;
        public bool Ended;
        // 게임 코드가 붙여 두는 값.
        public object Tag;
        internal NetHub.RoomTransport Transport;
        internal double CreatedAt;
        internal bool Ending;
        internal List<NetMatchResult> PendingEnd;
        // 마지막 저장 뒤 행동이 있었다(MatchSaveSeconds마다 저장).
        internal bool SaveDirty;
        internal double LastSaved;
        // 서버를 다시 켜 이어 연 매치가 꺼지기 전에 이미 시작했다(RoomReady를 쏜 뒤) — 정원이 돌아와도 RoomReady를 다시 쏘지 않는다.
        internal bool WasReady;
        // 방을 열 때 계정 값을 읽지 못한 계정(매치가 끝나도 계정 값을 쓰지 않는다 — 처음 값으로 덮어 재화를 잃지 않게).
        internal readonly HashSet<string> AccountUnread = new HashSet<string>(StringComparer.Ordinal);
    }

    // 매칭큐 서버: 전송 하나로 로비(입장 확인·줄 서기·수락)와 여러 방(매치)을 돌린다. 엔진 없는 코어라 어떤 서버 프로그램에서도 같다.
    //   var hub = new NetHub(new WebSocketServerTransport(7777), room => 새 세션, new FakeAuthenticator(), new LocalMatchmaker { PlayersPerMatch = 2 }, new FilePlayerStore(폴더));
    //   hub.RoomStarting += room => { room.Server.Validate = 차례 검사; …플레이어별 키·공개 범위… };
    //   hub.RoomReady += room => room.Session.Do(new StartFirstTurn());
    //   매치가 끝나면(규칙이 판정): hub.EndMatch(room, 결과);
    //   계정 값(재화 · 해금 · 로스터): DB › 플레이어 데이터의 '플레이어마다' + '매칭 계정에 남김' 칸 — 방이 열릴 때 계정에서 그 사람 몫으로 읽고,
    //   매치가 끝나면(EndMatch) 계정에 다시 쓴다. 규칙은 다른 플레이어 데이터처럼 읽고 쓴다(클라이언트는 고칠 수 없다). 취소된 매치는 쓰지 않는다.
    //   매 틱: hub.Update(deltaTime);
    // 지키는 것: 한 계정은 한 줄·한 매치에만(다시 접속하면 그 매치로) · 전원 수락 뒤에만 방을 만든다 · 수락하지 않으면 남은 사람은 기다린 시간 그대로 다시 줄 ·
    //            정원이 다 들어와야 RoomReady · 계정·점수는 서버 저장소에만.
    public sealed class NetHub : IDisposable
    {
        enum LobbyState
        {
            Authenticating,
            Lobby,
            Queued,
            Proposed,
            InRoom,
        }

        sealed class Lobbyist
        {
            public int Connection;
            public NetHello Hello;
            public byte[] HelloBytes;
            public NetAccount Account;
            public LobbyState State;
            public NetRoom Room;
            public string ProposalId;
            public double LastHeard;
            // 로비 메시지 수 세기(초당 LobbyMessagesPerSecond — 넘친 것은 버리고 네 배면 끊는다).
            public double WindowStart;
            public int WindowCount;
        }

        sealed class Proposal
        {
            public NetMatchProposal Match;
            public double Deadline;
            public readonly HashSet<string> Accepted = new HashSet<string>(StringComparer.Ordinal);
        }

        internal sealed class RoomTransport : INetServerTransport
        {
            readonly NetHub _hub;
            readonly NetRoom _room;
            readonly Queue<NetEvent> _inbox = new Queue<NetEvent>();

            public RoomTransport(NetHub hub, NetRoom room)
            {
                _hub = hub;
                _room = room;
            }

            public string Address => _hub.Address;
            public void Start() { }
            public void Send(int connection, byte[] data, int length, bool reliable = true) => _hub._transport.Send(connection, data, length, reliable);
            public void Disconnect(int connection, string reason = null) => _hub.RoomDisconnect(_room, connection, reason);
            public bool Poll(out NetEvent e)
            {
                if (_inbox.Count > 0)
                {
                    e = _inbox.Dequeue();
                    return true;
                }
                e = default;
                return false;
            }
            internal void Enqueue(NetEvent e) => _inbox.Enqueue(e);
            public void Dispose() { }
        }

        readonly INetServerTransport _transport;
        readonly Func<NetRoom, GameSession> _createSession;
        readonly INetAuthenticator _auth;
        readonly INetMatchmaker _matchmaker;
        readonly INetPlayerStore _store;
        readonly Dictionary<int, Lobbyist> _lobby = new Dictionary<int, Lobbyist>();
        readonly Dictionary<string, Proposal> _proposals = new Dictionary<string, Proposal>(StringComparer.Ordinal);
        readonly List<NetRoom> _rooms = new List<NetRoom>();
        readonly ConcurrentQueue<(int connection, NetAdmission admission)> _admissions = new ConcurrentQueue<(int, NetAdmission)>();
        readonly Stopwatch _clock = Stopwatch.StartNew();
        bool _started;

        public NetHub(INetServerTransport transport, Func<NetRoom, GameSession> createSession, INetAuthenticator auth = null,
                      INetMatchmaker matchmaker = null, INetPlayerStore store = null, NetHubOptions options = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _createSession = createSession ?? throw new ArgumentNullException(nameof(createSession));
            _auth = auth ?? new FakeAuthenticator();
            _matchmaker = matchmaker ?? new LocalMatchmaker();
            _store = store;
            Options = options ?? new NetHubOptions();
        }

        public NetHubOptions Options { get; }
        public INetMatchmaker Matchmaker => _matchmaker;
        public IReadOnlyList<NetRoom> Rooms => _rooms;
        public int LobbyCount => _lobby.Values.Count(l => l.State != LobbyState.InRoom);
        public string Address => _transport.Address;
        // 테스트가 시간을 정할 때(비우면 실제 시계).
        public Func<double> Clock;
        double Now => Clock?.Invoke() ?? _clock.Elapsed.TotalSeconds;

        // 방이 만들어졌다(세션·자리 예약 끝, 아직 아무도 안 들어옴): 서버 규칙(Validate)·플레이어별 키·공개 범위를 건다.
        public event Action<NetRoom> RoomStarting;
        // 정원이 모두 들어왔다: 첫 턴 시작 등.
        public event Action<NetRoom> RoomReady;
        public event Action<NetRoom> RoomEnded;
        // 기록(콘솔 등에 남길 글).
        public event Action<string> Log;

        public void Start()
        {
            if (_started) return;
            _transport.Start();
            _started = true;
            Say($"매칭큐 서버를 열었습니다({_transport.Address}).");
        }

        // ── 매 틱 ──────────────────────────────────────────────

        public void Update(double deltaTime)
        {
            if (!_started) return;
            while (_transport.Poll(out var e)) Route(e);
            while (_admissions.TryDequeue(out var done)) Admitted(done.connection, done.admission);
            var now = Now;
            DropSilent(now);
            _matchmaker.Tick(now);
            while (_matchmaker.TryTake(out var match)) Propose(match, now);
            foreach (var proposal in _proposals.Values.Where(p => now >= p.Deadline).ToList()) Cancel(proposal, "수락 시간이 지났습니다.");
            foreach (var room in _rooms.ToList()) UpdateRoom(room, deltaTime, now);
            foreach (var room in _rooms.Where(r => r.PendingEnd != null).ToList()) CloseRoom(room, room.PendingEnd, null);
        }

        void Route(NetEvent e)
        {
            switch (e.Kind)
            {
                case NetEventKind.Connected:
                    _lobby[e.Connection] = new Lobbyist { Connection = e.Connection, State = LobbyState.Authenticating, LastHeard = Now };
                    break;
                case NetEventKind.Disconnected:
                    Lost(e.Connection, e.Reason);
                    break;
                case NetEventKind.Data:
                    if (!_lobby.TryGetValue(e.Connection, out var who)) return;
                    who.LastHeard = Now;
                    if (who.State == LobbyState.InRoom && who.Room?.Transport != null)
                    {
                        who.Room.Transport.Enqueue(e);
                        return;
                    }
                    if (!LobbyBudget(who)) return;
                    try { HandleLobby(who, e.Data, e.Length); }
                    catch (Exception ex) { Say($"로비 메시지를 처리하지 못했습니다(#{e.Connection}): {ex.Message}"); }
                    break;
            }
        }

        // 로비 연결 하나가 1초에 보낼 수 있는 메시지 수(줄 서기·수락·핑뿐이라 넉넉하다). 방 안의 메시지는 방의 서버 중개가 센다.
        const int LobbyMessagesPerSecond = 60;

        bool LobbyBudget(Lobbyist who)
        {
            var now = Now;
            if (now - who.WindowStart >= 1)
            {
                who.WindowStart = now;
                who.WindowCount = 0;
            }
            if (++who.WindowCount <= LobbyMessagesPerSecond) return true;
            if (who.WindowCount == LobbyMessagesPerSecond * 4)
            {
                Say($"#{who.Connection}이(가) 로비에서 1초에 메시지를 {LobbyMessagesPerSecond * 4}개 넘게 보내 끊었습니다.");
                _transport.Disconnect(who.Connection, "메시지를 너무 많이 보냈습니다.");
                Lost(who.Connection, "메시지를 너무 많이 보냈습니다.");
            }
            return false;
        }

        // 로비·줄·수락 대기 중에 아무것도 오지 않은 지 오래된 연결을 끊는다(대기표도 함께 없어진다). 방 안은 방의 서버 중개가 끊는다.
        void DropSilent(double now)
        {
            if (Options.TimeoutSeconds <= 0) return;
            foreach (var who in _lobby.Values.Where(l => l.State != LobbyState.InRoom && now - l.LastHeard > Options.TimeoutSeconds).ToList())
            {
                _transport.Disconnect(who.Connection, "응답이 없어 끊었습니다.");
                Lost(who.Connection, "응답 없음");
            }
        }

        void HandleLobby(Lobbyist who, byte[] data, int length)
        {
            var r = new MirrorReader(data, 1, length);
            switch (NetProtocol.KindOf(data, length))
            {
                case NetMessage.Hello when who.State == LobbyState.Authenticating && who.Hello == null:
                {
                    var version = (int)r.UVar();
                    var hello = new NetHello { Name = NetProtocol.CleanName(r.Str()), Token = r.Str(), GameVersion = r.Str(), Connection = who.Connection };
                    if (version != NetProtocol.Version) { Refuse(who, $"멀티플레이 규격 버전이 다릅니다(서버 {NetProtocol.Version}, 클라이언트 {version})."); return; }
                    if (!string.IsNullOrEmpty(Options.GameVersion) && hello.GameVersion != Options.GameVersion) { Refuse(who, $"게임 버전이 다릅니다(서버 {Options.GameVersion}, 클라이언트 {hello.GameVersion})."); return; }
                    who.Hello = hello;
                    who.HelloBytes = new byte[length];
                    Buffer.BlockCopy(data, 0, who.HelloBytes, 0, length);
                    var connection = who.Connection;
                    try { _auth.Authenticate(hello, admission => _admissions.Enqueue((connection, admission))); }
                    catch (Exception e) { _admissions.Enqueue((connection, NetAdmission.Refuse("입장 확인 중 오류: " + e.Message))); }
                    break;
                }
                case NetMessage.Queue when who.State == LobbyState.Lobby:
                {
                    var mode = r.AtEnd ? "" : r.Str();
                    _matchmaker.Enqueue(new NetTicket { Id = who.Account.id, Account = who.Account.id, Name = who.Account.name, Rating = who.Account.rating, Mode = mode ?? "", QueuedAt = Now });
                    who.State = LobbyState.Queued;
                    Send(who.Connection, NetMessage.Queued, w => w.Str(mode ?? ""));
                    break;
                }
                case NetMessage.CancelQueue when who.State == LobbyState.Queued:
                    _matchmaker.Cancel(who.Account.id);
                    who.State = LobbyState.Lobby;
                    SendLobby(who);
                    break;
                case NetMessage.MatchAnswer when who.State == LobbyState.Proposed:
                {
                    var matchId = r.Str();
                    var accept = r.Bool();
                    if (matchId != who.ProposalId || !_proposals.TryGetValue(matchId, out var proposal)) return;
                    if (!accept) { Cancel(proposal, $"{who.Account.name}이(가) 수락하지 않았습니다.", decliner: who.Account.id); return; }
                    proposal.Accepted.Add(who.Account.id);
                    if (proposal.Match.Tickets.All(t => proposal.Accepted.Contains(t.Account))) CreateRoom(proposal);
                    break;
                }
                case NetMessage.Ping:
                    Send(who.Connection, NetMessage.Pong, w => w.Double(r.Double()));
                    break;
                case NetMessage.Bye:
                    _transport.Disconnect(who.Connection, "나갔습니다.");
                    Lost(who.Connection, "나갔습니다.");
                    break;
            }
        }

        // 같은 계정의 옛 연결이 이만큼(초) 조용하면 끊긴 것으로 본다(핑 간격 2초 + 여유).
        const double TakeoverSilence = 3;

        void Admitted(int connection, NetAdmission admission)
        {
            if (!_lobby.TryGetValue(connection, out var who) || who.State != LobbyState.Authenticating) return;
            if (!admission.Accepted) { Refuse(who, admission.Reason ?? "입장이 거절됐습니다."); return; }
            var id = string.IsNullOrEmpty(admission.Account) ? "name:" + who.Hello.Name : admission.Account;
            // 다시 접속: 옛 연결이 한동안 조용하면 끊긴 것으로 보고 새 연결이 이어받는다(진행 중인 매치면 그 자리로).
            // 옛 연결이 아직 살아 있으면 거절하지 않고 끊기만 한다 — 클라이언트가 잠시 뒤 다시 시도한다.
            var old = _lobby.Values.FirstOrDefault(l => l != who && l.Account?.id == id);
            if (old != null)
            {
                if (Now - old.LastHeard < TakeoverSilence)
                {
                    _lobby.Remove(who.Connection);
                    _transport.Disconnect(who.Connection, "같은 계정이 아직 연결되어 있습니다 — 잠시 뒤 다시 접속합니다.");
                    return;
                }
                _transport.Disconnect(old.Connection, "같은 계정이 다시 접속했습니다.");
                Lost(old.Connection, "같은 계정이 다시 접속했습니다.");
            }
            NetAccount account;
            try
            {
                account = _store?.Load(id) ?? new NetAccount { id = id };
                account.name = !string.IsNullOrEmpty(admission.Name) ? admission.Name : string.IsNullOrEmpty(who.Hello.Name) ? account.name ?? "플레이어" : who.Hello.Name;
                _store?.Save(account);
            }
            catch (Exception e)
            {
                // 저장소가 실패해도(디스크·이름 문제) 이 틱의 다른 방·매칭은 계속 돈다 — 이 사람만 거절한다.
                Say($"계정 {id}을(를) 불러오거나 저장하지 못했습니다: {e.Message}");
                Refuse(who, "계정을 불러오지 못했습니다. 잠시 뒤 다시 접속하세요.");
                return;
            }
            who.Account = account;
            // 진행 중인 매치가 있으면(끊겼다 다시 옴) 그 방으로.
            var room = _rooms.FirstOrDefault(rm => !rm.Ended && !rm.Ending && rm.Accounts.Contains(id));
            if (room != null)
            {
                Say($"{account.name}이(가) 매치 {room.MatchId}에 다시 들어갑니다.");
                EnterRoom(who, room);
                return;
            }
            who.State = LobbyState.Lobby;
            SendLobby(who);
        }

        void Lost(int connection, string reason)
        {
            if (!_lobby.TryGetValue(connection, out var who)) return;
            _lobby.Remove(connection);
            switch (who.State)
            {
                case LobbyState.Queued:
                    _matchmaker.Cancel(who.Account.id);
                    break;
                case LobbyState.Proposed:
                    if (who.ProposalId != null && _proposals.TryGetValue(who.ProposalId, out var proposal))
                        Cancel(proposal, $"{who.Account?.name}이(가) 나갔습니다.", decliner: who.Account?.id);
                    break;
                case LobbyState.InRoom:
                    who.Room?.Transport?.Enqueue(new NetEvent { Kind = NetEventKind.Disconnected, Connection = connection, Reason = reason });
                    break;
            }
        }

        // ── 매치 성사 · 수락 ───────────────────────────────────

        void Propose(NetMatchProposal match, double now)
        {
            var members = match.Tickets.Select(t => _lobby.Values.FirstOrDefault(l => l.Account?.id == t.Account && l.State == LobbyState.Queued)).ToList();
            if (members.Any(m => m == null))
            {
                // 그 사이 나간 사람이 있으면 남은 사람만 다시 줄(기다린 시간 그대로).
                foreach (var ticket in match.Tickets)
                    if (_lobby.Values.Any(l => l.Account?.id == ticket.Account && l.State == LobbyState.Queued)) _matchmaker.Enqueue(ticket);
                return;
            }
            var proposal = new Proposal { Match = match, Deadline = now + Math.Max(1, Options.AcceptSeconds) };
            _proposals[match.MatchId] = proposal;
            foreach (var member in members)
            {
                member.State = LobbyState.Proposed;
                member.ProposalId = match.MatchId;
                Send(member.Connection, NetMessage.MatchFound, w =>
                {
                    w.Str(match.MatchId);
                    w.Str(match.Mode ?? "");
                    NetProtocol.WriteStrings(w, match.Tickets.Select(t => t.Name).ToList());
                    w.Double(Options.AcceptSeconds);
                });
            }
            Say($"매치 {match.MatchId} 성사: {string.Join(", ", match.Tickets.Select(t => t.Name))} — 수락을 기다립니다.");
        }

        void Cancel(Proposal proposal, string reason, string decliner = null)
        {
            _proposals.Remove(proposal.Match.MatchId);
            foreach (var ticket in proposal.Match.Tickets)
            {
                var member = _lobby.Values.FirstOrDefault(l => l.Account?.id == ticket.Account && l.ProposalId == proposal.Match.MatchId);
                if (member == null) continue;
                member.ProposalId = null;
                // 수락한 사람은 다시 줄(기다린 시간 그대로), 거절·시간 넘김은 로비로.
                var requeue = ticket.Account != decliner && proposal.Accepted.Contains(ticket.Account);
                if (requeue)
                {
                    member.State = LobbyState.Queued;
                    _matchmaker.Enqueue(ticket);
                }
                else member.State = LobbyState.Lobby;
                Send(member.Connection, NetMessage.MatchCancelled, w =>
                {
                    w.Str(reason ?? "");
                    w.Bool(requeue);
                });
                if (!requeue) SendLobby(member);
            }
            Say($"매치 {proposal.Match.MatchId} 취소: {reason}");
        }

        void CreateRoom(Proposal proposal)
        {
            _proposals.Remove(proposal.Match.MatchId);
            var match = proposal.Match;
            var room = new NetRoom { MatchId = match.MatchId, Mode = match.Mode, CreatedAt = Now };
            room.Accounts.AddRange(match.Tickets.Select(t => t.Account));
            if (!OpenRoom(room, match.Tickets.Select(t => (t.Account, t.Name)).ToList(), null))
            {
                // 방을 열지 못했다(세션 만들기 실패): 수락한 사람들을 로비로 돌려보낸다(없어진 매치의 수락 대기에 남지 않게).
                // 다시 줄을 세우지 않는다 — 곧바로 같은 실패가 되풀이된다.
                proposal.Accepted.Clear();
                Cancel(proposal, "매치를 시작하지 못했습니다(서버 오류).");
                return;
            }
            foreach (var ticket in match.Tickets)
            {
                var member = _lobby.Values.FirstOrDefault(l => l.Account?.id == ticket.Account);
                if (member != null) EnterRoom(member, room);
            }
        }

        // 방을 연다. savedRun이 있으면 그 판으로(서버를 다시 켰을 때 이어 하기).
        // savedAccounts: 이어 열 때 저장해 둔 계정 값(플레이어 번호 → 키 → JSON). 없으면 계정 저장소에서 읽는다.
        bool OpenRoom(NetRoom room, List<(string account, string name)> players, string savedRun, Dictionary<string, object> savedAccounts = null)
        {
            room.Transport = new RoomTransport(this, room);
            GameSession session;
            try { session = _createSession(room); }
            catch (Exception e)
            {
                Say($"매치 {room.MatchId}의 세션을 만들지 못했습니다: {e.Message}");
                return false;
            }
            room.Server = new NetServer(session, room.Transport, new NetServerOptions
            {
                MaxPlayers = players.Count, GameVersion = Options.GameVersion, DriveClock = true, AllowDeveloperCommands = Options.AllowDeveloperCommands,
                TimeoutSeconds = Options.TimeoutSeconds, ByeOnClose = false,
            });
            room.Server.Clock = () => Now;
            room.Server.Admit = hello =>
            {
                var who = _lobby.TryGetValue(hello.Connection, out var l) ? l : null;
                return who?.Account != null && room.Accounts.Contains(who.Account.id)
                    ? NetAdmission.Accept(who.Account.id, who.Account.name)
                    : NetAdmission.Refuse("이 매치의 플레이어가 아닙니다.");
            };
            foreach (var (account, name) in players) room.Server.Reserve(account, name);
            if (savedRun == null || savedAccounts == null) ReadAccounts(room);
            else foreach (var player in room.Server.Players) session.Data.AddPlayer(player.Id);
            if (savedRun != null) session.ImportRun(savedRun);
            if (savedRun != null && savedAccounts != null) RestoreAccounts(room, savedAccounts);
            room.Server.Start();
            _rooms.Add(room);
            if (Options.SaveMatchEachAction && _store != null)
                session.Actions.Executed += record =>
                {
                    if (record.Nested || record.Status != ActionStatus.Done || room.Ended) return;
                    if (Options.MatchSaveSeconds <= 0) SaveRoom(room);
                    else room.SaveDirty = true;
                };
            try { RoomStarting?.Invoke(room); }
            catch (Exception e) { Say($"RoomStarting 처리 중 오류(매치 {room.MatchId}): {e.Message}"); }
            SaveRoom(room);
            Say($"매치 {room.MatchId} 방을 열었습니다.");
            return true;
        }

        void EnterRoom(Lobbyist who, NetRoom room)
        {
            who.State = LobbyState.InRoom;
            who.Room = room;
            who.ProposalId = null;
            room.Transport.Enqueue(new NetEvent { Kind = NetEventKind.Connected, Connection = who.Connection });
            room.Transport.Enqueue(new NetEvent { Kind = NetEventKind.Data, Connection = who.Connection, Data = who.HelloBytes, Length = who.HelloBytes.Length });
        }

        void UpdateRoom(NetRoom room, double deltaTime, double now)
        {
            try { room.Server.Update(deltaTime); }
            catch (Exception e) { Say($"매치 {room.MatchId} 처리 중 오류: {e.Message}"); }
            if (room.Ended) return;
            if (room.SaveDirty && now - room.LastSaved >= Options.MatchSaveSeconds) SaveRoom(room);
            var players = room.Server.Players;
            if (!room.Ready && players.Count == room.Accounts.Count && players.All(p => p.Connected))
            {
                room.Ready = true;
                // 이어 연 매치가 이미 시작했던 것이면 첫 턴 시작(RoomReady)을 다시 하지 않는다 — 저장된 판에서 이어 간다.
                if (!room.WasReady)
                {
                    try { RoomReady?.Invoke(room); }
                    catch (Exception e) { Say($"RoomReady 처리 중 오류(매치 {room.MatchId}): {e.Message}"); }
                }
                room.Server.Flush();
                SaveRoom(room);
            }
            else if (!room.Ready && now - room.CreatedAt > Math.Max(1, Options.ReadySeconds))
            {
                Say($"매치 {room.MatchId}: 정원이 다 들어오지 않아 없던 것으로 합니다.");
                CloseRoom(room, null, "다른 플레이어가 들어오지 않아 매치를 취소했습니다.");
            }
        }

        // ── 매치 끝 ────────────────────────────────────────────

        // 규칙이 판정한 결과로 매치를 끝낸다: 점수(Elo)·전적을 저장하고, 결과를 알리고, 플레이어를 로비로 돌려보낸다.
        // 다음 Update에서 처리한다 — 규칙의 이벤트 처리(행동 안·뒤) 어디서 불러도 그 행동이 다 끝나고 마지막 바뀐 곳이 간 뒤에 닫힌다.
        public void EndMatch(NetRoom room, IEnumerable<NetMatchResult> results)
        {
            if (room == null || room.Ended || room.Ending || room.PendingEnd != null) return;
            room.PendingEnd = (results ?? Enumerable.Empty<NetMatchResult>()).ToList();
        }

        void CloseRoom(NetRoom room, List<NetMatchResult> results, string cancelReason)
        {
            room.Ending = true;
            // 결과는 한 번만 반영한다: 반영하기 전에 기다리던 결과를 비우고 진행 중 목록에서 뺀다. 아래에서 저장이 실패해도
            // 다음 틱(PendingEnd)이나 서버를 다시 켠 뒤(ResumeOpenMatches)에 같은 결과가 이미 저장한 계정에 또 들어가지 않는다.
            room.PendingEnd = null;
            try { _store?.CloseMatch(room.MatchId); }
            catch (Exception e) { Say($"매치 {room.MatchId}을(를) 진행 중 목록에서 빼지 못했습니다: {e.Message}"); }
            var outcome = new Dictionary<string, (int place, double rating, double delta)>(StringComparer.Ordinal);
            if (results != null)
            {
                var rated = new List<(NetAccount account, int place)>();
                var playerOf = new Dictionary<string, NetPlayer>(StringComparer.Ordinal);
                var counted = new HashSet<string>(StringComparer.Ordinal);
                // 저장하지 않은 계정(읽지 못함·저장 실패): 등수만 알리고 점수는 저장소에 남은 그대로.
                var unsaved = new HashSet<string>(StringComparer.Ordinal);
                foreach (var result in results)
                {
                    var player = room.Server.FindPlayer(result.Player);
                    // 같은 사람이 결과에 두 번 들어와도 한 번만 센다(점수가 두 번 바뀌거나 표가 겹쳐 멈추지 않게).
                    if (player == null || !counted.Add(player.Account ?? "")) continue;
                    var live = _lobby.Values.FirstOrDefault(l => l.Account?.id == player.Account)?.Account;
                    NetAccount account;
                    try { account = _store?.Load(player.Account) ?? live ?? new NetAccount { id = player.Account, name = player.Name }; }
                    catch (Exception e)
                    {
                        // 읽지 못한 계정은 다른 사람의 점수 계산에만 넣고 저장하지 않는다(옛 값으로 덮어 재화·구매를 잃지 않게).
                        Say($"매치 {room.MatchId}: 계정 {player.Account}을(를) 읽지 못해 이 계정의 결과는 저장하지 않습니다: {e.Message}");
                        account = new NetAccount { id = player.Account, name = player.Name };
                        if (live != null) account.rating = live.rating;
                        unsaved.Add(account.id);
                    }
                    rated.Add((account, Math.Max(1, result.Place)));
                    playerOf[account.id] = player;
                }
                var before = rated.ToDictionary(r => r.account.id, r => r.account.rating, StringComparer.Ordinal);
                NetRating.Apply(rated);
                foreach (var (account, place) in rated)
                {
                    if (!unsaved.Contains(account.id))
                    {
                        WriteAccountValues(room, playerOf.TryGetValue(account.id, out var owner) ? owner : null, account);
                        try { _store?.Save(account); }
                        catch (Exception e)
                        {
                            // 한 계정이 실패해도 나머지 계정·방 닫기는 이어 간다(이 결과는 다시 반영하지 않는다).
                            Say($"매치 {room.MatchId}: 계정 {account.id}의 결과(점수·전적)를 저장하지 못했습니다: {e.Message}");
                            unsaved.Add(account.id);
                        }
                    }
                    if (unsaved.Contains(account.id))
                    {
                        outcome[account.id] = (place, before[account.id], 0);
                        continue;
                    }
                    outcome[account.id] = (place, account.rating, account.rating - before[account.id]);
                    var holder = _lobby.Values.FirstOrDefault(l => l.Account?.id == account.id);
                    if (holder != null) holder.Account = account;
                }
                // 결과에 없는 사람(등수 없음)의 계정 값도 쓴다 — 매치는 끝났으니 그동안 바뀐 재화 · 해금은 남는다.
                if (_store != null && room.Session.Data.AccountKeys.Any())
                    foreach (var player in room.Server.Players)
                    {
                        if (string.IsNullOrEmpty(player.Account) || counted.Contains(player.Account)) continue;
                        try
                        {
                            var account = _store.Load(player.Account) ?? new NetAccount { id = player.Account, name = player.Name };
                            WriteAccountValues(room, player, account);
                            _store.Save(account);
                            var holder = _lobby.Values.FirstOrDefault(l => l.Account?.id == account.id);
                            if (holder != null) holder.Account = account;
                        }
                        catch (Exception e)
                        {
                            Say($"매치 {room.MatchId}: 계정 {player.Account}의 계정 값을 저장하지 못했습니다: {e.Message}");
                        }
                    }
            }
            foreach (var who in _lobby.Values.Where(l => l.Room == room).ToList())
            {
                var has = outcome.TryGetValue(who.Account.id, out var mine);
                Send(who.Connection, NetMessage.MatchEnded, w =>
                {
                    w.Str(room.MatchId);
                    w.Bool(cancelReason == null);
                    w.Str(cancelReason ?? "");
                    w.UVar((ulong)(has ? mine.place : 0));
                    w.Double(has ? mine.rating : who.Account.rating);
                    w.Double(has ? mine.delta : 0);
                });
                who.Room = null;
                who.State = LobbyState.Lobby;
                SendLobby(who);
            }
            room.Ended = true;
            try { room.Server.Dispose(); }
            catch (Exception e) { Say($"매치 {room.MatchId}을(를) 닫다가 오류: {e.Message}"); }
            _rooms.Remove(room);
            try { RoomEnded?.Invoke(room); }
            catch (Exception e) { Say($"RoomEnded 처리 중 오류: {e.Message}"); }
            Say(cancelReason == null ? $"매치 {room.MatchId} 끝." : $"매치 {room.MatchId} 취소: {cancelReason}");
        }

        // 방의 서버 중개가 연결을 끊으려 할 때(거절·강퇴). 방을 닫는 중이면 연결은 로비로 돌아가므로 끊지 않는다.
        internal void RoomDisconnect(NetRoom room, int connection, string reason)
        {
            if (room.Ending || room.Ended) return;
            _transport.Disconnect(connection, reason);
            _lobby.Remove(connection);
        }

        // ── 계정 값 ────────────────────────────────────────────

        // 방을 열 때: 플레이어마다 키의 몫을 만들고, '매칭 계정에 남김' 키는 그 사람 계정의 값(values[ID] = JSON)으로 채운다(없으면 처음 값).
        void ReadAccounts(NetRoom room)
        {
            var data = room.Session.Data;
            var keys = data.AccountKeys.ToList();
            foreach (var player in room.Server.Players)
            {
                data.AddPlayer(player.Id);
                if (keys.Count == 0 || _store == null || string.IsNullOrEmpty(player.Account)) continue;
                NetAccount account;
                try { account = _store.Load(player.Account); }
                catch (Exception e)
                {
                    Say($"매치 {room.MatchId}: 계정 {player.Account}을(를) 읽지 못해 계정 값을 처음 값으로 둡니다(이 매치가 끝나도 계정 값을 쓰지 않습니다): {e.Message}");
                    room.AccountUnread.Add(player.Account);
                    continue;
                }
                if (account?.values == null) continue;
                foreach (var key in keys)
                {
                    if (!account.values.TryGetValue(key, out var json) || data.ImportAccountValue(key, player.Id, json)) continue;
                    Say($"매치 {room.MatchId}: 계정 {player.Account}의 '{key}' 값을 읽지 못해 처음 값으로 둡니다(이 매치가 끝나도 계정 값을 쓰지 않습니다).");
                    room.AccountUnread.Add(player.Account);
                }
            }
        }

        // 이어 열 때: 저장해 둔 매치 안의 계정 값으로(꺼지기 전까지 바뀐 것 — 계정에는 아직 쓰지 않았다).
        void RestoreAccounts(NetRoom room, Dictionary<string, object> saved)
        {
            var data = room.Session.Data;
            foreach (var player in room.Server.Players)
            {
                var id = player.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!saved.TryGetValue(id, out var entry) || !(entry is Dictionary<string, object> values)) continue;
                foreach (var pair in values)
                    if (pair.Value is string json && !data.ImportAccountValue(pair.Key, player.Id, json))
                    {
                        // 읽지 못한 값은 처음 값으로 남으므로, 매치가 끝나도 그 계정에 쓰지 않는다(처음 값으로 덮어 잃지 않게).
                        Say($"매치 {room.MatchId}: 저장해 둔 '{pair.Key}'(플레이어 {player.Id}) 값을 읽지 못했습니다(이 매치가 끝나도 계정 값을 쓰지 않습니다).");
                        if (!string.IsNullOrEmpty(player.Account)) room.AccountUnread.Add(player.Account);
                    }
            }
        }

        // 매치가 끝났을 때: 그 사람 몫을 계정 값으로 쓴다(방을 열 때 읽지 못한 계정은 건너뛴다).
        void WriteAccountValues(NetRoom room, NetPlayer player, NetAccount account)
        {
            if (player == null || account == null || room.AccountUnread.Contains(player.Account ?? "")) return;
            var data = room.Session?.Data;
            if (data == null) return;
            foreach (var key in data.AccountKeys)
            {
                var json = data.ExportAccountValue(key, player.Id);
                if (json == null) continue;
                account.values ??= new Dictionary<string, string>(StringComparer.Ordinal);
                account.values[key] = json;
            }
        }

        // 매치 저장에 넣을 계정 값(플레이어 번호 → 키 → JSON).
        Dictionary<string, object> SavedAccounts(NetRoom room)
        {
            var data = room.Session.Data;
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var player in room.Server.Players)
            {
                var values = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var key in data.AccountKeys)
                {
                    var json = data.ExportAccountValue(key, player.Id);
                    if (json != null) values[key] = json;
                }
                result[player.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)] = values;
            }
            return result;
        }

        // ── 저장 · 이어 하기 ───────────────────────────────────

        void SaveRoom(NetRoom room)
        {
            if (_store == null || room.Server == null) return;
            room.SaveDirty = false;
            room.LastSaved = Now;
            try
            {
                var players = room.Server.Players.Select(p => (object)new Dictionary<string, object> { ["account"] = p.Account, ["name"] = p.Name, ["id"] = (double)p.Id }).ToList();
                var state = new Dictionary<string, object>
                {
                    ["matchId"] = room.MatchId,
                    ["mode"] = room.Mode ?? "",
                    // 이어 연 뒤 아직 정원이 돌아오지 않았어도 이미 시작한 매치다(다시 꺼졌다 켜져도 RoomReady를 다시 쏘지 않게).
                    ["ready"] = room.Ready || room.WasReady,
                    ["players"] = players,
                    ["run"] = room.Session.ExportRun(),
                    ["accounts"] = SavedAccounts(room),
                    ["accountUnread"] = room.AccountUnread.Select(a => (object)a).ToList(),
                };
                _store.SaveMatch(room.MatchId, MiniJson.Serialize(state));
            }
            catch (Exception e)
            {
                Say($"매치 {room.MatchId}을(를) 저장하지 못했습니다: {e.Message}");
            }
        }

        // 서버를 다시 켰을 때: 저장소에 남은 진행 중인 매치를 다시 연다. 플레이어가 다시 접속하면 그 매치로 들어간다. 연 방 수.
        public int ResumeOpenMatches()
        {
            if (_store == null) return 0;
            var opened = 0;
            foreach (var matchId in _store.OpenMatches().ToList())
            {
                if (_rooms.Any(r => r.MatchId == matchId)) continue;
                try
                {
                    if (!(MiniJson.Deserialize(_store.LoadMatch(matchId) ?? "") is Dictionary<string, object> state)) continue;
                    // 정원이 다시 다 들어올 때까지는 준비 전(ReadySeconds 안에 안 돌아오면 없던 것으로). 이미 시작했던 매치면 RoomReady는 다시 쏘지 않는다.
                    var room = new NetRoom
                    {
                        MatchId = matchId, Mode = state.TryGetValue("mode", out var mode) ? mode as string : "", CreatedAt = Now, Ready = false,
                        WasReady = state.TryGetValue("ready", out var ready) && ready is bool started && started,
                    };
                    var players = new List<(string, string)>();
                    if (state.TryGetValue("players", out var list) && list is List<object> items)
                        foreach (var item in items.OfType<Dictionary<string, object>>().OrderBy(p => p.TryGetValue("id", out var id) ? Convert.ToDouble(id) : 0))
                        {
                            var account = item.TryGetValue("account", out var a) ? a as string : null;
                            if (account == null) continue;
                            players.Add((account, item.TryGetValue("name", out var n) ? n as string : account));
                            room.Accounts.Add(account);
                        }
                    if (players.Count == 0) continue;
                    if (state.TryGetValue("accountUnread", out var unread) && unread is List<object> unreadList)
                        foreach (var account in unreadList.OfType<string>()) room.AccountUnread.Add(account);
                    var savedAccounts = state.TryGetValue("accounts", out var accounts) ? accounts as Dictionary<string, object> : null;
                    if (OpenRoom(room, players, state.TryGetValue("run", out var run) ? run as string : null, savedAccounts)) opened++;
                }
                catch (Exception e)
                {
                    Say($"매치 {matchId}을(를) 이어 열지 못했습니다: {e.Message}");
                }
            }
            return opened;
        }

        // ── 보내기 ─────────────────────────────────────────────

        void SendLobby(Lobbyist who)
        {
            var account = who.Account;
            Send(who.Connection, NetMessage.Lobby, w =>
            {
                w.Str(account.name ?? "");
                w.Double(account.rating);
                w.UVar((ulong)Math.Max(0, account.wins));
                w.UVar((ulong)Math.Max(0, account.losses));
                w.UVar((ulong)Math.Max(0, account.draws));
            });
        }

        void Refuse(Lobbyist who, string reason)
        {
            Send(who.Connection, NetMessage.Reject, w => w.Str(reason));
            Say($"입장 거절(#{who.Connection}): {reason}");
            _transport.Disconnect(who.Connection, reason);
            _lobby.Remove(who.Connection);
        }

        void Send(int connection, NetMessage kind, Action<MirrorWriter> write)
        {
            var w = NetProtocol.Begin(kind);
            write(w);
            _transport.Send(connection, w.Data, w.Length);
        }

        void Say(string message) => Log?.Invoke("[MomoiProject] " + message);

        public void Dispose()
        {
            foreach (var room in _rooms.ToList())
            {
                // 저장 간격 사이에 있었던 행동도 남긴다(다시 켜면 ResumeOpenMatches가 이어 연다).
                if (room.SaveDirty && !room.Ended) SaveRoom(room);
                room.Ending = true;
                try { room.Server.Dispose(); } catch (Exception) { }
            }
            _rooms.Clear();
            _transport.Dispose();
            _started = false;
        }
    }
}
