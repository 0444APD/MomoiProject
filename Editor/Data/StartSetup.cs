using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GBible.Data;
using UnityEditor;
using UnityEngine;

namespace GBible.EditorTools
{
    // 시작 설정: 새 프로젝트에서 한 번에 하는 준비.
    //   코드 구조(고정): Assets/MomoiProject/Code/Rules · Client · Editor · Tests, 어셈블리 MomoiProject.*, 네임스페이스 MomoiProject.
    //     Rules  = 규칙(데이터·행동·이벤트·Block·[BindTo]) — GBible.Core·GBible.Unity(+ 데이터 모듈)만 참조
    //     Client = Unity 쪽(시작 설정 코드·화면·연출) — Rules + GBible + 켠 모듈
    //     Editor = 에디터 확장(Editor 전용), Tests = EditMode 규칙 테스트
    //   그 밖: Product Name, 선택 모듈, Undo 방식(DB 에셋의 세션 옵션), 언어, 시작 파일(Bootstrap.cs·플레이어 데이터 뼈대), DB 에셋.
    // 이미 있는 파일은 덮어쓰지 않는다(다시 열어도 안전). 모듈을 켜고 끄면 asmdef 참조를 따라 맞춘다.
    [InitializeOnLoad]
    public static class StartSetup
    {
        public const string CodeRoot = ProjectFolders.Code;
        public const string Prefix = "MomoiProject";
        public const string MarkerPath = "ProjectSettings/MomoiProject/StartSetup.json";
        public const string MenuPath = "MomoiProject/시작 설정";

        public static readonly string[] Parts = { "Rules", "Client", "Editor", "Tests" };

        public static string FolderOf(string part) => CodeRoot + "/" + part;
        public static string AsmdefPathOf(string part) => $"{FolderOf(part)}/{Prefix}.{part}.asmdef";
        public static string AssemblyOf(string part) => $"{Prefix}.{part}";

        // 고를 수 있는 언어(코드, 그 언어로 쓴 이름).
        public static readonly (string code, string name)[] Languages =
        {
            ("ko", "한국어"), ("en", "English"), ("ja", "日本語"), ("zh-Hans", "简体中文"), ("zh-Hant", "繁體中文"),
        };

        public sealed class Choices
        {
            public string productName;
            public HashSet<string> features = new HashSet<string>(StringComparer.Ordinal);
            public HistoryMode history = HistoryMode.Actions;
            public string sourceLanguage = "ko";
            public HashSet<string> languages = new HashSet<string>(StringComparer.Ordinal) { "ko" };
        }

        // 시작 설정이 만드는 플레이어 데이터: (ID, 구역, 클래스 이름, 설명).
        static readonly (string id, DataZone zone, string className, string label)[] DataEntries =
        {
            ("run", DataZone.Session, "RunState", "판"),
            ("profile", DataZone.Meta, "Profile", "프로필"),
        };

        const string RegenerateIdsKey = "MomoiProject.StartSetupRegenerateIds";

        static StartSetup()
        {
            // 깨끗한 프로젝트를 처음 열 때(시작 설정 전) 처음 상태를 기록해 둔다(메뉴 '처음 상태로 되돌리기'가 돌아올 곳).
            if (!Application.isBatchMode) ProjectReset.CaptureIfClean();
            GameFeatures.Changed -= SyncModuleReferences;
            GameFeatures.Changed += SyncModuleReferences;
            // 시작 설정이 만든 run·profile의 클래스가 컴파일된 뒤 GameIds(Game.Data().Run …)를 다시 만든다.
            if (SessionState.GetBool(RegenerateIdsKey, false) && TypeResolver.Find($"{Prefix}.{DataEntries[0].className}") != null)
            {
                SessionState.EraseBool(RegenerateIdsKey);
                EditorApplication.delayCall += () => GameIdsGenerator.Generate(force: true);
            }
            // 처음 한 번: 시작 설정을 마치지 않은 프로젝트면 창을 연다(배치 실행·테스트에서는 열지 않는다).
            if (!IsDone && !Application.isBatchMode) EditorApplication.delayCall += OpenOnce;
            // 시작 설정을 마친 프로젝트: Claude Code 파일(CLAUDE.md·스킬·컴파일 검사 훅)을 패키지 경로·템플릿에 맞춘다.
            // 그리고 DB 정리: 옛 한글 종류 폴더를 영어 이름으로 옮기고, 하나만 두는 설정(Steam 등)이 없으면 만든다.
            if (IsDone && !Application.isBatchMode)
                EditorApplication.delayCall += () =>
                {
                    ClaudeFiles.Sync();
                    EditorDatabase.Upkeep();
                };
        }

