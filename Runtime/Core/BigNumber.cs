using System;
using System.Globalization;

namespace GBible
{
    // 큰 수(방치형·인플레이션 게임): double(1e308까지)을 넘는 값.
    // 필요한 필드에만 쓴다(그 밖의 숫자는 그대로 int·double). 플레이어 데이터에 그냥 두면 저장·Undo·Mirror가 따라온다.
    //   public BigNumber cookies;                    // RunState
    //   run.cookies += production * seconds;          // 연산자는 평소처럼(+ − × ÷ 비교)
    //   if (run.cookies < price) throw Game.Fail(…);  // 비용은 코드로(Game.Pay·DB Block은 double 숫자 필드만 안다)
    //   label.text = run.cookies.ToString();          // "1.23K" … "4.56Qa" … 그 위는 "1.23e45". "ko" 형식은 만·억·조·경…
    // 1e300 아래는 double 값 그대로 담는다 — 1을 49번 더하면 정확히 49(오차가 쌓이지 않는다), 정수는 2^53까지 정확하다.
    // 그 위는 가수(1 ≤ |m| < 10) × 10^지수: 유효 숫자 약 15자리, 큰 값끼리 차이가 17자리 넘게 나면 작은 쪽은 더해도 바뀌지 않는다.
    [Serializable]
    public struct BigNumber : IComparable<BigNumber>, IEquatable<BigNumber>, IFormattable
    {
        // 1e300 아래: m = 값, e = 0. 그 위: m = 가수(1 ≤ |m| < 10), e = 10의 지수(300 이상). 직접 고치지 않는다(만들 때 맞춘다).
        // 예전 저장(작은 값도 가수 × 10^지수로 담음)도 읽은 뒤 셈·비교할 때 맞춘다.
        public double m;
        public long e;

        // 이 크기부터 가수 × 10^지수로 담는다(곱해도 double이 넘치지 않을 만큼 1e308보다 작게).
        const double Limit = 1e300;

        public static readonly BigNumber Zero = default;
        public static readonly BigNumber One = new BigNumber(1);

        public BigNumber(double value)
        {
            Check(value);
            this = Math.Abs(value) < Limit ? Small(value) : Make(value, 0);
        }

        public BigNumber(double mantissa, long exponent)
        {
            Check(mantissa);
            this = Make(mantissa, exponent);
        }

