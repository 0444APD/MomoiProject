using System;

namespace GBible
{
    // 인스턴스(DB › 인스턴스)를 편집할 때 이 필드를 보이지 않는다. 값은 그대로 저장·복사된다.
    // 에디터의 클래스 상세에서 켜고 꺼도 이 어트리뷰트가 코드에 쓰인다.
    [AttributeUsage(AttributeTargets.Field, Inherited = true)]
    public sealed class HideAttribute : Attribute
    {
    }

    // 다른 필드의 값에 따라 이 필드를 보인다. 조건 필드가 bool이면 참일 때, 객체·글자면 비어 있지 않을 때, 그 밖에는 값 중 하나와 같을 때.
    //   [ShowIf(nameof(usable))] public int charges;                              // usable 기능이 붙어 있을 때만
    //   [ShowIf(nameof(kind), "weapon")] public int attack;                      // kind가 "weapon"일 때만
    //   [ShowIf(nameof(kind), CurveKind.Linear, CurveKind.Power)] public double startX;   // 둘 중 하나일 때
    // DB › 인스턴스, 그리고 DB 항목 안의 묶음 필드(곡선·지속 설정 …)에서 쓰인다.
    [AttributeUsage(AttributeTargets.Field, Inherited = true)]
    public sealed class ShowIfAttribute : Attribute
    {
        public string Field { get; }
        public object Value { get; }
        public object[] Values { get; }

        public ShowIfAttribute(string field)
        {
            Field = field;
            Values = Array.Empty<object>();
        }

        public ShowIfAttribute(string field, object value, params object[] more)
        {
            Field = field;
            Value = value;
            Values = new object[(more?.Length ?? 0) + 1];
            Values[0] = value;
            more?.CopyTo(Values, 1);
        }

        // 조건 필드의 지금 값(글자로)이 맞는지. 값을 주지 않았으면 bool은 참일 때, 그 밖에는 비어 있지 않을 때.
        public bool Matches(object current)
        {
            if (Values.Length == 0)
            {
                if (current is bool flag) return flag;
                if (current is string text) return text.Length > 0;
                return current != null;
            }
            var actual = Convert.ToString(current, System.Globalization.CultureInfo.InvariantCulture);
            foreach (var value in Values)
                if (string.Equals(actual, Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)) return true;
            return false;
        }

        public string Describe() =>
            Values.Length == 0 ? $"'{Field}'이(가) 있거나 참일 때만 보임" : $"'{Field}'이(가) {string.Join(" 또는 ", Array.ConvertAll(Values, v => $"'{v}'"))}일 때만 보임";
    }
}
