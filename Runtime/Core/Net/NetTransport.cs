using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace GBible
{
    // 전송 계층(어댑터): 바이트 메시지를 나르기만 한다. 게임·동기화 규칙은 모른다(NetServer·NetClient가 안다).
    // 바꿔 끼우는 것: 메모리(한 프로그램 안 시험 모드) · 직접 접속(테스트용 멀티, WebSocket) · Steam 릴레이(사설 호스트) · 클라우드(매칭큐).
    //
    // 규칙
    //   · 받은 것은 Poll로 꺼낸다(게임 스레드에서). 전송이 다른 스레드에서 받아도 Poll 전에는 게임 데이터를 건드리지 않는다.
    //   · reliable = true: 잃지 않고 보낸 차례대로. false: 잃거나 늦게 와도 되는 것(실시간 위치 — NetMessage.Stream만).
    //   · Send에 넘긴 배열은 돌아온 뒤 다시 쓸 수 있다(전송이 필요하면 복사한다).
    public enum NetEventKind
    {
        Connected,
        Data,
        Disconnected,
    }

    public struct NetEvent
    {
        public NetEventKind Kind;
        // 서버 쪽 연결 번호(클라이언트 쪽은 0).
        public int Connection;
        public byte[] Data;
        public int Length;
        // 끊긴 이유(사람이 읽는 글).
        public string Reason;
    }

    public interface INetServerTransport : IDisposable
    {
        void Start();
        void Send(int connection, byte[] data, int length, bool reliable = true);
        void Disconnect(int connection, string reason = null);
        bool Poll(out NetEvent e);
        // 접속할 사람에게 알려 줄 주소(사람이 읽는 글. 없으면 null).
        string Address { get; }
    }

    public interface INetClientTransport : IDisposable
    {
        void Connect();
        void Send(byte[] data, int length, bool reliable = true);
        void Disconnect(string reason = null);
        bool Poll(out NetEvent e);
    }

    // ── 메모리 전송(시험 모드) ─────────────────────────────────
    // 서버와 클라이언트를 한 프로그램에 띄우고 메시지를 메모리로 나른다. 일부러 지연·흔들림·유실을 넣어
    // 싱글에서 숨는 문제(Do 직후 결과를 읽는 화면 등)를 드러낸다. 시간은 실제 시계, 또는 Manual이면 Advance로만 흐른다(테스트).
    //   var net = new MemoryNetwork { Latency = 0.08, Jitter = 0.02 };
    //   var server = new NetServer(serverSession, net.Server());
    //   var client = new NetClient(clientSession, net.Client(), "철수");
    public sealed class MemoryNetwork
    {
        // 한쪽 방향 지연(초)과 그 흔들림(±초). 믿을 수 있는 메시지는 흔들려도 차례가 바뀌지 않는다.
        public double Latency;
        public double Jitter;
        // 믿지 않는 메시지(Stream)를 잃을 확률(0~1).
        public double Loss;
        // true면 시간이 Advance로만 흐른다(테스트: 정해진 순서로 재현).
        public bool Manual;

        readonly Stopwatch _watch = Stopwatch.StartNew();
        double _manualTime;
        ulong _noise = 0x9E3779B97F4A7C15UL;
        MemoryServer _server;
        int _nextConnection = 1;

        public double Now => Manual ? _manualTime : _watch.Elapsed.TotalSeconds;

        public void Advance(double seconds) => _manualTime += Math.Max(0, seconds);

        public INetServerTransport Server()
        {
            if (_server != null) throw new InvalidOperationException("[MomoiProject] 메모리 네트워크에는 서버가 하나만 있습니다.");
            return _server = new MemoryServer(this);
        }

        public INetClientTransport Client() => new MemoryClient(this);

        double Next01()
        {
            _noise ^= _noise << 13;
            _noise ^= _noise >> 7;
            _noise ^= _noise << 17;
            return (_noise >> 11) * (1.0 / (1UL << 53));
        }

        // 보낼 시각(믿는 메시지는 앞 메시지보다 먼저 오지 않게).
        double DeliverAt(ref double lastReliable, bool reliable)
        {
            var at = Now + Math.Max(0, Latency + (Jitter > 0 ? (Next01() * 2 - 1) * Jitter : 0));
            if (!reliable) return at;
            if (at < lastReliable) at = lastReliable;
            lastReliable = at;
            return at;
        }

        bool Dropped(bool reliable) => !reliable && Loss > 0 && Next01() < Loss;

        sealed class Pending
        {
            public double At;
            public NetEvent Event;
        }

        // 도착 시각 순 대기열(같은 시각이면 넣은 차례).
        sealed class Inbox
        {
            readonly List<Pending> _items = new List<Pending>();

            public void Add(double at, NetEvent e)
            {
                var index = _items.Count;
                while (index > 0 && _items[index - 1].At > at) index--;
                _items.Insert(index, new Pending { At = at, Event = e });
            }

            public bool Take(double now, out NetEvent e)
            {
                if (_items.Count > 0 && _items[0].At <= now)
                {
                    e = _items[0].Event;
                    _items.RemoveAt(0);
                    return true;
                }
                e = default;
                return false;
            }

            public void Clear() => _items.Clear();
        }

        static byte[] Copy(byte[] data, int length)
        {
            var copy = new byte[length];
            Buffer.BlockCopy(data, 0, copy, 0, length);
            return copy;
        }

        sealed class MemoryServer : INetServerTransport
        {
            readonly MemoryNetwork _net;
            readonly Inbox _inbox = new Inbox();
            internal readonly Dictionary<int, MemoryClient> Clients = new Dictionary<int, MemoryClient>();
            bool _started;

            public MemoryServer(MemoryNetwork net) => _net = net;

            public string Address => "memory";

            public void Start() => _started = true;

            internal bool Accept(MemoryClient client)
            {
                if (!_started) return false;
                client.Connection = _net._nextConnection++;
                Clients[client.Connection] = client;
                _inbox.Add(_net.DeliverAt(ref client.LastToServer, true), new NetEvent { Kind = NetEventKind.Connected, Connection = client.Connection });
                return true;
            }

            internal void Receive(MemoryClient from, byte[] data, int length, bool reliable)
            {
                if (_net.Dropped(reliable)) return;
                _inbox.Add(_net.DeliverAt(ref from.LastToServer, reliable), new NetEvent { Kind = NetEventKind.Data, Connection = from.Connection, Data = Copy(data, length), Length = length });
            }

            internal void Lost(MemoryClient from, string reason)
            {
                if (!Clients.Remove(from.Connection)) return;
                _inbox.Add(_net.DeliverAt(ref from.LastToServer, true), new NetEvent { Kind = NetEventKind.Disconnected, Connection = from.Connection, Reason = reason });
            }

            public void Send(int connection, byte[] data, int length, bool reliable = true)
            {
                if (Clients.TryGetValue(connection, out var client)) client.Receive(data, length, reliable);
            }

            public void Disconnect(int connection, string reason = null)
            {
                if (!Clients.TryGetValue(connection, out var client)) return;
                Clients.Remove(connection);
                client.Closed(reason ?? "서버가 연결을 끊었습니다.");
            }

            public bool Poll(out NetEvent e) => _inbox.Take(_net.Now, out e);

            public void Dispose()
            {
                foreach (var client in new List<MemoryClient>(Clients.Values)) client.Closed("서버가 닫혔습니다.");
                Clients.Clear();
                _started = false;
                _inbox.Clear();
                _net._server = null;
            }
        }

        sealed class MemoryClient : INetClientTransport
        {
            readonly MemoryNetwork _net;
            readonly Inbox _inbox = new Inbox();
            internal int Connection;
            internal double LastToServer;
            double _lastToClient;
            bool _open;

            public MemoryClient(MemoryNetwork net) => _net = net;

            public void Connect()
            {
                if (_open) return;
                var server = _net._server;
                if (server == null || !server.Accept(this))
                {
                    _inbox.Add(_net.Now, new NetEvent { Kind = NetEventKind.Disconnected, Reason = "서버가 열려 있지 않습니다." });
                    return;
                }
                _open = true;
                _inbox.Add(_net.DeliverAt(ref _lastToClient, true), new NetEvent { Kind = NetEventKind.Connected });
            }

            internal void Receive(byte[] data, int length, bool reliable)
            {
                if (!_open || _net.Dropped(reliable)) return;
                _inbox.Add(_net.DeliverAt(ref _lastToClient, reliable), new NetEvent { Kind = NetEventKind.Data, Data = Copy(data, length), Length = length });
            }

            internal void Closed(string reason)
            {
                if (!_open) return;
                _open = false;
                _inbox.Add(_net.DeliverAt(ref _lastToClient, true), new NetEvent { Kind = NetEventKind.Disconnected, Reason = reason });
            }

            public void Send(byte[] data, int length, bool reliable = true)
            {
                if (_open) _net._server?.Receive(this, data, length, reliable);
            }

            public void Disconnect(string reason = null)
            {
                if (!_open) return;
                _open = false;
                _net._server?.Lost(this, reason ?? "클라이언트가 나갔습니다.");
            }

            public bool Poll(out NetEvent e) => _inbox.Take(_net.Now, out e);

            public void Dispose() => Disconnect();
        }
    }
}
