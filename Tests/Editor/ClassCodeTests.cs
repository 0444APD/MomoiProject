using System.Linq;
using GBible.Data;
using GBible.EditorTools;
using GBible.EditorTools.UI;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace GBible.Tests
{
    // 도감 › 클래스: C# 파일 글자 다루기(새 클래스, 필드 구역에 더하기·빼기·이름 바꾸기)와 적용 전 변경 모으기.
    public class ClassCodeTests
    {
        [Test]
        public void 새_클래스는_종류에_맞는_선언과_빈_필드_구역을_가진다()
        {
            var data = ClassCode.NewClass("Game.Data", "Inventory", ClassKind.Data, null);
            StringAssert.Contains("namespace Game.Data", data);
            StringAssert.Contains("[Serializable, PlayerData]", data);
            StringAssert.Contains("public class Inventory", data);
            Assert.IsTrue(ClassCode.IsGenerated(data));
            Assert.IsTrue(ClassCode.HasRegion(data, "Inventory"));
            Assert.AreEqual(0, ClassCode.RegionFields(data, "Inventory").Count);

            var codex = ClassCode.NewClass("", "Item", ClassKind.Codex, "아이템");
            StringAssert.Contains("[Database(\"아이템\", Group = \"데이터\")]", codex);
            StringAssert.Contains("public class Item : GameData", codex);
            StringAssert.DoesNotContain("namespace", codex);

            var evt = ClassCode.NewClass("Game.Data", "EnemyKilled", ClassKind.Event, "적 \"처치\"");
            StringAssert.Contains("[GameEvent(\"적 \\\"처치\\\"\")]", evt);
            StringAssert.Contains("public sealed class EnemyKilled", evt);
        }

        [Test]
        public void 필드를_더하고_빼고_이름을_바꾼다()
        {
            var text = ClassCode.NewClass("Game.Data", "Item", ClassKind.Codex, "아이템");
            Assert.IsTrue(ClassCode.TryAddField(ref text, "Item", ClassCode.Declaration("int", "level"), out var error), error);
            Assert.IsTrue(ClassCode.TryAddField(ref text, "Item", ClassCode.Declaration("string", "grade"), out error), error);
            Assert.IsTrue(ClassCode.TryAddField(ref text, "Item", ClassCode.Declaration("List<string>", "tags"), out error), error);
            CollectionAssert.AreEqual(new[] { "level", "grade", "tags" }, ClassCode.RegionFields(text, "Item"));
            StringAssert.Contains("public string grade = \"\";", text);
            StringAssert.Contains("public List<string> tags = new List<string>();", text);

            Assert.IsFalse(ClassCode.TryAddField(ref text, "Item", ClassCode.Declaration("float", "level"), out error), "같은 이름은 더하지 못한다");

            Assert.IsTrue(ClassCode.TryRenameField(ref text, "Item", "level", "rank", out error), error);
            StringAssert.Contains("[FormerlySerializedAs(\"level\")] public int rank;", text);
            CollectionAssert.AreEqual(new[] { "rank", "grade", "tags" }, ClassCode.RegionFields(text, "Item"));

            Assert.IsTrue(ClassCode.TryRemoveField(ref text, "Item", "grade", out error), error);
            CollectionAssert.AreEqual(new[] { "rank", "tags" }, ClassCode.RegionFields(text, "Item"));
            Assert.IsFalse(ClassCode.TryRemoveField(ref text, "Item", "grade", out error));
            // 중괄호 짝이 그대로다(파일이 깨지지 않았다).
            Assert.AreEqual(text.Count(c => c == '{'), text.Count(c => c == '}'));
        }


        [Test]
        public void 데이터_클래스에_플레이어_데이터_표시를_붙인다()
        {
            var text = ClassCode.NewClass("Game.Data", "Bag", ClassKind.Data, null).Replace("[Serializable, PlayerData]", "[Serializable]");
            Assert.IsTrue(ClassCode.TryAddField(ref text, "Bag", "public List<int> counts = new List<int>();", out var error), error);
            Assert.IsTrue(ClassCode.TryAddField(ref text, "Bag", "public string[] names = new string[0];", out error), error);
            Assert.IsTrue(ClassCode.TryUpgradeToPlayerData(ref text, "Bag", out error), error);
            StringAssert.Contains("[PlayerData]", text);
            StringAssert.Contains("public List<int> counts = new List<int>();", text);
            StringAssert.Contains("public string[] names = new string[0];", text);
            CollectionAssert.AreEqual(new[] { "counts", "names" }, ClassCode.RegionFields(text, "Bag"));
            // 두 번 해도 그대로.
            var again = text;
            Assert.IsTrue(ClassCode.TryUpgradeToPlayerData(ref again, "Bag", out _));
            Assert.AreEqual(text, again);
        }
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
            "        public int hp;\r\n" +
            "\r\n" +
            "        public bool IsDead() { return hp <= 0; }\r\n" +
            "    }\r\n" +
            "\r\n" +
            "    public sealed class Other\r\n" +
            "    {\r\n" +
            "        public int value;\r\n" +
            "    }\r\n" +
            "}\r\n";

        [Test]
        public void 직접_쓴_클래스에는_구역을_만들어_넣고_구역_밖은_건드리지_않는다()
        {
            var text = HandWritten;
            Assert.IsFalse(ClassCode.HasRegion(text, "Monster"));
            Assert.IsTrue(ClassCode.TryAddField(ref text, "Monster", ClassCode.Declaration("float", "speed"), out var error), error);
            Assert.IsTrue(ClassCode.HasRegion(text, "Monster"));
            CollectionAssert.AreEqual(new[] { "speed" }, ClassCode.RegionFields(text, "Monster"));
            // 구역은 Monster 안에, 메서드 뒤·닫는 중괄호 앞에 들어간다.
            Assert.Less(text.IndexOf("IsDead"), text.IndexOf(ClassCode.Begin));
            Assert.Less(text.IndexOf(ClassCode.End), text.IndexOf("class Other"));
            // 줄바꿈 형식 유지, 원래 있던 코드는 그대로.
            Assert.IsFalse(text.Replace("\r\n", "").Contains("\n"), "CRLF 유지");
            StringAssert.Contains("public string name = \"{}\";", text);
            StringAssert.Contains("public bool IsDead() { return hp <= 0; }", text);
            // 구역 밖에 직접 쓴 필드는 도감이 빼거나 이름을 바꾸지 않는다.
            Assert.IsFalse(ClassCode.TryRemoveField(ref text, "Monster", "hp", out error));
            Assert.IsFalse(ClassCode.TryRenameField(ref text, "Monster", "hp", "health", out error));
            // 이름 바꾸기는 필요한 using을 더한다.
            Assert.IsTrue(ClassCode.TryRenameField(ref text, "Monster", "speed", "moveSpeed", out error), error);
            StringAssert.Contains("using UnityEngine.Serialization;", text);
            Assert.AreEqual(text.Count(c => c == '{'), text.Count(c => c == '}'));
            Assert.IsFalse(ClassCode.TryAddField(ref text, "Missing", ClassCode.Declaration("int", "x"), out error), "없는 클래스");
        }

        [Test]
        public void 이름_규칙()
        {
            Assert.IsTrue(ClassCode.IsValidClassName("PlayerInventory", out _));
            Assert.IsFalse(ClassCode.IsValidClassName("playerInventory", out _));
            Assert.IsFalse(ClassCode.IsValidClassName("Item Data", out _));
            Assert.IsTrue(ClassCode.IsValidFieldName("maxCount", out _));
            Assert.IsFalse(ClassCode.IsValidFieldName("MaxCount", out _));
            Assert.IsFalse(ClassCode.IsValidFieldName("class", out _));
        }

        [Test]
        public void 적용_전_변경은_서로_상쇄된다()
        {
            ClassStaging.Discard();
            try
            {
                const string type = "Game.Data.TestOnlyClass";
                ClassStaging.CreateClass(type, ClassKind.Data, "", "", ClassRegistry.ClassTab);
                ClassStaging.AddField(type, "level", ClassCode.Declaration("int", "level"), "int", null);
                ClassStaging.RenameField(type, "level", "rank");
                var add = ClassStaging.For(type).Single(o => o.op == ClassOp.AddField);
                Assert.AreEqual("rank", add.field);
                Assert.AreEqual("public int rank;", add.declaration, "더하기 전 이름 바꾸기는 선언만 고친다");
                ClassStaging.RemoveField(type, "rank");
                Assert.AreEqual(1, ClassStaging.For(type).Count(), "더한 필드를 빼면 더하기가 취소된다");
                Assert.AreEqual(1, ClassStaging.Changes().Count);
                ClassStaging.DeleteClass(type);
                Assert.IsFalse(ClassStaging.HasChanges, "만들기 전의 클래스를 지우면 만들기가 취소된다");

                // 이미 있는 필드: 이름을 바꿨다가 되돌리면 변경이 없다.
                ClassStaging.RenameField(type, "a", "b");
                ClassStaging.RenameField(type, "b", "a");
                Assert.IsFalse(ClassStaging.HasChanges);
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
                Assert.Greater(context.Content.Query<VisualElement>().ToList().Count, 1, page.Key);
            }

            // 클래스 탭에는 개발자가 선언한 클래스만: 시스템·선택 기능의 클래스는 목록에 없다.
            var classes = ClassCatalog.All(ClassRegistry.ClassTab);
            Assert.IsFalse(classes.Any(e => e.Type == typeof(SpritePresetData) || e.Type == typeof(TemplateData)), "시스템 클래스는 숨긴다");
            Assert.IsFalse(classes.Any(e => e.Type != null && GameFeatures.Of(e.Type) != null), "기능의 클래스는 숨긴다");

            // 숨긴 클래스도 상세(필드 연결)는 열린다: 잠겨 있고 지울 수 없다.
            var sprite = ClassCatalog.Find(typeof(SpritePresetData).FullName);
            Assert.IsNotNull(sprite, "스프라이트 규격 클래스");
            Assert.IsTrue(sprite.Locked);
            Assert.IsNotNull(ClassCatalog.DeleteBlocker(sprite));
            Assert.IsFalse(ClassCatalog.Fields(sprite).Any(f => f.Editable), "잠긴 클래스의 필드는 고칠 수 없다");
            Assert.DoesNotThrow(() => ClassDetail.Build(new VisualElement(), typeof(AchievementData).FullName, null));

            foreach (var entry in classes.Concat(ClassCatalog.All(ClassRegistry.EventTab)))
            {
                var name = entry.FullName;
                Assert.DoesNotThrow(() => ClassDetail.Build(new VisualElement(), name, null), name);
            }
        }

        [Test]
        public void 필드_형식_목록으로_선언을_만든다()
        {
            var choices = ClassCatalog.TypeChoices(ClassKind.Codex);
            var number = choices.First(c => c.Code == "int");
            Assert.AreEqual("public int level;", ClassCatalog.Declare(number, 0, "level").declaration);
            var list = ClassCatalog.Declare(choices.First(c => c.Code == "string" && c.Attributes == null), 1, "tags");
            Assert.AreEqual("public List<string> tags = new List<string>();", list.declaration);
            CollectionAssert.Contains(list.usings, "System.Collections.Generic");
            Assert.IsTrue(choices.Any(c => c.Code == "Sprite"), "도감 종류에는 스프라이트 필드를 둘 수 있다");
            Assert.IsFalse(ClassCatalog.TypeChoices(ClassKind.Data).Any(c => c.Code == "Sprite"), "저장소에 담는 데이터 클래스에는 Unity 에셋을 두지 않는다");
            var reference = choices.First(c => c.Attributes != null && c.Attributes.Contains(nameof(StatData)));
            StringAssert.StartsWith("[DataRef(typeof(global::GBible.Data.StatData))] public string stat = \"\";", ClassCatalog.Declare(reference, 0, "stat").declaration);
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
