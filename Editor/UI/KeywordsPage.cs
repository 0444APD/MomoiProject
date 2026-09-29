using System;
using System.Linq;
using GameSystems.Data;
using UnityEngine.UIElements;

namespace GameSystems.EditorTools.UI
{
    // 도감의 키워드 탭: 키워드 그룹과 키워드를 한 화면에서 만들고, 끌어서 정리한다.
    public sealed class KeywordsPage : GroupedCodexPage
    {
        public const string PageKey = "keywords";
        public override string Key => PageKey;

        protected override Type GroupType => typeof(KeywordGroupData);
        protected override Type ItemType => typeof(KeywordData);
        protected override string GroupNoun => "그룹";
        protected override string ItemNoun => "키워드";
        protected override string EmptyHint => "그룹은 키워드의 부모입니다. 예: grade(common·rare·legendary), equipmentTag(melee·ranged·dot).";

        protected override string ItemTooltip(GameData item) =>
            item.Id + (item is KeywordData k && !string.IsNullOrEmpty(k.description) ? "\n" + k.description : "");

        protected override string GroupInfo(GameData group, int count) =>
            $"키워드 {count}개  ·  연결된 필드 {KeywordTools.FieldsLinkedTo(group.Id).Count()}개";

        protected override void Toolbar(CodexPageContext context, VisualElement bar)
        {
            var link = new GsButton("필드에 연결…", () => KeywordWindow.Open(null), GsButtonKind.Ghost);
            link.tooltip = "키워드 연결 창: 클래스의 string 필드에 키워드 그룹을 연결합니다.";
            bar.Add(link);
        }

        protected override void NewGroup(CodexPageContext context) =>
            KeywordDialogs.NewGroup(context.Host, null, _ => context.Refresh?.Invoke(), staged: true);

        protected override void AddItems(CodexPageContext context, GameData group) =>
            KeywordDialogs.AddKeywords(context.Host, group.Id, _ => context.Refresh?.Invoke(), staged: true);
    }
}
