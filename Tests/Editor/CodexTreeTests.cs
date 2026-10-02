using System.Linq;
using GBible.EditorTools;
using GBible.EditorTools.UI;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace GBible.Tests
{
    // 도감 공용 목록(CodexTree)과 정리용 폴더(CodexFolders): 정렬(폴더 먼저, 이름순)·중첩·검색·경로 바꾸기.
    public class CodexTreeTests
    {
        static CodexTree Sample()
        {
            var tree = new CodexTree { Key = "test" };
            tree.Folders.Add(new CodexTree.Folder { Path = "", Parent = null, Name = "뿌리" });
            tree.Folders.Add(new CodexTree.Folder { Path = "b", Parent = "", Name = "b" });
            tree.Folders.Add(new CodexTree.Folder { Path = "a", Parent = "", Name = "a" });
            tree.Folders.Add(new CodexTree.Folder { Path = "a/inner", Parent = "a", Name = "inner" });
            tree.Items.Add(new CodexTree.Item { Key = "z", Folder = "", Name = "z" });
            tree.Items.Add(new CodexTree.Item { Key = "y", Folder = "", Name = "y" });
            tree.Items.Add(new CodexTree.Item { Key = "deep", Folder = "a/inner", Name = "deep" });
            return tree;
        }

        static string Title(VisualElement section) => ((GsGroupSection)section).Titles.Q<Label>(className: "gs-group__title").text;

        static string[] Children(GsGroupSection section) =>
            section.Items.Children().Select(c => c is GsGroupSection s ? "폴더:" + Title(s) : c.Q<Label>(className: "gs-group__row-text")?.text).ToArray();

        [Test]
        public void 한_폴더_안에서_폴더가_먼저_그다음_요소가_이름순이다()
        {
            var list = new VisualElement();
            Sample().Build(list, new VisualElement());
            var root = (GsGroupSection)list.Children().Single();
            CollectionAssert.AreEqual(new[] { "폴더:a", "폴더:b", "y", "z" }, Children(root));
            var a = (GsGroupSection)root.Items.Children().First();
            CollectionAssert.AreEqual(new[] { "폴더:inner" }, Children(a));
        }

        [Test]
        public void 없는_상위나_고리로_이어진_폴더와_없는_폴더의_요소는_뿌리에_보인다()
        {
            var tree = Sample();
            tree.Folders.Add(new CodexTree.Folder { Path = "lost", Parent = "gone", Name = "lost" });
            tree.Folders.Add(new CodexTree.Folder { Path = "p", Parent = "q", Name = "p" });
            tree.Folders.Add(new CodexTree.Folder { Path = "q", Parent = "p", Name = "q" });
            tree.Items.Add(new CodexTree.Item { Key = "orphan", Folder = "nowhere", Name = "orphan" });
            var list = new VisualElement();
            tree.Build(list, new VisualElement());
            var root = (GsGroupSection)list.Children().Single();
            var names = Children(root);
            CollectionAssert.Contains(names, "폴더:lost");
            CollectionAssert.Contains(names, "폴더:p");
            CollectionAssert.Contains(names, "폴더:q");
            CollectionAssert.Contains(names, "orphan");
        }

        [Test]
        public void 검색하면_걸린_요소와_그_폴더만_남는다()
        {
            var tree = Sample();
            tree.Query = "deep";
            var list = new VisualElement();
            tree.Build(list, new VisualElement());
            var root = (GsGroupSection)list.Children().Single();
            CollectionAssert.AreEqual(new[] { "폴더:a" }, Children(root));
            var a = (GsGroupSection)root.Items.Children().Single();
            var inner = (GsGroupSection)a.Items.Children().Single();
            CollectionAssert.AreEqual(new[] { "deep" }, Children(inner));
        }

        [Test]
        public void 폴더_경로를_옮기고_지우면_안쪽도_따라간다()
        {
            Assert.AreEqual("x/a/inner", CodexFolders.Rebase("a/inner", "a", "x/a"));
            Assert.AreEqual("ab", CodexFolders.Rebase("ab", "a", "x"), "이름이 겹칠 뿐인 다른 폴더는 그대로");
            Assert.AreEqual("x", CodexFolders.Rebase("a", "a", "x"));
            Assert.AreEqual("a", CodexFolders.Parent("a/b"));
            Assert.AreEqual("b", CodexFolders.Name("a/b"));
            Assert.AreEqual("", CodexFolders.Parent("a"));

            // 쓰는 사람의 저장 안 된 폴더 변경을 건드리지 않게 보관했다가 되돌린다.
            var pending = UnityEditor.SessionState.GetString(CodexFolders.StashKey, "");
            CodexFolders.Override(true);
            try
            {
                CodexFolders.Create("t", "a/inner");
                CodexFolders.Create("t", "b");
                CollectionAssert.AreEqual(new[] { "a", "a/inner", "b" }, CodexFolders.All("t", new GBible.Data.GameData[0]));
                CodexFolders.Move("t", "a", "b/a");
                CollectionAssert.AreEqual(new[] { "b", "b/a", "b/a/inner" }, CodexFolders.All("t", new GBible.Data.GameData[0]));
                CodexFolders.Delete("t", "b/a");
                CollectionAssert.AreEqual(new[] { "b", "b/inner" }, CodexFolders.All("t", new GBible.Data.GameData[0]), "지운 폴더의 안쪽은 한 단계 위로");
            }
            finally
            {
                if (pending.Length > 0) UnityEditor.SessionState.SetString(CodexFolders.StashKey, pending);
                else UnityEditor.SessionState.EraseString(CodexFolders.StashKey);
                CodexFolders.Override(false);
            }
        }
    }
}
