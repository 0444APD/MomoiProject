using System;
using System.Collections.Generic;
using System.Linq;

namespace GBible
{
    // 예약(Schedule): 행동을 나중에 실행한다. 저장·Undo·멀티에서도 그대로다(예약 자체가 플레이어 데이터).
    //   시간: Game.ScheduleAfter(2.5, new Explode { target = enemy })   — 게임 시간 2.5초 뒤, 프레임 끝에 행동 하나로 실행
    //   이벤트: Game.ScheduleOn<TurnEnded>(3, new Explode { … })        — TurnEnded를 3번 받은 순간, 그 행동 안에서 실행
    // 넘긴 행동은 예약에 맡긴다: 대상 필드(id가 있는 객체)는 id만 남겨 저장하고, 실행할 때 지금 데이터의 객체로 다시 찾는다.
    // 대상이 그때 없으면(죽은 적) 실행하지 않고 지운다. 시간 예약이 실패하면 기록에 남기고 지운다(매 프레임 다시 시도하지 않게).
    // 이벤트 예약은 그 이벤트를 쏜 행동의 일부로 돈다(같이 Undo된다). 실패하면 그 예약이 바꾼 것만 되돌리고(저장점) 타임라인에 경고를 남기며,
    // 이벤트를 쏜 행동은 계속된다.
    // 기획자가 DB에서 쓰는 지연 효과("3턴 뒤 폭발")는 상태 효과의 '끝날 때 결과'로 만든다.
    [Serializable]
    public sealed class ScheduledAction
    {
        public string id;
        public string source;
        public double at = -1;      // 게임 시간 예약이면 실행 시각, 아니면 -1
        public string eventType;    // 이벤트 예약이면 이벤트 형식 이름
        public int remaining;       // 이벤트 예약의 남은 횟수
        public GameAction action;
    }

    [Serializable]
    public sealed class ScheduleList
    {
        public List<ScheduledAction> items = new List<ScheduledAction>();
    }

    public sealed class ScheduleSystem
    {
        public const string SessionKey = "$schedule";
        public const string MetaKey = "$schedule.meta";

        readonly GameSession _session;

        internal ScheduleSystem(GameSession session)
        {
            _session = session;
            session.Events.SubscribeAll(OnEvent);
        }

        public string After(double seconds, GameAction action, string source = null, bool permanent = false)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            // 게임 시간은 판마다 0부터 다시 흐르므로 판을 넘는(영구) 시간 예약은 시각이 맞지 않는다.
            if (permanent) throw new InvalidOperationException("[MomoiProject] 영구 예약은 이벤트 예약(ScheduleOn)만 쓸 수 있습니다(게임 시간은 판마다 0부터 다시 흐릅니다).");
            return Add(new ScheduledAction { at = _session.Clock.Time + Math.Max(0, seconds) }, action, source, permanent);
        }

