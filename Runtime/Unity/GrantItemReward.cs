using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace GameSystems.Data
{
    // 아이템 지급: 템플릿의 사본을 플레이어 데이터 안의 리스트(예: 인벤토리 › items)에 넣는다.
    [Serializable]
    [BlockInfo("아이템 지급", Category = "플레이어 데이터", Description = "템플릿의 사본을 플레이어 데이터 안의 리스트(예: 인벤토리의 items)에 넣습니다.",
        Sentence = "{key}.{path} ← {template} ×{count}")]
    public sealed class GrantItemReward : Reward
    {
        [StateKeyRef, FieldPathRoot(FieldPathRootKind.StateKey), FieldLabel("플레이어 데이터")] public string key;
        [FieldPath, FieldLabel("리스트 위치(비우면 값 자체)")] public string path = "";
        [DataRef(typeof(TemplateData)), FieldLabel("템플릿")] public string template;
        [FieldLabel("개수")] public int count = 1;

        public override RewardEffect Effect => RewardEffect.Reversible;

        public override void Grant(GameSession session)
        {
            var source = GameDatabase.Load()?.Entries.OfType<TemplateData>().FirstOrDefault(t => t.Id == template);
            if (source == null)
            {
                Debug.LogWarning($"[GameSystems] 아이템 지급: 템플릿 '{template}'을 찾을 수 없습니다.");
                return;
            }
            session.State.Modify<object>(key, root =>
            {
                var target = string.IsNullOrEmpty(path) ? root : FieldPaths.Resolve(root, path).FirstOrDefault();
                if (!(target is IList list) || list.IsFixedSize)
                {
                    Debug.LogWarning($"[GameSystems] 아이템 지급: '{key}.{path}'가 리스트가 아닙니다(배열은 크기가 고정되어 넣을 수 없습니다).");
                    return;
                }
                for (int i = 0; i < Math.Max(1, count); i++) list.Add(source.CreateInstance());
            });
        }

        public override string Describe(INameResolver names) =>
            $"{key}{(string.IsNullOrEmpty(path) ? "" : "." + path)} ← {template} ×{Math.Max(1, count)}";

        public override string Natural(INameResolver names) => $"{template} {Math.Max(1, count)}개 지급";

        public override void Validate(IValidationContext context)
        {
            if (string.IsNullOrEmpty(key)) context.Error("아이템을 넣을 플레이어 데이터가 선택되지 않았습니다.");
            if (string.IsNullOrEmpty(template)) context.Error("지급할 템플릿이 선택되지 않았습니다.");
            if (count < 1) context.Error("개수는 1 이상이어야 합니다.");
        }
    }
}
