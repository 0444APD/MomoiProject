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
    // 예전처럼 라이브러리를 Assets/Code/System에 둔 프로젝트에서는 그 폴더를 그룹으로 보지 않는다(지금은 Packages/com.momoi.gbible).
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


        // 그룹(폴더) 목록(이름순).
        public static List<string> Groups() =>
            OnDisk().Where(g => !string.IsNullOrEmpty(g)).Distinct().OrderBy(g => g, StringComparer.Ordinal).ToList();

        // 클래스의 그룹: 파일이 있는 폴더. Code 밖이면 Outside, 파일을 못 찾으면 null.
        public static string GroupOf(string fullName)
        {
            var path = PathOf(fullName);
            if (path == null) return null;
            var folder = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "";
            if (folder == Root) return "";
            if (!folder.StartsWith(Root + "/", StringComparison.Ordinal)) return Outside;
            var group = folder.Substring(Root.Length + 1);
            return IsReserved(group) ? Outside : group;
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
    }
}
