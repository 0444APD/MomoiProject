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

        // 세션 옵션(DB › 시작 파일 › Bootstrap 화면에서 고친다). 게임이 켜질 때 SessionOptions에 먼저 들어가고, 코드의 Configuring이 바꾸면 그 값이 우선한다.
        [SerializeField] HistoryMode history = HistoryMode.Actions;
        [SerializeField] int historyLimit;
        [SerializeField] double snapshotInterval = 1.0;
        [SerializeField] int snapshotLimit = 30;
        [SerializeField] bool fixSeedInDevelopment;
        [SerializeField] long developmentSeed = 12345;
        // 판 저장(이어 하기): 언제 저장할지, 어떤 이벤트를 받으면 판이 끝난 것으로 볼지(그때 판 저장을 지우고 프로필을 바로 저장).
        [SerializeField] RunSaveMode runSave = RunSaveMode.OnQuit;
        [SerializeField] string runEndEvent = "";
        // 고급: 기록(Journal) 보관 줄 수, 개발 빌드의 Modify 누락 검사(예전 방식 데이터).
        [SerializeField] int journalCapacity = 4096;
        [SerializeField] bool detectUntrackedChanges = true;

        public IReadOnlyList<GameData> Entries => entries;
        public IReadOnlyList<IdRename> Renames => renames;
        public bool IsReleased => released;

        public IEnumerable<T> All<T>() where T : GameData
        {
            foreach (var entry in entries)
                if (entry is T typed) yield return typed;
        }

        public static GameDatabase Load() => Resources.Load<GameDatabase>(ResourcesPath);

        public HistoryMode History => history;
        public int HistoryLimit => historyLimit;
        public double SnapshotInterval => snapshotInterval;
        public int SnapshotLimit => snapshotLimit;
        public bool FixSeedInDevelopment => fixSeedInDevelopment;
        public long DevelopmentSeed => developmentSeed;
        public RunSaveMode RunSave => runSave;
        public string RunEndEvent => runEndEvent;
        public int JournalCapacity => journalCapacity;
        public bool DetectUntrackedChanges => detectUntrackedChanges;

        // 에디터에서 정한 세션 옵션을 넣는다(시드는 개발 빌드에서 고정을 켰을 때만).
        public void ApplyTo(SessionOptions options, bool developmentBuild)
        {
            options.History = history;
            options.HistoryLimit = Math.Max(0, historyLimit);
            options.SnapshotInterval = snapshotInterval > 0 ? snapshotInterval : 1.0;
            options.SnapshotLimit = Math.Max(1, snapshotLimit);
            if (developmentBuild && fixSeedInDevelopment) options.Seed = (ulong)Math.Max(0, developmentSeed);
            options.JournalCapacity = journalCapacity > 0 ? journalCapacity : 4096;
            options.DetectUntrackedChanges = developmentBuild && detectUntrackedChanges;
        }

#if UNITY_EDITOR
        public void EditorSetEntries(List<GameData> list) => entries = list;

        public void EditorSetReleased(bool value) => released = value;

        public void EditorSetSaveOptions(RunSaveMode mode, string endEvent)
        {
            runSave = mode;
            runEndEvent = endEvent ?? "";
        }

        public void EditorSetAdvanced(int journalCapacity, bool detectUntrackedChanges)
        {
            this.journalCapacity = Math.Max(64, journalCapacity);
            this.detectUntrackedChanges = detectUntrackedChanges;
        }

        public void EditorSetSessionOptions(HistoryMode history, int historyLimit, double snapshotInterval, int snapshotLimit, bool fixSeed, long seed)
        {
            this.history = history;
            this.historyLimit = Math.Max(0, historyLimit);
            this.snapshotInterval = snapshotInterval > 0 ? snapshotInterval : 1.0;
            this.snapshotLimit = Math.Max(1, snapshotLimit);
            fixSeedInDevelopment = fixSeed;
            developmentSeed = Math.Max(0, seed);
        }

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

    // 판(이어 하기) 저장 시점. DB › 시작 파일 › Bootstrap에서 고른다.
    public enum RunSaveMode
    {
        // 게임을 끌 때 진행 중인 판을 저장한다(기본).
        OnQuit,
        // 바깥 행동이 끝날 때마다(Undo 포함) 저장하고, 끌 때도 저장한다. 턴제·카드.
        AfterEachAction,
        // 저장하지 않는다. 이어 하기가 없는 게임.
        Never,
    }

    [Serializable]
    public sealed class IdRename
    {
        public string dataType;
        public string oldId;
        public string newId;
    }
}
