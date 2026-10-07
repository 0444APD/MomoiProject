using System;
using System.Collections.Generic;
using System.Globalization;

namespace GBible
{
    // 멀티플레이 메시지 규격(서버 ↔ 클라이언트). 메시지 하나 = 전송 한 번(전송 계층이 경계를 지킨다).
    // 첫 바이트가 종류(NetMessage), 나머지는 MirrorWriter 형식(부호 없는 가변 정수·UTF-8 글자).
    //
    //   클라이언트 → 서버: Hello(이름·입장 표·게임 버전·형식 번호표) · Act(요청 번호 + 행동) · Dev(개발 명령) · Ping · Stream · Bye
    //   서버 → 클라이언트: Welcome(플레이어 번호·형식 번호표·명단) · Reject(이유) · Batch(바뀐 곳·이벤트 묶음)
    //                      · ActResult(요청 번호·성공·실패 글) · Players(명단) · Pong · Stream · Bye
    //
    // Batch 한 개 = 서버에서 일어난 한 묶음(행동 하나 또는 틱 하나). 그 안의 기록은 일어난 차례대로 적용한다:
    //   Reset(처음 모습 통째) · Delta(바뀐 곳 — ChangeSet.Forward) · Value(단순 값) · Tree(예전 방식 데이터 통째) · Hide(더는 안 보냄) · Event
    public enum NetMessage : byte
    {
        Hello = 1,
        Welcome = 2,
        Reject = 3,
        Act = 4,
        ActResult = 5,
        Batch = 6,
        Ping = 7,
        Pong = 8,
        Players = 9,
        Bye = 10,
        Stream = 11,
        Dev = 12,
        // 매칭큐(NetHub): 로비 · 줄 서기 · 매치 성사 · 수락 · 취소 · 끝.
        Lobby = 13,
        Queue = 14,
        Queued = 15,
        MatchFound = 16,
        MatchAnswer = 17,
        MatchCancelled = 18,
        MatchEnded = 19,
        CancelQueue = 20,
    }

    // Batch 안의 기록 종류.
    public enum NetRecord : byte
    {
        Reset = 1,
        Delta = 2,
        Value = 3,
        Tree = 4,
        Hide = 5,
        Event = 6,
    }

    public static class NetProtocol
    {
        // 메시지 규격 버전. 바뀌면 서로 다른 버전은 Hello에서 거절한다.
        public const int Version = 1;

        // ── 덩어리(길이 + 바이트) ──────────────────────────────

        public static void WriteBlock(MirrorWriter w, byte[] data, int start, int count)
        {
            w.UVar((ulong)count);
            if (count > 0) w.Bytes(data, start, count);
        }

        public static void WriteBlock(MirrorWriter w, MirrorWriter block) => WriteBlock(w, block.Data, 0, block.Length);

        // 덩어리 하나를 건너뛰며 그 범위를 돌려준다(복사하지 않는다).
        public static void ReadBlock(MirrorReader r, out int start, out int end)
        {
            var count = r.UVar();
            if (count > (ulong)Math.Max(0, r.End - r.Position)) throw new FormatException("[MomoiProject] 멀티플레이 메시지가 잘렸습니다.");
            start = r.Position;
            end = start + (int)count;
            r.Position = end;
        }

        public static byte[] ReadBlockCopy(MirrorReader r)
        {
            ReadBlock(r, out var start, out var end);
            var copy = new byte[end - start];
            Buffer.BlockCopy(r.Data, start, copy, 0, copy.Length);
            return copy;
        }

        // 받은 이름(입장 표의 이름 — 화면·기록·계정 키에 쓰인다): 제어 문자를 빼고 앞뒤 공백을 지우고 32자로 자른다.
        public const int MaxNameLength = 32;

        public static string CleanName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            var b = new System.Text.StringBuilder(Math.Min(name.Length, MaxNameLength));
            foreach (var c in name)
            {
                if (char.IsControl(c)) continue;
                if (b.Length >= MaxNameLength) break;
                b.Append(c);
            }
            // 잘린 자리에 짝 잃은 서러게이트가 남지 않게.
            if (b.Length > 0 && char.IsHighSurrogate(b[b.Length - 1])) b.Length--;
            return b.ToString().Trim();
        }

        // ── 글자 목록(형식 번호표 등) ──────────────────────────

        public static void WriteStrings(MirrorWriter w, IReadOnlyList<string> items)
        {
            w.UVar((ulong)(items?.Count ?? 0));
            if (items == null) return;
            foreach (var item in items) w.Str(item);
        }

        // 형식 번호표(Mirror.TypeTable)로 받는 이름 수의 한도. 게임의 데이터 형식은 이만큼 많지 않다 — 받은 개수만큼 배열을 잡기 전에 막는다
        // (1바이트짜리 빈 이름 수천만 개를 담은 32MB 메시지 하나가 입장 확인 전에 수백 MB를 잡게 하지 못하게).
        public const int MaxTypeTable = 1 << 16;

        public static string[] ReadStrings(MirrorReader r, int max = int.MaxValue)
        {
            var count = r.Count();
            if (count > max) throw new FormatException($"[MomoiProject] 이름 수({count})가 한도({max})를 넘습니다(받은 데이터가 깨졌거나 잘못되었습니다).");
            var items = new string[count];
            for (var i = 0; i < count; i++) items[i] = r.Str();
            return items;
        }

