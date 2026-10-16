using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GBible.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GBible.EditorTools.UI
{
    // 클래스 상세(상세 창의 탭): 코드로 정의한 클래스의 결과를 확인한다(읽기 전용, 클래스는 코드에서 고친다).
    //  · 필드마다 이름·형식·연결된 키워드 그룹·[Hide]·[ShowIf](모두 코드가 원본, 보기만). 형식이 다른 클래스면 그 클래스로 바로 이동.
    //  · 스크립트(cs) 열기·프로젝트 창에서 보기.
    //  · 코드가 아닌 에디터 설정(스프라이트 규격 그룹, 필수 여부)은 여기서 정하고 DB 저장(Ctrl+S) 때 쓴다.
    public static class ClassDetail
    {
        // DB·키워드 탭·값 칸 어디서든: 클래스 상세를 열고 focusField가 있으면 그 필드를 강조한다.
        public static void Open(string fullName, string focusField = null)
        {
            var entry = ClassCatalog.Find(fullName);
            if (entry == null)
            {
                Debug.LogWarning($"[MomoiProject] 클래스 '{fullName}'을(를) 찾을 수 없습니다.");
                return;
            }
            var focus = focusField;
            DetailWindow.OpenCustom("class:" + fullName, entry.Name, "클래스", host =>
            {
                var content = Build(host, fullName, focus);
                // 강조는 처음 열 때 한 번만.
                focus = null;
                return content;
            });
        }

        public static void Open(Type type, string focusField = null)
        {
            if (type != null) Open(type.FullName, focusField);
        }

        internal static VisualElement Build(VisualElement host, string fullName, string focusField)
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            var root = scroll.contentContainer;
            root.style.paddingLeft = 20;
            root.style.paddingRight = 20;
            root.style.paddingTop = 16;
            root.style.paddingBottom = 24;

            var entry = ClassCatalog.Find(fullName);
            if (entry == null)
            {
                root.Add(Gs.Label("이 클래스는 더 이상 없습니다.", "gs-caption"));
                return scroll;
            }

            // ── 머리: 이름·종류·그룹·파일 ──
            var head = Gs.Row();
            head.Add(Gs.Label(entry.Name, "gs-title"));
            var kind = new GsBadge(KindText(entry.Kind) +(string.IsNullOrEmpty(entry.Label) || entry.Label == entry.Name ? "" : $" · {entry.Label}"), Gs.PaletteFor("kind:" + entry.Kind));
            kind.style.marginLeft = 10;
            head.Add(kind);
            if (entry.Locked) head.Add(Badge("시스템", Gs.PaletteFor("locked")));
            root.Add(head);
            var name = Gs.Label(entry.FullName, "gs-caption");
            name.style.marginTop = 2;
            root.Add(name);

            var tools = Gs.Row("gs-wrap");
            tools.style.marginTop = 12;
            tools.Add(Gs.Label("폴더: " + GroupLabel(entry.Group), "gs-caption"));
            tools.Add(Gs.HSpace(10));
            var file = entry.Type != null ? ClassCatalog.FileOf(entry.FullName) : null;
            if (!string.IsNullOrEmpty(file))
            {
                var open = new GsButton("cs 열기", () => AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<MonoScript>(file)), GsButtonKind.Primary);
                open.tooltip = file;
                tools.Add(open);
                var ping = new GsButton("프로젝트 창", () => EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<MonoScript>(file)), GsButtonKind.Ghost);
                ping.tooltip = "프로젝트 창에서 스크립트 보기";
                tools.Add(ping);
            }
            if (entry.Kind == ClassKind.Codex && entry.Type != null)
                tools.Add(new GsButton("DB 탭 열기", () => DatabaseWindow.ShowType(entry.Type), GsButtonKind.Ghost));
            root.Add(tools);

            // ── 필드 ──
            var fieldsHead = Gs.Row();
            fieldsHead.style.marginTop = 20;
            fieldsHead.style.marginBottom = 6;
            fieldsHead.Add(Gs.Label("필드", "gs-heading"));
            root.Add(fieldsHead);

            var fields = ClassCatalog.Fields(entry);
            if (fields.Count == 0)
                root.Add(Gs.Label("필드가 없습니다. 스크립트에서 선언하세요.", "gs-caption"));
            VisualElement focusRow = null;
            foreach (var field in fields)
            {
                var row = FieldRow(host, entry, field);
                if (focusField != null && field.Name == focusField)
                {
                    row.style.borderLeftWidth = 2;
                    row.style.borderLeftColor = Gs.Accent;
                    focusRow = row;
                }
                root.Add(row);
            }
            if (focusRow != null) focusRow.schedule.Execute(() =>
            {
                if (focusRow.panel != null) scroll.ScrollTo(focusRow);
            }).StartingIn(60);

            return scroll;
        }

        static string KindText(ClassKind kind) =>
            kind == ClassKind.Codex ? "DB 종류" : kind == ClassKind.Event ? "이벤트" : "데이터 클래스";

        static GsBadge Badge(string text, Color color)
        {
            var badge = new GsBadge(text, color);
            badge.style.marginLeft = 6;
            return badge;
        }

        // ── 필드 한 줄 ─────────────────────────────────────────

        static VisualElement FieldRow(VisualElement host, ClassEntry entry, ClassFieldEntry field)
        {
            var row = Gs.Col("gs-kw__group");
            row.style.marginBottom = 6;

            var top = Gs.Row();
            var names = Gs.Col("gs-grow");
            var title = Gs.Row();
            title.Add(Gs.Label(field.Name, "gs-heading"));
            if (field.InheritedFrom != null) title.Add(Badge("상속: " + field.InheritedFrom, Gs.PaletteFor("inherited")));
            names.Add(title);
            var label = field.Field?.GetCustomAttribute<FieldLabelAttribute>()?.Text;
            names.Add(Gs.Label(field.TypeLabel + (string.IsNullOrEmpty(label) ? "" : $"  ·  {label}"), "gs-caption"));
            top.Add(names);
            row.Add(top);

            var info = field.Field;
            if (info == null) return row;
            var declaring = info.DeclaringType;

            // 형식이 다른 클래스(또는 그 리스트·배열·딕셔너리)면 그 클래스로 바로 이동.
            var links = Gs.Row("gs-wrap");
            foreach (var target in ReferencedClasses(info.FieldType))
            {
                var name = target.Name;
                var go = new GsButton("→ " + name, () => Open(target), GsButtonKind.Ghost);
                go.tooltip = $"{target.FullName} 클래스 보기";
                links.Add(go);
            }
            if (links.childCount > 0) row.Add(links);

            // 인스턴스 편집에서 보이기: [Hide]·[ShowIf](코드에서 정한 것, 보기만).
            if (entry.Kind == ClassKind.Data && !entry.Locked && info.DeclaringType == entry.Type)
            {
                if (InstanceFields.IsHidden(info))
                    row.Add(Caption("DB › 인스턴스에서 보이지 않는 필드입니다(값은 그대로 저장·복사).  [Hide]"));
                var showIf = info.GetCustomAttribute<ShowIfAttribute>(true);
                if (showIf != null)
                    row.Add(Caption(showIf.Describe() + "  [ShowIf]"));
                if (InstanceFields.IsFeature(info))
                    row.Add(Caption("기능 필드: 비어 있으면 그 기능이 없습니다. 기능을 붙이고 빼는 것은 AI가 DB 반영 스크립트로 합니다."));
            }

            if (KeywordTools.CanConnect(info))
            {
                var group = KeywordTools.GroupOf(info);
                var rule = KeywordTools.RuleOf(declaring, info);
                string text;
                if (group == null) text = rule == null || rule.source == FieldRuleSource.Free || rule.source == FieldRuleSource.Keyword ? "연결 없음(자유 입력)" : rule.source == FieldRuleSource.Data ? "DB 항목에서 고름" : "플레이어 데이터에서 고름";
                else text = KeywordTools.FindGroup(group)?.DisplayName ?? group;
                var many = KeywordTools.IsMulti(info) ? "여러 개 고름" : "하나 고름";
                // 키워드 그룹 연결은 코드의 [Keyword(typeof(그룹))]다(보기만).
                row.Add(LinkRow("키워드 그룹", text, many + "  ·  코드의 [Keyword]", group != null, null));
            }
            else if (KeywordTools.IsKeywordField(info) && EditorDatabase.RefTypeOf(info) is Type reference)
            {
                row.Add(Caption($"코드에서 '{EditorDatabase.RefKindLabel(info)}' 항목을 고르도록 정해진 필드입니다."));
            }

            if (KeywordTools.IsSpriteField(info))
            {
                var rule = KeywordTools.RuleOf(declaring, info);
                var spriteFrame = rule?.spriteFrame ?? "";
                row.Add(LinkRow("스프라이트 틀", spriteFrame == "" ? "연결 없음" : spriteFrame, "원본을 넣으면 틀의 모든 크기로 한꺼번에 자른다", spriteFrame != "",
                    anchor => PickSpriteFrame(anchor, declaring, info, spriteFrame)));
                if (spriteFrame != "")
                    row.Add(LinkRow("미리보기 크기", string.IsNullOrEmpty(rule.previewSize) ? "원본 그대로" : rule.previewSize, "목록·카드에 보일 크기", !string.IsNullOrEmpty(rule.previewSize),
                        anchor => PickPreview(anchor, declaring, info, spriteFrame, rule.previewSize)));
            }

            if (declaring != typeof(GameData))
            {
                var required = EditorDatabase.IsRequired(info);
                var toggle = new Toggle("필수 (비어 있으면 DB 검증 오류)") { value = required };
                toggle.style.marginTop = 6;
                toggle.RegisterValueChangedCallback(e => KeywordTools.SetRequired(declaring, info, e.newValue));
                row.Add(toggle);
            }
            return row;
        }

        static Label Caption(string text)
        {
            var caption = Gs.Label(text, "gs-caption");
            caption.style.marginTop = 6;
            caption.style.whiteSpace = WhiteSpace.Normal;
            return caption;
        }

        // "이름표   [값 ▾]   설명" 한 줄. 값을 누르면 목록이 열린다(pick이 null이면 보기만).
        static VisualElement LinkRow(string label, string value, string hint, bool linked, Action<VisualElement> pick)
        {
            var line = Gs.Row();
            line.style.marginTop = 8;
            var name = Gs.Label(label, "gs-detail__label");
            name.style.width = 130;
            line.Add(name);
            var box = pick != null ? Gs.Box("gs-inline", "gs-inline--editable") : Gs.Box("gs-inline");
            box.style.minWidth = 160;
            var text = Gs.Label(value, "gs-inline__value");
            if (!linked) text.AddToClassList("gs-inline__empty");
            box.Add(text);
            if (pick != null) box.RegisterCallback<ClickEvent>(_ => pick(box));
            line.Add(box);
            var caption = Gs.Label(hint, "gs-caption");
            caption.style.marginLeft = 10;
            line.Add(caption);
            return line;
        }

        // ── 연결 고르기 ────────────────────────────────────────

        static void PickSpriteFrame(VisualElement anchor, Type type, FieldInfo field, string current)
        {
            var items = new List<GsDropdownItem>
            {
                new GsDropdownItem("연결 없음", () => KeywordTools.SetSpriteFrame(type, field, "")) { Selected = current == "" },
            };
            foreach (var frame in SpriteSpecEditor.Frames.OrderBy(f => f.Id, StringComparer.Ordinal))
            {
                var id = frame.Id;
                items.Add(new GsDropdownItem(id, () => KeywordTools.SetSpriteFrame(type, field, id))
                {
                    Group = "스프라이트 틀",
                    Caption = string.Join(" · ", SpriteSpecEditor.SizesOf(id).Select(s => $"{s.name} {s.width}×{s.height}")),
                    Selected = current == id,
                });
            }
            if (items.Count == 1) items.Add(new GsDropdownItem("틀이 없습니다 — Project 창에서 Create › MomoiProject › 스프라이트 틀로 먼저 만드세요") { Disabled = true });
            GsDropdown.Show(anchor, items, placeholder: "틀 검색");
        }

        static void PickPreview(VisualElement anchor, Type type, FieldInfo field, string frame, string current)
        {
            var items = new List<GsDropdownItem>
            {
                new GsDropdownItem("원본 그대로", () => KeywordTools.SetPreviewSize(type, field, "")) { Selected = string.IsNullOrEmpty(current) },
            };
            foreach (var size in SpriteSpecEditor.SizesOf(frame))
            {
                var name = size.name;
                items.Add(new GsDropdownItem(name, () => KeywordTools.SetPreviewSize(type, field, name)) { Caption = $"{size.width}×{size.height}", Selected = current == name });
            }
            GsDropdown.Show(anchor, items, placeholder: "크기 검색");
        }

        // 그룹 표시: "" = Code 폴더 바로 아래, Outside = Code 폴더 밖.
        public static string GroupLabel(string group) =>
            group == ClassFolders.Outside ? "Code 폴더 밖" : string.IsNullOrEmpty(group) ? "Code (최상위)" : group;

        // 필드 형식에서 이동할 수 있는 클래스(클래스 탭에 있는 것): T, List<T>, T[], Dictionary<K, T>.
        static IEnumerable<Type> ReferencedClasses(Type type)
        {
            var seen = new HashSet<Type>();
            var stack = new Stack<Type>();
            stack.Push(type);
            while (stack.Count > 0)
            {
                var t = stack.Pop();
                if (t == null || !seen.Add(t)) continue;
                if (t.IsArray) stack.Push(t.GetElementType());
                else if (t.IsGenericType) foreach (var argument in t.GetGenericArguments()) stack.Push(argument);
                else if (!t.IsPrimitive && t != typeof(string) && !t.IsEnum && ClassCatalog.Find(t.FullName) != null) yield return t;
            }
        }

    }
}
