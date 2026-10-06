using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine.UIElements;

namespace GBible.EditorTools.UI
{
    // DB의 키워드 탭: 코드에 정의된 키워드 그룹([KeywordGroup] static class)과 키워드(const string)를 본다(읽기 전용).
    // 코드가 원본이고 AI가 코드에서 만든다(관례: Assets/MomoiProject/Code/Keywords/그룹이름.cs). 다시 컴파일되면 그대로 보인다.
    // 그룹 = 폴더(하위 그룹은 안쪽 클래스), 키워드 = 항목. 그룹 아래에는 이 그룹을 쓰는 필드([Keyword(typeof(그룹))])가 보인다.
    public sealed class KeywordsPage : CodexPage
    {
        public const string PageKey = "keywords";
        public override string Key => PageKey;
        public override string TabLabel => "키워드";
        public override string TabGroup => ClassesPage.DeclareGroup;
        public override int TabOrder => 0;
        public override string TabCount => KeywordTools.AllKeywords().Count().ToString();
        // 만들기는 코드에서.
        public override string CreateLabel => null;

        public override string Caption(CodexPageContext context)
        {
            var groups = KeywordTools.Groups();
            return $"그룹 {groups.Count}개 · 키워드 {groups.Sum(g => g.Keywords.Count)}개  ·  코드가 원본(만들기·바꾸기는 AI에게) · 필드 연결: [Keyword(typeof(그룹))]";
        }

        public override void Create(CodexPageContext context)
        {
        }

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
                    Tools = tools =>
                    {
                        if (g.Type == null) return;
                        var open = new GsButton("cs 열기", () => OpenCode(g.Type), GsButtonKind.Ghost);
                        open.tooltip = KeywordTools.FileOf(g.Type);
                        tools.Add(open);
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
                        Build = () => Chip(g, k),
                        Draggable = false,
                        Payload = k,
                    });
                }
            }
            if (groups.Count == 0)
            {
                var hint = Gs.Label("아직 키워드 그룹이 없습니다. 키워드는 코드의 [KeywordGroup] static class입니다 — 필요하면 AI에게 만들어 달라고 하세요. 예: Rarity(common·rare·legendary).", "gs-caption");
                hint.style.whiteSpace = WhiteSpace.Normal;
                list.Add(hint);
            }
            tree.Build(list, context.Host, context.Group);
        }

        static VisualElement Chip(KeywordTools.Group group, KeywordTools.Keyword keyword)
        {
            var row = GsGroupSection.Row(keyword.Id, keyword.Name);
            row.tooltip = $"{group.Name}.{keyword.Name} = \"{keyword.Id}\"";
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
                var none = Gs.Label("없음 — 코드에서 필드에 [Keyword(typeof(그룹))]를 붙이면 연결됩니다", "gs-caption");
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
            var file = KeywordTools.FileOf(type);
            if (file != null) AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<MonoScript>(file));
        }
    }
}
