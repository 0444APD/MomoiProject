using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GameSystems.Data
{
    // 대화 캐릭터. 이름은 텍스트 시트의 '<id>.name', 표정은 ID → 스프라이트.
    [Database("캐릭터", Group = "대화", Order = 0, IdPrefix = "character")]
    public sealed class CharacterData : GameData
    {
        [Serializable]
        public sealed class Expression
        {
            [FieldLabel("표정 ID")] public string id = "default";
            [FieldLabel("스프라이트")] public Sprite sprite;
        }

        [FieldLabel("표정")] public List<Expression> expressions = new List<Expression> { new Expression() };
        [FieldLabel("기본 표정 ID(비우면 첫 표정)")] public string defaultExpression = "";
        [FieldLabel("이름 색")] public Color nameColor = Color.white;
        [FieldLabel("스탠딩 프리팹(StandingView가 붙은 프리팹, 스파인 등. 비우면 표정 스프라이트로 그린다)")] public GameObject standingPrefab;
        [FieldLabel("스탠딩 크기 배율"), Min(0.01f)] public float standingScale = 1f;

        public Sprite SpriteOf(string expression)
        {
            var found = expressions.FirstOrDefault(e => e.id == expression)
                        ?? expressions.FirstOrDefault(e => e.id == defaultExpression)
                        ?? expressions.FirstOrDefault();
            return found?.sprite;
        }

        public override void ValidateData(IValidationContext context)
        {
            foreach (var duplicate in expressions.GroupBy(e => e.id).Where(g => g.Count() > 1))
                context.Error($"표정 ID '{duplicate.Key}'가 겹칩니다.");
            if (!string.IsNullOrEmpty(defaultExpression) && expressions.All(e => e.id != defaultExpression))
                context.Error($"기본 표정 '{defaultExpression}'이(가) 표정 목록에 없습니다.");
        }
    }
}
