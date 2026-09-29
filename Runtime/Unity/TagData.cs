using UnityEngine;

namespace GameSystems.Data
{
    // 태그 하나. 태그 이름은 여기서만 짓고, 모든 데이터의 태그 칸에서는 목록에서 고른다.
    [Database("태그", Group = "식별자", Order = 1, IdPrefix = "tag")]
    [CreateAssetMenu(menuName = "Game Systems/태그", fileName = "NewTag")]
    public sealed class TagData : GameData
    {
        [FieldLabel("설명")] public string description = "";
    }
}
