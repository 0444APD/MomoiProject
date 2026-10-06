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
