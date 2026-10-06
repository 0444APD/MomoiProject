#if !(UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX)
#define DISABLESTEAMWORKS
#endif
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
#if !DISABLESTEAMWORKS
using Steamworks;
#endif

namespace GBible
{
    // 사설 호스트 멀티(친구 방): Steam 로비(친구 초대) + Steam Datagram Relay(NAT 통과, 서버 비용 없음). 방장 권한.
    //   방 생성:     SteamLobby.Create(4);            — 로비를 만들고 이 프로그램이 방장(GameNet.Host)이 된다. 친구 초대 창: SteamLobby.Invite()
    //   방 참가:     친구의 초대를 받거나(Steam 친구 목록 › 게임 참가) SteamLobby.Join(로비 번호) — 방장에게 접속(GameNet.Join)
    //   나가기:      SteamLobby.Leave();              — 방장이 나가면 판이 끝난다(모두 연결이 끊긴다).
    // 게임 코드는 4블록 그대로다(Game.Do·SubscribeAfterAction). 전송만 Steam 릴레이로 바뀐다. 시험은 직접 접속(GameNet.HostDirect·JoinDirect)으로 해도 같다.
    public static class SteamLobby
    {
        // 지금 들어가 있는 로비(없으면 0).
        public static ulong Current { get; private set; }
        public static bool IsHost { get; private set; }

        // 로비를 만들었거나 들어갔을 때 / 실패했을 때(이유).
        public static event Action<ulong> Entered;
        public static event Action<string> Failed;

        const string HostKey = "momoi.host";

#if !DISABLESTEAMWORKS
        static CallResult<LobbyCreated_t> _created;
        static CallResult<LobbyEnter_t> _entered;
        static Callback<GameLobbyJoinRequested_t> _joinRequested;
        static string _playerName;
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Current = 0;
            IsHost = false;
            Entered = null;
            Failed = null;
#if !DISABLESTEAMWORKS
            _created = null;
            _entered = null;
            _joinRequested = null;
            _playerName = null;
#endif
        }

        // Steam이 준비되면: 친구 초대 수락을 받고, 초대로 게임을 켰으면(+connect_lobby) 그 로비로 들어간다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            GameSteam.Ready -= OnSteamReady;
            GameSteam.Ready += OnSteamReady;
            if (GameSteam.IsReady) OnSteamReady();
        }

        static void OnSteamReady()
        {
#if !DISABLESTEAMWORKS
            SteamNetworkingUtils.InitRelayNetworkAccess();
            _joinRequested ??= Callback<GameLobbyJoinRequested_t>.Create(e => Join(e.m_steamIDLobby.m_SteamID));
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "+connect_lobby");
            if (index >= 0 && index + 1 < args.Length && ulong.TryParse(args[index + 1], out var lobby)) Join(lobby);
#endif
        }

        // 친구 방을 만든다(최대 인원, 친구만 = true). 만들어지면 이 프로그램이 방장이 된다.
        public static void Create(int maxPlayers = 4, bool friendsOnly = true, string playerName = null)
        {
#if !DISABLESTEAMWORKS
            if (!GameSteam.IsReady) { Fail("Steam이 준비되지 않았습니다."); return; }
            Leave();
            _playerName = playerName;
            _created ??= CallResult<LobbyCreated_t>.Create(OnCreated);
            _created.Set(SteamMatchmaking.CreateLobby(friendsOnly ? ELobbyType.k_ELobbyTypeFriendsOnly : ELobbyType.k_ELobbyTypePublic, Mathf.Clamp(maxPlayers, 1, 250)));
#else
            Fail("이 플랫폼에서는 Steam을 쓰지 않습니다.");
#endif
        }

        // 친구 초대 창(Steam 오버레이).
        public static void Invite()
        {
#if !DISABLESTEAMWORKS
            if (Current != 0) SteamFriends.ActivateGameOverlayInviteDialog(new CSteamID(Current));
#endif
        }

        public static void Join(ulong lobby, string playerName = null)
        {
#if !DISABLESTEAMWORKS
            if (!GameSteam.IsReady) { Fail("Steam이 준비되지 않았습니다."); return; }
            if (lobby == 0 || lobby == Current) return;
            Leave();
            _playerName = playerName;
            _entered ??= CallResult<LobbyEnter_t>.Create(OnEntered);
            _entered.Set(SteamMatchmaking.JoinLobby(new CSteamID(lobby)));
#else
            Fail("이 플랫폼에서는 Steam을 쓰지 않습니다.");
#endif
        }

        // 로비와 멀티를 끝낸다(방장이면 판이 끝난다).
        public static void Leave()
        {
#if !DISABLESTEAMWORKS
            if (Current != 0 && GameSteam.IsReady) SteamMatchmaking.LeaveLobby(new CSteamID(Current));
#endif
            var was = Current != 0;
            Current = 0;
            IsHost = false;
            if (was && GameNet.IsOnline) GameNet.Leave();
        }

        static void Fail(string reason)
        {
            Debug.LogWarning("[MomoiProject] Steam 로비: " + reason);
            Failed?.Invoke(reason);
        }

