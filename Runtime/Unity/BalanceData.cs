using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GBible.Data
{
    // DB › 밸런스 값: 기획자가 조정하는 이름 붙은 숫자. 코드는 Game.Balance(GameIds.Balance.X)로 읽는다(곡선이 있으면 Game.Balance(id, 단계)).
    // 플레이 중에 고치면 바로 반영된다(다음에 읽을 때부터).
    [Database("밸런스 값", Group = "DB", Order = 40, IdPrefix = "balance")]
    [CreateAssetMenu(menuName = "MomoiProject/밸런스 값", fileName = "NewBalance")]
    public sealed class BalanceData : GameData
    {
        [FieldLabel("값"), Tooltip("곡선이 있으면 시작 단계의 값")] public double value;
        [FieldLabel("곡선(단계별)"), Tooltip("레벨·층·웨이브에 따라 변하는 값. 없음이면 늘 '값'")] public Curve curve = new Curve();
        [FieldLabel("메모"), TextArea(1, 4)] public string note = "";

        public Curve ActiveCurve => curve != null && curve.kind != CurveKind.None ? curve : null;

        public void Define(BalanceSystem balance) => balance.Define(Id, value, ActiveCurve);

        public override void ValidateData(IValidationContext context) => ActiveCurve?.Validate(context, "곡선");
    }
}
