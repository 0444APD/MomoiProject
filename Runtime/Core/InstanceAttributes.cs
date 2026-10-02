using System;

namespace GBible
{
    // 인스턴스(도감 › 인스턴스)를 편집할 때 이 필드를 보이지 않는다. 값은 그대로 저장·복사된다.
    // 에디터의 클래스 상세에서 켜고 꺼도 이 어트리뷰트가 코드에 쓰인다.
    [AttributeUsage(AttributeTargets.Field, Inherited = true)]
    public sealed class HideAttribute : Attribute
    {
    }

    // 다른 필드의 값에 따라 이 필드를 보인다. 조건 필드가 bool이면 참일 때, 객체·글자면 비어 있지 않을 때, 그 밖에는 value와 같을 때.
    //   [ShowIf(nameof(usable))] public int charges;              // usable 기능이 붙어 있을 때만
    //   [ShowIf(nameof(kind), "weapon")] public int attack;      // kind가 "weapon"일 때만
    [AttributeUsage(AttributeTargets.Field, Inherited = true)]
    public sealed class ShowIfAttribute : Attribute
    {
        public string Field { get; }
        public object Value { get; }

        public ShowIfAttribute(string field)
        {
            Field = field;
        }

        public ShowIfAttribute(string field, object value)
        {
            Field = field;
            Value = value;
        }
    }
}
