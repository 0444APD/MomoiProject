using System;
using System.Collections.Generic;
using System.Text;

namespace GBible
{
    // 기기·플랫폼과 무관하게 같은 시드면 같은 순서를 내는 난수(xoshiro256**, 정수 연산만 사용).
    // UnityEngine.Random·System.Random은 버전·플랫폼에 따라 결과가 달라질 수 있어 게임 로직에서 쓰지 않는다.
    public sealed class RandomStream
    {
        ulong _s0, _s1, _s2, _s3;

        public string Name { get; }

        public RandomStream(string name, ulong seed)
        {
            Name = name;
            Reseed(seed);
        }

        public void Reseed(ulong seed)
        {
            var sm = seed;
            _s0 = SplitMix64.Next(ref sm);
            _s1 = SplitMix64.Next(ref sm);
            _s2 = SplitMix64.Next(ref sm);
            _s3 = SplitMix64.Next(ref sm);
        }

        public ulong NextULong()
        {
            ulong result = RotateLeft(_s1 * 5, 7) * 9;
            ulong t = _s1 << 17;
            _s2 ^= _s0;
            _s3 ^= _s1;
            _s1 ^= _s2;
            _s0 ^= _s3;
            _s2 ^= t;
            _s3 = RotateLeft(_s3, 45);
            return result;
        }

        public uint NextUInt() => (uint)(NextULong() >> 32);

        // [min, max) 범위의 편향 없는 정수.
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                throw new ArgumentException($"[MomoiProject] 난수 범위가 잘못되었습니다: [{minInclusive}, {maxExclusive})");
            ulong range = (ulong)((long)maxExclusive - minInclusive);
            ulong limit = ulong.MaxValue - (ulong.MaxValue % range + 1) % range;
            ulong value;
            do value = NextULong(); while (value > limit);
            return (int)((long)minInclusive + (long)(value % range));
        }

        // [0, 1)
        public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

        public bool Chance(double probability) => NextDouble() < probability;

        public ulong[] GetState() => new[] { _s0, _s1, _s2, _s3 };

        public void SetState(ulong[] state)
        {
            if (state == null || state.Length != 4) throw new ArgumentException("난수 상태는 ulong 4개여야 합니다.");
            _s0 = state[0];
            _s1 = state[1];
            _s2 = state[2];
            _s3 = state[3];
        }

        static ulong RotateLeft(ulong x, int k) => (x << k) | (x >> (64 - k));
    }

    public static class SplitMix64
    {
        public static ulong Next(ref ulong state)
        {
            ulong z = state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    // 용도별 난수 스트림(맵 생성·드롭·전투 …). 한 스트림에 호출을 추가해도 다른 스트림의 결과는 바뀌지 않는다.
    public sealed class RandomStreams : IInspectable
    {
        readonly Dictionary<string, RandomStream> _streams = new Dictionary<string, RandomStream>(StringComparer.Ordinal);

        public ulong MasterSeed { get; private set; }

        public RandomStreams(ulong masterSeed)
        {
            MasterSeed = masterSeed;
        }

        public string InspectName => "난수";
        public IEnumerable<RandomStream> Streams => _streams.Values;

        public RandomStream Get(string name)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("스트림 이름이 비어 있습니다.", nameof(name));
            if (!_streams.TryGetValue(name, out var stream))
                _streams[name] = stream = new RandomStream(name, SeedFor(name));
            return stream;
        }

        public void Reseed(ulong masterSeed)
        {
            MasterSeed = masterSeed;
            foreach (var stream in _streams.Values) stream.Reseed(SeedFor(stream.Name));
        }

        ulong SeedFor(string name)
        {
            var mix = MasterSeed ^ Fnv1a64(name);
            return SplitMix64.Next(ref mix);
        }

        // string.GetHashCode는 실행마다 달라질 수 있어 쓰지 않는다.
        public static ulong Fnv1a64(string text)
        {
            ulong hash = 14695981039346656037UL;
            foreach (var b in Encoding.UTF8.GetBytes(text))
            {
                hash ^= b;
                hash *= 1099511628211UL;
            }
            return hash;
        }

        public Dictionary<string, ulong[]> ExportStates()
        {
            var result = new Dictionary<string, ulong[]>(StringComparer.Ordinal);
            foreach (var pair in _streams) result[pair.Key] = pair.Value.GetState();
            return result;
        }

        public void ImportStates(Dictionary<string, ulong[]> states)
        {
            foreach (var pair in states) Get(pair.Key).SetState(pair.Value);
        }

        // 스냅샷 시점으로 되돌린다. 그 이후 새로 만들어진 스트림은 처음 상태로 되돌린다.
        public void Restore(Dictionary<string, ulong[]> states)
        {
            foreach (var stream in _streams.Values)
            {
                if (states.TryGetValue(stream.Name, out var state)) stream.SetState(state);
                else stream.Reseed(SeedFor(stream.Name));
            }
        }

        public void Inspect(InspectNode root)
        {
            root.Add("마스터 시드", MasterSeed.ToString());
            foreach (var stream in _streams.Values) root.Add(stream.Name, "사용 중");
        }
    }

    // 게임 로직이 쓰는 시계. UnityEngine.Time 대신 사용한다.
    public sealed class GameClock
    {
        public long Tick { get; private set; }
        public double Time { get; private set; }

        public void Advance(double deltaTime)
        {
            Tick++;
            if (deltaTime > 0) Time += deltaTime;
        }

        // 되감기 전용: 스냅샷 시점으로 시간을 돌린다.
        public void Rewind(long tick, double time)
        {
            Tick = tick;
            Time = time;
        }
    }
}
