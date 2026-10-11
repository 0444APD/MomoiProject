using System;
using System.Linq;
using System.IO;
using GBible.Data;
using UnityEngine;

namespace GBible
{
    // 플레이 시작 시 세션을 만들고, 데이터베이스를 읽어 수치·도전과제를 정의하고, 매 프레임 세션을 진행시킨다.
    // Session은 싱글 게임용 기본 인스턴스다. 멀티플레이·테스트는 GameSession을 직접 만든다.
    public static class GBibleRuntime
    {
        public static GameSession Session { get; private set; }
        // 클라이언트(씬) 코드의 창구: GBibleRuntime.Game.All<Unit>() 등. 서버·시뮬레이션은 세션마다 session.Game을 쓴다.
        public static GameApi Game => Session?.Game;

        // 화면 없는 실행(Dedicated Server 빌드 · -batchmode로 켠 빌드): 화면(UI)을 만들지 않는다. 에디터는 화면이 있는 것으로 본다.
        public static bool IsHeadless
        {
            get
            {
#if UNITY_SERVER && !UNITY_EDITOR
                return true;
#else
                return Application.isBatchMode && !Application.isEditor;
#endif
            }
        }

        // 세션 생성 직전: 옵션 조정(되돌리기 방식, 시드, 저장 버전 변환 등).
        public static event Action<SessionOptions> Configuring;
        // 세션 준비 완료: 행동 처리기·디버그 명령 등록.
        public static event Action<GameSession> Started;
        public static event Action<GameSession> Stopped;
        // 판을 저장하기 직전(화면 쪽 상태 — 위치 등 — 를 판 데이터에 담을 마지막 기회. 행동 밖이므로 담을 때는 Game.Do).
        // 이 안에서 한 행동은 '저장할 것 있음'으로 세지 않는다(저장이 되풀이되지 않게).
        public static event Action<GameSession> Saving;
        // Saving을 부르는 동안 참.
        internal static bool IsSaving { get; private set; }
        // 저장한 판을 불러온 뒤(이어 하기). 화면 쪽 상태를 판 데이터에서 다시 놓을 때.
        public static event Action<GameSession> RunLoaded;
        // 끄기 직전(저장하기 전).
        public static event Action<GameSession> Stopping;
        // 게임을 끌 때: 멀티를 닫고 판·프로필을 저장한 뒤(외부 연동을 마지막에 닫는 곳 — Steam 통계 올리기·SteamAPI 종료).
        public static event Action Quitting;

        // 지금 슬롯의 프로필 저장 파일.
        public static string SavePath => Path.Combine(SlotFolder(Slot), "profile.json");
        // 진행 중인 판(런) 저장 파일. 이어하기용.
        public static string RunSavePath => Path.Combine(SlotFolder(Slot), "run.json");
        // 이어 할 판이 있는가(비어 있는 파일은 없는 것으로).
        public static bool HasSavedRun => HasRun(RunSavePath);

        static bool HasRun(string path) => NonEmpty(path) || (!File.Exists(path) && NonEmpty(SafeFile.Backup(path)));

        static bool NonEmpty(string path) => File.Exists(path) && new FileInfo(path).Length > 0;

        // ── 슬롯 ───────────────────────────────────────────────
        // 저장 묶음(프로필 + 진행 중인 판) 하나. 슬롯마다 폴더가 따로다: MomoiProject/slots/<이름>/. 이름이 ""(기본)이면 MomoiProject/ 바로 아래.
        // 설정(GameSettings)은 슬롯과 상관없이 하나다. 마지막으로 쓴 슬롯을 기억해 다음 실행 때 그 슬롯으로 시작한다.
        //   타이틀: foreach (var name in new[] { "1", "2", "3" }) { var info = GBibleRuntime.SlotInfoOf(name); … }  → GBibleRuntime.UseSlot("2");
        // UseSlot은 지금 슬롯을 저장하고 세션을 새로 시작한다(Started가 다시 온다). 타이틀처럼 게임 화면이 없을 때 부른다.
        public static string Slot => _slot ?? "";

        // 슬롯 목록·요약에 남길 한 줄(예: "3층 · 전사"). 저장할 때마다 slot.json에 같이 쓴다.
        public static Func<GameSession, string> SlotSummary;

        // 지금 슬롯의 누적 플레이 시간(초, 실제 시간). 저장할 때 slot.json에 남는다.
        public static double PlayTime => _playTime;

        const string LastSlotKey = "momoi.slot";
        static string _slot;
        static double _playTime;
        static GameObject _host;

        public static string SlotFolder(string slot) =>
            string.IsNullOrEmpty(slot)
                ? Path.Combine(Application.persistentDataPath, "MomoiProject")
                : Path.Combine(Application.persistentDataPath, "MomoiProject", "slots", CheckSlotName(slot).ToLowerInvariant());

        // 슬롯 이름은 대소문자를 가리지 않는다 — 폴더는 소문자로(대소문자를 가리는 파일 시스템(리눅스·맥)에서도 "A"와 "a"가 같은 슬롯).
        static string CheckSlotName(string slot)
        {
            if (!IsValidSlotName(slot))
                throw new ArgumentException($"[MomoiProject] 슬롯 이름 '{slot}'에는 글자·숫자·-·_만 쓸 수 있습니다(CON·NUL·COM1 같은 장치 이름은 안 됩니다).");
            return slot;
        }

        static readonly string[] DeviceNames = { "con", "prn", "aux", "nul", "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9", "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9" };

        static bool IsValidSlotName(string slot) =>
            slot != null && slot.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_') && !DeviceNames.Contains(slot.ToLowerInvariant());

