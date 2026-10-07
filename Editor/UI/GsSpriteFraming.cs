using System;
using System.Collections.Generic;
using System.Linq;
using GBible.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace GBible.EditorTools.UI
{
    // 스프라이트 맞춤 창: 원본 위에 틀의 크기마다 영역을 겹쳐 보이고, 클릭으로 중심·휠로 배율을 맞춘다.
    //  · 공통: 모든 크기가 같은 중심·배율을 쓴다.
    //  · 크기 하나 선택: 그 크기만 따로 잡는다(예외 촬영, 영구 기록).
    // 적용하면 틀의 모든 크기를 한꺼번에 굽는다.
    public static class GsSpriteFraming
    {
        const float CanvasSize = 440f;
        const int PreviewMax = 120;

        // 연 창(GsModal)을 돌려준다(열지 못하면 null). 닫히면 Closed가 온다.
        public static GsModal Open(VisualElement host, Sprite source, string frame, Action applied = null)
        {
            if (source == null || string.IsNullOrEmpty(frame) || host == null) return null;
            var presets = SpriteSpecEditor.SizesOf(frame);
            var frameData = SpriteSpecEditor.FrameOf(frame);
            var box = Gs.Box("gs-dialog", "gs-framing");
            box.Add(Gs.Label($"스프라이트 맞춤 · {source.name}", "gs-title"));
            if (presets.Count == 0)
            {
                box.Add(Gs.Label(frameData == null
                    ? $"틀 '{frame}'을(를) 찾지 못했습니다. Project 창에서 Create › MomoiProject › 스프라이트 틀로 만들거나, 필드 규칙의 틀을 다시 고르세요."
                    : $"틀 '{frame}'에 크기가 없습니다. 틀 파일을 골라 인스펙터의 표에 크기를 더하세요.", "gs-caption"));
                var close = Gs.Box("gs-dialog__buttons");
                box.Add(close);
                var m = GsModal.Show(host, box);
                close.Add(new GsButton("닫기", m.Close, GsButtonKind.Primary));
                return m;
            }

            // 기존 맞춤에서 시작한다(다른 틀로 구웠던 원본이면 공통 맞춤만 이어받고 예외는 새로).
            var existing = SpriteSpecEditor.Registry.Find(source);
            var center = existing?.center ?? new Vector2(0.5f, 0.5f);
            var zoom = existing?.zoom ?? 1f;
            var overrides = new Dictionary<string, (Vector2 center, float zoom)>();
            var reviews = new HashSet<string>();
            if (existing != null && existing.frame == frame)
                foreach (var o in existing.overrides)
                {
                    if (!presets.Any(p => p.name == o.size)) continue;
                    overrides[o.size] = (o.center, o.zoom);
                    if (o.needsReview) reviews.Add(o.size);
                }
            string target = null; // null = 공통

            var caption = Gs.Label($"틀 {frame} · 크기 {presets.Count}개. 클릭: 중심 · 휠: 배율 · 크기 단추: 그 크기만 따로 잡기(예외 촬영)", "gs-caption");
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
                    var (c, z) = Current(preset.name);
                    var crop = SpriteSpecEditor.Crop(new Rect(0, 0, rect.width, rect.height), c, z, preset.width, preset.height);
                    var outline = frames[preset.name];
                    outline.style.left = shown.x + crop.x * fit;
                    // 원본 좌표는 아래에서 위로, 화면은 위에서 아래로.
                    outline.style.top = shown.y + (rect.height - crop.y - crop.height) * fit;
                    outline.style.width = crop.width * fit;
                    outline.style.height = crop.height * fit;
                    outline.EnableInClassList("gs-framing__frame--active", target == null || target == preset.name);
                    outline.EnableInClassList("gs-framing__frame--override", overrides.ContainsKey(preset.name));
                }
                foreach (var t in textures) UnityEngine.Object.DestroyImmediate(t);
                textures.Clear();
                foreach (var preset in presets)
                {
                    var (c, z) = Current(preset.name);
                    var crop = SpriteSpecEditor.Crop(source.textureRect, c, z, preset.width, preset.height);
                    var scale = Mathf.Min(1f, (float)PreviewMax / Mathf.Max(preset.width, preset.height));
                    var tex = SpriteSpecEditor.Render(source, crop, Mathf.Max(8, (int)(preset.width * scale)), Mathf.Max(8, (int)(preset.height * scale)));
                    textures.Add(tex);
                    var (previewImage, label) = previews[preset.name];
                    previewImage.style.backgroundImage = new StyleBackground(tex);
                    previewImage.style.width = tex.width;
                    previewImage.style.height = tex.height;
                    label.text = $"{preset.name}  {preset.width}×{preset.height}" +
                                 (overrides.ContainsKey(preset.name) ? "  · 예외" : "") + (reviews.Contains(preset.name) ? "  · 확인 필요" : "");
                }
                status.text = target == null
                    ? "공통 맞춤을 조정합니다(예외가 없는 크기 모두)."
                    : $"'{target}'만 따로 잡습니다(예외 촬영으로 영구 기록).";
            }

            // 모드: 공통 / 크기별
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
                var id = preset.name;
                var button = new GsButton(preset.name, () => SelectMode(id), GsButtonKind.Ghost);
                button.tooltip = "이 크기만 따로 잡기(예외 촬영)";
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
            clear.tooltip = "선택한 크기를 공통 맞춤으로 되돌립니다.";
            modes.Add(Gs.Spacer());
            modes.Add(clear);

            var colors = Gs.Palette;
            int index = 0;
            foreach (var preset in presets)
            {
                var color = colors[(index++ * 3) % colors.Length];
                var outline = Gs.Box("gs-framing__frame");
                outline.pickingMode = PickingMode.Ignore;
                outline.style.borderLeftColor = outline.style.borderRightColor = outline.style.borderTopColor = outline.style.borderBottomColor = color;
                var tag = Gs.Label(preset.name, "gs-framing__tag");
                tag.style.backgroundColor = color;
                tag.pickingMode = PickingMode.Ignore;
                outline.Add(tag);
                canvas.Add(outline);
                frames[preset.name] = outline;

                var card = Gs.Col("gs-framing__preview");
                var previewImage = Gs.Box("gs-framing__preview-image");
                card.Add(previewImage);
                var label = Gs.Label("", "gs-caption");
                card.Add(label);
                side.Add(card);
                previews[preset.name] = (previewImage, label);
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
            // 미리보기 텍스처는 닫을 때(적용·취소) 지운다. 창째 닫히거나(창의 X) 다른 원본으로 다시 열려 떨어져 나갈 때도.
            void Release()
            {
                foreach (var t in textures) UnityEngine.Object.DestroyImmediate(t);
                textures.Clear();
            }
            modal.Closed += Release;
            modal.RegisterCallback<DetachFromPanelEvent>(_ => Release());
            buttons.Add(new GsButton("취소", modal.Close, GsButtonKind.Ghost));
            buttons.Add(new GsButton("적용 (모든 크기 굽기)", () =>
            {
                var count = SpriteSpecEditor.Apply(source, frame, center, zoom, overrides);
                modal.Close();
                Debug.Log($"[MomoiProject] '{source.name}'을(를) 틀 {frame}의 크기 {count}개로 구웠습니다.");
                applied?.Invoke();
            }, GsButtonKind.Primary));
            SelectMode(null);
            return modal;
        }
    }
}
