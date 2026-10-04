#if !(UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX)
#define DISABLESTEAMWORKS
#endif
using System;
using System.Collections.Generic;
using System.Linq;
using GBible.Data;
using UnityEngine;
#if !DISABLESTEAMWORKS
using Steamworks;
#endif

namespace GBible
{
    // Steam(선택 기능, Steamworks.NET): 게임 코드는 Steamworks를 직접 부르지 않고 이것만 쓴다. 설정은 DB › Steam.
    //   켜기: 세션이 시작될 때(GBibleRuntime.Started) 저절로 — Steam 초기화, 매 프레임 콜백, 끌 때 정리.
    //   출시 빌드를 Steam 밖에서 켜면 Steam으로 다시 시작한다(설정). Steam이 꺼져 있으면 경고 하나 남기고 Steam 없이 돈다(IsReady 거짓).
    //   도전과제: 달성하는 순간 같은 이름의 Steam 도전과제를 달성한다(켤 때 이미 달성한 것도 올린다).
    //   로그 → Steam 통계: 연결한 로그 값이 바뀌면 모아서 2초마다 올린다.
    //   리치 프레즌스: GameSteam.SetPresence("status", "3층 · 전투 중"), 친구 목록 문장은 Steamworks의 리치 프레즌스 현지화 파일로.
    // Undo로 되돌린 값도 Steam 통계에는 그 값이 올라간다(Steam은 되돌리지 않는다). 미리보기는 올리지 않는다(변경 알림이 없다).
    public static class GameSteam
    {
        // Steam이 붙어 있다(초기화 성공). 거짓이면 모든 호출이 아무것도 하지 않는다.
        public static bool IsReady { get; private set; }

        // 초기화가 끝났을 때(성공한 경우만).
        public static event Action Ready;

        const string OverlayPause = "Steam 오버레이";
        const float StoreInterval = 2f;

        static SteamData _settings;
        static bool _tried;
        static GameSession _session;
        static readonly Dictionary<string, SteamStatLink> StatKeys = new Dictionary<string, SteamStatLink>(StringComparer.Ordinal);
        static readonly HashSet<SteamStatLink> DirtyStats = new HashSet<SteamStatLink>();
        static readonly HashSet<string> Warned = new HashSet<string>(StringComparer.Ordinal);
        static bool _storeNow;
        static float _nextStore;
#if !DISABLESTEAMWORKS
        static Callback<GameOverlayActivated_t> _overlay;
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            IsReady = false;
            Ready = null;
            _settings = null;
            _tried = false;
            _session = null;
            StatKeys.Clear();
            DirtyStats.Clear();
            Warned.Clear();
            _storeNow = false;
            _nextStore = 0;
#if !DISABLESTEAMWORKS
            _overlay = null;
#endif
        }

        // GBibleRuntime의 정적 값이 비워진 뒤(SubsystemRegistration), 세션이 시작되기 전(BeforeSceneLoad)에 건다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Hook()
        {
            GBibleRuntime.Started -= OnStarted;
            GBibleRuntime.Started += OnStarted;
        }

        // ── 게임 코드가 쓰는 것 ─────────────────────────────────

        // 친구 목록·프로필에 보일 상태. key "steam_display"는 Steamworks에 올린 현지화 토큰("#Status_Battle")을 고른다.
        public static bool SetPresence(string key, string value)
        {
#if !DISABLESTEAMWORKS
            return IsReady && SteamFriends.SetRichPresence(key, value);
#else
            return false;
#endif
        }

        public static void ClearPresence()
        {
#if !DISABLESTEAMWORKS
            if (IsReady) SteamFriends.ClearRichPresence();
#endif
        }

        // Steam 이름(없으면 빈 글자).
        public static string PlayerName
        {
            get
            {
#if !DISABLESTEAMWORKS
                return IsReady ? SteamFriends.GetPersonaName() : "";
#else
                return "";
#endif
            }
        }

        // Steam 사용자 ID(64비트, 없으면 0).
        public static ulong SteamId
        {
            get
            {
#if !DISABLESTEAMWORKS
                return IsReady ? SteamUser.GetSteamID().m_SteamID : 0;
#else
                return 0;
#endif
            }
        }

        // Steam 클라이언트의 언어를 BCP-47로(ko, en, ja, zh-Hans …). 없으면 null.
        public static string Language
        {
            get
            {
#if !DISABLESTEAMWORKS
                return IsReady ? ToBcp47(SteamApps.GetCurrentGameLanguage()) : null;
#else
                return null;
#endif
            }
        }

