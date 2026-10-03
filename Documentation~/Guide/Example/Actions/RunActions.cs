// 카드 던전 — 판 단위 행동. 행동 하나 = Undo 한 단위 = 네트워크 한 묶음.
// 행동 안에서 다른 행동(Game.Do)·능력(Game.Use)을 부르면 바깥 행동의 일부로 그 자리에서 실행된다.
using System;
using System.Collections.Generic;
using GBible;
using GBible.Ids;

namespace CardDungeon
{
    // 새 판을 연다: 주인공·시작 덱·시작 유물을 정하고 첫 전투로.
    [Serializable, BlockInfo("새 판 시작")]
    public class StartRun : SimpleAction
    {
        protected override void Run()
        {
            var run = Game.Data().Run;
            run.floor = 1;
            run.gold = 99;
            run.hero = new Hero { id = "hero", name = "모모이", hp = 70, maxHp = 70 };
            run.deck.Clear();
            for (var i = 0; i < 5; i++) run.deck.Add(Rules.NewCard(Game, GameIds.Instance.Strike));
            for (var i = 0; i < 4; i++) run.deck.Add(Rules.NewCard(Game, GameIds.Instance.Defend));
            run.deck.Add(Rules.NewCard(Game, GameIds.Instance.Fireball));
            run.relics.Clear();
            run.relics.Add(GameIds.Relic.BurningBlood);
            run.potions.slots.Clear();
            run.potions.Add(GameIds.Instance.HealPotion, 1, maxStack: Rules.PotionOf(Game, GameIds.Instance.HealPotion)?.maxStack ?? 1);
            Game.Do(new StartBattle { monsters = { GameIds.Instance.Goblin, GameIds.Instance.Slime } });
        }
    }

    // 상점에서 카드 사기.
    [Serializable, BlockInfo("카드 사기")]
    public class BuyCard : SimpleAction
    {
        public string source;
        public int price;

        protected override void Run()
        {
            var run = Game.Data().Run;
            if (run.gold < price) throw new InvalidOperationException("골드가 모자랍니다.");   // 예외 = 행동 실패, 바뀐 것 모두 취소
            run.gold -= price;
            run.deck.Add(Rules.NewCard(Game, source));
        }

        public override string Describe() => $"카드 사기: {source} ({price}골드)";
    }

    // 물약 마시기: 인벤토리에서 하나 빼고, 물약 원본의 사용 기능을 쓴다.
    [Serializable, BlockInfo("물약 마시기")]
    public class DrinkPotion : SimpleAction
    {
        public string potion;
        public string targetId;

        protected override void Run()
        {
            var run = Game.Data().Run;
            if (!run.potions.TryRemove(potion)) throw new InvalidOperationException("물약이 없습니다.");
            if (string.IsNullOrEmpty(targetId)) Game.Use(potion, run.hero);
            else Game.Use(potion, run.hero, targetId);
        }
    }

    // 판 끝: 프로필(메타 구역)에 남길 것을 정리한다.
    [Serializable, BlockInfo("판 끝")]
    public class EndRun : SimpleAction
    {
        public bool victory;

        protected override void Run()
        {
            var run = Game.Data().Run;
            var profile = Game.Data().Profile;
            profile.bestFloor = Math.Max(profile.bestFloor, run.floor);
            profile.totalGold += run.gold;
            Game.Events.Publish(new RunEnded { floor = run.floor, victory = victory });
            run.battle = null;
            run.floor = 0;   // 판 끝: 이어 하기에서 다시 열리지 않는다(RunState.IsActive)
        }
    }
}
