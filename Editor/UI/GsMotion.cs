using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace GBible.EditorTools.UI
{
    // 모션 토큰과 애니메이션 도우미. DOTween 없이 USS 트랜지션 + 자체 스프링으로 만든다.
    public static class GsMotion
    {
        public const int StaggerMs = 28;

        // 요소들이 순서대로 떠오르며 나타난다.
        public static void Appear(IEnumerable<VisualElement> elements, int stepMs = StaggerMs, int maxDelayMs = 420)
        {
            int index = 0;
            foreach (var element in elements)
            {
                element.AddToClassList("gs-appear");
                element.AddToClassList("gs-appear--hidden");
                var delay = Mathf.Min(index * stepMs, maxDelayMs) + 16;
                element.schedule.Execute(() => element.RemoveFromClassList("gs-appear--hidden")).StartingIn(delay);
                index++;
            }
        }

        public static void Appear(VisualElement container) => Appear(container.Children());

        // 누르면 살짝 눌렸다가 튕기며 돌아오는 스프링.
        public static void AddPressSpring(VisualElement element, float pressedScale = 0.94f)
        {
            var spring = new Spring(1f) { Stiffness = 420f, Damping = 18f };
            IVisualElementScheduledItem loop = null;

            void EnsureLoop()
            {
                if (loop != null) return;
                loop = element.schedule.Execute(() =>
                {
                    var value = spring.Step(1f / 60f);
                    element.style.scale = new Scale(new Vector3(value, value, 1f));
                    if (spring.IsSettled)
                    {
                        element.style.scale = new Scale(new Vector3(spring.Target, spring.Target, 1f));
                        loop.Pause();
                        loop = null;
                    }
                }).Every(16);
            }

            element.RegisterCallback<PointerDownEvent>(_ =>
            {
                spring.Target = pressedScale;
                EnsureLoop();
            });
            element.RegisterCallback<PointerUpEvent>(_ =>
            {
                spring.Target = 1f;
                spring.Velocity += 2.5f;
                EnsureLoop();
            });
            element.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (Mathf.Approximately(spring.Target, 1f)) return;
                spring.Target = 1f;
                EnsureLoop();
            });
        }

        // 감쇠 스프링(질량 1). 목표값으로 튕기듯 다가간다.
        public sealed class Spring
        {
            public float Value;
            public float Velocity;
            public float Target;
            public float Stiffness = 300f;
            public float Damping = 20f;

            public Spring(float value)
            {
                Value = value;
                Target = value;
            }

            public float Step(float dt)
            {
                var force = -Stiffness * (Value - Target) - Damping * Velocity;
                Velocity += force * dt;
                Value += Velocity * dt;
                return Value;
            }

            public bool IsSettled => Mathf.Abs(Value - Target) < 0.001f && Mathf.Abs(Velocity) < 0.01f;
        }
    }
}
