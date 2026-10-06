using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GBible.EditorTools
{
    // 프로젝트의 폴더 나눔(2026-10-06):
    //   Assets/MomoiProject/  시스템·AI가 쓰는 곳 — 개발자는 들어가지 않는다.
    //     Code/       AI가 쓰는 코드(Rules · Client · Editor · Tests · Keywords + asmdef). DB의 요소 탭(클래스·이벤트·키워드)이 여기서 읽는다.
    //     Data/       DB 항목(종류마다 영어 폴더)·Resources/MomoiProject(GameDatabase·설정)·Texts·Tests(테스트 상황)
    //     UI/         화면 UXML·USS·Panel Settings·테마
    //     Generated/  생성 코드(형식이 asmdef 밖일 때의 GameIds)
    //     Scenes/     AI가 만드는 씬(테스트 씬 Test.unity)
    //   Assets/Dev/           개발자가 노는 곳 — 바로 아래 폴더 = DB 'Dev' 탭. 게임 씬(Dev/Scenes)·에셋 가져오기·넣을 에셋 자리도 여기.
    // 폴더·파일 이름은 영어(관례 2절).
    public static class ProjectFolders
    {
        public const string Root = "Assets/MomoiProject";
        public const string Code = Root + "/Code";
        public const string Data = Root + "/Data";
        public const string UI = Root + "/UI";
        public const string Generated = Root + "/Generated";
        // AI가 만드는 씬(테스트 씬).
        public const string Scenes = Root + "/Scenes";
        // 게임 씬(개발자가 레벨을 배치한다). Unity 기본 Assets/Scenes는 시작 설정·에디터를 열 때 여기로 옮긴다.
        public const string DevScenes = Dev + "/Scenes";
        public const string Dev = "Assets/Dev";

        // 예전 자리(2026-10-06 전): 에디터를 열 때 새 자리로 옮긴다(EditorDatabase.Upkeep). 시스템이 만든 것만 — 표시 파일이 있을 때.
        // 데이터·생성 코드는 폴더 이름(MomoiProjectData·MomoiProjectGenerated)이 시스템만 쓰는 이름이라 폴더 자체가 표시다.
        internal static readonly (string from, string to, string marker)[] Legacy =
        {
            ("Assets/Code", Code, "Assets/Code/Rules/MomoiProject.Rules.asmdef"),
            ("Assets/MomoiProjectData", Data, "Assets/MomoiProjectData"),
            ("Assets/UI", UI, "Assets/UI/PanelSettings.asset"),
            ("Assets/MomoiProjectGenerated", Generated, "Assets/MomoiProjectGenerated"),
        };

        // 이번 도메인에서 옮기기를 해 봤는가(그 뒤에도 남았으면 DB 목록을 맞추지 못한다고 알린다 — EditorDatabase.SyncDatabaseAsset).
        internal static bool MoveTried { get; private set; }

        // 아직 옮길 것이 남은 예전 폴더가 있는가. Unity가 읽는 파일이 하나라도 있을 때만 — .gitkeep·.DS_Store·이름 끝이 ~인 것만 남은 폴더가 DB 동기화를 영영 막지 않게.
        internal static bool AnyLegacy() => LegacyLeft().Any();

        internal static System.Collections.Generic.IEnumerable<string> LegacyLeft() =>
            Legacy.Where(l => Directory.Exists(l.from) && (File.Exists(l.marker) || Directory.Exists(l.marker)) && HasAssets(l.from)).Select(l => l.from);

        // Unity가 에셋으로 읽는 파일인가(relative = 폴더 기준 상대 경로). .meta·.tmp·숨은 것(.으로 시작)·이름 끝이 ~·cvs 폴더는 읽지 않는다.
        static bool IsAsset(string relative)
        {
            if (relative.EndsWith(".meta", System.StringComparison.OrdinalIgnoreCase) || relative.EndsWith(".tmp", System.StringComparison.OrdinalIgnoreCase)) return false;
            foreach (var part in relative.Split('/'))
                if (part.Length == 0 || part[0] == '.' || part.EndsWith("~", System.StringComparison.Ordinal) || part.Equals("cvs", System.StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        static bool HasAssets(string folder) =>
            Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any(f => IsAsset(f.Replace('\\', '/').Substring(folder.Length + 1)));

        // 게임 씬 폴더 Assets/Scenes → Assets/Dev/Scenes. 예전 테스트 씬(Assets/Scenes/Test.unity)은 Assets/MomoiProject/Scenes로.
        // 빌드 설정의 씬 목록은 GUID로 다시 맞춘다(씬 흐름은 씬 이름으로 찾으므로 그대로 돈다). 새 자리에 같은 이름이 있으면 그 파일은 그대로 둔다.
        internal static void MoveScenes(System.Collections.Generic.List<string> done)
        {
            const string legacy = "Assets/Scenes";
            if (!AssetDatabase.IsValidFolder(legacy)) return;
            var oldTest = legacy + "/Test.unity";
            if (File.Exists(oldTest) && !File.Exists(Scenes + "/Test.unity"))
            {
                EditorDatabase.EnsureFolder(Scenes);
                if (string.IsNullOrEmpty(AssetDatabase.MoveAsset(oldTest, Scenes + "/Test.unity"))) done?.Add($"{oldTest} → {Scenes}/Test.unity");
            }
            if (!AssetDatabase.IsValidFolder(DevScenes))
            {
                EditorDatabase.EnsureFolder(Dev);
                var error = AssetDatabase.MoveAsset(legacy, DevScenes);
                if (string.IsNullOrEmpty(error))
                {
                    Debug.Log($"[MomoiProject] 씬 폴더를 옮겼습니다: {legacy} → {DevScenes}");
                    done?.Add($"{legacy} → {DevScenes}(게임 씬은 개발자 폴더에)");
                }
                else Debug.LogWarning($"[MomoiProject] {legacy}를 {DevScenes}(으)로 옮기지 못했습니다: {error}");
            }
            else
            {
                foreach (var entry in Directory.GetFileSystemEntries(legacy))
                {
                    var from = entry.Replace('\\', '/');
                    if (from.EndsWith(".meta", System.StringComparison.OrdinalIgnoreCase)) continue;
                    var to = DevScenes + "/" + Path.GetFileName(from);
                    if (!File.Exists(to) && !Directory.Exists(to)) AssetDatabase.MoveAsset(from, to);
                }
                if (!Directory.EnumerateFileSystemEntries(legacy).Any()) AssetDatabase.DeleteAsset(legacy);
            }
            var scenes = EditorBuildSettings.scenes;
            var fixedScenes = scenes.Select(s =>
            {
                var path = AssetDatabase.GUIDToAssetPath(s.guid);
                return string.IsNullOrEmpty(path) || path == s.path ? s : new EditorBuildSettingsScene(path, s.enabled);
            }).ToArray();
            if (!scenes.Select(s => s.path).SequenceEqual(fixedScenes.Select(s => s.path))) EditorBuildSettings.scenes = fixedScenes;
        }

        // 새 자리에 이미 폴더가 생겼을 때(옮기기 전에 무언가 새 자리에 만들었을 때): 예전 것을 파일마다 새 자리로 합친다.
        // 같은 파일이 양쪽에 있으면 예전 것(원래 데이터 · 원래 GUID)을 쓰고, 새 자리의 것은 MomoiProjectBackups/<시각>-merge/로 옮겨 둔다(지우지 않는다).
        // Unity가 읽지 않는 파일(.gitkeep·.DS_Store·이름 끝이 ~ 등)은 새 자리로 옮기지 않고, 다 옮긴 뒤 남은 예전 폴더째(빈 폴더·.meta 포함) 같은 백업에 옮긴다.
        static void Merge(string from, string to)
        {
            var assets = Directory.GetFiles(from, "*", SearchOption.AllDirectories)
                .Select(f => f.Replace('\\', '/')).Where(f => IsAsset(f.Substring(from.Length + 1))).ToList();
            var backup = Path.Combine(ProjectReset.BackupFolder, System.DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-merge");
            var replaced = 0;
            foreach (var file in assets)
            {
                var target = to + file.Substring(from.Length);
                if (!File.Exists(target)) continue;
                var keep = Path.Combine(backup, target);
                Directory.CreateDirectory(Path.GetDirectoryName(keep));
                File.Move(target, keep);
                if (File.Exists(target + ".meta")) File.Move(target + ".meta", keep + ".meta");
                replaced++;
            }
            if (replaced > 0) AssetDatabase.Refresh();
            var moved = 0;
            var refresh = false;
            var left = new System.Collections.Generic.List<string>();
            foreach (var file in assets)
            {
                var target = to + file.Substring(from.Length);
                EditorDatabase.EnsureFolder(Path.GetDirectoryName(target).Replace('\\', '/'));
                var error = AssetDatabase.MoveAsset(file, target);
                if (string.IsNullOrEmpty(error))
                {
                    moved++;
                    continue;
                }
                // Unity가 옮기지 못하면(아직 가져오지 않은 파일 등) 파일과 .meta를 그대로 옮긴다(GUID 유지 — 다시 읽을 때 Unity가 옮긴 것으로 본다).
                try
                {
                    if (File.Exists(target)) throw new IOException("새 자리에 같은 이름이 있습니다");
                    File.Move(file, target);
                    if (File.Exists(file + ".meta")) File.Move(file + ".meta", target + ".meta");
                    moved++;
                    refresh = true;
                }
                catch (System.Exception e)
                {
                    left.Add($"  {file} ({error} / {e.Message})");
                }
            }
            if (refresh) AssetDatabase.Refresh();

            // 남은 예전 폴더(Unity가 읽지 않는 파일·빈 폴더·.meta)를 백업으로 옮긴다 — 남겨 두면 DB 목록 맞추기를 막고 에디터를 열 때마다 다시 합치려 든다.
            var swept = 0;
            if (left.Count == 0 && Directory.Exists(from))
            {
                var rest = Directory.GetFiles(from, "*", SearchOption.AllDirectories).Count(f => !f.EndsWith(".meta", System.StringComparison.OrdinalIgnoreCase));
                var keep = Path.Combine(backup, from);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(keep));
                    Directory.Move(from, keep);
                    if (File.Exists(from + ".meta")) File.Move(from + ".meta", keep + ".meta");
                    swept = rest;
                    AssetDatabase.Refresh();
                }
                catch (System.Exception e)
                {
                    // 에셋은 다 옮겼으므로 DB 목록 맞추기는 막지 않는다(AnyLegacy는 Unity가 읽는 파일만 본다). 다음에 에디터를 열 때 다시 해 본다.
                    Debug.LogWarning($"[MomoiProject] {from}에 Unity가 읽지 않는 파일·빈 폴더만 남았는데 {keep}(으)로 옮기지 못했습니다: {e.Message}. " +
                                     "Unity를 닫고 그 폴더를 직접 옮기거나 지워도 됩니다(에셋은 이미 새 자리에 있습니다).");
                }
            }
            if (moved + replaced > 0)
                Debug.Log($"[MomoiProject] 시스템 폴더를 합쳤습니다: {from} → {to} (옮긴 파일 {moved}개" +
                          (replaced > 0 ? $", 새 자리에 먼저 생긴 같은 이름 {replaced}개는 {backup}에 옮겨 둠" : "") +
                          (swept > 0 ? $", Unity가 읽지 않는 파일 {swept}개는 {backup}에 옮겨 둠" : "") + ")");
            else if (!Directory.Exists(from))
                Debug.Log($"[MomoiProject] 옮길 에셋이 없는 예전 시스템 폴더를 치웠습니다: {from} → {backup}" + (swept > 0 ? $" (Unity가 읽지 않는 파일 {swept}개)" : ""));
            if (left.Count > 0)
                Debug.LogWarning($"[MomoiProject] {from}의 일부를 옮기지 못해 예전 폴더가 남았습니다(남아 있는 동안 DB 목록을 맞추지 못합니다). " +
                                 $"Unity를 닫고 아래 파일을 {to} 아래 같은 자리로(.meta와 함께) 옮기세요:\n" + string.Join("\n", left));
        }

        // 예전 자리의 시스템 폴더를 새 자리로 옮긴다(GUID 유지 — 참조가 끊기지 않는다).
        // 새 자리에 이미 있거나 예전 자리에 Unity가 읽는 파일이 없으면(.gitkeep 같은 것만 남음) 합치기로 — 남은 것은 백업으로 옮기고 예전 폴더를 치운다.
        internal static void MoveLegacy()
        {
            MoveTried = true;
            foreach (var (from, to, marker) in Legacy)
            {
                if (!AssetDatabase.IsValidFolder(from) || !(File.Exists(marker) || Directory.Exists(marker))) continue;
                if (AssetDatabase.IsValidFolder(to) || !HasAssets(from))
                {
                    Merge(from, to);
                    continue;
                }
                EditorDatabase.EnsureFolder(Path.GetDirectoryName(to).Replace('\\', '/'));
                var error = AssetDatabase.MoveAsset(from, to);
                if (string.IsNullOrEmpty(error)) Debug.Log($"[MomoiProject] 시스템 폴더를 옮겼습니다: {from} → {to}");
                else Debug.LogWarning($"[MomoiProject] {from}를 {to}(으)로 옮기지 못했습니다: {error}");
            }
        }
    }
}
