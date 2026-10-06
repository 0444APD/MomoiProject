using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace GBible.EditorTools
{
    // 개발자 폴더(Assets/Dev): 바로 아래 폴더 하나 = DB 사이드바 'Dev' 그룹의 탭 하나. 탭은 그 폴더 안(하위 포함)의 모든 에셋을 보인다 —
    // 더 깊은 폴더는 탭 안에서 접고 펼치는 묶음. 개발자가 Project 창에서 폴더를 만들고 정리하면 탭·묶음이 바로 따라 바뀐다.
    // 탭은 보여 주기만 한다(옮기기·이름 바꾸기는 Project 창). AI는 요청받을 때만 이 폴더를 건드린다(관례 21절).
    public static class DevFolders
    {
        public const string Root = ProjectFolders.Dev;
        public const string Group = "Dev";

        // Assets/Dev 아래가 바뀌었을 때(만들기·지우기·옮기기·가져오기).
        public static event Action Changed;

        // 탭이 될 폴더(이름순, "Assets/Dev/이름").
        public static List<string> Tabs()
        {
            if (!Directory.Exists(Root)) return new List<string>();
            return Directory.GetDirectories(Root).Select(Normalize).Where(Visible)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // 폴더 안(하위 포함)의 에셋과 하위 폴더(프로젝트 기준 경로). Unity가 읽지 않는 것(.meta·점으로 시작·~로 끝남)은 뺀다.
        public static (List<string> assets, List<string> folders) Contents(string folder)
        {
            var assets = new List<string>();
            var folders = new List<string>();
            if (!Directory.Exists(folder)) return (assets, folders);
            Walk(folder, assets, folders);
            return (assets, folders);
        }

        static void Walk(string folder, List<string> assets, List<string> folders)
        {
            foreach (var file in Directory.GetFiles(folder).Select(Normalize))
                if (Visible(file) && !file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) assets.Add(file);
            foreach (var dir in Directory.GetDirectories(folder).Select(Normalize))
            {
                if (!Visible(dir)) continue;
                folders.Add(dir);
                Walk(dir, assets, folders);
            }
        }

        static string Normalize(string path) => path.Replace('\\', '/');

        static bool Visible(string path)
        {
            var name = Path.GetFileName(path);
            return name.Length > 0 && name[0] != '.' && !name.EndsWith("~", StringComparison.Ordinal);
        }

        static bool Under(string path) => path != null && (path == Root || path.StartsWith(Root + "/", StringComparison.Ordinal));

        sealed class Watcher : AssetPostprocessor
        {
            static bool _scheduled;

            static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                if (_scheduled || !(imported.Any(Under) || deleted.Any(Under) || moved.Any(Under) || movedFrom.Any(Under))) return;
                _scheduled = true;
                EditorApplication.delayCall += () =>
                {
                    _scheduled = false;
                    Changed?.Invoke();
                };
            }
        }
    }
}
