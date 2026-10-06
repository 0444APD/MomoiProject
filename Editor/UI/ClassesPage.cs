using System.Linq;
using UnityEditor;
using UnityEngine.UIElements;

namespace GBible.EditorTools.UI
{
    // DB의 클래스 탭: AI가 쓴 코드(Assets/MomoiProject/Code) 안의 클래스(아이템·유닛 …)와 DB 종류를 본다(읽기 전용).
    // 클래스는 AI가 코드(C# 파일)로 만들고 고친다. 이 탭은 결과를 확인하는 곳: 눌러서 필드의 이름·형식·키워드 연결을 보고,
    // 다른 클래스 형식으로 이동하거나 스크립트를 연다. 코드 폴더의 경로·구조는 보이지 않는다(이름순 한 목록 + 기본 묶음).
    // 목록 모양·조작은 모든 DB 탭 공용(CodexTree).
    public class ClassesPage : CodexPage
    {
        public const string PageKey = "classes";
        public const string DeclareGroup = "요소";

        public override string Key => PageKey;
        public override string TabLabel => "클래스";
        public override string TabGroup => DeclareGroup;
        public override int TabOrder => 1;
        public override string TabCount => Shown().Count(c => !c.AutoGroup).ToString();

        protected virtual string Tab => ClassRegistry.ClassTab;
        protected virtual string Noun => "클래스";
        protected virtual string EmptyHint =>
            "클래스는 AI가 코드로 만듭니다. 다시 컴파일한 뒤 여기에 보입니다.";

        const string AutoPrefix = "$auto:";

        // 만들기는 코드에서.
        public override string CreateLabel => null;

        // 목록에 보이는 것: AI 코드 폴더 안의 것(밖의 스크립트·플러그인은 보이지 않는다) + 이 라이브러리의 기본 묶음.
        // 탭 개수·제목 아래 개수·목록이 모두 이것을 센다.
        System.Collections.Generic.List<ClassEntry> Shown() =>
            ClassCatalog.All(Tab).Where(c => c.AutoGroup || (c.Group != null && c.Group != ClassFolders.Outside)).ToList();

        public override string Caption(CodexPageContext context) =>
            $"{Noun} {Shown().Count(c => !c.AutoGroup)}개  ·  AI가 코드로 만들고 고칩니다 · 눌러서 필드 보기";

        public override void Create(CodexPageContext context)
        {
        }

        public override void Build(CodexPageContext context)
        {
            var list = Gs.Col("gs-grouped__list");
            context.Content.Add(list);
            var classes = Shown();
            var tree = new CodexTree { Key = Key, ItemNoun = Noun, Query = (context.Search ?? "").Trim() };

            tree.Folders.Add(new CodexTree.Folder
            {
                Path = "", Parent = null, Name = TabLabel, Info = $"{Noun} {classes.Count(c => !c.AutoGroup)}개",
                Draggable = false, AcceptsItems = false, AcceptsFolders = false, Hint = EmptyHint,
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
                    Folder = e.AutoGroup ? AutoPrefix + e.Group : "",
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
            if (entry.Kind == ClassKind.Codex) row.Add(new GsBadge("DB 종류", Gs.PaletteFor("kind:codex")));
            if (entry.Locked) row.Add(new GsBadge("시스템", Gs.PaletteFor("locked")));
            row.tooltip = entry.FullName + "\n눌러서 필드 보기";
            return row;
        }
    }

    // DB의 이벤트 탭: [GameEvent] 클래스를 폴더별로 본다(읽기 전용, 코드에서 만든다). 클래스 탭과 같은 폴더를 쓴다.
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
