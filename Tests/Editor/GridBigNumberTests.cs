using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GBible.Tests
{
    // 격자(Grid)·큰 수(BigNumber)·밸런싱 값 합·개수.
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
        public void 큰_수는_작은_정수를_더하고_빼도_오차가_쌓이지_않는다()
        {
            BigNumber c = 0;
            for (var i = 0; i < 49; i++) c += 1;
            Assert.IsFalse(c < 49, "1을 49번 더하면 49");
            Assert.AreEqual(new BigNumber(49), c);
            BigNumber half = 0;
            for (var i = 0; i < 48; i++) half += 0.5;
            Assert.IsTrue(half >= 24);
            BigNumber down = 1000;
            for (var i = 0; i < 999; i++) down -= 1;
            Assert.AreEqual(1.0, down.ToDouble());
            Assert.AreEqual(113.0, BigNumber.Floor(new BigNumber(113)).ToDouble());
            Assert.AreEqual("113", new BigNumber(113).ToString("full", null));
            Assert.AreEqual("123,456,789,012", new BigNumber(123456789012).ToString("full", null));
            Assert.AreEqual(8.0, BigNumber.Floor(BigNumber.Pow(2, 3)).ToDouble());
            Assert.AreEqual(new BigNumber(113), new BigNumber { m = 1.13, e = 2 }, "예전 저장 모양(가수 × 10^지수)도 같은 수");
            Assert.AreEqual("1M", new BigNumber(999600).ToString(), "반올림이 단위를 채우면 다음 단위");
            Assert.AreEqual("-156K", new BigNumber(-155500).ToString(), "음수도 양수와 같은 자릿수");
            Assert.AreEqual("0", new BigNumber(-0.004).ToString("full", null), "full은 소수를 버린다");
            Assert.AreEqual("1e6", new BigNumber(999600).ToString("sci", null));
            Assert.AreEqual("1억", new BigNumber(99999999).ToString("ko", null));
            Assert.AreEqual("1K", new BigNumber(999.996).ToString(), "단위 경계 바로 아래도 다음 단위");
            Assert.AreEqual("1만", new BigNumber(9999.996).ToString("ko", null));
            Assert.AreEqual("1e3", new BigNumber(999.996).ToString("sci", null));
            Assert.DoesNotThrow(() => new BigNumber(5e-324).ToString());
            Assert.AreEqual(new BigNumber(5e-324), new BigNumber(5e-324) * 1);
            var huge = new BigNumber(1.5, 400);
            Assert.AreEqual(new BigNumber(3, 400), huge + huge, "큰 수끼리는 그대로");
            Assert.AreEqual(new BigNumber(1.5, 400), huge + 1, "큰 수에 작은 수를 더하면 그대로");
            Assert.IsTrue(new BigNumber(1e299) * 1e299 > huge, "작은 수끼리 곱해 큰 수(1e598)가 되어도 비교가 맞다");
            Assert.AreEqual(598, (int)Math.Round((new BigNumber(1e299) * 1e299).Log10()));
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

        sealed class Warnings : IValidationContext
        {
            public readonly List<string> Messages = new List<string>();
            public bool StatExists(string id) => true;
            public bool AchievementExists(string id) => true;
            public StatDefinitionInfo? FindStat(string id) => null;
            public void Report(ValidationSeverity severity, string message)
            {
                if (severity == ValidationSeverity.Warning) Messages.Add(message);
            }
        }

        [Test]
        public void 가격이_0_이하가_되는_단계부터는_세지_않고_검증이_알린다()
        {
            var session = new GameSession(new SessionOptions { Seed = 3 });
            var falling = new Curve { kind = CurveKind.Linear, startX = 0, step = -10 };   // 30 · 20 · 10 · 0 · −10 …
            session.Balance.Define("price", 30, falling);
            Assert.AreEqual(3, session.Game.BalanceCount("price", 0, 1000), "0이 되는 넷째부터는 값을 매길 수 없어 사지 못한다(10000개가 아니다)");
            Assert.AreEqual(30 + 20 + 10, session.Game.BalanceSum("price", 0, 10), "합도 그 앞까지 — 음수가 되지 않는다");
            Assert.AreEqual(0, session.Game.BalanceCount("price", 5, 1000));
            Assert.AreEqual(0, session.Game.BalanceSum("price", 5, 3));

            var warnings = new Warnings();
            falling.Validate(warnings, "곡선");
            Assert.AreEqual(1, warnings.Messages.Count, "줄어들기만 하고 자르지 않는 곡선");
            warnings.Messages.Clear();
            falling.Validate(warnings, "곡선", 30);
            StringAssert.Contains("3단계에서 0 이하", warnings.Messages.Single(), "기준 값을 알면 그 단계까지");
            warnings.Messages.Clear();
            new Curve { kind = CurveKind.Linear, step = -10, useMin = true, min = 1 }.Validate(warnings, "곡선", 30);
            new Curve { kind = CurveKind.Linear, step = 5 }.Validate(warnings, "곡선", 30);
            new Curve { kind = CurveKind.Linear, step = -1 }.Validate(warnings, "곡선", -5);
            Assert.IsEmpty(warnings.Messages, "최솟값으로 자른 것·오르는 것·처음부터 0 이하인 것(가격이 아닌 값)은 묻지 않는다");
            new Curve { kind = CurveKind.Table, points = { new CurvePoint { x = 1, y = 10 }, new CurvePoint { x = 5, y = -5 } } }.Validate(warnings, "곡선");
            StringAssert.Contains("점 5에서", warnings.Messages.Single(), "표는 기준 값 없이도");
        }

        [Test]
        public void 직선과_시야는_양쪽에서_같다()
        {
            var a = new Cell(0, 0);
            var b = new Cell(2, 1);
            var line = Grid.Line(a, b);
            Assert.AreEqual(a, line.First());
            Assert.AreEqual(b, line.Last(), "a에서 b 차례");
            CollectionAssert.AreEqual(line, Enumerable.Reverse(Grid.Line(b, a)).ToList(), "Line(a, b)는 Line(b, a)를 거꾸로 한 것");
            var middle = line[1];
            Assert.AreEqual(Grid.Visible(a, b, c => c == middle), Grid.Visible(b, a, c => c == middle), "A가 B를 못 보면 B도 A를 못 본다");
            for (var x1 = -3; x1 <= 3; x1++)
            for (var y1 = -3; y1 <= 3; y1++)
            for (var x2 = -3; x2 <= 3; x2++)
            for (var y2 = -3; y2 <= 3; y2++)
            {
                var from = new Cell(x1, y1);
                var to = new Cell(x2, y2);
                CollectionAssert.AreEqual(Grid.Line(from, to), Enumerable.Reverse(Grid.Line(to, from)).ToList(), $"{from} ↔ {to}");
            }
        }

        // 맨 아래 줄(y = 0)은 길(비용 0), 나머지는 풀(1). 7 × 3칸.
        static int RoadCost(Cell c) => c.x < 0 || c.y < 0 || c.x > 6 || c.y > 2 ? -1 : c.y == 0 ? 0 : 1;

        [Test]
        public void 비용_0인_칸이_있어도_가장_싼_길이고_이동_범위와_같은_비용이다()
        {
            var start = new Cell(0, 2);
            var goal = new Cell(6, 2);
            var path = Grid.Path(start, goal, RoadCost);
            var spent = path.Sum(RoadCost);
            var left = Grid.Reachable(start, 10, RoadCost)[goal];
            Assert.AreEqual(10 - left, spent, "이동 범위(Reachable)와 같은 비용");
            Assert.AreEqual(3, spent, "길로 내려갔다 올라온다(곧장 가면 6)");
            Assert.IsTrue(path.Any(c => c.y == 0));
            Assert.AreEqual(goal, path.Last());
            // 비용이 모두 1 이상인 맵은 minCost: 1로 목표 쪽으로만 넓혀도 같은 비용이다.
            var walls = Grid.Path(new Cell(0, 1), new Cell(3, 3), Cost, minCost: 1);
            Assert.AreEqual(Grid.Path(new Cell(0, 1), new Cell(3, 3), Cost).Sum(Cost), walls.Sum(Cost));
        }
    }
}