        // 모아 둔 통계·도전과제를 지금 올린다(보통은 저절로 2초마다·끌 때).
        public static void StoreNow() => _storeNow = true;

        // ── 켜기 ───────────────────────────────────────────────

        static void OnStarted(GameSession session)
        {
            _settings ??= GameDatabase.Load()?.All<SteamData>().FirstOrDefault();
            if (_settings == null)
            {
                WarnOnce("settings", "DB › Steam 항목이 없어 Steam을 켜지 않습니다(선택 기능 'Steam'을 켰으면 DB › Steam에 항목을 하나 만드세요).");
                return;
            }
            if (!_tried)
            {
                _tried = true;
                Init(_settings);
            }
            if (!IsReady) return;
            Bind(session, _settings);
        }

        static void Init(SteamData settings)
        {
#if !DISABLESTEAMWORKS
            try
            {
                // 출시 빌드를 Steam 밖에서 켰으면 Steam이 다시 켜 준다(이 실행은 끝낸다). 에디터·개발 빌드는 steam_appid.txt로 바로 붙는다.
                if (settings.restartThroughSteam && !Application.isEditor && !Debug.isDebugBuild && SteamAPI.RestartAppIfNecessary(new AppId_t(settings.appId)))
                {
                    Application.Quit();
                    return;
                }
            }
            catch (DllNotFoundException e)
            {
                Debug.LogError("[MomoiProject] Steam 라이브러리(steam_api)를 찾지 못했습니다. Steamworks.NET 패키지가 들어 있는지 확인하세요. " + e.Message);
                return;
            }
            if (!Packsize.Test()) Debug.LogError("[MomoiProject] Steamworks.NET의 Packsize 검사가 실패했습니다(플랫폼에 맞지 않는 Steamworks.NET).");
            if (!DllCheck.Test()) Debug.LogError("[MomoiProject] Steamworks.NET의 DllCheck 검사가 실패했습니다(steam_api 버전이 맞지 않음).");
            if (!SteamAPI.Init())
            {
                Debug.LogWarning("[MomoiProject] Steam에 붙지 못해 Steam 없이 시작합니다(Steam 클라이언트가 켜져 있는지, steam_appid.txt의 App ID가 맞는지 확인).");
                return;
            }
            IsReady = true;
            var host = new GameObject("[MomoiProject Steam]");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.AddComponent<GameSteamHost>();
            if (settings.pauseOnOverlay)
                _overlay = Callback<GameOverlayActivated_t>.Create(e =>
                {
                    if (e.m_bActive != 0) GameTime.Pause(OverlayPause);
                    else GameTime.Resume(OverlayPause);
                });
            if (settings.languageFromSteam && !GameLanguage.HasSavedChoice && !GameSettings.HasSavedLanguage && Language is string code)
                GameLanguage.Set(code, remember: false);
            Ready?.Invoke();
#endif
        }

        // 세션마다(슬롯을 바꿔 다시 시작해도) 도전과제·로그를 잇는다.
        static void Bind(GameSession session, SteamData settings)
        {
            _session = session;
            if (settings.achievements)
            {
                session.Achievements.Unlocked += definition => Unlock(settings.SteamNameOf(definition.Id));
                foreach (var definition in session.Achievements.Definitions.ToList())
                    if (session.Achievements.IsUnlocked(definition.Id)) Unlock(settings.SteamNameOf(definition.Id), onlyIfMissing: true);
            }
            StatKeys.Clear();
            DirtyStats.Clear();
            foreach (var link in settings.stats.Where(s => s != null && !string.IsNullOrEmpty(s.stat) && !string.IsNullOrWhiteSpace(s.steamName)))
            {
                if (!session.Stats.IsDefined(link.stat))
                {
                    WarnOnce("stat:" + link.stat, $"DB › Steam: 로그 '{link.stat}'가 없어 Steam 통계 '{link.steamName}'에 잇지 않습니다.");
                    continue;
                }
                StatKeys[PlayerDataKeys.Stat(link.stat)] = link;
                DirtyStats.Add(link);   // 켤 때 한 번 올린다
            }
            session.Data.Changed += key =>
            {
                if (session == _session && StatKeys.TryGetValue(key, out var link)) DirtyStats.Add(link);
            };
            if (Debug.isDebugBuild)
                session.Debug.Register("Steam", "Steam 통계·도전과제 모두 지우기", ResetAll, "시험용: 이 계정의 이 앱 Steam 통계와 도전과제를 모두 지웁니다(GBible 쪽은 그대로).");
        }

