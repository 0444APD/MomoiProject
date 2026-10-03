using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GBible.Data;
using UnityEditor;
using UnityEngine;

namespace GBible.EditorTools
{
    // 스프라이트 규격 베이크: 원본 일러 + 맞춤(중심·배율) → 그룹의 규격마다 잘라 PNG 스프라이트로 저장한다.
    // 잘라 내기는 GPU로 하므로 원본 텍스처에 읽기 권한(Read/Write)이 없어도 된다.
    // 규격이 바뀌면 그 규격의 베이크를 다시 만들고, 예외 촬영이 걸린 것은 '확인 필요'로 표시한다.
    public static class SpriteSpecEditor
    {
        public const string RegistryPath = EditorDatabase.DataRoot + "/Resources/MomoiProject/SpriteSpecs.asset";
        public const string OutputRoot = EditorDatabase.DataRoot + "/Sprites";

        // ── 맞춤 기록 저장소 ───────────────────────────────────

        public static SpriteSpecDatabase Registry
        {
            get
            {
                var registry = AssetDatabase.LoadAssetAtPath<SpriteSpecDatabase>(RegistryPath);
                if (registry != null) return registry;
                EditorDatabase.EnsureFolder(Path.GetDirectoryName(RegistryPath).Replace('\\', '/'));
                registry = ScriptableObject.CreateInstance<SpriteSpecDatabase>();
                AssetDatabase.CreateAsset(registry, RegistryPath);
                return registry;
            }
        }

        public static IEnumerable<SpritePresetData> PresetsOf(string group) =>
            EditorDatabase.Instance.OfType<SpritePresetData>().Where(p => p.group == group).OrderBy(p => p.Id, StringComparer.Ordinal);

        // 스프라이트 필드의 규칙(규격 그룹·미리보기 규격).
        public static FieldRuleData RuleFor(FieldInfo field)
        {
            var rule = EditorDatabase.RuleFor(field);
            return rule != null && !string.IsNullOrEmpty(rule.spriteGroup) ? rule : null;
        }

        // 목록·카드용 미리보기: 규칙의 미리보기 규격 베이크본(없으면 원본).
        public static UnityEngine.Object Preview(UnityEngine.Object image, FieldInfo field)
        {
            if (!(image is Sprite sprite)) return image;
            var rule = RuleFor(field);
            if (rule == null || string.IsNullOrEmpty(rule.previewPreset)) return image;
            var bake = Registry.Find(sprite)?.BakeFor(rule.previewPreset);
            return bake?.sprite != null ? bake.sprite : image;
        }

        // ── 잘라 낼 영역 ───────────────────────────────────────
        // 배율 1 = 규격 비율의 가장 큰 사각형이 원본 안에 꽉 차게. 배율이 클수록 확대(작은 영역). 영역은 원본 밖으로 나가지 않는다.
        public static Rect Crop(Rect source, Vector2 center, float zoom, int width, int height)
        {
            var aspect = (float)width / height;
            float baseW, baseH;
            if (source.width / source.height > aspect)
            {
                baseH = source.height;
                baseW = source.height * aspect;
            }
            else
            {
                baseW = source.width;
                baseH = source.width / aspect;
            }
            zoom = Mathf.Max(1f, zoom);
            var w = baseW / zoom;
            var h = baseH / zoom;
            var x = Mathf.Clamp(center.x * source.width - w / 2f, 0f, source.width - w);
            var y = Mathf.Clamp(center.y * source.height - h / 2f, 0f, source.height - h);
            return new Rect(source.x + x, source.y + y, w, h);
        }

        public static (Vector2 center, float zoom) FramingFor(SpriteFraming framing, string preset)
        {
            var custom = framing.OverrideFor(preset);
            return custom != null ? (custom.center, custom.zoom) : (framing.center, framing.zoom);
        }

        // ── 그리기 ─────────────────────────────────────────────

        // 원본의 한 영역을 size 크기로 그린 텍스처(미리보기·베이크 공용).
        public static Texture2D Render(Sprite source, Rect crop, int width, int height)
        {
            var texture = source.texture;
            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            try
            {
                var scale = new Vector2(crop.width / texture.width, crop.height / texture.height);
                var offset = new Vector2(crop.x / texture.width, crop.y / texture.height);
                Graphics.Blit(texture, rt, scale, offset);
                RenderTexture.active = rt;
                var result = new Texture2D(width, height, TextureFormat.RGBA32, false);
                result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                result.Apply();
                return result;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        // ── 베이크 ─────────────────────────────────────────────

        // 맞춤을 저장하고 그룹의 모든 규격을 베이크한다. 돌려주는 값: 만든 스프라이트 수.
        public static int Apply(Sprite source, string group, Vector2 center, float zoom, IDictionary<string, (Vector2 center, float zoom)> overrides)
        {
            var registry = Registry;
            Undo.RecordObject(registry, "스프라이트 맞춤");
            var framing = registry.FindOrAdd(source, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source)));
            framing.group = group;
            framing.center = center;
            framing.zoom = zoom;
            framing.overrides.Clear();
            if (overrides != null)
                foreach (var pair in overrides)
                    framing.overrides.Add(new SpriteFramingOverride { preset = pair.Key, center = pair.Value.center, zoom = pair.Value.zoom });
            var count = 0;
            foreach (var preset in PresetsOf(group))
                if (Bake(framing, preset)) count++;
            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssetIfDirty(registry);
            SpriteSpecs.ClearCache();
            return count;
        }

