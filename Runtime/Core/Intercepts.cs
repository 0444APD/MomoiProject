using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace GBible
{
    // 가로채기(Intercept): 일이 일어나기 직전에 그 내용(의도, Intent)을 고치거나 막는 단계.
    // 이벤트는 일어난 뒤에 알리고, 가로채기는 일어나기 전에 바꾼다.
    //
    // 중간층 규칙 함수가 의도를 만들어 통과시키고, 돌아온 값으로 일을 한다:
    //   var hit = Game.Intercept(new DamageIntent { source = s, target = t, amount = 10 });
    //   if (hit.cancelled) return;  target.hp -= hit.amount;
    // 가로채는 쪽은 트리거의 '가로채기' 잎(의도 형식 + 조건 + 순서)으로 등록하고, 결과 '의도 고치기'나 [BindTo] 코드로 의도를 고친다:
    //   유물 "가시 갑옷" — 가로채기 DamageIntent → 의도 고치기 amount 더하기 -5
    //   [BindTo("undying")] static void Undying(DeathIntent i, TriggerContext c) { i.cancelled = true; … }
    // 같은 의도에 여럿이 걸리면 순서(order)가 작은 것부터, 같으면 등록 순서. 모두 행동 안에서 돌아 Undo·저장·멀티가 그대로다.
    // 가로채는 쪽은 되도록 의도만 고친다(다른 값을 바꾸려면 의도를 고친 뒤 규칙 함수가 하거나, 이벤트 받기로).

    // 의도 클래스에 붙인다(DB의 '가로채기' 잎·'이벤트 쏘기'처럼 목록에 나오고, 필드 읽기 코드가 생성된다).
    // 취소할 수 있게 하려면 public bool cancelled; 필드를 둔다.
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
    public sealed class IntentAttribute : Attribute
    {
        public string DisplayName { get; }

        public IntentAttribute(string displayName = null)
        {
            DisplayName = displayName;
        }
    }

    // 문자열 필드가 의도 형식 이름임을 표시한다(에디터가 [Intent] 형식 목록을 띄운다).
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class IntentTypeRefAttribute : Attribute
    {
    }

    public static class Intents
    {
        static readonly Dictionary<Type, string> Names = new Dictionary<Type, string>();

        public static string DisplayNameOf(Type type)
        {
            if (type == null) return "";
            lock (Names)
            {
                if (Names.TryGetValue(type, out var name)) return name;
                name = (type.GetCustomAttributes(typeof(IntentAttribute), false).FirstOrDefault() as IntentAttribute)?.DisplayName ?? type.Name;
                Names[type] = name;
                return name;
            }
        }

        // 의도의 cancelled 필드(있으면).
        public static bool IsCancelled(object intent) => intent != null && FieldAccess.Read(intent, "cancelled") is bool b && b;
    }

    // 기본 제공 의도: 비용 지불(Game.Pay). "이번 턴 모든 비용 0" 같은 효과가 이것을 가로챈다.
    [Serializable, Intent("비용 지불")]
    public sealed class CostIntent
    {
        public string key;      // 플레이어 데이터 키
        public string path;     // 그 값 안의 숫자 필드 경로(비우면 값 전체)
        public double amount;   // 낼 양
        public bool cancelled;  // 참이면 내지 않는다(공짜)
    }

    [Serializable]
    [BlockInfo("가로채기", Category = "트리거", Description = "어떤 일이 일어나기 직전입니다(의도). 결과에서 '의도 고치기'로 그 내용을 바꾸거나 막습니다. 예: 받는 피해 −5, 죽음 막기, 비용 0.",
               Sentence = "{intentType} 가로채기")]
    public sealed class InterceptTrigger : Trigger
    {
        [IntentTypeRef, FieldPathRoot(FieldPathRootKind.EventType), FieldLabel("의도")] public string intentType;
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.SerializeReference]
#endif
        [FieldLabel("의도 조건")] public ValueFilter filter;
        [FieldLabel("순서(작을수록 먼저)")] public int order;

        public override TriggerKinds Kind => TriggerKinds.Intercept;

        [NonSerialized] string _resolvedFrom;
        [NonSerialized] string _resolved;

        public string ResolvedType
        {
            get
            {
                if (_resolved != null && ReferenceEquals(_resolvedFrom, intentType)) return _resolved;
                _resolvedFrom = intentType;
                _resolved = string.IsNullOrEmpty(intentType) ? "" : TypeResolver.Find(intentType)?.FullName ?? intentType;
                return _resolved;
            }
        }

        public override bool Test(TriggerContext moment)
        {
            var intent = moment?.Event;
            if (intent == null || !moment.IsIntercept || string.IsNullOrEmpty(intentType) || intent.GetType().FullName != ResolvedType) return false;
            return filter == null || filter.Test(intent);
        }

        string Name => TypeResolver.Find(intentType) is Type t ? Intents.DisplayNameOf(t) : (string.IsNullOrEmpty(intentType) ? "(의도 없음)" : intentType);

        public override string Describe(INameResolver names) => $"intercept {Name}" + (filter == null ? "" : $" where {filter.Describe()}");
        public override string Natural(INameResolver names) => $"{Name} 가로채기" + (filter == null ? "" : $"({filter.Natural()}일 때)");

        public override void Validate(IValidationContext context)
        {
            var type = TypeResolver.Find(intentType);
            if (type == null)
            {
                context.Error(string.IsNullOrEmpty(intentType) ? "가로채기: 의도가 선택되지 않았습니다." : $"가로채기: 의도 '{intentType}'을 찾을 수 없습니다(이름이 바뀌었는지 확인).");
                return;
            }
            if (filter == null) return;
            var problems = new List<string>();
            filter.Validate(type, problems);
            foreach (var problem in problems) context.Error($"가로채기({Intents.DisplayNameOf(type)}) 조건: {problem}");
        }
    }

    public enum IntentOp
    {
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("값으로 바꾸기")]
#endif
        Set,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("더하기")]
