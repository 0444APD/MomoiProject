using System;

namespace GBible
{
    // 확률: "25% 확률로", "1/4 확률로". 굴리기 전에 의도 '확률'(ChanceIntent)을 거쳐 행운·"모든 확률 2배"·"확률 무시"가 고친다.
    //   코드: if (Game.Chance(0.25, "loot", source: GameIds.Passive.EightBall)) …
    //   DB: 조건 '확률'(이벤트 받기·가로채기·사용 트리거와 AND).
    // 난수는 흐름 이름마다 따로(결정론, Undo·이어 하기와 같다). 의도가 확률을 바꿔도 난수는 한 번 굴린다(같은 시드면 같은 순서).

    // 기본 제공 의도: 확률. "모든 확률 2배": probability 곱하기 2 · "행운 1당 +1%": probability 더하기(코드) · 반드시 실패: 막기.
    [Serializable, Intent("확률")]
    public sealed class ChanceIntent
    {
        public double probability;   // 0~1(1 이상이면 반드시 성공)
        public string stream;        // 난수 흐름
        public string sourceId;      // 굴리는 쪽(항목 ID)
        public string userId;        // 사용자·가진 쪽 id
        public bool cancelled;       // 참이면 실패
    }

    // 조건 '확률': 이 확률로 참. 이벤트를 받는 순간·가로채는 순간·쓰는 순간에만 굴린다.
    //   '쓸 수 있나' 미리 볼 때(Game.CanUse·버튼 흐리게)는 굴리지 않고 참, 이벤트 없이 값만 보는 '조건 충족'·행동 밖에서는 거짓.
    [Serializable]
    [BlockInfo("확률", Category = "조건", Description = "이 확률로 참입니다. 굴리기 전에 의도 '확률'을 거쳐 행운·'모든 확률 2배'가 고칩니다. 이벤트 받기·가로채기·사용 트리거와 AND로 씁니다(조건 충족만으로는 굴리지 않습니다).",
               Sentence = "{percent}% 확률")]
    public sealed class ChanceCondition : SimpleCondition
    {
        [FieldLabel("확률(%)")] public double percent = 25;
        [FieldLabel("난수 흐름(비우면 chance)")] public string stream = "";

        protected override bool Check()
        {
            var moment = Context;
            if (moment == null || Game == null) return false;
            if (moment.IsCheck) return true;
            if (moment.Event == null && !moment.IsUse) return false;
            // 행동 밖에서는 굴리지 않는다(결정론 난수는 행동 안에서만). 공짜 성공이 되지 않게 거짓.
            if (!Game.Session.Data.InTransaction) return false;
            return Game.Chance(percent / 100.0, stream, moment.Owner, moment.User);
        }

        public override string Describe(INameResolver names) => percent.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "% 확률";

        public override void Validate(IValidationContext context)
        {
            if (percent <= 0 || percent > 100) context.Warning("확률: 0보다 크고 100 이하로 적으세요.");
        }
    }

    // 조건 '사용자 필드 검사': 사용자(가진 쪽·놓인 조각)의 필드를 비교한다 — "이 조커의 카운터가 6 이상"·"이 아티팩트의 처치 수 40 이상".
    [Serializable]
    [BlockInfo("사용자 필드 검사", Category = "조건", Description = "사용자(가진 쪽·놓인 조각·능력을 쓴 개체)의 필드를 비교합니다. 예: 이 조커의 counter 이상 6, 이 아티팩트의 kills 이상 40. 값에 틀({event:경로}·{data:키.경로}·{level} …)을 쓸 수 있습니다.",
               Sentence = "사용자의 {path} {op} {value}")]
    public sealed class UserFieldCondition : SimpleCondition
    {
        [FieldLabel("필드(사용자의)")] public string path = "";
        [FieldLabel("비교")] public FieldOp op = FieldOp.AtLeast;
        [FieldLabel("값(틀 가능)")] public string value = "";

        protected override bool Check()
        {
            var user = Context?.User;
            if (user == null || string.IsNullOrEmpty(path)) return false;
            return FieldOps.Test(user, path, op, ModifyIntentOutcome.Expand(value, Context));
        }

        public override string Describe(INameResolver names) => "사용자의 " + FieldOps.Sentence(path, op, value);

        public override void Validate(IValidationContext context)
        {
            if (string.IsNullOrEmpty(path)) context.Error("사용자 필드 검사: 필드를 적으세요.");
        }
    }
}
