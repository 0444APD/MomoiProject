using System.Collections.Generic;
using System.Linq;
using GBible.Data;
using GBible.EditorTools;
using GBible.EditorTools.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GBible.Tests
{
    // [ShowIf] 판정(ShowIfRules)·값 칸 목록(ValueChoices)·가중치 표 검증.
    public class ShowIfRuleTests
    {
        readonly List<Object> _created = new List<Object>();

        T Make<T>(string id) where T : GameData
        {
            var data = ScriptableObject.CreateInstance<T>();
            data.EditorAssignId(id);
            _created.Add(data);
            return data;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void 조건_칸이_숨으면_딸린_칸도_숨는다_객체()
        {
            var curve = new Curve { kind = CurveKind.None, useMin = true };
            var min = typeof(Curve).GetField(nameof(Curve.min));
            Assert.IsFalse(ShowIfRules.Visible(curve, min), "방식이 없음이면 '최솟값으로 자르기'가 숨으니 '최솟값'도 숨는다");
            curve.kind = CurveKind.Linear;
            Assert.IsTrue(ShowIfRules.Visible(curve, min));
            curve.useMin = false;
            Assert.IsFalse(ShowIfRules.Visible(curve, min));
        }

        [Test]
        public void 조건_칸이_숨으면_딸린_칸도_숨는다_칸()
        {
            var balance = Make<BalanceData>("balance1");
            balance.curve = new Curve { kind = CurveKind.None, useMin = true };
            var so = new SerializedObject(balance);
            Assert.IsFalse(ShowIfRules.Visible(so.FindProperty("curve.min")));
            balance.curve.kind = CurveKind.Power;
            so.Update();
            Assert.IsTrue(ShowIfRules.Visible(so.FindProperty("curve.min")));
            Assert.IsTrue(ShowIfRules.Visible(so.FindProperty("curve.power")));
            Assert.IsFalse(ShowIfRules.Visible(so.FindProperty("curve.step")));
        }

        [Test]
        public void 다른_칸의_스위치를_알아본다()
        {
            Assert.IsTrue(ShowIfRules.IsSwitch(typeof(WeightTableEntry).GetField(nameof(WeightTableEntry.weightBalance))));
            Assert.IsFalse(ShowIfRules.IsSwitch(typeof(WeightTableEntry).GetField(nameof(WeightTableEntry.weight))));
        }

        [Test]
        public void enum_목록은_보이는_이름으로_값은_enum으로()
        {
            var choices = ValueChoices.For(null, typeof(CurveKind));
            var none = choices.First(c => Equals(c.Value, CurveKind.None));
            Assert.AreEqual("없음(고정)", none.Label);
            Assert.AreEqual("없음(고정)", ValueChoices.Display(null, CurveKind.None));
            Assert.AreEqual("없음(고정)", ValueChoices.EnumLabel(typeof(CurveKind), nameof(CurveKind.None)));
        }

        [Test]
        public void 밸런스_값으로_무게를_정하는_줄은_무게_0으로_보지_않는다()
        {
            var table = Make<WeightTableData>("table1");
            table.entries.Add(new WeightTableEntry { item = "table1x", weight = 0, weightBalance = "balance1" });
            var issues = DataValidator.Validate(new List<GameData> { table }).Where(i => i.Asset == table).Select(i => i.Message).ToList();
            Assert.IsFalse(issues.Any(m => m.Contains("모두 0")), string.Join("\n", issues));

            table.entries[0].weightBalance = "";
            issues = DataValidator.Validate(new List<GameData> { table }).Where(i => i.Asset == table).Select(i => i.Message).ToList();
            Assert.IsTrue(issues.Any(m => m.Contains("모두 0")), string.Join("\n", issues));   // "무게·%가 모두 0이라 아무것도 나오지 않습니다."
        }

        [Test]
        public void 숨은_칸과_스위치_칸은_비어도_경고하지_않는다()
        {
            var table = Make<WeightTableData>("table1");
            // 목록 줄: 항목 칸은 숨고(종류 = 플레이어 데이터 목록), 밸런스 칸은 스위치(비면 끔).
            table.entries.Add(new WeightTableEntry { source = WeightEntrySource.List, listKey = "run", weight = 1 });
            var warnings = DataValidator.Validate(new List<GameData> { table })
                .Where(i => i.Asset == table && i.Severity == ValidationSeverity.Warning).Select(i => i.Message).ToList();
            Assert.IsFalse(warnings.Any(m => m.Contains("선택되지 않았습니다")), string.Join("\n", warnings));
        }
    }
}
