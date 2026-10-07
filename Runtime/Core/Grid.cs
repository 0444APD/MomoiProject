using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GBible
{
    // 격자(칸 단위 게임: 전술·로그라이크 던전·보드·농장): 칸 좌표와 거리·이웃·이동 범위·길찾기·직선·시야·범위 모양.
    // 맵 자체는 게임의 데이터다(원본 인스턴스의 rows 글자 목록·칸 → 지형 딕셔너리 …). 여기 함수들은 비용·막힘을 함수로 받는다.
    //   var range = Grid.Reachable(unit.Cell, unit.move, c => map.CostAt(c));          // 이동할 수 있는 칸 → 남은 이동력
    //   var path = Grid.Path(unit.Cell, target, c => map.CostAt(c));                   // 길(시작 칸 뺀 차례), 없으면 빈 목록
    //   bool see = Grid.Visible(a, b, c => map.Blocks(c));                             // 사이 칸이 막지 않으면 참
    //   foreach (var c in Grid.Area(center, 2, GridMetric.Manhattan)) …                // 마름모 범위
    // 같은 비용이면 늘 같은 길(결정론): Undo·멀티·재현에서 같다. 비용 함수가 음수를 돌려주면 그 칸은 지나갈 수 없다.
    // 플레이어 데이터에는 x·y(int) 필드로 둔다(Cell은 계산용). 딕셔너리 키가 필요하면 Grid.Key(x, y) = "x,y".
    [Serializable]
    public struct Cell : IEquatable<Cell>
    {
        public int x;
        public int y;

        public Cell(int x, int y)
        {
            this.x = x;
            this.y = y;
        }

        public bool Equals(Cell other) => x == other.x && y == other.y;
        public override bool Equals(object obj) => obj is Cell other && Equals(other);
        public override int GetHashCode() => (x * 73856093) ^ (y * 19349663);
        public static bool operator ==(Cell a, Cell b) => a.Equals(b);
        public static bool operator !=(Cell a, Cell b) => !a.Equals(b);
        public static Cell operator +(Cell a, Cell b) => new Cell(a.x + b.x, a.y + b.y);
        public static Cell operator -(Cell a, Cell b) => new Cell(a.x - b.x, a.y - b.y);
        public override string ToString() => Grid.Key(x, y);
    }

    public enum GridMetric
    {
        // 상하좌우 4방향(마름모 범위).
        Manhattan,
        // 대각선 포함 8방향(정사각형 범위).
        Chebyshev,
    }

    public static class Grid
    {
        public static readonly Cell Up = new Cell(0, 1), Down = new Cell(0, -1), Left = new Cell(-1, 0), Right = new Cell(1, 0);
        static readonly Cell[] Four = { Right, Up, Left, Down };
        static readonly Cell[] Eight = { Right, new Cell(1, 1), Up, new Cell(-1, 1), Left, new Cell(-1, -1), Down, new Cell(1, -1) };

        public static string Key(int x, int y) => x.ToString(CultureInfo.InvariantCulture) + "," + y.ToString(CultureInfo.InvariantCulture);
        public static string Key(Cell cell) => Key(cell.x, cell.y);

        public static bool TryParse(string key, out Cell cell)
        {
            cell = default;
            var comma = key?.IndexOf(',') ?? -1;
            if (comma <= 0) return false;
            if (!int.TryParse(key.Substring(0, comma), NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)) return false;
            if (!int.TryParse(key.Substring(comma + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var y)) return false;
            cell = new Cell(x, y);
            return true;
        }

        public static int Distance(Cell a, Cell b, GridMetric metric = GridMetric.Manhattan)
        {
            var dx = Math.Abs(a.x - b.x);
            var dy = Math.Abs(a.y - b.y);
            return metric == GridMetric.Chebyshev ? Math.Max(dx, dy) : dx + dy;
        }

        public static IEnumerable<Cell> Neighbors(Cell cell, bool diagonal = false)
        {
            foreach (var step in diagonal ? Eight : Four) yield return cell + step;
        }

        // 가장 가까운 4방향(밀치기·바라보는 쪽). 같은 칸이면 (0,0).
        public static Cell Direction(Cell from, Cell to)
        {
            var dx = to.x - from.x;
            var dy = to.y - from.y;
            if (dx == 0 && dy == 0) return default;
            return Math.Abs(dx) >= Math.Abs(dy) ? new Cell(Math.Sign(dx), 0) : new Cell(0, Math.Sign(dy));
        }

        // 시계 방향으로 turns번(90°씩) 돌린 상대 좌표(무늬·범위의 회전). Grid 좌표는 y가 위(+)다 — 목록 칸(Placement)은 화면 기준 y 아래(+)라 Placement.Rotate.
        public static Cell Rotate(Cell offset, int turns)
        {
            switch (((turns % 4) + 4) % 4)
            {
                case 1: return new Cell(offset.y, -offset.x);
                case 2: return new Cell(-offset.x, -offset.y);
                case 3: return new Cell(-offset.y, offset.x);
                default: return offset;
            }
        }

        // 이동 범위: 시작 칸에서 budget 이내로 갈 수 있는 칸 → 남은 이동력(시작 칸 포함). cost(칸)이 음수면 못 들어간다.
        public static Dictionary<Cell, int> Reachable(Cell start, int budget, Func<Cell, int> cost, bool diagonal = false)
        {
            var left = new Dictionary<Cell, int> { [start] = budget };
            var frontier = new SortedSet<(int spent, int order, Cell cell)>(Comparer<(int spent, int order, Cell cell)>.Create((a, b) =>
                a.spent != b.spent ? a.spent.CompareTo(b.spent) : a.order.CompareTo(b.order)));
            var order = 0;
            frontier.Add((0, order++, start));
            while (frontier.Count > 0)
            {
                var current = frontier.Min;
                frontier.Remove(current);
                if (budget - current.spent < left[current.cell]) continue;
                foreach (var next in Neighbors(current.cell, diagonal))
                {
                    var step = cost(next);
                    if (step < 0) continue;
                    var spent = current.spent + step;
                    if (spent > budget) continue;
                    if (left.TryGetValue(next, out var had) && had >= budget - spent) continue;
                    left[next] = budget - spent;
                    frontier.Add((spent, order++, next));
                }
            }
            return left;
        }

        // 길찾기(A*): 시작 칸을 뺀 칸들의 차례. 갈 수 없으면 빈 목록. maxCost를 넘는 길은 찾지 않는다(0이면 제한 없음).
        // 가장 싼 길이다(Reachable과 같은 비용) — minCost가 모든 칸 비용 이하일 때: 목표까지의 어림 = 남은 칸 수 × minCost라 실제로 남은 비용을 넘지 않는다.
        //   minCost = 들어갈 수 있는 칸 비용의 최솟값. 기본 0은 비용 0인 칸(길·다리)이 있어도 늘 맞다(어림 없이 싼 칸부터 넓힌다).
        //   모든 칸이 1 이상인 맵은 1을 주면 목표 쪽으로만 넓혀 더 빨리 찾는다. 그보다 싼 칸이 있으면 가장 싼 길이 아닐 수 있다(아직 보지 않은 싼 칸은 미리 알 수 없다).
        //   만난 칸이 minCost보다 싸면 그때부터 그 비용으로 어림을 낮춘다(덜 빗나가게 — 보장은 아니다).
        public static List<Cell> Path(Cell start, Cell goal, Func<Cell, int> cost, bool diagonal = false, int maxCost = 0, int minCost = 0)
        {
            var metric = diagonal ? GridMetric.Chebyshev : GridMetric.Manhattan;
            var scale = Math.Max(0, minCost);
            var spentTo = new Dictionary<Cell, int> { [start] = 0 };
            var cameFrom = new Dictionary<Cell, Cell>();
            var byScore = Comparer<(int score, int order, Cell cell, int spent)>.Create((a, b) =>
                a.score != b.score ? a.score.CompareTo(b.score) : a.order.CompareTo(b.order));
            var open = new SortedSet<(int score, int order, Cell cell, int spent)>(byScore);
            var order = 0;
            open.Add((Distance(start, goal, metric) * scale, order++, start, 0));
            var guard = 0;
            while (open.Count > 0 && guard++ < 100000)
            {
                var current = open.Min;
                open.Remove(current);
                // 더 싼 길로 다시 넣은 칸의 옛 줄은 건너뛴다.
                if (current.spent > spentTo[current.cell]) continue;
                if (current.cell == goal)
                {
                    var path = new List<Cell>();
                    for (var at = goal; at != start; at = cameFrom[at]) path.Add(at);
                    path.Reverse();
                    return path;
                }
                foreach (var next in Neighbors(current.cell, diagonal))
                {
                    var step = cost(next);
                    if (step < 0) continue;
                    if (step < scale)
                    {
                        // minCost보다 싼 칸: 어림이 실제 비용을 넘지 않게 낮추고 열린 칸들의 점수를 다시 매긴다(넣은 차례는 그대로 — 결정론).
                        scale = step;
                        var rescored = new SortedSet<(int score, int order, Cell cell, int spent)>(byScore);
                        foreach (var item in open) rescored.Add((item.spent + Distance(item.cell, goal, metric) * scale, item.order, item.cell, item.spent));
                        open = rescored;
                    }
                    var spent = current.spent + step;
                    if (maxCost > 0 && spent > maxCost) continue;
                    if (spentTo.TryGetValue(next, out var had) && had <= spent) continue;
                    spentTo[next] = spent;
                    cameFrom[next] = current.cell;
                    open.Add((spent + Distance(next, goal, metric) * scale, order++, next, spent));
                }
            }
            return new List<Cell>();
        }

        // 직선(Bresenham): a에서 b까지(양 끝 포함)의 칸들. 늘 같은 쪽 끝(x가 작은 쪽, 같으면 y가 작은 쪽)에서 긋고 필요하면 뒤집으므로
        // Line(a, b)는 Line(b, a)를 거꾸로 한 것이다 — A가 B를 보면 B도 A를 본다(시야가 대칭).
        public static List<Cell> Line(Cell a, Cell b)
        {
            var swap = a.x > b.x || (a.x == b.x && a.y > b.y);
            if (swap) (a, b) = (b, a);
            var cells = new List<Cell>();
            int x = a.x, y = a.y, dx = Math.Abs(b.x - a.x), dy = -Math.Abs(b.y - a.y), sx = a.x < b.x ? 1 : -1, sy = a.y < b.y ? 1 : -1, err = dx + dy;
            while (true)
            {
                cells.Add(new Cell(x, y));
                if (x == b.x && y == b.y) break;
                var e2 = 2 * err;
                if (e2 >= dy) { err += dy; x += sx; }
                if (e2 <= dx) { err += dx; y += sy; }
            }
            if (swap) cells.Reverse();
            return cells;
        }

        // 시야: a와 b 사이(양 끝 뺀) 칸 중 막는 칸이 없으면 참.
        public static bool Visible(Cell a, Cell b, Func<Cell, bool> blocks)
        {
            var line = Line(a, b);
            for (var i = 1; i < line.Count - 1; i++)
                if (blocks(line[i])) return false;
            return true;
        }

        // 범위 모양: 중심에서 거리 radius 이내(Manhattan = 마름모, Chebyshev = 정사각형). minRadius로 가운데를 비운다(링).
        public static IEnumerable<Cell> Area(Cell center, int radius, GridMetric metric = GridMetric.Manhattan, int minRadius = 0)
        {
            for (var dy = -radius; dy <= radius; dy++)
                for (var dx = -radius; dx <= radius; dx++)
                {
                    var cell = new Cell(center.x + dx, center.y + dy);
                    var d = Distance(center, cell, metric);
                    if (d <= radius && d >= minRadius) yield return cell;
                }
        }

        // 한 방향 직선 범위(관통·돌진): origin 다음 칸부터 direction으로 length칸.
        public static IEnumerable<Cell> Ray(Cell origin, Cell direction, int length)
        {
            var at = origin;
            for (var i = 0; i < length; i++)
            {
                at += direction;
                yield return at;
            }
        }

        // 부채꼴(앞쪽 삼각형): direction(4방향) 쪽으로 거리 1..length, 거리 d에서 좌우로 d−1칸씩 퍼진다.
        public static IEnumerable<Cell> Cone(Cell origin, Cell direction, int length)
        {
            var side = new Cell(-direction.y, direction.x);
            for (var d = 1; d <= length; d++)
                for (var s = -d + 1; s <= d - 1; s++)
                    yield return new Cell(origin.x + direction.x * d + side.x * s, origin.y + direction.y * d + side.y * s);
        }
    }
}
