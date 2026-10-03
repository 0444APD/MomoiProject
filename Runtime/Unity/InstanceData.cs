using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace GBible.Data
{
    // 인스턴스: 클래스(코드로 정의한 아이템·유닛 …)로 만든 원본 하나(몬스터A, 아이템A …). 게임은 이 원본의 깊은 사본을 쓴다.
    // DB › 인스턴스에서 형식을 고르면 빈 객체가 만들어지고, 값을 채운다(필드 보이기·숨기기는 클래스의 [Hide]·[ShowIf]).
    // 이 에셋은 저장용 껍데기다(평범한 C# 객체는 혼자 에셋이 될 수 없다). 게임 코드는 value(클래스 객체)만 다룬다.
    [Database("인스턴스", Group = "DB", Order = 50, IdPrefix = "instance")]
    [CreateAssetMenu(menuName = "MomoiProject/인스턴스", fileName = "NewInstance")]
    public sealed class InstanceData : GameData
    {
        [FieldLabel("형식"), DataTypeRef(InstanceField = nameof(value)), FormerlySerializedAs("templateType")] public string instanceType = "";
        [SerializeReference, FieldLabel("값")] public object value;

        public Type InstanceType => TypeResolver.Find(instanceType);

        // 원본의 깊은 사본(원본을 건드리지 않는다). 비교 코드가 있는 형식은 생성 코드로 복사한다(리플렉션 없음).
        public object CreateInstance() => Copy(value);
        public new T CreateInstance<T>() where T : class => CreateInstance() as T;

        public static object Copy(object value)
        {
            if (value == null) return null;
            return Mirror.Find(value.GetType()) != null && Mirror.Supports(value.GetType(), out _) ? Mirror.Clone(value) : DataModel.Clone(value, null);
        }

        public override void ValidateData(IValidationContext context)
        {
            var type = InstanceType;
            if (type == null)
            {
                context.Error(string.IsNullOrEmpty(instanceType) ? "형식이 선택되지 않았습니다." : $"형식 '{instanceType}'을 찾을 수 없습니다.");
                return;
            }
            if (value == null) context.Error("값이 비어 있습니다. 형식을 다시 고르면 새로 만들어집니다.");
            else if (!type.IsInstanceOfType(value)) context.Error($"값의 형식({value.GetType().Name})이 고른 형식({type.Name})과 다릅니다. 형식을 다시 고르세요.");
        }
    }
}
