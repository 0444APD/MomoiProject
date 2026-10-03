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
        // 덱에 카드 한 장을 넣을 판 데이터. 원본은 복사하지 않고 ID만 든다.
        public static DeckCard NewCard(GameApi game, string card)
        {
            if (CardData(game, card) == null) throw new InvalidOperationException($"카드 '{card}'이(가) 도감에 없습니다.");
            return new DeckCard { id = game.NewId("card"), card = card };
        }

        // 원본(도감 › 인스턴스)을 복사 없이 읽는다. 게임 중에는 고치지 않는다(모든 판이 같이 쓰는 값).
        public static Card CardData(GameApi game, string card) => game.Session.Instances?.Invoke(card) as Card;
        public static MonsterData MonsterOf(GameApi game, string monster) => game.Session.Instances?.Invoke(monster) as MonsterData;
        public static Potion PotionOf(GameApi game, string potion) => game.Session.Instances?.Invoke(potion) as Potion;

        // 몬스터 원본으로 이번 전투의 적 하나를 만든다(바뀌는 값 hp만 판 데이터에, 패턴 같은 고정 값은 원본에서).
        public static Enemy NewEnemy(GameApi game, string monster)
        {
            var data = MonsterOf(game, monster) ?? throw new InvalidOperationException($"몬스터 '{monster}'이(가) 도감에 없습니다.");
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

        public static DeckCard CardOf(GameApi game, string cardId) => game.Find<DeckCard>(cardId);

        public static bool InBattle(GameApi game) => game.Data().Run.battle != null;

        public static IEnumerable<Enemy> LivingEnemies(GameApi game) =>
            game.Data().Run.battle?.enemies.Where(e => !e.IsDead) ?? Enumerable.Empty<Enemy>();
    }
}
