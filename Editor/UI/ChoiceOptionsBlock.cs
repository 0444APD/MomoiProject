using System;
using System.Collections.Generic;
using System.Linq;
using GameSystems.Data;
using UnityEngine.UIElements;

namespace GameSystems.EditorTools.UI
{
    // 선택지 목록 항목의 "선택지" 블록: 이 목록에 속한 선택지를 보여 주고, 바로 추가한다.
    [LayoutBlock(Id, "선택지", Description = "이 선택지 목록에 속한 선택지를 보여 주고 추가합니다.")]
    public sealed class ChoiceOptionsBlock : GsLayoutBlock
    {
        public const string Id = "choice-options";
        public override bool AutoInclude(Type type) => type == typeof(ChoiceListData);

        public override VisualElement Build(DataViewContext context, BlockPlacement placement, ISet<string> claimed)
        {
            var box = Gs.Col();
            if (!(context.Target is ChoiceListData list)) return Titled("선택지", Gs.Label("선택지 목록 항목에서만 보입니다.", "gs-caption"));
            var db = EditorDatabase.Instance;
            var options = db.OfType<ChoiceData>().Where(c => c.list == list.Id).OrderBy(c => c.DisplayName).ToList();
            if (options.Count == 0) box.Add(Gs.Label("아직 선택지가 없습니다.", "gs-caption"));
            foreach (var option in options)
            {
                var row = Gs.Row("gs-sheet__row");
                row.Add(Gs.Label(option.DisplayName, "gs-grow"));
                row.Add(Gs.Label(option.Id, "gs-caption"));
                box.Add(row);
            }
            GsButton add = null;
            add = new GsButton("+ 선택지 추가", () =>
            {
                var host = add.panel?.visualTree.Q(className: "gs-root") ?? box;
                GsDialog.Form(host, $"'{list.DisplayName}'에 선택지 추가", "ID는 코드(GameIds)·저장값으로 쓰이고, 이름은 화면에 보입니다.",
                    new[]
                    {
                        new DialogField { Key = "id", Label = "ID", Value = "", Caption = "예: weapon, armor" },
                        new DialogField { Key = "name", Label = "표시 이름", Value = "" },
                    },
                    "추가",
                    values => db.CanUseId(values["id"], null, out var error) ? null : error,
                    values =>
                    {
                        var choice = (ChoiceData)db.Create(typeof(ChoiceData), string.IsNullOrWhiteSpace(values["name"]) ? values["id"] : values["name"].Trim(), values["id"]);
                        choice.list = list.Id;
                        UnityEditor.EditorUtility.SetDirty(choice);
                        UnityEditor.AssetDatabase.SaveAssetIfDirty(choice);
                        EditorDatabase.MarkDirty();
                        box.Clear();
                        box.Add(Build(context, placement, claimed));
                    });
            }, GsButtonKind.Ghost);
            add.style.alignSelf = Align.FlexStart;
            add.style.marginTop = 6;
            box.Add(add);
            return Titled("선택지", box);
        }
    }
}
