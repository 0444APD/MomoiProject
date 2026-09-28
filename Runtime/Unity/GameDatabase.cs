using System.Collections.Generic;
using UnityEngine;

namespace GameSystems.Data
{
    // 런타임이 읽는 데이터 목록. 에디터가 GameData 에셋 변화를 감지해 자동으로 갱신한다(직접 편집하지 않음).
    [HideInDatabase]
    public sealed class GameDatabase : ScriptableObject
    {
        public const string ResourcesPath = "GameSystems/GameDatabase";

        [SerializeField] List<GameData> entries = new List<GameData>();

        public IReadOnlyList<GameData> Entries => entries;

        public IEnumerable<T> All<T>() where T : GameData
        {
            foreach (var entry in entries)
                if (entry is T typed) yield return typed;
        }

        public static GameDatabase Load() => Resources.Load<GameDatabase>(ResourcesPath);

#if UNITY_EDITOR
        public void EditorSetEntries(List<GameData> list) => entries = list;
#endif
    }
}
