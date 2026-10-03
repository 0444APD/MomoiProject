// 카드 던전 — 규칙 도우미. 행동·카드 블록·[BindTo] 코드가 같이 쓰는 게임 고유 로직은 여기 모은다(평범한 static 함수).
using System;
using System.Collections.Generic;
using System.Linq;
using GBible;
using GBible.Ids;

namespace CardDungeon
{
    public static class Rules
    {
        // 원본 카드(도감 › 인스턴스)의 사본을 만들어 덱에 넣는다. 사본마다 id가 다르다.
        public static Card NewCard(GameApi game, string source)
        {
            var card = (Card)game.Session.CopyInstance(source) ?? throw new InvalidOperationException($"카드 '{source}'이(가) 도감에 없습니다.");
            card.id = game.NewId("card");
            card.source = source;
            return card;
        }

        // 몬스터 원본으로 이번 전투의 적 하나를 만든다.
        public static Enemy NewEnemy(GameApi game, string monster)
        {
            var data = (MonsterData)game.Session.CopyInstance(monster) ?? throw new InvalidOperationException($"몬스터 '{monster}'이(가) 도감에 없습니다.");
            return new Enemy { id = game.NewId("enemy"), name = data.title, hp = data.maxHp, maxHp = data.maxHp, monster = monster };
        }

        // 피해: 방어도가 먼저 깎이고 남은 만큼 체력. 적이 쓰러지면 전투에서 빼고 이벤트를 쏜다.
        public static void Damage(GameApi game, Fighter source, Fighter target, int amount)
        {
            if (target == null || target.IsDead || amount <= 0) return;
            var blocked = Math.Min(target.block, amount);
            target.block -= blocked;
            var dealt = amount - blocked;
            target.hp -= dealt;

            var run = game.Data().Run;
            if (run.battle != null && source == run.hero) run.battle.damageThisTurn += dealt;
            game.Events.Publish(new DamageDealt { sourceId = source?.id, targetId = target.id, amount = dealt });

            if (target is Enemy enemy && enemy.IsDead && run.battle != null)
            {
                run.battle.enemies.Remove(enemy);
                game.Events.Publish(new EnemyKilled { killerId = source?.id, enemyId = enemy.id, monster = enemy.monster });
            }
        }

        // 결정론 난수로 섞는다(같은 시드·같은 행동이면 같은 순서, Undo하면 난수도 되돌아간다).
        public static void Shuffle(GameApi game, List<string> pile)
        {
            var random = game.Random("deck");
            for (var i = pile.Count - 1; i > 0; i--)
            {
                var j = random.Range(0, i + 1);
                (pile[i], pile[j]) = (pile[j], pile[i]);
            }
        }

        public static Card CardOf(GameApi game, string cardId) => game.Find<Card>(cardId);

        public static bool InBattle(GameApi game) => game.Data().Run.battle != null;

        public static IEnumerable<Enemy> LivingEnemies(GameApi game) =>
            game.Data().Run.battle?.enemies.Where(e => !e.IsDead) ?? Enumerable.Empty<Enemy>();
    }
}