        // remember: 거짓이면 다음 실행의 시작 슬롯으로 기억하지 않는다(테스트 씬의 'test' 슬롯).
        public static void UseSlot(string slot, bool remember = true)
        {
            slot ??= "";
            CheckSlotName(slot);
            if (Session != null && SameSlot(slot, Slot)) return;
            // 저장도 없고 행동도 한 번 없었던 세션(타이틀에서 막 켠 기본 슬롯)은 빈 저장을 남기지 않는다.
            if (Session != null) Stop(save: Session.Actions.Log.Count > 0 || File.Exists(SavePath) || File.Exists(RunSavePath));
            _slot = slot;
            if (remember) GameSettings.SetRaw(LastSlotKey, slot);
            Start();
        }

        // 슬롯 이름은 대소문자를 가리지 않는다(Windows에서 "A"와 "a"는 같은 폴더).
        static bool SameSlot(string a, string b) => string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);

        // 판을 파일이 아닌 곳(테스트 상황)에서 불러왔을 때: 판 저장 주기를 다시 세고 RunLoaded를 쏜다(화면이 판 데이터로 다시 맞춘다).
        public static void NotifyRunLoaded(GameSession session)
        {
            if (session == null) return;
            if (session == Session) _runSaver?.Reset();
            InvokeEach(RunLoaded, session);
        }

        public static SlotInfo SlotInfoOf(string slot)
        {
            var folder = SlotFolder(slot ?? "");
            var info = new SlotInfo
            {
                Name = slot ?? "",
                // 바꿔 넣는 사이에 꺼져 바로 전 저장(.bak)만 남은 것도 저장이 있는 것(불러오면 .bak으로 이어 간다).
                HasProfile = SafeFile.Exists(Path.Combine(folder, "profile.json")),
                HasRun = HasRun(Path.Combine(folder, "run.json")),
            };
            var meta = Path.Combine(folder, "slot.json");
            if (!File.Exists(meta)) return info;
            try
            {
                if (MiniJson.Deserialize(File.ReadAllText(meta)) is System.Collections.Generic.Dictionary<string, object> root)
                {
                    if (root.TryGetValue("savedAt", out var saved) && saved is string text && DateTime.TryParse(text, null, System.Globalization.DateTimeStyles.RoundtripKind, out var time)) info.SavedAt = time;
                    if (root.TryGetValue("playTime", out var play) && play is double seconds) info.PlayTime = seconds;
                    if (root.TryGetValue("summary", out var summary)) info.Summary = summary as string ?? "";
                }
            }
            catch (Exception)
            {
                // 요약이 깨졌어도 저장 자체는 쓸 수 있다.
            }
            return info;
        }

        // 저장이 있는 슬롯들(기본 슬롯 제외, 이름순).
        public static System.Collections.Generic.IReadOnlyList<SlotInfo> Slots()
        {
            var root = Path.Combine(Application.persistentDataPath, "MomoiProject", "slots");
            if (!Directory.Exists(root)) return Array.Empty<SlotInfo>();
            // 플레이어가 탐색기에서 만든 폴더("1 - 복사본" 등)는 슬롯이 아니다 — 건너뛴다(목록 전체가 실패하지 않게).
            return Directory.GetDirectories(root).Select(Path.GetFileName).Where(IsValidSlotName).OrderBy(n => n, StringComparer.Ordinal)
                .Select(SlotInfoOf).Where(i => !i.IsEmpty).ToList();
        }

        // 슬롯을 지운다. 지금 쓰는 슬롯이면 저장하지 않고 세션을 새로 시작한다(빈 프로필·새 판, Started가 다시 온다).
        public static void DeleteSlot(string slot)
        {
            slot ??= "";
            var current = Session != null && SameSlot(slot, Slot);
            if (current) Stop(save: false);
            var folder = SlotFolder(slot);
            // 바로 전 저장(.bak)·쓰다 만 파일(.tmp)도 함께(지운 슬롯이 되살아나지 않게).
            foreach (var file in new[] { "profile.json", "run.json", "slot.json" }) SafeFile.Delete(Path.Combine(folder, file));
            if (current) Start();
        }

        // 슬롯을 통째로 복사한다(덮어씀). 지금 슬롯을 복사하면 먼저 저장한다.
        public static void CopySlot(string from, string to)
        {
            from ??= "";
            to ??= "";
            if (SameSlot(from, to)) return;
            // 지금 쓰는 슬롯에 덮어쓰면 실행 중인 세션이 저장·끌 때 다시 덮는다.
            if (Session != null && SameSlot(to, Slot))
                throw new InvalidOperationException($"[MomoiProject] 지금 쓰는 슬롯 '{to}'(으)로는 복사할 수 없습니다. 다른 슬롯으로 바꾼 뒤(UseSlot) 복사하세요.");
            if (Session != null && SameSlot(from, Slot))
            {
                _runSaver?.FlushEnd();
                if (RunIsLive) SaveRun(Session);
                else SaveProfile(Session);
            }
            var source = SlotFolder(from);
            var target = SlotFolder(to);
            Directory.CreateDirectory(target);
            foreach (var file in new[] { "profile.json", "run.json", "slot.json" })
            {
                var path = Path.Combine(source, file);
                var copy = Path.Combine(target, file);
                // 바로 전 저장(.bak)까지 같이 옮긴다 — 대상의 옛 .bak이 남으면 원본에 없는 저장이 그 .bak에서 되살아난다.
                SafeFile.Delete(copy);
                if (File.Exists(path)) File.Copy(path, copy, true);
                if (File.Exists(SafeFile.Backup(path))) File.Copy(SafeFile.Backup(path), SafeFile.Backup(copy), true);
            }
        }

        internal static void AddPlayTime(double seconds) => _playTime += seconds;

