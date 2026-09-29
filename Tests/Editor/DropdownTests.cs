using System.Collections.Generic;
using System.Linq;
using GameSystems.EditorTools.UI;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace GameSystems.Tests
{
    public class DropdownTests
    {
        [TestCase("처치", true)]
        [TestCase("ㅊㅊ", true)]
        [TestCase("ㅊㅊ ㅅ", true)]
        [TestCase("stat_0001", true)]
        [TestCase("골드", false)]
        [TestCase("ㄱㄷ", false)]
        public void 이름_캡션_초성으로_검색된다(string query, bool expected)
        {
            var item = new GsDropdownItem("처치 수 (누적)") { Caption = "stat_0001", Group = "수치" };
            Assert.AreEqual(expected, GsDropdown.Matches(item, query));
        }

        [Test]
        public void 초성_추출()
        {
            Assert.AreEqual("ㅊㅊㅅ(ㄴㅈ)", GsDropdown.InitialsOf("처치 수 (누적)"));
        }

        [Test]
        public void 목록을_그리고_고르면_실행되고_닫힌다()
        {
            var host = new VisualElement();
            Gs.Apply(host);
            var anchor = new VisualElement();
            host.Add(anchor);

            string picked = null;
            var items = new List<GsDropdownItem>
            {
                new GsDropdownItem("이상", () => picked = "이상") { Selected = true },
                new GsDropdownItem("이하", () => picked = "이하"),
                new GsDropdownItem("사용 불가") { Disabled = true },
            };
            var dropdown = GsDropdown.Show(anchor, items);
            Assert.IsNotNull(dropdown);
            var rows = dropdown.Query(className: "gs-dropdown__item").ToList();
            Assert.AreEqual(3, rows.Count);
            Assert.AreEqual(1, dropdown.Query(className: "gs-dropdown__item--selected").ToList().Count, "현재 값 표시");
            Assert.IsNull(dropdown.Q<GsSearchField>(), "항목이 적으면 검색창이 없다");

            dropdown.PickVisible(1);
            Assert.AreEqual("이하", picked);
        }

        [Test]
        public void 항목이_많으면_검색창이_생긴다()
        {
            var host = new VisualElement();
            Gs.Apply(host);
            var anchor = new VisualElement();
            host.Add(anchor);
            var items = Enumerable.Range(0, 12).Select(i => new GsDropdownItem($"항목 {i}")).ToList();
            var dropdown = GsDropdown.Show(anchor, items);
            Assert.IsNotNull(dropdown.Q<GsSearchField>());
        }
    }
}
