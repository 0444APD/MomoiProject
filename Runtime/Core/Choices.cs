using System;
using System.Collections.Generic;
using System.Linq;

namespace GBible
{
    // 선택 대기: 행동 안에서 플레이어에게 고르기를 요구하고, 답이 오면 이어서 할 행동을 실행한다.
    //   Game.Ask("버릴 카드", hand, new DiscardChosen());          // DiscardChosen의 [Chosen] 필드에 고른 것이 들어간다
    //   화면: Game.Choice(지금 기다리는 선택)를 보여 주고, 플레이어가 고르면 Game.Answer(choice.id, 고른 것들).
    // 행동은 멈추지 않는다: Ask 다음 줄은 바로 실행된다. 고른 뒤에 할 일은 모두 이어서 할 행동에 쓴다.
    // 이어서 할 행동은 예약처럼 맡긴다(대상 필드는 id만 저장, 실행할 때 다시 찾는다). 답하기(ChoiceAnswer)는 플레이어 행동이라 타임라인에 남고 Undo된다.
    // 기다리는 선택은 플레이어 데이터라 Undo·저장·멀티 그대로다. 여럿이면 먼저 요구한 것이 Game.Choice(맨 앞)다.
    // DB 원본(아직 판에 없는 카드·유물)에서 고르는 것은 결과 '가중치 표에서 고르기'(표에서 뽑아 보이고, 고른 원본 id를 목록에 넣는다).
    // 후보가 없으면 기다리지 않고 바로 이어서 할 행동을 실행한다(고른 것 없음).
    // 기다리는 동안 다른 입력을 막는 것은 화면의 일이다(Game.IsWaiting).

    // 이어서 할 행동의 이 필드에 고른 것이 들어간다. 형식: string(id 하나) · id가 있는 클래스(객체 하나) · 그 리스트·배열(여럿).
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ChosenAttribute : Attribute
    {
    }

    [Serializable]
    public sealed class ChoiceCandidate
    {
        public string id;
        public string label;

        public ChoiceCandidate()
        {
        }

        public ChoiceCandidate(string id, string label = null)
        {
            this.id = id;
            this.label = label ?? id;
        }
    }

    [Serializable]
    public sealed class PendingChoice
    {
        public string id;
        public string prompt;
        public string player;     // 답할 플레이어(멀티플레이용, 비우면 누구나)
        public string source;
        public List<ChoiceCandidate> options = new List<ChoiceCandidate>();
        public int min = 1;
        public int max = 1;
        public GameAction then;
        // '가중치 표에서 고르기'로 만든 선택이면 그 표·후보 수(다시 뽑기 Game.RerollChoice가 쓴다). 비어 있으면 다시 뽑을 수 없다.
        public string table = "";
        public int count;
        public bool unique;
        // 처음 요구한 최소·최대(다시 뽑아 후보 수가 바뀌어도 이 값을 기준으로 맞춘다).
        public int wantMin;
        public int wantMax;
    }

    [Serializable]
    public sealed class ChoiceList
    {
        public List<PendingChoice> items = new List<PendingChoice>();
    }

    // 선택을 요구한 순간 쏜다(화면이 받아서 고르기 창을 연다).
    [Serializable, GameEvent("선택 요구")]
    public sealed class ChoiceRequested
    {
        public string choice;
        public string prompt;
        public string player;
    }

    public sealed class ChoiceSystem
    {
        public const string Key = "$choices";

        readonly GameSession _session;

        internal ChoiceSystem(GameSession session)
        {
            _session = session;
        }

