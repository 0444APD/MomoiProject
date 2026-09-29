using System.Linq;
using GameSystems.Data;
using GameSystems.EditorTools;
using GameSystems.EditorTools.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameSystems.Tests
{
    public class DetailPanelTests
    {
        ShowcaseItem _item;
        VisualElement _host;

        [SetUp]
        public void SetUp()
        {
            _item = ScriptableObject.CreateInstance<ShowcaseItem>();
            _item.hideFlags = HideFlags.DontSave;
            _item.name = "검";
            _item.displayName = "검";
            _item.price = 120;
            _host = new VisualElement();
            Gs.Apply(_host);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_item);

        static void Change(GsDetailPanel panel, string path, int value)
        {
            panel.Draft.Update();
            panel.Draft.FindProperty(path).intValue = value;
            panel.Draft.ApplyModifiedPropertiesWithoutUndo();
            panel.RefreshState();
        }

        [Test]
        public void 편집은_사본에만_되고_변경이_이전과_현재로_잡힌다()
        {
            var panel = GsDetailPanel.Open(_host, _item);
            Change(panel, "price", 999);

            Assert.AreEqual(120, _item.price, "저장 전 원본은 그대로");
            Assert.AreEqual(1, panel.Changes.Count);
            Assert.AreEqual("120", panel.Changes[0].Old);
            Assert.AreEqual("999", panel.Changes[0].New);
            panel.Close();
        }

        [Test]
        public void 저장하면_원본에_반영되고_이름과_숨김_플래그는_지킨다()
        {
            var panel = GsDetailPanel.Open(_host, _item);
            Change(panel, "price", 500);
            panel.SaveNow();

            Assert.AreEqual(500, _item.price);
            Assert.AreEqual("검", _item.name);
            Assert.AreEqual(HideFlags.DontSave, _item.hideFlags);
            Assert.AreEqual(0, panel.Changes.Count, "저장 후에는 변경 없음");
            panel.Close();
        }

        [Test]
        public void 되돌리면_사본이_원본으로_돌아간다()
        {
            var panel = GsDetailPanel.Open(_host, _item);
            Change(panel, "price", 700);
            panel.RevertNow();
            panel.RefreshState();

            Assert.AreEqual(0, panel.Changes.Count);
            Assert.AreEqual(120, panel.Draft.FindProperty("price").intValue);
            panel.Close();
        }

        // 시트 더블클릭은 어느 열에서든 열 수 있어야 한다(제목 필드는 스크롤 영역 밖 헤더에 있다).
        [Test]
        public void 어느_필드로_열어도_예외가_없다()
        {
            var panel = GsDetailPanel.Open(_host, _item);
            foreach (var property in PropertyUtil.TopLevel(new SerializedObject(_item)).Select(p => p.propertyPath).ToList())
            {
                var path = property;
                Assert.DoesNotThrow(() => panel.FocusField(path), path);
            }
            panel.Close();

            var stat = ScriptableObject.CreateInstance<StatData>();
            try
            {
                var statPanel = GsDetailPanel.Open(_host, stat);
                foreach (var path in new[] { "displayName", "tags", "scope", "aggregate", "rules" })
                    Assert.DoesNotThrow(() => statPanel.FocusField(path), path);
                statPanel.Close();
            }
            finally
            {
                Object.DestroyImmediate(stat);
            }
        }

        [Test]
        public void 조건_블록_안쪽의_변화도_문장으로_잡는다()
        {
            var original = ScriptableObject.CreateInstance<AchievementData>();
            original.condition = new StatCompareCondition { stat = "kills", op = CompareOp.AtLeast, value = 5 };
            var draft = Object.Instantiate(original);
            ((StatCompareCondition)draft.condition).value = 7;
            try
            {
                var changes = DraftDiff.Compute(new SerializedObject(original), new SerializedObject(draft));
                Assert.AreEqual(1, changes.Count);
                Assert.AreEqual("condition", changes[0].Path);
                StringAssert.Contains(">= 5", changes[0].Old);
                StringAssert.Contains(">= 7", changes[0].New);
            }
            finally
            {
                Object.DestroyImmediate(original);
                Object.DestroyImmediate(draft);
            }
        }

        [Test]
        public void 시트는_읽기_전용_셀과_열기_요청을_제공한다()
        {
            var sheet = new GsSheet(Gs.Surface);
            _host.Add(sheet);
            sheet.Build(new[]
            {
                new SheetColumn { Title = "이름", Path = "displayName", Bold = true },
                new SheetColumn { Title = "가격", Path = "price" },
            }, new Object[] { _item });

            Assert.AreEqual(2, sheet.childCount, "헤더 + 행 1개");
            Assert.AreEqual(0, sheet.Query<GsInlineProperty>().ToList().Count, "시트 셀은 편집 부품을 두지 않는다");
            Assert.AreEqual(2, sheet.Query(className: "gs-sheet__resizer").ToList().Count, "열마다 폭 조절 손잡이");

            Object opened = null;
            string openedPath = null;
            sheet.OpenRequested += (o, p) =>
            {
                opened = o;
                openedPath = p;
            };
            sheet.Select(_item);
            Assert.AreEqual(1, sheet.Query(className: "gs-sheet__row--selected").ToList().Count);
            Assert.IsNull(opened, "선택만으로는 열리지 않는다");
            _ = openedPath;
        }
    }
}
