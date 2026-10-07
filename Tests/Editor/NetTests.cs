using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GBible.Tests
{
    [Serializable]
    public class NtUnit
    {
        public string id;
        public int hp = 10;
    }

    [Serializable]
    public class NtParty
    {
        public List<NtUnit> units = new List<NtUnit>();
    }

    [GameEvent("시험 맞음")]
    public class NtHit
    {
        public NtUnit unit;
        public int damage;
    }

    [Serializable]
    public class NtStrike : GameAction
    {
        public NtUnit unit;
        public int damage = 3;

        protected override void Run()
        {
            unit.hp -= damage;
            Game.Session.Data.Set("lastActor", (double)actorId);
            Game.Session.Events.Publish(new NtHit { unit = unit, damage = damage });
        }
    }

    [Serializable]
    public class NtRecruit : GameAction
    {
        public string newId;

        protected override void Run() => Game.Session.Data.Get<NtParty>("party").units.Add(new NtUnit { id = newId, hp = 5 });
    }

    [Serializable]
    public class NtFail : GameAction
    {
        protected override void Run()
        {
            Game.Session.Data.Set("gold", 999.0);
            Game.Session.Events.Publish(new NtHit { damage = 1 });
            throw new GameFailure("test.netFail", TextArgs.From(("n", 3)), "{n}번 실패");
        }
    }

    [Serializable]
    public class NtRemoveThenFail : GameAction
    {
        protected override void Run()
        {
            Game.Session.Data.Get<NtParty>("party").units.RemoveAt(0);
            throw new GameFailure("test.netFail2", null, "실패");
        }
    }

    [Serializable]
    public class NtAskPick : GameAction
    {
        protected override void Run() => Game.Ask("고르기", new[] { new ChoiceCandidate("x", "엑스"), new ChoiceCandidate("y", "와이") }, new NtPicked());
    }

    [Serializable]
    public class NtPicked : GameAction
    {
        [Chosen] public string picked;

        protected override void Run() => Game.Session.Data.Set("gold", picked == "x" ? 1.0 : 2.0);
    }

    // 멀티플레이 공통층: 한 프로그램 안의 서버 하나 + 클라이언트(메모리 전송, 시간은 손으로 흘린다).
    public class NetTests
    {
        sealed class Rig
        {
            public readonly MemoryNetwork Net = new MemoryNetwork { Manual = true, Latency = 0.02 };
            public readonly GameSession ServerSession = new GameSession();
            public readonly NetServer Server;
            public readonly List<NetClient> Clients = new List<NetClient>();

            public Rig()
            {
                var data = ServerSession.Data;
                data.Define("party", DataZone.Session, new NtParty { units = { new NtUnit { id = "a", hp = 10 }, new NtUnit { id = "b", hp = 8 } } });
                data.Define("gold", DataZone.Session, 5.0);
                data.Define("lastActor", DataZone.Session, 0.0);
                data.Define("hand:1", DataZone.Session, new NtParty { units = { new NtUnit { id = "h1" } } });
                data.Define("hand:2", DataZone.Session, new NtParty { units = { new NtUnit { id = "h2" } } });
                data.Define("best", DataZone.Meta, 100.0);
                data.SetVisibility("hand:1", DataVisibility.Owner, 1);
                data.SetVisibility("hand:2", DataVisibility.Owner, 2);
                Server = new NetServer(ServerSession, Net.Server());
                Server.Start();
            }

            public NetClient Join(string name)
            {
                var client = new NetClient(new GameSession(), Net.Client(), name);
                Clients.Add(client);
                client.Connect();
                Pump();
                return client;
            }

            public void Pump(int steps = 20)
            {
                for (var i = 0; i < steps; i++)
                {
                    Net.Advance(0.01);
                    Server.Update(0.01);
                    foreach (var client in Clients) client.Update(0.01);
                }
            }
        }

        static NtUnit UnitOf(GameSession session, string id) => session.Data.Get<NtParty>("party").units.First(u => u.id == id);

        [Test]
        public void 들어오면_보이는_데이터의_처음_모습을_받는다()
        {
            var rig = new Rig();
            var client = rig.Join("철수");
            Assert.IsTrue(client.IsJoined, "입장");
            Assert.AreEqual(1, client.PlayerId);
            Assert.AreEqual(1, client.Session.LocalPlayer);
            Assert.AreEqual(SessionRole.Client, client.Session.Role);
            var party = client.Session.Data.Get<NtParty>("party");
            Assert.AreEqual(2, party.units.Count);
            Assert.AreEqual(8, party.units[1].hp);
            Assert.AreEqual(5.0, client.Session.Data.GetNumber("gold"));
            Assert.IsTrue(client.Session.Data.IsDefined("hand:1"), "내 손패는 보인다");
            Assert.IsFalse(client.Session.Data.IsDefined("hand:2"), "남의 손패는 오지 않는다");
            Assert.IsFalse(client.Session.Data.IsDefined("best"), "메타 구역(프로필)은 기본으로 보내지 않는다");
        }

        [Test]
        public void 실패한_행동의_되돌림은_객체_번호를_바꾸지_않는다()
        {
            var rig = new Rig();
            var client = rig.Join("철수");
            var hits = new List<NtHit>();
            client.Session.Events.SubscribeAfterAction<NtHit>(hits.Add);
            client.Session.Do(new NtRemoveThenFail());
            rig.Pump();
            Assert.AreEqual(2, client.Session.Data.Get<NtParty>("party").units.Count, "실패라 그대로");
            client.Session.Do(new NtStrike { unit = UnitOf(client.Session, "a"), damage = 2 });
            rig.Pump();
            Assert.AreEqual(1, hits.Count);
            Assert.AreSame(UnitOf(client.Session, "a"), hits[0].unit, "이벤트의 객체가 받는 쪽 데이터의 같은 인스턴스(번호가 이어진다)");
            Assert.AreEqual(8, UnitOf(client.Session, "a").hp);
        }

        [Test]
        public void 한_사람에게_온_선택은_그_사람에게만_가고_그_사람이_답한다()
        {
            var rig = new Rig();
            var first = rig.Join("철수");
            var second = rig.Join("영희");
            second.Session.Do(new NtAskPick());
            rig.Pump();
            Assert.IsNotNull(second.Session.Game.Choice, "물은 사람에게 고르기 창");
            Assert.IsNull(first.Session.Game.Choice, "다른 사람에게는 오지 않는다");
            Assert.IsFalse(first.Session.Data.IsDefined(ChoiceSystem.KeyFor("2")), "남의 후보는 받지 않는다");
            Assert.IsNull(rig.ServerSession.Game.Choice, "서버(내 번호 없음)는 남의 선택을 자기 것으로 보지 않는다");
            Assert.AreEqual(1, rig.ServerSession.Game.Choices.Count);

            var id = second.Session.Game.Choice.id;
            first.Session.Do(new ChoiceAnswer { choice = id, picked = { "x" } });
            rig.Pump();
            Assert.AreEqual(5.0, rig.ServerSession.Data.GetNumber("gold"), "남이 답하면 거절");
            second.Session.Game.Answer(id, "y");
            rig.Pump();
            Assert.AreEqual(2.0, rig.ServerSession.Data.GetNumber("gold"));
            Assert.IsNull(second.Session.Game.Choice);
        }

        [Test]
        public void 클라이언트의_행동은_서버가_실행하고_바뀐_곳이_돌아온다()
        {
            var rig = new Rig();
            var client = rig.Join("철수");
            var hits = new List<NtHit>();
            client.Session.Events.SubscribeAfterAction<NtHit>(hits.Add);
            var target = UnitOf(client.Session, "a");

            var record = client.Session.Do(new NtStrike { unit = target, damage = 4 });
            Assert.AreEqual(ActionStatus.Sent, record.Status);
            Assert.AreEqual(10, target.hp, "보내기만 한다(결과는 서버에서)");
            rig.Pump();

            Assert.AreEqual(6, UnitOf(rig.ServerSession, "a").hp, "서버가 실행");
            Assert.AreEqual(6, UnitOf(client.Session, "a").hp, "바뀐 곳이 돌아온다");
            Assert.AreSame(target, UnitOf(client.Session, "a"), "같은 인스턴스가 고쳐진다");
            Assert.AreEqual(1.0, rig.ServerSession.Data.GetNumber("lastActor"), "actorId = 보낸 사람 번호");
            Assert.AreEqual(1, hits.Count, "이벤트는 연출 쪽(SubscribeAfterAction)에 온다");
            Assert.AreSame(UnitOf(client.Session, "a"), hits[0].unit, "이벤트 안의 객체는 이쪽 데이터의 그 객체");
            Assert.AreEqual(0, client.PendingActions);
        }

        [Test]
        public void 방장의_연출이_미리보기나_실패한_행동을_해도_방금_행동의_이벤트는_간다()
        {
            var rig = new Rig();
            var client = rig.Join("철수");
            var hits = new List<NtHit>();
            client.Session.Events.SubscribeAfterAction<NtHit>(hits.Add);
            // 방장 화면의 연출: 맞을 때마다 버튼을 다시 보려고 미리보기(CanDo)를 하고, 실패하는 행동도 한 번 해 본다.
            rig.ServerSession.Events.SubscribeAfterAction<NtHit>(hit =>
            {
                rig.ServerSession.Game.CanDo(new NtStrike { unit = UnitOf(rig.ServerSession, "b"), damage = 1 }, out _);
                Assert.Throws<GameFailure>(() => rig.ServerSession.Do(new NtFail()));
            });
            rig.ServerSession.Do(new NtStrike { unit = UnitOf(rig.ServerSession, "a"), damage = 2 });
            rig.Pump();
            Assert.AreEqual(8, UnitOf(client.Session, "a").hp, "바뀐 곳은 온다");
            Assert.AreEqual(1, hits.Count, "그 행동의 이벤트도 온다(미리보기·실패한 행동이 버리지 않는다)");
            Assert.AreEqual(2, hits[0].damage);
            Assert.AreEqual(8, UnitOf(client.Session, "b").hp, "미리보기는 아무것도 보내지 않는다");
        }

        [Test]
        public void 서버에서_새로_생긴_객체와_키도_온다()
        {
            var rig = new Rig();
            var client = rig.Join("철수");
            client.Session.Do(new NtRecruit { newId = "c" });
            rig.ServerSession.Data.Define("extra", DataZone.Session, 7.0);
            rig.ServerSession.Data.Set("gold", 9.0);
            rig.Pump();
            Assert.AreEqual(3, client.Session.Data.Get<NtParty>("party").units.Count);
            Assert.AreEqual(5, UnitOf(client.Session, "c").hp);
            Assert.AreEqual(7.0, client.Session.Data.GetNumber("extra"), "실행 중에 생긴 키");
            Assert.AreEqual(9.0, client.Session.Data.GetNumber("gold"), "행동 밖에서 바뀐 단순 값");

            // 새 객체에 이어서 행동해도 같은 객체를 가리킨다.
            client.Session.Do(new NtStrike { unit = UnitOf(client.Session, "c"), damage = 2 });
            rig.Pump();
            Assert.AreEqual(3, UnitOf(client.Session, "c").hp);
        }

        [Test]
        public void 늦게_들어온_사람은_지금_모습을_받는다()
        {
            var rig = new Rig();
            var first = rig.Join("철수");
            first.Session.Do(new NtStrike { unit = UnitOf(first.Session, "b"), damage = 5 });
            rig.Pump();
            var second = rig.Join("영희");
            Assert.AreEqual(2, second.PlayerId);
            Assert.AreEqual(3, UnitOf(second.Session, "b").hp);
            Assert.IsTrue(second.Session.Data.IsDefined("hand:2"));
            Assert.IsFalse(second.Session.Data.IsDefined("hand:1"));
            Assert.AreEqual(2, first.Players.Count, "명단이 바뀐다");

            second.Session.Do(new NtStrike { unit = UnitOf(second.Session, "a"), damage = 1 });
            rig.Pump();
            Assert.AreEqual(9, UnitOf(first.Session, "a").hp, "다른 사람의 행동도 받는다");
            Assert.AreEqual(2.0, rig.ServerSession.Data.GetNumber("lastActor"));
        }

        [Test]
        public void 거절된_행동은_알림으로_오고_아무것도_바뀌지_않는다()
        {
            var rig = new Rig();
            var client = rig.Join("철수");
            var failures = new List<NetActionFailure>();
            var hits = new List<NtHit>();
            client.ActionFailed += failures.Add;
            client.Session.Events.SubscribeAfterAction<NtHit>(hits.Add);

            client.Session.Do(new NtFail());
            rig.Pump();
            Assert.AreEqual(1, failures.Count);
            Assert.AreEqual("test.netFail", failures[0].Key);
            StringAssert.Contains("3번 실패", failures[0].Message);
            Assert.AreEqual(5.0, client.Session.Data.GetNumber("gold"), "실패한 행동의 변경은 남지 않는다");
            Assert.AreEqual(0, hits.Count, "실패한 행동의 이벤트는 보내지 않는다");

            client.Session.Do(new StatusExpireRun());
            rig.Pump();
            Assert.AreEqual(2, failures.Count);
            StringAssert.Contains("시스템 행동", failures[1].Message);

            rig.Server.Validate = (player, action) => action is NtStrike ? "지금은 공격할 수 없습니다." : null;
            client.Session.Do(new NtStrike { unit = UnitOf(client.Session, "a") });
            rig.Pump();
            Assert.AreEqual(3, failures.Count);
            Assert.AreEqual(10, UnitOf(rig.ServerSession, "a").hp, "검사에서 막히면 실행하지 않는다");
        }

        [Test]
        public void 행동_검사가_오류를_내도_거절로_결과가_돌아온다()
        {
            var rig = new Rig();
            var client = rig.Join("철수");
            var failures = new List<NetActionFailure>();
            client.ActionFailed += failures.Add;
            // 게임의 검사 코드가 예외를 낸다 — 결과가 오지 않으면 클라이언트는 그 행동을 끝없이 기다린다(대화 진행이 멈추는 등).
            rig.Server.Validate = (player, action) => throw new InvalidOperationException("검사 오류(시험)");
            client.Session.Do(new NtStrike { unit = UnitOf(client.Session, "a") });
            rig.Pump();
            Assert.AreEqual(0, client.PendingActions, "결과가 온다");
            Assert.AreEqual(1, failures.Count, "거절로 온다");
            StringAssert.Contains("검사 오류", failures[0].Message);
            Assert.AreEqual(10, UnitOf(rig.ServerSession, "a").hp, "실행하지 않는다");
        }

        [Test]
        public void 초당_한도를_넘어_버린_행동도_거절로_결과가_돌아온다()
        {
            var rig = new Rig();
            var now = 0.0;
            rig.Server.Clock = () => now;   // 서버의 1초 창을 시험이 정한다
            var client = rig.Join("철수");
            var failures = new List<NetActionFailure>();
            client.ActionFailed += failures.Add;
            now = 5;   // 입장 메시지의 1초 창을 지나 새 창에서 센다
            rig.Server.Options.MaxMessagesPerSecond = 3;
            // 한 번에 다섯 — 셋만 실행하고 둘은 한도를 넘는다. 넘은 것도 결과가 와야 보낸 쪽이 그 행동을 끝없이 기다리지 않는다.
            for (var i = 0; i < 5; i++) client.Session.Do(new NtStrike { unit = UnitOf(client.Session, "a") });
            rig.Pump();
            Assert.AreEqual(0, client.PendingActions, "모두 결과가 온다");
            Assert.AreEqual(1, UnitOf(rig.ServerSession, "a").hp, "셋만 실행한다");
            Assert.AreEqual(2, failures.Count, "한도를 넘은 둘은 거절로");
            StringAssert.Contains("너무 많아", failures[0].Message);
            Assert.AreEqual(1, UnitOf(client.Session, "a").hp, "네 배 전에는 끊지 않고 바뀐 곳도 계속 온다");
        }

        [Test]
        public void 클라이언트는_되돌리지_않고_규칙이_스스로_돌지_않는다()
        {
            var rig = new Rig();
            var client = rig.Join("철수");
            client.Session.Do(new NtStrike { unit = UnitOf(client.Session, "a") });
            rig.Pump();
            Assert.IsFalse(client.Session.Undo(), "멀티 클라이언트는 Undo하지 않는다");
            Assert.AreEqual(7, UnitOf(client.Session, "a").hp);
            Assert.AreEqual(1, rig.ServerSession.UndoCount, "서버는 이력이 있다");
            rig.ServerSession.Undo();
            rig.Pump();
            Assert.AreEqual(10, UnitOf(client.Session, "a").hp, "서버의 되돌리기는 바뀐 곳으로 온다");
        }

        [Test]
        public void 지연이_있어도_차례대로_도착한다()
        {
            var rig = new Rig();
            rig.Net.Latency = 0.2;
            rig.Net.Jitter = 0.05;
            var client = new NetClient(new GameSession(), rig.Net.Client(), "철수");
            rig.Clients.Add(client);
            client.Connect();
            rig.Pump(5);
            Assert.IsFalse(client.IsJoined, "지연 안에서는 아직 들어오지 않았다");
            rig.Pump(80);
            Assert.IsTrue(client.IsJoined);
            for (var i = 0; i < 5; i++) client.Session.Do(new NtStrike { unit = UnitOf(client.Session, "a"), damage = 1 });
            rig.Pump(80);
            Assert.AreEqual(5, UnitOf(client.Session, "a").hp, "다섯 번 모두 차례대로");
        }

        [Test]
        public void 직접_접속_전송으로_실제_TCP에서_주고받는다()
        {
            var serverSession = new GameSession();
            serverSession.Data.Define("party", DataZone.Session, new NtParty { units = { new NtUnit { id = "a", hp = 10 } } });
            serverSession.Data.Define("lastActor", DataZone.Session, 0.0);
            var transport = new WebSocketServerTransport(0);
            var server = new NetServer(serverSession, transport);
            server.Start();
            var client = new NetClient(new GameSession(), new WebSocketClientTransport("127.0.0.1:" + transport.Port), "철수");
            client.Connect();
            try
            {
                bool Wait(Func<bool> done)
                {
                    var until = DateTime.UtcNow.AddSeconds(10);
                    while (DateTime.UtcNow < until)
                    {
                        server.Update(0);
                        client.Update(0);
                        if (done()) return true;
                        System.Threading.Thread.Sleep(5);
                    }
                    return false;
                }
                Assert.IsTrue(Wait(() => client.IsJoined), "실제 TCP로 입장");
                Assert.AreEqual(10, UnitOf(client.Session, "a").hp);
                client.Session.Do(new NtStrike { unit = UnitOf(client.Session, "a"), damage = 6 });
                Assert.IsTrue(Wait(() => UnitOf(client.Session, "a").hp == 4), "행동이 돌아온다");
                Assert.IsTrue(Wait(() => client.RoundTrip > 0), "핑");
            }
            finally
            {
                client.Dispose();
                server.Dispose();
            }
        }

        [Test]
        public void 형식_번호표로_상대의_번호를_이쪽_형식으로_읽는다()
        {
            var w = new MirrorWriter();
            Mirror.Write(w, new NtUnit { id = "x", hp = 3 });
            var table = Mirror.TypeTable();

            var same = new MirrorReceiver();
            Assert.AreEqual(0, same.UseTypes(table).Count);
            Assert.AreEqual(3, ((NtUnit)same.Load(w.ToArray())).hp);

            var index = Array.FindIndex(table, name => name.StartsWith(typeof(NtUnit).FullName + "|", StringComparison.Ordinal));
            Assert.GreaterOrEqual(index, 0);
            var remote = (string[])table.Clone();
            remote[index] = "Nope.Missing|Nope";
            var other = new MirrorReceiver();
            Assert.AreEqual(1, other.UseTypes(remote).Count, "없는 형식을 알려 준다");
            var error = Assert.Throws<InvalidOperationException>(() => other.Load(w.ToArray()));
            StringAssert.Contains("Nope.Missing", error.Message);
        }
    }
}
