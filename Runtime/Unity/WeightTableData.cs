using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GBible.Data
{
    // DB › 가중치 표: 드롭·보상·조우처럼 풀에서 무게만큼의 확률로 뽑는 표. 코드는 Game.Roll(GameIds.WeightTable.X), DB는 결과 '가중치 표에서 주기'·'가중치 표에서 고르기'.
    // 풀은 뽑는 순간에 만든다: 미리 정한 항목 + 조건(진행·도전과제) + 플레이어 데이터 목록 줄(판에서 만들어진 것) − 뺄 목록(가진 것).
    [Database("가중치 표", Group = "DB", Order = 45, IdPrefix = "table")]
    [CreateAssetMenu(menuName = "MomoiProject/가중치 표", fileName = "NewWeightTable")]
    public sealed class WeightTableData : GameData
    {
        [FieldLabel("항목")] public List<WeightTableEntry> entries = new List<WeightTableEntry>();
        [FieldLabel("꽝 무게"), Tooltip("이 무게만큼 아무것도 나오지 않습니다(0이면 꽝 없음)")] public double emptyWeight;
        [FieldLabel("뺄 목록"), Tooltip("이 플레이어 데이터 목록에 있는 ID는 뽑히지 않습니다(가진 유물·이번 판에 본 이벤트). 안쪽 표에도 적용됩니다")]
        public List<PlayerDataListRef> exclude = new List<PlayerDataListRef>();

        public WeightTableDefinition ToDefinition()
        {
            var definition = new WeightTableDefinition { Id = Id, EmptyWeight = Math.Max(0, emptyWeight) };
            foreach (var entry in entries.Where(e => e != null && (e.source == WeightEntrySource.List ? !string.IsNullOrEmpty(e.listKey) : !string.IsNullOrEmpty(e.item))))
            {
                var list = entry.source == WeightEntrySource.List;
                definition.Entries.Add(new WeightEntry
                {
                    item = list ? "" : entry.item, weight = entry.weight, minCount = entry.minCount, maxCount = entry.maxCount, condition = entry.condition,
                    listKey = list ? entry.listKey : "", listPath = list ? entry.listPath ?? "" : "",
                    weightBalance = entry.weightBalance ?? "", weightStepKey = entry.weightStepKey ?? "", weightStepPath = entry.weightStepPath ?? "",
                });
            }
            foreach (var list in exclude.Where(l => l != null && !string.IsNullOrEmpty(l.key)))
                definition.Exclude.Add(new PlayerDataListRef { key = list.key, path = list.path ?? "" });
            return definition;
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
                if (entry.weight < 0) context.Error($"'{name}': 무게는 0 이상이어야 합니다.");
                if (entry.minCount < 0 || entry.maxCount < entry.minCount) context.Error($"'{name}': 개수는 0 ≤ 최소 ≤ 최대여야 합니다.");
            }
            if (exclude.Any(l => l == null || string.IsNullOrEmpty(l.key))) context.Error("뺄 목록에 플레이어 데이터를 고르지 않은 줄이 있습니다.");
            if (emptyWeight < 0) context.Error("꽝 무게는 0 이상이어야 합니다.");
            if (entries.Count > 0 && entries.Where(e => e != null).Sum(e => Math.Max(0, e.weight)) <= 0) context.Error("무게가 모두 0이라 아무것도 나오지 않습니다.");
        }
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
        [FieldPath(EmptyLabel = "값 전체"), FieldLabel("필드(목록)"), ShowIf(nameof(source), WeightEntrySource.List), Tooltip("요소가 ID 글자거나 id가 있는 객체인 목록")] public string listPath = "";
        [FieldLabel("무게")] public double weight = 1;
        [FieldLabel("무게를 밸런스 값에서"), DataRef(typeof(BalanceData)), Tooltip("고르면 무게 = 이 밸런스 값(곡선)의 단계 값. 천장·운·레벨에 따라 바뀌는 확률")] public string weightBalance = "";
        [PlayerDataRef, FieldPathRoot(FieldPathRootKind.PlayerDataKey), FieldLabel("단계(플레이어 데이터)"), ShowIf(nameof(weightBalance)), Tooltip("곡선의 단계로 쓸 숫자. 비우면 기준값")] public string weightStepKey = "";
        [FieldPath(EmptyLabel = "값 전체"), FieldLabel("단계 필드"), ShowIf(nameof(weightBalance))] public string weightStepPath = "";
        [FieldLabel("최소 개수")] public int minCount = 1;
        [FieldLabel("최대 개수")] public int maxCount = 1;
        [FieldLabel("조건(맞을 때만 후보)"), SerializeReference] public Condition condition;
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
            var options = game.Roll(table, Math.Max(1, count), unique).Select(id => new ChoiceCandidate(id, Texts.NameOf(id)));
            var user = context.User != null && GameApi.HasId(context.User.GetType()) ? GameApi.IdOf(context.User) : "";
            var next = new ChoiceEffectsRun
            {
                owner = context.Owner ?? "", user = user, ids = true, key = key ?? "", path = path ?? "",
                then = (List<Outcome>)DataModel.Clone(then.Where(o => o != null).ToList(), null),
            };
            game.Session.Choices.Ask(prompt, options, next, min, max, source: context.Owner);
        }

        public override string Describe(INameResolver names)
        {
            var amount = min == max ? $"{min}개" : $"{min}~{max}개";
            var into = string.IsNullOrEmpty(key) ? "" : $" {(string.IsNullOrEmpty(path) ? key : key + " › " + path)}에 넣기";
            return $"{table}에서 {count}개 보여 {amount} 고르기{into}";
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
