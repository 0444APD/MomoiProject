using System.Collections.Generic;
using UnityEngine;

namespace GameSystems.Data
{
    [Database("도전과제", Group = "진행", Order = 20, IdPrefix = "achievement")]
    [CreateAssetMenu(menuName = "Game Systems/도전과제", fileName = "NewAchievement")]
    public sealed class AchievementData : GameData
    {
        [FieldLabel("아이콘")] public Sprite icon;
        [FieldLabel("설명"), TextArea] public string description;
        [FieldLabel("달성 조건"), SerializeReference] public Condition condition;
        [FieldLabel("보상"), SerializeReference] public List<Reward> rewards = new List<Reward>();

        public AchievementDefinition ToDefinition()
        {
            var definition = new AchievementDefinition
            {
                Id = Id,
                DisplayName = DisplayName,
                Description = description,
                Condition = condition,
            };
            foreach (var reward in rewards)
                if (reward != null) definition.Rewards.Add(reward);
            return definition;
        }

        public override void ValidateData(IValidationContext context)
        {
            if (condition == null) context.Error("달성 조건이 없습니다.");
            else condition.Validate(context);
            foreach (var reward in rewards)
            {
                if (reward == null) context.Error("비어 있는 보상 칸이 있습니다.");
                else reward.Validate(context);
            }
        }
    }
}
