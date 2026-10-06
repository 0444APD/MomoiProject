using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace GBible
{
    // Mirror: 플레이어 데이터의 사본을 하나 들고 있다가, Sync할 때 실제 값과 비교해 바뀐 곳만 ChangeSet으로 기록하고 사본을 맞춘다.
    //
    // 흐름
    //   1. 컴파일할 때(ILPostProcessor, GBible.CodeGen.MirrorWeaver) [Serializable] 클래스·구조체마다 비교·쓰기·읽기·적용 메서드가 생긴다.
    //      클래스 안에 생기므로 private 필드도 그대로 다룬다. 게임 코드는 평범한 C#(List·Dictionary·구조체)을 쓰면 된다.
    //   2. 게임이 시작할 때 Initialize가 그 메서드들을 한 번 모은다(리플렉션은 여기서만).
    //   3. Make(값)로 사본(MirrorNode 나무)을 만든다. 객체 사본은 원본(Origin)을 기억해 리스트에서 같은 객체를 따라간다.
    //   4. Sync(값, 사본, 기록)는 생성 코드로 필드를 비교한다(실행 중 리플렉션·할당 없음). 바뀐 곳마다 경로 + 새 값·옛 값을 쓴다.
    //   5. Apply는 앞으로 기록을 다른 쪽 값에 적용(동기화), Undo는 뒤로 기록을 거꾸로 적용(되돌리기)한다.
    //
    // 생성 코드가 없는 형식([Serializable]이 없는 클래스 등)은 Supports가 false라 저장소가 예전 비교 방식(DataModel)을 쓴다.
    public static class Mirror
    {
        static readonly object Gate = new object();
        static readonly Dictionary<Type, MirrorType> ByType = new Dictionary<Type, MirrorType>();
        static readonly List<MirrorType> ById = new List<MirrorType>();
        static bool _initialized;
        static bool _initializing;

        // 생성 코드가 없어 변경을 따라가지 못하는 객체를 만났을 때(한 형식에 한 번).
        public static event Action<string> Warning;
        static readonly HashSet<Type> Warned = new HashSet<Type>();

        public static bool IsInitialized => _initialized;

        // 생성 코드를 모은다. 여러 번 불러도 한 번만 한다. 게임 시작(첫 초기화)에서 부르면 실행 중에는 일이 없다.
        public static void Initialize()
        {
            if (_initialized) return;
            lock (Gate)
            {
                if (_initialized || _initializing) return;
                _initializing = true;
                try
                {
                    const string core = "GBible.Core";
                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        var name = assembly.GetName().Name;
                        if (name != core && !assembly.GetReferencedAssemblies().Any(r => r.Name == core)) continue;
                        Type[] types;
                        try { types = assembly.GetTypes(); }
                        catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                        foreach (var type in types)
                        {
                            if (type.IsGenericTypeDefinition) continue;
                            var register = type.GetMethod("__gb_register", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                            register?.Invoke(null, null);
                        }
                    }
                    // 형식 번호: 이름순(같은 코드로 빌드한 서버·클라이언트가 같은 번호를 쓴다).
                    ById.Clear();
                    ById.AddRange(ByType.Values.OrderBy(t => t.Type.FullName, StringComparer.Ordinal).ThenBy(t => t.Type.Assembly.GetName().Name, StringComparer.Ordinal));
                    for (var i = 0; i < ById.Count; i++) ById[i].Id = i;
                    _initialized = true;
                }
                finally
                {
                    _initializing = false;
                }
            }
        }

        internal static void Add(MirrorType type)
        {
            lock (Gate) ByType[type.Type] = type;
        }

        public static MirrorType Find(Type type)
        {
            if (!_initialized) Initialize();
            return type != null && ByType.TryGetValue(type, out var found) ? found : null;
        }

        internal static MirrorType FindById(int id) => id >= 0 && id < ById.Count ? ById[id] : null;

        // 형식 번호표: 번호 순서의 "전체 이름|어셈블리". 네트워크 접속 때 서로 보내 받는 쪽이 자기 번호로 바꾼다(MirrorReceiver.UseTypes) —
        // 에디터와 빌드처럼 어셈블리 구성이 달라 번호가 달라도 같은 형식으로 잇는다.
        public static string[] TypeTable()
        {
            if (!_initialized) Initialize();
            var names = new string[ById.Count];
            for (var i = 0; i < names.Length; i++) names[i] = TableName(ById[i].Type);
            return names;
        }

        static string TableName(Type type) => type.FullName + "|" + type.Assembly.GetName().Name;

        // 받은 번호를 이쪽 형식으로(받는 쪽이 번호표를 받았으면 그 표로 바꾼다).
        static MirrorType FindRead(int id)
        {
            var receiver = _receiver;
            if (receiver?.TypeMap == null) return FindById(id) ?? throw new InvalidOperationException("[MomoiProject] 모르는 형식 번호입니다(서로 다른 코드로 빌드됨).");
            if (id < 0 || id >= receiver.TypeMap.Length) throw new InvalidOperationException($"[MomoiProject] 상대가 보낸 형식 번호({id})가 번호표 밖입니다.");
            var local = receiver.TypeMap[id];
            if (local < 0) throw new InvalidOperationException($"[MomoiProject] 상대에게는 있고 이쪽에는 없는 형식입니다: {receiver.RemoteName(id)}. 서로 다른 버전의 게임입니다.");
            return ById[local];
        }

        // 번호표의 이름 → 이쪽 번호(없으면 -1). 같은 전체 이름이 어셈블리만 다르면 이름만으로도 찾는다.
        internal static int[] MapTable(string[] remote)
        {
            if (!_initialized) Initialize();
            var exact = new Dictionary<string, int>(StringComparer.Ordinal);
            var byName = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < ById.Count; i++)
            {
                exact[TableName(ById[i].Type)] = i;
                var name = ById[i].Type.FullName;
                if (name != null && !byName.ContainsKey(name)) byName[name] = i;
            }
            var map = new int[remote?.Length ?? 0];
            for (var i = 0; i < map.Length; i++)
            {
                var entry = remote[i] ?? "";
                if (exact.TryGetValue(entry, out var id)) map[i] = id;
                else
                {
                    var bar = entry.IndexOf('|');
                    map[i] = byName.TryGetValue(bar < 0 ? entry : entry.Substring(0, bar), out id) ? id : -1;
                }
            }
            return map;
        }

        // 이 형식을 뿌리로 쓸 수 있는지(모든 필드가 생성 코드로 다뤄지는지). 아니면 reason에 첫 이유.
        public static bool Supports(Type type, out string reason)
        {
            if (!_initialized) Initialize();
            reason = null;
            var root = Find(type);
            if (root == null)
            {
                reason = $"{DataModel.FriendlyName(type)}에 비교 코드가 없습니다([Serializable] 클래스가 아님)";
                return false;
            }
            return CheckType(root, new HashSet<Type>(), ref reason);
        }

        static bool CheckType(MirrorType type, HashSet<Type> seen, ref string reason)
        {
            if (!seen.Add(type.Type)) return true;
            for (var i = 0; i < type.FieldCount; i++)
            {
                var field = type.FieldTypes[i];
                bool ok;
                switch (type.Kinds[i])
                {
                    case MirrorKind.None:
                        ok = false;
                        break;
                    case MirrorKind.Struct:
                        ok = MirrorValues.IsSupported(field);
                        break;
                    case MirrorKind.Object:
                        ok = CheckReference(field, seen, ref reason);
                        break;
                    case MirrorKind.List:
                    case MirrorKind.Array:
                        ok = CheckElement(field.IsArray ? field.GetElementType() : field.GetGenericArguments()[0], seen, ref reason);
                        break;
                    case MirrorKind.Dictionary:
                        var args = field.GetGenericArguments();
                        ok = IsKeyType(args[0]) && CheckElement(args[1], seen, ref reason);
                        break;
                    default:
                        ok = true;
                        break;
                }
                if (ok) continue;
                reason ??= $"{DataModel.FriendlyName(type.Type)}.{type.Names[i]}({DataModel.FriendlyName(field ?? typeof(object))})는 비교 코드로 다룰 수 없는 형식입니다";
                return false;
            }
            return true;
        }

        static bool IsKeyType(Type type) => type == typeof(string) || type.IsEnum || (type.IsPrimitive && type != typeof(float) && type != typeof(double));

        static bool IsCollection(Type type) =>
            type.IsArray || (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(List<>) || type.GetGenericTypeDefinition() == typeof(Dictionary<,>)));

        static bool CheckElement(Type type, HashSet<Type> seen, ref string reason)
        {
            if (type.IsPrimitive || type.IsEnum || type == typeof(string)) return true;
            if (IsCollection(type)) return false;
            if (type.IsValueType) return MirrorValues.IsSupported(type);
            return CheckReference(type, seen, ref reason);
        }

        static bool CheckReference(Type type, HashSet<Type> seen, ref string reason)
        {
            if (type == null) return false;
            if (DataModel.IsReferenceType(type)) return true;               // 에셋 참조: 참조 그대로 비교
            if (type == typeof(object) || type.IsAbstract || type.IsInterface) return true; // 실제 형식으로 그때그때
            if (IsCollection(type)) return false;
            var found = Find(type);
            if (found == null)
            {
                reason = $"{DataModel.FriendlyName(type)}에 비교 코드가 없습니다([Serializable]을 붙이세요)";
                return false;
            }
            return CheckType(found, seen, ref reason);
        }

        // ── 사본 ───────────────────────────────────────────────

        // ── 사본 나무 ───────────────────────────────────────────
        // 사본 나무 하나(플레이어 데이터 하나)에서 객체 하나는 사본도 하나다: 두 곳에서 가리키면 두 곳이 같은 사본을 가리킨다.
        //   · Sync는 한 회차에 사본 하나를 한 번만 비교한다(같은 변경을 두 번 기록하지 않고, 서로 가리키는 객체도 끝난다).
        //   · 사본마다 객체 번호(NetId)가 있어, 받는 쪽(MirrorReceiver)이 같은 객체를 같은 인스턴스로 잇는다.
        //   · 가리키는 곳 수(Refs)가 0이 되면 나무에서 뺀다.

        [ThreadStatic] static MirrorTree _tree;
        [ThreadStatic] static bool _building;
        static long _nextNetId;

        internal static long NextNetId() => System.Threading.Interlocked.Increment(ref _nextNetId);

        // 뿌리 값(클래스 객체)의 사본을 만든다. 처음 나무의 객체는 받는 쪽이 처음 모습(Snapshot)으로 이미 가진 것으로 본다.
        public static MirrorNode Make(object live)
        {
            if (live == null) throw new ArgumentNullException(nameof(live));
            var type = Find(live.GetType()) ?? throw new InvalidOperationException($"[MomoiProject] {live.GetType().Name}에 비교 코드가 없습니다.");
            var tree = new MirrorTree();
            var node = new MirrorNode(type, live) { NetId = NextNetId(), Refs = 1, Sent = true, Tree = tree };
            tree.Nodes[live] = node;
            var previousTree = _tree;
            var previousBuilding = _building;
            _tree = tree;
            _building = true;
            try
            {
                node.Pass = tree.Pass;
                type.Sync(live, node, null);
            }
            finally
            {
                _tree = previousTree;
                _building = previousBuilding;
            }
            return node;
        }

        // 실제 값과 사본을 비교해 바뀐 곳을 changes에 쓰고 사본을 맞춘다(changes가 null이면 기록 없이 맞추기만).
        public static void Sync(object live, MirrorNode root, ChangeSet changes)
        {
            var tree = root.Tree;
            if (tree == null)
            {
                tree = root.Tree = new MirrorTree();
                if (root.Origin != null) tree.Nodes[root.Origin] = root;
                root.Refs = 1;
            }
            if (!ReferenceEquals(root.Origin, live))
            {
                if (root.Origin != null && tree.Nodes.TryGetValue(root.Origin, out var was) && was == root) tree.Nodes.Remove(root.Origin);
                root.Origin = live;
                if (live != null) tree.Nodes[live] = root;
            }
            var previousTree = _tree;
            var previousBuilding = _building;
            _tree = tree;
            _building = false;
            try
            {
                tree.Pass++;
                root.Pass = tree.Pass;
                root.Type.Sync(live, root, changes);
            }
            finally
            {
                _tree = previousTree;
                _building = previousBuilding;
            }
        }

        // 객체 하나(또는 null·에셋 참조)의 사본. 생성 코드가 없는 객체는 참조 그대로(바뀐 내용은 따라가지 못한다).
        // 같은 나무에 이미 사본이 있으면 그 사본을 함께 가리킨다(가리키는 곳 수 +1). 이번 회차에 아직 비교하지 않은
        // 사본일 수 있으니 부른 쪽이 Enter로 확인해 비교한다.
        internal static object MakeAny(object live)
        {
            if (live == null) return null;
            var tree = _tree;
            if (tree != null && tree.Nodes.TryGetValue(live, out var existing))
            {
                existing.Refs++;
                return existing;
            }
            var type = Find(live.GetType());
            if (type == null)
            {
                WarnOpaque(live.GetType());
                return live;
            }
            var node = new MirrorNode(type, live) { NetId = NextNetId(), Refs = 1, Sent = _building || tree == null, Tree = tree };
            if (tree != null)
            {
                tree.Nodes[live] = node;
                node.Pass = tree.Pass;
            }
            type.Sync(live, node, null);
            return node;
        }

        // 이번 회차에 이 사본을 처음 비교하는지(처음이면 표시하고 true). 나무 밖(Sync 중이 아님)이면 언제나 true.
        internal static bool Enter(MirrorNode node)
        {
            var tree = node.Tree;
            if (tree == null) return true;
            if (node.Pass == tree.Pass) return false;
            node.Pass = tree.Pass;
            return true;
        }

        // 한 곳이 사본을 더 가리키지 않는다(가리키는 곳이 없어지면 나무에서 빼고 안쪽도 같이).
        internal static void Release(object shadow)
        {
            if (shadow == null) return;
            if (shadow is MirrorNode node)
            {
                var tree = node.Tree;
                if (tree == null) return;
                if (--node.Refs > 0) return;
                if (node.Origin != null && tree.Nodes.TryGetValue(node.Origin, out var found) && found == node) tree.Nodes.Remove(node.Origin);
                for (var i = 0; i < node.Type.FieldCount; i++)
                {
                    var child = node.R[i];
                    if (child is MirrorNode || child is IMirrorShadow) Release(child);
                }
                return;
            }
            if (shadow is IMirrorShadow collection)
            {
                var nodes = new List<MirrorNode>();
                collection.AddNodes(nodes);
                foreach (var item in nodes) Release(item);
            }
        }

        static void WarnOpaque(Type type)
        {
            if (DataModel.IsReferenceType(type)) return;
            lock (Warned)
                if (!Warned.Add(type)) return;
            Warning?.Invoke($"{DataModel.FriendlyName(type)}에 비교 코드가 없어 안쪽 변경을 따라가지 못합니다. 클래스에 [Serializable]을 붙이세요.");
        }

        // 같은 문장은 한 번만 알린다(생성 코드가 없는 형식을 실행 중에 만났을 때 등).
        internal static void Warn(string message)
        {
            lock (Warned)
                if (!WarnedText.Add(message)) return;
            Warning?.Invoke(message);
        }

        static readonly HashSet<string> WarnedText = new HashSet<string>();

        internal static object OriginOf(object shadow) => shadow is MirrorNode node ? node.Origin : shadow;

        // ── 쓰기·읽기 ───────────────────────────────────────────
        // 객체 표시: 0 = null, 1 = 생성 코드가 있는 객체(형식 번호 + 필드), 2 = 에셋 참조(ID 글자),
        //           3 = 원래 객체(뒤로 기록 전용: 원래 객체 번호 + 형식 번호 + 필드 — 되돌릴 때 그 인스턴스에 다시 채운다),
        //           4 = 같은 프로세스 안의 참조(번호 — 복사·되돌리기에서 스프라이트 같은 에셋 참조를 그대로 잇는다. 네트워크로는 보내지 않는다),
        //           5 = 받는 쪽이 이미 가진 객체(객체 번호), 6 = 객체 번호가 붙은 새 객체(객체 번호 + 형식 번호 + 필드) — 네트워크(앞으로 기록·처음 모습),
        //           7 = 같은 값 안에서 앞서 쓴 객체(번호) — 한 값 안에서 두 곳이 같은 객체를 가리키면 받는 쪽도 같은 인스턴스.
        // 값 하나(가장 바깥 Write·WriteShadow 한 번)가 한 범위다: 1·3으로 쓴 객체에 차례로 번호를 매기고, 다시 나오면 7로 가리킨다.
        [ThreadStatic] static List<object> _writeRefs;
        [ThreadStatic] static int _writeDepth;
        [ThreadStatic] static Dictionary<object, int> _written;
        [ThreadStatic] static int _readDepth;
        [ThreadStatic] static List<object> _read;
        // 네트워크로 쓰는 중(사본 → 5·6). _snapshot이 있으면 처음 모습(한 번 쓴 사본은 5로), 없으면 앞으로 기록(Sent로 판단).
        [ThreadStatic] static bool _net;
        [ThreadStatic] static HashSet<MirrorNode> _snapshot;
        // 받는 쪽(5·6을 읽을 때 객체 번호 → 인스턴스).
        [ThreadStatic] static MirrorReceiver _receiver;
        // 살아 있는 값(이벤트·행동)을 네트워크로 쓰는 중: 받는 쪽이 이미 가진 데이터 안의 객체는 번호(5)로 가리킨다.
        [ThreadStatic] static Func<object, MirrorNode> _liveNodes;

        // 살아 있는 값을 네트워크로 쓴다. nodeOf가 데이터 안의 객체 사본을 찾아 주면(받는 쪽에 이미 보낸 것) 내용 대신 번호만 쓴다.
        // 받는 쪽은 MirrorReceiver.Load로 읽는다(데이터 밖 객체·처음 보는 객체는 내용째 새로 만든다).
        public static void WriteNetValue(MirrorWriter writer, object value, Func<object, MirrorNode> nodeOf)
        {
            var previous = _liveNodes;
            _liveNodes = nodeOf;
            try { Write(writer, value); }
            finally { _liveNodes = previous; }
        }

        // 이 범위에서 처음 쓰는 객체면 번호를 매기고 true, 이미 썼으면 7 + 번호를 쓰고 false.
        static bool First(MirrorWriter writer, object value)
        {
            var written = _written ??= new Dictionary<object, int>(RefEq.Instance);
            if (written.TryGetValue(value, out var index))
            {
                writer.Byte(7);
                writer.UVar((ulong)index);
                return false;
            }
            written.Add(value, written.Count);
            return true;
        }

        static void OpenScope(bool write)
        {
            if (write)
            {
                if (_writeDepth++ == 0) _written?.Clear();
            }
            else if (_readDepth++ == 0) _read?.Clear();
        }

        static void CloseScope(bool write)
        {
            if (write) _writeDepth--;
            else _readDepth--;
        }

        static void Remember(object value) => (_read ??= new List<object>()).Add(value);

        public static void Write(MirrorWriter writer, object value)
        {
            if (value == null)
            {
                writer.Byte(0);
                return;
            }
            if (_liveNodes != null && _liveNodes(value) is MirrorNode known && known.Sent)
            {
                writer.Byte(5);
                writer.UVar((ulong)known.NetId);
                return;
            }
            var type = Find(value.GetType());
            if (type != null)
            {
                OpenScope(true);
                try
                {
                    if (!First(writer, value)) return;
                    writer.Byte(1);
                    writer.UVar((ulong)type.Id);
                    type.Write(writer, value);
                }
                finally
                {
                    CloseScope(true);
                }
                return;
            }
            if (_writeRefs != null)
            {
                writer.Byte(4);
                writer.UVar((ulong)_writeRefs.Count);
                _writeRefs.Add(value);
                return;
            }
            writer.Byte(2);
            writer.Str(DataModel.ReferenceToId?.Invoke(value));
        }

        public static object Read(MirrorReader reader, Type declared)
        {
            OpenScope(false);
            try
            {
                switch (reader.Byte())
                {
                    case 0:
                        return null;
                    case 1:
                    {
                        var type = FindRead((int)reader.UVar());
                        var value = type.New();
                        Remember(value);
                        type.Read(value, reader);
                        return value;
                    }
                    case 3:
                    {
                        var original = (int)reader.UVar();
                        var kind = FindRead((int)reader.UVar());
                        var reused = _undoOriginals != null && original + _undoOffset < _undoOriginals.Count ? _undoOriginals[original + _undoOffset] : null;
                        if (reused == null || reused.GetType() != kind.Type) reused = kind.New();
                        Remember(reused);
                        kind.Read(reused, reader);
                        return reused;
                    }
                    case 4:
                    {
                        var index = (int)reader.UVar() + _undoOffset;
                        return _undoOriginals != null && index < _undoOriginals.Count ? _undoOriginals[index] : null;
                    }
                    case 5:
                    {
                        var id = (long)reader.UVar();
                        var found = _receiver?.Find(id);
                        if (found != null || (_receiver != null && _receiver.MissingAsNull)) return found;
                        throw new InvalidOperationException($"[MomoiProject] 받는 쪽에 없는 객체 번호({id})입니다. 처음 모습(Mirror.Snapshot)부터 MirrorReceiver로 받으세요.");
                    }
                    case 6:
                    {
                        var id = (long)reader.UVar();
                        var kind = FindRead((int)reader.UVar());
                        var value = _receiver?.Find(id);
                        if (value == null || value.GetType() != kind.Type) value = kind.New();
                        _receiver?.Register(id, value);
                        kind.Read(value, reader);
                        return value;
                    }
                    case 7:
                    {
                        var index = (int)reader.UVar();
                        return _read != null && index < _read.Count ? _read[index] : throw new InvalidOperationException("[MomoiProject] 변경 기록의 객체 번호가 맞지 않습니다.");
                    }
                    default:
                    {
                        var id = reader.Str();
                        return id == null ? null : DataModel.IdToReference?.Invoke(id, declared ?? typeof(object));
                    }
                }
            }
            finally
            {
                CloseScope(false);
            }
        }

        // 사본에서 쓴다(살아 있는 값을 쓴 것과 같은 내용). originals가 있으면 원래 객체를 그 목록에 남기고 번호로 가리킨다.
        internal static void WriteShadow(MirrorWriter writer, object shadow, List<object> originals = null)
        {
            if (shadow == null)
            {
                writer.Byte(0);
                return;
            }
            if (shadow is MirrorNode node)
            {
                if (_net)
                {
                    WriteNet(writer, node);
                    return;
                }
                OpenScope(true);
                try
                {
                    if (!First(writer, node)) return;
                    if (originals != null && node.Origin != null)
                    {
                        writer.Byte(3);
                        writer.UVar((ulong)originals.Count);
                        originals.Add(node.Origin);
                    }
                    else writer.Byte(1);
                    writer.UVar((ulong)node.Type.Id);
                    WriteNode(writer, node, originals);
                }
                finally
                {
                    CloseScope(true);
                }
                return;
            }
            if (originals != null)
            {
                writer.Byte(4);
                writer.UVar((ulong)originals.Count);
                originals.Add(shadow);
                return;
            }
            writer.Byte(2);
            writer.Str(DataModel.ReferenceToId?.Invoke(shadow));
        }

        // 네트워크용: 받는 쪽이 가진 객체면 번호만(5), 아니면 번호 + 내용(6).
        static void WriteNet(MirrorWriter writer, MirrorNode node)
        {
            OpenScope(true);
            try
            {
                var known = _snapshot != null ? !_snapshot.Add(node) : node.Sent;
                if (known)
                {
                    writer.Byte(5);
                    writer.UVar((ulong)node.NetId);
                    return;
                }
                if (_snapshot == null) node.Sent = true;
                writer.Byte(6);
                writer.UVar((ulong)node.NetId);
                writer.UVar((ulong)node.Type.Id);
                WriteNode(writer, node, null);
            }
            finally
            {
                CloseScope(true);
            }
        }

        // 앞으로 기록에 사본(객체·null·에셋)을 네트워크 형식으로 쓴다.
        internal static void WriteForward(MirrorWriter writer, object shadow)
        {
            var previous = _net;
            _net = true;
            try { WriteShadow(writer, shadow); }
            finally { _net = previous; }
        }

        // 앞으로 기록에 컬렉션 사본을 네트워크 형식으로 쓴다(null이면 0).
        internal static void WriteForwardCollection(MirrorWriter writer, IMirrorShadow shadow)
        {
            if (shadow == null)
            {
                writer.UVar(0);
                return;
            }
            var previous = _net;
            _net = true;
            try { shadow.WriteShadow(writer, null); }
            finally { _net = previous; }
        }

        // 처음 모습: 받는 쪽이 처음 받을 뿌리 전체(객체 번호 포함). 받는 쪽은 MirrorReceiver.Load로 읽는다.
        // 사본이 지금 값과 같아야 하니 Sync한 뒤에 부른다(PlayerDataStore.Snapshot이 그렇게 한다).
        public static byte[] Snapshot(MirrorNode root)
        {
            var writer = new MirrorWriter();
            var previousNet = _net;
            var previousSnapshot = _snapshot;
            _net = true;
            _snapshot = new HashSet<MirrorNode>();
            try { WriteNet(writer, root); }
            finally
            {
                _net = previousNet;
                _snapshot = previousSnapshot;
            }
            return writer.ToArray();
        }

        internal static object ReadWith(MirrorReceiver receiver, MirrorReader reader, Type declared)
        {
            var previous = _receiver;
            _receiver = receiver;
            try { return Read(reader, declared); }
            finally { _receiver = previous; }
        }

        internal static void ApplyWith(MirrorReceiver receiver, object root, byte[] data, int start, int end)
        {
            var previous = _receiver;
            _receiver = receiver;
            try { Apply(root, data, start, end); }
            finally { _receiver = previous; }
        }

        internal static void WriteNode(MirrorWriter writer, MirrorNode node, List<object> originals = null)
        {
            var type = node.Type;
            for (var i = 0; i < type.FieldCount; i++)
            {
                var kind = type.Kinds[i];
                switch (kind)
                {
                    case MirrorKind.None:
                        break;
                    case MirrorKind.String:
                        writer.Str((string)node.R[i]);
                        break;
                    case MirrorKind.Struct:
                        MirrorValues.WriteBoxed(type.FieldTypes[i], writer, node.R[i]);
                        break;
                    case MirrorKind.Object:
                        WriteShadow(writer, node.R[i], originals);
                        break;
                    case MirrorKind.List:
                    case MirrorKind.Array:
                    case MirrorKind.Dictionary:
                        if (node.R[i] is IMirrorShadow shadow) shadow.WriteShadow(writer, originals);
                        else writer.UVar(0);
                        break;
                    default:
                        MirrorCode.WriteBits(writer, node.L[i], (int)kind);
                        break;
                }
            }
        }

        // 사본에서 객체를 다시 만든다(사람이 읽는 기록용).
        internal static object Rebuild(object shadow, Type declared)
        {
            if (shadow == null || !(shadow is MirrorNode)) return shadow;
            var w = new MirrorWriter();
            WriteShadow(w, shadow);
            return Read(new MirrorReader(w.Data, 0, w.Length), declared);
        }

        // 생성 코드로 깊은 복사(쓰고 다시 읽기). 인스턴스의 사본을 만들 때 쓴다. 에셋 참조(스프라이트 등)는 같은 참조 그대로.
        public static T Clone<T>(T value) where T : class
        {
            if (value == null) return null;
            var refs = new List<object>();
            var previousWrite = _writeRefs;
            var previousRead = _undoOriginals;
            var previousOffset = _undoOffset;
            try
            {
                _writeRefs = refs;
                var w = new MirrorWriter();
                Write(w, value);
                _writeRefs = previousWrite;
                _undoOriginals = refs;
                _undoOffset = 0;
                return (T)Read(new MirrorReader(w.Data, 0, w.Length), typeof(T));
            }
            finally
            {
                _writeRefs = previousWrite;
                _undoOriginals = previousRead;
                _undoOffset = previousOffset;
            }
        }

        // ── 찾기 ───────────────────────────────────────────────
        // 사본 나무를 따라가며 객체를 찾는다(리플렉션 없음). 사본은 마지막 Sync 때의 모습이라, 부르기 전에 Sync한다.

        static int _walk;

        // root 안의 T를 들어 있는 순서로(필드 순서 → 리스트 순서) into에 더한다. 두 곳에서 가리키는 객체는 한 번만.
        public static void Collect<T>(MirrorNode root, List<T> into) where T : class
        {
            var walk = System.Threading.Interlocked.Increment(ref _walk);
            var stack = new List<MirrorNode> { root };
            while (stack.Count > 0)
            {
                var node = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);
                if (node.Walk == walk) continue;
                node.Walk = walk;
                if (node.Origin is T found) into.Add(found);
                var start = stack.Count;
                PushChildren(node, stack);
                stack.Reverse(start, stack.Count - start);
            }
        }

        // root 안에서 id가 같은 T 하나.
        public static T FindById<T>(MirrorNode root, string id) where T : class => (T)FindById(root, typeof(T), id);

        public static object FindById(MirrorNode root, Type type, string id)
        {
            var walk = System.Threading.Interlocked.Increment(ref _walk);
            var stack = new List<MirrorNode> { root };
            while (stack.Count > 0)
            {
                var node = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);
                if (node.Walk == walk) continue;
                node.Walk = walk;
                if (node.Origin is { } found && type.IsInstanceOfType(found))
                {
                    // id 필드(사본의 값), 없으면 id 속성(생성 코드로 읽기).
                    var field = node.Type.FindIdField();
                    var nodeId = field >= 0 ? (string)node.R[field] : (FieldAccess.Read(found, "id") ?? FieldAccess.Read(found, "Id")) as string;
                    if (string.Equals(nodeId, id)) return found;
                }
                var start = stack.Count;
                PushChildren(node, stack);
                stack.Reverse(start, stack.Count - start);
            }
            return null;
        }

        static void PushChildren(MirrorNode node, List<MirrorNode> stack)
        {
            for (var i = 0; i < node.Type.FieldCount; i++)
            {
                switch (node.R[i])
                {
                    case MirrorNode child:
                        stack.Add(child);
                        break;
                    case IMirrorShadow collection:
                        collection.AddNodes(stack);
                        break;
                }
            }
        }

        // ── 적용 ───────────────────────────────────────────────

        // 앞으로 기록(Forward)을 다른 쪽 값에 적용한다(받는 쪽 동기화).
        public static void Apply(object root, ChangeSet changes) => Apply(root, changes.Forward.Data, 0, changes.Forward.Length);

        public static void Apply(object root, byte[] data, int start, int end)
        {
            var reader = new MirrorReader(data, start, end);
            while (!reader.AtEnd) ApplyOne(root, reader);
        }

        // 뒤로 기록(Backward)을 거꾸로 적용해 Sync 전으로 되돌린다. 사본은 그대로라 이어서 Sync하면 되돌린 내용이 기록된다.
        // 빠지거나 갈아 끼워졌던 객체는 원래 인스턴스로 돌아온다(값은 기록대로 다시 채운다).
        public static void Undo(object root, ChangeSet changes)
        {
            var previous = _undoOriginals;
            _undoOriginals = changes.Originals;
            try
            {
                for (var i = changes.BackwardStarts.Count - 1; i >= 0; i--)
                {
                    var start = changes.BackwardStarts[i];
                    _undoOffset = changes.OriginalsOffset(start);
                    ApplyOne(root, new MirrorReader(changes.Backward.Data, start, changes.Backward.Length));
                }
            }
            finally
            {
                _undoOriginals = previous;
                _undoOffset = 0;
            }
        }

        [ThreadStatic] static List<object> _undoOriginals;
        [ThreadStatic] static int _undoOffset;

        static void ApplyOne(object root, MirrorReader reader)
        {
            var op = (MirrorOp)reader.Byte();
            var depth = (int)reader.UVar();
            ApplyInto(root, reader, depth, op);
        }

        // 경로의 다음 칸(필드)을 읽고 그 객체의 적용 코드로 넘긴다. depth = 남은 칸 수(이 필드 칸 포함).
        internal static void ApplyInto(object target, MirrorReader reader, int depth, MirrorOp op)
        {
            if (target == null) throw new InvalidOperationException("[MomoiProject] 변경 기록의 경로가 비어 있는 객체를 지납니다.");
            reader.Byte();
            var field = (int)reader.UVar();
            var type = Find(target.GetType()) ?? throw new InvalidOperationException($"[MomoiProject] {target.GetType().Name}에 비교 코드가 없습니다.");
            type.Apply(target, field, depth, reader, op);
        }
    }

    public enum MirrorKind : byte
    {
        None = 0,   // 다루지 않는 필드(중첩 컬렉션 등) — 이 형식은 Supports가 false
        Bool = 1,
        SByte = 2,
        Byte = 3,
        Int16 = 4,
        UInt16 = 5,
        Int32 = 6,
        UInt32 = 7,
        Int64 = 8,
        UInt64 = 9,
        Single = 10,
        Double = 11,
        Char = 12,
        String = 20,
        Struct = 30,
        Object = 40,
        List = 50,
        Array = 51,
        Dictionary = 60,
    }

    // 생성 코드가 있는 형식 하나. 필드 번호는 부모 클래스의 필드가 먼저.
    public sealed class MirrorType
    {
        public Type Type;
        public int Id;
        public string[] Names;
        public MirrorKind[] Kinds;
        public Type[] FieldTypes;
        internal Action<object, MirrorNode, ChangeSet> Sync;
        internal Action<MirrorWriter, object> Write;
        internal Action<object, MirrorReader> Read;
        internal Func<object> New;
        internal Action<object, int, int, MirrorReader, MirrorOp> Apply;

        public int FieldCount => Names.Length;

        // string id 필드(이름 id 또는 Id)의 번호. 없으면 -1. Game.Find가 리플렉션 없이 id를 읽는다(사본의 값).
        public int IdField = -2;

        internal int FindIdField()
        {
            if (IdField != -2) return IdField;
            IdField = -1;
            for (var i = 0; i < FieldCount; i++)
                if (Kinds[i] == MirrorKind.String && (Names[i] == "id" || Names[i] == "Id"))
                {
                    IdField = i;
                    break;
                }
            return IdField;
        }
    }

    // 객체 하나의 사본: 숫자·참거짓·enum은 L(비트), 글자·구조체·자식 사본·컬렉션 사본은 R.
    public sealed class MirrorNode
    {
        public readonly MirrorType Type;
        public object Origin;
        public readonly long[] L;
        public readonly object[] R;
        // 객체 번호(보내는 쪽에서 매김). 받는 쪽이 같은 객체를 같은 인스턴스로 잇는다.
        public long NetId { get; internal set; }
        // 이 나무에서 이 사본을 가리키는 곳 수.
        internal int Refs;
        // 마지막으로 비교한 Sync 회차·찾기 회차.
        internal int Pass;
        internal int Walk;
        // 받는 쪽이 이미 가진 객체인지(처음 나무에 있었거나 앞으로 기록으로 통째로 보냈음).
        internal bool Sent;
        // 이 사본이 속한 나무(객체 → 사본, Sync 회차).
        internal MirrorTree Tree;

        public MirrorNode(MirrorType type, object origin)
        {
            Type = type;
            Origin = origin;
            L = new long[type.FieldCount];
            R = new object[type.FieldCount];
        }
    }

    // 사본 나무 하나의 객체 → 사본(객체 하나에 사본 하나).
    sealed class MirrorTree
    {
        public readonly Dictionary<object, MirrorNode> Nodes = new Dictionary<object, MirrorNode>(RefEq.Instance);
        public int Pass;
    }

    // 받는 쪽: 보내는 쪽의 객체 번호 → 이쪽 인스턴스. 같은 객체를 두 곳에서 가리키면 이쪽도 같은 인스턴스를 가리킨다.
    //   var receiver = new MirrorReceiver();
    //   var party = (Party)receiver.Load(snapshot);       // 처음 모습(Mirror.Snapshot·PlayerDataStore.Snapshot)
    //   receiver.Apply(party, changes);                    // 이후 앞으로 기록(PlayerDataStore.Synced)마다
    // 인스턴스는 약하게 붙잡는다(데이터에서 빠져 버려진 객체는 사라져도 된다: 보내는 쪽은 나무에 있는 객체만 번호로 가리킨다).
    public sealed class MirrorReceiver
    {
        readonly Dictionary<long, WeakReference> _objects = new Dictionary<long, WeakReference>();
        int _sincePrune;
        string[] _remoteNames;

        public int Count => _objects.Count;

        // 보내는 쪽 형식 번호 → 이쪽 번호(UseTypes로 받은 번호표). null이면 같은 번호로 본다(같은 프로세스).
        internal int[] TypeMap { get; private set; }

        // 보내는 쪽에 있고 이쪽에 없는 객체 번호를 만나면 예외 대신 null(이벤트처럼 받는 쪽이 못 본 데이터를 가리킬 수 있는 값).
        public bool MissingAsNull;

        // 보내는 쪽의 형식 번호표(Mirror.TypeTable)를 받는다. 이후 읽기는 그 번호를 이쪽 형식으로 바꿔 읽는다.
        // 돌려주는 값: 상대에게 있고 이쪽에 없는 형식 이름(버전이 다를 때). 실제로 그 형식이 오면 그때 예외가 난다.
        public List<string> UseTypes(string[] remoteTable)
        {
            _remoteNames = remoteTable ?? Array.Empty<string>();
            TypeMap = Mirror.MapTable(_remoteNames);
            var missing = new List<string>();
            for (var i = 0; i < TypeMap.Length; i++)
                if (TypeMap[i] < 0) missing.Add(_remoteNames[i]);
            return missing;
        }

        internal string RemoteName(int id) => _remoteNames != null && id >= 0 && id < _remoteNames.Length ? _remoteNames[id] : "#" + id;

        public object Load(byte[] snapshot) => Load(snapshot, 0, snapshot.Length);

        public object Load(byte[] data, int start, int end) => Mirror.ReadWith(this, new MirrorReader(data, start, end), typeof(object));

        public void Apply(object root, ChangeSet changes) => Apply(root, changes.Forward.Data, 0, changes.Forward.Length);

        public void Apply(object root, byte[] data, int start, int end) => Mirror.ApplyWith(this, root, data, start, end);

        public object Find(long id) => _objects.TryGetValue(id, out var weak) ? weak.Target : null;

        internal void Register(long id, object value)
        {
            if (_objects.TryGetValue(id, out var weak))
            {
                weak.Target = value;
                return;
            }
            _objects[id] = new WeakReference(value);
            if (++_sincePrune < 1024) return;
            _sincePrune = 0;
            var dead = new List<long>();
            foreach (var pair in _objects)
                if (!pair.Value.IsAlive) dead.Add(pair.Key);
            foreach (var id2 in dead) _objects.Remove(id2);
        }
    }

    interface IMirrorShadow
    {
        void WriteShadow(MirrorWriter writer, List<object> originals);
        // 안에 든 객체 사본(들어 있는 순서).
        void AddNodes(List<MirrorNode> into);
    }

    // 요소가 값(숫자·글자·enum·구조체)인지 객체인지.
    static class Elem<T>
    {
        public static readonly bool IsValue = typeof(T).IsValueType || typeof(T) == typeof(string);
    }

    // 값 형식 하나의 비교·쓰기·읽기. 숫자·글자·enum·DateTime 등은 기본 제공, [Serializable] 구조체는 생성 코드가 등록,
    // Unity 구조체(Vector3 …)는 Unity 층이 등록한다.
    public static class ValueOps<T>
    {
        public static MirrorEqualFn<T> Equal;
        public static MirrorWriteFn<T> Write;
        public static MirrorValueReadFn<T> Read;

        static ValueOps() => MirrorValues.Builtin<T>();

        public static bool Supported => Equal != null && Write != null && Read != null;

        public static void Register(MirrorEqualFn<T> equal, MirrorWriteFn<T> write, MirrorValueReadFn<T> read)
        {
            Equal = equal;
            Write = write;
            Read = read;
            MirrorValues.AddBoxed(typeof(T), (w, o) => write(w, o == null ? default : (T)o), r => read(r));
        }
    }

    // 딕셔너리 키를 경로에 쓸 때(상자에 담긴 키).
    static class KeyOps<T>
    {
        public static readonly Action<MirrorWriter, object> WriteBoxed = (w, o) => ValueOps<T>.Write(w, (T)o);
    }

    static class MirrorValues
    {
        static readonly Dictionary<Type, (Action<MirrorWriter, object> write, Func<MirrorReader, object> read)> Boxed =
            new Dictionary<Type, (Action<MirrorWriter, object>, Func<MirrorReader, object>)>();

        static readonly HashSet<Type> BuiltinTypes = new HashSet<Type>
        {
            typeof(DateTime), typeof(TimeSpan), typeof(Guid), typeof(DateTimeOffset), typeof(decimal),
        };

        internal static void AddBoxed(Type type, Action<MirrorWriter, object> write, Func<MirrorReader, object> read)
        {
            lock (Boxed) Boxed[type] = (write, read);
        }

        public static bool IsSupported(Type type)
        {
            if (type.IsPrimitive || type.IsEnum || type == typeof(string) || BuiltinTypes.Contains(type)) return true;
            lock (Boxed) return Boxed.ContainsKey(type);
        }

        internal static void WriteBoxed(Type type, MirrorWriter writer, object value)
        {
            (Action<MirrorWriter, object> write, Func<MirrorReader, object> read) ops;
            lock (Boxed)
                if (!Boxed.TryGetValue(type, out ops)) throw new InvalidOperationException($"[MomoiProject] {type.Name} 값을 쓸 방법이 없습니다.");
            ops.write(writer, value);
        }

        static void Set<TValue, T>(MirrorEqualFn<TValue> equal, MirrorWriteFn<TValue> write, MirrorValueReadFn<TValue> read)
        {
            ValueOps<T>.Equal = (MirrorEqualFn<T>)(object)equal;
            ValueOps<T>.Write = (MirrorWriteFn<T>)(object)write;
            ValueOps<T>.Read = (MirrorValueReadFn<T>)(object)read;
            AddBoxed(typeof(T), (w, o) => write(w, o == null ? default : (TValue)o), r => read(r));
        }

        internal static void Builtin<T>()
        {
            var t = typeof(T);
            if (t == typeof(int)) Set<int, T>((a, b) => a == b, (w, v) => w.Long(v), r => (int)r.Long());
            else if (t == typeof(string)) Set<string, T>(string.Equals, (w, v) => w.Str(v), r => r.Str());
            else if (t == typeof(float)) Set<float, T>((a, b) => a.Equals(b), (w, v) => w.Float(v), r => r.Float());
            else if (t == typeof(bool)) Set<bool, T>((a, b) => a == b, (w, v) => w.Bool(v), r => r.Bool());
            else if (t == typeof(long)) Set<long, T>((a, b) => a == b, (w, v) => w.Long(v), r => r.Long());
            else if (t == typeof(double)) Set<double, T>((a, b) => a.Equals(b), (w, v) => w.Double(v), r => r.Double());
            else if (t == typeof(byte)) Set<byte, T>((a, b) => a == b, (w, v) => w.UVar(v), r => (byte)r.UVar());
            else if (t == typeof(sbyte)) Set<sbyte, T>((a, b) => a == b, (w, v) => w.Long(v), r => (sbyte)r.Long());
            else if (t == typeof(short)) Set<short, T>((a, b) => a == b, (w, v) => w.Long(v), r => (short)r.Long());
            else if (t == typeof(ushort)) Set<ushort, T>((a, b) => a == b, (w, v) => w.UVar(v), r => (ushort)r.UVar());
            else if (t == typeof(uint)) Set<uint, T>((a, b) => a == b, (w, v) => w.UVar(v), r => (uint)r.UVar());
            else if (t == typeof(ulong)) Set<ulong, T>((a, b) => a == b, (w, v) => w.UVar(v), r => r.UVar());
            else if (t == typeof(char)) Set<char, T>((a, b) => a == b, (w, v) => w.UVar(v), r => (char)r.UVar());
            else if (t == typeof(DateTime)) Set<DateTime, T>((a, b) => a == b && a.Kind == b.Kind, (w, v) => w.Long(v.ToBinary()), r => DateTime.FromBinary(r.Long()));
            else if (t == typeof(TimeSpan)) Set<TimeSpan, T>((a, b) => a == b, (w, v) => w.Long(v.Ticks), r => new TimeSpan(r.Long()));
            else if (t == typeof(DateTimeOffset)) Set<DateTimeOffset, T>((a, b) => a.Equals(b) && a.Offset == b.Offset, (w, v) => { w.Long(v.Ticks); w.Long(v.Offset.Ticks); }, r => new DateTimeOffset(r.Long(), new TimeSpan(r.Long())));
            else if (t == typeof(Guid)) Set<Guid, T>((a, b) => a == b, (w, v) => w.Str(v.ToString("N")), r => Guid.ParseExact(r.Str(), "N"));
            else if (t == typeof(decimal)) Set<decimal, T>((a, b) => a == b, (w, v) => w.Str(v.ToString(CultureInfo.InvariantCulture)), r => decimal.Parse(r.Str(), CultureInfo.InvariantCulture));
            else if (t.IsEnum)
            {
                var comparer = EqualityComparer<T>.Default;
                var unsigned = Type.GetTypeCode(Enum.GetUnderlyingType(t)) is TypeCode.Byte or TypeCode.UInt16 or TypeCode.UInt32 or TypeCode.UInt64;
                ValueOps<T>.Equal = comparer.Equals;
                if (unsigned)
                {
                    ValueOps<T>.Write = (w, v) => w.UVar(Convert.ToUInt64(v, CultureInfo.InvariantCulture));
                    ValueOps<T>.Read = r => (T)Enum.ToObject(t, r.UVar());
                }
                else
                {
                    ValueOps<T>.Write = (w, v) => w.Long(Convert.ToInt64(v, CultureInfo.InvariantCulture));
                    ValueOps<T>.Read = r => (T)Enum.ToObject(t, r.Long());
                }
                var write = ValueOps<T>.Write;
                var read = ValueOps<T>.Read;
                AddBoxed(t, (w, o) => write(w, (T)o), r => read(r));
            }
            else if (typeof(IEquatable<T>).IsAssignableFrom(t))
            {
                // 쓰기·읽기는 등록될 때 채워진다(Unity 구조체·생성 코드 구조체).
                var comparer = EqualityComparer<T>.Default;
                ValueOps<T>.Equal = comparer.Equals;
            }
        }
    }

    public delegate void MirrorSyncFn<T>(T live, MirrorNode node, ChangeSet changes);
    public delegate void MirrorWriteFn<T>(MirrorWriter writer, T value);
    public delegate void MirrorReadFn<T>(T target, MirrorReader reader);
    public delegate T MirrorNewFn<T>();
    public delegate void MirrorApplyFn<T>(T target, int field, int depth, MirrorReader reader, MirrorOp op);
    public delegate bool MirrorEqualFn<T>(T a, T b);
    public delegate T MirrorValueReadFn<T>(MirrorReader reader);
    // 멤버 번호로 읽기·쓰기(FieldAccess). 번호 = 그 형식에 선언된 순서.
    public delegate object MirrorGetFn(object target, int member);
    public delegate void MirrorSetFn(object target, int member, object value);

    sealed class RefEq : IEqualityComparer<object>
    {
        public static readonly RefEq Instance = new RefEq();
        public new bool Equals(object a, object b) => ReferenceEquals(a, b);
        public int GetHashCode(object o) => RuntimeHelpers.GetHashCode(o);
    }
}
