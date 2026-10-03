// 카드 던전 — 이벤트: "방금 일어난 일"을 값과 함께 담는다. 게임 코드가 쏘고, 로그·도전과제·유물·화면이 받는다.
// [GameEvent]가 붙은 형식은 도감·모니터 목록에 나오고, 컴파일할 때 값 읽기 코드가 생긴다(조건·로그가 리플렉션 없이 읽는다).
using GBible;

namespace CardDungeon
{
    [GameEvent("카드 사용")]
    public class CardPlayed
    {
        public string cardId;     // 사본 id
        public string source;     // 원본 ID
        public string userId;
        public int cost;
    }

    [GameEvent("피해")]
    public class DamageDealt
    {
        public string sourceId;
        public string targetId;
        public int amount;
    }

    [GameEvent("적 처치")]
    public class EnemyKilled
    {
        public string killerId;
        public string enemyId;
        public string monster;
    }

    [GameEvent("턴 시작")]
    public class TurnStarted
    {
        public int turn;
    }

    [GameEvent("턴 끝")]
    public class TurnEnded
    {
        public int turn;
        public int damage;        // 이번 턴에 준 피해 합(로그 "한 턴 최대 피해"가 읽는다)
    }

    [GameEvent("전투 승리")]
    public class BattleWon
    {
        public int floor;
        public int turns;
    }

    [GameEvent("골드 획득")]
    public class GoldGained
    {
        public int amount;
    }

    [GameEvent("판 끝")]
    public class RunEnded
    {
        public int floor;
        public bool victory;
    }
}
