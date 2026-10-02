using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine.UIElements;

namespace GBible.EditorTools.UI
{
    // 도감의 클래스 탭: 개발자가 만든 데이터 클래스·도감 종류를 폴더별로 보고, 새 클래스를 만들고, 눌러서 필드와 연결을 고친다.
    // 코드(C# 파일)가 원본이다. 여기서 만든 클래스는 실제 스크립트 파일로 생기고, 프로그래머가 코드에서 고친 것도 다시 컴파일되면 그대로 보인다.
    // 폴더 = Assets/Code 아래 폴더(중첩 가능, 동기화). 폴더를 만들고 지우고, 클래스·폴더를 끌어 다른 폴더에 놓으면 저장(Ctrl+S)할 때 디스크가 그렇게 바뀐다.
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
            "폴더는 Assets/Code 아래의 폴더입니다. '+ 데이터 클래스'(인벤토리처럼 값을 담는 클래스)나 '+ 도감 종류'(아이템처럼 도감에 항목을 만드는 클래스)로 클래스를 더하고, '+ 하위 폴더'로 나누세요.";

        const string AutoPrefix = "$auto:";

        public override string CreateLabel => "+ 새 폴더";

        public override string Caption(CodexPageContext context)
        {
            var classes = ClassCatalog.All(Tab);
            return $"폴더 {ClassFolders.Groups().Count}개 · {Noun} {classes.Count}개  ·  폴더 = {ClassFolders.Root} 아래 폴더 · 줄을 눌러 접고 펼치고, 끌어서 다른 폴더에 놓아 옮깁니다 · ↗ 새 창에서 열기";
        }

        public override void Create(CodexPageContext context) => NewGroup(context, "");

