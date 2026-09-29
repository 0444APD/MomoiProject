using System;
using System.Collections.Generic;
using System.Linq;
using GameSystems.Data;
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
        [FieldLabel("태그"), DataRef(typeof(TagData))] public List<string> Tags = new List<string>();
    }

    // ── 샘플 게임 데이터(개발자가 짜는 클래스) ──────────────────
    public enum SampleElement
    {
        None,
        Fire,
        Ice,
        Poison,
    }

    [Serializable]
    public sealed class SampleEnhancement
    {
        [FieldLabel("효과"), DataRef(typeof(TagData))] public string effect = "";
        [FieldLabel("수치")] public int power;
    }

    [Serializable]
    public sealed class SampleEquipment
    {
        [FieldLabel("이름")] public string name = "";
        [FieldLabel("아이콘")] public Sprite icon;
        [FieldLabel("등급")] public int grade = 1;
        [FieldLabel("속성")] public SampleElement element;
        [FieldLabel("태그"), DataRef(typeof(TagData))] public List<string> tags = new List<string>();
        [FieldLabel("강화")] public List<SampleEnhancement> enhancements = new List<SampleEnhancement>();
    }

    [Serializable]
    public sealed class SampleInventory
    {
        [FieldLabel("장비")] public List<SampleEquipment> items = new List<SampleEquipment>();
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
    public sealed class LootAction : GameAction
    {
        public override string Describe() => $"플레이어 {actorId}: 장비 획득";
    }

    [System.Serializable]
    public sealed class EnhanceAction : GameAction
    {
        public override string Describe() => $"플레이어 {actorId}: 첫 장비 강화";
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
        // 게임 상태는 반드시 상태 저장소에 둔다(static 변수에 두면 Undo·되감기에서 빠진다).
        const string ComboKey = "sample:combo";
        // 도감의 상태 키(샘플 데이터 만들기로 생성). 없으면 장비 명령은 안내만 한다.
        const string InventoryKey = "player.inventory";

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
            session.Actions.Register<LootAction>(_ => Loot(session));
            session.Actions.Register<EnhanceAction>(_ => Enhance(session));

            session.Debug.Register("샘플", "고블린 공격", () => session.Do(new AttackAction()), "명중하면 적 처치 이벤트가 발생합니다(전투 난수 스트림).");
            session.Debug.Register("샘플", "오크 공격 ×10", () =>
            {
                for (int i = 0; i < 10; i++) session.Do(new AttackAction { target = "오크" });
            });
            session.Debug.Register("샘플", "상자 열기", () => session.Do(new OpenChestAction()), "드롭 난수 스트림으로 골드를 얻습니다.");
            session.Debug.Register("샘플", "드래곤(보스) 공격", () => session.Do(new AttackAction { target = "드래곤" }), "보스 태그가 붙은 처치 이벤트 → 보스 처치 수·용 사냥꾼");
            session.Debug.Register("샘플", "장비 획득(템플릿)", () => Guarded(session, new LootAction()), "도감의 장비 템플릿 중 하나의 사본을 인벤토리에 넣습니다.");
            session.Debug.Register("샘플", "첫 장비 강화", () => Guarded(session, new EnhanceAction()), "첫 장비에 강화 효과를 하나 붙입니다(강화 장인 조건).");
            session.Debug.Register("샘플", "Undo", () => session.Undo(), "마지막 행동을 되돌립니다(몇 번이든 뒤에서부터).");
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
            var tags = action.target == "드래곤" ? new List<string> { "boss" } : new List<string>();
            session.Events.Publish(new EnemyKilled { Enemy = action.target, Count = 1, Combo = combo, Tags = tags });
        }

        static void Guarded(GameSession session, GameAction action)
        {
            if (!session.State.IsDefined(InventoryKey))
            {
                session.Journal.Append(JournalKind.Warning, "Sample", "인벤토리 상태 키가 없습니다. 메뉴 Game Systems > 샘플 > 샘플 데이터 만들기를 먼저 실행하세요.");
                return;
            }
            session.Do(action);
        }

        // 도감의 장비 템플릿 중 하나(드롭 난수)의 사본을 넣는다.
        static void Loot(GameSession session)
        {
            var templates = GameDatabase.Load()?.All<TemplateData>().Where(t => t.value is SampleEquipment).ToList();
            if (templates == null || templates.Count == 0) throw new InvalidOperationException("장비 템플릿이 없습니다(샘플 데이터 만들기).");
            var pick = templates[session.Random.Get("drop").Range(0, templates.Count)];
            session.State.Modify<SampleInventory>(InventoryKey, inv => inv.items.Add(pick.CreateInstance<SampleEquipment>()));
        }

        static void Enhance(GameSession session)
        {
            var effects = new[] { "lifesteal", "fire_resist" };
            var roll = session.Random.Get("enhance");
            session.State.Modify<SampleInventory>(InventoryKey, inv =>
            {
                if (inv.items.Count == 0) throw new InvalidOperationException("강화할 장비가 없습니다.");
                inv.items[0].enhancements.Add(new SampleEnhancement { effect = effects[roll.Range(0, effects.Length)], power = roll.Range(1, 10) });
            });
        }

        static void OpenChest(GameSession session)
        {
            var drop = session.Random.Get("drop");
            session.Events.Publish(new GoldEarned { Amount = drop.Range(10, 60) });
        }
    }
}