#endif
        Add,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("곱하기")]
#endif
        Multiply,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("막기")]
#endif
        Cancel,
    }

    // 결과 Block '의도 고치기': 가로챈 의도의 필드를 바꾸거나 의도를 막는다. 가로채기 트리거의 결과로만 의미가 있다.
    // 값에 틀을 쓸 수 있다: {user}(사용자·가진 쪽의 id) · {owner}(이 결과를 가진 항목 ID) · {stacks}(가진 쪽의 이 상태 효과 중첩) · {balance:ID}(밸런스 값).
    //   대신 맞기: targetId 값으로 바꾸기 {user}   도발: 공격 의도의 targetId 값으로 바꾸기 {user}   중첩만큼 피해 감소: amount 더하기 -{stacks}
    [Serializable]
    [BlockInfo("의도 고치기", Category = "결과", Description = "가로챈 의도의 값을 바꾸거나(설정·더하기·곱하기) 막습니다(취소).")]
    public sealed class ModifyIntentOutcome : Outcome
    {
        [FieldPath, FieldLabel("필드")] public string path = "";
        [FieldLabel("방법")] public IntentOp op = IntentOp.Add;
        [FieldLabel("값(숫자·글자, 틀 {user} {stacks} {balance:ID})")] public string value = "";

        public override OutcomeKinds Kind => OutcomeKinds.Intent;

        static readonly Regex Token = new Regex(@"\{(user|owner|stacks|balance:[^}]+)\}", RegexOptions.Compiled);

        // 틀을 이 순간의 값으로 바꾼다. 앞에 붙은 '-'와 겹치면(-{stacks}가 -3) 숫자 하나로 읽힌다.
        public static string Expand(string text, TriggerContext context)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0 || context == null) return text ?? "";
            var expanded = Token.Replace(text, m =>
            {
                var name = m.Groups[1].Value;
                if (name == "user") return context.User == null || !GameApi.HasId(context.User.GetType()) ? "" : GameApi.IdOf(context.User) ?? "";
                if (name == "owner") return context.Owner ?? "";
                if (name == "stacks") return context.User == null || string.IsNullOrEmpty(context.Owner) ? "0" : context.Game.StatusStacks(context.User, context.Owner).ToString(CultureInfo.InvariantCulture);
                return context.Game.Balance(name.Substring("balance:".Length)).ToString("R", CultureInfo.InvariantCulture);
            });
            return expanded.Replace("--", "");
        }

        public override void Run(TriggerContext context)
        {
            var intent = context?.Event;
            if (intent == null || !context.IsIntercept)
            {
                context?.Session?.Journal.Append(JournalKind.Warning, "Intercept", "의도 고치기: 가로채기로 발동한 것이 아니라 고칠 의도가 없습니다.");
                return;
            }
            if (op == IntentOp.Cancel)
            {
                var cancel = FieldAccess.Find(intent.GetType(), "cancelled");
                if (cancel == null) context.Session.Journal.Append(JournalKind.Warning, "Intercept", $"의도 고치기: {intent.GetType().Name}에 cancelled 필드가 없어 막을 수 없습니다.");
                else cancel.Set(intent, true);
                return;
            }
            var type = FieldPaths.TypeAt(intent.GetType(), path, out var error);
            if (type == null)
            {
                context.Session.Journal.Append(JournalKind.Warning, "Intercept", $"의도 고치기: {error}");
                return;
            }
            var cut = path.LastIndexOf('.');
            var parentPath = cut < 0 ? "" : path.Substring(0, cut);
            var member = cut < 0 ? path : path.Substring(cut + 1);
            foreach (var parent in FieldPaths.Resolve(intent, parentPath).ToList())
            {
                if (parent == null || parent.GetType().IsValueType) continue;
                var access = FieldAccess.Find(parent.GetType(), member);
                if (access == null) continue;
                var next = Compute(access.Get(parent), type, Expand(value, context));
                if (next != null) access.Set(parent, next);
            }
        }

        object Compute(object current, Type type, string text)
        {
            var underlying = Nullable.GetUnderlyingType(type) ?? type;
            if (op == IntentOp.Set) return PublishEventOutcome.TryParse(type, text, out var set) ? set : null;
            if (!TypeResolver.IsNumeric(underlying) || !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return null;
            var before = current == null ? 0 : Convert.ToDouble(current, CultureInfo.InvariantCulture);
            var after = op == IntentOp.Add ? before + number : before * number;
            return Convert.ChangeType(underlying == typeof(int) || underlying == typeof(long) || underlying == typeof(short) ? Math.Round(after) : after, underlying, CultureInfo.InvariantCulture);
        }

        public override string Describe(INameResolver names)
        {
            switch (op)
            {
                case IntentOp.Cancel: return "의도 막기";
                case IntentOp.Set: return $"의도 {path} = {value}";
                case IntentOp.Add: return $"의도 {path} {(value.StartsWith("-") ? "" : "+")}{value}";
                default: return $"의도 {path} ×{value}";
            }
        }

        public override void Validate(IValidationContext context)
        {
            if (op == IntentOp.Cancel) return;
            if (string.IsNullOrEmpty(path)) context.Error("의도 고치기: 필드를 고르세요.");
            if (op != IntentOp.Set && value.IndexOf('{') < 0 && !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) context.Error("의도 고치기: 더하기·곱하기에는 숫자(또는 {stacks}·{balance:ID} 틀)를 적으세요.");
        }
    }
}
