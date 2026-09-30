using System;
using System.Collections.Generic;
using GameSystems.Data;
using UnityEngine;

namespace GameSystems.Samples
{
    // 기능 조립식 아이템 예시: 모든 아이템에 공통인 칸은 평평하게, 종류마다 다른 부분은 기능 묶음 목록으로 붙인다.
    //   강철검 = [장착] · 체력 물약 = [사용, 겹침] · 축복받은 반지 = [장착, 사용] · 폭탄 = [사용, 겹침, 투척]
    // 코드는 종류를 묻지 않는다: item.features.GetFeature<SampleEquippable>(), SampleItem.Use(item, user)
    [Database("아이템", Group = "샘플", Order = 5, IdPrefix = "item")]
    public sealed class SampleItem : GameData
    {
        [FieldLabel("아이콘")] public Sprite icon;
        // 등급·분류는 키워드(키워드 연결 창에서 그룹을 잇는다).
        [FieldLabel("등급")] public string grade = "";
        [FieldLabel("가격"), Min(0)] public int price;
        [FieldLabel("기능"), SerializeReference] public List<SampleItemFeature> features = new List<SampleItemFeature>();

        public override void ValidateData(IValidationContext context) => Features.Validate(features, context);

        // 사용하기: 붙은 기능마다 자기 동작을 한다(사용 기능이 없으면 아무 일도 없다).
        public static bool Use(SampleItem item, SampleUnit user)
        {
            if (!item.features.HasFeature<SampleConsumable>()) return false;
            foreach (var feature in item.features) feature?.OnUse(item, user);
            return true;
        }
    }

    // 아이템 기능의 바탕. 기능마다 필요한 순간만 덮어쓴다.
    [Serializable]
    public abstract class SampleItemFeature
    {
        public virtual void OnUse(SampleItem item, SampleUnit user)
        {
        }
    }

    [Serializable, BlockInfo("장착", Category = "아이템 기능", Description = "장비 칸에 낄 수 있다.")]
    public sealed class SampleEquippable : SampleItemFeature
    {
        [FieldLabel("부위")] public string slot = "";
        [FieldLabel("공격력")] public int attack;
        [FieldLabel("방어력")] public int defense;
    }

    [Serializable, BlockInfo("사용", Category = "아이템 기능", Description = "쓰면 효과가 난다.")]
    public sealed class SampleConsumable : SampleItemFeature
    {
        [FieldLabel("회복량")] public int heal;
        [FieldLabel("쓰면 사라진다")] public bool consume = true;

        public override void OnUse(SampleItem item, SampleUnit user) => user.hp = Mathf.Min(user.maxHp, user.hp + heal);
    }

    [Serializable, BlockInfo("겹침", Category = "아이템 기능", Description = "한 칸에 여러 개가 쌓인다.")]
    public sealed class SampleStackable : SampleItemFeature
    {
        [FieldLabel("최대 겹침"), Min(1)] public int maxStack = 99;
    }

    // 규칙 예시: 투척은 사용 기능이 함께 있어야 하고, 장착과는 함께 붙일 수 없다.
    [Serializable, BlockInfo("투척", Category = "아이템 기능", Description = "던져서 피해를 준다.")]
    [FeatureRule(Requires = new[] { typeof(SampleConsumable) }, ExclusiveWith = new[] { typeof(SampleEquippable) })]
    public sealed class SampleThrowable : SampleItemFeature
    {
        [FieldLabel("피해")] public int damage = 30;
        [FieldLabel("사거리")] public float range = 5f;
    }

    // 예시용 사용자.
    [Serializable]
    public sealed class SampleUnit
    {
        public int hp = 50;
        public int maxHp = 100;
    }
}
