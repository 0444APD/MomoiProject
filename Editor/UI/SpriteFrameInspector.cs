using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using GBible.Data;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace GBible.EditorTools.UI
{
    // 스프라이트 틀 파일의 인스펙터: Project 창(보통 Assets/Dev 아래 아무 폴더)에서 우클릭 › Create › MomoiProject › 스프라이트 틀로 만들고 여기서 채운다.
    //   ID · 크기 표(이름·가로·세로, 줄 더하기·빼기·빠른 크기·가로세로 바꾸기, 비율 미리보기)
    //   원본 넣기(끌어 놓기 — 하나면 맞춤 창, 여럿이면 지금 맞춤으로 모두 굽기) · 이 틀로 자른 원본(맞추기·예외 촬영 확인) · 쓰는 필드.
    // 크기를 고치면(칸을 벗어나거나 Enter) 바로 저장하고, 바뀐 크기로 구운 이미지를 다시 굽는다.
    [CustomEditor(typeof(SpriteFrameData))]
    public sealed class SpriteFrameInspector : Editor
    {
        static readonly int[] QuickSizes = { 64, 128, 256, 512, 1024, 2048 };
        const float PreviewSide = 96f;

        VisualElement _root;

        SpriteFrameData Frame => (SpriteFrameData)target;

        void OnEnable() => Undo.undoRedoPerformed += OnUndoRedo;

        void OnDisable() => Undo.undoRedoPerformed -= OnUndoRedo;

        // 되돌리기(Ctrl+Z)로 표가 바뀌었으면 다시 그리고 그 크기로 다시 굽는다.
        void OnUndoRedo()
        {
            if (target == null) return;
            EditorDatabase.MarkDirty();
            if (!Problems(Frame).Any()) SpriteSpecEditor.RebakeStale();
            Rebuild();
        }

        public override VisualElement CreateInspectorGUI()
        {
            _root = new VisualElement();
            _root.style.paddingTop = 4;
            Rebuild();
            return _root;
        }

        void Rebuild()
        {
            if (_root == null || target == null) return;
            _root.Clear();
            var frame = Frame;

            _root.Add(Heading("스프라이트 틀"));
            _root.Add(Caption("원본 일러 하나를 아래 표의 크기마다 한꺼번에 잘라 둔다. 원본을 '원본 넣기'에 끌어 놓거나 이 틀을 쓰는 스프라이트 필드에 넣으면 맞춤 창이 열린다 " +
                              "(공통 맞춤 한 번 + 필요하면 크기마다 따로 잡기 = 예외 촬영)."));

            // ID
            var idRow = Row();
            var idField = new TextField("ID") { value = frame.Id };
            idField.style.flexGrow = 1;
            idRow.Add(idField);
            var rename = new Button(() => Rename(idField.value)) { text = "바꾸기" };
            rename.tooltip = "ID를 바꾸고 이 ID를 쓰는 곳(필드 규칙·자른 기록)도 함께 고친다. 게임 코드의 GameIds 이름도 바뀐다";
            idRow.Add(rename);
            _root.Add(idRow);
            var fileName = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(frame));
            var fromFile = IdRules.Normalize(fileName ?? "");
            if (!string.IsNullOrEmpty(frame.Id) && fromFile.Length > 1 && fromFile != frame.Id)
            {
                var hint = Row();
                hint.Add(Caption($"파일 이름({fileName})과 ID({frame.Id})가 다릅니다."));
                hint.Add(new Button(() => Rename(fromFile)) { text = $"ID를 '{fromFile}'로" });
                _root.Add(hint);
            }

            BuildSizes(frame);
            BuildSources(frame);

            // 쓰는 필드
            _root.Add(Heading("쓰는 필드"));
            var fields = string.IsNullOrEmpty(frame.Id) ? new List<string>()
                : KeywordTools.SpriteFieldsLinkedTo(frame.Id).Select(f => $"{f.type.Name}.{f.field.Name}").ToList();
            _root.Add(Caption(fields.Count == 0
                ? "이 틀을 쓰는 스프라이트 필드가 아직 없습니다(AI가 필드 규칙으로 연결한다 — DB › 클래스 › 클래스 상세의 '스프라이트 틀'). 연결 전에도 위 '원본 넣기'로 미리 잘라 둘 수 있다."
                : "쓰는 필드: " + string.Join(", ", fields) + ". 게임 코드: SpriteSpecs.Get(원본, GameIds.SpriteSize." + IdRules.ToIdentifier(frame.Id) + ".크기)"));
        }

        // ── 크기 표 ────────────────────────────────────────────

        void BuildSizes(SpriteFrameData frame)
        {
            _root.Add(Heading("크기"));
            var header = Row();
            header.style.marginLeft = 3;
            header.Add(HeaderCell("이름", 140));
            header.Add(HeaderCell("가로(px)", 72));
            header.Add(HeaderCell("", 24));
            header.Add(HeaderCell("세로(px)", 72));
            _root.Add(header);

            for (var i = 0; i < frame.sizes.Count; i++)
            {
                var index = i;
                var size = frame.sizes[i] ?? (frame.sizes[i] = new SpriteFrameSize());
                var row = Row();
                row.style.marginLeft = 3;
                row.style.marginBottom = 2;

                var name = new TextField { value = size.name, isDelayed = true };
                name.style.width = 140;
                name.tooltip = "크기 이름(영문 소문자로 시작, 영문·숫자). 예: standing, icon, cardFront";
                name.RegisterValueChangedCallback(e => RenameSize(index, e.previousValue, e.newValue));
                row.Add(name);

                var width = new IntegerField { value = size.width, isDelayed = true };
                width.style.width = 72;
                width.RegisterValueChangedCallback(e => Resize(index, e.newValue, null));
                row.Add(width);

                var swap = new Button(() => Resize(index, frame.sizes[index].height, frame.sizes[index].width)) { text = "↔" };
                swap.tooltip = "가로·세로 바꾸기";
                swap.style.width = 24;
                row.Add(swap);

                var height = new IntegerField { value = size.height, isDelayed = true };
                height.style.width = 72;
                height.RegisterValueChangedCallback(e => Resize(index, null, e.newValue));
                row.Add(height);

                var quick = new Button(() => QuickMenu(index)) { text = "▾" };
                quick.tooltip = "빠른 크기";
                row.Add(quick);

                var remove = new Button(() => RemoveSize(index)) { text = "✕" };
                remove.tooltip = "이 크기 빼기(이 크기로 구운 이미지는 백업 폴더로 옮긴다)";
                row.Add(remove);
                _root.Add(row);
            }

            var add = new Button(AddSize) { text = "+ 크기 더하기" };
            add.style.alignSelf = Align.FlexStart;
            add.style.marginLeft = 3;
            add.style.marginTop = 2;
            _root.Add(add);

            foreach (var problem in Problems(frame)) _root.Add(new HelpBox(problem, HelpBoxMessageType.Error));

            // 비율 미리보기(긴 쪽 96px)
            var previews = Row();
            previews.style.flexWrap = Wrap.Wrap;
            previews.style.alignItems = Align.FlexEnd;
            previews.style.marginTop = 8;
            previews.style.marginLeft = 3;
            foreach (var size in frame.sizes.Where(s => s != null && s.width > 0 && s.height > 0))
            {
                var card = new VisualElement();
                card.style.marginRight = 10;
                card.style.marginBottom = 6;
                card.style.alignItems = Align.Center;
                var box = new VisualElement();
                var scale = PreviewSide / Mathf.Max(size.width, size.height);
                box.style.width = Mathf.Max(6, size.width * scale);
                box.style.height = Mathf.Max(6, size.height * scale);
                box.style.backgroundColor = new Color(0.35f, 0.55f, 0.9f, 0.25f);
                SetBorder(box, new Color(0.35f, 0.55f, 0.9f, 0.9f));
                card.Add(box);
                var label = new Label($"{size.name}\n{size.width}×{size.height}");
                label.style.unityTextAlign = TextAnchor.MiddleCenter;
                label.style.fontSize = 10;
                label.style.opacity = 0.8f;
                card.Add(label);
                previews.Add(card);
            }
            _root.Add(previews);
        }

        static IEnumerable<string> Problems(SpriteFrameData frame)
        {
            if (frame.sizes.Count == 0) yield return "크기가 없습니다. '+ 크기 더하기'로 줄을 더하세요.";
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var size in frame.sizes.Where(s => s != null))
            {
                if (!SpriteFrameData.IsValidName(size.name, out var error)) yield return $"'{size.name}': {error}";
                else if (!seen.Add(size.name)) yield return $"크기 이름 '{size.name}'이(가) 겹칩니다.";
                if (!SpriteFrameData.IsValidSize(size.width) || !SpriteFrameData.IsValidSize(size.height))
                    yield return $"'{size.name}': 크기는 {SpriteFrameData.MinSize}~{SpriteFrameData.MaxSize}px 사이여야 합니다.";
            }
        }

        void AddSize()
        {
            var frame = Frame;
            var name = "size1";
            for (var n = 1; frame.Find(name) != null || frame.sizes.Any(s => s != null && string.Equals(s.name, name, StringComparison.OrdinalIgnoreCase)); n++)
                name = "size" + n.ToString(CultureInfo.InvariantCulture);
            Edit("크기 더하기", f => f.sizes.Add(new SpriteFrameSize { name = name, width = 256, height = 256 }));
        }

        void RemoveSize(int index)
        {
            var frame = Frame;
            if (index < 0 || index >= frame.sizes.Count) return;
            var size = frame.sizes[index];
            var baked = SpriteSpecEditor.FramingsOf(frame.Id).Count(f => size != null && f.BakeFor(size.name)?.sprite != null);
            if (baked > 0 && !EditorUtility.DisplayDialog("크기 빼기",
                    $"'{size.name}'으로 구운 이미지 {baked}개는 MomoiProjectBackups 폴더로 옮겨집니다(게임 코드가 이 크기를 쓰면 원본이 대신 보인다). 뺄까요?", "빼기", "취소"))
                return;
            Edit("크기 빼기", f => f.sizes.RemoveAt(index));
        }

        void RenameSize(int index, string from, string to)
        {
            var frame = Frame;
            to = (to ?? "").Trim();
            if (index < 0 || index >= frame.sizes.Count || to == from) return;
            if (!SpriteFrameData.IsValidName(to, out var error) ||
                frame.sizes.Where((s, i) => i != index && s != null).Any(s => string.Equals(s.name, to, StringComparison.OrdinalIgnoreCase)))
            {
                EditorUtility.DisplayDialog("크기 이름", error ?? $"'{to}'은(는) 이 틀에 이미 있습니다.", "확인");
                Rebuild();
                return;
            }
            // 예전 이름이 올바른 이름이었으면 자른 기록·예외 촬영·미리보기 크기를 따라 바꾼다.
            if (SpriteFrameData.IsValidName(from, out _)) SpriteSpecEditor.RenameSize(frame, from, to);
            Edit("크기 이름 바꾸기", f => f.sizes[index].name = to);
        }

        void Resize(int index, int? width, int? height)
        {
            var frame = Frame;
            if (index < 0 || index >= frame.sizes.Count) return;
            Edit("크기 바꾸기", f =>
            {
                if (width.HasValue) f.sizes[index].width = width.Value;
                if (height.HasValue) f.sizes[index].height = height.Value;
            });
        }

        void QuickMenu(int index)
        {
            var menu = new GenericMenu();
            foreach (var size in QuickSizes)
            {
                var s = size;
                menu.AddItem(new GUIContent($"{s}×{s}"), false, () => Resize(index, s, s));
            }
            menu.AddSeparator("");
            foreach (var (w, h) in new[] { (1920, 1080), (1080, 1920), (600, 900), (512, 768), (300, 400) })
            {
                var (cw, ch) = (w, h);
                menu.AddItem(new GUIContent($"{cw}×{ch}"), false, () => Resize(index, cw, ch));
            }
            menu.ShowAsContext();
        }

        // 틀을 고치고 저장한 뒤 바뀐 크기로 다시 굽는다(틀에서 빠진 크기의 구운 이미지는 백업으로).
        void Edit(string undoName, Action<SpriteFrameData> edit)
        {
            var frame = Frame;
            Undo.RecordObject(frame, undoName);
            edit(frame);
            EditorUtility.SetDirty(frame);
            AssetDatabase.SaveAssetIfDirty(frame);
            EditorDatabase.MarkDirty();
            if (!Problems(frame).Any())
            {
                var (rebaked, review, moved) = SpriteSpecEditor.RebakeStale();
                if (rebaked > 0) Debug.Log($"[MomoiProject] 틀 '{frame.Id}'의 바뀐 크기로 {rebaked}개를 다시 구웠습니다.");
                if (moved > 0) Debug.Log($"[MomoiProject] 틀에서 빠진 크기로 구운 이미지 {moved}개를 백업 폴더로 옮겼습니다.");
                if (review.Count > 0) Debug.LogWarning("[MomoiProject] 크기가 바뀌어 예외 촬영을 확인해야 합니다(아래 '이 틀로 자른 원본'의 맞추기):\n  " + string.Join("\n  ", review));
            }
            Rebuild();
        }

        // ── 원본 넣기 · 자른 원본 ──────────────────────────────

        void BuildSources(SpriteFrameData frame)
        {
            _root.Add(Heading("원본 넣기"));
            var ready = SpriteSpecEditor.SizesOf(frame.Id).Count > 0 && !Problems(frame).Any();
            var drop = new VisualElement();
            drop.style.marginLeft = 3;
            drop.style.marginRight = 3;
            drop.style.paddingTop = drop.style.paddingBottom = 14;
            drop.style.paddingLeft = drop.style.paddingRight = 8;
            drop.style.borderTopLeftRadius = drop.style.borderTopRightRadius = drop.style.borderBottomLeftRadius = drop.style.borderBottomRightRadius = 4;
            SetBorder(drop, new Color(0.5f, 0.5f, 0.5f, 0.8f));
            var dropLabel = new Label(ready
                ? "여기에 원본 스프라이트를 끌어 놓으세요.\n하나면 맞춤 창이 열리고, 여럿이면 지금 맞춤(처음이면 가운데·배율 1)으로 모두 한꺼번에 굽습니다."
                : "먼저 위 표에 올바른 크기를 하나 이상 적으세요.");
            dropLabel.style.whiteSpace = WhiteSpace.Normal;
            dropLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            dropLabel.style.opacity = 0.8f;
            drop.Add(dropLabel);
            if (ready)
            {
                drop.RegisterCallback<DragUpdatedEvent>(_ =>
                {
                    DragAndDrop.visualMode = DraggedSprites().Count > 0 ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                });
                drop.RegisterCallback<DragPerformEvent>(_ =>
                {
                    var sprites = DraggedSprites();
                    if (sprites.Count == 0) return;
                    DragAndDrop.AcceptDrag();
                    Place(sprites);
                });
            }
            _root.Add(drop);

            if (ready)
            {
                var pick = new ObjectField("원본 고르기") { objectType = typeof(Sprite), allowSceneObjects = false };
                pick.tooltip = "고르면 맞춤 창이 열린다";
                pick.RegisterValueChangedCallback(e =>
                {
                    if (e.newValue is Sprite sprite) Place(new List<Sprite> { sprite });
                    pick.SetValueWithoutNotify(null);
                });
                _root.Add(pick);
            }

            var framings = SpriteSpecEditor.FramingsOf(frame.Id);
            var sizes = SpriteSpecEditor.SizesOf(frame.Id);
            var titleRow = Row();
            titleRow.Add(Heading($"이 틀로 자른 원본 {framings.Count}개"));
            if (framings.Count > 0 && ready)
            {
                var all = new Button(() => RebakeFrame(frame)) { text = "모두 다시 굽기" };
                all.tooltip = "이 틀로 자른 원본을 지금 맞춤(예외 촬영 포함) 그대로 모든 크기로 다시 굽는다";
                titleRow.Add(all);
            }
            _root.Add(titleRow);
            if (framings.Count == 0)
            {
                _root.Add(Caption("아직 없습니다."));
                return;
            }
            foreach (var framing in framings)
            {
                var row = Row();
                row.style.marginLeft = 3;
                row.style.marginBottom = 3;
                var thumb = new Image { sprite = framing.source, scaleMode = ScaleMode.ScaleToFit };
                thumb.style.width = 32;
                thumb.style.height = 32;
                thumb.style.marginRight = 6;
                row.Add(thumb);
                var name = new Label(framing.source.name);
                name.style.flexGrow = 1;
                row.Add(name);
                var exceptions = framing.overrides.Count(o => sizes.Any(s => s.name == o.size));
                var review = framing.overrides.Count(o => o.needsReview);
                var missing = sizes.Count(s => !(framing.BakeFor(s.name) is SpriteBake b && b.sprite != null && b.revision == s.Revision));
                if (exceptions > 0) row.Add(Badge($"예외 {exceptions}", new Color(0.35f, 0.55f, 0.9f), string.Join(", ", framing.overrides.Select(o => o.size))));
                if (review > 0) row.Add(Badge($"확인 필요 {review}", new Color(0.85f, 0.65f, 0.2f), string.Join("\n", framing.overrides.Where(o => o.needsReview).Select(o => o.note))));
                if (missing > 0) row.Add(Badge($"구울 것 {missing}", new Color(0.8f, 0.35f, 0.35f), "아직 굽지 않았거나 크기가 바뀐 크기 — '모두 다시 굽기'나 '맞추기'로 굽는다"));
                var source = framing.source;
                var adjust = new Button(() => SpriteFramingWindow.Open(source, frame.Id, Rebuild)) { text = "맞추기" };
                adjust.tooltip = "맞춤 창: 공통 중심·배율을 다시 잡거나 크기마다 따로 잡는다(예외 촬영)";
                adjust.SetEnabled(ready);
                row.Add(adjust);
                var ping = new Button(() => EditorGUIUtility.PingObject(source)) { text = "원본" };
                ping.tooltip = "Project 창에서 원본 보기";
                row.Add(ping);
                _root.Add(row);
            }
        }

        static List<Sprite> DraggedSprites()
        {
            var sprites = new List<Sprite>();
            foreach (var item in DragAndDrop.objectReferences)
            {
                if (item is Sprite sprite) sprites.Add(sprite);
                else if (item is Texture2D texture)
                {
                    // 텍스처를 끌어 놓으면 그 안의 스프라이트(Single이면 하나)를 쓴다.
                    var path = AssetDatabase.GetAssetPath(texture);
                    sprites.AddRange(AssetDatabase.LoadAllAssetRepresentationsAtPath(path).OfType<Sprite>());
                    var main = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                    if (main != null && !sprites.Contains(main)) sprites.Add(main);
                }
            }
            return sprites.Distinct().ToList();
        }

        void Place(List<Sprite> sprites)
        {
            var frame = Frame;
            if (sprites.Count == 1)
            {
                SpriteFramingWindow.Open(sprites[0], frame.Id, Rebuild);
                return;
            }
            var count = 0;
            foreach (var sprite in sprites) count += SpriteSpecEditor.BakeWithCurrent(sprite, frame.Id);
            Debug.Log($"[MomoiProject] 원본 {sprites.Count}개를 틀 '{frame.Id}'의 모든 크기로 구웠습니다(이미지 {count}개). 위치를 바꿀 것은 '이 틀로 자른 원본'의 맞추기로.");
            Rebuild();
        }

        void RebakeFrame(SpriteFrameData frame)
        {
            var count = 0;
            foreach (var framing in SpriteSpecEditor.FramingsOf(frame.Id)) count += SpriteSpecEditor.BakeWithCurrent(framing.source, frame.Id);
            Debug.Log($"[MomoiProject] 틀 '{frame.Id}'로 자른 이미지 {count}개를 다시 구웠습니다.");
            Rebuild();
        }

        void Rename(string newId)
        {
            var frame = Frame;
            newId = (newId ?? "").Trim();
            var oldId = frame.Id;
            if (newId == oldId) return;
            try
            {
                EditorDatabase.Instance.Rename(frame, newId);
                SpriteSpecEditor.RenameFrame(oldId, newId);
                AssetDatabase.SaveAssets();
            }
            catch (ArgumentException e)
            {
                EditorUtility.DisplayDialog("ID 바꾸기", e.Message.Replace("[MomoiProject] ", ""), "확인");
            }
            Rebuild();
        }

        // ── 모양 ───────────────────────────────────────────────

        static VisualElement Row()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            return row;
        }

        static Label HeaderCell(string text, float width)
        {
            var label = new Label(text);
            label.style.width = width;
            label.style.opacity = 0.7f;
            label.style.fontSize = 10;
            return label;
        }

        static Label Badge(string text, Color color, string tooltip)
        {
            var label = new Label(text) { tooltip = tooltip };
            label.style.backgroundColor = new Color(color.r, color.g, color.b, 0.25f);
            SetBorder(label, color);
            label.style.paddingLeft = label.style.paddingRight = 4;
            label.style.marginRight = 4;
            label.style.fontSize = 10;
            return label;
        }

        static Label Heading(string text)
        {
            var label = new Label(text);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginTop = 10;
            label.style.marginBottom = 2;
            label.style.marginLeft = 3;
            label.style.flexGrow = 1;
            return label;
        }

        static Label Caption(string text)
        {
            var label = new Label(text);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.opacity = 0.7f;
            label.style.marginLeft = 3;
            label.style.marginBottom = 4;
            label.style.flexShrink = 1;
            return label;
        }

        static void SetBorder(VisualElement element, Color color)
        {
            element.style.borderTopWidth = element.style.borderBottomWidth = element.style.borderLeftWidth = element.style.borderRightWidth = 1;
            element.style.borderTopColor = element.style.borderBottomColor = element.style.borderLeftColor = element.style.borderRightColor = color;
        }
    }
}
