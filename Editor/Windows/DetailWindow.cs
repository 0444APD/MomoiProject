using System;
using System.Collections.Generic;
using System.Linq;
using GameSystems.Data;
using GameSystems.EditorTools.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameSystems.EditorTools
{
    // 상세 창: 도감 항목의 상세 속성을 따로 띄우는 창(도킹 가능). 위쪽에 탭이 쌓인다(크롬처럼).
    //  · 도감에서 항목을 열면 항목 탭. 이미 열린 항목이면 그 탭으로 간다.
    //  · 항목 안의 클래스·배열·리스트를 누르면 그 값의 탭이 바로 오른쪽에 열린다(안쪽으로 계속 들어갈 수 있다).
    //  · 탭을 닫으면 그 안쪽 탭도 함께 닫힌다.
    // 값은 바로 반영되고(도감의 적용 대기 목록), Unity 적용은 도감에서 탭을 옮길 때 한다.
    public sealed class DetailWindow : EditorWindow
    {
        sealed class Tab
        {
            public string Title;
            public string Caption;
            public Tab Parent;
            public VisualElement Content;
            public GsDetailPanel Panel;
            // 항목 탭: 적용·되돌리기로 작업본이 바뀌면 같은 ID로 다시 연다.
            public Type ItemType;
            public string ItemId;
            public Func<GameData, DetailOptions> Options;
            // 안쪽 탭: 같은 값이면 새로 열지 않고 그 탭으로 간다.
            public object Value;
        }

        readonly List<Tab> _tabs = new List<Tab>();
        Tab _active;
        VisualElement _root;
        VisualElement _bar;
        VisualElement _body;
        bool _rebindQueued;

        [MenuItem("Game Systems/상세", priority = 1)]
        public static void OpenWindow() => Get();

        static DetailWindow Get()
        {
            var window = GetWindow<DetailWindow>();
            window.titleContent = new GUIContent("상세");
            window.minSize = new Vector2(360, 420);
            return window;
        }

        // 도감 항목을 연다. options: 작업본을 받아 패널 설정을 만든다(적용 뒤 다시 열 때도 쓴다).
        public static void OpenItem(GameData item, Func<GameData, DetailOptions> options)
        {
            var window = Get();
            var existing = window._tabs.FirstOrDefault(t => t.Parent == null && t.ItemType == item.GetType() && t.ItemId == item.Id);
            if (existing != null)
            {
                window.Activate(existing);
                return;
            }
            var tab = new Tab { ItemType = item.GetType(), ItemId = item.Id, Options = options };
            window.BuildItem(tab, item);
            window._tabs.Add(tab);
            window.Activate(tab);
        }

        void OnEnable()
        {
            EditorDatabase.Changed += QueueRebind;
        }

        void OnDisable()
        {
            EditorDatabase.Changed -= QueueRebind;
        }

        void CreateGUI() => EnsureRoot();

        // 창의 뿌리(탭 바 + 본문). 창이 그려지기 전에 탭을 열 때도 먼저 만든다.
        void EnsureRoot()
        {
            // 이미 만들어 붙였으면 그대로 쓴다(열린 패널의 대화 상자가 이 뿌리를 쓴다).
            if (_root != null && _root.parent == rootVisualElement) return;
            var host = rootVisualElement;
            host.Clear();
            _root = Gs.Col();
            Gs.Apply(_root);
            _root.AddToClassList("gs-tabs");
            host.Add(_root);
            _bar = Gs.Row("gs-tabs__bar");
            _root.Add(_bar);
            _body = Gs.Col("gs-tabs__body");
            _root.Add(_body);
            RebuildBar();
            ShowActive();
        }

        // ── 탭 만들기 ──────────────────────────────────────────

        void BuildItem(Tab tab, GameData item)
        {
            EnsureRoot();
            var work = EditorDatabase.Staging.Work(item);
            var options = tab.Options(work);
            options.OpenNested = value => OpenNested(tab, value);
            var panel = GsDetailPanel.Create(_root, work, options);
            panel.CloseRequested += () => CloseTab(tab, closePanel: false);
            panel.BodyRebuilt += () => CloseChildren(tab);
            tab.Panel = panel;
            tab.Content = panel;
            tab.Title = item.Id;
            tab.Caption = DataTypes.Find(item.GetType())?.DisplayName;
        }

        bool OpenNested(Tab parent, ExplorerItem value)
        {
            if (value?.Value == null) return false;
            var existing = _tabs.FirstOrDefault(t => t.Parent == parent && ReferenceEquals(t.Value, value.Value));
            if (existing != null)
            {
                Activate(existing);
                return true;
            }
            var tab = new Tab
            {
                Parent = parent,
                Value = value.Value,
                Title = value.Label,
                Caption = DataModel.TypeCaption(value.Type ?? value.Value.GetType()),
            };
            var content = Gs.Col("gs-tabs__nested");
            var head = Gs.Row("gs-tabs__nested-head");
            head.Add(Gs.Label(Path(tab), "gs-title", "gs-grow"));
            head.Add(Gs.Label(tab.Caption ?? "", "gs-caption"));
            content.Add(head);
            var explorer = GsDataExplorer.Browse(value);
            explorer.AddToClassList("gs-explorer--embedded");
            explorer.OpenInTab = inner => OpenNested(tab, inner);
            // 안쪽 값을 고치면 바깥 항목 패널이 반영한다(Apply가 항목 패널까지 이어진다).
            content.Add(explorer);
            tab.Content = content;

            // 부모의 마지막 후손 바로 뒤에 넣는다(크롬처럼 연 탭 옆에 쌓인다).
            var index = _tabs.IndexOf(parent);
            while (index + 1 < _tabs.Count && IsDescendant(_tabs[index + 1], parent)) index++;
            _tabs.Insert(index + 1, tab);
            Activate(tab);
            return true;
        }

        static string Path(Tab tab)
        {
            var names = new List<string>();
            for (var t = tab; t != null; t = t.Parent) names.Add(t.Title);
            names.Reverse();
            return string.Join(" › ", names);
        }

        static bool IsDescendant(Tab tab, Tab ancestor)
        {
            for (var t = tab.Parent; t != null; t = t.Parent)
                if (t == ancestor) return true;
            return false;
        }

        // ── 탭 오가기·닫기 ─────────────────────────────────────

        void Activate(Tab tab)
        {
            _active = tab;
            RebuildBar();
            ShowActive();
        }

        void ShowActive()
        {
            if (_body == null) return;
            _body.Clear();
            if (_active == null)
            {
                var empty = Gs.Box("gs-empty");
                empty.Add(Gs.Label("열린 항목이 없습니다", "gs-title"));
                empty.Add(Gs.Space(8));
                empty.Add(Gs.Label("도감에서 항목을 누르면 여기에 탭으로 열립니다.", "gs-caption"));
                _body.Add(empty);
                return;
            }
            _body.Add(_active.Content);
        }

        void RebuildBar()
        {
            if (_bar == null) return;
            _bar.Clear();
            foreach (var tab in _tabs)
            {
                var t = tab;
                var chip = Gs.Row("gs-tabs__tab");
                chip.EnableInClassList("gs-tabs__tab--active", tab == _active);
                chip.EnableInClassList("gs-tabs__tab--nested", tab.Parent != null);
                var depth = 0;
                for (var p = tab.Parent; p != null; p = p.Parent) depth++;
                if (depth > 0) chip.Add(Gs.Label(new string('›', depth), "gs-tabs__depth"));
                var title = Gs.Label(tab.Title, "gs-tabs__title", "gs-nowrap");
                chip.Add(title);
                chip.tooltip = Path(tab) + (string.IsNullOrEmpty(tab.Caption) ? "" : "\n" + tab.Caption);
                var close = new GsButton("✕", () => CloseTab(t, closePanel: true), GsButtonKind.Ghost);
                close.AddToClassList("gs-tabs__close");
                close.tooltip = "탭 닫기(안쪽 탭도 함께)";
                chip.Add(close);
                chip.RegisterCallback<ClickEvent>(e =>
                {
                    if (e.target is VisualElement v && (v is GsButton || v.GetFirstAncestorOfType<GsButton>() != null)) return;
                    Activate(t);
                });
                // 가운데 버튼 클릭으로도 닫는다(크롬처럼).
                chip.RegisterCallback<PointerUpEvent>(e =>
                {
                    if (e.button == 2) CloseTab(t, closePanel: true);
                });
                _bar.Add(chip);
            }
        }

        void CloseChildren(Tab parent)
        {
            var children = _tabs.Where(t => IsDescendant(t, parent)).ToList();
            if (children.Count == 0) return;
            foreach (var child in children) _tabs.Remove(child);
            if (children.Contains(_active)) _active = parent;
            RebuildBar();
            ShowActive();
        }

        void CloseTab(Tab tab, bool closePanel)
        {
            if (!_tabs.Contains(tab)) return;
            // 항목 탭은 패널의 닫기 절차를 거친다(패널이 CloseRequested로 다시 부른다).
            if (closePanel && tab.Panel != null)
            {
                tab.Panel.Close();
                return;
            }
            var index = _tabs.IndexOf(tab);
            var removed = _tabs.Where(t => t == tab || IsDescendant(t, tab)).ToList();
            foreach (var t in removed) _tabs.Remove(t);
            if (removed.Contains(_active))
                _active = tab.Parent ?? (_tabs.Count == 0 ? null : _tabs[Math.Min(index, _tabs.Count - 1)]);
            RebuildBar();
            ShowActive();
        }

        // ── 적용·되돌리기 뒤 다시 잇기 ─────────────────────────
        // 도감이 변경을 적용하거나 되돌리면 작업본이 사라진다. 같은 ID의 항목으로 탭을 다시 연다(없어졌으면 닫는다).

        void QueueRebind()
        {
            if (_rebindQueued || _root == null) return;
            _rebindQueued = true;
            _root.schedule.Execute(() =>
            {
                _rebindQueued = false;
                Rebind();
            });
        }

        void Rebind()
        {
            var changed = false;
            foreach (var tab in _tabs.Where(t => t.Parent == null).ToList())
            {
                if (tab.Panel != null && tab.Panel.Target != null) continue;
                var item = EditorDatabase.Instance.All.FirstOrDefault(d => d.GetType() == tab.ItemType && d.Id == tab.ItemId);
                foreach (var child in _tabs.Where(t => IsDescendant(t, tab)).ToList()) _tabs.Remove(child);
                if (item == null)
                {
                    _tabs.Remove(tab);
                    if (_active == tab || (_active != null && !_tabs.Contains(_active))) _active = _tabs.FirstOrDefault();
                }
                else
                {
                    var wasActive = _active == tab || (_active != null && !_tabs.Contains(_active));
                    BuildItem(tab, item);
                    if (wasActive) _active = tab;
                }
                changed = true;
            }
            if (!changed) return;
            RebuildBar();
            ShowActive();
        }

        // 제목(ID)을 바꾸면 탭 이름도 바꾼다.
        internal static void Renamed(GameData item, string oldId)
        {
            if (!HasOpenInstances<DetailWindow>()) return;
            var window = GetWindow<DetailWindow>(false, null, false);
            foreach (var tab in window._tabs.Where(t => t.Parent == null && t.ItemType == item.GetType() && t.ItemId == oldId))
            {
                tab.ItemId = item.Id;
                tab.Title = item.Id;
            }
            window.RebuildBar();
        }
    }
}
