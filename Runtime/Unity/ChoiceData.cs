using System;
using UnityEngine;

namespace GameSystems.Data
{
    // 선택지 목록: 아이템 분류·등급·속성처럼 "목록에서 고르는 값"의 묶음. 선택지 하나하나는 ChoiceData 항목이다.
    // 코드에는 빈 string 필드만 두고, 어떤 목록에서 고를지는 필드 규칙으로 연결한다.
    [Database("선택지 목록", Group = "식별자", Order = 2, IdPrefix = "choices")]
    [CreateAssetMenu(menuName = "Game Systems/선택지 목록", fileName = "NewChoiceList")]
    public sealed class ChoiceListData : GameData
    {
        [FieldLabel("설명")] public string description = "";
    }

    // 선택지 하나. 값으로는 이 항목의 ID가 들어간다(이름을 바꿔도 값이 따라간다).
    [Database("선택지", Group = "식별자", Order = 3, IdPrefix = "choice")]
    [CreateAssetMenu(menuName = "Game Systems/선택지", fileName = "NewChoice")]
    public sealed class ChoiceData : GameData
    {
        [FieldLabel("목록"), DataRef(typeof(ChoiceListData))] public string list = "";

        public override void ValidateData(IValidationContext context)
        {
            if (string.IsNullOrEmpty(list)) context.Error("어느 선택지 목록에 속하는지 정해지지 않았습니다.");
        }
    }

    public enum FieldRuleSource
    {
#if UNITY_2020_1_OR_NEWER
        [InspectorName("태그")]
#endif
        Tag,
#if UNITY_2020_1_OR_NEWER
        [InspectorName("선택지 목록")]
#endif
        Choice,
#if UNITY_2020_1_OR_NEWER
        [InspectorName("도감 항목")]
#endif
        Data,
#if UNITY_2020_1_OR_NEWER
        [InspectorName("상태 키")]
#endif
        StateKey,
#if UNITY_2020_1_OR_NEWER
        [InspectorName("자유 입력")]
#endif
        Free,
    }

    // 필드 규칙: "이 클래스의 이 필드는 여기서 고른다(, 필수다)". 코드·어트리뷰트 없이 도감에서 연결한다.
    // 모든 편집 칸(도감·탐색기·이벤트 발생기·조건식 값)과 ID 바꾸기·검증이 이 규칙을 따른다.
    [Database("필드 규칙", Group = "식별자", Order = 4, IdPrefix = "rule")]
    [CreateAssetMenu(menuName = "Game Systems/필드 규칙", fileName = "NewFieldRule")]
    public sealed class FieldRuleData : GameData
    {
        [FieldLabel("클래스"), ClassTypeRef] public string targetType = "";
        [FieldLabel("필드"), MemberRef(nameof(targetType))] public string field = "";
        [FieldLabel("값 출처")] public FieldRuleSource source = FieldRuleSource.Tag;
        [FieldLabel("선택지 목록"), DataRef(typeof(ChoiceListData))] public string choiceList = "";
        [FieldLabel("도감 종류"), GameDataTypeRef] public string dataType = "";
        [FieldLabel("필수")] public bool required;

        public Type TargetType => TypeResolver.Find(targetType);

        public override void ValidateData(IValidationContext context)
        {
            var type = TargetType;
            if (type == null)
            {
                context.Error(string.IsNullOrEmpty(targetType) ? "클래스가 선택되지 않았습니다." : $"클래스 '{targetType}'를 찾을 수 없습니다.");
                return;
            }
            if (string.IsNullOrEmpty(field)) context.Error("필드가 선택되지 않았습니다.");
            else if (type.GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic) == null)
                context.Error($"'{type.Name}'에 '{field}' 필드가 없습니다(이름이 바뀌었는지 확인).");
            if (source == FieldRuleSource.Choice && string.IsNullOrEmpty(choiceList)) context.Error("선택지 목록을 고르세요.");
            if (source == FieldRuleSource.Data && TypeResolver.Find(dataType) == null) context.Error("도감 종류를 고르세요.");
        }
    }

    // 문자열 필드가 클래스 이름(타입 전체 이름)임을 표시한다. 편집기는 프로젝트의 데이터 클래스·도감 종류 목록을 띄운다.
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ClassTypeRefAttribute : Attribute
    {
    }

    // 문자열 필드가 도감 종류(GameData 파생 타입 이름)임을 표시한다.
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class GameDataTypeRefAttribute : Attribute
    {
    }

    // 문자열 필드가 옆 칸(타입 이름)이 가리키는 클래스의 필드 이름임을 표시한다.
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class MemberRefAttribute : Attribute
    {
        public string TypeField { get; }

        public MemberRefAttribute(string typeField)
        {
            TypeField = typeField;
        }
    }
}
