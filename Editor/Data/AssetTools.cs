using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GBible.EditorTools
{
    public enum AssetKind
    {
        Sprite,
        Audio,
        Font,
        Model,
        Prefab,
        Scene,
        Material,
        Animation,
        UI,
        Video,
        Other,
    }

    // 에셋 관리(미도리 — 개발자가 리소스를 가져오고·바꾸고·정돈한다). 창은 AssetWindow, AI가 부르는 곳은 AssetRequests.
    //   가져오기: 밖의 파일·폴더 → 종류별 폴더(FolderFor)로 복사.
    //   교체: 같은 경로에 새 파일 내용을 덮는다 → GUID가 그대로라 그 에셋을 쓰는 곳(씬·프리팹·DB)이 모두 새 것을 쓴다.
    //   쓰는 곳 · 안 쓰는 것(Assets/_Unused로 옮김, 지우지 않음) · 폴더 정돈(종류별 폴더로 옮김, GUID 그대로).
    public static class AssetTools
    {
        public const string UnusedFolder = ProjectFolders.Dev + "/_Unused";

        // 종류별 기본 폴더(개발자 폴더 안 — 개발자는 Assets/Dev에서만 다룬다. 옮겨 정리해도 된다).
        public static string FolderFor(AssetKind kind)
        {
            switch (kind)
            {
                case AssetKind.Sprite: return ProjectFolders.Dev + "/Art";
                case AssetKind.Audio: return ProjectFolders.Dev + "/Audio";
                case AssetKind.Font: return ProjectFolders.Dev + "/Fonts";
                case AssetKind.Model: return ProjectFolders.Dev + "/Models";
                case AssetKind.Prefab: return ProjectFolders.Dev + "/Prefabs";
                case AssetKind.Scene: return ProjectFolders.Dev + "/Scenes";
                case AssetKind.Material: return ProjectFolders.Dev + "/Materials";
                case AssetKind.Animation: return ProjectFolders.Dev + "/Animations";
                case AssetKind.UI: return ProjectFolders.Dev + "/UI";
                case AssetKind.Video: return ProjectFolders.Dev + "/Video";
                default: return ProjectFolders.Dev + "/Other";
            }
        }

        public static string KindName(AssetKind kind)
        {
            switch (kind)
            {
                case AssetKind.Sprite: return "그림";
                case AssetKind.Audio: return "소리";
                case AssetKind.Font: return "글꼴";
                case AssetKind.Model: return "모델";
                case AssetKind.Prefab: return "프리팹";
                case AssetKind.Scene: return "씬";
                case AssetKind.Material: return "머티리얼";
                case AssetKind.Animation: return "애니메이션";
                case AssetKind.UI: return "UI";
                case AssetKind.Video: return "영상";
                default: return "기타";
            }
        }

        public static AssetKind KindOf(string path)
        {
            switch (Path.GetExtension(path ?? "").ToLowerInvariant())
            {
                case ".png": case ".jpg": case ".jpeg": case ".psd": case ".tga": case ".bmp": case ".gif": case ".tif": case ".tiff": case ".exr": case ".hdr": case ".svg":
                    return AssetKind.Sprite;
                case ".wav": case ".mp3": case ".ogg": case ".aif": case ".aiff": case ".flac":
                    return AssetKind.Audio;
                case ".ttf": case ".otf": case ".fnt":
                    return AssetKind.Font;
                case ".fbx": case ".obj": case ".blend": case ".dae": case ".3ds": case ".gltf": case ".glb":
                    return AssetKind.Model;
                case ".prefab": return AssetKind.Prefab;
                case ".unity": return AssetKind.Scene;
                case ".mat": return AssetKind.Material;
                case ".anim": case ".controller": case ".overridecontroller":
                    return AssetKind.Animation;
                case ".uxml": case ".uss": case ".tss":
                    return AssetKind.UI;
                case ".mp4": case ".webm": case ".mov":
                    return AssetKind.Video;
                default: return AssetKind.Other;
            }
        }

        // 관리 대상이 아닌 곳: 코드·패키지·DB 데이터·Unity가 이름으로 찾는 특수 폴더.
        static readonly string[] Skip =
        {
            ProjectFolders.Root, "Assets/Plugins", "Assets/StreamingAssets",
            "Assets/Editor", "Assets/Gizmos", "Assets/TextMesh Pro", "Assets/Settings", UnusedFolder,
        };

        static bool Managed(string path) =>
            path.StartsWith("Assets/", StringComparison.Ordinal) && !Skip.Any(s => path == s || path.StartsWith(s + "/", StringComparison.Ordinal))
            && !path.Contains("/Resources/") && !path.Contains("/Editor/") && !AssetDatabase.IsValidFolder(path)
            && !path.EndsWith(".cs", StringComparison.Ordinal) && !path.EndsWith(".asmdef", StringComparison.Ordinal) && !path.EndsWith(".asmref", StringComparison.Ordinal);

        // 관리하는 에셋 경로(종류를 주면 그 종류만).
        public static List<string> All(AssetKind? kind = null) =>
            AssetDatabase.GetAllAssetPaths().Where(Managed).Where(p => kind == null || KindOf(p) == kind.Value).OrderBy(p => p, StringComparer.Ordinal).ToList();

        // ── 가져오기 ─────────────────────────────────────────────

        // 밖의 파일·폴더를 종류별 폴더로 복사한다(폴더는 그 안의 파일을 하나씩). 같은 이름이 있으면 뒤에 번호를 붙인다. 만든 에셋 경로를 돌려준다.
        public static List<string> Import(IEnumerable<string> externalPaths, string targetFolder = null)
        {
            var created = new List<string>();
            foreach (var source in externalPaths ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrEmpty(source)) continue;
                var files = Directory.Exists(source) ? Directory.GetFiles(source, "*", SearchOption.AllDirectories) : File.Exists(source) ? new[] { source } : Array.Empty<string>();
                foreach (var file in files)
                {
                    if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                    var folder = targetFolder ?? FolderFor(KindOf(file));
                    Directory.CreateDirectory(folder);
                    var target = Unique(folder + "/" + Path.GetFileName(file));
                    File.Copy(file, target);
                    created.Add(target);
                }
            }
            AssetDatabase.Refresh();
            return created;
        }

        static string Unique(string path)
        {
            if (!File.Exists(path)) return path;
            var folder = Path.GetDirectoryName(path)?.Replace('\\', '/');
            var name = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            for (int i = 1; ; i++)
            {
                var candidate = $"{folder}/{name} {i}{ext}";
                if (!File.Exists(candidate)) return candidate;
            }
        }

        // ── 교체 ─────────────────────────────────────────────────

        // 같은 자리(GUID)에 새 파일 내용을 넣는다. 확장자가 다르면(같은 종류일 때만 — png ↔ jpg) 먼저 이름을 바꿔 GUID를 지킨다. 바뀐 경로를 돌려준다.
        public static string Replace(string assetPath, string externalFile)
        {
            if (string.IsNullOrEmpty(assetPath) || !File.Exists(assetPath)) throw new ArgumentException($"[MomoiProject] 바꿀 에셋이 없습니다: {assetPath}");
            if (string.IsNullOrEmpty(externalFile) || !File.Exists(externalFile)) throw new ArgumentException($"[MomoiProject] 넣을 파일이 없습니다: {externalFile}");
            if (KindOf(assetPath) != KindOf(externalFile))
                throw new ArgumentException($"[MomoiProject] 종류가 다릅니다({KindName(KindOf(assetPath))} ← {KindName(KindOf(externalFile))}).");
            var newExt = Path.GetExtension(externalFile);
            if (!string.Equals(Path.GetExtension(assetPath), newExt, StringComparison.OrdinalIgnoreCase))
            {
                var moved = Path.ChangeExtension(assetPath, newExt).Replace('\\', '/');
                var error = AssetDatabase.MoveAsset(assetPath, moved);
                if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException("[MomoiProject] " + error);
                assetPath = moved;
            }
            File.Copy(externalFile, assetPath, overwrite: true);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            AssetRequests.MarkDone(assetPath);
            return assetPath;
        }

        // ── 쓰는 곳 ──────────────────────────────────────────────

        static Dictionary<string, List<string>> _usedBy;

        static AssetTools()
        {
            EditorApplication.projectChanged += () => _usedBy = null;
        }

        // 이 에셋을 가리키는 에셋(씬·프리팹·머티리얼·DB 항목 …).
        public static IReadOnlyList<string> UsedBy(string assetPath)
        {
            if (_usedBy == null) BuildUsedBy();
            return _usedBy.TryGetValue(assetPath, out var list) ? list : (IReadOnlyList<string>)Array.Empty<string>();
        }

        static void BuildUsedBy()
        {
            _usedBy = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var path in AssetDatabase.GetAllAssetPaths())
            {
                if (!path.StartsWith("Assets/", StringComparison.Ordinal) || AssetDatabase.IsValidFolder(path) || path.EndsWith(".cs", StringComparison.Ordinal)) continue;
                foreach (var dependency in AssetDatabase.GetDependencies(path, false))
                {
                    if (dependency == path) continue;
                    if (!_usedBy.TryGetValue(dependency, out var users)) _usedBy[dependency] = users = new List<string>();
                    users.Add(path);
                }
            }
        }

        // ── 안 쓰는 것 ───────────────────────────────────────────

        // 빌드에 든 씬·Resources·DB 데이터·UI·넣을 것 목록에서 닿지 않는 그림·소리·글꼴·모델·프리팹·머티리얼·애니메이션·영상.
        public static List<string> Unused()
        {
            var roots = new List<string>();
            roots.AddRange(EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path));
            roots.AddRange(AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/", StringComparison.Ordinal)
                && (p.Contains("/Resources/") || p.StartsWith(ProjectFolders.Root + "/", StringComparison.Ordinal))));
            roots.AddRange(AssetRequests.Load().Select(r => r.path));
            var reachable = new HashSet<string>(AssetDatabase.GetDependencies(roots.Where(File.Exists).ToArray(), true), StringComparer.Ordinal);
            var kinds = new[] { AssetKind.Sprite, AssetKind.Audio, AssetKind.Font, AssetKind.Model, AssetKind.Prefab, AssetKind.Material, AssetKind.Animation, AssetKind.Video };
            return All().Where(p => kinds.Contains(KindOf(p)) && !reachable.Contains(p)).ToList();
        }

        // 안 쓰는 에셋을 Assets/_Unused 아래로 옮긴다(지우지 않는다 — 다시 쓰려면 옮겨 오면 된다). 옮긴 수.
        public static int MoveToUnused(IEnumerable<string> paths)
        {
            var count = 0;
            foreach (var path in paths)
            {
                var target = UnusedFolder + path.Substring("Assets".Length);
                if (MoveTo(path, target)) count++;
            }
            return count;
        }

        // ── 폴더 정돈 ────────────────────────────────────────────

        // 종류별 폴더 밖에 있는 에셋과 옮길 곳.
        public static List<(string path, string target)> Misplaced()
        {
            var list = new List<(string, string)>();
            foreach (var path in All())
            {
                var kind = KindOf(path);
                if (kind == AssetKind.Other) continue;
                var folder = FolderFor(kind);
                if (path.StartsWith(folder + "/", StringComparison.Ordinal)) continue;
                list.Add((path, folder + "/" + Path.GetFileName(path)));
            }
            return list;
        }

        public static int Tidy(IEnumerable<(string path, string target)> moves)
        {
            var count = 0;
            foreach (var (path, target) in moves)
                if (MoveTo(path, target)) count++;
            return count;
        }

        static bool MoveTo(string path, string target)
        {
            var folder = Path.GetDirectoryName(target)?.Replace('\\', '/');
            EnsureFolder(folder);
            target = Unique(target);
            var error = AssetDatabase.MoveAsset(path, target);
            if (string.IsNullOrEmpty(error)) return true;
            Debug.LogWarning($"[MomoiProject] {path} → {target} 옮기지 못했습니다: {error}");
            return false;
        }

        // Assets/A/B 같은 폴더를 AssetDatabase로 만든다(meta가 함께 생긴다).
        public static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;
            var parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
