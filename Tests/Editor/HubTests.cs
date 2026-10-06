using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace GBible.Tests
{
    // 매칭큐 서버(NetHub): 로비 → 줄 → 성사 → 수락 → 방 → 끝 → 점수. 가짜 인증·로컬 매칭·파일 저장.
    public class HubTests
    {
        sealed class Rig : IDisposable
        {
            public readonly MemoryNetwork Net = new MemoryNetwork { Manual = true, Latency = 0.01 };
            public readonly string Folder = Path.Combine(Path.GetTempPath(), "momoi-hub-" + Guid.NewGuid().ToString("N"));
            public readonly LocalMatchmaker Matchmaker = new LocalMatchmaker { PlayersPerMatch = 2 };
            public NetHub Hub;
            public FilePlayerStore Store;
            public readonly List<NetClient> Clients = new List<NetClient>();
            public readonly List<NetRoom> Ready = new List<NetRoom>();

            public Rig(NetHubOptions options = null)
            {
                Store = new FilePlayerStore(Folder);
                Open(options);
            }

            public void Open(NetHubOptions options = null)
            {
                Hub = new NetHub(Net.Server(), room => NewMatchSession(), new FakeAuthenticator(), Matchmaker, Store, options) { Clock = () => Net.Now };
                Hub.RoomReady += Ready.Add;
                Hub.Start();
            }

            public static GameSession NewMatchSession()
            {
                var session = new GameSession();
                session.Data.Define("party", DataZone.Session, new NtParty { units = { new NtUnit { id = "a", hp = 10 } } });
                session.Data.Define("lastActor", DataZone.Session, 0.0);
                return session;
            }

            public NetClient Connect(string name)
            {
                var client = new NetClient(new GameSession(), Net.Client(), name);
                Clients.Add(client);
                client.Connect();
                Pump();
                return client;
            }

            public void Pump(int steps = 30, double step = 0.01)
            {
                for (var i = 0; i < steps; i++)
                {
                    Net.Advance(step);
                    Hub.Update(step);
                    foreach (var client in Clients) client.Update(step);
                }
            }

            public void Dispose()
            {
                foreach (var client in Clients) client.Dispose();
                Hub.Dispose();
                try { Directory.Delete(Folder, true); } catch (Exception) { }
            }
        }

        static NtUnit UnitA(GameSession session) => session.Data.Get<NtParty>("party").units[0];

        [Test]
        public void 줄을_서고_수락하면_방에서_대전하고_끝나면_점수가_바뀐다()
        {
            using (var rig = new Rig())
            {
                var a = rig.Connect("철수");
                var b = rig.Connect("영희");
                Assert.AreEqual(NetClientState.Lobby, a.State);
                Assert.AreEqual(1000.0, a.Account.Rating);

                a.Queue();
                b.Queue();
                rig.Pump();
                Assert.AreEqual(NetClientState.MatchFound, a.State);
                Assert.IsNotNull(b.Match);
                Assert.AreEqual(2, b.Match.Players.Length);

                a.AnswerMatch(true);
                b.AnswerMatch(true);
                rig.Pump();
                Assert.IsTrue(a.IsJoined && b.IsJoined, "모두 방에 들어갔다");
                Assert.AreEqual(1, a.PlayerId, "묶인 차례대로 번호");
                Assert.AreEqual(2, b.PlayerId);
                Assert.AreEqual(1, rig.Ready.Count, "정원이 다 들어오면 RoomReady");
                var room = rig.Hub.Rooms.Single();

                b.Session.Do(new NtStrike { unit = UnitA(b.Session), damage = 4 });
                rig.Pump();
                Assert.AreEqual(6, UnitA(a.Session).hp, "다른 사람의 행동이 온다");
                Assert.AreEqual(2.0, room.Session.Data.GetNumber("lastActor"));

                var ends = new List<NetMatchEnd>();
                a.MatchEnded += ends.Add;
                b.MatchEnded += ends.Add;
                rig.Hub.EndMatch(room, new[] { new NetMatchResult { Player = 2, Place = 1 }, new NetMatchResult { Player = 1, Place = 2 } });
                rig.Pump();
                Assert.AreEqual(2, ends.Count);
                Assert.AreEqual(NetClientState.Lobby, a.State, "로비로 돌아온다");
                Assert.AreEqual(984.0, a.Account.Rating, "진 쪽 -16");
                Assert.AreEqual(1016.0, b.Account.Rating, "이긴 쪽 +16");
                Assert.AreEqual(1, rig.Store.Load("name:영희").wins, "전적은 서버 저장소에");
                Assert.AreEqual(0, rig.Hub.Rooms.Count);
                Assert.AreEqual(0, rig.Store.OpenMatches().Count(), "끝난 매치는 진행 중 목록에서 빠진다");

                // 다음 매치: 지난 판 데이터는 비워지고 새 판을 받는다.
                a.Queue();
                b.Queue();
                rig.Pump();
                a.AnswerMatch(true);
                b.AnswerMatch(true);
                rig.Pump();
                Assert.AreEqual(10, UnitA(a.Session).hp, "새 매치의 처음 모습");
            }
        }

        [Test]
        public void 거절하면_수락한_사람은_다시_줄을_서고_시간이_지나도_취소된다()
        {
            using (var rig = new Rig(new NetHubOptions { AcceptSeconds = 1 }))
            {
                var a = rig.Connect("철수");
                var b = rig.Connect("영희");
                var cancelled = new List<(string reason, bool requeued)>();
                a.MatchCancelled += (reason, requeued) => cancelled.Add((reason, requeued));
                a.Queue();
                b.Queue();
                rig.Pump();
                a.AnswerMatch(true);
                b.AnswerMatch(false);
                rig.Pump();
                Assert.AreEqual(NetClientState.Queued, a.State, "수락한 사람은 다시 줄");
                Assert.AreEqual(NetClientState.Lobby, b.State, "거절한 사람은 로비");
                Assert.AreEqual(1, cancelled.Count);
                Assert.IsTrue(cancelled[0].requeued);

                b.Queue();
                rig.Pump();
                Assert.AreEqual(NetClientState.MatchFound, a.State);
                rig.Pump(150);
                Assert.AreEqual(NetClientState.Lobby, a.State, "아무도 수락하지 않고 시간이 지나면 취소");
                Assert.AreEqual(0, rig.Hub.Rooms.Count);
            }
        }

        [Test]
        public void 매치_중에_끊겼다_다시_오면_그_매치로_들어간다()
        {
            using (var rig = new Rig())
            {
                var a = rig.Connect("철수");
                var b = rig.Connect("영희");
                a.Queue();
                b.Queue();
                rig.Pump();
                a.AnswerMatch(true);
                b.AnswerMatch(true);
                rig.Pump();
                b.Session.Do(new NtStrike { unit = UnitA(b.Session), damage = 3 });
                rig.Pump();

                a.Disconnect();
                rig.Pump();
                Assert.IsFalse(rig.Hub.Rooms.Single().Server.FindPlayer(1).Connected);
                var again = rig.Connect("철수");
                rig.Pump();
                Assert.IsTrue(again.IsJoined, "로비를 거치지 않고 그 매치로");
                Assert.AreEqual(1, again.PlayerId, "같은 자리");
                Assert.AreEqual(7, UnitA(again.Session).hp, "지금 모습");
            }
        }

        [Test]
        public void 서버를_다시_켜면_진행_중인_매치를_이어_연다()
        {
            using (var rig = new Rig())
            {
                var a = rig.Connect("철수");
                var b = rig.Connect("영희");
                a.Queue();
                b.Queue();
                rig.Pump();
                a.AnswerMatch(true);
                b.AnswerMatch(true);
                rig.Pump();
                a.Session.Do(new NtStrike { unit = UnitA(a.Session), damage = 5 });
                rig.Pump();
                Assert.AreEqual(1, rig.Store.OpenMatches().Count(), "행동마다 저장");

                // 서버가 꺼졌다 켜짐: 모두 끊기고 새 서버가 저장소에서 매치를 다시 연다.
                rig.Hub.Dispose();
                rig.Pump(5);
                rig.Open();
                Assert.AreEqual(1, rig.Hub.ResumeOpenMatches());
                var a2 = rig.Connect("철수");
                var b2 = rig.Connect("영희");
                rig.Pump();
                Assert.IsTrue(a2.IsJoined && b2.IsJoined);
                Assert.AreEqual(5, UnitA(b2.Session).hp, "저장된 판에서 이어 간다");
                Assert.AreEqual(2, b2.PlayerId);
            }
        }

        [Test]
        public void 실력_차이가_크면_기다린_뒤에야_묶인다()
        {
            var matchmaker = new LocalMatchmaker { PlayersPerMatch = 2, BaseWindow = 100, WidenPerSecond = 10, MaxWaitSeconds = 60 };
            matchmaker.Enqueue(new NetTicket { Account = "x", Rating = 1000, QueuedAt = 0 });
            matchmaker.Enqueue(new NetTicket { Account = "y", Rating = 1400, QueuedAt = 0 });
            matchmaker.Tick(10);
            Assert.IsFalse(matchmaker.TryTake(out _), "폭 200 < 차이 400");
            matchmaker.Tick(31);
            Assert.IsTrue(matchmaker.TryTake(out var match), "폭 410 ≥ 400");
            Assert.AreEqual(2, match.Tickets.Count);

            matchmaker.Enqueue(new NetTicket { Account = "p", Rating = 1000, QueuedAt = 0 });
            matchmaker.Enqueue(new NetTicket { Account = "q", Rating = 3000, QueuedAt = 0 });
            matchmaker.Tick(59);
            Assert.IsFalse(matchmaker.TryTake(out _));
            matchmaker.Tick(60);
            Assert.IsTrue(matchmaker.TryTake(out _), "최대 대기를 넘기면 아무나와");
        }
    }
}
