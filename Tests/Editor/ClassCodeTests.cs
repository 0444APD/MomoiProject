using System.Linq;
using GBible.Data;
using GBible.EditorTools;
using GBible.EditorTools.UI;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace GBible.Tests
{
    // 코드 글자 다루기: DB가 코드에 써 넣는 것(키워드 그룹·키워드, 필드의 어트리뷰트)과 적용 전 변경 모으기. 클래스 탭은 읽기 전용.
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
        public void 필드에_어트리뷰트를_붙이고_뗀다()
        {
            var text = HandWritten;
            Assert.IsTrue(ClassCode.TrySetFieldAttribute(ref text, "Monster", "hp", "Hide", true, out var error), error);
            StringAssert.Contains("[Hide] [UnityEngine.SerializeField] private int hp;", text);
            StringAssert.Contains("using GBible;", text);
            var once = text;
            Assert.IsTrue(ClassCode.TrySetFieldAttribute(ref text, "Monster", "hp", "Hide", true, out error), error);
            Assert.AreEqual(once, text, "이미 있으면 그대로");
            Assert.IsTrue(ClassCode.TrySetFieldAttribute(ref text, "Monster", "hp", "Hide", false, out error), error);
            StringAssert.DoesNotContain("[Hide]", text);
            StringAssert.Contains("[UnityEngine.SerializeField] private int hp;", text);
            // 줄바꿈 형식 유지, 다른 코드는 그대로.
            Assert.IsFalse(text.Replace("\r\n", "").Contains("\n"), "CRLF 유지");
            StringAssert.Contains("public bool IsDead() { return hp <= 0; }", text);
            Assert.IsFalse(ClassCode.TrySetFieldAttribute(ref text, "Monster", "missing", "Hide", true, out _), "없는 필드");
            Assert.IsFalse(ClassCode.TrySetFieldAttribute(ref text, "Missing", "hp", "Hide", true, out _), "없는 클래스");
        }

        [Test]
        public void 필드에_키워드_그룹을_연결한다()
        {
            var text = HandWritten;
            Assert.IsTrue(KeywordCode.TrySetFieldKeyword(ref text, "Monster", "grade", "Rarity", out var error), error);
            StringAssert.Contains("[Keyword(typeof(Rarity))] public string grade;", text);
            Assert.IsTrue(KeywordCode.TrySetFieldKeyword(ref text, "Monster", "grade", "Element", out error), error);
            StringAssert.Contains("[Keyword(typeof(Element))] public string grade;", text);
            StringAssert.DoesNotContain("Rarity", text);
        }

        [Test]
        public void 키워드_그룹_코드를_만들고_고친다()
        {
            var text = KeywordCode.NewGroupFile("Game.Data", "Rarity", "등급");
            StringAssert.Contains("[KeywordGroup(\"등급\")]", text);
            StringAssert.Contains("public static class Rarity", text);
            Assert.IsTrue(KeywordCode.TryAddKeyword(ref text, "Rarity", "Common", "common", out var error), error);
            Assert.IsTrue(KeywordCode.TryAddKeyword(ref text, "Rarity", "Legendary", "legendary", out error), error);
            Assert.IsFalse(KeywordCode.TryAddKeyword(ref text, "Rarity", "Common", "common2", out _), "같은 이름");
            StringAssert.Contains("public const string Common = \"common\";", text);
            Assert.Less(text.IndexOf("Common"), text.IndexOf("Legendary"), "더한 순서대로");

            Assert.IsTrue(KeywordCode.TryAddGroup(ref text, "Rarity", "Special", null, out error), error);
            Assert.IsTrue(KeywordCode.TryAddKeyword(ref text, "Special", "Event", "event", out error), error);
            Assert.IsTrue(KeywordCode.TryAddKeyword(ref text, "Rarity", "Rare", "rare", out error), error);
            Assert.Less(text.IndexOf("\"rare\""), text.IndexOf("class Special"), "키워드는 하위 그룹보다 위에");

            Assert.IsTrue(KeywordCode.TryRenameKeyword(ref text, "Rarity", "Legendary", "Mythic", out error), error);
            StringAssert.Contains("public const string Mythic = \"legendary\";", text, "이름만 바뀌고 저장되는 ID는 그대로");
            Assert.IsTrue(KeywordCode.TryRemoveKeyword(ref text, "Rarity", "Common", out error), error);
            StringAssert.DoesNotContain("\"common\"", text);
            Assert.IsTrue(KeywordCode.TryRemoveGroup(ref text, "Special", out error), error);
            StringAssert.DoesNotContain("Special", text);
            Assert.IsTrue(KeywordCode.IsOnlyGroupInFile(text, "Rarity"));
            Assert.AreEqual(text.Count(c => c == '{'), text.Count(c => c == '}'));
        }

        [Test]
        public void 키워드_상수_이름()
        {
            Assert.AreEqual("Legendary", KeywordCode.ConstName("legendary"));
            Assert.AreEqual("FireResist", KeywordCode.ConstName("fire_resist"));
            Assert.AreEqual("EquipmentTag", KeywordCode.ConstName("equipmentTag"));
            Assert.AreEqual("K2star", KeywordCode.ConstName("2star"));
            Assert.IsTrue(KeywordCode.IsValidName("Rarity", out _));
            Assert.IsFalse(KeywordCode.IsValidName("rarity", out _));
            Assert.IsFalse(KeywordCode.IsValidName("Rare Item", out _));
        }

        [Test]
        public void 적용_전_변경은_서로_상쇄된다()
        {
            ClassStaging.Discard();
            try
            {
                var group = ClassStaging.AddKeywordGroup("", "TestOnlyGroup", "");
                ClassStaging.AddKeyword(group, "A", "a");
                ClassStaging.RenameKeyword(group, "A", "B");
                Assert.AreEqual("B", ClassStaging.For(group).Single(o => o.op == ClassOp.KeywordAdd).field, "더하기 전 이름 바꾸기는 더하기만 고친다");
                ClassStaging.RemoveKeyword(group, "B");
                Assert.IsFalse(ClassStaging.For(group).Any(), "더한 키워드를 빼면 더하기가 취소된다");
                ClassStaging.RemoveKeywordGroup(group);
                Assert.IsFalse(ClassStaging.HasChanges, "만들기 전의 그룹을 지우면 만들기가 취소된다");

                ClassStaging.SetFieldAttribute("Game.Data.X", "hp", "Hide", true);
                Assert.IsTrue(ClassStaging.PendingAttribute("Game.Data.X", "hp", "Hide", false));
                ClassStaging.SetFieldAttribute("Game.Data.X", "hp", "Hide", false);
                Assert.IsFalse(ClassStaging.HasChanges, "붙였다 떼면 변경이 없다");
            }
            finally
            {
                ClassStaging.Discard();
            }
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