        public override void Build(CodexPageContext context)
        {
            var root = context.Content;
            if (ClassStaging.HasChanges)
            {
                var pending = new GsNotice($"코드 변경 {ClassStaging.All.Count}건이 저장을 기다립니다. 저장(Ctrl+S)하면 스크립트 파일·폴더에 쓰고 다시 컴파일합니다.", GsNoticeKind.Warning);
                pending.style.marginBottom = 12;
                root.Add(pending);
            }

            var list = Gs.Col("gs-grouped__list");
            root.Add(list);
            var classes = ClassCatalog.All(Tab);
            var groups = ClassFolders.Groups();
            var tree = new CodexTree { Key = Key, ItemNoun = Noun, Query = (context.Search ?? "").Trim() };

            // Code 폴더(맨 위)와 그 아래 폴더들.
            tree.Folders.Add(FolderNode(context, "", null, classes, groups));
            foreach (var group in groups) tree.Folders.Add(FolderNode(context, group, ClassFolders.Parent(group), classes, groups));

            // Code 폴더 밖에 있는 개발자 클래스: 끌어서 Code 안으로 옮길 수 있다(받지는 않는다).
            var outside = classes.Count(c => !c.AutoGroup && c.Group == ClassFolders.Outside);
            if (outside > 0)
                tree.Folders.Add(new CodexTree.Folder
                {
                    Path = ClassFolders.Outside, Parent = null, Name = "Code 폴더 밖",
                    Info = $"{Noun} {outside}개 · 끌어서 {ClassFolders.Root} 안의 폴더에 놓으면 그리로 옮겨집니다",
                    Draggable = false, AcceptsItems = false, AcceptsFolders = false,
                });

            // 자동 묶음(이 라이브러리가 기본으로 내는 이벤트·기능의 클래스): 옮길 수 없다.
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
                    Draggable = !e.AutoGroup && !e.Locked && !e.PendingDelete,
                    Payload = e,
                });
            }

            tree.MoveItem = (item, folder) => ClassStaging.MoveClass(item.Key, folder.Path);
            tree.MoveFolder = (folder, target) => MoveGroup(context, folder.Path, target.Path, groups);
            tree.Build(list, context.Host, context.Group);
        }

        CodexTree.Folder FolderNode(CodexPageContext context, string group, string parent, List<ClassEntry> classes, List<string> groups) =>
            new CodexTree.Folder
            {
                Path = group,
                Parent = parent,
                Name = group == "" ? "Code" : ClassFolders.Name(group),
                Tooltip = ClassFolders.FolderOf(group),
                Draggable = group != "",
                Hint = group == "" ? EmptyHint : null,
                Tools = tools =>
                {
                    foreach (var (label, kind) in CreateKinds())
                    {
                        var k = kind;
                        tools.Add(new GsButton(label, () => NewClass(context, group, k), GsButtonKind.Ghost));
                    }
                    var sub = new GsButton("+ 하위 폴더", () => NewGroup(context, group), GsButtonKind.Ghost);
                    sub.tooltip = $"{ClassFolders.FolderOf(group)} 안에 폴더를 만듭니다";
                    tools.Add(sub);
                    if (context.Group != group)
                    {
                        var open = new GsButton("↗", () => CodexGroupWindow.Open(Key, group, $"{TabLabel} · {(group == "" ? "Code" : group)}"), GsButtonKind.Ghost);
                        open.tooltip = "새 창에서 열기";
                        tools.Add(open);
                    }
                    var menu = new List<GsDropdownItem>
                    {
                        new GsDropdownItem("프로젝트 창에서 보기", () => EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ClassFolders.FolderOf(group)))),
                    };
                    if (group != "")
                        menu.Add(new GsDropdownItem("폴더 지우기", () => RemoveGroup(context, classes, groups, group)) { Detail = "폴더를 지웁니다. 안의 클래스·하위 폴더는 지우지 않고 한 단계 위로 옮깁니다" });
                    GsButton more = null;
                    more = new GsButton("⋯", () => GsDropdown.Show(more, menu, searchable: false), GsButtonKind.Ghost);
                    more.tooltip = "폴더 메뉴";
                    tools.Add(more);
                },
            };

        protected virtual IEnumerable<(string label, ClassKind kind)> CreateKinds()
        {
            yield return ("+ 데이터 클래스", ClassKind.Data);
            yield return ("+ 도감 종류", ClassKind.Codex);
        }

        // 클래스 한 줄: 이름 · 표시 이름 · 필드 수, 오른쪽에 종류·적용 전 배지.
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
            if (entry.Locked) row.Add(new GsBadge("잠김", Gs.PaletteFor("locked")));
            if (entry.PendingNew) row.Add(new GsBadge("새 클래스 · 적용 전", Gs.Warning));
            else if (entry.PendingDelete) row.Add(new GsBadge("삭제 · 적용 전", Gs.Danger));
            else if (ClassStaging.For(entry.FullName).Any()) row.Add(new GsBadge("변경 · 적용 전", Gs.Warning));
            row.tooltip = entry.FullName + (entry.Locked ? "\n" + entry.LockReason : "") + "\n눌러서 필드·연결 보기" + (entry.Locked || entry.AutoGroup ? "" : " · 끌어서 다른 폴더로 옮기기");
            return row;
        }

        void NewClass(CodexPageContext context, string group, ClassKind kind)
        {
            var needsLabel = kind != ClassKind.Data;
            var fields = new List<DialogField>
            {
                new DialogField { Key = "name", Label = "클래스 이름", Value = "", Caption = "영문 대문자로 시작(예: Item, PlayerInventory). 스크립트 파일 이름이 됩니다." },
            };
            if (needsLabel)
                fields.Add(new DialogField
                {
                    Key = "label", Label = "표시 이름", Value = "",
                    Caption = kind == ClassKind.Codex ? "도감 사이드바의 탭 이름(예: 아이템). 비우면 클래스 이름." : "이벤트 목록에 보일 이름(예: 적 처치). 비우면 클래스 이름.",
                });
            var settings = ClassRegistry.Current;
            var where = $"{ClassFolders.FolderOf(group)}/ 에 스크립트 파일로 만들어집니다" + (string.IsNullOrEmpty(settings.codeNamespace) ? "." : $"(네임스페이스 {settings.codeNamespace}).");
            var what = kind == ClassKind.Codex ? "도감 종류는 자기 탭이 생기고, 그 탭에서 항목을 만듭니다."
                : kind == ClassKind.Event ? "이벤트는 게임 코드가 발생시키고, 로그의 반영 규칙과 모니터에서 고릅니다."
                : "데이터 클래스는 플레이어 데이터의 값 형식·템플릿·다른 클래스의 필드로 씁니다.";
            GsDialog.Form(context.Host, $"새 {ClassStaging.KindText(kind.ToString())}", $"{what} {where} 저장(Ctrl+S)할 때 적용됩니다.", fields, "만들기",
                values => ClassCatalog.CanCreate(values["name"].Trim(), out var error) ? null : "클래스 이름: " + error,
                values =>
                {
                    var fullName = ClassRegistry.FullName(values["name"].Trim());
                    ClassStaging.CreateClass(fullName, kind, needsLabel ? values["label"].Trim() : "", group, Tab);
                    context.Refresh?.Invoke();
                    ClassDetail.Open(fullName);
                });
        }

        static void NewGroup(CodexPageContext context, string parent)
        {
            var where = ClassFolders.FolderOf(parent);
            GsDialog.Form(context.Host, string.IsNullOrEmpty(parent) ? "새 폴더" : $"'{parent}' 안에 하위 폴더", $"{where}/ 안에 폴더를 만듭니다. 저장(Ctrl+S)할 때 적용됩니다.",
                new[] { new DialogField { Key = "id", Label = "폴더 이름", Value = "", Caption = "예: 전투, 인벤토리" } },
                "만들기",
                values => ClassFolders.CanCreate(parent, values["id"], out var error) ? null : error,
                values =>
                {
                    ClassStaging.CreateGroup(ClassFolders.Join(parent, values["id"].Trim()));
                    context.Refresh?.Invoke();
                });
        }

        // 폴더를 다른 폴더 안으로 옮긴다(같은 이름이 있으면 막는다).
        static void MoveGroup(CodexPageContext context, string group, string target, List<string> groups)
        {
            var to = ClassFolders.Join(target, ClassFolders.Name(group));
            if (groups.Any(g => string.Equals(g, to, StringComparison.OrdinalIgnoreCase)))
            {
                GsDialog.Show(context.Host, "옮길 수 없습니다", $"'{ClassFolders.FolderOf(target)}' 안에 '{ClassFolders.Name(group)}' 폴더가 이미 있습니다.", null,
                    new DialogButton("확인", GsButtonKind.Primary, null));
                return;
            }
            ClassStaging.MoveGroup(group, to);
        }

        void RemoveGroup(CodexPageContext context, List<ClassEntry> classes, List<string> groups, string group)
        {
            var count = classes.Count(c => !c.AutoGroup && ClassFolders.IsUnder(c.Group, group));
            var subgroups = groups.Count(g => g != group && ClassFolders.IsUnder(g, group));
            var parent = ClassFolders.Parent(group);
            GsDialog.Confirm(context.Host, "폴더 지우기",
                $"'{group}' 폴더({ClassFolders.FolderOf(group)})를 지웁니다. 안의 {Noun} {count}개와 하위 폴더 {subgroups}개는 지우지 않고 " +
                $"'{ClassDetail.GroupLabel(parent)}'(으)로 옮깁니다. 폴더 안의 다른 파일도 함께 옮깁니다. 저장(Ctrl+S)할 때 적용됩니다.", "지우기", () =>
                {
                    ClassStaging.DeleteGroup(group);
                    context.Refresh?.Invoke();
                });
        }
    }

    // 도감의 이벤트 탭: [GameEvent] 클래스를 폴더별로 보고 만든다. 클래스 탭과 같은 폴더를 쓴다.
    public sealed class EventsPage : ClassesPage
    {
        public new const string PageKey = "events";

        public override string Key => PageKey;
        public override string TabLabel => "이벤트";
        public override int TabOrder => 3;

        protected override string Tab => ClassRegistry.EventTab;
        protected override string Noun => "이벤트";
        protected override string EmptyHint =>
            "이벤트는 게임에서 일어난 일(적 처치, 골드 획득 …)을 알리는 클래스입니다. '+ 이벤트'로 더하고, 필드에 함께 알릴 값을 둡니다. 폴더는 클래스 탭과 같은 Assets/Code 아래 폴더입니다.";

        protected override IEnumerable<(string label, ClassKind kind)> CreateKinds()
        {
            yield return ("+ 이벤트", ClassKind.Event);
        }
    }
}
