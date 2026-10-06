using System;
using UnityEngine;

namespace GBible
{
    // 사설 호스트의 방 창구(바꿔 끼우는 곳): 방 생성·참가·초대·나가기. 화면은 GameNet.CreateRoom·JoinRoom·InviteToRoom·LeaveRoom만 부른다.
    //   기본 — 직접 접속(DirectRoomProvider): 방 생성 = 이 컴퓨터가 서버(포트), 초대 = 접속 주소를 클립보드에 복사, 참가 = 주소.
    //   Steam 모듈 — Steam이 준비되면 스스로 바꿔 끼운다(SteamRoomProvider): 방 생성 = Steam 로비 + 릴레이, 초대 = 친구 초대 창, 참가 = 초대 수락·로비 번호.
    // 다른 플랫폼(Epic 등)은 이것을 구현해 GameNet.Rooms에 넣는다. 방장이 나가면 판이 끝난다(방장 이어받기는 하지 않는다).
    public interface INetRoomProvider
    {
        // 화면에 보일 이름("직접 접속", "Steam").
        string Name { get; }
        // 지금 쓸 수 있는가(Steam이 꺼져 있으면 거짓).
        bool Available { get; }
        void Create(int maxPlayers, string playerName);
        // target: 직접 접속이면 "주소:포트", Steam이면 로비 번호.
        void Join(string target, string playerName);
        // 친구를 부른다.
        void Invite();
        void Leave();
        // 지금 방에 들어올 방법(화면 표시용). 방장이 아니면 null.
        string InviteText { get; }
    }

    public sealed class DirectRoomProvider : INetRoomProvider
    {
        // 방을 열 포트.
        public int Port = GameNet.DefaultPort;

        public string Name => "직접 접속";
        public bool Available => true;

        public void Create(int maxPlayers, string playerName) => GameNet.HostDirect(Port, NameOr(playerName), maxPlayers);

        public void Join(string target, string playerName)
        {
            if (string.IsNullOrWhiteSpace(target))
            {
                GameScreens.ShowFailure("들어갈 주소가 없습니다.");
                return;
            }
            GameNet.JoinDirect(target.Trim(), NameOr(playerName));
        }

        // 접속 주소(이 컴퓨터의 첫 주소)를 클립보드에 복사한다 — 메신저로 보내면 친구가 '방 참가'에 붙여 넣는다.
        public void Invite()
        {
            var text = InviteText;
            if (string.IsNullOrEmpty(text)) return;
            var first = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)[0];
            GUIUtility.systemCopyBuffer = first;
            GameScreens.Toast("접속 주소를 복사했습니다: " + first, 3f);
        }

        public void Leave() => GameNet.Leave();

        public string InviteText => GameNet.IsHost ? GameNet.Server.Address : null;

        static string NameOr(string playerName) => string.IsNullOrWhiteSpace(playerName) ? Environment.UserName : playerName.Trim();
    }
}
