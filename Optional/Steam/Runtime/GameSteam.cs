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
    //   켜기: 세션이 시작될 때(GBibleRuntime.Started) 저절로 — Steam 초기화, 매 프레임 콜백, 끌 때 정리. 화면 없는 실행(서버 빌드·-batchmode)에서는 켜지 않는다.
    //   출시 빌드를 Steam 밖에서 켜면 Steam으로 다시 시작한다(설정, App ID 480이면 하지 않음). 개발 빌드는 DB의 App ID를 환경 변수 SteamAppId로 넣어 Steam 밖에서도 붙는다.
    //   Steam이 꺼져 있거나 steam_api(네이티브 라이브러리)가 없거나 맞지 않으면 경고 하나 남기고 Steam 없이 돈다(IsReady 거짓, 게임 시작은 끊기지 않음).
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
        // Valve의 시험용 앱(Spacewar). 출시 빌드의 'Steam으로 다시 시작'에는 쓰지 않는다.
        const uint TestAppId = 480;

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
            // 끌 때는 멀티를 닫고 저장한 뒤에 Steam을 닫는다(GBibleHost가 순서를 정한다).
            GBibleRuntime.Quitting -= Shutdown;
            GBibleRuntime.Quitting += Shutdown;
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
            // 화면 없는 실행(Dedicated Server 빌드·-batchmode)에는 Steam 클라이언트가 없다 — 켜지 않는다(출시 빌드의 'Steam으로 다시 시작'이 서버를 끄지 않게).
            if (GBibleRuntime.IsHeadless)
            {
                if (!_tried)
                {
                    _tried = true;
                    Debug.Log("[MomoiProject] 화면 없는 실행(서버 빌드·-batchmode)이라 Steam을 켜지 않습니다.");
                }
                return;
            }
            // Started의 다른 받는 쪽과 판 시작(시작할 때 이어 하기)이 끊기지 않게 여기서 멈춘다.
            try
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
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        static void Init(SteamData settings)
        {
#if !DISABLESTEAMWORKS
            try
            {
                // 출시 빌드를 Steam 밖에서 켰으면 Steam이 다시 켜 준다(이 실행은 끝낸다). 에디터는 steam_appid.txt, 개발 빌드는 환경 변수(UseAppIdInDevelopmentBuild)로 바로 붙는다.
                // 480(시험용 Spacewar)으로는 다시 시작하지 않는다 — Steam이 Spacewar를 켜고 이 게임은 꺼지기만 한다(DB 검증도 경고한다).
                if (settings.restartThroughSteam && !Application.isEditor && !Debug.isDebugBuild)
                {
                    if (settings.appId == TestAppId)
                        WarnOnce("restart480", "DB › Steam의 App ID가 480(시험용 Spacewar)이라 'Steam 밖에서 켜면 Steam으로 다시 시작'을 하지 않습니다(다시 시작하면 Spacewar가 켜지고 이 게임은 꺼집니다). 출시 전에 실제 App ID로 바꾸세요.");
                    else if (SteamAPI.RestartAppIfNecessary(new AppId_t(settings.appId)))
                    {
                        Application.Quit();
                        return;
                    }
                }
                if (!Packsize.Test()) Debug.LogError("[MomoiProject] Steamworks.NET의 Packsize 검사가 실패했습니다(플랫폼에 맞지 않는 Steamworks.NET).");
                if (!DllCheck.Test()) Debug.LogError("[MomoiProject] Steamworks.NET의 DllCheck 검사가 실패했습니다(steam_api 버전이 맞지 않음).");
                UseAppIdInDevelopmentBuild(settings);
                if (!SteamAPI.Init())
                {
                    Debug.LogWarning("[MomoiProject] Steam에 붙지 못해 Steam 없이 시작합니다(Steam 클라이언트가 켜져 있는지, 계정이 App ID의 앱을 가졌는지 확인 — " +
                                     "App ID는 에디터는 프로젝트 폴더의 steam_appid.txt, 개발 빌드는 DB › Steam의 App ID, 출시 빌드는 Steam이 켜 준 것).");
                    return;
                }
                IsReady = true;
                // 붙었다는 표시(에디터에서는 오버레이 Shift+Tab이 뜨지 않으므로 콘솔로 알린다 — 오버레이는 빌드에서).
                Debug.Log($"[MomoiProject] Steam에 붙었습니다: {SteamFriends.GetPersonaName()} · App ID {SteamUtils.GetAppID().m_AppId}. Steam 친구 목록에 이 앱을 하는 중으로 보입니다" +
                          (Application.isEditor ? " (오버레이 Shift+Tab은 에디터에서는 뜨지 않습니다 — 빌드에서 확인)." : "."));
                if (settings.pauseOnOverlay)
                    _overlay = Callback<GameOverlayActivated_t>.Create(e =>
                    {
                        if (e.m_bActive != 0) GameTime.Pause(OverlayPause);
                        else GameTime.Resume(OverlayPause);
                    });
                var host = new GameObject("[MomoiProject Steam]");
                UnityEngine.Object.DontDestroyOnLoad(host);
                host.AddComponent<GameSteamHost>();
            }
            catch (Exception e)
            {
                // steam_api(네이티브 라이브러리)가 없거나(DllNotFoundException) 플랫폼·버전이 맞지 않으면 첫 Steam 호출에서 예외가 난다.
                // 게임 시작(GBibleRuntime.Start)이 끊기지 않게, 붙은 것이 있으면 놓고 경고 하나로 Steam 없이 간다.
                Abandon();
                Debug.LogWarning("[MomoiProject] Steam을 켜지 못해 Steam 없이 시작합니다: " + Describe(e));
                return;
            }
            try
            {
                if (settings.languageFromSteam && !GameLanguage.HasSavedChoice && !GameSettings.HasSavedLanguage && Language is string code)
                    GameLanguage.Set(code, remember: false);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            RaiseReady();
#endif
        }

#if !DISABLESTEAMWORKS
        // 개발 빌드는 Steam 밖에서(exe를 바로) 켜도 DB › Steam의 App ID로 붙는다: steam_appid.txt 대신 이 프로세스의 환경 변수 SteamAppId·SteamGameId를 넣는다.
        // steam_appid.txt는 '현재 작업 폴더'에서 찾으므로 바로 가기·다른 폴더에서 켜기·macOS .app에서는 못 찾고, 빌드 폴더에 남으면 출시 빌드로 새어 나간다 — 그래서 파일을 복사하지 않는다.
        // 출시 빌드는 넣지 않는다(Steam이 켜 준다). Steam이 켠 실행(SteamAppId가 이미 있음 — 베타 브랜치에 올린 개발 빌드 등)은 그대로 둔다.
        static void UseAppIdInDevelopmentBuild(SteamData settings)
        {
            if (Application.isEditor || !Debug.isDebugBuild || settings.appId == 0) return;
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SteamAppId"))) return;
            var id = settings.appId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Environment.SetEnvironmentVariable("SteamAppId", id);
            Environment.SetEnvironmentVariable("SteamGameId", id);
        }

        // 초기화가 중간에 실패했을 때: 붙은 것(콜백·SteamAPI)을 놓는다 — 반쯤 켜진 채로 두지 않는다.
        static void Abandon()
        {
            _overlay?.Dispose();
            _overlay = null;
            if (!IsReady) return;
            IsReady = false;
            try { SteamAPI.Shutdown(); }
            catch (Exception) { /* 이미 실패한 초기화의 정리 — 더 알릴 것이 없다 */ }
        }

        // 초기화 예외 → 원인 한 줄(경고에 붙인다).
        static string Describe(Exception e)
        {
            while (e is TypeInitializationException && e.InnerException != null) e = e.InnerException;
            string reason;
            if (e is DllNotFoundException) reason = "Steam 라이브러리(steam_api)를 찾지 못했습니다 — Steamworks.NET 패키지의 Plugins가 이 플랫폼 빌드에 들어갔는지 확인하세요";
            else if (e is BadImageFormatException) reason = "Steam 라이브러리(steam_api)가 이 플랫폼·CPU(32/64비트)에 맞지 않습니다";
            else if (e is EntryPointNotFoundException) reason = "Steam 라이브러리(steam_api)의 버전이 Steamworks.NET과 맞지 않습니다";
            else reason = "초기화 중 오류가 났습니다";
            return $"{reason}. ({e.GetType().Name}: {e.Message})";
        }

        // Ready의 받는 쪽 하나가 던져도 나머지 받는 쪽과 게임 시작은 계속한다.
        static void RaiseReady()
        {
            var ready = Ready;
            if (ready == null) return;
            foreach (Action handler in ready.GetInvocationList())
            {
                try { handler(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }
#endif

        // 세션마다(슬롯을 바꿔 다시 시작해도) 도전과제·로그를 잇는다.
        static void Bind(GameSession session, SteamData settings)
        {
            _session = session;
            if (settings.achievements)
            {
                // 이 화면의 플레이어가 단 것만 Steam 계정에: 혼자 하기(0) · 방장(자기 번호) · 클라이언트(서버가 그 사람에게만 보낸 알림).
                session.Events.SubscribeAfterAction<AchievementUnlocked>(e =>
                {
                    if (e.Player == 0 || e.Player == session.LocalPlayer) Unlock(settings.SteamNameOf(e.Id));
                });
                // 혼자 하기 · 방장: 이미 단 것(오프라인에서 단 것 등)을 Steam에 맞춘다. 클라이언트는 서버 것이라 맞추지 않는다.
                if (!session.IsClient)
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
                if (double.IsNaN(value))
                {
                    // 최고·최저 로그는 첫 기록 전에는 비어 있다(NaN) — 올릴 것이 없을 뿐이라 알리지 않는다.
                    var aggregate = session.Stats.Find(link.stat)?.Aggregate;
                    if (aggregate != StatAggregate.Max && aggregate != StatAggregate.Min)
                        WarnOnce("statnan:" + name, $"로그 '{link.stat}'의 값이 숫자가 아니어서(NaN) Steam 통계 '{name}'에 올리지 않습니다.");
                    continue;
                }
                bool ok;
                if (link.integer)
                {
                    // int 범위를 넘는 값을 그대로 바꾸면 엉뚱한 값(음수 등)이 된다 — 끝값으로 자르고 한 번 알린다.
                    var rounded = GameNumbers.Round(value);
                    if (rounded > int.MaxValue || rounded < int.MinValue)
                    {
                        WarnOnce("statrange:" + name, $"로그 '{link.stat}'의 값({value})이 Steam INT 통계 '{name}'의 범위(±2,147,483,647)를 넘어 끝값으로 올립니다(큰 값은 Steamworks에서 FLOAT 통계로, DB › Steam의 '정수'를 끄세요).");
                        rounded = Math.Max(int.MinValue, Math.Min(int.MaxValue, rounded));
                    }
                    ok = SteamUserStats.SetStat(name, (int)rounded);
                }
                else
                {
                    var single = value;
                    if (single > float.MaxValue || single < -float.MaxValue)
                    {
                        WarnOnce("statrange:" + name, $"로그 '{link.stat}'의 값({value})이 Steam FLOAT 통계 '{name}'의 범위를 넘어 끝값으로 올립니다.");
                        single = Math.Max(-float.MaxValue, Math.Min(float.MaxValue, single));
                    }
                    ok = SteamUserStats.SetStat(name, (float)single);
                }
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
                case "norwegian": return "nb";   // GameLanguage는 노르웨이어를 nb(보크몰)로 쓴다
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
    }
}
