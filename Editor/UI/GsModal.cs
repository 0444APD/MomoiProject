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

        // 입력 창. 칸마다 값을 받고, 입력할 때마다 validate로 검사해 오류를 보여 주며 오류가 있으면 확인 버튼을 막는다.
        // validate: 값들 → 오류 문장(없으면 null). onConfirm: 확인된 값들.
        public static GsModal Form(VisualElement host, string title, string message, IList<DialogField> fields, string confirmText,
                                   Func<Dictionary<string, string>, string> validate, Action<Dictionary<string, string>> onConfirm)
        {
            var box = Gs.Box("gs-dialog");
            box.Add(Gs.Label(title, "gs-title"));
            if (!string.IsNullOrEmpty(message))
            {
                var text = Gs.Label(message, "gs-text-2");
                text.style.marginTop = 8;
                text.style.whiteSpace = WhiteSpace.Normal;
                box.Add(text);
            }

            var values = new Dictionary<string, string>();
            var inputs = new List<TextField>();
            var error = Gs.Label("", "gs-caption");
            error.style.color = Gs.Danger;
            error.style.marginTop = 10;
            error.style.whiteSpace = WhiteSpace.Normal;
            GsButton confirm = null;

            void Check()
            {
                var problem = validate?.Invoke(values);
                error.text = problem ?? "";
                error.style.display = string.IsNullOrEmpty(problem) ? DisplayStyle.None : DisplayStyle.Flex;
                confirm?.SetEnabled(string.IsNullOrEmpty(problem));
            }

            foreach (var field in fields)
            {
                var key = field.Key;
                values[key] = field.Value ?? "";
                var row = Gs.Col();
                row.style.marginTop = 14;
                row.Add(Gs.Label(field.Label, "gs-detail__label"));
                var input = new TextField { value = values[key], multiline = field.Multiline };
                if (field.Multiline) input.style.minHeight = 90;
                input.AddToClassList("gs-inline");
                input.style.marginTop = 6;
                input.RegisterValueChangedCallback(e =>
                {
                    values[key] = e.newValue ?? "";
                    Check();
                });
                row.Add(input);
                if (!string.IsNullOrEmpty(field.Caption))
                {
                    var caption = Gs.Label(field.Caption, "gs-caption");
                    caption.style.marginTop = 4;
                    caption.style.whiteSpace = WhiteSpace.Normal;
                    row.Add(caption);
                }
                box.Add(row);
                inputs.Add(input);
            }
            box.Add(error);

            var buttons = Gs.Box("gs-dialog__buttons");
            box.Add(buttons);
            var modal = GsModal.Show(host, box);
            buttons.Add(new GsButton("취소", modal.Close, GsButtonKind.Ghost));
            void Submit()
            {
                if (!string.IsNullOrEmpty(validate?.Invoke(values))) return;
                modal.Close();
                onConfirm?.Invoke(new Dictionary<string, string>(values));
            }
            confirm = new GsButton(confirmText, Submit, GsButtonKind.Primary);
            buttons.Add(confirm);

            // Enter로 확인.
            box.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return;
                if (e.target is VisualElement target && (target as TextField ?? target.GetFirstAncestorOfType<TextField>()) is TextField text && text.multiline) return;
                e.StopPropagation();
                Submit();
            }, TrickleDown.TrickleDown);

            Check();
            if (inputs.Count > 0)
                inputs[0].schedule.Execute(() =>
                {
                    inputs[0].Focus();
                    inputs[0].SelectAll();
                }).StartingIn(40);
            return modal;
        }
    }

    public sealed class DialogField
    {
        public string Key;
        public string Label;
        public string Value;
        public string Caption;
        // 여러 줄 입력(Enter는 줄바꿈, 확인은 버튼으로).
        public bool Multiline;
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
            Gs.SetText(_old, oldText);
            Gs.SetText(_new, newText);
        }
    }
}
