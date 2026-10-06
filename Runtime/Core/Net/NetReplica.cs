using System;
using System.Collections.Generic;

namespace GBible
{
    // 받는 쪽 적용(클라이언트·리플레이 재생기 공통): 서버의 형식 번호표와 묶음(바뀐 곳·이벤트)을 세션에 적용한다.
    public sealed class NetReplica
    {
        readonly GameSession _session;

        public NetReplica(GameSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public GameSession Session => _session;
        public MirrorReceiver Receiver { get; private set; } = new MirrorReceiver();

        // 처음부터 다시 받을 때(리플레이 찾아가기·다시 접속): 객체 번호 기억을 비운다. 번호표는 다시 UseTypes로.
        public void Reset() => Receiver = new MirrorReceiver();

        // 보내는 쪽의 형식 번호표. 돌려주는 값: 상대에게만 있는 형식 이름.
        public List<string> UseTypes(string[] table) => Receiver.UseTypes(table);

        // 묶음 하나를 적용한다(r = 메시지 종류 바이트 다음부터). events가 거짓이면 이벤트를 쏘지 않는다(리플레이에서 빠르게 찾아갈 때).
        public void ApplyBatch(MirrorReader r, bool events = true)
        {
            var tick = r.Long();
            var time = r.Double();
            _session.Clock.Rewind(tick, time);
            var data = _session.Data;
            while (!r.AtEnd)
            {
                var kind = (NetRecord)r.Byte();
                if (kind == 0) break;
                switch (kind)
                {
                    case NetRecord.Reset:
                    {
                        var key = r.Str();
                        var zone = (DataZone)r.Byte();
                        var label = r.Str();
                        var snapshot = NetProtocol.ReadBlockCopy(r);
                        data.LoadRemote(key, zone, label, snapshot, Receiver);
                        break;
                    }
                    case NetRecord.Delta:
                    {
                        var key = r.Str();
                        NetProtocol.ReadBlock(r, out var start, out var end);
                        data.ApplyRemote(key, r.Data, start, end, Receiver);
                        break;
                    }
                    case NetRecord.Value:
                    {
                        var key = r.Str();
                        var zone = (DataZone)r.Byte();
                        var label = r.Str();
                        data.SetRemote(key, zone, label, NetProtocol.ReadScalar(r));
                        break;
                    }
                    case NetRecord.Tree:
                    {
                        var key = r.Str();
                        var zone = (DataZone)r.Byte();
                        var label = r.Str();
                        var type = TypeResolver.Find(r.Str());
                        data.LoadRemoteTree(key, zone, label, type, MiniJson.Deserialize(r.Str()));
                        break;
                    }
                    case NetRecord.Hide:
                        r.Str();
                        break;
                    case NetRecord.Event:
                    {
                        NetProtocol.ReadBlock(r, out var start, out var end);
                        if (!events) break;
                        object evt;
                        Receiver.MissingAsNull = true;
                        try { evt = Receiver.Load(r.Data, start, end); }
                        finally { Receiver.MissingAsNull = false; }
                        _session.Events.PublishRemote(evt);
                        break;
                    }
                    default:
                        throw new FormatException($"[MomoiProject] 모르는 묶음 기록({(byte)kind})입니다.");
                }
            }
        }
    }
}
