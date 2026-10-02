using System;
using System.Collections.Generic;
using System.Linq;
using GBible.EditorTools.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace GBible.Tests
{
    [Serializable]
    public sealed class ViewStats
    {
        public int power;
        public int speed;
    }

    [Serializable]
    public sealed class ViewItem
    {
        public string title;
        public int grade;
        public Sprite icon;
        public Sprite portrait;
        public ViewStats stats = new ViewStats();
    }

    // 그리드 배치: 기본 배치, 블록이 가져간 필드는 데이터 나열에서 빠짐, 없는 블록은 표시만.
    public class DataViewTests
    {
        sealed class FakeContext : DataViewContext
        {
            public readonly List<string> Rows = new List<string>();
            public override Type Type => typeof(ViewItem);
            public override bool CanEdit => false;
            public override IEnumerable<string> Fields() => FieldPaths.MembersOf(typeof(ViewItem)).Select(m => m.Name);

            public override VisualElement FieldRow(string path)
            {
                Rows.Add(path);
                return new Label(path);
            }

            public override VisualElement FieldValue(string path) => new Label(path);
            public override UnityEngine.Object ObjectAt(string path) => null;
            public override void SetObject(string path, UnityEngine.Object value) { }
        }

        [Test]
        public void 기본_배치는_스프라이트_나열과_데이터_나열()
        {
            var context = new FakeContext();
            var layout = DataLayouts.Default(typeof(ViewItem), context);
            CollectionAssert.AreEqual(new[] { SpriteListBlock.Id, DataListBlock.Id }, layout.blocks.Select(b => b.block));
            Assert.AreEqual(4, layout.blocks[0].w);
            Assert.AreEqual(2, layout.blocks[0].perRow);
        }

        [Test]
        public void 데이터_나열은_스프라이트와_다른_블록이_가져간_필드를_뺀다()
        {
            var context = new FakeContext();
            var layout = new TypeLayout
            {
                type = typeof(ViewItem).FullName,
                blocks =
                {
                    new BlockPlacement { block = FieldCardBlock.Id, path = "grade", y = 0, w = 4 },
                    new BlockPlacement { block = ClassCardBlock.Id, path = "stats", paths = { "power" }, y = 0, x = 4, w = 4 },
                    new BlockPlacement { block = DataListBlock.Id, y = 1 },
                },
            };
            var grid = new VisualElement();
            GsDataView.Render(grid, context, layout);
            CollectionAssert.AreEqual(new[] { "title" }, context.Rows, "grade·stats는 카드가, 스프라이트는 이미지 블록이 가져간다");
            Assert.AreEqual(2, grid.childCount, "행 두 개");
        }

        [Test]
        public void 등록된_기본_블록이_모두_있다()
        {
            foreach (var id in new[] { SpriteListBlock.Id, DataListBlock.Id, SpriteFieldBlock.Id, FieldCardBlock.Id, ClassCardBlock.Id })
                Assert.IsNotNull(GsLayoutBlock.Create(id), id);
            Assert.IsNull(GsLayoutBlock.Create("없는블록"));
        }
    }
}
