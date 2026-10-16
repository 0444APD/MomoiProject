using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GBible.Data;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

// 불러온 어셈블리 이름만 본다(기능 어셈블리가 컴파일되어 있는가). 다시 컴파일하면 도메인이 새로 뜨므로 한 번만 모은다.
#pragma warning disable UAC0005

namespace GBible.EditorTools
{
    // 프로젝트마다 켜고 끄는 선택 기능(다이얼로그 시스템 등) 하나.
    // 기능의 코드는 자기 어셈블리에 있고, 스크립팅 심볼(Symbol)이 있을 때만 컴파일된다(asmdef의 defineConstraints).
    // 끄면 기능의 클래스·DB 탭·메뉴가 모두 사라지고, 만들어 둔 데이터 에셋은 그대로 남아 다시 켜면 돌아온다.
    // 켜고 끄기 = 설정 파일(ProjectSettings/MomoiProject/Features.json)의 enabled. DB의 '기능 활성화' 스위치가 쓰고, AI도 이 파일을 고쳐 켠다
    // (명세서 6절에 켠 부가 기능 — 관례 0절). 에디터가 다시 읽을 때(컴파일 뒤) 심볼은 SyncOnLoad가, asmdef 참조는 StartSetup이 파일에 맞춘다.
    public sealed class GameFeature
    {
        public string Id;
        public string Name;
        public string Description;
        public string Symbol;
        // 기능의 게임 쪽 어셈블리 이름(이 안의 DB 종류가 기능의 클래스다).
        public string[] Assemblies = Array.Empty<string>();
        // 기능의 에디터 어셈블리(프로젝트 Editor asmdef만 참조한다 — DB 반영 스크립트가 기능의 에디터 도구를 부를 수 있게. 예: DialogueTemplate.Create).
        public string[] EditorAssemblies = Array.Empty<string>();
        // 꺼져 있을 때 DB 사이드바에 흐리게 보일 탭 이름(켜면 실제 탭으로 바뀐다).
        public string[] TabNames = Array.Empty<string>();
        // 기능의 데이터 클래스(DB 종류가 아닌 것) 가운데 프로젝트가 직접 쓰는 것의 전체 이름.
        // 플레이어 데이터의 값 형식으로 고를 수 있다(DB › 클래스 탭에는 보이지 않는다)(예: 인벤토리).
        public string[] DataClasses = Array.Empty<string>();

