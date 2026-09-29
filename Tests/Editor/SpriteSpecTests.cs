using GameSystems.EditorTools;
using NUnit.Framework;
using UnityEngine;

namespace GameSystems.Tests
{
    // 스프라이트 규격: 잘라 낼 영역 계산(베이크는 GPU라 여기서는 계산만).
    public class SpriteSpecTests
    {
        [Test]
        public void 배율_1은_규격_비율로_원본에_꽉_찬다()
        {
            var crop = SpriteSpecEditor.Crop(new Rect(0, 0, 1000, 2000), new Vector2(0.5f, 0.5f), 1f, 100, 100);
            Assert.AreEqual(1000, crop.width, 0.01f);
            Assert.AreEqual(1000, crop.height, 0.01f);
            Assert.AreEqual(500, crop.y, 0.01f, "가운데");
        }

        [Test]
        public void 확대하면_영역이_작아지고_원본_밖으로_나가지_않는다()
        {
            var crop = SpriteSpecEditor.Crop(new Rect(0, 0, 1000, 1000), new Vector2(0f, 1f), 4f, 256, 128);
            Assert.AreEqual(250, crop.width, 0.01f);
            Assert.AreEqual(125, crop.height, 0.01f);
            Assert.AreEqual(0, crop.x, 0.01f, "왼쪽 끝에 붙는다");
            Assert.AreEqual(1000 - 125, crop.y, 0.01f, "위쪽 끝에 붙는다");
        }

        [Test]
        public void 아틀라스_안의_스프라이트는_그_영역_기준()
        {
            var crop = SpriteSpecEditor.Crop(new Rect(200, 300, 400, 400), new Vector2(0.5f, 0.5f), 2f, 64, 64);
            Assert.AreEqual(300, crop.x, 0.01f);
            Assert.AreEqual(400, crop.y, 0.01f);
        }
    }
}
