using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace GBible
{
    // 직접 접속 전송(테스트용 멀티·사설 호스트 시험): WebSocket(RFC 6455, 바이너리 메시지)을 TCP 위에 직접 구현한다.
    //   · 서버는 포트 하나만 연다(관리자 권한·URL 등록 필요 없음 — Windows 방화벽 허용만). 클라우드(로드밸런서)와도 같은 형식이다.
    //   · 받기·보내기는 연결마다 백그라운드 스레드가 하고, 받은 것은 Poll로 게임 스레드에 넘긴다.
    //   var server = new WebSocketServerTransport(7777);           // 접속 주소: ws://<이 컴퓨터 IP>:7777
    //   var client = new WebSocketClientTransport("ws://100.64.1.2:7777");
    // 인터넷으로 닿게 하는 법(게임 코드와 무관): 가상 랜 도구(Tailscale 등, 권장) · 공유기 포트 포워딩 · 작은 서버 임대.
    public static class WebSocketFrames
    {
        public const string Guid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        // 메시지 하나의 최대 크기(바이트). 넘으면 연결을 끊는다(잘못된 데이터·공격 방지).
        public const int MaxMessage = 32 * 1024 * 1024;

        public static string AcceptKey(string key)
        {
            using (var sha = SHA1.Create())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(key.Trim() + Guid)));
        }

        // 프레임 하나를 쓴다(opcode 2 = 바이너리, 8 = 닫기, 9 = 핑, 10 = 퐁). 클라이언트는 mask = true(규격).
        public static void Write(Stream stream, int opcode, byte[] data, int start, int count, bool mask, Random random)
        {
            var header = new byte[14];
            var length = 0;
            header[length++] = (byte)(0x80 | (opcode & 0x0F));
            var maskBit = mask ? 0x80 : 0;
            if (count < 126) header[length++] = (byte)(maskBit | count);
            else if (count <= ushort.MaxValue)
            {
                header[length++] = (byte)(maskBit | 126);
                header[length++] = (byte)(count >> 8);
                header[length++] = (byte)count;
            }
            else
            {
                header[length++] = (byte)(maskBit | 127);
                for (var i = 7; i >= 0; i--) header[length++] = (byte)((long)count >> (8 * i));
            }
            // 머리와 내용을 한 버퍼로 한 번에 쓴다(따로 쓰면 NoDelay라 메시지마다 TCP 조각이 둘 — 작은 메시지가 많은 실시간 멀티에서 두 배).
            byte[] key = null;
            if (mask)
            {
                key = new byte[4];
                lock (random) random.NextBytes(key);
                Buffer.BlockCopy(key, 0, header, length, 4);
                length += 4;
            }
            var frame = new byte[length + count];
            Buffer.BlockCopy(header, 0, frame, 0, length);
            if (key == null) Buffer.BlockCopy(data, start, frame, length, count);
            else
                for (var i = 0; i < count; i++) frame[length + i] = (byte)(data[start + i] ^ key[i & 3]);
            stream.Write(frame, 0, frame.Length);
        }

        // 메시지 하나를 읽는다(조각 이어 붙이기, 핑에는 퐁). 닫기·끊김이면 null. opcode는 마지막 데이터 프레임의 종류.
        public static byte[] ReadMessage(Stream stream, Action<byte[]> pong, out int opcode)
        {
            opcode = 0;
            MemoryStream assembled = null;
            while (true)
            {
                var b0 = stream.ReadByte();
                var b1 = stream.ReadByte();
                if (b0 < 0 || b1 < 0) return null;
                var fin = (b0 & 0x80) != 0;
                var code = b0 & 0x0F;
                var masked = (b1 & 0x80) != 0;
                long length = b1 & 0x7F;
                if (length == 126)
                {
                    var two = ReadExact(stream, 2);
                    length = (two[0] << 8) | two[1];
                }
                else if (length == 127)
                {
                    var bytes = ReadExact(stream, 8);
                    length = 0;
                    for (var i = 0; i < 8; i++) length = (length << 8) | bytes[i];
                }
                if (length < 0 || length > MaxMessage) throw new IOException("WebSocket 메시지가 너무 큽니다.");
                var key = masked ? ReadExact(stream, 4) : null;
                var payload = ReadExact(stream, (int)length);
                if (key != null)
                    for (var i = 0; i < payload.Length; i++) payload[i] ^= key[i & 3];
                switch (code)
                {
                    case 8:
                        return null;
                    case 9:
                        pong?.Invoke(payload);
                        continue;
                    case 10:
                        continue;
                }
                if (code != 0) opcode = code;
                if (fin && assembled == null) return payload;
                assembled ??= new MemoryStream();
                assembled.Write(payload, 0, payload.Length);
                if (assembled.Length > MaxMessage) throw new IOException("WebSocket 메시지가 너무 큽니다.");
                if (fin) return assembled.ToArray();
            }
        }

        // count바이트를 다 읽는다(끊기면 예외).
        static byte[] ReadExact(Stream stream, int count)
        {
            var buffer = new byte[count];
            var read = 0;
            while (read < count)
            {
                var n = stream.Read(buffer, read, count - read);
                if (n <= 0) throw new EndOfStreamException();
                read += n;
            }
            return buffer;
        }

        // HTTP 머리(빈 줄까지)를 읽는다.
        public static string ReadHeader(Stream stream)
        {
            var bytes = new List<byte>();
            while (bytes.Count < 16384)
            {
                var b = stream.ReadByte();
                if (b < 0) return null;
                bytes.Add((byte)b);
                var n = bytes.Count;
                if (n >= 4 && bytes[n - 4] == 13 && bytes[n - 3] == 10 && bytes[n - 2] == 13 && bytes[n - 1] == 10) break;
            }
            return Encoding.ASCII.GetString(bytes.ToArray());
        }

        public static string HeaderValue(string header, string name)
        {
            foreach (var line in header.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                var colon = line.IndexOf(':');
                if (colon > 0 && string.Equals(line.Substring(0, colon).Trim(), name, StringComparison.OrdinalIgnoreCase))
                    return line.Substring(colon + 1).Trim();
            }
            return null;
        }

        // 이 컴퓨터의 IPv4 주소들(접속할 사람에게 알려 줄 후보: 가상 랜·랜).
        public static List<string> LocalAddresses()
        {
            var found = new List<string>();
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                        if (unicast.Address.AddressFamily == AddressFamily.InterNetwork) found.Add(unicast.Address.ToString());
                }
            }
            catch (Exception)
            {
                // 일부 플랫폼은 네트워크 목록을 주지 않는다.
            }
            return found.Distinct().ToList();
        }
    }

    // 직접 접속 전송의 연결 하나(서버·클라이언트 공통): 받기 스레드 + 보내기 스레드.
    sealed class WebSocketLink
    {
        readonly TcpClient _tcp;
        readonly Stream _stream;
        readonly bool _mask;
        readonly Random _random = new Random();
        readonly BlockingCollection<(int opcode, byte[] data)> _outbox = new BlockingCollection<(int, byte[])>();
        // 보낼 목록에 쌓아 둘 수 있는 메시지 수(넘으면 끊는다).
        const int MaxQueued = 8192;
        readonly Action<byte[], int> _received;
        readonly Action<string> _closed;
        int _done;

        public WebSocketLink(TcpClient tcp, Stream stream, bool mask, Action<byte[], int> received, Action<string> closed)
        {
            _tcp = tcp;
            _stream = stream;
            _mask = mask;
            _received = received;
            _closed = closed;
            new Thread(ReadLoop) { IsBackground = true, Name = "MomoiProject Net 받기" }.Start();
            new Thread(WriteLoop) { IsBackground = true, Name = "MomoiProject Net 보내기" }.Start();
        }

        public void Send(byte[] data, int length)
        {
            if (_done != 0) return;
            // 받는 쪽이 멈춰(읽지 않음) 보낼 목록이 끝없이 쌓이면 끊는다(핑만 보내는 클라이언트가 서버 메모리를 채우지 못하게).
            if (_outbox.Count >= MaxQueued)
            {
                Close("보낼 메시지가 너무 많이 쌓였습니다(받는 쪽이 멈췄습니다).");
                return;
            }
            var copy = new byte[length];
            Buffer.BlockCopy(data, 0, copy, 0, length);
            try { _outbox.Add((2, copy)); }
            catch (InvalidOperationException) { }
        }

        // 닫기: 보낼 목록에 남은 메시지(방이 닫혔다는 Bye 등)를 다 보낸 뒤 닫기 프레임을 보내고 끊는다(보내기 스레드가 한다).
        // 보내기가 막혀 있으면 1초 뒤 그냥 끊는다.
        public void Close(string reason)
        {
            if (Interlocked.Exchange(ref _done, 1) != 0) return;
            var bytes = Encoding.UTF8.GetBytes(reason ?? "");
            var payload = new byte[2 + Math.Min(bytes.Length, 120)];
            payload[0] = 0x03;
            payload[1] = 0xE8;
            Buffer.BlockCopy(bytes, 0, payload, 2, payload.Length - 2);
            try
            {
                _outbox.Add((8, payload));
                _outbox.CompleteAdding();
            }
            catch (InvalidOperationException) { }
            System.Threading.Tasks.Task.Delay(1000).ContinueWith(_ => Shutdown());
        }

        void Shutdown()
        {
            try { _outbox.CompleteAdding(); } catch (ObjectDisposedException) { }
            try { _tcp.Close(); } catch (Exception) { }
        }

        void ReadLoop()
        {
            string reason = "연결이 끊겼습니다.";
            try
            {
                while (true)
                {
                    var message = WebSocketFrames.ReadMessage(_stream, ping =>
                    {
                        try { _outbox.Add((10, ping)); } catch (InvalidOperationException) { }
                    }, out _);
                    if (message == null) { reason = "상대가 연결을 닫았습니다."; break; }
                    _received(message, message.Length);
                }
            }
            catch (Exception e)
            {
                if (_done == 0) reason = "연결이 끊겼습니다: " + e.Message;
            }
            var first = Interlocked.Exchange(ref _done, 1) == 0;
            Shutdown();
            if (first) _closed(reason);
        }

        void WriteLoop()
        {
            try
            {
                foreach (var (opcode, data) in _outbox.GetConsumingEnumerable())
                    lock (_stream) WebSocketFrames.Write(_stream, opcode, data, 0, data.Length, _mask, _random);
                // 닫기(Close)로 보낼 목록이 끝났으면 다 보낸 뒤 끊는다.
                if (_done != 0) Shutdown();
            }
            catch (Exception)
            {
                if (Interlocked.Exchange(ref _done, 1) == 0)
                {
                    Shutdown();
                    _closed("보내지 못해 연결을 끊었습니다.");
                }
            }
        }
    }

    public sealed class WebSocketServerTransport : INetServerTransport
    {
        int _port;
        readonly ConcurrentQueue<NetEvent> _inbox = new ConcurrentQueue<NetEvent>();
        readonly ConcurrentDictionary<int, WebSocketLink> _links = new ConcurrentDictionary<int, WebSocketLink>();
        TcpListener _listener;
        int _nextConnection;
        volatile bool _running;

        // port 0이면 비어 있는 포트를 고른다(Start 뒤 Port로 읽는다 — 테스트용).
        public WebSocketServerTransport(int port) => _port = port;

        public int Port => _port;

        // 접속할 사람이 넣을 주소 후보(이 컴퓨터의 IPv4마다).
        public string Address
        {
            get
            {
                var addresses = WebSocketFrames.LocalAddresses();
                return addresses.Count == 0 ? $"ws://localhost:{_port}" : string.Join("  ", addresses.Select(a => $"ws://{a}:{_port}"));
            }
        }

        public void Start()
        {
            if (_running) return;
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            _port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _running = true;
            new Thread(AcceptLoop) { IsBackground = true, Name = "MomoiProject Net 접속 받기" }.Start();
        }

        void AcceptLoop()
        {
            while (_running)
            {
                TcpClient tcp;
                try { tcp = _listener.AcceptTcpClient(); }
                catch (Exception) { break; }
                new Thread(() => Handshake(tcp)) { IsBackground = true, Name = "MomoiProject Net 악수" }.Start();
            }
        }

        void Handshake(TcpClient tcp)
        {
            try
            {
                tcp.NoDelay = true;
                var stream = tcp.GetStream();
                stream.ReadTimeout = 10000;
                var header = WebSocketFrames.ReadHeader(stream);
                var key = header == null ? null : WebSocketFrames.HeaderValue(header, "Sec-WebSocket-Key");
                if (key == null)
                {
                    var bad = Encoding.ASCII.GetBytes("HTTP/1.1 400 Bad Request\r\nContent-Length: 0\r\n\r\n");
                    stream.Write(bad, 0, bad.Length);
                    tcp.Close();
                    return;
                }
                var response = "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + WebSocketFrames.AcceptKey(key) + "\r\n\r\n";
                var bytes = Encoding.ASCII.GetBytes(response);
                stream.Write(bytes, 0, bytes.Length);
                stream.ReadTimeout = Timeout.Infinite;
                var connection = Interlocked.Increment(ref _nextConnection);
                var link = new WebSocketLink(tcp, stream, false,
                    (data, length) => _inbox.Enqueue(new NetEvent { Kind = NetEventKind.Data, Connection = connection, Data = data, Length = length }),
                    reason =>
                    {
                        _links.TryRemove(connection, out _);
                        _inbox.Enqueue(new NetEvent { Kind = NetEventKind.Disconnected, Connection = connection, Reason = reason });
                    });
                _links[connection] = link;
                _inbox.Enqueue(new NetEvent { Kind = NetEventKind.Connected, Connection = connection });
            }
            catch (Exception)
            {
                try { tcp.Close(); } catch (Exception) { }
            }
        }

        public void Send(int connection, byte[] data, int length, bool reliable = true)
        {
            if (_links.TryGetValue(connection, out var link)) link.Send(data, length);
        }

        public void Disconnect(int connection, string reason = null)
        {
            if (_links.TryRemove(connection, out var link)) link.Close(reason);
        }

        public bool Poll(out NetEvent e) => _inbox.TryDequeue(out e);

        public void Dispose()
        {
            _running = false;
            try { _listener?.Stop(); } catch (Exception) { }
            foreach (var pair in _links.ToArray()) pair.Value.Close("서버가 닫혔습니다.");
            _links.Clear();
        }
    }

    public sealed class WebSocketClientTransport : INetClientTransport
    {
        readonly string _url;
        readonly ConcurrentQueue<NetEvent> _inbox = new ConcurrentQueue<NetEvent>();
        WebSocketLink _link;
        volatile bool _connecting;

        // url: ws://주소:포트 (주소만 주면 ws://주소, 포트를 빼면 7777).
        public WebSocketClientTransport(string url) => _url = Normalize(url);

        public string Url => _url;

        public static string Normalize(string url)
        {
            url = (url ?? "").Trim();
            if (!url.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("wss://", StringComparison.OrdinalIgnoreCase)) url = "ws://" + url;
            var uri = new Uri(url);
            return uri.IsDefaultPort || uri.Port <= 0 ? $"ws://{uri.Host}:7777{uri.PathAndQuery}" : uri.ToString();
        }

        volatile bool _closedWhileConnecting;

        public void Connect()
        {
            if (_connecting || _link != null) return;
            _connecting = true;
            _closedWhileConnecting = false;
            new Thread(ConnectLoop) { IsBackground = true, Name = "MomoiProject Net 연결" }.Start();
        }

        void ConnectLoop()
        {
            try
            {
                var uri = new Uri(_url);
                if (uri.Scheme == "wss") throw new NotSupportedException("wss(암호화)는 직접 접속 전송에서 쓰지 않습니다 — ws://를 쓰세요.");
                var tcp = new TcpClient { NoDelay = true };
                var connect = tcp.BeginConnect(uri.Host, uri.Port, null, null);
                if (!connect.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(8))) throw new TimeoutException("8초 안에 서버에 닿지 않았습니다(주소·포트·방화벽을 확인).");
                tcp.EndConnect(connect);
                var stream = tcp.GetStream();
                var key = Convert.ToBase64String(System.Guid.NewGuid().ToByteArray());
                var request = $"GET {uri.PathAndQuery} HTTP/1.1\r\nHost: {uri.Host}:{uri.Port}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: {key}\r\nSec-WebSocket-Version: 13\r\n\r\n";
                var bytes = Encoding.ASCII.GetBytes(request);
                stream.Write(bytes, 0, bytes.Length);
                stream.ReadTimeout = 10000;
                var header = WebSocketFrames.ReadHeader(stream);
                if (header == null || !header.StartsWith("HTTP/1.1 101", StringComparison.Ordinal) || WebSocketFrames.HeaderValue(header, "Sec-WebSocket-Accept") != WebSocketFrames.AcceptKey(key))
                    throw new IOException("WebSocket 서버가 아닙니다(주소를 확인).");
                stream.ReadTimeout = Timeout.Infinite;
                var link = new WebSocketLink(tcp, stream, true,
                    (data, length) => _inbox.Enqueue(new NetEvent { Kind = NetEventKind.Data, Data = data, Length = length }),
                    reason => _inbox.Enqueue(new NetEvent { Kind = NetEventKind.Disconnected, Reason = reason }));
                // 연결하는 동안 닫혔으면(Dispose) 막 만든 연결도 닫는다 — 연결과 스레드가 남지 않게.
                if (_closedWhileConnecting)
                {
                    link.Close("나갔습니다.");
                    return;
                }
                _link = link;
                _inbox.Enqueue(new NetEvent { Kind = NetEventKind.Connected });
            }
            catch (Exception e)
            {
                _inbox.Enqueue(new NetEvent { Kind = NetEventKind.Disconnected, Reason = "접속하지 못했습니다: " + e.Message });
            }
            finally
            {
                _connecting = false;
            }
        }

        public void Send(byte[] data, int length, bool reliable = true) => _link?.Send(data, length);

        public void Disconnect(string reason = null)
        {
            if (_connecting) _closedWhileConnecting = true;
            _link?.Close(reason ?? "나갔습니다.");
            _link = null;
        }

        public bool Poll(out NetEvent e) => _inbox.TryDequeue(out e);

        public void Dispose() => Disconnect();
    }
}
