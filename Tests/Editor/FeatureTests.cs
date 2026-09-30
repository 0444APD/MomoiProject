using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GameSystems.Tests
{
    public sealed class FeatureTests
    {
        abstract class Feature
        {
        }

        [BlockInfo("장착")]
        sealed class Equip : Feature
        {
            public int attack;
        }

        [BlockInfo("사용")]
        sealed class Use : Feature
        {
            public int heal;
        }

        [BlockInfo("효과"), FeatureRule(Single = false)]
        sealed class Effect : Feature
        {
        }

        [BlockInfo("투척"), FeatureRule(Requires = new[] { typeof(Use) }, ExclusiveWith = new[] { typeof(Equip) })]
        sealed class Throw : Feature
        {
        }

        [Test]
        public void 기능은_여럿_함께_붙고_종류가_아니라_기능으로_찾는다()
        {
            var ring = new List<Feature> { new Equip { attack = 3 }, new Use { heal = 10 } };
            Assert.AreEqual(3, ring.GetFeature<Equip>().attack);
            Assert.AreEqual(10, ring.GetFeature<Use>().heal);
            Assert.IsTrue(ring.HasFeature<Use>());
            Assert.IsFalse(ring.HasFeature<Throw>());
            Assert.IsNull(ring.GetFeature<Throw>());
            Assert.IsEmpty(Features.Check(ring), "장착 + 사용은 함께 붙는다");
        }

        [Test]
        public void 규칙_한_번만_함께_못_붙음_먼저_있어야_함()
        {
            var doubled = new List<Feature> { new Equip(), new Equip() };
            StringAssert.Contains("하나만", Features.Check(doubled).Single());

            var effects = new List<Feature> { new Effect(), new Effect() };
            Assert.IsEmpty(Features.Check(effects), "Single = false면 여러 개");

            var badThrow = new List<Feature> { new Throw(), new Equip() };
            var messages = Features.Check(badThrow).ToList();
            Assert.IsTrue(messages.Any(m => m.Contains("함께 붙일 수 없습니다")));
            Assert.IsTrue(messages.Any(m => m.Contains("'사용' 기능이 함께 있어야")));

            Assert.IsEmpty(Features.Check(new List<Feature> { new Use(), new Throw() }));
            Assert.IsNotEmpty(Features.Check(new List<Feature> { null }), "빈 칸");
            CollectionAssert.AreEqual(new[] { 2 }, new List<Feature> { new Effect(), new Use(), new Effect() }.FeaturesOf<Effect>().Select(_ => 2).Distinct().ToArray());
        }
    }
}
