using System.Linq;
using GBible.Dialogue;
using UnityEngine;

namespace GBible.Data
{
    // 다이얼로그 하나(노드 그래프). 편집은 MomoiProject › 다이얼로그 시스템 › 그래프 창에서 한다.
    [Database("다이얼로그", Group = "다이얼로그 시스템", Order = 1, IdPrefix = "dialogue", Hidden = true)]
    public sealed class DialogueData : GameData
    {
        [HideInInspector] public NodeGraph graph = new NodeGraph();

        public StartNode Entry(string entryId) => graph.OfType<StartNode>().FirstOrDefault(s => s.entryId == entryId);

        public override void ValidateData(IValidationContext context)
        {
            var starts = graph.OfType<StartNode>().ToList();
            if (starts.Count == 0) context.Error("시작 노드가 없습니다.");
            foreach (var duplicate in starts.GroupBy(s => s.entryId).Where(g => g.Count() > 1))
                context.Error($"시작 ID '{duplicate.Key}'가 {duplicate.Count()}번 쓰였습니다.");
            foreach (var line in graph.OfType<LineNode>().GroupBy(l => l.lineId).Where(g => g.Count() > 1))
                context.Error($"대사 키 '{line.Key}'가 겹칩니다(노드를 복사한 뒤 키가 바뀌지 않았습니다).");
        }
    }
}
