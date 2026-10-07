using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GBible.Tests
{
    // 현실 시각(Game.Now·Day·Week)과 되돌리기 지점(HistoryMark·UndoTo).
    public class RealTimeTests
    {
        [Serializable] public class RtRun { public List<string> seen = new List<string>(); public int turn; }

        sealed class Record : GameAction
        {
            public bool nested;
            protected override void Run()
            {
                Game.Get<RtRun>("run").seen.Add(Game.Now.ToString("HH:mm:ss"));
                if (nested) Game.Do(new Record());
            }
        }

        sealed class NextTurn : GameAction
        {
            protected override void Run() => Game.Get<RtRun>("run").turn++;
        }

        DateTime _now = new DateTime(2026, 10, 4, 3, 30, 0, DateTimeKind.Utc);
        GameSession _session;
        GameApi Game => _session.Game;
        RtRun Run => _session.Data.Get<RtRun>("run");

        [SetUp]
        public void SetUp()
        {
            // NUnit은 한 객체로 여러 시험을 돌린다 — 앞 시험이 옮긴 시각이 남지 않게 매번 처음 시각으로.
            _now = new DateTime(2026, 10, 4, 3, 30, 0, DateTimeKind.Utc);
            _session = new GameSession(new SessionOptions { Seed = 1, RealClock = () => _now, TimeZone = TimeZoneInfo.Utc });
            _session.Data.Define("run", DataZone.Session, new RtRun(), "판");
        }

        [Test]
        public void 행동_안의_시각은_바깥_행동이_시작한_순간_하나다()
        {
            var record = Game.Do(new Record { nested = true });
            CollectionAssert.AreEqual(new[] { "03:30:00", "03:30:00" }, Run.seen);
            Assert.AreEqual(_now, Game.Now);
            Assert.AreNotEqual(0, record.Action.utc, "행동에 시각이 기록된다");

            _now = _now.AddMinutes(5);
            var replay = new Record { utc = record.Action.utc };
            Game.Do(replay);
            Assert.AreEqual("03:30:00", Run.seen[2], "받은 행동(시각이 이미 있음)은 그 시각으로 돈다");
            Assert.AreEqual("03:35:00", Game.Now.ToString("HH:mm:ss"), "행동 밖에서는 지금 시각");
        }

        sealed class NowProbe : GameAction
        {
            public DateTime seen;
            public long day;
            protected override void Run()
            {
                seen = Game.Now;
                day = Game.Day();
            }
        }

        [Test]
        public void 시계가_Kind_없는_시각이나_지역_시각을_줘도_행동_안팎의_시각이_같다()
        {
            var unspecified = new DateTime(2026, 10, 4, 3, 30, 0, DateTimeKind.Unspecified);
            var session = new GameSession(new SessionOptions { RealClock = () => unspecified, TimeZone = TimeZoneInfo.Utc });
            var probe = new NowProbe();
            session.Do(probe);
            Assert.AreEqual(new DateTime(2026, 10, 4, 3, 30, 0, DateTimeKind.Utc), probe.seen, "Kind가 없으면 UTC로 본다");
            Assert.AreEqual(probe.seen, session.Game.Now, "행동 밖도 같은 순간");
            Assert.AreEqual(DateTimeKind.Utc, session.Game.Now.Kind);
            Assert.AreEqual(probe.day, session.Game.Day(), "날짜 번호도 같다");

            var local = new DateTime(2026, 10, 4, 12, 30, 0, DateTimeKind.Local);
            var localSession = new GameSession(new SessionOptions { RealClock = () => local, TimeZone = TimeZoneInfo.Utc });
            var localProbe = new NowProbe();
            localSession.Do(localProbe);
            Assert.AreEqual(local.ToUniversalTime(), localProbe.seen, "지역 시각은 UTC로 바꾼다");
            Assert.AreEqual(localProbe.seen, localSession.Game.Now);
            Assert.AreEqual(DateTimeKind.Utc, localSession.Game.Now.Kind);
        }

        [Test]
        public void 멀티에서_시간대를_비우면_서버와_클라이언트가_UTC로_같은_날을_센다()
        {
            var clock = new DateTime(2026, 10, 4, 23, 30, 0, DateTimeKind.Utc);
            var utcDay = (long)Math.Floor((clock - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalDays);
            var server = new GameSession(new SessionOptions { RealClock = () => clock });
            var client = new GameSession(new SessionOptions { RealClock = () => clock });
            var network = new MemoryNetwork();
            new NetServer(server, network.Server());
            new NetClient(client, network.Client(), "철수");
            Assert.AreEqual(utcDay, server.Game.Day(), "서버(흔히 UTC 컴퓨터)와");
            Assert.AreEqual(utcDay, client.Game.Day(), "클라이언트(제 지역 시각)가 같은 날을 센다");
            server.Game.Day();
            Assert.AreEqual(1, server.Journal.Entries.Count(e => e.Kind == JournalKind.Warning && e.Message.Contains("SessionOptions.TimeZone")), "한 번 알린다");

            var single = new GameSession(new SessionOptions { RealClock = () => clock });
            var mine = TimeZoneInfo.ConvertTimeFromUtc(clock, TimeZoneInfo.Local);
            Assert.AreEqual((long)Math.Floor((mine - new DateTime(1970, 1, 1)).TotalDays), single.Game.Day(), "싱글은 이 컴퓨터의 시간대 그대로");
            Assert.IsFalse(single.Journal.Entries.Any(e => e.Message.Contains("SessionOptions.TimeZone")));

            var kst = TimeZoneInfo.CreateCustomTimeZone("KST", TimeSpan.FromHours(9), "KST", "KST");
            var zoned = new GameSession(new SessionOptions { RealClock = () => clock, TimeZone = kst });
            new NetServer(zoned, new MemoryNetwork().Server());
            Assert.AreEqual(utcDay + 1, zoned.Game.Day(), "정해 두면 그 시간대(23:30 UTC = 다음 날 08:30 KST)");
        }

        [Test]
        public void 날짜_번호는_초기화_시각에_바뀐다()
        {
            var before = Game.Day(4);
            _now = new DateTime(2026, 10, 4, 4, 0, 0, DateTimeKind.Utc);
            Assert.AreEqual(before + 1, Game.Day(4), "새벽 4시에 하루가 바뀐다");
            Assert.AreEqual(Game.Day(0), Game.Day(4), "4시가 지나면 자정 기준 날짜와 같다");
            Assert.AreEqual(new DateTime(2026, 10, 4) .Subtract(new DateTime(1970, 1, 1)).Days, Game.Day());

            _now = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);      // 일요일
            var week = Game.Week();
            _now = new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);       // 월요일 0시
            Assert.AreEqual(week + 1, Game.Week(), "월요일에 한 주가 바뀐다");
            Assert.AreEqual(Game.Week(start: DayOfWeek.Sunday), Game.Week(), "일요일 시작이면 일요일부터 같은 주");
        }

        [Test]
        public void 미리보기가_찍은_시각은_남지_않는다()
        {
            var action = new Record();
            Game.Preview(action, g => 0);
            Assert.AreEqual(0, action.utc);
            _now = _now.AddMinutes(10);
            Game.Do(action);
            Assert.AreEqual("03:40:00", Run.seen[0], "나중에 Do하면 그때 시각");
        }

        [Test]
        public void 스냅샷_방식의_지점은_보관_수가_차도_맞다()
        {
            var session = new GameSession(new SessionOptions { Seed = 1, History = HistoryMode.Snapshots, SnapshotLimit = 2 });
            session.Data.Define("run", DataZone.Session, new RtRun(), "판");
            session.Game.Do(new NextTurn());
            session.CaptureSnapshot();                    // turn 1
            var mark = session.HistoryMark;
            session.Game.Do(new NextTurn());
            session.CaptureSnapshot();                    // turn 2
            session.Game.Do(new NextTurn());
            session.CaptureSnapshot();                    // turn 3 (turn 1 스냅샷은 보관 수 때문에 빠짐)
            Assert.IsTrue(session.HistoryMark > mark);
            session.UndoTo(mark + 1);
            Assert.AreEqual(2, session.Data.Get<RtRun>("run").turn, "지점 번호까지(turn 2 스냅샷)로 되감는다");
        }

        [Test]
        public void 되돌리기_지점까지_한_번에_되돌린다()
        {
            Game.Do(new NextTurn());
            var mark = _session.HistoryMark;
            Game.Do(new NextTurn());
            Game.Do(new NextTurn());
            Assert.AreEqual(3, Run.turn);
            Assert.AreEqual(2, _session.UndoTo(mark));
            Assert.AreEqual(1, Run.turn);
            Assert.AreEqual(0, _session.UndoTo(mark), "이미 그 지점이면 아무것도 하지 않는다");
            Assert.AreEqual(1, _session.UndoTo(0));
            Assert.AreEqual(0, Run.turn);
        }
    }
}
