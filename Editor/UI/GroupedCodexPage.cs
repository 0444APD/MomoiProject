using System;
using System.Collections.Generic;
using System.Linq;
using GBible.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace GBible.EditorTools.UI
{
    // 그룹 + 항목을 한 탭에서 다루는 도감 화면(키워드, 스프라이트 규격).
    // 모양은 모든 도감 탭 공용 목록(CodexTree, 클래스 탭과 같음): 그룹이 폴더처럼 보이고 그룹 안에 그룹을 둘 수 있다.
    //  · 그룹 줄을 누르면 접고 펼친다. 그룹 편집(ID 바꾸기·삭제)은 그룹 줄의 ⋯ 메뉴.
    //  · 항목 줄을 누르면 상세 화면.
    //  · 항목·그룹을 끌어 다른 그룹 위에 놓으면 그 안으로 옮긴다. 순서는 이름순(따로 두지 않는다).
    // 모든 변경은 도감의 적용 대기 목록에 모이고, 저장(Ctrl+S)할 때 적용한다.
    //
    // 끌어서 순서를 바꾸던 예전 화면(IOrderedData.Order)은 지우지 않고 숨겨 두었다: ManualOrder를 켜면 돌아온다.
    public abstract class GroupedCodexPage : CodexPage
    {
        const float DragThreshold = 5f;

        // true면 예전 화면(그룹 카드, 끌어서 순서 바꾸기). 기본은 공용 목록(이름순).
        public static bool ManualOrder = false;

        protected abstract Type GroupType { get; }
        protected abstract Type ItemType { get; }
        // 화면 글자: 그룹·항목을 부르는 말(예: "그룹"·"키워드", "그룹"·"규격").
        protected abstract string GroupNoun { get; }
        protected abstract string ItemNoun { get; }
        protected abstract string EmptyHint { get; }

        // 칩에 보일 글자(기본은 ID).
        protected virtual string ItemText(GameData item) => item.Id;
        protected virtual string ItemTooltip(GameData item) => item.Id;
        // 그룹 카드 제목 아래 한 줄.
        protected virtual string GroupInfo(GameData group, int count) => $"{ItemNoun} {count}개";
        // 목록 위 도구 줄(없으면 비워 둔다).
        protected virtual void Toolbar(CodexPageContext context, VisualElement bar)
        {
        }

        // 그룹 카드의 항목 칩 아래에 덧붙일 것(예: 이 그룹에 연결된 필드). 없으면 아무것도 넣지 않는다.
        protected virtual void GroupFooter(CodexPageContext context, GameData group, VisualElement body)
        {
        }

        // parent: 상위 그룹 ID(맨 위면 "").
        protected abstract void NewGroup(CodexPageContext context, string parent);
        protected abstract void AddItems(CodexPageContext context, GameData group);

        static EditorDatabase Db => EditorDatabase.Instance;
        static CodexStaging Staging => EditorDatabase.Staging;

        public override string CreateLabel => $"+ 새 {GroupNoun}";

        public override void Create(CodexPageContext context) => NewGroup(context, "");

        public override string Caption(CodexPageContext context)
        {
            var groups = CodexGroups.Groups(GroupType).Count();
            var items = Db.OfType(ItemType).Count();
            if (!ManualOrder)
                return $"{GroupNoun} {groups}개 · {ItemNoun} {items}개  ·  줄을 눌러 접고 펼치고, 끌어서 다른 {GroupNoun}에 놓아 옮깁니다(그룹 안에 그룹도 둘 수 있습니다) · 이름순";
            return $"{GroupNoun} {groups}개 · {ItemNoun} {items}개  ·  제목 왼쪽 화살표로 접고 펼칩니다 · 끌어서 순서를 바꾸고, {ItemNoun}를 다른 {GroupNoun}에 놓아 옮깁니다";
        }

        public override void Build(CodexPageContext context)
        {
            if (!ManualOrder)
            {
                BuildTree(context);
                return;
            }
            var root = context.Content;
            var bar = Gs.Row("gs-grouped__toolbar");
            Toolbar(context, bar);
            if (bar.childCount > 0) root.Add(bar);

            var list = Gs.Col("gs-grouped__list");
            root.Add(list);

            var query = (context.Search ?? "").Trim();
            bool Hit(GameData d) => query.Length == 0 || d.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

            var groups = CodexGroups.Groups(GroupType).ToList();
            // 그룹 하나만(새 창): 그 그룹만 그리고, 그룹 없는 항목·빈 안내는 그리지 않는다.
            if (context.Group != null) groups = groups.Where(g => g.Id == context.Group).ToList();
            var drag = new DragState { Context = context };
            foreach (var group in groups)
            {
                var items = CodexGroups.Items(ItemType, group.Id).ToList();
                if (query.Length > 0 && !Hit(group) && !items.Any(Hit)) continue;
                var shown = items.Where(i => query.Length == 0 || Hit(group) || Hit(i)).ToList();
                var (card, chips) = GroupCard(context, group, items.Count, shown, drag);
                drag.Cards.Add((card, chips, group));
                list.Add(card);
            }

            // 그룹이 없는(지워진 그룹을 가리키는) 항목: 다른 그룹으로 끌어 옮기면 정리된다.
            var orphans = context.Group != null ? new List<GameData>() : Db.OfType(ItemType).Where(i => groups.All(g => g.Id != CodexGroups.GroupOf(i))).Where(Hit).OrderBy(CodexGroups.OrderOf).ToList();
            if (orphans.Count > 0)
            {
                var section = new GsGroupSection(Key + ":$none");
                section.AddTitle($"{GroupNoun} 없음", $"다른 {GroupNoun}으로 끌어 옮기세요");
                foreach (var item in orphans) section.Items.Add(Chip(context, item, drag));
                list.Add(section);
            }

            if (groups.Count == 0 && context.Group != null) list.Add(Gs.Label($"'{context.Group}' {GroupNoun}이(가) 더 이상 없습니다.", "gs-caption"));
            else if (groups.Count == 0)
            {
                var empty = Gs.Box("gs-empty");
                empty.Add(Gs.Label($"아직 {GroupNoun}이(가) 없습니다", "gs-title"));
                empty.Add(Gs.Space(8));
                empty.Add(Gs.Label(EmptyHint, "gs-caption"));
                empty.Add(Gs.Space(18));
                empty.Add(new GsButton($"+ 첫 {GroupNoun} 만들기", () => NewGroup(context, ""), GsButtonKind.Primary));
                list.Add(empty);
            }
        }

        // ── 공용 목록(기본) ─────────────────────────────────────
        // 맨 위 묶음(탭 이름) 아래에 그룹이 폴더처럼, 그룹 안에 하위 그룹과 항목이 이름순으로.

        const string NoGroup = "$none";

        static string ParentOf(GameData group) => (group as INestedData)?.Parent ?? "";

        void BuildTree(CodexPageContext context)
        {
            var root = context.Content;
            var bar = Gs.Row("gs-grouped__toolbar");
            Toolbar(context, bar);
            if (bar.childCount > 0) root.Add(bar);
            var list = Gs.Col("gs-grouped__list");
            root.Add(list);

            var groups = Db.OfType(GroupType).ToList();
            var ids = new HashSet<string>(groups.Select(g => g.Id), StringComparer.Ordinal);
            var info = DataTypes.Find(ItemType);
            var tree = new CodexTree { Key = Key, ItemNoun = ItemNoun, Query = (context.Search ?? "").Trim() };

            tree.Folders.Add(new CodexTree.Folder
            {
                Path = "", Parent = null, Name = info?.DisplayName ?? ItemNoun,
                Info = $"{GroupNoun} {groups.Count}개 · {ItemNoun} {Db.OfType(ItemType).Count()}개",
                Draggable = false, AcceptsItems = false,
                Hint = EmptyHint,
                Tools = tools =>
                {
                    tools.Add(new GsButton($"+ {GroupNoun}", () => NewGroup(context, ""), GsButtonKind.Ghost));
                },
            });
            foreach (var group in groups)
            {
                var g = group;
                var count = CodexGroups.Items(ItemType, g.Id).Count();
                var errors = context.Issues.Count(i => i.Severity == ValidationSeverity.Error && i.Asset == g);
                tree.Folders.Add(new CodexTree.Folder
                {
                    Path = g.Id,
                    Parent = ids.Contains(ParentOf(g)) ? ParentOf(g) : "",
                    Name = g.Id,
                    Info = GroupInfo(g, count),
                    Tooltip = $"{GroupNoun} {g.Id}",
                    Payload = g,
                    Badges = titles =>
                    {
                        if (errors > 0) titles.Add(Spaced(new GsBadge($"오류 {errors}", Gs.Danger)));
                        if (Staging.IsPending(g)) titles.Add(Spaced(new GsBadge(Staging.IsAdded(g) ? "새 항목 · 적용 전" : "변경 · 적용 전", Gs.Warning)));
                    },
                    Tools = tools =>
                    {
                        tools.Add(new GsButton($"+ {ItemNoun}", () => AddItems(context, g), GsButtonKind.Ghost));
                        tools.Add(new GsButton($"+ 하위 {GroupNoun}", () => NewGroup(context, g.Id), GsButtonKind.Ghost));
                        if (context.Group != g.Id)
                        {
                            var open = new GsButton("↗", () => CodexGroupWindow.Open(Key, g.Id, $"{GroupNoun} · {g.Id}"), GsButtonKind.Ghost);
                            open.tooltip = "새 창에서 열기";
                            tools.Add(open);
                        }
                        var menu = new List<GsDropdownItem>
                        {
                            new GsDropdownItem($"{GroupNoun} 편집", () => context.OpenDetail(g)) { Detail = "ID 바꾸기·삭제" },
                        };
                        GsButton more = null;
                        more = new GsButton("⋯", () => GsDropdown.Show(more, menu, searchable: false), GsButtonKind.Ghost);
                        more.tooltip = $"{GroupNoun} 메뉴";
                        tools.Add(more);
                    },
                    Footer = footer => GroupFooter(context, g, footer),
                });
            }

            // 그룹이 없는(지워진 그룹을 가리키는) 항목: 다른 그룹으로 끌어 옮기면 정리된다.
            var items = Db.OfType(ItemType).ToList();
            if (items.Any(i => !ids.Contains(CodexGroups.GroupOf(i) ?? "")))
                tree.Folders.Add(new CodexTree.Folder
                {
                    Path = NoGroup, Parent = null, Name = $"{GroupNoun} 없음", Info = $"다른 {GroupNoun}으로 끌어 옮기세요",
                    Draggable = false, AcceptsItems = false, AcceptsFolders = false,
                });
            foreach (var item in items)
            {
                var it = item;
                var group = CodexGroups.GroupOf(it) ?? "";
                tree.Items.Add(new CodexTree.Item
                {
                    Key = it.Id,
                    Folder = ids.Contains(group) ? group : NoGroup,
                    Name = it.Id,
                    SearchText = ItemText(it),
                    Build = () => TreeRow(context, it),
                    Open = () => context.OpenDetail(it),
                    Payload = it,
                });
            }

            tree.MoveItem = (item, folder) =>
            {
                if (Staging.Work((GameData)item.Payload) is IGroupedData grouped) grouped.Group = folder.Path;
                Staging.Touch();
            };
            tree.MoveFolder = (folder, target) =>
            {
                if (Staging.Work((GameData)folder.Payload) is INestedData nested) nested.Parent = target.Path;
                Staging.Touch();
            };
            tree.Build(list, context.Host, context.Group);
        }

        static VisualElement Spaced(VisualElement badge)
        {
            badge.style.marginLeft = 8;
            return badge;
        }

        VisualElement TreeRow(CodexPageContext context, GameData item)
        {
            var row = GsGroupSection.Row(ItemText(item));
            if (context.Issues.Any(i => i.Asset == item && i.Severity == ValidationSeverity.Error))
            {
                row.AddToClassList("gs-group__row--error");
                row.Add(new GsBadge("오류", Gs.Danger));
            }
            if (Staging.IsPending(item)) row.Add(new GsBadge(Staging.IsAdded(item) ? "새 항목 · 적용 전" : "변경 · 적용 전", Gs.Warning));
            row.tooltip = ItemTooltip(item) + "\n눌러서 편집 · 끌어서 다른 그룹으로 옮기기";
            return row;
        }

        // 새 그룹을 parent 아래에 둔다(만든 직후, 적용 전 항목).
        protected static void PlaceUnder(string id, string parent)
        {
            if (string.IsNullOrEmpty(parent)) return;
            if (Staging.Work(Db.FindById(id)) is INestedData nested)
            {
                nested.Parent = parent;
                Staging.Touch();
            }
        }

        // ── 예전 화면(ManualOrder): 그룹 카드, 끌어서 순서 바꾸기 ──────────────

        (VisualElement card, VisualElement chips) GroupCard(CodexPageContext context, GameData group, int count, List<GameData> items, DragState drag)
        {
            var section = new GsGroupSection(Key + ":" + group.Id);
            var errors = context.Issues.Count(i => i.Severity == ValidationSeverity.Error && (i.Asset == group || items.Contains(i.Asset as GameData)));

            // 제목: 누르면 그룹 편집, 끌면 그룹 순서 바꾸기.
            var title = section.AddTitle(group.Id, GroupInfo(group, count));
            title.AddToClassList("gs-group__title--link");
            title.tooltip = $"눌러서 {GroupNoun} 편집(ID 바꾸기·삭제) · 끌어서 {GroupNoun} 순서 바꾸기";
            if (errors > 0) section.Tools.Add(new GsBadge($"오류 {errors}", Gs.Danger));
            if (Staging.IsPending(group)) section.Tools.Add(new GsBadge(Staging.IsAdded(group) ? "새 항목 · 적용 전" : "변경 · 적용 전", Gs.Warning));
            section.Tools.Add(new GsButton($"+ {ItemNoun}", () => AddItems(context, group), GsButtonKind.Ghost));
            if (context.Group == null)
            {
                var open = new GsButton("↗", () => CodexGroupWindow.Open(Key, group.Id, $"{GroupNoun} · {group.Id}"), GsButtonKind.Ghost);
                open.tooltip = "새 창에서 열기";
                section.Tools.Add(open);
            }

            foreach (var item in items) section.Items.Add(Chip(context, item, drag));
            if (items.Count == 0) section.AddHint($"{ItemNoun}를 여기로 끌어 놓거나 '+ {ItemNoun}'로 추가하세요.");
            GroupFooter(context, group, section.Footer);

            RegisterDrag(title, group, section, drag);
            return (section, section.Items);
        }

        // 항목 한 줄.
        VisualElement Chip(CodexPageContext context, GameData item, DragState drag)
        {
            var row = GsGroupSection.Row(ItemText(item));
            if (context.Issues.Any(i => i.Asset == item && i.Severity == ValidationSeverity.Error))
            {
                row.AddToClassList("gs-group__row--error");
                row.Add(new GsBadge("오류", Gs.Danger));
            }
            if (Staging.IsPending(item)) row.Add(new GsBadge(Staging.IsAdded(item) ? "새 항목 · 적용 전" : "변경 · 적용 전", Gs.Warning));
            row.tooltip = ItemTooltip(item) + "\n눌러서 편집 · 끌어서 옮기기";
            row.userData = item;
            RegisterDrag(row, item, row, drag);
            return row;
        }

        // ── 끌어 놓기 ───────────────────────────────────────────

        sealed class DragState
        {
            public CodexPageContext Context;
            public readonly List<(VisualElement card, VisualElement chips, GameData group)> Cards = new List<(VisualElement, VisualElement, GameData)>();
            public GameData Item;
            public bool IsGroup;
            public VisualElement Source;
            public Vector2 Start;
            public bool Active;
            public VisualElement Ghost;
            public VisualElement Marker;
            public bool HasTarget;
            // 항목을 놓을 그룹.
            public GameData TargetGroup;
            // 이 항목 앞에 놓는다(null이면 맨 뒤). 검색으로 일부만 보일 때도 전체 순서에서 자리를 찾는다.
            public GameData Before;
        }

        void RegisterDrag(VisualElement handle, GameData item, VisualElement source, DragState drag)
        {
            var isGroup = GroupType.IsInstanceOfType(item);
            handle.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                drag.Item = item;
                drag.IsGroup = isGroup;
                drag.Source = source;
                drag.Start = e.position;
                drag.Active = false;
                handle.CapturePointer(e.pointerId);
                e.StopPropagation();
            });
            handle.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (drag.Item != item || !handle.HasPointerCapture(e.pointerId)) return;
                var position = (Vector2)e.position;
                if (!drag.Active)
                {
                    if ((position - drag.Start).magnitude < DragThreshold) return;
                    Begin(drag, isGroup ? item.Id : ItemText(item));
                }
                Move(drag, position);
            });
            handle.RegisterCallback<PointerUpEvent>(e =>
            {
                if (drag.Item != item || !handle.HasPointerCapture(e.pointerId)) return;
                handle.ReleasePointer(e.pointerId);
                if (drag.Active)
                {
                    Drop(drag);
                    End(drag);
                }
                else
                {
                    drag.Item = null;
                    // 끌지 않고 뗀 것은 누른 것: 상세 화면을 연다(그룹 제목·항목 줄 모두).
                    drag.Context.OpenDetail(item);
                }
            });
            handle.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                if (drag.Item == item && drag.Active) End(drag);
            });
        }

        static void Begin(DragState drag, string text)
        {
            drag.Active = true;
            drag.Source.AddToClassList("gs-grouped--dragging");
            var host = drag.Context.Host;
            drag.Ghost = Gs.Label(text, "gs-grouped__ghost");
            drag.Ghost.pickingMode = PickingMode.Ignore;
            drag.Ghost.style.position = Position.Absolute;
            host.Add(drag.Ghost);
            drag.Marker = Gs.Box("gs-grouped__marker");
            drag.Marker.pickingMode = PickingMode.Ignore;
            drag.Marker.style.position = Position.Absolute;
            drag.Marker.style.display = DisplayStyle.None;
            host.Add(drag.Marker);
        }

        static void Move(DragState drag, Vector2 position)
        {
            var origin = drag.Context.Host.worldBound.position;
            drag.Ghost.style.left = position.x - origin.x + 12;
            drag.Ghost.style.top = position.y - origin.y + 10;
            AutoScroll(drag, position);

            drag.HasTarget = false;
            drag.TargetGroup = null;
            drag.Before = null;
            drag.Marker.style.display = DisplayStyle.None;
            if (drag.Cards.Count == 0) return;

            if (!drag.IsGroup)
            {
                var hit = drag.Cards.FirstOrDefault(c => c.card.worldBound.Contains(position));
                if (hit.card == null) return;
                // 접힌 그룹에 놓으면 맨 뒤로 들어간다.
                var folded = hit.card is GsGroupSection target && target.FoldedNow;
                var rows = folded
                    ? new List<VisualElement>()
                    : hit.chips.Children().Where(c => c.ClassListContains("gs-group__row") && c != drag.Source).ToList();
                // 항목은 세로로 한 줄씩: 줄의 위아래 중간을 기준으로 앞·뒤를 가른다.
                var index = rows.Count;
                for (int i = 0; i < rows.Count; i++)
                {
                    if (position.y < rows[i].worldBound.center.y)
                    {
                        index = i;
                        break;
                    }
                }
                drag.HasTarget = true;
                drag.TargetGroup = hit.group;
                drag.Before = index < rows.Count ? rows[index].userData as GameData : null;
                Rect at;
                if (index < rows.Count)
                {
                    var r = rows[index].worldBound;
                    at = new Rect(r.xMin, r.yMin - 1, r.width, 2);
                }
                else if (rows.Count > 0)
                {
                    var r = rows[rows.Count - 1].worldBound;
                    at = new Rect(r.xMin, r.yMax + 1, r.width, 2);
                }
                else
                {
                    var r = hit.card.worldBound;
                    at = new Rect(r.xMin + 24, r.yMax - 2, r.width - 24, 2);
                }
                Place(drag.Marker, at, origin);
                return;
            }

            // 그룹: 카드 사이(위아래 중간 기준).
            var cards = drag.Cards.Where(c => c.card != drag.Source).ToList();
            var slot = cards.Count;
            for (int i = 0; i < cards.Count; i++)
                if (position.y < cards[i].card.worldBound.center.y)
                {
                    slot = i;
                    break;
                }
            drag.HasTarget = true;
            drag.Before = slot < cards.Count ? cards[slot].group : null;
            var list = drag.Cards[0].card.parent.worldBound;
            float y;
            if (slot < cards.Count) y = cards[slot].card.worldBound.yMin - 5;
            else y = cards.Count > 0 ? cards[cards.Count - 1].card.worldBound.yMax + 3 : list.yMin;
            Place(drag.Marker, new Rect(list.xMin, y, list.width, 2), origin);
        }

        static void Place(VisualElement marker, Rect world, Vector2 origin)
        {
            marker.style.display = DisplayStyle.Flex;
            marker.style.left = world.x - origin.x;
            marker.style.top = world.y - origin.y;
            marker.style.width = world.width;
            marker.style.height = world.height;
        }

        // 목록 끝 가까이 끌면 스크롤한다.
        static void AutoScroll(DragState drag, Vector2 position)
        {
            var scroll = drag.Context.Content.GetFirstAncestorOfType<ScrollView>();
            if (scroll == null) return;
            var bounds = scroll.worldBound;
            const float edge = 40f;
            if (position.y < bounds.yMin + edge) scroll.scrollOffset -= new Vector2(0, 12);
            else if (position.y > bounds.yMax - edge) scroll.scrollOffset += new Vector2(0, 12);
        }

        void Drop(DragState drag)
        {
            if (!drag.HasTarget) return;
            if (!drag.IsGroup)
            {
                if (drag.TargetGroup == null) return;
                var ordered = CodexGroups.Items(ItemType, drag.TargetGroup.Id).Where(k => k.Id != drag.Item.Id).ToList();
                ordered.Insert(IndexOf(ordered, drag.Before), drag.Item);
                Reorder(ordered, drag.TargetGroup.Id);
            }
            else
            {
                var ordered = CodexGroups.Groups(GroupType).Where(g => g.Id != drag.Item.Id).ToList();
                ordered.Insert(IndexOf(ordered, drag.Before), drag.Item);
                Reorder(ordered, null);
            }
            Staging.Touch();
        }

        static int IndexOf(List<GameData> list, GameData before)
        {
            if (before == null) return list.Count;
            var index = list.FindIndex(d => d.Id == before.Id);
            return index < 0 ? list.Count : index;
        }

        // 순서를 10 간격으로 다시 매긴다(바뀌는 항목만 작업본을 만든다). group이 있으면 그 그룹으로 옮긴다.
        static void Reorder(List<GameData> ordered, string group)
        {
            for (int i = 0; i < ordered.Count; i++)
            {
                var order = (i + 1) * CodexGroups.OrderStep;
                var item = ordered[i];
                var moving = group != null && CodexGroups.GroupOf(item) != group;
                if (CodexGroups.OrderOf(item) == order && !moving) continue;
                var work = Staging.Work(item);
                if (work is IOrderedData ordering) ordering.Order = order;
                if (moving && work is IGroupedData grouped) grouped.Group = group;
            }
        }

        static void End(DragState drag)
        {
            drag.Source?.RemoveFromClassList("gs-grouped--dragging");
            drag.Ghost?.RemoveFromHierarchy();
            drag.Marker?.RemoveFromHierarchy();
            drag.Ghost = null;
            drag.Marker = null;
            drag.Active = false;
            drag.Item = null;
        }
    }
}
