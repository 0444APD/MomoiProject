using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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

            // 그림(그라데이션 띠 · 별 · 버튼 아이콘)을 USS보다 먼저 — USS의 url()이 가져올 때 찾을 수 있게.
            var white = WriteWhiteSprite(folder);
            WriteImages(folder);
            var uss = WriteText(folder, "DialogueUI.uss", ShownUss(Uss));
            var uxml = WriteText(folder, "DialogueUI.uxml", Uxml);
            var choice = WriteText(folder, "DialogueChoice.uxml", ChoiceUxml);
            var theme = WriteText(folder, "DialogueTheme.tss", "@import url(\"unity-theme://default\");\n");
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
                    m.moveY = AnimationCurve.EaseInOut(0, 12, 1, 0);
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

        // ── 그림 ─────────────────────────────────────────────
        // 흰색 + 알파만 있는 그림이다. 색은 USS의 -unity-background-image-tint-color가 입힌다.

        // 글 띠의 알파(위 0 → 아래 1, 띠 높이에 대한 비율). 개발자 참고 그림(2026-10-20)의 밝기를 줄마다 재서 맞춘 값:
        // 띠 위 끝에서 투명 → 29% 지점 0.55 → 55% 지점 0.82 → 88%부터 0.9(아래쪽은 거의 가린다 — 흰 바탕에서도 글이 읽힌다).
        public static readonly (float at, float alpha)[] GradientKeys =
        {
            (0f, 0f), (0.10f, 0.12f), (0.20f, 0.30f), (0.29f, 0.55f), (0.41f, 0.70f), (0.55f, 0.82f), (0.715f, 0.88f), (0.88f, 0.90f), (1f, 0.90f),
        };

        // 글 띠의 색 — USS .dialogue-box의 -unity-background-image-tint-color와 같게 둔다(Linear 보정이 이 색으로 계산한다).
        static readonly Color GradientTint = new Color(24 / 255f, 24 / 255f, 28 / 255f);

        // 글 띠 그림의 크기: 세로는 띠 높이(1080 기준 372px)와 1:1, 가로는 4배로 늘어난다(디더 무늬가 화면 칸에 가깝게).
        const int GradientWidth = 480, GradientHeight = 372;

        static void WriteImages(string folder)
        {
            var rows = new float[GradientHeight];
            for (var y = 0; y < GradientHeight; y++) rows[y] = ShownAlpha(Monotone(GradientKeys, (y + 0.5f) / GradientHeight), GradientTint);
            WriteTexture(folder, "DialogueGradient.exr", GradientWidth, GradientHeight, (x, y) => rows[y] <= 0f ? 0f : rows[y] + Dither(x, y));
            WriteTexture(folder, "DialogueIconLog.png", 64, 64, Mask(LogIcon));
        }

        // 디더: Unity의 화면 출력은 8비트(Linear 값으로 저장)라, 어두운 쪽에서 1단계가 밝기 3단계쯤으로 커져 띠에 계단이 보인다.
        // 칸마다 ±1/255의 삼각 분포 잡음을 알파에 더해 계단 경계를 흩뜨린다(흰 바탕에서 출력 1단계 — 2026-10-21 실험실에서 재서 정한 크기).
        static float Dither(int x, int y) => (Hash01(x, y, 0x9E37) + Hash01(x, y, 0x85EB) - 1f) / 255f;

        static float Hash01(int x, int y, uint seed)
        {
            var h = (uint)x * 0x27D4EB2Du ^ (uint)y * 0x165667B1u ^ seed;
            h ^= h >> 15;
            h *= 0x2C1B3C6Du;
            h ^= h >> 12;
            h *= 0x297A2D39u;
            h ^= h >> 15;
            return (h & 0xFFFFFF) / 16777216f;
        }

        // 로그: 끝이 둥근 막대 셋.
        static bool LogIcon(float x, float y)
        {
            foreach (var row in new[] { 19f, 32f, 45f })
            {
                var dy = Mathf.Abs(y - row);
                if (dy > 2.75f) continue;
                if (x >= 15f && x <= 49f) return true;
                var end = x < 15f ? 15f : 49f;
                if ((x - end) * (x - end) + dy * dy <= 2.75f * 2.75f) return true;
            }
            return false;
        }


        // 모양 → 가장자리를 부드럽게 한 알파(한 칸을 4×4로 나눠 센다).
        static System.Func<int, int, float> Mask(System.Func<float, float, bool> inside) => (x, y) =>
        {
            var hits = 0;
            for (var i = 0; i < 4; i++)
                for (var j = 0; j < 4; j++)
                    if (inside(x + (i + 0.5f) / 4f, y + (j + 0.5f) / 4f)) hits++;
            return hits / 16f;
        };

        // ── Linear 색 공간 보정 ─────────────────────────────────
        // 이 템플릿의 반투명 값(그라데이션 알파 · USS의 rgba)은 디자인 도구처럼 감마 값으로 섞은 모양이다.
        // Linear 프로젝트의 UI는 Linear 값으로 섞어, 어두운 반투명이 그보다 옅게 보인다(흰 바탕 위 rgb(24,24,28) 알파 0.9 → 47이 아니라 92).
        // 그래서 Linear 프로젝트에서는 감마 섞기와 같아 보이는 알파로 바꿔 굽는다 — 밝기 0~1의 회색 바탕들에서 오차가 가장 작은 값(차이 6/255 안팎).
        // PanelSettings.forceGammaRendering은 sRGB 대상에 감마 값을 한 번 더 써서 더 옅어진다(2026-10-21 실험실에서 확인).
        static bool Linear => PlayerSettings.colorSpace == ColorSpace.Linear;

        static float ShownAlpha(float alpha, Color color)
        {
            if (!Linear || alpha <= 0f || alpha >= 1f) return alpha;
            float lo = 0f, hi = 1f;
            for (var i = 0; i < 40; i++)
            {
                var m1 = lo + (hi - lo) / 3f;
                var m2 = hi - (hi - lo) / 3f;
                if (BlendError(alpha, m1, color) < BlendError(alpha, m2, color)) hi = m2;
                else lo = m1;
            }
            return (lo + hi) / 2f;
        }

        static float BlendError(float alpha, float shown, Color color)
        {
            var error = 0f;
            for (var b = 0; b <= 10; b++)
            {
                var back = b / 10f;
                for (var ch = 0; ch < 3; ch++)
                {
                    var want = alpha * color[ch] + (1f - alpha) * back;
                    var got = Mathf.LinearToGammaSpace(shown * Mathf.GammaToLinearSpace(color[ch]) + (1f - shown) * Mathf.GammaToLinearSpace(back));
                    error += (got - want) * (got - want);
                }
            }
            return error;
        }

        static string ShownUss(string uss)
        {
            if (!Linear) return uss;
            var shown = Regex.Replace(uss, @"rgba\((\d+),\s*(\d+),\s*(\d+),\s*([\d.]+)\)", m =>
            {
                var color = new Color(int.Parse(m.Groups[1].Value) / 255f, int.Parse(m.Groups[2].Value) / 255f, int.Parse(m.Groups[3].Value) / 255f);
                var alpha = ShownAlpha(float.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture), color);
                return $"rgba({m.Groups[1].Value}, {m.Groups[2].Value}, {m.Groups[3].Value}, {alpha.ToString("0.###", CultureInfo.InvariantCulture)})";
            });
            return "/* Linear 색 공간: rgba의 알파를 디자인 값(감마 섞기)과 같아 보이게 바꿔 적었다(DialogueTemplate 'Linear 색 공간 보정'). */\n" + shown;
        }

        // 단조 3차 보간(Fritsch–Carlson) — 열쇠 사이가 꺾이지도 넘치지도 않는다(띠에 줄무늬가 생기지 않게).
        static float Monotone((float at, float alpha)[] keys, float t)
        {
            var n = keys.Length;
            if (t <= keys[0].at) return keys[0].alpha;
            if (t >= keys[n - 1].at) return keys[n - 1].alpha;
            var d = new float[n - 1];
            for (var k = 0; k < n - 1; k++) d[k] = (keys[k + 1].alpha - keys[k].alpha) / (keys[k + 1].at - keys[k].at);
            var m = new float[n];
            m[0] = d[0];
            m[n - 1] = d[n - 2];
            for (var k = 1; k < n - 1; k++) m[k] = d[k - 1] * d[k] <= 0 ? 0 : (d[k - 1] + d[k]) / 2f;
            for (var k = 0; k < n - 1; k++)
            {
                if (Mathf.Approximately(d[k], 0)) { m[k] = 0; m[k + 1] = 0; continue; }
                var a = m[k] / d[k];
                var b = m[k + 1] / d[k];
                var s = a * a + b * b;
                if (s <= 9f) continue;
                var tau = 3f / Mathf.Sqrt(s);
                m[k] = tau * a * d[k];
                m[k + 1] = tau * b * d[k];
            }
            var i = 0;
            while (t > keys[i + 1].at) i++;
            var h = keys[i + 1].at - keys[i].at;
            var u = (t - keys[i].at) / h;
            var u2 = u * u;
            var u3 = u2 * u;
            return (2 * u3 - 3 * u2 + 1) * keys[i].alpha + (u3 - 2 * u2 + u) * h * m[i] + (-2 * u3 + 3 * u2) * keys[i + 1].alpha + (u3 - u2) * h * m[i + 1];
        }

        // alphaAt(x, y): 위에서부터의 칸 좌표 → 알파. 파일이 .exr이면 16비트(Half)로 굽는다 — 디더 잡음(1/255 크기)이 8비트로 깎이지 않게.
        static void WriteTexture(string folder, string file, int width, int height, System.Func<int, int, float> alphaAt)
        {
            var path = $"{folder}/{file}";
            var exr = file.EndsWith(".exr");
            var texture = new Texture2D(width, height, exr ? TextureFormat.RGBAHalf : TextureFormat.RGBA32, false);
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                    texture.SetPixel(x, height - 1 - y, new Color(1f, 1f, 1f, Mathf.Clamp01(alphaAt(x, y))));
            File.WriteAllBytes(path, exr ? texture.EncodeToEXR(Texture2D.EXRFlags.None) : texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
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
        <!-- 글 띠: 화면 아래 전체 폭, 위는 투명하고 아래로 갈수록 어두운 그라데이션(DialogueGradient.exr). 이름 · 소속 · 대사는 왼쪽 18%에서 시작한다. -->
        <ui:VisualElement name=""dialogue-box"" class=""dialogue-box"">
            <ui:VisualElement name=""dialogue-name-box"" class=""dialogue-name-box"">
                <ui:VisualElement class=""dialogue-name-row"">
                    <ui:Label name=""dialogue-name"" class=""dialogue-name"" text="""" />
                    <!-- 프로젝트별 추가 글 예시: 스탠딩 프로필의 추가 글 키 affiliation(소속) — 이름 오른쪽에 늘 같은 간격으로. 비어 있으면 숨는다. 필요 없으면 지운다. -->
                    <ui:Label name=""dialogue-extra-affiliation"" class=""dialogue-extra dialogue-affiliation"" text="""" />
                </ui:VisualElement>
            </ui:VisualElement>
            <ui:Label name=""dialogue-text"" class=""dialogue-text"" text="""" enable-rich-text=""true"" />
        </ui:VisualElement>
        <!-- 오른쪽 위 둥근 버튼: 대사 로그. 같은 줄에 건너뛰기(name dialogue-skip-button — 다음 선택지 직전까지) · 자동 진행(dialogue-auto-button — 켜지면 루트에 dialogue--auto)을
             더할 수 있다(DialogueUI가 이름으로 찾는다). 클릭 대기 표시는 name dialogue-next(dialogue--waiting일 때 보이게). -->
        <ui:VisualElement class=""dialogue-menu"">
            <ui:VisualElement name=""dialogue-log-button"" class=""dialogue-round-button dialogue-log-button""><ui:VisualElement class=""dialogue-icon dialogue-icon--log"" picking-mode=""Ignore"" /></ui:VisualElement>
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
   · dialogue--choosing · dialogue--narration · dialogue--auto(자동 진행) · dialogue--speaker-<캐릭터 ID> (루트에 붙는다) */

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

/* 글 띠 — 화면 아래 전체 폭, 위는 투명하고 아래로 갈수록 어두운 그라데이션(개발자 참고 그림 2026-10-20).
   알파 곡선은 DialogueGradient.exr(흰색 + 알파, 계단이 보이지 않게 디더를 섞었다)에 들어 있고, 색은 아래 tint가 입힌다. 전체를 옅게 하려면 tint에 알파를 준다(rgba). */
.dialogue-box {
    position: absolute;
    left: 0;
    right: 0;
    bottom: 0;
    height: 372px;
    padding: 0 349px 136px 349px;
    justify-content: flex-end;
    background-image: url(""DialogueGradient.exr"");
    -unity-background-scale-mode: stretch-to-fill;
    -unity-background-image-tint-color: rgb(24, 24, 28);
    transition-property: opacity, translate;
    transition-duration: 0.2s;
}

.dialogue--staging .dialogue-box {
    opacity: 0;
    translate: 0 12px;
}

/* 이름 줄: 이름 · 오른쪽에 소속(금색, 세로 가운데 — 이름 길이와 상관없이 같은 간격).
   글은 모두 아래로 옅은 그림자(참고 그림 — 밝은 바탕에서도 획이 부드럽게 읽힌다). 굵기는 DialogueUI의 글꼴 칸(SemiBold 권장 — UI 가이드 1-10). */
.dialogue-name-box {
    flex-direction: column;
    align-items: flex-start;
    margin-bottom: 16px;
}

.dialogue-name-row {
    flex-direction: row;
    align-items: center;
}

.dialogue-name {
    margin: 0;
    padding: 0;
    font-size: 36px;
    -unity-font-style: bold;
    color: rgb(245, 244, 246);
    text-shadow: 0 2px 6px rgba(0, 0, 0, 0.45);
}

.dialogue-affiliation {
    margin: 0 0 0 16px;
    padding: 0;
    font-size: 18px;
    color: rgb(235, 214, 160);
    text-shadow: 0 1px 4px rgba(0, 0, 0, 0.45);
}

/* 추가 글이 비었으면 숨긴다(dialogue-extra-<키> 요소 공통) */
.dialogue-extra--empty {
    display: none;
}

.dialogue-text {
    margin: 0;
    padding: 0;
    font-size: 30px;
    color: rgb(242, 242, 244);
    white-space: normal;
    text-shadow: 0 2px 6px rgba(0, 0, 0, 0.45);
}

.dialogue--narration .dialogue-text {
    -unity-font-style: italic;
    color: rgb(214, 216, 222);
}

.dialogue--thought .dialogue-text {
    -unity-font-style: italic;
    color: rgb(186, 198, 228);
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

/* 오른쪽 위 둥근 버튼(로그) — 버튼을 더하면 같은 줄에 16 간격으로 선다 */
.dialogue-menu {
    position: absolute;
    top: 28px;
    right: 48px;
    flex-direction: row;
}

.dialogue-round-button {
    width: 52px;
    height: 52px;
    margin-left: 16px;
    border-radius: 26px;
    background-color: rgba(44, 46, 54, 0.9);
    align-items: center;
    justify-content: center;
    transition-property: background-color, scale;
    transition-duration: 0.08s, 0.06s;
}

.dialogue-round-button:hover {
    background-color: rgba(64, 67, 77, 0.92);
}

.dialogue-round-button:active {
    scale: 0.97 0.97;
}

.dialogue-icon {
    width: 24px;
    height: 24px;
    -unity-background-image-tint-color: rgb(240, 240, 242);
}

.dialogue-icon--log { background-image: url(""DialogueIconLog.png""); }

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
