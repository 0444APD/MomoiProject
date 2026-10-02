using System;
using System.Collections.Generic;
using System.Reflection;

namespace GBible
{
    // 멤버 하나를 읽고 쓰는 도구. 생성 코드(__gb_get·__gb_set)가 있으면 그것을, 없으면(생성 대상이 아닌 형식) 리플렉션을 쓴다.
    public sealed class MemberAccess
    {
        public readonly Type Owner;
        public readonly string Name;
        // 생성 코드로 읽는다(리플렉션 없음).
        public readonly bool Generated;
        // readonly 필드·속성이면 false.
        public readonly bool CanWrite;
        readonly int _index;
        readonly MirrorGetFn _get;
        readonly MirrorSetFn _set;
        readonly FieldInfo _field;
        readonly PropertyInfo _property;

        internal MemberAccess(Type owner, string name, int index, MirrorGetFn get, MirrorSetFn set, bool canWrite)
        {
            Owner = owner;
            Name = name;
            _index = index;
            _get = get;
            _set = set;
            Generated = true;
            CanWrite = canWrite;
        }

        internal MemberAccess(Type owner, string name, FieldInfo field, PropertyInfo property)
        {
            Owner = owner;
            Name = name;
            _field = field;
            _property = property;
            CanWrite = field != null ? !field.IsInitOnly : property.CanWrite;
        }

        public object Get(object target) => Generated ? _get(target, _index) : _field != null ? _field.GetValue(target) : _property.GetValue(target);

        public void Set(object target, object value)
        {
            if (!CanWrite) return;
            if (Generated) _set(target, _index, value);
            else if (_field != null) _field.SetValue(target, value);
            else _property.SetValue(target, value);
        }
    }

    // 형식별 멤버 읽기·쓰기 목록. Mirror 생성 코드가 게임 시작 때 등록한다(Mirror.Initialize).
    // 필드 경로(조건·필터·플레이어 데이터에 주기), 이벤트 값 읽기(로그에 기록), 이벤트 기록 문장이 이것으로 읽는다.
    // 생성 대상: [Serializable]·[PlayerData]·[GameEvent] 형식. 그 밖의 형식은 리플렉션으로 읽고 한 번 경고한다.
    public static class FieldAccess
    {
        sealed class Table
        {
            public string[] Names;
            public byte[] Kinds;
            public MirrorGetFn Get;
            public MirrorSetFn Set;
        }

        static readonly object Gate = new object();
        static readonly Dictionary<Type, Table> Tables = new Dictionary<Type, Table>();
        // (형식, 이름) → 찾은 도구(없으면 null). 처음 찾을 때 한 번 만든다.
        static readonly Dictionary<(Type, string), MemberAccess> Cache = new Dictionary<(Type, string), MemberAccess>();
        static readonly Dictionary<Type, IReadOnlyList<string>> NameCache = new Dictionary<Type, IReadOnlyList<string>>();

        // 생성 코드가 부른다.
        internal static void Register(Type type, string[] names, byte[] kinds, MirrorGetFn get, MirrorSetFn set)
        {
            lock (Gate)
            {
                Tables[type] = new Table { Names = names, Kinds = kinds, Get = get, Set = set };
                Cache.Clear();
                NameCache.Clear();
            }
        }

        // 이 형식(부모 포함)에 생성된 읽기 코드가 있는지.
        public static bool IsGenerated(Type type)
        {
            if (!Mirror.IsInitialized) Mirror.Initialize();
            lock (Gate) return type != null && Tables.ContainsKey(type);
        }

        // 이름으로 멤버를 찾는다(부모 형식으로 올라가며). 없으면 null.
        public static MemberAccess Find(Type type, string name)
        {
            if (type == null || string.IsNullOrEmpty(name)) return null;
            if (!Mirror.IsInitialized) Mirror.Initialize();
            lock (Gate)
            {
                if (Cache.TryGetValue((type, name), out var cached)) return cached;
                var found = Make(type, name);
                Cache[(type, name)] = found;
                return found;
            }
        }

        public static object Read(object target, string name) => target == null ? null : Find(target.GetType(), name)?.Get(target);

        // 읽을 수 있는 멤버 이름(부모 먼저). 기록 문장 등에 쓴다.
        public static IReadOnlyList<string> NamesOf(Type type)
        {
            if (type == null) return Array.Empty<string>();
            if (!Mirror.IsInitialized) Mirror.Initialize();
            lock (Gate)
            {
                if (NameCache.TryGetValue(type, out var cached)) return cached;
                var chain = new List<Type>();
                for (var t = type; t != null && t != typeof(object); t = t.BaseType) chain.Insert(0, t);
                var names = new List<string>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var t in chain)
                {
                    if (Tables.TryGetValue(t, out var table))
                    {
                        foreach (var name in table.Names)
                            if (seen.Add(name)) names.Add(name);
                        continue;
                    }
                    Unwoven(t);
                    foreach (var field in t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                        if (seen.Add(field.Name)) names.Add(field.Name);
                    foreach (var property in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                        if (property.CanRead && property.GetIndexParameters().Length == 0 && seen.Add(property.Name)) names.Add(property.Name);
                }
                NameCache[type] = names;
                return names;
            }
        }

        static MemberAccess Make(Type type, string name)
        {
            for (var t = type; t != null && t != typeof(object) && t != typeof(ValueType); t = t.BaseType)
            {
                if (Tables.TryGetValue(t, out var table))
                {
                    var index = Array.IndexOf(table.Names, name);
                    if (index >= 0) return new MemberAccess(t, name, index, table.Get, table.Set, table.Kinds[index] == 0);
                    continue;
                }
                // 생성 코드가 없는 형식: 리플렉션(처음 한 번 찾고, 읽을 때마다 리플렉션으로 읽는다).
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                var field = t.GetField(name, flags);
                if (field != null && (field.IsPublic || HasSerializeAttribute(field)))
                {
                    Unwoven(t);
                    return new MemberAccess(t, name, field, null);
                }
                var property = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (property != null && property.CanRead && property.GetIndexParameters().Length == 0)
                {
                    Unwoven(t);
                    return new MemberAccess(t, name, null, property);
                }
            }
            return null;
        }

        static bool HasSerializeAttribute(FieldInfo field)
        {
            foreach (var attribute in field.GetCustomAttributes(false))
            {
                var attributeName = attribute.GetType().Name;
                if (attributeName == "SerializeField" || attributeName == "SerializeReference") return true;
            }
            return false;
        }

        static void Unwoven(Type type)
        {
            if (type.Assembly == typeof(object).Assembly) return;
            Mirror.Warn($"{DataModel.FriendlyName(type)}에 읽기 코드가 없어 리플렉션으로 읽습니다. 클래스에 [Serializable](이벤트면 [GameEvent])을 붙이세요.");
        }
    }
}
