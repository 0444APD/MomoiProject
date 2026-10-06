using System.IO;
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
    //   Assets/Dev/           개발자가 노는 곳 — 바로 아래 폴더 = DB 'Dev' 탭. 에셋 가져오기·넣을 에셋 자리도 여기.
    // 폴더·파일 이름은 영어(관례 2절).
    public static class ProjectFolders
    {
        public const string Root = "Assets/MomoiProject";
        public const string Code = Root + "/Code";
        public const string Data = Root + "/Data";
        public const string UI = Root + "/UI";
        public const string Generated = Root + "/Generated";
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

        // 예전 자리의 시스템 폴더를 새 자리로 옮긴다(GUID 유지 — 참조가 끊기지 않는다). 새 자리에 이미 있으면 그대로 두고 알린다.
        internal static void MoveLegacy()
        {
            foreach (var (from, to, marker) in Legacy)
            {
                if (!AssetDatabase.IsValidFolder(from) || !(File.Exists(marker) || Directory.Exists(marker))) continue;
                if (AssetDatabase.IsValidFolder(to))
                {
                    Debug.LogWarning($"[MomoiProject] {from}를 {to}(으)로 옮기지 않았습니다 — 새 자리에 이미 폴더가 있습니다. 안의 것을 직접 옮겨 주세요.");
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
