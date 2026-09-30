using System;
using System.Collections.Generic;
using System.Linq;
using GameSystems.Dialogue;
using UnityEngine;

namespace GameSystems.Data
{
    // 대화용 스탠딩 프로필(대화 시스템 전용, 게임의 캐릭터 데이터와 따로 관리한다). 스탠딩 목록(ID → 스프라이트)을 두고,
    // 대화 그래프에서는 "프로필 ID + 스탠딩 ID"로 고른다. 대사의 화자도 이 프로필 ID다.
    // 이름은 텍스트 시트의 '<id>.name'. 소속 같은 프로젝트별 글은 '추가 글'에 키로 적는다(UI에 같은 키의 요소를 두면 보인다).
    [Database("스탠딩 프로필", Group = "대화", Order = 0, IdPrefix = "profile")]
    public sealed class StandingProfileData : GameData
    {
        // 스탠딩 하나(표정·의상·포즈 등). 필드 이름 expressions는 저장 호환을 위해 그대로 둔다.
        [Serializable]
        public sealed class Expression
        {
            [FieldLabel("스탠딩 ID")] public string id = "default";
            [FieldLabel("스프라이트")] public Sprite sprite;
        }

        [FieldLabel("스탠딩 목록(스탠딩 ID · 스프라이트)")] public List<Expression> expressions = new List<Expression> { new Expression() };
        [FieldLabel("기본 스탠딩 ID(비우면 첫 스탠딩)")] public string defaultExpression = "";
        [FieldLabel("이름 색")] public Color nameColor = Color.white;
        [FieldLabel("스탠딩 프리팹(StandingView가 붙은 프리팹, 스파인 등. 비우면 스탠딩 목록의 스프라이트로 그린다)")] public GameObject standingPrefab;
        [FieldLabel("스탠딩 크기 배율"), Min(0.01f)] public float standingScale = 1f;
        [FieldLabel("추가 글(소속·직함 등, UI의 dialogue-extra-<키> 요소에 보인다)")] public List<ExtraText> extras = new List<ExtraText>();

        public IEnumerable<string> StandingIds => expressions.Select(e => e.id).Where(id => !string.IsNullOrEmpty(id));

        public Sprite SpriteOf(string standing)
        {
            var found = expressions.FirstOrDefault(e => e.id == standing)
                        ?? expressions.FirstOrDefault(e => e.id == defaultExpression)
                        ?? expressions.FirstOrDefault();
            return found?.sprite;
        }

        public string Extra(string key) => extras.FirstOrDefault(e => e.key == key)?.text;

        public override void ValidateData(IValidationContext context)
        {
            foreach (var duplicate in expressions.GroupBy(e => e.id).Where(g => g.Count() > 1))
                context.Error($"스탠딩 ID '{duplicate.Key}'가 겹칩니다.");
            if (!string.IsNullOrEmpty(defaultExpression) && expressions.All(e => e.id != defaultExpression))
                context.Error($"기본 스탠딩 '{defaultExpression}'이(가) 스탠딩 목록에 없습니다.");
            foreach (var duplicate in extras.GroupBy(e => e.key).Where(g => g.Count() > 1))
                context.Error($"추가 글 키 '{duplicate.Key}'가 겹칩니다.");
        }
    }
}

namespace GameSystems.Dialogue
{
    // 프로젝트마다 더하는 글(소속·직함·장소 등). 키가 같은 UI 요소(dialogue-extra-<키>)에 보인다.
    // 번역: 캐릭터 글은 '<캐릭터 ID>.<키>', 대사 글은 '<대사 키>.<키>' 텍스트가 있으면 그것을 쓴다.
    [Serializable]
    public sealed class ExtraText
    {
        [FieldLabel("키(영문, 예: affiliation)")] public string key = "";
        [FieldLabel("글(문장 틀 문법 가능)"), TextArea(1, 4)] public string text = "";
    }

    // 문자열 칸이 어느 스탠딩 프로필의 스탠딩 ID인지. characterField: 같은 노드에서 프로필 ID를 담은 칸 이름(비어 있으면 현재 화자).
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class StandingRefAttribute : Attribute
    {
        public string CharacterField { get; }

        public StandingRefAttribute(string characterField)
        {
            CharacterField = characterField;
        }
    }
}