        public string Ask(string prompt, IEnumerable<ChoiceCandidate> options, GameAction then, int min = 1, int max = 1, string source = null, string player = null)
        {
            if (then == null) throw new ArgumentNullException(nameof(then));
            if (!ActionReferences.HasChosen(then.GetType()))
                throw new InvalidOperationException($"[MomoiProject] 이어서 할 행동 '{then.GetType().Name}'에 고른 것을 받을 [Chosen] 필드가 없습니다.");
            var list = (options ?? Enumerable.Empty<ChoiceCandidate>()).Where(o => o != null && !string.IsNullOrEmpty(o.id)).GroupBy(o => o.id).Select(g => g.First()).ToList();
            if (list.Count == 0)
            {
                ActionReferences.SetChosen(then, Array.Empty<string>());
                _session.Do(then);
                return null;
            }
            min = Math.Max(0, Math.Min(min, list.Count));
            max = Math.Max(Math.Max(min, 1), Math.Min(max <= 0 ? list.Count : max, list.Count));
            ActionReferences.Detach(then);
            var choice = new PendingChoice
            {
                id = _session.Game.NewId("$choice"), prompt = prompt ?? "", player = player ?? "", source = source ?? "",
                options = list, min = min, max = max, then = then,
            };
            if (!_session.Data.IsDefined(Key)) _session.Data.Define(Key, DataZone.Session, new ChoiceList(), "선택 대기");
            _session.Data.Modify<ChoiceList>(Key, l => l.items.Add(choice));
            _session.Events.Publish(new ChoiceRequested { choice = choice.id, prompt = choice.prompt, player = choice.player });
            return choice.id;
        }

        // 다시 뽑기: '가중치 표에서 고르기'로 만든 선택의 후보를 같은 표에서 새로 뽑는다(행동 안에서). 비용·횟수는 부르는 행동이 정한다.
        // 화면은 ChoiceRequested를 다시 받아 후보를 새로 그린다. 표로 만든 선택이 아니면 거짓.
        public bool Reroll(string id)
        {
            var choice = Items().FirstOrDefault(c => c.id == id);
            if (choice == null || string.IsNullOrEmpty(choice.table)) return false;
            var options = _session.Game.Roll(choice.table, Math.Max(1, choice.count), choice.unique)
                .Where(o => !string.IsNullOrEmpty(o)).Distinct().Select(o => new ChoiceCandidate(o, Texts.NameOf(o))).ToList();
            if (options.Count == 0) return false;
            _session.Data.Modify<ChoiceList>(Key, _ =>
            {
                choice.options = options;
                choice.min = Math.Min(choice.wantMin, options.Count);
                choice.max = Math.Max(Math.Max(choice.min, 1), Math.Min(choice.wantMax <= 0 ? options.Count : choice.wantMax, options.Count));
            });
            _session.Events.Publish(new ChoiceRequested { choice = choice.id, prompt = choice.prompt, player = choice.player });
            return true;
        }

        // 표로 만든 선택이라고 적어 둔다(다시 뽑기용).
        public void MarkTable(string id, string table, int count, bool unique, int wantMin = -1, int wantMax = -1)
        {
            var choice = Items().FirstOrDefault(c => c.id == id);
            if (choice == null) return;
            _session.Data.Modify<ChoiceList>(Key, _ =>
            {
                choice.table = table ?? "";
                choice.count = count;
                choice.unique = unique;
                choice.wantMin = wantMin >= 0 ? wantMin : choice.min;
                choice.wantMax = wantMax >= 0 ? wantMax : choice.max;
            });
        }

        public PendingChoice Current => Items().FirstOrDefault();
        public IReadOnlyList<PendingChoice> All => Items().ToList();

        public bool Cancel(string id)
        {
            if (!Items().Any(c => c.id == id)) return false;
            _session.Data.Modify<ChoiceList>(Key, l => l.items.RemoveAll(c => c.id == id));
            return true;
        }

        internal PendingChoice Take(string id)
        {
            var found = Items().FirstOrDefault(c => c.id == id);
            if (found != null) _session.Data.Modify<ChoiceList>(Key, l => l.items.Remove(found));
            return found;
        }

