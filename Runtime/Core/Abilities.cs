using System;
using System.Collections.Generic;
using System.Linq;

namespace GBible
{
    // 능력: 스킬·사용 아이템처럼 "쓰는 순간" 발동하는 것. 다른 기능과 같은 트리거·결과 두 칸으로 이루어진다(Triggers.cs).
    //   트리거 = 사용(고정) + 원하면 AND로 사용 조건(조건 충족). 결과 = 대상에게 효과·플레이어 데이터에 주기·코딩한 결과.
    // 기획자는 DB › 능력에서 Block을 조립하고 수치를 정하고, 프로그래머는 새 동작이 필요할 때만 Block 클래스를 하나 만든다.
    //   Game.Use("ability.splash", 사용자, 고른 대상들)
    //   → 트리거 검사(사용 조건이 거짓이면 실패) → 결과를 차례로(대상에게 효과: 대상 Block → 대상마다 효과 Block)
    //   → AbilityUsed 이벤트(로그·도전과제가 반응). 전체가 행동 하나라 Undo 한 번에 되돌아간다.

    // 무엇을: 대상 하나에 하는 일.
    [Serializable]
    public abstract class Effect
    {
        [NonSerialized] AbilityUse _use;

        // 지금 실행 중인 능력 사용(사용자·고른 대상·능력 ID).
        protected AbilityUse Use => _use;
        protected GameApi Game => _use?.Game;

        internal void Run(AbilityUse use, object target)
        {
            _use = use;
            try { Apply(target); }
            finally { _use = null; }
        }

        protected abstract void Apply(object target);

        // 받을 수 있는 대상 형식(다르면 건너뛴다).
        public virtual Type TargetType => typeof(object);

        public virtual string Describe() => BlockNames.Of(GetType());

        // DB 검증(저장할 때·검증 창)에서 부른다. 값이 맞지 않으면 context.Error·Warning(예: 음수 피해).
        public virtual void Validate(IValidationContext context)
        {
        }
    }

    // 대상을 T로 받는 효과. 받은 객체의 필드를 그냥 고친다.
    //   [Serializable, BlockInfo("피해")] public class Damage : Effect<Unit> { public int amount = 10; protected override void Apply(Unit t) => t.hp -= amount; }
    [Serializable]
    public abstract class Effect<T> : Effect where T : class
    {
        public override Type TargetType => typeof(T);

        protected sealed override void Apply(object target)
        {
            if (target is T typed) Apply(typed);
        }

        protected abstract void Apply(T target);

        // 사용자를 형식으로 받기(없거나 형식이 다르면 null).
        protected TUser User<TUser>() where TUser : class => Use?.User as TUser;
    }

    // 누구에게: 앞 단계의 대상들을 받아 다음 대상들을 낸다.
    [Serializable]
    public abstract class TargetSelector
    {
        [NonSerialized] AbilityUse _use;

        protected AbilityUse Use => _use;
        protected GameApi Game => _use?.Game;

        internal IEnumerable<object> Run(AbilityUse use, IEnumerable<object> targets)
        {
            _use = use;
            try { return Select(targets).ToList(); }
            finally { _use = null; }
        }

        protected abstract IEnumerable<object> Select(IEnumerable<object> targets);

        public virtual string Describe() => BlockNames.Of(GetType());

        // DB 검증에서 부른다(값 검사).
        public virtual void Validate(IValidationContext context)
        {
        }
    }

    //   [Serializable, BlockInfo("주변 n칸 추가")] public class AddAround : TargetSelector<Unit> { … Select(IEnumerable<Unit> targets, Unit user) … }
    [Serializable]
    public abstract class TargetSelector<T> : TargetSelector where T : class
    {
        protected sealed override IEnumerable<object> Select(IEnumerable<object> targets) =>
            Select(targets.OfType<T>(), Use?.User as T).Cast<object>();

        protected abstract IEnumerable<T> Select(IEnumerable<T> targets, T user);
    }

    // 기본 대상: 사용자 자신(나에게 부여).
    [Serializable]
    [BlockInfo("사용자 자신", Category = "대상", Description = "능력을 쓴 개체 자신을 대상으로 합니다(고른 대상은 버립니다).")]
    public sealed class SelfTarget : TargetSelector
    {
        protected override IEnumerable<object> Select(IEnumerable<object> targets) =>
            Use?.User == null ? Enumerable.Empty<object>() : new[] { Use.User };
    }

    // 효과를 한 번 실행하는 동안의 정보. 효과·대상 Block이 Use로 본다.
    public sealed class AbilityUse
    {
        public GameApi Game;
        // 능력에서 실행 중이면 그 능력(도전과제 등 다른 기능의 결과면 null).
        public AbilityDefinition Ability;
        public object User;
        public IReadOnlyList<object> Picked;
        // 트리거 순간(이벤트·사용자·대상).
        public TriggerContext Context;
    }

