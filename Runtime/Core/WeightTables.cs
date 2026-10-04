using System;
using System.Collections.Generic;
using System.Linq;

namespace GBible
{
    // 가중치 표(DB › 가중치 표): 드롭·보상·조우처럼 "풀(후보들)에서 무게만큼의 확률로 뽑기".
    //   var drop = Game.Roll(GameIds.WeightTable.EliteDrop);                 // 항목 ID 하나(아무것도 안 나오면 null)
    //   var cards = Game.Roll(GameIds.WeightTable.CardReward, 3, unique: true); // 서로 다른 3개
    // 풀은 뽑는 순간에 만든다. 세 가지가 섞일 수 있다:
    //   - 미리 정한 풀: 항목 = DB 항목 ID(인스턴스·다른 가중치 표 …) · 무게 · 개수(최소~최대).
    //   - 진행에 따라 바뀌는 풀: 항목의 조건(맞을 때만 후보, 도전과제·해금·층 …). 뽑을 때마다 다시 보므로 업데이트로 항목이 늘어도 저장을 고칠 일이 없다.
    //   - 판에서 만들어지는 풀: 플레이어 데이터 목록 줄(그 목록의 요소마다 후보) · 뺄 목록(가진 유물·본 이벤트는 후보가 아님).
    // 다른 가중치 표를 항목으로 넣으면 그 표에서 다시 뽑는다. "꽝" 무게를 주면 그만큼 아무것도 나오지 않는다.
    // 표 없이 코드에서 만든 풀은 Game.Roll(풀, 개수, unique, stream)(난수 흐름 "pool:" + stream).
    // 난수는 표마다 따로인 결정론 흐름(Game.Random("table:ID"))이라 Undo하면 같은 결과, 표를 추가해도 다른 표의 결과가 바뀌지 않는다.
    // 행동 안에서 부른다(난수 상태가 행동과 함께 되돌아간다).
    [Serializable]
    public sealed class WeightEntry
    {
        public string item = "";
        public double weight = 1;
        public int minCount = 1;
        public int maxCount = 1;
        public Condition condition;
        // 비어 있지 않으면 이 줄은 플레이어 데이터 목록(listKey › listPath)의 요소들이다(item은 쓰지 않음).
        // 요소 = ID 글자 또는 id가 있는 객체(그 id). 무게·개수·조건은 요소마다 같다. 같은 ID는 한 번만.
        public string listKey = "";
        public string listPath = "";
        // 비어 있지 않으면 무게 = 밸런스 값(weightBalance)의 단계 값. 단계 = 플레이어 데이터 weightStepKey › weightStepPath의 숫자
        // (비우면 기준값). 천장(뽑은 횟수가 늘수록 확률이 오름)·운·레벨에 따라 바뀌는 무게.
        public string weightBalance = "";
        public string weightStepKey = "";
        public string weightStepPath = "";
    }

    // 플레이어 데이터 안의 목록 하나(키 › 필드 경로).
    [Serializable]
    public sealed class PlayerDataListRef
    {
        [PlayerDataRef, FieldPathRoot(FieldPathRootKind.PlayerDataKey), FieldLabel("플레이어 데이터")] public string key = "";
        [FieldPath(EmptyLabel = "값 전체"), FieldLabel("필드(목록)")] public string path = "";
    }

    public sealed class WeightTableDefinition
    {
        public string Id;
        public double EmptyWeight;
        public readonly List<WeightEntry> Entries = new List<WeightEntry>();
        // 뺄 목록: 이 목록들에 있는 ID는 후보가 아니다(가진 유물·이번 판에 본 이벤트). 안쪽 표에도 적용된다.
        public readonly List<PlayerDataListRef> Exclude = new List<PlayerDataListRef>();
    }

    public sealed class WeightTableSystem
    {
        const int MaxDepth = 8;

        readonly GameSession _session;
        readonly Dictionary<string, WeightTableDefinition> _tables = new Dictionary<string, WeightTableDefinition>(StringComparer.Ordinal);

        internal WeightTableSystem(GameSession session)
        {
            _session = session;
        }

        public void Define(WeightTableDefinition table)
        {
            if (table != null && !string.IsNullOrEmpty(table.Id)) _tables[table.Id] = table;
        }

        public bool Has(string id) => id != null && _tables.ContainsKey(id);

