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
            Assert.IsFalse(classes.Any(e => e.Type == typeof(SpriteFrameData)), "시스템 클래스는 숨긴다");
            Assert.IsFalse(classes.Any(e => e.Type != null && GameFeatures.Of(e.Type) != null), "기능의 클래스는 숨긴다");
            Assert.IsNotNull(ClassCatalog.Find(typeof(SpriteFrameData).FullName), "숨긴 클래스도 상세는 열린다");
            Assert.DoesNotThrow(() => ClassDetail.Build(new VisualElement(), typeof(AchievementData).FullName, null));
            foreach (var entry in classes.Concat(ClassCatalog.All(ClassRegistry.EventTab)))
            {
                var name = entry.FullName;
                Assert.DoesNotThrow(() => ClassDetail.Build(new VisualElement(), name, null), name);
            }
        }

        [Test]
        public void 스프라이트_틀의_크기_이름과_크기_검사()
        {
            Assert.IsTrue(SpriteFrameData.IsValidName("standing", out _));
            Assert.IsTrue(SpriteFrameData.IsValidName("cardFront", out _));
            Assert.IsFalse(SpriteFrameData.IsValidName("Icon", out _), "소문자로 시작");
            Assert.IsFalse(SpriteFrameData.IsValidName("card.front", out _), "점 없음");
            Assert.IsFalse(SpriteFrameData.IsValidName("", out _));
            Assert.IsTrue(SpriteFrameData.IsValidSize(8));
            Assert.IsTrue(SpriteFrameData.IsValidSize(8192));
            Assert.IsFalse(SpriteFrameData.IsValidSize(4), "너무 작다");
            Assert.IsFalse(SpriteFrameData.IsValidSize(9000), "너무 크다");

            var frame = UnityEngine.ScriptableObject.CreateInstance<SpriteFrameData>();
            try
            {
                frame.EditorAssignId("character");
                frame.sizes.Add(new SpriteFrameSize { name = "icon", width = 128, height = 128 });
                frame.sizes.Add(new SpriteFrameSize { name = "Icon", width = 4, height = 128 });
                var issues = DataValidator.Validate(new System.Collections.Generic.List<GameData> { frame });
                Assert.IsTrue(issues.Any(i => i.Asset == frame && i.Severity == ValidationSeverity.Error && i.Message.Contains("8~8192")), "크기 범위");
                Assert.IsTrue(issues.Any(i => i.Asset == frame && i.Severity == ValidationSeverity.Error && i.Message.Contains("'Icon'")), "이름 형식");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(frame);
            }
        }
    }
}
