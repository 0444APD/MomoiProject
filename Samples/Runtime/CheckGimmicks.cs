using System;
using System.Collections.Generic;
using System.Linq;

namespace GBible.Samples
{
    // 확인용 패널 '기믹' 탭: 네 게임 기믹 부품을 눌러 본다(매뉴얼 17·28·29·37장, AI 문서 기믹 목록).
    //   조커 순서(패시브 '놓인 것마다') · Blueprint(결과 '빌려 쓰기' + 대상 '자리로 고르기') · 힘의 부적(가진 동안 능력치 보정, 레벨별 값)
    //   테자스(가로채기 '상태 효과 걸기' → 화상을 푸른 화상으로) · 확률 2배(가로채기 '확률').
    // DB 추가 메뉴(확인용 항목)가 같은 ID로 DB 항목을 만든다. DB 항목이 있으면 그것이 쓰인다.

    public static class CheckGimmickIds
    {
        public const string JokerPlus = "checkJokerPlus";
        public const string JokerTimes = "checkJokerTimes";
        public const string Blueprint = "checkBlueprint";
        public const string Amulet = "checkAmulet";
        public const string Tejas = "checkTejas";
        public const string Oops = "checkOops";
        public const string Burn = "checkBurn";
        public const string BlueBurn = "checkBlueBurn";
    }

    [Serializable]
    public sealed class CheckJoker
    {
        public string id;
        public string joker;   // 원본 ID(패시브의 일치 필드)
        public int mult;
    }

    [Serializable, GameEvent("확인용 점수 단계")]
    public sealed class CheckScored
    {
        public int step;
    }

    [Serializable, BlockInfo("확인용 배수 더하기", Category = "확인용", Description = "확인용 판의 배수에 값을 더한다.")]
    public sealed class CheckAddMult : Outcome
    {
        public int value = 4;
        public override OutcomeKinds Kind => OutcomeKinds.Coded;
        public override void Run(TriggerContext context) => CheckRules.Run(context.Game).mult += value;
        public override string Describe(INameResolver names) => $"배수 +{value}";
    }

    [Serializable, BlockInfo("확인용 배수 곱하기", Category = "확인용", Description = "확인용 판의 배수에 값을 곱한다.")]
    public sealed class CheckTimesMult : Outcome
    {
        public int value = 2;
        public override OutcomeKinds Kind => OutcomeKinds.Coded;
        public override void Run(TriggerContext context) => CheckRules.Run(context.Game).mult *= value;
        public override string Describe(INameResolver names) => $"배수 ×{value}";
    }

    public static class CheckGimmickDefaults
    {
        static PassiveDefinition Joker(string id, Outcome outcome)
        {
            var definition = new PassiveDefinition
            {
                Id = id, OwnerKey = CheckIds.Run, OwnerPath = "jokers", MatchField = "joker", PerElement = true,
                When = new EventTrigger { eventType = typeof(CheckScored).FullName },
            };
            definition.Outcomes.Add(outcome);
            return definition;
        }

        public static PassiveDefinition JokerPlus() => Joker(CheckGimmickIds.JokerPlus, new CheckAddMult { value = 4 });
        public static PassiveDefinition JokerTimes() => Joker(CheckGimmickIds.JokerTimes, new CheckTimesMult { value = 2 });

        public static PassiveDefinition Blueprint() => Joker(CheckGimmickIds.Blueprint, new BorrowOutcome
        {
            targets = { new PlaceTarget { list = new PlayerDataListRef { key = CheckIds.Run, path = "jokers" }, direction = PlaceDirection.Right } },
        });

        public static PassiveDefinition Amulet()
        {
            var definition = new PassiveDefinition { Id = CheckGimmickIds.Amulet, OwnerKey = CheckIds.Run, OwnerPath = "relics", ModifierTarget = "units[checkHero]" };
            definition.Modifiers.Add(new PassiveModifier { attribute = nameof(CheckUnit.attack), kind = ModifierKind.Flat, byLevel = { 2, 3, 4, 6 } });
            return definition;
        }

