using System;
using System.Collections.Generic;
using UnityEngine;

namespace GBible.Dialogue
{
    // 그래프 정리용(실행에는 영향 없음): 메모와 그룹 상자.

    [Serializable, GraphNode("메모", Category = "정리", Graphs = GraphKinds.Both)]
    public sealed class NoteNode : GraphNode
    {
        [FieldLabel("메모"), TextArea(2, 12)] public string text = "";

        public override string Summary => null;

        public override IEnumerable<PortInfo> Ports
        {
            get { yield break; }
        }
    }

    // 노드 뒤에 깔리는 제목 달린 영역. 머리를 끌면 안에 든 노드도 함께 움직인다.
    [Serializable, GraphNode("그룹 상자", Category = "정리", Graphs = GraphKinds.Both, Order = 1)]
    public sealed class GroupNode : GraphNode
    {
        [FieldLabel("제목")] public string title = "그룹";
        [FieldLabel("색")] public Color color = new Color(0.55f, 0.7f, 1f, 1f);
        [HideInInspector] public Vector2 size = new Vector2(640, 420);

        public override string Summary => title;

        public override IEnumerable<PortInfo> Ports
        {
            get { yield break; }
        }
    }
}
