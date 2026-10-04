using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GBible.Tests
{
    // 격자(Grid)·큰 수(BigNumber)·밸런스 값 합·개수.
    public class GridBigNumberTests
    {
        [Serializable] public class GbRun { public BigNumber cookies; public List<BigNumber> history = new List<BigNumber>(); }

        sealed class Bake : GameAction
        {
            public double amount;
            protected override void Run() => Game.Get<GbRun>("run").cookies += new BigNumber(amount, 300);
        }

        //   0123
        // 3 ....
        // 2 .##.
        // 1 ....
        // 0 S..G
        static readonly HashSet<Cell> Walls = new HashSet<Cell> { new Cell(1, 2), new Cell(2, 2) };
        static int Cost(Cell c) => c.x < 0 || c.y < 0 || c.x > 3 || c.y > 3 || Walls.Contains(c) ? -1 : 1;

        [Test]
        public void 격자의_거리_이동_범위_길_시야를_센다()
        {
            Assert.AreEqual(5, Grid.Distance(new Cell(0, 0), new Cell(3, 2)));
            Assert.AreEqual(3, Grid.Distance(new Cell(0, 0), new Cell(3, 2), GridMetric.Chebyshev));
            var range = Grid.Reachable(new Cell(0, 0), 2, Cost);
            CollectionAssert.AreEquivalent(new[] { new Cell(0, 0), new Cell(1, 0), new Cell(2, 0), new Cell(0, 1), new Cell(1, 1), new Cell(0, 2) }, range.Keys);
            Assert.AreEqual(0, range[new Cell(2, 0)], "두 칸 가면 남은 이동력 0");
            var path = Grid.Path(new Cell(0, 1), new Cell(3, 3), Cost);
            Assert.AreEqual(5, path.Count, "벽을 돌아간다");
            Assert.IsFalse(path.Any(Walls.Contains));
            CollectionAssert.AreEqual(path, Grid.Path(new Cell(0, 1), new Cell(3, 3), Cost), "같은 입력이면 같은 길");
            Assert.IsEmpty(Grid.Path(new Cell(0, 0), new Cell(9, 9), Cost), "못 가면 빈 목록");
            Assert.IsFalse(Grid.Visible(new Cell(0, 2), new Cell(3, 2), Walls.Contains));
            Assert.IsTrue(Grid.Visible(new Cell(0, 0), new Cell(3, 0), Walls.Contains));
            Assert.AreEqual(13, Grid.Area(new Cell(5, 5), 2).Count(), "반지름 2 마름모");
            Assert.AreEqual(Grid.Right, Grid.Direction(new Cell(0, 0), new Cell(4, 1)));
            Assert.IsTrue(Grid.TryParse(Grid.Key(-3, 7), out var parsed) && parsed == new Cell(-3, 7));
        }

        [Test]
        public void 큰_수는_double을_넘어서도_계산하고_보인다()
        {
            var big = new BigNumber(1.5, 400);
            Assert.AreEqual(new BigNumber(3, 400), big + big);
            Assert.AreEqual(new BigNumber(2.25, 800), big * big);
            Assert.IsTrue(big > new BigNumber(9, 399));
            Assert.AreEqual(big, big + 1, "차이가 17자리 넘게 나면 작은 쪽은 사라진다");
            Assert.IsTrue(double.IsPositiveInfinity(big.ToDouble()));
            Assert.AreEqual("1.23K", new BigNumber(1234).ToString());
            Assert.AreEqual("4.56Qa", new BigNumber(4.56, 15).ToString());
            Assert.AreEqual("1.5e400", big.ToString());
            Assert.AreEqual("1.23억", new BigNumber(123450000).ToString("ko", null));
            Assert.AreEqual(new BigNumber(1.5, 400), BigNumber.Parse("1.5e400"));
            Assert.AreEqual(new BigNumber(3200), BigNumber.Parse("3.2K"));
            Assert.AreEqual(new BigNumber(1, 30), BigNumber.Pow(new BigNumber(10), 30));
            Assert.AreEqual("1.23K", ScreenBinding.Format(new BigNumber(1234)), "binding-path도 같은 글");
        }

        [Test]
        public void 큰_수_필드는_Undo와_판_저장을_따른다()
        {
            var session = new GameSession(new SessionOptions { Seed = 2 });
            session.Data.Define("run", DataZone.Session, new GbRun(), "판");
            session.Game.Do(new Bake { amount = 2 });
            session.Game.Do(new Bake { amount = 3 });
            Assert.AreEqual(new BigNumber(5, 300), session.Data.Get<GbRun>("run").cookies);
            Assert.IsTrue(session.Undo());
            Assert.AreEqual(new BigNumber(2, 300), session.Data.Get<GbRun>("run").cookies);
            var json = session.ExportRun();
            var loaded = new GameSession(new SessionOptions { Seed = 2 });
            loaded.Data.Define("run", DataZone.Session, new GbRun(), "판");
            loaded.ImportRun(json);
            Assert.AreEqual(new BigNumber(2, 300), loaded.Data.Get<GbRun>("run").cookies);
        }

        [Test]
        public void 밸런스_값의_합과_살_수_있는_개수()
        {
            var session = new GameSession(new SessionOptions { Seed = 3 });
            session.Balance.Define("price", 10, new Curve { kind = CurveKind.Linear, startX = 0, step = 5 });
            Assert.AreEqual(10 + 15 + 20, session.Game.BalanceSum("price", 0, 3));
            Assert.AreEqual(3, session.Game.BalanceCount("price", 0, 50), "10+15+20 = 45 ≤ 50 < 70");
            Assert.AreEqual(0, session.Game.BalanceCount("price", 4, 20), "30이 넘어 하나도 못 산다");
        }
    }
}