        public static PassiveDefinition Tejas()
        {
            var definition = new PassiveDefinition
            {
                Id = CheckGimmickIds.Tejas, OwnerKey = CheckIds.Run, OwnerPath = "relics",
                When = new InterceptTrigger { intentType = typeof(StatusIntent).FullName, filter = new FieldFilter { path = "statusId", op = FieldOp.Equal, value = CheckGimmickIds.Burn } },
            };
            definition.Outcomes.Add(new ModifyIntentOutcome { path = "statusId", op = IntentOp.Set, value = CheckGimmickIds.BlueBurn });
            return definition;
        }

        public static PassiveDefinition Oops()
        {
            var definition = new PassiveDefinition { Id = CheckGimmickIds.Oops, OwnerKey = CheckIds.Run, OwnerPath = "relics", When = new InterceptTrigger { intentType = typeof(ChanceIntent).FullName } };
            definition.Outcomes.Add(new ModifyIntentOutcome { path = "probability", op = IntentOp.Multiply, value = "2" });
            return definition;
        }

        public static StatusDefinition Burn(string id, int turns) => new StatusDefinition
        {
            Id = id, DisplayName = id == CheckGimmickIds.Burn ? "확인용 화상" : "확인용 푸른 화상", Kind = StatusKind.Debuff, MaxStacks = 5,
            Duration = StatusDuration.Events, Length = turns, TickEvent = typeof(CheckTurnEnded).FullName,
        };

        public static IEnumerable<PassiveDefinition> Passives() => new[] { JokerPlus(), JokerTimes(), Blueprint(), Amulet(), Tejas(), Oops() };

        // 게임이 켜질 때: DB에 같은 ID가 없으면 코드 정의를 쓴다.
        public static void DefineMissing(GameSession session)
        {
            foreach (var passive in Passives())
                if (session.Passives.Find(passive.Id) == null) session.Passives.Define(passive);
            if (session.Statuses.Find(CheckGimmickIds.Burn) == null) session.Statuses.Define(Burn(CheckGimmickIds.Burn, 2));
            if (session.Statuses.Find(CheckGimmickIds.BlueBurn) == null) session.Statuses.Define(Burn(CheckGimmickIds.BlueBurn, 3));
        }
    }

    // ── 행동 ───────────────────────────────────────────────

    [Serializable, BlockInfo("확인용: 점수 내기")]
    public sealed class CheckScore : GameAction
    {
        protected override void Run()
        {
            CheckRules.Run(Game).mult = 1;
            Game.Events.Publish(new CheckScored { step = 1 });
        }
    }

    [Serializable, BlockInfo("확인용: 조커 자리 바꾸기")]
    public sealed class CheckSwapJokers : GameAction
    {
        protected override void Run()
        {
            var jokers = CheckRules.Run(Game).jokers;
            if (jokers.Count >= 2) jokers.Reverse();
        }
    }

    [Serializable, BlockInfo("확인용: Blueprint 넣기·빼기")]
    public sealed class CheckToggleBlueprint : GameAction
    {
        protected override void Run()
        {
            var jokers = CheckRules.Run(Game).jokers;
            var blueprint = jokers.FirstOrDefault(j => j.joker == CheckGimmickIds.Blueprint);
            if (blueprint != null) jokers.Remove(blueprint);
            else jokers.Insert(0, new CheckJoker { id = Game.NewId("checkJ"), joker = CheckGimmickIds.Blueprint });
        }
    }

    [Serializable, BlockInfo("확인용: 유물 레벨")]
    public sealed class CheckSetRelic : GameAction
    {
        public string relic;
        public int level;
        protected override void Run()
        {
            var relics = CheckRules.Run(Game).relics;
            if (level <= 0) relics.Remove(relic);
            else relics[relic] = level;
        }
    }

    [Serializable, BlockInfo("확인용: 25% 확률 20번")]
    public sealed class CheckRollGems : GameAction
    {
        protected override void Run()
        {
            var run = CheckRules.Run(Game);
            run.gems = 0;
            for (int i = 0; i < 20; i++)
                if (Game.Chance(0.25, "checkGem", source: "checkPanel")) run.gems++;
        }
    }
}
