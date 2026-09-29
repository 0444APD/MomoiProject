using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GameSystems.Data;
using UnityEngine.UIElements;

namespace GameSystems.EditorTools.UI
{
    // 도감의 스프라이트 규격 탭: 규격 묶음과 규격(출력 크기)을 한 화면에서 만들고, 끌어서 정리한다.
    public sealed class SpritePresetsPage : GroupedCodexPage
    {
        public const string PageKey = "sprites";
        public override string Key => PageKey;

        protected override Type GroupType => typeof(SpritePresetGroupData);
        protected override Type ItemType => typeof(SpritePresetData);
        protected override string GroupNoun => "묶음";
        protected override string ItemNoun => "규격";
        protected override string EmptyHint => "묶음은 한 용도의 규격 모음입니다. 예: character(standingA 600×900 · standingB 400×900 · icon 128×128).";

        const int DefaultSize = 256;
        const string LinesCaption = "한 줄에 하나씩 'ID 가로x세로'. 예: icon 128x128. 크기를 빼면 256x256.";

        protected override string ItemText(GameData item) =>
            item is SpritePresetData preset ? $"{preset.Id}  {preset.width}×{preset.height}" : item.Id;

        protected override string GroupInfo(GameData group, int count)
        {
            var fields = EditorDatabase.Instance.OfType<FieldRuleData>().Count(r => r.spriteGroup == group.Id);
            return $"규격 {count}개  ·  쓰는 필드 {fields}개";
        }

        protected override void NewGroup(CodexPageContext context)
        {
            GsDialog.Form(context.Host, "새 규격 묶음", "묶음을 스프라이트 필드에 연결하면(필드 규칙), 그 필드에 원본을 넣을 때 묶음의 모든 규격으로 잘라 저장합니다.",
                new[]
                {
                    new DialogField { Key = "id", Label = "묶음 ID", Value = "", Caption = "예: character, itemImages" },
                    new DialogField { Key = "lines", Label = "규격 (선택)", Value = "", Caption = LinesCaption, Multiline = true },
                },
                "만들기",
                values =>
                {
                    if (!EditorDatabase.Instance.CanUseId(values["id"], null, out var error)) return "묶음 ID: " + error;
                    var (presets, problem) = Parse(values["lines"]);
                    if (problem != null) return problem;
                    if (presets.Any(p => string.Equals(p.id, values["id"], StringComparison.OrdinalIgnoreCase))) return "묶음 ID와 규격 ID가 같을 수 없습니다.";
                    return CodexGroups.CheckNew(presets.Select(p => p.id));
                },
                values =>
                {
                    var group = CodexGroups.Create(typeof(SpritePresetGroupData), values["id"], staged: true);
                    Create(group.Id, Parse(values["lines"]).presets);
                    context.Refresh?.Invoke();
                });
        }

        protected override void AddItems(CodexPageContext context, GameData group)
        {
            GsDialog.Form(context.Host, $"'{group.Id}'에 규격 추가", "규격 크기를 바꾸면 그 규격으로 만든 이미지가 모두 다시 만들어집니다.",
                new[] { new DialogField { Key = "lines", Label = "규격", Value = "", Caption = LinesCaption, Multiline = true } },
                "추가",
                values =>
                {
                    var (presets, problem) = Parse(values["lines"]);
                    if (problem != null) return problem;
                    return presets.Count == 0 ? "규격을 하나 이상 입력하세요." : CodexGroups.CheckNew(presets.Select(p => p.id));
                },
                values =>
                {
                    Create(group.Id, Parse(values["lines"]).presets);
                    context.Refresh?.Invoke();
                });
        }

        static void Create(string group, List<(string id, int width, int height)> presets)
        {
            foreach (var (id, width, height) in presets)
                CodexGroups.Create(typeof(SpritePresetData), id, group, staged: true, init: data =>
                {
                    var preset = (SpritePresetData)data;
                    preset.width = width;
                    preset.height = height;
                });
        }

        // "icon 128x128" · "icon 128 128" · "icon" 한 줄씩.
        internal static (List<(string id, int width, int height)> presets, string problem) Parse(string text)
        {
            var result = new List<(string, int, int)>();
            foreach (var raw in (text ?? "").Replace("\r", "").Split('\n', ','))
            {
                // 첫 낱말이 ID(ID에 x가 들어 있어도 크기와 섞이지 않게), 나머지가 크기.
                var words = raw.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length == 0) continue;
                var id = words[0];
                if (words.Length == 1)
                {
                    result.Add((id, DefaultSize, DefaultSize));
                    continue;
                }
                var size = string.Join(" ", words.Skip(1)).Replace('×', 'x').Replace('X', 'x')
                    .Split(new[] { ' ', 'x' }, StringSplitOptions.RemoveEmptyEntries);
                if (size.Length != 2 || !int.TryParse(size[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) ||
                    !int.TryParse(size[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height))
                    return (result, $"'{raw.Trim()}'을(를) 읽을 수 없습니다. 'ID 가로x세로'로 적으세요(예: icon 128x128).");
                if (width < 8 || height < 8 || width > 8192 || height > 8192) return (result, $"'{id}': 크기는 8~8192px 사이여야 합니다.");
                result.Add((id, width, height));
            }
            return (result, null);
        }
    }
}
