using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameSystems.EditorTools.UI
{
    // 창 위에 뜨는 패널. 바깥 클릭·Esc는 닫기 요청으로 전달한다(저장 안 된 변경 확인 등은 요청 처리 쪽 몫).
    public sealed class GsModal : VisualElement
    {
        readonly VisualElement _content;
        bool _closing;

        public event Action Closed;
        public Action RequestClose;

        GsModal(VisualElement content)
        {
            AddToClassList("gs-modal");
            AddToClassList("gs-modal--hidden");
            focusable = true;
            _content = content;
            _content.AddToClassList("gs-modal__content");
            Add(_content);
            RequestClose = Close;

            RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.target == this) RequestClose?.Invoke();
            });
            RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Escape) return;
                e.StopPropagation();
                RequestClose?.Invoke();
            });
        }

        public VisualElement Content => _content;

        // host는 디자인 시스템이 적용된 창 루트(gs-root)여야 토큰 색이 적용된다.
        public static GsModal Show(VisualElement host, VisualElement content)
        {
            var modal = new GsModal(content);
            host.Add(modal);
            modal.schedule.Execute(() =>
            {
                modal.RemoveFromClassList("gs-modal--hidden");
                modal.Focus();
            }).StartingIn(16);
            return modal;
        }

        public void Close()
        {
            if (_closing) return;
            _closing = true;
            AddToClassList("gs-modal--hidden");
            schedule.Execute(RemoveFromHierarchy).StartingIn(200);
            Closed?.Invoke();
        }
    }

    public readonly struct DialogButton
    {
        public readonly string Label;
        public readonly GsButtonKind Kind;
        public readonly Action Action;

        public DialogButton(string label, GsButtonKind kind, Action action)
        {
            Label = label;
            Kind = kind;
            Action = action;
        }
    }

    // 확인 창. 변경 목록([이전] › [현재])을 함께 보여줄 수 있다.
    public static class GsDialog
    {
        public static GsModal Show(VisualElement host, string title, string message, IList<FieldChange> changes, params DialogButton[] buttons)
        {
            var box = Gs.Box("gs-dialog");
            box.Add(Gs.Label(title, "gs-title"));
            if (!string.IsNullOrEmpty(message))
            {
                var text = Gs.Label(message, "gs-text-2");
                text.style.marginTop = 8;
                box.Add(text);
            }

            if (changes != null && changes.Count > 0)
            {
                var scroll = new ScrollView();
                scroll.style.maxHeight = 300;
                scroll.style.marginTop = 14;
                foreach (var change in changes) scroll.Add(new GsChangeView(change.Label, change.Old, change.New, "gs-change-row", change.OldObject, change.NewObject));
                box.Add(scroll);
            }

            var row = Gs.Box("gs-dialog__buttons");
            box.Add(row);
            var modal = GsModal.Show(host, box);
            foreach (var button in buttons)
            {
                var b = button;
                var element = new GsButton(b.Label, () =>
                {
                    modal.Close();
                    b.Action?.Invoke();
                }, b.Kind);
                row.Add(element);
            }
            return modal;
        }

        public static GsModal Confirm(VisualElement host, string title, string message, string confirmText, Action onConfirm, IList<FieldChange> changes = null) =>
            Show(host, title, message, changes,
                 new DialogButton("취소", GsButtonKind.Ghost, null),
                 new DialogButton(confirmText, GsButtonKind.Primary, onConfirm));
    }

    // 위 [이전] · V 화살표 · 아래 [현재]
    public sealed class GsChangeView : VisualElement
    {
        readonly Label _old;
        readonly Label _new;

        public GsChangeView(string header, string oldText, string newText, string extraClass = null,
                            UnityEngine.Object oldImage = null, UnityEngine.Object newImage = null)
        {
            if (!string.IsNullOrEmpty(extraClass)) AddToClassList(extraClass);
            if (!string.IsNullOrEmpty(header))
            {
                var label = Gs.Label(header, "gs-detail__label");
                Add(label);
            }
            var images = IsImage(oldImage) || IsImage(newImage);
            var row = Gs.Box("gs-change");
            var oldBox = Gs.Box("gs-change__box", "gs-change__old");
            if (images) oldBox.Add(Thumbnail(oldImage));
            _old = Gs.Label(oldText);
            oldBox.Add(_old);
            row.Add(oldBox);
            row.Add(Gs.Box("gs-change__chevron"));
            var newBox = Gs.Box("gs-change__box", "gs-change__new");
            if (images) newBox.Add(Thumbnail(newImage));
            _new = Gs.Label(newText);
            newBox.Add(_new);
            row.Add(newBox);
            Add(row);
        }

        static bool IsImage(UnityEngine.Object o) => o is Sprite || o is Texture2D;

        static VisualElement Thumbnail(UnityEngine.Object image)
        {
            var thumb = Gs.Box("gs-change__thumb");
            if (image is Sprite sprite) thumb.style.backgroundImage = new StyleBackground(sprite);
            else if (image is Texture2D texture) thumb.style.backgroundImage = new StyleBackground(texture);
            return thumb;
        }

        public void Set(string oldText, string newText)
        {
            _old.text = oldText;
            _new.text = newText;
        }
    }
}
