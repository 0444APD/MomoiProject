using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace GBible
{
    // 플레이어 데이터 클래스: 게임 중에 바뀌는 값을 담고 플레이어 데이터 안에 들어가는 클래스(유닛·인벤토리·아이템 사본 …).
    // 이 표시가 붙은 클래스의 필드에 값을 쓰면(=, +=, ++) 쓰는 순간 기록된다(Undo·Journal·조건 알림).
    // 컴파일 때 위빙(GBible.CodeGen)이 필드 쓰기를 기록 호출로 바꿔 넣는다. 게임 코드는 평소처럼 쓴다.
    // 조건: class(구조체 아님), 인자 없는 생성자, 리스트·딕셔너리 필드는 GameList<T>·GameDictionary<K,V>.
    // 클래스 탭이 만드는 데이터 클래스에는 자동으로 붙는다.
    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public sealed class PlayerDataAttribute : Attribute
    {
    }

    // 위빙 도구가 처리한 플레이어 데이터 클래스에 붙이는 표시(직접 쓰지 않는다). 없으면 쓰기를 못 잡으므로 저장소가 예전 방식(비교)을 쓴다.
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class PlayerDataWovenAttribute : Attribute
    {
    }

    // 위빙된 코드·시스템 컬렉션이 쓰기를 알리는 곳. 어느 플레이어 데이터 안의 객체인지 기억해 두고(부모 연결),
    // 그 저장소에 기록을 넘긴다. 저장소에 붙지 않은 객체(아직 넣지 않은 새 객체·템플릿 원본)의 쓰기는 무시한다.
    public static class PlayerDataTracker
    {
        internal sealed class Node
        {
            public PlayerDataStore Store;
            public string Key;
            // 부모 객체(플레이어 데이터 값 자체면 null)와 그 안의 필드 이름(리스트·딕셔너리 요소면 null).
            public object Parent;
            public string Member;
        }

        static readonly ConditionalWeakTable<object, Node> Nodes = new ConditionalWeakTable<object, Node>();
        [ThreadStatic] static int _silent;

        // 이 안의 쓰기는 기록하지 않는다(저장소가 직접 되돌리거나 불러올 때).
        public static IDisposable Silent()
        {
            _silent++;
            return new SilentScope();
        }

        sealed class SilentScope : IDisposable
        {
            bool _done;

            public void Dispose()
            {
                if (_done) return;
                _done = true;
                _silent--;
            }
        }

        static bool Find(object owner, out Node node)
        {
            node = null;
            return _silent == 0 && owner != null && Nodes.TryGetValue(owner, out node) && node.Store != null;
        }

        // ── 위빙된 필드 쓰기 ────────────────────────────────────
        // u.hp = 20 → (위빙) PlayerDataTracker.Write(u, "hp", u.hp, 20); u.hp = 20;

        public static void Write<T>(object owner, string field, T old, T value)
        {
            if (EqualityComparer<T>.Default.Equals(old, value)) return;
            if (!Find(owner, out var node)) return;
            node.Store.OnFieldWrite(node, owner, field, old, value);
        }

        // ── 시스템 컬렉션 ──────────────────────────────────────

        internal static void ListChanged(object list, DataWriteKind kind, int index, object old, object value)
        {
            if (!Find(list, out var node)) return;
            node.Store.OnCollectionWrite(node, list, kind, index, null, old, value);
        }

        internal static void DictionaryChanged(object dictionary, DataWriteKind kind, object key, object old, object value)
        {
            if (!Find(dictionary, out var node)) return;
            node.Store.OnCollectionWrite(node, dictionary, kind, -1, key, old, value);
        }

        internal static void CollectionCleared(object collection, IList items)
        {
            if (!Find(collection, out var node)) return;
            node.Store.OnCollectionWrite(node, collection, DataWriteKind.Clear, -1, null, items, null);
        }

        // ── 부모 연결 ──────────────────────────────────────────

        // 플레이어 데이터 객체·시스템 컬렉션인지(기록 대상).
        public static bool IsTracked(object value) =>
            value != null && (value is IGameCollection || value.GetType().IsDefined(typeof(PlayerDataAttribute), true));

        internal static bool TryGetNode(object value, out Node node) => Nodes.TryGetValue(value, out node);

        // value와 그 안쪽 전체를 저장소의 key 아래에 붙인다(이미 다른 곳에 붙어 있었으면 옮긴다).
        internal static void Attach(object value, PlayerDataStore store, string key, object parent, string member)
        {
            if (!IsTracked(value)) return;
            Nodes.AddOrUpdate(value, new Node { Store = store, Key = key, Parent = parent, Member = member });
            foreach (var (child, name) in Children(value)) Attach(child, store, key, value, name);
        }

        // value와 그 안쪽 전체를 뗀다(빠진 객체를 나중에 고쳐도 기록되지 않는다).
        internal static void Detach(object value)
        {
            if (!IsTracked(value) || !Nodes.TryGetValue(value, out _)) return;
            Nodes.Remove(value);
            foreach (var (child, _) in Children(value)) Detach(child);
        }

        static IEnumerable<(object child, string member)> Children(object value)
        {
            switch (value)
            {
                case IDictionary map:
                    foreach (DictionaryEntry pair in map)
                        if (IsTracked(pair.Value)) yield return (pair.Value, null);
                    yield break;
                case IGameCollection collection:
                    foreach (var item in collection.Items)
                        if (IsTracked(item)) yield return (item, null);
                    yield break;
            }
            foreach (var field in DataModel.Of(value.GetType()).Fields)
            {
                var child = field.Field.GetValue(value);
                if (IsTracked(child)) yield return (child, field.Name);
            }
        }
    }

    public enum DataWriteKind
    {
        Field,
        Insert,
        Remove,
        SetItem,
        Clear,
        // 값 전체를 바꿈(Set·불러오기). Old는 이전 모습의 사본.
        Replace,
    }

    // 시스템 컬렉션 공통(기록·되돌리기용).
    public interface IGameCollection
    {
        IEnumerable Items { get; }
        // 기록 없이 바꾸기(되돌리기용).
        void RawInsert(int index, object key, object value);
        void RawRemove(int index, object key);
        void RawSet(int index, object key, object value);
        void RawRestore(IList items);
    }
}
