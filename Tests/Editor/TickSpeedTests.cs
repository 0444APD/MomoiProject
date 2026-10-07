using NUnit.Framework;

namespace GBible.Tests
{
    // 고정 틱의 한 프레임 한도와 호스트의 배속(GameSession.HostSpeed — Unity에서는 GameTime이 맞춘다).
    public class TickSpeedTests
    {
        static GameSession NewSession(int tickRate) => new GameSession(new SessionOptions { Seed = 1, TickRate = tickRate });

        [Test]
        public void 배속이면_한_프레임에_따라잡는_틱도_배속만큼_늘어난다()
        {
            var session = NewSession(60);
            var ticks = 0;
            session.Ticked += _ => ticks++;
            session.HostSpeed = 16;
            for (var i = 0; i < 60; i++) session.EndFrame(16.0 / 60);   // ×16으로 60fps 1초 = 게임 16초
            Assert.AreEqual(16 * 60, ticks, 1, "남은 시간을 버리지 않는다(한도가 8이면 480틱 — 규칙 시간만 ×8)");
            Assert.AreEqual(16.0, session.Clock.Time, 0.02);
        }

        [Test]
        public void 배속을_알리지_않으면_한도는_그대로_8틱이다()
        {
            var session = NewSession(60);
            var ticks = 0;
            session.Ticked += _ => ticks++;
            session.EndFrame(16.0 / 60);   // 16틱어치가 한 프레임에 들어온 느린 프레임
            Assert.AreEqual(GameSession.MaxTicksPerFrame, session.TickLimit);
            Assert.AreEqual(GameSession.MaxTicksPerFrame, ticks, "남은 시간은 버린다");
            Assert.IsTrue(session.TickAlpha < 1);
        }

        [TestCase(1.0, 8)]
        [TestCase(0.25, 8)]
        [TestCase(1.5, 16)]
        [TestCase(16.0, 128)]
        [TestCase(1000.0, 128)]
        [TestCase(double.NaN, 8)]
        [TestCase(double.PositiveInfinity, 128)]
        public void 한도는_8에_올린_배속을_곱하고_상한을_넘지_않는다(double speed, int limit)
        {
            var session = NewSession(60);
            session.HostSpeed = speed;
            Assert.AreEqual(limit, session.TickLimit);
        }

        [Test]
        public void 배속을_아무리_올려도_한_프레임에_상한까지만_돈다()
        {
            var session = NewSession(10);
            var ticks = 0;
            session.Ticked += _ => ticks++;
            session.HostSpeed = 1000;
            session.EndFrame(100);   // 1000틱어치
            Assert.AreEqual(GameSession.MaxTicksPerFrameAtSpeed, ticks);
        }
    }
}
