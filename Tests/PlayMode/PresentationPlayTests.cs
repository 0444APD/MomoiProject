using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace GBible.Tests
{
    // 화면 쪽 연출 대기열을 플레이 중에(호스트의 코루틴으로) 돌려 본다: 씬 흐름의 기다리기 · 멈춘 연출 넘기기 · 버리기 · 씬 열기 · 배속 알리기 · 확인 창 다시 열기.
    // 플레이에 들어가면 GBibleRuntime이 이 프로젝트의 DB로 켜진다 — 시험마다 대기열이 빈 뒤에 시작하고, 끝에 버리고 비울 때까지 기다린다.
    public sealed class PresentationPlayTests
    {
        const string PauseReason = "시험: 연출";
        const string Unchanging = "$test.unchanging";
        readonly List<Object> _made = new List<Object>();
        float _speed;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Assume.That(GBibleHost.Instance != null, "게임이 켜져 있어야 한다(GBibleRuntime — 플레이에 들어가면 저절로)");
            _speed = GameTime.Speed;
            GameView.ClearPresentation();
            yield return Idle(10f);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            GameView.StallSeconds = GameView.DefaultStallSeconds;
            GameTime.Resume(PauseReason);
            GameTime.SetSpeed(_speed);
            foreach (var made in _made)
                if (made != null) Object.Destroy(made);
            _made.Clear();
            GameView.ClearPresentation();
            yield return Idle(10f);
        }

        [UnityTest]
        public IEnumerator 같은_프레임에_넣은_연출이_끝난_뒤에_씬_흐름의_처음_그리기가_돈다()
        {
            var log = new List<string>();
            // 감시 키는 바뀌지 않는 이름 — 처음 그리기 한 번씩만(다른 값이 바뀌어도 다시 그리지 않게).
            var scene = GameView.RedrawAfterPresentation(() => log.Add("scene"), Unchanging);                        // 씬 흐름(게임이 켜질 때 먼저 등록된다)
            var enemies = GameView.Redraw(() => GameView.Present(After(0.2f, () => log.Add("death"))), Unchanging);   // 적 목록: 마지막 적의 죽는 모습
            try
            {
                for (var until = Time.realtimeSinceStartup + 5f; log.Count < 2 && Time.realtimeSinceStartup < until;) yield return null;
                CollectionAssert.AreEqual(new[] { "death", "scene" }, log, "죽는 모습이 끝난 뒤에 씬을 연다(씬과 함께 오브젝트가 없어지지 않게)");
            }
            finally
            {
                scene.Dispose();
                enemies.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator 다른_오브젝트의_코루틴을_기다리다_그_오브젝트가_없어져도_대기열은_넘어간다()
        {
            GameView.StallSeconds = 0.5f;
            var other = NewHost();
            GameView.Present(WaitFor(other, () => { }));
            yield return null;
            Assert.IsTrue(GameView.IsPresenting);
            Object.Destroy(other.gameObject);   // Unity는 그 코루틴을 기다리던 연출을 다시 깨우지 않는다
            yield return Idle(5f);
            Assert.IsFalse(GameView.IsPresenting, "나아가지 않는 연출은 정해진 시간 뒤 버리고 넘어간다(Busy가 풀린다)");
        }

        [UnityTest]
        public IEnumerator 일시정지_동안은_멈춘_연출의_시간을_세지_않는다()
        {
            GameView.StallSeconds = 0.5f;
            var other = NewHost();
            GameView.Present(WaitFor(other, () => { }));
            yield return null;
            Object.Destroy(other.gameObject);
            GameTime.Pause(PauseReason);
            yield return new WaitForSecondsRealtime(1.2f);
            Assert.IsTrue(GameView.IsPresenting, "멈춘 동안은 넘기지 않는다(일시정지가 길어도 연출을 건너뛰지 않게)");
            GameTime.Resume(PauseReason);
            yield return Idle(5f);
            Assert.IsFalse(GameView.IsPresenting, "다시 흐르면 정해진 시간 뒤 넘어간다");
        }

        [UnityTest]
        public IEnumerator done을_부르지_않는_연출은_정해진_시간_뒤_넘어간다()
        {
            GameView.Present(done => { }, 0.3f);
            yield return null;
            Assert.IsTrue(GameView.IsPresenting);
            LogAssert.Expect(LogType.Warning, new Regex("안에 끝나지 않아 넘어갑니다"));
            yield return Idle(5f);
            Assert.IsFalse(GameView.IsPresenting);
        }

        [UnityTest]
        public IEnumerator 버리면_재생_중인_연출은_다음_걸음에서_멈추고_finally가_돈다()
        {
            var steps = 0;
            var cleaned = false;
            GameView.Present(Endless(() => steps++, () => cleaned = true));
            yield return null;
            yield return null;
            Assert.IsTrue(GameView.IsPresenting);
            GameView.ClearPresentation();
            Assert.IsFalse(GameView.IsPresenting, "버리면 바로 빈다(입력이 풀린다)");
            var atClear = steps;
            yield return null;
            yield return null;
            Assert.IsTrue(cleaned, "멈춘 연출의 finally(using)는 돈다 — 만든 것을 치울 길");
            Assert.LessOrEqual(steps, atClear, "버린 뒤로는 나아가지 않는다");
        }

        [UnityTest]
        public IEnumerator 버려도_씬_열기는_끝까지_돌고_다_연_뒤_Presented가_한_번_온다()
        {
            var opened = 0;
            var presented = 0;
            var dropped = false;
            Action onPresented = () => presented++;
            GameView.Presented += onPresented;
            try
            {
                GameView.PresentSceneChange(Frames(5, () => opened++));   // 씬 흐름의 씬 열기
                GameView.Present(After(0f, () => dropped = true));        // 그 뒤에 기다리는 연출
                yield return null;
                GameView.ClearPresentation();
                Assert.IsTrue(GameView.IsPresenting, "씬을 여는 중이면 그것은 끝까지 연다(페이드가 어두운 채 남지 않게)");
                yield return Idle(5f);
                Assert.IsFalse(GameView.IsPresenting);
                Assert.AreEqual(5, opened, "씬 열기를 끝까지 돌았다");
                Assert.IsFalse(dropped, "뒤에 기다리던 연출은 버렸다");
                Assert.AreEqual(1, presented, "다 연 뒤 대기열이 비어 한 번");
            }
            finally { GameView.Presented -= onPresented; }
        }

        [UnityTest]
        public IEnumerator 버리면_차례를_기다리던_사라지는_오브젝트를_바로_없앤다()
        {
            var prefab = new GameObject("시험 프리팹");
            prefab.SetActive(false);
            _made.Add(prefab);
            var parent = new GameObject("시험 목록");
            _made.Add(parent);
            var items = new List<string> { "a" };
            var list = new ViewList<string, Transform>(prefab.transform, parent.transform, () => items, (v, id) => { }, false);
            try
            {
                list.OnRemove = (v, done) => done();
                yield return null;
                var view = list.ViewOf("a");
                Assert.IsNotNull(view);
                GameView.Present(Endless(() => { }, () => { }));   // 앞 연출이 대기열을 잡고 있다
                items.Clear();
                list.Refresh();
                yield return null;
                Assert.IsTrue(view != null, "사라짐은 앞 연출 뒤 차례를 기다린다");
                GameView.ClearPresentation();
                yield return null;
                Assert.IsTrue(view == null, "버린 사라짐의 오브젝트는 31초 안전장치를 기다리지 않고 없앤다");
            }
            finally { list.Dispose(); }
        }

        [UnityTest]
        public IEnumerator 배속을_올리면_세션의_한_프레임_틱_한도도_따라_오른다()
        {
            var session = GBibleRuntime.Session;
            Assume.That(session != null, "플레이 중 세션이 있어야 한다");
            GameTime.SetSpeed(16f);
            yield return null;
            Assert.AreEqual(16.0, session.HostSpeed, 1e-6, "GameTime이 배속을 세션에 알린다");
            Assert.AreEqual(GameSession.MaxTicksPerFrameAtSpeed, session.TickLimit, "×16 → 8 × 16 = 128틱");
            GameTime.SetSpeed(1f);
            yield return null;
            Assert.AreEqual(GameSession.MaxTicksPerFrame, session.TickLimit);
        }

        [UnityTest]
        public IEnumerator 확인_창을_뒤로로_닫을_때_아니오가_띄운_새_확인_창이_열린_채_남는다()
        {
            if (GameScreens.Overlay == null) Assert.Ignore("화면(UI Toolkit)이 준비되지 않은 실행입니다.");
            var events = new List<string>();
            var secondYes = 0;
            Action<GameScreen> onOpened = s => { if (s.Id == GameScreens.Confirmation) events.Add("opened"); };
            Action<GameScreen> onClosed = s => { if (s.Id == GameScreens.Confirmation) events.Add("closed"); };
            GameScreens.Opened += onOpened;
            GameScreens.Closed += onClosed;
            try
            {
                GameScreens.Confirm("시험: 첫 창", null, () => GameScreens.Confirm("시험: 두 번째 창", () => secondYes++));
                yield return null;
                events.Clear();
                Assert.IsTrue(GameScreens.Back(), "맨 위 확인 창을 뒤로(Esc)로 닫는다 = 아니오");
                CollectionAssert.AreEqual(new[] { "closed", "opened" }, events, "닫힘을 먼저, 아니오가 다시 연 창의 열림을 그 뒤에 알린다");
                var screen = GameScreens.Get(GameScreens.Confirmation);
                Assert.IsNotNull(screen, "아니오가 띄운 창이 열려 있다");
                Assert.IsTrue(screen.IsOpen);
                Assert.IsNotNull(screen.Root?.panel, "새 창의 칸이 화면에 붙어 있다(닫힌 창의 칸과 함께 떨어지지 않았다)");
                yield return null;
                var buttons = screen.Root.Query<Button>().ToList();
                Assert.IsNotEmpty(buttons);
                Press(buttons[buttons.Count - 1]);   // '예'(마지막 버튼)
                yield return null;
                Assert.AreEqual(1, secondYes, "새 창의 버튼이 돈다(닫힌 창의 버튼 연결 정리에 끊기지 않았다)");
                Assert.IsFalse(GameScreens.IsOpen(GameScreens.Confirmation));
            }
            finally
            {
                GameScreens.Opened -= onOpened;
                GameScreens.Closed -= onClosed;
                GameScreens.Close(GameScreens.Confirmation);
            }
        }

        // ── 도우미 ───────────────────────────────────────────────

        CoroutineHost NewHost()
        {
            var go = new GameObject("시험: 다른 오브젝트");
            Object.DontDestroyOnLoad(go);
            _made.Add(go);
            return go.AddComponent<CoroutineHost>();
        }

        // 대기열이 빌 때까지(실제 초로 최대 seconds).
        static IEnumerator Idle(float seconds)
        {
            for (var until = Time.realtimeSinceStartup + seconds; GameView.IsPresenting && Time.realtimeSinceStartup < until;) yield return null;
        }

        static IEnumerator After(float seconds, Action then)
        {
            for (var t = 0f; t < seconds; t += Time.unscaledDeltaTime) yield return null;
            then();
        }

        static IEnumerator Frames(int count, Action each)
        {
            for (var i = 0; i < count; i++)
            {
                each();
                yield return null;
            }
        }

        static IEnumerator Endless(Action step, Action cleanup)
        {
            try
            {
                while (true)
                {
                    step();
                    yield return null;
                }
            }
            finally { cleanup(); }
        }

        // 다른 오브젝트에서 돌린 코루틴을 기다리는 연출(yield return x.StartCoroutine(…)).
        static IEnumerator WaitFor(MonoBehaviour other, Action after)
        {
            yield return other.StartCoroutine(Endless(() => { }, () => { }));
            after();
        }

        // 버튼 누르기(키보드의 확인과 같은 길 — Button이 clicked를 부른다).
        static void Press(Button button)
        {
            using (var e = NavigationSubmitEvent.GetPooled())
            {
                e.target = button;
                button.SendEvent(e);
            }
        }
    }
}
