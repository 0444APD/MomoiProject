using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GBible.EditorTools
{
    // 테스트 씬 만들기(관례 19절). AI가 DB 반영 스크립트([DbSeed])에서 부른다:
    //   [DbSeed("1")] static void TestScene() => TestSceneSetup.Ensure(copyFrom: "Assets/Dev/Scenes/Battle.unity", scenario: "보스 직전");
    // 게임 씬을 복사하고(없으면 빈 씬) '[테스트]' 오브젝트에 TestScene 컴포넌트를 붙인다. 이미 있으면 건너뛴다(개발자가 고친 씬을 덮지 않는다).
    public static class TestSceneSetup
    {
        public const string DefaultPath = ProjectFolders.Scenes + "/Test.unity";

        public static bool Ensure(string path = DefaultPath, string copyFrom = null, string scenario = "")
        {
            if (string.IsNullOrEmpty(path)) path = DefaultPath;
            if (File.Exists(path)) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "Assets");
            if (!string.IsNullOrEmpty(copyFrom) && File.Exists(copyFrom))
            {
                AssetDatabase.CopyAsset(copyFrom, path);
            }
            else
            {
                var fresh = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Additive);
                EditorSceneManager.SaveScene(fresh, path);
                EditorSceneManager.CloseScene(fresh, true);
            }
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            if (!scene.GetRootGameObjects().Any(g => g.GetComponentInChildren<TestScene>(true) != null))
            {
                var holder = new GameObject("[테스트]");
                SceneManager.MoveGameObjectToScene(holder, scene);
                holder.AddComponent<TestScene>().scenario = scenario ?? "";
            }
            EditorSceneManager.SaveScene(scene);
            if (SceneManager.sceneCount > 1) EditorSceneManager.CloseScene(scene, true);
            AssetDatabase.Refresh();
            Debug.Log($"[MomoiProject] 테스트 씬을 만들었습니다: {path}. 열고 플레이하면 테스트 패널(F1)이 뜹니다.");
            return true;
        }

        [MenuItem("MomoiProject/테스트/지금 씬을 복사해 테스트 씬 만들기", priority = 70)]
        static void FromActiveScene()
        {
            var active = SceneManager.GetActiveScene();
            if (File.Exists(DefaultPath))
            {
                EditorUtility.DisplayDialog("테스트 씬", $"{DefaultPath}가 이미 있습니다. 메뉴 '테스트 씬 열기'로 엽니다.", "확인");
                return;
            }
            if (active.isDirty && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Ensure(DefaultPath, string.IsNullOrEmpty(active.path) ? null : active.path);
            EditorUtility.DisplayDialog("테스트 씬", $"{DefaultPath}를 만들었습니다. 열고 플레이하면 테스트 패널(F1)이 뜹니다.", "확인");
        }

        [MenuItem("MomoiProject/테스트/테스트 씬 열기", priority = 71)]
        static void Open()
        {
            if (!File.Exists(DefaultPath))
            {
                EditorUtility.DisplayDialog("테스트 씬", $"{DefaultPath}가 없습니다. AI에게 테스트 씬을 만들어 달라고 하거나 메뉴 '지금 씬을 복사해 테스트 씬 만들기'.", "확인");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(DefaultPath);
        }

        [MenuItem("MomoiProject/테스트/테스트 상황 폴더 열기", priority = 72)]
        static void RevealScenarios()
        {
            Directory.CreateDirectory(TestScenarios.Folder);
            EditorUtility.RevealInFinder(TestScenarios.Folder);
        }
    }

    // 테스트 씬 인스펙터: 시작 상황을 저장된 테스트 상황 목록에서 고른다.
    [CustomEditor(typeof(TestScene))]
    sealed class TestSceneEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var scenario = serializedObject.FindProperty(nameof(TestScene.scenario));
            var names = TestScenarios.Names();
            var options = new[] { "(새 판)" }.Concat(names).ToArray();
            var index = names.IndexOf(scenario.stringValue) + 1;
            var picked = EditorGUILayout.Popup(new GUIContent("시작 상황", "플레이하면 이 테스트 상황으로 시작한다(Assets/MomoiProject/Data/Tests). 테스트 패널·모니터 › 명령에서 저장한다"), Mathf.Max(0, index), options);
            scenario.stringValue = picked == 0 ? "" : names[picked - 1];
            if (index == 0 && !string.IsNullOrEmpty(scenario.stringValue))
                EditorGUILayout.HelpBox($"테스트 상황 '{scenario.stringValue}' 파일이 없습니다.", MessageType.Warning);
            DrawPropertiesExcluding(serializedObject, "m_Script", nameof(TestScene.scenario));
            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.HelpBox("플레이하면 'test' 슬롯에서 시작 상황으로 열리고, 게임 화면 위에 테스트 패널(테스트 명령 · 상황 저장·불러오기 · 시간 · Undo)이 뜬다. 출시 빌드에서는 아무것도 하지 않는다.", MessageType.Info);
        }
    }
}
