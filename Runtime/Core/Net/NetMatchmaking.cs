using System;
using System.Collections.Generic;
using System.Linq;

namespace GBible
{
    // 대기표 하나(한 계정 = 대기표 하나).
    public sealed class NetTicket
    {
        public string Id;
        public string Account;
        public string Name;
        public double Rating;
        // 매칭 종류(빈 글자 = 기본). 같은 종류끼리만 묶는다.
        public string Mode = "";
        // 처음 줄을 선 시각(초). 거절로 다시 줄을 서도 그대로 둔다(기다린 시간을 잃지 않게).
        public double QueuedAt;
    }

    // 묶인 매치(아직 수락 전).
    public sealed class NetMatchProposal
    {
        public string MatchId;
        public string Mode;
        public readonly List<NetTicket> Tickets = new List<NetTicket>();
    }

    // 매칭(가짜: LocalMatchmaker, 출시: AWS GameLift FlexMatch 어댑터 — 같은 창구). 게임 스레드에서 부른다.
    public interface INetMatchmaker
    {
        void Enqueue(NetTicket ticket);
        void Cancel(string account);
        bool IsQueued(string account);
        // 시간을 흘려 묶는다(now = 초).
        void Tick(double now);
        // 묶인 매치를 하나 꺼낸다.
        bool TryTake(out NetMatchProposal proposal);
    }

    // 가짜 매칭(로컬): 오래 기다린 사람부터, 실력 폭 안의 사람을 정원만큼 묶는다. 기다릴수록 폭이 넓어지고, 최대 대기를 넘으면 아무나와.
    // AWS FlexMatch의 규칙(실력 폭 넓히기·최대 대기)과 같은 모양이라 바꿔 끼워도 게임 쪽 흐름이 같다.
    public sealed class LocalMatchmaker : INetMatchmaker
    {
        readonly List<NetTicket> _queue = new List<NetTicket>();
        readonly Queue<NetMatchProposal> _ready = new Queue<NetMatchProposal>();
        int _nextMatch = 1;

        // 한 매치의 인원.
        public int PlayersPerMatch = 2;
        // 처음 실력 폭(±점수)과 1초마다 넓어지는 양.
        public double BaseWindow = 100;
        public double WidenPerSecond = 10;
        // 이 시간(초)을 넘게 기다리면 실력과 상관없이 묶는다.
        public double MaxWaitSeconds = 60;

        public int QueuedCount => _queue.Count;

        public void Enqueue(NetTicket ticket)
        {
            if (ticket == null || string.IsNullOrEmpty(ticket.Account)) return;
            _queue.RemoveAll(t => t.Account == ticket.Account);
            _queue.Add(ticket);
        }

        public void Cancel(string account) => _queue.RemoveAll(t => t.Account == account);

        public bool IsQueued(string account) => _queue.Any(t => t.Account == account);

        public void Tick(double now)
        {
            var need = Math.Max(1, PlayersPerMatch);
            foreach (var mode in _queue.Select(t => t.Mode ?? "").Distinct().ToList())
            {
                while (true)
                {
                    var waiting = _queue.Where(t => (t.Mode ?? "") == mode).OrderBy(t => t.QueuedAt).ToList();
                    if (waiting.Count < need) break;
                    var formed = false;
                    foreach (var anchor in waiting)
                    {
                        var waited = Math.Max(0, now - anchor.QueuedAt);
                        var window = waited >= MaxWaitSeconds ? double.PositiveInfinity : BaseWindow + WidenPerSecond * waited;
                        var group = new List<NetTicket> { anchor };
                        foreach (var other in waiting.Where(t => t != anchor).OrderBy(t => Math.Abs(t.Rating - anchor.Rating)))
                        {
                            if (group.Count >= need) break;
                            // 더 오래 기다린 쪽의 폭으로 판단한다(최대 대기를 넘긴 사람은 새로 온 사람과도 묶인다 — 최대 대기 보장).
                            var otherWindow = now - other.QueuedAt >= MaxWaitSeconds ? double.PositiveInfinity : BaseWindow + WidenPerSecond * Math.Max(0, now - other.QueuedAt);
                            var gap = Math.Abs(other.Rating - anchor.Rating);
                            if (gap <= Math.Max(window, otherWindow)) group.Add(other);
                        }
                        if (group.Count < need) continue;
                        var proposal = new NetMatchProposal { MatchId = "m" + _nextMatch++ + "-" + Guid.NewGuid().ToString("N").Substring(0, 6), Mode = mode };
                        proposal.Tickets.AddRange(group);
                        foreach (var ticket in group) _queue.Remove(ticket);
                        _ready.Enqueue(proposal);
                        formed = true;
                        break;
                    }
                    if (!formed) break;
                }
            }
        }

        public bool TryTake(out NetMatchProposal proposal)
        {
            if (_ready.Count > 0)
            {
                proposal = _ready.Dequeue();
                return true;
            }
            proposal = null;
            return false;
        }
    }

    // 매치 결과 한 사람 몫(게임이 정한다). place: 1등 = 1(같은 등수 = 무승부).
    public sealed class NetMatchResult
    {
        public int Player;
        public int Place = 1;
    }

    public static class NetRating
    {
        // 등수로 실력 점수를 고친다(Elo — 둘씩 짝지어 기대 승률과 비교). k = 한 판의 최대 변화.
        public static void Apply(IList<(NetAccount account, int place)> results, double k = 32)
        {
            var count = results.Count;
            if (count < 2) return;
            var deltas = new double[count];
            for (var i = 0; i < count; i++)
            for (var j = 0; j < count; j++)
            {
                if (i == j) continue;
                var a = results[i];
                var b = results[j];
                var expected = 1.0 / (1.0 + Math.Pow(10, (b.account.rating - a.account.rating) / 400.0));
                var actual = a.place < b.place ? 1.0 : a.place == b.place ? 0.5 : 0.0;
                deltas[i] += k * (actual - expected) / (count - 1);
            }
            for (var i = 0; i < count; i++)
            {
                var (account, place) = results[i];
                account.rating = Math.Round(account.rating + deltas[i], 1);
                var best = results.Min(r => r.place);
                var tied = results.Count(r => r.place == place) > 1;
                if (place == best && !tied) account.wins++;
                else if (place == best) account.draws++;
                else account.losses++;
            }
        }
    }
}
