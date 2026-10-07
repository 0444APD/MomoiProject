using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GBible.Data;
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
    //   쓰는 곳 · 안 쓰는 것(Assets/Dev/_Unused로 옮김, 지우지 않음) · 폴더 정돈(종류별 폴더로 옮김, GUID 그대로).
    //   안 쓰는 것·정돈은 개발자 폴더(Assets/Dev) 안에서만 옮긴다 — 시스템 폴더(Assets/MomoiProject)·패키지·Plugins·에셋 스토어 폴더는 건드리지 않는다.
    //   옮긴 것은 원래 경로 → 새 경로를 MomoiProjectBackups/<시각>-tidy|unused/moves.tsv에 남기고, UndoMoves로 되돌린다.
    public static class AssetTools
    {
        public const string UnusedFolder = ProjectFolders.Dev + "/_Unused";
        const string MoveLogName = "moves.tsv";
        const string UndoneLogName = "moves-undone.tsv";

        // 옮긴 기록을 남길 곳(프로젝트 폴더 바로 아래 MomoiProjectBackups — 시험이 임시 자리로 바꾼다).
        internal static string BackupRoot = ProjectReset.BackupFolder;

        // 종류마다 확장자(대소문자는 가리지 않는다).
        static readonly (AssetKind kind, string[] extensions)[] KindExtensions =
        {
            (AssetKind.Sprite, new[] { ".png", ".jpg", ".jpeg", ".psd", ".tga", ".bmp", ".gif", ".tif", ".tiff", ".exr", ".hdr", ".svg" }),
            (AssetKind.Audio, new[] { ".wav", ".mp3", ".ogg", ".aif", ".aiff", ".flac" }),
            (AssetKind.Font, new[] { ".ttf", ".otf", ".fnt" }),
            (AssetKind.Model, new[] { ".fbx", ".obj", ".blend", ".dae", ".3ds", ".gltf", ".glb" }),
            (AssetKind.Prefab, new[] { ".prefab" }),
            (AssetKind.Scene, new[] { ".unity" }),
            (AssetKind.Material, new[] { ".mat" }),
            (AssetKind.Animation, new[] { ".anim", ".controller", ".overridecontroller" }),
            (AssetKind.UI, new[] { ".uxml", ".uss", ".tss" }),
            (AssetKind.Video, new[] { ".mp4", ".webm", ".mov" }),
        };

        static readonly Dictionary<string, AssetKind> KindByExtension = KindExtensions
            .SelectMany(k => k.extensions.Select(e => (extension: e, k.kind)))
            .ToDictionary(p => p.extension, p => p.kind, StringComparer.OrdinalIgnoreCase);

        // 종류별 폴더 이름(개발자 폴더 바로 아래).
        static string FolderName(AssetKind kind)
        {
            switch (kind)
            {
                case AssetKind.Sprite: return "Art";
                case AssetKind.Audio: return "Audio";
                case AssetKind.Font: return "Fonts";
                case AssetKind.Model: return "Models";
                case AssetKind.Prefab: return "Prefabs";
                case AssetKind.Scene: return "Scenes";
                case AssetKind.Material: return "Materials";
                case AssetKind.Animation: return "Animations";
                case AssetKind.UI: return "UI";
                case AssetKind.Video: return "Video";
                default: return "Other";
            }
        }

        static readonly HashSet<string> FolderNames = new HashSet<string>(
            Enum.GetValues(typeof(AssetKind)).Cast<AssetKind>().Select(FolderName), StringComparer.OrdinalIgnoreCase);

        // 종류별 기본 폴더(개발자 폴더 안 — 개발자는 Assets/Dev에서만 다룬다. 옮겨 정리해도 된다).
        public static string FolderFor(AssetKind kind) => FolderIn(ProjectFolders.Dev, kind);

        static string FolderIn(string root, AssetKind kind) => root + "/" + FolderName(kind);

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

        public static AssetKind KindOf(string path) =>
            KindByExtension.TryGetValue(Path.GetExtension(path ?? ""), out var kind) ? kind : AssetKind.Other;

        // 그 종류의 확장자(점 없이 — 파일 고르기 창의 거르기에 쓴다). 기타는 빈 배열.
        public static string[] ExtensionsOf(AssetKind kind) =>
            KindExtensions.Where(k => k.kind == kind).SelectMany(k => k.extensions).Select(e => e.Substring(1)).ToArray();

        // 관리 대상이 아닌 곳: 코드·패키지·DB 데이터·Unity가 이름으로 찾는 특수 폴더.
        static readonly string[] Skip =
        {
            ProjectFolders.Root, "Assets/Plugins", "Assets/StreamingAssets",
            "Assets/Editor", "Assets/Gizmos", "Assets/TextMesh Pro", "Assets/Settings", UnusedFolder,
        };

        static bool Managed(string path) => Managed(path, AssetDatabase.IsValidFolder);

        static bool Managed(string path, Func<string, bool> isFolder) =>
            path.StartsWith("Assets/", StringComparison.Ordinal) && !Skip.Any(s => Under(path, s))
            && !path.Contains("/Resources/") && !path.Contains("/Editor/") && !isFolder(path)
            && !path.EndsWith(".cs", StringComparison.Ordinal) && !path.EndsWith(".asmdef", StringComparison.Ordinal) && !path.EndsWith(".asmref", StringComparison.Ordinal);

        static bool Under(string path, string folder) =>
            path != null && (path == folder || path.StartsWith(folder + "/", StringComparison.Ordinal));

        static string Parent(string path)
        {
            var slash = path.LastIndexOf('/');
            return slash > 0 ? path.Substring(0, slash) : null;
        }

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
            var kind = KindOf(assetPath);
            if (kind != KindOf(externalFile))
                throw new ArgumentException($"[MomoiProject] 종류가 다릅니다({KindName(kind)} ← {KindName(KindOf(externalFile))}).");
            var newExt = Path.GetExtension(externalFile);
            var sameExt = string.Equals(Path.GetExtension(assetPath), newExt, StringComparison.OrdinalIgnoreCase);
            // 기타(.asset·.json …)는 확장자가 곧 형식이라 같은 확장자로만 바꾼다.
            if (kind == AssetKind.Other && !sameExt)
                throw new ArgumentException($"[MomoiProject] 확장자가 다릅니다({Path.GetExtension(assetPath)} ← {newExt}). 기타 에셋은 같은 확장자로만 바꿉니다.");
            if (!sameExt)
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

        // ── 그대로 둘 자리 ───────────────────────────────────────
        // 개발자 폴더(root) 안이라도 정돈·안 쓰는 것이 옮기지 않는 곳:
        //   코드(.cs·.asmdef·.dll …)가 든 폴더 — 가져온 패키지(에셋 스토어 등)로 본다. 종류별 폴더 자체는 빼고 그 아래 폴더부터.
        //   씬 옆 같은 이름 폴더(Level1.unity 옆 Level1/) — 라이트맵·내비메시처럼 Unity가 굽는 것.
        //   Resources·Editor·Plugins·Gizmos·StreamingAssets 폴더(Unity가 이름으로 찾는다) · Addressables 폴더 항목 안 · _Unused.
        static readonly string[] CodeExtensions = { ".cs", ".asmdef", ".asmref", ".dll", ".so", ".dylib", ".a", ".aar", ".jar", ".jslib" };
        static readonly string[] SpecialFolders = { "Resources", "Editor", "Editor Default Resources", "Plugins", "Gizmos", "StreamingAssets" };

        sealed class KeepAsIs
        {
            readonly string _root;
            readonly HashSet<string> _folders = new HashSet<string>(StringComparer.Ordinal);

            public KeepAsIs(string root, IEnumerable<string> paths, IEnumerable<string> addressableFolders)
            {
                _root = root;
                _folders.Add(root + "/_Unused");
                foreach (var path in paths)
                {
                    if (!Under(path, root) || path == root) continue;
                    if (CodeExtensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                    {
                        for (var folder = Parent(path); folder != null && folder.Length > root.Length; folder = Parent(folder))
                            if (!(Parent(folder) == root && FolderNames.Contains(folder.Substring(root.Length + 1)))) _folders.Add(folder);
                    }
                    else if (path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                    {
                        _folders.Add(path.Substring(0, path.Length - ".unity".Length));
                    }
                }
                foreach (var folder in addressableFolders ?? Enumerable.Empty<string>())
                    if (!string.IsNullOrEmpty(folder)) _folders.Add(folder.TrimEnd('/'));
            }

            public bool Covers(string path)
            {
                if (!Under(path, _root) || path == _root) return true;
                foreach (var special in SpecialFolders)
                    if (path.IndexOf("/" + special + "/", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                for (var folder = Parent(path); folder != null && folder.Length >= _root.Length; folder = Parent(folder))
                    if (_folders.Contains(folder)) return true;
                return false;
            }
        }

        // ── Addressables ─────────────────────────────────────────

        const string AddressablesFolder = "Assets/AddressableAssetsData";
        static readonly Regex AddressableGuid = new Regex(@"m_GUID:\s*([0-9a-fA-F]{32})");

        // Addressables를 쓰면(Assets/AddressableAssetsData) 그룹 파일에 적힌 항목(에셋·폴더)의 경로. 쓰지 않으면 빈 목록.
        // 패키지를 참조하지 않고 그룹 파일(텍스트 직렬화)을 읽는다.
        internal static List<string> AddressableEntries()
        {
            var entries = new List<string>();
            if (!Directory.Exists(AddressablesFolder)) return entries;
            foreach (var file in Directory.GetFiles(AddressablesFolder, "*.asset", SearchOption.AllDirectories))
            {
                string text;
                try { text = File.ReadAllText(file); }
                catch (Exception) { continue; }
                foreach (Match match in AddressableGuid.Matches(text))
                {
                    var path = AssetDatabase.GUIDToAssetPath(match.Groups[1].Value);
                    if (!string.IsNullOrEmpty(path) && path.StartsWith("Assets/", StringComparison.Ordinal)) entries.Add(path);
                }
            }
            return entries.Distinct(StringComparer.Ordinal).ToList();
        }

        // ── 안 쓰는 것 ───────────────────────────────────────────

        static readonly AssetKind[] UnusedKinds = { AssetKind.Sprite, AssetKind.Audio, AssetKind.Font, AssetKind.Model, AssetKind.Prefab, AssetKind.Material, AssetKind.Animation, AssetKind.Video };

        // 개발자 폴더(Assets/Dev) 안에서, 뿌리(UnusedRoots)에서 닿지 않는 그림·소리·글꼴·모델·프리팹·머티리얼·애니메이션·영상.
        public static List<string> Unused()
        {
            var paths = AssetDatabase.GetAllAssetPaths();
            var addressable = AddressableEntries();
            var roots = UnusedRoots(paths, EditorBuildSettings.scenes.Select(s => s.path), AssetDatabase.GetMainAssetTypeAtPath, addressable, AssetDatabase.IsValidFolder,
                AssetRequests.Load().Select(AssetRequests.PathOf));
            var reachable = new HashSet<string>(AssetDatabase.GetDependencies(roots.Where(File.Exists).Distinct(StringComparer.Ordinal).ToArray(), true), StringComparer.Ordinal);
            return UnusedIn(ProjectFolders.Dev, paths, AssetDatabase.IsValidFolder, reachable, addressable.Where(AssetDatabase.IsValidFolder));
        }

        // 쓰는 것의 뿌리: 빌드 목록의 씬(꺼 둔 것도) · 개발자 폴더 안의 모든 씬(빌드 목록에 아직 없어도, _Unused 빼고) · 시스템 폴더(DB 데이터·UI·AI 씬 Assets/MomoiProject/Scenes) ·
        // Resources 안 · 시스템 폴더 밖의 DB 항목 · Addressables 항목(폴더면 그 안 모두) · 넣을 것 목록의 지금 경로.
        internal static List<string> UnusedRoots(IEnumerable<string> paths, IEnumerable<string> buildScenes, Func<string, Type> typeOf,
                                                 IEnumerable<string> addressable, Func<string, bool> isFolder, IEnumerable<string> requests)
        {
            var all = paths as IList<string> ?? paths.ToList();
            var roots = new List<string>();
            roots.AddRange((buildScenes ?? Enumerable.Empty<string>()).Where(p => !string.IsNullOrEmpty(p)));
            foreach (var path in all)
            {
                if (!path.StartsWith("Assets/", StringComparison.Ordinal)) continue;
                if (path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) && Under(path, ProjectFolders.Dev) && !Under(path, UnusedFolder)) roots.Add(path);
                else if (Under(path, ProjectFolders.Root) || path.Contains("/Resources/")) roots.Add(path);
                else if (path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) && typeof(GameData).IsAssignableFrom(typeOf(path))) roots.Add(path);
            }
            foreach (var entry in addressable ?? Enumerable.Empty<string>())
            {
                if (isFolder(entry)) roots.AddRange(all.Where(p => p.StartsWith(entry + "/", StringComparison.Ordinal)));
                else roots.Add(entry);
            }
            roots.AddRange((requests ?? Enumerable.Empty<string>()).Where(p => !string.IsNullOrEmpty(p)));
            return roots;
        }

        internal static List<string> UnusedIn(string root, IEnumerable<string> paths, Func<string, bool> isFolder, ICollection<string> reachable, IEnumerable<string> addressableFolders)
        {
            var all = paths as IList<string> ?? paths.ToList();
            var keep = new KeepAsIs(root, all, addressableFolders);
            return all.Where(p => Under(p, root) && Managed(p, isFolder) && UnusedKinds.Contains(KindOf(p)) && !reachable.Contains(p) && !keep.Covers(p))
                .OrderBy(p => p, StringComparer.Ordinal).ToList();
        }

        // 안 쓰는 에셋을 Assets/Dev/_Unused 아래 원래 경로 그대로 옮긴다(지우지 않는다 — 다시 쓰려면 옮겨 오면 된다). 개발자 폴더 밖은 옮기지 않는다. 옮긴 수.
        public static int MoveToUnused(IEnumerable<string> paths) => MoveToUnused(ProjectFolders.Dev, paths, out _);

        internal static int MoveToUnused(string root, IEnumerable<string> paths, out string log)
        {
            var unused = root + "/_Unused";
            var moves = new List<(string path, string target)>();
            foreach (var path in paths ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrEmpty(path)) continue;
                if (!Under(path, root) || Under(path, unused))
                {
                    Debug.LogWarning($"[MomoiProject] {path}: 개발자 폴더({root}) 밖이거나 이미 _Unused 안이라 옮기지 않았습니다.");
                    continue;
                }
                moves.Add((path, unused + path.Substring("Assets".Length)));
            }
            return MoveAll(moves, "unused", "안 쓰는 것 옮기기", out log);
        }

        // ── 폴더 정돈 ────────────────────────────────────────────

        // 개발자 폴더(Assets/Dev) 안에서 종류별 폴더 밖에 있는 에셋과 옮길 곳. 하위 폴더 모양은 그대로 둔다:
        //   Assets/Dev/Cards/Fire/a.png → Assets/Dev/Art/Cards/Fire/a.png (다른 종류 폴더 안이면 그 폴더 이름만 뺀다 — Assets/Dev/Audio/Icons/a.png → Assets/Dev/Art/Icons/a.png).
        // 그대로 둘 자리(KeepAsIs)는 빼고, 기타 종류는 옮기지 않는다.
        public static List<(string path, string target)> Misplaced() =>
            PlanTidy(ProjectFolders.Dev, AssetDatabase.GetAllAssetPaths(), AssetDatabase.IsValidFolder, AddressableEntries().Where(AssetDatabase.IsValidFolder));

        internal static List<(string path, string target)> PlanTidy(string root, IEnumerable<string> paths, Func<string, bool> isFolder, IEnumerable<string> addressableFolders)
        {
            var all = paths as IList<string> ?? paths.ToList();
            var keep = new KeepAsIs(root, all, addressableFolders);
            var list = new List<(string, string)>();
            foreach (var path in all.Where(p => Under(p, root) && Managed(p, isFolder)).OrderBy(p => p, StringComparer.Ordinal))
            {
                var kind = KindOf(path);
                if (kind == AssetKind.Other || keep.Covers(path)) continue;
                if (path.StartsWith(FolderIn(root, kind) + "/", StringComparison.OrdinalIgnoreCase)) continue;
                list.Add((path, TidyTarget(root, path, kind)));
            }
            return list;
        }

        // 정돈할 곳: 종류 폴더 + root 기준 상대 경로(맨 앞이 다른 종류 폴더 이름이면 그것은 뺀다).
        internal static string TidyTarget(string root, string path, AssetKind kind)
        {
            var relative = path.Substring(root.Length + 1);
            var slash = relative.IndexOf('/');
            if (slash > 0 && FolderNames.Contains(relative.Substring(0, slash))) relative = relative.Substring(slash + 1);
            return FolderIn(root, kind) + "/" + relative;
        }

        // 정돈한다(개발자 폴더 안끼리만). 옮긴 수 — 옮긴 목록은 MomoiProjectBackups/<시각>-tidy/moves.tsv.
        public static int Tidy(IEnumerable<(string path, string target)> moves) => Tidy(ProjectFolders.Dev, moves, out _);

        internal static int Tidy(string root, IEnumerable<(string path, string target)> moves, out string log)
        {
            var allowed = new List<(string path, string target)>();
            foreach (var (path, target) in moves ?? Enumerable.Empty<(string, string)>())
            {
                if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(target)) continue;
                if (!Under(path, root) || !Under(target, root) || Under(path, root + "/_Unused"))
                {
                    Debug.LogWarning($"[MomoiProject] {path} → {target}: 개발자 폴더({root}) 밖은 정돈하지 않습니다.");
                    continue;
                }
                allowed.Add((path, target));
            }
            return MoveAll(allowed, "tidy", "폴더 정돈", out log);
        }

        // ── 옮기기 · 기록 · 되돌리기 ─────────────────────────────

        // 하나씩 옮기고(GUID 그대로) 옮긴 것을 기록 파일에 남긴다. 옮긴 수.
        static int MoveAll(List<(string path, string target)> moves, string tag, string what, out string log)
        {
            var done = new List<(string from, string to)>();
            try
            {
                foreach (var (path, target) in moves)
                {
                    var moved = MoveTo(path, target);
                    if (moved != null) done.Add((path, moved));
                }
            }
            finally
            {
                log = WriteMoveLog(tag, what, done);
            }
            return done.Count;
        }

        // 옮긴 곳(같은 이름이 있으면 번호를 붙인 곳)을 돌려준다. 못 옮기면 null.
        static string MoveTo(string path, string target)
        {
            var folder = Path.GetDirectoryName(target)?.Replace('\\', '/');
            EnsureFolder(folder);
            target = Unique(target);
            var error = AssetDatabase.MoveAsset(path, target);
            if (string.IsNullOrEmpty(error)) return target;
            Debug.LogWarning($"[MomoiProject] {path} → {target} 옮기지 못했습니다: {error}");
            return null;
        }

        // MomoiProjectBackups/<시각>-<tag>/moves.tsv — 줄마다 "원래 경로<TAB>새 경로". 남긴 파일 경로(옮긴 것이 없으면 null).
        static string WriteMoveLog(string tag, string what, List<(string from, string to)> moves)
        {
            if (moves.Count == 0) return null;
            try
            {
                var basePath = Path.Combine(BackupRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + tag).Replace('\\', '/');
                var folder = basePath;
                for (int i = 2; Directory.Exists(folder); i++) folder = basePath + "-" + i;
                Directory.CreateDirectory(folder);
                var lines = new List<string>
                {
                    $"# MomoiProject 에셋 관리 — {what} ({DateTime.Now:yyyy-MM-dd HH:mm:ss}, {moves.Count}개)",
                    "# 줄마다 원래 경로<TAB>새 경로. 되돌리기: 에셋 관리 창의 '마지막 … 되돌리기', 또는 Unity Project 창에서 새 경로의 에셋을 원래 경로로 옮긴다(GUID 그대로).",
                };
                lines.AddRange(moves.Select(m => m.from + "\t" + m.to));
                var log = folder + "/" + MoveLogName;
                File.WriteAllLines(log, lines, new UTF8Encoding(false));
                return log;
            }
            catch (Exception e)
            {
                // 기록을 못 남겨도 무엇을 옮겼는지는 콘솔에 남긴다(되돌릴 수 있게).
                Debug.LogWarning($"[MomoiProject] 옮긴 기록을 {BackupRoot}에 남기지 못했습니다({e.Message}). 옮긴 것:\n" + string.Join("\n", moves.Select(m => $"{m.from} → {m.to}")));
                return null;
            }
        }

        // 가장 최근의 아직 되돌리지 않은 기록(tag = "tidy" · "unused"). 없으면 null.
        public static string LastMoveLog(string tag)
        {
            if (string.IsNullOrEmpty(tag) || !Directory.Exists(BackupRoot)) return null;
            return Directory.GetDirectories(BackupRoot, "*-" + tag + "*")
                .Select(d => Path.Combine(d, MoveLogName).Replace('\\', '/'))
                .Where(File.Exists)
                .OrderBy(p => p, StringComparer.Ordinal)
                .LastOrDefault();
        }

        // 기록대로 되돌린다(새 경로 → 원래 경로, 나중에 옮긴 것부터, GUID 그대로). 새 경로에 없거나 원래 자리에 다른 파일이 생긴 것은 건너뛰고 알린다.
        // 다 돌면 기록 파일 이름을 moves-undone.tsv로 바꾼다(같은 기록을 다시 되돌리지 않게 — 지우지 않는다). 되돌린 수.
        public static int UndoMoves(string log)
        {
            if (string.IsNullOrEmpty(log) || !File.Exists(log)) return 0;
            var moves = File.ReadAllLines(log)
                .Where(l => l.Length > 0 && l[0] != '#')
                .Select(l => l.Split('\t'))
                .Where(p => p.Length == 2 && p[0].Length > 0 && p[1].Length > 0)
                .Reverse()
                .ToList();
            var count = 0;
            var skipped = new List<string>();
            foreach (var parts in moves)
            {
                var original = parts[0];
                var current = parts[1];
                if (!File.Exists(current) || File.Exists(original))
                {
                    skipped.Add($"  {current} → {original} ({(File.Exists(original) ? "원래 자리에 다른 파일이 있습니다" : "새 경로에 없습니다")})");
                    continue;
                }
                EnsureFolder(Path.GetDirectoryName(original)?.Replace('\\', '/'));
                var error = AssetDatabase.MoveAsset(current, original);
                if (string.IsNullOrEmpty(error)) count++;
                else skipped.Add($"  {current} → {original} ({error})");
            }
            try
            {
                File.Move(log, Path.Combine(Path.GetDirectoryName(log) ?? "", UndoneLogName));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MomoiProject] {log}의 이름을 바꾸지 못했습니다(다음에 또 되돌리기로 보일 수 있습니다): {e.Message}");
            }
            if (skipped.Count > 0) Debug.LogWarning($"[MomoiProject] 되돌리지 못한 것 {skipped.Count}개(그대로 둠):\n" + string.Join("\n", skipped));
            return count;
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
