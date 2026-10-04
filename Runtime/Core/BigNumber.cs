using System;
using System.Globalization;

namespace GBible
{
    // 큰 수(방치형·인플레이션 게임): double(1e308까지)을 넘는 값. 가수(1 ≤ |m| < 10) × 10^지수로 둔다.
    // 필요한 필드에만 쓴다(그 밖의 숫자는 그대로 int·double). 플레이어 데이터에 그냥 두면 저장·Undo·Mirror가 따라온다.
    //   public BigNumber cookies;                    // RunState
    //   run.cookies += production * seconds;          // 연산자는 평소처럼(+ − × ÷ 비교)
    //   if (run.cookies < price) throw Game.Fail(…);  // 비용은 코드로(Game.Pay·DB Block은 double 숫자 필드만 안다)
    //   label.text = run.cookies.ToString();          // "1.23K" … "4.56Qa" … 그 위는 "1.23e45". "ko" 형식은 만·억·조·경…
    // 정밀도는 double의 가수(유효 숫자 약 15자리)다. 큰 값끼리 차이가 17자리 넘게 나면 작은 쪽은 더해도 바뀌지 않는다.
    [Serializable]
    public struct BigNumber : IComparable<BigNumber>, IEquatable<BigNumber>, IFormattable
    {
        // 가수(0이거나 1 ≤ |m| < 10)와 10의 지수. 직접 고치지 않는다(만들 때 맞춘다).
        public double m;
        public long e;

        public static readonly BigNumber Zero = default;
        public static readonly BigNumber One = new BigNumber(1);

        public BigNumber(double value) : this(value, 0)
        {
        }

        public BigNumber(double mantissa, long exponent)
        {
            if (double.IsNaN(mantissa) || double.IsInfinity(mantissa)) throw new ArgumentOutOfRangeException(nameof(mantissa), "[MomoiProject] 큰 수: 무한·NaN은 담을 수 없습니다.");
            if (mantissa == 0)
            {
                m = 0;
                e = 0;
                return;
            }
            var shift = (long)Math.Floor(Math.Log10(Math.Abs(mantissa)));
            m = mantissa / Math.Pow(10, shift);
            e = exponent + shift;
            // 반올림 오차로 10이 되거나 1보다 작아진 것을 바로잡는다.
            if (Math.Abs(m) >= 10) { m /= 10; e++; }
            else if (Math.Abs(m) < 1) { m *= 10; e--; }
        }

        public bool IsZero => m == 0;
        public int Sign => Math.Sign(m);

        // double로(1e308을 넘으면 무한대).
        public double ToDouble() => m == 0 ? 0 : e > 308 ? (m > 0 ? double.PositiveInfinity : double.NegativeInfinity) : e < -324 ? 0 : m * Math.Pow(10, e);

        // 상용로그(0이면 음의 무한대).
        public double Log10() => m == 0 ? double.NegativeInfinity : Math.Log10(Math.Abs(m)) + e;

        public static BigNumber Pow(BigNumber value, double power)
        {
            if (value.m == 0) return power == 0 ? One : Zero;
            if (value.m < 0) throw new ArgumentOutOfRangeException(nameof(value), "[MomoiProject] 큰 수: 음수의 거듭제곱은 지원하지 않습니다.");
            var log = value.Log10() * power;
            var exponent = (long)Math.Floor(log);
            return new BigNumber(Math.Pow(10, log - exponent), exponent);
        }

        public static BigNumber Floor(BigNumber value) => value.e >= 15 ? value : new BigNumber(Math.Floor(value.ToDouble()));

        public static BigNumber Max(BigNumber a, BigNumber b) => a >= b ? a : b;
        public static BigNumber Min(BigNumber a, BigNumber b) => a <= b ? a : b;

        public static implicit operator BigNumber(double value) => new BigNumber(value);
        public static implicit operator BigNumber(long value) => new BigNumber(value);
        public static implicit operator BigNumber(int value) => new BigNumber(value);
        public static explicit operator double(BigNumber value) => value.ToDouble();

        public static BigNumber operator -(BigNumber a) => new BigNumber(-a.m, a.e);

        public static BigNumber operator +(BigNumber a, BigNumber b)
        {
            if (a.m == 0) return b;
            if (b.m == 0) return a;
            if (a.e < b.e) (a, b) = (b, a);
            var gap = a.e - b.e;
            if (gap > 17) return a;
            return new BigNumber(a.m + b.m / Math.Pow(10, gap), a.e);
        }

