using System;
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
        // ID 이름 변경 이력. 예전 이름으로 저장된 값을 새 이름으로 불러오는 데 쓴다(런타임이 자동 적용).
        [SerializeField] List<IdRename> renames = new List<IdRename>();

        public IReadOnlyList<GameData> Entries => entries;
        public IReadOnlyList<IdRename> Renames => renames;

        public IEnumerable<T> All<T>() where T : GameData
        {
            foreach (var entry in entries)
                if (entry is T typed) yield return typed;
        }

        public static GameDatabase Load() => Resources.Load<GameDatabase>(ResourcesPath);

#if UNITY_EDITOR
        public void EditorSetEntries(List<GameData> list) => entries = list;

        public void EditorAddRename(string dataType, string oldId, string newId)
        {
            // a → b 다음 b → c 이면 a도 c로 가게 이어 준다.
            foreach (var rename in renames)
                if (rename.dataType == dataType && rename.newId == oldId) rename.newId = newId;
            renames.RemoveAll(r => r.dataType == dataType && r.oldId == newId);
            renames.Add(new IdRename { dataType = dataType, oldId = oldId, newId = newId });
        }
#endif
    }

    [Serializable]
    public sealed class IdRename
    {
        public string dataType;
        public string oldId;
        public string newId;
    }
}
