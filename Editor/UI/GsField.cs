using System;
using UnityEngine.UIElements;

namespace GameSystems.EditorTools.UI
{
    // 입력 칸 하나: 둥근 상자 안에 위쪽 작은 이름표, 아래쪽 값. 도감·탐색기·대화 상자의 모든 값 칸이 이 모양을 쓴다.
    //  · 비어 있음: 이름표가 값 자리에 크게(자리 표시). 누르면 편집.
    //  · 편집 중: 아래쪽에 포인트 컬러 밑줄.
    //  · 오류: 이름표·밑줄이 빨강, 상자 아래에 오류 문구.
    //  · 비활성: 더 어두운 상자, 옅은 글자.
    // 긴 값은 칸이 늘어나며 최대 10줄까지 보이고, 그 이상은 칸 안에서 스크롤한다.
    //
    // '편집 중'은 따로 켜고 끄지 않고 실제 상태에서 매번 다시 계산한다(글자 편집 중 · 목록이 열려 있음 · 포커스가 안에 있음).
    // 어느 경로로 편집이 끝나도(바깥 클릭·다시 그리기·편집기가 안 열린 클릭) 밑줄이 남지 않게, 켜져 있는 동안 주기적으로 다시 확인한다.
    public sealed class GsField : VisualElement
    {
        // 값 10줄 높이(글자 13px 기준).
        public const float MaxValueHeight = 180f;

        readonly Label _label;
        readonly Label _message;
        bool _active;
        bool _editing;
        int _menus;
        IVisualElementScheduledItem _watch;

        public VisualElement Box { get; }
        public VisualElement Body { get; }
        // 값이 비었는지(비었으면 이름표를 자리 표시로 크게 보인다).
        public Func<bool> IsEmpty;
        // 비어 있을 때 상자를 누르면 할 일(보통 값 칸의 편집 열기).
        public Action Open;

        public GsField(string label, VisualElement content = null)
        {
            AddToClassList("gs-field");
            Box = Gs.Col("gs-field__box");
            Add(Box);
            _label = Gs.Label(label ?? "", "gs-field__label");
            Box.Add(_label);
            Body = Gs.Col("gs-field__body");
            Box.Add(Body);
            _message = Gs.Label("", "gs-field__message");
            _message.style.display = DisplayStyle.None;
            Add(_message);
            if (content != null) Body.Add(content);

            Box.RegisterCallback<ClickEvent>(e =>
            {
                if (!ClassListContains("gs-field--empty") || Open == null) return;
                e.StopPropagation();
                Open();
                RefreshActive();
            });
            // 글자 입력 칸(대화 상자 등)은 포커스로 '편집 중'을 표시한다.
            RegisterCallback<FocusInEvent>(_ => RefreshActive());
            RegisterCallback<FocusOutEvent>(_ => schedule.Execute(RefreshActive).StartingIn(20));
            RegisterCallback<DetachFromPanelEvent>(_ => _watch?.Pause());
        }

        public string Label
        {
            get => _label.text;
            set => _label.text = value;
        }

        public bool Active => _active;

        // 값 칸이 글자 편집을 시작·끝낼 때.
        public void SetEditing(bool editing)
        {
            _editing = editing;
            RefreshActive();
        }

        // 값 칸이 목록(드롭다운)을 열고 닫을 때.
        public void MenuOpened()
        {
            _menus++;
            RefreshActive();
        }

        public void MenuClosed()
        {
            _menus = Math.Max(0, _menus - 1);
            RefreshActive();
        }

        bool FocusWithin()
        {
            var focused = focusController?.focusedElement as VisualElement;
            return focused != null && (focused == this || Contains(focused));
        }

        void RefreshActive()
        {
            // 편집기가 이미 사라졌으면(다시 그리기 등) 편집 중이 아니다.
            if (_editing && Body.Q(className: "gs-inline--editing") == null && Body.Q<TextField>() == null) _editing = false;
            var active = _editing || _menus > 0 || FocusWithin();
            if (active != _active)
            {
                _active = active;
                EnableInClassList("gs-field--active", active);
            }
            RefreshEmpty();
            if (active)
            {
                _watch ??= schedule.Execute(RefreshActive).Every(300);
                _watch.Resume();
            }
            else _watch?.Pause();
        }

        public void RefreshEmpty() => EnableInClassList("gs-field--empty", !_active && (IsEmpty?.Invoke() ?? false));

        // 오류 문구(null이면 지운다).
        public void SetError(string message)
        {
            var has = !string.IsNullOrEmpty(message);
            EnableInClassList("gs-field--error", has);
            _message.text = message ?? "";
            _message.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // 도움말 문구(오류가 없을 때 상자 아래에 옅게).
        public void SetHelp(string text)
        {
            if (ClassListContains("gs-field--error")) return;
            _message.text = text ?? "";
            _message.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        public void SetDisabled(bool disabled) => EnableInClassList("gs-field--disabled", disabled);

        // 값 칸을 이 모양으로 감싼다(비었을 때 누르면 편집이 열리고, 편집 중 표시가 따라간다).
        // body: 칸 안에 넣을 것(기본은 값 칸 자체. 상세 패널은 이전 값·화살표가 함께 든 묶음을 넣는다).
        public static GsField For(string label, GsInlineProperty inline, VisualElement body = null)
        {
            var field = new GsField(label, body ?? inline)
            {
                IsEmpty = () => inline.IsEmptyValue,
                Open = inline.Open,
            };
            inline.CapHeight(MaxValueHeight);
            // 오브젝트·색 선택기는 칸 상자 전체를 덮는다(어디를 눌러도 Unity 선택 창이 바로 열린다).
            if (inline.DirectPicker != null) field.Box.Add(inline.DirectPicker);
            inline.Changed += field.RefreshEmpty;
            field.SetDisabled(!inline.Editable);
            field.RefreshEmpty();
            return field;
        }
    }
}
