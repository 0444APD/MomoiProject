using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameSystems.Dialogue;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameSystems.EditorTools.Graph
{
    // 대화 UI 템플릿: 모든 기능이 연결된 기본 프리팹과 UXML·USS·모션·패널 설정을 한 폴더에 만든다.
    // 프로젝트마다 모양을 바꿀 때는 이 폴더의 UXML·USS를 고치거나(요소 이름만 지키면 된다), 프리팹 변형을 만든다.
    public static class DialogueTemplate
    {
        const string Root = EditorDatabase.DataRoot + "/DialogueUI";

        [MenuItem("Game Systems/대화 UI 템플릿 만들기", priority = 7)]
        static void CreateMenu()
        {
            var prefab = Create();
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
            Debug.Log($"[대화 UI] 템플릿을 만들었습니다: {AssetDatabase.GetAssetPath(prefab)}\n씬에 끌어 놓고 DialoguePlayer.Play(\"대화 ID\")로 재생합니다. 한글이 네모로 보이면 UI 오브젝트의 DialogueUI > font에 한글 폰트(TTF)를 넣으세요.");
        }

        // 새 폴더에 템플릿 한 벌을 만들고 프리팹을 돌려준다(이미 있으면 'DialogueUI 1' …).
        public static GameObject Create(string folder = null)
        {
            folder ??= UniqueFolder(Root);
            EditorDatabase.EnsureFolder(folder);

            var uss = WriteText(folder, "DialogueUI.uss", Uss);
            var uxml = WriteText(folder, "DialogueUI.uxml", Uxml);
            var choice = WriteText(folder, "DialogueChoice.uxml", ChoiceUxml);
            var theme = WriteText(folder, "DialogueTheme.tss", "@import url(\"unity-theme://default\");\n");
            var white = WriteWhiteSprite(folder);
            AssetDatabase.Refresh();

            var panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(theme);
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1920, 1080);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0.5f;
            panel.sortingOrder = 100;
            AssetDatabase.CreateAsset(panel, folder + "/DialoguePanel.asset");

            var motions = new List<DialogueUI.MotionSlot>
            {
                Slot(folder, "BoxOpen", DialogueUI.Moment.Open, "", m =>
                {
                    m.duration = 0.25f;
                    m.opacity = true;
                    m.opacityCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
                    m.move = true;
                    m.moveY = AnimationCurve.EaseInOut(0, 30, 1, 0);
                }),
                Slot(folder, "NextBlink", DialogueUI.Moment.Waiting, "dialogue-next", m =>
                {
                    m.duration = 0.6f;
                    m.loop = UiMotion.LoopMode.PingPong;
                    m.restoreWhenDone = true;
                    m.opacity = true;
                    m.opacityCurve = AnimationCurve.EaseInOut(0, 0.25f, 1, 1);
                    m.move = true;
                    m.moveY = AnimationCurve.EaseInOut(0, 0, 1, 4);
                }),
                Slot(folder, "ChoiceAppear", DialogueUI.Moment.ChoicesShown, "", m =>
                {
                    m.duration = 0.2f;
                    m.opacity = true;
                    m.opacityCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
                    m.move = true;
                    m.moveY = AnimationCurve.EaseInOut(0, 16, 1, 0);
                }),
                Slot(folder, "ChoicePicked", DialogueUI.Moment.ChoicePicked, "", m =>
                {
                    m.duration = 0.15f;
                    m.restoreWhenDone = true;
                    m.scale = true;
                    m.scaleCurve = new AnimationCurve(new Keyframe(0, 1), new Keyframe(0.5f, 1.05f), new Keyframe(1, 1));
                }),
            };

            var prefab = BuildPrefab(folder, AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxml), AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(choice),
                panel, AssetDatabase.LoadAssetAtPath<Sprite>(white), motions);
            AssetDatabase.SaveAssets();
            return prefab;
        }

        static string UniqueFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path)) return path;
            for (var i = 1; ; i++)
                if (!AssetDatabase.IsValidFolder($"{path} {i}"))
                    return $"{path} {i}";
        }

        static string WriteText(string folder, string file, string content)
        {
            var path = $"{folder}/{file}";
            File.WriteAllText(path, content, new System.Text.UTF8Encoding(false));
            AssetDatabase.ImportAsset(path);
            return path;
        }

        static string WriteWhiteSprite(string folder)
        {
            var path = $"{folder}/White.png";
            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            texture.SetPixels(Enumerable.Repeat(Color.white, 64).ToArray());
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 8;
                importer.filterMode = FilterMode.Point;
                importer.SaveAndReimport();
            }
            return path;
        }

        static DialogueUI.MotionSlot Slot(string folder, string name, DialogueUI.Moment moment, string element, System.Action<UiMotion> setup)
        {
            var motion = ScriptableObject.CreateInstance<UiMotion>();
            setup(motion);
            AssetDatabase.CreateAsset(motion, $"{folder}/Motion_{name}.asset");
            return new DialogueUI.MotionSlot { moment = moment, element = element, motion = motion };
        }

        static GameObject BuildPrefab(string folder, VisualTreeAsset uxml, VisualTreeAsset choice, PanelSettings panel, Sprite white, List<DialogueUI.MotionSlot> motions)
        {
            var root = new GameObject("DialogueSystem");
            try
            {
                var player = root.AddComponent<DialoguePlayer>();

                var uiObject = new GameObject("UI");
                uiObject.transform.SetParent(root.transform, false);
                var document = uiObject.AddComponent<UIDocument>();
                document.panelSettings = panel;
                document.visualTreeAsset = uxml;
                var ui = uiObject.AddComponent<DialogueUI>();
                ui.choiceTemplate = choice;
                ui.motions = motions;

                var stageObject = new GameObject("Stage");
                stageObject.transform.SetParent(root.transform, false);
                var stage = stageObject.AddComponent<DialogueStage>();
                stage.ui = ui;
                stage.backgroundBack = Renderer(stageObject.transform, "BackgroundBack", null);
                stage.backgroundFront = Renderer(stageObject.transform, "BackgroundFront", null);
                stage.dim = Renderer(stageObject.transform, "Dim", white);
                stage.dim.color = new Color(0, 0, 0, 0);
                stage.standingRoot = Child(stageObject.transform, "Standings");
                stage.effectRoot = Child(stageObject.transform, "Effects");
                var music = Child(stageObject.transform, "Music").gameObject.AddComponent<AudioSource>();
                music.loop = true;
                music.playOnAwake = false;
                stage.music = music;
                var sounds = Child(stageObject.transform, "Sounds").gameObject.AddComponent<AudioSource>();
                sounds.playOnAwake = false;
                stage.sounds = sounds;

                player.stage = stage;
                return PrefabUtility.SaveAsPrefabAsset(root, folder + "/DialogueSystem.prefab");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static Transform Child(Transform parent, string name)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        static SpriteRenderer Renderer(Transform parent, string name, Sprite sprite)
        {
            var renderer = Child(parent, name).gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            return renderer;
        }

        // UXML에 이름 규격의 요소가 있는지. (필드, 이름, 필수, 찾음)
        public static List<(string field, string name, bool required, bool found)> CheckNames(VisualTreeAsset uxml, DialogueUI.ElementNames names)
        {
            var result = new List<(string, string, bool, bool)>();
            if (uxml == null) return result;
            var tree = uxml.Instantiate();
            foreach (var (field, name, required) in names.All())
                result.Add((field, name, required, !string.IsNullOrEmpty(name) && tree.Q(name) != null));
            return result;
        }

        // ── 템플릿 파일 ──────────────────────────────────────

        const string Uxml = @"<ui:UXML xmlns:ui=""UnityEngine.UIElements"">
    <Style src=""DialogueUI.uss"" />
    <!-- 요소 이름(name)은 DialogueUI의 이름 규격과 같아야 한다. 모양·배치·클래스는 자유롭게 바꾼다. -->
    <ui:VisualElement name=""dialogue-root"" class=""dialogue-root"">
        <ui:VisualElement name=""dialogue-fade"" class=""dialogue-fade"" picking-mode=""Ignore"" />
        <ui:VisualElement name=""dialogue-flash"" class=""dialogue-flash"" picking-mode=""Ignore"" />
        <ui:VisualElement name=""dialogue-letterbox-top"" class=""dialogue-letterbox dialogue-letterbox--top"" picking-mode=""Ignore"" />
        <ui:VisualElement name=""dialogue-letterbox-bottom"" class=""dialogue-letterbox dialogue-letterbox--bottom"" picking-mode=""Ignore"" />
        <ui:VisualElement name=""dialogue-choices"" class=""dialogue-choices"" picking-mode=""Ignore"" />
        <ui:VisualElement name=""dialogue-box"" class=""dialogue-box"">
            <ui:VisualElement name=""dialogue-name-box"" class=""dialogue-name-box"">
                <ui:Label name=""dialogue-name"" class=""dialogue-name"" text="""" />
                <!-- 프로젝트별 추가 글 예시: 스탠딩 프로필의 추가 글 키 affiliation(소속). 비어 있으면 숨는다. 필요 없으면 지운다. -->
                <ui:Label name=""dialogue-extra-affiliation"" class=""dialogue-extra dialogue-affiliation"" text="""" />
            </ui:VisualElement>
            <ui:Label name=""dialogue-text"" class=""dialogue-text"" text="""" enable-rich-text=""true"" />
            <ui:VisualElement name=""dialogue-next"" class=""dialogue-next"" picking-mode=""Ignore"" />
        </ui:VisualElement>
        <ui:VisualElement name=""dialogue-tooltip"" class=""dialogue-tooltip"" picking-mode=""Ignore"">
            <ui:Label name=""dialogue-tooltip-text"" class=""dialogue-tooltip-text"" text="""" />
        </ui:VisualElement>
    </ui:VisualElement>
</ui:UXML>
";

        const string ChoiceUxml = @"<ui:UXML xmlns:ui=""UnityEngine.UIElements"">
    <ui:VisualElement class=""dialogue-choice"">
        <ui:Label name=""choice-text"" class=""choice-text"" text="""" />
    </ui:VisualElement>
</ui:UXML>
";

        const string Uss = @"/* 대화 UI 기본 모양. 상태 클래스: dialogue--open · dialogue--staging · dialogue--typing · dialogue--waiting
   · dialogue--choosing · dialogue--narration · dialogue--speaker-<캐릭터 ID> (루트에 붙는다) */

.dialogue-root {
    position: absolute;
    left: 0;
    top: 0;
    right: 0;
    bottom: 0;
    opacity: 0;
    transition-property: opacity;
    transition-duration: 0.25s;
}

.dialogue--open {
    opacity: 1;
}

/* 화면 덮개 */
.dialogue-fade,
.dialogue-flash {
    position: absolute;
    left: 0;
    top: 0;
    right: 0;
    bottom: 0;
}

.dialogue-fade {
    background-color: rgb(0, 0, 0);
    opacity: 0;
}

.dialogue-letterbox {
    position: absolute;
    left: 0;
    right: 0;
    height: 0;
    background-color: rgb(0, 0, 0);
}

.dialogue-letterbox--top { top: 0; }
.dialogue-letterbox--bottom { bottom: 0; }

/* 글상자 */
.dialogue-box {
    position: absolute;
    left: 6%;
    right: 6%;
    bottom: 4%;
    min-height: 230px;
    padding: 40px 52px 36px 52px;
    border-radius: 20px;
    background-color: rgba(14, 15, 17, 0.72);
    border-width: 1px;
    border-color: rgba(255, 255, 255, 0.14);
    backdrop-filter: blur(14px);
    transition-property: opacity, translate;
    transition-duration: 0.2s;
}

.dialogue--staging .dialogue-box {
    opacity: 0;
    translate: 0 16px;
}

.dialogue-name-box {
    position: absolute;
    top: -24px;
    left: 40px;
    padding: 6px 20px;
    border-radius: 12px;
    background-color: rgb(253, 93, 131);
}

.dialogue-name {
    font-size: 26px;
    -unity-font-style: bold;
    color: rgb(255, 255, 255);
}

.dialogue-name-box {
    flex-direction: row;
    align-items: center;
}

.dialogue-affiliation {
    font-size: 18px;
    color: rgba(255, 255, 255, 0.85);
    margin-left: 12px;
}

/* 추가 글이 비었으면 숨긴다(dialogue-extra-<키> 요소 공통) */
.dialogue-extra--empty {
    display: none;
}

.dialogue-text {
    font-size: 30px;
    color: rgb(242, 242, 243);
    white-space: normal;
    margin-top: 6px;
    text-shadow: 0 2px 6px rgba(0, 0, 0, 0.6);
}

.dialogue--narration .dialogue-name-box {
    flex-direction: row;
    align-items: center;
}

.dialogue-affiliation {
    font-size: 18px;
    color: rgba(255, 255, 255, 0.85);
    margin-left: 12px;
}

/* 추가 글이 비었으면 숨긴다(dialogue-extra-<키> 요소 공통) */
.dialogue-extra--empty {
    display: none;
}

.dialogue-text {
    -unity-font-style: italic;
    color: rgba(242, 242, 243, 0.82);
}

.dialogue-next {
    position: absolute;
    right: 30px;
    bottom: 24px;
    width: 16px;
    height: 16px;
    rotate: 45deg;
    border-right-width: 3px;
    border-bottom-width: 3px;
    border-color: rgb(242, 242, 243);
    opacity: 0;
}

.dialogue--waiting .dialogue-next {
    opacity: 1;
}

/* 선택지 */
.dialogue-choices {
    position: absolute;
    left: 0;
    right: 0;
    top: 12%;
    bottom: 34%;
    align-items: center;
    justify-content: center;
}

.dialogue-choice {
    min-width: 44%;
    margin: 8px;
    padding: 18px 28px;
    border-radius: 14px;
    background-color: rgba(22, 23, 26, 0.88);
    border-width: 1px;
    border-color: rgba(255, 255, 255, 0.14);
    transition-property: background-color, scale;
    transition-duration: 0.12s;
}

.dialogue-choice:hover {
    background-color: rgba(253, 93, 131, 0.35);
    scale: 1.02 1.02;
}

.dialogue-choice--locked {
    opacity: 0.4;
}

.dialogue-choice--picked {
    background-color: rgba(253, 93, 131, 0.8);
}

.choice-text {
    font-size: 26px;
    color: rgb(242, 242, 243);
    -unity-text-align: middle-center;
    white-space: normal;
}

/* 용어 툴팁 */
.dialogue-tooltip {
    position: absolute;
    max-width: 480px;
    padding: 12px 16px;
    border-radius: 10px;
    background-color: rgba(14, 15, 17, 0.95);
    border-width: 1px;
    border-color: rgba(255, 255, 255, 0.14);
}

.dialogue-tooltip-text {
    font-size: 22px;
    color: rgb(242, 242, 243);
    white-space: normal;
}
";
    }

    // 대화 UI 인스펙터: UXML에 이름 규격의 요소가 모두 있는지 보여 준다.
    [CustomEditor(typeof(DialogueUI))]
    sealed class DialogueUIInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var ui = (DialogueUI)target;
            var document = ui.GetComponent<UIDocument>();
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("요소 연결 확인", EditorStyles.boldLabel);
            if (document == null || document.visualTreeAsset == null)
            {
                EditorGUILayout.HelpBox("UIDocument에 UXML(Source Asset)이 없습니다.", MessageType.Warning);
                return;
            }
            var missingRequired = 0;
            foreach (var (field, name, required, found) in DialogueTemplate.CheckNames(document.visualTreeAsset, ui.names))
            {
                if (!found && required) missingRequired++;
                var mark = found ? "✓" : required ? "✕ (필수)" : "– (없으면 그 기능만 꺼짐)";
                EditorGUILayout.LabelField($"{field}: {name}", mark);
            }
            if (missingRequired > 0) EditorGUILayout.HelpBox($"필수 요소 {missingRequired}개가 UXML에 없습니다. UXML의 name을 맞추거나 위 이름 칸을 고치세요.", MessageType.Error);
            if (ui.font == null && ui.fontAsset == null) EditorGUILayout.HelpBox("폰트가 비어 있습니다. 기본 폰트에 한글이 없으면 네모로 보입니다.", MessageType.Info);
        }
    }
}
