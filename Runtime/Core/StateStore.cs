using System;
using System.Collections.Generic;
using System.Globalization;

namespace GameSystems
{
    // Session: 게임 내용. 되감기·무르기 대상.
    // Meta: 도전과제·누적 수치 같은 프로필 상태. 확정(Commit)된 행동만 반영된다.
    public enum StateZone
    {
        Session,
        Meta,
    }

    public sealed class StateChange
    {
        public long TransactionId;
        public string Key;
        public StateZone Zone;
        public object Old;
        public object New;
    }

    // 행동 하나로 일어난 변화의 묶음. 되돌리기 단위.
    public sealed class Transaction
    {
        public long Id;
        public long JournalId;
        public string Label;
        public readonly List<StateChange> SessionChanges = new List<StateChange>();
        public readonly List<StateChange> MetaChanges = new List<StateChange>();
    }

    // 모든 게임 상태가 사는 곳. 값은 Set으로만 바뀌고, 바뀔 때마다 이전값·새값이 기록된다.
    // 저장 가능한 값: null, bool, 숫자(double로 정규화), string.
    public sealed class StateStore : IInspectable
    {
        sealed class Slot
        {
            public StateZone Zone;
            public object Value;
            public object Default;
            public string Label;
        }

        readonly Dictionary<string, Slot> _slots = new Dictionary<string, Slot>(StringComparer.Ordinal);
        readonly Dictionary<string, object> _preloaded = new Dictionary<string, object>(StringComparer.Ordinal);
        readonly List<Transaction> _uncommitted = new List<Transaction>();
        // 키별로 마지막으로 '적용된' 변화의 기록 id. 그 변화로 일어난 일(조건 충족 등)의 원인으로 쓴다.
        readonly Dictionary<string, long> _lastEntry = new Dictionary<string, long>(StringComparer.Ordinal);
        readonly Journal _journal;
        Transaction _active;
        long _nextTransactionId = 1;

        // 적용된 값이 바뀌었을 때(Session: 즉시, Meta: 확정 시).
        public event Action<string> Changed;
        // Meta 값이 확정 대기열에 들어가거나 빠졌을 때.
        public event Action<string> PendingChanged;
        public event Action<Transaction> TransactionUndone;
        public event Action<Transaction> TransactionCommitted;

        public StateStore(Journal journal)
        {
            _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        }

        public string InspectName => "상태";
        public int UncommittedCount => _uncommitted.Count;
        public bool InTransaction => _active != null;
        public IReadOnlyList<Transaction> Uncommitted => _uncommitted;
        public IEnumerable<string> Keys => _slots.Keys;

        // ── 정의 ───────────────────────────────────────────────

        public void Define(string key, StateZone zone, object defaultValue = null, string label = null)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("상태 키가 비어 있습니다.", nameof(key));
            var value = Normalize(key, defaultValue);

            if (_slots.TryGetValue(key, out var existing))
            {
                if (existing.Zone != zone)
                    throw new InvalidOperationException($"[GameSystems] 상태 키 '{key}'가 이미 {existing.Zone} 구역으로 정의되어 있습니다.");
                existing.Default = value;
                if (label != null) existing.Label = label;
                return;
            }

            var slot = new Slot { Zone = zone, Default = value, Value = value, Label = label ?? key };
            if (_preloaded.TryGetValue(key, out var loaded))
            {
                slot.Value = loaded;
                _preloaded.Remove(key);
            }
            _slots.Add(key, slot);
        }

        public bool IsDefined(string key) => key != null && _slots.ContainsKey(key);

        public StateZone ZoneOf(string key) => GetSlot(key).Zone;

        public string LabelOf(string key) => _slots.TryGetValue(key, out var slot) ? slot.Label : key;

        // ── 읽기 ───────────────────────────────────────────────

        // Meta 키는 확정 대기 중인 값이 있으면 그 값을 돌려준다(행동 중 읽기 일관성).
        public object Get(string key)
        {
            var slot = GetSlot(key);
            if (slot.Zone == StateZone.Meta && TryGetPending(key, out var pending)) return pending;
            return slot.Value;
        }

        // 확정된 값만.
        public object GetCommitted(string key) => GetSlot(key).Value;

        public bool HasPending(string key) => TryGetPending(key, out _);

        public long LastEntryId(string key) => key != null && _lastEntry.TryGetValue(key, out var id) ? id : 0;

