using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GBible.Data
{
    // DB › 가중치 표: 드롭·보상·조우처럼 풀에서 무게만큼의 확률로 뽑는 표. 코드는 Game.Roll(GameIds.WeightTable.X), DB는 결과 '가중치 표에서 주기'·'가중치 표에서 고르기'.
    // 풀은 뽑는 순간에 만든다: 미리 정한 항목 + 조건(진행·도전과제) + 플레이어 데이터 목록 줄(판에서 만들어진 것) − 뺄 목록(가진 것).
    // 줄마다 '고정 %'(전체 중 그 %) 또는 '무게'(고정 %가 남긴 몫을 무게 비율로) — "이 묶음(안쪽 표) = 전체의 5%". 상세 위 요약에 줄마다 최종 %가 보인다(Preview).
    [Database("가중치 표", Group = "DB", Order = 45, IdPrefix = "table")]
    [CreateAssetMenu(menuName = "MomoiProject/가중치 표", fileName = "NewWeightTable")]
    public sealed class WeightTableData : GameData
    {
        [FieldLabel("항목")] public List<WeightTableEntry> entries = new List<WeightTableEntry>();
        [FieldLabel("꽝 방식"), Tooltip("무게: 고정 %가 남긴 몫을 다른 무게 줄과 나눈다. 고정 %: 전체 중 그 %만큼 아무것도 나오지 않는다")] public WeightMode emptyMode = WeightMode.Weight;
        [FieldLabel("꽝 무게"), ShowIf(nameof(emptyMode), WeightMode.Weight), Tooltip("이 무게만큼 아무것도 나오지 않습니다(0이면 꽝 없음)")] public double emptyWeight;
        [FieldLabel("꽝 %"), ShowIf(nameof(emptyMode), WeightMode.Percent), Tooltip("전체 중 이 %만큼 아무것도 나오지 않습니다")] public double emptyPercent;
        [FieldLabel("뺄 목록"), Tooltip("이 플레이어 데이터 목록에 있는 ID는 뽑히지 않습니다(가진 유물·이번 판에 본 이벤트). 안쪽 표에도 적용됩니다")]
        public List<PlayerDataListRef> exclude = new List<PlayerDataListRef>();

        public WeightTableDefinition ToDefinition()
        {
            var definition = new WeightTableDefinition { Id = Id, EmptyWeight = Math.Max(0, emptyMode == WeightMode.Percent ? emptyPercent : emptyWeight), EmptyPercent = emptyMode == WeightMode.Percent };
            foreach (var entry in entries.Where(e => e != null && (e.source == WeightEntrySource.List ? !string.IsNullOrEmpty(e.listKey) : !string.IsNullOrEmpty(e.item))))
            {
                var list = entry.source == WeightEntrySource.List;
                definition.Entries.Add(new WeightEntry
                {
                    item = list ? "" : entry.item, weight = entry.Value, percent = entry.mode == WeightMode.Percent, minCount = entry.minCount, maxCount = entry.maxCount, condition = entry.condition,
                    listKey = list ? entry.listKey : "", listPath = list ? entry.listPath ?? "" : "",
                    weightBalance = entry.weightBalance ?? "", weightStepKey = entry.weightStepKey ?? "", weightStepPath = entry.weightStepPath ?? "",
                });
            }
            foreach (var list in exclude.Where(l => l != null && !string.IsNullOrEmpty(l.key)))
                definition.Exclude.Add(new PlayerDataListRef { key = list.key, path = list.path ?? "" });
            return definition;
        }

        // 편집 화면 요약용: 줄마다 최종 %(안쪽 표까지 곱한 전체 기준, depth = 안쪽 표 깊이). 뽑기와 같은 몫 계산(WeightTableSystem.Shares)을 쓰고,
        // 조건은 모두 맞다고, 목록 줄은 한 줄로, 밸런싱 값은 기준값으로 본다. find: 다른 가중치 표, balance: 밸런싱 값의 기준값(없으면 null).
        public List<(string label, double percent, int depth)> Preview(Func<string, WeightTableData> find, Func<string, double?> balance)
        {
            var lines = new List<(string label, double percent, int depth)>();
            AddPreview(this, 100, 0, lines, find, balance, new HashSet<string>(StringComparer.Ordinal));
            return lines;
        }

        static void AddPreview(WeightTableData table, double scale, int depth, List<(string label, double percent, int depth)> lines,
                               Func<string, WeightTableData> find, Func<string, double?> balance, HashSet<string> path)
        {
            if (table == null || depth >= 8 || !path.Add(table.Id ?? "")) return;
            var rows = table.entries.Where(e => e != null && (e.source == WeightEntrySource.List ? !string.IsNullOrEmpty(e.listKey) : !string.IsNullOrEmpty(e.item))).ToList();
            var values = rows.Select(e => (string.IsNullOrEmpty(e.weightBalance) ? e.Value : balance?.Invoke(e.weightBalance) ?? 0, e.mode == WeightMode.Percent)).ToList();
            values.Add((table.emptyMode == WeightMode.Percent ? table.emptyPercent : table.emptyWeight, table.emptyMode == WeightMode.Percent));
            var shares = WeightTableSystem.Shares(values, out var total);
            if (total > 0)
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    var e = rows[i];
                    var p = scale * shares[i] / total;
                    var label = e.source == WeightEntrySource.List ? "목록 " + PlayerDataGrants.PathLabel(e.listKey, e.listPath) : e.item;
                    if (e.condition != null) label += "(조건)";
                    lines.Add((label, p, depth));
                    if (e.source == WeightEntrySource.Item && find?.Invoke(e.item) is WeightTableData inner) AddPreview(inner, p, depth + 1, lines, find, balance, path);
                }
                var empty = scale * shares[rows.Count] / total;
                if (empty > 0) lines.Add(("꽝", empty, depth));
            }
            path.Remove(table.Id ?? "");
        }

        public override void ValidateData(IValidationContext context)
        {
            if (entries.Count == 0) context.Error("항목이 없습니다.");
            foreach (var entry in entries)
            {
                if (entry == null) continue;
                var name = entry.source == WeightEntrySource.List ? PlayerDataGrants.PathLabel(entry.listKey, entry.listPath) : entry.item;
                if (entry.source == WeightEntrySource.List)
                {
                    if (string.IsNullOrEmpty(entry.listKey)) context.Error("목록 줄의 플레이어 데이터를 고르지 않았습니다.");
                }
                else if (string.IsNullOrEmpty(entry.item)) context.Error("항목을 고르지 않은 줄이 있습니다.");
                else if (entry.item == Id) context.Error("표가 자기 자신을 항목으로 가지고 있습니다.");
                if (entry.Value < 0) context.Error($"'{name}': {(entry.mode == WeightMode.Percent ? "%" : "무게")}는 0 이상이어야 합니다.");
                if (entry.minCount < 0 || entry.maxCount < entry.minCount) context.Error($"'{name}': 개수는 0 ≤ 최소 ≤ 최대여야 합니다.");
                // 줄의 조건 Block(없는 도전과제·로그, 비어 있는 AND …). 트리거 칸이 아니라 DB 검증이 따로 보지 않는다.
                entry.condition?.Validate(context);
            }
            if (exclude.Any(l => l == null || string.IsNullOrEmpty(l.key))) context.Error("뺄 목록에 플레이어 데이터를 고르지 않은 줄이 있습니다.");
            if ((emptyMode == WeightMode.Percent ? emptyPercent : emptyWeight) < 0) context.Error("꽝은 0 이상이어야 합니다.");
            // 무게를 밸런싱 값에서 읽는 줄은 뽑는 순간에 무게가 정해지므로(천장·레벨) 고정 무게 0으로 보지 않는다.
            var rows = entries.Where(e => e != null).ToList();
            if (entries.Count > 0 && !rows.Any(e => !string.IsNullOrEmpty(e.weightBalance)) && rows.Sum(e => Math.Max(0, e.Value)) <= 0)
                context.Error("무게·%가 모두 0이라 아무것도 나오지 않습니다.");
            // 고정 %: 합이 100을 넘으면 오류(무게 줄의 몫이 없다), 무게 줄이 없는데 합이 100이 아니면 경고(비율대로 맞춰 뽑는다). 밸런싱 값에서 읽는 줄은 그 순간에 정해져 보지 않는다.
            var fixedRows = rows.Where(e => e.mode == WeightMode.Percent && string.IsNullOrEmpty(e.weightBalance)).ToList();
            var fixedSum = fixedRows.Sum(e => Math.Max(0, e.percent)) + (emptyMode == WeightMode.Percent ? Math.Max(0, emptyPercent) : 0);
            var anyWeight = rows.Any(e => e.mode == WeightMode.Weight && (e.weight > 0 || !string.IsNullOrEmpty(e.weightBalance))) || (emptyMode == WeightMode.Weight && emptyWeight > 0);
            var anyFixedBalance = rows.Any(e => e.mode == WeightMode.Percent && !string.IsNullOrEmpty(e.weightBalance));
            if (fixedSum > 100 + 1e-9) context.Error($"고정 % 합이 100을 넘습니다(지금 {fixedSum:0.##}%). 무게 줄은 몫을 받지 못하고, 고정 % 줄끼리 비율대로 나눕니다.");
            else if (fixedSum > 0 && !anyWeight && !anyFixedBalance && Math.Abs(fixedSum - 100) > 1e-9)
                context.Warning($"무게 줄 없이 고정 % 합이 {fixedSum:0.##}%입니다. 합이 100이 되게 비율대로 맞춰 뽑습니다(남는 몫을 꽝으로 하려면 꽝을 고정 %로).");
        }
    }

    public enum WeightMode
    {
        [InspectorName("무게(나머지를 나눔)")] Weight,
        [InspectorName("고정 %")] Percent,
    }

    public enum WeightEntrySource
    {
        [InspectorName("항목 하나")] Item,
        [InspectorName("플레이어 데이터 목록")] List,
    }

    [Serializable]
    public sealed class WeightTableEntry
    {
        [FieldLabel("줄 종류"), Tooltip("항목 하나: DB 항목 하나. 플레이어 데이터 목록: 판에서 만들어진 목록의 요소마다 후보(무게·개수·조건은 요소마다 같다)")]
        public WeightEntrySource source = WeightEntrySource.Item;
        [FieldLabel("항목"), DataRef(typeof(GameData)), ShowIf(nameof(source), WeightEntrySource.Item), Tooltip("DB 항목(인스턴스·다른 가중치 표 …). 다른 가중치 표면 그 표에서 다시 뽑는다")] public string item = "";
        [PlayerDataRef, FieldPathRoot(FieldPathRootKind.PlayerDataKey), FieldLabel("플레이어 데이터"), ShowIf(nameof(source), WeightEntrySource.List)] public string listKey = "";
        [FieldPath(EmptyLabel = "값 전체", Root = nameof(listKey)), FieldLabel("필드(목록)"), ShowIf(nameof(source), WeightEntrySource.List), Tooltip("요소가 ID 글자거나 id가 있는 객체인 목록")] public string listPath = "";
        [FieldLabel("방식"), Tooltip("고정 %: 전체 중 이 %(묶음이면 묶음 전체가 이 %). 무게: 고정 % 줄이 남긴 몫을 무게 줄끼리 무게 비율로 나눈다. 조건으로 줄이 빠지면 고정 %는 그대로, 빠진 몫은 무게 줄이 나눠 가진다")]
        public WeightMode mode = WeightMode.Weight;
        [FieldLabel("무게"), ShowIf(nameof(mode), WeightMode.Weight)] public double weight = 1;
        [FieldLabel("%(전체 중)"), ShowIf(nameof(mode), WeightMode.Percent), Tooltip("전체 중 이 줄이 나올 %. 목록 줄이면 요소들이 이 %를 나눠 가진다")] public double percent = 10;
        [FieldLabel("값을 밸런싱 값에서"), DataRef(typeof(BalanceData)), Tooltip("고르면 무게(고정 %면 %) = 이 밸런싱 값(곡선)의 단계 값. 천장·운·레벨·깊이에 따라 바뀌는 확률")] public string weightBalance = "";
        [PlayerDataRef, FieldPathRoot(FieldPathRootKind.PlayerDataKey), FieldLabel("단계(플레이어 데이터)"), ShowIf(nameof(weightBalance)), Tooltip("곡선의 단계로 쓸 숫자. 비우면 기준값")] public string weightStepKey = "";
        // 뿌리 = 바로 위 '단계(플레이어 데이터)'(Root를 정하지 않으면 줄에서 처음 만나는 뿌리 칸인 '플레이어 데이터'(listKey)를 본다).
        [FieldPath(EmptyLabel = "값 전체", Root = nameof(weightStepKey)), FieldLabel("단계 필드"), ShowIf(nameof(weightBalance))] public string weightStepPath = "";
        [FieldLabel("최소 개수")] public int minCount = 1;
        [FieldLabel("최대 개수")] public int maxCount = 1;
        [FieldLabel("조건(맞을 때만 후보)"), SerializeReference] public Condition condition;

        // 방식에 맞는 값(무게 또는 %). 밸런싱 값에서 읽는 줄은 뽑는 순간에 그 값이 대신한다.
        public double Value => mode == WeightMode.Percent ? percent : weight;
    }
}

