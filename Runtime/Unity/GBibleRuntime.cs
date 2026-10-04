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

        // 세션 생성 직전: 옵션 조정(되돌리기 방식, 시드, 저장 버전 변환 등).
        public static event Action<SessionOptions> Configuring;
        // 세션 준비 완료: 행동 처리기·디버그 명령 등록.
        public static event Action<GameSession> Started;
        public static event Action<GameSession> Stopped;

        public static string SavePath => Path.Combine(Application.persistentDataPath, "MomoiProject", "profile.json");
        // 진행 중인 판(런) 저장 파일. 이어하기용.
        public static string RunSavePath => Path.Combine(Application.persistentDataPath, "MomoiProject", "run.json");
        public static bool HasSavedRun => File.Exists(RunSavePath);
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
            _autoSaveRun = null;
            _runSaveMode = RunSaveMode.OnQuit;
            _runSaver = null;
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

            // Mirror 비교 코드를 모은다(리플렉션은 게임이 켜질 때 여기서 한 번만).
            Mirror.Warning -= LogMirrorWarning;
            Mirror.Warning += LogMirrorWarning;
            Mirror.Initialize();
            // 행동의 참조 필드(id가 있는 객체)도 여기서 한 번 정해 둔다.
            ActionReferences.PrepareAll();
            // [BindTo] 코드 결과도 여기서 한 번 모은다(형식이 맞지 않는 함수는 알린다).
            foreach (var problem in BoundOutcomes.Problems) Debug.LogWarning("[MomoiProject] " + problem);

            // 예전 방식 데이터의 Modify 누락 검사는 값 전체를 비교하므로 개발 빌드에서만 켠다.
            var options = new SessionOptions { Seed = (ulong)DateTime.UtcNow.Ticks, DetectUntrackedChanges = Debug.isDebugBuild };
            // DB › 시작 파일 › Bootstrap에서 정한 옵션이 먼저 들어가고, 코드(Configuring)가 바꾸면 그 값이 우선한다.
            var database = GameDatabase.Load();
            database?.ApplyTo(options, Debug.isDebugBuild);
            // DB › 저장 변환(버전 순서, 같은 버전은 목록 순서). 코드(Configuring)가 그 뒤에 더 등록할 수 있다.
            if (database != null)
                foreach (var migration in database.All<SaveMigrationData>().OrderBy(m => m.version)) migration.Register(options.Migrations);
            var fromDatabase = (options.History, options.HistoryLimit, options.SnapshotInterval, options.SnapshotLimit, options.Seed);
            Configuring?.Invoke(options);
            var session = new GameSession(options);
            // 개발 명령(모니터 › 명령)은 개발 빌드에서만 등록된다. 출시 빌드에서는 Register가 아무것도 하지 않는다.
            session.Debug.Enabled = Debug.isDebugBuild;
            if (database != null) NoteCodeOverrides(session, database, fromDatabase, options);

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
                // 필드 규칙의 '필수': 저장소 값에서 비면 경고하고 채워질 때까지 모니터에 남긴다.
                foreach (var rule in database.All<FieldRuleData>())
                    if (rule.required && rule.TargetType != null) session.Data.Require(rule.TargetType, rule.field);
                // 저장 호환: 지금 데이터 구조를 저장 파일에 남기고, 개발 중이면 호환되지 않는 저장을 지운다.
                session.SaveSchema = BuildSchema(database);
                if (!database.IsReleased) ResetIncompatibleSaves(session);
                // ID 이름이 바뀐 항목은 예전 이름으로 저장된 값을 이어받는다(출시 후에만 이력이 쌓인다).
                foreach (var rename in database.Renames)
                {
                    if (rename.dataType == typeof(StatData).FullName) session.Data.AddAlias(PlayerDataKeys.Stat(rename.oldId), PlayerDataKeys.Stat(rename.newId));
                    else if (rename.dataType == typeof(AchievementData).FullName) session.Data.AddAlias(PlayerDataKeys.Achievement(rename.oldId), PlayerDataKeys.Achievement(rename.newId));
                    else if (rename.dataType == typeof(PlayerDataEntry).FullName) session.Data.AddAlias(rename.oldId, rename.newId);
                }
                // 출시 뒤 DB 항목 ID를 바꿨으면 저장 안의 그 ID([DataRef] 필드)를 불러올 때 새 ID로 바꾼다.
                options.Migrations.EveryLoad = SavedIdRenames.Build(database);
                LoadProfile(session);
                // DB › 상태 효과(Game.ApplyStatus·효과 '상태 효과 걸기').
                foreach (var status in database.All<StatusData>()) session.Statuses.Define(status.ToDefinition());
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

            // 판 저장(DB › 시작 파일 › Bootstrap): 행동마다 저장·판 끝 이벤트.
            _runSaveMode = database?.RunSave ?? RunSaveMode.OnQuit;
            var endEvent = FindEvent(session, database?.RunEndEvent, "판 끝 이벤트");
            var undoBoundary = FindEvent(session, database?.UndoBoundaryEvent, "Undo 끊는 이벤트");
            if (undoBoundary != null) UndoBoundary.Watch(session, undoBoundary);
            _runSaver = new RunSaver(session, _runSaveMode, endEvent, database?.SaveProfileEachAction ?? false);

            session.Journal.Appended += ForwardToConsole;
            session.Debug.Register("저장", "프로필 저장", () => SaveProfile(session));
            session.Debug.Register("저장", "프로필 삭제", DeleteProfile, "저장 파일을 지웁니다. 다음 플레이부터 적용됩니다.");
            session.Debug.Register("저장", "판 저장", () => SaveRun(session), "진행 중인 판(세션 구역·난수·시간)을 저장합니다.");
            session.Debug.Register("저장", "판 불러오기", () => LoadRun(session), "저장한 판을 이어서 진행합니다.");
            session.Debug.Register("저장", "새 판", () => NewRun(session), "이번 판 내용을 처음 값으로 되돌리고 판 저장을 지웁니다.");
            session.Debug.Register("시간", "일시정지 / 재개", () => GameTime.TogglePause("개발 도구"));
            session.Debug.Register("시간", "1프레임 진행", () => GameTime.StepFrame(), "일시정지 중에만 동작합니다.");
            session.Debug.Register("시간", "배속 0.25", () => GameTime.SetSpeed(0.25f));
            session.Debug.Register("시간", "배속 1", () => GameTime.SetSpeed(1f));
            session.Debug.Register("시간", "배속 2", () => GameTime.SetSpeed(2f));

            Session = session;
            // 숨기지 않는다: 하이어라키의 DontDestroyOnLoad 아래에서 보이는 편이 상태 확인에 낫다.
            var host = new GameObject("[MomoiProject]");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.AddComponent<GBibleHost>();

            Started?.Invoke(session);
            if (database != null && database.ContinueOnStart) ContinueOrStartRun(session, database);
            return session;
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
            var type = string.IsNullOrEmpty(database.NewRunAction) ? null : TypeResolver.Find(database.NewRunAction);
            if (type == null || type.IsAbstract || !typeof(GameAction).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) == null)
            {
                session.Journal.Append(JournalKind.Warning, "Runtime", $"시작할 때 이어 하기: 새 판 행동 '{database.NewRunAction}'을(를) 만들 수 없습니다(매개변수 없는 생성자가 있는 행동 클래스를 고르세요).");
                return;
            }
            try
            {
                session.Do((GameAction)Activator.CreateInstance(type));
            }
            catch (Exception e)
            {
                session.Journal.Append(JournalKind.Error, "Runtime", $"시작할 때 이어 하기: 새 판 행동이 실패했습니다 — {e.Message}");
            }
        }

        internal static void Stop()
        {
            if (Session == null) return;
            SaveProfile(Session);
            if (AutoSaveRun && !(_runSaver?.Ended ?? false)) SaveRun(Session);
            Stopped?.Invoke(Session);
            Session = null;
            _runSaver = null;
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
                Directory.CreateDirectory(Path.GetDirectoryName(SavePath));
                File.WriteAllText(SavePath, session.ExportMeta());
                session.Journal.Append(JournalKind.System, "Save", "프로필 저장: " + SavePath);
            }
            catch (Exception e)
            {
                session.Journal.Append(JournalKind.Error, "Save", "프로필 저장 실패: " + e.Message);
            }
        }

        static void LoadProfile(GameSession session)
        {
            if (!File.Exists(SavePath)) return;
            try
            {
                session.ImportMeta(File.ReadAllText(SavePath));
            }
            catch (Exception e)
            {
                session.Journal.Append(JournalKind.Error, "Save", "프로필 불러오기 실패: " + e.Message);
            }
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
            foreach (var path in new[] { SavePath, RunSavePath })
            {
                if (!File.Exists(path)) continue;
                string reason;
                try
                {
                    if (GameSession.IsSaveCompatible(File.ReadAllText(path), session.SaveSchema, out reason)) continue;
                }
                catch (Exception e)
                {
                    reason = e.Message;
                }
                DeleteProfile();
                DeleteRun();
                var message = $"개발 중 데이터 구조가 바뀌어 저장을 초기화했습니다: {reason}. (출시 후에는 초기화하지 않고 이어받습니다)";
                session.Journal.Append(JournalKind.Warning, "Save", message);
                Debug.LogWarning("[MomoiProject] " + message);
                return;
            }
        }

        public static void SaveRun(GameSession session)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(RunSavePath));
                File.WriteAllText(RunSavePath, session.ExportRun());
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
            if (!HasSavedRun) return false;
            try
            {
                session.ImportRun(File.ReadAllText(RunSavePath));
                if (session == Session) _runSaver?.Reset();
                return true;
            }
            catch (Exception e)
            {
                session.Journal.Append(JournalKind.Error, "Save", "판 불러오기 실패: " + e.Message);
                return false;
            }
        }

        internal static void FrameEndSave() => _runSaver?.FrameEnd();

        // 새 판: 이번 판 값을 처음 값으로 되돌리고(Undo 기록도 비움) 판 저장 파일(run.json)을 지운다. 프로필은 그대로다.
        // 메모리와 파일을 함께 지워야 "화면은 새 판인데 이어 하기는 예전 판"이 생기지 않는다. 행동 밖(버튼 등)에서 부른다.
        public static void NewRun(GameSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (session.Data.InTransaction) throw new InvalidOperationException("[MomoiProject] 행동 실행 중에는 새 판을 시작할 수 없습니다. 행동 밖(버튼 등)에서 부르세요.");
            session.NewRun();
            DeleteRun();
            if (session == Session) _runSaver?.Reset();
        }

        public static void DeleteRun()
        {
            if (File.Exists(RunSavePath)) File.Delete(RunSavePath);
        }

        static void DeleteProfile()
        {
            if (File.Exists(SavePath)) File.Delete(SavePath);
        }
    }

    [AddComponentMenu("")]
    sealed class GBibleHost : MonoBehaviour
    {
        void LateUpdate()
        {
            var session = GBibleRuntime.Session;
            // 일시정지 중(시간이 흐르지 않음)에는 시계를 멈추고, Sync와 조건 평가만 한다.
            if (session != null)
            {
                if (Time.deltaTime > 0f) session.EndFrame(Time.deltaTime);
                else if (!session.Data.InTransaction)
                {
                    session.Data.Sync();
                    session.Achievements.Flush();
                }
                GBibleRuntime.FrameEndSave();
                GameView.FrameEnd();
            }
            GameTime.OnFrameEnd();
        }

        void OnApplicationQuit() => GBibleRuntime.Stop();
    }
}
