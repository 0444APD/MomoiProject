using System.Collections.Generic;
using System.Linq;
using GBible.Data;
using GBible.EditorTools;
using NUnit.Framework;
using UnityEngine;

namespace GBible.Tests
{
    // DB의 변경 모아 두기: 적용 전에는 에셋이 그대로이고, DB가 보는 모습(All)에만 나타난다.
    public class CodexStagingTests
    {
        static CodexStaging Staging => EditorDatabase.Staging;
        readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            Staging.Discard();
            foreach (var o in _created) Object.DestroyImmediate(o);
            _created.Clear();
        }

        // 적용된 에셋 대신 쓰는 메모리 항목(적용 계획은 에셋 없이 시험한다).
        T Make<T>(string id) where T : GameData
        {
            var data = ScriptableObject.CreateInstance<T>();
            data.EditorAssignId(id);
            _created.Add(data);
            return data;
        }

        // 작업본 대신: ID만 바꾼 사본.
        T Copy<T>(T original, string id) where T : GameData
        {
            var copy = Object.Instantiate(original);
            copy.EditorAssignId(id);
            _created.Add(copy);
            return copy;
        }

        static CodexStaging.CommitPlan Plan(GameData[] committed, Dictionary<GameData, GameData> working, Dictionary<GameData, string> refsAt,
            GameDatabase database = null, params GameData[] added) =>
            CodexStaging.Plan(committed, working, added, new List<GameData>(), refsAt, database);

        [Test]
        public void ID_바꾸기로_서로_바꾼_ID는_그대로_적용한다()
        {
            // a → t, b → a, t → b: 참조는 ID를 바꿀 때 이미 옮겼으므로 적용할 때는 ID·파일 이름만 바뀐다.
            var a = Make<SpriteFrameData>("a");
            var b = Make<SpriteFrameData>("b");
            var plan = Plan(new GameData[] { a, b }, new Dictionary<GameData, GameData> { [a] = Copy(a, "b"), [b] = Copy(b, "a") },
                new Dictionary<GameData, string> { [a] = "b", [b] = "a" });
            Assert.IsEmpty(plan.Problems, string.Join("\n", plan.Problems));
            Assert.IsEmpty(plan.Follow, "참조를 다시 훑지 않는다(다시 훑으면 서로 바뀐 참조가 또 바뀐다)");
            Assert.AreEqual(2, plan.Renames.Count);
            Assert.AreEqual(2, plan.Files.Count);
        }

        [Test]
        public void 비운_ID를_다른_항목이_쓰면_그_ID의_참조는_새_주인을_가리킨다()
        {
            // A a → x, B b → a(둘 다 ID 바꾸기): 'a'를 가리키는 칸은 이제 B의 것 — A로 옮기지 않는다.
            var a = Make<SpriteFrameData>("a");
            var b = Make<SpriteFrameData>("b");
            var plan = Plan(new GameData[] { a, b }, new Dictionary<GameData, GameData> { [a] = Copy(a, "x"), [b] = Copy(b, "a") },
                new Dictionary<GameData, string> { [a] = "x", [b] = "a" });
            Assert.IsEmpty(plan.Problems, string.Join("\n", plan.Problems));
            Assert.IsFalse(plan.Follow.ContainsKey("a"));
            Assert.AreSame(b, plan.Follow["b"].Asset, "아무도 쓰지 않는 예전 ID 'b'에 남은 칸만 B를 따라간다");
        }

        [Test]
        public void 작업본의_ID만_바꾸면_남은_참조가_적용할_때_따라간다()
        {
            var a = Make<SpriteFrameData>("a");
            var plan = Plan(new GameData[] { a }, new Dictionary<GameData, GameData> { [a] = Copy(a, "x") }, new Dictionary<GameData, string>());
            Assert.IsEmpty(plan.Problems);
            Assert.AreSame(a, plan.Follow["a"].Asset);
        }

