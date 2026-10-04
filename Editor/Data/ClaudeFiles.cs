using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace GBible.EditorTools
{
    // Claude Code 파일: 시작 설정이 만들고, 에디터를 열 때마다 패키지 경로·템플릿이 바뀌었으면 다시 맞춘다.
    //   CLAUDE.md                          — 표시한 칸(MomoiProject:begin~end)만 다시 쓴다. 칸 밖에 사람이 쓴 것은 그대로.
    //                                        관례·참조 문서를 @가져오기로 불러온다(패키지 경로는 설치 방식마다 달라 여기서 채운다).
    //   .claude/skills/think-momoi/SKILL.md — 스킬 /think-momoi(시스템 모드·콘텐츠 모드)
    //   .claude/hooks/compile-check.ps1    — 컴파일 검사(패키지 Tools~에서 복사)
    //   .claude/settings.json              — Stop 훅 한 줄(이미 있으면 그대로, 다른 설정은 건드리지 않는다)
    //   Docs/PlanningSpec.md               — 기획 명세서 양식(없을 때만)
    // 템플릿: 패키지 Documentation~/AI/Templates({{GBIBLE}} = 프로젝트에서 본 패키지 경로).
    public static class ClaudeFiles
    {
        const string BeginMark = "<!-- MomoiProject:begin";
        const string Begin = "<!-- MomoiProject:begin — 시작 설정이 다시 쓰는 칸. 이 칸 밖에 적은 것은 그대로 둔다. -->";
        const string End = "<!-- MomoiProject:end -->";
        public const string HookScript = ".claude/hooks/compile-check.ps1";
        public const string HookCommand = "powershell -NoProfile -ExecutionPolicy Bypass -File .claude/hooks/compile-check.ps1";

        static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        // 패키지 폴더(절대 경로). 패키지가 아니라 Assets 안에 두었으면 이 파일에서 거슬러 찾는다.
        static string PackageRoot()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ClaudeFiles).Assembly);
            if (info != null && Directory.Exists(info.resolvedPath)) return info.resolvedPath;
            var here = AssetDatabase.FindAssets("t:Script ClaudeFiles").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => p.EndsWith("/ClaudeFiles.cs"));
            if (here == null) return null;
            var dir = Path.GetDirectoryName(Path.GetFullPath(here));
            while (dir != null && !Directory.Exists(Path.Combine(dir, "Documentation~"))) dir = Path.GetDirectoryName(dir);
            return dir;
        }

        // 프로젝트 폴더 안이면 상대 경로(/), 밖이면 절대 경로(/).
        static string FromProject(string path)
        {
            var root = ProjectRoot.Replace('\\', '/').TrimEnd('/') + "/";
            var full = Path.GetFullPath(path).Replace('\\', '/');
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full.Substring(root.Length) : full;
        }

        // 이 파일이 있으면 맞추지 않는다(라이브러리 개발 프로젝트처럼 게임 규칙을 Claude에 넣지 않을 곳).
        public const string OffMarker = "ProjectSettings/MomoiProject/ClaudeFiles.off";
        const string MenuSync = "MomoiProject/Claude Code 파일/지금 맞추기";
        const string MenuOff = "MomoiProject/Claude Code 파일/맞추지 않기";

        public static bool IsOff => File.Exists(Path.Combine(ProjectRoot, OffMarker));

        [MenuItem(MenuSync, priority = 70)]
        static void SyncMenu()
        {
            if (IsOff)
            {
                EditorUtility.DisplayDialog("Claude Code 파일", "'맞추지 않기'가 켜져 있습니다. 끄고 다시 누르세요.", "확인");
                return;
            }
            var done = Sync();
            EditorUtility.DisplayDialog("Claude Code 파일",
                done.Count == 0 ? "이미 맞습니다." : "만들거나 고친 것:\n- " + string.Join("\n- ", done), "확인");
        }

        // 켜면 시작 설정·에디터 열기에서 Claude Code 파일을 만들지도 고치지도 않는다(이미 있는 파일은 그대로 둔다).
        [MenuItem(MenuOff, priority = 71)]
        static void ToggleOff()
        {
            var path = Path.Combine(ProjectRoot, OffMarker);
            if (IsOff) File.Delete(path);
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, "Claude Code 파일(CLAUDE.md·.claude)을 맞추지 않는다. 메뉴 MomoiProject › Claude Code 파일 › 맞추지 않기로 끈다.\n", new UTF8Encoding(false));
            }
        }

        [MenuItem(MenuOff, true)]
        static bool ToggleOffCheck()
        {
            UnityEditor.Menu.SetChecked(MenuOff, IsOff);
            return true;
        }

        // 맞춘 파일 목록을 돌려준다(바뀐 것이 없으면 빈 목록). 패키지 템플릿을 찾지 못했거나 '맞추지 않기'면 아무것도 하지 않는다.
        public static List<string> Sync()
        {
            var done = new List<string>();
            if (IsOff) return done;
            var package = PackageRoot();
            var templates = package == null ? null : Path.Combine(package, "Documentation~", "AI", "Templates");
            if (templates == null || !Directory.Exists(templates)) return done;
            var gbible = FromProject(package);
            string Template(string name) => File.ReadAllText(Path.Combine(templates, name)).Replace("{{GBIBLE}}", gbible).Replace("\r\n", "\n");

            var claude = Path.Combine(ProjectRoot, "CLAUDE.md");
            var block = Begin + "\n" + Template("CLAUDE.block.md").TrimEnd() + "\n" + End;
            var current = File.Exists(claude) ? File.ReadAllText(claude) : null;
            var next = current == null ? block + "\n" : WithBlock(current, block);
            if (next != current)
            {
                Write(claude, next);
                done.Add("CLAUDE.md");
            }

            if (WriteIfChanged(Path.Combine(ProjectRoot, ".claude", "skills", "think-momoi", "SKILL.md"), Template("think-momoi.SKILL.md")))
                done.Add(".claude/skills/think-momoi/SKILL.md");

            var script = Path.Combine(package, "Tools~", "compile-check.ps1");
            if (File.Exists(script))
            {
                var target = Path.Combine(ProjectRoot, HookScript);
                var bytes = File.ReadAllBytes(script);   // BOM 그대로(Windows PowerShell 5.1이 한글을 읽도록)
                if (!File.Exists(target) || !File.ReadAllBytes(target).SequenceEqual(bytes))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.WriteAllBytes(target, bytes);
                    done.Add(HookScript);
                }
                if (EnsureHook(Path.Combine(ProjectRoot, ".claude", "settings.json"))) done.Add(".claude/settings.json (응답을 끝낼 때 컴파일 검사)");
            }

            var spec = Path.Combine(ProjectRoot, "Docs", "PlanningSpec.md");
            var specTemplate = Path.Combine(package, "Documentation~", "AI", "PlanningSpec.md");
            if (!File.Exists(spec) && File.Exists(specTemplate))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(spec));
                File.Copy(specTemplate, spec);
                done.Add("Docs/PlanningSpec.md (기획 명세서 빈 양식)");
            }
            return done;
        }

        // 표시한 칸을 바꾼다. 칸이 없으면 맨 앞에 넣는다.
        static string WithBlock(string text, string block)
        {
            var normalized = text.Replace("\r\n", "\n");
            var begin = normalized.IndexOf(BeginMark, StringComparison.Ordinal);
            var end = begin < 0 ? -1 : normalized.IndexOf(End, begin, StringComparison.Ordinal);
            if (begin < 0 || end < 0) return block + "\n\n" + normalized;
            return normalized.Substring(0, begin) + block + normalized.Substring(end + End.Length);
        }

        static bool WriteIfChanged(string path, string text)
        {
            if (File.Exists(path) && File.ReadAllText(path).Replace("\r\n", "\n") == text) return false;
            Write(path, text);
            return true;
        }

        static void Write(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        // .claude/settings.json의 hooks.Stop에 컴파일 검사가 없으면 더한다. 읽지 못하는 파일이면 건드리지 않는다.
        static bool EnsureHook(string path)
        {
            Dictionary<string, object> root;
            if (File.Exists(path))
            {
                try { root = MiniJson.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>; }
                catch (Exception) { root = null; }
                if (root == null)
                {
                    Debug.LogWarning($"[MomoiProject] {FromProject(path)}를 읽지 못해 컴파일 검사 훅을 넣지 않았습니다. hooks.Stop에 \"{HookCommand}\"를 직접 넣으세요.");
                    return false;
                }
            }
            else root = new Dictionary<string, object>();
            if (!(root.TryGetValue("hooks", out var hooksValue) && hooksValue is Dictionary<string, object> hooks))
                root["hooks"] = hooks = new Dictionary<string, object>();
            if (!(hooks.TryGetValue("Stop", out var stopValue) && stopValue is List<object> stop))
                hooks["Stop"] = stop = new List<object>();
            if (MiniJson.Serialize(stop).Contains("compile-check.ps1")) return false;
            stop.Add(new Dictionary<string, object>
            {
                ["hooks"] = new List<object>
                {
                    new Dictionary<string, object> { ["type"] = "command", ["command"] = HookCommand, ["timeout"] = 300 },
                },
            });
            Write(path, Pretty(root, 0) + "\n");
            return true;
        }

        // 사람이 읽을 수 있게 들여 쓴 JSON.
        static string Pretty(object value, int depth)
        {
            var pad = new string(' ', (depth + 1) * 2);
            var close = new string(' ', depth * 2);
            switch (value)
            {
                case Dictionary<string, object> map when map.Count > 0:
                    return "{\n" + string.Join(",\n", map.Select(p => pad + MiniJson.Serialize(p.Key) + ": " + Pretty(p.Value, depth + 1))) + "\n" + close + "}";
                case List<object> list when list.Count > 0:
                    return "[\n" + string.Join(",\n", list.Select(v => pad + Pretty(v, depth + 1))) + "\n" + close + "]";
                default:
                    return MiniJson.Serialize(value);
            }
        }
    }
}
