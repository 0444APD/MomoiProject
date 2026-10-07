using System;
using System.Runtime.InteropServices;
using System.Text;

namespace GBible
{
    // Mirror가 쓰는 바이트 버퍼. 숫자는 가변 길이(작은 수는 1바이트), 글자는 UTF-8.
    // 같은 값은 언제나 같은 바이트가 되므로 비교·동기화·되돌리기가 바이트 단위로 맞는다.
    public sealed class MirrorWriter
    {
        public byte[] Data;
        public int Length;

        public MirrorWriter(int capacity = 256)
        {
            Data = new byte[Math.Max(16, capacity)];
        }

        void Ensure(int more)
        {
            if (Length + more <= Data.Length) return;
            var next = new byte[Math.Max(Data.Length * 2, Length + more)];
            Buffer.BlockCopy(Data, 0, next, 0, Length);
            Data = next;
        }

        public void Clear() => Length = 0;

        public byte[] ToArray()
        {
            var bytes = new byte[Length];
            Buffer.BlockCopy(Data, 0, bytes, 0, Length);
            return bytes;
        }

        public void Byte(byte v)
        {
            Ensure(1);
            Data[Length++] = v;
        }

        public void UVar(ulong v)
        {
            Ensure(10);
            while (v >= 0x80)
            {
                Data[Length++] = (byte)(v | 0x80);
                v >>= 7;
            }
            Data[Length++] = (byte)v;
        }

        public void Long(long v) => UVar((ulong)((v << 1) ^ (v >> 63)));
        public void Int(int v) => Long(v);
        public void Bool(bool v) => Byte(v ? (byte)1 : (byte)0);

        public void Float(float v)
        {
            var bits = new FloatBits { F = v }.I;
            Ensure(4);
            Data[Length++] = (byte)bits;
            Data[Length++] = (byte)(bits >> 8);
            Data[Length++] = (byte)(bits >> 16);
            Data[Length++] = (byte)(bits >> 24);
        }

        public void Double(double v)
        {
            var bits = BitConverter.DoubleToInt64Bits(v);
            Ensure(8);
            for (var i = 0; i < 8; i++) Data[Length++] = (byte)(bits >> (8 * i));
        }

        public void Str(string v)
        {
            if (v == null)
            {
                UVar(0);
                return;
            }
            if (HasLoneSurrogate(v))
            {
                StrWithLoneSurrogates(v);
                return;
            }
            var count = Encoding.UTF8.GetByteCount(v);
            UVar((ulong)count + 1);
            Ensure(count);
            Length += Encoding.UTF8.GetBytes(v, 0, v.Length, Data, Length);
        }

        // 짝을 잃은 서러게이트(U+D800~DFFF 한 글자)가 있는지. UTF-8은 그 글자를 U+FFFD로 바꾸므로 따로 쓴다.
        static bool HasLoneSurrogate(string v)
        {
            for (var i = 0; i < v.Length; i++)
                if (IsLoneAt(v, ref i)) return true;
            return false;
        }

        // 짝을 잃은 서러게이트는 그 값의 3바이트 꼴(ED A0~BF 80~BF — 올바른 UTF-8에는 없는 바이트)로, 나머지는 UTF-8로 쓴다(MirrorReader.Str가 되살린다).
        void StrWithLoneSurrogates(string v)
        {
            var count = 0;
            for (var i = 0; i < v.Length; i++)
            {
                var c = v[i];
                if (c < 0x80) count += 1;
                else if (c < 0x800) count += 2;
                else if (c >= 0xD800 && c <= 0xDBFF && i + 1 < v.Length && char.IsLowSurrogate(v[i + 1]))
                {
                    count += 4;
                    i++;
                }
                else count += 3;
            }
            UVar((ulong)count + 1);
            Ensure(count);
            var start = 0;
            for (var i = 0; i <= v.Length; i++)
            {
                if (i < v.Length && !IsLoneAt(v, ref i)) continue;
                Length += Encoding.UTF8.GetBytes(v, start, i - start, Data, Length);
                if (i < v.Length)
                {
                    var c = v[i];
                    Data[Length++] = 0xED;
                    Data[Length++] = (byte)(0x80 | ((c >> 6) & 0x3F));
                    Data[Length++] = (byte)(0x80 | (c & 0x3F));
                }
                start = i + 1;
            }
        }

        // v[i]가 짝을 잃은 서러게이트인지(짝이 맞으면 거짓이고 i를 짝의 뒤 글자로 옮긴다 — 부른 쪽 반복이 짝을 한 번에 넘긴다).
        static bool IsLoneAt(string v, ref int i)
        {
            var c = v[i];
            if (c < 0xD800 || c > 0xDFFF) return false;
            if (c <= 0xDBFF && i + 1 < v.Length && char.IsLowSurrogate(v[i + 1]))
            {
                i++;
                return false;
            }
            return true;
        }

