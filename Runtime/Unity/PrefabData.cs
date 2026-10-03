using UnityEngine;

namespace GBible.Data
{
    // 프리팹: DB에 등록한 프리팹. DB에서 컴포넌트 필드를 탐색기로 보고 고친다(프리팹 에셋에 바로 저장, Undo 가능).
    // DB에서는 지우지 않는다. 프로젝트 창에서 프리팹 에셋을 지우면 이 항목도 자동으로 정리된다.
    [Database("프리팹", Group = "DB", Order = 51, IdPrefix = "prefab")]
    [CreateAssetMenu(menuName = "MomoiProject/프리팹", fileName = "NewPrefab")]
    public sealed class PrefabData : GameData
    {
        [FieldLabel("프리팹")] public GameObject prefab;
        // 연결한 프리팹 에셋의 GUID(프리팹이 지워졌는지 알아내는 데 쓴다).
        [SerializeField, HideInInspector] string prefabGuid = "";

        public string PrefabGuid => prefabGuid;

#if UNITY_EDITOR
        void OnValidate()
        {
            if (prefab == null) return;
            var guid = UnityEditor.AssetDatabase.AssetPathToGUID(UnityEditor.AssetDatabase.GetAssetPath(prefab));
            if (!string.IsNullOrEmpty(guid)) prefabGuid = guid;
        }
#endif

        public override void ValidateData(IValidationContext context)
        {
            if (prefab == null) context.Error("프리팹이 연결되지 않았습니다.");
        }
    }
}
