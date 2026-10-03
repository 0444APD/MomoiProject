// 카드 던전 — 카드·물약 효과 Block. 프로그래머는 동작 하나를 Block 하나로 만들고, 기획자는 도감에서 조립하고 수치를 정한다.
// 대상 Block(누구에게)과 효과 Block(무엇을)을 '대상에게 효과' 결과 안에서 조합한다.
using System;
using System.Collections.Generic;
using System.Linq;
using GBible;
using GBible.Ids;

namespace CardDungeon
{
    [Serializable, BlockInfo("피해", Category = "카드", Description = "대상에게 피해를 줍니다(방어도 먼저).")]
    public class DealDamage : Effect<Fighter>
    {
        public int amount = 6;
        protected override void Apply(Fighter target) => Rules.Damage(Game, User<Fighter>(), target, amount);
        public override string Describe() => $"피해 {amount}";
    }

    [Serializable, BlockInfo("방어도", Category = "카드", Description = "대상의 방어도를 올립니다(이번 턴).")]
    public class GainBlock : Effect<Fighter>
    {
        public int amount = 5;
        protected override void Apply(Fighter target) => target.block += amount;
        public override string Describe() => $"방어도 +{amount}";
    }

    [Serializable, BlockInfo("회복", Category = "카드", Description = "대상의 체력을 최대 체력까지 회복합니다.")]
    public class Heal : Effect<Fighter>
    {
        public int amount = 10;
        protected override void Apply(Fighter target) => target.hp = Math.Min(target.maxHp, target.hp + amount);
        public override string Describe() => $"회복 {amount}";
    }

    [Serializable, BlockInfo("상태 걸기", Category = "카드")]
    public class AddStatus : Effect<Fighter>
    {
        [Keyword(typeof(Status))] public string status = Status.Burn;

        protected override void Apply(Fighter target)
        {
            if (!target.statuses.Contains(status)) target.statuses.Add(status);
        }
    }

    [Serializable, BlockInfo("모든 적", Category = "대상", Description = "전투 중인 적 모두(고른 대상은 버립니다).")]
    public class AllEnemies : TargetSelector<Fighter>
    {
        protected override IEnumerable<Fighter> Select(IEnumerable<Fighter> targets, Fighter user) =>
            Rules.LivingEnemies(Game).Cast<Fighter>().ToList();
    }

    [Serializable, BlockInfo("무작위 적", Category = "대상")]
    public class RandomEnemy : TargetSelector<Fighter>
    {
        public int count = 1;

        protected override IEnumerable<Fighter> Select(IEnumerable<Fighter> targets, Fighter user)
        {
            var pool = Rules.LivingEnemies(Game).ToList();
            var random = Game.Random("cardTarget");
            for (var i = 0; i < count && pool.Count > 0; i++)
            {
                var pick = pool[random.Range(0, pool.Count)];
                yield return pick;
            }
        }
    }
}
