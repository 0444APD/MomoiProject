using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace GBible.EditorTools.UI
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
        // 따로 연 탭(상세 창의 안쪽 탭)이 바깥 탐색기의 규칙을 그대로 잇는 데 쓴다:
        //   SerializedProperty 경로(바깥 탐색기 뿌리 기준, 모르면 null) · 인스턴스 편집 규칙 · 블록 편집기.
        public string Serialized;
        public bool InstanceEditing;
        public Func<string, VisualElement> BlockEditor;
    }

    // 폴더형 데이터 탐색기. 클래스·컬렉션은 폴더, 값은 파일처럼 한 단계씩 들어가며 본다.
    //  · 고르기(Pick): 타입만 보고 필드 경로를 고른다(조건의 "필드" 칸 등).
    //  · 둘러보기(Browse): 실제 값을 보고, 값 파일을 눌러 바로 고친다. 클래스는 타입별 배치(GsDataView)로 그린다.
    // 어디서 쓰든 같은 모양이라, 인벤토리 모니터링·DB·더미 아이템 확인이 같은 화면이 된다.
    public sealed class GsDataExplorer : VisualElement
    {
        sealed class Crumb
        {
            public string Label;
            public string Path;
            // SerializedProperty 경로(뿌리 기준, 모르면 null): 블록 편집기를 붙일 때 쓴다.
            public string Serialized;
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
            public string Serialized;
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

        // 폴더(클래스·배열·리스트)를 이 탐색기 안에서 들어가지 않고 따로 연다(상세 창의 새 탭). true를 돌려주면 안에서 들어가지 않는다.
        public Func<ExplorerItem, bool> OpenInTab;

        // 인스턴스 편집(DB 상세): [Hide] 필드는 감추고 [ShowIf]는 조건대로, 기능 필드(비어 있을 수 있는 [SerializeReference])는
        // 붙어 있으면 '빼기', 비어 있으면 '+ 기능'으로 붙인다. 모니터처럼 실제 값을 살피는 곳에서는 끈다(모든 필드를 보인다).
        public bool InstanceEditing;

        // 블록 필드(트리거·결과 …)의 편집기를 만든다(인자 = 뿌리 기준 SerializedProperty 경로). 상세 패널이 넘겨준다.
        public Func<string, VisualElement> BlockEditor;

        // configure: 처음 그리기 전에 정할 것(인스턴스 편집·블록 편집기 — 첫 화면부터 그 규칙대로 그린다).
        GsDataExplorer(bool browse, Crumb root, Action<string> onPick, Action<GsDataExplorer> configure = null)
        {
            _browse = browse;
            _onPick = onPick;
            configure?.Invoke(this);
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

        // 가상 폴더(분류)나 값 하나를 뿌리로 둘러보기. 안쪽 탭으로 연 값이면 바깥 탐색기의 인스턴스 편집 규칙·블록 편집기를 잇는다.
        public static GsDataExplorer Browse(ExplorerItem root)
        {
            var crumb = new Crumb
            {
                Label = root.Label, Serialized = root.Serialized ?? "", Type = root.Type ?? root.Value?.GetType(), Value = root.Value, Member = root.Member,
                Apply = root.Apply, Children = root.Children,
            };
            return new GsDataExplorer(true, crumb, null, explorer =>
            {
                explorer.InstanceEditing = root.InstanceEditing;
                explorer.BlockEditor = root.BlockEditor;
            });
        }

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
            // 따로 연 탭도 같은 규칙: [Hide]·[ShowIf]·'+ 기능', 값 안의 트리거·결과는 DB 편집기(결과 칸 잠금·[BindTo]·구조 잠금).
            // 경로를 모르는 값(딕셔너리 요소 등)이면 블록 편집기를 잇지 않는다(탐색기로 그린다).
            if (_browse && OpenInTab != null && OpenInTab(new ExplorerItem
            {
                Label = entry.Label,
                Caption = entry.Caption,
                Value = entry.Value,
                Type = entry.Type,
                Member = entry.Member,
                Apply = entry.Apply ?? here.Apply,
                Children = entry.Children,
                Serialized = entry.Serialized,
                InstanceEditing = InstanceEditing,
                BlockEditor = entry.Serialized != null ? BlockEditor : null,
            }))
                return;
            _trail.Add(new Crumb
            {
                Label = entry.Label,
                Path = entry.Path,
                Serialized = entry.Serialized,
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
            // 둘러보기에서 클래스(컬렉션이 아닌 객체)는 타입별 배치로 그린다(DB·상세 패널·더미 생성기와 같은 모양).
            if (_browse && here.Children == null && here.Value != null && !FieldPaths.IsCollectionValue(here.Value) &&
                DataModel.Of(here.Value.GetType()).Kind == DataKind.Object)
            {
                _list.Add(new GsDataView(new ExplorerContext(this, here)));
                if (InstanceEditing && here.Apply != null) AddFeatureTools(here);
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
                remove.tooltip = "빼기";
                card.Add(remove);
            }
            card.tooltip = "클릭해서 열기";
            return card;
        }

        // 구조 잠금(EditorPolicy): DB 값 편집에서 Block·규칙 줄(TriggerRule)처럼 로직이 든 요소는 AI가 더하고 뺀다.
        // 데이터 목록의 줄은 개발자가 더하고 뺀다. 모니터처럼 실제 값을 살피는 곳(인스턴스 편집이 아님)은 잠그지 않는다.
        bool StructureLocked(Type elementType) => InstanceEditing && !EditorPolicy.CanEditStructure && EditorPolicy.IsStructural(elementType);

        // 목록 폴더: 맨 아래 '+ 추가'(요소 형식의 기본값으로, 추상 형식이면 만들 형식을 고른다). 딕셔너리는 키를 먼저 정한다.
        void AddListTools(Crumb here)
        {
            if (_browse && here.Apply != null && here.Value is IDictionary map)
            {
                if (!StructureLocked(FieldPaths.ElementTypeOf(map.GetType()))) AddDictionaryTools(here, map);
                return;
            }
            if (!_browse || here.Apply == null || !(here.Value is IList list) || list.IsFixedSize) return;
            var elementType = FieldPaths.ElementTypeOf(here.Value.GetType());
            if (elementType == null || StructureLocked(elementType)) return;

            void AddItem(object item)
            {
                here.Apply(() => list.Add(item));
                Changed?.Invoke();
                Refresh();
            }

            GsButton add = null;
            add = new GsButton("+ 추가", () =>
            {
                // 추상 형식(관리 참조 목록): 빈 칸(null)을 넣지 않고 만들 형식을 고른다.
                if (elementType.IsAbstract || elementType.IsInterface)
                {
                    var items = InstanceFields.Choices(elementType).Select(t =>
                    {
                        var picked = t;
                        return new GsDropdownItem(BlockTypes.DisplayName(picked), () => AddItem(Activator.CreateInstance(picked)))
                        {
                            Caption = TypeName(picked),
                            Detail = BlockTypes.Description(picked),
                        };
                    }).ToList();
                    if (items.Count == 0) items.Add(new GsDropdownItem("만들 수 있는 형식이 없습니다") { Disabled = true });
                    GsDropdown.Show(add, items, placeholder: "형식 검색");
                    return;
                }
                object item;
                try
                {
                    item = elementType == typeof(string) ? "" : DataModel.Create(elementType);
                }
                catch (Exception)
                {
                    item = null;
                }
                AddItem(item);
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
                    Caption = member.IsCollection ? DataModel.TypeCaption(member.Type) : TypeName(member.Type),
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
            // 구조 잠금: 로직이 든 요소(Block·규칙 줄)는 빼지 않는다(값은 그대로 고친다).
            var locked = StructureLocked(elementType);
            if (value is IDictionary map)
            {
                // 딕셔너리: 한 요소 = 한 줄([키] 값). 값은 바로 고치고, 요소는 빼거나 새 키로 더한다.
                foreach (DictionaryEntry pair in map)
                {
                    var key = pair.Key;
                    var entry = ValueEntry($"[{ValueChoices.Display(here.Member, key)}]", pair.Value, elementType ?? pair.Value?.GetType(), null, v => map[key] = v, null);
                    if (!locked) entry.Remove = () => map.Remove(key);
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
                // 뿌리가 목록 자신이면(가중치 표의 뺄 목록 등) 경로는 "Array.data[i]"(앞에 점 없이).
                if (ilist != null && here.Serialized != null) entry.Serialized = Join(here.Serialized, $"Array.data[{at}]");
                if (ilist != null && !ilist.IsFixedSize && !locked) entry.Remove = () => ilist.RemoveAt(at);
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
                Caption = type == null ? "" : folder && FieldPaths.IsCollectionValue(value) ? $"{DataModel.TypeCaption(type)} · {FieldPaths.Items(value).Count()}개" : TypeName(type),
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
                    var items = choices.Where(c => !map.Contains(c.Value) && !(c.Value is string text && text.Length == 0))
                        .Select(c => new GsDropdownItem(c.Label, () => AddWithKey(c.Value)) { Caption = c.Caption, Group = c.Group }).ToList();
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
            row.Add(Gs.Label(entry.Preview ?? "", "gs-grow", "gs-nowrap", "gs-explorer__value"));
            if (entry.IsFolder) row.Add(Gs.Label("›", "gs-explorer__chevron"));

            if (!_browse && entry.Pickable)
            {
                var pick = new GsButton("선택", () => Pick(entry.Path), GsButtonKind.Ghost);
                pick.AddToClassList("gs-explorer__pick");
                row.Add(pick);
            }

            // 배열·리스트 안의 클래스 요소(폴더)는 빼기 버튼을 단다.
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
                remove.tooltip = "빼기";
                row.Add(remove);
            }

            row.RegisterCallback<ClickEvent>(e =>
            {
                // 줄 안의 버튼(선택·빼기)은 버튼이 처리한다.
                if (e.target is VisualElement target && (target is GsButton || target.GetFirstAncestorOfType<GsButton>() != null)) return;
                if (entry.IsFolder) Enter(entry);
                else if (!_browse && entry.Pickable) Pick(entry.Path);
            });
            return row;
        }

        // 값 하나를 입력 칸 모양으로: 상자 안 위쪽 이름표, 아래 값(긴 값은 10줄까지, 그 이상은 칸 안에서 스크롤).
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
            // 마우스를 올리면 형식과 설명([FieldLabel]).
            var hint = GsPropertyEditor.HintOf(entry.Member);
            field.tooltip = string.IsNullOrEmpty(hint) ? entry.Caption : $"{entry.Caption}\n{hint}";
            field.SetDisabled(!editable);

            if (entry.Remove != null && apply != null)
            {
                var remove = new GsButton("✕", () =>
                {
                    apply(entry.Remove);
                    Changed?.Invoke();
                    Refresh();
                }, GsButtonKind.Ghost);
                remove.tooltip = "빼기";
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
                    if (rule != null && value is Sprite placed && host != null) GsSpriteFraming.Open(host, placed, rule.spriteFrame, Refresh);
                };
                field.Box.Add(picker.Element);
            }
            // 색: 왼쪽 둥근 정사각형 미리보기 + 16진수. 칸을 누르면 색 선택기가 바로 열린다.
            if (entry.Value is Color || entry.Type == typeof(Color))
            {
                var current = entry.Value is Color c ? c : Color.white;
                var swatch = Gs.Box("gs-inline__swatch");
                swatch.style.backgroundColor = current;
                line.Insert(0, swatch);
                preview.text = "#" + (current.a < 1f ? ColorUtility.ToHtmlStringRGBA(current) : ColorUtility.ToHtmlStringRGB(current));
                if (editable)
                {
                    var picker = new UnityEditor.UIElements.ColorField { value = current, showEyeDropper = false };
                    picker.AddToClassList("gs-inline__color-picker");
                    picker.RegisterValueChangedCallback(e =>
                    {
                        apply(() => entry.Set(e.newValue));
                        Changed?.Invoke();
                        Refresh();
                    });
                    field.Box.Add(picker);
                }
            }
            var spriteRule = SpriteSpecEditor.RuleFor(entry.Member as FieldInfo);
            if (spriteRule != null && entry.Value is Sprite framed)
            {
                line.Add(new GsButton("맞추기", () =>
                {
                    var host = panel?.visualTree.Q(className: "gs-root");
                    if (host != null) GsSpriteFraming.Open(host, framed, spriteRule.spriteFrame, Refresh);
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
            // 오브젝트·색은 칸을 덮은 Unity 선택기가 받는다.
            if (DataModel.Of(type).Kind == DataKind.Reference || type == typeof(Color)) return;
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
                var menu = GsDropdown.Show(owner?.Box ?? (VisualElement)preview, items, placeholder: "검색");
                owner?.TrackMenu(menu);
                return;
            }
            var host = preview.parent;
            if (host.Q<TextField>() != null) return;
            var field = new TextField { value = entry.Value == null ? "" : Convert.ToString(entry.Value, CultureInfo.InvariantCulture), multiline = type == typeof(string) };
            if (field.multiline)
            {
                // 긴 글은 칸이 늘어나며 10줄까지, 그 이상은 칸 안에서 스크롤(Enter는 확정).
                field.style.whiteSpace = WhiteSpace.Normal;
                field.style.maxHeight = GsField.MaxValueHeight;
                field.verticalScrollerVisibility = ScrollerVisibility.Auto;
            }
            field.AddToClassList("gs-inline");
            field.style.flexGrow = 1;
            preview.style.display = DisplayStyle.None;
            host.Insert(host.IndexOf(preview), field);
            var box = preview.GetFirstAncestorOfType<GsField>();
            box?.SetEditing(true);
            void Finish(bool commit)
            {
                if (field.parent == null) return;
                box?.SetEditing(false);
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
            // 인스턴스 편집: [Hide]는 감추고 [ShowIf]는 조건대로(ShowIfRules — 값을 바꾸면 탐색기를 다시 그려 다시 판정한다).
            public override IEnumerable<string> Fields() =>
                FieldPaths.MembersOf(Type).Where(m => !_explorer.InstanceEditing || ShowIfRules.Visible(Current, m.Info as FieldInfo)).Select(m => m.Name);

            FieldPaths.Member MemberOf(string name) => FieldPaths.MembersOf(Type).FirstOrDefault(m => m.Name == name);

            public override VisualElement FieldRow(string path)
            {
                var member = MemberOf(path);
                if (member == null) return new VisualElement();
                var field = member.Info as FieldInfo;
                var target = Current;
                var serialized = _crumb.Serialized == null || field == null ? null : Join(_crumb.Serialized, field.Name);
                // 트리거·결과 같은 블록 필드는 DB와 같은 편집기로(상세 패널이 넘겨준 BlockEditor).
                if (serialized != null && _explorer.BlockEditor != null && IsBlockField(field) && _explorer.BlockEditor(serialized) is { } editor)
                {
                    var section = Gs.Col("gs-explorer__block");
                    section.style.marginTop = 6;
                    section.Add(Gs.Label(GsDataExplorer.LabelOf(member.Info, member.Name), "gs-detail__label"));
                    section.Add(editor);
                    return section;
                }
                // 옆 칸·위쪽 칸을 읽어 목록을 만드는 칸([FieldPath]의 뿌리 이벤트·플레이어 데이터, [Keyword] 그룹, 비교·값 …)도 DB와 같은 값 칸으로.
                // 키워드 목록(List<string>)은 DB와 같은 칩.
                if (serialized != null && _explorer.BlockEditor != null && NeedsProperty(field) && _explorer.BlockEditor(serialized) is { } same)
                {
                    if (!(same is GsInlineProperty inline))
                    {
                        var chips = Gs.Col("gs-explorer__block");
                        chips.style.marginTop = 6;
                        chips.Add(Gs.Label(GsDataExplorer.LabelOf(member.Info, member.Name), "gs-detail__label"));
                        chips.tooltip = GsPropertyEditor.HintOf(member.Info);
                        chips.Add(same);
                        return chips;
                    }
                    var box = GsField.For(GsDataExplorer.LabelOf(member.Info, member.Name), inline);
                    box.tooltip = GsPropertyEditor.HintOf(member.Info);
                    // 다른 칸의 [ShowIf] 스위치면(키워드 '종류' 등) 바꾼 뒤 탐색기를 다시 그려 다시 판정한다.
                    if (_explorer.InstanceEditing && ShowIfRules.IsSwitch(field))
                        inline.Changed += () => _explorer.schedule.Execute(_explorer.Refresh);
                    return box;
                }
                var current = field != null ? field.GetValue(target) : FieldPaths.Read(target, member.Name);
                Action<object> set = field != null && !field.IsInitOnly ? v => field.SetValue(target, v) : (Action<object>)null;
                var entry = _explorer.ValueEntry(GsDataExplorer.LabelOf(member.Info, member.Name), current, member.Type, member.Info, set, null);
                entry.Path = path;
                entry.Serialized = serialized;
                // 기능 필드를 떼는 것은 구조(EditorPolicy) — AI가 한다.
                if (_explorer.InstanceEditing && EditorPolicy.CanEditStructure && current != null && field != null && InstanceFields.IsFeature(field) && _crumb.Apply != null)
                {
                    var apply = _crumb.Apply;
                    entry.Remove = () => apply(() => field.SetValue(target, null));
                }
                return _explorer.Row(entry);
            }

            // 트리거·결과·조건 같은 블록(또는 그 목록) 필드인지.
            static bool IsBlockField(FieldInfo field)
            {
                var type = FieldPaths.ElementTypeOf(field.FieldType) ?? field.FieldType;
                return typeof(Trigger).IsAssignableFrom(type) || typeof(Outcome).IsAssignableFrom(type) || typeof(Condition).IsAssignableFrom(type) ||
                       typeof(ValueFilter).IsAssignableFrom(type) || typeof(Effect).IsAssignableFrom(type) || typeof(TargetSelector).IsAssignableFrom(type);
            }

            // 값 칸 목록을 SerializedProperty(옆 칸·위쪽 칸)로 만드는 칸인지: 탐색기의 리플렉션 값 칸으로는 같은 목록을 만들 수 없다.
            static bool NeedsProperty(FieldInfo field)
            {
                if (field == null) return false;
                if (field.IsDefined(typeof(FieldOpRefAttribute), false)) return true;
                if (typeof(IList).IsAssignableFrom(field.FieldType) && FieldPaths.ElementTypeOf(field.FieldType) == typeof(string)) return KeywordTools.GroupOf(field) != null;
                if (field.FieldType != typeof(string)) return false;
                return field.IsDefined(typeof(FieldPathAttribute), false) || field.IsDefined(typeof(FieldValueRefAttribute), false) ||
                       field.IsDefined(typeof(EventFieldRefAttribute), false) || field.IsDefined(typeof(GBible.Data.MemberRefAttribute), false) ||
                       KeywordTools.GroupOf(field) != null;
            }

            public override VisualElement FieldValue(string path)
            {
                var value = FieldPaths.Resolve(Current, path).FirstOrDefault();
                if (value != null && (FieldPaths.IsCollectionValue(value) || DataModel.Of(value.GetType()).Kind == DataKind.Object))
                    return Gs.Label(DataModel.Summary(value), "gs-nowrap");
                // 값 하나는 값 칸과 같은 표시(ID는 이름, enum은 보이는 이름).
                FieldPaths.TypeAt(Type, path, out var member, out _);
                return Gs.Label(ValueChoices.Display(member, value), "gs-nowrap");
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

        // 비어 있는 기능 필드를 붙이는 '+ 기능'(인스턴스 편집).
        void AddFeatureTools(Crumb here)
        {
            if (!EditorPolicy.CanEditStructure) return;   // 기능을 붙이는 것은 구조 — AI가 한다(EditorPolicy)
            var target = here.Value;
            var empty = InstanceFields.Features(target.GetType()).Where(f => f.GetValue(target) == null).ToList();
            if (empty.Count == 0) return;
            var row = Gs.Row("gs-wrap");
            row.style.marginTop = 8;
            GsButton add = null;
            add = new GsButton("+ 기능", () =>
            {
                var items = new List<GsDropdownItem>();
                foreach (var field in empty)
                    foreach (var type in InstanceFields.Choices(field.FieldType))
                    {
                        var f = field;
                        var t = type;
                        items.Add(new GsDropdownItem(f.Name, () =>
                        {
                            here.Apply(() => f.SetValue(target, Activator.CreateInstance(t)));
                            Changed?.Invoke();
                            Refresh();
                        })
                        {
                            Caption = t == f.FieldType ? TypeName(t) : $"{TypeName(f.FieldType)} › {TypeName(t)}",
                            Detail = BlockTypes.Description(t),
                        });
                    }
                GsDropdown.Show(add, items, placeholder: "기능 검색");
            }, GsButtonKind.Ghost);
            add.tooltip = "비어 있는 기능 필드를 붙입니다(붙으면 그 기능의 칸이 보입니다)";
            row.Add(add);
            _list.Add(row);
        }

        // ── 이름 ───────────────────────────────────────────────

        static string TypeName(Type type) => type == null ? "" : StorableTypes.Label(type);

        public static string LabelOf(MemberInfo member, string fallback)
        {
            // 칸 이름표 = 코드의 변수 이름.
            return member?.Name ?? fallback ?? "";
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
