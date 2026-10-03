// 카드 던전 — 항목 하나만의 결과를 함수로([BindTo]). Block으로 조립하기 어려운 예외 로직만 여기에 쓴다.
// 연결된 도감 항목의 결과 칸은 잠기고, 이 함수 이름과 cs 열기가 보인다.
using System.Linq;
using GBible;
using GBible.Ids;

namespace CardDungeon
{
    public static class CardCode
    {
        // 연쇄 번개: 적마다 차례로 3, 4, 5 … 피해(대상 Block 조합으로는 '점점 커지는 피해'를 나타내기 어렵다).
        [BindTo(GameIds.Instance.ChainLightning)]
        static void ChainLightning(TriggerContext c)
        {
            var hero = c.User as Fighter;
            var damage = 3;
            foreach (var enemy in Rules.LivingEnemies(c.Game).ToList()) Rules.Damage(c.Game, hero, enemy, damage++);
        }
    }

    public static class ProgressCode
    {
        // 첫 승리 도전과제: 연쇄 번개 카드를 해금한다(프로필 = 메타 구역, 판이 끝나도 남는다).
        [BindTo(GameIds.Achievement.FirstWin)]
        static void FirstWin(BattleWon e, TriggerContext c)
        {
            var profile = c.Game.Data().Profile;
            if (!profile.unlocked.Contains(GameIds.Instance.ChainLightning)) profile.unlocked.Add(GameIds.Instance.ChainLightning);
        }
    }
}