        // 스크립트가 하나도 없는 코드 어셈블리(처음의 Editor·Tests)에 자리 표시 파일을 둔다 — Unity가 "어셈블리에 스크립트가 없다" 경고를 내지 않게.
        // AI가 그 어셈블리에 스크립트를 쓴 뒤에는 지워도 된다.
        public static void EnsurePlaceholders(List<string> done)
        {
            foreach (var part in Parts)
            {
                var folder = FolderOf(part);
                if (!File.Exists(AsmdefPathOf(part)) || Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories).Length > 0) continue;
                var path = $"{folder}/{part}Assembly.cs";
                File.WriteAllText(path,
                    $"// {AssemblyOf(part)} 어셈블리의 자리 표시: 스크립트가 하나도 없으면 Unity가 경고를 내므로 둔다. 이 어셈블리에 다른 스크립트가 생기면 지워도 된다.\n" +
                    $"namespace {Prefix}\n{{\n    static class {part}Assembly\n    {{\n    }}\n}}\n", new UTF8Encoding(false));
                AssetDatabase.ImportAsset(path);
                done?.Add($"{path} 만듦(빈 어셈블리 경고 막기)");
            }
        }

        static void OpenOnce()
        {
            if (IsDone || SessionState.GetBool("MomoiProject.StartSetupShown", false)) return;
            SessionState.SetBool("MomoiProject.StartSetupShown", true);
            StartSetupWindow.Open();
        }

        public static bool IsDone => File.Exists(MarkerPath);

        public static bool HasStructure => Parts.All(p => File.Exists(AsmdefPathOf(p)));

        // 지금 상태로 채운 선택(창이 처음 보여 줄 값).
        public static Choices Current()
        {
            var choices = new Choices { productName = PlayerSettings.productName };
            foreach (var feature in GameFeatures.All)
                if (GameFeatures.IsEnabled(feature)) choices.features.Add(feature.Id);
            var settings = AssetDatabase.LoadAssetAtPath<LocalizationSettings>(TextSheetTools.SettingsPath);
            if (settings != null)
            {
                choices.sourceLanguage = settings.sourceLanguage;
                choices.languages = new HashSet<string>(settings.languages.Select(l => l.code), StringComparer.Ordinal);
            }
            choices.languages.Add(choices.sourceLanguage);
            var database = AssetDatabase.LoadAssetAtPath<GameDatabase>(EditorDatabase.DatabaseAssetPath);
            if (database != null) choices.history = database.History;
            return choices;
        }

