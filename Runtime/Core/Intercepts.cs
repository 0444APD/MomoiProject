using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace GBible
{
    // 행동 직전 이벤트(코드 이름 Intent·Intercept): 일(피해·비용·사용 …)이 일어나기 직전에 그 내용을 담아 쏘는 이벤트. 받는 쪽이 고치거나 막는다.
    // 이벤트는 일어난 뒤에 알리고, 행동 직전 이벤트는 일어나기 전에 바꾼다. 플레이어 입력 한 단위인 '행동'(GameAction) 하나 안에서 여러 번 생긴다.
    //
    // 중간층 규칙 함수가 의도를 만들어 통과시키고, 돌아온 값으로 일을 한다:
    //   var hit = Game.Intercept(new DamageIntent { source = s, target = t, amount = 10 });
    //   if (hit.cancelled) return;  target.hp -= hit.amount;
    // 받는 쪽은 트리거의 '행동 직전 이벤트 받기' 잎(형식 + 조건 + 순서)으로 등록하고, 결과 '행동 직전 이벤트 고치기'나 [BindTo] 코드로 고친다:
    //   유물 "가시 갑옷" — 행동 직전 이벤트 받기 DamageIntent → 행동 직전 이벤트 고치기 amount 더하기 -5
    //   [BindTo("undying")] static void Undying(DeathIntent i, TriggerContext c) { i.cancelled = true; … }
    // 같은 의도에 여럿이 걸리면 순서(order)가 작은 것부터, 같으면 등록 순서. 모두 행동 안에서 돌아 Undo·저장·멀티가 그대로다.
    // 가로채는 쪽은 되도록 의도만 고친다(다른 값을 바꾸려면 의도를 고친 뒤 규칙 함수가 하거나, 이벤트 받기로).

    // 행동 직전 이벤트 클래스에 붙인다(DB의 '행동 직전 이벤트 받기' 잎·'이벤트 쏘기'처럼 목록에 나오고, 필드 읽기 코드가 생성된다).
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
        public double floor;    // 내고 남은 값이 이보다 작으면 실패(기본 0. 빚을 허용하면 음수 — "−20까지 빚": 값으로 바꾸기 −20)
        public string forId;    // 무엇을 위한 비용(사는 카드·쓰는 주문 ID, 비우면 모름) — "행성 카드는 공짜"가 거른다
        public string userId;   // 내는 쪽(지팡이·플레이어 id)
        public bool cancelled;  // 참이면 내지 않는다(공짜)
    }

    [Serializable]
    [BlockInfo("행동 직전 이벤트 받기", Category = "트리거", Description = "어떤 일이 일어나기 직전에 쏘는 행동 직전 이벤트를 받습니다. 결과에서 '행동 직전 이벤트 고치기'로 그 내용을 바꾸거나 막습니다. 예: 받는 피해 −5, 죽음 막기, 비용 0.",
               Sentence = "{intentType} 행동 직전 이벤트 받기")]
    public sealed class InterceptTrigger : Trigger
    {
        [IntentTypeRef, FieldPathRoot(FieldPathRootKind.EventType), FieldLabel("행동 직전 이벤트")] public string intentType;
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.SerializeReference]
#endif
        [FieldLabel("행동 직전 이벤트 조건")] public ValueFilter filter;
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

        string Name => TypeResolver.Find(intentType) is Type t ? Intents.DisplayNameOf(t) : (string.IsNullOrEmpty(intentType) ? "(행동 직전 이벤트 없음)" : intentType);

        public override string Describe(INameResolver names) => $"intercept {Name}" + (filter == null ? "" : $" where {filter.Describe()}");
        public override string Natural(INameResolver names) => $"{Name} 행동 직전 이벤트 받기" + (filter == null ? "" : $"({filter.Natural()}일 때)");

        public override void Validate(IValidationContext context)
        {
            var type = TypeResolver.Find(intentType);
            if (type == null)
            {
                context.Error(string.IsNullOrEmpty(intentType) ? "행동 직전 이벤트 받기: 행동 직전 이벤트가 선택되지 않았습니다." : $"행동 직전 이벤트 받기: 행동 직전 이벤트 '{intentType}'을 찾을 수 없습니다(이름이 바뀌었는지 확인).");
                return;
            }
            if (filter == null) return;
            var problems = new List<string>();
            filter.Validate(type, problems);
            foreach (var problem in problems) context.Error($"행동 직전 이벤트 받기({Intents.DisplayNameOf(type)}) 조건: {problem}");
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
    // 값에 틀을 쓸 수 있다: {user}(사용자·가진 쪽의 id) · {user:경로}(사용자의 필드) · {event:경로}(받은 이벤트·의도의 필드) · {data:키.경로}(플레이어 데이터) · {count:키.경로}(목록의 개수) · {owner}(이 결과를 가진 항목 ID) · {stacks}(가진 쪽의 이 상태 효과 중첩) · {level}(가진 쪽의 이 패시브 레벨) · {level:2/4/7}(레벨별 값, 1레벨부터) · {balance:ID}(밸런스 값).
    //   대신 맞기: targetId 값으로 바꾸기 {user}   도발: 공격 의도의 targetId 값으로 바꾸기 {user}   중첩만큼 피해 감소: amount 더하기 -{stacks}
    [Serializable]
    [BlockInfo("행동 직전 이벤트 고치기", Category = "결과", Description = "받은 행동 직전 이벤트의 값을 바꾸거나(설정·더하기·곱하기) 막습니다(취소).")]
    public sealed class ModifyIntentOutcome : Outcome
    {
        [FieldPath, FieldLabel("필드")] public string path = "";
        [FieldLabel("방법")] public IntentOp op = IntentOp.Add;
        [FieldLabel("값(숫자·글자, 틀 {user} {user:경로} {event:경로} {data:키.경로} {count:키.경로} {stacks} {level} {level:2/4/7} {balance:ID})")] public string value = "";

        public override OutcomeKinds Kind => OutcomeKinds.Intent;

        // 틀 앞의 '-'도 함께 잡는다(-{stacks}): 틀의 값과 숫자 하나로 읽는다.
        static readonly Regex Token = new Regex(@"(-?)\{(user(?::[^}]+)?|owner|stacks|level(?::[^}]+)?|event:[^}]+|data:[^}]+|count:[^}]+|balance:[^}]+)\}", RegexOptions.Compiled);

        // 틀을 이 순간의 값으로 바꾼다. 앞에 붙은 '-'와 겹치면(-{stacks}가 -3) 숫자 하나로 읽힌다 — 틀의 값이 음수면 부호가 바뀐다(-{x}에서 x가 -2면 2).
        // 합치는 것은 틀 앞의 '-'와 그 틀 값의 부호뿐이다(틀이 펼친 글자 안·틀 밖의 '--'는 그대로).
        public static string Expand(string text, TriggerContext context)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0 || context == null) return text ?? "";
            return Token.Replace(text, m =>
            {
                var value = ValueOf(m.Groups[2].Value, context);
                if (m.Groups[1].Length == 0) return value;
                return value.StartsWith("-", StringComparison.Ordinal) ? value.Substring(1) : "-" + value;
            });
        }

        static string ValueOf(string name, TriggerContext context)
        {
            if (name == "user") return context.User == null || !GameApi.HasId(context.User.GetType()) ? "" : GameApi.IdOf(context.User) ?? "";
            if (name.StartsWith("user:", StringComparison.Ordinal)) return Text(FieldPaths.Resolve(context.User, name.Substring(5)).FirstOrDefault());
            if (name.StartsWith("event:", StringComparison.Ordinal)) return Text(FieldPaths.Resolve(context.Event, name.Substring(6)).FirstOrDefault());
            if (name.StartsWith("data:", StringComparison.Ordinal)) return Text(DataAt(context, name.Substring(5)));
            if (name.StartsWith("count:", StringComparison.Ordinal))
            {
                var found = DataAt(context, name.Substring(6));
                // 빈 칸(null)은 세지 않는다(34칸 가방에 든 것의 수).
                return (found is IEnumerable items && !(found is string) ? items.Cast<object>().Count(o => o != null) : 0).ToString(CultureInfo.InvariantCulture);
            }
            if (name == "owner") return context.Owner ?? "";
            if (name == "stacks") return context.User == null || string.IsNullOrEmpty(context.Owner) ? "0" : context.Game.StatusStacks(context.User, context.Owner).ToString(CultureInfo.InvariantCulture);
            if (name == "level" || name.StartsWith("level:", StringComparison.Ordinal))
            {
                var level = context.User == null || string.IsNullOrEmpty(context.Owner) ? 0 : context.Session.Passives.Owned(context.Owner, context.User);
                return name == "level" ? level.ToString(CultureInfo.InvariantCulture) : PickByLevel(name.Substring("level:".Length), level);
            }
            return context.Game.Balance(name.Substring("balance:".Length)).ToString("R", CultureInfo.InvariantCulture);
        }

        static string Text(object value) => value == null ? "" : value is double d ? d.ToString("R", CultureInfo.InvariantCulture) : Convert.ToString(value, CultureInfo.InvariantCulture);

        // "run.jokers" → 플레이어 데이터 run의 jokers. 키에 점이 들어갈 수 있으므로(player.inventory) 정의된 가장 긴 앞부분을 키로 본다.
        static object DataAt(TriggerContext context, string keyAndPath)
        {
            var data = context.Session?.Data;
            if (data == null || string.IsNullOrEmpty(keyAndPath)) return null;
            for (var cut = keyAndPath.Length; cut > 0; cut = keyAndPath.LastIndexOf('.', cut - 1))
            {
                var key = keyAndPath.Substring(0, cut);
                if (!data.IsDefined(key)) continue;
                var root = data.Get(key);
                return cut == keyAndPath.Length ? root : FieldPaths.Resolve(root, keyAndPath.Substring(cut + 1)).FirstOrDefault();
            }
            return null;
        }

        // "2/4/7"에서 레벨 번째 값(1레벨 = 첫 값, 넘치면 마지막 값, 레벨 0이면 0).
        public static string PickByLevel(string values, int level)
        {
            if (level <= 0) return "0";
            var parts = (values ?? "").Split('/');
            return parts[Math.Min(level, parts.Length) - 1].Trim();
        }

        public override void Run(TriggerContext context)
        {
            var intent = context?.Event;
            if (intent == null || !context.IsIntercept)
            {
                context?.Session?.Journal.Append(JournalKind.Warning, "Intercept", "행동 직전 이벤트 고치기: 행동 직전 이벤트 받기로 발동한 것이 아니라 고칠 행동 직전 이벤트가 없습니다.");
                return;
            }
            if (op == IntentOp.Cancel)
            {
                var cancel = FieldAccess.Find(intent.GetType(), "cancelled");
                if (cancel == null) context.Session.Journal.Append(JournalKind.Warning, "Intercept", $"행동 직전 이벤트 고치기: {intent.GetType().Name}에 cancelled 필드가 없어 막을 수 없습니다.");
                else cancel.Set(intent, true);
                return;
            }
            var type = FieldPaths.TypeAt(intent.GetType(), path, out var error);
            if (type == null)
            {
                context.Session.Journal.Append(JournalKind.Warning, "Intercept", $"행동 직전 이벤트 고치기: {error}");
                return;
            }
            // 마지막 마디는 필드나 딕셔너리의 키(byElement[fire]). 괄호 안의 점(x[forest.chest3])은 나누지 않는다.
            FieldPaths.SplitLast(path, out var parentPath, out var member);
            var text = Expand(value, context);
            var reached = false;
            foreach (var parent in FieldPaths.Resolve(intent, parentPath).ToList())
            {
                if (parent == null) continue;
                reached = true;
                if (parent.GetType().IsValueType)
                {
                    context.Session.Journal.Append(JournalKind.Warning, "Intercept", $"행동 직전 이벤트 고치기: '{path}'의 부모가 구조체라 고칠 수 없습니다(클래스로 바꾸세요).");
                    continue;
                }
                var unreadable = false;
                var written = FieldPaths.Assign(parent, member, (current, memberType) =>
                {
                    var next = Compute(current, memberType, text);
                    unreadable = next == null;
                    return next;
                });
                // 읽지 못한 값은 넣지 않고 알린다(참거짓·숫자가 아닌 글자 — 틀이 엉뚱한 값으로 펼쳐졌을 때).
                if (unreadable) context.Session.Journal.Append(JournalKind.Warning, "Intercept", $"행동 직전 이벤트 고치기: '{path}'에 '{text}'를 넣을 수 없습니다({type.Name}).");
                else if (!written) context.Session.Journal.Append(JournalKind.Warning, "Intercept", $"행동 직전 이벤트 고치기: '{path}'에 쓰지 못했습니다(그 필드·키가 없습니다).");
            }
            // 경로 중간이 비었다(info.amount의 info가 null, units[hero]의 hero가 없음): 고칠 곳이 없다 — 조용히 넘어가지 않고 알린다.
            // 요소마다 펼치는 경로([])가 빈 목록이면 고칠 것이 없는 것이 맞다.
            if (!reached && parentPath.IndexOf(FieldPaths.Each, StringComparison.Ordinal) < 0)
                context.Session.Journal.Append(JournalKind.Warning, "Intercept", $"행동 직전 이벤트 고치기: '{path}'의 부모가 비어 있어 쓰지 못했습니다(경로 중간의 값이 없습니다).");
        }

        object Compute(object current, Type type, string text)
        {
            var underlying = Nullable.GetUnderlyingType(type) ?? type;
            if (op == IntentOp.Set) return PublishEventOutcome.TryParse(type, text, out var set) ? set : null;
            if (!TypeResolver.IsNumeric(underlying) || !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return null;
            var before = current == null ? 0 : Convert.ToDouble(current, CultureInfo.InvariantCulture);
            var after = op == IntentOp.Add ? before + number : before * number;
            return GameNumbers.ToType(after, underlying);
        }

        public override string Describe(INameResolver names)
        {
            switch (op)
            {
                case IntentOp.Cancel: return "행동 직전 이벤트 막기";
                case IntentOp.Set: return $"행동 직전 이벤트 {path} = {value}";
                case IntentOp.Add: return $"행동 직전 이벤트 {path} {(value.StartsWith("-") ? "" : "+")}{value}";
                default: return $"행동 직전 이벤트 {path} ×{value}";
            }
        }

        public override void Validate(IValidationContext context)
        {
            if (op == IntentOp.Cancel) return;
            if (string.IsNullOrEmpty(path)) context.Error("행동 직전 이벤트 고치기: 필드를 고르세요.");
            if (op != IntentOp.Set && value.IndexOf('{') < 0 && !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) context.Error("행동 직전 이벤트 고치기: 더하기·곱하기에는 숫자(또는 {stacks}·{level}·{level:2/4/7}·{balance:ID} 틀)를 적으세요.");
        }
    }
}
