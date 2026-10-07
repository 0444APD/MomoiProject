using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace GBible
{
    public enum NetClientState
    {
        Idle,
        Connecting,
        // 매칭큐 서버의 로비(줄을 서지 않음).
        Lobby,
        // 매칭을 기다린다.
        Queued,
        // 매치가 성사돼 수락을 기다린다(AnswerMatch).
        MatchFound,
        // Welcome을 받았고 처음 모습을 기다린다.
        Joining,
        Joined,
        Closed,
    }

    // 클라이언트 중개(4블록의 [클라이언트 중개]): 이 세션을 '받는 쪽'으로 바꾼다(SessionRole.Client).
    //   var client = new NetClient(session, transport, "철수");
    //   client.Connect();
    //   매 프레임: client.Update(deltaTime);
    //
    // 화면 코드는 싱글과 같다:
    //   · Game.Do(행동) → 서버로 보낸다(결과는 바뀐 곳·이벤트로 온다). 서버가 거절하면 ActionFailed.
    //   · session.Data.Get·Changed·DataChanged → 서버가 보낸 값(행동 밖 변경으로 적용·알림).
    //   · 이벤트 → SubscribeAfterAction으로 받는다(서버에서 일어난 이벤트를 차례대로). Subscribe(규칙 쪽)에는 오지 않는다.
    //   · session.LocalPlayer = 서버가 준 플레이어 번호.
    public sealed class NetClient : IDisposable
    {
        readonly GameSession _session;
        readonly INetClientTransport _transport;
        readonly NetReplica _replica;
        readonly Dictionary<long, GameAction> _pending = new Dictionary<long, GameAction>();
        readonly List<NetPlayerInfo> _players = new List<NetPlayerInfo>();
        readonly Stopwatch _watch = Stopwatch.StartNew();
        long _nextRequest = 1;
        double _nextPing;
        double _lastHeard;
        double _recordStart;
        double _recordOffset;
        bool _hadRoom;

        public NetClient(GameSession session, INetClientTransport transport, string name, string token = null, string gameVersion = "")
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            if (session.Role == SessionRole.Server) throw new InvalidOperationException("[MomoiProject] 서버 세션은 클라이언트가 될 수 없습니다.");
            Name = name ?? "";
            Token = token ?? "";
            GameVersion = gameVersion ?? "";
            session.Role = SessionRole.Client;
            session.RemoteDo = Send;
            _replica = new NetReplica(session);
        }

        public GameSession Session => _session;
        public string Name { get; }
        // 입장 표(가짜 인증이면 비어도 된다 — Steam이면 인증 티켓). 접속 전에 바꿔도 된다(Hello를 보낼 때 읽는다).
        public string Token { get; set; }
        public string GameVersion { get; }
        // 서버가 거절했거나 서버가 끊었다(다시 접속해도 소용없다). 거짓인데 끊겼으면 네트워크 문제 — 다시 접속해 볼 만하다.
        public bool ServerClosed { get; private set; }
        // 서버에 닿은 적이 있다(로비나 방에 들어갔다).
        public bool Reached { get; private set; }
        // 이만큼(초) 서버에서 아무것도 오지 않으면 끊긴 것으로 본다(서버는 핑에 답하므로 살아 있으면 2초마다 온다).
        public double TimeoutSeconds = 15;
        // 들어갈 때부터 받은 것을 녹화한다(리플레이). 접속 전에 켜야 처음 모습부터 담긴다 — 들어간 뒤 켜면 다음 입장부터.
        public NetReplay Recording { get; private set; }
        // 들어갈 때(Welcome) 판 데이터를 먼저 비운다 — 다시 접속: 떨어져 있던 사이 서버에서 지워진 키가 남지 않게(이어서 처음 모습이 온다).
        public bool ClearOnJoin { get; set; }

        // 녹화를 켠다(이미 들어가 있으면 다음 Welcome부터). continueInto: 다시 접속할 때 앞 연결의 녹화에 이어 쌓는다.
        // 돌려주는 값: 녹화(계속 쌓인다).
        public NetReplay StartRecording(NetReplay continueInto = null)
        {
            Recording = continueInto ?? new NetReplay { Source = "클라이언트 " + Name };
            _recordOffset = continueInto?.Duration ?? 0;
            _recordStart = -1;
            return Recording;
        }

        public NetReplay StopRecording()
        {
            var done = Recording;
            Recording = null;
            return done;
        }
        public NetClientState State { get; private set; }
        public bool IsJoined => State == NetClientState.Joined;
        // 서버가 준 플레이어 번호(행동의 actorId). 들어가기 전에는 0.
        public int PlayerId { get; private set; }
        public IReadOnlyList<NetPlayerInfo> Players => _players;
        // 왕복 시간(초, 핑으로 잰다).
        public double RoundTrip { get; private set; }
        // 서버가 아직 결과를 보내지 않은 행동 수(화면이 '기다리는 중'을 보일 때).
        public int PendingActions => _pending.Count;
        // 핑 간격(초).
        public double PingInterval = 2;

        // 매칭큐: 내 계정(로비에 들어오면) · 기다리는 매칭 종류 · 성사된 매치(수락 전).
        public NetLobbyInfo Account { get; private set; }
        public string QueueMode { get; private set; }
        public NetMatchFound Match { get; private set; }
        // 매칭큐 상태가 바뀌었을 때(로비·줄·성사·취소).
        public event Action LobbyChanged;
        public event Action<NetMatchFound> MatchFound;
        // 성사된 매치가 취소됐을 때(이유, 다시 줄을 섰는가).
        public event Action<string, bool> MatchCancelled;
        // 매치가 끝났을 때(등수·점수 변화). 이어서 로비로 돌아온다.
        public event Action<NetMatchEnd> MatchEnded;

        // 처음 모습까지 받아 화면이 데이터를 읽을 수 있을 때.
        public event Action Joined;
        // 연결이 끝났을 때(거절·끊김·서버 종료). 이유 글.
        public event Action<string> Closed;
        // 서버가 행동을 거절했을 때(화면은 알림으로 보인다 — 예외가 아니다).
        public event Action<NetActionFailure> ActionFailed;
        public event Action PlayersChanged;
        // 다른 사람(또는 서버 = 0)이 보낸 Stream(실시간 위치 등). (보낸 사람, 채널, 바이트, 시작, 끝) — 이 호출 안에서만 쓴다.
        public event Action<int, byte, byte[], int, int> StreamReceived;

        public void Connect()
        {
            if (State != NetClientState.Idle && State != NetClientState.Closed) return;
            State = NetClientState.Connecting;
            _transport.Connect();
        }

        public void Update(double deltaTime)
        {
            while (_transport.Poll(out var e))
            {
                switch (e.Kind)
                {
                    case NetEventKind.Connected:
                        _lastHeard = Now;
                        SendHello();
                        break;
                    case NetEventKind.Disconnected:
                        Close(e.Reason ?? "연결이 끊겼습니다.");
                        break;
                    case NetEventKind.Data:
                        _lastHeard = Now;
                        Record(e.Data, e.Length);
                        try { Handle(e.Data, e.Length); }
                        catch (Exception ex)
                        {
                            _session.Journal.Append(JournalKind.Error, "Net", "서버 메시지를 처리하지 못했습니다: " + ex.Message);
                        }
                        break;
                }
            }
            if (!Connected) return;
            // 핑: 로비·줄·방 어디서든 보낸다(서버가 끊긴 연결을 알아채고, 이쪽도 서버가 살아 있는지 안다).
            if (Now >= _nextPing)
            {
                _nextPing = Now + Math.Max(0.2, PingInterval);
                var w = NetProtocol.Begin(NetMessage.Ping);
                w.Double(Now);
                _transport.Send(w.Data, w.Length);
            }
            if (TimeoutSeconds > 0 && Now - _lastHeard > TimeoutSeconds)
            {
                _transport.Disconnect("서버 응답이 없습니다.");
                Close($"서버가 {TimeoutSeconds:0}초 동안 응답하지 않았습니다.");
            }
        }

        // 서버에 닿아 있는 상태(입장 확인 중 이후).
        bool Connected => State == NetClientState.Lobby || State == NetClientState.Queued || State == NetClientState.MatchFound ||
                          State == NetClientState.Joining || State == NetClientState.Joined || (State == NetClientState.Connecting && _lastHeard > 0);

        // 녹화: 형식 번호표(Welcome)와 묶음(Batch)만. 첫 Welcome부터 쌓는다.
        void Record(byte[] data, int length)
        {
            if (Recording == null || length == 0) return;
            var kind = (NetMessage)data[0];
            if (_recordStart < 0)
            {
                if (kind != NetMessage.Welcome) return;
                _recordStart = Now;
            }
            if (kind == NetMessage.Welcome || kind == NetMessage.Batch) Recording.Add(_recordOffset + Now - _recordStart, data, length);
        }

        // 서버의 개발 명령을 실행해 달라고 한다(서버가 AllowDeveloperCommands일 때만 돈다).
        public void RunDeveloperCommand(string group, string name)
        {
            if (!IsJoined) return;
            var w = NetProtocol.Begin(NetMessage.Dev);
            w.Str(group ?? "");
            w.Str(name ?? "");
            _transport.Send(w.Data, w.Length);
        }

        // 실시간 바이트(내 캐릭터 위치 등)를 보낸다. 잃어도 되는 것만(늦게 오거나 빠질 수 있다).
        public void SendStream(byte channel, byte[] data, int start, int count)
        {
            if (!IsJoined) return;
            var w = NetProtocol.Begin(NetMessage.Stream, new MirrorWriter(count + 4));
            w.Byte(channel);
            w.Bytes(data, start, count);
            _transport.Send(w.Data, w.Length, reliable: false);
        }

        public void Disconnect()
        {
            if (State == NetClientState.Closed || State == NetClientState.Idle) return;
            var w = NetProtocol.Begin(NetMessage.Bye);
            w.Str("나갔습니다.");
            _transport.Send(w.Data, w.Length);
            _transport.Disconnect("나갔습니다.");
            Close("나갔습니다.");
        }

        // 네트워크가 끊긴 것처럼 닫는다(나간다고 알리지 않는다 — 서버는 끊김으로 안다). 다시 접속 시험용.
        public void Drop(string reason = "연결이 끊겼습니다(시험).")
        {
            if (State == NetClientState.Closed || State == NetClientState.Idle) return;
            _transport.Disconnect(reason);
            Close(reason);
        }

        public void Dispose()
        {
            Disconnect();
            _transport.Dispose();
            if (_session.Role == SessionRole.Client)
            {
                _session.Role = SessionRole.Local;
                _session.RemoteDo = null;
            }
        }

        // 시계(초). 비우면 실제 시계(테스트가 정한다).
        public Func<double> Clock;
        double Now => Clock?.Invoke() ?? _watch.Elapsed.TotalSeconds;

        // ── 보내기 ─────────────────────────────────────────────

        void SendHello()
        {
            var w = NetProtocol.Begin(NetMessage.Hello);
            w.UVar((ulong)NetProtocol.Version);
            w.Str(Name);
            w.Str(Token);
            w.Str(GameVersion);
            NetProtocol.WriteStrings(w, Mirror.TypeTable());
            _transport.Send(w.Data, w.Length);
        }

        // session.Do가 부른다: 행동을 서버로 보낸다(대상 객체는 id만).
        // 생성 코드가 온전히 쓰지 못하는 행동(중첩 컬렉션·쓸 수 없는 구조체·생성 코드가 없는 값·readonly 필드 …)은 보내지 않는다 —
        // 서버에 빈 칸으로 도착하거나 쓰다가 멈추는 대신, 어느 필드인지 밝힌 오류(기록 + 예외)로 실패한다.
        ActionRecord Send(GameAction action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (!IsJoined)
            {
                _session.Journal.Append(JournalKind.Warning, "Net", $"서버에 들어가기 전이라 '{action.GetType().Name}'을(를) 보내지 못했습니다.");
                return new ActionRecord { Action = action, Status = ActionStatus.Failed, Error = "서버에 연결되어 있지 않습니다." };
            }
            // 형식 검사(형식마다 처음 보낼 때 한 번 — 결과는 Mirror.CanSend가 기억한다). 참조 필드를 id로 바꾸기(Detach) 전에 본다.
            if (!Mirror.CanSend(action.GetType(), out var reason)) throw Unsendable(action, reason, null);
            ActionReferences.Detach(action);
            var body = new MirrorWriter();
            // object·추상·인터페이스 칸의 실제 값은 쓰는 동안 객체마다 본다(CheckValue).
            try { Mirror.WriteNetValue(body, action, CheckValue); }
            catch (Exception e) { throw Unsendable(action, e.Message, e); }
            var request = _nextRequest++;
            var w = NetProtocol.Begin(NetMessage.Act, new MirrorWriter(body.Length + 16));
            w.UVar((ulong)request);
            NetProtocol.WriteBlock(w, body);
            _transport.Send(w.Data, w.Length);
            _pending[request] = action;
            var entry = _session.Journal.Append(JournalKind.Action, "Net", "보냄: " + SafeDescribe(action));
            entry.Payload = action;
            return new ActionRecord { Action = action, Status = ActionStatus.Sent, JournalId = entry.Id };
        }

        static string SafeDescribe(GameAction action)
        {
            try { return action.Describe(); }
            catch (Exception) { return action.GetType().Name; }
        }

        // 행동을 쓰는 동안 객체마다(Mirror.WriteNetValue): 보낼 수 없는 값이면 예외(Send가 행동 이름을 붙인다).
        // 서버가 가진 객체를 번호로 가리키지 않으므로(참조 필드는 id만 든 객체로 바뀌어 있다) 언제나 null.
        static readonly Func<object, MirrorNode> CheckValue = value => Mirror.CanSendValue(value, out var reason) ? (MirrorNode)null : throw new InvalidOperationException(reason);

        // 보낼 수 없다고 알린 행동 형식(기록에는 형식마다 한 번 — 예외는 보낼 때마다).
        readonly HashSet<Type> _unsendable = new HashSet<Type>();

        Exception Unsendable(GameAction action, string reason, Exception inner)
        {
            var type = action.GetType();
            var message = $"행동 '{type.Name}'을(를) 서버로 보낼 수 없습니다: {reason}";
            if (_unsendable.Add(type)) _session.Journal.Append(JournalKind.Error, "Net", message);
            return new InvalidOperationException("[MomoiProject] " + message, inner);
        }

        // ── 받기 ───────────────────────────────────────────────

        void Handle(byte[] data, int length)
        {
            var r = new MirrorReader(data, 1, length);
            switch (NetProtocol.KindOf(data, length))
            {
                case NetMessage.Welcome:
                    OnWelcome(r);
                    break;
                case NetMessage.Reject:
                    ServerClosed = true;
                    Close("입장 거절: " + r.Str());
                    break;
                case NetMessage.Batch:
                    OnBatch(r);
                    break;
                case NetMessage.ActResult:
                    OnResult(r);
                    break;
                case NetMessage.Players:
                    ReadPlayers(r);
                    PlayersChanged?.Invoke();
                    break;
                case NetMessage.Pong:
                    RoundTrip = Math.Max(0, Now - r.Double());
                    break;
                case NetMessage.Stream:
                {
                    var from = (int)r.UVar();
                    var channel = r.Byte();
                    StreamReceived?.Invoke(from, channel, r.Data, r.Position, r.End);
                    break;
                }
                case NetMessage.Bye:
                    ServerClosed = true;
                    Close(r.AtEnd ? "서버가 연결을 끊었습니다." : r.Str());
                    break;
                case NetMessage.Lobby:
                    OnLobby(r);
                    break;
                case NetMessage.Queued:
                    State = NetClientState.Queued;
                    QueueMode = r.AtEnd ? "" : r.Str();
                    LobbyChanged?.Invoke();
                    break;
                case NetMessage.MatchFound:
                {
                    var found = new NetMatchFound { MatchId = r.Str(), Mode = r.Str(), Players = NetProtocol.ReadStrings(r), Seconds = r.Double() };
                    Match = found;
                    State = NetClientState.MatchFound;
                    MatchFound?.Invoke(found);
                    LobbyChanged?.Invoke();
                    break;
                }
                case NetMessage.MatchCancelled:
                {
                    var reason = r.Str();
                    var requeued = r.Bool();
                    Match = null;
                    if (requeued) State = NetClientState.Queued;
                    MatchCancelled?.Invoke(reason, requeued);
                    LobbyChanged?.Invoke();
                    break;
                }
                case NetMessage.MatchEnded:
                {
                    var ended = new NetMatchEnd { MatchId = r.Str(), Finished = r.Bool(), Reason = r.Str(), Place = (int)r.UVar(), Rating = r.Double(), RatingDelta = r.Double() };
                    Match = null;
                    _session.Journal.Append(JournalKind.System, "Net", ended.Finished ? $"매치 끝: {ended.Place}등, 점수 {ended.Rating:0} ({ended.RatingDelta:+0;-0;0})" : "매치 취소: " + ended.Reason);
                    MatchEnded?.Invoke(ended);
                    break;
                }
            }
        }

        void OnLobby(MirrorReader r)
        {
            Account = new NetLobbyInfo { Name = r.Str(), Rating = r.Double(), Wins = (int)r.UVar(), Losses = (int)r.UVar(), Draws = (int)r.UVar() };
            State = NetClientState.Lobby;
            Reached = true;
            PlayerId = 0;
            Match = null;
            _session.LocalPlayer = 0;
            LobbyChanged?.Invoke();
        }

        // 매칭큐: 줄을 선다(mode = 매칭 종류, 비우면 기본).
        public void Queue(string mode = "")
        {
            if (State != NetClientState.Lobby) return;
            var w = NetProtocol.Begin(NetMessage.Queue);
            w.Str(mode ?? "");
            _transport.Send(w.Data, w.Length);
        }

        public void CancelQueue()
        {
            if (State != NetClientState.Queued) return;
            var w = NetProtocol.Begin(NetMessage.CancelQueue);
            _transport.Send(w.Data, w.Length);
        }

        // 성사된 매치에 답한다(모두 수락하면 방으로 들어간다).
        public void AnswerMatch(bool accept)
        {
            if (State != NetClientState.MatchFound || Match == null) return;
            var w = NetProtocol.Begin(NetMessage.MatchAnswer);
            w.Str(Match.MatchId);
            w.Bool(accept);
            _transport.Send(w.Data, w.Length);
        }

        void OnWelcome(MirrorReader r)
        {
            // 매칭큐에서 새 매치에 들어가면 지난 매치의 판 데이터를 비운다(이어서 처음 모습이 온다).
            if (_hadRoom || ClearOnJoin) _session.Data.ResetZone(DataZone.Session);
            _hadRoom = true;
            Reached = true;
            PlayerId = (int)r.UVar();
            _replica.Reset();
            var missing = _replica.UseTypes(NetProtocol.ReadStrings(r));
            if (missing.Count > 0)
                _session.Journal.Append(JournalKind.Warning, "Net", $"서버에는 있고 이 게임에는 없는 형식 {missing.Count}개: {string.Join(", ", missing.GetRange(0, Math.Min(3, missing.Count)))}{(missing.Count > 3 ? " …" : "")} — 버전이 다를 수 있습니다.");
            ReadPlayers(r);
            _session.LocalPlayer = PlayerId;
            State = NetClientState.Joining;
            _session.Journal.Append(JournalKind.System, "Net", $"서버에 들어갔습니다(플레이어 #{PlayerId}).");
            PlayersChanged?.Invoke();
        }

        void OnBatch(MirrorReader r)
        {
            _replica.ApplyBatch(r);
            if (State == NetClientState.Joining)
            {
                State = NetClientState.Joined;
                _nextPing = Now;
                Joined?.Invoke();
            }
        }

        void OnResult(MirrorReader r)
        {
            var request = (long)r.UVar();
            var ok = r.Bool();
            _pending.TryGetValue(request, out var action);
            _pending.Remove(request);
            if (ok) return;
            var failure = new NetActionFailure { RequestId = request, Action = action, Key = r.Str(), Message = r.Str() };
            if (string.IsNullOrEmpty(failure.Key)) failure.Key = null;
            var args = new Dictionary<string, object>(StringComparer.Ordinal);
            var count = r.AtEnd ? 0 : (int)r.UVar();
            for (var i = 0; i < count; i++) args[r.Str()] = NetProtocol.ReadScalar(r);
            failure.Args = args;
            // 플레이어용 실패는 이쪽 언어로 다시 만든다(텍스트 표에 키가 있으면).
            if (failure.Key != null && Texts.Has(failure.Key)) failure.Message = Texts.Get(failure.Key, (IReadOnlyDictionary<string, object>)args);
            _session.Journal.Append(JournalKind.Warning, "Net", $"서버가 행동을 거절했습니다: {failure.Message}");
            ActionFailed?.Invoke(failure);
        }

        void ReadPlayers(MirrorReader r)
        {
            _players.Clear();
            var count = (int)r.UVar();
            for (var i = 0; i < count; i++)
                _players.Add(new NetPlayerInfo { Id = (int)r.UVar(), Name = r.Str(), Connected = r.Bool() });
        }

        void Close(string reason)
        {
            if (State == NetClientState.Closed) return;
            State = NetClientState.Closed;
            _pending.Clear();
            _session.Journal.Append(JournalKind.System, "Net", "서버 연결 끝: " + reason);
            Closed?.Invoke(reason);
        }
    }
}