        // 등록돼 있거나 불러온 저장의 값이 기다리고 있으면(지금 등록한다) 그 목록.
        IEnumerable<PendingChoice> Items()
        {
            if (!_session.Data.IsDefined(Key))
            {
                if (!_session.Data.IsPending(Key)) return Enumerable.Empty<PendingChoice>();
                _session.Data.Define(Key, DataZone.Session, new ChoiceList(), "선택 대기");
            }
            return _session.Data.Get<ChoiceList>(Key)?.items ?? Enumerable.Empty<PendingChoice>();
        }
    }

    // 답하기: 기다리는 선택을 꺼내 고른 것을 이어서 할 행동의 [Chosen] 필드에 넣고 실행한다(하나의 행동, 틀리면 아무것도 바뀌지 않음).
    [Serializable]
    public sealed class ChoiceAnswer : GameAction
    {
        public string choice;
        public List<string> picked = new List<string>();

        public override string Describe() => picked.Count == 0 ? "선택: 고르지 않음" : $"선택: {string.Join(", ", picked)}";

        protected override void Run()
        {
            var pending = Game.Session.Choices.Take(choice)
                ?? throw new GameFailure(FailureKeys.ChoiceGone, null, "이미 답했거나 취소된 선택입니다.");
            var ids = (picked ?? new List<string>()).Where(id => !string.IsNullOrEmpty(id)).ToList();
            if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
                throw new GameFailure(FailureKeys.ChosenTwice, null, "같은 것을 두 번 골랐습니다.");
            var unknown = ids.FirstOrDefault(id => pending.options.All(o => o.id != id));
            if (unknown != null) throw new GameFailure(FailureKeys.NotACandidate, TextArgs.From(("item", unknown)), "{item}은(는) 고를 수 있는 후보가 아닙니다.");
            if (ids.Count < pending.min || ids.Count > pending.max)
                throw pending.min == pending.max
                    ? new GameFailure(FailureKeys.ChooseExactly, TextArgs.From(("count", pending.min), ("picked", ids.Count)), "{count}개를 골라야 합니다(고른 것 {picked}개).")
                    : new GameFailure(FailureKeys.ChooseBetween, TextArgs.From(("min", pending.min), ("max", pending.max), ("picked", ids.Count)), "{min}~{max}개를 골라야 합니다(고른 것 {picked}개).");
            ActionReferences.SetChosen(pending.then, ids);
            Game.Do(pending.then);
        }
    }

