using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameSystems.EditorTools.UI
{
    // 탐색기에 넣을 항목 하나. 실제 값이거나, 하위 항목 목록을 가진 가상 폴더(분류)다.
    public sealed class ExplorerItem
    {
        public string Label;
        public string Caption;
        public object Value;
        public Type Type;
        // 목록 고르기 규칙(ID 참조·enum 등)을 정하는 필드 정보.
        public MemberInfo Member;
        // 이 값을 바꾸는 방법(없으면 읽기 전용).
        public Action<object> Set;
        // 이 항목 안쪽을 고칠 때 거치는 방법(예: 저장소 Modify, 에셋 Undo 기록). 없으면 위 폴더의 방법을 쓴다.
        public Action<Action> Apply;
        // 가상 폴더: 하위 항목(분류 등).
        public Func<IEnumerable<ExplorerItem>> Children;
        public Color? Color;
    }

    // 폴더형 데이터 탐색기. 클래스·컬렉션은 폴더, 값은 파일처럼 한 단계씩 들어가며 본다.
    //  · 고르기(Pick): 타입만 보고 필드 경로를 고른다(조건의 "필드" 칸 등).
    //  · 둘러보기(Browse): 실제 값을 보고, 값 파일을 눌러 바로 고친다. 클래스는 타입별 배치(GsDataView)로 그린다.
    // 어디서 쓰든 같은 모양이라, 인벤토리 모니터링·도감·더미 아이템 확인이 같은 화면이 된다.
    public sealed class GsDataExplorer : VisualElement
    {
        sealed class Crumb
        {
            public string Label;
            public string Path;
            public Type Type;
            public object Value;
            public MemberInfo Member;
            public Action<Action> Apply;
            public Func<IEnumerable<ExplorerItem>> Children;
        }

        internal sealed class Entry
        {
            public string Label;
            public string Caption;
            public string Preview;
            public string Path;
            public Type Type;
            public object Value;
            public MemberInfo Member;
            public bool IsFolder;
            public bool Pickable;
            public Action<object> Set;
            public Action<Action> Apply;
            public Func<IEnumerable<ExplorerItem>> Children;
            public Color Color;
            // 목록 요소 빼기(없으면 뺄 수 없음).
            public Action Remove;
        }

        readonly bool _browse;
        readonly Action<string> _onPick;
        readonly List<Crumb> _trail = new List<Crumb>();
        readonly VisualElement _breadcrumb;
        readonly VisualElement _list;
        readonly GsButton _pickFolder;
        readonly GsButton _viewToggle;

        public event Action Changed;

        GsDataExplorer(bool browse, Crumb root, Action<string> onPick)
        {
            _browse = browse;
            _onPick = onPick;
            AddToClassList("gs-explorer");

            var toolbar = Gs.Row();
            toolbar.AddToClassList("gs-explorer__toolbar");
            var back = new GsButton("←", GoUp, GsButtonKind.Ghost);
            back.tooltip = "위 폴더로";
            toolbar.Add(back);
            _breadcrumb = Gs.Row("gs-grow");
            _breadcrumb.AddToClassList("gs-explorer__crumbs");
            toolbar.Add(_breadcrumb);
            _viewToggle = new GsButton("목록으로", () =>
            {
                _cards = !_cards;
                Refresh();
            }, GsButtonKind.Ghost);
            _viewToggle.tooltip = "클래스 목록을 카드 격자 / 줄 목록으로 바꿔 봅니다.";
            toolbar.Add(_viewToggle);
            if (!browse)
            {
                _pickFolder = new GsButton("이 폴더 선택", () => Pick(_trail[_trail.Count - 1].Path), GsButtonKind.Ghost);
                _pickFolder.tooltip = "컬렉션 자체를 고릅니다(포함·개수 검사용).";
                toolbar.Add(_pickFolder);
            }
            Add(toolbar);

            _list = new ScrollView();
            _list.AddToClassList("gs-explorer__list");
            Add(_list);

            _trail.Add(root);
            Refresh();
        }

        // ── 만들기 ─────────────────────────────────────────────

        // 필드 경로 고르기 창. current가 있으면 그 폴더에서 시작한다.
        public static GsModal Pick(VisualElement host, Type rootType, string rootLabel, string current, Action<string> onPick)
        {
            GsModal modal = null;
            var explorer = new GsDataExplorer(false, new Crumb { Label = rootLabel, Path = "", Type = rootType }, path =>
            {
                modal?.Close();
                onPick(path);
            });
            explorer.OpenPath(current, parentOnly: true);
            modal = GsModal.Show(host, Frame("필드 고르기", "폴더를 열어 들어가고, 값을 누르면 고릅니다.", explorer));
            return modal;
        }

        // 실제 값 둘러보기·고치기. apply: 값을 고치는 동작을 감싸는 방법(예: 저장소 Modify, Undo 기록). null이면 읽기 전용.
        public static GsDataExplorer Browse(string rootLabel, object root, Action<Action> apply) =>
            Browse(new ExplorerItem { Label = rootLabel, Value = root, Type = root?.GetType(), Apply = apply });

        // 가상 폴더(분류)나 값 하나를 뿌리로 둘러보기.
        public static GsDataExplorer Browse(ExplorerItem root) =>
            new GsDataExplorer(true, new Crumb { Label = root.Label, Type = root.Type ?? root.Value?.GetType(), Value = root.Value, Member = root.Member, Apply = root.Apply, Children = root.Children }, null);

        public static GsModal BrowseModal(VisualElement host, string title, object root, Action<Action> apply) =>
            GsModal.Show(host, Frame(title, apply != null ? "값을 누르면 고칠 수 있습니다." : "읽기 전용", Browse(title, root, apply)));

        static VisualElement Frame(string title, string caption, GsDataExplorer explorer)
        {
            var box = Gs.Box("gs-dialog", "gs-explorer-dialog");
            box.Add(Gs.Label(title, "gs-title"));
            if (!string.IsNullOrEmpty(caption))
            {
                var text = Gs.Label(caption, "gs-caption");
                text.style.marginTop = 4;
                box.Add(text);
            }
            explorer.style.marginTop = 12;
            box.Add(explorer);
            return box;
        }

        // ── 이동 ───────────────────────────────────────────────

        // 경로의 폴더를 연다(parentOnly: 마지막 항목은 파일이므로 그 부모까지만).
        public void OpenPath(string path, bool parentOnly)
        {
            if (string.IsNullOrEmpty(path) || _browse) return;
            var parts = path.Split('.');
            var count = parentOnly ? parts.Length - 1 : parts.Length;
            for (int i = 0; i < count; i++)
            {
                var raw = parts[i];
                var many = raw.EndsWith(FieldPaths.Each, StringComparison.Ordinal);
                var name = many ? raw.Substring(0, raw.Length - 2) : raw;
                var current = _trail[_trail.Count - 1];
                var member = FieldPaths.MembersOf(current.Type).FirstOrDefault(m => m.Name == name);
                if (member == null) break;
                var memberPath = Join(current.Path, name);
                _trail.Add(new Crumb { Label = LabelOf(member.Info, name), Path = memberPath, Type = member.Type });
                if (many && member.IsCollection) _trail.Add(new Crumb { Label = "각 항목", Path = memberPath + FieldPaths.Each, Type = member.ElementType });
            }
            Refresh();
        }

        void GoUp()
        {
            if (_trail.Count <= 1) return;
            _trail.RemoveAt(_trail.Count - 1);
            Refresh();
        }

        void Enter(Entry entry)
        {
            var here = _trail[_trail.Count - 1];
            _trail.Add(new Crumb
            {
                Label = entry.Label,
                Path = entry.Path,
                Type = entry.Type,
                Value = entry.Value,
                Member = entry.Member,
                Apply = entry.Apply ?? here.Apply,
                Children = entry.Children,
            });
            Refresh();
        }

        void Pick(string path) => _onPick?.Invoke(path);

        // 값이 바뀌었을 때 현재 폴더를 다시 그린다(위치는 유지).
        public void Refresh()
        {
            _breadcrumb.Clear();
            for (int i = 0; i < _trail.Count; i++)
            {
                var depth = i;
                if (i > 0) _breadcrumb.Add(Gs.Label("›", "gs-explorer__sep"));
                var crumb = Gs.Label(_trail[i].Label, "gs-explorer__crumb");
                crumb.EnableInClassList("gs-explorer__crumb--current", i == _trail.Count - 1);
                crumb.RegisterCallback<ClickEvent>(_ =>
                {
                    _trail.RemoveRange(depth + 1, _trail.Count - depth - 1);
                    Refresh();
                });
                _breadcrumb.Add(crumb);
            }

            var here = _trail[_trail.Count - 1];
            _viewToggle.style.display = _browse && IsClassCollection(here) ? DisplayStyle.Flex : DisplayStyle.None;
            _viewToggle.Text = _cards ? "목록으로" : "카드로";
            if (_pickFolder != null)
                _pickFolder.style.display = here.Path.Length > 0 && FieldPaths.ElementTypeOf(here.Type) != null ? DisplayStyle.Flex : DisplayStyle.None;

            _list.Clear();
            // 둘러보기에서 클래스(컬렉션이 아닌 객체)는 타입별 배치로 그린다(도감·상세 패널·더미 생성기와 같은 모양).
            if (_browse && here.Children == null && here.Value != null && !FieldPaths.IsCollectionValue(here.Value) &&
                DataModel.Of(here.Value.GetType()).Kind == DataKind.Object)
            {
                _list.Add(new GsDataView(new ExplorerContext(this, here)));
                return;
            }
            var entries = _browse ? BrowseEntries(here) : PickEntries(here);
            if (entries.Count == 0)
            {
                _list.Add(Gs.Label(_browse ? "비어 있습니다" : "더 들어갈 항목이 없습니다", "gs-caption", "gs-explorer__empty"));
                return;
            }
            // 클래스 목록은 카드 격자(기본), 값 목록은 시트 줄. 도구줄에서 바꿀 수 있다.
            if (_browse && IsClassCollection(here) && _cards)
            {
                var grid = Gs.Row("gs-explorer__cards");
                foreach (var entry in entries) grid.Add(Card(entry));
                _list.Add(grid);
            }
            else
            {
                foreach (var entry in entries) _list.Add(Row(entry));
            }
            AddListTools(here);
        }

        bool _cards = true;

        static bool IsClassCollection(Crumb here)
        {
            if (here.Children != null || here.Value == null || !FieldPaths.IsCollectionValue(here.Value)) return false;
            var element = FieldPaths.ElementTypeOf(here.Value.GetType());
            return element != null && (DataModel.Of(element).Kind == DataKind.Object || element.IsAbstract || element.IsInterface);
        }

        // 클래스 한 개 = 카드 한 장: 첫 스프라이트 필드를 이미지로, 이름을 제목으로, 앞쪽 값 몇 개를 수치로.
        VisualElement Card(Entry entry)
        {
            var card = new GsCard(CardLayout.Vertical);
            card.AddToClassList("gs-explorer__card");
            var value = entry.Value;
            UnityEngine.Object image = null;
            var stats = new List<(string label, string text)>();
            if (value != null)
            {
                foreach (var member in FieldPaths.MembersOf(value.GetType()))
                {
                    var v = FieldPaths.Read(value, member.Name);
                    if (image == null && v is UnityEngine.Object o && GsDataViewImages.IsImage(o)) image = SpriteSpecEditor.Preview(o, member.Info as FieldInfo);
                    else if (stats.Count < 3 && member.ElementType == null && DataModel.Of(member.Type).Kind != DataKind.Object && !(v is UnityEngine.Object))
                        stats.Add((LabelOf(member.Info, member.Name), ValueChoices.Display(member.Info, v)));
                }
            }
            card.SetImage(image, Gs.PaletteFor(value?.GetType().Name));
            card.Title = value == null ? "(비어 있음)" : DataModel.Summary(value);
            card.Subtitle = entry.Label;
            foreach (var (label, text) in stats) card.AddStat(label, text);
            card.RegisterCallback<ClickEvent>(e =>
            {
                if (e.target is VisualElement target && (target is GsButton || target.GetFirstAncestorOfType<GsButton>() != null)) return;
                if (entry.IsFolder) Enter(entry);
            });
            var apply = ApplyFor(entry);
            if (entry.Remove != null && apply != null)
            {
                var remove = new GsButton("✕", () =>
                {
                    apply(entry.Remove);
                    Changed?.Invoke();
                    Refresh();
                }, GsButtonKind.Ghost);
                remove.AddToClassList("gs-explorer__card-remove");
                remove.tooltip = "목록에서 빼기";
                card.Add(remove);
            }
            card.tooltip = "클릭해서 열기";
            return card;
        }

        // 목록 폴더: 맨 아래 '+ 추가'(요소 형식의 기본값으로). 딕셔너리는 키를 먼저 정한다.
        void AddListTools(Crumb here)
        {
            if (_browse && here.Apply != null && here.Value is IDictionary map)
            {
                AddDictionaryTools(here, map);
                return;
            }
            if (!_browse || here.Apply == null || !(here.Value is IList list) || list.IsFixedSize) return;
            var elementType = FieldPaths.ElementTypeOf(here.Value.GetType());
            if (elementType == null) return;
            var add = new GsButton("+ 추가", () =>
            {
                object item;
                try
                {
                    item = elementType == typeof(string) ? "" : elementType.IsAbstract || elementType.IsInterface ? null : DataModel.Create(elementType);
                }
                catch (Exception)
                {
                    item = null;
                }
                here.Apply(() => list.Add(item));
                Changed?.Invoke();
                Refresh();
            }, GsButtonKind.Ghost);
            add.style.alignSelf = Align.FlexStart;
            add.style.marginTop = 6;
            _list.Add(add);
        }

        // ── 고르기: 타입의 구조 ─────────────────────────────────

        List<Entry> PickEntries(Crumb here)
        {
            var list = new List<Entry>();
            var element = FieldPaths.ElementTypeOf(here.Type);
            if (element != null && here.Path.Length > 0 && !here.Path.EndsWith(FieldPaths.Each, StringComparison.Ordinal))
            {
                list.Add(new Entry
                {
                    Label = "각 항목",
                    Caption = TypeName(element),
                    Path = here.Path + FieldPaths.Each,
                    Type = element,
                    IsFolder = FieldPaths.MembersOf(element).Count > 0 || FieldPaths.ElementTypeOf(element) != null,
                    Pickable = true,
                    Color = Gs.Palette[6],
                });
                return list;
            }
            foreach (var member in FieldPaths.MembersOf(here.Type))
            {
                var nested = member.IsCollection || DataModel.Of(member.Type).Kind == DataKind.Object;
                list.Add(new Entry
                {
                    Label = LabelOf(member.Info, member.Name),
                    Caption = member.IsCollection ? $"목록 · {TypeName(member.ElementType)}" : TypeName(member.Type),
                    Path = Join(here.Path, member.Name),
                    Type = member.Type,
                    Member = member.Info,
                    IsFolder = nested,
                    Pickable = !nested || member.IsCollection,
                    Color = nested ? Gs.Palette[6] : Gs.Palette[8],
                });
            }
            return list;
        }

        // ── 둘러보기: 실제 값 ───────────────────────────────────

        List<Entry> BrowseEntries(Crumb here)
        {
            var list = new List<Entry>();
            if (here.Children != null)
            {
                foreach (var item in here.Children()) list.Add(FromItem(item));
                return list;
            }
            var value = here.Value;
            if (value == null || !FieldPaths.IsCollectionValue(value)) return list;
            var elementType = FieldPaths.ElementTypeOf(value.GetType());
            if (value is IDictionary map)
            {
                // 딕셔너리: 한 요소 = 한 줄([키] 값). 값은 바로 고치고, 요소는 빼거나 새 키로 더한다.
                foreach (DictionaryEntry pair in map)
                {
                    var key = pair.Key;
                    var entry = ValueEntry($"[{ValueChoices.Display(here.Member, key)}]", pair.Value, elementType ?? pair.Value?.GetType(), null, v => map[key] = v, null);
                    entry.Remove = () => map.Remove(key);
                    list.Add(entry);
                }
                return list;
            }
            var ilist = value as IList;
            int index = 0;
            foreach (var item in FieldPaths.Items(value))
            {
                var at = index++;
                // 목록 요소의 선택 규칙은 목록 필드의 규칙을 따른다(예: 태그 목록 → 태그 고르기).
                var entry = ValueEntry($"[{at}]", item, elementType ?? item?.GetType(), here.Member, ilist != null ? v => ilist[at] = v : (Action<object>)null, null);
                if (ilist != null && !ilist.IsFixedSize) entry.Remove = () => ilist.RemoveAt(at);
                list.Add(entry);
            }
            return list;
        }

        Entry FromItem(ExplorerItem item)
        {
            if (item.Children != null)
                return new Entry
                {
                    Label = item.Label,
                    Caption = item.Caption,
                    Preview = "",
                    IsFolder = true,
                    Children = item.Children,
                    Apply = item.Apply,
                    Color = item.Color ?? Gs.Palette[6],
                };
            var entry = ValueEntry(item.Label, item.Value, item.Type ?? item.Value?.GetType(), item.Member, item.Set, item.Apply);
            if (!string.IsNullOrEmpty(item.Caption)) entry.Caption = item.Caption;
            if (item.Color.HasValue) entry.Color = item.Color.Value;
            return entry;
        }

        internal Entry ValueEntry(string label, object value, Type declared, MemberInfo member, Action<object> set, Action<Action> apply)
        {
            var type = value?.GetType() ?? declared;
            var kind = type == null ? DataKind.Scalar : DataModel.Of(type).Kind;
            var folder = value != null && (kind == DataKind.Object || FieldPaths.IsCollectionValue(value));
            return new Entry
            {
                Label = label,
                Caption = type == null ? "" : folder && FieldPaths.IsCollectionValue(value) ? $"목록 · {FieldPaths.Items(value).Count()}개" : TypeName(type),
                Preview = folder ? DataModel.Summary(value) : ValueChoices.Display(member, value),
                Type = type,
                Value = value,
                Member = member,
                IsFolder = folder,
                Set = folder ? null : set,
                Apply = apply,
                Color = folder ? Gs.Palette[6] : Gs.Palette[8],
            };
        }

        Action<Action> ApplyFor(Entry entry) => entry.Apply ?? _trail[_trail.Count - 1].Apply;

        void AddDictionaryTools(Crumb here, IDictionary map)
        {
            var types = here.Value.GetType().GetInterfaces().Concat(new[] { here.Value.GetType() })
                .FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IDictionary<,>))?.GetGenericArguments();
            if (types == null) return;
            var keyType = types[0];
            var valueType = types[1];

            void AddWithKey(object key)
            {
                if (key == null || map.Contains(key)) return;
                object value;
                try
                {
                    value = valueType == typeof(string) ? "" : valueType.IsValueType ? Activator.CreateInstance(valueType) :
                            valueType.IsAbstract || valueType.IsInterface ? null : DataModel.Create(valueType);
                }
                catch (Exception)
                {
                    value = null;
                }
                here.Apply(() => map.Add(key, value));
                Changed?.Invoke();
                Refresh();
            }

            GsButton add = null;
            add = new GsButton("+ 키 추가", () =>
            {
                // 키가 enum이거나 ID 참조면 목록에서 고르고(이미 있는 키는 제외), 아니면 글자로 입력한다.
                var choices = ValueChoices.For(here.Member, keyType);
                if (choices != null)
                {
                    var items = choices.Where(c => !map.Contains(c.Value)).Select(c => new GsDropdownItem(c.Label, () => AddWithKey(c.Value)) { Caption = c.Caption, Group = c.Group }).ToList();
                    if (items.Count == 0) items.Add(new GsDropdownItem("더할 수 있는 키가 없습니다") { Disabled = true });
                    GsDropdown.Show(add, items, placeholder: "키 검색");
                    return;
                }
                var host = panel?.visualTree.Q(className: "gs-root") ?? parent;
                GsDialog.Form(host, "키 추가", $"{StorableTypes.Label(keyType)} 키", new[] { new DialogField { Key = "key", Label = "키", Value = "" } }, "추가",
                    values =>
                    {
                        if (!TryParseKey(values["key"], keyType, out var parsed)) return $"'{values["key"]}'를 {StorableTypes.Label(keyType)}(으)로 읽을 수 없습니다.";
                        return map.Contains(parsed) ? "이미 있는 키입니다." : null;
                    },
                    values =>
                    {
                        if (TryParseKey(values["key"], keyType, out var parsed)) AddWithKey(parsed);
                    });
            }, GsButtonKind.Ghost);
            add.style.alignSelf = Align.FlexStart;
            add.style.marginTop = 6;
            _list.Add(add);
        }

        static bool TryParseKey(string text, Type keyType, out object key)
        {
            key = null;
            try
            {
                if (keyType == typeof(string))
                {
                    if (string.IsNullOrEmpty(text)) return false;
                    key = text;
                }
                else if (keyType.IsEnum) key = Enum.Parse(keyType, text);
                else key = Convert.ChangeType(text, keyType, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ── 한 줄 ──────────────────────────────────────────────

        internal VisualElement Row(Entry entry)
        {
            // 값 하나(폴더가 아닌 줄)는 입력 칸 모양으로. 폴더·고르기 목록은 줄 모양 그대로.
            if (_browse && !entry.IsFolder) return ValueField(entry);
            var row = Gs.Box("gs-explorer__row");
            var icon = Gs.Box(entry.IsFolder ? "gs-explorer__folder" : "gs-explorer__file");
            icon.style.backgroundColor = Gs.WithAlpha(entry.Color, entry.IsFolder ? 0.85f : 0.35f);
            row.Add(icon);
            row.Add(Gs.Label(entry.Label, "gs-explorer__name"));
            row.Add(Gs.Label(entry.Caption ?? "", "gs-caption", "gs-explorer__type"));
            var preview = Gs.Label(entry.Preview ?? "", "gs-grow", "gs-nowrap", "gs-explorer__value");
            row.Add(preview);
            if (entry.IsFolder) row.Add(Gs.Label("›", "gs-explorer__chevron"));

            if (!_browse && entry.Pickable)
            {
                var pick = new GsButton("선택", () => Pick(entry.Path), GsButtonKind.Ghost);
                pick.AddToClassList("gs-explorer__pick");
                row.Add(pick);
            }

            var apply = ApplyFor(entry);
            if (_browse && entry.Remove != null && apply != null)
            {
                var remove = new GsButton("✕", () =>
                {
                    apply(entry.Remove);
                    Changed?.Invoke();
                    Refresh();
                }, GsButtonKind.Ghost);
                remove.AddToClassList("gs-explorer__pick");
                remove.tooltip = "목록에서 빼기";
                row.Add(remove);
            }
            var editable = _browse && !entry.IsFolder && entry.Set != null && apply != null;
            // 에셋 참조(스프라이트·데이터 등): Unity 오브젝트 선택 창을 줄 전체에 덮는다.
            if (editable && DataModel.Of(entry.Type ?? typeof(object)).Kind == DataKind.Reference)
            {
                var picker = new ObjectFieldProxy(entry.Type);
                picker.SetValue(entry.Value as UnityEngine.Object);
                picker.Changed += value =>
                {
                    apply(() => entry.Set(value));
                    Changed?.Invoke();
                    Refresh();
                    var rule = SpriteSpecEditor.RuleFor(entry.Member as FieldInfo);
                    var host = panel?.visualTree.Q(className: "gs-root");
                    if (rule != null && value is Sprite placed && host != null) GsSpriteFraming.Open(host, placed, rule.spriteGroup, Refresh);
                };
                row.Add(picker.Element);
            }
            var spriteRule = _browse ? SpriteSpecEditor.RuleFor(entry.Member as FieldInfo) : null;
            if (spriteRule != null && entry.Value is Sprite framed)
            {
                var frame = new GsButton("맞추기", () =>
                {
                    var host = panel?.visualTree.Q(className: "gs-root");
                    if (host != null) GsSpriteFraming.Open(host, framed, spriteRule.spriteGroup, Refresh);
                }, GsButtonKind.Ghost);
                frame.AddToClassList("gs-explorer__pick");
                row.Add(frame);
            }

            row.RegisterCallback<ClickEvent>(e =>
            {
                // 줄 안의 버튼(선택)은 버튼이 처리한다.
                if (e.target is VisualElement target && (target is GsButton || target.GetFirstAncestorOfType<GsButton>() != null)) return;
                if (entry.IsFolder) Enter(entry);
                else if (!_browse && entry.Pickable) Pick(entry.Path);
                else if (editable) BeginEdit(row, preview, entry, apply);
            });
            if (editable) row.tooltip = "클릭해서 고치기";
            return row;
        }

        // 값 하나를 입력 칸 모양으로: 상자 안 위쪽 이름표, 아래 값(긴 값은 5줄까지, 그 이상은 칸 안에서 스크롤).
        VisualElement ValueField(Entry entry)
        {
            var apply = ApplyFor(entry);
            var editable = entry.Set != null && apply != null;
            var preview = Gs.Label(entry.Preview ?? "", "gs-field__value");
            preview.style.whiteSpace = WhiteSpace.Normal;
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("gs-inline__scroll");
            scroll.style.maxHeight = GsField.MaxValueHeight;
            scroll.style.flexGrow = 1;
            scroll.Add(preview);
            var line = Gs.Row("gs-field__line");
            line.Add(scroll);

            var field = new GsField(entry.Label, line)
            {
                IsEmpty = () => entry.Value == null || (entry.Value is string text && text.Length == 0),
            };
            if (!string.IsNullOrEmpty(entry.Caption)) field.tooltip = entry.Caption;
            field.SetDisabled(!editable);

            if (entry.Remove != null && apply != null)
            {
                var remove = new GsButton("✕", () =>
                {
                    apply(entry.Remove);
                    Changed?.Invoke();
                    Refresh();
                }, GsButtonKind.Ghost);
                remove.tooltip = "목록에서 빼기";
                line.Add(remove);
            }
            // 에셋 참조(스프라이트·데이터 등): Unity 오브젝트 선택 창을 칸 전체에 덮는다.
            if (editable && DataModel.Of(entry.Type ?? typeof(object)).Kind == DataKind.Reference)
            {
                var picker = new ObjectFieldProxy(entry.Type);
                picker.SetValue(entry.Value as UnityEngine.Object);
                picker.Changed += value =>
                {
                    apply(() => entry.Set(value));
                    Changed?.Invoke();
                    Refresh();
                    var rule = SpriteSpecEditor.RuleFor(entry.Member as FieldInfo);
                    var host = panel?.visualTree.Q(className: "gs-root");
                    if (rule != null && value is Sprite placed && host != null) GsSpriteFraming.Open(host, placed, rule.spriteGroup, Refresh);
                };
                field.Box.Add(picker.Element);
            }
            var spriteRule = SpriteSpecEditor.RuleFor(entry.Member as FieldInfo);
            if (spriteRule != null && entry.Value is Sprite framed)
            {
                line.Add(new GsButton("맞추기", () =>
                {
                    var host = panel?.visualTree.Q(className: "gs-root");
                    if (host != null) GsSpriteFraming.Open(host, framed, spriteRule.spriteGroup, Refresh);
                }, GsButtonKind.Ghost));
            }

            if (editable)
            {
                field.Open = () => BeginEdit(field, preview, entry, apply);
                field.Box.RegisterCallback<ClickEvent>(e =>
                {
                    // 비어 있을 때는 칸이 이미 열었고, 칸 안의 버튼은 버튼이 처리한다.
                    if (e.isPropagationStopped) return;
                    if (e.target is VisualElement target && (target is GsButton || target.GetFirstAncestorOfType<GsButton>() != null)) return;
                    BeginEdit(field, preview, entry, apply);
                });
            }
            field.RefreshEmpty();
            return field;
        }

        // 값 고치기: 목록 고르기 규칙(ID 참조·enum) → 목록, 참거짓 → 뒤집기, 나머지 → 글자 입력.
        void BeginEdit(VisualElement row, Label preview, Entry entry, Action<Action> apply)
        {
            var type = Nullable.GetUnderlyingType(entry.Type ?? typeof(string)) ?? entry.Type ?? typeof(string);
            void Commit(object next)
            {
                apply(() => entry.Set(next));
                Changed?.Invoke();
                Refresh();
            }
            if (DataModel.Of(type).Kind == DataKind.Reference) return;
            if (type == typeof(bool))
            {
                Commit(!(entry.Value is bool b && b));
                return;
            }
            var choices = ValueChoices.For(entry.Member, type);
            if (choices != null)
            {
                var items = choices.Select(c => new GsDropdownItem(c.Label, () => Commit(c.Value is Enum || type == typeof(string) ? c.Value : Convert.ChangeType(c.Value, type, CultureInfo.InvariantCulture)))
                {
                    Caption = c.Caption,
                    Group = c.Group,
                    Selected = Equals(c.Value, entry.Value),
                }).ToList();
                var owner = preview.GetFirstAncestorOfType<GsField>();
                owner?.SetActive(true);
                var menu = GsDropdown.Show(owner?.Box ?? (VisualElement)preview, items, placeholder: "검색");
                if (menu != null) menu.Closed += () => owner?.SetActive(false);
                return;
            }
            var host = preview.parent;
            if (host.Q<TextField>() != null) return;
            var field = new TextField { value = entry.Value == null ? "" : Convert.ToString(entry.Value, CultureInfo.InvariantCulture), multiline = type == typeof(string) };
            if (field.multiline)
            {
                // 긴 글은 칸이 늘어나며 5줄까지, 그 이상은 칸 안에서 스크롤(Enter는 확정).
                field.style.whiteSpace = WhiteSpace.Normal;
                field.style.maxHeight = GsField.MaxValueHeight;
                field.verticalScrollerVisibility = ScrollerVisibility.Auto;
            }
            field.AddToClassList("gs-inline");
            field.style.flexGrow = 1;
            preview.style.display = DisplayStyle.None;
            host.Insert(host.IndexOf(preview), field);
            var box = preview.GetFirstAncestorOfType<GsField>();
            box?.SetActive(true);
            void Finish(bool commit)
            {
                if (field.parent == null) return;
                box?.SetActive(false);
                field.RemoveFromHierarchy();
                preview.style.display = DisplayStyle.Flex;
                if (!commit) return;
                try
                {
                    object next = type == typeof(string) ? field.value : Convert.ChangeType(field.value, type, CultureInfo.InvariantCulture);
                    Commit(next);
                }
                catch (Exception)
                {
                    preview.text = $"{field.value} — {StorableTypes.Label(type)}(으)로 읽을 수 없습니다";
                }
            }
            field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Finish(true);
                else if (e.keyCode == KeyCode.Escape) Finish(false);
            }, TrickleDown.TrickleDown);
            field.RegisterCallback<FocusOutEvent>(_ => Finish(true));
            field.schedule.Execute(() =>
            {
                field.Focus();
                field.SelectAll();
            });
        }

        // 탐색기 안의 실제 객체를 배치 블록에 넘겨준다. 폴더(클래스·컬렉션) 줄을 누르면 탐색기 안에서 들어간다.
        sealed class ExplorerContext : DataViewContext
        {
            readonly GsDataExplorer _explorer;
            readonly Crumb _crumb;

            public ExplorerContext(GsDataExplorer explorer, Crumb crumb)
            {
                _explorer = explorer;
                _crumb = crumb;
            }

            object Current => _crumb.Value;
            public override Type Type => Current.GetType();
            public override bool CanEdit => _crumb.Apply != null;
            // 상세 패널 안에 들어간 탐색기는 좁으므로 블록을 한 줄씩 쌓는다.
            public override bool SingleColumn => _explorer.GetFirstAncestorOfType<GsDetailPanel>() != null;
            public override IEnumerable<string> Fields() => FieldPaths.MembersOf(Type).Select(m => m.Name);

            FieldPaths.Member MemberOf(string name) => FieldPaths.MembersOf(Type).FirstOrDefault(m => m.Name == name);

            public override VisualElement FieldRow(string path)
            {
                var member = MemberOf(path);
                if (member == null) return new VisualElement();
                var field = member.Info as FieldInfo;
                var target = Current;
                var current = field != null ? field.GetValue(target) : FieldPaths.Read(target, member.Name);
                Action<object> set = field != null && !field.IsInitOnly ? v => field.SetValue(target, v) : (Action<object>)null;
                var entry = _explorer.ValueEntry(GsDataExplorer.LabelOf(member.Info, member.Name), current, member.Type, member.Info, set, null);
                entry.Path = path;
                return _explorer.Row(entry);
            }

            public override VisualElement FieldValue(string path)
            {
                var value = FieldPaths.Resolve(Current, path).FirstOrDefault();
                return Gs.Label(value != null && (FieldPaths.IsCollectionValue(value) || DataModel.Of(value.GetType()).Kind == DataKind.Object)
                    ? DataModel.Summary(value)
                    : StateStore.Format(value), "gs-nowrap");
            }

            public override UnityEngine.Object ObjectAt(string path) => FieldPaths.Resolve(Current, path).FirstOrDefault() as UnityEngine.Object;

            public override void SetObject(string path, UnityEngine.Object value)
            {
                var member = MemberOf(path);
                if (!(member?.Info is FieldInfo field) || _crumb.Apply == null) return;
                var target = Current;
                _crumb.Apply(() => field.SetValue(target, value));
                _explorer.Changed?.Invoke();
                _explorer.Refresh();
            }
        }

        // ── 이름 ───────────────────────────────────────────────

        static string TypeName(Type type) => type == null ? "" : StorableTypes.Label(type);

        public static string LabelOf(MemberInfo member, string fallback)
        {
            var custom = member?.GetCustomAttribute<FieldLabelAttribute>();
            return custom != null ? custom.Text : UnityEditor.ObjectNames.NicifyVariableName(fallback ?? "");
        }

        // 경로를 사람이 읽는 이름으로: items[].tags → 아이템 › 각 항목 › 태그
        public static string PathLabel(Type root, string path)
        {
            if (string.IsNullOrEmpty(path)) return "(필드 선택)";
            var parts = new List<string>();
            var type = root;
            foreach (var raw in path.Split('.'))
            {
                var many = raw.EndsWith(FieldPaths.Each, StringComparison.Ordinal);
                var name = many ? raw.Substring(0, raw.Length - 2) : raw;
                if (name.Length > 0)
                {
                    var member = type == null ? null : FieldPaths.MembersOf(type).FirstOrDefault(m => m.Name == name);
                    parts.Add(member != null ? LabelOf(member.Info, name) : name);
                    type = member?.Type;
                }
                if (many)
                {
                    parts.Add("각 항목");
                    type = FieldPaths.ElementTypeOf(type);
                }
            }
            return string.Join(" › ", parts);
        }

        static string Join(string path, string name) => string.IsNullOrEmpty(path) ? name : path + "." + name;
    }
}
