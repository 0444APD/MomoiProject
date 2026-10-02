using System;
using System.Collections.Generic;
using UnityEngine;

namespace GBible.Data
{
    // 런타임이 읽는 데이터 목록. 에디터가 GameData 에셋 변화를 감지해 자동으로 갱신한다(직접 편집하지 않음).
    [HideInDatabase]
    public sealed class GameDatabase : ScriptableObject
    {
        public const string ResourcesPath = "MomoiProject/GameDatabase";

        [SerializeField] List<GameData> entries = new List<GameData>();
        // ID 이름 변경 이력. 예전 이름으로 저장된 값을 새 이름으로 불러오는 데 쓴다(런타임이 자동 적용).
        [SerializeField] List<IdRename> renames = new List<IdRename>();
        // 출시 여부. 개발 중(false)에는 데이터 구조가 호환되지 않게 바뀌면 저장을 변환하지 않고 지운다.
        // 출시 후(true)에는 ID 이름 변경 이력과 버전 변환으로 기존 저장을 이어받는다.
        [SerializeField] bool released;

        public IReadOnlyList<GameData> Entries => entries;
        public IReadOnlyList<IdRename> Renames => renames;
        public bool IsReleased => released;

        public IEnumerable<T> All<T>() where T : GameData
        {
            foreach (var entry in entries)
                if (entry is T typed) yield return typed;
        }

        public static GameDatabase Load() => Resources.Load<GameDatabase>(ResourcesPath);

#if UNITY_EDITOR
        public void EditorSetEntries(List<GameData> list) => entries = list;

        public void EditorSetReleased(bool value) => released = value;

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
