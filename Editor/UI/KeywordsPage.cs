using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine.UIElements;

namespace GBible.EditorTools.UI
{
    // DB의 키워드 탭: 코드에 정의된 키워드 그룹([KeywordGroup] static class)과 키워드(const string)를 보고 고친다.
    // 코드가 원본이다. 여기서 한 변경(그룹·키워드 만들기, 이름 바꾸기, 지우기)은 저장(Ctrl+S)할 때 그룹 코드 파일에 쓰이고,
    // 프로그래머가 코드에서 고친 것도 다시 컴파일되면 그대로 보인다. 새 그룹은 Assets/Code/Keywords/그룹이름.cs.
    // 그룹 = 폴더(하위 그룹은 안쪽 클래스), 키워드 = 항목. 그룹 아래에는 이 그룹을 쓰는 필드([Keyword(typeof(그룹))])가 보인다.
    public sealed class KeywordsPage : CodexPage
    {
        public const string PageKey = "keywords";
        public override string Key => PageKey;
        public override string TabLabel => "키워드";
        public override string TabGroup => ClassesPage.DeclareGroup;
        public override int TabOrder => 0;
        public override string TabCount => KeywordTools.AllKeywords().Count().ToString();
        public override string CreateLabel => "+ 새 그룹";

        const string IdsCaption = "여러 개는 줄바꿈·띄어쓰기·쉼표로 나눕니다. 예: common rare legendary";

        public override string Caption(CodexPageContext context)
        {
            var groups = KeywordTools.Groups();
            return $"그룹 {groups.Count}개 · 키워드 {groups.Sum(g => g.Keywords.Count)}개  ·  코드가 원본(저장하면 그룹 코드에 씁니다) · 필드 연결: [Keyword(typeof(그룹))]";
        }

        public override void Create(CodexPageContext context) => NewGroup(context, null);

        public override void Build(CodexPageContext context)
        {
            var list = Gs.Col("gs-grouped__list");
            context.Content.Add(list);
            var groups = KeywordTools.Groups();
            var links = KeywordTools.Fields().ToLookup(f => KeywordTools.GroupOf(f.field));
            var tree = new CodexTree { Key = Key, ItemNoun = "키워드", Query = (context.Search ?? "").Trim() };

            foreach (var group in groups)
            {
                var g = group;
                tree.Folders.Add(new CodexTree.Folder
                {
                    Path = g.Id,
                    Parent = g.Parent,
                    Name = g.DisplayName,
                    Info = $"{g.Name} · 키워드 {g.Keywords.Count}개 · 연결된 필드 {links[g.Id].Count()}개",
                    Tooltip = g.Id,
                    Draggable = false,
                    AcceptsItems = false,
                    AcceptsFolders = false,
                    Badges = titles =>
                    {
                        if (g.Pending) titles.Add(new GsBadge("새 그룹 · 저장 전", Gs.Warning));
                    },
                    Tools = tools =>
                    {
                        tools.Add(new GsButton("+ 키워드", () => AddKeywords(context, g.Id), GsButtonKind.Ghost));
                        tools.Add(new GsButton("+ 하위 그룹", () => NewGroup(context, g.Id), GsButtonKind.Ghost));
                        if (g.Type != null)
                        {
                            var open = new GsButton("cs 열기", () => OpenCode(g.Type), GsButtonKind.Ghost);
                            open.tooltip = KeywordCode.FileOf(g.Type);
                            tools.Add(open);
                        }
                        var menu = new List<GsDropdownItem>
                        {
                            new GsDropdownItem("그룹 지우기", () => RemoveGroup(context, g)) { Detail = "그룹 코드(하위 그룹 포함)를 지웁니다. 이 그룹을 쓰는 필드의 [Keyword]는 직접 고치세요" },
                        };
                        GsButton more = null;
                        more = new GsButton("⋯", () => GsDropdown.Show(more, menu, searchable: false), GsButtonKind.Ghost);
                        tools.Add(more);
                    },
                    Footer = body => Linked(body, links[g.Id].ToList()),
                });
                foreach (var keyword in g.Keywords)
                {
                    var k = keyword;
                    tree.Items.Add(new CodexTree.Item
                    {
                        Key = g.Id + "|" + k.Name,
                        Folder = g.Id,
                        Name = k.Id,
                        SearchText = k.Name,
                        Build = () => Chip(context, g, k),
                        Open = () => Rename(context, g, k),
                        Draggable = false,
                        Payload = k,
                    });
                }
            }
            if (groups.Count == 0)
            {
                var hint = Gs.Label("아직 키워드 그룹이 없습니다. '+ 새 그룹'으로 만들거나, 코드에 [KeywordGroup] static class를 쓰세요. 예: Rarity(common·rare·legendary).", "gs-caption");
                hint.style.whiteSpace = WhiteSpace.Normal;
                list.Add(hint);
            }
            tree.Build(list, context.Host, context.Group);
        }

