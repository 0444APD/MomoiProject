using System;
using System.Collections.Generic;
using System.Linq;
using GBible.Data;
using UnityEditor;
using UnityEngine.UIElements;

namespace GBible.EditorTools.UI
{
    // 전용 화면이 없는 도감 종류(로그·도전과제·템플릿·능력·플레이어 데이터·프리팹·개발자가 만든 도감 종류 …)의 목록.
    // 모양·조작은 클래스 탭과 같다(CodexTree): 폴더(중첩) 안에 항목이 이름순으로, 눌러서 상세, 끌어서 다른 폴더로.
    // 폴더는 정리용이다(게임 동작·디스크와 상관없음). 항목의 폴더는 항목 자신(GameData.Folder)에, 빈 폴더는 CodexFolders에 둔다.
    // 바꾼 것은 모두 도감의 다른 변경처럼 저장(Ctrl+S)할 때 적용한다.
    public sealed class DataListPage : CodexPage
    {
        public const string Prefix = "data:";

        readonly Type _type;

        public DataListPage(Type type)
        {
            _type = type;
        }

        public Type Type => _type;
        public override string Key => Prefix + _type.FullName;
        public override string CreateLabel => "+ 새로 만들기";

        string Tab => _type.FullName;
        string Noun => DataTypes.Find(_type)?.DisplayName ?? _type.Name;

        static EditorDatabase Db => EditorDatabase.Instance;
        static CodexStaging Staging => EditorDatabase.Staging;

        List<GameData> AllItems() => Db.OfType(_type).ToList();

        public override string Caption(CodexPageContext context)
        {
            var items = AllItems();
            var errors = context.Issues.Count(i => i.Severity == ValidationSeverity.Error && i.Asset != null && i.Asset.GetType() == _type);
            return $"{items.Count}개 항목 · 폴더 {CodexFolders.All(Tab, items).Count}개" + (errors > 0 ? $" · 오류 {errors}" : "") +
                   "  ·  줄을 눌러 열고, 끌어서 다른 폴더에 놓아 옮깁니다 · 폴더는 정리용입니다(게임 동작과 상관없음)";
        }

        public override void Create(CodexPageContext context) => NewItem(context, "");

