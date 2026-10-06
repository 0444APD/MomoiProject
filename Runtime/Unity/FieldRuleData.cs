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
        [InspectorName("DB 항목")]
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

    // 필드 규칙: 에디터에서 정하는 필드 설정 — 값 출처(DB 항목·플레이어 데이터), 필수, 스프라이트 규격 그룹.
    // 키워드 그룹 연결은 코드의 [Keyword(typeof(그룹))]로 한다(예전 Keyword 출처는 이전 도구가 옮긴다).
    // 모든 편집 칸(DB·탐색기·이벤트 호출·조건식 값)과 ID 바꾸기·검증이 이 규칙을 따른다.
    [Database("필드 규칙", Group = "요소", Order = 9, IdPrefix = "rule", Hidden = true)]
    [CreateAssetMenu(menuName = "MomoiProject/필드 규칙", fileName = "NewFieldRule")]
    public sealed class FieldRuleData : GameData
    {
        [FieldLabel("클래스"), ClassTypeRef] public string targetType = "";
        [FieldLabel("필드"), MemberRef(nameof(targetType))] public string field = "";
        [FieldLabel("값 출처")] public FieldRuleSource source = FieldRuleSource.Keyword;
        // 예전 키워드 연결(지금은 필드의 [Keyword(typeof(그룹))]). 이전 도구가 읽고 비운다.
        [HideInInspector] public string keywordGroup = "";
        [FieldLabel("DB 종류"), GameDataTypeRef] public string dataType = "";
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
            if (source == FieldRuleSource.Data && TypeResolver.Find(dataType) == null) context.Error("DB 종류를 고르세요.");
        }
    }

    // 문자열 필드가 클래스 이름(타입 전체 이름)임을 표시한다. 편집기는 프로젝트의 데이터 클래스·DB 종류 목록을 띄운다.
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ClassTypeRefAttribute : Attribute
    {
    }

    // 문자열 필드가 DB 종류(GameData 파생 타입 이름)임을 표시한다.
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
