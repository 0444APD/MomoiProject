using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameSystems.EditorTools.UI
{
    public sealed class GsDropdownItem
    {
        public string Label;
        // 오른쪽에 옅게(ID 등).
        public string Caption;
        // 아래 줄에 옅게(설명).
        public string Detail;
        // 같은 그룹끼리 머리글 아래 묶인다.
        public string Group;
        public bool Selected;
        public bool Disabled;
        public Action OnPick;

        public GsDropdownItem(string label, Action onPick = null)
        {
            Label = label;
            OnPick = onPick;
        }
    }

    // 디자인 시스템의 드롭다운. 검색(한글 초성 포함), 그룹 머리글, 현재 값 표시, 키보드 조작(↑↓ Enter Esc).
    // Unity 기본 메뉴(GenericMenu) 대신 모든 선택 목록이 이것을 쓴다.
    public sealed class GsDropdown : VisualElement
    {
        const int SearchThreshold = 8;
        readonly List<GsDropdownItem> _items;
        readonly VisualElement _layer;
        readonly ScrollView _list;
        readonly List<(GsDropdownItem item, VisualElement row)> _visible = new List<(GsDropdownItem, VisualElement)>();
        readonly GsSearchField _search;
        int _active = -1;
        bool _closed;

        GsDropdown(VisualElement layer, List<GsDropdownItem> items, bool searchable, string placeholder)
        {
            _layer = layer;
            _items = items;
            AddToClassList("gs-dropdown");
            AddToClassList("gs-dropdown--hidden");
            focusable = true;

            if (searchable)
            {
                _search = new GsSearchField(placeholder);
                _search.Changed += _ => Rebuild();
                Add(_search);
            }
            _list = new ScrollView(ScrollViewMode.Vertical);
            _list.AddToClassList("gs-dropdown__list");
            Add(_list);

            RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            Rebuild();
        }

        // anchor 아래에 연다. anchor가 속한 디자인 시스템 루트(gs-root) 위에 띄운다.
        public static GsDropdown Show(VisualElement anchor, IEnumerable<GsDropdownItem> items, bool? searchable = null, string placeholder = "검색")
        {
            var host = FindHost(anchor);
            if (host == null) return null;
            var list = items.ToList();

            var layer = new VisualElement();
            layer.AddToClassList("gs-dropdown-layer");
            host.Add(layer);

            var dropdown = new GsDropdown(layer, list, searchable ?? list.Count >= SearchThreshold, placeholder);
            layer.Add(dropdown);
            layer.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.target == layer) dropdown.Close();
            });

            var anchorRect = host.WorldToLocal(anchor.worldBound);
            var width = Mathf.Clamp(Mathf.Max(anchorRect.width, 240f), 200f, Mathf.Max(200f, host.layout.width - 16f));
            dropdown.style.width = width;
            dropdown.style.left = Mathf.Clamp(anchorRect.x, 8f, Mathf.Max(8f, host.layout.width - width - 8f));
            dropdown.style.top = anchorRect.yMax + 4f;

            // 아래 공간이 모자라면 위로 연다.
            dropdown.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                var height = dropdown.layout.height;
                if (anchorRect.yMax + 4f + height > host.layout.height - 8f && anchorRect.y - 4f - height > 8f)
                    dropdown.style.top = anchorRect.y - 4f - height;
            });

            dropdown.schedule.Execute(() =>
            {
                dropdown.RemoveFromClassList("gs-dropdown--hidden");
                if (dropdown._search != null) dropdown._search.FocusInput();
                else dropdown.Focus();
                dropdown.ScrollToSelected();
            }).StartingIn(16);
            return dropdown;
        }

        static VisualElement FindHost(VisualElement anchor)
        {
            for (var e = anchor; e != null; e = e.parent)
                if (e.ClassListContains("gs-root")) return e;
            return anchor.panel?.visualTree;
        }

        // 닫힐 때(고르거나 바깥을 눌러서). 입력 칸이 '편집 중' 표시를 끄는 데 쓴다.
        public event Action Closed;

        public void Close()
        {
            if (_closed) return;
            _closed = true;
            AddToClassList("gs-dropdown--hidden");
            Closed?.Invoke();
            _layer.schedule.Execute(_layer.RemoveFromHierarchy).StartingIn(120);
        }

        void Rebuild()
        {
            _list.Clear();
            _visible.Clear();
            var query = _search?.Value?.Trim() ?? "";
            string group = null;

            foreach (var item in _items)
            {
                if (!Matches(item, query)) continue;
                if (!string.IsNullOrEmpty(item.Group) && item.Group != group)
                {
                    group = item.Group;
                    _list.Add(Gs.Label(group, "gs-dropdown__group"));
                }
                var row = MakeRow(item);
                _visible.Add((item, row));
                _list.Add(row);
            }

            if (_visible.Count == 0) _list.Add(Gs.Label(string.IsNullOrEmpty(query) ? "항목이 없습니다" : "검색 결과가 없습니다", "gs-dropdown__empty"));

            var selected = _visible.FindIndex(v => v.item.Selected && !v.item.Disabled);
            SetActive(string.IsNullOrEmpty(query) && selected >= 0 ? selected : _visible.FindIndex(v => !v.item.Disabled));
        }

        VisualElement MakeRow(GsDropdownItem item)
        {
            var row = Gs.Box("gs-dropdown__item");
            row.EnableInClassList("gs-dropdown__item--selected", item.Selected);
            row.EnableInClassList("gs-dropdown__item--disabled", item.Disabled);
            row.Add(Gs.Label(item.Selected ? "✓" : "", "gs-dropdown__check"));

            var texts = Gs.Col("gs-grow");
            texts.style.minWidth = 0;
            texts.Add(Gs.Label(item.Label, "gs-dropdown__label"));
            if (!string.IsNullOrEmpty(item.Detail)) texts.Add(Gs.Label(item.Detail, "gs-dropdown__detail"));
            row.Add(texts);
            if (!string.IsNullOrEmpty(item.Caption)) row.Add(Gs.Label(item.Caption, "gs-dropdown__caption"));

            if (!item.Disabled)
            {
                row.RegisterCallback<PointerEnterEvent>(_ => SetActive(_visible.FindIndex(v => v.row == row)));
                row.RegisterCallback<ClickEvent>(_ => Pick(item));
            }
            return row;
        }

        void Pick(GsDropdownItem item)
        {
            if (item.Disabled) return;
            Close();
            item.OnPick?.Invoke();
        }

        void SetActive(int index)
        {
            if (_active >= 0 && _active < _visible.Count) _visible[_active].row.RemoveFromClassList("gs-dropdown__item--active");
            _active = index;
            if (_active < 0 || _active >= _visible.Count) return;
            var row = _visible[_active].row;
            row.AddToClassList("gs-dropdown__item--active");
            if (row.layout.height > 0) _list.ScrollTo(row);
        }

        void ScrollToSelected()
        {
            if (_active >= 0 && _active < _visible.Count) _list.ScrollTo(_visible[_active].row);
        }

        void OnKey(KeyDownEvent e)
        {
            switch (e.keyCode)
            {
                case KeyCode.Escape:
                    Close();
                    break;
                case KeyCode.DownArrow:
                    Move(1);
                    break;
                case KeyCode.UpArrow:
                    Move(-1);
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (_active >= 0 && _active < _visible.Count) Pick(_visible[_active].item);
                    break;
                default:
                    return;
            }
            e.StopPropagation();
        }

        void Move(int step)
        {
            if (_visible.Count == 0) return;
            var index = _active;
            for (int i = 0; i < _visible.Count; i++)
            {
                index = (index + step + _visible.Count) % _visible.Count;
                if (!_visible[index].item.Disabled) break;
            }
            SetActive(index);
        }

        // ── 검색: 이름·캡션·그룹 포함 검색 + 한글 초성 검색("ㅊㅊ" → "처치 수") ──

        static readonly char[] Initials = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ".ToCharArray();

        public static bool Matches(GsDropdownItem item, string query)
        {
            if (string.IsNullOrEmpty(query)) return true;
            return Contains(item.Label, query) || Contains(item.Caption, query) || Contains(item.Group, query) ||
                   (IsInitialsQuery(query) && Contains(InitialsOf(item.Label), query.Replace(" ", "")));
        }

        static bool Contains(string text, string query) =>
            !string.IsNullOrEmpty(text) && text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

        static bool IsInitialsQuery(string query) => query.Replace(" ", "").All(c => Array.IndexOf(Initials, c) >= 0);

        public static string InitialsOf(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var builder = new StringBuilder(text.Length);
            foreach (var c in text)
            {
                if (c >= '가' && c <= '힣') builder.Append(Initials[(c - '가') / 588]);
                else if (!char.IsWhiteSpace(c)) builder.Append(c);
            }
            return builder.ToString();
        }
    }
}