        public double GetNumber(string key)
        {
            var value = Get(key);
            return value == null ? double.NaN : Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        public bool GetBool(string key)
        {
            var value = Get(key);
            return value is bool b ? b : value is double d && d != 0;
        }

        public string GetString(string key) => Get(key)?.ToString();

        // ── 쓰기 ───────────────────────────────────────────────

        public void Set(string key, object value)
        {
            var slot = GetSlot(key);
            var normalized = Normalize(key, value);
            var old = Get(key);
            if (Equals(old, normalized)) return;

            if (_active == null)
            {
                slot.Value = normalized;
                _lastEntry[key] = _journal.Append(JournalKind.State, "State", Describe(key, old, normalized), key, old, normalized).Id;
                Changed?.Invoke(key);
                return;
            }

            var change = new StateChange
            {
                TransactionId = _active.Id,
                Key = key,
                Zone = slot.Zone,
                Old = old,
                New = normalized,
            };

            if (slot.Zone == StateZone.Session)
            {
                slot.Value = normalized;
                _active.SessionChanges.Add(change);
                _lastEntry[key] = _journal.Append(JournalKind.State, "State", Describe(key, old, normalized), key, old, normalized, _active.Id).Id;
                Changed?.Invoke(key);
            }
            else
            {
                _active.MetaChanges.Add(change);
                _journal.Append(JournalKind.State, "State", Describe(key, old, normalized) + " (확정 대기)", key, old, normalized, _active.Id);
                PendingChanged?.Invoke(key);
            }
        }

        // ── 트랜잭션 ───────────────────────────────────────────

        public Transaction Begin(long journalId, string label)
        {
            if (_active != null)
                throw new InvalidOperationException($"[GameSystems] 트랜잭션 '{_active.Label}'이 진행 중입니다. 행동 안에서 다른 행동을 실행할 수 없습니다.");
            _active = new Transaction { Id = _nextTransactionId++, JournalId = journalId, Label = label };
            return _active;
        }

        public void End()
        {
            if (_active == null) return;
            // 값이 바뀌지 않은 행동(빗나감 등)도 하나의 무르기 단위로 남긴다(난수 소비도 되돌려야 하므로).
            _uncommitted.Add(_active);
            _active = null;
        }

        // 행동 실행 중 예외가 나면 진행 중이던 변화를 모두 되돌린다(원자성).
        public void Abort()
        {
            if (_active == null) return;
            var tx = _active;
            _active = null;
            RevertSession(tx, "중단");
            foreach (var change in tx.MetaChanges) PendingChanged?.Invoke(change.Key);
        }

        // 확정: 대기 중인 Meta 변화를 순서대로 적용한다. 확정된 행동은 더 이상 되돌릴 수 없다.
        public void Commit()
        {
            if (_active != null)
                throw new InvalidOperationException("[GameSystems] 트랜잭션 진행 중에는 확정할 수 없습니다.");
            if (_uncommitted.Count == 0) return;

            var committed = new List<Transaction>(_uncommitted);
            _uncommitted.Clear();

            var entry = _journal.Append(JournalKind.Commit, "State", $"확정: 행동 {committed.Count}개");
            var changedKeys = new List<string>();
            foreach (var tx in committed)
            {
                // 확정으로 적용되는 변화의 원인은 그 변화를 만든 행동이다(행동 → 상태 → 달성 → 보상으로 이어진다).
                using (_journal.Cause(tx.JournalId != 0 ? tx.JournalId : entry.Id))
                {
                    foreach (var change in tx.MetaChanges)
                    {
                        var slot = _slots[change.Key];
                        var old = slot.Value;
                        slot.Value = change.New;
                        _lastEntry[change.Key] = _journal.Append(JournalKind.State, "State", Describe(change.Key, old, change.New) + " (확정)", change.Key, old, change.New, tx.Id).Id;
                        if (!changedKeys.Contains(change.Key)) changedKeys.Add(change.Key);
                    }
                }
            }

            foreach (var tx in committed) TransactionCommitted?.Invoke(tx);
            foreach (var key in changedKeys) Changed?.Invoke(key);
        }

        // 확정되지 않은 마지막 행동을 되돌린다. Session 변화는 역적용, Meta 대기분은 폐기.
        public bool UndoLast()
        {
            if (_active != null)
                throw new InvalidOperationException("[GameSystems] 트랜잭션 진행 중에는 되돌릴 수 없습니다.");
            if (_uncommitted.Count == 0) return false;

            var tx = _uncommitted[_uncommitted.Count - 1];
            _uncommitted.RemoveAt(_uncommitted.Count - 1);

            var entry = _journal.Append(JournalKind.Undo, "State", $"무르기: {tx.Label}", transactionId: tx.Id);
            using (_journal.Cause(entry.Id)) RevertSession(tx, "무르기");
            foreach (var change in tx.MetaChanges) PendingChanged?.Invoke(change.Key);

            TransactionUndone?.Invoke(tx);
            return true;
        }

        void RevertSession(Transaction tx, string reason)
        {
            for (int i = tx.SessionChanges.Count - 1; i >= 0; i--)
            {
                var change = tx.SessionChanges[i];
                var slot = _slots[change.Key];
                var current = slot.Value;
                slot.Value = change.Old;
                _lastEntry[change.Key] = _journal.Append(JournalKind.Undo, "State", $"{reason}: " + Describe(change.Key, current, change.Old), change.Key, current, change.Old, tx.Id).Id;
                Changed?.Invoke(change.Key);
            }
        }

        bool TryGetPending(string key, out object value)
        {
            if (_active != null && FindLast(_active.MetaChanges, key, out value)) return true;
            for (int i = _uncommitted.Count - 1; i >= 0; i--)
                if (FindLast(_uncommitted[i].MetaChanges, key, out value)) return true;
            value = null;
            return false;
        }

        static bool FindLast(List<StateChange> changes, string key, out object value)
        {
            for (int i = changes.Count - 1; i >= 0; i--)
            {
                if (changes[i].Key != key) continue;
                value = changes[i].New;
                return true;
            }
            value = null;
            return false;
        }

        // ── 저장·불러오기 ───────────────────────────────────────

        public Dictionary<string, object> Export(StateZone zone)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var pair in _slots)
                if (pair.Value.Zone == zone) result[pair.Key] = pair.Value.Value;
            return result;
        }

        // 아직 정의되지 않은 키는 보관했다가 Define 시점에 적용한다.
        public void Import(Dictionary<string, object> values, StateZone zone)
        {
            foreach (var pair in values)
            {
                var value = Normalize(pair.Key, pair.Value);
                if (_slots.TryGetValue(pair.Key, out var slot))
                {
                    if (slot.Zone != zone) continue;
                    slot.Value = value;
                    Changed?.Invoke(pair.Key);
                }
                else
                {
                    _preloaded[pair.Key] = value;
                }
            }
        }

        public void ResetZone(StateZone zone)
        {
            foreach (var pair in _slots)
            {
                if (pair.Value.Zone != zone || Equals(pair.Value.Value, pair.Value.Default)) continue;
                Set(pair.Key, pair.Value.Default);
            }
        }

        // ── 관찰 ───────────────────────────────────────────────

        public void Inspect(InspectNode root)
        {
            var groups = new SortedDictionary<string, InspectNode>(StringComparer.Ordinal);
            foreach (var pair in _slots)
            {
                var prefix = pair.Key.Contains(":") ? pair.Key.Substring(0, pair.Key.IndexOf(':')) : "기타";
                if (!groups.TryGetValue(prefix, out var group))
                {
                    group = new InspectNode(prefix);
                    groups.Add(prefix, group);
                }
                var node = group.Add(pair.Value.Label, Format(Get(pair.Key)), pair.Key);
                node.Tag = pair.Value.Zone == StateZone.Meta ? "메타" : "세션";
                if (TryGetPending(pair.Key, out _)) node.Note = $"확정값 {Format(pair.Value.Value)}";
            }
            foreach (var group in groups.Values) root.Children.Add(group);
            root.Add("확정 대기 행동", _uncommitted.Count.ToString(CultureInfo.InvariantCulture));
        }

        // ── 도우미 ─────────────────────────────────────────────

        Slot GetSlot(string key)
        {
            if (key != null && _slots.TryGetValue(key, out var slot)) return slot;
            throw new InvalidOperationException($"[GameSystems] 정의되지 않은 상태 키 '{key}'입니다. Define으로 먼저 등록하세요.");
        }

        static object Normalize(string key, object value)
        {
            switch (value)
            {
                case null: return null;
                case bool _: return value;
                case string _: return value;
                case double _: return value;
                case float f: return (double)f;
                case int i: return (double)i;
                case long l: return (double)l;
                case short s: return (double)s;
                case byte b: return (double)b;
                case uint ui: return (double)ui;
                case decimal m: return (double)m;
                case Enum e: return Convert.ToDouble(e, CultureInfo.InvariantCulture);
            }
            throw new ArgumentException($"[GameSystems] 상태 '{key}'에 저장할 수 없는 값 형식입니다: {value.GetType().Name}. bool·숫자·string만 저장할 수 있습니다.");
        }

        string Describe(string key, object oldValue, object newValue) =>
            $"{LabelOf(key)}  {Format(oldValue)} → {Format(newValue)}";

        public static string Format(object value)
        {
            switch (value)
            {
                case null: return "없음";
                case bool b: return b ? "예" : "아니오";
                case double d: return d.ToString("0.##", CultureInfo.InvariantCulture);
                default: return value.ToString();
            }
        }
    }
}
