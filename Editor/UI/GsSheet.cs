using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GBible.EditorTools.UI
{
    public sealed class SheetColumn
    {
        public string Title;
        public float Width = 150;
        public string Path;
        public bool Bold;
    }

    // 좁은 칸에 정보를 담는 시트. 셀은 읽기 전용이고(한 번 클릭으로 값이 바뀌는 위험 없음), 행을 더블클릭하면 상세 패널을 연다.
    // 칸보다 긴 글자는 끝이 행 배경색으로 흐리게 가려진다. 머리줄 경계를 끌어 열 폭을 바꾼다.
    public sealed class GsSheet : VisualElement
    {
        const float MinColumnWidth = 60;
        public const string TrailingKey = "__trailing";
        static Texture2D _fade;

        readonly Color _baseColor;
        readonly Color _hoverColor;
        readonly Color _selectedColor;
        readonly Dictionary<UnityEngine.Object, VisualElement> _rows = new Dictionary<UnityEngine.Object, VisualElement>();
        readonly List<List<VisualElement>> _columnCells = new List<List<VisualElement>>();
        UnityEngine.Object _selected;

        public event Action<UnityEngine.Object> Selected;
        // (항목, 더블클릭한 셀의 필드 경로)
        public event Action<UnityEngine.Object, string> OpenRequested;
        // (열 경로 또는 TrailingKey, 새 폭) — 열 폭을 저장할 때 쓴다.
        public event Action<string, float> ColumnResized;

        public Func<UnityEngine.Object, UnityEngine.Object> Thumbnail;
        public Func<UnityEngine.Object, string> Trailing;
        public string TrailingTitle = "ID";
        public float TrailingWidth = 110;
        public Func<UnityEngine.Object, bool> HasError;

        // background: 시트가 놓인 면의 색(가림막 그라데이션을 행 배경과 맞추기 위해).
        public GsSheet(Color background)
        {
            AddToClassList("gs-col");
            _baseColor = background;
            _hoverColor = Gs.Blend(background, Color.white, 0.022f);
            _selectedColor = Gs.Blend(background, Gs.Accent, 0.14f);
        }

        public void Build(IList<SheetColumn> columns, IEnumerable<UnityEngine.Object> items)
        {
            Clear();
            _rows.Clear();
            _columnCells.Clear();
            var columnCount = columns.Count + (Trailing != null ? 1 : 0);
            for (int i = 0; i < columnCount; i++) _columnCells.Add(new List<VisualElement>());

            var header = Gs.Box("gs-sheet__header");
            if (Thumbnail != null) header.Add(Fixed(Gs.Label(""), 40));
            for (int i = 0; i < columns.Count; i++) header.Add(HeaderCell(columns[i].Title, columns[i].Width, i, columns[i].Path));
            if (Trailing != null) header.Add(HeaderCell(TrailingTitle, TrailingWidth, columns.Count, TrailingKey));
            Add(header);

            foreach (var item in items)
            {
                if (item == null) continue;
                var so = new SerializedObject(item);
                var row = Gs.Box("gs-sheet__row");
                row.EnableInClassList("gs-sheet__row--selected", item == _selected);
                var target = item;
                string pressedPath = null;
                row.RegisterCallback<ClickEvent>(e =>
                {
                    Select(target);
                    if (e.clickCount == 2) OpenRequested?.Invoke(target, pressedPath);
                });
                row.RegisterCallback<PointerEnterEvent>(_ => TintRow(row, target, true));
                row.RegisterCallback<PointerLeaveEvent>(_ => TintRow(row, target, false));

                if (Thumbnail != null)
                {
                    var thumb = Gs.Box("gs-sheet__thumb");
                    var image = Thumbnail(item);
                    if (image is Sprite sprite) thumb.style.backgroundImage = new StyleBackground(sprite);
                    else if (image is Texture2D texture) thumb.style.backgroundImage = new StyleBackground(texture);
                    row.Add(Fixed(thumb, 40));
                }

                for (int i = 0; i < columns.Count; i++)
                {
                    var column = columns[i];
                    var property = column.Path != null ? so.FindProperty(column.Path) : null;
                    var cell = Cell(column, property);
                    var path = column.Path;
                    cell.RegisterCallback<PointerDownEvent>(_ => pressedPath = path);
                    _columnCells[i].Add(cell);
                    row.Add(cell);
                }

                if (Trailing != null)
                {
                    var trailing = Gs.Row();
                    trailing.Add(Gs.Label(Trailing(item), "gs-caption", "gs-nowrap"));
                    if (HasError != null && HasError(item)) trailing.Add(new GsBadge("오류", Gs.Danger) { style = { marginLeft = 8 } });
                    var cell = Fixed(trailing, TrailingWidth);
                    _columnCells[columns.Count].Add(cell);
                    row.Add(cell);
                }

                TintRow(row, item, false);
                _rows[item] = row;
                Add(row);
            }
        }

        public void Select(UnityEngine.Object item, bool notify = true)
        {
            _selected = item;
            foreach (var pair in _rows)
            {
                pair.Value.EnableInClassList("gs-sheet__row--selected", pair.Key == item);
                TintRow(pair.Value, pair.Key, false);
            }
            if (notify) Selected?.Invoke(item);
        }

        // 가림막 색을 행의 현재 배경(기본·마우스 올림·선택)과 맞춘다.
        void TintRow(VisualElement row, UnityEngine.Object item, bool hover)
        {
            var color = item == _selected ? _selectedColor : hover ? _hoverColor : _baseColor;
            row.Query(className: "gs-cell__fade").ForEach(fade => fade.style.unityBackgroundImageTintColor = color);
        }

        VisualElement HeaderCell(string title, float width, int index, string key)
        {
            var cell = Fixed(Gs.Label(title), width);
            var handle = Gs.Box("gs-sheet__resizer");
            handle.tooltip = "끌어서 열 폭 조절";
            cell.Add(handle);
            _columnCells[index].Add(cell);

            float startX = 0, startWidth = 0;
            bool dragging = false;
            handle.RegisterCallback<PointerDownEvent>(e =>
            {
                dragging = true;
                startX = e.position.x;
                startWidth = cell.resolvedStyle.width;
                handle.CapturePointer(e.pointerId);
                e.StopPropagation();
            });
            handle.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!dragging) return;
                SetColumnWidth(index, Mathf.Max(MinColumnWidth, startWidth + e.position.x - startX));
            });
            handle.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!dragging) return;
                dragging = false;
                handle.ReleasePointer(e.pointerId);
                ColumnResized?.Invoke(key, cell.resolvedStyle.width);
            });
            return cell;
        }

        void SetColumnWidth(int index, float width)
        {
            foreach (var cell in _columnCells[index]) cell.style.width = width;
        }

        VisualElement Cell(SheetColumn column, SerializedProperty property)
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
            label.tooltip = text;
            cell.Add(label);

            // 글자가 칸보다 길 때만 끝을 흐리게 가린다.
            var fade = Gs.Box("gs-cell__fade");
            fade.pickingMode = PickingMode.Ignore;
            fade.style.backgroundImage = new StyleBackground(FadeTexture());
            cell.Add(fade);
            cell.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                var needed = label.MeasureTextSize(label.text, 0, MeasureMode.Undefined, 0, MeasureMode.Undefined).x;
                fade.style.display = needed > cell.contentRect.width - 4 ? DisplayStyle.Flex : DisplayStyle.None;
            });
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
                var t = Mathf.Clamp01(x / (width * 0.8f));
                _fade.SetPixel(x, 0, new Color(1, 1, 1, t * t * (3 - 2 * t)));
            }
            _fade.Apply();
            return _fade;
        }
    }
}
