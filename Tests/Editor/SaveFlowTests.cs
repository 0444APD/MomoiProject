using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GBible.Tests
{
    // 저장·불러오기의 빈틈: 아직 정의되지 않은 키(라이브러리가 처음 쓸 때 정의)도 다시 저장되고,
    // 불러온 저장에 없는 키는 처음 값으로 돌아간다(판 불러오기·테스트 상황·스냅샷 되감기).
    public class SaveFlowTests
    {
        [Serializable] public class SfBag { public List<string> items = new List<string>(); }

        [Test]
        public void 불러왔지만_아직_정의되지_않은_키도_다시_저장된다()
        {
            var session = new GameSession();
            session.Data.Import(new Dictionary<string, object> { ["$id.status"] = 7.0 }, DataZone.Meta);
            var exported = session.Data.Export(DataZone.Meta);
            Assert.AreEqual(7.0, exported["$id.status"], "이번 실행에서 쓰지 않았어도 저장에 남는다");

            session.Data.Define("$id.status", DataZone.Meta, 0.0);
            Assert.AreEqual(7.0, session.Data.GetNumber("$id.status"), "정의하면 불러온 값으로");
            Assert.AreEqual(7.0, session.Data.Export(DataZone.Meta)["$id.status"]);
        }

        [Test]
        public void 구역을_비우면_미리_불러_둔_옛_값도_버린다()
        {
            var session = new GameSession();
            session.Data.Import(new Dictionary<string, object> { ["$world.door"] = true }, DataZone.Session);
            session.Data.ResetZone(DataZone.Session);
            Assert.IsFalse(session.Data.Export(DataZone.Session).ContainsKey("$world.door"), "새 판에 옛 판 값이 들어오지 않는다");
            session.Data.Define("$world.door", DataZone.Session, false);
            Assert.IsFalse((bool)session.Data.Get("$world.door"));
        }

        [Test]
        public void 다시_불러오면_앞_저장에서_미리_불러_둔_값은_새_저장이_대신한다()
        {
            var session = new GameSession();
            session.Data.Import(new Dictionary<string, object> { ["$id.unit"] = 3.0 }, DataZone.Meta);
            session.Data.Import(new Dictionary<string, object>(), DataZone.Meta);
            Assert.IsFalse(session.Data.Export(DataZone.Meta).ContainsKey("$id.unit"));
        }

        [Test]
        public void 불러온_저장에_없는_키는_처음_값으로_돌아간다()
        {
            var session = new GameSession();
            session.Data.Define("gold", DataZone.Session, 0.0);
            session.Data.Define("poison", DataZone.Session, 0.0);
            session.Data.Define("bag", DataZone.Session, new SfBag());
            session.Data.Set("gold", 10.0);
            var saved = session.Data.Export(DataZone.Session);
            saved.Remove("poison");
            saved.Remove("bag");

            // 저장한 뒤에 생긴 것들
            session.Data.Set("poison", 3.0);
            session.Data.Get<SfBag>("bag").items.Add("key");
            session.Data.Set("gold", 99.0);

            session.Data.Import(saved, DataZone.Session);
            Assert.AreEqual(10.0, session.Data.GetNumber("gold"));
            Assert.AreEqual(0.0, session.Data.GetNumber("poison"), "저장에 없는 키는 처음 값");
            Assert.AreEqual(0, session.Data.Get<SfBag>("bag").items.Count, "데이터 값도 처음 모습");
        }

        [Test]
        public void 되감기_스냅샷에_없던_키는_처음_값으로()
        {
            var session = new GameSession();
            session.Data.Define("gold", DataZone.Session, 0.0);
            var snapshot = session.Data.Export(DataZone.Session, report: false, keepReferences: true);
            session.Data.Define("burn", DataZone.Session, 0.0);
            session.Data.Set("burn", 2.0);
            session.Data.Restore(snapshot, DataZone.Session, "되감기");
            Assert.AreEqual(0.0, session.Data.GetNumber("burn"));
        }

        [Serializable] public class SfUnit { public string id; public int hp = 30; }
        [Serializable] public class SfParty { public List<SfUnit> units = new List<SfUnit>(); }

        sealed class SfEdit : GameAction
        {
            public Action<GameApi> run;
            protected override void Run() => run(Game);
        }

        // 행동 뒤에 행동 밖에서 리스트 구조를 바꾸면, 그 행동의 Undo는 번호가 밀려 엉뚱한 요소를 고친다 → 거부하고 알린다.
        [Test]
        public void 행동_밖에서_리스트_구조가_바뀌면_그_앞_행동의_Undo를_거부한다()
        {
            var session = new GameSession();
            var party = new SfParty { units = { new SfUnit { id = "hero" }, new SfUnit { id = "foe", hp = 20 } } };
            session.Data.Define("party", DataZone.Session, party);
            var hero = party.units[0];
            var foe = party.units[1];
            session.Game.Do(new SfEdit { run = g => hero.hp -= 5 });
            party.units.RemoveAt(0);
            session.Data.Sync();

            Assert.IsFalse(session.Undo(), "거부한다");
            Assert.AreEqual(20, foe.hp, "다른 요소가 바뀌지 않는다");
            Assert.AreEqual(25, hero.hp);
            Assert.AreEqual(1, session.Data.HistoryCount, "이력에 그대로 남는다");

            // 구조가 아닌 값만 바뀌었으면 그대로 되돌린다(덮어쓴 곳은 알린다).
            var other = new GameSession();
            var party2 = new SfParty { units = { new SfUnit { id = "hero" }, new SfUnit { id = "foe" } } };
            other.Data.Define("party", DataZone.Session, party2);
            other.Game.Do(new SfEdit { run = g => party2.units[0].hp -= 5 });
            party2.units[1].hp = 1;
            other.Data.Sync();
            Assert.IsTrue(other.Undo());
            Assert.AreEqual(30, party2.units[0].hp);
            Assert.AreEqual(1, party2.units[1].hp);
        }
    }
}