namespace GBible
{
    using GBible.Data;

    // 결과 '가중치 표에서 주기': 표에서 뽑은 항목을 플레이어 데이터의 목록에 넣는다(인스턴스 목록이면 사본, ID 목록이면 ID).
    [Serializable]
    [BlockInfo("가중치 표에서 주기", Category = "결과", Description = "가중치 표에서 뽑은 항목을 플레이어 데이터의 목록에 넣습니다. 인스턴스 목록이면 그 인스턴스의 사본을, 글자 목록이면 ID를 넣습니다. 꽝이면 아무것도 넣지 않습니다.")]
    public sealed class RollTableOutcome : Outcome
    {
        [DataRef(typeof(WeightTableData)), FieldLabel("가중치 표")] public string table = "";
        [FieldLabel("횟수")] public int count = 1;
        [FieldLabel("서로 다른 것만")] public bool unique;
        [PlayerDataRef, FieldPathRoot(FieldPathRootKind.PlayerDataKey), FieldLabel("넣을 플레이어 데이터")] public string key = "";
        [FieldPath(EmptyLabel = "값 전체"), FieldLabel("필드(목록)")] public string path = "";

        public override OutcomeKinds Kind => OutcomeKinds.PlayerData;

        public override void Run(TriggerContext context)
        {
            var rolled = context.Game.Roll(table, Math.Max(1, count), unique);
            if (rolled.Count == 0) return;
            GrantIds.Into(context.Session, key, path, rolled);
        }

