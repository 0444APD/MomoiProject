using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GBible
{
    // 밸런스 값(DB › 밸런스 값): 기획자가 조정하는 이름 붙은 숫자(시작 에너지 3, 엘리트 확률 0.2 …). 코드는 숫자를 직접 쓰지 않고 이름으로 읽는다.
    //   var energy = Game.Balance(GameIds.Balance.StartEnergy);
    // 곡선(Curve)을 붙이면 단계(레벨·층·웨이브)에 따라 값이 변한다: 기준 값이 시작 단계의 값이다.
    //   var hp = Game.Balance(GameIds.Balance.EnemyHp, floor);
    // 에디터에서 플레이 중에 값을 고치면 바로 반영된다(다음에 읽을 때부터).
    public enum CurveKind
    {
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("없음(고정)")]
#endif
        None,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("일정하게(+단계마다)")]
#endif
        Linear,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("비율로(×단계마다)")]
#endif
        Exponential,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("거듭제곱(단계^지수)")]
#endif
        Power,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("표(점 사이를 잇기)")]
#endif
        Table,
    }

    public enum CurveRounding
    {
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("그대로")]
#endif
        None,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("반올림")]
#endif
        Round,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("내림")]
#endif
        Floor,
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("올림")]
#endif
        Ceiling,
    }

    [Serializable]
    public sealed class CurvePoint
    {
        [FieldLabel("단계")] public double x;
        [FieldLabel("값")] public double y;
    }

    // 곡선: 기준 값(시작 단계의 값)에서 단계에 따라 변하는 값.
    //   일정하게: 기준 + 증가량 × (단계 − 시작)        예: 체력 100, +15/층 → 3층 130
    //   비율로:   기준 × 비율^(단계 − 시작)            예: 100, ×1.1/층 → 3층 121
    //   거듭제곱: 기준 × (단계 / 시작)^지수             예: 경험치 100, 지수 1.5 → 4레벨 800
    //   표:       점들 사이를 직선으로 잇는다(처음 점 앞·마지막 점 뒤는 끝 값 유지). 기준 값은 쓰지 않는다.
    // 계산한 뒤 반올림하고, 켜 둔 쪽(최솟값·최댓값)으로 자른다.
    [Serializable]
    public sealed class Curve
    {
        [FieldLabel("방식")] public CurveKind kind = CurveKind.None;
        [FieldLabel("시작 단계"), ShowIf(nameof(kind), CurveKind.Linear, CurveKind.Exponential, CurveKind.Power)] public double startX = 1;
        [FieldLabel("단계마다 더할 값"), ShowIf(nameof(kind), CurveKind.Linear)] public double step;
        [FieldLabel("단계마다 곱할 비율(1.1 = +10%)"), ShowIf(nameof(kind), CurveKind.Exponential)] public double rate = 1.1;
        [FieldLabel("지수"), ShowIf(nameof(kind), CurveKind.Power)] public double power = 1.5;
        [FieldLabel("점"), ShowIf(nameof(kind), CurveKind.Table)] public List<CurvePoint> points = new List<CurvePoint>();
        [FieldLabel("반올림"), ShowIf(nameof(kind), CurveKind.Linear, CurveKind.Exponential, CurveKind.Power, CurveKind.Table)] public CurveRounding rounding = CurveRounding.None;
        [FieldLabel("최솟값으로 자르기"), ShowIf(nameof(kind), CurveKind.Linear, CurveKind.Exponential, CurveKind.Power, CurveKind.Table)] public bool useMin;
        [FieldLabel("최솟값"), ShowIf(nameof(useMin))] public double min;
        [FieldLabel("최댓값으로 자르기"), ShowIf(nameof(kind), CurveKind.Linear, CurveKind.Exponential, CurveKind.Power, CurveKind.Table)] public bool useMax;
        [FieldLabel("최댓값"), ShowIf(nameof(useMax))] public double max;

        public double Evaluate(double baseValue, double x)
        {
            double value;
            switch (kind)
            {
                case CurveKind.Linear: value = baseValue + step * (x - startX); break;
                case CurveKind.Exponential: value = baseValue * Math.Pow(rate, x - startX); break;
                case CurveKind.Power: value = startX > 0 ? baseValue * Math.Pow(Math.Max(0, x) / startX, power) : baseValue; break;
                case CurveKind.Table: value = Interpolate(x, baseValue); break;
                default: value = baseValue; break;
            }
            switch (rounding)
            {
                case CurveRounding.Round: value = Math.Round(value, MidpointRounding.AwayFromZero); break;
                case CurveRounding.Floor: value = Math.Floor(value); break;
                case CurveRounding.Ceiling: value = Math.Ceiling(value); break;
            }
            if (useMin) value = Math.Max(min, value);
            if (useMax) value = Math.Min(max, value);
            return value;
        }

        double Interpolate(double x, double fallback)
        {
            var sorted = points.Where(p => p != null).OrderBy(p => p.x).ToList();
            if (sorted.Count == 0) return fallback;
            if (x <= sorted[0].x) return sorted[0].y;
            for (int i = 1; i < sorted.Count; i++)
            {
                var a = sorted[i - 1];
                var b = sorted[i];
                if (x > b.x) continue;
                return b.x == a.x ? b.y : a.y + (b.y - a.y) * (x - a.x) / (b.x - a.x);
            }
            return sorted[sorted.Count - 1].y;
        }

        // 미리보기 글: "1 → 100 · 2 → 115 · …".
        public string Preview(double baseValue, int count = 5)
        {
            var parts = new List<string>();
            for (int i = 0; i < count; i++)
            {
                var x = startX + i;
                parts.Add($"{Format(x)} → {Format(Evaluate(baseValue, x))}");
            }
            return string.Join(" · ", parts);
        }

        public void Validate(IValidationContext context, string label)
        {
            if (kind == CurveKind.Power && startX <= 0) context.Error($"{label}: 거듭제곱은 시작 단계가 0보다 커야 합니다.");
            if (kind == CurveKind.Exponential && rate <= 0) context.Error($"{label}: 비율은 0보다 커야 합니다(1.1 = 단계마다 +10%).");
            if (kind == CurveKind.Table && points.Count == 0) context.Error($"{label}: 표에 점이 없습니다.");
            if (useMin && useMax && min > max) context.Error($"{label}: 최솟값이 최댓값보다 큽니다.");
        }

        static string Format(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    public sealed class BalanceSystem
    {
        readonly Dictionary<string, (double value, Curve curve)> _values = new Dictionary<string, (double, Curve)>(StringComparer.Ordinal);

        public void Define(string id, double value, Curve curve = null)
        {
            if (string.IsNullOrEmpty(id)) return;
            _values[id] = (value, curve);
        }

        public bool Has(string id) => id != null && _values.ContainsKey(id);
        public IEnumerable<string> Ids => _values.Keys;

        public double Get(string id) => Find(id).value;

        public double Get(string id, double x)
        {
            var (value, curve) = Find(id);
            return curve == null ? value : curve.Evaluate(value, x);
        }

        // 단계 from부터 count개(단계 1씩)의 값을 더한다. "10개 사기" = Sum(가격, 산 개수, 10).
        public double Sum(string id, double from, int count)
        {
            var total = 0.0;
            for (var i = 0; i < count; i++) total += Get(id, from + i);
            return total;
        }

        // budget 안에서 단계 from부터 몇 개를 살 수 있나("살 수 있는 만큼"). limit개까지만 센다.
        public int MaxCount(string id, double from, double budget, int limit = 10000)
        {
            var count = 0;
            var spent = 0.0;
            while (count < limit)
            {
                var next = Get(id, from + count);
                if (spent + next > budget) break;
                spent += next;
                count++;
            }
            return count;
        }

        (double value, Curve curve) Find(string id) =>
            id != null && _values.TryGetValue(id, out var found)
                ? found
                : throw new InvalidOperationException($"[MomoiProject] 없는 밸런스 값 '{id}'입니다(DB › 밸런스 값).");
    }
}
