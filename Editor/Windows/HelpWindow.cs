using System;
using System.Linq;
using GameSystems.EditorTools.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameSystems.EditorTools
{
    // 도움말 창: 모든 기능의 설명을 분류별로 모아 보여 준다. 각 화면의 "?" 버튼이 해당 항목으로 연다.
    public sealed class HelpWindow : EditorWindow
    {
        [SerializeField] string _topic;
        [SerializeField] string _search = "";

        VisualElement _list;
        VisualElement _page;

        // 메뉴는 도움말 보류 동안 뺀다(HelpFeature).
        public static void Open() => Show(null);

        public static void Show(string topicId)
        {
            var window = GetWindow<HelpWindow>();
            window.titleContent = new GUIContent("도움말");
            window.minSize = new Vector2(760, 480);
            if (!string.IsNullOrEmpty(topicId)) window._topic = topicId;
            window.Rebuild();
        }

        void CreateGUI() => Rebuild();

        void Rebuild()
        {
            var host = rootVisualElement;
            host.Clear();
            var root = Gs.Row();
            Gs.Apply(root);
            root.style.alignItems = Align.Stretch;
            host.Add(root);

            var sidebar = Gs.Col("gs-sidebar");
            sidebar.style.width = 240;
            var search = new GsSearchField("도움말 검색");
            search.Value = _search;
            search.Changed += value =>
            {
                _search = value;
                RebuildList();
            };
            sidebar.Add(search);
            sidebar.Add(Gs.Space(8));
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            _list = scroll.contentContainer;
            sidebar.Add(scroll);
            root.Add(sidebar);

            var pageScroll = new ScrollView(ScrollViewMode.Vertical);
            pageScroll.AddToClassList("gs-grow");
            _page = pageScroll.contentContainer;
            _page.style.paddingLeft = 32;
            _page.style.paddingRight = 32;
            _page.style.paddingTop = 28;
            _page.style.paddingBottom = 28;
            _page.style.maxWidth = 760;
            root.Add(pageScroll);

            if (HelpRegistry.Find(_topic) == null) _topic = HelpIds.Setup;
            RebuildList();
            RebuildPage();
        }

        void RebuildList()
        {
            if (_list == null) return;
            _list.Clear();
            var topics = HelpRegistry.All.Where(Matches).ToList();
            foreach (var group in topics.GroupBy(t => t.Category ?? "기타").OrderBy(g => CategoryOrder(g.Key)))
            {
                _list.Add(Gs.Label(group.Key.ToUpperInvariant(), "gs-sidebar__group"));
                foreach (var topic in group)
                {
                    var id = topic.Id;
                    var item = new GsNavItem(topic.Title, Gs.PaletteFor(group.Key), () => Select(id));
                    item.tooltip = topic.Summary;
                    item.SetActive(id == _topic);
                    _list.Add(item);
                }
            }
            if (topics.Count == 0) _list.Add(Gs.Label("검색 결과가 없습니다.", "gs-caption"));
        }

        // 안내 → 도감 종류 → 모니터 탭 → 블록 순.
        static int CategoryOrder(string category)
        {
            switch (category)
            {
                case "안내": return 0;
                case "도감 종류": return 1;
                case "모니터 탭": return 2;
                case "화면 배치 블록": return 3;
                case "조건 블록": return 4;
                case "필드 조건 블록": return 5;
                case "보상 블록": return 6;
                default: return 10;
            }
        }

        bool Matches(HelpTopic topic)
        {
            if (string.IsNullOrWhiteSpace(_search)) return true;
            return new[] { topic.Title, topic.Summary, topic.Body, topic.Category }
                .Any(text => !string.IsNullOrEmpty(text) && text.IndexOf(_search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
        }

        void Select(string id)
        {
            _topic = id;
            RebuildList();
            RebuildPage();
        }

        void RebuildPage()
        {
            if (_page == null) return;
            _page.Clear();
            var topic = HelpRegistry.Find(_topic);
            if (topic == null) return;
            _page.Add(Gs.Label(topic.Category ?? "", "gs-caption"));
            var title = Gs.Label(topic.Title, "gs-display");
            title.style.marginTop = 4;
            _page.Add(title);
            if (!string.IsNullOrEmpty(topic.Summary))
            {
                var summary = Gs.Label(topic.Summary, "gs-help__summary");
                _page.Add(summary);
            }
            _page.Add(HelpUI.Body(topic.Body));

            if (topic.Id == HelpIds.Setup)
                _page.Add(new GsButton("시작하기 열기", DatabaseWindow.ShowSetup, GsButtonKind.Primary) { style = { alignSelf = Align.FlexStart } });

            var related = (topic.Related ?? Array.Empty<string>()).Select(HelpRegistry.Find).Where(t => t != null).ToList();
            if (related.Count > 0)
            {
                _page.Add(Gs.Space(8));
                _page.Add(Gs.Label("함께 보기", "gs-help__heading"));
                var row = Gs.Row("gs-wrap");
                foreach (var other in related)
                {
                    var id = other.Id;
                    var link = new GsButton(other.Title, () => Select(id), GsButtonKind.Ghost);
                    link.tooltip = other.Summary;
                    link.style.marginRight = 6;
                    link.style.marginBottom = 6;
                    row.Add(link);
                }
                _page.Add(row);
            }
        }
    }
}
