using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GameSystems.Data;
using GameSystems.EditorTools.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameSystems.EditorTools
{
    // 키워드 연결 창: 왼쪽은 키워드를 담을 수 있는 필드, 오른쪽은 키워드 그룹. 두 쪽을 함께 보며 연결한다.
    // 필드를 고르고 → 그룹을 연결하거나, 새 그룹과 키워드를 만들어 바로 연결한다. 필드 규칙은 뒤에서 자동으로 만들어진다.
    public sealed class KeywordWindow : EditorWindow
    {
        [SerializeField] string _selected;          // "타입 전체 이름|필드 이름"
        [SerializeField] string _focusGroup;
        [SerializeField] string _fieldSearch = "";
        [SerializeField] string _groupSearch = "";
        [SerializeField] bool _unlinkedOnly;

        VisualElement _root;
        VisualElement _fields;
        VisualElement _selection;
        VisualElement _groups;
        bool _refreshQueued;

        [MenuItem("Game Systems/키워드 연결", priority = 3)]
        public static void OpenMenu() => Open(null);

        public static void Open(string group, Type type = null, string field = null)
        {
            var window = GetWindow<KeywordWindow>();
            window.titleContent = new GUIContent("키워드 연결");
            window.minSize = new Vector2(920, 520);
            if (group != null) window._focusGroup = group;
            if (type != null && field != null) window._selected = type.FullName + "|" + field;
            window.Rebuild();
        }

        void OnEnable()
        {
            EditorDatabase.Changed += QueueRefresh;
            Undo.undoRedoPerformed += QueueRefresh;
        }

        void OnDisable()
        {
            EditorDatabase.Changed -= QueueRefresh;
            Undo.undoRedoPerformed -= QueueRefresh;
        }

        void CreateGUI() => Rebuild();

        void QueueRefresh()
        {
            if (_refreshQueued || _root == null) return;
            _refreshQueued = true;
            _root.schedule.Execute(() =>
            {
                _refreshQueued = false;
                RefreshAll();
            });
        }

        void Rebuild()
        {
            var host = rootVisualElement;
            host.Clear();
            _root = Gs.Col();
            Gs.Apply(_root);
            _root.style.paddingLeft = 24;
            _root.style.paddingRight = 24;
            _root.style.paddingTop = 20;
            host.Add(_root);

            var header = Gs.Row();
            header.style.marginBottom = 14;
            var titles = Gs.Col();
            var titleRow = Gs.Row();
            titleRow.Add(Gs.Label("키워드 연결", "gs-display"));
            var help = HelpUI.Button("window:keywords");
            help.style.marginLeft = 10;
            titleRow.Add(help);
            titles.Add(titleRow);
            var caption = Gs.Label("왼쪽에서 필드를 고르고, 오른쪽에서 키워드 그룹을 연결합니다. string 필드는 하나, string 배열·리스트 필드는 여러 개를 고릅니다.", "gs-caption");
            caption.style.marginTop = 4;
            titles.Add(caption);
            header.Add(titles);
            _root.Add(header);

            var split = Gs.Row("gs-grow");
            split.style.alignItems = Align.Stretch;
            _root.Add(split);

            // 왼쪽: 필드
            var left = Gs.Col("gs-kw__pane");
            left.style.width = new Length(46, LengthUnit.Percent);
            var leftHead = Gs.Row();
            leftHead.Add(Gs.Label("필드", "gs-title"));
            leftHead.Add(Gs.Spacer());
            var unlinked = new Toggle("연결 안 된 것만") { value = _unlinkedOnly };
            unlinked.RegisterValueChangedCallback(e =>
            {
                _unlinkedOnly = e.newValue;
                RebuildFields();
            });
            leftHead.Add(unlinked);
            left.Add(leftHead);
            var fieldSearch = new GsSearchField("클래스·필드 검색") { Value = _fieldSearch };
            fieldSearch.Changed += v =>
            {
                _fieldSearch = v;
                RebuildFields();
            };
            fieldSearch.style.marginTop = 8;
            left.Add(fieldSearch);
            var fieldScroll = new ScrollView(ScrollViewMode.Vertical);
            fieldScroll.style.flexGrow = 1;
            fieldScroll.style.marginTop = 8;
            _fields = fieldScroll.contentContainer;
            left.Add(fieldScroll);
            _selection = Gs.Col("gs-kw__selection");
            left.Add(_selection);
            split.Add(left);

            split.Add(Gs.HSpace(16));

            // 오른쪽: 키워드 그룹
            var right = Gs.Col("gs-kw__pane", "gs-grow");
            var rightHead = Gs.Row();
            rightHead.Add(Gs.Label("키워드 그룹", "gs-title"));
            rightHead.Add(Gs.Spacer());
            rightHead.Add(new GsButton("+ 새 그룹", () => KeywordDialogs.NewGroup(_root, null, id =>
            {
                _focusGroup = id;
                QueueRefresh();
            }, staged: false), GsButtonKind.Ghost));
            right.Add(rightHead);
            var groupSearch = new GsSearchField("그룹·키워드 검색") { Value = _groupSearch };
            groupSearch.Changed += v =>
            {
                _groupSearch = v;
                RebuildGroups();
            };
            groupSearch.style.marginTop = 8;
            right.Add(groupSearch);
            var groupScroll = new ScrollView(ScrollViewMode.Vertical);
            groupScroll.style.flexGrow = 1;
            groupScroll.style.marginTop = 8;
            _groups = groupScroll.contentContainer;
            right.Add(groupScroll);
            split.Add(right);

            RefreshAll();
        }

        void RefreshAll()
        {
            RebuildFields();
            RebuildGroups();
        }

        (Type type, FieldInfo field)? Selected
        {
            get
            {
                if (string.IsNullOrEmpty(_selected)) return null;
                foreach (var entry in KeywordTools.Fields())
                    if (entry.type.FullName + "|" + entry.field.Name == _selected) return entry;
                return null;
            }
        }

        static string LinkText(Type type, FieldInfo field, out bool linked)
        {
            var rule = KeywordTools.RuleOf(type, field);
            var group = KeywordTools.GroupOf(type, field);
            linked = group != null;
            if (group == null)
            {
                if (rule == null || rule.source == FieldRuleSource.Free) return "";
                return rule.source == FieldRuleSource.Data ? "도감 항목에서 고름" : "플레이어 데이터에서 고름";
            }
            return group == "" ? "모든 키워드" : EditorDatabase.Instance.FindById(group)?.DisplayName ?? $"없는 그룹: {group}";
        }

        void RebuildFields()
        {
            if (_fields == null) return;
            _fields.Clear();
            var query = (_fieldSearch ?? "").Trim();
            var entries = KeywordTools.Fields().Where(f =>
            {
                if (_unlinkedOnly && KeywordTools.RuleOf(f.type, f.field) != null) return false;
                if (query.Length == 0) return true;
                return ClassTypes.Label(f.type).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                       f.field.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                       KeywordTools.FieldLabel(f.field).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
            }).ToList();

            foreach (var byType in entries.GroupBy(e => e.type))
            {
                _fields.Add(Gs.Label($"{ClassTypes.GroupOf(byType.Key)} · {ClassTypes.Label(byType.Key)}", "gs-sidebar__group"));
                foreach (var (type, field) in byType)
                {
                    var key = type.FullName + "|" + field.Name;
                    var row = Gs.Row("gs-kw__row");
                    row.EnableInClassList("gs-kw__row--selected", key == _selected);
                    var names = Gs.Col("gs-grow");
                    names.Add(Gs.Label(KeywordTools.FieldLabel(field), "gs-nowrap"));
                    names.Add(Gs.Label(field.Name + (KeywordTools.IsMulti(field) ? "  ·  여러 개" : "  ·  하나"), "gs-caption"));
                    row.Add(names);
                    var link = LinkText(type, field, out var linked);
                    if (!string.IsNullOrEmpty(link)) row.Add(new GsBadge(link, linked ? Gs.PaletteFor("kw:" + KeywordTools.GroupOf(type, field)) : Gs.PaletteFor("other")));
                    row.RegisterCallback<ClickEvent>(_ =>
                    {
                        _selected = key;
                        var group = KeywordTools.GroupOf(type, field);
                        if (!string.IsNullOrEmpty(group)) _focusGroup = group;
                        RefreshAll();
                    });
                    _fields.Add(row);
                }
            }
            if (entries.Count == 0)
                _fields.Add(Gs.Label(_unlinkedOnly ? "연결 안 된 필드가 없습니다." : "키워드를 담을 수 있는 필드(string 또는 string 배열·리스트)가 없습니다.", "gs-caption"));
            RebuildSelection();
        }

        // 왼쪽 아래: 고른 필드와 바로 하기.
        void RebuildSelection()
        {
            _selection.Clear();
            var selected = Selected;
            if (selected == null)
            {
                _selection.Add(Gs.Label("필드를 고르면 여기서 연결 상태를 보고 바꿀 수 있습니다.", "gs-caption"));
                return;
            }
            var (type, field) = selected.Value;
            _selection.Add(Gs.Label($"{ClassTypes.Label(type)} › {KeywordTools.FieldLabel(field)}", "gs-heading"));
            var link = LinkText(type, field, out var linked);
            var state = Gs.Label((KeywordTools.IsMulti(field) ? "여러 개 선택" : "하나 선택") + "  ·  " + (string.IsNullOrEmpty(link) ? "연결 없음(자유 입력)" : link), "gs-caption");
            state.style.marginTop = 2;
            _selection.Add(state);
            var actions = Gs.Row("gs-wrap");
            actions.style.marginTop = 8;
            actions.Add(new GsButton("+ 새 그룹 만들어 연결", () => KeywordDialogs.NewGroup(_root, $"'{KeywordTools.FieldLabel(field)}'에 쓸 새 키워드 그룹", id =>
            {
                KeywordTools.Connect(type, field, id);
                _focusGroup = id;
                QueueRefresh();
            }, staged: false), GsButtonKind.Primary));
            actions.Add(Gs.HSpace(6));
            if (KeywordTools.GroupOf(type, field) != "")
                actions.Add(new GsButton("모든 키워드 허용", () =>
                {
                    KeywordTools.Connect(type, field, "");
                    QueueRefresh();
                }, GsButtonKind.Ghost) { tooltip = "그룹을 정하지 않고 모든 그룹의 키워드에서 고르게 합니다." });
            if (KeywordTools.RuleOf(type, field) != null)
                actions.Add(new GsButton("연결 해제", () =>
                {
                    KeywordTools.Disconnect(type, field);
                    QueueRefresh();
                }, GsButtonKind.Ghost));
            _selection.Add(actions);
        }

        void RebuildGroups()
        {
            if (_groups == null) return;
            _groups.Clear();
            var selected = Selected;
            var current = selected != null ? KeywordTools.GroupOf(selected.Value.type, selected.Value.field) : null;
            var query = (_groupSearch ?? "").Trim();
            var groups = KeywordTools.Groups().Where(g => query.Length == 0 ||
                g.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || g.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                KeywordTools.KeywordsOf(g.Id).Any(k => k.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || k.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();

            if (groups.Count == 0)
            {
                var empty = Gs.Box("gs-empty");
                empty.Add(Gs.Label(query.Length > 0 ? "검색 결과가 없습니다" : "아직 키워드 그룹이 없습니다", "gs-heading"));
                if (query.Length == 0)
                {
                    empty.Add(Gs.Space(8));
                    empty.Add(Gs.Label("'+ 새 그룹'으로 만들거나, 왼쪽에서 필드를 고른 뒤 '+ 새 그룹 만들어 연결'을 누르세요.", "gs-caption"));
                }
                _groups.Add(empty);
            }

            VisualElement focusCard = null;
            foreach (var group in groups)
            {
                var id = group.Id;
                var card = Gs.Col("gs-kw__group");
                var isCurrent = current == id;
                card.EnableInClassList("gs-kw__group--current", isCurrent);
                card.EnableInClassList("gs-kw__group--focus", id == _focusGroup);

                var head = Gs.Row();
                var names = Gs.Col("gs-grow");
                names.Add(Gs.Label(group.DisplayName, "gs-heading"));
                var linkedCount = KeywordTools.FieldsLinkedTo(id).Count();
                names.Add(Gs.Label($"연결된 필드 {linkedCount}개", "gs-caption"));
                head.Add(names);
                if (isCurrent) head.Add(new GsBadge("연결됨", Gs.Success));
                else if (selected != null && EditorDatabase.Staging.IsAdded(group))
                {
                    var pending = new GsBadge("도감에서 적용 대기", Gs.Warning);
                    pending.tooltip = "도감에서 만든 뒤 아직 적용하지 않은 그룹입니다. 도감에서 탭을 옮겨 적용하면 연결할 수 있습니다.";
                    head.Add(pending);
                }
                else if (selected != null)
                {
                    var (type, field) = selected.Value;
                    head.Add(new GsButton("이 필드에 연결", () =>
                    {
                        KeywordTools.Connect(type, field, id);
                        _focusGroup = id;
                        QueueRefresh();
                    }, GsButtonKind.Primary));
                }
                card.Add(head);

                var chips = Gs.Row("gs-wrap");
                chips.style.marginTop = 8;
                foreach (var keyword in KeywordTools.KeywordsOf(id))
                {
                    var chip = new GsBadge(keyword.DisplayName, Gs.PaletteFor("kw:" + id));
                    chip.tooltip = keyword.Id;
                    chip.style.marginRight = 6;
                    chip.style.marginBottom = 6;
                    chips.Add(chip);
                }
                var add = new GsButton("+ 키워드", () => KeywordDialogs.AddKeywords(_root, id, _ => QueueRefresh(), staged: false), GsButtonKind.Ghost);
                chips.Add(add);
                card.Add(chips);

                var tools = Gs.Row();
                tools.Add(Gs.Spacer());
                tools.Add(new GsButton("도감에서 열기", () => DatabaseWindow.ShowItem(group), GsButtonKind.Ghost));
                card.Add(tools);
                _groups.Add(card);
                if (id == _focusGroup) focusCard = card;
            }
            if (focusCard != null) focusCard.schedule.Execute(() => (focusCard.GetFirstAncestorOfType<ScrollView>())?.ScrollTo(focusCard)).StartingIn(30);
        }
    }
}
