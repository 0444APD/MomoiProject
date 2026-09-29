using System;
using System.Linq;
using System.IO;
using GameSystems.Data;
using UnityEngine;

namespace GameSystems
{
    // 플레이 시작 시 세션을 만들고, 데이터베이스를 읽어 수치·도전과제를 정의하고, 매 프레임 세션을 진행시킨다.
    // Session은 싱글 게임용 기본 인스턴스다. 멀티플레이·테스트는 GameSession을 직접 만든다.
    public static class GameSystemsRuntime
    {
        public static GameSession Session { get; private set; }

        // 세션 생성 직전: 옵션 조정(되돌리기 방식, 시드, 저장 버전 변환 등).
        public static event Action<SessionOptions> Configuring;
        // 세션 준비 완료: 행동 처리기·디버그 명령 등록.
        public static event Action<GameSession> Started;
        public static event Action<GameSession> Stopped;

        public static string SavePath => Path.Combine(Application.persistentDataPath, "GameSystems", "profile.json");
        // 진행 중인 판(런) 저장 파일. 이어하기용.
        public static string RunSavePath => Path.Combine(Application.persistentDataPath, "GameSystems", "run.json");
        public static bool HasSavedRun => File.Exists(RunSavePath);
        // 종료할 때 진행 중인 판을 자동으로 저장할지(불러오기는 게임이 LoadRun으로 직접 한다: 타이틀의 '이어하기' 등).
        public static bool AutoSaveRun = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            // 도메인 리로드를 끈 설정(Enter Play Mode Options)에서도 이전 플레이의 상태가 남지 않게 한다.
            Session = null;
            Configuring = null;
            Started = null;
            Stopped = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void AutoStart() => Start();

        public static GameSession Start()
        {
            if (Session != null) return Session;

            // Modify 누락 검사는 값 전체를 비교하므로 개발 빌드에서만 켠다.
            var options = new SessionOptions { Seed = (ulong)DateTime.UtcNow.Ticks, DetectUntrackedChanges = Debug.isDebugBuild };
            Configuring?.Invoke(options);
            var session = new GameSession(options);

            var database = GameDatabase.Load();
            // 데이터 값 안의 에셋 참조는 데이터베이스 ID로 저장·복원한다.
            DataModel.ReferenceToId = value => value is GameData data && !string.IsNullOrEmpty(data.Id) ? data.Id : null;
            DataModel.IdToReference = (id, type) => database == null ? null : database.Entries.FirstOrDefault(e => e != null && e.Id == id && type.IsInstanceOfType(e));
            if (database == null)
            {
                session.Journal.Append(JournalKind.Warning, "Runtime", "GameDatabase를 찾을 수 없습니다. 에디터에서 데이터베이스 창을 한 번 열면 자동으로 만들어집니다.");
            }
            else
            {
                foreach (var key in database.All<StateKeyData>())
                {
                    try { key.DefineIn(session.State); }
                    catch (Exception e) { session.Journal.Append(JournalKind.Error, "Runtime", e.Message.Replace("[GameSystems] ", "")); }
                }
                foreach (var stat in database.All<StatData>()) session.Stats.Define(stat.ToDefinition());
                // 필드 규칙의 '필수': 저장소 값에서 비면 경고하고 채워질 때까지 모니터에 남긴다.
                foreach (var rule in database.All<FieldRuleData>())
                    if (rule.required && rule.TargetType != null) session.State.Require(rule.TargetType, rule.field);
                // 저장 호환: 지금 데이터 구조를 저장 파일에 남기고, 개발 중이면 호환되지 않는 저장을 지운다.
                session.SaveSchema = BuildSchema(database);
                if (!database.IsReleased) ResetIncompatibleSaves(session);
                // ID 이름이 바뀐 항목은 예전 이름으로 저장된 값을 이어받는다(출시 후에만 이력이 쌓인다).
                foreach (var rename in database.Renames)
                {
                    if (rename.dataType == typeof(StatData).FullName) session.State.AddAlias(StateKeys.Stat(rename.oldId), StateKeys.Stat(rename.newId));
                    else if (rename.dataType == typeof(AchievementData).FullName) session.State.AddAlias(StateKeys.Achievement(rename.oldId), StateKeys.Achievement(rename.newId));
                    else if (rename.dataType == typeof(StateKeyData).FullName) session.State.AddAlias(rename.oldId, rename.newId);
                }
                LoadProfile(session);
                foreach (var achievement in database.All<AchievementData>()) session.Achievements.Define(achievement.ToDefinition());
                session.Achievements.EvaluateAll();
            }

            session.Journal.Appended += ForwardToConsole;
            session.Debug.Register("저장", "프로필 저장", () => SaveProfile(session));
            session.Debug.Register("저장", "프로필 삭제", DeleteProfile, "저장 파일을 지웁니다. 다음 플레이부터 적용됩니다.");
            session.Debug.Register("저장", "판 저장", () => SaveRun(session), "진행 중인 판(세션 구역·난수·시간)을 저장합니다.");
            session.Debug.Register("저장", "판 불러오기", () => LoadRun(session), "저장한 판을 이어서 진행합니다.");
            session.Debug.Register("저장", "새 판", () => { session.NewRun(); DeleteRun(); }, "이번 판 내용을 처음 값으로 되돌리고 판 저장을 지웁니다.");
            session.Debug.Register("시간", "일시정지 / 재개", () => GameTime.TogglePause("개발 도구"));
            session.Debug.Register("시간", "1프레임 진행", () => GameTime.StepFrame(), "일시정지 중에만 동작합니다.");
            session.Debug.Register("시간", "배속 0.25", () => GameTime.SetSpeed(0.25f));
            session.Debug.Register("시간", "배속 1", () => GameTime.SetSpeed(1f));
            session.Debug.Register("시간", "배속 2", () => GameTime.SetSpeed(2f));

            Session = session;
            // 숨기지 않는다: 하이어라키의 DontDestroyOnLoad 아래에서 보이는 편이 상태 확인에 낫다.
            var host = new GameObject("[GameSystems]");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.AddComponent<GameSystemsHost>();

            Started?.Invoke(session);
            return session;
        }

