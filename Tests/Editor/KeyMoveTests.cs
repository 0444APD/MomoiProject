using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GBible.Tests
{
    // 다른 플레이어 데이터 키로 객체 옮기기(판 ↔ 프로필 — 탈출해서 창고로, 창고에서 꺼내 판으로).
    // 한 행동 안에서 한쪽 목록에서 빼고 다른 쪽 목록에 넣으면, 어느 순간에도 한 키만 그 객체를 가리킨다 — Undo · 저장이 맞는지 본다.
    public class KeyMoveTests
    {
        [Serializable] public class KmItem { public string id; public string kind; public int bonus; }
        [Serializable] public class KmRun { public List<KmItem> bag = new List<KmItem>(); }
        [Serializable] public class KmProfile { public List<KmItem> stash = new List<KmItem>(); }

        sealed class Edit : GameAction
        {
            public Action<GameApi> run;
            protected override void Run() => run(Game);
        }

        static GameSession NewSession()
        {
            var session = new GameSession(new SessionOptions { Seed = 1 });
            session.Data.Define("run", DataZone.Session, new KmRun { bag = { new KmItem { id = "it1", kind = "sword", bonus = 5 } } });
            session.Data.Define("profile", DataZone.Meta, new KmProfile());
            return session;
        }

        static void MoveToStash(GameApi game)
        {
            var run = game.Get<KmRun>("run");
            var profile = game.Get<KmProfile>("profile");
            var item = run.bag.Single(i => i.id == "it1");
            run.bag.Remove(item);
            profile.stash.Add(item);   // 같은 객체를 그대로 옮긴다(id 그대로)
        }

        [Test]
        public void 같은_객체를_다른_키로_옮기고_Undo하면_제자리로_돌아온다()
        {
            var session = NewSession();
            session.Do(new Edit { run = MoveToStash });
            Assert.AreEqual(0, session.Game.Get<KmRun>("run").bag.Count);
            Assert.AreEqual(5, session.Game.Get<KmProfile>("profile").stash.Single().bonus);

            Assert.IsTrue(session.Undo());
            Assert.AreEqual("it1", session.Game.Get<KmRun>("run").bag.Single().id);
            Assert.AreEqual(0, session.Game.Get<KmProfile>("profile").stash.Count);
        }

        [Test]
        public void 옮긴_뒤_고친_값도_새_키에서_Undo되고_저장에_한_번만_남는다()
        {
            var session = NewSession();
            session.Do(new Edit { run = MoveToStash });
            session.Do(new Edit { run = g => g.Get<KmProfile>("profile").stash[0].bonus = 9 });
            Assert.AreEqual(9, session.Game.Get<KmProfile>("profile").stash[0].bonus);
            Assert.IsTrue(session.Undo());
            Assert.AreEqual(5, session.Game.Get<KmProfile>("profile").stash[0].bonus);

            // 저장: 판에는 없고 프로필에만 있다.
            var meta = session.ExportMeta();
            var run = session.ExportRun();
            StringAssert.Contains("it1", meta);
            StringAssert.DoesNotContain("it1", run);
        }
    }
}
