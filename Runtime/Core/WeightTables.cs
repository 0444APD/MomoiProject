using System;
using System.Collections.Generic;
using System.Linq;

namespace GBible
{
    // 가중치 표(DB › 가중치 표): 드롭·보상·조우처럼 "여러 후보 중 무게만큼의 확률로 뽑기".
    //   var drop = Game.Roll(GameIds.Table.EliteDrop);                 // 항목 ID 하나(아무것도 안 나오면 null)
    //   var cards = Game.Roll(GameIds.Table.CardReward, 3, unique: true); // 서로 다른 3개
    // 항목 = DB 항목 ID(인스턴스·다른 가중치 표 …) · 무게 · 개수(최소~최대) · 조건(맞을 때만 후보).
    // 다른 가중치 표를 항목으로 넣으면 그 표에서 다시 뽑는다. "꽝" 무게를 주면 그만큼 아무것도 나오지 않는다.
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
    }

    public sealed class WeightTableDefinition
    {
        public string Id;
        public double EmptyWeight;
        public readonly List<WeightEntry> Entries = new List<WeightEntry>();
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
        public WeightTableDefinition Find(string id) => id != null && _tables.TryGetValue(id, out var t) ? t : null;

        // count번 뽑는다. unique면 한 번 나온 항목은 다시 후보가 되지 않는다(후보가 모자라면 있는 만큼만).
        // 안쪽 표는 계속 후보로 남고, 그 안에서 이미 나온 항목만 빠진다(등급 표 → 등급별 카드 표에서도 서로 다른 카드).
        // 항목의 개수(최소~최대)만큼 같은 ID가 여러 번 들어간다. 꽝은 결과에 넣지 않는다.
        public List<string> Roll(string id, int count = 1, bool unique = false)
        {
            var result = new List<string>();
            var used = unique ? new HashSet<string>(StringComparer.Ordinal) : null;
            for (int i = 0; i < count; i++) RollInto(id, result, used, 0);
            return result;
        }

        void RollInto(string id, List<string> result, HashSet<string> used, int depth)
        {
            var table = Find(id) ?? throw new InvalidOperationException($"[MomoiProject] 없는 가중치 표 '{id}'입니다(DB › 가중치 표).");
            if (depth >= MaxDepth) throw new InvalidOperationException($"[MomoiProject] 가중치 표 '{id}'가 {MaxDepth}단계 넘게 서로를 부릅니다(표끼리 돌고 있는지 확인하세요).");
            var candidates = table.Entries.Where(e => e != null && e.weight > 0 && !string.IsNullOrEmpty(e.item)
                                                      && (used == null || Has(e.item) || !used.Contains(e.item))
                                                      && (e.condition == null || e.condition.Evaluate(_session))).ToList();
            var total = candidates.Sum(e => e.weight) + Math.Max(0, table.EmptyWeight);
            if (total <= 0) return;
            var random = _session.Random.Get("table:" + id);
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
                if (Has(picked.item)) RollInto(picked.item, result, used, depth + 1);
                else
                {
                    result.Add(picked.item);
                    used?.Add(picked.item);
                }
            }
        }

        // 표 하나의 항목별 확률(조건은 지금 상태로 본다). 화면의 "드롭 확률" 표시·검증용.
        public IReadOnlyList<(string item, double chance)> Chances(string id)
        {
            var table = Find(id);
            if (table == null) return Array.Empty<(string, double)>();
            var candidates = table.Entries.Where(e => e != null && e.weight > 0 && !string.IsNullOrEmpty(e.item)
                                                      && (e.condition == null || e.condition.Evaluate(_session))).ToList();
            var total = candidates.Sum(e => e.weight) + Math.Max(0, table.EmptyWeight);
            return total <= 0 ? Array.Empty<(string, double)>() : candidates.Select(e => (e.item, e.weight / total)).ToList();
        }
    }
}
