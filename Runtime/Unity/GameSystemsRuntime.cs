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

        // 세션 생성 직전: 옵션 조정(자동 확정 여부, 시드 등).
        public static event Action<SessionOptions> Configuring;
        // 세션 준비 완료: 행동 처리기·디버그 명령 등록.
        public static event Action<GameSession> Started;
        public static event Action<GameSession> Stopped;

        public static string SavePath => Path.Combine(Application.persistentDataPath, "GameSystems", "profile.json");

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
                foreach (var stat in database.All<StatData>()) session.Stats.Define(stat.ToDefinition());
                LoadProfile(session);
                foreach (var achievement in database.All<AchievementData>()) session.Achievements.Define(achievement.ToDefinition());
                session.Achievements.EvaluateAll();
            }

            session.Journal.Appended += ForwardToConsole;
            session.Debug.Register("저장", "프로필 저장", () => SaveProfile(session));
            session.Debug.Register("저장", "프로필 삭제", DeleteProfile, "저장 파일을 지웁니다. 다음 플레이부터 적용됩니다.");

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

        static void DeleteProfile()
        {
            if (File.Exists(SavePath)) File.Delete(SavePath);
        }
    }

    [AddComponentMenu("")]
    sealed class GameSystemsHost : MonoBehaviour
    {
        void LateUpdate() => GameSystemsRuntime.Session?.EndFrame(Time.deltaTime);

        void OnApplicationQuit() => GameSystemsRuntime.Stop();
    }
}
