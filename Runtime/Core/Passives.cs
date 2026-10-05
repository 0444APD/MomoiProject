using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GBible
{
    // 패시브: "가지고 있으면 발동"(유물·특성·패시브 스킬·장비 효과·축복·조커·참). 정의(DB › 패시브)는 늘 등록되어 있고,
    // 가진 목록(플레이어 데이터의 ID 목록·객체 목록·딕셔너리)에 그 ID가 있는 쪽마다 트리거를 검사해 결과를 낸다.
    //   가진 목록 run › relics           → 가진 쪽 = run(판 하나에 하나)
    //   가진 목록 run › hero.perks       → 가진 쪽 = hero(사용자 자신 = 주인공)
    //   가진 목록 run › party[].traits   → 가진 쪽 = 그 목록을 가진 동료마다
    // 놓인 것마다(PerElement): 가진 쪽 = 목록의 요소 하나하나(조커·참·켜진 아티팩트). 같은 목록의 '놓인 것마다' 패시브는
    //   패시브 등록 순서가 아니라 **목록 순서(자리)대로** 발동한다 — 조커 칸 왼쪽부터, 보드 앞 칸부터.
    // 일치 필드(MatchField): 객체 목록에서 요소의 이 필드가 패시브 ID면 가진 것(run.jokers의 joker 필드). 비우면 요소 자신(글자)·요소의 id.
    // 몇 개 가졌나(레벨): 목록이면 그 ID가 든 횟수(레벨 필드를 고르면 그 숫자의 합), 딕셔너리면 그 키의 숫자 → Game.Owned(ID, 가진 쪽).
    //   놓인 것마다면 요소 하나의 레벨(레벨 필드, 없으면 1). 결과의 값 틀 {level}이 이것이다.
    // 가진 동안 능력치 보정(Modifiers): 가진 동안만 붙는 보정. 저장하지 않고 읽을 때 계산한다(얻고 잃는 순간을 따로 다루지 않는다).
    //   받는 쪽 = 가진 쪽(놓인 것마다면 그 요소), 또는 '보정 받을 대상' 경로(가진 목록의 플레이어 데이터 안, 예: hero).
    // 결과의 컨텍스트: User = 가진 쪽, Owner = 패시브 ID. 가진 쪽 필드를 고르면 이벤트·의도의 그 필드가 가진 쪽 id일 때만(가시 갑옷: 내가 맞을 때만).
    // 상태 효과와 다른 점: 지속·중첩·만료가 없고, 가진 것은 게임의 데이터(유물 목록)에 그대로 있다.
    public sealed class PassiveDefinition
    {
        public string Id;
        public string DisplayName;
        public string OwnerKey;          // 가진 목록이 든 플레이어 데이터
        public string OwnerPath;         // 그 안의 목록·딕셔너리 경로(앞마디에 []를 쓰면 가진 쪽이 여럿)
        public string MatchField;        // 객체 목록 요소의 이 필드가 패시브 ID면 가진 것(비우면 요소 자신·id)
        public bool PerElement;          // 놓인 것마다: 가진 쪽 = 요소, 같은 목록끼리 목록 순서로 발동
        public string LevelField;        // 요소의 이 숫자 필드가 레벨(비우면 1개 = 1)
        public string ModifierTarget;    // 보정 받을 대상 경로(가진 목록의 플레이어 데이터 안, 비우면 가진 쪽)
        public Trigger When;
        public string HolderField;       // 이벤트·의도의 이 필드가 가진 쪽 id일 때만
        public readonly List<Outcome> Outcomes = new List<Outcome>();
        public readonly List<PassiveModifier> Modifiers = new List<PassiveModifier>();

        // 같은 목록의 '놓인 것마다' 패시브를 한 줄로 세우는 이름.
        internal string OrderGroup => PerElement ? "passive-list:" + OwnerKey + ":" + OwnerPath : null;
    }

    // 패시브의 '가진 동안 능력치 보정' 한 줄.
    [Serializable]
    public sealed class PassiveModifier
    {
        [FieldLabel("능력치(숫자 필드 이름)")] public string attribute = "";
        [FieldLabel("칸")] public ModifierKind kind = ModifierKind.Flat;
        [FieldLabel("값(퍼센트는 0.2 = +20%)")] public double value;
        [FieldLabel("레벨마다(값 × 레벨)")] public bool perLevel;
        [FieldLabel("레벨별 값(1레벨부터, 비우면 위 값)")] public List<double> byLevel = new List<double>();

        // 그 레벨의 값: 레벨별 값이 있으면 그 칸(넘치면 마지막), 아니면 값(레벨마다면 × 레벨).
        public double At(int level)
        {
            if (byLevel != null && byLevel.Count > 0) return byLevel[Math.Max(0, Math.Min(level, byLevel.Count) - 1)];
            return perLevel ? value * level : value;
        }
    }

    public sealed class PassiveSystem
    {
        readonly GameSession _session;
        readonly Dictionary<string, PassiveDefinition> _definitions = new Dictionary<string, PassiveDefinition>(StringComparer.Ordinal);
        // 능력치 이름 → 그 능력치를 보정하는 패시브(읽을 때 빨리 건너뛰려고).
        readonly Dictionary<string, List<PassiveDefinition>> _byAttribute = new Dictionary<string, List<PassiveDefinition>>(StringComparer.Ordinal);
        // 지금 빌려 쓰는 중인 (패시브, 원본) — 서로 빌리는 고리(Blueprint ↔ Brainstorm)를 끊는다.
        readonly HashSet<(string, object)> _borrowing = new HashSet<(string, object)>();

        internal PassiveSystem(GameSession session)
        {
            _session = session;
        }

        public IEnumerable<PassiveDefinition> Definitions => _definitions.Values;
        public PassiveDefinition Find(string id) => id != null && _definitions.TryGetValue(id, out var d) ? d : null;

        public void Define(PassiveDefinition definition)
        {
            if (definition == null || string.IsNullOrEmpty(definition.Id)) return;
            if (_definitions.TryGetValue(definition.Id, out var previous))
                foreach (var list in _byAttribute.Values) list.Remove(previous);
            _definitions[definition.Id] = definition;
            foreach (var attribute in definition.Modifiers.Where(m => m != null && !string.IsNullOrEmpty(m.attribute)).Select(m => m.attribute).Distinct())
            {
                if (!_byAttribute.TryGetValue(attribute, out var list)) _byAttribute[attribute] = list = new List<PassiveDefinition>();
                list.Add(definition);
            }
            var owner = "passive:" + definition.Id;
            _session.Triggers.Unregister(owner);
            if (definition.When == null) return;
            if (definition.PerElement)
                _session.Triggers.Register(owner, definition.When, repeat: true, fire: moment => Fire(definition, moment),
                    orderedHolders: moment => OrderedHoldersFor(definition, moment), orderGroup: definition.OrderGroup);
            else
                _session.Triggers.Register(owner, definition.When, repeat: true, fire: moment => Fire(definition, moment), holders: moment => HoldersFor(definition, moment));
        }

        // 몇 개 가졌나. holder를 비우면 모든 가진 쪽의 합. 놓인 것마다면 holder = 요소 하나(그 요소의 레벨).
        public int Owned(string id, object holder = null)
        {
            var definition = Find(id);
            if (definition == null) return 0;
            var total = 0;
            foreach (var (parent, collection) in Collections(definition))
            {
                if (holder == null || ReferenceEquals(parent, holder) || SameId(parent, holder))
                {
                    total += CountIn(definition, collection);
                    continue;
                }
                if (!(collection is IList list)) continue;
                foreach (var element in list)
                    if (element != null && (ReferenceEquals(element, holder) || SameId(element, holder)) && Matches(definition, element))
                        total += LevelOf(definition, element);
            }
            return total;
        }

        // 그 대상에 붙는 '가진 동안 능력치 보정'(attribute를 비우면 모든 능력치). 저장된 Modifier와 같은 모양으로 돌려준다(source = 패시브 ID).
        public IEnumerable<Modifier> ModifiersFor(object target, string targetId, string attribute = null)
        {
            if (target == null && string.IsNullOrEmpty(targetId)) yield break;
            IEnumerable<PassiveDefinition> definitions;
            if (attribute == null) definitions = _byAttribute.Values.SelectMany(l => l).Distinct();
            else if (_byAttribute.TryGetValue(attribute, out var list)) definitions = list;
            else yield break;
            foreach (var definition in definitions.ToList())
            {
                var level = LevelOnTarget(definition, target, targetId);
                if (level <= 0) continue;
                foreach (var m in definition.Modifiers)
                {
                    if (m == null || string.IsNullOrEmpty(m.attribute) || (attribute != null && m.attribute != attribute)) continue;
                    yield return new Modifier
                    {
                        id = "passive:" + definition.Id, target = targetId ?? "", attribute = m.attribute, kind = m.kind,
                        value = m.At(level), source = definition.Id,
                    };
                }
            }
        }

        internal bool HasModifiers(string attribute) => attribute == null ? _byAttribute.Count > 0 : _byAttribute.ContainsKey(attribute);

        // 보정을 받는 쪽이 target이면 그 레벨(아니면 0).
        int LevelOnTarget(PassiveDefinition definition, object target, string targetId)
        {
            if (!string.IsNullOrEmpty(definition.ModifierTarget))
            {
                if (string.IsNullOrEmpty(definition.OwnerKey) || !_session.Data.IsDefined(definition.OwnerKey)) return 0;
                var receiver = FieldPaths.Resolve(_session.Data.Get(definition.OwnerKey), definition.ModifierTarget).FirstOrDefault();
                return IsTarget(receiver, target, targetId) ? Owned(definition.Id) : 0;
            }
            var level = 0;
            foreach (var (parent, collection) in Collections(definition))
            {
                if (!definition.PerElement)
                {
                    if (IsTarget(parent, target, targetId)) level += CountIn(definition, collection);
                    continue;
                }
                if (!(collection is IList list)) continue;
                foreach (var element in list)
                    if (element != null && IsTarget(element, target, targetId) && Matches(definition, element)) level += LevelOf(definition, element);
            }
            return level;
        }

        static bool IsTarget(object candidate, object target, string targetId)
        {
            if (candidate == null) return false;
            if (target != null && ReferenceEquals(candidate, target)) return true;
            return !string.IsNullOrEmpty(targetId) && GameApi.HasId(candidate.GetType()) && GameApi.IdOf(candidate) == targetId;
        }

        IEnumerable<object> HoldersFor(PassiveDefinition definition, TriggerContext moment)
        {
            foreach (var (parent, collection) in Collections(definition).ToList())
            {
                if (CountIn(definition, collection) <= 0) continue;
                if (!HolderFieldMatches(definition, parent, moment)) continue;
                yield return parent;
            }
        }

        // 놓인 것마다: 맞는 요소를 목록 순서대로(가진 쪽이 여럿이면 그 순서대로 이어서 센다).
        IEnumerable<(object holder, int order)> OrderedHoldersFor(PassiveDefinition definition, TriggerContext moment)
        {
            var order = 0;
            foreach (var (_, collection) in Collections(definition).ToList())
            {
                if (!(collection is IList list)) continue;
                foreach (var element in list.Cast<object>().ToList())
                {
                    var index = order++;
                    if (element == null || !Matches(definition, element)) continue;
                    if (!HolderFieldMatches(definition, element, moment)) continue;
                    yield return (element, index);
                }
            }
        }

        static bool HolderFieldMatches(PassiveDefinition definition, object holder, TriggerContext moment)
        {
            if (string.IsNullOrEmpty(definition.HolderField)) return true;
            var holderId = holder != null && GameApi.HasId(holder.GetType()) ? GameApi.IdOf(holder) : null;
            return holderId != null && FieldText(moment.Event, definition.HolderField) == holderId;
        }

        // 빌려 쓰기(복사·흉내): 지금 순간(이벤트·의도)으로 그 패시브의 트리거를 원본(lender)을 가진 쪽으로 검사하고, 참이면 결과를 낸다.
        //   결과의 사용자 = 원본 조각(원본의 값을 읽는다), IsBorrowed = 참 — '필드 바꾸기'는 원본 자신의 필드를 바꾸지 않는다(쌓이는 값은 원본만).
        //   이미 빌려 쓰는 중인 (패시브, 원본)을 다시 빌리면 거짓(고리 끊기). 결과를 냈으면 참.
        public bool Borrow(string id, object lender, TriggerContext moment)
        {
            var definition = Find(id);
            if (definition == null || lender == null || moment == null) return false;
            var key = (definition.Id, lender);
            if (!_borrowing.Add(key)) return false;
            try
            {
                var context = new TriggerContext
                {
                    Session = _session, Owner = definition.Id, Event = moment.Event, IsIntercept = moment.IsIntercept, IsUse = moment.IsUse,
                    User = lender, Picked = new[] { lender }, IsBorrowed = true,
                };
                if (definition.When != null && !_session.Triggers.Test(definition.When, context)) return false;
                if (!HolderFieldMatches(definition, lender, context)) return false;
                Fire(definition, context);
                return true;
            }
            finally { _borrowing.Remove(key); }
        }

        // 그 조각을 가진 것으로 보는 패시브들(그 패시브의 가진 목록에 그 조각이 들어 있고 일치하는 것).
        public IEnumerable<PassiveDefinition> MatchingFor(object element)
        {
            if (element == null) yield break;
            foreach (var definition in _definitions.Values.ToList())
            {
                if (!Matches(definition, element)) continue;
                if (Collections(definition).Any(p => p.collection is IList list && list.Cast<object>().Any(e => ReferenceEquals(e, element)))) yield return definition;
            }
        }

        void Fire(PassiveDefinition definition, TriggerContext moment)
        {
            if (definition.Outcomes.Count == 0) return;
            var holder = moment.User;
            var context = new TriggerContext
            {
                Session = _session, Owner = definition.Id, Event = moment.Event, IsIntercept = moment.IsIntercept, IsBorrowed = moment.IsBorrowed,
                User = holder, Picked = holder == null ? Array.Empty<object>() : new[] { holder },
            };
            _session.Triggers.RunOutcomes(definition.Outcomes, context, "Passive");
        }

        // 가진 쪽(목록을 품은 객체)과 그 목록.
        IEnumerable<(object parent, object collection)> Collections(PassiveDefinition definition)
        {
            if (string.IsNullOrEmpty(definition.OwnerKey) || !_session.Data.IsDefined(definition.OwnerKey)) yield break;
            var root = _session.Data.Get(definition.OwnerKey);
            FieldPaths.SplitLast(definition.OwnerPath ?? "", out var parentPath, out var last);
            if (string.IsNullOrEmpty(last))
            {
                yield return (root, root);
                yield break;
            }
            foreach (var parent in FieldPaths.Resolve(root, parentPath).ToList())
                if (parent != null) yield return (parent, FieldPaths.Resolve(parent, last).FirstOrDefault());
        }

        // 요소가 이 패시브인가: 글자면 그 글자, 일치 필드가 있으면 그 필드, 아니면 요소의 id.
        static bool Matches(PassiveDefinition definition, object element)
        {
            switch (element)
            {
                case null: return false;
                case string s: return s == definition.Id;
            }
            if (!string.IsNullOrEmpty(definition.MatchField))
                return Convert.ToString(FieldAccess.Read(element, definition.MatchField), CultureInfo.InvariantCulture) == definition.Id;
            return GameApi.HasId(element.GetType()) && GameApi.IdOf(element) == definition.Id;
        }

        static int LevelOf(PassiveDefinition definition, object element)
        {
            if (string.IsNullOrEmpty(definition.LevelField) || element is string) return 1;
            var value = FieldAccess.Read(element, definition.LevelField);
            return value is IConvertible && !(value is string) ? (int)Math.Round(Convert.ToDouble(value, CultureInfo.InvariantCulture)) : 1;
        }

        static int CountIn(PassiveDefinition definition, object collection)
        {
            var id = definition.Id;
            switch (collection)
            {
                case null: return 0;
                case string text: return text == id ? 1 : 0;
                case IDictionary map:
                    foreach (DictionaryEntry entry in map)
                    {
                        if (!string.Equals(Convert.ToString(entry.Key, CultureInfo.InvariantCulture), id, StringComparison.Ordinal)) continue;
                        return entry.Value is IConvertible && !(entry.Value is string) ? (int)Math.Round(Convert.ToDouble(entry.Value, CultureInfo.InvariantCulture)) : 1;
                    }
                    return 0;
                case IEnumerable list:
                    var count = 0;
                    foreach (var item in list)
                        if (Matches(definition, item)) count += LevelOf(definition, item);
                    return count;
                default: return 0;
            }
        }

        static bool SameId(object a, object b) =>
            a != null && b != null && GameApi.HasId(a.GetType()) && (b is string text ? GameApi.IdOf(a) == text : GameApi.HasId(b.GetType()) && GameApi.IdOf(a) == GameApi.IdOf(b));

        static string FieldText(object evt, string field)
        {
            if (evt == null || string.IsNullOrEmpty(field)) return null;
            var value = FieldAccess.Read(evt, field);
            return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }

    // 결과 '빌려 쓰기': 대상 Block이 고른 조각(옆 칸·맨 왼쪽 …)의 패시브를 지금 순간으로 빌려 쓴다(Blueprint·Brainstorm).
    //   Blueprint = 패시브(놓인 것마다, 조커 칸) + 이 결과(대상: 자리로 고르기 — 오른쪽). 대상의 패시브는 '일치 필드'로 찾는다.
    [Serializable]
    [BlockInfo("빌려 쓰기", Category = "결과", Description = "대상 Block이 고른 조각(옆 칸·맨 왼쪽 …)의 패시브를 지금 순간으로 한 번 더 냅니다(복사·흉내). 결과의 사용자는 원본 조각이고, 원본 자신의 쌓이는 값('필드 바꾸기')은 바꾸지 않습니다. 서로 빌리는 고리는 끊습니다.")]
    public sealed class BorrowOutcome : Outcome
    {
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.SerializeReference]
#endif
        [FieldLabel("빌릴 조각")] public List<TargetSelector> targets = new List<TargetSelector>();

        public override OutcomeKinds Kind => OutcomeKinds.Targeted;

        public override void Run(TriggerContext context)
        {
            if (context?.Session == null) return;
            var use = new AbilityUse { Game = context.Game, User = context.User, Picked = context.Picked ?? Array.Empty<object>(), Context = context };
            IEnumerable<object> chosen = use.Picked;
            foreach (var selector in targets.Where(t => t != null)) chosen = selector.Run(use, chosen);
            foreach (var lender in chosen.Where(l => l != null && !ReferenceEquals(l, context.User)).ToList())
                foreach (var definition in context.Session.Passives.MatchingFor(lender).ToList())
                    context.Session.Passives.Borrow(definition.Id, lender, context);
        }

        public override string Describe(INameResolver names) =>
            $"빌려 쓰기: {(targets.Count == 0 ? "고른 대상" : string.Join(" → ", targets.Where(t => t != null).Select(t => t.Describe())))}";

        public override void Validate(IValidationContext context)
        {
            if (targets.Count == 0) context.Error("빌려 쓰기: 빌릴 조각을 고르는 대상 Block을 넣으세요(예: 자리로 고르기 — 오른쪽).");
            if (targets.Contains(null)) context.Error("빌려 쓰기: 비어 있는 Block이 있습니다(스크립트가 지워졌을 수 있음).");
            foreach (var target in targets) target?.Validate(context);
        }
    }
}
