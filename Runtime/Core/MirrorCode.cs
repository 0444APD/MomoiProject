using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;

namespace GBible
{
    // Mirror 생성 코드(GBible.CodeGen.MirrorWeaver)가 부르는 도우미. 게임 코드에서 직접 쓰지 않는다.
    // 생성 코드는 필드마다 이 메서드 하나를 부른다(숫자 필드는 비교까지 생성 코드 안에서 하고, 다를 때만 부른다).
    // 메서드 이름·매개변수를 바꾸면 MirrorWeaver도 같이 바꾼다.
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class MirrorCode
    {
        // ── 등록 ───────────────────────────────────────────────

        public static void RegisterClass<T>(string[] names, byte[] kinds, MirrorSyncFn<T> sync, MirrorWriteFn<T> write, MirrorReadFn<T> read,
            MirrorNewFn<T> create, MirrorApplyFn<T> apply) where T : class
        {
            var type = new MirrorType
            {
                Type = typeof(T),
                Names = names,
                Kinds = kinds.Select(k => (MirrorKind)k).ToArray(),
                FieldTypes = names.Select(n => FieldType(typeof(T), n)).ToArray(),
                Sync = (o, n, c) => sync((T)o, n, c),
                Write = (w, o) => write(w, (T)o),
                Read = (o, r) => read((T)o, r),
                New = () => create(),
                Apply = (o, f, d, r, op) => apply((T)o, f, d, r, op),
            };
            Mirror.Add(type);
        }

        // Nullable<T> 값(int? 등)의 비교·쓰기·읽기. 생성 코드가 그런 필드를 가진 형식의 등록에서 부른다(IL2CPP가 형식을 미리 알게).
        public static void RegisterNullable<T>() where T : struct
        {
            if (ValueOps<T?>.Supported) return;
            ValueOps<T?>.Register(EqualNullable<T>, WriteNullable<T>, ReadNullable<T>);
        }

        static bool EqualNullable<T>(T? a, T? b) where T : struct => a.HasValue ? b.HasValue && ValueOps<T>.Equal(a.Value, b.Value) : !b.HasValue;

        static void WriteNullable<T>(MirrorWriter w, T? v) where T : struct
        {
            w.Bool(v.HasValue);
            if (v.HasValue) ValueOps<T>.Write(w, v.Value);
        }

        static T? ReadNullable<T>(MirrorReader r) where T : struct => r.Bool() ? ValueOps<T>.Read(r) : (T?)null;

        // 멤버 읽기·쓰기(필드 경로·저장·이벤트 값). kinds: 0 = 저장 필드, 1 = readonly 필드, 2 = 속성.
        public static void RegisterAccess(RuntimeTypeHandle type, string[] names, byte[] kinds, MirrorGetFn get, MirrorSetFn set) =>
            FieldAccess.Register(Type.GetTypeFromHandle(type), names, kinds, get, set);

        public static void RegisterStruct<T>(MirrorEqualFn<T> equal, MirrorWriteFn<T> write, MirrorValueReadFn<T> read) where T : struct =>
            ValueOps<T>.Register(equal, write, read);

        // 안쪽에 객체·컬렉션 필드가 있는 구조체: 쓰기·읽기는 하되 변경 추적은 하지 않는다(Supports가 거짓 → 예전 비교 방식).
        public static void RegisterShallowStruct<T>(MirrorEqualFn<T> equal, MirrorWriteFn<T> write, MirrorValueReadFn<T> read) where T : struct
        {
            ValueOps<T>.Register(equal, write, read);
            MirrorValues.MarkShallow(typeof(T));
        }

        static Type FieldType(Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var field = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null) return field.FieldType;
            }
            return typeof(object);
        }

        public static object Uninitialized(RuntimeTypeHandle handle) => FormatterServices.GetUninitializedObject(Type.GetTypeFromHandle(handle));

        public static void BadField() => throw new InvalidOperationException("[MomoiProject] 변경 기록의 필드 번호가 맞지 않습니다(서로 다른 코드로 빌드됨).");

        // ── 숫자 비트 ───────────────────────────────────────────
        // 숫자·참거짓·enum은 long 비트로 사본에 둔다(float·double은 비트 그대로라 NaN도 같게 본다).

        public static long F2B(float v) => BitConverter.DoubleToInt64Bits(v);
        public static long D2B(double v) => BitConverter.DoubleToInt64Bits(v);
        public static float B2F(long bits) => (float)BitConverter.Int64BitsToDouble(bits);
        public static double B2D(long bits) => BitConverter.Int64BitsToDouble(bits);

        public static void WriteBits(MirrorWriter w, long bits, int kind)
        {
            switch ((MirrorKind)kind)
            {
                case MirrorKind.Bool: w.Byte((byte)bits); return;
                case MirrorKind.Byte:
                case MirrorKind.UInt16:
                case MirrorKind.UInt32:
                case MirrorKind.UInt64:
                case MirrorKind.Char: w.UVar((ulong)bits); return;
                case MirrorKind.Single: w.Float(B2F(bits)); return;
                case MirrorKind.Double: w.Double(B2D(bits)); return;
                default: w.Long(bits); return;
            }
        }

        public static long ReadBits(MirrorReader r, int kind)
        {
            switch ((MirrorKind)kind)
            {
                case MirrorKind.Bool: return r.Byte();
                case MirrorKind.Byte:
                case MirrorKind.UInt16:
                case MirrorKind.UInt32:
                case MirrorKind.UInt64:
                case MirrorKind.Char: return (long)r.UVar();
                case MirrorKind.Single: return F2B(r.Float());
                case MirrorKind.Double: return D2B(r.Double());
                default: return r.Long();
            }
        }

        // 사람이 읽는 기록용 값.
        static object BoxBits(long bits, MirrorKind kind, Type fieldType)
        {
            if (fieldType != null && fieldType.IsEnum) return Enum.ToObject(fieldType, bits);
            switch (kind)
            {
                case MirrorKind.Bool: return bits != 0;
                case MirrorKind.Single: return B2F(bits);
                case MirrorKind.Double: return B2D(bits);
                case MirrorKind.Char: return (char)bits;
                case MirrorKind.Int32: return (int)bits;
                case MirrorKind.UInt64: return (ulong)bits;
                default: return bits;
            }
        }

        // ── 비교(Sync) ─────────────────────────────────────────

        public static void SyncBits(MirrorNode n, int i, long now, ChangeSet cs)
        {
            var old = n.L[i];
            n.L[i] = now;
            if (cs == null) return;
            var type = n.Type;
            var kind = type.Kinds[i];
            cs.PushField(i, type.Names[i]);
            cs.Begin(MirrorOp.Set, MirrorOp.Set);
            WriteBits(cs.Forward, now, (int)kind);
            WriteBits(cs.Backward, old, (int)kind);
            if (cs.Describe) cs.Note(DataChangeKind.Set, BoxBits(old, kind, type.FieldTypes[i]), BoxBits(now, kind, type.FieldTypes[i]));
            cs.Pop();
        }

        public static void SyncStr(string now, MirrorNode n, int i, ChangeSet cs)
        {
            var old = (string)n.R[i];
            if (string.Equals(old, now)) return;
            n.R[i] = now;
            if (cs == null) return;
            cs.PushField(i, n.Type.Names[i]);
            cs.Begin(MirrorOp.Set, MirrorOp.Set);
            cs.Forward.Str(now);
            cs.Backward.Str(old);
            if (cs.Describe) cs.Note(DataChangeKind.Set, old, now);
            cs.Pop();
        }

        public static void SyncStruct<T>(T now, MirrorNode n, int i, ChangeSet cs)
        {
            var box = n.R[i];
            var equal = ValueOps<T>.Equal;
            // int?·enum? 같은 Nullable의 null은 상자에 담으면 null이다 — 둘 다 비었으면 같은 것(매 Sync "null → null"을 기록하지 않게).
            if (box == null ? now == null : equal != null ? equal((T)box, now) : EqualityComparer<T>.Default.Equals((T)box, now)) return;
            n.R[i] = now;
            if (cs == null) return;
            var old = box == null ? default : (T)box;
            cs.PushField(i, n.Type.Names[i]);
            cs.Begin(MirrorOp.Set, MirrorOp.Set);
            ValueOps<T>.Write(cs.Forward, now);
            ValueOps<T>.Write(cs.Backward, old);
            if (cs.Describe) cs.Note(DataChangeKind.Set, old, now);
            cs.Pop();
        }

        public static void SyncObj<T>(T now, MirrorNode n, int i, ChangeSet cs) where T : class
        {
            var old = n.R[i];
            if (old is MirrorNode child)
            {
                if (ReferenceEquals(child.Origin, now))
                {
                    if (!Mirror.Enter(child)) return;
                    cs?.PushField(i, n.Type.Names[i]);
                    child.Type.Sync(now, child, cs);
                    cs?.Pop();
                    return;
                }
            }
            else if (ReferenceEquals(old, now)) return;
            var made = Mirror.MakeAny(now);
            cs?.PushField(i, n.Type.Names[i]);
            if (cs != null)
            {
                cs.Begin(MirrorOp.Set, MirrorOp.Set);
                Mirror.WriteForward(cs.Forward, made);
                Mirror.WriteShadow(cs.Backward, old, cs.Originals);
                if (cs.Describe) cs.Note(DataChangeKind.Set, Mirror.Rebuild(old, typeof(T)), now);
            }
            n.R[i] = made;
            Mirror.Release(old);
            Visit(made, now, cs);
            cs?.Pop();
        }

        // 다른 곳에서도 가리키는 사본을 새로 가리키게 됐을 때: 이번 회차에 아직 비교하지 않았으면 지금 자리(경로)에서 비교한다.
        static void Visit(object shadow, object live, ChangeSet cs)
        {
            if (shadow is MirrorNode node && Mirror.Enter(node)) node.Type.Sync(live, node, cs);
        }

        public static void SyncList<T>(List<T> now, MirrorNode n, int i, ChangeSet cs)
        {
            var old = n.R[i] as ListShadow<T>;
            if (now == null || old == null)
            {
                if (now == null && old == null) return;
                ReplaceList(now, old, n, i, cs);
                return;
            }
            if (!ReferenceEquals(old.Origin, now)) Mirror.Track(old, now, n, i);
            cs?.PushField(i, n.Type.Names[i]);
            if (Elem<T>.IsValue) SyncValues(now, old.Values, cs);
            else SyncObjects(now, old.Items, cs);
            cs?.Pop();
        }

        public static void SyncArray<T>(T[] now, MirrorNode n, int i, ChangeSet cs)
        {
            var old = n.R[i] as ArrayShadow<T>;
            if (now == null || old == null || now.Length != old.Length)
            {
                if (now == null && old == null) return;
                ReplaceArray(now, old, n, i, cs);
                return;
            }
            if (!ReferenceEquals(old.Origin, now)) Mirror.Track(old, now, n, i);
            cs?.PushField(i, n.Type.Names[i]);
            if (Elem<T>.IsValue)
            {
                var equal = ValueOps<T>.Equal;
                var values = old.Values;
                for (var k = 0; k < now.Length; k++)
                {
                    var x = now[k];
                    if (equal(x, values[k])) continue;
                    if (cs != null) RecordValueSet(cs, k, x, values[k]);
                    values[k] = x;
                }
            }
            else
            {
                var items = old.Items;
                for (var k = 0; k < now.Length; k++) SyncElement(now[k], items, k, cs);
            }
            cs?.Pop();
        }

        public static void SyncDict<TKey, TValue>(Dictionary<TKey, TValue> now, MirrorNode n, int i, ChangeSet cs)
        {
            var old = n.R[i] as DictShadow<TKey, TValue>;
            if (now == null || old == null)
            {
                if (now == null && old == null) return;
                ReplaceDict(now, old, n, i, cs);
                return;
            }
            if (!ReferenceEquals(old.Origin, now)) Mirror.Track(old, now, n, i);
            cs?.PushField(i, n.Type.Names[i]);
            if (Elem<TValue>.IsValue) SyncValueDict(now, old.Values, cs);
            else SyncObjectDict(now, old.Items, cs);
            cs?.Pop();
        }

        // 컬렉션이 통째로 생기거나(null → 값) 사라질(값 → null) 때. 람다 없이 따로 둔다(자주 불리는 Sync가 할당하지 않게).
        static void ReplaceList<T>(List<T> now, ListShadow<T> old, MirrorNode n, int i, ChangeSet cs)
        {
            var made = now == null ? null : ListShadow<T>.Make(now);
            if (cs != null)
            {
                BeginReplace(n, i, cs);
                Mirror.WriteForwardCollection(cs.Forward, made);
                EndReplace(cs, old, now);
            }
            n.R[i] = made;
            Mirror.Release(old);
            if (made != null) Mirror.Track(made, now, n, i);
            if (made?.Items == null) return;
            cs?.PushField(i, n.Type.Names[i]);
            SyncObjects(now, made.Items, cs);
            cs?.Pop();
        }

        static void ReplaceArray<T>(T[] now, ArrayShadow<T> old, MirrorNode n, int i, ChangeSet cs)
        {
            var made = now == null ? null : ArrayShadow<T>.Make(now);
            if (cs != null)
            {
                BeginReplace(n, i, cs);
                Mirror.WriteForwardCollection(cs.Forward, made);
                EndReplace(cs, old, now);
            }
            n.R[i] = made;
            Mirror.Release(old);
            if (made != null) Mirror.Track(made, now, n, i);
            if (made?.Items == null) return;
            cs?.PushField(i, n.Type.Names[i]);
            for (var k = 0; k < now.Length; k++) SyncElement(now[k], made.Items, k, cs);
            cs?.Pop();
        }

        static void ReplaceDict<TKey, TValue>(Dictionary<TKey, TValue> now, DictShadow<TKey, TValue> old, MirrorNode n, int i, ChangeSet cs)
        {
            var made = now == null ? null : DictShadow<TKey, TValue>.Make(now);
            if (cs != null)
            {
                BeginReplace(n, i, cs);
                Mirror.WriteForwardCollection(cs.Forward, made);
                EndReplace(cs, old, now);
            }
            n.R[i] = made;
            Mirror.Release(old);
            if (made != null) Mirror.Track(made, now, n, i);
            if (made?.Items == null) return;
            cs?.PushField(i, n.Type.Names[i]);
            SyncObjectDict(now, made.Items, cs);
            cs?.Pop();
        }

        static void BeginReplace(MirrorNode n, int i, ChangeSet cs)
        {
            cs.PushField(i, n.Type.Names[i]);
            cs.Begin(MirrorOp.Set, MirrorOp.Set);
        }

        static void EndReplace(ChangeSet cs, IMirrorShadow old, object now)
        {
            if (old == null) cs.Backward.UVar(0);
            else old.WriteShadow(cs.Backward, cs.Originals);
            if (cs.Describe) cs.Note(DataChangeKind.Set, old == null ? null : "(컬렉션)", now);
            cs.Pop();
        }

        static void RecordValueSet<T>(ChangeSet cs, int k, T now, T old)
        {
            cs.PushIndex(k);
            cs.Begin(MirrorOp.Set, MirrorOp.Set);
            ValueOps<T>.Write(cs.Forward, now);
            ValueOps<T>.Write(cs.Backward, old);
            if (cs.Describe) cs.Note(DataChangeKind.Set, old, now);
            cs.Pop();
        }

        // 값 리스트: 같은 번호끼리 비교, 늘거나 줄면 끝에서 더하기·자르기.
        static void SyncValues<T>(List<T> a, List<T> b, ChangeSet cs)
        {
            var equal = ValueOps<T>.Equal;
            var n = a.Count < b.Count ? a.Count : b.Count;
            for (var k = 0; k < n; k++)
            {
                var x = a[k];
                if (equal(x, b[k])) continue;
                if (cs != null) RecordValueSet(cs, k, x, b[k]);
                b[k] = x;
            }
            if (a.Count > b.Count)
            {
                if (cs != null)
                {
                    cs.Begin(MirrorOp.ListAdd, MirrorOp.ListCut);
                    if (cs.Describe) cs.Reshaped.Add(cs.PathText());
                    cs.Forward.UVar((ulong)(a.Count - b.Count));
                    for (var k = b.Count; k < a.Count; k++) ValueOps<T>.Write(cs.Forward, a[k]);
                    cs.Backward.UVar((ulong)b.Count);
                    if (cs.Describe)
                        for (var k = b.Count; k < a.Count; k++) cs.Note(DataChangeKind.Insert, null, a[k], $"[{k}]");
                }
                for (var k = b.Count; k < a.Count; k++) b.Add(a[k]);
            }
            else if (a.Count < b.Count)
            {
                if (cs != null)
                {
                    cs.Begin(MirrorOp.ListCut, MirrorOp.ListAdd);
                    if (cs.Describe) cs.Reshaped.Add(cs.PathText());
                    cs.Forward.UVar((ulong)a.Count);
                    cs.Backward.UVar((ulong)(b.Count - a.Count));
                    for (var k = a.Count; k < b.Count; k++) ValueOps<T>.Write(cs.Backward, b[k]);
                    if (cs.Describe)
                        for (var k = a.Count; k < b.Count; k++) cs.Note(DataChangeKind.Remove, b[k], null, $"[{k}]");
                }
                b.RemoveRange(a.Count, b.Count - a.Count);
            }
        }

        // 객체 리스트: 같은 자리에 같은 객체가 있으면 안쪽만 비교, 아니면 배치를 다시 짠다(지우기·끼워 넣기·순서 바꾸기).
        static void SyncObjects<T>(List<T> a, List<object> b, ChangeSet cs)
        {
            var same = a.Count == b.Count;
            if (same)
                for (var k = 0; k < a.Count; k++)
                    if (!ReferenceEquals(a[k], Mirror.OriginOf(b[k])))
                    {
                        same = false;
                        break;
                    }
            if (!same) Relayout(a, b, cs);
            for (var k = 0; k < a.Count; k++)
            {
                if (!(b[k] is MirrorNode node) || !Mirror.Enter(node)) continue;
                cs?.PushIndex(k, a[k]);
                node.Type.Sync(a[k], node, cs);
                cs?.Pop();
            }
        }

        // 배열·딕셔너리 값 자리 하나(객체): 같은 객체면 안쪽, 아니면 통째로 바꾼다.
        static void SyncElement<T>(T now, List<object> items, int k, ChangeSet cs)
        {
            var old = items[k];
            if (old is MirrorNode node)
            {
                if (ReferenceEquals(node.Origin, now))
                {
                    if (!Mirror.Enter(node)) return;
                    cs?.PushIndex(k, now);
                    node.Type.Sync(now, node, cs);
                    cs?.Pop();
                    return;
                }
            }
            else if (ReferenceEquals(old, now)) return;
            var made = Mirror.MakeAny(now);
            cs?.PushIndex(k, now);
            if (cs != null)
            {
                cs.Begin(MirrorOp.Set, MirrorOp.Set);
                Mirror.WriteForward(cs.Forward, made);
                Mirror.WriteShadow(cs.Backward, old, cs.Originals);
                if (cs.Describe) cs.Note(DataChangeKind.Set, Mirror.Rebuild(old, typeof(T)), now);
            }
            items[k] = made;
            Mirror.Release(old);
            Visit(made, now, cs);
            cs?.Pop();
        }

        static void Relayout<T>(List<T> a, List<object> b, ChangeSet cs)
        {
            var pos = new Dictionary<object, int>(b.Count, RefEq.Instance);
            for (var j = 0; j < b.Count; j++)
            {
                var origin = Mirror.OriginOf(b[j]);
                if (origin != null && !pos.ContainsKey(origin)) pos.Add(origin, j);
            }
            var src = new int[a.Count];
            var dst = new int[b.Count];
            for (var j = 0; j < dst.Length; j++) dst[j] = -1;
            for (var i = 0; i < a.Count; i++)
            {
                object x = a[i];
                if (x != null && pos.TryGetValue(x, out var j) && dst[j] < 0)
                {
                    src[i] = j;
                    dst[j] = i;
                }
                else src[i] = -1;
            }
            // 새 자리의 사본(남은 요소는 옛 사본, 새 요소는 새로 — 다른 곳에 이미 있는 객체면 그 사본을 함께 가리킨다).
            var next = new List<object>(a.Count);
            for (var i = 0; i < a.Count; i++) next.Add(src[i] >= 0 ? b[src[i]] : Mirror.MakeAny(a[i]));
            if (cs != null)
            {
                cs.Begin(MirrorOp.Layout, MirrorOp.Layout);
                WriteRuns(cs.Forward, src, a.Count, k => Mirror.WriteForward(cs.Forward, next[k]));
                WriteRuns(cs.Backward, dst, b.Count, k => Mirror.WriteShadow(cs.Backward, b[k], cs.Originals));
                if (cs.Describe)
                {
                    cs.Reshaped.Add(cs.PathText());
                    for (var j = 0; j < b.Count; j++)
                        if (dst[j] < 0) cs.Note(DataChangeKind.Remove, Mirror.Rebuild(b[j], typeof(T)), null, cs.ElementText(j, Mirror.OriginOf(b[j])));
                    for (var i = 0; i < a.Count; i++)
                        if (src[i] < 0) cs.Note(DataChangeKind.Insert, null, a[i], cs.ElementText(i, a[i]));
                    var last = -1;
                    var moved = false;
                    foreach (var j in src)
                    {
                        if (j < 0) continue;
                        if (j < last) moved = true;
                        last = j;
                    }
                    if (moved) cs.Note(DataChangeKind.Reorder, null, null);
                }
            }
            for (var j = 0; j < b.Count; j++)
                if (dst[j] < 0) Mirror.Release(b[j]);
            b.Clear();
            b.AddRange(next);
        }

        // 배치 구간: 옛 리스트의 [시작, 시작+길이)를 그대로(0), 또는 새 요소 길이개(1). map[i] = 다른 쪽 번호(-1 = 통째로 쓸 요소).
        static void WriteRuns(MirrorWriter w, int[] map, int count, Action<int> writeElement)
        {
            var runs = 0;
            for (var i = 0; i < count; i++)
                if (i == 0 || (map[i] < 0) != (map[i - 1] < 0) || (map[i] >= 0 && map[i] != map[i - 1] + 1)) runs++;
            w.UVar((ulong)count);
            w.UVar((ulong)runs);
            for (var i = 0; i < count;)
            {
                var len = 1;
                if (map[i] >= 0)
                {
                    var start = map[i];
                    while (i + len < count && map[i + len] == start + len) len++;
                    w.Byte(0);
                    w.UVar((ulong)start);
                    w.UVar((ulong)len);
                }
                else
                {
                    while (i + len < count && map[i + len] < 0) len++;
                    w.Byte(1);
                    w.UVar((ulong)len);
                    for (var k = i; k < i + len; k++) writeElement(k);
                }
                i += len;
            }
        }

        static void SyncValueDict<TKey, TValue>(Dictionary<TKey, TValue> a, Dictionary<TKey, TValue> b, ChangeSet cs)
        {
            var equal = ValueOps<TValue>.Equal;
            foreach (var pair in a)
            {
                if (b.TryGetValue(pair.Key, out var old))
                {
                    if (equal(pair.Value, old)) continue;
                    if (cs != null)
                    {
                        cs.PushKey(pair.Key, KeyOps<TKey>.WriteBoxed);
                        cs.Begin(MirrorOp.Set, MirrorOp.Set);
                        ValueOps<TValue>.Write(cs.Forward, pair.Value);
                        ValueOps<TValue>.Write(cs.Backward, old);
                        if (cs.Describe) cs.Note(DataChangeKind.Set, old, pair.Value);
                        cs.Pop();
                    }
                    b[pair.Key] = pair.Value;
                    continue;
                }
                if (cs != null)
                {
                    cs.Begin(MirrorOp.DictSet, MirrorOp.DictRemove);
                    ValueOps<TKey>.Write(cs.Forward, pair.Key);
                    ValueOps<TValue>.Write(cs.Forward, pair.Value);
                    ValueOps<TKey>.Write(cs.Backward, pair.Key);
                    if (cs.Describe) cs.Note(DataChangeKind.Insert, null, pair.Value, KeyText(pair.Key));
                }
                b.Add(pair.Key, pair.Value);
            }
            if (b.Count == a.Count) return;
            foreach (var key in Missing(a, b.Keys))
            {
                if (cs != null)
                {
                    cs.Begin(MirrorOp.DictRemove, MirrorOp.DictSet);
                    ValueOps<TKey>.Write(cs.Forward, key);
                    ValueOps<TKey>.Write(cs.Backward, key);
                    ValueOps<TValue>.Write(cs.Backward, b[key]);
                    if (cs.Describe) cs.Note(DataChangeKind.Remove, b[key], null, KeyText(key));
                }
                b.Remove(key);
            }
        }

        static void SyncObjectDict<TKey, TValue>(Dictionary<TKey, TValue> a, Dictionary<TKey, object> b, ChangeSet cs)
        {
            foreach (var pair in a)
            {
                object now = pair.Value;
                if (b.TryGetValue(pair.Key, out var old))
                {
                    if (old is MirrorNode node && ReferenceEquals(node.Origin, now))
                    {
                        if (!Mirror.Enter(node)) continue;
                        cs?.PushKey(pair.Key, KeyOps<TKey>.WriteBoxed);
                        node.Type.Sync(now, node, cs);
                        cs?.Pop();
                        continue;
                    }
                    if (!(old is MirrorNode) && ReferenceEquals(old, now)) continue;
                    var replaced = Mirror.MakeAny(now);
                    cs?.PushKey(pair.Key, KeyOps<TKey>.WriteBoxed);
                    if (cs != null)
                    {
                        cs.Begin(MirrorOp.Set, MirrorOp.Set);
                        Mirror.WriteForward(cs.Forward, replaced);
                        Mirror.WriteShadow(cs.Backward, old, cs.Originals);
                        if (cs.Describe) cs.Note(DataChangeKind.Set, Mirror.Rebuild(old, typeof(TValue)), now);
                    }
                    b[pair.Key] = replaced;
                    Mirror.Release(old);
                    Visit(replaced, now, cs);
                    cs?.Pop();
                    continue;
                }
                var made = Mirror.MakeAny(now);
                if (cs != null)
                {
                    cs.Begin(MirrorOp.DictSet, MirrorOp.DictRemove);
                    ValueOps<TKey>.Write(cs.Forward, pair.Key);
                    Mirror.WriteForward(cs.Forward, made);
                    ValueOps<TKey>.Write(cs.Backward, pair.Key);
                    if (cs.Describe) cs.Note(DataChangeKind.Insert, null, now, KeyText(pair.Key));
                }
                b.Add(pair.Key, made);
                cs?.PushKey(pair.Key, KeyOps<TKey>.WriteBoxed);
                Visit(made, now, cs);
                cs?.Pop();
            }
            if (b.Count == a.Count) return;
            foreach (var key in Missing(a, b.Keys))
            {
                if (cs != null)
                {
                    cs.Begin(MirrorOp.DictRemove, MirrorOp.DictSet);
                    ValueOps<TKey>.Write(cs.Forward, key);
                    ValueOps<TKey>.Write(cs.Backward, key);
                    Mirror.WriteShadow(cs.Backward, b[key], cs.Originals);
                    if (cs.Describe) cs.Note(DataChangeKind.Remove, Mirror.Rebuild(b[key], typeof(TValue)), null, KeyText(key));
                }
                Mirror.Release(b[key]);
                b.Remove(key);
            }
        }

        // a에 없는 b의 키(람다 없이: 자주 불리는 Sync가 할당하지 않게).
        static List<TKey> Missing<TKey, TValue>(Dictionary<TKey, TValue> a, IEnumerable<TKey> keys)
        {
            var gone = new List<TKey>();
            foreach (var key in keys)
                if (!a.ContainsKey(key)) gone.Add(key);
            return gone;
        }

        static string KeyText(object key) => $"[{key}]";

        // ── 쓰기 ───────────────────────────────────────────────

        public static void WriteStr(MirrorWriter w, string v) => w.Str(v);

        public static void WriteStruct<T>(MirrorWriter w, T v) => ValueOps<T>.Write(w, v);

        public static void WriteObj<T>(MirrorWriter w, T v) where T : class => Mirror.Write(w, v);

        static void WriteElement<T>(MirrorWriter w, T v)
        {
            if (Elem<T>.IsValue) ValueOps<T>.Write(w, v);
            else Mirror.Write(w, v);
        }

        public static void WriteList<T>(MirrorWriter w, List<T> v)
        {
            if (v == null)
            {
                w.UVar(0);
                return;
            }
            w.UVar((ulong)v.Count + 1);
            for (var i = 0; i < v.Count; i++) WriteElement(w, v[i]);
        }

        public static void WriteArray<T>(MirrorWriter w, T[] v)
        {
            if (v == null)
            {
                w.UVar(0);
                return;
            }
            w.UVar((ulong)v.Length + 1);
            for (var i = 0; i < v.Length; i++) WriteElement(w, v[i]);
        }

        // 딕셔너리는 키 순서로 쓴다(같은 내용이면 언제나 같은 바이트).
        public static void WriteDict<TKey, TValue>(MirrorWriter w, Dictionary<TKey, TValue> v)
        {
            if (v == null)
            {
                w.UVar(0);
                return;
            }
            w.UVar((ulong)v.Count + 1);
            foreach (var key in SortedKeys(v.Keys))
            {
                ValueOps<TKey>.Write(w, key);
                WriteElement(w, v[key]);
            }
        }

        internal static IEnumerable<TKey> SortedKeys<TKey>(IEnumerable<TKey> keys)
        {
            var list = keys.ToList();
            if (typeof(TKey) == typeof(string)) list.Sort((a, b) => string.CompareOrdinal((string)(object)a, (string)(object)b));
            else list.Sort(Comparer<TKey>.Default);
            return list;
        }

        // ── 읽기 ───────────────────────────────────────────────

        public static string ReadStr(MirrorReader r) => r.Str();

        public static T ReadStruct<T>(MirrorReader r) => ValueOps<T>.Read(r);

        public static T ReadObj<T>(MirrorReader r) where T : class => (T)Mirror.Read(r, typeof(T));

        static T ReadElement<T>(MirrorReader r) => Elem<T>.IsValue ? ValueOps<T>.Read(r) : (T)Mirror.Read(r, typeof(T));

        public static List<T> ReadList<T>(MirrorReader r)
        {
            var n = r.Count(1);
            if (n == 0) return null;
            var list = new List<T>(n - 1);
            for (var i = 1; i < n; i++) list.Add(ReadElement<T>(r));
            return list;
        }

        public static T[] ReadArray<T>(MirrorReader r)
        {
            var n = r.Count(1);
            if (n == 0) return null;
            var array = new T[n - 1];
            for (var i = 0; i < array.Length; i++) array[i] = ReadElement<T>(r);
            return array;
        }

        public static Dictionary<TKey, TValue> ReadDict<TKey, TValue>(MirrorReader r)
        {
            var n = r.Count(1);
            if (n == 0) return null;
            var map = new Dictionary<TKey, TValue>(n - 1);
            for (var i = 1; i < n; i++)
            {
                var key = ValueOps<TKey>.Read(r);
                map[key] = ReadElement<TValue>(r);
            }
            return map;
        }

        // ── 적용(받는 쪽 동기화·되돌리기) ─────────────────────────
        // depth = 남은 경로 칸 수(이 필드 칸 포함). 1이면 이 필드가 대상, 그보다 크면 안으로 들어간다.

        public static void ApplyStr(ref string field, MirrorReader r, int depth, MirrorOp op) => field = r.Str();

        public static void ApplyStruct<T>(ref T field, MirrorReader r, int depth, MirrorOp op) => field = ValueOps<T>.Read(r);

        public static void ApplyObj<T>(ref T field, MirrorReader r, int depth, MirrorOp op) where T : class
        {
            if (depth == 1) field = (T)Mirror.Read(r, typeof(T));
            else Mirror.ApplyInto(field, r, depth - 1, op);
        }

        public static void ApplyList<T>(ref List<T> field, MirrorReader r, int depth, MirrorOp op)
        {
            if (depth == 1)
            {
                switch (op)
                {
                    case MirrorOp.Set:
                        field = ReadList<T>(r);
                        return;
                    case MirrorOp.ListAdd:
                        var add = (int)r.UVar();
                        for (var k = 0; k < add; k++) field.Add(ReadElement<T>(r));
                        return;
                    case MirrorOp.ListCut:
                        var keep = (int)r.UVar();
                        field.RemoveRange(keep, field.Count - keep);
                        return;
                    case MirrorOp.Layout:
                        r.UVar();
                        var runs = (int)r.UVar();
                        var old = field.ToArray();
                        field.Clear();
                        for (var q = 0; q < runs; q++)
                        {
                            if (r.Byte() == 0)
                            {
                                var start = (int)r.UVar();
                                var len = (int)r.UVar();
                                for (var k = 0; k < len; k++) field.Add(old[start + k]);
                            }
                            else
                            {
                                var len = (int)r.UVar();
                                for (var k = 0; k < len; k++) field.Add(ReadElement<T>(r));
                            }
                        }
                        return;
                }
                throw new InvalidOperationException("[MomoiProject] 리스트에 쓸 수 없는 기록입니다: " + op);
            }
            r.Byte();
            var index = (int)r.UVar();
            if (depth == 2) field[index] = ReadElement<T>(r);
            else Mirror.ApplyInto(field[index], r, depth - 2, op);
        }

        public static void ApplyArray<T>(ref T[] field, MirrorReader r, int depth, MirrorOp op)
        {
            if (depth == 1)
            {
                field = ReadArray<T>(r);
                return;
            }
            r.Byte();
            var index = (int)r.UVar();
            if (depth == 2) field[index] = ReadElement<T>(r);
            else Mirror.ApplyInto(field[index], r, depth - 2, op);
        }

        public static void ApplyDict<TKey, TValue>(ref Dictionary<TKey, TValue> field, MirrorReader r, int depth, MirrorOp op)
        {
            if (depth == 1)
            {
                switch (op)
                {
                    case MirrorOp.Set:
                        field = ReadDict<TKey, TValue>(r);
                        return;
                    case MirrorOp.DictSet:
                        var key = ValueOps<TKey>.Read(r);
                        field[key] = ReadElement<TValue>(r);
                        return;
                    case MirrorOp.DictRemove:
                        field.Remove(ValueOps<TKey>.Read(r));
                        return;
                }
                throw new InvalidOperationException("[MomoiProject] 딕셔너리에 쓸 수 없는 기록입니다: " + op);
            }
            r.Byte();
            var at = ValueOps<TKey>.Read(r);
            if (depth == 2) field[at] = ReadElement<TValue>(r);
            else Mirror.ApplyInto(field[at], r, depth - 2, op);
        }

        // ── 구조체 안의 비교 ─────────────────────────────────────

        public static bool EqStr(string a, string b) => string.Equals(a, b);

        public static bool EqStruct<T>(T a, T b)
        {
            var equal = ValueOps<T>.Equal;
            return equal != null ? equal(a, b) : EqualityComparer<T>.Default.Equals(a, b);
        }

        public static bool EqRef(object a, object b) => ReferenceEquals(a, b);
    }

    sealed class ListShadow<T> : MirrorCollection
    {
        public List<T> Values;
        public List<object> Items;

        public override void AddNodes(List<MirrorNode> into)
        {
            if (Items == null) return;
            foreach (var item in Items)
                if (item is MirrorNode node) into.Add(node);
        }

        public static ListShadow<T> Make(List<T> live)
        {
            var shadow = new ListShadow<T>();
            if (Elem<T>.IsValue) shadow.Values = new List<T>(live);
            else
            {
                shadow.Items = new List<object>(live.Count);
                foreach (var item in live) shadow.Items.Add(Mirror.MakeAny(item));
            }
            return shadow;
        }

        public override void WriteShadow(MirrorWriter w, List<object> originals)
        {
            if (Values != null)
            {
                w.UVar((ulong)Values.Count + 1);
                foreach (var v in Values) ValueOps<T>.Write(w, v);
                return;
            }
            w.UVar((ulong)Items.Count + 1);
            foreach (var item in Items) Mirror.WriteShadow(w, item, originals);
        }
    }

    sealed class ArrayShadow<T> : MirrorCollection
    {
        public T[] Values;
        public List<object> Items;
        public int Length;

        public override void AddNodes(List<MirrorNode> into)
        {
            if (Items == null) return;
            foreach (var item in Items)
                if (item is MirrorNode node) into.Add(node);
        }

        public static ArrayShadow<T> Make(T[] live)
        {
            var shadow = new ArrayShadow<T> { Length = live.Length };
            if (Elem<T>.IsValue) shadow.Values = (T[])live.Clone();
            else shadow.Items = live.Select(item => Mirror.MakeAny(item)).ToList();
            return shadow;
        }

        public override void WriteShadow(MirrorWriter w, List<object> originals)
        {
            w.UVar((ulong)Length + 1);
            if (Values != null)
                foreach (var v in Values) ValueOps<T>.Write(w, v);
            else
                foreach (var item in Items) Mirror.WriteShadow(w, item, originals);
        }
    }

    sealed class DictShadow<TKey, TValue> : MirrorCollection
    {
        public Dictionary<TKey, TValue> Values;
        public Dictionary<TKey, object> Items;

        public override void AddNodes(List<MirrorNode> into)
        {
            if (Items == null) return;
            foreach (var item in Items.Values)
                if (item is MirrorNode node) into.Add(node);
        }

        public static DictShadow<TKey, TValue> Make(Dictionary<TKey, TValue> live)
        {
            var shadow = new DictShadow<TKey, TValue>();
            if (Elem<TValue>.IsValue) shadow.Values = new Dictionary<TKey, TValue>(live, live.Comparer);
            else
            {
                shadow.Items = new Dictionary<TKey, object>(live.Count, live.Comparer);
                foreach (var pair in live) shadow.Items.Add(pair.Key, Mirror.MakeAny(pair.Value));
            }
            return shadow;
        }

        public override void WriteShadow(MirrorWriter w, List<object> originals)
        {
            if (Values != null)
            {
                w.UVar((ulong)Values.Count + 1);
                foreach (var key in MirrorCode.SortedKeys(Values.Keys))
                {
                    ValueOps<TKey>.Write(w, key);
                    ValueOps<TValue>.Write(w, Values[key]);
                }
                return;
            }
            w.UVar((ulong)Items.Count + 1);
            foreach (var key in MirrorCode.SortedKeys(Items.Keys))
            {
                ValueOps<TKey>.Write(w, key);
                Mirror.WriteShadow(w, Items[key], originals);
            }
        }
    }
}
