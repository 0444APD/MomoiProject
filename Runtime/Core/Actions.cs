using System;
using System.Collections.Generic;

namespace GameSystems
{
    public enum ActionVisibility
    {
        // 모든 플레이어의 행동 로그에 보인다.
        Public,
        // 행동한 플레이어 본인에게만 보인다(멀티플레이 상대에게 비공개).
        Private,
        // 개발자 모니터에만 보인다.
        Developer,
    }

    // 플레이어 행동은 메서드 호출이 아니라 데이터다.
    // 이 한 경로가 행동 로그·되돌리기·리플레이·멀티플레이(서버 검증)를 함께 담당한다.
    [Serializable]
    public abstract class GameAction
    {
        public int actorId;
        public long tick;

        public virtual ActionVisibility Visibility => ActionVisibility.Public;
        public abstract string Describe();
    }

    public enum ActionStatus
    {
        Pending,
        Committed,
        Undone,
        Failed,
    }

    public sealed class ActionRecord
    {
        public long JournalId;
        public long TransactionId;
        public GameAction Action;
        public ActionStatus Status;
        public string Error;
    }

    public sealed class ActionDispatcher : IInspectable
    {
        readonly Dictionary<Type, Action<GameAction>> _handlers = new Dictionary<Type, Action<GameAction>>();
        readonly List<ActionRecord> _log = new List<ActionRecord>();
        readonly Dictionary<long, ActionRecord> _byTransaction = new Dictionary<long, ActionRecord>();
        readonly Journal _journal;
        readonly StateStore _state;
        readonly GameClock _clock;
        readonly int _logCapacity;

        public event Action<ActionRecord> Executed;
        // 행동의 트랜잭션이 시작·중단될 때(난수 상태 보존 등 상태 저장소 밖 자원의 되돌리기용).
        public event Action<long> TransactionStarted;
        public event Action<long> TransactionAborted;
        public event Action<ActionRecord> StatusChanged;

        public ActionDispatcher(Journal journal, StateStore state, GameClock clock, int logCapacity = 1024)
        {
            _journal = journal;
            _state = state;
            _clock = clock;
            _logCapacity = Math.Max(16, logCapacity);
            _state.TransactionUndone += tx => SetStatus(tx.Id, ActionStatus.Undone);
            _state.TransactionCommitted += tx => SetStatus(tx.Id, ActionStatus.Committed);
        }

        public string InspectName => "행동";
        public IReadOnlyList<ActionRecord> Log => _log;

        public void Register<T>(Action<T> handler) where T : GameAction
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (_handlers.ContainsKey(typeof(T)))
                throw new InvalidOperationException($"[GameSystems] 행동 '{typeof(T).Name}'의 처리기가 이미 등록되어 있습니다.");
            _handlers[typeof(T)] = a => handler((T)a);
        }

        public bool IsRegistered(Type actionType) => _handlers.ContainsKey(actionType);

        public ActionRecord Execute(GameAction action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (!_handlers.TryGetValue(action.GetType(), out var handler))
                throw new InvalidOperationException($"[GameSystems] 행동 '{action.GetType().Name}'의 처리기가 없습니다. Actions.Register로 등록하세요.");

            action.tick = _clock.Tick;
            var entry = _journal.Append(JournalKind.Action, "Actions", action.Describe());
            var record = new ActionRecord { JournalId = entry.Id, Action = action, Status = ActionStatus.Pending };
            var tx = _state.Begin(entry.Id, action.Describe());
            record.TransactionId = tx.Id;
            entry.TransactionId = tx.Id;
            TransactionStarted?.Invoke(tx.Id);

            try
            {
                using (_journal.Cause(entry.Id)) handler(action);
                _state.End();
            }
            catch (Exception e)
            {
                _state.Abort();
                TransactionAborted?.Invoke(tx.Id);
                record.Status = ActionStatus.Failed;
                record.Error = e.Message;
                _journal.Append(JournalKind.Error, "Actions", $"행동 실패, 변경 취소: {action.Describe()} — {e.Message}", transactionId: tx.Id);
                Add(record);
                throw;
            }

            Add(record);
            Executed?.Invoke(record);
            return record;
        }

        void Add(ActionRecord record)
        {
            _log.Add(record);
            _byTransaction[record.TransactionId] = record;
            if (_log.Count > _logCapacity)
            {
                _byTransaction.Remove(_log[0].TransactionId);
                _log.RemoveAt(0);
            }
        }

        void SetStatus(long transactionId, ActionStatus status)
        {
            if (!_byTransaction.TryGetValue(transactionId, out var record)) return;
            record.Status = status;
            StatusChanged?.Invoke(record);
        }

        public void Inspect(InspectNode root)
        {
            root.Add("기록된 행동", _log.Count.ToString());
            for (int i = _log.Count - 1; i >= 0 && i >= _log.Count - 50; i--)
            {
                var record = _log[i];
                var node = root.Add(record.Action.Describe(), StatusText(record.Status));
                node.Tag = $"틱 {record.Action.tick}";
            }
        }

        public static string StatusText(ActionStatus status)
        {
            switch (status)
            {
                case ActionStatus.Pending: return "확정 대기";
                case ActionStatus.Committed: return "확정";
                case ActionStatus.Undone: return "무름";
                default: return "실패";
            }
        }
    }
}