        // 기능의 코드가 이 프로젝트에 들어 있는지(폴더째 지운 프로젝트에서는 켤 수 없다).
        // 에셋 검색은 느려서(DB 사이드바를 그릴 때마다 불린다) 기억해 두고, 프로젝트 파일이 바뀌면 다시 찾는다.
        public bool Installed
        {
            get
            {
                if (_installed == null)
                {
                    _installed = Assemblies.Any(name => AssetDatabase.FindAssets(name + " t:AssemblyDefinitionAsset")
                        .Any(guid => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid)) == name));
                    EditorApplication.projectChanged -= ForgetInstalled;
                    EditorApplication.projectChanged += ForgetInstalled;
                }
                return _installed.Value;
            }
        }

        bool? _installed;

        void ForgetInstalled() => _installed = null;
    }

    // 선택 기능 목록과 켜짐 상태. 상태는 ProjectSettings/MomoiProject/Features.json에 두고(팀 공유),
    // 스크립팅 심볼은 그 파일에 맞춰 자동으로 넣고 뺀다 — 고른 플랫폼·PC·서버(Dedicated Server)에, 플랫폼을 바꾸는 순간 그 플랫폼에도.
    public static class GameFeatures
    {
        public const string SettingsPath = "ProjectSettings/MomoiProject/Features.json";

        [Serializable]
        sealed class Settings
        {
            public List<string> enabled = new List<string>();
        }

        public static readonly IReadOnlyList<GameFeature> All = new[]
        {
            new GameFeature
            {
                Id = "dialogue",
                Name = "다이얼로그 시스템",
                Description = "노드 그래프로 만드는 다이얼로그: 대사·선택지·조건 분기, 연출(배경·스탠딩·카메라·음악) 체인, 연출 프리셋, UI 템플릿, 대사 로그, 진행 저장.",
                Symbol = "GBIBLE_DIALOGUE",
                Assemblies = new[] { "GBible.Dialogue" },
                EditorAssemblies = new[] { "GBible.Dialogue.Editor" },
                TabNames = new[] { "스탠딩 프로필", "장소" },
            },
            new GameFeature
            {
                Id = "inventory",
                Name = "인벤토리 시스템",
                Description = "인벤토리(슬롯의 리스트: 겹치기·슬롯 수 제한·넣기·빼기). 아이템은 프로젝트가 코드로 정의한 클래스의 인스턴스이고, 슬롯에는 인스턴스 ID가 들어간다. 인벤토리는 플레이어 데이터의 값 형식으로 쓴다.",
                Symbol = "GBIBLE_INVENTORY",
                Assemblies = new[] { "GBible.Inventory" },
                DataClasses = new[] { "GBible.InventorySystem.Inventory", "GBible.InventorySystem.InventorySlot" },
            },
            new GameFeature
            {
                Id = "input",
                Name = "입력(키 바꾸기)",
                Description = "Input System의 키 바꾸기·되돌리기와 저장(설정 파일, 기기마다 하나). 같은 묶음의 다른 키와 겹치면 바꾸지 않고 알린다. Input System 패키지가 있어야 컴파일된다.",
                Symbol = "GBIBLE_INPUT",
                Assemblies = new[] { "GBible.Input" },
            },
            new GameFeature
            {
                Id = "steam",
                Name = "Steam",
                Description = "Steamworks.NET 위의 Steam 연동: 초기화·콜백·종료, Steam으로 다시 시작, 오버레이 일시정지, 처음 언어, 도전과제 올리기, 로그 → Steam 통계, 리치 프레즌스(GameSteam). 설정은 DB › Steam. Steamworks.NET 패키지가 있어야 컴파일된다.",
                Symbol = "GBIBLE_STEAM",
                Assemblies = new[] { "GBible.Steam" },
                TabNames = new[] { "Steam" },
            },
        };

        const string LegacySymbolPrefix = "GAME_SYSTEMS_";

        static Settings _settings;

        public static event Action Changed;

        static Settings Current
        {
            get
            {
                if (_settings != null) return _settings;
                try
                {
                    _settings = File.Exists(SettingsPath) ? JsonUtility.FromJson<Settings>(File.ReadAllText(SettingsPath)) : null;
                }
                catch (Exception)
                {
                    _settings = null;
                }
                return _settings ??= new Settings();
            }
        }

        public static GameFeature Find(string id) => All.FirstOrDefault(f => f.Id == id);

        public static bool IsEnabled(GameFeature feature) => feature != null && Current.enabled.Contains(feature.Id);

        // 켜져 있고 코드도 컴파일되어 있다(막 켠 직후에는 다시 컴파일이 끝나야 참이 된다).
        public static bool IsActive(GameFeature feature) =>
            IsEnabled(feature) && feature.Assemblies.Any(LoadedAssemblies.Contains);

        // 불러온 어셈블리 이름. 다시 컴파일하면 도메인이 새로 뜨므로 한 번만 모은다.
        static HashSet<string> _loadedAssemblies;

        static HashSet<string> LoadedAssemblies =>
            _loadedAssemblies ??= new HashSet<string>(AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name), StringComparer.Ordinal);

        public static void SetEnabled(GameFeature feature, bool enabled)
        {
            if (feature == null || IsEnabled(feature) == enabled) return;
            if (enabled) Current.enabled.Add(feature.Id);
            else Current.enabled.Remove(feature.Id);
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            File.WriteAllText(SettingsPath, JsonUtility.ToJson(Current, true));
            SessionState.SetString(StampKey, FileStamp());
            ApplySymbols();
            Changed?.Invoke();
        }

        // 이 형식이 속한 기능(기능의 클래스가 아니면 null).
        public static GameFeature Of(Type type)
        {
            if (type == null) return null;
            var assembly = type.Assembly.GetName().Name;
            return All.FirstOrDefault(f => f.Assemblies.Contains(assembly));
        }

        // 켜진 기능의 DB 종류(기능의 필수 클래스).
        public static IEnumerable<Type> ClassTypes(GameFeature feature) =>
            DataTypes.All.Select(i => i.Type).Where(t => Of(t) == feature && IsEnabled(feature));

        public static IEnumerable<Type> ClassTypes() => DataTypes.All.Select(i => i.Type).Where(t => IsEnabled(Of(t)));

        // 켜진 기능의 데이터 클래스(컴파일되어 있는 것만).
        public static IEnumerable<Type> DataClassTypes() =>
            All.Where(IsEnabled).SelectMany(f => f.DataClasses).Select(TypeResolver.Find).Where(t => t != null);

        // ── 스크립팅 심볼 ───────────────────────────────────────

        [InitializeOnLoadMethod]
        static void SyncOnLoad()
        {
            _settings = null;
            SessionState.SetString(StampKey, FileStamp());
            EditorApplication.delayCall += () => ApplySymbols();
        }

        const string StampKey = "MomoiProject.GameFeatures.Stamp";

        static string FileStamp() => File.Exists(SettingsPath) ? File.GetLastWriteTimeUtc(SettingsPath).Ticks.ToString() : "";

        // AI가 설정 파일을 고친 뒤(Play · RunTests 요청을 처리하기 전): 파일이 마지막으로 읽은 뒤 바뀌었으면 새로 읽어 심볼과 asmdef 참조를 맞춘다.
        // 새로 읽기(AssetDatabase.Refresh)만으로는 ProjectSettings의 파일이 다시 읽히지 않는다 — 코드가 바뀌지 않았으면 도메인도 다시 뜨지 않는다.
        // 맞추느라 다시 컴파일을 일으켰으면 참(요청 처리는 이번 확인을 건너뛰고, 컴파일이 끝난 뒤 이어서 한다).
        public static bool SyncIfChanged()
        {
            var stamp = FileStamp();
            if (SessionState.GetString(StampKey, "") == stamp) return false;
            SessionState.SetString(StampKey, stamp);
            _settings = null;
            Debug.Log("[MomoiProject] 기능 설정(ProjectSettings/MomoiProject/Features.json)이 바뀌어 심볼 · 어셈블리 참조를 맞춥니다.");
            var symbols = ApplySymbols();
            Changed?.Invoke();   // StartSetup이 asmdef 참조를 맞춘다(바뀌면 새로 읽기)
            return symbols || EditorApplication.isCompiling;
        }

        // 설정 파일에 맞게 심볼을 넣고 뺀다. 바뀐 것이 있을 때만 쓴다(다시 컴파일이 일어난다 — 그때 참).
        internal static bool ApplySymbols()
        {
            var saved = false;
            foreach (var target in Targets())
            {
                string current;
                try { current = PlayerSettings.GetScriptingDefineSymbols(target); }
                catch (Exception) { continue; }
                var symbols = current.Split(';').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                var changed = false;
                // 옛 이름(GameSystems) 때의 심볼은 새 이름으로 바꾼다(손으로 넣은 것도).
                for (var i = 0; i < symbols.Count; i++)
                {
                    if (!symbols[i].StartsWith(LegacySymbolPrefix, StringComparison.Ordinal)) continue;
                    var renamed = "GBIBLE_" + symbols[i].Substring(LegacySymbolPrefix.Length);
                    if (symbols.Contains(renamed)) symbols.RemoveAt(i--);
                    else symbols[i] = renamed;
                    changed = true;
                }
                foreach (var feature in All)
                {
                    var wanted = IsEnabled(feature) && feature.Installed;
                    if (wanted && !symbols.Contains(feature.Symbol))
                    {
                        symbols.Add(feature.Symbol);
                        changed = true;
                    }
                    else if (!wanted && symbols.Remove(feature.Symbol)) changed = true;
                }
                if (!changed) continue;
                try
                {
                    PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", symbols));
                    saved = true;
                }
                catch (Exception e) { Debug.LogWarning($"[MomoiProject] 기능 심볼을 바꾸지 못했습니다({target.TargetName}): {e.Message}"); }
            }
            // 프로젝트 설정 파일에도 남긴다(다음에 열 때·배치 실행 때 다시 바꾸지 않도록).
            if (saved) AssetDatabase.SaveAssets();
            return saved;
        }

        // 지금 고른 플랫폼, 지금 빌드 대상의 플랫폼, PC(에디터 기본), 서버(Dedicated Server).
        // 서버는 PC와 심볼 칸이 따로라(NamedBuildTarget.Server) 늘 같이 맞춘다 — 서버 빌드 프로필로 바꿔도 기능 어셈블리와 그것을 쓰는 게임 코드가 컴파일된다.
        // 다른 플랫폼은 그 플랫폼으로 바꾸는 순간(GameFeaturesBuildTargetSync) 맞춘다.
        static IEnumerable<NamedBuildTarget> Targets()
        {
            var targets = new List<NamedBuildTarget>();
            foreach (var group in new[] { EditorUserBuildSettings.selectedBuildTargetGroup, BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget) })
            {
                if (group == BuildTargetGroup.Unknown) continue;
                try { targets.Add(NamedBuildTarget.FromBuildTargetGroup(group)); }
                catch (Exception) { /* 이 에디터가 모르는 플랫폼 */ }
            }
            targets.Add(NamedBuildTarget.Standalone);
            targets.Add(NamedBuildTarget.Server);
            var seen = new HashSet<string>();
            return targets.Where(t => seen.Add(t.TargetName)).ToList();
        }
    }

    // 플랫폼(빌드 대상)을 바꾸면 그 플랫폼의 심볼도 바로 맞춘다. 바꾼 뒤의 다시 컴파일이 기능 심볼을 갖고 돌게 —
    // 심볼 없이 먼저 컴파일되면 기능을 쓰는 게임 코드가 실패하고, 실패한 컴파일 뒤에는 InitializeOnLoad(SyncOnLoad)가 다시 돌지 않는다.
    sealed class GameFeaturesBuildTargetSync : IActiveBuildTargetChanged
    {
        public int callbackOrder => 0;

        public void OnActiveBuildTargetChanged(BuildTarget previousTarget, BuildTarget newTarget) => GameFeatures.ApplySymbols();
    }
}