        public override string Describe(INameResolver names) =>
            $"{table}에서 {(count > 1 ? count + "개 " : "")}뽑아 {(string.IsNullOrEmpty(path) ? key : key + " › " + path)}에 넣기";

        public override void Validate(IValidationContext context)
        {
            if (string.IsNullOrEmpty(table)) context.Error("가중치 표에서 주기: 표를 고르세요.");
            if (string.IsNullOrEmpty(key)) context.Error("가중치 표에서 주기: 넣을 플레이어 데이터를 고르세요.");
            if (count < 1) context.Error("가중치 표에서 주기: 횟수는 1 이상이어야 합니다.");
        }
    }

    // 결과 '가중치 표에서 고르기': 표에서 뽑은 원본 ID를 후보로 보이고, 플레이어가 고른 것을 플레이어 데이터의 목록에 넣는다(보상 셋 중 하나).
    // 뽑기는 요구하는 순간(그 행동 안)에 끝나 후보가 플레이어 데이터에 남는다. 껐다 켜거나 Undo해도 후보가 바뀌지 않는다.
    [Serializable]
    [BlockInfo("가중치 표에서 고르기", Category = "결과", Description = "가중치 표에서 뽑은 항목을 후보로 보이고, 플레이어가 고른 것을 플레이어 데이터의 목록에 넣습니다(보상 셋 중 하나). 고른 뒤 결과에서는 고른 원본 ID를 읽을 수 있습니다.")]
    public sealed class ChooseFromTableOutcome : Outcome
    {
        [FieldLabel("안내 글")] public string prompt = "";
        [DataRef(typeof(WeightTableData)), FieldLabel("가중치 표")] public string table = "";
        [FieldLabel("후보 수")] public int count = 3;
        [FieldLabel("후보 수에 더할 숫자(플레이어 데이터)"), Tooltip("비우면 더하지 않습니다. 예: run › rewardBonus(유물 '선택지 +1'이 올린다)")] public PlayerDataNumberRef countBonus = new PlayerDataNumberRef();
        [FieldLabel("서로 다른 것만")] public bool unique = true;
        [FieldLabel("최소 개수"), Tooltip("0이면 고르지 않고 넘길 수 있습니다(건너뛰기)")] public int min = 1;
        [FieldLabel("최대 개수")] public int max = 1;
        [PlayerDataRef, FieldPathRoot(FieldPathRootKind.PlayerDataKey), FieldLabel("고른 것을 넣을 플레이어 데이터"), Tooltip("비우면 넣지 않습니다(고른 뒤 결과만)")] public string key = "";
        [FieldPath(EmptyLabel = "값 전체"), FieldLabel("필드(목록)")] public string path = "";
        [SerializeReference, FieldLabel("고른 뒤 결과")] public List<Outcome> then = new List<Outcome>();

