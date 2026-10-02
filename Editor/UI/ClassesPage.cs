using System.Linq;
using UnityEditor;
using UnityEngine.UIElements;

namespace GBible.EditorTools.UI
{
    // 도감의 클래스 탭: 프로젝트가 코드로 정의한 클래스(아이템·유닛 …)와 도감 종류를 폴더별로 본다(읽기 전용).
    // 클래스는 코드(C# 파일)에서 만들고 고친다. 이 탭은 결과를 확인하는 곳: 눌러서 필드의 이름·형식·키워드 연결을 보고,
    // 다른 클래스 형식으로 이동하거나 스크립트를 연다. 폴더 = Assets/Code 아래 실제 폴더.
    // 목록 모양·조작은 모든 도감 탭 공용(CodexTree).
    public class ClassesPage : CodexPage
    {
        public const string PageKey = "classes";
        public const string DeclareGroup = "선언";

        public override string Key => PageKey;
        public override string TabLabel => "클래스";
        public override string TabGroup => DeclareGroup;
        public override int TabOrder => 1;
        public override string TabCount => ClassCatalog.All(Tab).Count.ToString();

        protected virtual string Tab => ClassRegistry.ClassTab;
        protected virtual string Noun => "클래스";
        protected virtual string EmptyHint =>
            "클래스는 코드에서 만듭니다. [Serializable] 클래스를 Assets/Code 아래에 쓰면 다시 컴파일한 뒤 여기에 보입니다.";

        const string AutoPrefix = "$auto:";

        // 만들기는 코드에서.
        public override string CreateLabel => null;

        public override string Caption(CodexPageContext context)
        {
            var classes = ClassCatalog.All(Tab);
            return $"폴더 {ClassFolders.Groups().Count}개 · {Noun} {classes.Count}개  ·  코드에서 만들고 고칩니다 · 눌러서 필드 보기 · ↗ 새 창에서 열기";
        }

        public override void Create(CodexPageContext context)
        {
        }

        public override void Build(CodexPageContext context)
        {
            var list = Gs.Col("gs-grouped__list");
            context.Content.Add(list);
            var classes = ClassCatalog.All(Tab);
            var groups = ClassFolders.Groups();
            var tree = new CodexTree { Key = Key, ItemNoun = Noun, Query = (context.Search ?? "").Trim() };

            tree.Folders.Add(FolderNode(context, "", null));
            foreach (var group in groups) tree.Folders.Add(FolderNode(context, group, ClassFolders.Parent(group)));

            var outside = classes.Count(c => !c.AutoGroup && c.Group == ClassFolders.Outside);
            if (outside > 0)
                tree.Folders.Add(new CodexTree.Folder
                {
                    Path = ClassFolders.Outside, Parent = null, Name = "Code 폴더 밖", Info = $"{Noun} {outside}개",
                    Draggable = false, AcceptsItems = false, AcceptsFolders = false,
                });

            // 자동 묶음(이 라이브러리가 기본으로 내는 이벤트·기능의 클래스).
            foreach (var auto in classes.Where(c => c.AutoGroup).Select(c => c.Group).Distinct())
                tree.Folders.Add(new CodexTree.Folder
                {
                    Path = AutoPrefix + auto, Parent = null, Name = auto, Info = "항상 있는 기본 " + Noun,
                    Badges = titles => titles.Add(new GsBadge("자동", Gs.PaletteFor("auto"))),
                    Draggable = false, AcceptsItems = false, AcceptsFolders = false,
                });

            foreach (var entry in classes)
            {
                var e = entry;
                tree.Items.Add(new CodexTree.Item
                {
                    Key = e.FullName,
                    Folder = e.AutoGroup ? AutoPrefix + e.Group : e.Group ?? "",
                    Name = e.Name,
                    SearchText = e.Label,
                    Build = () => Chip(e),
                    Open = () => ClassDetail.Open(e.FullName),
                    Draggable = false,
                    Payload = e,
                });
            }
            tree.Build(list, context.Host, context.Group);
        }

        CodexTree.Folder FolderNode(CodexPageContext context, string group, string parent) =>
            new CodexTree.Folder
            {
                Path = group,
                Parent = parent,
                Name = group == "" ? "Code" : ClassFolders.Name(group),
                Tooltip = ClassFolders.FolderOf(group),
                Draggable = false,
                AcceptsItems = false,
                AcceptsFolders = false,
                Hint = group == "" ? EmptyHint : null,
                Tools = tools =>
                {
                    if (context.Group != group)
                    {
                        var open = new GsButton("↗", () => CodexGroupWindow.Open(Key, group, $"{TabLabel} · {(group == "" ? "Code" : group)}"), GsButtonKind.Ghost);
                        open.tooltip = "새 창에서 열기";
                        tools.Add(open);
                    }
                    var ping = new GsButton("프로젝트 창", () => EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ClassFolders.FolderOf(group))), GsButtonKind.Ghost);
                    ping.tooltip = "프로젝트 창에서 이 폴더 보기";
                    tools.Add(ping);
                },
            };

        // 클래스 한 줄: 이름 · 표시 이름 · 필드 수, 오른쪽에 종류 배지.
        static VisualElement Chip(ClassEntry entry)
        {
            var caption = !string.IsNullOrEmpty(entry.Label) && entry.Label != entry.Name ? entry.Label : "";
            if (entry.Type != null)
            {
                var fields = ClassTypes.FieldsOf(entry.Type).Count(f => f.DeclaringType != typeof(GBible.Data.GameData));
                caption += (caption.Length > 0 ? "  ·  " : "") + $"필드 {fields}개";
            }
            var row = GsGroupSection.Row(entry.Name, caption);
            if (entry.Kind == ClassKind.Codex) row.Add(new GsBadge("도감 종류", Gs.PaletteFor("kind:codex")));
            if (entry.Locked) row.Add(new GsBadge("시스템", Gs.PaletteFor("locked")));
            row.tooltip = entry.FullName + "\n눌러서 필드 보기";
            return row;
        }
    }

    // 도감의 이벤트 탭: [GameEvent] 클래스를 폴더별로 본다(읽기 전용, 코드에서 만든다). 클래스 탭과 같은 폴더를 쓴다.
    // 에디터에서 이벤트를 쏘는 곳은 모니터(플레이 중)의 이벤트 탭이다.
    public sealed class EventsPage : ClassesPage
    {
        public new const string PageKey = "events";

        public override string Key => PageKey;
        public override string TabLabel => "이벤트";
        public override int TabOrder => 3;

        protected override string Tab => ClassRegistry.EventTab;
        protected override string Noun => "이벤트";
        protected override string EmptyHint =>
            "이벤트는 게임에서 일어난 일(적 처치, 골드 획득 …)을 알리는 클래스입니다. 코드에서 [GameEvent] 클래스로 만들고, 필드에 함께 알릴 값을 둡니다.";
    }
}
