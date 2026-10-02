using System;
using UnityEngine;

namespace GBible.Data
{
    public enum FieldRuleSource
    {
#if UNITY_2020_1_OR_NEWER
        [InspectorName("키워드")]
#endif
        Keyword = 0,
#if UNITY_2020_1_OR_NEWER
        [InspectorName("도감 항목")]
#endif
        Data = 2,
#if UNITY_2020_1_OR_NEWER
        [InspectorName("플레이어 데이터")]
#endif
        PlayerDataKey = 3,
#if UNITY_2020_1_OR_NEWER
        [InspectorName("자유 입력")]
#endif
        Free = 4,
    }

    // 필드 규칙: "이 클래스의 이 필드는 여기서 고른다(, 필수다)". 코드·어트리뷰트 없이 에디터에서 연결한다.
    // 모든 편집 칸(도감·탐색기·이벤트 발생기·조건식 값)과 ID 바꾸기·검증이 이 규칙을 따른다.
    // 키워드: string 필드는 그룹에서 하나, string 배열·리스트 필드는 여러 개를 고른다. 그룹을 비우면 모든 키워드.
    // 보통은 '키워드 연결' 창에서 필드와 그룹을 이어 만들고, 이 도감 탭은 확인·유지보수용이다.
    [Database("필드 규칙", Group = "선언", Order = 9, IdPrefix = "rule", Hidden = true)]
    [CreateAssetMenu(menuName = "MomoiProject/필드 규칙", fileName = "NewFieldRule")]
    public sealed class FieldRuleData : GameData
    {
        [FieldLabel("클래스"), ClassTypeRef] public string targetType = "";
        [FieldLabel("필드"), MemberRef(nameof(targetType))] public string field = "";
        [FieldLabel("값 출처")] public FieldRuleSource source = FieldRuleSource.Keyword;
        [FieldLabel("키워드 그룹"), DataRef(typeof(KeywordGroupData))] public string keywordGroup = "";
        [FieldLabel("도감 종류"), GameDataTypeRef] public string dataType = "";
        [FieldLabel("필수")] public bool required;
        // 스프라이트 필드: 원본을 넣으면 이 그룹의 규격으로 베이크하고, 목록·카드는 미리보기 규격으로 보여 준다.
        [FieldLabel("스프라이트 규격 그룹"), DataRef(typeof(SpritePresetGroupData))] public string spriteGroup = "";
        [FieldLabel("미리보기 규격"), DataRef(typeof(SpritePresetData))] public string previewPreset = "";

        public Type TargetType => TypeResolver.Find(targetType);

        public System.Reflection.FieldInfo Field =>
            TargetType?.GetField(field ?? "", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

        public override void ValidateData(IValidationContext context)
        {
            var type = TargetType;
            if (type == null)
            {
                context.Error(string.IsNullOrEmpty(targetType) ? "클래스가 선택되지 않았습니다." : $"클래스 '{targetType}'를 찾을 수 없습니다.");
                return;
            }
            if (string.IsNullOrEmpty(field)) context.Error("필드가 선택되지 않았습니다.");
            else if (Field == null)
                context.Error($"'{type.Name}'에 '{field}' 필드가 없습니다(이름이 바뀌었는지 확인).");
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
