using System.Collections.Generic;
using System.IO;
using System.Linq;
using GBible.Dialogue;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GBible.EditorTools.Graph
{
    // 다이얼로그 UI 템플릿: 모든 기능이 연결된 기본 프리팹과 UXML·USS·모션·패널 설정을 한 폴더에 만든다.
    // 프로젝트마다 모양을 바꿀 때는 이 폴더의 UXML·USS를 고치거나(요소 이름만 지키면 된다), 프리팹 변형을 만든다.
    public static class DialogueTemplate
    {
        const string Root = EditorDatabase.DataRoot + "/DialogueUI";


        // 새 폴더에 템플릿 한 벌을 만들고 프리팹을 돌려준다(이미 있으면 'DialogueUI 1' …).
        public static GameObject Create(string folder = null, bool registerAsDefault = true)
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
            if (registerAsDefault) RegisterDefault(prefab);
            AssetDatabase.SaveAssets();
            return prefab;
        }

        public const string SettingsPath = EditorDatabase.DataRoot + "/Resources/MomoiProject/DialogueSettings.asset";

        // 다이얼로그 관리자가 쓸 기본 프리팹으로 등록한다(설정이 없거나 프리팹이 비어 있을 때만. 이미 있으면 그대로 둔다).
        static void RegisterDefault(GameObject prefab)
        {
            var settings = AssetDatabase.LoadAssetAtPath<DialogueSettings>(SettingsPath);
            if (settings == null)
            {
                EditorDatabase.EnsureFolder(System.IO.Path.GetDirectoryName(SettingsPath).Replace('\\', '/'));
                settings = ScriptableObject.CreateInstance<DialogueSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }
            if (settings.playerPrefab != null) return;
            settings.playerPrefab = prefab;
            EditorUtility.SetDirty(settings);
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
                stage.imageRoot = Child(stageObject.transform, "Images");


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
        <!-- 지금 장소 이름(기본 추가 글 location). 장소가 없으면 숨는다. -->
        <ui:Label name=""dialogue-extra-location"" class=""dialogue-extra dialogue-location"" text="""" picking-mode=""Ignore"" />
        <ui:VisualElement name=""dialogue-choices"" class=""dialogue-choices"" picking-mode=""Ignore"" />
        <ui:VisualElement name=""dialogue-box"" class=""dialogue-box"">
            <ui:VisualElement name=""dialogue-name-box"" class=""dialogue-name-box"">
                <ui:Label name=""dialogue-name"" class=""dialogue-name"" text="""" />
                <!-- 프로젝트별 추가 글 예시: 스탠딩 프로필의 추가 글 키 affiliation(소속). 비어 있으면 숨는다. 필요 없으면 지운다. -->
                <ui:Label name=""dialogue-extra-affiliation"" class=""dialogue-extra dialogue-affiliation"" text="""" />
            </ui:VisualElement>
            <ui:Label name=""dialogue-text"" class=""dialogue-text"" text="""" enable-rich-text=""true"" />
            <ui:VisualElement name=""dialogue-next"" class=""dialogue-next"" picking-mode=""Ignore"" />
            <ui:Label name=""dialogue-log-button"" class=""dialogue-log-button"" text=""로그"" />
        </ui:VisualElement>
        <!-- 대사 로그: 로그 버튼·글상자 위로 휠을 굴리면 열린다. -->
        <ui:VisualElement name=""dialogue-log"" class=""dialogue-log"">
            <ui:VisualElement class=""dialogue-log__panel"">
                <ui:VisualElement class=""dialogue-log__head"">
                    <ui:Label class=""dialogue-log__title"" text=""대사 로그"" />
                    <ui:Label name=""dialogue-log-close"" class=""dialogue-log-close"" text=""✕"" />
                </ui:VisualElement>
                <ui:ScrollView name=""dialogue-log-list"" class=""dialogue-log__list"" />
            </ui:VisualElement>
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

        const string Uss = @"/* 다이얼로그 UI 기본 모양. 상태 클래스: dialogue--open · dialogue--staging · dialogue--typing · dialogue--waiting
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

/* 글상자 — 매트한 종이 흰색(테두리·그림자 없음, 차가운 검정 + 종이 — GBible AI/UiGuide.md 3절). 결은 GBible 재질 텍스처 */
.dialogue-box {
    position: absolute;
    left: 18%;
    right: 18%;
    bottom: 5%;
    min-height: 120px;
    padding: 24px 30px 22px 30px;
    border-radius: 6px;
    background-color: rgb(244, 243, 240);
    transition-property: opacity, translate;
    transition-duration: 0.2s;
}

.dialogue--staging .dialogue-box {
    opacity: 0;
    translate: 0 12px;
}

.dialogue-name-box {
    position: absolute;
    top: -16px;
    left: 24px;
    padding: 3px 14px;
    border-radius: 4px;
    background-color: rgb(36, 37, 41);
    flex-direction: row;
    align-items: center;
}

.dialogue-name {
    font-size: 17px;
    -unity-font-style: bold;
    color: rgb(236, 236, 238);
}

.dialogue-affiliation {
    font-size: 13px;
    color: rgba(236, 236, 238, 0.75);
    margin-left: 10px;
}

/* 추가 글이 비었으면 숨긴다(dialogue-extra-<키> 요소 공통) */
.dialogue-extra--empty {
    display: none;
}

.dialogue-text {
    font-size: 19px;
    color: rgb(38, 38, 42);
    white-space: normal;
    margin-top: 4px;
}

.dialogue--narration .dialogue-text {
    -unity-font-style: italic;
    color: rgb(110, 110, 116);
}

.dialogue--thought .dialogue-text {
    -unity-font-style: italic;
    color: rgb(80, 90, 120);
}

.dialogue-location {
    position: absolute;
    left: 24px;
    top: 20px;
    padding: 4px 12px;
    border-radius: 4px;
    background-color: rgb(54, 56, 61);
    font-size: 15px;
    color: rgb(226, 228, 232);
}

.dialogue-next {
    position: absolute;
    right: 18px;
    bottom: 14px;
    width: 10px;
    height: 10px;
    rotate: 45deg;
    border-right-width: 2px;
    border-bottom-width: 2px;
    border-color: rgb(38, 38, 42);
    opacity: 0;
}

.dialogue--waiting .dialogue-next {
    opacity: 1;
}

/* 선택지 — 폴더 색 카드(고르면 강조색으로 채움, 테두리 없음) */
.dialogue-choices {
    position: absolute;
    left: 0;
    right: 0;
    top: 14%;
    bottom: 32%;
    align-items: center;
    justify-content: center;
}

.dialogue-choice {
    min-width: 40%;
    margin: 6px;
    padding: 12px 22px;
    border-radius: 4px;
    background-color: rgb(222, 221, 218);
    transition-property: background-color;
    transition-duration: 0.1s;
}

.dialogue-choice:hover {
    background-color: rgb(210, 209, 206);
}

.dialogue-choice--locked {
    opacity: 0.4;
}

.dialogue-choice--picked {
    background-color: rgb(204, 184, 146);
}

.choice-text {
    font-size: 18px;
    color: rgb(52, 52, 56);
    -unity-text-align: middle-center;
    white-space: normal;
}

/* 대사 로그: 위에서 아래로, 내가 고른 선택지는 오른쪽 */
.dialogue-log-button {
    position: absolute;
    right: 12px;
    top: 8px;
    font-size: 13px;
    color: rgba(38, 38, 42, 0.6);
    padding: 2px 8px;
    border-radius: 4px;
}

.dialogue-log-button:hover {
    color: rgb(38, 38, 42);
    background-color: rgba(38, 38, 42, 0.08);
}

.dialogue-log {
    position: absolute;
    left: 0;
    top: 0;
    right: 0;
    bottom: 0;
    background-color: rgba(0, 0, 0, 0.5);
    align-items: center;
    justify-content: center;
}

.dialogue-log__panel {
    width: 60%;
    height: 80%;
    padding: 18px 24px;
    border-radius: 6px;
    background-color: rgb(36, 37, 41);
}

.dialogue-log__head {
    flex-direction: row;
    align-items: center;
    margin-bottom: 10px;
}

.dialogue-log__title {
    flex-grow: 1;
    font-size: 20px;
    -unity-font-style: bold;
    color: rgb(236, 236, 238);
}

.dialogue-log-close {
    font-size: 18px;
    color: rgba(236, 236, 238, 0.6);
    padding: 2px 8px;
}

.dialogue-log__list {
    flex-grow: 1;
}

.dialogue-log__entry {
    flex-direction: row;
    align-items: flex-start;
    margin: 8px 0;
}

.dialogue-log__portrait {
    width: 56px;
    height: 56px;
    flex-shrink: 0;
    margin-right: 12px;
    border-radius: 4px;
    background-color: rgba(255, 255, 255, 0.06);
    -unity-background-scale-mode: scale-and-crop;
    background-position-y: top;
}

.dialogue-log__body {
    flex-shrink: 1;
    max-width: 80%;
}

.dialogue-log__name {
    font-size: 15px;
    -unity-font-style: bold;
    color: rgb(204, 184, 146);
}

.dialogue-log__text {
    font-size: 17px;
    color: rgb(236, 236, 238);
    white-space: normal;
}

.dialogue-log__entry--choice {
    flex-direction: row-reverse;
}

.dialogue-log__entry--choice .dialogue-log__body {
    padding: 8px 14px;
    border-radius: 4px;
    background-color: rgba(204, 184, 146, 0.25);
}

.dialogue-log__entry--narration .dialogue-log__text,
.dialogue-log__entry--thought .dialogue-log__text {
    -unity-font-style: italic;
    color: rgba(236, 236, 238, 0.75);
}

/* 용어 툴팁 — 종이 */
.dialogue-tooltip {
    position: absolute;
    max-width: 420px;
    padding: 10px 14px;
    border-radius: 4px;
    background-color: rgb(244, 243, 240);
}

.dialogue-tooltip-text {
    font-size: 16px;
    color: rgb(38, 38, 42);
    white-space: normal;
}
";
    }

    // 다이얼로그 UI 인스펙터: UXML에 이름 규격의 요소가 모두 있는지 보여 준다.
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
