using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace GBible.EditorTools.UI
{
    // DB 탭 공용 목록(모든 탭이 클래스 탭과 같은 모양): 폴더(중첩, 접고 펼침)와 요소 줄.
    //   · 폴더 줄: 버튼 빼고 어디를 눌러도 접고 펼친다. 마우스를 올린 줄만 옅게 밝아지고 도구 버튼이 보인다.
    //   · 요소 줄: 누르면 연다(상세).
    //   · 끌어 놓기: 요소나 폴더를 끌어 다른 폴더 위에 놓으면 그 폴더 안으로 옮긴다. 순서는 따로 두지 않는다.
    //   · 정렬: 한 폴더 안에서 폴더들 먼저, 그다음 요소들. 각각 이름순.
    // 폴더가 무엇인지(디스크 폴더·정리용 폴더·키워드 그룹)는 쓰는 쪽이 정하고, 옮기기도 쓰는 쪽(MoveItem·MoveFolder)이 한다.
    public sealed class CodexTree
    {
        const float DragThreshold = 5f;

        public sealed class Folder
        {
            // 고유 키. 맨 위 폴더(탭 이름으로 보이는 뿌리)는 보통 "".
            public string Path;
            // 상위 폴더의 Path. null이면 맨 위 단계에 따로 놓이는 묶음(예: 'Code 폴더 밖', 자동 그룹).
            public string Parent;
            public string Name;
            // 이름 옆 옅은 설명. null이면 "{ItemNoun} n개 · 하위 폴더 m개".
            public string Info;
            public string Tooltip;
            // 도구 버튼(마우스를 올린 줄에만 보인다).
            public Action<VisualElement> Tools;
            // 이름 옆 배지(항상 보인다: 오류·적용 전 등).
            public Action<VisualElement> Badges;
            // 요소 아래에 덧붙일 것(연결된 필드 등).
            public Action<VisualElement> Footer;
            // 비었을 때의 안내(null이면 기본 문구).
            public string Hint;
            public bool Draggable = true;
            // 요소를 받는지·폴더를 받는지.
            public bool AcceptsItems = true;
            public bool AcceptsFolders = true;
            public object Payload;
        }

        public sealed class Item
        {
            public string Key;
            public string Folder;
            // 정렬·검색에 쓰는 이름.
            public string Name;
            // 이름 말고도 검색에 걸릴 글자(표시 이름 등).
            public string SearchText;
            // 줄 만들기(GsGroupSection.Row + 배지 등). 누르기·끌기는 이 목록이 붙인다.
            public Func<VisualElement> Build;
            public Action Open;
            public bool Draggable = true;
            public object Payload;
        }

        // 접은 상태를 기억할 이름 앞머리(탭).
        public string Key = "";
        public string ItemNoun = "요소";
        public readonly List<Folder> Folders = new List<Folder>();
        public readonly List<Item> Items = new List<Item>();
        // 검색 글자. 비어 있지 않으면 걸린 요소와 그 폴더만 보인다(폴더 이름이 걸리면 그 안 전체).
        public string Query = "";
        // 검색 대신 쓸 거르기(예: 오류 항목만). 있으면 Query보다 먼저.
        public Func<Item, bool> Filter;

        // 옮기기(놓은 폴더). 같은 폴더에 놓으면 부르지 않는다.
        public Action<Item, Folder> MoveItem;
        public Action<Folder, Folder> MoveFolder;

        Dictionary<string, Folder> _byPath;
        Dictionary<string, List<Folder>> _children;
        Dictionary<string, List<Item>> _items;
        readonly List<(GsGroupSection section, Folder folder, int depth)> _sections = new List<(GsGroupSection, Folder, int)>();
        Drag _drag;

        bool Searching => Filter != null || !string.IsNullOrEmpty(Query);

        bool Hit(Item item)
        {
            if (Filter != null) return Filter(item);
            if (string.IsNullOrEmpty(Query)) return true;
            return Contains(item.Name, Query) || Contains(item.SearchText, Query);
        }

        static bool Contains(string text, string query) => !string.IsNullOrEmpty(text) && text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

        static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

        // ── 그리기 ─────────────────────────────────────────────

        // list에 그린다. host: 끌 때 유령 글자·표시를 띄울 곳(DB 창 뿌리). only: 그 폴더만(새 창).
        public void Build(VisualElement list, VisualElement host, string only = null)
        {
            Index();
            _sections.Clear();
            _drag = new Drag { Host = host, Scroll = list.GetFirstAncestorOfType<ScrollView>() };

            if (only != null)
            {
                if (!_byPath.TryGetValue(only, out var single))
                {
                    list.Add(Gs.Label("이 폴더가 더 이상 없습니다.", "gs-caption"));
                    return;
                }
                list.Add((VisualElement)Section(single, 0) ?? Gs.Label("검색 결과가 없습니다.", "gs-caption"));
                return;
            }

            var any = false;
            foreach (var top in Folders.Where(f => f.Parent == null))
            {
                var section = Section(top, 0);
                if (section == null) continue;
                list.Add(section);
                any = true;
            }
            if (!any && Searching) list.Add(Gs.Label("검색 결과가 없습니다.", "gs-caption"));
        }

        // 폴더·요소를 부모별로 모은다. 부모가 없거나 고리로 이어진 폴더는 뿌리("")로, 없는 폴더의 요소도 뿌리로.
        void Index()
        {
            _byPath = new Dictionary<string, Folder>(StringComparer.Ordinal);
            foreach (var folder in Folders)
                if (folder.Path != null && !_byPath.ContainsKey(folder.Path))
                    _byPath[folder.Path] = folder;
            var fallback = _byPath.ContainsKey("") ? "" : null;

            _children = new Dictionary<string, List<Folder>>(StringComparer.Ordinal);
            foreach (var folder in Folders)
            {
                if (folder.Parent == null) continue;
                var parent = folder.Parent;
                if (!_byPath.ContainsKey(parent) || ReachesSelf(folder)) parent = fallback;
                if (parent == null || parent == folder.Path) continue;
                if (!_children.TryGetValue(parent, out var list)) _children[parent] = list = new List<Folder>();
                list.Add(folder);
            }

            _items = new Dictionary<string, List<Item>>(StringComparer.Ordinal);
            foreach (var item in Items)
            {
                var folder = item.Folder != null && _byPath.ContainsKey(item.Folder) ? item.Folder : fallback;
                if (folder == null) continue;
                if (!_items.TryGetValue(folder, out var list)) _items[folder] = list = new List<Item>();
                list.Add(item);
            }
        }

        // 상위로 따라 올라가다 자기 자신을 다시 만나는지(고리).
        bool ReachesSelf(Folder folder)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { folder.Path };
            for (var p = folder.Parent; p != null && _byPath.TryGetValue(p, out var current); p = current.Parent)
                if (!seen.Add(p)) return true;
            return false;
        }

        List<Folder> ChildrenOf(Folder folder) =>
            _children.TryGetValue(folder.Path, out var list) ? list.OrderBy(f => f.Name, Names).ToList() : new List<Folder>();

        List<Item> ItemsOf(Folder folder) =>
            _items.TryGetValue(folder.Path, out var list) ? list.OrderBy(i => i.Name, Names).ToList() : new List<Item>();

        // 폴더 하나: 제목 줄 + 하위 폴더(이름순) + 요소(이름순). 검색으로 아무것도 안 남으면 null.
        GsGroupSection Section(Folder folder, int depth)
        {
            var members = ItemsOf(folder);
            var subfolders = ChildrenOf(folder);
            var nameHit = !string.IsNullOrEmpty(Query) && Filter == null && Contains(folder.Name, Query);
            var shown = Searching && !nameHit ? members.Where(Hit).ToList() : members;
            var children = new List<GsGroupSection>();
            foreach (var child in subfolders)
            {
                var built = nameHit ? SectionAll(child, depth + 1) : Section(child, depth + 1);
                if (built != null) children.Add(built);
            }
            if (Searching && !nameHit && shown.Count == 0 && children.Count == 0) return null;

            var section = new GsGroupSection(Key + ":" + folder.Path);
            section.AddToClassList("gs-tree__folder");
            if (depth > 0) section.AddToClassList("gs-tree__folder--nested");
            section.Head.AddToClassList("gs-group__head--toggle");
            section.Head.tooltip = (string.IsNullOrEmpty(folder.Tooltip) ? "" : folder.Tooltip + "\n") + "눌러서 접기 · 펼치기" + (folder.Draggable ? " · 끌어서 다른 폴더로 옮기기" : "");
            var info = folder.Info ?? DefaultInfo(members.Count, subfolders.Count);
            section.AddTitle(folder.Name, info);
            folder.Badges?.Invoke(section.Titles);
            folder.Tools?.Invoke(section.Tools);

            foreach (var child in children) section.Items.Add(child);
            foreach (var item in shown) section.Items.Add(Row(item));
            if (members.Count == 0 && subfolders.Count == 0)
                section.AddHint(folder.Hint ?? (folder.AcceptsItems ? $"비어 있습니다. {ItemNoun}를 끌어 놓거나 위의 '+'로 만드세요." : "비어 있습니다."));
            folder.Footer?.Invoke(section.Footer);

            RegisterFolder(section, folder);
            _sections.Add((section, folder, depth));
            return section;
        }

        // 검색에 폴더 이름이 걸렸을 때: 그 안은 거르지 않고 모두.
        GsGroupSection SectionAll(Folder folder, int depth)
        {
            var query = Query;
            Query = "";
            try { return Section(folder, depth); }
            finally { Query = query; }
        }

        string DefaultInfo(int items, int folders) => $"{ItemNoun} {items}개" + (folders > 0 ? $" · 하위 폴더 {folders}개" : "");

        VisualElement Row(Item item)
        {
            var row = item.Build?.Invoke() ?? GsGroupSection.Row(item.Name);
            row.userData = item;
            if (item.Draggable && MoveItem != null) RegisterItem(row, item);
            else row.RegisterCallback<ClickEvent>(_ => item.Open?.Invoke());
            return row;
        }

        // ── 누르기·끌기 ─────────────────────────────────────────

        sealed class Drag
        {
            public VisualElement Host;
            public ScrollView Scroll;
            public Item Item;
            public Folder Folder;
            public VisualElement Source;
            public Vector2 Start;
            public bool Pressed;
            public bool Active;
            public VisualElement Ghost;
            public GsGroupSection Target;
            public Folder TargetFolder;
        }

        static bool OnButton(IEventHandler target) =>
            target is VisualElement v && (v is GsButton || v.GetFirstAncestorOfType<GsButton>() != null);

        void RegisterItem(VisualElement row, Item item)
        {
            row.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0 || OnButton(e.target)) return;
                Press(row, e, item, null);
            });
            row.RegisterCallback<PointerMoveEvent>(e => Move(row, e));
            row.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!Release(row, e)) item.Open?.Invoke();
            });
            row.RegisterCallback<PointerCaptureOutEvent>(_ => Cancel(row));
        }

        void RegisterFolder(GsGroupSection section, Folder folder)
        {
            var head = section.Head;
            head.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0 || OnButton(e.target)) return;
                // 끌 수 없는 폴더는 누르기만(접고 펼치기).
                Press(head, e, null, folder.Draggable && MoveFolder != null ? folder : null);
            });
            head.RegisterCallback<PointerMoveEvent>(e => Move(head, e));
            head.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!Release(head, e)) section.Toggle();
            });
            head.RegisterCallback<PointerCaptureOutEvent>(_ => Cancel(head));
        }

        void Press(VisualElement handle, PointerDownEvent e, Item item, Folder folder)
        {
            _drag.Item = item;
            _drag.Folder = folder;
            _drag.Source = handle;
            _drag.Start = e.position;
            _drag.Pressed = true;
            _drag.Active = false;
            handle.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        void Move(VisualElement handle, PointerMoveEvent e)
        {
            if (!_drag.Pressed || _drag.Source != handle || !handle.HasPointerCapture(e.pointerId)) return;
            if (_drag.Item == null && _drag.Folder == null) return;
            var position = (Vector2)e.position;
            if (!_drag.Active)
            {
                if ((position - _drag.Start).magnitude < DragThreshold) return;
                Begin();
            }
            var origin = _drag.Host.worldBound.position;
            _drag.Ghost.style.left = position.x - origin.x + 12;
            _drag.Ghost.style.top = position.y - origin.y + 10;
            AutoScroll(position);
            SetTarget(FindTarget(position));
        }

        // 끌기였으면 놓고 true, 그냥 누른 것이면 false(누르기 동작은 부르는 쪽이).
        bool Release(VisualElement handle, PointerUpEvent e)
        {
            if (!_drag.Pressed || _drag.Source != handle || !handle.HasPointerCapture(e.pointerId)) return true;
            handle.ReleasePointer(e.pointerId);
            var dragged = _drag.Active;
            if (dragged) Drop();
            End();
            e.StopPropagation();
            return dragged;
        }

        void Cancel(VisualElement handle)
        {
            if (_drag.Source == handle && _drag.Active) End();
        }

        void Begin()
        {
            _drag.Active = true;
            _drag.Source.AddToClassList("gs-grouped--dragging");
            _drag.Ghost = Gs.Label(_drag.Item?.Name ?? _drag.Folder?.Name ?? "", "gs-grouped__ghost");
            _drag.Ghost.pickingMode = PickingMode.Ignore;
            _drag.Ghost.style.position = Position.Absolute;
            _drag.Host.Add(_drag.Ghost);
        }

        // 놓을 폴더: 마우스 아래의 가장 안쪽 폴더(펼친 폴더 안 요소 줄 위면 그 폴더). 받을 수 없으면 null.
        (GsGroupSection section, Folder folder) FindTarget(Vector2 position)
        {
            var hit = _sections.Where(s => s.section.worldBound.Contains(position) && s.section.resolvedStyle.display != DisplayStyle.None)
                               .OrderByDescending(s => s.depth).ThenByDescending(s => Depth(s.section))
                               .FirstOrDefault();
            if (hit.section == null) return (null, null);
            var target = hit.folder;
            if (_drag.Item != null)
            {
                if (!target.AcceptsItems || target.Path == ItemFolder(_drag.Item)) return (null, null);
            }
            else
            {
                var moving = _drag.Folder;
                if (!target.AcceptsFolders || target.Path == moving.Path || IsInside(target, moving) || target.Path == ParentOf(moving)) return (null, null);
            }
            return (hit.section, target);
        }

        // 화면 트리에서의 깊이(같은 단계 숫자끼리 겹칠 때 더 안쪽을 고른다).
        static int Depth(VisualElement element)
        {
            var depth = 0;
            for (var e = element.parent; e != null; e = e.parent) depth++;
            return depth;
        }

        string ItemFolder(Item item) => item.Folder != null && _byPath.ContainsKey(item.Folder) ? item.Folder : (_byPath.ContainsKey("") ? "" : null);

        string ParentOf(Folder folder) => folder.Parent != null && _byPath.ContainsKey(folder.Parent) ? folder.Parent : (folder.Parent == null ? null : "");

        // target이 folder 안쪽(자기 자신 포함)인지.
        bool IsInside(Folder target, Folder folder)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var current = target; current != null && seen.Add(current.Path);)
            {
                if (current.Path == folder.Path) return true;
                if (current.Parent == null || !_byPath.TryGetValue(current.Parent, out current)) break;
            }
            return false;
        }

        void SetTarget((GsGroupSection section, Folder folder) target)
        {
            if (_drag.Target == target.section) return;
            _drag.Target?.RemoveFromClassList("gs-tree__folder--drop");
            _drag.Target = target.section;
            _drag.TargetFolder = target.folder;
            _drag.Target?.AddToClassList("gs-tree__folder--drop");
        }

        void Drop()
        {
            var target = _drag.TargetFolder;
            if (target == null) return;
            if (_drag.Item != null) MoveItem?.Invoke(_drag.Item, target);
            else if (_drag.Folder != null) MoveFolder?.Invoke(_drag.Folder, target);
        }

        void End()
        {
            _drag.Source?.RemoveFromClassList("gs-grouped--dragging");
            _drag.Ghost?.RemoveFromHierarchy();
            _drag.Target?.RemoveFromClassList("gs-tree__folder--drop");
            _drag.Ghost = null;
            _drag.Target = null;
            _drag.TargetFolder = null;
            _drag.Active = false;
            _drag.Pressed = false;
            _drag.Item = null;
            _drag.Folder = null;
        }

        // 목록 끝 가까이 끌면 스크롤한다.
        void AutoScroll(Vector2 position)
        {
            var scroll = _drag.Scroll;
            if (scroll == null) return;
            var bounds = scroll.worldBound;
            const float edge = 40f;
            if (position.y < bounds.yMin + edge) scroll.scrollOffset -= new Vector2(0, 12);
            else if (position.y > bounds.yMax - edge) scroll.scrollOffset += new Vector2(0, 12);
        }
    }
}