        readonly HashSet<string> _warnedSingle = new HashSet<string>(StringComparer.Ordinal);

        // 하나만 뽑기(Game.Roll(id)): 개수가 2 이상인 항목이 나오면 첫 ID만 돌려주고, 표마다 한 번 경고한다.
        public string RollOne(string id)
        {
            var rolled = Roll(id);
            if (rolled.Count > 1 && _warnedSingle.Add(id))
                _session.Journal.Append(JournalKind.Warning, "Tables",
                    $"가중치 표 '{id}'에서 개수가 2 이상인 항목이 나와 Game.Roll(id)은 첫 ID만 돌려줍니다. 개수까지 쓰려면 Game.Roll(id, 1)로 목록을 받으세요.");
            return rolled.FirstOrDefault();
        }
        public WeightTableDefinition Find(string id) => id != null && _tables.TryGetValue(id, out var t) ? t : null;

        // count번 뽑는다. unique면 한 번 나온 항목은 다시 후보가 되지 않는다(후보가 모자라면 있는 만큼만).
        // 안쪽 표는 그 안에 남은 것이 있을 때만 후보다(등급 표 → 등급별 카드 표에서도 서로 다른 카드, 다 나온 등급은 건너뜀).
        // 항목의 개수(최소~최대)만큼 같은 ID가 여러 번 들어간다. 꽝은 결과에 넣지 않는다.
        public List<string> Roll(string id, int count = 1, bool unique = false)
        {
            var table = Find(id) ?? throw new InvalidOperationException($"[MomoiProject] 없는 가중치 표 '{id}'입니다(DB › 가중치 표).");
            var result = new List<string>();
            var used = unique ? new HashSet<string>(StringComparer.Ordinal) : null;
            for (int i = 0; i < count; i++) RollInto(table, "table:" + id, result, used, null, 0);
            return result;
        }

        // 코드에서 만든 풀에서 뽑는다(표 없이). 항목이 가중치 표 ID면 그 표에서 다시 뽑는다. 난수 흐름은 "pool:" + stream(쓰는 곳마다 이름을 다르게).
        public List<string> Roll(IEnumerable<(string item, double weight)> pool, int count = 1, bool unique = false, string stream = "pool")
        {
            var table = new WeightTableDefinition { Id = "pool:" + stream };
            foreach (var (item, weight) in pool ?? Enumerable.Empty<(string, double)>())
                if (!string.IsNullOrEmpty(item)) table.Entries.Add(new WeightEntry { item = item, weight = weight });
            var result = new List<string>();
            var used = unique ? new HashSet<string>(StringComparer.Ordinal) : null;
            for (int i = 0; i < count; i++) RollInto(table, table.Id, result, used, null, 0);
            return result;
        }

        void RollInto(WeightTableDefinition table, string stream, List<string> result, HashSet<string> used, HashSet<string> blocked, int depth)
        {
            if (depth >= MaxDepth) throw new InvalidOperationException($"[MomoiProject] 가중치 표 '{table.Id}'가 {MaxDepth}단계 넘게 서로를 부릅니다(표끼리 돌고 있는지 확인하세요).");
            blocked = Blocked(table, blocked);
            var candidates = Candidates(table, used, blocked, depth);
            var total = candidates.Sum(e => e.weight) + Math.Max(0, table.EmptyWeight);
            if (total <= 0) return;
            var random = _session.Random.Get(stream);
            var roll = random.NextDouble() * total;
            WeightEntry picked = null;
            foreach (var entry in candidates)
            {
                roll -= entry.weight;
                if (roll < 0)
                {
                    picked = entry;
                    break;
                }
            }
            if (picked == null) return;   // 꽝
            var min = Math.Max(0, picked.minCount);
            var max = Math.Max(min, picked.maxCount);
            var copies = min == max ? min : random.Range(min, max + 1);
            for (int c = 0; c < copies; c++)
            {
                if (Has(picked.item)) RollInto(Find(picked.item), "table:" + picked.item, result, used, blocked, depth + 1);
                else
                {
                    result.Add(picked.item);
                    used?.Add(picked.item);
                }
            }
        }

