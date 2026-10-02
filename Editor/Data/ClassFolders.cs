using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace GBible.EditorTools
{
    // 도감 › 클래스·이벤트 탭의 그룹 = Assets/Code 아래의 폴더(동기화).
    //   그룹 "전투/버프" = 폴더 Assets/Code/전투/버프. 클래스의 그룹 = 그 스크립트 파일이 있는 폴더.
    //   프로젝트 창에서 폴더를 만들거나 옮겨도 그대로 보이고, 도감에서 그룹을 만들거나 지우면 저장(Ctrl+S)할 때 폴더가 생기고 지워진다.
    // 이 라이브러리 폴더(Assets/Code/System)는 그룹이 아니다.
    public static class ClassFolders
    {
        public const string Root = "Assets/Code";
        // 이 이름의 최상위 폴더는 그룹으로 쓰지 않는다(라이브러리).
        public static readonly string[] Reserved = { "System" };
        // Code 폴더 밖에 있는 클래스의 그룹 표시.
        public const string Outside = "$outside";

        public static string FolderOf(string group) => string.IsNullOrEmpty(group) ? Root : Root + "/" + group;

        public static string Parent(string group)
        {
            var cut = (group ?? "").LastIndexOf('/');
            return cut < 0 ? "" : group.Substring(0, cut);
        }

        public static string Name(string group)
        {
            var cut = (group ?? "").LastIndexOf('/');
            return cut < 0 ? group ?? "" : group.Substring(cut + 1);
        }

        public static string Join(string parent, string name) => string.IsNullOrEmpty(parent) ? name : parent + "/" + name;

        public static bool IsUnder(string group, string ancestor) =>
            group == ancestor || (group ?? "").StartsWith(ancestor + "/", StringComparison.Ordinal);

        static bool IsReserved(string group)
        {
            var top = (group ?? "").Split('/')[0];
            return Reserved.Any(r => string.Equals(r, top, StringComparison.OrdinalIgnoreCase));
        }

        // ── 디스크의 폴더 ─────────────────────────────────────

        public static List<string> OnDisk()
        {
            var result = new List<string>();
            if (!Directory.Exists(Root)) return result;
            foreach (var dir in Directory.GetDirectories(Root, "*", SearchOption.AllDirectories))
            {
                var group = dir.Replace('\\', '/').Substring(Root.Length + 1);
                if (!IsReserved(group)) result.Add(group);
            }
            return result;
        }

        // ── 적용 전 변경을 반영한 모습 ─────────────────────────

        // 저장 전 폴더 변경(지우기·옮기기)을 from번째 변경부터 차례로 적용한 위치. 지울 그룹의 안쪽은 한 단계 위로 올라간다(클래스·하위 그룹은 지우지 않는다).
        // 디스크에서 읽은 위치는 처음(0)부터, 변경이 적어 둔 위치는 그 변경 다음부터 적용한다(그 뒤에 일어난 변경만 반영).
        public static string Map(string group, int from = 0)
        {
            if (group == null || group == Outside) return group;
            var ops = ClassStaging.All;
            for (int i = Math.Max(0, from); i < ops.Count; i++)
            {
                var op = ops[i];
                if (op.op == ClassOp.DeleteGroup)
                {
                    if (group == op.group) group = Parent(op.group);
                    else if (IsUnder(group, op.group)) group = Join(Parent(op.group), group.Substring(op.group.Length + 1));
                }
                else if (op.op == ClassOp.MoveGroup) group = Rebase(group, op.group, op.newName);
            }
            return group;
        }

        // group이 from 안쪽이면 to 아래로 옮긴 경로.
        public static string Rebase(string group, string from, string to)
        {
            if (group == from) return to;
            return IsUnder(group, from) && !string.IsNullOrEmpty(from) ? Join(to, group.Substring(from.Length + 1)) : group;
        }

        // 그룹 목록(저장 전 만들기·지우기 반영, 이름순).
        public static List<string> Groups()
        {
            // 디스크의 폴더에 저장 전 변경을 차례로 적용한다(지우기: 그 그룹은 빠지고 안쪽은 한 단계 위로).
            var set = new List<string>(OnDisk());
            foreach (var op in ClassStaging.All)
            {
                if (op.op == ClassOp.CreateGroup) set.Add(op.group);
                else if (op.op == ClassOp.DeleteGroup)
                    set = set.Where(g => g != op.group)
                             .Select(g => IsUnder(g, op.group) ? Join(Parent(op.group), g.Substring(op.group.Length + 1)) : g)
                             .ToList();
                else if (op.op == ClassOp.MoveGroup)
                    set = set.Select(g => Rebase(g, op.group, op.newName)).ToList();
            }
            return set.Where(g => !string.IsNullOrEmpty(g)).Distinct().OrderBy(g => g, StringComparer.Ordinal).ToList();
        }

        public static bool Exists(string group) => string.IsNullOrEmpty(group) || Groups().Contains(group);

        public static bool CanCreate(string parent, string name, out string error)
        {
            error = null;
            name = (name ?? "").Trim();
            if (name.Length == 0) error = "폴더 이름을 입력하세요.";
            else if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains("/") || name.StartsWith(".", StringComparison.Ordinal))
                error = "폴더 이름으로 쓸 수 없는 글자가 있습니다.";
            else if (string.IsNullOrEmpty(parent) && IsReserved(name)) error = $"'{name}'은(는) 라이브러리 폴더 이름이라 쓸 수 없습니다.";
            else if (Groups().Any(g => string.Equals(g, Join(parent, name), StringComparison.OrdinalIgnoreCase))) error = $"'{name}' 폴더가 이미 있습니다.";
            return error == null;
        }

        // 클래스의 그룹: 저장 전 옮기기 → 파일이 있는 폴더(지울 그룹은 위로). Code 밖이면 Outside, 파일을 못 찾으면 null.
        public static string GroupOf(string fullName)
        {
            var ops = ClassStaging.All;
            for (int i = ops.Count - 1; i >= 0; i--)
                if ((ops[i].op == ClassOp.MoveClass || ops[i].op == ClassOp.Create) && ops[i].type == fullName)
                    return Map(ops[i].group ?? "", i + 1);
            var path = PathOf(fullName);
            if (path == null) return null;
            var folder = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "";
            if (folder == Root) return "";
            if (!folder.StartsWith(Root + "/", StringComparison.Ordinal)) return Outside;
            var group = folder.Substring(Root.Length + 1);
            return IsReserved(group) ? Outside : Map(group);
        }

        // ── 클래스 → 파일 ─────────────────────────────────────
        // 스크립트 이름 = 클래스 이름인 파일을 디스크에서 찾아 기억한다(프로젝트 파일이 바뀌면 다시 찾는다).
        // 에셋을 불러오지 않는다(LoadAssetAtPath): 저장 직후 스크립트가 다시 가져와지는 중에 에셋을 불러오면
        // Unity가 그 가져오기를 기다리며 멈출 수 있다(2026-10-01 저장 후 멈춤).

        static Dictionary<string, List<string>> _files;
        static readonly Dictionary<string, string> Resolved = new Dictionary<string, string>(StringComparer.Ordinal);

        public static string PathOf(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return null;
            if (_files == null)
            {
                _files = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                if (Directory.Exists(Root))
                    foreach (var file in Directory.GetFiles(Root, "*.cs", SearchOption.AllDirectories))
                    {
                        var path = file.Replace('\\', '/');
                        if (IsReserved(Path.GetDirectoryName(path).Replace('\\', '/').Substring(Root.Length).TrimStart('/'))) continue;
                        var name = Path.GetFileNameWithoutExtension(path);
                        if (!_files.TryGetValue(name, out var list)) _files[name] = list = new List<string>();
                        list.Add(path);
                    }
                Resolved.Clear();
                EditorApplication.projectChanged -= Forget;
                EditorApplication.projectChanged += Forget;
            }
            if (Resolved.TryGetValue(fullName, out var found)) return found.Length == 0 ? null : found;

            var className = fullName.Substring(fullName.LastIndexOfAny(new[] { '.', '+' }) + 1);
            var ns = fullName.Contains(".") ? fullName.Substring(0, fullName.LastIndexOf('.')) : "";
            if (_files.TryGetValue(className, out var candidates))
            {
                // 같은 이름 파일이 여럿이면 네임스페이스·클래스 선언이 맞는 것.
                found = candidates.Count == 1 ? candidates[0] : candidates.FirstOrDefault(p => Declares(p, ns, className));
            }
            // Code 밖이거나 파일 이름이 클래스 이름과 다르면 한 클래스씩 찾는다(못 찾은 것도 기억한다).
            found ??= ClassCatalog.FileOf(fullName) ?? "";
            Resolved[fullName] = found;
            return found.Length == 0 ? null : found;
        }

        static bool Declares(string path, string ns, string className)
        {
            try
            {
                var text = File.ReadAllText(path);
                return (ns.Length == 0 || text.Contains("namespace " + ns)) && ClassCode.TryFindBody(text, className, out _, out _);
            }
            catch (IOException)
            {
                return false;
            }
        }

        public static void Forget()
        {
            _files = null;
            Resolved.Clear();
        }

        // ── 적용(저장할 때) ───────────────────────────────────

        // 폴더를 Unity에 만든다(중간 폴더 포함).
        public static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            var parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        // 폴더를 통째로 옮긴다(안의 파일·하위 폴더째로). 옮기지 못하면 problems에.
        public static void MoveFolder(string group, string to, List<string> problems)
        {
            var from = FolderOf(group);
            var target = FolderOf(to);
            if (!AssetDatabase.IsValidFolder(from))
            {
                problems.Add($"폴더 '{group}'을(를) 찾지 못해 옮기지 못했습니다.");
                return;
            }
            if (AssetDatabase.IsValidFolder(target))
            {
                problems.Add($"'{target}' 폴더가 이미 있어 '{group}'을(를) 옮기지 못했습니다.");
                return;
            }
            EnsureFolder(Path.GetDirectoryName(target)?.Replace('\\', '/'));
            var error = AssetDatabase.MoveAsset(from, target);
            if (!string.IsNullOrEmpty(error)) problems.Add($"폴더 '{group}'을(를) '{target}'(으)로 옮기지 못했습니다({error}).");
            Forget();
        }

        // 그룹 폴더를 지운다: 안의 파일·하위 폴더를 한 단계 위로 옮긴 뒤 빈 폴더를 지운다. 옮기지 못한 것은 problems에.
        public static void DeleteFolder(string group, List<string> problems)
        {
            var folder = FolderOf(group);
            if (!AssetDatabase.IsValidFolder(folder)) return;
            var parent = FolderOf(Parent(group));
            foreach (var child in Directory.GetFileSystemEntries(folder))
            {
                var path = child.Replace('\\', '/');
                if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                var target = parent + "/" + Path.GetFileName(path);
                var error = AssetDatabase.MoveAsset(path, target);
                if (!string.IsNullOrEmpty(error)) problems.Add($"그룹 '{group}' 지우기: '{path}'를 옮기지 못했습니다({error}).");
            }
            if (Directory.GetFileSystemEntries(folder).All(e => e.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)))
                AssetDatabase.DeleteAsset(folder);
            else problems.Add($"그룹 '{group}': 옮기지 못한 파일이 있어 폴더를 남겼습니다.");
        }
    }
}