        static bool Bake(SpriteFraming framing, SpritePresetData preset)
        {
            if (framing.source == null) return false;
            var (center, zoom) = FramingFor(framing, preset.Id);
            var crop = Crop(framing.source.textureRect, center, zoom, preset.width, preset.height);
            var texture = Render(framing.source, crop, preset.width, preset.height);
            var folder = $"{OutputRoot}/{Safe(framing.group)}/{Safe(preset.Id)}";
            EditorDatabase.EnsureFolder(folder);
            var path = $"{folder}/{Safe(framing.source.name)}_{Safe(preset.Id)}.png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            var bake = framing.BakeFor(preset.Id);
            if (bake == null) framing.baked.Add(bake = new SpriteBake { preset = preset.Id });
            bake.sprite = sprite;
            bake.revision = preset.Revision;
            return sprite != null;
        }

        // 규격 크기가 바뀌었거나 그룹에 규격이 추가된 경우 다시 베이크한다. 예외 촬영이 걸린 것은 '확인 필요'로 표시.
        // 돌려주는 값: (다시 베이크한 수, 확인이 필요한 예외 목록)
        public static (int rebaked, List<string> review) RebakeStale()
        {
            var registry = AssetDatabase.LoadAssetAtPath<SpriteSpecDatabase>(RegistryPath);
            var review = new List<string>();
            if (registry == null) return (0, review);
            int rebaked = 0;
            foreach (var framing in registry.Framings)
            {
                if (framing.source == null || string.IsNullOrEmpty(framing.group)) continue;
                foreach (var preset in PresetsOf(framing.group))
                {
                    var bake = framing.BakeFor(preset.Id);
                    if (bake != null && bake.sprite != null && bake.revision == preset.Revision) continue;
                    var custom = framing.OverrideFor(preset.Id);
                    if (custom != null && bake != null)
                    {
                        custom.needsReview = true;
                        custom.note = $"규격 '{preset.DisplayName}'이(가) {preset.width}×{preset.height}로 바뀌어 예외 촬영을 확인해야 합니다.";
                        review.Add($"{framing.source.name} · {preset.DisplayName}");
                    }
                    if (Bake(framing, preset)) rebaked++;
                }
            }
            if (rebaked > 0 || review.Count > 0)
            {
                EditorUtility.SetDirty(registry);
                AssetDatabase.SaveAssetIfDirty(registry);
                SpriteSpecs.ClearCache();
            }
            return (rebaked, review);
        }

        static bool _scheduled;

        [InitializeOnLoadMethod]
        static void Hook()
        {
            EditorDatabase.Changed -= Schedule;
            EditorDatabase.Changed += Schedule;
        }

        static void Schedule()
        {
            if (_scheduled) return;
            _scheduled = true;
            EditorApplication.delayCall += () =>
            {
                _scheduled = false;
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
                // DB에 적용 대기 중인 규격 변경은 적용된 뒤에 베이크한다.
                if (EditorDatabase.Staging.HasChanges) return;
                var (rebaked, review) = RebakeStale();
                if (rebaked > 0) Debug.Log($"[MomoiProject] 바뀐 스프라이트 규격으로 {rebaked}개를 다시 베이크했습니다.");
                if (review.Count > 0)
                    Debug.LogWarning("[MomoiProject] 규격이 바뀌어 예외 촬영을 확인해야 합니다(DB › 스프라이트 규격에서 표시):\n  " + string.Join("\n  ", review));
            };
        }

        [MenuItem("MomoiProject/스프라이트 규격 다시 베이크", priority = 31)]
        static void RebakeAllMenu()
        {
            var registry = AssetDatabase.LoadAssetAtPath<SpriteSpecDatabase>(RegistryPath);
            if (registry == null) return;
            foreach (var framing in registry.Framings)
                foreach (var bake in framing.baked) bake.revision = 0;
            var (rebaked, _) = RebakeStale();
            Debug.Log($"[MomoiProject] 스프라이트 {rebaked}개를 다시 베이크했습니다.");
        }

        static string Safe(string name) => string.Concat((name ?? "").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    }
}