        static VisualElement Chip(CodexPageContext context, KeywordTools.Group group, KeywordTools.Keyword keyword)
        {
            var row = GsGroupSection.Row(keyword.Id, keyword.Name);
            if (keyword.Pending) row.Add(new GsBadge("저장 전", Gs.Warning));
            var remove = new GsButton("✕", () => KeywordTools.RemoveKeyword(group.Id, keyword.Name), GsButtonKind.Ghost);
            remove.tooltip = "키워드 지우기(저장할 때 코드에서 뺍니다)";
            remove.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            row.Add(remove);
            row.tooltip = $"{group.Name}.{keyword.Name} = \"{keyword.Id}\"\n눌러서 코드의 이름 바꾸기";
            return row;
        }

        // 이 그룹에서 값을 고르는 필드들. 누르면 그 클래스의 상세로 간다.
        static void Linked(VisualElement body, List<(Type type, FieldInfo field)> fields)
        {
            var row = Gs.Row("gs-wrap");
            row.style.marginTop = 4;
            var label = Gs.Label("연결된 필드", "gs-caption");
            label.style.marginRight = 8;
            row.Add(label);
            if (fields.Count == 0)
            {
                var none = Gs.Label("없음 — 필드에 [Keyword(typeof(그룹))]를 붙이거나 클래스 상세에서 연결하세요", "gs-caption");
                none.style.opacity = 0.7f;
                row.Add(none);
            }
            foreach (var (type, field) in fields)
            {
                var linkType = type;
                var linkField = field.Name;
                var chip = new GsBadge($"{type.Name}.{field.Name}", Gs.PaletteFor("class:" + type.Name));
                chip.tooltip = $"{ClassTypes.Label(type)} › {field.Name}  ·  {(KeywordTools.IsMulti(field) ? "여러 개 고름" : "하나 고름")}\n눌러서 클래스 상세 열기";
                chip.style.marginRight = 6;
                chip.style.marginBottom = 4;
                chip.RegisterCallback<ClickEvent>(e =>
                {
                    e.StopPropagation();
                    ClassDetail.Open(linkType, linkField);
                });
                row.Add(chip);
            }
            body.Add(row);
        }

        static void OpenCode(Type type)
        {
            var file = KeywordCode.FileOf(type);
            if (file != null) AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<MonoScript>(file));
        }

        // ── 만들기·고치기(저장할 때 코드에 쓴다) ─────────────────

        public static void NewGroup(CodexPageContext context, string parent, Action<string> done = null) =>
            NewGroup(context.Host, parent, id =>
            {
                context.Refresh?.Invoke();
                done?.Invoke(id);
            });

