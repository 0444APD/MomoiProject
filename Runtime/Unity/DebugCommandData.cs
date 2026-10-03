using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GBible.Data
{
    // DB › 개발 명령: 모니터 › 명령 탭의 버튼 하나. 결과 Block(플레이어 데이터에 주기·이벤트 쏘기·로그에 기록·코딩한 결과 …)을 조립한다.
    // 누르면 결과 전체가 행동 하나(DeveloperEditAction)로 실행된다: 게임 행동처럼 타임라인에 남고 Undo된다.
    // 결과를 코드로 쓰려면 이 항목 ID에 [BindTo]를 붙인다(예: "적 모두 처치"처럼 규칙 함수를 부르는 명령).
    // 개발 빌드에서만 등록된다(출시 빌드에는 버튼이 없다).
    [Database("개발 명령", Group = "개발", Order = 10, IdPrefix = "command")]
    public sealed class DebugCommandData : GameData
    {
        [FieldLabel("버튼 이름"), Tooltip("비우면 ID")] public string label = "";
        [FieldLabel("분류"), Tooltip("명령 탭에서 묶이는 이름")] public string category = "개발";
        [FieldLabel("설명")] public string description = "";
        [FieldLabel("결과"), SerializeReference, OutcomeSlot(OutcomeKinds.All)] public List<Outcome> outcomes = new List<Outcome>();

        public string ButtonName => string.IsNullOrEmpty(label) ? DisplayName : label;

        public override void ValidateData(IValidationContext context)
        {
            if (outcomes.Count == 0 && !BoundOutcomes.Has(Id)) context.Error("결과가 없습니다(무엇을 할지).");
            foreach (var outcome in outcomes)
            {
                if (outcome == null) context.Error("비어 있는 결과 칸이 있습니다.");
                else outcome.Validate(context);
            }
        }

        // 세션의 명령 탭에 등록한다(개발 빌드에서만 실제로 등록된다: DebugCommands.Enabled).
        public void Register(GameSession session)
        {
            var id = Id;
            IReadOnlyList<Outcome> run = BoundOutcomes.Has(id) ? BoundOutcomes.Create(id) : outcomes.Where(o => o != null).ToList();
            var name = ButtonName;
            session.Debug.Register(string.IsNullOrEmpty(category) ? "개발" : category, name, () => session.Do(new DeveloperEditAction
            {
                label = name,
                Edit = () => session.Triggers.RunOutcomes(run, new TriggerContext { Session = session, Owner = id }, "Debug"),
            }), string.IsNullOrEmpty(description) ? null : description);
        }
    }
}
