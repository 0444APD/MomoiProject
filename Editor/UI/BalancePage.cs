using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using GBible.Data;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace GBible.EditorTools.UI
{
    // DB › 밸런스 값 탭: 줄마다 이름(개발자가 고치는 표시 이름, 없으면 ID) · 값. 상세 탭으로 넘어가지 않고 그 자리에서 접고 펼친다.
    //   - 이름 쪽을 누르면 펼쳐져 아래 칸(곡선 …)이 보인다. 다른 칸의 값에 따라 보이는 칸([ShowIf])은 그 값을 바꾸면 생기고 사라진다.
    //     칸은 쓰는 곳에만 보인다: 곡선을 쓰지 않는 값(방식 = 없음)은 곡선 칸을 그리지 않는다(✎ 편집 칸의 '곡선 쓰기'로 켠다). 펼칠 칸이 없으면 화살표도 없다.
    //   - 줄에 마우스를 올리면 오른쪽에 입력 칸이 뜬다(입력 칸 주석이 있으면 칸 안에 흐리게). 목록 칸만 새 창에서 고친다.
    //   - 줄 맨 오른쪽 ✎: 표시 이름 · 입력 칸 주석 · 메모를 고친다(ID는 코드가 쓰는 이름이라 바꾸기는 AI에게).
    // 바꾼 것은 DB의 다른 변경처럼 저장(Ctrl+S)할 때 적용한다.
    public sealed class BalancePage : CodexPage
    {
        public const string PageKey = "balance";
        public override string Key => PageKey;
        // 밸런스 값 항목은 AI가 DB 반영 스크립트로 만든다(EditorPolicy — 개발자는 값을 바꾼다). 구조 편집을 열었을 때만 만들기 버튼이 보인다.
        public override string CreateLabel => "+ 새로 만들기";

        // 펼친 줄 · 편집 칸을 연 줄(다시 그려도 남는다).
        static readonly HashSet<string> Expanded = new HashSet<string>(StringComparer.Ordinal);
        static readonly HashSet<string> Editing = new HashSet<string>(StringComparer.Ordinal);
        // 스크립트가 아니라 사람이 고치는 칸(✎ 편집 칸)과 줄의 값 칸 — 펼친 아래 칸에는 다시 보이지 않는다.
        static readonly HashSet<string> RowFields = new HashSet<string>(StringComparer.Ordinal) { "value", "label", "hint", "note" };

        static EditorDatabase Db => EditorDatabase.Instance;
        static CodexStaging Staging => EditorDatabase.Staging;

        static List<BalanceData> AllItems() => Db.OfType(typeof(BalanceData)).Cast<BalanceData>().ToList();

        public override string Caption(CodexPageContext context)
        {
            var items = AllItems();
            var errors = context.Issues.Count(i => i.Severity == ValidationSeverity.Error && i.Asset is BalanceData);
            return $"{items.Count}개" + (errors > 0 ? $" · 오류 {errors}" : "") +
                   "  ·  이름을 눌러 펼치고 접습니다 · 줄에 마우스를 올리면 오른쪽에 입력 칸 · ✎ 이름·주석·메모  ·  바꾼 것은 저장(Ctrl+S)할 때 적용";
        }

        public override void Create(CodexPageContext context) => new DataListPage(typeof(BalanceData)).Create(context);

        public override void Build(CodexPageContext context)
        {
            var list = Gs.Col("gs-bal-list");
            context.Content.Add(list);
            var search = (context.Search ?? "").Trim();
            var items = AllItems().Where(i => Matches(i, search)).ToList();
            if (items.Count == 0)
            {
                list.Add(Gs.Label(search == "" ? "밸런스 값이 아직 없습니다. 바꿀 숫자는 AI가 DB 반영 스크립트로 만듭니다." : "찾는 밸런스 값이 없습니다.", "gs-caption"));
                return;
            }
            // 폴더 = 묶음 머리글(정리용). 폴더 없는 것이 먼저.
            foreach (var group in items.GroupBy(i => i.Folder ?? "").OrderBy(g => g.Key == "" ? "" : "\u0001" + g.Key, StringComparer.Ordinal))
            {
                if (group.Key != "") list.Add(Gs.Label(group.Key, "gs-bal-section"));
                foreach (var item in group) list.Add(Entry(context, item));
            }
        }

        static bool Matches(BalanceData item, string search)
        {
            if (search == "") return true;
            return (item.Id ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                || (item.label ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                || (item.note ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ── 줄 하나 ─────────────────────────────────────────────

        static VisualElement Entry(CodexPageContext context, BalanceData item)
        {
            var box = Gs.Col("gs-bal-entry");
            var id = item.Id ?? "";
            var so = new SerializedObject(item);
            var hasChildren = Visible(so).Any();
            var expanded = hasChildren && Expanded.Contains(id);
            var issues = context.Issues.Where(i => i.Asset == item).ToList();

            var row = Gs.Row("gs-bal-row");
            var name = Gs.Row("gs-bal-name");
            name.Add(Gs.Label(!hasChildren ? "" : expanded ? "▾" : "▸", "gs-bal-arrow"));
            var title = Gs.Label(string.IsNullOrWhiteSpace(item.label) ? id : item.label, "gs-bal-label", "gs-nowrap");
            name.Add(title);
            if (!string.IsNullOrWhiteSpace(item.label)) name.Add(Gs.Label(id, "gs-bal-id", "gs-nowrap"));
            name.tooltip = (string.IsNullOrWhiteSpace(item.note) ? "" : item.note + "\n") + $"ID {id}" + (hasChildren ? $"  ·  눌러서 {(expanded ? "접기" : "펼치기")}" : "");
            name.RegisterCallback<ClickEvent>(e =>
            {
                if (!hasChildren) return;
                if (!Expanded.Remove(id)) Expanded.Add(id);
                context.Refresh?.Invoke();
            });
            row.Add(name);

            if (issues.Any(i => i.Severity == ValidationSeverity.Error))
            {
                title.AddToClassList("gs-bal-label--error");
                var badge = new GsBadge("오류", Gs.Danger);
                badge.tooltip = string.Join("\n", issues.Select(i => i.Message));
                row.Add(badge);
            }
            if (Staging.IsPending(item)) row.Add(new GsBadge(Staging.IsAdded(item) ? "새 항목 · 적용 전" : "변경 · 적용 전", Gs.Warning));

            AddValue(context, row, item, so.FindProperty("value"), item.hint);

            var edit = new GsButton("✎", () =>
            {
                if (!Editing.Remove(id)) Editing.Add(id);
                context.Refresh?.Invoke();
            }, GsButtonKind.Ghost);
            edit.AddToClassList("gs-bal-edit-btn");
            edit.tooltip = "표시 이름 · 입력 칸 주석 · 메모 고치기";
            row.Add(edit);
            box.Add(row);

            if (Editing.Contains(id)) box.Add(EditPanel(context, item));
            if (expanded) box.Add(Children(context, item, so));
            return box;
        }

        // 펼친 아래 칸: 줄의 칸(값·이름·주석·메모)을 뺀 칸. 묶음 칸(곡선)은 머리글 + 그 안의 칸. [ShowIf]가 맞지 않는 칸은 그리지 않는다.
        static VisualElement Children(CodexPageContext context, BalanceData item, SerializedObject so)
        {
            var children = Gs.Col("gs-bal-children");
            foreach (var property in Visible(so)) AddProperty(context, children, item, property, 0);
            if (children.childCount == 0) children.Add(Gs.Label("더 고칠 칸이 없습니다.", "gs-caption"));
            return children;
        }

        // 펼친 아래에 그릴 최상위 칸: 줄의 칸을 빼고, [ShowIf]가 맞고, 곡선이면 쓰는 것(방식 ≠ 없음)만.
        static IEnumerable<SerializedProperty> Visible(SerializedObject so) =>
            PropertyUtil.TopLevel(so).Where(p => !RowFields.Contains(p.name) && ShowIfRules.Visible(p) && !UnusedCurve(p));

        static bool UnusedCurve(SerializedProperty property) =>
            property.name == "curve" && property.FindPropertyRelative("kind") is SerializedProperty kind && kind.enumValueIndex == (int)CurveKind.None;

        static void AddProperty(CodexPageContext context, VisualElement parent, BalanceData item, SerializedProperty property, int depth)
        {
            var field = PropertyUtil.GetField(property, out _);
            var label = field?.GetCustomAttribute<FieldLabelAttribute>()?.Text ?? PropertyUtil.Nicify(property.name);
            var tip = field?.GetCustomAttribute<TooltipAttribute>()?.tooltip;
            if (property.propertyType == SerializedPropertyType.Generic && !property.isArray)
            {
                var head = Gs.Label(label, "gs-bal-subhead");
                if (!string.IsNullOrEmpty(tip)) head.tooltip = tip;
                parent.Add(head);
                var inner = Gs.Col("gs-bal-children");
                foreach (var child in PropertyUtil.Children(property))
                    if (ShowIfRules.Visible(child)) AddProperty(context, inner, item, child.Copy(), depth + 1);
                parent.Add(inner);
                return;
            }
            var row = Gs.Row("gs-bal-row", "gs-bal-row--child");
            var name = Gs.Row("gs-bal-name");
            name.Add(Gs.Label(label, "gs-bal-label", "gs-nowrap"));
            name.tooltip = (string.IsNullOrEmpty(tip) ? "" : tip + "\n") + property.name;
            row.Add(name);
            AddValue(context, row, item, property, null);
            parent.Add(row);
        }

        // 줄 오른쪽의 값: 평소에는 글자, 마우스를 올리면(또는 입력 중) 입력 칸. 숫자·글자는 입력 칸, enum은 고르기, 참거짓은 체크, 목록은 새 창.
        static void AddValue(CodexPageContext context, VisualElement row, BalanceData item, SerializedProperty property, string hint)
        {
            if (property == null) return;
            var path = property.propertyPath;
            var shown = Gs.Label(Text(property), "gs-bal-value", "gs-nowrap");
            row.Add(shown);
            var input = Gs.Row("gs-bal-input");
            row.Add(input);

            switch (property.propertyType)
            {
                case SerializedPropertyType.Boolean:
                {
                    var toggle = new Toggle { value = property.boolValue };
                    toggle.RegisterValueChangedCallback(e => Write(context, item, path, p => p.boolValue = e.newValue));
                    input.Add(toggle);
                    break;
                }
                case SerializedPropertyType.Enum:
                {
                    var names = property.enumDisplayNames.ToList();
                    var popup = new PopupField<string>(names, Mathf.Clamp(property.enumValueIndex, 0, Math.Max(0, names.Count - 1)));
                    popup.RegisterValueChangedCallback(e => Write(context, item, path, p => p.enumValueIndex = names.IndexOf(e.newValue)));
                    input.Add(popup);
                    break;
                }
                default:
                    if (property.isArray && property.propertyType != SerializedPropertyType.String)
                    {
                        var open = new GsButton($"목록 {property.arraySize}개 · 새 창에서 고치기", () => BalanceListWindow.Open(item, path), GsButtonKind.Ghost);
                        input.Add(open);
                        break;
                    }
                    var text = new TextField { value = Raw(property), isDelayed = true };
                    text.RegisterCallback<FocusInEvent>(_ => row.AddToClassList("gs-bal-row--editing"));
                    text.RegisterCallback<FocusOutEvent>(_ => row.RemoveFromClassList("gs-bal-row--editing"));
                    var type = property.propertyType;
                    text.RegisterValueChangedCallback(e =>
                    {
                        if (!TryWrite(context, item, path, type, e.newValue)) text.SetValueWithoutNotify(e.previousValue);
                    });
                    input.Add(text);
                    if (!string.IsNullOrWhiteSpace(hint))
                    {
                        var note = Gs.Label(hint, "gs-bal-hint");
                        note.pickingMode = PickingMode.Ignore;
                        input.Add(note);
                    }
                    break;
            }
        }

        static string Raw(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Float: return property.doubleValue.ToString("0.######", CultureInfo.InvariantCulture);
                case SerializedPropertyType.Integer: return property.longValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.String: return property.stringValue ?? "";
                default: return PropertyUtil.Summary(property);
            }
        }

        static string Text(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Boolean: return property.boolValue ? "켬" : "끔";
                case SerializedPropertyType.Enum:
                    return property.enumValueIndex >= 0 && property.enumValueIndex < property.enumDisplayNames.Length ? property.enumDisplayNames[property.enumValueIndex] : "";
                case SerializedPropertyType.Float: return property.doubleValue.ToString("0.####", CultureInfo.InvariantCulture);
                default:
                    if (property.isArray && property.propertyType != SerializedPropertyType.String) return $"목록 {property.arraySize}개";
                    return Raw(property);
            }
        }

        static bool TryWrite(CodexPageContext context, BalanceData item, string path, SerializedPropertyType type, string text)
        {
            text = (text ?? "").Trim();
            switch (type)
            {
                case SerializedPropertyType.Float:
                    if (!TryNumber(text, out var number)) return false;
                    Write(context, item, path, p => p.doubleValue = number);
                    return true;
                case SerializedPropertyType.Integer:
                    if (!TryNumber(text, out var whole)) return false;
                    Write(context, item, path, p => p.longValue = (long)Math.Round(whole, MidpointRounding.AwayFromZero));
                    return true;
                case SerializedPropertyType.String:
                    Write(context, item, path, p => p.stringValue = text);
                    return true;
                default:
                    return false;
            }
        }

        static bool TryNumber(string text, out double value) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

        // 작업본에 쓴다(적용은 저장 Ctrl+S). 다시 그리면 바뀐 값·[ShowIf]가 맞는 칸이 보인다.
        static void Write(CodexPageContext context, BalanceData item, string path, Action<SerializedProperty> set)
        {
            var target = Staging.Work(item);
            var so = new SerializedObject(target);
            var property = so.FindProperty(path);
            if (property == null) return;
            set(property);
            so.ApplyModifiedPropertiesWithoutUndo();
            Staging.Touch();
            context.Refresh?.Invoke();
        }

        // ✎ 편집 칸: 표시 이름 · 입력 칸 주석 · 메모. ID는 보이기만 한다.
        static VisualElement EditPanel(CodexPageContext context, BalanceData item)
        {
            var panel = Gs.Col("gs-bal-edit");
            panel.Add(Line(context, item, "label", "표시 이름", item.label, "이 탭에 보일 이름(한글로 자유롭게). 비우면 ID", false));
            panel.Add(Line(context, item, "hint", "입력 칸 주석", item.hint, "입력 칸 안에 흐리게 보일 글. 예: 1 = 그대로, 0 = 멈춤", false));
            panel.Add(Line(context, item, "note", "메모", item.note, "무엇에 쓰는 숫자인지(이름에 마우스를 올리면 보인다)", true));
            // 곡선은 쓰는 값에만 칸이 보이므로, 켜고 끄는 것은 여기서(켜면 '일정하게'로 시작 — 펼친 칸에서 방식을 바꾼다).
            var curve = Gs.Row("gs-bal-edit-line");
            var curveName = Gs.Label("곡선 쓰기", "gs-bal-edit-name");
            curveName.tooltip = "레벨·층·깊이 같은 단계에 따라 바뀌는 값. 켜면 펼친 칸에 곡선 칸이 생긴다";
            curve.Add(curveName);
            var uses = item.curve != null && item.curve.kind != CurveKind.None;
            var toggle = new Toggle { value = uses };
            toggle.RegisterValueChangedCallback(e =>
            {
                if (e.newValue) Expanded.Add(item.Id ?? "");
                Write(context, item, "curve.kind", p => p.enumValueIndex = (int)(e.newValue ? CurveKind.Linear : CurveKind.None));
            });
            curve.Add(toggle);
            panel.Add(curve);
            var id = Gs.Label($"ID  {item.Id}   ·   코드(GameIds)·저장 파일이 쓰는 이름 — 바꾸기는 AI에게", "gs-caption");
            id.style.marginTop = 4;
            panel.Add(id);
            return panel;
        }

        static VisualElement Line(CodexPageContext context, BalanceData item, string path, string label, string value, string caption, bool multiline)
        {
            var line = Gs.Row("gs-bal-edit-line");
            var name = Gs.Label(label, "gs-bal-edit-name");
            name.tooltip = caption;
            line.Add(name);
            var text = new TextField { value = value ?? "", isDelayed = true, multiline = multiline };
            text.AddToClassList("gs-bal-edit-input");
            text.tooltip = caption;
            text.RegisterValueChangedCallback(e => Write(context, item, path, p => p.stringValue = e.newValue ?? ""));
            line.Add(text);
            return line;
        }
    }

    // 밸런스 값의 목록 칸(곡선의 점 …)을 고치는 창. 고친 것은 작업본에 들어가고 저장(Ctrl+S)할 때 적용한다.
    public sealed class BalanceListWindow : EditorWindow
    {
        BalanceData _item;
        string _path;

        public static void Open(BalanceData item, string path)
        {
            var window = GsWindows.Open<BalanceListWindow>($"{(string.IsNullOrWhiteSpace(item.label) ? item.Id : item.label)} · {path}", new Vector2(420, 480));
            window._item = item;
            window._path = path;
            window.Rebuild();
        }

        void CreateGUI() => Rebuild();

        void Rebuild()
        {
            rootVisualElement.Clear();
            Gs.Apply(rootVisualElement);
            if (_item == null) return;
            var target = EditorDatabase.Staging.Work(_item);
            var so = new SerializedObject(target);
            var property = so.FindProperty(_path);
            if (property == null) return;
            var body = Gs.Col();
            body.style.paddingLeft = 12;
            body.style.paddingRight = 12;
            body.style.paddingTop = 10;
            var caption = Gs.Label("고친 것은 작업본에 들어갑니다 — DB에서 저장(Ctrl+S)할 때 적용됩니다.", "gs-caption");
            caption.style.marginBottom = 8;
            body.Add(caption);
            var field = new PropertyField(property);
            field.Bind(so);
            field.RegisterCallback<SerializedPropertyChangeEvent>(_ => EditorDatabase.Staging.Touch());
            body.Add(field);
            rootVisualElement.Add(body);
        }
    }
}
