using System;
using System.Collections.Generic;
using System.Linq;
using GameSystems.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameSystems.EditorTools.UI
{
    // 도감의 키워드 탭: 그룹과 키워드를 한 화면에서 만들고, 끌어서 정리한다.
    //  · 그룹 카드의 손잡이(⠿)를 끌어 그룹 순서를 바꾼다.
    //  · 키워드 칩을 끌어 순서를 바꾸거나 다른 그룹 카드에 놓아 옮긴다.
    //  · 그룹 이름·키워드 칩을 누르면 상세 화면(설명·ID 바꾸기·삭제).
    // 모든 변경은 도감의 적용 대기 목록에 모이고, 탭을 옮길 때 적용한다.
    public sealed class KeywordsPage : CodexPage
    {
        public const string PageKey = "keywords";
        public override string Key => PageKey;

        const float DragThreshold = 5f;
        const int OrderStep = 10;

        static EditorDatabase Db => EditorDatabase.Instance;
        static CodexStaging Staging => EditorDatabase.Staging;

        public override string Caption(CodexPageContext context)
        {
            var groups = KeywordTools.Groups().Count();
            var keywords = Db.OfType<KeywordData>().Count();
            return $"그룹 {groups}개 · 키워드 {keywords}개  ·  끌어서 순서를 바꾸고, 키워드를 다른 그룹에 놓아 옮깁니다";
        }

        public override void Create(CodexPageContext context) =>
            KeywordDialogs.NewGroup(context.Host, null, _ => context.Refresh?.Invoke(), staged: true);

        public override void Build(CodexPageContext context)
        {
            var root = context.Content;
            var toolbar = Gs.Row();
            toolbar.style.marginBottom = 12;
            toolbar.Add(new GsButton("+ 새 그룹", () => Create(context), GsButtonKind.Primary));
            toolbar.Add(Gs.HSpace(8));
            var linkWindow = new GsButton("필드에 연결…", () => KeywordWindow.Open(null), GsButtonKind.Ghost);
            linkWindow.tooltip = "키워드 연결 창: 클래스의 string 필드에 키워드 그룹을 연결합니다.";
            toolbar.Add(linkWindow);
            root.Add(toolbar);

            var list = Gs.Col("gs-kwp__list");
            root.Add(list);

            var query = (context.Search ?? "").Trim();
            bool Hit(GameData d) => query.Length == 0 || d.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || d.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

            var groups = KeywordTools.Groups().ToList();
            var cards = new List<(VisualElement card, VisualElement chips, KeywordGroupData group)>();
            var drag = new DragState { Context = context, Cards = cards };

            foreach (var group in groups)
            {
                var keywords = KeywordTools.KeywordsOf(group.Id).ToList();
                if (query.Length > 0 && !Hit(group) && !keywords.Any(Hit)) continue;
                var (card, chips) = GroupCard(context, group, keywords.Where(k => query.Length == 0 || Hit(group) || Hit(k)).ToList(), drag);
                cards.Add((card, chips, group));
                list.Add(card);
            }

            // 그룹이 없는(지워진 그룹을 가리키는) 키워드: 다른 그룹으로 끌어 옮기면 정리된다.
            var orphans = Db.OfType<KeywordData>().Where(k => groups.All(g => g.Id != k.group)).Where(Hit).OrderBy(k => k.order).ToList();
            if (orphans.Count > 0)
            {
                var card = Gs.Col("gs-kwp__group", "gs-kwp__group--orphan");
                var head = Gs.Row("gs-kwp__head");
                head.Add(Gs.Label("그룹 없음", "gs-heading", "gs-grow"));
                head.Add(Gs.Label("그룹 카드로 끌어 옮기세요", "gs-caption"));
                card.Add(head);
                var chips = Gs.Row("gs-kwp__chips");
                foreach (var keyword in orphans) chips.Add(Chip(context, keyword, drag, true));
                card.Add(chips);
                list.Add(card);
            }

            if (groups.Count == 0)
            {
                var empty = Gs.Box("gs-empty");
                empty.Add(Gs.Label("아직 키워드 그룹이 없습니다", "gs-title"));
                empty.Add(Gs.Space(8));
                empty.Add(Gs.Label("그룹은 키워드의 부모입니다. 예: 등급(일반·레어·전설), 장비 태그(근접·원거리·도트).", "gs-caption"));
                empty.Add(Gs.Space(18));
                empty.Add(new GsButton("+ 첫 그룹 만들기", () => Create(context), GsButtonKind.Primary));
                list.Add(empty);
            }
            if (context.Animate) GsMotion.Appear(list);
        }

        (VisualElement card, VisualElement chips) GroupCard(CodexPageContext context, KeywordGroupData group, List<KeywordData> keywords, DragState drag)
        {
            var card = Gs.Col("gs-kwp__group");
            if (Staging.IsPending(group)) card.AddToClassList("gs-kwp__group--pending");
            var errors = context.Issues.Count(i => i.Severity == ValidationSeverity.Error && (i.Asset == group || keywords.Contains(i.Asset as KeywordData)));

            var head = Gs.Row("gs-kwp__head");
            var handle = Gs.Label("⠿", "gs-kwp__handle");
            handle.tooltip = "끌어서 그룹 순서 바꾸기";
            head.Add(handle);
            var names = Gs.Col("gs-grow");
            var title = Gs.Label(group.DisplayName, "gs-heading", "gs-kwp__title");
            title.tooltip = "눌러서 그룹 편집(이름·설명·ID·삭제)";
            title.RegisterCallback<ClickEvent>(_ => context.OpenDetail(group));
            names.Add(title);
            var linked = KeywordTools.FieldsLinkedTo(group.Id).Count();
            names.Add(Gs.Label($"{group.Id}  ·  키워드 {keywords.Count}개  ·  연결된 필드 {linked}개", "gs-caption"));
            head.Add(names);
            if (errors > 0) head.Add(new GsBadge($"오류 {errors}", Gs.Danger));
            if (Staging.IsPending(group)) head.Add(new GsBadge(Staging.IsAdded(group) ? "새 그룹 · 적용 전" : "변경 · 적용 전", Gs.Warning));
            head.Add(new GsButton("+ 키워드", () => KeywordDialogs.AddKeywords(context.Host, group.Id, _ => context.Refresh?.Invoke(), staged: true), GsButtonKind.Ghost));
            card.Add(head);

            var chips = Gs.Row("gs-kwp__chips");
            foreach (var keyword in keywords) chips.Add(Chip(context, keyword, drag, false));
            if (keywords.Count == 0) chips.Add(Gs.Label("키워드를 여기로 끌어 놓거나 '+ 키워드'로 추가하세요.", "gs-caption", "gs-kwp__hint"));
            card.Add(chips);

            RegisterDrag(handle, group, card, drag);
            return (card, chips);
        }

        VisualElement Chip(CodexPageContext context, KeywordData keyword, DragState drag, bool orphan)
        {
            var chip = Gs.Row("gs-kwp__chip");
            chip.style.backgroundColor = Gs.WithAlpha(Gs.PaletteFor("kw:" + keyword.group), 0.22f);
            chip.Add(Gs.Label(keyword.DisplayName, "gs-kwp__chip-name"));
            if (Staging.IsPending(keyword)) chip.AddToClassList("gs-kwp__chip--pending");
            if (context.Issues.Any(i => i.Asset == keyword && i.Severity == ValidationSeverity.Error)) chip.AddToClassList("gs-kwp__chip--error");
            chip.tooltip = $"{keyword.Id}" + (string.IsNullOrEmpty(keyword.description) ? "" : "\n" + keyword.description) + "\n눌러서 편집 · 끌어서 옮기기";
            chip.userData = keyword;
            RegisterDrag(chip, keyword, chip, drag);
            return chip;
        }

        // ── 끌어 놓기 ───────────────────────────────────────────

        sealed class DragState
        {
            public CodexPageContext Context;
            public List<(VisualElement card, VisualElement chips, KeywordGroupData group)> Cards;
            public GameData Item;
            public VisualElement Source;
            public Vector2 Start;
            public bool Active;
            public VisualElement Ghost;
            public VisualElement Marker;
            // 놓을 곳: 키워드면 (그룹, 칩 위치), 그룹이면 (카드 위치).
            public KeywordGroupData TargetGroup;
            public int TargetIndex = -1;
            // 이 항목 앞에 놓는다(null이면 맨 뒤). 검색으로 일부만 보일 때도 전체 순서에서 자리를 찾는다.
            public GameData Before;
        }

        void RegisterDrag(VisualElement handle, GameData item, VisualElement source, DragState drag)
        {
            handle.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                drag.Item = item;
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
                    Begin(drag);
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
                    // 끌지 않고 뗀 것은 누른 것: 키워드 칩은 상세 화면을 연다.
                    if (item is KeywordData) drag.Context.OpenDetail(item);
                }
            });
            handle.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                if (drag.Item == item && drag.Active) End(drag);
            });
        }

        static void Begin(DragState drag)
        {
            drag.Active = true;
            drag.Source.AddToClassList("gs-kwp--dragging");
            var host = drag.Context.Host;
            drag.Ghost = Gs.Label(drag.Item.DisplayName, "gs-kwp__ghost");
            drag.Ghost.pickingMode = PickingMode.Ignore;
            drag.Ghost.style.position = Position.Absolute;
            host.Add(drag.Ghost);
            drag.Marker = Gs.Box(drag.Item is KeywordData ? "gs-kwp__marker" : "gs-kwp__marker-row");
            drag.Marker.pickingMode = PickingMode.Ignore;
            drag.Marker.style.position = Position.Absolute;
            drag.Marker.style.display = DisplayStyle.None;
            host.Add(drag.Marker);
        }

        static void Move(DragState drag, Vector2 position)
        {
            var host = drag.Context.Host;
            var origin = host.worldBound.position;
            drag.Ghost.style.left = position.x - origin.x + 12;
            drag.Ghost.style.top = position.y - origin.y + 10;
            AutoScroll(drag, position);

            drag.TargetGroup = null;
            drag.TargetIndex = -1;
            drag.Before = null;
            drag.Marker.style.display = DisplayStyle.None;
            if (drag.Cards.Count == 0) return;

            if (drag.Item is KeywordData)
            {
                var hit = drag.Cards.FirstOrDefault(c => c.card.worldBound.Contains(position));
                if (hit.card == null) return;
                var chips = hit.chips.Children().Where(c => c.ClassListContains("gs-kwp__chip") && c != drag.Source).ToList();
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
                drag.TargetGroup = hit.group;
                drag.TargetIndex = index;
                drag.Before = index < chips.Count ? chips[index].userData as GameData : null;
                Rect at;
                if (index < chips.Count)
                {
                    var r = chips[index].worldBound;
                    at = new Rect(r.xMin - 5, r.yMin, 3, r.height);
                }
                else if (chips.Count > 0)
                {
                    var r = chips[chips.Count - 1].worldBound;
                    at = new Rect(r.xMax + 3, r.yMin, 3, r.height);
                }
                else
                {
                    var r = hit.chips.worldBound;
                    at = new Rect(r.xMin + 2, r.yMin + 2, 3, 24);
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
            drag.TargetIndex = slot;
            drag.Before = slot < cards.Count ? cards[slot].group : null;
            var list = drag.Cards[0].card.parent.worldBound;
            float y;
            if (slot < cards.Count) y = cards[slot].card.worldBound.yMin - 5;
            else y = cards.Count > 0 ? cards[cards.Count - 1].card.worldBound.yMax + 2 : list.yMin;
            Place(drag.Marker, new Rect(list.xMin, y, list.width, 3), origin);
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

        static void Drop(DragState drag)
        {
            if (drag.TargetIndex < 0) return;
            if (drag.Item is KeywordData keyword)
            {
                var target = drag.TargetGroup;
                if (target == null) return;
                var ordered = KeywordTools.KeywordsOf(target.Id).Where(k => k.Id != keyword.Id).Cast<GameData>().ToList();
                ordered.Insert(IndexOf(ordered, drag.Before), keyword);
                Reorder(ordered, target.Id);
            }
            else if (drag.Item is KeywordGroupData group)
            {
                var ordered = KeywordTools.Groups().Where(g => g.Id != group.Id).Cast<GameData>().ToList();
                ordered.Insert(IndexOf(ordered, drag.Before), group);
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

        // 순서를 10 간격으로 다시 매긴다(바뀌는 항목만 작업본을 만든다). group이 있으면 키워드를 그 그룹으로 옮긴다.
        static void Reorder(List<GameData> ordered, string group)
        {
            for (int i = 0; i < ordered.Count; i++)
            {
                var order = (i + 1) * OrderStep;
                switch (ordered[i])
                {
                    case KeywordData k when k.order != order || (group != null && k.group != group):
                        var wk = (KeywordData)Staging.Work(k);
                        wk.order = order;
                        if (group != null) wk.group = group;
                        break;
                    case KeywordGroupData g when g.order != order:
                        ((KeywordGroupData)Staging.Work(g)).order = order;
                        break;
                }
            }
        }

        static void End(DragState drag)
        {
            drag.Source?.RemoveFromClassList("gs-kwp--dragging");
            drag.Ghost?.RemoveFromHierarchy();
            drag.Marker?.RemoveFromHierarchy();
            drag.Ghost = null;
            drag.Marker = null;
            drag.Active = false;
            drag.Item = null;
        }
    }
}
