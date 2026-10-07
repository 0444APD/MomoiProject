using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace GBible.Tests
{
    // 화면 쪽: 다시 그리기의 순서 · 목록 칸(ElementList) · binding-path. 에디터 모드에서 돈다 —
    // 연출 대기열은 플레이 중에만 돌아(호스트의 코루틴) 여기서는 넣기와 남은 것까지만 본다.
    public class PresentationTests
    {
        [SetUp]
        public void SetUp() => GameView.Reset();

        [TearDown]
        public void TearDown() => GameView.Reset();

        [Test]
        public void 같은_프레임에_넣은_사라지는_연출이_있으면_씬_흐름은_그_뒤로_미룬다()
        {
            var opened = 0;
            using (GameView.RedrawAfterPresentation(() => opened++))   // 씬 흐름(게임이 켜질 때 먼저 등록된다)
            using (GameView.Redraw(() => GameView.Present(0.5f)))      // 적 목록: 사라진 적의 죽는 모습을 넣는다
            {
                GameView.FrameEnd();
                Assert.AreEqual(0, opened, "죽는 모습이 남아 있으면 씬을 열지 않는다");
                Assert.IsTrue(GameView.IsPresenting);
                GameView.ClearPresentation();                           // (플레이 중이면 죽는 모습이 끝나 대기열이 빈다)
                GameView.FrameEnd();
                Assert.AreEqual(1, opened, "대기열이 빈 프레임 끝에 연다");
            }
        }

        [Test]
        public void 목록_칸은_id가_없는_항목도_자리_번호로_만든다()
        {
            var container = new VisualElement();
            var items = new List<int> { 5, 6, 7 };
            var bound = new List<int>();
            using (var list = new ElementList<int>(container, null, () => items, (element, n) => bound.Add(n)))
            {
                GameView.FrameEnd();
                Assert.AreEqual(3, container.childCount);
                CollectionAssert.AreEqual(new[] { 5, 6, 7 }, bound);
                Assert.IsNotNull(list.Find("$0"));
            }
        }

        [Test]
        public void 목록_칸의_OnCreate가_예외를_내도_나머지_칸은_그린다()
        {
            var container = new VisualElement();
            var items = new List<string> { "a", "b", "c" };
            var bound = new List<string>();
            using (var list = new ElementList<string>(container, null, () => items, (element, id) => bound.Add(id)))
            {
                list.OnCreate = (element, id) =>
                {
                    if (id == "b") throw new InvalidOperationException("OnCreate 시험");
                };
                LogAssert.Expect(LogType.Exception, new Regex("OnCreate 시험"));
                GameView.FrameEnd();
                Assert.AreEqual(3, container.childCount);
                CollectionAssert.AreEqual(new[] { "a", "b", "c" }, bound);
            }
        }

        [Test]
        public void 목록을_정리하면_사라지는_연출을_기다리는_칸도_치운다()
        {
            var container = new VisualElement();
            var items = new List<string> { "a", "b" };
            var list = new ElementList<string>(container, null, () => items, (element, id) => { });
            list.OnRemove = (element, done) => { };   // done을 부르지 않는 연출
            GameView.FrameEnd();
            items.Remove("b");
            GameView.RedrawAll();
            GameView.FrameEnd();
            Assert.AreEqual(2, container.childCount, "사라지는 연출의 차례를 기다리는 칸은 아직 있다");
            Assert.IsTrue(GameView.IsPresenting);
            list.Dispose();
            Assert.AreEqual(0, container.childCount, "정리하면 사라지는 중인 칸도 치운다");
        }

        [Test]
        public void 연출_대기열을_버리면_차례를_기다리던_사라지는_칸은_바로_치운다()
        {
            var container = new VisualElement();
            var items = new List<string> { "a", "b" };
            using (var list = new ElementList<string>(container, null, () => items, (element, id) => { }))
            {
                list.OnRemove = (element, done) => { };   // done을 부르지 않는 연출
                GameView.FrameEnd();
                items.Remove("b");
                GameView.RedrawAll();
                GameView.FrameEnd();
                Assert.AreEqual(2, container.childCount, "사라지는 연출의 차례를 기다리는 칸은 아직 있다");
                GameView.ClearPresentation();
                Assert.AreEqual(1, container.childCount, "버린 사라짐은 31초 안전장치를 기다리지 않고 바로 치운다");
                Assert.IsFalse(GameView.IsPresenting);
                items.Add("b");   // Undo로 되살아남
                GameView.RedrawAll();
                GameView.FrameEnd();
                Assert.AreEqual(2, container.childCount, "되살아난 항목은 새 칸 하나 — 옛 칸과 겹치지 않는다");
            }
        }

        [Test]
        public void 토글_슬라이더에_텍스트_키만_적으면_값은_그대로_두고_이름표를_번역한다()
        {
            var toggle = new Toggle();
            toggle.SetValueWithoutNotify(true);
            GameScreens.Apply(toggle, ScreenBinding.Parse("@hud.auto"), null, null);
            Assert.IsTrue(toggle.value, "값이 없는 연결은 체크를 덮지 않는다");
            Assert.AreEqual(Texts.Get("hud.auto"), toggle.label);

            var slider = new Slider(0f, 1f);
            slider.SetValueWithoutNotify(0.7f);
            GameScreens.Apply(slider, ScreenBinding.Parse("@hud.volume"), null, null);
            Assert.AreEqual(0.7f, slider.value, 1e-5f);
            Assert.AreEqual(Texts.Get("hud.volume"), slider.label);
        }
    }
}
