using System;
using System.Collections.Generic;
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
