using System;
using System.Collections.Generic;
using System.Linq;
using GBible.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace GBible.EditorTools.UI
{
    // 스프라이트 맞춤 창: 원본 위에 그룹의 규격 틀을 겹쳐 보이고, 클릭으로 중심·휠로 배율을 맞춘다.
    //  · 공통: 모든 규격이 같은 중심·배율을 쓴다.
    //  · 규격 하나 선택: 그 규격만 따로 잡는다(예외 촬영, 영구 기록).
    // 적용하면 그룹의 규격이 모두 베이크된다.
    public static class GsSpriteFraming
    {
        const float CanvasSize = 440f;
        const int PreviewMax = 120;

        public static void Open(VisualElement host, Sprite source, string group, Action applied = null)
        {
            if (source == null || string.IsNullOrEmpty(group)) return;
            var presets = SpriteSpecEditor.PresetsOf(group).ToList();
            var groupData = EditorDatabase.Instance.FindById(group);
            var box = Gs.Box("gs-dialog", "gs-framing");
            box.Add(Gs.Label($"스프라이트 맞춤 · {source.name}", "gs-title"));
            if (presets.Count == 0)
            {
                box.Add(Gs.Label($"'{groupData?.DisplayName ?? group}' 그룹에 규격이 없습니다. DB › 스프라이트 규격에서 먼저 만드세요.", "gs-caption"));
                var close = Gs.Box("gs-dialog__buttons");
                box.Add(close);
                var m = GsModal.Show(host, box);
                close.Add(new GsButton("닫기", m.Close, GsButtonKind.Primary));
                return;
            }

            // 기존 맞춤에서 시작한다.
            var existing = SpriteSpecEditor.Registry.Find(source);
            var center = existing?.center ?? new Vector2(0.5f, 0.5f);
            var zoom = existing?.zoom ?? 1f;
            var overrides = new Dictionary<string, (Vector2 center, float zoom)>();
            var reviews = new HashSet<string>();
            if (existing != null)
                foreach (var o in existing.overrides)
                {
                    overrides[o.preset] = (o.center, o.zoom);
                    if (o.needsReview) reviews.Add(o.preset);
                }
            string target = null; // null = 공통

            var caption = Gs.Label($"{groupData?.DisplayName ?? group} 그룹 · 규격 {presets.Count}개. 클릭: 중심 · 휠: 배율", "gs-caption");
            caption.style.marginTop = 4;
            box.Add(caption);

            var modes = Gs.Row("gs-wrap");
            modes.style.marginTop = 10;
            box.Add(modes);

            var split = Gs.Row();
            split.style.alignItems = Align.FlexStart;
            split.style.marginTop = 10;
            box.Add(split);

            var canvas = Gs.Box("gs-framing__canvas");
            canvas.style.width = CanvasSize;
            canvas.style.height = CanvasSize;
            var image = Gs.Box("gs-framing__image");
            image.style.backgroundImage = new StyleBackground(source);
            canvas.Add(image);
            split.Add(canvas);

            var side = Gs.Col("gs-framing__side");
            split.Add(side);
            var status = Gs.Label("", "gs-caption");
            status.style.whiteSpace = WhiteSpace.Normal;
            box.Add(status);

            // 원본을 캔버스에 맞춰 놓은 영역(비율 유지).
            var rect = source.textureRect;
            var fit = Mathf.Min(CanvasSize / rect.width, CanvasSize / rect.height);
            var shown = new Rect((CanvasSize - rect.width * fit) / 2f, (CanvasSize - rect.height * fit) / 2f, rect.width * fit, rect.height * fit);
            image.style.left = shown.x;
            image.style.top = shown.y;
            image.style.width = shown.width;
            image.style.height = shown.height;

            var frames = new Dictionary<string, VisualElement>();
            var previews = new Dictionary<string, (VisualElement image, Label label)>();
            var textures = new List<Texture2D>();

            (Vector2 center, float zoom) Current(string preset) =>
                preset != null && overrides.TryGetValue(preset, out var o) ? o : (center, zoom);

            void Redraw()
            {
                foreach (var preset in presets)
                {
                    var (c, z) = Current(preset.Id);
                    var crop = SpriteSpecEditor.Crop(new Rect(0, 0, rect.width, rect.height), c, z, preset.width, preset.height);
                    var frame = frames[preset.Id];
                    frame.style.left = shown.x + crop.x * fit;
                    // 원본 좌표는 아래에서 위로, 화면은 위에서 아래로.
                    frame.style.top = shown.y + (rect.height - crop.y - crop.height) * fit;
                    frame.style.width = crop.width * fit;
                    frame.style.height = crop.height * fit;
                    frame.EnableInClassList("gs-framing__frame--active", target == null || target == preset.Id);
                    frame.EnableInClassList("gs-framing__frame--override", overrides.ContainsKey(preset.Id));
                }
                foreach (var t in textures) UnityEngine.Object.DestroyImmediate(t);
                textures.Clear();
                foreach (var preset in presets)
                {
                    var (c, z) = Current(preset.Id);
                    var crop = SpriteSpecEditor.Crop(source.textureRect, c, z, preset.width, preset.height);
                    var scale = Mathf.Min(1f, (float)PreviewMax / Mathf.Max(preset.width, preset.height));
                    var tex = SpriteSpecEditor.Render(source, crop, Mathf.Max(8, (int)(preset.width * scale)), Mathf.Max(8, (int)(preset.height * scale)));
                    textures.Add(tex);
                    var (previewImage, label) = previews[preset.Id];
                    previewImage.style.backgroundImage = new StyleBackground(tex);
                    previewImage.style.width = tex.width;
                    previewImage.style.height = tex.height;
                    label.text = $"{preset.DisplayName}  {preset.width}×{preset.height}" +
                                 (overrides.ContainsKey(preset.Id) ? "  · 예외" : "") + (reviews.Contains(preset.Id) ? "  · 확인 필요" : "");
                }
                status.text = target == null
                    ? "공통 맞춤을 조정합니다(예외가 없는 규격 모두)."
                    : $"'{presets.First(p => p.Id == target).DisplayName}'만 따로 잡습니다(예외 촬영으로 영구 기록).";
            }

            // 모드: 공통 / 규격별
            var segments = new List<(string id, GsButton button)>();
            void SelectMode(string preset)
            {
                target = preset;
                foreach (var (id, button) in segments) button.EnableInClassList("gs-framing__mode--active", id == preset);
                Redraw();
            }
            var common = new GsButton("공통", () => SelectMode(null), GsButtonKind.Ghost);
            segments.Add((null, common));
            modes.Add(common);
            foreach (var preset in presets)
            {
                var id = preset.Id;
                var button = new GsButton(preset.DisplayName, () => SelectMode(id), GsButtonKind.Ghost);
                button.tooltip = "이 규격만 따로 잡기(예외 촬영)";
                segments.Add((id, button));
                modes.Add(button);
            }
            var clear = new GsButton("예외 해제", () =>
            {
                if (target == null) return;
                overrides.Remove(target);
                reviews.Remove(target);
                Redraw();
            }, GsButtonKind.Ghost);
            clear.tooltip = "선택한 규격을 공통 맞춤으로 되돌립니다.";
            modes.Add(Gs.Spacer());
            modes.Add(clear);

            var colors = Gs.Palette;
            int index = 0;
            foreach (var preset in presets)
            {
                var color = colors[(index++ * 3) % colors.Length];
                var frame = Gs.Box("gs-framing__frame");
                frame.pickingMode = PickingMode.Ignore;
                frame.style.borderLeftColor = frame.style.borderRightColor = frame.style.borderTopColor = frame.style.borderBottomColor = color;
                var tag = Gs.Label(preset.DisplayName, "gs-framing__tag");
                tag.style.backgroundColor = color;
                tag.pickingMode = PickingMode.Ignore;
                frame.Add(tag);
                canvas.Add(frame);
                frames[preset.Id] = frame;

                var card = Gs.Col("gs-framing__preview");
                var previewImage = Gs.Box("gs-framing__preview-image");
                card.Add(previewImage);
                var label = Gs.Label("", "gs-caption");
                card.Add(label);
                side.Add(card);
                previews[preset.Id] = (previewImage, label);
            }

            // 클릭: 중심, 휠: 배율
            void Set(Vector2 c, float z)
            {
                if (target == null)
                {
                    center = c;
                    zoom = z;
                }
                else
                {
                    overrides[target] = (c, z);
                    reviews.Remove(target);
                }
                Redraw();
            }
            canvas.RegisterCallback<PointerDownEvent>(e =>
            {
                var local = e.localPosition;
                var x = Mathf.Clamp01((local.x - shown.x) / shown.width);
                var y = Mathf.Clamp01(1f - (local.y - shown.y) / shown.height);
                Set(new Vector2(x, y), Current(target).zoom);
            });
            canvas.RegisterCallback<WheelEvent>(e =>
            {
                var (c, z) = Current(target);
                Set(c, Mathf.Clamp(z * (e.delta.y > 0 ? 0.92f : 1.08f), 1f, 12f));
                e.StopPropagation();
            });

            var buttons = Gs.Box("gs-dialog__buttons");
            box.Add(buttons);
            var modal = GsModal.Show(host, box);
            modal.Closed += () =>
            {
                foreach (var t in textures) UnityEngine.Object.DestroyImmediate(t);
            };
            buttons.Add(new GsButton("취소", modal.Close, GsButtonKind.Ghost));
            buttons.Add(new GsButton("적용 (베이크)", () =>
            {
                var count = SpriteSpecEditor.Apply(source, group, center, zoom, overrides);
                modal.Close();
                Debug.Log($"[MomoiProject] '{source.name}'을(를) 규격 {count}개로 베이크했습니다.");
                applied?.Invoke();
            }, GsButtonKind.Primary));
            SelectMode(null);
        }
    }
}
