using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameSystems.EditorTools.UI
{
    public sealed class SheetColumn
    {
        public string Title;
        public float Width = 150;
        public string Path;
        public bool Bold;
    }

    // 좁은 칸에 정보를 담는 시트. 셀은 읽기 전용이다(한 번 클릭으로 값이 바뀌는 위험을 없앤다).
    // 셀에 마우스를 올리면 오른쪽에 '…'이 나타나고, 셀 끝 글자는 흐려지며 가려진다. '…'은 상세 패널을 연다.
    public sealed class GsSheet : VisualElement
    {
        static Texture2D _fade;

        readonly Color _hoverColor;
        readonly Color _selectedColor;
        readonly Dictionary<UnityEngine.Object, VisualElement> _rows = new Dictionary<UnityEngine.Object, VisualElement>();
        UnityEngine.Object _selected;

        public event Action<UnityEngine.Object> Selected;
        // (항목, 누른 셀의 필드 경로)
        public event Action<UnityEngine.Object, string> OpenRequested;

        public Func<UnityEngine.Object, UnityEngine.Object> Thumbnail;
        public Func<UnityEngine.Object, string> Trailing;
        public Func<UnityEngine.Object, bool> HasError;

        // background: 시트가 놓인 면의 색(가림막 그라데이션을 행 배경과 맞추기 위해).
        public GsSheet(Color background)
        {
            AddToClassList("gs-col");
            _hoverColor = Gs.Blend(background, Color.white, 0.022f);
            _selectedColor = Gs.Blend(background, Gs.Accent, 0.14f);
        }

        public void Build(IList<SheetColumn> columns, IEnumerable<UnityEngine.Object> items, float trailingWidth = 130)
        {
            Clear();
            _rows.Clear();

            var header = Gs.Box("gs-sheet__header");
            if (Thumbnail != null) header.Add(Fixed(Gs.Label(""), 40));
            foreach (var column in columns) header.Add(Fixed(Gs.Label(column.Title), column.Width));
            if (Trailing != null) header.Add(Fixed(Gs.Label("ID"), trailingWidth));
            Add(header);

            foreach (var item in items)
            {
                if (item == null) continue;
                var so = new SerializedObject(item);
                var row = Gs.Box("gs-sheet__row");
                row.EnableInClassList("gs-sheet__row--selected", item == _selected);
                var target = item;
                row.RegisterCallback<ClickEvent>(e =>
                {
                    Select(target);
                    if (e.clickCount == 2) OpenRequested?.Invoke(target, null);
                });

                if (Thumbnail != null)
                {
                    var thumb = Gs.Box("gs-sheet__thumb");
                    var image = Thumbnail(item);
                    if (image is Sprite sprite) thumb.style.backgroundImage = new StyleBackground(sprite);
                    else if (image is Texture2D texture) thumb.style.backgroundImage = new StyleBackground(texture);
                    row.Add(Fixed(thumb, 40));
                }

                foreach (var column in columns)
                {
                    var property = column.Path != null ? so.FindProperty(column.Path) : null;
                    row.Add(Cell(target, column, property));
                }

                if (Trailing != null)
                {
                    var trailing = Gs.Row();
                    trailing.Add(Gs.Label(Trailing(item), "gs-caption", "gs-nowrap"));
                    if (HasError != null && HasError(item)) trailing.Add(new GsBadge("오류", Gs.Danger) { style = { marginLeft = 8 } });
                    row.Add(Fixed(trailing, trailingWidth));
                }

                _rows[item] = row;
                Add(row);
            }
        }

        public void Select(UnityEngine.Object item, bool notify = true)
        {
            _selected = item;
            foreach (var pair in _rows) pair.Value.EnableInClassList("gs-sheet__row--selected", pair.Key == item);
            if (notify) Selected?.Invoke(item);
        }

        VisualElement Cell(UnityEngine.Object item, SheetColumn column, SerializedProperty property)
        {
            var cell = Gs.Box("gs-sheet__cell", "gs-cell");
            cell.style.width = column.Width;

            string text;
            bool empty = false, broken = false;
            if (property == null) text = "—";
            else if (property.isArray && property.propertyType == SerializedPropertyType.Generic || property.propertyType == SerializedPropertyType.ManagedReference)
                text = DraftDiff.Text(property);
            else (text, empty, broken) = GsInlineProperty.Describe(property);

            var label = Gs.Label(text, "gs-cell__text");
            if (column.Bold) label.AddToClassList("gs-heading");
            if (empty) label.AddToClassList("gs-text-3");
            if (broken) label.style.color = Gs.Danger;
            cell.Add(label);

            var fade = Gs.Box("gs-cell__fade");
            fade.pickingMode = PickingMode.Ignore;
            fade.style.backgroundImage = new StyleBackground(FadeTexture());
            cell.Add(fade);

            var more = Gs.Box("gs-cell__more");
            more.Add(new Label("···"));
            more.tooltip = "자세히 보기 · 편집";
            more.RegisterCallback<ClickEvent>(e =>
            {
                e.StopPropagation();
                Select(item);
                OpenRequested?.Invoke(item, column.Path);
            });
            cell.Add(more);

            cell.RegisterCallback<PointerEnterEvent>(_ =>
            {
                fade.style.unityBackgroundImageTintColor = item == _selected ? _selectedColor : _hoverColor;
                cell.AddToClassList("gs-cell--hover");
            });
            cell.RegisterCallback<PointerLeaveEvent>(_ => cell.RemoveFromClassList("gs-cell--hover"));
            return cell;
        }

        static VisualElement Fixed(VisualElement content, float width)
        {
            var cell = Gs.Box("gs-sheet__cell");
            cell.style.width = width;
            cell.Add(content);
            return cell;
        }

        // 왼쪽 투명 → 오른쪽 불투명 흰색. 행 배경색으로 물들여 글자 끝을 부드럽게 가린다.
        static Texture2D FadeTexture()
        {
            if (_fade != null) return _fade;
            const int width = 64;
            _fade = new Texture2D(width, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            for (int x = 0; x < width; x++)
            {
                var t = Mathf.Clamp01(x / (width * 0.55f));
                _fade.SetPixel(x, 0, new Color(1, 1, 1, t * t * (3 - 2 * t)));
            }
            _fade.Apply();
            return _fade;
        }
    }
}
