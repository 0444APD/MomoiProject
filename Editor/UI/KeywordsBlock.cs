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
                row.Add(Gs.Label(keyword.DisplayName, "gs-grow"));
                row.Add(Gs.Label(keyword.Id, "gs-caption"));
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

    // 키워드 만들기 대화 상자(블록·연결 창·드롭다운 공용).
    public static class KeywordDialogs
    {
        const string LinesCaption = "한 줄에 하나씩 'ID 표시이름'. 예: legendary 전설. 표시 이름은 생략할 수 있습니다. ID는 영문 소문자·숫자·밑줄.";

        public static void AddKeywords(VisualElement host, string group, Action<List<string>> done)
        {
            var groupName = EditorDatabase.Instance.FindById(group)?.DisplayName ?? group;
            GsDialog.Form(host, $"'{groupName}'에 키워드 추가", "필드에는 키워드의 ID가 저장됩니다. 표시 이름은 나중에 바꿔도 값에 영향이 없습니다.",
                new[] { new DialogField { Key = "lines", Label = "키워드", Value = "", Caption = LinesCaption, Multiline = true } },
                "추가",
                values =>
                {
                    var lines = KeywordTools.ParseLines(values["lines"]);
                    return lines.Count == 0 ? "키워드를 하나 이상 입력하세요." : KeywordTools.CheckNew(lines.Select(l => l.id));
                },
                values =>
                {
                    var created = new List<string>();
                    foreach (var (id, name) in KeywordTools.ParseLines(values["lines"])) created.Add(KeywordTools.CreateKeyword(group, id, name).Id);
                    done?.Invoke(created);
                });
        }

        // 새 그룹과 그 키워드를 한 번에. 만든 그룹 ID를 돌려준다.
        public static void NewGroup(VisualElement host, string title, Action<string> done)
        {
            GsDialog.Form(host, title ?? "새 키워드 그룹", "그룹은 키워드의 부모입니다. 예: 등급(전설·레어·일반), 장비 태그(불속성·얼음·전기).",
                new[]
                {
                    new DialogField { Key = "id", Label = "그룹 ID", Value = "", Caption = "예: grade, equipment_tag" },
                    new DialogField { Key = "name", Label = "그룹 표시 이름", Value = "", Caption = "예: 등급" },
                    new DialogField { Key = "lines", Label = "키워드", Value = "", Caption = LinesCaption, Multiline = true },
                },
                "만들기",
                values =>
                {
                    if (!EditorDatabase.Instance.CanUseId(values["id"], null, out var error)) return "그룹 ID: " + error;
                    var lines = KeywordTools.ParseLines(values["lines"]);
                    if (lines.Any(l => l.id == values["id"])) return "그룹 ID와 키워드 ID가 같을 수 없습니다.";
                    return KeywordTools.CheckNew(lines.Select(l => l.id));
                },
                values =>
                {
                    var group = KeywordTools.CreateGroup(values["id"], values["name"]);
                    foreach (var (id, name) in KeywordTools.ParseLines(values["lines"])) KeywordTools.CreateKeyword(group.Id, id, name);
                    done?.Invoke(group.Id);
                });
        }
    }
}
