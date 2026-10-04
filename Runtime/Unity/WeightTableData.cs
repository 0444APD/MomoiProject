using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GBible.Data
{
    // DB › 가중치 표: 드롭·보상·조우처럼 무게만큼의 확률로 뽑는 표. 코드는 Game.Roll(GameIds.WeightTable.X), DB는 결과 '가중치 표에서 주기'.
    [Database("가중치 표", Group = "DB", Order = 45, IdPrefix = "table")]
    [CreateAssetMenu(menuName = "MomoiProject/가중치 표", fileName = "NewWeightTable")]
    public sealed class WeightTableData : GameData
    {
        [FieldLabel("항목")] public List<WeightTableEntry> entries = new List<WeightTableEntry>();
        [FieldLabel("꽝 무게"), Tooltip("이 무게만큼 아무것도 나오지 않습니다(0이면 꽝 없음)")] public double emptyWeight;

        public WeightTableDefinition ToDefinition()
        {
            var definition = new WeightTableDefinition { Id = Id, EmptyWeight = Math.Max(0, emptyWeight) };
            foreach (var entry in entries.Where(e => e != null && !string.IsNullOrEmpty(e.item)))
                definition.Entries.Add(new WeightEntry { item = entry.item, weight = entry.weight, minCount = entry.minCount, maxCount = entry.maxCount, condition = entry.condition });
            return definition;
        }

        public override void ValidateData(IValidationContext context)
        {
            if (entries.Count == 0) context.Error("항목이 없습니다.");
            foreach (var entry in entries)
            {
                if (entry == null) continue;
                if (string.IsNullOrEmpty(entry.item)) context.Error("항목을 고르지 않은 줄이 있습니다.");
                else if (entry.item == Id) context.Error("표가 자기 자신을 항목으로 가지고 있습니다.");
                if (entry.weight < 0) context.Error($"'{entry.item}': 무게는 0 이상이어야 합니다.");
                if (entry.minCount < 0 || entry.maxCount < entry.minCount) context.Error($"'{entry.item}': 개수는 0 ≤ 최소 ≤ 최대여야 합니다.");
            }
            if (emptyWeight < 0) context.Error("꽝 무게는 0 이상이어야 합니다.");
            if (entries.Count > 0 && entries.Where(e => e != null).Sum(e => Math.Max(0, e.weight)) <= 0) context.Error("무게가 모두 0이라 아무것도 나오지 않습니다.");
        }
    }

    [Serializable]
    public sealed class WeightTableEntry
    {
        [FieldLabel("항목"), DataRef(typeof(GameData)), Tooltip("DB 항목(인스턴스·다른 가중치 표 …). 다른 가중치 표면 그 표에서 다시 뽑는다")] public string item = "";
        [FieldLabel("무게")] public double weight = 1;
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
            new PlayerDataReward { key = key, path = path, items = rolled }.Grant(context.Session);
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
}
