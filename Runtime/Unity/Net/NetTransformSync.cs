using System;
using System.Collections.Generic;
using UnityEngine;

namespace GBible
{
    // 실시간 위치 맞추기(사설 호스트 멀티): 매 순간 움직이는 것(내 캐릭터·탄환)은 규칙 데이터가 아니라 이 컴포넌트로 맞춘다.
    //   · 주인(owner = 플레이어 번호)이 이 프로그램이면: 위치·회전을 초당 sendRate번 보낸다(잃어도 되는 메시지 — 다음 것이 곧 온다).
    //   · 아니면: 받은 위치를 delay초 뒤에서 부드럽게 따라간다(보간).
    // 방장은 받은 위치를 다른 사람에게 그대로 전한다(NetServer.RelayStreams). 판정(피해·드롭·상태 효과)은 방장의 규칙이 한다 — 위치는 판정의 재료.
    //   스폰할 때: var sync = go.AddComponent<NetTransformSync>(); sync.entityId = "hero:" + playerId; sync.owner = playerId;
    // entityId는 모든 프로그램에서 같은 글자여야 한다(같은 물체). 싱글(멀티 아님)에서는 아무것도 하지 않는다.
    [AddComponentMenu("MomoiProject/실시간 위치 맞추기")]
    public sealed class NetTransformSync : MonoBehaviour
    {
        [Tooltip("모든 프로그램에서 같은 이름(비우면 오브젝트 이름)")] public string entityId = "";
        [Tooltip("이 물체를 움직이는 플레이어 번호(내 번호면 내가 보낸다). 0 = 방장(서버)")] public int owner;
        [Tooltip("초당 보내는 횟수"), Range(1, 60)] public int sendRate = 20;
        [Tooltip("받은 위치를 이만큼 늦게 그린다(초) — 클수록 부드럽고 늦다"), Range(0f, 0.5f)] public float delay = 0.1f;
        [Tooltip("회전도 맞춘다")] public bool syncRotation = true;

        public const byte Channel = 1;

        struct Sample
        {
            public float Time;
            public Vector3 Position;
            public Quaternion Rotation;
        }

        static readonly Dictionary<string, NetTransformSync> ById = new Dictionary<string, NetTransformSync>(StringComparer.Ordinal);
        static readonly List<NetTransformSync> Owned = new List<NetTransformSync>();
        static readonly MirrorWriter Packet = new MirrorWriter(512);
        static float _nextSend;
        static NetClient _hookedClient;
        static NetServer _hookedServer;

        readonly List<Sample> _samples = new List<Sample>();

        public string Id => string.IsNullOrEmpty(entityId) ? name : entityId;

        // 이 프로그램이 이 물체의 주인인가(멀티가 아니면 언제나 참 — 그냥 움직인다).
        public bool IsMine
        {
            get
            {
                if (!GameNet.IsOnline) return true;
                var local = GBibleRuntime.Session?.LocalPlayer ?? 0;
                return owner == local || (owner == 0 && GameNet.Server != null && !GameNet.IsTestMode);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            ById.Clear();
            Owned.Clear();
            _nextSend = 0;
            _hookedClient = null;
            _hookedServer = null;
        }

        void OnEnable()
        {
            ById[Id] = this;
            Owned.Add(this);
        }

        void OnDisable()
        {
            if (ById.TryGetValue(Id, out var self) && self == this) ById.Remove(Id);
            Owned.Remove(this);
        }

        void Update()
        {
            if (!GameNet.IsOnline || IsMine || _samples.Count == 0) return;
            var renderTime = Time.unscaledTime - delay;
            // 그릴 시각을 감싸는 두 표본 사이를 보간한다(없으면 가장 가까운 것).
            while (_samples.Count >= 3 && _samples[1].Time <= renderTime) _samples.RemoveAt(0);
            var a = _samples[0];
            var b = _samples.Count > 1 ? _samples[1] : a;
            var t = b.Time > a.Time ? Mathf.Clamp01((renderTime - a.Time) / (b.Time - a.Time)) : 1f;
            transform.position = Vector3.LerpUnclamped(a.Position, b.Position, t);
            if (syncRotation) transform.rotation = Quaternion.Slerp(a.Rotation, b.Rotation, t);
        }

        void Receive(Vector3 position, Quaternion rotation)
        {
            if (IsMine) return;
            _samples.Add(new Sample { Time = Time.unscaledTime, Position = position, Rotation = rotation });
            if (_samples.Count > 32) _samples.RemoveAt(0);
        }

        // GameNet이 매 프레임 부른다: 받는 쪽을 잇고, 내 것들을 보낸다.
        internal static void Pump()
        {
            Hook();
            if (!GameNet.IsOnline || Owned.Count == 0) return;
            var rate = 0;
            foreach (var sync in Owned) rate = Math.Max(rate, sync.sendRate);
            if (rate <= 0 || Time.unscaledTime < _nextSend) return;
            _nextSend = Time.unscaledTime + 1f / rate;

            Packet.Clear();
            var count = 0;
            foreach (var sync in Owned) if (sync.IsMine) count++;
            if (count == 0) return;
            Packet.UVar((ulong)count);
            foreach (var sync in Owned)
            {
                if (!sync.IsMine) continue;
                var p = sync.transform.position;
                var r = sync.transform.rotation;
                Packet.Str(sync.Id);
                Packet.Float(p.x);
                Packet.Float(p.y);
                Packet.Float(p.z);
                Packet.Float(r.x);
                Packet.Float(r.y);
                Packet.Float(r.z);
                Packet.Float(r.w);
            }
            if (GameNet.Client != null) GameNet.Client.SendStream(Channel, Packet.Data, 0, Packet.Length);
            else GameNet.Server?.SendStream(Channel, Packet.Data, Packet.Length, from: GBibleRuntime.Session?.LocalPlayer ?? 0);
        }

        static void Hook()
        {
            if (_hookedClient != GameNet.Client)
            {
                if (_hookedClient != null) _hookedClient.StreamReceived -= OnClientStream;
                _hookedClient = GameNet.Client;
                if (_hookedClient != null) _hookedClient.StreamReceived += OnClientStream;
            }
            if (_hookedServer != GameNet.Server)
            {
                if (_hookedServer != null) _hookedServer.StreamReceived -= OnServerStream;
                _hookedServer = GameNet.Server;
                if (_hookedServer != null) _hookedServer.StreamReceived += OnServerStream;
            }
        }

        static void OnClientStream(int from, byte channel, byte[] data, int start, int end)
        {
            if (channel == Channel) Read(new MirrorReader(data, start, end));
        }

        // 방장: 클라이언트가 보낸 것(앞의 두 바이트 = 메시지 종류·채널).
        static void OnServerStream(NetPlayer from, byte channel, byte[] data, int length)
        {
            if (channel == Channel) Read(new MirrorReader(data, 2, length));
        }

        static void Read(MirrorReader r)
        {
            var count = (int)r.UVar();
            for (var i = 0; i < count && !r.AtEnd; i++)
            {
                var id = r.Str();
                var position = new Vector3(r.Float(), r.Float(), r.Float());
                var rotation = new Quaternion(r.Float(), r.Float(), r.Float(), r.Float());
                if (id != null && ById.TryGetValue(id, out var sync)) sync.Receive(position, rotation);
            }
        }
    }
}
