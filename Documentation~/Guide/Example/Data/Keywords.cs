// 카드 던전 — 키워드(정해진 목록에서 고르는 글자 값).
// 코드가 곧 키워드 목록이다. 도감 › 키워드 탭에서 고쳐도 이 파일이 고쳐진다.
using GBible;

namespace CardDungeon
{
    [KeywordGroup("등급")]
    public static class Rarity
    {
        public const string Common = "common";
        public const string Rare = "rare";
        public const string Legendary = "legendary";
    }

    [KeywordGroup("속성")]
    public static class Element
    {
        public const string Neutral = "neutral";
        public const string Fire = "fire";
        public const string Ice = "ice";
    }

    [KeywordGroup("카드 종류")]
    public static class CardTag
    {
        public const string Attack = "attack";
        public const string Skill = "skill";
        public const string Power = "power";
    }

    [KeywordGroup("상태")]
    public static class Status
    {
        public const string Burn = "burn";
        public const string Frozen = "frozen";
    }
}
