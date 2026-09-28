using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace GameSystems
{
    // 상태 저장소에 담을 수 있는 임의의 데이터(클래스·구조체·리스트·배열·딕셔너리)를 다루는 도구.
    // 타입마다 한 번만 분석해 캐시하고, 복제·비교·제자리 복원·저장 변환을 리플렉션으로 자동 처리한다.
    // 담을 수 있는 것: 데이터 필드(public 또는 [SerializeField]). 담지 않는 것: 함수(델리게이트), 순환 참조.
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
        Unsupported,
    }

    public sealed class DataField
    {
        public string Name;
        public FieldInfo Field;
        public Type Type;
        // [FormerlySerializedAs]로 남긴 예전 이름. 저장 데이터를 읽을 때 따라간다.
        public string[] FormerNames = Array.Empty<string>();
    }

    public sealed class DataType
    {
        public Type Type;
        public DataKind Kind;
        public Type ElementType;
        public Type KeyType;
        public IReadOnlyList<DataField> Fields = Array.Empty<DataField>();
        public string Problem;

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
            _ids.AddOrUpdate(instance, new Box { Id = id });
            if (primary) _live[id] = new WeakReference(instance);
            if (id >= _next) _next = id + 1;
        }

        public object Find(long id) => _live.TryGetValue(id, out var weak) ? weak.Target : null;
    }

    public static class DataModel
    {
        static readonly Dictionary<Type, DataType> Cache = new Dictionary<Type, DataType>();
        const int MaxDepth = 64;

        // 에셋 참조 저장 연결(Unity 층에서 GameData ID 등으로 연결한다). 없으면 참조는 저장되지 않는다.
        public static Func<object, string> ReferenceToId;
        public static Func<string, Type, object> IdToReference;

        // ── 타입 분석 ───────────────────────────────────────────

        public static DataType Of(Type type)
        {
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
            type == typeof(string) || type == typeof(bool) || TypeResolver.IsNumeric(type) || type == typeof(char);

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
                    info.Problem = "딕셔너리 키는 숫자·글자·enum만 됩니다";
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
                    if (field.Name.Contains("<")) continue; // 자동 속성의 숨은 필드
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
                    Validate(info.ElementType, path + "[]", problems, seen);
                    break;
                case DataKind.Dictionary:
                    Validate(info.ElementType, path + "[키]", problems, seen);
                    break;
            }
        }

        // ── 복제 ───────────────────────────────────────────────
        // primary: 만든 복제본을 게임에서 쓸 실제 인스턴스로 등록한다(불러오기·복원 시).

        public static object Clone(object value, DataIdentity identity, bool primary = false) =>
            Clone(value, new HashSet<object>(ReferenceEqualityComparer.Instance), identity, primary, "", 0);

        static object Clone(object value, HashSet<object> visiting, DataIdentity identity, bool primary, string path, int depth)
        {
            if (value == null) return null;
            var info = Of(value.GetType());
            switch (info.Kind)
            {
                case DataKind.Scalar:
                case DataKind.Enum:
                case DataKind.Reference:
                    return value;
                case DataKind.Unsupported:
                    throw new InvalidOperationException($"[GameSystems] '{path}'에 담을 수 없는 값입니다: {info.Problem}.");
            }
            if (depth > MaxDepth) throw new InvalidOperationException($"[GameSystems] '{path}'가 너무 깊습니다(순환 참조일 수 있습니다).");
            if (!info.Type.IsValueType && !visiting.Add(value))
                throw new InvalidOperationException($"[GameSystems] '{path}'에 순환 참조가 있습니다. 서로를 가리키는 대신 ID로 참조하세요.");
            try
            {
                switch (info.Kind)
                {
                    case DataKind.Array:
                    {
                        var source = (Array)value;
                        var copy = Array.CreateInstance(info.ElementType, source.Length);
                        for (int i = 0; i < source.Length; i++)
                            copy.SetValue(CloneElement(source.GetValue(i), visiting, identity, primary, $"{path}[{i}]", depth), i);
                        return copy;
                    }
                    case DataKind.List:
                    {
                        var source = (IList)value;
                        var copy = (IList)Create(info.Type);
                        for (int i = 0; i < source.Count; i++)
                            copy.Add(CloneElement(source[i], visiting, identity, primary, $"{path}[{i}]", depth));
                        return copy;
                    }
                    case DataKind.Dictionary:
                    {
                        var source = (IDictionary)value;
                        var copy = (IDictionary)Create(info.Type);
                        foreach (DictionaryEntry pair in source)
                            copy[pair.Key] = Clone(pair.Value, visiting, identity, primary, $"{path}[{pair.Key}]", depth + 1);
                        return copy;
                    }
                    default:
                    {
                        var copy = Create(info.Type);
                        foreach (var field in info.Fields)
                            field.Field.SetValue(copy, Clone(field.Field.GetValue(value), visiting, identity, primary, Join(path, field.Name), depth + 1));
                        return copy;
                    }
                }
            }
            finally
            {
                if (!info.Type.IsValueType) visiting.Remove(value);
            }
        }

        static object CloneElement(object element, HashSet<object> visiting, DataIdentity identity, bool primary, string path, int depth)
        {
            var copy = Clone(element, visiting, identity, primary, path, depth + 1);
            if (identity != null && element != null && copy != null && Of(element.GetType()).HasIdentity)
                identity.Assign(copy, identity.IdOf(element), primary);
            return copy;
        }

        public static object Create(Type type)
        {
            if (type.IsValueType) return Activator.CreateInstance(type);
            var constructor = type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (constructor != null) return constructor.Invoke(null);
            return FormatterServices.GetUninitializedObject(type);
        }

        // ── 비교 ───────────────────────────────────────────────
        // before(이전 스냅샷)와 after(현재 값)를 비교해 바뀐 곳만 경로로 모은다.
        // 컬렉션의 클래스 요소는 내부 ID로 짝지어 추가·제거·수정을 구분한다.

        public static List<DataChange> Diff(object before, object after, DataIdentity identity)
        {
            var changes = new List<DataChange>();
            Diff(before, after, "", identity, changes, 0);
            return changes;
        }

        static void Diff(object before, object after, string path, DataIdentity identity, List<DataChange> changes, int depth)
        {
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
                    foreach (var field in info.Fields)
                        Diff(field.Field.GetValue(before), field.Field.GetValue(after), Join(path, field.Name), identity, changes, depth + 1);
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
                        else Diff(a[pair.Key], pair.Value, itemPath, identity, changes, depth + 1);
                    }
                    return;
                }
                case DataKind.List:
                case DataKind.Array:
                    DiffList((IList)before, (IList)after, info, path, identity, changes, depth);
                    return;
            }
        }

        static void DiffList(IList before, IList after, DataType info, string path, DataIdentity identity, List<DataChange> changes, int depth)
        {
            var element = Of(info.ElementType);
            bool tracked = identity != null && (element.HasIdentity || info.ElementType.IsInterface || info.ElementType.IsAbstract || info.ElementType == typeof(object));
            if (!tracked)
            {
                if (before.Count != after.Count)
                {
                    changes.Add(new DataChange { Kind = DataChangeKind.Set, Path = path, Old = before, New = after });
                    return;
                }
                for (int i = 0; i < after.Count; i++) Diff(before[i], after[i], $"{path}[{i}]", identity, changes, depth + 1);
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
                    Diff(old, afterById[id], itemPath, identity, changes, depth + 1);
            }
            var keptBefore = beforeIds.Where(afterById.ContainsKey).ToList();
            var keptAfter = afterIds.Where(beforeById.ContainsKey).ToList();
            if (!keptBefore.SequenceEqual(keptAfter)) changes.Add(new DataChange { Kind = DataChangeKind.Reorder, Path = path });
        }

        // ── 제자리 복원 ─────────────────────────────────────────
        // target(현재 값)을 source(스냅샷)와 같게 만든다. 가능한 한 기존 인스턴스를 그대로 두고 필드만 되돌려,
        // 게임 코드가 쥐고 있던 인스턴스와 요소 ID가 이어지게 한다. 결과(새로 만들었으면 새 객체)를 돌려준다.

        public static object ApplyInPlace(object target, object source, DataIdentity identity)
        {
            if (source == null) return null;
            if (ReferenceEquals(target, source)) return target;
            var info = Of(source.GetType());
            switch (info.Kind)
            {
                case DataKind.Scalar:
                case DataKind.Enum:
                case DataKind.Reference:
                    return source;
            }
            if (target == null || target.GetType() != source.GetType() || info.Type.IsValueType)
                return Clone(source, identity, true);

            switch (info.Kind)
            {
                case DataKind.Object:
                    foreach (var field in info.Fields)
                    {
                        var current = field.Field.GetValue(target);
                        var next = ApplyInPlace(current, field.Field.GetValue(source), identity);
                        if (!ReferenceEquals(current, next) || field.Type.IsValueType) field.Field.SetValue(target, next);
                    }
                    return target;
                case DataKind.Dictionary:
                {
                    var t = (IDictionary)target;
                    var s = (IDictionary)source;
                    foreach (var key in t.Keys.Cast<object>().ToList())
                        if (!s.Contains(key)) t.Remove(key);
                    foreach (DictionaryEntry pair in s)
                        t[pair.Key] = ApplyInPlace(t.Contains(pair.Key) ? t[pair.Key] : null, pair.Value, identity);
                    return target;
                }
                case DataKind.Array:
                {
                    var t = (Array)target;
                    var s = (Array)source;
                    if (t.Length != s.Length) return Clone(source, identity, true);
                    var items = RestoreElements(ToList(t), ToList(s), identity);
                    for (int i = 0; i < items.Count; i++) t.SetValue(items[i], i);
                    return target;
                }
                case DataKind.List:
                {
                    var t = (IList)target;
                    var items = RestoreElements(ToList(t), ToList((IList)source), identity);
                    t.Clear();
                    foreach (var item in items) t.Add(item);
                    return target;
                }
            }
            return Clone(source, identity, true);
        }

        static List<object> ToList(IList list)
        {
            var result = new List<object>(list.Count);
            foreach (var item in list) result.Add(item);
            return result;
        }

        static List<object> RestoreElements(List<object> current, List<object> source, DataIdentity identity)
        {
            var byId = new Dictionary<long, object>();
            if (identity != null)
                foreach (var item in current)
                    if (item != null && identity.TryGetId(item, out var id)) byId[id] = item;

            var result = new List<object>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                var src = source[i];
                if (src != null && identity != null && Of(src.GetType()).HasIdentity && identity.TryGetId(src, out var id))
                {
                    // 같은 ID의 인스턴스가 지금 목록에 있거나(수정), 빠졌지만 아직 살아 있으면(제거 취소) 그것을 되살린다.
                    var instance = byId.TryGetValue(id, out var present) ? present : identity.Find(id);
                    if (instance != null && instance.GetType() == src.GetType())
                    {
                        ApplyInPlace(instance, src, identity);
                        identity.Assign(instance, id, true);
                        result.Add(instance);
                        continue;
                    }
                    var created = Clone(src, identity, true);
                    identity.Assign(created, id, true);
                    result.Add(created);
                    continue;
                }
                result.Add(ApplyInPlace(i < current.Count ? current[i] : null, src, identity));
            }
            return result;
        }

        // ── 저장 변환 ───────────────────────────────────────────
        // 저장 트리: null, bool, double, string, Dictionary<string, object>, List<object>(MiniJson이 그대로 쓴다).
        // 필드 이름을 키로 쓰고, enum은 이름으로, 선언과 실제 타입이 다르면 "$type", 추적 요소는 "$id"를 남긴다.

        public static object ToTree(object value, Type declared, DataIdentity identity, List<string> warnings, string path = "")
        {
            if (value == null) return null;
            var info = Of(value.GetType());
            switch (info.Kind)
            {
                case DataKind.Scalar:
                    if (value is bool || value is string) return value;
                    if (value is char c) return c.ToString();
                    return Convert.ToDouble(value, CultureInfo.InvariantCulture);
                case DataKind.Enum:
                    return value.ToString();
                case DataKind.Reference:
                {
                    var id = ReferenceToId?.Invoke(value);
                    if (id == null) warnings?.Add($"{path}: 저장할 수 없는 참조입니다({value.GetType().Name}). 데이터베이스에 등록된 데이터만 저장됩니다.");
                    return id;
                }
                case DataKind.Array:
                case DataKind.List:
                {
                    var list = new List<object>();
                    int i = 0;
                    foreach (var item in (IList)value) list.Add(ToTree(item, info.ElementType, identity, warnings, $"{path}[{i++}]"));
                    return list;
                }
                case DataKind.Dictionary:
                {
                    var map = new Dictionary<string, object>(StringComparer.Ordinal);
                    foreach (DictionaryEntry pair in (IDictionary)value)
                        map[KeyText(pair.Key)] = ToTree(pair.Value, info.ElementType, identity, warnings, $"{path}[{KeyText(pair.Key)}]");
                    return map;
                }
                case DataKind.Object:
                {
                    var map = new Dictionary<string, object>(StringComparer.Ordinal);
                    if (value.GetType() != declared) map["$type"] = value.GetType().FullName;
                    if (identity != null && info.HasIdentity && identity.TryGetId(value, out var id)) map["$id"] = (double)id;
                    foreach (var field in info.Fields)
                        map[field.Name] = ToTree(field.Field.GetValue(value), field.Type, identity, warnings, Join(path, field.Name));
                    return map;
                }
            }
            warnings?.Add($"{path}: 저장할 수 없는 값입니다({info.Problem}).");
            return null;
        }

        // 트리 → 값. 저장에 없는 필드는 클래스의 기본값을 유지하고, 모르는 필드·형식 불일치는 warnings에 남긴다.
        public static object FromTree(object tree, Type declared, DataIdentity identity, List<string> warnings, string path = "")
        {
            if (tree == null) return declared.IsValueType ? Activator.CreateInstance(declared) : null;
            var type = declared;
            if (tree is Dictionary<string, object> typed && typed.TryGetValue("$type", out var typeName) && typeName is string name)
            {
                var found = TypeResolver.Find(name);
                if (found != null && declared.IsAssignableFrom(found)) type = found;
                else warnings?.Add($"{path}: 형식 '{name}'을 찾을 수 없어 {declared.Name}(으)로 읽습니다.");
            }
            var info = Of(type);
            try
            {
                switch (info.Kind)
                {
                    case DataKind.Scalar:
                        if (type == typeof(string)) return tree as string ?? Convert.ToString(tree, CultureInfo.InvariantCulture);
                        if (type == typeof(char)) return tree is string s && s.Length > 0 ? s[0] : '\0';
                        return Convert.ChangeType(tree, type, CultureInfo.InvariantCulture);
                    case DataKind.Enum:
                        if (tree is string enumName && Enum.IsDefined(type, enumName)) return Enum.Parse(type, enumName);
                        if (tree is double number) return Enum.ToObject(type, (long)number);
                        warnings?.Add($"{path}: '{tree}'은(는) {type.Name}에 없는 값입니다. 기본값으로 읽습니다.");
                        return Activator.CreateInstance(type);
                    case DataKind.Reference:
                        return tree is string refId ? IdToReference?.Invoke(refId, type) : null;
                    case DataKind.Array:
                    {
                        var items = tree as List<object> ?? new List<object>();
                        var array = Array.CreateInstance(info.ElementType, items.Count);
                        for (int i = 0; i < items.Count; i++) array.SetValue(FromElement(items[i], info.ElementType, identity, warnings, $"{path}[{i}]"), i);
                        return array;
                    }
                    case DataKind.List:
                    {
                        var list = (IList)Create(type);
                        if (tree is List<object> items)
                            for (int i = 0; i < items.Count; i++) list.Add(FromElement(items[i], info.ElementType, identity, warnings, $"{path}[{i}]"));
                        return list;
                    }
                    case DataKind.Dictionary:
                    {
                        var map = (IDictionary)Create(type);
                        if (tree is Dictionary<string, object> items)
                            foreach (var pair in items)
                                map[ParseKey(pair.Key, info.KeyType)] = FromTree(pair.Value, info.ElementType, identity, warnings, $"{path}[{pair.Key}]");
                        return map;
                    }
                    case DataKind.Object:
                    {
                        var instance = Create(type);
                        if (!(tree is Dictionary<string, object> map))
                        {
                            warnings?.Add($"{path}: 객체가 아닌 값이 저장되어 있어 기본값으로 읽습니다.");
                            return instance;
                        }
                        var used = new HashSet<string>(StringComparer.Ordinal) { "$type", "$id" };
                        foreach (var field in info.Fields)
                        {
                            var key = map.ContainsKey(field.Name) ? field.Name : field.FormerNames.FirstOrDefault(map.ContainsKey);
                            if (key == null) continue;
                            used.Add(key);
                            field.Field.SetValue(instance, FromTree(map[key], field.Type, identity, warnings, Join(path, field.Name)));
                        }
                        foreach (var key in map.Keys)
                            if (!used.Contains(key)) warnings?.Add($"{Join(path, key)}: 지금 {type.Name}에 없는 필드라 무시합니다.");
                        return instance;
                    }
                }
            }
            catch (Exception e) when (e is FormatException || e is InvalidCastException || e is OverflowException)
            {
                warnings?.Add($"{path}: 저장된 값 '{tree}'을(를) {type.Name}(으)로 읽을 수 없어 기본값을 씁니다.");
                return type.IsValueType ? Activator.CreateInstance(type) : null;
            }
            warnings?.Add($"{path}: {info.Problem}");
            return null;
        }

        static object FromElement(object tree, Type elementType, DataIdentity identity, List<string> warnings, string path)
        {
            var value = FromTree(tree, elementType, identity, warnings, path);
            if (identity != null && value != null && tree is Dictionary<string, object> map && map.TryGetValue("$id", out var id) && id is double number)
                identity.Assign(value, (long)number, true);
            return value;
        }

        static object ParseKey(string text, Type keyType)
        {
            if (keyType == typeof(string)) return text;
            if (keyType.IsEnum) return Enum.Parse(keyType, text);
            return Convert.ChangeType(text, keyType, CultureInfo.InvariantCulture);
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
                case ICollection collection: return $"{FriendlyName(value.GetType())} {collection.Count}개";
            }
            var info = Of(value.GetType());
            if (info.Kind == DataKind.Reference) return value.ToString();
            // 이름처럼 보이는 필드가 있으면 그 값으로 부른다.
            var label = info.Fields.FirstOrDefault(f => f.Type == typeof(string) &&
                (f.Name.Equals("name", StringComparison.OrdinalIgnoreCase) || f.Name.Equals("displayName", StringComparison.OrdinalIgnoreCase) ||
                 f.Name.Equals("id", StringComparison.OrdinalIgnoreCase)));
            var text = label?.Field.GetValue(value) as string;
            return string.IsNullOrEmpty(text) ? FriendlyName(value.GetType()) : $"{FriendlyName(value.GetType())} '{text}'";
        }

        public static string FriendlyName(Type type)
        {
            if (type.IsArray) return FriendlyName(type.GetElementType()) + "[]";
            if (!type.IsGenericType) return type.Name;
            var name = type.Name.Substring(0, type.Name.IndexOf('`'));
            return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(FriendlyName))}>";
        }

        static string KeyText(object key) => key is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : key?.ToString() ?? "";

        static string Join(string path, string name) => string.IsNullOrEmpty(path) ? name : path + "." + name;

        sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
