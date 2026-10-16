using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GBible
{
    // 패시브 효과: "가지고 있으면 발동"(유물·특성·패시브 스킬·장비 효과·축복·조커·참). 정의(DB › 패시브 효과)는 늘 등록되어 있고,
    // 가진 목록(플레이어 데이터의 ID 목록·객체 목록·딕셔너리)에 그 ID가 있는 쪽마다 트리거를 검사해 결과를 낸다.
    //   가진 목록 run › relics           → 가진 쪽 = run(판 하나에 하나)
    //   가진 목록 run › hero.perks       → 가진 쪽 = hero(사용자 자신 = 주인공)
    //   가진 목록 run › party[].traits   → 가진 쪽 = 그 목록을 가진 동료마다
    // 놓인 것마다(PerElement): 가진 쪽 = 목록의 요소 하나하나(조커·참·가방의 아티팩트). 같은 목록의 '놓인 것마다' 패시브 효과는
    //   패시브 효과 등록 순서가 아니라 **목록 순서(자리)대로** 발동한다 — 조커 칸 왼쪽부터, 보드 앞 칸부터.
    // 일치 필드(MatchField): 객체 목록에서 요소의 이 필드가 패시브 효과 ID면(글자 목록이면 그 안에 있으면) 가진 것. 비우면 요소 자신(글자)·요소의 id.
    //   원본에서 읽기(MatchVia): 일치 필드가 원본(DB › 인스턴스) ID를 담고, 그 원본의 이 필드(글자·글자 목록)에 패시브 효과 ID가 있으면 가진 것.
    //     조각 { id, card = "card.foxee" } + 원본 Card { passives = [reactHit, wild] } → 조각 하나에 패시브 효과 여럿, 판 데이터에는 원본 ID만.
    // 몇 개 가졌나(레벨): 목록이면 그 ID가 든 횟수(레벨 필드를 고르면 그 숫자의 합), 딕셔너리면 그 키의 숫자 → Game.Owned(ID, 가진 쪽).
    //   놓인 것마다면 요소 하나의 레벨(레벨 필드, 없으면 1). 레벨이 0 이하인 요소는 꺼진 것(발동·보정 없음). 결과의 값 틀 {level}이 이것이다.
    // 발동 여러 줄(Rules): 같은 패시브 효과가 다른 순간마다 다른 결과(손을 낼 때 +1 쌓기 · 버릴 때 −1 · 점수 때 내기). 줄마다 트리거 + 결과.
    // 가진 동안 스탯 보정(Modifiers): 가진 동안만 붙는 보정. 저장하지 않고 읽을 때 계산한다(얻고 잃는 순간을 따로 다루지 않는다).
    //   받는 쪽 = 가진 쪽(놓인 것마다면 그 요소), 또는 '보정 받을 대상' 경로(가진 목록의 플레이어 데이터 안, 예: hero). 줄마다 따로 정할 수도 있다.
    //   보정 조건(ModifierWhen): 참일 때만 붙는다(가진 쪽마다 읽을 때 검사, 사용자 = 가진 쪽) — 같은 줄에 혼자일 때·체력이 절반 이하일 때.
    //   여럿일 때(Stacking): 받는 쪽 하나에 가진 쪽이 여럿이면 레벨 합으로 한 번 / 가진 쪽마다 값을 더함 / 가장 높은 레벨 하나만.
    // 주기(Pulse, 초): 가진 동안 N초(게임 시간)마다 가진 쪽마다 결과를 낸다(레벨별 주기 가능). 트리거가 없어도 된다.
    // 침묵: 가진 쪽에 '패시브 효과를 끔'인 상태 효과가 걸려 있으면 그 가진 쪽의 패시브 효과는 발동·보정·레벨이 모두 없다(Ink·디버프된 조커).
    // 결과의 컨텍스트: User = 가진 쪽, Owner = 패시브 효과 ID, Power = 의도 '효과 배율'. 가진 쪽 필드를 고르면 이벤트·의도의 그 필드가 가진 쪽 id일 때만.
    // 상태 효과와 다른 점: 지속·중첩·만료가 없고, 가진 것은 게임의 데이터(유물 목록)에 그대로 있다.
    public sealed class PassiveDefinition
    {
        public string Id;
        public string DisplayName;
        public string OwnerKey;          // 가진 목록이 든 플레이어 데이터
        public string OwnerPath;         // 그 안의 목록·딕셔너리 경로(앞마디에 []를 쓰면 가진 쪽이 여럿)
        public string MatchField;        // 객체 목록 요소의 이 필드(글자·글자 목록)에 패시브 효과 ID가 있으면 가진 것(비우면 요소 자신·id)
        public string MatchVia;          // 일치 필드가 원본 ID일 때 그 원본에서 읽을 필드(비우면 일치 필드를 그대로 비교)
        public bool PerElement;          // 놓인 것마다: 가진 쪽 = 요소, 같은 목록끼리 목록 순서로 발동
        public string LevelField;        // 요소의 이 숫자 필드가 레벨(비우면 1개 = 1)
        public string ModifierTarget;    // 보정 받을 대상 경로(가진 목록의 플레이어 데이터 안, 비우면 가진 쪽, party[]면 그 모두)
        public Trigger ModifierWhen;     // 보정 조건(조건 충족만, 비우면 늘)
        public PassiveStacking Stacking = PassiveStacking.SumLevels;
        public double Pulse;             // 0보다 크면 가진 동안 이 초마다 결과
        public readonly List<double> PulseByLevel = new List<double>();
        public Trigger When;
        public string HolderField;       // 이벤트·의도의 이 필드가 가진 쪽 id일 때만
        public readonly List<Outcome> Outcomes = new List<Outcome>();
        public readonly List<PassiveRule> Rules = new List<PassiveRule>();
        public readonly List<PassiveModifier> Modifiers = new List<PassiveModifier>();

        // 같은 목록의 '놓인 것마다' 패시브 효과를 한 줄로 세우는 이름.
        internal string OrderGroup => PerElement ? "passive-list:" + OwnerKey + ":" + OwnerPath : null;

        // 그 레벨의 주기(초). 레벨별 주기가 있으면 그 칸(넘치면 마지막).
        public double PulseAt(int level) => PulseByLevel.Count > 0 ? PulseByLevel[Math.Max(0, Math.Min(level, PulseByLevel.Count) - 1)] : Pulse;
    }

    // 패시브 효과의 발동 한 줄(같은 패시브 효과의 다른 순간).
    public sealed class PassiveRule
    {
        public Trigger When;
        public readonly List<Outcome> Outcomes = new List<Outcome>();
    }

    public enum PassiveStacking
    {
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("레벨을 더해 한 번")]
#endif
        SumLevels,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("가진 쪽마다 값을 더함")]
