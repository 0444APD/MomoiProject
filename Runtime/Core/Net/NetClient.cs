using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace GBible
{
    public enum NetClientState
    {
        Idle,
        Connecting,
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
        readonly MirrorReceiver _receiver = new MirrorReceiver();
        readonly Dictionary<long, GameAction> _pending = new Dictionary<long, GameAction>();
        readonly List<NetPlayerInfo> _players = new List<NetPlayerInfo>();
        readonly Stopwatch _watch = Stopwatch.StartNew();
        long _nextRequest = 1;
        double _nextPing;

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
        }

        public GameSession Session => _session;
        public string Name { get; }
        public string Token { get; }
        public string GameVersion { get; }
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
                        SendHello();
                        break;
                    case NetEventKind.Disconnected:
                        Close(e.Reason ?? "연결이 끊겼습니다.");
                        break;
                    case NetEventKind.Data:
                        try { Handle(e.Data, e.Length); }
                        catch (Exception ex)
                        {
                            _session.Journal.Append(JournalKind.Error, "Net", "서버 메시지를 처리하지 못했습니다: " + ex.Message);
                        }
                        break;
                }
            }
            if (State == NetClientState.Joined && Now >= _nextPing)
            {
                _nextPing = Now + Math.Max(0.2, PingInterval);
                var w = NetProtocol.Begin(NetMessage.Ping);
                w.Double(Now);
                _transport.Send(w.Data, w.Length);
            }
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

        double Now => _watch.Elapsed.TotalSeconds;

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
        ActionRecord Send(GameAction action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (!IsJoined)
            {
                _session.Journal.Append(JournalKind.Warning, "Net", $"서버에 들어가기 전이라 '{action.GetType().Name}'을(를) 보내지 못했습니다.");
                return new ActionRecord { Action = action, Status = ActionStatus.Failed, Error = "서버에 연결되어 있지 않습니다." };
            }
            ActionReferences.Detach(action);
            var body = new MirrorWriter();
            Mirror.Write(body, action);
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
                    Close(r.AtEnd ? "서버가 연결을 끊었습니다." : r.Str());
                    break;
            }
        }

        void OnWelcome(MirrorReader r)
        {
            PlayerId = (int)r.UVar();
            var missing = _receiver.UseTypes(NetProtocol.ReadStrings(r));
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
            var tick = r.Long();
            var time = r.Double();
            _session.Clock.Rewind(tick, time);
            var data = _session.Data;
            while (!r.AtEnd)
            {
                var kind = (NetRecord)r.Byte();
                if (kind == 0) break;
                switch (kind)
                {
                    case NetRecord.Reset:
                    {
                        var key = r.Str();
                        var zone = (DataZone)r.Byte();
                        var label = r.Str();
                        var snapshot = NetProtocol.ReadBlockCopy(r);
                        data.LoadRemote(key, zone, label, snapshot, _receiver);
                        break;
                    }
                    case NetRecord.Delta:
                    {
                        var key = r.Str();
                        NetProtocol.ReadBlock(r, out var start, out var end);
                        data.ApplyRemote(key, r.Data, start, end, _receiver);
                        break;
                    }
                    case NetRecord.Value:
                    {
                        var key = r.Str();
                        var zone = (DataZone)r.Byte();
                        var label = r.Str();
                        data.SetRemote(key, zone, label, NetProtocol.ReadScalar(r));
                        break;
                    }
                    case NetRecord.Tree:
                    {
                        var key = r.Str();
                        var zone = (DataZone)r.Byte();
                        var label = r.Str();
                        var type = TypeResolver.Find(r.Str());
                        data.LoadRemoteTree(key, zone, label, type, MiniJson.Deserialize(r.Str()));
                        break;
                    }
                    case NetRecord.Hide:
                        r.Str();
                        break;
                    case NetRecord.Event:
                    {
                        NetProtocol.ReadBlock(r, out var start, out var end);
                        object evt;
                        _receiver.MissingAsNull = true;
                        try { evt = _receiver.Load(r.Data, start, end); }
                        finally { _receiver.MissingAsNull = false; }
                        _session.Events.PublishRemote(evt);
                        break;
                    }
                    default:
                        throw new FormatException($"[MomoiProject] 모르는 묶음 기록({(byte)kind})입니다.");
                }
            }
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
