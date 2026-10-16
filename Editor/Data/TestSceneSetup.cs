using System.Collections.Generic;
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
                NewEmptyScene(path, "테스트 씬");
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

        // 빈 씬(카메라 · 빛)을 path에 만든다. 지금 열린 씬은 그대로 둔다(저장하지 않은 새 씬이 열려 있으면 그 자리에 만든다).
        internal static void NewEmptyScene(string path, string what)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "Assets");
            var active = SceneManager.GetActiveScene();
            var untitled = string.IsNullOrEmpty(active.path);
            // 저장하지 않은 새 씬에 바뀐 것이 있으면 Unity가 새 씬을 덧붙이지 못한다 — 까닭을 알린다(반영 스크립트면 기록되지 않아 다음 컴파일 뒤 다시 돈다).
            if (untitled && active.isDirty)
                throw new System.InvalidOperationException($"지금 열린 씬이 저장하지 않은 새 씬이라 {what}을(를) 만들지 못했습니다. 씬을 저장하거나 닫으면 다음 컴파일 뒤(또는 메뉴 '개발 › DB 반영 스크립트 모두 다시 돌리기') 다시 만듭니다.");
            // 빈 새 씬(바뀐 것 없음)이면 그 자리에 연다 — 저장하지 않은 씬 옆에는 덧붙일 수 없다.
            var fresh = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, untitled ? NewSceneMode.Single : NewSceneMode.Additive);
            EditorSceneManager.SaveScene(fresh, path);
            if (!untitled) EditorSceneManager.CloseScene(fresh, true);
        }

        [MenuItem("MomoiProject/테스트 씬 열기", priority = 40)]
        static void Open()
        {
            if (!File.Exists(DefaultPath))
            {
                EditorUtility.DisplayDialog("테스트 씬", $"{DefaultPath}가 없습니다. AI에게 테스트 씬을 만들어 달라고 하세요(DB 반영 스크립트의 TestSceneSetup.Ensure).", "확인");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(DefaultPath);
        }
    }

    // 게임 씬 만들기(관례 2절 — 게임 씬은 Assets/Dev/Scenes, AI가 처음 한 번만 만들고 그 뒤 배치는 개발자 몫). DB 반영 스크립트에서:
    //   [DbSeed("1")] static void Scenes()
    //   {
    //       GameSceneSetup.Ensure("Assets/Dev/Scenes/Battle.unity", firstInBuild: true);         // 게임이 켜질 때 여는 씬
    //       TestSceneSetup.Ensure(copyFrom: "Assets/Dev/Scenes/Battle.unity");                   // 그 씬을 복사한 테스트 씬
    //   }
    // 없을 때만 빈 씬(카메라 · 빛)을 만들고(있으면 건드리지 않는다 — 개발자가 배치한 씬), 빌드 목록(Build Settings)에 없으면 더한다.
    // firstInBuild면 빌드 목록 맨 앞으로(게임이 켜질 때 여는 씬). 씬 흐름(DB › Bootstrap)을 쓰면 그 뒤로는 흐름이 씬을 연다.
    public static class GameSceneSetup
    {
        public static bool Ensure(string path, bool firstInBuild = false)
        {
            if (string.IsNullOrEmpty(path)) throw new System.ArgumentException("[MomoiProject] 게임 씬 경로가 비었습니다(예: Assets/Dev/Scenes/Battle.unity).");
            var created = false;
            if (!File.Exists(path))
            {
                TestSceneSetup.NewEmptyScene(path, "게임 씬");
                AssetDatabase.Refresh();
                created = true;
                Debug.Log($"[MomoiProject] 게임 씬을 만들었습니다: {path}(빈 씬 — 배치는 개발자).");
            }
            var scenes = EditorBuildSettings.scenes.ToList();
            var index = scenes.FindIndex(s => s.path == path);
            var changed = false;
            if (index < 0)
            {
                scenes.Insert(firstInBuild ? 0 : scenes.Count, new EditorBuildSettingsScene(path, true));
                changed = true;
            }
            else if (firstInBuild && index > 0)
            {
                var entry = scenes[index];
                scenes.RemoveAt(index);
                scenes.Insert(0, entry);
                changed = true;
            }
            if (changed) EditorBuildSettings.scenes = scenes.ToArray();
            return created;
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
            var names = TestScenarios.Names().ToList();
            var current = scenario.stringValue;
            // 파일이 없는 이름(지웠거나 아직 저장 전)은 '(없음) 이름'으로 목록에 남긴다 — 그리기만 해서는 이름을 지우지 않는다.
            var missing = !string.IsNullOrEmpty(current) && !names.Contains(current);
            var options = new List<string> { "(새 판)" };
            if (missing) options.Add($"(없음) {current}");
            options.AddRange(names);
            var offset = missing ? 2 : 1;
            var index = string.IsNullOrEmpty(current) ? 0 : missing ? 1 : names.IndexOf(current) + offset;
            EditorGUI.BeginChangeCheck();
            var picked = EditorGUILayout.Popup(new GUIContent("시작 상황", "플레이하면 이 테스트 상황으로 시작한다(Assets/MomoiProject/Data/Tests). 테스트 패널·모니터 › 명령에서 저장한다"), index, options.ToArray());
            // 바꾼 것은 사용자가 고를 때만 적는다.
            if (EditorGUI.EndChangeCheck() && picked != index)
            {
                if (picked == 0) scenario.stringValue = "";
                else if (picked >= offset) scenario.stringValue = names[picked - offset];
            }
            if (missing && scenario.stringValue == current)
                EditorGUILayout.HelpBox($"테스트 상황 '{current}' 파일이 없습니다. 같은 이름으로 저장하거나 다른 상황을 고르세요.", MessageType.Warning);
            DrawPropertiesExcluding(serializedObject, "m_Script", nameof(TestScene.scenario));
            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.HelpBox("플레이하면 'test' 슬롯에서 시작 상황으로 열리고, 게임 화면 위에 테스트 패널(테스트 명령 · 상황 저장·불러오기 · 시간 · Undo)이 뜬다. 출시 빌드에서는 아무것도 하지 않는다.", MessageType.Info);
        }
    }
}
