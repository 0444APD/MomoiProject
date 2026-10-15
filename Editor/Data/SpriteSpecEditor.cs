using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using GBible.Data;
using UnityEditor;
using UnityEngine;

namespace GBible.EditorTools
{
    // 스프라이트 틀 굽기: 원본 일러 + 맞춤(중심·배율, 크기마다 예외 촬영) → 틀의 크기마다 잘라 PNG 스프라이트로 저장한다.
    // 잘라 내기는 GPU로 하므로 원본 텍스처에 읽기 권한(Read/Write)이 없어도 된다.
    // 틀의 크기가 바뀌면 그 크기를 다시 굽고, 예외 촬영이 걸린 것은 '확인 필요'로 표시한다. 틀에서 뺀 크기의 구운 파일은 백업으로 옮긴다.
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

        static SpriteSpecDatabase ExistingRegistry => AssetDatabase.LoadAssetAtPath<SpriteSpecDatabase>(RegistryPath);

        public static SpriteFrameData FrameOf(string id) => string.IsNullOrEmpty(id) ? null : EditorDatabase.Instance.FindById(id) as SpriteFrameData;

        public static IEnumerable<SpriteFrameData> Frames => EditorDatabase.Instance.OfType<SpriteFrameData>();

        // 틀의 크기(이름이 맞고 크기가 범위 안인 것만, 표 순서대로).
        public static List<SpriteFrameSize> SizesOf(string frame)
        {
            var data = FrameOf(frame);
            var result = new List<SpriteFrameSize>();
            if (data == null) return result;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var size in data.sizes)
                if (size != null && SpriteFrameData.IsValidName(size.name, out _) && seen.Add(size.name) &&
                    SpriteFrameData.IsValidSize(size.width) && SpriteFrameData.IsValidSize(size.height))
                    result.Add(size);
            return result;
        }

        // 이 틀로 구운 원본들(이름순).
        public static List<SpriteFraming> FramingsOf(string frame)
        {
            var registry = ExistingRegistry;
            if (registry == null || string.IsNullOrEmpty(frame)) return new List<SpriteFraming>();
            return registry.Framings.Where(f => f != null && f.frame == frame && f.source != null).OrderBy(f => f.source.name, StringComparer.Ordinal).ToList();
        }

        // 스프라이트 필드의 규칙(틀·미리보기 크기). 틀이 없으면 null.
        public static FieldRuleData RuleFor(FieldInfo field)
        {
            var rule = EditorDatabase.RuleFor(field);
            return rule != null && !string.IsNullOrEmpty(rule.spriteFrame) ? rule : null;
        }

        // 목록·카드용 미리보기: 규칙의 미리보기 크기로 구운 이미지(없으면 원본).
        public static UnityEngine.Object Preview(UnityEngine.Object image, FieldInfo field)
        {
            if (!(image is Sprite sprite)) return image;
            var rule = RuleFor(field);
            if (rule == null || string.IsNullOrEmpty(rule.previewSize)) return image;
            var bake = ExistingRegistry?.Find(sprite)?.BakeFor(rule.previewSize);
            return bake?.sprite != null ? bake.sprite : image;
        }

        // ── 잘라 낼 영역 ───────────────────────────────────────
        // 배율 1 = 크기 비율의 가장 큰 사각형이 원본 안에 꽉 차게. 배율이 클수록 확대(작은 영역). 영역은 원본 밖으로 나가지 않는다.
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

        public static (Vector2 center, float zoom) FramingFor(SpriteFraming framing, string size)
        {
            var custom = framing.OverrideFor(size);
            return custom != null ? (custom.center, custom.zoom) : (framing.center, framing.zoom);
        }

        // ── 그리기 ─────────────────────────────────────────────

        // 원본의 한 영역을 size 크기로 그린 텍스처(미리보기·굽기 공용).
        // 2배 넘게 줄일 때는 목표의 2^n배로 먼저 그린 뒤 반씩 줄인다(2×2 평균) — 한 번에 줄이면 밉맵이 없는 원본(스프라이트 기본)의 픽셀을 건너뛰어 거칠어진다.
        // 점 필터(픽셀 아트) 원본은 섞지 않도록 한 번에 그린다.
        public static Texture2D Render(Sprite source, Rect crop, int width, int height)
        {
            var texture = source.texture;
            var steps = 0;
            if (texture.filterMode != FilterMode.Point)
                while (steps < 12 && (width << (steps + 1)) < crop.width && (height << (steps + 1)) < crop.height) steps++;
            var rt = RenderTexture.GetTemporary(width << steps, height << steps, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            try
            {
                var scale = new Vector2(crop.width / texture.width, crop.height / texture.height);
                var offset = new Vector2(crop.x / texture.width, crop.y / texture.height);
                Graphics.Blit(texture, rt, scale, offset);
                for (; steps > 0; steps--)
                {
                    var half = RenderTexture.GetTemporary(width << (steps - 1), height << (steps - 1), 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    rt.filterMode = FilterMode.Bilinear;
                    Graphics.Blit(rt, half);
                    RenderTexture.ReleaseTemporary(rt);
                    rt = half;
                }
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

        // ── 굽기 ───────────────────────────────────────────────

        // 맞춤을 저장하고 틀의 모든 크기를 한꺼번에 굽는다. overrides: 크기 이름 → 따로 잡은 위치(예외 촬영). 돌려주는 값: 만든 스프라이트 수.
        // keepReview: 예외 촬영의 '확인 필요'를 그대로 둔다(맞춤을 바꾸지 않고 다시 굽기 — BakeWithCurrent). 맞춤 창의 적용은 본 것으로 보고 지운다.
        public static int Apply(Sprite source, string frame, Vector2 center, float zoom, IDictionary<string, (Vector2 center, float zoom)> overrides, bool keepReview = false)
        {
            if (source == null || FrameOf(frame) == null) return 0;
            var registry = Registry;
            Undo.RecordObject(registry, "스프라이트 맞춤");
            var framing = registry.FindOrAdd(source, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source)));
            var moved = PruneOtherFrame(framing, frame);
            var before = framing.overrides.ToList();
            framing.frame = frame;
            framing.center = center;
            framing.zoom = zoom;
            framing.overrides.Clear();
            if (overrides != null)
                foreach (var pair in overrides)
                {
                    var kept = keepReview ? before.Find(o => o.size == pair.Key && o.needsReview) : null;
                    framing.overrides.Add(new SpriteFramingOverride { size = pair.Key, center = pair.Value.center, zoom = pair.Value.zoom, needsReview = kept != null, note = kept?.note ?? "" });
                }
            var count = 0;
            foreach (var size in SizesOf(frame))
                if (Bake(framing, size)) count++;
            Save(registry, refresh: moved > 0);
            if (moved > 0) Debug.Log($"[MomoiProject] '{source.name}'을(를) 다른 틀로 옮겨, 예전 틀로 구운 이미지 {moved}개를 {BackupFolder()}에 옮겨 두었습니다.");
            return count;
        }

        // 맞춤을 바꾸지 않고 다시 굽는다(여러 원본을 한꺼번에 끌어 놓았을 때·모두 다시 굽기 — 처음이면 가운데·배율 1,
        // 다른 틀로 구웠던 원본이면 맞춤 창처럼 공통 맞춤만 이어받고 예외는 새로). '확인 필요'는 남긴다. 돌려주는 값: 만든 스프라이트 수.
        public static int BakeWithCurrent(Sprite source, string frame)
        {
            var existing = ExistingRegistry?.Find(source);
            if (existing == null) return Apply(source, frame, new Vector2(0.5f, 0.5f), 1f, null);
            var overrides = existing.frame == frame ? existing.overrides.GroupBy(o => o.size).ToDictionary(g => g.Key, g => (g.First().center, g.First().zoom)) : null;
            return Apply(source, frame, existing.center, existing.zoom, overrides, keepReview: true);
        }

        static bool Bake(SpriteFraming framing, SpriteFrameSize size)
        {
            if (framing.source == null) return false;
            var (center, zoom) = FramingFor(framing, size.name);
            var crop = Crop(framing.source.textureRect, center, zoom, size.width, size.height);
            var texture = Render(framing.source, crop, size.width, size.height);
            var path = OutputPath(framing, size.name);
            var previous = framing.BakeFor(size.name)?.sprite;
            EditorDatabase.EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                // 2048보다 큰 크기(배경 등)가 가져오기 최대 크기(기본 2048)로 줄어들지 않게.
                var longest = Mathf.Max(size.width, size.height);
                if (importer.maxTextureSize < longest) importer.maxTextureSize = Mathf.NextPowerOfTwo(longest);
                // 구울 때의 맞춤·크기를 파일에 적어 둔다 — 되돌리기(Ctrl+Z)로 기록만 돌아갔는지 견준다(RebakeStale checkFiles).
                importer.userData = Stamp(center, zoom, size);
                importer.SaveAndReimport();
            }
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            // 파일 이름 규칙이 바뀌어 예전 자리에 구운 파일이 남았으면 백업으로(같은 원본의 같은 크기가 두 파일로 남지 않게).
            // 대소문자만 다른 경로(틀 ID의 대소문자만 바꿈 등)는 Windows에서 방금 쓴 그 파일이다 — 옮기지 않는다.
            if (previous != null && !string.Equals(AssetDatabase.GetAssetPath(previous), path, StringComparison.OrdinalIgnoreCase)) MoveToBackup(previous);
            var bake = framing.BakeFor(size.name);
            if (bake == null) framing.baked.Add(bake = new SpriteBake { size = size.name });
            bake.sprite = sprite;
            bake.revision = size.Revision;
            return sprite != null;
        }

        static string Stamp(Vector2 center, float zoom, SpriteFrameSize size) =>
            string.Format(CultureInfo.InvariantCulture, "momoi-frame {0:R} {1:R} {2:R} {3}x{4}", center.x, center.y, zoom, size.width, size.height);

        // 구운 파일에 적어 둔 맞춤·크기가 지금 기록과 다른가. 적어 둔 것이 없는 예전 파일은 같다고 본다.
        static bool FileDiffers(SpriteFraming framing, SpriteFrameSize size, Sprite baked)
        {
            var stamp = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(baked))?.userData;
            if (string.IsNullOrEmpty(stamp)) return false;
            var (center, zoom) = FramingFor(framing, size.name);
            return stamp != Stamp(center, zoom, size);
        }

        // 파일 이름에 원본 GUID 앞 8자를 붙인다 — 다른 폴더의 같은 이름 원본이 같은 파일을 덮어쓰지 않게.
        static string OutputPath(SpriteFraming framing, string size)
        {
            var guid = framing.sourceGuid ?? "";
            if (guid.Length == 0) guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(framing.source)) ?? "";
            var tag = guid.Length >= 8 ? "_" + guid.Substring(0, 8) : "";
            return $"{OutputRoot}/{Safe(framing.frame)}/{Safe(size)}/{Safe(framing.source.name)}{tag}_{Safe(size)}.png";
        }

        // 틀에 없는 크기(틀에서 빼거나 이름을 바꾼 크기)의 기록을 지우고 구운 파일은 백업으로 옮긴다. 돌려주는 값: 옮긴 파일 수.
        static int PruneMissing(SpriteFraming framing, ICollection<string> sizes)
        {
            var moved = 0;
            foreach (var bake in framing.baked.Where(b => !sizes.Contains(b.size)).ToList())
            {
                if (MoveToBackup(bake.sprite)) moved++;
                framing.baked.Remove(bake);
            }
            framing.overrides.RemoveAll(o => !sizes.Contains(o.size));
            return moved;
        }

        // 원본을 다른 틀로 옮길 때: 예전 틀로 구운 것을 치운다.
        static int PruneOtherFrame(SpriteFraming framing, string frame)
        {
            if (string.IsNullOrEmpty(framing.frame) || framing.frame == frame) return 0;
            var moved = 0;
            foreach (var bake in framing.baked)
                if (MoveToBackup(bake.sprite)) moved++;
            framing.baked.Clear();
            framing.overrides.Clear();
            return moved;
        }

        static string _backup;

        static string BackupFolder() => _backup ??= Path.Combine(ProjectReset.BackupFolder, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-sprites").Replace('\\', '/');

        // 구운 파일을 MomoiProjectBackups/<시각>-sprites/ 아래 같은 상대 경로로 옮긴다(지우지 않는다 — 다른 곳이 그 파일을 직접 썼을 수 있다).
        static bool MoveToBackup(Sprite sprite)
        {
            if (sprite == null) return false;
            var path = AssetDatabase.GetAssetPath(sprite);
            if (string.IsNullOrEmpty(path) || !path.StartsWith(OutputRoot + "/", StringComparison.Ordinal) || !File.Exists(path)) return false;
            var target = Path.Combine(BackupFolder(), path).Replace('\\', '/');
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                if (File.Exists(target)) target = Path.Combine(Path.GetDirectoryName(target), Path.GetFileNameWithoutExtension(target) + "_" + DateTime.Now.Ticks + Path.GetExtension(target)).Replace('\\', '/');
                File.Move(path, target);
                if (File.Exists(path + ".meta")) File.Move(path + ".meta", target + ".meta");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MomoiProject] 구운 이미지 {path}를 백업으로 옮기지 못했습니다: {e.Message}");
                return false;
            }
        }

        // refresh: 구운 파일을 백업으로 옮겼으면 Unity가 사라진 파일을 알게 한다.
        static void Save(SpriteSpecDatabase registry, bool refresh)
        {
            registry.Changed();
            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssetIfDirty(registry);
            _savedFingerprint = Fingerprint(registry);
            if (refresh) AssetDatabase.Refresh();
            SpriteSpecs.ClearCache();
        }

        // 마지막으로 저장한 기록의 내용을 줄인 값. 되돌리기가 기록을 바꿨는지(저장한 뒤와 달라졌는지) 알아낸다.
        static int? _savedFingerprint;

        static int Fingerprint(SpriteSpecDatabase registry)
        {
            unchecked
            {
                var hash = 17;
                foreach (var f in registry.Framings)
                {
                    if (f == null) continue;
                    hash = hash * 31 + (f.source != null ? f.source.GetHashCode() : 0);
                    hash = hash * 31 + (f.frame ?? "").GetHashCode();
                    hash = hash * 31 + f.center.GetHashCode();
                    hash = hash * 31 + f.zoom.GetHashCode();
                    foreach (var o in f.overrides) hash = hash * 31 + ((o.size ?? "").GetHashCode() ^ o.center.GetHashCode() ^ o.zoom.GetHashCode());
                    foreach (var b in f.baked) hash = hash * 31 + ((b.size ?? "").GetHashCode() ^ b.revision ^ (b.sprite != null ? b.sprite.GetHashCode() : 0));
                }
                return hash;
            }
        }

        // 되돌리기(Ctrl+Z)가 기록을 바꿨는가: 저장 안 된 상태이거나 마지막으로 저장한 내용과 다르다.
        static bool ChangedSinceSave(SpriteSpecDatabase registry) =>
            registry != null && (EditorUtility.IsDirty(registry) || _savedFingerprint != Fingerprint(registry));

        // 틀의 크기가 바뀌었거나 틀에 크기가 더해진 경우 다시 굽는다. 예외 촬영이 걸린 크기의 가로·세로가 바뀌었으면 '확인 필요'로 표시.
        // 틀에서 빠진 크기의 구운 파일은 백업으로 옮긴다 — 값이 잘못되어 지금 굽지 않는 줄은 빠진 것이 아니다(고치는 동안 구운 이미지·예외 촬영을 남긴다).
        // all: 모두 다시 굽기. checkFiles: 구운 파일에 적어 둔 맞춤·크기와도 견준다(되돌리기 뒤 — 파일은 되돌리기로 돌아오지 않는다).
        // 돌려주는 값: (다시 구운 수, 확인이 필요한 예외 목록, 백업으로 옮긴 수)
        public static (int rebaked, List<string> review, int moved) RebakeStale(bool all = false, bool checkFiles = false)
        {
            var registry = ExistingRegistry;
            var review = new List<string>();
            if (registry == null) return (0, review, 0);
            int rebaked = 0, moved = 0;
            var changed = false;
            foreach (var framing in registry.Framings)
            {
                if (framing == null || framing.source == null || string.IsNullOrEmpty(framing.frame)) continue;
                var data = FrameOf(framing.frame);
                if (data == null) continue;   // 틀 파일이 없으면(옮기는 중 등) 그대로 둔다
                var sizes = SizesOf(framing.frame);
                var names = new HashSet<string>(data.sizes.Where(s => s != null && !string.IsNullOrEmpty(s.name)).Select(s => s.name), StringComparer.Ordinal);
                var before = framing.baked.Count + framing.overrides.Count;
                moved += PruneMissing(framing, names);
                if (framing.baked.Count + framing.overrides.Count != before) changed = true;
                foreach (var size in sizes)
                {
                    var bake = framing.BakeFor(size.name);
                    var resized = bake != null && bake.revision != size.Revision;
                    if (!all && bake != null && bake.sprite != null && !resized && !(checkFiles && FileDiffers(framing, size, bake.sprite))) continue;
                    var custom = framing.OverrideFor(size.name);
                    if (custom != null && resized)
                    {
                        custom.needsReview = true;
                        custom.note = $"크기 '{size.name}'이(가) {size.width}×{size.height}로 바뀌어 예외 촬영을 확인해야 합니다.";
                        review.Add($"{framing.source.name} · {data.Id}.{size.name}");
                    }
                    if (Bake(framing, size)) rebaked++;
                    changed = true;
                }
            }
            // 되돌린 기록은 견준 뒤 저장한다(저장 안 된 상태로 남으면 다음 되돌리기마다 다시 견준다).
            if (changed || checkFiles) Save(registry, refresh: moved > 0);
            return (rebaked, review, moved);
        }

        static bool _scheduled;
        static bool _undone;

        [InitializeOnLoadMethod]
        static void Hook()
        {
            EditorDatabase.Changed -= Schedule;
            EditorDatabase.Changed += Schedule;
            Undo.undoRedoPerformed -= OnUndoRedo;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.delayCall += () =>
            {
                if (ExistingRegistry is SpriteSpecDatabase registry && !EditorUtility.IsDirty(registry)) _savedFingerprint = Fingerprint(registry);
            };
            EditorApplication.delayCall += MigrateLegacy;
        }

        // 되돌리기(Ctrl+Z)·다시 하기: 맞춤 기록이 돌아갔으면(맞춤 적용·틀의 표 고치기를 되돌림) 구운 파일과 다시 맞추고,
        // 틀의 표만 돌아갔으면(틀 인스펙터가 닫혀 있어도) 바뀐 크기로 다시 굽는다.
        static void OnUndoRedo()
        {
            var registry = ExistingRegistry;
            registry?.Changed();
            SpriteSpecs.ClearCache();
            if (ChangedSinceSave(registry)) _undone = true;
            Schedule();
        }

        // 되돌리기로 기록이 돌아갔는가(RebakeStale의 checkFiles). 읽으면 지운다.
        public static bool TakeUndoCheck()
        {
            var undone = _undone || ChangedSinceSave(ExistingRegistry);
            _undone = false;
            return undone;
        }

        static void Schedule()
        {
            if (_scheduled) return;
            _scheduled = true;
            EditorApplication.delayCall += () =>
            {
                _scheduled = false;
                // 처음 상태로 되돌리는 중이면(파일을 옮긴 뒤 Unity를 다시 열기 전) 옮긴 자리에 다시 굽지 않는다.
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || ProjectReset.Resetting) return;
                // DB에 적용 대기 중인 변경은 적용된 뒤에 굽는다.
                if (EditorDatabase.Staging.HasChanges) return;
                var (rebaked, review, moved) = RebakeStale(checkFiles: TakeUndoCheck());
                if (rebaked > 0) Debug.Log($"[MomoiProject] 바뀐 스프라이트 틀로 {rebaked}개를 다시 구웠습니다.");
                if (moved > 0) Debug.Log($"[MomoiProject] 틀에서 빠진 크기로 구운 이미지 {moved}개를 {BackupFolder()}에 옮겨 두었습니다.");
                if (review.Count > 0)
                    Debug.LogWarning("[MomoiProject] 틀의 크기가 바뀌어 예외 촬영을 확인해야 합니다(틀 인스펙터의 '이 틀로 자른 원본'에서 맞추기):\n  " + string.Join("\n  ", review));
            };
        }


        // 틀의 크기 이름을 바꿀 때: 구운 기록·예외 촬영·필드 규칙의 미리보기 크기를 함께 바꾼다(구운 파일은 새 이름으로 다시 굽고 예전 파일은 백업으로).
        // undo: Ctrl+Z로 되돌릴 수 있게 남긴다(틀 인스펙터). 예전 형식 옮기기는 남기지 않는다(옮긴 파일은 되돌아오지 않는다).
        public static void RenameSize(SpriteFrameData frame, string from, string to, bool undo = true)
        {
            if (frame == null || string.IsNullOrEmpty(from) || from == to) return;
            var registry = ExistingRegistry;
            if (registry != null)
            {
                if (undo) Undo.RecordObject(registry, "크기 이름 바꾸기");
                // 새 이름이 아직 표에 없으면 그 이름으로 남은 기록은 예전 것(뺀 뒤 아직 정리되지 않은 크기)이다 — 이름을 바꾼 크기에 섞이지 않게 치운다(같은 자리 파일은 새로 구울 때 덮인다).
                var leftover = frame.Find(to) == null;
                foreach (var framing in registry.Framings.Where(f => f != null && f.frame == frame.Id))
                {
                    if (leftover)
                    {
                        framing.overrides.RemoveAll(o => o.size == to);
                        framing.baked.RemoveAll(b => b.size == to);
                    }
                    foreach (var custom in framing.overrides.Where(o => o.size == from)) custom.size = to;
                    // 구운 파일 이름에 크기 이름이 들어가므로 새로 굽는다(예전 기록은 다음 정리에서 백업으로).
                    foreach (var bake in framing.baked.Where(b => b.size == from)) bake.revision = 0;
                }
                registry.Changed();
                EditorUtility.SetDirty(registry);
            }
            foreach (var rule in EditorDatabase.Instance.OfType<FieldRuleData>().Where(r => r.spriteFrame == frame.Id && r.previewSize == from).ToList())
            {
                // 적용 대기 중인 작업본이면 그 작업본을(적용할 때 함께 들어간다), 아니면 에셋을 바로 고친다.
                if (EditorDatabase.Staging.IsWorking(rule))
                {
                    rule.previewSize = to;
                    continue;
                }
                if (undo) Undo.RecordObject(rule, "크기 이름 바꾸기");
                rule.previewSize = to;
                EditorUtility.SetDirty(rule);
                AssetDatabase.SaveAssetIfDirty(rule);
            }
            EditorDatabase.MarkDirty();
        }

        // 틀의 ID를 바꿀 때(EditorDatabase.Rename 뒤): 맞춤 기록이 가리키는 틀도 바꾼다(필드 규칙은 Rename이 [DataRef]로 바꾼다).
        // ID 바꾸기(EditorDatabase.Rename)처럼 Undo로 남기지 않고 기록의 예전 Undo도 지운다 — Ctrl+Z가 기록만 예전 ID로 돌리면
        // 틀과 끊기고, 다시 맞출 때 다른 틀로 옮긴 것으로 보아 예외 촬영을 지운다.
        public static void RenameFrame(string from, string to)
        {
            var registry = ExistingRegistry;
            if (registry == null || string.IsNullOrEmpty(from) || from == to) return;
            foreach (var framing in registry.Framings.Where(f => f != null && f.frame == from)) framing.frame = to;
            Save(registry, refresh: false);
            Undo.ClearUndo(registry);
        }

        // ── 예전 형식 옮기기 ───────────────────────────────────
        // 2026-10-07 전에는 크기마다 규격 파일(SpritePresetData)이 하나씩 있었다. 그 그룹(지금의 틀)의 크기 줄로 옮기고 파일은 백업으로.
        // 구운 기록·예외 촬영·필드 규칙은 규격 ID를 크기 이름으로 쓰므로 이어진다(크기 이름으로 맞지 않는 ID — 점이 든 것 등 — 는 바꾼 이름으로 함께 옮긴다).
        // Undo로 남기지 않는다(옮긴 파일은 Ctrl+Z로 돌아오지 않는다).
        static void MigrateLegacy()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var guids = AssetDatabase.FindAssets("t:" + nameof(SpritePresetData));
            if (guids.Length == 0) return;
            var moved = new List<string>();
            var skipped = new List<string>();
            var renamed = false;
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var legacy = AssetDatabase.LoadAssetAtPath<SpritePresetData>(path);
                if (legacy == null) continue;
                var frame = FrameOf(legacy.group);
                var name = SpriteFrameData.IsValidName(legacy.Id, out _) ? legacy.Id : IdRules.Normalize(legacy.Id?.Replace(".", "_"));
                if (frame == null)
                {
                    skipped.Add($"{path} (그룹 '{legacy.group}'을(를) 찾지 못함)");
                    continue;
                }
                if (frame.Find(name) == null)
                {
                    frame.sizes.Add(new SpriteFrameSize { name = name, width = legacy.width, height = legacy.height });
                    EditorUtility.SetDirty(frame);
                    AssetDatabase.SaveAssetIfDirty(frame);
                }
                if (name != legacy.Id)
                {
                    RenameSize(frame, legacy.Id, name, undo: false);
                    renamed = true;
                }
                var target = Path.Combine(BackupFolder(), path).Replace('\\', '/');
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Move(path, target);
                    if (File.Exists(path + ".meta")) File.Move(path + ".meta", target + ".meta");
                    moved.Add(path);
                }
                catch (Exception e)
                {
                    skipped.Add($"{path} ({e.Message})");
                }
            }
            if (renamed && ExistingRegistry is SpriteSpecDatabase registry) Save(registry, refresh: false);
            if (moved.Count > 0)
            {
                AssetDatabase.Refresh();
                EditorDatabase.Instance.Sync();
                Debug.Log($"[MomoiProject] 예전 스프라이트 규격 파일 {moved.Count}개를 틀의 크기 줄로 옮겼습니다(원래 파일은 {BackupFolder()}).");
            }
            if (skipped.Count > 0)
                Debug.LogWarning("[MomoiProject] 예전 스프라이트 규격 파일을 틀로 옮기지 못했습니다 — 틀을 만들고 그 표에 같은 크기를 적은 뒤 이 파일을 지우세요:\n  " + string.Join("\n  ", skipped));
        }

        static string Safe(string name) => string.Concat((name ?? "").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    }
}
