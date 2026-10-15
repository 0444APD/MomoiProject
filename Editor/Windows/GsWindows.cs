using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GBible.EditorTools
{
    // MomoiProject 창을 여는 공통 방법.
    // 기본은 유틸리티 창: Unity의 탭 막대 없이 떠 있는 창(제목은 OS 창 제목줄에). 도킹은 안 되고 Unity 메인 창 위에 뜬다.
    // 메뉴 'MomoiProject › 개발 › 창 탭 막대 숨기기'를 끄면 예전처럼 도킹되는 탭 창으로 연다.
    // 이미 도킹된 탭으로 열려 있던 창은 그대로 쓰므로, 바꾼 뒤에는 한 번 닫고 다시 연다.
    public static class GsWindows
    {
        const string PrefKey = "GBible.UtilityWindows";
        const string MenuPath = "MomoiProject/개발/창 탭 막대 숨기기";

        public static bool Utility
        {
            get => EditorPrefs.GetBool(PrefKey, true);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        // 하나만 두는 창: 열려 있으면 그것을, 없으면 새로 연다.
        public static T Get<T>(bool focus = true) where T : EditorWindow => EditorWindow.GetWindow<T>(Utility, null, focus);

        // 같은 종류를 하나 더 연다(그룹을 새 창에서 열기 등).
        public static T Open<T>(string title, Vector2 size) where T : EditorWindow
        {
            var window = ScriptableObject.CreateInstance<T>();
            window.titleContent = new GUIContent(title);
            var main = EditorGUIUtility.GetMainWindowPosition();
            window.position = new Rect(main.x + (main.width - size.x) / 2, main.y + (main.height - size.y) / 2, size.x, size.y);
            if (Utility) window.ShowUtility();
            else window.Show();
            return window;
        }

        [MenuItem(MenuPath, priority = 981)]
        static void Toggle()
        {
            Utility = !Utility;
            Debug.Log(Utility
                ? "[MomoiProject] 이제 MomoiProject 창을 탭 막대 없는 창으로 엽니다. 이미 열린 창은 닫고 다시 여세요."
                : "[MomoiProject] 이제 MomoiProject 창을 도킹되는 탭 창으로 엽니다. 이미 열린 창은 닫고 다시 여세요.");
        }

        [MenuItem(MenuPath, true)]
        static bool ToggleCheck()
        {
            Menu.SetChecked(MenuPath, Utility);
            return true;
        }
    }
}
