using System.Collections.Generic;
using System.Linq;
using GameSystems.Data;
using GameSystems.EditorTools;
using NUnit.Framework;
using UnityEngine;

namespace GameSystems.Tests
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
            data.EditorSetDisplayName(name);
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
        public void 선택지_목록별로_코드가_만들어진다()
        {
            var list = Make<ChoiceListData>("item_category", "아이템 분류");
            var weapon = Make<ChoiceData>("weapon", "무기");
            weapon.list = list.Id;
            var armor = Make<ChoiceData>("armor", "방어구");
            armor.list = list.Id;
            var code = GameIdsGenerator.Build(new List<GameData> { list, weapon, armor }, "GameSystems.Tests.Editor");
            StringAssert.Contains("public static class ItemCategory", code);
            StringAssert.Contains("public const string Weapon = \"weapon\";", code);
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
            var rule = Make<FieldRuleData>("rule.tag_required", "태그 필수");
            rule.targetType = typeof(StatData).FullName;
            rule.field = "tags";
            rule.source = FieldRuleSource.Tag;
            rule.required = true;
            Assert.IsEmpty(DataValidator.Validate(new List<GameData> { rule }).Where(i => i.Asset == rule && i.Severity == ValidationSeverity.Error),
                "규칙 자체는 올바르다");
        }
    }
}
