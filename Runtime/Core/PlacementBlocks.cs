using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace GBible
{
    // 자리 Block(DB): 목록(조커 칸·보드·가방)에서 기준의 자리를 찾아 방향의 칸을 고르거나(대상), 기준이 어느 자리인지 본다(조건).
    //   "오른쪽 조커" = 자리로 고르기(기준 사용자, 목록 run › jokers, 오른쪽)
    //   "맞은편 줄 맨 앞의 적" = 자리로 고르기(기준 사용자, 목록 run › board, 줄 길이 3, 같은 줄 맨 앞, 다른 목록 run › enemies)
    //   "인벤토리 맨 아래 줄일 때만" = 자리 검사(사용자, run › bag, 줄 길이 6, 맨 아래 줄)
    public enum PlaceOrigin
    {
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("사용자(가진 쪽)")]
#endif
        User,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("앞의 대상")]
#endif
        Previous,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("이벤트의 개체")]
#endif
        EventField,
    }

    static class PlaceBlocks
    {
        // 플레이어 데이터의 목록(요소가 id 글자면 그대로 — 고를 때 개체로 바꾼다).
        internal static IList ListOf(GameApi game, PlayerDataListRef reference)
        {
            if (game == null || reference == null || string.IsNullOrEmpty(reference.key) || !game.Session.Data.IsDefined(reference.key)) return null;
            return FieldPaths.Resolve(game.Session.Data.Get(reference.key), reference.path ?? "").FirstOrDefault() as IList;
        }

        internal static IEnumerable<object> FromEvent(GameApi game, TriggerContext context, string field)
        {
            var evt = context?.Event;
            if (evt == null || string.IsNullOrEmpty(field)) yield break;
            foreach (var node in FieldPaths.Resolve(evt, field).ToList())
                foreach (var item in FieldPaths.IsCollectionValue(node) ? FieldPaths.Items(node).ToList() : new List<object> { node })
                    if (item != null) yield return item is string id ? game.Find(id) ?? (object)id : item;
        }

        internal static object Resolve(GameApi game, object element) => element is string id ? game.Find(id) : element;
    }

    [Serializable]
    [BlockInfo("자리로 고르기", Category = "대상", Description = "목록(조커 칸·보드·가방)에서 기준이 놓인 자리를 찾아 그 방향 칸에 있는 것을 고릅니다(빈 칸은 뺍니다). 옆 칸·같은 줄·주변 8칸·맨 처음, 다른 목록을 고르면 맞은편 보드의 같은 줄 맨 앞 …",
               Sentence = "{origin} 기준 {direction}")]
    public sealed class PlaceTarget : TargetSelector
    {
        [FieldLabel("기준")] public PlaceOrigin origin = PlaceOrigin.User;
        [FieldLabel("이벤트 필드(id)"), ShowIf(nameof(origin), PlaceOrigin.EventField)] public string eventField = "";
        [FieldLabel("기준이 놓인 목록")] public PlayerDataListRef list = new PlayerDataListRef();
        [FieldLabel("줄 길이(0 = 한 줄)")] public int columns;
        [FieldLabel("방향")] public PlaceDirection direction = PlaceDirection.Right;
        [FieldLabel("거리(왼쪽·오른쪽·위·아래)")] public int distance = 1;
        [FieldLabel("다른 목록에서 고르기(비우면 같은 목록)")] public PlayerDataListRef other = new PlayerDataListRef();

        protected override IEnumerable<object> Select(IEnumerable<object> targets)
        {
            var game = Game;
            var source = PlaceBlocks.ListOf(game, list);
            if (source == null) yield break;
            var into = string.IsNullOrEmpty(other?.key) ? source : PlaceBlocks.ListOf(game, other);
            if (into == null) yield break;
            var bases = origin == PlaceOrigin.User ? (Use?.User == null ? Enumerable.Empty<object>() : new[] { Use.User })
                : origin == PlaceOrigin.Previous ? targets
                : PlaceBlocks.FromEvent(game, Use?.Context, eventField);
            var seen = new HashSet<object>();
            foreach (var at in bases.ToList())
            {
                var index = Placement.IndexOf(source, at);
                if (index < 0) continue;
                foreach (var i in Placement.Indices(index, into.Count, columns, direction, distance, into, includeSelf: !ReferenceEquals(into, source)))
                {
                    if (i >= into.Count || (ReferenceEquals(into, source) && i == index)) continue;
                    var found = into[i] == null ? null : PlaceBlocks.Resolve(game, into[i]);
                    if (found != null && seen.Add(found)) yield return found;
                }
            }
        }

        public override string Describe() => $"{(string.IsNullOrEmpty(other?.key) ? "" : "맞은편 ")}{Placement.Name(direction)}{(distance > 1 && direction <= PlaceDirection.Down ? $" {distance}칸" : "")}";

        public override void Validate(IValidationContext context)
        {
            if (string.IsNullOrEmpty(list?.key)) context.Error("자리로 고르기: 기준이 놓인 목록(플레이어 데이터)을 고르세요.");
            if (origin == PlaceOrigin.EventField && string.IsNullOrEmpty(eventField)) context.Error("자리로 고르기: 이벤트 필드를 적으세요.");
            if (columns < 0) context.Error("자리로 고르기: 줄 길이는 0 이상이어야 합니다.");
        }
    }

    // 조건 '자리 검사': 사용자(가진 쪽)나 이벤트의 개체가 목록에서 그 자리에 있나.
    [Serializable]
    [BlockInfo("자리 검사", Category = "조건", Description = "사용자(가진 쪽)나 이벤트의 개체가 목록(조커 칸·보드·가방)에서 그 자리에 있는지 봅니다. 맨 위 줄·맨 아래 줄·가장자리·안쪽·양옆이 빈 칸·같은 줄에 혼자·맨 처음 …",
               Sentence = "{spot}")]
    public sealed class PlaceCondition : SimpleCondition
    {
        [FieldLabel("누구")] public PlaceOrigin who = PlaceOrigin.User;
        [FieldLabel("이벤트 필드(id)"), ShowIf(nameof(who), PlaceOrigin.EventField)] public string eventField = "";
        [FieldLabel("목록")] public PlayerDataListRef list = new PlayerDataListRef();
        [FieldLabel("줄 길이(0 = 한 줄)")] public int columns;
        [FieldLabel("자리")] public PlaceSpot spot = PlaceSpot.Edge;
        [FieldLabel("n(n번째 칸)"), ShowIf(nameof(spot), PlaceSpot.Index)] public int index;
        [FieldLabel("아닐 때 참")] public bool not;

        protected override bool Check()
        {
            var source = PlaceBlocks.ListOf(Game, list);
            if (source == null) return false;
            var subject = who == PlaceOrigin.EventField ? PlaceBlocks.FromEvent(Game, Context, eventField).FirstOrDefault() : Context?.User;
            var at = Placement.IndexOf(source, subject);
            if (at < 0) return false;
            return Placement.Is(source, at, columns, spot, index) != not;
        }

        public override string Describe(INameResolver names) => $"{(who == PlaceOrigin.EventField ? "이벤트의 " + eventField : "사용자")}가 {(spot == PlaceSpot.Index ? index + "번째 칸" : Placement.Name(spot))}{(not ? " 아님" : "")}";

        public override void Validate(IValidationContext context)
        {
            if (string.IsNullOrEmpty(list?.key)) context.Error("자리 검사: 목록(플레이어 데이터)을 고르세요.");
            if (who == PlaceOrigin.EventField && string.IsNullOrEmpty(eventField)) context.Error("자리 검사: 이벤트 필드를 적으세요.");
            if (who == PlaceOrigin.Previous) context.Error("자리 검사: 누구는 사용자나 이벤트의 개체만 됩니다.");
        }
    }
}
