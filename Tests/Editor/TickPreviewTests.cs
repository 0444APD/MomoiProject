using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GBible.Tests
{
    // 고정 틱·미리보기 실행.
    public class TickPreviewTests
    {
        [Serializable] public class TpUnit { public string id; public int hp = 30; }
        [Serializable] public class TpRun { public List<TpUnit> units = new List<TpUnit> { new TpUnit { id = "hero" } }; public int energy = 3; public int rolls; }
        [Serializable, GameEvent("맞음")] public class TpHit { public string unit; }

        public sealed class Strike : GameAction
        {
            public TpUnit unit;
            public int amount;
            protected override void Run()
            {
                Game.Pay("run", "energy", 1);
                unit.hp -= amount + Game.Random("dmg").Range(0, 3);
                Game.Get<TpRun>("run").rolls++;
                Game.Session.Events.Publish(new TpHit { unit = unit.id });
            }
        }

        GameSession _session;
        GameApi Game => _session.Game;
        TpUnit Hero => Game.Find<TpUnit>("hero");

        GameSession NewSession(int tickRate = 0)
        {
            var session = new GameSession(new SessionOptions { Seed = 4, TickRate = tickRate });
            session.Data.Define("run", DataZone.Session, new TpRun(), "판");
            return session;
        }

        [SetUp]
        public void SetUp() => _session = NewSession();

        [Test]
        public void 고정_틱은_프레임과_상관없이_같은_간격으로_흐른다()
        {
            var a = NewSession(tickRate: 10);
            var b = NewSession(tickRate: 10);
            var ticksA = new List<double>();
            var ticksB = new List<double>();
            a.Ticked += dt => ticksA.Add(a.Clock.Time);
            b.Ticked += dt => ticksB.Add(b.Clock.Time);
            for (int i = 0; i < 60; i++) a.EndFrame(1.0 / 60);      // 60fps 1초
            for (int i = 0; i < 20; i++) b.EndFrame(1.0 / 20);      // 20fps 1초
            Assert.AreEqual(10, ticksA.Count, 1);
            Assert.AreEqual(ticksA.Count, ticksB.Count);
            CollectionAssert.AreEqual(ticksA.Select(t => Math.Round(t, 6)).ToList(), ticksB.Select(t => Math.Round(t, 6)).ToList());
            Assert.IsTrue(a.TickAlpha >= 0 && a.TickAlpha < 1);
        }

        [Test]
        public void 느린_프레임은_정해진_틱까지만_따라잡는다()
        {
            var session = NewSession(tickRate: 10);
            var count = 0;
            session.Ticked += _ => count++;
            session.EndFrame(5);
            Assert.AreEqual(GameSession.MaxTicksPerFrame, count);
        }

        [Test]
        public void 미리보기는_결과만_읽고_아무것도_남기지_않는다()
        {
            var seen = new List<TpHit>();
            _session.Events.SubscribeAfterAction<TpHit>(seen.Add);
            var changes = 0;
            _session.Data.Changed += _ => changes++;
            var journal = _session.Journal.LastId;
            var undo = _session.UndoCount;

            var predicted = Game.Preview(new Strike { unit = Hero, amount = 10 }, g => g.Find<TpUnit>("hero").hp);
            Assert.IsTrue(predicted <= 20 && predicted >= 18);
            Assert.AreEqual(30, Hero.hp, "되돌린다");
            Assert.AreEqual(3, _session.Data.Get<TpRun>("run").energy);
            Assert.AreEqual(journal, _session.Journal.LastId, "기록이 남지 않는다");
            Assert.AreEqual(undo, _session.UndoCount, "Undo 이력이 늘지 않는다");
            Assert.AreEqual(0, seen.Count, "행동 뒤에 받는 쪽은 받지 않는다");
            Assert.AreEqual(0, changes, "데이터 변경 알림도 보내지 않는다(화면이 다시 그려지지 않음)");

            Game.Do(new Strike { unit = Hero, amount = 10 });
            Assert.AreEqual(predicted, Hero.hp, "난수도 돌아가서 실제로 하면 미리본 값과 같다");
            Assert.AreEqual(1, seen.Count);
        }

        [Test]
        public void 할_수_있는지_이유와_함께_알려준다()
        {
            Assert.IsTrue(Game.CanDo(new Strike { unit = Hero, amount = 1 }, out var none));
            Assert.IsNull(none);
            Game.Do(new Strike { unit = Hero, amount = 1 });
            Game.Do(new Strike { unit = Hero, amount = 1 });
            Game.Do(new Strike { unit = Hero, amount = 1 });
            Assert.IsFalse(Game.CanDo(new Strike { unit = Hero, amount = 1 }, out var why));
            StringAssert.Contains("모자랍니다", why);
            Assert.AreEqual(0, _session.Data.Get<TpRun>("run").energy);
        }

        [Test]
        public void 행동_안에서는_미리보기를_할_수_없다()
        {
            Assert.Throws<InvalidOperationException>(() => Game.Do(new Probe()));
        }

        public sealed class Probe : GameAction
        {
            protected override void Run() => Game.Preview(new Strike { unit = Game.Find<TpUnit>("hero"), amount = 1 }, g => 0);
        }
    }
}
