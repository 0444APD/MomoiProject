using System;
using System.Collections.Generic;
using System.IO;

namespace GBible
{
    // 리플레이(판 밖에서 다시 보기): 받은 메시지(형식 번호표·묶음)를 시각과 함께 녹화한 것.
    // 계산을 다시 하지 않고 기록된 바뀐 곳을 그대로 다시 적용하므로 어느 기기에서 틀어도 같다.
    //   녹화: 클라이언트 — NetClient.Recording(들어갈 때부터, 내가 본 것) · 서버·방장·혼자 — NetServer.AddRecorder(모두 본 것)
    //   재생: new NetReplayPlayer(세션, 리플레이) → Update(dt) · Seek(초) · Speed · Playing
    public sealed class NetReplay
    {
        public const string Magic = "MOMOIRP";
        public const int FileVersion = 1;

        public struct Entry
        {
            // 녹화를 시작한 뒤의 초.
            public double Time;
            public byte[] Message;
        }

        readonly List<Entry> _entries = new List<Entry>();

        // 누구의 눈으로 녹화했는가(사람이 읽는 글: "클라이언트 #2 철수", "서버(모두)").
        public string Source = "";
        // 녹화를 시작한 현실 시각(UTC 밀리초).
        public long StartedUtc = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;

        public IReadOnlyList<Entry> Entries => _entries;
        public double Duration => _entries.Count == 0 ? 0 : _entries[_entries.Count - 1].Time;
        public int Count => _entries.Count;

        public void Add(double time, byte[] data, int length)
        {
            var copy = new byte[length];
            Buffer.BlockCopy(data, 0, copy, 0, length);
            if (_entries.Count > 0 && time < _entries[_entries.Count - 1].Time) time = _entries[_entries.Count - 1].Time;
            _entries.Add(new Entry { Time = time, Message = copy });
        }

        public byte[] ToBytes()
        {
            var w = new MirrorWriter(4096);
            w.Str(Magic);
            w.UVar(FileVersion);
            w.Str(Source ?? "");
            w.Long(StartedUtc);
            w.UVar((ulong)_entries.Count);
            foreach (var entry in _entries)
            {
                w.Double(entry.Time);
                NetProtocol.WriteBlock(w, entry.Message, 0, entry.Message.Length);
            }
            return w.ToArray();
        }

        public static NetReplay FromBytes(byte[] bytes)
        {
            var r = new MirrorReader(bytes);
            if (r.Str() != Magic) throw new FormatException("[MomoiProject] 리플레이 파일이 아닙니다.");
            var version = (int)r.UVar();
            if (version > FileVersion) throw new FormatException($"[MomoiProject] 더 새 버전의 리플레이입니다(v{version}).");
            var replay = new NetReplay { Source = r.Str(), StartedUtc = r.Long() };
            var count = (int)r.UVar();
            for (var i = 0; i < count; i++)
            {
                var time = r.Double();
                replay._entries.Add(new Entry { Time = time, Message = NetProtocol.ReadBlockCopy(r) });
            }
            return replay;
        }

        public void Save(string path)
        {
            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            File.WriteAllBytes(path, ToBytes());
        }

        public static NetReplay Load(string path) => FromBytes(File.ReadAllBytes(path));
    }

    // 리플레이 재생기: 세션을 '받는 쪽'으로 바꾸고(행동은 하지 않는다) 녹화된 메시지를 시각에 맞춰 적용한다.
    // 뒤로 찾아가기는 처음부터 다시 적용한다(이벤트는 쏘지 않고 데이터만) — 바뀐 곳은 앞으로만 기록돼 있으므로.
    public sealed class NetReplayPlayer
    {
        readonly GameSession _session;
        readonly NetReplica _replica;
        int _next;
        bool _welcomed;

        public NetReplayPlayer(GameSession session, NetReplay replay)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            Replay = replay ?? throw new ArgumentNullException(nameof(replay));
            if (session.Role == SessionRole.Server) throw new InvalidOperationException("[MomoiProject] 서버 세션으로는 리플레이를 틀 수 없습니다.");
            session.Role = SessionRole.Client;
            session.RemoteDo = action =>
            {
                session.Journal.Append(JournalKind.Warning, "Net", "리플레이 중에는 행동하지 않습니다.");
                return new ActionRecord { Action = action, Status = ActionStatus.Failed, Error = "리플레이 중" };
            };
            _replica = new NetReplica(session);
            Seek(0);
        }

        public NetReplay Replay { get; }
        public GameSession Session => _session;
        // 지금 위치(초).
        public double Time { get; private set; }
        public double Duration => Replay.Duration;
        public bool Playing = true;
        // 배속(1 = 녹화 속도).
        public double Speed = 1;
        public bool AtEnd => _next >= Replay.Count;

        // 시간을 흘려 그 시각까지의 메시지를 적용한다(이벤트도 쏜다 — 연출이 보인다).
        public void Update(double deltaTime)
        {
            if (!Playing) return;
            Time = Math.Min(Duration, Time + Math.Max(0, deltaTime) * Math.Max(0, Speed));
            ApplyUntil(Time, events: true);
            if (AtEnd) Playing = false;
        }

        // 그 시각의 모습으로 간다(이벤트 없이 데이터만).
        public void Seek(double time)
        {
            time = Math.Max(0, Math.Min(Duration, time));
            if (time < Time || _next == 0)
            {
                _session.Data.ResetZone(DataZone.Session);
                _replica.Reset();
                _next = 0;
                _welcomed = false;
            }
            Time = time;
            ApplyUntil(time, events: false);
        }

        // 메시지 하나만큼 나아간다(이벤트 포함).
        public void Step()
        {
            if (AtEnd) return;
            Time = Replay.Entries[_next].Time;
            Apply(Replay.Entries[_next].Message, events: true);
            _next++;
        }

        void ApplyUntil(double time, bool events)
        {
            while (_next < Replay.Count && Replay.Entries[_next].Time <= time)
            {
                Apply(Replay.Entries[_next].Message, events);
                _next++;
            }
        }

        void Apply(byte[] message, bool events)
        {
            if (message.Length == 0) return;
            var r = new MirrorReader(message, 1, message.Length);
            try
            {
                switch ((NetMessage)message[0])
                {
                    case NetMessage.Welcome:
                        // 한 녹화에 매치가 여럿이면(매칭큐) 새 매치마다 판 데이터를 비운다.
                        if (_welcomed) _session.Data.ResetZone(DataZone.Session);
                        _welcomed = true;
                        _session.LocalPlayer = (int)r.UVar();
                        _replica.Reset();
                        _replica.UseTypes(NetProtocol.ReadStrings(r, NetProtocol.MaxTypeTable));
                        break;
                    case NetMessage.Batch:
                        _replica.ApplyBatch(r, events);
                        break;
                }
            }
            catch (Exception e)
            {
                _session.Journal.Append(JournalKind.Error, "Net", "리플레이 메시지를 적용하지 못했습니다: " + e.Message);
            }
        }
    }
}
