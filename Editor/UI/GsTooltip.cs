using UnityEngine;
using UnityEngine.UIElements;

namespace GBible.EditorTools.UI
{
    // 툴팁을 Unity 기본 툴팁 대신 창 안의 상자로 띄운다. 기본 툴팁은 여러 줄·긴 글이 잘려 보여서,
    // 글 양에 맞춰 커지는 상자(가로는 최대 폭에서 줄바꿈, 세로는 글이 다 들어갈 만큼)로 바꾼다.
    // Gs.Apply가 붙인다. 요소의 tooltip 속성을 그대로 쓰므로 툴팁을 다는 쪽은 바꿀 것이 없다.
    public static class GsTooltip
    {
        const float MaxWidth = 460f;
        const float Gap = 6f;
        const float Margin = 8f;

        sealed class State
        {
            public VisualElement Host;
            public VisualElement Box;
            public Label Text;
            public VisualElement Owner;
        }

        public static void Attach(VisualElement host)
        {
            if (host.ClassListContains("gs-tooltip-host")) return;
            host.AddToClassList("gs-tooltip-host");
            var state = new State { Host = host };

            // 기본 툴팁을 부르는 이벤트를 가장 먼저 받아, 기본 툴팁은 비워 두고 우리 상자를 띄운다.
            host.RegisterCallback<TooltipEvent>(e =>
            {
                var owner = OwnerOf(e.target as VisualElement, host);
                e.tooltip = string.Empty;
                e.StopImmediatePropagation();
                if (owner == null)
                {
                    Hide(state);
                    return;
                }
                if (state.Owner == owner && state.Box?.parent != null && state.Text.text == owner.tooltip) return;
                Show(state, owner);
            }, TrickleDown.TrickleDown);

            host.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (state.Owner != null && !state.Owner.worldBound.Contains(e.position)) Hide(state);
            }, TrickleDown.TrickleDown);
            host.RegisterCallback<PointerDownEvent>(_ => Hide(state), TrickleDown.TrickleDown);
            host.RegisterCallback<WheelEvent>(_ => Hide(state), TrickleDown.TrickleDown);
            host.RegisterCallback<KeyDownEvent>(_ => Hide(state), TrickleDown.TrickleDown);
            host.RegisterCallback<PointerLeaveEvent>(_ => Hide(state));
            host.RegisterCallback<DetachFromPanelEvent>(_ => Hide(state));
        }

        // 가리킨 요소에서 위로 올라가며 툴팁이 있는 첫 요소(기본 툴팁과 같은 규칙).
        static VisualElement OwnerOf(VisualElement element, VisualElement host)
        {
            for (var e = element; e != null; e = e.parent)
            {
                if (!string.IsNullOrEmpty(e.tooltip)) return e;
                if (e == host) break;
            }
            return null;
        }

        static void Show(State state, VisualElement owner)
        {
            if (state.Box == null)
            {
                state.Box = new VisualElement { pickingMode = PickingMode.Ignore };
                state.Box.AddToClassList("gs-tooltip");
                state.Box.style.position = Position.Absolute;
                state.Text = new Label { pickingMode = PickingMode.Ignore };
                state.Text.AddToClassList("gs-tooltip__text");
                state.Box.Add(state.Text);
                state.Box.RegisterCallback<GeometryChangedEvent>(_ => Place(state));
            }
            state.Owner = owner;
            state.Text.text = owner.tooltip;
            var room = state.Host.layout.width - Margin * 2;
            state.Box.style.maxWidth = float.IsNaN(room) ? MaxWidth : Mathf.Min(MaxWidth, Mathf.Max(160f, room));
            state.Box.style.visibility = Visibility.Hidden;
            state.Host.Add(state.Box);
            state.Box.BringToFront();
            // 크기가 그대로면 GeometryChangedEvent가 오지 않으므로 다음 틱에도 한 번 자리를 잡는다.
            state.Box.schedule.Execute(() => Place(state));
        }

        // 요소 바로 아래(자리가 없으면 위)에, 창 밖으로 나가지 않게 놓는다.
        static void Place(State state)
        {
            var box = state.Box;
            if (box == null || box.parent == null || state.Owner == null) return;
            var size = box.layout.size;
            if (float.IsNaN(size.x) || float.IsNaN(size.y)) return;
            var hostSize = state.Host.layout.size;
            var bounds = state.Owner.worldBound;
            var min = state.Host.WorldToLocal(bounds.min);
            var max = state.Host.WorldToLocal(bounds.max);

            var x = Mathf.Clamp(min.x, Margin, Mathf.Max(Margin, hostSize.x - size.x - Margin));
            var y = max.y + Gap;
            if (y + size.y > hostSize.y - Margin)
            {
                var above = min.y - Gap - size.y;
                y = above >= Margin ? above : Mathf.Max(Margin, hostSize.y - size.y - Margin);
            }
            box.style.left = x;
            box.style.top = y;
            box.style.visibility = Visibility.Visible;
        }

        static void Hide(State state)
        {
            state.Owner = null;
            state.Box?.RemoveFromHierarchy();
        }
    }
}
