using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine.UIElements;

namespace GBible.EditorTools.UI
{
    // 도감의 그룹 하나: 제목 줄 + 그 아래로 요소가 한 줄씩 나열된다(키워드·스프라이트 규격·클래스·이벤트 탭 공용).
    // 제목 왼쪽(화살표)을 누르면 접고 펼친다. 접은 상태는 에디터를 쓰는 동안 기억한다(다시 그려도·다시 컴파일해도 유지).
    //   [▾] 제목  설명 ……………… [도구들]
    //        요소
    //        요소
    //        (꼬리: 연결된 필드 등)
    public sealed class GsGroupSection : VisualElement
    {
        const string FoldKey = "GBible.Codex.Folded";

        static HashSet<string> _folded;

        static HashSet<string> Folded =>
            _folded ??= new HashSet<string>(SessionState.GetString(FoldKey, "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);

        public static bool IsFolded(string key) => Folded.Contains(key);

        public static void SetFolded(string key, bool folded)
        {
            if (folded ? !Folded.Add(key) : !Folded.Remove(key)) return;
            SessionState.SetString(FoldKey, string.Join("\n", Folded));
        }

        readonly string _key;
        readonly Label _arrow;
        readonly VisualElement _content;

        // 제목 줄. 화살표 다음에 제목(Titles)이 오고, 오른쪽 끝에 도구(Tools)가 붙는다.
        public VisualElement Head { get; }
        public VisualElement Titles { get; }
        public VisualElement Tools { get; }
        // 요소가 한 줄씩 쌓이는 곳.
        public VisualElement Items { get; }
        // 요소 아래에 덧붙이는 곳(연결된 필드 등).
        public VisualElement Footer { get; }

        // key: 접은 상태를 기억할 이름(탭 + 그룹).
        public GsGroupSection(string key)
        {
            _key = key;
            AddToClassList("gs-group");

            Head = Gs.Row("gs-group__head");
            _arrow = Gs.Label("", "gs-group__arrow");
            _arrow.tooltip = "접기 · 펼치기";
            _arrow.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            _arrow.RegisterCallback<ClickEvent>(e =>
            {
                e.StopPropagation();
                Toggle();
            });
            Head.Add(_arrow);
            Titles = Gs.Row("gs-group__titles", "gs-grow");
            Head.Add(Titles);
            Tools = Gs.Row("gs-group__tools");
            Head.Add(Tools);
            Add(Head);

            _content = Gs.Col("gs-group__content");
            Items = Gs.Col("gs-group__items");
            _content.Add(Items);
            Footer = Gs.Col("gs-group__footer");
            _content.Add(Footer);
            Add(_content);
            Apply();
        }

        public bool FoldedNow => IsFolded(_key);

        public void Toggle()
        {
            SetFolded(_key, !IsFolded(_key));
            Apply();
        }

        void Apply()
        {
            var folded = IsFolded(_key);
            _arrow.text = folded ? "▸" : "▾";
            _content.style.display = folded ? DisplayStyle.None : DisplayStyle.Flex;
            EnableInClassList("gs-group--folded", folded);
        }

        // 제목 줄 전체로 접고 펼친다(버튼 빼고 어디를 눌러도). 도구 버튼은 마우스를 올린 줄에만 보이고, 그 줄은 옅게 밝아진다.
        public void UseHeadToggle()
        {
            Head.AddToClassList("gs-group__head--toggle");
            Head.tooltip = "눌러서 접기 · 펼치기";
            Head.RegisterCallback<ClickEvent>(e =>
            {
                if (e.target is VisualElement target && (target is GsButton || target.GetFirstAncestorOfType<GsButton>() != null)) return;
                e.StopPropagation();
                Toggle();
            });
        }

        // 제목과 그 옆의 옅은 설명.
        public Label AddTitle(string title, string info = null)
        {
            var label = Gs.Label(title, "gs-heading", "gs-group__title");
            Titles.Add(label);
            if (!string.IsNullOrEmpty(info)) Titles.Add(Gs.Label(info, "gs-caption", "gs-group__info"));
            return label;
        }

        // 요소 한 줄(누를 수 있는 줄). 왼쪽 글자 + 오른쪽에 배지 등을 덧붙일 수 있다.
        public static VisualElement Row(string text, string caption = null)
        {
            var row = Gs.Row("gs-group__row");
            row.Add(Gs.Label(text, "gs-group__row-text"));
            if (!string.IsNullOrEmpty(caption)) row.Add(Gs.Label(caption, "gs-caption", "gs-group__row-caption"));
            return row;
        }

        // 요소가 없을 때의 안내 글.
        public void AddHint(string text) => Items.Add(Gs.Label(text, "gs-caption", "gs-group__hint"));
    }
}
