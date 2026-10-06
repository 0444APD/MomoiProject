using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace GBible.Data
{
    // DB › Steam(선택 기능 'Steam'): Steam 연동 설정. 항목은 하나만 둔다(여럿이면 첫 항목). Steam을 켜면 에디터가 기본값(App ID 480)으로 하나 만든다.
    //   App ID · Steam 밖에서 켜면 Steam으로 다시 시작 · 오버레이 일시정지 · 처음 언어 · 도전과제 올리기(이름이 다르면 표) · 로그 → Steam 통계.
    // 에디터에서 App ID를 고치면 프로젝트 폴더의 steam_appid.txt도 같이 고친다(에디터·개발 빌드가 Steam에 그 앱으로 붙는다).
    [Database("Steam", Group = "개발", Order = 40, IdPrefix = "steam", Single = true)]
    [CreateAssetMenu(menuName = "MomoiProject/Steam", fileName = "Steam")]
    public sealed class SteamData : GameData
    {
        [FieldLabel("App ID"), Tooltip("Steamworks의 앱 ID. 480은 Valve의 시험용 앱(Spacewar)")] public uint appId = 480;
        [FieldLabel("Steam 밖에서 켜면 Steam으로 다시 시작"), Tooltip("출시 빌드에서만. Steam이 게임을 다시 켜고 지금 실행은 끝난다")] public bool restartThroughSteam = true;
        [FieldLabel("오버레이가 열리면 일시정지")] public bool pauseOnOverlay = true;
        [FieldLabel("처음 실행 언어는 Steam 언어"), Tooltip("플레이어가 언어를 고른 적이 없을 때만 Steam 클라이언트의 언어로 시작한다")] public bool languageFromSteam = true;
        [FieldLabel("도전과제를 Steam에 올리기"), Tooltip("도전과제를 달성하면 같은 이름(아래 표에 없으면 ID)의 Steam 도전과제를 달성한다")] public bool achievements = true;
        [FieldLabel("도전과제 이름(ID와 다를 때)")] public List<SteamAchievementName> achievementNames = new List<SteamAchievementName>();
        [FieldLabel("로그 → Steam 통계")] public List<SteamStatLink> stats = new List<SteamStatLink>();

        // 이 도전과제의 Steam 이름(API Name): 표에 있으면 그 이름, 없으면 ID.
        public string SteamNameOf(string achievementId)
        {
            var named = achievementNames.FirstOrDefault(n => n != null && n.achievement == achievementId)?.steamName;
            return string.IsNullOrWhiteSpace(named) ? achievementId : named.Trim();
        }

        public override void ValidateData(IValidationContext context)
        {
            if (appId == 0) context.Error("App ID가 0입니다(시험은 480).");
            foreach (var group in achievementNames.Where(n => n != null && !string.IsNullOrEmpty(n.achievement)).GroupBy(n => n.achievement))
                if (group.Count() > 1) context.Error($"도전과제 '{group.Key}'의 Steam 이름이 두 번 적혀 있습니다.");
            if (achievementNames.Any(n => n != null && string.IsNullOrEmpty(n.achievement))) context.Error("도전과제를 고르지 않은 이름 줄이 있습니다.");
            foreach (var link in stats)
            {
                if (link == null) continue;
                if (string.IsNullOrEmpty(link.stat)) context.Error("로그를 고르지 않은 통계 줄이 있습니다.");
                if (string.IsNullOrWhiteSpace(link.steamName)) context.Error($"로그 '{link.stat}': Steam 통계 이름(API Name)을 적으세요.");
            }
            foreach (var group in stats.Where(s => s != null && !string.IsNullOrWhiteSpace(s.steamName)).GroupBy(s => s.steamName.Trim()))
                if (group.Count() > 1) context.Error($"Steam 통계 '{group.Key}'에 로그가 둘 이상 연결되어 있습니다.");
        }

#if UNITY_EDITOR
        static string AppIdPath => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "steam_appid.txt");

        // Steam 기능을 켜면(이 어셈블리가 컴파일되면) steam_appid.txt가 없을 때 바로 만든다 — 에디터 플레이가 그 앱으로 Steam에 붙는다.
        // DB › Steam 항목이 있으면 그 App ID, 없으면 480. 이미 있으면 그대로 둔다(App ID를 고치면 OnValidate·SteamAppIdPostprocessor가 고친다).
        [UnityEditor.InitializeOnLoadMethod]
        static void EnsureAppIdFile()
        {
            if (File.Exists(AppIdPath)) return;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (File.Exists(AppIdPath)) return;
                uint id = 480;
                foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:" + nameof(SteamData)))
                {
                    var item = UnityEditor.AssetDatabase.LoadAssetAtPath<SteamData>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                    if (item != null && item.appId != 0)
                    {
                        id = item.appId;
                        break;
                    }
                }
                WriteAppId(id);
            };
        }

        internal static void WriteAppId(uint id)
        {
            try
            {
                var text = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (File.Exists(AppIdPath) && File.ReadAllText(AppIdPath).Trim() == text) return;
                File.WriteAllText(AppIdPath, text, new System.Text.UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MomoiProject] steam_appid.txt를 쓰지 못했습니다: " + e.Message);
            }
        }

        void OnValidate()
        {
            // 저장된 DB 항목만(에디터가 필드 목록을 읽으려고 잠깐 만든 기본값 인스턴스가 프로젝트의 App ID를 480으로 덮지 않게).
            if (appId == 0 || !UnityEditor.EditorUtility.IsPersistent(this)) return;
            WriteAppId(appId);
        }
#endif
    }

#if UNITY_EDITOR
    // DB의 저장(Ctrl+S)은 작업본을 원본에 복사하므로(EditorUtility.CopySerialized) OnValidate가 불리지 않을 수 있다.
    // 저장된 Steam 항목 에셋이 다시 읽히면(저장·가져오기) 그 App ID로 steam_appid.txt를 맞춘다(같으면 쓰지 않는다).
    sealed class SteamAppIdPostprocessor : UnityEditor.AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var path in imported)
            {
                if (!path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) || UnityEditor.AssetDatabase.GetMainAssetTypeAtPath(path) != typeof(SteamData)) continue;
                var item = UnityEditor.AssetDatabase.LoadAssetAtPath<SteamData>(path);
                if (item == null || item.appId == 0 || !UnityEditor.EditorUtility.IsPersistent(item)) continue;
                SteamData.WriteAppId(item.appId);
                return;
            }
        }
    }
#endif

    [Serializable]
    public sealed class SteamAchievementName
    {
        [AchievementRef, FieldLabel("도전과제")] public string achievement = "";
        [FieldLabel("Steam 이름(API Name)")] public string steamName = "";
    }

    [Serializable]
    public sealed class SteamStatLink
    {
        [StatRef, FieldLabel("로그")] public string stat = "";
        [FieldLabel("Steam 통계 이름(API Name)")] public string steamName = "";
        [FieldLabel("정수(INT)"), Tooltip("Steamworks에서 INT 통계면 켜고, FLOAT면 끈다")] public bool integer = true;
    }
}
