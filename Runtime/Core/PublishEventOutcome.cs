using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GBible
{
    // 결과 Block '이벤트 쏘기': 고른 이벤트를 적은 값으로 쏜다(그 이벤트를 받는 로그·도전과제·트리거·코드가 반응한다).
    // 개발 명령(DB › 개발 명령)에서 "한 턴 30 피해"처럼 이벤트 반응을 시험할 때 쓴다. 다른 결과 칸에서도 쓸 수 있다.
    // 값은 숫자·글자·참거짓·enum 필드만(경로 a.b로 안쪽 필드도). 적지 않은 필드는 이벤트 클래스의 기본값이다.
    [Serializable]
    [BlockInfo("이벤트 쏘기", Category = "결과", Description = "고른 이벤트를 적은 값으로 쏩니다. 그 이벤트를 받는 로그·도전과제·트리거가 반응합니다.")]
    public sealed class PublishEventOutcome : Outcome
    {
        [EventTypeRef, FieldPathRoot(FieldPathRootKind.EventType), FieldLabel("이벤트")] public string eventType = "";
        [FieldLabel("값")] public List<EventFieldValue> values = new List<EventFieldValue>();
        [FieldLabel("횟수")] public int count = 1;

        public override OutcomeKinds Kind => OutcomeKinds.Event;

        public override void Run(TriggerContext context)
        {
            var type = TypeResolver.Find(eventType);
            if (type == null)
            {
                context.Session.Journal.Append(JournalKind.Warning, "Events", $"이벤트 쏘기: 이벤트 '{eventType}'를 찾을 수 없습니다.");
                return;
            }
            for (int i = 0; i < Math.Max(1, count); i++)
            {
                var evt = DataModel.Create(type);
                foreach (var value in values)
                    if (!TryApply(evt, type, value, context, out var error))
                        context.Session.Journal.Append(JournalKind.Warning, "Events", $"이벤트 쏘기({EventBus.DisplayNameOf(type)}): {error}");
                context.Session.Events.PublishObject(evt);
            }
        }

        // 값에 의도 고치기와 같은 틀을 쓸 수 있다: {user}·{user:경로}·{event:경로}·{data:키.경로}·{count:키.경로}·{level}·{stacks}·{balance:ID}.
        static bool TryApply(object evt, Type type, EventFieldValue value, TriggerContext context, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(value?.path)) return true;
            var target = FieldPaths.TypeAt(type, value.path, out error);
            if (target == null) return false;
            var text = ModifyIntentOutcome.Expand(value.value, context);
            if (!TryParse(target, text, out var parsed))
            {
                error = $"'{value.path}'에 '{text}'를 넣을 수 없습니다({target.Name}).";
                return false;
            }
            // 마지막 마디는 필드나 딕셔너리의 키. 괄호 안의 점은 나누지 않는다.
            FieldPaths.SplitLast(value.path, out var parentPath, out var member);
            var written = false;
            foreach (var parent in FieldPaths.Resolve(evt, parentPath).ToList())
                if (parent != null && !parent.GetType().IsValueType && FieldPaths.Assign(parent, member, (_, __) => parsed)) written = true;
            if (!written)
            {
                error = $"'{value.path}'에 쓰지 못했습니다(부모가 비었거나 구조체입니다).";
                return false;
            }
            return true;
        }

        // 글자를 그 형식의 값으로. 참거짓은 PlayerDataGrants.TryParseBool(대소문자 무관 — 값 틀이 펼친 True도), 읽지 못하는 글자는 거짓을 돌려준다
        // (부르는 쪽이 경고한다 — 거짓으로 넣지 않는다). 정수 형식은 GameNumbers의 반올림(0에서 먼 쪽).
        public static bool TryParse(Type type, string text, out object value)
        {
            value = null;
            text = text ?? "";
            var underlying = Nullable.GetUnderlyingType(type) ?? type;
            try
            {
                if (underlying == typeof(string)) value = text;
                else if (underlying == typeof(bool))
                {
                    if (!PlayerDataGrants.TryParseBool(text, out var flag)) return false;
                    value = flag;
                }
                else if (underlying.IsEnum) value = Enum.Parse(underlying, text);
                else if (TypeResolver.IsNumeric(underlying)) value = GameNumbers.ToType(double.Parse(text, CultureInfo.InvariantCulture), underlying);
                else return false;
                return true;
            }
            catch (Exception e) when (e is FormatException || e is ArgumentException || e is OverflowException)
            {
                return false;
            }
        }

        public override string Describe(INameResolver names)
        {
            var type = TypeResolver.Find(eventType);
            var name = type != null ? EventBus.DisplayNameOf(type) : (string.IsNullOrEmpty(eventType) ? "(이벤트 없음)" : eventType);
            var set = values.Where(v => !string.IsNullOrEmpty(v?.path)).Select(v => $"{v.path} = {v.value}").ToList();
            return $"이벤트 쏘기: {name}" + (set.Count > 0 ? $" ({string.Join(", ", set)})" : "") + (count > 1 ? $" ×{count}" : "");
        }

        public override void Validate(IValidationContext context)
        {
            var type = TypeResolver.Find(eventType);
            if (type == null)
            {
                context.Error(string.IsNullOrEmpty(eventType) ? "이벤트 쏘기: 이벤트를 고르세요." : $"이벤트 쏘기: 이벤트 '{eventType}'를 찾을 수 없습니다.");
                return;
            }
            foreach (var value in values)
            {
                if (string.IsNullOrEmpty(value?.path)) continue;
                var target = FieldPaths.TypeAt(type, value.path, out var error);
                if (target == null) context.Error($"이벤트 쏘기: {error}");
                // 틀({user}·{count:…})이 든 값은 발동할 때 정해진다 → 형식은 그때 본다(맞지 않으면 경고).
                else if ((value.value ?? "").IndexOf('{') < 0 && !TryParse(target, value.value, out _)) context.Error($"이벤트 쏘기: '{value.path}'에 '{value.value}'를 넣을 수 없습니다({target.Name}).");
            }
            if (count < 1) context.Error("이벤트 쏘기: 횟수는 1 이상이어야 합니다.");
        }
    }

    // 이벤트 쏘기의 값 한 줄: 필드 경로 = 값(글자로).
    [Serializable]
    public sealed class EventFieldValue
    {
        [FieldPath, FieldLabel("필드")] public string path = "";
        [FieldLabel("값")] public string value = "";
    }
}
