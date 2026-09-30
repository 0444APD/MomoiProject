using System.Collections.Generic;
using System.Linq;
using GameSystems.Dialogue;
using UnityEngine;

namespace GameSystems.Data
{
    // 연출 프리셋: 효과 노드 묶음. Game Systems › 연출 프리셋 창에서 노드로 만들고, 대화 그래프에서는 노드 하나로 쓴다.
    [Database("연출 프리셋", Group = "대화", Order = 2, IdPrefix = "preset", Hidden = true)]
    public sealed class EffectPresetData : GameData
    {
        [HideInInspector] public NodeGraph graph = new NodeGraph();

        // 밖으로 내보내는 신호 이름(대화 그래프의 프리셋 노드에 점으로 생긴다).
        public IEnumerable<string> Outputs => graph.OfType<PresetOutputNode>().Select(o => o.signal).Where(s => !string.IsNullOrEmpty(s)).Distinct();

        public override void ValidateData(IValidationContext context)
        {
            if (!graph.OfType<PresetEntryNode>().Any()) context.Error("'프리셋 시작' 노드가 없습니다.");
        }
    }
}