        public string On(Type eventType, int count, GameAction action, string source = null, bool permanent = false)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (eventType == null) throw new ArgumentNullException(nameof(eventType));
            return Add(new ScheduledAction { eventType = eventType.FullName, remaining = Math.Max(1, count) }, action, source, permanent);
        }

        public bool Cancel(string id) => RemoveWhere(s => s.id == id) > 0;
        public int CancelBySource(string source) => RemoveWhere(s => s.source == source);

        public IReadOnlyList<ScheduledAction> All =>
            Keys().SelectMany(k => _session.Data.Get<ScheduleList>(k)?.items ?? new List<ScheduledAction>()).ToList();

        string Add(ScheduledAction item, GameAction action, string source, bool permanent)
        {
            ActionReferences.Detach(action);
            item.id = _session.Game.NewId("$schedule");
            item.source = source ?? "";
            item.action = action;
            var key = Ensure(permanent);
            _session.Data.Modify<ScheduleList>(key, list => list.items.Add(item));
            return item.id;
        }

        string Ensure(bool permanent)
        {
            var key = permanent ? MetaKey : SessionKey;
            if (!_session.Data.IsDefined(key))
                _session.Data.Define(key, permanent ? DataZone.Meta : DataZone.Session, new ScheduleList(), permanent ? "영구 예약" : "예약");
            return key;
        }

        // 등록된 키(불러온 저장의 값이 기다리고 있으면 지금 등록한다).
        IEnumerable<string> Keys()
        {
            foreach (var key in new[] { SessionKey, MetaKey })
            {
                if (!_session.Data.IsDefined(key))
                {
                    if (!_session.Data.IsPending(key)) continue;
                    Ensure(key == MetaKey);
                }
                yield return key;
            }
        }

        int RemoveWhere(Func<ScheduledAction, bool> match)
        {
            var removed = 0;
            foreach (var key in Keys().ToList())
            {
                var list = _session.Data.Get<ScheduleList>(key);
                if (list == null || !list.items.Any(match)) continue;
                _session.Data.Modify<ScheduleList>(key, l => removed += l.items.RemoveAll(s => match(s)));
            }
            return removed;
        }

        // 이벤트 예약: 그 이벤트를 받을 때마다 하나씩 줄이고, 0이 되면 그 자리(그 이벤트를 쏜 행동 안)에서 실행한다.
        void OnEvent(object evt)
        {
            var type = evt.GetType().FullName;
            var due = new List<ScheduledAction>();
            foreach (var key in Keys().ToList())
            {
                var list = _session.Data.Get<ScheduleList>(key);
                if (list == null || !list.items.Any(s => s.eventType == type)) continue;
                _session.Data.Modify<ScheduleList>(key, l =>
                {
                    foreach (var item in l.items.Where(s => s.eventType == type).ToList())
                    {
                        item.remaining--;
                        if (item.remaining > 0) continue;
                        l.items.Remove(item);
                        due.Add(item);
                    }
                });
            }
            foreach (var item in due)
            {
                var point = _session.Data.Mark();
                try { Run(item); }
                catch (Exception e)
                {
                    _session.Data.RollbackTo(point);
                    _session.Journal.Append(JournalKind.Warning, "Schedule", $"예약 실행이 실패해 그 예약이 바꾼 것만 되돌렸습니다: {item.action?.Describe()} — {e.Message}");
                }
            }
        }

        // 프레임 끝(행동 밖): 시간이 된 예약을 하나씩 행동으로 실행한다.
        internal void Tick()
        {
            if (_session.Data.InTransaction) return;
            var now = _session.Clock.Time;
            var due = All.Where(s => s.at >= 0 && s.at <= now).OrderBy(s => s.at).ThenBy(s => s.id, StringComparer.Ordinal).ToList();
            foreach (var item in due)
            {
                try
                {
                    _session.Do(new ScheduledRun { schedule = item.id });
                }
                catch (Exception e)
                {
                    _session.Journal.Append(JournalKind.Warning, "Schedule", $"예약 실행이 실패해 지웁니다: {item.action?.Describe()} — {e.Message}");
                    Cancel(item.id);
                }
            }
        }

        // 꺼내 둔 예약 하나를 실행한다(행동 안에서). 대상이 없으면 건너뛴다.
        internal void Run(ScheduledAction item)
        {
            if (item?.action == null) return;
            if (!ActionReferences.CanResolve(item.action, _session.Game))
            {
                _session.Journal.Append(JournalKind.System, "Schedule", $"대상이 없어 예약을 실행하지 않았습니다: {item.action.Describe()}");
                return;
            }
            _session.Do(item.action);
        }

        internal ScheduledAction Take(string id)
        {
            ScheduledAction found = null;
            foreach (var key in Keys().ToList())
            {
                var list = _session.Data.Get<ScheduleList>(key);
                var item = list?.items.FirstOrDefault(s => s.id == id);
                if (item == null) continue;
                _session.Data.Modify<ScheduleList>(key, l => l.items.Remove(item));
                found = item;
                break;
            }
            return found;
        }
    }

    // 시간 예약 하나를 꺼내 실행하는 행동(꺼내기와 실행이 한 묶음: Undo하면 예약이 돌아온다).
    [Serializable]
    public sealed class ScheduledRun : GameAction
    {
        public string schedule;

        public override ActionVisibility Visibility => ActionVisibility.Developer;
        public override string Describe() => "예약 실행";

        protected override void Run()
        {
            var item = Game.Session.Schedules.Take(schedule);
            if (item != null) Game.Session.Schedules.Run(item);
        }
    }
}