        // 설정을 적용한다. 만든 것·바꾼 것을 사람이 읽는 줄로 돌려준다.
        public static List<string> Apply(Choices choices)
        {
            var done = new List<string>();

            if (!string.IsNullOrWhiteSpace(choices.productName) && PlayerSettings.productName != choices.productName.Trim())
            {
                PlayerSettings.productName = choices.productName.Trim();
                done.Add($"Product Name: {PlayerSettings.productName}");
            }

            foreach (var feature in GameFeatures.All)
            {
                var wanted = choices.features.Contains(feature.Id) && feature.Installed;
                if (GameFeatures.IsEnabled(feature) == wanted) continue;
                GameFeatures.SetEnabled(feature, wanted);
                done.Add($"{feature.Name}: {(wanted ? "켬" : "끔")}");
            }

            ApplyLanguages(choices, done);

            EditorDatabase.EnsureFolder(CodeRoot);
            foreach (var part in Parts)
            {
                EditorDatabase.EnsureFolder(FolderOf(part));
                if (File.Exists(AsmdefPathOf(part))) continue;
                File.WriteAllText(AsmdefPathOf(part), AsmdefJson(part), new UTF8Encoding(false));
                done.Add($"{AsmdefPathOf(part)} 만듦");
            }
            EnsurePlaceholders(done);

            var bootstrap = FolderOf("Client") + "/Bootstrap.cs";
            if (!File.Exists(bootstrap))
            {
                File.WriteAllText(bootstrap, BootstrapCode, new UTF8Encoding(false));
                done.Add($"{bootstrap} 만듦");
            }
            var playerData = FolderOf("Rules") + "/PlayerData.cs";
            if (!File.Exists(playerData))
            {
                File.WriteAllText(playerData, PlayerDataCode, new UTF8Encoding(false));
                done.Add($"{playerData} 만듦");
            }

            var database = AssetDatabase.LoadAssetAtPath<GameDatabase>(EditorDatabase.DatabaseAssetPath);
            if (database == null)
            {
                EditorDatabase.EnsureFolder(Path.GetDirectoryName(EditorDatabase.DatabaseAssetPath).Replace('\\', '/'));
                database = ScriptableObject.CreateInstance<GameDatabase>();
                AssetDatabase.CreateAsset(database, EditorDatabase.DatabaseAssetPath);
                done.Add($"{EditorDatabase.DatabaseAssetPath} 만듦");
            }
            // 판·프로필 플레이어 데이터(run = RunState·세션, profile = Profile·메타). 이미 있으면 건드리지 않는다.
            // 클래스는 아직 컴파일 전일 수 있으므로 형식 이름(글자)만 넣고, 다시 컴파일된 뒤 GameIds를 다시 만든다.
            var madeEntries = false;
            foreach (var (id, zone, className, label) in DataEntries)
            {
                if (EditorDatabase.Instance.FindById(id) != null) continue;
                var entry = (PlayerDataEntry)EditorDatabase.Instance.Create(typeof(PlayerDataEntry), id);
                entry.zone = zone;
                entry.valueType = $"{Prefix}.{className}";
                EditorUtility.SetDirty(entry);
                done.Add($"DB › 플레이어 데이터 '{id}' 만듦({label}, {className})");
                madeEntries = true;
            }
            if (madeEntries) SessionState.SetBool(RegenerateIdsKey, true);
            // Undo 방식은 DB 에셋의 세션 옵션(DB › 시작 파일 › Bootstrap 화면과 같은 값)에 넣는다.
            if (database.History != choices.history)
            {
                database.EditorSetSessionOptions(choices.history, database.HistoryLimit, database.SnapshotInterval, database.SnapshotLimit, database.FixSeedInDevelopment, database.DevelopmentSeed);
                EditorUtility.SetDirty(database);
                done.Add($"Undo 방식: {choices.history}");
            }

            // 개발자 폴더: 안에 폴더를 만들면 DB 사이드바 'Dev' 그룹의 탭이 된다.
            if (!AssetDatabase.IsValidFolder(DevFolders.Root))
            {
                EditorDatabase.EnsureFolder(DevFolders.Root);
                done.Add($"{DevFolders.Root} 만듦(개발자 폴더 — 안에 폴더를 만들면 DB 탭이 된다)");
            }

            SyncModuleReferences();
            // Claude Code: CLAUDE.md(관례·참조 문서 가져오기) · 스킬 /think-momoi · 응답을 끝낼 때 컴파일 검사 훅 · Docs/PlanningSpec.md.
            foreach (var file in ClaudeFiles.Sync()) done.Add($"{file} 만듦(Claude Code)");
            Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath));
            File.WriteAllText(MarkerPath, "{\"done\":true}", new UTF8Encoding(false));
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return done;
        }

        static void ApplyLanguages(Choices choices, List<string> done)
        {
            var settings = TextSheetTools.EnsureSettings();
            var changed = false;
            choices.languages.Add(choices.sourceLanguage);
            foreach (var code in choices.languages)
            {
                if (settings.languages.Any(l => l.code == code)) continue;
                var name = Languages.FirstOrDefault(l => l.code == code).name ?? code;
                settings.languages.Add(new LocalizationSettings.Language { code = code, nativeName = name, table = TextSheetTools.EnsureTable(code) });
                done.Add($"언어 추가: {name}");
                changed = true;
            }
            if (settings.sourceLanguage != choices.sourceLanguage)
            {
                settings.sourceLanguage = choices.sourceLanguage;
                done.Add($"원문 언어: {choices.sourceLanguage}");
                changed = true;
            }
            if (!changed) return;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
        }

        // ── asmdef ───────────────────────────────────────────

        // 켠 모듈의 어셈블리(Rules에는 데이터 클래스를 내놓는 모듈만 — 규칙은 화면 모듈을 모른다).
        static IEnumerable<string> ModuleAssemblies(bool rulesOnly) =>
            GameFeatures.All.Where(f => GameFeatures.IsEnabled(f) && f.Installed && (!rulesOnly || f.DataClasses.Length > 0)).SelectMany(f => f.Assemblies);

        static IEnumerable<string> AllModuleAssemblies => GameFeatures.All.SelectMany(f => f.Assemblies);

        static List<string> ReferencesOf(string part)
        {
            switch (part)
            {
                case "Rules": return new List<string> { "GBible.Core", "GBible.Unity" }.Concat(ModuleAssemblies(true)).ToList();
                case "Client": return new List<string> { AssemblyOf("Rules"), "GBible.Core", "GBible.Unity" }.Concat(ModuleAssemblies(false)).ToList();
                case "Editor": return new List<string> { AssemblyOf("Rules"), AssemblyOf("Client"), "GBible.Core", "GBible.Unity", "GBible.Editor" }.Concat(ModuleAssemblies(false)).ToList();
                default: return new List<string> { AssemblyOf("Rules"), "GBible.Core", "GBible.Unity", "UnityEngine.TestRunner", "UnityEditor.TestRunner" }.Concat(ModuleAssemblies(true)).ToList();
            }
        }

        static string AsmdefJson(string part)
        {
            var editorOnly = part == "Editor" || part == "Tests";
            var tests = part == "Tests";
            var b = new StringBuilder();
            b.Append("{\n");
            b.Append($"    \"name\": \"{AssemblyOf(part)}\",\n");
            b.Append($"    \"rootNamespace\": \"{Prefix}\",\n");
            b.Append($"    \"references\": [{string.Join(", ", ReferencesOf(part).Select(r => $"\"{r}\""))}],\n");
            b.Append($"    \"includePlatforms\": [{(editorOnly ? "\"Editor\"" : "")}],\n");
            b.Append("    \"excludePlatforms\": [],\n");
            b.Append("    \"allowUnsafeCode\": false,\n");
            b.Append($"    \"overrideReferences\": {(tests ? "true" : "false")},\n");
            b.Append($"    \"precompiledReferences\": [{(tests ? "\"nunit.framework.dll\"" : "")}],\n");
            b.Append($"    \"autoReferenced\": {(tests ? "false" : "true")},\n");
            b.Append($"    \"defineConstraints\": [{(tests ? "\"UNITY_INCLUDE_TESTS\"" : "")}],\n");
            b.Append("    \"versionDefines\": [],\n");
            b.Append("    \"noEngineReferences\": false\n");
            b.Append("}\n");
            return b.ToString();
        }

        // 켠 모듈에 맞게 Rules·Client·Editor asmdef의 참조를 더하고 뺀다(개발자가 asmdef를 열 일이 없게).
        public static void SyncModuleReferences()
        {
            var changed = false;
            foreach (var part in new[] { "Rules", "Client", "Editor", "Tests" })
            {
                var path = AsmdefPathOf(part);
                if (!File.Exists(path)) continue;
                var text = File.ReadAllText(path);
                var json = MiniJson.Deserialize(text) as Dictionary<string, object>;
                if (json == null || !(json.TryGetValue("references", out var raw) && raw is List<object> list)) continue;
                var references = list.Select(r => r as string).Where(r => r != null).ToList();
                var wanted = new HashSet<string>(ModuleAssemblies(part == "Rules" || part == "Tests"), StringComparer.Ordinal);
                var next = references.Where(r => !AllModuleAssemblies.Contains(r) || wanted.Contains(r)).ToList();
                foreach (var module in wanted)
                    if (!next.Contains(module)) next.Add(module);
                if (next.SequenceEqual(references)) continue;
                var start = text.IndexOf("\"references\"", StringComparison.Ordinal);
                var open = start < 0 ? -1 : text.IndexOf('[', start);
                var close = open < 0 ? -1 : text.IndexOf(']', open);
                if (close < 0) continue;
                text = text.Substring(0, open + 1) + string.Join(", ", next.Select(r => $"\"{r}\"")) + text.Substring(close);
                File.WriteAllText(path, text, new UTF8Encoding(false));
                changed = true;
            }
            if (changed) AssetDatabase.Refresh();
        }

        // ── 시작 파일 ─────────────────────────────────────────

        const string BootstrapCode =