#if !DISABLESTEAMWORKS
        static void OnCreated(LobbyCreated_t e, bool ioFailure)
        {
            if (ioFailure || e.m_eResult != EResult.k_EResultOK) { Fail("로비를 만들지 못했습니다(" + e.m_eResult + ")."); return; }
            Current = e.m_ulSteamIDLobby;
            IsHost = true;
            var lobby = new CSteamID(Current);
            SteamMatchmaking.SetLobbyData(lobby, HostKey, SteamUser.GetSteamID().m_SteamID.ToString());
            GameNet.Host(new SteamServerTransport(), _playerName ?? GameSteam.PlayerName);
            Entered?.Invoke(Current);
        }

        static void OnEntered(LobbyEnter_t e, bool ioFailure)
        {
            if (ioFailure || e.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess) { Fail("로비에 들어가지 못했습니다."); return; }
            Current = e.m_ulSteamIDLobby;
            var lobby = new CSteamID(Current);
            // 방장 자신이 로비를 만든 경우에도 LobbyEnter가 온다 — 그때는 이미 방장이다.
            if (SteamMatchmaking.GetLobbyOwner(lobby) == SteamUser.GetSteamID()) return;
            IsHost = false;
            if (!ulong.TryParse(SteamMatchmaking.GetLobbyData(lobby, HostKey), out var host) || host == 0) host = SteamMatchmaking.GetLobbyOwner(lobby).m_SteamID;
            GameNet.Join(new SteamClientTransport(host), _playerName ?? GameSteam.PlayerName);
            Entered?.Invoke(Current);
        }
#endif
    }

