using System;
using System.Collections.Generic;

namespace GBible
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
    //
    // 행동 하나 = 이 클래스를 상속하고, 필요한 정보를 필드로 두고, Run을 쓴다.
    //   [Serializable] public class PlayCard : GameAction
    //   {
    //       public DeckCard card;     // 대상(string id가 있는 클래스) — 몇 개든
    //       public Fighter target;    // 없으면 null
    //       public int times = 1;     // 그냥 값
    //       protected override void Run() { … card·target은 지금 데이터 안의 실제 객체 … }
    //   }
    //   Game.Do(new PlayCard { card = deckCard, target = enemy });
    // string id가 있는 클래스 형식의 필드(와 그 리스트·배열)는 참조로 다룬다: 기록·전송에는 id만 남고, 실행 직전에
    // 그 id로 지금 데이터에서 다시 찾아 넣는다(ActionReferences). 그래서 Undo·불러오기 뒤의 옛 객체나 다른 컴퓨터의 객체를 넣어도 맞다.
    [Serializable]
    public abstract class GameAction
    {
        public int actorId;
        public long tick;

        // 실행 중일 때만 들어 있다: 데이터·난수·이벤트·다른 행동을 쓰는 창구.
        [NonSerialized] GameApi _game;
        protected GameApi Game => _game;

        public virtual ActionVisibility Visibility => ActionVisibility.Public;

        // 타임라인에 보일 글. 기본: Block 이름 + 대상 id들.
        public virtual string Describe() => ActionReferences.Describe(this);

        // 처리 내용. Actions.Register로 처리기를 따로 등록한 행동은 쓰지 않는다.
        protected virtual void Run() =>
            throw new InvalidOperationException($"[MomoiProject] 행동 '{GetType().Name}'의 처리기가 없습니다. 행동 클래스에 Run을 쓰거나 Actions.Register로 처리기를 등록하세요.");

        internal void RunIn(GameApi game)
        {
            var previous = _game;
            _game = game;
            try { Run(); }
            finally { _game = previous; }
        }
    }

    public enum ActionStatus
    {
        // 실행됨(행동은 실행되는 즉시 끝난다).
        Done,
        // Undo·되감기로 되돌려짐.
        Undone,
        // 실행 중 오류로 변경이 모두 취소됨.
        Failed,
    }

    public sealed class ActionRecord
    {
        public long JournalId;
        public long TransactionId;
        public GameAction Action;
        public ActionStatus Status;
        public string Error;
        // 다른 행동 안에서 실행된 행동(바깥 행동의 Undo·변경 묶음에 들어간다).
        public bool Nested;
    }

    public sealed class ActionDispatcher : IInspectable
    {
        readonly Dictionary<Type, Action<GameAction>> _handlers = new Dictionary<Type, Action<GameAction>>();
        readonly List<ActionRecord> _log = new List<ActionRecord>();
        readonly Dictionary<long, ActionRecord> _byTransaction = new Dictionary<long, ActionRecord>();
        readonly Journal _journal;
        readonly PlayerDataStore _state;
        readonly GameClock _clock;
        readonly int _logCapacity;

        public event Action<ActionRecord> Executed;
        // 행동의 트랜잭션이 시작·중단될 때(난수 상태 보존 등 플레이어 데이터 저장소 밖 자원의 되돌리기용).
        public event Action<long> TransactionStarted;
        public event Action<long> TransactionAborted;
        public event Action<ActionRecord> StatusChanged;

        public ActionDispatcher(Journal journal, PlayerDataStore state, GameClock clock, int logCapacity = 1024)
        {
            _journal = journal;
            _state = state;
            _clock = clock;
            _logCapacity = Math.Max(16, logCapacity);
            _state.TransactionUndone += tx => SetStatus(tx.Id, ActionStatus.Undone);
        }

        public string InspectName => "행동";
        public IReadOnlyList<ActionRecord> Log => _log;

        public void Register<T>(Action<T> handler) where T : GameAction
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (_handlers.ContainsKey(typeof(T)))
                throw new InvalidOperationException($"[MomoiProject] 행동 '{typeof(T).Name}'의 처리기가 이미 등록되어 있습니다.");
            _handlers[typeof(T)] = a => handler((T)a);
        }

        public bool IsRegistered(Type actionType) => _handlers.ContainsKey(actionType);

        // 처리를 클래스 안에 쓴 행동(Run)의 실행 방법(세션이 정한다).
        public Action<GameAction> SelfRun;
        // 실행 직전 준비(세션이 정한다): 참조 필드를 지금 데이터의 객체로 다시 찾아 넣는다.
        public Action<GameAction> Prepare;

        // 미리보기 중(Preview 안). 규칙 코드가 밖으로 나가는 일(분석 전송 등)을 건너뛸 때 본다.
        public bool IsPreviewing { get; private set; }

        // 미리보기: 행동을 실행해 보고 read로 결과를 읽은 뒤 모두 되돌린다.
        // 기록·Undo·행동 로그·행동 뒤에 받는 쪽(SubscribeAfterAction)·도전과제 달성이 남지 않고, 난수도 돌아간다.
        // 행동이 실패하면 바꾼 것을 되돌린 뒤 그 예외를 그대로 던진다. 행동 밖(화면 코드)에서 부른다.
        public T Preview<T>(GameAction action, Func<T> read)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (_state.InTransaction) throw new InvalidOperationException("[MomoiProject] 행동 안에서는 미리보기를 할 수 없습니다(행동 밖, 화면 코드에서 부르세요).");
            if (!_handlers.TryGetValue(action.GetType(), out var registered))
                registered = SelfRun ?? throw new InvalidOperationException($"[MomoiProject] 행동 '{action.GetType().Name}'을 실행할 세션이 없습니다.");
            var prepare = Prepare;
            using (_journal.Mute())
            {
                var tx = _state.Begin(0, "미리보기");
                IsPreviewing = true;
                TransactionStarted?.Invoke(tx.Id);
                try
                {
                    _state.Track(() =>
                    {
                        prepare?.Invoke(action);
                        registered(action);
                    });
                    return read == null ? default : read();
                }
                finally
                {
                    _state.Abort();
                    IsPreviewing = false;
                    TransactionAborted?.Invoke(tx.Id);
                }
            }
        }

        public ActionRecord Execute(GameAction action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            // 등록한 처리기가 없으면 클래스 안의 Run을 쓴다. 어느 쪽이든 실행 직전에 참조 필드를 다시 찾는다(행동 안이라 실패하면 전체 취소).
            if (!_handlers.TryGetValue(action.GetType(), out var registered))
                registered = SelfRun ?? throw new InvalidOperationException($"[MomoiProject] 행동 '{action.GetType().Name}'을 실행할 세션이 없습니다.");
            var prepare = Prepare;
            Action<GameAction> handler = a =>
            {
                prepare?.Invoke(a);
                registered(a);
            };

            action.tick = _clock.Tick;
            // 행동 안에서 부른 행동(카드 사용 안의 드로우, 행동 안의 Game.Use 등): 바깥 행동의 일부로 그 자리에서 실행한다.
            // Undo·변경 묶음은 바깥 행동 하나. 실패하면 예외가 바깥 행동으로 올라가 전체가 취소된다.
            if (_state.InTransaction)
            {
                var inner = _journal.Append(JournalKind.Action, "Actions", action.Describe());
                inner.Payload = action;
                inner.TransactionId = _state.ActiveTransactionId;
                using (_journal.Cause(inner.Id)) handler(action);
                var nested = new ActionRecord { JournalId = inner.Id, Action = action, Status = ActionStatus.Done, TransactionId = _state.ActiveTransactionId, Nested = true };
                Executed?.Invoke(nested);
                return nested;
            }
            var entry = _journal.Append(JournalKind.Action, "Actions", action.Describe());
            entry.Payload = action;
            var record = new ActionRecord { JournalId = entry.Id, Action = action, Status = ActionStatus.Done };
            var tx = _state.Begin(entry.Id, action.Describe());
            record.TransactionId = tx.Id;
            entry.TransactionId = tx.Id;
            TransactionStarted?.Invoke(tx.Id);

            try
            {
                // 처리 중에 꺼낸 데이터 값은 직접 고쳐도 기록된다(PlayerDataStore.Track).
                using (_journal.Cause(entry.Id)) _state.Track(() => handler(action));
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

        // 되감기: 이 기록 id 이후에 실행된 행동을 되돌려진 것으로 표시한다.
        public void MarkUndoneAfter(long journalId)
        {
            foreach (var record in _log)
            {
                if (record.Status != ActionStatus.Done || record.JournalId <= journalId) continue;
                record.Status = ActionStatus.Undone;
                StatusChanged?.Invoke(record);
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
                case ActionStatus.Done: return "실행";
                case ActionStatus.Undone: return "Undo됨";
                default: return "실패";
            }
        }
    }
}