        public override OutcomeKinds Kind => OutcomeKinds.PlayerData;

        public override void Run(TriggerContext context)
        {
            var game = context.Game;
            var total = Math.Max(1, count + GameNumbers.RoundToInt(countBonus?.Read(game.Session) ?? 0));
            var options = game.Roll(table, total, unique).Select(id => new ChoiceCandidate(id));
            var user = context.User != null && GameApi.HasId(context.User.GetType()) ? GameApi.IdOf(context.User) : "";
            var next = new ChoiceEffectsRun
            {
                owner = context.Owner ?? "", user = user, ids = true, key = key ?? "", path = path ?? "",
                then = (List<Outcome>)DataModel.Clone(then.Where(o => o != null).ToList(), null),
            };
            var choice = game.Session.Choices.Ask(prompt, options, next, min, max, source: context.Owner);
            // 다시 뽑기(Game.RerollChoice)가 같은 표·후보 수로 새로 뽑게 적어 둔다.
            if (choice != null) game.Session.Choices.MarkTable(choice, table, total, unique, min, max);
        }

        public override string Describe(INameResolver names)
        {
            var amount = min == max ? $"{min}개" : $"{min}~{max}개";
            var into = string.IsNullOrEmpty(key) ? "" : $" {(string.IsNullOrEmpty(path) ? key : key + " › " + path)}에 넣기";
            // 후보 수에 더할 숫자(유물 '선택지 +1' 등)가 있으면 함께 보인다.
            var bonus = string.IsNullOrEmpty(countBonus?.key) ? "" : $"(+{(string.IsNullOrEmpty(countBonus.path) ? countBonus.key : countBonus.key + " › " + countBonus.path)})";
            return $"{table}에서 {count}개{bonus} 보여 {amount} 고르기{into}";
        }

        public override void Validate(IValidationContext context)
        {
            if (string.IsNullOrEmpty(table)) context.Error("가중치 표에서 고르기: 표를 고르세요.");
            if (count < 1) context.Error("가중치 표에서 고르기: 후보 수는 1 이상이어야 합니다.");
            if (max < 1 || min > max) context.Error("가중치 표에서 고르기: 개수는 최소 ≤ 최대, 최대 1 이상이어야 합니다.");
            if (min < 0) context.Error("가중치 표에서 고르기: 최소 개수는 0 이상이어야 합니다.");
            if (string.IsNullOrEmpty(key) && then.Count == 0) context.Error("가중치 표에서 고르기: 고른 것을 넣을 플레이어 데이터나 고른 뒤 결과가 있어야 합니다.");
            if (then.Contains(null)) context.Error("가중치 표에서 고르기: 비어 있는 Block이 있습니다(스크립트가 지워졌을 수 있음).");
            foreach (var outcome in then) outcome?.Validate(context);
        }
    }
}
