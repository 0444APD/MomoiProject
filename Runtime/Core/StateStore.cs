using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GameSystems
{
    // Session: 게임 내용(이번 판). 판이 끝나면 사라진다.
    // Meta: 도전과제·누적 수치·인벤토리 같은 프로필 상태. 저장된다.
    // 두 구역은 저장 여부만 다르고 쓰는 법·무르기 규칙은 같다.
    public enum StateZone
    {
        Session,
        Meta,
    }

    // 타입이 붙은 상태 키. 데이터베이스 창에서 이름을 지은 키는 코드 생성(GameIds)으로 이 형태로 제공된다.
    // 문자열 키 대신 쓰면 값의 타입이 컴파일 단계에서 맞춰지고 오타가 날 수 없다.
    public readonly struct StateKey<T>
    {
        public readonly string Id;

        public StateKey(string id)
        {
            Id = id;
        }

        public static implicit operator string(StateKey<T> key) => key.Id;
        public override string ToString() => Id;
    }

    // 단순 값 하나의 변화(무르기용).
    public sealed class StateChange
    {
        public long TransactionId;
        public string Key;
        public StateZone Zone;
        public object Old;
        public object New;
    }

    // 데이터 값 하나가 한 행동 안에서 바뀐 기록. Before는 행동 전 첫 모습, After는 마지막 모습.
    // 같은 행동 안에서 같은 값을 여러 번 고쳐도 기록은 하나로 합친다(무르기 메모리 절약).
    public sealed class DataRecord
    {
        public string Key;
        public StateZone Zone;
        public object Before;
        public object After;
        public List<DataChange> Changes = new List<DataChange>();
    }

    // 행동 하나로 일어난 변화의 묶음. 무르기 단위.
    public sealed class Transaction
    {
        public long Id;
        public long JournalId;
        public string Label;
        public readonly List<StateChange> ValueChanges = new List<StateChange>();
        public readonly List<DataRecord> DataChanges = new List<DataRecord>();
    }

    // 모든 게임 상태가 사는 곳.
    //
    // 규칙은 하나: 값은 바꾸는 즉시 반영되고, 확정 전에는 무를 수 있으며, 확정하면 굳는다.
    // 되돌릴 수 없는 일(보상 지급·저장·외부 전송)만 확정 시점에 일어난다.
    //
    // 담을 수 있는 값: bool, 숫자(double로 정규화), string, 날짜·시간·Guid, 그리고 임의의 클래스·구조체·
    // 리스트·배열·딕셔너리·집합·큐·스택(필드 깊이 무관). 모든 타입이 같은 문법을 쓴다.
    //   등록  Define(키, 구역, 기본값)          읽기  Get<T>(키)
    //   교체  Set(키, 값)                       부분 수정  Modify<T>(키, 값 => ...)   ← 클래스·컬렉션
    // 데이터 값의 변화는 경로 단위로 기록되고(예: 인벤토리.items[#12].level 3 → 4), 무르면 게임 코드가 쥔
    // 인스턴스를 그대로 둔 채 제자리로 돌아간다. 컬렉션 요소에는 내부 ID가 자동으로 붙는다.
    // Modify를 거치지 않은 직접 수정은 DetectUntrackedChanges가 켜져 있으면 찾아내 경고한다.
    public sealed class StateStore : IInspectable
    {
        sealed class Slot
        {
            public StateZone Zone;
            public object Value;
            public object Default;
            public string Label;
            // 단순 값: 선언 타입(없으면 제한 없음). 데이터 값: DataType과 마지막 기록 시점의 스냅샷.
            public Type ValueType;
            public Type DataType;
            public object Shadow;
            public bool SizeWarned;
        }

        const int MaxLoggedChanges = 12;
        // 데이터 값 하나의 요소 수가 이보다 크면 한 번 경고한다(수정마다 전체를 복제·비교하므로).
        public const int LargeValueNodes = 20000;

        readonly Dictionary<string, Slot> _slots = new Dictionary<string, Slot>(StringComparer.Ordinal);
        readonly Dictionary<string, object> _preloaded = new Dictionary<string, object>(StringComparer.Ordinal);
        readonly Dictionary<string, string> _aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly List<Transaction> _uncommitted = new List<Transaction>();
        // 키별로 마지막으로 적용된 변화의 기록 id. 그 변화로 일어난 일(조건 충족 등)의 원인으로 쓴다.
        readonly Dictionary<string, long> _lastEntry = new Dictionary<string, long>(StringComparer.Ordinal);
        readonly Journal _journal;
        Transaction _active;
        long _nextTransactionId = 1;

        // 값이 바뀌었을 때(무르기로 돌아갈 때 포함).
        public event Action<string> Changed;
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

        // 어떤 타입이든 등록한다. 기본값의 타입이 이 키의 타입이 된다(null이면 단순 값, 타입 제한 없음).
        public void Define(string key, StateZone zone, object defaultValue = null, string label = null) =>
            DefineOfType(key, zone, defaultValue?.GetType(), defaultValue, label);

        // 위와 같고, 저장소가 관리하는 실제 값을 돌려준다. 예: var inv = state.Define("player:inventory", StateZone.Meta, new Inventory());
        public T Define<T>(string key, StateZone zone, T defaultValue, string label = null)
        {
            DefineOfType(key, zone, defaultValue?.GetType() ?? typeof(T), defaultValue, label);
            return Get<T>(key);
        }

        // 타입을 지정해 등록한다(데이터베이스 창의 상태 키가 이 경로로 등록된다).
        // 클래스·컬렉션인데 기본값이 없으면 기본 생성자로 만든다.
        public void DefineOfType(string key, StateZone zone, Type type, object defaultValue, string label = null)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("[GameSystems] 상태 키가 비어 있습니다.", nameof(key));
            if (type != null && IsDataType(type))
            {
                DefineData(key, zone, defaultValue ?? DataModel.Create(type), label);
                return;
            }
            DefineValue(key, zone, type, defaultValue, label);
        }

        // 타입이 붙은 키로 등록하고, 저장소가 관리하는 실제 값을 돌려준다.
        public T Define<T>(StateKey<T> key, StateZone zone, T defaultValue = default, string label = null)
        {
            DefineOfType(key.Id, zone, typeof(T), defaultValue, label);
            return Get<T>(key.Id);
        }

        void DefineValue(string key, StateZone zone, Type type, object defaultValue, string label)
        {
            var valueType = type == null ? null : Nullable.GetUnderlyingType(type) ?? type;
            var value = Normalize(key, defaultValue);
            if (_slots.TryGetValue(key, out var existing))
            {
                CheckRedefine(key, existing, zone, null);
                existing.Default = value;
                if (valueType != null) existing.ValueType = valueType;
                if (label != null) existing.Label = label;
                return;
            }

            var slot = new Slot { Zone = zone, Default = value, Value = value, Label = label ?? key, ValueType = valueType };
            if (TakePreloaded(key, out var loaded))
            {
                if (TryNormalize(loaded, out var normalized)) slot.Value = normalized;
                else _journal.Append(JournalKind.Warning, "State", $"저장된 '{slot.Label}' 값의 형식이 맞지 않아 기본값을 씁니다.", key);
            }
            _slots.Add(key, slot);
        }

        void DefineData(string key, StateZone zone, object value, string label)
        {
            var type = value.GetType();
            var problems = DataModel.Validate(type);
            if (problems.Count > 0)
                throw new ArgumentException($"[GameSystems] 상태 '{key}'에 담을 수 없는 필드가 있습니다.\n  " + string.Join("\n  ", problems));

            if (_slots.TryGetValue(key, out var existing))
            {
                CheckRedefine(key, existing, zone, type);
                existing.Default = DataModel.Clone(value, null);
                if (label != null) existing.Label = label;
                return;
            }

            var slot = new Slot { Zone = zone, Label = label ?? key, DataType = type, Default = DataModel.Clone(value, null), Value = value };
            if (TakePreloaded(key, out var loaded)) slot.Value = LoadData(key, slot, loaded);
            slot.Shadow = CloneShadow(slot);
            _slots.Add(key, slot);
        }

        static void CheckRedefine(string key, Slot existing, StateZone zone, Type dataType)
        {
            if (existing.Zone != zone)
                throw new InvalidOperationException($"[GameSystems] 상태 키 '{key}'가 이미 {ZoneName(existing.Zone)} 구역으로 정의되어 있습니다.");
            if (existing.DataType != dataType)
                throw new InvalidOperationException($"[GameSystems] 상태 키 '{key}'는 이미 {TypeText(existing)}(으)로 정의되어 있습니다.");
        }

        // 이름이 바뀐 키. 예전 이름으로 저장된 값을 새 이름으로 불러온다(식별자 이름 변경 시 자동 등록).
        public void AddAlias(string oldKey, string newKey)
        {
            if (string.IsNullOrEmpty(oldKey) || string.IsNullOrEmpty(newKey) || oldKey == newKey) return;
            _aliases[oldKey] = newKey;
            if (_preloaded.TryGetValue(oldKey, out var value))
            {
                _preloaded.Remove(oldKey);
                if (!_preloaded.ContainsKey(newKey)) _preloaded[newKey] = value;
            }
        }

        bool TakePreloaded(string key, out object value)
        {
            if (!_preloaded.TryGetValue(key, out value)) return false;
            _preloaded.Remove(key);
            return true;
        }

        static bool IsDataType(Type type)
        {
            var kind = DataModel.Of(type).Kind;
            return kind == DataKind.Object || kind == DataKind.List || kind == DataKind.Array || kind == DataKind.Dictionary || kind == DataKind.Collection;
        }

        public bool IsDefined(string key) => key != null && _slots.ContainsKey(key);

        public bool IsData(string key) => key != null && _slots.TryGetValue(key, out var slot) && slot.DataType != null;

        // 등록된 타입(데이터 값은 클래스·컬렉션 타입, 단순 값은 선언 타입 또는 null).
        public Type TypeOf(string key) => key != null && _slots.TryGetValue(key, out var slot) ? slot.DataType ?? slot.ValueType : null;

        public StateZone ZoneOf(string key) => GetSlot(key).Zone;

        public string LabelOf(string key) => _slots.TryGetValue(key, out var slot) ? slot.Label : key;

        // ── 읽기 ───────────────────────────────────────────────
        // 데이터 값은 저장소가 관리하는 실제 인스턴스를 돌려준다(읽기는 자유, 수정은 Modify).

        public object Get(string key) => GetSlot(key).Value;

        public T Get<T>(string key) => Convert<T>(key, GetSlot(key).Value);

        public T Get<T>(StateKey<T> key) => Get<T>(key.Id);

        public long LastEntryId(string key) => key != null && _lastEntry.TryGetValue(key, out var id) ? id : 0;

        public double GetNumber(string key)
        {
            var value = Get(key);
            return value == null ? double.NaN : System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        public bool GetBool(string key)
        {
            var value = Get(key);
            return value is bool b ? b : value is double d && d != 0;
        }

        public string GetString(string key) => Get(key)?.ToString();

        static T Convert<T>(string key, object value)
        {
            if (value == null) return default;
            if (value is T typed) return typed;
            var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            try
            {
                if (target.IsEnum) return (T)Enum.ToObject(target, System.Convert.ToInt64(value, CultureInfo.InvariantCulture));
                if (value is IConvertible && TypeResolver.IsNumeric(target)) return (T)System.Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
            }
            catch (Exception e) when (e is InvalidCastException || e is FormatException || e is OverflowException)
            {
            }
            throw new InvalidCastException($"[GameSystems] 상태 '{key}'의 값({DataModel.FriendlyName(value.GetType())})을 {DataModel.FriendlyName(typeof(T))}(으)로 읽을 수 없습니다.");
        }

        // ── 쓰기 ───────────────────────────────────────────────

        // 값을 통째로 바꾼다. 클래스·컬렉션은 내용을 복사해 넣으므로 게임 코드가 쥔 인스턴스는 그대로 유지된다.
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

            CheckValueType(key, slot, value);
            var normalized = Normalize(key, value);
            var old = slot.Value;
            if (Equals(old, normalized)) return;
            slot.Value = normalized;
            _active?.ValueChanges.Add(new StateChange { TransactionId = _active.Id, Key = key, Zone = slot.Zone, Old = old, New = normalized });
            _lastEntry[key] = _journal.Append(JournalKind.State, "State", Describe(key, old, normalized), key, old, normalized, _active?.Id ?? 0).Id;
            Changed?.Invoke(key);
        }

        public void Set<T>(StateKey<T> key, T value) => Set(key.Id, value);

        static void CheckValueType(string key, Slot slot, object value)
        {
            if (value == null || slot.ValueType == null) return;
            var type = value.GetType();
            bool ok = slot.ValueType.IsAssignableFrom(type) ||
                      (TypeResolver.IsNumeric(slot.ValueType) && (TypeResolver.IsNumeric(type) || type.IsEnum)) ||
                      (slot.ValueType.IsEnum && (TypeResolver.IsNumeric(type) || type == slot.ValueType));
            if (!ok) throw new ArgumentException($"[GameSystems] 상태 '{key}'에는 {DataModel.FriendlyName(slot.ValueType)} 값만 넣을 수 있습니다({type.Name}).");
        }

        // 클래스·컬렉션의 일부를 바꾼다. 예: state.Modify<Inventory>("player:inventory", inv => inv.items[0].level++);
        // 바뀐 곳을 찾아 경로로 기록하고, 행동 안이면 무르기 단위에 넣는다. 예외가 나면 바꾸기 전으로 되돌린다.
        public void Modify<T>(string key, Action<T> change) where T : class
        {
            if (change == null) throw new ArgumentNullException(nameof(change));
            var slot = GetSlot(key);
            if (slot.DataType == null)
                throw new InvalidOperationException($"[GameSystems] 상태 '{key}'는 단순 값입니다. Set으로 바꾸세요.");
            if (slot.Value != null && !(slot.Value is T))
                throw new InvalidCastException($"[GameSystems] 상태 '{key}'는 {DataModel.FriendlyName(slot.Value.GetType())}입니다({typeof(T).Name} 아님).");
            ModifyCore(key, slot, live =>
            {
                change((T)live);
                return live;
            });
        }

        public void Modify<T>(StateKey<T> key, Action<T> change) where T : class => Modify(key.Id, change);

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
                if (c.New != null && IsDataType(c.New.GetType())) c.New = DataModel.Clone(c.New, Identity);
            slot.Shadow = CloneShadow(slot);

            if (_active != null)
            {
                var record = _active.DataChanges.FirstOrDefault(r => r.Key == key);
                if (record == null)
                {
                    record = new DataRecord { Key = key, Zone = slot.Zone, Before = before };
                    _active.DataChanges.Add(record);
                }
                record.After = slot.Shadow;
                record.Changes.AddRange(changes);
            }
            LogChanges(key, slot, changes, JournalKind.State, null, _active?.Id ?? 0);
            Changed?.Invoke(key);
            DataChanged?.Invoke(key, changes);
        }

        object CloneShadow(Slot slot)
        {
            var shadow = DataModel.Clone(slot.Value, Identity, false, out var nodes);
            if (nodes > LargeValueNodes && !slot.SizeWarned)
            {
                slot.SizeWarned = true;
                _journal.Append(JournalKind.Warning, "State",
                    $"'{slot.Label}' 값이 큽니다(요소 {nodes}개). 수정할 때마다 전체를 복제·비교하므로, 자주 바뀌는 부분은 별도 키로 나누세요.");
            }
            return shadow;
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

        // 마지막 기록 이후 Modify 없이 바뀐 곳이 있으면 받아들이고 경고를 남긴다(무르기·원인 기록에서는 빠진다).
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
            slot.Shadow = CloneShadow(slot);
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
        }

        // 확정: 무르기 대기 중인 행동을 굳힌다. 확정된 행동은 더 이상 되돌릴 수 없다.
        public void Commit()
        {
            if (_active != null)
                throw new InvalidOperationException("[GameSystems] 트랜잭션 진행 중에는 확정할 수 없습니다.");
            if (_uncommitted.Count == 0) return;

            var committed = new List<Transaction>(_uncommitted);
            _uncommitted.Clear();
            _journal.Append(JournalKind.Commit, "State", $"확정: 행동 {committed.Count}개");
            foreach (var tx in committed) TransactionCommitted?.Invoke(tx);
        }

        // 확정되지 않은 마지막 행동을 되돌린다.
        public bool UndoLast()
        {
            if (_active != null)
                throw new InvalidOperationException("[GameSystems] 트랜잭션 진행 중에는 되돌릴 수 없습니다.");
            if (_uncommitted.Count == 0) return false;

            var tx = _uncommitted[_uncommitted.Count - 1];
            _uncommitted.RemoveAt(_uncommitted.Count - 1);

            var entry = _journal.Append(JournalKind.Undo, "State", $"무르기: {tx.Label}", transactionId: tx.Id);
            using (_journal.Cause(entry.Id)) Revert(tx, "무르기");
            TransactionUndone?.Invoke(tx);
            return true;
        }

        void Revert(Transaction tx, string reason)
        {
            for (int i = tx.ValueChanges.Count - 1; i >= 0; i--)
            {
                var change = tx.ValueChanges[i];
                var slot = _slots[change.Key];
                var current = slot.Value;
                slot.Value = change.Old;
                _lastEntry[change.Key] = _journal.Append(JournalKind.Undo, "State", $"{reason}: " + Describe(change.Key, current, change.Old), change.Key, current, change.Old, tx.Id).Id;
                Changed?.Invoke(change.Key);
            }

            // 데이터 값: 행동 전 스냅샷으로 제자리 복원(인스턴스·요소 ID 유지).
            for (int i = tx.DataChanges.Count - 1; i >= 0; i--)
            {
                var record = tx.DataChanges[i];
                var slot = _slots[record.Key];
                if (DetectUntrackedChanges) AdoptUntracked(record.Key, slot);
                var before = slot.Shadow;
                slot.Value = DataModel.ApplyInPlace(slot.Value, record.Before, Identity);
                slot.Shadow = CloneShadow(slot);
                var changes = DataModel.Diff(before, slot.Value, Identity);
                if (changes.Count == 0) continue;
                LogChanges(record.Key, slot, changes, JournalKind.Undo, $"{reason}: ", tx.Id);
                Changed?.Invoke(record.Key);
                DataChanged?.Invoke(record.Key, changes);
            }
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
                var warnings = new List<string>();
                result[pair.Key] = slot.DataType == null
                    ? DataModel.ToTree(slot.Value, slot.Value?.GetType() ?? typeof(object), Identity, warnings, slot.Label)
                    : DataModel.ToTree(slot.Value, slot.DataType, Identity, warnings, slot.Label);
                foreach (var warning in warnings.Distinct()) _journal.Append(JournalKind.Warning, "Save", warning, pair.Key);
            }
            return result;
        }

        // 아직 정의되지 않은 키는 보관했다가 Define 시점에 적용한다. 이름이 바뀐 키는 새 이름으로 옮긴다.
        public void Import(Dictionary<string, object> values, StateZone zone)
        {
            foreach (var pair in values)
            {
                var key = _aliases.TryGetValue(pair.Key, out var renamed) && !values.ContainsKey(renamed) ? renamed : pair.Key;
                if (!_slots.TryGetValue(key, out var slot))
                {
                    _preloaded[key] = pair.Value;
                    continue;
                }
                if (slot.Zone != zone) continue;
                if (slot.DataType != null)
                {
                    slot.Value = LoadData(key, slot, pair.Value);
                    slot.Shadow = CloneShadow(slot);
                }
                else if (TryNormalize(ReadScalar(slot, pair.Value), out var value))
                {
                    slot.Value = value;
                }
                else
                {
                    _journal.Append(JournalKind.Warning, "Save", $"저장된 '{slot.Label}' 값의 형식이 맞지 않아 무시합니다.", key);
                    continue;
                }
                Changed?.Invoke(key);
            }
        }

        static object ReadScalar(Slot slot, object tree)
        {
            if (tree == null || slot.ValueType == null || tree is double || tree is bool) return tree;
            if (slot.ValueType == typeof(string)) return tree;
            return DataModel.FromTree(tree, slot.ValueType, null, null);
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
                if (slot.DataType != null || !Equals(slot.Value, slot.Default)) Set(pair.Key, slot.Default);
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
                var node = group.Add(slot.Label, Format(slot.Value), slot.DataType == null ? pair.Key : null);
                node.Tag = ZoneName(slot.Zone);
                node.Note = TypeText(slot);
            }
            foreach (var group in groups.Values) root.Children.Add(group);
            root.Add("무르기 대기 행동", _uncommitted.Count.ToString(CultureInfo.InvariantCulture));
        }

        // ── 도우미 ─────────────────────────────────────────────

        Slot GetSlot(string key)
        {
            if (key != null && _slots.TryGetValue(key, out var slot)) return slot;
            throw new InvalidOperationException($"[GameSystems] 정의되지 않은 상태 키 '{key}'입니다. 데이터베이스 창의 상태 키에 등록하거나 Define으로 먼저 등록하세요.");
        }

        static string ZoneName(StateZone zone) => zone == StateZone.Meta ? "메타" : "세션";

        static string TypeText(Slot slot) =>
            slot.DataType != null ? DataModel.FriendlyName(slot.DataType) : slot.ValueType != null ? DataModel.FriendlyName(slot.ValueType) : "단순 값";

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

        // 숫자는 double로 맞춘다(비교·집계가 한 가지 타입으로 이뤄지도록). 읽을 때 Get<T>가 원래 타입으로 바꿔 준다.
        static object Normalize(string key, object value)
        {
            switch (value)
            {
                case null: return null;
                case bool _:
                case string _:
                case double _:
                case DateTime _:
                case DateTimeOffset _:
                case TimeSpan _:
                case Guid _:
                    return value;
                case Enum e: return System.Convert.ToDouble(e, CultureInfo.InvariantCulture);
                case char c: return c.ToString();
            }
            if (TypeResolver.IsNumeric(value.GetType())) return System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
            throw new ArgumentException($"[GameSystems] 상태 '{key}'에 저장할 수 없는 값 형식입니다: {value.GetType().Name}.");
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