#endif
        SumValues,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("가장 높은 레벨 하나만")]
#endif
        Highest,
    }

    // 패시브 효과의 '가진 동안 스탯 보정' 한 줄.
    [Serializable]
    [DataRow]   // 줄 추가 · 삭제는 개발자(값), 줄 안의 받는 쪽 조건 붙이기는 AI(구조)
    public sealed class PassiveModifier
    {
        [FieldLabel("스탯(숫자 필드 이름)")] public string attribute = "";
        [FieldLabel("칸")] public ModifierKind kind = ModifierKind.Flat;
        [FieldLabel("값(퍼센트는 0.2 = +20%)")] public double value;
        [FieldLabel("레벨마다(값 × 레벨)")] public bool perLevel;
        [FieldLabel("레벨별 값(1레벨부터, 비우면 위 값)")] public List<double> byLevel = new List<double>();
        [FieldLabel("받을 대상(비우면 패시브 효과의 보정 받을 대상)")] public string target = "";
        // 받는 쪽 조건: 받을 대상(여럿이면 하나하나) 가운데 참인 것만 이 줄을 받는다(사용자 = 받는 쪽). 시너지 "기사만 +20" · "모든 적 −20"
        // (받을 대상 = 모든 유닛 units[], 조건 = 사용자 필드 검사 tags에 knight · team이 적). 비우면 받을 대상 모두. 조건 붙이기는 AI(구조), 줄 추가는 개발자(값).
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.SerializeReference]
#endif
        [FieldLabel("받는 쪽 조건(참인 대상만 받음 — 비우면 모두)"), TriggerSlot(TriggerKinds.Condition)] public Trigger receiverWhen;

        // 그 레벨의 값: 레벨별 값이 있으면 그 칸(넘치면 마지막), 아니면 값(레벨마다면 × 레벨).
        public double At(int level)
        {
            if (byLevel != null && byLevel.Count > 0) return byLevel[Math.Max(0, Math.Min(level, byLevel.Count) - 1)];
            return perLevel ? value * level : value;
        }
    }

    // 기본 제공 의도: 효과 배율. 액티브 효과를 쓸 때·패시브 효과가 발동할 때 한 번 거친다. 기본 효과 Block(상태 효과 걸기 중첩·필드 바꾸기 더하기·
    // 스탯 보정 값)이 곱하고, 프로젝트 Block은 Use.Context.Power로 읽는다. "이 카드의 수치 2배"(Lumin)·"맨 오른쪽이면 2배"(Critical).
    [Serializable, Intent("효과 배율")]
    public sealed class PowerIntent
    {
        public string ownerId;          // 액티브 효과·패시브 효과 ID
        public string userId;           // 사용자·가진 쪽 id
        public double multiplier = 1;   // 곱할 배율
    }

    public sealed class PassiveSystem
    {
        readonly GameSession _session;
        readonly Dictionary<string, PassiveDefinition> _definitions = new Dictionary<string, PassiveDefinition>(StringComparer.Ordinal);
        // 스탯 이름 → 그 스탯을 보정하는 패시브 효과(읽을 때 빨리 건너뛰려고).
        readonly Dictionary<string, List<PassiveDefinition>> _byAttribute = new Dictionary<string, List<PassiveDefinition>>(StringComparer.Ordinal);
        // 지금 빌려 쓰는 중인 (패시브 효과, 원본) — 서로 빌리는 고리(Blueprint ↔ Brainstorm)를 끊는다.
        readonly HashSet<(string, object)> _borrowing = new HashSet<(string, object)>();
        // 빌려 쓰는 중인 빌리는 쪽(맨 위 = 지금) — 빌려 쓰는 결과 안에서 또 빌리면(Brainstorm → Blueprint → 오른쪽) 가진 쪽 필드를 처음 빌린 조각으로 본다.
        readonly Stack<object> _borrowers = new Stack<object>();
        // 보정 조건을 검사하는 중인 (패시브 효과, 가진 쪽) — 조건이 같은 스탯을 읽어 다시 들어오면 그 보정은 없는 것으로 본다.
        readonly HashSet<(string, object)> _checking = new HashSet<(string, object)>();
        double _lastPulse = -1;

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
            if (definition.When != null) Register(owner, definition, definition.When, definition.Outcomes);
            foreach (var rule in definition.Rules.Where(r => r?.When != null)) Register(owner, definition, rule.When, rule.Outcomes);
        }

        void Register(string owner, PassiveDefinition definition, Trigger when, List<Outcome> outcomes)
        {
            if (definition.PerElement)
                _session.Triggers.Register(owner, when, repeat: true, fire: moment => Fire(definition, outcomes, moment),
                    orderedHolders: moment => OrderedHoldersFor(definition, moment), orderGroup: definition.OrderGroup, item: definition.Id);
            else
                _session.Triggers.Register(owner, when, repeat: true, fire: moment => Fire(definition, outcomes, moment), holders: moment => HoldersFor(definition, moment), item: definition.Id);
        }

        // 몇 개 가졌나. holder를 비우면 모든 가진 쪽의 합. 놓인 것마다면 holder = 요소 하나(그 요소의 레벨). 침묵한 가진 쪽은 0.
        // 가진 목록이 플레이어마다 데이터면 holder를 비웠을 때 지금 플레이어의 몫만 센다(남의 것과 합치지 않는다).
        public int Owned(string id, object holder = null)
        {
            var definition = Find(id);
            if (definition == null) return 0;
            var total = 0;
            var onlyKey = holder == null && _session.Data.IsPerPlayer(definition.OwnerKey) ? _session.Data.Resolve(definition.OwnerKey) : null;
            foreach (var (parent, collection) in Collections(definition, onlyKey))
            {
                if (holder == null || ReferenceEquals(parent, holder) || SameId(parent, holder))
                {
                    if (!definition.PerElement && IsSilenced(parent)) continue;
                    total += CountIn(definition, collection);
                    continue;
                }
                if (!(collection is IList list)) continue;
                foreach (var element in list)
                    if (element != null && (ReferenceEquals(element, holder) || SameId(element, holder)) && Matches(definition, element) && !IsSilenced(element))
                        total += Math.Max(0, LevelOf(definition, element));
            }
            return total;
        }

        // 그 대상에 붙는 '가진 동안 스탯 보정'(attribute를 비우면 모든 스탯). 저장된 Modifier와 같은 모양으로 돌려준다(source = 패시브 효과 ID).
        public IEnumerable<Modifier> ModifiersFor(object target, string targetId, string attribute = null)
        {
            if (target == null && string.IsNullOrEmpty(targetId)) yield break;
            IEnumerable<PassiveDefinition> definitions;
            if (attribute == null) definitions = _byAttribute.Values.SelectMany(l => l).Distinct();
            else if (_byAttribute.TryGetValue(attribute, out var list)) definitions = list;
            else yield break;
            foreach (var definition in definitions.ToList())
                foreach (var m in definition.Modifiers)
                {
                    if (m == null || string.IsNullOrEmpty(m.attribute) || (attribute != null && m.attribute != attribute)) continue;
                    var value = ValueOnTarget(definition, m, target, targetId);
                    if (value == null) continue;
                    yield return new Modifier
                    {
                        id = "passive:" + definition.Id, target = targetId ?? "", attribute = m.attribute, kind = m.kind, value = value.Value, source = definition.Id,
                    };
                }
        }

        internal bool HasModifiers(string attribute) => attribute == null ? _byAttribute.Count > 0 : _byAttribute.ContainsKey(attribute);

        // 보정 한 줄이 target에 주는 값(받는 쪽이 아니거나 조건이 거짓이면 null).
        double? ValueOnTarget(PassiveDefinition definition, PassiveModifier m, object target, string targetId)
        {
            if (m.receiverWhen != null && !ReceiverHolds(definition, m, target ?? (string.IsNullOrEmpty(targetId) ? null : _session.Game.Find(targetId)))) return null;
            var path = string.IsNullOrEmpty(m.target) ? definition.ModifierTarget : m.target;
            // 받는 쪽 = 경로의 객체(정해져 있으면, 가진 쪽 전부가 그 객체에 보탠다 — party[]처럼 []면 그 모두가 받는다) 또는 가진 쪽 자신.
            var toPath = !string.IsNullOrEmpty(path);
            // 받을 대상 경로가 있으면 그 경로가 대상을 품은 가진 목록(플레이어마다 따로인 키면 그 플레이어 몫)의 가진 쪽만 보탠다.
            string inKey = null;
            if (toPath)
            {
                if (string.IsNullOrEmpty(definition.OwnerKey)) return null;
                inKey = _session.Data.KeysOfAllPlayers(definition.OwnerKey).FirstOrDefault(k => FieldPaths.Resolve(_session.Data.Get(k), path).Any(r => IsTarget(r, target, targetId)));
                if (inKey == null) return null;
            }
            var levels = new List<int>();
            foreach (var (holder, level) in ActiveHolders(definition, inKey))
            {
                if (!toPath && !IsTarget(holder, target, targetId)) continue;
                if (!ModifierConditionHolds(definition, holder)) continue;
                levels.Add(level);
            }
            if (levels.Count == 0) return null;
            switch (definition.Stacking)
            {
                case PassiveStacking.SumValues: return levels.Sum(m.At);
                case PassiveStacking.Highest: return m.At(levels.Max());
                default: return m.At(levels.Sum());
            }
        }

        // 받는 쪽 조건(보정 줄마다): 받는 쪽을 사용자로 검사한다(조건이 다시 이 스탯을 읽으면 그동안은 거짓 — 고리 막기).
        bool ReceiverHolds(PassiveDefinition definition, PassiveModifier m, object receiver)
        {
            if (receiver == null) return false;
            var key = (definition.Id + "#receiver", receiver);
            if (!_checking.Add(key)) return false;
            try { return _session.Triggers.Test(m.receiverWhen, new TriggerContext { Session = _session, Owner = definition.Id, User = receiver, Picked = new[] { receiver } }); }
            finally { _checking.Remove(key); }
        }

        bool ModifierConditionHolds(PassiveDefinition definition, object holder)
        {
            if (definition.ModifierWhen == null) return true;
            var key = (definition.Id, holder);
            if (!_checking.Add(key)) return false;
            try { return _session.Triggers.Test(definition.ModifierWhen, new TriggerContext { Session = _session, Owner = definition.Id, User = holder, Picked = new[] { holder } }); }
            finally { _checking.Remove(key); }
        }

        // 켜진 가진 쪽과 그 레벨(놓인 것마다면 요소마다, 아니면 목록을 품은 객체마다). 침묵·레벨 0 이하는 뺀다.
        IEnumerable<(object holder, int level)> ActiveHolders(PassiveDefinition definition, string onlyKey = null)
        {
            foreach (var (parent, collection) in Collections(definition, onlyKey).ToList())
            {
                if (!definition.PerElement)
                {
                    if (IsSilenced(parent)) continue;
                    var count = CountIn(definition, collection);
                    if (count > 0) yield return (parent, count);
                    continue;
                }
                if (!(collection is IList list)) continue;
                foreach (var element in list.Cast<object>().ToList())
                {
                    if (element == null || !Matches(definition, element) || IsSilenced(element)) continue;
                    var level = LevelOf(definition, element);
                    if (level > 0) yield return (element, level);
                }
            }
        }

        static bool IsTarget(object candidate, object target, string targetId)
        {
            if (candidate == null) return false;
            if (target != null && ReferenceEquals(candidate, target)) return true;
            return !string.IsNullOrEmpty(targetId) && GameApi.HasId(candidate.GetType()) && GameApi.IdOf(candidate) == targetId;
        }

        IEnumerable<object> HoldersFor(PassiveDefinition definition, TriggerContext moment)
        {
            foreach (var (holder, _) in ActiveHolders(definition))
                if (HolderFieldMatches(definition, holder, moment)) yield return holder;
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
                    if (element == null || !Matches(definition, element) || IsSilenced(element)) continue;
                    if (!string.IsNullOrEmpty(definition.LevelField) && LevelOf(definition, element) <= 0) continue;
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

        // 침묵: 가진 쪽에 '걸린 쪽의 패시브 효과를 끔' 상태 효과가 있나.
        bool IsSilenced(object holder) => holder != null && !(holder is string) && GameApi.HasId(holder.GetType()) && _session.Statuses.Silences(holder);

        void Fire(PassiveDefinition definition, List<Outcome> outcomes, TriggerContext moment)
        {
            if (outcomes == null || outcomes.Count == 0) return;
            var holder = moment.User;
            var context = new TriggerContext
            {
                Session = _session, Owner = definition.Id, Event = moment.Event, IsIntercept = moment.IsIntercept, IsBorrowed = moment.IsBorrowed,
                User = holder, Picked = holder == null ? Array.Empty<object>() : new[] { holder },
            };
            // 효과 배율을 고치는 패시브 효과 자신은 배율을 거치지 않는다(같은 의도를 다시 만들어 끝없이 돈다).
            context.Power = moment.Event is PowerIntent ? 1 : PowerOf(definition.Id, holder);
            // 가진 목록이 플레이어마다 데이터면 결과의 플레이어마다 키('gold')는 그 가진 쪽 플레이어의 몫이다(행동한 사람의 것이 아니라).
            var player = PlayerOf(definition, holder);
            using (player != 0 ? _session.ActingAs(player) : null)
                _session.Triggers.RunOutcomes(outcomes, context, "Passive");
        }

        // 가진 쪽이 든 플레이어마다 키의 플레이어 번호(가진 목록이 플레이어마다 데이터가 아니면 0).
        int PlayerOf(PassiveDefinition definition, object holder)
        {
            if (holder == null || !_session.Data.IsPerPlayer(definition.OwnerKey)) return 0;
            foreach (var key in _session.Data.KeysOfAllPlayers(definition.OwnerKey).ToList())
                foreach (var (parent, collection) in Collections(definition, key))
                    if (ReferenceEquals(parent, holder) || (collection is IList list && list.Cast<object>().Any(e => ReferenceEquals(e, holder))))
                        return PlayerDataStore.PlayerOfKey(key);
            return 0;
        }

        // 의도 '효과 배율'을 한 번 거친 배율.
        double PowerOf(string owner, object user)
        {
            var intent = _session.Game.Intercept(new PowerIntent
            {
                ownerId = owner ?? "", userId = user != null && !(user is string) && GameApi.HasId(user.GetType()) ? GameApi.IdOf(user) : user as string ?? "",
            });
            return intent.multiplier;
        }

        // 빌려 쓰기(복사·흉내): 지금 순간(이벤트·의도)으로 그 패시브 효과의 트리거를 원본(lender)을 가진 쪽으로 검사하고, 참이면 결과를 낸다.
        //   결과의 사용자 = 원본 조각(원본의 값을 읽는다), IsBorrowed = 참 — '필드 바꾸기'는 원본 자신의 필드를 바꾸지 않는다(쌓이는 값은 원본만).
        //   가진 쪽 필드는 빌리는 쪽(지금 순간의 사용자)으로 본다 — "이 조커의 차례" 이벤트에서 Blueprint가 오른쪽 조커를 빌린다.
        //     빌려 쓰는 결과 안에서 또 빌리면(Brainstorm → Blueprint → 오른쪽) 처음 빌린 조각(그 차례의 조각)으로 본다.
        //   발동이 여러 줄이면 맞는 줄마다. 이미 빌려 쓰는 중인 (패시브 효과, 원본)을 다시 빌리면 거짓(고리 끊기). 결과를 냈으면 참.
        //   꺼진 원본(침묵·레벨 0 이하)은 빌리지 못한다(거짓) — 직접 발동하지 않는 조각은 빌려도 발동하지 않는다.
        public bool Borrow(string id, object lender, TriggerContext moment)
        {
            var definition = Find(id);
            if (definition == null || lender == null || moment == null || IsSilenced(lender)) return false;
            // 레벨이 0 이하인 조각은 꺼져 있다(발동·보정 없음) — 빌려 쓰지도 못한다(침묵한 조각과 같다).
            if (LevelOf(definition, lender) <= 0) return false;
            var key = (definition.Id, lender);
            if (!_borrowing.Add(key)) return false;
            try
            {
                var context = new TriggerContext
                {
                    Session = _session, Owner = definition.Id, Event = moment.Event, IsIntercept = moment.IsIntercept, IsUse = moment.IsUse,
                    User = lender, Picked = new[] { lender }, IsBorrowed = true,
                };
                var borrower = moment.IsBorrowed && _borrowers.Count > 0 ? _borrowers.Peek() : moment.User ?? lender;
                if (!HolderFieldMatches(definition, borrower, context)) return false;
                var fired = false;
                _borrowers.Push(borrower);
                try
                {
                    foreach (var (when, outcomes) in RulesOf(definition))
                    {
                        if (!_session.Triggers.Test(when, context)) continue;
                        Fire(definition, outcomes, context);
                        fired = true;
                    }
                }
                finally { _borrowers.Pop(); }
                return fired;
            }
            finally { _borrowing.Remove(key); }
        }

        static IEnumerable<(Trigger when, List<Outcome> outcomes)> RulesOf(PassiveDefinition definition)
        {
            if (definition.When != null) yield return (definition.When, definition.Outcomes);
            foreach (var rule in definition.Rules)
                if (rule?.When != null) yield return (rule.When, rule.Outcomes);
        }

        // 그 조각을 가진 것으로 보는 패시브 효과들(그 패시브 효과의 가진 목록에 그 조각이 들어 있고 일치하는 것).
        public IEnumerable<PassiveDefinition> MatchingFor(object element)
        {
            if (element == null) yield break;
            foreach (var definition in _definitions.Values.ToList())
            {
                if (!Matches(definition, element)) continue;
                if (Collections(definition).Any(p => p.collection is IList list && list.Cast<object>().Any(e => ReferenceEquals(e, element)))) yield return definition;
            }
        }

        // ── 주기 ─────────────────────────────────────────────

        // 프레임 끝(행동 밖): 주기가 있는 패시브 효과 중 경계를 넘은 것이 있으면 행동 하나로 낸다.
        // 그 행동이 실패하면(결과의 비용이 모자람 등) 가진 쪽의 결과마다 저장점으로 감싸 한 번 더 낸다: 실패한 결과만 되돌리고(경고)
        // 다른 가진 쪽·패시브 효과의 주기는 돈다. 지난 구간은 실패해도 지난 것으로 둔다(다음 프레임에 다시 내지 않는다).
        internal void Tick()
        {
            if (_session.Data.InTransaction) return;
            var now = _session.Clock.Time;
            if (_lastPulse < 0 || now < _lastPulse)
            {
                _lastPulse = now;
                return;
            }
            if (now <= _lastPulse) return;
            var pulsing = _definitions.Values.Where(d => d.Pulse > 0 || d.PulseByLevel.Count > 0).Select(d => d.Id).ToList();
            var from = _lastPulse;
            _lastPulse = now;
            if (pulsing.Count == 0) return;
            if (!pulsing.Any(id => ActiveHolders(Find(id)).Any(h => Crosses(Find(id).PulseAt(h.level), from, now)))) return;
            try
            {
                _session.Do(new PassivePulseRun { passives = pulsing, from = from, to = now });
                return;
            }
            catch (Exception)
            {
            }
            try { _session.Do(new PassivePulseRun { passives = pulsing, from = from, to = now, isolate = true }); }
            catch (Exception e)
            {
                _session.Journal.Append(JournalKind.Error, "Passive", $"패시브 효과 주기: 결과를 하나씩 감싸 다시 냈는데도 실패했습니다 — {e.Message}");
            }
        }

        // 게임 시계를 되돌리거나 불러온 판의 시각으로 옮긴 뒤(세션이 부른다): 지난 경계를 새 시각으로 — 그 시각까지의 주기는 이미 낸 것으로 본다.
        // 그러지 않으면 앞으로 크게 옮긴 뒤 밀린 주기를 한꺼번에(최대 8번) 내고, 뒤로 돌린 뒤 첫 프레임의 경계를 건너뛴다.
        internal void ResetPulseClock() => _lastPulse = _session.Clock.Time;

        static bool Crosses(double interval, double from, double to) => interval > 0 && Math.Floor(to / interval) > Math.Floor(from / interval);

        // isolate: 가진 쪽의 결과를 회차마다 저장점으로 감싼다(실패한 회차는 그 결과만 되돌리고 그 가진 쪽의 남은 회차를 버린다).
        internal void PulseRun(List<string> ids, double from, double to, bool isolate = false)
        {
            foreach (var id in ids)
            {
                var definition = Find(id);
                if (definition == null) continue;
                // 주기는 가진 쪽 필드(이벤트 거르기)와 상관없이 켜진 가진 쪽마다, 목록 순서로.
                // 앞 결과(앞 가진 쪽·앞 회차)가 가진 쪽을 없애거나 껐으면(목록에서 뺌·침묵·레벨 0) 그 가진 쪽은 남은 회차를 내지 않는다(이벤트 발동과 같다).
                var holders = ActiveHolders(definition).ToList();
                var fired = false;
                foreach (var (holder, listed) in holders)
                {
                    var level = listed;
                    if (fired && !LevelNow(definition, holder, out level)) continue;
                    var interval = definition.PulseAt(Math.Max(1, level));
                    if (interval <= 0) continue;
                    var times = Math.Min(8, (int)(Math.Floor(to / interval) - Math.Floor(from / interval)));
                    for (int i = 0; i < times; i++)
                    {
                        if (i > 0 && !LevelNow(definition, holder, out _)) break;
                        var moment = new TriggerContext { Session = _session, Owner = definition.Id, User = holder };
                        fired = true;
                        if (!isolate) Fire(definition, definition.Outcomes, moment);
                        else if (!Guarded(definition, holder, () => Fire(definition, definition.Outcomes, moment))) break;
                    }
                }
            }
        }

        // 지금도 켜진 가진 쪽인가(그 레벨).
        bool LevelNow(PassiveDefinition definition, object holder, out int level)
        {
            foreach (var (active, now) in ActiveHolders(definition))
                if (ReferenceEquals(active, holder))
                {
                    level = now;
                    return true;
                }
            level = 0;
            return false;
        }

        // 저장점 안에서 돌린다. 실패하면 그것이 바꾼 것과 쏜 이벤트(연출·전송)만 되돌리고 경고를 남긴다. 돌았으면 참.
        bool Guarded(PassiveDefinition definition, object holder, Action run)
        {
            var point = _session.Data.Mark();
            var events = _session.Events.MarkEvents();
            try
            {
                run();
                return true;
            }
            catch (Exception e)
            {
                _session.Data.RollbackTo(point);
                _session.Events.RollbackEvents(events);
                var who = holder != null && !(holder is string) && GameApi.HasId(holder.GetType()) ? GameApi.IdOf(holder) : holder as string ?? "";
                _session.Journal.Append(JournalKind.Warning, "Passive", $"패시브 효과 '{definition.DisplayName ?? definition.Id}'({who})의 주기 결과가 실패해 그것이 바꾼 것만 되돌렸습니다 — {e.Message}");
                return false;
            }
        }

        // ── 가진 목록 읽기 ───────────────────────────────────

        // 가진 쪽(목록을 품은 객체)과 그 목록. 가진 목록이 플레이어마다 따로인 키(army)면 모든 플레이어 몫(army:1 · army:2 …)을 훑는다(onlyKey면 그 키만).
        IEnumerable<(object parent, object collection)> Collections(PassiveDefinition definition, string onlyKey = null)
        {
            if (string.IsNullOrEmpty(definition.OwnerKey)) yield break;
            FieldPaths.SplitLast(definition.OwnerPath ?? "", out var parentPath, out var last);
            foreach (var key in _session.Data.KeysOfAllPlayers(definition.OwnerKey).ToList())
            {
                if (onlyKey != null && key != onlyKey) continue;
                var root = _session.Data.Get(key);
                if (string.IsNullOrEmpty(last))
                {
                    yield return (root, root);
                    continue;
                }
                foreach (var parent in FieldPaths.Resolve(root, parentPath).ToList())
                    if (parent != null) yield return (parent, FieldPaths.Resolve(parent, last).FirstOrDefault());
            }
        }

        // 요소가 이 패시브 효과인가: 글자면 그 글자, 일치 필드가 있으면 그 필드(글자·글자 목록, 원본에서 읽기면 그 원본의 필드), 아니면 요소의 id.
        bool Matches(PassiveDefinition definition, object element)
        {
            switch (element)
            {
                case null: return false;
                case string s: return s == definition.Id;
            }
            if (string.IsNullOrEmpty(definition.MatchField))
                return GameApi.HasId(element.GetType()) && GameApi.IdOf(element) == definition.Id;
            var value = FieldAccess.Read(element, definition.MatchField);
            if (!string.IsNullOrEmpty(definition.MatchVia))
            {
                var source = value is string sourceId && _session.Instances != null ? _session.Instances(sourceId) : null;
                value = source == null ? null : FieldAccess.Read(source, definition.MatchVia);
            }
            return Contains(value, definition.Id);
        }

        static bool Contains(object value, string id)
        {
            switch (value)
            {
                case null: return false;
                case string text: return text == id;
                case IEnumerable items:
                    foreach (var item in items)
                        if (item is string s && s == id) return true;
                    return false;
                default: return Convert.ToString(value, CultureInfo.InvariantCulture) == id;
            }
        }

        static int LevelOf(PassiveDefinition definition, object element)
        {
            if (string.IsNullOrEmpty(definition.LevelField) || element is string) return 1;
            var value = FieldAccess.Read(element, definition.LevelField);
            return value is IConvertible && !(value is string) ? GameNumbers.RoundToInt(Convert.ToDouble(value, CultureInfo.InvariantCulture)) : 1;
        }

        int CountIn(PassiveDefinition definition, object collection)
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
                        // 0 이하 = 꺼짐(목록 요소의 레벨과 같다) — 여러 가진 쪽의 합을 깎지 않는다.
                        return entry.Value is IConvertible && !(entry.Value is string) ? Math.Max(0, GameNumbers.RoundToInt(Convert.ToDouble(entry.Value, CultureInfo.InvariantCulture))) : 1;
                    }
                    return 0;
                case IEnumerable list:
                    var count = 0;
                    foreach (var item in list)
                        if (Matches(definition, item) && !IsSilenced(item)) count += Math.Max(0, LevelOf(definition, item));
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

    // 주기가 된 패시브 효과의 결과를 내는 행동(게임 시간 from → to 사이의 경계마다).
    [Serializable, SystemAction]
    public sealed class PassivePulseRun : GameAction, ITimeRuleAction
    {
        public List<string> passives = new List<string>();
        public double from;
        public double to;
        // 결과를 회차마다 저장점으로 감싼다(묶음이 한 번 실패한 뒤 다시 낼 때 — 실패한 결과만 되돌린다).
        public bool isolate;

        public override ActionVisibility Visibility => ActionVisibility.Developer;
        public override string Describe() => isolate ? "패시브 효과 주기(실패 감싸기)" : "패시브 효과 주기";

        protected override void Run() => Game.Session.Passives.PulseRun(passives, from, to, isolate);
    }

    // 결과 '빌려 쓰기': 대상 Block이 고른 조각(옆 칸·맨 왼쪽 …)의 패시브 효과를 지금 순간으로 빌려 쓴다(Blueprint·Brainstorm).
    //   Blueprint = 패시브 효과(놓인 것마다, 조커 칸) + 이 결과(대상: 자리로 고르기 — 오른쪽). 대상의 패시브 효과는 '일치 필드'로 찾는다.
    [Serializable, Element(Elements.Passive)]
    [BlockInfo("빌려 쓰기", Category = "결과", Description = "대상 Block이 고른 조각(옆 칸·맨 왼쪽 …)의 패시브 효과를 지금 순간으로 한 번 더 냅니다(복사·흉내). 결과의 사용자는 원본 조각이고, 원본 자신의 쌓이는 값('필드 바꾸기')은 바꾸지 않습니다. 서로 빌리는 고리는 끊습니다.")]
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
