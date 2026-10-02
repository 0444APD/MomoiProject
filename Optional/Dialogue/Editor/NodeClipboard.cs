using GBible.Dialogue;
using UnityEngine;

namespace GBible.EditorTools.Graph
{
    // 노드 깊은 복사용 그릇. Unity 직렬화로 복사하므로 [SerializeReference] 안쪽(조건·보상 블록)까지 새 인스턴스가 된다.
    public sealed class NodeClipboard : ScriptableObject
    {
        [SerializeReference] public GraphNode node;

        public static GraphNode Clone(GraphNode source)
        {
            var holder = CreateInstance<NodeClipboard>();
            holder.hideFlags = HideFlags.HideAndDontSave;
            holder.node = source;
            var copy = Instantiate(holder);
            copy.hideFlags = HideFlags.HideAndDontSave;
            var result = copy.node;
            DestroyImmediate(holder);
            DestroyImmediate(copy);
            return result;
        }
    }
}