        internal static void Stop()
        {
            if (Session == null) return;
            SaveProfile(Session);
            if (AutoSaveRun) SaveRun(Session);
            Stopped?.Invoke(Session);
            Session = null;
        }

        static void ForwardToConsole(JournalEntry entry)
        {
            if (entry.Kind == JournalKind.Error) Debug.LogError($"[GameSystems] {entry.Message}");
            else if (entry.Kind == JournalKind.Warning) Debug.LogWarning($"[GameSystems] {entry.Message}");
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
            foreach (var key in database.All<StateKeyData>())
            {
                var type = key.ValueType;
                if (type != null) DataModel.Schema(type, key.Id, schema);
            }
            foreach (var stat in database.All<StatData>()) schema[StateKeys.Stat(stat.Id)] = nameof(Double);
            foreach (var achievement in database.All<AchievementData>()) schema[StateKeys.Achievement(achievement.Id)] = nameof(Boolean);
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
                Debug.LogWarning("[GameSystems] " + message);
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
                return true;
            }
            catch (Exception e)
            {
                session.Journal.Append(JournalKind.Error, "Save", "판 불러오기 실패: " + e.Message);
                return false;
            }
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
    sealed class GameSystemsHost : MonoBehaviour
    {
        void LateUpdate()
        {
            var session = GameSystemsRuntime.Session;
            // 일시정지 중(시간이 흐르지 않음)에는 시계를 멈추고, 조건 평가만 한다.
            if (session != null)
            {
                if (Time.deltaTime > 0f) session.EndFrame(Time.deltaTime);
                else if (!session.State.InTransaction) session.Achievements.Flush();
            }
            GameTime.OnFrameEnd();
        }

        void OnApplicationQuit() => GameSystemsRuntime.Stop();
    }
}
