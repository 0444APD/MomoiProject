using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GBible.Tests
{
    // 화면 칸의 binding-path 읽기, 결과가 아닌 [BindTo] 클래스(화면 코드).
    public class ScreenBindingTests
    {
        [Serializable] public class SbHero { public int hp = 30; public int maxHp = 50; }
        [Serializable] public class SbRun { public int gold = 99; public SbHero hero = new SbHero(); public List<string> relics = new List<string> { "anchor", "blood" }; public bool boss; }

        // 결과가 아닌 클래스도 [BindTo]로 모인다(화면 코드). 결과 칸에는 들어가지 않는다.
        [BindTo("sb.screen")]
        public sealed class SbScreenCode
        {
        }

        GameSession _session;

        [SetUp]
        public void SetUp()
        {
            _session = new GameSession(new SessionOptions { Seed = 3 });
            _session.Data.Define("run", DataZone.Session, new SbRun(), "판");
        }

        [Test]
        public void 연결_글을_키_경로_텍스트_키로_읽는다()
        {
            var a = ScreenBinding.Parse("run:hero.hp");
            Assert.AreEqual("run", a.Key);
            Assert.AreEqual("hero.hp", a.Path);
            Assert.AreEqual("", a.TextKey);

            var b = ScreenBinding.Parse(" run:gold @ hud.gold ");
            Assert.AreEqual("gold", b.Path);
            Assert.AreEqual("hud.gold", b.TextKey);

            var c = ScreenBinding.Parse("@ui.endTurn");
            Assert.IsFalse(c.HasData);
            Assert.AreEqual("ui.endTurn", c.TextKey);

            Assert.AreEqual("", ScreenBinding.Parse("run").Path, "키만 쓰면 값 전체");
            Assert.IsNull(ScreenBinding.Parse(""));
            Assert.IsNull(ScreenBinding.Parse(":gold"), "키가 없으면 틀린 글");
            Assert.IsNull(ScreenBinding.Parse("run@"), "@ 뒤가 비면 틀린 글");
        }

        [Test]
        public void 키에_콜론이_든_플레이어_데이터도_가리킨다()
        {
            _session.Data.Define("stat:kills", DataZone.Meta, 12.0, "처치");
            _session.Data.Define("hand:1", DataZone.Session, new SbRun { gold = 7 }, "손패 1");
            Assert.AreEqual("12", ScreenBinding.Parse("stat:kills").Text(_session, null), "키 그대로");
            Assert.AreEqual("7", ScreenBinding.Parse("hand:1:gold").Text(_session, null), "키 뒤의 경로");
            Assert.AreEqual("99", ScreenBinding.Parse("run:gold").Text(_session, null), "콜론이 없는 키는 그대로");
            CollectionAssert.AreEqual(new[] { "hand:1:gold", "hand:1", "hand" }, ScreenBinding.Parse("hand:1:gold").Keys, "다시 그릴 키 후보(긴 것부터)");
        }

        [Test]
        public void 값을_글로_보이고_목록은_개수다()
        {
            Assert.AreEqual("30", ScreenBinding.Parse("run:hero.hp").Text(_session, "HP"));
            Assert.AreEqual("체력 30 / 50", ScreenBinding.Parse("run:hero.hp").Text(_session, "체력 {value} / 50"), "원래 글자에 {value}가 있으면 틀");
            Assert.AreEqual("2", ScreenBinding.Parse("run:relics").Text(_session, null));
            Assert.AreEqual(1, ScreenBinding.Number(new List<int> { 4 }));
            Assert.IsFalse(ScreenBinding.Flag(ScreenBinding.Parse("run:boss").Read(_session)));
            Assert.AreEqual("", ScreenBinding.Parse("nope:gold").Text(_session, null), "없는 키는 빈 글");
        }

        [Test]
        public void 결과가_아닌_BindTo_클래스는_결과로_만들지_않는다()
        {
            Assert.AreEqual(typeof(SbScreenCode), BoundOutcomes.ClassFor("sb.screen", typeof(SbScreenCode)));
            Assert.IsNull(BoundOutcomes.ClassFor("sb.screen", typeof(Outcome)));
            Assert.IsFalse(BoundOutcomes.Has("sb.screen"), "결과로 연결된 것이 아니다");
            Assert.AreEqual(0, BoundOutcomes.Create("sb.screen").Count);
        }
    }
}
