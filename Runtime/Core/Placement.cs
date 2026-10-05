using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace GBible
{
    // 자리(Placement): 플레이어 데이터의 목록 하나를 칸으로 본다. 감싸는 형식 없이 평범한 List<T> 그대로다.
    //   한 줄(조커 칸·지팡이 칸·손패): 줄 길이 0.   격자(가방·보드): 줄 길이 = 열 수(6이면 0~5가 첫 줄).
    //   칸 번호 = 목록 위치, 빈 칸 = null. 줄 = 번호 / 열 수, 열 = 번호 % 열 수. 마지막 줄은 덜 차도 된다(34칸 = 6×5 + 4).
    //   좌표는 화면 기준: x = 열(오른쪽 +), y = 줄(아래 +). "위"는 앞 줄이다.
    // 코드: Placement.Around(run.bag, artifact, 6, PlaceDirection.Left) · Placement.Is(run.jokers, joker, 0, PlaceSpot.First)
    //       석판처럼 상대 칸 무늬: Placement.Pattern(index, count, 6, offsets, turns) — turns번 시계 방향으로 돌린 무늬.
    // DB: 대상 '자리로 고르기'(옆 칸·같은 줄·맞은편 줄 맨 앞 …) · 조건 '자리 검사'(맨 위 줄·가장자리·양옆이 빈 칸 …).
    public enum PlaceDirection
    {
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("왼쪽")]
#endif
        Left,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("오른쪽")]
#endif
        Right,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("위(앞 줄)")]
#endif
        Up,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("아래(뒤 줄)")]
#endif
        Down,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("상하좌우")]
#endif
        Around4,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("주변 8칸")]
#endif
        Around8,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("같은 줄")]
#endif
        SameRow,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("같은 열")]
#endif
        SameColumn,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("같은 줄 맨 앞(차 있는 첫 칸)")]
#endif
        RowFirst,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("같은 줄 맨 뒤(차 있는 끝 칸)")]
#endif
        RowLast,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("목록 맨 처음(차 있는 첫 칸)")]
#endif
        First,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("목록 맨 끝(차 있는 끝 칸)")]
#endif
        Last,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("같은 칸(다른 목록의 맞은편)")]
#endif
        Same,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("나를 뺀 모두")]
#endif
        Others,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("같은 열 맨 앞(차 있는 첫 칸, 위부터)")]
#endif
        ColumnFirst,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("같은 열 맨 뒤(차 있는 끝 칸)")]
#endif
        ColumnLast,
    }

    public enum PlaceSpot
    {
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("맨 위 줄")]
#endif
        TopRow,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("맨 아래 줄")]
#endif
        BottomRow,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("맨 왼쪽 열")]
#endif
        LeftColumn,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("맨 오른쪽 열")]
#endif
        RightColumn,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("가장자리(상하좌우 중 바깥이 있음)")]
#endif
        Edge,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("안쪽(가장자리가 아님)")]
#endif
        Inner,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("목록 맨 처음(차 있는 것 중)")]
#endif
        First,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("목록 맨 끝(차 있는 것 중)")]
#endif
        Last,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("같은 줄 맨 앞(차 있는 것 중)")]
#endif
        RowFirst,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("양옆이 빈 칸")]
#endif
        SidesEmpty,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("같은 줄에 혼자")]
#endif
        RowAlone,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("n번째 칸(0부터)")]
