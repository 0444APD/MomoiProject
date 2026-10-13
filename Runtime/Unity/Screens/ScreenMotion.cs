using System;
using UnityEngine.UIElements;

namespace GBible
{
    // 화면 움직임(AI/UiGuide.md 3-5): 씬 전환을 뺀 모든 움직임은 짧게 한 모양으로 — 패널은 InSeconds에 나타나고 OutSeconds에 사라진다.
    // 나타날 때는 투명도 0→100%와 함께 짧은 거리(Distance px)를 옆·아래에서 제자리로, 사라질 때는 투명도만(미끄러지지 않음). 모양(시간·거리)은 Screens.uss의 움직임 클래스가 정하고,
    // 이 클래스는 그 클래스를 붙였다 떼는 순서만 맡는다(USS의 시간과 같은 값을 쓴다).
    //   화면 전체: GameScreens가 열 때 Enter, 닫을 때 Leave를 부른다(창·팝업·바탕 모두. 알림은 따로).
    //   화면 안의 패널: 패널 요소에 클래스 momoi-motion(+ --left·--right·--down — 오는 쪽, 없으면 아래에서)을 붙이면 화면과 함께 움직이고,
    //   열린 화면 안에서 패널 하나를 보이고 숨길 때는 ScreenMotion.Show(요소, 참거짓).
    //   translate로 가운데 맞춘 요소에는 붙이지 않는다(움직이는 동안 translate를 덮는다 — margin으로 가운데 맞춘다).
    public static class ScreenMotion
    {
        public const float InSeconds = 0.189f;   // 개발자 2026-10-13: 0.14초는 짧고 1.5배는 과해 1.35배
        public const float OutSeconds = 0.135f;
        public const float Distance = 14f;

        public const string EnterClass = "momoi-screen--enter";
        public const string LeaveClass = "momoi-screen--leave";
        public const string PanelClass = "momoi-motion";
        public const string HiddenClass = "momoi-motion--hidden";
        public const string PreClass = "momoi-motion--pre";   // 나타나기 전(시간 0 — 오는 쪽에 둔다)

        // 켜면 움직임을 쓴다(테스트·자동 시뮬레이션에서 끌 수 있다).
        public static bool Enabled = true;

        // 화면을 붙인 직후: 처음 그려질 때 '나타나기 전' 모습으로 두었다가 다음 프레임에 떼어 USS 전환으로 나타난다.
        public static void Enter(VisualElement host)
        {
            if (host == null || !Enabled) return;
            host.AddToClassList(EnterClass);
            void Ready(GeometryChangedEvent e)
            {
                host.UnregisterCallback<GeometryChangedEvent>(Ready);
                host.schedule.Execute(() => host.RemoveFromClassList(EnterClass)).StartingIn(0);
            }
            host.RegisterCallback<GeometryChangedEvent>(Ready);
        }

        // 화면을 떼기 전: 사라지는 모습으로 바꾸고 OutSeconds 뒤에 떼어 낸다. 그동안 누름은 막는다. 끄면 바로 뗀다.
        public static void Leave(VisualElement host, Action removed = null)
        {
            if (host == null) return;
            if (!Enabled || host.panel == null)
            {
                host.RemoveFromHierarchy();
                removed?.Invoke();
                return;
            }
            host.RegisterCallback<PointerDownEvent>(Swallow, TrickleDown.TrickleDown);
            host.RegisterCallback<ClickEvent>(Swallow, TrickleDown.TrickleDown);
            host.RemoveFromClassList(EnterClass);
            host.AddToClassList(LeaveClass);
            host.schedule.Execute(() =>
            {
                host.RemoveFromHierarchy();
                removed?.Invoke();
            }).StartingIn((long)(OutSeconds * 1000) + 20);
        }

        // 열린 화면 안의 패널 하나를 보이고 숨긴다(요소에 momoi-motion이 있어야 움직인다 — 없으면 바로).
        public static void Show(VisualElement element, bool visible)
        {
            if (element == null) return;
            var animated = Enabled && element.ClassListContains(PanelClass) && element.panel != null;
            if (visible)
            {
                if (Shown(element) && !element.ClassListContains(HiddenClass)) return;
                element.style.display = DisplayStyle.Flex;
                element.RemoveFromClassList(HiddenClass);
                if (!animated) return;
                element.AddToClassList(PreClass);
                element.schedule.Execute(() => element.RemoveFromClassList(PreClass)).StartingIn(16);
                return;
            }
            if (!Shown(element)) return;
            if (!animated)
            {
                element.style.display = DisplayStyle.None;
                return;
            }
            element.AddToClassList(HiddenClass);
            element.schedule.Execute(() =>
            {
                if (element.ClassListContains(HiddenClass)) element.style.display = DisplayStyle.None;
            }).StartingIn((long)(OutSeconds * 1000) + 20);
        }

        static void Swallow(EventBase e) => e.StopImmediatePropagation();

        // 인라인으로 정한 값이 있으면 그것, 없으면 USS가 정한 값.
        static bool Shown(VisualElement element) => element.style.display.keyword == StyleKeyword.Undefined
            ? element.style.display.value != DisplayStyle.None
            : element.resolvedStyle.display != DisplayStyle.None;
    }
}
