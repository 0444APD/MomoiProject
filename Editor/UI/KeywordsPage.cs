using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GBible.Data;
using UnityEngine.UIElements;

namespace GBible.EditorTools.UI
{
    // 도감의 키워드 탭: 키워드 그룹과 키워드를 한 화면에서 만들고, 끌어서 정리한다.
    // 그룹 카드 아래에는 이 그룹에 연결된 클래스 필드가 나열되고, 누르면 그 클래스의 상세(연결 설정)로 간다.
    public sealed class KeywordsPage : GroupedCodexPage
    {
        public const string PageKey = "keywords";
        public override string Key => PageKey;

        protected override Type GroupType => typeof(KeywordGroupData);
        protected override Type ItemType => typeof(KeywordData);
        protected override string GroupNoun => "그룹";
        protected override string ItemNoun => "키워드";
        protected override string EmptyHint => "그룹은 키워드의 부모입니다. 예: grade(common·rare·legendary), equipmentTag(melee·ranged·dot).";

        // 그룹마다 연결된 필드. 모든 클래스의 필드를 훑어야 해서 화면을 그릴 때 한 번만 모은다.
        ILookup<string, (Type type, FieldInfo field)> _links;

        IEnumerable<(Type type, FieldInfo field)> LinkedTo(string group) =>
            (_links ??= KeywordTools.Fields().ToLookup(f => KeywordTools.GroupOf(f.type, f.field)))[group];

        public override void Build(CodexPageContext context)
        {
            _links = null;
            base.Build(context);
            _links = null;
        }

        protected override string GroupInfo(GameData group, int count) =>
            $"키워드 {count}개  ·  연결된 필드 {LinkedTo(group.Id).Count()}개";

        // 이 그룹에서 값을 고르는 필드들. 연결을 바꾸려면 눌러서 클래스 상세로 간다(연결은 도감 › 클래스 › 클래스 상세에서 정한다).
        protected override void GroupFooter(CodexPageContext context, GameData group, VisualElement body)
        {
            var fields = LinkedTo(group.Id).ToList();
            var row = Gs.Row("gs-wrap");
            row.style.marginTop = 4;
            var label = Gs.Label("연결된 필드", "gs-caption");
            label.style.marginRight = 8;
            row.Add(label);
            if (fields.Count == 0)
            {
                var none = Gs.Label("없음 — 도감 › 클래스에서 클래스를 눌러 필드에 이 그룹을 연결하세요", "gs-caption");
                none.style.opacity = 0.7f;
                row.Add(none);
            }
            foreach (var (type, field) in fields)
            {
                var linkType = type;
                var linkField = field.Name;
                var chip = new GsBadge($"{type.Name}.{field.Name}", Gs.PaletteFor("class:" + type.Name));
                chip.tooltip = $"{ClassTypes.Label(type)} › {field.Name}  ·  {(KeywordTools.IsMulti(field) ? "여러 개 고름" : "하나 고름")}\n눌러서 클래스 상세 열기";
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

        protected override void NewGroup(CodexPageContext context, string parent) =>
            KeywordDialogs.NewGroup(context.Host, string.IsNullOrEmpty(parent) ? null : $"'{parent}' 안에 하위 그룹", id =>
            {
                PlaceUnder(id, parent);
                context.Refresh?.Invoke();
            }, staged: true);

        protected override void AddItems(CodexPageContext context, GameData group) =>
            KeywordDialogs.AddKeywords(context.Host, group.Id, _ => context.Refresh?.Invoke(), staged: true);
    }
}
