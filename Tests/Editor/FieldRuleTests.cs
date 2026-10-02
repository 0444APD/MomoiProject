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
        public void 키워드는_그룹별로_코드가_만들어진다()
        {
            var group = Make<KeywordGroupData>("grade", "등급");
            var legendary = Make<KeywordData>("legendary", "전설");
            legendary.group = group.Id;
            var common = Make<KeywordData>("common", "일반");
            common.group = group.Id;
            var code = GameIdsGenerator.Build(new List<GameData> { group, legendary, common }, "GBible.Tests.Editor");
            StringAssert.Contains("public static class Keyword", code);
            StringAssert.Contains("public static class Grade", code);
            StringAssert.Contains("public const string Legendary = \"legendary\";", code);
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
            rule.targetType = typeof(KeywordData).FullName;
            rule.field = nameof(KeywordData.textStyle);
            rule.source = FieldRuleSource.Keyword;
            rule.required = true;
            Assert.IsEmpty(DataValidator.Validate(new List<GameData> { rule }).Where(i => i.Asset == rule && i.Severity == ValidationSeverity.Error),
                "규칙 자체는 올바르다");
        }
    }
}
