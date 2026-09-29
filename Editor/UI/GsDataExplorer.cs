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
    // 폴더형 데이터 탐색기. 클래스·컬렉션은 폴더, 값은 파일처럼 한 단계씩 들어가며 본다.
    //  · 고르기(Pick): 타입만 보고 필드 경로를 고른다(조건의 "필드" 칸 등).
    //  · 둘러보기(Browse): 실제 값을 보고, 값 파일을 눌러 바로 고친다(모니터의 인벤토리 등).
    // 어디서 쓰든 같은 모양이라, 인벤토리 모니터링과 더미 아이템 확인이 같은 화면이 된다.
    public sealed class GsDataExplorer : VisualElement
    {
        // 현재 폴더까지의 발자취. 고르기는 타입만, 둘러보기는 값과 그 값을 담은 부모까지 기억한다.
        sealed class Crumb
        {
            public string Label;
            public string Path;      // 고르기용 필드 경로(요소는 [] 로)
            public Type Type;
            public object Value;     // 둘러보기용
        }

        sealed class Entry
        {
            public string Label;
            public string Caption;
            public string Preview;
            public string Path;
            public Type Type;
            public object Value;
            public bool IsFolder;
            public bool Pickable;
            // 둘러보기: 값을 고치는 방법(없으면 읽기 전용).
            public Action<object> Set;
            public Color Color;
        }

        readonly bool _browse;
        readonly Action<string> _onPick;
        readonly Action<Action> _apply;
        readonly List<Crumb> _trail = new List<Crumb>();
        readonly VisualElement _breadcrumb;
        readonly VisualElement _list;
        readonly GsButton _pickFolder;
        readonly Func<Type, string> _typeName;

        public event Action Changed;

        GsDataExplorer(bool browse, string rootLabel, Type rootType, object rootValue, Action<string> onPick, Action<Action> apply)
        {
            _browse = browse;
            _onPick = onPick;
            _apply = apply;
            _typeName = t => t == null ? "" : StorableTypes.Label(t);
            AddToClassList("gs-explorer");

            var toolbar = Gs.Row();
            toolbar.AddToClassList("gs-explorer__toolbar");
            var back = new GsButton("←", GoUp, GsButtonKind.Ghost);
            back.tooltip = "위 폴더로";
            toolbar.Add(back);
            _breadcrumb = Gs.Row("gs-grow");
            _breadcrumb.AddToClassList("gs-explorer__crumbs");
            toolbar.Add(_breadcrumb);
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

            _trail.Add(new Crumb { Label = rootLabel, Path = "", Type = rootType, Value = rootValue });
            Refresh();
        }

        // 필드 경로 고르기 창. current가 있으면 그 폴더에서 시작한다.
        public static GsModal Pick(VisualElement host, Type rootType, string rootLabel, string current, Action<string> onPick)
        {
            GsModal modal = null;
            var explorer = new GsDataExplorer(false, rootLabel, rootType, null, path =>
            {
                modal?.Close();
                onPick(path);
            }, null);
            explorer.OpenPath(current, parentOnly: true);
            modal = GsModal.Show(host, Frame("필드 고르기", "폴더를 열어 들어가고, 값을 누르면 고릅니다.", explorer));
            return modal;
        }

        // 실제 값 둘러보기·고치기. apply: 값을 고치는 동작을 감싸는 방법(예: 저장소 Modify, Undo 기록).
        public static GsDataExplorer Browse(string rootLabel, object root, Action<Action> apply) =>
            new GsDataExplorer(true, rootLabel, root?.GetType(), root, null, apply);

        public static GsModal BrowseModal(VisualElement host, string title, object root, Action<Action> apply)
        {
            var explorer = Browse(title, root, apply);
            return GsModal.Show(host, Frame(title, apply != null ? "값을 누르면 고칠 수 있습니다." : null, explorer));
        }

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

        void Pick(string path)
        {
            _onPick?.Invoke(path);
        }

        void Refresh()
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
            if (_pickFolder != null)
                _pickFolder.style.display = here.Path.Length > 0 && FieldPaths.ElementTypeOf(here.Type) != null ? DisplayStyle.Flex : DisplayStyle.None;

            _list.Clear();
            var entries = _browse ? BrowseEntries(here) : PickEntries(here);
            if (entries.Count == 0)
            {
                _list.Add(Gs.Label(_browse ? "비어 있습니다" : "더 들어갈 항목이 없습니다", "gs-caption", "gs-explorer__empty"));
                return;
            }
            foreach (var entry in entries) _list.Add(Row(entry));
        }

        // ── 고르기: 타입의 구조 ─────────────────────────────────

        List<Entry> PickEntries(Crumb here)
        {
            var list = new List<Entry>();
            var element = FieldPaths.ElementTypeOf(here.Type);
            if (element != null && here.Path.Length > 0 && !here.Path.EndsWith(FieldPaths.Each, StringComparison.Ordinal))
            {
                // 컬렉션 폴더 안: "각 항목" 폴더(요소마다 검사)
                list.Add(new Entry
                {
                    Label = "각 항목",
                    Caption = _typeName(element),
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
                    Caption = member.IsCollection ? $"목록 · {_typeName(member.ElementType)}" : _typeName(member.Type),
                    Path = Join(here.Path, member.Name),
                    Type = member.Type,
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
            var value = here.Value;
            if (value == null) return list;
            var info = DataModel.Of(value.GetType());
            if (FieldPaths.IsCollectionValue(value))
            {
                int index = 0;
                if (value is IDictionary map)
                {
                    foreach (DictionaryEntry pair in map)
                    {
                        var key = pair.Key;
                        list.Add(ValueEntry($"[{pair.Key}]", pair.Value, info.ElementType ?? pair.Value?.GetType(),
                            v => map[key] = v));
                    }
                    return list;
                }
                var ilist = value as IList;
                foreach (var item in FieldPaths.Items(value))
                {
                    var at = index++;
                    Action<object> set = ilist != null ? v => ilist[at] = v : (Action<object>)null;
                    list.Add(ValueEntry($"[{at}]", item, info.ElementType ?? item?.GetType(), set));
                }
                return list;
            }
            foreach (var member in FieldPaths.MembersOf(value.GetType()))
            {
                var target = value;
                var field = member.Info as FieldInfo;
                var current = field != null ? field.GetValue(target) : FieldPaths.Read(target, member.Name);
                Action<object> set = field != null && !field.IsInitOnly ? v => field.SetValue(target, v) : (Action<object>)null;
                var entry = ValueEntry(LabelOf(member.Info, member.Name), current, member.Type, set);
                list.Add(entry);
            }
            return list;
        }

        Entry ValueEntry(string label, object value, Type declared, Action<object> set)
        {
            var type = value?.GetType() ?? declared;
            var kind = type == null ? DataKind.Scalar : DataModel.Of(type).Kind;
            var folder = value != null && (kind == DataKind.Object || FieldPaths.IsCollectionValue(value));
            return new Entry
            {
                Label = label,
                Caption = type == null ? "" : folder && FieldPaths.IsCollectionValue(value) ? $"목록 · {FieldPaths.Items(value).Count()}개" : _typeName(type),
                Preview = folder ? DataModel.Summary(value) : StateStore.Format(value),
                Type = type,
                Value = value,
                IsFolder = folder,
                Set = folder || kind == DataKind.Reference ? null : set,
                Color = folder ? Gs.Palette[6] : Gs.Palette[8],
            };
        }

        // ── 한 줄 ──────────────────────────────────────────────

        VisualElement Row(Entry entry)
        {
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

            row.RegisterCallback<ClickEvent>(e =>
            {
                // 줄 안의 버튼(선택)은 버튼이 처리한다.
                if (e.target is VisualElement target && (target is GsButton || target.GetFirstAncestorOfType<GsButton>() != null)) return;
                if (entry.IsFolder)
                {
                    _trail.Add(new Crumb { Label = entry.Label, Path = entry.Path, Type = entry.Type, Value = entry.Value });
                    Refresh();
                }
                else if (!_browse && entry.Pickable) Pick(entry.Path);
                else if (_browse && entry.Set != null && _apply != null) BeginEdit(row, preview, entry);
            });
            if (_browse && !entry.IsFolder && entry.Set != null && _apply != null) row.tooltip = "클릭해서 고치기";
            return row;
        }

        // 값 파일 고치기: 형식에 맞는 입력(참거짓은 바로 뒤집기, enum은 목록).
        void BeginEdit(VisualElement row, Label preview, Entry entry)
        {
            var type = Nullable.GetUnderlyingType(entry.Type) ?? entry.Type;
            void Commit(object next)
            {
                _apply(() => entry.Set(next));
                Changed?.Invoke();
                Refresh();
            }
            if (type == typeof(bool))
            {
                Commit(!(entry.Value is bool b && b));
                return;
            }
            if (type != null && type.IsEnum)
            {
                var items = Enum.GetNames(type).Select(name => new GsDropdownItem(name, () => Commit(Enum.Parse(type, name))) { Selected = entry.Value?.ToString() == name }).ToList();
                GsDropdown.Show(preview, items);
                return;
            }
            if (row.Q<TextField>() != null) return;
            var field = new TextField { value = entry.Value == null ? "" : Convert.ToString(entry.Value, CultureInfo.InvariantCulture) };
            field.AddToClassList("gs-inline");
            field.style.flexGrow = 1;
            preview.style.display = DisplayStyle.None;
            row.Insert(row.IndexOf(preview), field);
            void Finish(bool commit)
            {
                if (field.parent == null) return;
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

        // ── 이름 ───────────────────────────────────────────────

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