        static void WriteSlotInfo(GameSession session)
        {
            string summary = null;
            try { summary = SlotSummary?.Invoke(session); }
            catch (Exception e) { session.Journal.Append(JournalKind.Warning, "Save", "슬롯 요약(SlotSummary)이 실패했습니다: " + e.Message); }
            var root = new System.Collections.Generic.Dictionary<string, object>
            {
                ["savedAt"] = DateTime.Now.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                ["playTime"] = _playTime,
                ["summary"] = summary ?? "",
            };
            SafeFile.WriteAllText(Path.Combine(SlotFolder(Slot), "slot.json"), MiniJson.Serialize(root));
        }
        // 종료할 때 진행 중인 판을 자동으로 저장할지(불러오기는 게임이 LoadRun으로 직접 한다: 타이틀의 '이어하기' 등).
        // 기본은 DB › 시작 파일 › Bootstrap의 '판 저장'(저장하지 않음이면 거짓). 코드에서 값을 넣으면 그 값이 우선한다.
        public static bool AutoSaveRun
        {
            get => _autoSaveRun ?? _runSaveMode != RunSaveMode.Never;
            set => _autoSaveRun = value;
        }

        static bool? _autoSaveRun;
        static RunSaveMode _runSaveMode = RunSaveMode.OnQuit;
        static RunSaver _runSaver;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            // 도메인 리로드를 끈 설정(Enter Play Mode Options)에서도 이전 플레이의 상태가 남지 않게 한다.
            Session = null;
            Configuring = null;
            Started = null;
            Stopped = null;
            Saving = null;
            RunLoaded = null;
            Stopping = null;
            IsSaving = false;
            _autoSaveRun = null;
            _runSaveMode = RunSaveMode.OnQuit;
            _runSaver = null;
            _slot = null;
            _playTime = 0;
            _host = null;
            SlotSummary = null;
            StartAsClient = null;
            Quitting = null;
            _preparedWarnings = false;
            Mirror.ResetWarnings();
            GameView.Reset();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void AutoStart() => Start();

        static void LogMirrorWarning(string message) => Debug.LogWarning("[MomoiProject] " + message);

        // 인스턴스 값의 사용 기능(Usable 필드, 붙어 있을 때). 게임이 켜질 때만 부른다(필드 목록은 형식마다 한 번 찾아 둔다).
        static readonly System.Collections.Generic.Dictionary<Type, System.Reflection.FieldInfo> UsableFields = new System.Collections.Generic.Dictionary<Type, System.Reflection.FieldInfo>();

