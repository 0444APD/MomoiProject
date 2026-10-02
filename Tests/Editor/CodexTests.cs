using System;
using System.Collections.Generic;
using System.Linq;
using GBible.Data;
using GBible.EditorTools.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace GBible.Tests
{
    // 도감: 템플릿·프리팹 항목.
    public class CodexTests
    {
        [Test]
        public void 템플릿은_사본을_만들어_준다()
        {
            var template = ScriptableObject.CreateInstance<TemplateData>();
            try
            {
                template.templateType = typeof(TestLoot).FullName;
                template.value = new TestLoot { name = "훈련용 검", grade = 2, tags = { "dummy" } };
                var copy = template.CreateInstance<TestLoot>();
                Assert.AreEqual("훈련용 검", copy.name);
                Assert.AreNotSame(template.value, copy, "원본을 건드리지 않게 사본을 준다");
                copy.tags.Add("changed");
                CollectionAssert.AreEqual(new[] { "dummy" }, ((TestLoot)template.value).tags);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(template);
            }
        }

        [Test]
        public void 템플릿_형식과_값이_다르면_검증_오류()
        {
            var template = ScriptableObject.CreateInstance<TemplateData>();
            try
            {
                template.EditorAssignId("templateBad");
                template.templateType = typeof(TestLoot).FullName;
                template.value = new TestBag();
                var issues = GBible.EditorTools.DataValidator.Validate(new List<GameData> { template });
                Assert.IsTrue(issues.Any(i => i.Message.Contains("형식")));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(template);
            }
        }

        sealed class PrefabContext : DataViewContext
        {
            public override Type Type => typeof(PrefabData);
            public override bool CanEdit => false;
            public override IEnumerable<string> Fields() => new[] { "prefab" };
            public override VisualElement FieldRow(string path) => new Label(path);
            public override VisualElement FieldValue(string path) => new Label(path);
            public override UnityEngine.Object ObjectAt(string path) => null;
            public override void SetObject(string path, UnityEngine.Object value) { }
        }

        [Test]
        public void 프리팹_항목의_기본_배치에는_컴포넌트_블록이_들어간다()
        {
            var layout = DataLayouts.Default(typeof(PrefabData), new PrefabContext());
            Assert.IsTrue(layout.blocks.Any(b => b.block == PrefabComponentsBlock.Id));
        }
    }
}
