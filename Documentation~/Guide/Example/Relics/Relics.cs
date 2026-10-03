// 카드 던전 — 프로젝트 고유 기능: 유물. "가지고 있으면 어떤 순간에 무언가 한다"(Trigger → 결과).
// 도감에 '유물' 탭이 생기고, 기획자가 발동(Trigger)과 효과(결과)를 조립한다. 로그·도전과제와 같은 편집 화면을 쓴다.
using System.Collections.Generic;
using GBible;
using GBible.Data;
using GBible.Ids;
using UnityEngine;

namespace CardDungeon
{
    [Database("유물", Group = "콘텐츠", Order = 40, IdPrefix = "relic")]
    public class RelicData : GameData
    {
        [FieldLabel("설명")] public string description = "";
        [FieldLabel("아이콘")] public Sprite icon;
        [FieldLabel("발동"), SerializeReference, TriggerSlot(TriggerKinds.Event | TriggerKinds.Condition)]
        public Trigger trigger;
        [FieldLabel("효과"), SerializeReference, OutcomeSlot(OutcomeKinds.PlayerData | OutcomeKinds.Coded | OutcomeKinds.Targeted)]
        public List<Outcome> outcomes = new List<Outcome>();

        public override void ValidateData(IValidationContext context)
        {
            if (trigger == null) context.Error("발동(트리거)이 없습니다.");
            else trigger.Validate(context);
            foreach (var outcome in outcomes)
                if (outcome == null) context.Error("비어 있는 효과 칸이 있습니다.");
                else outcome.Validate(context);
        }
    }

    // 게임이 켜질 때 모든 유물의 발동을 감시한다. 발동하면 그 유물을 가지고 있을 때만 효과를 낸다.
    public static class RelicRuntime
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Hook() => GBibleRuntime.Started += Start;

        static void Start(GameSession session)
        {
            var database = GameDatabase.Load();
            if (database == null) return;
            foreach (var relic in database.All<RelicData>())
            {
                var id = relic.Id;
                IEnumerable<Outcome> outcomes = BoundOutcomes.Has(id) ? BoundOutcomes.Create(id) : relic.outcomes;
                session.Triggers.Register(id, relic.trigger, repeat: true, fire: moment =>
                {
                    var run = session.Game.Data().Run;
                    if (run == null || !run.relics.Contains(id)) return;
                    session.Triggers.RunOutcomes(outcomes, moment, "Relics");
                });
            }
        }
    }

    public static class RelicCode
    {
        // 불타는 피: 전투 승리 때 체력 6 회복(조건이 붙은 숫자 계산이라 코드로).
        [BindTo(GameIds.Relic.BurningBlood)]
        static void BurningBlood(BattleWon e, TriggerContext c)
        {
            var hero = c.Game.Data().Run.hero;
            hero.hp = System.Math.Min(hero.maxHp, hero.hp + 6);
        }
    }
}
