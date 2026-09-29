using System;
using System.Collections.Generic;
using System.Linq;
using GameSystems.Data;
using UnityEngine.UIElements;

namespace GameSystems.EditorTools.UI
{
    // 스프라이트 규격 항목의 "사용 현황" 블록: 이 규격으로 베이크된 이미지 수와 예외 촬영 목록.
    // 규격이 바뀌어 확인이 필요한 예외는 표시되고, 바로 맞춤 창을 열어 다시 잡을 수 있다.
    [LayoutBlock(Id, "규격 사용 현황", Description = "이 규격으로 베이크된 이미지와 예외 촬영(확인 필요 포함)을 보여 줍니다.")]
    public sealed class SpritePresetBlock : GsLayoutBlock
    {
        public const string Id = "sprite-preset-usage";
        public override bool AutoInclude(Type type) => type == typeof(SpritePresetData);

        public override VisualElement Build(DataViewContext context, BlockPlacement placement, ISet<string> claimed)
        {
            var box = Gs.Col();
            if (!(context.Target is SpritePresetData preset)) return Titled("규격 사용 현황", Gs.Label("스프라이트 규격 항목에서만 보입니다.", "gs-caption"));
            var registry = SpriteSpecEditor.Registry;
            var baked = registry.Framings.Count(f => f.BakeFor(preset.Id)?.sprite != null);
            box.Add(Gs.Label($"이 규격으로 베이크된 이미지 {baked}개", "gs-caption"));
            var exceptions = registry.Framings.Where(f => f.OverrideFor(preset.Id) != null).ToList();
            if (exceptions.Count == 0)
            {
                box.Add(Gs.Label("예외 촬영 없음", "gs-caption"));
                return Titled("규격 사용 현황", box);
            }
            box.Add(Gs.Label($"예외 촬영 {exceptions.Count}개", "gs-heading"));
            foreach (var framing in exceptions)
            {
                var custom = framing.OverrideFor(preset.Id);
                var row = Gs.Row("gs-sheet__row");
                row.Add(Gs.Label(framing.source != null ? framing.source.name : "(원본 없음)", "gs-grow"));
                if (custom.needsReview)
                {
                    var badge = new GsBadge("확인 필요", Gs.Warning);
                    badge.tooltip = custom.note;
                    row.Add(badge);
                }
                if (framing.source != null)
                {
                    var target = framing;
                    row.Add(new GsButton("맞추기", () =>
                    {
                        var host = row.panel?.visualTree.Q(className: "gs-root");
                        if (host != null) GsSpriteFraming.Open(host, target.source, target.group);
                    }, GsButtonKind.Ghost));
                }
                box.Add(row);
            }
            return Titled("규격 사용 현황", box);
        }
    }
}