#endif
        Index,
    }

    public static class Placement
    {
        // 칸 번호 → 좌표(x = 열, y = 줄). 줄 길이 0이면 한 줄.
        public static Cell CellOf(int index, int columns) => columns <= 0 ? new Cell(index, 0) : new Cell(index % columns, index / columns);

        // 좌표 → 칸 번호. 목록 밖이면 −1.
        public static int IndexOf(Cell cell, int count, int columns)
        {
            if (cell.x < 0 || cell.y < 0) return -1;
            if (columns <= 0) return cell.y == 0 && cell.x < count ? cell.x : -1;
            if (cell.x >= columns) return -1;
            var index = cell.y * columns + cell.x;
            return index < count ? index : -1;
        }

        // 목록에서 그 요소의 칸 번호(같은 객체 또는 같은 id, 요소가 id 글자여도). 없으면 −1.
        public static int IndexOf(IList list, object item)
        {
            if (list == null || item == null) return -1;
            var id = item as string ?? (GameApi.HasId(item.GetType()) ? GameApi.IdOf(item) : null);
            for (int i = 0; i < list.Count; i++)
            {
                var element = list[i];
                if (element == null) continue;
                if (ReferenceEquals(element, item)) return i;
                if (id == null) continue;
                if (element is string text ? text == id : GameApi.HasId(element.GetType()) && GameApi.IdOf(element) == id) return i;
            }
            return -1;
        }

        // 시계 방향으로 turns번(90°씩) 돌린 상대 좌표(화면 기준, y 아래 +). 석판·무늬의 회전.
        public static Cell Rotate(Cell offset, int turns)
        {
            switch (((turns % 4) + 4) % 4)
            {
                case 1: return new Cell(-offset.y, offset.x);
                case 2: return new Cell(-offset.x, -offset.y);
                case 3: return new Cell(offset.y, -offset.x);
                default: return offset;
            }
        }

        // 그 칸에서 상대 무늬(돌린 뒤)에 해당하는 칸 번호들(목록 밖은 뺀다).
        public static IEnumerable<int> Pattern(int index, int count, int columns, IEnumerable<Cell> offsets, int turns = 0)
        {
            var origin = CellOf(index, columns);
            foreach (var offset in offsets ?? Enumerable.Empty<Cell>())
            {
                var at = IndexOf(origin + Rotate(offset, turns), count, columns);
                if (at >= 0) yield return at;
            }
        }

        // 그 칸에서 방향에 있는 칸 번호들. 빈 칸(null)도 낸다(차 있는 것만 원하면 Around).
        //   "맨 앞·맨 끝"처럼 차 있는 칸을 찾는 방향은 list가 있어야 한다. 다른 목록(맞은편 보드)의 칸을 볼 때는 그 목록의 count를 주고
        //   includeSelf를 참으로(같은 줄·같은 열·모두가 기준과 같은 번호의 칸 = 바로 맞은편도 낸다).
        public static IEnumerable<int> Indices(int index, int count, int columns, PlaceDirection direction, int distance = 1, IList list = null, bool includeSelf = false)
        {
            // 기준이 없으면(−1) 목록 전체 기준의 방향만(맨 처음·맨 끝·모두).
            if (index < 0)
            {
                switch (direction)
                {
                    case PlaceDirection.First: return Enumerable.Range(0, count).Where(i => list == null || (i < list.Count && list[i] != null)).Take(1);
                    case PlaceDirection.Last: return Enumerable.Range(0, count).Reverse().Where(i => list == null || (i < list.Count && list[i] != null)).Take(1);
                    case PlaceDirection.Others: return Enumerable.Range(0, count);
                    default: return Array.Empty<int>();
                }
            }
            var origin = CellOf(index, columns);
            var rows = columns <= 0 ? 1 : (count + columns - 1) / columns;
            var width = columns <= 0 ? count : columns;
            distance = Math.Max(1, distance);
            switch (direction)
            {
                case PlaceDirection.Left: return One(origin + new Cell(-distance, 0));
                case PlaceDirection.Right: return One(origin + new Cell(distance, 0));
                case PlaceDirection.Up: return One(origin + new Cell(0, -distance));
                case PlaceDirection.Down: return One(origin + new Cell(0, distance));
                case PlaceDirection.Around4: return Many(new[] { new Cell(0, -1), new Cell(1, 0), new Cell(0, 1), new Cell(-1, 0) });
                case PlaceDirection.Around8:
                    return Many(new[] { new Cell(-1, -1), new Cell(0, -1), new Cell(1, -1), new Cell(-1, 0), new Cell(1, 0), new Cell(-1, 1), new Cell(0, 1), new Cell(1, 1) });
                case PlaceDirection.SameRow: return Enumerable.Range(0, width).Select(x => IndexOf(new Cell(x, origin.y), count, columns)).Where(i => i >= 0 && (includeSelf || i != index));
                case PlaceDirection.SameColumn: return Enumerable.Range(0, rows).Select(y => IndexOf(new Cell(origin.x, y), count, columns)).Where(i => i >= 0 && (includeSelf || i != index));
                case PlaceDirection.RowFirst:
                    return Filled(Enumerable.Range(0, width).Select(x => IndexOf(new Cell(x, origin.y), count, columns))).Take(1);
                case PlaceDirection.RowLast:
                    return Filled(Enumerable.Range(0, width).Reverse().Select(x => IndexOf(new Cell(x, origin.y), count, columns))).Take(1);
                case PlaceDirection.ColumnFirst:
                    return Filled(Enumerable.Range(0, rows).Select(y => IndexOf(new Cell(origin.x, y), count, columns))).Take(1);
                case PlaceDirection.ColumnLast:
                    return Filled(Enumerable.Range(0, rows).Reverse().Select(y => IndexOf(new Cell(origin.x, y), count, columns))).Take(1);
                case PlaceDirection.First: return Filled(Enumerable.Range(0, count)).Take(1);
                case PlaceDirection.Last: return Filled(Enumerable.Range(0, count).Reverse()).Take(1);
                case PlaceDirection.Same: return One(origin);
                default: return Enumerable.Range(0, count).Where(i => includeSelf || i != index);
            }

            IEnumerable<int> One(Cell cell)
            {
                var at = IndexOf(cell, count, columns);
                return at >= 0 ? new[] { at } : Array.Empty<int>();
            }

            IEnumerable<int> Many(IEnumerable<Cell> offsets) => offsets.Select(o => IndexOf(origin + o, count, columns)).Where(i => i >= 0);

            IEnumerable<int> Filled(IEnumerable<int> indices) => indices.Where(i => i >= 0 && (list == null || (i < list.Count && list[i] != null)));
        }

        // 그 요소 기준 방향에 있는 차 있는 요소들(빈 칸은 뺀다). 다른 목록(맞은편)을 보려면 target에 그 목록.
        //   var right = Placement.Around(run.jokers, joker, 0, PlaceDirection.Right).FirstOrDefault();
        public static IEnumerable<T> Around<T>(IList<T> list, T item, int columns, PlaceDirection direction, int distance = 1, IList<T> target = null)
        {
            var index = IndexOf((IList)list, item);
            if (index < 0) yield break;
            var into = target ?? list;
            foreach (var i in Indices(index, into.Count, columns, direction, distance, (IList)into, includeSelf: target != null))
                if (i < into.Count && into[i] != null && !(target == null && i == index)) yield return into[i];
        }

        // 그 칸이 그 자리인가.
        public static bool Is(IList list, int index, int columns, PlaceSpot spot, int n = 0)
        {
            if (list == null || index < 0 || index >= list.Count) return false;
            var count = list.Count;
            var cell = CellOf(index, columns);
            var lastRow = columns <= 0 ? 0 : (count - 1) / columns;
            var width = columns <= 0 ? count : columns;
            switch (spot)
            {
                case PlaceSpot.TopRow: return cell.y == 0;
                case PlaceSpot.BottomRow: return cell.y == lastRow;
                case PlaceSpot.LeftColumn: return cell.x == 0;
                case PlaceSpot.RightColumn: return cell.x == width - 1;
                case PlaceSpot.Edge: return IsEdge(cell, count, columns);
                case PlaceSpot.Inner: return !IsEdge(cell, count, columns);
                case PlaceSpot.First: return Enumerable.Range(0, count).FirstOrDefault(i => list[i] != null) == index && list[index] != null;
                case PlaceSpot.Last: return Enumerable.Range(0, count).Reverse().FirstOrDefault(i => list[i] != null) == index && list[index] != null;
                case PlaceSpot.RowFirst: return Indices(index, count, columns, PlaceDirection.RowFirst, 1, list).FirstOr(-1) == index;
                case PlaceSpot.SidesEmpty:
                    return Indices(index, count, columns, PlaceDirection.Left, 1).Concat(Indices(index, count, columns, PlaceDirection.Right, 1)).All(i => list[i] == null);
                case PlaceSpot.RowAlone: return Indices(index, count, columns, PlaceDirection.SameRow, 1).All(i => list[i] == null);
                default: return index == n;
            }
        }

        public static bool Is<T>(IList<T> list, T item, int columns, PlaceSpot spot, int n = 0) => Is((IList)list, IndexOf((IList)list, item), columns, spot, n);

        // DB 요약 글에 쓰는 한글 이름(InspectorName과 같다).
        public static string Name(PlaceDirection direction)
        {
            switch (direction)
            {
                case PlaceDirection.Left: return "왼쪽";
                case PlaceDirection.Right: return "오른쪽";
                case PlaceDirection.Up: return "위";
                case PlaceDirection.Down: return "아래";
                case PlaceDirection.Around4: return "상하좌우";
                case PlaceDirection.Around8: return "주변 8칸";
                case PlaceDirection.SameRow: return "같은 줄";
                case PlaceDirection.SameColumn: return "같은 열";
                case PlaceDirection.RowFirst: return "같은 줄 맨 앞";
                case PlaceDirection.RowLast: return "같은 줄 맨 뒤";
                case PlaceDirection.First: return "목록 맨 처음";
                case PlaceDirection.Last: return "목록 맨 끝";
                case PlaceDirection.Same: return "같은 칸";
                case PlaceDirection.ColumnFirst: return "같은 열 맨 앞";
                case PlaceDirection.ColumnLast: return "같은 열 맨 뒤";
                default: return "나를 뺀 모두";
            }
        }

        public static string Name(PlaceSpot spot)
        {
            switch (spot)
            {
                case PlaceSpot.TopRow: return "맨 위 줄";
                case PlaceSpot.BottomRow: return "맨 아래 줄";
                case PlaceSpot.LeftColumn: return "맨 왼쪽 열";
                case PlaceSpot.RightColumn: return "맨 오른쪽 열";
                case PlaceSpot.Edge: return "가장자리";
                case PlaceSpot.Inner: return "안쪽";
                case PlaceSpot.First: return "목록 맨 처음";
                case PlaceSpot.Last: return "목록 맨 끝";
                case PlaceSpot.RowFirst: return "같은 줄 맨 앞";
                case PlaceSpot.SidesEmpty: return "양옆이 빈 칸";
                case PlaceSpot.RowAlone: return "같은 줄에 혼자";
                default: return "n번째 칸";
            }
        }

        static bool IsEdge(Cell cell, int count, int columns)
        {
            foreach (var offset in new[] { new Cell(0, -1), new Cell(1, 0), new Cell(0, 1), new Cell(-1, 0) })
                if (IndexOf(cell + offset, count, columns) < 0) return true;
            return false;
        }

        static int FirstOr(this IEnumerable<int> source, int fallback)
        {
            foreach (var value in source) return value;
            return fallback;
        }
    }
}
