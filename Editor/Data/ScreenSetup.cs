using System.Collections.Generic;
using System.IO;
using System.Linq;
using GBible.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GBible.EditorTools
{
    // 화면(UI Toolkit) 준비: Assets/UI에 기본 테마(.tss)·Panel Settings·프로젝트 테마 USS를 만들고, DB › 화면 설정 항목을 적용 전으로 만든다.
    // 이미 있는 파일·항목은 건너뛴다(사람이 고친 것을 덮지 않는다). 시작 설정·DB 추가 메뉴에서도 부른다(ScreenSetup.Ensure()).
    // 폴더: Assets/MomoiProject/UI/Screens(화면 UXML·USS), Assets/MomoiProject/UI/Items(목록 한 칸 UXML). 화면 코드는 Assets/MomoiProject/Code/Client/Screens.
    public static class ScreenSetup
    {
        public const string Folder = ProjectFolders.UI;
        public const string ThemeFile = Folder + "/DefaultTheme.tss";
        public const string PanelFile = Folder + "/PanelSettings.asset";
        public const string ProjectThemeFile = Folder + "/Theme.uss";

        [MenuItem("MomoiProject/화면/화면 준비", priority = 60)]
        static void Prepare()
        {
            var made = Ensure();
            DatabaseWindow.Open();
            EditorUtility.DisplayDialog("화면 준비",
                made.Count == 0 ? "화면 준비가 이미 되어 있습니다." : "만든 것:\n- " + string.Join("\n- ", made) + "\n\nDB › 화면 설정을 확인하고 Ctrl+S로 적용하세요.", "확인");
        }

        // 만든 것의 목록을 돌려준다(없으면 빈 목록). commit: DB 항목을 작업본이 아니라 바로 저장한다(에디터가 저절로 만들 때 — EditorDatabase.Upkeep).
        public static List<string> Ensure(bool commit = false)
        {
            var made = new List<string>();
            EditorDatabase.EnsureFolder(Folder);
            EditorDatabase.EnsureFolder(Folder + "/Screens");
            EditorDatabase.EnsureFolder(Folder + "/Items");

            if (!File.Exists(ThemeFile))
            {
                File.WriteAllText(ThemeFile, "@import url(\"unity-theme://default\");\n");
                AssetDatabase.ImportAsset(ThemeFile);
                made.Add(ThemeFile + " (UI Toolkit 기본 테마)");
            }
            if (!File.Exists(ProjectThemeFile))
            {
                File.WriteAllText(ProjectThemeFile, ProjectTheme);
                AssetDatabase.ImportAsset(ProjectThemeFile);
                made.Add(ProjectThemeFile + " (프로젝트 테마: 색·글자 크기)");
            }
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelFile);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemeFile);
                panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                panel.referenceResolution = new Vector2Int(1920, 1080);
                panel.match = 0.5f;
                AssetDatabase.CreateAsset(panel, PanelFile);
                AssetDatabase.SaveAssets();
                made.Add(PanelFile + " (1920×1080 기준으로 맞춤)");
            }

            var theme = AssetDatabase.LoadAssetAtPath<StyleSheet>(ProjectThemeFile);
            var settings = EditorDatabase.Instance.OfType<ScreenSettingsData>().FirstOrDefault();
            if (settings == null && commit)
            {
                settings = (ScreenSettingsData)EditorDatabase.Instance.Create(typeof(ScreenSettingsData), "screenSettings");
                settings.panelSettings = panel;
                if (theme != null) settings.theme.Add(theme);
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssetIfDirty(settings);
                made.Add("DB › 화면 설정 'screenSettings'");
            }
            else if (settings == null)
            {
                settings = (ScreenSettingsData)EditorDatabase.Staging.Add(typeof(ScreenSettingsData), "screenSettings");
                settings.panelSettings = panel;
                if (theme != null) settings.theme.Add(theme);
                EditorDatabase.Staging.Touch();
                made.Add("DB › 화면 설정 'screenSettings'(적용 전)");
            }
            else if (settings.panelSettings == null || (theme != null && !settings.theme.Contains(theme)))
            {
                var work = (ScreenSettingsData)EditorDatabase.Staging.Work(settings);
                if (work.panelSettings == null) work.panelSettings = panel;
                if (theme != null && !work.theme.Contains(theme)) work.theme.Add(theme);
                EditorDatabase.Staging.Touch();
                made.Add($"DB › 화면 설정 '{settings.Id}'에 Panel Settings·테마 연결(적용 전)");
            }
            return made;
        }

        const string ProjectTheme =
@"/* 프로젝트 테마: 모든 화면에 붙는다(DB › 화면 설정 › 테마 USS). 기본 화면(고르기·설정·슬롯·일시정지·확인·알림)도 이 변수를 쓴다.
   값을 바꾸면 모든 화면의 색·크기가 바뀐다. 클래스(.momoi-panel · .momoi-button · .momoi-title …)를 여기서 덮어써도 된다. */

:root {
    --momoi-text: rgb(236, 238, 244);
    --momoi-dim: rgb(150, 156, 172);
    --momoi-panel: rgb(30, 34, 46);
    --momoi-button: rgb(52, 58, 78);
    --momoi-button-hover: rgb(70, 78, 104);
    --momoi-accent: rgb(255, 176, 64);
    --momoi-accent-text: rgb(24, 22, 20);
    --momoi-backdrop: rgba(0, 0, 0, 0.55);
    --momoi-radius: 10px;
    --momoi-font-size: 22px;
}
";
    }
}
