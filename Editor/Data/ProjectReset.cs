using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GBible.EditorTools
{
    // 처음 상태로 되돌리기: 프로젝트를 "막 만들고 Momoi Project와 필요한 에셋만 설치한" 깨끗한 상태로 되돌린다.
    //
    // 처음 상태 기록(MomoiProjectBaseline/, 프로젝트 폴더 바로 아래 — Assets 밖이라 Unity가 읽지 않는다):
    //   Assets · ProjectSettings · Packages(GBible 자신은 빼고)의 사본. 시작 설정을 하기 전 깨끗한 프로젝트를 처음 열 때 저절로 남기고,
    //   메뉴 '지금 상태를 처음 상태로 기록'으로 다시 남길 수 있다.
    // 되돌리기:
    //   기록에 없는 파일은 치우고, 바뀐 파일은 기록의 것으로 되돌리고, 지워진 파일은 다시 만든다. 저장 파일(persistentDataPath/MomoiProject 전체)·PlayerPrefs도 지운다.
    //   프로젝트 폴더 바로 아래의 시스템 파일(CLAUDE.md · .claude/ · Docs/PlanningSpec.md · Docs/Decisions.md · steam_appid.txt)도 치운다 — AI가 예전 결정을 따르지 않게.
    //   (Docs/Source 등 Docs의 다른 파일은 그대로. 'Claude Code 파일 › 맞추지 않기'를 켠 프로젝트의 CLAUDE.md·.claude·Docs는 사람이 쓴 것이라 그대로.)
    //   열린 씬·DB의 저장 안 된 변경은 먼저 묻는다(저장·버리기·취소).
    //   치우거나 덮어쓴 것은 모두 MomoiProjectBackups/<시각>/에 옮겨 두므로 되돌리기를 되돌릴 수 있다(영구 삭제 없음).
    //   끝나면 Unity를 다시 연다(ProjectSettings·패키지를 깨끗이 다시 읽도록).
    // 기록이 없으면 시스템이 만든 것만(Assets/MomoiProject 전체, ProjectSettings/MomoiProject, 위의 프로젝트 폴더 시스템 파일, GBIBLE_ 심볼, 저장) 치울 수 있다.
    public static class ProjectReset
    {
        public const string BaselineFolder = "MomoiProjectBaseline";
        public const string BackupFolder = "MomoiProjectBackups";
        const string MenuReset = "MomoiProject/처음 상태로 되돌리기…";
        const string MenuCapture = "MomoiProject/지금 상태를 처음 상태로 기록";
        const string GBiblePackage = "Packages/com.momoi.gbible";

        static readonly string[] Areas = { "Assets", "ProjectSettings", "Packages" };

        static string ProjectRoot => Path.GetFullPath(".").Replace('\\', '/');
        public static bool HasBaseline => File.Exists(Path.Combine(BaselineFolder, "baseline.json"));

        // 시스템이 아직 아무것도 만들지 않은 프로젝트인가(처음 상태를 저절로 기록해도 되는 때).
        // 개발자 폴더(Assets/Dev)가 있으면 아니다 — '시스템이 만든 것만 치우기' 뒤에 남은 개발자 것을 처음 상태로 기록하지 않게.
        public static bool LooksClean =>
            !Directory.Exists(ProjectFolders.Root) && !Directory.Exists(ProjectFolders.Dev) && !ProjectFolders.AnyLegacy() && !File.Exists(StartSetup.MarkerPath);

        // ── 처음 상태 기록 ─────────────────────────────────────

        [MenuItem(MenuCapture, priority = 32)]
        static void CaptureFromMenu()
        {
            var message = HasBaseline
                ? "이미 있는 처음 상태 기록을 지금 상태로 바꿉니다. 앞으로 '처음 상태로 되돌리기'는 지금 상태로 돌아옵니다.\n\n프로젝트를 막 만든 깨끗한 때에만 하세요."
                : "지금 상태를 처음 상태로 기록합니다. 앞으로 '처음 상태로 되돌리기'는 지금 상태로 돌아옵니다.\n\n프로젝트를 막 만든 깨끗한 때에만 하세요.";
            if (!EditorUtility.DisplayDialog("처음 상태 기록", message, "기록", "취소")) return;
            AssetDatabase.SaveAssets();
            Capture();
            Debug.Log($"[MomoiProject] 처음 상태를 기록했습니다: {ProjectRoot}/{BaselineFolder}");
        }

        // 깨끗한 프로젝트를 처음 열 때(시작 설정 전) 저절로 부른다.
        internal static void CaptureIfClean()
        {
            if (HasBaseline || !LooksClean) return;
            try
            {
                Capture();
                Debug.Log($"[MomoiProject] 처음 상태를 기록했습니다(메뉴 '처음 상태로 되돌리기'가 이 상태로 돌아옵니다): {BaselineFolder}");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MomoiProject] 처음 상태를 기록하지 못했습니다: " + e.Message);
            }
        }

        static void Capture()
        {
            if (Directory.Exists(BaselineFolder)) Directory.Delete(BaselineFolder, true);
            foreach (var file in CurrentFiles()) CopyFile(file, Path.Combine(BaselineFolder, file));
            File.WriteAllText(Path.Combine(BaselineFolder, "baseline.json"),
                $"{{\"time\":\"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\",\"unity\":\"{Application.unityVersion}\"}}");
        }

        // ── 되돌리기 ───────────────────────────────────────────

        [MenuItem(MenuReset, priority = 31)]
        static void ResetFromMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("처음 상태로 되돌리기", "플레이 중에는 할 수 없습니다. 플레이를 멈추고 다시 하세요.", "확인");
                return;
            }
            if (HasBaseline) ResetToBaseline();
            else ResetSystemOnly();
        }

        static void ResetToBaseline()
        {
            AssetDatabase.SaveAssets();
            var plan = Plan();
            var rootItems = RootItems();
            if (plan.remove.Count + plan.restore.Count + plan.recreate.Count == 0 && !HasSaves() && rootItems.Count == 0)
            {
                EditorUtility.DisplayDialog("처음 상태로 되돌리기", "이미 처음 상태와 같습니다.", "확인");
                return;
            }
            var sample = string.Join("\n", plan.remove.Concat(plan.restore).Take(8).Select(p => "  " + p));
            var summary =
                $"처음 상태({BaselineTime()})로 되돌립니다.\n\n" +
                $"치울 파일 {plan.remove.Count}개 · 되돌릴 파일 {plan.restore.Count}개 · 다시 만들 파일 {plan.recreate.Count}개\n" +
                (sample.Length > 0 ? sample + (plan.remove.Count + plan.restore.Count > 8 ? "\n  …" : "") + "\n" : "") +
                RootItemsLine(rootItems) +
                "저장 파일(persistentDataPath/MomoiProject 전체 — profile·run·슬롯·리플레이 등)과 PlayerPrefs도 지웁니다.\n\n" +
                $"치우거나 덮어쓴 것은 모두 {BackupFolder}/ 에 옮겨 두므로 되돌릴 수 있습니다. 끝나면 Unity를 다시 엽니다.";
            if (!EditorUtility.DisplayDialog("처음 상태로 되돌리기", summary, "되돌리기", "취소")) return;
            if (!EditorUtility.DisplayDialog("처음 상태로 되돌리기", "정말 되돌릴까요? 개발자가 만들고 바꾼 것이 모두 프로젝트에서 빠집니다(백업 폴더에는 남습니다).", "되돌리고 다시 열기", "취소")) return;
            if (!AskAboutUnsaved()) return;
            // 방금 저장한 씬·DB까지 넣어 다시 계산한다(저장한 것도 백업으로 간다).
            AssetDatabase.SaveAssets();
            plan = Plan();

            var backup = PrepareBackup();
            try
            {
                EditorUtility.DisplayProgressBar("처음 상태로 되돌리기", "파일을 옮기는 중", 0.3f);
                foreach (var file in plan.remove) MoveFile(file, Path.Combine(backup, file));
                foreach (var file in plan.restore)
                {
                    MoveFile(file, Path.Combine(backup, file));
                    CopyFile(Path.Combine(BaselineFolder, file), file);
                }
                foreach (var file in plan.recreate) CopyFile(Path.Combine(BaselineFolder, file), file);
                foreach (var area in new[] { "Assets", "Packages" }) RemoveEmptyFolders(area);
                var rootMoved = MoveRootItems(backup, rootItems);
                ClearSavesAndPrefs(backup);
                WriteBackupNote(backup, plan.remove.Count + rootMoved, plan.restore.Count, plan.recreate.Count);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            Reopen();
        }

        // 처음 상태 기록이 없을 때: 시스템이 만든 것만 치운다.
        static void ResetSystemOnly()
        {
            var targets = new[] { ProjectFolders.Root, "ProjectSettings/MomoiProject" }.Concat(ProjectFolders.Legacy.Where(l => File.Exists(l.marker) || Directory.Exists(l.marker)).Select(l => l.from))
                .Where(p => Directory.Exists(p)).Distinct().ToList();
            var rootItems = RootItems();
            var message =
                "처음 상태 기록이 없습니다(깨끗한 프로젝트를 처음 열 때 저절로 남거나, 메뉴 '지금 상태를 처음 상태로 기록'으로 남깁니다).\n\n" +
                "대신 시스템이 만든 것만 치울 수 있습니다:\n" +
                string.Join("\n", targets.Select(t => "  " + t + "/")) + (targets.Count > 0 ? "\n" : "") +
                string.Join("\n", rootItems.Select(r => "  " + RootLabel(r))) + (rootItems.Count > 0 ? "\n" : "") +
                "  모듈 심볼(GBIBLE_…), 저장 파일(persistentDataPath/MomoiProject 전체), PlayerPrefs\n\n" +
                "개발자가 그 밖에 더한 에셋(Assets/Dev 등)·패키지·설정·Docs의 다른 파일은 그대로 남습니다. 치운 것은 모두 백업 폴더에 옮겨 둡니다. 끝나면 Unity를 다시 엽니다.";
            if (!EditorUtility.DisplayDialog("처음 상태로 되돌리기", message, "시스템이 만든 것만 치우기", "취소")) return;
            if (!AskAboutUnsaved()) return;

            AssetDatabase.SaveAssets();
            var backup = PrepareBackup();
            var moved = 0;
            foreach (var folder in targets)
            {
                foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Select(Normalize))
                {
                    MoveFile(file, Path.Combine(backup, file));
                    moved++;
                }
                Directory.Delete(folder, true);
                if (File.Exists(folder + ".meta")) MoveFile(folder + ".meta", Path.Combine(backup, folder + ".meta"));
            }
            // 미리 정한 목록으로(ProjectSettings/MomoiProject를 옮긴 뒤에는 '맞추지 않기' 표시도 빠져 있다).
            moved += MoveRootItems(backup, rootItems);
            RemoveModuleSymbols();
            ClearSavesAndPrefs(backup);
            WriteBackupNote(backup, moved, 0, 0);
            Reopen();
        }

        // ── 계획 ───────────────────────────────────────────────

        static (List<string> remove, List<string> restore, List<string> recreate) Plan()
        {
            var current = new HashSet<string>(CurrentFiles(), StringComparer.Ordinal);
            var baseline = new HashSet<string>(BaselineFiles(), StringComparer.Ordinal);
            var remove = current.Where(f => !baseline.Contains(f)).OrderBy(f => f, StringComparer.Ordinal).ToList();
            var restore = current.Where(f => baseline.Contains(f) && !SameFile(f, Path.Combine(BaselineFolder, f))).OrderBy(f => f, StringComparer.Ordinal).ToList();
            var recreate = baseline.Where(f => !current.Contains(f)).OrderBy(f => f, StringComparer.Ordinal).ToList();
            return (remove, restore, recreate);
        }

        // 비교 대상 파일(프로젝트 기준 상대 경로). GBible 패키지 자신과 Unity 버전 파일은 뺀다.
        static IEnumerable<string> CurrentFiles()
        {
            foreach (var area in Areas)
            {
                if (!Directory.Exists(area)) continue;
                foreach (var file in Directory.GetFiles(area, "*", SearchOption.AllDirectories).Select(Normalize))
                    if (Included(file)) yield return file;
            }
        }

        static IEnumerable<string> BaselineFiles()
        {
            foreach (var area in Areas)
            {
                var root = Path.Combine(BaselineFolder, area);
                if (!Directory.Exists(root)) continue;
                foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    var relative = Normalize(file).Substring(BaselineFolder.Length + 1);
                    if (Included(relative)) yield return relative;
                }
            }
        }

        static bool Included(string file) =>
            !file.StartsWith(GBiblePackage + "/", StringComparison.Ordinal) && file != "ProjectSettings/ProjectVersion.txt";

        static string Normalize(string path) => path.Replace('\\', '/');

        static bool SameFile(string a, string b)
        {
            var fa = new FileInfo(a);
            var fb = new FileInfo(b);
            if (!fa.Exists || !fb.Exists || fa.Length != fb.Length) return false;
            using (var sa = fa.OpenRead())
            using (var sb = fb.OpenRead())
            {
                var ba = new byte[64 * 1024];
                var bb = new byte[64 * 1024];
                int na;
                while ((na = sa.Read(ba, 0, ba.Length)) > 0)
                {
                    var nb = sb.Read(bb, 0, na);
                    if (nb != na) return false;
                    for (int i = 0; i < na; i++)
                        if (ba[i] != bb[i]) return false;
                }
            }
            return true;
        }

        // ── 실행 도우미 ─────────────────────────────────────────

        // 열린 씬·DB의 저장 안 된 변경을 먼저 묻는다(저장·저장 안 함·취소). 되돌리기는 열린 씬을 닫고 DB 작업본을 버리므로, 묻지 않으면 그 변경이 백업에도 남지 않고 사라진다.
        // 취소하면 false(되돌리지 않는다).
        static bool AskAboutUnsaved()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            return CodexStaging.AskBeforeLeaving("처음 상태로 되돌리기 전에") != CodexStaging.AskResult.Cancel;
        }

        // ── 프로젝트 폴더 바로 아래의 시스템 파일(Assets 밖) ─────────────
        // Claude Code 파일(CLAUDE.md · .claude/)·기획 명세서·결정 기록(ClaudeFiles가 만든다)과 Steam App ID 파일(Steam 모듈이 만든다).
        // 남겨 두면 되돌린 뒤에도 AI가 예전 결정·명세를 따른다. Docs/Source(자료)와 Docs의 다른 파일은 개발자 것이라 건드리지 않는다.
        // 'Claude Code 파일 › 맞추지 않기'를 켠 프로젝트(라이브러리 개발 등)의 CLAUDE.md·.claude·Docs는 시스템이 만든 것이 아니므로 그대로 둔다.
        static readonly string[] ClaudeItems = { "CLAUDE.md", ".claude", "Docs/PlanningSpec.md", "Docs/Decisions.md" };
        const string SteamAppIdFile = "steam_appid.txt";

        static List<string> RootItems()
        {
            var items = new List<string>();
            if (!ClaudeFiles.IsOff)
                items.AddRange(ClaudeItems.Where(p => File.Exists(p) || Directory.Exists(p)));
            if (File.Exists(SteamAppIdFile)) items.Add(SteamAppIdFile);
            return items;
        }

        static string RootLabel(string item) => Directory.Exists(item) ? item + "/" : item;

        static string RootItemsLine(List<string> items) =>
            items.Count == 0 ? "" : $"프로젝트 폴더의 시스템 파일도 치웁니다(AI가 예전 결정을 따르지 않게): {string.Join(" · ", items.Select(RootLabel))}\n";

        // 백업으로 옮긴다(같은 상대 경로). 옮긴 파일 수.
        static int MoveRootItems(string backup, List<string> items)
        {
            var moved = 0;
            foreach (var item in items)
            {
                if (File.Exists(item))
                {
                    MoveFile(item, Path.Combine(backup, item));
                    moved++;
                }
                else if (Directory.Exists(item))
                {
                    foreach (var file in Directory.GetFiles(item, "*", SearchOption.AllDirectories).Select(Normalize))
                    {
                        MoveFile(file, Path.Combine(backup, file));
                        moved++;
                    }
                    RemoveEmptyTree(item);
                }
            }
            if (Directory.Exists("Docs") && !Directory.EnumerateFileSystemEntries("Docs").Any()) Directory.Delete("Docs");
            return moved;
        }

        // 빈 폴더만 지운다(안쪽부터, folder 자신까지). 파일이 남은 폴더는 그대로.
        static void RemoveEmptyTree(string folder)
        {
            if (!Directory.Exists(folder)) return;
            foreach (var dir in Directory.GetDirectories(folder, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length).Append(folder))
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
        }

        static string PrepareBackup()
        {
            // 열린 씬·DB 작업본을 놓는다(바뀐 파일을 Unity가 다시 쓰지 않게). 저장 안 된 변경은 그 전에 물었다(AskAboutUnsaved).
            CodexChanges.Discard();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var backup = Path.Combine(BackupFolder, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(backup);
            return backup;
        }

        static void MoveFile(string from, string to)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            if (File.Exists(to)) File.Delete(to);
            File.Move(from, to);
        }

        static void CopyFile(string from, string to)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            File.Copy(from, to, true);
        }

        static void RemoveEmptyFolders(string root)
        {
            if (!Directory.Exists(root)) return;
            foreach (var dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
            {
                if (Normalize(dir).StartsWith(GBiblePackage, StringComparison.Ordinal)) continue;
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
            }
        }

        static string SaveFolder => Path.Combine(Application.persistentDataPath, "MomoiProject");

        // 저장 폴더 안 모두(하위 폴더 — 슬롯 slots/<이름>/ · Replays/ · server/ · Tests/ 등 — 까지).
        static bool HasSaves() => Directory.Exists(SaveFolder) && Directory.EnumerateFiles(SaveFolder, "*", SearchOption.AllDirectories).Any();

        static void ClearSavesAndPrefs(string backup)
        {
            if (Directory.Exists(SaveFolder))
            {
                var root = Path.GetFullPath(SaveFolder);
                foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetFullPath(file).Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    MoveFile(file, Path.Combine(backup, "_saves", relative));
                }
                RemoveEmptyTree(root);
            }
            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();
        }

        static void RemoveModuleSymbols()
        {
            foreach (BuildTargetGroup group in Enum.GetValues(typeof(BuildTargetGroup)))
            {
                if (group == BuildTargetGroup.Unknown) continue;
                UnityEditor.Build.NamedBuildTarget target;
                try { target = UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(group); }
                catch (Exception) { continue; }
                string current;
                try { current = PlayerSettings.GetScriptingDefineSymbols(target); }
                catch (Exception) { continue; }
                var symbols = current.Split(';').Where(s => s.Length > 0 && !s.StartsWith("GBIBLE_", StringComparison.Ordinal)).ToArray();
                var next = string.Join(";", symbols);
                if (next != current) PlayerSettings.SetScriptingDefineSymbols(target, next);
            }
            AssetDatabase.SaveAssets();
        }

        static void WriteBackupNote(string backup, int moved, int restored, int recreated)
        {
            File.WriteAllText(Path.Combine(backup, "README.txt"),
                $"MomoiProject 처음 상태로 되돌리기 백업 ({DateTime.Now:yyyy-MM-dd HH:mm:ss})\r\n" +
                $"치운 파일 {moved}개, 덮어쓴 파일 {restored}개(원래 것이 여기 있음), 다시 만든 파일 {recreated}개.\r\n" +
                "되돌리려면 Unity를 닫고 이 폴더의 Assets·ProjectSettings·Packages(와 CLAUDE.md·.claude·Docs·steam_appid.txt — 있으면)를 프로젝트 폴더에 덮어쓴다. " +
                "_saves는 저장 파일(persistentDataPath/MomoiProject — 하위 폴더 그대로).\r\n");
        }

        static string BaselineTime()
        {
            try
            {
                var text = File.ReadAllText(Path.Combine(BaselineFolder, "baseline.json"));
                var start = text.IndexOf("\"time\":\"", StringComparison.Ordinal);
                if (start < 0) return "기록";
                start += 8;
                return text.Substring(start, text.IndexOf('"', start) - start);
            }
            catch (Exception)
            {
                return "기록";
            }
        }

        static void Reopen()
        {
            Debug.Log("[MomoiProject] 처음 상태로 되돌렸습니다. Unity를 다시 엽니다.");
            EditorApplication.OpenProject(ProjectRoot);
        }
    }
}
