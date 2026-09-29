using System;
using System.Collections.Generic;
using System.Linq;
using GameSystems.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameSystems.EditorTools.UI
{
    // 그룹 + 항목을 한 탭에서 다루는 도감 화면(키워드, 스프라이트 규격).
    //  · 그룹 카드 왼쪽의 세로선을 끌어 그룹 순서를 바꾼다.
    //  · 항목 칩을 끌어 순서를 바꾸거나 다른 그룹 카드에 놓아 옮긴다.
    //  · 그룹 이름·항목 칩을 누르면 상세 화면.
    // 모든 변경은 도감의 적용 대기 목록에 모이고, 탭을 옮길 때 적용한다.
    public abstract class GroupedCodexPage : CodexPage
    {
        const float DragThreshold = 5f;

        protected abstract Type GroupType { get; }
        protected abstract Type ItemType { get; }
        // 화면 글자: 그룹·항목을 부르는 말(예: "그룹"·"키워드", "묶음"·"규격").
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

        protected abstract void NewGroup(CodexPageContext context);
        protected abstract void AddItems(CodexPageContext context, GameData group);

        static EditorDatabase Db => EditorDatabase.Instance;
        static CodexStaging Staging => EditorDatabase.Staging;

        public override string CreateLabel => $"+ 새 {GroupNoun}";

        public override void Create(CodexPageContext context) => NewGroup(context);

        public override string Caption(CodexPageContext context)
        {
            var groups = CodexGroups.Groups(GroupType).Count();
            var items = Db.OfType(ItemType).Count();
            return $"{GroupNoun} {groups}개 · {ItemNoun} {items}개  ·  끌어서 순서를 바꾸고, {ItemNoun}를 다른 {GroupNoun}에 놓아 옮깁니다";
        }

        public override void Build(CodexPageContext context)
        {
            var root = context.Content;
            var bar = Gs.Row("gs-grouped__toolbar");
            Toolbar(context, bar);
            if (bar.childCount > 0) root.Add(bar);

            var list = Gs.Col("gs-grouped__list");
            root.Add(list);

            var query = (context.Search ?? "").Trim();
            bool Hit(GameData d) => query.Length == 0 || d.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

            var groups = CodexGroups.Groups(GroupType).ToList();
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
            var orphans = Db.OfType(ItemType).Where(i => groups.All(g => g.Id != CodexGroups.GroupOf(i))).Where(Hit).OrderBy(CodexGroups.OrderOf).ToList();
            if (orphans.Count > 0)
            {
                var card = Gs.Col("gs-grouped__group");
                var head = Gs.Row("gs-grouped__head");
                head.Add(Gs.Label($"{GroupNoun} 없음", "gs-heading", "gs-grow"));
                head.Add(Gs.Label($"{GroupNoun} 카드로 끌어 옮기세요", "gs-caption"));
                card.Add(head);
                var chips = Gs.Row("gs-grouped__chips");
                foreach (var item in orphans) chips.Add(Chip(context, item, drag));
                card.Add(chips);
                list.Add(card);
            }

            if (groups.Count == 0)
            {
                var empty = Gs.Box("gs-empty");
                empty.Add(Gs.Label($"아직 {GroupNoun}이(가) 없습니다", "gs-title"));
                empty.Add(Gs.Space(8));
                empty.Add(Gs.Label(EmptyHint, "gs-caption"));
                empty.Add(Gs.Space(18));
                empty.Add(new GsButton($"+ 첫 {GroupNoun} 만들기", () => NewGroup(context), GsButtonKind.Primary));
                list.Add(empty);
            }
            if (context.Animate) GsMotion.Appear(list);
        }

        (VisualElement card, VisualElement chips) GroupCard(CodexPageContext context, GameData group, int count, List<GameData> items, DragState drag)
        {
            var card = Gs.Row("gs-grouped__group");
            // 왼쪽 세로선: 끌어서 그룹 순서를 바꾸는 손잡이.
            var handle = Gs.Box("gs-grouped__handle");
            handle.tooltip = $"끌어서 {GroupNoun} 순서 바꾸기";
            handle.Add(Gs.Box("gs-grouped__handle-line"));
            card.Add(handle);

            var body = Gs.Col("gs-grow");
            card.Add(body);
            var errors = context.Issues.Count(i => i.Severity == ValidationSeverity.Error && (i.Asset == group || items.Contains(i.Asset as GameData)));

            var head = Gs.Row("gs-grouped__head");
            var names = Gs.Col("gs-grow");
            var title = Gs.Label(group.Id, "gs-heading", "gs-grouped__title");
            title.tooltip = $"눌러서 {GroupNoun} 편집(설명·ID 바꾸기·삭제)";
            title.RegisterCallback<ClickEvent>(_ => context.OpenDetail(group));
            names.Add(title);
            names.Add(Gs.Label(GroupInfo(group, count), "gs-caption"));
            head.Add(names);
            if (errors > 0) head.Add(new GsBadge($"오류 {errors}", Gs.Danger));
            if (Staging.IsPending(group)) head.Add(new GsBadge(Staging.IsAdded(group) ? "새 항목 · 적용 전" : "변경 · 적용 전", Gs.Warning));
            head.Add(new GsButton($"+ {ItemNoun}", () => AddItems(context, group), GsButtonKind.Ghost));
            body.Add(head);

            var chips = Gs.Row("gs-grouped__chips");
            foreach (var item in items) chips.Add(Chip(context, item, drag));
            if (items.Count == 0) chips.Add(Gs.Label($"{ItemNoun}를 여기로 끌어 놓거나 '+ {ItemNoun}'로 추가하세요.", "gs-caption", "gs-grouped__hint"));
            body.Add(chips);

            RegisterDrag(handle, group, card, drag);
            return (card, chips);
        }

        VisualElement Chip(CodexPageContext context, GameData item, DragState drag)
        {
            var chip = Gs.Row("gs-grouped__chip");
            chip.Add(Gs.Label(ItemText(item), "gs-grouped__chip-text"));
            if (context.Issues.Any(i => i.Asset == item && i.Severity == ValidationSeverity.Error)) chip.AddToClassList("gs-grouped__chip--error");
            chip.tooltip = ItemTooltip(item) + "\n눌러서 편집 · 끌어서 옮기기";
            chip.userData = item;
            RegisterDrag(chip, item, chip, drag);
            return chip;
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
                    // 끌지 않고 뗀 것은 누른 것: 항목 칩은 상세 화면을 연다.
                    if (!isGroup) drag.Context.OpenDetail(item);
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
                var chips = hit.chips.Children().Where(c => c.ClassListContains("gs-grouped__chip") && c != drag.Source).ToList();
                var index = chips.Count;
                for (int i = 0; i < chips.Count; i++)
                {
                    var r = chips[i].worldBound;
                    if (position.y < r.yMin || (position.y <= r.yMax && position.x < r.center.x))
                    {
                        index = i;
                        break;
                    }
                }
                drag.HasTarget = true;
                drag.TargetGroup = hit.group;
                drag.Before = index < chips.Count ? chips[index].userData as GameData : null;
                Rect at;
                if (index < chips.Count)
                {
                    var r = chips[index].worldBound;
                    at = new Rect(r.xMin - 5, r.yMin, 2, r.height);
                }
                else if (chips.Count > 0)
                {
                    var r = chips[chips.Count - 1].worldBound;
                    at = new Rect(r.xMax + 3, r.yMin, 2, r.height);
                }
                else
                {
                    var r = hit.chips.worldBound;
                    at = new Rect(r.xMin + 2, r.yMin + 2, 2, 24);
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
