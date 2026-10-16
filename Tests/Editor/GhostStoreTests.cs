using System;
using System.IO;
using NUnit.Framework;

namespace GBible.Tests
{
    // 고스트(비동기 대전) 기본 저장소: 이 기기 안 파일 — 올리고, rank 범위 · 남긴 사람으로 골라 받는다.
    public class GhostStoreTests
    {
        string _folder;
        string _before;

        [SetUp]
        public void SetUp()
        {
            _before = FileGhostStore.Folder;
            _folder = Path.Combine(Path.GetTempPath(), "GBibleGhostTests_" + Guid.NewGuid().ToString("N"));
            FileGhostStore.Folder = _folder;
        }

        [TearDown]
        public void TearDown()
        {
            FileGhostStore.Folder = _before;
            if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
        }

        [Test]
        public void 올린_고스트를_범위와_남긴_사람으로_골라_받는다()
        {
            var store = new FileGhostStore();
            var saved = 0;
            store.Upload(new GhostRecord { kind = "board", rank = 3, data = "{\"units\":1}", owner = "me" }, ok => { if (ok) saved++; });
            store.Upload(new GhostRecord { kind = "board", rank = 5, data = "{\"units\":2}", owner = "rival" }, ok => { if (ok) saved++; });
            store.Upload(new GhostRecord { kind = "other", rank = 5, data = "x" });
            Assert.AreEqual(2, saved);

            GhostRecord picked = null;
            store.Pick("board", 4, 6, g => picked = g);
            Assert.AreEqual("{\"units\":2}", picked?.data, "rank 5만 범위 안");

            store.Pick("board", 0, 10, g => picked = g, exceptOwner: "rival");
            Assert.AreEqual("me", picked?.owner, "남긴 사람을 빼고");

            store.Pick("board", 7, 9, g => picked = g);
            Assert.IsNull(picked, "범위에 없으면 null");
            Assert.AreEqual(2, new FileGhostStore().Read("board").Count, "파일에 남아 다음 실행에도 읽힌다");
        }
    }
}
