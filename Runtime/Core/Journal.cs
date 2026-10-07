using System;
using System.Collections.Generic;

namespace GBible
{
    public enum JournalKind
    {
        Action,
        Event,
        Data,
        Snapshot,
        Undo,
        Achievement,
        Reward,
        System,
        Warning,
        Error,
    }

    // 통합 기록의 한 줄. CauseId로 "무엇 때문에 일어났는가"를 잇는다.
    public sealed class JournalEntry
    {
        public long Id;
        public long CauseId;
        public long Tick;
        public long TransactionId;
        public JournalKind Kind;
        public string System;
        public string Key;
        public string Message;
        public object Old;
        public object New;
        // 이 기록의 내용 객체(이벤트·행동, 바뀐 클래스 값의 사본). 모니터가 탐색기로 연다.
        public object Payload;
        // 코드 형태의 메시지(모니터에서 Alt를 누르면 보인다). 없으면 Message만.
        public string Code;
    }

    // 모든 시스템이 공유하는 링 버퍼 기록.
    // Cause(id) 범위 안에서 추가된 기록은 자동으로 그 id를 원인으로 갖는다.
    public sealed class Journal
    {
        readonly JournalEntry[] _buffer;
        readonly Stack<long> _causes = new Stack<long>();
        int _start;
        int _count;
        long _nextId = 1;

        public Func<long> TickSource;
        public event Action<JournalEntry> Appended;

        public Journal(int capacity = 4096)
        {
            if (capacity < 16) capacity = 16;
            _buffer = new JournalEntry[capacity];
        }

        public int Capacity => _buffer.Length;
        public int Count => _count;
        public long LastId => _nextId - 1;
        public long CurrentCause => _causes.Count > 0 ? _causes.Peek() : 0;

        int _muted;
        // 미리보기 안의 줄: 보관하지 않고 Appended도 보내지 않지만, 원인 잇기(Cause·CauseChain·Find)는 실제 기록과 똑같이 돈다
        // (상태 효과의 "그 이벤트를 받는 도중에 건 것은 세지 않는다"가 미리보기에서도 같게). 번호는 음수로 따로 매기고
        // (실제 기록의 번호와 겹치지 않는다), 미리보기가 끝나면 버린다. 번호는 미리보기마다 이어서 매겨 끝난 미리보기의 번호가 다음 것과 겹치지 않는다.
        readonly List<JournalEntry> _quiet = new List<JournalEntry>();
        long _quietNext = -1;
        long _quietFirst = -1;

        // 이 안에서는 기록하지 않는다(미리보기 실행). 돌려주는 줄은 보관되지 않고 Appended도 오지 않는다(Id는 음수 — 이 안에서만 찾을 수 있다).
        public IDisposable Mute() => new MuteScope(this);

        public bool IsMuted => _muted > 0;

        sealed class MuteScope : IDisposable
        {
            Journal _journal;

            public MuteScope(Journal journal)
            {
                _journal = journal;
                if (journal._muted++ == 0) journal._quietFirst = journal._quietNext;
            }

            public void Dispose()
            {
                if (_journal == null) return;
                if (--_journal._muted == 0) _journal._quiet.Clear();
                _journal = null;
            }
        }

        public JournalEntry Append(JournalKind kind, string system, string message,
                                   string key = null, object oldValue = null, object newValue = null,
                                   long transactionId = 0)
        {
            if (_muted > 0)
            {
                var quiet = new JournalEntry
                {
                    Id = _quietNext--, CauseId = CurrentCause, Kind = kind, System = system, Key = key, Message = message, Old = oldValue, New = newValue, TransactionId = transactionId,
                };
                _quiet.Add(quiet);
                return quiet;
            }
            var entry = new JournalEntry
            {
                Id = _nextId++,
                CauseId = CurrentCause,
                Tick = TickSource != null ? TickSource() : 0,
                TransactionId = transactionId,
                Kind = kind,
                System = system,
                Key = key,
                Message = message,
                Old = oldValue,
                New = newValue,
            };

            int index = (_start + _count) % _buffer.Length;
            _buffer[index] = entry;
            if (_count < _buffer.Length) _count++;
            else _start = (_start + 1) % _buffer.Length;

            Appended?.Invoke(entry);
            return entry;
        }

        public CauseScope Cause(long entryId) => new CauseScope(this, entryId);

        public readonly struct CauseScope : IDisposable
        {
            readonly Journal _journal;

            internal CauseScope(Journal journal, long id)
            {
                _journal = journal;
                // 끝난 미리보기의 번호(음수)는 원인이 아니다(이미 버린 줄).
                journal._causes.Push(id < 0 && journal._muted == 0 ? 0 : id);
            }

            public void Dispose() => _journal._causes.Pop();
        }

        // 오래된 것부터 최신 순.
        public IEnumerable<JournalEntry> Entries
        {
            get
            {
                for (int i = 0; i < _count; i++)
                    yield return _buffer[(_start + i) % _buffer.Length];
            }
        }

        public JournalEntry Find(long id)
        {
            if (id < 0)
            {
                var index = _quietFirst - id;
                return _muted > 0 && index >= 0 && index < _quiet.Count ? _quiet[(int)index] : null;
            }
            if (_count == 0) return null;
            long first = _buffer[_start].Id;
            long offset = id - first;
            if (offset < 0 || offset >= _count) return null;
            return _buffer[(_start + (int)offset) % _buffer.Length];
        }

        // 뿌리 원인부터 id 자신까지.
        public List<JournalEntry> CauseChain(long id)
        {
            var chain = new List<JournalEntry>();
            var guard = 0;
            for (var entry = Find(id); entry != null && guard++ < 256; entry = entry.CauseId != 0 ? Find(entry.CauseId) : null)
                chain.Add(entry);
            chain.Reverse();
            return chain;
        }

        // id가 직접·간접적으로 일으킨 기록들.
        public List<JournalEntry> Consequences(long id)
        {
            var result = new List<JournalEntry>();
            var ids = new HashSet<long> { id };
            foreach (var entry in Entries)
            {
                if (entry.CauseId != 0 && ids.Contains(entry.CauseId))
                {
                    result.Add(entry);
                    ids.Add(entry.Id);
                }
            }
            return result;
        }

        public void Clear()
        {
            Array.Clear(_buffer, 0, _buffer.Length);
            _start = 0;
            _count = 0;
        }
    }
}