        [Test]
        public void 작업본의_ID만_서로_바꾸면_적용하지_않는다()
        {
            // 참조를 옮기지 않은 채 서로 바꾸면 적용하는 순간 참조가 엉뚱한 항목을 가리킨다.
            var a = Make<SpriteFrameData>("a");
            var b = Make<SpriteFrameData>("b");
            var plan = Plan(new GameData[] { a, b }, new Dictionary<GameData, GameData> { [a] = Copy(a, "b"), [b] = Copy(b, "a") }, new Dictionary<GameData, string>());
            Assert.AreEqual(2, plan.Problems.Count, string.Join("\n", plan.Problems));
        }

        [Test]
        public void 마지막_모습에서_ID가_겹치면_적용하지_않는다()
        {
            var a = Make<SpriteFrameData>("a");
            var c = Make<SpriteFrameData>("x");
            var plan = Plan(new GameData[] { a, c }, new Dictionary<GameData, GameData> { [a] = Copy(a, "X") }, new Dictionary<GameData, string>());
            Assert.AreEqual(1, plan.Problems.Count, "대소문자만 달라도 겹친다");
            var added = Make<SpriteFrameData>("x");
            plan = Plan(new GameData[] { c }, new Dictionary<GameData, GameData>(), new Dictionary<GameData, string>(), null, added);
            Assert.AreEqual(1, plan.Problems.Count, "새 항목도 본다");
        }

        [Test]
        public void 복제한_에셋의_ID를_바꾸면_참조와_이력은_남은_항목의_것으로_둔다()
        {
            var a = Make<SpriteFrameData>("d");
            var duplicate = Make<SpriteFrameData>("d");
            var plan = Plan(new GameData[] { a, duplicate }, new Dictionary<GameData, GameData> { [duplicate] = Copy(duplicate, "e") },
                new Dictionary<GameData, string> { [duplicate] = "e" });
            Assert.IsEmpty(plan.Problems, string.Join("\n", plan.Problems));
            Assert.IsEmpty(plan.Follow);
            Assert.IsEmpty(plan.Renames, "이력·이름 텍스트는 같은 ID를 그대로 쓰는 남은 항목의 것");
            Assert.AreEqual(1, plan.Files.Count, "파일 이름은 그 에셋 것만(이름이 예전 ID일 때)");
        }

        [Test]
        public void 출시_뒤에는_이력의_예전_ID를_다른_항목이_쓰지_못한다()
        {
            var database = ScriptableObject.CreateInstance<GameDatabase>();
            _created.Add(database);
            database.EditorSetReleased(true);
            database.EditorAddRename(typeof(SpriteFrameData).FullName, "potion", "potionSmall");
            var small = Make<SpriteFrameData>("potionSmall");
            var reused = Make<SpriteFrameData>("potion");
            var plan = Plan(new GameData[] { small }, new Dictionary<GameData, GameData>(), new Dictionary<GameData, string>(), database, reused);
            Assert.AreEqual(1, plan.Problems.Count, "새 항목이 예전 ID를 쓰면 예전 저장이 그 항목으로 읽힌다");

            // 같은 항목이 예전 이름으로 돌아가는 것은 된다(이력이 그 항목으로 이어져 있다).
            plan = Plan(new GameData[] { small }, new Dictionary<GameData, GameData> { [small] = Copy(small, "potion") },
                new Dictionary<GameData, string> { [small] = "potion" }, database);
            Assert.IsEmpty(plan.Problems, string.Join("\n", plan.Problems));

            // 이번 저장에서 비우는 ID도(저장하면 이력에 들어간다).
            var a = Make<SpriteFrameData>("a");
            var b = Make<SpriteFrameData>("b");
            plan = Plan(new GameData[] { a, b }, new Dictionary<GameData, GameData> { [a] = Copy(a, "x"), [b] = Copy(b, "a") },
                new Dictionary<GameData, string> { [a] = "x", [b] = "a" }, database);
            Assert.AreEqual(1, plan.Problems.Count, string.Join("\n", plan.Problems));
        }

