using System;
using System.Collections.Generic;
using System.Linq;

namespace GBible
{
    // 기본 대상 Block(장르 공통): 앞의 대상(고른 대상 또는 앞 Block의 결과)을 받아 다음 대상을 낸다. 이어 붙이면 차례로 돈다.
    //   "체력이 가장 낮은 적 하나" = 플레이어 데이터 목록(run › battle.enemies) → 정렬해서 n개(hp, 1개)
    //   "불 속성 적 중 무작위 2" = 플레이어 데이터 목록 → 조건으로 거르기(element 일치 fire) → 무작위 n개(2)
    //   "때린 쪽에게 반사" = 이벤트의 개체(sourceId)
    // 프로젝트만의 대상(주인공·옆 칸의 적 …)은 TargetSelector를 상속해 더한다.

    // 플레이어 데이터 안의 목록의 요소 모두(앞의 대상은 버린다). 요소가 글자면 그 id의 개체를 찾는다.
    [Serializable]
    [BlockInfo("플레이어 데이터 목록", Category = "대상", Description = "플레이어 데이터 안의 목록(전투의 적·아군·손패 …)의 요소 모두. 앞의 대상은 버립니다. 요소가 id 글자면 그 개체를 찾습니다.")]
    public sealed class DataListTarget : TargetSelector
    {
        [PlayerDataRef, FieldPathRoot(FieldPathRootKind.PlayerDataKey), FieldLabel("플레이어 데이터")] public string key = "";
        [FieldPath(EmptyLabel = "값 전체"), FieldLabel("필드(목록)")] public string path = "";

        protected override IEnumerable<object> Select(IEnumerable<object> targets)
        {
            var session = Game?.Session;
            if (session == null || string.IsNullOrEmpty(key) || !session.Data.IsDefined(key)) yield break;
            foreach (var node in FieldPaths.Resolve(session.Data.Get(key), path).ToList())
                foreach (var item in FieldPaths.IsCollectionValue(node) ? FieldPaths.Items(node).ToList() : new List<object> { node })
                {
                    if (item is string id) { if (Game.Find(id) is object found) yield return found; }
                    else if (item != null) yield return item;
                }
        }

        public override string Describe() => string.IsNullOrEmpty(path) ? key : $"{key} › {path}";
    }

    // 받은 이벤트(가로채기면 의도)의 필드가 가리키는 개체. 글자면 그 id의 개체, 객체면 그대로, 목록이면 요소마다.
    [Serializable]
    [BlockInfo("이벤트의 개체", Category = "대상", Description = "받은 이벤트(가로채기면 의도)의 필드가 가리키는 개체. 예: 피해 이벤트의 sourceId = 때린 쪽(반사), targetId = 맞은 쪽.")]
    public sealed class EventFieldTarget : TargetSelector
    {
        [FieldLabel("이벤트 필드(id 또는 개체)")] public string field = "";

        protected override IEnumerable<object> Select(IEnumerable<object> targets)
        {
            var evt = Use?.Context?.Event;
            if (evt == null || string.IsNullOrEmpty(field)) yield break;
            foreach (var node in FieldPaths.Resolve(evt, field).ToList())
                foreach (var item in FieldPaths.IsCollectionValue(node) ? FieldPaths.Items(node).ToList() : new List<object> { node })
                {
                    if (item is string id) { if (Game.Find(id) is object found) yield return found; }
                    else if (item != null && GameApi.HasId(item.GetType())) yield return item;
                }
        }

        public override string Describe() => $"이벤트의 {field}";
    }

    // 앞의 대상 중 필드 검사가 참인 것만.
    [Serializable]
    [BlockInfo("조건으로 거르기", Category = "대상", Description = "앞의 대상 중 필드 검사가 참인 것만 남깁니다. 예: hp 이하 10, element 일치 fire, tags 포함 boss.")]
    public sealed class FilterTarget : TargetSelector
    {
        [FieldLabel("필드")] public string path = "";
        [FieldLabel("비교")] public FieldOp op = FieldOp.Equal;
        [FieldLabel("값")] public string value = "";

        protected override IEnumerable<object> Select(IEnumerable<object> targets) =>
            targets.Where(t => t != null && FieldOps.Test(t, path, op, value));

        public override string Describe() => FieldOps.Sentence(path, op, value);

        public override void Validate(IValidationContext context)
        {
            if (string.IsNullOrEmpty(path)) context.Error("조건으로 거르기: 필드를 적으세요.");
        }
    }

    // 앞의 대상 중 서로 다른 n개를 무작위로(결정론 난수 흐름).
    [Serializable]
    [BlockInfo("무작위 n개", Category = "대상", Description = "앞의 대상 중 서로 다른 n개를 무작위로 고릅니다(결정론 난수 흐름 — Undo하면 같은 결과).")]
    public sealed class RandomTarget : TargetSelector
    {
        [FieldLabel("개수")] public int count = 1;
        [FieldLabel("난수 흐름")] public string stream = "target";

        protected override IEnumerable<object> Select(IEnumerable<object> targets)
        {
            var list = targets.Where(t => t != null).ToList();
            var random = Game.Random(string.IsNullOrEmpty(stream) ? "target" : stream);
            var take = Math.Min(Math.Max(0, count), list.Count);
            for (var i = 0; i < take; i++)
            {
                var j = random.Range(i, list.Count);
                (list[i], list[j]) = (list[j], list[i]);
            }
            return list.Take(take);
        }

        public override string Describe() => $"무작위 {count}개";
    }

    // 앞의 대상을 숫자로 정렬해 앞에서 n개. 필드 이름 하나면 능력치(Modifier 포함), 경로면 그 값.
    [Serializable]
    [BlockInfo("정렬해서 n개", Category = "대상", Description = "앞의 대상을 숫자 필드로 정렬해 앞에서 n개를 남깁니다(0이면 모두). 예: hp 작은 것부터 1개 = 체력이 가장 낮은 적. 필드 이름 하나면 능력치(보정 포함)로 잽니다.")]
    public sealed class SortTarget : TargetSelector
    {
        [FieldLabel("숫자 필드")] public string path = "";
        [FieldLabel("큰 것부터")] public bool descending;
        [FieldLabel("개수(0 = 모두)")] public int count = 1;

        protected override IEnumerable<object> Select(IEnumerable<object> targets)
        {
            var list = targets.Where(t => t != null).Select((t, i) => (t, i, v: ValueOf(t))).ToList();
            // 값이 같으면 원래 순서(결정론).
            var sorted = descending ? list.OrderByDescending(x => x.v).ThenBy(x => x.i) : list.OrderBy(x => x.v).ThenBy(x => x.i);
            var result = sorted.Select(x => x.t);
            return count > 0 ? result.Take(count) : result;
        }

        double ValueOf(object target)
        {
            if (string.IsNullOrEmpty(path)) return 0;
            if (path.IndexOf('.') < 0 && path.IndexOf('[') < 0 && GameApi.HasId(target.GetType())) return Game.Attribute(target, path);
            return ScreenBinding.Number(FieldPaths.Resolve(target, path).FirstOrDefault());
        }

        public override string Describe() => $"{path} {(descending ? "큰" : "작은")} 것부터 {(count > 0 ? count + "개" : "모두")}";
    }
}