        // ── 단순 값(플레이어 데이터의 단순 값 칸: 숫자는 double로 맞춰져 있다) ──

        public static void WriteScalar(MirrorWriter w, object value)
        {
            switch (value)
            {
                case null: w.Byte(0); return;
                case bool b: w.Byte(1); w.Bool(b); return;
                case double d: w.Byte(2); w.Double(d); return;
                case string s: w.Byte(3); w.Str(s); return;
                case DateTime t: w.Byte(4); w.Long(t.Ticks); w.Byte((byte)t.Kind); return;
                case DateTimeOffset o: w.Byte(5); w.Long(o.Ticks); w.Long(o.Offset.Ticks); return;
                case TimeSpan span: w.Byte(6); w.Long(span.Ticks); return;
                case Guid g: w.Byte(7); w.Str(g.ToString("N")); return;
            }
            if (value is IConvertible && TypeResolver.IsNumeric(value.GetType()))
            {
                w.Byte(2);
                w.Double(Convert.ToDouble(value, CultureInfo.InvariantCulture));
                return;
            }
            w.Byte(3);
            w.Str(Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        public static object ReadScalar(MirrorReader r)
        {
            switch (r.Byte())
            {
                case 0: return null;
                case 1: return r.Bool();
                case 2: return r.Double();
                case 3: return r.Str();
                case 4:
                {
                    var ticks = r.Long();
                    return new DateTime(ticks, (DateTimeKind)r.Byte());
                }
                case 5:
                {
                    var ticks = r.Long();
                    return new DateTimeOffset(ticks, new TimeSpan(r.Long()));
                }
                case 6: return new TimeSpan(r.Long());
                case 7: return Guid.ParseExact(r.Str(), "N");
                default: throw new FormatException("[MomoiProject] 모르는 단순 값 표시입니다.");
            }
        }

        // ── 메시지 머리 ───────────────────────────────────────

        public static MirrorWriter Begin(NetMessage kind, MirrorWriter reuse = null)
        {
            var w = reuse ?? new MirrorWriter();
            w.Clear();
            w.Byte((byte)kind);
            return w;
        }

        public static NetMessage KindOf(byte[] data, int length) =>
            length > 0 ? (NetMessage)data[0] : throw new FormatException("[MomoiProject] 빈 멀티플레이 메시지입니다.");
    }

    // 접속한 사람 한 명(서버 쪽). 행동의 actorId = Id.
    public sealed class NetPlayer
    {
        public int Id;
        public string Name;
        // 입장 확인이 정한 계정 키(가짜 인증이면 이름). 다시 접속한 사람을 같은 플레이어로 잇는 데 쓴다.
        public string Account;
        // 전송 계층의 연결 번호. 서버와 같은 프로그램의 플레이어(방장)는 -1.
        public int Connection = -1;
        public bool Connected;
        // 게임 코드가 붙여 두는 값(팀·준비 여부 등).
        public object Tag;

        // 서버와 같은 프로그램의 플레이어(방장).
        public bool IsLocal;
        public override string ToString() => $"{Name}(#{Id})";
    }

    // 클라이언트 쪽 명단 한 줄.
    public sealed class NetPlayerInfo
    {
        public int Id;
        public string Name;
        public bool Connected;
    }

    // 입장 요청(Hello)의 내용 — 입장 확인(NetServer.Admit)이 본다.
    public sealed class NetHello
    {
        public string Name;
        // 보낸 연결 번호(서버가 채운다 — 입장 확인이 연결로 계정을 찾을 때).
        public int Connection;
        // 입장 표(가짜 인증이면 비어도 된다. Steam 인증이면 인증 티켓 등).
        public string Token;
        public string GameVersion;
    }

    // 입장 확인 결과.
    public struct NetAdmission
    {
        public bool Accepted;
        public string Reason;
        // 계정 키(같은 계정이 다시 오면 같은 플레이어 번호). 비우면 이름.
        public string Account;
        // 보일 이름(비우면 Hello의 이름).
        public string Name;

        public static NetAdmission Accept(string account = null, string name = null) => new NetAdmission { Accepted = true, Account = account, Name = name };
        public static NetAdmission Refuse(string reason) => new NetAdmission { Accepted = false, Reason = reason };
    }

    // 매칭큐 로비의 내 계정(서버 저장소의 값).
    public sealed class NetLobbyInfo
    {
        public string Name;
        public double Rating;
        public int Wins;
        public int Losses;
        public int Draws;
    }

    // 성사된 매치(수락 전).
    public sealed class NetMatchFound
    {
        public string MatchId;
        public string Mode;
        public string[] Players;
        // 수락할 시간(초).
        public double Seconds;
    }

    // 끝난 매치의 내 결과.
    public sealed class NetMatchEnd
    {
        public string MatchId;
        // false면 취소(정원이 들어오지 않음 등 — 점수 변화 없음).
        public bool Finished;
        public string Reason;
        public int Place;
        public double Rating;
        public double RatingDelta;
    }

    // 서버가 거절한 행동(클라이언트 쪽 알림). Key가 있으면 플레이어용 실패(GameFailure)의 텍스트 키.
    public sealed class NetActionFailure
    {
        public long RequestId;
        public GameAction Action;
        public string Key;
        public string Message;
        // 실패 글의 인자(이름·숫자). 키를 이쪽 언어로 다시 만들 때 쓴다.
        public IReadOnlyDictionary<string, object> Args;
    }
}