        // 지금 뽑을 수 있는 항목: 무게 > 0, 조건이 맞고, 뺄 목록에 없고, unique면 아직 안 나온 것. 목록 줄은 요소마다 한 후보로 펼친다.
        // 안쪽 표는 그 안에 뽑을 것이 남아 있을 때만(unique·뺄 목록이 있을 때 본다).
        List<WeightEntry> Candidates(WeightTableDefinition table, HashSet<string> used, HashSet<string> blocked, int depth)
        {
            var list = new List<WeightEntry>();
            foreach (var source in table.Entries)
            {
                if (source == null) continue;
                var w = WeightOf(source);
                if (w <= 0) continue;
                var e = w == source.weight ? source : new WeightEntry { item = source.item, weight = w, minCount = source.minCount, maxCount = source.maxCount, condition = source.condition, listKey = source.listKey, listPath = source.listPath };
                if (e.condition != null && !e.condition.Evaluate(_session)) continue;
                if (!string.IsNullOrEmpty(e.listKey))
                {
                    foreach (var id in IdsAt(e.listKey, e.listPath).Distinct(StringComparer.Ordinal))
                        if (Open(id, used, blocked)) list.Add(new WeightEntry { item = id, weight = e.weight, minCount = e.minCount, maxCount = e.maxCount });
                    continue;
                }
                if (string.IsNullOrEmpty(e.item)) continue;
                if (Has(e.item))
                {
                    var inner = Find(e.item);
                    if ((used == null && blocked == null && inner.Exclude.Count == 0) || depth + 1 >= MaxDepth
                        || Candidates(inner, used, Blocked(inner, blocked), depth + 1).Count > 0) list.Add(e);
                }
                else if (Open(e.item, used, blocked)) list.Add(e);
            }
            return list;
        }

        // 줄의 무게: 밸런스 값이 있으면 그 단계 값(단계 = 플레이어 데이터의 숫자), 없으면 적은 무게.
        double WeightOf(WeightEntry entry)
        {
            if (string.IsNullOrEmpty(entry.weightBalance)) return entry.weight;
            if (string.IsNullOrEmpty(entry.weightStepKey) || !_session.Data.IsDefined(entry.weightStepKey)) return _session.Balance.Get(entry.weightBalance);
            var step = ScreenBinding.Number(FieldPaths.Resolve(_session.Data.Get(entry.weightStepKey), entry.weightStepPath ?? "").FirstOrDefault());
            return _session.Balance.Get(entry.weightBalance, step);
        }

        static bool Open(string id, HashSet<string> used, HashSet<string> blocked) =>
            (used == null || !used.Contains(id)) && (blocked == null || !blocked.Contains(id));

        // 표의 뺄 목록을 더한 집합(표에 뺄 목록이 없으면 받은 것 그대로).
        HashSet<string> Blocked(WeightTableDefinition table, HashSet<string> outer)
        {
            if (table.Exclude.Count == 0) return outer;
            var set = outer == null ? new HashSet<string>(StringComparer.Ordinal) : new HashSet<string>(outer, StringComparer.Ordinal);
            foreach (var list in table.Exclude)
                if (list != null) set.UnionWith(IdsAt(list.key, list.path));
            return set;
        }

        // 플레이어 데이터 목록의 ID들: 요소가 글자면 그대로, id가 있는 객체면 그 id. 없는 키는 비어 있다.
        IEnumerable<string> IdsAt(string key, string path)
        {
            if (string.IsNullOrEmpty(key) || !_session.Data.IsDefined(key)) yield break;
            foreach (var node in FieldPaths.Resolve(_session.Data.Get(key), path))
                foreach (var item in FieldPaths.IsCollectionValue(node) ? FieldPaths.Items(node) : new[] { node })
                {
                    var id = item as string ?? (item != null && GameApi.HasId(item.GetType()) ? GameApi.IdOf(item) : null);
                    if (!string.IsNullOrEmpty(id)) yield return id;
                }
        }

        // 표 하나의 항목별 확률(조건·목록·뺄 목록은 지금 상태로 본다). 화면의 "드롭 확률" 표시·검증용. 안쪽 표는 한 줄.
        public IReadOnlyList<(string item, double chance)> Chances(string id)
        {
            var table = Find(id);
            if (table == null) return Array.Empty<(string, double)>();
            var candidates = Candidates(table, null, Blocked(table, null), 0);
            var total = candidates.Sum(e => e.weight) + Math.Max(0, table.EmptyWeight);
            return total <= 0 ? Array.Empty<(string, double)>() : candidates.Select(e => (e.item, e.weight / total)).ToList();
        }
    }
}