        static void Check(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value), "[MomoiProject] 큰 수: 무한·NaN은 담을 수 없습니다.");
        }

        static BigNumber Small(double value) => new BigNumber { m = value == 0 ? 0 : value, e = 0 };

        // mantissa × 10^exponent를 맞춘 모양으로(1e300 아래는 값 그대로, 그 위는 가수 × 10^지수).
        static BigNumber Make(double mantissa, long exponent)
        {
            if (mantissa == 0) return Zero;
            if (Math.Log10(Math.Abs(mantissa)) + exponent < 300.5)
            {
                var value = Scale(mantissa, exponent);
                if (Math.Abs(value) < Limit) return Small(exponent == 0 ? value : Snap(value));
            }
            var (m, e) = Normalize(mantissa, exponent);
            return new BigNumber { m = m, e = e };
        }

        // mantissa × 10^exponent를 double로(음의 지수는 나누기로 — 10^-n은 double로 정확하지 않다).
        static double Scale(double mantissa, long exponent)
        {
            if (exponent == 0) return mantissa;
            if (exponent > 0) return exponent > 400 ? Math.Sign(mantissa) * double.PositiveInfinity : mantissa * Math.Pow(10, exponent);
            return exponent < -400 ? 0 : mantissa / Math.Pow(10, -exponent);
        }

        // 가수 × 10^지수에서 옮긴 값이 정수에서 반올림 오차(몇 ulp)만큼 벗어나 있으면 그 정수로(1.13 × 100 = 112.99999999999999 → 113).
        static double Snap(double value)
        {
            if (Math.Abs(value) < 1) return value;
            var whole = Math.Round(value);
            return Math.Abs(value - whole) <= Math.Abs(value) * 4e-16 ? whole : value;
        }

        // 가수(1 ≤ |m| < 10)와 지수로.
        static (double m, long e) Normalize(double mantissa, long exponent)
        {
            var shift = (long)Math.Floor(Math.Log10(Math.Abs(mantissa)));
            // 아주 작은 값(1e-300 아래 — 10^n이 double을 넘는다)은 두 번에 나눠 곱한다.
            var m = shift == 0 ? mantissa
                : shift > 0 ? mantissa / Math.Pow(10, shift)
                : -shift > 300 ? mantissa * 1e300 * Math.Pow(10, -shift - 300)
                : mantissa * Math.Pow(10, -shift);
            var e = exponent + shift;
            // 반올림 오차로 10이 되거나 1보다 작아진 것을 바로잡는다.
            if (Math.Abs(m) >= 10) { m /= 10; e++; }
            else if (Math.Abs(m) < 1) { m *= 10; e--; }
            return (m, e);
        }

        // 맞춘 모양(예전 저장의 값도). 대부분(작은 수)은 그대로다.
        BigNumber Canonical => e == 0 && Math.Abs(m) < Limit ? this : e >= 300 && Math.Abs(m) >= 1 && Math.Abs(m) < 10 ? this : Make(m, e);

        // 작은 수(e = 0)인가.
        bool IsSmall => e == 0;

        // 가수(1 ≤ |m| < 10)와 지수(작은 수도). 0이면 (0, 0).
        (double m, long e) Parts() => m == 0 ? (0, 0) : IsSmall ? Normalize(m, 0) : (m, e);

        public bool IsZero => m == 0;
        public int Sign => Math.Sign(m);

        // double로(1e308을 넘으면 무한대).
        public double ToDouble()
        {
            var c = Canonical;
            if (c.m == 0 || c.IsSmall) return c.m;
            return c.e > 308 ? (c.m > 0 ? double.PositiveInfinity : double.NegativeInfinity) : c.m * Math.Pow(10, c.e);
        }

        // 상용로그(0이면 음의 무한대).
        public double Log10()
        {
            var c = Canonical;
            return c.m == 0 ? double.NegativeInfinity : Math.Log10(Math.Abs(c.m)) + c.e;
        }

        public static BigNumber Pow(BigNumber value, double power)
        {
            var c = value.Canonical;
            if (c.m == 0) return power == 0 ? One : Zero;
            if (c.m < 0) throw new ArgumentOutOfRangeException(nameof(value), "[MomoiProject] 큰 수: 음수의 거듭제곱은 지원하지 않습니다.");
            if (c.IsSmall)
            {
                // 작은 수: double 그대로(2^3 = 8, 5^2 = 25 정확히).
                var direct = Math.Pow(c.m, power);
                if (!double.IsInfinity(direct) && !double.IsNaN(direct)) return new BigNumber(direct);
            }
            var log = c.Log10() * power;
            var exponent = (long)Math.Floor(log);
            return Make(Math.Pow(10, log - exponent), exponent);
        }

        // 소수 버림(음의 무한대 쪽). 1e300 위는 소수가 없다.
        public static BigNumber Floor(BigNumber value)
        {
            var c = value.Canonical;
            return c.IsSmall ? new BigNumber(Math.Floor(c.m)) : c;
        }

        public static BigNumber Max(BigNumber a, BigNumber b) => a >= b ? a : b;
        public static BigNumber Min(BigNumber a, BigNumber b) => a <= b ? a : b;

        public static implicit operator BigNumber(double value) => new BigNumber(value);
        public static implicit operator BigNumber(long value) => new BigNumber(value);
        public static implicit operator BigNumber(int value) => new BigNumber(value);
        public static explicit operator double(BigNumber value) => value.ToDouble();

        public static BigNumber operator -(BigNumber a)
        {
            var c = a.Canonical;
            return c.m == 0 ? Zero : new BigNumber { m = -c.m, e = c.e };
        }

        public static BigNumber operator +(BigNumber a, BigNumber b)
        {
            a = a.Canonical;
            b = b.Canonical;
            if (a.m == 0) return b;
            if (b.m == 0) return a;
            // 둘 다 작은 수: double 그대로 더한다(오차가 쌓이지 않는다).
            if (a.IsSmall && b.IsSmall) return new BigNumber(a.m + b.m);
            var (am, ae) = a.Parts();
            var (bm, be) = b.Parts();
            if (ae < be)
            {
                (am, bm) = (bm, am);
                (ae, be) = (be, ae);
            }
            var gap = ae - be;
            if (gap > 17) return Make(am, ae);
            return Make(am + bm / Math.Pow(10, gap), ae);
        }

        public static BigNumber operator -(BigNumber a, BigNumber b) => a + -b;

        public static BigNumber operator *(BigNumber a, BigNumber b)
        {
            a = a.Canonical;
            b = b.Canonical;
            if (a.m == 0 || b.m == 0) return Zero;
            if (a.IsSmall && b.IsSmall)
            {
                var product = a.m * b.m;
                if (!double.IsInfinity(product)) return new BigNumber(product);
            }
            var (am, ae) = a.Parts();
            var (bm, be) = b.Parts();
            return Make(am * bm, ae + be);
        }

        public static BigNumber operator /(BigNumber a, BigNumber b)
        {
            a = a.Canonical;
            b = b.Canonical;
            if (b.m == 0) throw new DivideByZeroException("[MomoiProject] 큰 수: 0으로 나눌 수 없습니다.");
            if (a.m == 0) return Zero;
            if (a.IsSmall && b.IsSmall)
            {
                var quotient = a.m / b.m;
                if (!double.IsInfinity(quotient)) return new BigNumber(quotient);
            }
            var (am, ae) = a.Parts();
            var (bm, be) = b.Parts();
            return Make(am / bm, ae - be);
        }

        public int CompareTo(BigNumber other)
        {
            var a = Canonical;
            var b = other.Canonical;
            if (a.IsSmall && b.IsSmall) return a.m.CompareTo(b.m);
            var sa = Math.Sign(a.m);
            var sb = Math.Sign(b.m);
            if (sa != sb) return sa.CompareTo(sb);
            if (sa == 0) return 0;
            var (am, ae) = a.Parts();
            var (bm, be) = b.Parts();
            if (ae != be) return sa > 0 ? ae.CompareTo(be) : be.CompareTo(ae);
            return am.CompareTo(bm);
        }

        public bool Equals(BigNumber other) => CompareTo(other) == 0;
        public override bool Equals(object obj) => obj is BigNumber other && Equals(other);

        public override int GetHashCode()
        {
            var c = Canonical;
            return c.m == 0 ? 0 : (c.m.GetHashCode() * 397) ^ c.e.GetHashCode();
        }

        public static bool operator ==(BigNumber a, BigNumber b) => a.Equals(b);
        public static bool operator !=(BigNumber a, BigNumber b) => !a.Equals(b);
        public static bool operator <(BigNumber a, BigNumber b) => a.CompareTo(b) < 0;
        public static bool operator >(BigNumber a, BigNumber b) => a.CompareTo(b) > 0;
        public static bool operator <=(BigNumber a, BigNumber b) => a.CompareTo(b) <= 0;
        public static bool operator >=(BigNumber a, BigNumber b) => a.CompareTo(b) >= 0;

        static readonly string[] Short = { "", "K", "M", "B", "T", "Qa", "Qi", "Sx", "Sp", "Oc", "No", "Dc" };
        static readonly string[] Korean = { "", "만", "억", "조", "경", "해", "자", "양", "구", "간", "정", "재", "극" };

        public override string ToString() => ToString(null, CultureInfo.InvariantCulture);
        public string ToString(string format) => ToString(format, CultureInfo.InvariantCulture);

        // 형식: 비우면 "short"(1.23K … 4.56Dc, 그 위는 지수) · "sci"(1.23e45) · "ko"(1.23억 … 그 위는 지수) · "full"(1e15 아래는 소수를 버린 자릿수 그대로).
        public string ToString(string format, IFormatProvider provider)
        {
            provider ??= CultureInfo.InvariantCulture;
            var c = Canonical;
            if (c.m == 0) return "0";
            switch (format)
            {
                case "sci":
                    return Sci(c, provider);
                case "full":
                {
                    if (!c.IsSmall || Math.Abs(c.m) >= 1e15) return Sci(c, provider);
                    var whole = Math.Truncate(c.m);
                    if (whole == 0) whole = 0;   // -0 → 0
                    return whole.ToString("N0", provider);
                }
                case "ko":
                    return Units(c, provider, Korean, 4);
                default:
                    return Units(c, provider, Short, 3);
            }
        }

        // 1.23e45. 반올림이 10에 닿으면 지수를 올린다(9.996e5 → 1e6, 10e5가 아니라).
        static string Sci(BigNumber c, IFormatProvider provider)
        {
            var (m, e) = c.Parts();
            // 소수 둘째 자리 반올림이 1000에 닿으면(999.996) 지수 꼴로(1000이 아니라 1e3).
            if (Math.Abs(e) < 3 && Math.Round(Math.Abs(c.ToDouble()), 2, MidpointRounding.AwayFromZero) < 1000) return c.ToDouble().ToString("0.##", provider);
            var rounded = Math.Round(m, 2, MidpointRounding.AwayFromZero);
            if (Math.Abs(rounded) >= 10)
            {
                rounded /= 10;
                e++;
            }
            return $"{rounded.ToString("0.##", provider)}e{e}";
        }

        // 단위(K·M… 또는 만·억…): 유효 숫자 셋(1.23 · 12.3 · 123)으로 반올림하고, 반올림이 단위 하나를 채우면 다음 단위로(999,600 → 1M, 1000K가 아니라).
        static string Units(BigNumber c, IFormatProvider provider, string[] units, int step)
        {
            var (m, e) = c.Parts();
            if (e < step)
            {
                // 단위 없이 쓰되, 소수 둘째 자리 반올림이 단위 하나를 채우면(999.996) 다음 단위로(1000이 아니라 1K).
                if (Math.Round(Math.Abs(c.ToDouble()), 2, MidpointRounding.AwayFromZero) < Math.Pow(10, step)) return c.ToDouble().ToString("0.##", provider);
                return (m < 0 ? "-" : "") + "1" + units[1];
            }
            var group = e / step;
            var scaled = Math.Abs(m) * Math.Pow(10, e % step);
            var decimals = scaled < 10 ? 2 : scaled < 100 ? 1 : 0;
            var rounded = Math.Round(scaled, decimals, MidpointRounding.AwayFromZero);
            if (rounded >= Math.Pow(10, step))
            {
                group++;
                rounded = 1;
            }
            if (group >= units.Length) return Sci(c, provider);
            var digits = rounded < 10 ? "0.##" : rounded < 100 ? "0.#" : "0";
            return (m < 0 ? "-" : "") + rounded.ToString(digits, provider) + units[group];
        }

        // "1.5e40" · "12345" · "3.2K"(ToString 기본 형식)을 읽는다.
        // 읽지 못하면 거짓(던지지 않는다 — 조건 · 검사의 값 글자처럼 사람이 적은 글).
        public static bool TryParse(string text, out BigNumber value)
        {
            try
            {
                value = Parse(text);
                return true;
            }
            catch (Exception e) when (e is FormatException || e is OverflowException || e is ArgumentException)
            {
                value = Zero;   // "NaN" · "Infinity"(큰 수로 담을 수 없다) 포함
                return false;
            }
        }

        public static BigNumber Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Zero;
            text = text.Trim();
            var at = text.IndexOfAny(new[] { 'e', 'E' });
            if (at > 0)
            {
                var exponent = long.Parse(text.Substring(at + 1), CultureInfo.InvariantCulture);
                // double로 담기는 크기는 글자 그대로 읽는다(1.13e2 → 정확히 113).
                if (Math.Abs(exponent) < 290 && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var exact) && !double.IsInfinity(exact))
                    return new BigNumber(exact);
                return new BigNumber(double.Parse(text.Substring(0, at), CultureInfo.InvariantCulture), exponent);
            }
            for (var i = Short.Length - 1; i > 0; i--)
                if (text.EndsWith(Short[i], StringComparison.Ordinal))
                    return new BigNumber(double.Parse(text.Substring(0, text.Length - Short[i].Length), CultureInfo.InvariantCulture), i * 3);
            return new BigNumber(double.Parse(text, CultureInfo.InvariantCulture));
        }
    }
}
