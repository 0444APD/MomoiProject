using System.Collections.Generic;
using System.Linq;
using GBible.Data;
using GBible.EditorTools;
using NUnit.Framework;
using UnityEngine;

namespace GBible.Tests
{
    [System.Serializable]
    public sealed class RuleItem
    {
        public string category = "";
        public List<string> tags = new List<string>();
        [Keyword(typeof(TestRarity))] public string grade = "";
    }

    [KeywordGroup("등급")]
    public static class TestRarity
    {
        public const string Common = "common";
        public const string Legendary = "legendary";

        [KeywordGroup]
        public static class Special
        {
            public const string Event = "eventOnly";
        }
    }

    // 필드 규칙: 코드·어트리뷰트 없이 "이 필드는 여기서 고른다(, 필수)"를 연결한다.
    public class FieldRuleTests
    {
        readonly List<Object> _created = new List<Object>();

        T Make<T>(string id, string name) where T : GameData
        {
            var data = ScriptableObject.CreateInstance<T>();
            data.EditorAssignId(id);
            _created.Add(data);
            return data;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void 키워드는_코드에서_모인다()
        {
            Keywords.Reset();
            var group = Keywords.GroupOf(typeof(TestRarity));
            Assert.IsNotNull(group, "[KeywordGroup] static class");
            Assert.AreEqual("등급", group.DisplayName);
            CollectionAssert.AreEqual(new[] { "common", "legendary" }, group.Keywords.Select(k => k.Id));
            Assert.AreEqual("Legendary", Keywords.Find("legendary").Name);
            Assert.AreSame(group, Keywords.GroupOf(typeof(TestRarity.Special)).Parent, "안쪽 클래스 = 하위 그룹");
            Assert.AreEqual(group.Id, KeywordTools.GroupOf(typeof(RuleItem).GetField(nameof(RuleItem.grade))), "[Keyword(typeof(그룹))]");
            Assert.IsNull(KeywordTools.GroupOf(typeof(RuleItem).GetField(nameof(RuleItem.category))));
        }

        [Test]
        public void 문자열_필드는_하나_문자열_목록은_여러_개()
        {
            Assert.IsFalse(KeywordTools.IsMulti(typeof(RuleItem).GetField(nameof(RuleItem.category))));
            Assert.IsTrue(KeywordTools.IsMulti(typeof(RuleItem).GetField(nameof(RuleItem.tags))));
            Assert.IsTrue(KeywordTools.CanConnect(typeof(RuleItem).GetField(nameof(RuleItem.category))));
        }

        [Test]
        public void 여러_ID는_줄바꿈_띄어쓰기_쉼표로_나눈다()
        {
            CollectionAssert.AreEqual(new[] { "legendary", "common", "rare", "epic" }, CodexGroups.ParseIds("legendary\ncommon,  rare epic "));
        }

        [Test]
        public void 규칙이_없는_필드는_참조가_아니다()
        {
            var field = typeof(RuleItem).GetField(nameof(RuleItem.category));
            Assert.IsNull(EditorDatabase.RuleFor(field));
            Assert.IsNull(EditorDatabase.RefTypeOf(field));
        }

        [Test]
        public void 필수_규칙은_검증기가_잡는다()
        {
            var rule = Make<FieldRuleData>("rule.textStyleRequired", "용어 스타일 필수");
            rule.targetType = typeof(RuleItem).FullName;
            rule.field = nameof(RuleItem.category);
            rule.source = FieldRuleSource.Free;
            rule.required = true;
            Assert.IsEmpty(DataValidator.Validate(new List<GameData> { rule }).Where(i => i.Asset == rule && i.Severity == ValidationSeverity.Error),
                "규칙 자체는 올바르다");
        }
    }
}