        public static BigNumber operator -(BigNumber a, BigNumber b) => a + -b;
        public static BigNumber operator *(BigNumber a, BigNumber b) => a.m == 0 || b.m == 0 ? Zero : new BigNumber(a.m * b.m, a.e + b.e);

        public static BigNumber operator /(BigNumber a, BigNumber b)
        {
            if (b.m == 0) throw new DivideByZeroException("[MomoiProject] 큰 수: 0으로 나눌 수 없습니다.");
            return a.m == 0 ? Zero : new BigNumber(a.m / b.m, a.e - b.e);
        }

        public int CompareTo(BigNumber other)
        {
            if (Sign != other.Sign) return Sign.CompareTo(other.Sign);
            if (m == 0) return 0;
            if (e != other.e) return Sign > 0 ? e.CompareTo(other.e) : other.e.CompareTo(e);
            return m.CompareTo(other.m);
        }

        public bool Equals(BigNumber other) => CompareTo(other) == 0;
        public override bool Equals(object obj) => obj is BigNumber other && Equals(other);
        public override int GetHashCode() => m == 0 ? 0 : (m.GetHashCode() * 397) ^ e.GetHashCode();

        public static bool operator ==(BigNumber a, BigNumber b) => a.Equals(b);
        public static bool operator !=(BigNumber a, BigNumber b) => !a.Equals(b);
        public static bool operator <(BigNumber a, BigNumber b) => a.CompareTo(b) < 0;
        public static bool operator >(BigNumber a, BigNumber b) => a.CompareTo(b) > 0;
        public static bool operator <=(BigNumber a, BigNumber b) => a.CompareTo(b) <= 0;
        public static bool operator >=(BigNumber a, BigNumber b) => a.CompareTo(b) >= 0;

        static readonly string[] Short = { "", "K", "M", "B", "T", "Qa", "Qi", "Sx", "Sp", "Oc", "No", "Dc" };
        static readonly string[] Korean = { "", "만", "억", "조", "경", "해", "자", "양", "구", "간", "정", "재", "극" };

        public override string ToString() => ToString(null, CultureInfo.InvariantCulture);

        // 형식: 비우면 "short"(1.23K … 4.56Dc, 그 위는 지수) · "sci"(1.23e45) · "ko"(1.23억 … 그 위는 지수) · "full"(double로 되면 자릿수 그대로).
        public string ToString(string format, IFormatProvider provider)
        {
            provider ??= CultureInfo.InvariantCulture;
            if (m == 0) return "0";
            switch (format)
            {
                case "sci":
                    return Math.Abs(e) < 3 ? ToDouble().ToString("0.##", provider) : $"{m.ToString("0.##", provider)}e{e}";
                case "full":
                    return e < 15 ? Math.Floor(ToDouble()).ToString("N0", provider) : ToString("sci", provider);
                case "ko":
                {
                    if (e < 4) return ToDouble().ToString("0.##", provider);
                    var group = e / 4;
                    if (group >= Korean.Length) return ToString("sci", provider);
                    var scaled = m * Math.Pow(10, e % 4);
                    return scaled.ToString(scaled < 10 ? "0.##" : scaled < 100 ? "0.#" : "0", provider) + Korean[group];
                }
                default:
                {
                    if (e < 3) return ToDouble().ToString("0.##", provider);
                    var group = e / 3;
                    if (group >= Short.Length) return ToString("sci", provider);
                    var scaled = m * Math.Pow(10, e % 3);
                    return scaled.ToString(scaled < 10 ? "0.##" : scaled < 100 ? "0.#" : "0", provider) + Short[group];
                }
            }
        }

        // "1.5e40" · "12345" · "3.2K"(ToString 기본 형식)을 읽는다.
        public static BigNumber Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Zero;
            text = text.Trim();
            var at = text.IndexOfAny(new[] { 'e', 'E' });
            if (at > 0) return new BigNumber(double.Parse(text.Substring(0, at), CultureInfo.InvariantCulture), long.Parse(text.Substring(at + 1), CultureInfo.InvariantCulture));
            for (var i = Short.Length - 1; i > 0; i--)
                if (text.EndsWith(Short[i], StringComparison.Ordinal))
                    return new BigNumber(double.Parse(text.Substring(0, text.Length - Short[i].Length), CultureInfo.InvariantCulture), i * 3);
            return new BigNumber(double.Parse(text, CultureInfo.InvariantCulture));
        }
    }
}
