using System;
using UnityEngine.UIElements;

namespace GBible.EditorTools.UI
{
    // 입력 칸 하나: 둥근 상자 안에 위쪽 작은 이름표, 아래쪽 값. 도감·탐색기·확인 창의 모든 값 칸이 이 모양을 쓴다.
    //  · 비어 있음: 이름표가 값 자리에 크게(자리 표시). 누르면 편집.
    //  · 편집 중: 아래쪽에 포인트 컬러 밑줄.
    //  · 오류: 이름표·밑줄이 빨강, 상자 아래에 오류 문구.
    //  · 비활성: 더 어두운 상자, 옅은 글자.
    // 긴 값은 칸이 늘어나며 최대 10줄까지 보이고, 그 이상은 칸 안에서 스크롤한다.
    //
    // '편집 중'(밑줄)은 켜고 끄는 깃발을 두지 않고, 눈에 보이는 실제 상태에서만 계산한다:
    //   ① 칸 안에 글자 편집기가 떠 있다  ② 칸 안의 글자 입력에 포커스가 있다  ③ 이 칸이 연 목록이 아직 화면에 있다.
    // 셋 다 아니면 무조건 꺼진다. 취소(Esc)·바깥 클릭·다시 그리기처럼 어느 경로로 끝나도 밑줄이 남지 않는다
    // (값 칸 자체에 포커스가 남아 있는 것은 편집 중으로 치지 않는다). 켜져 있는 동안은 짧은 주기로 다시 확인한다.
    public sealed class GsField : VisualElement
    {
        // 값 10줄 높이(글자 13px 기준).
        public const float MaxValueHeight = 180f;

        readonly Label _label;
        readonly Label _message;
        bool _active;
        readonly System.Collections.Generic.List<GsDropdown> _menus = new System.Collections.Generic.List<GsDropdown>();
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
            // 글자 입력 칸(확인 창 등)은 포커스로 '편집 중'을 표시한다.
            RegisterCallback<FocusInEvent>(_ => RefreshActive());
            RegisterCallback<FocusOutEvent>(_ => RefreshSoon());
            // 취소·확정 키 뒤에는 편집기가 사라지므로 바로 다시 확인한다.
            RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == UnityEngine.KeyCode.Escape || e.keyCode == UnityEngine.KeyCode.Return || e.keyCode == UnityEngine.KeyCode.KeypadEnter) RefreshSoon();
            }, TrickleDown.TrickleDown);
            RegisterCallback<AttachToPanelEvent>(_ => RefreshActive());
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                _watch?.Pause();
                _menus.Clear();
                SetActive(false);
            });
        }

        public string Label
        {
            get => _label.text;
            set => _label.text = value;
        }

        public bool Active => _active;

        // 값 칸이 글자 편집을 시작·끝낼 때 알린다(상태는 기억하지 않고 실제 화면에서 다시 계산한다).
        public void SetEditing(bool editing)
        {
            RefreshActive();
            RefreshSoon();
        }

        // 값 칸이 연 목록(드롭다운). 목록이 닫히거나 화면에서 사라지면 편집 중이 아니다.
        public void TrackMenu(GsDropdown menu)
        {
            if (menu != null)
            {
                _menus.Add(menu);
                menu.Closed += RefreshSoon;
            }
            RefreshActive();
        }

        // 칸 안의 글자 입력(TextField 등)에 포커스가 있는지. 값 칸·상자에 남은 포커스는 세지 않는다.
        bool TextInputFocused()
        {
            var focused = focusController?.focusedElement as VisualElement;
            if (focused == null || !Contains(focused)) return false;
            for (var e = focused; e != null && e != this; e = e.parent)
                if (e is TextField || e.ClassListContains("unity-base-text-field")) return true;
            return false;
        }

        bool ComputeActive()
        {
            _menus.RemoveAll(m => m == null || m.IsClosed || m.panel == null);
            return _menus.Count > 0 || Body.Q(className: "gs-inline--editing") != null || TextInputFocused();
        }

        void SetActive(bool active)
        {
            if (active == _active && ClassListContains("gs-field--active") == active) return;
            _active = active;
            EnableInClassList("gs-field--active", active);
        }

        void RefreshSoon()
        {
            if (panel == null) return;
            schedule.Execute(RefreshActive).StartingIn(0);
            schedule.Execute(RefreshActive).StartingIn(60);
        }

        void RefreshActive()
        {
            var active = ComputeActive();
            SetActive(active);
            RefreshEmpty();
            if (active)
            {
                _watch ??= schedule.Execute(RefreshActive).Every(100);
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
        // body: 칸 안에 넣을 것(기본은 값 칸 자체. 상세 패널은 이전 값·화살표가 함께 든 상자를 넣는다).
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
