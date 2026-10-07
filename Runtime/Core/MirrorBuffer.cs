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
            var count = Encoding.UTF8.GetByteCount(v);
            UVar((ulong)count + 1);
            Ensure(count);
            Length += Encoding.UTF8.GetBytes(v, 0, v.Length, Data, Length);
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
            End = end < 0 ? data.Length : end;
        }

        public bool AtEnd => Position >= End;

        public byte Byte() => Data[Position++];

        public ulong UVar()
        {
            ulong v = 0;
            var shift = 0;
            while (true)
            {
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
        public bool Bool() => Data[Position++] != 0;

        public float Float()
        {
            var bits = Data[Position] | Data[Position + 1] << 8 | Data[Position + 2] << 16 | Data[Position + 3] << 24;
            Position += 4;
            return new FloatBits { I = bits }.F;
        }

        public double Double()
        {
            long bits = 0;
            for (var i = 0; i < 8; i++) bits |= (long)Data[Position + i] << (8 * i);
            Position += 8;
            return BitConverter.Int64BitsToDouble(bits);
        }

        public string Str()
        {
            var n = Count(1);
            if (n == 0) return null;
            var s = Encoding.UTF8.GetString(Data, Position, n - 1);
            Position += n - 1;
            return s;
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    struct FloatBits
    {
        [FieldOffset(0)] public float F;
        [FieldOffset(0)] public int I;
    }
}
