using System;
using System.IO;
using System.Linq;
using GBible.Data;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace GBible.EditorTools.UI
{
    // 스프라이트 규격 파일의 인스펙터: Project 창(보통 Assets/Dev 아래 아무 폴더)에서 우클릭 › Create › MomoiProject › 스프라이트 규격으로 만들고 여기서 채운다.
    //   ID(이름) · 그룹(고르기 · 새 그룹 만들기) · 크기(빠른 크기 · 가로세로 바꾸기) · 비율 미리보기 · 쓰는 필드 · 구운 이미지.
    // 저장(Ctrl+S)하면 이 규격으로 구운 이미지가 새 크기로 다시 구워진다(SpriteSpecEditor.RebakeStale).
    [CustomEditor(typeof(SpritePresetData))]
    public sealed class SpritePresetInspector : Editor
    {
        static readonly int[] QuickSizes = { 64, 128, 256, 512, 1024, 2048 };

        VisualElement _root;

        public override VisualElement CreateInspectorGUI()
        {
            _root = new VisualElement();
            _root.style.paddingTop = 4;
            Rebuild();
            return _root;
        }

        SpritePresetData Preset => (SpritePresetData)target;

        void Rebuild()
        {
            _root.Clear();
            serializedObject.Update();
            var preset = Preset;

            _root.Add(Heading("스프라이트 규격"));
            _root.Add(Caption("원본 일러를 이 크기로 잘라 구운 이미지를 만든다. 같은 그룹의 규격은 한 원본에서 함께 구워진다(필드 규칙으로 그룹을 쓰는 스프라이트 필드에 원본을 넣을 때)."));

            // ID
            var idRow = Row();
            var idField = new TextField("ID") { value = preset.Id };
            idField.style.flexGrow = 1;
            idRow.Add(idField);
            var rename = new Button(() => Rename(idField.value)) { text = "바꾸기" };
            rename.tooltip = "ID를 바꾸고 이 ID를 쓰는 곳도 함께 고친다";
            idRow.Add(rename);
            _root.Add(idRow);

            // 그룹
            var groups = EditorDatabase.Instance.OfType(typeof(SpritePresetGroupData)).Select(g => g.Id).OrderBy(g => g, StringComparer.Ordinal).ToList();
            var groupProperty = serializedObject.FindProperty("group");
            var choices = groups.ToList();
            if (!string.IsNullOrEmpty(preset.group) && !choices.Contains(preset.group)) choices.Insert(0, preset.group);
            choices.Insert(0, "(없음)");
            var groupField = new DropdownField("그룹", choices, string.IsNullOrEmpty(preset.group) ? "(없음)" : preset.group);
            groupField.RegisterValueChangedCallback(e =>
            {
                groupProperty.stringValue = e.newValue == "(없음)" ? "" : e.newValue;
                serializedObject.ApplyModifiedProperties();
                Rebuild();
            });
            _root.Add(groupField);
            var newGroupRow = Row();
            var newGroup = new TextField("새 그룹") { value = "" };
            newGroup.style.flexGrow = 1;
            newGroup.tooltip = "새 그룹 ID(영어 lowerCamelCase). 이 파일과 같은 폴더에 그룹 파일을 만든다";
            newGroupRow.Add(newGroup);
            newGroupRow.Add(new Button(() => CreateGroup(newGroup.value)) { text = "만들기" });
            _root.Add(newGroupRow);

            // 크기
            var width = serializedObject.FindProperty("width");
            var height = serializedObject.FindProperty("height");
            var sizeRow = Row();
            var widthField = new IntegerField("가로(px)");
            widthField.BindProperty(width);
            widthField.style.flexGrow = 1;
            var heightField = new IntegerField("세로(px)");
            heightField.BindProperty(height);
            heightField.style.flexGrow = 1;
            sizeRow.Add(widthField);
            var swap = new Button(() =>
            {
                (width.intValue, height.intValue) = (height.intValue, width.intValue);
                serializedObject.ApplyModifiedProperties();
            }) { text = "↔" };
            swap.tooltip = "가로·세로 바꾸기";
            sizeRow.Add(swap);
            _root.Add(sizeRow);
            _root.Add(heightField);

            var quick = Row();
            quick.style.flexWrap = Wrap.Wrap;
            quick.style.marginLeft = 3;
            quick.Add(Caption("빠른 크기"));
            foreach (var size in QuickSizes)
            {
                var s = size;
                quick.Add(new Button(() =>
                {
                    width.intValue = s;
                    height.intValue = s;
                    serializedObject.ApplyModifiedProperties();
                }) { text = $"{s}" });
            }
            _root.Add(quick);

            // 비율 미리보기(긴 쪽 160px)
            var preview = new VisualElement();
            preview.style.marginTop = 8;
            preview.style.marginLeft = 3;
            preview.style.alignItems = Align.FlexStart;
            var box = new VisualElement();
            box.style.backgroundColor = new Color(0.35f, 0.55f, 0.9f, 0.25f);
            SetBorder(box, new Color(0.35f, 0.55f, 0.9f, 0.9f));
            box.style.justifyContent = Justify.Center;
            box.style.alignItems = Align.Center;
            var sizeLabel = new Label();
            box.Add(sizeLabel);
            preview.Add(box);
            _root.Add(preview);
            void UpdatePreview()
            {
                var w = Mathf.Max(1, width.intValue);
                var h = Mathf.Max(1, height.intValue);
                var scale = 160f / Mathf.Max(w, h);
                box.style.width = Mathf.Max(8, w * scale);
                box.style.height = Mathf.Max(8, h * scale);
                sizeLabel.text = $"{w}×{h}";
            }
            // 검사: 크기를 바꿀 때마다 다시 본다(입력·빠른 크기·가로세로 바꾸기).
            var checks = new VisualElement();
            _root.Add(checks);
            void UpdateChecks()
            {
                checks.Clear();
                // 칸의 지금 값(바인딩이 속성에 쓰기 전에 불릴 수 있다).
                var w = widthField.value;
                var h = heightField.value;
                if (string.IsNullOrEmpty(groupProperty.stringValue)) checks.Add(new HelpBox("그룹을 정하세요.", HelpBoxMessageType.Error));
                if (w < SpritePresetsPage.MinSize || h < SpritePresetsPage.MinSize || w > SpritePresetsPage.MaxSize || h > SpritePresetsPage.MaxSize)
                    checks.Add(new HelpBox($"크기는 {SpritePresetsPage.MinSize}~{SpritePresetsPage.MaxSize}px 사이여야 합니다.", HelpBoxMessageType.Error));
            }

            UpdatePreview();
            UpdateChecks();
            widthField.RegisterValueChangedCallback(_ =>
            {
                UpdatePreview();
                UpdateChecks();
            });
            heightField.RegisterValueChangedCallback(_ =>
            {
                UpdatePreview();
                UpdateChecks();
            });

            // 쓰는 필드 · 구운 이미지
            _root.Add(Heading("쓰임"));
            var fields = string.IsNullOrEmpty(preset.group) ? new System.Collections.Generic.List<string>()
                : KeywordTools.SpriteFieldsLinkedTo(preset.group).Select(f => $"{f.type.Name}.{f.field.Name}").ToList();
            _root.Add(Caption(fields.Count == 0 ? "이 그룹을 쓰는 스프라이트 필드가 아직 없습니다(AI가 필드 규칙으로 연결한다)." : "쓰는 필드: " + string.Join(", ", fields)));
            var registry = AssetDatabase.LoadAssetAtPath<SpriteSpecDatabase>(SpriteSpecEditor.RegistryPath);
            var baked = registry == null ? 0 : registry.Framings.Count(f => f.BakeFor(preset.Id)?.sprite != null);
            var stale = registry == null ? 0 : registry.Framings.Count(f => f.BakeFor(preset.Id) is SpriteBake b && b.sprite != null && b.revision != preset.Revision);
            _root.Add(Caption($"이 규격으로 구운 이미지 {baked}개" + (stale > 0 ? $" · 크기가 바뀌어 다시 구울 것 {stale}개(저장하면 다시 굽는다)" : "")));
        }

        void Rename(string newId)
        {
            newId = (newId ?? "").Trim();
            if (newId == Preset.Id) return;
            try
            {
                EditorDatabase.Instance.Rename(Preset, newId);
                AssetDatabase.SaveAssets();
            }
            catch (ArgumentException e)
            {
                EditorUtility.DisplayDialog("ID 바꾸기", e.Message.Replace("[MomoiProject] ", ""), "확인");
            }
            Rebuild();
        }

        void CreateGroup(string id)
        {
            id = (id ?? "").Trim();
            if (!EditorDatabase.Instance.CanUseId(id, null, out var error))
            {
                EditorUtility.DisplayDialog("새 그룹", error, "확인");
                return;
            }
            var folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(Preset))?.Replace('\\', '/') ?? ProjectFolders.Dev;
            var group = CreateInstance<SpritePresetGroupData>();
            group.EditorAssignId(id);
            AssetDatabase.CreateAsset(group, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{id}.asset"));
            serializedObject.FindProperty("group").stringValue = id;
            serializedObject.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            EditorDatabase.Instance.Sync();
            Rebuild();
        }

        static VisualElement Row()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            return row;
        }

        static Label Heading(string text)
        {
            var label = new Label(text);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginTop = 8;
            label.style.marginBottom = 2;
            label.style.marginLeft = 3;
            return label;
        }

        static Label Caption(string text)
        {
            var label = new Label(text);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.opacity = 0.7f;
            label.style.marginLeft = 3;
            label.style.marginBottom = 4;
            return label;
        }

        static void SetBorder(VisualElement element, Color color)
        {
            element.style.borderTopWidth = element.style.borderBottomWidth = element.style.borderLeftWidth = element.style.borderRightWidth = 1;
            element.style.borderTopColor = element.style.borderBottomColor = element.style.borderLeftColor = element.style.borderRightColor = color;
        }
    }
}
