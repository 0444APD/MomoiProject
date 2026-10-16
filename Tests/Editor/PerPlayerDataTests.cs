using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GBible.Tests
{
    [Serializable, GameEvent("시험: 처치")]
    public class PpKilled { }

    [Serializable]
    public class PpEarn : GameAction
    {
        protected override void Run() => Game.Get<PerPlayerDataTests.PpArmy>("army").gold += 1;
    }

    // 플레이어마다 따로인 플레이어 데이터: 'army'를 읽고 쓰면 지금 행동을 한 플레이어의 'army:<번호>'. 패시브 효과의 가진 목록은 모든 플레이어 몫을 훑되
    // 받을 대상은 같은 플레이어 몫 안에서만 짝짓고, 결과는 그 가진 쪽 플레이어의 몫으로 돈다.
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

        static GameSession NewSession()
        {
            var session = new GameSession(new SessionOptions { Seed = 1 });
            session.Data.DefinePerPlayer("army", DataZone.Session, typeof(PpArmy), new PpArmy(), "군대", ownerOnly: true);
            return session;
        }

        [SetUp]
        public void SetUp()
        {
            _session = NewSession();
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
            using (_session.ActingAs(2)) Assert.AreEqual(5, _session.Data.Get<PpArmy>("army").gold, "ActingAs 안은 그 플레이어");
            Assert.IsTrue(_session.Data.IsDefined("army"));
            Assert.AreEqual(DataVisibility.Owner, _session.Data.VisibilityOf("army:2", out var owner));
            Assert.AreEqual(2, owner, "그 사람에게만 보임");
            CollectionAssert.AreEqual(new[] { "army:1", "army:2" }, _session.Data.KeysOfAllPlayers("army").ToArray());
            _session.LocalPlayer = 2;
            _session.Data.ResetKey("army");
            Assert.AreEqual(0, _session.Data.Get<PpArmy>("army:2").gold, "ResetKey도 이름을 푼다");
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

        [Test]
        public void 남의_패시브_효과가_발동해도_결과는_그_가진_쪽_플레이어의_몫으로()
        {
            var bounty = new PassiveDefinition { Id = "bounty", OwnerKey = "army", OwnerPath = "relics", When = new EventTrigger { eventType = typeof(PpKilled).FullName } };
            bounty.Outcomes.Add(new PlayerDataReward { key = "army", path = "gold", amount = 1 });
            _session.Passives.Define(bounty);
            _session.Data.Get<PpArmy>("army:1").relics.Add("bounty");
            _session.Data.Get<PpArmy>("army:2").relics.Add("bounty");
            _session.Do(new Edit { actorId = 1, run = g => g.Session.Events.Publish(new PpKilled()) });
            Assert.AreEqual(1, _session.Data.Get<PpArmy>("army:1").gold);
            Assert.AreEqual(1, _session.Data.Get<PpArmy>("army:2").gold, "1번이 처치해도 2번 유물의 금화는 2번에게");
            _session.LocalPlayer = 1;
            Assert.AreEqual(1, _session.Passives.Owned("bounty"), "가진 수는 지금 플레이어 몫만");
        }

        [Test]
        public void 방장은_혼자_하기의_몫을_이어받고_방을_닫으면_되돌린다()
        {
            var session = NewSession();
            session.Data.AddPlayer(session.LocalPlayer);
            session.Do(new Edit { run = g => g.Get<PpArmy>("army").gold = 9 });
            Assert.AreEqual(9, session.Data.Get<PpArmy>("army:0").gold, "혼자 하기 = 0번 몫");
            Assert.IsNull(session.Game.Find("hostHero"), "찾기 목록을 한 번 만들어 둔다");

            var net = new MemoryNetwork { Manual = true };
            var server = new NetServer(session, net.Server());
            server.Start();
            server.AddLocalPlayer("방장");
            CollectionAssert.AreEqual(new[] { "army:1" }, session.Data.KeysOfAllPlayers("army").ToArray(), "0번 몫은 방장 몫으로 옮겨졌다");
            Assert.AreEqual(9, session.Game.Get<PpArmy>("army").gold, "방장은 혼자 하기의 값을 이어받는다");
            session.Data.Get<PpArmy>("army:1").hero.id = "hostHero";
            Assert.IsNotNull(session.Game.Find("hostHero"), "키 하나가 지워지고 하나가 생겨도(수는 같다) 찾기 목록을 다시 만든다");
            Assert.DoesNotThrow(() => session.Undo(), "옮긴 몫을 바꾼 지난 행동도 되돌릴 수 있다(지운 키는 건너뜀)");

            server.Dispose();
            Assert.AreEqual(0, session.LocalPlayer);
            CollectionAssert.AreEqual(new[] { "army:0" }, session.Data.KeysOfAllPlayers("army").ToArray(), "방을 닫으면 혼자 하기로");
            Assert.AreEqual("hostHero", session.Game.Get<PpArmy>("army").hero.id);
        }

        [Test]
        public void 혼자_하기_녹화는_몫을_건드리지_않는다()
        {
            var session = NewSession();
            session.Data.AddPlayer(session.LocalPlayer);
            var server = NetServer.StartLocalRecording(session, out _);
            Assert.IsTrue(session.Data.IsDefined("army:0"));
            session.Do(new Edit { run = g => g.Get<PpArmy>("army").gold = 4 });
            server.Dispose();
            Assert.AreEqual(4, session.Game.Get<PpArmy>("army").gold);
        }

        [Test]
        public void 서버_검사는_보낸_사람의_몫을_본다()
        {
            var session = NewSession();
            var net = new MemoryNetwork { Manual = true, Latency = 0.01 };
            var server = new NetServer(session, net.Server());
            server.Start();
            server.AddLocalPlayer("방장");
            string seen = null;
            server.Validate = (player, action) =>
            {
                seen = session.Data.Resolve("army");
                return null;
            };
            var clientSession = NewSession();
            var client = new NetClient(clientSession, net.Client(), "철수");
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
            Assert.IsTrue(client.Session.Game.CanDo(new PpEarn(), out _), "클라이언트 미리보기도 내 몫(army:2)으로");
            client.Session.Do(new PpEarn());
            Pump();
            Assert.AreEqual("army:2", seen, "Validate 안의 'army'는 보낸 사람(2번)의 몫");
            Assert.AreEqual(1, session.Data.Get<PpArmy>("army:2").gold);
            Assert.AreEqual(0, session.Data.Get<PpArmy>("army:1").gold);

            NetActionFailure refused = null;
            client.ActionFailed += f => refused = f;
            server.Validate = (player, action) => throw session.Game.Fail("fail.notYourTurn", "내 차례가 아닙니다.");
            client.Session.Do(new PpEarn());
            Pump();
            Assert.AreEqual("fail.notYourTurn", refused?.Key, "Validate의 Game.Fail은 키도 간다(받는 쪽 언어로)");
            client.Dispose();
            server.Dispose();
        }

        [Test]
        public void 다른_기기의_같은_이름은_다른_자리이고_개발_명령은_누른_사람으로_돈다()
        {
            var session = NewSession();
            var net = new MemoryNetwork { Manual = true, Latency = 0.01 };
            var server = new NetServer(session, net.Server(), new NetServerOptions { AllowDeveloperCommands = true });
            server.Start();
            session.Debug.Register("시험", "금화 +1", () => session.Do(new Edit { run = g => g.Get<PpArmy>("army").gold += 1 }));
            var a = new NetClient(NewSession(), net.Client(), "플레이어") { Device = "pcA" };
            var b = new NetClient(NewSession(), net.Client(), "플레이어") { Device = "pcB" };
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
            Assert.AreEqual(2, server.Players.Count, "기본 이름이 같아도 기기가 다르면 두 사람");
            Assert.AreEqual(a.PlayerId, server.Owner.Id, "방장이 없으면 먼저 들어온 사람이 방 주인");
            b.RunDeveloperCommand("시험", "금화 +1");
            Pump();
            Assert.AreEqual(1, session.Data.Get<PpArmy>(PlayerDataStore.KeyFor("army", b.PlayerId)).gold, "누른 사람의 몫");
            Assert.AreEqual(0, session.Data.Get<PpArmy>(PlayerDataStore.KeyFor("army", a.PlayerId)).gold);
            a.Dispose();
            b.Dispose();
            server.Dispose();
        }

        [Test]
        public void 계정_값은_형식이_맞을_때만_들이고_프로필_구역은_받지_않는다()
        {
            _session.Data.DefinePerPlayer("gems", DataZone.Session, typeof(int), 0, "보석", account: true);
            Assert.IsFalse(_session.Data.ImportAccountValue("gems", 1, "true"), "숫자 칸에 참/거짓");
            Assert.IsTrue(_session.Data.ImportAccountValue("gems", 1, "5"));
            Assert.AreEqual(5, _session.Data.Get<int>("gems:1"));
            Assert.Throws<ArgumentException>(() => _session.Data.DefinePerPlayer("medals", DataZone.Meta, typeof(int), 0));
            _session.Data.DefinePerPlayer("deck", DataZone.Session, typeof(PpArmy), new PpArmy(), "덱", serverOnly: true);
            _session.Data.AddPlayer(3);
            Assert.AreEqual(DataVisibility.Server, _session.Data.VisibilityOf("deck:3", out _), "서버만 — 그 사람에게도 보내지 않는다");
        }
    }
}
