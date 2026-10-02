using System.Linq;
using GBible.Data;
using GBible.EditorTools;
using NUnit.Framework;
using UnityEngine;

namespace GBible.Tests
{
    // 도감의 변경 모아 두기: 적용 전에는 에셋이 그대로이고, 도감이 보는 모습(All)에만 나타난다.
    public class CodexStagingTests
    {
        static CodexStaging Staging => EditorDatabase.Staging;

        [TearDown]
        public void TearDown() => Staging.Discard();

        [Test]
        public void 새_항목은_적용_전에는_도감에만_보인다()
        {
            var added = Staging.Add(typeof(SpritePresetGroupData), "testStagedGroup");
            Assert.IsTrue(EditorDatabase.Instance.All.Contains(added));
            Assert.IsFalse(EditorDatabase.Instance.Committed.Contains(added));
            Assert.IsTrue(Staging.HasChanges);
            Assert.AreEqual(PendingKind.Added, Staging.Changes().Single().Kind);

            Staging.Discard();
            Assert.IsFalse(EditorDatabase.Instance.All.Any(d => d.Id == "testStagedGroup"));
            Assert.IsFalse(Staging.HasChanges);
        }

        [Test]
        public void 새_항목을_지우면_변경에서_빠진다()
        {
            var added = Staging.Add(typeof(SpritePresetData), "testStagedKeyword");
            Staging.Remove(added);
            Assert.IsFalse(Staging.HasChanges);
        }

        [Test]
        public void 작업본을_고치면_필드별_변경이_나온다()
        {
            var original = ScriptableObject.CreateInstance<SpritePresetData>();
            original.EditorAssignId("testOriginalKeyword");
            original.group = "a";
            try
            {
                var work = (SpritePresetData)Staging.Work(original);
                Assert.AreNotSame(original, work);
                Assert.AreSame(work, Staging.Work(work), "작업본을 다시 고쳐도 같은 작업본");
                Assert.IsFalse(Staging.IsPending(work), "열기만 하고 고치지 않으면 변경이 아니다");

                work.group = "b";
                work.order = 30;
                var change = Staging.Changes().Single();
                Assert.AreEqual(PendingKind.Modified, change.Kind);
                Assert.IsTrue(change.Fields.Any(f => f.Path == "group"));
                Assert.IsTrue(change.Fields.Any(f => f.Path == "order"), "숨긴 순서 필드도 변경으로 보인다");
                Assert.AreEqual("a", original.group, "적용 전에는 원본이 그대로");
            }
            finally
            {
                Staging.Discard();
                Object.DestroyImmediate(original);
            }
        }
    }
}
