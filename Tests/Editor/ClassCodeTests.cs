using System.Linq;
using GBible.Data;
using GBible.EditorTools;
using GBible.EditorTools.UI;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace GBible.Tests
{
    // 코드 읽기: 클래스 본문 찾기(주석·문자열 안의 중괄호는 세지 않는다)와 클래스·이벤트 탭(읽기 전용).
    public class ClassCodeTests
    {
        const string HandWritten =
            "using System;\r\n" +
            "\r\n" +
            "namespace My.Game\r\n" +
            "{\r\n" +
            "    // class Monster { 주석 안의 가짜 선언 }\r\n" +
            "    [Serializable]\r\n" +
            "    public sealed class Monster\r\n" +
            "    {\r\n" +
            "        public string name = \"{}\";\r\n" +
            "        [UnityEngine.SerializeField] private int hp;\r\n" +
            "        public string grade;\r\n" +
            "\r\n" +
            "        public bool IsDead() { return hp <= 0; }\r\n" +
            "    }\r\n" +
            "}\r\n";

        [Test]
        public void 클래스_본문을_찾는다()
        {
            Assert.IsTrue(ClassCode.TryFindBody(HandWritten, "Monster", out var open, out var close));
            StringAssert.Contains("IsDead", HandWritten.Substring(open, close - open), "주석 안의 가짜 선언이 아니라 진짜 본문");
            Assert.IsFalse(ClassCode.TryFindBody(HandWritten, "Missing", out _, out _));
        }

        [Test]
        public void 클래스_이벤트_탭과_클래스_상세가_예외_없이_구성된다()
        {
            var pages = CodexPage.All.Where(p => p.TabLabel != null).ToList();
            Assert.IsTrue(pages.Any(p => p is ClassesPage && !(p is EventsPage)), "클래스 탭");
            Assert.IsTrue(pages.Any(p => p is EventsPage), "이벤트 탭");
            foreach (var page in pages)
            {
                var context = new CodexPageContext { Host = new VisualElement(), Content = new VisualElement() };
                Assert.DoesNotThrow(() =>
                {
                    page.Caption(context);
                    page.Build(context);
                }, page.Key);
            }

            // 클래스 탭에는 개발자가 선언한 클래스만: 시스템·선택 기능의 클래스는 목록에 없다.
            var classes = ClassCatalog.All(ClassRegistry.ClassTab);
            Assert.IsFalse(classes.Any(e => e.Type == typeof(SpritePresetData)), "시스템 클래스는 숨긴다");
            Assert.IsFalse(classes.Any(e => e.Type != null && GameFeatures.Of(e.Type) != null), "기능의 클래스는 숨긴다");
            Assert.IsNotNull(ClassCatalog.Find(typeof(SpritePresetData).FullName), "숨긴 클래스도 상세는 열린다");
            Assert.DoesNotThrow(() => ClassDetail.Build(new VisualElement(), typeof(AchievementData).FullName, null));
            foreach (var entry in classes.Concat(ClassCatalog.All(ClassRegistry.EventTab)))
            {
                var name = entry.FullName;
                Assert.DoesNotThrow(() => ClassDetail.Build(new VisualElement(), name, null), name);
            }
        }

        [Test]
        public void 스프라이트_규격_크기_검사와_종횡비_고정()
        {
            Assert.IsNull(SpritePresetsPage.CheckSize("128", "256", out var width, out var height));
            Assert.AreEqual(128, width);
            Assert.AreEqual(256, height);
            Assert.IsNotNull(SpritePresetsPage.CheckSize("abc", "256", out _, out _));
            Assert.IsNotNull(SpritePresetsPage.CheckSize("4", "256", out _, out _), "너무 작다");
            Assert.IsNotNull(SpritePresetsPage.CheckSize("128", "9000", out _, out _), "너무 크다");
            // 600×900(2:3)에서 가로를 400으로 바꾸면 세로는 600.
            Assert.AreEqual(600, SpritePresetsPage.Follow(400, 900f / 600f));
            Assert.AreEqual(1, SpritePresetsPage.Follow(1, 0.1f), "최소 1");
        }
    }
}