        public void Bytes(byte[] data, int start, int count)
        {
            Ensure(count);
            Buffer.BlockCopy(data, start, Data, Length, count);
            Length += count;
        }

        public bool SameAs(MirrorWriter other)
        {
            if (other == null || other.Length != Length) return false;
            for (var i = 0; i < Length; i++)
                if (Data[i] != other.Data[i]) return false;
            return true;
        }
    }

    public sealed class MirrorReader
    {
        public readonly byte[] Data;
        public int Position;
        public readonly int End;

        public MirrorReader(byte[] data, int start = 0, int end = -1)
        {
            Data = data;
            Position = start;
            End = end < 0 ? data.Length : Math.Min(end, data.Length);
        }

        public bool AtEnd => Position >= End;

        // 읽기는 End를 넘지 않는다: 받은 범위(메시지 안의 한 기록)가 잘렸으면 뒤의 바이트(다음 기록·버퍼 찌꺼기)를 값으로 읽는 대신 거절한다.
        static FormatException Truncated() => new FormatException("[MomoiProject] 데이터가 중간에 끝났습니다(받은 데이터가 잘렸거나 깨졌습니다).");

        public byte Byte()
        {
            if (Position >= End) throw Truncated();
            return Data[Position++];
        }

        public ulong UVar()
        {
            ulong v = 0;
            var shift = 0;
            while (true)
            {
                if (Position >= End) throw Truncated();
                var b = Data[Position++];
                v |= (ulong)(b & 0x7F) << shift;
                if (b < 0x80) return v;
                shift += 7;
                if (shift > 63) throw new FormatException("[MomoiProject] 잘못된 숫자 인코딩입니다(받은 데이터가 깨졌습니다).");
            }
        }

        // 뒤따르는 항목 수(항목마다 1바이트 이상). 남은 바이트보다 많으면 깨진(또는 악의적인) 데이터다 — 거대한 미리 할당을 막는다.
        // bias: 개수에 더해 쓰는 값(0 = null을 뜻하는 목록 인코딩이면 1).
        public int Count(int bias = 0)
        {
            var value = UVar();
            if (value > (ulong)Math.Max(0, End - Position) + (ulong)bias) throw new FormatException("[MomoiProject] 항목 수가 남은 데이터보다 많습니다(받은 데이터가 깨졌습니다).");
            return (int)value;
        }

        public long Long()
        {
            var u = UVar();
            return (long)(u >> 1) ^ -(long)(u & 1);
        }

        public int Int() => (int)Long();
        public bool Bool() => Byte() != 0;

        public float Float()
        {
            if (End - Position < 4) throw Truncated();
            var bits = Data[Position] | Data[Position + 1] << 8 | Data[Position + 2] << 16 | Data[Position + 3] << 24;
            Position += 4;
            return new FloatBits { I = bits }.F;
        }

        public double Double()
        {
            if (End - Position < 8) throw Truncated();
            long bits = 0;
            for (var i = 0; i < 8; i++) bits |= (long)Data[Position + i] << (8 * i);
            Position += 8;
            return BitConverter.Int64BitsToDouble(bits);
        }

        public string Str()
        {
            var n = Count(1);
            if (n == 0) return null;
            var end = Position + n - 1;
            var s = HasLoneSurrogate(Position, end) ? StrWithLoneSurrogates(Position, end) : Encoding.UTF8.GetString(Data, Position, n - 1);
            Position = end;
            return s;
        }

        // 짝을 잃은 서러게이트의 3바이트 꼴(MirrorWriter.Str)이 있는지.
        bool HasLoneSurrogate(int start, int end)
        {
            for (var i = Array.IndexOf(Data, (byte)0xED, start, end - start); i >= 0; i = Array.IndexOf(Data, (byte)0xED, i + 1, end - i - 1))
                if (IsLoneAt(i, end)) return true;
            return false;
        }

        bool IsLoneAt(int i, int end) => i + 2 < end && Data[i] == 0xED && Data[i + 1] >= 0xA0 && Data[i + 1] <= 0xBF && Data[i + 2] >= 0x80 && Data[i + 2] <= 0xBF;

        string StrWithLoneSurrogates(int start, int end)
        {
            var b = new StringBuilder(end - start);
            var segment = start;
            for (var i = start; i < end; i++)
            {
                if (!IsLoneAt(i, end)) continue;
                if (i > segment) b.Append(Encoding.UTF8.GetString(Data, segment, i - segment));
                b.Append((char)(0xD000 | ((Data[i + 1] & 0x3F) << 6) | (Data[i + 2] & 0x3F)));
                i += 2;
                segment = i + 1;
            }
            if (end > segment) b.Append(Encoding.UTF8.GetString(Data, segment, end - segment));
            return b.ToString();
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    struct FloatBits
    {
        [FieldOffset(0)] public float F;
        [FieldOffset(0)] public int I;
    }
}
