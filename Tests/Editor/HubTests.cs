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

            public void Open(NetHubOptions options = null, INetPlayerStore store = null)
            {
                Hub = new NetHub(Net.Server(), room => NewMatchSession(), new FakeAuthenticator(), Matchmaker, store ?? Store, options) { Clock = () => Net.Now };
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

        // 계정 읽기·저장이 실패하는 저장소(디스크 오류 흉내). 나머지는 안쪽 저장소에 넘긴다.
        sealed class FlakyStore : INetPlayerStore
        {
            readonly INetPlayerStore _inner;
            public string FailLoad;
            public string FailSave;

            public FlakyStore(INetPlayerStore inner) => _inner = inner;

            public NetAccount Load(string id) => id == FailLoad ? throw new IOException("읽기 오류(시험)") : _inner.Load(id);

            public void Save(NetAccount account)
            {
                if (account?.id == FailSave) throw new IOException("디스크 오류(시험)");
                _inner.Save(account);
            }

            public void SaveMatch(string matchId, string state) => _inner.SaveMatch(matchId, state);
            public string LoadMatch(string matchId) => _inner.LoadMatch(matchId);
            public IEnumerable<string> OpenMatches() => _inner.OpenMatches();
            public void CloseMatch(string matchId) => _inner.CloseMatch(matchId);
        }

        // 두 사람이 줄을 서고 수락해 방에 들어간다(묶인 차례 = 플레이어 번호).
        static NetRoom StartMatch(Rig rig, NetClient a, NetClient b)
        {
            a.Queue();
            b.Queue();
            rig.Pump();
            a.AnswerMatch(true);
            b.AnswerMatch(true);
            rig.Pump();
            return rig.Hub.Rooms.Single();
        }

        static string TempFolder() => Path.Combine(Path.GetTempPath(), "momoi-store-" + Guid.NewGuid().ToString("N"));

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
        public void 로비에서_같은_계정이_다시_오면_옛_연결이_조용할_때만_이어받는다()
        {
            using (var rig = new Rig())
            {
                var a = rig.Connect("철수");
                Assert.AreEqual(NetClientState.Lobby, a.State);

                // 옛 연결이 살아 있으면 새 연결만 끊는다(거절이 아니다 — 잠시 뒤 다시 시도할 수 있다).
                var b = rig.Connect("철수");
                rig.Pump();
                Assert.AreEqual(NetClientState.Lobby, a.State, "옛 연결은 그대로");
                Assert.AreNotEqual(NetClientState.Lobby, b.State);
                Assert.AreEqual(1, rig.Hub.LobbyCount);

                // 옛 연결이 3초 넘게 조용하면(네트워크가 끊겼는데 서버는 아직 모름) 새 연결이 이어받는다.
                rig.Clients.Remove(a);
                rig.Pump(400);
                var c = rig.Connect("철수");
                rig.Pump();
                Assert.AreEqual(NetClientState.Lobby, c.State, "이어받는다");
                Assert.AreEqual(1, rig.Hub.LobbyCount);
                a.Dispose();
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

        [Test]
        public void 결과를_저장하다_한_계정이_실패해도_점수는_한_번만_바뀌고_방은_닫힌다()
        {
            using (var rig = new Rig())
            {
                var store = new FlakyStore(rig.Store);
                rig.Hub.Dispose();
                rig.Open(store: store);
                var logs = new List<string>();
                rig.Hub.Log += logs.Add;
                var a = rig.Connect("철수");
                var b = rig.Connect("영희");
                var room = StartMatch(rig, a, b);
                var ends = new List<NetMatchEnd>();
                a.MatchEnded += ends.Add;
                b.MatchEnded += ends.Add;

                // 이긴 영희를 먼저 저장하고, 진 철수를 저장하다 디스크 오류.
                store.FailSave = "name:철수";
                rig.Hub.EndMatch(room, new[] { new NetMatchResult { Player = 2, Place = 1 }, new NetMatchResult { Player = 1, Place = 2 } });
                Assert.DoesNotThrow(() => rig.Pump(), "저장 실패가 서버 틱을 멈추지 않는다");
                rig.Pump();
                Assert.AreEqual(1016.0, rig.Store.Load("name:영희").rating, "다음 틱에 다시 반영되지 않는다(한 번만 +16)");
                Assert.AreEqual(1, rig.Store.Load("name:영희").wins);
                Assert.AreEqual(1000.0, rig.Store.Load("name:철수").rating, "저장하지 못한 계정은 그대로");
                Assert.AreEqual(0, rig.Hub.Rooms.Count, "방은 닫힌다");
                Assert.AreEqual(0, rig.Store.OpenMatches().Count());
                Assert.AreEqual(2, ends.Count, "둘 다 끝을 받는다");
                Assert.AreEqual(0.0, ends.Single(e => e.Place == 2).RatingDelta, "저장하지 못한 점수는 알리지 않는다");
                Assert.AreEqual(1000.0, a.Account.Rating);
                Assert.AreEqual(1016.0, b.Account.Rating);
                Assert.IsTrue(logs.Any(l => l.Contains("name:철수") && l.Contains("저장하지 못했습니다")), string.Join("\n", logs));
            }
        }

        [Test]
        public void 결과를_반영할_때_읽지_못한_계정은_덮어쓰지_않고_다른_사람의_결과는_반영한다()
        {
            using (var rig = new Rig())
            {
                var store = new FlakyStore(rig.Store);
                rig.Hub.Dispose();
                rig.Open(store: store);
                var a = rig.Connect("철수");
                var b = rig.Connect("영희");
                var room = StartMatch(rig, a, b);
                store.FailLoad = "name:철수";
                rig.Hub.EndMatch(room, new[] { new NetMatchResult { Player = 2, Place = 1 }, new NetMatchResult { Player = 1, Place = 2 } });
                Assert.DoesNotThrow(() => rig.Pump());
                Assert.AreEqual(1016.0, rig.Store.Load("name:영희").rating, "읽지 못한 쪽은 마지막으로 안 점수(1000)로 계산에 넣는다");
                Assert.AreEqual(0, rig.Store.Load("name:철수").losses, "읽지 못한 계정은 옛 값으로 덮어쓰지 않는다");
                Assert.AreEqual(0, rig.Hub.Rooms.Count);
            }
        }

        [Test]
        public void 계정_파일이_깨지면_바로_전_저장에서_되살리고_깨진_파일은_지우지_않고_옆에_둔다()
        {
            var folder = TempFolder();
            try
            {
                var store = new FilePlayerStore(folder);
                var logs = new List<string>();
                store.Log += logs.Add;
                store.Save(new NetAccount { id = "name:철수", rating = 1100, values = { ["gold"] = "50" } });
                store.Save(new NetAccount { id = "name:철수", rating = 1200, values = { ["gold"] = "70" } });
                var accounts = Path.Combine(folder, "accounts");
                var path = Directory.GetFiles(accounts).Single(f => f.EndsWith(".json", StringComparison.Ordinal));
                File.WriteAllText(path, "{\"id\":\"name:철수\",\"rati");   // 쓰다 잘린 파일

                var loaded = store.Load("name:철수");
                Assert.AreEqual(1100.0, loaded.rating, "바로 전 저장(.bak)");
                Assert.AreEqual("50", loaded.values["gold"]);
                Assert.AreEqual(1, Directory.GetFiles(accounts, "*.broken-*").Length, "깨진 파일은 옆에 남는다");
                Assert.IsTrue(logs.Any(l => l.Contains(".bak")), string.Join("\n", logs));

                loaded.rating = 1300;
                store.Save(loaded);
                Assert.AreEqual(1300.0, store.Load("name:철수").rating, "다음 저장부터 다시 본 파일");
            }
            finally
            {
                try { Directory.Delete(folder, true); } catch (Exception) { }
            }
        }

        [Test]
        public void 읽을_수_있는_저장이_없을_때만_없는_계정으로_보고_깨진_파일은_남긴다()
        {
            var folder = TempFolder();
            try
            {
                var store = new FilePlayerStore(folder);
                var logs = new List<string>();
                store.Log += logs.Add;
                store.Save(new NetAccount { id = "name:영희", rating = 1100 });
                var accounts = Path.Combine(folder, "accounts");
                var path = Directory.GetFiles(accounts).Single();
                File.WriteAllText(path, "<<<<<<< HEAD\n{\"id\":\"name:영희\"}\n=======\n");   // 병합 충돌 표시

                Assert.IsNull(store.Load("name:영희"), ".bak도 없으면 null(새 계정)");
                Assert.IsFalse(File.Exists(path));
                Assert.AreEqual(1, Directory.GetFiles(accounts, "*.broken-*").Length, "지우지 않는다");
                Assert.IsTrue(logs.Any(l => l.Contains("name:영희") && l.Contains("없는 계정")), string.Join("\n", logs));
            }
            finally
            {
                try { Directory.Delete(folder, true); } catch (Exception) { }
            }
        }

        [Test]
        public void 바꿔_넣는_사이에_꺼져_본_파일이_없으면_끝까지_쓴_임시_파일에서_되살린다()
        {
            var folder = TempFolder();
            try
            {
                var store = new FilePlayerStore(folder);
                store.Save(new NetAccount { id = "name:철수", rating = 1100 });
                store.Save(new NetAccount { id = "name:철수", rating = 1200 });
                var path = Directory.GetFiles(Path.Combine(folder, "accounts")).Single(f => f.EndsWith(".json", StringComparison.Ordinal));
                // 새 내용(.tmp)은 다 썼고 옛 파일은 .bak으로 옮겼는데, 아직 바꿔 넣지 못하고 꺼진 모습.
                File.Move(path, path + ".tmp");
                Assert.AreEqual(1200.0, store.Load("name:철수").rating, "새 계정(1000)이 아니라 끝까지 쓴 새 내용");

                // 임시 파일마저 반쪽이면 바로 전 저장(.bak)으로.
                File.WriteAllText(path + ".tmp", "{\"id\":");
                Assert.AreEqual(1100.0, store.Load("name:철수").rating);
            }
            finally
            {
                try { Directory.Delete(folder, true); } catch (Exception) { }
            }
        }

        [Test]
        public void 긴_계정_키도_파일_이름_한도_안에서_서로_겹치지_않는다()
        {
            var folder = TempFolder();
            try
            {
                var store = new FilePlayerStore(folder);
                var prefix = "name:" + new string('가', 120);
                store.Save(new NetAccount { id = prefix + "하나", rating = 1001 });
                store.Save(new NetAccount { id = prefix + "둘", rating = 1002 });
                Assert.AreEqual(1001.0, store.Load(prefix + "하나").rating);
                Assert.AreEqual(1002.0, store.Load(prefix + "둘").rating, "앞부분이 같아도 키 전체의 해시로 갈린다");
                // ext4 이름 한도 255바이트: .bak·.broken-시각을 붙여도 들어가야 한다.
                foreach (var file in Directory.GetFiles(Path.Combine(folder, "accounts")))
                    Assert.IsTrue(System.Text.Encoding.UTF8.GetByteCount(Path.GetFileName(file) + ".bak.broken-20261007-000000-9") <= 255, Path.GetFileName(file));
            }
            finally
            {
                try { Directory.Delete(folder, true); } catch (Exception) { }
            }
        }

        [Test]
        public void 매치_파일도_바꿔_넣다_꺼지면_되살리고_끝나면_bak까지_지운다()
        {
            var folder = TempFolder();
            try
            {
                var store = new FilePlayerStore(folder);
                store.SaveMatch("m1-abc", "{\"matchId\":\"m1-abc\",\"run\":\"1\"}");
                store.SaveMatch("m1-abc", "{\"matchId\":\"m1-abc\",\"run\":\"2\"}");
                CollectionAssert.AreEqual(new[] { "m1-abc" }, store.OpenMatches().ToList(), ".bak이 있어도 한 번");
                var matches = Path.Combine(folder, "matches");
                var path = Directory.GetFiles(matches).Single(f => f.EndsWith(".json", StringComparison.Ordinal));
                File.Move(path, path + ".tmp");
                CollectionAssert.AreEqual(new[] { "m1-abc" }, store.OpenMatches().ToList(), "본 파일이 없어도 찾는다");
                StringAssert.Contains("\"2\"", store.LoadMatch("m1-abc"));

                store.CloseMatch("m1-abc");
                Assert.AreEqual(0, store.OpenMatches().Count(), "끝난 매치는 .bak에서 되살아나지 않는다");
                Assert.AreEqual(0, Directory.GetFiles(matches).Length);
            }
            finally
            {
                try { Directory.Delete(folder, true); } catch (Exception) { }
            }
        }

        [Test]
        public void 방을_열지_못하면_수락한_사람들은_로비로_돌아가고_다시_줄을_설_수_있다()
        {
            using (var rig = new Rig())
            {
                // 방 세션을 만들다 실패하는 서버(게임 코드 오류 흉내).
                rig.Hub.Dispose();
                var broken = true;
                rig.Hub = new NetHub(rig.Net.Server(), room => broken ? throw new InvalidOperationException("세션을 만들지 못함(시험)") : Rig.NewMatchSession(),
                                     new FakeAuthenticator(), rig.Matchmaker, rig.Store) { Clock = () => rig.Net.Now };
                rig.Hub.Start();
                var a = rig.Connect("철수");
                var b = rig.Connect("영희");
                a.Queue();
                b.Queue();
                rig.Pump();
                a.AnswerMatch(true);
                b.AnswerMatch(true);
                rig.Pump();
                Assert.AreEqual(0, rig.Hub.Rooms.Count);
                Assert.AreEqual(NetClientState.Lobby, a.State, "없어진 매치의 수락 대기에 갇히지 않는다");
                Assert.AreEqual(NetClientState.Lobby, b.State);

                broken = false;
                StartMatch(rig, a, b);
                Assert.IsTrue(a.IsJoined && b.IsJoined, "다시 줄을 서서 매치에 들어간다");
            }
        }

        [Test]
        public void 서버를_다시_켜_이어_연_매치는_정원이_돌아와도_첫_턴을_다시_시작하지_않는다()
        {
            using (var rig = new Rig())
            {
                // 게임의 첫 턴 시작(RoomReady): 판에 유닛을 하나 넣는다. 이어 연 매치에서 다시 불리면 저장된 판 위에서 또 시작한다.
                var started = 0;
                void Hook() => rig.Hub.RoomReady += room => room.Session.Do(new NtRecruit { newId = "r" + ++started });
                Hook();
                var a = rig.Connect("철수");
                var b = rig.Connect("영희");
                StartMatch(rig, a, b);
                Assert.AreEqual(1, started);
                a.Session.Do(new NtStrike { unit = UnitA(a.Session), damage = 5 });
                rig.Pump();

                // 서버가 꺼졌다 켜지기를 두 번(두 번째는 아무도 돌아오기 전에).
                for (var restart = 0; restart < 2; restart++)
                {
                    rig.Hub.Dispose();
                    rig.Pump(5);
                    rig.Open();
                    Hook();
                    Assert.AreEqual(1, rig.Hub.ResumeOpenMatches());
                }
                var a2 = rig.Connect("철수");
                var b2 = rig.Connect("영희");
                rig.Pump();
                Assert.IsTrue(a2.IsJoined && b2.IsJoined);
                Assert.AreEqual(1, started, "이미 시작한 매치다 — RoomReady를 다시 쏘지 않는다");
                Assert.AreEqual(2, b2.Session.Data.Get<NtParty>("party").units.Count, "저장된 판 그대로");
                Assert.AreEqual(5, UnitA(b2.Session).hp);
            }
        }

        [Test]
        public void 매치가_닫히는_사이에_보낸_행동을_로비에서_계속_기다리지_않는다()
        {
            using (var rig = new Rig())
            {
                var a = rig.Connect("철수");
                var b = rig.Connect("영희");
                var room = StartMatch(rig, a, b);
                rig.Hub.EndMatch(room, new[] { new NetMatchResult { Player = 1, Place = 1 }, new NetMatchResult { Player = 2, Place = 2 } });
                // 서버가 방을 닫는 틱. 끝났다는 알림이 오는 중에 철수가 행동을 보낸다 — 서버에는 방이 닫힌 뒤(로비에) 닿아 결과가 오지 않는다.
                rig.Hub.Update(0.001);
                Assert.AreEqual(0, rig.Hub.Rooms.Count);
                Assert.IsTrue(a.IsJoined, "아직 끝난 줄 모른다");
                a.Session.Do(new NtStrike { unit = UnitA(a.Session), damage = 1 });
                Assert.AreEqual(1, a.PendingActions);
                rig.Pump();
                Assert.AreEqual(NetClientState.Lobby, a.State);
                Assert.AreEqual(0, a.PendingActions, "결과가 올 수 없는 행동은 기다리지 않는다(다음 매치의 대화·화면이 막히지 않게)");

                StartMatch(rig, a, b);
                Assert.IsTrue(a.IsJoined);
                Assert.AreEqual(0, a.PendingActions);
            }
        }
    }
}
