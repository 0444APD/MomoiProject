using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace GBible
{
    // 플레이어 데이터 저장소에 담을 수 있는 임의의 데이터(클래스·구조체·리스트·배열·딕셔너리)를 다루는 도구.
    // 타입마다 한 번만 분석해 캐시하고, 복제·비교·제자리 복원·저장 변환을 리플렉션으로 자동 처리한다.
    // 담을 수 있는 것: 데이터 필드(public 또는 [SerializeField]). 담지 않는 것: 함수(델리게이트).
    // 같은 객체를 두 곳에서 가리키거나 서로 가리켜도 된다(복제·복원·저장 뒤에도 같은 인스턴스를 가리킨다 — Mirror와 같다). 컬렉션은 한 곳에서만 가리킨다.
    // 에셋 참조(UnityEngine.Object)는 복제하지 않고 참조 그대로 둔다(저장은 DataModel.ReferenceToId 연결 시).

    public enum DataKind
    {
        Scalar,     // bool, 숫자, string
        Enum,
        Reference,  // UnityEngine.Object 등 복제하지 않는 참조
        Object,     // 클래스·구조체
        List,       // IList 구현(List<T> 등)
        Array,
        Dictionary,
        Collection, // 그 밖의 컬렉션: HashSet·SortedSet·LinkedList·Queue·Stack 등
        Unsupported,
    }

    public sealed class DataField
    {
        public string Name;
        public FieldInfo Field;
        public Type Type;
        // [FormerlySerializedAs]로 남긴 예전 이름. 저장 데이터를 읽을 때 따라간다.
        public string[] FormerNames = Array.Empty<string>();

        // [FieldLabel] 글(처음 한 번 찾아 둔다 — 바뀐 곳마다 기록 문장을 만들 때 리플렉션하지 않게). 없으면 null.
        string _label;
        bool _labelResolved;

        public string Label
        {
            get
            {
                if (_labelResolved) return _label;
                _label = Field?.GetCustomAttributes(typeof(FieldLabelAttribute), true).OfType<FieldLabelAttribute>().FirstOrDefault()?.Text;
                _labelResolved = true;
                return _label;
            }
        }

        // 읽기·쓰기: 생성 코드(FieldAccess)가 있으면 그것으로(저장·불러오기·비교에 리플렉션 없음), 없으면 리플렉션.
        MemberAccess _access;
        bool _resolved;

        MemberAccess Access
        {
            get
            {
                if (_resolved) return _access;
                _access = FieldAccess.Find(Field.DeclaringType, Name);
                if (_access != null && !_access.Generated) _access = null;
                _resolved = true;
                return _access;
            }
        }

        public object Get(object target) => Access is { } access ? access.Get(target) : Field.GetValue(target);

        public void Set(object target, object value)
        {
            var access = Access;
            if (access == null)
            {
                Field.SetValue(target, value);
                return;
            }
            // 생성 코드는 정확한 형식만 받는다: 값 형식 필드의 null은 기본값으로.
            if (value == null && Type.IsValueType && Nullable.GetUnderlyingType(Type) == null) value = DataModel.Create(Type);
            access.Set(target, value);
        }
    }

    public sealed class DataType
    {
        public Type Type;
        public DataKind Kind;
        public Type ElementType;
        public Type KeyType;
        public IReadOnlyList<DataField> Fields = Array.Empty<DataField>();
        public string Problem;
        // Collection 전용: 순서가 없는 집합인지, 요소를 꺼내고 다시 채우는 방법.
        public bool Unordered;
        internal Func<object, List<object>> ReadItems;
        internal Action<object, List<object>> WriteItems;

        // 요소마다 내부 ID를 붙여 추적하는 타입(참조형 클래스). 구조체·값은 순번으로 다룬다.
        public bool HasIdentity => Kind == DataKind.Object && !Type.IsValueType;
    }

    public enum DataChangeKind
    {
        Set,
        Insert,
        Remove,
        Reorder,
    }

    // 데이터 한 곳의 변화. Path 예: "items[#12].level", "tags[3]", "slots[\"head\"]".
    public sealed class DataChange
    {
        public DataChangeKind Kind;
        public string Path;
        public object Old;
        public object New;
        public long ElementId;

        public override string ToString()
        {
            switch (Kind)
            {
                case DataChangeKind.Insert: return $"{Path} 추가: {DataModel.Summary(New)}";
                case DataChangeKind.Remove: return $"{Path} 제거: {DataModel.Summary(Old)}";
                case DataChangeKind.Reorder: return $"{Path} 순서 변경";
                default: return $"{Path}  {DataModel.Summary(Old)} → {DataModel.Summary(New)}";
            }
        }
    }

    // 컬렉션 요소(클래스 인스턴스)의 내부 ID. 개발자는 인스턴스로만 다루고, ID는 저장소가 알아서 붙인다.
    // 불러오기·되돌리기 뒤에도 같은 ID로 이어져 로그와 조건이 같은 물건을 가리킨다.
    public sealed class DataIdentity
    {
        sealed class Box
        {
            public long Id;
        }

        readonly ConditionalWeakTable<object, Box> _ids = new ConditionalWeakTable<object, Box>();
        readonly Dictionary<long, WeakReference> _live = new Dictionary<long, WeakReference>();
        long _next = 1;

        public bool TryGetId(object instance, out long id)
        {
            if (instance != null && _ids.TryGetValue(instance, out var box))
            {
                id = box.Id;
                return true;
            }
            id = 0;
            return false;
        }

        public long IdOf(object instance)
        {
            if (TryGetId(instance, out var id)) return id;
            id = _next++;
            Assign(instance, id, true);
            return id;
        }

        // primary: 실제 게임에서 쓰는 인스턴스. 복제본(스냅샷)은 ID만 공유하고 대표가 되지 않는다.
        public void Assign(object instance, long id, bool primary)
        {
            if (_ids.TryGetValue(instance, out var box)) box.Id = id;
            else _ids.Add(instance, new Box { Id = id });
            if (primary && (!_live.TryGetValue(id, out var weak) || !ReferenceEquals(weak.Target, instance)))
            {
                _live[id] = new WeakReference(instance);
                // 사라진 인스턴스의 줄을 가끔 치운다(오래 켜 둔 게임·서버에서 요소가 생기고 없어질 때마다 표가 끝없이 커지지 않게).
                if (++_sinceSweep >= SweepEvery && _live.Count >= SweepEvery) Sweep();
            }
            if (id >= _next) _next = id + 1;
        }

        const int SweepEvery = 4096;
        int _sinceSweep;

        void Sweep()
        {
            _sinceSweep = 0;
            List<long> dead = null;
            foreach (var pair in _live)
                if (!pair.Value.IsAlive) (dead ??= new List<long>()).Add(pair.Key);
            if (dead != null)
                foreach (var id in dead) _live.Remove(id);
        }

        public object Find(long id) => _live.TryGetValue(id, out var weak) ? weak.Target : null;
    }

    public static class DataModel
    {
        static readonly Dictionary<Type, DataType> Cache = new Dictionary<Type, DataType>();
        // 복제·비교·복원·저장 변환이 따라가는 객체 깊이(게임 데이터는 이만큼 깊지 않다 — 스택이 넘치기 전에 멈춘다). Mirror와 같다.
        const int MaxDepth = 256;

        // 에셋 참조 저장 연결(Unity 층에서 GameData ID 등으로 연결한다). 없으면 참조는 저장되지 않는다.
        public static Func<object, string> ReferenceToId;
        public static Func<string, Type, object> IdToReference;
        // 참이면 ToTree가 에셋 참조를 ID 대신 참조 그대로 넣는다(메모리 안에서만 쓰는 트리: 되감기 스냅샷).
        [ThreadStatic] internal static bool KeepReferences;

        // ── 타입 분석 ───────────────────────────────────────────

        public static DataType Of(Type type)
        {
            // int? 같은 nullable은 속의 타입으로 다룬다(값이 없으면 null).
            var underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null) return Of(underlying);
            lock (Cache)
            {
                if (Cache.TryGetValue(type, out var cached)) return cached;
                var info = new DataType { Type = type };
                Cache[type] = info;
                Analyze(info);
                return info;
            }
        }

        public static bool IsScalarType(Type type) =>
            type == typeof(string) || type == typeof(bool) || TypeResolver.IsNumeric(type) || type == typeof(char) ||
            type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan) || type == typeof(Guid);

        public static bool IsReferenceType(Type type)
        {
            for (var t = type; t != null; t = t.BaseType)
                if (t.FullName == "UnityEngine.Object") return true;
            return false;
        }

        static void Analyze(DataType info)
        {
            var type = info.Type;
            if (IsScalarType(type))
            {
                info.Kind = DataKind.Scalar;
                return;
            }
            if (type.IsEnum)
            {
                info.Kind = DataKind.Enum;
                return;
            }
            if (IsReferenceType(type))
            {
                info.Kind = DataKind.Reference;
                return;
            }
            if (typeof(Delegate).IsAssignableFrom(type) || type.IsPointer || type == typeof(IntPtr))
            {
                info.Kind = DataKind.Unsupported;
                info.Problem = "함수·포인터는 데이터가 아닙니다";
                return;
            }
            // 인터페이스·추상 컬렉션 칸(IList<T>·IDictionary<K, V>·IReadOnlyList<T> …): 불러올 때 만들 실제 형식을 정할 수 없고, 비교 코드(Mirror)도 안쪽 변경을 따라가지 못한다.
            if ((type.IsInterface || type.IsAbstract) && typeof(IEnumerable).IsAssignableFrom(type))
            {
                info.Kind = DataKind.Unsupported;
                info.Problem = "인터페이스·추상 컬렉션 형식은 담을 수 없습니다 — List<T>·Dictionary<K, V>로 선언하세요";
                return;
            }
            if (type.IsArray)
            {
                info.Kind = type.GetArrayRank() == 1 ? DataKind.Array : DataKind.Unsupported;
                info.ElementType = type.GetElementType();
                if (info.Kind == DataKind.Unsupported) info.Problem = "다차원 배열은 지원하지 않습니다";
                return;
            }
            var dictionary = FindGeneric(type, typeof(IDictionary<,>));
            if (dictionary != null)
            {
                info.Kind = DataKind.Dictionary;
                info.KeyType = dictionary.GetGenericArguments()[0];
                info.ElementType = dictionary.GetGenericArguments()[1];
                if (!IsScalarType(info.KeyType) && !info.KeyType.IsEnum)
                {
                    info.Kind = DataKind.Unsupported;
                    info.Problem = "딕셔너리 키는 숫자·글자·참거짓·enum·날짜·시간·Guid만 됩니다";
                }
                return;
            }
            var list = FindGeneric(type, typeof(IList<>));
            if (list != null && typeof(IList).IsAssignableFrom(type))
            {
                info.Kind = DataKind.List;
                info.ElementType = list.GetGenericArguments()[0];
                return;
            }
            if (TryCollection(info)) return;
            if (type.IsPrimitive || type == typeof(object))
            {
                info.Kind = DataKind.Unsupported;
                info.Problem = type == typeof(object) ? "object 필드는 형식을 알 수 없습니다" : "지원하지 않는 기본 형식입니다";
                return;
            }

            info.Kind = DataKind.Object;
            var fields = new List<DataField>();
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.IsInitOnly) continue;
                    if (field.IsDefined(typeof(NonSerializedAttribute), false)) continue;
                    // 자동 속성의 숨은 필드는 [field: SerializeField]일 때만(Unity·Mirror와 같다).
                    if (field.Name.Contains("<") && !HasAttribute(field, "SerializeField")) continue;
                    if (!field.IsPublic && !HasAttribute(field, "SerializeField") && !HasAttribute(field, "SerializeReference")) continue;
                    fields.Add(new DataField
                    {
                        Name = field.Name,
                        Field = field,
                        Type = field.FieldType,
                        FormerNames = FormerNamesOf(field),
                    });
                }
            }
            info.Fields = fields;
        }

        static Type FindGeneric(Type type, Type definition)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == definition) return type;
            return type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == definition);
        }

        // 리스트·딕셔너리가 아닌 컬렉션: 요소를 꺼내고(ReadItems) 비운 뒤 다시 채우는(WriteItems) 방법을 정해 둔다.
        static bool TryCollection(DataType info)
        {
            var type = info.Type;
            if (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(Queue<>) || type.GetGenericTypeDefinition() == typeof(Stack<>)))
            {
                var isStack = type.GetGenericTypeDefinition() == typeof(Stack<>);
                var clear = type.GetMethod("Clear", Type.EmptyTypes);
                var add = type.GetMethod(isStack ? "Push" : "Enqueue");
                info.Kind = DataKind.Collection;
                info.ElementType = type.GetGenericArguments()[0];
                info.ReadItems = value => ((IEnumerable)value).Cast<object>().ToList();
                info.WriteItems = (target, items) =>
                {
                    clear.Invoke(target, null);
                    // 스택은 위에서부터 나열되므로 거꾸로 쌓아야 같은 모양이 된다.
                    if (isStack)
                        for (int i = items.Count - 1; i >= 0; i--) add.Invoke(target, new[] { items[i] });
                    else
                        foreach (var item in items) add.Invoke(target, new[] { item });
                };
                return true;
            }

            var collection = FindGeneric(type, typeof(ICollection<>));
            if (collection == null) return false;
            var clearMethod = collection.GetMethod("Clear");
            var addMethod = collection.GetMethod("Add");
            info.Kind = DataKind.Collection;
            info.ElementType = collection.GetGenericArguments()[0];
            info.Unordered = FindGeneric(type, typeof(ISet<>)) != null;
            info.ReadItems = value => ((IEnumerable)value).Cast<object>().ToList();
            info.WriteItems = (target, items) =>
            {
                clearMethod.Invoke(target, null);
                foreach (var item in items) addMethod.Invoke(target, new[] { item });
            };
            return true;
        }

        static bool HasAttribute(MemberInfo member, string name) =>
            member.GetCustomAttributes(true).Any(a => a.GetType().Name == name || a.GetType().Name == name + "Attribute");

        static string[] FormerNamesOf(FieldInfo field)
        {
            var names = new List<string>();
            foreach (var attribute in field.GetCustomAttributes(true))
            {
                if (attribute.GetType().Name != "FormerlySerializedAsAttribute") continue;
                var property = attribute.GetType().GetProperty("oldName");
                if (property?.GetValue(attribute) is string old) names.Add(old);
            }
            return names.ToArray();
        }

        // 타입 전체를 따라가며 담을 수 없는 필드를 찾는다. 저장소 등록 시 한 번 검사한다.
        public static List<string> Validate(Type type)
        {
            var problems = new List<string>();
            Validate(type, type.Name, problems, new HashSet<Type>());
            return problems;
        }

        static void Validate(Type type, string path, List<string> problems, HashSet<Type> seen)
        {
            var info = Of(type);
            if (info.Kind == DataKind.Unsupported)
            {
                problems.Add($"{path} ({type.Name}): {info.Problem}");
                return;
            }
            if (!seen.Add(type)) return;
            switch (info.Kind)
            {
                case DataKind.Object:
                    foreach (var field in info.Fields) Validate(field.Type, path + "." + field.Name, problems, seen);
                    break;
                case DataKind.List:
                case DataKind.Array:
                case DataKind.Collection:
                    Validate(info.ElementType, path + "[]", problems, seen);
                    break;
                case DataKind.Dictionary:
                    Validate(info.ElementType, path + "[키]", problems, seen);
                    break;
            }
        }

        // ── 복제 ───────────────────────────────────────────────
        // primary: 만든 복제본을 게임에서 쓸 실제 인스턴스로 등록한다(불러오기·복원 시).
        // 같은 객체(클래스 인스턴스)를 두 곳에서 가리키면 복제본에서도 두 곳이 같은 복제본을 가리킨다 — 서로 가리키는 객체도 그대로다(Mirror와 같다).
        // 컬렉션(리스트·배열·딕셔너리)은 가리키는 곳마다 따로 복제한다(컬렉션 인스턴스는 한 곳에서만 가리킨다 — 관례).

        sealed class CopyState
        {
            // 원본 객체 → 결과(복제본, 제자리 복원이면 채운 인스턴스). 클래스 인스턴스만 담는다.
            public readonly Dictionary<object, object> Results = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
            // 제자리 복원에서 이미 채운 인스턴스(원본 둘이 인스턴스 하나에 겹쳐 채워지지 않게).
            public readonly HashSet<object> Filled = new HashSet<object>(ReferenceEqualityComparer.Instance);
            public DataIdentity Identity;
            public bool Primary;
            // 거짓이면 ID가 없는 요소에 새 ID를 붙이지 않는다(밖에서 온 값 — 처음 값·게임 코드가 넘긴 값 — 의 요소가 ID표에 실제 인스턴스로 오르지 않게).
            public bool AssignIds = true;
            public int Nodes;
        }

        public static object Clone(object value, DataIdentity identity, bool primary = false) =>
            Clone(value, identity, primary, out _);

        // 밖에서 온 값(처음 값·Set에 넘긴 값)의 복제: 이미 ID가 있는 요소(지금 데이터의 인스턴스)는 그 ID를 복제본에 잇고,
        // ID가 없는 요소에는 붙이지 않는다 — 원본의 객체가 ID표에 실제 인스턴스로 올라 제자리 복원이 원본을 데이터 안으로 끌어오지 않게.
        internal static object CloneForeign(object value, DataIdentity identity) =>
            Clone(value, new CopyState { Identity = identity, AssignIds = false }, "", 0);

        // nodes: 복제한 요소 수(값 크기 경고에 쓴다).
        public static object Clone(object value, DataIdentity identity, bool primary, out int nodes)
        {
            var state = new CopyState { Identity = identity, Primary = primary };
            var copy = Clone(value, state, "", 0);
            nodes = state.Nodes;
            return copy;
        }

        static object Clone(object value, CopyState state, string path, int depth)
        {
            if (value == null) return null;
            var info = Of(value.GetType());
            switch (info.Kind)
            {
                case DataKind.Scalar:
                case DataKind.Enum:
                case DataKind.Reference:
                    state.Nodes++;
                    return value;
                case DataKind.Unsupported:
                    throw new InvalidOperationException($"[MomoiProject] '{path}'에 담을 수 없는 값입니다: {info.Problem}.");
            }
            // 앞에서 복제한 객체: 같은 복제본을 가리킨다(두 곳에서 가리키는 객체·서로 가리키는 객체).
            if (info.HasIdentity && state.Results.TryGetValue(value, out var done)) return done;
            state.Nodes++;
            if (depth > MaxDepth) throw new InvalidOperationException($"[MomoiProject] '{path}'가 너무 깊이 겹쳐 있습니다({MaxDepth}단계 넘게).");
            switch (info.Kind)
            {
                case DataKind.Array:
                {
                    var source = (Array)value;
                    var copy = Array.CreateInstance(info.ElementType, source.Length);
                    for (int i = 0; i < source.Length; i++)
                        copy.SetValue(CloneElement(source.GetValue(i), state, $"{path}[{i}]", depth), i);
                    return copy;
                }
                case DataKind.List:
                {
                    var source = (IList)value;
                    var copy = (IList)Create(info.Type);
                    for (int i = 0; i < source.Count; i++)
                        copy.Add(CloneElement(source[i], state, $"{path}[{i}]", depth));
                    return copy;
                }
                case DataKind.Collection:
                {
                    var items = info.ReadItems(value);
                    var copies = new List<object>(items.Count);
                    for (int i = 0; i < items.Count; i++) copies.Add(CloneElement(items[i], state, $"{path}[{i}]", depth));
                    var copy = Create(info.Type);
                    info.WriteItems(copy, copies);
                    return copy;
                }
                case DataKind.Dictionary:
                {
                    var source = (IDictionary)value;
                    var copy = (IDictionary)Create(info.Type);
                    foreach (DictionaryEntry pair in source)
                        copy[pair.Key] = Clone(pair.Value, state, $"{path}[{pair.Key}]", depth + 1);
                    return copy;
                }
                default:
                {
                    var copy = Create(info.Type);
                    // 필드를 채우기 전에 등록한다 — 필드가 이 객체를 다시 가리키면(서로 가리킴) 이 복제본으로 잇는다.
                    if (info.HasIdentity) state.Results.Add(value, copy);
                    foreach (var field in info.Fields)
                        field.Set(copy, Clone(field.Get(value), state, Join(path, field.Name), depth + 1));
                    return copy;
                }
            }
        }

        static object CloneElement(object element, CopyState state, string path, int depth)
        {
            var copy = Clone(element, state, path, depth + 1);
            if (state.Identity != null && element != null && copy != null && Of(element.GetType()).HasIdentity)
            {
                if (state.AssignIds) state.Identity.Assign(copy, state.Identity.IdOf(element), state.Primary);
                else if (state.Identity.TryGetId(element, out var id)) state.Identity.Assign(copy, id, state.Primary);
            }
            return copy;
        }

        // root 안(필드·컬렉션을 따라)에서 match에 맞는 첫 객체(클래스·구조체). 두 곳에서 가리키는 객체는 한 번만 본다(서로 가리켜도 끝난다).
        // 비교 코드(Mirror)가 없는 예전 방식 데이터에서 객체를 찾을 때 쓴다(리플렉션).
        internal static object FindObject(object root, Func<object, bool> match)
        {
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            var stack = new Stack<object>();
            if (root != null) stack.Push(root);
            while (stack.Count > 0)
            {
                var value = stack.Pop();
                var info = Of(value.GetType());
                if (info.Kind == DataKind.Scalar || info.Kind == DataKind.Enum || info.Kind == DataKind.Reference || info.Kind == DataKind.Unsupported) continue;
                if (!info.Type.IsValueType && !seen.Add(value)) continue;
                if (info.Kind == DataKind.Object && match(value)) return value;
                switch (info.Kind)
                {
                    case DataKind.Object:
                        for (int i = info.Fields.Count - 1; i >= 0; i--)
                            if (info.Fields[i].Get(value) is object child) stack.Push(child);
                        break;
                    case DataKind.Dictionary:
                        foreach (DictionaryEntry pair in (IDictionary)value)
                            if (pair.Value != null) stack.Push(pair.Value);
                        break;
                    case DataKind.Collection:
                        foreach (var item in info.ReadItems(value))
                            if (item != null) stack.Push(item);
                        break;
                    default:
                        foreach (var item in (IEnumerable)value)
                            if (item != null) stack.Push(item);
                        break;
                }
            }
            return null;
        }

        public static object Create(Type type)
        {
            if (type.IsValueType) return Activator.CreateInstance(type);
            // 생성 코드가 있으면 그것으로(인자 없는 생성자, 없으면 생성자 없이).
            if (Mirror.Find(type) is { } mirror && mirror.New != null) return mirror.New();
            var constructor = type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (constructor != null) return constructor.Invoke(null);
            return FormatterServices.GetUninitializedObject(type);
        }

        // ── 비교 ───────────────────────────────────────────────
        // before(이전 스냅샷)와 after(현재 값)를 비교해 바뀐 곳만 경로로 모은다.
        // 컬렉션의 클래스 요소는 내부 ID로 짝지어 추가·제거·수정을 구분한다.

        public static List<DataChange> Diff(object before, object after, DataIdentity identity)
        {
            var state = new DiffState { Identity = identity, Changes = new List<DataChange>() };
            Diff(before, after, "", state, 0);
            return state.Changes;
        }

        sealed class DiffState
        {
            public DataIdentity Identity;
            public List<DataChange> Changes;
            // 이미 비교한 (이전, 지금) 객체 짝 — 두 곳에서 가리키는 객체는 한 번만, 서로 가리키는 객체는 끝없이 돌지 않게.
            public HashSet<RefPair> Compared;
        }

        static void Diff(object before, object after, string path, DiffState state, int depth)
        {
            var changes = state.Changes;
            if (before == null && after == null) return;
            if (before == null || after == null || before.GetType() != after.GetType())
            {
                changes.Add(new DataChange { Kind = DataChangeKind.Set, Path = path, Old = before, New = after });
                return;
            }
            if (depth > MaxDepth) return;
            var info = Of(after.GetType());
            switch (info.Kind)
            {
                case DataKind.Scalar:
                case DataKind.Enum:
                    if (!Equals(before, after)) changes.Add(new DataChange { Kind = DataChangeKind.Set, Path = path, Old = before, New = after });
                    return;
                case DataKind.Reference:
                    if (!ReferenceEquals(before, after)) changes.Add(new DataChange { Kind = DataChangeKind.Set, Path = path, Old = before, New = after });
                    return;
                case DataKind.Object:
                    if (info.HasIdentity && !(state.Compared ??= new HashSet<RefPair>()).Add(new RefPair(before, after))) return;
                    foreach (var field in info.Fields)
                        Diff(field.Get(before), field.Get(after), Join(path, field.Name), state, depth + 1);
                    return;
                case DataKind.Dictionary:
                {
                    var a = (IDictionary)before;
                    var b = (IDictionary)after;
                    foreach (DictionaryEntry pair in a)
                        if (!b.Contains(pair.Key)) changes.Add(new DataChange { Kind = DataChangeKind.Remove, Path = $"{path}[{KeyText(pair.Key)}]", Old = pair.Value });
                    foreach (DictionaryEntry pair in b)
                    {
                        var itemPath = $"{path}[{KeyText(pair.Key)}]";
                        if (!a.Contains(pair.Key)) changes.Add(new DataChange { Kind = DataChangeKind.Insert, Path = itemPath, New = pair.Value });
                        else Diff(a[pair.Key], pair.Value, itemPath, state, depth + 1);
                    }
                    return;
                }
                case DataKind.List:
                case DataKind.Array:
                    DiffList((IList)before, (IList)after, info.ElementType, false, path, state, depth);
                    return;
                case DataKind.Collection:
                    DiffList(info.ReadItems(before), info.ReadItems(after), info.ElementType, info.Unordered, path, state, depth);
                    return;
            }
        }

        static void DiffList(IList before, IList after, Type elementType, bool unordered, string path, DiffState state, int depth)
        {
            var identity = state.Identity;
            var changes = state.Changes;
            var element = Of(elementType);
            bool tracked = identity != null && (element.HasIdentity || elementType.IsInterface || elementType.IsAbstract || elementType == typeof(object));
            if (!tracked && unordered)
            {
                // 순서 없는 집합의 값 요소: 내용이 같은지만 본다.
                var a = before.Cast<object>().ToList();
                var b = after.Cast<object>().ToList();
                if (a.Count != b.Count || a.Any(x => !b.Contains(x)))
                    changes.Add(new DataChange { Kind = DataChangeKind.Set, Path = path, Old = before, New = after });
                return;
            }
            if (!tracked)
            {
                if (before.Count != after.Count)
                {
                    changes.Add(new DataChange { Kind = DataChangeKind.Set, Path = path, Old = before, New = after });
                    return;
                }
                for (int i = 0; i < after.Count; i++) Diff(before[i], after[i], $"{path}[{i}]", state, depth + 1);
                return;
            }

            // 요소 ID로 짝짓는다. 새로 들어온 인스턴스는 여기서 ID를 받는다.
            var beforeIds = new List<long>();
            var beforeById = new Dictionary<long, object>();
            foreach (var item in before)
            {
                if (item == null) continue;
                var id = identity.IdOf(item);
                beforeIds.Add(id);
                beforeById[id] = item;
            }
            var afterIds = new List<long>();
            var afterById = new Dictionary<long, object>();
            foreach (var item in after)
            {
                if (item == null) continue;
                var id = identity.IdOf(item);
                afterIds.Add(id);
                afterById[id] = item;
            }

            foreach (var id in beforeIds)
                if (!afterById.ContainsKey(id))
                    changes.Add(new DataChange { Kind = DataChangeKind.Remove, Path = $"{path}[#{id}]", Old = beforeById[id], ElementId = id });
            foreach (var id in afterIds)
            {
                var itemPath = $"{path}[#{id}]";
                if (!beforeById.TryGetValue(id, out var old))
                    changes.Add(new DataChange { Kind = DataChangeKind.Insert, Path = itemPath, New = afterById[id], ElementId = id });
                else
                    Diff(old, afterById[id], itemPath, state, depth + 1);
            }
            if (unordered) return;
            var keptBefore = beforeIds.Where(afterById.ContainsKey).ToList();
            var keptAfter = afterIds.Where(beforeById.ContainsKey).ToList();
            if (!keptBefore.SequenceEqual(keptAfter) || SlotsMoved(before, after, identity)) changes.Add(new DataChange { Kind = DataChangeKind.Reorder, Path = path });
        }

        // 빈 칸(null)이 섞인 같은 길이 목록(장비 칸 배열 등)에서 남은 요소가 다른 칸으로 옮겨졌는가 — [A, null] → [null, A]는 순서는 같아도 바뀐 것이다.
        static bool SlotsMoved(IList before, IList after, DataIdentity identity)
        {
            if (before.Count != after.Count) return false;
            bool empty = false;
            for (int i = 0; i < before.Count && !empty; i++) empty = before[i] == null || after[i] == null;
            if (!empty) return false;
            var at = new Dictionary<long, int>();
            for (int i = 0; i < after.Count; i++)
                if (after[i] != null) at[identity.IdOf(after[i])] = i;
            for (int i = 0; i < before.Count; i++)
                if (before[i] != null && at.TryGetValue(identity.IdOf(before[i]), out var now) && now != i) return true;
            return false;
        }

        // ── 제자리 복원 ─────────────────────────────────────────
        // target(현재 값)을 source(스냅샷)와 같게 만든다. 가능한 한 기존 인스턴스를 그대로 두고 필드만 되돌려,
        // 게임 코드가 쥐고 있던 인스턴스와 요소 ID가 이어지게 한다. 결과(새로 만들었으면 새 객체)를 돌려준다.

        // 원본 안에서 두 곳이 같은 객체를 가리키면 결과에서도 같은 인스턴스를 가리킨다(서로 가리키는 객체도 끝없이 돌지 않는다).
        // 인스턴스 하나는 원본 하나로만 채운다 — 이미 다른 원본으로 채운 인스턴스 자리에는 복제본을 둔다.
        public static object ApplyInPlace(object target, object source, DataIdentity identity) =>
            Apply(target, source, new CopyState { Identity = identity, Primary = true }, 0);

        static object Apply(object target, object source, CopyState state, int depth)
        {
            if (source == null) return null;
            var info = Of(source.GetType());
            switch (info.Kind)
            {
                case DataKind.Scalar:
                case DataKind.Enum:
                case DataKind.Reference:
                    return source;
            }
            if (info.HasIdentity)
            {
                // 앞에서 채운(또는 복제한) 원본: 그 결과를 가리킨다.
                if (state.Results.TryGetValue(source, out var done)) return done;
                if (target != null && state.Filled.Contains(target)) target = null;
            }
            if (target == null || target.GetType() != source.GetType() || info.Type.IsValueType)
                return Clone(source, state, "", depth);
            if (depth > MaxDepth) throw new InvalidOperationException($"[MomoiProject] 되돌릴 값이 너무 깊이 겹쳐 있습니다({MaxDepth}단계 넘게).");

            switch (info.Kind)
            {
                case DataKind.Object:
                    state.Results[source] = target;
                    state.Filled.Add(target);
                    foreach (var field in info.Fields)
                    {
                        var current = field.Get(target);
                        var next = Apply(current, field.Get(source), state, depth + 1);
                        if (!ReferenceEquals(current, next) || field.Type.IsValueType) field.Set(target, next);
                    }
                    return target;
                case DataKind.Dictionary:
                {
                    var t = (IDictionary)target;
                    var s = (IDictionary)source;
                    // 원본 쪽을 먼저 옮겨 둔다(같은 딕셔너리를 고치며 훑지 않게).
                    var entries = new List<DictionaryEntry>(s.Count);
                    foreach (DictionaryEntry pair in s) entries.Add(pair);
                    foreach (var key in t.Keys.Cast<object>().ToList())
                        if (!s.Contains(key)) t.Remove(key);
                    foreach (var pair in entries)
                        t[pair.Key] = Apply(t.Contains(pair.Key) ? t[pair.Key] : null, pair.Value, state, depth + 1);
                    return target;
                }
                case DataKind.Array:
                {
                    var t = (Array)target;
                    var s = (Array)source;
                    if (t.Length != s.Length) return Clone(source, state, "", depth);
                    var items = RestoreElements(ToList(t), ToList(s), state, depth);
                    for (int i = 0; i < items.Count; i++) t.SetValue(items[i], i);
                    return target;
                }
                case DataKind.Collection:
                {
                    var items = RestoreElements(info.ReadItems(target), info.ReadItems(source), state, depth);
                    info.WriteItems(target, items);
                    return target;
                }
                case DataKind.List:
                {
                    var t = (IList)target;
                    var items = RestoreElements(ToList(t), ToList((IList)source), state, depth);
                    t.Clear();
                    foreach (var item in items) t.Add(item);
                    return target;
                }
            }
            return Clone(source, state, "", depth);
        }

        static List<object> ToList(IList list)
        {
            var result = new List<object>(list.Count);
            foreach (var item in list) result.Add(item);
            return result;
        }

        static List<object> RestoreElements(List<object> current, List<object> source, CopyState state, int depth)
        {
            var identity = state.Identity;
            Dictionary<long, object> byId = null;
            HashSet<long> claimed = null;
            if (identity != null)
            {
                foreach (var item in current)
                    if (item != null && identity.TryGetId(item, out var id)) (byId ??= new Dictionary<long, object>())[id] = item;
                foreach (var item in source)
                    if (item != null && identity.TryGetId(item, out var id)) (claimed ??= new HashSet<long>()).Add(id);
            }

            var result = new List<object>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                var src = source[i];
                if (src != null && identity != null && Of(src.GetType()).HasIdentity && identity.TryGetId(src, out var id))
                {
                    // 같은 ID의 인스턴스가 지금 목록에 있거나(수정), 빠졌지만 아직 살아 있으면(제거 취소) 그것을 되살린다.
                    var instance = byId != null && byId.TryGetValue(id, out var present) ? present : identity.Find(id);
                    if (instance != null && instance.GetType() != src.GetType()) instance = null;
                    var restored = Apply(instance, src, state, depth + 1);
                    identity.Assign(restored, id, true);
                    result.Add(restored);
                    continue;
                }
                // ID가 없는 요소는 같은 칸의 인스턴스를 다시 쓴다 — 다른 요소가 ID로 데려갈 인스턴스는 빼고.
                var slot = i < current.Count ? current[i] : null;
                if (slot != null && claimed != null && identity.TryGetId(slot, out var slotId) && claimed.Contains(slotId)) slot = null;
                result.Add(Apply(slot, src, state, depth + 1));
            }
            return result;
        }

        // ── 저장 변환 ───────────────────────────────────────────
        // 저장 트리: null, bool, double, string, Dictionary<string, object>, List<object>(MiniJson이 그대로 쓴다).
        // 필드 이름을 키로 쓰고, enum은 이름으로, 선언과 실제 타입이 다르면 "$type", 추적 요소는 "$id"를 남긴다.
        // 한 값 안에서 같은 객체가 다시 나오면(두 곳에서 가리킴·서로 가리킴) 처음 쓴 곳에 번호("$n")를 달고 다음부터는 {"$ref": 번호}만 쓴다 —
        // 불러오면 두 곳이 같은 인스턴스를 가리킨다(Mirror와 같다).

        // double이 정확히 담는 가장 큰 정수(2^53).
        const long MaxExactInteger = 9007199254740992;

        sealed class TreeWriter
        {
            public DataIdentity Identity;
            // 참이면 ID가 없는 컬렉션 요소에 ID를 붙여 "$id"를 남긴다(같은 세션 안에서 되돌릴 트리 — 되감기 지점·스냅샷 — 가 요소를 인스턴스로 짝짓게).
            public bool AssignIds;
            public List<string> Warnings;
            // 쓴 객체 → 그 저장 트리(두 번째로 나오면 앞의 트리에 번호를 단다).
            public readonly Dictionary<object, Dictionary<string, object>> Written = new Dictionary<object, Dictionary<string, object>>(ReferenceEqualityComparer.Instance);
            public int Anchors;
        }

        public static object ToTree(object value, Type declared, DataIdentity identity, List<string> warnings, string path = "") =>
            ToTree(value, declared, new TreeWriter { Identity = identity, Warnings = warnings }, path, 0);

        // assignIds: ID가 없는 컬렉션 요소에도 ID를 붙여 쓴다(같은 세션 안에서 되돌릴 트리).
        internal static object ToTree(object value, Type declared, DataIdentity identity, List<string> warnings, string path, bool assignIds) =>
            ToTree(value, declared, new TreeWriter { Identity = identity, AssignIds = assignIds && identity != null, Warnings = warnings }, path, 0);

        static object ToTree(object value, Type declared, TreeWriter writer, string path, int depth)
        {
            if (value == null) return null;
            var info = Of(value.GetType());
            var warnings = writer.Warnings;
            switch (info.Kind)
            {
                case DataKind.Scalar:
                    switch (value)
                    {
                        case bool _:
                        case string _: return value;
                        case char c: return c.ToString();
                        case DateTime time: return time.ToString("o", CultureInfo.InvariantCulture);
                        case DateTimeOffset offset: return offset.ToString("o", CultureInfo.InvariantCulture);
                        case TimeSpan span: return span.ToString("c", CultureInfo.InvariantCulture);
                        case Guid guid: return guid.ToString("D");
                        // JSON 숫자(double)로 그대로 담을 수 없는 값은 글자로(읽을 때 Convert.ChangeType이 다시 숫자로 바꾼다):
                        // NaN·무한대(JSON에 없음 — null이 되어 0으로 읽힌다), 2^53을 넘는 정수(Steam ID·시드 — 아래 자리가 망가진다), decimal(자릿수).
                        case double d when double.IsNaN(d) || double.IsInfinity(d): return d.ToString("R", CultureInfo.InvariantCulture);
                        case float f when float.IsNaN(f) || float.IsInfinity(f): return f.ToString("R", CultureInfo.InvariantCulture);
                        case long l when l > MaxExactInteger || l < -MaxExactInteger: return l.ToString(CultureInfo.InvariantCulture);
                        case ulong u when u > MaxExactInteger: return u.ToString(CultureInfo.InvariantCulture);
                        case decimal m: return m.ToString(CultureInfo.InvariantCulture);
                    }
                    return Convert.ToDouble(value, CultureInfo.InvariantCulture);
                case DataKind.Enum:
                    return value.ToString();
                case DataKind.Reference:
                {
                    // 메모리 안 스냅샷(되감기)은 참조를 그대로 둔다(스프라이트처럼 데이터베이스에 없는 에셋도 이어진다).
                    if (KeepReferences) return value;
                    var id = ReferenceToId?.Invoke(value);
                    if (id == null) warnings?.Add($"{path}: 저장할 수 없는 참조입니다({value.GetType().Name}). 데이터베이스에 등록된 데이터만 저장됩니다.");
                    return id;
                }
                case DataKind.Unsupported:
                    warnings?.Add($"{path}: 저장할 수 없는 값입니다({info.Problem}).");
                    return null;
            }
            if (depth > MaxDepth)
            {
                warnings?.Add($"{path}: 너무 깊이 겹쳐 있어({MaxDepth}단계 넘게) 이 아래는 저장하지 않습니다.");
                return null;
            }
            switch (info.Kind)
            {
                case DataKind.Array:
                case DataKind.List:
                case DataKind.Collection:
                {
                    var list = new List<object>();
                    int i = 0;
                    foreach (var item in (IEnumerable)value)
                    {
                        if (writer.AssignIds && item != null && Of(item.GetType()).HasIdentity) writer.Identity.IdOf(item);
                        list.Add(ToTree(item, info.ElementType, writer, $"{path}[{i++}]", depth + 1));
                    }
                    return list;
                }
                case DataKind.Dictionary:
                {
                    var map = new Dictionary<string, object>(StringComparer.Ordinal);
                    foreach (DictionaryEntry pair in (IDictionary)value)
                    {
                        var key = KeyText(pair.Key);
                        map[key] = ToTree(pair.Value, info.ElementType, writer, $"{path}[{key}]", depth + 1);
                    }
                    return map;
                }
                default:
                {
                    if (info.HasIdentity && writer.Written.TryGetValue(value, out var first))
                    {
                        // 같은 값 안에서 앞서 쓴 객체: 번호로 가리킨다.
                        if (!first.TryGetValue("$n", out var anchor))
                        {
                            first["$n"] = anchor = (double)++writer.Anchors;
                            // 어느 곳에서 먼저 읽어도 같은 형식으로 만들 수 있게 실제 형식을 남긴다.
                            if (!first.ContainsKey("$type")) first["$type"] = value.GetType().FullName;
                        }
                        return new Dictionary<string, object>(StringComparer.Ordinal) { ["$ref"] = anchor };
                    }
                    var map = new Dictionary<string, object>(StringComparer.Ordinal);
                    // 필드를 쓰기 전에 등록한다 — 필드가 이 객체를 다시 가리키면(서로 가리킴) 번호로 잇는다.
                    if (info.HasIdentity) writer.Written.Add(value, map);
                    if (value.GetType() != declared) map["$type"] = value.GetType().FullName;
                    if (writer.Identity != null && info.HasIdentity && writer.Identity.TryGetId(value, out var id)) map["$id"] = (double)id;
                    foreach (var field in info.Fields)
                        map[field.Name] = ToTree(field.Get(value), field.Type, writer, Join(path, field.Name), depth + 1);
                    return map;
                }
            }
        }

        sealed class TreeReader
        {
            public DataIdentity Identity;
            // 참이면 읽은 요소를 그 ID의 실제 인스턴스로 등록한다(불러오기). 거짓이면 ID만 붙인다(되감기처럼 지금 값에 견줘 볼 과거 모습).
            public bool Primary = true;
            public List<string> Warnings;
            public object Root;
            // 번호("$n") → 만든 인스턴스.
            public Dictionary<long, object> Made;
            // 번호 → 그 저장 트리(앞에서 아직 만나지 않은 번호를 가리킬 때 찾는다).
            public Dictionary<long, Dictionary<string, object>> Anchors;
        }

        // 트리 → 값. 저장에 없는 필드는 클래스의 기본값을 유지한다. 모르는 필드·형식 불일치·읽을 수 없는 값은 warnings에 남기고
        // 그 자리만 기본값으로 읽는다(한 곳이 깨져도 나머지는 읽는다).
        public static object FromTree(object tree, Type declared, DataIdentity identity, List<string> warnings, string path = "") =>
            FromTree(tree, declared, new TreeReader { Identity = identity, Warnings = warnings, Root = tree }, path, 0);

        // 지금 값에 견줘 볼 과거 모습(되감기): 요소에 ID는 붙이되 실제 인스턴스로 등록하지 않는다 — 빠졌던 요소를 되살릴 때 원래 인스턴스를 찾게.
        internal static object FromTreeDetached(object tree, Type declared, DataIdentity identity, List<string> warnings, string path = "") =>
            FromTree(tree, declared, new TreeReader { Identity = identity, Primary = false, Warnings = warnings, Root = tree }, path, 0);

        static object FromTree(object tree, Type declared, TreeReader reader, string path, int depth)
        {
            var warnings = reader.Warnings;
            if (tree == null) return DefaultOf(declared);
            declared = Nullable.GetUnderlyingType(declared) ?? declared;
            if (depth > MaxDepth)
            {
                warnings?.Add($"{path}: 너무 깊이 겹쳐 있어({MaxDepth}단계 넘게) 기본값으로 읽습니다.");
                return DefaultOf(declared);
            }
            var type = declared;
            if (tree is Dictionary<string, object> typed && Of(declared).Kind == DataKind.Object)
            {
                if (typed.Count == 1 && typed.TryGetValue("$ref", out var link)) return Linked(link, declared, reader, path, depth);
                if (typed.TryGetValue("$type", out var typeName) && typeName is string name)
                {
                    var found = TypeResolver.Find(name);
                    if (found != null && declared.IsAssignableFrom(found)) type = found;
                    else warnings?.Add($"{path}: 형식 '{name}'을 찾을 수 없어 {declared.Name}(으)로 읽습니다.");
                }
            }
            var info = Of(type);
            try
            {
                switch (info.Kind)
                {
                    case DataKind.Scalar:
                        if (tree is Dictionary<string, object> || tree is List<object>) throw new InvalidCastException();
                        if (type == typeof(string)) return tree as string ?? Convert.ToString(tree, CultureInfo.InvariantCulture);
                        if (type == typeof(char)) return tree is string s && s.Length > 0 ? s[0] : '\0';
                        if (type == typeof(DateTime)) return DateTime.Parse((string)tree, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                        if (type == typeof(DateTimeOffset)) return DateTimeOffset.Parse((string)tree, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                        if (type == typeof(TimeSpan)) return TimeSpan.ParseExact((string)tree, "c", CultureInfo.InvariantCulture);
                        if (type == typeof(Guid)) return Guid.Parse((string)tree);
                        return Convert.ChangeType(tree, type, CultureInfo.InvariantCulture);
                    case DataKind.Enum:
                        if (tree is string enumName)
                        {
                            if (Enum.IsDefined(type, enumName)) return Enum.Parse(type, enumName);
                            // 여러 값을 겹친 플래그("A, B")·숫자 글자도 읽는다. 없어진 이름이면 아래에서 기본값.
                            try { return Enum.Parse(type, enumName); }
                            catch (ArgumentException) { }
                        }
                        else if (tree is double number) return Enum.ToObject(type, (long)number);
                        warnings?.Add($"{path}: '{TreeText(tree)}'은(는) {type.Name}에 없는 값입니다. 기본값으로 읽습니다.");
                        return Activator.CreateInstance(type);
                    case DataKind.Reference:
                        return tree is string refId ? IdToReference?.Invoke(refId, type) : type.IsInstanceOfType(tree) ? tree : null;
                    case DataKind.Array:
                    {
                        var items = tree as List<object> ?? new List<object>();
                        var array = Array.CreateInstance(info.ElementType, items.Count);
                        for (int i = 0; i < items.Count; i++) array.SetValue(FromElement(items[i], info.ElementType, reader, $"{path}[{i}]", depth + 1), i);
                        return array;
                    }
                    case DataKind.List:
                    {
                        var list = (IList)Create(type);
                        if (tree is List<object> items)
                            for (int i = 0; i < items.Count; i++) list.Add(FromElement(items[i], info.ElementType, reader, $"{path}[{i}]", depth + 1));
                        return list;
                    }
                    case DataKind.Collection:
                    {
                        var collection = Create(type);
                        var values = new List<object>();
                        if (tree is List<object> items)
                            for (int i = 0; i < items.Count; i++) values.Add(FromElement(items[i], info.ElementType, reader, $"{path}[{i}]", depth + 1));
                        info.WriteItems(collection, values);
                        return collection;
                    }
                    case DataKind.Dictionary:
                    {
                        var map = (IDictionary)Create(type);
                        if (tree is Dictionary<string, object> items)
                            foreach (var pair in items)
                            {
                                // 읽을 수 없는 키(없어진 enum 이름 등)는 그 항목만 버린다.
                                if (!TryParseKey(pair.Key, info.KeyType, out var key))
                                {
                                    warnings?.Add($"{path}[{pair.Key}]: 키 '{pair.Key}'을(를) {info.KeyType.Name}(으)로 읽을 수 없어 이 항목은 버립니다.");
                                    continue;
                                }
                                map[key] = FromTree(pair.Value, info.ElementType, reader, $"{path}[{pair.Key}]", depth + 1);
                            }
                        return map;
                    }
                    case DataKind.Object:
                    {
                        if (type.IsAbstract || type.IsInterface)
                        {
                            warnings?.Add($"{path}: {type.Name}은(는) 만들 수 없는 형식(추상 클래스·인터페이스)이라 비워 둡니다.");
                            return null;
                        }
                        if (!(tree is Dictionary<string, object> map))
                        {
                            warnings?.Add($"{path}: 객체가 아닌 값이 저장되어 있어 기본값으로 읽습니다.");
                            return Create(type);
                        }
                        long anchor = -1;
                        if (info.HasIdentity && map.TryGetValue("$n", out var n) && n is double nd)
                        {
                            anchor = (long)nd;
                            // 이 객체를 가리키는 곳을 먼저 읽어 이미 만들었다(필드 순서가 바뀐 저장).
                            if (reader.Made != null && reader.Made.TryGetValue(anchor, out var made) && type.IsInstanceOfType(made)) return made;
                        }
                        var instance = Create(type);
                        // 필드를 읽기 전에 등록한다 — 필드가 이 객체를 다시 가리키면(서로 가리킴) 이 인스턴스로 잇는다.
                        if (anchor >= 0) (reader.Made ??= new Dictionary<long, object>())[anchor] = instance;
                        var used = new HashSet<string>(StringComparer.Ordinal) { "$type", "$id", "$n" };
                        foreach (var field in info.Fields)
                        {
                            var key = map.ContainsKey(field.Name) ? field.Name : field.FormerNames.FirstOrDefault(map.ContainsKey);
                            if (key == null) continue;
                            used.Add(key);
                            field.Set(instance, FromTree(map[key], field.Type, reader, Join(path, field.Name), depth + 1));
                        }
                        foreach (var key in map.Keys)
                            if (!used.Contains(key)) warnings?.Add($"{Join(path, key)}: 지금 {type.Name}에 없는 필드라 무시합니다.");
                        return instance;
                    }
                }
            }
            catch (Exception e) when (e is FormatException || e is InvalidCastException || e is OverflowException || e is ArgumentException)
            {
                warnings?.Add($"{path}: 저장된 값 '{TreeText(tree)}'을(를) {type.Name}(으)로 읽을 수 없어 기본값을 씁니다.");
                return DefaultOf(type);
            }
            warnings?.Add($"{path}: {info.Problem}");
            return null;
        }

        // {"$ref": 번호} — 같은 값 안에서 따로 쓴 객체를 가리킨다.
        static object Linked(object link, Type declared, TreeReader reader, string path, int depth)
        {
            var number = link is double d ? (long)d : -1;
            object linked = null;
            if (reader.Made == null || !reader.Made.TryGetValue(number, out linked))
            {
                // 아직 만나지 않은 객체(필드 순서가 바뀌었거나 처음 쓴 필드가 없어진 저장): 저장 트리에서 찾아 지금 만든다.
                var anchor = FindAnchor(reader, number);
                if (anchor != null) linked = FromTree(anchor, declared, reader, path, depth + 1);
            }
            if (linked != null && declared.IsInstanceOfType(linked)) return linked;
            reader.Warnings?.Add($"{path}: 가리키는 객체(#{number})를 찾을 수 없어 비워 둡니다.");
            return DefaultOf(declared);
        }

        static Dictionary<string, object> FindAnchor(TreeReader reader, long number)
        {
            if (reader.Anchors == null)
            {
                reader.Anchors = new Dictionary<long, Dictionary<string, object>>();
                var stack = new Stack<object>();
                stack.Push(reader.Root);
                while (stack.Count > 0)
                {
                    switch (stack.Pop())
                    {
                        case Dictionary<string, object> map:
                            if (map.TryGetValue("$n", out var n) && n is double d && !reader.Anchors.ContainsKey((long)d)) reader.Anchors.Add((long)d, map);
                            foreach (var child in map.Values)
                                if (child is Dictionary<string, object> || child is List<object>) stack.Push(child);
                            break;
                        case List<object> list:
                            foreach (var child in list)
                                if (child is Dictionary<string, object> || child is List<object>) stack.Push(child);
                            break;
                    }
                }
            }
            return reader.Anchors.TryGetValue(number, out var found) ? found : null;
        }

        static object FromElement(object tree, Type elementType, TreeReader reader, string path, int depth)
        {
            var value = FromTree(tree, elementType, reader, path, depth);
            if (reader.Identity != null && value != null && tree is Dictionary<string, object> map && map.TryGetValue("$id", out var id) && id is double number)
                reader.Identity.Assign(value, (long)number, reader.Primary);
            return value;
        }

        static object DefaultOf(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;

        // 경고 문장에 넣을 저장 값(객체·목록은 통째로 쓰지 않는다).
        static string TreeText(object tree) =>
            tree is Dictionary<string, object> ? "{…}" : tree is List<object> ? "[…]" : Convert.ToString(tree, CultureInfo.InvariantCulture);

        static bool TryParseKey(string text, Type keyType, out object key)
        {
            try
            {
                key = ParseKey(text, keyType);
                return key != null;
            }
            catch (Exception e) when (e is ArgumentException || e is FormatException || e is OverflowException || e is InvalidCastException)
            {
                key = null;
                return false;
            }
        }

        // 딕셔너리 키 글자 → 키(KeyText의 반대). 날짜·시간·Guid는 저장 값과 같은 형식으로 읽는다.
        static object ParseKey(string text, Type keyType)
        {
            if (keyType == typeof(string)) return text;
            if (keyType.IsEnum) return Enum.Parse(keyType, text);
            if (keyType == typeof(DateTime)) return DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            if (keyType == typeof(DateTimeOffset)) return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            if (keyType == typeof(TimeSpan)) return TimeSpan.ParseExact(text, "c", CultureInfo.InvariantCulture);
            if (keyType == typeof(Guid)) return Guid.Parse(text);
            if (keyType == typeof(char)) return text.Length == 1 ? (object)text[0] : throw new FormatException();
            return Convert.ChangeType(text, keyType, CultureInfo.InvariantCulture);
        }

        // ── 구조 서명(저장 호환 판단) ───────────────────────────
        // 타입을 "경로 → 형식" 목록으로 펼친다. 예: items[].level → Int32, element=Fire → enum
        // 저장할 때의 목록과 지금 목록을 비교해, 저장된 경로가 모두 같은 형식으로 남아 있으면 호환(추가는 호환).

        public static void Schema(Type type, string prefix, IDictionary<string, string> into)
        {
            Schema(type, prefix, into, new HashSet<Type>());
        }

        static void Schema(Type type, string prefix, IDictionary<string, string> into, HashSet<Type> visiting)
        {
            var info = Of(type);
            switch (info.Kind)
            {
                case DataKind.Scalar:
                    into[prefix] = (Nullable.GetUnderlyingType(type) ?? type).Name;
                    return;
                case DataKind.Enum:
                    into[prefix] = "enum " + type.Name;
                    // enum은 이름으로 저장되므로, 저장된 값 이름이 없어지면 호환되지 않는다.
                    foreach (var name in Enum.GetNames(type)) into[prefix + "=" + name] = "enum value";
                    return;
                case DataKind.Reference:
                    into[prefix] = "ref " + type.Name;
                    return;
                case DataKind.Unsupported:
                    return;
            }
            if (!visiting.Add(type))
            {
                into[prefix] = "recursive " + type.Name;
                return;
            }
            switch (info.Kind)
            {
                case DataKind.List:
                case DataKind.Array:
                case DataKind.Collection:
                    into[prefix] = "list";
                    Schema(info.ElementType, prefix + "[]", into, visiting);
                    break;
                case DataKind.Dictionary:
                    into[prefix] = "map " + info.KeyType.Name;
                    Schema(info.ElementType, prefix + "[]", into, visiting);
                    break;
                default:
                    into[prefix] = "object";
                    foreach (var field in info.Fields) Schema(field.Type, Join(prefix, field.Name), into, visiting);
                    break;
            }
            visiting.Remove(type);
        }

        // 저장 당시 구조(saved)가 지금 구조(current)에 모두 같은 형식으로 남아 있는지. 아니면 첫 차이를 reason에.
        public static bool IsCompatible(IDictionary<string, string> saved, IDictionary<string, string> current, out string reason)
        {
            foreach (var pair in saved)
            {
                if (!current.TryGetValue(pair.Key, out var now))
                {
                    reason = $"'{pair.Key}'가 없어졌거나 이름이 바뀌었습니다";
                    return false;
                }
                if (now != pair.Value)
                {
                    reason = $"'{pair.Key}'의 형식이 바뀌었습니다({pair.Value} → {now})";
                    return false;
                }
            }
            reason = null;
            return true;
        }

        // 경로를 사람이 읽는 이름으로: items[#12].level → 아이템 #12 › 레벨 (필드 이름은 [FieldLabel]을 쓴다)
        public static string NaturalPath(Type root, string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            var parts = new List<string>();
            var type = root;
            var i = 0;
            while (i < path.Length)
            {
                if (path[i] == '.')
                {
                    i++;
                    continue;
                }
                if (path[i] == '[')
                {
                    var close = path.IndexOf(']', i);
                    if (close < 0) break;
                    var key = path.Substring(i + 1, close - i - 1);
                    if (parts.Count > 0) parts[parts.Count - 1] += key.StartsWith("#", StringComparison.Ordinal) ? " " + key : $" [{key}]";
                    else parts.Add(key);
                    type = type == null ? null : Of(type).ElementType;
                    i = close + 1;
                    continue;
                }
                var end = i;
                while (end < path.Length && path[end] != '.' && path[end] != '[') end++;
                var name = path.Substring(i, end - i);
                var field = type == null ? null : Of(type).Fields.FirstOrDefault(f => f.Name == name);
                var label = field?.Label;
                parts.Add(label ?? name);
                type = field?.Type;
                i = end;
            }
            return string.Join(" › ", parts);
        }
        // ── 표시 ───────────────────────────────────────────────

        public static string Summary(object value)
        {
            switch (value)
            {
                case null: return "없음";
                case bool b: return b ? "예" : "아니오";
                case string s: return s;
                case double d: return d.ToString("0.##", CultureInfo.InvariantCulture);
                case float f: return f.ToString("0.##", CultureInfo.InvariantCulture);
                case IFormattable number when TypeResolver.IsNumeric(value.GetType()): return number.ToString(null, CultureInfo.InvariantCulture);
                case Enum e: return e.ToString();
                case DateTime time: return time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                case TimeSpan span: return span.ToString("c", CultureInfo.InvariantCulture);
                case ICollection collection: return $"{FriendlyName(value.GetType())} {collection.Count}개";
            }
            var info = Of(value.GetType());
            if (info.Kind == DataKind.Collection) return $"{FriendlyName(value.GetType())} {info.ReadItems(value).Count}개";
            if (info.Kind == DataKind.Scalar) return Convert.ToString(value, CultureInfo.InvariantCulture);
            if (info.Kind == DataKind.Reference) return value.ToString();
            // 이름처럼 보이는 필드가 있으면 그 값으로 부른다.
            var label = info.Fields.FirstOrDefault(f => f.Type == typeof(string) &&
                (f.Name.Equals("name", StringComparison.OrdinalIgnoreCase) || f.Name.Equals("displayName", StringComparison.OrdinalIgnoreCase) ||
                 f.Name.Equals("id", StringComparison.OrdinalIgnoreCase)));
            var text = label?.Get(value) as string;
            return string.IsNullOrEmpty(text) ? FriendlyName(value.GetType()) : $"{FriendlyName(value.GetType())} '{text}'";
        }

        public static string FriendlyName(Type type)
        {
            if (type.IsArray) return FriendlyName(type.GetElementType()) + "[]";
            var underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null) return FriendlyName(underlying) + "?";
            if (!type.IsGenericType) return type.Name;
            var name = type.Name.Substring(0, type.Name.IndexOf('`'));
            return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(FriendlyName))}>";
        }

        // 컬렉션 종류 이름: 배열 · 리스트 · 딕셔너리 · 컬렉션(그 밖). 컬렉션이 아니면 null.
        public static string CollectionWord(Type type)
        {
            if (type == null) return null;
            if (type.IsArray) return "배열";
            if (type.IsGenericType)
            {
                var definition = type.GetGenericTypeDefinition();
                if (definition == typeof(List<>)) return "리스트";
                if (definition == typeof(Dictionary<,>) || definition == typeof(SortedDictionary<,>)) return "딕셔너리";
                if (definition == typeof(HashSet<>)) return "집합";
            }
            if (typeof(System.Collections.IDictionary).IsAssignableFrom(type)) return "딕셔너리";
            if (type != typeof(string) && typeof(System.Collections.IEnumerable).IsAssignableFrom(type)) return "컬렉션";
            return null;
        }

        // 화면용 형식 이름: "SampleEquipment 리스트", "int 배열", "string → int 딕셔너리", 그 밖은 C# 이름.
        public static string TypeCaption(Type type)
        {
            if (type == null) return "";
            var word = CollectionWord(type);
            if (word == null) return ShortName(type);
            if (type.IsArray) return $"{ShortName(type.GetElementType())} {word}";
            var args = type.IsGenericType ? type.GetGenericArguments() : Type.EmptyTypes;
            if (args.Length == 2) return $"{ShortName(args[0])} → {ShortName(args[1])} {word}";
            if (args.Length == 1) return $"{ShortName(args[0])} {word}";
            return word;
        }

        static string ShortName(Type type)
        {
            if (type == typeof(int)) return "int";
            if (type == typeof(long)) return "long";
            if (type == typeof(float)) return "float";
            if (type == typeof(double)) return "double";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(string)) return "string";
            return CollectionWord(type) != null ? TypeCaption(type) : FriendlyName(type);
        }

        // 딕셔너리 키 → 글자(저장 트리의 키·경로). 날짜·시간·Guid는 저장 값과 같은 형식(ParseKey가 되읽는다).
        static string KeyText(object key)
        {
            switch (key)
            {
                case null: return "";
                case DateTime time: return time.ToString("o", CultureInfo.InvariantCulture);
                case DateTimeOffset offset: return offset.ToString("o", CultureInfo.InvariantCulture);
                case TimeSpan span: return span.ToString("c", CultureInfo.InvariantCulture);
                case Guid guid: return guid.ToString("D");
                case double d: return d.ToString("R", CultureInfo.InvariantCulture);
                case float f: return f.ToString("R", CultureInfo.InvariantCulture);
                case IFormattable formattable: return formattable.ToString(null, CultureInfo.InvariantCulture);
                default: return key.ToString();
            }
        }

        static string Join(string path, string name) => string.IsNullOrEmpty(path) ? name : path + "." + name;

        sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }

        // 객체 두 개의 짝(참조로 비교).
        readonly struct RefPair : IEquatable<RefPair>
        {
            readonly object _a;
            readonly object _b;

            public RefPair(object a, object b)
            {
                _a = a;
                _b = b;
            }

            public bool Equals(RefPair other) => ReferenceEquals(_a, other._a) && ReferenceEquals(_b, other._b);
            public override bool Equals(object obj) => obj is RefPair other && Equals(other);
            public override int GetHashCode() => unchecked(RuntimeHelpers.GetHashCode(_a) * 397 ^ RuntimeHelpers.GetHashCode(_b));
        }
    }
}
