using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GBible.Data;
using UnityEditor;
using UnityEngine.UIElements;

namespace GBible.EditorTools.UI
{
    // 개발자 폴더 탭(DevFolders): Assets/Dev/<이름> 안의 모든 에셋. 하위 폴더는 접는 묶음(클래스 탭과 같은 모양).
    // 줄을 누르면 Project 창에서 고르고(인스펙터에 보인다), DB 항목이면 상세가 열린다. '열기'는 씬·프리팹·스크립트를 연다.
    // 정리(옮기기·이름 바꾸기·지우기)는 Project 창에서 한다 — 이 탭은 끌어 옮기지 않는다.
    public sealed class DevFolderPage : CodexPage
    {
        public const string Prefix = "dev:";

        static readonly Dictionary<string, DevFolderPage> Pages = new Dictionary<string, DevFolderPage>(StringComparer.Ordinal);

        readonly string _folder;

        DevFolderPage(string folder)
        {
            _folder = folder;
        }

        // 폴더마다 하나(사이드바가 지금 연 탭을 같은 객체로 알아보게).
        public static DevFolderPage For(string folder)
        {
            if (!Pages.TryGetValue(folder, out var page)) Pages[folder] = page = new DevFolderPage(folder);
            return page;
        }

        public string Folder => _folder;
        public override string Key => Prefix + _folder;
        public override string TabLabel => Path.GetFileName(_folder);
        public override string TabGroup => DevFolders.Group;
        public override string TabCount => DevFolders.Contents(_folder).assets.Count.ToString();
        // 만들기 버튼 없음(에셋은 Project 창에서 넣는다).
        public override string CreateLabel => null;

        public override void Create(CodexPageContext context) { }

        public override string Caption(CodexPageContext context)
        {
            var (assets, folders) = DevFolders.Contents(_folder);
            return $"{_folder} · 에셋 {assets.Count}개 · 폴더 {folders.Count}개  ·  줄을 누르면 선택(DB 항목은 상세) · 정리는 Project 창에서 합니다(폴더를 만들면 묶음이 바로 생깁니다)";
        }

        public override void Build(CodexPageContext context)
        {
            var list = Gs.Col("gs-grouped__list");
            context.Content.Add(list);
            var (assets, folders) = DevFolders.Contents(_folder);
            var tree = new CodexTree { Key = Key, ItemNoun = "에셋", Query = (context.Search ?? "").Trim() };
            tree.Folders.Add(new CodexTree.Folder
            {
                Path = "", Parent = null, Name = TabLabel, Draggable = false, AcceptsItems = false, AcceptsFolders = false,
                Hint = $"비어 있습니다. Project 창에서 {_folder}에 에셋이나 폴더를 넣으면 여기 보입니다.",
                Tools = tools => tools.Add(PingButton(_folder)),
            });
            foreach (var folder in folders)
            {
                var path = folder;
                var relative = Relative(path);
                tree.Folders.Add(new CodexTree.Folder
                {
                    Path = relative, Parent = ParentOf(relative), Name = Path.GetFileName(relative),
                    Draggable = false, AcceptsItems = false, AcceptsFolders = false,
                    Tools = tools => tools.Add(PingButton(path)),
                });
            }
            foreach (var asset in assets)
            {
                var path = asset;
                var type = AssetDatabase.GetMainAssetTypeAtPath(path);
                tree.Items.Add(new CodexTree.Item
                {
                    Key = path, Folder = ParentOf(Relative(path)), Name = Path.GetFileName(path), SearchText = type?.Name,
                    Draggable = false, Payload = path,
                    Build = () => Row(path, type),
                    Open = () => Select(context, path),
                });
            }
            tree.Build(list, context.Host, context.Group);
        }

        string Relative(string path) => path.Length > _folder.Length ? path.Substring(_folder.Length + 1) : "";

        static string ParentOf(string relative)
        {
            var slash = relative.LastIndexOf('/');
            return slash < 0 ? "" : relative.Substring(0, slash);
        }

        static VisualElement Row(string path, Type type)
        {
            var row = GsGroupSection.Row(Path.GetFileNameWithoutExtension(path), KindOf(path, type));
            var icon = AssetDatabase.GetCachedIcon(path);
            if (icon != null)
            {
                var image = new Image { image = icon };
                image.style.width = 16;
                image.style.height = 16;
                image.style.marginRight = 6;
                image.style.flexShrink = 0;
                row.Insert(0, image);
            }
            var open = new GsButton("열기", () => AssetDatabase.OpenAsset(AssetDatabase.LoadMainAssetAtPath(path)), GsButtonKind.Ghost);
            open.tooltip = "Unity에서 연다(씬·프리팹·스크립트는 편집기로, 그 밖은 인스펙터로)";
            open.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            open.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            row.Add(open);
            row.tooltip = path + "\n눌러서 선택(DB 항목은 상세)";
            return row;
        }

        // 줄 옆 옅은 글: DB 항목이면 종류 이름, 아니면 형식 이름.
        static string KindOf(string path, Type type)
        {
            if (type == null) return Path.GetExtension(path).TrimStart('.');
            if (typeof(GameData).IsAssignableFrom(type)) return DataTypes.Find(type)?.DisplayName ?? type.Name;
            return type.Name;
        }

        static void Select(CodexPageContext context, string path)
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset == null) return;
            if (asset is GameData data && DataTypes.Find(data.GetType()) != null)
            {
                context.OpenDetail(data);
                return;
            }
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        static GsButton PingButton(string folder)
        {
            var button = new GsButton("Project에서 보기", () =>
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(folder);
                if (asset == null) return;
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }, GsButtonKind.Ghost);
            button.tooltip = "Project 창에서 이 폴더를 고른다(여기서 에셋을 넣고 정리한다)";
            return button;
        }
    }
}
