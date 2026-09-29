using UnityEngine;

namespace GameSystems.Samples
{
    // ── 샘플 이벤트 ─────────────────────────────────────────────
    [GameEvent("적 처치")]
    public sealed class EnemyKilled
    {
        public string Enemy;
        public int Count = 1;
        public int Combo;
    }

    [GameEvent("골드 획득")]
    public sealed class GoldEarned
    {
        public int Amount;
    }

    // ── 샘플 행동 (데이터) ──────────────────────────────────────
    [System.Serializable]
    public sealed class AttackAction : GameAction
    {
        public string target = "고블린";

        public override string Describe() => $"플레이어 {actorId}: {target} 공격";
    }

    [System.Serializable]
    public sealed class OpenChestAction : GameAction
    {
        public override ActionVisibility Visibility => ActionVisibility.Private;
        public override string Describe() => $"플레이어 {actorId}: 상자 열기";
    }

    // 샘플 연결: 세션이 시작되면 행동 처리기와 디버그 명령을 등록한다.
    public static class SampleGame
    {
        // 게임 상태는 반드시 상태 저장소에 둔다(static 변수에 두면 무르기·되감기에서 빠진다).
        const string ComboKey = "sample:combo";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Hook()
        {
            GameSystemsRuntime.Started += Register;
        }

        static void Register(GameSession session)
        {
            session.State.Define(ComboKey, StateZone.Session, 0, "현재 콤보");
            session.Actions.Register<AttackAction>(action => Attack(session, action));
            session.Actions.Register<OpenChestAction>(_ => OpenChest(session));

            session.Debug.Register("샘플", "고블린 공격", () => session.Do(new AttackAction()), "명중하면 적 처치 이벤트가 발생합니다(전투 난수 스트림).");
            session.Debug.Register("샘플", "오크 공격 ×10", () =>
            {
                for (int i = 0; i < 10; i++) session.Do(new AttackAction { target = "오크" });
            });
            session.Debug.Register("샘플", "상자 열기", () => session.Do(new OpenChestAction()), "드롭 난수 스트림으로 골드를 얻습니다.");
            session.Debug.Register("샘플", "무르기", () => session.Undo(), "마지막 행동을 되돌립니다(몇 번이든 뒤에서부터).");
        }

        static void Attack(GameSession session, AttackAction action)
        {
            var combat = session.Random.Get("combat");
            if (!combat.Chance(0.7))
            {
                session.State.Set(ComboKey, 0);
                session.Journal.Append(JournalKind.System, "Sample", $"{action.target} 공격 빗나감");
                return;
            }
            var combo = (int)session.State.GetNumber(ComboKey) + 1;
            session.State.Set(ComboKey, combo);
            session.Events.Publish(new EnemyKilled { Enemy = action.target, Count = 1, Combo = combo });
        }

        static void OpenChest(GameSession session)
        {
            var drop = session.Random.Get("drop");
            session.Events.Publish(new GoldEarned { Amount = drop.Range(10, 60) });
        }
    }
}
