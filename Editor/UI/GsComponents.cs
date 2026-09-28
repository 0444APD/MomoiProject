using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameSystems.EditorTools.UI
{
    public enum GsButtonKind
    {
        Default,
        Primary,
        Ghost,
    }

    // Unity 기본 버튼 모양을 쓰지 않는 자체 버튼.
    public sealed class GsButton : VisualElement
    {
        readonly Label _label;

        public GsButton(string text, Action onClick, GsButtonKind kind = GsButtonKind.Default)
        {
            AddToClassList("gs-button");
            if (kind == GsButtonKind.Primary) AddToClassList("gs-button--primary");
            if (kind == GsButtonKind.Ghost) AddToClassList("gs-button--ghost");
            _label = new Label(text);
            Add(_label);
            // 비활성화는 VisualElement.SetEnabled로 한다(:disabled 스타일 적용, 클릭 차단).
            this.AddManipulator(new Clickable(() => onClick?.Invoke()));
        }

        public string Text
        {
            get => _label.text;
            set => _label.text = value;
        }
    }

    public sealed class GsBadge : VisualElement
    {
        readonly Label _label;

        public GsBadge(string text, Color color)
        {
            AddToClassList("gs-badge");
            _label = new Label(text);
            Add(_label);
            SetColor(color);
        }

        public void SetColor(Color color)
        {
            style.backgroundColor = Gs.WithAlpha(color, 0.14f);
            _label.style.color = color;
        }

        public string Text
        {
            get => _label.text;
            set => _label.text = value;
        }
    }

    public sealed class GsProgress : VisualElement
    {
        readonly VisualElement _fill;

        public GsProgress()
        {
            AddToClassList("gs-progress");
            _fill = new VisualElement();
            _fill.AddToClassList("gs-progress__fill");
            _fill.style.width = Length.Percent(0);
            Add(_fill);
        }

        public void SetValue(double current, double target)
        {
            var ratio = target <= 0 ? 0 : Mathf.Clamp01((float)(current / target));
            _fill.style.width = Length.Percent(ratio * 100f);
            EnableInClassList("gs-progress--done", ratio >= 1f);
        }
    }

    // 보기 전환 등 여러 개 중 하나를 고르는 컨트롤.
    public sealed class GsSegmented : VisualElement
    {
        readonly List<VisualElement> _items = new List<VisualElement>();
        int _index = -1;

        public event Action<int> Changed;

        public GsSegmented(params string[] labels)
        {
            AddToClassList("gs-segmented");
            for (int i = 0; i < labels.Length; i++)
            {
                var index = i;
                var item = new VisualElement();
                item.AddToClassList("gs-segmented__item");
                item.Add(new Label(labels[i]));
                item.RegisterCallback<ClickEvent>(_ => Select(index, true));
                _items.Add(item);
                Add(item);
            }
        }

        public int Index => _index;

        public void Select(int index, bool notify = false)
        {
            if (index == _index) return;
            _index = index;
            for (int i = 0; i < _items.Count; i++)
                _items[i].EnableInClassList("gs-segmented__item--active", i == index);
            if (notify) Changed?.Invoke(index);
        }
    }

    public sealed class GsSearchField : VisualElement
    {
        readonly TextField _field;
        readonly Label _placeholder;

        public event Action<string> Changed;

        public GsSearchField(string placeholder)
        {
            AddToClassList("gs-search");
            var icon = new VisualElement();
            icon.AddToClassList("gs-search__icon");
            Add(icon);

            _field = new TextField();
            _field.RegisterValueChangedCallback(e =>
            {
                UpdatePlaceholder();
                Changed?.Invoke(e.newValue);
            });
            _field.RegisterCallback<FocusInEvent>(_ => EnableInClassList("gs-search--focused", true));
            _field.RegisterCallback<FocusOutEvent>(_ => EnableInClassList("gs-search--focused", false));
            Add(_field);

            _placeholder = Gs.Label(placeholder, "gs-caption", "gs-search__placeholder");
            _placeholder.pickingMode = PickingMode.Ignore;
            Add(_placeholder);
        }

        public string Value
        {
            get => _field.value;
            set
            {
                _field.SetValueWithoutNotify(value ?? "");
                UpdatePlaceholder();
            }
        }

        public void FocusInput() => _field.Focus();

        void UpdatePlaceholder() => _placeholder.style.display = string.IsNullOrEmpty(_field.value) ? DisplayStyle.Flex : DisplayStyle.None;
    }

    public sealed class GsNavItem : VisualElement
    {
        readonly Label _count;

        public GsNavItem(string text, Color dot, Action onClick)
        {
            AddToClassList("gs-nav-item");
            var marker = new VisualElement();
            marker.AddToClassList("gs-nav-dot");
            marker.style.backgroundColor = dot;
            Add(marker);
            var label = Gs.Label(text, "gs-grow", "gs-nowrap");
            Add(label);
            _count = Gs.Label("", "gs-nav-item__count");
            Add(_count);
            RegisterCallback<ClickEvent>(_ => onClick?.Invoke());
        }

        public void SetCount(string text) => _count.text = text;

        public void SetActive(bool active) => EnableInClassList("gs-nav-item--active", active);
    }

    public enum GsNoticeKind
    {
        Info,
        Ok,
        Warning,
        Error,
    }

    public sealed class GsNotice : VisualElement
    {
        public GsNotice(string text, GsNoticeKind kind)
        {
            AddToClassList("gs-notice");
            if (kind == GsNoticeKind.Error) AddToClassList("gs-notice--error");
            if (kind == GsNoticeKind.Warning) AddToClassList("gs-notice--warning");
            if (kind == GsNoticeKind.Ok) AddToClassList("gs-notice--ok");
            var label = Gs.Label(text, "gs-grow");
            if (kind == GsNoticeKind.Info) label.AddToClassList("gs-text-2");
            Add(label);
        }
    }

    // 카드 A(세로형) · B(가로형) · C(바형). 같은 슬롯을 배치만 달리한다.
    public enum CardLayout
    {
        Vertical,
        Horizontal,
        Bar,
    }

    public sealed class GsCard : VisualElement
    {
        readonly VisualElement _media;
        readonly Label _title;
        readonly Label _subtitle;
        readonly VisualElement _badges;
        readonly VisualElement _stats;
        readonly VisualElement _textColumn;

        public GsCard(CardLayout layout)
        {
            AddToClassList("gs-card");
            AddToClassList(layout == CardLayout.Vertical ? "gs-card--vertical"
                         : layout == CardLayout.Horizontal ? "gs-card--horizontal" : "gs-card--bar");

            _media = new VisualElement();
            _media.AddToClassList("gs-card__media");
            Add(_media);

            var body = new VisualElement();
            body.AddToClassList("gs-card__body");
            Add(body);

            _textColumn = new VisualElement();
            _textColumn.style.flexShrink = 1;
            _textColumn.style.flexGrow = layout == CardLayout.Bar ? 1 : 0;
            _textColumn.style.minWidth = 0;
            body.Add(_textColumn);

            _title = Gs.Label("", "gs-card__title");
            _textColumn.Add(_title);
            _subtitle = Gs.Label("", "gs-card__subtitle");
            _textColumn.Add(_subtitle);

            _badges = new VisualElement();
            _badges.AddToClassList("gs-card__badges");
            body.Add(_badges);

            _stats = new VisualElement();
            _stats.AddToClassList("gs-card__stats");
            body.Add(_stats);

            if (layout == CardLayout.Horizontal)
            {
                // 가로형은 수치를 아래로 붙여 여백을 확보한다.
                var spacer = new VisualElement();
                spacer.style.flexGrow = 1;
                body.Insert(body.IndexOf(_stats), spacer);
            }
        }

        public string Title
        {
            get => _title.text;
            set => _title.text = value;
        }

        public string Subtitle
        {
            get => _subtitle.text;
            set
            {
                _subtitle.text = value;
                _subtitle.style.display = string.IsNullOrEmpty(value) ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        public void SetImage(UnityEngine.Object image, Color placeholder)
        {
            _media.Clear();
            if (image is Sprite sprite)
            {
                _media.style.backgroundImage = new StyleBackground(sprite);
                return;
            }
            if (image is Texture2D texture)
            {
                _media.style.backgroundImage = new StyleBackground(texture);
                return;
            }
            // 이미지가 없으면 분류색의 옅은 면으로 자리만 표시한다.
            _media.style.backgroundImage = StyleKeyword.Null;
            _media.style.backgroundColor = Gs.WithAlpha(placeholder, 0.10f);
        }

        public void HideMedia() => _media.style.display = DisplayStyle.None;

        public void AddBadge(string text, Color color) => _badges.Add(new GsBadge(text, color));

        public void AddStat(string label, string value)
        {
            var stat = new VisualElement();
            stat.AddToClassList("gs-card__stat");
            stat.Add(Gs.Label(label, "gs-card__stat-label"));
            stat.Add(Gs.Label(value, "gs-card__stat-value"));
            _stats.Add(stat);
        }

        public void AddToStats(VisualElement element) => _stats.Add(element);

        public void SetSelected(bool selected) => EnableInClassList("gs-card--selected", selected);
    }
}
