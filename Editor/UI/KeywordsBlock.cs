using System;
using System.Collections.Generic;
using System.Linq;
using GameSystems.Data;
using UnityEngine.UIElements;

namespace GameSystems.EditorTools.UI
{
    // 키워드 그룹 항목의 "키워드" 블록: 이 그룹의 키워드와 연결된 필드를 보여 주고, 키워드를 바로 추가한다.
    [LayoutBlock(Id, "키워드", Description = "이 키워드 그룹에 속한 키워드와, 그룹이 연결된 필드를 보여 줍니다. 키워드를 여러 개 한 번에 추가할 수 있습니다.")]
    public sealed class KeywordsBlock : GsLayoutBlock
    {
        public const string Id = "keyword-list";
        public override bool AutoInclude(Type type) => type == typeof(KeywordGroupData);

        public override VisualElement Build(DataViewContext context, BlockPlacement placement, ISet<string> claimed)
        {
            var box = Gs.Col();
            if (!(context.Target is KeywordGroupData group)) return Titled("키워드", Gs.Label("키워드 그룹 항목에서만 보입니다.", "gs-caption"));
            var keywords = KeywordTools.KeywordsOf(group.Id).ToList();
            if (keywords.Count == 0) box.Add(Gs.Label("아직 키워드가 없습니다.", "gs-caption"));
            foreach (var keyword in keywords)
            {
                var row = Gs.Row("gs-sheet__row");
                row.Add(Gs.Label(keyword.Id, "gs-grow"));
                box.Add(row);
            }
            var tools = Gs.Row();
            tools.style.marginTop = 6;
            GsButton add = null;
            add = new GsButton("+ 키워드 추가", () =>
            {
                var host = add.panel?.visualTree.Q(className: "gs-root") ?? box;
                KeywordDialogs.AddKeywords(host, group.Id, _ =>
                {
                    box.Clear();
                    box.Add(Build(context, placement, claimed));
                });
            }, GsButtonKind.Ghost);
            tools.Add(add);
            tools.Add(new GsButton("키워드 연결 창", () => KeywordWindow.Open(group.Id), GsButtonKind.Ghost));
            box.Add(tools);

            var fields = KeywordTools.FieldsLinkedTo(group.Id).ToList();
            var linked = Gs.Col();
            linked.style.marginTop = 12;
            linked.Add(Gs.Label("연결된 필드", "gs-detail__label"));
            if (fields.Count == 0) linked.Add(Gs.Label("아직 이 그룹을 쓰는 필드가 없습니다.", "gs-caption"));
            foreach (var (type, field) in fields)
            {
                var row = Gs.Row("gs-sheet__row");
                row.Add(Gs.Label($"{ClassTypes.Label(type)} › {KeywordTools.FieldLabel(field)}", "gs-grow"));
                row.Add(Gs.Label(KeywordTools.IsMulti(field) ? "여러 개" : "하나", "gs-caption"));
                linked.Add(row);
            }
            box.Add(linked);
            return Titled("키워드", box);
        }
    }

    // 키워드 만들기 대화 상자(도감 키워드 탭·그룹 블록·연결 창·드롭다운 공용). 항목은 ID로만 식별한다.
    public static class KeywordDialogs
    {
        const string IdsCaption = "여러 개는 줄바꿈·띄어쓰기·쉼표로 나눕니다. 예: common rare legendary";

        public static void AddKeywords(VisualElement host, string group, Action<List<string>> done, bool? staged = null)
        {
            GsDialog.Form(host, $"'{group}'에 키워드 추가", "필드에는 키워드 ID가 저장됩니다. ID를 바꾸면 쓰던 곳이 함께 바뀝니다.",
                new[] { new DialogField { Key = "ids", Label = "키워드 ID", Value = "", Caption = IdsCaption, Multiline = true } },
                "추가",
                values =>
                {
                    var ids = CodexGroups.ParseIds(values["ids"]);
                    return ids.Count == 0 ? "키워드 ID를 하나 이상 입력하세요." : CodexGroups.CheckNew(ids);
                },
                values =>
                {
                    var created = CodexGroups.ParseIds(values["ids"]).Select(id => KeywordTools.CreateKeyword(group, id, staged).Id).ToList();
                    done?.Invoke(created);
                });
        }

        // 새 그룹과 그 키워드를 한 번에. 만든 그룹 ID를 돌려준다.
        public static void NewGroup(VisualElement host, string title, Action<string> done, bool? staged = null)
        {
            GsDialog.Form(host, title ?? "새 키워드 그룹", "그룹은 키워드의 부모입니다. 예: grade(common·rare·legendary), equipmentTag(melee·ranged·dot).",
                new[]
                {
                    new DialogField { Key = "id", Label = "그룹 ID", Value = "", Caption = "예: grade, equipmentTag" },
                    new DialogField { Key = "ids", Label = "키워드 ID (선택)", Value = "", Caption = IdsCaption, Multiline = true },
                },
                "만들기",
                values =>
                {
                    if (!EditorDatabase.Instance.CanUseId(values["id"], null, out var error)) return "그룹 ID: " + error;
                    var ids = CodexGroups.ParseIds(values["ids"]);
                    if (ids.Any(i => string.Equals(i, values["id"], StringComparison.OrdinalIgnoreCase))) return "그룹 ID와 키워드 ID가 같을 수 없습니다.";
                    return CodexGroups.CheckNew(ids);
                },
                values =>
                {
                    var group = KeywordTools.CreateGroup(values["id"], staged);
                    foreach (var id in CodexGroups.ParseIds(values["ids"])) KeywordTools.CreateKeyword(group.Id, id, staged);
                    done?.Invoke(group.Id);
                });
        }
    }
}
