using System;
using System.Globalization;
using System.Linq;
using GBible.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace GBible.EditorTools.UI
{
    // 도감의 스프라이트 규격 탭: 규격 그룹과 규격(출력 크기)을 한 화면에서 만들고, 끌어서 정리한다.
    // 그룹을 어느 스프라이트 필드에 쓸지는 도감 › 클래스 › 클래스 상세에서 필드마다 정한다.
    // 베이크는 그렇게 연결된 필드에 원본 스프라이트를 넣는 순간 맞춤 창이 열려 이루어진다.
    public sealed class SpritePresetsPage : GroupedCodexPage
    {
        public const string PageKey = "sprites";
        public override string Key => PageKey;

        protected override Type GroupType => typeof(SpritePresetGroupData);
        protected override Type ItemType => typeof(SpritePresetData);
        protected override string GroupNoun => "그룹";
        protected override string ItemNoun => "규격";
        protected override string EmptyHint => "그룹은 한 용도의 규격을 모은 것입니다. 예: character(standingA 600×900 · standingB 400×900 · icon 128×128).";

        public const int DefaultSize = 256;
        public const int MinSize = 8;
        public const int MaxSize = 8192;

        protected override string ItemText(GameData item) =>
            item is SpritePresetData preset ? $"{preset.Id}  {preset.width}×{preset.height}" : item.Id;

        protected override string GroupInfo(GameData group, int count) =>
            $"규격 {count}개  ·  쓰는 필드 {KeywordTools.SpriteFieldsLinkedTo(group.Id).Count()}개";

        // 이 그룹으로 베이크하는 스프라이트 필드들. 누르면 그 클래스의 상세로 간다.
        protected override void GroupFooter(CodexPageContext context, GameData group, VisualElement body)
        {
            var fields = KeywordTools.SpriteFieldsLinkedTo(group.Id).ToList();
            var row = Gs.Row("gs-wrap");
            row.style.marginTop = 4;
            var label = Gs.Label("쓰는 필드", "gs-caption");
            label.style.marginRight = 8;
            row.Add(label);
            if (fields.Count == 0)
            {
                var none = Gs.Label("없음 — 도감 › 클래스에서 클래스를 눌러 스프라이트 필드에 이 그룹을 연결하면, 그 필드에 원본을 넣을 때 베이크됩니다", "gs-caption");
                none.style.opacity = 0.7f;
                row.Add(none);
            }
            foreach (var (type, field) in fields)
            {
                var linkType = type;
                var linkField = field.Name;
                var chip = new GsBadge($"{type.Name}.{field.Name}", Gs.PaletteFor("class:" + type.Name));
                chip.tooltip = "눌러서 클래스 상세 열기";
                chip.style.marginRight = 6;
                chip.style.marginBottom = 4;
                chip.RegisterCallback<ClickEvent>(e =>
                {
                    e.StopPropagation();
                    ClassDetail.Open(linkType, linkField);
                });
                row.Add(chip);
            }
            body.Add(row);
        }

        // 그룹은 이름만 받는다(규격은 그룹 카드의 '+ 규격'으로 하나씩).
        protected override void NewGroup(CodexPageContext context, string parent)
        {
            GsDialog.Form(context.Host, string.IsNullOrEmpty(parent) ? "새 규격 그룹" : $"'{parent}' 안에 하위 그룹", "그룹을 만든 뒤 그룹 줄의 '+ 규격'으로 규격을 더합니다.",
                new[] { new DialogField { Key = "id", Label = "그룹 ID", Value = "", Caption = "예: character, itemImages" } },
                "만들기",
                values => EditorDatabase.Instance.CanUseId(values["id"], null, out var error) ? null : error,
                values =>
                {
                    CodexGroups.Create(typeof(SpritePresetGroupData), values["id"], staged: true, init: data => ((INestedData)data).Parent = parent ?? "");
                    context.Refresh?.Invoke();
                });
        }

        // 규격 하나: ID와 크기(가로 x · 세로 y, 왼쪽 체크박스로 종횡비 고정).
        protected override void AddItems(CodexPageContext context, GameData group)
        {
            var box = Gs.Box("gs-dialog");
            box.Add(Gs.Label($"'{group.Id}'에 규격 추가", "gs-title"));
            var message = Gs.Label("규격 크기를 바꾸면 그 규격으로 만든 이미지가 모두 다시 만들어집니다.", "gs-text-2");
            message.style.marginTop = 8;
            message.style.whiteSpace = WhiteSpace.Normal;
            box.Add(message);

            var idInput = new TextField();
            idInput.AddToClassList("gs-field__input");
            var idField = new GsField("ID", idInput) { Open = () => idInput.Focus() };
            idField.SetHelp("예: icon, standingA");
            idField.style.marginTop = 14;
            box.Add(idField);

            // 크기: [종횡비 고정] [x] [y]
            var sizeRow = Gs.Row();
            sizeRow.style.marginTop = 14;
            sizeRow.style.alignItems = Align.FlexStart;
            var lockRatio = new Toggle { value = false };
            lockRatio.tooltip = "종횡비 고정: 한쪽을 바꾸면 다른 쪽이 같은 비율로 따라갑니다";
            lockRatio.style.marginTop = 14;
            lockRatio.style.marginRight = 8;
            sizeRow.Add(lockRatio);
            var xInput = new TextField { value = DefaultSize.ToString(CultureInfo.InvariantCulture) };
            xInput.AddToClassList("gs-field__input");
            var xField = new GsField("x (가로 px)", xInput) { Open = () => xInput.Focus() };
            xField.style.flexGrow = 1;
            xField.style.flexBasis = 0;
            sizeRow.Add(xField);
            sizeRow.Add(Gs.HSpace(8));
            var yInput = new TextField { value = DefaultSize.ToString(CultureInfo.InvariantCulture) };
            yInput.AddToClassList("gs-field__input");
            var yField = new GsField("y (세로 px)", yInput) { Open = () => yInput.Focus() };
            yField.style.flexGrow = 1;
            yField.style.flexBasis = 0;
            sizeRow.Add(yField);
            box.Add(sizeRow);

            var error = Gs.Label("", "gs-caption");
            error.style.color = Gs.Danger;
            error.style.marginTop = 10;
            error.style.whiteSpace = WhiteSpace.Normal;
            error.style.display = DisplayStyle.None;
            box.Add(error);

            var buttons = Gs.Box("gs-dialog__buttons");
            box.Add(buttons);
            var modal = GsModal.Show(context.Host, box);
            GsButton confirm = null;
            // 고정할 때의 비율(가로 ÷ 세로). 체크하는 순간의 값으로 잡는다.
            var ratio = 1f;
            var syncing = false;
            var touched = false;

            string Problem(out int width, out int height)
            {
                width = height = 0;
                var id = idInput.value?.Trim() ?? "";
                if (!EditorDatabase.Instance.CanUseId(id, null, out var idError)) return "ID: " + idError;
                if (string.Equals(id, group.Id, StringComparison.OrdinalIgnoreCase)) return "ID: 그룹 ID와 규격 ID가 같을 수 없습니다.";
                return CheckSize(xInput.value, yInput.value, out width, out height);
            }

            void Check()
            {
                var problem = Problem(out _, out _);
                var shown = touched ? problem : null;
                idField.SetError(shown != null && shown.StartsWith("ID:", StringComparison.Ordinal) ? shown : null);
                if (shown == null || !shown.StartsWith("ID:", StringComparison.Ordinal)) idField.SetHelp("예: icon, standingA");
                var sizeProblem = shown != null && !shown.StartsWith("ID:", StringComparison.Ordinal) ? shown : "";
                error.text = sizeProblem;
                error.style.display = sizeProblem.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                confirm?.SetEnabled(problem == null);
            }

            lockRatio.RegisterValueChangedCallback(e =>
            {
                if (e.newValue && TryParse(xInput.value, out var w) && TryParse(yInput.value, out var h) && h > 0) ratio = (float)w / h;
            });
            idInput.RegisterValueChangedCallback(_ =>
            {
                touched = true;
                Check();
            });
            xInput.RegisterValueChangedCallback(e =>
            {
                touched = true;
                if (!syncing && lockRatio.value && TryParse(e.newValue, out var w))
                {
                    syncing = true;
                    yInput.value = Follow(w, 1f / ratio).ToString(CultureInfo.InvariantCulture);
                    syncing = false;
                }
                Check();
            });
            yInput.RegisterValueChangedCallback(e =>
            {
                touched = true;
                if (!syncing && lockRatio.value && TryParse(e.newValue, out var h))
                {
                    syncing = true;
                    xInput.value = Follow(h, ratio).ToString(CultureInfo.InvariantCulture);
                    syncing = false;
                }
                Check();
            });

            void Submit()
            {
                if (Problem(out var width, out var height) != null) return;
                var id = idInput.value.Trim();
                modal.Close();
                CodexGroups.Create(typeof(SpritePresetData), id, group.Id, staged: true, init: data =>
                {
                    var preset = (SpritePresetData)data;
                    preset.width = width;
                    preset.height = height;
                });
                context.Refresh?.Invoke();
            }

            buttons.Add(new GsButton("취소", modal.Close, GsButtonKind.Ghost));
            confirm = new GsButton("추가", Submit, GsButtonKind.Primary);
            buttons.Add(confirm);
            box.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return;
                e.StopPropagation();
                Submit();
            }, TrickleDown.TrickleDown);
            Check();
            idInput.schedule.Execute(() => idInput.Focus()).StartingIn(40);
        }

        static bool TryParse(string text, out int value) =>
            int.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0;

        // 종횡비 고정: 한쪽 값에 비율을 곱해 다른 쪽을 구한다(반올림, 최소 1).
        internal static int Follow(int value, float factor) => Mathf.Max(1, Mathf.RoundToInt(value * factor));

        // 크기 글자 검사. 문제가 있으면 문장, 없으면 null.
        internal static string CheckSize(string x, string y, out int width, out int height)
        {
            width = height = 0;
            if (!TryParse(x, out width) || !TryParse(y, out height)) return "크기는 숫자로 입력하세요(픽셀).";
            if (width < MinSize || height < MinSize || width > MaxSize || height > MaxSize) return $"크기는 {MinSize}~{MaxSize}px 사이여야 합니다.";
            return null;
        }
    }
}
