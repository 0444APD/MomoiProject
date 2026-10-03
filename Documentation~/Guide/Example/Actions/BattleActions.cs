// 카드 던전 — 전투 행동.
using System;
using System.Collections.Generic;
using System.Linq;
using GBible;
using GBible.Ids;

namespace CardDungeon
{
    [Serializable, BlockInfo("전투 시작")]
    public class StartBattle : SimpleAction
    {
        public List<string> monsters = new List<string>();

        protected override void Run()
        {
            var run = Game.Data().Run;
            var battle = new Battle { turn = 1, energy = run.hero.energyPerTurn };
            foreach (var monster in monsters) battle.enemies.Add(Rules.NewEnemy(Game, monster));
            battle.drawPile.AddRange(run.deck.Select(c => c.id));
            Rules.Shuffle(Game, battle.drawPile);
            run.battle = battle;
            run.hero.block = 0;
            Game.Do(new DrawCards { count = run.hero.drawPerTurn });
            Game.Events.Publish(new TurnStarted { turn = 1 });
        }
    }

    // 카드 뽑기. 뽑을 더미가 비면 버린 더미를 섞어 다시 쓴다. 다른 행동 안에서 불린다.
    [Serializable, BlockInfo("카드 뽑기")]
    public class DrawCards : SimpleAction
    {
        public int count = 1;

        protected override void Run()
        {
            var battle = Game.Data().Run.battle;
            for (var i = 0; i < count; i++)
            {
                if (battle.drawPile.Count == 0)
                {
                    if (battle.discard.Count == 0) return;
                    battle.drawPile.AddRange(battle.discard);
                    battle.discard.Clear();
                    Rules.Shuffle(Game, battle.drawPile);
                }
                var top = battle.drawPile[battle.drawPile.Count - 1];
                battle.drawPile.RemoveAt(battle.drawPile.Count - 1);
                battle.hand.Add(top);
            }
        }
    }

    // 카드 내기: 에너지 지불 → 손패에서 버린 더미로 → 카드 원본의 사용 기능 실행 → 이벤트.
    // 대상은 id(target = 카드 사본 id, targetId = 고른 적 id)라서 저장·리플레이·서버 전송이 된다.
    [Serializable, BlockInfo("카드 내기")]
    public class PlayCard : GameAction<DeckCard>
    {
        public string targetId;

        protected override void Run(DeckCard card)
        {
            var run = Game.Data().Run;
            var data = Rules.CardData(Game, card.card);
            var battle = run.battle ?? throw new InvalidOperationException("전투 중이 아닙니다.");
            if (!battle.hand.Contains(card.id)) throw new InvalidOperationException("손에 없는 카드입니다.");
            if (battle.energy < data.cost) throw new InvalidOperationException("에너지가 모자랍니다.");

            battle.energy -= data.cost;
            battle.hand.Remove(card.id);
            battle.discard.Add(card.id);

            if (string.IsNullOrEmpty(targetId)) Game.Use(card.card, run.hero);
            else Game.Use(card.card, run.hero, targetId);
            Game.Events.Publish(new CardPlayed { cardId = card.id, source = card.card, userId = run.hero.id, cost = data.cost });

            if (battle.enemies.Count == 0) Game.Do(new WinBattle());
        }

        public override string Describe() => $"카드 내기: {target}" + (string.IsNullOrEmpty(targetId) ? "" : $" → {targetId}");
    }

    // 턴 끝: 적이 행동하고, 손패를 버리고, 다음 턴을 연다.
    [Serializable, BlockInfo("턴 끝")]
    public class EndTurn : SimpleAction
    {
        protected override void Run()
        {
            var run = Game.Data().Run;
            var battle = run.battle ?? throw new InvalidOperationException("전투 중이 아닙니다.");
            Game.Events.Publish(new TurnEnded { turn = battle.turn, damage = battle.damageThisTurn });

            foreach (var enemy in battle.enemies.ToList())
            {
                var pattern = Rules.MonsterOf(Game, enemy.monster)?.attackPattern;
                if (pattern != null && pattern.Count > 0) Rules.Damage(Game, enemy, run.hero, pattern[enemy.intentIndex % pattern.Count]);
                enemy.intentIndex++;
            }
            if (run.hero.IsDead)
            {
                Game.Do(new EndRun { victory = false });
                return;
            }

            battle.discard.AddRange(battle.hand);
            battle.hand.Clear();
            battle.turn++;
            battle.energy = run.hero.energyPerTurn;
            battle.damageThisTurn = 0;
            run.hero.block = 0;
            Game.Do(new DrawCards { count = run.hero.drawPerTurn });
            Game.Events.Publish(new TurnStarted { turn = battle.turn });
        }
    }

    // 전투 승리: 보상 골드(난수 흐름 "loot")를 주고 다음 층으로.
    [Serializable, BlockInfo("전투 승리")]
    public class WinBattle : SimpleAction
    {
        protected override void Run()
        {
            var run = Game.Data().Run;
            // 마지막 적을 쓰러뜨린 턴도 턴 끝으로 센다(로그 "한 턴 최대 피해"가 놓치지 않게).
            Game.Events.Publish(new TurnEnded { turn = run.battle.turn, damage = run.battle.damageThisTurn });
            var gold = Game.Random("loot").Range(10, 21);
            run.gold += gold;
            Game.Events.Publish(new BattleWon { floor = run.floor, turns = run.battle.turn });
            Game.Events.Publish(new GoldGained { amount = gold });
            run.battle = null;
            run.floor++;
        }
    }
}