#if !DISABLESTEAMWORKS
    // Steam 릴레이 전송(공통): 받은 메시지를 꺼내 NetEvent로.
    static class SteamNetShared
    {
        public const int MaxBatch = 64;

        public static int ReceiveAll(HSteamNetConnection connection, Queue<NetEvent> into, int connectionId, IntPtr[] buffer)
        {
            var count = SteamNetworkingSockets.ReceiveMessagesOnConnection(connection, buffer, buffer.Length);
            for (var i = 0; i < count; i++)
            {
                var message = SteamNetworkingMessage_t.FromIntPtr(buffer[i]);
                var data = new byte[message.m_cbSize];
                if (message.m_cbSize > 0) Marshal.Copy(message.m_pData, data, 0, message.m_cbSize);
                SteamNetworkingMessage_t.Release(buffer[i]);
                into.Enqueue(new NetEvent { Kind = NetEventKind.Data, Connection = connectionId, Data = data, Length = data.Length });
            }
            return count;
        }

        public static void Send(HSteamNetConnection connection, byte[] data, int length, bool reliable)
        {
            var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                var flags = reliable ? Constants.k_nSteamNetworkingSend_Reliable : Constants.k_nSteamNetworkingSend_Unreliable;
                SteamNetworkingSockets.SendMessageToConnection(connection, handle.AddrOfPinnedObject(), (uint)length, flags, out _);
            }
            finally
            {
                handle.Free();
            }
        }
    }

    public sealed class SteamServerTransport : INetServerTransport
    {
        readonly Queue<NetEvent> _inbox = new Queue<NetEvent>();
        readonly Dictionary<int, HSteamNetConnection> _connections = new Dictionary<int, HSteamNetConnection>();
        readonly IntPtr[] _buffer = new IntPtr[SteamNetShared.MaxBatch];
        HSteamListenSocket _listen;
        Callback<SteamNetConnectionStatusChangedCallback_t> _status;
        bool _started;

        public string Address => SteamLobby.Current != 0 ? "Steam 로비 " + SteamLobby.Current : "Steam 릴레이";

        public void Start()
        {
            if (_started) return;
            _status = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnStatus);
            _listen = SteamNetworkingSockets.CreateListenSocketP2P(0, 0, null);
            _started = true;
        }

        static int IdOf(HSteamNetConnection connection) => unchecked((int)connection.m_HSteamNetConnection);

        void OnStatus(SteamNetConnectionStatusChangedCallback_t e)
        {
            if (e.m_info.m_hListenSocket != _listen) return;
            var id = IdOf(e.m_hConn);
            switch (e.m_info.m_eState)
            {
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
                    SteamNetworkingSockets.AcceptConnection(e.m_hConn);
                    break;
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                    _connections[id] = e.m_hConn;
                    _inbox.Enqueue(new NetEvent { Kind = NetEventKind.Connected, Connection = id });
                    break;
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                    SteamNetworkingSockets.CloseConnection(e.m_hConn, 0, null, false);
                    if (_connections.Remove(id))
                        _inbox.Enqueue(new NetEvent { Kind = NetEventKind.Disconnected, Connection = id, Reason = string.IsNullOrEmpty(e.m_info.m_szEndDebug) ? "연결이 끊겼습니다." : e.m_info.m_szEndDebug });
                    break;
            }
        }

        public void Send(int connection, byte[] data, int length, bool reliable = true)
        {
            if (_connections.TryGetValue(connection, out var handle)) SteamNetShared.Send(handle, data, length, reliable);
        }

        public void Disconnect(int connection, string reason = null)
        {
            if (!_connections.TryGetValue(connection, out var handle)) return;
            _connections.Remove(connection);
            SteamNetworkingSockets.CloseConnection(handle, 0, reason ?? "", true);
        }

        public bool Poll(out NetEvent e)
        {
            if (_inbox.Count == 0)
                foreach (var pair in _connections) SteamNetShared.ReceiveAll(pair.Value, _inbox, pair.Key, _buffer);
            if (_inbox.Count > 0)
            {
                e = _inbox.Dequeue();
                return true;
            }
            e = default;
            return false;
        }

        public void Dispose()
        {
            foreach (var handle in _connections.Values) SteamNetworkingSockets.CloseConnection(handle, 0, "방이 닫혔습니다.", true);
            _connections.Clear();
            if (_started) SteamNetworkingSockets.CloseListenSocket(_listen);
            _status?.Dispose();
            _status = null;
            _started = false;
        }
    }

    public sealed class SteamClientTransport : INetClientTransport
    {
        readonly ulong _host;
        readonly Queue<NetEvent> _inbox = new Queue<NetEvent>();
        readonly IntPtr[] _buffer = new IntPtr[SteamNetShared.MaxBatch];
        HSteamNetConnection _connection;
        Callback<SteamNetConnectionStatusChangedCallback_t> _status;
        bool _open;

        // host: 방장의 SteamID.
        public SteamClientTransport(ulong host) => _host = host;

        public void Connect()
        {
            if (_open) return;
            _status ??= Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnStatus);
            var identity = new SteamNetworkingIdentity();
            identity.SetSteamID(new CSteamID(_host));
            _connection = SteamNetworkingSockets.ConnectP2P(ref identity, 0, 0, null);
            _open = true;
        }

        void OnStatus(SteamNetConnectionStatusChangedCallback_t e)
        {
            if (e.m_hConn != _connection) return;
            switch (e.m_info.m_eState)
            {
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                    _inbox.Enqueue(new NetEvent { Kind = NetEventKind.Connected });
                    break;
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                    SteamNetworkingSockets.CloseConnection(e.m_hConn, 0, null, false);
                    if (_open) _inbox.Enqueue(new NetEvent { Kind = NetEventKind.Disconnected, Reason = string.IsNullOrEmpty(e.m_info.m_szEndDebug) ? "방장과의 연결이 끊겼습니다." : e.m_info.m_szEndDebug });
                    _open = false;
                    break;
            }
        }

        public void Send(byte[] data, int length, bool reliable = true)
        {
            if (_open) SteamNetShared.Send(_connection, data, length, reliable);
        }

        public void Disconnect(string reason = null)
        {
            if (!_open) return;
            _open = false;
            SteamNetworkingSockets.CloseConnection(_connection, 0, reason ?? "", true);
        }

        public bool Poll(out NetEvent e)
        {
            if (_inbox.Count == 0 && _open) SteamNetShared.ReceiveAll(_connection, _inbox, 0, _buffer);
            if (_inbox.Count > 0)
            {
                e = _inbox.Dequeue();
                return true;
            }
            e = default;
            return false;
        }

        public void Dispose()
        {
            Disconnect();
            _status?.Dispose();
            _status = null;
        }
    }
#endif
}
