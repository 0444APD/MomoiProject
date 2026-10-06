using System;
using System.Linq;
using NUnit.Framework;

namespace GBible.Tests
{
    [Serializable]
    public class RwMark : GameAction
    {
        protected override void Run() => Game.MarkRewind("턴");
    }

    [Serializable]
    public class RwHit : GameAction
    {
        public NtUnit unit;
        public int damage;

        protected override void Run() => unit.hp -= damage;
    }

    [Serializable]
    public class RwGold : GameAction
    {
        public double gold;

        protected override void Run() => Game.Session.Data.Set("gold", gold);
    }

    [Serializable]
    public class RwBack : GameAction
    {
        public int steps = 1;

        protected override void Run() => Game.Rewind(steps);
    }

    [Serializable]
    public class RwBackUnit : GameAction
    {
        public NtUnit unit;
        public int steps = 1;

        protected override void Run() => Game.RewindEntity(unit, steps);
    }

    [Serializable]
    public class RwBackKey : GameAction
    {
        public string key;
        public int steps = 1;

        protected override void Run() => Game.RewindKey(key, steps);
    }

    // 되감기 기믹: 지점 → 전체·키 하나·객체 하나 되돌리기(하나의 행동, Undo·멀티에 그대로).
    public class RewindTests
    {
        static GameSession NewSession()
        {
            var session = new GameSession();
            session.Data.Define("party", DataZone.Session, new NtParty { units = { new NtUnit { id = "a", hp = 10 }, new NtUnit { id = "b", hp = 8 } } });
            session.Data.Define("gold", DataZone.Session, 5.0);
            return session;
        }

        static NtUnit Unit(GameSession session, string id) => session.Data.Get<NtParty>("party").units.First(u => u.id == id);

        [Test]
        public void 전체를_두_지점_앞으로_되감고_Undo로_되돌린다()
        {
            var session = NewSession();
            session.Do(new RwMark());
            session.Do(new RwHit { unit = Unit(session, "a"), damage = 3 });
            session.Do(new RwMark());
            session.Do(new RwHit { unit = Unit(session, "b"), damage = 2 });
            Assert.AreEqual(2, session.Rewind.Count);

            session.Do(new RwBack { steps = 2 });
            Assert.AreEqual(10, Unit(session, "a").hp);
            Assert.AreEqual(8, Unit(session, "b").hp);
            Assert.AreEqual(2, session.Rewind.Count, "지점은 그대로 남는다");

            session.Undo();
            Assert.AreEqual(7, Unit(session, "a").hp, "되감기도 하나의 행동이라 Undo된다");
            Assert.AreEqual(6, Unit(session, "b").hp);
        }

        [Test]
        public void 객체_하나만_그때_모습으로_되돌린다()
        {
            var session = NewSession();
            session.Do(new RwMark());
            session.Do(new RwHit { unit = Unit(session, "a"), damage = 3 });
            session.Do(new RwHit { unit = Unit(session, "b"), damage = 2 });
            var a = Unit(session, "a");
            session.Do(new RwBackUnit { unit = a });
            Assert.AreEqual(10, Unit(session, "a").hp, "그 객체만");
            Assert.AreSame(a, Unit(session, "a"), "인스턴스는 그대로");
            Assert.AreEqual(6, Unit(session, "b").hp, "다른 객체는 그대로");
        }

        [Test]
        public void 키_하나만_되돌리고_너무_멀면_실패한다()
        {
            var session = NewSession();
            session.Do(new RwMark());
            session.Do(new RwGold { gold = 9 });
            session.Do(new RwHit { unit = Unit(session, "a"), damage = 1 });
            session.Do(new RwBackKey { key = "gold" });
            Assert.AreEqual(5.0, session.Data.GetNumber("gold"));
            Assert.AreEqual(9, Unit(session, "a").hp, "다른 키는 그대로");
            var failure = Assert.Throws<GameFailure>(() => session.Do(new RwBack { steps = 3 }));
            Assert.AreEqual(FailureKeys.RewindTooFar, failure.Key);
            Assert.Throws<InvalidOperationException>(() => session.Rewind.Rewind(1));
        }

        [Test]
        public void 멀티에서_지점은_서버만_알고_되감은_결과는_모두가_받는다()
        {
            var net = new MemoryNetwork { Manual = true, Latency = 0.02 };
            var serverSession = NewSession();
            var server = new NetServer(serverSession, net.Server());
            server.Start();
            var client = new NetClient(new GameSession(), net.Client(), "철수");
            client.Connect();
            void Pump()
            {
                for (var i = 0; i < 20; i++)
                {
                    net.Advance(0.01);
                    server.Update(0.01);
                    client.Update(0.01);
                }
            }
            Pump();
            serverSession.Do(new RwMark());
            client.Session.Do(new RwHit { unit = Unit(client.Session, "a"), damage = 4 });
            Pump();
            Assert.AreEqual(6, Unit(client.Session, "a").hp);
            Assert.IsFalse(client.Session.Data.IsDefined(RewindSystem.Key), "되감기 지점은 클라이언트에 보내지 않는다");

            client.Session.Do(new RwBack());
            Pump();
            Assert.AreEqual(10, Unit(serverSession, "a").hp);
            Assert.AreEqual(10, Unit(client.Session, "a").hp, "되감은 결과는 바뀐 곳으로 온다");
        }
    }
}