        public static void NewGroup(VisualElement host, string parent, Action<string> done)
        {
            GsDialog.Form(host, string.IsNullOrEmpty(parent) ? "새 키워드 그룹" : $"'{parent}' 안에 하위 그룹",
                string.IsNullOrEmpty(parent) ? $"{KeywordCode.Folder}/그룹이름.cs 파일로 만들어집니다(저장할 때)." : "상위 그룹 코드 안에 안쪽 클래스로 만들어집니다(저장할 때).",
                new[]
                {
                    new DialogField { Key = "name", Label = "그룹 이름(코드)", Value = "", Caption = "대문자로 시작하는 영문(예: Rarity, ItemTag). 코드에서 Rarity.Rare처럼 씁니다." },
                    new DialogField { Key = "label", Label = "표시 이름 (선택)", Value = "", Caption = "DB에 보일 이름(예: 등급)" },
                    new DialogField { Key = "ids", Label = "키워드 ID (선택)", Value = "", Caption = IdsCaption, Multiline = true },
                },
                "만들기",
                values =>
                {
                    var name = values["name"].Trim();
                    if (!KeywordCode.IsValidName(name, out var error)) return "그룹 이름: " + error;
                    var id = ClassStaging.GroupId(parent, name);
                    if (KeywordTools.FindGroup(id) != null) return $"'{name}' 그룹이 이미 있습니다.";
                    return KeywordTools.CheckNewIds(CodexGroups.ParseIds(values["ids"]));
                },
                values =>
                {
                    var id = KeywordTools.CreateGroup(parent, values["name"].Trim(), values["label"].Trim());
                    KeywordTools.AddKeywords(id, CodexGroups.ParseIds(values["ids"]));
                    done?.Invoke(id);
                });
        }

        public static void AddKeywords(CodexPageContext context, string group) =>
            AddKeywords(context.Host, group, _ => context.Refresh?.Invoke());

        public static void AddKeywords(VisualElement host, string group, Action<List<string>> done)
        {
            GsDialog.Form(host, $"'{KeywordTools.FindGroup(group)?.DisplayName ?? group}'에 키워드 추가",
                "필드에는 키워드 ID가 저장됩니다. 코드에는 ID로 만든 상수 이름으로 들어갑니다(예: legendary → Legendary).",
                new[] { new DialogField { Key = "ids", Label = "키워드 ID", Value = "", Caption = IdsCaption, Multiline = true } },
                "추가",
                values =>
                {
                    var ids = CodexGroups.ParseIds(values["ids"]);
                    return ids.Count == 0 ? "키워드 ID를 하나 이상 입력하세요." : KeywordTools.CheckNewIds(ids);
                },
                values => done?.Invoke(KeywordTools.AddKeywords(group, CodexGroups.ParseIds(values["ids"]))));
        }

        static void Rename(CodexPageContext context, KeywordTools.Group group, KeywordTools.Keyword keyword)
        {
            GsDialog.Form(context.Host, $"'{keyword.Id}' 코드 이름 바꾸기",
                $"코드의 상수 이름만 바뀝니다({group.Name}.{keyword.Name} → {group.Name}.새 이름). 저장되는 키워드 ID('{keyword.Id}')는 그대로라 데이터가 이어집니다. 이 상수를 쓰는 게임 코드는 직접 고쳐야 합니다(컴파일 오류로 알려 줍니다).",
                new[] { new DialogField { Key = "name", Label = "새 이름", Value = keyword.Name } },
                "바꾸기",
                values =>
                {
                    var name = values["name"].Trim();
                    if (name == keyword.Name) return "새 이름을 입력하세요.";
                    if (!KeywordCode.IsValidName(name, out var error)) return error;
                    return group.Keywords.Any(k => k.Name == name) ? $"'{name}'이(가) 이미 있습니다." : null;
                },
                values =>
                {
                    KeywordTools.RenameKeyword(group.Id, keyword.Name, values["name"].Trim());
                    context.Refresh?.Invoke();
                });
        }

        static void RemoveGroup(CodexPageContext context, KeywordTools.Group group)
        {
            var linked = KeywordTools.FieldsLinkedTo(group.Id).Count();
            GsDialog.Confirm(context.Host, "키워드 그룹 지우기",
                $"'{group.DisplayName}' 그룹의 코드(키워드 {group.Keywords.Count}개, 하위 그룹 포함)를 저장할 때 지웁니다." +
                (linked > 0 ? $" 이 그룹을 쓰는 필드 {linked}개의 [Keyword(typeof({group.Name}))]는 컴파일 오류가 나므로 직접 고치세요." : ""),
                "지우기", () =>
                {
                    KeywordTools.RemoveGroup(group.Id);
                    context.Refresh?.Invoke();
                });
        }
    }
}
