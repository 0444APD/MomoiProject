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
        // 행동마다 진행 중인 매치를 저장한다(서버가 꺼져도 ResumeOpenMatches로 이어 하기).
        public bool SaveMatchEachAction = true;
        // 방에 정원이 다 들어오지 않을 때 기다리는 시간(초). 넘기면 매치를 없던 것으로 하고(아무도 점수를 잃지 않는다) 로비로 돌려보낸다.
        public double ReadySeconds = 30;
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
    }

    // 매칭큐 서버: 전송 하나로 로비(입장 확인·줄 서기·수락)와 여러 방(매치)을 돌린다. 엔진 없는 코어라 어떤 서버 프로그램에서도 같다.
    //   var hub = new NetHub(new WebSocketServerTransport(7777), room => 새 세션, new FakeAuthenticator(), new LocalMatchmaker { PlayersPerMatch = 2 }, new FilePlayerStore(폴더));
    //   hub.RoomStarting += room => { room.Server.Validate = 차례 검사; …플레이어별 키·공개 범위… };
    //   hub.RoomReady += room => room.Session.Do(new StartFirstTurn());
    //   매치가 끝나면(규칙이 판정): hub.EndMatch(room, 결과);
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
            _matchmaker.Tick(now);
            while (_matchmaker.TryTake(out var match)) Propose(match, now);
            foreach (var proposal in _proposals.Values.Where(p => now >= p.Deadline).ToList()) Cancel(proposal, "수락 시간이 지났습니다.");
            foreach (var room in _rooms.ToList()) UpdateRoom(room, deltaTime, now);
        }

        void Route(NetEvent e)
        {
            switch (e.Kind)
            {
                case NetEventKind.Connected:
                    _lobby[e.Connection] = new Lobbyist { Connection = e.Connection, State = LobbyState.Authenticating };
                    break;
                case NetEventKind.Disconnected:
                    Lost(e.Connection, e.Reason);
                    break;
                case NetEventKind.Data:
                    if (!_lobby.TryGetValue(e.Connection, out var who)) return;
                    if (who.State == LobbyState.InRoom && who.Room?.Transport != null)
                    {
                        who.Room.Transport.Enqueue(e);
                        return;
                    }
                    try { HandleLobby(who, e.Data, e.Length); }
                    catch (Exception ex) { Say($"로비 메시지를 처리하지 못했습니다(#{e.Connection}): {ex.Message}"); }
                    break;
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
                    var hello = new NetHello { Name = r.Str(), Token = r.Str(), GameVersion = r.Str(), Connection = who.Connection };
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

        void Admitted(int connection, NetAdmission admission)
        {
            if (!_lobby.TryGetValue(connection, out var who) || who.State != LobbyState.Authenticating) return;
            if (!admission.Accepted) { Refuse(who, admission.Reason ?? "입장이 거절됐습니다."); return; }
            var id = string.IsNullOrEmpty(admission.Account) ? "name:" + who.Hello.Name : admission.Account;
            if (_lobby.Values.Any(l => l != who && l.Account?.id == id)) { Refuse(who, "같은 계정이 이미 들어와 있습니다."); return; }
            var account = _store?.Load(id) ?? new NetAccount { id = id };
            account.name = !string.IsNullOrEmpty(admission.Name) ? admission.Name : string.IsNullOrEmpty(who.Hello.Name) ? account.name ?? "플레이어" : who.Hello.Name;
            _store?.Save(account);
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
            if (!OpenRoom(room, match.Tickets.Select(t => (t.Account, t.Name)).ToList(), null)) return;
            foreach (var ticket in match.Tickets)
            {
                var member = _lobby.Values.FirstOrDefault(l => l.Account?.id == ticket.Account);
                if (member != null) EnterRoom(member, room);
            }
        }

        // 방을 연다. savedRun이 있으면 그 판으로(서버를 다시 켰을 때 이어 하기).
        bool OpenRoom(NetRoom room, List<(string account, string name)> players, string savedRun)
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
            });
            room.Server.Admit = hello =>
            {
                var who = _lobby.TryGetValue(hello.Connection, out var l) ? l : null;
                return who?.Account != null && room.Accounts.Contains(who.Account.id)
                    ? NetAdmission.Accept(who.Account.id, who.Account.name)
                    : NetAdmission.Refuse("이 매치의 플레이어가 아닙니다.");
            };
            foreach (var (account, name) in players) room.Server.Reserve(account, name);
            if (savedRun != null) session.ImportRun(savedRun);
            room.Server.Start();
            _rooms.Add(room);
            if (Options.SaveMatchEachAction && _store != null)
                session.Actions.Executed += record =>
                {
                    if (!record.Nested && record.Status == ActionStatus.Done && !room.Ended) SaveRoom(room);
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
            var players = room.Server.Players;
            if (!room.Ready && players.Count == room.Accounts.Count && players.All(p => p.Connected))
            {
                room.Ready = true;
                try { RoomReady?.Invoke(room); }
                catch (Exception e) { Say($"RoomReady 처리 중 오류(매치 {room.MatchId}): {e.Message}"); }
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
        public void EndMatch(NetRoom room, IEnumerable<NetMatchResult> results)
        {
            if (room == null || room.Ended || room.Ending) return;
            var list = (results ?? Enumerable.Empty<NetMatchResult>()).ToList();
            CloseRoom(room, list, null);
        }

        void CloseRoom(NetRoom room, List<NetMatchResult> results, string cancelReason)
        {
            room.Ending = true;
            var outcome = new Dictionary<string, (int place, double rating, double delta)>(StringComparer.Ordinal);
            if (results != null)
            {
                var rated = new List<(NetAccount account, int place)>();
                foreach (var result in results)
                {
                    var player = room.Server.FindPlayer(result.Player);
                    if (player == null) continue;
                    var account = _store?.Load(player.Account) ?? _lobby.Values.FirstOrDefault(l => l.Account?.id == player.Account)?.Account ?? new NetAccount { id = player.Account, name = player.Name };
                    rated.Add((account, Math.Max(1, result.Place)));
                }
                var before = rated.ToDictionary(r => r.account.id, r => r.account.rating, StringComparer.Ordinal);
                NetRating.Apply(rated);
                foreach (var (account, place) in rated)
                {
                    _store?.Save(account);
                    outcome[account.id] = (place, account.rating, account.rating - before[account.id]);
                    var live = _lobby.Values.FirstOrDefault(l => l.Account?.id == account.id);
                    if (live != null) live.Account = account;
                }
            }
            _store?.CloseMatch(room.MatchId);
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

        // ── 저장 · 이어 하기 ───────────────────────────────────

        void SaveRoom(NetRoom room)
        {
            if (_store == null || room.Server == null) return;
            try
            {
                var players = room.Server.Players.Select(p => (object)new Dictionary<string, object> { ["account"] = p.Account, ["name"] = p.Name, ["id"] = (double)p.Id }).ToList();
                var state = new Dictionary<string, object>
                {
                    ["matchId"] = room.MatchId,
                    ["mode"] = room.Mode ?? "",
                    ["ready"] = room.Ready,
                    ["players"] = players,
                    ["run"] = room.Session.ExportRun(),
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
                    var room = new NetRoom { MatchId = matchId, Mode = state.TryGetValue("mode", out var mode) ? mode as string : "", CreatedAt = Now, Ready = false };
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
                    if (OpenRoom(room, players, state.TryGetValue("run", out var run) ? run as string : null)) opened++;
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
                room.Ending = true;
                try { room.Server.Dispose(); } catch (Exception) { }
            }
            _rooms.Clear();
            _transport.Dispose();
            _started = false;
        }
    }
}