@"// 시작 설정이 만든 파일. 자유롭게 고친다.
// 세션이 만들어지기 전(Configuring)과 만들어진 뒤(Started)에 할 일을 둔다.
using GBible;
using UnityEngine;

namespace MomoiProject
{
    public static class Bootstrap
    {
        // GBibleRuntime이 첫 씬을 읽기 전에 세션을 만들므로, 그보다 먼저 구독한다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Hook()
        {
            GBibleRuntime.Configuring += Configure;
            GBibleRuntime.Started += Started;
        }

        // 세션을 만들기 직전. Undo 방식·보관 수·시드는 DB › 시작 파일 › Bootstrap 화면에서 정하고, 그 값이 options에 먼저 들어 있다.
        // 여기서 options를 바꾸면 그 값이 우선한다(조건에 따라 바꿀 때만). 출시 뒤 저장 구조를 바꿨다면 변환도 여기서: options.Migrations.MoveField(2, ""run"", ""hero.gold"", ""gold"");
        static void Configure(SessionOptions options)
        {
        }

        // 세션 준비가 끝난 뒤(DB 데이터 등록·저장 불러오기까지). 행동 처리기, 프로젝트 고유 기능의 감시 등록, 개발용 명령 등.
        static void Started(GameSession session)
        {
        }
    }
}
";

        const string PlayerDataCode =
@"// 시작 설정이 만든 파일. 판(세션 구역)·프로필(메타 구역) 플레이어 데이터의 뼈대.
// DB › 플레이어 데이터의 ""run""(세션 구역)·""profile""(메타 구역)이 이 두 클래스를 가리킨다(시작 설정이 만든다). 코드에서는 Game.Data().Run·Game.Data().Profile.
// 필드는 그냥 고치면 기록된다(Undo·저장·멀티). 클래스 이름을 바꾸면 DB › 플레이어 데이터에서 값 형식도 다시 고른다.
using System;
using System.Collections.Generic;

namespace MomoiProject
{
    // 판 하나 동안의 값. 판이 끝나면 버린다.
    [Serializable]
    public class RunState
    {
    }

    // 판이 끝나도 남는 값(해금·누적 기록·설정).
    [Serializable]
    public class Profile
    {
    }
}
";
    }
}
