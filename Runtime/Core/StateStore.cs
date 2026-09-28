using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GameSystems
{
    // Session: 게임 내용. 되감기·무르기 대상.
    // Meta: 도전과제·누적 수치 같은 프로필 상태. 되감기지 않고 저장된다.
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

    // 데이터 값(클래스·컬렉션) 하나를 한 번 수정한 기록. Before·After는 그 시점의 스냅샷이다.
    public sealed class DataRecord
    {
        public string Key;
        public StateZone Zone;
        public object Before;
        public object After;
        public IReadOnlyList<DataChange> Changes;
    }

    // 행동 하나로 일어난 변화의 묶음. 되돌리기 단위.
    public sealed class Transaction
    {
        public long Id;
        public long JournalId;
        public string Label;
        public readonly List<StateChange> SessionChanges = new List<StateChange>();
        public readonly List<StateChange> MetaChanges = new List<StateChange>();
        public readonly List<DataRecord> DataChanges = new List<DataRecord>();
    }

    // 모든 게임 상태가 사는 곳. 값이 바뀔 때마다 이전값·새값이 기록되고, 확정 전이면 되돌릴 수 있다.
    //
    // 값의 두 가지 형태
    //  · 단순 값: null, bool, 숫자(double로 정규화), string. Set으로 바꾼다.
    //    Meta 단순 값은 확정 대기로 들어갔다가 확정 시 적용된다(대기 중에도 Get에는 보인다).
    //  · 데이터 값: 임의의 클래스·구조체·리스트·배열·딕셔너리. DefineData로 등록하고 Modify로 바꾼다.
    //    바뀐 곳은 경로 단위로 기록되고(예: 인벤토리.items[#12].level 3 → 4), 무르면 인스턴스를 유지한 채 제자리로 되돌아간다.
    //    컬렉션 요소에는 저장소가 내부 ID를 자동으로 붙여 불러오기·되돌리기 뒤에도 같은 물건으로 이어진다.
    //    Modify를 거치지 않은 직접 수정은 DetectUntrackedChanges가 켜져 있으면 찾아내 경고한다(무르기·기록에서 빠지므로).
    public sealed class StateStore : IInspectable
    {
        sealed class Slot
        {
            public StateZone Zone;
            public object Value;
            public object Default;
            public string Label;
            // 데이터 값일 때만: 선언 타입과 마지막으로 기록된 상태의 스냅샷.
            public Type DataType;
            public object Shadow;
        }

        const int MaxLoggedChanges = 12;

        readonly Dictionary<string, Slot> _slots = new Dictionary<string, Slot>(StringComparer.Ordinal);
        readonly Dictionary<string, object> _preloaded = new Dictionary<string, object>(StringComparer.Ordinal);
        readonly List<Transaction> _uncommitted = new List<Transaction>();
        // 키별로 마지막으로 '적용된' 변화의 기록 id. 그 변화로 일어난 일(조건 충족 등)의 원인으로 쓴다.
        readonly Dictionary<string, long> _lastEntry = new Dictionary<string, long>(StringComparer.Ordinal);
        readonly Journal _journal;
        Transaction _active;
        long _nextTransactionId = 1;

        // 적용된 값이 바뀌었을 때(Session·데이터 값: 즉시, Meta 단순 값: 확정 시).
        public event Action<string> Changed;
        // Meta 단순 값이 확정 대기열에 들어가거나 빠졌을 때.
        public event Action<string> PendingChanged;
        // 데이터 값이 바뀌었을 때 바뀐 경로 목록과 함께.
        public event Action<string, IReadOnlyList<DataChange>> DataChanged;
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
        public DataIdentity Identity { get; } = new DataIdentity();
        // Modify를 거치지 않은 데이터 수정을 찾아 경고한다. 값 전체를 비교하므로 출시 빌드에서는 끈다.
        public bool DetectUntrackedChanges = true;

        // ── 정의 ───────────────────────────────────────────────

        public void Define(string key, StateZone zone, object defaultValue = null, string label = null)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("상태 키가 비어 있습니다.", nameof(key));
            if (defaultValue != null && IsDataType(defaultValue.GetType()))
            {
                DefineDataCore(key, zone, defaultValue, label);
                return;
            }
            var value = Normalize(key, defaultValue);

            if (_slots.TryGetValue(key, out var existing))
            {
                if (existing.Zone != zone)
                    throw new InvalidOperationException($"[GameSystems] 상태 키 '{key}'가 이미 {existing.Zone} 구역으로 정의되어 있습니다.");
                if (existing.DataType != null)
                    throw new InvalidOperationException($"[GameSystems] 상태 키 '{key}'는 데이터 값({DataModel.FriendlyName(existing.DataType)})으로 정의되어 있습니다.");
                existing.Default = value;
                if (label != null) existing.Label = label;
                return;
            }

            var slot = new Slot { Zone = zone, Default = value, Value = value, Label = label ?? key };
            if (_preloaded.TryGetValue(key, out var loaded))
            {
                _preloaded.Remove(key);
                if (TryNormalize(loaded, out var normalized)) slot.Value = normalized;
                else _journal.Append(JournalKind.Warning, "State", $"저장된 '{slot.Label}' 값의 형식이 맞지 않아 기본값을 씁니다.", key);
            }
            _slots.Add(key, slot);
        }

        // 데이터 값(클래스·컬렉션)을 등록한다. initial을 주지 않으면 T의 기본 생성자로 만든다.
        // 돌려받은 인스턴스가 저장소가 관리하는 실제 값이다. 읽기는 자유, 수정은 Modify로 한다.
        public T DefineData<T>(string key, StateZone zone, T initial = null, string label = null) where T : class
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("상태 키가 비어 있습니다.", nameof(key));
            DefineDataCore(key, zone, initial ?? (T)DataModel.Create(typeof(T)), label);
            return (T)_slots[key].Value;
        }

        void DefineDataCore(string key, StateZone zone, object value, string label)
        {
            var type = value.GetType();
            var problems = DataModel.Validate(type);
            if (problems.Count > 0)
                throw new ArgumentException($"[GameSystems] 상태 '{key}'에 담을 수 없는 필드가 있습니다.\n  " + string.Join("\n  ", problems));

            if (_slots.TryGetValue(key, out var existing))
            {
                if (existing.Zone != zone)
                    throw new InvalidOperationException($"[GameSystems] 상태 키 '{key}'가 이미 {existing.Zone} 구역으로 정의되어 있습니다.");
                if (existing.DataType != type)
                    throw new InvalidOperationException($"[GameSystems] 상태 키 '{key}'는 이미 {(existing.DataType == null ? "단순 값" : DataModel.FriendlyName(existing.DataType))}으로 정의되어 있습니다.");
                existing.Default = DataModel.Clone(value, null);
                if (label != null) existing.Label = label;
                return;
            }

            var slot = new Slot { Zone = zone, Label = label ?? key, DataType = type, Default = DataModel.Clone(value, null), Value = value };
            if (_preloaded.TryGetValue(key, out var loaded))
            {
                _preloaded.Remove(key);
                slot.Value = LoadData(key, slot, loaded);
            }
            slot.Shadow = DataModel.Clone(slot.Value, Identity);
            _slots.Add(key, slot);
        }

        static bool IsDataType(Type type)
        {
            var kind = DataModel.Of(type).Kind;
            return kind == DataKind.Object || kind == DataKind.List || kind == DataKind.Array || kind == DataKind.Dictionary;
        }

        public bool IsDefined(string key) => key != null && _slots.ContainsKey(key);

        public bool IsData(string key) => key != null && _slots.TryGetValue(key, out var slot) && slot.DataType != null;

        public Type DataTypeOf(string key) => key != null && _slots.TryGetValue(key, out var slot) ? slot.DataType : null;

        public StateZone ZoneOf(string key) => GetSlot(key).Zone;

        public string LabelOf(string key) => _slots.TryGetValue(key, out var slot) ? slot.Label : key;

        // ── 읽기 ───────────────────────────────────────────────

        // Meta 단순 값은 확정 대기 중인 값이 있으면 그 값을 돌려준다(행동 중 읽기 일관성).
        // 데이터 값은 저장소가 관리하는 실제 인스턴스를 돌려준다.
        public object Get(string key)
        {
            var slot = GetSlot(key);
            if (slot.DataType == null && slot.Zone == StateZone.Meta && TryGetPending(key, out var pending)) return pending;
            return slot.Value;
        }

        public T GetData<T>(string key) where T : class
        {
            var value = GetSlot(key).Value;
            if (value == null || value is T typed) return (T)value;
            throw new InvalidCastException($"[GameSystems] 상태 '{key}'는 {DataModel.FriendlyName(value.GetType())}입니다({typeof(T).Name} 아님).");
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
            if (slot.DataType != null)
            {
                if (value != null && !slot.DataType.IsInstanceOfType(value))
                    throw new ArgumentException($"[GameSystems] 상태 '{key}'에는 {DataModel.FriendlyName(slot.DataType)}만 넣을 수 있습니다({value.GetType().Name}).");
                var copy = DataModel.Clone(value, Identity);
                ModifyCore(key, slot, live => DataModel.ApplyInPlace(live, copy, Identity));
                return;
            }

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
                _lastEntry[key] = _journal.Append(JournalKind.State, "State", Describe(key, old, normalized) + " (확정 대기)", key, old, normalized, _active.Id).Id;
                PendingChanged?.Invoke(key);
            }
        }

        // 데이터 값을 바꾼다. 예: store.Modify<Inventory>("inventory", inv => inv.items[0].level++);
        // 바뀐 곳을 찾아 기록하고, 행동 안이면 무르기 단위에 넣는다. 예외가 나면 바꾸기 전으로 되돌린다.
        public void Modify<T>(string key, Action<T> change) where T : class
        {
            if (change == null) throw new ArgumentNullException(nameof(change));
            var slot = GetDataSlot(key);
            if (slot.Value != null && !(slot.Value is T))
                throw new InvalidCastException($"[GameSystems] 상태 '{key}'는 {DataModel.FriendlyName(slot.Value.GetType())}입니다({typeof(T).Name} 아님).");
            ModifyCore(key, slot, live =>
            {
                change((T)live);
                return live;
            });
        }

        void ModifyCore(string key, Slot slot, Func<object, object> change)
        {
            if (DetectUntrackedChanges) AdoptUntracked(key, slot);
            var before = slot.Shadow;
            try
            {
                slot.Value = change(slot.Value);
            }
            catch
            {
                slot.Value = DataModel.ApplyInPlace(slot.Value, before, Identity);
                throw;
            }

            var changes = DataModel.Diff(before, slot.Value, Identity);
            if (changes.Count == 0) return;
            // 기록에는 그 시점의 모습을 남긴다(나중에 실제 인스턴스가 바뀌어도 기록은 그대로).
            foreach (var c in changes)
            {
                if (c.New != null && IsDataType(c.New.GetType())) c.New = DataModel.Clone(c.New, Identity);
            }
            var after = DataModel.Clone(slot.Value, Identity);
            slot.Shadow = after;

            var record = new DataRecord { Key = key, Zone = slot.Zone, Before = before, After = after, Changes = changes };
            _active?.DataChanges.Add(record);
            LogChanges(key, slot, changes, JournalKind.State, null, _active?.Id ?? 0);
            Changed?.Invoke(key);
            DataChanged?.Invoke(key, changes);
        }

        void LogChanges(string key, Slot slot, List<DataChange> changes, JournalKind kind, string prefix, long transactionId)
        {
            long last = 0;
            for (int i = 0; i < changes.Count; i++)
            {
                if (i == MaxLoggedChanges)
                {
                    last = _journal.Append(kind, "State", $"{prefix}{slot.Label}  … 외 {changes.Count - i}곳", key, transactionId: transactionId).Id;
                    break;
                }
                var c = changes[i];
                last = _journal.Append(kind, "State", prefix + DescribeData(slot, c), key, Leaf(c.Old), Leaf(c.New), transactionId).Id;
            }
            if (last != 0) _lastEntry[key] = last;
        }

        static object Leaf(object value) => value == null || !IsDataType(value.GetType()) ? value : DataModel.Summary(value);

        static string DescribeData(Slot slot, DataChange change)
        {
            var text = change.ToString();
            return string.IsNullOrEmpty(change.Path) ? $"{slot.Label}{text}" : $"{slot.Label}.{text}";
        }

        // 마지막 기록 이후 Modify 없이 바뀐 곳이 있으면 받아들이고 경고를 남긴다(무르기·인과 기록에서는 빠진다).
        public int CheckUntrackedChanges()
        {
            int count = 0;
            foreach (var pair in _slots.ToList())
                if (pair.Value.DataType != null && AdoptUntracked(pair.Key, pair.Value)) count++;
            return count;
        }

        bool AdoptUntracked(string key, Slot slot)
        {
            var changes = DataModel.Diff(slot.Shadow, slot.Value, Identity);
            if (changes.Count == 0) return false;
            var where = changes[0].Path + (changes.Count > 1 ? $" 외 {changes.Count - 1}곳" : "");
            _lastEntry[key] = _journal.Append(JournalKind.Warning, "State",
                $"'{slot.Label}'이(가) Modify를 거치지 않고 바뀌었습니다({where}). 무르기와 원인 기록에서 빠집니다. state.Modify(\"{key}\", …)로 바꾸세요.", key).Id;
            slot.Shadow = DataModel.Clone(slot.Value, Identity);
            Changed?.Invoke(key);
            DataChanged?.Invoke(key, changes);
            return true;
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
            Revert(tx, "중단");
            foreach (var change in tx.MetaChanges) PendingChanged?.Invoke(change.Key);
        }

        // 확정: 대기 중인 Meta 단순 값 변화를 순서대로 적용한다. 확정된 행동은 더 이상 되돌릴 수 없다.
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

        // 확정되지 않은 마지막 행동을 되돌린다. Session·데이터 변화는 역적용, Meta 대기분은 폐기.
        public bool UndoLast()
        {
            if (_active != null)
                throw new InvalidOperationException("[GameSystems] 트랜잭션 진행 중에는 되돌릴 수 없습니다.");
            if (_uncommitted.Count == 0) return false;

            var tx = _uncommitted[_uncommitted.Count - 1];
            _uncommitted.RemoveAt(_uncommitted.Count - 1);

            var entry = _journal.Append(JournalKind.Undo, "State", $"무르기: {tx.Label}", transactionId: tx.Id);
            using (_journal.Cause(entry.Id)) Revert(tx, "무르기");
            foreach (var change in tx.MetaChanges) PendingChanged?.Invoke(change.Key);

            TransactionUndone?.Invoke(tx);
            return true;
        }

        void Revert(Transaction tx, string reason)
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

            // 데이터 값: 수정 직전 스냅샷으로 제자리 복원(인스턴스·요소 ID 유지).
            for (int i = tx.DataChanges.Count - 1; i >= 0; i--)
            {
                var record = tx.DataChanges[i];
                var slot = _slots[record.Key];
                if (DetectUntrackedChanges) AdoptUntracked(record.Key, slot);
                var before = slot.Shadow;
                slot.Value = DataModel.ApplyInPlace(slot.Value, record.Before, Identity);
                slot.Shadow = DataModel.Clone(slot.Value, Identity);
                var changes = DataModel.Diff(before, slot.Value, Identity);
                if (changes.Count == 0) continue;
                LogChanges(record.Key, slot, changes, JournalKind.Undo, $"{reason}: ", tx.Id);
                Changed?.Invoke(record.Key);
                DataChanged?.Invoke(record.Key, changes);
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
        // 데이터 값은 저장 트리(필드 이름 기준, enum은 이름, 요소 ID 포함)로 바꿔 내보낸다.

        public Dictionary<string, object> Export(StateZone zone)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var pair in _slots)
            {
                var slot = pair.Value;
                if (slot.Zone != zone) continue;
                if (slot.DataType == null)
                {
                    result[pair.Key] = slot.Value;
                    continue;
                }
                var warnings = new List<string>();
                result[pair.Key] = DataModel.ToTree(slot.Value, slot.DataType, Identity, warnings, slot.Label);
                foreach (var warning in warnings.Distinct()) _journal.Append(JournalKind.Warning, "Save", warning, pair.Key);
            }
            return result;
        }

        // 아직 정의되지 않은 키는 보관했다가 Define 시점에 적용한다.
        public void Import(Dictionary<string, object> values, StateZone zone)
        {
            foreach (var pair in values)
            {
                if (!_slots.TryGetValue(pair.Key, out var slot))
                {
                    _preloaded[pair.Key] = pair.Value;
                    continue;
                }
                if (slot.Zone != zone) continue;
                if (slot.DataType != null)
                {
                    slot.Value = LoadData(pair.Key, slot, pair.Value);
                    slot.Shadow = DataModel.Clone(slot.Value, Identity);
                }
                else if (TryNormalize(pair.Value, out var value))
                {
                    slot.Value = value;
                }
                else
                {
                    _journal.Append(JournalKind.Warning, "Save", $"저장된 '{slot.Label}' 값의 형식이 맞지 않아 무시합니다.", pair.Key);
                    continue;
                }
                Changed?.Invoke(pair.Key);
            }
        }

        object LoadData(string key, Slot slot, object tree)
        {
            var warnings = new List<string>();
            var loaded = DataModel.FromTree(tree, slot.DataType, Identity, warnings, slot.Label);
            foreach (var warning in warnings.Distinct()) _journal.Append(JournalKind.Warning, "Save", warning, key);
            return DataModel.ApplyInPlace(slot.Value, loaded, Identity);
        }

        public void ResetZone(StateZone zone)
        {
            foreach (var pair in _slots.ToList())
            {
                var slot = pair.Value;
                if (slot.Zone != zone) continue;
                if (slot.DataType != null) Set(pair.Key, slot.Default);
                else if (!Equals(slot.Value, slot.Default)) Set(pair.Key, slot.Default);
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
                var slot = pair.Value;
                // 데이터 값은 글자 한 칸으로 고칠 수 없으므로 편집 키를 넘기지 않는다.
                var node = group.Add(slot.Label, Format(Get(pair.Key)), slot.DataType == null ? pair.Key : null);
                node.Tag = slot.Zone == StateZone.Meta ? "메타" : "세션";
                if (TryGetPending(pair.Key, out _)) node.Note = $"확정값 {Format(slot.Value)}";
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

        Slot GetDataSlot(string key)
        {
            var slot = GetSlot(key);
            if (slot.DataType == null)
                throw new InvalidOperationException($"[GameSystems] 상태 '{key}'는 단순 값입니다. Set으로 바꾸세요.");
            return slot;
        }

        static bool TryNormalize(object value, out object normalized)
        {
            try
            {
                normalized = Normalize(null, value);
                return true;
            }
            catch (ArgumentException)
            {
                normalized = null;
                return false;
            }
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
            throw new ArgumentException($"[GameSystems] 상태 '{key}'에 저장할 수 없는 값 형식입니다: {value.GetType().Name}. 클래스·컬렉션은 DefineData로 등록하세요.");
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
                case string s: return s;
                default: return DataModel.Summary(value);
            }
        }
    }
}
