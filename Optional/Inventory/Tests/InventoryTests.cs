using GBible.InventorySystem;
using NUnit.Framework;

namespace GBible.Tests
{
    // 인벤토리: 겹치기, 칸 수 제한, 빼기.
    public sealed class InventoryTests
    {
        [Test]
        public void 덜_찬_칸부터_채우고_남으면_새_칸을_만든다()
        {
            var inventory = new Inventory();
            Assert.AreEqual(0, inventory.Add("potion", 150, 99));
            Assert.AreEqual(2, inventory.slots.Count);
            Assert.AreEqual(99, inventory.slots[0].count);
            Assert.AreEqual(51, inventory.slots[1].count);
            Assert.AreEqual(0, inventory.Add("potion", 10, 99));
            Assert.AreEqual(61, inventory.slots[1].count, "덜 찬 칸에 먼저 들어간다");
            Assert.AreEqual(160, inventory.CountOf("potion"));
            Assert.IsTrue(inventory.Has("potion", 160));
            Assert.IsFalse(inventory.Has("potion", 161));
        }

        [Test]
        public void 겹치지_않는_아이템은_한_칸에_하나씩()
        {
            var inventory = new Inventory();
            Assert.AreEqual(0, inventory.Add("sword", 3, 1));
            Assert.AreEqual(3, inventory.slots.Count);
        }

        [Test]
        public void 칸이_모자라면_남은_개수를_돌려준다()
        {
            var inventory = new Inventory { capacity = 2 };
            Assert.AreEqual(20, inventory.RoomFor("potion", 10));
            Assert.AreEqual(5, inventory.Add("potion", 25, 10));
            Assert.AreEqual(20, inventory.CountOf("potion"));
            Assert.AreEqual(0, inventory.RoomFor("potion", 10));
            Assert.AreEqual(1, inventory.Add("sword", 1, 1), "빈 칸이 없다");
        }

        [Test]
        public void 뒤쪽_칸부터_빼고_빈_칸은_없앤다()
        {
            var inventory = new Inventory();
            inventory.Add("potion", 15, 10);
            inventory.Add("sword", 1, 1);
            Assert.AreEqual(7, inventory.Remove("potion", 7));
            Assert.AreEqual(2, inventory.slots.Count, "5개짜리 칸이 비어 없어졌다");
            Assert.AreEqual(8, inventory.CountOf("potion"));
            Assert.AreEqual(8, inventory.Remove("potion", 100), "있는 만큼만 뺀다");
            Assert.AreEqual(1, inventory.slots.Count);
            Assert.IsFalse(inventory.TryRemove("sword", 2), "모자라면 빼지 않는다");
            Assert.AreEqual(1, inventory.CountOf("sword"));
            Assert.IsTrue(inventory.TryRemove("sword"));
            Assert.AreEqual(0, inventory.slots.Count);
        }

        [Test]
        public void 잘못된_값은_아무것도_바꾸지_않는다()
        {
            var inventory = new Inventory();
            Assert.AreEqual(0, inventory.Add("potion", 0, 10));
            Assert.AreEqual(3, inventory.Add("", 3, 10));
            Assert.AreEqual(3, inventory.Add((ItemData)null, 3));
            Assert.AreEqual(0, inventory.Remove("potion", 5));
            Assert.AreEqual(0, inventory.slots.Count);
        }
    }
}
