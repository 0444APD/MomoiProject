using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GBible
{
    // Session: 게임 내용(이번 판). 판이 끝나면 사라진다.
    // Meta: 도전과제·누적 수치·인벤토리 같은 프로필 데이터. 저장된다.
    // 두 구역은 저장 여부만 다르고 쓰는 법·Undo 규칙은 같다.
    public enum DataZone
    {
        Session,
        Meta,
    }

    // 멀티플레이: 이 플레이어 데이터를 어느 클라이언트에 보내는가(서버가 판단한다). 키 단위로 정한다 —
    // 남에게 숨길 것(손패 등)은 플레이어마다 키를 나눠 Owner로 둔다.
    public enum DataVisibility
    {
        // 모든 클라이언트(세션 구역의 기본).
        Public,
        // 주인(플레이어 번호) 클라이언트에게만.
        Owner,
        // 보내지 않는다(서버만 안다. 메타 구역의 기본 — 프로필은 각자 자기 쪽 것을 쓴다).
        Server,
    }

    // 타입이 붙은 플레이어 데이터 키. DB 창에서 이름을 지은 키는 코드 생성(GameIds)으로 이 형태로 제공된다.
    // 문자열 키 대신 쓰면 값의 타입이 컴파일 단계에서 맞춰지고 오타가 날 수 없다.
    public readonly struct PlayerDataKey<T>
    {
        public readonly string Id;

        public PlayerDataKey(string id)
        {
            Id = id;
        }

        public static implicit operator string(PlayerDataKey<T> key) => key.Id;
        public override string ToString() => Id;
    }

    // 단순 값 하나의 변화(Undo용).
    public sealed class PlayerDataChange
    {
        public long TransactionId;
        public string Key;
        public DataZone Zone;
        public object Old;
        public object New;
    }

    // 데이터 값 하나가 한 행동 안에서 바뀐 기록. Before는 행동 전 첫 모습, After는 마지막 모습.
    // 같은 행동 안에서 같은 값을 여러 번 고쳐도 기록은 하나로 합친다(Undo 메모리 절약).
    public sealed class DataRecord
    {
        public string Key;
        public DataZone Zone;
        public object Before;
        public object After;
        public List<DataChange> Changes = new List<DataChange>();
    }

    // 행동 하나로 일어난 변화 전체. Undo 한 단위.
    public sealed class Transaction
    {
        public long Id;
        public long JournalId;
        public string Label;
        // 시작한 게임 시각(행동 방식 Undo가 시간 규칙 행동을 되돌릴 때 시계를 여기로 돌린다).
        internal long Tick;
        internal double Time;
        // 게임 시각이 되어 실행된 시스템 행동(ITimeRuleAction).
        internal bool TimeRule;
        public readonly List<PlayerDataChange> ValueChanges = new List<PlayerDataChange>();
        public readonly List<DataRecord> DataChanges = new List<DataRecord>();
        // Mirror로 비교하는 플레이어 데이터의 변경(Sync 순서). Undo는 거꾸로 되돌린다.
        public readonly List<ChangeSet> Mirrored = new List<ChangeSet>();
        // 이 행동이 끝난 뒤 행동 밖에서 바뀐 곳("키:경로", 변경 목록을 만들 때만). Undo가 덮어쓰면 경고한다.
        internal HashSet<string> OutsideAfter;
        // 이 행동이 끝난 뒤 행동 밖에서 구조가 바뀐 리스트("키:경로"). 이 행동의 기록이 그 리스트의 요소에 닿으면 Undo를 거부한다(번호가 밀려 엉뚱한 요소가 바뀐다).
        internal HashSet<string> ReshapedAfter;
        // 이 수보다 앞의 Mirror 기록에는 이어 붙이지 않는다(저장점 뒤의 변경이 저장점 앞의 기록에 섞이지 않게).
        internal int SealedMirrored;
    }

    // 행동 안의 저장점(PlayerDataStore.Mark). RollbackTo로 그 뒤의 변경만 되돌린다.
    public sealed class Savepoint
    {
        internal Transaction Tx;
        internal int Mirrored;
        internal int Values;
        internal int Data;
        internal Dictionary<string, object> LegacyAt;
    }

    // 모든 플레이어 데이터가 사는 곳.
    //
    // 규칙은 하나: 값은 바꾸는 즉시 반영된다. 행동은 실행되면 끝이고, 이력(History)에 쌓여 언제든 뒤에서부터 Undo할 수 있다.
    // 이력에 남지 않는 변화: 행동 밖의 변화, Permanent 안의 변화(도전과제 달성·보상처럼 되돌리지 않을 일).
    // 실시간 게임은 이력을 끄고(RecordHistory = false) 세션이 일정 간격 스냅샷으로 되감는다.
    //
    // 담을 수 있는 값: bool, 숫자(double로 정규화), string, 날짜·시간·Guid, 그리고 임의의 클래스·구조체·
    // 리스트·배열·딕셔너리(필드 깊이 무관). 게임 코드는 평범한 C#으로 값을 고친다.
    //   등록  Define(키, 구역, 기본값)          읽기  Get<T>(키)
    //   교체  Set(키, 값)                       부분 수정  Modify<T>(키, 값 => ...) 또는 꺼낸 객체를 그냥 고치기
    // 데이터 값(클래스)은 Mirror가 사본을 들고 있다가 Sync할 때(행동 끝·Modify 끝·프레임 끝) 바뀐 곳을 경로로 기록한다
    // (예: units[3].hp 30 → 20). 행동 안이면 그 변경(ChangeSet)이 Undo 단위에 들어가고, Synced로 알려 네트워크가 보낼 수 있다.
    // 비교 코드가 없는 형식([Serializable]이 없는 클래스 등)은 예전 방식(리플렉션 비교)으로 다룬다.
    public sealed class PlayerDataStore : IInspectable
    {
        sealed class Slot
        {
            public DataZone Zone;
            public object Value;
            public object Default;
            public string Label;
            // 단순 값: 선언 타입(없으면 제한 없음). 데이터 값: DataType과 비교용 사본.
            public Type ValueType;
            public Type DataType;
            // Mirror 사본(비교 코드가 있는 형식). 없으면 Shadow(예전 방식: 마지막 기록 시점의 복제본).
            public MirrorNode Mirror;
            public object Shadow;
            public bool SizeWarned;
        }

        const int MaxLoggedChanges = 12;
        // 데이터 값 하나의 요소 수가 이보다 크면 한 번 경고한다(수정마다 전체를 복제·비교하므로).
        public const int LargeValueNodes = 20000;

        readonly Dictionary<string, Slot> _slots = new Dictionary<string, Slot>(StringComparer.Ordinal);
        readonly Dictionary<string, object> _preloaded = new Dictionary<string, object>(StringComparer.Ordinal);
        // 미리 불러 둔 값의 구역(그 구역을 저장할 때 함께 쓰고, 그 구역을 비우거나 다시 불러오면 버린다).
        readonly Dictionary<string, DataZone> _preloadedZone = new Dictionary<string, DataZone>(StringComparer.Ordinal);
        readonly Dictionary<string, string> _aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly List<Transaction> _history = new List<Transaction>();
        // 키별로 마지막으로 적용된 변화의 기록 id. 그 변화로 일어난 일(조건 충족 등)의 원인으로 쓴다.
        readonly Dictionary<string, long> _lastEntry = new Dictionary<string, long>(StringComparer.Ordinal);
        readonly Journal _journal;
        Transaction _active;
        long _nextTransactionId = 1;

        // Sync가 쓰는 작업용 기록.
        readonly ChangeSet _work = new ChangeSet();
        Func<object, long> _elementId;
        // 행동 중에 Sync한 변경(Synced로 알릴 차례대로). Permanent로 잠시 행동 밖에 나가도 이어서 담는다.
        readonly List<ChangeSet> _held = new List<ChangeSet>();
        bool _holding;

        // 값이 바뀌었을 때(Undo로 돌아갈 때 포함).
        public event Action<string> Changed;
        // Mirror로 비교하는 플레이어 데이터가 Sync로 바뀌었을 때 그 변경(앞으로 기록을 다른 쪽에 보내면 같아진다).
        // 행동 안의 변경은 행동이 끝날 때(또는 중단될 때) 순서대로 한꺼번에 알린다: 행동 중간 모습은 밖으로 나가지 않는다.
        public event Action<ChangeSet> Synced;
        // 데이터 값이 바뀌었을 때 바뀐 경로 목록과 함께.
        public event Action<string, IReadOnlyList<DataChange>> DataChanged;
        public event Action<Transaction> TransactionUndone;
        // 이력 한도를 넘어 가장 오래된 행동이 Undo 대상에서 빠졌을 때.
        public event Action<Transaction> TransactionForgotten;
        // 새 키가 등록됐을 때(실행 중에 생기는 라이브러리 키 포함). 멀티플레이 서버가 클라이언트에 알린다.
        public event Action<string> KeyDefined;

        public PlayerDataStore(Journal journal)
        {
            _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        }

        public string InspectName => "플레이어 데이터";
        // Undo할 수 있는 행동 수와 목록(오래된 것부터).
        public int HistoryCount => _history.Count;
        public IReadOnlyList<Transaction> History => _history;
        public bool InTransaction => _active != null;
        // 진행 중인 행동의 번호(없으면 -1).
        public long ActiveTransactionId => _active?.Id ?? -1;
        // false면 행동을 이력에 남기지 않는다(실시간 게임: 스냅샷으로 되감음, 또는 Undo가 없는 게임).
        public bool RecordHistory = true;
        // 이력에 남길 최대 행동 수. 0이면 무제한(행동마다 바뀐 값의 이전 모습을 보관하므로 메모리가 늘어난다).
        public int HistoryLimit;
        public IEnumerable<string> Keys => _slots.Keys;
        public int KeyCount => _slots.Count;
        public DataIdentity Identity { get; } = new DataIdentity();
        // 예전 방식(리플렉션 비교) 데이터에서 Modify를 거치지 않은 수정을 찾아 경고한다. 값 전체를 비교하므로 출시 빌드에서는 끈다.
        public bool DetectUntrackedChanges = true;
        // false면 모든 데이터를 예전 방식으로 비교한다(비교 코드 문제를 가를 때).
        public bool UseMirror = true;
        // 바뀐 곳마다 사람이 읽는 기록(경로·옛 값·새 값)을 만든다. 끄면 기록·DataChanged의 목록이 비지만 Sync가 가벼워진다.
        public bool DescribeChanges = true;

        // ── 정의 ───────────────────────────────────────────────

        // 어떤 타입이든 등록한다. 기본값의 타입이 이 키의 타입이 된다(null이면 단순 값, 타입 제한 없음).
        public void Define(string key, DataZone zone, object defaultValue = null, string label = null) =>
            DefineOfType(key, zone, defaultValue?.GetType(), defaultValue, label);

        // 위와 같고, 저장소가 관리하는 실제 값을 돌려준다. 예: var inv = state.Define("player:inventory", DataZone.Meta, new Inventory());
        public T Define<T>(string key, DataZone zone, T defaultValue, string label = null)
        {
            DefineOfType(key, zone, defaultValue?.GetType() ?? typeof(T), defaultValue, label);
            return Get<T>(key);
        }

        // 타입을 지정해 등록한다(DB 창의 플레이어 데이터가 이 경로로 등록된다).
        // 클래스·컬렉션인데 기본값이 없으면 기본 생성자로 만든다.
        public void DefineOfType(string key, DataZone zone, Type type, object defaultValue, string label = null)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("[MomoiProject] 플레이어 데이터 ID가 비어 있습니다.", nameof(key));
            if (type != null && IsDataType(type))
            {
                DefineData(key, zone, defaultValue ?? DataModel.Create(type), label);
                return;
            }
            DefineValue(key, zone, type, defaultValue, label);
        }

        // 타입이 붙은 키로 등록하고, 저장소가 관리하는 실제 값을 돌려준다.
        public T Define<T>(PlayerDataKey<T> key, DataZone zone, T defaultValue = default, string label = null)
        {
            DefineOfType(key.Id, zone, typeof(T), defaultValue, label);
            return Get<T>(key.Id);
        }

        void DefineValue(string key, DataZone zone, Type type, object defaultValue, string label)
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
                else _journal.Append(JournalKind.Warning, "Data", $"저장된 '{slot.Label}' 값의 형식이 맞지 않아 기본값을 씁니다.", key);
            }
            _slots.Add(key, slot);
            _keys = null;
            KeyDefined?.Invoke(key);
        }

        void DefineData(string key, DataZone zone, object value, string label)
        {
            var type = value.GetType();
            var problems = DataModel.Validate(type);
            if (problems.Count > 0)
                throw new ArgumentException($"[MomoiProject] 플레이어 데이터 '{key}'에 담을 수 없는 필드가 있습니다.\n  " + string.Join("\n  ", problems));

            if (_slots.TryGetValue(key, out var existing))
            {
                CheckRedefine(key, existing, zone, type);
                existing.Default = DataModel.Clone(value, null);
                if (label != null) existing.Label = label;
                return;
            }

            var slot = new Slot { Zone = zone, Label = label ?? key, DataType = type, Default = DataModel.Clone(value, null), Value = value };
            if (TakePreloaded(key, out var loaded)) slot.Value = LoadData(key, slot, loaded);
            ChooseMirror(key, slot);
            RefreshShadow(slot);
            _slots.Add(key, slot);
            _keys = null;
            CheckRequired(key, slot);
            KeyDefined?.Invoke(key);
        }

        // 비교 코드가 있는 형식이면 Mirror 사본을 만든다. 없으면 예전 방식(리플렉션 비교)이고, 이유를 한 번 남긴다.
        void ChooseMirror(string key, Slot slot)
        {
            slot.Mirror = null;
            if (!UseMirror || slot.Value == null) return;
            if (Mirror.Supports(slot.Value.GetType(), out var reason))
            {
                slot.Mirror = Mirror.Make(slot.Value);
                return;
            }
            _journal.Append(JournalKind.Warning, "Data", $"'{slot.Label}'은(는) 예전 방식(리플렉션)으로 비교합니다: {reason}", key);
        }

        static void CheckRedefine(string key, Slot existing, DataZone zone, Type dataType)
        {
            if (existing.Zone != zone)
                throw new InvalidOperationException($"[MomoiProject] 플레이어 데이터 '{key}'가 이미 {ZoneName(existing.Zone)} 구역으로 정의되어 있습니다.");
            if (existing.DataType != dataType)
                throw new InvalidOperationException($"[MomoiProject] 플레이어 데이터 '{key}'는 이미 {TypeText(existing)}(으)로 정의되어 있습니다.");
        }

        // 이름이 바뀐 키. 예전 이름으로 저장된 값을 새 이름으로 불러온다(식별자 이름 변경 시 자동 등록).
        public void AddAlias(string oldKey, string newKey)
        {
            if (string.IsNullOrEmpty(oldKey) || string.IsNullOrEmpty(newKey) || oldKey == newKey) return;
            _aliases[oldKey] = newKey;
            if (_preloaded.TryGetValue(oldKey, out var value))
            {
                _preloaded.Remove(oldKey);
                var hasZone = _preloadedZone.TryGetValue(oldKey, out var zone);
                _preloadedZone.Remove(oldKey);
                if (!_preloaded.ContainsKey(newKey))
                {
                    _preloaded[newKey] = value;
                    if (hasZone) _preloadedZone[newKey] = zone;
                }
            }
        }

        bool TakePreloaded(string key, out object value)
        {
            if (!_preloaded.TryGetValue(key, out value)) return false;
            _preloaded.Remove(key);
            _preloadedZone.Remove(key);
            return true;
        }

        // 이 형식을 Mirror(생성 코드 비교)로 다루는지. 아니면 예전 방식(리플렉션 비교).
        public static bool IsMirrored(Type type) => type != null && Mirror.Supports(type, out _);

        // 예전 방식 데이터만 비교용 복제본을 둔다(Mirror 데이터는 Mirror 사본이 대신한다).
        void RefreshShadow(Slot slot) => slot.Shadow = slot.Mirror != null ? null : CloneShadow(slot);

        static bool IsDataType(Type type)
        {
            var kind = DataModel.Of(type).Kind;
            return kind == DataKind.Object || kind == DataKind.List || kind == DataKind.Array || kind == DataKind.Dictionary || kind == DataKind.Collection;
        }

        public bool IsDefined(string key) => key != null && _slots.ContainsKey(key);

        // 이 안에서는 Changed·DataChanged·Synced를 알리지 않는다(미리보기: 바뀌었다가 그대로 돌아오는 변경이라 화면·전송이 알 필요가 없다).
        // 안에서 새로 붙인 받는 쪽은 그대로 남는다. 캐시를 가진 쪽은 되돌림(TransactionAborted)으로 비운다.
        public IDisposable Quiet()
        {
            var changed = Changed;
            var dataChanged = DataChanged;
            var synced = Synced;
            Changed = null;
            DataChanged = null;
            Synced = null;
            // 받는 쪽은 이 안의 변경을 받지 않는다: 빠졌다가 돌아온 객체는 예전 객체 번호를 그대로 쓴다(Mirror.KeepIds).
            foreach (var slot in _slots.Values) Mirror.KeepIds(slot.Mirror, true);
            return new QuietScope(() =>
            {
                foreach (var slot in _slots.Values) Mirror.KeepIds(slot.Mirror, false);
                Changed = changed + Changed;
                DataChanged = dataChanged + DataChanged;
                Synced = synced + Synced;
            });
        }

        sealed class QuietScope : IDisposable
        {
            Action _restore;

            public QuietScope(Action restore) => _restore = restore;

            public void Dispose()
            {
                _restore?.Invoke();
                _restore = null;
            }
        }

        // 불러온 저장에 값이 있는데 아직 등록되지 않은 키(등록하는 순간 그 값이 적용된다).
        // 필요할 때 등록하는 라이브러리 키($modifiers·$statuses·$schedule)가 판을 불러온 직후에도 보이게 쓴다.
        public bool IsPending(string key) => key != null && _preloaded.ContainsKey(key);

        public bool IsData(string key) => key != null && _slots.TryGetValue(key, out var slot) && slot.DataType != null;

        // 등록된 타입(데이터 값은 클래스·컬렉션 타입, 단순 값은 선언 타입 또는 null).
        public Type TypeOf(string key) => key != null && _slots.TryGetValue(key, out var slot) ? slot.DataType ?? slot.ValueType : null;

        public DataZone ZoneOf(string key) => GetSlot(key).Zone;

        public string LabelOf(string key) => _slots.TryGetValue(key, out var slot) ? slot.Label : key;

        // ── 읽기 ───────────────────────────────────────────────
        // 데이터 값은 저장소가 관리하는 실제 인스턴스를 돌려준다(읽기는 자유, 수정은 Modify).

        public object Get(string key)
        {
            var slot = GetSlot(key);
            Note(key, slot);
            return slot.Value;
        }

        public T Get<T>(string key)
        {
            var slot = GetSlot(key);
            Note(key, slot);
            return Convert<T>(key, slot.Value);
        }

        public T Get<T>(PlayerDataKey<T> key) => Get<T>(key.Id);

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
            throw new InvalidCastException($"[MomoiProject] 플레이어 데이터 '{key}'의 값({DataModel.FriendlyName(value.GetType())})을 {DataModel.FriendlyName(typeof(T))}(으)로 읽을 수 없습니다.");
        }

        // ── 쓰기 ───────────────────────────────────────────────

        // 값을 통째로 바꾼다. 클래스·컬렉션은 내용을 복사해 넣으므로 게임 코드가 쥔 인스턴스는 그대로 유지된다.
        public void Set(string key, object value)
        {
            var slot = GetSlot(key);
            if (slot.DataType != null)
            {
                if (value != null && !slot.DataType.IsInstanceOfType(value))
                    throw new ArgumentException($"[MomoiProject] 플레이어 데이터 '{key}'에는 {DataModel.FriendlyName(slot.DataType)}만 넣을 수 있습니다({value.GetType().Name}).");
                var copy = DataModel.Clone(value, Identity);
                ModifyCore(key, slot, live => DataModel.ApplyInPlace(live, copy, Identity));
                return;
            }

            CheckValueType(key, slot, value);
            var normalized = Normalize(key, value);
            var old = slot.Value;
            if (Equals(old, normalized)) return;
            slot.Value = normalized;
            _active?.ValueChanges.Add(new PlayerDataChange { TransactionId = _active.Id, Key = key, Zone = slot.Zone, Old = old, New = normalized });
            _lastEntry[key] = _journal.Append(JournalKind.Data, "Data", Describe(key, old, normalized), key, old, normalized, _active?.Id ?? 0).Id;
            Changed?.Invoke(key);
        }

        public void Set<T>(PlayerDataKey<T> key, T value) => Set(key.Id, value);

        static void CheckValueType(string key, Slot slot, object value)
        {
            if (value == null || slot.ValueType == null) return;
            var type = value.GetType();
            bool ok = slot.ValueType.IsAssignableFrom(type) ||
                      (TypeResolver.IsNumeric(slot.ValueType) && (TypeResolver.IsNumeric(type) || type.IsEnum)) ||
                      (slot.ValueType.IsEnum && (TypeResolver.IsNumeric(type) || type == slot.ValueType));
            if (!ok) throw new ArgumentException($"[MomoiProject] 플레이어 데이터 '{key}'에는 {DataModel.FriendlyName(slot.ValueType)} 값만 넣을 수 있습니다({type.Name}).");
        }

        // 클래스·컬렉션의 일부를 바꾼다. 예: state.Modify<Inventory>("player:inventory", inv => inv.items[0].level++);
        // 바뀐 곳을 찾아 경로로 기록하고, 행동 안이면 Undo 단위에 넣는다. 예외가 나면 바꾸기 전으로 되돌린다.
        public void Modify<T>(string key, Action<T> change) where T : class
        {
            if (change == null) throw new ArgumentNullException(nameof(change));
            var slot = GetSlot(key);
            if (slot.DataType == null)
                throw new InvalidOperationException($"[MomoiProject] 플레이어 데이터 '{key}'는 단순 값입니다. Set으로 바꾸세요.");
            if (slot.Value != null && !(slot.Value is T))
                throw new InvalidCastException($"[MomoiProject] 플레이어 데이터 '{key}'는 {DataModel.FriendlyName(slot.Value.GetType())}입니다({typeof(T).Name} 아님).");
            ModifyCore(key, slot, live =>
            {
                change((T)live);
                return live;
            });
        }

        public void Modify<T>(PlayerDataKey<T> key, Action<T> change) where T : class => Modify(key.Id, change);

        void ModifyCore(string key, Slot slot, Func<object, object> change)
        {
            if (slot.Mirror != null)
            {
                // 행동 밖이면 그 전에 바뀐 것(들고 있던 객체를 직접 고친 것)을 따로 기록해 이번 변경과 섞지 않는다.
                // 행동 안이면 어차피 같은 행동의 변경이라 한 번에 비교한다.
                if (_active == null) SyncSlot(key, slot);
                try
                {
                    slot.Value = change(slot.Value);
                }
                catch
                {
                    RevertToMirror(slot);
                    throw;
                }
                SyncSlot(key, slot);
                return;
            }
            // 추적 범위에서 이미 꺼낸 값은 직접 고친 부분이 있어도 경고하지 않는다(이번 변경과 함께 기록된다).
            if (DetectUntrackedChanges && (_touched == null || !_touched.Contains(key))) AdoptUntracked(key, slot);
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
            Commit(key, slot, before);
        }

        // 마지막 기록(Shadow) 이후 바뀐 곳을 찾아 기록한다: 행동 안이면 Undo 단위에, 그리고 Journal·변경 알림.
        void Commit(string key, Slot slot, object before)
        {
            var changes = DataModel.Diff(before, slot.Value, Identity);
            if (changes.Count == 0) return;
            // 기록에는 그 시점의 모습을 남긴다(나중에 실제 인스턴스가 바뀌어도 기록은 그대로).
            foreach (var c in changes)
                if (c.New != null && IsDataType(c.New.GetType())) c.New = DataModel.Clone(c.New, Identity);
            RefreshShadow(slot);

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
            LogChanges(key, slot, changes, JournalKind.Data, null, _active?.Id ?? 0);
            Changed?.Invoke(key);
            DataChanged?.Invoke(key, changes);
            CheckRequired(key, slot);
        }

        object CloneShadow(Slot slot)
        {
            var shadow = DataModel.Clone(slot.Value, Identity, false, out var nodes);
            if (nodes > LargeValueNodes && !slot.SizeWarned)
            {
                slot.SizeWarned = true;
                _journal.Append(JournalKind.Warning, "Data",
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
                    last = _journal.Append(kind, "Data", $"{prefix}{slot.Label}  … 외 {changes.Count - i}곳", key, transactionId: transactionId).Id;
                    break;
                }
                var c = changes[i];
                var entry = _journal.Append(kind, "Data", prefix + DescribeNatural(slot, c), key, Leaf(c.Old), Leaf(c.New), transactionId);
                entry.Code = prefix + DescribeData(slot, c);
                // 클래스·컬렉션이 통째로 들어오거나 빠진 경우 그 모습(사본)을 남겨 모니터에서 열어 볼 수 있게 한다.
                if (IsDataObject(c.New) || IsDataObject(c.Old)) entry.Payload = IsDataObject(c.New) ? c.New : c.Old;
                last = entry.Id;
            }
            if (last != 0) _lastEntry[key] = last;
        }

        static bool IsDataObject(object value) => value != null && IsDataType(value.GetType());

        static object Leaf(object value) => value == null || !IsDataType(value.GetType()) ? value : DataModel.Summary(value);

        // 자연어: 인벤토리 › 아이템 #12 › 레벨  3 → 4
        static string DescribeNatural(Slot slot, DataChange change)
        {
            var where = DataModel.NaturalPath(slot.DataType, change.Path);
            var head = string.IsNullOrEmpty(where) ? slot.Label : $"{slot.Label} › {where}";
            switch (change.Kind)
            {
                case DataChangeKind.Insert: return $"{head} 추가: {DataModel.Summary(change.New)}";
                case DataChangeKind.Remove: return $"{head} 제거: {DataModel.Summary(change.Old)}";
                case DataChangeKind.Reorder: return $"{head} 순서 변경";
                default: return $"{head}  {DataModel.Summary(change.Old)} → {DataModel.Summary(change.New)}";
            }
        }

        static string DescribeData(Slot slot, DataChange change)
        {
            var text = change.ToString();
            return string.IsNullOrEmpty(change.Path) ? $"{slot.Label}{text}" : $"{slot.Label}.{text}";
        }

        // ── Sync ───────────────────────────────────────────────
        // Mirror 데이터를 사본과 비교해 바뀐 곳을 기록한다(행동 안이면 그 행동의 Undo 단위, 밖이면 행동 밖 변경).
        // 시스템이 행동 시작·끝, Modify, 프레임 끝에 부른다. 게임 코드가 직접 부를 일은 없다.
        // 예전 방식 데이터는 DetectUntrackedChanges가 켜져 있으면 Modify 없이 바뀐 곳을 찾아 경고한다.
        // 바뀐 플레이어 데이터 수를 돌려준다.
        public int Sync()
        {
            var count = 0;
            foreach (var key in SlotKeys())
            {
                var slot = _slots[key];
                if (slot.Mirror != null)
                {
                    if (SyncSlot(key, slot) != null) count++;
                }
                else if (slot.DataType != null && DetectUntrackedChanges && AdoptUntracked(key, slot)) count++;
            }
            return count;
        }

        // 키 목록(정의가 바뀔 때만 새로 만든다). 알림을 받은 쪽이 새 키를 정의해도 훑는 중인 목록은 그대로다.
        string[] _keys;

        string[] SlotKeys() => _keys ??= _slots.Keys.ToArray();

        // 예전 이름. Sync와 같다.
        public int CheckUntrackedChanges() => Sync();

        // Mirror 데이터의 사본(지금 값과 맞춘 뒤). 예전 방식 데이터면 null. Game.Find·All이 리플렉션 없이 찾는 데 쓴다.
        internal MirrorNode SyncedMirror(string key)
        {
            if (!_slots.TryGetValue(key, out var slot) || slot.Mirror == null) return null;
            SyncSlot(key, slot);
            return slot.Mirror;
        }

        // 받는 쪽(다른 플레이어)이 처음 받을 모습: 지금 값 전체 + 객체 번호. 받는 쪽은 MirrorReceiver.Load로 읽고,
        // 이후 Synced로 오는 변경을 MirrorReceiver.Apply로 적용한다. 예전 방식 데이터(Mirror가 아님)면 null.
        public byte[] Snapshot(string key)
        {
            var mirror = SyncedMirror(key);
            return mirror == null ? null : Mirror.Snapshot(mirror);
        }

        // Mirror 데이터 하나를 Sync한다. 바뀐 것이 없으면 null.
        ChangeSet SyncSlot(string key, Slot slot, JournalKind kind = JournalKind.Data, string prefix = null, long transactionId = -1)
        {
            if (slot.Value == null || slot.Value.GetType() != slot.Mirror.Type.Type)
            {
                // 값이 통째로 다른 형식으로 바뀐 경우(드묾): 사본을 새로 만들고, 받는 쪽에는 처음 모습을 통째로 보낸다.
                ChooseMirror(key, slot);
                RefreshShadow(slot);
                if (slot.Mirror == null) return null;
                var reset = new ChangeSet { Key = key, Snapshot = Mirror.Snapshot(slot.Mirror) };
                if (_holding) _held.Add(reset);
                else Synced?.Invoke(reset);
            }
            _work.Clear();
            _work.Key = key;
            _work.Describe = DescribeChanges;
            _work.ElementId = _elementId ??= Identity.IdOf;
            Mirror.Sync(slot.Value, slot.Mirror, _work);
            if (_work.IsEmpty) return null;
            var changes = _work.Detach();
            if (kind == JournalKind.Data && _active != null)
            {
                // 한 행동 안에서 같은 데이터를 잇달아 Sync하면 Undo 기록 하나로 합친다.
                var history = _active.Mirrored;
                var last = history.Count > 0 ? history[history.Count - 1] : null;
                if (last == null || last.Key != key || history.Count <= _active.SealedMirrored)
                {
                    last = new ChangeSet { Key = key };
                    history.Add(last);
                }
                last.Append(changes);
            }
            LogChanges(key, slot, changes.Changes, kind, prefix, transactionId >= 0 ? transactionId : _active?.Id ?? 0);
            Changed?.Invoke(key);
            DataChanged?.Invoke(key, changes.Changes);
            if (_holding) _held.Add(changes);
            else
            {
                if (kind == JournalKind.Data) NoteOutside(key, changes);
                Synced?.Invoke(changes);
            }
            CheckRequired(key, slot);
            return changes;
        }

        // 행동 밖 변경을 마지막 행동에 적어 둔다(그 행동을 되돌릴 때 덮어쓰는지 보려고).
        void NoteOutside(string key, ChangeSet changes)
        {
            if (!RecordHistory || _history.Count == 0 || changes.Changes.Count == 0) return;
            var last = _history[_history.Count - 1];
            last.OutsideAfter ??= new HashSet<string>(StringComparer.Ordinal);
            foreach (var change in changes.Changes) last.OutsideAfter.Add(key + ":" + change.Path);
            foreach (var path in changes.Reshaped) (last.ReshapedAfter ??= new HashSet<string>(StringComparer.Ordinal)).Add(key + ":" + path);
        }

        // 되돌리는 행동이 바꾼 곳 중 그 뒤 행동 밖에서 다시 바뀐 곳("키:경로").
        static List<string> Overwritten(Transaction tx)
        {
            var found = new List<string>();
            if (tx.OutsideAfter == null) return found;
            foreach (var changes in tx.Mirrored)
            foreach (var change in changes.Changes)
            {
                var mine = changes.Key + ":" + change.Path;
                foreach (var outside in tx.OutsideAfter)
                    if (Overlaps(mine, outside) && !found.Contains(outside)) found.Add(outside);
            }
            return found;
        }

        // 되돌리는 행동의 기록이 그 뒤 행동 밖에서 구조가 바뀐 리스트(또는 그 요소)에 닿으면 그 리스트("키:경로"), 아니면 null.
        static string Reshaped(Transaction tx)
        {
            if (tx.ReshapedAfter == null) return null;
            foreach (var changes in tx.Mirrored)
            foreach (var change in changes.Changes)
            {
                var mine = changes.Key + ":" + change.Path;
                foreach (var list in tx.ReshapedAfter)
                    if (mine.Length > list.Length ? mine.StartsWith(list, StringComparison.Ordinal) && mine[list.Length] == '[' : mine == list)
                        return list;
            }
            return null;
        }

        // 같은 곳이거나 한쪽이 다른 쪽을 품는다(예: units[#3] 와 units[#3].hp).
        static bool Overlaps(string a, string b)
        {
            if (a.Length > b.Length) (a, b) = (b, a);
            if (!b.StartsWith(a, StringComparison.Ordinal)) return false;
            if (b.Length == a.Length || a.EndsWith(":", StringComparison.Ordinal)) return true;
            var next = b[a.Length];
            return next == '.' || next == '[';
        }

        // 마지막 Sync 이후의 변경을 버리고 사본 모습으로 되돌린다(기록 없이). 예외로 중단된 변경에 쓴다.
        void RevertToMirror(Slot slot)
        {
            _work.Clear();
            Mirror.Sync(slot.Value, slot.Mirror, _work);
            if (_work.IsEmpty) return;
            var changes = _work.Detach();
            Mirror.Undo(slot.Value, changes);
            Mirror.Sync(slot.Value, slot.Mirror, null);
        }

        bool AdoptUntracked(string key, Slot slot)
        {
            var changes = DataModel.Diff(slot.Shadow, slot.Value, Identity);
            if (changes.Count == 0) return false;
            var where = changes[0].Path + (changes.Count > 1 ? $" 외 {changes.Count - 1}곳" : "");
            var message = $"'{slot.Label}'이(가) Modify를 거치지 않고 바뀌었습니다({where}). Undo와 원인 기록에서 빠집니다. 행동 안에서 고치거나 session.Data.Modify(\"{key}\", …)로 바꾸세요(클래스에 [Serializable]을 붙이면 Modify 없이 기록됩니다).";
            _lastEntry[key] = _journal.Append(JournalKind.Warning, "Data", message, key).Id;
            RefreshShadow(slot);
            Changed?.Invoke(key);
            DataChanged?.Invoke(key, changes);
            CheckRequired(key, slot);
            return true;
        }

        // ── 직접 수정 추적·읽기 기록 ─────────────────────────────
        // Track 안에서 Get으로 꺼낸 데이터 값(클래스·컬렉션)은 Modify 없이 필드를 그냥 고쳐도 된다.
        // 끝날 때 꺼냈던 값만 마지막 기록과 비교해 Modify와 똑같이 기록한다(행동 안이면 Undo 단위, Journal, 변경 알림).
        // 예외가 나면 꺼냈던 값을 마지막 기록으로 되돌린다. 행동(GameAction)·효과 실행은 시스템이 이 안에서 부른다.
        HashSet<string> _touched;
        HashSet<string> _reads;

        public bool Tracking => _touched != null;

        public void Track(Action change)
        {
            if (change == null) throw new ArgumentNullException(nameof(change));
            if (_touched != null)
            {
                change();
                return;
            }
            var touched = _touched = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                change();
            }
            catch
            {
                _touched = null;
                foreach (var key in touched)
                {
                    var slot = _slots[key];
                    slot.Value = DataModel.ApplyInPlace(slot.Value, slot.Shadow, Identity);
                }
                foreach (var key in SlotKeys())
                    if (_slots[key].Mirror != null) RevertToMirror(_slots[key]);
                throw;
            }
            _touched = null;
            foreach (var key in touched.OrderBy(k => k, StringComparer.Ordinal))
            {
                var slot = _slots[key];
                Commit(key, slot, slot.Shadow);
            }
            // Mirror 데이터는 꺼냈는지와 상관없이 모두 비교한다(들고 있던 객체를 고쳐도 잡힌다).
            foreach (var key in SlotKeys())
            {
                var slot = _slots[key];
                if (slot.Mirror != null) SyncSlot(key, slot);
            }
        }

        // 이 안에서 읽은 키를 into에 모은다(조건이 무엇을 보는지 자동으로 알아내는 데 쓴다).
        public IDisposable RecordReads(HashSet<string> into) => new ReadScope(this, into);

        sealed class ReadScope : IDisposable
        {
            readonly PlayerDataStore _store;
            readonly HashSet<string> _previous;

            public ReadScope(PlayerDataStore store, HashSet<string> into)
            {
                _store = store;
                _previous = store._reads;
                store._reads = into;
            }

            public void Dispose() => _store._reads = _previous;
        }

        void Note(string key, Slot slot)
        {
            _reads?.Add(key);
            if (_touched == null || slot.DataType == null || slot.Mirror != null || !_touched.Add(key)) return;
            // 추적을 시작하기 전에 직접 바뀐 곳은 이번 변경과 섞지 않고 따로 경고한다(들고 있던 객체를 행동 밖에서 고친 경우).
            if (DetectUntrackedChanges) AdoptUntracked(key, slot);
        }

        // ── 필수 값 ─────────────────────────────────────────────
        // 필드 규칙의 "필수": 어떤 클래스의 어떤 필드가 비어 있으면(null·빈 글자·빈 목록) 기록에 경고를 남기고,
        // 채워질 때까지 MissingRequired에 남겨 둔다(모니터가 목록으로 보여 준다).

        sealed class Requirement
        {
            public Type Owner;
            public string Field;
        }

        readonly List<Requirement> _required = new List<Requirement>();
        readonly Dictionary<string, HashSet<string>> _missing = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        public event Action MissingChanged;

        public void Require(Type owner, string field)
        {
            if (owner == null || FieldAccess.Find(owner, field) == null) return;
            _required.Add(new Requirement { Owner = owner, Field = field });
            _checks.Clear();
            _mayContain.Clear();
            foreach (var pair in _slots) if (pair.Value.DataType != null) CheckRequired(pair.Key, pair.Value);
        }

        // (플레이어 데이터, 사람이 읽는 위치) 목록.
        public IEnumerable<(string key, string where)> MissingRequired =>
            _missing.SelectMany(pair => pair.Value.Select(path => (pair.Key, path)));

        void CheckRequired(string key, Slot slot)
        {
            if (_required.Count == 0 || slot.DataType == null) return;
            // 필수 규칙의 클래스가 들어 있을 수 없는 데이터는 훑지 않는다(형식마다 한 번 판단).
            if (!MayContain(slot.DataType)) return;
            var found = new HashSet<string>(StringComparer.Ordinal);
            _trail.Clear();
            FindMissing(slot.Value, found, 0);
            _missing.TryGetValue(key, out var before);
            before = before ?? new HashSet<string>(StringComparer.Ordinal);
            if (found.SetEquals(before)) return;
            foreach (var path in found.Where(p => !before.Contains(p)))
                _journal.Append(JournalKind.Warning, "Data", $"필수 값이 비어 있습니다: {slot.Label} › {DataModel.NaturalPath(slot.DataType, path)}", key);
            foreach (var path in before.Where(p => !found.Contains(p)))
                _journal.Append(JournalKind.System, "Data", $"필수 값이 채워졌습니다: {slot.Label} › {DataModel.NaturalPath(slot.DataType, path)}", key);
            if (found.Count == 0) _missing.Remove(key);
            else _missing[key] = found;
            MissingChanged?.Invoke();
        }

        // 형식별: 확인할 필수 필드와, 안쪽으로 내려갈 멤버(객체·컬렉션만 — 숫자·글자는 읽지 않는다).
        sealed class TypeCheck
        {
            public (string name, MemberAccess access)[] Required;
            public (string name, MemberAccess access)[] Children;
        }

        readonly Dictionary<Type, TypeCheck> _checks = new Dictionary<Type, TypeCheck>();
        readonly Dictionary<Type, bool> _mayContain = new Dictionary<Type, bool>();

        // 지금 훑는 위치(필드 이름 또는 요소 표시). 빈 값을 찾았을 때만 글자로 만든다.
        readonly List<(string name, string element)> _trail = new List<(string, string)>();

        TypeCheck CheckOf(Type type)
        {
            if (_checks.TryGetValue(type, out var check)) return check;
            var required = _required.Where(r => r.Owner.IsAssignableFrom(type))
                                    .Select(r => (r.Field, FieldAccess.Find(type, r.Field))).Where(p => p.Item2 != null).ToArray();
            var children = new List<(string, MemberAccess)>();
            var mirror = Mirror.Find(type);
            if (mirror != null)
            {
                for (var i = 0; i < mirror.FieldCount; i++)
                {
                    var kind = mirror.Kinds[i];
                    if (kind != MirrorKind.Object && kind != MirrorKind.List && kind != MirrorKind.Array && kind != MirrorKind.Dictionary) continue;
                    if (!MayContain(mirror.FieldTypes[i])) continue;
                    var access = FieldAccess.Find(type, mirror.Names[i]);
                    if (access != null) children.Add((mirror.Names[i], access));
                }
            }
            else if (DataModel.Of(type).Kind == DataKind.Object)
            {
                foreach (var field in DataModel.Of(type).Fields)
                    if (!field.Type.IsPrimitive && field.Type != typeof(string) && FieldAccess.Find(type, field.Name) is { } access)
                        children.Add((field.Name, access));
            }
            check = new TypeCheck { Required = required, Children = children.ToArray() };
            _checks[type] = check;
            return check;
        }

        // 이 형식(또는 그 안쪽)에 필수 규칙의 클래스가 올 수 있는지.
        bool MayContain(Type type)
        {
            if (type == null) return false;
            if (_mayContain.TryGetValue(type, out var known)) return known;
            var result = Reaches(type, new HashSet<Type>());
            _mayContain[type] = result;
            return result;
        }

        bool Reaches(Type type, HashSet<Type> seen)
        {
            if (type == null || type.IsPrimitive || type.IsEnum || type == typeof(string) || !seen.Add(type)) return false;
            if (type == typeof(object) || type.IsInterface || type.IsAbstract) return true;
            foreach (var requirement in _required)
                if (requirement.Owner.IsAssignableFrom(type) || type.IsAssignableFrom(requirement.Owner)) return true;
            var element = FieldPaths.ElementTypeOf(type);
            if (element != null) return Reaches(element, seen);
            var mirror = Mirror.Find(type);
            if (mirror == null) return !type.IsValueType;
            for (var i = 0; i < mirror.FieldCount; i++)
                if (Reaches(mirror.FieldTypes[i], seen)) return true;
            return false;
        }

        void FindMissing(object value, HashSet<string> found, int depth)
        {
            if (value == null || depth > 32) return;
            if (FieldPaths.IsCollectionValue(value))
            {
                var index = 0;
                foreach (var item in FieldPaths.Items(value))
                {
                    if (item != null && !(item is string) && !item.GetType().IsPrimitive)
                    {
                        _trail.Add((null, Identity.TryGetId(item, out var id) ? "#" + id : index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                        FindMissing(item, found, depth + 1);
                        _trail.RemoveAt(_trail.Count - 1);
                    }
                    index++;
                }
                return;
            }
            var check = CheckOf(value.GetType());
            foreach (var (name, access) in check.Required)
            {
                var current = access.Get(value);
                if (current == null || (current is string text && text.Length == 0) || (FieldPaths.IsCollectionValue(current) && !FieldPaths.Items(current).Any()))
                    found.Add(Join(Trail(), name));
            }
            foreach (var (name, access) in check.Children)
            {
                _trail.Add((name, null));
                FindMissing(access.Get(value), found, depth + 1);
                _trail.RemoveAt(_trail.Count - 1);
            }
        }

        // 지금 위치의 경로 글자(예: units[#3].weapon).
        string Trail()
        {
            var builder = new System.Text.StringBuilder();
            foreach (var (name, element) in _trail)
            {
                if (name != null)
                {
                    if (builder.Length > 0) builder.Append('.');
                    builder.Append(name);
                }
                else builder.Append('[').Append(element).Append(']');
            }
            return builder.ToString();
        }

        static string Join(string path, string name) => string.IsNullOrEmpty(path) ? name : path + "." + name;
        // ── 트랜잭션 ───────────────────────────────────────────

        public Transaction Begin(long journalId, string label)
        {
            if (_active != null)
                throw new InvalidOperationException($"[MomoiProject] 트랜잭션 '{_active.Label}'이 진행 중입니다. 행동 안에서 다른 행동을 실행할 수 없습니다.");
            // 행동 밖에서 바뀐 것은 먼저 행동 밖 변경으로 남긴다(이 행동의 Undo에 섞이지 않게).
            Sync();
            _active = new Transaction { Id = _nextTransactionId++, JournalId = journalId, Label = label };
            _holding = true;
            return _active;
        }

        // 행동 중에 모아 둔 변경을 차례대로 알린다(한 행동 = 한 묶음).
        void Release()
        {
            _holding = false;
            if (_held.Count == 0) return;
            var held = _held.ToArray();
            _held.Clear();
            if (Synced == null) return;
            foreach (var changes in held) Synced(changes);
        }

        public void End()
        {
            if (_active == null) return;
            Sync();
            var tx = _active;
            _active = null;
            Release();
            if (!RecordHistory) return;
            // 값이 바뀌지 않은 행동(빗나감 등)도 하나의 Undo 단위로 남긴다(난수 소비도 되돌려야 하므로).
            _history.Add(tx);
            if (HistoryLimit > 0 && _history.Count > HistoryLimit)
            {
                var oldest = _history[0];
                _history.RemoveAt(0);
                TransactionForgotten?.Invoke(oldest);
            }
        }

        // 행동 안의 저장점: 지금까지의 변경을 굳혀 두고, 나중에 RollbackTo로 그 뒤의 변경만 되돌린다(행동 하나 안의 작은 실패 처리용).
        // 행동 밖이면 null. 저장점을 만들 때 Sync를 한 번 한다(자주 만들지 않는다).
        public Savepoint Mark()
        {
            if (_active == null) return null;
            Sync();
            var tx = _active;
            tx.SealedMirrored = tx.Mirrored.Count;
            var point = new Savepoint { Tx = tx, Mirrored = tx.Mirrored.Count, Values = tx.ValueChanges.Count, Data = tx.DataChanges.Count, LegacyAt = new Dictionary<string, object>(StringComparer.Ordinal) };
            // 예전 방식 데이터는 키마다 행동 전 모습 하나만 들고 있으므로, 이미 건드린 키는 지금 모습을 따로 떠 둔다.
            foreach (var record in tx.DataChanges)
                if (_slots.TryGetValue(record.Key, out var slot)) point.LegacyAt[record.Key] = DataModel.Clone(slot.Value, Identity);
            return point;
        }

        // 저장점 뒤의 변경만 되돌린다(그 앞의 변경과 행동은 그대로). 되돌린 변경은 이 행동의 Undo 기록에서도 빠진다.
        public void RollbackTo(Savepoint point)
        {
            if (point == null || _active == null || _active != point.Tx) return;
            Sync();
            var tx = _active;
            var tail = new Transaction { Id = tx.Id, JournalId = tx.JournalId, Label = tx.Label };
            tail.Mirrored.AddRange(tx.Mirrored.Skip(point.Mirrored));
            tx.Mirrored.RemoveRange(point.Mirrored, tx.Mirrored.Count - point.Mirrored);
            tail.ValueChanges.AddRange(tx.ValueChanges.Skip(point.Values));
            tx.ValueChanges.RemoveRange(point.Values, tx.ValueChanges.Count - point.Values);
            tail.DataChanges.AddRange(tx.DataChanges.Skip(point.Data));
            tx.DataChanges.RemoveRange(point.Data, tx.DataChanges.Count - point.Data);
            foreach (var pair in point.LegacyAt)
                if (_slots.TryGetValue(pair.Key, out var slot)) tail.DataChanges.Add(new DataRecord { Key = pair.Key, Zone = slot.Zone, Before = pair.Value });
            Revert(tail, "부분 취소");
            tx.SealedMirrored = tx.Mirrored.Count;
        }

        // 행동 실행 중 예외가 나면 진행 중이던 변화를 모두 되돌린다(원자성).
        public void Abort()
        {
            if (_active == null) return;
            Sync();
            var tx = _active;
            _active = null;
            // 중단은 진행분과 되돌림을 함께 알린다: 받는 쪽은 한 묶음으로 적용해 같은 결과(Permanent 변경만 남음)가 된다.
            Revert(tx, "중단");
            Release();
        }

        // 이 안의 변화는 Undo 이력에 넣지 않는다(도전과제 달성·보상처럼 한 번 일어나면 되돌리지 않을 일).
        // 예: using (state.Permanent()) state.Set("achievement:first", true);
        public IDisposable Permanent() => new PermanentScope(this);

        sealed class PermanentScope : IDisposable
        {
            readonly PlayerDataStore _store;
            readonly Transaction _suspended;

            // 들어가기 전 변경은 진행 중인 행동의 것으로, 안의 변경은 이력 밖으로 Sync한다.
            public PermanentScope(PlayerDataStore store)
            {
                _store = store;
                store.Sync();
                _suspended = store._active;
                store._active = null;
            }

            public void Dispose()
            {
                _store.Sync();
                _store._active = _suspended;
            }
        }

        // 마지막 행동을 되돌린다. 이력에 남은 행동은 언제든(몇 번이든) 뒤에서부터 Undo할 수 있다.
        public bool UndoLast()
        {
            if (_active != null)
                throw new InvalidOperationException("[MomoiProject] 행동 실행 중에는 Undo할 수 없습니다.");
            if (_history.Count == 0) return false;
            Sync();

            var tx = _history[_history.Count - 1];
            var reshaped = Reshaped(tx);
            if (reshaped != null)
            {
                _journal.Append(JournalKind.Warning, "Data",
                    $"Undo '{tx.Label}'를 할 수 없습니다: 그 뒤 행동 밖에서 리스트 구조가 바뀌었습니다({reshaped.Replace(":", ".")}). " +
                    "되돌리면 번호가 밀려 엉뚱한 요소가 바뀝니다. 리스트에 더하기·빼기·순서 바꾸기는 행동 안에서 하세요.");
                return false;
            }
            _history.RemoveAt(_history.Count - 1);

            var entry = _journal.Append(JournalKind.Undo, "Data", $"Undo: {tx.Label}", transactionId: tx.Id);
            using (_journal.Cause(entry.Id))
            {
                var overwritten = Overwritten(tx);
                try
                {
                    Revert(tx, "Undo");
                }
                catch (Exception e)
                {
                    // 끝까지 되돌리지 못했다(예상하지 못한 구조 변화). 지금 모습을 기준으로 다시 맞추고 알린다(이력에는 다시 넣지 않는다 — 일부는 이미 되돌렸다).
                    _journal.Append(JournalKind.Warning, "Data", $"Undo '{tx.Label}'를 끝까지 되돌리지 못해 일부만 되돌렸습니다: {e.Message}");
                    Sync();
                }
                if (overwritten.Count > 0)
                    _journal.Append(JournalKind.Warning, "Data",
                        $"Undo '{tx.Label}'가 행동 밖에서 바뀐 값을 덮었습니다: {string.Join(", ", overwritten.Take(5).Select(p => p.Replace(":", ".")))}" +
                        (overwritten.Count > 5 ? $" 외 {overwritten.Count - 5}곳" : "") + ". 되돌릴 수 있어야 하는 변경은 행동 안에서 하세요.");
            }
            // 이 행동 뒤의 행동 밖 변경은 그 앞 행동 뒤의 것이기도 하다.
            if (tx.OutsideAfter != null && _history.Count > 0)
            {
                var previous = _history[_history.Count - 1];
                previous.OutsideAfter ??= new HashSet<string>(StringComparer.Ordinal);
                previous.OutsideAfter.UnionWith(tx.OutsideAfter);
            }
            if (tx.ReshapedAfter != null && _history.Count > 0)
                (_history[_history.Count - 1].ReshapedAfter ??= new HashSet<string>(StringComparer.Ordinal)).UnionWith(tx.ReshapedAfter);
            TransactionUndone?.Invoke(tx);
            return true;
        }

        public void ClearHistory()
        {
            foreach (var tx in _history) TransactionForgotten?.Invoke(tx);
            _history.Clear();
        }
        void Revert(Transaction tx, string reason)
        {
            // Mirror 데이터: 뒤로 기록을 거꾸로 적용하고, 다시 Sync해 되돌린 내용을 기록·전파한다.
            if (tx.Mirrored.Count > 0)
            {
                var keys = new List<string>();
                for (int i = tx.Mirrored.Count - 1; i >= 0; i--)
                {
                    var changes = tx.Mirrored[i];
                    if (!_slots.TryGetValue(changes.Key, out var mirrored) || mirrored.Mirror == null) continue;
                    Mirror.Undo(mirrored.Value, changes);
                    if (!keys.Contains(changes.Key)) keys.Add(changes.Key);
                }
                foreach (var key in keys) SyncSlot(key, _slots[key], JournalKind.Undo, $"{reason}: ", tx.Id);
            }
            // 단순 값: 행동이 남긴 값과 지금 값이 다르면 행동 밖에서 바뀐 것(되돌리면 덮인다).
            var lastSeen = new HashSet<string>(StringComparer.Ordinal);
            var overwritten = new List<string>();
            for (int i = tx.ValueChanges.Count - 1; i >= 0; i--)
            {
                var change = tx.ValueChanges[i];
                var slot = _slots[change.Key];
                var current = slot.Value;
                if (reason == "Undo" && lastSeen.Add(change.Key) && !Equals(current, change.New)) overwritten.Add(change.Key);
                slot.Value = change.Old;
                _lastEntry[change.Key] = _journal.Append(JournalKind.Undo, "Data", $"{reason}: " + Describe(change.Key, current, change.Old), change.Key, current, change.Old, tx.Id).Id;
                Changed?.Invoke(change.Key);
            }
            if (overwritten.Count > 0)
                _journal.Append(JournalKind.Warning, "Data", $"Undo '{tx.Label}'가 행동 밖에서 바뀐 값을 덮었습니다: {string.Join(", ", overwritten)}. 되돌릴 수 있어야 하는 변경은 행동 안에서 하세요.");

            // 데이터 값: 행동 전 스냅샷으로 제자리 복원(인스턴스·요소 ID 유지).
            for (int i = tx.DataChanges.Count - 1; i >= 0; i--)
            {
                var record = tx.DataChanges[i];
                var slot = _slots[record.Key];
                if (DetectUntrackedChanges) AdoptUntracked(record.Key, slot);
                var before = slot.Shadow ?? DataModel.Clone(slot.Value, Identity);
                slot.Value = DataModel.ApplyInPlace(slot.Value, record.Before, Identity);
                RefreshShadow(slot);
                var changes = DataModel.Diff(before, slot.Value, Identity);
                if (changes.Count == 0) continue;
                LogChanges(record.Key, slot, changes, JournalKind.Undo, $"{reason}: ", tx.Id);
                Changed?.Invoke(record.Key);
                DataChanged?.Invoke(record.Key, changes);
                CheckRequired(record.Key, slot);
            }
        }

        // ── 저장·불러오기 ───────────────────────────────────────
        // 데이터 값은 저장 트리(필드 이름 기준, enum은 이름, 요소 ID 포함)로 바꿔 내보낸다.

        // report: 참조를 저장할 수 없는 등의 경고를 기록에 남길지(스냅샷처럼 자주 부를 때는 끈다).
        // keepReferences: 메모리 안에서만 쓸 트리(되감기 스냅샷) — 에셋 참조를 ID 대신 그대로 둔다.
        public Dictionary<string, object> Export(DataZone zone, bool report = true, bool keepReferences = false)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            var previous = DataModel.KeepReferences;
            DataModel.KeepReferences = keepReferences;
            try
            {
                foreach (var pair in _slots)
                {
                    var slot = pair.Value;
                    if (slot.Zone != zone) continue;
                    var warnings = new List<string>();
                    result[pair.Key] = slot.DataType == null
                        ? DataModel.ToTree(slot.Value, slot.Value?.GetType() ?? typeof(object), Identity, warnings, slot.Label)
                        : DataModel.ToTree(slot.Value, slot.DataType, Identity, warnings, slot.Label);
                    if (report) foreach (var warning in warnings.Distinct()) _journal.Append(JournalKind.Warning, "Save", warning, pair.Key);
                }
                // 불러왔지만 아직 정의되지 않은 키(라이브러리가 처음 쓸 때 정의하는 $id.… · $modifiers · $world …)도 그대로 다시 쓴다 —
                // 이번 실행에서 그 기능을 쓰지 않았다고 저장에서 빠지면 다음 실행에서 번호가 겹치거나 값이 사라진다.
                foreach (var pair in _preloaded)
                    if (_preloadedZone.TryGetValue(pair.Key, out var preloadedZone) && preloadedZone == zone && !result.ContainsKey(pair.Key))
                        result[pair.Key] = pair.Value;
            }
            finally
            {
                DataModel.KeepReferences = previous;
            }
            return result;
        }

        // 스냅샷(Export로 만든 트리)으로 되돌린다. 바뀐 곳은 기록하고 알린다(인스턴스·요소 ID 유지).
        // 실시간 게임의 되감기가 쓴다. 이 되돌림은 Undo 이력에 남지 않는다.
        public void Restore(Dictionary<string, object> tree, DataZone zone, string reason)
        {
            if (_active != null)
                throw new InvalidOperationException("[MomoiProject] 행동 실행 중에는 되감을 수 없습니다.");
            foreach (var pair in _slots.ToList())
            {
                var key = pair.Key;
                var slot = pair.Value;
                if (slot.Zone != zone) continue;
                // 그 시점에 없던 키(뒤에 처음 생긴 상태 효과·예약 …)는 처음 값으로.
                if (!tree.TryGetValue(key, out var saved)) saved = DefaultTree(slot);
                if (slot.DataType != null && slot.Mirror != null)
                {
                    SyncSlot(key, slot);
                    slot.Value = LoadData(key, slot, saved);
                    SyncSlot(key, slot, JournalKind.Undo, $"{reason}: ", 0);
                }
                else if (slot.DataType != null)
                {
                    if (DetectUntrackedChanges) AdoptUntracked(key, slot);
                    var before = slot.Shadow ?? DataModel.Clone(slot.Value, Identity);
                    slot.Value = LoadData(key, slot, saved);
                    RefreshShadow(slot);
                    var changes = DataModel.Diff(before, slot.Value, Identity);
                    if (changes.Count == 0) continue;
                    LogChanges(key, slot, changes, JournalKind.Undo, $"{reason}: ", 0);
                    Changed?.Invoke(key);
                    DataChanged?.Invoke(key, changes);
                    CheckRequired(key, slot);
                }
                else if (TryNormalize(ReadScalar(slot, saved), out var value) && !Equals(value, slot.Value))
                {
                    var current = slot.Value;
                    slot.Value = value;
                    _lastEntry[key] = _journal.Append(JournalKind.Undo, "Data", $"{reason}: " + Describe(key, current, value), key, current, value).Id;
                    Changed?.Invoke(key);
                }
            }
        }
        // 아직 정의되지 않은 키는 보관했다가 Define 시점에 적용한다. 이름이 바뀐 키는 새 이름으로 옮긴다.
        public void Import(Dictionary<string, object> values, DataZone zone)
        {
            // 이 구역의 앞 저장에서 미리 불러 둔 값은 버린다(새 저장이 대신한다).
            foreach (var stale in _preloadedZone.Where(p => p.Value == zone).Select(p => p.Key).ToList())
            {
                _preloaded.Remove(stale);
                _preloadedZone.Remove(stale);
            }
            var present = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in values)
            {
                var key = _aliases.TryGetValue(pair.Key, out var renamed) && !values.ContainsKey(renamed) ? renamed : pair.Key;
                present.Add(key);
                if (!_slots.TryGetValue(key, out var slot))
                {
                    _preloaded[key] = pair.Value;
                    _preloadedZone[key] = zone;
                    continue;
                }
                if (slot.Zone != zone) continue;
                if (slot.DataType != null)
                {
                    slot.Value = LoadData(key, slot, pair.Value);
                    // 불러온 값이 새 출발점: 기록 없이 사본을 다시 만든다. 받는 쪽(멀티플레이)에는 처음 모습을 통째로 보낸다.
                    ChooseMirror(key, slot);
                    if (slot.Mirror != null && Synced != null) Synced(new ChangeSet { Key = key, Snapshot = Mirror.Snapshot(slot.Mirror) });
                    RefreshShadow(slot);
                    CheckRequired(key, slot);
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
            // 저장에 없는 이 구역의 키(저장한 뒤에 처음 생긴 상태 효과·월드 상태·기다리는 선택·예약 …)는 처음 값으로 — 불러온 모습에 섞이지 않게.
            foreach (var pair in _slots.ToList())
            {
                var key = pair.Key;
                var slot = pair.Value;
                if (slot.Zone != zone || present.Contains(key)) continue;
                if (slot.DataType != null)
                {
                    if (DataModel.Diff(slot.Value, slot.Default, Identity).Count == 0) continue;
                    slot.Value = DataModel.ApplyInPlace(slot.Value, DataModel.Clone(slot.Default, Identity), Identity);
                    ChooseMirror(key, slot);
                    if (slot.Mirror != null && Synced != null) Synced(new ChangeSet { Key = key, Snapshot = Mirror.Snapshot(slot.Mirror) });
                    RefreshShadow(slot);
                    CheckRequired(key, slot);
                }
                else
                {
                    if (Equals(slot.Value, slot.Default)) continue;
                    slot.Value = slot.Default;
                }
                Changed?.Invoke(key);
            }
        }

        // 처음 값의 저장 트리(되감기에서 그 시점에 없던 키를 되돌릴 때).
        object DefaultTree(Slot slot)
        {
            if (slot.DataType == null) return slot.Default;
            return DataModel.ToTree(slot.Default, slot.DataType, Identity, new List<string>(), slot.Label);
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

        // ── 멀티플레이 ─────────────────────────────────────────
        // 서버는 키마다 보낼 곳(공개 범위)을 보고 바뀐 곳을 클라이언트에 보낸다(NetServer). 클라이언트는 받은 처음 모습·바뀐 곳을
        // 아래 Remote 함수로 적용한다 — 행동 밖 변경으로 적용되므로 화면(Changed·DataChanged)은 싱글과 똑같이 알림을 받는다.

        readonly Dictionary<string, (DataVisibility visibility, int owner)> _visibility = new Dictionary<string, (DataVisibility, int)>(StringComparer.Ordinal);

        // 공개 범위를 정한다(키 단위). owner는 Owner일 때의 플레이어 번호(행동의 actorId와 같은 번호).
        public void SetVisibility(string key, DataVisibility visibility, int owner = 0)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("[MomoiProject] 플레이어 데이터 ID가 비어 있습니다.", nameof(key));
            _visibility[key] = (visibility, owner);
            VisibilityChanged?.Invoke(key);
        }

        // 공개 범위가 바뀌었을 때(서버가 새로 보이게 된 클라이언트에 처음 모습을 보낸다).
        public event Action<string> VisibilityChanged;

        // 정한 공개 범위. 정하지 않았으면 세션 구역은 Public, 메타 구역은 Server.
        public DataVisibility VisibilityOf(string key, out int owner)
        {
            if (key != null && _visibility.TryGetValue(key, out var set))
            {
                owner = set.owner;
                return set.visibility;
            }
            owner = 0;
            return key != null && _slots.TryGetValue(key, out var slot) && slot.Zone == DataZone.Meta ? DataVisibility.Server : DataVisibility.Public;
        }

        // 이 플레이어(번호)의 클라이언트에 보내는가.
        public bool IsVisibleTo(string key, int player)
        {
            switch (VisibilityOf(key, out var owner))
            {
                case DataVisibility.Public: return true;
                case DataVisibility.Owner: return owner == player;
                default: return false;
            }
        }

        // 데이터 안 객체의 사본(받는 쪽에 보낸 객체 번호가 든다). 데이터 밖이면 null. 이벤트를 보낼 때 객체를 번호로 가리키는 데 쓴다.
        internal MirrorNode NodeOf(object live)
        {
            if (live == null) return null;
            foreach (var key in SlotKeys())
            {
                var mirror = _slots[key].Mirror;
                if (mirror?.Tree != null && mirror.Tree.Nodes.TryGetValue(live, out var node)) return node;
            }
            return null;
        }

        // 키가 Mirror(생성 코드 비교)로 다뤄지는 데이터 값인지(바뀐 곳만 보낼 수 있는지).
        internal bool IsMirroredKey(string key) => key != null && _slots.TryGetValue(key, out var slot) && slot.Mirror != null;

        // 받는 쪽: 처음 모습(PlayerDataStore.Snapshot)으로 키의 값을 바꾼다. 없는 키면 그 값의 형식으로 새로 등록한다.
        // 값은 새 인스턴스로 바뀐다(받는 쪽이 객체 번호로 이 인스턴스들을 잇는다).
        public void LoadRemote(string key, DataZone zone, string label, byte[] snapshot, MirrorReceiver receiver)
        {
            var value = receiver.Load(snapshot);
            if (value == null) return;
            if (!_slots.TryGetValue(key, out var slot))
            {
                DefineData(key, zone, value, label);
                slot = _slots[key];
            }
            if (slot.DataType == null) throw new InvalidOperationException($"[MomoiProject] 플레이어 데이터 '{key}'는 단순 값입니다(받은 것은 데이터 값).");
            slot.DataType = value.GetType();
            slot.Value = value;
            ChooseMirror(key, slot);
            RefreshShadow(slot);
            _lastEntry[key] = _journal.Append(JournalKind.Data, "Net", $"{slot.Label}  받음(처음 모습)", key).Id;
            Changed?.Invoke(key);
            DataChanged?.Invoke(key, Array.Empty<DataChange>());
            CheckRequired(key, slot);
        }

        // 받는 쪽: 보내는 쪽의 앞으로 기록(ChangeSet.Forward)을 적용한다. 행동 밖 변경으로 기록·알림된다.
        public void ApplyRemote(string key, byte[] data, int start, int end, MirrorReceiver receiver)
        {
            var slot = GetSlot(key);
            if (slot.Mirror == null || slot.Value == null)
                throw new InvalidOperationException($"[MomoiProject] 플레이어 데이터 '{key}'는 바뀐 곳을 받을 수 없습니다(처음 모습을 먼저 받아야 합니다).");
            receiver.Apply(slot.Value, data, start, end);
            SyncSlot(key, slot);
        }

        // 받는 쪽: 단순 값을 바꾼다. 없는 키면 새로 등록한다.
        public void SetRemote(string key, DataZone zone, string label, object value)
        {
            if (!_slots.TryGetValue(key, out var slot))
            {
                DefineValue(key, zone, null, value, label);
                _lastEntry[key] = _journal.Append(JournalKind.Data, "Net", $"{LabelOf(key)}  받음 {Format(value)}", key).Id;
                Changed?.Invoke(key);
                return;
            }
            if (slot.DataType != null) throw new InvalidOperationException($"[MomoiProject] 플레이어 데이터 '{key}'는 데이터 값입니다(받은 것은 단순 값).");
            var normalized = Normalize(key, value);
            var old = slot.Value;
            if (Equals(old, normalized)) return;
            slot.Value = normalized;
            _lastEntry[key] = _journal.Append(JournalKind.Data, "Net", Describe(key, old, normalized), key, old, normalized).Id;
            Changed?.Invoke(key);
        }

        // 받는 쪽: Mirror로 다룰 수 없는 데이터 값(예전 방식)을 저장 트리째 받는다.
        public void LoadRemoteTree(string key, DataZone zone, string label, Type type, object tree)
        {
            if (!_slots.TryGetValue(key, out var slot))
            {
                if (type == null) return;
                DefineData(key, zone, DataModel.Create(type), label);
                slot = _slots[key];
            }
            if (slot.DataType == null) return;
            var before = slot.Shadow ?? DataModel.Clone(slot.Value, Identity);
            slot.Value = LoadData(key, slot, tree);
            RefreshShadow(slot);
            var changes = DataModel.Diff(before, slot.Value, Identity);
            if (changes.Count == 0) return;
            LogChanges(key, slot, changes, JournalKind.Data, "받음: ", 0);
            Changed?.Invoke(key);
            DataChanged?.Invoke(key, changes);
        }

        // 키 하나를 기본값으로(행동 안이면 그 행동의 변경).
        public void ResetKey(string key)
        {
            var slot = GetSlot(key);
            if (slot.DataType != null || !Equals(slot.Value, slot.Default)) Set(key, slot.Default);
        }

        // 이 객체가 들어 있는 플레이어 데이터(Mirror 데이터만). 없으면 null.
        internal string KeyOf(object live)
        {
            if (live == null) return null;
            foreach (var key in SlotKeys())
            {
                var mirror = _slots[key].Mirror;
                if (mirror?.Tree != null && mirror.Tree.Nodes.ContainsKey(live)) return key;
            }
            return null;
        }

        // 저장 트리 → 이 키 형식의 새 객체(지금 값에 적용하지 않는다).
        internal object FromTree(string key, object tree)
        {
            var slot = GetSlot(key);
            if (slot.DataType == null) return null;
            var warnings = new List<string>();
            var value = DataModel.FromTree(tree, slot.DataType, Identity, warnings, slot.Label);
            foreach (var warning in warnings.Distinct()) _journal.Append(JournalKind.Warning, "Data", warning, key);
            return value;
        }

        // 저장 트리 → 이 단순 값 키의 값.
        internal object ScalarFromTree(string key, object tree)
        {
            var slot = GetSlot(key);
            return TryNormalize(ReadScalar(slot, tree), out var value) ? value : slot.Default;
        }

        // 보내는 쪽: 예전 방식 데이터 값의 저장 트리(받는 쪽 LoadRemoteTree).
        internal object ExportTree(string key)
        {
            var slot = GetSlot(key);
            return DataModel.ToTree(slot.Value, slot.DataType ?? slot.Value?.GetType() ?? typeof(object), Identity, new List<string>(), slot.Label);
        }

        public void ResetZone(DataZone zone)
        {
            // 이 구역을 처음부터: 미리 불러 둔(아직 정의되지 않은) 옛 값이 새 판에 들어오지 않게 버린다.
            foreach (var stale in _preloadedZone.Where(p => p.Value == zone).Select(p => p.Key).ToList())
            {
                _preloaded.Remove(stale);
                _preloadedZone.Remove(stale);
            }
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
                var prefix = IdRules.GroupOf(pair.Key);
                if (!groups.TryGetValue(prefix, out var group))
                {
                    group = new InspectNode(prefix);
                    groups.Add(prefix, group);
                }
                var slot = pair.Value;
                // 데이터 값은 글자 한 칸으로 고칠 수 없으므로 편집 키를 넘기지 않는다.
                var node = group.Add(slot.Label, Format(slot.Value), slot.DataType == null ? pair.Key : null);
                node.Tag = ZoneName(slot.Zone);
                if (slot.DataType != null) node.DataKey = pair.Key;
                node.Note = TypeText(slot);
            }
            foreach (var group in groups.Values) root.Children.Add(group);
            root.Add("Undo할 수 있는 행동", _history.Count.ToString(CultureInfo.InvariantCulture));
        }

        // ── 도우미 ─────────────────────────────────────────────

        Slot GetSlot(string key)
        {
            if (key != null && _slots.TryGetValue(key, out var slot)) return slot;
            throw new InvalidOperationException($"[MomoiProject] 정의되지 않은 플레이어 데이터 '{key}'입니다. DB 창의 플레이어 데이터에 등록하거나 Define으로 먼저 등록하세요.");
        }

        static string ZoneName(DataZone zone) => zone == DataZone.Meta ? "메타" : "세션";

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
            throw new ArgumentException($"[MomoiProject] 플레이어 데이터 '{key}'에 저장할 수 없는 값 형식입니다: {value.GetType().Name}.");
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