        public override void Build(CodexPageContext context)
        {
            var list = Gs.Col("gs-grouped__list");
            context.Content.Add(list);
            var items = AllItems();
            var folders = CodexFolders.All(Tab, items);
            var search = (context.Search ?? "").Trim();
            var tree = new CodexTree { Key = Key, ItemNoun = "항목" };
            var tokens = search.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            // "invalid": 오류가 있는 항목만. 그 밖의 글자는 ID에서 찾는다(폴더 이름이 걸리면 그 안 전체).
            if (tokens.Any(t => t.Equals("invalid", StringComparison.OrdinalIgnoreCase)))
                tree.Filter = item => tokens.All(t => t.Equals("invalid", StringComparison.OrdinalIgnoreCase)
                    ? HasError(context, (GameData)item.Payload)
                    : item.Name.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
            else tree.Query = search;

            tree.Folders.Add(FolderNode(context, "", null, items, folders));
            foreach (var folder in folders) tree.Folders.Add(FolderNode(context, folder, CodexFolders.Parent(folder), items, folders));

            var view = ViewSettings.Resolve(_type);
            foreach (var item in items)
            {
                var it = item;
                tree.Items.Add(new CodexTree.Item
                {
                    Key = it.Id,
                    Folder = it.Folder,
                    Name = it.Id,
                    Build = () => Row(context, it, view),
                    Open = () => context.OpenDetail(it),
                    Payload = it,
                });
            }

            tree.MoveItem = (item, folder) =>
            {
                Staging.Work((GameData)item.Payload).EditorAssignFolder(folder.Path);
                Staging.Touch();
            };
            tree.MoveFolder = (folder, target) => Move(context, folder.Path, CodexFolders.Join(target.Path, CodexFolders.Name(folder.Path)));
            tree.Build(list, context.Host, context.Group);
        }

        static bool HasError(CodexPageContext context, GameData item) =>
            context.Issues.Any(i => i.Asset == item && i.Severity == ValidationSeverity.Error);

        CodexTree.Folder FolderNode(CodexPageContext context, string path, string parent, List<GameData> items, List<string> folders) =>
            new CodexTree.Folder
            {
                Path = path,
                Parent = parent,
                Name = path == "" ? Noun : CodexFolders.Name(path),
                Draggable = path != "",
                Hint = path == "" ? $"'+ {Noun}'로 항목을, '+ 하위 폴더'로 폴더를 만듭니다. 항목·폴더를 끌어 폴더에 놓아 정리합니다." : null,
                Tools = tools =>
                {
                    tools.Add(new GsButton($"+ {Noun}", () => NewItem(context, path), GsButtonKind.Ghost));
                    tools.Add(new GsButton("+ 하위 폴더", () => NewFolder(context, path, items), GsButtonKind.Ghost));
                    if (context.Group != path)
                    {
                        var open = new GsButton("↗", () => CodexGroupWindow.Open(Key, path, $"{Noun} · {(path == "" ? "전체" : path)}"), GsButtonKind.Ghost);
                        open.tooltip = "새 창에서 열기";
                        tools.Add(open);
                    }
                    if (path == "") return;
                    var menu = new List<GsDropdownItem>
                    {
                        new GsDropdownItem("이름 바꾸기", () => RenameFolder(context, path, folders)),
                        new GsDropdownItem("폴더 지우기", () => DeleteFolder(context, path, items)) { Detail = "폴더만 지웁니다. 안의 항목·하위 폴더는 한 단계 위로 옮깁니다" },
                    };
                    GsButton more = null;
                    more = new GsButton("⋯", () => GsDropdown.Show(more, menu, searchable: false), GsButtonKind.Ghost);
                    more.tooltip = "폴더 메뉴";
                    tools.Add(more);
                },
            };

        // 항목 한 줄: ID · 부제목(보기 설정의 부제목 필드), 오른쪽에 오류·적용 전 배지.
        static VisualElement Row(CodexPageContext context, GameData item, ResolvedView view)
        {
            string caption = null;
            if (view.Subtitle != null)
            {
                var property = new SerializedObject(item).FindProperty(view.Subtitle.Path);
                if (property != null) caption = PropertyUtil.Summary(property);
            }
            var row = GsGroupSection.Row(item.Id, caption);
            if (HasError(context, item))
            {
                row.AddToClassList("gs-group__row--error");
                row.Add(new GsBadge("오류", Gs.Danger));
            }
            if (Staging.IsPending(item)) row.Add(new GsBadge(Staging.IsAdded(item) ? "새 항목 · 적용 전" : "변경 · 적용 전", Gs.Warning));
            row.tooltip = item.Id + "\n눌러서 편집 · 끌어서 다른 폴더로 옮기기";
            return row;
        }

        // ── 만들기 ─────────────────────────────────────────────

        void NewItem(CodexPageContext context, string folder)
        {
            var info = DataTypes.Find(_type);
            if (info == null) return;
            // 이름(ID)은 여기서 한 번 짓는다. 이후 모든 곳에서는 목록에서 고른다.
            GsDialog.Form(context.Host, $"새 {info.DisplayName}" + (folder == "" ? "" : $" · {folder}"),
                "ID는 저장 파일·코드(GameIds)·조건이 함께 쓰는 식별자입니다. 나중에 바꿔도 참조가 모두 따라갑니다.",
                new[]
                {
                    new DialogField { Key = "id", Label = "ID", Value = Db.NextId(info), Caption = "영문 소문자로 시작, 단어 사이는 대문자(띄어쓰기·밑줄 없음), 분류는 점(.)으로. 예: playerGold, player.inventory" },
                },
                "만들기",
                values => Db.CanUseId(values["id"], null, out var error) ? null : error,
                values =>
                {
                    var created = Staging.Add(_type, values["id"]);
                    created.EditorAssignFolder(folder);
                    Staging.Touch();
                    context.Refresh?.Invoke();
                    context.OpenDetail(created);
                });
        }

        void NewFolder(CodexPageContext context, string parent, List<GameData> items)
        {
            GsDialog.Form(context.Host, parent == "" ? "새 폴더" : $"'{parent}' 안에 하위 폴더", "정리용 폴더입니다(게임 동작과 상관없음). 저장(Ctrl+S)할 때 적용됩니다.",
                new[] { new DialogField { Key = "name", Label = "폴더 이름", Value = "", Caption = "예: 전투, 보스, 튜토리얼" } },
                "만들기",
                values => CodexFolders.CanCreate(Tab, items, parent, values["name"], out var error) ? null : error,
                values =>
                {
                    CodexFolders.Create(Tab, CodexFolders.Join(parent, values["name"].Trim()));
                    context.Refresh?.Invoke();
                });
        }

        // ── 옮기기·이름 바꾸기·지우기 ──────────────────────────────

        void Move(CodexPageContext context, string from, string to)
        {
            if (from == to) return;
            var items = AllItems();
            if (CodexFolders.All(Tab, items).Any(p => string.Equals(p, to, StringComparison.OrdinalIgnoreCase)))
            {
                GsDialog.Show(context.Host, "옮길 수 없습니다", $"'{to}' 폴더가 이미 있습니다.", null, new DialogButton("확인", GsButtonKind.Primary, null));
                return;
            }
            CodexFolders.Move(Tab, from, to);
            foreach (var item in items.Where(i => CodexFolders.IsUnder(i.Folder, from)))
                Staging.Work(item).EditorAssignFolder(CodexFolders.Rebase(item.Folder, from, to));
            Staging.Touch();
        }

        void RenameFolder(CodexPageContext context, string path, List<string> folders)
        {
            var parent = CodexFolders.Parent(path);
            GsDialog.Form(context.Host, "폴더 이름 바꾸기", "안의 항목·하위 폴더는 그대로 따라갑니다. 저장(Ctrl+S)할 때 적용됩니다.",
                new[] { new DialogField { Key = "name", Label = "폴더 이름", Value = CodexFolders.Name(path) } },
                "바꾸기",
                values =>
                {
                    var name = (values["name"] ?? "").Trim();
                    if (name == CodexFolders.Name(path)) return null;
                    return CodexFolders.CanCreate(Tab, AllItems(), parent, name, out var error) ? null : error;
                },
                values => Move(context, path, CodexFolders.Join(parent, values["name"].Trim())));
        }

        void DeleteFolder(CodexPageContext context, string path, List<GameData> items)
        {
            var inside = items.Count(i => CodexFolders.IsUnder(i.Folder, path));
            var parent = CodexFolders.Parent(path);
            GsDialog.Confirm(context.Host, "폴더 지우기",
                $"'{path}' 폴더를 지웁니다. 안의 항목 {inside}개와 하위 폴더는 지우지 않고 '{(parent == "" ? Noun : parent)}'(으)로 옮깁니다. 저장(Ctrl+S)할 때 적용됩니다.", "지우기", () =>
                {
                    CodexFolders.Delete(Tab, path);
                    foreach (var item in items.Where(i => CodexFolders.IsUnder(i.Folder, path)))
                    {
                        var folder = item.Folder == path ? parent : CodexFolders.Join(parent, item.Folder.Substring(path.Length + 1));
                        Staging.Work(item).EditorAssignFolder(folder);
                    }
                    Staging.Touch();
                });
        }
    }
}