        [Test]
        public void ID_바꾸기는_가리키는_칸의_작업본도_고치고_중복이면_옮기지_않는다()
        {
            var frame = Staging.Add(typeof(SpriteFrameData), "testRenameFrameA");
            var rule = (FieldRuleData)Staging.Add(typeof(FieldRuleData), "testRenameRule");
            rule.spriteFrame = "testRenameFrameA";
            Assert.AreEqual(1, Staging.Rename(frame, "testRenameFrameB"));
            Assert.AreEqual("testRenameFrameB", frame.Id);
            Assert.AreEqual("testRenameFrameB", rule.spriteFrame);

            // 같은 ID가 둘(복제한 에셋): 참조는 남은 항목을 그대로 가리킨다.
            var twin = Staging.Add(typeof(SpriteFrameData), "testRenameFrameC");
            twin.EditorAssignId("testRenameFrameB");
            Assert.AreEqual(-1, Staging.Rename(twin, "testRenameFrameD"));
            Assert.AreEqual("testRenameFrameB", rule.spriteFrame);
        }

        [Test]
        public void 같은_종류에서_코드_이름이_같아지는_ID는_쓸_수_없다()
        {
            Staging.Add(typeof(SpriteFrameData), "testTwin.dragon");
            var db = EditorDatabase.Instance;
            Assert.IsFalse(db.CanUseId("testTwinDragon", null, typeof(SpriteFrameData), out var error), "GameIds에서 둘 다 TestTwinDragon");
            StringAssert.Contains("TestTwinDragon", error);
            Assert.IsTrue(db.CanUseId("testTwinDragon", null, typeof(FieldRuleData), out _), "종류가 다르면 GameIds 안쪽 클래스가 달라 괜찮다");
            Assert.Throws<System.ArgumentException>(() => Staging.Add(typeof(SpriteFrameData), "testTwinDragon"));
        }

        [Test]
        public void 숨은_칸만_바꾼_작업본도_변경이다()
        {
            var original = ScriptableObject.CreateInstance<FieldRuleData>();
            original.EditorAssignId("testHiddenOnly");
            _created.Add(original);
            var work = (FieldRuleData)Staging.Work(original);
            work.keywordGroup = "legacy";
            Assert.IsTrue(Staging.HasChanges, "적용할 때 버려지지 않고, DB 반영 스크립트도 '바뀐 것 없음'으로 기록하지 않는다");
            Assert.IsTrue(Staging.Changes().Single().Fields.Any(f => f.Path == "keywordGroup"));
        }

        [Test]
        public void 새_항목은_적용_전에는_DB에만_보인다()
        {
            var added = Staging.Add(typeof(SpriteFrameData), "testStagedGroup");
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
            var added = Staging.Add(typeof(SpriteFrameData), "testStagedKeyword");
            Staging.Remove(added);
            Assert.IsFalse(Staging.HasChanges);
        }

        [Test]
        public void 작업본을_고치면_필드별_변경이_나온다()
        {
            var original = ScriptableObject.CreateInstance<FieldRuleData>();
            original.EditorAssignId("testOriginalKeyword");
            original.targetType = "a";
            try
            {
                var work = (FieldRuleData)Staging.Work(original);
                Assert.AreNotSame(original, work);
                Assert.AreSame(work, Staging.Work(work), "작업본을 다시 고쳐도 같은 작업본");
                Assert.IsFalse(Staging.IsPending(work), "열기만 하고 고치지 않으면 변경이 아니다");

                work.targetType = "b";
                work.keywordGroup = "x";
                var change = Staging.Changes().Single();
                Assert.AreEqual(PendingKind.Modified, change.Kind);
                Assert.IsTrue(change.Fields.Any(f => f.Path == "targetType"));
                Assert.IsTrue(change.Fields.Any(f => f.Path == "keywordGroup"), "숨긴 필드도 변경으로 보인다");
                Assert.AreEqual("a", original.targetType, "적용 전에는 원본이 그대로");
            }
            finally
            {
                Staging.Discard();
                Object.DestroyImmediate(original);
            }
        }
    }
}
