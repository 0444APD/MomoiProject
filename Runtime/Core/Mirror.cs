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
        // 훑은 어셈블리(다시 훑을 때 새로 들어온 것만 — Rescan).
        static readonly HashSet<string> Scanned = new HashSet<string>(StringComparer.Ordinal);

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
                    Scan();
                    _initialized = true;
                }
                finally
                {
                    _initializing = false;
                }
            }
        }

        // 처음 훑을 때 빠졌던 어셈블리(테스트 어셈블리를 다시 넣을 때 — GameAssemblies.Use)를 더 훑는다. 형식 번호를 다시 매긴다.
        public static void Rescan()
        {
            lock (Gate)
            {
                if (!_initialized || _initializing) return;
                _initializing = true;
                try { Scan(); }
                finally { _initializing = false; }
            }
        }

        static void Scan()
        {
            foreach (var assembly in GameAssemblies.UsingCore())
            {
                if (!Scanned.Add(assembly.FullName)) continue;
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                foreach (var type in types) RegisterType(type);
            }
            // 형식 번호: 이름순(같은 코드로 빌드한 서버·클라이언트가 같은 번호를 쓴다).
            ById.Clear();
            ById.AddRange(ByType.Values.OrderBy(t => t.Type.FullName, StringComparer.Ordinal).ThenBy(t => t.Type.Assembly.GetName().Name, StringComparer.Ordinal));
            for (var i = 0; i < ById.Count; i++) ById[i].Id = i;
            // 새로 등록된 형식이 있으면 보내기 검사(CanSend·CanSendValue) 결과가 달라질 수 있다.
            lock (SendChecks) SendChecks.Clear();
            lock (ValueChecks) ValueChecks.Clear();
        }

        // 형식 하나의 생성 코드(__gb_register)를 부른다. 한 형식이 실패해도(깨진 생성 코드·형식을 불러오지 못함 …) 나머지 형식과
        // 게임 시작은 그대로 간다: 그 형식은 비교 코드가 없는 것으로 남기고(예전 비교 방식 — 반쯤 등록된 것도 뺀다) 이유를 한 번 알린다.
        static void RegisterType(Type type)
        {
            try
            {
                if (type.IsGenericTypeDefinition) return;
                var register = type.GetMethod("__gb_register", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                register?.Invoke(null, null);
            }
            catch (Exception e)
            {
                var cause = e is TargetInvocationException { InnerException: { } inner } ? inner : e;
                lock (Gate) ByType.Remove(type);
                Warn($"{type.FullName}의 생성 코드를 등록하지 못했습니다 — 이 형식은 비교 코드가 없는 것으로 다룹니다(예전 비교 방식). {cause.GetType().Name}: {cause.Message}");
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

        // 이 형식을 뿌리로 쓸 수 있는지(모든 필드가 생성 코드로 다뤄지는지). 아니면 reason에 첫 이유(어느 필드인지 경로로).
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
            return CheckType(root, DataModel.FriendlyName(type), new HashSet<Type>(), ref reason, false, false);
        }

        // 부모 쪽 필드를 생성 코드가 다루지 못한 클래스(제네릭 부모·GBible.Core를 참조하지 않는 어셈블리의 부모)의 표시:
        // 필드 배치 맨 앞의 종류 None 칸, 이름 = 이 앞부분 + 사람이 읽는 설명(MirrorWeaver와 같은 값). 이 형식은 Supports·CanSend가 거짓이다.
        internal const string LostBaseMarker = "$base ";

        // 형식을 네트워크로 보낼 수 있는지(행동: 클라이언트 → 서버, 이벤트: 서버 → 클라이언트). 형식만 보고, 결과는 기억한다
        // (보내는 쪽이 형식마다 처음 보낼 때 부른다 — NetClient·NetServer). 아니면 reason에 첫 이유(어느 필드인지 경로로).
        //   · 생성 코드가 모든 필드를 쓰고 읽는지: 중첩 컬렉션·쓸 수 없는 구조체((int, int)·KeyValuePair …)·생성 코드가 없는 클래스(HashSet …)·
        //     다루지 못한 부모 필드가 있으면 받는 쪽에 빈 칸으로 도착하거나 쓰다가 멈춘다.
        //     안쪽에 컬렉션이 있는 구조체는 된다(쓰기·읽기는 온전 — 변경 추적만 못 하므로 Supports는 거짓).
        //   · 값으로 보이는데 생성 코드가 쓰지 않는 필드(readonly·자동 속성·대리자)가 없는지 — 받는 쪽에 기본값으로 도착한다.
        //   · 행동의 참조 필드(id가 있는 객체와 그 리스트·배열 — ActionReferences)는 id만 가므로 안쪽을 보지 않는다.
        //   object·추상·인터페이스 칸의 실제 값은 형식만으로 알 수 없으므로 보낼 때 CanSendValue로 본다.
        public static bool CanSend(Type type, out string reason)
        {
            reason = null;
            if (type == null) return true;
            if (!_initialized) Initialize();
            lock (SendChecks)
                if (SendChecks.TryGetValue(type, out reason)) return reason == null;
            string problem = null;
            var path = DataModel.FriendlyName(type);
            var root = Find(type);
            if (root == null)
                problem = type.IsValueType ? $"{path}: 구조체는 보낼 수 없습니다([Serializable] 클래스로 만드세요)" : $"{path}에 생성 코드가 없습니다([Serializable]을 붙이세요)";
            else if (CheckUnwritten(type, path, ref problem))
                CheckType(root, path, new HashSet<Type>(), ref problem, true, typeof(GameAction).IsAssignableFrom(type));
            lock (SendChecks) SendChecks[type] = problem;
            reason = problem;
            return problem == null;
        }

        static readonly Dictionary<Type, string> SendChecks = new Dictionary<Type, string>();

        // 행동·이벤트 안의 값 하나(실제 형식)를 네트워크로 보낼 수 있는지 — 보내는 쪽이 값을 쓰는 동안 객체마다 부른다(Mirror.WriteNetValue의 nodeOf).
        // object·추상·인터페이스 칸에 든 값처럼 형식만으로 모르는 것을 잡는다: 생성 코드가 없는 값(상자에 든 숫자·글자, [Serializable]이 없는 클래스)과
        // ID가 없는 에셋 참조는 받는 쪽에 null로 도착한다. 생성 코드가 있는 값은 그 형식 자신의 필드만 본다(안쪽 객체는 쓸 때 따로 불린다).
        // 결과는 형식마다 기억한다(에셋 참조는 값마다).
        public static bool CanSendValue(object value, out string reason)
        {
            reason = null;
            if (value == null) return true;
            var type = value.GetType();
            if (DataModel.IsReferenceType(type))
            {
                if (DataModel.ReferenceToId == null || !string.IsNullOrEmpty(DataModel.ReferenceToId(value))) return true;
                reason = $"{DataModel.FriendlyName(type)} 에셋은 ID가 있는 DB 항목이 아니라 보낼 수 없습니다(받는 쪽에는 null) — DB 항목이나 그 ID를 담으세요";
                return false;
            }
            if (!_initialized) Initialize();
            lock (ValueChecks)
                if (ValueChecks.TryGetValue(type, out reason)) return reason == null;
            var path = DataModel.FriendlyName(type);
            var found = Find(type);
            string problem = null;
            if (found == null)
                problem = $"{path} 값은 생성 코드가 없어 보낼 수 없습니다(받는 쪽에는 null) — 숫자·글자는 object 칸 대신 그 형식의 필드에, 객체는 [Serializable] 클래스로";
            else
                // id가 있는 객체(데이터 안의 객체)는 행동에서는 id만 쓰이고(받는 쪽이 지금 데이터에서 다시 찾는다) 이벤트에서는 보통 받는 쪽이
                // 가진 객체를 번호로 가리키므로 빠지는 칸은 상관없다 — 쓸 수 있는지만 본다.
                CheckOwnFields(found, path, ref problem, SentAsId(type));
            lock (ValueChecks) ValueChecks[type] = problem;
            reason = problem;
            return problem == null;
        }

        static readonly Dictionary<Type, string> ValueChecks = new Dictionary<Type, string>();

        // 형식의 필드를 차례로 본다(안쪽 객체 형식까지). path = 이 형식까지의 경로(이유에 쓴다).
        // network: 네트워크로 보낼 값(쓰고 읽을 수만 있으면 된다). actionRoot: 행동 형식 자신(참조 필드는 id만 가므로 안쪽을 보지 않는다).
        static bool CheckType(MirrorType type, string path, HashSet<Type> seen, ref string reason, bool network, bool actionRoot)
        {
            if (!seen.Add(type.Type)) return true;
            for (var i = 0; i < type.FieldCount; i++)
            {
                var field = type.FieldTypes[i];
                var name = path + "." + type.Names[i];
                bool ok;
                switch (type.Kinds[i])
                {
                    case MirrorKind.None:
                        reason = NoneReason(type, i, path);
                        return false;
                    case MirrorKind.Struct:
                        ok = CheckValue(field, name, network, ref reason);
                        break;
                    case MirrorKind.Object:
                        ok = (actionRoot && SentAsId(field)) || CheckReference(field, name, seen, ref reason, network);
                        break;
                    case MirrorKind.List:
                    case MirrorKind.Array:
                    {
                        var element = ElementOf(field);
                        ok = element != null && ((actionRoot && SentAsId(element)) || CheckElement(element, name + "[]", seen, ref reason, network));
                        break;
                    }
                    case MirrorKind.Dictionary:
                    {
                        var args = field != null && field.IsGenericType ? field.GetGenericArguments() : Type.EmptyTypes;
                        ok = args.Length == 2 && IsKeyType(args[0]) && CheckElement(args[1], name + "[]", seen, ref reason, network);
                        break;
                    }
                    default:
                        ok = true;
                        break;
                }
                if (ok) continue;
                reason ??= $"{name}({DataModel.FriendlyName(field ?? typeof(object))})는 비교 코드로 다룰 수 없는 형식입니다";
                return false;
            }
            return true;
        }

        // 형식 자신의 필드만(안쪽 객체는 쓸 때 따로 본다 — CanSendValue): 생성 코드가 빼먹는 칸(skipMissing이면 보지 않는다)·쓸 수 없는 구조체·요소.
        static void CheckOwnFields(MirrorType type, string path, ref string reason, bool skipMissing)
        {
            for (var i = 0; i < type.FieldCount && reason == null; i++)
            {
                var field = type.FieldTypes[i];
                var name = path + "." + type.Names[i];
                switch (type.Kinds[i])
                {
                    case MirrorKind.None:
                        if (!skipMissing) reason = NoneReason(type, i, path);
                        break;
                    case MirrorKind.Struct:
                        CheckValue(field, name, true, ref reason);
                        break;
                    case MirrorKind.List:
                    case MirrorKind.Array:
                    {
                        var element = ElementOf(field);
                        if (element != null && element.IsValueType) CheckValue(element, name + "[]", true, ref reason);
                        break;
                    }
                    case MirrorKind.Dictionary:
                    {
                        var args = field != null && field.IsGenericType ? field.GetGenericArguments() : Type.EmptyTypes;
                        if (args.Length == 2 && args[1].IsValueType) CheckValue(args[1], name + "[]", true, ref reason);
                        break;
                    }
                }
            }
        }

        // 행동의 참조 필드로 id만 보내는 객체인지: id가 있는 클래스(ActionReferences — 받는 쪽이 지금 데이터에서 id로 다시 찾는다)이고
        // 그 id를 생성 코드가 쓴다(잃은 부모 쪽에 있으면 id도 가지 않는다).
        static bool SentAsId(Type type) => ActionReferences.IsReferenceType(type) && Find(type) is { } found && found.FindIdField() >= 0;

        // 종류 None 칸(생성 코드가 다루지 못하는 필드, 또는 잃은 부모 필드의 표시)의 이유.
        static string NoneReason(MirrorType type, int i, string path)
        {
            var name = type.Names[i] ?? "";
            if (name.StartsWith(LostBaseMarker, StringComparison.Ordinal))
                return $"{path}: 부모 {name.Substring(LostBaseMarker.Length)}는 생성 코드가 다루지 못합니다(그 필드를 이 클래스로 옮기거나, 부모를 제네릭이 아닌 [Serializable] 클래스로 GBible.Core를 참조하는 어셈블리에 두세요)";
            return $"{path}.{name}({DataModel.FriendlyName(type.FieldTypes[i] ?? typeof(object))})는 생성 코드가 다루지 못하는 형식입니다(중첩 컬렉션·다차원 배열·키가 글자·정수·enum이 아닌 딕셔너리 — List<T>·Dictionary<K, V>·[Serializable] 클래스로 바꾸세요)";
        }

        // 구조체·값 요소 하나. network면 쓰고 읽을 수만 있으면 되고(안쪽에 컬렉션이 있는 구조체도), 아니면 변경을 따라갈 수 있어야 한다.
        static bool CheckValue(Type type, string name, bool network, ref string reason)
        {
            if (type != null && (network ? MirrorValues.CanWrite(type) : MirrorValues.IsSupported(type))) return true;
            var shown = DataModel.FriendlyName(type ?? typeof(object));
            reason = !network && type != null && MirrorValues.IsShallow(type)
                ? $"{name}: {shown}는 안쪽에 객체·컬렉션 필드가 있는 구조체라 안쪽 변경을 따라가지 못합니다([Serializable] 클래스로 바꾸세요)"
                : $"{name}: {shown}는 쓰고 읽는 방법이 없는 구조체입니다((int, int)·KeyValuePair 같은 구조체는 담지 못합니다 — [Serializable] 구조체나 클래스로 바꾸세요)";
            return false;
        }

        static bool IsKeyType(Type type) => type == typeof(string) || type.IsEnum || (type.IsPrimitive && type != typeof(float) && type != typeof(double));

        static bool IsCollection(Type type) =>
            type.IsArray || (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(List<>) || type.GetGenericTypeDefinition() == typeof(Dictionary<,>)));

        static Type ElementOf(Type collection) =>
            collection == null ? null : collection.IsArray ? collection.GetElementType() : collection.IsGenericType ? collection.GetGenericArguments()[0] : null;

        static bool CheckElement(Type type, string name, HashSet<Type> seen, ref string reason, bool network)
        {
            if (type == typeof(string) || type.IsEnum) return true;
            if (IsCollection(type))
            {
                reason = $"{name}({DataModel.FriendlyName(type)}): 컬렉션 안의 컬렉션은 다루지 못합니다([Serializable] 클래스로 한 번 감싸세요)";
                return false;
            }
            if (type.IsValueType) return CheckValue(type, name, network, ref reason);
            return CheckReference(type, name, seen, ref reason, network);
        }

        static bool CheckReference(Type type, string name, HashSet<Type> seen, ref string reason, bool network)
        {
            if (type == null) return false;
            if (DataModel.IsReferenceType(type)) return true;               // 에셋 참조: 참조 그대로 비교(네트워크는 ID로 — 보낼 때 값마다 본다)
            // 인터페이스·추상 컬렉션(IList<T>·IDictionary<K,V>·IReadOnlyList<T> …): 실제 값(List·Dictionary)은 생성 코드가 없어 참조로만 비교한다 — 안쪽 변경을 놓친다.
            if ((type.IsAbstract || type.IsInterface) && typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
            {
                reason = $"{name}({DataModel.FriendlyName(type)}): 인터페이스·추상 컬렉션 형식은 다루지 못합니다(List<T>·Dictionary<K, V>로 선언하세요)";
                return false;
            }
            if (type == typeof(object) || type.IsAbstract || type.IsInterface) return true; // 실제 형식으로 그때그때(네트워크는 보낼 때 값마다 본다)
            if (IsCollection(type))
            {
                reason = $"{name}({DataModel.FriendlyName(type)}): 컬렉션 안의 컬렉션은 다루지 못합니다([Serializable] 클래스로 한 번 감싸세요)";
                return false;
            }
            var found = Find(type);
            if (found == null)
            {
                reason = type.Namespace == "System" || (type.Namespace?.StartsWith("System.", StringComparison.Ordinal) ?? false)
                    ? $"{name}({DataModel.FriendlyName(type)})는 다루지 못하는 형식입니다(List<T>·Dictionary<K, V>·배열이나 [Serializable] 클래스로 바꾸세요)"
                    : $"{name}({DataModel.FriendlyName(type)})에 비교 코드가 없습니다([Serializable]을 붙이세요)";
                return false;
            }
            return CheckType(found, name, seen, ref reason, network, false);
        }

        // 값으로 보이는데 생성 코드가 쓰지 않는 필드(readonly 필드·자동 속성·대리자): 네트워크로 보내면 받는 쪽에 기본값으로 도착한다.
        // [NonSerialized]와 private 필드([SerializeField] 없음)는 값이 아닌 것으로 본다(Unity와 같다). 보낼 형식 자신만 본다(CanSend가 형식마다 한 번).
        static bool CheckUnwritten(Type type, string path, ref string reason)
        {
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
                foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.IsNotSerialized) continue;
                    var backing = field.Name.StartsWith("<", StringComparison.Ordinal);
                    var serialized = field.CustomAttributes.Any(a => a.AttributeType.Name == "SerializeField" || a.AttributeType.Name == "SerializeReference");
                    if (!field.IsPublic && !backing && !serialized) continue;
                    string what = null;
                    if (field.IsInitOnly) what = backing ? "읽기 전용 자동 속성" : "readonly 필드";
                    else if (backing && !serialized) what = "자동 속성";
                    else if (typeof(Delegate).IsAssignableFrom(field.FieldType)) what = "대리자(함수) 필드";
                    if (what == null) continue;
                    var end = field.Name.IndexOf('>');
                    var name = backing && end > 1 ? field.Name.Substring(1, end - 1) : field.Name;
                    reason = $"{path}.{name}: {what}는 보내지 않습니다(받는 쪽에는 기본값으로 도착) — public 필드로 바꾸거나, 보내지 않을 값이면 [NonSerialized]를 붙이세요";
                    return false;
                }
            return true;
        }

        // ── 사본 ───────────────────────────────────────────────

        // ── 사본 나무 ───────────────────────────────────────────
        // 사본 나무 하나(플레이어 데이터 하나)에서 객체 하나는 사본도 하나다: 두 곳에서 가리키면 두 곳이 같은 사본을 가리킨다.
        //   · Sync는 한 회차에 사본 하나를 한 번만 비교한다(같은 변경을 두 번 기록하지 않고, 서로 가리키는 객체도 끝난다).
        //   · 사본마다 객체 번호(NetId)가 있어, 받는 쪽(MirrorReceiver)이 같은 객체를 같은 인스턴스로 잇는다.
        //   · 가리키는 곳 수(Refs)가 0이 되면 나무에서 뺀다.

        [ThreadStatic] static MirrorTree _tree;
        [ThreadStatic] static bool _building;
        // 새 사본을 만드는 깊이(MakeAny 안). 만드는 동안에는 이미 있던 사본에 들어가지 않는다(Enter 참고).
        [ThreadStatic] static int _making;
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
            var previousMaking = _making;
            _tree = tree;
            _building = true;
            _making = 0;
            try
            {
                node.Pass = tree.Pass;
                type.Sync(live, node, null);
            }
            finally
            {
                _tree = previousTree;
                _building = previousBuilding;
                _making = previousMaking;
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
            var previousMaking = _making;
            _tree = tree;
            _building = false;
            _making = 0;
            try
            {
                tree.Pass++;
                root.Pass = tree.Pass;
                root.Type.Sync(live, root, changes);
                if (tree.MaybeOrphans) Sweep(tree);
            }
            finally
            {
                _tree = previousTree;
                _building = previousBuilding;
                _making = previousMaking;
            }
        }

        // 이번 회차에 들르지 않은 사본(뿌리에서 닿지 않는다 — 서로 가리키는 객체가 함께 빠진 고리)을 나무에서 뺀다.
        // Sync는 닿는 사본마다 한 번 들어가므로(Enter) 들르지 않음 = 닿지 않음이다. 빼지 않으면 메모리에 남고, 다시 들어올 때
        // 늦게 들어온 받는 쪽이 모르는 번호로 보낸다. 번호를 지키는 동안(KeepIds)이면 기억해 두었다가 돌아오면 그 번호를 쓴다.
        static void Sweep(MirrorTree tree)
        {
            tree.MaybeOrphans = false;
            List<MirrorNode> orphans = null;
            foreach (var node in tree.Nodes.Values)
                if (node.Pass != tree.Pass) (orphans ??= new List<MirrorNode>()).Add(node);
            if (orphans == null) return;
            foreach (var node in orphans)
            {
                if (node.Origin == null || !tree.Nodes.TryGetValue(node.Origin, out var found) || found != node) continue;
                tree.Nodes.Remove(node.Origin);
                if (tree.Retired != null) tree.Retired[node.Origin] = node;
                node.Refs = 0;
                for (var i = 0; i < node.Type.FieldCount; i++)
                    if (node.R[i] is MirrorCollection tracked) Untrack(tracked);
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
            // 번호를 지키는 동안(KeepIds) 빠졌다가 돌아온 객체: 받는 쪽은 빠진 것을 받지 않았으니 예전 번호·보낸 표시를 그대로 쓴다.
            if (tree?.Retired != null && tree.Retired.TryGetValue(live, out var retired))
            {
                tree.Retired.Remove(live);
                node.NetId = retired.NetId;
                node.Sent = retired.Sent;
            }
            if (tree != null)
            {
                tree.Nodes[live] = node;
                // 이번 회차에 한 번 더 들어갈 수 있게 둔다: 다 만든 뒤 부른 쪽이 Visit로 기록과 함께 다시 훑는다.
                node.Pass = tree.Pass - 1;
            }
            _making++;
            try
            {
                type.Sync(live, node, null);
            }
            finally
            {
                _making--;
            }
            return node;
        }

        // 받는 쪽에 보내지 않는 변경(미리보기: 바뀌었다가 그대로 돌아온다) 동안 켠다. 그동안 나무에서 빠진 객체가 다시 들어오면
        // 새 번호 대신 예전 번호를 쓴다 — 받는 쪽은 빠진 것을 받지 않아 예전 인스턴스를 그 번호로 들고 있다.
        // 끄면 기억한 것을 버린다(돌아오지 않은 객체를 붙잡지 않는다).
        public static void KeepIds(MirrorNode root, bool keep)
        {
            var tree = root?.Tree;
            if (tree == null) return;
            tree.Retired = keep ? tree.Retired ?? new Dictionary<object, MirrorNode>(RefEq.Instance) : null;
        }

        // 번호를 지키는 중인가(KeepIds).
        internal static bool KeepingIds(MirrorNode root) => root?.Tree?.Retired != null;

        // 이번 회차에 이 사본을 처음 비교하는지(처음이면 표시하고 true). 나무 밖(Sync 중이 아님)이면 언제나 true.
        // 새 사본을 만드는 동안(기록 없음)에는 false: 새 객체가 이미 있던 객체를 가리킬 때 그 객체의 바뀐 곳을 기록 없이 맞춰 버리면
        // 이번 변경이 기록에서 빠진다(예: u.hp -= 5; log.Add(new Hit { target = u })). 다 만든 뒤 부른 쪽이 Visit로 새 사본을
        // 기록과 함께 다시 훑으면서 그때 들어간다(새 사본은 Pass를 한 칸 뒤로 두어 한 번 더 들어갈 수 있다).
        internal static bool Enter(MirrorNode node)
        {
            var tree = node.Tree;
            if (tree == null) return true;
            if (_making > 0 || _building) return false;
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
                if (--node.Refs > 0)
                {
                    // 아직 가리키는 곳이 남았다 — 서로 가리키는 객체끼리만 가리키는 것(닿지 않는 고리)일 수 있다: 이번 Sync 끝에 훑는다.
                    tree.MaybeOrphans = true;
                    return;
                }
                if (node.Origin != null && tree.Nodes.TryGetValue(node.Origin, out var found) && found == node)
                {
                    tree.Nodes.Remove(node.Origin);
                    if (tree.Retired != null) tree.Retired[node.Origin] = node;
                }
                for (var i = 0; i < node.Type.FieldCount; i++)
                {
                    var child = node.R[i];
                    if (child is MirrorNode || child is IMirrorShadow) Release(child);
                }
                return;
            }
            if (shadow is IMirrorShadow collection)
            {
                if (collection is MirrorCollection tracked) Untrack(tracked);
                var nodes = new List<MirrorNode>();
                collection.AddNodes(nodes);
                foreach (var item in nodes) Release(item);
            }
        }

        // 컬렉션 사본이 따라가는 실제 컬렉션을 정한다. 같은 인스턴스를 다른 곳의 사본도 따라가면 알린다:
        // 사본이 곳마다 따로라 한 번의 변경이 두 번 기록되고, 되돌리기는 같은 리스트에 두 번 적용되어 데이터가 깨진다.
        internal static void Track(MirrorCollection shadow, object live, MirrorNode owner, int field)
        {
            var tree = _tree;
            if (tree == null) return;
            Untrack(shadow);
            if (live == null) return;
            shadow.Origin = live;
            var map = tree.Collections ??= new Dictionary<object, MirrorCollection>(RefEq.Instance);
            if (!map.TryGetValue(live, out var other)) map[live] = shadow;
            else if (other != shadow)
                Warn($"{DataModel.FriendlyName(owner.Type.Type)}.{owner.Type.Names[field]}: 다른 곳과 같은 컬렉션 인스턴스를 가리킵니다. 변경이 두 번 기록되어 Undo·멀티에서 데이터가 깨질 수 있습니다. " +
                     "컬렉션은 한 곳에서만 가리키게 하세요(필요하면 new List<T>(원본)처럼 복사).");
        }

        static void Untrack(MirrorCollection shadow)
        {
            var map = _tree?.Collections;
            if (map != null && shadow.Origin != null && map.TryGetValue(shadow.Origin, out var found) && found == shadow) map.Remove(shadow.Origin);
            shadow.Origin = null;
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

        // 알린 기록을 지운다(도메인 리로드를 끈 설정에서도 플레이마다 다시 알린다).
        public static void ResetWarnings()
        {
            lock (Warned)
            {
                Warned.Clear();
                WarnedText.Clear();
            }
        }

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
        // 읽기·적용에서 허용하는 객체·경로 깊이(게임 데이터는 이만큼 깊지 않다).
        const int MaxDepth = 256;
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
                // 받은 데이터(네트워크·리플레이)가 객체를 끝없이 겹쳐 넣어도 스택이 넘치지 않게(StackOverflow는 잡을 수 없어 프로세스가 죽는다).
                if (_readDepth > MaxDepth) throw new FormatException("[MomoiProject] 객체가 너무 깊이 겹쳐 있습니다(받은 데이터가 깨졌거나 잘못되었습니다).");
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
            var depth = reader.UVar();
            if (depth > MaxDepth) throw new FormatException("[MomoiProject] 변경 기록의 경로가 너무 깁니다(받은 데이터가 깨졌습니다).");
            ApplyInto(root, reader, (int)depth, op);
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
        // 실제 컬렉션 → 그것을 따라가는 사본(같은 인스턴스를 두 곳에서 가리키는지 볼 때).
        public Dictionary<object, MirrorCollection> Collections;
        // KeepIds 동안 나무에서 빠진 사본(원래 객체 → 사본). 다시 들어오면 그 번호를 쓴다.
        public Dictionary<object, MirrorNode> Retired;
        // 이번 Sync에서 가리키는 곳이 줄었지만 남은 사본이 있었다(닿지 않는 고리일 수 있다 — Sync 끝에 훑는다).
        public bool MaybeOrphans;
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

    // 컬렉션 사본의 공통 부분: 지금 따라가는 실제 컬렉션(같은 인스턴스를 두 곳에서 가리키는지 볼 때 — Mirror.Track).
    abstract class MirrorCollection : IMirrorShadow
    {
        public object Origin;
        public abstract void WriteShadow(MirrorWriter writer, List<object> originals);
        public abstract void AddNodes(List<MirrorNode> into);
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

        // 안쪽에 객체·컬렉션 필드가 있는 구조체(쓰기·읽기만 — 변경 추적은 못 한다).
        static readonly HashSet<Type> Shallow = new HashSet<Type>();

        internal static void MarkShallow(Type type)
        {
            lock (Boxed) Shallow.Add(type);
        }

        internal static bool IsShallow(Type type)
        {
            lock (Boxed) return Shallow.Contains(type);
        }

        // 변경을 따라갈 수 있는 값 형식인가. T?는 T도 되어야 한다(T?의 비교·쓰기가 T의 것을 부른다).
        public static bool IsSupported(Type type)
        {
            if (type.IsPrimitive || type.IsEnum || type == typeof(string) || BuiltinTypes.Contains(type)) return true;
            var inner = Nullable.GetUnderlyingType(type);
            if (inner != null && !IsSupported(inner)) return false;
            lock (Boxed) return Boxed.ContainsKey(type) && !Shallow.Contains(type);
        }

        // 쓰고 읽을 수 있는 값 형식인가(변경 추적과 상관없이 — 네트워크로 보낼 때). 안쪽에 객체·컬렉션이 있는 구조체도 된다.
        // IntPtr·UIntPtr은 기본 형식이지만 쓰는 방법이 없다.
        public static bool CanWrite(Type type)
        {
            if (type == typeof(IntPtr) || type == typeof(UIntPtr)) return false;
            if (type.IsPrimitive || type.IsEnum || type == typeof(string) || BuiltinTypes.Contains(type)) return true;
            var inner = Nullable.GetUnderlyingType(type);
            if (inner != null && !CanWrite(inner)) return false;
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
