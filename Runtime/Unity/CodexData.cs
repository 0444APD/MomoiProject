using System;
using UnityEngine;

namespace GameSystems.Data
{
    // 템플릿: 개발자가 만든 클래스의 미리 채운 인스턴스(더미 아이템·테스트 몬스터 설정 등).
    // 도감에서 형식을 고르면 빈 인스턴스가 만들어지고, 탐색기로 값을 채운다. 게임·테스트에서는 CreateInstance로 사본을 받는다.
    [Database("템플릿", Group = "도감", Order = 50, IdPrefix = "template")]
    [CreateAssetMenu(menuName = "Game Systems/템플릿", fileName = "NewTemplate")]
    public sealed class TemplateData : GameData
    {
        [FieldLabel("형식"), DataTypeRef(InstanceField = nameof(value))] public string templateType = "";
        [SerializeReference, FieldLabel("값")] public object value;

        public Type TemplateType => TypeResolver.Find(templateType);

        // 템플릿의 사본(원본을 건드리지 않는다).
        public object CreateInstance() => value == null ? null : DataModel.Clone(value, null);
        public T CreateInstance<T>() where T : class => CreateInstance() as T;

        public override void ValidateData(IValidationContext context)
        {
            var type = TemplateType;
            if (type == null)
            {
                context.Error(string.IsNullOrEmpty(templateType) ? "형식이 선택되지 않았습니다." : $"형식 '{templateType}'을 찾을 수 없습니다.");
                return;
            }
            if (value == null) context.Error("값이 비어 있습니다. 형식을 다시 고르면 새로 만들어집니다.");
            else if (!type.IsInstanceOfType(value)) context.Error($"값의 형식({value.GetType().Name})이 고른 형식({type.Name})과 다릅니다. 형식을 다시 고르세요.");
        }
    }

    // 프리팹: 도감에 등록한 프리팹. 도감에서 컴포넌트 필드를 탐색기로 보고 고친다(프리팹 에셋에 바로 저장, Undo 가능).
    [Database("프리팹", Group = "도감", Order = 51, IdPrefix = "prefab")]
    [CreateAssetMenu(menuName = "Game Systems/프리팹", fileName = "NewPrefab")]
    public sealed class PrefabData : GameData
    {
        [FieldLabel("프리팹")] public GameObject prefab;

        public override void ValidateData(IValidationContext context)
        {
            if (prefab == null) context.Error("프리팹이 연결되지 않았습니다.");
        }
    }
}
