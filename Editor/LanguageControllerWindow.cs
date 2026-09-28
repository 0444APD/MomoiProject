#if UNITY_EDITOR && GAME_SYSTEMS_READY
using UnityEditor;
using UnityEngine;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using System.Collections.Generic;

public class LanguageControllerWindow : OdinEditorWindow {

    [MenuItem("Tools/런타임 언어 컨트롤러")]
    private static void OpenWindow() {
        GetWindow<LanguageControllerWindow>("언어 컨트롤러").Show();
    }

    [Title("실시간 언어 변경 (Play Mode 전용)")]
    [ValueDropdown("GetLanguageList")] // 드롭다운 항목을 제공할 함수 지정
    [HideLabel]
    [OnValueChanged("OnLanguageChanged")]
    public string testLanguage = "Kr";

    // 드롭다운 메뉴에 표시할 임의의 string 배열을 반환하는 함수
    private IEnumerable<string> GetLanguageList() {
        return new string[] { "Kr", "En", "Jp", "Cn" };
    }

    protected override void OnEnable() {
        base.OnEnable();
        if (Application.isPlaying) {
            testLanguage = LanguageManager.CurrentLanguage;
        }
    }

    private void OnLanguageChanged() {
        if (!Application.isPlaying) {
            Debug.LogWarning("[언어 컨트롤러] 이 기능은 게임이 실행 중(Play Mode)일 때만 작동합니다.");
            return;
        }

        LanguageManager.ChangeLanguage(testLanguage);
        Debug.Log($"[언어 컨트롤러] 런타임 언어가 {testLanguage}(으)로 변경되었습니다.");
    }
}
#endif