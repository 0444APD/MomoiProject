using System;
using UnityEngine.UIElements;

namespace GameSystems.EditorTools.UI
{
    // 입력 칸 하나: 둥근 상자 안에 위쪽 작은 이름표, 아래쪽 값. 도감·탐색기·대화 상자의 모든 값 칸이 이 모양을 쓴다.
    //  · 비어 있음: 이름표가 값 자리에 크게(자리 표시). 누르면 편집.
    //  · 편집 중: 아래쪽에 포인트 컬러 밑줄.
    //  · 오류: 이름표·밑줄이 빨강, 상자 아래에 오류 문구.
    //  · 비활성: 더 어두운 상자, 옅은 글자.
    // 긴 값은 칸이 늘어나며 최대 5줄까지 보이고, 그 이상은 칸 안에서 스크롤한다.
    public sealed class GsField : VisualElement
    {
        // 값 5줄 높이(글자 13px 기준).
        public const float MaxValueHeight = 92f;

        readonly Label _label;
        readonly Label _message;
        bool _active;

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
                SetActive(true);
                Open();
            });
            // 글자 입력 칸(대화 상자 등)은 포커스로 '편집 중'을 표시한다.
            RegisterCallback<FocusInEvent>(_ => SetActive(true));
            RegisterCallback<FocusOutEvent>(_ => schedule.Execute(() =>
            {
                var focused = focusController?.focusedElement as VisualElement;
                if (focused == null || !Contains(focused)) SetActive(false);
            }).StartingIn(20));
        }

        public string Label
        {
            get => _label.text;
            set => _label.text = value;
        }

        public bool Active => _active;

        public void SetActive(bool active)
        {
            _active = active;
            EnableInClassList("gs-field--active", active);
            RefreshEmpty();
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
            inline.Changed += field.RefreshEmpty;
            field.SetDisabled(!inline.Editable);
            field.RefreshEmpty();
            return field;
        }
    }
}
