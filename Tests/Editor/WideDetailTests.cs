using System.Collections.Generic;
using System.Linq;
using GameSystems.EditorTools;
using GameSystems.EditorTools.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameSystems.Tests
{
    // 필드 종류·개수가 많은 데이터로 상세 패널을 띄워 입력 칸·스크롤·배치 편집이 모두 동작하는지 본다.
    // 배치 블록이나 입력 칸 모양을 바꿀 때마다 이 테스트가 함께 돌아 회귀를 잡는다.
    public class WideDetailTests
    {
        WideTestData _data;
        VisualElement _host;
        GsDetailPanel _panel;

        [SetUp]
        public void SetUp()
        {
            _data = ScriptableObject.CreateInstance<WideTestData>();
            _data.hideFlags = HideFlags.DontSave;
            _data.longText = string.Join("\n", Enumerable.Range(1, 12).Select(i => $"{i}번째 줄"));
            _host = new VisualElement();
            Gs.Apply(_host);
            _host.style.width = 1200;
            _host.style.height = 800;
            _panel = GsDetailPanel.Open(_host, _data, new DetailOptions { TitlePath = null, Title = "wideTest", ShowMedia = false });
        }

        [TearDown]
        public void TearDown()
        {
            _panel?.Close();
            Object.DestroyImmediate(_data);
        }

        GsField Field(string label) => _panel.Query<GsField>().ToList().FirstOrDefault(f => f.Label == label);

        [Test]
        public void 본문은_스크롤된다()
        {
            Assert.IsNotNull(_panel.Q<ScrollView>(className: "gs-detail__body"));
        }

        [Test]
        public void 값_하나짜리_필드는_모두_입력_칸이다()
        {
            foreach (var label in new[] { "정수", "큰 정수", "실수", "배정밀도", "짧은 글", "긴 글", "빈 글", "참거짓", "enum", "프리팹", "색", "추가 1", "추가 3", "추가 6" })
                Assert.IsNotNull(Field(label), $"'{label}' 입력 칸이 없습니다");
        }

        [Test]
        public void 클래스_안쪽_값도_입력_칸이다()
        {
            Assert.IsNotNull(Field("안쪽 이름"), "클래스 안쪽 값도 입력 칸 모양이어야 합니다");
        }

        [Test]
        public void 빈_값은_비어_있음_모양()
        {
            Assert.IsTrue(Field("빈 글").ClassListContains("gs-field--empty"));
            Assert.IsFalse(Field("짧은 글").ClassListContains("gs-field--empty"));
        }

        [Test]
        public void 긴_글은_5줄까지만_늘어난다()
        {
            var scroll = Field("긴 글").Q<ScrollView>(className: "gs-inline__scroll");
            Assert.IsNotNull(scroll, "긴 값은 칸 안에서 스크롤돼야 합니다");
            Assert.AreEqual(GsField.MaxValueHeight, scroll.style.maxHeight.value.value, 0.01f);
        }

        [Test]
        public void 오류는_칸에_표시된다()
        {
            var field = Field("정수");
            field.SetError("정수가 너무 큽니다");
            Assert.IsTrue(field.ClassListContains("gs-field--error"));
            field.SetError(null);
            Assert.IsFalse(field.ClassListContains("gs-field--error"));
        }

        [Test]
        public void 모든_배치_블록을_어떤_배치로_놓아도_그려진다()
        {
            var view = _panel.Q<GsDataView>();
            Assert.IsNotNull(view);
            var ids = GsLayoutBlock.Registry.Keys.ToList();
            // 블록마다 혼자 · 반폭 둘씩 · 역순으로 놓아 본다.
            var layouts = new List<TypeLayout>();
            foreach (var id in ids)
                layouts.Add(new TypeLayout { blocks = { new BlockPlacement { block = id, x = 0, y = 0, w = 12, path = "icon", paths = { "label" } } } });
            layouts.Add(new TypeLayout { blocks = ids.Select((id, i) => new BlockPlacement { block = id, x = (i % 2) * 6, y = i / 2, w = 6, path = "nested" }).ToList() });
            layouts.Add(new TypeLayout { blocks = ids.AsEnumerable().Reverse().Select((id, i) => new BlockPlacement { block = id, y = i, w = 12 }).ToList() });
            foreach (var layout in layouts)
            {
                Assert.DoesNotThrow(() => view.Preview(layout));
                var broken = view.Query<GsNotice>().ToList().Select(n => n.Q<Label>()?.text ?? "").Where(t => t.Contains("블록 오류")).ToList();
                Assert.IsEmpty(broken, string.Join("\n", broken));
            }
        }

        [Test]
        public void 배치_편집_창이_열린다()
        {
            var view = _panel.Q<GsDataView>();
            Assert.DoesNotThrow(() => GsLayoutEditor.Open(_host, view.Context, () => { }));
        }
    }
}
