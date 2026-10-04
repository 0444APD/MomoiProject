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
        [SerializeField] int historyLimit = 200;
        [SerializeField] double snapshotInterval = 1.0;
        [SerializeField] int snapshotLimit = 30;
        [SerializeField] bool fixSeedInDevelopment;
        [SerializeField] long developmentSeed = 12345;
        // 판 저장(이어 하기): 언제 저장할지, 어떤 이벤트를 받으면 판이 끝난 것으로 볼지(그때 판 저장을 지우고 프로필을 바로 저장).
        [SerializeField] RunSaveMode runSave = RunSaveMode.OnQuit;
        [SerializeField] string runEndEvent = "";
        // 'N초마다 저장'의 간격(초), '이벤트를 받을 때 저장'의 이벤트 형식 이름.
        [SerializeField] float runSaveInterval = 30f;
        [SerializeField] string runSaveEvent = "";
        // 프로필도 바깥 행동이 끝날 때마다 저장할지(수집형·방치형처럼 진행이 프로필에 있는 게임. 끄면 끌 때·판 끝에만).
        [SerializeField] bool saveProfileEachAction;
        // 선택: 이 이벤트를 받은 행동이 끝나면 Undo 기록을 비운다(예: 턴 끝 — 지난 턴으로는 되돌릴 수 없다).
        [SerializeField] string undoBoundaryEvent = "";
        // 선택: 게임이 켜질 때 저장된 판이 있으면 이어 하고, 없으면 새 판 행동을 실행한다(타이틀 화면이 없는 게임).
        [SerializeField] bool continueOnStart;
        [SerializeField] string newRunAction = "";
        // 고정 틱(실시간): 초당 틱 수. 0이면 프레임마다.
        [SerializeField] int tickRate;
        // 씬 흐름(선택): 이 플레이어 데이터 필드가 가리키는 씬을 연다(GameScenes.Follow). 키를 비우면 쓰지 않는다.
        [SerializeField] string sceneKey = "";
        [SerializeField] string scenePath = "";
        [SerializeField] string emptyScene = "";
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
        public float RunSaveInterval => runSaveInterval;
        public string RunSaveEvent => runSaveEvent;
        public bool SaveProfileEachAction => saveProfileEachAction;
        public string UndoBoundaryEvent => undoBoundaryEvent;
        public bool ContinueOnStart => continueOnStart;
        public int TickRate => tickRate;
        public string SceneKey => sceneKey;
        public string ScenePath => scenePath;
        public string EmptyScene => emptyScene;
        public string NewRunAction => newRunAction;
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
            options.TickRate = Math.Max(0, tickRate);
        }

#if UNITY_EDITOR
        public void EditorSetEntries(List<GameData> list) => entries = list;

        public void EditorSetReleased(bool value) => released = value;

        public void EditorSetSaveOptions(RunSaveMode mode, string endEvent, bool profileEachAction)
        {
            runSave = mode;
            runEndEvent = endEvent ?? "";
            saveProfileEachAction = profileEachAction;
        }

        public void EditorSetSaveTiming(float interval, string saveEvent)
        {
            runSaveInterval = Math.Max(1f, interval);
            runSaveEvent = saveEvent ?? "";
        }

        public void EditorSetFlowOptions(string undoBoundary, bool continueAtStart, string newRun)
        {
            undoBoundaryEvent = undoBoundary ?? "";
            continueOnStart = continueAtStart;
            newRunAction = newRun ?? "";
        }

        public void EditorSetTickRate(int value) => tickRate = Math.Max(0, value);

        public void EditorSetSceneFlow(string key, string path, string empty)
        {
            sceneKey = key ?? "";
            scenePath = path ?? "";
            emptyScene = empty ?? "";
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
        // N초마다(판 데이터가 바뀌었을 때만), 그리고 끌 때. 실시간·방치형.
        Interval,
        // 정한 이벤트를 받은 행동이 끝나면(하루 끝·체크포인트·방 클리어), 그리고 끌 때.
        OnEvent,
    }

    [Serializable]
    public sealed class IdRename
    {
        public string dataType;
        public string oldId;
        public string newId;
    }
}
