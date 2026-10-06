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
        internal static readonly (string from, string to, string marker)[] Legacy =
        {
            ("Assets/Code", Code, "Assets/Code/Rules/MomoiProject.Rules.asmdef"),
            ("Assets/MomoiProjectData", Data, "Assets/MomoiProjectData"),
            ("Assets/UI", UI, "Assets/UI/PanelSettings.asset"),
            ("Assets/MomoiProjectGenerated", Generated, "Assets/MomoiProjectGenerated"),
        };

        internal static bool AnyLegacy()
        {
            foreach (var (from, _, marker) in Legacy)
                if (Directory.Exists(from) && (File.Exists(marker) || Directory.Exists(marker))) return true;
            return false;
        }

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
        static void Merge(string from, string to)
        {
            var files = Directory.GetFiles(from, "*", SearchOption.AllDirectories)
                .Select(f => f.Replace('\\', '/')).Where(f => !f.EndsWith(".meta", System.StringComparison.OrdinalIgnoreCase)).ToList();
            var backup = Path.Combine(ProjectReset.BackupFolder, System.DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-merge");
            var replaced = 0;
            foreach (var file in files)
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
            foreach (var file in files)
            {
                var target = to + file.Substring(from.Length);
                EditorDatabase.EnsureFolder(Path.GetDirectoryName(target).Replace('\\', '/'));
                if (string.IsNullOrEmpty(AssetDatabase.MoveAsset(file, target))) moved++;
            }
            // 빈 예전 폴더를 치운다(안이 빈 폴더만 — 데이터 없음).
            foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length).Append(from))
            {
                var path = dir.Replace('\\', '/');
                if (Directory.Exists(path) && !Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Any(f => !f.EndsWith(".meta", System.StringComparison.OrdinalIgnoreCase)))
                    AssetDatabase.DeleteAsset(path);
            }
            Debug.Log($"[MomoiProject] 시스템 폴더를 합쳤습니다: {from} → {to} (옮긴 파일 {moved}개" +
                      (replaced > 0 ? $", 새 자리에 먼저 생긴 같은 이름 {replaced}개는 {backup}에 옮겨 둠)" : ")"));
        }

        // 예전 자리의 시스템 폴더를 새 자리로 옮긴다(GUID 유지 — 참조가 끊기지 않는다). 새 자리에 이미 있으면 그대로 두고 알린다.
        internal static void MoveLegacy()
        {
            foreach (var (from, to, marker) in Legacy)
            {
                if (!AssetDatabase.IsValidFolder(from) || !(File.Exists(marker) || Directory.Exists(marker))) continue;
                if (AssetDatabase.IsValidFolder(to))
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
