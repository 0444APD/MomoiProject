using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace GBible.Tests
{
    [GameEvent("시험 카드 뽑음")]
    public class NtDrawn : IOwnedEvent
    {
        public int player;
        public string card;
        public int Owner => player;
    }

    [GameEvent("시험 서버 전용", ServerOnly = true)]
    public class NtSecret
    {
        public int value;
    }

    [Serializable]
    public class NtDraw : GameAction
    {
        public string card = "fireball";

        protected override void Run()
        {
            Game.Session.Events.Publish(new NtDrawn { player = actorId, card = card });
            Game.Session.Events.Publish(new NtSecret { value = 7 });
            Game.Session.Data.Set("gold", Game.Session.Data.GetNumber("gold") + 1);
        }
    }

    // 멀티 보강: 리플레이(녹화·재생·찾아가기) · 이벤트 공개 범위 · 응답 없는 연결 끊기 · 매칭큐 부하 시험(불변식).
    public class NetExtraTests
    {
        static GameSession ServerSession()
        {
            var session = new GameSession();
            session.Data.Define("party", DataZone.Session, new NtParty { units = { new NtUnit { id = "a", hp = 10 }, new NtUnit { id = "b", hp = 8 } } });
            session.Data.Define("gold", DataZone.Session, 5.0);
            session.Data.Define("lastActor", DataZone.Session, 0.0);
            session.Data.Define("hand:1", DataZone.Session, new NtParty { units = { new NtUnit { id = "h1" } } });
            session.Data.Define("hand:2", DataZone.Session, new NtParty { units = { new NtUnit { id = "h2" } } });
            session.Data.SetVisibility("hand:1", DataVisibility.Owner, 1);
            session.Data.SetVisibility("hand:2", DataVisibility.Owner, 2);
            return session;
        }

        static NtUnit Unit(GameSession session, string id) => session.Data.Get<NtParty>("party").units.First(u => u.id == id);

        [Test]
        public void 클라이언트가_녹화한_것을_다시_틀고_앞뒤로_찾아간다()
        {
            var net = new MemoryNetwork { Manual = true, Latency = 0.01 };
            var server = new NetServer(ServerSession(), net.Server());
            server.Start();
            var client = new NetClient(new GameSession(), net.Client(), "철수") { Clock = () => net.Now };
            client.StartRecording();
            client.Connect();
            void Pump(int steps = 20)
            {
                for (var i = 0; i < steps; i++)
                {
                    net.Advance(0.05);
                    server.Update(0.05);
                    client.Update(0.05);
                }
            }
            Pump();
            client.Session.Do(new NtStrike { unit = Unit(client.Session, "a"), damage = 3 });
            Pump();
            client.Session.Do(new NtStrike { unit = Unit(client.Session, "a"), damage = 2 });
            Pump();
            var replay = NetReplay.FromBytes(client.Recording.ToBytes());
            Assert.GreaterOrEqual(replay.Count, 3);

            var view = new GameSession();
            var hits = new List<NtHit>();
            view.Events.SubscribeAfterAction<NtHit>(hits.Add);
            var player = new NetReplayPlayer(view, replay);
            player.Seek(replay.Duration);
            Assert.AreEqual(5, Unit(view, "a").hp, "끝 모습");
            Assert.AreEqual(0, hits.Count, "찾아갈 때는 이벤트를 쏘지 않는다");
            Assert.IsFalse(view.Data.IsDefined("hand:2"), "녹화는 그 사람이 본 것만");

            player.Seek(0);
            Assert.AreEqual(10, Unit(view, "a").hp, "처음으로 돌아간다");
            player.Playing = true;
            while (!player.AtEnd) player.Update(0.05);
            Assert.AreEqual(5, Unit(view, "a").hp);
            Assert.AreEqual(2, hits.Count, "틀면 연출 이벤트가 온다");
            Assert.IsFalse(view.Undo(), "리플레이 세션은 되돌리지 않는다");
            Assert.AreEqual(ActionStatus.Failed, view.Do(new NtStrike { unit = Unit(view, "a") }).Status, "리플레이 중에는 행동하지 않는다");
        }

        [Test]
        public void 혼자_하는_판도_녹화하고_모두_보는_눈으로_틀린다()
        {
            var session = ServerSession();
            var recorder = NetServer.StartLocalRecording(session, out var replay);
            session.Do(new NtStrike { unit = Unit(session, "b"), damage = 5 });
            recorder.Flush();
            session.Do(new NtDraw());
            recorder.Flush();
            recorder.Dispose();
            Assert.AreEqual(SessionRole.Local, session.Role, "녹화를 끝내면 혼자 하기로");

            var path = Path.Combine(Path.GetTempPath(), "momoi-replay-" + Guid.NewGuid().ToString("N") + ".gbreplay");
            replay.Save(path);
            var loaded = NetReplay.Load(path);
            File.Delete(path);

            var view = new GameSession();
            var player = new NetReplayPlayer(view, loaded);
            player.Seek(loaded.Duration);
            Assert.AreEqual(3, Unit(view, "b").hp);
            Assert.AreEqual(6.0, view.Data.GetNumber("gold"));
            Assert.IsTrue(view.Data.IsDefined("hand:1") && view.Data.IsDefined("hand:2"), "모두 보는 눈은 주인만 보는 키도 담는다");
        }

        [Test]
        public void 혼자_녹화_중_Undo도_녹화된다()
        {
            var session = ServerSession();
            var recorder = NetServer.StartLocalRecording(session, out var replay);
            Assert.AreEqual(SessionRole.Local, session.Role, "녹화만 한다 — 혼자 하기 그대로");
            session.Do(new NtStrike { unit = Unit(session, "a"), damage = 4 });
            recorder.Flush();
            Assert.IsTrue(session.Undo());
            recorder.Flush();
            recorder.Dispose();

            var view = new GameSession();
            var player = new NetReplayPlayer(view, replay);
            player.Seek(replay.Duration);
            Assert.AreEqual(10, Unit(view, "a").hp, "Undo한 뒤 모습");
        }

        [Test]
        public void 끊겼다가_다시_접속하면_같은_자리로_잇고_녹화도_이어진다()
        {
            var net = new MemoryNetwork { Manual = true, Latency = 0.01 };
            var server = new NetServer(ServerSession(), net.Server()) { Clock = () => net.Now };
            server.Start();
            var session = new GameSession();
            var first = new NetClient(session, net.Client(), "철수", "티켓1") { Clock = () => net.Now };
            var replay = first.StartRecording();
            first.Connect();
            NetClient current = first;
            void Pump()
            {
                for (var i = 0; i < 20; i++)
                {
                    net.Advance(0.05);
                    server.Update(0.05);
                    current.Update(0.05);
                }
            }
            Pump();
            var id = first.PlayerId;
            session.Do(new NtStrike { unit = Unit(session, "a"), damage = 3 });
            Pump();
            first.Drop();
            Assert.IsFalse(first.ServerClosed, "네트워크가 끊긴 것 — 다시 접속해 볼 만하다");
            Pump();
            Assert.IsFalse(server.Players.Single().Connected);
            // 떨어져 있는 사이 서버에서 바뀐 것
            server.Session.Do(new NtStrike { unit = Unit(server.Session, "b"), damage = 5 });
            server.Flush();

            first.Dispose();
            var again = new NetClient(session, net.Client(), "철수", "티켓2") { Clock = () => net.Now, ClearOnJoin = true };
            again.StartRecording(replay);
            again.Connect();
            current = again;
            Pump();
            Assert.IsTrue(again.IsJoined);
            Assert.AreEqual(id, again.PlayerId, "같은 계정 = 같은 자리");
            Assert.AreEqual(1, server.Players.Count);
            Assert.AreEqual(7, Unit(session, "a").hp);
            Assert.AreEqual(3, Unit(session, "b").hp, "떨어져 있던 사이의 변경도 받는다");

            var view = new GameSession();
            var player = new NetReplayPlayer(view, NetReplay.FromBytes(replay.ToBytes()));
            player.Seek(player.Duration);
            Assert.AreEqual(3, Unit(view, "b").hp, "녹화는 다시 접속한 뒤로 이어진다");
            for (var i = 1; i < replay.Count; i++) Assert.IsTrue(replay.Entries[i].Time >= replay.Entries[i - 1].Time, "시각은 앞으로만");
        }


        [Test]
        public void 같은_계정이_다시_오면_옛_연결이_조용할_때만_이어받는다()
        {
            var net = new MemoryNetwork { Manual = true, Latency = 0.01 };
            var server = new NetServer(ServerSession(), net.Server(), new NetServerOptions { TimeoutSeconds = 30 }) { Clock = () => net.Now };
            server.Start();
            var first = new NetClient(new GameSession(), net.Client(), "철수") { Clock = () => net.Now, TimeoutSeconds = 30 };
            first.Connect();
            var clients = new List<NetClient> { first };
            void Pump(double seconds, bool firstAlive = true)
            {
                for (var t = 0.0; t < seconds; t += 0.05)
                {
                    net.Advance(0.05);
                    server.Update(0.05);
                    foreach (var c in clients)
                        if (c != first || firstAlive) c.Update(0.05);
                }
            }
            Pump(1);
            Assert.IsTrue(first.IsJoined);
            var id = first.PlayerId;

            // 옛 연결이 살아 있으면 새 연결만 끊는다(거절이 아니다 — 잠시 뒤 다시 시도할 수 있다).
            var second = new NetClient(new GameSession(), net.Client(), "철수") { Clock = () => net.Now, TimeoutSeconds = 30 };
            clients.Add(second);
            second.Connect();
            Pump(1);
            Assert.IsTrue(first.IsJoined, "옛 연결은 그대로");
            Assert.IsFalse(second.IsJoined);
            Assert.AreEqual(1, server.Players.Count);
            Assert.IsTrue(server.Players.Single().Connected);

            // 옛 연결이 3초 넘게 조용하면(네트워크가 끊겼는데 서버는 아직 모름) 새 연결이 같은 자리를 이어받는다.
            Pump(4, firstAlive: false);
            var third = new NetClient(new GameSession(), net.Client(), "철수") { Clock = () => net.Now, TimeoutSeconds = 30 };
            clients.Add(third);
            third.Connect();
            Pump(1, firstAlive: false);
            Assert.IsTrue(third.IsJoined, "이어받는다");
            Assert.AreEqual(id, third.PlayerId, "같은 자리");
            Assert.AreEqual(1, server.Players.Count);
            Assert.IsTrue(server.Players.Single().Connected);
        }

        [Test]
        public void 이어받으면_옛_연결은_나간_것으로_알린_뒤_새_연결로_들어온다()
        {
            var net = new MemoryNetwork { Manual = true, Latency = 0.01 };
            var server = new NetServer(ServerSession(), net.Server(), new NetServerOptions { TimeoutSeconds = 30 }) { Clock = () => net.Now };
            server.Start();
            // 들어올 때 캐릭터를 만들고 나갈 때 지우는 게임 코드: 들어옴·나감이 짝을 이뤄야 한 사람의 캐릭터가 둘이 되지 않는다.
            var seen = new List<string>();
            server.PlayerJoined += p => seen.Add("들어옴 " + p.Id);
            server.PlayerLeft += p => seen.Add("나감 " + p.Id);
            var first = new NetClient(new GameSession(), net.Client(), "철수") { Clock = () => net.Now, TimeoutSeconds = 30 };
            first.Connect();
            NetClient second = null;
            // 옛 연결(first)은 멈춰 있다 — 네트워크가 끊겼는데 서버는 아직 모름.
            void Pump(double seconds)
            {
                for (var t = 0.0; t < seconds; t += 0.05)
                {
                    net.Advance(0.05);
                    server.Update(0.05);
                    second?.Update(0.05);
                }
            }
            for (var i = 0; i < 20; i++)
            {
                net.Advance(0.05);
                server.Update(0.05);
                first.Update(0.05);
            }
            Assert.IsTrue(first.IsJoined);
            Pump(4);
            second = new NetClient(new GameSession(), net.Client(), "철수") { Clock = () => net.Now, TimeoutSeconds = 30 };
            second.Connect();
            Pump(1);
            Assert.IsTrue(second.IsJoined, "이어받는다");
            CollectionAssert.AreEqual(new[] { "들어옴 1", "나감 1", "들어옴 1" }, seen);
            Assert.IsTrue(server.Players.Single().Connected);
        }

        // 받은 바이트를 한 번에 chunk바이트까지만 돌려주는 흐름(머리만 오고 내용은 아직 오는 중인 연결 흉내). Read에 넘어온 버퍼 중 가장 큰 크기를 남긴다.
        sealed class TrickleStream : Stream
        {
            readonly byte[] _data;
            readonly int _chunk;
            int _position;
            public int LargestBuffer;

            public TrickleStream(byte[] data, int chunk = int.MaxValue)
            {
                _data = data;
                _chunk = chunk;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                LargestBuffer = Math.Max(LargestBuffer, buffer.Length);
                var n = Math.Min(Math.Min(count, _chunk), _data.Length - _position);
                Buffer.BlockCopy(_data, _position, buffer, offset, n);
                _position += n;
                return n;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        [Test]
        public void WebSocket_머리에_적힌_길이만큼_미리_잡지_않고_받은_만큼만_잡는다()
        {
            // 64비트 길이 칸에 한도(32MB)를 적고 내용은 10바이트만 보낸 프레임. 머리 14바이트마다 32MB를 잡으면 연결 몇백 개로 서버 메모리가 바닥난다.
            var frame = new List<byte> { 0x82, 0x80 | 127 };
            for (var i = 7; i >= 0; i--) frame.Add((byte)((long)WebSocketFrames.MaxMessage >> (8 * i)));
            frame.AddRange(new byte[] { 1, 2, 3, 4 });
            frame.AddRange(new byte[10]);
            var stream = new TrickleStream(frame.ToArray());
            Assert.Throws<EndOfStreamException>(() => WebSocketFrames.ReadMessage(stream, null, out _));
            Assert.IsTrue(stream.LargestBuffer <= 1024 * 1024, $"잡은 버퍼 {stream.LargestBuffer}바이트");

            // 조각조각 오는 큰 메시지(가면 씀)도 그대로 받는다.
            var payload = Enumerable.Range(0, 300000).Select(i => (byte)(i * 7)).ToArray();
            var written = new MemoryStream();
            WebSocketFrames.Write(written, 2, payload, 0, payload.Length, true, new Random(3));
            var received = WebSocketFrames.ReadMessage(new TrickleStream(written.ToArray(), chunk: 1000), null, out var opcode);
            Assert.AreEqual(2, opcode);
            CollectionAssert.AreEqual(payload, received);
        }

        [Test]
        public void WebSocket_제어_프레임은_125바이트까지만_받는다()
        {
            // 큰 핑(규격은 125바이트까지)에 같은 크기의 퐁을 쌓게 하지 않는다.
            var pongs = 0;
            var big = new MemoryStream();
            WebSocketFrames.Write(big, 9, new byte[126], 0, 126, true, new Random(5));
            big.Position = 0;
            Assert.Throws<IOException>(() => WebSocketFrames.ReadMessage(big, _ => pongs++, out _));
            Assert.AreEqual(0, pongs);

            // 규격 안의 핑에는 퐁을 하고 다음 메시지를 받는다.
            var ok = new MemoryStream();
            WebSocketFrames.Write(ok, 9, new byte[125], 0, 125, true, new Random(6));
            WebSocketFrames.Write(ok, 2, new byte[] { 1, 2 }, 0, 2, true, new Random(7));
            ok.Position = 0;
            CollectionAssert.AreEqual(new byte[] { 1, 2 }, WebSocketFrames.ReadMessage(ok, _ => pongs++, out _));
            Assert.AreEqual(1, pongs);
        }

        // 코어 시험 실행기는 System.Net.Sockets를 직접 참조하지 않는다 — 날 TCP 연결(WebSocket을 흉내 내는 클라이언트)은 형식 이름으로 만든다.
        static (IDisposable tcp, Stream stream) RawTcp(int port)
        {
            var type = Type.GetType("System.Net.Sockets.TcpClient, System.Net.Sockets") ?? Type.GetType("System.Net.Sockets.TcpClient, System");
            var tcp = (IDisposable)Activator.CreateInstance(type, "127.0.0.1", port);
            return (tcp, (Stream)type.GetMethod("GetStream").Invoke(tcp, null));
        }

        static readonly byte[] Upgrade = System.Text.Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\nHost: 127.0.0.1\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nSec-WebSocket-Version: 13\r\n\r\n");

        [Test]
        public void WebSocket_서버는_연결을_알린_뒤에_그_연결에서_받은_것을_넘긴다()
        {
            var transport = new WebSocketServerTransport(0);
            transport.Start();
            var raws = new List<IDisposable>();
            try
            {
                // 악수 요청 바로 뒤에 첫 메시지를 붙여 보내는 클라이언트(또는 응답을 받자마자 Hello를 보내는 빠른 클라이언트).
                // 받은 것이 Connected보다 먼저 넘어가면 서버는 모르는 연결의 메시지로 버린다 — Hello를 잃은 클라이언트는 들어가지 못한 채 핑만 주고받는다.
                for (var i = 0; i < 5; i++)
                {
                    var (tcp, stream) = RawTcp(transport.Port);
                    raws.Add(tcp);
                    var frame = new MemoryStream();
                    WebSocketFrames.Write(frame, 2, new byte[] { (byte)NetMessage.Ping, 1, 2, 3 }, 0, 4, true, new Random(i));
                    var bytes = Upgrade.Concat(frame.ToArray()).ToArray();
                    stream.Write(bytes, 0, bytes.Length);
                }
                var connected = new HashSet<int>();
                var data = 0;
                var early = 0;
                var until = DateTime.UtcNow.AddSeconds(10);
                while (data < 5 && DateTime.UtcNow < until)
                {
                    while (transport.Poll(out var e))
                    {
                        if (e.Kind == NetEventKind.Connected) connected.Add(e.Connection);
                        else if (e.Kind == NetEventKind.Data)
                        {
                            data++;
                            if (!connected.Contains(e.Connection)) early++;
                        }
                    }
                    System.Threading.Thread.Sleep(1);
                }
                Assert.AreEqual(5, data);
                Assert.AreEqual(0, early, "받은 것이 연결 알림보다 먼저 왔다");
            }
            finally
            {
                foreach (var raw in raws) raw.Dispose();
                transport.Dispose();
            }
        }

        [Test]
        public void WebSocket_받는_쪽이_멈춰_보낼_목록이_넘쳐_닫으면_끊김을_알린다()
        {
            var transport = new WebSocketServerTransport(0);
            transport.Start();
            var (tcp, stream) = RawTcp(transport.Port);
            try
            {
                stream.Write(Upgrade, 0, Upgrade.Length);
                Assert.IsNotNull(WebSocketFrames.ReadHeader(stream));
                var connection = -1;
                var until = DateTime.UtcNow.AddSeconds(10);
                while (connection < 0 && DateTime.UtcNow < until)
                {
                    if (transport.Poll(out var e) && e.Kind == NetEventKind.Connected) connection = e.Connection;
                    else System.Threading.Thread.Sleep(1);
                }
                Assert.IsTrue(connection > 0, "연결됨");
                // 이 클라이언트는 더 읽지 않는다: 보낼 목록이 한도(8192개)를 넘을 만큼 보낸다.
                var message = new byte[1024];
                for (var i = 0; i < 30000; i++) transport.Send(connection, message, message.Length);
                var closed = false;
                until = DateTime.UtcNow.AddSeconds(10);
                while (!closed && DateTime.UtcNow < until)
                {
                    while (transport.Poll(out var e)) closed |= e.Kind == NetEventKind.Disconnected && e.Connection == connection;
                    System.Threading.Thread.Sleep(5);
                }
                Assert.IsTrue(closed, "전송이 스스로 닫은 연결도 끊김으로 알린다(서버가 그 사람을 끊긴 것으로 안다)");
            }
            finally
            {
                tcp.Dispose();
                transport.Dispose();
            }
        }

        [Test]
        public void WebSocket_핑만_쏟아붓고_읽지_않는_연결은_퐁을_한없이_쌓지_않고_끊는다()
        {
            var transport = new WebSocketServerTransport(0);
            transport.Start();
            var (tcp, stream) = RawTcp(transport.Port);
            try
            {
                stream.Write(Upgrade, 0, Upgrade.Length);
                Assert.IsNotNull(WebSocketFrames.ReadHeader(stream));
                // WebSocket 핑(제어 프레임)은 전송이 스스로 퐁을 한다 — 게임 쪽 메시지가 아니라 서버의 메시지 수 제한·시간 넘김에도 걸리지 않는다.
                var pings = new MemoryStream();
                var random = new Random(9);
                for (var i = 0; i < 60000; i++) WebSocketFrames.Write(pings, 9, new byte[125], 0, 125, true, random);
                var bytes = pings.ToArray();
                var closed = false;
                var until = DateTime.UtcNow.AddSeconds(15);
                var sender = System.Threading.Tasks.Task.Run(() =>
                {
                    try { stream.Write(bytes, 0, bytes.Length); }
                    catch (Exception) { }   // 서버가 끊으면 쓰기가 실패한다
                });
                while (!closed && DateTime.UtcNow < until)
                {
                    while (transport.Poll(out var e)) closed |= e.Kind == NetEventKind.Disconnected;
                    System.Threading.Thread.Sleep(5);
                }
                Assert.IsTrue(closed, "보낼 목록(퐁)이 한도를 넘으면 끊고 알린다");
                sender.Wait(TimeSpan.FromSeconds(5));
            }
            finally
            {
                tcp.Dispose();
                transport.Dispose();
            }
        }

        [Test]
        public void 방장이_방을_닫으면_다시_접속하지_않는다()
        {
            var net = new MemoryNetwork { Manual = true, Latency = 0.01 };
            var server = new NetServer(ServerSession(), net.Server());
            server.Start();
            server.AddLocalPlayer("방장");
            var client = new NetClient(new GameSession(), net.Client(), "철수");
            client.Connect();
            for (var i = 0; i < 20; i++)
            {
                net.Advance(0.05);
                server.Update(0.05);
                client.Update(0.05);
            }
            Assert.IsTrue(client.IsJoined);
            server.Dispose();
            for (var i = 0; i < 5; i++)
            {
                net.Advance(0.05);
                client.Update(0.05);
            }
            Assert.AreEqual(NetClientState.Closed, client.State);
            Assert.IsTrue(client.ServerClosed, "방이 닫혔다 — 다시 접속해도 소용없다");
        }

        [Test]
        public void 가짜_인증은_입장_표가_바뀌어도_이름으로_같은_계정()
        {
            var auth = new FakeAuthenticator();
            string a = null, b = null;
            auth.Authenticate(new NetHello { Name = "철수", Token = "AAAA" }, admission => a = admission.Account);
            auth.Authenticate(new NetHello { Name = "철수", Token = "BBBB" }, admission => b = admission.Account);
            Assert.AreEqual("name:철수", a);
            Assert.AreEqual(a, b);
        }

        [Test]
        public void 주인만_받는_이벤트와_서버만_아는_이벤트()
        {
            var net = new MemoryNetwork { Manual = true, Latency = 0.01 };
            var server = new NetServer(ServerSession(), net.Server());
            server.Start();
            var a = new NetClient(new GameSession(), net.Client(), "철수");
            var b = new NetClient(new GameSession(), net.Client(), "영희");
            var drawnA = new List<NtDrawn>();
            var drawnB = new List<NtDrawn>();
            var secrets = new List<NtSecret>();
            a.Session.Events.SubscribeAfterAction<NtDrawn>(drawnA.Add);
            b.Session.Events.SubscribeAfterAction<NtDrawn>(drawnB.Add);
            a.Session.Events.SubscribeAfterAction<NtSecret>(secrets.Add);
            a.Connect();
            b.Connect();
            void Pump()
            {
                for (var i = 0; i < 20; i++)
                {
                    net.Advance(0.01);
                    server.Update(0.01);
                    a.Update(0.01);
                    b.Update(0.01);
                }
            }
            Pump();
            b.Session.Do(new NtDraw { card = "ice" });
            Pump();
            Assert.AreEqual(0, drawnA.Count, "남이 뽑은 카드 이벤트는 오지 않는다");
            Assert.AreEqual(1, drawnB.Count);
            Assert.AreEqual("ice", drawnB[0].card);
            Assert.AreEqual(0, secrets.Count, "서버만 아는 이벤트는 보내지 않는다");
            Assert.AreEqual(6.0, a.Session.Data.GetNumber("gold"), "데이터는 그대로 온다");
        }

        [Test]
        public void 응답이_없으면_서버도_클라이언트도_끊는다()
        {
            var net = new MemoryNetwork { Manual = true, Latency = 0.01 };
            var server = new NetServer(ServerSession(), net.Server(), new NetServerOptions { TimeoutSeconds = 5 }) { Clock = () => net.Now };
            server.Start();
            var client = new NetClient(new GameSession(), net.Client(), "철수") { Clock = () => net.Now, TimeoutSeconds = 5 };
            client.Connect();
            for (var i = 0; i < 20; i++)
            {
                net.Advance(0.05);
                server.Update(0.05);
                client.Update(0.05);
            }
            Assert.IsTrue(client.IsJoined);
            // 클라이언트가 멈춤(핑 없음): 서버가 5초 뒤 끊는다.
            for (var i = 0; i < 70; i++)
            {
                net.Advance(0.1);
                server.Update(0.1);
            }
            Assert.IsFalse(server.Players.Single().Connected, "응답이 없는 사람은 끊긴다");

            // 반대로 서버가 멈춤: 클라이언트가 5초 뒤 스스로 끊긴 것으로 본다(서버가 끊은 것이 아니라 네트워크 문제 — 다시 접속해 볼 만하다).
            var server2 = new NetServer(ServerSession(), new MemoryNetwork { Manual = true }.Server());
            var net2 = new MemoryNetwork { Manual = true, Latency = 0.01 };
            var live = new NetServer(ServerSession(), net2.Server()) { Clock = () => net2.Now };
            live.Start();
            var c2 = new NetClient(new GameSession(), net2.Client(), "영희") { Clock = () => net2.Now, TimeoutSeconds = 5 };
            c2.Connect();
            for (var i = 0; i < 20; i++)
            {
                net2.Advance(0.05);
                live.Update(0.05);
                c2.Update(0.05);
            }
            Assert.IsTrue(c2.IsJoined);
            string closed = null;
            c2.Closed += reason => closed = reason;
            for (var i = 0; i < 70; i++)
            {
                net2.Advance(0.1);
                c2.Update(0.1);
            }
            Assert.AreEqual(NetClientState.Closed, c2.State);
            Assert.IsNotNull(closed);
            Assert.IsFalse(c2.ServerClosed, "서버가 끊은 것이 아니다");
            Assert.IsTrue(c2.Reached);
            server2.Dispose();
        }

        [Test]
        public void 매칭큐_부하_시험_무작위_이탈_거절_재접속에도_불변식이_지켜진다()
        {
            var net = new MemoryNetwork { Manual = true, Latency = 0.02, Jitter = 0.01 };
            var folder = Path.Combine(Path.GetTempPath(), "momoi-load-" + Guid.NewGuid().ToString("N"));
            var matchmaker = new LocalMatchmaker { PlayersPerMatch = 2, BaseWindow = 50, WidenPerSecond = 50, MaxWaitSeconds = 5 };
            var hub = new NetHub(net.Server(), room =>
            {
                var s = new GameSession();
                s.Data.Define("party", DataZone.Session, new NtParty { units = { new NtUnit { id = "a", hp = 10 } } });
                return s;
            }, new FakeAuthenticator(), matchmaker, new FilePlayerStore(folder), new NetHubOptions { AcceptSeconds = 2, ReadySeconds = 3 }) { Clock = () => net.Now };
            hub.Start();
            var random = new Random(7);
            var names = Enumerable.Range(1, 16).Select(i => "p" + i).ToList();
            var clients = new Dictionary<string, NetClient>();
            NetClient Spawn(string name)
            {
                var c = new NetClient(new GameSession(), net.Client(), name) { Clock = () => net.Now };
                c.Connect();
                return clients[name] = c;
            }
            foreach (var name in names) Spawn(name);
            var violations = new List<string>();
            var ended = 0;

            for (var tick = 0; tick < 1500; tick++)
            {
                net.Advance(0.02);
                hub.Update(0.02);
                foreach (var c in clients.Values.ToList()) c.Update(0.02);

                // 무작위 행동
                var name = names[random.Next(names.Count)];
                var client = clients[name];
                switch (random.Next(12))
                {
                    case 0:
                    case 1:
                    case 2:
                        if (client.State == NetClientState.Lobby) client.Queue();
                        break;
                    case 3:
                        if (client.State == NetClientState.Queued) client.CancelQueue();
                        break;
                    case 4:
                    case 5:
                    case 6:
                        if (client.State == NetClientState.MatchFound) client.AnswerMatch(true);
                        break;
                    case 7:
                        if (client.State == NetClientState.MatchFound) client.AnswerMatch(random.Next(3) > 0);
                        break;
                    case 8:
                        if (client.IsJoined) client.Session.Do(new NtStrike { unit = client.Session.Data.Get<NtParty>("party").units[0], damage = 1 });
                        break;
                    case 9:
                        // 갑자기 나감 → 잠시 뒤 다시 접속
                        if (random.Next(4) == 0)
                        {
                            client.Dispose();
                            Spawn(name);
                        }
                        break;
                    case 10:
                        var room = hub.Rooms.FirstOrDefault(r => r.Ready && random.Next(3) == 0);
                        if (room != null)
                        {
                            hub.EndMatch(room, new[] { new NetMatchResult { Player = 1, Place = 1 }, new NetMatchResult { Player = 2, Place = 2 } });
                            ended++;
                        }
                        break;
                }

                // 불변식
                var inRooms = hub.Rooms.Where(r => !r.Ended).SelectMany(r => r.Accounts).ToList();
                if (inRooms.Count != inRooms.Distinct().Count()) violations.Add($"{tick}: 한 계정이 두 매치에");
                foreach (var room in hub.Rooms)
                    if (room.Accounts.Count != 2) violations.Add($"{tick}: 정원이 아닌 방 {room.MatchId}");
                foreach (var account in inRooms)
                    if (matchmaker.IsQueued(account)) violations.Add($"{tick}: {account}이(가) 방에 있으면서 줄에도");
            }

            Assert.IsEmpty(violations.Take(5), "불변식");
            Assert.GreaterOrEqual(ended, 1, "매치가 실제로 이루어지고 끝났다");
            foreach (var c in clients.Values) c.Dispose();
            hub.Dispose();
            try { Directory.Delete(folder, true); } catch (Exception) { }
        }
    }
}
