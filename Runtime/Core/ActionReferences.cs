using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace GBible
{
    // 행동의 참조 필드: string id가 있는 클래스 형식의 필드(와 그 리스트·배열).
    // 프로그래머는 객체를 넣고(Game.Do(new PlayCard { card = deckCard })), 기록·전송에는 그 객체의 id만 쓴다.
    // 실행 직전에 id로 지금 데이터에서 다시 찾아 필드에 넣는다 — Undo·불러오기 뒤의 옛 객체, 다른 컴퓨터의 객체를 넣어도 Run에서는 지금 객체다.
    // 어느 필드가 참조인지는 형식마다 한 번 정해 둔다(게임이 켜질 때 PrepareAll로 미리, 빠진 형식은 처음 쓸 때).
    public static class ActionReferences
    {
        sealed class Field
        {
            public string Name;
            public Type ElementType;
            public Type FieldType;
            public bool Many;
            public MemberAccess Access;
            // 새 배열·목록을 만드는 함수(형식마다 한 번 정한다 — 실행 중 리플렉션 없이). 배열·List·HashSet·Queue·Stack이 아니면 null(아래의 예전 길).
            public Func<IReadOnlyList<object>, object> Make;
        }

        static readonly Dictionary<Type, Field[]> Plans = new Dictionary<Type, Field[]>();

        // [Chosen] 필드(선택 대기의 답을 받는 칸). Ids면 string(들), 아니면 id가 있는 클래스(들).
        sealed class ChosenField
        {
            public string Name;
            public Type ElementType;
            public Type FieldType;
            public bool Many;
            public bool Ids;
            // id만 든 객체로 만들 형식. 추상 형식이면 그 아래 구체 형식 하나(실행 직전에 Resolve가 id로 지금 객체로 바꾸므로 어느 것이든 된다).
            public Type StubType;
            public MemberAccess Access;
            public Func<IReadOnlyList<object>, object> Make;
        }

        static readonly Dictionary<Type, ChosenField> Chosen = new Dictionary<Type, ChosenField>();

        // 실행 직전: 참조 필드를 지금 데이터의 객체로 바꿔 넣는다. 없으면 행동 실패(변경 없음).
        public static void Resolve(GameAction action, GameApi game)
        {
            if (action == null || game == null) return;
            foreach (var field in PlanOf(action.GetType()))
            {
                var value = field.Access?.Get(action);
                if (value == null) continue;
                if (!field.Many)
                {
                    field.Access.Set(action, Live(game, action, field, value));
                    continue;
                }
                // 넣어 준 목록은 고치지 않고 새 목록에 담아 바꿔 넣는다(데이터 안의 목록을 그대로 넣었어도 그 목록이 바뀌지 않게).
                if (field.Make != null)
                {
                    var items = new List<object>();
                    foreach (var item in (IEnumerable)value) items.Add(item == null ? null : Live(game, action, field, item));
                    field.Access.Set(action, field.Make(items));
                    continue;
                }
                if (value is Array array)
                {
                    var copy = Array.CreateInstance(field.ElementType, array.Length);
                    for (int i = 0; i < array.Length; i++)
                        if (array.GetValue(i) is { } item) copy.SetValue(Live(game, action, field, item), i);
                    field.Access.Set(action, copy);
                }
                else if (value is IList list && DataModel.Create(field.FieldType) is IList fresh)
                {
                    foreach (var item in list) fresh.Add(item == null ? null : Live(game, action, field, item));
                    field.Access.Set(action, fresh);
                }
                else if (DataModel.Of(field.FieldType) is { Kind: DataKind.Collection } info)
                {
                    // 리스트가 아닌 컬렉션(HashSet·Queue …)도 새 컬렉션에 담는다.
                    var collection = DataModel.Create(field.FieldType);
                    info.WriteItems(collection, info.ReadItems(value).Select(item => item == null ? null : Live(game, action, field, item)).ToList());
                    field.Access.Set(action, collection);
                }
            }
        }

        // 저장용(예약에 맡길 때): 참조 필드를 id만 든 같은 형식의 객체로 바꾼다 — 데이터 안의 객체를 다른 저장 칸에 끌어들이지 않게.
        // 실행할 때 Resolve가 id로 지금 객체를 다시 찾는다.
        public static void Detach(GameAction action)
        {
            if (action == null) return;
            foreach (var field in PlanOf(action.GetType()))
            {
                var value = field.Access?.Get(action);
                if (value == null) continue;
                if (!field.Many) { field.Access.Set(action, Stub(value)); continue; }
                if (field.Make != null)
                {
                    var items = new List<object>();
                    foreach (var item in (IEnumerable)value) items.Add(item == null ? null : Stub(item));
                    field.Access.Set(action, field.Make(items));
                    continue;
                }
                if (value is Array array)
                {
                    var copy = Array.CreateInstance(field.ElementType, array.Length);
                    for (int i = 0; i < array.Length; i++) copy.SetValue(array.GetValue(i) is { } item ? Stub(item) : null, i);
                    field.Access.Set(action, copy);
                }
                else if (value is IList list && DataModel.Create(field.FieldType) is IList fresh)
                {
                    foreach (var item in list) fresh.Add(item == null ? null : Stub(item));
                    field.Access.Set(action, fresh);
                }
                else if (DataModel.Of(field.FieldType) is { Kind: DataKind.Collection } info)
                {
                    var collection = DataModel.Create(field.FieldType);
                    info.WriteItems(collection, info.ReadItems(value).Select(item => item == null ? null : Stub(item)).ToList());
                    field.Access.Set(action, collection);
                }
            }
        }

        // 참조 필드가 가리키는 객체가 지금 데이터에 모두 있는가(예약을 실행해도 되는가).
        public static bool CanResolve(GameAction action, GameApi game)
        {
            if (action == null || game == null) return false;
            foreach (var field in PlanOf(action.GetType()))
            {
                var value = field.Access?.Get(action);
                if (value == null) continue;
                var items = field.Many ? ((IEnumerable)value).Cast<object>() : new[] { value };
                foreach (var item in items)
                    if (item != null && game.Find(field.ElementType, SafeId(item)) == null) return false;
            }
            return true;
        }

        public static bool HasChosen(Type type) => ChosenOf(type) != null;

        // 고른 id들을 [Chosen] 필드에 넣는다. 객체 칸이면 id만 든 객체를 넣고, 실행 직전에 Resolve가 지금 객체로 바꾼다.
        public static void SetChosen(GameAction action, IReadOnlyList<string> ids)
        {
            var field = ChosenOf(action.GetType())
                ?? throw new InvalidOperationException($"[MomoiProject] 행동 '{action.GetType().Name}'에 [Chosen] 필드가 없습니다.");
            ids ??= Array.Empty<string>();
            object Item(string id) => field.Ids ? id : StubOf(field.StubType, id);
            if (!field.Many)
            {
                field.Access.Set(action, ids.Count == 0 ? null : Item(ids[0]));
                return;
            }
            if (field.Make != null)
            {
                field.Access.Set(action, field.Make(ids.Select(Item).ToList()));
                return;
            }
            if (field.FieldType.IsArray)
            {
                var array = Array.CreateInstance(field.ElementType, ids.Count);
                for (int i = 0; i < ids.Count; i++) array.SetValue(Item(ids[i]), i);
                field.Access.Set(action, array);
            }
            else if (DataModel.Create(field.FieldType) is IList list)
            {
                foreach (var id in ids) list.Add(Item(id));
                field.Access.Set(action, list);
            }
        }

        static ChosenField ChosenOf(Type type)
        {
            lock (Chosen)
            {
                if (Chosen.TryGetValue(type, out var known)) return known;
                ChosenField found = null;
                foreach (var field in DataModel.Of(type).Fields)
                {
                    if (!IsChosen(type, field.Name)) continue;
                    var fieldType = field.Type;
                    var info = DataModel.Of(fieldType);
                    var many = info.Kind == DataKind.List || info.Kind == DataKind.Array;
                    var element = many ? info.ElementType : fieldType;
                    if (element != typeof(string) && !IsReferenceType(element))
                        throw new InvalidOperationException($"[MomoiProject] '{type.Name}.{field.Name}': [Chosen] 필드는 string, id가 있는 클래스, 그 리스트·배열이어야 합니다.");
                    found = new ChosenField
                    {
                        Name = field.Name, ElementType = element, FieldType = fieldType, Many = many, Ids = element == typeof(string),
                        StubType = element == typeof(string) ? null : StubTypeOf(element), Access = FieldAccess.Find(type, field.Name),
                        Make = many ? MakerOf(fieldType, element) : null,
                    };
                    break;
                }
                Chosen[type] = found;
                return found;
            }
        }

        // 추상 형식은 만들 수 없으므로 그 아래 구체 형식으로 id만 든 객체를 만든다(형식마다 한 번 — 게임이 켜질 때 PrepareAll).
        static Type StubTypeOf(Type type)
        {
            if (!type.IsAbstract) return type;
            foreach (var assembly in GameAssemblies.All())
                foreach (var candidate in GameAssemblies.TypesOf(assembly))
                    if (!candidate.IsAbstract && !candidate.ContainsGenericParameters && type.IsAssignableFrom(candidate)) return candidate;
            return type;
        }

        // 형식마다 한 번(게임이 켜질 때 PrepareAll) 어트리뷰트를 본다.
        static bool IsChosen(Type type, string name)
        {
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                var info = t.GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly);
                if (info != null) return info.IsDefined(typeof(ChosenAttribute), false);
            }
            return false;
        }

        static object StubOf(Type type, string id)
        {
            var stub = DataModel.Create(type);
            GameApi.SetId(stub, id);
            return stub;
        }

        static object Stub(object item)
        {
            var stub = DataModel.Create(item.GetType());
            GameApi.SetId(stub, GameApi.IdOf(item));
            return stub;
        }

        // 참조 필드의 (필드 이름, id) — 타임라인 글·전송용.
        public static IEnumerable<(string field, string id)> Ids(GameAction action)
        {
            if (action == null) yield break;
            foreach (var field in PlanOf(action.GetType()))
            {
                var value = field.Access?.Get(action);
                if (value == null) continue;
                if (!field.Many) yield return (field.Name, SafeId(value));
                else foreach (var item in (IEnumerable)value)
                    if (item != null) yield return (field.Name, SafeId(item));
            }
        }

        // 기본 타임라인 글: Block 이름 (참조 id들).
        public static string Describe(GameAction action)
        {
            var name = BlockNames.Of(action.GetType());
            var ids = Ids(action).Select(p => p.id).Where(id => !string.IsNullOrEmpty(id)).ToList();
            return ids.Count == 0 ? name : $"{name} ({string.Join(", ", ids)})";
        }

        public static bool IsReferenceType(Type type) =>
            type != null && type.IsClass && type != typeof(string) && !typeof(Delegate).IsAssignableFrom(type) && !typeof(GameAction).IsAssignableFrom(type)
            && DataModel.Of(type).Kind == DataKind.Object && GameApi.HasId(type);

        // 게임이 켜질 때: 불러온 모든 어셈블리의 행동 형식을 미리 정해 둔다(실행 중에 형식 정보를 읽지 않게).
        public static void PrepareAll()
        {
            foreach (var assembly in GameAssemblies.All())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                foreach (var type in types)
                    if (!type.IsAbstract && typeof(GameAction).IsAssignableFrom(type))
                    {
                        PlanOf(type);
                        try { ChosenOf(type); }
                        catch (InvalidOperationException) { }   // 잘못된 [Chosen] 필드는 그 행동으로 Ask할 때 알린다
                    }
            }
        }

        static Field[] PlanOf(Type type)
        {
            lock (Plans)
            {
                if (Plans.TryGetValue(type, out var known)) return known;
                var fields = new List<Field>();
                foreach (var field in DataModel.Of(type).Fields)
                {
                    var fieldType = field.Type;
                    if (IsReferenceType(fieldType))
                    {
                        fields.Add(new Field { Name = field.Name, ElementType = fieldType, Access = FieldAccess.Find(type, field.Name) });
                        continue;
                    }
                    var info = DataModel.Of(fieldType);
                    if ((info.Kind == DataKind.List || info.Kind == DataKind.Array || info.Kind == DataKind.Collection) && IsReferenceType(info.ElementType))
                        fields.Add(new Field { Name = field.Name, ElementType = info.ElementType, FieldType = fieldType, Many = true, Access = FieldAccess.Find(type, field.Name), Make = MakerOf(fieldType, info.ElementType) });
                }
                var plan = fields.ToArray();
                Plans[type] = plan;
                return plan;
            }
        }

        // 새 배열·목록을 만드는 함수: 배열·List<T>·HashSet<T>·Queue<T>·Stack<T>는 형식마다 한 번 일반화 도우미에 묶어 둔다(실행 중에는 그냥 부른다).
        // 그 밖의 형식(목록을 상속한 클래스 등)은 null — 부르는 쪽이 예전 길(DataModel)로 만든다.
        static Func<IReadOnlyList<object>, object> MakerOf(Type fieldType, Type element)
        {
            if (fieldType == null || element == null) return null;
            string name = null;
            if (fieldType.IsArray) name = fieldType.GetArrayRank() == 1 ? nameof(MakeArray) : null;
            else if (fieldType.IsGenericType)
            {
                var definition = fieldType.GetGenericTypeDefinition();
                name = definition == typeof(List<>) ? nameof(MakeList)
                     : definition == typeof(HashSet<>) ? nameof(MakeSet)
                     : definition == typeof(Queue<>) ? nameof(MakeQueue)
                     : definition == typeof(Stack<>) ? nameof(MakeStack) : null;
            }
            var method = name == null ? null : typeof(ActionReferences).GetMethod(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (method == null) return null;
            try { return (Func<IReadOnlyList<object>, object>)method.MakeGenericMethod(element).CreateDelegate(typeof(Func<IReadOnlyList<object>, object>)); }
            catch (Exception) { return null; }
        }

        // 리플렉션으로만 묶이므로 코드 줄이기가 지우지 않게 [Preserve].
        [Preserve]
        static object MakeArray<T>(IReadOnlyList<object> items)
        {
            var array = new T[items.Count];
            for (var i = 0; i < array.Length; i++) array[i] = (T)items[i];
            return array;
        }

        [Preserve]
        static object MakeList<T>(IReadOnlyList<object> items)
        {
            var list = new List<T>(items.Count);
            foreach (var item in items) list.Add((T)item);
            return list;
        }

        [Preserve]
        static object MakeSet<T>(IReadOnlyList<object> items)
        {
            var set = new HashSet<T>();
            foreach (var item in items) set.Add((T)item);
            return set;
        }

        [Preserve]
        static object MakeQueue<T>(IReadOnlyList<object> items)
        {
            var queue = new Queue<T>(items.Count);
            foreach (var item in items) queue.Enqueue((T)item);
            return queue;
        }

        // 스택은 위에서부터 나열되므로(꺼낸 차례) 거꾸로 쌓아야 같은 모양이 된다.
        [Preserve]
        static object MakeStack<T>(IReadOnlyList<object> items)
        {
            var stack = new Stack<T>(items.Count);
            for (var i = items.Count - 1; i >= 0; i--) stack.Push((T)items[i]);
            return stack;
        }

        static object Live(GameApi game, GameAction action, Field field, object item)
        {
            var id = GameApi.IdOf(item);
            if (string.IsNullOrEmpty(id))
                throw new InvalidOperationException($"[MomoiProject] 행동 '{action.GetType().Name}'의 '{field.Name}': id가 비어 있는 {item.GetType().Name}입니다. 데이터 안의 객체를 넣으세요(새 객체는 행동 안에서 만든다).");
            // 지금 데이터에 없으면 플레이어용 실패(변경 없음) — 보낸 사이에 사라진 것(실시간 · 멀티의 엇갈림)이지 코드 실수가 아니다.
            return game.Find(field.ElementType, id)
                ?? throw game.Fail(FailureKeys.TargetGone, "대상이 이미 없습니다.", ("what", field.ElementType.Name));
        }

        static string SafeId(object item)
        {
            try { return GameApi.IdOf(item); }
            catch (InvalidOperationException) { return null; }
        }
    }
}
