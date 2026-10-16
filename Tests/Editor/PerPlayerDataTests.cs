using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GBible.Tests
{
    // 플레이어마다 따로인 플레이어 데이터: 'army'를 읽고 쓰면 지금 행동을 한 플레이어의 'army:<번호>'. 패시브 효과의 가진 목록은 모든 플레이어 몫을 훑되
    // 받을 대상은 같은 플레이어 몫 안에서만 짝짓는다.
    public class PerPlayerDataTests
    {
        [Serializable] public class PpHero { public string id; public int attack = 2; }
        [Serializable] public class PpArmy { public PpHero hero = new PpHero(); public List<string> relics = new List<string>(); public int gold; }

        sealed class Edit : GameAction
        {
            public Action<GameApi> run;
            protected override void Run() => run(Game);
        }

        GameSession _session;

        [SetUp]
        public void SetUp()
        {
            _session = new GameSession(new SessionOptions { Seed = 1 });
            _session.Data.DefinePerPlayer("army", DataZone.Session, typeof(PpArmy), new PpArmy(), "군대", ownerOnly: true);
            _session.Data.AddPlayer(1);
            _session.Data.AddPlayer(2);
            // 각자 영웅 id(플레이어 데이터 전체에서 하나)
            _session.Data.Get<PpArmy>("army:1").hero.id = "hero1";
            _session.Data.Get<PpArmy>("army:2").hero.id = "hero2";
        }

        [Test]
        public void 행동한_플레이어의_몫을_읽고_쓴다()
        {
            _session.Do(new Edit { actorId = 2, run = g => g.Get<PpArmy>("army").gold += 5 });
            Assert.AreEqual(0, _session.Data.Get<PpArmy>("army:1").gold);
            Assert.AreEqual(5, _session.Data.Get<PpArmy>("army:2").gold, "2번이 한 행동은 2번 몫");
            _session.LocalPlayer = 1;
            Assert.AreEqual(0, _session.Data.Get<PpArmy>("army").gold, "행동 밖은 이 화면의 플레이어(1번) 몫");
            Assert.IsTrue(_session.Data.IsDefined("army"));
            Assert.AreEqual(DataVisibility.Owner, _session.Data.VisibilityOf("army:2", out var owner));
            Assert.AreEqual(2, owner, "그 사람에게만 보임");
            CollectionAssert.AreEqual(new[] { "army:1", "army:2" }, _session.Data.KeysOfAllPlayers("army").ToArray());
        }

        [Test]
        public void 혼자_하기_세션이_방이_되면_0번_몫은_지우고_방장은_새_몫을_받는다()
        {
            var session = new GameSession(new SessionOptions { Seed = 1 });
            session.Data.DefinePerPlayer("army", DataZone.Session, typeof(PpArmy), new PpArmy(), "군대");
            session.Data.AddPlayer(session.LocalPlayer);
            session.Do(new Edit { run = g => g.Get<PpArmy>("army").gold = 9 });
            Assert.AreEqual(9, session.Data.Get<PpArmy>("army:0").gold, "혼자 하기 = 0번 몫");

            var net = new MemoryNetwork { Manual = true };
            using (var server = new NetServer(session, net.Server()))
            {
                server.Start();
                server.AddLocalPlayer("방장");
                CollectionAssert.AreEqual(new[] { "army:1" }, session.Data.KeysOfAllPlayers("army").ToArray(), "0번 몫은 남지 않는다");
                Assert.AreEqual(0, session.Game.Get<PpArmy>("army").gold, "방장(1번)은 처음 값");
                Assert.DoesNotThrow(() => session.Undo(), "지운 몫을 바꾼 지난 행동도 되돌릴 수 있다(지운 키는 건너뜀)");
            }
        }

        [Test]
        public void 패시브_효과는_모든_플레이어_몫을_훑고_같은_몫의_대상에게만_보탠다()
        {
            var banner = new PassiveDefinition { Id = "banner", OwnerKey = "army", OwnerPath = "relics", ModifierTarget = "hero" };
            banner.Modifiers.Add(new PassiveModifier { attribute = "attack", value = 3 });
            _session.Passives.Define(banner);
            _session.Do(new Edit { actorId = 2, run = g => g.Get<PpArmy>("army").relics.Add("banner") });
            Assert.AreEqual(2 + 3, _session.Game.Attribute("hero2", "attack"), "2번 영웅은 2번 깃발을 받는다");
            Assert.AreEqual(2, _session.Game.Attribute("hero1", "attack"), "1번 영웅은 남의 깃발을 받지 않는다");
        }
    }
}