        static Usable UsableOf(object value)
        {
            if (value == null) return null;
            var type = value.GetType();
            if (!UsableFields.TryGetValue(type, out var field))
            {
                for (var t = type; t != null && field == null; t = t.BaseType)
                    field = t.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly)
                        .FirstOrDefault(f => f.FieldType == typeof(Usable));
                UsableFields[type] = field;
            }
            return field?.GetValue(value) as Usable;
        }

        public static GameSession Start()
        {
            if (Session != null) return Session;

            // 설정(슬롯과 상관없이 하나)을 먼저 읽고, 마지막으로 쓴 슬롯으로 시작한다.
            GameSettings.Load();
            _slot ??= GameSettings.Raw(LastSlotKey) ?? "";
            try { CheckSlotName(_slot); }
            catch (ArgumentException) { _slot = ""; }
            _playTime = SlotInfoOf(_slot).PlayTime;

            PrepareStatics();
            var database = GameDatabase.Load();
            // 멀티 클라이언트로 다시 시작하는 중이면(GameNet.Join) 판 저장·시작할 때 이어 하기를 하지 않는다 — 판은 서버가 가진다.
            var asClient = StartAsClient;
            StartAsClient = null;
            var session = BuildSession(database, main: true);

            // 판 저장(DB › 시작 파일 › Bootstrap): 행동마다 저장·판 끝 이벤트.
            _runSaveMode = database?.RunSave ?? RunSaveMode.OnQuit;
            var endEvent = FindEvent(session, database?.RunEndEvent, "판 끝 이벤트");
            var undoBoundary = FindEvent(session, database?.UndoBoundaryEvent, "Undo 끊는 이벤트");
            if (undoBoundary != null) UndoBoundary.Watch(session, undoBoundary);
            var saveEvent = _runSaveMode == RunSaveMode.OnEvent ? FindEvent(session, database?.RunSaveEvent, "판 저장 이벤트") : null;
            _runSaver = asClient == null ? new RunSaver(session, _runSaveMode, endEvent, database?.SaveProfileEachAction ?? false, database?.RunSaveInterval ?? 30f, saveEvent) : null;

            session.Debug.Register("저장", "프로필 저장", () => SaveProfile(session));
            session.Debug.Register("저장", "프로필 삭제", DeleteProfile, "저장 파일을 지웁니다. 다음 플레이부터 적용됩니다.");
            session.Debug.Register("저장", "판 저장", () => SaveRun(session), "진행 중인 판(세션 구역·난수·시간)을 저장합니다.");
            session.Debug.Register("저장", "판 불러오기", () => LoadRun(session), "저장한 판을 이어서 진행합니다.");
            session.Debug.Register("저장", "새 판", () => StartNewRun(session), "이번 판 내용을 처음 값으로 되돌리고(판 저장을 지움) DB › Bootstrap의 '새 판 행동'을 돌립니다.");
            session.Debug.Register("시간", "일시정지 / 재개", () => GameTime.TogglePause("개발 도구"));
            session.Debug.Register("시간", "1프레임 진행", () => GameTime.StepFrame(), "일시정지 중에만 동작합니다.");
            session.Debug.Register("시간", "배속 0.25", () => GameTime.SetSpeed(0.25f));
            session.Debug.Register("시간", "배속 1", () => GameTime.SetSpeed(1f));
            session.Debug.Register("시간", "배속 2", () => GameTime.SetSpeed(2f));

            Session = session;
            // 숨기지 않는다: 하이어라키의 DontDestroyOnLoad 아래에서 보이는 편이 상태 확인에 낫다. 슬롯을 바꿔 다시 시작해도 하나만 둔다.
            if (_host == null)
            {
                _host = new GameObject("[MomoiProject]");
                UnityEngine.Object.DontDestroyOnLoad(_host);
                _host.AddComponent<GBibleHost>();
            }

            // DB › 시작 파일 › Bootstrap의 씬 흐름(정해 두었으면).
            if (database != null && !string.IsNullOrEmpty(database.SceneKey)) GameScenes.Follow(database.SceneKey, database.ScenePath, database.EmptyScene);
            // DB › 화면·화면 설정(UI Toolkit). 슬롯을 바꿔 다시 시작해도 한 번만 준비한다. 화면 없는 실행(서버 빌드)은 만들지 않는다.
            if (!IsHeadless) GameScreens.Initialize(database);
            asClient?.Invoke(session);
            InvokeEach(Started, session);
            if (asClient == null && database != null && database.ContinueOnStart) ContinueOrStartRun(session, database);
            return session;
        }

        // 게임이 켜질 때 한 번(여러 번 불러도 같다): Mirror 비교 코드·행동 참조 필드·[BindTo]를 모은다(리플렉션은 여기서만).
        static bool _preparedWarnings;

        static void PrepareStatics()
        {
            // 게임 코드만 훑는다: 패키지·프로젝트의 테스트 어셈블리(테스트용 행동·[BindTo])는 플레이에 섞지 않는다(경고가 나지 않게).
            GameAssemblies.Use(skipTests: true);
            // Mirror 비교 코드를 모은다(리플렉션은 게임이 켜질 때 여기서 한 번만).
            Mirror.Warning -= LogMirrorWarning;
            Mirror.Warning += LogMirrorWarning;
            Mirror.Initialize();
            // 행동의 참조 필드(id가 있는 객체)도 여기서 한 번 정해 둔다.
            ActionReferences.PrepareAll();
            // [BindTo] 코드 결과도 여기서 한 번 모은다(형식이 맞지 않는 함수는 알린다 — 세션을 더 만들어도 한 번만).
            if (_preparedWarnings) return;
            _preparedWarnings = true;
            foreach (var problem in BoundOutcomes.Problems) Debug.LogWarning("[MomoiProject] " + problem);
        }

        // DB로 세션 하나를 만든다(옵션·플레이어 데이터·정의·인스턴스·필수 규칙·개발 명령).
        // main = 화면이 쓰는 세션: 프로필을 불러오고, 개발 중이면 호환되지 않는 저장을 지운다.
        static GameSession BuildSession(GameDatabase database, bool main)
        {
            // 예전 방식 데이터의 Modify 누락 검사는 값 전체를 비교하므로 개발 빌드에서만 켠다.
            var options = new SessionOptions { Seed = (ulong)DateTime.UtcNow.Ticks, DetectUntrackedChanges = Debug.isDebugBuild };
            // DB › 시작 파일 › Bootstrap에서 정한 옵션이 먼저 들어가고, 코드(Configuring)가 바꾸면 그 값이 우선한다.
            database?.ApplyTo(options, Debug.isDebugBuild);
            // DB › 저장 변환(버전 순서, 같은 버전은 ID 글자 순서 — DB 목록 차례). 코드(Configuring)가 그 뒤에 더 등록할 수 있다.
            if (database != null)
                foreach (var migration in database.All<SaveMigrationData>().OrderBy(m => m.version)) migration.Register(options.Migrations);
            var fromDatabase = (options.History, options.HistoryLimit, options.SnapshotInterval, options.SnapshotLimit, options.Seed);
            InvokeEach(Configuring, options);
            var session = new GameSession(options);
            // 오류·경고를 콘솔에도 — 프로필을 불러올 때(저장 변환·읽기 실패)의 것도 보이게 불러오기 전에 붙인다.
            if (main) session.Journal.Appended += ForwardToConsole;
            // 개발 명령(모니터 › 명령)은 개발 빌드에서만 등록된다. 출시 빌드에서는 Register가 아무것도 하지 않는다.
            session.Debug.Enabled = Debug.isDebugBuild;
            if (database != null && main) NoteCodeOverrides(session, database, fromDatabase, options);

            // 데이터 값 안의 에셋 참조는 데이터베이스 ID로 저장·복원한다.
            DataModel.ReferenceToId = value => value is GameData data && !string.IsNullOrEmpty(data.Id) ? data.Id : null;
            DataModel.IdToReference = (id, type) => database == null ? null : database.Entries.FirstOrDefault(e => e != null && e.Id == id && type.IsInstanceOfType(e));
            if (database == null)
            {
                session.Journal.Append(JournalKind.Warning, "Runtime", "GameDatabase를 찾을 수 없습니다. 에디터에서 DB 창을 한 번 열면 자동으로 만들어집니다.");
            }
            else
            {
                foreach (var key in database.All<PlayerDataEntry>())
                {
                    try { key.DefineIn(session.Data); }
                    catch (Exception e) { session.Journal.Append(JournalKind.Error, "Runtime", e.Message.Replace("[MomoiProject] ", "")); }
                }
                foreach (var stat in database.All<StatData>()) session.Stats.Define(stat.ToDefinition());
                // 인스턴스 원본: ID → 값(게임이 켜질 때 한 번 표를 만든다 — 찾을 때마다 훑지 않게).
                var instances = new System.Collections.Generic.Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var instance in database.All<InstanceData>())
                    if (!string.IsNullOrEmpty(instance.Id) && !instances.ContainsKey(instance.Id)) instances.Add(instance.Id, instance.value);
                session.Instances = id => id != null && instances.TryGetValue(id, out var found) ? found : null;
                // DB 항목: ID → 항목(Game.Entry<T> — 규칙이 프로젝트 DB 종류의 칸을 읽는다). 같은 ID가 둘이면 먼저 것.
                var entries = new System.Collections.Generic.Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var entry in database.Entries)
                    if (entry != null && !string.IsNullOrEmpty(entry.Id) && !entries.ContainsKey(entry.Id)) entries.Add(entry.Id, entry);
                session.Entries = id => id != null && entries.TryGetValue(id, out var found) ? found : null;
                var list = database.Entries.Where(e => e != null).Cast<object>().ToList();
                session.EntriesOfType = type => list.Where(type.IsInstanceOfType);
                // 필드 규칙의 '필수': 저장소 값에서 비면 경고하고 채워질 때까지 모니터에 남긴다.
                foreach (var rule in database.All<FieldRuleData>())
                    if (rule.required && rule.TargetType != null) session.Data.Require(rule.TargetType, rule.field);
                // 저장 호환: 지금 데이터 구조를 저장 파일에 남기고, 개발 중이면 호환되지 않는 저장을 지운다.
                session.SaveSchema = BuildSchema(database);
                if (main && !database.IsReleased) ResetIncompatibleSaves(session);
                // ID 이름이 바뀐 항목은 예전 이름으로 저장된 값을 이어받는다(출시 후에만 이력이 쌓인다).
                foreach (var rename in database.Renames)
                {
                    if (rename.dataType == typeof(StatData).FullName) session.Data.AddAlias(PlayerDataKeys.Stat(rename.oldId), PlayerDataKeys.Stat(rename.newId));
                    else if (rename.dataType == typeof(AchievementData).FullName) session.Data.AddAlias(PlayerDataKeys.Achievement(rename.oldId), PlayerDataKeys.Achievement(rename.newId));
                    else if (rename.dataType == typeof(PlayerDataEntry).FullName) session.Data.AddAlias(rename.oldId, rename.newId);
                }
                // 출시 뒤 DB 항목 ID를 바꿨으면 저장 안의 그 ID([DataRef] 필드)를 불러올 때 새 ID로 바꾼다.
                options.Migrations.EveryLoad = SavedIdRenames.Build(database);
                if (main) LoadProfile(session);
                // 선택 요소: 프로젝트가 템플릿에서 들인 상태 효과·패시브 DB 종류(규격 IStatusData·IPassiveData). 없으면 아무것도 등록하지 않는다.
                foreach (var status in database.Entries.OfType<IStatusData>()) session.Statuses.Define(status.ToDefinition());
                foreach (var passive in database.Entries.OfType<IPassiveData>()) session.Passives.Define(passive.ToDefinition());
                // DB › 밸런스 값(Game.Balance)·가중치 표(Game.Roll).
                foreach (var balance in database.All<BalanceData>()) balance.Define(session.Balance);
                foreach (var table in database.All<WeightTableData>()) session.Tables.Define(table.ToDefinition());
                // Game.Use로 쓰는 것: 능력(IUsableData)과,사용 기능(Usable 필드)이 붙은 인스턴스(아이템 등, ID = 인스턴스 ID).
                foreach (var usable in database.Entries.OfType<IUsableData>())
                    if (usable.ToUsable() is AbilityDefinition definition) session.Abilities.Define(definition);
                foreach (var instance in database.All<InstanceData>())
                    if (UsableOf(instance.value) is Usable use) session.Abilities.Define(use.ToDefinition(instance.Id));
                foreach (var achievement in database.All<AchievementData>()) session.Achievements.Define(achievement.ToDefinition());
                // DB › 개발 명령 → 모니터 › 명령 탭 버튼(개발 빌드에서만 등록된다).
                foreach (var command in database.All<DebugCommandData>()) command.Register(session);
                // [BindTo] ID가 DB의 어느 항목(도전과제·로그·능력·인스턴스·프로젝트 고유 기능 …)에도 없으면 알린다(오타·지운 항목).
                var entryIds = new System.Collections.Generic.HashSet<string>(database.Entries.Where(e => e != null && !string.IsNullOrEmpty(e.Id)).Select(e => e.Id), StringComparer.Ordinal);
                foreach (var id in BoundOutcomes.Ids)
                    if (!entryIds.Contains(id))
                        Debug.LogWarning($"[MomoiProject] [BindTo(\"{id}\")]: 이 ID의 DB 항목이 없어 연결되지 않았습니다({string.Join(", ", BoundOutcomes.For(id).Select(c => c.Name))}).");
                session.Achievements.EvaluateAll();
            }

            return session;
        }

        // 화면에 묶이지 않은 세션을 하나 더 만든다(멀티 시험 모드의 서버·추가 클라이언트, 서버 빌드). 프로필은 불러오지 않는다.
        public static GameSession CreateSession()
        {
            PrepareStatics();
            return BuildSession(GameDatabase.Load(), main: false);
        }

        // 다음 Start를 멀티 클라이언트로 한다(GameNet.Join이 정한다): 판 저장·시작할 때 이어 하기를 하지 않고, Started 전에 이 함수에 세션을 넘긴다.
        internal static Action<GameSession> StartAsClient;

        // 지금 세션을 멈추고(프로필 저장) 멀티 클라이언트 세션으로 다시 시작한다(GameNet.Join). setup이 Started 전에 세션을 받는다.
        internal static GameSession RestartAsClient(Action<GameSession> setup)
        {
            if (Session != null) Stop(save: true);
            StartAsClient = setup;
            return Start();
        }

        // 멀티를 끝내고 혼자 하기 세션으로 다시 시작한다(GameNet.Leave).
        internal static GameSession RestartLocal()
        {
            if (Session != null) Stop(save: true);
            return Start();
        }

        // DB 설정의 이벤트 형식 이름 → 형식(없으면 기록에 경고).
        static Type FindEvent(GameSession session, string typeName, string label)
        {
            if (string.IsNullOrEmpty(typeName)) return null;
            var type = TypeResolver.Find(typeName);
            if (type == null) session.Journal.Append(JournalKind.Warning, "Runtime", $"{label} '{typeName}'를 찾을 수 없습니다(이벤트 클래스 이름이 바뀌었는지 확인).");
            return type;
        }

        // DB › Bootstrap의 '시작할 때 이어 하기': 저장된 판이 있으면 이어 하고, 없으면 새 판 행동을 실행한다.
        // 형식 이름 → 객체 만들기는 게임이 켜질 때 한 번뿐이다(실행 중 리플렉션 아님).
        static void ContinueOrStartRun(GameSession session, GameDatabase database)
        {
            if (LoadRun(session))
            {
                session.Journal.Append(JournalKind.System, "Runtime", "시작할 때 이어 하기: 저장된 판을 불러왔습니다.");
                return;
            }
            RunNewRunAction(session, database);
        }

        // DB › 시작 파일 › Bootstrap의 '새 판 행동'(있으면)을 그 세션에서 돌린다. 형식 이름 → 객체 만들기는 새 판마다 한 번뿐이다(실행 중 리플렉션 아님).
        internal static void RunNewRunAction(GameSession session, GameDatabase database = null)
        {
            database ??= GameDatabase.Load();
            if (session == null || database == null || string.IsNullOrEmpty(database.NewRunAction)) return;
            var type = TypeResolver.Find(database.NewRunAction);
            if (type == null || type.IsAbstract || !typeof(GameAction).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) == null)
            {
                session.Journal.Append(JournalKind.Warning, "Runtime", $"새 판 행동 '{database.NewRunAction}'을(를) 만들 수 없습니다(매개변수 없는 생성자가 있는 행동 클래스를 고르세요).");
                return;
            }
            try
            {
                session.Do((GameAction)Activator.CreateInstance(type));
            }
            catch (Exception e)
            {
                session.Journal.Append(JournalKind.Error, "Runtime", $"새 판 행동이 실패했습니다 — {e.Message}");
            }
        }

        // 새 판 = 처음 값으로 되돌리기(NewRun) + DB › Bootstrap의 '새 판 행동'. 테스트 패널·개발 명령·시험 모드 서버가 같은 뜻으로 쓴다.
        public static void StartNewRun(GameSession session, ulong? seed = null)
        {
            NewRun(session, seed);
            RunNewRunAction(session);
        }

        internal static void Stop() => Stop(save: true);

        static void Stop(bool save)
        {
            if (Session == null) return;
            InvokeEach(Stopping, Session);
            if (save)
            {
                // 판 끝과 같은 프레임에 끄면: 판 끝 마무리(판 저장 지우기·프로필 저장)를 먼저.
                _runSaver?.FlushEnd();
                // 살아 있는 판만 저장한다(프로필도 함께). 멀티 클라이언트·리플레이 세션(판 저장기가 없음)의 판은 서버·녹화의 것이라 남기지 않는다.
                if (AutoSaveRun && RunIsLive) SaveRun(Session);
                else SaveProfile(Session);
            }
            // 받는 쪽 하나가 던져도 나머지는 받고, 세션은 끝까지 비운다(UseSlot·DeleteSlot이 중간에 끊기지 않게).
            try { InvokeEach(Stopped, Session); }
            finally
            {
                Session = null;
                _runSaver = null;
            }
        }

        // 코드(Configuring)가 DB의 세션 옵션을 바꿨으면 기록에 남긴다(에디터 값이 왜 안 먹는지 바로 보이게).
        static void NoteCodeOverrides(GameSession session, GameDatabase database, (HistoryMode history, int limit, double interval, int snapshots, ulong seed) before, SessionOptions after)
        {
            var changed = new System.Collections.Generic.List<string>();
            if (before.history != after.History) changed.Add($"Undo 방식 {before.history} → {after.History}");
            if (before.limit != after.HistoryLimit) changed.Add($"Undo 보관 수 {before.limit} → {after.HistoryLimit}");
            if (before.interval != after.SnapshotInterval) changed.Add($"스냅샷 간격 {before.interval} → {after.SnapshotInterval}");
            if (before.snapshots != after.SnapshotLimit) changed.Add($"스냅샷 보관 수 {before.snapshots} → {after.SnapshotLimit}");
            if (database.FixSeedInDevelopment && Debug.isDebugBuild && before.seed != after.Seed) changed.Add($"시드 {before.seed} → {after.Seed}");
            if (changed.Count > 0)
                session.Journal.Append(JournalKind.System, "Runtime", "코드(Configuring)가 DB › Bootstrap의 세션 옵션을 바꿨습니다: " + string.Join(", ", changed));
        }

        static void ForwardToConsole(JournalEntry entry)
        {
            if (entry.Kind == JournalKind.Error) Debug.LogError($"[MomoiProject] {entry.Message}");
            else if (entry.Kind == JournalKind.Warning) Debug.LogWarning($"[MomoiProject] {entry.Message}");
        }

        public static void SaveProfile(GameSession session)
        {
            try
            {
                SafeFile.WriteAllText(SavePath, session.ExportMeta());
                WriteSlotInfo(session);
                session.Journal.Append(JournalKind.System, "Save", "프로필 저장: " + SavePath);
            }
            catch (Exception e)
            {
                session.Journal.Append(JournalKind.Error, "Save", "프로필 저장 실패: " + e.Message);
            }
        }

        static void LoadProfile(GameSession session)
        {
            if (!SafeFile.Exists(SavePath)) return;
            ImportSave(session, SavePath, "프로필", text => session.ImportMeta(text));
        }

        // 저장 파일 하나를 읽어 넣는다. 비었거나(쓰는 도중 꺼짐·디스크 가득) 읽지 못하면 원본을 옆에 남기고 바로 전 저장(.bak)으로. 넣었으면 참.
        static bool ImportSave(GameSession session, string path, string what, Action<string> import)
        {
            string problem;
            try
            {
                var text = SafeFile.ReadAllText(path);
                if (!string.IsNullOrEmpty(text))
                {
                    import(text);
                    return true;
                }
                problem = text == null ? "파일이 없습니다" : "파일이 비어 있습니다";
            }
            catch (Exception e)
            {
                problem = e.Message;
            }
            var kept = File.Exists(path) ? KeepBroken(path) : "(원본 없음)";
            var backup = SafeFile.Backup(path);
            if (File.Exists(backup))
            {
                try
                {
                    var text = SafeFile.ReadAllText(backup);
                    if (!string.IsNullOrEmpty(text))
                    {
                        import(text);
                        session.Journal.Append(JournalKind.Warning, "Save", $"{what} 불러오기 실패: {problem} — 바로 전 저장(.bak)으로 이어 갑니다. 원본은 {kept}에 남겨 둡니다.");
                        return true;
                    }
                }
                catch (Exception e)
                {
                    problem += " / .bak도 읽지 못함: " + e.Message;
                }
            }
            session.Journal.Append(JournalKind.Error, "Save", $"{what} 불러오기 실패: {problem} — 원본은 {kept}에 남겨 둡니다.");
            return false;
        }

        // 데이터베이스가 정의한 저장 대상(플레이어 데이터·수치·도전과제)의 구조.
        static System.Collections.Generic.Dictionary<string, string> BuildSchema(GameDatabase database)
        {
            var schema = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var key in database.All<PlayerDataEntry>())
            {
                var type = key.ValueType;
                if (type != null) DataModel.Schema(type, key.Id, schema);
            }
            foreach (var stat in database.All<StatData>()) schema[PlayerDataKeys.Stat(stat.Id)] = nameof(Double);
            foreach (var achievement in database.All<AchievementData>()) schema[PlayerDataKeys.Achievement(achievement.Id)] = nameof(Boolean);
            return schema;
        }

        // 개발 중: 저장된 키·필드가 없어졌거나 이름·형식이 바뀌었으면 변환하지 않고 저장을 지운다(추가만 된 경우는 그대로 이어감).
        static void ResetIncompatibleSaves(GameSession session)
        {
            foreach (var main in new[] { SavePath, RunSavePath })
            {
                // 바로 전 저장(.bak)만 남았으면 그것을 본다(불러오기가 그것으로 이어 가므로).
                var path = File.Exists(main) ? main : File.Exists(SafeFile.Backup(main)) ? SafeFile.Backup(main) : null;
                if (path == null) continue;
                string reason;
                try
                {
                    // 빈 파일(쓰는 도중 꺼짐)은 구조 문제가 아니다 — 불러올 때 바로 전 저장(.bak)으로 되돌린다.
                    var text = SafeFile.ReadAllText(path);
                    if (string.IsNullOrEmpty(text) || GameSession.IsSaveCompatible(text, session.SaveSchema, out reason)) continue;
                }
                catch (Exception e)
                {
                    reason = e.Message;
                }
                // 맞지 않는 그 파일만 옆으로 옮긴다(지우지 않는다 — 판 파일 하나 때문에 멀쩡한 프로필까지 잃지 않게).
                // 그 파일의 바로 전 저장(.bak)도 — 남기면 불러오기가 .bak으로 이어 가 초기화가 되지 않는다.
                var kept = File.Exists(main) ? MoveAside(main, "reset") : "";
                if (File.Exists(SafeFile.Backup(main))) kept += (kept.Length > 0 ? ", " : "") + MoveAside(SafeFile.Backup(main), "reset");
                var message = $"개발 중 데이터 구조가 바뀌어 {Path.GetFileName(main)}을(를) 초기화했습니다: {reason}. 원래 파일은 {kept}. (출시 후에는 초기화하지 않고 이어받습니다)";
                session.Journal.Append(JournalKind.Warning, "Save", message);
                Debug.LogWarning("[MomoiProject] " + message);
            }
        }

        // 판을 저장한다: 저장 직전 알림(Saving) → 프로필 → 판. 프로필도 함께 쓰는 까닭: 판만 앞서 저장되면, 꺼진 뒤 이어 할 때
        // 판은 지금 모습인데 프로필은 예전 모습이 된다(판에서 쓴 메타 재화가 되살아나는 등). Saving에서 바꾼 메타 값도 이번 프로필에 들어간다.
        public static void SaveRun(GameSession session)
        {
            NotifySaving(session);
            SaveProfile(session);
            WriteRun(session);
        }

        // 저장 직전 알림. 이 안에서 담은 행동은 이번 저장에 들어간다(RunSaver가 따로 저장을 잡지 않는다).
        internal static void NotifySaving(GameSession session)
        {
            IsSaving = true;
            try { InvokeEach(Saving, session); }
            finally { IsSaving = false; }
        }

        // 판 파일(run.json)만 쓴다 — 부르는 쪽이 Saving·프로필 차례를 맡는다(RunSaver).
        internal static void WriteRun(GameSession session)
        {
            try
            {
                SafeFile.WriteAllText(RunSavePath, session.ExportRun());
                WriteSlotInfo(session);
                session.Journal.Append(JournalKind.System, "Save", "판 저장: " + RunSavePath);
            }
            catch (Exception e)
            {
                session.Journal.Append(JournalKind.Error, "Save", "판 저장 실패: " + e.Message);
            }
        }

        // 저장한 판이 있으면 이어서 진행하고 true.
        public static bool LoadRun(GameSession session)
        {
            if (!SafeFile.Exists(RunSavePath)) return false;
            if (!ImportSave(session, RunSavePath, "판", text => session.ImportRun(text))) return false;
            if (session == Session) _runSaver?.Reset();
            InvokeEach(RunLoaded, session);
            return true;
        }

        // 읽지 못한 저장 파일을 옆에 복사해 둔다(다음 저장이 빈 값으로 덮어도 원본이 남게). 돌려주는 값: 복사본 경로.
        static string KeepBroken(string path)
        {
            try
            {
                var copy = Path.Combine(Path.GetDirectoryName(path) ?? ".", Path.GetFileNameWithoutExtension(path) + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + Path.GetExtension(path));
                File.Copy(path, copy, true);
                return copy;
            }
            catch (Exception)
            {
                return "(복사 실패)";
            }
        }

        // 파일을 옆으로 옮긴다(이름.tag-시각.확장자). 돌려주는 값: 옮긴 경로.
        static string MoveAside(string path, string tag)
        {
            try
            {
                var target = Path.Combine(Path.GetDirectoryName(path) ?? ".", Path.GetFileNameWithoutExtension(path) + "." + tag + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + Path.GetExtension(path));
                File.Move(path, target);
                return target;
            }
            catch (Exception)
            {
                return "(옮기기 실패)";
            }
        }

        // 받는 쪽을 하나씩 부른다 — 한 곳(프로젝트 코드·선택 기능)이 던져도 나머지 시작 처리(다른 받는 쪽·이어 하기)는 그대로 돈다.
        static void InvokeEach<T>(Action<T> handlers, T value)
        {
            if (handlers == null) return;
            foreach (var handler in handlers.GetInvocationList())
            {
                try { ((Action<T>)handler)(value); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        internal static void FrameEndSave() => _runSaver?.FrameEnd();

        internal static void NotifyQuitting()
        {
            try { Quitting?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        // 이 실행의 판이 살아 있다(불러왔거나 새로 시작했거나 행동이 판 데이터를 바꿨고, 끝나지 않았다). 아니면 끌 때·슬롯을 바꿀 때 판을 저장하지 않는다.
        static bool RunIsLive => _runSaver != null && _runSaver.Live && !_runSaver.Ended;

        // 새 판: 이번 판 값을 처음 값으로 되돌리고(Undo 기록도 비움) 판 저장 파일(run.json)을 지운다. 프로필은 그대로다.
        // 메모리와 파일을 함께 지워야 "화면은 새 판인데 이어 하기는 예전 판"이 생기지 않는다. 행동 밖(버튼 등)에서 부른다.
        // seed: 시드 입력·일일 도전(같은 시드면 같은 판). 일일: GBibleRuntime.NewRun(session, RandomStreams.SeedOf(날짜 글자)).
        public static void NewRun(GameSession session, ulong? seed = null)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (session.Data.InTransaction) throw new InvalidOperationException("[MomoiProject] 행동 실행 중에는 새 판을 시작할 수 없습니다. 행동 밖(버튼 등)에서 부르세요.");
            session.NewRun(seed);
            // 판 저장 파일은 화면 세션(이 슬롯)의 것만 지운다(시험 모드 서버 등 다른 세션의 새 판이 이 슬롯의 저장을 지우지 않게).
            if (session != Session) return;
            DeleteRun();
            _runSaver?.Reset();
        }

        static void DeleteProfile()
        {
            try { SafeFile.Delete(SavePath); }
            catch (Exception e) { Debug.LogWarning("[MomoiProject] 프로필 저장 파일을 지우지 못했습니다: " + e.Message); }
        }

        // 판 저장 파일을 지운다(.bak·.tmp도). 지우지 못해도(파일 잠금 등) 던지지 않는다 — 판 끝·새 판 처리가 중간에 끊기지 않게.
        public static void DeleteRun()
        {
            try { SafeFile.Delete(RunSavePath); }
            catch (Exception e) { Debug.LogWarning("[MomoiProject] 판 저장 파일을 지우지 못했습니다: " + e.Message); }
        }
    }

    [AddComponentMenu("")]
    sealed class GBibleHost : MonoBehaviour
    {
        // 연출 대기열(GameView.Present)의 코루틴을 돌리는 오브젝트.
        internal static GBibleHost Instance { get; private set; }

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // 멀티플레이: 받은 메시지를 먼저 처리한다(이 프레임의 화면·규칙이 받은 결과를 본다).
        void Update() => GameNet.Update(Time.deltaTime);

        void LateUpdate()
        {
            var session = GBibleRuntime.Session;
            // 일시정지 중(시간이 흐르지 않음)에는 시계를 멈추고, Sync와 조건 평가만 한다.
            if (session != null)
            {
                GBibleRuntime.AddPlayTime(Time.unscaledDeltaTime);
                if (Time.deltaTime > 0f) session.EndFrame(Time.deltaTime);
                else if (!session.Data.InTransaction)
                {
                    session.Data.Sync();
                    session.Achievements.Flush();
                }
                GBibleRuntime.FrameEndSave();
                GameView.FrameEnd();
                GameView.Pump();
                GameScreens.Pump();
            }
            // 멀티플레이: 이 프레임에 바뀐 곳(시간 규칙 포함)을 보낸다.
            GameNet.LateUpdate(Time.deltaTime);
            GameTime.OnFrameEnd();
        }

        // 끄는 순서를 정해 둔다: 멀티 닫기 → 저장 → 외부 연동(Steam) 닫기. 앞 단계가 실패해도 저장은 한다.
        void OnApplicationQuit()
        {
            try { GameNet.Shutdown(); }
            catch (Exception e) { Debug.LogException(e); }
            try { GBibleRuntime.Stop(); }
            catch (Exception e) { Debug.LogException(e); }
            GBibleRuntime.NotifyQuitting();
        }
    }
}