        static void Unlock(string name, bool onlyIfMissing = false)
        {
#if !DISABLESTEAMWORKS
            if (!IsReady || string.IsNullOrEmpty(name)) return;
            if (onlyIfMissing && SteamUserStats.GetAchievement(name, out var achieved) && achieved) return;
            if (!SteamUserStats.SetAchievement(name))
            {
                WarnOnce("ach:" + name, $"Steam 도전과제 '{name}'을(를) 달성하지 못했습니다(Steamworks의 API Name과 도전과제 ID가 같은지, 다르면 DB › Steam 표에 적었는지 확인).");
                return;
            }
            _storeNow = true;
#endif
        }

        static void ResetAll()
        {
#if !DISABLESTEAMWORKS
            if (!IsReady) return;
            SteamUserStats.ResetAllStats(true);
            SteamUserStats.StoreStats();
#endif
        }

        // 매 프레임(호스트): 콜백을 돌리고, 모아 둔 통계를 올린다.
        internal static void Tick()
        {
#if !DISABLESTEAMWORKS
            if (!IsReady) return;
            SteamAPI.RunCallbacks();
            if (DirtyStats.Count > 0 && Time.unscaledTime >= _nextStore) PushStats();
            if (_storeNow)
            {
                _storeNow = false;
                _nextStore = Time.unscaledTime + StoreInterval;
                SteamUserStats.StoreStats();
            }
#endif
        }

        static void PushStats()
        {
#if !DISABLESTEAMWORKS
            var session = _session;
            if (session == null) return;
            foreach (var link in DirtyStats)
            {
                double value;
                try { value = session.Stats.Get(link.stat); }
                catch (InvalidOperationException) { continue; }
                var name = link.steamName.Trim();
                var ok = link.integer ? SteamUserStats.SetStat(name, (int)Math.Round(value)) : SteamUserStats.SetStat(name, (float)value);
                if (!ok) WarnOnce("statset:" + name, $"Steam 통계 '{name}'을(를) 올리지 못했습니다(Steamworks의 API Name·형식(INT/FLOAT)이 DB › Steam과 맞는지 확인).");
            }
            DirtyStats.Clear();
            _storeNow = true;
#endif
        }

        internal static void Shutdown()
        {
#if !DISABLESTEAMWORKS
            if (!IsReady) return;
            if (DirtyStats.Count > 0) PushStats();
            SteamUserStats.StoreStats();
            SteamAPI.Shutdown();
            IsReady = false;
#endif
        }

        static void WarnOnce(string key, string message)
        {
            if (Warned.Add(key)) Debug.LogWarning("[MomoiProject] " + message);
        }

        // Steam API 언어 이름 → BCP-47(https://partner.steamgames.com/doc/store/localization/languages).
        static string ToBcp47(string steam)
        {
            switch ((steam ?? "").ToLowerInvariant())
            {
                case "english": return "en";
                case "koreana": return "ko";
                case "japanese": return "ja";
                case "schinese": return "zh-Hans";
                case "tchinese": return "zh-Hant";
                case "french": return "fr";
                case "german": return "de";
                case "spanish": return "es";
                case "latam": return "es-419";
                case "brazilian": return "pt-BR";
                case "portuguese": return "pt";
                case "russian": return "ru";
                case "italian": return "it";
                case "polish": return "pl";
                case "turkish": return "tr";
                case "thai": return "th";
                case "vietnamese": return "vi";
                case "ukrainian": return "uk";
                case "indonesian": return "id";
                case "dutch": return "nl";
                case "swedish": return "sv";
                case "danish": return "da";
                case "finnish": return "fi";
                case "norwegian": return "no";
                case "czech": return "cs";
                case "hungarian": return "hu";
                case "romanian": return "ro";
                case "greek": return "el";
                case "bulgarian": return "bg";
                case "arabic": return "ar";
                default: return null;
            }
        }
    }

    [AddComponentMenu("")]
    sealed class GameSteamHost : MonoBehaviour
    {
        void Update() => GameSteam.Tick();

        void OnApplicationQuit() => GameSteam.Shutdown();
    }
}
