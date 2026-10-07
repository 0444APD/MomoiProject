using System;
using NUnit.Framework;

namespace GBible.Tests
{
    // 월드 상태.
    public class WorldStateTests
    {
        sealed class Edit : GameAction
        {
            public Action<GameApi> run;
            protected override void Run() => run(Game);
        }

        GameSession _session;
        GameApi Game => _session.Game;

        [SetUp]
        public void SetUp() => _session = new GameSession(new SessionOptions { Seed = 1 });

        void Do(Action<GameApi> run) => Game.Do(new Edit { run = run });

        [Test]
        public void 거짓_글자는_대소문자를_가리지_않는다()
        {
            Do(g => g.SetWorld("door", false.ToString()));
            Assert.IsFalse(Game.WorldFlag("door"), "bool.ToString()의 \"False\"도 거짓");
            Do(g => g.SetWorld("door", "FALSE"));
            Assert.IsFalse(Game.WorldFlag("door"));
            Do(g => g.SetWorld("door", "opened"));
            Assert.IsTrue(Game.WorldFlag("door"));
        }

        [Test]
        public void 이름으로_기억하고_Undo된다()
        {
            Assert.IsNull(Game.World("forest.chest3"));
            Do(g => g.SetWorld("forest.chest3", "opened"));
            Do(g => g.SetWorld("forest.boss", true));
            Assert.AreEqual("opened", Game.World("forest.chest3"));
            Assert.IsTrue(Game.WorldFlag("forest.boss"));
            Assert.IsTrue(_session.Undo());
            Assert.IsFalse(Game.WorldFlag("forest.boss"));
            Do(g => g.ClearWorld("forest.chest3"));
            Assert.IsNull(Game.World("forest.chest3"));
        }

        [Test]
        public void 판_값이_프로필_값보다_앞서고_새_판에도_프로필은_남는다()
        {
            Do(g => g.SetWorld("village.gate", "open", permanent: true));
            Do(g => g.SetWorld("village.gate", "broken"));
            Assert.AreEqual("broken", Game.World("village.gate"));
            _session.NewRun();
            Assert.AreEqual("open", Game.World("village.gate"));
        }

        [Test]
        public void 프로필_값이_있어도_판에서_끌_수_있다()
        {
            Do(g => g.SetWorld("cave.torch", true, permanent: true));
            Do(g => g.SetWorld("cave.torch", false));
            Assert.IsFalse(Game.WorldFlag("cave.torch"), "판에서 끈 것이 앞선다");
            _session.NewRun();
            Assert.IsTrue(Game.WorldFlag("cave.torch"), "새 판이면 프로필 값");
        }

        [Test]
        public void 판_저장으로_이어진다()
        {
            Do(g => g.SetWorld("cave.lever", true));
            var json = _session.ExportRun();
            var loaded = new GameSession(new SessionOptions { Seed = 1 });
            loaded.ImportRun(json);
            Assert.IsTrue(loaded.Game.WorldFlag("cave.lever"));
        }
    }
}