    public sealed class AbilityDefinition
    {
        public string Id;
        public string DisplayName;
        // 트리거(없으면 사용할 때 언제나). 사용 조건은 AND로 조건 충족을 붙인다.
        public Trigger When;
        public readonly List<Outcome> Outcomes = new List<Outcome>();
        // 예전 형태(대상·효과를 바로): 결과 앞에 "대상에게 효과" 하나로 실행된다.
        public readonly List<TargetSelector> Targets = new List<TargetSelector>();
        public readonly List<Effect> Effects = new List<Effect>();
    }

    // 세션의 능력 목록. 정의를 복사해 두므로(Block 인스턴스가 세션마다 따로) 여러 세션이 동시에 돌아도 섞이지 않는다.
    public sealed class AbilitySystem
    {
        readonly Dictionary<string, AbilityDefinition> _definitions = new Dictionary<string, AbilityDefinition>(StringComparer.Ordinal);

        public void Define(AbilityDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (string.IsNullOrEmpty(definition.Id)) throw new ArgumentException("[MomoiProject] 능력 ID가 비어 있습니다.");
            var copy = new AbilityDefinition
            {
                Id = definition.Id,
                DisplayName = definition.DisplayName ?? definition.Id,
                When = definition.When == null ? null : (Trigger)DataModel.Clone(definition.When, null),
            };
            // 코드로 쓴 결과가 연결되어 있으면([BindTo]) 그것이 이 능력의 결과다(에디터에서 고른 결과 대신).
            if (BoundOutcomes.Has(definition.Id)) copy.Outcomes.AddRange(BoundOutcomes.Create(definition.Id));
            else copy.Outcomes.AddRange(definition.Outcomes.Where(o => o != null).Select(o => (Outcome)DataModel.Clone(o, null)));
            // 연결된 코드가 결과 전체다: 예전 짧은 형식(대상·효과)도 쓰지 않는다.
            if (!BoundOutcomes.Has(definition.Id)) copy.Targets.AddRange(definition.Targets.Where(t => t != null).Select(t => (TargetSelector)DataModel.Clone(t, null)));
            if (!BoundOutcomes.Has(definition.Id)) copy.Effects.AddRange(definition.Effects.Where(e => e != null).Select(e => (Effect)DataModel.Clone(e, null)));
            _definitions[copy.Id] = copy;
        }

        public AbilityDefinition Get(string id) => id != null && _definitions.TryGetValue(id, out var definition) ? definition : null;

        public IEnumerable<AbilityDefinition> All => _definitions.Values;
    }

    // 능력 사용 = 행동 하나(데이터: 능력 ID·사용자 id·대상 id들 → 서버 전송·리플레이 가능).
    [Serializable]
    public sealed class UseAbility : GameAction, ISelfRunningAction
    {
        public string ability;
        public string user;
        public List<string> targets = new List<string>();

        public override string Describe() => $"능력 사용: {ability}" + (string.IsNullOrEmpty(user) ? "" : $" ({user})");

        void ISelfRunningAction.Run(GameApi game)
        {
            var definition = game.Session.Abilities.Get(ability) ?? throw new InvalidOperationException($"[MomoiProject] 능력 '{ability}'이(가) 없습니다.");
            var picked = (targets ?? new List<string>()).Select(game.Find).Where(t => t != null).ToList();
            var triggers = game.Session.Triggers;
            if (!triggers.CanUse(ability, definition.When, game.Find(user), picked, out var moment))
                throw new InvalidOperationException($"[MomoiProject] 능력 '{definition.DisplayName}'의 사용 조건을 만족하지 않습니다: {definition.When.Natural(game.Session)}");
            var final = new List<object>();
            if (definition.Targets.Count > 0 || definition.Effects.Count > 0)
            {
                var legacy = new TargetedOutcome();
                legacy.targets.AddRange(definition.Targets);
                legacy.effects.AddRange(definition.Effects);
                final.AddRange(legacy.Apply(moment, definition));
            }
            foreach (var outcome in definition.Outcomes)
            {
                if (outcome is TargetedOutcome targeted) final.AddRange(targeted.Apply(moment, definition));
                else if (outcome != null) triggers.RunOutcomes(new[] { outcome }, moment, "Abilities");
            }
            final = final.Distinct().ToList();
            game.Events.Publish(new AbilityUsed
            {
                Ability = ability,
                User = user,
                Targets = final.Select(t => GameApi.IdOf(t)).ToList(),
            });
        }
    }

    [GameEvent("능력 사용")]
    public sealed class AbilityUsed
    {
        public string Ability;
        public string User;
        public List<string> Targets = new List<string>();
        public int TargetCount => Targets.Count;
    }
}