    // 결과 '선택 요구': 후보(대상 Block)에서 플레이어가 고르게 하고, 고른 것마다 효과를 준 뒤 '고른 뒤 결과'를 실행한다.
    [Serializable]
    [BlockInfo("선택 요구", Category = "결과", Description = "후보 중에서 플레이어가 고르게 합니다(카드 1장 버리기·보상 3개 중 1개 …). 고르면 고른 것마다 효과를 주고, 이어서 '고른 뒤 결과'를 실행합니다.")]
    public sealed class ChoiceOutcome : Outcome
    {
        [FieldLabel("안내 글")] public string prompt = "";
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.SerializeReference]
#endif
        [FieldLabel("후보")] public List<TargetSelector> candidates = new List<TargetSelector>();
        [FieldLabel("최소 개수")] public int min = 1;
        [FieldLabel("최대 개수")] public int max = 1;
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.SerializeReference]
#endif
        [FieldLabel("고른 것에 효과")] public List<Effect> effects = new List<Effect>();
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.SerializeReference]
#endif
        [FieldLabel("고른 뒤 결과")] public List<Outcome> then = new List<Outcome>();

        public override OutcomeKinds Kind => OutcomeKinds.Targeted;

        public override void Run(TriggerContext context)
        {
            var use = new AbilityUse { Game = context.Game, User = context.User, Picked = context.Picked ?? Array.Empty<object>(), Context = context };
            IEnumerable<object> pool = use.Picked;
            foreach (var selector in candidates.Where(t => t != null)) pool = selector.Run(use, pool);
            var options = pool.Where(o => o != null && GameApi.HasId(o.GetType())).Select(o => GameApi.IdOf(o)).Select(id => new ChoiceCandidate(id, Texts.NameOf(id))).ToList();
            var user = context.User != null && GameApi.HasId(context.User.GetType()) ? GameApi.IdOf(context.User) : "";
            var next = new ChoiceEffectsRun
            {
                owner = context.Owner ?? "", user = user,
                effects = (List<Effect>)DataModel.Clone(effects.Where(e => e != null).ToList(), null),
                then = (List<Outcome>)DataModel.Clone(then.Where(o => o != null).ToList(), null),
            };
            context.Game.Session.Choices.Ask(prompt, options, next, min, max, source: context.Owner);
        }

        public override string Describe(INameResolver names)
        {
            var who = candidates.Count == 0 ? "고른 대상" : string.Join(" → ", candidates.Where(t => t != null).Select(t => t.Describe()));
            var count = min == max ? $"{min}개" : $"{min}~{max}개";
            var what = effects.Count == 0 ? "" : ": " + string.Join(", ", effects.Where(e => e != null).Select(e => e.Describe()));
            return $"{who}에서 {count} 고르기{what}";
        }

        public override void Validate(IValidationContext context)
        {
            if (max < 1 || min > max) context.Error("선택 요구: 개수는 최소 ≤ 최대, 최대 1 이상이어야 합니다.");
            if (min < 0) context.Error("선택 요구: 최소 개수는 0 이상이어야 합니다.");
            if (effects.Count == 0 && then.Count == 0) context.Warning("선택 요구: 고른 뒤 할 일(효과·결과)이 없습니다.");
            if (effects.Contains(null) || candidates.Contains(null) || then.Contains(null)) context.Error("선택 요구: 비어 있는 Block이 있습니다(스크립트가 지워졌을 수 있음).");
            foreach (var selector in candidates) selector?.Validate(context);
            foreach (var effect in effects) effect?.Validate(context);
            foreach (var outcome in then) outcome?.Validate(context);
        }
    }

    // '선택 요구'·'가중치 표에서 고르기'의 이어서 할 행동: 고른 것마다 효과, 고른 원본 id를 플레이어 데이터에, 이어서 '고른 뒤 결과'(사용자·Owner는 요구한 순간 그대로).
    [Serializable]
    public sealed class ChoiceEffectsRun : GameAction
    {
        public string owner;
        public string user;
        [Chosen] public List<string> picked = new List<string>();
        public List<Effect> effects = new List<Effect>();
        public List<Outcome> then = new List<Outcome>();
        // 참이면 후보가 원본 id(판 데이터 객체가 아님): 고른 것을 id 글자 그대로 '고른 뒤 결과'의 Picked로 넘기고, key가 있으면 그 목록에 넣는다.
        public bool ids;
        public string key = "";
        public string path = "";

        public override ActionVisibility Visibility => ActionVisibility.Developer;
        public override string Describe() => ids ? "고른 것 넣기" : "고른 것에 효과";

        protected override void Run()
        {
            var session = Game.Session;
            var holder = string.IsNullOrEmpty(user) ? null : Game.Find(user);
            var picks = ids ? picked.Where(id => !string.IsNullOrEmpty(id)).Cast<object>().ToList()
                            : picked.Select(id => Game.Find(id)).Where(o => o != null).ToList();
            var context = new TriggerContext { Session = session, Owner = owner, User = holder, Picked = picks };
            var use = new AbilityUse { Game = Game, User = holder, Picked = picks, Context = context };
            foreach (var target in picks)
                foreach (var effect in effects.Where(e => e != null))
                    if (effect.TargetType.IsInstanceOfType(target)) effect.Run(use, target);
            if (ids && !string.IsNullOrEmpty(key) && picks.Count > 0)
                GrantIds.Into(session, key, path, picks.Cast<string>().ToList());
            if (then.Count > 0) session.Triggers.RunOutcomes(then, context, "Choice");
        }
    }
}
